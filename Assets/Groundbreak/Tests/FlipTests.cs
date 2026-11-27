using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>House flipping: the market, buying with a hard-money loan, holding costs, the punch list played through catalog
    /// defects (player steps and crew days), upgrades, listing, offers, sale and profit, save round trip, and the street look.
    /// Uses a Birch St map ranch (z ≈ 250, x &lt; -100), away from the core town and the build pad other tests use.</summary>
    public class FlipTests
    {
        const string Slot = "test_flips";
        int _money, _rep, _day; float _hours; GameMode _mode; bool _relaxed, _unlimited;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(2);
            _money = Game.Wallet.Money; _rep = Career.Reputation; _mode = GameSettings.Mode; _relaxed = GameSettings.Relaxed; _unlimited = GameSettings.UnlimitedMoney;
            if (GameClock.I != null) { _day = GameClock.I.Day; _hours = GameClock.I.Hours; GameClock.I.Hours = 11f; }
            GameSettings.Mode = GameMode.Career; GameSettings.Relaxed = false; GameSettings.UnlimitedMoney = false;
            Company.ResetState();
            RepairJobs.ResetState();
            Flips.ResetState();
            HandymanStart.Begin();
            Company.Stage = CompanyStage.Contractor;
            Career.Reputation = 60;
            Game.Wallet = new Wallet(60000);
            yield return Frames(2);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Flips.ResetState();
            RepairJobs.ResetState();
            WorkVan.Despawn();
            VanCargo.Clear();
            Town.Destroy();
            Company.ResetState();
            Crew.ResetState();
            Game.Wallet = new Wallet(_money);
            Career.Reputation = _rep; GameSettings.Mode = _mode; GameSettings.Relaxed = _relaxed; GameSettings.UnlimitedMoney = _unlimited;
            if (GameClock.I != null) { GameClock.I.Day = _day; GameClock.I.Hours = _hours; }
            if (SaveSystem.Exists(Slot)) SaveSystem.Delete(Slot);
            yield return Frames(2);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A map ranch on Birch St (school district), the one furthest west.</summary>
        static TownLot TestLot()
        {
            TownLot best = null;
            foreach (var l in Town.Addresses)
                if (l.ServiceCalls && l.External && l.House != null && (best == null || l.Center.x < best.Center.x)) best = l;
            Assert.NotNull(best, "a map ranch with a house");
            return best;
        }

        static FlipHouse ListAndBuy(bool financed = true)
        {
            var lot = TestLot();
            var f = Flips.AddListing(lot, 12345);
            Assert.IsNull(Flips.BuyBlocker(f, financed), Flips.BuyBlocker(f, financed));
            Assert.IsTrue(Flips.Buy(f, financed, out var msg), msg);
            return f;
        }

        static void StreetShot(TownLot lot, string name, float side = 7f)
        {
            var sideV = Vector3.Cross(Vector3.up, lot.Front).normalized;
            var cam = lot.FrontMid + lot.Front * 9f + sideV * side + Vector3.up * 2.6f;
            SiteLookTests.Shot(cam, lot.Center + lot.Front * 4f + Vector3.up * 1.6f, name, 62f);
        }

        static void KitchenShot(TownLot lot, string name)
        {
            var a = lot.House.Anchor(HouseSpot.KitchenSink);
            var fwd = a.rotation * Vector3.forward; fwd.y = 0f; fwd.Normalize();
            var light = new GameObject("FlipTestLight").AddComponent<Light>();
            light.type = LightType.Point; light.range = 7f; light.intensity = 1.4f; light.transform.position = a.position + fwd * 1.5f + Vector3.up * 1.6f;
            SiteLookTests.Shot(a.position + fwd * 2.6f + Vector3.up * 1.55f, a.position + Vector3.up * 0.7f, name, 70f);
            Object.DestroyImmediate(light.gameObject);
        }

        /// <summary>From the bath door side, looking at the tub's back and end walls.</summary>
        static void BathShot(TownLot lot, string name)
        {
            var h = lot.House;
            var a = h.transform.InverseTransformPoint(h.Anchor(HouseSpot.TubCaulk).position);   // (tub end wall x, rim, zb + 30")
            var cam = h.transform.TransformPoint(new Vector3(HouseBuilder.P + 0.45f, a.y + 1.15f, a.z + 1.5f));
            var look = h.transform.TransformPoint(new Vector3(a.x - 0.35f, a.y + 0.55f, a.z - 0.45f));
            var light = new GameObject("FlipTestLight").AddComponent<Light>();
            light.type = LightType.Point; light.range = 6f; light.intensity = 1.4f; light.transform.position = h.transform.TransformPoint(new Vector3(a.x - 1.2f, a.y + 1.6f, a.z + 0.6f));
            SiteLookTests.Shot(cam, look, name, 70f);
            Object.DestroyImmediate(light.gameObject);
        }

        static Transform Interior(FlipHouse f, string name)
        {
            var root = FlipVisuals.RootOf(f);
            return root != null ? root.transform.Find(name) : null;
        }

        static void PlayAll(FlipHouse f, bool upgrades)
        {
            foreach (var it in f.items)
            {
                if (it.upgrade && !upgrades) continue;
                foreach (var s in it.defects)
                {
                    var d = Flips.LiveDefect(f, s);
                    Assert.NotNull(d, $"{s.def} is in the house");
                    JobCatalog.Solve(d);
                    Assert.IsTrue(d.IsFixed, $"{s.def} fixed by its own steps");
                }
            }
            Flips.SyncAll();
        }

        // ------------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator Market_ListsDistressedHousesWithRealNumbers()
        {
            Flips.RefreshMarket(Flips.Today);
            var list = new List<FlipHouse>(Flips.Listings);
            Assert.GreaterOrEqual(list.Count, 3, "a handful of houses on the market");
            foreach (var f in list)
            {
                var lot = f.Lot;
                Assert.NotNull(lot); Assert.IsTrue(lot.ServiceCalls, "only enterable houses (they have the repair spots)");
                Assert.IsFalse(string.IsNullOrEmpty(f.address));
                int arv = Flips.Arv(f, Flips.Today);
                Assert.That(arv, Is.InRange(120000, 320000), "small-town ranch ARV");
                Assert.Less(f.asking, arv * 0.9f, "distressed: well under ARV");
                Assert.Greater(f.asking, arv * 0.55f);
                Assert.IsTrue(f.items.Exists(i => i.id == "yard" && !i.upgrade), "overgrown yard on every report");
                Assert.IsTrue(f.items.Exists(i => i.id == "roof" || i.id == "siding" || i.id == "waterheater"), "one big-ticket problem");
                Assert.GreaterOrEqual(new List<FlipItem>(f.Problems).Count, 5);
                Assert.AreEqual(FlipDefs.Upgrades.Count, new List<FlipItem>(f.UpgradeItems).Count);
                foreach (var it in f.items) foreach (var s in it.defects) Assert.NotNull(JobCatalog.Get(s.def), s.def);
            }
            // locked for a handyman
            Company.Stage = CompanyStage.Handyman;
            Assert.IsFalse(Flips.Unlocked);
            Assert.IsNotNull(Flips.BuyBlocker(list[0], true));
            Company.Stage = CompanyStage.Contractor;
            // the flip-only catalog tasks validate like every other job
            Assert.IsEmpty(JobCatalog.Validate(JobCatalog.Get(FlipDefs.TrashOut)));
            Assert.IsEmpty(JobCatalog.Validate(JobCatalog.Get(FlipDefs.Landscape)));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Buy_FinancedShowsProblemsAndCharges()
        {
            var lot = TestLot();
            var f = Flips.AddListing(lot, 777);
            yield return Frames(2);
            StreetShot(lot, "flip_01_listed_for_sale");
            int cash = Flips.CashToClose(f, true), before = Game.Wallet.Money;
            Assert.AreEqual(f.asking - Flips.LoanAmount(f) + Mathf.RoundToInt(f.asking * 0.02f) + Mathf.RoundToInt(Flips.LoanAmount(f) * 0.02f), cash, "10% down + 2% closing + 2 points");
            // a service call at that address goes away once we own it
            var call = JobCatalog.CreateService(lot, true, null, JobCatalog.Get("plb.toilet-running"));
            Assert.IsTrue(Flips.Buy(f, true, out var msg), msg);
            Assert.AreEqual(before - cash, Game.Wallet.Money);
            Assert.AreEqual(FlipStatus.Owned, f.Status);
            Assert.AreEqual(Flips.LoanAmount(f), f.loanBalance);
            Assert.IsFalse(Company.Offers.Contains(call), "no customer jobs at a house the company owns");
            int defects = 0; foreach (var it in f.items) defects += it.defects.Count;
            Assert.AreEqual(defects, Flips.LiveDefects(f).Count, "every problem and dated finish is in the house");
            Assert.NotNull(FlipVisuals.RootOf(f), "street dressing");
            // daily holding costs: tax + vacant insurance + utilities + interest
            int carry = 0; foreach (var (label, amount) in Company.DailyCostBreakdown()) if (label.StartsWith("Flip")) carry += amount;
            Assert.AreEqual(Flips.DailyCarry(f), carry);
            Assert.That(Flips.DailyInterest(f), Is.InRange(25, 80), "11.5% on ~$120-160k");
            int spent = f.costSpent;
            Flips.Step(Flips.Today + 1);
            Assert.AreEqual(spent + Flips.DailyCarry(f), f.costSpent, "carry is tracked in the flip's costs");
            Assert.IsFalse(Flips.Value(f, Flips.Today) > f.asking * 1.08f, "as-is value stays near the purchase price");
            yield return Frames(2);
            StreetShot(lot, "flip_02_distressed_exterior");
            StreetShot(lot, "flip_02b_distressed_side", -9f);
            KitchenShot(lot, "flip_03_dated_kitchen");
            Assert.NotNull(Interior(f, FlipInterior.KitchenDated), "dated oak fronts + almond laminate in the kitchen");
            Assert.NotNull(Interior(f, FlipInterior.BathDated), "almond/pink tile on the tub walls");
            Assert.IsNull(Interior(f, FlipInterior.KitchenNew));
            var kd = Interior(f, FlipInterior.KitchenDated);
            Assert.IsNull(kd.GetComponent<Collider>(), "dressing never blocks the repair spots");
            BathShot(lot, "flip_03b_dated_bath");
        }

        [UnityTest]
        public IEnumerator Renovate_PlayerAndCrew_ThenSellForProfit()
        {
            var f = ListAndBuy();
            var lot = f.Lot;
            int day = Flips.Today;
            int asIs = Flips.Value(f, day);
            // crew: a carpenter and a laborer work the problems a day at a time
            var w1 = Company.Hire(new Worker { Name = "Test Carpenter", Trade = "carpenter", Skill = 4, DailyWage = 260, Morale = 0.8f });
            var w2 = Company.Hire(new Worker { Name = "Test Laborer", Trade = "laborer", Skill = 3, DailyWage = 170, Morale = 0.8f });
            Assert.NotNull(Flips.AssignNext(f)); Assert.NotNull(Flips.AssignNext(f));
            Assert.AreEqual(2, new List<Worker>(Flips.CrewOn(f)).Count);
            int open = 0; foreach (var it in f.Problems) if (!it.done) open++;
            Flips.Step(day + 1);
            int open2 = 0; foreach (var it in f.Problems) if (!it.done) open2++;
            Assert.Less(open2, open, "two workers finish at least one problem in a day");
            foreach (var it in f.items) if (it.done) foreach (var s in it.defects) Assert.IsTrue(Flips.LiveDefect(f, s).IsFixed, "crew work shows on the defect");
            Flips.Release(f);
            Assert.IsNull(w1.AssignedJobId); Assert.IsNull(w2.AssignedJobId);
            // the player does the rest and every upgrade with the normal repair steps
            PlayAll(f, true);
            Assert.IsTrue(Flips.AllProblemsFixed(f));
            Assert.AreEqual(1f, Flips.Condition(f));
            int value = Flips.Value(f, Flips.Today);
            Assert.Greater(value, asIs + 30000, "renovated + upgraded is worth a lot more");
            Assert.That(value, Is.InRange(Flips.Arv(f, Flips.Today) * 0.95f, Flips.Arv(f, Flips.Today) * 1.001f), "value reaches ARV");
            yield return Frames(3);
            Assert.IsTrue(Flips.List(f, value, out var msg), msg);
            yield return Frames(2);
            StreetShot(lot, "flip_04_after_renovation");
            StreetShot(lot, "flip_04b_after_renovation_side", -9f);
            KitchenShot(lot, "flip_05_renovated_kitchen");
            BathShot(lot, "flip_05b_renovated_bath");
            Assert.NotNull(Interior(f, FlipInterior.KitchenNew), "white shaker fronts + quartz once the kitchen upgrade is done");
            Assert.NotNull(Interior(f, FlipInterior.BathNew), "white subway tile once the bath refresh is done");
            Assert.IsNull(Interior(f, FlipInterior.KitchenDated));
            Assert.IsNull(Interior(f, FlipInterior.BathDated));
            // offers come at value; accept and close
            Assert.Less(Flips.ExpectedDaysToOffer(f, value, Flips.Today), 30);
            Assert.AreEqual(int.MaxValue, Flips.ExpectedDaysToOffer(f, Mathf.RoundToInt(value * 1.2f), Flips.Today), "15%+ over value: no buyers");
            int d = Flips.Today;
            for (int i = 1; i <= 60 && !f.HasOffer; i++) { d++; if (GameClock.I != null) GameClock.I.Day = d; Flips.Step(d); }
            Assert.IsTrue(f.HasOffer, "an offer within two months at value");
            Assert.That(f.offerPrice, Is.InRange(value * 0.93f, value * 1.03f));
            int cashBefore = Game.Wallet.Money, loan = f.loanBalance, price = f.offerPrice;
            Assert.IsTrue(Flips.AcceptOffer(f, out msg), msg);
            Assert.AreEqual(FlipStatus.Sold, f.Status);
            int net = price - Mathf.RoundToInt(price * 0.055f) - Mathf.RoundToInt(price * 0.015f);
            Assert.AreEqual(cashBefore + net - loan, Game.Wallet.Money, "proceeds less commission, closing and the loan payoff");
            Assert.AreEqual(0, f.loanBalance);
            Assert.Greater(Flips.Profit(f), 10000, "a full renovation is worth doing");
            Assert.AreEqual(0, Flips.LiveDefects(f).Count, "the new owners' house is back to normal");
            int carry = 0; foreach (var (label, amount) in Company.DailyCostBreakdown()) if (label.StartsWith("Flip")) carry += amount;
            Assert.AreEqual(0, carry, "no holding costs after the sale");
            yield return Frames(2);
            StreetShot(lot, "flip_06_sold");
        }

        [UnityTest]
        public IEnumerator Unfixed_SellsForLess_AndSlower()
        {
            Game.Wallet = new Wallet(250000);
            var f = ListAndBuy(false);
            int day = Flips.Today;
            int asIs = Flips.Value(f, day);
            Assert.Less(Flips.Condition(f), 0.01f);
            // only the yard: still worth roughly as-is + the yard discount
            var yard = f.items.Find(i => i.id == "yard");
            foreach (var s in yard.defects) JobCatalog.Solve(Flips.LiveDefect(f, s));
            Flips.SyncAll();
            Assert.IsTrue(yard.done);
            Assert.AreEqual(asIs + 4500, Flips.Value(f, day), 600, "the trash-out adds its buyer discount back");
            int slow = Flips.ExpectedDaysToOffer(f, Flips.Value(f, day), day);
            PlayAll(f, false);
            int fast = Flips.ExpectedDaysToOffer(f, Flips.Value(f, day), day);
            Assert.Less(fast, slow, "a house with open problems sits longer");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PartsUsed_CountInFlipCosts_Once()
        {
            var f = ListAndBuy();
            // the player does the kitchen: the faucet, paint and pulls used on its steps go into the flip's costs
            var kitchen = f.items.Find(i => i.id == "kitchen");
            Assert.NotNull(kitchen);
            int spent = f.costSpent;
            foreach (var s in kitchen.defects) { var d = Flips.LiveDefect(f, s); Assert.NotNull(d, s.def); JobCatalog.Solve(d); }
            int parts = Flips.PartsUsed(kitchen);
            Assert.Greater(parts, 0, "the kitchen steps use parts");
            Flips.SyncAll();
            Assert.AreEqual(spent + parts, f.costSpent, "parts used on the flip are flip costs");
            Flips.SyncAll();
            Flips.Sync(f);
            Assert.AreEqual(spent + parts, f.costSpent, "each use is counted once");
            // a crew finishes the bath: its materials are billed once (not again from the parts the solver records)
            var bath = f.items.Find(i => i.id == "bath");
            int crewMats = 0;
            foreach (var s in bath.defects) { var jd = JobCatalog.Get(s.def); crewMats += Mathf.RoundToInt(jd.MaterialCostFor(jd.Causes[s.cause])); }
            int before = f.costSpent, cash = Game.Wallet.Money;
            Flips.FinishByCrew(f, bath, 0.9f);
            Flips.SyncAll();
            Assert.AreEqual(before + crewMats, f.costSpent, "crew materials counted once");
            Assert.AreEqual(cash - crewMats, Game.Wallet.Money);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SaveLoad_RoundTrip()
        {
            var f = ListAndBuy();
            var yard = f.items.Find(i => i.id == "yard");
            foreach (var s in yard.defects) JobCatalog.Solve(Flips.LiveDefect(f, s));
            Flips.SyncAll();
            var other = Flips.AddListing(new List<TownLot>(Flips.Candidates())[0], 99);
            int loan = f.loanBalance, spent = f.costSpent, liveBefore = Flips.LiveDefects(f).Count;
            string id = f.id, addr = f.address;
            Assert.IsTrue(SaveSystem.Save(Slot), SaveSystem.LastMessage);
            Flips.ResetState();
            Assert.AreEqual(0, Flips.All.Count);
            Assert.AreEqual(0, Flips.LiveDefects(f).Count, "reset takes the defects out");
            Assert.IsTrue(SaveSystem.Load(Slot), SaveSystem.LastMessage);
            yield return Frames(3);
            var g = Flips.Get(id);
            Assert.NotNull(g, "flip restored");
            Assert.AreEqual(FlipStatus.Owned, g.Status);
            Assert.AreEqual(addr, g.address);
            Assert.AreEqual(loan, g.loanBalance); Assert.AreEqual(spent, g.costSpent);
            Assert.IsTrue(g.items.Find(i => i.id == "yard").done, "punch-list progress kept");
            Assert.AreEqual(liveBefore, Flips.LiveDefects(g).Count, "defects back in the house");
            Assert.NotNull(Flips.Get(other.id), "market listing kept");
            Assert.NotNull(FlipVisuals.RootOf(g));
        }

        /// <summary>The Flips tablet app (IMGUI: only renders in a windowed run, see Docs/UI-CAPTURE.md).</summary>
        [UnityTest, Category("UICapture")]
        public IEnumerator CaptureFlipsApp()
        {
            if (Application.isBatchMode) { Assert.Ignore("IMGUI does not render in -batchmode: run windowed, see Docs/UI-CAPTURE.md"); yield break; }
            UICaptureTests.SetGameView(1600, 900);
            if (MainMenu.Showing) MainMenu.I.Hide();
            Flips.RefreshMarket(Flips.Today);
            Assert.NotNull(Tablet.I);
            FlipApp.ShowTab(0);
            Tablet.I.ShowApp(FlipApp.AppName);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Capture("flip_app_market");
            var f = ListAndBuy();
            var yard = f.items.Find(i => i.id == "yard");
            foreach (var s in yard.defects) JobCatalog.Solve(Flips.LiveDefect(f, s));
            FlipApp.ShowTab(1, f.id);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Capture("flip_app_my_flip");
            Tablet.I.Close();
        }

        static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var dir = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
