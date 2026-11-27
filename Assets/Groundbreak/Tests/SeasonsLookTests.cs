using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Season / weather visuals (Scripts/Seasons: SeasonLook, WeatherFx, StormDamage): the same Birch St view in four seasons,
    /// rain, a storm and storm damage → Logs/shots/season_*.png.</summary>
    public class SeasonsLookTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        float _hours = -1f;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SeasonWeather.FreeMonth = 5; SeasonWeather.FreeSky = Sky.Clear; SeasonWeather.FreeStorm = StormKind.None;
            SeasonWeather.Override = null; SeasonWeather.GroundOverride = null;
            StormDamage.ClearAll();
            WeatherFx.FollowOverride = null;
            Town.Destroy();
            MapStreamer.Frozen = false;
            if (GameClock.I != null && _hours >= 0) GameClock.I.Hours = _hours;
            yield return Frames(3);
            SeasonLook.Refresh();
        }

        TownLot _ranch;
        Vector3 _cam, _look;

        IEnumerator Setup()
        {
            yield return Frames(2);
            GameSettings.Mode = GameMode.FreeBuild;
            Town.Ensure();
            if (GameClock.I != null) { _hours = GameClock.I.Hours; GameClock.I.Hours = 11f; }
            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            _ranch = Town.Lots.Where(l => l.TypeId == "ranch" && l.External).OrderBy(l => l.Center.x).Skip(2).First();
            var right = _ranch.Rotation * Vector3.right;
            _cam = _ranch.FrontMid + _ranch.Front * 11f + right * 16f + Vector3.up * 1.7f;
            _look = _ranch.Local(-6f, 12f) + Vector3.up * 3f;
        }

        IEnumerator Shoot(string name, Vector3? cam = null, Vector3? look = null, float fov = 62f)
        {
            yield return Frames(2);
            SeasonLook.Refresh();
            yield return Frames(2);
            WeatherFx.FollowOverride = cam ?? _cam;
            WeatherFx.Settle();
            SiteLookTests.Shot(cam ?? _cam, look ?? _look, name, fov);
        }

        [UnityTest]
        public IEnumerator FourSeasonsSameStreet()
        {
            yield return Setup();
            SeasonWeather.FreeMonth = 5; yield return Shoot("season_summer");
            Assert.AreEqual("Summer/0/0", SeasonLook.LookKey, "default Free Build June is the stock look");
            var grass = Materials.Get(SurfaceKind.Grass); var stock = grass.color;

            SeasonWeather.FreeMonth = 4; yield return Shoot("season_spring");
            StringAssert.StartsWith("Spring/", SeasonLook.LookKey);

            SeasonWeather.FreeMonth = 9; yield return Shoot("season_fall");
            StringAssert.StartsWith("Fall/", SeasonLook.LookKey);
            Assert.AreNotEqual(stock, grass.color, "fall grass is tan");

            SeasonWeather.FreeMonth = 0; yield return Shoot("season_winter");
            StringAssert.StartsWith("Winter/", SeasonLook.LookKey);
            Assert.IsTrue(Resources.FindObjectsOfTypeAll<Material>().Any(m => m.name.StartsWith("GBL_Foliage") && m.IsKeywordEnabled("_ALPHATEST_ON")), "bare deciduous trees");
            Assert.Greater(SeasonWeather.Ground.SnowIn, 0.5f);

            SeasonWeather.FreeMonth = 5; yield return Frames(2); SeasonLook.Refresh();
            Assert.AreEqual(stock, grass.color, "summer restores the stock grass");
            Assert.IsFalse(Resources.FindObjectsOfTypeAll<Material>().Any(m => m.name.StartsWith("GBL_Foliage") && m.IsKeywordEnabled("_ALPHATEST_ON")), "leaves back");
        }

        [UnityTest]
        public IEnumerator RainAndStorm()
        {
            yield return Setup();
            SeasonWeather.FreeMonth = 5; SeasonWeather.FreeSky = Sky.Rain;
            yield return Shoot("season_rain");
            StringAssert.EndsWith("/2", SeasonLook.LookKey, "wet look while raining");
            Assert.IsTrue(Weather.IsRaining);

            SeasonWeather.FreeSky = Sky.Snow; SeasonWeather.FreeMonth = 0; SeasonWeather.FreeStorm = StormKind.HeavySnow;
            yield return Shoot("season_storm");
            Assert.IsTrue(SeasonWeather.Snowing && SeasonWeather.StormNow);

            SeasonWeather.FreeSky = Sky.Thunderstorm; SeasonWeather.FreeMonth = 6; SeasonWeather.FreeStorm = StormKind.Hail;
            yield return Shoot("season_storm_hail");
            SeasonWeather.FreeSky = Sky.Cloudy; SeasonWeather.FreeStorm = StormKind.Windstorm;
            yield return Shoot("season_storm_wind");
        }

        [UnityTest]
        public IEnumerator StormDamageOnHouses()
        {
            yield return Setup();
            var ranch = _ranch;
            var map = Town.Lots.First(l => l.External && l.TypeId == "twostory" && MapLayout.ParcelOf(l)?.Spec != null);
            var cape = Town.Lots.Where(l => l.External && (l.TypeId == "cape" || l.TypeId == "foursquare") && MapLayout.ParcelOf(l)?.Spec != null).Skip(1).First();
            var other = Town.Lots.Where(l => l.TypeId == "ranch" && l.External && l != ranch).First();
            Assert.NotNull(StormDamage.Add(ranch, StormKind.Windstorm, 1));
            Assert.NotNull(StormDamage.Add(map, StormKind.Hail, 2));
            Assert.NotNull(StormDamage.Add(cape, StormKind.FloodRain, 3));
            Assert.NotNull(StormDamage.Add(other, StormKind.HeavySnow, 4));
            Assert.AreEqual(4, StormDamage.Count); Assert.IsTrue(StormDamage.Has(ranch.Id));

            Vector3 View(TownLot l, float side, float back) => l.FrontMid + l.Front * back + l.Rotation * Vector3.right * side + Vector3.up * 1.7f;
            Vector3 At(TownLot l) => (MapLayout.ParcelOf(l)?.Origin ?? l.Local(0, 9f)) + Vector3.up * 2.2f;
            yield return Shoot("season_damage", View(ranch, 7f, 6f), ranch.Local(0f, 9f) + Vector3.up * 1.5f, 66f);
            yield return Shoot("season_damage_hail", View(map, -5f, 7f), At(map) + Vector3.up * 1.5f, 66f);
            yield return Shoot("season_damage_flood", View(cape, 6f, 5f), At(cape) - Vector3.up * 1.2f, 66f);
            SeasonWeather.FreeMonth = 0;
            yield return Shoot("season_damage_snow", View(other, -6f, 5f), other.Local(0f, 9f) + Vector3.up * 2f, 66f);

            StormDamage.Clear(ranch.Id);
            Assert.IsFalse(StormDamage.Has(ranch.Id)); Assert.AreEqual(3, StormDamage.Count);
        }
    }
}
