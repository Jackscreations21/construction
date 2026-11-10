using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Roofing jobs over a framed + sheathed roof (RoofInfo planes), in the real sequence:
    /// DryIn: drip edge at the eaves -> ice &amp; water shield (36" at the eaves + valleys) -> synthetic underlayment courses
    /// (4" laps, cap staples) -> drip edge on the rakes over the underlayment.
    /// Shingles: chalk lines -> starter strip -> architectural shingle courses (5-5/8" exposure, 6-1/4" offsets, 4 nails each)
    /// -> ridge vent -> hip &amp; ridge cap shingles.  Helpers: StepFlashing (roof-to-wall), RoofJacks (staging).</summary>
    public static class Roofing
    {
        static float I(float inch) => inch * Units.Inch;
        public const int NailsPerShingle = 4;
        public const int ShinglesPerBundle = 22;

        static Func<(Vector3, Vector3, Quaternion)> Band(RoofPlane p, float v0, float v1, float h = 0.01f)
        {
            return () =>
            {
                float vm = (v0 + v1) * 0.5f;
                float u0 = p.UMin(Mathf.Max(0f, v0)), u1 = p.UMax(Mathf.Max(0f, v0));
                return (p.At((u0 + u1) * 0.5f, vm, h), new Vector3(Mathf.Max(0.02f, v1 - v0), 0.03f, Mathf.Max(0.05f, u1 - u0)), Quaternion.LookRotation(p.AlongEave, p.Normal));
            };
        }

        static Func<(Vector3, Vector3, Quaternion)> Line(Vector3 a, Vector3 b, float w = 0.15f)
        {
            return () => ((a + b) * 0.5f, new Vector3(w, 0.1f, Vector3.Distance(a, b)), Quaternion.LookRotation(b - a));
        }

        static FramingJob NewJob(string title)
        {
            var job = FramingJob.Create(title);
            job.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return job;
        }

        /// <summary>Fractional consumable use (rolls / bundles by length): returns whole units to take now.</summary>
        class Meter
        {
            float _used; int _taken; readonly float _per;
            public Meter(float per) { _per = per; }
            public int Take(float amount) { _used += amount; int need = Mathf.CeilToInt(_used / _per - 0.0001f); int n = need - _taken; _taken = need; return Mathf.Max(0, n); }
        }

        // ------------------------------------------------------------------ dry-in

        /// <summary>Dry-in: eave drip edge, ice &amp; water shield, synthetic underlayment, rake drip edge.</summary>
        public static FramingJob DryIn(RoofInfo roof)
        {
            var job = NewJob("Roof dry-in (drip edge, ice & water, underlayment)");
            roof.DryInJob = job;
            var root = job.transform;
            // 1. drip edge along every eave (under the ice & water), 10' pieces lapped 2"
            for (int pi = 0; pi < roof.Planes.Count; pi++)
            {
                var p = roof.Planes[pi];
                float fo = p.FasciaOutset;
                float s0 = -(fo + I(0.06f)), s1 = p.EaveLength + fo + I(0.06f);
                float piece = 10 * Units.Foot;
                int n = Mathf.CeilToInt((s1 - s0 - I(2f)) / (piece - I(2f)) - 0.001f);
                for (int k = 0; k < n; k++)
                {
                    float a = s0 + k * (piece - I(2f)), b = Mathf.Min(s1, a + piece);
                    var pp = p; float lift = (k % 2) * 0.0004f;
                    job.Add("Drip edge on the " + p.Name.ToLower() + " eave (piece " + (k + 1) + "/" + n + "), roofing nails 12\" O.C.", ToolSets.Roof, () =>
                    {
                        var m = new RoofMesh();
                        ShingleMesh.DripEdge(m, (s, d, h) => pp.At(s, d, h), a, b, ShingleMesh.HDrip + lift, pp.OutHorizontal, pp.Normal, RoofTextures.DripEdge, pp.FasciaOutset / pp.Cos);
                        m.Attach(root, "Drip edge (eave)");
                    }, Line(p.At(a, 0), p.At(b, 0))).Use("drip-edge").Use("roof-nails", 10);
                }
            }
            // 2. ice & water shield: first 36" at the eaves, then the valleys
            var ice = new Meter(66 * Units.Foot);
            for (int pi = 0; pi < roof.Planes.Count; pi++)
            {
                var p = roof.Planes[pi]; var pp = p;
                float v1 = Mathf.Min(p.SlopeLength, I(36f));
                job.Add("Ice & water shield: first 36\" strip along the " + p.Name.ToLower() + " eave (peel + stick over the drip edge)", ToolSets.Roof, () =>
                {
                    var m = new RoofMesh();
                    ShingleMesh.Membrane(m, pp, 0f, v1, ShingleMesh.HIce, RoofTextures.IceWater);
                    m.Attach(root, "Ice & water shield (" + pp.Name + ")");
                }, Band(p, 0f, v1)).Use("ice-water", ice.Take(p.EaveLength));
            }
            foreach (var vl in roof.Valleys)
            {
                var e = vl;
                job.Add("Ice & water shield: 36\" centered down the valley", ToolSets.Roof, () =>
                {
                    var m = new RoofMesh();
                    foreach (int side in new[] { e.PlaneA, e.PlaneB })
                    {
                        if (side < 0 || side >= roof.Planes.Count) continue;
                        var p = roof.Planes[side];
                        Vector3 la = p.ToLocal(e.A), lb = p.ToLocal(e.B);
                        Vector2 A = new Vector2(la.x, la.y), B = new Vector2(lb.x, lb.y);
                        Vector2 d = (B - A).normalized, nrm = new Vector2(-d.y, d.x);
                        Vector2 mid = new Vector2(p.EaveLength * 0.5f, p.SlopeLength * 0.3f);
                        if (Vector2.Dot(mid - A, nrm) < 0f) nrm = -nrm;
                        var poly = Geo2.Clip(new List<Vector2> { A, B, B + nrm * I(18f), A + nrm * I(18f) }, p.Bounds(0f, 0f, p.SlopeLength));
                        if (poly.Count < 3) continue;
                        int s = m.Mat(RoofTextures.IceWater);
                        var pts = new List<Vector3>(); var uvs = new List<Vector2>();
                        foreach (var q in poly) { pts.Add(p.At(q.x, q.y, ShingleMesh.HIce)); uvs.Add(q); }
                        m.Poly(pts, uvs, p.Normal, s);
                    }
                    if (!m.Empty) m.Attach(root, "Ice & water shield (valley)");
                }, Line(e.A, e.B, 0.9f)).Use("ice-water", ice.Take(e.Length * 2f));
            }
            // 3. synthetic underlayment: 48" courses lapped 4", starting over the top of the ice & water, cap-stapled
            var roll = new Meter(250 * Units.Foot);
            for (int pi = 0; pi < roof.Planes.Count; pi++)
            {
                var p = roof.Planes[pi];
                float top = p.SlopeLength - p.RidgeGap;
                int k = 0;
                for (float v0 = Mathf.Min(I(32f), top - I(12f)); v0 < top - I(1f); v0 += I(44f), k++)
                {
                    float a = v0, b = Mathf.Min(top, v0 + I(48f));
                    var pp = p; int kk = k;
                    float width = Mathf.Max(p.WidthAt(a), p.WidthAt(b));
                    int staples = Mathf.CeilToInt(width / Units.Foot) * 2 + Mathf.CeilToInt(width / (2 * Units.Foot));
                    job.Add("Underlayment course " + (k + 1) + " (" + p.Name.ToLower() + "): roll out, 4\" lap, cap staples 12\" laps / 24\" field", ToolSets.Staple, () =>
                    {
                        var m = new RoofMesh();
                        float h = ShingleMesh.HUnder + (kk % 2) * ShingleMesh.HUnderLap;
                        ShingleMesh.Membrane(m, pp, a, b, h, RoofTextures.Underlayment);
                        int sub = m.Mat(RoofTextures.CapStaple);
                        float u0 = pp.UMin(a), u1 = pp.UMax(a);
                        for (float u = u0 + I(6f); u < u1 - I(3f); u += I(12f))
                        {
                            if (pp.Contains(u, a + I(2f), -I(1f))) ShingleMesh.CapStaple(m, pp, u, a + I(2f), h, sub);
                            if (b < top - 0.001f && pp.Contains(u, b - I(6f), -I(1f))) ShingleMesh.CapStaple(m, pp, u, b - I(6f), h, sub);
                        }
                        for (float u = u0 + I(18f); u < u1 - I(6f); u += I(24f))
                            if (pp.Contains(u, (a + b) * 0.5f, -I(1f))) ShingleMesh.CapStaple(m, pp, u, (a + b) * 0.5f, h, sub);
                        m.Attach(root, "Underlayment course " + (kk + 1) + " (" + pp.Name + ")");
                    }, Band(p, a, b)).Use("underlayment", roll.Take(width)).Use("cap-staples", staples);
                }
            }
            // 4. drip edge on the rakes, OVER the underlayment
            for (int pi = 0; pi < roof.Planes.Count; pi++)
            {
                var p = roof.Planes[pi];
                for (int end = 0; end < 2; end++)
                {
                    if (end == 0 ? p.HipStart : p.HipEnd) continue;
                    var pp = p; int e = end;
                    float v0 = -(p.FasciaOutset + I(0.06f)) / p.Cos, v1 = p.SlopeLength;
                    float piece = 10 * Units.Foot;
                    int n = Mathf.CeilToInt((v1 - v0 - I(2f)) / (piece - I(2f)) - 0.001f);
                    for (int k = 0; k < n; k++)
                    {
                        float a = v0 + k * (piece - I(2f)), b = Mathf.Min(v1, a + piece);
                        float lift = (k % 2) * 0.0004f;
                        Vector3 outH = e == 0 ? -pp.AlongEave : pp.AlongEave;
                        Func<float, float, float, Vector3> at = e == 0
                            ? (s, d, h) => pp.At(d, s, h)
                            : (Func<float, float, float, Vector3>)((s, d, h) => pp.At(pp.EaveLength - d, s, h));
                        job.Add("Drip edge on the " + (e == 0 ? "left" : "right") + " rake of the " + p.Name.ToLower() + " (piece " + (k + 1) + "/" + n + "), over the underlayment", ToolSets.Roof, () =>
                        {
                            var m = new RoofMesh();
                            ShingleMesh.DripEdge(m, at, a, b, ShingleMesh.HRakeDrip + lift, outH, pp.Normal, RoofTextures.DripEdge, pp.FasciaOutset);
                            m.Attach(root, "Drip edge (rake)");
                        }, Line(at(a, 0, 0), at(b, 0, 0))).Use("drip-edge").Use("roof-nails", 10);
                    }
                }
            }
            job.Completed += () => roof.DriedIn = true;
            return job;
        }

        // ------------------------------------------------------------------ shingles

        /// <summary>Chalk lines, starter strip, architectural shingle courses, ridge vent, hip/ridge caps.</summary>
        public static FramingJob Shingles(RoofInfo roof)
        {
            var job = NewJob("Shingles (architectural, weathered wood)");
            roof.ShinglesJob = job;
            var root = job.transform;
            var main = RoofTextures.Shingle; var shadow = RoofTextures.ShingleShadow; var edge = RoofTextures.AsphaltEdge;

            // chalk lines: horizontal course lines every 5-5/8" (snapped from both rakes) + a vertical offset line
            for (int pi = 0; pi < roof.Planes.Count; pi++)
            {
                var p = roof.Planes[pi]; var pp = p;
                int n = ShingleMesh.CourseCount(p);
                job.Add("Snap chalk lines on the " + p.Name.ToLower() + ": course lines every 5-5/8\", offset line for the 6-1/4\" stagger", ToolSets.Chalk, () =>
                {
                    float h = ShingleMesh.HUnder + 0.0001f;
                    for (int c = 1; c < n; c++)
                    {
                        float v = ShingleMesh.FirstButt(pp) + c * ShingleMesh.Exposure;
                        if (v < 0.02f) continue;
                        float u0 = pp.UMin(v) + 0.01f, u1 = pp.UMax(v) - 0.01f;
                        if (u1 - u0 < 0.05f) continue;
                        ChalkLine.SnapWorld(pp.At(u0, v, h), pp.At(u1, v, h), 0.1f, pp.Normal);
                    }
                    float uc = pp.EaveLength * 0.5f;
                    ChalkLine.SnapWorld(pp.At(uc, 0.02f, h), pp.At(uc, ShingleMesh.FieldTop(pp) - 0.02f, h), 0.1f, pp.Normal);
                }, Band(p, 0f, p.SlopeLength));
            }

            // starter strips
            var starter = new Meter(105 * Units.Foot);
            for (int pi = 0; pi < roof.Planes.Count; pi++)
            {
                var p = roof.Planes[pi]; var pp = p;
                job.Add("Starter strip along the " + p.Name.ToLower() + " eave: sealant down, 1/2\" past the drip edge", ToolSets.Roof, () =>
                {
                    var m = new RoofMesh();
                    ShingleMesh.Starter(m, pp, main, edge);
                    m.Attach(root, "Starter strip (" + pp.Name + ")");
                }, Band(p, ShingleMesh.FirstButt(p), ShingleMesh.FirstButt(p) + I(7.5f))).Use("starter-strip", starter.Take(p.EaveLength + 2 * ShingleMesh.RakeOut(p))).Use("roof-nails", Mathf.CeilToInt(p.EaveLength / I(12f)) * 2);
            }

            // shingle courses
            var bundles = new Meter(ShinglesPerBundle);
            for (int pi = 0; pi < roof.Planes.Count; pi++)
            {
                var p = roof.Planes[pi];
                int n = ShingleMesh.CourseCount(p);
                for (int c = 0; c < n; c++)
                {
                    var pp = p; int cc = c, pidx = pi;
                    float vB = ShingleMesh.FirstButt(p) + c * ShingleMesh.Exposure;
                    float width = (p.HipStart || p.HipEnd) ? p.WidthAt(Mathf.Max(0f, vB)) : p.EaveLength + 2 * ShingleMesh.RakeOut(p);
                    int est = Mathf.Max(1, Mathf.CeilToInt(width / ShingleMesh.Length) + 1);
                    job.Add("Shingle course " + (c + 1) + "/" + n + " (" + p.Name.ToLower() + "): " + est + " shingles, " + NailsPerShingle + " nails each on the nail line",
                        ToolSets.Roof, () =>
                        {
                            var m = new RoofMesh();
                            ShingleMesh.Course(m, pp, pidx, cc, main, shadow, edge);
                            if (!m.Empty) m.Attach(root, "Shingle course " + (cc + 1) + " (" + pp.Name + ")");
                        }, Band(p, vB, vB + ShingleMesh.Exposure)).Use("shingles-arch", bundles.Take(est)).Use("roof-nails", est * NailsPerShingle);
                }
            }

            // ridge vent over the slot, then caps (hips first, then the ridge)
            float hs = ShingleMesh.H0 + ShingleMesh.TButt * 0.6f;
            float ventH = I(0.75f), ventW = I(5.5f);
            bool vent = roof.HasRidge && roof.Ridge.PlaneA >= 0 && roof.Ridge.PlaneB >= 0 && roof.Planes[roof.Ridge.PlaneA].RidgeGap > 0f;
            Vector3 rdir = roof.HasRidge ? (roof.Ridge.B - roof.Ridge.A).normalized : Vector3.zero;
            float ext = roof.Hip ? 0f : I(1.2f);
            Vector3 rA = roof.Ridge.A - rdir * ext, rB = roof.Ridge.B + rdir * ext;
            if (vent)
            {
                var pa = roof.Planes[roof.Ridge.PlaneA]; var pb = roof.Planes[roof.Ridge.PlaneB];
                int sections = Mathf.CeilToInt(Vector3.Distance(rA, rB) / (4 * Units.Foot) - 0.001f);
                job.Add("Ridge vent: " + sections + " x 4' sections over the slot, 1-3/4\" coil nails", ToolSets.Roof, () =>
                {
                    var m = new RoofMesh();
                    ShingleMesh.RidgeVent(m, pa, pb, rA + rdir * I(0.5f), rB - rdir * I(0.5f), hs, ventH, ventW, RoofTextures.RidgeVent);
                    m.Attach(root, "Ridge vent");
                }, Line(rA, rB, 0.3f)).Use("ridge-vent", sections).Use("roof-nails", sections * 8);
            }
            var capMeter = new Meter(33 * Units.Foot);
            for (int hi = 0; hi < roof.Hips.Count; hi++)
            {
                var e = roof.Hips[hi]; int idx = hi;
                if (e.PlaneA < 0 || e.PlaneB < 0) continue;
                var pa = roof.Planes[e.PlaneA]; var pb = roof.Planes[e.PlaneB];
                Vector3 d = (e.B - e.A).normalized;
                Vector3 A = e.A;
                job.Add("Hip cap shingles (hip " + (hi + 1) + "): bottom up, 6-5/8\" exposure, 2 nails each", ToolSets.Roof, () =>
                {
                    var m = new RoofMesh();
                    ShingleMesh.Caps(m, pa, pb, A, e.B, hs + I(0.02f), ShingleMesh.H0, I(6f), main, edge, 900 + idx);
                    m.Attach(root, "Hip caps " + (idx + 1));
                }, Line(A, e.B, 0.3f)).Use("ridge-cap", capMeter.Take(Vector3.Distance(A, e.B))).Use("roof-nails", Mathf.CeilToInt(Vector3.Distance(A, e.B) / (6.625f * Units.Inch)) * 2);
            }
            if (roof.HasRidge && roof.Ridge.PlaneA >= 0 && roof.Ridge.PlaneB >= 0)
            {
                var pa = roof.Planes[roof.Ridge.PlaneA]; var pb = roof.Planes[roof.Ridge.PlaneB];
                float baseH = vent ? hs + ventH : hs + I(0.02f);
                float lower = vent ? hs + ventH : ShingleMesh.H0;
                float half = vent ? ventW : I(6f);
                float len = Vector3.Distance(rA, rB);
                job.Add("Ridge cap shingles: lay from one end, 6-5/8\" exposure, nails through the vent", ToolSets.Roof, () =>
                {
                    var m = new RoofMesh();
                    ShingleMesh.Caps(m, pa, pb, rA, rB, baseH, lower, half, main, edge, 777);
                    m.Attach(root, "Ridge caps");
                }, Line(rA, rB, 0.3f)).Use("ridge-cap", capMeter.Take(len)).Use("roof-nails", Mathf.CeilToInt(len / (6.625f * Units.Inch)) * 2);
            }
            job.Completed += () => roof.Shingled = true;
            return job;
        }

        /// <summary>Dry-in followed by shingles as one job (crew mode).</summary>
        public static FramingJob All(RoofInfo roof)
        {
            var a = DryIn(roof);
            var b = Shingles(roof);
            foreach (var s in b.Steps) a.Steps.Add(s);
            a.Title = "Roofing (dry-in + shingles)";
            b.Steps.Clear();
            b.enabled = false;   // its GameObject stays: shingle meshes are parented to it
            roof.ShinglesJob = a;
            a.Completed += () => roof.Shingled = true;
            return a;
        }

        // ------------------------------------------------------------------ flashing & staging

        /// <summary>Step flashing (4x4x8 galvanized, one per course) where a roof plane dies into a wall.
        /// uWall = the wall line in plane u; wallOnLeft = the wall is at smaller u. Pieces run from course butt vFrom to vTo.</summary>
        public static FramingJob StepFlashing(RoofInfo roof, int planeIndex, float uWall, bool wallOnLeft, float vFrom, float vTo)
        {
            var job = NewJob("Step flashing (" + roof.Planes[planeIndex].Name.ToLower() + ")");
            var p = roof.Planes[planeIndex];
            var root = job.transform;
            float E = ShingleMesh.Exposure;
            int c0 = Mathf.Max(0, Mathf.FloorToInt((vFrom - ShingleMesh.FirstButt(p)) / E));
            for (int c = c0; ; c++)
            {
                float vB = ShingleMesh.FirstButt(p) + c * E;
                if (vB > vTo) break;
                float v0 = vB + E + I(0.25f), v1 = v0 + I(8f);
                float dirU = wallOnLeft ? 1f : -1f;
                var pp = p;
                job.Add("Step flashing piece at course " + (c + 1) + ": 4\" on the shingle, 4\" up the wall, one nail high", ToolSets.Roof, () =>
                {
                    var m = new RoofMesh();
                    int s = m.Mat(RoofTextures.Flashing);
                    float h = ShingleMesh.H0 - 0.0003f;
                    Vector3 a = pp.At(uWall, v0, h), b = pp.At(uWall, v1, h), a2 = pp.At(uWall + dirU * I(4f), v0, h), b2 = pp.At(uWall + dirU * I(4f), v1, h);
                    m.Quad2(a, b, b2, a2, new Vector2(0, 0), new Vector2(0.2f, 0), new Vector2(0.2f, 0.1f), new Vector2(0, 0.1f), pp.Normal, s);
                    m.Quad2(a, b, b + Vector3.up * I(4f), a + Vector3.up * I(4f), new Vector2(0, 0), new Vector2(0.2f, 0), new Vector2(0.2f, 0.1f), new Vector2(0, 0.1f), pp.AlongEave * dirU, s);
                    m.Attach(root, "Step flashing");
                }, Line(pp.At(uWall, v0), pp.At(uWall, v1), 0.2f)).Use("step-flash").Use("roof-nails", 1);
            }
            return job;
        }

        /// <summary>Roof jacks (pairs, ~4'-8' apart, nailed into rafters through the deck) with a 2x8 plank, so roofers can work
        /// on steep roofs without slipping (RoofSafety). v = up-slope position of the jacks; u0..u1 = plank extent.</summary>
        public static FramingJob RoofJacks(RoofInfo roof, int planeIndex, float v, float u0, float u1)
        {
            var p = roof.Planes[planeIndex];
            var job = NewJob("Roof jacks + plank (" + p.Name.ToLower() + ")");
            var root = job.transform;
            float span = u1 - u0;
            int jacks = Mathf.Max(2, Mathf.CeilToInt(span / (6 * Units.Foot)) + 1);
            var us = new List<float>();
            for (int i = 0; i < jacks; i++) us.Add(Mathf.Lerp(u0 + I(12f), u1 - I(12f), i / (float)(jacks - 1)));
            // snap each jack to the nearest rafter
            for (int i = 0; i < us.Count; i++)
            {
                float best = us[i], bd = float.MaxValue;
                foreach (var f in p.FramingU) if (Mathf.Abs(f - us[i]) < bd) { bd = Mathf.Abs(f - us[i]); best = f; }
                if (bd < I(16f)) us[i] = best;
            }
            foreach (var u in us)
            {
                float uu = u;
                job.Add("Nail a roof jack into the rafter @ " + Units.Format(u) + " (3 x 16d through the deck)", ToolSets.Fasten, () => RoofJack.Create(root, p, uu, v),
                    () => (p.At(uu, v, 0.1f), new Vector3(0.1f, 0.2f, 0.35f), Quaternion.LookRotation(p.UpSlope, p.Normal))).Use("roof-jack", 1).Nails = 3;
            }
            job.Add("Lay the 2x8 plank on the jacks, nail through the jack lips", ToolSets.Hands, () => RoofJack.Plank(root, p, u0, u1, v),
                () => (p.At((u0 + u1) * 0.5f, v, 0.15f), new Vector3(0.2f, 0.05f, span), Quaternion.LookRotation(p.AlongEave, Vector3.up))).Nails = 2;
            return job;
        }
    }
}
