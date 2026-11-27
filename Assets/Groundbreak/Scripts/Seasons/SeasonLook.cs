using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// What the seasons and weather look like: lawn/field/foliage colours per season (summer = the stock look), bare deciduous trees in winter
    /// (foliage materials switch to an alpha-tested twig texture; evergreens stay), snow cover blended into ground and roof textures, wet dark
    /// glossy ground and pavement in rain, and the sun path / cloud / storm light. Works on the shared materials in place (remembers and restores
    /// the originals) and only re-applies when the target look changes. Runs after <see cref="SiteLook"/>'s atmosphere pass.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class SeasonLook : MonoBehaviour
    {
        public static SeasonLook I;
        /// <summary>Last applied look (for tests): season, snow level 0..4, wet level 0..2.</summary>
        public static string LookKey { get; private set; } = "";

        enum Kind { Grass, Dirt, Crop, Foliage, Evergreen, Roof, Hard }

        class Orig
        {
            public Material M; public Kind K;
            public Color Color; public Texture Tex; public Vector2 Scale; public string[] Keywords; public int Queue; public string RenderType;
            public bool Terrain; public Color TintA, TintB; public float Gloss, GlossMap; public bool HasGloss, HasGlossMap;
        }

        static readonly List<Orig> _mats = new List<Orig>();
        static readonly HashSet<Material> _seen = new HashSet<Material>();
        static readonly Dictionary<(Texture, int), Texture2D> _snowTex = new Dictionary<(Texture, int), Texture2D>();
        static Texture2D _twigs, _snowAlb;
        static Material _snowMat, _iceMat;
        float _nextScan; string _applied = null; int _appliedCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("SeasonLook");
            DontDestroyOnLoad(go);
            go.AddComponent<SeasonLook>();
            go.AddComponent<WeatherFx>();
        }

        void Awake() { I = this; }
        void OnDestroy() { RestoreAll(); if (I == this) I = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _mats.Clear(); _seen.Clear(); _snowTex.Clear(); LookKey = ""; }

        static bool On => GameSettings.WeatherEnabled;

        // ------------------------------------------------------------------ shared assets

        /// <summary>Packed snow: white with a faint blue cast and sparkle (UVs in metres, 1 m tile).</summary>
        public static Material SnowMaterial
        {
            get
            {
                if (_snowMat != null) return _snowMat;
                _snowMat = new Material(Shader.Find("Standard")) { name = "GB_Snow", hideFlags = HideFlags.DontUnloadUnusedAsset };
                _snowMat.mainTexture = SnowAlbedo; _snowMat.color = Color.white;
                _snowMat.SetFloat("_Glossiness", 0.35f); _snowMat.SetFloat("_Metallic", 0f);
                return _snowMat;
            }
        }

        /// <summary>Clear blue-white ice (icicles, ice dams).</summary>
        public static Material IceMaterial
        {
            get
            {
                if (_iceMat != null) return _iceMat;
                _iceMat = new Material(Shader.Find("Standard")) { name = "GB_Ice", hideFlags = HideFlags.DontUnloadUnusedAsset, color = new Color(0.80f, 0.90f, 0.97f) };
                _iceMat.SetFloat("_Glossiness", 0.92f); _iceMat.SetFloat("_Metallic", 0f);
                return _iceMat;
            }
        }

        static Texture2D SnowAlbedo
        {
            get
            {
                if (_snowAlb != null) return _snowAlb;
                const int n = 128; var px = new Color[n * n]; var r = new System.Random(5);
                for (int i = 0; i < px.Length; i++)
                {
                    float v = 0.90f + (float)r.NextDouble() * 0.06f;
                    px[i] = new Color(v * 0.97f, v * 0.985f, v, 0.35f);
                    if (r.NextDouble() < 0.004) px[i] = new Color(1f, 1f, 1f, 0.9f);   // glints
                }
                _snowAlb = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "GB_SnowAlbedo", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
                _snowAlb.SetPixels(px); _snowAlb.Apply(true);
                return _snowAlb;
            }
        }

        static float Hash(int x, int y, int s) { uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777215f; }
        static float VNoise(float x, float y, int period, int s)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            int X(int v) => ((v % period) + period) % period;
            return Mathf.Lerp(Mathf.Lerp(Hash(X(x0), X(y0), s), Hash(X(x0 + 1), X(y0), s), tx), Mathf.Lerp(Hash(X(x0), X(y0 + 1), s), Hash(X(x0 + 1), X(y0 + 1), s), tx), ty);
        }

        /// <summary>Bare winter crown: tileable alpha-tested branch/twig network, mips keep their coverage so distant trees stay a grey-brown haze.</summary>
        static Texture2D Twigs
        {
            get
            {
                if (_twigs != null) return _twigs;
                const int n = 256; var a = new float[n * n]; var c = new float[n * n];
                var r = new System.Random(77);
                void Dot(float x, float y, float rad, float shade)
                {
                    int ri = Mathf.CeilToInt(rad);
                    for (int dy = -ri; dy <= ri; dy++)
                        for (int dx = -ri; dx <= ri; dx++)
                        {
                            float d = Mathf.Sqrt(dx * dx + dy * dy); if (d > rad + 0.5f) continue;
                            int px = ((Mathf.RoundToInt(x) + dx) % n + n) % n, py = ((Mathf.RoundToInt(y) + dy) % n + n) % n;
                            int i = py * n + px; a[i] = 1f; c[i] = shade;
                        }
                }
                void Branch(float x, float y, float ang, float len, float w, int depth)
                {
                    float step = 1f; int steps = Mathf.CeilToInt(len / step); float shade = 0.75f + (float)r.NextDouble() * 0.3f;
                    for (int s = 0; s < steps; s++)
                    {
                        ang += ((float)r.NextDouble() - 0.5f) * 0.25f;
                        x += Mathf.Cos(ang) * step; y += Mathf.Sin(ang) * step;
                        Dot(x, y, Mathf.Lerp(w, w * 0.5f, s / (float)steps), shade);
                        if (depth > 0 && r.NextDouble() < 0.07) Branch(x, y, ang + (r.NextDouble() < 0.5 ? -1 : 1) * (0.4f + (float)r.NextDouble() * 0.6f), len * 0.55f, w * 0.6f, depth - 1);
                    }
                }
                for (int i = 0; i < 15; i++) Branch((float)r.NextDouble() * n, (float)r.NextDouble() * n, (float)(r.NextDouble() * Mathf.PI * 2), 60f + (float)r.NextDouble() * 70f, 2.0f, 3);
                _twigs = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "GB_WinterTwigs", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                var col = new Color[n * n];
                for (int i = 0; i < col.Length; i++) { float s = c[i]; col[i] = new Color(0.30f * s, 0.27f * s, 0.24f * s, a[i]); }
                // mip chain by hand: colour average, alpha boosted so coverage survives into the distance
                int size = n, level = 0; var cur = col;
                while (true)
                {
                    _twigs.SetPixels(cur, level);
                    if (size == 1) break;
                    int ns = size / 2; var next = new Color[ns * ns];
                    for (int y = 0; y < ns; y++)
                        for (int x = 0; x < ns; x++)
                        {
                            Color s = cur[(2 * y) * size + 2 * x] + cur[(2 * y) * size + 2 * x + 1] + cur[(2 * y + 1) * size + 2 * x] + cur[(2 * y + 1) * size + 2 * x + 1];
                            s /= 4f;
                            // keep coverage at distance as a dithered see-through speckle (a far bare crown is a grey-brown haze)
                            float cov = Mathf.Clamp01(s.a * 1.1f), al = Hash(x, y, 31 + level) < cov ? 1f : 0f;
                            next[y * ns + x] = new Color(0.27f, 0.25f, 0.22f, al);
                        }
                    cur = next; size = ns; level++;
                }
                _twigs.Apply(false);
                return _twigs;
            }
        }

        /// <summary>The source albedo with snow patches blended over it (level 1..4 = light dusting .. full cover). Cached.</summary>
        static Texture2D SnowBlend(Texture src, int level)
        {
            if (src == null) return SnowAlbedo;
            var key = (src, level);
            if (_snowTex.TryGetValue(key, out var t) && t != null) return t;
            int n = Mathf.Min(256, Mathf.Max(64, src.width));
            var rt = RenderTexture.GetTemporary(n, n, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(n, n, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, n, n), 0, 0); read.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = read.GetPixels(); Destroy(read);
            float cover = level / 4f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float noise = 0.55f * VNoise(u * 4, v * 4, 4, 3) + 0.3f * VNoise(u * 11, v * 11, 11, 4) + 0.15f * VNoise(u * 29, v * 29, 29, 5);
                    float k = Mathf.Clamp01((cover * 1.25f - noise) * 6f + (cover >= 1f ? 1f : 0f));
                    float sv = 0.90f + Hash(x, y, 9) * 0.06f;
                    var s = new Color(sv * 0.97f, sv * 0.985f, sv, 0.3f);
                    px[y * n + x] = Color.Lerp(px[y * n + x], s, k);
                }
            t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = src.name + "_Snow" + level, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels(px); t.Apply(true);
            _snowTex[key] = t;
            return t;
        }

        // ------------------------------------------------------------------ material registry

        static Material _overcast, _clearSky;
        static void RestoreSky() { if (_clearSky != null && RenderSettings.skybox == _overcast) RenderSettings.skybox = _clearSky; }
        /// <summary>Flat overcast sky: a lat-long gradient (darker grey overhead, lighter at the horizon).</summary>
        static Material Overcast
        {
            get
            {
                if (_overcast != null) return _overcast;
                var tex = new Texture2D(4, 64, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = "OvercastSky" };
                for (int y = 0; y < 64; y++)
                {
                    float v = y / 63f;   // 0 = straight down, 0.5 = horizon, 1 = zenith
                    float g = v < 0.5f ? 0.5f : Mathf.Lerp(0.74f, 0.56f, (v - 0.5f) * 2f);
                    for (int x = 0; x < 4; x++) tex.SetPixel(x, y, new Color(g, g * 1.01f, g * 1.04f));
                }
                tex.Apply();
                var sh = Shader.Find("Skybox/Panoramic");
                _overcast = new Material(sh) { name = "GB_OvercastSky", mainTexture = tex };
                if (_overcast.HasProperty("_Mapping")) _overcast.SetFloat("_Mapping", 1f);
                _overcast.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
                return _overcast;
            }
        }
        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        static bool Classify(Material m, out Kind k)
        {
            k = Kind.Hard; string n = m.name;
            if (n.StartsWith("GB_Grass") || n.StartsWith("GBL_Sod") || n == "fieldHay" || n == "fieldPasture" || n.StartsWith("MapField_Hay") || n.StartsWith("MapField_Pasture")) { k = Kind.Grass; return true; }
            if (n.StartsWith("GB_Dirt") || n.StartsWith("GBL_Topsoil") || n.StartsWith("MapField_Plowed") || n == "fieldPlowed") { k = Kind.Dirt; return true; }
            if (n.StartsWith("MapField_") || n.StartsWith("field")) { k = Kind.Crop; return true; }
            if (n.StartsWith("GBL_Foliage"))
            {
                var c = m.color; float g = Mathf.Max(0.01f, c.g);
                k = c.b / g >= 0.75f && c.r / g <= 0.83f ? Kind.Evergreen : Kind.Foliage;   // spruce / fir / pine tints are blue-green
                return true;
            }
            // vacant-lot meadow tufts (MapFollowup/VacantLots: plain MaterialLibrary colours, unnamed)
            if (n.Length == 0 && (Near(m.color, new Color(0.36f, 0.50f, 0.22f)) || Near(m.color, new Color(0.26f, 0.38f, 0.16f)))) { k = Kind.Grass; return true; }
            string l = n.ToLowerInvariant();
            if (l.Contains("shingle")) { k = Kind.Roof; return true; }
            if (n.StartsWith("GBL_Asphalt") || n.StartsWith("GB_Concrete") || n.StartsWith("GB_Gravel") || n.StartsWith("GB_BroomedConcrete")) { k = Kind.Hard; return true; }
            return false;
        }

        void Scan()
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || !_seen.Add(m)) continue;
                if (m.name.EndsWith("_Snow") || m.name == "GB_Snow") continue;
                if (!Classify(m, out var k)) continue;
                var o = new Orig
                {
                    M = m, K = k, Color = m.color, Tex = m.mainTexture, Scale = m.mainTextureScale, Keywords = m.shaderKeywords, Queue = m.renderQueue,
                    RenderType = m.GetTag("RenderType", false, ""), Terrain = m.HasProperty("_TintA"),
                    HasGloss = m.HasProperty("_Glossiness"), HasGlossMap = m.HasProperty("_GlossMapScale")
                };
                if (o.Terrain) { o.TintA = m.GetColor("_TintA"); o.TintB = m.GetColor("_TintB"); }
                if (o.HasGloss) o.Gloss = m.GetFloat("_Glossiness");
                if (o.HasGlossMap) o.GlossMap = m.GetFloat("_GlossMapScale");
                _mats.Add(o);
            }
        }

        static void Restore(Orig o)
        {
            var m = o.M; if (m == null) return;
            m.color = o.Color; m.mainTexture = o.Tex; m.mainTextureScale = o.Scale; m.shaderKeywords = o.Keywords; m.renderQueue = o.Queue;
            m.SetOverrideTag("RenderType", o.RenderType);
            if (o.Terrain) { m.SetColor("_TintA", o.TintA); m.SetColor("_TintB", o.TintB); }
            if (o.HasGloss) m.SetFloat("_Glossiness", o.Gloss);
            if (o.HasGlossMap) m.SetFloat("_GlossMapScale", o.GlossMap);
        }

        /// <summary>Tests: pick up newly created materials and re-apply the current look now.</summary>
        public static void Refresh() { if (I == null) return; I.Scan(); I._applied = null; I.LateUpdate(); }

        void RestoreAll() { RestoreSky(); foreach (var o in _mats) Restore(o); _applied = null; LookKey = ""; }

        // ------------------------------------------------------------------ per-frame

        void Update()
        {
            if (!On) return;
            SunPath();
        }

        void LateUpdate()
        {
            if (!On) { if (_applied != null) RestoreAll(); return; }
            if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 2.5f; Scan(); }
            var g = SeasonWeather.Ground;
            int snow = g.SnowIn < 0.5f ? 0 : g.SnowIn < 1.5f ? 1 : g.SnowIn < 3f ? 2 : g.SnowIn < 6f ? 3 : 4;
            int wet = SeasonWeather.Raining ? 2 : (g.Wetness > 0.35f && !g.Frozen) || (SeasonWeather.Snowing == false && g.SnowIn > 0.5f && SeasonWeather.TempF > 34f) ? 1 : 0;
            string key = $"{SeasonWeather.Season}/{snow}/{wet}";
            if (key != _applied || _mats.Count != _appliedCount) { Apply(SeasonWeather.Season, snow, wet); _applied = key; _appliedCount = _mats.Count; LookKey = key; }
            Atmosphere(g, snow);
        }

        static readonly Color[] FallLeaves =
        {
            new Color(1.40f, 0.60f, 0.16f), new Color(1.20f, 0.28f, 0.14f), new Color(1.35f, 1.05f, 0.22f), new Color(1.05f, 0.45f, 0.18f),
            new Color(1.30f, 0.82f, 0.26f), new Color(0.92f, 0.95f, 0.42f)
        };

        static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);

        static void Apply(Seasons season, int snow, int wet)
        {
            float wetDark = wet == 2 ? 0.72f : wet == 1 ? 0.85f : 1f, wetGloss = wet == 2 ? 0.45f : wet == 1 ? 0.2f : 0f;
            foreach (var o in _mats)
            {
                var m = o.M; if (m == null) continue;
                Restore(o);
                switch (o.K)
                {
                    case Kind.Grass:
                    case Kind.Dirt:
                    case Kind.Crop:
                    {
                        Color t = Color.white;
                        if (o.K == Kind.Grass) t = season == Seasons.Spring ? new Color(1.06f, 1.20f, 0.70f) : season == Seasons.Fall ? new Color(1.10f, 0.96f, 0.72f) : season == Seasons.Winter ? new Color(1.18f, 0.92f, 0.62f) : Color.white;
                        if (o.K == Kind.Crop) t = season == Seasons.Spring ? new Color(0.85f, 0.80f, 0.70f) : season == Seasons.Fall ? new Color(1.25f, 0.95f, 0.55f) : season == Seasons.Winter ? new Color(1.05f, 0.85f, 0.60f) : Color.white;
                        var c = Mul(o.Color, t) * wetDark; c.a = o.Color.a;
                        if (snow > 0)
                        {
                            m.mainTexture = SnowBlend(o.Tex, snow);
                            float s = snow / 4f;
                            c = Color.Lerp(c, new Color(1f, 1f, 1f, c.a), 0.5f + 0.5f * s);
                            if (o.Terrain) { m.SetColor("_TintA", Color.Lerp(o.TintA, Color.white, 0.4f + 0.5f * s)); m.SetColor("_TintB", Color.Lerp(o.TintB, Color.white, 0.4f + 0.5f * s)); }
                        }
                        m.color = c;
                        if (wet > 0 && snow == 0) Wet(o, wetGloss);
                        break;
                    }
                    case Kind.Hard:
                        if (wet > 0) { var c = o.Color * Mathf.Lerp(1f, wetDark, 0.9f); c.a = o.Color.a; m.color = c; Wet(o, wetGloss + 0.1f); }
                        if (snow >= 3 && m.name.StartsWith("GB_Gravel")) m.mainTexture = SnowBlend(o.Tex, snow - 1);
                        break;
                    case Kind.Roof:
                        if (snow > 0) { m.mainTexture = SnowBlend(o.Tex, Mathf.Min(4, snow + 1)); m.color = Color.Lerp(o.Color, Color.white, 0.55f + 0.1f * snow); }
                        else if (wet == 2) { m.color = o.Color * 0.8f; Wet(o, 0.3f); }
                        break;
                    case Kind.Evergreen:
                        if (season == Seasons.Winter) m.color = o.Color * 0.88f;
                        break;
                    case Kind.Foliage:
                        if (season == Seasons.Spring) { var c = Mul(o.Color, new Color(1.28f, 1.32f, 0.60f)); c.a = 1f; m.color = c; }
                        else if (season == Seasons.Fall)
                        {
                            float lum = Mathf.Clamp(o.Color.g, 0.4f, 1.1f);
                            var pal = FallLeaves[(int)(Hash(Mathf.RoundToInt(o.Color.r * 1000), Mathf.RoundToInt(o.Color.b * 1000), 3) * FallLeaves.Length) % FallLeaves.Length];
                            var c = pal * Mathf.Lerp(lum, 0.9f, 0.5f); c.a = 1f; m.color = c;
                        }
                        else if (season == Seasons.Winter) Bare(o, snow);
                        break;
                }
            }
        }

        static void Wet(Orig o, float add)
        {
            var m = o.M;
            if (o.HasGloss) m.SetFloat("_Glossiness", Mathf.Clamp01(o.Gloss + add));
            if (o.HasGlossMap) m.SetFloat("_GlossMapScale", o.GlossMap + add * 2f);
        }

        static void Bare(Orig o, int snow)
        {
            var m = o.M;
            m.mainTexture = Twigs;
            m.mainTextureScale = Vector2.one / 1.6f;
            m.color = snow >= 2 ? new Color(0.95f, 0.93f, 0.92f) : new Color(1f, 1f, 1f);
            m.DisableKeyword("_NORMALMAP");
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cutoff", 0.5f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = 2450;
            if (o.HasGloss) m.SetFloat("_Glossiness", 0.05f);
        }

        // ------------------------------------------------------------------ sun & sky

        /// <summary>Sun elevation at noon for the month at ~43°N (90 − latitude + declination).</summary>
        public static float NoonElevation(int month) => 47f + 23.44f * Mathf.Sin(2f * Mathf.PI * (month * 30.4f + 15f - 81f) / 365f);

        static Light Sun => RenderSettings.sun;

        void SunPath()
        {
            var sun = Sun; if (sun == null) return;
            int m = SeasonWeather.Month; int dom = SeasonWeather.Career ? Climate.DayOfMonth(SeasonWeather.Day) : 15;
            float D = Climate.DaylightHours(m, dom), rise = 12f - D / 2f, h = SeasonWeather.Hours;
            float theta;   // 0 sunrise .. π sunset .. 2π next sunrise
            if (h >= rise && h <= rise + D) theta = Mathf.PI * (h - rise) / D;
            else { float n = Mathf.Repeat(h - (rise + D), 24f) / (24f - D); theta = Mathf.PI + Mathf.PI * n; }
            float maxE = NoonElevation(m) * Mathf.Deg2Rad;
            // arc tilted toward the south: rises in the east, highest at noon, sets in the west (same plane GameClock uses at yaw -30°)
            Vector3 east = new Vector3(0.5f, 0f, -0.866f), south = new Vector3(-0.866f, 0f, -0.5f);
            Vector3 p = east * Mathf.Cos(theta) + (Vector3.up * Mathf.Sin(maxE) + south * Mathf.Cos(maxE)) * Mathf.Sin(theta);
            sun.transform.rotation = Quaternion.LookRotation(-p.normalized, Vector3.up);
        }

        static void Atmosphere(GroundState g, int snow)
        {
            var sun = Sun;
            float cloud = SeasonWeather.Cloud;
            bool rainLook = Weather.IsRaining;            // SiteLook already darkened for rain
            bool snowing = SeasonWeather.Snowing, storm = SeasonWeather.StormNow;
            var today = SeasonWeather.Today;
            float extra = rainLook ? 0f : snowing ? 1f : cloud;
            if (sun != null && extra > 0.01f)
            {
                sun.intensity *= Mathf.Lerp(1f, 0.38f, extra * extra);
                sun.shadowStrength = Mathf.Lerp(sun.shadowStrength, 0.4f, extra);
                sun.color = Color.Lerp(sun.color, new Color(0.84f, 0.87f, 0.92f), extra * 0.6f);
            }
            if (storm && sun != null) sun.intensity *= 0.6f;

            float amb = 1f - 0.25f * extra - (storm ? 0.25f : 0f);
            Color cool = SeasonWeather.Season == Seasons.Winter ? new Color(0.92f, 0.96f, 1.08f) : Color.white;
            if (amb < 0.999f || cool != Color.white)
            {
                RenderSettings.ambientSkyColor = Mul(RenderSettings.ambientSkyColor, cool) * amb;
                RenderSettings.ambientEquatorColor = Mul(RenderSettings.ambientEquatorColor, cool) * amb;
                RenderSettings.ambientGroundColor *= amb;
            }
            if (snow > 0) RenderSettings.ambientGroundColor = Color.Lerp(RenderSettings.ambientGroundColor, RenderSettings.ambientSkyColor * 1.1f, snow / 5f);

            float density = RenderSettings.fogDensity;
            if (snowing) density = today.Storm == StormKind.HeavySnow ? 0.03f : 0.016f;
            else if (rainLook && today.Storm == StormKind.FloodRain) density = 0.014f;
            else if (!rainLook) density = Mathf.Lerp(density, 0.0065f, cloud * 0.6f);
            if (storm && !snowing) density *= 1.3f;
            RenderSettings.fogDensity = density;
            if (snowing) RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, new Color(0.78f, 0.80f, 0.84f) * Mathf.Max(0.15f, SiteLook.Daylight), 0.8f);
            else if (!rainLook && cloud > 0.01f) RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, new Color(0.62f, 0.65f, 0.69f) * Mathf.Max(0.12f, SiteLook.Daylight), cloud * 0.7f);
            if (storm) RenderSettings.fogColor *= 0.75f;

            // Overcast: Unity's procedural sky can't do a flat grey deck (thin atmosphere = navy, thick = orange), so swap in a
            // grey gradient sky while it's overcast and put the original back when it clears.
            float k = rainLook ? 1f : extra;
            if (k >= 0.7f || storm)
            {
                if (RenderSettings.skybox != Overcast) { _clearSky = RenderSettings.skybox; RenderSettings.skybox = Overcast; }
                float lum = Mathf.Lerp(0.06f, 1f, SiteLook.Daylight) * (storm ? 0.62f : rainLook ? 0.8f : 0.92f);
                lum *= 0.5f;   // Skybox/Panoramic doubles the tint (unity_ColorSpaceDouble)
                Overcast.SetColor("_Tint", new Color(lum, lum, lum * 1.03f));
                Overcast.SetFloat("_Exposure", 1f);
            }
            else
            {
                RestoreSky();
                var sky = RenderSettings.skybox;
                if (sky != null && sky.HasProperty("_Exposure") && extra > 0.01f) sky.SetFloat("_Exposure", sky.GetFloat("_Exposure") * Mathf.Lerp(1f, 0.85f, extra));
            }
            WeatherFx.ApplyFlash();
        }
    }
}
