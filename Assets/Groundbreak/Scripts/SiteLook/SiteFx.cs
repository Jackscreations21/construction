using UnityEngine;

namespace Groundbreak
{
    /// <summary>Cheap pooled particle feedback for site work: dirt clods + dust when digging, a gray splash when concrete lands,
    /// stone chips when gravel is spread. One world-space ParticleSystem per effect, emitted with EmitParams (no allocations).</summary>
    public static class SiteFx
    {
        static ParticleSystem _clods, _dust;
        static Material _mat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { _clods = null; _dust = null; }

        static Material Mat
        {
            get
            {
                if (_mat != null) return _mat;
                var sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
                _mat = new Material(sh) { name = "GB_SiteFx", hideFlags = HideFlags.DontUnloadUnusedAsset };
                _mat.mainTexture = SoftDot();
                return _mat;
            }
        }

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
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
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
            main.maxParticles = heavy ? 600 : 300;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; // short bursts: never pause/cull by the player camera's view
            main.gravityModifier = heavy ? 1f : -0.02f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         heavy ? new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) }
                               : new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0.6f, 0.1f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            if (!heavy)
            {
                var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.4f, 1, 1f));
                var vel = ps.limitVelocityOverLifetime; vel.enabled = true; vel.dampen = 0.15f; vel.limit = 0.3f;
            }
            else
            {
                var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.dampen = 0.6f; c.bounce = 0.1f; c.lifetimeLoss = 0.4f;
                c.quality = ParticleSystemCollisionQuality.Low;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        static ParticleSystem Clods => _clods != null ? _clods : (_clods = Make("SiteFx_Clods", true));
        static ParticleSystem Dust => _dust != null ? _dust : (_dust = Make("SiteFx_Dust", false));

        /// <summary>Spray of small pieces thrown up from p (clods, chips, splatter) plus a little dust.</summary>
        public static void Burst(Vector3 p, Color color, int pieces, float pieceSize, float dust, float throwSpeed = 1.6f)
        {
            if (!Application.isPlaying) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < pieces; i++)
            {
                Vector2 d = Random.insideUnitCircle * 0.6f;
                ep.position = p + new Vector3(d.x * 0.15f, 0.03f, d.y * 0.15f);
                ep.velocity = new Vector3(d.x, 1f + Random.value, d.y) * throwSpeed * (0.6f + 0.5f * Random.value);
                ep.startSize = pieceSize * (0.5f + Random.value);
                ep.startLifetime = 0.9f + Random.value * 0.6f;
                ep.startColor = color * (0.75f + 0.35f * Random.value);
                Clods.Emit(ep, 1);
            }
            int puffs = Mathf.RoundToInt(dust * 6f);
            Color dc = Color.Lerp(color, new Color(0.78f, 0.72f, 0.62f), 0.5f); dc.a = 1f;
            for (int i = 0; i < puffs; i++)
            {
                Vector2 d = Random.insideUnitCircle;
                ep.position = p + new Vector3(d.x * 0.25f, 0.1f + 0.1f * Random.value, d.y * 0.25f);
                ep.velocity = new Vector3(d.x * 0.25f, 0.25f + 0.2f * Random.value, d.y * 0.25f);
                ep.startSize = 0.35f + 0.4f * Random.value;
                ep.startLifetime = 1.8f + Random.value * 1.4f;
                ep.startColor = dc;
                Dust.Emit(ep, 1);
            }
        }

        public static readonly Color DirtColor = new Color(0.42f, 0.32f, 0.22f);
        public static readonly Color GravelColor = new Color(0.52f, 0.5f, 0.47f);
        public static readonly Color WetConcrete = new Color(0.5f, 0.5f, 0.49f);

        public static void Dig(Vector3 p) => Burst(p, DirtColor, 12, 0.045f, 0.8f);
        public static void Gravel(Vector3 p) => Burst(p, GravelColor, 8, 0.025f, 0.3f, 1.1f);
        public static void Concrete(Vector3 p) => Burst(p, WetConcrete, 6, 0.04f, 0f, 1.0f);

        /// <summary>Particles currently alive (tests).</summary>
        public static int Alive => (_clods != null ? _clods.particleCount : 0) + (_dust != null ? _dust.particleCount : 0);
    }
}
