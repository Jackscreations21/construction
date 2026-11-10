using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum LandscapeKind { Walkway, GravelDrive, Fence, Gate, Plant, Sod, RetainingBlock, PathLight, Mailbox }

    /// <summary>
    /// A landscaping job on the site, built in real stages with the right tool and material at each step, e.g. a paver walk:
    /// layout → excavate 7-3/8" (spade) → 4" crusher-run base (wheelbarrow) → 1" sand bed (rake) → lay pavers (hands) →
    /// edging (hands) → sweep polymeric sand (rake). Tools call <see cref="Work"/> when aimed at it.
    /// </summary>
    public class LandscapeFeature : MonoBehaviour
    {
        public static readonly List<LandscapeFeature> All = new List<LandscapeFeature>();

        public LandscapeKind Kind;
        public string ItemId = "";
        public Vector3 A, B;           // endpoints (runs/rects) or A = position (point features)
        public float Width;            // meters (walkway/drive)
        public float Yaw;              // point features
        public float BaseY;            // finished grade elevation
        public int Stage, Progress;
        public float CureUntil = -1;   // absolute game hours (Day*24 + Hours)
        public int WateredDay = -1, PlacedDay;

        public struct StageDef
        {
            public string Name, Tool, Material; public float Qty; public int Steps;
            public StageDef(string name, string tool, string material, float qty, int steps) { Name = name; Tool = tool; Material = material; Qty = qty; Steps = Mathf.Max(1, steps); }
        }

        // ------------------------------------------------------------------ geometry

        public float Length => Vector3.Distance(Flat(A), Flat(B));
        public Vector3 Dir => (Flat(B) - Flat(A)).sqrMagnitude > 1e-6f ? (Flat(B) - Flat(A)).normalized : Vector3.forward;
        public Vector3 Center => (A + B) * 0.5f;
        public float RunYaw => Mathf.Atan2(Dir.x, Dir.z) * Mathf.Rad2Deg;
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        public const float WalkDepth = 0.1873f;               // 7-3/8": 4" base + 1" sand + 2-3/8" paver
        public static readonly float BaseT = Units.In(4), SandT = Units.In(1), PaverT = Units.In(2.375f);
        public static readonly float DriveDepth = Units.In(6);
        public static readonly float FenceHeight = 6 * Units.Foot;
        public const float LoadYd = 0.222f;                   // 6 cu ft wheelbarrow load

        public static float HoursNow => GameClock.I != null ? GameClock.I.Day * 24f + GameClock.I.Hours : Time.time / 50f;
        static int Day => GameClock.I != null ? GameClock.I.Day : 1;

        public int Bays => Mathf.Max(1, Mathf.CeilToInt(Length / (8 * Units.Foot) - 0.01f));
        public int Posts => Kind == LandscapeKind.Gate ? 2 : Bays + 1;
        public Vector3 PostAt(int i) { var p = Vector3.Lerp(A, B, Kind == LandscapeKind.Gate ? i : i / (float)Bays); p.y = Grade.HeightAt(p.x, p.z); return p; }

        static int Ceil(float f) => Mathf.CeilToInt(f - 1e-4f);
        float AreaM2 => Length * Width;
        float Yd(float m3) => m3 / Grade.CubicYard;

        PlantSpec Plant => PlantSpec.For(ItemId);

        public List<StageDef> Stages()
        {
            var s = new List<StageDef>();
            switch (Kind)
            {
                case LandscapeKind.Walkway:
                {
                    float L = Length;
                    int perRow = Ceil(Width / Units.In(8)), rows = Ceil(L / Units.In(4));
                    float baseYd = Yd(AreaM2 * BaseT * 1.15f), sandYd = Yd(AreaM2 * SandT * 1.1f);
                    int baseLoads = Mathf.Max(1, Ceil(baseYd / LoadYd)), sandLoads = Mathf.Max(1, Ceil(sandYd / LoadYd));
                    int rowSteps = Mathf.Max(1, Ceil(rows / 3f));
                    s.Add(new StageDef("Excavate 7-3/8\"", "spade", null, 0, Mathf.Max(2, Ceil(L / 0.9f))));
                    s.Add(new StageDef("Spread & tamp 4\" crusher-run base", "wheelbarrow", "crusher-run", baseYd / baseLoads, baseLoads));
                    s.Add(new StageDef("Screed 1\" sand bed", "rake", "paver-sand", sandYd / sandLoads, sandLoads));
                    s.Add(new StageDef("Lay pavers (running bond)", "landhands", "paver-holland", perRow * 3, rowSteps));
                    s.Add(new StageDef("Spike edging restraint", "landhands", "paver-edging", 1, Ceil(2 * L / (8 * Units.Foot))));
                    s.Add(new StageDef("Sweep polymeric sand into joints", "rake", "poly-sand", Mathf.Max(1, Ceil(AreaM2 * 10.764f / 60f)), 1));
                    break;
                }
                case LandscapeKind.GravelDrive:
                {
                    float L = Length;
                    float baseYd = Yd(AreaM2 * Units.In(4)), topYd = Yd(AreaM2 * Units.In(2));
                    int bl = Mathf.Max(1, Ceil(baseYd / 0.5f)), tl = Mathf.Max(1, Ceil(topYd / 0.5f));
                    s.Add(new StageDef("Strip sod", "sodcutter", null, 0, Mathf.Max(1, Ceil(L / 1.5f))));
                    s.Add(new StageDef("Excavate 6\"", "shovel", null, 0, Mathf.Max(2, Ceil(L / 1.5f))));
                    s.Add(new StageDef("Spread 4\" crusher-run base (1/2 yd dumps)", "wheelbarrow", "crusher-run", baseYd / bl, bl));
                    s.Add(new StageDef("Top with 2\" #57 stone", "wheelbarrow", "gravel-57", topYd / tl, tl));
                    break;
                }
                case LandscapeKind.Fence:
                case LandscapeKind.Gate:
                {
                    int posts = Posts;
                    s.Add(new StageDef("Dig 10\" post holes, 24\" deep", "posthole", null, 0, posts * 2));
                    s.Add(new StageDef("Set 4x4 posts plumb", "landhands", "4x4x8", 1, posts));
                    s.Add(new StageDef("Pour post concrete (2 bags/post)", "landhands", "postmix-50", 2, posts));
                    if (Kind == LandscapeKind.Fence)
                    {
                        s.Add(new StageDef("Nail 2x4 rails (3 per bay)", "landhands", "pt2x4x8", 1, Bays * 3));
                        s.Add(new StageDef("Hang cedar pickets", "landhands", "picket-cedar", Ceil(Length / Bays / Units.In(5.5f)), Bays));
                    }
                    else s.Add(new StageDef("Hang gate + hardware", "landhands", "gate-kit", 1, 1));
                    break;
                }
                case LandscapeKind.Plant:
                    s.Add(new StageDef($"Dig hole {Units.Format(Plant.HoleR * 2)} wide", "shovel", null, 0, Plant.DigSteps));
                    s.Add(new StageDef("Set " + Plant.Short + " at grade", "landhands", ItemId, 1, 1));
                    s.Add(new StageDef("Backfill & firm soil", "shovel", null, 0, Mathf.Max(1, Plant.DigSteps / 2)));
                    s.Add(new StageDef("Water in", "hose", null, 0, 1));
                    break;
                case LandscapeKind.Sod:
                    s.Add(new StageDef("Water sod", "hose", null, 0, 1));
                    break;
            }
            return s;
        }

        public bool Done => Stage >= Stages().Count;
        public StageDef? Current { get { var s = Stages(); return Stage < s.Count ? s[Stage] : (StageDef?)null; } }

        public string Title
        {
            get
            {
                switch (Kind)
                {
                    case LandscapeKind.Walkway: return $"Paver walk {Units.Format(Width)} x {Units.Format(Length)}";
                    case LandscapeKind.GravelDrive: return $"Gravel drive {Units.Format(Width)} x {Units.Format(Length)}";
                    case LandscapeKind.Fence: return $"Privacy fence {Units.Format(Length)} ({Bays} bays)";
                    case LandscapeKind.Gate: return "Gate 4'";
                    case LandscapeKind.Plant: return Plant.Short;
                    case LandscapeKind.Sod: return "Sod roll";
                    case LandscapeKind.RetainingBlock: return "Retaining wall block";
                    case LandscapeKind.PathLight: return "Path light";
                    case LandscapeKind.Mailbox: return "Mailbox";
                }
                return Kind.ToString();
            }
        }

        public static string ToolName(string id)
        {
            switch (id)
            {
                case "spade": return "Spade"; case "shovel": return "Shovel"; case "rake": return "Rake"; case "hoe": return "Hoe";
                case "wheelbarrow": return "Wheelbarrow"; case "sodcutter": return "Sod cutter"; case "tiller": return "Tiller";
                case "posthole": return "Post-hole digger"; case "hose": return "Hose"; case "landhands": return "Landscaping hands";
            }
            return id;
        }

        public string Hint
        {
            get
            {
                var c = Current;
                if (!c.HasValue)
                {
                    if (Kind == LandscapeKind.Sod) return "Sod: watered - knits into lawn in a few days";
                    return Title + ": complete";
                }
                string mat = c.Value.Material != null ? $", {Fmt(c.Value.Qty)} {LandCatalog.Name(c.Value.Material)}/step" : "";
                string cure = CuringHours > 0 ? $"  (concrete curing {CuringHours:0.0} h)" : "";
                return $"{Title}: {c.Value.Name} [{ToolName(c.Value.Tool)}{mat}] {Progress}/{c.Value.Steps}{cure}";
            }
        }

        static string Fmt(float q) => Mathf.Abs(q - Mathf.Round(q)) < 0.01f ? Mathf.RoundToInt(q).ToString() : q.ToString("0.00");
        public float CuringHours => CureUntil > 0 ? Mathf.Max(0, CureUntil - HoursNow) : 0;

        // ------------------------------------------------------------------ work

        /// <summary>One step of the current stage with the given tool. False (with a reason) if it's the wrong tool / no material.</summary>
        public bool Work(string toolId, out string message)
        {
            var c = Current;
            if (!c.HasValue) { message = Title + " is complete."; return false; }
            var st = c.Value;
            if (st.Tool != toolId) { message = $"{Title}: next is \"{st.Name}\" - use the {ToolName(st.Tool)}."; return false; }
            if (CuringHours > 0 && !GameSettings.Relaxed) { message = $"Post concrete is still curing ({CuringHours:0.0} game hours)."; return false; }
            if (!string.IsNullOrEmpty(st.Material))
            {
                bool frac = Mathf.Abs(st.Qty - Mathf.Round(st.Qty)) > 0.001f;
                bool ok = frac ? Bulk.Take(st.Material, st.Qty) : Game.Stock.TryTake(st.Material, Mathf.RoundToInt(st.Qty));
                if (!ok) { message = $"Need {Fmt(st.Qty)} x {LandCatalog.Name(st.Material)} on site (order it from the Garden Center)."; return false; }
            }
            OnStep(st, Progress);
            Progress++;
            message = $"{Title}: {st.Name} {Progress}/{st.Steps}";
            if (Progress >= st.Steps)
            {
                OnStageComplete(Stage);
                Stage++; Progress = 0;
                var next = Current;
                message = next.HasValue ? $"{st.Name} done. Next: {next.Value.Name} ({ToolName(next.Value.Tool)})" : $"{Title} complete!";
            }
            Rebuild();
            Sfx.Play(SoundFor(toolId), transform.position + Vector3.up * 0.3f, 0.8f);
            return true;
        }

        static string SoundFor(string tool)
        {
            switch (tool)
            {
                case "shovel": case "spade": case "posthole": case "rake": case "hoe": return "shovel";
                case "wheelbarrow": return "gravel";
                case "hose": return "water";
                case "sodcutter": case "tiller": return "engine";
                default: return "thud";
            }
        }

        /// <summary>Completes every remaining stage, taking materials if available (tests, sandbox "finish job").</summary>
        public int CompleteAll(Func<string, string> toolFor = null)
        {
            int steps = 0;
            for (int guard = 0; guard < 2000 && !Done; guard++)
            {
                var st = Current.Value;
                if (CuringHours > 0) CureUntil = HoursNow;
                if (!Work(st.Tool, out var msg)) { Debug.LogWarning("[Landscape] " + msg); break; }
                steps++;
            }
            return steps;
        }

        void OnStep(StageDef st, int i)
        {
            var f = Grade.Field;
            switch (Kind)
            {
                case LandscapeKind.Walkway when Stage == 0:
                case LandscapeKind.GravelDrive when Stage == 1:
                {
                    if (f == null) Grade.Ensure(Center);
                    float depth = Kind == LandscapeKind.Walkway ? WalkDepth : DriveDepth;
                    float t0 = i / (float)st.Steps, t1 = (i + 1) / (float)st.Steps;
                    Vector3 a = Vector3.Lerp(A, B, t0), b = Vector3.Lerp(A, B, t1);
                    float vol = LowerRun(a, b, Width / 2, -depth, true);
                    Grade.DirtOnHand += vol / Grade.CubicYard;
                    break;
                }
                case LandscapeKind.GravelDrive when Stage == 0:
                {
                    if (f == null) Grade.Ensure(Center);
                    float t0 = i / (float)st.Steps, t1 = (i + 1) / (float)st.Steps;
                    Vector3 a = Vector3.Lerp(A, B, t0), b = Vector3.Lerp(A, B, t1);
                    Grade.Field?.PaintRect((a + b) / 2, new Vector2(Width / 2 + 0.15f, Vector3.Distance(a, b) / 2 + 0.05f), RunYaw, LandCover.Dirt);
                    break;
                }
            }
        }

        void OnStageComplete(int stage)
        {
            switch (Kind)
            {
                case LandscapeKind.Walkway when stage == 4:
                    BackfillShoulders(Width / 2, 0.8f); // topsoil against the edging so the lawn meets the paver edge
                    break;
                case LandscapeKind.GravelDrive when stage == 3:

                    LowerRun(A, B, Width / 2, 0f, false);
                    Grade.Field?.PaintRect(Center, new Vector2(Width / 2, Length / 2), RunYaw, LandCover.Gravel);
                    break;
                case LandscapeKind.Fence when stage == 2:
                case LandscapeKind.Gate when stage == 2:
                    CureUntil = HoursNow + (GameSettings.Relaxed ? 0f : 4f); // fast-setting post mix: 4 h before loading
                    break;
                case LandscapeKind.Plant when stage == 3:
                    WateredDay = Day;
                    if (Plant.Tree) { Grade.Ensure(A); Grade.Field?.Paint(A, 0.75f, LandCover.Mulch); }
                    break;
                case LandscapeKind.Sod when stage == 0:
                    WateredDay = Day;
                    break;
            }
        }

        /// <summary>Finished-surface height of this run at a world point (walks/drives follow the grade from A to B).</summary>
        public float SurfaceAt(Vector3 p)
        {
            Vector3 d = Flat(B) - Flat(A); float len2 = Mathf.Max(1e-6f, d.sqrMagnitude);
            float t = Mathf.Clamp01(Vector3.Dot(Flat(p) - Flat(A), d) / len2);
            return Mathf.Lerp(A.y, B.y, t);
        }

        /// <summary>True if the point lies on this walk/drive's footprint.</summary>
        public bool Covers(Vector3 p)
        {
            if (Kind != LandscapeKind.Walkway && Kind != LandscapeKind.GravelDrive) return false;
            var l = Quaternion.Inverse(Quaternion.Euler(0, RunYaw, 0)) * (Flat(p) - Flat(Center));
            return Mathf.Abs(l.x) <= Width / 2 && Mathf.Abs(l.z) <= Length / 2;
        }

        /// <summary>Raises/lowers the ground beside the run to just under its finished surface (1/2" lip), blending back to the
        /// existing grade over `shoulder` meters, so a walk on uneven ground doesn't show its edge above the lawn.</summary>
        void BackfillShoulders(float halfW, float shoulder)
        {
            var f = Grade.Field; if (f == null) return;
            Vector3 c = Flat(Center); float halfL = Length / 2;
            var inv = Quaternion.Inverse(Quaternion.Euler(0, RunYaw, 0));
            float r = Mathf.Sqrt((halfW + shoulder) * (halfW + shoulder) + (halfL + shoulder) * (halfL + shoulder)) + Grade.Cell;
            int x0 = Mathf.FloorToInt((c.x - r - f.X0) / Grade.Cell), x1 = Mathf.CeilToInt((c.x + r - f.X0) / Grade.Cell);
            int z0 = Mathf.FloorToInt((c.z - r - f.Z0) / Grade.Cell), z1 = Mathf.CeilToInt((c.z + r - f.Z0) / Grade.Cell);
            for (int z = Mathf.Max(1, z0); z <= Mathf.Min(f.Cells - 1, z1); z++)
                for (int x = Mathf.Max(1, x0); x <= Mathf.Min(f.Cells - 1, x1); x++)
                {
                    var w = new Vector3(f.X0 + x * Grade.Cell, 0, f.Z0 + z * Grade.Cell);
                    if (Grade.InPad(w.x, w.z)) continue;
                    var l = inv * (w - c);
                    float ox = Mathf.Abs(l.x) - halfW, oz = Mathf.Abs(l.z) - halfL;
                    if (ox <= Grade.Cell * 0.5f && oz <= Grade.Cell * 0.5f) continue;   // under the pavers (excavated, hidden)
                    float out_ = Mathf.Max(ox, oz);
                    if (out_ > shoulder) continue;
                    float target = SurfaceAt(w) - Units.In(0.5f);
                    int k = z * f.V + x;
                    f.H[k] = Mathf.Lerp(target, f.H[k], Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(out_ / shoulder)));
                }
            f.MarkAllDirty();
        }

        /// <summary>Sets field vertices on the run segment a→b (half width) to the run surface + offset (lowerOnly: only if higher). Returns m³ removed.</summary>
        float LowerRun(Vector3 a, Vector3 b, float halfW, float offset, bool lowerOnly)
        {
            var f = Grade.Field; if (f == null) return 0;
            float vol = 0, area = Grade.Cell * Grade.Cell;
            Vector3 c = (Flat(a) + Flat(b)) / 2; float halfL = Vector3.Distance(Flat(a), Flat(b)) / 2 + 0.01f;
            var inv = Quaternion.Inverse(Quaternion.Euler(0, RunYaw, 0));
            float r = Mathf.Sqrt(halfW * halfW + halfL * halfL) + Grade.Cell;
            int x0 = Mathf.FloorToInt((c.x - r - f.X0) / Grade.Cell), x1 = Mathf.CeilToInt((c.x + r - f.X0) / Grade.Cell);
            int z0 = Mathf.FloorToInt((c.z - r - f.Z0) / Grade.Cell), z1 = Mathf.CeilToInt((c.z + r - f.Z0) / Grade.Cell);
            for (int z = Mathf.Max(1, z0); z <= Mathf.Min(f.Cells - 1, z1); z++)
                for (int x = Mathf.Max(1, x0); x <= Mathf.Min(f.Cells - 1, x1); x++)
                {
                    var w = new Vector3(f.X0 + x * Grade.Cell, 0, f.Z0 + z * Grade.Cell);
                    if (Grade.InPad(w.x, w.z)) continue; // never cut the building pad (SiteGround)
                    var l = inv * (w - c);
                    // include the vertex ring just outside so the excavation reads; it is hidden under the finished surface
                    if (Mathf.Abs(l.x) > halfW + Grade.Cell * 0.5f || Mathf.Abs(l.z) > halfL + Grade.Cell * 0.5f) continue;
                    float target = SurfaceAt(w) + offset;
                    int k = z * f.V + x;
                    float o = f.H[k];
                    if (lowerOnly && o <= target) continue;
                    f.H[k] = target; vol += (o - target) * area;
                }
            f.MarkAllDirty();
            return vol;
        }

        // ------------------------------------------------------------------ lifecycle

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static LandscapeFeature Create(LandscapeKind kind, string item, Vector3 a, Vector3 b, float width, float yaw)
        {
            var go = new GameObject("Landscape_" + kind);
            var f = go.AddComponent<LandscapeFeature>();
            f.Kind = kind; f.ItemId = item ?? ""; f.A = a; f.B = b; f.Width = width; f.Yaw = yaw;
            f.BaseY = (Grade.HeightAt(a.x, a.z) + Grade.HeightAt(b.x, b.z)) * 0.5f;
            f.PlacedDay = Day;
            if (kind == LandscapeKind.Walkway || kind == LandscapeKind.GravelDrive || kind == LandscapeKind.Fence || kind == LandscapeKind.Gate || kind == LandscapeKind.Plant)
                Grade.Ensure(f.Center);
            f.Rebuild();
            return f;
        }

        GameObject _visual;
        Light _light;

        public void Rebuild()
        {
            if (_visual != null) Destroy(_visual);
            _visual = new GameObject("Visual");
            _visual.transform.SetParent(transform, false);
            bool slab = Kind == LandscapeKind.Walkway || Kind == LandscapeKind.GravelDrive;
            transform.position = slab ? Center : Kind <= LandscapeKind.Gate ? new Vector3(Center.x, BaseY, Center.z) : A;
            transform.rotation = slab ? Quaternion.LookRotation(B - A) : Kind <= LandscapeKind.Gate ? Quaternion.Euler(0, RunYaw, 0) : Quaternion.Euler(0, Yaw, 0);
            var col = GetComponent<BoxCollider>(); if (col == null) col = gameObject.AddComponent<BoxCollider>();
            LandFeatureVisuals.Build(this, _visual.transform, col);
            if (Kind == LandscapeKind.PathLight && _light == null)
            {
                var lg = new GameObject("Glow"); lg.transform.SetParent(transform, false);
                lg.transform.localPosition = new Vector3(0, Units.In(18.5f), 0);
                _light = lg.AddComponent<Light>(); _light.type = LightType.Point; _light.range = 2.6f; _light.intensity = 1.4f;
                _light.color = new Color(1f, 0.82f, 0.58f); _light.shadows = LightShadows.None;
            }
        }

        public void SetLight(bool on) { if (_light != null) _light.enabled = on; }

        // ------------------------------------------------------------------ save

        [Serializable]
        public class Data
        {
            public int kind; public string item; public Vector3 a, b; public float width, yaw, baseY, cure; public int stage, progress, watered, placed;
        }

        public Data ToData() => new Data { kind = (int)Kind, item = ItemId, a = A, b = B, width = Width, yaw = Yaw, baseY = BaseY, cure = CureUntil, stage = Stage, progress = Progress, watered = WateredDay, placed = PlacedDay };

        public static LandscapeFeature FromData(Data d)
        {
            var go = new GameObject("Landscape_" + (LandscapeKind)d.kind);
            var f = go.AddComponent<LandscapeFeature>();
            f.Kind = (LandscapeKind)d.kind; f.ItemId = d.item ?? ""; f.A = d.a; f.B = d.b; f.Width = d.width; f.Yaw = d.yaw; f.BaseY = d.baseY;
            f.CureUntil = d.cure; f.Stage = d.stage; f.Progress = d.progress; f.WateredDay = d.watered; f.PlacedDay = d.placed;
            f.Rebuild();
            return f;
        }
    }

    /// <summary>Plant sizes/specs by catalog id.</summary>
    public struct PlantSpec
    {
        public string Short; public bool Tree, Conifer; public float HoleR, BallR, Height; public int DigSteps;
        public static PlantSpec For(string id)
        {
            switch (id)
            {
                case "tree-maple": return new PlantSpec { Short = "Red maple", Tree = true, HoleR = 0.6f, BallR = 0.3f, Height = 4f, DigSteps = 4 };
                case "tree-spruce": return new PlantSpec { Short = "Blue spruce", Tree = true, Conifer = true, HoleR = 0.55f, BallR = 0.28f, Height = 1.85f, DigSteps = 4 };
                case "shrub-boxwood": return new PlantSpec { Short = "Boxwood", HoleR = 0.3f, BallR = 0.15f, Height = 0.6f, DigSteps = 2 };
                case "shrub-hydrangea": return new PlantSpec { Short = "Hydrangea", HoleR = 0.3f, BallR = 0.15f, Height = 0.75f, DigSteps = 2 };
                default: return new PlantSpec { Short = "Hosta", HoleR = 0.2f, BallR = 0.1f, Height = 0.35f, DigSteps = 1 };
            }
        }
        public static readonly string[] Ids = { "tree-maple", "tree-spruce", "shrub-boxwood", "shrub-hydrangea", "perennial-hosta" };
    }
}
