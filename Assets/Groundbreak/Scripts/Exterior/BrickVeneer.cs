using System.Collections.Generic;
using UnityEngine;
using static Groundbreak.ExtGeo;

namespace Groundbreak
{
    /// <summary>
    /// Anchored brick veneer: modular brick (7-5/8 x 2-1/4 x 3-5/8) in running bond with 3/8" tooled concave joints,
    /// 1" air space in front of the house wrap, through-wall base flashing with a drip and cell-vent weeps every 32"
    /// in the first course, corrugated ties nailed to the studs every 16" horizontally / 9 courses (23-5/8") vertically,
    /// rowlock sills under windows and steel angle lintels (6" bearing) over openings.
    /// Scripted: <see cref="LayAll"/>, or BaseFlashing / TieRow / LayCourse (one course per call).
    /// Bricks/mortar come from the site stock ("brick-modular" or "brick-used", "mortar-s80": ~40 brick per 80 lb bag).
    /// </summary>
    public static class BrickVeneer
    {
        public static readonly float BrickL = In(7.625f), BrickH = In(2.25f), BrickD = In(3.625f), Joint = In(0.375f),
            Course = In(2.625f), Module = In(8f), AirSpace = In(1f), WeepSpacing = In(32f), Bearing = In(6f);
        public const int TieEvery = 9, FirstTieCourse = 4, LiftCourses = 6;
        public const float BricksPerBag = 40f;

        public class State
        {
            public string BrickId = ExteriorCatalog.Brick;
            public float Air = In(1f);
            public bool Flashing;
            public int Courses;
            public float BaseV, TopV;
            public readonly List<float> TieRows = new List<float>();     // v of tie rows (bed joints)
            public readonly List<Vector2> Ties = new List<Vector2>();     // (u, v)
            public readonly List<float> Weeps = new List<float>();       // u of weeps in course 1
            public readonly Dictionary<int, float> Lintels = new Dictionary<int, float>(); // opening -> bearing each side
            public readonly List<GameObject> Lifts = new List<GameObject>();
            public float MortarUsed;
            public int MaxCourses;
            public bool Complete => Courses >= MaxCourses && MaxCourses > 0;
        }

        static State S(ExteriorWall w)
        {
            if (w.Brick == null)
            {
                var s = w.Brick = new State();
                s.BaseV = w.B.yMin;
                s.TopV = SidingPrep.TopLimit(w);
                int n = 0; while (BottomOf(s, n) + BrickH <= s.TopV - In(0.25f)) n++;
                s.MaxCourses = n;
            }
            return w.Brick;
        }

        /// <summary>Choose the brick (before the first course): "brick-modular" or "brick-used".</summary>
        public static void UseBrick(ExteriorWall w, string id) => S(w).BrickId = id;

        public static State StateOf(ExteriorWall w) => S(w);

        public static float BottomOf(State s, int i) => s.BaseV + Joint + i * Course;
        static float ZBack(ExteriorWall w) => w.WrapZ - S(w).Air;
        static float ZFront(ExteriorWall w) => ZBack(w) - BrickD;

        // ================================================================== scripted API

        /// <summary>Whole wall: base flashing + weeps, then every course with tie rows nailed as the wall passes their heights.</summary>
        public static bool LayAll(ExteriorWall w, string brickId = ExteriorCatalog.Brick)
        {
            if (w == null || !SidingPrep.Ready(w, SidingKind.Brick)) return false;
            var s = S(w);
            if (s.Courses == 0) s.BrickId = brickId;
            if (!s.Flashing && !BaseFlashing(w)) return false;
            int guard = 0;
            while (!s.Complete && guard++ < 200)
            {
                if (TieRowDue(w, out _) && !TieRow(w)) return false;
                if (!LayCourse(w)) return false;
            }
            return true;
        }

        /// <summary>Through-wall flashing on the brick ledge, up behind the house wrap, with a drip past the brick face.</summary>
        public static bool BaseFlashing(ExteriorWall w)
        {
            if (!SidingPrep.Ready(w, SidingKind.Brick)) return false;
            var s = S(w);
            if (s.Flashing) { Game.Toast("Base flashing is already in"); return false; }
            if (!FinishStock.Take(ExteriorCatalog.ThroughWallFlash, Mathf.CeilToInt(w.B.width / (33f * Units.Foot)))) return false;
            s.Flashing = true;
            RebuildStatic(w);
            Game.Toast("Through-wall flashing on the ledge, lapped under the house wrap, drip 3/8\" past the face");
            return true;
        }

        /// <summary>Is a tie row needed before the next course? (Ties go in the bed joint every 9 courses.)</summary>
        public static bool TieRowDue(ExteriorWall w, out float v)
        {
            var s = S(w); int i = s.Courses;
            v = BottomOf(s, i) - Joint * 0.5f;
            if (i < FirstTieCourse || (i - FirstTieCourse) % TieEvery != 0) return false;
            foreach (var r in s.TieRows) if (Mathf.Abs(r - v) < In(0.5f)) return false;
            return true;
        }

        /// <summary>Nails the next row of corrugated ties to the studs (16" O.C.) at the next tie height.</summary>
        public static bool TieRow(ExteriorWall w)
        {
            var s = S(w);
            if (!s.Flashing) { Game.Toast("Base flashing goes in first"); return false; }
            int i = s.Courses;
            while (i < s.MaxCourses && (i < FirstTieCourse || (i - FirstTieCourse) % TieEvery != 0)) i++;
            float v = BottomOf(s, i) - Joint * 0.5f;
            foreach (var r in s.TieRows) if (Mathf.Abs(r - v) < In(0.5f)) { Game.Toast("That tie row is already nailed"); return false; }
            var us = new List<float>();
            foreach (var u in w.StudLines) if (u > w.B.xMin + In(1f) && u < w.B.xMax - In(1f) && !InHole(w, new Vector2(u, v))) us.Add(u);
            if (!FinishStock.Take(ExteriorCatalog.BrickTies, us.Count)) return false;
            s.TieRows.Add(v);
            foreach (var u in us) s.Ties.Add(new Vector2(u, v));
            RebuildStatic(w);
            Game.Toast($"{us.Count} brick ties nailed to the studs at course {i + 1} (16\" O.C., 24\" max vertical)");
            return true;
        }

        /// <summary>Lays one course (mortar bed, bricks buttered and set to the line, joints tooled concave).</summary>
        public static bool LayCourse(ExteriorWall w)
        {
            if (!SidingPrep.Ready(w, SidingKind.Brick)) return false;
            var s = S(w);
            if (!s.Flashing) { Game.Toast("Install the base flashing first (the first course sits on it, with weeps)"); return false; }
            if (s.Complete) { Game.Toast("Veneer is up to the frieze/soffit"); return false; }
            if (TieRowDue(w, out _)) { Game.Toast("Nail a row of brick ties at this height first (16\" O.C., 24\" max vertical)"); return false; }
            int i = s.Courses;
            var bricks = CourseBricks(w, i);
            int n = bricks.Count;
            float bags = (s.MortarUsed + n / BricksPerBag);
            int newBags = Mathf.FloorToInt(bags) - Mathf.FloorToInt(s.MortarUsed);
            if (!FinishStock.Has(s.BrickId, n)) { FinishStock.Take(s.BrickId, n); return false; }
            if (newBags > 0 && !FinishStock.Has(ExteriorCatalog.Mortar, newBags)) { FinishStock.Take(ExteriorCatalog.Mortar, newBags); return false; }
            if (i == 0)
            {
                int weeps = 0; foreach (var bk in bricks) if (bk.Weep) weeps++;
                if (!FinishStock.Take(ExteriorCatalog.Weeps, weeps)) return false;
                s.Weeps.Clear(); foreach (var bk in bricks) if (bk.Weep) s.Weeps.Add(bk.WeepU);
            }
            // lintels / rowlock sills reached by this course
            for (int o = 0; o < w.Openings.Count; o++)
            {
                if (s.Lintels.ContainsKey(o)) continue;
                if (BottomOf(s, i) + BrickH > LintelV(w, o) - In(0.3f) && BottomOf(s, i) < LintelV(w, o) + Course)
                {
                    if (!FinishStock.Take(ExteriorCatalog.Lintel)) return false;
                    s.Lintels[o] = Bearing;
                }
            }
            FinishStock.Take(s.BrickId, n);
            if (newBags > 0) FinishStock.Take(ExteriorCatalog.Mortar, newBags);
            s.MortarUsed = bags;
            s.Courses++;
            RebuildLift(w, i / LiftCourses);
            RebuildStatic(w);
            Game.Toast($"Course {s.Courses}/{s.MaxCourses}: {n} brick{(i == 0 ? $", {s.Weeps.Count} weeps" : "")} — joints struck concave");
            return true;
        }

        // ================================================================== layout

        struct Brk { public float U0, U1, V0, V1; public bool Weep; public float WeepU; public bool HeadL, HeadR; }

        /// <summary>v of the lintel's bearing (brick bottom resting on its horizontal leg) for an opening.</summary>
        public static float LintelV(ExteriorWall w, int o)
        {
            var s = S(w); var r = w.Openings[o];
            for (int i = 0; i < 400; i++) { float b = BottomOf(s, i); if (b - In(0.25f) >= r.yMax + In(0.1f)) return b; }
            return r.yMax + Course;
        }

        static float RowlockBed(ExteriorWall w, int o) => w.Openings[o].yMin - BrickD - Joint;

        static bool InHole(ExteriorWall w, Vector2 uv)
        {
            for (int o = 0; o < w.Openings.Count; o++)
            {
                var r = w.Openings[o];
                if (uv.x > r.xMin && uv.x < r.xMax && uv.y > RowlockBed(w, o) - Joint && uv.y < LintelV(w, o)) return true;
            }
            return false;
        }

        static List<Brk> CourseBricks(ExteriorWall w, int i)
        {
            var s = S(w);
            var res = new List<Brk>();
            float v0 = BottomOf(s, i), v1 = v0 + BrickH;
            float uA = w.B.xMin, uB = w.B.xMax;
            float off = (i % 2 == 0) ? 0f : Module * 0.5f;
            float first = uA - off;
            int k = 0;
            for (float jc = first; jc < uB; jc += Module, k++)
            {
                float a = jc + Joint * 0.5f, b = jc + Module - Joint * 0.5f;
                float ca = Mathf.Max(a, uA), cb = Mathf.Min(b, uB);
                if (cb - ca < In(1.5f)) continue;
                // split at opening hole u-ranges; inside, clip vertically outside the hole
                var parts = new List<Vector4> { new Vector4(ca, cb, v0, v1) };
                for (int o = 0; o < w.Openings.Count; o++)
                {
                    var r = w.Openings[o];
                    float hb = RowlockBed(w, o) - Joint, ht = LintelV(w, o);
                    var next = new List<Vector4>();
                    foreach (var p in parts)
                    {
                        if (p.y <= r.xMin || p.x >= r.xMax || p.w <= hb || p.z >= ht) { next.Add(p); continue; }
                        if (p.x < r.xMin) next.Add(new Vector4(p.x, r.xMin, p.z, p.w));
                        if (p.y > r.xMax) next.Add(new Vector4(r.xMax, p.y, p.z, p.w));
                        float ix0 = Mathf.Max(p.x, r.xMin), ix1 = Mathf.Min(p.y, r.xMax);
                        if (hb - p.z > In(0.6f)) next.Add(new Vector4(ix0, ix1, p.z, Mathf.Min(p.w, hb)));
                        if (p.w - ht > In(0.6f)) next.Add(new Vector4(ix0, ix1, Mathf.Max(p.z, ht), p.w));
                    }
                    parts = next;
                }
                foreach (var p in parts)
                {
                    if (p.y - p.x < In(1f)) continue;
                    var bk = new Brk { U0 = p.x, U1 = p.y, V0 = p.z, V1 = p.w, HeadL = p.x > uA + 1e-4f, HeadR = p.y < uB - 1e-4f };
                    // weeps: open head joints with cell vents every 32" in the first course
                    if (i == 0 && k % 4 == 2 && bk.HeadR && Mathf.Abs(p.y - b) < 1e-4f) { bk.Weep = true; bk.WeepU = b + Joint * 0.5f; }
                    res.Add(bk);
                }
            }
            return res;
        }

        // ================================================================== visuals

        static Material[] BrickMats(State s)
        {
            var kind = s.BrickId == ExteriorCatalog.UsedBrick ? SurfaceKind.UsedBrick : SurfaceKind.Brick;
            return new[]
            {
                Materials.Get(kind), Materials.Get(kind, new Color(0.90f, 0.86f, 0.84f)), Materials.Get(kind, new Color(1.07f, 0.98f, 0.94f)),
                Materials.Get(kind, new Color(0.80f, 0.74f, 0.74f)), Materials.Get(SurfaceKind.Mortar, new Color(0.97f, 0.95f, 0.92f)),
                Materials.Get(SurfaceKind.DarkPlastic, new Color(2.6f, 2.6f, 2.5f)), Materials.Get(SurfaceKind.MatteBlack, new Color(0.3f, 0.3f, 0.3f)),
            };
        }

        static void RebuildLift(ExteriorWall w, int lift)
        {
            var s = S(w);
            while (s.Lifts.Count <= lift) s.Lifts.Add(null);
            if (s.Lifts[lift] != null) Object.Destroy(s.Lifts[lift]);
            var b = new MeshBuilder();
            float zf = ZFront(w), zk = ZBack(w);
            int i0 = lift * LiftCourses, i1 = Mathf.Min(s.Courses, i0 + LiftCourses);
            var rot = Quaternion.LookRotation(Vector3.right, Vector3.up);
            for (int i = i0; i < i1; i++)
            {
                var bricks = CourseBricks(w, i);
                float v0 = BottomOf(s, i);
                foreach (var bk in bricks)
                {
                    uint h = FinishNoise.Hash(Mathf.RoundToInt(bk.U0 * 1000f), i, 77);
                    int tint = (int)(h % 4u);
                    b.UvOffset = new Vector2((h >> 4) % 1000 / 1000f * 0.6f, (h >> 14) % 1000 / 1000f * 0.6f);
                    float bev = Mathf.Min(In(0.12f), (bk.V1 - bk.V0) * 0.2f);
                    b.AddBevelBox(new Vector3((bk.U0 + bk.U1) * 0.5f, (bk.V0 + bk.V1) * 0.5f, (zf + zk) * 0.5f), new Vector3(BrickD, bk.V1 - bk.V0, bk.U1 - bk.U0), rot, bev, tint, tint);
                    b.UvOffset = Vector2.zero;
                    // head joint to the right (concave), or an open weep with a cell vent
                    if (bk.HeadR)
                    {
                        float x0 = bk.U1, x1 = bk.U1 + Joint;
                        if (bk.Weep)
                        {
                            Box(b, x0, x1, bk.V0, bk.V1, zf + In(0.12f), zk, 5);
                            Front(b, x0 + In(0.06f), x1 - In(0.06f), bk.V0 + In(0.15f), bk.V1 - In(0.15f), zf + In(0.11f), 6);
                        }
                        else ConcaveV(b, x0, x1, bk.V0, bk.V1, zf, 4);
                    }
                }
                // bed joint under this course (where bricks sit)
                foreach (var bk in bricks) if (Mathf.Abs(bk.V0 - v0) < 1e-4f) ConcaveH(b, bk.U0 - (bk.HeadL ? Joint : 0f), bk.U1, v0 - Joint, v0, zf, 4);
            }
            // rowlock sills + lintels reached by these courses
            for (int o = 0; o < w.Openings.Count; o++)
            {
                var r = w.Openings[o];
                float bed = RowlockBed(w, o);
                int j = Mathf.FloorToInt((bed - s.BaseV) / Course);
                if (j >= i0 && j < i1) Rowlock(b, w, r, bed, zf, zk);
            }
            var mesh = Build(b, "BrickLift", 7);
            var go = w.Part("brick", "BrickLift_" + (lift + 1), mesh, BrickMats(s), "brick", lift);
            s.Lifts[lift] = go;
        }

        static void Rowlock(MeshBuilder b, ExteriorWall w, Rect r, float bed, float zf, float zk)
        {
            float pitch = BrickH + Joint;
            int n = Mathf.Max(1, Mathf.RoundToInt((r.width + Joint) / pitch));
            float bw = (r.width + Joint) / n - Joint;
            float proj = In(1f), len = (zk - zf) + proj;
            float slope = 10f;
            for (int k = 0; k < n; k++)
            {
                float u0 = r.xMin + k * (bw + Joint);
                uint h = FinishNoise.Hash(k, Mathf.RoundToInt(bed * 1000f), 91);
                int tint = (int)(h % 4u);
                b.UvOffset = new Vector2((h >> 4) % 1000 / 1000f * 0.6f, 0f);
                // brick on edge: length runs into the wall, tilted down toward the front
                var rot = Quaternion.AngleAxis(-slope, Vector3.right) * Quaternion.LookRotation(Vector3.forward, Vector3.up);
                var c = new Vector3(u0 + bw * 0.5f, bed + BrickD * 0.5f + In(0.15f), zk - len * 0.5f);
                b.AddBevelBox(c, new Vector3(bw, BrickD, len), rot, In(0.1f), tint, tint);
                b.UvOffset = Vector2.zero;
                if (k < n - 1) ConcaveV(b, u0 + bw, u0 + bw + Joint, bed, bed + BrickD, zf - proj * 0.8f, 4);
            }
            ConcaveH(b, r.xMin, r.xMax, bed - Joint, bed, zf, 4);
        }

        /// <summary>Tooled concave joint across v (bed joint): recessed ~1/8" at the middle.</summary>
        static void ConcaveH(MeshBuilder b, float x0, float x1, float y0, float y1, float zf, int sub)
        {
            if (x1 - x0 < 1e-4f) return;
            float[] ys = { y0, y0 + (y1 - y0) * 0.3f, y0 + (y1 - y0) * 0.7f, y1 };
            float[] zs = { zf + In(0.04f), zf + In(0.12f), zf + In(0.12f), zf + In(0.04f) };
            for (int k = 0; k < 3; k++)
                b.AddQuad(new Vector3(x0, ys[k], zs[k]), new Vector3(x1, ys[k], zs[k]), new Vector3(x1, ys[k + 1], zs[k + 1]), new Vector3(x0, ys[k + 1], zs[k + 1]), sub, Out, Vector3.right);
        }

        static void ConcaveV(MeshBuilder b, float x0, float x1, float y0, float y1, float zf, int sub)
        {
            float[] xs = { x0, x0 + (x1 - x0) * 0.3f, x0 + (x1 - x0) * 0.7f, x1 };
            float[] zs = { zf + In(0.04f), zf + In(0.12f), zf + In(0.12f), zf + In(0.04f) };
            for (int k = 0; k < 3; k++)
                b.AddQuad(new Vector3(xs[k], y0, zs[k]), new Vector3(xs[k + 1], y0, zs[k + 1]), new Vector3(xs[k + 1], y1, zs[k + 1]), new Vector3(xs[k], y1, zs[k]), sub, Out, Vector3.up);
        }

        /// <summary>Base flashing, ties, lintels, brick-mould fillers, mason's line.</summary>
        static void RebuildStatic(ExteriorWall w)
        {
            w.ClearLayer("brick-static");
            var s = S(w);
            var b = new MeshBuilder();
            float zf = ZFront(w), zk = ZBack(w), wz = w.WrapZ;
            const int sFlash = 0, sGalv = 1, sSteel = 2, sTrim = 3, sLine = 4;
            if (s.Flashing)
            {
                Box(b, w.B.xMin, w.B.xMax, s.BaseV - In(0.04f), s.BaseV, zf - In(0.375f), wz, sFlash);
                Box(b, w.B.xMin, w.B.xMax, s.BaseV, s.BaseV + In(8f), -0.0004f, -0.0002f, sFlash);
                Box(b, w.B.xMin, w.B.xMax, s.BaseV - In(0.45f), s.BaseV - In(0.04f), zf - In(0.4f), zf - In(0.375f), sGalv);
            }
            foreach (var t in s.Ties)
            {
                // nailed leg on the wall + horizontal leg across the air space into the bed joint (2" embedment)
                Box(b, t.x - In(0.44f), t.x + In(0.44f), t.y - In(0.5f), t.y + In(1.0f), wz - In(0.04f), wz, sGalv);
                Box(b, t.x - In(0.44f), t.x + In(0.44f), t.y - In(0.015f), t.y + In(0.015f), zk - In(2f), wz, sGalv);
                for (int c = -1; c <= 1; c += 2) Box(b, t.x + c * In(0.3f) - In(0.04f), t.x + c * In(0.3f) + In(0.04f), t.y - In(0.015f), t.y + In(0.05f), zk - In(2f), wz, sGalv);
                b.AddCylinder(new Vector3(t.x, t.y + In(0.6f), wz - In(0.06f)), Quaternion.Euler(-90f, 0f, 0f), In(0.14f), In(0.04f), 8, sGalv);
            }
            for (int o = 0; o < w.Openings.Count; o++)
            {
                if (!s.Lintels.TryGetValue(o, out float bear)) continue;
                var r = w.Openings[o];
                float lv = LintelV(w, o);
                // L 3-1/2 x 3-1/2 x 1/4: horizontal leg under the brick, vertical leg up the back
                Box(b, r.xMin - bear, r.xMax + bear, lv - In(0.25f), lv, zf + In(0.125f), zf + In(3.625f), sSteel);
                Box(b, r.xMin - bear, r.xMax + bear, lv - In(0.25f), lv + In(3.25f), zk - In(0.25f), zk, sSteel);
                // brick-mould filler between window head and lintel
                if (lv - In(0.25f) - r.yMax > In(0.1f)) Box(b, r.xMin, r.xMax, r.yMax, lv - In(0.25f), zf + In(0.6f), wz, sTrim);
            }
            if (s.Flashing && !s.Complete)
            {
                float lv = BottomOf(s, s.Courses) + BrickH;
                b.AddRod(new Vector3((w.B.xMin + w.B.xMax) * 0.5f, lv, zf - In(0.06f)), Quaternion.LookRotation(Vector3.right, Vector3.up), In(0.035f), w.B.width + In(8f), 5, sLine);
                for (int e = 0; e < 2; e++)
                {
                    float x = e == 0 ? w.B.xMin - In(1.5f) : w.B.xMax + In(1.5f);
                    Box(b, x - In(1.5f), x + In(1.5f), lv - In(0.75f), lv + In(0.75f), zf - In(0.5f), zf + In(1.5f), sTrim);
                }
            }
            var mesh = Build(b, "BrickStatic", 5);
            w.Part("brick-static", "BrickStatic", mesh, new[]
            {
                FinishMaterials.Get(ExteriorLooks.FlashTape), Materials.Get(SurfaceKind.Galvanized), Materials.Get(SurfaceKind.PaintedSteel, new Color(0.35f, 0.33f, 0.32f)),
                FinishMaterials.Get(ExteriorLooks.Trim), FinishMaterials.Get("paint-smooth", new Color(1f, 0.92f, 0.1f)),
            }, "brick-static", 0, false);
        }

        /// <summary>Course index at face point (or -1).</summary>
        public static int CourseAt(ExteriorWall w, Vector2 uv)
        {
            if (w.Brick == null) return -1;
            int i = Mathf.FloorToInt((uv.y - w.Brick.BaseV) / Course);
            return i >= 0 && i < w.Brick.Courses ? i : -1;
        }
    }
}
