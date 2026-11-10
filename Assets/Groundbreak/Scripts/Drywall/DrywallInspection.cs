using System.Collections.Generic;
using UnityEngine;
using V = Groundbreak.CloseupInspection.Violation;

namespace Groundbreak
{
    /// <summary>Drywall (screw / fire) inspection rules, phase "Drywall". Ids: dw-screws, dw-ends, dw-typex, dw-wet,
    /// dw-boxes, dw-stagger, dw-bead.</summary>
    public static class DrywallInspection
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            const string P = CloseupInspection.Drywall;
            CloseupInspection.Register("dw-screws", P, "IRC Table R702.3.5", "Screws along every framing member: max 12\" o.c. ceilings / 16\" walls, ≥3/8\" from edges", Screws);
            CloseupInspection.Register("dw-ends", P, "IRC R702.3.5 / GA-216", "Sheet ends land on framing (no floating butt ends)", Ends);
            CloseupInspection.Register("dw-typex", P, "IRC R302 / R702.3", "5/8\" Type X where required (fire-rated faces, ceilings framed 24\" o.c.)", TypeX);
            CloseupInspection.Register("dw-wet", P, "IRC R702.3.7", "Moisture/mold-resistant board near tubs and showers", Wet);
            CloseupInspection.Register("dw-boxes", P, "NEC 314.20", "No electrical boxes covered by drywall (cut-outs made)", Boxes);
            CloseupInspection.Register("dw-stagger", P, "GA-216", "Butt joints in adjacent rows staggered at least 16\"", Stagger);
            CloseupInspection.Register("dw-bead", P, "GA-216", "Corner bead on every outside corner", Bead);
        }

        static Vector3 At(DrywallSurface s, Vector2 uv) => s.FrontWorld(uv, 0.03f);

        static IEnumerable<V> Screws()
        {
            foreach (var s in Drywall.Surfaces)
            {
                if (s == null) continue;
                float max = Drywall.Spacing(s) + Units.In(0.25f), edgeMin = Units.In(DrywallRules.ScrewEdgeMinIn) - 0.001f, edgeMax = Units.In(DrywallRules.ScrewEdgeMaxIn);
                foreach (var sh in s.Sheets)
                {
                    foreach (var sc in s.Screws)
                    {
                        if (sc.Sheet != sh) continue;
                        float e = Mathf.Min(Mathf.Min(sc.P.x - sh.R.xMin, sh.R.xMax - sc.P.x), Mathf.Min(sc.P.y - sh.R.yMin, sh.R.yMax - sc.P.y));
                        if (e < edgeMin) yield return new V("dw-screws", At(s, sc.P), "Screw closer than 3/8\" to the sheet edge");
                    }
                    foreach (var r in Drywall.ScrewRuns(sh))
                    {
                        float u = r.U; var run = new Vector2(r.V0, r.V1);
                        var vs = new List<float>();
                        foreach (var sc in s.Screws) if (sc.Sheet == sh && Mathf.Abs(sc.P.x - u) < Units.In(1f)) vs.Add(sc.P.y);
                        vs.Sort();
                        {
                            var inRun = vs.FindAll(v => v >= run.x - 0.001f && v <= run.y + 0.001f);
                            if (inRun.Count == 0) { yield return new V("dw-screws", At(s, new Vector2(u, (run.x + run.y) * 0.5f)), "No screws along this stud/joist"); continue; }
                            float prev = run.x; bool bad = false; float badAt = 0f;
                            foreach (var v in inRun) { if (v - prev > (prev == run.x ? edgeMax : max)) { bad = true; badAt = (v + prev) * 0.5f; break; } prev = v; }
                            if (!bad && run.y - prev > edgeMax) { bad = true; badAt = (run.y + prev) * 0.5f; }
                            if (bad) yield return new V("dw-screws", At(s, new Vector2(u, badAt)), $"Screw spacing over {Drywall.Spacing(s) / Units.Inch:0}\" along the framing");
                        }
                    }
                }
            }
        }

        static IEnumerable<V> Ends()
        {
            foreach (var s in Drywall.Surfaces)
            {
                if (s == null) continue;
                foreach (var sh in s.Sheets)
                    foreach (float u in new[] { sh.R.xMin, sh.R.xMax })
                    {
                        if (Mathf.Abs(u - s.Fp.Bounds.xMin) < 0.01f || Mathf.Abs(u - s.Fp.Bounds.xMax) < 0.01f) continue; // at the face's end (corner framing)
                        var mid = new Vector2(u, sh.R.center.y);
                        bool onHole = false; foreach (var h in sh.Holes) if (Mathf.Abs(h.xMin - u) < 0.01f || Mathf.Abs(h.xMax - u) < 0.01f) onHole = true;
                        if (onHole) continue;
                        var m = s.Fp.MemberAt(mid, -Units.In(0.25f));
                        if (m == null || !m.AlongV) yield return new V("dw-ends", At(s, mid), "Butt end floats between framing — end it on a stud/joist center");
                    }
            }
        }

        static bool FireRequired(DrywallSurface s, out string why)
        {
            why = null;
            foreach (var f in DrywallRules.FireRatedFaces) if (f.SameFace(s.Fp)) { why = "Fire-rated face: 5/8\" Type X required"; return true; }
            if (DrywallRules.TypeXOn24InchCeilings && s.IsCeiling)
            {
                var l = s.Fp.FramingLinesU();
                if (l.Count >= 3)
                {
                    var gaps = new List<float>(); for (int i = 1; i < l.Count; i++) gaps.Add(l[i] - l[i - 1]);
                    gaps.Sort();
                    if (gaps[gaps.Count / 2] > Units.In(20f)) { why = "Ceiling framed 24\" o.c.: 5/8\" Type X required"; return true; }
                }
            }
            return false;
        }

        static IEnumerable<V> TypeX()
        {
            foreach (var s in Drywall.Surfaces)
            {
                if (s == null || !FireRequired(s, out var why)) continue;
                foreach (var sh in s.Sheets) if (!sh.P.FireRated) yield return new V("dw-typex", At(s, sh.R.center), why);
            }
        }

        static IEnumerable<V> Wet()
        {
            var fixtures = new List<Bounds>();
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                if (p.Def != null && DrywallRules.WetFixtureIds.Contains(p.Def.Id)) { var c = p.GetComponent<Collider>(); if (c != null) fixtures.Add(c.bounds); }
            if (fixtures.Count == 0) yield break;
            float reach = DrywallRules.WetAreaFeet * Units.Foot;
            foreach (var s in Drywall.Surfaces)
            {
                if (s == null) continue;
                foreach (var sh in s.Sheets)
                {
                    if (sh.P.WetRated) continue;
                    var wb = new Bounds(s.Fp.World(sh.R.center), Vector3.zero);
                    wb.Encapsulate(s.Fp.World(sh.R.min)); wb.Encapsulate(s.Fp.World(sh.R.max));
                    foreach (var f in fixtures)
                    {
                        var fb = f; fb.Expand(reach * 2f);
                        if (fb.Intersects(wb)) { yield return new V("dw-wet", At(s, sh.R.center), $"Within {DrywallRules.WetAreaFeet:0}' of a tub/shower: use moisture- or mold-resistant board"); break; }
                    }
                }
            }
        }

        static IEnumerable<V> Boxes()
        {
            foreach (var s in Drywall.Surfaces)
            {
                if (s == null) continue;
                foreach (var sh in s.Sheets) foreach (var c in sh.Covered) yield return new V("dw-boxes", At(s, c.center), "Electrical box covered — cut it out");
            }
        }

        static IEnumerable<V> Stagger()
        {
            foreach (var s in Drywall.Surfaces)
            {
                if (s == null) continue;
                var butts = new Dictionary<int, List<float>>();
                foreach (var sh in s.Sheets)
                {
                    if (!butts.ContainsKey(sh.Row)) butts[sh.Row] = new List<float>();
                    foreach (var o in s.Sheets)
                        if (o != sh && o.Row == sh.Row && Mathf.Abs(o.R.xMin - sh.R.xMax) < 0.004f) butts[sh.Row].Add(sh.R.xMax);
                }
                float min = Units.In(DrywallRules.ButtStaggerIn) - 0.005f;
                foreach (var kv in butts)
                {
                    if (!butts.TryGetValue(kv.Key + 1, out var next)) continue;
                    foreach (var a in kv.Value)
                        foreach (var b in next)
                            if (Mathf.Abs(a - b) < min)
                            {
                                float v = 0f; foreach (var sh in s.Sheets) if (sh.Row == kv.Key) { v = s.IsCeiling ? sh.R.yMax : sh.R.yMin; break; }
                                yield return new V("dw-stagger", At(s, new Vector2((a + b) * 0.5f, v)), $"Butt joints only {Units.Format(Mathf.Abs(a - b))} apart in adjacent rows");
                            }
                }
            }
        }

        static IEnumerable<V> Bead()
        {
            foreach (var s in Drywall.Surfaces)
            {
                if (s == null) continue;
                foreach (var seam in s.Seams)
                    if (seam.Kind == SeamKind.OutsideCorner && !Drywall.HasBead(s, seam)) yield return new V("dw-bead", At(s, seam.At(seam.Length * 0.5f)), "Outside corner without corner bead");
            }
        }
    }

    /// <summary>One-call drywall job for scripted play-throughs (integration tests): hang → cut-outs → screw → 3 coats
    /// with dry time → sand → texture → prime → 2 coats of paint. Skips game time for every dry period.</summary>
    public static class DrywallJob
    {
        public static DrywallSurface Complete(FramePlane fp, string productId = "dw-12-4x12", string texture = "orange-peel", string color = "Linen", Sheen sheen = Sheen.Eggshell)
        {
            var s = Drywall.HangSurface(fp, productId);
            if (s == null) return null;
            Drywall.CutOutBoxes(s);
            Drywall.ScrewAll(s);
            Finish(s, texture, color, sheen);
            return s;
        }

        /// <summary>From hung + screwed to painted.</summary>
        public static void Finish(DrywallSurface s, string texture = "orange-peel", string color = "Linen", Sheen sheen = Sheen.Eggshell)
        {
            for (int c = 1; c <= 3; c++) { Drywall.CoatAll(s, c); FinishTime.Skip(DrywallRules.Compound("dw-mud-ap").DryHours + 0.1f); }
            Drywall.SandAll(s);
            if (texture != null) { Drywall.ApplyTexture(s, texture); FinishTime.Skip((texture == "smooth" ? DrywallRules.SkimDryHours : DrywallRules.TextureDryHours) + 0.1f); }
            PaintJob.Prime(s); FinishTime.Skip(PaintRules.DryHours + 0.1f);
            var pc = PaintColors.Get(color);
            PaintJob.Paint(s, pc, sheen); FinishTime.Skip(PaintRules.DryHours + 0.1f);
            PaintJob.Paint(s, pc, sheen); FinishTime.Skip(PaintRules.DryHours + 0.1f);
            s.Refresh();
        }
    }
}
