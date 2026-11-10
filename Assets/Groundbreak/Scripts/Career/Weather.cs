using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Rain + temperature. Rain stops roofing and concrete pours (<see cref="CanRoof"/>, <see cref="CanPourConcrete"/>)
    /// and soaks uncovered sheet goods/drywall/insulation piles unless tarped (Landscaping hands → "Tarp a material pile").</summary>
    public class Weather : MonoBehaviour
    {
        public static Weather I;
        public static bool IsRaining { get; private set; }
        public static float TemperatureF { get; private set; } = 68f;
        /// <summary>Test/sandbox override: null = simulated.</summary>
        public static bool? ForceRain;
        public static bool CanPourConcrete => !IsRaining && TemperatureF >= 40f && TemperatureF <= 95f;
        public static bool CanRoof => !IsRaining;
        public static string Label => (IsRaining ? "Rain" : "Clear") + $"  {Mathf.RoundToInt(TemperatureF)}°F";

        static readonly HashSet<string> _tarped = new HashSet<string>();
        static readonly Dictionary<string, float> _wetHours = new Dictionary<string, float>();
        public static bool IsTarped(string id) => _tarped.Contains(id);

        int _forecastDay = -1; float _rainStart, _rainEnd, _mean;
        ParticleSystem _rainFx;
        readonly Dictionary<string, GameObject> _tarpVis = new Dictionary<string, GameObject>();

        public static bool Sensitive(string id)
        {
            id = id.ToLowerInvariant();
            return id.Contains("drywall") || id.Contains("osb") || id.Contains("ply") || id.Contains("insul") || id.Contains("gyp");
        }

        public static string ToggleTarp(string id)
        {
            if (_tarped.Remove(id)) { Game.Stock.Add("tarp", 1); return "Tarp removed"; }
            if (!Game.Stock.TryTake("tarp", 1)) return "No tarps on site (Garden Center: Outdoor)";
            _tarped.Add(id); return $"Tarped {LandCatalog.Name(id)}";
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Forecast(int day)
        {
            _forecastDay = day;
            var rng = new System.Random(day * 7919 + 17);
            _mean = 52f + (float)rng.NextDouble() * 30f;
            if (rng.NextDouble() < 0.28) { _rainStart = 6f + (float)rng.NextDouble() * 12f; _rainEnd = _rainStart + 1.5f + (float)rng.NextDouble() * 4f; }
            else { _rainStart = _rainEnd = -1f; }
        }

        float _lastHours = -1;
        void Update()
        {
            var clock = GameClock.I;
            float hours = clock != null ? clock.Hours : 12f; int day = clock != null ? clock.Day : 1;
            if (day != _forecastDay) Forecast(day);
            bool rain = GameSettings.WeatherEnabled && hours >= _rainStart && hours < _rainEnd;
            if (ForceRain.HasValue) rain = ForceRain.Value;
            if (rain != IsRaining) Game.Toast(rain ? "It's raining - no roofing or pours; tarp your sheet goods" : "Rain stopped");
            IsRaining = rain;
            TemperatureF = GameSettings.WeatherEnabled ? _mean + 10f * Mathf.Sin((hours - 9f) / 24f * Mathf.PI * 2f) - (rain ? 6f : 0f) : 70f;

            float dh = _lastHours < 0 ? 0 : Mathf.Repeat(hours - _lastHours, 24f);
            _lastHours = hours;
            if (IsRaining && !GameSettings.Relaxed && dh > 0 && dh < 1f) Soak(dh);
            UpdateFx();
            UpdateTarps();
        }

        void Soak(float dh)
        {
            foreach (var kv in new List<KeyValuePair<string, int>>(Game.Stock.Entries))
            {
                if (kv.Value <= 0 || !Sensitive(kv.Key) || _tarped.Contains(kv.Key)) continue;
                _wetHours.TryGetValue(kv.Key, out float w); w += dh;
                if (w >= 2f)
                {
                    int lose = Mathf.Max(1, kv.Value / 10);
                    Game.Stock.TryTake(kv.Key, lose);
                    Game.Toast($"Rain ruined {lose} x {LandCatalog.Name(kv.Key)} - tarp your piles!");
                    w = 0;
                }
                _wetHours[kv.Key] = w;
            }
        }

        void LateUpdate()
        {
            // dim the sun/sky under cloud (GameClock sets intensity each frame in Update)
            var sun = RenderSettings.sun;
            if (sun != null && IsRaining) sun.intensity *= 0.35f;
            RenderSettings.fogColor = IsRaining ? new Color(0.55f, 0.58f, 0.62f) : new Color(0.74f, 0.80f, 0.87f);
            RenderSettings.fogEndDistance = IsRaining ? 220f : 600f;
        }

        void UpdateFx()
        {
            if (_rainFx == null)
            {
                var go = new GameObject("RainFX");
                _rainFx = go.AddComponent<ParticleSystem>();
                _rainFx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _rainFx.main; main.startLifetime = 1.2f; main.startSpeed = 0f; main.startSize = 0.02f; main.maxParticles = 6000;
                main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = new Color(0.75f, 0.8f, 0.88f, 0.55f);
                var vel = _rainFx.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(0.6f); vel.y = new ParticleSystem.MinMaxCurve(-9f); vel.z = new ParticleSystem.MinMaxCurve(0.2f);
                var em = _rainFx.emission; em.rateOverTime = 3500f;
                var sh = _rainFx.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(30f, 0.1f, 30f);
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 2f;
                var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit");
                if (shader != null) r.sharedMaterial = new Material(shader) { color = new Color(0.8f, 0.85f, 0.95f, 0.5f) };
            }
            var p = Game.Player != null ? Game.Player.transform.position : Vector3.zero;
            _rainFx.transform.position = p + Vector3.up * 10f;
            if (IsRaining && !_rainFx.isPlaying) _rainFx.Play();
            if (!IsRaining && _rainFx.isPlaying) _rainFx.Stop();
        }

        float _nextTarp;
        void UpdateTarps()
        {
            if (Time.time < _nextTarp) return;
            _nextTarp = Time.time + 0.4f;
            var piles = new Dictionary<string, MaterialPile>();
            foreach (var mp in FindObjectsByType<MaterialPile>(FindObjectsSortMode.None)) piles[mp.DefinitionId] = mp;
            foreach (var id in new List<string>(_tarpVis.Keys))
                if (!_tarped.Contains(id) || !piles.ContainsKey(id) || _tarpVis[id] == null || _tarpVis[id].transform.parent != piles[id].transform)
                { if (_tarpVis[id] != null) Destroy(_tarpVis[id]); _tarpVis.Remove(id); }
            foreach (var id in _tarped)
            {
                if (_tarpVis.ContainsKey(id) || !piles.TryGetValue(id, out var pile)) continue;
                var b = new Bounds(pile.transform.position, Vector3.zero); bool any = false;
                foreach (var r in pile.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                var size = any ? b.size : new Vector3(1.2f, 0.5f, 2.4f);
                var holder = new GameObject("Tarp"); holder.transform.SetParent(pile.transform, false);
                holder.transform.position = new Vector3(b.center.x, pile.transform.position.y, b.center.z);
                var k = new LandModel(); LandModels.Tarp(k, new Vector3(size.x, b.max.y - pile.transform.position.y, size.z));
                k.Make(holder.transform, "TarpMesh");
                _tarpVis[id] = holder;
            }
        }

        // ------------------------------------------------------------------ save

        [Serializable] class Data { public List<string> tarped = new List<string>(); }
        static string Save() { var d = new Data(); d.tarped.AddRange(_tarped); return JsonUtility.ToJson(d); }
        static void Load(string json) { _tarped.Clear(); _wetHours.Clear(); if (!string.IsNullOrEmpty(json)) _tarped.UnionWith(JsonUtility.FromJson<Data>(json).tarped); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _tarped.Clear(); _wetHours.Clear(); IsRaining = false; ForceRain = null; TemperatureF = 68f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => SaveSystem.RegisterExtension("weather", Save, Load);
    }
}
