using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>
    /// The whole business career as one game, through the public APIs: new Career → Handyman (van, $2,500, home driveway, a service
    /// call) → Contractor (warehouse + pickup, a fence job on a town lot) → Crew (two hires finish a contractor job off site, tasks
    /// tagged with the town job) → Builder (yard + shop, rep 65, a client house on a town lot built by the crew) → Developer (HQ,
    /// 8 workers, rep 80, a land listing bought, platted, spec houses built and sold). Money and reputation are granted where a real
    /// playthrough would take hours. Mid-career save + load round trip. Screenshots → Logs/shots/career_*.png.
    /// </summary>
    public class CareerPlaythroughTests
    {
        const string Slot = "career_playthrough_test";
        int _money; GameMode _mode; Vector3 _pos; float _yaw; int _day; float _hours;
        readonly List<GameObject> _built = new List<GameObject>();

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(2);
            _money = Game.Wallet.Money; _mode = GameSettings.Mode;
            if (Game.Player != null) { _pos = Game.Player.transform.position; _yaw = Game.Player.transform.eulerAngles.y; }
            if (GameClock.I != null) { _day = GameClock.I.Day; _hours = GameClock.I.Hours; }
            Weather.ForceRain = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var g in _built) if (g != null) Object.Destroy(g);
            _built.Clear();
            if (SaveSystem.Exists(Slot)) SaveSystem.Delete(Slot);
            MainMenu.NewGame(GameMode.FreeBuild);           // resets every feature's state through the save extensions; Free Build has no town
            WorkVan.Despawn(); Town.Destroy(); LotBroker.ReleaseAll();
            Crew.ResetState(); Projects.ResetState(); Developments.ResetState(); DevVisuals.RefreshNow();
            Company.ResetState(); YardFleetRuntime.ResetAll(); RepairJobs.ResetState();
            CrewManager.ArrivalOverride = null; Weather.ForceRain = null;
            GameSettings.Mode = _mode; GameSettings.UnlimitedMoney = false;
            Game.Wallet = new Wallet(_money);
            if (GameClock.I != null) { GameClock.I.Day = _day; GameClock.I.Hours = _hours; }
            HandymanStart.MovePlayer(_pos, _yaw);
            yield return Frames(3);
        }

        // ------------------------------------------------------------------ helpers

        static void Shot(Vector3 cam, Vector3 look, string name, float fov = 60f)
        {
            float h = GameClock.I.Hours; GameClock.I.Hours = 11.5f;   // midday light for every shot (day steps leave the clock at dawn)
            Weather.ForceRain = false;
            SiteLookTests.Shot(cam, look, name, fov);
            GameClock.I.Hours = h;
        }

        /// <summary>One game day through the real rollover (Company.Tick → daily costs → DayStarted for every feature).</summary>
        static IEnumerator NextDay()
        {
            GameClock.I.Day++; GameClock.I.Hours = 11.5f;
            yield return null; yield return null;
        }

        static void Grant(int dollars, string why) => Game.Wallet.Add(dollars, "Test grant: " + why);

        /// <summary>No two offers / active jobs name the same town lot.</summary>
        static void AssertOneJobPerLot(string stage)
        {
            var seen = new Dictionary<string, string>();
            foreach (var list in new[] { Company.Offers, Company.ActiveJobs })
                foreach (var j in list)
                {
                    var l = Town.Find(j.Address);
                    if (l == null || j.Kind == JobKind.Repair && j.Title.StartsWith("Callback")) continue;
                    if (seen.TryGetValue(l.Id, out var prev)) Assert.Fail($"{stage}: lot {l.Id} named by '{prev}' and '{j.Title}'");
                    seen[l.Id] = j.Title;
                }
        }

        static Job Find(System.Predicate<Job> p) => Company.Offers.Find(p);

        // ------------------------------------------------------------------ the test

        [UnityTest]
        public IEnumerator HandymanToDeveloper_OneGame()
        {
            Assert.NotNull(GameClock.I, "game clock");
            // ================================================================ 0. new Career game
            MainMenu.NewGame(GameMode.Career);
            GameClock.I.Hours = 10f;
            yield return Frames(3);
            Assert.IsTrue(GameSettings.IsCareer);
            Assert.IsTrue(Town.Exists, "new career builds the town");
            Assert.AreEqual(CompanyStage.Handyman, Company.Stage);
            Assert.AreEqual(HandymanStart.StartingCash, Game.Wallet.Money, "starts with $2,500");
            Assert.IsTrue(Company.Owns("van"));
            Assert.AreEqual(1, Company.Vehicles.Count);
            Assert.NotNull(WorkVan.I, "the work van is parked at home");
            Assert.Less(Vector3.Distance(WorkVan.I.transform.position, Town.Home.Parking), 1f, "van in the home driveway");
            Assert.Less(Vector3.Distance(Game.Player.transform.position, Town.Home.Parking), 6f, "player starts beside the van");
            Assert.IsTrue(Company.Offers.Exists(j => j.Kind == JobKind.Repair), "service calls on the board");
            AssertOneJobPerLot("handyman");

            // tablet: one Jobs app, business apps grouped, nothing duplicated
            var names = new List<string>((string[])typeof(Tablet).GetMethod("AppNames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null));
            Assert.AreEqual(names.Count, new HashSet<string>(names).Count, "no duplicate apps");
            foreach (var a in IntegrationRuntime.BusinessApps) Assert.Contains(a, names, a + " app in career");
            foreach (var a in IntegrationRuntime.MergedIntoJobs) Assert.IsFalse(names.Contains(a), a + " is a tab of Jobs now");

            var van = WorkVan.I.transform;
            Shot(van.position - van.forward * 8f + van.right * 5f + Vector3.up * 3.2f, van.position + Vector3.up * 1.2f, "career_1_handyman");

            // a service call: tripped GFCI at a customer's house
            TownLot custLot = null;
            foreach (var l in Town.Addresses) if (!Company.Offers.Exists(j => j.Address == l.Address)) { custLot = l; break; }
            Assert.NotNull(custLot);
            var repair = RepairJobs.CreateJob(custLot, true, (RepairType.DeadOutlet, HouseSpot.OutletLiving, DeadOutletDefect.TrippedGfci));
            Assert.IsTrue(Company.Accept(repair, out var msg), msg);
            var gfci = RepairJobs.DefectsFor(repair).Find(d => d.Type == RepairType.DeadOutlet);
            Assert.NotNull(gfci);
            foreach (var a in new[] { "gfci-reset", "test" }) Assert.IsTrue(gfci.Perform(a, RepairContext.Bypass, out msg), a + ": " + msg);
            int before = Game.Wallet.Money;
            Assert.NotNull(RepairJobs.SendInvoice(repair));
            Assert.Greater(Game.Wallet.Money, before, "customer paid");
            Assert.AreEqual(1, Company.JobsCompleted, "handyman job done");
            Assert.AreEqual(CompanyStage.Handyman, Company.Stage);

            // ================================================================ 1. Contractor: warehouse + truck
            Grant(150000, "months of service calls");
            Company.Reputation = 55;
            Assert.IsTrue(Yard.Lease(1, out msg), msg);
            yield return Frames(3);
            var yardLot = LotBroker.YardLot;
            Assert.NotNull(yardLot, "the warehouse is on a town lot");
            Assert.AreEqual(LotUse.Company, yardLot.Use);
            Assert.AreNotEqual(Town.Home, yardLot);
            Assert.Less(Vector3.Distance(Yard.Position, Town.HQPosition), 0.01f, "Town.HQPosition follows the yard");
            Assert.IsTrue(yardLot.Contains(Yard.Position - yardLot.Front * 0.1f), "yard origin on its lot");
            Assert.NotNull(YardSite.I);
            Assert.Less(Vector3.Distance(YardSite.I.transform.position, Yard.Position), 0.01f, "premises built on the yard lot, not at home");
            Assert.Less(Vector3.Distance(WorkVan.I.transform.position, Yard.Position), 60f, "the van moved into its stall at the yard");
            Assert.AreEqual(CompanyStage.Handyman, Company.Stage, "a warehouse alone is not enough");
            Assert.NotNull(Fleet.Buy("pickup", true, 0, out msg), msg);
            yield return Frames(3);
            Assert.AreEqual(CompanyStage.Contractor, Company.Stage);
            Assert.IsTrue(Company.Offers.Exists(j => j.Kind == JobKind.Remodel || j.Kind == JobKind.Outbuilding), "contractor jobs on the board");
            foreach (var j in Company.Offers)
                if (ContractorJobs.IsContractorJob(j)) { var l = Town.Find(j.Address); Assert.NotNull(l, j.Title + " on a town lot"); Assert.IsTrue(l.IsFree && !LotBroker.IsTract(l), j.Address + " is an empty house lot"); }
            AssertOneJobPerLot("contractor");
            // one van: the yard does not draw a second copy of the van you drive; the pickup is parked there
            Assert.AreEqual(1, YardSite.I.ParkedVehicles.Count, "pickup in a stall, van not duplicated");
            Shot(YardSite.I.CameraSpot, YardSite.I.CameraLook, "career_2_contractor");

            // a fence job on an empty town lot, built with the landscaping system
            var fence = ContractorJobs.Create("fence", 4242);
            Company.Offers.Add(fence);
            var fenceLot = Town.Find(fence.Address);
            Assert.NotNull(fenceLot, "fence job on a town lot");
            Assert.IsTrue(Company.Accept(fence, out msg), msg);
            Assert.AreEqual(LotBroker.JobOwnerPrefix + fence.Id, fenceLot.Owner, "lot claimed for the job");
            var takeoff = ContractorJobs.Takeoff(fence);
            foreach (var kv in takeoff) Game.Stock.Add(kv.Key, kv.Value * Mathf.Max(1, Catalog.Get(kv.Key)?.UnitsPerPurchase ?? 1) + 12);
            var pts = ContractorJobs.FenceLine(fence);
            for (int i = 0; i < 3; i++)
            {
                var run = Landscape.LayOut(LandscapeKind.Fence, pts[i], pts[i + 1]);
                Assert.NotNull(run, "fence run " + i); _built.Add(run.gameObject); run.CompleteAll();
            }
            ContractorJobs.GateLine(fence, out var ga, out var gb);
            var gate = Landscape.LayOut(LandscapeKind.Gate, ga, gb);
            Assert.NotNull(gate); _built.Add(gate.gameObject); gate.CompleteAll();
            yield return Frames(3);
            Assert.IsTrue(ContractorJobs.TryComplete(fence, out msg), msg);
            Assert.AreEqual(2, Company.JobsCompleted, "contractor job done");
            StringAssert.StartsWith(LotBroker.ClientOwnerPrefix, fenceLot.Owner, "the client keeps the lot");
            Shot(fenceLot.FrontMid + fenceLot.Front * 6f + Vector3.up * 9f, fenceLot.Center, "career_2b_fence_on_town_lot");

            // ================================================================ 2. Crew: two hires
            Grant(60000, "more contractor work");
            if (Crew.Market.Count < 2) Crew.RefreshMarket(GameClock.I.Day);
            var w1 = Crew.HireCandidate(Crew.Market[0], out msg); Assert.NotNull(w1, msg);
            var w2 = Crew.HireCandidate(Crew.Market[0], out msg); Assert.NotNull(w2, msg);
            Assert.AreEqual(CompanyStage.Crew, Company.Stage);

            // a contractor job the pickup can do, at a town address; the crew shows up there and works it off site later
            var cj = Find(j => ContractorJobs.IsContractorJob(j) && Company.Blocker(j) == null);
            if (cj == null) { cj = ContractorJobs.Create("fence", 777); cj.RequiredVehicleType = "pickup"; Company.Offers.Add(cj); }
            Assert.IsTrue(Company.Accept(cj, out msg), msg);
            var cjLot = Town.Find(cj.Address);
            Assert.NotNull(cjLot, "crew job at a town address");
            Crew.Assign(w1, cj.Id); Crew.Assign(w2, cj.Id);

            GameClock.I.Hours = 5.5f;                       // before work: drive there first
            HandymanStart.MovePlayer(cjLot.Center, 0f);
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(cj.Id, Crew.HereJobId, "standing on the job's lot = that job is 'here'");
            Assert.IsTrue(CrewManager.ArrivalOverride.HasValue && Vector3.Distance(CrewManager.ArrivalOverride.Value, cjLot.Parking) < 4f, "crew truck pulls up at the lot");
            var task = Crew.AddTask(new StockTask { Ref = "2x4x8", Count = 1, A = cjLot.Center, B = cjLot.Center });
            Assert.AreEqual(cj.Id, task.JobId, "tasks added on a town job are tagged with that job");
            Crew.RemoveTask(task);
            GameClock.I.Hours = 9.5f;
            yield return new WaitForSeconds(1.2f);
            var npc = CrewManager.NpcFor(w1);
            Assert.NotNull(npc, "worker came to the town job");
            Assert.Less(Vector3.Distance(npc.transform.position, cjLot.Parking), 25f, "worker arrived at the job's lot");
            Shot(cjLot.Parking + cjLot.Front * 3f + Vector3.up * 4f - cjLot.Rotation * Vector3.right * 6f, npc.transform.position + Vector3.up, "career_3_crew_at_town_job");

            // go back to the yard: the job is now off site and the crew finishes it day by day
            HandymanStart.MovePlayer(YardSite.I.PlayerSpawn, 0f);
            yield return new WaitForSeconds(0.7f);
            Assert.AreNotEqual(cj.Id, Crew.HereJobId);
            int doneBefore = Company.JobsCompleted;
            for (int d = 0; d < 60 && !cj.Done; d++) yield return NextDay();
            Assert.IsTrue(cj.Done, $"crew finished {cj.Title} off site");
            Assert.AreEqual(doneBefore + 1, Company.JobsCompleted, "crew-stage job done");
            AssertOneJobPerLot("crew");

            // ================================================================ 3. Builder: yard + shop, rep 65
            Grant(900000, "a year of jobs");
            Company.Reputation = 66;
            Assert.IsTrue(Yard.Lease(2, out msg), msg);
            yield return Frames(3);
            Assert.AreEqual(CompanyStage.Builder, Company.Stage);
            Assert.AreEqual(yardLot, LotBroker.YardLot, "the yard + shop still fits the commercial lot");
            var houseOffer = Find(j => Builder.IsHouseJob(j) && Company.Blocker(j) == null);
            Assert.NotNull(houseOffer, "client house offers on the board: " + string.Join(", ", Company.Offers.ConvertAll(j => j.Title)));
            Assert.NotNull(Town.Find(houseOffer.Address), "client house on a town lot: " + houseOffer.Address);
            AssertOneJobPerLot("builder");
            Assert.IsTrue(Company.Accept(houseOffer, out msg), msg);
            var house = Projects.ForJob(houseOffer.Id);
            Assert.NotNull(house, "project created on accept");
            var houseLot = Town.Find(house.LotId);
            Assert.NotNull(houseLot); Assert.AreEqual(LotUse.BuildSite, houseLot.Use, "house lot claimed in the town");
            float subsOnly = new CrewHouseProgress().DailyProgress(house, BuildPhase.Framing, 1);
            Crew.Assign(w1, houseOffer.Id); Crew.Assign(w2, houseOffer.Id);
            Assert.Greater(Projects.Crew.DailyProgress(house, BuildPhase.Framing, 1), subsOnly * 1.2f, "the hired crew speeds the house up");
            Assert.AreEqual(1f / house.Plan.PhaseDays(BuildPhase.Framing), subsOnly, 1e-5f, "subs alone keep the plan schedule");
            doneBefore = Company.JobsCompleted;
            for (int d = 0; d < 400 && !houseOffer.Done; d++)
            {
                yield return NextDay();
                if (d == 25) { DevVisuals.RefreshNow(); yield return null; var dl = house.Lot; Shot(dl.FrontMid + dl.Front * 10f + Vector3.up * 9f, dl.Center + Vector3.up * 2f, "career_4_builder_house_going_up"); }
            }
            Assert.IsTrue(houseOffer.Done, "client house finished: " + house.StatusLabel);
            Assert.AreEqual(doneBefore + 1, Company.JobsCompleted, "builder job done");

            // ================================================================ 4. Developer: HQ, 8 workers, rep 80
            Grant(4000000, "years of houses");
            Company.Reputation = 85;
            Assert.IsTrue(Yard.Lease(3, out msg), msg);
            yield return Frames(3);
            var hqLot = LotBroker.YardLot;
            Assert.NotNull(hqLot, "HQ on a town lot");
            Assert.AreNotEqual(yardLot, hqLot, "HQ needs a bigger lot: the company moved");
            Assert.IsTrue(yardLot.IsFree, "old yard lot released");
            Assert.AreEqual(CompanyStage.Builder, Company.Stage, "still needs 8 workers");
            Crew.RefreshMarket(GameClock.I.Day);
            while (Company.Workers.Count < 8 && Crew.Market.Count > 0) Assert.NotNull(Crew.HireCandidate(Crew.Market[0], out msg), msg);
            Assert.AreEqual(8, Company.Workers.Count);
            Assert.AreEqual(CompanyStage.Developer, Company.Stage);
            var land = Find(j => LandOffers.TractOf(j) != null);
            Assert.NotNull(land, "land listings on the board");
            AssertOneJobPerLot("developer");
            Assert.IsTrue(Company.Accept(land, out msg), msg);
            var dev = Developments.All.Find(x => x.TractId == LandOffers.TractOf(land));
            Assert.NotNull(dev, "land bought on accept");
            Assert.Greater(Developments.Subdivide(dev, 70f, out msg), 0, msg);
            Assert.IsTrue(Developments.StartSiteWork(dev, out msg), msg);
            dev.SiteWorkStartDay = Projects.Day - dev.SiteWorkDays;   // grant: the street and utilities are in
            foreach (var l in dev.Lots) Assert.NotNull(Developments.StartSpecHouse(dev, l, HousePlans.Get("starter-900"), 0.35f, out msg), msg);
            var specs = new List<HouseProject>(dev.Houses);
            for (int i = 0; i < Company.Workers.Count; i++) Crew.Assign(Company.Workers[i], specs[i % specs.Count].Id);
            for (int d = 0; d < 12; d++) yield return NextDay();
            YardSite.I?.RefreshContents();
            DevVisuals.RefreshNow();
            yield return Frames(2);
            Shot(YardSite.I.CameraSpot, YardSite.I.CameraLook, "career_5a_developer_hq");
            Shot(dev.Tract.FrontMid + dev.Tract.Front * 25f + Vector3.up * 45f, dev.Tract.Center, "career_5b_developer_subdivision");

            // ================================================================ 5. save + load mid-career
            int money = Game.Wallet.Money, rep = Company.Reputation, jobs = Company.JobsCompleted, projects = Projects.All.Count;
            string hq = hqLot.Id, landId = land.Id, devId = dev.Id;
            Vector3 hqPos = Town.HQPosition;
            var progress = new List<float>(); foreach (var p in specs) progress.Add(p.Overall);
            Assert.IsTrue(SaveSystem.Save(Slot), SaveSystem.LastMessage);
            string json = File.ReadAllText(SaveSystem.PathFor(Slot));
            foreach (var key in new[] { "company", "town", "van", "repairs", "yardfleet", "crew", "development", "career" })
            {
                Assert.Contains(key, new List<string>(SaveSystem.ExtensionKeys), key + " registered");
                StringAssert.Contains("\"" + key + "\"", json, key + " in the save file");
            }
            Grant(12345, "noise to be undone by the load");
            Crew.FireWorker(Company.Workers[7]);
            Assert.IsTrue(SaveSystem.Load(Slot), SaveSystem.LastMessage);
            yield return Frames(4);
            Assert.AreEqual(CompanyStage.Developer, Company.Stage);
            Assert.AreEqual(money, Game.Wallet.Money, "money");
            Assert.AreEqual(rep, Company.Reputation, "reputation");
            Assert.AreEqual(jobs, Company.JobsCompleted);
            Assert.AreEqual(8, Company.Workers.Count, "workers");
            Assert.AreEqual(3, Company.WarehouseLevel);
            Assert.AreEqual(2, Company.Vehicles.Count, "van + pickup");
            Assert.AreEqual(hq, LotBroker.YardLot?.Id, "HQ lot claim restored");
            Assert.Less(Vector3.Distance(hqPos, Town.HQPosition), 0.01f);
            Assert.Less(Vector3.Distance(WorkVan.I.transform.position, hqPos), 120f, "the van moved to the HQ with the company");
            Assert.IsTrue(yardLot.IsFree);
            Assert.NotNull(WorkVan.I, "work van respawned");
            Assert.NotNull(YardSite.I, "HQ rebuilt");
            Assert.Less(Vector3.Distance(YardSite.I.transform.position, hqPos), 0.01f);
            Assert.AreEqual(projects, Projects.All.Count, "house projects");
            dev = Developments.Get(devId);
            Assert.NotNull(dev, "development restored");
            Assert.IsFalse(Town.Find(dev.TractId).IsFree, "bought tract still claimed");
            land = Company.ActiveJobs.Find(j => j.Id == landId);
            Assert.NotNull(land, "land job still active");
            specs = new List<HouseProject>(dev.Houses);
            for (int i = 0; i < specs.Count; i++) Assert.AreEqual(progress[i], specs[i].Overall, 1e-4f, "spec house progress");
            foreach (var w in Company.Workers) Assert.NotNull(Crew.Info(w));
            Assert.IsTrue(Company.Workers.Exists(w => specs.Exists(p => p.Id == w.AssignedJobId)), "crew assignments to spec houses restored");
            AssertOneJobPerLot("after load");
            Shot(dev.Tract.FrontMid + dev.Tract.Front * 25f + Vector3.up * 45f, dev.Tract.Center, "career_6_after_load");

            // finish the neighborhood: list each house when done, the market sells them
            for (int d = 0; d < 400 && !land.Done; d++)
            {
                yield return NextDay();
                int day = GameClock.I.Day;
                // the client house's one-year warranty call comes in meanwhile: send a carpenter
                foreach (var p in Projects.All) if (p.WarrantyJobId != null && !p.WarrantyDone) Builder.ResolveWarranty(p, out _);
                foreach (var h in dev.Houses) if (h.Complete && !h.Listed && !h.Sold) Developments.List(h, Mathf.RoundToInt(Developments.MarketValue(h, day) * 0.97f), out _);
            }
            Assert.IsTrue(dev.Complete, $"every spec house sold ({dev.SoldCount}/{dev.Lots.Count})");
            Assert.IsTrue(land.Done, "developer job completed when the neighborhood sold out");
            Assert.Contains(land.Id, Company.CompletedJobIds, "developer job counted");
            if (NeighborhoodComplete.I != null) { Object.Destroy(NeighborhoodComplete.I.gameObject); NeighborhoodComplete.I = null; Game.UIOpen = false; }
            DevVisuals.RefreshNow();
            yield return Frames(2);
            Shot(dev.Tract.FrontMid + dev.Tract.Front * 25f + Vector3.up * 50f, dev.Tract.Center, "career_7_neighborhood_sold");
        }
    }
}
