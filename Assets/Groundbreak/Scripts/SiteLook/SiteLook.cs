using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>
    /// Global outdoor look: ground materials with anti-tiling + large-scale patch variation ("Groundbreak/Terrain" shader),
    /// time-of-day sun colour, ambient and fog matched to the sky horizon, and higher quality sun shadows.
    /// Ground kinds (Grass/Dirt/Gravel) are supplied through the <see cref="Materials.Custom"/> extension point.
    /// </summary>
    [DefaultExecutionOrder(900)] // after GameClock (sun intensity) and Weather (fog), before LateUpdate consumers
    public class SiteLook : MonoBehaviour
    {
        static Texture2D _macro;
        static Shader _terrain;

        // ------------------------------------------------------------------ materials

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Materials.Custom[SurfaceKind.Grass] = () => Ground(SurfaceKind.Grass);
            Materials.Custom[SurfaceKind.Dirt] = () => Ground(SurfaceKind.Dirt);
            Materials.Custom[SurfaceKind.Gravel] = () => Ground(SurfaceKind.Gravel);
        }

        public static Shader TerrainShader
        {
            get
            {
                if (_terrain == null) _terrain = Shader.Find("Groundbreak/Terrain");
                return _terrain;
            }
        }

        static Material Ground(SurfaceKind kind)
        {
            var set = ProceduralTextures.Get(kind);
            float gloss = kind == SurfaceKind.Grass ? 0.2f : kind == SurfaceKind.Dirt ? 0.06f : 0.15f;
            var m = MakeTerrain("GB_" + kind, set.Albedo, set.Normal, 1f, gloss, 1f / Materials.TileMeters(kind));
            switch (kind)
            {
                case SurfaceKind.Grass:
                    // lush/darker vs. sun-dried yellow-green patches; slightly toned down overall (the texture is bright)
                    Style(m, new Color(1.10f, 1.02f, 0.74f), new Color(0.78f, 0.92f, 0.78f), 0.55f, new Color(0.86f, 0.88f, 0.84f));
                    break;
                case SurfaceKind.Dirt:
                    Style(m, new Color(1.08f, 1.0f, 0.92f), new Color(0.80f, 0.76f, 0.74f), 0.45f, Color.white);
                    break;
                default:
                    Style(m, new Color(1.04f, 1.02f, 0.98f), new Color(0.86f, 0.87f, 0.90f), 0.35f, Color.white);
                    break;
            }
            return m;
        }

        /// <summary>A material on the terrain shader (falls back to Standard). Properties match Standard's names.</summary>
        public static Material MakeTerrain(string name, Texture albedo, Texture normal, float bump, float gloss, float tiling)
        {
            var sh = TerrainShader != null ? TerrainShader : Shader.Find("Standard");
            var m = new Material(sh) { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.mainTexture = albedo;
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_BumpScale", bump);
            m.SetFloat("_Glossiness", gloss);
            m.SetFloat("_GlossMapScale", Mathf.Min(2f, gloss * 2f));
            m.SetFloat("_SmoothnessTextureChannel", 1f);
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_Metallic", 0f);
            m.SetTexture("_MacroTex", Macro);
            m.mainTextureScale = Vector2.one * tiling;
            m.SetFloat("_WorldTile", tiling); // world-projected texture on steep cut faces
            m.enableInstancing = true;
            return m;
        }

        public static void Style(Material m, Color tintA, Color tintB, float strength, Color baseColor)
        {
            m.SetColor("_TintA", tintA); m.SetColor("_TintB", tintB);
            m.SetFloat("_MacroStrength", strength);
            m.color = baseColor;
        }

        /// <summary>256 px tileable fBm value noise, three independent fields in R, G, B.</summary>
        public static Texture2D Macro
        {
            get
            {
                if (_macro != null) return _macro;
                const int n = 256;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = x / (float)n, v = y / (float)n;
                        px[y * n + x] = new Color32((byte)(Fbm(u, v, 4, 11) * 255), (byte)(Fbm(u, v, 5, 23) * 255), (byte)(Fbm(u, v, 3, 37) * 255), 255);
                    }
                _macro = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { name = "GB_TerrainMacro", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
                _macro.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _macro.SetPixels32(px); _macro.Apply(true, true);
                return _macro;
            }
        }

        static float Fbm(float u, float v, int baseCells, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f; int p = baseCells;
            for (int o = 0; o < 5; o++) { sum += amp * Value(u * p, v * p, p, seed + o * 101); norm += amp; amp *= 0.5f; p *= 2; }
            float r = sum / norm;
            return Mathf.Clamp01((r - 0.5f) * 1.6f + 0.5f); // spread the histogram
        }

        static float Value(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float a = H(x0, y0, period, seed), b = H(x0 + 1, y0, period, seed), c = H(x0, y0 + 1, period, seed), d = H(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        static float H(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period; y = ((y % period) + period) % period;
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }

        // ------------------------------------------------------------------ lighting / atmosphere

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<SiteLook>() != null) return;
            var go = new GameObject("SiteLook");
            DontDestroyOnLoad(go);
            go.AddComponent<SiteLook>();
            ConfigureShadows();
            BuildFarGround();
            UpdateAtmosphere();
        }

        /// <summary>The bootstrap ground plane ends 1000' out, which showed as a hard line against the sky. A grass ring continues
        /// it to 1.2 km, where the haze is fully opaque, so the horizon dissolves into fog.</summary>
        static void BuildFarGround()
        {
            var g = GameObject.Find("Ground");
            if (g == null || GameObject.Find("FarGround") != null) return;
            float half = 5f * g.transform.lossyScale.x, far = 1200f;
            var b = new MeshBuilder();
            // four slabs around the plane's square: south, north (full width), west, east (between them)
            b.AddQuad(new Vector3(-far, 0, -far), new Vector3(-far, 0, -half), new Vector3(far, 0, -half), new Vector3(far, 0, -far), 0, Vector3.up);
            b.AddQuad(new Vector3(-far, 0, half), new Vector3(-far, 0, far), new Vector3(far, 0, far), new Vector3(far, 0, half), 0, Vector3.up);
            b.AddQuad(new Vector3(-far, 0, -half), new Vector3(-far, 0, half), new Vector3(-half, 0, half), new Vector3(-half, 0, -half), 0, Vector3.up);
            b.AddQuad(new Vector3(half, 0, -half), new Vector3(half, 0, half), new Vector3(far, 0, half), new Vector3(far, 0, -half), 0, Vector3.up);
            var mesh = b.Build("FarGround");
            var go = new GameObject("FarGround");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Materials.Get(SurfaceKind.Grass);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        public static void ConfigureShadows()
        {
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 120f;
            QualitySettings.shadowCascade4Split = new Vector3(0.05f, 0.16f, 0.42f);
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            var sun = RenderSettings.sun;
            if (sun != null) { sun.shadowBias = 0.04f; sun.shadowNormalBias = 0.3f; sun.shadowStrength = 0.9f; }
        }

        static readonly Color NoonSun = new Color(1.00f, 0.95f, 0.86f);
        static readonly Color LowSun = new Color(1.00f, 0.68f, 0.42f);
        static readonly Color NoonSky = new Color(0.56f, 0.68f, 0.86f), NoonEquator = new Color(0.62f, 0.64f, 0.62f), NoonGround = new Color(0.27f, 0.25f, 0.20f);
        static readonly Color ClearHaze = new Color(0.76f, 0.82f, 0.88f), RainHaze = new Color(0.52f, 0.55f, 0.59f), DuskHaze = new Color(0.80f, 0.66f, 0.55f);

        /// <summary>0 at night, 1 with the sun well up (derived from the sun's elevation).</summary>
        public static float Daylight { get; private set; } = 1f;

        void LateUpdate() => UpdateAtmosphere();

        public static void UpdateAtmosphere()
        {
            var sun = RenderSettings.sun;
            float elev = 50f;
            if (sun != null) elev = Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float day = Mathf.Clamp01((elev + 5f) / 11f);       // twilight → full day over -5°..6°
            float low = 1f - Mathf.Clamp01((elev - 4f) / 26f);  // warm light under ~30° elevation
            Daylight = day;
            bool rain = Weather.IsRaining;

            if (sun != null)
            {
                sun.color = Color.Lerp(NoonSun, LowSun, low * low);
                if (rain) sun.color = Color.Lerp(sun.color, new Color(0.8f, 0.84f, 0.9f), 0.6f);
                sun.shadowStrength = rain ? 0.45f : 0.9f;
                // low sun is still strong direct light (GameClock's curve dims it too early): ramp over -2°..18° elevation
                float k = Mathf.Clamp01((elev + 2f) / 20f);
                sun.intensity = Mathf.Lerp(0.03f, 1.15f, k * k * (3f - 2f * k)) * (rain ? 0.35f : 1f);
            }

            float amb = Mathf.Lerp(0.08f, 1f, day) * (rain ? 0.75f : 1f);
            Color warm = Color.Lerp(Color.white, new Color(1.05f, 0.88f, 0.78f), low * 0.6f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = NoonSky * amb;
            RenderSettings.ambientEquatorColor = NoonEquator * warm * amb;
            RenderSettings.ambientGroundColor = NoonGround * amb;
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.15f, 0.8f, day);

            // exponential-squared haze reads as a soft horizon instead of linear fog's hard band
            Color haze = Color.Lerp(ClearHaze, DuskHaze, low * 0.7f);
            if (rain) haze = RainHaze;
            haze *= Mathf.Lerp(0.12f, 1f, day);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = rain ? 0.009f : 0.0042f;
            RenderSettings.fogColor = haze;

            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_Exposure"))
            {
                sky.SetFloat("_Exposure", Mathf.Lerp(0.08f, rain ? 0.7f : 1.15f, day));
                sky.SetFloat("_AtmosphereThickness", rain ? 1.6f : 0.95f);
                sky.SetColor("_SkyTint", rain ? new Color(0.42f, 0.44f, 0.46f) : new Color(0.46f, 0.50f, 0.56f));
                sky.SetColor("_GroundColor", new Color(0.36f, 0.38f, 0.32f));
            }
        }
    }
}
