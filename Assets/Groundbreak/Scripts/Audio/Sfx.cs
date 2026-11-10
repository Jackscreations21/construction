using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Synthesized sound effects (AudioClip.Create, no assets) + ambient birds/wind/rain.
    /// <c>Sfx.Play("hammer", worldPos)</c>. Names: hammer, nailgun, saw, drill, click, shovel, rake, gravel, water, engine,
    /// step-grass, step-wood, step-hard, truck, beeper, pour, thud, error, success.</summary>
    public class Sfx : MonoBehaviour
    {
        static Sfx _i;
        const int Rate = 22050;
        static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> _pool = new List<AudioSource>();
        AudioSource _wind, _birds, _rain;
        Vector3 _lastStep; bool _stepInit;

        public static void Play(string name, Vector3 pos, float volume = 1f)
        {
            if (_i == null || string.IsNullOrEmpty(name)) return;
            var clip = Clip(name); if (clip == null) return;
            var src = _i.Free();
            src.transform.position = pos; src.clip = clip; src.volume = volume * GameSettings.MasterVolume;
            src.pitch = Random.Range(0.94f, 1.06f); src.spatialBlend = name == "click" || name == "error" || name == "success" ? 0f : 0.85f;
            src.Play();
        }

        AudioSource Free()
        {
            foreach (var s in _pool) if (!s.isPlaying) return s;
            var go = new GameObject("Sfx"); go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.rolloffMode = AudioRolloffMode.Linear; a.maxDistance = 60f; a.dopplerLevel = 0;
            _pool.Add(a); return a;
        }

        void Awake()
        {
            _i = this;
            _wind = Loop("wind", 0.18f); _birds = Loop("birds", 0.22f); _rain = Loop("rain", 0f);
        }
        void OnDestroy() { if (_i == this) _i = null; }

        AudioSource Loop(string name, float vol)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = Clip(name); a.loop = true; a.volume = vol; a.spatialBlend = 0; a.Play();
            return a;
        }

        void Update()
        {
            float day = GameClock.I != null ? GameClock.I.Hours : 12f;
            bool rain = Weather.IsRaining;
            float v = GameSettings.MasterVolume;
            _birds.volume = Mathf.MoveTowards(_birds.volume, (day > 5.5f && day < 20f && !rain ? 0.22f : 0f) * v, Time.deltaTime * 0.2f);
            _wind.volume = Mathf.MoveTowards(_wind.volume, (rain ? 0.3f : 0.14f) * v, Time.deltaTime * 0.2f);
            _rain.volume = Mathf.MoveTowards(_rain.volume, (rain ? 0.45f : 0f) * v, Time.deltaTime * 0.3f);

            var p = Game.Player;
            if (p == null) return;
            // tool sounds for milestone-1 tools (their files are shared, so we listen here instead of editing them)
            var tool = Game.Belt != null ? Game.Belt.Active : null;
            if (tool != null && !Game.UIOpen && Cursor.lockState == CursorLockMode.Locked && GInput.MouseDown(0))
            {
                string snd = tool.Id == "hammer" ? "hammer" : tool.Id == "nailer" ? "nailgun" : tool.Id == "saw" ? "saw" : tool.Id == "place" ? "thud" : null;
                if (snd != null) Play(snd, p.transform.position + p.transform.forward, 0.7f);
            }
            var pos = p.transform.position;
            if (!_stepInit) { _lastStep = pos; _stepInit = true; }
            if (new Vector2(pos.x - _lastStep.x, pos.z - _lastStep.z).magnitude > 0.75f)
            {
                _lastStep = pos;
                string kind = "step-grass";
                if (Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out var hit, 1f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.GetComponentInParent<Piece>() != null) kind = "step-wood";
                    else if (hit.collider.GetComponentInParent<LandscapeFeature>() != null) kind = "step-hard";
                    else { var c = Grade.CoverAt(hit.point.x, hit.point.z); if (c == LandCover.Gravel) kind = "gravel"; }
                }
                Play(kind, pos, kind == "gravel" ? 0.25f : 0.35f);
            }
        }

        // ------------------------------------------------------------------ synthesis

        static float Noise(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFF) / 32768f - 1f; }

        delegate float Gen(float t, int i, ref uint seed);

        static AudioClip Make(string name, float seconds, Gen g)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var d = new float[n]; uint seed = (uint)name.GetHashCode() | 1u;
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(g(i / (float)Rate, i, ref seed), -1f, 1f);
            var c = AudioClip.Create("sfx_" + name, n, 1, Rate, false);
            c.SetData(d, 0); return c;
        }

        public static AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var c) && c != null) return c;
            float lp = 0, lp2 = 0;
            switch (name)
            {
                case "hammer": c = Make(name, 0.35f, (float t, int i, ref uint s) => { float e = Mathf.Exp(-t * 28f); return (Mathf.Sin(t * 2 * Mathf.PI * 1850) * 0.4f + Mathf.Sin(t * 2 * Mathf.PI * 620) * 0.5f + Noise(ref s) * 0.5f * Mathf.Exp(-t * 90)) * e; }); break;
                case "nailgun": c = Make(name, 0.25f, (float t, int i, ref uint s) => { float n = Noise(ref s); lp += (n - lp) * 0.3f; return (lp * 1.4f * Mathf.Exp(-t * 18) + Mathf.Sin(t * 2 * Mathf.PI * 140) * Mathf.Exp(-t * 40) * 0.8f); }); break;
                case "saw": c = Make(name, 1.2f, (float t, int i, ref uint s) => { float env = Mathf.Clamp01(t * 8) * Mathf.Clamp01((1.2f - t) * 5); float f = 180 + 40 * Mathf.Sin(t * 9); return (Mathf.Sin(t * 2 * Mathf.PI * f) * 0.35f + Mathf.Sin(t * 2 * Mathf.PI * f * 3.02f) * 0.2f + Noise(ref s) * 0.35f) * env; }); break;
                case "drill": c = Make(name, 0.8f, (float t, int i, ref uint s) => (Mathf.Sin(t * 2 * Mathf.PI * 420) * 0.4f + Noise(ref s) * 0.15f) * Mathf.Clamp01(t * 10) * Mathf.Clamp01((0.8f - t) * 6)); break;
                case "click": c = Make(name, 0.04f, (float t, int i, ref uint s) => Mathf.Sin(t * 2 * Mathf.PI * 2400) * Mathf.Exp(-t * 160) * 0.5f); break;
                case "shovel": c = Make(name, 0.45f, (float t, int i, ref uint s) => { float n = Noise(ref s); lp += (n - lp) * 0.12f; float scrape = Mathf.Exp(-Mathf.Abs(t - 0.12f) * 18); return lp * 2.2f * scrape + Mathf.Sin(t * 2 * Mathf.PI * 90) * Mathf.Exp(-t * 20) * 0.3f; }); break;
                case "rake": c = Make(name, 0.5f, (float t, int i, ref uint s) => { float n = Noise(ref s); lp += (n - lp) * 0.25f; return lp * (0.6f + 0.4f * Mathf.Sin(t * 60)) * Mathf.Sin(Mathf.PI * t / 0.5f); }); break;
                case "gravel": c = Make(name, 0.6f, (float t, int i, ref uint s) => { float n = Noise(ref s); float crack = (s & 255) < 6 ? 1f : 0f; lp += (n - lp) * 0.35f; return (lp * 0.8f + crack * Noise(ref s)) * Mathf.Exp(-t * 4) * Mathf.Clamp01(t * 30); }); break;
                case "water": c = Make(name, 1.0f, (float t, int i, ref uint s) => { float n = Noise(ref s); lp += (n - lp) * 0.5f; return lp * 0.5f * Mathf.Clamp01(t * 6) * Mathf.Clamp01((1f - t) * 4); }); break;
                case "engine": c = Make(name, 1.0f, (float t, int i, ref uint s) => { float f = 38; float saw = (t * f % 1f) * 2 - 1; return (saw * 0.35f + Noise(ref s) * 0.1f) * Mathf.Clamp01(t * 6) * Mathf.Clamp01((1f - t) * 4); }); break;
                case "truck": c = Make(name, 2.0f, (float t, int i, ref uint s) => { float f = 32 + 6 * Mathf.Sin(t * 1.3f); float saw = (t * f % 1f) * 2 - 1; float n = Noise(ref s); lp += (n - lp) * 0.05f; return (saw * 0.3f + lp * 0.8f) * Mathf.Clamp01(t * 2) * Mathf.Clamp01((2f - t) * 2); }); break;
                case "beeper": c = Make(name, 1.0f, (float t, int i, ref uint s) => (t % 0.5f) < 0.25f ? Mathf.Sin(t * 2 * Mathf.PI * 1100) * 0.4f : 0f); break;
                case "pour": c = Make(name, 1.5f, (float t, int i, ref uint s) => { float n = Noise(ref s); lp += (n - lp) * 0.08f; lp2 += (lp - lp2) * 0.1f; return lp2 * 3f * Mathf.Clamp01(t * 3) * Mathf.Clamp01((1.5f - t) * 2); }); break;
                case "thud": c = Make(name, 0.3f, (float t, int i, ref uint s) => (Mathf.Sin(t * 2 * Mathf.PI * (110 - t * 120)) * 0.7f + Noise(ref s) * 0.2f * Mathf.Exp(-t * 60)) * Mathf.Exp(-t * 14)); break;
                case "error": c = Make(name, 0.4f, (float t, int i, ref uint s) => Mathf.Sin(t * 2 * Mathf.PI * (t < 0.2f ? 330 : 247)) * 0.3f * Mathf.Exp(-(t % 0.2f) * 8)); break;
                case "success": c = Make(name, 0.6f, (float t, int i, ref uint s) => Mathf.Sin(t * 2 * Mathf.PI * (t < 0.15f ? 523 : t < 0.3f ? 659 : 784)) * 0.3f * Mathf.Exp(-(t % 0.15f) * 6)); break;
                case "step-grass": c = Make(name, 0.18f, (float t, int i, ref uint s) => { float n = Noise(ref s); lp += (n - lp) * 0.2f; return lp * 1.2f * Mathf.Exp(-t * 25); }); break;
                case "step-wood": c = Make(name, 0.15f, (float t, int i, ref uint s) => (Mathf.Sin(t * 2 * Mathf.PI * 180) * 0.6f + Noise(ref s) * 0.2f) * Mathf.Exp(-t * 40)); break;
                case "step-hard": c = Make(name, 0.12f, (float t, int i, ref uint s) => (Noise(ref s) * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 300) * 0.3f) * Mathf.Exp(-t * 60)); break;
                case "wind": c = Make(name, 6f, (float t, int i, ref uint s) => { float n = Noise(ref s); lp += (n - lp) * 0.02f; float gust = 0.6f + 0.4f * Mathf.Sin(t * 2 * Mathf.PI / 6f); return lp * 4f * gust; }); break;
                case "rain": c = Make(name, 4f, (float t, int i, ref uint s) => { float n = Noise(ref s); lp += (n - lp) * 0.6f; float drop = (s & 1023) < 3 ? Noise(ref s) : 0f; return lp * 0.4f + drop * 0.6f; }); break;
                case "birds":
                    c = Make(name, 8f, (float t, int i, ref uint s) =>
                    {
                        // a few chirp phrases: fast frequency sweeps
                        float sum = 0;
                        for (int k = 0; k < 3; k++)
                        {
                            float start = 1.3f + k * 2.4f, local = t - start;
                            if (local < 0 || local > 0.9f) continue;
                            float note = local % 0.15f, idx = Mathf.Floor(local / 0.15f);
                            float f = 2800 + 900 * Mathf.Sin(idx * 1.7f + k) - note * 6000;
                            sum += Mathf.Sin(2 * Mathf.PI * f * note) * Mathf.Sin(Mathf.PI * note / 0.15f) * 0.25f;
                        }
                        return sum;
                    });
                    break;
                default: return null;
            }
            _clips[name] = c;
            return c;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _clips.Clear();
    }
}
