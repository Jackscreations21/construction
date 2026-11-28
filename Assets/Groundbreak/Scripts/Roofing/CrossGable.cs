using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Intersecting gable (L-shaped house): a garage wing projecting from the front (eave) wall of a gable main roof,
    /// same pitch and plate height, framed as a California (overlay) valley on top of the sheathed main roof:
    /// wing walls on the slab, valley lines snapped on the main deck, 2x10 valley boards laid flat along them, 2x10 wing
    /// ridge, 2x8 common rafters with birdsmouths on the wing plates, valley jack rafters seat-cut onto the valley boards,
    /// fascia, OSB deck. Roofing (<see cref="Roof"/>): 36" ice &amp; water down each valley (main side comes from the main
    /// dry-in via RoofInfo.Valleys), underlayment, main-roof courses run 12" across the valley, wing courses cut 2" back
    /// on a chalk line (closed-cut valley), ridge cap. Build the wing after the main sheathing and before the main dry-in.
    /// Plan coordinates: x along the main eave, z into the building (z = 0 = main front wall line, wing at z &lt; 0).
    /// </summary>
    public class CrossGable : MonoBehaviour
    {
        public RoofInfo Main;
        public Footprint Fp;
        public float X0, X1, Projection;     // wing outside framing (plan), projection out from the main wall line
        public float S, A, E, Oh;            // slope, main deck height at z = 0, sheathing vertical thickness, eave overhang
        public int MainPlane = -1;
        public readonly List<RoofEdge> Valleys = new List<RoofEdge>();
        public FramingJob FramingJob, RoofingJob;
        public bool Framed, Roofed;

        static float I(float v) => v * Units.Inch;
        public float Xc => (X0 + X1) * 0.5f;
        public float HalfW => (X1 - X0) * 0.5f;
        Vector3 AX => Fp.AlongX;
        Vector3 AZ => Fp.AlongZ;
        Vector3 P(float x, float y, float z) => Fp.World(x, y, z);
        /// <summary>Main deck (sheathing top) height at plan z.</summary>
        public float MainDeck(float z) => A + S * z;
        /// <summary>Wing deck height at plan x (s = run in from the wing wall line on either side).</summary>
        public float WingDeck(float x) => A + S * (HalfW - Mathf.Abs(x - Xc));
        float Cos => Mathf.Cos(Mathf.Atan(S));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Catalog.Register(new PieceDefinition { Id = "pt2x4x16", Name = "PT 2x4 x 16' (slab plate)", Category = PieceCategory.Lumber, Size = new Vector3(I(3.5f), I(1.5f), I(192)), Price = 12, Cuttable = true, Color = new Color(0.55f, 0.62f, 0.42f) });
            // the flat valley board of an overlay valley bears on the main roof deck
            var r = StructuralRules.Get(MemberRole.ValleyRafter);
            var sup = new List<MemberRole>(r.SupportedBy) { MemberRole.RoofSheathing, MemberRole.Truss, MemberRole.Rafter };
            StructuralRules.Set(new RoleRule { Role = MemberRole.ValleyRafter, Requires = r.Requires, SupportedBy = sup.ToArray(), Note = "valley rafter, or a flat 2x valley board on the deck (California valley)" });
        }

        /// <summary>Frames the wing. centerX / widthFt / projectionFt in plan (outside of framing).</summary>
        public static CrossGable Frame(RoofInfo main, float centerX, float widthFt, float projectionFt)
        {
            var job = FramingJob.Create("Cross gable: garage wing + valleys");
            job.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var cg = job.gameObject.AddComponent<CrossGable>();
            cg.Init(main, centerX, widthFt * Units.Foot, projectionFt * Units.Foot);
            cg.FramingJob = job;
            cg.BuildFraming(job);
            return cg;
        }

        void Init(RoofInfo main, float cx, float w, float proj)
        {
            Main = main; Fp = main.Footprint; S = main.Slope; Oh = main.EaveOverhang > 0f ? main.EaveOverhang : I(12f);
            X0 = cx - w / 2; X1 = cx + w / 2; Projection = proj;
            for (int i = 0; i < main.Planes.Count; i++)
                if (Vector3.Dot(main.Planes[i].OutHorizontal, -Fp.AlongZ) > 0.9f) MainPlane = i;
            if (MainPlane < 0) { Debug.LogError("[CrossGable] main roof has no plane over the front wall"); MainPlane = 0; }
            var mp = main.Planes[MainPlane];
            // deck height at the front wall line, from the plane itself (so the valley closes exactly)
            Vector3 w0 = P(cx, 0f, 0f), w1 = P(cx, 0f, 1f);
            float Y(Vector3 q) => mp.EaveStart.y - (mp.Normal.x * (q.x - mp.EaveStart.x) + mp.Normal.z * (q.z - mp.EaveStart.z)) / mp.Normal.y;
            A = Y(w0);
            S = Y(w1) - A;
            E = mp.SheathingThickness / Cos;
            // valleys in RoofInfo (main dry-in lays the main-side ice & water from these)
            for (int side = -1; side <= 1; side += 2)
            {
                float xs = side < 0 ? X0 : X1;
                var a = P(xs + side * Oh, MainDeck(-Oh), -Oh);
                var b = P(Xc, MainDeck(HalfW), HalfW);
                var e = new RoofEdge(a, b, MainPlane, -1);
                Valleys.Add(e); main.Valleys.Add(e);
            }
        }

        // ------------------------------------------------------------------ framing

        StructuralMember Member(string id, Transform root, Vector3 center, Quaternion rot, Vector3 size, MemberRole role, Action<FrameKit> draw, Connector c = Connector.Nailed)
            => Framer.Custom(id, size, root, center, rot, role, (b, s) =>
            {
                b.Transform = Matrix4x4.TRS(center, rot, Vector3.one).inverse;
                draw(new FrameKit(b, s));
                b.ResetTransform();
            }, c, "wing");

        /// <summary>Member from a convex (x or z, y) profile extruded across [t0, t1] along 'thick'. u = AX or AZ.</summary>
        StructuralMember Profile(string id, Transform root, List<Vector2> poly, Vector3 u, Vector3 thick, float t0, float t1, Vector3 grain, MemberRole role, Connector c = Connector.Nailed)
        {
            Vector3 origin = P(0f, 0f, 0f);
            Vector2 cen = Geo2.Centroid(poly);
            Vector3 center = origin + u * cen.x + Vector3.up * cen.y + thick * ((t0 + t1) * 0.5f);
            var gdir = grain.normalized;
            float lo = float.MaxValue, hi = float.MinValue, dlo = float.MaxValue, dhi = float.MinValue;
            Vector3 dAxis = Vector3.Cross(gdir, thick).normalized;
            foreach (var q in poly)
            {
                Vector3 w = origin + u * q.x + Vector3.up * q.y;
                float a = Vector3.Dot(w - center, gdir), d = Vector3.Dot(w - center, dAxis);
                lo = Mathf.Min(lo, a); hi = Mathf.Max(hi, a); dlo = Mathf.Min(dlo, d); dhi = Mathf.Max(dhi, d);
            }
            center += gdir * ((lo + hi) * 0.5f) + dAxis * ((dlo + dhi) * 0.5f);
            var rot = Quaternion.LookRotation(gdir, thick);
            var size = new Vector3(dhi - dlo, Mathf.Abs(t1 - t0), hi - lo);
            return Member(id, root, center, rot, size, role, k => k.Prism(poly, origin, u, Vector3.up, thick, t0, t1, SurfaceKind.PineLumber, grain));
        }

        void BuildFraming(FramingJob job)
        {
            var root = job.transform;
            float wd = Fp.WallDepth > 0f ? Fp.WallDepth : I(3.5f), t = I(1.5f), slab = Fp.Origin.y, PT = Fp.PlateTopY;
            float zF = -Projection;
            Func<Vector3, Vector3, Vector3, Func<(Vector3, Vector3, Quaternion)>> ghost = (c, size, fwd) => () => (c, size, Quaternion.LookRotation(fwd));

            // --- wing walls on the slab: PT bottom plate, 2x4 studs 16" O.C., top + cap plates level with the main plates
            float studLen = PT - slab - 3 * t;
            var walls = new[]
            {
                (name: "front (gable end)", a: new Vector2(X0, zF + wd / 2), b: new Vector2(X1, zF + wd / 2)),
                (name: "left side", a: new Vector2(X0 + wd / 2, zF + wd), b: new Vector2(X0 + wd / 2, 0f)),
                (name: "right side", a: new Vector2(X1 - wd / 2, zF + wd), b: new Vector2(X1 - wd / 2, 0f)),
            };
            foreach (var wl in walls)
            {
                var w = wl;
                Vector2 d2 = w.b - w.a; float len = d2.magnitude; Vector3 dir = (AX * d2.x + AZ * d2.y).normalized;
                Vector3 across = Vector3.Cross(Vector3.up, dir);
                Vector3 mid = P((w.a.x + w.b.x) / 2, 0f, (w.a.y + w.b.y) / 2);
                // plates longer than a 16' board are spliced: bottom anywhere, top on a stud, cap 24" off the top plate joints
                int nStuds = Mathf.Max(2, Mathf.CeilToInt(len / I(16f)) + 1);
                var studAt = new List<float>();
                for (int i = 0; i < nStuds; i++) studAt.Add(Mathf.Min(i * I(16f), len - t) + t / 2);
                var botSegs = Splice.Segments(len, Splice.LongestStock("pt2x4"));
                var topSegs = Splice.Segments(len, Splice.LongestStock("2x4"), studAt);
                var capSegs = Splice.Segments(len, Splice.LongestStock("2x4"), studAt, Splice.Joints(topSegs), I(24f));
                var step = job.Add("Wing wall (" + w.name + "): PT plate on the slab, studs 16\" O.C., top + cap plates", ToolSets.Fasten, () =>
                {
                    foreach (var (a, b) in botSegs)
                        Framer.Make(FloorPlan.PickStock("pt2x4", b - a), b - a, root, mid + dir * ((a + b) / 2 - len / 2) + Vector3.up * (slab + t / 2), dir, Vector3.up, MemberRole.BottomPlate, Connector.Nailed | Connector.AnchorBolt, "wing");
                    foreach (var sa in studAt)
                        Framer.Make("2x4x12", studLen, root, mid + dir * (sa - len / 2) + Vector3.up * (slab + t + studLen / 2), Vector3.up, dir, MemberRole.Stud, Connector.Nailed, "wing");
                    foreach (var (a, b) in topSegs)
                        Framer.Make(FloorPlan.PickStock("2x4", b - a), b - a, root, mid + dir * ((a + b) / 2 - len / 2) + Vector3.up * (PT - t * 1.5f), dir, Vector3.up, MemberRole.TopPlate, Connector.Nailed, "wing");
                    foreach (var (a, b) in capSegs)
                        Framer.Make(FloorPlan.PickStock("2x4", b - a), b - a, root, mid + dir * ((a + b) / 2 - len / 2) + Vector3.up * (PT - t * 0.5f), dir, Vector3.up, MemberRole.CapPlate, Connector.Nailed, "wing");
                }, ghost(mid + Vector3.up * (PT / 2 + slab / 2), new Vector3(wd, PT - slab, len), dir)).Use("2x4x12", nStuds);
                foreach (var (a, b) in botSegs) step.Use(FloorPlan.PickStock("pt2x4", b - a));
                foreach (var (a, b) in topSegs) step.Use(FloorPlan.PickStock("2x4", b - a));
                foreach (var (a, b) in capSegs) step.Use(FloorPlan.PickStock("2x4", b - a));
                step.Nails = 40;
            }

            // --- valley lines + flat 2x10 valley boards on the main deck
            float boardT = I(1.5f), boardW = I(9.25f);
            foreach (var v in Valleys)
            {
                var e = v;
                job.Add("Snap the valley line on the main deck (wall corner to the wing ridge)", ToolSets.Chalk, () => ChalkLine.Snap(root, e.A, e.B), () => ((e.A + e.B) * 0.5f, new Vector3(0.05f, 0.05f, e.Length), Quaternion.LookRotation(e.B - e.A)));
                var mp = Main.Planes[MainPlane];
                Vector3 dirV = (e.B - e.A).normalized;
                Vector3 toWing = Vector3.Cross(mp.Normal, dirV);
                if (Vector3.Dot(toWing, P(Xc, 0, 0) - P(0, 0, 0) - ((e.A + e.B) * 0.5f - P(0, 0, 0))) < 0f) toWing = -toWing;
                toWing.Normalize();
                Vector3 c = (e.A + e.B) * 0.5f + toWing * (I(3.2f) + boardW / 2) + mp.Normal * (boardT / 2);
                float len = e.Length - I(4f);
                var rot = Quaternion.LookRotation(dirV, mp.Normal);
                job.Add("Lay the 2x10 valley board flat along the line, nail to the main rafters", ToolSets.Fasten, () =>
                    Member("2x10x16", root, c, rot, new Vector3(boardW, boardT, len), MemberRole.ValleyRafter,
                        k => k.B.AddBevelBox(Vector3.zero + c, new Vector3(boardW, boardT, len), rot, I(0.06f), k.Sub(SurfaceKind.PineLumber), k.Sub(SurfaceKind.PineEndGrain))),
                    () => (c, new Vector3(boardW, boardT, len), rot)).Use("2x10x16").Nails = 10;
            }

            // --- wing ridge board (2x10): from the gable end to where it lands on the main deck
            float Dr = I(7.25f), Dv = Dr / Cos, ridgeD = I(9.25f);
            float yTop = WingDeck(Xc) - E, yBot = yTop - ridgeD;
            float zt = (yTop - A + E) / S, zb = (yBot - A + E) / S; // framing-top plane of the main roof = MainDeck - E
            var ridgePoly = new List<Vector2> { new Vector2(zF, yBot), new Vector2(zb, yBot), new Vector2(zt, yTop), new Vector2(zF, yTop) };
            job.Add("Wing ridge board 2x10: gable end to the main roof (plumb + slope cut)", ToolSets.Fasten,
                () => Profile("2x10x16", root, ridgePoly, AZ, AX, Xc - t / 2, Xc + t / 2, AZ, MemberRole.RidgeBoard),
                () => (P(Xc, (yTop + yBot) / 2, (zF + zt) / 2), new Vector3(t, ridgeD, zt - zF), Quaternion.LookRotation(AZ))).Use("2x10x16").Nails = 6;

            // --- rafters 16" O.C. from the gable end: commons over the wing plates, valley jacks over the main deck
            var zs = new List<float>();
            for (float z = zF + t / 2; z < HalfW - I(2f); z += I(16f)) zs.Add(z);
            foreach (float z in zs)
            {
                bool common = z < -Oh - t;
                for (int side = -1; side <= 1; side += 2)
                {
                    var polys = common ? CommonProfile(side, Dv) : JackProfile(side, z, Dv);
                    if (polys == null) continue;
                    float zz = z; int sd = side;
                    Vector3 grain = (AX * -sd + Vector3.up * S).normalized;
                    string label = common ? "Common rafter 2x8 (" + (sd < 0 ? "left" : "right") + "), birdsmouth on the wing plate, hurricane tie"
                                          : "Valley jack 2x8 (" + (sd < 0 ? "left" : "right") + "): seat cut on the valley board, face-nail at the ridge";
                    var conn = common ? Connector.Nailed | Connector.HurricaneTie : Connector.Nailed;
                    job.Add(label, ToolSets.Fasten, () =>
                    {
                        foreach (var poly in polys)
                            Profile(common ? "2x8x16" : "2x8x12", root, poly, AX, AZ, zz - t / 2, zz + t / 2, grain, common ? MemberRole.Rafter : MemberRole.JackRafter, conn);
                    }, () => (P(Xc - sd * HalfW / 2, WingDeck(Xc - sd * HalfW / 2), zz), new Vector3(HalfW, Dr, t), Quaternion.LookRotation(AX))).Use(common ? "2x8x16" : "2x8x12").Nails = 6;
                }
            }

            // --- fascia (1x6) on the wing eaves, then the OSB deck clipped at the valleys
            job.Add("Wing fascia 1x6 on the rafter tails (both eaves)", ToolSets.Fasten, () =>
                Framer.AttachMesh(root, "Wing fascia", (MeshBuilder b, Func<SurfaceKind, int> s) =>
                {
                    int g = s(SurfaceKind.PaintedWhite);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = (side < 0 ? X0 - Oh : X1 + Oh) + side * I(0.375f), yT = WingDeck(side < 0 ? X0 - Oh : X1 + Oh) - E;
                        float za = zF, zb2 = -Oh;
                        b.AddBox(P(x, yT - I(2.75f), (za + zb2) / 2), new Vector3(I(0.75f), I(5.5f), zb2 - za), Quaternion.LookRotation(AZ), g, Vector3.forward);
                    }
                }), () => (P(Xc, A, zF / 2), new Vector3(HalfW * 2, 0.2f, -zF), Quaternion.LookRotation(AZ))).Use("1x6x16", 2).Nails = 12;
            for (int side = -1; side <= 1; side += 2)
            {
                int sd = side;
                job.Add("Wing roof sheathing (" + (sd < 0 ? "left" : "right") + "): 7/16\" OSB, cut on the valley line", ToolSets.Fasten, () =>
                    Framer.AttachMesh(root, "Wing sheathing " + (sd < 0 ? "L" : "R"), (MeshBuilder b, Func<SurfaceKind, int> s) => Deck(b, s(SurfaceKind.Osb), sd)),
                    () => (P(Xc - sd * HalfW / 2, WingDeck(Xc - sd * HalfW / 2), zF / 2), new Vector3(HalfW, 0.05f, -zF), Quaternion.LookRotation(AZ))).Use("osb716roof", Mathf.CeilToInt(HalfW * (HalfW - zF) / 2.97f) + 1).Nails = 40;
            }
            job.Completed += () => Framed = true;
        }

        /// <summary>Common rafter: tail (eave overhang, plumb cut) + body with a birdsmouth seat on the plate (2 convex pieces).</summary>
        List<List<Vector2>> CommonProfile(int side, float Dv)
        {
            float PT = Fp.PlateTopY, xw = side < 0 ? X0 : X1, xr = Xc + side * I(0.75f);
            float Y(float x) => WingDeck(x) - E;
            float xe = xw + side * Oh;
            var tail = new List<Vector2> { new Vector2(xe, Y(xe)), new Vector2(xw, Y(xw)), new Vector2(xw, Y(xw) - Dv), new Vector2(xe, Y(xe) - Dv) };
            float xs = xw - side * (PT - (Y(xw) - Dv)) / S;
            List<Vector2> body;
            if ((xr - xs) * -side > 0f) body = new List<Vector2> { new Vector2(xw, PT), new Vector2(xw, Y(xw)), new Vector2(xr, Y(xr)), new Vector2(xr, Y(xr) - Dv), new Vector2(xs, PT) };
            else body = new List<Vector2> { new Vector2(xw, PT), new Vector2(xw, Y(xw)), new Vector2(xr, Y(xr)), new Vector2(xr, PT) };
            return new List<List<Vector2>> { tail, body };
        }

        /// <summary>Valley jack: ridge plumb cut at the top, seat cut flat on the valley board, toe running out to the deck.</summary>
        List<List<Vector2>> JackProfile(int side, float z, float Dv)
        {
            float xw = side < 0 ? X0 : X1, xr = Xc + side * I(0.75f);
            float Y(float x) => WingDeck(x) - E;
            float yp = MainDeck(z) + I(1.5f) / Cos;              // top of the valley board
            float xlo = xw - side * (yp - (A - E)) / S;          // where the jack's top edge meets the board
            float xseat = xw - side * (yp + Dv - (A - E)) / S;   // where its bottom edge would meet the board
            if ((xr - xlo) * -side <= I(1f)) return null;
            var poly = new List<Vector2> { new Vector2(xlo, yp) };
            if ((xr - xseat) * -side > 0f) { poly.Add(new Vector2(xseat, yp)); poly.Add(new Vector2(xr, Y(xr) - Dv)); }
            else poly.Add(new Vector2(xr, yp));
            poly.Add(new Vector2(xr, Y(xr)));
            return new List<List<Vector2>> { poly };
        }

        /// <summary>Plan polygon of one wing plane at the deck: rake (gable end) to ridge, eave to the valley line.</summary>
        List<Vector2> PlanePlan(int side, float valleyOffset = 0f, float rake = 0f)
        {
            float xe = (side < 0 ? X0 - Oh : X1 + Oh), xw = side < 0 ? X0 : X1, zF = -Projection - rake;
            var poly = Geo2.Rect(Mathf.Min(xe, Xc), zF, Mathf.Max(xe, Xc), HalfW + 1f);
            // valley: z <= |x - xw|-ish run from the wall corner, i.e. z <= (x - xw) * -side - valleyOffset
            var h = new HalfPlane(new Vector2(xw, -valleyOffset), new Vector2(-side, -1f));
            return Geo2.Clip(poly, h);
        }

        void Deck(MeshBuilder b, int g, int side)
        {
            var poly = PlanePlan(side);
            if (poly.Count < 3) return;
            var top = new List<Vector3>(); var bot = new List<Vector3>();
            foreach (var q in poly) { top.Add(P(q.x, WingDeck(q.x), q.y)); bot.Add(P(q.x, WingDeck(q.x) - E, q.y)); }
            Vector3 n = (Vector3.up + AX * (side * S)).normalized;
            b.AddPolygon(top, g, n, AZ);
            b.AddPolygon(bot, g, -n, AZ);
            for (int i = 0; i < top.Count; i++)
            {
                int j = (i + 1) % top.Count;
                Vector3 mid = (top[i] + top[j]) * 0.5f, cen = Vector3.zero;
                foreach (var p in top) cen += p; cen /= top.Count;
                b.AddQuad(bot[i], bot[j], top[j], top[i], g, mid - cen);
            }
            // panel joints: 4' courses up the slope, 8' sheets staggered (thin dark lines)
            int seam = g;
            float run = 48f * Units.Inch * Cos;
            float xe = side < 0 ? X0 - Oh : X1 + Oh;
            for (float s2 = run; s2 < HalfW + Oh - 0.05f; s2 += run)
            {
                float x = xe - side * s2;
                var l = new List<Vector2> { new Vector2(x - 0.002f, -Projection), new Vector2(x + 0.002f, -Projection), new Vector2(x + 0.002f, HalfW), new Vector2(x - 0.002f, HalfW) };
                l = Geo2.Clip(l, new HalfPlane(new Vector2(side < 0 ? X0 : X1, 0f), new Vector2(-side, -1f)));
                if (l.Count < 3) continue;
                var pts = new List<Vector3>(); foreach (var q in l) pts.Add(P(q.x, WingDeck(q.x) + 0.0006f, q.y));
                b.AddPolygon(pts, Mathf.Max(0, seam), n, AZ);
            }
        }

        // ------------------------------------------------------------------ roofing

        /// <summary>Roofing the wing after the main roof is dried in and shingled up past the valleys.</summary>
        public static FramingJob Roof(CrossGable cg)
        {
            var job = FramingJob.Create("Cross gable roofing: valleys, closed-cut");
            job.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            cg.RoofingJob = job;
            var root = job.transform;
            float zF = -cg.Projection;
            Func<(Vector3, Vector3, Quaternion)> G(float x) => () => (cg.P(x, cg.WingDeck(x), zF / 2), new Vector3(0.3f, 0.05f, -zF), Quaternion.LookRotation(cg.AZ));
            for (int side = -1; side <= 1; side += 2)
            {
                int sd = side;
                float xv = cg.Xc - sd * cg.HalfW * 0.5f;
                job.Add("Ice & water shield: 18\" on the wing side of the " + (sd < 0 ? "left" : "right") + " valley (laps the main-side strip)", ToolSets.Roof,
                    () => cg.Layer(root, sd, "Valley ice & water (wing)", RoofTextures.IceWater, I(0.04f), band0: 0f, band1: I(18f) / cg.Cos), G(xv)).Use("ice-water", 1);
                job.Add("Synthetic underlayment on the wing (" + (sd < 0 ? "left" : "right") + "), lapped over the valley membrane", ToolSets.Staple,
                    () => cg.Layer(root, sd, "Wing underlayment", RoofTextures.Underlayment, I(0.03f), band0: I(17f) / cg.Cos, band1: 99f), G(xv)).Use("underlayment", 1).Use("cap-staples", 60);
                job.Add("Weave the main-roof courses 12\" across the " + (sd < 0 ? "left" : "right") + " valley onto the wing", ToolSets.Roof,
                    () => cg.CrossCourses(root, sd), G(xv)).Use("shingles-arch", 1).Use("roof-nails", 60);
                job.Add("Snap the cut line 2\" off the valley, shingle the wing (" + (sd < 0 ? "left" : "right") + "), cut + cement the closed-cut valley", ToolSets.Roof,
                    () => cg.WingCourses(root, sd), G(xv)).Use("starter-strip", 1).Use("shingles-arch", Mathf.CeilToInt(cg.HalfW * cg.Projection * 10.764f / cg.Cos / 33.3f) + 1).Use("roof-nails", 300);
            }
            job.Add("Ridge cap on the wing ridge (nail 1\" above the exposure, lapped away from the wind)", ToolSets.Roof, () => cg.RidgeCap(root), G(cg.Xc)).Use("ridge-cap", 1);
            job.Completed += () => cg.Roofed = true;
            return job;
        }

        /// <summary>Membrane on one wing plane between band0 and band1 (plan distance from the valley line along x).</summary>
        void Layer(Transform root, int side, string name, Material mat, float h, float band0, float band1)
        {
            float xw = side < 0 ? X0 : X1;
            var poly = PlanePlan(side, 0f, I(0.5f));
            poly = Geo2.Clip(poly, new HalfPlane(new Vector2(xw, -band0), new Vector2(-side, -1f)));      // at least band0 from the valley
            if (band1 < 50f) poly = Geo2.Clip(poly, new HalfPlane(new Vector2(xw, -band1), new Vector2(side, 1f)));
            if (poly.Count < 3) return;
            var m = new RoofMesh(); int s = m.Mat(mat);
            var pts = new List<Vector3>(); var uv = new List<Vector2>();
            foreach (var q in poly) { pts.Add(P(q.x, WingDeck(q.x) + h, q.y)); uv.Add(new Vector2(q.y, q.x / Cos)); }
            m.Poly(pts, uv, Vector3.up, s);
            m.Attach(root, name);
        }

        const float ExpoIn = 5.625f, ShingleHIn = 13.25f;

        /// <summary>Main-roof courses carried 12" across the valley: butt lines continue the main courses (constant z).</summary>
        void CrossCourses(Transform root, int side)
        {
            float xw = side < 0 ? X0 : X1;
            var m = new RoofMesh(); int s = m.Mat(RoofTextures.Shingle), sh = m.Mat(RoofTextures.ShingleShadow);
            float ex = I(ExpoIn) * Cos;
            for (float z0 = -Oh; z0 < HalfW; z0 += ex)
            {
                var poly = Geo2.Rect(Mathf.Min(xw + side * Oh, Xc) - 1f, z0, Mathf.Max(xw + side * Oh, Xc) + 1f, z0 + ex);
                poly = Geo2.Clip(poly, new HalfPlane(new Vector2(xw, 0f), new Vector2(-side, -1f)));               // wing side of the valley
                poly = Geo2.Clip(poly, new HalfPlane(new Vector2(xw, -I(12f)), new Vector2(side, 1f)));            // 12" onto the wing
                poly = Geo2.Clip(poly, side < 0 ? HalfPlane.XMax(Xc) : HalfPlane.XMin(Xc));
                Course(m, poly, s, sh, I(0.16f), true);
            }
            if (!m.Empty) m.Attach(root, "Main courses across the valley");
        }

        /// <summary>A wing plane as a RoofPlane (deck top), its valley end clipped 'cut' back from the valley line,
        /// so the regular shingle courses (tabs, stagger, rake overhang) can be laid on it.</summary>
        public RoofPlane WingPlane(int side, float cut)
        {
            float c = Cos, slope = (HalfW + Oh) / c;
            Vector3 up = (AX * -side + Vector3.up * S).normalized;
            var p = new RoofPlane
            {
                Name = "Wing " + (side < 0 ? "left" : "right"), PitchRise = Mathf.RoundToInt(S * 12f), UpSlope = up,
                Normal = Vector3.Cross(side < 0 ? -AZ : AZ, up).normalized, SlopeLength = slope, EaveLength = HalfW + Projection,
                SheathingThickness = I(0.4375f), FasciaOutset = I(0.75f),
            };
            if (Vector3.Dot(p.Normal, Vector3.up) < 0f) p.Normal = -p.Normal;
            float xe = side < 0 ? X0 - Oh : X1 + Oh;
            float cu = cut * 1.41421f;
            if (side < 0)
            {
                p.AlongEave = -AZ; p.EaveStart = P(xe, WingDeck(xe), HalfW);
                p.ExtraBounds = new List<HalfPlane> { new HalfPlane(new Vector2(HalfW + Oh + cu, 0f), new Vector2(1f, c)) };
            }
            else
            {
                p.AlongEave = AZ; p.EaveStart = P(xe, WingDeck(xe), -Projection);
                p.ExtraBounds = new List<HalfPlane> { new HalfPlane(new Vector2(Projection - Oh - cu, 0f), new Vector2(-1f, c)) };
            }
            return p;
        }

        /// <summary>Wing courses (starter + architectural courses) cut 2" back from the valley centerline: closed-cut valley.</summary>
        void WingCourses(Transform root, int side)
        {
            float xw = side < 0 ? X0 : X1, cut = I(2f);
            var p = WingPlane(side, cut);
            var m = new RoofMesh();
            ShingleMesh.Starter(m, p, RoofTextures.Shingle, RoofTextures.AsphaltEdge);
            int n = ShingleMesh.CourseCount(p);
            for (int k = 0; k < n; k++) ShingleMesh.Course(m, p, 50 + side, k, RoofTextures.Shingle, RoofTextures.ShingleShadow, RoofTextures.AsphaltEdge);
            // trimmed edge along the chalk line: cut asphalt + a bead of roofing cement
            int dark = m.Mat(RoofTextures.AsphaltEdge);
            var edge = Geo2.Clip(PlanePlan(side), new List<HalfPlane> { new HalfPlane(new Vector2(xw, -cut), new Vector2(side, 1f)), new HalfPlane(new Vector2(xw, -cut + I(0.3f)), new Vector2(-side, -1f)) });
            if (edge.Count >= 3)
            {
                var pts = new List<Vector3>(); var uv = new List<Vector2>();
                foreach (var q in edge) { pts.Add(P(q.x, WingDeck(q.x) + I(0.12f), q.y)); uv.Add(new Vector2(q.y, q.x)); }
                m.Poly(pts, uv, Vector3.up, dark);
            }
            if (!m.Empty) m.Attach(root, "Wing shingles " + (side < 0 ? "L" : "R"));
        }

        /// <summary>One exposed course band (plan polygon) on the wing deck: top face at h, butt face at its low edge.</summary>
        void Course(RoofMesh m, List<Vector2> poly, int s, int shadow, float h, bool constZButt, int side = 0)
        {
            if (poly == null || poly.Count < 3) return;
            var pts = new List<Vector3>(); var uv = new List<Vector2>();
            foreach (var q in poly) { pts.Add(P(q.x, WingDeck(q.x) + h, q.y)); uv.Add(new Vector2(q.y, q.x / Cos)); }
            m.Poly(pts, uv, Vector3.up, s);
            // butt edge: lowest edge of the band drops to the course below
            float best = float.MaxValue; int bi = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                int j = (i + 1) % poly.Count;
                Vector2 c = (poly[i] + poly[j]) * 0.5f;
                float key = constZButt ? c.y : WingDeck(c.x);
                if (key < best) { best = key; bi = i; }
            }
            int bj = (bi + 1) % poly.Count;
            Vector2 a = poly[bi], b = poly[bj];
            Vector3 ta = P(a.x, WingDeck(a.x) + h, a.y), tb = P(b.x, WingDeck(b.x) + h, b.y);
            Vector3 da = ta - Vector3.up * I(0.14f), db = tb - Vector3.up * I(0.14f);
            Vector3 outDir = constZButt ? -AZ : AX * side;
            m.Quad(da, db, tb, ta, new Vector2(a.y, 0f), new Vector2(b.y, 0f), new Vector2(b.y, 0.004f), new Vector2(a.y, 0.004f), outDir + Vector3.up * 0.2f, shadow);
        }

        void RidgeCap(Transform root)
        {
            var m = new RoofMesh(); int s = m.Mat(RoofTextures.Shingle), sh = m.Mat(RoofTextures.ShingleShadow);
            float half = I(6f) * Cos, ex = I(5f);
            float zEnd = (WingDeck(Xc) - A) / S;   // ridge meets the main deck
            for (float z = -Projection - I(0.75f); z < zEnd - I(1f); z += ex)
            {
                float z1 = Mathf.Min(z + ex, zEnd);
                for (int side = -1; side <= 1; side += 2)
                {
                    float xo = Xc + side * half;
                    float yR = WingDeck(Xc) + I(0.45f), yO = WingDeck(xo) + I(0.4f);
                    m.Quad(P(Xc, yR, z), P(Xc, yR, z1), P(xo, yO, z1), P(xo, yO, z), new Vector2(z, 0), new Vector2(z1, 0), new Vector2(z1, half), new Vector2(z, half), Vector3.up + AX * side, s);
                    m.Quad(P(Xc, yR - I(0.12f), z), P(Xc, yR, z), P(xo, yO, z), P(xo, yO - I(0.12f), z), new Vector2(0, 0), new Vector2(0, 0.003f), new Vector2(half, 0.003f), new Vector2(half, 0), -AZ, sh);
                }
            }
            if (!m.Empty) m.Attach(root, "Wing ridge cap");
        }
    }
}
