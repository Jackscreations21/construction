using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Engineered floor variant: PT sills as usual, 1-1/4" LVL rim board, I-joists 16" O.C. (9-1/2" to a 16'-6" span,
    /// 11-7/8" to 20'), and past 20' a flush 3-ply LVL girder on an interior bearing line with the joists in face-mount
    /// hangers and web stiffeners at every hanger. Squash blocks (2x4, joist depth + 1/16") go under the jack studs of
    /// every opening in the walls above. No mid-span blocking: the rim board and the glued subfloor brace the joists.
    /// Selected in Framing Plans ("Use I-joists"); inspection checks spans, hangers and stiffeners.
    /// </summary>
    public static class IJoistFloor
    {
        public const float RimIn = 1.25f, FlangeWIn = 2.3125f, FlangeDIn = 1.375f, WebIn = 0.375f;
        public const float MaxSpan9In = 198f, MaxSpan12In = 240f;   // L/480-ish residential tables, 40 psf live, 16" O.C.
        const float GirderPlyIn = 1.75f;
        static float I(float v) => v * Units.Inch;

        public static float DepthForSpan(float spanIn) => spanIn <= MaxSpan9In ? 9.5f : 11.875f;
        public static float MaxSpanIn(float depthIn) => depthIn < 10f ? MaxSpan9In : MaxSpan12In;
        static string Tag(float depthIn) => depthIn < 10f ? "9" : "12";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Color pine = new Color(0.86f, 0.72f, 0.50f), lvl = new Color(0.88f, 0.70f, 0.48f), galv = new Color(0.72f, 0.74f, 0.76f);
            PieceDefinition L(string id, string name, float d, float t, float len, int price, Color c) =>
                new PieceDefinition { Id = id, Name = name, Category = PieceCategory.Lumber, Size = new Vector3(I(d), I(t), I(len)), Price = price, Cuttable = true, Color = c };
            PieceDefinition C(string id, string name, int price, Color c) =>
                new PieceDefinition { Id = id, Name = name, Category = PieceCategory.Consumable, Price = price, UnitsPerPurchase = 1, Color = c };
            Catalog.Register(
                L("ij9x16", "I-joist 9-1/2\" x 16'", 9.5f, FlangeWIn, 192, 44, pine),
                L("ij9x20", "I-joist 9-1/2\" x 20'", 9.5f, FlangeWIn, 240, 55, pine),
                L("ij9x24", "I-joist 9-1/2\" x 24'", 9.5f, FlangeWIn, 288, 66, pine),
                L("ij12x16", "I-joist 11-7/8\" x 16'", 11.875f, FlangeWIn, 192, 52, pine),
                L("ij12x20", "I-joist 11-7/8\" x 20'", 11.875f, FlangeWIn, 240, 65, pine),
                L("ij12x24", "I-joist 11-7/8\" x 24'", 11.875f, FlangeWIn, 288, 78, pine),
                L("lvlrim9x16", "LVL rim board 1-1/4 x 9-1/2 x 16'", 9.5f, RimIn, 192, 46, lvl),
                L("lvlrim9x24", "LVL rim board 1-1/4 x 9-1/2 x 24'", 9.5f, RimIn, 288, 69, lvl),
                L("lvlrim12x16", "LVL rim board 1-1/4 x 11-7/8 x 16'", 11.875f, RimIn, 192, 54, lvl),
                L("lvlrim12x24", "LVL rim board 1-1/4 x 11-7/8 x 24'", 11.875f, RimIn, 288, 81, lvl),
                L("lvlbeam9x16", "LVL 1-3/4 x 9-1/2 x 16' (girder ply)", 9.5f, GirderPlyIn, 192, 92, lvl),
                L("lvlbeam9x24", "LVL 1-3/4 x 9-1/2 x 24' (girder ply)", 9.5f, GirderPlyIn, 288, 138, lvl),
                L("lvlbeam12x16", "LVL 1-3/4 x 11-7/8 x 16' (girder ply)", 11.875f, GirderPlyIn, 192, 112, lvl),
                L("lvlbeam12x24", "LVL 1-3/4 x 11-7/8 x 24' (girder ply)", 11.875f, GirderPlyIn, 288, 168, lvl),
                C("hanger-ij9", "Face-mount I-joist hanger 2-5/16 x 9-1/2 (IUS2.37/9.5)", 3, galv),
                C("hanger-ij12", "Face-mount I-joist hanger 2-5/16 x 11-7/8 (IUS2.37/11.88)", 3, galv),
                C("web-stiffener", "Web stiffener pair, 23/32\" OSB (cut to fit)", 1, new Color(0.78f, 0.62f, 0.38f)));
            StructuralRules.Set(new RoleRule
            {
                Role = MemberRole.SquashBlock, Requires = Connector.Nailed,
                SupportedBy = new[] { MemberRole.SillPlate, MemberRole.Beam, MemberRole.TopPlate },
                Note = "2x4 squash blocks 1/16\" taller than the joist, under point loads",
            });
            if (!ProductModels.Overrides.Contains(Model)) ProductModels.Overrides.Add(Model);
            if (!FramingInspection.Checks.Contains(Check)) FramingInspection.Checks.Add(Check);
        }

        /// <summary>I-joist visual: two solid-sawn/LVL flanges (2-5/16" x 1-3/8") on a 3/8" OSB web glued into routed grooves.
        /// Piece frame: X = depth, Y = flange width, Z = length.</summary>
        static bool Model(PieceDefinition d, Vector3 s, MeshBuilder b, Func<Material, int> mat)
        {
            if (d == null || d.Category != PieceCategory.Lumber || d.Id == null || !d.Id.StartsWith("ij")) return false;
            float hx = s.x / 2, fd = I(FlangeDIn);
            int fl = mat(Materials.Get(SurfaceKind.PineLumber, new Color(1f, 0.95f, 0.86f)));
            int osb = mat(Materials.Get(SurfaceKind.Osb));
            int ink = mat(Materials.Get(SurfaceKind.DarkPlastic));
            b.AddBevelBox(new Vector3(hx - fd / 2, 0f, 0f), new Vector3(fd, s.y, s.z), Quaternion.identity, I(0.06f), fl);
            b.AddBevelBox(new Vector3(-hx + fd / 2, 0f, 0f), new Vector3(fd, s.y, s.z), Quaternion.identity, I(0.06f), fl);
            b.AddBox(Vector3.zero, new Vector3(s.x - 2 * fd + I(0.75f), I(WebIn), s.z), Quaternion.identity, osb, Vector3.forward);
            // printed line along the web (manufacturer stamp strip), both faces, every 4'
            for (float z = -s.z / 2 + I(24f); z < s.z / 2 - I(12f); z += I(48f))
                for (int side = -1; side <= 1; side += 2)
                    b.AddBox(new Vector3(0f, side * (I(WebIn) / 2 + 0.0004f), z), new Vector3(I(0.45f), 0.0004f, I(14f)), Quaternion.identity, ink, Vector3.forward, side > 0 ? MeshBuilder.FacePosY : MeshBuilder.FaceNegY);
            return true;
        }

        static List<(float start, float len)> Segments(float total, float maxLen)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(total / maxLen - 1e-4f));
            var l = new List<(float, float)>();
            for (int i = 0; i < n; i++) l.Add((total * i / n, total / n));
            return l;
        }

        public static FramingJob Build(Footprint fp, List<WallOpening>[] openings = null)
        {
            var root = FloorPlan.Root(fp, "Floor System (I-joists)");
            var job = FramingJob.Create("Floor deck (I-joists)", root);
            float L = fp.Length, W = fp.Width;
            float spanIn = Units.ToInches(W) - 2 * RimIn;
            float dIn = fp.IJoistDepthIn > 0f ? fp.IJoistDepthIn : DepthForSpan(spanIn);
            string tag = Tag(dIn);
            float JD = I(dIn), rim = I(RimIn), sillT = I(1.5f), sillW = I(5.5f), y0 = 0f, yb = y0 + sillT, ry = yb + JD / 2;
            bool girder = spanIn > MaxSpanIn(dIn);
            float gW = I(3 * GirderPlyIn), gz = W / 2;
            Func<Vector3, Vector3, Vector3, Func<(Vector3, Vector3, Quaternion)>> ghost = (c, size, fwd) =>
                () => (root.TransformPoint(c), size, root.rotation * Quaternion.LookRotation(fwd));
            Vector3 X = Vector3.right, Z = Vector3.forward, U = Vector3.up;
            float maxStock = I(288f);

            job.Add("Snap chalk lines for the sill plates", ToolSets.Chalk, () =>
            {
                ChalkLine.Snap(root, new Vector3(0, y0, 0), new Vector3(L, y0, 0));
                ChalkLine.Snap(root, new Vector3(L, y0, 0), new Vector3(L, y0, W));
                ChalkLine.Snap(root, new Vector3(L, y0, W), new Vector3(0, y0, W));
                ChalkLine.Snap(root, new Vector3(0, y0, W), new Vector3(0, y0, 0));
            }, ghost(new Vector3(L / 2, 0.01f, 0), new Vector3(L, 0.02f, 0.02f), X));
            FloorPlan.AddSills(job, root, L, W, y0, ghost);

            // interior bearing line + flush girder (only when the span is past the I-joist table)
            if (girder)
            {
                float sl = L - 2 * sillW;
                foreach (var seg in Segments(sl, I(192f)))
                {
                    var c = new Vector3(sillW + seg.start + seg.len / 2, y0 + sillT / 2, gz);
                    float len = seg.len;
                    string stock = FloorPlan.PickStock("pt2x6", len);
                    int bolts = Mathf.Max(2, Mathf.CeilToInt((len - I(24)) / I(72f)) + 1);
                    job.Add("Interior bearing line: gasket + PT 2x6 sill on the center footing (" + Units.Format(len) + ")", ToolSets.Fasten, () =>
                    {
                        var m = Framer.Make(stock, len, root, c, X, U, MemberRole.SillPlate, Connector.Nailed | Connector.Gasket | Connector.AnchorBolt, "floor");
                        for (int i = 0; i < bolts; i++)
                        {
                            float zz = Mathf.Lerp(-len / 2 + I(12), len / 2 - I(12), i / (float)(bolts - 1));
                            Framer.Hardware(m.transform, "AnchorBolt", new Vector3(0, sillT / 2, zz), Quaternion.identity, (b, g) =>
                            {
                                b.AddCylinder(new Vector3(0, I(0.75f), 0), I(0.25f), I(1.75f), 10, g);
                                b.AddBox(new Vector3(0, I(0.1f), 0), new Vector3(I(2f), I(0.18f), I(2f)), g);
                                b.AddCylinder(new Vector3(0, I(0.45f), 0), I(0.42f), I(0.45f), 6, g);
                            });
                        }
                    }, ghost(c, new Vector3(sillW, sillT, len), X)).Use(stock).Use("sill-gasket", Mathf.CeilToInt(Units.ToInches(len) / 12f)).Use("anchor-bolt", bolts).Nails = 0;
                }
                float gl = L - 2 * rim;
                for (int ply = 0; ply < 3; ply++)
                    foreach (var seg in Segments(gl, maxStock))
                    {
                        float len = seg.len;
                        var c = new Vector3(rim + seg.start + len / 2, ry, gz + (ply - 1) * I(GirderPlyIn));
                        string stock = FloorPlan.PickStock("lvlbeam" + tag, len);
                        job.Add("Girder ply " + (ply + 1) + "/3: LVL 1-3/4 x " + Units.Format(JD) + ", nail plies 12\" O.C. in 3 rows", ToolSets.Fasten,
                            () => Framer.Make(stock, len, root, c, X, Z, MemberRole.Beam, Connector.Nailed, "floor"),
                            ghost(c, new Vector3(I(GirderPlyIn), JD, len), X)).Use(stock).Nails = 12;
                    }
            }

            // LVL rim board: long sides full length (spliced), end rims between them
            foreach (float z in new[] { rim / 2, W - rim / 2 })
                foreach (var seg in Segments(L, maxStock))
                {
                    float len = seg.len;
                    var c = new Vector3(seg.start + len / 2, ry, z);
                    string stock = FloorPlan.PickStock("lvlrim" + tag, len);
                    job.Add("LVL rim board (" + (z < W / 2 ? "front" : "back") + ", " + Units.Format(len) + "), toenail to the sill 6\" O.C.", ToolSets.Fasten,
                        () => Framer.Make(stock, len, root, c, X, Z, MemberRole.RimJoist, Connector.Nailed, "floor"),
                        ghost(c, new Vector3(rim, JD, len), X)).Use(stock).Nails = 8;
                }
            foreach (float x in new[] { rim / 2, L - rim / 2 })
                foreach (var seg in Segments(W - 2 * rim, maxStock))
                {
                    float len = seg.len;
                    var c = new Vector3(x, ry, rim + seg.start + len / 2);
                    string stock = FloorPlan.PickStock("lvlrim" + tag, len);
                    job.Add("LVL end rim (" + (x < L / 2 ? "left" : "right") + ", " + Units.Format(len) + ")", ToolSets.Fasten,
                        () => Framer.Make(stock, len, root, c, Z, X, MemberRole.RimJoist, Connector.Nailed, "floor"),
                        ghost(c, new Vector3(rim, JD, len), Z)).Use(stock).Nails = 8;
                }

            // I-joists 16" O.C.: one flange nail each side at the sill, rim nailed into the flanges
            float oc = I(16f);
            var joistX = new List<float>();
            for (float x = oc; x < L - rim - I(2f); x += oc) joistX.Add(x);
            foreach (float x in joistX)
            {
                if (!girder)
                {
                    float len = W - 2 * rim;
                    var c = new Vector3(x, ry, W / 2);
                    string stock = FloorPlan.PickStock("ij" + tag, len);
                    job.Add("I-joist @ " + Units.Format(x) + " (" + Units.Format(len) + "), nail flanges to sill + rim", ToolSets.Fasten,
                        () => Framer.Make(stock, len, root, c, Z, X, MemberRole.Joist, Connector.Nailed, "floor"),
                        ghost(c, new Vector3(I(FlangeWIn), JD, len), Z)).Use(stock).Nails = 4;
                    continue;
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    float z0 = side < 0 ? rim : gz + gW / 2, z1 = side < 0 ? gz - gW / 2 : W - rim;
                    float len = z1 - z0, zFace = side < 0 ? z1 : z0, away = side < 0 ? -1f : 1f;
                    var c = new Vector3(x, ry, (z0 + z1) / 2);
                    string stock = FloorPlan.PickStock("ij" + tag, len);
                    float fx = x;
                    job.Add("I-joist @ " + Units.Format(x) + " (" + (side < 0 ? "front" : "back") + " bay): web stiffeners, hang in IUS hanger on the girder", ToolSets.Fasten, () =>
                    {
                        var j = Framer.Make(stock, len, root, c, Z, X, MemberRole.Joist, Connector.Nailed | Connector.Hanger, "floor");
                        HangerAndStiffeners(j, root, fx, yb, zFace, away, JD);
                    }, ghost(c, new Vector3(I(FlangeWIn), JD, len), Z)).Use(stock).Use("hanger-ij" + tag).Use("web-stiffener").Nails = 14;
                }
            }

            // squash blocks under the jack studs of every opening in the walls above (point loads to the sill)
            if (openings != null)
            {
                float blk = JD + I(0.0625f), d = fp.WallDepth > 0f ? fp.WallDepth : I(3.5f);
                float clear = I(FlangeWIn) / 2 + I(1.75f) + I(0.1f);
                for (int w = 0; w < 4 && w < openings.Length; w++)
                    foreach (var o in openings[w])
                    {
                        if (o == null || o.Product == null) continue;
                        float half = Units.In(o.RoughWidthIn / 2 + 0.75f);
                        var pts = new List<Vector3>();
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float a = Units.In(o.CenterIn) + s * half;   // along the wall
                            Vector3 p;
                            if (w == 0) p = new Vector3(AvoidJoists(a, joistX, clear), yb + blk / 2, rim + I(0.75f));
                            else if (w == 2) p = new Vector3(AvoidJoists(L - a, joistX, clear), yb + blk / 2, W - rim - I(0.75f));
                            else
                            {
                                float z = w == 1 ? d + a : W - d - a;
                                if (girder && Mathf.Abs(z - gz) < gW / 2 + I(1.85f)) z = gz + Mathf.Sign(z - gz + 1e-5f) * (gW / 2 + I(1.85f));
                                p = new Vector3(w == 1 ? L - rim - I(0.75f) : rim + I(0.75f), yb + blk / 2, z);
                            }
                            pts.Add(p);
                        }
                        Vector3 thin = (w == 0 || w == 2) ? Z : X;
                        var list = pts;
                        job.Add("Squash blocks under the jack studs of the " + o.Product.Name + " (2x4, joist depth + 1/16\")", ToolSets.Fasten, () =>
                        {
                            foreach (var p in list) Framer.Make("2x4x12", blk, root, p, U, thin, MemberRole.SquashBlock, Connector.Nailed, "floor");
                        }, ghost(pts[0], new Vector3(I(3.5f), I(1.5f), blk), U)).Use("2x4x12").Nails = 4;
                    }
            }

            var xs = new List<float> { rim / 2 };
            xs.AddRange(joistX); xs.Add(L - rim / 2);
            FloorPlan.AddSubfloor(job, root, L, W, yb + JD + I(0.71875f) / 2, xs, ghost);
            return job;
        }

        static float AvoidJoists(float x, List<float> joistX, float clear)
        {
            foreach (float jx in joistX)
                if (Mathf.Abs(x - jx) < clear) return jx + (x >= jx ? clear : -clear);
            return x;
        }

        /// <summary>Face-mount hanger on the girder face plus 23/32" OSB web stiffeners both sides of the web.
        /// Authored in floor-root axes at (x, bottom of joist, girder face); 'away' = +1/-1 direction from the girder into the joist.</summary>
        static void HangerAndStiffeners(StructuralMember j, Transform root, float x, float yb, float zFace, float away, float JD)
        {
            var go = Framer.AttachMesh(j.transform, "IJoistHanger", (MeshBuilder b, Func<SurfaceKind, int> s) =>
            {
                int g = s(SurfaceKind.Galvanized), osb = s(SurfaceKind.Osb);
                float fw = I(FlangeWIn), t = I(0.06f), len = I(2f), hh = JD - I(1.75f);
                void Bx(float x0, float x1, float y0, float y1, float z0, float z1, int sub) =>
                    b.AddBox(new Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), Quaternion.identity, sub);
                Bx(-fw / 2 - t, fw / 2 + t, -t, 0f, 0f, away * len, g);          // seat under the bottom flange
                Bx(-fw / 2 - t, -fw / 2, 0f, hh, 0f, away * len, g);             // sides
                Bx(fw / 2, fw / 2 + t, 0f, hh, 0f, away * len, g);
                Bx(-fw / 2 - I(1.4f), -fw / 2 - t, 0f, hh, 0f, away * t, g);    // face flanges nailed to the girder
                Bx(fw / 2 + t, fw / 2 + I(1.4f), 0f, hh, 0f, away * t, g);
                var face = Quaternion.FromToRotation(Vector3.up, new Vector3(0f, 0f, away));
                for (int k = 0; k < 4; k++)
                    for (int sd = -1; sd <= 1; sd += 2)
                        b.AddDisc(new Vector3(sd * (fw / 2 + I(0.75f)), hh * (0.15f + 0.23f * k), away * (t + 0.0005f)), face, I(0.15f), 8, g, true);
                // web stiffeners: tight to the bottom flange, 1/8" gap under the top flange
                float sx = I(WebIn) / 2, st = I(0.72f), y0s = I(FlangeDIn), y1s = JD - I(FlangeDIn) - I(0.125f);
                Bx(sx, sx + st, y0s, y1s, away * I(0.1f), away * I(3.1f), osb);
                Bx(-sx - st, -sx, y0s, y1s, away * I(0.1f), away * I(3.1f), osb);
            });
            go.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(x, yb, zFace)), root.rotation);
            var e = j.gameObject.AddComponent<IJoistEnd>();
            e.Hanger = true; e.Stiffened = true;
        }

        /// <summary>Inspection: I-joist spans vs. depth, hangers + web stiffeners where joists frame into a girder,
        /// squash blocks bearing tight.</summary>
        static List<InspectionIssue> Check()
        {
            var l = new List<InspectionIssue>();
            foreach (var m in SupportGraph.Members)
            {
                if (m == null || !m.isActiveAndEnabled || m.Piece == null || m.Piece.Def == null) continue;
                string id = m.Piece.Def.Id ?? "";
                if (m.Role != MemberRole.Joist || !id.StartsWith("ij")) continue;
                float dIn = Units.ToInches(m.Piece.Def.Size.x), lenIn = Units.ToInches(m.Piece.Size.z);
                if (lenIn > MaxSpanIn(dIn) + 0.5f)
                    l.Add(new InspectionIssue { Member = m, Severity = StructuralStatus.Warning, Where = m.transform.position,
                        Note = "I-joist " + Units.Format(m.Piece.Size.z) + " span is past the " + Units.Format(Units.In(MaxSpanIn(dIn))) + " limit for " + Units.Format(m.Piece.Def.Size.x) + " @ 16\" O.C. - go deeper or add a girder" });
                bool onBeam = m.SupportedBy.Exists(s => s != null && s.Role == MemberRole.Beam);
                if (!onBeam) continue;
                var e = m.GetComponent<IJoistEnd>();
                if (!m.Has(Connector.Hanger) || e == null || !e.Hanger)
                    l.Add(new InspectionIssue { Member = m, Severity = StructuralStatus.Unsupported, Where = m.transform.position, Note = "I-joist framed into the girder without a hanger" });
                else if (!e.Stiffened)
                    l.Add(new InspectionIssue { Member = m, Severity = StructuralStatus.Warning, Where = m.transform.position, Note = "I-joist: web stiffeners missing at the face-mount hanger" });
            }
            return l;
        }
    }
}
