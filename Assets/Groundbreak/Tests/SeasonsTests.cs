using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Seasons + weather rules + storms (Scripts/Seasons). Visual season shots are in SeasonsLookTests.</summary>
    public class SeasonsTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        GameMode _mode; int _day; float _hours; Wallet _wallet;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _mode = GameSettings.Mode; _wallet = Game.Wallet;
            if (GameClock.I != null) { _day = GameClock.I.Day; _hours = GameClock.I.Hours; }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SeasonWeather.Override = null; SeasonWeather.GroundOverride = null; SeasonWeather.BatchSimulate = false;
            SeasonWeather.FreeMonth = 5; SeasonWeather.FreeSky = Sky.Clear; SeasonWeather.FreeStorm = StormKind.None;
            StormEvents.Force = false; StormEvents.End(); SeasonBusiness.Force = false; SeasonBusiness.RestoreWeights();
            SeasonRules.ResetState();
            foreach (var id in new[] { SeasonRules.Blanket, SeasonRules.Heater, SeasonRules.ThawBlanket }) while (Game.Stock.Count(id) > 0) Game.Stock.TryTake(id, 1);
            GameSettings.Mode = _mode; Game.Wallet = _wallet;
            if (GameClock.I != null) { GameClock.I.Day = _day; GameClock.I.Hours = _hours; }
            Company.Offers.Clear();
            yield return Frames(2);
        }

        static DayWeather Day(float hi, float lo, Sky sky = Sky.Clear, float wind = 5f) =>
            new DayWeather { Day = 200, Sky = sky, HighF = hi, LowF = lo, WindMph = wind, GustMph = wind * 1.3f, PrecipIn = sky == Sky.Clear ? 0 : 0.5f, PrecipStart = 0, PrecipEnd = 24 };

        [Test]
        public void ClimateFollowsNorthernUsNormals()
        {
            Climate.Seed = 7; Climate.ClearCache();
            float[] hi = new float[12]; int[] n = new int[12], wet = new int[12], snow = new int[12];
            for (int d = 1; d <= 360 * 3; d++)
            {
                var w = Climate.Actual(d); int m = Climate.Month(d);
                hi[m] += w.HighF; n[m]++; if (w.Wet) wet[m]++; if (w.IsSnow) snow[m]++;
            }
            for (int m = 0; m < 12; m++)
            {
                Assert.AreEqual(Climate.NormalHigh[m], hi[m] / n[m], 6f, Climate.MonthNames[m] + " mean high");
                Assert.AreEqual(Climate.PrecipDays[m] / 30f, wet[m] / (float)n[m], 0.12f, Climate.MonthNames[m] + " wet-day odds");
            }
            Assert.Greater(snow[0] + snow[1] + snow[11], 20, "snow in winter");
            Assert.AreEqual(0, snow[5] + snow[6] + snow[7], "no snow in summer");
            Assert.Greater(Climate.DaylightHours(5, 21), 15f); Assert.Less(Climate.DaylightHours(11, 21), 9.5f);
            // ground: thawed in the April start, frozen deep by late winter, thawed again in summer
            Assert.IsFalse(Climate.GroundAt(1).Frozen);
            int feb = 1 + 30 * 10 + 15;   // career starts April: day 316 is mid-February
            Assert.AreEqual(1, Climate.Month(feb));
            Assert.IsTrue(Climate.GroundAt(feb).Frozen, "frozen in February"); Assert.Greater(Climate.GroundAt(feb).FrostIn, 10f);
            Assert.IsFalse(Climate.GroundAt(feb + 150).Frozen, "thawed in July");
            // storms: a handful a year, each kind in its season
            var storms = Enumerable.Range(1, 1080).Select(Climate.Actual).Where(w => w.Storm != StormKind.None).ToList();
            Assert.That(storms.Count, Is.InRange(6, 30), "storms in 3 years");
            Assert.IsTrue(storms.Where(w => w.Storm == StormKind.HeavySnow).All(w => Climate.SeasonOf(Climate.Month(w.Day)) == Seasons.Winter || Climate.Month(w.Day) == 2));
            Assert.IsTrue(storms.Where(w => w.Storm == StormKind.Hail).All(w => Climate.Month(w.Day) >= 3 && Climate.Month(w.Day) <= 7));
            for (int d = 1; d <= Climate.GraceDays; d++) { Assert.AreEqual(Sky.Clear, Climate.Actual(d).Sky); Assert.GreaterOrEqual(Climate.Actual(d).LowF, 40f); }
            Climate.Seed = 1; Climate.ClearCache();
        }

        [Test]
        public void ForecastIsMostlyButNotPerfectlyRight()
        {
            int right1 = 0, right6 = 0, total = 0;
            for (int today = 10; today < 370; today++)
            {
                total++;
                if (Climate.Forecast(today, today + 1, out _).Wet == Climate.Actual(today + 1).Wet) right1++;
                if (Climate.Forecast(today, today + 6, out _).Wet == Climate.Actual(today + 6).Wet) right6++;
                Assert.AreEqual(Climate.Actual(today).Wet, Climate.Forecast(today, today, out _).Wet, "today is known");
            }
            float a1 = right1 / (float)total, a6 = right6 / (float)total;
            Debug.Log($"[Seasons] forecast rain/dry right: 1 day out {a1:P0}, 6 days out {a6:P0}");
            Assert.Greater(a1, 0.8f); Assert.Less(a1, 1f); Assert.Less(a6, a1); Assert.Greater(a6, 0.6f);
        }

        [Test]
        public void DefaultsAreCalmAndRulesOffOutsideCareer()
        {
            GameSettings.Mode = GameMode.FreeBuild;
            Assert.AreEqual(Seasons.Summer, SeasonWeather.Season);
            Assert.AreEqual(Sky.Clear, SeasonWeather.Today.Sky);
            Assert.IsFalse(SeasonWeather.Precipitating);
            Assert.IsFalse(SeasonWeather.RulesOn);
            Assert.IsNull(SeasonRules.PourBlock(2f)); Assert.IsNull(SeasonRules.DigBlock());
            SeasonWeather.FreeMonth = 0; SeasonWeather.FreeSky = Sky.Snow;   // picker: winter snow — still no blocking in Free Build
            Assert.AreEqual(Seasons.Winter, SeasonWeather.Season); Assert.IsTrue(SeasonWeather.Snowing); Assert.Greater(SeasonWeather.Ground.SnowIn, 1f);
            Assert.IsNull(SeasonRules.FramingBlock("Shingles", "Shingle course"));
            // headless career runs are calm by default
            GameSettings.Mode = GameMode.Career; if (GameClock.I != null) GameClock.I.Day = 300;   // January
            if (Application.isBatchMode) { Assert.AreEqual(Sky.Clear, SeasonWeather.Today.Sky); Assert.IsFalse(SeasonWeather.Ground.Frozen); Assert.IsNull(SeasonRules.DigBlock()); }
            // tests forcing the old weather get calm, mild days in Career
            GameSettings.Mode = GameMode.Career; Weather.ForceRain = false;
            try { Assert.IsFalse(SeasonWeather.RulesOn); Assert.Greater(SeasonWeather.TempF, 45f); Assert.IsNull(SeasonRules.PourBlock(3f)); }
            finally { Weather.ForceRain = null; }
        }

        [Test]
        public void ColdPoursNeedBlanketsOrHeaterAndCureSlower()
        {
            GameSettings.Mode = GameMode.Career;
            SeasonWeather.Override = Day(36f, 22f);
            Game.Wallet = new Wallet(100000);
            var order = ConcreteDispatch.Place(new ConcreteOrder { Yards = 3f }, 60f, out string msg);
            Assert.IsNull(order, "pour ordered in the cold"); StringAssert.Contains("Too cold", msg); StringAssert.Contains("curing blankets", msg);
            Game.Stock.Add(SeasonRules.Blanket, SeasonRules.BlanketsFor(3f));
            Assert.IsNull(SeasonRules.PourBlock(3f), "blankets allow the pour");
            Game.Stock.TryTake(SeasonRules.Blanket, 1); Assert.NotNull(SeasonRules.PourBlock(3f), "one blanket short");
            Game.Stock.Add(SeasonRules.Heater, 1); Assert.IsNull(SeasonRules.PourBlock(3f), "a heater allows it");
            // maturity: cold cures slowly, blankets/heater help, warm is normal speed
            Assert.Less(SeasonRules.CureRate(35f, false, false), 0.5f);
            Assert.Greater(SeasonRules.CureRate(35f, true, false), SeasonRules.CureRate(35f, false, false) + 0.2f);
            Assert.Greater(SeasonRules.CureRate(20f, false, true), 0.8f);
            Assert.AreEqual(1f, SeasonRules.CureRate(70f, false, false), 0.01f);
            // rain stops pours outright
            SeasonWeather.Override = Day(65f, 50f, Sky.Rain);
            StringAssert.Contains("raining", SeasonRules.PourBlock(1f));
        }

        [Test]
        public void FrozenGroundBlocksDiggingUntilThawed()
        {
            GameSettings.Mode = GameMode.Career;
            SeasonWeather.Override = Day(20f, 5f);
            SeasonWeather.GroundOverride = new GroundState { FrostIn = 14f };
            StringAssert.Contains("frozen 14\"", SeasonRules.DigBlock());
            Assert.IsTrue(SeasonRules.DigBlocked());
            if (SiteGround.I != null) Assert.AreEqual(0f, SiteGround.I.DigBy(SiteGround.I.transform.position, 0.5f, 0.2f), "shovel bounces off");
            Game.Stock.Add(SeasonRules.ThawBlanket, SeasonRules.ThawBlanketsNeeded);
            StringAssert.Contains("Laid", SeasonRules.DigBlock());
            Assert.AreEqual(0, Game.Stock.Count(SeasonRules.ThawBlanket));
            StringAssert.Contains("thaw blankets are working", SeasonRules.DigBlock());
            ConcreteTime.Skip(15f * 60f);
            Assert.IsNull(SeasonRules.DigBlock(), "thawed after ~14 h");
        }

        [Test]
        public void RainWindColdAndHeatRules()
        {
            GameSettings.Mode = GameMode.Career;
            if (GameClock.I != null) GameClock.I.Hours = 14f;
            SeasonWeather.Override = Day(62f, 48f, Sky.Clear, 31f);
            StringAssert.Contains("Too windy", SeasonRules.FramingBlock("Roof trusses (24' span)", "Set truss"));
            StringAssert.Contains("Too windy", SeasonRules.FramingBlock("Roofing", "Shingles"));
            Assert.IsNull(SeasonRules.FramingBlock("Wall framing", "Nail studs"), "walls go on in the wind");
            SeasonWeather.Override = Day(62f, 48f, Sky.Rain, 6f);
            StringAssert.Contains("no roofing", SeasonRules.FramingBlock("Dry-in", "Underlayment"));
            Assert.IsNull(SeasonRules.FramingBlock("Roof trusses", "Set truss"), "calm rain: trusses OK");
            StringAssert.Contains("Wet weather", SeasonRules.ExteriorBlock("Paint sprayer"));
            SeasonWeather.Override = Day(44f, 30f);
            StringAssert.Contains("Too cold", SeasonRules.ExteriorBlock("Exterior paint brush"));
            Assert.IsNull(SeasonRules.ExteriorBlock("Siding nailer"));
            Assert.AreEqual(1f, SeasonRules.CrewSpeed("carpenter"), 0.11f);
            SeasonWeather.Override = Day(98f, 75f);
            Assert.Less(SeasonRules.CrewSpeed("carpenter"), 0.75f, "heat slows crews");
            StringAssert.Contains("heat", SeasonRules.CrewPaceNote());
            SeasonWeather.GroundOverride = new GroundState { Wetness = 1f };
            Assert.IsTrue(SeasonWeather.Ground.Muddy);
            Assert.Less(SeasonRules.CrewSpeed("laborer"), 0.65f, "heat + mud");
        }

        [UnityTest]
        public IEnumerator StormFloodsJobBoardAndDamagesHouses()
        {
            GameSettings.Mode = GameMode.Career;
            Town.Ensure();
            Company.ResetState(); Company.Offers.Clear(); Career.Reputation = 60;
            yield return Frames(2);
            StormEvents.Force = true;
            StormEvents.Trigger(StormKind.Windstorm, SeasonWeather.Day, 123);
            Assert.IsTrue(StormEvents.Active);
            Assert.GreaterOrEqual(StormEvents.DamagedLots.Count, 10);
            Assert.GreaterOrEqual(StormDamage.Count, 10, "damage visible on houses");
            StormEvents.Tick();
            var storm = Company.Offers.Where(j => j.Title.StartsWith(StormEvents.TitlePrefix)).ToList();
            Assert.GreaterOrEqual(storm.Count, 3, "storm repairs on the board");
            var allowed = StormEvents.DefsFor(StormKind.Windstorm).Select(id => JobCatalog.Get(id)).Where(d => d != null).ToList();
            foreach (var j in storm) Assert.IsTrue(allowed.Contains(JobCatalog.DefOf(j)), j.Title + " is not a wind repair");
            // premium: same def without the storm pays less
            var d0 = JobCatalog.DefOf(storm[0]); var plain = JobCatalog.CreateService(Town.Find(storm[0].Address), false, null, d0);
            Debug.Log($"[Seasons] storm job '{storm[0].Title}' ${storm[0].Payout} vs normal ${plain?.Payout}");
            // finishing a storm job clears that house's damage
            var lot = Town.Find(storm[0].Address); storm[0].Done = true;
            Assert.IsTrue(StormDamage.Has(lot.Id));
            StormEvents.Tick();
            Assert.IsFalse(StormDamage.Has(lot.Id), "damage gone once repaired");
            // a week later the rush is over
            GameClock.I.Day += StormEvents.SurgeDays;
            StormEvents.Tick();
            Assert.IsFalse(StormEvents.Active); Assert.AreEqual(0, StormDamage.Count);
            Town.Destroy();
        }

        [UnityTest]
        public IEnumerator SeasonalDemandPayAndEmergencies()
        {
            Town.Ensure(); yield return Frames(2);
            Assert.Greater(SeasonBusiness.PayFactor(Seasons.Summer), SeasonBusiness.PayFactor(Seasons.Winter));
            Assert.Less(SeasonBusiness.DemandFactor(Seasons.Winter, true), 0.5f);
            Assert.Greater(SeasonBusiness.DemandFactor(Seasons.Winter, false), 1f);
            Assert.Greater(SeasonBusiness.DemandFactor(Seasons.Summer, true), 1.2f);
            GameSettings.Mode = GameMode.Career; Company.ResetState(); Company.Offers.Clear();
            SeasonBusiness.Force = true;
            SeasonWeather.Override = new DayWeather { Day = 300, Sky = Sky.Clear, HighF = 8f, LowF = -12f, WindMph = 12f, GustMph = 20f };  // January cold snap
            Assert.AreEqual(Seasons.Winter, SeasonWeather.Season); Assert.IsTrue(SeasonBusiness.ColdSnap);
            var fence = JobCatalog.Get("ext.fence-section"); var faucet = JobCatalog.Get("plb.kitchen-faucet-drip");
            float wf = fence.Weight, wk = faucet.Weight;
            SeasonBusiness.Tick();
            Assert.Less(fence.Weight, wf * 0.5f, "outdoor work slow in winter"); Assert.Greater(faucet.Weight, wk, "indoor work up");
            var e = Company.Offers.FirstOrDefault(j => j.Title.StartsWith(SeasonBusiness.EmergencyPrefix));
            Assert.NotNull(e, "cold snap furnace call"); Assert.AreSame(JobCatalog.Get("hvac.no-heat"), JobCatalog.DefOf(e));
            SeasonBusiness.RestoreWeights();
            Assert.AreEqual(wf, fence.Weight);
            Town.Destroy();
        }

        [UnityTest]
        public IEnumerator WeatherAppForecastShot()
        {
            GameSettings.Mode = GameMode.Career; SeasonWeather.BatchSimulate = true;
            if (GameClock.I != null) { GameClock.I.Day = 40; GameClock.I.Hours = 9f; }
            yield return null;
            var rows = WeatherApp.ForecastRows();
            Assert.AreEqual(7, rows.Count);
            var lines = WeatherApp.Lines();
            Assert.IsTrue(lines.Any(l => l.StartsWith("7-DAY")));
            Assert.IsTrue(lines.Any(l => l.Contains("Concrete pours")));
            var cam = Vector3.up * 200f;
            var board = WeatherApp.BuildBoard(cam + Vector3.forward * 1.6f + Vector3.up * 0.55f + Vector3.left * 0.0f, Quaternion.identity);
            bool fog = RenderSettings.fog; RenderSettings.fog = false;
            SiteLookTests.Shot(cam, cam + Vector3.forward * 2f, "season_weather_app", 60f);
            RenderSettings.fog = fog;
            Object.Destroy(board);
        }
    }
}
