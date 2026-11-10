using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Shared MEP tool helpers.</summary>
    public static class MepAim
    {
        public const float Range = 5f;

        public static bool Aim(out RaycastHit hit, float range = Range)
        {
            hit = default;
            return Game.Player != null && Game.Player.TryAim(out hit, range);
        }

        public static NetRun AimRun(out RaycastHit hit)
        {
            if (!Aim(out hit)) return null;
            return NetRun.FromCollider(hit.collider);
        }

        /// <summary>Port nearest the aim point (within radius) passing the filter.</summary>
        public static NetPort AimPort(float radius, Func<NetPort, bool> filter, out RaycastHit hit)
        {
            if (!Aim(out hit)) return null;
            return NetGraph.NearestPort(hit.point, radius, filter);
        }

        public static LineRenderer MakeLine(string name, Color c, float w = 0.012f)
        {
            var go = new GameObject(name) { layer = 2 };
            var lr = go.AddComponent<LineRenderer>();
            lr.startWidth = lr.endWidth = w;
            lr.useWorldSpace = true;
            lr.sharedMaterial = MaterialLibrary.Get(c);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.positionCount = 0;
            return lr;
        }

        public static void Progress(float t, string label)
        {
            var r = new Rect(Screen.width * 0.5f - 100, Screen.height * 0.5f + 30, 200, 14);
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.75f, 0.2f); GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            ToolUtil.CenterLabel(label, Color.white, 50f);
        }

        public static string SlopeLabel(float inPerFt)
        {
            if (float.IsInfinity(inPerFt)) return "vertical";
            if (Mathf.Abs(inPerFt) < 0.01f) return "level";
            return (inPerFt < 0 ? "-" : "") + Fittings.Fraction(Mathf.Abs(inPerFt)) + "\"/ft";
        }
    }

    /// <summary>Routing tool for one trade: click points, RMB to finish. Snaps to grid, to free ports (fittings, boxes,
    /// fixture stubs) and tees into existing runs. Drain runs get the selected slope; the slope gauge shows each leg.</summary>
    public class RouteTool : Tool
    {
        readonly string _id, _name; readonly Trade _trade; readonly Func<RunSpec, bool> _filter;
        public override string Id => _id;
        public override string Name => _name;
        public override int Slots => 1;
        public override string Hint => "LMB add point · RMB finish (RMB with 1 point = cancel) · Scroll material · Shift+Scroll height · Alt+Scroll slope";

        public RunSpec Spec { get; private set; }
        public float SlopeInPerFt = 0.25f;
        public float HeightOffset;
        static readonly float[] Slopes = { 0f, 0.125f, 0.25f, 0.5f };
        readonly List<Vector3> _pts = new List<Vector3>();
        NetPort _startPort; NetRun _teeRun; Vector3 _teePoint;
        LineRenderer _line;
        Vector3 _cursor; NetPort _cursorPort; NetRun _cursorRun;
        string _last = "";

        public RouteTool(string id, string name, Trade trade, Func<RunSpec, bool> filter = null)
        { _id = id; _name = name; _trade = trade; _filter = filter; }

        List<RunSpec> Specs() { var l = RunSpecs.ForTrade(_trade); if (_filter != null) l = l.FindAll(s => _filter(s)); return l; }

        public void SelectSpec(string id) { var s = RunSpecs.Get(id); if (s != null) Spec = s; }

        public override void OnEquip()
        {
            if (Spec == null) { var l = Specs(); if (l.Count > 0) Spec = l[0]; }
            if (_line == null) _line = MepAim.MakeLine("RoutePreview", new Color(1f, 0.85f, 0.1f));
            _line.gameObject.SetActive(true);
        }

        public override void OnUnequip() { if (_line != null) _line.gameObject.SetActive(false); }

        Vector3 SnapPoint(RaycastHit hit)
        {
            float stand = Spec != null ? Mathf.Max(Spec.Width, Spec.Height) * 0.5f : 0.01f;
            Vector3 p = hit.point + hit.normal * stand;
            if (!GInput.Ctrl) p = Game.Snap.SnapPosition(p);
            p.y += HeightOffset;
            return p;
        }

        public override void Tick()
        {
            if (Spec == null) return;
            float s = GInput.Scroll;
            if (s != 0)
            {
                if (GInput.Shift) HeightOffset += s * Units.In(1);
                else if (GInput.Alt) { int i = Array.IndexOf(Slopes, SlopeInPerFt); SlopeInPerFt = Slopes[(Mathf.Max(0, i) + (s > 0 ? 1 : Slopes.Length - 1)) % Slopes.Length]; }
                else { var l = Specs(); int i = l.IndexOf(Spec); Spec = l[(i + (s > 0 ? 1 : l.Count - 1)) % l.Count]; }
            }
            _cursorPort = null; _cursorRun = null;
            if (MepAim.Aim(out var hit))
            {
                _cursorPort = NetGraph.NearestPort(hit.point, 0.12f, p => p.Free && p.Accepts(Spec));
                if (_cursorPort != null) _cursor = _cursorPort.WorldPos;
                else
                {
                    _cursorRun = NetRun.FromCollider(hit.collider);
                    if (_cursorRun != null && _cursorRun.Spec.Kind == Spec.Kind && Mathf.Approximately(_cursorRun.Spec.NominalIn, Spec.NominalIn))
                    { _cursorRun.DistanceAlong(hit.point, out _); float d = _cursorRun.DistanceAlong(hit.point, out _); _cursor = _cursorRun.PointAt(d, out _); }
                    else { _cursorRun = null; _cursor = SnapPoint(hit); }
                }
            }
            if (_line != null)
            {
                var preview = new List<Vector3>(_pts);
                if (_pts.Count > 0) preview.Add(_cursor);
                if (Spec.IsDrain) preview = RunBuilder.ApplySlope(preview, SlopeInPerFt);
                _line.positionCount = preview.Count;
                _line.SetPositions(preview.ToArray());
            }
        }

        public override void Primary()
        {
            if (Spec == null) return;
            if (_pts.Count == 0)
            {
                _startPort = _cursorPort; _teeRun = _cursorRun; _teePoint = _cursor;
            }
            _pts.Add(_cursor);
            if (_pts.Count >= 2 && _cursorPort != null) Finish(_cursorPort); // landing on a port ends the run
        }

        public override void Secondary()
        {
            if (_pts.Count < 2) { Cancel(); return; }
            Finish(null);
        }

        void Cancel() { _pts.Clear(); _startPort = null; _teeRun = null; if (_line != null) _line.positionCount = 0; }

        void Finish(NetPort end)
        {
            var start = _startPort;
            if (start == null && _teeRun != null)
                start = RunBuilder.TeeInto(_teeRun, _teePoint, _pts[1] - _pts[0]);
            var res = RunBuilder.Route(Spec, _pts, start, end, Spec.IsDrain ? SlopeInPerFt : 0f);
            _last = res.Ok ? $"Ran {res.Length / Units.Foot:0.#} ft of {Spec.Name}, {res.Fittings.Count + Math.Max(0, res.Runs.Count - 1)} fittings" : "";
            if (res.Warnings.Count > 0) Game.Toast(res.Warnings[0]); else if (res.Ok) Game.Toast(_last);
            Cancel();
        }

        public override void DrawGUI()
        {
            if (Spec == null) { ToolUtil.CenterLabel("No materials for this trade", Color.red); return; }
            string avail = float.IsInfinity(MepStock.AvailableFeet(Spec)) ? "" : $" · {MepStock.AvailableFeet(Spec):0} ft on site";
            string txt = Spec.Name + avail;
            if (Spec.IsDrain) txt += " · slope " + MepAim.SlopeLabel(SlopeInPerFt);
            if (HeightOffset != 0) txt += " · +" + Units.Format(HeightOffset);
            if (_pts.Count > 0)
            {
                float len = 0; for (int i = 1; i < _pts.Count; i++) len += Vector3.Distance(_pts[i - 1], _pts[i]);
                len += Vector3.Distance(_pts[_pts.Count - 1], _cursor);
                txt += " · " + Units.Format(len);
            }
            if (_cursorPort != null) txt += " · snap: " + _cursorPort.Node.Label + " " + _cursorPort.Label;
            else if (_cursorRun != null) txt += " · tee into " + _cursorRun.Spec.Name;
            ToolUtil.CenterLabel(txt, new Color(1f, 0.85f, 0.1f));
            // slope gauge readout (torpedo level on the aimed drain run)
            var r = MepAim.AimRun(out var hit);
            if (r != null && r.Spec.IsDrain)
            {
                float s = r.DistanceAlong(hit.point, out _); int seg = SegmentAt(r, s);
                float sl = r.SlopeInPerFt(seg);
                bool ok = float.IsInfinity(sl) || sl >= DrainRules.MinSlope(r.Spec) - 0.01f;
                ToolUtil.CenterLabel("Slope gauge: " + MepAim.SlopeLabel(sl) + (ok ? "  OK" : "  TOO FLAT"), ok ? Color.green : Color.red, 70f);
            }
        }

        static int SegmentAt(NetRun r, float s)
        {
            for (int i = 1; i < r.Points.Count; i++) { float l = Vector3.Distance(r.Points[i - 1], r.Points[i]); if (s <= l) return i - 1; s -= l; }
            return r.Points.Count - 2;
        }
    }

    /// <summary>Code minimum drain slopes (IPC 704.1): ≤ 2-1/2" = 1/4"/ft, 3"–6" = 1/8"/ft. Condensate 1/8"/ft.</summary>
    public static class DrainRules
    {
        public static float MinSlope(RunSpec s)
        {
            if (s.Kind == NetKind.Vent) return 0f;                 // dry vents just need to grade back to the drain
            if (s.Kind == NetKind.Condensate) return 0.125f;
            return s.NominalIn <= 2.5f ? 0.25f : 0.125f;
        }
    }

    /// <summary>Cuts a run at the aim point (tubing cutter / PVC saw / tin snips). RMB removes the aimed run (material back to offcuts).</summary>
    public class CutTool : Tool
    {
        readonly string _id, _name, _hint;
        public override string Id => _id;
        public override string Name => _name;
        public override string Hint => _hint;
        public CutTool(string id, string name, string hint) { _id = id; _name = name; _hint = hint; }

        bool Can(NetRun r) => r != null && (r.Spec.CutTool == _id || string.IsNullOrEmpty(r.Spec.CutTool));

        public override void Primary()
        {
            var r = MepAim.AimRun(out var hit);
            if (r == null) return;
            if (!Can(r)) { Game.Toast($"Use the right cutter for {r.Spec.Name}"); return; }
            Vector3 p = GInput.Ctrl ? hit.point : Game.Snap.SnapPosition(hit.point);
            if (r.SplitAt(p) != null) { ToolUtil.Wear(this, 0.001f); Game.Toast("Cut " + r.Spec.Name); }
        }

        public override void Secondary()
        {
            var r = MepAim.AimRun(out _);
            if (r == null || !Can(r)) return;
            MepStock.Return(r.Spec, r.Length * 0.9f);
            UnityEngine.Object.Destroy(r.gameObject);
            Game.Toast("Removed " + r.Spec.Name + " (offcut kept)");
        }
    }

    /// <summary>Hold LMB on a joint to make it: PEX crimp ring, sweat (torch + solder), PVC primer + cement,
    /// foil tape on duct seams, pipe wrench on threaded joints.</summary>
    public class JoinTool : Tool
    {
        readonly string _id, _name, _verb, _consumable;
        readonly JoinMethod _method; readonly float _seconds;
        public override string Id => _id;
        public override string Name => _name;
        public override string Hint => $"Hold LMB on an un-made joint to {_verb}";
        float _t; NetPort _target;

        public JoinTool(string id, string name, JoinMethod method, float seconds, string verb, string consumableId = null)
        { _id = id; _name = name; _method = method; _seconds = seconds; _verb = verb; _consumable = consumableId; }

        NetPort Find() => MepAim.AimPort(0.25f, p => p.Join == _method && p.NeedsJoin, out _);

        public override void Primary() { _target = Find(); _t = 0; if (_target == null) Game.Toast("No un-" + _verb.Split(' ')[0] + "ed joint here"); }

        public override void PrimaryHeld()
        {
            if (_target == null) return;
            if (Find() != _target) { _target = null; return; }
            _t += Time.deltaTime;
            if (_t < _seconds) return;
            if (!string.IsNullOrEmpty(_consumable) && Catalog.Get(_consumable) != null && !MepStock.Free && !Game.Stock.TryTake(_consumable, 1))
            { Game.Toast("Out of " + Catalog.Get(_consumable).Name); _target = null; return; }
            MakeJoint(_target);
            ToolUtil.Wear(this, 0.0005f);
            Game.Toast("Joint " + (_method == JoinMethod.Solvent ? "primed + glued" : _method == JoinMethod.Sweat ? "sweated" : _method == JoinMethod.Crimp ? "crimped" : _method == JoinMethod.Tape ? "taped" : "made"));
            _target = null;
        }

        public static void MakeJoint(NetPort p)
        {
            p.Joined = true;
            if (p.Node != null) p.Node.RefreshJoints();
            NetGraph.Touch();
        }

        public override void DrawGUI()
        {
            if (_target != null && GInput.Mouse(0)) MepAim.Progress(_t / _seconds, _verb + "...");
            else
            {
                var p = Find();
                if (p != null) ToolUtil.CenterLabel($"{p.Node.Label}: joint not {_verb.Split(' ')[0]}ed", new Color(1f, 0.6f, 0.2f));
            }
        }
    }

    /// <summary>Cordless drill / right-angle drill with auger, spade and hole-saw bits. Hold LMB on a stud to bore through.</summary>
    public class DrillTool : Tool
    {
        readonly string _id, _name; readonly float _speed; readonly int _slots;
        public override string Id => _id;
        public override string Name => _name;
        public override int Slots => _slots;
        public override bool Cordless => true;
        public override string Hint => "Hold LMB on a stud/joist to bore · RMB change bit";

        public static readonly (string name, float dia)[] Bits =
        {
            ("3/4\" auger", 0.75f), ("7/8\" auger", 0.875f), ("1\" auger", 1f), ("1-1/8\" auger", 1.125f), ("1-1/4\" spade", 1.25f),
            ("1-1/2\" spade", 1.5f), ("2\" hole saw", 2f), ("2-1/8\" hole saw", 2.125f), ("2-9/16\" hole saw", 2.5625f), ("3-5/8\" hole saw", 3.625f),
        };
        public int Bit = 2;
        float _t; Piece _target;

        public DrillTool(string id, string name, float speed, int slots) { _id = id; _name = name; _speed = speed; _slots = slots; }

        public override void Secondary() { Bit = (Bit + 1) % Bits.Length; Game.Toast("Bit: " + Bits[Bit].name); }
        public override void Primary() { _target = ToolUtil.AimPiece(MepAim.Range, out _); _t = 0; }

        public override void PrimaryHeld()
        {
            if (_target == null || _target.Def == null || _target.Def.Category != PieceCategory.Lumber) return;
            if (Battery <= 0f) { Game.Toast("Drill battery dead"); return; }
            var p = ToolUtil.AimPiece(MepAim.Range, out var hit);
            if (p != _target) { _target = null; return; }
            _t += Time.deltaTime * _speed;
            float need = 0.4f + Bits[Bit].dia * 0.6f;
            if (_t < need) return;
            Vector3 c = GInput.Ctrl ? hit.point : Game.Snap.SnapPosition(hit.point);
            var h = StudHoles.Bore(p, c, -hit.normal, Units.In(Bits[Bit].dia), Bits[Bit].name);
            Battery = Mathf.Max(0f, Battery - 0.004f * Bits[Bit].dia);
            ToolUtil.Wear(this, 0.0004f);
            var sh = p.GetComponent<StudHoles>();
            float ratio = sh.Ratio(h);
            Game.Toast($"Bored {Bits[Bit].name} hole ({ratio * 100:0}% of stud depth){(ratio > 0.6f ? " - OVER-BORED" : "")}");
            _target = null;
        }

        public override void DrawGUI()
        {
            if (_target != null && GInput.Mouse(0)) MepAim.Progress(_t / (0.4f + Bits[Bit].dia * 0.6f), "Boring " + Bits[Bit].name);
            else ToolUtil.CenterLabel("Bit: " + Bits[Bit].name, Color.white);
        }
    }

    /// <summary>Registers the shared MEP tools.</summary>
    public static class MepToolRegistry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            ToolFactory.Register(new ToolShopItem { Id = "drill", Name = "Cordless Drill + bit kit", Price = 229, Description = "Auger, spade and hole-saw bits for boring studs (holes ≤ 60% of stud depth)." },
                () => new DrillTool("drill", "Cordless Drill", 1f, 1));
            ToolFactory.Register(new ToolShopItem { Id = "ra-drill", Name = "Right-Angle Drill", Price = 349, Description = "Fits between studs; bores big holes fast. 2 slots." },
                () => new DrillTool("ra-drill", "Right-Angle Drill", 2.2f, 2));
            ToolFactory.Register(new ToolShopItem { Id = "pipe-route", Name = "Pipe & fittings kit", Price = 0, Description = "Lay out PEX, copper and PVC/ABS: click points, fittings auto-inserted, drains sloped." },
                () => new RouteTool("pipe-route", "Pipe Layout", Trade.Plumbing));
            ToolFactory.Register(new ToolShopItem { Id = "fishtape", Name = "Fish Tape (pull cable)", Price = 39, Description = "Pull Romex from the panel through bored studs to boxes." },
                () => new RouteTool("fishtape", "Fish Tape / Cable Pull", Trade.Electrical));
            ToolFactory.Register(new ToolShopItem { Id = "duct-route", Name = "Duct layout + crimper", Price = 59, Description = "Hang trunk and branch duct, takeoffs, line set and condensate." },
                () => new RouteTool("duct-route", "Duct Layout", Trade.Hvac));
            ToolFactory.Register(new ToolShopItem { Id = "tubing-cutter", Name = "Tubing Cutter", Price = 25, Description = "Cuts PEX and copper square." },
                () => new CutTool("tubing-cutter", "Tubing Cutter", "LMB cut PEX/copper · RMB remove run"));
            ToolFactory.Register(new ToolShopItem { Id = "pvc-saw", Name = "PVC Saw", Price = 18, Description = "Cuts PVC/ABS DWV pipe." },
                () => new CutTool("pvc-saw", "PVC Saw", "LMB cut PVC/ABS · RMB remove run"));
            ToolFactory.Register(new ToolShopItem { Id = "tin-snips", Name = "Tin Snips (aviation)", Price = 22, Description = "Cuts duct and sheet metal." },
                () => new CutTool("tin-snips", "Tin Snips", "LMB cut duct · RMB remove duct"));
            ToolFactory.Register(new ToolShopItem { Id = "pex-crimper", Name = "PEX Crimp Tool", Price = 89, Description = "Copper crimp rings on brass PEX fittings." },
                () => new JoinTool("pex-crimper", "PEX Crimper", JoinMethod.Crimp, 0.8f, "crimp ring", "pex-crimp-rings"));
            ToolFactory.Register(new ToolShopItem { Id = "torch", Name = "Propane Torch + solder", Price = 49, Description = "Flux, heat and sweat copper joints." },
                () => new JoinTool("torch", "Propane Torch", JoinMethod.Sweat, 3.5f, "sweat (flux, heat, solder)", "solder"));
            ToolFactory.Register(new ToolShopItem { Id = "pvc-cement", Name = "PVC Primer + Cement", Price = 16, Description = "Purple primer then solvent cement on every PVC joint." },
                () => new JoinTool("pvc-cement", "PVC Primer + Cement", JoinMethod.Solvent, 1.6f, "prime and glue", "pvc-cement"));
            ToolFactory.Register(new ToolShopItem { Id = "foil-tape", Name = "Foil Tape (UL 181)", Price = 12, Description = "Seal duct joints and takeoffs." },
                () => new JoinTool("foil-tape", "Foil Tape", JoinMethod.Tape, 1.2f, "tape the seam", "foil-tape"));
            ToolFactory.Register(new ToolShopItem { Id = "pipe-wrench", Name = "Pipe Wrench 14\"", Price = 35, Description = "Tighten threaded fittings: hose bibs, water heater nipples, gas." },
                () => new JoinTool("pipe-wrench", "Pipe Wrench", JoinMethod.Thread, 1.0f, "thread and tighten"));
        }
    }
}
