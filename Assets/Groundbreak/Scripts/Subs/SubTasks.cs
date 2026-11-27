using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    // Crew tasks for the trades the Crew feature has no task for. Same contract as Scripts/Crew/CrewTask.cs: every NextUnit
    // reads the live world and hands out one unit that calls the trade's own public API (what the player's tool does).
    // A sloppy unit (low-quality sub) leaves a real defect the trade's inspection rules find.

    /// <summary>Fits a batt in each open bay of one wall face (cut to size, split around pipes/boxes, faced flanges stapled).
    /// Sloppy: flanges left unstapled (ins-staple).</summary>
    public class SubInsulateTask : CrewTask
    {
        public FramePlane Face;
        public override string Kind => "sub-insulate";
        public override string Trade => "laborer";
        public override string Title => "Insulate wall";
        public const string Product = "ins-r13-k15";

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            if (Face == null) return Finish();
            foreach (var bay in Face.Bays())
            {
                if (bay.IsOpening || bay.R.height < Units.In(3f) || bay.R.width < Units.In(2f)) continue;
                if (Insulation.BattIn(Face, bay) != null || Insulation.FoamIn(Face, bay) != null) continue;
                if (!FinishStock.Has(Product, 1)) return Block("Need R-13 batts on site");
                var b = bay; var fp = Face;
                return new WorkUnit
                {
                    Spot = fp.World(b.R.center, 0.05f), HasStand = true, StandAt = Flat(fp.World(b.R.center, 0.7f)), Seconds = 3f, Anim = "place",
                    Label = "Fitting a batt",
                    Do = ctx =>
                    {
                        var batt = Insulation.FitBatt(fp, b, Product, true);
                        if (batt == null) return "Couldn't fit the batt (stock?)";
                        Insulation.SplitAround(batt);
                        if (ctx.Sloppy) { CrewDefect.Mark(batt.FaceCenter, "batt flanges not stapled", ctx.W); return null; }
                        if (batt.P.Faced) Insulation.Staple(batt);
                        return null;
                    }
                };
            }
            return Finish();
        }
    }

    /// <summary>Callback: staples every unstapled faced batt in the area.</summary>
    public class SubStapleFixTask : CrewTask
    {
        public override string Kind => "sub-staple";
        public override string Trade => "laborer";
        public override string Title => "Staple batt flanges";

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            foreach (var b in Insulation.Batts)
            {
                if (b == null || !b.P.Faced || b.Stapled || (Radius > 0f && Flat(b.FaceCenter - A).magnitude > Radius)) continue;
                var batt = b;
                return new WorkUnit { Spot = batt.FaceCenter, Seconds = 1.5f, Anim = "hammer", Label = "Stapling flanges", Do = ctx => Insulation.Staple(batt) ? null : "Need staples on site" };
            }
            return Finish();
        }
    }

    /// <summary>Callback: drives every screw line on every hung sheet in the area (fixes a skipped stud line).</summary>
    public class SubDrywallFixTask : CrewTask
    {
        public override string Kind => "sub-dwfix";
        public override string Trade => "drywall";
        public override string Title => "Re-screw drywall";
        readonly HashSet<DrywallSurface> _done = new HashSet<DrywallSurface>();

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            foreach (var s in Drywall.Surfaces)
            {
                if (s == null || s.Fp == null || s.Sheets.Count == 0 || _done.Contains(s)) continue;
                Vector3 c = s.Fp.World(s.Fp.Bounds.center);
                if (Radius > 0f && Flat(c - A).magnitude > Radius) continue;
                var surf = s;
                return new WorkUnit { Spot = s.Fp.World(s.Fp.Bounds.center, 0.05f), Seconds = 4f, Anim = "drill", Label = "Screwing off drywall", Do = ctx => { Drywall.ScrewAll(surf); _done.Add(surf); return null; } };
            }
            return Finish();
        }
    }

    /// <summary>Makes up each electrical box (strip, grounds, neutrals, hots) and installs the device (GFCI where wet,
    /// receptacle otherwise). Sloppy: the grounds left unspliced in one box (el-grounds).</summary>
    public class SubElectricalTask : CrewTask
    {
        public override string Kind => "sub-electrical";
        public override string Trade => "electrician";
        public override string Title => "Make up boxes and trim out";
        readonly HashSet<ElBox> _skip = new HashSet<ElBox>();

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            ElBox best = null; float bd = float.MaxValue;
            foreach (var b in SubScopes.OpenBoxes(A, Radius))
            {
                if (_skip.Contains(b)) continue;
                float d = (b.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = b; }
            }
            if (best == null) return Finish();
            var box = best;
            return new WorkUnit
            {
                Spot = box.transform.position, Seconds = 4f,
                Anim = box.MadeUp ? "drill" : "place", Label = box.MadeUp ? "Installing a device" : "Making up a box",
                Do = ctx =>
                {
                    string err = null;
                    if (!box.Stripped) err = ElTools.Strip(box);
                    for (int i = 0; i < 4 && !box.MadeUp && string.IsNullOrEmpty(err); i++)
                    {
                        if (ctx.Sloppy && box.Stripped && !box.Grounds) { box.Neutrals = true; box.Hots = true; break; }
                        err = ElTools.Splice(box);
                    }
                    if (ctx.Sloppy && !box.Grounds)
                    {
                        _skip.Add(box);
                        CrewDefect.Mark(box.transform.position, "grounds not spliced / pigtailed", ctx.W);
                        return null;
                    }
                    if (!string.IsNullOrEmpty(err)) { _skip.Add(box); return null; }
                    if (box.Device == ElDevice.None && box.Kind != ElBoxKind.Round)
                    {
                        var d = box.Wet ? ElDevice.Gfci : box.Kind == ElBoxKind.TwoGang ? ElDevice.Receptacle20 : ElDevice.Receptacle15;
                        string e2 = ElTools.Install(box, d);
                        if (!string.IsNullOrEmpty(e2)) _skip.Add(box);
                    }
                    return null;
                }
            };
        }
    }

    /// <summary>Makes every open plumbing or HVAC joint in the area (the joining tool's MakeJoint). Sloppy: one joint missed
    /// (mep:*-joints, pressure / duct leakage).</summary>
    public class SubJointTask : CrewTask
    {
        public override string Kind => "sub-joints";
        public override string Trade => Ref == "hvac" ? "hvac" : "plumber";
        public override string Title => Ref == "hvac" ? "Seal duct joints" : "Make pipe joints";
        readonly HashSet<NetPort> _missed = new HashSet<NetPort>();
        Groundbreak.Trade T => Ref == "hvac" ? Groundbreak.Trade.Hvac : Groundbreak.Trade.Plumbing;

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            NetPort best = null; float bd = float.MaxValue;
            foreach (var p in SubScopes.OpenJoints(T, A, Radius))
            {
                if (_missed.Contains(p)) continue;
                float d = (p.WorldPos - from).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (best == null) return Finish();
            var port = best;
            return new WorkUnit
            {
                Spot = port.WorldPos, Seconds = 2.5f, Anim = Ref == "hvac" ? "trowel" : "drill", Label = Ref == "hvac" ? "Taping a duct joint" : "Making a joint",
                Do = ctx =>
                {
                    if (!port.NeedsJoin) return null;
                    if (ctx.Sloppy) { _missed.Add(port); CrewDefect.Mark(port.WorldPos, "joint not made", ctx.W); return null; }
                    JoinTool.MakeJoint(port);
                    return null;
                }
            };
        }
    }

    /// <summary>Works a staked landscape job stage by stage with the right tool (LandscapeFeature.Work).</summary>
    public class SubLandscapeTask : CrewTask
    {
        public LandscapeFeature Feature;
        public override string Kind => "sub-landscape";
        public override string Trade => "landscaper";
        public override string Title => Feature != null ? Feature.Title : "Landscape job";

        static string Anim(string tool)
        {
            switch (tool)
            {
                case "shovel": case "spade": case "posthole": case "rake": case "hoe": return "shovel";
                case "hose": return "hose";
                case "trowel": case "float": return "trowel";
                default: return "kneel";
            }
        }

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            var f = Feature;
            if (f == null || f.Done) return Finish();
            var st = f.Current.Value;
            return new WorkUnit
            {
                Spot = Vector3.Lerp(f.A, f.B, (f.Progress + 0.5f) / Mathf.Max(1, st.Steps)), Seconds = 2.5f, Anim = Anim(st.Tool), Label = st.Name,
                Do = ctx =>
                {
                    if (f.Done) return null;
                    var cur = f.Current.Value;
                    if (f.CuringHours > 0 && !GameSettings.Relaxed) return $"Concrete curing ({f.CuringHours:0.0} h)";
                    return f.Work(cur.Tool, out var msg) ? null : msg;
                }
            };
        }
    }

    /// <summary>Lays LVP in the room of one subfloor (Lvp.Install with the sub's own pull bar). Sloppy: tight 1/8" gap at the walls (fin-floor-gap).</summary>
    public class SubFloorTask : CrewTask
    {
        public FramePlane Face;
        public override string Kind => "sub-floor";
        public override string Trade => "carpenter";
        public override string Title => "Install LVP flooring";

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            if (Face == null || FloorInstall.All.Exists(f => f != null && f.Floor != null && f.Floor.SameFace(Face))) return Finish();
            var fp = Face;
            Vector3 at = SubScopes.ClampTo(fp, A);
            return new WorkUnit
            {
                Spot = at, Seconds = 12f, Anim = "kneel", Label = "Laying LVP",
                Do = ctx =>
                {
                    Rect room = Flooring.FindRoom(fp, at);
                    bool owned = FinishStock.Owns("pull-bar");
                    Tool bar = null;
                    if (!owned && Game.Belt != null) { bar = ToolFactory.Create("pull-bar"); if (bar != null) Game.Belt.Owned.Add(bar); }   // the sub's own tools
                    FloorInstall f;
                    try { f = Lvp.Install(fp, room, null, "oak", ctx.Sloppy ? Units.In(0.125f) : -1f); }
                    finally { if (bar != null) Game.Belt.Owned.Remove(bar); }
                    if (f == null) return "Need LVP boxes and underlayment on site";
                    if (ctx.Sloppy) CrewDefect.Mark(at, "expansion gap only 1/8\" at the walls", ctx.W);
                    return null;
                }
            };
        }
    }

    /// <summary>Pours, consolidates and finishes the formed slab (PourSystem): ready-mix placed in lifts (billed by the plant to
    /// you at $165/cu yd), raked, vibrated, screeded, bull-floated, edged, troweled. Sloppy: vibrating skipped (honeycombing).</summary>
    public class SubConcreteTask : CrewTask
    {
        public const int ReadyMixPerYard = 165;
        public override string Kind => "sub-concrete";
        public override string Trade => "concrete";
        public override string Title => "Pour and finish the slab";
        int _op;
        List<Vector3> _pts;
        int _pt;
        bool _skippedVibe;
        static readonly FinishOp[] Ops = { FinishOp.Rake, FinishOp.Vibrate, FinishOp.Screed, FinishOp.BullFloat, FinishOp.Edge, FinishOp.Trowel };

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            var o = SubScopes.ActiveForm(A, Radius);
            if (o == null) return Finish();
            var pour = o.Pour ?? PourSystem.ForPoint(o.Corners[0]);
            if (pour == null || !pour.Full)
            {
                Vector3 c = Vector3.zero; foreach (var p in o.Corners) c += p; c /= o.Corners.Count;
                return new WorkUnit
                {
                    Spot = c, Seconds = 4f, Anim = "shovel", Label = "Placing concrete",
                    Do = ctx =>
                    {
                        var ps = o.Pour ?? PourSystem.Begin(o);
                        if (ps == null) return "Form isn't ready to pour";
                        _pts = _pts ?? ps.WorkPoints(0.9f);
                        if (_pts.Count == 0) _pts.Add(c);
                        float each = ps.CapacityM3 * 0.27f / _pts.Count, placed = 0f;     // about a quarter of the slab per lift
                        for (int i = 0; i < _pts.Count && !ps.Full; i++) { ps.AddConcrete(_pts[i], each, null); placed += each; }
                        int cost = Mathf.RoundToInt(placed * FormSystem.YardsPerM3 * ReadyMixPerYard);
                        if (!GameSettings.UnlimitedMoney && cost > 0) Game.Wallet.TrySpend(cost, "Ready-mix (ordered by your concrete sub)");
                        return null;
                    }
                };
            }
            _pts = _pts ?? pour.WorkPoints(0.9f);
            if (_op < Ops.Length)
            {
                var op = Ops[_op];
                if (_pt >= _pts.Count) { _op++; _pt = 0; return NextUnit(w, from); }
                var at = _pts[_pt];
                return new WorkUnit
                {
                    Spot = at, Seconds = 1.2f, Anim = op == FinishOp.Rake || op == FinishOp.Vibrate ? "shovel" : "trowel", Label = op.ToString(),
                    Do = ctx =>
                    {
                        if (op == FinishOp.Vibrate && ctx.Sloppy && !_skippedVibe) { _skippedVibe = true; CrewDefect.Mark(at, "concrete not consolidated (honeycomb)", ctx.W); }
                        else pour.Apply(op, at);
                        _pt++;
                        return null;
                    }
                };
            }
            return new WorkUnit { Spot = _pts.Count > 0 ? _pts[0] : A, Seconds = 3f, Anim = "trowel", Label = "Final finish", Do = ctx => { pour.Complete(); return null; } };
        }
    }
}
