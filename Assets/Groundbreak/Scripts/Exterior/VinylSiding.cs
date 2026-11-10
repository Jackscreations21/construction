using System.Collections.Generic;
using UnityEngine;
using static Groundbreak.ExtGeo;

namespace Groundbreak
{
    /// <summary>Shared checks before any siding goes on a wall.</summary>
    public static class SidingPrep
    {
        /// <summary>Wall is wrapped, seams taped, openings flashed, and not already sided with another product.</summary>
        public static bool Ready(ExteriorWall w, SidingKind kind)
        {
            if (w == null) return false;
            if (w.Siding != SidingKind.None && w.Siding != kind) { Game.Toast($"This wall already has {w.Siding} siding"); return false; }
            if (!HouseWrap.Covered(w)) { Game.Toast("Install the house wrap first (weather-resistive barrier behind all siding)"); return false; }
            if (!HouseWrap.AllSeamsTaped(w)) { Game.Toast("Tape the house wrap seams first"); return false; }
            for (int i = 0; i < w.Openings.Count; i++)
                if (!w.Wrap.Op(i).Flashed) { Game.Toast("Flash the rough openings first (sill, jambs, head)"); return false; }
            w.Siding = kind;
            return true;
        }

        /// <summary>Top of the siding: the soffit underside if an eave was built over this wall, else the wall top.</summary>
        public static float TopLimit(ExteriorWall w)
        {
            float s = Eaves.SoffitUnderside(w);
            return float.IsNaN(s) ? w.B.yMax : Mathf.Min(w.B.yMax, s);
        }
    }

    /// <summary>
    /// Vinyl lap siding, double 4" (D4) profile, 12'6" panels. Sequence: starter strip, outside corner posts, J-channel
    /// (openings + under the soffit), then courses bottom-up, each locking into the one below, nailed in the CENTER of the
    /// nailing slots at every stud (16" O.C.), nails NOT tight (1/32" under the head so panels slide), 1/4" expansion gaps
    /// in the posts / J-channel pockets, 1" staggered end laps. The zip tool unlocks a course to remove or re-fit it.
    /// Scripted: <see cref="InstallAll"/>, or Starter / CornerPosts / JChannel / HangCourse / NailCourse.
    /// </summary>
    public static class VinylSiding
    {
        public static readonly float Exposure = In(8f), Lap = In(4f), Butt = In(0.625f), LapTop = In(0.1f), HemP = In(0.04f),
            HemH = In(1.25f), SlotLo = In(8.35f), SlotHi = In(8.6f), SlotLen = In(1.125f), SlotPitch = In(1.5f),
            PanelLen = In(150f), EndLap = In(1f), Gap = In(0.25f), NailGap = In(1f / 32f),
            PostFace = In(3f), PostProud = In(1f), PostOut = In(1f), ChannelDepth = In(0.75f),
            JFace = In(0.875f), JProud = In(0.75f), StarterH = In(2.5f), AoH = In(0.55f);

        public class Panel { public float U0, U1; public int Layer; }
        public class Nail { public float U, V, SlotOffset, HeadGap; public bool InFraming; }
        public class Course
        {
            public int Index; public float V0;
            public readonly List<Panel> Panels = new List<Panel>();
            public readonly List<Nail> Nails = new List<Nail>();
            public bool Locked = true, Removed;
            public float GapL, GapR;
            public GameObject Go;
            /// <summary>Ripped top course: no nail hem left; held by snap-lock punches in the utility trim.</summary>
            public bool SnapLocked;
            public bool Nailed => Nails.Count > 0 || SnapLocked;
            public float SlotV => V0 + (SlotLo + SlotHi) * 0.5f;
        }

        public class State
        {
            public string ColorName = "White";
            public Color Color => ExteriorLooks.VinylColor(ColorName);
            public bool Starter, Posts, JOpenings, JTop;
            public float StarterV, BottomV, TopLimit, RunL, RunR;
            public readonly List<Course> Courses = new List<Course>();
            public Course Top { get { for (int i = Courses.Count - 1; i >= 0; i--) if (!Courses[i].Removed) return Courses[i]; return null; } }
            public bool Complete;
        }

        static State S(ExteriorWall w)
        {
            if (w.Vinyl == null)
            {
                w.Vinyl = new State();
                w.Vinyl.StarterV = w.B.yMin - In(0.5f);
                w.Vinyl.BottomV = w.Vinyl.StarterV + In(0.25f);
                w.Vinyl.TopLimit = SidingPrep.TopLimit(w);
                w.Vinyl.RunL = w.B.xMin - PostOut + PostFace - ChannelDepth + Gap;
                w.Vinyl.RunR = w.B.xMax + PostOut - PostFace + ChannelDepth - Gap;
            }
            return w.Vinyl;
        }

        /// <summary>Vinyl state of a wall (created on first use).</summary>
        public static State StateOf(ExteriorWall w) => S(w);

        /// <summary>Rebuilds trim and every course (after a color change).</summary>
        public static void RebuildAll(ExteriorWall w) { RebuildTrim(w); foreach (var c in S(w).Courses) RebuildCourse(w, c); }

        static Material Mat(ExteriorWall w) => FinishMaterials.Get(ExteriorLooks.Vinyl, S(w).Color);
        static float Zb(ExteriorWall w) => w.WrapZ - 0.0005f;

        // ================================================================== scripted API

        /// <summary>Whole wall: starter, posts, J-channel, every course hung and nailed correctly. colorName: see ExteriorLooks.VinylColors.</summary>
        public static bool InstallAll(ExteriorWall w, string colorName = "White")
        {
            if (w == null) return false;
            if (!SidingPrep.Ready(w, SidingKind.Vinyl)) return false;
            S(w).ColorName = colorName;
            if (!S(w).Starter && !Starter(w)) return false;
            if (!S(w).Posts && !CornerPosts(w)) return false;
            if (!S(w).JOpenings && !JChannel(w)) return false;
            int guard = 0;
            while (!S(w).Complete && guard++ < 60)
            {
                var c = HangCourse(w); if (c == null) return S(w).Complete;
                if (!NailCourse(w, c.Index)) return false;
            }
            return true;
        }

        /// <summary>Starter strip along the bottom (the first course locks onto it).</summary>
        public static bool Starter(ExteriorWall w)
        {
            if (!SidingPrep.Ready(w, SidingKind.Vinyl)) return false;
            var s = S(w);
            if (s.Starter) { Game.Toast("Starter strip is already on"); return false; }
            int n = Mathf.CeilToInt((s.RunR - s.RunL) / PanelLen);
            if (!FinishStock.Take(ExteriorCatalog.VinylStarter, n)) return false;
            s.Starter = true; RebuildTrim(w);
            Game.Toast("Starter strip nailed level along the bottom");
            return true;
        }

        /// <summary>Outside corner posts at both ends of the wall.</summary>
        public static bool CornerPosts(ExteriorWall w)
        {
            var s = S(w);
            if (!s.Starter) { Game.Toast("Snap a level line and nail the starter strip first"); return false; }
            if (s.Posts) { Game.Toast("Corner posts are already up"); return false; }
            int n = Mathf.CeilToInt((s.TopLimit - s.StarterV) / (10f * Units.Foot)) * 2;
            if (!FinishStock.Take(ExteriorCatalog.VinylOCP, n)) return false;
            s.Posts = true; RebuildTrim(w);
            Game.Toast("Outside corner posts hung 1/4\" below the soffit, nailed at the top of the top slots");
            return true;
        }

        /// <summary>J-channel around every opening and along the top under the soffit.</summary>
        public static bool JChannel(ExteriorWall w)
        {
            var s = S(w);
            if (!s.Posts) { Game.Toast("Install the corner posts first"); return false; }
            if (s.JOpenings && s.JTop) { Game.Toast("J-channel is already installed"); return false; }
            float len = (s.RunR - s.RunL);
            foreach (var r in w.Openings) len += 2f * (r.width + r.height) + 4f * JFace;
            if (!FinishStock.Take(ExteriorCatalog.VinylJ, Mathf.CeilToInt(len / PanelLen))) return false;
            s.JOpenings = s.JTop = true; RebuildTrim(w);
            Game.Toast("J-channel around the openings (head laps over the jambs) and under the soffit");
            return true;
        }

        /// <summary>Hangs the next course: locks it into the course below (or the starter) and cuts it around openings.
        /// Returns the course (not yet nailed) or null.</summary>
        public static Course HangCourse(ExteriorWall w)
        {
            var s = S(w);
            if (!s.Starter || !s.Posts || !s.JOpenings) { Game.Toast("Starter, corner posts and J-channel go on before the panels"); return null; }
            if (s.Complete) { Game.Toast("Siding is complete up to the soffit"); return null; }
            // re-fit a removed course first
            foreach (var rc in s.Courses) if (rc.Removed) { Game.Toast($"Re-fit course {rc.Index + 1} first (zip tool)"); return null; }
            var below = s.Top;
            if (below != null && (!below.Nailed || !below.Locked)) { Game.Toast($"Nail course {below.Index + 1} (and lock it) before hanging the next one"); return null; }
            int k = s.Courses.Count;
            float v0 = s.BottomV + k * Exposure;
            if (v0 >= s.TopLimit - JFace) { s.Complete = true; Game.Toast("Siding is complete up to the soffit"); return null; }
            var c = new Course { Index = k, V0 = v0, GapL = Gap, GapR = Gap };
            LayoutPanels(s, c);
            if (!FinishStock.Take(ExteriorCatalog.VinylPanel, c.Panels.Count)) return null;
            s.Courses.Add(c);
            if (below != null) RebuildCourse(w, below);
            RebuildCourse(w, c);
            if (v0 + Exposure >= s.TopLimit - JFace) s.Complete = true; // last (ripped) course
            Game.Toast($"Course {k + 1} locked into the {(k == 0 ? "starter strip" : "course below")} — nail it in the slot centers");
            return c;
        }

        static void LayoutPanels(State s, Course c)
        {
            // install from the right end toward the left; each new panel laps 1" OVER the previous one (laps face right)
            float stagger = (c.Index % 3) * In(32f);
            float x1 = s.RunR, len = s.RunR - s.RunL;
            float first = len <= PanelLen ? PanelLen : (PanelLen - stagger > In(24f) ? PanelLen - stagger : PanelLen);
            int layer = 0;
            float cur = first;
            while (true)
            {
                float x0 = Mathf.Max(s.RunL, x1 - cur);
                c.Panels.Add(new Panel { U0 = x0, U1 = x1, Layer = layer & 1 });
                if (x0 <= s.RunL + 1e-4f) break;
                x1 = x0 + EndLap; cur = PanelLen; layer++;
                if (c.Panels.Count > 40) break;
            }
        }

        /// <summary>Nails a course at every stud (16" O.C.): centered in the slot over the stud, 1/32" under the head.
        /// centered=false drives at the stud center regardless of the slot; tight=true drives the head tight (both wrong).</summary>
        public static bool NailCourse(ExteriorWall w, int k, bool centered = true, bool tight = false)
        {
            var s = S(w);
            if (k < 0 || k >= s.Courses.Count || s.Courses[k].Removed) { Game.Toast("No course there"); return false; }
            var c = s.Courses[k];
            if (c.SlotV > s.TopLimit - Gap - In(0.1f))
            {
                c.SnapLocked = true;
                Game.Toast($"Course {k + 1} ripped to fit: snap-lock punched every 6\" and hooked into the utility trim under the soffit");
                return true;
            }
            int added = 0;
            foreach (var u in w.StudLines)
            {
                if (u < s.RunL + In(0.75f) || u > s.RunR - In(0.75f)) continue;
                if (NailAt(w, c, u, centered, tight, false)) added++;
            }
            RebuildCourse(w, c);
            if (added > 0) Game.Toast($"Course {k + 1}: {added} nails {(centered ? "in slot centers" : "at stud centers")}{(tight ? " (driven tight!)" : ", 1/32\" loose")}");
            return c.Nailed;
        }

        /// <summary>One nail into the hem near u (freehand or snapped to the slot center). Returns false if no slot/hem there.</summary>
        public static bool NailAt(ExteriorWall w, Course c, float u, bool centered, bool tight, bool rebuild = true)
        {
            var s = S(w);
            float v = c.SlotV;
            if (Holed(w, new Vector2(u, v))) return false;
            Panel top = null;
            foreach (var p in c.Panels) if (u >= p.U0 && u <= p.U1 && (top == null || p.Layer > top.Layer || p.U0 < top.U0)) top = p;
            if (top == null) return false;
            float rel = u - (top.U0 + SlotPitch * 0.5f);
            float slotC = top.U0 + SlotPitch * 0.5f + Mathf.Round(rel / SlotPitch) * SlotPitch;
            float nu = centered ? slotC : u;
            foreach (var n in c.Nails) if (Mathf.Abs(n.U - nu) < In(0.3f)) return false;
            if (!FinishStock.Take(ExteriorCatalog.SidingNails)) return false;
            var fp = w.Studs ?? w.Face;
            c.Nails.Add(new Nail { U = nu, V = v, SlotOffset = Mathf.Abs(nu - slotC), HeadGap = tight ? 0f : NailGap, InFraming = fp.MemberAt(new Vector2(nu, v), In(0.1f)) != null });
            if (rebuild) RebuildCourse(w, c);
            return true;
        }

        /// <summary>Zip tool: unlocks a course's bottom lock from the course below (it hangs from its nails).</summary>
        public static bool Unzip(ExteriorWall w, int k)
        {
            var s = S(w);
            if (k < 0 || k >= s.Courses.Count || s.Courses[k].Removed) return false;
            var c = s.Courses[k];
            if (!c.Locked) { Game.Toast($"Course {k + 1} is already unlocked"); return false; }
            c.Locked = false; RebuildCourse(w, c);
            Game.Toast($"Course {k + 1} unzipped from the course below");
            return true;
        }

        /// <summary>Zip tool: snaps an unlocked course back into the course below.</summary>
        public static bool Relock(ExteriorWall w, int k)
        {
            var s = S(w);
            if (k < 0 || k >= s.Courses.Count || s.Courses[k].Removed) return false;
            var c = s.Courses[k];
            if (c.Locked) return false;
            c.Locked = true; RebuildCourse(w, c);
            Game.Toast($"Course {k + 1} zipped back in");
            return true;
        }

        /// <summary>Removes an unlocked course (pulls its nails). The course above must be unzipped to reach the nails.</summary>
        public static bool Remove(ExteriorWall w, int k)
        {
            var s = S(w);
            if (k < 0 || k >= s.Courses.Count || s.Courses[k].Removed) return false;
            var c = s.Courses[k];
            if (c.Locked) { Game.Toast("Unzip the course first"); return false; }
            if (k + 1 < s.Courses.Count && !s.Courses[k + 1].Removed && s.Courses[k + 1].Locked) { Game.Toast($"Unzip course {k + 2} too — its lock covers these nails"); return false; }
            c.Removed = true; c.Nails.Clear(); c.SnapLocked = false;
            if (c.Go != null) Object.Destroy(c.Go);
            s.Complete = false;
            Game.Toast($"Course {k + 1} removed — re-fit it with the zip tool");
            return true;
        }

        /// <summary>Re-hangs a removed course (locks it into the one below; it must be nailed again).</summary>
        public static bool Refit(ExteriorWall w, int k)
        {
            var s = S(w);
            if (k < 0 || k >= s.Courses.Count) return false;
            var c = s.Courses[k];
            if (!c.Removed) return Relock(w, k);
            if (k > 0 && (s.Courses[k - 1].Removed || !s.Courses[k - 1].Locked)) { Game.Toast($"Re-fit course {k} first"); return false; }
            c.Removed = false; c.Locked = true; c.Nails.Clear(); c.SnapLocked = false;
            RebuildCourse(w, c);
            if (k == s.Courses.Count - 1 && c.V0 + Exposure >= s.TopLimit - JFace) s.Complete = true;
            Game.Toast($"Course {k + 1} re-fitted — nail it again");
            return true;
        }

        /// <summary>Tin snips: re-cuts the panel ends of a course to leave the 1/4" expansion gap in the posts/J.</summary>
        public static bool TrimEnds(ExteriorWall w, int k)
        {
            var s = S(w);
            if (k < 0 || k >= s.Courses.Count) return false;
            var c = s.Courses[k];
            c.GapL = c.GapR = Gap;
            foreach (var p in c.Panels) { p.U0 = Mathf.Max(p.U0, s.RunL); p.U1 = Mathf.Min(p.U1, s.RunR); }
            RebuildCourse(w, c);
            return true;
        }

        /// <summary>Sets a course's end gap (in the corner-post channel) — e.g. a panel cut too long.</summary>
        public static void SetEndGap(ExteriorWall w, int k, float gapMeters)
        {
            var s = S(w); var c = s.Courses[k];
            float d = gapMeters - c.GapR; c.GapR = gapMeters;
            foreach (var p in c.Panels) if (Mathf.Abs(p.U1 - s.RunR) < 1e-4f) p.U1 -= d;
            RebuildCourse(w, c);
        }

        /// <summary>Course whose exposure/hem contains face point uv (or -1).</summary>
        public static int CourseAt(ExteriorWall w, Vector2 uv)
        {
            if (w.Vinyl == null) return -1;
            foreach (var c in w.Vinyl.Courses) if (!c.Removed && uv.y >= c.V0 - In(0.2f) && uv.y < c.V0 + Exposure + HemH * 0.5f) return c.Index;
            return -1;
        }

        // ================================================================== geometry

        /// <summary>Panel cut-outs: openings (panel ends 1/4" short of the J pocket bottom) and everything above the top J.</summary>
        static List<Rect> Holes(ExteriorWall w)
        {
            var l = new List<Rect>();
            foreach (var r in w.Openings) l.Add(Rect.MinMaxRect(r.xMin - Gap, r.yMin - Gap, r.xMax + Gap, r.yMax + Gap));
            return l;
        }

        static bool Holed(ExteriorWall w, Vector2 uv)
        {
            foreach (var h in Holes(w)) if (h.Contains(uv)) return true;
            return uv.y > S(w).TopLimit - Gap;
        }

        static float Prof(float rel, bool fromBelow)
        {
            if (rel < Lap || (fromBelow && rel <= Lap + 1e-6f)) return Butt + (LapTop - Butt) * Mathf.Clamp01(rel / Lap);
            if (rel < Exposure || (fromBelow && rel <= Exposure + 1e-6f)) return Butt + (LapTop - Butt) * Mathf.Clamp01((rel - Lap) / Lap);
            return HemP;
        }

        public static void RebuildCourse(ExteriorWall w, Course c)
        {
            if (c.Go != null) Object.Destroy(c.Go);
            c.Go = null;
            if (c.Removed) return;
            var s = S(w);
            var b = new MeshBuilder();
            float zb = Zb(w);
            var holes = Holes(w);
            float top = Mathf.Min(c.V0 + Exposure + HemH, s.TopLimit - Gap);
            foreach (var p in c.Panels)
            {
                float dz = -p.Layer * 0.0008f;
                var ys = new List<float> { c.V0, c.V0 + Lap - AoH, c.V0 + Lap, c.V0 + Exposure - AoH, c.V0 + Exposure, c.V0 + SlotLo, c.V0 + SlotHi, c.V0 + Exposure + HemH, top };
                foreach (var h in holes) { ys.Add(h.yMin); ys.Add(h.yMax); }
                ys.RemoveAll(y => y < c.V0 - 1e-5f || y > top + 1e-5f);
                ys.Sort();
                for (int j = 0; j + 1 < ys.Count; j++)
                {
                    float ya = ys[j], yb = ys[j + 1];
                    if (yb - ya < 1e-5f) continue;
                    var cuts = new List<Vector2>();
                    foreach (var h in holes) if (h.yMin < yb - 1e-5f && h.yMax > ya + 1e-5f) cuts.Add(new Vector2(h.xMin, h.xMax));
                    bool slotBand = ya >= c.V0 + SlotLo - 1e-5f && yb <= c.V0 + SlotHi + 1e-5f;
                    if (slotBand)
                        for (float sc = p.U0 + SlotPitch * 0.5f; sc < p.U1; sc += SlotPitch) cuts.Add(new Vector2(sc - SlotLen * 0.5f, sc + SlotLen * 0.5f));
                    float pa = Prof(ya - c.V0, false), pb = Prof(yb - c.V0, true);
                    float za = zb + dz - pa, zbb = zb + dz - pb;
                    foreach (var iv in Subtract(p.U0, p.U1, cuts))
                    {
                        b.AddQuad(new Vector3(iv.x, ya, za), new Vector3(iv.y, ya, za), new Vector3(iv.y, yb, zbb), new Vector3(iv.x, yb, zbb), 0, Out, Vector3.right);
                        // contact shadow just under the next butt (lap 2 of this course, lap 1 of the next)
                        if (ya >= c.V0 + Lap - AoH - 1e-5f && yb <= c.V0 + Lap + 1e-5f) AoStrip(b, iv.x, iv.y, ya, za, yb, zbb, c.V0 + Lap, zb + dz - LapTop, 2);
                        else if (ya >= c.V0 + Exposure - AoH - 1e-5f && yb <= c.V0 + Exposure + 1e-5f && !(c.Index == s.Courses.Count - 1)) AoStrip(b, iv.x, iv.y, ya, za, yb, zbb, c.V0 + Exposure, zb + dz - LapTop, 2);
                        if (!slotBand)
                        {
                            // cut ends (visible at laps / inside the J and posts)
                            b.AddQuad(new Vector3(iv.x, ya, zb + dz), new Vector3(iv.x, ya, za), new Vector3(iv.x, yb, zbb), new Vector3(iv.x, yb, zb + dz), 0, Vector3.left, Vector3.up);
                            b.AddQuad(new Vector3(iv.y, ya, zb + dz), new Vector3(iv.y, ya, za), new Vector3(iv.y, yb, zbb), new Vector3(iv.y, yb, zb + dz), 0, Vector3.right, Vector3.up);
                        }
                        // butts (shadow lines) at the bottom of each 4" lap
                        float rel = ya - c.V0;
                        if (Mathf.Abs(rel) < 1e-5f || Mathf.Abs(rel - Lap) < 1e-5f)
                            b.AddQuad(new Vector3(iv.x, ya, zb + dz - Butt), new Vector3(iv.y, ya, zb + dz - Butt), new Vector3(iv.y, ya, zb + dz - LapTop * 0.5f), new Vector3(iv.x, ya, zb + dz - LapTop * 0.5f), 0, Vector3.down, Vector3.right);
                    }
                }
                // horizontal cut edges at openings and the ripped top edge
                foreach (var h in holes)
                {
                    float x0 = Mathf.Max(h.xMin, p.U0), x1 = Mathf.Min(h.xMax, p.U1);
                    if (x1 <= x0) continue;
                    if (h.yMin > c.V0 && h.yMin < top) { float z = zb + dz - Prof(h.yMin - c.V0, true); b.AddQuad(new Vector3(x0, h.yMin, z), new Vector3(x1, h.yMin, z), new Vector3(x1, h.yMin, zb + dz), new Vector3(x0, h.yMin, zb + dz), 0, Vector3.up, Vector3.right); }
                    if (h.yMax > c.V0 && h.yMax < top) { float z = zb + dz - Prof(h.yMax - c.V0, false); b.AddQuad(new Vector3(x0, h.yMax, z), new Vector3(x1, h.yMax, z), new Vector3(x1, h.yMax, zb + dz), new Vector3(x0, h.yMax, zb + dz), 0, Vector3.down, Vector3.right); }
                }
                if (top < c.V0 + Exposure + HemH - 1e-4f)
                {
                    float z = zb + dz - Prof(top - c.V0, true);
                    b.AddQuad(new Vector3(p.U0, top, z), new Vector3(p.U1, top, z), new Vector3(p.U1, top, zb + dz), new Vector3(p.U0, top, zb + dz), 0, Vector3.up, Vector3.right);
                }
            }
            foreach (var n in c.Nails)
            {
                float z = zb - HemP - n.HeadGap - In(0.03f);
                b.AddCylinder(new Vector3(n.U, n.V, z), Quaternion.Euler(-90f, 0f, 0f), In(0.19f), In(0.06f), 10, 1);
            }
            var mesh = Build(b, "VinylCourse", 3);
            c.Go = w.Part("vinyl", "VinylCourse_" + (c.Index + 1), mesh, new[] { Mat(w), Materials.Get(SurfaceKind.Galvanized), FinishMaterials.Get(ExteriorLooks.AO) }, "vinyl-course", c.Index);
            // thin lapped faces self-shadow into wide dark bands (shadow-map acne); the sheathing/framing behind still casts the wall's shadow, and the AO strip draws the lap shadow line
            c.Go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!c.Locked)
            {
                // unzipped: the bottom lock swings out, hinged at the nail hem
                var pivot = new Vector3(0f, c.V0 + Exposure + HemH, zb);
                var rot = Quaternion.Euler(9f, 0f, 0f);
                c.Go.transform.localRotation = rot;
                c.Go.transform.localPosition = pivot - rot * pivot;
            }
        }

        /// <summary>Starter, corner posts, J-channel (vinyl color).</summary>
        public static void RebuildTrim(ExteriorWall w)
        {
            w.ClearLayer("vinyl-trim");
            var s = S(w);
            var b = new MeshBuilder();
            float zb = Zb(w);
            float Z(float p) => zb - p;
            if (s.Starter)
            {
                Box(b, s.RunL - In(0.5f), s.RunR + In(0.5f), s.StarterV + In(0.4f), s.StarterV + StarterH, Z(In(0.06f)), Z(0f), 1);
                Box(b, s.RunL - In(0.5f), s.RunR + In(0.5f), s.StarterV, s.StarterV + In(0.4f), Z(In(0.32f)), Z(0f), 1);
            }
            if (s.Posts)
            {
                float y0 = s.StarterV - In(0.5f), y1 = s.TopLimit - In(0.25f);
                for (int side = 0; side < 2; side++)
                {
                    float sg = side == 0 ? 1f : -1f, e = side == 0 ? w.B.xMin : w.B.xMax;
                    float corner = e - sg * PostOut, inner = corner + sg * PostFace;
                    // outer face, corner leg wrapping the end of the wall, channel lip, nailing flange
                    BoxX(b, corner, inner, y0, y1, Z(PostProud), Z(PostProud - In(0.05f)), 0);
                    BoxX(b, corner, corner + sg * In(0.05f), y0, y1, Z(PostProud), Z(-In(2.5f)), 0);
                    BoxX(b, inner - sg * In(0.05f), inner, y0, y1, Z(PostProud), Z(In(0.3f)), 0);
                    BoxX(b, inner - sg * ChannelDepth, inner + sg * In(1f), y0, y1, Z(In(0.05f)), Z(0f), 0);
                }
            }
            if (s.JOpenings)
                foreach (var r in w.Openings)
                {
                    J(b, zb, new Vector2(r.xMin, r.yMin - JFace), new Vector2(-1, 0), new Vector2(0, 1), r.height + 2f * JFace, 0f);
                    J(b, zb, new Vector2(r.xMax, r.yMin - JFace), new Vector2(1, 0), new Vector2(0, 1), r.height + 2f * JFace, 0f);
                    J(b, zb, new Vector2(r.xMin - JFace, r.yMin), new Vector2(0, -1), new Vector2(1, 0), r.width + 2f * JFace, 0f);
                    J(b, zb, new Vector2(r.xMin - JFace - In(0.5f), r.yMax), new Vector2(0, 1), new Vector2(1, 0), r.width + 2f * JFace + In(1f), -0.0006f);
                }
            if (s.JTop)
                J(b, zb, new Vector2(s.RunL - In(0.5f), s.TopLimit), new Vector2(0, -1), new Vector2(1, 0), s.RunR - s.RunL + In(1f), 0f);
            var mesh = Build(b, "VinylTrim", 2);
            w.Part("vinyl-trim", "VinylTrim", mesh, new[] { Mat(w), FinishMaterials.Get(ExteriorLooks.Alum) }, "vinyl-trim");
        }

        static void BoxX(MeshBuilder b, float xa, float xb, float y0, float y1, float za, float zb2, int sub)
            => Box(b, Mathf.Min(xa, xb), Mathf.Max(xa, xb), y0, y1, Mathf.Min(za, zb2), Mathf.Max(za, zb2), sub, Vector3.up);

        /// <summary>J-channel: edge = the line it trims (window edge / soffit line), a = direction away from that edge across the face,
        /// along = run direction from start.</summary>
        static void J(MeshBuilder b, float zb, Vector2 start, Vector2 a, Vector2 along, float len, float dz)
        {
            void Piece(float a0, float a1, float p0, float p1)
            {
                Vector2 c0 = start + a * a0, c1 = start + a * a1 + along * len;
                float x0 = Mathf.Min(c0.x, c1.x), x1 = Mathf.Max(c0.x, c1.x), y0 = Mathf.Min(c0.y, c1.y), y1 = Mathf.Max(c0.y, c1.y);
                Box(b, x0, x1, y0, y1, zb + dz - p1, zb + dz - p0, 0, new Vector3(along.x, along.y, 0f));
            }
            Piece(0f, In(0.05f), 0f, JProud);                 // inner wall (against the window / soffit)
            Piece(0f, JFace, JProud - In(0.05f), JProud);     // finished face
            Piece(JFace - In(0.05f), JFace, In(0.5f), JProud); // return lip
            Piece(0f, JFace + In(0.75f), 0f, In(0.04f));      // nailing flange
        }
    }
}
