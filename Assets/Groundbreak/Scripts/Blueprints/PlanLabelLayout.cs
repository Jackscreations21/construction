using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Keeps the on-site plan overlay readable: after the blueprint layers are added, each room's name/size label
    /// is shrunk to fit its room and moved to the spot in the room farthest from fixture markers (and their tags),
    /// stair treads and door/window RO tags, staying near the room center when the center is already clear.
    /// Works in plan feet; labels lie flat with local x = plan X and local y = plan Y.</summary>
    public static class PlanLabelLayout
    {
        /// <summary>Wanted clear distance between a room label and any marker (~0.6 m).</summary>
        public const float ClearFt = 2f;
        /// <summary>After the last layout: smallest gap (ft) between any room label and any fixture marker/tag (0 = touching).</summary>
        public static float MinFixtureClearanceFt = float.PositiveInfinity;
        public static int MovedCount;

        public static void Arrange(HousePlan plan, Blueprint bp, PlanPlacement at, int floor)
        {
            MinFixtureClearanceFt = float.PositiveInfinity; MovedCount = 0;
            if (plan == null || PlanOverlay.Root == null) return;
            var fixtures = new List<Rect>();
            var others = new List<Rect>();
            if (bp != null)
            {
                foreach (var x in bp.Fixtures)
                {
                    if (x.Floor != floor) continue;
                    float sz = (x.Kind == "SUP" || x.Kind == "RET" ? 0.25f : x.Kind == "RTU" ? 1.2f : 0.4f) / Units.Foot;
                    fixtures.Add(new Rect(x.Ft.x - sz / 2, x.Ft.y - sz / 2, sz, sz));
                    if (x.Kind != "SUP") fixtures.Add(new Rect(x.Ft.x - 0.9f, x.Ft.y + 0.7f - 0.35f, 1.8f, 0.7f));   // its tag
                }
                foreach (var s in bp.Stairs) if (s.Floor == floor) others.Add(s.Ft);
                foreach (var kv in bp.LevelOffsetFt)
                {
                    var r = bp.Rooms.Find(q => q.Name == kv.Key && q.Floor == floor);
                    if (r != null) others.Add(new Rect(r.Ft.center.x - 1.5f, r.Ft.center.y - 1.6f - 0.4f, 3f, 0.8f));
                }
            }
            foreach (var o in plan.Openings)
            {
                if (o.Floor != floor || o.Wall < 0 || o.Wall >= plan.Walls.Count) continue;
                var w = plan.Walls[o.Wall]; var n = new Vector2(-w.Dir.y, w.Dir.x);
                var p = w.At(o.CenterFt) + n * (w.Exterior ? 1.6f : 1.0f);
                others.Add(new Rect(p.x - 1.3f, p.y - 0.35f, 2.6f, 0.7f));
            }
            if (fixtures.Count == 0) return;

            // room labels as PlanOverlay drew them
            var labels = new List<(Transform t, TextMesh tm)>();
            foreach (Transform c in PlanOverlay.Root.transform)
            {
                if (c.name != "PlanLabel") continue;
                var tm = c.GetComponent<TextMesh>();
                if (tm != null) labels.Add((c, tm));
            }
            var used = new HashSet<Transform>();
            foreach (var r in plan.Rooms)
            {
                if (r.Floor != floor) continue;
                string text = $"{r.Name}\n{r.Ft.width:0}' x {r.Ft.height:0}'";
                Vector3 centerW = at.World(r.Ft.center.x, r.Ft.center.y, 0.05f);
                Transform best = null; TextMesh bestTm = null; float bd = float.MaxValue;
                foreach (var (t, tm) in labels)
                {
                    if (used.Contains(t) || tm.text != text) continue;
                    float d = (t.position - centerW).sqrMagnitude;
                    if (d < bd) { bd = d; best = t; bestTm = tm; }
                }
                if (best == null) continue;
                used.Add(best);

                Vector2 size = SizeFt(bestTm, best);
                float maxW = Mathf.Max(1f, r.Ft.width - 0.6f), maxH = Mathf.Max(1f, r.Ft.height - 0.6f);
                float k = Mathf.Min(1f, maxW / Mathf.Max(0.01f, size.x), maxH / Mathf.Max(0.01f, size.y));
                k = Mathf.Max(0.5f, k);
                if (k < 0.999f) { bestTm.characterSize *= k; size *= k; }

                Vector2 center = r.Ft.center, bestP = center;
                float bestScore = Score(center, size, center, fixtures, others);
                float rx = Mathf.Max(0f, r.Ft.width / 2 - size.x / 2 - 0.3f), ry = Mathf.Max(0f, r.Ft.height / 2 - size.y / 2 - 0.3f);
                const int N = 8;
                for (int i = 0; i <= N; i++)
                    for (int j = 0; j <= N; j++)
                    {
                        var c = new Vector2(center.x - rx + 2 * rx * i / N, center.y - ry + 2 * ry * j / N);
                        float sc = Score(c, size, center, fixtures, others);
                        if (sc < bestScore - 1e-4f) { bestScore = sc; bestP = c; }
                    }
                if ((bestP - center).sqrMagnitude > 1e-4f)
                {
                    best.position = at.World(bestP.x, bestP.y, 0.05f);
                    MovedCount++;
                }
                var rect = new Rect(bestP - size / 2, size);
                foreach (var f in fixtures) MinFixtureClearanceFt = Mathf.Min(MinFixtureClearanceFt, Gap(rect, f));
            }
        }

        /// <summary>Lower is better: overlap with fixtures (heavy), then other tags, then lack of clearance, then distance from center.</summary>
        static float Score(Vector2 c, Vector2 size, Vector2 center, List<Rect> fixtures, List<Rect> others)
        {
            var r = new Rect(c - size / 2, size);
            float s = 0f, minGap = ClearFt;
            foreach (var f in fixtures) { s += Overlap(r, f) * 100f; minGap = Mathf.Min(minGap, Gap(r, f)); }
            foreach (var o in others) { s += Overlap(r, o) * 20f; minGap = Mathf.Min(minGap, Gap(r, o) + 0.5f); }
            s += (ClearFt - Mathf.Max(0f, minGap)) * 4f;
            s += (c - center).magnitude * 0.15f;
            return s;
        }

        static float Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        static float Gap(Rect a, Rect b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax)), dy = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Label footprint in plan feet (from the generated text mesh, or estimated from the string).</summary>
        static Vector2 SizeFt(TextMesh tm, Transform t)
        {
            var r = t.GetComponent<MeshRenderer>();
            if (r != null)
            {
                var lb = r.localBounds.size;
                if (lb.x > 0.01f && lb.y > 0.01f) return new Vector2(lb.x, lb.y) / Units.Foot;
            }
            string[] lines = (tm.text ?? "").Split('\n');
            int chars = 1; foreach (var l in lines) chars = Mathf.Max(chars, l.Length);
            float lineM = tm.characterSize * tm.fontSize * 0.1f;
            return new Vector2(chars * lineM * 0.5f, lines.Length * lineM * 1.1f) / Units.Foot;
        }
    }
}
