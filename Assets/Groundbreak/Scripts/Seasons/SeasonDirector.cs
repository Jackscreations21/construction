using UnityEngine;

namespace Groundbreak
{
    /// <summary>Runs the Seasons feature each frame: concrete cure in cold, morning weather briefing, plain-language warnings when
    /// conditions change (wind over the truss limit, ground freezing, storm warnings), storm triggering + surge, seasonal business.</summary>
    public class SeasonDirector : MonoBehaviour
    {
        public static SeasonDirector I;
        int _briefedDay = -1, _stormDay = -1;
        bool _windy, _frozen;
        float _nextBiz;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("SeasonSystems");
            DontDestroyOnLoad(go);
            go.AddComponent<SeasonDirector>();
            Tablet.AddHelp("Weather app: 7-day forecast, what work the weather allows today, storm repair rushes");
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start() { ConcreteElement.Created -= SeasonRules.OnPoured; ConcreteElement.Created += SeasonRules.OnPoured; }

        void Update()
        {
            SeasonRules.TickCure();
            if (StormEvents.Force) StormEvents.Tick();
            if (!GameSettings.IsCareer || !SeasonWeather.Simulated) { _briefedDay = -1; return; }
            int day = SeasonWeather.Day; float h = SeasonWeather.Hours;
            if (day != _briefedDay && h >= 6f) { _briefedDay = day; Brief(day); }
            var t = SeasonWeather.Today;
            // storm hits
            if (t.Storm != StormKind.None && _stormDay != day && SeasonWeather.StormNow && StormEvents.StartDay != day)
            { _stormDay = day; StormEvents.Trigger(t.Storm, day); }
            // warnings when things change
            bool windy = SeasonWeather.WindMph >= SeasonRules.TrussWindMph;
            if (windy && !_windy) Game.Toast($"Wind {Mathf.RoundToInt(SeasonWeather.WindMph)} mph - truss setting and roof framing stop until it calms down");
            _windy = windy;
            bool frozen = SeasonWeather.Ground.Frozen;
            if (frozen && !_frozen) Game.Toast($"The ground has frozen ({Mathf.RoundToInt(SeasonWeather.Ground.FrostIn)}\" deep) - no digging without ground thaw blankets");
            if (!frozen && _frozen) Game.Toast("The ground has thawed - digging is possible again");
            _frozen = frozen;
            if (Time.unscaledTime >= _nextBiz)
            {
                _nextBiz = Time.unscaledTime + 3f;
                StormEvents.Tick();
                SeasonBusiness.Tick();
            }
        }

        /// <summary>Morning briefing + storm warning from the forecast.</summary>
        void Brief(int day)
        {
            var t = SeasonWeather.Today;
            string msg = $"{Climate.DateLabel(day)}: {t.SkyLabel}, {Mathf.RoundToInt(t.LowF)}-{Mathf.RoundToInt(t.HighF)}°F, wind {Mathf.RoundToInt(t.WindMph)} mph";
            if (t.Wet) msg += $" ({(t.IsSnow ? "snow" : "rain")} {Hour(t.PrecipStart)}-{Hour(t.PrecipEnd)})";
            if (t.LowF < SeasonRules.PourMinF) msg += " · cold: protect pours";
            if (SeasonWeather.Ground.Frozen) msg += " · ground frozen";
            Game.Toast(msg);
            for (int d = day + 1; d <= day + 3; d++)
            {
                var f = Climate.Forecast(day, d, out _);
                if (f.Storm != StormKind.None) { Game.Toast($"Storm watch: {Climate.StormName(f.Storm)} possible {Climate.DateLabel(d)} - plan around it (Weather app)"); break; }
            }
        }

        public static string Hour(float h)
        {
            int hh = Mathf.FloorToInt(Mathf.Repeat(h, 24f)); int h12 = hh % 12 == 0 ? 12 : hh % 12;
            return $"{h12} {(hh < 12 ? "AM" : "PM")}";
        }
    }
}
