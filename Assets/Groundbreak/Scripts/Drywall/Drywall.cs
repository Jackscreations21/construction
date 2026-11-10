using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Drywall trade: scripted API (tools call the same functions). Sequence: ceilings first (panel lift), then walls
    /// top row first (horizontal, butt ends on studs, bottom row ~1/2" off the floor) → cut out boxes → screw →
    /// corner bead → tape + coat 1 (6") → dry → coat 2 (10") → dry → coat 3 (12") → dry → sand → texture → prime → paint.
    /// <code>
    ///   var s = Drywall.HangSurface(ceilingFp, "dw-12-4x12");   // ceiling first
    ///   var w = Drywall.HangSurface(wallFp, "dw-12-4x12");
    ///   Drywall.CutOutBoxes(w); Drywall.ScrewAll(s); Drywall.ScrewAll(w);
    ///   for (int c = 1; c &lt;= 3; c++) { Drywall.CoatAll(s, c); Drywall.CoatAll(w, c); FinishTime.Skip(24); }
    ///   Drywall.SandAll(w); Drywall.ApplyTexture(w, "orange-peel"); FinishTime.Skip(8);
    ///   PaintJob.Prime(w); FinishTime.Skip(2); PaintJob.Paint(w, PaintColors.Get("Linen"), Sheen.Eggshell); ...
    /// </code>
    /// Or all at once: <see cref="DrywallJob.Complete"/>.
    /// </summary>
    public static partial class Drywall
    {
        public static readonly List<DrywallSurface> Surfaces = new List<DrywallSurface>();
        public static readonly List<CornerBead> Beads = new List<CornerBead>();
        static readonly Dictionary<string, float> _mudLeft = new Dictionary<string, float>();
        static float _tapeFeetPaper, _tapeFeetMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() { Surfaces.Clear(); Beads.Clear(); _mudLeft.Clear(); _tapeFeetMesh = _tapeFeetPaper = 0f; DrywallRules.FireRatedFaces.Clear(); }

        public static DrywallSurface SurfaceFor(FramePlane fp, bool create = false)
        {
            if (fp == null) return null;
            foreach (var s in Surfaces) if (s != null && s.Fp.SameFace(fp)) return s;
            if (!create) return null;
            var go = new GameObject(fp.IsCeiling ? "DrywallCeiling" : "DrywallWall");
            var ds = go.AddComponent<DrywallSurface>();
            ds.Init(fp);
            Surfaces.Add(ds);
            return ds;
        }

        /// <summary>Surface under a raycast hit (on a sheet, or the framing behind it).</summary>
        public static DrywallSurface SurfaceAt(RaycastHit hit)
        {
            var fl = hit.collider.GetComponentInParent<FinishLayer>();
            if (fl != null && fl.Owner is DrywallSheet sh) return sh.S;
            return SurfaceFor(FaceAim.Front(hit));
        }

        public static void TagFireRated(FramePlane fp) { if (!DrywallRules.FireRatedFaces.Exists(f => f.SameFace(fp))) DrywallRules.FireRatedFaces.Add(fp); }

        // ================================================================ layout & hanging

        public struct Plan { public Rect R; public int Row; public bool TaperLo, TaperHi; }

        /// <summary>Ceiling framing above a wall face, and the ceiling drywall thickness hung there (0 = not hung).</summary>
        public static bool CeilingAbove(FramePlane wall, out float hungThickness)
        {
            hungThickness = 0f;
            // probe up from just inside the room along the wall top (joists may sit anywhere along it)
            for (float u = wall.Bounds.xMin + Units.In(4f); u < wall.Bounds.xMax; u += Units.In(3f))
            {
                var p = wall.World(u, wall.Bounds.yMax - 0.03f, 0.25f);
                foreach (var h in FinishAim.SortedHits(new Ray(p, Vector3.up), 0.4f))
                {
                    if (h.collider.GetComponentInParent<FinishLayer>() != null) continue;
                    var pc = h.collider.GetComponentInParent<Piece>();
                    if (pc == null || pc.Def == null || pc.Def.Category != PieceCategory.Lumber) break;
                    if (h.point.y - wall.World(u, wall.Bounds.yMax).y > 0.03f) break; // not resting on this wall
                    foreach (var s in Surfaces)
                        if (s != null && s.Fp.IsCeiling && s.Fp.Members.Exists(m => m.Piece == pc))
                        {
                            var sh = s.SheetAt(s.Fp.ToUV(p));
                            if (sh != null) hungThickness = sh.P.Thick;
                        }
                    return true;
                }
            }
            return false;
        }

        /// <summary>Planned sheet layout for a face with the given product: walls horizontal from the top (tight to the
        /// ceiling), ceilings perpendicular to the joists; butt ends land on framing centers; odd rows staggered.</summary>
        public static List<Plan> PlanLayout(FramePlane fp, DrywallProduct p)
        {
            var res = new List<Plan>();
            var b = fp.Bounds; float sw = p.Width;
            var rows = new List<(float v0, float v1, bool lo, bool hi)>();
            if (fp.IsWall)
            {
                float top = b.yMax;
                if (CeilingAbove(fp, out float ct)) top -= ct > 0 ? ct : Units.In(0.5f);
                float bottom = b.yMin + Units.In(DrywallRules.FloorGapIn);
                float v = top;
                while (v - bottom > 0.02f) { float h = Mathf.Min(sw, v - bottom); rows.Add((v - h, v, h >= sw - 0.001f, true)); v -= h; }
            }
            else
            {
                float v = b.yMin;
                while (b.yMax - v > 0.02f) { float h = Mathf.Min(sw, b.yMax - v); rows.Add((v, v + h, true, h >= sw - 0.001f)); v += h; }
            }
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                float s = b.xMin; bool first = true;
                var lines = fp.FramingLinesU((row.v0 + row.v1) * 0.5f);
                while (b.xMax - s > 0.02f)
                {
                    float target = s + p.Length;
                    if (first && r % 2 == 1 && b.width > p.Length + 0.01f) target = s + p.Length * 0.5f;
                    float e;
                    if (target >= b.xMax - 0.01f) e = b.xMax;
                    else
                    {
                        e = target;
                        for (int k = lines.Count - 1; k >= 0; k--) if (lines[k] <= target + 0.001f && lines[k] > s + 0.3f) { e = lines[k]; break; }
                    }
                    res.Add(new Plan { R = Rect.MinMaxRect(s, row.v0, e, row.v1), Row = r, TaperLo = row.lo, TaperHi = row.hi });
                    s = e; first = false;
                }
            }
            return res;
        }

        /// <summary>Next sheet of the layout that isn't hung yet (walls: refuses until the ceiling above is hung).</summary>
        public static bool NextPlan(FramePlane fp, DrywallProduct p, out Plan plan, out string why)
        {
            plan = default; why = null;
            if (fp.IsWall && CeilingAbove(fp, out float ct) && ct <= 0f) { why = "Hang the ceiling before the walls"; return false; }
            var s = SurfaceFor(fp);
            foreach (var pl in PlanLayout(fp, p))
            {
                bool done = false;
                if (s != null) foreach (var sh in s.Sheets) if (FramePlane.Overlaps(sh.R, pl.R, 0.01f)) { done = true; break; }
                if (!done) { plan = pl; return true; }
            }
            why = "This face is fully hung";
            return false;
        }

        /// <summary>Measure, score &amp; snap, and hang one sheet (cut to the plan rect, openings cut out). Uses one sheet of stock.</summary>
        public static DrywallSheet HangSheet(FramePlane fp, DrywallProduct p, Plan pl)
        {
            var s = SurfaceFor(fp, true);
            foreach (var sh0 in s.Sheets) if (FramePlane.Overlaps(sh0.R, pl.R, 0.01f)) { Game.Toast("A sheet is already hung there"); return null; }
            if (!FinishStock.Take(p.Id)) return null;
            var sh = new DrywallSheet { P = p, R = pl.R, Row = pl.Row, TaperLo = pl.TaperLo, TaperHi = pl.TaperHi };
            foreach (var o in fp.Openings())
            {
                var h = o;
                if (h.yMin < fp.Bounds.yMin + Units.In(2f)) h.yMin = fp.Bounds.yMin - 0.05f; // door: no sliver over the plate
                if (FramePlane.Overlaps(h, sh.R)) sh.Holes.Add(h);
            }
            foreach (var o in fp.Obstacles(sh.R))
                if (o.Proud > -0.006f && o.Proud < 0.04f && FramePlane.Overlaps(o.R, sh.R) && !sh.InHole(o.R.center)) sh.Covered.Add(o.R); // boxes set flush/proud for the board; pipes running through are ignored
            s.AddSheet(sh);
            Insulation.RebuildBatts(fp);
            return sh;
        }

        public static DrywallSheet HangNext(FramePlane fp, string productId)
        {
            var p = Product(productId);
            if (p == null) { Game.Toast("Unknown drywall " + productId); return null; }
            if (!NextPlan(fp, p, out var pl, out var why)) { Game.Toast(why); return null; }
            return HangSheet(fp, p, pl);
        }

        /// <summary>Hang the whole face (in the real order) with one product. Returns the surface.</summary>
        public static DrywallSurface HangSurface(FramePlane fp, string productId)
        {
            var p = Product(productId);
            for (int guard = 0; guard < 200; guard++)
            {
                if (!NextPlan(fp, p, out var pl, out var why)) { if (why != "This face is fully hung") Game.Toast(why); break; }
                if (HangSheet(fp, p, pl) == null) break;
            }
            var s = SurfaceFor(fp);
            if (s != null) s.Refresh();
            return s;
        }

        // ================================================================ cut-outs

        /// <summary>Cut a box (or any covered obstacle) out of the sheet with a router / keyhole saw.</summary>
        public static bool CutOut(DrywallSheet sh, Rect box)
        {
            if (sh == null) return false;
            var h = Rect.MinMaxRect(box.xMin - Units.In(0.0625f), box.yMin - Units.In(0.0625f), box.xMax + Units.In(0.0625f), box.yMax + Units.In(0.0625f));
            sh.Covered.RemoveAll(c => FramePlane.Overlaps(c, h));
            sh.Holes.Add(h);
            sh.S.BuildSheetMesh(sh);
            sh.S.Changed(true);
            return true;
        }

        public static int CutOutBoxes(DrywallSurface s)
        {
            int n = 0;
            foreach (var sh in s.Sheets) foreach (var c in new List<Rect>(sh.Covered)) if (CutOut(sh, c)) n++;
            s.Refresh();
            return n;
        }

        /// <summary>Covered box nearest uv (within range) on any sheet of the surface.</summary>
        public static bool CoveredNear(DrywallSurface s, Vector2 uv, float range, out DrywallSheet sheet, out Rect box)
        {
            sheet = null; box = default; float bd = range;
            foreach (var sh in s.Sheets)
                foreach (var c in sh.Covered)
                {
                    float d = (c.center - uv).magnitude;
                    if (d < bd) { bd = d; sheet = sh; box = c; }
                }
            return sheet != null;
        }

        // ================================================================ screws

        public static float Spacing(DrywallSurface s) => Units.In(s.IsCeiling ? DrywallRules.ScrewSpacingCeilingIn : DrywallRules.ScrewSpacingWallIn);

        /// <summary>A stretch of one framing member (stud / joist / cripple) behind a sheet that must be screwed.</summary>
        public struct ScrewRun { public float U, V0, V1; }

        /// <summary>Every run of framing behind the sheet: member centers (pulled 1/2" in from the sheet ends),
        /// limited to the member's length and split by cut-outs.</summary>
        public static List<ScrewRun> ScrewRuns(DrywallSheet sh)
        {
            var res = new List<ScrewRun>();
            float edge = Units.In(0.5f);
            foreach (var m in sh.S.Fp.Members)
            {
                if (!m.AlongV) continue;
                float c = m.CenterU;
                if (c < sh.R.xMin - edge || c > sh.R.xMax + edge) continue;
                float u = Mathf.Clamp(c, sh.R.xMin + edge, sh.R.xMax - edge);
                if (u < m.R.xMin - 0.003f || u > m.R.xMax + 0.003f) continue;
                float v0 = Mathf.Max(sh.R.yMin, m.R.yMin), v1 = Mathf.Min(sh.R.yMax, m.R.yMax);
                if (v1 - v0 < Units.In(1.5f)) continue;
                var spans = new List<Vector2> { new Vector2(v0, v1) };
                foreach (var h in sh.Holes)
                {
                    if (u < h.xMin || u > h.xMax) continue;
                    var next = new List<Vector2>();
                    foreach (var sp in spans)
                    {
                        if (h.yMax <= sp.x || h.yMin >= sp.y) { next.Add(sp); continue; }
                        if (h.yMin > sp.x) next.Add(new Vector2(sp.x, h.yMin));
                        if (h.yMax < sp.y) next.Add(new Vector2(h.yMax, sp.y));
                    }
                    spans = next;
                }
                foreach (var sp in spans)
                    if (sp.y - sp.x >= Units.In(1.5f) && !res.Exists(r => Mathf.Abs(r.U - u) < 0.005f && Mathf.Abs(r.V0 - sp.x) < 0.005f))
                        res.Add(new ScrewRun { U = u, V0 = sp.x, V1 = sp.y });
            }
            return res;
        }

        public static List<float> ScrewLines(DrywallSheet sh)
        {
            var l = new List<float>();
            foreach (var r in ScrewRuns(sh)) if (!l.Exists(x => Mathf.Abs(x - r.U) < 0.005f)) l.Add(r.U);
            l.Sort();
            return l;
        }

        static bool HasScrew(DrywallSurface s, Vector2 p, DrywallSheet sh, float r = 0.02f) { foreach (var sc in s.Screws) if (sc.Sheet == sh && (sc.P - p).sqrMagnitude < r * r) return true; return false; }

        /// <summary>Drive one screw (snapped to the nearest framing line, kept 1/2" off the sheet edges).</summary>
        public static DrywallScrew DriveScrew(DrywallSurface s, Vector2 uv)
        {
            var sh = s.SheetAt(uv);
            if (sh == null) return null;
            ScrewRun best = default; float bd = Units.In(3f); bool found = false;
            foreach (var r in ScrewRuns(sh))
            {
                float d = Mathf.Abs(r.U - uv.x) + Mathf.Max(0f, Mathf.Max(r.V0 - uv.y, uv.y - r.V1));
                if (d < bd) { bd = d; best = r; found = true; }
            }
            if (!found) { Game.Toast("No stud/joist there — screws must hit framing"); return null; }
            var p = new Vector2(best.U, Mathf.Clamp(uv.y, best.V0 + Units.In(0.5f), best.V1 - Units.In(0.5f)));
            if (sh.InHole(p) || HasScrew(s, p, sh)) return null;
            if (!FinishStock.Take(ScrewsId)) return null;
            var sc = new DrywallScrew { P = p, Sheet = sh };
            s.Screws.Add(sc);
            s.Changed(true);
            return sc;
        }

        /// <summary>Auto-feed: screws down a whole framing line of a sheet at code spacing (12" ceilings / 16" walls).</summary>
        public static int ScrewLine(DrywallSheet sh, float u)
        {
            var s = sh.S; int n = 0;
            float sp = Spacing(s) - Units.In(0.5f), e = Units.In(0.5f);
            foreach (var r in ScrewRuns(sh))
            {
                if (Mathf.Abs(r.U - u) > 0.005f) continue;
                var run = new Vector2(r.V0, r.V1);
                float len = run.y - run.x - 2 * e;
                int segs = Mathf.Max(1, Mathf.CeilToInt(len / sp));
                for (int k = 0; k <= segs; k++)
                {
                    var p = new Vector2(u, run.x + e + len * k / segs);
                    if (HasScrew(s, p, sh)) continue;
                    if (!FinishStock.Take(ScrewsId)) return n;
                    s.Screws.Add(new DrywallScrew { P = p, Sheet = sh }); n++;
                }
            }
            if (n > 0) s.Changed(true);
            return n;
        }

        public static int ScrewSheet(DrywallSheet sh) { int n = 0; foreach (var u in ScrewLines(sh)) n += ScrewLine(sh, u); return n; }
        public static int ScrewAll(DrywallSurface s) { int n = 0; foreach (var sh in s.Sheets) n += ScrewSheet(sh); s.Refresh(); return n; }

        // ================================================================ joints

        static bool TakeMud(string compound, float feet)
        {
            _mudLeft.TryGetValue(compound, out float left);
            float need = feet / 300f; // ~300 lf of joint per bucket per coat
            while (left < need) { if (!FinishStock.Take(compound)) return false; left += 1f; }
            _mudLeft[compound] = left - need;
            return true;
        }

        static bool TakeTape(bool mesh, float feet)
        {
            if (mesh) { while (_tapeFeetMesh < feet) { if (!FinishStock.Take(MeshTapeId, 300)) return false; _tapeFeetMesh += 300f; } _tapeFeetMesh -= feet; }
            else { while (_tapeFeetPaper < feet) { if (!FinishStock.Take(PaperTapeId, 500)) return false; _tapeFeetPaper += 500f; } _tapeFeetPaper -= feet; }
            return true;
        }

        /// <summary>Apply the next coat to one ~4' joint segment with the given knife. Coat 1 embeds tape.</summary>
        public static bool Coat(DrywallSurface s, Seam seam, SeamSeg g, float knifeIn, bool mesh, string compound, out string why)
        {
            why = null;
            int next = g.Coats + 1;
            if (next > 3) { why = "This joint has its 3 coats — sand it when dry"; return false; }
            if (seam.Kind == SeamKind.OutsideCorner && !HasBead(s, seam)) { why = "Set corner bead on this outside corner first"; return false; }
            if (g.Coats > 0 && DrywallSurface.SegWet(g)) { why = $"Coat {next} needs coat {g.Coats} dry: {DrywallSurface.SegDryLeft(g):0.#} h left"; return false; }
            float need = DrywallRules.KnifeForCoat[next - 1];
            if (Mathf.Abs(knifeIn - need) > 0.1f) { why = next == 1 ? "Tape coat goes on with the 6\" knife" : $"Coat {next} goes on with the {need:0}\" knife"; return false; }
            var comp = DrywallRules.Compound(compound);
            if (next == 1 && mesh && !comp.Setting) { why = "Mesh tape needs setting compound — all-purpose cracks over mesh"; return false; }
            float feet = (g.B - g.A) / Units.Foot;
            if (next == 1 && seam.Kind != SeamKind.OutsideCorner && !TakeTape(mesh, feet)) { why = "Out of tape"; return false; }
            if (!TakeMud(comp.Id, feet)) { why = "Out of joint compound"; return false; }
            g.Coats = next; g.CoatTime = FinishTime.Now; g.Compound = comp.Id; g.Sanded = false;
            if (next == 1) g.Mesh = mesh && seam.Kind != SeamKind.OutsideCorner;
            s.Changed(false);
            return true;
        }

        /// <summary>Spot screws (one coat) within radius of uv. Returns screws spotted.</summary>
        public static int SpotScrews(DrywallSurface s, Vector2 uv, float radius, out string why)
        {
            why = null; int n = 0, wet = 0;
            foreach (var sc in s.Screws)
            {
                if ((sc.P - uv).sqrMagnitude > radius * radius || sc.Spots >= 3) continue;
                if (DrywallSurface.ScrewWet(sc)) { wet++; continue; }
                sc.Spots++; sc.SpotTime = FinishTime.Now; sc.Sanded = false; n++;
            }
            if (n > 0) { TakeMud("dw-mud-ap", n * 0.05f); s.Changed(false); s.RebuildScrews(); }
            else if (wet > 0) why = "Those screw spots are still wet";
            return n;
        }

        /// <summary>Apply coat <paramref name="coat"/> (1..3) to every joint segment and screw that is ready for it.
        /// Coat 1 = tape (paper unless mesh) + 6" knife; 2 = 10"; 3 = 12". Returns pieces coated.</summary>
        public static int CoatAll(DrywallSurface s, int coat, bool mesh = false, string compound = null)
        {
            s.Refresh();
            compound = compound ?? (mesh && coat == 1 ? "dw-mud-set45" : "dw-mud-ap");
            int n = 0; string last = null;
            foreach (var seam in s.Seams)
            {
                if (seam.Kind == SeamKind.OutsideCorner && !HasBead(s, seam)) InstallBead(s, seam, false);
                foreach (var g in seam.Segs)
                    if (g.Coats == coat - 1) { if (Coat(s, seam, g, DrywallRules.KnifeForCoat[coat - 1], mesh, compound, out var why)) n++; else last = why; }
            }
            foreach (var sc in s.Screws)
                if (sc.Spots == coat - 1 && !DrywallSurface.ScrewWet(sc)) { sc.Spots++; sc.SpotTime = FinishTime.Now; n++; }
            s.RebuildScrews(); s.Changed(false); s.Refresh();
            if (last != null && n == 0) Game.Toast(last);
            return n;
        }

        /// <summary>Is every joint segment and screw at this coat (and dry)?</summary>
        public static bool AllCoated(DrywallSurface s, int coat, bool dry)
        {
            foreach (var seam in s.Seams) foreach (var g in seam.Segs) if (g.Coats < coat || (dry && DrywallSurface.SegWet(g))) return false;
            foreach (var sc in s.Screws) if (sc.Spots < coat || (dry && DrywallSurface.ScrewWet(sc))) return false;
            return true;
        }

        /// <summary>Sand dry third-coat joints near uv (pole sander on flats; sponge for corners).</summary>
        public static int Sand(DrywallSurface s, Vector2 uv, float radius, bool sponge, out string why)
        {
            why = null; int n = 0;
            foreach (var seam in s.Seams)
                foreach (var g in seam.Segs)
                {
                    var mid = seam.At((g.A + g.B) * 0.5f);
                    float d = Mathf.Min((mid - uv).magnitude, Mathf.Min((seam.At(g.A) - uv).magnitude, (seam.At(g.B) - uv).magnitude));
                    if (d > radius || g.Sanded) continue;
                    if (g.Coats < 3) { why = $"Joint has {g.Coats} coat(s) — sand after the third"; continue; }
                    if (DrywallSurface.SegWet(g)) { why = $"Mud still wet: {DrywallSurface.SegDryLeft(g):0.#} h"; continue; }
                    if (seam.Kind == SeamKind.InsideCorner && !sponge) { why = "Use the sanding sponge in corners (RMB)"; continue; }
                    g.Sanded = true; n++;
                }
            foreach (var sc in s.Screws)
                if ((sc.P - uv).sqrMagnitude <= radius * radius && sc.Spots >= 3 && !DrywallSurface.ScrewWet(sc) && !sc.Sanded) { sc.Sanded = true; n++; }
            if (n > 0) { FinishStock.Take(SandpaperId, Mathf.Max(1, n / 12)); s.Changed(false); }
            return n;
        }

        public static int SandAll(DrywallSurface s)
        {
            int n = 0;
            foreach (var seam in s.Seams) foreach (var g in seam.Segs) if (!g.Sanded && g.Coats >= 3 && !DrywallSurface.SegWet(g)) { g.Sanded = true; n++; }
            foreach (var sc in s.Screws) if (!sc.Sanded && sc.Spots >= 3 && !DrywallSurface.ScrewWet(sc)) { sc.Sanded = true; n++; }
            if (n > 0) FinishStock.Take(SandpaperId, Mathf.Max(1, n / 12));
            s.Changed(false); s.Refresh();
            return n;
        }

        public static bool AllSanded(DrywallSurface s)
        {
            foreach (var seam in s.Seams) foreach (var g in seam.Segs) if (!g.Sanded) return false;
            foreach (var sc in s.Screws) if (!sc.Sanded) return false;
            return true;
        }

        // ================================================================ texture

        public static readonly string[] TextureKinds = { "smooth", "orange-peel", "knockdown" };

        /// <summary>Texture the whole surface (area = null) or an area: "orange-peel" / "knockdown" sprayed, "smooth" = level-5 skim.
        /// Needs the joints sanded.</summary>
        public static bool ApplyTexture(DrywallSurface s, string kind, Rect? area = null)
        {
            s.Refresh();
            if (System.Array.IndexOf(TextureKinds, kind) < 0) { Game.Toast("Unknown texture " + kind); return false; }
            if (!AllSanded(s)) { Game.Toast("Finish and sand all joints before texturing"); return false; }
            if (s.TextureKind != null && s.TextureKind != kind) { Game.Toast($"This surface already has {s.TextureKind} texture"); return false; }
            int c = s.TextureRect(area ?? s.Area);
            if (c == 0) return s.TextureKind == kind;
            float sq = c * DrywallSurface.T * DrywallSurface.T / (Units.Foot * Units.Foot);
            TakeMud(kind == "smooth" ? "dw-mud-ap" : TextureMudId, sq * 0.6f);
            s.TextureKind = kind; s.TextureTime = FinishTime.Now - (kind == "smooth" ? DrywallRules.TextureDryHours - DrywallRules.SkimDryHours : 0f);
            s.Changed(false); s.Refresh();
            return true;
        }

        public static bool TextureDry(DrywallSurface s) => s.TextureKind == null || FinishTime.Now - s.TextureTime >= DrywallRules.TextureDryHours;

        // ================================================================ corners & bead

        public struct CornerInfo { public SeamKind Kind; public Vector2 P0, P1, Side; public DrywallSurface Other; }

        /// <summary>Inside / outside corners where another drywall face meets this one (perpendicular faces).</summary>
        public static List<CornerInfo> CornersOn(DrywallSurface s)
        {
            var res = new List<CornerInfo>();
            if (s.Sheets.Count == 0) return res;
            var S = s.Fp;
            foreach (var o in Surfaces)
            {
                if (o == null || o == s || o.Sheets.Count == 0) continue;
                var O = o.Fp;
                Vector3 on = O.N;
                if (Mathf.Abs(Vector3.Dot(S.N, on)) > 0.1f) continue;
                float du = Vector3.Dot(S.U, on), dv = Vector3.Dot(S.V, on);
                bool vertical = Mathf.Abs(du) > 0.99f;
                if (!vertical && Mathf.Abs(dv) < 0.99f) continue;
                float c = O.FaceOffset + o.Thickness;
                float w0 = (c - Vector3.Dot(S.Origin + S.N * s.Thickness, on)) / (vertical ? du : dv);
                // reach: s sheets meet the line and o sheets meet s's plane
                if (!Reaches(s, vertical, w0)) continue;
                Vector3 sn = S.N; float ou = Vector3.Dot(O.U, sn), ov = Vector3.Dot(O.V, sn);
                bool oVert = Mathf.Abs(ou) > 0.99f;
                float w1 = (S.FaceOffset + s.Thickness - Vector3.Dot(O.Origin + O.N * o.Thickness, sn)) / (oVert ? ou : ov);
                if (!Reaches(o, oVert, w1)) continue;
                // extent along the line: overlap of both faces' bounds
                float lo = vertical ? S.Bounds.yMin : S.Bounds.xMin, hi = vertical ? S.Bounds.yMax : S.Bounds.xMax;
                var ob = O.WorldBounds(); float olo = float.MaxValue, ohi = float.MinValue;
                for (int k = 0; k < 8; k++)
                {
                    var pt = new Vector3((k & 1) == 0 ? ob.min.x : ob.max.x, (k & 2) == 0 ? ob.min.y : ob.max.y, (k & 4) == 0 ? ob.min.z : ob.max.z);
                    var uv = S.ToUV(pt); float a = vertical ? uv.y : uv.x;
                    olo = Mathf.Min(olo, a); ohi = Mathf.Max(ohi, a);
                }
                // clip to where this face actually has sheets along the line
                float slo = float.MaxValue, shi = float.MinValue;
                foreach (var sh in s.Sheets)
                {
                    float a0 = vertical ? sh.R.xMin : sh.R.yMin, a1 = vertical ? sh.R.xMax : sh.R.yMax;
                    if (w0 < a0 - 0.03f || w0 > a1 + 0.03f) continue;
                    slo = Mathf.Min(slo, vertical ? sh.R.yMin : sh.R.xMin); shi = Mathf.Max(shi, vertical ? sh.R.yMax : sh.R.xMax);
                }
                lo = Mathf.Max(lo, Mathf.Max(olo, slo)); hi = Mathf.Min(hi, Mathf.Min(ohi, shi));
                if (hi - lo < 0.05f) continue;
                bool oFront = S.ProudOf(O.World(O.Bounds.center)) > 0f, sFront = O.ProudOf(S.World(S.Bounds.center)) > 0f;
                SeamKind kind;
                if (oFront && sFront) kind = SeamKind.InsideCorner;
                else if (!oFront && !sFront) kind = SeamKind.OutsideCorner;
                else continue;
                var toward = vertical ? new Vector2(Mathf.Sign(du), 0f) : new Vector2(0f, Mathf.Sign(dv));
                res.Add(new CornerInfo
                {
                    Kind = kind,
                    P0 = vertical ? new Vector2(w0, lo) : new Vector2(lo, w0),
                    P1 = vertical ? new Vector2(w0, hi) : new Vector2(hi, w0),
                    Side = kind == SeamKind.InsideCorner ? toward : -toward,
                    Other = o
                });
            }
            return res;
        }

        static bool Reaches(DrywallSurface s, bool vertical, float w)
        {
            foreach (var sh in s.Sheets)
            {
                float a0 = vertical ? sh.R.xMin : sh.R.yMin, a1 = vertical ? sh.R.xMax : sh.R.yMax;
                if (w >= a0 - 0.03f && w <= a1 + 0.03f) return true;
            }
            return false;
        }

        public class CornerBead
        {
            public Vector3 A, B, N1, N2; public bool Paper; public GameObject Go;
        }

        public static bool HasBead(DrywallSurface s, Seam seam)
        {
            var a = s.FrontWorld(seam.P0); var b = s.FrontWorld(seam.P1);
            foreach (var bd in Beads)
            {
                var mid = (a + b) * 0.5f;
                var d = bd.B - bd.A; float t = Mathf.Clamp01(Vector3.Dot(mid - bd.A, d) / Mathf.Max(1e-6f, d.sqrMagnitude));
                if ((bd.A + d * t - mid).magnitude < 0.04f) return true;
            }
            return false;
        }

        /// <summary>Set (nail + mud) corner bead on an outside corner seam: metal or paper-faced. Visual angle along the nose.</summary>
        public static CornerBead InstallBead(DrywallSurface s, Seam seam, bool paper)
        {
            if (seam.Kind != SeamKind.OutsideCorner || HasBead(s, seam)) return null;
            int sticks = Mathf.Max(1, Mathf.CeilToInt(seam.Length / (Units.Foot * 8f)));
            if (!FinishStock.Take(paper ? BeadPaperId : BeadMetalId, sticks)) return null;
            // the corner nose: on s's finished plane at the seam line, pushed out to the other face's finished plane
            var n1 = s.Fp.N; var other = s.Fp.U * seam.Side.x + s.Fp.V * seam.Side.y; var n2 = -other; // other face's normal points away from s's band
            Vector3 a = s.FrontWorld(seam.P0) , b = s.FrontWorld(seam.P1);
            var bd = new CornerBead { A = a, B = b, N1 = n1, N2 = n2, Paper = paper };
            var sm = new SoftMesh();
            Vector3 dir = (b - a).normalized; float L = (b - a).magnitude, fl = Units.In(1.25f);
            var nose = (n1 + n2).normalized * 0.002f;
            var go = new GameObject("CornerBead");
            go.AddComponent<FinishLayer>().Kind = "corner-bead";
            go.transform.SetPositionAndRotation(a, Quaternion.LookRotation(dir, n1));
            // local frame: z along the corner, y = n1, x = cross
            Vector3 L1 = go.transform.InverseTransformDirection(-n2) * fl, L2 = go.transform.InverseTransformDirection(-n1) * fl, Ln = go.transform.InverseTransformDirection(nose);
            Vector3 up1 = go.transform.InverseTransformDirection(n1) * 0.0008f, up2 = go.transform.InverseTransformDirection(n2) * 0.0008f;
            var z = Vector3.forward * L;
            sm.FlatQuad(Ln, Ln + z, L1 + up1 + z, L1 + up1, 0, go.transform.InverseTransformDirection(n1), Vector3.forward, Vector3.right);
            sm.FlatQuad(Ln, Ln + z, L2 + up2 + z, L2 + up2, 0, go.transform.InverseTransformDirection(n2), Vector3.forward, Vector3.right);
            go.AddComponent<MeshFilter>().sharedMesh = sm.Build("CornerBead");
            go.AddComponent<MeshRenderer>().sharedMaterial = paper ? FinishMaterials.Get(DrywallLooks.Back, new Color(1.6f, 1.6f, 1.65f)) : FinishMaterials.Get(DrywallLooks.Bead);
            bd.Go = go;
            Beads.Add(bd);
            s.Changed(false);
            return bd;
        }

        /// <summary>Bead looks: bare metal → mudded (white) → painted with the face's paint.</summary>
        public static void UpdateBeads(DrywallSurface s)
        {
            foreach (var seam in s.Seams)
            {
                if (seam.Kind != SeamKind.OutsideCorner) continue;
                foreach (var bd in Beads)
                {
                    if (bd.Go == null) continue;
                    var mid = s.FrontWorld(seam.At(seam.Length * 0.5f));
                    var d = bd.B - bd.A; float t = Mathf.Clamp01(Vector3.Dot(mid - bd.A, d) / Mathf.Max(1e-6f, d.sqrMagnitude));
                    if ((bd.A + d * t - mid).magnitude > 0.04f) continue;
                    int coats = 0; foreach (var g in seam.Segs) coats = Mathf.Max(coats, g.Coats);
                    Color c = Color.white; bool painted = false;
                    if (s.ToTexel(seam.At(seam.Length * 0.5f) + seam.Side * 0.03f, out int x, out int y)) { int i = y * s.W + x; if (s.Paint[i] > 0) { c = s.PaintColor[i]; painted = true; } }
                    var mr = bd.Go.GetComponent<MeshRenderer>();
                    if (painted) mr.sharedMaterial = FinishMaterials.Get("paint-smooth", c);
                    else if (coats > 0) mr.sharedMaterial = FinishMaterials.Get("paint-smooth", new Color(0.97f, 0.97f, 0.96f));
                }
            }
        }
    }
}
