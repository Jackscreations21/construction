using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Procedural visual models for every catalog item, built at true size (meters) as ONE mesh per piece with
    /// one submesh per material. Origin is the center of the size box; products face +Z (front/exterior), Y up.
    /// UVs are in meters (see MeshBuilder). No colliders are added.
    /// </summary>
    public static class ProductModels
    {
        // ================================================================== public API

        public static GameObject Build(PieceDefinition def, Vector3 size, Transform parent)
        {
            bool raw = def != null && (def.Category == PieceCategory.Lumber || def.Category == PieceCategory.Sheet ||
                                       def.Category == PieceCategory.Masonry || def.Category == PieceCategory.Rebar);
            int variant = raw ? UvVariant(parent) : 0;
            string key = (def != null ? def.Id : "null") + "|" + Mathf.RoundToInt(size.x * 1000f) + "," +
                         Mathf.RoundToInt(size.y * 1000f) + "," + Mathf.RoundToInt(size.z * 1000f) + "|" + variant;

            if (!TryCache(key, out Mesh mesh, out Material[] mats))
            {
                var k = new Kit(def);
                if (raw) k.B.UvOffset = VariantOffset(variant);
                BuildInto(k, def, size);
                mesh = k.B.BuildWith(k, def != null ? def.Id : "piece");
                mats = k.Mats.ToArray();
                Cache[key] = new Entry { Mesh = mesh, Mats = mats };
            }
            return MakeObject(parent, "Visual", mesh, mats);
        }

        /// <summary>Delivery flatbed (~8 m x 2.5 m). Origin at ground center, facing +Z.</summary>
        public static GameObject BuildFlatbedTruck(Transform parent)
        {
            const string key = "__truck";
            if (!TryCache(key, out Mesh mesh, out Material[] mats))
            {
                var k = new Kit(null);
                Truck(k);
                mesh = k.B.BuildWith(k, "FlatbedTruck");
                mats = k.Mats.ToArray();
                Cache[key] = new Entry { Mesh = mesh, Mats = mats };
            }
            return MakeObject(parent, "Truck", mesh, mats);
        }

        // ================================================================== plumbing

        struct Entry { public Mesh Mesh; public Material[] Mats; }
        static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>();
        const int UvVariants = 32;

        static bool TryCache(string key, out Mesh mesh, out Material[] mats)
        {
            mesh = null; mats = null;
            if (!Cache.TryGetValue(key, out Entry e) || e.Mesh == null || e.Mats == null) return false;
            for (int i = 0; i < e.Mats.Length; i++) if (e.Mats[i] == null) return false;
            mesh = e.Mesh; mats = e.Mats;
            return true;
        }

        static int UvVariant(Transform parent)
        {
            uint h = (uint)(parent != null ? parent.GetHashCode() : 0);
            h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return (int)(h % UvVariants);
        }

        static Vector2 VariantOffset(int v)
        {
            if (v == 0) return Vector2.zero;
            float u = Mathf.Repeat(v * 0.6180339f, 1f) * 3f;
            float w = Mathf.Repeat(v * 0.7548777f + 0.31f, 1f) * 3f;
            return new Vector2(u, w);
        }

        static GameObject MakeObject(Transform parent, string name, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            return go;
        }

        static Mesh BuildWith(this MeshBuilder b, Kit k, string name)
        {
            if (k.Mats.Count == 0) k.M(SurfaceKind.PaintedWhite);
            b.MinSubmeshCount = k.Mats.Count;
            return b.Build(name);
        }

        /// <summary>Material palette → submesh index, plus min/max box helpers.</summary>
        sealed class Kit
        {
            public readonly MeshBuilder B = new MeshBuilder();
            public readonly List<Material> Mats = new List<Material>();
            readonly Dictionary<Material, int> index = new Dictionary<Material, int>();
            readonly PieceDefinition def;
            static Material fallback;

            public Kit(PieceDefinition d) { def = d; }

            public int Mat(Material m)
            {
                if (m == null)
                {
                    if (fallback == null) fallback = new Material(Shader.Find("Standard")) { name = "GB_Fallback" };
                    m = fallback;
                }
                if (!index.TryGetValue(m, out int i)) { i = Mats.Count; Mats.Add(m); index[m] = i; }
                return i;
            }
            public int M(SurfaceKind kind) => Mat(Materials.Get(kind));
            public int M(SurfaceKind kind, Color tint) => Mat(Materials.Get(kind, tint));
            public int Main() => M(def != null ? Materials.ForDefinition(def) : SurfaceKind.PaintedWhite);

            public void Box(float x0, float x1, float y0, float y1, float z0, float z1, int sub, int mask = MeshBuilder.AllFaces)
            {
                B.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                         new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), Quaternion.identity, sub, Vector3.zero, mask);
            }
            public void BoxU(float x0, float x1, float y0, float y1, float z0, float z1, int sub, Vector3 uHint)
            {
                B.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                         new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), Quaternion.identity, sub, uHint);
            }
            public void BoxR(Vector3 c, Vector3 size, Quaternion r, int sub, Vector3 uHint = default) => B.AddBox(c, size, r, sub, uHint);
            public void CylY(float x, float z, float y0, float y1, float r, int segs, int sub, bool cap = true)
                => B.AddCylinder(new Vector3(x, (y0 + y1) * 0.5f, z), Quaternion.identity, r, Mathf.Abs(y1 - y0), segs, sub, cap);
            public void CylX(float y, float z, float x0, float x1, float r, int segs, int sub, bool cap = true)
                => B.AddCylinder(new Vector3((x0 + x1) * 0.5f, y, z), Quaternion.Euler(0f, 0f, 90f), r, Mathf.Abs(x1 - x0), segs, sub, cap);
            public void CylZ(float x, float y, float z0, float z1, float r, int segs, int sub, bool cap = true)
                => B.AddCylinder(new Vector3(x, y, (z0 + z1) * 0.5f), Quaternion.Euler(90f, 0f, 0f), r, Mathf.Abs(z1 - z0), segs, sub, cap);
        }

        static float I(float inches) => inches * Units.Inch;
        static Vector2 Pi(float rIn, float yIn) => new Vector2(rIn * Units.Inch, yIn * Units.Inch);
        static readonly Quaternion ToZ = Quaternion.Euler(90f, 0f, 0f);    // local Y -> +Z
        static readonly Quaternion ToNegZ = Quaternion.Euler(-90f, 0f, 0f); // local Y -> -Z
        static readonly Quaternion ToX = Quaternion.Euler(0f, 0f, -90f);   // local Y -> +X
        static readonly Color Copper = new Color(0.95f, 0.62f, 0.45f);

        static void BuildInto(Kit k, PieceDefinition def, Vector3 s)
        {
            if (def == null) { k.Box(-s.x / 2, s.x / 2, -s.y / 2, s.y / 2, -s.z / 2, s.z / 2, k.M(SurfaceKind.PaintedWhite)); return; }
            if (s.x <= 0f || s.y <= 0f || s.z <= 0f) s = new Vector3(Mathf.Max(s.x, 0.15f), Mathf.Max(s.y, 0.1f), Mathf.Max(s.z, 0.1f));
            string id = def.Id ?? "";
            switch (def.Category)
            {
                case PieceCategory.Lumber: Lumber(k, def, s); return;
                case PieceCategory.Sheet: k.B.AddBox(Vector3.zero, s, Quaternion.identity, k.Main(), Vector3.forward); return;
                case PieceCategory.Masonry: Masonry(k, def, s); return;
                case PieceCategory.Rebar: k.B.AddRod(Vector3.zero, Quaternion.identity, Mathf.Min(s.x, s.y) * 0.5f, s.z, 8, k.Main()); return;
                case PieceCategory.Consumable: Consumable(k, def, s); return;
                case PieceCategory.Door: Door(k, def, s); return;
                case PieceCategory.GarageDoor:
                    if (id.Contains("opener")) GarageOpener(k, s); else GarageDoor(k, s, id.Contains("carr"));
                    return;
                case PieceCategory.DoorHardware: Hardware(k, def, s); return;
                case PieceCategory.Window: Window(k, id, s); return;
                case PieceCategory.Plumbing: Plumbing(k, def, s); return;
                case PieceCategory.Mechanical: Hvac(k, def, s); return;
                case PieceCategory.Electrical: Electrical(k, def, s); return;
                case PieceCategory.Appliance: Appliance(k, def, s); return;
            }
            k.Box(-s.x / 2, s.x / 2, -s.y / 2, s.y / 2, -s.z / 2, s.z / 2, k.Main());
        }

        // ================================================================== raw materials

        static void Lumber(Kit k, PieceDefinition def, Vector3 s)
        {
            SurfaceKind face = Materials.ForDefinition(def);
            int side = k.M(face);
            int end = face == SurfaceKind.TreatedLumber ? k.M(SurfaceKind.PineEndGrain, new Color(0.80f, 0.92f, 0.70f)) : k.M(SurfaceKind.PineEndGrain);
            float bevel = Mathf.Min(0.003f, Mathf.Min(s.x, s.y) * 0.2f);
            k.B.AddBevelBox(Vector3.zero, s, Quaternion.identity, bevel, side, end);
        }

        static void Masonry(Kit k, PieceDefinition def, Vector3 s)
        {
            int m = k.Main();
            string id = def.Id ?? "";
            if (!id.StartsWith("cmu"))
            {
                // brick: slightly eased long edges, square-ish ends
                k.B.AddBevelBox(Vector3.zero, s, Quaternion.identity, Mathf.Min(0.002f, Mathf.Min(s.x, s.y) * 0.1f), m, m);
                return;
            }
            // hollow CMU: two face shells along Z, webs across, cores open top & bottom
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            float shell = Mathf.Min(I(1.25f), s.x * 0.2f);
            float web = Mathf.Min(I(1.0f), s.z * 0.12f);
            k.BoxU(-hx, -hx + shell, -hy, hy, -hz, hz, m, Vector3.forward);
            k.BoxU(hx - shell, hx, -hy, hy, -hz, hz, m, Vector3.forward);
            k.Box(-hx + shell, hx - shell, -hy, hy, -hz, -hz + web, m);
            k.Box(-hx + shell, hx - shell, -hy, hy, hz - web, hz, m);
            bool half = s.z < I(11f);
            if (!half) k.Box(-hx + shell, hx - shell, -hy, hy, -web * 0.6f, web * 0.6f, m);
        }

        static void Consumable(Kit k, PieceDefinition def, Vector3 s)
        {
            string id = def.Id ?? "";
            int m = k.Main();
            if (id.Contains("tiewire"))
            {
                k.B.AddRing(Vector3.zero, Quaternion.identity, 0.07f, 0.035f, 0.08f, 24, m);
                return;
            }
            if (id.Contains("mortar"))
            {
                k.B.AddBevelBox(Vector3.zero, new Vector3(0.40f, 0.13f, 0.62f), Quaternion.identity, 0.03f, m, m);
                return;
            }
            k.Box(-0.08f, 0.08f, -0.05f, 0.05f, -0.06f, 0.06f, m); // nail box
        }

        // ================================================================== doors

        struct Opening { public float x0, x1, y0, y1, t; }
        enum Fill { Raised, Flat, Glass, Lites }
        struct Row
        {
            public float W; public int Cols; public Fill F; public int Lx, Ly;
            public Row(float w, int cols, Fill f, int lx = 1, int ly = 1) { W = w; Cols = cols; F = f; Lx = lx; Ly = ly; }
        }

        /// <summary>Casing on both faces + jambs + optional stops/threshold. Returns slab opening.</summary>
        static Opening DoorFrame(Kit k, Vector3 s, int trim, bool threshold, bool stops)
        {
            float hx = s.x * 0.5f, hy = s.y * 0.5f, hz = s.z * 0.5f;
            float casW = Mathf.Min(I(1.5f), s.x * 0.06f);
            float casT = s.z * 0.1f;
            float jambT = I(0.625f), reveal = I(0.1875f);
            float jIn = hx - casW + reveal;
            float headIn = hy - casW + reveal;
            float jz = hz - casT;
            for (int side = -1; side <= 1; side += 2)
            {
                float z0 = side > 0 ? hz - casT : -hz, z1 = z0 + casT;
                k.BoxU(-hx, -hx + casW, -hy, hy, z0, z1, trim, Vector3.up);
                k.BoxU(hx - casW, hx, -hy, hy, z0, z1, trim, Vector3.up);
                k.Box(-hx + casW, hx - casW, hy - casW, hy, z0, z1, trim);
            }
            k.BoxU(-jIn - jambT, -jIn, -hy, headIn + jambT, -jz, jz, trim, Vector3.up);
            k.BoxU(jIn, jIn + jambT, -hy, headIn + jambT, -jz, jz, trim, Vector3.up);
            k.Box(-jIn, jIn, headIn, headIn + jambT, -jz, jz, trim);

            float slabT = s.z * 0.72f;
            float floorY = -hy;
            float yb = -hy + I(0.5f);
            if (threshold)
            {
                float th = I(0.75f);
                k.Box(-jIn, jIn, -hy, -hy + th, -hz, hz, k.M(SurfaceKind.BrushedSteel));
                floorY = -hy + th;
                yb = floorY + I(0.125f);
            }
            if (stops && jz - slabT * 0.5f > 1e-4f)
            {
                float sw = I(0.4f), zs0 = slabT * 0.5f, zs1 = jz;
                k.Box(-jIn, -jIn + sw, floorY, headIn, zs0, zs1, trim);
                k.Box(jIn - sw, jIn, floorY, headIn, zs0, zs1, trim);
                k.Box(-jIn + sw, jIn - sw, headIn - sw, headIn, zs0, zs1, trim);
            }
            float gap = I(0.125f);
            return new Opening { x0 = -jIn + gap, x1 = jIn - gap, y0 = yb, y1 = headIn - gap, t = slabT };
        }

        /// <summary>Stile-and-rail slab. rails (top→bottom) has rows.Length+1 entries. Returns the top Y of each rail.</summary>
        static float[] GridSlab(Kit k, float x0, float x1, float y0, float y1, float zc, float T, float stileW, float mullW, float[] rails, Row[] rows, int sub)
        {
            float railSum = 0f, wSum = 0f;
            foreach (var r in rails) railSum += r;
            foreach (var r in rows) wSum += r.W;
            if (wSum <= 0f) wSum = 1f;
            float avail = Mathf.Max(0.01f, (y1 - y0) - railSum);
            float hz = T * 0.5f;
            k.BoxU(x0, x0 + stileW, y0, y1, zc - hz, zc + hz, sub, Vector3.up);
            k.BoxU(x1 - stileW, x1, y0, y1, zc - hz, zc + hz, sub, Vector3.up);
            var tops = new float[rails.Length];
            float y = y1;
            for (int r = 0; r < rails.Length; r++)
            {
                tops[r] = y;
                k.BoxU(x0 + stileW, x1 - stileW, y - rails[r], y, zc - hz, zc + hz, sub, Vector3.right);
                y -= rails[r];
                if (r >= rows.Length) break;
                Row row = rows[r];
                float ph = avail * row.W / wSum;
                float py1 = y, py0 = y - ph;
                int c = Mathf.Max(1, row.Cols);
                float pw = ((x1 - x0) - 2f * stileW - (c - 1) * mullW) / c;
                for (int ci = 0; ci < c; ci++)
                {
                    float px0 = x0 + stileW + ci * (pw + mullW), px1 = px0 + pw;
                    if (ci > 0) k.BoxU(px0 - mullW, px0, py0, py1, zc - hz, zc + hz, sub, Vector3.up);
                    Panel(k, px0, px1, py0, py1, zc, T, row, sub);
                }
                y = py0;
            }
            return tops;
        }

        static void Panel(Kit k, float x0, float x1, float y0, float y1, float zc, float T, Row row, int sub)
        {
            float rec = Mathf.Min(I(0.3f), T * 0.22f);
            float core = T - 2f * rec;
            switch (row.F)
            {
                case Fill.Raised:
                {
                    k.Box(x0, x1, y0, y1, zc - core / 2, zc + core / 2, sub);
                    float inset = Mathf.Min(I(1.25f), 0.22f * Mathf.Min(x1 - x0, y1 - y0));
                    RaisedField(k, x0, x1, y0, y1, zc + core / 2, 1f, rec * 0.8f, inset, sub);
                    RaisedField(k, x0, x1, y0, y1, zc - core / 2, -1f, rec * 0.8f, inset, sub);
                    break;
                }
                case Fill.Flat:
                    k.Box(x0, x1, y0, y1, zc - core / 2, zc + core / 2, sub);
                    break;
                default:
                {
                    int gl = k.M(SurfaceKind.Glass);
                    float gt = Mathf.Min(T * 0.4f, I(0.75f));
                    k.Box(x0, x1, y0, y1, zc - gt / 2, zc + gt / 2, gl);
                    float bw = Mathf.Min(I(0.5f), (x1 - x0) * 0.1f), bt = (T - gt) * 0.5f * 0.7f;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float za = zc + side * gt / 2, zb = za + side * bt;
                        k.Box(x0, x1, y1 - bw, y1, za, zb, sub);
                        k.Box(x0, x1, y0, y0 + bw, za, zb, sub);
                        k.Box(x0, x0 + bw, y0 + bw, y1 - bw, za, zb, sub);
                        k.Box(x1 - bw, x1, y0 + bw, y1 - bw, za, zb, sub);
                    }
                    if (row.F == Fill.Lites)
                    {
                        float mw = I(0.75f), mt = T * 0.6f;
                        for (int i = 1; i < row.Lx; i++)
                        {
                            float x = Mathf.Lerp(x0, x1, i / (float)row.Lx);
                            k.Box(x - mw / 2, x + mw / 2, y0, y1, zc - mt / 2, zc + mt / 2, sub);
                        }
                        for (int j = 1; j < row.Ly; j++)
                        {
                            float y = Mathf.Lerp(y0, y1, j / (float)row.Ly);
                            k.Box(x0, x1, y - mw / 2, y + mw / 2, zc - mt / 2, zc + mt / 2, sub);
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>Raised field with 4 bevels rising from the panel face (z) by h toward dir.</summary>
        static void RaisedField(Kit k, float x0, float x1, float y0, float y1, float z, float dir, float h, float inset, int sub)
        {
            float zi = z + dir * h;
            var o00 = new Vector3(x0, y0, z); var o10 = new Vector3(x1, y0, z);
            var o11 = new Vector3(x1, y1, z); var o01 = new Vector3(x0, y1, z);
            var i00 = new Vector3(x0 + inset, y0 + inset, zi); var i10 = new Vector3(x1 - inset, y0 + inset, zi);
            var i11 = new Vector3(x1 - inset, y1 - inset, zi); var i01 = new Vector3(x0 + inset, y1 - inset, zi);
            var n = new Vector3(0f, 0f, dir);
            k.B.AddQuad(o01, o11, i11, i01, sub, n + Vector3.up * 0.5f);
            k.B.AddQuad(o00, o10, i10, i00, sub, n + Vector3.down * 0.5f);
            k.B.AddQuad(o00, o01, i01, i00, sub, n + Vector3.left * 0.5f);
            k.B.AddQuad(o10, o11, i11, i10, sub, n + Vector3.right * 0.5f);
            k.B.AddQuad(i00, i10, i11, i01, sub, n);
        }

        static readonly Vector2[] KnobProfile =
        {
            Pi(0, 0), Pi(1.25f, 0), Pi(1.25f, 0.12f), Pi(1.0f, 0.3f), Pi(0.38f, 0.4f), Pi(0.32f, 1.0f),
            Pi(0.85f, 1.25f), Pi(1.12f, 1.6f), Pi(1.05f, 2.0f), Pi(0.7f, 2.3f), Pi(0, 2.38f),
        };

        /// <summary>Round knob: rose + neck + ball, axis along dir*Z from the mounting face point.</summary>
        static void Knob(Kit k, Vector3 at, float dir, int sub, float depthScale = 1f, float radiusScale = 1f)
        {
            var p = new Vector2[KnobProfile.Length];
            for (int i = 0; i < p.Length; i++) p[i] = new Vector2(KnobProfile[i].x * radiusScale, KnobProfile[i].y * depthScale);
            k.B.AddLathe(at, dir > 0 ? ToZ : ToNegZ, p, 16, sub);
        }

        static void Rose(Kit k, Vector3 at, float dir, float r, float h, int sub)
        {
            k.B.AddLathe(at, dir > 0 ? ToZ : ToNegZ, new[]
            {
                new Vector2(0, 0), new Vector2(r, 0), new Vector2(r, h * 0.4f), new Vector2(r * 0.8f, h * 0.9f), new Vector2(0, h)
            }, 16, sub);
        }

        /// <summary>Lever: rose + neck + arm along armSign*X. depth = total projection from face.</summary>
        static void Lever(Kit k, Vector3 at, float dir, float armSign, int sub, float depth, float armLen)
        {
            Rose(k, at, dir, I(1.15f), I(0.35f), sub);
            float zA = at.z + dir * I(0.3f), zB = at.z + dir * (depth - I(0.25f));
            k.CylZ(at.x, at.y, zA, zB, I(0.3f), 12, sub);
            float z0 = at.z + dir * (depth - I(0.6f)), z1 = at.z + dir * depth;
            float xEnd = at.x + armSign * armLen;
            k.Box(at.x - armSign * I(0.35f), xEnd, at.y - I(0.28f), at.y + I(0.28f), z0, z1, sub);
            k.CylZ(xEnd, at.y, z0, z1, I(0.3f), 12, sub);
        }

        static void Deadbolt(Kit k, Vector3 at, float dir, int sub)
        {
            Rose(k, at, dir, I(1.1f), I(0.5f), sub);
            k.Box(at.x - I(0.18f), at.x + I(0.18f), at.y - I(0.65f), at.y + I(0.65f), at.z + dir * I(0.4f), at.z + dir * I(0.85f), sub);
        }

        static void Door(Kit k, PieceDefinition def, Vector3 s)
        {
            string id = def.Id ?? "";
            if (id.Contains("slider")) { SliderDoor(k, s); return; }
            if (id.Contains("barn")) { BarnDoor(k, s); return; }
            if (id.Contains("pocket")) { PocketDoor(k, s); return; }

            int trim = k.M(SurfaceKind.PaintedWhite);
            int slab = k.Main();
            bool ext = id.Contains("ext");
            bool bifold = id.Contains("bifold");
            var o = DoorFrame(k, s, trim, ext, !bifold);
            float knobY = -s.y * 0.5f + I(36f);
            float sw = o.x1 - o.x0;
            float ft = o.t * 0.5f;

            if (id.Contains("french"))
            {
                int hw = k.M(SurfaceKind.Nickel);
                float mid = (o.x0 + o.x1) * 0.5f, g = I(0.0625f);
                var rows = new[] { new Row(1f, 1, Fill.Lites, 3, 5) };
                var rails = new[] { I(4.5f), I(9f) };
                GridSlab(k, o.x0, mid - g, o.y0, o.y1, 0f, o.t, I(4.5f), 0f, rails, rows, slab);
                GridSlab(k, mid + g, o.x1, o.y0, o.y1, 0f, o.t, I(4.5f), 0f, rails, rows, slab);
                k.Box(mid - I(0.75f), mid + I(0.75f), o.y0, o.y1, ft, ft + I(0.15f), slab); // astragal
                Lever(k, new Vector3(mid + I(2.4f), knobY, ft), 1f, 1f, hw, I(1.6f), I(4f));
                Lever(k, new Vector3(mid + I(2.4f), knobY, -ft), -1f, 1f, hw, I(1.6f), I(4f));
                Deadbolt(k, new Vector3(mid + I(2.4f), knobY + I(5.5f), ft), 1f, hw);
                Deadbolt(k, new Vector3(mid + I(2.4f), knobY + I(5.5f), -ft), -1f, hw);
                return;
            }

            if (bifold)
            {
                int hw = k.M(SurfaceKind.Nickel);
                float g = I(0.125f);
                float lw = (sw - 3f * g) / 4f;
                float st = Mathf.Min(I(1.75f), lw * 0.22f);
                var rows = new[] { new Row(1f, 1, Fill.Raised), new Row(1.4f, 1, Fill.Raised) };
                var rails = new[] { I(2.5f), I(3f), I(4f) };
                for (int i = 0; i < 4; i++)
                {
                    float lx0 = o.x0 + i * (lw + g);
                    GridSlab(k, lx0, lx0 + lw, o.y0, o.y1, 0f, o.t, st, 0f, rails, rows, slab);
                }
                var small = new[] { Pi(0, 0), Pi(0.45f, 0), Pi(0.3f, 0.35f), Pi(0.55f, 0.6f), Pi(0.5f, 0.95f), Pi(0, 1.0f) };
                float kx1 = o.x0 + 2f * lw + g - I(1.25f), kx2 = o.x0 + 2f * (lw + g) + I(1.25f);
                k.B.AddLathe(new Vector3(kx1, knobY, ft), ToZ, small, 12, hw);
                k.B.AddLathe(new Vector3(kx2, knobY, ft), ToZ, small, 12, hw);
                return;
            }

            float stile = Mathf.Min(I(4.5f), sw * 0.14f), mull = Mathf.Min(I(4f), sw * 0.12f);
            int hwm;
            bool lever = false, bolt = ext;
            if (id.Contains("craftsman") || id.Contains("fg"))
            {
                var rails = new[] { I(4.5f), I(5.5f), I(10f) };
                var rows = new[] { new Row(22f, 1, Fill.Lites, 3, 1), new Row(38f, 2, Fill.Flat) };
                float[] tops = GridSlab(k, o.x0, o.x1, o.y0, o.y1, 0f, o.t, stile, mull, rails, rows, slab);
                float ledge = Mathf.Max(I(0.1f), Mathf.Min(I(0.6f), s.z * 0.5f - ft));
                k.Box(o.x0 + I(0.5f), o.x1 - I(0.5f), tops[1] - I(0.75f), tops[1], ft, ft + ledge, slab); // dentil shelf
                hwm = k.M(SurfaceKind.MatteBlack);
                lever = true;
            }
            else if (id.Contains("shaker"))
            {
                GridSlab(k, o.x0, o.x1, o.y0, o.y1, 0f, o.t, stile, mull, new[] { I(4.5f), I(8f) }, new[] { new Row(1f, 1, Fill.Flat) }, slab);
                hwm = k.M(SurfaceKind.MatteBlack);
                lever = true;
            }
            else
            {
                // classic 6-panel: short top pair, tall middle pair, bottom pair
                var rails = new[] { I(4.5f), I(4.5f), I(7f), I(8f) };
                var rows = new[] { new Row(11f, 2, Fill.Raised), new Row(25f, 2, Fill.Raised), new Row(22f, 2, Fill.Raised) };
                GridSlab(k, o.x0, o.x1, o.y0, o.y1, 0f, o.t, stile, mull, rails, rows, slab);
                hwm = k.M(SurfaceKind.Nickel);
            }

            float hx = o.x1 - I(2.375f);
            for (int side = -1; side <= 1; side += 2)
            {
                var at = new Vector3(hx, knobY, side * ft);
                if (lever) Lever(k, at, side, -1f, hwm, I(1.6f), I(4f));
                else Knob(k, at, side, hwm, 0.65f);
                if (bolt) Deadbolt(k, new Vector3(hx, knobY + I(5.5f), side * ft), side, hwm);
            }
        }

        static void PocketDoor(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int slab = k.M(SurfaceKind.PaintedWhite), steel = k.M(SurfaceKind.Galvanized), hw = k.M(SurfaceKind.Nickel);
            float t = Mathf.Min(s.z * 0.9f, I(1.375f)) * 0.5f;
            float top = hy - I(2.25f);
            k.BoxU(-hx + I(0.1f), hx - I(0.1f), -hy + I(0.5f), top, -t, t, slab, Vector3.up);
            float tz = Mathf.Min(I(0.6f), hz);
            k.Box(-hx, hx, hy - I(1.5f), hy, -tz, tz, steel); // overhead track
            for (int i = -1; i <= 1; i += 2)
            {
                float x = i * (hx - I(5f));
                k.Box(x - I(0.5f), x + I(0.5f), top, hy - I(1.5f), -I(0.15f), I(0.15f), steel); // hangers
            }
            float px = hx - I(1.6f), py = -hy + I(36f);
            k.Box(px - I(0.45f), px + I(0.45f), py - I(1.3f), py + I(1.3f), t, t + I(0.05f), hw);
            k.Box(px - I(0.45f), px + I(0.45f), py - I(1.3f), py + I(1.3f), -t - I(0.05f), -t, hw);
            k.Box(hx - I(0.1f), hx - I(0.05f), py - I(0.8f), py + I(0.8f), -t * 0.5f, t * 0.5f, hw); // edge pull
        }

        static void SliderDoor(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int fr = k.M(SurfaceKind.PaintedWhite), gl = k.M(SurfaceKind.Glass), hw = k.M(SurfaceKind.MatteBlack);
            float fw = I(2f);
            k.Box(-hx, hx, hy - fw, hy, -hz, hz, fr);
            k.Box(-hx, hx, -hy, -hy + I(1.5f), -hz, hz, k.M(SurfaceKind.BrushedSteel));
            k.BoxU(-hx, -hx + fw, -hy + I(1.5f), hy - fw, -hz, hz, fr, Vector3.up);
            k.BoxU(hx - fw, hx, -hy + I(1.5f), hy - fw, -hz, hz, fr, Vector3.up);
            float ox0 = -hx + fw, ox1 = hx - fw, oy0 = -hy + I(1.5f), oy1 = hy - fw;
            float mid = (ox0 + ox1) / 2, ov = I(1f), T = Mathf.Min(I(1.5f), s.z * 0.33f);
            float zI = -s.z * 0.2f, zE = s.z * 0.2f;
            Sash(k, ox0, mid + ov, oy0, oy1, zI, T, I(2.5f), I(3f), I(4f), fr, gl);   // operable, interior track
            Sash(k, mid - ov, ox1, oy0, oy1, zE, T, I(2.5f), I(3f), I(4f), fr, gl);   // fixed, exterior track
            float hxp = ox0 + I(1.25f), py = -hy + I(36f);
            float zf = zI - T / 2;
            k.Box(hxp - I(0.35f), hxp + I(0.35f), py - I(4f), py + I(4f), zf - I(0.55f), zf, hw);
        }

        static void BarnDoor(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int wood = k.M(SurfaceKind.Walnut), blk = k.M(SurfaceKind.MatteBlack);
            // track, standoffs, rollers
            float trackTop = hy - I(3.2f);
            k.BoxU(-hx, hx, trackTop - I(1.5f), trackTop, -hz, -hz + I(0.3f), blk, Vector3.right);
            for (int i = -1; i <= 1; i++) k.CylZ(i * (hx - I(3f)), trackTop - I(0.75f), -hz, -hz + I(0.35f), I(0.35f), 10, blk);
            float slabTop = trackTop - I(1.7f);
            float pz0 = -hz + I(0.25f), pz1 = hz - I(0.35f);
            float rr = I(1.5f), rollY = trackTop + rr;
            for (int i = -1; i <= 1; i += 2)
            {
                float x = i * (hx - I(6f));
                k.CylZ(x, rollY, -hz, -hz + I(0.6f), rr, 16, blk);
                k.CylZ(x, rollY, -hz + I(0.6f), hz - I(0.05f), I(0.25f), 8, blk); // axle bolt
                k.Box(x - I(0.75f), x + I(0.75f), slabTop - I(7f), rollY + I(0.5f), hz - I(0.2f), hz, blk); // strap
            }
            // vertical planks over a recessed core
            float y0 = -hy + I(0.5f);
            k.Box(-hx + I(0.2f), hx - I(0.2f), y0, slabTop, pz0 + I(0.2f), pz1 - I(0.2f), wood);
            int n = Mathf.Max(3, Mathf.RoundToInt(s.x / I(7f)));
            float pw = s.x / n, gp = I(0.12f);
            for (int i = 0; i < n; i++)
                k.BoxU(-hx + i * pw + gp / 2, -hx + (i + 1) * pw - gp / 2, y0, slabTop, pz0, pz1, wood, Vector3.up);
            // Z brace on the front
            float bz0 = pz1, bz1 = hz - I(0.08f), bh = I(5.5f);
            k.BoxU(-hx, hx, slabTop - bh, slabTop, bz0, bz1, wood, Vector3.right);
            k.BoxU(-hx, hx, y0, y0 + bh, bz0, bz1, wood, Vector3.right);
            float w = s.x - I(8f), h = (slabTop - bh) - (y0 + bh) - I(2f);
            float len = Mathf.Sqrt(w * w + h * h);
            float ang = Mathf.Atan2(h, w) * Mathf.Rad2Deg;
            k.BoxR(new Vector3(0f, (slabTop + y0) * 0.5f, (bz0 + bz1) * 0.5f), new Vector3(len, I(5f), bz1 - bz0), Quaternion.Euler(0f, 0f, ang), wood, Vector3.right);
            // bar pull
            float hxp = hx - I(3f), py = -hy + I(38f);
            k.CylY(hxp, pz1 + I(0.3f), py - I(6f), py + I(6f), I(0.28f), 10, blk);
        }

        // ================================================================== garage

        static void GarageDoor(Kit k, Vector3 s, bool carriage)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int body = k.M(carriage ? SurfaceKind.Walnut : SurfaceKind.PaintedSteel);
            int blk = k.M(SurfaceKind.MatteBlack), dark = k.M(SurfaceKind.DarkPlastic), steel = k.M(SurfaceKind.Galvanized);
            float skinBack = hz - I(0.55f), skinFront = hz - I(0.35f);
            float strutT = I(0.6f);
            k.Box(-hx, hx, -hy, hy, -hz + strutT, skinBack, body);
            float secH = s.y / 4f, g = I(0.2f);
            k.Box(-hx, hx, -hy, -hy + I(0.4f), -hz + strutT, skinBack + I(0.05f), dark); // bottom seal

            for (int sct = 0; sct < 4; sct++)
            {
                float sb = -hy + sct * secH, st = sb + secH;
                // back struts on the inside face
                k.BoxU(-hx + I(3f), hx - I(3f), st - I(3.5f), st - I(1f), -hz, -hz + strutT, steel, Vector3.right);

                if (!carriage)
                {
                    k.BoxU(-hx, hx, sb + g, st - g, skinBack, skinFront, body, Vector3.right);
                    int n = Mathf.Max(2, Mathf.RoundToInt(s.x / I(24f)));
                    float m = I(3f), gap = I(3f);
                    float pw = (s.x - 2f * m - (n - 1) * gap) / n;
                    for (int i = 0; i < n; i++)
                    {
                        float x0 = -hx + m + i * (pw + gap);
                        RaisedField(k, x0, x0 + pw, sb + I(3.5f), st - I(3.5f), skinFront, 1f, I(0.28f), I(1.25f), body);
                    }
                    continue;
                }

                // carriage: vertical planks, faux window row in the top section
                if (sct == 3)
                {
                    float wb0 = sb + secH * 0.22f, wb1 = st - secH * 0.22f;
                    k.BoxU(-hx, hx, wb1, st - g, skinBack, skinFront, body, Vector3.right);
                    k.BoxU(-hx, hx, sb + g, wb0, skinBack, skinFront, body, Vector3.right);
                    int gl = k.M(SurfaceKind.Glass);
                    for (int half = 0; half < 2; half++)
                    {
                        float hx0 = half == 0 ? -hx : 0f, hx1 = hx0 + hx;
                        float wx0 = hx0 + I(4f), wx1 = hx1 - I(4f);
                        int lites = Mathf.Max(2, Mathf.RoundToInt((wx1 - wx0) / I(20f)));
                        k.Box(wx0, wx1, wb0, wb1, skinBack - I(0.07f), skinBack + I(0.05f), dark);
                        k.Box(wx0, wx1, wb0, wb1, skinBack + I(0.07f), skinBack + I(0.13f), gl);
                        k.BoxU(hx0, wx0, wb0, wb1, skinBack, skinFront, body, Vector3.up);
                        k.BoxU(wx1, hx1, wb0, wb1, skinBack, skinFront, body, Vector3.up);
                        float mw = I(1.5f);
                        for (int i = 1; i < lites; i++)
                        {
                            float x = Mathf.Lerp(wx0, wx1, i / (float)lites);
                            k.BoxU(x - mw / 2, x + mw / 2, wb0, wb1, skinBack, skinFront, body, Vector3.up);
                        }
                    }
                }
                else
                {
                    int n = Mathf.Max(4, Mathf.RoundToInt(s.x / I(6f)));
                    float pw = s.x / n, gp = I(0.2f);
                    for (int i = 0; i < n; i++)
                        k.Box(-hx + i * pw + gp / 2, -hx + (i + 1) * pw - gp / 2, sb + g, st - g, skinBack, skinFront, body, MeshBuilder.AllFaces & ~MeshBuilder.FaceNegZ);
                }
                // strap hinges at both outer edges with spear tips
                float sy = (sb + st) * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float xa = side * (hx - I(2f)), xb = side * (hx - I(18f));
                    k.Box(Mathf.Min(xa, xb), Mathf.Max(xa, xb), sy - I(0.8f), sy + I(0.8f), skinFront, hz - I(0.05f), blk);
                    k.BoxR(new Vector3(xb, sy, (skinFront + hz - I(0.05f)) * 0.5f), new Vector3(I(1.9f), I(1.9f), hz - I(0.05f) - skinFront),
                           Quaternion.Euler(0f, 0f, 45f), blk);
                    for (int r = 0; r < 3; r++)
                    {
                        float rx = Mathf.Lerp(xa, xb, 0.15f + r * 0.3f);
                        k.CylZ(rx, sy, hz - I(0.05f), hz, I(0.25f), 6, blk);
                    }
                }
            }
            if (carriage)
            {
                float py = -hy + secH * 1.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * I(6f);
                    k.CylY(x, hz - I(0.12f), py - I(4f), py + I(4f), I(0.12f), 8, blk);
                    k.Box(x - I(0.3f), x + I(0.3f), py + I(3.6f), py + I(4.4f), skinFront, hz - I(0.12f), blk);
                    k.Box(x - I(0.3f), x + I(0.3f), py - I(4.4f), py - I(3.6f), skinFront, hz - I(0.12f), blk);
                }
            }
        }

        static void GarageOpener(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int housing = k.M(SurfaceKind.PaintedWhite), lens = k.M(SurfaceKind.Porcelain), steel = k.M(SurfaceKind.Galvanized), dark = k.M(SurfaceKind.DarkPlastic);
            float motorLen = Mathf.Min(I(15f), s.z * 0.4f);
            float mz0 = -hz, mz1 = -hz + motorLen;
            k.B.AddBevelBox(new Vector3(0f, I(0.25f), (mz0 + mz1) * 0.5f), new Vector3(s.x, s.y - I(0.5f), motorLen), Quaternion.identity, I(1f), housing);
            k.Box(-hx + I(2f), hx - I(2f), -hy, -hy + I(0.5f), mz0 + I(2f), mz1 - I(2f), lens);
            k.Box(-hx + I(1f), hx - I(1f), hy, hy + 0.0004f, mz0 + I(1f), mz1 - I(1f), dark, MeshBuilder.FacePosY); // vent top
            // T-rail with belt and trolley
            float ry1 = hy - I(0.5f), ry0 = ry1 - I(1.25f);
            k.BoxU(-I(0.6f), I(0.6f), ry0, ry1, mz1, hz, steel, Vector3.forward);
            k.BoxU(-I(0.4f), I(0.4f), ry0 - I(0.2f), ry0, mz1, hz - I(1f), dark, Vector3.forward);
            float tz = mz1 + (hz - mz1) * 0.55f;
            k.Box(-I(1.1f), I(1.1f), ry0 - I(1.5f), ry1, tz - I(2f), tz + I(2f), dark);
            k.Box(-I(1.5f), I(1.5f), ry0 - I(1f), ry1 + I(0.4f), hz - I(1f), hz, steel); // header bracket
        }

        // ================================================================== hardware

        static void Hardware(Kit k, PieceDefinition def, Vector3 s)
        {
            string id = def.Id ?? "";
            int m = k.Main();
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            var face = new Vector3(0f, 0f, -hz);
            if (id.Contains("hinge"))
            {
                float lt = Mathf.Min(I(0.09f), s.z * 0.2f), br = Mathf.Min(I(0.25f), s.z * 0.5f);
                float zc = -hz + br;
                k.Box(-hx, -br * 0.6f, -hy, hy, -hz, -hz + lt, m);
                k.Box(br * 0.6f, hx, -hy, hy, -hz, -hz + lt, m);
                int kn = 5; float kh = s.y / kn;
                for (int i = 0; i < kn; i++) k.CylY(0f, zc, -hy + i * kh + I(0.02f), -hy + (i + 1) * kh - I(0.02f), br, 12, m);
                int dark = k.M(SurfaceKind.DarkPlastic);
                for (int i = -1; i <= 1; i++)
                    for (int side = -1; side <= 1; side += 2)
                        k.B.AddDisc(new Vector3(side * hx * 0.55f, i * hy * 0.6f, -hz + lt + 0.0003f), ToZ, I(0.17f), 8, dark, true);
                return;
            }
            if (id.Contains("handleset"))
            {
                k.B.AddBevelBox(new Vector3(0f, 0f, -hz + I(0.15f)), new Vector3(s.x * 0.85f, s.y * 0.97f, I(0.3f)), Quaternion.identity, I(0.6f), m);
                float gz = -hz + I(2.1f);
                k.CylY(0f, gz, -hy + I(3f), I(2.5f), I(0.4f), 12, m);
                k.CylZ(0f, -hy + I(3.3f), -hz + I(0.3f), gz, I(0.3f), 10, m);
                k.CylZ(0f, I(2.2f), -hz + I(0.3f), gz, I(0.3f), 10, m);
                k.Box(-I(0.25f), I(0.25f), I(2.6f), I(3.1f), -hz + I(0.3f), -hz + I(1.6f), m); // thumb latch
                Deadbolt(k, new Vector3(0f, hy - I(2f), -hz + I(0.3f)), 1f, m);
                return;
            }
            if (id.Contains("smart"))
            {
                int pad = k.M(SurfaceKind.DarkPlastic), gl = k.M(SurfaceKind.Glass);
                float d = Mathf.Min(I(1.3f), s.z * 0.45f);
                k.B.AddBevelBox(new Vector3(0f, 0f, -hz + d / 2), new Vector3(s.x * 0.95f, s.y * 0.97f, d), Quaternion.identity, I(0.5f), m);
                float fz = -hz + d;
                k.Box(-hx * 0.7f, hx * 0.7f, -hy * 0.25f, hy * 0.8f, fz, fz + I(0.04f), pad);
                k.Box(-hx * 0.7f, hx * 0.7f, -hy * 0.25f, hy * 0.8f, fz + I(0.04f), fz + I(0.07f), gl);
                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 2; c++)
                    {
                        float bx = (c == 0 ? -1 : 1) * hx * 0.33f, by = hy * 0.65f - r * hy * 0.2f;
                        k.Box(bx - I(0.3f), bx + I(0.3f), by - I(0.17f), by + I(0.17f), fz + I(0.07f), fz + I(0.12f), m);
                    }
                k.CylZ(0f, -hy * 0.62f, fz - I(0.05f), fz + I(0.15f), I(0.4f), 12, k.M(SurfaceKind.Nickel));
                k.Box(-hx * 0.85f, hx * 0.85f, -hy * 0.85f, hy * 0.85f, -hz + d + I(0.3f), hz, m); // interior housing (behind door)
                return;
            }
            if (id.Contains("combo"))
            {
                Deadbolt(k, new Vector3(0f, hy - I(1.25f), -hz), 1f, m);
                Lever(k, new Vector3(-hx + I(1.25f), -hy + I(1.25f), -hz), 1f, 1f, m, s.z, s.x - I(1.6f));
                return;
            }
            if (id.Contains("lever"))
            {
                Lever(k, new Vector3(-hx + I(1.25f), 0f, -hz), 1f, 1f, m, s.z, s.x - I(1.6f));
                return;
            }
            // knobs
            float ds = s.z / I(2.38f), rs = Mathf.Min(s.x, s.y) / I(2.5f);
            Knob(k, face, 1f, m, ds, rs);
            if (id.Contains("privacy")) k.CylZ(0f, 0f, hz - I(0.1f), hz, I(0.18f), 8, m);
        }

        // ================================================================== windows

        static void Sash(Kit k, float x0, float x1, float y0, float y1, float zc, float T, float sw, float top, float bot, int fr, int gl)
        {
            float h = T / 2;
            k.BoxU(x0, x0 + sw, y0, y1, zc - h, zc + h, fr, Vector3.up);
            k.BoxU(x1 - sw, x1, y0, y1, zc - h, zc + h, fr, Vector3.up);
            k.Box(x0 + sw, x1 - sw, y1 - top, y1, zc - h, zc + h, fr);
            k.Box(x0 + sw, x1 - sw, y0, y0 + bot, zc - h, zc + h, fr);
            float gt = Mathf.Min(T * 0.55f, I(0.75f)), e = I(0.25f);
            k.Box(x0 + sw - e, x1 - sw + e, y0 + bot - e, y1 - top + e, zc - gt / 2, zc + gt / 2, gl);
        }

        static void Window(Kit k, string id, Vector3 s)
        {
            if (id.Contains("bay")) { BayWindow(k, s); return; }
            if (id.Contains("skylight")) { Skylight(k, s); return; }
            string style = id.Contains("-dh") ? "dh" : id.Contains("casement") ? "casement" : id.Contains("slider") ? "slider" :
                           id.Contains("picture") ? "picture" : id.Contains("hopper") ? "hopper" : "sh";
            WindowUnit(k, style, s);
        }

        static void WindowUnit(Kit k, string style, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int fr = k.M(SurfaceKind.PaintedWhite), gl = k.M(SurfaceKind.Glass), hw = k.M(SurfaceKind.Nickel);
            float fw = Mathf.Min(I(1.75f), Mathf.Min(s.x, s.y) * 0.12f);
            k.Box(-hx, hx, hy - fw, hy, -hz, hz, fr);
            k.Box(-hx, hx, -hy, -hy + fw, -hz, hz, fr);
            k.BoxU(-hx, -hx + fw, -hy + fw, hy - fw, -hz, hz, fr, Vector3.up);
            k.BoxU(hx - fw, hx, -hy + fw, hy - fw, -hz, hz, fr, Vector3.up);
            float ox0 = -hx + fw, ox1 = hx - fw, oy0 = -hy + fw, oy1 = hy - fw;
            float T = Mathf.Min(I(1.25f), s.z * 0.38f);
            float zE = s.z * 0.17f, zI = -s.z * 0.17f;
            float sw = Mathf.Min(I(1.5f), Mathf.Min(ox1 - ox0, oy1 - oy0) * 0.15f);
            float ov = I(0.6f), mr = I(1.25f);
            switch (style)
            {
                case "sh":
                case "dh":
                {
                    float mid = (oy0 + oy1) * 0.5f;
                    Sash(k, ox0, ox1, mid - ov, oy1, zE, T, sw, sw, mr, fr, gl);
                    Sash(k, ox0, ox1, oy0, mid + ov, zI, T, sw, mr, sw * 1.4f, fr, gl);
                    float yl = mid + ov;
                    k.Box(-I(1.2f), I(1.2f), yl, yl + I(0.35f), zI - T / 2, zI + T * 0.3f, hw); // sash lock
                    float zf = zI - T / 2;
                    for (int i = -1; i <= 1; i += 2)
                    {
                        float x = i * (ox1 - ox0) * 0.3f;
                        k.Box(x - I(0.6f), x + I(0.6f), oy0 + I(0.4f), oy0 + I(0.9f), zf - I(0.35f), zf, hw); // lifts
                        if (style == "dh")
                        {
                            float tx = i > 0 ? ox1 - sw * 0.5f : ox0 + sw * 0.5f;
                            k.Box(tx - I(0.35f), tx + I(0.35f), yl - mr * 0.7f, yl - mr * 0.2f, zf - I(0.1f), zf, fr); // tilt latches
                        }
                    }
                    break;
                }
                case "slider":
                {
                    float mid = (ox0 + ox1) * 0.5f;
                    Sash(k, ox0, mid + ov, oy0, oy1, zE, T, sw, sw, sw, fr, gl);
                    Sash(k, mid - ov, ox1, oy0, oy1, zI, T, sw, sw, sw, fr, gl);
                    float zf = zI - T / 2, cy = (oy0 + oy1) * 0.5f;
                    k.Box(mid - ov + I(0.2f), mid - ov + sw - I(0.2f), cy - I(1.2f), cy + I(1.2f), zf - I(0.3f), zf, hw);
                    break;
                }
                case "casement":
                {
                    float zc = zE * 0.5f;
                    Sash(k, ox0, ox1, oy0, oy1, zc, T, sw * 1.2f, sw * 1.2f, sw * 1.2f, fr, gl);
                    float zh0 = -hz, zh1 = Mathf.Min(-hz + I(1.0f), zc - T / 2 - I(0.05f));
                    k.Box(ox1 - I(4.5f), ox1 - I(0.8f), oy0, oy0 + I(0.9f), zh0, zh1, fr); // operator cover
                    k.Box(ox1 - I(4.2f), ox1 - I(1.2f), oy0 + I(0.9f), oy0 + I(1.2f), zh0 + I(0.15f), zh1 - I(0.15f), hw); // folded crank
                    k.CylY(ox1 - I(3.9f), (zh0 + zh1) * 0.5f, oy0 + I(1.2f), oy0 + I(1.8f), I(0.22f), 8, hw);
                    k.Box(ox0 + I(0.2f), ox0 + I(0.8f), -I(1.5f), I(1.5f), zc - T / 2 - I(0.4f), zc - T / 2, hw); // lock lever
                    break;
                }
                case "hopper":
                {
                    float zc = zE * 0.5f;
                    Sash(k, ox0, ox1, oy0, oy1, zc, T, sw, sw, sw, fr, gl);
                    k.Box(-I(1f), I(1f), oy1 - sw * 0.8f, oy1 - sw * 0.2f, zc - T / 2 - I(0.35f), zc - T / 2, hw);
                    break;
                }
                default: // picture
                    Sash(k, ox0, ox1, oy0, oy1, 0f, T, sw * 0.8f, sw * 0.8f, sw * 0.8f, fr, gl);
                    break;
            }
        }

        static void BayWindow(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            float d = Mathf.Min(I(3.25f), s.z * 0.3f), board = I(0.75f);
            float z1 = hz - d / 2, z0 = -hz + d * 0.75f;
            float p = z1 - z0;
            float xb = hx - d * 0.75f, xc = Mathf.Max(I(6f), xb - p);
            float unitH = s.y - 2f * board;
            float L = Mathf.Sqrt((xb - xc) * (xb - xc) + p * p);
            float ang = Mathf.Atan2(p, xb - xc) * Mathf.Rad2Deg;

            k.B.Transform = Matrix4x4.TRS(new Vector3(0f, 0f, z1), Quaternion.identity, Vector3.one);
            WindowUnit(k, "picture", new Vector3(2f * xc, unitH, d));
            k.B.Transform = Matrix4x4.TRS(new Vector3((xb + xc) * 0.5f, 0f, (z0 + z1) * 0.5f), Quaternion.Euler(0f, ang, 0f), Vector3.one);
            WindowUnit(k, "dh", new Vector3(L, unitH, d));
            k.B.Transform = Matrix4x4.TRS(new Vector3(-(xb + xc) * 0.5f, 0f, (z0 + z1) * 0.5f), Quaternion.Euler(0f, -ang, 0f), Vector3.one);
            WindowUnit(k, "dh", new Vector3(L, unitH, d));
            k.B.ResetTransform();

            int fr = k.M(SurfaceKind.PaintedWhite), seat = k.M(SurfaceKind.Oak);
            for (int side = -1; side <= 1; side += 2)
                k.BoxR(new Vector3(side * xc, 0f, z1), new Vector3(d * 0.8f, unitH, d * 0.8f), Quaternion.Euler(0f, side * ang * 0.5f, 0f), fr);
            // seat & head boards: trapezoid in plan. Extrude rot maps local Z->Y and local Y->-Z.
            float fx = xc + d * 0.4f;
            var poly = new[] { new Vector2(-hx, hz), new Vector2(hx, hz), new Vector2(fx, -hz), new Vector2(-fx, -hz) };
            var rot = Quaternion.Euler(-90f, 0f, 0f);
            k.B.AddExtrude(poly, new Vector3(0f, -hy + board / 2, 0f), rot, board, seat);
            k.B.AddExtrude(poly, new Vector3(0f, hy - board / 2, 0f), rot, board, fr);
        }

        static void Skylight(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int lum = k.M(SurfaceKind.PineLumber), clad = k.M(SurfaceKind.PaintedSteel, new Color(0.30f, 0.31f, 0.32f)), gl = k.M(SurfaceKind.Glass);
            float ct = I(1.5f), curbH = Mathf.Max(I(1f), s.y - I(2f));
            float cx = hx - I(0.25f), cz = hz - I(0.25f);
            k.BoxU(-cx, -cx + ct, y0, y0 + curbH, -cz, cz, lum, Vector3.forward);
            k.BoxU(cx - ct, cx, y0, y0 + curbH, -cz, cz, lum, Vector3.forward);
            k.BoxU(-cx + ct, cx - ct, y0, y0 + curbH, -cz, -cz + ct, lum, Vector3.right);
            k.BoxU(-cx + ct, cx - ct, y0, y0 + curbH, cz - ct, cz, lum, Vector3.right);
            float fy0 = y0 + curbH - I(1f), fy1 = hy - I(0.25f), fw = I(2f);
            k.Box(-hx, -hx + fw, fy0, fy1, -hz, hz, clad);
            k.Box(hx - fw, hx, fy0, fy1, -hz, hz, clad);
            k.Box(-hx + fw, hx - fw, fy0, fy1, -hz, -hz + fw, clad);
            k.Box(-hx + fw, hx - fw, fy0, fy1, hz - fw, hz, clad);
            k.Box(-hx + fw - I(0.3f), hx - fw + I(0.3f), fy1 - I(0.8f), fy1 - I(0.2f), -hz + fw - I(0.3f), hz - fw + I(0.3f), gl);
            float cw = I(0.6f);
            k.Box(-hx + fw - cw, -hx + fw + cw * 0.5f, fy1, hy, -hz + fw - cw, hz - fw + cw, clad);
            k.Box(hx - fw - cw * 0.5f, hx - fw + cw, fy1, hy, -hz + fw - cw, hz - fw + cw, clad);
            k.Box(-hx + fw + cw * 0.5f, hx - fw - cw * 0.5f, fy1, hy, -hz + fw - cw, -hz + fw + cw * 0.5f, clad);
            k.Box(-hx + fw + cw * 0.5f, hx - fw - cw * 0.5f, fy1, hy, hz - fw - cw * 0.5f, hz - fw + cw, clad);
        }

        // ================================================================== plumbing

        static void Plumbing(Kit k, PieceDefinition def, Vector3 s)
        {
            string id = def.Id ?? "";
            if (id.Contains("tankless")) { Tankless(k, s); return; }
            if (id.StartsWith("wh-")) { WaterHeater(k, def, s, id.Contains("gas")); return; }
            if (id.Contains("toilet")) { Toilet(k, s); return; }
            if (id.Contains("vanity")) { Vanity(k, def, s); return; }
            if (id.Contains("kitchen-sink")) { KitchenSink(k, def, s); return; }
            if (id.Contains("tub")) { Tub(k, s); return; }
            if (id.Contains("shower")) { Shower(k, s); return; }
            if (id.Contains("sump")) { Sump(k, s); return; }
            k.Box(-s.x / 2, s.x / 2, -s.y / 2, s.y / 2, -s.z / 2, s.z / 2, k.Main());
        }

        static void WaterHeater(Kit k, PieceDefinition def, Vector3 s, bool gas)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int body = k.Main(), blk = k.M(SurfaceKind.MatteBlack), dark = k.M(SurfaceKind.DarkPlastic), galv = k.M(SurfaceKind.Galvanized);
            int cu = k.M(SurfaceKind.Brass, Copper), brass = k.M(SurfaceKind.Brass), label = k.M(SurfaceKind.PaintedWhite, new Color(1f, 0.95f, 0.7f));
            float R = Mathf.Min(hx, hz) - I(0.4f);
            float yt = hy - (gas ? I(6f) : I(3f));
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[]
            {
                new Vector2(0f, y0), new Vector2(R * 0.97f, y0), new Vector2(R, y0 + I(1f)), new Vector2(R, yt - I(1.5f)),
                new Vector2(R * 0.93f, yt - I(0.3f)), new Vector2(R * 0.75f, yt), new Vector2(0f, yt),
            }, 32, body);
            k.B.AddRing(new Vector3(0f, y0 + I(1.5f), 0f), Quaternion.identity, R + I(0.05f), R * 0.9f, I(3f), 32, dark); // base skirt
            // supply nipples with color rings
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * I(4.5f);
                k.CylY(x, 0f, yt - I(0.2f), hy, I(0.45f), 12, cu);
                k.CylY(x, 0f, yt + I(0.3f), yt + I(0.8f), I(0.55f), 12, k.M(SurfaceKind.PaintedSteel, side < 0 ? new Color(0.3f, 0.45f, 0.95f) : new Color(0.95f, 0.3f, 0.25f)));
            }
            if (gas)
            {
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[]
                {
                    new Vector2(I(3.6f), yt + I(0.6f)), new Vector2(I(3.6f), yt + I(1.2f)), new Vector2(I(2.1f), yt + I(2.6f)),
                }, 20, galv);
                k.CylY(0f, 0f, yt + I(2.4f), hy, I(2f), 20, galv, false);
                k.B.AddRing(new Vector3(0f, hy - I(0.05f), 0f), Quaternion.identity, I(2f), I(1.85f), I(0.1f), 20, galv);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 120f * Mathf.Deg2Rad;
                    k.CylY(Mathf.Cos(a) * I(3f), Mathf.Sin(a) * I(3f), yt - I(0.1f), yt + I(0.8f), I(0.15f), 6, galv);
                }
            }
            else
            {
                k.Box(-I(1.5f), I(1.5f), yt - I(0.3f), yt + I(1.5f), I(3f), I(5.5f), body); // junction box
            }
            // front-diagonal controls (the corner of the size box has room around the round tank)
            k.B.Transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 45f, 0f), Vector3.one);
            k.Box(-I(3f), I(3f), y0 + I(20f), y0 + I(30f), R - I(0.4f), R + I(0.06f), label);
            if (gas)
            {
                k.Box(-I(2.4f), I(2.4f), y0 + I(1.5f), y0 + I(6f), R - I(0.5f), R + I(0.15f), blk); // burner door
                k.Box(-I(2f), I(2f), y0 + I(11f), y0 + I(16f), R - I(0.6f), R + I(1.6f), dark);   // gas valve
                k.CylZ(-I(0.6f), y0 + I(14f), R + I(1.6f), R + I(2.1f), I(0.8f), 16, k.M(SurfaceKind.PaintedSteel, new Color(0.9f, 0.2f, 0.15f)));
                k.CylZ(I(2.2f), y0 + I(12f), R + I(0.4f), R + I(1.5f), I(0.42f), 10, blk);
                k.CylY(I(2.2f), R + I(1.5f), y0 + I(8f), hy, I(0.42f), 10, blk);  // gas supply + drip leg
                k.CylY(I(2.2f), R + I(1.5f), y0 + I(5f), y0 + I(8f), I(0.5f), 10, blk);
            }
            else
            {
                k.Box(-I(2.5f), I(2.5f), y0 + I(10f), y0 + I(15f), R - I(0.4f), R + I(0.1f), body);
                k.Box(-I(2.5f), I(2.5f), y0 + I(40f), y0 + I(45f), R - I(0.4f), R + I(0.1f), body);
            }
            // T&P valve and discharge pipe on the other diagonal
            k.B.Transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, -45f, 0f), Vector3.one);
            float ty = yt - I(3f);
            k.CylZ(0f, ty, R - I(0.3f), R + I(1.4f), I(0.5f), 12, brass);
            k.Box(-I(0.4f), I(0.4f), ty, ty + I(1.4f), R + I(0.3f), R + I(1f), brass);
            k.CylY(0f, R + I(1.8f), y0 + I(6f), ty, I(0.42f), 10, cu);
            k.B.ResetTransform();
        }

        static void Tankless(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int body = k.Main(), dark = k.M(SurfaceKind.DarkPlastic), gl = k.M(SurfaceKind.Glass), white = k.M(SurfaceKind.PaintedWhite);
            int blk = k.M(SurfaceKind.MatteBlack), cu = k.M(SurfaceKind.Brass, Copper);
            float by0 = -hy + I(3.5f), by1 = hy - I(3f);
            k.B.AddBevelBox(new Vector3(0f, (by0 + by1) / 2, -I(0.1f)), new Vector3(s.x, by1 - by0, s.z - I(0.2f)), Quaternion.identity, I(0.75f), body);
            float fz = hz - I(0.2f);
            k.Box(-I(2f), I(2f), I(2f), I(4.5f), fz, fz + I(0.1f), dark);
            k.Box(-I(1.8f), I(1.8f), I(2.2f), I(4.3f), fz + I(0.1f), fz + I(0.18f), gl);
            k.Box(-hx + I(1.5f), hx - I(1.5f), by0 + I(1f), by0 + I(1.4f), fz, fz + I(0.1f), dark); // seam
            k.CylY(0f, -I(1f), by1, hy, I(2.1f), 20, white);
            k.CylY(0f, -I(1f), hy - I(0.5f), hy, I(2.4f), 20, white);
            k.CylY(-I(3.5f), I(0f), -hy, by0, I(0.45f), 10, blk);
            k.CylY(0f, I(0f), -hy, by0, I(0.45f), 10, cu);
            k.CylY(I(3.5f), I(0f), -hy, by0, I(0.45f), 10, cu);
            k.CylY(0f, I(0f), by0 - I(1.2f), by0 - I(0.4f), I(0.6f), 10, k.M(SurfaceKind.Brass));
        }

        static void Toilet(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int por = k.M(SurfaceKind.Porcelain), hw = k.M(SurfaceKind.Nickel), water = k.M(SurfaceKind.Glass);
            float tankD = I(8f);
            float zb = -hz + tankD + I(7.5f) * 1.28f + I(0.3f);
            float h = s.y / I(30f); // vertical scale to fit any height
            k.B.Transform = Matrix4x4.TRS(new Vector3(0f, y0, zb), Quaternion.identity, new Vector3(1f, h, 1.28f));
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[]
            {
                Pi(5.4f, 0f), Pi(4.6f, 2f), Pi(4.4f, 6f), Pi(5.2f, 9f), Pi(6.8f, 12f), Pi(7.4f, 14.6f), Pi(7.5f, 15f),
                Pi(7.0f, 15.3f), Pi(6.2f, 15.1f), Pi(4.6f, 12f), Pi(2.8f, 9.5f), Pi(0f, 9.4f),
            }, 28, por);
            k.B.AddDisc(new Vector3(0f, I(10.5f), 0f), Quaternion.identity, I(3.5f), 20, water, true);
            k.B.AddRing(new Vector3(0f, I(15.75f), 0f), Quaternion.identity, I(7.4f), I(4.7f), I(0.8f), 28, por); // seat
            k.B.ResetTransform();
            // closed lid (an open lid would not fit the size box)
            k.B.Transform = Matrix4x4.TRS(new Vector3(0f, y0 + I(16.15f) * h, zb), Quaternion.identity, new Vector3(1f, h, 1.28f));
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { Pi(0, 0), Pi(7.3f, 0), Pi(7.2f, 0.5f), Pi(6.4f, 0.85f), Pi(0, 0.95f) }, 24, por);
            k.B.ResetTransform();
            // tank + lid + flush lever
            float tW = Mathf.Min(s.x - I(1f), I(19f));
            float ty0 = y0 + I(15.2f) * h, ty1 = hy - I(0.8f);
            k.B.AddBevelBox(new Vector3(0f, (ty0 + ty1) / 2, -hz + tankD / 2), new Vector3(tW, tankD, ty1 - ty0), Quaternion.Euler(90f, 0f, 0f), I(1.2f), por);
            k.B.AddBevelBox(new Vector3(0f, hy - I(0.4f), -hz + tankD / 2 + I(0.3f)), new Vector3(Mathf.Min(s.x, tW + I(0.6f)), tankD + I(0.6f), I(0.8f)), Quaternion.Euler(90f, 0f, 0f), I(1.4f), por);
            float lz = -hz + tankD;
            k.CylZ(-tW / 2 + I(2.5f), ty1 - I(2.5f), lz, lz + I(0.4f), I(0.6f), 12, hw);
            k.Box(-tW / 2 + I(2.5f), -tW / 2 + I(5.5f), ty1 - I(2.8f), ty1 - I(2.2f), lz + I(0.3f), lz + I(0.6f), hw);
        }

        static void Vanity(Kit k, PieceDefinition def, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int wood = k.Main(), top = k.M(SurfaceKind.Porcelain), hw = k.M(SurfaceKind.Nickel), dark = k.M(SurfaceKind.DarkPlastic);
            float cy1 = hy - I(4f), cy0 = cy1 - I(1f);
            float fz = hz - I(1.75f);
            k.Box(-hx + I(0.5f), hx - I(0.5f), y0 + I(3.5f), cy0, -hz, fz, wood);
            k.Box(-hx + I(1f), hx - I(1f), y0, y0 + I(3.5f), -hz, fz - I(3f), dark); // toe kick
            // false drawer + two shaker doors
            float dt = I(0.75f), zc = fz + dt / 2;
            k.BoxU(-hx + I(1f), hx - I(1f), cy0 - I(6f), cy0 - I(1f), fz, fz + dt, wood, Vector3.right);
            float g = I(0.12f), dy0 = y0 + I(4f), dy1 = cy0 - I(6.4f);
            var rows = new[] { new Row(1f, 1, Fill.Flat) };
            var rails = new[] { I(2.25f), I(2.25f) };
            GridSlab(k, -hx + I(1f), -g, dy0, dy1, zc, dt, I(2.25f), 0f, rails, rows, wood);
            GridSlab(k, g, hx - I(1f), dy0, dy1, zc, dt, I(2.25f), 0f, rails, rows, wood);
            var kp = new[] { Pi(0, 0), Pi(0.35f, 0), Pi(0.25f, 0.4f), Pi(0.6f, 0.7f), Pi(0.55f, 1.0f), Pi(0, 1.05f) };
            k.B.AddLathe(new Vector3(-I(1.5f), dy1 - I(2.5f), fz + dt), ToZ, kp, 12, hw);
            k.B.AddLathe(new Vector3(I(1.5f), dy1 - I(2.5f), fz + dt), ToZ, kp, 12, hw);
            // countertop with an oval basin
            float a = Mathf.Min(I(8f), hx - I(3f)), b = Mathf.Min(I(6f), hz - I(3f));
            var hc = new Vector2(0f, I(1f));
            k.Box(-hx, hx, cy0, cy1, -hz, hz, top, MeshBuilder.AllFaces & ~MeshBuilder.FacePosY);
            k.B.AddRectWithEllipseHole(new Vector3(0f, cy1, 0f), Quaternion.identity, hx, hz, hc, a, b, 8, top);
            k.B.Transform = Matrix4x4.TRS(new Vector3(hc.x, cy1, hc.y), Quaternion.identity, new Vector3(1f, 1f, b / a));
            float ai = a / Units.Inch;
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[]
            {
                Pi(ai, 0f), Pi(ai * 0.95f, -1.2f), Pi(ai * 0.8f, -3.8f), Pi(ai * 0.5f, -5.4f), Pi(ai * 0.12f, -5.9f), Pi(0f, -5.95f),
            }, 28, top);
            k.B.ResetTransform();
            k.B.AddDisc(new Vector3(hc.x, cy1 - I(5.9f), hc.y), Quaternion.identity, I(0.8f), 12, hw, true);
            // faucet
            float bz = -hz + I(2.5f);
            k.Box(-I(3.5f), I(3.5f), cy1, cy1 + I(0.35f), bz - I(1f), bz + I(1f), hw);
            k.CylY(0f, bz, cy1, cy1 + I(3.5f), I(0.55f), 12, hw);
            k.CylZ(0f, cy1 + I(3.2f), bz, bz + I(4.5f), I(0.45f), 12, hw);
            for (int side = -1; side <= 1; side += 2)
            {
                k.CylY(side * I(3f), bz, cy1, cy1 + I(1.5f), I(0.5f), 12, hw);
                k.Box(side * I(3f) - I(0.2f), side * I(3f) + I(0.2f), cy1 + I(1.3f), cy1 + I(1.8f), bz, bz + I(2.2f), hw);
            }
        }

        static void KitchenSink(Kit k, PieceDefinition def, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int st = k.Main(), hw = k.M(SurfaceKind.Nickel), dark = k.M(SurfaceKind.DarkPlastic);
            float deck = I(0.5f), side = Mathf.Min(I(1.5f), s.x * 0.05f), back = Mathf.Min(I(4f), s.z * 0.2f), front = Mathf.Min(I(2f), s.z * 0.1f), div = I(1f);
            float dy0 = hy - deck;
            k.Box(-hx, hx, dy0, hy, -hz, -hz + back, st);
            k.Box(-hx, hx, dy0, hy, hz - front, hz, st);
            k.Box(-hx, -hx + side, dy0, hy, -hz + back, hz - front, st);
            k.Box(hx - side, hx, dy0, hy, -hz + back, hz - front, st);
            k.Box(-div / 2, div / 2, dy0, hy, -hz + back, hz - front, st);
            float bz0 = -hz + back, bz1 = hz - front, bw = hx - side - div / 2;
            float depth = Mathf.Max(I(2f), s.y - deck - I(0.5f));
            for (int i = -1; i <= 1; i += 2)
            {
                float cx = i * (div / 2 + bw / 2);
                var topSz = new Vector2(bw, bz1 - bz0);
                var botSz = new Vector2(bw - I(1f), bz1 - bz0 - I(1f));
                k.B.AddWell(new Vector3(cx, dy0, (bz0 + bz1) / 2), topSz, botSz, depth, st);
                k.Box(cx - bw / 2, cx + bw / 2, dy0 - depth, dy0, bz0, bz1, st, MeshBuilder.AllFaces & ~MeshBuilder.FacePosY); // outer shell
                float fy = dy0 - depth + I(0.02f);
                k.B.AddRing(new Vector3(cx, fy, (bz0 + bz1) / 2), Quaternion.identity, I(2.2f), I(1.6f), I(0.06f), 16, hw);
                k.B.AddDisc(new Vector3(cx, fy, (bz0 + bz1) / 2), Quaternion.identity, I(1.6f), 16, dark, true);
            }
            for (int i = -1; i <= 1; i++)
                k.B.AddDisc(new Vector3(i * I(4f), hy + 0.0005f, -hz + back * 0.5f), Quaternion.identity, I(0.7f), 12, dark, true);
        }

        static void Tub(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int por = k.M(SurfaceKind.Porcelain), hw = k.M(SurfaceKind.Nickel), dark = k.M(SurfaceKind.DarkPlastic);
            float endW = I(3.5f), backW = I(3f), apronW = I(4f);
            k.Box(-hx, hx, y0, hy, hz - apronW, hz, por);
            k.Box(-hx, hx, y0, hy, -hz, -hz + backW, por);
            k.Box(-hx, -hx + endW, y0, hy, -hz + backW, hz - apronW, por);
            k.Box(hx - endW, hx, y0, hy, -hz + backW, hz - apronW, por);
            float tw = s.x - 2f * endW, td = s.z - backW - apronW;
            float zc = (-hz + backW + hz - apronW) / 2;
            float depth = s.y - I(2f);
            k.B.AddWell(new Vector3(0f, hy, zc), new Vector2(tw, td), new Vector2(tw - I(10f), td - I(5f)), depth, por);
            float fy = hy - depth + I(0.02f), dx = -hx + endW + I(8f);
            k.B.AddRing(new Vector3(dx, fy, zc), Quaternion.identity, I(1.5f), I(1.1f), I(0.08f), 16, hw);
            k.B.AddDisc(new Vector3(dx, fy, zc), Quaternion.identity, I(1.1f), 16, dark, true);
            // overflow plate on the drain-end wall (sloped wall, placed just proud of it)
            float wallX = -hx + endW + I(1.6f);
            k.B.AddLathe(new Vector3(wallX, hy - I(4f), zc), ToX, new[] { Pi(0, 0), Pi(1.4f, 0), Pi(1.3f, 0.2f), Pi(0, 0.3f) }, 16, hw);
        }

        static void Shower(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int por = k.M(SurfaceKind.Porcelain), hw = k.M(SurfaceKind.Nickel), dark = k.M(SurfaceKind.DarkPlastic);
            float panH = I(4f), wt = I(0.5f), rim = I(2f), thr = I(3f);
            float py1 = y0 + panH;
            k.Box(-hx, hx, y0, py1, hz - thr, hz, por);
            k.Box(-hx, hx, y0, py1, -hz, -hz + rim, por);
            k.Box(-hx, -hx + rim, y0, py1, -hz + rim, hz - thr, por);
            k.Box(hx - rim, hx, y0, py1, -hz + rim, hz - thr, por);
            float pw = s.x - 2f * rim, pd = s.z - rim - thr, zc = (-hz + rim + hz - thr) / 2;
            k.B.AddWell(new Vector3(0f, py1, zc), new Vector2(pw, pd), new Vector2(pw - I(1f), pd - I(1f)), panH - I(1f), por);
            float fy = py1 - panH + I(1f) + I(0.02f);
            k.B.AddRing(new Vector3(0f, fy, zc), Quaternion.identity, I(2f), I(1.4f), I(0.06f), 16, hw);
            k.B.AddDisc(new Vector3(0f, fy, zc), Quaternion.identity, I(1.4f), 16, dark, true);
            // walls
            k.BoxU(-hx, hx, py1, hy, -hz, -hz + wt, por, Vector3.up);
            k.BoxU(-hx, -hx + wt, py1, hy, -hz + wt, hz - thr * 0.5f, por, Vector3.up);
            k.BoxU(hx - wt, hx, py1, hy, -hz + wt, hz - thr * 0.5f, por, Vector3.up);
            // soap shelf
            k.Box(hx - wt - I(10f), hx - wt, y0 + I(48f), y0 + I(48.75f), -hz + wt, -hz + wt + I(4f), por);
            // valve trim + head
            float wz = -hz + wt;
            Rose(k, new Vector3(0f, y0 + I(48f), wz), 1f, I(3.2f), I(0.5f), hw);
            Lever(k, new Vector3(0f, y0 + I(48f), wz + I(0.4f)), 1f, 1f, hw, I(1.8f), I(3.5f));
            float ay = y0 + I(76f) > hy - I(2f) ? hy - I(2f) : y0 + I(76f);
            k.CylZ(0f, ay, wz, wz + I(6f), I(0.4f), 10, hw);
            k.B.AddLathe(new Vector3(0f, ay - I(0.6f), wz + I(6.5f)), Quaternion.Euler(40f, 0f, 0f), new[]
            {
                Pi(0, -1.2f), Pi(2.2f, -1.2f), Pi(2.2f, -0.6f), Pi(0.8f, 0.6f), Pi(0, 0.7f),
            }, 16, hw);
            // curtain rod across the opening
            k.CylX(hy - I(4f), hz - I(1.5f), -hx + wt, hx - wt, I(0.5f), 12, hw);
        }

        static void Sump(Kit k, Vector3 s)
        {
            float hy = s.y / 2, y0 = -hy;
            int body = k.Main(), pvc = k.M(SurfaceKind.PaintedWhite), galv = k.M(SurfaceKind.Galvanized), blk = k.M(SurfaceKind.MatteBlack);
            float r = Mathf.Min(s.x, s.z) / 2;
            float hs = Mathf.Min(1f, s.y / I(12f));
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[]
            {
                new Vector2(0f, y0), new Vector2(r * 0.96f, y0), new Vector2(r * 0.96f, y0 + I(2.2f) * hs), new Vector2(r * 0.8f, y0 + I(2.6f) * hs),
                new Vector2(r * 0.78f, y0 + I(8f) * hs), new Vector2(r * 0.66f, y0 + I(8.6f) * hs), new Vector2(0f, y0 + I(8.8f) * hs),
            }, 24, body);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f;
                var c = Quaternion.Euler(0f, a, 0f) * new Vector3(r * 0.96f, y0 + I(1.1f) * hs, 0f);
                k.BoxR(c, new Vector3(I(0.1f), I(1.2f) * hs, I(0.5f)), Quaternion.Euler(0f, a, 0f), galv);
            }
            k.CylY(0f, 0f, y0 + I(8.6f) * hs, hy, I(0.95f), 16, pvc);
            k.CylY(0f, 0f, hy - I(2f), hy - I(0.5f), I(1.15f), 16, pvc);
            float fx = -r * 0.72f;
            k.CylY(fx, fx, y0 + I(2f) * hs, y0 + I(9f) * hs, I(0.12f), 6, galv);
            k.CylY(fx, fx, y0 + I(5f) * hs, y0 + I(6.8f) * hs, I(0.8f), 14, blk);
            k.CylY(r * 0.3f, -r * 0.3f, y0 + I(8.6f) * hs, y0 + I(9.4f) * hs, I(0.4f), 10, blk); // cord boss
        }

        // ================================================================== HVAC

        static void Hvac(Kit k, PieceDefinition def, Vector3 s)
        {
            string id = def.Id ?? "";
            if (id.StartsWith("hvac-ac") || id.StartsWith("hvac-hp") || id.Contains("condenser")) { Condenser(k, s); return; }
            if (id.Contains("furnace")) { Furnace(k, s); return; }
            if (id.Contains("airhandler")) { AirHandler(k, s); return; }
            if (id.Contains("minisplit")) { MiniSplit(k, s); return; }
            if (id.Contains("bathfan")) { BathFan(k, s); return; }
            if (id.Contains("rangehood")) { RangeHood(k, s); return; }
            k.Box(-s.x / 2, s.x / 2, -s.y / 2, s.y / 2, -s.z / 2, s.z / 2, k.Main());
        }

        static void Condenser(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int cab = k.M(SurfaceKind.Galvanized), paint = k.M(SurfaceKind.PaintedSteel, new Color(0.58f, 0.60f, 0.60f));
            int dark = k.M(SurfaceKind.DarkPlastic), blk = k.M(SurfaceKind.MatteBlack), cu = k.M(SurfaceKind.Brass, Copper), white = k.M(SurfaceKind.PaintedWhite);
            float post = I(1.5f), baseH = I(2f), topH = I(1.5f);
            float yb = y0 + baseH, yt = hy - topH;
            k.Box(-hx, hx, y0, yb, -hz, hz, paint);
            float ci = I(1f);
            k.Box(-hx + ci, hx - ci, yb, yt, -hz + ci, hz - ci, dark); // coil
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    k.BoxU(sx > 0 ? hx - post : -hx, sx > 0 ? hx : -hx + post, yb, yt, sz > 0 ? hz - post : -hz, sz > 0 ? hz : -hz + post, cab, Vector3.up);
            // top deck with fan opening
            float fanR = Mathf.Min(hx, hz) - I(2.5f);
            k.Box(-hx, hx, yt, hy, -hz, hz, paint, MeshBuilder.AllFaces & ~MeshBuilder.FacePosY);
            k.B.AddRectWithEllipseHole(new Vector3(0f, hy, 0f), Quaternion.identity, hx, hz, Vector2.zero, fanR, fanR, 8, paint);
            float sh = topH + I(1f);
            k.B.AddRing(new Vector3(0f, hy - sh / 2, 0f), Quaternion.identity, fanR + I(0.3f), fanR, sh, 32, paint);
            // fan: motor, hub, three pitched blades
            k.CylY(0f, 0f, hy - I(9f), hy - I(4.5f), I(3f), 16, dark);
            k.CylY(0f, 0f, hy - I(4.5f), hy - I(2.5f), I(1.8f), 16, blk);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                Quaternion ya = Quaternion.Euler(0f, a, 0f);
                k.BoxR(ya * new Vector3(fanR * 0.52f, 0f, 0f) + new Vector3(0f, hy - I(3.5f), 0f), new Vector3(fanR * 0.75f, I(0.12f), I(6f)), ya * Quaternion.Euler(25f, 0f, 0f), blk);
            }
            // guard: concentric rings + radial bars
            float gy = hy - I(0.15f);
            foreach (float f in new[] { 0.28f, 0.5f, 0.72f, 0.94f })
                k.B.AddRing(new Vector3(0f, gy, 0f), Quaternion.identity, fanR * f + I(0.09f), fanR * f - I(0.09f), I(0.18f), 28, blk);
            for (int i = 0; i < 8; i++)
            {
                Quaternion ya = Quaternion.Euler(0f, i * 45f + 22.5f, 0f);
                k.BoxR(ya * new Vector3(fanR * 0.6f, 0f, 0f) + new Vector3(0f, gy, 0f), new Vector3(fanR * 0.76f, I(0.18f), I(0.18f)), ya, blk);
            }
            // louvered side panels
            float servZ0 = hz - post - Mathf.Min(I(10f), s.z * 0.35f);
            float step = I(0.8f);
            for (float y = yb + I(0.6f); y < yt - I(0.3f); y += step)
            {
                float lenX = s.x - 2f * post;
                for (int sz = -1; sz <= 1; sz += 2)
                    k.BoxR(new Vector3(0f, y, sz * (hz - I(0.45f))), new Vector3(lenX, I(0.06f), I(0.7f)), Quaternion.Euler(sz * 35f, 0f, 0f), cab, Vector3.right);
                float lenZ = s.z - 2f * post;
                k.BoxR(new Vector3(-(hx - I(0.45f)), y, 0f), new Vector3(I(0.7f), I(0.06f), lenZ), Quaternion.Euler(0f, 0f, -35f), cab, Vector3.forward);
                float z0 = -hz + post, z1 = servZ0;
                k.BoxR(new Vector3(hx - I(0.45f), y, (z0 + z1) * 0.5f), new Vector3(I(0.7f), I(0.06f), z1 - z0), Quaternion.Euler(0f, 0f, 35f), cab, Vector3.forward);
            }
            // service panel with valves and label
            k.BoxU(hx - I(0.9f), hx - I(0.05f), yb, yt, servZ0, hz - post, paint, Vector3.up);
            k.CylX(yb + I(3f), servZ0 + I(2.5f), hx - I(0.9f), hx, I(0.45f), 12, cu);
            k.CylX(yb + I(3f), servZ0 + I(5.5f), hx - I(0.9f), hx, I(0.3f), 12, cu);
            k.Box(hx - I(0.06f), hx, yt - I(9f), yt - I(4f), servZ0 + I(1.5f), hz - post - I(1.5f), white);
        }

        static void Furnace(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int body = k.Main(), dark = k.M(SurfaceKind.DarkPlastic), galv = k.M(SurfaceKind.Galvanized), blk = k.M(SurfaceKind.MatteBlack), white = k.M(SurfaceKind.PaintedWhite);
            float top = hy - I(3f), fz = hz - I(0.5f);
            k.Box(-hx, hx, y0, top, -hz, fz, body);
            float split = y0 + s.y * 0.45f;
            k.Box(-hx + I(0.75f), hx - I(0.75f), split + I(0.25f), top - I(0.75f), fz, hz - I(0.1f), body);
            k.Box(-hx + I(0.75f), hx - I(0.75f), y0 + I(1f), split - I(0.25f), fz, hz - I(0.1f), body);
            for (int c = -1; c <= 1; c += 2)
                for (int r = 0; r < 6; r++)
                {
                    float x = c * s.x * 0.22f, y = top - I(3f) - r * I(1.1f);
                    k.Box(x - I(2.5f), x + I(2.5f), y - I(0.2f), y + I(0.2f), hz - I(0.1f), hz - I(0.04f), dark);
                }
            k.Box(-I(2f), I(2f), split - I(4f), split - I(1.5f), hz - I(0.1f), hz - I(0.04f), white);   // label
            k.Box(-I(1f), I(1f), split + I(1f), split + I(1.6f), hz - I(0.1f), hz - I(0.02f), dark);   // pull
            k.Box(-hx + I(1f), hx - I(1f), top, hy - I(1.5f), -hz + I(1f), hz - I(8.5f), galv);        // plenum collar
            k.CylY(0f, hz - I(4.5f), top, hy, I(2f), 16, galv);                                        // flue
            k.CylY(hx - I(2.5f), hz - I(4.5f), top, hy, I(0.42f), 10, blk);                            // gas line
        }

        static void AirHandler(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int body = k.Main(), galv = k.M(SurfaceKind.Galvanized), cu = k.M(SurfaceKind.Brass, Copper), pvc = k.M(SurfaceKind.PaintedWhite), dark = k.M(SurfaceKind.DarkPlastic);
            float top = hy - I(2.5f), fz = hz - I(0.4f);
            k.Box(-hx, hx, y0, top, -hz, fz, body);
            float split = y0 + s.y * 0.42f;
            k.Box(-hx + I(0.6f), hx - I(0.6f), split + I(0.25f), top - I(0.6f), fz, hz - I(0.05f), body);
            k.Box(-hx + I(0.6f), hx - I(0.6f), y0 + I(0.75f), split - I(0.25f), fz, hz - I(0.05f), body);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * (hx - I(1.4f));
                float y = i < 2 ? top - I(1.4f) : split + I(1.2f);
                k.CylZ(x, y, hz - I(0.05f), hz, I(0.2f), 8, galv);
            }
            k.Box(-I(2.5f), I(2.5f), split - I(5f), split - I(2f), hz - I(0.05f), hz - I(0.01f), pvc);
            k.CylZ(hx - I(3f), split + I(6f), hz - I(0.05f), hz, I(0.45f), 10, cu);
            k.CylZ(hx - I(3f), split + I(3.5f), hz - I(0.05f), hz, I(0.3f), 10, cu);
            k.CylZ(-hx + I(3f), split + I(2f), hz - I(0.05f), hz, I(0.45f), 10, pvc);
            k.Box(-hx + I(1.5f), hx - I(1.5f), top, hy, -hz + I(1.5f), hz - I(1.5f), galv);
            k.Box(-hx + I(2f), hx - I(2f), hy, hy + 0.0004f, -hz + I(2f), hz - I(2f), dark, MeshBuilder.FacePosY);
        }

        static void MiniSplit(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int white = k.M(SurfaceKind.PaintedWhite), gloss = k.M(SurfaceKind.Porcelain), dark = k.M(SurfaceKind.DarkPlastic);
            float by0 = -hy + I(0.5f);
            // local Z -> world X, local X -> world -Z
            k.B.AddBevelBox(new Vector3(0f, (by0 + hy) / 2, -I(0.15f)), new Vector3(s.z - I(0.3f), hy - by0, s.x), Quaternion.Euler(0f, 90f, 0f), Mathf.Min(I(1.5f), s.z * 0.2f), white);
            float fz = hz - I(0.15f);
            k.Box(-hx + I(1f), hx - I(1f), -hy + I(3f), hy - I(1f), fz - I(0.1f), hz, gloss);
            k.Box(-hx + I(3f), hx - I(3f), -hy + I(1.2f), -hy + I(2.8f), hz - I(3.5f), fz + I(0.05f), dark); // outlet
            k.BoxR(new Vector3(0f, -hy + I(1.0f), hz - I(1.4f)), new Vector3(s.x - I(6.5f), I(0.12f), I(2.2f)), Quaternion.Euler(-20f, 0f, 0f), white); // vane
            k.Box(hx - I(5f), hx - I(2.5f), -hy + I(3.5f), -hy + I(4.3f), hz - I(0.02f), hz, dark); // display
        }

        static void BathFan(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int galv = k.M(SurfaceKind.Galvanized), white = k.M(SurfaceKind.PaintedWhite), dark = k.M(SurfaceKind.DarkPlastic);
            float g0 = -hy, g1 = -hy + I(0.6f);
            k.Box(-hx + I(0.75f), hx - I(0.75f), g1, hy, -hz + I(0.75f), hz - I(0.75f), galv);
            float rr = Mathf.Min(I(1.9f), (s.y - I(1f)) * 0.5f);
            k.CylX(hy - rr - I(0.3f), 0f, hx - I(0.75f), hx, rr, 16, galv);
            float bw = I(1.25f);
            k.Box(-hx, hx, g0, g1, -hz, -hz + bw, white);
            k.Box(-hx, hx, g0, g1, hz - bw, hz, white);
            k.Box(-hx, -hx + bw, g0, g1, -hz + bw, hz - bw, white);
            k.Box(hx - bw, hx, g0, g1, -hz + bw, hz - bw, white);
            k.Box(-hx + bw, hx - bw, g1 - I(0.1f), g1, -hz + bw, hz - bw, dark);
            int n = 7;
            float span = s.z - 2f * bw;
            for (int i = 0; i < n; i++)
            {
                float z = -hz + bw + span * (i + 0.5f) / n;
                k.Box(-hx + bw, hx - bw, g0 + I(0.05f), g1 - I(0.1f), z - I(0.18f), z + I(0.18f), white);
            }
        }

        static void RangeHood(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int st = k.Main(), galv = k.M(SurfaceKind.Galvanized), dark = k.M(SurfaceKind.DarkPlastic), lens = k.M(SurfaceKind.Porcelain);
            float sb = -hy + I(0.25f), vz = hz - Mathf.Min(I(4f), s.z * 0.25f);
            k.Box(-hx, hx, sb, hy, -hz, vz, st);
            var rot = Quaternion.Euler(0f, 90f, 0f); // local X -> world -Z ; poly = (-z, y)
            var poly = new[] { new Vector2(-vz, hy), new Vector2(-hz, sb + I(1.5f)), new Vector2(-hz, sb), new Vector2(-vz, sb) };
            k.B.AddExtrude(poly, Vector3.zero, rot, s.x, st);
            for (int i = -1; i <= 1; i += 2)
            {
                float cx = i * s.x * 0.24f, w = s.x * 0.2f;
                k.Box(cx - w, cx + w, -hy, sb, -hz + I(2f), vz - I(1f), galv);
                for (int b = 0; b < 6; b++)
                {
                    float z = Mathf.Lerp(-hz + I(3f), vz - I(2f), b / 5f);
                    k.Box(cx - w + I(0.5f), cx + w - I(0.5f), -hy - 0.0005f, -hy + I(0.05f), z - I(0.12f), z + I(0.12f), dark, MeshBuilder.FaceNegY);
                }
            }
            k.Box(-I(1.5f), I(1.5f), -hy, sb, vz - I(0.5f), hz - I(0.5f), lens);
            for (int b = 0; b < 4; b++)
            {
                float x = hx - I(2f) - b * I(1.4f);
                k.Box(x - I(0.45f), x + I(0.45f), sb + I(0.4f), sb + I(1.1f), hz - I(0.1f), hz, dark);
            }
        }

        // ================================================================== electrical

        static void Electrical(Kit k, PieceDefinition def, Vector3 s)
        {
            string id = def.Id ?? "";
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int m = k.Main();
            if (id.Contains("panel"))
            {
                int dark = k.M(SurfaceKind.DarkPlastic), hw = k.M(SurfaceKind.Nickel), white = k.M(SurfaceKind.PaintedWhite);
                k.Box(-hx, hx, -hy, hy, -hz, hz - I(0.3f), m);
                k.Box(-hx + I(0.25f), hx - I(0.25f), -hy + I(0.25f), hy - I(0.25f), hz - I(0.3f), hz - I(0.15f), m);
                k.Box(-hx + I(1.5f), hx - I(1.5f), -hy + I(3f), hy - I(3f), hz - I(0.15f), hz, m);
                k.Box(hx - I(2.4f), hx - I(1.8f), -I(1f), I(1f), hz, hz + 0.0004f, dark, MeshBuilder.FacePosZ);
                k.Box(-I(2.5f), I(2.5f), hy - I(2.6f), hy - I(1f), hz - I(0.15f), hz - I(0.1f), white);
                for (int i = 0; i < 4; i++)
                    k.CylZ((i % 2 == 0 ? -1 : 1) * (hx - I(0.8f)), (i < 2 ? 1 : -1) * (hy - I(1.2f)), hz - I(0.15f), hz - I(0.05f), I(0.2f), 8, hw);
                for (int i = -1; i <= 1; i++)
                    k.B.AddDisc(new Vector3(i * I(3.5f), hy + 0.0004f, 0f), Quaternion.identity, I(0.55f), 12, k.M(SurfaceKind.Galvanized), true);
                return;
            }
            if (id.Contains("meter"))
            {
                int hw = k.M(SurfaceKind.Nickel), gl = k.M(SurfaceKind.Glass), dark = k.M(SurfaceKind.DarkPlastic), white = k.M(SurfaceKind.PaintedWhite);
                float bz1 = -hz + s.z * 0.55f;
                k.Box(-hx, hx, -hy, hy, -hz, bz1, m);
                float cy = I(1f), rr = Mathf.Min(I(3.6f), hx - I(0.5f));
                k.B.AddRing(new Vector3(0f, cy, bz1 + I(0.25f)), ToZ, rr, rr - I(0.4f), I(0.5f), 24, hw);
                k.CylZ(0f, cy, bz1, bz1 + I(0.4f), rr - I(0.35f), 24, dark);
                k.CylZ(0f, cy, bz1 + I(0.4f), bz1 + I(0.5f), rr - I(0.9f), 24, white);
                k.Box(-I(1f), I(1f), cy + I(0.3f), cy + I(1f), bz1 + I(0.5f), bz1 + I(0.55f), dark);
                float dh = hz - (bz1 + I(0.45f));
                k.B.AddLathe(new Vector3(0f, cy, bz1 + I(0.45f)), ToZ, new[]
                {
                    new Vector2(rr - I(0.4f), 0f), new Vector2(rr - I(0.4f), dh * 0.4f), new Vector2(rr * 0.85f, dh * 0.8f), new Vector2(rr * 0.45f, dh * 0.98f), new Vector2(0f, dh),
                }, 24, gl);
                return;
            }
            if (id.Contains("ceiling-light"))
            {
                int hw = k.M(SurfaceKind.Nickel), shade = k.M(SurfaceKind.Porcelain);
                float r = Mathf.Min(hx, hz);
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(r * 0.85f, hy - I(0.8f)), new Vector2(r * 0.92f, hy), new Vector2(0f, hy) }, 24, hw);
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[]
                {
                    new Vector2(0f, -hy + I(0.4f)), new Vector2(r * 0.6f, -hy + I(0.9f)), new Vector2(r * 0.9f, -hy + s.y * 0.45f),
                    new Vector2(r * 0.98f, hy - I(1.3f)), new Vector2(r * 0.88f, hy - I(0.8f)),
                }, 28, shade);
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0f, -hy), new Vector2(I(0.35f), -hy + I(0.15f)), new Vector2(I(0.25f), -hy + I(0.5f)), new Vector2(0f, -hy + I(0.55f)) }, 12, hw);
                return;
            }
            if (id.Contains("ceiling-fan"))
            {
                int body = k.M(SurfaceKind.MatteBlack), wood = k.Main(), shade = k.M(SurfaceKind.Porcelain);
                float sc = s.y / I(12f);
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0f, hy - I(2.5f) * sc), new Vector2(I(1f), hy - I(2.5f) * sc), new Vector2(I(2.8f), hy - I(0.8f) * sc), new Vector2(I(3f), hy), new Vector2(0f, hy) }, 20, body);
                k.CylY(0f, 0f, hy - I(6.5f) * sc, hy - I(2.5f) * sc, I(0.5f), 10, body);
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[]
                {
                    new Vector2(0f, -hy + I(3f) * sc), new Vector2(I(4.5f), -hy + I(3.2f) * sc), new Vector2(I(5f), -hy + I(4.2f) * sc),
                    new Vector2(I(4.8f), -hy + I(5.6f) * sc), new Vector2(I(3f), hy - I(6.3f) * sc), new Vector2(I(0.6f), hy - I(6f) * sc),
                }, 24, body);
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0f, -hy), new Vector2(I(2.2f), -hy + I(0.2f) * sc), new Vector2(I(3.4f), -hy + I(1.4f) * sc), new Vector2(I(3.6f), -hy + I(3f) * sc) }, 24, shade);
                float r = Mathf.Min(hx, hz) - I(0.5f);
                float by = -hy + I(4.6f) * sc;
                for (int i = 0; i < 5; i++)
                {
                    Quaternion ya = Quaternion.Euler(0f, i * 72f, 0f);
                    k.BoxR(ya * new Vector3(I(6.5f), 0f, 0f) + new Vector3(0f, by - I(0.3f), 0f), new Vector3(I(5f), I(0.3f), I(1.2f)), ya, body);
                    float r0 = I(6f), len = r - r0;
                    k.BoxR(ya * new Vector3(r0 + len / 2, 0f, 0f) + new Vector3(0f, by, 0f), new Vector3(len, I(0.3f), I(5.5f)), ya * Quaternion.Euler(12f, 0f, 0f), wood, Vector3.right);
                }
                return;
            }
            if (id.Contains("smoke"))
            {
                int dark = k.M(SurfaceKind.DarkPlastic), red = k.M(SurfaceKind.TruckPaint), white = k.M(SurfaceKind.Porcelain);
                float r = Mathf.Min(hx, hz), lift = I(0.06f);
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[]
                {
                    new Vector2(0f, -hy + lift), new Vector2(r * 0.8f, -hy + lift), new Vector2(r * 0.94f, -hy + s.y * 0.2f), new Vector2(r, -hy + s.y * 0.6f), new Vector2(r, hy), new Vector2(0f, hy),
                }, 28, m);
                k.B.AddRing(new Vector3(0f, -hy + lift / 2, 0f), Quaternion.identity, r * 0.64f, r * 0.48f, lift, 24, dark);
                k.CylY(0f, 0f, -hy, -hy + lift, r * 0.32f, 20, white);
                k.Box(r * 0.55f, r * 0.62f, -hy + lift * 0.5f, -hy + lift, -I(0.04f), I(0.04f), red);
                return;
            }
            if (id.Contains("porch"))
            {
                int gl = k.M(SurfaceKind.Glass), bulb = k.M(SurfaceKind.Porcelain);
                float lw = Mathf.Min(I(2.75f), hx - I(0.2f));
                float lz1 = hz - I(0.75f), lz0 = lz1 - 2f * lw, lzc = (lz0 + lz1) / 2;
                k.B.AddBevelBox(new Vector3(0f, -I(0.5f), -hz + I(0.375f)), new Vector3(Mathf.Min(s.x, I(4.5f)), s.y - I(3f), I(0.75f)), Quaternion.identity, I(0.5f), m);
                k.Box(-I(0.5f), I(0.5f), hy - I(4.5f), hy - I(3.5f), -hz + I(0.75f), lz0 + I(0.2f), m);
                float yb = -hy + I(1f), yt = hy - I(3.2f);
                k.Box(-lw - I(0.2f), lw + I(0.2f), -hy + I(0.4f), yb, lzc - lw - I(0.2f), lzc + lw + I(0.2f), m);
                k.Box(-lw + I(0.1f), lw - I(0.1f), yb, yt, lz0 + I(0.1f), lz1 - I(0.1f), gl);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        k.Box(sx * lw - I(0.2f), sx * lw + I(0.2f), yb, yt, lzc + sz * lw - I(0.2f), lzc + sz * lw + I(0.2f), m);
                k.B.AddLathe(new Vector3(0f, yb, lzc), Quaternion.identity, new[] { Pi(0.5f, 0f), Pi(0.5f, 1.5f), Pi(1.0f, 2.6f), Pi(0.8f, 3.6f), Pi(0f, 4f) }, 14, bulb);
                float sq = Mathf.Sqrt(2f);
                k.B.AddFrustum(new Vector3(0f, (yt + hy - I(1f)) / 2, lzc), Quaternion.Euler(0f, 45f, 0f), (lw + I(0.35f)) * sq, I(0.6f) * sq, hy - I(1f) - yt, 4, m);
                k.CylY(0f, lzc, hy - I(1f), hy, I(0.3f), 8, m);
                return;
            }
            k.Box(-hx, hx, -hy, hy, -hz, hz, m);
        }

        // ================================================================== appliances

        static void Appliance(Kit k, PieceDefinition def, Vector3 s)
        {
            string id = def.Id ?? "";
            if (id.Contains("fridge")) Fridge(k, s);
            else if (id.Contains("range")) Range(k, s);
            else if (id.Contains("dishwasher")) Dishwasher(k, s);
            else if (id.Contains("washer")) Laundry(k, s, true);
            else if (id.Contains("dryer")) Laundry(k, s, false);
            else k.Box(-s.x / 2, s.x / 2, -s.y / 2, s.y / 2, -s.z / 2, s.z / 2, k.Main());
        }

        static void BarHandle(Kit k, bool vertical, float x, float y, float len, float faceZ, float standoff, int sub)
        {
            float r = I(0.45f), zc = faceZ + standoff;
            if (vertical)
            {
                k.CylY(x, zc, y - len / 2, y + len / 2, r, 12, sub);
                k.CylZ(x, y - len / 2 + I(1f), faceZ, zc, I(0.35f), 8, sub);
                k.CylZ(x, y + len / 2 - I(1f), faceZ, zc, I(0.35f), 8, sub);
            }
            else
            {
                k.CylX(y, zc, x - len / 2, x + len / 2, r, 12, sub);
                k.CylZ(x - len / 2 + I(1f), y, faceZ, zc, I(0.35f), 8, sub);
                k.CylZ(x + len / 2 - I(1f), y, faceZ, zc, I(0.35f), 8, sub);
            }
        }

        static void Fridge(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int st = k.M(SurfaceKind.BrushedSteel), side = k.M(SurfaceKind.PaintedSteel, new Color(0.28f, 0.28f, 0.29f)), dark = k.M(SurfaceKind.DarkPlastic), gl = k.M(SurfaceKind.Glass);
            float bodyZ = hz - I(3.5f), doorZ = hz - I(1.25f);
            k.Box(-hx, hx, y0, hy, -hz, bodyZ, side);
            k.Box(-hx + I(0.5f), hx - I(0.5f), y0, y0 + I(3.5f), bodyZ, bodyZ + I(0.3f), dark);
            float dy = y0 + I(27f);
            k.BoxU(-hx + I(0.1f), hx - I(0.1f), y0 + I(3.75f), dy, bodyZ, doorZ, st, Vector3.right);
            k.BoxU(-hx + I(0.1f), -I(0.15f), dy + I(0.4f), hy - I(0.25f), bodyZ, doorZ, st, Vector3.up);
            k.BoxU(I(0.15f), hx - I(0.1f), dy + I(0.4f), hy - I(0.25f), bodyZ, doorZ, st, Vector3.up);
            float fh = hy - I(0.25f) - (dy + I(0.4f));
            float hyc = dy + I(0.4f) + fh * 0.5f;
            BarHandle(k, true, -I(1.75f), hyc, Mathf.Min(I(40f), fh * 0.85f), doorZ, I(0.8f), st);
            BarHandle(k, true, I(1.75f), hyc, Mathf.Min(I(40f), fh * 0.85f), doorZ, I(0.8f), st);
            BarHandle(k, false, 0f, dy - I(3f), Mathf.Min(I(28f), s.x * 0.8f), doorZ, I(0.8f), st);
            float dxc = -hx * 0.5f, dyc = dy + I(22f);
            k.Box(dxc - I(3.5f), dxc + I(3.5f), dyc - I(6f), dyc + I(6f), doorZ, doorZ + I(0.06f), dark);
            k.Box(dxc - I(2.5f), dxc + I(2.5f), dyc + I(6.5f), dyc + I(8.5f), doorZ, doorZ + I(0.06f), gl);
            k.Box(-hx + I(2f), -hx + I(5f), hy - I(0.25f), hy, bodyZ, doorZ, side);
            k.Box(hx - I(5f), hx - I(2f), hy - I(0.25f), hy, bodyZ, doorZ, side);
        }

        static void Range(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int st = k.M(SurfaceKind.BrushedSteel), blk = k.M(SurfaceKind.MatteBlack), dark = k.M(SurfaceKind.DarkPlastic), gl = k.M(SurfaceKind.Glass), side = k.M(SurfaceKind.PaintedSteel, new Color(0.3f, 0.3f, 0.31f));
            float ctop = Mathf.Min(y0 + I(36f), hy - I(4f));
            float sc = (ctop - y0) / I(36f);
            float bodyZ = hz - I(2.5f), doorZ = hz - I(1f);
            k.Box(-hx, hx, y0 + I(0.75f), ctop - I(1f), -hz, bodyZ, side);
            k.Box(-hx + I(0.5f), hx - I(0.5f), y0, y0 + I(0.75f), -hz, bodyZ - I(1f), dark);
            k.Box(-hx + I(0.1f), hx - I(0.1f), y0 + I(0.75f), y0 + I(6.2f) * sc, bodyZ, doorZ, st); // drawer
            // oven door with window
            float d0 = y0 + I(6.5f) * sc, d1 = y0 + I(29f) * sc, w0 = y0 + I(13f) * sc, w1 = y0 + I(24f) * sc, sx = hx - I(4.5f);
            k.Box(-hx + I(0.1f), hx - I(0.1f), d0, w0, bodyZ, doorZ, st);
            k.Box(-hx + I(0.1f), hx - I(0.1f), w1, d1, bodyZ, doorZ, st);
            k.Box(-hx + I(0.1f), -sx, w0, w1, bodyZ, doorZ, st);
            k.Box(sx, hx - I(0.1f), w0, w1, bodyZ, doorZ, st);
            k.Box(-sx, sx, w0, w1, bodyZ, doorZ - I(0.4f), dark);
            k.Box(-sx, sx, w0, w1, doorZ - I(0.4f), doorZ - I(0.25f), gl);
            BarHandle(k, false, 0f, d1 - I(1.6f) * sc, s.x - I(4f), doorZ, I(0.55f), st);
            // control band + knobs
            k.Box(-hx, hx, d1 + I(0.4f), ctop - I(1f), bodyZ, hz - I(1.6f), st);
            float ky = (d1 + I(0.4f) + ctop - I(1f)) * 0.5f;
            var kp = new[] { Pi(0, 0), Pi(0.95f, 0), Pi(0.95f, 0.85f), Pi(0.8f, 1.05f), Pi(0, 1.1f) };
            for (int i = -2; i <= 2; i++)
                k.B.AddLathe(new Vector3(i * s.x * 0.17f, ky, hz - I(1.6f)), ToZ, kp, 14, blk);
            // cooktop, burners, grates
            k.Box(-hx, hx, ctop - I(1f), ctop, -hz + I(3f), hz - I(1.6f), blk);
            float cz = (-hz + I(3f) + hz - I(1.6f)) * 0.5f, cd = (hz - I(1.6f)) - (-hz + I(3f));
            for (int i = 0; i < 4; i++)
            {
                float bx = (i % 2 == 0 ? -1 : 1) * s.x * 0.24f, bz = cz + (i < 2 ? 1 : -1) * cd * 0.24f;
                k.CylY(bx, bz, ctop, ctop + I(0.35f), I(2.1f), 16, k.M(SurfaceKind.Galvanized));
                k.CylY(bx, bz, ctop + I(0.35f), ctop + I(0.6f), I(1.5f), 16, blk);
            }
            float gt = ctop + I(0.6f), gtt = gt + I(0.5f), bar = I(0.4f);
            for (int g = -1; g <= 1; g += 2)
            {
                float gx0 = g < 0 ? -hx + I(0.3f) : I(0.2f), gx1 = g < 0 ? -I(0.2f) : hx - I(0.3f);
                float gz0 = -hz + I(3.2f), gz1 = hz - I(1.8f);
                k.Box(gx0, gx1, gt, gtt, gz0, gz0 + bar, blk);
                k.Box(gx0, gx1, gt, gtt, gz1 - bar, gz1, blk);
                k.Box(gx0, gx0 + bar, gt, gtt, gz0, gz1, blk);
                k.Box(gx1 - bar, gx1, gt, gtt, gz0, gz1, blk);
                for (int b = 1; b <= 2; b++)
                {
                    float x = Mathf.Lerp(gx0, gx1, b / 3f);
                    k.Box(x - bar / 2, x + bar / 2, gt, gtt, gz0, gz1, blk);
                    float z = Mathf.Lerp(gz0, gz1, b / 3f);
                    k.Box(gx0, gx1, gt, gtt, z - bar / 2, z + bar / 2, blk);
                }
            }
            // backguard with display
            k.Box(-hx, hx, ctop - I(1f), hy, -hz, -hz + I(3f), st);
            k.Box(-I(3f), I(3f), ctop + (hy - ctop) * 0.35f, ctop + (hy - ctop) * 0.7f, -hz + I(3f), -hz + I(3.08f), gl);
        }

        static void Dishwasher(Kit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int st = k.M(SurfaceKind.BrushedSteel), side = k.M(SurfaceKind.PaintedSteel, new Color(0.45f, 0.45f, 0.46f)), dark = k.M(SurfaceKind.DarkPlastic), gl = k.M(SurfaceKind.Glass);
            float bodyZ = hz - I(2f), doorZ = hz - I(1f);
            k.Box(-hx, hx, y0 + I(4f), hy, -hz, bodyZ, side);
            k.Box(-hx + I(0.3f), hx - I(0.3f), y0, y0 + I(4f), -hz, bodyZ - I(1f), dark);
            k.BoxU(-hx + I(0.05f), hx - I(0.05f), y0 + I(4f), hy - I(0.1f), bodyZ, doorZ, st, Vector3.right);
            k.Box(-hx + I(0.05f), hx - I(0.05f), hy - I(4f), hy - I(0.1f), doorZ, doorZ + I(0.05f), st);
            k.Box(I(1f), hx - I(2f), hy - I(2.8f), hy - I(1.4f), doorZ + I(0.05f), doorZ + I(0.1f), gl);
            BarHandle(k, false, 0f, hy - I(5.5f), s.x - I(4f), doorZ, I(0.55f), st);
        }

        static void Laundry(Kit k, Vector3 s, bool washer)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int body = k.M(SurfaceKind.PaintedWhite), chrome = k.M(SurfaceKind.Nickel), dark = k.M(SurfaceKind.DarkPlastic), gl = k.M(SurfaceKind.Glass), steel = k.M(SurfaceKind.BrushedSteel);
            float bodyZ = hz - I(1.5f);
            k.Box(-hx, hx, y0 + I(0.8f), hy, -hz, bodyZ, body);
            for (int i = 0; i < 4; i++)
                k.CylY((i % 2 == 0 ? -1 : 1) * (hx - I(2f)), (i < 2 ? 1 : -1) * (hz - I(3f)), y0, y0 + I(0.8f), I(0.8f), 10, dark);
            // control panel
            float py0 = hy - I(6f);
            k.Box(-hx, hx, py0, hy, bodyZ, hz - I(0.9f), body);
            float pz = hz - I(0.9f);
            var dial = new[] { Pi(0, 0), Pi(1.4f, 0), Pi(1.4f, 0.6f), Pi(1.2f, 0.8f), Pi(0, 0.85f) };
            float dialX = washer ? I(4f) : 0f;
            k.B.AddLathe(new Vector3(dialX, (py0 + hy) / 2, pz), ToZ, dial, 20, chrome);
            float dispX = washer ? -I(3f) : -I(6f);
            k.Box(dispX - I(2.2f), dispX + I(2.2f), py0 + I(2f), py0 + I(4f), pz, pz + I(0.05f), dark);
            if (washer) k.Box(-hx + I(1f), -hx + I(8.5f), py0 + I(1f), hy - I(0.8f), pz, pz + I(0.2f), body); // detergent drawer
            else
                for (int b = 0; b < 3; b++)
                    k.CylZ(I(6f) + b * I(1.6f), (py0 + hy) / 2, pz, pz + I(0.15f), I(0.45f), 10, dark);
            // round door
            float dy = y0 + (washer ? I(18.5f) : I(20f));
            float ro = washer ? I(8.5f) : I(9f), ri = washer ? I(6f) : I(7.4f);
            ro = Mathf.Min(ro, hx - I(1f)); ri = Mathf.Min(ri, ro - I(1f));
            k.B.AddRing(new Vector3(0f, dy, hz - I(0.9f)), ToZ, ro, ri, I(1.2f), 32, washer ? chrome : body);
            if (washer)
            {
                k.B.AddDisc(new Vector3(0f, dy, bodyZ + 0.0005f), ToZ, ri, 28, steel, true);
                k.B.AddRing(new Vector3(0f, dy, bodyZ + I(0.1f)), ToZ, ri, ri - I(1.2f), I(0.2f), 28, dark);
                k.B.AddLathe(new Vector3(0f, dy, hz - I(1.4f)), ToZ, new[] { new Vector2(ri + I(0.1f), 0f), new Vector2(ri * 0.92f, I(0.6f)), new Vector2(ri * 0.6f, I(1.0f)), new Vector2(0f, I(1.15f)) }, 28, gl);
            }
            else
            {
                k.B.AddLathe(new Vector3(0f, dy, hz - I(1.4f)), ToZ, new[] { new Vector2(ri + I(0.1f), 0f), new Vector2(ri, I(0.6f)), new Vector2(ri * 0.8f, I(0.9f)), new Vector2(0f, I(1.0f)) }, 28, k.M(SurfaceKind.PaintedWhite, new Color(0.9f, 0.9f, 0.92f)));
            }
            k.Box(ro - I(0.5f), ro + I(0.4f), dy - I(2f), dy + I(2f), hz - I(0.9f), hz - I(0.3f), chrome);
            k.Box(-hx + I(1f), -hx + I(5f), y0 + I(1.5f), y0 + I(4f), bodyZ, bodyZ + I(0.1f), body);
        }

        // ================================================================== truck

        static void Wheel(Kit k, Vector3 c, float side, float w, int tire, int rim, int hub)
        {
            float R = 0.5f, ri = 0.29f, hw = w * 0.5f;
            Quaternion rot = side > 0 ? ToX : Quaternion.Euler(0f, 0f, 90f); // local +Y points outboard
            k.B.AddLathe(c, rot, new[]
            {
                new Vector2(ri, -hw), new Vector2(R - 0.04f, -hw), new Vector2(R, -hw + 0.05f), new Vector2(R, hw - 0.05f), new Vector2(R - 0.04f, hw), new Vector2(ri, hw),
            }, 24, tire);
            k.B.AddCylinder(c, rot, ri + 0.002f, w - 0.04f, 20, rim);
            Vector3 o = rot * Vector3.up;
            k.B.AddCylinder(c + o * (hw - 0.02f + 0.015f), rot, 0.1f, 0.03f, 12, hub);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                Vector3 local = new Vector3(Mathf.Cos(a) * 0.16f, hw - 0.02f + 0.01f, Mathf.Sin(a) * 0.16f);
                k.B.AddCylinder(c + rot * local, rot, 0.018f, 0.02f, 6, hub);
            }
        }

        static void Truck(Kit k)
        {
            int paint = k.M(SurfaceKind.TruckPaint), glass = k.M(SurfaceKind.Glass), blk = k.M(SurfaceKind.MatteBlack), chrome = k.M(SurfaceKind.Nickel);
            int dark = k.M(SurfaceKind.DarkPlastic), tire = k.M(SurfaceKind.Tire), rim = k.M(SurfaceKind.Galvanized), deck = k.M(SurfaceKind.Oak);
            int amber = k.M(SurfaceKind.Brass), lens = k.M(SurfaceKind.Porcelain);

            // chassis
            k.BoxU(-0.45f, -0.35f, 0.62f, 0.9f, -3.9f, 3.7f, blk, Vector3.forward);
            k.BoxU(0.35f, 0.45f, 0.62f, 0.9f, -3.9f, 3.7f, blk, Vector3.forward);
            for (int i = 0; i < 5; i++) { float z = -3.6f + i * 1.6f; k.Box(-0.35f, 0.35f, 0.66f, 0.84f, z - 0.04f, z + 0.04f, blk); }

            // wheels: front singles, rear duals (6 total)
            float fz = 3.1f, rz = -1.9f, tw = 0.28f;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Wheel(k, new Vector3(sd * 0.96f, 0.5f, fz), sd, tw, tire, rim, chrome);
                Wheel(k, new Vector3(sd * 1.06f, 0.5f, rz), sd, tw, tire, rim, chrome);
                Wheel(k, new Vector3(sd * 0.77f, 0.5f, rz), sd, tw, tire, rim, rim);
            }
            k.CylX(0.5f, fz, -0.82f, 0.82f, 0.05f, 8, blk);
            k.CylX(0.5f, rz, -0.92f, 0.92f, 0.06f, 8, blk);
            k.B.AddLathe(new Vector3(0f, 0.5f, rz), ToZ, new[] { new Vector2(0f, -0.15f), new Vector2(0.17f, -0.12f), new Vector2(0.2f, 0f), new Vector2(0.12f, 0.2f), new Vector2(0f, 0.22f) }, 12, blk);

            // front: bumper, grille, headlights
            k.BoxU(-1.15f, 1.15f, 0.5f, 0.78f, 3.8f, 4.0f, chrome, Vector3.right);
            k.Box(-0.98f, 0.98f, 1.0f, 1.78f, 2.55f, 3.8f, paint);
            k.B.AddExtrude(new[] { new Vector2(-3.8f, 1.78f), new Vector2(-2.55f, 1.82f), new Vector2(-2.55f, 1.78f) }, Vector3.zero, Quaternion.Euler(0f, 90f, 0f), 1.9f, paint); // hood crown
            k.Box(-0.72f, 0.72f, 0.92f, 1.7f, 3.8f, 3.86f, blk);
            for (int i = 0; i < 6; i++) { float y = 0.98f + i * 0.12f; k.Box(-0.68f, 0.68f, y, y + 0.035f, 3.86f, 3.88f, chrome); }
            k.Box(-0.76f, 0.76f, 1.66f, 1.74f, 3.8f, 3.9f, chrome);
            k.Box(-0.76f, 0.76f, 0.88f, 0.94f, 3.8f, 3.9f, chrome);
            k.Box(-0.76f, -0.7f, 0.94f, 1.66f, 3.8f, 3.9f, chrome);
            k.Box(0.7f, 0.76f, 0.94f, 1.66f, 3.8f, 3.9f, chrome);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float x0 = sd * 0.78f, x1 = sd * 0.96f;
                k.Box(Mathf.Min(x0, x1), Mathf.Max(x0, x1), 1.32f, 1.56f, 3.79f, 3.84f, chrome);
                k.Box(Mathf.Min(x0, x1) + 0.01f, Mathf.Max(x0, x1) - 0.01f, 1.33f, 1.55f, 3.84f, 3.86f, glass);
                k.Box(Mathf.Min(x0, x1), Mathf.Max(x0, x1), 1.2f, 1.28f, 3.79f, 3.85f, amber);
                // front fender over the wheel
                float fx0 = sd * 0.9f, fx1 = sd * 1.13f;
                float lo = Mathf.Min(fx0, fx1), hi = Mathf.Max(fx0, fx1);
                k.Box(lo, hi, 1.02f, 1.14f, 2.52f, 3.78f, paint);
                k.Box(lo, hi, 0.72f, 1.02f, 3.66f, 3.78f, paint);
                k.Box(lo, hi, 0.8f, 1.02f, 2.52f, 2.62f, paint);
            }

            // cab
            k.Box(-1.05f, 1.05f, 0.95f, 1.86f, 1.0f, 2.55f, paint);
            var gh = new[] { new Vector2(-1.05f, 1.84f), new Vector2(-2.5f, 1.84f), new Vector2(-2.22f, 2.58f), new Vector2(-1.05f, 2.58f) };
            k.B.AddExtrude(gh, Vector3.zero, Quaternion.Euler(0f, 90f, 0f), 1.92f, dark); // greenhouse (seen through glass)
            k.Box(-1.0f, 1.0f, 2.58f, 2.67f, 1.0f, 2.26f, paint);   // roof
            k.Box(-0.99f, 0.99f, 1.84f, 1.9f, 1.0f, 2.5f, paint);   // belt line
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float x0 = sd * 0.94f, x1 = sd * 1.0f;
                k.Box(Mathf.Min(x0, x1), Mathf.Max(x0, x1), 1.84f, 2.6f, 1.0f, 1.2f, paint);    // C pillar
                k.BoxR(new Vector3(sd * 0.975f, 2.215f, 2.36f), new Vector3(0.06f, 0.79f, 0.09f), Quaternion.Euler(-21f, 0f, 0f), paint); // A pillar
                float gx = sd * 0.965f;
                k.B.AddQuad(new Vector3(gx, 1.9f, 1.22f), new Vector3(gx, 1.9f, 2.44f), new Vector3(gx, 2.55f, 2.2f), new Vector3(gx, 2.55f, 1.22f), glass, new Vector3(sd, 0f, 0f));
                float sx = sd * 1.052f;
                k.Box(sx - 0.002f, sx + 0.002f, 0.97f, 1.84f, 1.21f, 1.23f, blk);   // door seams
                k.Box(sx - 0.002f, sx + 0.002f, 0.97f, 1.84f, 2.43f, 2.45f, blk);
                k.Box(sx - 0.01f, sx + 0.01f, 1.62f, 1.66f, 1.3f, 1.45f, chrome);  // door handle
                // mirrors
                float ma = sd * 1.0f, mb = sd * 1.16f;
                k.Box(Mathf.Min(ma, mb), Mathf.Max(ma, mb), 2.0f, 2.03f, 2.28f, 2.31f, blk);
                float m0 = sd * 1.13f, m1 = sd * 1.25f;
                k.Box(Mathf.Min(m0, m1), Mathf.Max(m0, m1), 1.8f, 2.22f, 2.24f, 2.33f, blk);
                k.Box(Mathf.Min(m0, m1) + 0.01f, Mathf.Max(m0, m1) - 0.01f, 1.82f, 2.2f, 2.235f, 2.24f, chrome);
                // step + fuel tank
                float s0 = sd * 1.05f, s1 = sd * 1.2f;
                k.Box(Mathf.Min(s0, s1), Mathf.Max(s0, s1), 0.52f, 0.58f, 1.15f, 1.85f, blk);
            }
            Vector3 wn = new Vector3(0f, 0.28f, 0.73f).normalized * 0.006f;
            k.B.AddQuad(new Vector3(-0.92f, 1.87f, 2.49f) + wn, new Vector3(0.92f, 1.87f, 2.49f) + wn, new Vector3(0.92f, 2.56f, 2.225f) + wn, new Vector3(-0.92f, 2.56f, 2.225f) + wn, glass, new Vector3(0f, 0.28f, 0.73f));
            k.Box(-0.95f, 0.95f, 1.84f, 1.88f, 2.45f, 2.56f, blk); // cowl
            k.B.AddQuad(new Vector3(-0.8f, 1.95f, 1.044f), new Vector3(0.8f, 1.95f, 1.044f), new Vector3(0.8f, 2.5f, 1.044f), new Vector3(-0.8f, 2.5f, 1.044f), glass, Vector3.back);
            k.CylZ(-0.75f, 0.55f, -0.9f, 0.3f, 0.25f, 16, chrome); // fuel tank
            k.Box(-0.78f, -0.55f, 0.75f, 0.85f, -0.6f, -0.5f, blk);

            // flatbed
            float b0 = -3.95f, b1 = 0.8f;
            k.BoxU(-1.2f, 1.2f, 0.98f, 1.12f, b0, b1, blk, Vector3.forward);
            int boards = 12;
            float bw = 2.36f / boards;
            for (int i = 0; i < boards; i++)
            {
                float x = -1.18f + i * bw;
                k.BoxU(x + 0.003f, x + bw - 0.003f, 1.12f, 1.17f, b0 + 0.02f, b1 - 0.02f, deck, Vector3.forward);
            }
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float r0 = sd * 1.16f, r1 = sd * 1.22f;
                k.BoxU(Mathf.Min(r0, r1), Mathf.Max(r0, r1), 0.98f, 1.2f, b0 - 0.02f, b1, blk, Vector3.forward); // rub rail
                // stake pockets: open boxes outboard of the rail, dark openings on top
                for (float z = b0 + 0.3f; z < b1 - 0.1f; z += 0.6f)
                {
                    float p0 = sd * 1.22f, p1 = sd * 1.25f;
                    float lo = Mathf.Min(p0, p1), hi = Mathf.Max(p0, p1);
                    k.Box(lo, hi, 1.0f, 1.2f, z - 0.05f, z - 0.035f, blk);
                    k.Box(lo, hi, 1.0f, 1.2f, z + 0.035f, z + 0.05f, blk);
                    float oz = sd > 0 ? hi - 0.004f : lo + 0.004f;
                    k.Box(oz - 0.004f, oz + 0.004f, 1.0f, 1.2f, z - 0.035f, z + 0.035f, blk);
                    k.Box(lo, hi, 1.0f, 1.03f, z - 0.035f, z + 0.035f, blk);
                    k.Box(lo + 0.002f, hi - 0.002f, 1.03f, 1.19f, z - 0.035f, z + 0.035f, dark, MeshBuilder.FacePosY);
                }
                // mud flap behind rear duals
                float f0 = sd * 0.62f, f1 = sd * 1.2f;
                k.Box(Mathf.Min(f0, f1), Mathf.Max(f0, f1), 0.25f, 0.98f, -2.62f, -2.6f, dark);
                // tail light
                float t0 = sd * 0.85f, t1 = sd * 1.12f;
                k.Box(Mathf.Min(t0, t1), Mathf.Max(t0, t1), 0.9f, 1.0f, -3.99f, -3.95f, paint);
                k.Box(Mathf.Min(t0, t1) + 0.01f, Mathf.Max(t0, t1) - 0.01f, 0.91f, 0.99f, -4.0f, -3.99f, glass);
            }
            k.Box(-1.1f, 1.1f, 0.72f, 0.86f, -4.0f, -3.9f, blk); // rear bumper
            k.Box(-0.15f, 0.15f, 0.82f, 0.9f, -4.0f, -3.97f, lens); // plate lamp area
            // headache rack
            float hz0 = 0.74f, hz1 = 0.8f;
            for (int sd = -1; sd <= 1; sd += 2) k.Box(sd * 1.1f - 0.04f, sd * 1.1f + 0.04f, 1.17f, 2.4f, hz0, hz1, blk);
            k.Box(-1.14f, 1.14f, 2.32f, 2.4f, hz0, hz1, blk);
            k.Box(-1.14f, 1.14f, 1.75f, 1.8f, hz0, hz1, blk);
            for (int i = -4; i <= 4; i++) k.Box(i * 0.22f - 0.015f, i * 0.22f + 0.015f, 1.17f, 2.32f, hz0 + 0.01f, hz1 - 0.01f, blk);
        }
    }
}
