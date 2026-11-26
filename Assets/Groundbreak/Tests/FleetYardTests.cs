using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Contractor stage: vehicles, cargo limits, warehouse/yard, daily costs, loans, bankruptcy, save.
    /// Screenshots → Logs/shots/yf_*.png. Test area: x ≈ -230..-400, z ≈ -60..-300 (warehouse at TownHook.FallbackHQ).</summary>
    public class FleetYardTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Vector3 pos, Vector3 look, string name, float fov = 55f)
        {
            var cam = new GameObject("YardFleetTestCam").AddComponent<Camera>();
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

        int _money, _rep;

        [SetUp]
        public void Begin()
        {
            _money = Game.Wallet.Money; _rep = Career.Reputation;
            GameSettings.Mode = GameMode.Career; GameSettings.UnlimitedMoney = false;
            Company.ResetState(); YardFleetRuntime.ResetAll();
            Fleet.TripSeconds = 0f;
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            Weather.ForceRain = false;
        }

        [TearDown]
        public void End()
        {
            Company.ResetState(); YardFleetRuntime.ResetAll();
            Fleet.TripSeconds = 45f;
            GameSettings.Mode = GameMode.FreeBuild; Weather.ForceRain = null;
            Career.Reputation = _rep;
            int d = _money - Game.Wallet.Money;
            if (d > 0) Game.Wallet.Add(d, "test restore"); else if (d < 0) Game.Wallet.TrySpend(-d, "test restore");
            if (Game.Stock.Count("crusher-run") > 0) Game.Stock.TryTake("crusher-run", Game.Stock.Count("crusher-run"));
            SaveSystem.Delete("test_yardfleet");
        }

        static void Cash(int amount) { if (Game.Wallet.Money < amount) Game.Wallet.Add(amount - Game.Wallet.Money, "test cash"); }

        // A new game day: Company.Tick (CompanyClock) charges daily costs and fires DayStarted.
        static IEnumerator NextDay() { yield return null; GameClock.I.Day++; yield return Frames(2); }

        // ------------------------------------------------------------------ dealership

        [UnityTest]
        public IEnumerator BuyVehicles_CashAndFinanced()
        {
            yield return Frames(5);
            Cash(200000);
            int before = Game.Wallet.Money;
            var pickup = Fleet.Buy("pickup", true, 0, out var msg);
            Assert.NotNull(pickup, msg);
            Assert.IsTrue(Company.Owns("pickup"));
            Assert.AreEqual(before - FleetCatalog.Get("pickup").PriceUsed, Game.Wallet.Money);
            Assert.Less(pickup.Condition, 1f, "used trucks are worn");

            before = Game.Wallet.Money;
            var flat = Fleet.Buy("flatbed", false, 60, out msg);
            Assert.NotNull(flat, msg);
            Assert.AreEqual(before - Fleet.DownPayment("flatbed", false), Game.Wallet.Money);
            var loan = Finance.LoanOn(flat.Uid);
            Assert.NotNull(loan);
            Assert.AreEqual(FleetCatalog.Get("flatbed").PriceNew - Fleet.DownPayment("flatbed", false), loan.Principal);
            Assert.Greater(loan.Payment * 60, loan.Principal, "interest is charged");

            Assert.IsTrue(Fleet.AddRack(pickup, out msg), msg);
            Assert.IsTrue(Fleet.HasRack(pickup.Uid));
            Assert.IsTrue(Fleet.Sell(flat, out msg), msg);
            Assert.IsNull(Finance.LoanOn(flat.Uid), "selling pays off the auto loan");
            Assert.IsFalse(Company.Owns("flatbed"));
            Game.Wallet.TrySpend(Game.Wallet.Money - 1000, "test: almost broke");
            Assert.IsNull(Fleet.Buy("boom", false, 0, out msg), "can't buy a $185k crane with $1k");
        }

        // ------------------------------------------------------------------ cargo

        [Test]
        public void CargoLimits_LengthBulkWeightTrusses()
        {
            var longStock = new Dictionary<string, int> { { "rebar-4", 10 } };            // 20' sticks
            Assert.IsFalse(Cargo.Fits("pickup", false, longStock), "20' stock doesn't fit an 8' bed");
            Assert.IsTrue(Cargo.Fits("pickup", true, longStock), "...but rides on a lumber rack");
            Assert.IsTrue(Cargo.Fits("flatbed", false, longStock));

            var twelves = new Dictionary<string, int> { { "2x6x12", 20 } };
            Assert.IsTrue(Cargo.Fits("pickup", false, twelves), "12' stock with the tailgate down");

            var gravel = new Dictionary<string, int> { { "crusher-run", 3 } };
            Assert.IsFalse(Cargo.Fits("pickup", false, gravel));
            Assert.IsFalse(Cargo.Fits("flatbed", false, gravel));
            Assert.IsTrue(Cargo.Fits("dump", false, gravel));
            Assert.IsFalse(Cargo.Fits("dump", false, new Dictionary<string, int> { { "crusher-run", 6 } }), "6 yd of base is ~17,000 lb: over the dump's payload");

            var block = new Dictionary<string, int> { { "cmu-8x8x16", 90 } };              // ~3,000 lb
            var rep = Cargo.Check(FleetCatalog.Get("pickup"), false, block);
            Assert.IsFalse(rep.Fits, "90 block overloads a half-ton");
            StringAssert.Contains("Overweight", rep.Problems[0]);
            Assert.IsTrue(Cargo.Fits("flatbed", false, block));

            Assert.IsFalse(Cargo.Fits("box", false, new Dictionary<string, int> { { "osb716", 300 } }), "300 sheets is ~ 21,000 lb");
            Assert.AreEqual("dump", Cargo.SmallestVehicleFor(gravel));

            string truss = Catalog.Get("truss-fink") != null ? "truss-fink" : Catalog.Get("truss-gable") != null ? "truss-gable" : null;
            if (truss != null)
            {
                var t = new Dictionary<string, int> { { truss, 12 } };
                Assert.IsFalse(Cargo.Fits("flatbed", false, t), "trusses need the boom");
                Assert.IsTrue(Cargo.Fits("boom", false, t));
            }
            Assert.IsFalse(Cargo.Fits("skidsteer", false, twelves), "the skid steer carries no cargo");
        }

        // ------------------------------------------------------------------ warehouse, stage, daily costs

        [UnityTest]
        public IEnumerator Warehouse_MakesContractor_ChargesDailyCosts()
        {
            yield return Frames(5);
            Cash(80000);
            Company.AddVehicle("van", "Work van");
            Assert.IsTrue(Yard.Lease(1, out var msg), msg);
            Assert.IsTrue(Company.HasWarehouse); Assert.AreEqual(1, Company.WarehouseLevel);
            Assert.AreEqual(CompanyStage.Handyman, Company.Stage, "a van alone is still a handyman");
            Assert.NotNull(Fleet.Buy("pickup", true, 0, out msg), msg);
            Assert.AreEqual(CompanyStage.Contractor, Company.Stage);
            Assert.NotNull(YardSite.I, "premises built");

            // stock the warehouse at the bulk discount with our own truck
            var pickup = Company.Vehicles.Find(v => v.TypeId == "pickup");
            int before = Game.Wallet.Money;
            Assert.IsTrue(Fleet.Haul(pickup, new Dictionary<string, int> { { "2x4x8", 40 }, { "osb716", 20 } }, true, out msg), msg);
            Assert.AreEqual(40, Yard.Stock.Count("2x4x8"));
            int list = Catalog.Get("2x4x8").Price * 40 + Catalog.Get("osb716").Price * 20;
            Assert.Less(before - Game.Wallet.Money, list + 10, "bulk discount + fuel only, no delivery fee");
            Assert.IsFalse(Fleet.Haul(pickup, new Dictionary<string, int> { { "crusher-run", 2 } }, false, out msg), "pickup can't haul gravel");

            // take some to the job site
            int site = Game.Stock.Count("2x4x8");
            Assert.IsTrue(Fleet.HaulFromYard(pickup, new Dictionary<string, int> { { "2x4x8", 10 } }, out msg), msg);
            Assert.AreEqual(30, Yard.Stock.Count("2x4x8"));
            Assert.AreEqual(site + 10, Game.Stock.Count("2x4x8"));
            Game.Stock.TryTake("2x4x8", 10);

            int daily = 0; var labels = new List<string>();
            foreach (var (l, a) in Company.DailyCostBreakdown()) { daily += a; labels.Add(l); }
            CollectionAssert.Contains(labels, "Warehouse rent");
            CollectionAssert.Contains(labels, "Vehicle insurance");
            before = Game.Wallet.Money;
            yield return NextDay();
            Assert.AreEqual(before - daily, Game.Wallet.Money, "daily costs charged once on the new day");
            var pnl = Finance.Totals(GameClock.I.Day, GameClock.I.Day);
            Assert.IsTrue(pnl.ContainsKey("Warehouse") && pnl["Warehouse"] <= -Yard.Levels[1].RentPerDay, "rent shows in the P&L");
            Assert.AreEqual(0, Finance.InsolventDays);

            // screenshots: bay with fleet + stock, then the yard and HQ
            Fleet.Buy("dump", true, 0, out _);
            Yard.BuyEquipment("sawstation", out _); Yard.BuyEquipment("compressor", out _);
            Yard.OrderDelivered(new Dictionary<string, int> { { "2x6x12", 30 }, { "ply34", 15 }, { "door-ext-steel36", 2 }, { "nails16d", 4 } }, out _);
            yield return Frames(3);
            var s = YardSite.I;
            Assert.AreEqual(3, s.ParkedVehicles.Count, "van, pickup and dump truck parked");
            Shot(s.CameraSpot, s.CameraLook, "yf_10_bay_exterior");
            Shot(s.InteriorCameraSpot, s.InteriorCameraLook, "yf_11_bay_interior", 70f);

            Cash(200000);
            Assert.IsTrue(Yard.Lease(2, out msg), msg);
            Yard.BuyEquipment("fueltank", out _); Yard.BuyEquipment("lift", out _); Yard.BuyEquipment("forklift", out _);
            Fleet.Buy("flatbed", true, 0, out _); Fleet.Buy("boom", true, 0, out _); Fleet.Buy("skidsteer", true, 0, out _); Fleet.Buy("box", true, 0, out _);
            Yard.OrderDelivered(new Dictionary<string, int> { { "gravel-57", 8 }, { "rebar-4", 30 } }, out _);
            yield return Frames(3);
            s = YardSite.I;
            Assert.AreEqual(2, s.Level);
            Shot(s.CameraSpot, s.CameraLook, "yf_12_yard_exterior");
            Shot(s.InteriorCameraSpot, s.InteriorCameraLook, "yf_13_shop_interior", 70f);

            Cash(100000);
            Assert.IsTrue(Yard.Lease(3, out msg), msg);
            yield return Frames(3);
            s = YardSite.I;
            Assert.AreEqual(3, s.Level);
            Shot(s.CameraSpot, s.CameraLook, "yf_14_hq_exterior");
            Shot(s.InteriorCameraSpot, s.InteriorCameraLook, "yf_15_hq_interior", 70f);
        }

        // ------------------------------------------------------------------ loans + bankruptcy

        [UnityTest]
        public IEnumerator BankLoan_PaymentsThenBankruptcy()
        {
            yield return Frames(5);
            Cash(40000);
            Company.AddVehicle("van", "Work van");
            Assert.IsTrue(Yard.Lease(1, out var msg), msg);
            Assert.NotNull(Fleet.Buy("pickup", true, 84, out msg), msg);
            Assert.AreEqual(CompanyStage.Contractor, Company.Stage);
            int limit = Finance.AvailableCredit;
            Assert.Greater(limit, 5000);
            Assert.IsFalse(Finance.TakeBankLoan(limit + 1000, 60, out msg), "over the credit line");

            int before = Game.Wallet.Money;
            Assert.IsTrue(Finance.TakeBankLoan(5000, 60, out msg), msg);
            Assert.AreEqual(before + 5000, Game.Wallet.Money);
            var bank = Finance.Loans.Find(l => l.Kind == "bank");
            Assert.AreEqual(Finance.PaymentFor(5000, Finance.BankApr, 60), bank.Payment);

            yield return NextDay();
            Assert.AreEqual(1, bank.DaysPaid);
            Assert.Less(bank.Balance, 5000f, "principal goes down");
            Assert.Greater(bank.Balance, 5000f - bank.Payment, "part of the payment was interest");
            Assert.AreEqual(0, Finance.InsolventDays);

            // run out of money: three days of unpaid bills
            Game.Wallet.TrySpend(Game.Wallet.Money, "test: drain");
            int rep = Company.Reputation;
            yield return NextDay();
            Assert.AreEqual(1, Finance.InsolventDays);
            Assert.AreEqual(1, bank.Missed);
            yield return NextDay();
            yield return NextDay();
            Assert.AreEqual(1, Finance.Bankruptcies);
            Assert.IsFalse(Company.Owns("pickup"), "financed truck repossessed");
            Assert.IsTrue(Company.Owns("van"), "keeps one van to start over");
            Assert.IsFalse(Company.HasWarehouse, "lease terminated");
            Assert.AreEqual(0, Finance.Loans.Count, "debt settled or discharged");
            Assert.LessOrEqual(Company.Reputation, rep - Finance.BankruptcyRepHit);
            Assert.AreEqual(CompanyStage.Handyman, Company.Stage);
            StringAssert.Contains("BANKRUPTCY", Finance.LastBankruptcyReport);
        }

        // ------------------------------------------------------------------ save / load

        [UnityTest]
        public IEnumerator SaveRoundTrip()
        {
            yield return Frames(5);
            Cash(90000);
            Assert.IsTrue(Yard.Lease(1, out var msg), msg);
            var truck = Fleet.Buy("flatbed", true, 60, out msg); Assert.NotNull(truck, msg);
            Fleet.AddRack(Fleet.Buy("pickup", true, 0, out _), out _);
            Yard.BuyEquipment("compressor", out _);
            Yard.Stock.Add("2x4x8", 25);
            Finance.TakeBankLoan(3000, 30, out _);
            int loans = Finance.Loans.Count;
            float bal = Finance.LoanOn(truck.Uid).Balance;
            yield return null;
            Assert.IsTrue(SaveSystem.Save("test_yardfleet"), SaveSystem.LastMessage);

            Company.ResetState(); YardFleetRuntime.ResetAll();
            Assert.AreEqual(0, Yard.Stock.Count("2x4x8"));
            Assert.IsTrue(SaveSystem.Load("test_yardfleet"), SaveSystem.LastMessage);
            yield return Frames(3);
            Assert.AreEqual(1, Company.WarehouseLevel);
            Assert.AreEqual(CompanyStage.Contractor, Company.Stage);
            Assert.AreEqual(25, Yard.Stock.Count("2x4x8"));
            Assert.IsTrue(Yard.HasEquipment("compressor"));
            Assert.AreEqual(loans, Finance.Loans.Count);
            Assert.AreEqual(bal, Finance.LoanOn(truck.Uid).Balance, 0.01f);
            var pickup = Company.Vehicles.Find(v => v.TypeId == "pickup");
            Assert.NotNull(pickup); Assert.IsTrue(Fleet.HasRack(pickup.Uid));
            Assert.NotNull(YardSite.I, "premises rebuilt after load");
        }

        // ------------------------------------------------------------------ models

        [UnityTest]
        public IEnumerator VehicleModels_Screenshots()
        {
            yield return Frames(5);
            var root = new GameObject("VehicleLineup");
            var at = new Vector3(-330f, 0f, -60f);
            int i = 0;
            foreach (var t in FleetCatalog.All)
            {
                bool rack = t.RackMaxItemLength > 0;
                var go = VehicleModels.Build(t.Id, root.transform, rack);
                Assert.NotNull(go, t.Id);
                var p = at + new Vector3(i * 40f, 0, 0);
                go.transform.position = p;
                var size = VehicleModels.SizeOf(t.Id);
                Assert.Greater(size.z, 3f, t.Id);
                var b = new Bounds(p, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                Assert.AreEqual(0f, b.min.y, 0.05f, t.Id + " sits on the ground");
                Assert.AreEqual(size.z, b.size.z, 0.6f, t.Id + " length matches SizeOf");
                i++;
            }
            yield return Frames(2);
            i = 0;
            foreach (var t in FleetCatalog.All)
            {
                var p = at + new Vector3(i * 40f, 0, 0);
                var s = VehicleModels.SizeOf(t.Id);
                float dist = s.z * 0.95f + 3f;
                Shot(p + new Vector3(dist * 0.75f, s.y * 0.7f + 0.8f, dist * 0.8f), p + new Vector3(0, s.y * 0.4f, 0), $"yf_0{i}_{t.Id}");
                i++;
            }
            Object.Destroy(root);
        }
    }
}
