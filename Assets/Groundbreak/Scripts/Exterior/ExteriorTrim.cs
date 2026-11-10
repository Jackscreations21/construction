using System.Collections.Generic;
using UnityEngine;
using static Groundbreak.ExtGeo;

namespace Groundbreak
{
    /// <summary>
    /// Exterior trim (5/4x4 corner boards and window/door casing with a drip cap) for fiber cement and panel siding,
    /// plus the sealant joints of every sided wall: siding-to-trim butt joints and window/door perimeters.
    /// Scripted: <see cref="CornerBoards"/>, <see cref="CaseOpenings"/>, <see cref="CaulkAll"/>; in-game the shared
    /// caulk gun's "Exterior sealant (white)" tube calls <see cref="ApplySealant"/>.
    /// </summary>
    public static class ExteriorTrim
    {
        public static readonly float T = In(1f), Wd = In(3.5f), PlankGap = In(0.125f);

        public class Board { public Rect R; public string Kind; }
        public class Joint { public Vector2 A, B; public string What; public bool Caulked; public bool SidingJoint; public float Proud; }
        public class State
        {
            public bool Corners, Casings;
            public readonly List<Board> Boards = new List<Board>();
            public readonly List<Joint> Joints = new List<Joint>();
        }

        static float Zb(ExteriorWall w) => w.WrapZ - 0.0005f;
        static float TrimTop(ExteriorWall w) => SidingPrep.TopLimit(w);
        static float TrimBottom(ExteriorWall w) => Mathf.Max(w.B.yMin - In(1f), ExteriorSite.GradeY + In(6f));

        /// <summary>5/4x4 corner boards at both ends of the wall (with the return board on the wall end).</summary>
        public static bool CornerBoards(ExteriorWall w)
        {
            if (!HouseWrap.Covered(w)) { Game.Toast("Wrap the wall before the trim"); return false; }
            var s = w.Trim;
            if (s.Corners) { Game.Toast("Corner boards are already on"); return false; }
            float h = TrimTop(w) - TrimBottom(w);
            if (!FinishStock.Take(ExteriorCatalog.TrimBoard, 2 * Mathf.CeilToInt(h / (12f * Units.Foot)) * 2)) return false;
            s.Corners = true;
            float y0 = TrimBottom(w), y1 = TrimTop(w);
            s.Boards.Add(new Board { R = Rect.MinMaxRect(w.B.xMin, y0, w.B.xMin + Wd, y1), Kind = "corner" });
            s.Boards.Add(new Board { R = Rect.MinMaxRect(w.B.xMax - Wd, y0, w.B.xMax, y1), Kind = "corner" });
            s.Joints.Add(new Joint { A = new Vector2(w.B.xMin + Wd, y0), B = new Vector2(w.B.xMin + Wd, y1), What = "siding to corner board", SidingJoint = true, Proud = T });
            s.Joints.Add(new Joint { A = new Vector2(w.B.xMax - Wd, y0), B = new Vector2(w.B.xMax - Wd, y1), What = "siding to corner board", SidingJoint = true, Proud = T });
            Rebuild(w);
            Game.Toast("Corner boards nailed (two 5/4x4, lapped at the corner)");
            return true;
        }

        /// <summary>Casing around every opening: jambs, head with a metal drip cap, and a sill board.</summary>
        public static bool CaseOpenings(ExteriorWall w)
        {
            if (!HouseWrap.Covered(w)) { Game.Toast("Wrap and flash the openings before casing them"); return false; }
            for (int i = 0; i < w.Openings.Count; i++) if (!w.Wrap.Op(i).Flashed) { Game.Toast("Flash the openings before casing them"); return false; }
            var s = w.Trim;
            if (s.Casings) { Game.Toast("Openings are already cased"); return false; }
            int boards = 0; foreach (var r in w.Openings) boards += Mathf.CeilToInt((2f * r.height + 2f * r.width + 4f * Wd) / (12f * Units.Foot));
            if (!FinishStock.Take(ExteriorCatalog.TrimBoard, Mathf.Max(1, boards))) return false;
            s.Casings = true;
            foreach (var r in w.Openings)
            {
                s.Boards.Add(new Board { R = Rect.MinMaxRect(r.xMin - Wd, r.yMin, r.xMin, r.yMax), Kind = "jamb" });
                s.Boards.Add(new Board { R = Rect.MinMaxRect(r.xMax, r.yMin, r.xMax + Wd, r.yMax), Kind = "jamb" });
                s.Boards.Add(new Board { R = Rect.MinMaxRect(r.xMin - Wd, r.yMax, r.xMax + Wd, r.yMax + Wd), Kind = "head" });
                if (r.yMin > w.B.yMin + In(6f)) s.Boards.Add(new Board { R = Rect.MinMaxRect(r.xMin - Wd - In(1f), r.yMin - Wd, r.xMax + Wd + In(1f), r.yMin), Kind = "sill" });
                s.Joints.Add(new Joint { A = new Vector2(r.xMin - Wd, r.yMin - Wd), B = new Vector2(r.xMin - Wd, r.yMax + Wd), What = "siding to casing", SidingJoint = true, Proud = T });
                s.Joints.Add(new Joint { A = new Vector2(r.xMax + Wd, r.yMin - Wd), B = new Vector2(r.xMax + Wd, r.yMax + Wd), What = "siding to casing", SidingJoint = true, Proud = T });
            }
            Rebuild(w);
            Game.Toast("Openings cased, drip cap over each head");
            return true;
        }

        /// <summary>Window/door perimeter joints (frame to J-channel / casing / brick) for the wall's current siding.</summary>
        public static void EnsurePerimeterJoints(ExteriorWall w)
        {
            var s = w.Trim;
            if (s.Joints.Exists(j => j.What == "window perimeter")) return;
            float p = w.Siding == SidingKind.Brick ? BrickVeneer.BrickD + w.Brick.Air : w.Siding == SidingKind.Vinyl ? VinylSiding.JProud : T;
            foreach (var r in w.Openings)
            {
                s.Joints.Add(new Joint { A = new Vector2(r.xMin, r.yMin), B = new Vector2(r.xMax, r.yMin), What = "window perimeter", Proud = p });
                s.Joints.Add(new Joint { A = new Vector2(r.xMax, r.yMin), B = new Vector2(r.xMax, r.yMax), What = "window perimeter", Proud = p });
                s.Joints.Add(new Joint { A = new Vector2(r.xMin, r.yMax), B = new Vector2(r.xMax, r.yMax), What = "window perimeter", Proud = p });
                s.Joints.Add(new Joint { A = new Vector2(r.xMin, r.yMin), B = new Vector2(r.xMin, r.yMax), What = "window perimeter", Proud = p });
            }
        }

        /// <summary>Joints that need sealant now (siding joints only once siding is on; perimeters once sided).</summary>
        public static List<Joint> ActiveJoints(ExteriorWall w)
        {
            var l = new List<Joint>();
            bool sided = Sided(w);
            if (!sided) return l;
            EnsurePerimeterJoints(w);
            foreach (var j in w.Trim.Joints)
            {
                if (j.SidingJoint && (w.Siding == SidingKind.Vinyl || w.Siding == SidingKind.Brick)) continue;
                l.Add(j);
            }
            return l;
        }

        public static bool Sided(ExteriorWall w) =>
            (w.Siding == SidingKind.Vinyl && w.Vinyl != null && w.Vinyl.Courses.Count > 0) ||
            (w.Siding == SidingKind.FiberCement && w.FC != null && w.FC.Courses.Count > 0) ||
            (w.Siding == SidingKind.BoardBatten && w.BB != null && w.BB.Panels) ||
            (w.Siding == SidingKind.Brick && w.Brick != null && w.Brick.Courses > 0);

        /// <summary>Seals every open joint on the wall (tubes from stock: one 10 oz tube ≈ 25 ft of 1/4" bead).</summary>
        public static int CaulkAll(ExteriorWall w)
        {
            int n = 0; float feet = 0f;
            foreach (var j in ActiveJoints(w)) if (!j.Caulked) feet += (j.B - j.A).magnitude / Units.Foot;
            if (feet <= 0f) return 0;
            if (!FinishStock.Take(ExteriorCatalog.Sealant, Mathf.CeilToInt(feet / CaulkGun.FeetPerTube))) return 0;
            foreach (var j in ActiveJoints(w)) if (!j.Caulked) { Seal(w, j); n++; }
            Game.Toast($"Sealed {n} joints ({feet:F0} ft of bead)");
            return n;
        }

        /// <summary>Caulk gun handler: seals the open joint nearest the aim point.</summary>
        public static bool ApplySealant(RaycastHit hit, CaulkGun gun)
        {
            var w = ExteriorWall.FromHit(hit);
            if (w == null) { Game.Toast("Aim at an exterior trim joint or window perimeter"); return false; }
            var uv = w.ToUV(hit.point);
            Joint best = null; float bd = 0.35f;
            foreach (var j in ActiveJoints(w))
            {
                if (j.Caulked) continue;
                float d = DistSeg(uv, j.A, j.B);
                if (d < bd) { bd = d; best = j; }
            }
            if (best == null) { Game.Toast("No open joint here — sealant goes where siding meets trim and around windows"); return false; }
            if (!gun.Consume((best.B - best.A).magnitude)) return false;
            Seal(w, best);
            Game.Toast($"Sealed: {best.What}");
            return true;
        }

        static void Seal(ExteriorWall w, Joint j)
        {
            j.Caulked = true;
            float z = Zb(w) - j.Proud + In(0.05f);
            Bead.Add(w.L2W(new Vector3(j.A.x, j.A.y, z)), w.L2W(new Vector3(j.B.x, j.B.y, z)), new Color(0.95f, 0.95f, 0.93f), In(0.25f), w.Layer("caulk"), "ext-sealant");
        }

        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Plank/panel cut-outs around trim (openings + casing, with the 1/8" caulk gap).</summary>
        public static List<Rect> Holes(ExteriorWall w)
        {
            var l = new List<Rect>();
            foreach (var r in w.Openings)
            {
                bool cased = w.Trim.Casings;
                float g = cased ? Wd + PlankGap : 0f;
                l.Add(Rect.MinMaxRect(r.xMin - g, r.yMin - (cased && r.yMin > w.B.yMin + In(6f) ? Wd + PlankGap : 0f), r.xMax + g, r.yMax + (cased ? Wd + PlankGap : 0f)));
            }
            return l;
        }

        /// <summary>Siding run between corner boards (or the wall ends).</summary>
        public static Vector2 Run(ExteriorWall w) => w.Trim.Corners ? new Vector2(w.B.xMin + Wd + PlankGap, w.B.xMax - Wd - PlankGap) : new Vector2(w.B.xMin, w.B.xMax);

        public static void Rebuild(ExteriorWall w)
        {
            w.ClearLayer("trim");
            var s = w.Trim;
            var b = new MeshBuilder();
            float zb = Zb(w);
            foreach (var bd in s.Boards)
            {
                bool vert = bd.R.height > bd.R.width;
                Box(b, bd.R.xMin, bd.R.xMax, bd.R.yMin, bd.R.yMax, zb - T, zb, 0, vert ? Vector3.up : Vector3.right);
                if (bd.Kind == "head")
                {
                    // drip cap: over the head casing, up the wall behind the wrap line, bent down over the face
                    Box(b, bd.R.xMin - In(0.25f), bd.R.xMax + In(0.25f), bd.R.yMax, bd.R.yMax + In(0.03f), zb - T - In(0.25f), zb, 1);
                    Box(b, bd.R.xMin - In(0.25f), bd.R.xMax + In(0.25f), bd.R.yMax - In(0.4f), bd.R.yMax + In(0.03f), zb - T - In(0.28f), zb - T - In(0.25f), 1);
                    Box(b, bd.R.xMin - In(0.25f), bd.R.xMax + In(0.25f), bd.R.yMax, bd.R.yMax + In(2f), zb + 0.0003f, zb + 0.0006f, 1);
                }
                if (bd.Kind == "corner")
                {
                    // return board on the wall end (5/4x4 overlapping)
                    bool left = bd.R.xMin <= w.B.xMin + 1e-4f;
                    float x0 = left ? w.B.xMin - T : w.B.xMax, x1 = x0 + T;
                    Box(b, x0, x1, bd.R.yMin, bd.R.yMax, zb - T, zb - T + Wd, 0, Vector3.up);
                }
            }
            if (b.VertexCount == 0) return;
            var mesh = Build(b, "ExteriorTrim", 2);
            w.Part("trim", "ExteriorTrim", mesh, new[] { FinishMaterials.Get(ExteriorLooks.Trim), FinishMaterials.Get(ExteriorLooks.Alum) }, "trim");
        }
    }
}
