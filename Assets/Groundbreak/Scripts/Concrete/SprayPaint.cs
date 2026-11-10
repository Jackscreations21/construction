using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Alpha-blended marking-paint material: a 16 cm spray blot with a dense core, fuzzy overspray and droplet speckles.
    /// Mesh UVs are in meters centred on the blot (radius 0.08 m), so tiling maps -0.08..0.08 m to 0..1.</summary>
    public static class SprayPaint
    {
        static readonly Dictionary<Color, Material> _mats = new Dictionary<Color, Material>();
        static Texture2D _tex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _mats.Clear();

        public static Material Get(Color c)
        {
            if (_mats.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(Shader.Find("Standard")) { name = "GB_SprayPaint", hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.SetFloat("_Mode", 2f); // Fade
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
            m.mainTexture = Tex;
            m.mainTextureScale = Vector2.one / 0.16f;
            m.mainTextureOffset = Vector2.one * 0.5f;
            m.color = new Color(c.r * 0.86f, c.g * 0.86f, c.b * 0.86f, 1f); // matte paint on soil never reads pure white
            m.SetFloat("_Glossiness", 0.15f);
            _mats[c] = m;
            return m;
        }

        static Texture2D Tex
        {
            get
            {
                if (_tex != null) return _tex;
                const int n = 128;
                var px = new Color32[n * n];
                var rng = new System.Random(77);
                var a = new float[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float ang = Mathf.Atan2(dy, dx), d = Mathf.Sqrt(dx * dx + dy * dy);
                        float wob = 0.64f + 0.04f * Mathf.Sin(ang * 3f + 0.7f) + 0.03f * Mathf.Sin(ang * 7f + 2f);
                        float core = Mathf.Clamp01((wob - d) / 0.18f);
                        float haze = Mathf.Clamp01(1f - d) * 0.35f;
                        float grain = 0.75f + 0.25f * (float)rng.NextDouble();
                        a[y * n + x] = Mathf.Clamp01(Mathf.Max(core * grain, haze * grain));
                    }
                for (int i = 0; i < 260; i++) // overspray droplets
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f, d = 0.5f + 0.45f * (float)rng.NextDouble();
                    int cx = (int)((Mathf.Cos(ang) * d * 0.5f + 0.5f) * n), cy = (int)((Mathf.Sin(ang) * d * 0.5f + 0.5f) * n);
                    if (cx >= 0 && cy >= 0 && cx < n && cy < n) a[cy * n + cx] = Mathf.Max(a[cy * n + cx], 0.6f + 0.4f * (float)rng.NextDouble());
                }
                for (int i = 0; i < n * n; i++) px[i] = new Color32(255, 255, 255, (byte)(a[i] * 235));
                _tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "GB_SprayBlot", wrapMode = TextureWrapMode.Clamp };
                _tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _tex.SetPixels32(px); _tex.Apply(true, true);
                return _tex;
            }
        }
    }
}
