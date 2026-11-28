using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Business-side rules: yard saw station, weather seed per career, end lease / downgrade vs stage, loan shortfall at a
    /// sale, job-app supplier delivery fee, warehouse space with loads on the road, spec-house site-work share, crew labor credit
    /// order, weekly equipment rental cap, pro-rated sub invoices, single premises rebuild. Shot → Logs/shots/br_*.png.
    /// Uses the warehouse at TownHook.FallbackHQ (like FleetYardTests) and the fallback dev lots.</summary>
    public class BusinessRulesTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator NextDay() { yield return null; GameClock.I.Day++; yield return Frames(2); }

        static void Shot(Vector3 pos, Vector3 look, string name, float fov = 55f)
        {
            var cam = new GameObject("BusinessRulesTestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = fov;
            cam.farClipPlane = 800f;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt); Object.Destroy(cam.gameObject);
        }

        Wallet _wallet; int _money, _rep, _day; GameMode _mode;

        [SetUp]
        public void Begin()
        {
            _wallet = Game.Wallet; _money = Game.Wallet.Money; _rep = Career.Reputation; _mode = GameSettings.Mode;
            _day = GameClock.I != null ? GameClock.I.Day : 1;
            GameSettings.Mode = GameMode.Career; GameSettings.UnlimitedMoney = false;
            Company.ResetState(); YardFleetRuntime.ResetAll(); Crew.ResetState(); Subs.ResetState();
            Fleet.TripSeconds = 0f;
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            Weather.ForceRain = false;
            DevLots.Provider = new FallbackLotProvider();
        }

        [TearDown]
        public void End()
        {
            Projects.LeaveSite(); Projects.ResetState(); Developments.ResetState(); DevLots.Provider = null; DevVisuals.RefreshNow();
            ContractorJobSite.ClearAll();
            Company.ResetState(); YardFleetRuntime.ResetAll(); Crew.ResetState(); Subs.ResetState(); Equipment.Rentals.Clear();
            Finance.ResetState();
            Fleet.TripSeconds = 45f; Subs.IgnoreWorkHours = false;
            Climate.Seed = 1; Climate.ClearCache();
            if (DeliveryService.I != null) typeof(DeliveryService).GetMethod("CancelPending", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(DeliveryService.I, null);
            Game.Wallet = _wallet;
            int d = _money - Game.Wallet.Money;
            if (d > 0) Game.Wallet.Add(d, "test restore"); else if (d < 0) Game.Wallet.TrySpend(-d, "test restore");
            Career.Reputation = _rep; GameSettings.Mode = _mode; Weather.ForceRain = null;
            if (GameClock.I != null) GameClock.I.Day = _day;
        }

        static void Cash(int amount) { if (Game.Wallet.Money < amount) Game.Wallet.Add(amount - Game.Wallet.Money, "test cash"); }

        static void Contractor()
        {
            Cash(80000);
            Company.AddVehicle("van", "Work van");
            Assert.IsTrue(Yard.Lease(1, out var msg), msg);
            Assert.NotNull(Fleet.Buy("pickup", true, 0, out msg), msg);
            Assert.AreEqual(CompanyStage.Contractor, Company.Stage);
        }

        static Job ActiveContractorJob(string type, int seed)
        {
            var j = ContractorJobs.Create(type, seed);
            j.Accepted = true; Company.ActiveJobs.Add(j);
            return j;
        }

        // ------------------------------------------------------------------ 1. saw station

        [UnityTest]
        public IEnumerator SawStation_FasterCrewCarpentry_LessWaste_Shot()
        {
            yield return Frames(3);
            Contractor();
            var deck = ActiveContractorJob("deck", 9101);
            var patio = new Job { Id = ContractorJobs.IdPrefix + "patio-test", Kind = JobKind.Remodel, Payload = JsonUtility.ToJson(new ContractorJobSpec { type = "patio" }) };
            var carp = Company.Hire(new Worker { Name = "BR Carpenter", Trade = "carpenter", Skill = 3, DailyWage = 240, Morale = 0.75f, AssignedJobId = deck.Id });
            var subHand = new Worker { Uid = "sub-test-0", Name = "Sub hand", Trade = "carpenter", Skill = 3, Morale = 0.8f };

            Assert.AreEqual(1f, SawStationRules.JobSpeed(deck)); Assert.AreEqual(1f, SawStationRules.MaterialFactor(deck));
            float before = Crew.Speed(carp, "carpenter"), concrete = Crew.Speed(carp, "concrete");
            Assert.IsTrue(Yard.BuyEquipment(SawStationRules.Id, out var msg), msg);
            StringAssert.Contains("10% faster", Yard.EquipmentOf(SawStationRules.Id).Description, "the effect is shown where it is sold");
            StringAssert.Contains("4% less lumber", Yard.EquipmentOf(SawStationRules.Id).Description);
            Assert.AreEqual(before * 1.10f, Crew.Speed(carp, "carpenter"), 0.001f, "house framing/trim (carpenter) work 10% faster");
            Assert.AreEqual(concrete, Crew.Speed(carp, "concrete"), 0.0001f, "other trades unchanged");
            Assert.AreEqual(1f, SawStationRules.CrewSpeed(subHand, "carpenter"), "subs bring their own saws");
            Assert.AreEqual(1.10f, SawStationRules.JobSpeed(deck), 1e-4f, "deck: wood job, pre-cut");
            Assert.AreEqual(0.96f, SawStationRules.MaterialFactor(deck), 1e-4f, "4% less lumber on a crew-built deck");
            Assert.AreEqual(1f, SawStationRules.JobSpeed(patio), "a concrete patio gets nothing");
            Assert.AreEqual(1f, SawStationRules.MaterialFactor(patio));

            // a crew day on the deck: materials are bought at 96% of the budget, pro rata with progress
            yield return NextDay();
            var sim = Crew.SimFor(deck.Id);
            Assert.NotNull(sim, "the crew worked the deck off site");
            Assert.Greater(sim.Progress, 0f);
            Assert.AreEqual(Mathf.RoundToInt(deck.MaterialBudget * 0.96f * Mathf.Clamp01(sim.Progress)), sim.MaterialsCharged, 1, "material bill with the shop's cut lists");

            yield return Frames(3);
            var s = YardSite.I;
            Assert.NotNull(s);
            Shot(s.InteriorCameraSpot, s.InteriorCameraLook, "br_saw_station_bay", 70f);
        }

        // ------------------------------------------------------------------ 2. weather seed

        [Test]
        public void WeatherSeed_RandomPerCareer_SavedWithTheGame()
        {
            Assert.IsFalse(SeasonWeather.RandomizeSeedOnNewGame, "batch mode (tests) keeps the default seed 1");
            var save = typeof(SeasonWeather).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Static);
            var load = typeof(SeasonWeather).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static);
            Climate.Seed = 1;
            int a = SeasonWeather.NewCareerSeed();
            Assert.AreNotEqual(1, a, "a new career rolls its own weather");
            Assert.AreEqual(a, Climate.Seed);
            string json = (string)save.Invoke(null, null);
            int b = SeasonWeather.NewCareerSeed();
            Assert.AreNotEqual(a, b, "two careers, two weathers");
            load.Invoke(null, new object[] { json });
            Assert.AreEqual(a, Climate.Seed, "a loaded career keeps its weather");
            load.Invoke(null, new object[] { "" });
            Assert.AreEqual(1, Climate.Seed, "no saved section: default seed 1");
        }

        // ------------------------------------------------------------------ 3 + 7 + 12. lease vs stage, downgrade, single rebuild

        [UnityTest]
        public IEnumerator EndLease_BlockedByStage_DowngradeSellsEquipment_RebuildsOnce()
        {
            yield return Frames(3);
            Contractor();
            var site = YardSite.I;
            yield return Frames(3);
            Assert.AreSame(site, YardSite.I, "the lease built the premises once; the runtime didn't rebuild them again");

            Assert.IsFalse(Yard.TryEndLease(out int cash, out var msg), "a Contractor can't end the lease and keep the stage");
            StringAssert.Contains("Contractor", msg);
            Assert.IsTrue(Company.HasWarehouse); Assert.AreEqual(0, cash);

            Cash(100000);
            Assert.IsTrue(Yard.Lease(2, out msg), msg);
            Assert.IsTrue(Yard.BuyEquipment("lift", out msg), msg);
            Assert.IsTrue(Yard.BuyEquipment("sawstation", out msg), msg);
            int money = Game.Wallet.Money;
            Assert.IsTrue(Yard.Lease(1, out msg), "a Contractor may move back down to a bay: " + msg);
            Assert.IsFalse(Yard.HasEquipment("lift"), "the vehicle lift can't go in a bay: sold");
            Assert.IsTrue(Yard.HasEquipment("sawstation"), "the saw station fits a bay");
            Assert.AreEqual(money - Yard.Levels[1].MoveIn + 9000 * 4 / 10, Game.Wallet.Money, "lift sold back at 40%");
            StringAssert.Contains("Two-post vehicle lift", msg);

            Assert.IsTrue(Yard.Lease(2, out msg), msg);
            Company.Stage = CompanyStage.Builder;
            Assert.IsFalse(Yard.Lease(1, out msg), "a Builder needs the yard + shop");
            StringAssert.Contains("Builder", msg);
            Assert.AreEqual(2, Company.WarehouseLevel);

            GameSettings.Mode = GameMode.FreeBuild;
            Assert.IsNull(Yard.DowngradeBlocker(0), "Free Build has no stage rules");
            GameSettings.Mode = GameMode.Career;
            var report = new List<string>();
            Assert.Greater(Yard.Liquidate(report), 0, "a bankruptcy still gives the premises up");
            Assert.IsFalse(Company.HasWarehouse);
        }

        // ------------------------------------------------------------------ 4. loan shortfall at a sale

        [Test]
        public void SpecSale_LoanShortfall_StaysOwed()
        {
            DevLot lot = null; foreach (var l in DevLots.Provider.FreeLots()) { lot = l; break; }
            Assert.NotNull(lot);
            var p = Projects.Create(ProjectKind.Spec, lot, HousePlans.Get("starter-900"));
            p.Client = "Spec";
            Game.Wallet = new Wallet(5000);
            p.LoanBalance = 300000;
            int price = 200000;
            int net = price - Mathf.RoundToInt(price * Developments.CommissionPct) - Mathf.RoundToInt(price * Developments.ClosingPct);
            Developments.Close(p, price, 10);
            Assert.AreEqual(0, Game.Wallet.Money, "every dollar on hand went to the lender");
            var note = Finance.Loans.Find(l => l.Label.StartsWith("Loan shortfall"));
            Assert.NotNull(note, "the unpaid rest is a company debt, not forgiven");
            Assert.AreEqual(300000 - (5000 + net), note.Principal);
            Assert.AreEqual(note.Principal, Finance.TotalDebt, 1);
            Assert.Greater(note.Payment, 0);
            Assert.IsNull(Finance.AddShortfallNote("nothing", 0), "no note when the loan was covered");
        }

        // ------------------------------------------------------------------ 5. job app order: supplier delivery fee, filed as materials

        [UnityTest]
        public IEnumerator JobApp_OrderMissing_ChargesDeliveryFee_AsMaterials()
        {
            yield return Frames(3);
            if (DeliveryService.I == null) Assert.Ignore("no delivery service in this scene");
            Contractor();
            var deck = ActiveContractorJob("deck", 9102);
            var takeoff = ContractorJobs.Takeoff(deck);
            foreach (var kv in takeoff) if (Game.Stock.Count(kv.Key) > 0) Game.Stock.TryTake(kv.Key, Game.Stock.Count(kv.Key));
            int materials = ContractorJobs.CatalogCost(takeoff);
            Cash(materials + 10000);
            int before = Game.Wallet.Money;
            Assert.IsTrue(ContractorJobsApp.OrderMissing(takeoff, out var msg), msg);
            Assert.AreEqual(before - materials - Fleet.DeliveryFee, Game.Wallet.Money, "materials + the store's $79 delivery");
            StringAssert.Contains("$79 delivery", msg);
            Assert.AreEqual("Materials", Finance.Categorize(ContractorJobsApp.OrderReason, false), "filed under materials in the P&L");
            Assert.IsTrue(Game.Wallet.Log.Exists(l => l.Contains(ContractorJobsApp.OrderReason)));
        }

        // ------------------------------------------------------------------ 6. warehouse space counts loads on the road

        [UnityTest]
        public IEnumerator WarehouseCapacity_CountsInboundLoads()
        {
            yield return Frames(3);
            Contractor();
            var second = Fleet.Buy("pickup", true, 0, out var msg); Assert.NotNull(second, msg);
            var first = Company.Vehicles.Find(v => v.TypeId == "pickup" && v != second);
            Fleet.TripSeconds = 45f;                                           // runs stay on the road during the test
            var load = new Dictionary<string, int> { { "2x6x12", 20 } };
            float v = Cargo.UnitVolumeCuFt("2x6x12") * 20;
            int guard = 0;
            while (Yard.FreeCuFt - v >= v && guard++ < 10000) Yard.Stock.Add("2x6x12", Cargo.UnitsPerPurchase("2x6x12"));
            Assert.That(Yard.FreeCuFt, Is.InRange(v, 2f * v), "room for exactly one more load");
            Cash(20000);
            Assert.IsTrue(Fleet.Haul(first, new Dictionary<string, int>(load), true, out msg), msg);
            Assert.AreEqual(v, Fleet.InboundCuFt, 0.01f, "the first load is on the road");
            Assert.IsFalse(Fleet.Haul(second, new Dictionary<string, int>(load), true, out msg), "two trucks can't overfill the warehouse");
            StringAssert.Contains("cu ft free", msg);
            Assert.IsFalse(Yard.OrderDelivered(new Dictionary<string, int>(load), out msg), "nor can a supplier drop");
        }

        // ------------------------------------------------------------------ 8. spec house started before site work

        [Test]
        public void SpecHouseBeforeSiteWork_GetsSiteWorkShare()
        {
            Company.Stage = CompanyStage.Developer; Company.Reputation = 85;
            Game.Wallet = new Wallet(6_000_000);
            var tract = DevLots.Provider.Find("tract-a");
            Assert.IsTrue(Developments.BuyLand(tract, out var d, out var m), m);
            Assert.Greater(Developments.Subdivide(d, 70f, out m), 0, m);
            var p = Developments.StartSpecHouse(d, d.Lots[0], HousePlans.Get("starter-900"), 0.6f, out m);
            Assert.NotNull(p, m);
            int cost0 = p.CostSpent;
            Assert.IsTrue(Developments.StartSiteWork(d, out m), m);
            Assert.AreEqual(cost0 + d.SiteWorkCost / d.Lots.Count, p.CostSpent, "the house carries its share of the street and utilities");
            Assert.AreEqual(Developments.LandShare(d) + Developments.PermitAndFees(p.Plan), p.CostSpent, 2, "same as a house started after the site work");
        }

        // ------------------------------------------------------------------ 9. crew labor credit order

        [Test]
        public void CrewLaborCredit_EveryDayOfThePhase()
        {
            Assert.IsTrue(Projects.Crew is CrewHouseProgress, "integration sets the crew house progress");
            DevLot lot = null; foreach (var l in DevLots.Provider.FreeLots()) { lot = l; break; }
            var p = Projects.Create(ProjectKind.Spec, lot, HousePlans.Get("starter-900"));
            p.Client = "Spec";
            Game.Wallet = new Wallet(1_000_000);
            Company.Hire(new Worker { Name = "BR Framer", Trade = "carpenter", Skill = 3, DailyWage = 240, Morale = 0.8f, AssignedJobId = p.Id });
            Company.Hire(new Worker { Name = "BR Finisher", Trade = "concrete", Skill = 3, DailyWage = 230, Morale = 0.8f, AssignedJobId = p.Id });
            int logStart = Game.Wallet.Log.Count, days = 0, day = 2;
            while (p.CurrentPhase == BuildPhase.Foundation && days < 200) { Projects.DayStep(day++); days++; }
            Assert.Less(days, 200);
            int credits = 0;
            for (int i = logStart; i < Game.Wallet.Log.Count; i++) if (Game.Wallet.Log[i].Contains(CrewLaborCredit.Label)) credits++;
            Assert.AreEqual(days, credits, "credited on every day of the phase, the first and the last included");
        }

        // ------------------------------------------------------------------ 10. weekly rental rate

        [Test]
        public void EquipmentRental_WeeklyRateCapsTheWeek()
        {
            var ex = Equipment.Get("miniex");
            Assert.AreEqual(ex.RentPerDay, Equipment.RentForDay(ex, 1));
            Assert.AreEqual(Mathf.Min(3 * ex.RentPerDay, ex.RentPerWeek), Equipment.RentFor(ex, 3));
            Assert.AreEqual(ex.RentPerWeek, Equipment.RentFor(ex, 7), "a week costs the weekly rate, not 7 dailies");
            Assert.Less(Equipment.RentFor(ex, 7), 7 * ex.RentPerDay);
            Assert.AreEqual(ex.RentPerWeek + ex.RentPerDay, Equipment.RentFor(ex, 8), "the second week starts over");
            Assert.AreEqual(0, Equipment.RentForDay(ex, 6));
            Equipment.Rentals["miniex"] = 3;                                    // 3 days billed: tomorrow is day 4
            int billed = 0; foreach (var (lbl, amount) in Company.DailyCostBreakdown()) if (lbl == "Equipment rental") billed = amount;
            Assert.AreEqual(Equipment.RentForDay(ex, 4), billed, "the morning charge follows the weekly cap");
            Equipment.Rentals.Clear();
        }

        // ------------------------------------------------------------------ 11. pro-rated sub invoice

        [UnityTest]
        public IEnumerator SubInvoice_ProRatedToTheirWork()
        {
            yield return Frames(2);
            var co = SubCatalog.For("framing").GetEnumerator(); co.MoveNext();
            var c = co.Current; Assert.NotNull(c);
            var k = new SubContract { Id = "brsub1", CompanyId = c.Id, Trade = "framing", Qty = 100f, Price = 8000, QtyAtStart = 50f, WorkUnits = 12 };
            int min = Mathf.RoundToInt(SubCatalog.Trade("framing").Minimum * c.PriceFactor / 5f) * 5;
            Assert.AreEqual(Mathf.Max(min, 4000), Subs.ProRatedPrice(k), "half the quoted work was left: half the price");
            k.QtyAtStart = 100f; Assert.AreEqual(8000, Subs.ProRatedPrice(k), "all of it left: the quote");
            k.QtyAtStart = -1f; Assert.AreEqual(8000, Subs.ProRatedPrice(k), "old save, never measured: the quote");
            k.WorkUnits = 0; Assert.AreEqual(0, Subs.ProRatedPrice(k), "their hands did nothing: no invoice");

            // booked, but the scope is already done when they arrive: they leave without billing
            Subs.IgnoreWorkHours = true;
            int day = GameClock.I != null ? GameClock.I.Day : 1;
            var empty = new SubContract
            {
                Id = "brsub2", CompanyId = c.Id, Trade = "framing", JobId = "site", Center = new Vector3(-4800f, 0f, -4800f), Arrival = new Vector3(-4790f, 0f, -4800f),
                Radius = 4f, Qty = 40f, Price = 3000, BookedDay = day, PromisedDay = day, StartDay = day, Skill = 3, CrewSize = 2, State = SubState.Scheduled,
            };
            Subs.Contracts.Add(empty);
            int money = Game.Wallet.Money;
            Subs.Tick();
            Assert.AreEqual(SubState.Done, empty.State);
            Assert.AreEqual(0, empty.Price); Assert.IsTrue(empty.Paid);
            Assert.AreEqual(money, Game.Wallet.Money, "nothing billed");
            StringAssert.Contains("no charge", empty.Status);
        }
    }
}
