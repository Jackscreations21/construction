using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Roof framing jobs: engineered Fink trusses (with an optional boom truck), stick framing (gable or hip) and the
    /// roof deck. Every job keeps its <see cref="RoofInfo"/> on the job's GameObject:
    /// <c>RoofFraming.InfoOf(job)</c> == <c>job.GetComponent&lt;RoofInfo&gt;()</c> (available as soon as the job is created).</summary>
    public static class RoofFraming
    {
        static float I(float inch) => inch * Units.Inch;
        /// <summary>Boom truck swings each truss in (cosmetic; the truss is placed immediately). Tests may turn it off.</summary>
        public static bool AnimateBoom = true;

        public static RoofInfo InfoOf(FramingJob job) => job != null ? job.GetComponent<RoofInfo>() : null;
        public static int SnapPitch(int rise) => Mathf.Clamp(rise, 3, 12);

        // ------------------------------------------------------------------ shared geometry

        /// <summary>Roof section math in the roof frame (x along Length, y = world height, z across Width).</summary>
        class Geo
        {
            public Footprint Fp;
            public float L, W, P, WD, s, r, cos, sin;
            public float d, D;        // rafter / top chord depth (board) and its plumb (vertical) depth
            public float Yw;          // top of rafters at the outer wall line (z = 0)
            public float tailH;       // horizontal tail from the wall line to the plumb cut
            public float zE;          // deck (sheathing) edge = outer face of sub-fascia (negative)
            public float rakeIn;      // fly rafter outer face, outside the end wall (gable)
            public float tSh = Units.In(0.4375f);
            public int rise;
            public float Top(float q) => Yw + r * q;
            public float Deck(float q) => Top(q) + tSh / cos;

            public Geo(Footprint fp, int rise, float depthIn, bool truss, float eaveOhIn, float rakeOhIn)
            {
                Fp = fp; this.rise = rise;
                L = fp.Length; W = fp.Width; P = fp.PlateTopY; WD = fp.WallDepth > 0 ? fp.WallDepth : I(3.5f);
                s = W * 0.5f; r = rise / 12f;
                float th = Mathf.Atan(r); cos = Mathf.Cos(th); sin = Mathf.Sin(th);
                d = I(depthIn); D = d / cos;
                Yw = truss ? P + I(3.5f) + D : P - r * WD + D;
                tailH = Mathf.Max(I(2f), I(eaveOhIn) - I(2.25f));   // 2x6 sub-fascia + 1x6 fascia beyond the plumb cut
                zE = -(tailH + I(1.5f));
                rakeIn = Mathf.Max(I(2f), I(rakeOhIn) - I(0.75f));
            }
        }

        static Func<(Vector3, Vector3, Quaternion)> Ghost(Transform root, Vector3 c, Vector3 size, Quaternion rot) =>
            () => (root.TransformPoint(c), size, root.rotation * rot);

        static Transform MakeRoot(FramingJob job, Footprint fp)
        {
            job.transform.SetPositionAndRotation(new Vector3(fp.Origin.x, 0f, fp.Origin.z), fp.Rotation);
            return job.transform;
        }

        /// <summary>A framing member with a custom cut visual. Geometry is authored in the roof frame (root local space).</summary>
        static StructuralMember Member(Transform root, string defId, MemberRole role, Connector conn, Vector3 center, Quaternion rot, Vector3 size,
                                       Action<FrameKit> build, string name)
        {
            var inv = Matrix4x4.TRS(center, rot, Vector3.one).inverse;
            int seed = Mathf.Abs((int)(center.x * 997f) ^ (int)(center.z * 7919f) ^ (int)(center.y * 131f));
            var m = Framer.Custom(defId, size, root, center, rot, role, (b, s) =>
            {
                b.Transform = inv;
                b.UvOffset = new Vector2((seed % 97) * 0.173f, (seed % 89) * 0.211f);
                build(new FrameKit(b, s));
            }, conn, "Roof");
            m.gameObject.name = name;
            return m;
        }

        /// <summary>Box (center, rot, size) of a member that runs along 'grain' with thickness axis 'side'.</summary>
        static (Vector3 c, Quaternion rot, Vector3 size) OrientedBox(Vector3 c, Vector3 grain, Vector3 side, float thick, float depth, float len)
        {
            grain.Normalize();
            Vector3 perp = Vector3.Cross(grain, side).normalized;
            if (perp.y < 0f) perp = -perp;
            return (c, Quaternion.LookRotation(grain, perp), new Vector3(thick, depth, len));
        }

        /// <summary>Sloped rafter-type member: plan origin O (y ignored) at the outer wall line, horizontal dir h, top line
        /// y = top0 + m q, vertical depth Dv, from qTail (plumb cut) to qEnd (plumb cut). Optional birdsmouth seat at seatY.</summary>
        static StructuralMember Rafter(Transform root, string size, MemberRole role, Connector conn, Vector3 O, Vector3 h, float top0, float m, float Dv,
                                       float qTail, float qEnd, bool birdsmouth, float seatY, string name, SurfaceKind face = SurfaceKind.PineLumber)
        {
            O.y = 0f; h.y = 0f; h.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, h).normalized;
            float slopeLen = (qEnd - qTail) * Mathf.Sqrt(1f + m * m);
            float boardDepth = Dv / Mathf.Sqrt(1f + m * m);
            float qm = (qTail + qEnd) * 0.5f;
            Vector3 c = O + h * qm + Vector3.up * (top0 + m * qm - Dv * 0.5f);
            var box = OrientedBox(c, h + Vector3.up * m, side, I(1.5f), boardDepth, slopeLen);
            float Tp(float q) => top0 + m * q;
            float Bt(float q) => top0 + m * q - Dv;
            var parts = new List<List<Vector2>>();
            float qx = m > 1e-4f ? (seatY - (top0 - Dv)) / m : qTail;
            if (birdsmouth && qx > 0f && qx < qEnd && 0f > qTail)
            {
                parts.Add(new List<Vector2> { new Vector2(qTail, Bt(qTail)), new Vector2(0f, Bt(0f)), new Vector2(0f, Tp(0f)), new Vector2(qTail, Tp(qTail)) });
                parts.Add(new List<Vector2> { new Vector2(0f, seatY), new Vector2(qx, seatY), new Vector2(qx, Tp(qx)), new Vector2(0f, Tp(0f)) });
                parts.Add(new List<Vector2> { new Vector2(qx, Bt(qx)), new Vector2(qEnd, Bt(qEnd)), new Vector2(qEnd, Tp(qEnd)), new Vector2(qx, Tp(qx)) });
            }
            else parts.Add(new List<Vector2> { new Vector2(qTail, Bt(qTail)), new Vector2(qEnd, Bt(qEnd)), new Vector2(qEnd, Tp(qEnd)), new Vector2(qTail, Tp(qTail)) });
            var stock = RoofStock.Pick(size, slopeLen);
            return Member(root, stock.id, role, conn, box.c, box.rot, box.size, k =>
            {
                foreach (var p in parts) k.Prism(p, O, h, Vector3.up, side, -I(0.75f), I(0.75f), face, h + Vector3.up * m);
            }, name);
        }

        /// <summary>Flat-profile member in the (z,y) plane at x0..x1 (studs, collar ties, ceiling joists, webs), axis-aligned box.</summary>
        static StructuralMember PlanarYZ(Transform root, string defId, MemberRole role, Connector conn, List<Vector2> poly, float x0, float x1,
                                         Vector3 grainZY, string name, SurfaceKind face = SurfaceKind.PineLumber)
        {
            float zmin = float.MaxValue, zmax = float.MinValue, ymin = float.MaxValue, ymax = float.MinValue;
            foreach (var p in poly) { zmin = Mathf.Min(zmin, p.x); zmax = Mathf.Max(zmax, p.x); ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y); }
            var c = new Vector3((x0 + x1) * 0.5f, (ymin + ymax) * 0.5f, (zmin + zmax) * 0.5f);
            var size = new Vector3(Mathf.Abs(x1 - x0), ymax - ymin, zmax - zmin);
            Vector3 grain = new Vector3(0f, grainZY.y, grainZY.x);
            return Member(root, defId, role, conn, c, Quaternion.identity, size, k =>
                k.Prism(poly, new Vector3(0f, 0f, 0f), Vector3.forward, Vector3.up, Vector3.right, x0, x1, face, grain), name);
        }

        /// <summary>Straight rectangular board between two points (centerline), wide face perpendicular to 'thin'.</summary>
        static StructuralMember Board(Transform root, string defId, MemberRole role, Connector conn, Vector3 a, Vector3 b, Vector3 thin, float width, float thick,
                                      string name, SurfaceKind face = SurfaceKind.PineLumber)
        {
            Vector3 dir = (b - a); float len = dir.magnitude; dir /= len;
            Vector3 c = (a + b) * 0.5f;
            Quaternion rot = Quaternion.LookRotation(dir, thin);
            Vector3 wide = Vector3.Cross(thin, dir).normalized;
            var poly = Geo2.Rect(-len * 0.5f, -width * 0.5f, len * 0.5f, width * 0.5f);
            return Member(root, defId, role, conn, c, rot, new Vector3(width, thick, len), k =>
                k.Prism(poly, c, dir, wide, thin.normalized, -thick * 0.5f, thick * 0.5f, face, dir), name);
        }

        /// <summary>Galvanized hurricane tie (H2.5A-style) on a member at a bearing point (roof frame), wrapping onto the plate face.</summary>
        static void HurricaneTie(Transform member, Transform root, Vector3 bearing, Vector3 sideFace, Vector3 plateOut)
        {
            Vector3 world = root.TransformPoint(bearing);
            Vector3 lc = member.InverseTransformPoint(world);
            Vector3 ls = member.InverseTransformDirection(root.TransformDirection(sideFace)).normalized;
            Vector3 lo = member.InverseTransformDirection(root.TransformDirection(plateOut)).normalized;
            Vector3 lup = member.InverseTransformDirection(Vector3.up).normalized;
            Framer.Hardware(member, "HurricaneTie", lc, Quaternion.identity, (b, g) =>
            {
                // leg nailed to the side of the rafter/truss
                Vector3 legC = ls * I(0.78f) + lup * I(1.6f);
                b.AddBox(legC, new Vector3(1, 1, 1) * 0f + Abs(ls) * I(0.06f) + Abs(lup) * I(4.4f) + Abs(Vector3.Cross(ls, lup)) * I(1.75f), g);
                // twisted flange down the outside of the double top plate
                Vector3 flC = ls * I(1.6f) + lo * I(1.8f) - lup * I(1.4f);
                b.AddBox(flC, Abs(ls) * I(1.7f) + Abs(lo) * I(0.06f) + Abs(lup) * I(2.8f), g);
                // the twist between them
                b.AddBox(ls * I(1.2f) + lo * I(1.2f) + lup * I(0.1f), Abs(ls) * I(0.9f) + Abs(lo) * I(1.2f) + Abs(lup) * I(0.06f), g);
            });
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // ------------------------------------------------------------------ RoofInfo

        static RoofInfo MakeInfo(FramingJob job, Geo g, bool hip, bool trussed, List<float> framingX, List<float> framingZ, float eaveOhIn, float rakeOhIn)
        {
            var root = job.transform;
            var info = job.gameObject.AddComponent<RoofInfo>();
            info.Footprint = g.Fp; info.PitchRise = g.rise; info.Hip = hip; info.Trussed = trussed;
            info.EaveOverhang = I(eaveOhIn); info.RakeOverhang = hip ? info.EaveOverhang : I(rakeOhIn);
            info.FramingJob = job;
            Vector3 W(float x, float y, float z) => root.TransformPoint(new Vector3(x, y, z));
            Vector3 Dir(float x, float y, float z) => root.TransformDirection(new Vector3(x, y, z)).normalized;
            float c = g.cos, sn = g.sin;
            float vent0 = I(6f);

            RoofPlane NewPlane(string name, Vector3 start, Vector3 along, Vector3 up, Vector3 normal, float eaveLen, float slopeLen, bool h0, bool h1)
            {
                var p = new RoofPlane
                {
                    Name = name, EaveStart = start, AlongEave = along, UpSlope = up, Normal = normal, EaveLength = eaveLen, SlopeLength = slopeLen,
                    PitchRise = g.rise, HipStart = h0, HipEnd = h1, SheathingThickness = g.tSh, FasciaOutset = I(0.75f),
                };
                info.Planes.Add(p);
                return p;
            }

            if (!hip)
            {
                float x0 = -g.rakeIn, x1 = g.L + g.rakeIn, zE = g.zE;
                float sl = (g.s - zE) / c, el = x1 - x0;
                var front = NewPlane("Front", W(x0, g.Deck(zE), zE), Dir(1, 0, 0), Dir(0, sn, c), Dir(0, c, -sn), el, sl, false, false);
                var back = NewPlane("Back", W(x1, g.Deck(zE), g.W - zE), Dir(-1, 0, 0), Dir(0, sn, -c), Dir(0, c, sn), el, sl, false, false);
                foreach (var x in framingX) { front.FramingU.Add(x - x0); back.FramingU.Add(x1 - x); }
                front.FramingU.Sort(); back.FramingU.Sort();
                front.RidgeGap = back.RidgeGap = I(1f);
                front.VentU0 = vent0 - x0; front.VentU1 = g.L - vent0 - x0;
                back.VentU0 = x1 - (g.L - vent0); back.VentU1 = x1 - vent0;
                info.HasRidge = true;
                info.Ridge = new RoofEdge(W(x0, g.Deck(g.s), g.s), W(x1, g.Deck(g.s), g.s), 0, 1);
                info.Rakes.Add(new RoofEdge(front.At(0, 0), front.At(0, sl), 0, -1));
                info.Rakes.Add(new RoofEdge(front.At(el, 0), front.At(el, sl), 0, -1));
                info.Rakes.Add(new RoofEdge(back.At(0, 0), back.At(0, sl), 1, -1));
                info.Rakes.Add(new RoofEdge(back.At(el, 0), back.At(el, sl), 1, -1));
            }
            else
            {
                float o = -g.zE;
                float sl = (g.s + o) / c;
                float yE = g.Deck(-o);
                var front = NewPlane("Front", W(-o, yE, -o), Dir(1, 0, 0), Dir(0, sn, c), Dir(0, c, -sn), g.L + 2 * o, sl, true, true);
                var back = NewPlane("Back", W(g.L + o, yE, g.W + o), Dir(-1, 0, 0), Dir(0, sn, -c), Dir(0, c, sn), g.L + 2 * o, sl, true, true);
                var left = NewPlane("Left end", W(-o, yE, g.W + o), Dir(0, 0, -1), Dir(c, sn, 0), Dir(-sn, c, 0), g.W + 2 * o, sl, true, true);
                var right = NewPlane("Right end", W(g.L + o, yE, -o), Dir(0, 0, 1), Dir(-c, sn, 0), Dir(sn, c, 0), g.W + 2 * o, sl, true, true);
                foreach (var x in framingX) { front.FramingU.Add(x + o); back.FramingU.Add(g.L + o - x); }
                foreach (var z in framingZ) { left.FramingU.Add(g.W + o - z); right.FramingU.Add(z + o); }
                foreach (var p in info.Planes) p.FramingU.Sort();
                float ridgeLen = g.L - g.W;
                info.HasRidge = ridgeLen > I(1f);
                Vector3 rA = W(g.s, g.Deck(g.s), g.s), rB = W(g.L - g.s, g.Deck(g.s), g.s);
                info.Ridge = new RoofEdge(rA, rB, 0, 1);
                if (ridgeLen > I(24f))
                {
                    front.RidgeGap = back.RidgeGap = I(1f);
                    front.VentU0 = g.s + o + vent0; front.VentU1 = g.L - g.s + o - vent0;
                    back.VentU0 = front.VentU0; back.VentU1 = front.VentU1;
                }
                info.Hips.Add(new RoofEdge(W(-o, yE, -o), rA, 0, 2));
                info.Hips.Add(new RoofEdge(W(g.L + o, yE, -o), rB, 0, 3));
                info.Hips.Add(new RoofEdge(W(-o, yE, g.W + o), rA, 1, 2));
                info.Hips.Add(new RoofEdge(W(g.L + o, yE, g.W + o), rB, 1, 3));
            }
            return info;
        }

        // ------------------------------------------------------------------ trusses

        /// <summary>Engineered Fink trusses at 24" O.C. along the Length, bearing on the double top plates; gable-end trusses
        /// (vertical studs 16" O.C.) flush with the end walls; then lateral bracing, 2x6 sub-fascia on the tails, ladder rakes
        /// (lookouts + fly rafter) and 1x6 fascia. RoofInfo: <c>job.GetComponent&lt;RoofInfo&gt;()</c>.</summary>
        public static FramingJob Trusses(Footprint fp, int pitchRise, float eaveOverhangIn = 12, float rakeOverhangIn = 12, bool boomTruck = true)
        {
            int rise = SnapPitch(pitchRise);
            var g = new Geo(fp, rise, 3.5f, true, eaveOverhangIn, rakeOverhangIn);
            var job = FramingJob.Create("Roof trusses (" + rise + "/12 Fink, 24\" O.C.)");
            var root = MakeRoot(job, fp);
            float t = I(1.5f);

            var xs = new List<float> { t / 2 };
            for (float x = I(24f); x < g.L - I(12f); x += I(24f)) xs.Add(x);
            xs.Add(g.L - t / 2);
            var info = MakeInfo(job, g, false, true, xs, null, eaveOverhangIn, rakeOverhangIn);

            BoomTruck truck = null;
            if (boomTruck)
            {
                Vector3 truckLocal = new Vector3(g.L * 0.5f, 0f, g.zE - 5.2f);
                job.Add("Boom truck: park along the eave, set outriggers + pads", ToolSets.Any, () =>
                {
                    truck = BoomTruck.Spawn(root.TransformPoint(truckLocal), root.rotation * Quaternion.Euler(0f, 90f, 0f));
                }, Ghost(root, truckLocal + Vector3.up * 1.5f, new Vector3(9f, 3f, 2.6f), Quaternion.Euler(0, 90, 0)));
            }

            job.Add("Mark truss layout on the top plates: 24\" O.C. from the left end", ToolSets.Layout, null,
                Ghost(root, new Vector3(g.L / 2, g.P, g.WD / 2), new Vector3(g.L, I(0.5f), g.WD), Quaternion.identity));

            float yMinT = Mathf.Min(g.P, g.P + I(3.5f) - g.r * g.tailH);
            float yMaxT = g.P + I(3.5f) + g.r * g.s + g.D;
            var tsize = new Vector3(t, yMaxT - yMinT, g.W + 2 * g.tailH);
            for (int i = 0; i < xs.Count; i++)
            {
                int idx = i; float x = xs[i];
                bool gable = i == 0 || i == xs.Count - 1;
                var c = new Vector3(x, (yMinT + yMaxT) / 2, g.s);
                string label = gable ? "Set gable-end truss (" + (i == 0 ? "left" : "right") + " end), plumb + brace, toe-nail + hurricane ties"
                                     : "Set truss #" + i + " @ " + Units.Format(x) + ", toe-nail + 2 hurricane ties";
                job.Add(label, ToolSets.Fasten, () =>
                {
                    var m = BuildTruss(root, g, x, gable, idx);
                    if (truck != null && AnimateBoom) truck.Lift(m.transform.Find("Lift"));
                }, Ghost(root, c, tsize, Quaternion.identity)).Use(gable ? "truss-gable" : "truss-fink").Use("hurricane-tie", 2).Nails = 6;
            }

            // permanent lateral restraint along the bottom chords (center of span)
            var braceY = g.P + I(3.5f) + I(0.75f);
            AddRun(job, root, "2x4", "Continuous lateral brace on bottom chords", MemberRole.Blocking, new Vector3(0, braceY, g.s), Vector3.right, g.L, Vector3.up, I(3.5f), I(1.5f), xs);

            AddEaves(job, root, g, xs, false);
            AddRakes(job, root, g, true);
            AddFascia(job, root, g, false);

            if (boomTruck)
                job.Add("Release the boom truck", ToolSets.Any, () => { if (truck != null) truck.Leave(); },
                    Ghost(root, new Vector3(g.L * 0.5f, 1.5f, g.zE - 5.2f), new Vector3(9f, 3f, 2.6f), Quaternion.Euler(0, 90, 0)));

            job.Completed += () => info.Framed = true;
            return job;
        }

        /// <summary>One engineered truss as a single custom mesh: 2x4 top chords with plumb-cut tails, bottom chord, W webs
        /// (or vertical studs 16" O.C. for a gable-end truss), galvanized plates on both faces at every joint, hurricane ties.</summary>
        static StructuralMember BuildTruss(Transform root, Geo g, float x, bool gable, int idx)
        {
            float a = g.P + I(3.5f);            // bottom chord top = top chord lower edge at the wall line
            float r = g.r, W = g.W, s = g.s, D = g.D, zt = -g.tailH, ze = W + g.tailH, P = g.P;
            float yMin = Mathf.Min(P, a - r * g.tailH), yMax = a + r * s + D;
            var c = new Vector3(x, (yMin + yMax) / 2, s);
            var size = new Vector3(I(1.5f), yMax - yMin, ze - zt);
            float hx = I(0.75f);
            var m = Member(root, "2x4x16", MemberRole.Truss, Connector.HurricaneTie | Connector.Nailed, c, Quaternion.identity, size, k =>
            {
                Vector3 O = new Vector3(x, 0f, 0f);
                Vector3 Z = Vector3.forward, Y = Vector3.up, X = Vector3.right;
                Vector3 gl = new Vector3(0, r, 1), gr = new Vector3(0, -r, 1);
                // top chords
                k.Prism(new List<Vector2> { new Vector2(zt, a + r * zt), new Vector2(s, a + r * s), new Vector2(s, a + r * s + D), new Vector2(zt, a + r * zt + D) }, O, Z, Y, X, -hx, hx, SurfaceKind.PineLumber, gl);
                k.Prism(new List<Vector2> { new Vector2(s, a + r * s), new Vector2(ze, a - r * g.tailH), new Vector2(ze, a - r * g.tailH + D), new Vector2(s, a + r * s + D) }, O, Z, Y, X, -hx, hx, SurfaceKind.PineLumber, gr);
                // bottom chord
                k.Prism(Geo2.Rect(0f, P, W, a), O, Z, Y, X, -hx, hx, SurfaceKind.PineLumber, Z);
                var under = new List<HalfPlane> { HalfPlane.YMin(a), HalfPlane.Below(a, r), HalfPlane.Below(a + r * W, -r) };
                var outline = new List<HalfPlane> { HalfPlane.YMin(P), HalfPlane.Below(a + D, r), HalfPlane.Below(a + D + r * W, -r), HalfPlane.XMin(zt), HalfPlane.XMax(ze) };
                var plates = new List<List<Vector2>>
                {
                    Geo2.Rect(I(0.25f), P + I(0.25f), I(8f), a + I(3.5f)),
                    Geo2.Rect(W - I(8f), P + I(0.25f), W - I(0.25f), a + I(3.5f)),
                    Geo2.Rect(s - I(3.5f), a + r * s - I(2.5f), s + I(3.5f), yMax + 1f),
                };
                if (!gable)
                {
                    // Fink W: bottom panel points at the third points, top panel points at mid-chord
                    var B1 = new Vector2(W / 3f, a); var B2 = new Vector2(2f * W / 3f, a);
                    var T1 = new Vector2(W / 4f, a + r * W / 4f); var T2 = new Vector2(3f * W / 4f, a + r * W / 4f);
                    var Pk = new Vector2(s, a + r * s);
                    Web(k, O, B1, T1, under, Sep(B1, T1, Pk, true));
                    Web(k, O, B1, Pk, under, Sep(B1, T1, Pk, false), HalfPlane.XMax(s));
                    Web(k, O, B2, Pk, under, Sep(B2, T2, Pk, false), HalfPlane.XMin(s));
                    Web(k, O, B2, T2, under, Sep(B2, T2, Pk, true));
                    plates.Add(Geo2.Rect(B1.x - I(2.5f), P + I(0.25f), B1.x + I(2.5f), a + I(2.75f)));
                    plates.Add(Geo2.Rect(B2.x - I(2.5f), P + I(0.25f), B2.x + I(2.5f), a + I(2.75f)));
                    plates.Add(Geo2.Rect(T1.x - I(2f), T1.y - I(2.75f), T1.x + I(2f), T1.y + I(3f)));
                    plates.Add(Geo2.Rect(T2.x - I(2f), T2.y - I(2.75f), T2.x + I(2f), T2.y + I(3f)));
                }
                else
                {
                    // gable-end truss: vertical 2x4 studs at 16" O.C. (flat in the truss plane) for the gable sheathing
                    for (float z = I(16f); z < W - I(4f); z += I(16f))
                    {
                        var st = Geo2.Clip(Geo2.Rect(z - I(1.75f), a, z + I(1.75f), a + 10f), under);
                        if (st.Count < 3) continue;
                        float top = float.MinValue; foreach (var p in st) top = Mathf.Max(top, p.y);
                        if (top - a < I(2f)) continue;
                        k.Prism(st, O, Z, Y, X, -hx, hx, SurfaceKind.PineLumber, Y);
                        plates.Add(Geo2.Rect(z - I(2f), P + I(0.25f), z + I(2f), a + I(2.25f)));
                        float yu = a + r * Mathf.Min(z, W - z);
                        plates.Add(Geo2.Rect(z - I(2f), yu - I(2.5f), z + I(2f), yu + I(2.5f)));
                    }
                }
                foreach (var pl in plates)
                {
                    var cp = Geo2.Clip(pl, outline);
                    if (cp.Count < 3) continue;
                    k.Prism(cp, O, Z, Y, X, hx, hx + I(0.04f), SurfaceKind.Galvanized, Z);
                    k.Prism(cp, O, Z, Y, X, -hx - I(0.04f), -hx, SurfaceKind.Galvanized, Z);
                }
            }, gable ? "Gable-end truss" : "Truss #" + idx);

            HurricaneTie(m.transform, root, new Vector3(x, g.P, I(1.75f)), Vector3.right, Vector3.back);
            HurricaneTie(m.transform, root, new Vector3(x, g.P, g.W - I(1.75f)), Vector3.right, Vector3.forward);
            // group visual + hardware under "Lift" so the boom truck can swing it in without moving the collider
            var lift = new GameObject("Lift").transform;
            lift.SetParent(m.transform, false);
            var kids = new List<Transform>();
            foreach (Transform ch in m.transform) if (ch != lift) kids.Add(ch);
            foreach (var ch in kids) ch.SetParent(lift, false);
            return m;
        }

        /// <summary>Separating half-plane at a shared bottom panel point: web toward 'first' (true) or toward 'second'.</summary>
        static HalfPlane Sep(Vector2 node, Vector2 first, Vector2 second, bool keepFirst)
        {
            Vector2 dA = (first - node).normalized, dB = (second - node).normalized;
            Vector2 bis = (dA + dB).normalized;
            Vector2 nb = new Vector2(bis.y, -bis.x);
            if (Vector2.Dot(nb, dA) < 0f) nb = -nb;
            return new HalfPlane(node, keepFirst ? nb : -nb);
        }

        static void Web(FrameKit k, Vector3 O, Vector2 from, Vector2 to, List<HalfPlane> under, params HalfPlane[] extra)
        {
            Vector2 dir = (to - from).normalized, perp = new Vector2(-dir.y, dir.x) * I(1.75f);
            Vector2 a = from - dir * 1f, b = to + dir * 1f;
            var poly = new List<Vector2> { a - perp, b - perp, b + perp, a + perp };
            poly = Geo2.Clip(poly, under);
            foreach (var e in extra) poly = Geo2.Clip(poly, e);
            if (poly.Count < 3) return;
            k.Prism(poly, O, Vector3.forward, Vector3.up, Vector3.right, -I(0.75f), I(0.75f), SurfaceKind.PineLumber, new Vector3(0, dir.y, dir.x));
        }

        // ------------------------------------------------------------------ eaves, rakes, fascia

        /// <summary>Lays a straight run (brace, sub-fascia, ridge) in pieces no longer than the longest stock, splicing on framing.</summary>
        static void AddRun(FramingJob job, Transform root, string size, string label, MemberRole role, Vector3 start, Vector3 dir, float len,
                           Vector3 thin, float width, float thick, List<float> spliceAt, SurfaceKind face = SurfaceKind.PineLumber, string defOverride = null)
        {
            float maxLen = 16 * Units.Foot - I(0.5f);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / maxLen - 0.0001f));
            var cuts = new List<float> { 0f };
            for (int k = 1; k < n; k++)
            {
                float want = len * k / n, best = want;
                if (spliceAt != null)
                {
                    float bd = float.MaxValue;
                    foreach (var f in spliceAt)
                    {
                        float along = Vector3.Dot(new Vector3(f, 0, f), new Vector3(Mathf.Abs(dir.x), 0, Mathf.Abs(dir.z))) - Vector3.Dot(start, new Vector3(Mathf.Abs(dir.x), 0, Mathf.Abs(dir.z)));
                        if (along <= cuts[cuts.Count - 1] + I(12f) || along - cuts[cuts.Count - 1] > maxLen) continue;
                        if (Mathf.Abs(along - want) < bd) { bd = Mathf.Abs(along - want); best = along; }
                    }
                }
                cuts.Add(best);
            }
            cuts.Add(len);
            for (int k = 0; k + 1 < cuts.Count; k++)
            {
                Vector3 a = start + dir * cuts[k], b = start + dir * cuts[k + 1];
                float seg = cuts[k + 1] - cuts[k];
                string id = defOverride ?? RoofStock.Pick(size, seg).id;
                string lab = label + (cuts.Count > 2 ? " (piece " + (k + 1) + "/" + (cuts.Count - 1) + ")" : "");
                Vector3 ca = a, cb = b;
                var rot = Quaternion.LookRotation(dir, thin);
                job.Add(lab, ToolSets.Fasten, () => Board(root, id, role, Connector.Nailed, ca, cb, thin, width, thick, lab, face),
                    Ghost(root, (a + b) * 0.5f, new Vector3(width, thick, seg), rot)).Use(id).Nails = Mathf.Max(4, Mathf.RoundToInt(Units.ToInches(seg) / 24f) * 2);
            }
        }

        static void AddEaves(FramingJob job, Transform root, Geo g, List<float> framing, bool hip)
        {
            float topY = g.Top(g.zE);
            float y = topY - I(2.75f);
            float zf = -g.tailH - I(0.75f), zb = g.W + g.tailH + I(0.75f);
            if (!hip)
            {
                float x0 = -g.rakeIn, x1 = g.L + g.rakeIn;
                AddRun(job, root, "2x6", "Sub-fascia 2x6 on the tails (front eave)", MemberRole.SubFascia, new Vector3(x0, y, zf), Vector3.right, x1 - x0, Vector3.forward, I(5.5f), I(1.5f), framing);
                AddRun(job, root, "2x6", "Sub-fascia 2x6 on the tails (back eave)", MemberRole.SubFascia, new Vector3(x0, y, zb), Vector3.right, x1 - x0, Vector3.forward, I(5.5f), I(1.5f), framing);
            }
            else
            {
                float o = -g.zE;
                AddRun(job, root, "2x6", "Sub-fascia 2x6 (front eave)", MemberRole.SubFascia, new Vector3(-o, y, zf), Vector3.right, g.L + 2 * o, Vector3.forward, I(5.5f), I(1.5f), framing);
                AddRun(job, root, "2x6", "Sub-fascia 2x6 (back eave)", MemberRole.SubFascia, new Vector3(-o, y, zb), Vector3.right, g.L + 2 * o, Vector3.forward, I(5.5f), I(1.5f), framing);
                float xl = -g.tailH - I(0.75f), xr = g.L + g.tailH + I(0.75f);
                AddRun(job, root, "2x6", "Sub-fascia 2x6 (left hip end)", MemberRole.SubFascia, new Vector3(xl, y, -o + I(1.5f)), Vector3.forward, g.W + 2 * o - I(3f), Vector3.right, I(5.5f), I(1.5f), null);
                AddRun(job, root, "2x6", "Sub-fascia 2x6 (right hip end)", MemberRole.SubFascia, new Vector3(xr, y, -o + I(1.5f)), Vector3.forward, g.W + 2 * o - I(3f), Vector3.right, I(5.5f), I(1.5f), null);
            }
        }

        /// <summary>Ladder rakes at each gable: 2x4 lookouts 24" O.C. nailed to the end truss/rafter, 2x4 fly rafter on their ends.</summary>
        static void AddRakes(FramingJob job, Transform root, Geo g, bool trussed)
        {
            for (int end = 0; end < 2; end++)
                for (int side = 0; side < 2; side++)
                {
                    int e = end, sd = side;
                    float xo = e == 0 ? -g.rakeIn : g.L + g.rakeIn;          // fly rafter outer face
                    float xin = e == 0 ? xo + I(1.5f) : xo - I(1.5f);       // fly rafter inner face
                    float xFace = e == 0 ? 0f : g.L;                        // end truss / rafter outer face
                    string label = "Rake ladder: lookouts + fly rafter (" + (e == 0 ? "left" : "right") + " gable, " + (sd == 0 ? "front" : "back") + ")";
                    float qT = -g.tailH, qE = g.s;
                    Vector3 O = sd == 0 ? new Vector3(0, 0, 0) : new Vector3(0, 0, g.W);
                    Vector3 h = sd == 0 ? Vector3.forward : Vector3.back;
                    float flyX = (xo + xin) * 0.5f;
                    var gc = new Vector3(flyX, g.Top(g.s * 0.5f), sd == 0 ? g.s * 0.5f : g.W - g.s * 0.5f);
                    int nLook = 0; for (float q = I(12f); q < g.s - I(6f); q += I(24f)) nLook++;
                    var lk = RoofStock.Pick("2x4", g.rakeIn);
                    var fly = RoofStock.Pick("2x4", (qE - qT) / g.cos);
                    job.Add(label, ToolSets.Fasten, () =>
                    {
                        for (float q = I(12f); q < g.s - I(6f); q += I(24f))
                        {
                            float z = sd == 0 ? q : g.W - q;
                            float yTop = g.Top(q - I(0.75f));
                            float xa = Mathf.Min(xFace, xin), xb = Mathf.Max(xFace, xin);
                            Board(root, lk.id, MemberRole.Lookout, Connector.Nailed, new Vector3(xa, yTop - I(1.75f), z), new Vector3(xb, yTop - I(1.75f), z), Vector3.forward, I(3.5f), I(1.5f), "Lookout");
                        }
                        Rafter(root, "2x4", MemberRole.SubFascia, Connector.Nailed, new Vector3(flyX, 0, O.z), h, g.Yw, g.r, I(3.5f) / g.cos, qT, qE, false, 0f, "Fly rafter (barge)");
                    }, Ghost(root, gc, new Vector3(I(1.5f), I(3.5f), g.s / g.cos), Quaternion.LookRotation(new Vector3(0, g.r, sd == 0 ? 1 : -1)))).Use(lk.id, nLook).Use(fly.id).Nails = nLook * 4 + 6;
                }
        }

        static void AddFascia(FramingJob job, Transform root, Geo g, bool hip)
        {
            float top = g.Top(g.zE) + I(0.25f);
            float y = top - I(2.75f);
            float zf = g.zE - I(0.375f), zb = g.W - g.zE + I(0.375f);
            var white = SurfaceKind.PaintedWhite;
            if (!hip)
            {
                float x0 = -g.rakeIn - I(0.75f), x1 = g.L + g.rakeIn + I(0.75f);
                AddRun(job, root, "1x6", "Primed 1x6 fascia (front eave)", MemberRole.Fascia, new Vector3(x0, y, zf), Vector3.right, x1 - x0, Vector3.forward, I(5.5f), I(0.75f), null, white);
                AddRun(job, root, "1x6", "Primed 1x6 fascia (back eave)", MemberRole.Fascia, new Vector3(x0, y, zb), Vector3.right, x1 - x0, Vector3.forward, I(5.5f), I(0.75f), null, white);
                // rake boards on the fly rafters: tops in the deck plane, plumb cut at the peak
                for (int e = 0; e < 2; e++)
                    for (int sd = 0; sd < 2; sd++)
                    {
                        float x = e == 0 ? -g.rakeIn - I(0.375f) : g.L + g.rakeIn + I(0.375f);
                        Vector3 O = new Vector3(x, 0, sd == 0 ? 0f : g.W);
                        Vector3 h = sd == 0 ? Vector3.forward : Vector3.back;
                        float len = (g.s - g.zE) / g.cos;
                        var stock = RoofStock.Pick("1x6", len);
                        string lab = "Primed 1x6 rake fascia (" + (e == 0 ? "left" : "right") + ", " + (sd == 0 ? "front" : "back") + ")";
                        var gc = new Vector3(x, g.Top(g.s * 0.5f), sd == 0 ? g.s * 0.5f : g.W - g.s * 0.5f);
                        job.Add(lab, ToolSets.Fasten, () =>
                        {
                            var m = RakeBoard(root, stock.id, O, h, g, lab);
                        }, Ghost(root, gc, new Vector3(I(0.75f), I(5.5f), len), Quaternion.LookRotation(new Vector3(0, g.r, sd == 0 ? 1 : -1)))).Use(stock.id, stock.count).Nails = 8;
                    }
            }
            else
            {
                float o = -g.zE;
                AddRun(job, root, "1x6", "Primed 1x6 fascia (front)", MemberRole.Fascia, new Vector3(-o - I(0.75f), y, zf), Vector3.right, g.L + 2 * o + I(1.5f), Vector3.forward, I(5.5f), I(0.75f), null, white);
                AddRun(job, root, "1x6", "Primed 1x6 fascia (back)", MemberRole.Fascia, new Vector3(-o - I(0.75f), y, zb), Vector3.right, g.L + 2 * o + I(1.5f), Vector3.forward, I(5.5f), I(0.75f), null, white);
                AddRun(job, root, "1x6", "Primed 1x6 fascia (left end)", MemberRole.Fascia, new Vector3(-o - I(0.375f), y, -o), Vector3.forward, g.W + 2 * o, Vector3.right, I(5.5f), I(0.75f), null, white);
                AddRun(job, root, "1x6", "Primed 1x6 fascia (right end)", MemberRole.Fascia, new Vector3(g.L + o + I(0.375f), y, -o), Vector3.forward, g.W + 2 * o, Vector3.right, I(5.5f), I(0.75f), null, white);
            }
        }

        static StructuralMember RakeBoard(Transform root, string id, Vector3 O, Vector3 h, Geo g, string name)
        {
            float qT = g.zE - I(0.75f), qE = g.s;
            float top0 = g.Yw + I(0.25f);
            float Dv = I(5.5f) / g.cos;
            Vector3 side = Vector3.Cross(Vector3.up, h).normalized;
            float slopeLen = (qE - qT) / g.cos;
            float qm = (qT + qE) * 0.5f;
            Vector3 c = new Vector3(O.x, 0, O.z) + h * qm + Vector3.up * (top0 + g.r * qm - Dv * 0.5f);
            var box = OrientedBox(c, h + Vector3.up * g.r, side, I(0.75f), I(5.5f), slopeLen);
            var poly = new List<Vector2> { new Vector2(qT, top0 + g.r * qT - Dv), new Vector2(qE, top0 + g.r * qE - Dv), new Vector2(qE, top0 + g.r * qE), new Vector2(qT, top0 + g.r * qT) };
            Vector3 Op = new Vector3(O.x, 0, O.z);
            return Member(root, id, MemberRole.Fascia, Connector.Nailed, box.c, box.rot, box.size, k =>
                k.Prism(poly, Op, h, Vector3.up, side, -I(0.375f), I(0.375f), SurfaceKind.PaintedWhite, h + Vector3.up * g.r), name);
        }

        // ------------------------------------------------------------------ stick framing

        /// <summary>Stick-framed roof: ceiling joists, ridge board, common rafters 16" O.C. with birdsmouth seats, collar ties in the
        /// upper third, then (hip) hip rafters + jacks or (gable) gable studs and ladder rakes, sub-fascia and fascia.
        /// Hip rafter run = common run x sqrt(2): unit rise per 16.97" (~17") of run.</summary>
        public static FramingJob StickFramed(Footprint fp, int pitchRise, bool hip, float eaveOverhangIn = 12, float rakeOverhangIn = 12)
        {
            int rise = SnapPitch(pitchRise);
            float W = fp.Width, L = fp.Length;
            if (hip && L < W) Debug.LogWarning("[RoofFraming] hip roof expects Length >= Width");
            float run = W * 0.5f;
            string rs = run <= 8 * Units.Foot ? "2x6" : run <= 12 * Units.Foot ? "2x8" : "2x10";
            var g = new Geo(fp, rise, Units.ToInches(RoofStock.Depth(rs)), false, eaveOverhangIn, rakeOverhangIn);
            string ridgeSize = RoofStock.Deeper(rs);
            while (RoofStock.Depth(ridgeSize) < g.D && ridgeSize != "2x12") ridgeSize = RoofStock.Deeper(ridgeSize);
            string hipSize = RoofStock.Deeper(rs);
            string cjSize = W <= 14 * Units.Foot ? "2x6" : "2x8";
            var job = FramingJob.Create("Stick-framed " + (hip ? "hip" : "gable") + " roof (" + rise + "/12, " + rs + " rafters 16\" O.C.)");
            var root = MakeRoot(job, fp);
            float s = g.s, oc = I(16f), t = I(1.5f);
            var conn = Connector.HurricaneTie | Connector.Nailed;

            // rafter layout
            var commons = new List<float>();
            var jacksL = new List<float>(); var jacksR = new List<float>(); var endJacks = new List<float>();
            if (!hip)
            {
                commons.Add(t / 2);
                for (float x = oc; x < L - I(8f); x += oc) commons.Add(x);
                commons.Add(L - t / 2);
            }
            else
            {
                for (float x = s; x < L - s - I(4f); x += oc) commons.Add(x);
                commons.Add(L - s);
                for (float x = oc; x < s - I(4f); x += oc) { jacksL.Add(x); jacksR.Add(L - x); }
                for (float z = oc; z < s - I(4f); z += oc) { endJacks.Add(z); endJacks.Add(W - z); }
            }
            var framingX = new List<float>(commons); framingX.AddRange(jacksL); framingX.AddRange(jacksR);
            var framingZ = new List<float>(endJacks); if (hip) framingZ.Add(s);
            var info = MakeInfo(job, g, hip, false, framingX, framingZ, eaveOverhangIn, rakeOverhangIn);

            job.Add("Lay out rafters + ceiling joists on the plates (16\" O.C.), mark the ridge", ToolSets.Layout, null,
                Ghost(root, new Vector3(L / 2, g.P, g.WD / 2), new Vector3(L, I(0.5f), g.WD), Quaternion.identity));

            // ceiling joists beside each rafter, heel-cut to the roof line
            float dj = RoofStock.Depth(cjSize);
            var cjX = new List<float>();
            foreach (var x in framingX) if (x > I(4f) && x < L - I(4f)) cjX.Add(x - I(1.5f));
            cjX.Sort();
            foreach (var xx in cjX)
            {
                float x = xx;
                var poly = Geo2.Clip(Geo2.Rect(0f, g.P, W, g.P + dj), new List<HalfPlane> { HalfPlane.Below(g.Yw - I(0.5f), g.r), HalfPlane.Below(g.Yw - I(0.5f) + g.r * W, -g.r) });
                var stock = RoofStock.Pick(cjSize, W);
                job.Add("Ceiling joist @ " + Units.Format(x) + " (heel cut to the pitch)", ToolSets.Fasten,
                    () => PlanarYZ(root, stock.id, MemberRole.CeilingJoist, Connector.Nailed, poly, x - t / 2, x + t / 2, new Vector2(1, 0), "Ceiling joist"),
                    Ghost(root, new Vector3(x, g.P + dj / 2, W / 2), new Vector3(t, dj, W), Quaternion.identity)).Use(stock.id, stock.count).Nails = 6;
            }

            // ridge board
            float dr = RoofStock.Depth(ridgeSize);
            float ridgeTop = g.Top(s - t / 2);
            float rx0 = hip ? s - t / 2 : -g.rakeIn + t, rx1 = hip ? L - s + t / 2 : L + g.rakeIn - t;
            if (rx1 - rx0 > I(2f))
                AddRun(job, root, ridgeSize, "Set " + ridgeSize + " ridge board (temporary posts, crown up)", MemberRole.RidgeBoard, new Vector3(rx0, ridgeTop - dr / 2, s), Vector3.right, rx1 - rx0, Vector3.forward, dr, t, commons);

            // common rafter pairs
            float qT = -g.tailH, qR = s - t / 2;
            foreach (var xx in commons)
            {
                float x = xx;
                var stock = RoofStock.Pick(rs, (qR - qT) / g.cos);
                job.Add("Rafter pair @ " + Units.Format(x) + ": birdsmouth on the plate, nail to ridge + hurricane ties", ToolSets.Fasten, () =>
                {
                    var a = Rafter(root, rs, MemberRole.Rafter, conn, new Vector3(x, 0, 0), Vector3.forward, g.Yw, g.r, g.D, qT, qR, true, g.P, "Common rafter");
                    var b = Rafter(root, rs, MemberRole.Rafter, conn, new Vector3(x, 0, W), Vector3.back, g.Yw, g.r, g.D, qT, qR, true, g.P, "Common rafter");
                    float sx = x <= t ? 1f : -1f;
                    HurricaneTie(a.transform, root, new Vector3(x, g.P, g.WD / 2), Vector3.right * sx, Vector3.back);
                    HurricaneTie(b.transform, root, new Vector3(x, g.P, W - g.WD / 2), Vector3.right * sx, Vector3.forward);
                }, Ghost(root, new Vector3(x, g.Top(s * 0.5f), s), new Vector3(t, g.D, W), Quaternion.identity)).Use(stock.id, 2 * stock.count).Use("hurricane-tie", 2).Nails = 12;
            }

            if (hip)
            {
                // king commons at the middle of each hip end
                for (int e = 0; e < 2; e++)
                {
                    int ee = e;
                    Vector3 O = new Vector3(e == 0 ? 0f : L, 0, s);
                    Vector3 h = e == 0 ? Vector3.right : Vector3.left;
                    var stock = RoofStock.Pick(rs, (qR - qT) / g.cos);
                    job.Add("King common rafter (" + (e == 0 ? "left" : "right") + " hip end)", ToolSets.Fasten, () =>
                    {
                        var m = Rafter(root, rs, MemberRole.Rafter, conn, O, h, g.Yw, g.r, g.D, qT, qR, true, g.P, "King common rafter");
                        HurricaneTie(m.transform, root, O + h * (g.WD / 2) + Vector3.up * g.P, Vector3.forward, -h);
                    }, Ghost(root, O + h * (s * 0.5f) + Vector3.up * g.Top(s * 0.5f), new Vector3(t, g.D, s), Quaternion.LookRotation(h))).Use(stock.id, stock.count).Use("hurricane-tie").Nails = 8;
                }
                // hip rafters: run = common run * sqrt2, one size deeper, dropped so the edges stay below the planes
                float sq = Mathf.Sqrt(2f);
                float mh = g.r / sq;
                float dh = RoofStock.Depth(hipSize);
                float Dh = dh * Mathf.Sqrt(1f + mh * mh);
                float drop = g.r * t / 2;
                float hipTop0 = g.Yw - drop;
                var corners = new[] { (new Vector3(0, 0, 0), new Vector3(1, 0, 1)), (new Vector3(L, 0, 0), new Vector3(-1, 0, 1)), (new Vector3(0, 0, W), new Vector3(1, 0, -1)), (new Vector3(L, 0, W), new Vector3(-1, 0, -1)) };
                string[] cn = { "front-left", "front-right", "back-left", "back-right" };
                for (int ci = 0; ci < 4; ci++)
                {
                    var (O, hd) = corners[ci];
                    Vector3 h = hd.normalized;
                    float qTh = -g.tailH * sq, qEh = (s - t) * sq;
                    var stock = RoofStock.Pick(hipSize, (qEh - qTh) * Mathf.Sqrt(1 + mh * mh));
                    job.Add("Hip rafter (" + cn[ci] + "): " + rise + "\" rise per 16.97\" run, seat on the plate corner", ToolSets.Fasten,
                        () => Rafter(root, hipSize, MemberRole.HipRafter, Connector.Nailed | Connector.HurricaneTie, O, h, hipTop0, mh, Dh, qTh, qEh, true, g.P, "Hip rafter"),
                        Ghost(root, O + h * (qEh * 0.5f) + Vector3.up * (hipTop0 + mh * qEh * 0.5f), new Vector3(t, dh, qEh / Mathf.Cos(Mathf.Atan(mh))), Quaternion.LookRotation(h + Vector3.up * mh))).Use(stock.id, stock.count).Nails = 10;
                }
                // jack rafters: from the plate to the side of the hip
                float cut = I(1.8f);
                void Jack(Vector3 O, Vector3 h, float qEnd, string where)
                {
                    if (qEnd < I(4f)) return;
                    var stock = RoofStock.Pick(rs, (qEnd - qT) / g.cos);
                    job.Add("Jack rafter (" + where + "), " + Units.Format(qEnd) + " run: birdsmouth + cheek cut to the hip", ToolSets.Fasten,
                        () => Rafter(root, rs, MemberRole.JackRafter, Connector.Nailed, O, h, g.Yw, g.r, g.D, qT, qEnd, true, g.P, "Jack rafter"),
                        Ghost(root, O + h * (qEnd * 0.5f) + Vector3.up * g.Top(qEnd * 0.5f), new Vector3(t, g.D, (qEnd - qT) / g.cos), Quaternion.LookRotation(h + Vector3.up * g.r))).Use(stock.id, stock.count).Nails = 6;
                }
                foreach (var x in jacksL) { Jack(new Vector3(x, 0, 0), Vector3.forward, x - cut, "front left"); Jack(new Vector3(x, 0, W), Vector3.back, x - cut, "back left"); }
                foreach (var x in jacksR) { Jack(new Vector3(x, 0, 0), Vector3.forward, L - x - cut, "front right"); Jack(new Vector3(x, 0, W), Vector3.back, L - x - cut, "back right"); }
                foreach (var z in endJacks)
                {
                    float q = Mathf.Min(z, W - z) - cut;
                    Jack(new Vector3(0, 0, z), Vector3.right, q, "left end");
                    Jack(new Vector3(L, 0, z), Vector3.left, q, "right end");
                }
            }

            // collar ties in the upper third (every other common pair)
            float yc = g.P + (g.Top(s) - g.P) * 0.62f;
            for (int i = 1; i < commons.Count - 1; i += 2)
            {
                float x = commons[i];
                var poly = Geo2.Clip(Geo2.Rect(0f, yc, W, yc + I(3.5f)), new List<HalfPlane> { HalfPlane.Below(g.Yw - I(0.25f), g.r), HalfPlane.Below(g.Yw - I(0.25f) + g.r * W, -g.r) });
                if (poly.Count < 3) continue;
                float zl = W; foreach (var p in poly) zl = Mathf.Min(zl, p.x);
                var stock = RoofStock.Pick("2x4", W - 2 * zl);
                job.Add("Collar tie @ " + Units.Format(x) + " (upper third)", ToolSets.Fasten,
                    () => PlanarYZ(root, stock.id, MemberRole.CollarTie, Connector.Nailed, poly, x + t / 2, x + t * 1.5f, new Vector2(1, 0), "Collar tie"),
                    Ghost(root, new Vector3(x + t, yc + I(1.75f), s), new Vector3(t, I(3.5f), W - 2 * zl), Quaternion.identity)).Use(stock.id).Nails = 6;
            }

            if (!hip)
            {
                // gable studs under the end rafters, flush with the wall face, top cut to the rafter
                for (int e = 0; e < 2; e++)
                {
                    float x0 = e == 0 ? 0f : L - g.WD, x1 = e == 0 ? g.WD : L;
                    var studs = new List<List<Vector2>>();
                    for (float z = oc; z < W - I(4f); z += oc)
                    {
                        var p = Geo2.Clip(Geo2.Rect(z - t / 2, g.P, z + t / 2, g.P + 10f), new List<HalfPlane> { HalfPlane.Below(g.Yw - g.D, g.r), HalfPlane.Below(g.Yw - g.D + g.r * W, -g.r) });
                        if (p.Count >= 3) studs.Add(p);
                    }
                    float xa = x0, xb = x1;
                    job.Add("Gable-end studs @ 16\" O.C. (" + (e == 0 ? "left" : "right") + " end), angle-cut to the rafter", ToolSets.Fasten, () =>
                    {
                        foreach (var p in studs) PlanarYZ(root, "2x4x8", MemberRole.GableStud, Connector.Nailed, p, xa, xb, new Vector2(0, 1), "Gable stud");
                    }, Ghost(root, new Vector3((x0 + x1) / 2, g.P + (g.Top(s) - g.P) / 2, s), new Vector3(g.WD, g.Top(s) - g.P, W), Quaternion.identity)).Use("2x4x8", Mathf.CeilToInt(studs.Count * 0.6f)).Nails = studs.Count * 4;
                }
            }

            AddEaves(job, root, g, framingX, hip);
            if (!hip) AddRakes(job, root, g, false);
            AddFascia(job, root, g, hip);
            job.Completed += () => info.Framed = true;
            return job;
        }

        // ------------------------------------------------------------------ roof deck

        /// <summary>7/16" OSB roof deck: 4x8 panels long side across the rafters, rows from the eave up, end joints staggered 4' and
        /// landing on framing, 1/8" gaps, H-clips between rafters on the long edges, cut to the rakes / hips, ridge-vent slot left
        /// open (1" each side, stopping 6" from the end walls). Ends with a roof anchor at the ridge.</summary>
        public static FramingJob RoofSheathing(RoofInfo roof)
        {
            var job = FramingJob.Create("Roof sheathing (7/16\" OSB)");
            job.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var root = job.transform;
            roof.SheathingJob = job;
            float rowH = I(48f), gap = I(0.125f), sheetL = I(96f), tk = I(0.4375f);
            float offcut = 0f;
            for (int pi = 0; pi < roof.Planes.Count; pi++)
            {
                var p = roof.Planes[pi];
                float SL = p.SlopeLength;
                var bounds = p.Bounds(0f, 0f, SL);
                int row = 0;
                for (float rowV = 0f; rowV < SL - I(0.5f); rowV += rowH + gap, row++)
                {
                    float v0 = rowV;
                    float v1 = Mathf.Min(SL, v0 + rowH);
                    bool topRow = v1 >= SL - 0.001f;
                    float uLo = Mathf.Min(p.UMin(v0), p.UMin(v1)), uHi = Mathf.Max(p.UMax(v0), p.UMax(v1));
                    if (uHi - uLo < 0.02f) continue;
                    float u = uLo;
                    int k = 0;
                    bool first = true;
                    while (u < uHi - 0.01f)
                    {
                        float len = first && row % 2 == 1 ? sheetL / 2 : sheetL;
                        first = false;
                        float target = u + len, u1;
                        float next;
                        if (target >= uHi - 0.01f) { u1 = uHi; next = uHi; }
                        else
                        {
                            float joint = target;
                            for (int f = p.FramingU.Count - 1; f >= 0; f--)
                                if (p.FramingU[f] <= target + 1e-4f && p.FramingU[f] > u + I(16f)) { joint = p.FramingU[f]; break; }
                            u1 = joint - gap / 2; next = joint + gap / 2;
                        }
                        float ua = u, ub = u1;
                        u = next;
                        var rect = Geo2.Rect(ua, v0, ub, v1);
                        var parts = new List<List<Vector2>>();
                        var whole = Geo2.Clip(rect, bounds);
                        if (whole.Count < 3 || Geo2.Area(whole) < 0.01f) continue;
                        if (topRow && p.RidgeGap > 0f && ub > p.VentU0 && ua < p.VentU1)
                        {
                            var l = Geo2.Clip(whole, HalfPlane.XMax(p.VentU0));
                            var mid = Geo2.Clip(Geo2.Clip(Geo2.Clip(whole, HalfPlane.XMin(p.VentU0)), HalfPlane.XMax(p.VentU1)), HalfPlane.YMax(SL - p.RidgeGap));
                            var rr = Geo2.Clip(whole, HalfPlane.XMin(p.VentU1));
                            if (l.Count >= 3) parts.Add(l);
                            if (mid.Count >= 3) parts.Add(mid);
                            if (rr.Count >= 3) parts.Add(rr);
                        }
                        else parts.Add(whole);
                        float area = 0f; foreach (var q in parts) area += Geo2.Area(q);
                        float frac = area / (rowH * sheetL);
                        bool fromOffcut = frac < 0.5f && offcut >= frac;
                        if (fromOffcut) offcut -= frac; else offcut += Mathf.Max(0f, 1f - frac);
                        // H-clips on the long top edge, one per bay between framing members
                        var clips = new List<float>();
                        if (!topRow)
                            for (int f = 0; f + 1 < p.FramingU.Count; f++)
                            {
                                float mid = (p.FramingU[f] + p.FramingU[f + 1]) * 0.5f;
                                if (mid > ua + I(2f) && mid < ub - I(2f) && p.Contains(mid, v1, -I(2f))) clips.Add(mid);
                            }
                        var pp = p; int prow = row + 1, pk = ++k;
                        var ps = parts; var cl = clips; float pv1 = v1;
                        var step = job.Add("Sheathing (" + p.Name.ToLower() + "): row " + prow + ", panel " + pk + (frac < 0.97f ? " (cut to fit)" : "") + " - 8d nails 6\" edges / 12\" field",
                            ToolSets.Fasten, () =>
                            {
                                PlacePanel(root, pp, ps, tk, cl, pv1, gap);
                                roof.HasDeck = true;
                            }, () =>
                            {
                                return (pp.At((ua + ub) * 0.5f, (v0 + v1) * 0.5f, -tk / 2), new Vector3(v1 - v0, tk, ub - ua), Quaternion.LookRotation(pp.AlongEave, pp.Normal));
                            });
                        if (!fromOffcut) step.Use("osb716roof");
                        step.Use("h-clip", clips.Count).Use("nails8d", Mathf.Max(8, Mathf.RoundToInt(40 * frac)));
                    }
                }
            }
            job.Add("Install a roof anchor at the ridge (fall protection for the roofing crew)", ToolSets.Fasten, () =>
            {
                Vector3 at = roof.HasRidge ? Vector3.Lerp(roof.Ridge.A, roof.Ridge.B, 0.5f) : roof.Planes[0].At(roof.Planes[0].EaveLength / 2, roof.Planes[0].SlopeLength - 0.3f);
                RoofAnchor.Create(roof, at);
            }, () =>
            {
                Vector3 at = roof.HasRidge ? Vector3.Lerp(roof.Ridge.A, roof.Ridge.B, 0.5f) : roof.Planes[0].At(roof.Planes[0].EaveLength / 2, roof.Planes[0].SlopeLength - 0.3f);
                return (at, new Vector3(0.3f, 0.1f, 0.3f), Quaternion.identity);
            }).Nails = 6;
            job.Completed += () => { roof.Sheathed = true; roof.HasDeck = true; };
            return job;
        }

        static StructuralMember PlacePanel(Transform root, RoofPlane p, List<List<Vector2>> parts, float tk, List<float> clips, float vTopEdge, float gap)
        {
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            foreach (var part in parts) foreach (var q in part) { u0 = Mathf.Min(u0, q.x); u1 = Mathf.Max(u1, q.x); v0 = Mathf.Min(v0, q.y); v1 = Mathf.Max(v1, q.y); }
            Vector3 c = p.At((u0 + u1) * 0.5f, (v0 + v1) * 0.5f, -tk * 0.5f);
            Quaternion rot = Quaternion.LookRotation(p.AlongEave, p.Normal);
            var size = new Vector3(v1 - v0, tk, u1 - u0);
            var m = Member(root, "osb716roof", MemberRole.RoofSheathing, Connector.Nailed | Connector.HClip, c, rot, size, k =>
            {
                foreach (var part in parts) k.Prism(part, p.EaveStart, p.AlongEave, p.UpSlope, p.Normal, -tk, 0f, SurfaceKind.Osb, p.AlongEave, SurfaceKind.Osb);
            }, "Roof sheathing panel");
            foreach (var u in clips)
            {
                Vector3 w = p.At(u, vTopEdge + gap * 0.5f, 0f);
                Vector3 lc = m.transform.InverseTransformPoint(w);
                Quaternion lr = Quaternion.Inverse(m.transform.rotation) * p.Frame;
                Framer.Hardware(m.transform, "H-clip", lc, lr, (b, g) =>
                {
                    b.AddBox(new Vector3(0, I(0.012f), 0), new Vector3(I(1f), I(0.024f), I(0.9f)), g);                 // top flange over both edges
                    b.AddBox(new Vector3(0, -tk * 0.5f, 0), new Vector3(I(1f), tk, I(0.05f)), g);                        // web in the 1/8" gap
                    b.AddBox(new Vector3(0, -tk + I(0.012f), 0), new Vector3(I(1f), I(0.024f), I(0.9f)), g);             // bottom flange
                });
            }
            return m;
        }
    }
}
