using UnityEngine;

namespace Groundbreak
{
    /// <summary>Game time: Day 1 starts at 7:00 AM; 20 real minutes per game day.</summary>
    public class GameClock : MonoBehaviour
    {
        public static GameClock I;
        public const float RealSecondsPerDay = 20f * 60f;

        public float Hours = 7f;   // 0..24
        public int Day = 1;

        Light _sun;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            var go = GameObject.Find("Sun");
            if (go != null) _sun = go.GetComponent<Light>();
        }

        void Update()
        {
            Hours += Time.deltaTime * 24f / RealSecondsPerDay;
            while (Hours >= 24f) { Hours -= 24f; Day++; }

            if (_sun != null)
            {
                // Sun rises at 6:00, peaks at noon, sets at 18:00.
                float angle = (Hours - 6f) / 12f * 180f;
                _sun.transform.rotation = Quaternion.Euler(angle, -30f, 0f);
                float daylight = Mathf.Clamp01(Mathf.Sin(Mathf.Clamp(angle, 0f, 180f) * Mathf.Deg2Rad) * 3f);
                _sun.intensity = Mathf.Lerp(0.05f, 1.1f, daylight);
            }
        }

        public string TimeLabel
        {
            get
            {
                int totalMin = Mathf.FloorToInt(Hours * 60f);
                int h24 = (totalMin / 60) % 24, m = totalMin % 60;
                int h12 = h24 % 12; if (h12 == 0) h12 = 12;
                return $"Day {Day} · {h12}:{m:00} {(h24 < 12 ? "AM" : "PM")}";
            }
        }
    }
}
