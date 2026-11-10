using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Real-size procedural models for landscaping items. Origin at ground level (y = 0) unless noted. Cached by key.</summary>
    public static class LandModels
    {
        struct Entry { public Mesh Mesh; public Material[] Mats; }
        static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();

        public static GameObject Instance(string key, Transform parent, System.Action<LandModel> build, string name = null)
        {
            if (!_cache.TryGetValue(key, out var e) || e.Mesh == null)
            {
                var k = new LandModel();
                build(k);
                e = new Entry { Mesh = k.BuildMesh(key), Mats = k.Mats };
                _cache[key] = e;
            }
            var go = new GameObject(name ?? key);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = e.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = e.Mats;
            return go;
        }

        static readonly Color Burlap = new Color(0.62f, 0.50f, 0.34f);
        public static Color LeafMaple = new Color(0.78f, 0.92f, 0.70f), LeafSpruce = new Color(0.55f, 0.72f, 0.80f), LeafBox = new Color(0.62f, 0.85f, 0.55f);

        // ------------------------------------------------------------------ plants

        /// <summary>Deciduous tree. scale 1 = nursery 2" caliper (≈13' tall); 3 = mature shade tree.</summary>
        public static void Deciduous(LandModel k, int seed, float scale, Color leafTint)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            int bark = k.M(LandSurface.Bark), leaf = k.M(LandSurface.Foliage, leafTint), leafDark = k.M(LandSurface.Foliage, leafTint * 0.72f);
            float trunkR = Units.In(1.1f) * scale * (scale > 1.5f ? 2.2f : 1f), trunkH = 2.1f * scale;
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] {
                new Vector2(trunkR * 1.6f, 0f), new Vector2(trunkR * 1.15f, trunkH * 0.08f), new Vector2(trunkR, trunkH * 0.3f),
                new Vector2(trunkR * 0.8f, trunkH), new Vector2(trunkR * 0.55f, trunkH * 1.45f), new Vector2(0.002f, trunkH * 1.9f) }, 9, bark);
            float crownY = trunkH * 1.45f, crownR = 0.85f * scale, crownH = 1.3f * scale;
            int branches = 6 + rng.Next(3);
            for (int i = 0; i < branches; i++)
            {
                float a = i * 2.4f + R() * 0.5f, y = trunkH * (0.75f + 0.6f * R());
                float len = crownR * (0.8f + 0.4f * R());
                var dir = new Vector3(Mathf.Cos(a), 0.9f + R() * 0.5f, Mathf.Sin(a)).normalized;
                var rot = Quaternion.FromToRotation(Vector3.up, dir);
                k.B.AddFrustum(new Vector3(0, y, 0) + dir * len * 0.5f, rot, trunkR * 0.45f, trunkR * 0.12f, len, 6, bark);
            }
            int clumps = (int)(26 * Mathf.Sqrt(scale));
            for (int i = 0; i < clumps; i++)
            {
                // points in an upright ellipsoid crown, biased to the shell
                float u = R() * Mathf.PI * 2, v = Mathf.Acos(2 * R() - 1), rr = 0.55f + 0.45f * Mathf.Sqrt(R());
                var p = new Vector3(Mathf.Sin(v) * Mathf.Cos(u) * crownR, Mathf.Cos(v) * crownH * 0.5f, Mathf.Sin(v) * Mathf.Sin(u) * crownR) * rr;
                p.y += crownY;
                float s = (0.32f + 0.22f * R()) * Mathf.Sqrt(scale) * (scale > 1.5f ? 1.3f : 1f);
                k.B.Transform = Matrix4x4.TRS(p, Quaternion.Euler(R() * 40 - 20, R() * 360, R() * 40 - 20), new Vector3(1f + R() * 0.3f, 0.7f + R() * 0.3f, 1f + R() * 0.3f));
                k.B.AddLathe(Vector3.zero, Quaternion.identity, LandModel.BlobProfile(s, s * 1.6f, 6), 9, p.y < crownY - crownH * 0.15f ? leafDark : leaf);
                k.B.ResetTransform();
            }
        }

        /// <summary>Spruce/fir: trunk + stacked drooping whorls. height in meters.</summary>
        public static void Conifer(LandModel k, int seed, float height, Color tint)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            int bark = k.M(LandSurface.Bark), leaf = k.M(LandSurface.Foliage, tint), leafDark = k.M(LandSurface.Foliage, tint * 0.7f);
            k.B.AddFrustum(new Vector3(0, height * 0.45f, 0), Quaternion.identity, height * 0.022f, height * 0.005f, height * 0.9f, 7, bark);
            int tiers = Mathf.Max(6, (int)(height * 3.2f));
            float baseR = height * 0.27f, bottom = height * 0.08f;
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)(tiers - 1);
                float y = Mathf.Lerp(bottom, height * 0.93f, t);
                float r = baseR * (1f - t * 0.92f) * (0.92f + R() * 0.16f);
                float h = (height - bottom) / tiers * 2.1f;
                k.B.Transform = Matrix4x4.TRS(new Vector3(0, y, 0), Quaternion.Euler(R() * 6 - 3, R() * 360, R() * 6 - 3), Vector3.one);
                // drooping skirt: wide low edge, rising to the trunk, slightly scalloped by low segment count
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] {
                    new Vector2(r * 0.3f, -h * 0.35f), new Vector2(r, -h * 0.25f), new Vector2(r * 0.86f, -h * 0.05f),
                    new Vector2(r * 0.45f, h * 0.35f), new Vector2(0.01f, h * 0.65f) }, 7 + (i % 3), i % 2 == 0 ? leaf : leafDark, 25f);
                k.B.ResetTransform();
            }
        }

        public static void Shrub(LandModel k, int seed, float width, float height, Color tint, Color? flower = null)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            int leaf = k.M(LandSurface.Foliage, tint), dark = k.M(LandSurface.Foliage, tint * 0.75f);
            int fl = flower.HasValue ? k.M(LandSurface.Foliage, flower.Value) : -1;
            int n = 14;
            for (int i = 0; i < n; i++)
            {
                float a = R() * Mathf.PI * 2, d = Mathf.Sqrt(R()) * width * 0.3f;
                var p = new Vector3(Mathf.Cos(a) * d, height * (0.3f + 0.35f * R()), Mathf.Sin(a) * d);
                float s = width * (0.22f + 0.1f * R());
                k.B.Transform = Matrix4x4.TRS(p, Quaternion.Euler(0, R() * 360, 0), new Vector3(1, height / width * 1.2f, 1));
                k.B.AddLathe(Vector3.zero, Quaternion.identity, LandModel.BlobProfile(s, s * 1.8f, 6, 0.3f), 9, i % 3 == 0 ? dark : leaf);
                k.B.ResetTransform();
            }
            if (fl >= 0)
                for (int i = 0; i < 11; i++)
                {
                    float a = R() * Mathf.PI * 2, d = Mathf.Sqrt(R()) * width * 0.36f;
                    var p = new Vector3(Mathf.Cos(a) * d, height * (0.72f + 0.25f * R()), Mathf.Sin(a) * d);
                    k.B.AddLathe(p, Quaternion.Euler(0, R() * 90, 0), LandModel.BlobProfile(0.075f, 0.12f, 5), 8, fl);
                }
        }

        public static void Hosta(LandModel k, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            int leaf = k.M(LandSurface.Foliage, new Color(0.85f, 1.0f, 0.75f)), edge = k.M(LandSurface.Foliage, new Color(1.4f, 1.5f, 1.0f));
            for (int i = 0; i < 16; i++)
            {
                float a = i * 2.4f + R() * 0.3f, tilt = 25f + R() * 35f;
                var rot = Quaternion.Euler(0, a * Mathf.Rad2Deg, 0) * Quaternion.Euler(-tilt, 0, 0);
                var c = rot * new Vector3(0, 0, 0.13f) + Vector3.up * 0.1f;
                k.B.Transform = Matrix4x4.TRS(c, rot, new Vector3(0.55f, 0.08f, 1f));
                k.B.AddLathe(Vector3.zero, Quaternion.Euler(90, 0, 0), LandModel.BlobProfile(0.13f, 0.26f, 5), 7, i % 4 == 0 ? edge : leaf);
                k.B.ResetTransform();
            }
        }

        /// <summary>Burlap-wrapped root ball with twine (sits in the hole; center at y = r).</summary>
        public static void RootBall(LandModel k, float r)
        {
            int b = k.M(SurfaceKind.PaperBag, Burlap), twine = k.M(new Color(0.55f, 0.45f, 0.30f));
            k.B.AddLathe(new Vector3(0, r * 0.8f, 0), Quaternion.identity, LandModel.BlobProfile(r, r * 1.6f, 8, 0.4f), 14, b);
            k.B.AddLathe(new Vector3(0, r * 1.05f, 0), Quaternion.identity, new[] { new Vector2(r * 0.96f, -0.006f), new Vector2(r * 0.96f, 0.006f) }, 14, twine);
        }

        /// <summary>Black plastic nursery pot (gallon sizes), origin at bottom.</summary>
        public static void Pot(LandModel k, float r, float h)
        {
            int m = k.M(SurfaceKind.DarkPlastic), soil = k.M(LandSurface.Topsoil);
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0.001f, 0), new Vector2(r * 0.8f, 0), new Vector2(r, h), new Vector2(r * 1.04f, h), new Vector2(r * 0.97f, h * 0.94f) }, 16, m);
            k.B.AddDisc(new Vector3(0, h * 0.93f, 0), Quaternion.identity, r * 0.96f, 16, soil, true);
        }

        // ------------------------------------------------------------------ hardscape

        /// <summary>Sod roll 2'x5' rolled up (delivery/pile) or laid flat (1" thick).</summary>
        public static void SodFlat(LandModel k)
        {
            int g = k.M(LandSurface.Sod), soil = k.M(LandSurface.Topsoil);
            float w = 2 * Units.Foot, l = 5 * Units.Foot, t = Units.In(1);
            k.B.AddBox(new Vector3(0, t * 0.5f, 0), new Vector3(w, t, l), Quaternion.identity, soil, Vector3.forward, MeshBuilder.AllFaces & ~MeshBuilder.FacePosY);
            k.B.AddBox(new Vector3(0, t * 0.5f, 0), new Vector3(w, t, l), Quaternion.identity, g, Vector3.forward, MeshBuilder.FacePosY);
        }

        /// <summary>Segmental retaining wall block: 12" face x 4" high x 7" deep, split face front (+Z), rear lip.</summary>
        public static void RetainingBlock(LandModel k)
        {
            int face = k.M(LandSurface.RetainingBlock), body = k.M(SurfaceKind.ConcreteBlock, new Color(1.0f, 0.95f, 0.85f));
            float w = Units.In(11.75f), h = Units.In(4), d = Units.In(6.75f);
            // tapered body (wider at the face so blocks curve), split face slightly proud and bevelled
            k.B.AddBox(new Vector3(0, h / 2, -Units.In(0.3f)), new Vector3(w * 0.96f, h, d - Units.In(0.6f)), Quaternion.identity, body, Vector3.right,
                       MeshBuilder.AllFaces & ~MeshBuilder.FacePosZ);
            k.B.AddBevelBox(new Vector3(0, h / 2, d / 2 - Units.In(0.45f)), new Vector3(Units.In(0.9f), h, w), Quaternion.Euler(0, 90, 0), Units.In(0.25f), face);
            // rear lip that locks the course above back 3/4"
            k.B.AddBox(new Vector3(0, -Units.In(0.35f), -d / 2 + Units.In(0.5f)), new Vector3(w * 0.9f, Units.In(0.7f), Units.In(0.9f)), Quaternion.identity, body);
        }

        public static void Stake(LandModel k, Vector3 at)
        {
            int wood = k.M(SurfaceKind.PineLumber), flag = k.M(new Color(1f, 0.35f, 0.6f));
            k.B.AddBox(at + new Vector3(0, Units.In(9), 0), new Vector3(Units.In(1.5f), Units.In(18), Units.In(0.75f)), Quaternion.identity, wood, Vector3.up);
            k.B.AddBox(at + new Vector3(Units.In(1.5f), Units.In(16), 0), new Vector3(Units.In(2.5f), Units.In(1.2f), Units.In(0.1f)), Quaternion.identity, flag);
        }

        /// <summary>Mason's line between two points (thin bright string).</summary>
        public static void String(LandModel k, Vector3 a, Vector3 b, Color c)
        {
            int m = k.M(c);
            var d = b - a; if (d.magnitude < 0.01f) return;
            k.B.AddRod((a + b) / 2, Quaternion.LookRotation(d.normalized), 0.0015f, d.magnitude, 4, m, false);
        }

        public static void PathLight(LandModel k)
        {
            int blk = k.M(SurfaceKind.MatteBlack), lens = k.M(new Color(1f, 0.93f, 0.75f));
            k.B.AddCylinder(new Vector3(0, Units.In(9), 0), Units.In(0.5f), Units.In(18), 10, blk);
            k.B.AddFrustum(new Vector3(0, Units.In(18.75f), 0), Quaternion.identity, Units.In(1.2f), Units.In(1.2f), Units.In(1.5f), 14, lens, false);
            k.B.AddLathe(new Vector3(0, Units.In(19.5f), 0), Quaternion.identity, new[] { new Vector2(Units.In(3.2f), 0), new Vector2(Units.In(3.2f), Units.In(0.25f)), new Vector2(Units.In(1.2f), Units.In(1.6f)), new Vector2(0.001f, Units.In(1.9f)) }, 16, blk);
            k.B.AddFrustum(new Vector3(0, Units.In(-3), 0), Quaternion.identity, Units.In(0.1f), Units.In(0.6f), Units.In(6), 6, blk); // ground stake
        }

        /// <summary>Standard rural-style mailbox on a 4x4 post with arm; door faces +Z (street), numbers on the side.</summary>
        public static void Mailbox(LandModel k)
        {
            int post = k.M(SurfaceKind.TreatedLumber), box = k.M(SurfaceKind.MatteBlack), flag = k.M(new Color(0.85f, 0.1f, 0.08f)), num = k.M(SurfaceKind.Brass);
            float pw = Units.In(3.5f);
            k.B.AddBox(new Vector3(0, Units.In(24), 0), new Vector3(pw, Units.In(48), pw), Quaternion.identity, post, Vector3.up);
            k.B.AddBox(new Vector3(0, Units.In(40), Units.In(7)), new Vector3(Units.In(3.5f), Units.In(3.5f), Units.In(16)), Quaternion.identity, post, Vector3.forward);
            k.B.AddBox(new Vector3(0, Units.In(36.5f), Units.In(4)), new Vector3(Units.In(1.5f), Units.In(3.5f), Units.In(10)), Quaternion.Euler(-35, 0, 0), post, Vector3.forward);
            // box: 6.5" wide, 19" long, half-round top
            var prof = new List<Vector2>();
            float bw = Units.In(3.25f), bh = Units.In(5.5f);
            prof.Add(new Vector2(bw, 0)); prof.Add(new Vector2(bw, bh - bw));
            for (int i = 1; i < 8; i++) { float a = i / 8f * Mathf.PI; prof.Add(new Vector2(Mathf.Cos(a) * bw, bh - bw + Mathf.Sin(a) * bw)); }
            prof.Add(new Vector2(-bw, bh - bw)); prof.Add(new Vector2(-bw, 0));
            prof.Reverse();
            k.B.AddExtrude(prof, new Vector3(0, Units.In(41.75f), Units.In(9)), Quaternion.identity, Units.In(19), box);
            k.B.AddBox(new Vector3(Units.In(3.4f), Units.In(46), Units.In(4)), new Vector3(Units.In(0.15f), Units.In(6), Units.In(1)), Quaternion.identity, flag);
            k.B.AddBox(new Vector3(Units.In(3.4f), Units.In(48.5f), Units.In(5.5f)), new Vector3(Units.In(0.15f), Units.In(1.6f), Units.In(3.5f)), Quaternion.identity, flag);
            // address numbers on both sides of the post (four brass digits)
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 4; i++)
                    k.B.AddBox(new Vector3(s * (pw / 2 + 0.002f), Units.In(30 - i * 3.6f), 0), new Vector3(0.004f, Units.In(3), Units.In(2)), Quaternion.identity, num);
        }

        public static void Tarp(LandModel k, Vector3 size)
        {
            int m = k.M(LandSurface.Tarp);
            float hx = size.x / 2 + 0.08f, hz = size.z / 2 + 0.08f, top = size.y + 0.03f;
            // top + four draped sides, slightly sagging
            k.B.AddQuad(new Vector3(-hx, top, -hz), new Vector3(-hx, top, hz), new Vector3(hx, top, hz), new Vector3(hx, top, -hz), m, Vector3.up);
            k.B.AddQuad(new Vector3(-hx, top, hz), new Vector3(-hx - 0.05f, 0.05f, hz + 0.08f), new Vector3(hx + 0.05f, 0.05f, hz + 0.08f), new Vector3(hx, top, hz), m, Vector3.forward);
            k.B.AddQuad(new Vector3(-hx, top, -hz), new Vector3(hx, top, -hz), new Vector3(hx + 0.05f, 0.05f, -hz - 0.08f), new Vector3(-hx - 0.05f, 0.05f, -hz - 0.08f), m, Vector3.back);
            k.B.AddQuad(new Vector3(hx, top, -hz), new Vector3(hx, top, hz), new Vector3(hx + 0.08f, 0.05f, hz + 0.05f), new Vector3(hx + 0.08f, 0.05f, -hz - 0.05f), m, Vector3.right);
            k.B.AddQuad(new Vector3(-hx, top, -hz), new Vector3(-hx - 0.08f, 0.05f, -hz - 0.05f), new Vector3(-hx - 0.08f, 0.05f, hz + 0.05f), new Vector3(-hx, top, hz), m, Vector3.left);
        }

        /// <summary>Mound of bulk material (dirt, gravel, mulch...) with a cone-ish profile. volume in m³.</summary>
        public static void Mound(LandModel k, Material mat, float volume, int seed)
        {
            int m = k.M(mat);
            float h = Mathf.Clamp(Mathf.Pow(volume, 1f / 3f) * 0.55f, 0.1f, 1.4f);
            float r = Mathf.Sqrt(3f * volume / (Mathf.PI * h)) * 0.8f;
            var rng = new System.Random(seed);
            k.B.Transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), new Vector3(1.1f, 1, 0.9f));
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(r, 0), new Vector2(r * 0.8f, h * 0.25f), new Vector2(r * 0.45f, h * 0.75f), new Vector2(r * 0.15f, h * 0.97f), new Vector2(0.001f, h) }, 14, m, 60f);
            k.B.ResetTransform();
        }
    }
}
