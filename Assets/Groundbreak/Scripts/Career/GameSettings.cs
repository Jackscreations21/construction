using UnityEngine;

namespace Groundbreak
{
    public enum GameMode { FreeBuild, Career }

    /// <summary>Global game options chosen on the main menu (Settings) or in Free Build's sandbox toggles.
    /// Other features read these: e.g. Relaxed skips cure/dry waits, InspectionsEnabled=false auto-passes inspections.</summary>
    public static class GameSettings
    {
        public static GameMode Mode = GameMode.FreeBuild;
        public static bool IsCareer => Mode == GameMode.Career;

        /// <summary>Relaxed simulation: no waiting on cure/dry times, no tool wear, rain never ruins materials.</summary>
        public static bool Relaxed;
        /// <summary>Wallet is topped back up automatically (sandbox).</summary>
        public static bool UnlimitedMoney;
        /// <summary>When false, scheduling an inspection passes it immediately.</summary>
        public static bool InspectionsEnabled = true;
        /// <summary>Weather simulation (rain + temperature). Off = always clear 70°F.</summary>
        public static bool WeatherEnabled = true;
        public static float MasterVolume = 0.8f;
        public static bool ShowMenuOnBoot = true;

        public static void Reset()
        {
            Mode = GameMode.FreeBuild; Relaxed = false; UnlimitedMoney = false; InspectionsEnabled = true; WeatherEnabled = true;
        }

        [System.Serializable]
        public class Data { public int mode; public bool relaxed, unlimited, inspections = true, weather = true; }

        public static string ToJson() => JsonUtility.ToJson(new Data
        { mode = (int)Mode, relaxed = Relaxed, unlimited = UnlimitedMoney, inspections = InspectionsEnabled, weather = WeatherEnabled });

        public static void FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            Mode = (GameMode)d.mode; Relaxed = d.relaxed; UnlimitedMoney = d.unlimited; InspectionsEnabled = d.inspections; WeatherEnabled = d.weather;
        }
    }
}
