using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>
    /// Shared Standard-shader materials per SurfaceKind, textured with ProceduralTextures.
    /// UVs are authored in METERS; each material's tiling is 1 / TileMeters(kind) (shared by _BumpMap via _MainTex_ST).
    /// Smoothness is read from albedo alpha (relative factor * 0.5) scaled by _GlossMapScale = 2 * nominal gloss,
    /// with _Glossiness = nominal as a fallback if that keyword variant is unavailable.
    /// </summary>
    public static class Materials
    {
        static readonly Dictionary<SurfaceKind, Material> _base = new Dictionary<SurfaceKind, Material>();
        static readonly Dictionary<(SurfaceKind, Color), Material> _tinted = new Dictionary<(SurfaceKind, Color), Material>();
        static Material _sky;

        struct Look
        {
            public float Gloss, Metal, Bump;
            public Look(float gloss, float metal, float bump) { Gloss = gloss; Metal = metal; Bump = bump; }
        }

        static Look LookOf(SurfaceKind k)
        {
            switch (k)
            {
                case SurfaceKind.PineLumber: return new Look(0.10f, 0f, 0.6f);
                case SurfaceKind.PineEndGrain: return new Look(0.08f, 0f, 0.9f);
                case SurfaceKind.TreatedLumber: return new Look(0.12f, 0f, 0.8f);
                case SurfaceKind.Osb: return new Look(0.08f, 0f, 0.75f);
                case SurfaceKind.Plywood: return new Look(0.14f, 0f, 0.6f);
                case SurfaceKind.PaintedWhite: return new Look(0.45f, 0f, 0.25f);
                case SurfaceKind.PaintedSteel: return new Look(0.55f, 0f, 0.25f);
                case SurfaceKind.BrushedSteel: return new Look(0.55f, 0.9f, 0.35f);
                case SurfaceKind.Galvanized: return new Look(0.55f, 0.85f, 0.3f);
                case SurfaceKind.DarkPlastic: return new Look(0.35f, 0f, 0.3f);
                case SurfaceKind.Glass: return new Look(0.95f, 0f, 0f);
                case SurfaceKind.Nickel: return new Look(0.6f, 0.9f, 0.3f);
                case SurfaceKind.Brass: return new Look(0.8f, 1f, 0.2f);
                case SurfaceKind.MatteBlack: return new Look(0.22f, 0f, 0.4f);
                case SurfaceKind.Oak: return new Look(0.35f, 0f, 0.5f);
                case SurfaceKind.Walnut: return new Look(0.38f, 0f, 0.5f);
                case SurfaceKind.Porcelain: return new Look(0.9f, 0f, 0.15f);
                case SurfaceKind.Grass: return new Look(0.2f, 0f, 1.0f);
                case SurfaceKind.Dirt: return new Look(0.06f, 0f, 1.0f);
                case SurfaceKind.Gravel: return new Look(0.15f, 0f, 1.0f);
                case SurfaceKind.Concrete: return new Look(0.18f, 0f, 0.6f);
                case SurfaceKind.TruckPaint: return new Look(0.85f, 0f, 0.12f);
                case SurfaceKind.Tire: return new Look(0.12f, 0f, 0.6f);
                case SurfaceKind.Brick: return new Look(0.12f, 0f, 0.9f);
                case SurfaceKind.UsedBrick: return new Look(0.10f, 0f, 1.0f);
                case SurfaceKind.ConcreteBlock: return new Look(0.10f, 0f, 1.0f);
                case SurfaceKind.Mortar: return new Look(0.08f, 0f, 0.7f);
                case SurfaceKind.Rebar: return new Look(0.18f, 0.35f, 1.0f);
                case SurfaceKind.PaperBag: return new Look(0.12f, 0f, 0.5f);
                default: return new Look(0.3f, 0f, 0.5f);
            }
        }

        /// <summary>Real-world size (meters) one texture tile covers.</summary>
        public static float TileMeters(SurfaceKind kind)
        {
            if (CustomTileMeters.TryGetValue(kind, out float custom)) return custom;
            switch (kind)
            {
                case SurfaceKind.Osb:
                case SurfaceKind.Plywood: return 1.2192f;       // 4'
                case SurfaceKind.PineLumber: return 1.0f;
                case SurfaceKind.PineEndGrain: return 0.4f;
                case SurfaceKind.TreatedLumber: return 0.5f;
                case SurfaceKind.PaintedWhite:
                case SurfaceKind.PaintedSteel:
                case SurfaceKind.TruckPaint:
                case SurfaceKind.Porcelain: return 0.5f;
                case SurfaceKind.BrushedSteel: return 0.3f;
                case SurfaceKind.Galvanized: return 0.4f;
                case SurfaceKind.DarkPlastic:
                case SurfaceKind.MatteBlack:
                case SurfaceKind.Tire: return 0.25f;
                case SurfaceKind.Nickel: return 0.15f;
                case SurfaceKind.Brass: return 0.2f;
                case SurfaceKind.Oak:
                case SurfaceKind.Walnut: return 0.6f;
                case SurfaceKind.Grass: return 2.0f;
                case SurfaceKind.Dirt: return 1.5f;
                case SurfaceKind.Gravel: return 1.0f;
                case SurfaceKind.Concrete: return 2.0f;
                case SurfaceKind.Brick:
                case SurfaceKind.UsedBrick: return 0.6f;          // ~3 brick lengths; offset UVs per brick for tone variety
                case SurfaceKind.ConcreteBlock: return 0.8f;
                case SurfaceKind.Mortar: return 0.5f;
                case SurfaceKind.Rebar: return 0.3048f;           // 1' => 8 ribs at 1.5" pitch along U
                case SurfaceKind.PaperBag: return 0.5f;
                case SurfaceKind.Glass: return 1f;
                default: return 1f;
            }
        }

        /// <summary>Extension point: feature modules supply the material for SurfaceKinds they appended (built from their own textures).
        /// Register from [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]; the factory runs lazily on first Get.</summary>
        public static readonly Dictionary<SurfaceKind, System.Func<Material>> Custom = new Dictionary<SurfaceKind, System.Func<Material>>();
        /// <summary>Extension point: tile size (meters) for appended SurfaceKinds.</summary>
        public static readonly Dictionary<SurfaceKind, float> CustomTileMeters = new Dictionary<SurfaceKind, float>();

        public static Material Get(SurfaceKind kind)
        {
            if (_base.TryGetValue(kind, out var m) && m != null) return m;
            m = Custom.TryGetValue(kind, out var make) && make != null ? make() : Build(kind);
            _base[kind] = m;
            return m;
        }

        public static Material Get(SurfaceKind kind, Color tint)
        {
            var key = (kind, tint);
            if (_tinted.TryGetValue(key, out var m) && m != null) return m;
            var b = Get(kind);
            m = new Material(b) { name = b.name + "_Tint" };
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            m.color = b.color * tint;
            _tinted[key] = m;
            return m;
        }

        static Material Build(SurfaceKind kind)
        {
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");
            var m = new Material(shader) { name = "GB_" + kind };
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var look = LookOf(kind);
            m.SetFloat("_Glossiness", look.Gloss);
            m.SetFloat("_Metallic", look.Metal);

            if (kind == SurfaceKind.Glass)
            {
                m.color = new Color(0.82f, 0.90f, 0.94f, 0.25f);
                m.SetFloat("_Mode", 3f); // Transparent (premultiplied: keeps reflections)
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.DisableKeyword("_ALPHATEST_ON");
                m.DisableKeyword("_ALPHABLEND_ON");
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
                return m;
            }

            m.color = Color.white;
            var set = ProceduralTextures.Get(kind);
            if (set.Albedo != null)
            {
                m.mainTexture = set.Albedo;
                m.SetFloat("_SmoothnessTextureChannel", 1f);
                m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                m.SetFloat("_GlossMapScale", Mathf.Min(2f, look.Gloss * 2f));
            }
            if (set.Normal != null)
            {
                m.SetTexture("_BumpMap", set.Normal);
                m.SetFloat("_BumpScale", look.Bump);
                m.EnableKeyword("_NORMALMAP");
            }
            m.mainTextureScale = Vector2.one / TileMeters(kind);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Main surface for a catalog item.</summary>
        public static SurfaceKind ForDefinition(PieceDefinition d)
        {
            if (d == null) return SurfaceKind.PaintedWhite;
            string id = (d.Id ?? "").ToLowerInvariant();
            string nm = (d.Name ?? "").ToLowerInvariant();
            bool Has(string s) => id.Contains(s) || nm.Contains(s);

            switch (d.Category)
            {
                case PieceCategory.Lumber:
                    return id.StartsWith("pt") || id.StartsWith("4x4") ? SurfaceKind.TreatedLumber : SurfaceKind.PineLumber;

                case PieceCategory.Sheet:
                    if (Has("ply")) return SurfaceKind.Plywood;
                    return SurfaceKind.Osb;

                case PieceCategory.Consumable:
                    if (Has("mortar") || Has("bag")) return SurfaceKind.PaperBag;
                    if (Has("tiewire") || Has("tie wire")) return SurfaceKind.MatteBlack;
                    if (Has("nail")) return SurfaceKind.Galvanized;
                    return SurfaceKind.PaperBag;

                case PieceCategory.Masonry:
                    if (Has("brick-used") || Has("used") || Has("tumbled")) return SurfaceKind.UsedBrick;
                    if (Has("brick")) return SurfaceKind.Brick;
                    if (Has("cmu") || Has("block")) return SurfaceKind.ConcreteBlock;
                    return SurfaceKind.ConcreteBlock;

                case PieceCategory.Rebar:
                    return SurfaceKind.Rebar;

                case PieceCategory.Door:
                    if (Has("fiberglass") || Has("craftsman")) return SurfaceKind.Oak;
                    if (Has("barn")) return SurfaceKind.Walnut;
                    if (Has("steel")) return SurfaceKind.PaintedSteel;
                    return SurfaceKind.PaintedWhite;

                case PieceCategory.GarageDoor:
                    if (Has("carriage")) return SurfaceKind.Walnut;
                    return SurfaceKind.PaintedSteel;

                case PieceCategory.DoorHardware:
                    if (Has("brass")) return SurfaceKind.Brass;
                    if (Has("black") || Has("smart")) return SurfaceKind.MatteBlack;
                    return SurfaceKind.Nickel;

                case PieceCategory.Window:
                    if (Has("skylight")) return SurfaceKind.PaintedSteel;
                    return SurfaceKind.PaintedWhite;

                case PieceCategory.Plumbing:
                    if (Has("toilet") || Has("tub") || Has("shower")) return SurfaceKind.Porcelain;
                    if (Has("vanity")) return SurfaceKind.Oak;
                    if (Has("sink") || Has("tankless")) return SurfaceKind.BrushedSteel;
                    if (Has("sump")) return SurfaceKind.DarkPlastic;
                    return SurfaceKind.PaintedWhite;

                case PieceCategory.Mechanical:
                    if (Has("condenser") || Has("heat pump") || Has("hvac-ac") || Has("hvac-hp")) return SurfaceKind.Galvanized;
                    if (Has("rangehood") || Has("range hood")) return SurfaceKind.BrushedSteel;
                    if (Has("furnace") || Has("air handler") || Has("airhandler")) return SurfaceKind.PaintedSteel;
                    return SurfaceKind.PaintedWhite;

                case PieceCategory.Electrical:
                    if (Has("meter")) return SurfaceKind.Galvanized;
                    if (Has("panel")) return SurfaceKind.PaintedSteel;
                    if (Has("fan")) return SurfaceKind.Walnut;
                    if (Has("porch") || Has("outdoor")) return SurfaceKind.MatteBlack;
                    return SurfaceKind.PaintedWhite;

                case PieceCategory.Appliance:
                    if (Has("washer") || Has("dryer")) return SurfaceKind.PaintedWhite;
                    return SurfaceKind.BrushedSteel;
            }
            return SurfaceKind.PaintedWhite;
        }

        /// <summary>Sky, ambient, fog and sun setup for a natural daylight look.</summary>
        public static void ApplyEnvironment()
        {
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                if (_sky == null)
                {
                    _sky = new Material(skyShader) { name = "GB_Sky" };
                    _sky.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    _sky.SetFloat("_SunDisk", 2f);
                    _sky.EnableKeyword("_SUNDISK_HIGH_QUALITY");
                    _sky.DisableKeyword("_SUNDISK_SIMPLE");
                    _sky.DisableKeyword("_SUNDISK_NONE");
                    _sky.SetFloat("_SunSize", 0.035f);
                    _sky.SetFloat("_SunSizeConvergence", 6f);
                    _sky.SetFloat("_AtmosphereThickness", 1.0f);
                    _sky.SetColor("_SkyTint", new Color(0.5f, 0.5f, 0.5f));
                    _sky.SetColor("_GroundColor", new Color(0.30f, 0.36f, 0.24f)); // reads as distant fields
                    _sky.SetFloat("_Exposure", 1.1f);
                }
                RenderSettings.skybox = _sky;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.58f, 0.54f);
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.27f, 0.22f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.9f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.74f, 0.80f, 0.87f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 600f;

            var go = GameObject.Find("Sun");
            var sun = go != null ? go.GetComponent<Light>() : null;
            if (sun != null)
            {
                sun.color = new Color(1f, 0.96f, 0.88f);
                sun.intensity = 1.0f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.85f;
                RenderSettings.sun = sun;
            }

            QualitySettings.shadowDistance = 80f;
            DynamicGI.UpdateEnvironment();
        }
    }
}
