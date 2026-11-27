using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Real-world weather rules for work (Career only, see <see cref="SeasonWeather.RulesOn"/>). Every check returns a plain-language
    /// reason (null = OK) that the calling trade shows instead of failing silently. Mitigations real crews use: curing blankets or a propane
    /// heater (heated enclosure) for cold pours, ground-thaw blankets for frozen ground, tarps for material piles (old Weather).</summary>
    public static class SeasonRules
    {
        public const float PourMinF = 40f;        // ACI 306: below 40°F protect the concrete
        public const float PaintMinF = 50f;       // most exterior latex: 50°F+ (35°F products exist, crews still wait)
        public const float CaulkMinF = 40f;
        public const float ShingleSealF = 40f;    // seal strips won't bond below ~40°F: hand-seal
        public const float TrussWindMph = 25f;    // stop lifting / setting trusses: a truss is a sail
        public const float RoofWindMph = 30f;
        public const float HeatF = 90f;
        public const float SqFtPerBlanket = 150f; // 6' x 25' insulated curing blanket
        public const int ThawBlanketsNeeded = 2;

        public const string Blanket = "curing-blanket", Heater = "propane-heater", ThawBlanket = "thaw-blanket";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterItems()
        {
            void Add(string id, string name, int price) { if (!LandCatalog.Items.Exists(i => i.Id == id)) LandCatalog.Items.Add(new LandCatalog.Item { Id = id, Group = "Outdoor", Name = name, Price = price }); }
            Add(Blanket, "Insulated concrete curing blanket 6'x25' (cold-weather pours)", 85);
            Add(Heater, "Propane construction heater 125k BTU + tank (heated enclosure)", 340);
            Add(ThawBlanket, "Ground thaw blanket 6'x25' (thaws frozen ground for digging)", 140);
            SeasonWeather.SaveRules = Save; SeasonWeather.LoadRules = Load;
        }

        static int Stock(string id) => Game.Stock != null ? Game.Stock.Count(id) : 0;
        static int F(float t) => Mathf.RoundToInt(t);

        /// <summary>Lowest temperature over the next <paramref name="hours"/> hours (today's curve, then tomorrow's).</summary>
        public static float MinTempAhead(float hours)
        {
            float min = SeasonWeather.TempF, h0 = SeasonWeather.Hours;
            var today = SeasonWeather.Today;
            for (float h = 1; h <= hours; h += 1f)
            {
                float t = h0 + h;
                float temp = t < 24f || !SeasonWeather.Career || SeasonWeather.Override != null || !SeasonWeather.Simulated ? today.TempAt(t % 24f) : Climate.Actual(SeasonWeather.Day + 1).TempAt(t - 24f);
                min = Mathf.Min(min, temp);
            }
            return min;
        }

        // ------------------------------------------------------------------ concrete

        /// <summary>Blankets needed to cover a pour of this many cubic yards (≈4" slab: 81 sq ft per yard).</summary>
        public static int BlanketsFor(float yards) => Mathf.Max(1, Mathf.CeilToInt(yards * 81f / SqFtPerBlanket));

        /// <summary>Concrete orders (ReadyMix): rain, cold without protection, extreme heat.</summary>
        public static string PourBlock(float yards)
        {
            if (!SeasonWeather.RulesOn) return null;
            if (SeasonWeather.Precipitating)
                return SeasonWeather.Snowing ? "It's snowing - no pours. Snow in the forms ruins the concrete. Check the Weather app for a dry day."
                                             : "It's raining - no pours. Rain washes out the surface and weakens the slab. Check the Weather app for a dry day.";
            float low = MinTempAhead(24f);
            if (low < PourMinF && !Protected(yards))
                return $"Too cold to pour: it gets down to {F(low)}°F (concrete needs 40°F+ while it sets). Buy {BlanketsFor(yards)} curing blankets " +
                       "or a propane heater (Garden Center: Outdoor) to protect the pour, or wait for warmer days.";
            if (SeasonWeather.Ground.Frozen && !Thawed)
                return "The ground is frozen - never pour on frozen subgrade (it settles when it thaws). Thaw it first with ground thaw blankets.";
            return null;
        }

        static bool Protected(float yards) => Stock(Heater) > 0 || Stock(Blanket) >= BlanketsFor(yards);

        // per-element protection, cure delay
        class Protection { public ConcreteElement El; public int Blankets; public bool Heater; }
        static readonly List<Protection> _prot = new List<Protection>();
        public static bool IsProtected(ConcreteElement el) => _prot.Exists(p => p.El == el);
        public static int BlanketsOn(ConcreteElement el) { var p = _prot.Find(x => x.El == el); return p != null ? p.Blankets : 0; }

        /// <summary>A new slab: if it's cold, the crew lays blankets / sets the heater from site stock.</summary>
        public static void OnPoured(ConcreteElement el)
        {
            if (el == null || !SeasonWeather.RulesOn) return;
            float low = MinTempAhead(48f);
            if (low >= PourMinF) return;
            int need = BlanketsFor(el.Yards);
            var p = new Protection { El = el };
            if (Game.Stock.Count(Blanket) >= need) { Game.Stock.TryTake(Blanket, need); p.Blankets = need; }
            else if (Game.Stock.TryTake(Heater, 1)) p.Heater = true;
            else { Game.Toast($"Cold pour with no protection - it will drop to {F(low)}°F. The slab will cure very slowly and may be damaged by frost."); return; }
            _prot.Add(p);
            Game.Toast(p.Heater ? "Heater set up under poly over the slab (heated enclosure) - it cures at a warm pace."
                                : $"Crew covered the slab with {need} curing blankets - they come off when it's strong enough.");
        }

        /// <summary>Cure speed relative to 70°F (Nurse-Saul maturity, datum 14°F). Cement heat adds ~10°F, blankets ~+20°F, a heater keeps 60°F+.</summary>
        public static float CureRate(float airF, bool blankets, bool heater)
        {
            float t = airF + 10f + (blankets ? 20f : 0f);
            if (heater) t = Mathf.Max(t, 65f);
            return Mathf.Clamp((t - 14f) / (70f + 10f - 14f), 0.04f, 1.25f);
        }

        static float _lastNow = -1f;
        /// <summary>Called every frame by <see cref="SeasonDirector"/>: grows each slab's cure lag in cold, takes blankets off at 50% strength.</summary>
        public static void TickCure()
        {
            float now = ConcreteTime.Now;
            if (_lastNow < 0f || now < _lastNow) { _lastNow = now; return; }
            float dm = now - _lastNow; _lastNow = now;
            if (dm <= 0f || !SeasonWeather.RulesOn) return;
            // integrate in ≤60 min steps so a time skip uses the right hours' temperatures
            int steps = Mathf.Clamp(Mathf.CeilToInt(dm / 60f), 1, 24 * 30);
            float step = dm / steps;
            foreach (var el in ConcreteElement.All)
            {
                if (el == null || el.AgeDays > 28f) continue;
                var p = _prot.Find(x => x.El == el);
                float lag = 0f;
                for (int i = 0; i < steps; i++)
                {
                    float tm = now - dm + (i + 0.5f) * step;
                    int day = Mathf.FloorToInt(tm / 1440f) + 1; float hrs = (tm % 1440f) / 60f;
                    float air = SeasonWeather.Override != null || !SeasonWeather.Career ? SeasonWeather.Today.TempAt(hrs) : Climate.Actual(day).TempAt(hrs);
                    lag += step * (1f - CureRate(air, p != null && p.Blankets > 0, p != null && p.Heater));
                }
                el.CureLagMinutes = Mathf.Max(0f, el.CureLagMinutes + lag);
            }
            for (int i = _prot.Count - 1; i >= 0; i--)
            {
                var p = _prot[i];
                if (p.El == null) { _prot.RemoveAt(i); continue; }
                if (p.El.Strength01 >= 0.5f && MinTempAhead(24f) > 25f)
                {
                    if (p.Blankets > 0) Game.Stock.Add(Blanket, p.Blankets);
                    if (p.Heater) Game.Stock.Add(Heater, 1);
                    _prot.RemoveAt(i);
                    Game.Toast("Slab reached cold-weather protection strength - blankets/heater back in site stock");
                }
            }
        }

        // ------------------------------------------------------------------ digging

        static float _thawReadyAt = -1f, _thawUntil = -1f;
        public static bool Thawing => _thawReadyAt > 0f && ConcreteTime.Now < _thawReadyAt;
        public static bool Thawed => _thawReadyAt > 0f && ConcreteTime.Now >= _thawReadyAt && ConcreteTime.Now < _thawUntil;
        public static float ThawHoursLeft => Mathf.Max(0f, (_thawReadyAt - ConcreteTime.Now) / 60f);

        /// <summary>Frozen ground stops shovels, excavators and trenching. With ground-thaw blankets in stock the crew lays them
        /// (≈1 hour per inch of frost, at least 8 h); the patch stays workable for 5 days.</summary>
        public static string DigBlock()
        {
            if (!SeasonWeather.RulesOn) return null;
            var g = SeasonWeather.Ground;
            if (!g.Frozen || Thawed) return null;
            if (Thawing) return $"Ground thaw blankets are working - diggable in about {Mathf.CeilToInt(ThawHoursLeft)} h.";
            if (Game.Stock != null && Game.Stock.Count(ThawBlanket) >= ThawBlanketsNeeded)
            {
                Game.Stock.TryTake(ThawBlanket, ThawBlanketsNeeded);
                float hrs = Mathf.Max(8f, g.FrostIn);
                _thawReadyAt = ConcreteTime.Now + hrs * 60f; _thawUntil = _thawReadyAt + 5f * 1440f;
                return $"Ground is frozen {F(g.FrostIn)}\" deep. Laid {ThawBlanketsNeeded} ground thaw blankets - diggable in about {Mathf.CeilToInt(hrs)} h.";
            }
            return $"Ground is frozen {F(g.FrostIn)}\" deep - a shovel or bucket just bounces off. Lay {ThawBlanketsNeeded} ground thaw blankets " +
                   "(Garden Center: Outdoor) or wait for a thaw (Weather app).";
        }

        static float _lastDigToast = -10f;
        /// <summary>For SiteGround's dig ops: true = blocked (the reason is toasted, at most every 2 s).</summary>
        public static bool DigBlocked()
        {
            var why = DigBlock();
            if (why == null) return false;
            if (Time.unscaledTime - _lastDigToast > 2f || why.StartsWith("Ground is frozen") && why.Contains("Laid")) { Game.Toast(why); _lastDigToast = Time.unscaledTime; }
            return true;
        }

        // ------------------------------------------------------------------ framing / roofing

        public static bool IsTruss(string s) => s.Contains("truss") || s.Contains("rafter") || s.Contains("roof framing") || s.Contains("ridge");
        public static bool IsRoofing(string s) => s.Contains("shingle") || s.Contains("underlay") || s.Contains("dry-in") || s.Contains("dry in") || s.Contains("drip edge")
                                                 || s.Contains("ice & water") || s.Contains("flashing") || s.Contains("roof sheath") || s.Contains("roofing");

        static float _lastShingleWarn = -100f;
        /// <summary>FramingJob steps: wind stops truss setting and roofing, rain/snow stops roofing.</summary>
        public static string FramingBlock(string jobTitle, string stepLabel)
        {
            if (!SeasonWeather.RulesOn) return null;
            string s = ((jobTitle ?? "") + " " + (stepLabel ?? "")).ToLowerInvariant();
            float wind = SeasonWeather.WindMph, gust = SeasonWeather.GustMph;
            if (IsTruss(s))
            {
                if (wind >= TrussWindMph || gust >= TrussWindMph + 12f)
                    return $"Too windy to set roof framing: {F(wind)} mph, gusts {F(gust)} (crews stop at {F(TrussWindMph)} mph - a truss catches wind like a sail). Wait for calmer air.";
                if (SeasonWeather.Snowing) return "Snowing - framing members get slick. Wait for it to stop.";
            }
            else if (IsRoofing(s))
            {
                if (SeasonWeather.Precipitating) return $"{(SeasonWeather.Snowing ? "Snowing" : "Raining")} - no roofing. A wet deck traps moisture under the shingles and a slick roof is a fall hazard. Tarp it and wait.";
                if (wind >= RoofWindMph || gust >= RoofWindMph + 15f) return $"Too windy for roofing: {F(wind)} mph, gusts {F(gust)}. Wind rips underlayment and bundles off the roof.";
                if (SeasonWeather.Ground.SnowIn > 2f && s.Contains("shingle")) return "Snow on the roof deck - shovel it off and let it dry first.";
                if (s.Contains("shingle") && SeasonWeather.TempF < ShingleSealF && Time.unscaledTime - _lastShingleWarn > 30f)
                { _lastShingleWarn = Time.unscaledTime; Game.Toast($"{F(SeasonWeather.TempF)}°F: shingle seal strips won't bond until it warms up - hand-seal the tabs with roofing cement."); }
            }
            return null;
        }

        // ------------------------------------------------------------------ exterior finish + repairs

        static bool IsCoating(string s) => s.Contains("paint") || s.Contains("stain") || s.Contains("prime") || s.Contains("caulk") || s.Contains("seal");

        /// <summary>Exterior closeup tools (siding nailer, caulk, paint...).</summary>
        public static string ExteriorBlock(string toolName)
        {
            if (!SeasonWeather.RulesOn) return null;
            string s = (toolName ?? "").ToLowerInvariant();
            if (IsCoating(s)) return CoatingBlock(s);
            if (SeasonWeather.WindMph >= 35f) return $"Too windy to hang siding safely ({F(SeasonWeather.WindMph)} mph) - panels act like sails on a ladder.";
            return null;
        }

        static string CoatingBlock(string s)
        {
            if (SeasonWeather.Precipitating) return "Wet weather - exterior paint, stain and caulk wash off and won't bond. Wait for a dry day (Weather app).";
            float min = s.Contains("caulk") || s.Contains("seal") ? CaulkMinF : PaintMinF;
            if (SeasonWeather.TempF < min) return $"Too cold: {F(SeasonWeather.TempF)}°F. Exterior {(min == PaintMinF ? "paint" : "caulk")} needs {F(min)}°F+ to cure - try a warmer afternoon.";
            return null;
        }

        static bool OutdoorDefect(RepairDefect d)
        {
            if (d == null) return false;
            if (d.Type == RepairType.Catalog) { var def = JobCatalog.Get(d.S.def); return def != null && def.Where != null && def.Where.Outdoor; }
            var t = d.Type; return t == RepairType.Shingles || t == RepairType.Gutter || t == RepairType.FencePicket || t == RepairType.DeckBoard || t == RepairType.Sidewalk;
        }

        /// <summary>Repair-job actions outdoors: rain/cold stop coatings and roofing, wind stops roof work.</summary>
        public static string RepairBlock(RepairDefect d, RepairAction a)
        {
            if (!SeasonWeather.RulesOn || a == null || a.Diagnostic || !OutdoorDefect(d)) return null;
            string s = ((a.Id ?? "") + " " + (a.Label ?? "") + " " + (a.Tool ?? "")).ToLowerInvariant();
            bool roof = s.Contains("shingle") || s.Contains("roof") || s.Contains("flash") || d.Type == RepairType.Shingles;
            if (IsCoating(s)) { var c = CoatingBlock(s); if (c != null) return c; }
            if (roof && SeasonWeather.Precipitating) return "Wet weather - no work on the roof (slick and the patch won't seal). Wait for it to dry.";
            if (roof && SeasonWeather.WindMph >= RoofWindMph) return $"Too windy to be on the roof: {F(SeasonWeather.WindMph)} mph.";
            if (s.Contains("concrete") || s.Contains("mortar") || s.Contains("patch") && d.Type == RepairType.Sidewalk)
                if (SeasonWeather.TempF < PourMinF) return $"Too cold for concrete/mortar: {F(SeasonWeather.TempF)}°F (needs 40°F+).";
            return null;
        }

        // ------------------------------------------------------------------ crews

        /// <summary>Worker pace from weather (multiplies <c>Crew.Speed</c>; rain's 0.75 is already applied there).</summary>
        public static float CrewSpeed(string trade)
        {
            if (!SeasonWeather.RulesOn) return 1f;
            float s = 1f, t = SeasonWeather.TempF;
            if (t >= 95f) s *= 0.7f; else if (t >= HeatF) s *= 0.8f;      // OSHA heat: water, rest, shade breaks
            if (t < 20f) s *= 0.8f; else if (t < 32f) s *= 0.9f;         // bulky gloves, warm-up breaks
            var g = SeasonWeather.Ground;
            if (g.Muddy) s *= 0.85f;
            if (g.SnowIn > 3f) s *= 0.85f;                               // shoveling out the site
            if (SeasonWeather.WindMph >= 25f) s *= 0.9f;
            return s;
        }

        public static string CrewPaceNote()
        {
            if (!SeasonWeather.RulesOn) return "Normal pace";
            float t = SeasonWeather.TempF; var g = SeasonWeather.Ground; var why = new List<string>();
            if (t >= HeatF) why.Add("heat breaks"); if (t < 32f) why.Add("cold");
            if (g.Muddy) why.Add("mud"); if (g.SnowIn > 3f) why.Add("snow"); if (SeasonWeather.Raining) why.Add("rain");
            if (SeasonWeather.WindMph >= 25f) why.Add("wind");
            float sp = CrewSpeed("") * (SeasonWeather.Raining ? 0.75f : 1f);
            return why.Count == 0 ? "Normal pace" : $"{Mathf.RoundToInt(sp * 100f)}% pace ({string.Join(", ", why)})";
        }

        // ------------------------------------------------------------------ status board (Weather app)

        public static List<(string work, string why)> Status()
        {
            var l = new List<(string, string)>
            {
                ("Concrete pours", PourStatus()),
                ("Digging / excavation", DigStatus()),
                ("Roofing", FramingBlockQuiet("roofing shingles")),
                ("Setting trusses", FramingBlockQuiet("roof trusses")),
                ("Exterior paint", SeasonWeather.RulesOn ? CoatingBlock("paint") : null),
            };
            return l;
        }

        static string FramingBlockQuiet(string what) { float w = _lastShingleWarn; _lastShingleWarn = Time.unscaledTime; var r = FramingBlock(what, ""); _lastShingleWarn = w; return r; }
        static string PourStatus()
        {
            var b = PourBlock(1f);
            if (b != null && b.StartsWith("Too cold")) return $"Below 40°F (low {F(MinTempAhead(24f))}°F) - needs curing blankets or a heater";
            if (b == null && SeasonWeather.RulesOn && MinTempAhead(24f) < PourMinF) return null;
            return b;
        }
        static string DigStatus()
        {
            if (!SeasonWeather.RulesOn) return null;
            var g = SeasonWeather.Ground;
            if (!g.Frozen || Thawed) return g.Muddy ? null : null;
            return Thawing ? $"Thawing - about {Mathf.CeilToInt(ThawHoursLeft)} h" : $"Ground frozen {F(g.FrostIn)}\" - needs thaw blankets";
        }

        // ------------------------------------------------------------------ save

        [Serializable] class Data { public float thawReady = -1f, thawUntil = -1f; }
        static string Save() => JsonUtility.ToJson(new Data { thawReady = _thawReadyAt, thawUntil = _thawUntil });
        static void Load(string json)
        {
            var d = string.IsNullOrEmpty(json) ? new Data() : JsonUtility.FromJson<Data>(json);
            _thawReadyAt = d.thawReady; _thawUntil = d.thawUntil; _prot.Clear(); _lastNow = -1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _prot.Clear(); _thawReadyAt = _thawUntil = -1f; _lastNow = -1f; _lastShingleWarn = -100f; }

        /// <summary>Tests: forget thaw/protection state.</summary>
        public static void ResetState() => StaticReset();
    }
}
