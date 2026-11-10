using System.Collections.Generic;
using UnityEngine;
using V = Groundbreak.CloseupInspection.Violation;

namespace Groundbreak
{
    /// <summary>Exterior close-up inspection rules (phase "Exterior"). Each failing item drops a red flag.</summary>
    public static class ExteriorInspection
    {
        static float In(float x) => Units.In(x);
        const string P = CloseupInspection.Exterior;

        static IEnumerable<ExteriorWall> Walls()
        {
            ExteriorWall.All.RemoveAll(w => w == null);
            return ExteriorWall.All.ToArray();
        }

        static Vector3 At(ExteriorWall w, float u, float v, float proud = 0.05f) => w.W(u, v, proud);

        static bool _done;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (_done) return; _done = true;
            CloseupInspection.Register("ext-wrap-lap", P, "IRC R703.2 / WRB mfr", "House wrap shingle-lapped: upper course over lower ≥ 6\", vertical seams ≥ 6\"; siding only over a WRB", WrapLaps);
            CloseupInspection.Register("ext-wrap-tape", P, "WRB mfr", "House wrap seams taped", WrapTape);
            CloseupInspection.Register("ext-opening-flash", P, "IRC R703.4", "Openings flashed in shingle order: sill, jambs, head (head flap down over the head tape)", OpeningFlash);
            CloseupInspection.Register("ext-vinyl-nail-slot", P, "ASTM D4756 / mfr", "Vinyl nails centered in the nailing slots", VinylSlot);
            CloseupInspection.Register("ext-vinyl-nail-tight", P, "ASTM D4756 / mfr", "Vinyl nails not driven tight (1/32\" under the head)", VinylTight);
            CloseupInspection.Register("ext-vinyl-nail-spacing", P, "ASTM D4756 / mfr", "Vinyl nailed into framing at ≤ 16\" O.C., every course locked", VinylSpacing);
            CloseupInspection.Register("ext-vinyl-gap", P, "ASTM D4756 / mfr", "Vinyl 1/4\" expansion gap at corner posts and J-channel", VinylGap);
            CloseupInspection.Register("ext-fc-overlap", P, "IRC R703.10 / mfr", "Fiber cement lap ≥ 1-1/4\", butt joints on studs with flashing", FcOverlap);
            CloseupInspection.Register("ext-fc-clearance", P, "Mfr install", "Fiber cement ≥ 6\" above grade and ≥ 2\" above roofing", FcClearance);
            CloseupInspection.Register("ext-brick-ties", P, "IRC R703.8.4", "Veneer ties ≤ 24\" vertical, ≤ 32\" horizontal, ≤ 2.67 sq ft per tie", BrickTies);
            CloseupInspection.Register("ext-brick-weeps", P, "IRC R703.8.5-6", "Base flashing with weeps ≤ 33\" O.C. in the first course", BrickWeeps);
            CloseupInspection.Register("ext-brick-airspace", P, "IRC R703.8.4.2", "Veneer air space ≥ 1\"; steel lintels over openings with ≥ 4\" bearing", BrickAirLintel);
            CloseupInspection.Register("ext-sealant", P, "IRC R703.4", "Trim/siding joints and window perimeters sealed", Sealant);
            CloseupInspection.Register("ext-gutter-slope", P, "Mfr / good practice", "Gutters slope ≥ 1/16\" per 10' toward the outlet", GutterSlope);
            CloseupInspection.Register("ext-deck-ledger", P, "IRC R507.9", "Deck ledger flashed (Z-flashing under the WRB) and lagged into framing ≤ 16\" O.C.", DeckLedger);
            CloseupInspection.Register("ext-deck-guard", P, "IRC R312 / R507", "Guards ≥ 36\" high, openings < 4\" (sphere rule); footings below frost", DeckGuard);
        }

        // ------------------------------------------------------------------ house wrap

        static IEnumerable<V> WrapLaps()
        {
            foreach (var w in Walls())
            {
                var sh = w.Wrap.Sheets;
                if (sh.Count == 0 && ExteriorTrim.Sided(w)) yield return new V("ext-wrap-lap", At(w, w.B.center.x, w.B.center.y), "Siding over bare sheathing — no house wrap");
                foreach (var a in sh)
                    foreach (var b in sh)
                    {
                        if (a == b || !FramePlane.Overlaps(a.R, b.R, -0.001f)) continue;
                        if (a.R.yMin > b.R.yMin + 0.01f && a.R.yMin <= b.R.yMax + 0.001f)
                        {
                            float lap = b.R.yMax - a.R.yMin;
                            float x = (Mathf.Max(a.R.xMin, b.R.xMin) + Mathf.Min(a.R.xMax, b.R.xMax)) * 0.5f;
                            if (a.Order < b.Order) yield return new V("ext-wrap-lap", At(w, x, a.R.yMin), "Reverse lap: upper wrap course tucked UNDER the lower one");
                            else if (lap < HouseWrap.HLap - In(0.1f)) yield return new V("ext-wrap-lap", At(w, x, a.R.yMin), $"Horizontal wrap lap only {Units.Format(lap)} (6\" min)");
                        }
                        if (a.R.xMin > b.R.xMin + 0.01f && a.R.xMin <= b.R.xMax + 0.001f && Mathf.Abs(a.R.yMin - b.R.yMin) < 0.3f)
                        {
                            float lap = b.R.xMax - a.R.xMin;
                            if (lap < In(5.9f)) yield return new V("ext-wrap-lap", At(w, a.R.xMin, (a.R.yMin + a.R.yMax) * 0.5f), $"Vertical wrap seam lapped {Units.Format(lap)} (6\" min)");
                        }
                    }
            }
        }

        static IEnumerable<V> WrapTape()
        {
            foreach (var w in Walls())
                foreach (var (a, b) in HouseWrap.Seams(w))
                    if (!HouseWrap.SeamTaped(w, a, b)) yield return new V("ext-wrap-tape", At(w, (a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f), "House wrap seam not taped");
        }

        static IEnumerable<V> OpeningFlash()
        {
            foreach (var w in Walls())
            {
                if (w.Wrap.Sheets.Count == 0) continue;
                for (int i = 0; i < w.Openings.Count; i++)
                {
                    var r = w.Openings[i]; var op = w.Wrap.Op(i);
                    var pos = At(w, r.center.x, r.yMax + In(4f));
                    if (!op.Flashed) { yield return new V("ext-opening-flash", pos, !op.Cut ? "Opening not cut/flashed" : "Opening flashing incomplete (sill, jambs, head, flap)"); continue; }
                    int s = op.Steps.IndexOf("sill"), j = op.Steps.IndexOf("jambs"), h = op.Steps.IndexOf("head");
                    if (!(s < j && j < h)) yield return new V("ext-opening-flash", pos, "Flashing not in shingle order (sill → jambs → head)");
                }
            }
        }

        // ------------------------------------------------------------------ vinyl

        static IEnumerable<V> VinylSlot()
        {
            foreach (var w in Walls())
                if (w.Vinyl != null)
                    foreach (var c in w.Vinyl.Courses)
                        foreach (var n in c.Nails)
                            if (n.SlotOffset > In(0.125f)) yield return new V("ext-vinyl-nail-slot", At(w, n.U, n.V), $"Nail {Units.Format(n.SlotOffset)} off the slot center — panel can't expand");
        }

        static IEnumerable<V> VinylTight()
        {
            foreach (var w in Walls())
                if (w.Vinyl != null)
                    foreach (var c in w.Vinyl.Courses)
                        foreach (var n in c.Nails)
                            if (n.HeadGap < In(1f / 64f)) yield return new V("ext-vinyl-nail-tight", At(w, n.U, n.V), "Nail driven tight — leave 1/32\" so the panel slides");
        }

        static IEnumerable<V> VinylSpacing()
        {
            foreach (var w in Walls())
            {
                if (w.Vinyl == null) continue;
                foreach (var c in w.Vinyl.Courses)
                {
                    if (c.Removed) { yield return new V("ext-vinyl-nail-spacing", At(w, w.B.center.x, c.V0 + In(4f)), $"Course {c.Index + 1} missing"); continue; }
                    if (!c.Locked) yield return new V("ext-vinyl-nail-spacing", At(w, w.B.center.x, c.V0 + In(1f)), $"Course {c.Index + 1} not locked into the course below");
                    if (c.SnapLocked) continue;
                    if (c.Nails.Count == 0) { yield return new V("ext-vinyl-nail-spacing", At(w, w.B.center.x, c.SlotV), $"Course {c.Index + 1} not nailed"); continue; }
                    var us = new List<float>(); foreach (var n in c.Nails) { us.Add(n.U); if (!n.InFraming) yield return new V("ext-vinyl-nail-spacing", At(w, n.U, n.V), "Nail misses the framing"); }
                    us.Sort();
                    for (int i = 0; i + 1 < us.Count; i++)
                    {
                        if (us[i + 1] - us[i] <= In(16f) + VinylSiding.SlotPitch + In(0.6f)) continue; // studs 16" O.C., nail in the slot over each stud
                        bool hole = false; foreach (var r in w.Openings) if (r.xMax > us[i] && r.xMin < us[i + 1] && c.SlotV > r.yMin - In(1f) && c.SlotV < r.yMax + In(1f)) hole = true;
                        if (!hole) yield return new V("ext-vinyl-nail-spacing", At(w, (us[i] + us[i + 1]) * 0.5f, c.SlotV), $"Nails {Units.Format(us[i + 1] - us[i])} apart (16\" O.C. max)");
                    }
                }
            }
        }

        static IEnumerable<V> VinylGap()
        {
            foreach (var w in Walls())
                if (w.Vinyl != null)
                    foreach (var c in w.Vinyl.Courses)
                    {
                        if (c.Removed) continue;
                        if (c.GapL < In(0.22f)) yield return new V("ext-vinyl-gap", At(w, w.B.xMin + In(2f), c.V0 + In(4f)), $"Panel end {Units.Format(c.GapL)} from the post — leave 1/4\"");
                        if (c.GapR < In(0.22f)) yield return new V("ext-vinyl-gap", At(w, w.B.xMax - In(2f), c.V0 + In(4f)), $"Panel end {Units.Format(c.GapR)} from the post — leave 1/4\"");
                    }
        }

        // ------------------------------------------------------------------ fiber cement

        static IEnumerable<V> FcOverlap()
        {
            foreach (var w in Walls())
            {
                if (w.FC == null || w.FC.Courses.Count == 0) continue;
                float lap = FiberCement.PlankH - w.FC.Exposure;
                if (lap < In(1.24f)) yield return new V("ext-fc-overlap", At(w, w.B.center.x, w.FC.BottomV + w.FC.Exposure), $"Plank overlap {Units.Format(lap)} (1-1/4\" min)");
                foreach (var c in w.FC.Courses)
                    for (int i = 0; i < c.Joints.Count; i++)
                    {
                        float u = c.Joints[i];
                        bool onStud = false; foreach (var s in w.StudLines) if (Mathf.Abs(s - u) < In(0.5f)) onStud = true;
                        if (!onStud) yield return new V("ext-fc-overlap", At(w, u, c.V0 + In(4f)), "Butt joint not on a stud");
                        if (!c.JointFlashed[i]) yield return new V("ext-fc-overlap", At(w, u, c.V0 + In(4f)), "Butt joint not flashed");
                    }
            }
        }

        static IEnumerable<V> FcClearance()
        {
            foreach (var w in Walls())
            {
                if (w.FC == null || w.FC.Courses.Count == 0) continue;
                if (w.FC.BottomV - ExteriorSite.GradeY < In(5.95f)) yield return new V("ext-fc-clearance", At(w, w.B.center.x, w.FC.BottomV), $"Fiber cement {Units.Format(w.FC.BottomV - ExteriorSite.GradeY)} above grade (6\" min)");
                var top = w.FC.Courses[w.FC.Courses.Count - 1];
                float tv = Mathf.Min(top.V0 + FiberCement.PlankH, w.FC.TopLimit);
                for (float u = w.B.xMin + In(6f); u < w.B.xMax; u += In(24f))
                {
                    var o = At(w, u, tv, In(0.5f));
                    if (Physics.Raycast(o, Vector3.up, out var h, In(2f)))
                    {
                        var p = h.collider.GetComponentInParent<Piece>();
                        if (p != null && p.Def.Category == PieceCategory.Sheet && h.normal.y > 0.1f && h.normal.y < 0.98f) { yield return new V("ext-fc-clearance", o, "Fiber cement within 2\" of the roof"); break; }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ brick

        static IEnumerable<V> BrickTies()
        {
            foreach (var w in Walls())
            {
                var s = w.Brick; if (s == null || s.Courses == 0) continue;
                float laidTop = BrickVeneer.BottomOf(s, s.Courses);
                var rows = new List<float>(s.TieRows); rows.Sort();
                float prev = s.BaseV;
                foreach (var r in rows) { if (r > laidTop) break; if (r - prev > In(24.5f)) yield return new V("ext-brick-ties", At(w, w.B.center.x, (r + prev) * 0.5f, BrickVeneer.BrickD + s.Air), $"Tie rows {Units.Format(r - prev)} apart (24\" max)"); prev = r; }
                if (laidTop - prev > In(24.5f)) yield return new V("ext-brick-ties", At(w, w.B.center.x, (laidTop + prev) * 0.5f, BrickVeneer.BrickD + s.Air), $"No ties for {Units.Format(laidTop - prev)} of veneer (24\" max)");
                foreach (var r in rows)
                {
                    if (r > laidTop) continue;
                    var us = new List<float>(); foreach (var t in s.Ties) if (Mathf.Abs(t.y - r) < In(0.2f)) us.Add(t.x);
                    us.Sort();
                    for (int i = 0; i + 1 < us.Count; i++)
                        if (us[i + 1] - us[i] > In(32.5f))
                        {
                            bool hole = false; foreach (var o in w.Openings) if (o.xMax > us[i] && o.xMin < us[i + 1] && r > o.yMin - In(12f) && r < o.yMax + In(12f)) hole = true;
                            if (!hole) yield return new V("ext-brick-ties", At(w, (us[i] + us[i + 1]) * 0.5f, r, BrickVeneer.BrickD + s.Air), $"Ties {Units.Format(us[i + 1] - us[i])} apart (32\" max)");
                        }
                }
            }
        }

        static IEnumerable<V> BrickWeeps()
        {
            foreach (var w in Walls())
            {
                var s = w.Brick; if (s == null || s.Courses == 0) continue;
                if (!s.Flashing) { yield return new V("ext-brick-weeps", At(w, w.B.center.x, s.BaseV, BrickVeneer.BrickD + s.Air), "No base flashing under the veneer"); continue; }
                var us = new List<float>(s.Weeps); us.Sort();
                if (us.Count == 0) { yield return new V("ext-brick-weeps", At(w, w.B.center.x, s.BaseV + In(1f), BrickVeneer.BrickD + s.Air), "No weep holes in the first course"); continue; }
                float prev = w.B.xMin;
                us.Add(w.B.xMax);
                foreach (var u in us) { if (u - prev > In(33f)) yield return new V("ext-brick-weeps", At(w, (u + prev) * 0.5f, s.BaseV + In(1f), BrickVeneer.BrickD + s.Air), $"Weeps {Units.Format(u - prev)} apart (33\" max)"); prev = u; }
            }
        }

        static IEnumerable<V> BrickAirLintel()
        {
            foreach (var w in Walls())
            {
                var s = w.Brick; if (s == null || s.Courses == 0) continue;
                if (s.Air < In(0.95f)) yield return new V("ext-brick-airspace", At(w, w.B.center.x, s.BaseV + In(12f), BrickVeneer.BrickD + s.Air), $"Air space {Units.Format(s.Air)} (1\" min)");
                float laidTop = BrickVeneer.BottomOf(s, s.Courses);
                for (int o = 0; o < w.Openings.Count; o++)
                {
                    if (laidTop <= BrickVeneer.LintelV(w, o) + In(1f)) continue;
                    var r = w.Openings[o];
                    if (!s.Lintels.TryGetValue(o, out float b)) yield return new V("ext-brick-airspace", At(w, r.center.x, r.yMax, BrickVeneer.BrickD + s.Air), "No lintel over the opening");
                    else if (b < In(4f)) yield return new V("ext-brick-airspace", At(w, r.xMax + b, r.yMax, BrickVeneer.BrickD + s.Air), $"Lintel bearing {Units.Format(b)} (4\" min)");
                }
            }
        }

        // ------------------------------------------------------------------ trim / sealant

        static IEnumerable<V> Sealant()
        {
            foreach (var w in Walls())
                foreach (var j in ExteriorTrim.ActiveJoints(w))
                    if (!j.Caulked) yield return new V("ext-sealant", At(w, (j.A.x + j.B.x) * 0.5f, (j.A.y + j.B.y) * 0.5f, j.Proud + In(0.5f)), $"Unsealed joint: {j.What}");
        }

        // ------------------------------------------------------------------ eaves / deck

        static IEnumerable<V> GutterSlope()
        {
            Eaves.All.RemoveAll(e => e == null);
            foreach (var e in Eaves.All.ToArray())
            {
                if (!e.Gutter) continue;
                float fall = e.OutletAtEnd ? e.GutterYStart - e.GutterYEnd : e.GutterYEnd - e.GutterYStart;
                float per10 = fall / (e.L / (10f * Units.Foot));
                var pos = e.P(e.L * 0.5f, e.FrontOff + In(3f), e.Top);
                if (per10 < In(1f / 16f) - 1e-5f) yield return new V("ext-gutter-slope", pos, fall < 0 ? "Gutter slopes AWAY from the outlet" : $"Gutter fall {Units.ToInches(per10):0.##}\" per 10' (1/16\" min)");
            }
        }

        static IEnumerable<V> DeckLedger()
        {
            DeckBuilder.All.RemoveAll(d => d == null);
            foreach (var d in DeckBuilder.All.ToArray())
            {
                if (d.Stage < 1) continue;
                var pos = d.P(d.U0 + d.Width * 0.5f, In(3f), d.LedgerTop);
                if (!d.ZFlash) yield return new V("ext-deck-ledger", pos, "Ledger not flashed");
                else if (!d.WrapLapped) yield return new V("ext-deck-ledger", pos, "Z-flashing not lapped under the house wrap");
                if (d.LagsInFraming < d.Lags.Count) yield return new V("ext-deck-ledger", pos + Vector3.down * 0.15f, $"{d.Lags.Count - d.LagsInFraming} ledger lag(s) miss the framing");
                var us = new List<float>(); foreach (var l in d.Lags) us.Add(l.x); us.Sort();
                for (int i = 0; i + 1 < us.Count; i++) if (us[i + 1] - us[i] > In(16.5f)) yield return new V("ext-deck-ledger", d.P(us[i], In(3f), d.LedgerTop), "Ledger lags more than 16\" apart");
            }
        }

        static IEnumerable<V> DeckGuard()
        {
            foreach (var d in DeckBuilder.All.ToArray())
            {
                if (d == null) continue;
                var pos = d.P(d.U0 + d.Width * 0.5f, d.Depth, d.SurfaceY + In(20f));
                if (d.Stage >= 2 && d.FootingBottomY > ExteriorSite.GradeY - ExteriorSite.FrostDepth + 1e-3f) yield return new V("ext-deck-guard", d.Footings.Count > 0 ? d.Footings[0] : pos, "Footings above frost depth");
                if (!d.Done) { if (d.Stage >= 7 && d.SurfaceY - ExteriorSite.GradeY > In(30f)) yield return new V("ext-deck-guard", pos, "Deck surface > 30\" above grade needs guards"); continue; }
                if (d.GuardHeight < In(36f) - 1e-4f) yield return new V("ext-deck-guard", pos, $"Guard {Units.Format(d.GuardHeight)} high (36\" min)");
                if (d.BalusterGap >= In(4f)) yield return new V("ext-deck-guard", pos, $"Baluster gap {Units.Format(d.BalusterGap)} (a 4\" sphere must not pass)");
            }
        }
    }
}
