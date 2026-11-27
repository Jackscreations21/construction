using UnityEngine;

namespace Groundbreak
{
    /// <summary>Precipitation and storm particles around the camera: drifting wind-blown snow, hail, a heavier downpour for flooding rain,
    /// leaves and debris in windstorms, and lightning flashes in thunderstorms. (Normal rain is <see cref="Weather"/>'s own RainFX.)</summary>
    public class WeatherFx : MonoBehaviour
    {
        public static WeatherFx I;
        /// <summary>Tests: centre the effects here instead of on the player/camera.</summary>
        public static Vector3? FollowOverride;
        /// <summary>Thunderstorm flash brightness right now (0..1).</summary>
        public static float Flash { get; private set; }

        ParticleSystem _snow, _hail, _downpour, _debris;
        readonly System.Collections.Generic.HashSet<ParticleSystem> _wanted = new System.Collections.Generic.HashSet<ParticleSystem>();
        float _nextBolt = 5f, _boltT = -1f;
        static Material _dotMat, _leafMat;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public static Vector3 Center
        {
            get
            {
                if (FollowOverride.HasValue) return FollowOverride.Value;
                var cam = Camera.main;
                if (cam != null) return cam.transform.position;
                return Game.Player != null ? Game.Player.transform.position : Vector3.zero;
            }
        }

        /// <summary>Wind direction (unit, horizontal) for the day: the way things blow toward.</summary>
        public static Vector3 WindDir => Quaternion.Euler(0f, (SeasonWeather.Today.Day * 73 + 40) % 360, 0f) * Vector3.forward;
        static float WindMs => SeasonWeather.WindMph * 0.447f;

        static Texture2D Dot()
        {
            const int n = 32; var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - d * d) ));
                }
            t.Apply(); return t;
        }

        static Material DotMat
        {
            get
            {
                if (_dotMat != null) return _dotMat;
                var sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit");
                _dotMat = new Material(sh) { name = "GB_WeatherDot", mainTexture = Dot(), color = new Color(1f, 1f, 1f, 0.9f) };
                return _dotMat;
            }
        }

        static Material LeafMat
        {
            get
            {
                if (_leafMat != null) return _leafMat;
                const int n = 32; var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        bool inLeaf = u * u / 0.35f + v * v < 1f;   // narrow ellipse
                        t.SetPixel(x, y, new Color(1, 1, 1, inLeaf ? 1f : 0f));
                    }
                t.Apply();
                var sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit");
                _leafMat = new Material(sh) { name = "GB_WeatherLeaf", mainTexture = t, color = Color.white };
                return _leafMat;
            }
        }

        static ParticleSystem Make(string name, Material mat, float lifetime, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(I != null ? I.transform : null, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.startLifetime = lifetime; main.startSpeed = 0f; main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        void Build()
        {
            if (_snow != null) return;
            // snow: 2-6 mm flakes rendered as soft 2-4 cm dots, ~1 m/s fall, turbulent drift
            _snow = Make("SnowFX", DotMat, 10f, 30000);
            { var m = _snow.main; m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.085f); m.startColor = new Color(1f, 1f, 1f, 1f);
              var sh = _snow.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(34f, 0.1f, 34f);
              var n = _snow.noise; n.enabled = true; n.strength = 0.5f; n.frequency = 0.35f; n.scrollSpeed = 0.2f; }
            // hail: pea to quarter size, falls ~15 m/s and bounces off the ground
            _hail = Make("HailFX", DotMat, 1.6f, 6000);
            { var m = _hail.main; m.startSize = new ParticleSystem.MinMaxCurve(0.014f, 0.026f); m.startColor = new Color(0.95f, 0.97f, 1f, 1f); m.gravityModifier = 1f;
              var sh = _hail.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(26f, 0.1f, 26f);
              var c = _hail.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D; c.quality = ParticleSystemCollisionQuality.Low; c.bounce = 0.35f; c.dampen = 0.2f; c.lifetimeLoss = 0.3f; }
            // downpour for flooding rain (on top of Weather's RainFX)
            _downpour = Make("DownpourFX", DotMat, 1.1f, 12000);
            { var m = _downpour.main; m.startSize = 0.025f; m.startColor = new Color(0.78f, 0.82f, 0.9f, 0.5f);
              var sh = _downpour.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(30f, 0.1f, 30f);
              var r = _downpour.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 2f; }
            // wind-blown leaves, twigs and grit
            _debris = Make("DebrisFX", LeafMat, 4f, 3000);
            { var m = _debris.main; m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.15f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
              var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(0.55f, 0.38f, 0.18f), 0f), new GradientColorKey(new Color(0.75f, 0.55f, 0.2f), 0.5f), new GradientColorKey(new Color(0.42f, 0.5f, 0.22f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
              m.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
              var sh = _debris.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(40f, 6f, 40f);
              var rot = _debris.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
              var n = _debris.noise; n.enabled = true; n.strength = 1.5f; n.frequency = 0.5f; }
        }

        void Update()
        {
            bool on = GameSettings.WeatherEnabled;
            Build();
            var t = SeasonWeather.Today;
            bool snow = on && SeasonWeather.Snowing;
            bool hail = on && SeasonWeather.Hailing;
            bool pour = on && SeasonWeather.Raining && t.Storm == StormKind.FloodRain;
            bool debris = on && (t.Storm == StormKind.Windstorm ? SeasonWeather.StormNow : SeasonWeather.WindMph > 25f || (t.Sky == Sky.Thunderstorm && SeasonWeather.Precipitating));
            var c = Center; var wind = WindDir * WindMs;

            Place(_snow, c + Vector3.up * 11f - wind * 3f, snow, t.Storm == StormKind.HeavySnow ? 7000f : 2600f, new Vector3(wind.x * 0.8f, -1.1f, wind.z * 0.8f));
            Place(_hail, c + Vector3.up * 14f - wind * 0.5f, hail, 2500f, new Vector3(wind.x * 0.3f, -15f, wind.z * 0.3f));
            Place(_downpour, c + Vector3.up * 10f, pour, 9000f, new Vector3(wind.x * 0.3f, -10f, wind.z * 0.3f));
            Place(_debris, c + Vector3.up * 3f - WindDir * 18f, debris, t.Storm == StormKind.Windstorm ? 450f : 60f, new Vector3(wind.x * 0.9f, 0.3f, wind.z * 0.9f));

            // lightning: a double flicker every 6-25 s while a thunderstorm rains
            bool thunder = on && t.Sky == Sky.Thunderstorm && SeasonWeather.Precipitating;
            if (thunder && Time.time >= _nextBolt) { _boltT = 0f; _nextBolt = Time.time + 6f + Random.value * 19f; }
            if (_boltT >= 0f)
            {
                _boltT += Time.deltaTime;
                Flash = _boltT < 0.07f ? 1f : _boltT < 0.13f ? 0.15f : _boltT < 0.2f ? 0.8f : Mathf.Max(0f, 0.8f - (_boltT - 0.2f) * 4f);
                if (_boltT > 0.5f) { _boltT = -1f; Flash = 0f; }
            }
            else Flash = 0f;
        }

        void Place(ParticleSystem ps, Vector3 pos, bool on, float rate, Vector3 vel)
        {
            if (on) _wanted.Add(ps); else _wanted.Remove(ps);
            ps.transform.position = pos;
            var em = ps.emission; em.rateOverTime = rate;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(vel.x); v.y = new ParticleSystem.MinMaxCurve(vel.y); v.z = new ParticleSystem.MinMaxCurve(vel.z);
            if (on && !ps.isPlaying) { ps.Simulate(ps.main.startLifetime.constantMax, true, true); ps.Play(); }
            if (!on && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>Lightning brightens ambient light and the sky (called after the atmosphere pass).</summary>
        public static void ApplyFlash()
        {
            if (Flash <= 0f) return;
            var add = new Color(0.55f, 0.6f, 0.75f) * Flash;
            RenderSettings.ambientSkyColor += add; RenderSettings.ambientEquatorColor += add * 0.8f; RenderSettings.ambientGroundColor += add * 0.5f;
            RenderSettings.fogColor += add * 0.4f;
        }

        /// <summary>Tests / teleports: refill every running effect around <see cref="Center"/> right away (also Weather's RainFX).</summary>
        public static void Settle()
        {
            if (I == null) return;
            I.Update();
            foreach (var ps in new[] { I._snow, I._hail, I._downpour, I._debris })
            {
                if (ps == null) continue;
                if (I._wanted.Contains(ps)) { ps.Simulate(ps.main.startLifetime.constantMax, true, true); ps.Play(); }
                else { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Clear(true); }
            }
            var rain = GameObject.Find("RainFX");
            if (rain != null && rain.TryGetComponent<ParticleSystem>(out var r))
            {
                if (Weather.IsRaining)
                {
                    if (!r.isPlaying) r.Play();
                    rain.transform.position = Center + Vector3.up * 10f;
                    r.Simulate(1.2f, true, true); r.Play();
                }
                else { r.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); r.Clear(true); }
            }
        }
    }
}
