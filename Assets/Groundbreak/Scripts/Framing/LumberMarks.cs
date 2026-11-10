using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>Mill marks on raw lumber and sheathing: ink grade stamps (procedural bitmap-font text, worn ink),
    /// per-board tone variation, and the nail heads left behind by the hammer/nailer.
    /// Stamps are alpha-cutout decal quads laid 0.2 mm off the face, built into the piece's cached mesh.</summary>
    public static class LumberMarks
    {
        // ------------------------------------------------------------------ board tone

        /// <summary>Bunks of SPF mix whiter spruce, yellower pine and pinker fir: subtle per-board multiply tints.</summary>
        static readonly Color[] Tones =
        {
            new Color(1.00f, 1.00f, 1.00f), new Color(1.04f, 1.03f, 1.01f), new Color(0.97f, 0.94f, 0.89f),
            new Color(1.01f, 0.97f, 0.90f), new Color(0.93f, 0.89f, 0.83f), new Color(1.02f, 1.01f, 0.97f),
            new Color(0.99f, 0.94f, 0.90f), new Color(0.95f, 0.93f, 0.90f),
        };
        public static Color Tone(int variant) => Tones[((variant % Tones.Length) + Tones.Length) % Tones.Length];

        // ------------------------------------------------------------------ stamps

        public enum Stamp { LumberSpf, LumberTreated, Sheathing }

        const float LumW = 0.105f, LumH = 0.060f;     // ~4" x 2-3/8" grade stamp
        const float ShW = 0.300f, ShH = 0.200f;       // ~12" x 8" panel stamp

        public static Vector2 StampSize(Stamp s) => s == Stamp.Sheathing ? new Vector2(ShW, ShH) : new Vector2(LumW, LumH);

        static readonly Dictionary<Stamp, Material> _mats = new Dictionary<Stamp, Material>();

        public static Material StampMaterial(Stamp s)
        {
            if (_mats.TryGetValue(s, out var m) && m != null) return m;
            Vector2 size = StampSize(s);
            Texture2D tex;
            Color ink;
            switch (s)
            {
                case Stamp.LumberTreated:
                    ink = new Color(0.10f, 0.12f, 0.30f);
                    tex = Render(new[] { "GROUND", "CONTACT", "ACQ .40 PCF", "KDAT  MILL 14" }, 192, 112, 9011, true);
                    break;
                case Stamp.Sheathing:
                    ink = new Color(0.10f, 0.16f, 0.38f);
                    tex = Render(new[] { "RATED SHEATHING", "24/16", "7/16 CATEGORY", "EXPOSURE 1", "SIZED FOR SPACING", "PS 2-10  MILL 412" }, 256, 168, 9021, true);
                    break;
                default:
                    ink = new Color(0.12f, 0.30f, 0.16f);
                    tex = Render(new[] { "S-P-F", "NO.2", "S-DRY  27" }, 192, 112, 9031, true);
                    break;
            }
            var sh = Shader.Find("Standard");
            m = new Material(sh) { name = "GB_Stamp_" + s, hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.mainTexture = tex;
            m.color = ink;
            m.SetFloat("_Mode", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.EnableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetFloat("_Glossiness", 0.12f);
            m.mainTextureScale = new Vector2(1f / size.x, 1f / size.y);
            _mats[s] = m;
            return m;
        }

        /// <summary>Adds a stamp quad on the face whose outward normal is n, centered at c, U along 'along'.
        /// UVs: MeshBuilder writes meters + UvOffset, so we shift UvOffset so the quad spans exactly one tile.</summary>
        public static void AddStamp(MeshBuilder b, int sub, Stamp s, Vector3 c, Vector3 n, Vector3 along, bool flip)
        {
            Vector2 size = StampSize(s);
            n.Normalize(); along = (along - n * Vector3.Dot(along, n)).normalized;
            if (flip) along = -along;
            Vector3 across = Vector3.Cross(n, along);
            c += n * 0.0002f;
            float hu = size.x * 0.5f, hv = size.y * 0.5f;
            Vector3 a = c - along * hu - across * hv, bb = c + along * hu - across * hv, cc = c + along * hu + across * hv, d = c - along * hu + across * hv;
            // MeshBuilder projects onto (u = along, v = n x u = across); make UV(a) = (0,0).
            Vector2 old = b.UvOffset;
            b.UvOffset = new Vector2(-Vector3.Dot(a, along), -Vector3.Dot(a, across));
            b.AddQuad(a, bb, cc, d, sub, n, along);
            b.UvOffset = old;
        }

        // ------------------------------------------------------------------ texture

        /// <summary>Rasterizes lines of 5x7 glyphs (centered, auto-fit) into an alpha texture with a worn-ink look.</summary>
        static Texture2D Render(string[] lines, int w, int h, int seed, bool border)
        {
            var a = new float[w * h];
            int maxLen = 0; foreach (var l in lines) maxLen = Mathf.Max(maxLen, l.Length);
            int margin = border ? 10 : 4;
            int scale = Mathf.Max(1, Mathf.Min((w - margin * 2) / (maxLen * 6), (h - margin * 2) / (lines.Length * 9)));
            int blockH = lines.Length * 9 * scale - 2 * scale;
            int y0 = (h - blockH) / 2;
            for (int li = 0; li < lines.Length; li++)
            {
                string l = lines[li];
                // first line bigger when there is room (the species / product name)
                int sc = scale;
                if (li == 0 && lines.Length <= 4 && l.Length * 6 * (scale + 1) <= w - margin * 2) sc = scale + 1;
                int lw = l.Length * 6 * sc - sc;
                int x0 = (w - lw) / 2;
                int yTop = y0 + li * 9 * scale - (sc - scale) * 4;
                for (int ci = 0; ci < l.Length; ci++)
                {
                    var g = Glyph(l[ci]);
                    if (g == null) continue;
                    for (int gy = 0; gy < 7; gy++)
                        for (int gx = 0; gx < 5; gx++)
                        {
                            if (g[gy][gx] != '#') continue;
                            for (int sy = 0; sy < sc; sy++)
                                for (int sx = 0; sx < sc; sx++)
                                {
                                    // MeshBuilder's face UV basis (u, n x u) is mirrored in Unity's left-handed space: mirror X here.
                                    int px = w - 1 - (x0 + ci * 6 * sc + gx * sc + sx);
                                    int py = h - 1 - (yTop + gy * sc + sy); // texture rows go bottom-up
                                    if (px >= 0 && px < w && py >= 0 && py < h) a[py * w + px] = 1f;
                                }
                        }
                }
            }
            if (border)
            {
                int t = Mathf.Max(2, scale);
                for (int y = 3; y < h - 3; y++)
                    for (int x = 3; x < w - 3; x++)
                        if (x < 3 + t || x >= w - 3 - t || y < 3 + t || y >= h - 3 - t) a[y * w + x] = 1f;
            }
            // worn ink: low-frequency pressure falloff + speckle dropouts
            var px32 = new Color32[w * h];
            float ox = ShellMaterials.Hash(seed, 1, 3) * 50f, oy = ShellMaterials.Hash(seed, 2, 3) * 50f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float pressure = Mathf.PerlinNoise(ox + x * 0.03f, oy + y * 0.03f);
                    float speck = ShellMaterials.Hash(x, y, seed);
                    float cov = a[i] * (pressure * 1.3f + 0.15f) - (speck < 0.18f ? 0.6f : 0f);
                    byte al = (byte)(cov > 0.45f ? 255 : 0);
                    byte lum = (byte)Mathf.Clamp(200 + (int)(55 * pressure), 0, 255);
                    px32[i] = new Color32(lum, lum, lum, al);
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "GB_StampTex_" + seed, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            tex.SetPixels32(px32);
            tex.Apply(true, true);
            return tex;
        }

        static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
            ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
            ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
            ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
            ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
            ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#." },
            ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
            ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
            ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
            ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            ['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
            ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
            ['/'] = new[] { "....#", "....#", "...#.", "..#..", ".#...", "#....", "#...." },
            ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
            ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." },
        };

        public static string[] Glyph(char ch) => Font.TryGetValue(char.ToUpperInvariant(ch), out var g) ? g : null;

        // ------------------------------------------------------------------ nail heads

        static Mesh _nailMesh;
        static Material _nailMat;

        /// <summary>Leaves a driven nail head (0.281" 16d sinker head, set just proud of the surface) at a hit point, parented to the piece.</summary>
        public static GameObject NailHead(Piece p, RaycastHit hit)
        {
            if (p == null || hit.collider == null) return null;
            if (_nailMesh == null)
            {
                var b = new MeshBuilder();
                float r = 0.281f * 0.5f * Units.Inch;
                b.AddCylinder(new Vector3(0, 0.0004f, 0), r, 0.0008f, 12, 0, true);
                _nailMesh = b.Build("NailHead");
            }
            if (_nailMat == null) _nailMat = Materials.Get(SurfaceKind.PaintedSteel, new Color(0.66f, 0.66f, 0.64f)); // bright steel head, not mirror-metal
            var go = new GameObject("NailHead");
            go.transform.SetParent(p.transform, true);
            go.transform.position = hit.point;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            go.AddComponent<MeshFilter>().sharedMesh = _nailMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _nailMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }
    }
}
