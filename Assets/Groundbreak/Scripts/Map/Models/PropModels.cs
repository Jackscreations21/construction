using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Street furniture, utilities, vehicles and farm props for the town map, at real US dimensions.
    /// Every prop: origin at its base on the ground (y = 0), +Z = the prop's front / facing direction. Registered in
    /// <see cref="MapModels"/> by type id. Also provides <see cref="Wire"/> (sagging conductor) and <see cref="Fence"/>.
    /// </summary>
    public static class PropModels
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            MapModels.Register("powerpole", PowerPole);
            MapModels.Register("streetlight", StreetLight);
            MapModels.Register("stopsign", StopSign);
            MapModels.Register("speedsign", SpeedSign);
            MapModels.Register("streetsign", StreetSign);
            MapModels.Register("hydrant", Hydrant);
            MapModels.Register("bench", (b, s) => BenchAt(b, Vector3.zero));
            MapModels.Register("trashcan", TrashCan);
            MapModels.Register("picnictable", PicnicTable);
            MapModels.Register("playground", Playground);
            MapModels.Register("backstop", Backstop);
            MapModels.Register("bleachers", Bleachers);
            MapModels.Register("flagpole", FlagPole);
            MapModels.Register("dumpster", Dumpster);
            MapModels.Register("car", Car);
            MapModels.Register("boxtruck", BoxTruck);
            MapModels.Register("semitrailer", SemiTrailer);
            MapModels.Register("tractor", Tractor);
            MapModels.Register("haybale", HayBale);
            MapModels.Register("billboard", Billboard);
            MapModels.Register("pylonsign", PylonSign);
            MapModels.Register("busstop", BusStop);
            MapModels.Register("mailboxcluster", MailboxCluster);
            MapModels.Register("transformerbox", TransformerBox);
        }

        // ================================================================== materials

        static readonly Dictionary<(Color, float, float), Material> _gloss = new Dictionary<(Color, float, float), Material>();

        /// <summary>Plain Standard material with given smoothness / metallic (paint, plastic, chrome...). Cached.</summary>
        public static Material GlossMat(Color c, float gloss, float metal)
        {
            var key = (c, gloss, metal);
            if (_gloss.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Standard")) { name = "MapProp", color = c, hideFlags = HideFlags.DontUnloadUnusedAsset, enableInstancing = true };
            m.SetFloat("_Glossiness", gloss);
            m.SetFloat("_Metallic", metal);
            _gloss[key] = m;
            return m;
        }

        static int G(MapBuild b, Color c, float gloss = 0.4f, float metal = 0f) => b.K.M(GlossMat(c, gloss, metal));
        static int Glass(MapBuild b) => G(b, new Color(0.06f, 0.08f, 0.10f), 0.93f, 0.2f);

        static Material _chain, _bale;

        /// <summary>Galvanized 2" chain-link mesh (alpha-cutout, draw both faces). Distant mips are dithered so the
        /// fence fades to a light haze instead of vanishing or turning into a solid sheet.</summary>
        public static Material ChainLinkMaterial()
        {
            if (_chain != null) return _chain;
            const int n = 128;
            const float diag = 2.8284f;                // 2" between parallel wires → 2.83" along U / V
            const float tileIn = diag * 4f;
            float ppi = n / tileIn, half = 0.1f;       // wire Ø 0.148", drawn a bit heavier
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float X = (x + 0.5f) / ppi, Y = (y + 0.5f) / ppi;
                    float a = (X + Y) % diag, c = ((X - Y) % diag + diag) % diag;
                    float da = Mathf.Min(a, diag - a) * 0.7071f, db = Mathf.Min(c, diag - c) * 0.7071f;
                    float d = Mathf.Min(da, db);
                    float alpha = Mathf.Clamp01((half - d) * ppi + 0.5f);
                    byte l = (byte)(150 + (int)(40f * Mathf.Clamp01(1f - Mathf.Abs(da - db) * 3f)));   // brighter knuckles
                    px[y * n + x] = new Color32(l, (byte)Mathf.Min(255, l + 3), l, (byte)(alpha * 255f));
                }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, false)
            {
                name = "MapChainLink", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            t.SetPixels32(px);
            t.Apply(true, false);
            for (int mip = 1; mip < t.mipmapCount; mip++)
            {
                var p = t.GetPixels32(mip);
                for (int i = 0; i < p.Length; i++)
                {
                    float a = p[i].a / 255f;
                    float o;
                    if (mip == 1) o = Mathf.Clamp01(a * 1.5f);
                    else
                    {
                        uint h = (uint)i * 2654435761u + (uint)mip * 40503u;
                        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                        o = a * 1.2f > (h & 0xFFFF) / 65535f ? 1f : 0f;
                    }
                    p[i].a = (byte)(o * 255f);
                }
                t.SetPixels32(p, mip);
            }
            t.Apply(false, true);
            var m = new Material(Shader.Find("Standard")) { name = "MapChainLink", hideFlags = HideFlags.DontUnloadUnusedAsset, enableInstancing = true };
            m.mainTexture = t;
            m.mainTextureScale = Vector2.one / (tileIn * 0.0254f);
            m.SetFloat("_Mode", 1f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.EnableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetFloat("_Cutoff", 0.45f);
            m.SetFloat("_Glossiness", 0.45f);
            m.SetFloat("_Metallic", 0.6f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            _chain = m;
            return m;
        }

        static Material BaleMat()
        {
            if (_bale != null) return _bale;
            _bale = new Material(MapMaterials.Field(FieldKind.Wheat)) { name = "MapHayBale", hideFlags = HideFlags.DontUnloadUnusedAsset, color = new Color(0.9f, 0.84f, 0.7f) };
            _bale.enableInstancing = true;
            return _bale;
        }

        // ================================================================== geometry helpers

        static readonly int[] Sides = { -1, 1 };

        static void Box(MapBuild b, Vector3 center, Vector3 size, int sub, Vector3 uAxis = default, int faces = MeshBuilder.AllFaces)
            => b.B.AddBox(center, size, Quaternion.identity, sub, uAxis == default ? Vector3.right : uAxis, faces);

        static Quaternion Look(Vector3 d)
        {
            d.Normalize();
            return Quaternion.LookRotation(d, Mathf.Abs(d.y) > 0.98f ? Vector3.forward : Vector3.up);
        }

        /// <summary>Rectangular member from a to c: w = width across (horizontal, perpendicular), h = depth (perpendicular, up-ish).</summary>
        static void Beam(MapBuild b, Vector3 a, Vector3 c, float w, float h, int sub, int faces = MeshBuilder.AllFaces, float extend = 0f)
        {
            Vector3 d = c - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            b.B.AddBox((a + c) * 0.5f, new Vector3(w, h, len + extend * 2f), Look(d), sub, Vector3.forward, faces);
        }

        /// <summary>Round tube from a to c.</summary>
        static void Tube(MapBuild b, Vector3 a, Vector3 c, float r, int seg, int sub, bool caps = false)
        {
            Vector3 d = c - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            b.B.AddRod((a + c) * 0.5f, Look(d), r, len, seg, sub, caps);
        }

        /// <summary>Tapered round pole standing on basePos with a flat top.</summary>
        static void Pole(MapBuild b, Vector3 basePos, float r0, float r1, float h, int seg, int sub)
            => b.B.AddLathe(basePos, Quaternion.identity, new[] { new Vector2(r0, 0f), new Vector2(r1, h), new Vector2(0.001f, h) }, seg, sub);

        /// <summary>Quad in the plane z (facing +Z, or -Z when back).</summary>
        static void QuadZ(MapBuild b, float x0, float y0, float x1, float y1, float z, int sub, bool back = false)
            => b.B.AddQuad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), sub, back ? Vector3.back : Vector3.forward);

        /// <summary>Quad in the plane x (facing +X or -X).</summary>
        static void QuadX(MapBuild b, float x, float z0, float y0, float z1, float y1, int sub, bool neg)
            => b.B.AddQuad(new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, y1, z1), new Vector3(x, y1, z0), sub, neg ? Vector3.left : Vector3.right, Vector3.forward);

        /// <summary>Double-sided vertical panel from a to c (ground points), h tall.</summary>
        static void Panel2(MapBuild b, Vector3 a, Vector3 c, float h, int sub)
        {
            Vector3 up = Vector3.up * h, dir = c - a;
            Vector3 n = Vector3.Cross(Vector3.up, dir).normalized;
            if (n.sqrMagnitude < 1e-6f) return;
            b.B.AddQuad(a, c, c + up, a + up, sub, n, dir);
            b.B.AddQuad(a, c, c + up, a + up, sub, -n, dir);
        }

        static void Wheel(MapBuild b, Vector3 c, float r, float w, int tire, int rim, int seg = 12)
        {
            b.B.AddRod(c, Quaternion.Euler(0f, 90f, 0f), r, w, seg, tire, true);
            float sx = c.x < 0f ? -1f : 1f;
            b.B.AddDisc(c + new Vector3(sx * (w * 0.5f + 0.004f), 0f, 0f), Quaternion.Euler(0f, 0f, -90f), r * 0.6f, 8, rim, sx > 0f);
        }

        /// <summary>Lines of text centred on center (center.z = text base plane, see MapText), fitted into maxW x maxH.</summary>
        static void TextLines(MapBuild b, string[] lines, Vector3 center, float maxW, float maxH, float maxPx, int sub)
        {
            int n = lines.Length;
            if (n == 0) return;
            int longest = 1;
            foreach (var l in lines) longest = Mathf.Max(longest, l.Length);
            float px = Mathf.Min(maxPx, Mathf.Min(maxW / (6f * longest - 1f), maxH / (n * 7f + (n - 1) * 3f)));
            float total = n * 7f * px + (n - 1) * 3f * px;
            float top = center.y + total * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float yb = top - (i + 1) * 7f * px - i * 3f * px;
                MapText.Draw(b, lines[i], new Vector3(center.x, yb, center.z), px, sub);
            }
        }

        static string[] SplitLines(string s, string fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) s = fallback;
            var parts = s.Split('|');
            var list = new List<string>();
            foreach (var p in parts) { var t = p.Trim().ToUpperInvariant(); if (t.Length > 0) list.Add(t); }
            return list.ToArray();
        }

        /// <summary>z of the MapText base plane so letters stand 3 mm proud of a sign face at faceZ (rest hidden in the slab).</summary>
        static float TextZ(float faceZ) => faceZ + 0.003f - MapText.Depth;

        // ================================================================== wires & fences

        /// <summary>Sagging conductor from a to c: thin square tube (1.5 cm) in 8 segments, parabolic sag (catenary approximation).</summary>
        public static void Wire(MapBuild b, Vector3 a, Vector3 c, float sag, int sub)
        {
            const int seg = 8;
            const int faces = MeshBuilder.FacePosX | MeshBuilder.FaceNegX | MeshBuilder.FacePosY | MeshBuilder.FaceNegY;
            Vector3 prev = a;
            for (int i = 1; i <= seg; i++)
            {
                float t = i / (float)seg;
                Vector3 p = Vector3.Lerp(a, c, t) + Vector3.down * (sag * 4f * t * (1f - t));
                Beam(b, prev, p, 0.015f, 0.015f, sub, faces, 0.008f);
                prev = p;
            }
        }

        public enum FenceKind { Picket, Privacy, ChainLink, SplitRail, BarbedWire, Board }

        /// <summary>Fence along the straight line a→c (a.y / c.y = ground at the ends). Posts at realistic spacing
        /// (8' picket/privacy/board, 10' chain-link/split-rail, 12' barbed wire).</summary>
        public static void Fence(MapBuild b, Vector3 a, Vector3 c, FenceKind kind)
        {
            Vector3 d = c - a;
            Vector3 dh = new Vector3(d.x, 0f, d.z);
            float L = dh.magnitude;
            if (L < 0.05f) return;
            Vector3 dir = dh / L;
            Vector3 side = Vector3.Cross(Vector3.up, dir);     // perpendicular, horizontal
            Quaternion yaw = Quaternion.LookRotation(dir, Vector3.up);
            float spacing;
            switch (kind)
            {
                case FenceKind.ChainLink: case FenceKind.SplitRail: spacing = 10f * Units.Foot; break;
                case FenceKind.BarbedWire: spacing = 12f * Units.Foot; break;
                default: spacing = 8f * Units.Foot; break;
            }
            int n = Mathf.Max(1, Mathf.CeilToInt(L / spacing - 0.02f));
            Vector3 P(float t) => Vector3.Lerp(a, c, t);
            Vector3 U = Vector3.up;
            const int noBottom = MeshBuilder.AllFaces & ~MeshBuilder.FaceNegY;

            switch (kind)
            {
                case FenceKind.Picket:
                {
                    int paint = b.K.M(SurfaceKind.PaintedWhite);
                    float post = Units.In(3.5f), ph = 1.22f;
                    for (int i = 0; i <= n; i++)
                    {
                        Vector3 p = P(i / (float)n);
                        b.B.AddBox(p + U * ((ph + 0.05f) * 0.5f), new Vector3(post, ph + 0.05f, post), yaw, paint, Vector3.up, noBottom);
                    }
                    Vector3 railOff = side * (post * 0.5f + 0.02f);
                    foreach (float ry in new[] { 0.25f, 0.92f })
                        Beam(b, a + U * ry + railOff, c + U * ry + railOff, Units.In(1.5f), Units.In(2.5f), paint);
                    // 3 1/2" pointed pickets at 5 1/2" o.c. on the outside face of the rails
                    float pw = Units.In(3.5f), pt = Units.In(0.75f), oc = Units.In(5.5f), top = 1.12f, point = 0.05f;
                    Vector3 pOff = side * (post * 0.5f + 0.04f + pt * 0.5f);
                    int count = Mathf.Max(1, Mathf.FloorToInt(L / oc));
                    float start = (L - (count - 1) * oc) * 0.5f;
                    for (int k = 0; k < count; k++)
                    {
                        float t = (start + k * oc) / L;
                        Vector3 g = P(t) + pOff + U * 0.06f;
                        float hb = top - point - 0.06f;
                        b.B.AddBox(g + U * (hb * 0.5f), new Vector3(pt, hb, pw), yaw, paint, Vector3.up, noBottom);
                        Vector3 l = g + U * hb - dir * (pw * 0.5f), r = g + U * hb + dir * (pw * 0.5f), apex = g + U * (hb + point);
                        b.B.AddTriangle(l + side * (pt * 0.5f), r + side * (pt * 0.5f), apex + side * (pt * 0.5f), paint, side);
                        b.B.AddTriangle(l - side * (pt * 0.5f), r - side * (pt * 0.5f), apex - side * (pt * 0.5f), paint, -side);
                    }
                    break;
                }
                case FenceKind.Privacy:
                {
                    int wood = b.K.M(LandSurface.Cedar);
                    float post = Units.In(3.5f), h = 1.83f;
                    for (int i = 0; i <= n; i++)
                    {
                        Vector3 p = P(i / (float)n);
                        b.B.AddBox(p + U * ((h + 0.08f) * 0.5f), new Vector3(post, h + 0.08f, post), yaw, wood, Vector3.up, noBottom);
                    }
                    Vector3 railOff = -side * (post * 0.5f - 0.01f);
                    foreach (float ry in new[] { 0.3f, 0.95f, 1.6f })
                        Beam(b, a + U * ry + railOff - side * 0.02f, c + U * ry + railOff - side * 0.02f, Units.In(1.5f), Units.In(3.5f), wood);
                    // solid 1x6 board face (cedar grain runs vertically), one panel per bay so it follows the grade
                    Vector3 bOff = side * (post * 0.5f + Units.In(0.375f));
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p0 = P(i / (float)n) + bOff + U * (0.05f + h * 0.5f), p1 = P((i + 1) / (float)n) + bOff + U * (0.05f + h * 0.5f);
                        Beam(b, p0, p1, Units.In(0.75f), h - 0.05f, wood, MeshBuilder.AllFaces, post * 0.5f);
                    }
                    break;
                }
                case FenceKind.ChainLink:
                {
                    int galv = b.K.M(SurfaceKind.Galvanized), mesh = b.K.M(ChainLinkMaterial());
                    float h = 1.22f;
                    for (int i = 0; i <= n; i++)
                    {
                        Vector3 p = P(i / (float)n);
                        bool terminal = i == 0 || i == n;
                        float r = terminal ? Units.In(1.19f) : Units.In(0.95f);
                        Pole(b, p, r, r, h + (terminal ? 0.08f : 0.04f), terminal ? 8 : 6, galv);
                    }
                    Tube(b, a + U * h, c + U * h, Units.In(0.83f), 6, galv);
                    for (int i = 0; i < n; i++)
                        Panel2(b, P(i / (float)n) + U * 0.04f, P((i + 1) / (float)n) + U * 0.04f, h - 0.04f, mesh);
                    break;
                }
                case FenceKind.SplitRail:
                {
                    int wood = b.K.M(SurfaceKind.PineLumber, new Color(0.66f, 0.62f, 0.56f));
                    float h = 1.1f;
                    for (int i = 0; i <= n; i++)
                        Pole(b, P(i / (float)n), 0.09f, 0.085f, h, 6, wood);
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 p0 = P(i / (float)n), p1 = P((i + 1) / (float)n);
                        foreach (float ry in new[] { 0.42f, 0.86f })
                            Beam(b, p0 + U * ry, p1 + U * ry, 0.1f, 0.13f, wood, MeshBuilder.AllFaces, 0.12f);
                    }
                    break;
                }
                case FenceKind.BarbedWire:
                {
                    int tpost = G(b, new Color(0.14f, 0.3f, 0.17f), 0.35f, 0.3f), wood = b.K.M(SurfaceKind.TreatedLumber, new Color(0.7f, 0.62f, 0.5f));
                    int wire = G(b, new Color(0.45f, 0.45f, 0.43f), 0.4f, 0.7f);
                    for (int i = 0; i <= n; i++)
                    {
                        Vector3 p = P(i / (float)n);
                        if (i == 0 || i == n) Pole(b, p, 0.1f, 0.095f, 1.6f, 6, wood);
                        else b.B.AddBox(p + U * 0.69f, new Vector3(0.04f, 1.38f, 0.04f), yaw, tpost, Vector3.up, noBottom);
                    }
                    const int wf = MeshBuilder.FacePosX | MeshBuilder.FaceNegX | MeshBuilder.FacePosY | MeshBuilder.FaceNegY;
                    Vector3 wOff = side * 0.025f;
                    foreach (float ry in new[] { 0.35f, 0.65f, 0.95f, 1.25f })
                        Beam(b, a + U * ry + wOff, c + U * ry + wOff, 0.007f, 0.007f, wire, wf);
                    break;
                }
                default: // Board: 4-board horse fence
                {
                    int paint = b.K.M(SurfaceKind.PaintedWhite);
                    float post = Units.In(5.5f), h = 1.4f;
                    for (int i = 0; i <= n; i++)
                    {
                        Vector3 p = P(i / (float)n);
                        b.B.AddBox(p + U * ((h + 0.05f) * 0.5f), new Vector3(post, h + 0.05f, post), yaw, paint, Vector3.up, noBottom);
                    }
                    Vector3 bOff = side * (post * 0.5f + Units.In(0.375f));
                    foreach (float ry in new[] { 0.36f, 0.68f, 1.0f, 1.32f })
                        Beam(b, a + U * ry + bOff, c + U * ry + bOff, Units.In(0.75f), Units.In(5.5f), paint);
                    break;
                }
            }
        }

        // ================================================================== utilities

        /// <summary>Pole height above grade (40' class pole, ~6' buried).</summary>
        public const float PoleHeight = 12f;
        public const float CrossarmY = 11.3f;
        /// <summary>Wire attachment height on the pin insulators (crossarm along X, conductors run along Z).</summary>
        public const float ConductorY = 11.52f, ConductorZ = 0.155f;
        public static readonly float[] ConductorX = { -1.05f, -0.38f, 1.05f };
        /// <summary>Neutral spool (+X side of the pole) and the communications cable bracket.</summary>
        public static readonly Vector3 NeutralPos = new Vector3(0.24f, 10.25f, 0f), CommPos = new Vector3(0.2f, 6.2f, 0f);

        static void PowerPole(MapBuild b, MapBuildingSpec s)
        {
            int wood = b.K.M(SurfaceKind.TreatedLumber, new Color(0.6f, 0.5f, 0.42f));
            int galv = b.K.M(SurfaceKind.Galvanized);
            int ins = G(b, new Color(0.52f, 0.55f, 0.53f), 0.75f);
            int blk = G(b, new Color(0.08f, 0.08f, 0.08f), 0.3f);
            float H = PoleHeight, r0 = 0.155f, r1 = 0.105f;
            float RAt(float y) => Mathf.Lerp(r0, r1, y / H);
            Pole(b, Vector3.zero, r0, r1, H, 8, wood);

            // 8' crossarm 3 5/8" x 4 5/8" with flat braces
            float armH = Units.In(4.625f), armD = Units.In(3.625f);
            Box(b, new Vector3(0f, CrossarmY, ConductorZ), new Vector3(2.44f, armH, armD), wood, Vector3.right);
            foreach (int sx in Sides)
                Beam(b, new Vector3(sx * 0.62f, CrossarmY - armH * 0.5f, ConductorZ - 0.02f), new Vector3(sx * 0.03f, CrossarmY - 0.62f, RAt(CrossarmY - 0.62f) + 0.004f), 0.006f, 0.035f, galv);
            // pin insulators (faceted porcelain skirts)
            foreach (float x in ConductorX)
                b.B.AddLathe(new Vector3(x, CrossarmY + armH * 0.5f, ConductorZ), Quaternion.identity, new[]
                {
                    new Vector2(0.022f, 0f), new Vector2(0.06f, 0.05f), new Vector2(0.034f, 0.075f), new Vector2(0.056f, 0.12f), new Vector2(0.03f, 0.15f), new Vector2(0.001f, 0.165f)
                }, 6, ins);
            // neutral: bracket + spool insulator on the +X side
            Box(b, new Vector3(RAt(NeutralPos.y) + 0.04f, NeutralPos.y, 0f), new Vector3(0.1f, 0.2f, 0.05f), galv);
            b.B.AddLathe(NeutralPos + Vector3.down * 0.06f, Quaternion.identity, new[] { new Vector2(0.04f, 0f), new Vector2(0.03f, 0.06f), new Vector2(0.04f, 0.12f) }, 6, ins);
            // communications bracket, ground-wire moulding, pole tag
            Box(b, new Vector3(RAt(CommPos.y) + 0.03f, CommPos.y, 0f), new Vector3(0.08f, 0.06f, 0.06f), galv);
            Beam(b, new Vector3(0f, 0.25f, -(r0 + 0.004f)), new Vector3(0f, H - 0.4f, -(RAt(H - 0.4f) + 0.004f)), 0.02f, 0.008f, blk);
            Box(b, new Vector3(0f, 1.8f, RAt(1.8f) + 0.002f), new Vector3(0.06f, 0.08f, 0.004f), galv);

            if (s.Units == 2)
            {
                // pole-top distribution transformer (~25 kVA can) on the back (-Z) side, primary jumper to the middle phase
                int can = G(b, new Color(0.55f, 0.58f, 0.6f), 0.35f, 0.2f);
                float cy = 9.1f, cr = 0.27f, cz = -(RAt(cy + 0.5f) + cr + 0.05f);
                b.B.AddLathe(new Vector3(0f, cy, cz), Quaternion.identity, new[]
                {
                    new Vector2(0.001f, 0f), new Vector2(cr, 0f), new Vector2(cr, 0.92f), new Vector2(cr - 0.03f, 0.98f), new Vector2(0.001f, 1.0f)
                }, 10, can);
                Box(b, new Vector3(0f, cy + 0.55f, (cz + cr * 0.6f - RAt(cy + 0.5f)) * 0.5f), new Vector3(0.12f, 0.6f, Mathf.Abs(cz) - cr * 0.6f - RAt(cy) + 0.04f), galv);
                foreach (int sx in Sides)
                    b.B.AddLathe(new Vector3(sx * 0.1f, cy + 0.99f, cz), Quaternion.identity, new[] { new Vector2(0.03f, 0f), new Vector2(0.018f, 0.06f), new Vector2(0.026f, 0.09f), new Vector2(0.001f, 0.13f) }, 6, ins);
                Wire(b, new Vector3(-0.1f, cy + 1.12f, cz), new Vector3(ConductorX[1], ConductorY, ConductorZ), 0.06f, blk);
            }
            b.Solid(new Vector3(-r0, 0f, -r0), new Vector3(r0, H, r0));
        }

        static void StreetLight(MapBuild b, MapBuildingSpec s)
        {
            int galv = b.K.M(SurfaceKind.Galvanized), conc = b.K.M(SurfaceKind.Concrete);
            int head = G(b, new Color(0.4f, 0.42f, 0.44f), 0.45f, 0.4f);
            int lens = b.K.M(MapMaterials.Emissive(new Color(1f, 0.96f, 0.88f)));
            // exposed foundation, anchor base, tapered round pole
            b.B.AddLathe(new Vector3(0f, -0.05f, 0f), Quaternion.identity, new[] { new Vector2(0.3f, 0f), new Vector2(0.3f, 0.2f), new Vector2(0.001f, 0.2f) }, 10, conc);
            Box(b, new Vector3(0f, 0.17f, 0f), new Vector3(0.34f, 0.04f, 0.34f), galv);
            const float poleH = 8.2f;
            Pole(b, new Vector3(0f, 0.15f, 0f), 0.1f, 0.06f, poleH - 0.15f, 8, galv);
            // davit arm curving out over +Z (~2.4 m)
            var pts = new[] { new Vector3(0f, poleH - 0.05f, 0f), new Vector3(0f, 8.55f, 0.22f), new Vector3(0f, 8.8f, 0.8f), new Vector3(0f, 8.86f, 1.6f), new Vector3(0f, 8.82f, 2.35f) };
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 d = (pts[i + 1] - pts[i]).normalized;
                Tube(b, pts[i] - d * 0.03f, pts[i + 1] + d * 0.03f, 0.045f, 6, galv);
            }
            // cobra-head LED luminaire
            b.B.AddBox(new Vector3(0f, 8.8f, 2.62f), new Vector3(0.34f, 0.13f, 0.72f), Quaternion.Euler(-2f, 0f, 0f), head);
            b.B.AddBox(new Vector3(0f, 8.88f, 2.6f), new Vector3(0.28f, 0.06f, 0.56f), Quaternion.Euler(-2f, 0f, 0f), head);
            b.B.AddBox(new Vector3(0f, 8.73f, 2.66f), new Vector3(0.27f, 0.02f, 0.46f), Quaternion.Euler(-2f, 0f, 0f), lens);
            b.Solid(new Vector3(-0.15f, 0f, -0.15f), new Vector3(0.15f, poleH, 0.15f));
        }

        // ================================================================== signs

        static void SignPost(MapBuild b, float top, int galv, float z = -0.04f)
        {
            float w = Units.In(2f);
            Box(b, new Vector3(0f, top * 0.5f, z), new Vector3(w, top, w), galv, Vector3.up);
            b.Solid(new Vector3(-w * 0.5f, 0f, z - w * 0.5f), new Vector3(w * 0.5f, top, z + w * 0.5f));
        }

        static List<Vector3> Oct(float cy, float apothem, float z)
        {
            float r = apothem / Mathf.Cos(Mathf.PI / 8f);
            var p = new List<Vector3>(8);
            for (int i = 0; i < 8; i++)
            {
                float a = (22.5f + 45f * i) * Mathf.Deg2Rad;
                p.Add(new Vector3(Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r, z));
            }
            return p;
        }

        /// <summary>Flat aluminium sign: 2 cm slab (front face at z = 0, back grey), border colour, face colour inset by border.</summary>
        static void RectSign(MapBuild b, float cx, float cy, float w, float h, int face, int border, float borderW, int back)
        {
            Box(b, new Vector3(cx, cy, -0.01f), new Vector3(w, h, 0.02f), back);
            if (borderW > 0f)
            {
                QuadZ(b, cx - w * 0.5f, cy - h * 0.5f, cx + w * 0.5f, cy + h * 0.5f, 0.0008f, border);
                QuadZ(b, cx - w * 0.5f + borderW, cy - h * 0.5f + borderW, cx + w * 0.5f - borderW, cy + h * 0.5f - borderW, 0.0016f, face);
            }
            else QuadZ(b, cx - w * 0.5f, cy - h * 0.5f, cx + w * 0.5f, cy + h * 0.5f, 0.0008f, face);
        }

        static void StopSign(MapBuild b, MapBuildingSpec s)
        {
            int galv = b.K.M(SurfaceKind.Galvanized);
            int red = G(b, new Color(0.72f, 0.05f, 0.06f), 0.55f), white = G(b, new Color(0.94f, 0.94f, 0.92f), 0.55f);
            int back = G(b, new Color(0.62f, 0.64f, 0.66f), 0.45f, 0.6f);
            float bottom = 7f * Units.Foot, A = Units.In(30f), cy = bottom + A * 0.5f;
            SignPost(b, bottom + A - 0.06f, galv);
            var front = Oct(cy, A * 0.5f, 0f);
            var rear = Oct(cy, A * 0.5f, -0.02f);
            b.B.AddPolygon(front, white, Vector3.forward);
            b.B.AddPolygon(rear, back, Vector3.back);
            for (int i = 0; i < 8; i++)
            {
                int j = (i + 1) % 8;
                Vector3 mid = (front[i] + front[j]) * 0.5f;
                b.B.AddQuad(front[i], front[j], rear[j], rear[i], back, new Vector3(mid.x, mid.y - cy, 0f));
            }
            b.B.AddPolygon(Oct(cy, A * 0.5f - Units.In(0.9f), 0.0015f), red, Vector3.forward);
            const float px = 0.026f;    // 7" letters
            MapText.Draw(b, "STOP", new Vector3(0f, cy - 3.5f * px, TextZ(0.0015f)), px, white);
        }

        static void SpeedSign(MapBuild b, MapBuildingSpec s)
        {
            int galv = b.K.M(SurfaceKind.Galvanized);
            int white = G(b, new Color(0.94f, 0.94f, 0.92f), 0.5f), black = G(b, new Color(0.05f, 0.05f, 0.05f), 0.4f);
            int back = G(b, new Color(0.62f, 0.64f, 0.66f), 0.45f, 0.6f);
            float w = Units.In(24f), h = Units.In(30f), bottom = 7f * Units.Foot, cy = bottom + h * 0.5f, top = bottom + h;
            SignPost(b, top - 0.06f, galv);
            RectSign(b, 0f, cy, w, h, white, black, Units.In(0.625f), back);
            float tz = TextZ(0.0016f);
            const float spx = 0.0165f;    // ~4.5" legend
            MapText.Draw(b, "SPEED", new Vector3(0f, top - 0.06f - 7f * spx, tz), spx, black);
            MapText.Draw(b, "LIMIT", new Vector3(0f, top - 0.06f - 7f * spx - 0.035f - 7f * spx, tz), spx, black);
            string num = string.IsNullOrWhiteSpace(s.Sign) ? "35" : s.Sign.Trim();
            float npx = Mathf.Min(0.042f, (w - 0.07f) / (6f * num.Length - 1f));   // ~11" numerals
            MapText.Draw(b, num, new Vector3(0f, bottom + 0.07f, tz), npx, black);
        }

        static void StreetSign(MapBuild b, MapBuildingSpec s)
        {
            int galv = b.K.M(SurfaceKind.Galvanized);
            int green = G(b, new Color(0.0f, 0.38f, 0.2f), 0.5f), white = G(b, new Color(0.94f, 0.94f, 0.92f), 0.5f);
            string sign = s.Sign ?? "";
            var parts = sign.Split('|');
            string n1 = parts.Length > 0 ? parts[0] : "MAIN ST", n2 = parts.Length > 1 ? parts[1] : "";
            if (string.IsNullOrWhiteSpace(sign)) n1 = "MAIN ST";
            const float postTop = 2.56f;
            float w = Units.In(2f);
            Box(b, new Vector3(0f, postTop * 0.5f, 0f), new Vector3(w, postTop, w), galv, Vector3.up);
            b.Solid(new Vector3(-w * 0.5f, 0f, -w * 0.5f), new Vector3(w * 0.5f, postTop, w * 0.5f));
            float bh = Units.In(6f);
            Box(b, new Vector3(0f, postTop + 0.03f, 0f), new Vector3(0.07f, 0.06f, 0.07f), galv);                     // post-top bracket
            Box(b, new Vector3(0f, postTop + 0.06f + bh + 0.02f, 0f), new Vector3(0.06f, 0.04f, 0.06f), galv);       // cross bracket
            Blade(b, new Vector3(0f, postTop + 0.06f + bh * 0.5f, 0f), 0f, n1, green, white);
            Blade(b, new Vector3(0f, postTop + 0.06f + bh * 1.5f + 0.04f, 0f), 90f, n2, green, white);
        }

        /// <summary>Double-faced street-name blade (6" tall, white border, 4" white letters). yaw 0 = faces ±Z.</summary>
        static void Blade(MapBuild b, Vector3 center, float yaw, string name, int green, int white)
        {
            name = (name ?? "").Trim().ToUpperInvariant();
            if (name.Length == 0) return;
            float h = Units.In(6f);
            float px = Mathf.Min(0.0145f, 1.1f / Mathf.Max(1f, 6f * name.Length - 1f));
            float L = Mathf.Clamp(MapText.Width(name, px) + 0.14f, 0.6f, 1.25f);
            for (int side = 0; side < 2; side++)
            {
                b.BeginLocal(center, Quaternion.Euler(0f, yaw + side * 180f, 0f));
                if (side == 0) Box(b, Vector3.zero, new Vector3(L, h, 0.02f), white);
                QuadZ(b, -L * 0.5f + 0.012f, -h * 0.5f + 0.012f, L * 0.5f - 0.012f, h * 0.5f - 0.012f, 0.0108f, green);
                MapText.Draw(b, name, new Vector3(0f, -3.5f * px, TextZ(0.0108f)), px, white);
                b.EndLocal();
            }
        }

        static void Billboard(MapBuild b, MapBuildingSpec s)
        {
            var rng = new System.Random(s.Seed * 131 + 11);
            int steel = G(b, new Color(0.45f, 0.47f, 0.48f), 0.35f, 0.5f), galv = b.K.M(SurfaceKind.Galvanized);
            int trim = G(b, new Color(0.1f, 0.1f, 0.11f), 0.4f), dark = G(b, new Color(0.08f, 0.08f, 0.08f), 0.3f);
            Color[] bgs = { new Color(0.95f, 0.95f, 0.93f), new Color(0.1f, 0.22f, 0.5f), new Color(0.75f, 0.1f, 0.08f), new Color(0.98f, 0.8f, 0.1f), new Color(0.08f, 0.35f, 0.2f) };
            Color bgc = bgs[rng.Next(bgs.Length)];
            bool lightBg = bgc.grayscale > 0.5f;
            int bg = G(b, bgc, 0.3f);
            int accent = G(b, lightBg ? new Color(0.75f, 0.1f, 0.08f) : new Color(0.95f, 0.95f, 0.93f), 0.3f);
            int ink = G(b, lightBg ? new Color(0.08f, 0.08f, 0.1f) : new Color(0.97f, 0.97f, 0.95f), 0.3f);

            const float fw = 14.63f, fh = 4.27f, fy0 = 8.0f, fy1 = fy0 + fh, fz = 0.85f;   // 14' x 48' face, 26' to the bottom
            Pole(b, Vector3.zero, 0.45f, 0.38f, fy0 + fh * 0.6f, 10, steel);
            foreach (float y in new[] { fy0 + 0.6f, fy1 - 0.6f })
                Box(b, new Vector3(0f, y, 0.52f), new Vector3(fw - 0.4f, 0.3f, 0.26f), steel);
            Box(b, new Vector3(0f, (fy0 + fy1) * 0.5f, fz - 0.1f), new Vector3(fw, fh, 0.2f), steel);
            QuadZ(b, -fw * 0.5f, fy0, fw * 0.5f, fy1, fz + 0.0005f, trim);
            QuadZ(b, -fw * 0.5f + 0.15f, fy0 + 0.15f, fw * 0.5f - 0.15f, fy1 - 0.15f, fz + 0.001f, bg);
            QuadZ(b, -fw * 0.5f + 0.15f, fy0 + 0.15f, fw * 0.5f - 0.15f, fy0 + 0.75f, fz + 0.0015f, accent);
            var lines = SplitLines(s.Sign, "SPACE AVAILABLE|CALL 555-0142");
            TextLines(b, lines, new Vector3(0f, (fy0 + 0.75f + fy1 - 0.15f) * 0.5f, TextZ(0.0015f + fz)), fw - 1.2f, fh - 1.5f, 0.22f, ink);
            // catwalk, handrail and four up-lights
            Box(b, new Vector3(0f, fy0 - 0.15f, fz + 0.45f), new Vector3(fw, 0.05f, 0.9f), galv);
            Tube(b, new Vector3(-fw * 0.5f, fy0 + 0.85f, fz + 0.88f), new Vector3(fw * 0.5f, fy0 + 0.85f, fz + 0.88f), 0.02f, 6, galv);
            foreach (float x in new[] { -fw * 0.5f, -fw / 6f, fw / 6f, fw * 0.5f })
                Box(b, new Vector3(Mathf.Clamp(x, -fw * 0.5f + 0.03f, fw * 0.5f - 0.03f), fy0 + 0.35f, fz + 0.88f), new Vector3(0.04f, 1.0f, 0.04f), galv);
            for (int i = 0; i < 4; i++)
            {
                float x = -fw * 0.375f + i * fw * 0.25f;
                Beam(b, new Vector3(x, fy0 - 0.12f, fz + 0.1f), new Vector3(x, fy0 + 0.05f, fz + 1.3f), 0.05f, 0.05f, dark);
                b.B.AddBox(new Vector3(x, fy0 + 0.1f, fz + 1.35f), new Vector3(0.45f, 0.14f, 0.25f), Quaternion.Euler(-35f, 0f, 0f), dark);
            }
            b.Solid(new Vector3(-0.45f, 0f, -0.45f), new Vector3(0.45f, fy0, 0.45f));
        }

        static void PylonSign(MapBuild b, MapBuildingSpec s)
        {
            int baseMat = b.K.M(MapMaterials.Stucco(new Color(0.86f, 0.81f, 0.72f)));
            int frame = G(b, s.Accent, 0.45f, 0.3f);
            int face = b.K.M(MapMaterials.Emissive(new Color(0.98f, 0.97f, 0.93f)));
            Color tc = s.Color.grayscale < 0.6f ? s.Color : new Color(0.72f, 0.08f, 0.06f);
            int ink = G(b, tc, 0.5f);
            // masonry base, pole cover, internally lit cabinet 10' x 6', cap  (~6 m total)
            Box(b, new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.7f), baseMat);
            Box(b, new Vector3(0f, 0.92f, 0f), new Vector3(0.98f, 0.06f, 0.78f), frame);
            Box(b, new Vector3(0f, 2.47f, 0f), new Vector3(0.5f, 3.06f, 0.4f), frame, Vector3.up);
            const float cw = 3.05f, ch = 1.83f, cy0 = 4.0f, cd = 0.5f;
            Box(b, new Vector3(0f, cy0 + ch * 0.5f, 0f), new Vector3(cw, ch, cd), frame);
            Box(b, new Vector3(0f, cy0 + ch + 0.05f, 0f), new Vector3(cw + 0.1f, 0.1f, cd + 0.1f), frame);
            var lines = SplitLines(s.Sign, "OPEN");
            for (int side = 0; side < 2; side++)
            {
                b.BeginLocal(new Vector3(0f, cy0 + ch * 0.5f, 0f), Quaternion.Euler(0f, side * 180f, 0f));
                float fz = cd * 0.5f + 0.001f;
                QuadZ(b, -cw * 0.5f + 0.1f, -ch * 0.5f + 0.1f, cw * 0.5f - 0.1f, ch * 0.5f - 0.1f, fz, face);
                TextLines(b, lines, new Vector3(0f, 0f, TextZ(fz)), cw - 0.5f, ch - 0.45f, 0.06f, ink);
                b.EndLocal();
            }
            b.Solid(new Vector3(-0.45f, 0f, -0.35f), new Vector3(0.45f, cy0, 0.35f));
        }

        // ================================================================== street furniture

        static void Hydrant(MapBuild b, MapBuildingSpec s)
        {
            var rng = new System.Random(s.Seed * 17 + 5);
            Color bodyC = rng.Next(3) == 0 ? new Color(0.72f, 0.08f, 0.06f) : new Color(0.93f, 0.72f, 0.08f);
            Color[] bonnets = { new Color(0.15f, 0.35f, 0.75f), new Color(0.12f, 0.5f, 0.2f), new Color(0.95f, 0.45f, 0.08f), new Color(0.72f, 0.08f, 0.06f), new Color(0.85f, 0.85f, 0.85f) };
            int body = G(b, bodyC, 0.55f), cap = G(b, bonnets[rng.Next(bonnets.Length)], 0.55f);
            int nut = G(b, new Color(0.3f, 0.3f, 0.28f), 0.4f, 0.6f);
            b.B.AddLathe(Vector3.zero, Quaternion.identity, new[]
            {
                new Vector2(0.14f, 0f), new Vector2(0.14f, 0.035f), new Vector2(0.1f, 0.05f), new Vector2(0.092f, 0.08f),
                new Vector2(0.092f, 0.48f), new Vector2(0.11f, 0.5f), new Vector2(0.11f, 0.53f), new Vector2(0.001f, 0.53f)
            }, 8, body);
            b.B.AddLathe(new Vector3(0f, 0.53f, 0f), Quaternion.identity, new[] { new Vector2(0.1f, 0f), new Vector2(0.1f, 0.02f), new Vector2(0.07f, 0.1f), new Vector2(0.001f, 0.13f) }, 8, cap);
            b.B.AddLathe(new Vector3(0f, 0.65f, 0f), Quaternion.Euler(0f, 18f, 0f), new[] { new Vector2(0.024f, 0f), new Vector2(0.024f, 0.045f), new Vector2(0.001f, 0.045f) }, 5, nut);
            // two 2 1/2" hose nozzles (±X) and the 4 1/2" pumper nozzle facing +Z
            foreach (int sx in Sides)
            {
                Tube(b, new Vector3(0f, 0.38f, 0f), new Vector3(sx * 0.15f, 0.38f, 0f), 0.04f, 8, body);
                Tube(b, new Vector3(sx * 0.14f, 0.38f, 0f), new Vector3(sx * 0.18f, 0.38f, 0f), 0.048f, 8, cap, true);
            }
            Tube(b, new Vector3(0f, 0.35f, 0f), new Vector3(0f, 0.35f, 0.15f), 0.06f, 8, body);
            Tube(b, new Vector3(0f, 0.35f, 0.14f), new Vector3(0f, 0.35f, 0.18f), 0.068f, 8, cap, true);
            b.Solid(new Vector3(-0.15f, 0f, -0.15f), new Vector3(0.15f, 0.7f, 0.15f));
        }

        /// <summary>6' park bench (cast-iron ends, 4 seat + 2 back slats), seat faces +Z, at offset o.</summary>
        static void BenchAt(MapBuild b, Vector3 o)
        {
            int wood = b.K.M(SurfaceKind.Oak), iron = G(b, new Color(0.1f, 0.1f, 0.1f), 0.35f, 0.3f);
            const float L = 1.83f;
            foreach (int sx in Sides)
            {
                float x = o.x + sx * 0.78f;
                Box(b, new Vector3(x, o.y + 0.32f, o.z + 0.19f), new Vector3(0.05f, 0.64f, 0.05f), iron, Vector3.up);     // front leg + arm post
                Box(b, new Vector3(x, o.y + 0.215f, o.z - 0.2f), new Vector3(0.05f, 0.43f, 0.05f), iron, Vector3.up);     // rear leg
                Box(b, new Vector3(x, o.y + 0.41f, o.z), new Vector3(0.05f, 0.04f, 0.5f), iron, Vector3.forward);          // seat rail
                Beam(b, new Vector3(x, o.y + 0.41f, o.z - 0.22f), new Vector3(x, o.y + 0.86f, o.z - 0.31f), 0.05f, 0.04f, iron);
                Box(b, new Vector3(x, o.y + 0.655f, o.z - 0.01f), new Vector3(0.06f, 0.03f, 0.44f), iron, Vector3.forward); // armrest
            }
            foreach (float z in new[] { 0.16f, 0.055f, -0.05f, -0.155f })
                Box(b, new Vector3(o.x, o.y + 0.45f, o.z + z), new Vector3(L, 0.035f, 0.089f), wood);
            foreach (float t in new[] { 0.45f, 0.8f })
            {
                var p = Vector3.Lerp(new Vector3(0f, 0.43f, -0.22f), new Vector3(0f, 0.86f, -0.31f), t) + new Vector3(0f, 0f, 0.035f);
                b.B.AddBox(o + p, new Vector3(L, 0.089f, 0.035f), Quaternion.Euler(-12f, 0f, 0f), wood, Vector3.right);
            }
        }

        static void TrashCan(MapBuild b, MapBuildingSpec s)
        {
            Color[] cols = { new Color(0.13f, 0.27f, 0.18f), new Color(0.08f, 0.08f, 0.08f), new Color(0.3f, 0.2f, 0.12f) };
            Color c = cols[Mathf.Abs(s.Seed) % cols.Length];
            int body = b.K.M(MapMaterials.MetalPanel(c)), lid = G(b, c * 0.8f, 0.35f, 0.3f);
            b.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0.27f, 0f), new Vector2(0.29f, 0.86f) }, 12, body);
            b.B.AddLathe(Vector3.zero, Quaternion.identity, new[]
            {
                new Vector2(0.285f, 0.86f), new Vector2(0.31f, 0.86f), new Vector2(0.31f, 0.9f), new Vector2(0.18f, 0.98f), new Vector2(0.001f, 1.0f)
            }, 12, lid);
            b.Solid(new Vector3(-0.3f, 0f, -0.3f), new Vector3(0.3f, 1.0f, 0.3f));
        }

        static void PicnicTable(MapBuild b, MapBuildingSpec s)
        {
            int wood = b.K.M(SurfaceKind.PineLumber, new Color(0.78f, 0.72f, 0.64f));
            const float L = 1.83f, t = 0.038f, w6 = 0.14f;
            foreach (float z in new[] { -0.29f, -0.145f, 0f, 0.145f, 0.29f })
                Box(b, new Vector3(0f, 0.76f - t * 0.5f, z), new Vector3(L, t, w6), wood);                         // top: 5 2x6
            foreach (int sz in Sides)
                Box(b, new Vector3(0f, 0.44f, sz * 0.6f), new Vector3(L, t, 0.235f), wood);                         // 2x10 seats
            foreach (int sx in Sides)
            {
                float x = sx * 0.66f;
                foreach (int sz in Sides)
                    Beam(b, new Vector3(x, 0f, sz * 0.74f), new Vector3(x, 0.72f, sz * 0.22f), t, w6, wood);       // A-frame legs
                Box(b, new Vector3(x, 0.4f, 0f), new Vector3(t, w6 * 0.6f, 1.62f), wood, Vector3.forward);         // seat support
                Box(b, new Vector3(x, 0.7f, 0f), new Vector3(t, 0.06f, 0.72f), wood, Vector3.forward);             // top cleat
            }
        }

        static void Playground(MapBuild b, MapBuildingSpec s)
        {
            int mulch = b.K.M(LandSurface.Mulch), timber = b.K.M(SurfaceKind.TreatedLumber), galv = b.K.M(SurfaceKind.Galvanized);
            int post = G(b, new Color(0.13f, 0.4f, 0.2f), 0.45f, 0.2f), yel = G(b, new Color(0.95f, 0.74f, 0.1f), 0.55f);
            int red = G(b, new Color(0.72f, 0.12f, 0.1f), 0.5f), blue = G(b, new Color(0.1f, 0.3f, 0.68f), 0.5f);
            int deck = G(b, new Color(0.22f, 0.24f, 0.24f), 0.3f), blk = G(b, new Color(0.06f, 0.06f, 0.06f), 0.2f);
            // engineered wood-fibre surfacing inside a 6x6 timber border (10 m x 8 m)
            Box(b, new Vector3(0f, 0.025f, 0f), new Vector3(9.7f, 0.05f, 7.7f), mulch, Vector3.right, MeshBuilder.FacePosY);
            foreach (int sz in Sides) Box(b, new Vector3(0f, 0.07f, sz * 3.925f), new Vector3(10f, 0.14f, 0.15f), timber);
            foreach (int sx in Sides) Box(b, new Vector3(sx * 4.925f, 0.07f, 0f), new Vector3(0.15f, 0.14f, 7.7f), timber, Vector3.forward);

            // swing set: 8' top beam on A-frames, two belt swings
            const float sy = 2.45f, sz0 = 1.8f, x0 = -4.4f, x1 = -0.9f;
            Tube(b, new Vector3(x0 - 0.1f, sy, sz0), new Vector3(x1 + 0.1f, sy, sz0), 0.05f, 8, galv);
            foreach (float x in new[] { x0, x1 })
                foreach (int d in Sides)
                    Tube(b, new Vector3(x, sy, sz0), new Vector3(x, 0f, sz0 + d * 1.0f), 0.042f, 6, galv);
            foreach (float xs in new[] { -3.5f, -1.8f })
            {
                foreach (int d in Sides)
                    Beam(b, new Vector3(xs + d * 0.25f, sy - 0.05f, sz0), new Vector3(xs + d * 0.25f, 0.55f, sz0), 0.012f, 0.012f, galv);
                Box(b, new Vector3(xs, 0.54f, sz0), new Vector3(0.62f, 0.02f, 0.17f), blk);
            }

            // play structure: 5' deck under a hip roof, slide (+X), ladder (-X), climbing wall (-Z)
            const float dx = 2.2f, dz = -0.6f, dh = 1.5f, hs = 0.75f, roofY = 2.9f;
            foreach (int sx in Sides)
                foreach (int sz in Sides)
                    Tube(b, new Vector3(dx + sx * hs, 0f, dz + sz * hs), new Vector3(dx + sx * hs, roofY, dz + sz * hs), 0.06f, 6, post);
            Box(b, new Vector3(dx, dh, dz), new Vector3(1.5f, 0.06f, 1.5f), deck);
            Vector3 apex = new Vector3(dx, roofY + 0.6f, dz);
            var rc = new[] { new Vector3(dx - hs - 0.1f, roofY, dz - hs - 0.1f), new Vector3(dx + hs + 0.1f, roofY, dz - hs - 0.1f), new Vector3(dx + hs + 0.1f, roofY, dz + hs + 0.1f), new Vector3(dx - hs - 0.1f, roofY, dz + hs + 0.1f) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = rc[i], q = rc[(i + 1) % 4];
                Vector3 outN = ((p + q) * 0.5f - new Vector3(dx, roofY, dz)).normalized + Vector3.up;
                b.B.AddTriangle(p, q, apex, red, outN);
                b.B.AddTriangle(p, q, apex, red, -outN);
            }
            Box(b, new Vector3(dx, dh + 0.45f, dz + hs), new Vector3(1.5f, 0.8f, 0.04f), blue);
            Box(b, new Vector3(dx, dh + 0.85f, dz - hs), new Vector3(1.5f, 0.05f, 0.05f), post);
            // slide
            Vector3 s0 = new Vector3(dx + hs, dh + 0.03f, dz), s1 = new Vector3(dx + hs + 1.6f, 0.32f, dz);
            Beam(b, s0, s1, 0.55f, 0.03f, yel);
            foreach (int sd in Sides)
                Beam(b, s0 + new Vector3(0f, 0.08f, sd * 0.29f), s1 + new Vector3(0f, 0.08f, sd * 0.29f), 0.03f, 0.16f, yel);
            Box(b, new Vector3(s1.x + 0.2f, 0.3f, dz), new Vector3(0.4f, 0.03f, 0.55f), yel);
            // ladder
            foreach (int sd in Sides)
                Tube(b, new Vector3(dx - hs - 0.35f, 0f, dz + sd * 0.25f), new Vector3(dx - hs, dh, dz + sd * 0.25f), 0.025f, 6, galv);
            for (int i = 1; i <= 4; i++)
            {
                float t = i / 5f;
                Box(b, new Vector3(dx - hs - 0.35f * (1f - t), dh * t, dz), new Vector3(0.04f, 0.04f, 0.5f), galv, Vector3.forward);
            }
            // climbing wall with a few holds
            Vector3 w0 = new Vector3(dx, 0f, dz - hs - 0.55f), w1 = new Vector3(dx, dh, dz - hs);
            Beam(b, w0, w1, 1.2f, 0.05f, red);
            Vector3 wn = new Vector3(0f, 0.55f, -dh).normalized;
            for (int i = 0; i < 5; i++)
            {
                float t = 0.15f + i * 0.17f;
                Box(b, Vector3.Lerp(w0, w1, t) + new Vector3((i % 2 == 0 ? -0.3f : 0.25f), 0f, 0f) + wn * 0.04f, new Vector3(0.09f, 0.07f, 0.07f), i % 2 == 0 ? yel : blue);
            }
            // overhead ladder (monkey bars)
            const float mz = -3.0f, mx0 = -4.2f, mx1 = -1.2f, my = 2.1f;
            foreach (int sd in Sides)
            {
                Tube(b, new Vector3(mx0, my, mz + sd * 0.25f), new Vector3(mx1, my, mz + sd * 0.25f), 0.03f, 8, post);
                foreach (float x in new[] { mx0, mx1 })
                    Tube(b, new Vector3(x, 0f, mz + sd * 0.25f), new Vector3(x, my, mz + sd * 0.25f), 0.035f, 6, post);
            }
            for (int i = 1; i <= 7; i++)
                Box(b, new Vector3(Mathf.Lerp(mx0, mx1, i / 8f), my, mz), new Vector3(0.035f, 0.035f, 0.5f), galv, Vector3.forward);

            b.Solid(new Vector3(dx - hs - 0.06f, 0f, dz - hs - 0.06f), new Vector3(dx + hs + 0.06f, roofY, dz + hs + 0.06f));
            foreach (float x in new[] { x0, x1 }) b.Solid(new Vector3(x - 0.05f, 0f, sz0 - 1.0f), new Vector3(x + 0.05f, sy, sz0 + 1.0f));
        }

        static void Backstop(MapBuild b, MapBuildingSpec s)
        {
            int galv = b.K.M(SurfaceKind.Galvanized), mesh = b.K.M(ChainLinkMaterial());
            const float cw = 3.05f, ch = 6.1f, wl = 3.66f, wh = 3.66f;   // 20' x 20' centre, 12' wings at 45°, 6' hood
            Vector3 L0 = new Vector3(-cw, 0f, 0f), R0 = new Vector3(cw, 0f, 0f);
            Vector3 L1 = L0 + new Vector3(-0.7071f, 0f, 0.7071f) * wl, R1 = R0 + new Vector3(0.7071f, 0f, 0.7071f) * wl;
            Vector3 U = Vector3.up;
            foreach (var p in new[] { L0, R0, Vector3.zero }) Pole(b, p, 0.06f, 0.06f, ch + 0.05f, 8, galv);
            foreach (var p in new[] { L1, R1 }) Pole(b, p, 0.05f, 0.05f, wh + 0.05f, 8, galv);
            Tube(b, L0 + U * ch, R0 + U * ch, 0.024f, 6, galv);
            Tube(b, L0 + U * wh, R0 + U * wh, 0.024f, 6, galv);
            Tube(b, L0 + U * wh, L1 + U * wh, 0.024f, 6, galv);
            Tube(b, R0 + U * wh, R1 + U * wh, 0.024f, 6, galv);
            Panel2(b, L0 + U * 0.04f, R0 + U * 0.04f, ch - 0.04f, mesh);
            Panel2(b, L1 + U * 0.04f, L0 + U * 0.04f, wh - 0.04f, mesh);
            Panel2(b, R0 + U * 0.04f, R1 + U * 0.04f, wh - 0.04f, mesh);
            // hood (overhang toward the field)
            Vector3 h0 = new Vector3(-cw, ch, 0f), h1 = new Vector3(cw, ch, 0f), h2 = new Vector3(cw, ch + 0.5f, 1.8f), h3 = new Vector3(-cw, ch + 0.5f, 1.8f);
            Vector3 hn = Vector3.Cross(h1 - h0, h3 - h0).normalized;
            b.B.AddQuad(h0, h1, h2, h3, mesh, hn, Vector3.right);
            b.B.AddQuad(h0, h1, h2, h3, mesh, -hn, Vector3.right);
            Tube(b, h3, h2, 0.024f, 6, galv);
            foreach (float x in new[] { -cw, 0f, cw })
                Tube(b, new Vector3(x, ch, 0f), new Vector3(x, ch + 0.5f, 1.8f), 0.024f, 6, galv);
            b.Solid(new Vector3(-cw, 0f, -0.05f), new Vector3(cw, ch, 0.05f));
            for (int i = 0; i < 3; i++)
            {
                float t = (i + 0.5f) / 3f;
                foreach (var (p, q) in new[] { (L0, L1), (R0, R1) })
                {
                    Vector3 m = Vector3.Lerp(p, q, t);
                    b.Solid(m - new Vector3(0.45f, 0f, 0.45f), m + new Vector3(0.45f, wh, 0.45f));
                }
            }
        }

        static void Bleachers(MapBuild b, MapBuildingSpec s)
        {
            int rows = 3 + Mathf.Abs(s.Seed) % 3;
            int alu = G(b, new Color(0.78f, 0.79f, 0.8f), 0.55f, 0.7f), galv = b.K.M(SurfaceKind.Galvanized);
            const float L = 4.57f, rise = 0.22f, depth = 0.61f, seat0 = 0.43f, zFront = 0.9f;
            const int noBottom = MeshBuilder.AllFaces;
            float SeatY(int i) => seat0 + i * rise;
            float SeatZ(int i) => zFront - i * depth;
            for (int i = 0; i < rows; i++)
            {
                Box(b, new Vector3(0f, SeatY(i), SeatZ(i)), new Vector3(L, 0.05f, 0.25f), alu, Vector3.right, noBottom);              // 2x10 seat plank
                Box(b, new Vector3(0f, SeatY(i) - 0.32f, SeatZ(i) + 0.3f), new Vector3(L, 0.04f, 0.25f), alu, Vector3.right, noBottom); // footboard
            }
            float topY = SeatY(rows - 1), backZ = SeatZ(rows - 1) - 0.2f;
            foreach (float x in new[] { -L * 0.5f + 0.25f, 0f, L * 0.5f - 0.25f })
            {
                Beam(b, new Vector3(x, 0f, zFront + 0.45f), new Vector3(x, topY - 0.03f, backZ), 0.05f, 0.08f, galv);   // stringer
                Beam(b, new Vector3(x, 0f, backZ), new Vector3(x, topY - 0.03f, backZ), 0.05f, 0.05f, galv);               // rear leg
                Beam(b, new Vector3(x, 0f, (zFront + backZ) * 0.5f), new Vector3(x, (topY - 0.03f) * 0.5f, backZ), 0.04f, 0.04f, galv);
            }
            // guard rail at the back and both ends of the top row (seat > 30" above grade)
            float ry = topY + 0.9f, rz = backZ - 0.05f;
            foreach (float x in new[] { -L * 0.5f, 0f, L * 0.5f })
                Tube(b, new Vector3(x, topY - 0.32f, rz), new Vector3(x, ry, rz), 0.024f, 6, galv);
            Tube(b, new Vector3(-L * 0.5f, ry, rz), new Vector3(L * 0.5f, ry, rz), 0.024f, 6, galv);
            Tube(b, new Vector3(-L * 0.5f, ry - 0.45f, rz), new Vector3(L * 0.5f, ry - 0.45f, rz), 0.02f, 6, galv);
            foreach (int sx in Sides)
                Tube(b, new Vector3(sx * L * 0.5f, ry, rz), new Vector3(sx * L * 0.5f, ry, SeatZ(rows - 1) + 0.1f), 0.024f, 6, galv);
            b.Solid(new Vector3(-L * 0.5f, 0f, backZ - 0.1f), new Vector3(L * 0.5f, topY, zFront + 0.45f));
        }

        static void FlagPole(MapBuild b, MapBuildingSpec s)
        {
            int alu = G(b, new Color(0.8f, 0.81f, 0.82f), 0.6f, 0.75f), gold = G(b, new Color(0.85f, 0.68f, 0.25f), 0.75f, 0.9f);
            int conc = b.K.M(SurfaceKind.Concrete), rope = G(b, new Color(0.9f, 0.9f, 0.88f), 0.2f);
            int red = G(b, new Color(0.70f, 0.09f, 0.14f), 0.25f), white = G(b, new Color(0.95f, 0.95f, 0.93f), 0.25f), blue = G(b, new Color(0.11f, 0.14f, 0.37f), 0.25f);
            const float H = 9.14f;   // 30'
            b.B.AddLathe(new Vector3(0f, -0.05f, 0f), Quaternion.identity, new[] { new Vector2(0.35f, 0f), new Vector2(0.35f, 0.15f), new Vector2(0.001f, 0.15f) }, 12, conc);
            Pole(b, new Vector3(0f, 0.1f, 0f), 0.065f, 0.038f, H - 0.1f, 10, alu);
            b.B.AddLathe(new Vector3(0f, H, 0f), Quaternion.identity, new[] { new Vector2(0.05f, 0f), new Vector2(0.05f, 0.04f), new Vector2(0.001f, 0.04f) }, 8, alu);   // truck
            b.B.AddLathe(new Vector3(0f, H + 0.11f, 0f), Quaternion.identity, LandModel.BlobProfile(0.065f, 0.14f, 4), 8, gold);
            Beam(b, new Vector3(0.045f, 1.2f, 0f), new Vector3(0.045f, H, 0f), 0.006f, 0.006f, rope);
            // 5' x 8' US flag flying toward +X with a gentle wave
            const float FH = 1.524f, FL = 2.438f, top = H - 0.12f, xh = 0.06f;
            float[] fx = { 0f, 0.475f, 0.65f, 0.82f, 1f };
            float Wz(float f) => 0.13f * Mathf.Sin(f * Mathf.PI * 1.6f) * f;
            float sh = FH / 13f;
            for (int c = 0; c < 4; c++)
            {
                float xa = xh + fx[c] * FL, xb = xh + fx[c + 1] * FL, za = Wz(fx[c]), zb = Wz(fx[c + 1]);
                for (int j = 0; j < 13; j++)
                {
                    if (c == 0 && j < 7) continue;
                    float y1 = top - j * sh;
                    FlagQuad(b, xa, xb, za, zb, y1 - sh, y1, j % 2 == 0 ? red : white);
                }
                if (c == 0) FlagQuad(b, xa, xb, za, zb, top - 7f * sh, top, blue);
            }
            b.Solid(new Vector3(-0.08f, 0f, -0.08f), new Vector3(0.08f, H, 0.08f));
        }

        static void FlagQuad(MapBuild b, float xa, float xb, float za, float zb, float y0, float y1, int sub)
        {
            Vector3 p0 = new Vector3(xa, y0, za), p1 = new Vector3(xb, y0, zb), p2 = new Vector3(xb, y1, zb), p3 = new Vector3(xa, y1, za);
            Vector3 n = Vector3.Cross(p1 - p0, Vector3.up).normalized;
            b.B.AddQuad(p0, p1, p2, p3, sub, n, Vector3.right);
            b.B.AddQuad(p0, p1, p2, p3, sub, -n, Vector3.right);
        }

        static void Dumpster(MapBuild b, MapBuildingSpec s)
        {
            Color c = (Mathf.Abs(s.Seed) & 1) == 0 ? new Color(0.15f, 0.36f, 0.2f) : new Color(0.12f, 0.24f, 0.52f);
            int paint = G(b, c, 0.35f, 0.25f), lid = G(b, new Color(0.06f, 0.06f, 0.06f), 0.35f), dark = G(b, new Color(0.12f, 0.12f, 0.12f), 0.25f, 0.4f);
            // 6-yd front-load: 80" wide, 66" deep, 72" at the back sloping to ~49" at the front, slanted lower front
            var poly = new[] { new Vector2(0.84f, 0.12f), new Vector2(-0.45f, 0.12f), new Vector2(-0.84f, 0.5f), new Vector2(-0.84f, 1.25f), new Vector2(0.84f, 1.83f) };
            b.B.AddExtrude(poly, Vector3.zero, Quaternion.Euler(0f, 90f, 0f), 2.03f, paint, paint);   // local x → world -z
            Vector3 dir = new Vector3(0f, -0.58f, 1.68f).normalized;
            Vector3 n = Vector3.Cross(dir, Vector3.right);
            Vector3 mid = new Vector3(0f, 1.54f, 0f) + n * 0.025f;
            foreach (int sx in Sides)
                b.B.AddBox(mid + new Vector3(sx * 0.505f, 0f, 0f), new Vector3(0.98f, 0.04f, 1.8f), Quaternion.LookRotation(dir, n), lid);
            foreach (int sx in Sides)
            {
                Box(b, new Vector3(sx * 1.065f, 1.15f, -0.05f), new Vector3(0.1f, 0.15f, 1.0f), paint, Vector3.forward);   // fork pockets
                Box(b, new Vector3(sx * 0.75f, 0.06f, 0f), new Vector3(0.1f, 0.12f, 1.6f), dark, Vector3.forward);          // skids
            }
            b.Solid(new Vector3(-1.12f, 0f, -0.84f), new Vector3(1.12f, 1.85f, 0.84f));
        }

        static void HayBale(MapBuild b, MapBuildingSpec s)
        {
            int hay = b.K.M(BaleMat());
            // 5' dia x 4' wide round bale lying on its side (axis along X), slightly settled
            b.B.AddLathe(new Vector3(0f, 0.74f, 0f), Quaternion.Euler(0f, 0f, 90f), new[]
            {
                new Vector2(0.001f, -0.61f), new Vector2(0.66f, -0.61f), new Vector2(0.75f, -0.52f), new Vector2(0.762f, 0f),
                new Vector2(0.75f, 0.52f), new Vector2(0.66f, 0.61f), new Vector2(0.001f, 0.61f)
            }, 12, hay);
            b.Solid(new Vector3(-0.61f, 0f, -0.75f), new Vector3(0.61f, 1.5f, 0.75f));
        }

        static void BusStop(MapBuild b, MapBuildingSpec s)
        {
            int conc = b.K.M(SurfaceKind.Concrete), galv = b.K.M(SurfaceKind.Galvanized);
            int blue = G(b, new Color(0.08f, 0.24f, 0.58f), 0.5f), white = G(b, new Color(0.94f, 0.94f, 0.92f), 0.5f);
            Box(b, new Vector3(0f, 0.05f, 0.1f), new Vector3(2.6f, 0.1f, 1.5f), conc);
            BenchAt(b, new Vector3(0f, 0.1f, -0.15f));
            // 12" x 18" BUS STOP sign on a 2" post, facing up/down the street (±X)
            Vector3 pp = new Vector3(1.15f, 0f, 0.55f);
            float w = Units.In(2f);
            Box(b, pp + new Vector3(0f, 1.15f, 0f), new Vector3(w, 2.3f, w), galv, Vector3.up);
            b.Solid(pp + new Vector3(-w * 0.5f, 0f, -w * 0.5f), pp + new Vector3(w * 0.5f, 2.3f, w * 0.5f));
            float sw = Units.In(12f), shh = Units.In(18f), cy = 2.3f + shh * 0.5f - 0.04f;
            for (int side = 0; side < 2; side++)
            {
                b.BeginLocal(pp + new Vector3(0f, cy, 0f), Quaternion.Euler(0f, side == 0 ? 90f : -90f, 0f));
                if (side == 0) Box(b, Vector3.zero, new Vector3(sw, shh, 0.02f), white);
                QuadZ(b, -sw * 0.5f + 0.012f, -shh * 0.5f + 0.012f, sw * 0.5f - 0.012f, shh * 0.5f - 0.012f, 0.0108f, blue);
                float tz = TextZ(0.0108f);
                MapText.Draw(b, "BUS", new Vector3(0f, 0.03f, tz), 0.016f, white);
                MapText.Draw(b, "STOP", new Vector3(0f, -0.08f, tz), 0.0105f, white);
                // simple bus pictogram block
                QuadZ(b, -0.07f, -0.19f, 0.07f, -0.115f, 0.0116f, white);
                b.EndLocal();
            }
        }

        static void MailboxCluster(MapBuild b, MapBuildingSpec s)
        {
            Color[] cols = { new Color(0.76f, 0.7f, 0.58f), new Color(0.5f, 0.52f, 0.54f), new Color(0.3f, 0.24f, 0.18f) };
            Color c = cols[Mathf.Abs(s.Seed) % cols.Length];
            int body = G(b, c, 0.4f, 0.3f), line = G(b, c * 0.55f, 0.3f), conc = b.K.M(SurfaceKind.Concrete), lockM = G(b, new Color(0.7f, 0.7f, 0.68f), 0.7f, 0.9f);
            // USPS-style CBU (16 tenant doors + 2 parcel lockers), ~62" tall on its pedestal
            Box(b, new Vector3(0f, 0.04f, 0f), new Vector3(1.0f, 0.08f, 0.7f), conc);
            Box(b, new Vector3(0f, 0.08f + 0.01f, 0f), new Vector3(0.4f, 0.02f, 0.4f), body);
            Box(b, new Vector3(0f, 0.23f, 0f), new Vector3(0.22f, 0.3f, 0.22f), body, Vector3.up);
            const float x0 = -0.39f, x1 = 0.39f, y0 = 0.38f, y1 = 1.52f, z1 = 0.215f;
            Box(b, new Vector3(0f, (y0 + y1) * 0.5f, 0f), new Vector3(x1 - x0, y1 - y0, z1 * 2f), body);
            Box(b, new Vector3(0f, y1 + 0.04f, -0.01f), new Vector3(0.84f, 0.08f, 0.47f), body);
            const int fr = MeshBuilder.AllFaces & ~MeshBuilder.FaceNegZ;
            float zf = z1 + 0.003f;
            foreach (float x in new[] { -0.195f, 0f, 0.195f })
                Box(b, new Vector3(x, 1.11f, zf), new Vector3(0.008f, 0.74f, 0.006f), line, Vector3.up, fr);
            Box(b, new Vector3(0f, 0.56f, zf), new Vector3(0.008f, 0.34f, 0.006f), line, Vector3.up, fr);
            foreach (float y in new[] { 0.74f, 0.925f, 1.11f, 1.295f, 1.48f })
                Box(b, new Vector3(0f, y, zf), new Vector3(0.74f, 0.008f, 0.006f), line, Vector3.right, fr);
            foreach (float x in new[] { -0.1f, 0.1f })
                Box(b, new Vector3(x, 0.62f, zf), new Vector3(0.025f, 0.025f, 0.01f), lockM, Vector3.right, fr);
            b.Solid(new Vector3(-0.42f, 0f, -0.25f), new Vector3(0.42f, 1.6f, 0.25f));
        }

        static void TransformerBox(MapBuild b, MapBuildingSpec s)
        {
            int conc = b.K.M(SurfaceKind.Concrete), green = G(b, new Color(0.22f, 0.33f, 0.24f), 0.35f, 0.2f);
            int seam = G(b, new Color(0.12f, 0.18f, 0.13f), 0.3f), label = G(b, new Color(0.95f, 0.8f, 0.1f), 0.4f), lockM = G(b, new Color(0.6f, 0.6f, 0.58f), 0.6f, 0.8f);
            Box(b, new Vector3(0f, 0.05f, 0f), new Vector3(1.6f, 0.1f, 1.5f), conc);
            Box(b, new Vector3(0f, 0.625f, 0f), new Vector3(1.2f, 1.05f, 1.1f), green);
            b.B.AddBox(new Vector3(0f, 1.17f, 0f), new Vector3(1.24f, 0.04f, 1.14f), Quaternion.Euler(2f, 0f, 0f), green);
            const int fr = MeshBuilder.AllFaces & ~MeshBuilder.FaceNegZ;
            Box(b, new Vector3(0f, 0.62f, 0.553f), new Vector3(0.01f, 1.0f, 0.006f), seam, Vector3.up, fr);
            Box(b, new Vector3(0.12f, 0.75f, 0.556f), new Vector3(0.05f, 0.09f, 0.012f), lockM, Vector3.right, fr);
            Box(b, new Vector3(-0.3f, 0.95f, 0.553f), new Vector3(0.14f, 0.09f, 0.004f), label, Vector3.right, fr);
            b.Solid(new Vector3(-0.6f, 0f, -0.55f), new Vector3(0.6f, 1.2f, 0.55f));
        }

        // ================================================================== vehicles

        sealed class CarShape
        {
            public float L, W, H, Wr, Tw, Bb, Zr, Zf;
            public Vector2[] Top;                     // body top (z, y), ascending z, ends at ±L/2
            public float Cz0, Cz1, Cy, Rz0, Rz1, Ry;  // greenhouse: base z range at Cy, roof z range at Ry
            public float Inset;                       // tumblehome (roof narrower than the belt line, per side)
            public float CPillar = 0.1f;
            public bool Bed; public float BedFront;
            public bool DarkTrim;
        }

        static CarShape Sedan() => new CarShape
        {
            L = 4.85f, W = 1.84f, H = 1.44f, Wr = 0.33f, Tw = 0.22f, Bb = 0.24f, Zr = -1.35f, Zf = 1.45f,
            Top = new[] { new Vector2(-2.425f, 0.86f), new Vector2(-2.3f, 0.96f), new Vector2(-1.55f, 0.98f), new Vector2(0.85f, 0.94f), new Vector2(2.15f, 0.8f), new Vector2(2.425f, 0.64f) },
            Cz0 = -1.55f, Cz1 = 0.85f, Cy = 0.93f, Rz0 = -0.9f, Rz1 = 0.08f, Ry = 1.44f, Inset = 0.16f, CPillar = 0.12f,
        };
        static CarShape Suv() => new CarShape
        {
            L = 4.85f, W = 1.92f, H = 1.78f, Wr = 0.37f, Tw = 0.26f, Bb = 0.38f, Zr = -1.42f, Zf = 1.43f,
            Top = new[] { new Vector2(-2.425f, 0.98f), new Vector2(-2.38f, 1.06f), new Vector2(1.0f, 1.06f), new Vector2(2.2f, 0.98f), new Vector2(2.425f, 0.8f) },
            Cz0 = -2.32f, Cz1 = 1.0f, Cy = 1.03f, Rz0 = -2.22f, Rz1 = 0.15f, Ry = 1.78f, Inset = 0.13f, CPillar = 0.14f, DarkTrim = true,
        };
        static CarShape Pickup() => new CarShape
        {
            L = 5.6f, W = 2.0f, H = 1.92f, Wr = 0.4f, Tw = 0.27f, Bb = 0.45f, Zr = -1.75f, Zf = 1.92f,
            Top = new[] { new Vector2(-2.8f, 0.96f), new Vector2(-0.5f, 0.96f), new Vector2(-0.48f, 1.2f), new Vector2(1.05f, 1.22f), new Vector2(2.55f, 1.14f), new Vector2(2.8f, 0.98f) },
            Cz0 = -0.48f, Cz1 = 1.05f, Cy = 1.19f, Rz0 = -0.44f, Rz1 = 0.4f, Ry = 1.92f, Inset = 0.13f, CPillar = 0.1f,
            Bed = true, BedFront = -0.49f, DarkTrim = true,
        };
        static CarShape Minivan() => new CarShape
        {
            L = 5.1f, W = 1.99f, H = 1.75f, Wr = 0.35f, Tw = 0.24f, Bb = 0.33f, Zr = -1.5f, Zf = 1.5f,
            Top = new[] { new Vector2(-2.55f, 0.92f), new Vector2(-2.5f, 1.0f), new Vector2(1.25f, 1.0f), new Vector2(2.3f, 0.9f), new Vector2(2.55f, 0.68f) },
            Cz0 = -2.46f, Cz1 = 1.25f, Cy = 0.97f, Rz0 = -2.38f, Rz1 = 0.12f, Ry = 1.75f, Inset = 0.14f, CPillar = 0.13f,
        };

        static float Interp(Vector2[] k, float z)
        {
            if (z <= k[0].x) return k[0].y;
            for (int i = 0; i < k.Length - 1; i++)
                if (z <= k[i + 1].x)
                {
                    float d = k[i + 1].x - k[i].x;
                    return d < 1e-5f ? k[i + 1].y : Mathf.Lerp(k[i].y, k[i + 1].y, (z - k[i].x) / d);
                }
            return k[k.Length - 1].y;
        }

        static Color PickCarColor(System.Random rng)
        {
            int r = rng.Next(100);
            if (r < 24) return new Color(0.9f, 0.9f, 0.9f);       // white
            if (r < 43) return new Color(0.035f, 0.035f, 0.04f);  // black
            if (r < 58) return new Color(0.6f, 0.61f, 0.63f);     // silver
            if (r < 72) return new Color(0.3f, 0.31f, 0.33f);     // grey
            if (r < 81) return new Color(0.55f, 0.04f, 0.05f);    // red
            if (r < 89) return new Color(0.07f, 0.15f, 0.36f);    // blue
            if (r < 93) return new Color(0.07f, 0.2f, 0.13f);     // dark green
            if (r < 97) return new Color(0.62f, 0.56f, 0.46f);    // beige
            return new Color(0.32f, 0.05f, 0.07f);                 // maroon
        }

        static void Car(MapBuild b, MapBuildingSpec s)
        {
            var rng = new System.Random(s.Seed * 9781 + 3);
            int t = rng.Next(100);
            CarShape cs = t < 38 ? Sedan() : t < 68 ? Suv() : t < 88 ? Pickup() : Minivan();
            DrawCar(b, cs, PickCarColor(rng));
        }

        /// <summary>Passenger vehicle along Z (front +Z): body with round wheel arches, greenhouse with pillars, lights,
        /// bumpers, plates, mirrors, 4 wheels. ~650-800 triangles.</summary>
        static void DrawCar(MapBuild b, CarShape cs, Color color)
        {
            int paint = b.K.M(GlossMat(color, 0.82f, 0.35f)), glass = Glass(b);
            int under = G(b, new Color(0.045f, 0.045f, 0.045f), 0.1f);
            int trim = cs.DarkTrim ? G(b, new Color(0.1f, 0.1f, 0.1f), 0.3f) : paint;
            int tire = b.K.M(SurfaceKind.Tire), rim = G(b, new Color(0.72f, 0.73f, 0.75f), 0.7f, 0.85f);
            int head = G(b, new Color(0.92f, 0.93f, 0.9f), 0.92f, 0.3f), tail = G(b, new Color(0.6f, 0.04f, 0.04f), 0.85f);
            int plate = G(b, new Color(0.9f, 0.9f, 0.86f), 0.5f), grille = G(b, new Color(0.06f, 0.06f, 0.06f), 0.4f, 0.3f);
            float L = cs.L, half = L * 0.5f, hw = cs.W * 0.5f;
            float R = cs.Wr + 0.05f;
            float[] wheelsZ = { cs.Zr, cs.Zf };

            // ---- body sampled along z (round arches cut out of the lower body)
            var zs = new List<float> { -half, half };
            foreach (var k in cs.Top) zs.Add(Mathf.Clamp(k.x, -half, half));
            foreach (float zw in wheelsZ)
            {
                zs.Add(zw - R - 0.003f); zs.Add(zw + R + 0.003f);
                for (int j = 0; j <= 6; j++) zs.Add(zw + R * Mathf.Cos(Mathf.PI * j / 6f));
            }
            zs.Sort();
            var zl = new List<float>();
            foreach (float z in zs) if (zl.Count == 0 || z - zl[zl.Count - 1] > 1e-4f) zl.Add(Mathf.Clamp(z, -half, half));

            float Bot(float z)
            {
                float y = cs.Bb;
                foreach (float zw in wheelsZ)
                {
                    float dz = Mathf.Abs(z - zw);
                    if (dz <= R + 1e-4f) y = Mathf.Max(y, cs.Wr + Mathf.Sqrt(Mathf.Max(0f, R * R - dz * dz)));
                }
                return y;
            }
            float TopY(float z) => Mathf.Max(Interp(cs.Top, z), Bot(z) + 0.03f);

            for (int i = 0; i < zl.Count - 1; i++)
            {
                float z0 = zl[i], z1 = zl[i + 1];
                float b0 = Bot(z0), b1 = Bot(z1), t0 = TopY(z0), t1 = TopY(z1);
                foreach (int sx in Sides)
                {
                    float x = sx * hw;
                    b.B.AddQuad(new Vector3(x, b0, z0), new Vector3(x, b1, z1), new Vector3(x, t1, z1), new Vector3(x, t0, z0), paint, new Vector3(sx, 0f, 0f), Vector3.forward);
                }
                bool bed = cs.Bed && (z0 + z1) * 0.5f < cs.BedFront;
                b.B.AddQuad(new Vector3(-hw, t0, z0), new Vector3(hw, t0, z0), new Vector3(hw, t1, z1), new Vector3(-hw, t1, z1), bed ? under : paint, Vector3.up);
                b.B.AddQuad(new Vector3(-hw, b0, z0), new Vector3(hw, b0, z0), new Vector3(hw, b1, z1), new Vector3(-hw, b1, z1), under, Vector3.down);
            }
            float tf = TopY(half), tr = TopY(-half), bf = Bot(half), br = Bot(-half);
            QuadZ(b, -hw, bf, hw, tf, half, paint);
            QuadZ(b, -hw, br, hw, tr, -half, paint, true);

            // ---- greenhouse
            float hw0 = hw - 0.03f, hw1 = hw - cs.Inset;
            Vector3 BR(float sx) => new Vector3(sx * hw0, cs.Cy, cs.Cz0);
            Vector3 BF(float sx) => new Vector3(sx * hw0, cs.Cy, cs.Cz1);
            Vector3 TR(float sx) => new Vector3(sx * hw1, cs.Ry, cs.Rz0);
            Vector3 TF(float sx) => new Vector3(sx * hw1, cs.Ry, cs.Rz1);
            b.B.AddQuad(BF(-1), BF(1), TF(1), TF(-1), glass, new Vector3(0f, 0.5f, 1f));
            b.B.AddQuad(BR(-1), BR(1), TR(1), TR(-1), glass, new Vector3(0f, 0.5f, -1f));
            foreach (int sx in Sides) b.B.AddQuad(BR(sx), BF(sx), TF(sx), TR(sx), glass, new Vector3(sx, 0.3f, 0f));
            b.B.AddQuad(TR(-1), TR(1), TF(1), TF(-1), paint, Vector3.up);
            foreach (int sx in Sides)
            {
                Beam(b, BF(sx), TF(sx), 0.07f, 0.06f, paint);                                   // A-pillar
                Beam(b, BR(sx), TR(sx), cs.CPillar, 0.07f, paint);                              // C/D-pillar
                float zb = Mathf.Clamp((cs.Cz0 + cs.Cz1) * 0.5f + 0.1f, cs.Rz0 + 0.12f, cs.Rz1 - 0.12f);
                Beam(b, new Vector3(sx * (hw0 + 0.005f), cs.Cy, zb), new Vector3(sx * (hw1 + 0.005f), cs.Ry, zb), 0.06f, 0.07f, trim);   // B-pillar
                Box(b, new Vector3(sx * (hw0 + 0.09f), cs.Cy + 0.1f, cs.Cz1 - 0.15f), new Vector3(0.13f, 0.1f, 0.07f), trim);     // mirror
            }

            // ---- pickup bed walls + tailgate
            if (cs.Bed)
            {
                float by0 = Interp(cs.Top, -half + 0.1f), by1 = cs.Top[2].y + 0.04f;
                foreach (int sx in Sides)
                    Box(b, new Vector3(sx * (hw - 0.025f), (by0 + by1) * 0.5f, (-half + 0.05f + cs.BedFront) * 0.5f), new Vector3(0.05f, by1 - by0, cs.BedFront + half - 0.05f), paint, Vector3.forward);
                Box(b, new Vector3(0f, (by0 + by1) * 0.5f, -half + 0.025f), new Vector3(cs.W, by1 - by0, 0.05f), paint);
                foreach (int sx in Sides)
                    Box(b, new Vector3(sx * (hw - 0.06f), by0 + 0.1f, -half - 0.005f), new Vector3(0.1f, 0.3f, 0.03f), tail);
            }
            else
                foreach (int sx in Sides)
                    Box(b, new Vector3(sx * (hw - 0.2f), tr - 0.12f, -half), new Vector3(0.3f, 0.12f, 0.04f), tail);

            // ---- front: headlights, grille; bumpers and plates
            foreach (int sx in Sides)
                Box(b, new Vector3(sx * (hw - 0.22f), tf - 0.1f, half), new Vector3(0.32f, 0.11f, 0.04f), head);
            Box(b, new Vector3(0f, tf - 0.1f, half + 0.005f), new Vector3(cs.W - 0.95f, 0.13f, 0.03f), grille);
            int bump = cs.DarkTrim ? trim : paint;
            Box(b, new Vector3(0f, cs.Bb + 0.09f, half), new Vector3(cs.W + 0.02f, 0.18f, 0.12f), bump);
            Box(b, new Vector3(0f, cs.Bb + 0.09f, -half), new Vector3(cs.W + 0.02f, 0.18f, 0.12f), bump);
            Box(b, new Vector3(0f, cs.Bb + 0.09f, half + 0.066f), new Vector3(0.3f, 0.15f, 0.012f), plate);
            Box(b, new Vector3(0f, cs.Bb + 0.09f, -half - 0.066f), new Vector3(0.3f, 0.15f, 0.012f), plate);

            // ---- wheels
            foreach (float zw in wheelsZ)
                foreach (int sx in Sides)
                    Wheel(b, new Vector3(sx * (hw - cs.Tw * 0.5f - 0.015f), cs.Wr, zw), cs.Wr, cs.Tw, tire, rim);

            b.Solid(new Vector3(-hw, 0f, -half), new Vector3(hw, cs.H, half));
        }

        static void BoxTruck(MapBuild b, MapBuildingSpec s)
        {
            var rng = new System.Random(s.Seed * 31 + 7);
            int white = G(b, new Color(0.93f, 0.93f, 0.92f), 0.45f);
            Color[] cabs = { new Color(0.93f, 0.93f, 0.92f), new Color(0.93f, 0.93f, 0.92f), new Color(0.6f, 0.06f, 0.05f), new Color(0.1f, 0.2f, 0.45f), new Color(0.95f, 0.55f, 0.08f) };
            int cab = b.K.M(GlossMat(cabs[rng.Next(cabs.Length)], 0.8f, 0.3f));
            int alu = G(b, new Color(0.75f, 0.76f, 0.78f), 0.6f, 0.8f), dark = G(b, new Color(0.07f, 0.07f, 0.07f), 0.2f);
            int glass = Glass(b), tire = b.K.M(SurfaceKind.Tire), rim = G(b, new Color(0.72f, 0.73f, 0.75f), 0.6f, 0.8f);
            int head = G(b, new Color(0.92f, 0.93f, 0.9f), 0.92f, 0.3f), tail = G(b, new Color(0.6f, 0.04f, 0.04f), 0.85f);
            int ink = G(b, new Color(0.08f, 0.18f, 0.45f), 0.4f);

            // 26' box truck: 24' box (102" wide), conventional cab; front +Z
            const float bx = 1.295f, bz0 = -5.0f, bz1 = 2.55f, by0 = 1.15f, by1 = 3.75f;
            float bl = bz1 - bz0, bm = (bz0 + bz1) * 0.5f;
            Box(b, new Vector3(0f, (by0 + by1) * 0.5f, bm), new Vector3(bx * 2f, by1 - by0, bl), white, Vector3.forward);
            foreach (int sx in Sides)
            {
                Box(b, new Vector3(sx * (bx + 0.01f), by1 - 0.04f, bm), new Vector3(0.04f, 0.08f, bl), alu, Vector3.forward);
                Box(b, new Vector3(sx * (bx + 0.01f), by0 + 0.06f, bm), new Vector3(0.04f, 0.12f, bl), alu, Vector3.forward);
                Box(b, new Vector3(sx * (bx - 0.04f), (by0 + by1) * 0.5f, bz0 - 0.01f), new Vector3(0.1f, by1 - by0, 0.04f), alu, Vector3.up);
                Box(b, new Vector3(sx * 0.45f, 0.82f, -1.1f), new Vector3(0.08f, 0.25f, 8.0f), dark, Vector3.forward);    // frame rails
                Box(b, new Vector3(sx * 1.15f, 1.05f, bz0 - 0.02f), new Vector3(0.12f, 0.2f, 0.03f), tail);
            }
            Box(b, new Vector3(0f, (by0 + by1) * 0.5f - 0.05f, bz0 - 0.008f), new Vector3(bx * 2f - 0.2f, by1 - by0 - 0.25f, 0.012f), alu);   // roll-up door
            Box(b, new Vector3(0f, by0 + 0.3f, bz0 - 0.02f), new Vector3(0.5f, 0.04f, 0.03f), dark);
            Box(b, new Vector3(0f, 0.9f, bz0 + 0.05f), new Vector3(2.3f, 0.14f, 0.15f), dark);                                         // rear bumper

            // cab + hood
            Box(b, new Vector3(0f, 1.75f, 3.2f), new Vector3(2.2f, 1.9f, 1.3f), cab);
            Box(b, new Vector3(0f, 1.2f, 4.42f), new Vector3(1.4f, 0.9f, 1.15f), cab, Vector3.forward);
            foreach (int sx in Sides)
            {
                Box(b, new Vector3(sx * 0.95f, 1.07f, 4.2f), new Vector3(0.5f, 0.1f, 1.25f), cab, Vector3.forward);   // fenders
                QuadX(b, sx * 1.101f, 2.75f, 1.8f, 3.75f, 2.55f, glass, sx < 0);
                Box(b, new Vector3(sx * 1.25f, 2.2f, 3.8f), new Vector3(0.05f, 0.35f, 0.12f), dark);
                Box(b, new Vector3(sx * 0.55f, 1.25f, 5.0f), new Vector3(0.22f, 0.16f, 0.03f), head);
            }
            QuadZ(b, -1.0f, 1.8f, 1.0f, 2.6f, 3.851f, glass);
            Box(b, new Vector3(0f, 1.2f, 5.0f), new Vector3(0.75f, 0.6f, 0.03f), dark);
            Box(b, new Vector3(0f, 0.75f, 5.06f), new Vector3(2.2f, 0.22f, 0.16f), alu);
            b.B.AddRod(new Vector3(-0.85f, 0.6f, 1.9f), Quaternion.identity, 0.25f, 0.9f, 10, alu, true);                              // fuel tank

            Wheel(b, new Vector3(-0.98f, 0.5f, 4.15f), 0.5f, 0.28f, tire, rim);
            Wheel(b, new Vector3(0.98f, 0.5f, 4.15f), 0.5f, 0.28f, tire, rim);
            Wheel(b, new Vector3(-0.92f, 0.5f, -2.6f), 0.5f, 0.56f, tire, rim);
            Wheel(b, new Vector3(0.92f, 0.5f, -2.6f), 0.5f, 0.56f, tire, rim);

            if (!string.IsNullOrWhiteSpace(s.Sign))
            {
                var lines = SplitLines(s.Sign, "");
                foreach (int sx in Sides)
                {
                    b.BeginLocal(new Vector3(sx * bx, (by0 + by1) * 0.5f + 0.2f, bm), Quaternion.Euler(0f, sx * 90f, 0f));
                    TextLines(b, lines, new Vector3(0f, 0f, TextZ(0f)), bl - 1.2f, 1.6f, 0.12f, ink);
                    b.EndLocal();
                }
            }
            b.Solid(new Vector3(-bx, 0f, bz0), new Vector3(bx, by1, 5.1f));
        }

        static void SemiTrailer(MapBuild b, MapBuildingSpec s)
        {
            int white = G(b, new Color(0.92f, 0.92f, 0.91f), 0.45f), alu = G(b, new Color(0.75f, 0.76f, 0.78f), 0.6f, 0.8f);
            int dark = G(b, new Color(0.07f, 0.07f, 0.07f), 0.2f), tire = b.K.M(SurfaceKind.Tire), rim = G(b, new Color(0.72f, 0.73f, 0.75f), 0.6f, 0.8f);
            int red = G(b, new Color(0.75f, 0.05f, 0.05f), 0.8f), refl = G(b, new Color(0.95f, 0.95f, 0.95f), 0.8f);
            // 53' x 102" dry van, 13'6" tall, parked on its landing gear; kingpin end = +Z
            const float L = 16.15f, h = L * 0.5f, W = 2.59f, hw = W * 0.5f, y0 = 1.22f, y1 = 4.11f;
            float ym = (y0 + y1) * 0.5f;
            Box(b, new Vector3(0f, ym, 0f), new Vector3(W, y1 - y0, L), white, Vector3.forward);
            foreach (int sx in Sides)
            {
                Box(b, new Vector3(sx * (hw + 0.012f), y1 - 0.05f, 0f), new Vector3(0.03f, 0.1f, L), alu, Vector3.forward);
                Box(b, new Vector3(sx * (hw + 0.015f), y0 + 0.09f, 0f), new Vector3(0.04f, 0.18f, L), alu, Vector3.forward);
                Box(b, new Vector3(sx * (hw - 0.05f), ym, -h - 0.015f), new Vector3(0.1f, y1 - y0, 0.04f), alu, Vector3.up);
                // DOT conspicuity tape (alternating red / white) along the bottom rail
                for (int k = 0; k < 10; k++)
                {
                    float z0 = -h + 0.5f + k * (L - 1.0f) / 10f, z1 = z0 + 0.6f;
                    QuadX(b, sx * (hw + 0.036f), z0, y0 + 0.05f, z1, y0 + 0.12f, (k & 1) == 0 ? red : refl, sx < 0);
                }
            }
            Box(b, new Vector3(0f, y1 - 0.1f, -h - 0.015f), new Vector3(W, 0.2f, 0.04f), alu);
            Box(b, new Vector3(0f, y0 + 0.08f, -h - 0.015f), new Vector3(W, 0.16f, 0.04f), alu);
            Box(b, new Vector3(0f, ym, -h - 0.02f), new Vector3(0.02f, y1 - y0 - 0.3f, 0.02f), dark);
            foreach (float x in new[] { -0.95f, -0.35f, 0.35f, 0.95f })
                Box(b, new Vector3(x, ym, -h - 0.04f), new Vector3(0.03f, y1 - y0 - 0.4f, 0.03f), alu, Vector3.up);       // lock rods
            // rear underride guard, sliding tandem, mud flaps
            Box(b, new Vector3(0f, 0.5f, -h + 0.25f), new Vector3(2.4f, 0.12f, 0.12f), dark);
            foreach (int sx in Sides) Box(b, new Vector3(sx * 0.7f, 0.85f, -h + 0.3f), new Vector3(0.08f, 0.6f, 0.08f), dark);
            Box(b, new Vector3(0f, 1.0f, -h + 2.0f), new Vector3(1.0f, 0.4f, 2.8f), dark);
            foreach (float az in new[] { -h + 1.35f, -h + 2.6f })
                foreach (int sx in Sides)
                    Wheel(b, new Vector3(sx * (hw - 0.31f), 0.5f, az), 0.5f, 0.58f, tire, rim);
            foreach (int sx in Sides) Box(b, new Vector3(sx * (hw - 0.31f), 0.55f, -h + 0.7f), new Vector3(0.6f, 0.6f, 0.01f), dark);
            // landing gear (sand shoes on the ground), upper coupler plate
            float lz = h - 3.6f;
            foreach (int sx in Sides)
            {
                Box(b, new Vector3(sx * 0.9f, (0.08f + y0) * 0.5f, lz), new Vector3(0.12f, y0 - 0.08f, 0.12f), dark, Vector3.up);
                Box(b, new Vector3(sx * 0.9f, 0.04f, lz), new Vector3(0.3f, 0.08f, 0.3f), dark);
            }
            Box(b, new Vector3(0f, 0.75f, lz), new Vector3(1.8f, 0.06f, 0.06f), dark);
            Box(b, new Vector3(1.05f, 0.85f, lz), new Vector3(0.25f, 0.04f, 0.04f), dark);
            Box(b, new Vector3(0f, y0 - 0.05f, h - 1.4f), new Vector3(1.8f, 0.1f, 2.6f), dark);
            b.Solid(new Vector3(-hw, 0f, -h), new Vector3(hw, y1, h));
        }

        static void Tractor(MapBuild b, MapBuildingSpec s)
        {
            int scheme = Mathf.Abs(s.Seed) % 3;
            Color body = scheme == 0 ? new Color(0.15f, 0.42f, 0.16f) : scheme == 1 ? new Color(0.72f, 0.1f, 0.08f) : new Color(0.1f, 0.25f, 0.6f);
            Color wheel = scheme == 0 ? new Color(0.95f, 0.78f, 0.1f) : scheme == 1 ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.85f, 0.85f, 0.82f);
            int paint = b.K.M(GlossMat(body, 0.75f, 0.25f)), rim = G(b, wheel, 0.55f, 0.3f);
            int dark = G(b, new Color(0.08f, 0.08f, 0.08f), 0.3f), glass = Glass(b), tire = b.K.M(SurfaceKind.Tire);
            int head = G(b, new Color(0.92f, 0.93f, 0.9f), 0.9f, 0.3f);
            // ~150 hp row-crop tractor, front +Z
            Box(b, new Vector3(0f, 0.78f, 0.9f), new Vector3(0.7f, 0.45f, 3.0f), dark, Vector3.forward);                 // chassis / drivetrain
            Box(b, new Vector3(0f, 1.38f, 1.2f), new Vector3(0.84f, 0.75f, 2.2f), paint, Vector3.forward);                // hood
            Box(b, new Vector3(0f, 1.35f, 2.31f), new Vector3(0.7f, 0.6f, 0.03f), dark);                                   // grille
            Box(b, new Vector3(0f, 0.95f, 2.45f), new Vector3(0.6f, 0.3f, 0.25f), dark);                                   // front weights
            foreach (int sx in Sides) Box(b, new Vector3(sx * 0.25f, 1.6f, 2.31f), new Vector3(0.1f, 0.07f, 0.03f), head);
            Tube(b, new Vector3(0.28f, 1.7f, 1.7f), new Vector3(0.28f, 3.0f, 1.7f), 0.06f, 8, dark);                     // exhaust stack
            // cab: platform, glass box, black posts, roof
            Box(b, new Vector3(0f, 1.45f, -0.75f), new Vector3(1.5f, 0.15f, 1.5f), dark);
            Box(b, new Vector3(0f, 2.24f, -0.75f), new Vector3(1.4f, 1.43f, 1.4f), glass);
            foreach (int sx in Sides)
                foreach (int sz in Sides)
                    Box(b, new Vector3(sx * 0.7f, 2.24f, -0.75f + sz * 0.7f), new Vector3(0.06f, 1.45f, 0.06f), dark, Vector3.up);
            Box(b, new Vector3(0f, 3.0f, -0.75f), new Vector3(1.6f, 0.12f, 1.65f), paint);
            // rear fenders over the big wheels
            foreach (int sx in Sides)
            {
                Box(b, new Vector3(sx * 0.95f, 1.78f, -1.0f), new Vector3(0.6f, 0.05f, 1.5f), paint, Vector3.forward);
                b.B.AddBox(new Vector3(sx * 0.95f, 1.5f, -0.1f), new Vector3(0.6f, 0.05f, 0.65f), Quaternion.Euler(-55f, 0f, 0f), paint);
            }
            // drawbar + lower links
            Box(b, new Vector3(0f, 0.5f, -1.95f), new Vector3(0.12f, 0.06f, 0.7f), dark, Vector3.forward);
            foreach (int sx in Sides) Beam(b, new Vector3(sx * 0.3f, 0.6f, -1.4f), new Vector3(sx * 0.4f, 0.45f, -2.1f), 0.06f, 0.06f, dark);
            // wheels: rear 20.8R42-ish, front 16.9R30-ish
            foreach (int sx in Sides)
            {
                Wheel(b, new Vector3(sx * 0.95f, 0.86f, -1.0f), 0.86f, 0.52f, tire, rim, 14);
                Wheel(b, new Vector3(sx * 0.9f, 0.55f, 1.75f), 0.55f, 0.36f, tire, rim, 12);
            }
            b.Solid(new Vector3(-1.25f, 0f, -2.0f), new Vector3(1.25f, 3.05f, 2.6f));
        }
    }
}
