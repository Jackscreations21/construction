using UnityEngine;

namespace Groundbreak
{
    /// <summary>Diesel engine loop that revs with speed, plus the back-up alarm whenever the truck moves in reverse.
    /// Add to any truck root: <c>TruckSounds.Attach(root)</c>.</summary>
    public class TruckSounds : MonoBehaviour
    {
        AudioSource _engine, _beeper;
        Vector3 _last;
        float _speed;
        public bool Reversing { get; private set; }

        public static TruckSounds Attach(GameObject truck)
        {
            var s = truck.GetComponent<TruckSounds>(); // no ?? : Unity fake-null
            return s != null ? s : truck.AddComponent<TruckSounds>();
        }

        AudioSource Src(string clip, float vol)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = Sfx.Clip(clip); a.loop = true; a.playOnAwake = false; a.volume = vol;
            a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 4f; a.maxDistance = 90f; a.dopplerLevel = 0f;
            return a;
        }

        void Start()
        {
            _last = transform.position;
            _engine = Src("truck", 0.5f);
            _beeper = Src("beeper", 0.6f);
            if (_engine.clip != null) _engine.Play();
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 v = (transform.position - _last) / dt; _last = transform.position;
            _speed = Mathf.Lerp(_speed, v.magnitude, 1f - Mathf.Exp(-dt * 4f));
            Reversing = v.sqrMagnitude > 0.04f && Vector3.Dot(v, transform.forward) < 0f;
            float master = GameSettings.MasterVolume;
            if (_engine != null) { _engine.pitch = 0.8f + Mathf.Clamp01(_speed / 12f) * 0.6f; _engine.volume = (0.35f + Mathf.Clamp01(_speed / 12f) * 0.35f) * master; }
            if (_beeper != null && _beeper.clip != null)
            {
                _beeper.volume = 0.6f * master;
                if (Reversing && !_beeper.isPlaying) _beeper.Play();
                else if (!Reversing && _beeper.isPlaying) _beeper.Stop();
            }
        }
    }
}
