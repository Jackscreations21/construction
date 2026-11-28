using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Ground conditions at the start of a day: snow on the ground, frost depth, soil wetness (mud).</summary>
    public struct GroundState
    {
        public float SnowIn, FrostIn, Wetness;
        public bool Frozen => FrostIn >= 2f;
        public bool Muddy => !Frozen && Wetness >= 0.35f;
    }

    /// <summary>The current season and weather (static facade). Career: the simulated <see cref="Climate"/> for <see cref="GameClock"/>'s day.
    /// Free Build: whatever the Weather app's picker says (default: a calm, fair June day). Tests that set <see cref="Weather.ForceRain"/>
    /// get calm mild weather (rain as forced). Drives the old <see cref="Weather"/> (rain + temperature) through <c>Weather.Provider</c>.</summary>
    public static class SeasonWeather
    {
        // ---------------------------------------------------------------- Free Build picker
        public static int FreeMonth = 5;                      // June
        public static Sky FreeSky = Sky.Clear;
        public static StormKind FreeStorm = StormKind.None;
        /// <summary>Tests / sandbox: override the whole day (null = normal).</summary>
        public static DayWeather Override;
        /// <summary>Tests / sandbox: override the ground (null = normal).</summary>
        public static GroundState? GroundOverride;

        public static int Day => GameClock.I != null ? GameClock.I.Day : 1;
        public static float Hours => GameClock.I != null ? GameClock.I.Hours : 12f;
        public static bool Career => GameSettings.IsCareer;
        /// <summary>Headless test runs (batchmode) get calm fair Career weather unless a test opts in with this, so other features'
        /// tests never meet a cold snap or a storm. Override / GroundOverride also opt in.</summary>
        public static bool BatchSimulate;
        static bool BatchCalm => Application.isBatchMode && !BatchSimulate && Career;
        public static bool Simulated => GameSettings.WeatherEnabled && !Weather.ForceRain.HasValue && !BatchCalm;
        /// <summary>Weather rules block work only in Career, with weather on, and not when a test forces the weather.</summary>
        public static bool RulesOn => Career && !GameSettings.Relaxed && (Simulated || Override != null || GroundOverride.HasValue);

        public static int Month => Override != null ? Climate.Month(Override.Day) : Career ? Climate.Month(Day) : FreeMonth;
        public static Seasons Season => Climate.SeasonOf(Month);
        public static string SeasonName => Season.ToString();
        public static string DateLabel => Career || Override != null ? Climate.DateLabel(Override != null ? Override.Day : Day) : Climate.MonthNames[FreeMonth] + " (Free Build)";

        /// <summary>Today's weather.</summary>
        public static DayWeather Today
        {
            get
            {
                if (Override != null) return Override;
                if (!GameSettings.WeatherEnabled || Weather.ForceRain.HasValue || BatchCalm) return Calm(Weather.ForceRain == true);
                return Career ? Climate.Actual(Day) : FreeDay();
            }
        }

        static DayWeather _calm, _calmRain, _free; static int _freeKey = -1;
        static DayWeather Calm(bool rain)
        {
            if (rain) return _calmRain ??= new DayWeather { Day = 1, Sky = Sky.Rain, HighF = 64, LowF = 52, WindMph = 6, GustMph = 10, PrecipIn = 0.3f, PrecipStart = 0, PrecipEnd = 24 };
            return _calm ??= new DayWeather { Day = 1, Sky = Sky.Clear, HighF = 74, LowF = 56, WindMph = 5, GustMph = 9 };
        }

        static DayWeather FreeDay()
        {
            int key = FreeMonth * 100 + (int)FreeSky * 10 + (int)FreeStorm;
            if (_free != null && key == _freeKey) return _free;
            _freeKey = key; int m = FreeMonth;
            var w = new DayWeather { Day = 0, Sky = FreeSky, HighF = Climate.NormalHigh[m], LowF = Climate.NormalLow[m], WindMph = Climate.MeanWind[m] * 0.6f };
            w.GustMph = w.WindMph * 1.5f;
            if (FreeSky == Sky.Snow && w.HighF > 34f) { w.HighF = 30f; w.LowF = Mathf.Min(w.LowF, 20f); }
            if ((FreeSky == Sky.Rain || FreeSky == Sky.Thunderstorm) && w.LowF < 36f) { w.LowF = 36f; w.HighF = Mathf.Max(w.HighF, 44f); }
            if (w.Wet) { w.PrecipIn = 0.5f; w.PrecipStart = 0f; w.PrecipEnd = 24f; }
            if (FreeStorm != StormKind.None) { Climate.ApplyStorm(w, FreeStorm); w.PrecipStart = 0f; w.PrecipEnd = 24f; }
            return _free = w;
        }

        // ---------------------------------------------------------------- right now

        public static float TempF => Simulated || Override != null ? Today.TempAt(Hours) : Weather.TemperatureF;
        public static bool Precipitating => Today.PrecipAt(Hours);
        public static bool Raining => Precipitating && !Today.IsSnow;
        public static bool Snowing => Precipitating && Today.IsSnow;
        public static bool Hailing => Precipitating && Today.Storm == StormKind.Hail && Hours < Today.PrecipStart + 0.6f;
        /// <summary>Sustained wind now (mph): calmer at night, peaks mid-afternoon.</summary>
        public static float WindMph { get { var t = Today; return t.WindMph * (0.7f + 0.45f * Mathf.Clamp01(Mathf.Sin((Hours - 7f) / 14f * Mathf.PI))); } }
        public static float GustMph => WindMph * (Today.GustMph / Mathf.Max(1f, Today.WindMph));
        /// <summary>0 clear .. 1 overcast / storm.</summary>
        public static float Cloud
        {
            get
            {
                var t = Today; float c = t.Sky switch { Sky.Clear => 0f, Sky.PartlyCloudy => 0.3f, Sky.Cloudy => 0.75f, _ => 0.6f };
                if (Precipitating) c = 1f; else if (t.Wet && Mathf.Abs(Hours - (t.PrecipStart + t.PrecipEnd) / 2f) < (t.PrecipEnd - t.PrecipStart) / 2f + 2f) c = 0.85f;
                if (t.Storm == StormKind.Windstorm) c = Mathf.Max(c, 0.8f);
                return c;
            }
        }
        public static bool StormNow => Today.Storm != StormKind.None && (Today.Storm == StormKind.Windstorm ? Hours >= 10f && Hours < 19f : Precipitating);

        public static GroundState Ground
        {
            get
            {
                if (GroundOverride.HasValue) return GroundOverride.Value;
                if (!GameSettings.WeatherEnabled || Weather.ForceRain.HasValue || BatchCalm && Override == null) return new GroundState { Wetness = Weather.ForceRain == true ? 0.5f : 0f };
                if (!Career && Override == null) return FreeGround();
                var g = Climate.GroundAt(Override != null ? Override.Day : Day);
                var t = Today;   // today's precipitation so far
                if (t.Wet && Hours > t.PrecipStart)
                {
                    float frac = Mathf.Clamp01((Hours - t.PrecipStart) / Mathf.Max(0.1f, t.PrecipEnd - t.PrecipStart)) * t.PrecipIn;
                    if (t.IsSnow) g.SnowIn += frac * 10f; else g.Wetness += frac * 1.2f;
                }
                return g;
            }
        }

        static GroundState FreeGround()
        {
            var g = new GroundState();
            var s = Season; var t = FreeDay();
            if (s == Seasons.Winter) { g.FrostIn = FreeMonth == 11 ? 6f : 20f; g.SnowIn = FreeSky == Sky.Rain ? 0f : 5f; }
            if (t.IsSnow) g.SnowIn = Mathf.Max(g.SnowIn, t.Storm == StormKind.HeavySnow ? 14f : 4f);
            if (t.Wet && !t.IsSnow) g.Wetness = t.Storm == StormKind.FloodRain ? 2f : 0.6f;
            return g;
        }

        /// <summary>One-line conditions, e.g. "April 12 · Partly cloudy 54°F · wind 9 mph".</summary>
        public static string Summary => $"{DateLabel} · {(Precipitating ? (Snowing ? "Snow" : "Rain") : Today.SkyLabel)} {Mathf.RoundToInt(TempF)}°F · wind {Mathf.RoundToInt(WindMph)} mph";

        // ---------------------------------------------------------------- hook + save

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset()
        {
            FreeMonth = 5; FreeSky = Sky.Clear; FreeStorm = StormKind.None; Override = null; BatchSimulate = false; GroundOverride = null; _free = null; _freeKey = -1;
            Climate.Seed = 1; Climate.ClearCache();
            RandomizeSeedOnNewGame = !Application.isBatchMode;
        }

        /// <summary>A new game (MainMenu.NewGame) rolls a fresh weather seed so every career gets its own weather; the seed is
        /// saved with the "seasons" section, so a loaded career keeps it. Off in batch mode (tests keep seed 1).</summary>
        public static bool RandomizeSeedOnNewGame = true;

        /// <summary>Picks a new weather seed (2..int.MaxValue-1; 1 stays the default/test seed).</summary>
        public static int NewCareerSeed()
        {
            Climate.Seed = new System.Random(unchecked(Environment.TickCount * 31 + Guid.NewGuid().GetHashCode())).Next(2, int.MaxValue);
            Climate.ClearCache(); _free = null; _freeKey = -1;
            return Climate.Seed;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Weather.Provider = () => (Raining, TempF);
            SaveSystem.RegisterExtension("seasons", Save, Load);
            MainMenu.NewGameStarted += _ => { if (RandomizeSeedOnNewGame) NewCareerSeed(); };
        }

        [Serializable] class Data { public int seed = 1, freeMonth = 5, freeSky, freeStorm; public string rules = "", storms = ""; }
        /// <summary>Other Seasons parts (rules, storms) add their own JSON here.</summary>
        public static Func<string> SaveRules, SaveStorms; public static Action<string> LoadRules, LoadStorms;

        static string Save() => JsonUtility.ToJson(new Data { seed = Climate.Seed, freeMonth = FreeMonth, freeSky = (int)FreeSky, freeStorm = (int)FreeStorm, rules = SaveRules?.Invoke() ?? "", storms = SaveStorms?.Invoke() ?? "" });
        static void Load(string json)
        {
            var d = string.IsNullOrEmpty(json) ? new Data() : JsonUtility.FromJson<Data>(json);
            Climate.Seed = d.seed; Climate.ClearCache(); FreeMonth = d.freeMonth; FreeSky = (Sky)d.freeSky; FreeStorm = (StormKind)d.freeStorm; _free = null; _freeKey = -1;
            LoadRules?.Invoke(d.rules); LoadStorms?.Invoke(d.storms);
        }
    }
}
