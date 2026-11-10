using UnityEngine;

namespace Groundbreak
{
    /// <summary>Base for exterior tools: resolves the aimed wall / face point / eave.</summary>
    public abstract class ExteriorTool : FinishTool
    {
        protected static ExteriorWall WallAt(RaycastHit hit, out Vector2 uv)
        {
            uv = default;
            var w = ExteriorWall.FromHit(hit);
            if (w == null) { Game.Toast("Aim at an exterior wall (sheathing or house wrap)"); return null; }
            if (Vector3.Dot(hit.normal, w.Face.N) < 0.5f && hit.collider.GetComponentInParent<ExtPart>() == null) { Game.Toast("That's the inside of the wall — work from outside"); return null; }
            uv = w.ToUV(hit.point);
            return w;
        }

        protected static Eave EaveAt(RaycastHit hit)
        {
            var fl = hit.collider.GetComponentInParent<FinishLayer>();
            if (fl != null && fl.Owner is Eave e) return e;
            var p = hit.collider.GetComponentInParent<Piece>();
            var ev = Eaves.For(p);
            if (ev == null)
            {
                var w = ExteriorWall.FromHit(hit);
                if (w != null) ev = Eaves.For(Eaves.FindFascia(w));
            }
            if (ev == null) Game.Toast("Aim at a fascia board (rafter tails) or the wall under an eave");
            return ev;
        }

        protected static int Opening(ExteriorWall w, Vector2 uv)
        {
            int i = w.OpeningNear(uv);
            if (i < 0) Game.Toast("Aim at (or next to) a rough opening");
            return i;
        }
    }

    /// <summary>Hammer cap stapler + utility knife + tape: house wrap, seam tape, I-cuts, window flashing.</summary>
    public class CapStaplerTool : ExteriorTool
    {
        public override string Id => "cap-stapler";
        public override string Name => "Hammer Cap Stapler";
        public override string[] Modes => new[] { "Wrap next course", "Tape seams", "Cut opening (modified I-cut)", "Flash sill", "Flash jambs", "Flash head + fold flap" };

        protected override void Use(RaycastHit hit)
        {
            var w = WallAt(hit, out var uv); if (w == null) return;
            int i;
            switch (Mode)
            {
                case 0: HouseWrap.WrapCourse(w); break;
                case 1: HouseWrap.TapeSeams(w); break;
                case 2: if ((i = Opening(w, uv)) >= 0) HouseWrap.CutOpening(w, i); break;
                case 3: if ((i = Opening(w, uv)) >= 0) HouseWrap.FlashSill(w, i); break;
                case 4: if ((i = Opening(w, uv)) >= 0) HouseWrap.FlashJambs(w, i); break;
                default: if ((i = Opening(w, uv)) >= 0) HouseWrap.FlashHead(w, i); break;
            }
            ToolUtil.Wear(this, 0.0005f);
        }
    }

    /// <summary>Coil siding nailer: vinyl accessories and courses, fiber cement, panels/battens, trim boards, soffit.</summary>
    public class SidingNailerTool : ExteriorTool
    {
        public override string Id => "siding-nailer";
        public override string Name => "Coil Siding Nailer";
        public override float Range => 4.5f;
        public override string[] Modes => new[]
        {
            "Vinyl: starter strip", "Vinyl: corner posts", "Vinyl: J-channel", "Vinyl: hang next course", "Vinyl: nail course (slot centers)",
            "Vinyl: single nail at aim (Shift = drive tight)", "Vinyl: next color", "Fiber cement: next course", "Board & batten: panels/battens", "Trim boards (corners, casing)", "Soffit panels (eave)",
        };

        protected override void Use(RaycastHit hit)
        {
            if (Mode == 10) { var e = EaveAt(hit); if (e != null && FinishStock.Require("ext-snips", "cut soffit panels")) Eaves.InstallSoffit(e); return; }
            var w = WallAt(hit, out var uv); if (w == null) return;
            switch (Mode)
            {
                case 0: if (FinishStock.Require("ext-snips", "cut the starter strip")) VinylSiding.Starter(w); break;
                case 1: VinylSiding.CornerPosts(w); break;
                case 2: if (FinishStock.Require("ext-snips", "cut and notch J-channel")) VinylSiding.JChannel(w); break;
                case 3: if (w.Openings.Count == 0 || FinishStock.Require("ext-snips", "notch panels around openings")) VinylSiding.HangCourse(w); break;
                case 4:
                {
                    int k = VinylSiding.CourseAt(w, uv);
                    if (k < 0 && w.Vinyl != null && w.Vinyl.Top != null) k = w.Vinyl.Top.Index;
                    if (k >= 0) VinylSiding.NailCourse(w, k); else Game.Toast("Hang a vinyl course first");
                    break;
                }
                case 5:
                {
                    int k = VinylSiding.CourseAt(w, uv);
                    if (k < 0) { Game.Toast("Aim at a vinyl course's nail hem"); break; }
                    var c = w.Vinyl.Courses[k];
                    bool stud = false; foreach (var s in w.StudLines) if (Mathf.Abs(s - uv.x) < Units.In(0.75f)) stud = true;
                    if (!stud) { Game.Toast("No stud behind — siding nails go into studs (16\" O.C.)"); break; }
                    if (VinylSiding.NailAt(w, c, uv.x, false, GInput.Shift)) Game.Toast(GInput.Shift ? "Nail driven tight — the panel can't move!" : "Nailed (check it's centered in the slot)");
                    break;
                }
                case 6:
                {
                    VinylSiding.StateOf(w);
                    var cols = ExteriorLooks.VinylColors; int ci = 0;
                    for (int j = 0; j < cols.Length; j++) if (cols[j].name == w.Vinyl.ColorName) ci = j;
                    w.Vinyl.ColorName = cols[(ci + 1) % cols.Length].name;
                    VinylSiding.RebuildAll(w);
                    Game.Toast("Vinyl color: " + w.Vinyl.ColorName);
                    break;
                }
                case 7: if (!w.Trim.Corners) ExteriorTrim.CornerBoards(w); else if (!(w.FC != null && w.FC.Starter)) FiberCement.Starter(w); else FiberCement.HangCourse(w); break;
                case 8: if (w.BB == null || !w.BB.Panels) BoardBatten.InstallPanels(w); else BoardBatten.InstallBattens(w); break;
                default: if (!w.Trim.Corners) ExteriorTrim.CornerBoards(w); else ExteriorTrim.CaseOpenings(w); break;
            }
            ToolUtil.Wear(this, 0.0004f);
        }
    }

    /// <summary>Zip tool: unlock / relock, remove, re-fit vinyl courses.</summary>
    public class ZipTool : ExteriorTool
    {
        public override string Id => "siding-zip-tool";
        public override string Name => "Siding Zip Tool";
        public override string[] Modes => new[] { "Unlock / relock course", "Remove course", "Re-fit course" };

        protected override void Use(RaycastHit hit)
        {
            var w = WallAt(hit, out var uv); if (w == null) return;
            if (w.Vinyl == null) { Game.Toast("No vinyl siding here"); return; }
            int k = VinylSiding.CourseAt(w, uv);
            if (Mode == 2)
            {
                // re-fit the lowest removed course
                foreach (var c in w.Vinyl.Courses) if (c.Removed) { VinylSiding.Refit(w, c.Index); return; }
                if (k >= 0) VinylSiding.Relock(w, k);
                return;
            }
            if (k < 0) { Game.Toast("Aim at a vinyl course"); return; }
            if (Mode == 0) { if (w.Vinyl.Courses[k].Locked) VinylSiding.Unzip(w, k); else VinylSiding.Relock(w, k); }
            else VinylSiding.Remove(w, k);
        }
    }

    /// <summary>Mason's trowel, line and jointer: base flashing + weeps, tie rows, one course per click.</summary>
    public class BrickTrowelTool : ExteriorTool
    {
        public override string Id => "brick-trowel";
        public override string Name => "Mason's Trowel & Line";
        public override string[] Modes => new[] { "Lay next course", "Nail brick tie row", "Base flashing + weeps", "Switch to tumbled brick" };

        protected override void Use(RaycastHit hit)
        {
            var w = WallAt(hit, out _); if (w == null) return;
            switch (Mode)
            {
                case 0: BrickVeneer.LayCourse(w); break;
                case 1: BrickVeneer.TieRow(w); break;
                case 2: BrickVeneer.BaseFlashing(w); break;
                default:
                    if (w.Brick != null && w.Brick.Courses > 0) { Game.Toast("Brick type is set once the first course is laid"); break; }
                    BrickVeneer.UseBrick(w, ExteriorCatalog.UsedBrick); Game.Toast("Using tumbled 'used' brick on this wall"); break;
            }
            ToolUtil.Wear(this, 0.001f);
        }
    }

    /// <summary>Aviation snips: vinyl end trims, gutters, downspouts.</summary>
    public class TinSnipsTool : ExteriorTool
    {
        public override string Id => "ext-snips";
        public override string Name => "Vinyl / Aluminum Snips";
        public override string[] Modes => new[] { "Trim vinyl course ends (1/4\" gap)", "Hang gutter (5\" K, 1/4\" per 10')", "Downspout + splash block" };

        protected override void Use(RaycastHit hit)
        {
            if (Mode == 0)
            {
                var w = WallAt(hit, out var uv); if (w == null) return;
                int k = VinylSiding.CourseAt(w, uv);
                if (k < 0) { Game.Toast("Aim at a vinyl course"); return; }
                if (w.Vinyl.Courses[k].Locked && w.Vinyl.Courses[k].Nailed) { Game.Toast("Unzip the course before re-cutting its ends"); return; }
                VinylSiding.TrimEnds(w, k); Game.Toast($"Course {k + 1} ends trimmed: 1/4\" expansion gap in the posts");
                return;
            }
            var e = EaveAt(hit); if (e == null) return;
            if (Mode == 1) { if (FinishStock.Require("level", "set the gutter slope")) Eaves.HangGutter(e); }
            else Eaves.AddDownspout(e);
            ToolUtil.Wear(this, 0.001f);
        }
    }

    /// <summary>Portable brake: bends trim coil to wrap fascia.</summary>
    public class CoilBrakeTool : ExteriorTool
    {
        public override string Id => "coil-brake";
        public override string Name => "Coil Brake";
        public override int Slots => 2;
        public override float Range => 5f;
        protected override void Use(RaycastHit hit)
        {
            var e = EaveAt(hit); if (e == null) return;
            Eaves.WrapFascia(e);
            ToolUtil.Wear(this, 0.0005f);
        }
        public override string Hint => "LMB on a fascia board: bend coil and wrap it (soffit first)";
    }

    /// <summary>Deck Builder's Kit: first click on a wall lays out a 16' x 10' deck, each next click builds a stage.</summary>
    public class DeckKitTool : ExteriorTool
    {
        public override string Id => "deck-kit";
        public override string Name => "Deck Builder's Kit";
        public override float Range => 6f;
        protected override void Use(RaycastHit hit)
        {
            var w = ExteriorWall.FromHit(hit);
            var dp = hit.collider.GetComponentInParent<DeckPart>();
            Deck d = dp != null ? dp.Deck : null;
            if (d == null && w != null) d = DeckBuilder.Near(w, w.ToUV(hit.point));
            if (d == null)
            {
                if (w == null) { Game.Toast("Aim at the house wall where the ledger goes"); return; }
                if (!FinishStock.Require("saw", "cut deck lumber")) return;
                var uv = w.ToUV(hit.point);
                float y = Mathf.Round(uv.y / Units.Inch) * Units.Inch;
                DeckBuilder.Plan(w, w.Face.SnapToFramingU(uv.x) - Units.In(0.75f), 192f, 120f, y);
                return;
            }
            DeckBuilder.NextStage(d);
            ToolUtil.Wear(this, 0.002f);
        }
        public override string Hint => "LMB on the wall: lay out a deck (ledger top at the aim point); LMB again: build the next stage";
    }
}
