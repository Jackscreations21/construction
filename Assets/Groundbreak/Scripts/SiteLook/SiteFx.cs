using UnityEngine;

namespace Groundbreak
{
    /// <summary>Pooled world-space particle feedback for site work: dirt clods + a billowing dust cloud when digging, stone chips and
    /// a fine-dust plume when gravel is dumped, gray slurry spatter where concrete lands. One ParticleSystem per effect, emitted with
    /// EmitParams (no allocations). Dust size / opacity / count scale with the action (shovel &lt; excavator bucket &lt; gravel dump)
    /// and the cloud drifts downwind (<see cref="Wind"/>: a light, slowly veering site breeze, stronger in rain).</summary>
    public static class SiteFx
    {
        static ParticleSystem _clods, _dust;
        static Material _mat, _dustMat;
        static float _lastDust = -10f;

        /// <summary>Set to force the wind (m/s, world). Null = the site breeze.</summary>
        public static Vector3? WindOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { _clods = null; _dust = null; _lastDust = -10f; WindOverride = null; }

        /// <summary>Wind at the site (m/s). Weather has no wind model, so this is a gentle breeze that veers and gusts slowly
        /// (0.5-1.6 m/s; roughly doubled while it rains).</summary>
        public static Vector3 Wind
        {
            get
            {
                if (WindOverride.HasValue) return WindOverride.Value;
                float t = Time.time;
                float ang = (200f + 140f * (Mathf.PerlinNoise(t * 0.004f, 3.1f) - 0.5f)) * Mathf.Deg2Rad;
                float spd = 0.5f + 1.1f * Mathf.PerlinNoise(t * 0.05f, 7.7f);
                if (Weather.IsRaining) spd *= 2f;
                return new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * spd;
            }
        }

        static Material MakeMat(string name, Texture2D tex)
        {
            var sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.mainTexture = tex;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)); // neutral (x2 in the shader)
            return m;
        }

        static Material Mat => _mat != null ? _mat : (_mat = MakeMat("GB_SiteFx", SoftDot()));
        static Material DustMat => _dustMat != null ? _dustMat : (_dustMat = MakeMat("GB_SiteFxDust", DustPuff()));

        static Texture2D SoftDot()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "GB_SoftDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    // clods / chips: a firmer edge than dust so they read as solid bits
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * 3f) * 255));
                }
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }

        /// <summary>Lumpy cloud puff: a few overlapping soft blobs + fractal noise, denser core, wispy edge, slight self-shading.</summary>
        static Texture2D DustPuff()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "GB_DustPuff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            var blobs = new[] { new Vector3(0f, 0f, 0.62f), new Vector3(-0.32f, 0.12f, 0.42f), new Vector3(0.3f, 0.18f, 0.4f), new Vector3(0.05f, -0.3f, 0.4f), new Vector3(0.12f, 0.35f, 0.32f) };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float d = 0f;
                    foreach (var b in blobs)
                    {
                        float dx = (u - b.x) / b.z, dy = (v - b.y) / b.z;
                        d = Mathf.Max(d, 1f - (dx * dx + dy * dy));
                    }
                    float noise = 0.55f * Mathf.PerlinNoise(u * 3f + 11f, v * 3f + 5f) + 0.3f * Mathf.PerlinNoise(u * 7f + 3f, v * 7f + 9f) + 0.15f * Mathf.PerlinNoise(u * 15f, v * 15f);
                    float a = Mathf.Clamp01(d * 1.3f - 0.05f) * Mathf.Lerp(0.55f, 1.15f, noise);
                    float edge = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                    a = Mathf.Clamp01(a) * Mathf.SmoothStep(0f, 1f, edge * 1.6f);
                    float shade = Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(0.5f + v * 0.5f)) * Mathf.Lerp(0.9f, 1.05f, noise); // lit from above
                    byte c = (byte)(Mathf.Clamp01(shade) * 255);
                    px[y * n + x] = new Color32(c, c, c, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }

        static ParticleSystem Make(string name, bool heavy)
        {
            var go = new GameObject(name);
            Object.DontDestroyOnLoad(go);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = heavy ? 800 : 500;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; // short bursts: never pause/cull by the player camera's view
            main.gravityModifier = heavy ? 1f : -0.015f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         heavy ? new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) }
                               : new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.07f), new GradientAlphaKey(0.6f, 0.45f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (!heavy)
            {
                // puffs swell as they spread, slow down (air drag), turn slowly, and ride the wind
                var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.45f), new Keyframe(0.3f, 1.0f), new Keyframe(1, 1.7f)));
                var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = 1.6f; lim.limit = 100f; lim.multiplyDragByParticleSize = false;
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                vel.x = 0f; vel.y = 0f; vel.z = 0f;
                r.sharedMaterial = DustMat;
                r.sortMode = ParticleSystemSortMode.Distance;
            }
            else
            {
                var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.dampen = 0.6f; c.bounce = 0.1f; c.lifetimeLoss = 0.4f;
                c.quality = ParticleSystemCollisionQuality.Low;
                r.sharedMaterial = Mat;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        static ParticleSystem Clods => _clods != null ? _clods : (_clods = Make("SiteFx_Clods", true));
        static ParticleSystem Dust => _dust != null ? _dust : (_dust = Make("SiteFx_Dust", false));

        static void UpdateWind()
        {
            // velocity-over-lifetime is added on top of the (dragged) particle velocity: the cloud settles into the wind speed
            var vel = Dust.velocityOverLifetime;
            Vector3 w = Wind * 0.85f;
            vel.x = w.x; vel.y = 0.05f; vel.z = w.z;
        }

        /// <summary>Spray of pieces thrown up from p (clods, chips, splatter) plus a dust cloud. 'dust' is the action's dust amount:
        /// ~0.8 a shovelful, ~1.2-1.5 an excavator bucket, ~1.6 a gravel dump, 0 wet concrete. Repeated calls in quick succession
        /// (held tools) are thinned so the cloud builds up instead of exploding.</summary>
        public static void Burst(Vector3 p, Color color, int pieces, float pieceSize, float dust, float throwSpeed = 1.6f)
        {
            if (!Application.isPlaying) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < pieces; i++)
            {
                Vector2 d = Random.insideUnitCircle * 0.6f;
                ep.position = p + new Vector3(d.x * 0.15f, 0.03f, d.y * 0.15f);
                ep.velocity = new Vector3(d.x, 1f + Random.value, d.y) * throwSpeed * (0.6f + 0.5f * Random.value);
                ep.startSize = pieceSize * (0.6f + 0.9f * Random.value);
                ep.startLifetime = 1.0f + Random.value * 0.8f;
                ep.startColor = color * (0.6f + 0.4f * Random.value);
                Clods.Emit(ep, 1);
            }
            if (dust <= 0f) return;
            float since = Time.time - _lastDust;
            _lastDust = Time.time;
            float thin = Mathf.Clamp(since / 0.25f, 0.2f, 1f);
            UpdateWind();
            int puffs = Mathf.Max(1, Mathf.RoundToInt((3f + dust * 9f) * thin));
            Color dc = Color.Lerp(color, new Color(0.74f, 0.66f, 0.54f), 0.55f);
            float size = 0.35f + 0.45f * dust;
            float alpha = Mathf.Clamp01(0.32f + 0.22f * dust);
            Vector3 wdir = Wind; wdir.y = 0; wdir = wdir.sqrMagnitude > 1e-4f ? wdir.normalized : Vector3.right;
            for (int i = 0; i < puffs; i++)
            {
                Vector2 d = Random.insideUnitCircle;
                bool low = i % 3 != 0; // most of the cloud rolls along the ground; every third puff lifts higher
                ep.position = p + new Vector3(d.x * 0.3f, (low ? 0.08f : 0.25f) + 0.15f * Random.value, d.y * 0.3f);
                ep.velocity = new Vector3(d.x * 0.9f, (low ? 0.25f : 0.7f) + 0.4f * Random.value, d.y * 0.9f) * (0.6f + 0.4f * dust) + wdir * 0.2f;
                ep.startSize = size * (0.7f + 0.6f * Random.value) * (low ? 1f : 0.8f);
                ep.startLifetime = 2.4f + 1.2f * dust + Random.value * 1.5f;
                ep.rotation = Random.value * 360f;
                var c = dc * (0.88f + 0.2f * Random.value); c.a = alpha * (0.75f + 0.25f * Random.value);
                ep.startColor = c;
                Dust.Emit(ep, 1);
            }
        }

        public static readonly Color DirtColor = new Color(0.36f, 0.27f, 0.18f);
        public static readonly Color GravelColor = new Color(0.55f, 0.53f, 0.5f);
        public static readonly Color WetConcrete = new Color(0.46f, 0.46f, 0.45f);

        /// <summary>A shovelful (amount 1) or a bucket (amount ~1.8).</summary>
        public static void Dig(Vector3 p, float amount = 1f) => Burst(p, DirtColor, Mathf.RoundToInt(10 + 8 * amount), 0.04f + 0.02f * amount, 0.8f * amount, 1.4f + 0.4f * amount);
        /// <summary>Gravel landing: chips + a pale fines plume (crushed stone is dusty).</summary>
        public static void Gravel(Vector3 p, float amount = 1f) => Burst(p, GravelColor, Mathf.RoundToInt(10 * amount), 0.03f, 1.4f * amount, 1.1f);
        /// <summary>Concrete landing from the chute: wet slurry spatter, no dust.</summary>
        public static void Concrete(Vector3 p, float amount = 1f) => Burst(p, WetConcrete, Mathf.RoundToInt(12 * amount), 0.055f, 0f, 1.3f);

        /// <summary>Particles currently alive (tests).</summary>
        public static int Alive => (_clods != null ? _clods.particleCount : 0) + (_dust != null ? _dust.particleCount : 0);
        public static int DustAlive => _dust != null ? _dust.particleCount : 0;

        /// <summary>Mean position of the live dust (tests: wind drift).</summary>
        public static Vector3 DustCentroid()
        {
            if (_dust == null || _dust.particleCount == 0) return Vector3.zero;
            var arr = new ParticleSystem.Particle[_dust.particleCount];
            int n = _dust.GetParticles(arr);
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < n; i++) sum += arr[i].position;
            return n > 0 ? sum / n : Vector3.zero;
        }

        /// <summary>Clears all live particles (tests).</summary>
        public static void Clear()
        {
            if (_dust != null) _dust.Clear();
            if (_clods != null) _clods.Clear();
            _lastDust = -10f;
        }
    }
}
