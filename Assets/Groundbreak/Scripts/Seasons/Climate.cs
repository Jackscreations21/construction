using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum Sky { Clear, PartlyCloudy, Cloudy, Rain, Thunderstorm, Snow }
    public enum StormKind { None, Hail, Windstorm, HeavySnow, FloodRain }

    /// <summary>One day's weather (actual or forecast). Temperatures °F, wind mph, precipitation inches of water.</summary>
    [Serializable]
    public class DayWeather
    {
        public int Day;
        public Sky Sky;
        public StormKind Storm;
        public float HighF, LowF, WindMph, GustMph, PrecipIn;
        public float PrecipStart = -1f, PrecipEnd = -1f;   // hours of the day it rains / snows

        public bool Wet => Sky == Sky.Rain || Sky == Sky.Thunderstorm || Sky == Sky.Snow;
        public bool IsSnow => Sky == Sky.Snow;
        public bool PrecipAt(float hours) => Wet && hours >= PrecipStart && hours < PrecipEnd;

        /// <summary>Temperature at an hour: low near 6 AM, high near 3 PM (cosine between).</summary>
        public float TempAt(float hours)
        {
            float t;
            if (hours >= 6f && hours <= 15f) t = Mathf.Lerp(LowF, HighF, 0.5f - 0.5f * Mathf.Cos((hours - 6f) / 9f * Mathf.PI));
            else { float h = hours < 6f ? hours + 24f : hours; t = Mathf.Lerp(HighF, LowF, 0.5f - 0.5f * Mathf.Cos((h - 15f) / 15f * Mathf.PI)); }
            if (PrecipAt(hours) && !IsSnow) t -= 4f;
            return t;
        }

        public string SkyLabel => Storm != StormKind.None ? Climate.StormName(Storm) : Sky switch
        {
            Sky.Clear => "Sunny", Sky.PartlyCloudy => "Partly cloudy", Sky.Cloudy => "Cloudy", Sky.Rain => "Rain",
            Sky.Thunderstorm => "Thunderstorms", Sky.Snow => "Snow", _ => Sky.ToString()
        };

        public DayWeather Clone() => (DayWeather)MemberwiseClone();
    }

    /// <summary>Northern-US climate (a southern Wisconsin / Michigan town, about 43°N): monthly normals, daylight, precipitation odds,
    /// and a deterministic weather generator. Day-to-day temperature swings persist (cold snaps, heat waves) through an AR(1) anomaly.</summary>
    public static class Climate
    {
        public static readonly string[] MonthNames = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        // NOAA-style normals (°F) for ~43°N in the upper Midwest
        public static readonly float[] NormalHigh = { 27, 31, 43, 57, 69, 79, 83, 80, 72, 59, 45, 31 };
        public static readonly float[] NormalLow = { 11, 15, 25, 36, 47, 57, 62, 60, 51, 39, 28, 16 };
        // days with ≥0.01" precipitation per month
        public static readonly float[] PrecipDays = { 10, 8, 10, 11, 12, 11, 10, 10, 9, 9, 9, 10 };
        // hours of daylight at 43°N, mid-month
        public static readonly float[] Daylight = { 9.3f, 10.5f, 11.9f, 13.4f, 14.7f, 15.4f, 15.0f, 13.9f, 12.5f, 11.0f, 9.7f, 9.0f };
        public static readonly float[] MeanWind = { 11, 11, 12, 12, 10, 9, 8, 8, 9, 10, 11, 11 };

        /// <summary>Changes every generated day (new career = new weather). Saved.</summary>
        public static int Seed = 1;
        /// <summary>Career days 1..GraceDays are always fair and mild (the first job isn't rained out; tests start calm).</summary>
        public const int GraceDays = 3;

        public static string StormName(StormKind k) => k switch
        {
            StormKind.Hail => "Hailstorm", StormKind.Windstorm => "Windstorm", StormKind.HeavySnow => "Blizzard / heavy snow", StormKind.FloodRain => "Flooding rain", _ => ""
        };

        public static int Month(int day) => JobCatalog.Month(day);
        public static int DayOfMonth(int day) => (Mathf.Max(1, day) - 1) % 30 + 1;
        public static Seasons SeasonOf(int month) => month == 11 || month <= 1 ? Seasons.Winter : month <= 4 ? Seasons.Spring : month <= 7 ? Seasons.Summer : Seasons.Fall;
        public static string DateLabel(int day) => $"{MonthNames[Month(day)]} {DayOfMonth(day)}";

        /// <summary>Monthly normal smoothly interpolated (each month's value sits at the 15th).</summary>
        public static float Normal(float[] table, int month, int dom)
        {
            float f = (dom - 15.5f) / 30f; int other = f >= 0 ? (month + 1) % 12 : (month + 11) % 12;
            return Mathf.Lerp(table[month], table[other], Mathf.Abs(f));
        }

        public static float DaylightHours(int month, int dom) => Normal(Daylight, month, dom);

        // ------------------------------------------------------------------ generator

        static readonly Dictionary<int, DayWeather> _days = new Dictionary<int, DayWeather>();
        static readonly List<float> _anom = new List<float>();
        static int _cacheSeed = int.MinValue;

        public static void ClearCache() { _days.Clear(); _anom.Clear(); _cacheSeed = int.MinValue; _ground.Clear(); _groundSeed = int.MinValue; }

        static System.Random Rng(int day, int salt) => new System.Random(unchecked(Seed * 92821 + day * 7919 + salt * 104729 + 13));

        static float Gauss(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
        }

        /// <summary>Temperature anomaly (°F) that persists for days: a = 0.72·a(prev) + N(0, 6.5).</summary>
        static float Anomaly(int day)
        {
            if (_cacheSeed != Seed) { _days.Clear(); _anom.Clear(); _cacheSeed = Seed; }
            day = Mathf.Max(1, day);
            while (_anom.Count < day)
            {
                int d = _anom.Count + 1; float prev = _anom.Count > 0 ? _anom[_anom.Count - 1] : 0f;
                _anom.Add(d <= GraceDays ? 0f : 0.72f * prev + 6.5f * Gauss(Rng(d, 1)));
            }
            return _anom[day - 1];
        }

        /// <summary>The weather that actually happens on a career day (deterministic for a seed).</summary>
        public static DayWeather Actual(int day)
        {
            day = Mathf.Max(1, day);
            float a = Anomaly(day);
            if (_days.TryGetValue(day, out var cached)) return cached;
            int m = Month(day), dom = DayOfMonth(day);
            var r = Rng(day, 2);
            var w = new DayWeather { Day = day };
            float hi = Normal(NormalHigh, m, dom), lo = Normal(NormalLow, m, dom);
            w.HighF = hi + a + Gauss(r) * 2.5f; w.LowF = lo + a * 0.85f + Gauss(r) * 2.5f;
            if (w.LowF > w.HighF - 6f) w.LowF = w.HighF - 6f;
            w.WindMph = Mathf.Max(0f, MeanWind[m] * (0.45f + (float)r.NextDouble() * 0.9f));
            w.GustMph = w.WindMph * (1.4f + (float)r.NextDouble() * 0.4f);

            float pWet = PrecipDays[m] / 30f;
            if (day <= GraceDays)
            {
                w.Sky = Sky.Clear; w.HighF = Mathf.Max(hi, 62f); w.LowF = Mathf.Max(lo, 46f); w.WindMph = 6f; w.GustMph = 10f;
            }
            else if (r.NextDouble() < pWet)
            {
                float mean = (w.HighF + w.LowF) / 2f;
                bool snow = mean < 33f || (mean < 36f && r.NextDouble() < 0.5);
                bool thunder = !snow && m >= 4 && m <= 8 && r.NextDouble() < 0.35;
                w.Sky = snow ? Sky.Snow : thunder ? Sky.Thunderstorm : Sky.Rain;
                w.PrecipIn = 0.05f + (float)(r.NextDouble() * r.NextDouble()) * 0.9f;
                w.PrecipStart = thunder ? 13f + (float)r.NextDouble() * 6f : 3f + (float)r.NextDouble() * 14f;
                w.PrecipEnd = w.PrecipStart + (thunder ? 1f + (float)r.NextDouble() * 2f : 2f + (float)r.NextDouble() * 6f);
                if (thunder) { w.GustMph += 12f; }
            }
            else w.Sky = r.NextDouble() < 0.4 ? Sky.Clear : r.NextDouble() < 0.6 ? Sky.PartlyCloudy : Sky.Cloudy;

            // severe storms: a few a year, at least 10 days apart
            if (day > GraceDays + 4 && !StormWithin(day, 10)) ApplyStorm(w, RollStorm(day, m, w));
            _days[day] = w;
            return w;
        }

        static StormKind RollStorm(int day, int m, DayWeather w)
        {
            var r = Rng(day, 3); double x = r.NextDouble();
            float mean = (w.HighF + w.LowF) / 2f;
            // per-day odds by month (Midwest climatology: hail May–Jul, wind spring/fall, snowstorms Dec–Mar, flooding spring/summer)
            double hail = m >= 3 && m <= 7 ? (m >= 4 && m <= 6 ? 0.018 : 0.007) : 0;
            double wind = m == 2 || m == 3 || m == 4 || m == 9 || m == 10 ? 0.012 : 0.004;
            double snow = (m == 11 || m <= 2) && mean < 34f ? 0.025 : 0;
            double flood = m >= 3 && m <= 8 ? 0.01 : 0;
            if ((x -= hail) < 0) return StormKind.Hail;
            if ((x -= wind) < 0) return StormKind.Windstorm;
            if ((x -= snow) < 0) return StormKind.HeavySnow;
            if ((x -= flood) < 0) return StormKind.FloodRain;
            return StormKind.None;
        }

        static bool StormWithin(int day, int gap)
        {
            for (int d = Mathf.Max(GraceDays + 5, day - gap); d < day; d++) if (Actual(d).Storm != StormKind.None) return true;
            return false;
        }

        /// <summary>Turns a day into a storm of the given kind (also used by Free Build's picker and tests).</summary>
        public static void ApplyStorm(DayWeather w, StormKind k)
        {
            w.Storm = k;
            switch (k)
            {
                case StormKind.Hail: w.Sky = Sky.Thunderstorm; w.PrecipIn = Mathf.Max(w.PrecipIn, 0.8f); w.PrecipStart = 15f; w.PrecipEnd = 17f; w.GustMph = Mathf.Max(w.GustMph, 55f); w.WindMph = Mathf.Max(w.WindMph, 18f); w.HighF = Mathf.Max(w.HighF, 70f); break;
                case StormKind.Windstorm: w.Sky = Sky.Cloudy; w.WindMph = Mathf.Max(w.WindMph, 38f); w.GustMph = Mathf.Max(w.GustMph, 65f); break;
                case StormKind.HeavySnow: w.Sky = Sky.Snow; w.PrecipIn = Mathf.Max(w.PrecipIn, 1.4f); w.PrecipStart = 2f; w.PrecipEnd = 20f; w.WindMph = Mathf.Max(w.WindMph, 22f); w.GustMph = Mathf.Max(w.GustMph, 40f); w.HighF = Mathf.Min(w.HighF, 28f); w.LowF = Mathf.Min(w.LowF, 15f); break;
                case StormKind.FloodRain: w.Sky = Sky.Rain; w.PrecipIn = Mathf.Max(w.PrecipIn, 3.5f); w.PrecipStart = 1f; w.PrecipEnd = 22f; w.WindMph = Mathf.Max(w.WindMph, 15f); w.HighF = Mathf.Max(w.HighF, 50f); break;
            }
        }

        static readonly List<GroundState> _ground = new List<GroundState>();
        static int _groundSeed = int.MinValue;

        /// <summary>Ground at the START of a career day, from all the weather before it: snow depth (10:1 snow ratio, melts ~0.2"/°F-day
        /// above freezing, rain melts it), frost depth (≈1.2·√freezing-degree-days, snow cover insulates), soil wetness (dries faster when warm).
        /// The career starts in April with thawed, dry ground.</summary>
        public static GroundState GroundAt(int day)
        {
            day = Mathf.Max(1, day);
            if (_groundSeed != Seed) { _ground.Clear(); _groundSeed = Seed; }
            if (_ground.Count == 0) _ground.Add(new GroundState());
            while (_ground.Count < day)
            {
                var g = _ground[_ground.Count - 1]; var w = Actual(_ground.Count);
                float mean = (w.HighF + w.LowF) / 2f;
                // snow
                if (w.IsSnow) g.SnowIn += w.PrecipIn * 10f;
                if (mean > 32f) g.SnowIn -= (mean - 32f) * 0.2f + (w.Wet && !w.IsSnow ? w.PrecipIn * 4f : 0f);
                if (w.HighF > 40f && !w.IsSnow) g.SnowIn -= 0.5f;
                g.SnowIn = Mathf.Max(0f, g.SnowIn);
                // frost: freezing degree-days index (frost depth² / 1.44)
                float idx = g.FrostIn * g.FrostIn / 1.44f;
                if (mean < 32f) idx += (32f - mean) * (g.SnowIn > 4f ? 0.4f : 1f);
                else idx -= (mean - 32f) * (g.SnowIn > 1f ? 0.5f : 2.5f);
                g.FrostIn = Mathf.Sqrt(Mathf.Max(0f, idx)) * 1.2f;
                // wetness (mud)
                if (w.Wet && !w.IsSnow) g.Wetness += w.PrecipIn * 1.2f;
                if (mean > 32f && g.SnowIn > 0f) g.Wetness += 0.05f;   // snowmelt
                g.Wetness = Mathf.Max(0f, g.Wetness - (mean > 60f ? 0.35f : mean > 40f ? 0.2f : 0.08f));
                g.Wetness = Mathf.Min(g.Wetness, 3f);
                _ground.Add(g);
            }
            return _ground[day - 1];
        }

        /// <summary>Forecast issued on <paramref name="today"/> for <paramref name="day"/>: right ~85% of the time a day out, less further out.
        /// Temperatures drift a few degrees, rain chances blur, and storms can be missed or false-alarmed.</summary>
        public static DayWeather Forecast(int today, int day, out int precipChance)
        {
            var act = Actual(day); var f = act.Clone();
            int lead = Mathf.Max(0, day - today);
            var r = Rng(day * 31 + today, 4);
            float err = lead == 0 ? 1f : 1.5f + lead * 1.1f;
            f.HighF = act.HighF + Gauss(r) * err; f.LowF = act.LowF + Gauss(r) * err;
            if (f.LowF > f.HighF - 6f) f.LowF = f.HighF - 6f;
            f.WindMph = Mathf.Max(0, act.WindMph + Gauss(r) * lead * 1.2f);
            float miss = lead == 0 ? 0f : 0.07f + lead * 0.035f;   // chance the sky call is wrong
            bool wrong = r.NextDouble() < miss;
            if (act.Wet)
            {
                precipChance = Mathf.Clamp(Mathf.RoundToInt(90 - lead * 6 + Gauss(r) * 8), 30, 100);
                if (wrong) { precipChance = Mathf.RoundToInt(precipChance * 0.35f); f.Sky = Sky.PartlyCloudy; if (act.Storm != StormKind.Hail) f.Storm = StormKind.None; }
            }
            else
            {
                precipChance = Mathf.Clamp(Mathf.RoundToInt(5 + lead * 3 + Gauss(r) * 5), 0, 30);
                if (wrong) { precipChance = 40 + r.Next(25); f.Sky = (f.HighF + f.LowF) / 2f < 33f ? Sky.Snow : Sky.Rain; }
            }
            if (act.Storm != StormKind.None && lead > 5) f.Storm = StormKind.None;  // storms show up ~5 days out
            if (lead == 0) { precipChance = act.Wet ? 100 : 0; f.Sky = act.Sky; f.Storm = act.Storm; }
            return f;
        }
    }
}
