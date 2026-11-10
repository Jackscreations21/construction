using UnityEngine;

namespace Groundbreak
{
    /// <summary>Framing square with stair gauges. LMB on a loose 2x12: pencil the stringer layout (from the Stair Calculator).
    /// RMB: cut the laid-out board into a stringer with the circular saw (must own one; drains its battery per cut).</summary>
    public class FramingSquare : Tool
    {
        public override string Id => "framingsquare";
        public override string Name => "Framing Square";
        public override string Hint => "LMB: lay out stringer on aimed loose 2x12 · RMB: cut it (circular saw) · " +
                                       (StairCalculator.HasLayout ? "gauges " + StairMath.FormatIn(StairCalculator.Layout.UnitRiseIn) + " / " + StairMath.FormatIn(StairCalculator.Layout.UnitRunIn)
                                                                  : "set rise in tablet > Stair Calculator");

        const float Range = 4f;
        public const float DrainPerCut = 0.003f;

        public override void Primary()
        {
            var p = ToolUtil.AimPiece(Range, out _);
            if (p == null) return;
            LayOut(p, out string msg);
            Game.Toast(msg);
        }

        public override void Secondary()
        {
            var p = ToolUtil.AimPiece(Range, out _);
            if (p == null) return;
            Cut(p, out _, out string msg);
            Game.Toast(msg);
        }

        public static bool IsStringerStock(Piece p)
        {
            if (p == null || p.Def == null || p.Def.Category != PieceCategory.Lumber) return false;
            return Mathf.Abs(Units.ToInches(p.Size.x) - StairMath.StringerBoardIn) < 0.1f && Mathf.Abs(Units.ToInches(p.Size.y) - 1.5f) < 0.1f;
        }

        /// <summary>Pencils the current Stair Calculator layout onto a loose 2x12.</summary>
        public bool LayOut(Piece p, out string msg)
        {
            if (!IsStringerStock(p)) { msg = "Lay out stringers on a 2x12"; return false; }
            if (p.State != PieceState.Loose) { msg = "Lay out on a loose board (on the sawhorses), not a nailed one"; return false; }
            if (p.GetComponent<CutStringer>() != null) { msg = "Already cut"; return false; }
            if (!StairCalculator.HasLayout) { msg = "Set the rise first: tablet > Stair Calculator > Use for layout"; return false; }
            return StringerMesh.LayOutOnBoard(p, StairCalculator.Layout, out msg);
        }

        /// <summary>Cuts a laid-out board into a stringer. Needs a circular saw on the belt with battery.</summary>
        public bool Cut(Piece p, out Piece stringer, out string msg)
        {
            stringer = null;
            if (p == null || p.GetComponent<StringerLayoutMark>() == null) { msg = "Lay out the stringer first (LMB)"; return false; }
            if (p.State != PieceState.Loose) { msg = "Cut it loose, not nailed in place"; return false; }
            Tool saw = null;
            if (Game.Belt != null) foreach (var t in Game.Belt.Owned) if (t != null && t.Id == "saw") { saw = t; break; }
            if (saw == null) { msg = "You need a circular saw to cut stringers (Tool Shop)"; return false; }
            if (saw.Battery <= 0f) { msg = "Saw battery dead"; return false; }
            if (saw.Durability <= 0f) { msg = "Saw is broken"; return false; }
            stringer = StringerMesh.CutBoard(p, out int cuts);
            saw.Battery = Mathf.Max(0f, saw.Battery - DrainPerCut * cuts);
            ToolUtil.Wear(saw, 0.0005f * cuts);
            var r = stringer.GetComponent<CutStringer>().Result;
            msg = "Stringer cut: " + r.Risers + " risers, " + r.Treads + " treads (" + cuts + " cuts) - finish the notches with a handsaw";
            return true;
        }

        public override void DrawGUI()
        {
            if (!StairCalculator.HasLayout) { ToolUtil.CenterLabel("No stair layout - tablet > Stair Calculator", new Color(1f, 0.7f, 0.3f)); return; }
            var r = StairCalculator.Layout;
            ToolUtil.CenterLabel("Gauges: " + StairMath.FormatIn(r.UnitRiseIn) + " tongue / " + StairMath.FormatIn(r.UnitRunIn) + " blade · " + r.Risers + " risers", Color.white);
        }
    }
}
