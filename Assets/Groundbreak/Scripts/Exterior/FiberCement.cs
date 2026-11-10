using System.Collections.Generic;
using UnityEngine;
using static Groundbreak.ExtGeo;

namespace Groundbreak
{
    /// <summary>
    /// Fiber cement lap siding: 8-1/4" x 12' x 5/16" planks, 7" exposure (1-1/4" lap), blind-nailed 1" below the top
    /// edge into every stud, butt joints centered on studs with a 6" flashing behind, staggered course to course, 1/8"
    /// gap at trim (sealed). Starts on a 1/4" starter strip at least 6" above grade; stays 2" clear of roofing.
    /// Scripted: <see cref="InstallAll"/> (trim + starter + all courses) or Starter / HangCourse.
    /// </summary>
    public static class FiberCement
    {
        public static readonly float PlankH = In(8.25f), Thick = In(0.3125f), PlankLen = In(144f), NailDown = In(1f), Lath = In(0.25f),
            JointGap = In(0.0625f), MinGrade = In(6f), MinRoof = In(2f);

        public class Plank { public float U0, U1; }
        public class Course
        {
            public int Index; public float V0;
            public readonly List<Plank> Planks = new List<Plank>();
            public readonly List<Vector2> Nails = new List<Vector2>();
            public readonly List<float> Joints = new List<float>();     // butt joint u
            public readonly List<bool> JointFlashed = new List<bool>();
            public GameObject Go;
        }
        public class State
        {
            public string ColorName = "Arctic white";
            public float Exposure = In(7f);
            public bool Starter, Complete;
            public float BottomV, TopLimit, RunL, RunR;
            public readonly List<Course> Courses = new List<Course>();
        }

        static State S(ExteriorWall w)
        {
            if (w.FC == null)
            {
                var s = w.FC = new State();
                s.BottomV = Mathf.Max(w.B.yMin - In(1f), ExteriorSite.GradeY + MinGrade);
                s.TopLimit = SidingPrep.TopLimit(w);
            }
            var run = ExteriorTrim.Run(w); w.FC.RunL = run.x; w.FC.RunR = run.y;
            return w.FC;
        }

        static float Zb(ExteriorWall w) => w.WrapZ - 0.0005f;

        /// <summary>Corner boards + casing, starter, then every course. colorName: ExteriorLooks.FiberColors.</summary>
        public static bool InstallAll(ExteriorWall w, string colorName = "Arctic white")
        {
            if (w == null || !SidingPrep.Ready(w, SidingKind.FiberCement)) return false;
            S(w).ColorName = colorName;
            if (!w.Trim.Corners && !ExteriorTrim.CornerBoards(w)) return false;
            if (w.Openings.Count > 0 && !w.Trim.Casings && !ExteriorTrim.CaseOpenings(w)) return false;
            if (!S(w).Starter && !Starter(w)) return false;
            int guard = 0;
            while (!S(w).Complete && guard++ < 60) if (HangCourse(w) == null) return S(w).Complete;
            return true;
        }

        /// <summary>1/4" starter (lath) strip so the first course has the same kick-out angle as the rest.</summary>
        public static bool Starter(ExteriorWall w)
        {
            if (!SidingPrep.Ready(w, SidingKind.FiberCement)) return false;
            if (!w.Trim.Corners) { Game.Toast("Install the corner boards first — planks butt into them"); return false; }
            var s = S(w);
            if (s.Starter) return false;
            s.Starter = true;
            Rebuild(w);
            if (s.BottomV > w.B.yMin - In(0.9f)) Game.Toast($"Starter strip set {Units.Format(s.BottomV - ExteriorSite.GradeY)} above grade (6\" minimum clearance)");
            return true;
        }

        /// <summary>Next course: planks butted on stud centers (joints flashed, staggered), blind-nailed at every stud.</summary>
        public static Course HangCourse(ExteriorWall w)
        {
            var s = S(w);
            if (!s.Starter) { Game.Toast("Nail the starter strip first"); return null; }
            if (s.Complete) { Game.Toast("Fiber cement is complete"); return null; }
            int k = s.Courses.Count;
            float v0 = s.BottomV + k * s.Exposure;
            if (v0 + In(1.5f) >= s.TopLimit) { s.Complete = true; return null; }
            var c = new Course { Index = k, V0 = v0 };
            // butt joints on studs, staggered from the two courses below
            var prev = new List<float>();
            for (int j = Mathf.Max(0, k - 2); j < k; j++) prev.AddRange(s.Courses[j].Joints);
            float x = s.RunL;
            while (x < s.RunR - 1e-4f)
            {
                float end = x + PlankLen;
                if (end >= s.RunR) { c.Planks.Add(new Plank { U0 = x, U1 = s.RunR }); break; }
                float best = float.NaN;
                for (int i = w.StudLines.Count - 1; i >= 0; i--)
                {
                    float u = w.StudLines[i];
                    if (u > end || u < x + In(24f)) continue;
                    bool clash = false; foreach (var p in prev) if (Mathf.Abs(p - u) < In(15f)) clash = true;
                    if (!clash) { best = u; break; }
                }
                if (float.IsNaN(best)) best = w.StudLines.FindLast(u => u <= end && u > x + In(24f));
                c.Planks.Add(new Plank { U0 = x, U1 = best - JointGap * 0.5f });
                c.Joints.Add(best); c.JointFlashed.Add(true);
                x = best + JointGap * 0.5f;
            }
            int planks = c.Planks.Count, flashes = c.Joints.Count;
            if (!FinishStock.Take(ExteriorCatalog.FCPlank, planks)) return null;
            if (flashes > 0 && !FinishStock.Take(ExteriorCatalog.JointFlash, flashes)) return null;
            float nv = v0 + PlankH - NailDown;
            var holes = ExteriorTrim.Holes(w);
            foreach (var u in w.StudLines)
            {
                if (u < s.RunL + In(0.5f) || u > s.RunR - In(0.5f)) continue;
                bool inHole = false; foreach (var h in holes) if (h.Contains(new Vector2(u, nv))) inHole = true;
                if (inHole || nv > s.TopLimit - In(0.5f)) continue;
                c.Nails.Add(new Vector2(u, nv));
            }
            FinishStock.Take(ExteriorCatalog.SidingNails, c.Nails.Count);
            s.Courses.Add(c);
            if (v0 + s.Exposure + In(1.5f) >= s.TopLimit) s.Complete = true;
            Rebuild(w);
            Game.Toast($"Fiber cement course {k + 1}: {planks} plank(s), {flashes} flashed butt joint(s), blind-nailed 1\" from the top");
            return c;
        }

        // ================================================================== visuals

        public static void Rebuild(ExteriorWall w)
        {
            w.ClearLayer("fc");
            var s = S(w);
            var b = new MeshBuilder();
            float zb = Zb(w);
            var holes = ExteriorTrim.Holes(w);
            if (s.Starter) Box(b, s.RunL, s.RunR, s.BottomV, s.BottomV + In(1.5f), zb - Lath, zb, 0);
            for (int k = 0; k < s.Courses.Count; k++)
            {
                var c = s.Courses[k];
                float kick = k == 0 ? Lath : Thick;
                float top = Mathf.Min(c.V0 + PlankH, s.TopLimit - In(0.25f));
                foreach (var p in c.Planks)
                {
                    // band split at hole edges; planks tilt: bottom kicked out by the plank below
                    var ys = new List<float> { c.V0, top };
                    foreach (var h in holes) { if (h.yMin > c.V0 && h.yMin < top) ys.Add(h.yMin); if (h.yMax > c.V0 && h.yMax < top) ys.Add(h.yMax); }
                    ys.Sort();
                    float Zf(float v) => zb - Thick - kick * (1f - Mathf.Clamp01((v - c.V0) / PlankH));
                    float Zk(float v) => zb - kick * (1f - Mathf.Clamp01((v - c.V0) / PlankH));
                    for (int j = 0; j + 1 < ys.Count; j++)
                    {
                        float ya = ys[j], yb = ys[j + 1];
                        var cuts = new List<Vector2>();
                        foreach (var h in holes) if (h.yMin < yb - 1e-5f && h.yMax > ya + 1e-5f) cuts.Add(new Vector2(h.xMin, h.xMax));
                        foreach (var iv in Subtract(p.U0, p.U1, cuts))
                        {
                            b.AddQuad(new Vector3(iv.x, ya, Zf(ya)), new Vector3(iv.y, ya, Zf(ya)), new Vector3(iv.y, yb, Zf(yb)), new Vector3(iv.x, yb, Zf(yb)), 1, Out, Vector3.right);
                            b.AddQuad(new Vector3(iv.x, ya, Zk(ya)), new Vector3(iv.x, ya, Zf(ya)), new Vector3(iv.x, yb, Zf(yb)), new Vector3(iv.x, yb, Zk(yb)), 1, Vector3.left, Vector3.up);
                            b.AddQuad(new Vector3(iv.y, ya, Zk(ya)), new Vector3(iv.y, ya, Zf(ya)), new Vector3(iv.y, yb, Zf(yb)), new Vector3(iv.y, yb, Zk(yb)), 1, Vector3.right, Vector3.up);
                            if (j == 0) b.AddQuad(new Vector3(iv.x, ya, Zf(ya)), new Vector3(iv.y, ya, Zf(ya)), new Vector3(iv.y, ya, Zk(ya)), new Vector3(iv.x, ya, Zk(ya)), 1, Vector3.down, Vector3.right);
                            if (j + 2 == ys.Count) b.AddQuad(new Vector3(iv.x, yb, Zf(yb)), new Vector3(iv.y, yb, Zf(yb)), new Vector3(iv.y, yb, Zk(yb)), new Vector3(iv.x, yb, Zk(yb)), 1, Vector3.up, Vector3.right);
                        }
                    }
                }
                // butt-joint flashing behind each joint
                foreach (var u in c.Joints) Box(b, u - In(3f), u + In(3f), c.V0 + In(0.5f), top, zb - 0.0006f, zb - 0.0002f, 2);
                // blind nails (hidden by the next course; visible on the top course until it is covered)
                foreach (var n in c.Nails) b.AddCylinder(new Vector3(n.x, n.y, zb - Thick * 0.4f - In(0.03f)), Quaternion.Euler(-90f, 0f, 0f), In(0.14f), In(0.05f), 8, 3);
            }
            if (b.VertexCount == 0) return;
            var mesh = Build(b, "FiberCement", 4);
            var col = ExteriorLooks.FiberColor(s.ColorName);
            w.Part("fc", "FiberCement", mesh, new[] { FinishMaterials.Get(ExteriorLooks.Trim, new Color(0.6f, 0.6f, 0.6f)), FinishMaterials.Get(ExteriorLooks.FiberCement, col), FinishMaterials.Get(ExteriorLooks.FlashTape), Materials.Get(SurfaceKind.Galvanized) }, "fc-course");
        }
    }

    /// <summary>
    /// Board-and-batten: 4x8 primed panels installed vertically (joints on studs, Z-flashing at any horizontal joint),
    /// then 1x3 battens over every stud line (16" O.C., covering the panel joints), trimmed at corners and openings.
    /// Scripted: <see cref="InstallAll"/> or InstallPanels / InstallBattens.
    /// </summary>
    public static class BoardBatten
    {
        public static readonly float PanelT = In(0.4375f), BattenT = In(0.75f), BattenW = In(2.5f);
        public class State { public string ColorName = "Arctic white"; public bool Panels, Battens; public float BattenSpacing = In(16f); public readonly List<float> BattenU = new List<float>(); }

        static State S(ExteriorWall w) { if (w.BB == null) w.BB = new State(); return w.BB; }

        public static bool InstallAll(ExteriorWall w, string colorName = "Arctic white")
        {
            if (w == null || !SidingPrep.Ready(w, SidingKind.BoardBatten)) return false;
            S(w).ColorName = colorName;
            if (!w.Trim.Corners && !ExteriorTrim.CornerBoards(w)) return false;
            if (w.Openings.Count > 0 && !w.Trim.Casings && !ExteriorTrim.CaseOpenings(w)) return false;
            if (!S(w).Panels && !InstallPanels(w)) return false;
            if (!S(w).Battens && !InstallBattens(w)) return false;
            return true;
        }

        public static bool InstallPanels(ExteriorWall w)
        {
            if (!SidingPrep.Ready(w, SidingKind.BoardBatten)) return false;
            if (!w.Trim.Corners) { Game.Toast("Install the corner boards first"); return false; }
            var s = S(w);
            if (s.Panels) return false;
            var run = ExteriorTrim.Run(w);
            int n = Mathf.CeilToInt((run.y - run.x) / In(48f)) * Mathf.CeilToInt((SidingPrep.TopLimit(w) - w.B.yMin) / In(96f));
            if (!FinishStock.Take(ExteriorCatalog.BBPanel, n)) return false;
            s.Panels = true; Rebuild(w);
            Game.Toast("Panels nailed 6\" O.C. at edges, 12\" in the field — joints land on studs");
            return true;
        }

        public static bool InstallBattens(ExteriorWall w)
        {
            var s = S(w);
            if (!s.Panels) { Game.Toast("Panels go on before the battens"); return false; }
            if (s.Battens) return false;
            var run = ExteriorTrim.Run(w);
            s.BattenU.Clear();
            foreach (var u in w.StudLines) if (u > run.x + BattenW * 0.5f && u < run.y - BattenW * 0.5f) s.BattenU.Add(u);
            float h = SidingPrep.TopLimit(w) - w.B.yMin;
            if (!FinishStock.Take(ExteriorCatalog.Batten, s.BattenU.Count * Mathf.CeilToInt(h / In(96f)))) return false;
            s.Battens = true; Rebuild(w);
            Game.Toast($"{s.BattenU.Count} battens at 16\" O.C. over the studs and panel joints");
            return true;
        }

        public static void Rebuild(ExteriorWall w)
        {
            w.ClearLayer("bb");
            var s = S(w);
            var b = new MeshBuilder();
            float zb = w.WrapZ - 0.0005f;
            var run = ExteriorTrim.Run(w);
            float y0 = Mathf.Max(w.B.yMin - In(1f), ExteriorSite.GradeY + In(6f)), y1 = SidingPrep.TopLimit(w) - In(0.25f);
            var holes = ExteriorTrim.Holes(w);
            if (s.Panels)
            {
                for (float x = run.x; x < run.y - 1e-3f; x += In(48f))
                {
                    var pr = Rect.MinMaxRect(x, y0, Mathf.Min(run.y, x + In(48f)) - In(0.0625f), y1);
                    PanelMesh.AddSlab(b, pr, holes, zb - PanelT, PanelT, 0, -1, 0, Vector3.up);
                }
            }
            if (s.Battens)
                foreach (var u in s.BattenU)
                {
                    var cuts = new List<Vector2>();
                    foreach (var h in holes) if (u + BattenW * 0.5f > h.xMin && u - BattenW * 0.5f < h.xMax) cuts.Add(new Vector2(h.yMin, h.yMax));
                    foreach (var iv in Subtract(y0, y1, cuts))
                        Box(b, u - BattenW * 0.5f, u + BattenW * 0.5f, iv.x, iv.y, zb - PanelT - BattenT, zb - PanelT, 0, Vector3.up);
                }
            if (b.VertexCount == 0) return;
            var mesh = Build(b, "BoardBatten", 1);
            w.Part("bb", "BoardBatten", mesh, new[] { FinishMaterials.Get(ExteriorLooks.PanelSiding, ExteriorLooks.FiberColor(s.ColorName)) }, "bb");
        }
    }
}
