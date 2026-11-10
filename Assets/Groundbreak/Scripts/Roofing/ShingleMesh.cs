using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Real geometry for laminated architectural shingles and the layers under them. Everything is built in a
    /// RoofPlane's (u, v, h) coordinates and clipped to the plane outline, so rakes, hips and the ridge cut cleanly.</summary>
    public static class ShingleMesh
    {
        static float I(float inch) => inch * Units.Inch;

        // shingle dimensions (inches -> meters)
        public static readonly float Exposure = 5.625f * Units.Inch;
        public static readonly float Length = 39.375f * Units.Inch;
        public static readonly float Height = 13.25f * Units.Inch;
        public static readonly float StaggerStep = 6.25f * Units.Inch;

        // layer heights above the deck top (meters)
        public const float HDrip = 0.0007f, HIce = 0.0016f, HUnder = 0.0022f, HUnderLap = 0.0005f, HRakeDrip = 0.0034f;
        public const float HStarter = 0.0016f, H0 = 0.0052f, TButt = 0.0032f, TLam = 0.0016f;

        /// <summary>Shingle overhang past the deck edge (fascia 3/4" + drip edge + ~1/2").</summary>
        public static float EaveOut(RoofPlane p) => I(1.3f) / p.Cos;
        public static float RakeOut(RoofPlane p) => I(1.2f);

        public static float FirstButt(RoofPlane p) => -EaveOut(p);

        /// <summary>Top of the shingle field on a plane (stops at the ridge-vent slot; ridge caps cover the rest).</summary>
        public static float FieldTop(RoofPlane p) => p.SlopeLength - (p.RidgeGap > 0f ? p.RidgeGap : 0f);

        public static int CourseCount(RoofPlane p) => Mathf.Max(1, Mathf.CeilToInt((FieldTop(p) - FirstButt(p) - I(0.5f)) / Exposure));

        public static List<HalfPlane> FieldBounds(RoofPlane p) => p.Bounds(RakeOut(p), EaveOut(p) + 0.001f, FieldTop(p));

        struct Seg { public float a, b; public bool tab; public Vector2 uv; }

        /// <summary>Plane polygon (u,v) lifted to world with h(v) = h0 + dh*(v - vRef).</summary>
        static void Face(RoofMesh m, RoofPlane p, List<Vector2> poly, float h0, float dhdv, float vRef, Vector2 uvOff, float uvVRef, int sub, Vector3 hint)
        {
            if (poly.Count < 3) return;
            var pts = new List<Vector3>(poly.Count); var uvs = new List<Vector2>(poly.Count);
            foreach (var q in poly)
            {
                pts.Add(p.At(q.x, q.y, h0 + dhdv * (q.y - vRef)));
                uvs.Add(new Vector2(q.x + uvOff.x, q.y - uvVRef + uvOff.y));
            }
            m.Poly(pts, uvs, hint, sub);
        }

        /// <summary>Vertical (along the normal) wall over a (u,v) segment between heights hb..ht (ht may vary with v).</summary>
        static void Wall(RoofMesh m, RoofPlane p, Vector2 a, Vector2 b, List<HalfPlane> bounds, float hb, float ht0, float dhdv, float vRef, int sub, Vector3 hint)
        {
            if (!Geo2.ClipSegment(ref a, ref b, bounds)) return;
            float ta = ht0 + dhdv * (a.y - vRef), tb = ht0 + dhdv * (b.y - vRef);
            float ba = hb + dhdv * (a.y - vRef), bb = hb + dhdv * (b.y - vRef);
            if (ta <= ba + 1e-5f && tb <= bb + 1e-5f) return;
            float len = (b - a).magnitude;
            m.Quad(p.At(a.x, a.y, ba), p.At(b.x, b.y, bb), p.At(b.x, b.y, tb), p.At(a.x, a.y, ta),
                   new Vector2(0, 0), new Vector2(len, 0), new Vector2(len, tb - bb), new Vector2(0, ta - ba), hint, sub);
        }

        /// <summary>One course of architectural shingles on a plane. Returns the number of shingles used.</summary>
        public static int Course(RoofMesh m, RoofPlane p, int planeIndex, int course, Material main, Material shadow, Material edge)
        {
            int sMain = m.Mat(main), sShadow = m.Mat(shadow), sEdge = m.Mat(edge);
            var bounds = FieldBounds(p);
            float E = Exposure, vB = FirstButt(p) + course * E;
            float vTop = FieldTop(p);
            if (vB >= vTop - 0.002f) return 0;
            float rOut = RakeOut(p);
            float uLeft = p.HipStart ? p.UMin(vB) - 0.01f : -rOut;
            float uRight = p.HipEnd ? p.UMax(vB) + 0.01f : p.EaveLength + rOut;
            float offset = (course * StaggerStep) % Length;
            float hBelow = course == 0 ? HUnder : H0;
            Vector3 down = -p.UpSlope;
            int count = 0;
            float dh = -TButt / E;   // nominal; per shingle thickness varies slightly
            for (int k = 0; ; k++)
            {
                float s0 = uLeft - offset + k * Length;
                if (s0 >= uRight) break;
                if (s0 + Length <= uLeft) continue;
                var rng = new RRand(RRand.Seed(planeIndex * 1000 + course, k, 77));
                float Ts = TButt * rng.Range(0.9f, 1.1f);
                float slope = -Ts / E;
                float c = rng.Range(3.8f, 4.75f) * Units.Inch;      // dragon-tooth cutout height
                float sl = I(0.3f);
                // tabs (top laminate reaching the butt) and cutouts (lower laminate showing through)
                var segs = new List<Seg>();
                float pos = 0f; bool tab = true;
                while (pos < Length - 0.001f)
                {
                    float w = tab ? rng.Range(4.2f, 8.8f) * Units.Inch : rng.Range(1.4f, 3.4f) * Units.Inch;
                    float b = Mathf.Min(Length, pos + w);
                    if (!tab && Length - b < I(1.5f)) b = Length;
                    segs.Add(new Seg { a = pos, b = b, tab = tab, uv = new Vector2(rng.Range(0f, 3f), rng.Range(0f, 3f)) });
                    pos = b; tab = !tab;
                }
                if (!segs[segs.Count - 1].tab && segs[segs.Count - 1].b - segs[segs.Count - 1].a < I(1.0f)) { var t = segs[segs.Count - 1]; t.tab = true; segs[segs.Count - 1] = t; }
                bool any = false;
                float hb0 = H0 + Ts;     // top laminate height at the butt
                for (int i = 0; i < segs.Count; i++)
                {
                    var sg = segs[i];
                    float a = s0 + sg.a, b = s0 + sg.b;
                    if (sg.tab)
                    {
                        // column: from the middle of the previous cutout to the middle of the next one
                        float colA = i > 0 ? s0 + (segs[i - 1].a + segs[i - 1].b) * 0.5f : s0;
                        float colB = i + 2 < segs.Count ? s0 + (segs[i + 1].a + segs[i + 1].b) * 0.5f : s0 + Length;
                        var upper = Geo2.Clip(Geo2.Rect(colA, vB + c, colB, vB + E), bounds);
                        Face(m, p, upper, hb0, slope, vB, sg.uv, vB, sMain, p.Normal);
                        float ta = Mathf.Max(colA, a - (i > 0 ? sl : 0f)), tb = Mathf.Min(colB, b + (i + 1 < segs.Count ? sl : 0f));
                        var lower = Geo2.Clip(new List<Vector2> { new Vector2(a, vB), new Vector2(b, vB), new Vector2(tb, vB + c), new Vector2(ta, vB + c) }, bounds);
                        if (lower.Count >= 3 || upper.Count >= 3) any = true;
                        Face(m, p, lower, hb0, slope, vB, sg.uv, vB, sMain, p.Normal);
                        Wall(m, p, new Vector2(a, vB), new Vector2(b, vB), bounds, hBelow, hb0, 0f, vB, sEdge, down);
                    }
                    else
                    {
                        float ga = a + sl, gb = b - (i + 1 < segs.Count ? sl : 0f);
                        if (gb < ga + I(0.2f)) { float mid = (ga + gb) * 0.5f; ga = mid - I(0.1f); gb = mid + I(0.1f); }
                        var gap = Geo2.Clip(new List<Vector2> { new Vector2(a, vB), new Vector2(b, vB), new Vector2(gb, vB + c), new Vector2(ga, vB + c) }, bounds);
                        Face(m, p, gap, hb0 - TLam, slope, vB, sg.uv * 0.7f, vB, sShadow, p.Normal);
                        // cutout walls (thickness of the top laminate) and the butt of the lower laminate
                        Wall(m, p, new Vector2(a, vB), new Vector2(ga, vB + c), bounds, hb0 - TLam, hb0, slope, vB, sEdge, p.AlongEave);
                        if (i + 1 < segs.Count) Wall(m, p, new Vector2(b, vB), new Vector2(gb, vB + c), bounds, hb0 - TLam, hb0, slope, vB, sEdge, -p.AlongEave);
                        Wall(m, p, new Vector2(ga, vB + c), new Vector2(gb, vB + c), bounds, hb0 - TLam + slope * c, hb0 + slope * c, 0f, vB, sEdge, down);
                        Wall(m, p, new Vector2(a, vB), new Vector2(b, vB), bounds, hBelow, hb0 - TLam, 0f, vB, sEdge, down);
                    }
                }
                if (any) count++;
            }
            // rake ends: the stacked shingle edge seen from the side
            float v1 = Mathf.Min(vB + E, vTop);
            float hEnd = H0 + TButt * (1f - (v1 - vB) / E);
            if (!p.HipStart)
            {
                float u = -rOut;
                m.Quad(p.At(u, vB, HUnder), p.At(u, vB, H0 + TButt), p.At(u, v1, hEnd), p.At(u, v1, HUnder),
                       new Vector2(vB, 0), new Vector2(vB, 0.01f), new Vector2(v1, 0.01f), new Vector2(v1, 0), -p.AlongEave, sEdge);
            }
            if (!p.HipEnd)
            {
                float u = p.EaveLength + rOut;
                m.Quad(p.At(u, vB, HUnder), p.At(u, vB, H0 + TButt), p.At(u, v1, hEnd), p.At(u, v1, HUnder),
                       new Vector2(vB, 0), new Vector2(vB, 0.01f), new Vector2(v1, 0.01f), new Vector2(v1, 0), p.AlongEave, sEdge);
            }
            return count;
        }

        /// <summary>Starter strip (adhesive edge down) along the eave, flush with where the first course's butt will be.</summary>
        public static float Starter(RoofMesh m, RoofPlane p, Material main, Material edge)
        {
            int sm = m.Mat(main), se = m.Mat(edge);
            var b = FieldBounds(p);
            float v0 = FirstButt(p), v1 = v0 + I(7.5f);
            var poly = Geo2.Clip(Geo2.Rect(-10f, v0, p.EaveLength + 10f, v1), b);
            Face(m, p, poly, HUnder + HStarter, 0f, v0, new Vector2(0.37f, 0.11f), v0, sm, p.Normal);
            Wall(m, p, new Vector2(-10f, v0), new Vector2(p.EaveLength + 10f, v0), b, HUnder - 0.0004f, HUnder + HStarter, 0f, v0, se, -p.UpSlope);
            float len = 0f; if (poly.Count >= 3) { float mn = float.MaxValue, mx = float.MinValue; foreach (var q in poly) { mn = Mathf.Min(mn, q.x); mx = Mathf.Max(mx, q.x); } len = mx - mn; }
            return len;
        }

        /// <summary>Flat membrane band (ice &amp; water, underlayment course) on the deck in a v range, clipped to the plane.</summary>
        public static void Membrane(RoofMesh m, RoofPlane p, float v0, float v1, float h, Material mat, float extraRake = 0f, float extraEave = 0f)
        {
            int s = m.Mat(mat);
            var poly = Geo2.Clip(Geo2.Rect(-10f, v0, p.EaveLength + 10f, v1), p.Bounds(extraRake, extraEave, p.SlopeLength));
            Face(m, p, poly, h, 0f, v0, Vector2.zero, v0, s, p.Normal);
            // the lower edge, visible as a thin step
            Wall(m, p, new Vector2(-10f, Mathf.Max(v0, -extraEave)), new Vector2(p.EaveLength + 10f, Mathf.Max(v0, -extraEave)), p.Bounds(extraRake, extraEave, p.SlopeLength), Mathf.Max(0f, h - 0.0012f), h, 0f, v0, s, -p.UpSlope);
        }

        /// <summary>Plastic-capped staple head (1" cap) on the underlayment.</summary>
        public static void CapStaple(RoofMesh m, RoofPlane p, float u, float v, float h, int sub)
        {
            const int n = 8; float r = I(0.5f), t = 0.0006f;
            var top = new List<Vector3>(n); var uv = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                top.Add(p.At(u + Mathf.Cos(a) * r, v + Mathf.Sin(a) * r, h + t));
                uv.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }
            m.Poly(top, uv, p.Normal, sub);
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                Vector3 p0 = p.At(u + Mathf.Cos(a0) * r, v + Mathf.Sin(a0) * r, h), p1 = p.At(u + Mathf.Cos(a1) * r, v + Mathf.Sin(a1) * r, h);
                Vector3 n0 = p.AlongEave * Mathf.Cos((a0 + a1) * 0.5f) + p.UpSlope * Mathf.Sin((a0 + a1) * 0.5f);
                m.Quad(p0, p1, p1 + p.Normal * t, p0 + p.Normal * t, Vector2.zero, new Vector2(0.01f, 0), new Vector2(0.01f, 0.001f), new Vector2(0, 0.001f), n0, sub);
            }
        }

        /// <summary>Drip edge profile (flange on the deck, over the fascia top, plumb leg, kick-out hem) swept along a line.
        /// 'at(s, d, h)' maps (distance along the run, distance onto the deck from the edge (negative = outward), height above deck).</summary>
        public static void DripEdge(RoofMesh m, System.Func<float, float, float, Vector3> at, float s0, float s1, float flangeH, Vector3 outH, Vector3 deckNormal, Material mat, float fascia)
        {
            int sub = m.Mat(mat);
            float fl = I(2.25f);
            Vector3 A0 = at(s0, fl, flangeH), A1 = at(s1, fl, flangeH);
            Vector3 B0 = at(s0, 0f, flangeH), B1 = at(s1, 0f, flangeH);
            Vector3 C0 = at(s0, -(fascia + I(0.06f)), flangeH), C1 = at(s1, -(fascia + I(0.06f)), flangeH);
            Vector3 D0 = C0 + Vector3.down * I(1.6f), D1 = C1 + Vector3.down * I(1.6f);
            Vector3 E0 = D0 + outH * I(0.45f) + Vector3.down * I(0.4f), E1 = D1 + outH * I(0.45f) + Vector3.down * I(0.4f);
            float L = (s1 - s0);
            Vector2 U(float s, float d) => new Vector2(s, d);
            m.Quad2(A0, A1, B1, B0, U(0, fl), U(L, fl), U(L, 0), U(0, 0), deckNormal, sub);
            m.Quad2(B0, B1, C1, C0, U(0, 0), U(L, 0), U(L, -0.03f), U(0, -0.03f), deckNormal, sub);
            m.Quad2(C0, C1, D1, D0, U(0, 0.1f), U(L, 0.1f), U(L, 0.14f), U(0, 0.14f), outH, sub);
            m.Quad2(D0, D1, E1, E0, U(0, 0.2f), U(L, 0.2f), U(L, 0.215f), U(0, 0.215f), outH + Vector3.down, sub);
        }

        /// <summary>Ridge / hip line cross-section helpers: apex point at height h above both planes.</summary>
        public static Vector3 Apex(Vector3 r, Vector3 na, Vector3 nb, float h) => r + (na + nb) / (1f + Vector3.Dot(na, nb)) * h;

        /// <summary>Unit vector in plane p, perpendicular to the edge direction, pointing into the plane (away from the edge).</summary>
        public static Vector3 Wing(RoofPlane p, Vector3 edgePoint, Vector3 edgeDir)
        {
            Vector3 w = Vector3.Cross(p.Normal, edgeDir).normalized;
            Vector3 inside = p.At(p.EaveLength * 0.5f, p.SlopeLength * 0.3f) - edgePoint;
            if (Vector3.Dot(w, inside) < 0f) w = -w;
            return w;
        }

        /// <summary>Shingle-over ridge vent (black plastic, 4' sections) centered on a ridge. Returns sections used.</summary>
        public static int RidgeVent(RoofMesh m, RoofPlane pa, RoofPlane pb, Vector3 A, Vector3 B, float baseH, float height, float halfWidth, Material mat)
        {
            int s = m.Mat(mat);
            Vector3 dir = (B - A); float len = dir.magnitude; dir /= len;
            Vector3 wa = Wing(pa, A, dir), wb = Wing(pb, A, dir);
            float sec = 4 * Units.Foot;
            int n = Mathf.CeilToInt(len / sec - 0.001f);
            float Ht = baseH + height;
            for (int i = 0; i < n; i++)
            {
                float t0 = i * sec + (i > 0 ? I(0.06f) : 0f), t1 = Mathf.Min(len, (i + 1) * sec);
                Vector3 R0 = A + dir * t0, R1 = A + dir * t1;
                foreach (var side in new[] { (pa, wa), (pb, wb) })
                {
                    var (p, w) = side;
                    Vector3 ap0 = Apex(R0, pa.Normal, pb.Normal, Ht), ap1 = Apex(R1, pa.Normal, pb.Normal, Ht);
                    Vector3 e0t = R0 + w * halfWidth + p.Normal * Ht, e1t = R1 + w * halfWidth + p.Normal * Ht;
                    Vector3 e0b = R0 + w * halfWidth + p.Normal * baseH, e1b = R1 + w * halfWidth + p.Normal * baseH;
                    m.Quad(ap0, ap1, e1t, e0t, new Vector2(t0, 0), new Vector2(t1, 0), new Vector2(t1, halfWidth), new Vector2(t0, halfWidth), p.Normal, s);
                    m.Quad(e0b, e1b, e1t, e0t, new Vector2(t0, 0), new Vector2(t1, 0), new Vector2(t1, height), new Vector2(t0, height), w, s);
                    // end plugs
                    Vector3 apb0 = Apex(R0, pa.Normal, pb.Normal, baseH), apb1 = Apex(R1, pa.Normal, pb.Normal, baseH);
                    m.Quad(apb0, ap0, e0t, e0b, new Vector2(0, 0), new Vector2(0, height), new Vector2(halfWidth, height), new Vector2(halfWidth, 0), -dir, s);
                    m.Quad(apb1, ap1, e1t, e1b, new Vector2(0, 0), new Vector2(0, height), new Vector2(halfWidth, height), new Vector2(halfWidth, 0), dir, s);
                }
            }
            return n;
        }

        /// <summary>Cap shingles (12" x 12", 6-5/8" exposure) folded over a ridge or hip, laid from A toward B.
        /// Each cap's butt is raised by its thickness so every piece casts a shadow line. Returns pieces used.</summary>
        public static int Caps(RoofMesh m, RoofPlane pa, RoofPlane pb, Vector3 A, Vector3 B, float baseH, float lowerH, float halfWidth, Material main, Material edge, int seed)
        {
            int sm = m.Mat(main), se = m.Mat(edge);
            Vector3 dir = (B - A); float len = dir.magnitude; dir /= len;
            Vector3 wa = Wing(pa, A, dir), wb = Wing(pb, A, dir);
            float Ec = 6.625f * Units.Inch, Tc = I(0.13f);
            int n = Mathf.CeilToInt(len / Ec - 0.001f);
            for (int i = 0; i < n; i++)
            {
                float t0 = i * Ec, t1 = Mathf.Min(len, t0 + Ec);
                var rng = new RRand(RRand.Seed(seed, i, 5));
                Vector2 off = new Vector2(rng.Range(0f, 3f), rng.Range(0f, 3f));
                float h0 = baseH + Tc, h1 = baseH + Tc * (1f - (t1 - t0) / Ec);
                Vector3 R0 = A + dir * t0, R1 = A + dir * t1;
                foreach (var side in new[] { (pa, wa), (pb, wb) })
                {
                    var (p, w) = side;
                    Vector3 a0 = Apex(R0, pa.Normal, pb.Normal, h0), a1 = Apex(R1, pa.Normal, pb.Normal, h1);
                    Vector3 e0 = R0 + w * halfWidth + p.Normal * h0, e1 = R1 + w * halfWidth + p.Normal * h1;
                    m.Quad(a0, a1, e1, e0, off + new Vector2(0, 0), off + new Vector2(0, t1 - t0), off + new Vector2(halfWidth, t1 - t0), off + new Vector2(halfWidth, 0), p.Normal, sm);
                    // butt (raised edge facing A)
                    Vector3 ab = Apex(R0, pa.Normal, pb.Normal, baseH), eb = R0 + w * halfWidth + p.Normal * lowerH;
                    m.Quad(ab, a0, e0, eb, new Vector2(0, 0), new Vector2(0, 0.01f), new Vector2(halfWidth, 0.01f), new Vector2(halfWidth, 0), -dir, se);
                    // outer edge down to the shingles / vent
                    Vector3 e0l = R0 + w * halfWidth + p.Normal * lowerH, e1l = R1 + w * halfWidth + p.Normal * lowerH;
                    m.Quad(e0l, e1l, e1, e0, new Vector2(t0, 0), new Vector2(t1, 0), new Vector2(t1, 0.01f), new Vector2(t0, 0.01f), w, se);
                }
            }
            return n;
        }
    }
}
