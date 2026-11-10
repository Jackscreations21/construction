using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Creates real framing members (Pieces + StructuralMember) in a parent's local frame.
    /// lengthDir = the board's long axis, thinDir = its 1-1/2" axis; the wide face takes the remaining axis.</summary>
    public static class Framer
    {
        public static PieceDefinition Def(string id)
        {
            var d = Catalog.Get(id);
            if (d == null) Debug.LogError("[Framer] missing catalog id " + id);
            return d;
        }

        /// <summary>Lumber/sheet member. lengthM = cut length (Z). localCenter in parent space.</summary>
        public static StructuralMember Make(string defId, float lengthM, Transform parent, Vector3 localCenter, Vector3 lengthDir, Vector3 thinDir,
                                           MemberRole role, Connector conn = Connector.Nailed, string group = "")
        {
            var def = Def(defId);
            var p = Piece.Create(def, lengthM, Vector3.zero, Quaternion.identity);
            return Finish(p, parent, localCenter, Quaternion.LookRotation(lengthDir, thinDir), role, conn, group);
        }

        /// <summary>Sheet cut to (width X, length Z); normal = face normal (sheet's Y).</summary>
        public static StructuralMember Sheet(string defId, float widthM, float lengthM, Transform parent, Vector3 localCenter, Vector3 lengthDir, Vector3 normal,
                                            MemberRole role, Connector conn = Connector.Nailed, string group = "")
        {
            var def = Def(defId);
            var p = Piece.Create(def, lengthM, Vector3.zero, Quaternion.identity);
            if (Mathf.Abs(widthM - def.Size.x) > 0.0005f) p.SetSize(new Vector3(widthM, def.Size.y, lengthM));
            return Finish(p, parent, localCenter, Quaternion.LookRotation(lengthDir, normal), role, conn, group);
        }

        /// <summary>Member with a custom visual (angled cuts, trusses, stringers). The collider is a box of 'size'
        /// (piece-local: X width, Y thickness, Z length); 'visual' is built in the same local frame.</summary>
        public static StructuralMember Custom(string defId, Vector3 size, Transform parent, Vector3 localCenter, Quaternion localRot,
                                             MemberRole role, System.Action<MeshBuilder, System.Func<SurfaceKind, int>> visual, Connector conn = Connector.Nailed, string group = "")
        {
            var def = Def(defId);
            var p = Piece.Create(def, size.z, Vector3.zero, Quaternion.identity);
            p.SetSize(size);
            foreach (Transform ch in p.transform)
                if (ch.name == "Visual") { ch.gameObject.SetActive(false); Object.Destroy(ch.gameObject); }
            AttachMesh(p.transform, "Visual", visual);
            return Finish(p, parent, localCenter, localRot, role, conn, group);
        }

        static StructuralMember Finish(Piece p, Transform parent, Vector3 localCenter, Quaternion localRot, MemberRole role, Connector conn, string group)
        {
            if (parent != null) p.transform.SetParent(parent, false);
            p.transform.localPosition = localCenter;
            p.transform.localRotation = localRot;
            p.Nails = 2;
            p.Fasten();
            var m = p.gameObject.AddComponent<StructuralMember>();
            m.Role = role; m.Connectors = conn; m.Group = group;
            p.gameObject.name = role + " (" + p.Def.Name + ")";
            return m;
        }

        /// <summary>Builds a mesh child with Materials-by-SurfaceKind submeshes.</summary>
        public static GameObject AttachMesh(Transform parent, string name, System.Action<MeshBuilder, System.Func<SurfaceKind, int>> build)
        {
            var mats = new List<Material>();
            var index = new Dictionary<SurfaceKind, int>();
            var b = new MeshBuilder();
            int Sub(SurfaceKind k)
            {
                if (!index.TryGetValue(k, out int i)) { i = mats.Count; mats.Add(Materials.Get(k)); index[k] = i; }
                return i;
            }
            build(b, Sub);
            return AttachBuilt(parent, name, b, mats);
        }

        /// <summary>Same, with arbitrary materials (e.g. roofing materials from RoofTextures).</summary>
        public static GameObject AttachMesh(Transform parent, string name, System.Action<MeshBuilder, System.Func<Material, int>> build)
        {
            var mats = new List<Material>();
            var b = new MeshBuilder();
            int Sub(Material m)
            {
                int i = mats.IndexOf(m);
                if (i < 0) { i = mats.Count; mats.Add(m); }
                return i;
            }
            build(b, Sub);
            return AttachBuilt(parent, name, b, mats);
        }

        static GameObject AttachBuilt(Transform parent, string name, MeshBuilder b, List<Material> mats)
        {
            if (mats.Count == 0) mats.Add(Materials.Get(SurfaceKind.PaintedWhite));
            b.MinSubmeshCount = mats.Count;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = b.Build(name);
            go.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            return go;
        }

        /// <summary>Galvanized connector visual (hanger / tie / H-clip / anchor bolt) as a child of a member, no collider.</summary>
        public static GameObject Hardware(Transform parent, string name, Vector3 localCenter, Quaternion localRot, System.Action<MeshBuilder, int> build)
        {
            var go = AttachMesh(parent, name, (MeshBuilder b, System.Func<SurfaceKind, int> s) => build(b, s(SurfaceKind.Galvanized)));
            go.transform.localPosition = localCenter;
            go.transform.localRotation = localRot;
            return go;
        }

        public static float In(float inches) => inches * Units.Inch;
    }

    /// <summary>Helpers shared by framing/roofing textures: make a Standard material from generated albedo/height.</summary>
    public static class ShellMaterials
    {
        static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

        public static bool TryGet(string key, out Material m) => _cache.TryGetValue(key, out m) && m != null;

        /// <summary>albedo: sRGB colors (alpha = relative smoothness 0..1); height: 0..1, n x n, tiling. tileMeters = world size of one tile.</summary>
        public static Material Make(string key, Color[] albedo, float[] height, int n, float tileMeters, float gloss, float bump, float normalStrength = 2f, float metal = 0f)
        {
            if (TryGet(key, out var cached)) return cached;
            var alb = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { name = "GBS_" + key + "_Albedo", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++) { var c = albedo[i]; px[i] = new Color32(B(c.r), B(c.g), B(c.b), B(c.a * 0.5f)); }
            alb.SetPixels32(px); alb.Apply(true, false);
            var m = new Material(Shader.Find("Standard")) { name = "GBS_" + key };
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            m.color = Color.white;
            m.mainTexture = alb;
            m.SetFloat("_Glossiness", gloss);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_SmoothnessTextureChannel", 1f);
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_GlossMapScale", Mathf.Min(2f, gloss * 2f));
            if (height != null)
            {
                m.SetTexture("_BumpMap", NormalFromHeight(height, n, normalStrength, key));
                m.SetFloat("_BumpScale", bump);
                m.EnableKeyword("_NORMALMAP");
            }
            m.mainTextureScale = Vector2.one / tileMeters;
            m.enableInstancing = true;
            _cache[key] = m;
            return m;
        }

        public static Texture2D NormalFromHeight(float[] h, int n, float strength, string key)
        {
            var px = new Color32[n * n];
            float k = strength * 0.125f;
            for (int y = 0; y < n; y++)
            {
                int ym = ((y - 1 + n) % n) * n, y0 = y * n, yp = ((y + 1) % n) * n;
                for (int x = 0; x < n; x++)
                {
                    int xm = (x - 1 + n) % n, xp = (x + 1) % n;
                    float dx = (h[ym + xp] + 2 * h[y0 + xp] + h[yp + xp]) - (h[ym + xm] + 2 * h[y0 + xm] + h[yp + xm]);
                    float dy = (h[yp + xm] + 2 * h[yp + x] + h[yp + xp]) - (h[ym + xm] + 2 * h[ym + x] + h[ym + xp]);
                    var nn = new Vector3(-dx * k * n / 64f, -dy * k * n / 64f, 1f).normalized;
                    px[y0 + x] = new Color32(B(nn.x * 0.5f + 0.5f), B(nn.y * 0.5f + 0.5f), B(nn.z * 0.5f + 0.5f), 255);
                }
            }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { name = "GBS_" + key + "_Normal", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            t.SetPixels32(px); t.Apply(true, false);
            return t;
        }

        static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        // ---- small hash noise helpers for generators ----
        public static float Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }

        /// <summary>Tileable value noise (period = n / cell).</summary>
        public static float Noise(float x, float y, int cell, int period, int seed)
        {
            float fx = x / cell, fy = y / cell;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
            float tx = fx - ix, ty = fy - iy;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            int x0 = ((ix % period) + period) % period, x1 = (x0 + 1) % period;
            int y0 = ((iy % period) + period) % period, y1 = (y0 + 1) % period;
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// <summary>Flat-color material with a little noise (chalk line, gasket, layout crayon, membranes).</summary>
        public static Material Flat(string key, Color c, float gloss = 0.2f, float noise = 0.05f, float tile = 0.5f)
        {
            if (TryGet(key, out var m)) return m;
            const int n = 64;
            var col = new Color[n * n]; var h = new float[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float v = 1f + (Noise(x, y, 8, n / 8, key.GetHashCode()) - 0.5f) * noise * 2f + (Hash(x, y, 7) - 0.5f) * noise;
                col[y * n + x] = new Color(c.r * v, c.g * v, c.b * v, 1f);
                h[y * n + x] = v;
            }
            return Make(key, col, h, n, tile, gloss, 0.3f, 1f);
        }
    }
}
