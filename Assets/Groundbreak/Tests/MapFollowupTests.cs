using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>MapFollowup: vacant-lot dressing, plans on map buildings, catalog jobs across the whole map, crew out of the road. Shots Logs/shots/followup_*.</summary>
    public class MapFollowupTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        float _hours = -1f;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlanOverlay.Hide();
            Town.Destroy();
            Company.ResetState();
            MapStreamer.Frozen = false;
            if (GameClock.I != null && _hours >= 0) GameClock.I.Hours = _hours;
            yield return Frames(2);
        }

        IEnumerator BuildTown()
        {
            yield return Frames(2);
            Town.Ensure();
            if (GameClock.I != null) { _hours = GameClock.I.Hours; GameClock.I.Hours = 10.5f; }
            yield return Frames(3);
        }

        static void Shot(Vector3 pos, Vector3 look, string name, float fov = 60f, bool noFog = false)
        {
            bool fog = RenderSettings.fog;
            if (noFog) RenderSettings.fog = false;
            SiteLookTests.Shot(pos, look, name, fov);
            RenderSettings.fog = fog;
        }

        [UnityTest]
        public IEnumerator VacantLotsAreDressedAndClearWhenClaimed()
        {
            yield return BuildTown();
            var free = Town.FreeLots.Where(l => l.TypeId == null).ToList();
            Assert.Greater(free.Count, 20, "free lots");
            foreach (var l in free)
            {
                Assert.IsTrue(VacantLots.ByLot.TryGetValue(l.Id, out var go) && go != null, l.Id + " not dressed");
                Assert.IsTrue(go.activeSelf, l.Id + " dressing hidden while free");
                Assert.NotNull(go.GetComponentInChildren<MeshRenderer>(), l.Id + " has no dressing mesh");
            }
            Assert.IsFalse(TownBuilder.ForSaleSigns, "core for-sale signs are per lot now (hidden on claim)");
            // claim → dressing (sign, stakes, weeds...) goes; release → back
            var core = free.First(l => !l.External && !LotBroker.IsTract(l));
            var map = free.First(l => l.External && l.District == "north");
            Assert.IsTrue(Town.Claim(core, LotUse.BuildSite, "test"));
            Assert.IsFalse(VacantLots.ByLot[core.Id].activeSelf, "dressing removed on a claimed lot");
            Town.Release(core);
            Assert.IsTrue(VacantLots.ByLot[core.Id].activeSelf);
            Debug.Log($"[Followup] {VacantLots.Dressed} vacant lots dressed");

            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            Shot(core.FrontMid + core.Front * 9f + Vector3.up * 1.7f + core.Rotation * Vector3.right * 4f, core.Local(0, 20f) + Vector3.up * 1f, "followup_01_vacant_core_eye");
            Shot(map.FrontMid + map.Front * 9f + Vector3.up * 1.7f - map.Rotation * Vector3.right * 5f, map.Local(0, 18f) + Vector3.up * 1f, "followup_02_vacant_north_eye");
            Shot(map.Local(0, 4f) + Vector3.up * 1.2f, map.Local(3f, 30f) + Vector3.up * 0.6f, "followup_03_vacant_inside");
            var home = Town.Home.Parking + Vector3.up * 1.7f;
            Shot(home, home + Vector3.forward * 50f, "followup_04_home_north");
            Shot(new Vector3(0f, 170f, -260f), new Vector3(0f, 0f, -120f), "followup_05_aerial_core", 60f, true);
            Shot(map.Center + new Vector3(-60f, 90f, -70f), map.Center, "followup_06_aerial_north", 55f, true);
        }

        [UnityTest]
        public IEnumerator MapBuildingsCarryPlans()
        {
            yield return BuildTown();
            var withPlans = MapPlans.LotsWithPlans().ToList();
            Debug.Log($"[Followup] {withPlans.Count} buildings with a plan on file, {MapPlans.RanchesAdded} map houses made enterable, {MapJobs.ServiceDistricts()} districts with service-call houses");
            Assert.Greater(withPlans.Count, 150, "a lot of buildings have plans");
            foreach (var (type, prefix) in new[] { ("diner", "diner-"), ("gasstation", "gasstation-"), ("office", "office-"), ("warehouse", "warehouse-"), ("townhomes", "townhome-"), ("duplex", "duplex-"), ("stripmall", "stripmall-"), ("twostory", "twostory-") })
            {
                var l = Town.Lots.FirstOrDefault(x => x.TypeId == type);
                if (l == null) continue;
                Assert.NotNull(MapLayout.PlanFor(l), type + " has a plan");
                StringAssert.StartsWith(prefix, l.PlanId, type);
            }
            Assert.Greater(MapPlans.RanchesAdded, 8, "more enterable houses on the map");
            Assert.GreaterOrEqual(MapJobs.ServiceDistricts(), 3, "service-call houses in several districts");
            Assert.IsTrue(Town.Addresses.Where(l => l.ServiceCalls).All(l => l.House != null), "every service-call address is enterable");

            var diner = Town.Lots.First(l => l.TypeId == "diner" && l.External);
            Assert.NotNull(BuildingPlansApp.ShowOver(diner), "blueprint laid over the diner");
            Assert.Greater(PlanOverlay.WallCount, 4);
            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            Assert.IsTrue(BuildingPlansApp.Footprint(diner, out var c, out var rot, out _, out float roof));
            var front = rot * Vector3.forward;
            Shot(c + front * 26f + Vector3.up * (roof + 16f) + rot * Vector3.right * 10f, c + Vector3.up * roof, "followup_10_diner_blueprint");
            PlanOverlay.Hide();
            var wh = Town.Lots.First(l => l.TypeId == "townhomes" && l.External);
            BuildingPlansApp.ShowOver(wh);
            Assert.IsTrue(BuildingPlansApp.Footprint(wh, out c, out rot, out _, out roof));
            Shot(c + rot * Vector3.forward * 24f + Vector3.up * (roof + 14f), c + Vector3.up * roof, "followup_11_townhomes_blueprint");
        }

        [UnityTest]
        public IEnumerator CatalogJobsReachTheWholeMap()
        {
            yield return BuildTown();
            Company.Stage = CompanyStage.Builder;
            // service calls: the address picker reaches outer-map houses
            var svc = JobCatalog.All.First(d => d.Mode == JobMode.Service && d.Where != null && d.Where.Name == "toilet");
            var districts = new HashSet<string>(); int outer = 0;
            // Westside has a single enterable house (~2% of the travel-weighted picks): an unseeded run misses it about 1 time in 80.
            // Seed the picks so the spread check is deterministic (the RNG state is restored afterwards).
            var rngState = Random.state; Random.InitState(20261127);
            for (int i = 0; i < 200; i++)
            {
                var l = JobCatalog.FreeAddressFor(svc);
                Assert.NotNull(l);
                districts.Add(l.External ? l.District : "oldtown");
                if (l.External) outer++;
            }
            Random.state = rngState;
            Assert.GreaterOrEqual(districts.Count, 3, "service calls across districts: " + string.Join(",", districts));
            Assert.Greater(outer, 20, "service calls at map houses");
            // contractor jobs: any empty house lot in town
            int mapLots = 0;
            for (int i = 0; i < 60; i++) { var (_, addr) = MapJobs.ContractorLot(i); var l = Town.Find(addr); if (l != null && l.External) mapLots++; Assert.IsTrue(l == null || l.Category == "residential", addr); }
            Assert.Greater(mapLots, 5, "contractor jobs on map lots");
            // builds: commercial plans on commercial lots
            var dinerPlan = HousePlans.Get("diner-3000");
            if (dinerPlan != null)
            {
                var first = MapJobs.BuildLotsFor(dinerPlan).FirstOrDefault();
                Assert.NotNull(first);
                Assert.AreEqual("commercial", CommercialSites.TownLotOf(first).Category, "diner build on a commercial lot (or a pad site on a commercial tract)");
            }
            // a real service call at a far-out house, played at that house
            var far = Town.Addresses.Where(l => l.External && l.House != null && JobCatalog.Fits(l.House, svc)).OrderByDescending(l => (l.Center - Town.Home.Center).sqrMagnitude).First();
            var job = JobCatalog.CreateService(far, true, null, svc);
            Assert.NotNull(job);
            Assert.AreEqual(far.Address, job.Address);
            Debug.Log($"[Followup] outer-map job '{job.Title}' at {job.Address} ({far.District}), {(far.Center - Town.Home.Center).magnitude:0} m from home");
            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            Shot(far.FrontMid + far.Front * 10f + Vector3.up * 2.2f + far.Rotation * Vector3.right * 7f, far.Local(0, 10f) + Vector3.up * 1.5f, "followup_20_job_outer_map");
        }

        [UnityTest]
        public IEnumerator CrewWaitsInsideTheLotNotInTheRoad()
        {
            yield return BuildTown();
            var j = ContractorJobs.Create("fence", 31337);
            Assert.NotNull(j);
            Company.ActiveJobs.Add(j);
            var at = CrewTownLink.ArrivalFor(j.Id);
            Assert.IsTrue(at.HasValue);
            Assert.IsFalse(Town.OnRoad(at.Value), "crew arrival is off the street");
            var l = Town.Find(j.Address);
            if (l != null) Assert.IsTrue(l.Rect.Contains(new Vector2(at.Value.x, at.Value.z)), "crew waits on the lot");
            Company.ActiveJobs.Remove(j);
        }
    }
}
