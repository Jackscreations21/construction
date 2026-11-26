using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum RepairType { SinkLeak, DeadOutlet, DrywallHole, StickingDoor, DeckBoard, FencePicket, Shingles, Caulk, Gutter, Sidewalk }

    [Serializable] public class UsedMat { public string id; public int n; }

    /// <summary>Everything that persists about one defect (lives in Job.Payload and in the "repairs" save extension).</summary>
    [Serializable]
    public class DefectState
    {
        public string jobId;
        public int type, spot, cause;
        public bool init, wrongRepair;
        public float quality = 1f, mess, waitUntil;      // waitUntil: absolute game hours when the last wet step is dry
        public string reading = "";                       // result of the last test
        public List<string> flags = new List<string>(), done = new List<string>();
        public List<UsedMat> used = new List<UsedMat>();
    }

    /// <summary>Who is doing the work and which checks to skip (tests, worker NPCs).</summary>
    public class RepairContext
    {
        public bool IgnoreTool, FreeMaterials, IgnoreAccess, SkipWaits;
        public static RepairContext Player => new RepairContext();
        /// <summary>No tool, material, ladder or drying checks.</summary>
        public static RepairContext Bypass => new RepairContext { IgnoreTool = true, FreeMaterials = true, IgnoreAccess = true, SkipWaits = true };
    }

    public class RepairAction
    {
        public string Id, Label, Tool, Station, Sound = "click";
        public float Seconds = 2f, WaitMinutes;
        public bool NeedsDry, Diagnostic;
        public readonly List<(string id, int n)> Mats = new List<(string, int)>();
        public Func<bool> When;
        public Func<string> Run;                           // does the work, returns the result text
        public RepairAction Uses(string id, int n = 1) { if (n > 0) Mats.Add((id, n)); return this; }
        public RepairAction Dry() { NeedsDry = true; return this; }
        public RepairAction Wait(float minutes) { WaitMinutes = minutes; return this; }
        public RepairAction At(string station) { Station = station; return this; }
        public RepairAction Snd(string s) { Sound = s; return this; }
        public RepairAction Diag() { Diagnostic = true; return this; }
        public string ToolName => RepairCatalog.ToolName(Tool);
    }

    /// <summary>Marks a secondary work location of a defect (breaker panel, upstream GFCI).</summary>
    public class RepairStation : MonoBehaviour { public string Name; }

    /// <summary>Small mesh helper for defect models (defect-local metres, real sizes).</summary>
    public class RM
    {
        public readonly LandModel L = new LandModel();
        public int Mat(SurfaceKind k) => L.M(k);
        public int Mat(SurfaceKind k, Color tint) => L.M(k, tint);
        public int Mat(Color c) => L.M(c);
        public int Mat(Material m) => L.M(m);
        public Matrix4x4 Frame { set => L.B.Transform = value; }
        public void Box(Vector3 c, Vector3 size, int m) => L.B.AddBox(c, size, m);
        public void Box(Vector3 c, Vector3 size, Quaternion r, int m, Vector3 uAxis = default) => L.B.AddBox(c, size, r, m, uAxis);
        public void Cyl(Vector3 c, float r, float h, int m, int seg = 12) => L.B.AddCylinder(c, r, h, seg, m);
        public void Cyl(Vector3 c, Quaternion rot, float r, float h, int m, int seg = 12) => L.B.AddCylinder(c, rot, r, h, seg, m);
        public void Tube(Vector3 a, Vector3 b, float r, int m, int seg = 10)
        {
            Vector3 d = b - a; if (d.sqrMagnitude < 1e-8f) return;
            L.B.AddCylinder((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized), r, d.magnitude, seg, m);
        }
        public void Path(IList<Vector3> pts, float r, int m) { for (int i = 0; i + 1 < pts.Count; i++) Tube(pts[i], pts[i + 1], r, m, 8); }
        public void Disc(Vector3 c, float r, int m, int seg = 14) => L.B.AddDisc(c, Quaternion.identity, r, seg, m, true);
        /// <summary>Flat disc facing +Z (wall decals).</summary>
        public void DiscZ(Vector3 c, float r, int m, int seg = 14) => L.B.AddDisc(c, Quaternion.Euler(90, 0, 0), r, seg, m, true);
        public void Poly(IList<Vector3> pts, int m, Vector3 normal) => L.B.AddPolygon(pts, m, normal);
    }

    /// <summary>
    /// One thing wrong at a customer's house. The customer reports only <see cref="Symptom"/>; the hidden <see cref="Cause"/> is found by
    /// testing. Work is a set of actions (<see cref="AvailableActions"/>) performed with <see cref="Perform(string, RepairContext)"/> — by the
    /// player through <see cref="RepairInteraction"/>, by tests, later by worker NPCs. The model is rebuilt from state after every action.
    /// </summary>
    public abstract class RepairDefect : MonoBehaviour
    {
        public static readonly List<RepairDefect> All = new List<RepairDefect>();

        public DefectState S;
        public Job Job;
        public TownHouse House;
        public string LastMessage = "";
        public HouseSpot Spot => (HouseSpot)S.spot;
        public RepairType Type => (RepairType)S.type;
        public int Cause => S.cause;

        readonly List<RepairAction> _actions = new List<RepairAction>();
        GameObject _model;
        public readonly List<(Collider col, string station)> Colliders = new List<(Collider, string)>();

        /// <summary>Short name for job titles and invoices.</summary>
        public abstract string Title { get; }
        /// <summary>What the customer says.</summary>
        public abstract string Symptom { get; }
        /// <summary>What you can see right now.</summary>
        public abstract string Observation { get; }
        /// <summary>Physically fixed (independent of how well).</summary>
        public abstract bool IsFixed { get; }
        /// <summary>Flat-rate book labour hours for the repair that was actually needed.</summary>
        public virtual float BookHours => 1f;
        public virtual bool NeedsLadder => false;
        /// <summary>Quality lost for prep/finish steps that were skipped (evaluated when billed).</summary>
        protected virtual float SkipPenalty => 0f;
        public float Quality => Mathf.Clamp01(S.quality - SkipPenalty);
        public float Mess => Mathf.Clamp01(S.mess);
        public bool WrongRepair => S.wrongRepair;
        public string Reading => S.reading;

        protected virtual void FirstInit() { }
        protected abstract void DefineActions();
        protected abstract void BuildModel(RM m);
        protected virtual Bounds ColliderBox => new Bounds(Vector3.zero, Vector3.one * 0.5f);
        protected virtual void StateChanged() { }

        public static float Now => GameClock.I != null ? (GameClock.I.Day - 1) * 24f + GameClock.I.Hours : Time.time / 50f;

        // ------------------------------------------------------------------ creation

        public static RepairDefect AddFor(GameObject go, RepairType type)
        {
            switch (type)
            {
                case RepairType.SinkLeak: return go.AddComponent<SinkLeakDefect>();
                case RepairType.DeadOutlet: return go.AddComponent<DeadOutletDefect>();
                case RepairType.DrywallHole: return go.AddComponent<DrywallHoleDefect>();
                case RepairType.StickingDoor: return go.AddComponent<StickingDoorDefect>();
                case RepairType.DeckBoard: return go.AddComponent<DeckBoardDefect>();
                case RepairType.FencePicket: return go.AddComponent<FencePicketDefect>();
                case RepairType.Shingles: return go.AddComponent<ShinglesDefect>();
                case RepairType.Caulk: return go.AddComponent<CaulkDefect>();
                case RepairType.Gutter: return go.AddComponent<GutterDefect>();
                default: return go.AddComponent<SidewalkDefect>();
            }
        }

        /// <summary>Takes the defect out of the world now and puts the house back (healthy fixture shown).</summary>
        public void Remove()
        {
            All.Remove(this);
            if (House != null) Restore();
            House = null;
            gameObject.SetActive(false);
            UnityEngine.Object.Destroy(gameObject);
        }

        public static RepairDefect Create(DefectState s, Job job, TownHouse house)
        {
            var pose = house.Anchor((HouseSpot)s.spot);
            var go = new GameObject("Repair " + (RepairType)s.type + " @ " + (HouseSpot)s.spot);
            go.transform.SetParent(house.transform, true);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            var d = AddFor(go, (RepairType)s.type);
            d.S = s; d.Job = job; d.House = house;
            if (!s.init) { s.init = true; s.quality = 1f; d.FirstInit(); }
            d.DefineActions();
            d.AddCollider(go, d.ColliderBox, null);
            All.Add(d);
            d.Rebuild();
            return d;
        }

        void AddCollider(GameObject go, Bounds b, string station)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.center = b.center; bc.size = b.size; bc.isTrigger = true;     // triggers: aimed at with Collider.Raycast, never block the player
            Colliders.Add((bc, station));
        }

        /// <summary>A second place to work on this defect, at another anchor of the same house.</summary>
        protected Transform AddStation(string name, HouseSpot spot, Bounds b)
        {
            var pose = House.Anchor(spot);
            var go = new GameObject("Station " + name);
            go.transform.SetParent(transform, true);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            go.AddComponent<RepairStation>().Name = name;
            AddCollider(go, b, name);
            return go.transform;
        }

        /// <summary>Matrix that lets BuildModel draw in a station's frame.</summary>
        protected Matrix4x4 FrameOf(Transform station) => transform.worldToLocalMatrix * station.localToWorldMatrix;

        void OnDestroy()
        {
            All.Remove(this);
            if (House != null) Restore();
        }

        /// <summary>Put the house back the way it was (show the healthy fixture).</summary>
        protected virtual void Restore() { House.SetFixtureVisible(Spot, true); }

        public void Rebuild()
        {
            if (_model != null)
            {
                _model.SetActive(false);
                var mf = _model.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) UnityEngine.Object.Destroy(mf.sharedMesh);
                UnityEngine.Object.Destroy(_model);
            }
            var m = new RM();
            BuildModel(m);
            _model = m.L.Make(transform, "Model");
            StateChanged();
        }

        // ------------------------------------------------------------------ state helpers

        protected bool Has(string flag) => S.flags.Contains(flag);
        protected void Set(string flag) { if (!S.flags.Contains(flag)) S.flags.Add(flag); }
        protected void Clear(string flag) { S.flags.Remove(flag); }
        public bool Did(string actionId) => S.done.Contains(actionId);
        protected void Lose(float q) { S.quality = Mathf.Max(0f, S.quality - q); }
        protected void Dirty(float m) { S.mess = Mathf.Clamp01(S.mess + m); }
        public bool Drying => !GameSettings.Relaxed && Now < S.waitUntil;
        public float DryMinutesLeft => Mathf.Max(0f, (S.waitUntil - Now) * 60f);

        protected RepairAction Act(string id, string label, string tool, float seconds, Func<bool> when, Func<string> run)
        {
            var a = new RepairAction { Id = id, Label = label, Tool = tool, Seconds = seconds, When = when, Run = run };
            _actions.Add(a);
            return a;
        }

        // ------------------------------------------------------------------ public work API

        public IEnumerable<RepairAction> AllActions => _actions;

        public List<RepairAction> AvailableActions
        {
            get
            {
                var l = new List<RepairAction>();
                foreach (var a in _actions) if (a.When == null || a.When()) l.Add(a);
                return l;
            }
        }

        /// <summary>Actions that can be done at a station (null = the defect itself). Actions without a station are offered everywhere.</summary>
        public List<RepairAction> ActionsAt(string station)
        {
            var l = AvailableActions;
            l.RemoveAll(a => a.Station != station && !(a.Station == null && station == null));
            return l;
        }

        public bool HasAccess()
        {
            if (!NeedsLadder) return true;
            var p = Game.Player;
            if (p != null && p.transform.position.y > transform.position.y - 1.9f) return true;        // on the ladder or the roof
            var lad = TownLadder.Placed;
            if (lad != null)
            {
                Vector3 d = lad.transform.position - transform.position; d.y = 0f;
                if (d.magnitude < 6f) return true;
            }
            return false;
        }

        /// <summary>Could this action start now? (tool in hand, ladder, drying, parts in the van)</summary>
        public bool CanStart(RepairAction a, RepairContext ctx, out string why)
        {
            why = null; ctx = ctx ?? RepairContext.Player;
            if (a == null || (a.When != null && !a.When())) { why = "Not possible right now"; return false; }
            if (!ctx.IgnoreAccess && !HasAccess()) { why = "Stand the extension ladder at the eave first (it's on the van's rack)"; return false; }
            string hand = Game.Belt != null && Game.Belt.Active != null ? Game.Belt.Active.Id : null;
            if (!ctx.IgnoreTool && !RepairCatalog.ToolMatches(a.Tool, hand)) { why = "Needs: " + a.ToolName; return false; }
            if (a.NeedsDry && !ctx.SkipWaits && Drying) { why = $"Still wet — about {Mathf.CeilToInt(DryMinutesLeft)} min"; return false; }
            if (!ctx.FreeMaterials && GameSettings.IsCareer && a.Mats.Count > 0)
            {
                if (!VanCargo.InReach(transform.position)) { why = "It's in the van — park closer"; return false; }
                foreach (var (id, n) in a.Mats)
                    if (VanCargo.Count(id) < n) { why = $"Need {n} x {RepairCatalog.MatName(id)} (van has {VanCargo.Count(id)}) — Hardware Store"; return false; }
            }
            return true;
        }

        public bool Perform(string actionId, RepairContext ctx = null) => Perform(actionId, ctx, out _);

        /// <summary>Does one action (instantly; hold time is the caller's business). False + reason when it can't be done.</summary>
        public bool Perform(string actionId, RepairContext ctx, out string msg)
        {
            ctx = ctx ?? RepairContext.Player;
            var a = _actions.Find(x => x.Id == actionId);
            if (!CanStart(a, ctx, out msg)) { LastMessage = msg; return false; }
            foreach (var (id, n) in a.Mats)
            {
                if (!ctx.FreeMaterials && GameSettings.IsCareer) VanCargo.TryTake(id, n);
                var u = S.used.Find(x => x.id == id);
                if (u == null) S.used.Add(new UsedMat { id = id, n = n }); else u.n += n;
            }
            bool was = IsFixed;
            msg = a.Run != null ? a.Run() : null;
            if (string.IsNullOrEmpty(msg)) msg = a.Label + " — done";
            if (a.Diagnostic) S.reading = msg;
            if (!S.done.Contains(a.Id)) S.done.Add(a.Id);
            if (a.WaitMinutes > 0f) S.waitUntil = ctx.SkipWaits ? 0f : Now + a.WaitMinutes / 60f;
            LastMessage = msg;
            Rebuild();
            RepairJobs.DefectChanged(this, !was && IsFixed);
            return true;
        }

        /// <summary>Cost of the parts used so far (our cost, before markup).</summary>
        public float MaterialCost { get { float c = 0f; foreach (var u in S.used) c += RepairCatalog.UnitCost(u.id) * u.n; return c; } }

        /// <summary>Material of the house surface behind the anchor (to colour-match patches), or null.</summary>
        protected Material SurfaceBehind()
        {
            var ray = new Ray(transform.position + transform.forward * 0.3f, -transform.forward);
            if (Physics.Raycast(ray, out var hit, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var mr = hit.collider.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterials.Length == 1) return mr.sharedMaterial;
            }
            return null;
        }
    }
}
