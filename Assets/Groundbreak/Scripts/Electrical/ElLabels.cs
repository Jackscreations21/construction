using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>Printed text for electrical gear (circuit directory, space numbers, amp ratings, nameplates): 5x7 glyphs
    /// (LumberMarks font) rasterized to an alpha-cutout texture, laid on explicit-UV quads at real size.</summary>
    public static class ElLabels
    {
        static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _mats.Clear();

        /// <summary>Ink material for lines of text. Lines are laid in equal-height cells (cellLines = 0: one cell per line),
        /// left-aligned when 'left', else centered. px per inch sets the texture resolution.</summary>
        public static Material Text(string[] lines, float wIn, float hIn, Color ink, bool left = false, int cellLines = 0, int pxPerIn = 64, int scale = 0)
        {
            string key = string.Join("|", lines) + $"#{wIn:0.00}x{hIn:0.00}#{ink}#{left}#{cellLines}#{pxPerIn}#{scale}";
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            int w = Mathf.Max(8, Mathf.RoundToInt(wIn * pxPerIn)), h = Mathf.Max(8, Mathf.RoundToInt(hIn * pxPerIn));
            var tex = Render(lines, w, h, left, cellLines, scale);
            m = new Material(Shader.Find("Standard")) { name = "GB_ElText", hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.mainTexture = tex;
            m.color = ink;
            m.SetFloat("_Mode", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetFloat("_Glossiness", 0.1f);
            _mats[key] = m;
            return m;
        }

        static Texture2D Render(string[] lines, int w, int h, bool left, int cellLines, int scale)
        {
            int cells = cellLines > 0 ? cellLines : lines.Length;
            int maxLen = 1; foreach (var l in lines) maxLen = Mathf.Max(maxLen, l.Length);
            float cellH = h / (float)cells;
            int sc = scale > 0 ? scale : Mathf.Max(1, Mathf.Min((w - 4) / (maxLen * 6), Mathf.FloorToInt(cellH / 9f)));
            var a = new byte[w * h];
            for (int li = 0; li < lines.Length && li < cells; li++)
            {
                string l = lines[li];
                int lw = l.Length * 6 * sc - sc;
                int x0 = left ? 2 * sc : (w - lw) / 2;
                int yTop = Mathf.RoundToInt(li * cellH + (cellH - 7 * sc) * 0.5f);
                for (int ci = 0; ci < l.Length; ci++)
                {
                    var g = LumberMarks.Glyph(l[ci]);
                    if (g == null) continue;
                    for (int gy = 0; gy < 7; gy++)
                        for (int gx = 0; gx < 5; gx++)
                        {
                            if (g[gy][gx] != '#') continue;
                            for (int sy = 0; sy < sc; sy++)
                                for (int sx = 0; sx < sc; sx++)
                                {
                                    int px = x0 + ci * 6 * sc + gx * sc + sx, py = h - 1 - (yTop + gy * sc + sy);
                                    if (px >= 0 && px < w && py >= 0 && py < h) a[py * w + px] = 255;
                                }
                        }
                }
            }
            var px32 = new Color32[w * h];
            for (int i = 0; i < px32.Length; i++) px32[i] = new Color32(255, 255, 255, a[i]);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "GB_ElTextTex", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            t.SetPixels32(px32); t.Apply(true, true);
            return t;
        }

        /// <summary>Quads with explicit 0..1 UVs, one submesh per material. Coordinates are in the parent's local space (meters).</summary>
        public class Quads
        {
            readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<List<int>> _tris = new List<List<int>>();
            readonly List<Material> _mats = new List<Material>();

            /// <summary>Quad centered at c facing 'normal' (toward the viewer), text up along 'up'; w x h meters.</summary>
            public void Add(Material m, Vector3 c, Vector3 normal, Vector3 up, float w, float h)
            {
                normal.Normalize(); up = (up - normal * Vector3.Dot(up, normal)).normalized;
                Vector3 r = Vector3.Cross(normal, up);
                c += normal * 0.0003f;
                int i = _mats.IndexOf(m);
                if (i < 0) { _mats.Add(m); _tris.Add(new List<int>()); i = _mats.Count - 1; }
                int b = _v.Count;
                Vector3 hr = r * (w * 0.5f), hu = up * (h * 0.5f);
                _v.Add(c - hr - hu); _uv.Add(new Vector2(0, 0));
                _v.Add(c - hr + hu); _uv.Add(new Vector2(0, 1));
                _v.Add(c + hr + hu); _uv.Add(new Vector2(1, 1));
                _v.Add(c + hr - hu); _uv.Add(new Vector2(1, 0));
                for (int k = 0; k < 4; k++) _n.Add(normal);
                var t = _tris[i];
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }

            public GameObject Spawn(Transform parent, string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                if (_v.Count == 0) return go;
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_v); mesh.SetNormals(_n); mesh.SetUVs(0, _uv);
                mesh.subMeshCount = _tris.Count;
                for (int i = 0; i < _tris.Count; i++) mesh.SetTriangles(_tris[i], i);
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = _mats.ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off;
                return go;
            }
        }
    }
}
