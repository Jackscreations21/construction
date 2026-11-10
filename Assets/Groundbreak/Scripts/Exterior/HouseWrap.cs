using System.Collections.Generic;
using UnityEngine;
using static Groundbreak.ExtGeo;

namespace Groundbreak
{
    /// <summary>
    /// House wrap (weather-resistive barrier) on an <see cref="ExteriorWall"/>: 9' rolls run horizontally bottom-up,
    /// shingle-lapped 6" at horizontal seams, 6-12" at vertical seams, cap-stapled ~16" O.C., seams taped. Rough openings
    /// get a modified I-cut (side + sill flaps folded in, head flap flipped up), then flashing tape in shingle order:
    /// sill, jambs, head, and the head flap folded back down over the head tape with its diagonal cuts taped.
    /// Scripted: <see cref="WrapWall"/> (everything) or WrapCourse / TapeSeams / CutOpening / FlashSill / FlashJambs / FlashHead.
    /// </summary>
    public static class HouseWrap
    {
        public static readonly float RollH = In(108f), HLap = In(6f), VLap = In(12f), RunMax = 100f * Units.Foot, FlapCut = In(9f);
        /// <summary>Layer thickness budget: siding backs sit at local z = -2*Thickness.</summary>
        public const float Thickness = 0.003f;

        public class Sheet { public Rect R; public int Order; public int Course; }

        public class OpeningState
        {
            public bool Cut, Sill, Jambs, Head, FlapDown;
            /// <summary>Steps in the order they were done (inspection checks shingle order).</summary>
            public readonly List<string> Steps = new List<string>();
            public bool Flashed => Sill && Jambs && Head && FlapDown;
        }

        public class State
        {
            public readonly List<Sheet> Sheets = new List<Sheet>();
            public readonly List<Vector2> Staples = new List<Vector2>();
            public readonly List<Rect> Tapes = new List<Rect>();
            public readonly Dictionary<int, OpeningState> Ops = new Dictionary<int, OpeningState>();
            public List<Sheet> Courses => Sheets;
            public float RollFeetLeft;
            public OpeningState Op(int i) { if (!Ops.TryGetValue(i, out var s)) Ops[i] = s = new OpeningState(); return s; }
            public float TopV { get { float t = float.MinValue; foreach (var s in Sheets) t = Mathf.Max(t, s.R.yMax); return t; } }
        }

        static float _tapeFeet, _flashFeet;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _tapeFeet = 0; _flashFeet = 0; }

        // ================================================================== scripted API

        /// <summary>Full sequence: every course bottom-up (stapled), seams taped, every opening I-cut and flashed.</summary>
        public static bool WrapWall(ExteriorWall w)
        {
            if (w == null) return false;
            int guard = 0;
            while (!Covered(w) && guard++ < 12) if (!WrapCourse(w)) return false;
            if (!TapeSeams(w)) return false;
            for (int i = 0; i < w.Openings.Count; i++) if (!FlashOpening(w, i)) return false;
            return true;
        }

        /// <summary>Is the whole face covered (wrap top reaches the top of the wall)?</summary>
        public static bool Covered(ExteriorWall w) => w.Wrap.Sheets.Count > 0 && w.Wrap.TopV >= w.B.yMax - In(0.5f);

        /// <summary>Unrolls the next 9' course across the wall (bottom-up, 6" over the course below), cap-stapled on the studs.</summary>
        public static bool WrapCourse(ExteriorWall w)
        {
            var st = w.Wrap;
            if (Covered(w)) { Game.Toast("This wall is fully wrapped — tape the seams next"); return false; }
            float v0 = st.Sheets.Count == 0 ? w.B.yMin - In(1f) : st.TopV - HLap;
            int course = 0; foreach (var s in st.Sheets) course = Mathf.Max(course, s.Course + 1);
            float x = w.B.xMin, end = w.B.xMax;
            var runs = new List<Rect>();
            while (x < end - 0.01f)
            {
                float x1 = Mathf.Min(end, x + RunMax);
                runs.Add(Rect.MinMaxRect(x, v0, x1, Mathf.Min(v0 + RollH, w.B.yMax)));
                if (x1 >= end) break;
                x = x1 - VLap;
            }
            float feet = 0f; foreach (var r in runs) feet += r.width / Units.Foot;
            if (!TakeRoll(st, feet)) return false;
            foreach (var r in runs) AddSheet(w, r, course);
            Rebuild(w);
            Game.Toast($"House wrap course {course + 1} stapled ({Units.Format(v0 - w.B.yMin)} up, 6\" shingle lap)");
            return true;
        }

        /// <summary>Adds one piece of wrap at an arbitrary rect (manual pieces / patches). Order = install order.</summary>
        public static void WrapPiece(ExteriorWall w, Rect r, int course)
        {
            AddSheet(w, r, course);
            Rebuild(w);
        }

        static void AddSheet(ExteriorWall w, Rect r, int course)
        {
            var st = w.Wrap;
            st.Sheets.Add(new Sheet { R = r, Order = st.Sheets.Count, Course = course });
            foreach (var u in w.StudLines)
            {
                if (u < r.xMin + In(1f) || u > r.xMax - In(1f)) continue;
                for (float v = r.yMin + In(3f); v < r.yMax - In(2f); v += In(16f))
                {
                    if (InAnyOpening(w, new Vector2(u, v), In(1f))) continue;
                    if (v > w.B.yMax - In(1f)) continue;
                    st.Staples.Add(new Vector2(u, v));
                }
            }
        }

        static bool TakeRoll(State st, float feet)
        {
            while (st.RollFeetLeft < feet)
            {
                if (!FinishStock.Take(ExteriorCatalog.WrapRoll)) return false;
                st.RollFeetLeft += 100f;
            }
            st.RollFeetLeft -= feet;
            float staples = feet * 0.6f;
            FinishStock.Take(ExteriorCatalog.CapStaples, Mathf.CeilToInt(staples));
            return true;
        }

        static bool TakeFeet(ref float left, string id, float perRoll, float feet)
        {
            while (left < feet)
            {
                if (!FinishStock.Take(id)) return false;
                left += perRoll;
            }
            left -= feet;
            return true;
        }

        /// <summary>Horizontal and vertical seams (u,v segments) between overlapping wrap pieces.</summary>
        public static List<(Vector2 a, Vector2 b)> Seams(ExteriorWall w)
        {
            var res = new List<(Vector2, Vector2)>();
            var sh = w.Wrap.Sheets;
            foreach (var a in sh)
                foreach (var b in sh)
                {
                    if (a == b || !FramePlane.Overlaps(a.R, b.R)) continue;
                    // horizontal: a's bottom edge lies inside b
                    if (a.R.yMin > b.R.yMin + 0.01f && a.R.yMin < b.R.yMax)
                    {
                        float x0 = Mathf.Max(a.R.xMin, b.R.xMin), x1 = Mathf.Min(a.R.xMax, b.R.xMax);
                        if (x1 - x0 > 0.05f) res.Add((new Vector2(x0, a.R.yMin), new Vector2(x1, a.R.yMin)));
                    }
                    // vertical: a's left edge lies inside b (same course)
                    if (a.R.xMin > b.R.xMin + 0.01f && a.R.xMin < b.R.xMax && Mathf.Abs(a.R.yMin - b.R.yMin) < 0.3f)
                    {
                        float y0 = Mathf.Max(a.R.yMin, b.R.yMin), y1 = Mathf.Min(a.R.yMax, b.R.yMax);
                        if (y1 - y0 > 0.05f) res.Add((new Vector2(a.R.xMin, y0), new Vector2(a.R.xMin, y1)));
                    }
                }
            return res;
        }

        public static bool SeamTaped(ExteriorWall w, Vector2 a, Vector2 b)
        {
            Vector2 m = (a + b) * 0.5f;
            foreach (var t in w.Wrap.Tapes)
            {
                var tt = t; tt.xMin -= 0.002f; tt.xMax += 0.002f; tt.yMin -= 0.002f; tt.yMax += 0.002f;
                if (tt.Contains(a + (m - a) * 0.02f) && tt.Contains(b + (m - b) * 0.02f) && tt.Contains(m)) return true;
            }
            return false;
        }

        /// <summary>Tapes every untaped horizontal and vertical seam with house-wrap tape.</summary>
        public static bool TapeSeams(ExteriorWall w)
        {
            if (w.Wrap.Sheets.Count == 0) { Game.Toast("Wrap the wall first"); return false; }
            float hw = In(1.875f) * 0.5f; int n = 0;
            foreach (var (a, b) in Seams(w))
            {
                if (SeamTaped(w, a, b)) continue;
                float feet = (b - a).magnitude / Units.Foot;
                if (!TakeFeet(ref _tapeFeet, ExteriorCatalog.WrapTape, 165f, feet)) break;
                bool horiz = Mathf.Abs(a.y - b.y) < 1e-4f;
                w.Wrap.Tapes.Add(horiz ? Rect.MinMaxRect(a.x, a.y - hw, b.x, a.y + hw) : Rect.MinMaxRect(a.x - hw, a.y, a.x + hw, b.y));
                n++;
            }
            Rebuild(w);
            Game.Toast(n > 0 ? $"Taped {n} seam(s)" : "All seams are taped");
            return true;
        }

        public static bool AllSeamsTaped(ExteriorWall w)
        {
            foreach (var (a, b) in Seams(w)) if (!SeamTaped(w, a, b)) return false;
            return true;
        }

        /// <summary>Modified I-cut at a rough opening: side + sill flaps folded into the opening, head flap cut 45° and flipped up.</summary>
        public static bool CutOpening(ExteriorWall w, int i)
        {
            if (i < 0 || i >= w.Openings.Count) return false;
            var r = w.Openings[i];
            bool covered = false; foreach (var s in w.Wrap.Sheets) if (FramePlane.Overlaps(s.R, r)) covered = true;
            if (!covered) { Game.Toast("Wrap over the opening first, then cut it"); return false; }
            var op = w.Wrap.Op(i);
            if (op.Cut) { Game.Toast("Opening is already cut"); return false; }
            op.Cut = true; op.Steps.Add("cut");
            Rebuild(w);
            Game.Toast("Modified I-cut: sides and sill folded in, head flap flipped up and taped temporarily");
            return true;
        }

        /// <summary>Sill flashing: tape across the sill, 6" up each jamb, lapped over the wrap below.</summary>
        public static bool FlashSill(ExteriorWall w, int i)
        {
            var op = w.Wrap.Op(i);
            if (!op.Cut) { Game.Toast("Cut the wrap at the opening first (modified I-cut)"); return false; }
            if (op.Sill) { Game.Toast("Sill is already flashed"); return false; }
            var r = w.Openings[i];
            if (!TakeFeet(ref _flashFeet, ExteriorCatalog.FlashTape, 75f, (r.width + In(20f)) / Units.Foot)) return false;
            op.Sill = true; op.Steps.Add("sill");
            Rebuild(w); Game.Toast("Sill flashed (first, so the jambs lap over it)");
            return true;
        }

        /// <summary>Jamb flashing: over the window flange, from below the sill tape to above the head.</summary>
        public static bool FlashJambs(ExteriorWall w, int i)
        {
            var op = w.Wrap.Op(i);
            if (!op.Sill) { Game.Toast("Flash the sill first — shingle order is sill, jambs, head"); return false; }
            if (op.Jambs) { Game.Toast("Jambs are already flashed"); return false; }
            var r = w.Openings[i];
            if (!TakeFeet(ref _flashFeet, ExteriorCatalog.FlashTape, 75f, 2f * (r.height + In(8f)) / Units.Foot)) return false;
            op.Jambs = true; op.Steps.Add("jambs");
            Rebuild(w); Game.Toast("Jambs flashed over the sill tape");
            return true;
        }

        /// <summary>Head flashing over the jamb tape, then the head flap folded down over it and the diagonal cuts taped.</summary>
        public static bool FlashHead(ExteriorWall w, int i)
        {
            var op = w.Wrap.Op(i);
            if (!op.Jambs) { Game.Toast("Flash the jambs before the head"); return false; }
            if (op.Head) { Game.Toast("Head is already flashed"); return false; }
            var r = w.Openings[i];
            if (!TakeFeet(ref _flashFeet, ExteriorCatalog.FlashTape, 75f, (r.width + In(10f)) / Units.Foot)) return false;
            op.Head = true; op.Steps.Add("head");
            op.FlapDown = true; op.Steps.Add("flap");
            Rebuild(w); Game.Toast("Head flashed, head flap folded down and its cuts taped");
            return true;
        }

        /// <summary>Cut + sill + jambs + head for one opening (skips steps already done).</summary>
        public static bool FlashOpening(ExteriorWall w, int i)
        {
            var op = w.Wrap.Op(i);
            if (!op.Cut && !CutOpening(w, i)) return false;
            if (!op.Sill && !FlashSill(w, i)) return false;
            if (!op.Jambs && !FlashJambs(w, i)) return false;
            if (!op.Head && !FlashHead(w, i)) return false;
            return true;
        }

        public static bool InAnyOpening(ExteriorWall w, Vector2 uv, float grow = 0f)
        {
            foreach (var r in w.Openings) if (uv.x > r.xMin - grow && uv.x < r.xMax + grow && uv.y > r.yMin - grow && uv.y < r.yMax + grow) return true;
            return false;
        }

        // ================================================================== visuals

        static float SheetZ(int order) => -(0.0006f + 0.00025f * Mathf.Min(order, 8));

        /// <summary>Head flap trapezoid corners above an opening (bottom-left, bottom-right, top-right, top-left).</summary>
        static Vector2[] Flap(Rect r)
        {
            float d = FlapCut * 0.7071f;
            return new[] { new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax + d, r.yMax + d), new Vector2(r.xMin - d, r.yMax + d) };
        }

        public static void Rebuild(ExteriorWall w)
        {
            w.ClearLayer("wrap");
            var st = w.Wrap;
            if (st.Sheets.Count == 0) return;
            var b = new MeshBuilder();
            const int sW = 0, sT = 1, sF = 2, sC = 3;
            float zTop = SheetZ(st.Sheets.Count - 1);

            // sheets with holes at cut openings (the RO + the head flap area, in thin strips)
            var holes = new List<Rect>();
            for (int i = 0; i < w.Openings.Count; i++)
            {
                if (!st.Ops.TryGetValue(i, out var op) || !op.Cut) continue;
                var r = w.Openings[i]; holes.Add(r);
                float d = FlapCut * 0.7071f; const int strips = 8;
                for (int k = 0; k < strips; k++)
                {
                    float y0 = r.yMax + d * k / strips, y1 = r.yMax + d * (k + 1) / strips, g = d * k / strips;
                    holes.Add(Rect.MinMaxRect(r.xMin - g, y0, r.xMax + g, y1));
                }
            }
            foreach (var s in st.Sheets)
                PanelMesh.AddSlab(b, s.R, holes, SheetZ(s.Order), 0f, sW, -1, -1);

            // staples (plastic caps)
            foreach (var p in st.Staples)
            {
                if (InCut(w, p)) continue;
                b.AddCylinder(new Vector3(p.x, p.y, zTop - 0.0008f), Quaternion.Euler(-90f, 0f, 0f), In(0.5f), 0.0016f, 10, sC);
            }
            // seam tape
            foreach (var t in st.Tapes) Front(b, t.xMin, t.xMax, t.yMin, t.yMax, zTop - 0.0004f, sT);

            float depth = Mathf.Max(w.Depth, In(3f)) * 0.95f;
            for (int i = 0; i < w.Openings.Count; i++)
            {
                if (!st.Ops.TryGetValue(i, out var op) || !op.Cut) continue;
                var r = w.Openings[i];
                // flaps folded into the RO (jambs + sill)
                b.AddQuad(new Vector3(r.xMin, r.yMin, 0f), new Vector3(r.xMin, r.yMax, 0f), new Vector3(r.xMin, r.yMax, depth), new Vector3(r.xMin, r.yMin, depth), sW, Vector3.right, Vector3.up);
                b.AddQuad(new Vector3(r.xMax, r.yMin, 0f), new Vector3(r.xMax, r.yMax, 0f), new Vector3(r.xMax, r.yMax, depth), new Vector3(r.xMax, r.yMin, depth), sW, Vector3.left, Vector3.up);
                b.AddQuad(new Vector3(r.xMin, r.yMin, 0f), new Vector3(r.xMax, r.yMin, 0f), new Vector3(r.xMax, r.yMin, depth), new Vector3(r.xMin, r.yMin, depth), sW, Vector3.up);
                var f = Flap(r);
                float zF = zTop - 0.0004f;
                if (op.Sill)
                {
                    zF = zTop - 0.0010f;
                    Front(b, r.xMin - In(4f), r.xMax + In(4f), r.yMin - In(3f), r.yMin, zF, sF);              // lapped onto the face below
                    Front(b, r.xMin - In(4f), r.xMin, r.yMin, r.yMin + In(6f), zF, sF);                        // ears up the face
                    Front(b, r.xMax, r.xMax + In(4f), r.yMin, r.yMin + In(6f), zF, sF);
                    float zs = -0.001f;
                    b.AddQuad(new Vector3(r.xMin, r.yMin + 0.001f, zs), new Vector3(r.xMax, r.yMin + 0.001f, zs), new Vector3(r.xMax, r.yMin + 0.001f, depth), new Vector3(r.xMin, r.yMin + 0.001f, depth), sF, Vector3.up);
                    b.AddQuad(new Vector3(r.xMin + 0.001f, r.yMin, zs), new Vector3(r.xMin + 0.001f, r.yMin + In(6f), zs), new Vector3(r.xMin + 0.001f, r.yMin + In(6f), depth), new Vector3(r.xMin + 0.001f, r.yMin, depth), sF, Vector3.right, Vector3.up);
                    b.AddQuad(new Vector3(r.xMax - 0.001f, r.yMin, zs), new Vector3(r.xMax - 0.001f, r.yMin + In(6f), zs), new Vector3(r.xMax - 0.001f, r.yMin + In(6f), depth), new Vector3(r.xMax - 0.001f, r.yMin, depth), sF, Vector3.left, Vector3.up);
                }
                if (op.Jambs)
                {
                    zF = zTop - 0.0014f;
                    Front(b, r.xMin - In(4f), r.xMin, r.yMin - In(2f), r.yMax + In(4f), zF, sF, Vector3.up);
                    Front(b, r.xMax, r.xMax + In(4f), r.yMin - In(2f), r.yMax + In(4f), zF, sF, Vector3.up);
                }
                if (op.Head)
                {
                    zF = zTop - 0.0018f;
                    Front(b, r.xMin - In(5f), r.xMax + In(5f), r.yMax, r.yMax + In(4f), zF, sF);
                }
                if (op.FlapDown)
                {
                    float zf = zTop - 0.0022f;
                    b.AddPolygon(new[] { V3(f[0], zf), V3(f[1], zf), V3(f[2], zf), V3(f[3], zf) }, sW, Out, Vector3.right);
                    // tape over the two diagonal cuts
                    DiagTape(b, f[0], f[3], zf - 0.0004f, sT);
                    DiagTape(b, f[1], f[2], zf - 0.0004f, sT);
                }
                else
                {
                    // flap flipped up over the hinge line (mirror of the trapezoid), held with a strip of tape
                    float hy = f[2].y, zf = zTop - 0.0006f;
                    Vector2 m0 = new Vector2(f[0].x, 2f * hy - f[0].y), m1 = new Vector2(f[1].x, 2f * hy - f[1].y);
                    b.AddPolygon(new[] { V3(f[3], zf), V3(f[2], zf), V3(m1, zf), V3(m0, zf) }, sW, Out, Vector3.right);
                    Front(b, (m0.x + f[3].x) * 0.5f, (m1.x + f[2].x) * 0.5f, m0.y - In(1f), m0.y + In(1f), zf - 0.0004f, sT);
                }
            }
            var mesh = Build(b, "HouseWrap", 4);
            var mats = new[] { FinishMaterials.Get(ExteriorLooks.Wrap), FinishMaterials.Get(ExteriorLooks.Tape), FinishMaterials.Get(ExteriorLooks.FlashTape), FinishMaterials.Get("caulk-white", new Color(0.55f, 0.72f, 1.0f)) };
            w.Part("wrap", "HouseWrap", mesh, mats, "wrap");
        }

        static Vector3 V3(Vector2 p, float z) => new Vector3(p.x, p.y, z);

        static void DiagTape(MeshBuilder b, Vector2 a, Vector2 c, float z, int sub)
        {
            Vector2 d = (c - a).normalized, n = new Vector2(-d.y, d.x) * In(1.875f) * 0.5f;
            Vector2 a0 = a - d * In(1f), c0 = c + d * In(1f);
            b.AddQuad(V3(a0 - n, z), V3(c0 - n, z), V3(c0 + n, z), V3(a0 + n, z), sub, Out, new Vector3(d.x, d.y, 0f));
        }

        static bool InCut(ExteriorWall w, Vector2 p)
        {
            for (int i = 0; i < w.Openings.Count; i++)
            {
                if (!w.Wrap.Ops.TryGetValue(i, out var op) || !op.Cut) continue;
                var r = w.Openings[i];
                if (p.x > r.xMin - In(10f) && p.x < r.xMax + In(10f) && p.y > r.yMin - In(1f) && p.y < r.yMax + In(14f)) return true;
            }
            return false;
        }
    }
}
