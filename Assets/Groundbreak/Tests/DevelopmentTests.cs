using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Builder + Developer stages: plan takeoff, plan overlay, draw payments on inspections, land + subdivision,
    /// spec house sale, save round-trip. Content lives at x ≥ 420 (fallback lots), far from other tests. Shots → Logs/shots/.</summary>
    public class DevelopmentTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        static Camera Cam(Vector3 pos, Vector3 look)
        {
            var cam = new GameObject("DevTestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = 55; cam.farClipPlane = 2000f;
            return cam;
        }

        [SetUp]
        public void Begin()
        {
            if (GameClock.I != null) { GameClock.I.Hours = 10.5f; GameClock.I.Day = 1; }
            Weather.ForceRain = false;
            DevLots.Provider = new FallbackLotProvider();   // these tests use the fallback lots even when the Town is merged
        }

        [TearDown]
        public void Cleanup()
        {
            Projects.LeaveSite(); Projects.ResetState(); Developments.ResetState(); DevLots.Provider = null;
            PlanOverlay.Hide(); DevVisuals.RefreshNow();
            Company.ResetState(); Career.ResetState(); Inspections.ResetState(); Inspections.RuleFilter = null;
            Site.Lot = null; GameSettings.Mode = GameMode.FreeBuild; GameSettings.UnlimitedMoney = false; Weather.ForceRain = null;
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c.name == "DevTestCam") Object.Destroy(c.gameObject);
            if (NeighborhoodComplete.I != null) { Object.Destroy(NeighborhoodComplete.I.gameObject); NeighborhoodComplete.I = null; Game.UIOpen = false; }
        }

        // ------------------------------------------------------------------ takeoff

        [Test]
        public void StarterPlanTakeoffMatchesHandCount()
        {
            var p = HousePlans.Get("starter-900");
            var t = p.Takeoff;
            Assert.AreEqual(900f, p.SqFt);
            Assert.AreEqual(120f, t.PerimeterFt, 0.01f);
            Assert.AreEqual(120f, t.ExtWallLf, 0.01f);
            Assert.AreEqual(74f, t.IntWallLf, 0.01f, "partitions: 30' hall wall + 16' + 14' + 14'");
            Assert.AreEqual(8, t.Windows); Assert.AreEqual(1, t.ExtDoors); Assert.AreEqual(3, t.IntDoors);
            // studs: 16" O.C. per wall (4x23 + 23+13+11+11) + 2 per opening (12) + 2 per corner (4) + 2 per partition (4)
            Assert.AreEqual(150 + 24 + 8 + 8, t.Studs);
            Assert.AreEqual(3f * (120f + 74f), t.PlateLf, 0.01f);
            Assert.AreEqual(110f, t.OpeningSqFt, 0.05f, "5 3648 + 2 4836 + 1 2436 windows + one 36x80 door");
            Assert.AreEqual(30, t.SheathingSheets, "ceil((960-110)/32 x 1.10)");
            Assert.AreEqual(69, t.DrywallSheets, "ceil((850 + 2x74x8 + 900)/48 x 1.12)");
            Assert.AreEqual((120f * 16f / 12f + 900f * 4f / 12f) / 27f, t.ConcreteYards, 0.01f, "16\"x12\" footing + 4\" slab");
            Assert.AreEqual(16, t.Trusses);
            Assert.AreEqual(37, t.ShingleBundles, "32'x32' roof at 5/12 = 11.09 sq x 1.10 x 3");
            foreach (var pl in HousePlans.All)
                Debug.Log($"[Dev] {pl.Id}: {pl.SqFt} sf, walls {pl.Walls.Count}, openings {pl.Openings.Count}, materials ${pl.Takeoff.MaterialTotal:N0}, total ${pl.Takeoff.Total:N0} (${pl.Takeoff.Total / pl.SqFt:0}/sf), {pl.TotalDays} days");
            Assert.AreEqual(6, HousePlans.All.Count);
            Assert.AreEqual(2400f, HousePlans.Get("twostory-2400").SqFt);
            foreach (var pl in HousePlans.All) Assert.That(pl.Takeoff.Total / pl.SqFt, Is.InRange(95f, 200f), pl.Id + " $/sq ft");
        }

        // ------------------------------------------------------------------ overlay

        [UnityTest]
        public IEnumerator PlanOverlayOnLot()
        {
            yield return Frames(3);
            var lot = DevLots.Provider.Find("ridge-101");
            var plan = HousePlans.Get("ranch-1600");
            var at = PlanPlacement.On(lot, plan);
            Assert.IsTrue(lot.Contains(at.World(0, 0)) && lot.Contains(at.World(plan.WidthFt, plan.DepthFt)), "plan inside the lot");
            Assert.AreEqual(-lot.Front.z, at.Y.z, 1e-4f, "plan depth runs away from the street");
            var root = PlanOverlay.Show(plan, at, 0);
            Assert.NotNull(root);
            Assert.AreEqual(plan.Walls.Count, PlanOverlay.WallCount);
            Assert.AreEqual(plan.Openings.Count, PlanOverlay.OpeningCount);
            Assert.AreEqual(plan.Rooms.Count, PlanOverlay.LabelCount);
            Assert.Greater(root.GetComponentsInChildren<TextMesh>().Length, plan.Rooms.Count, "room + opening labels");
            yield return Frames(2);
            var c = at.World(plan.WidthFt / 2, plan.DepthFt / 2);
            Shot(Cam(c + new Vector3(0, 19f, -9f), c + new Vector3(0, 0, -1f)), "dev_01_plan_overlay");
            Shot(Cam(at.World(-6, -14, 1.7f), c), "dev_02_plan_overlay_eye");
        }

        // ------------------------------------------------------------------ builder draws

        [UnityTest]
        public IEnumerator BuilderDrawsPaidOnInspections()
        {
            yield return Frames(3);
            GameSettings.Mode = GameMode.Career;
            Company.ResetState(); Career.ResetState();
            Company.Stage = CompanyStage.Builder; Company.Reputation = 70;
            Inspections.RuleFilter = r => false;   // inspection rules belong to the trades; here we test the money
            Builder.RefreshOffers();
            var job = Company.Offers.Find(j => j.Kind == JobKind.House && j.Title.StartsWith("Starter"));
            Assert.NotNull(job, "house contract on the job board");
            Assert.AreEqual(CompanyStage.Builder, job.MinStage);
            int price = job.Payout, start = Game.Wallet.Money;
            Assert.IsTrue(Company.Accept(job, out var msg), msg);
            var p = Projects.ForJob(job.Id);
            Assert.NotNull(p, "project created on accept");
            Assert.AreEqual(start + price / 10, Game.Wallet.Money, "10% deposit");
            Assert.IsTrue(p.Lot.Claimed);
            Projects.GoOnSite(p);
            Assert.AreEqual(p.Lot.Rect, Site.Lot.Value);
            Assert.IsFalse(Inspections.RequiredPhases.Contains(InspectionPhase.UnderSlab), "crawlspace plan: no under-slab inspection");

            var phases = new[] { InspectionPhase.Footing, InspectionPhase.Framing, InspectionPhase.RoughMEP, InspectionPhase.Insulation };
            for (int i = 0; i < 4; i++)
            {
                int before = Game.Wallet.Money;
                Assert.IsTrue(Inspections.Inspect(phases[i]).Passed);
                Assert.AreEqual(before + Mathf.RoundToInt(price * Builder.DrawPct[i]), Game.Wallet.Money, "draw " + (i + 1) + " on " + phases[i]);
                Assert.IsTrue(p.PhaseDone((BuildPhase)i));
            }
            // inspecting a passed phase again pays nothing twice
            int again = Game.Wallet.Money; Inspections.Inspect(InspectionPhase.Framing); Assert.AreEqual(again, Game.Wallet.Money);
            Assert.AreEqual(3, p.ChangeOrders.Count, "change orders offered after framing");
            var co = p.ChangeOrders[2];
            int beforeCo = Game.Wallet.Money, due = p.DueDay;
            Assert.IsTrue(Builder.AcceptChange(p, co, out var cm), cm);
            Assert.AreEqual(beforeCo - co.Cost, Game.Wallet.Money);
            Assert.AreEqual(due + co.Days, p.DueDay);

            int beforeFinal = Game.Wallet.Money;
            Assert.IsTrue(Inspections.Inspect(InspectionPhase.Final).Passed);
            Assert.IsTrue(p.Complete);
            Assert.IsTrue(job.Done, "job completed through Company");
            int final = Mathf.RoundToInt(price * Builder.DrawPct[4]) + co.Price;
            Assert.AreEqual(beforeFinal + Mathf.RoundToInt(final * 1.1f), Game.Wallet.Money, "final draw + change order (clean inspections = full quality)");
            Assert.Contains("house", Career.Completed);
            Assert.Greater(p.WarrantyDay, Projects.Day);

            var wj = Builder.IssueWarranty(p, p.WarrantyDay);
            Assert.IsTrue(Company.ActiveJobs.Contains(wj));
            int rep = Company.Reputation;
            Assert.IsTrue(Builder.ResolveWarranty(p, out var wm), wm);
            Assert.IsFalse(Company.ActiveJobs.Contains(wj));
            Assert.GreaterOrEqual(Company.Reputation, rep, "warranty handled: no reputation loss");
        }

        // ------------------------------------------------------------------ developer

        static Development BuyAndPlat(out int landPaid)
        {
            GameSettings.Mode = GameMode.Career;
            Company.ResetState(); Company.Stage = CompanyStage.Developer; Company.Reputation = 85;
            Game.Wallet = new Wallet(6_000_000);
            var tract = DevLots.Provider.Find("tract-a");
            int before = Game.Wallet.Money;
            Assert.IsTrue(Developments.BuyLand(tract, out var d, out var m), m);
            landPaid = before - Game.Wallet.Money;
            Assert.AreEqual(Mathf.RoundToInt(Developments.LandPrice(tract) * 1.02f), landPaid, 1, "price + 2% closing");
            Assert.That(tract.Acres, Is.InRange(5f, 5.5f));
            Assert.IsTrue(tract.Claimed);
            Assert.AreEqual(14, Developments.Subdivide(d, 70f, out m), m);
            return d;
        }

        [UnityTest]
        public IEnumerator BuyLandAndSubdivide()
        {
            yield return Frames(3);
            var d = BuyAndPlat(out _);
            var tract = d.Tract.Rect;
            foreach (var l in d.Lots)
            {
                Assert.AreEqual(70f, l.FrontageFt, 0.05f);
                Assert.AreEqual(130f, l.DepthFt, 0.05f);
                Assert.IsTrue(tract.Contains(l.Rect.min + Vector2.one * 0.01f) && tract.Contains(l.Rect.max - Vector2.one * 0.01f), l.Id + " inside the tract");
                Assert.IsFalse(l.Rect.Overlaps(d.StreetRect), l.Id + " clear of the street");
                Assert.Less(Vector3.Distance(l.FrontMid, new Vector3(d.StreetRect.center.x, 0, l.FrontMid.z)), Developments.StreetRowFt * Units.Foot / 2f + 0.01f, "lot fronts the new street");
                foreach (var o in d.Lots) if (o != l) Assert.IsFalse(l.Rect.Overlaps(new Rect(o.Rect.x + 0.01f, o.Rect.y + 0.01f, o.Rect.width - 0.02f, o.Rect.height - 0.02f)), "lots overlap");
            }
            int cost = Developments.SiteWorkCostFor(d), before = Game.Wallet.Money;
            Assert.AreEqual(Mathf.RoundToInt(d.StreetLengthFt * 630f + 14 * 3500), cost);
            Assert.IsTrue(Developments.StartSiteWork(d, out var m), m);
            Assert.AreEqual(before - cost, Game.Wallet.Money);
            Assert.IsFalse(d.SiteWorkDone);
            var p = Developments.StartSpecHouse(d, d.Lots[0], HousePlans.Get("starter-900"), 0.6f, out m);
            Assert.NotNull(p, m);
            Projects.DayStep(2);
            Assert.AreEqual(0f, p.Progress[0], "no building until the street and utilities are in");
            Assert.IsNull(Developments.StartSpecHouse(d, d.Lots[0], HousePlans.Get("cottage-1200"), 0.6f, out m), "one house per lot");
        }

        [UnityTest]
        public IEnumerator SpecNeighborhoodBuiltListedAndSold()
        {
            yield return Frames(3);
            var d = BuyAndPlat(out _);
            Assert.IsTrue(Developments.StartSiteWork(d, out var m), m);
            int day = 1;
            void Step() { day++; GameClock.I.Day = day; GameClock.I.Hours = 10.5f; Projects.DayStep(day); Developments.Step(day); }
            for (int i = 0; i < d.SiteWorkDays; i++) Step();
            Assert.IsTrue(d.SiteWorkDone);
            string[] plans = { "starter-900", "cottage-1200", "ranch-1600", "craftsman-1920", "twostory-2400" };
            var houses = new List<HouseProject>();
            // staggered starts so the street shows every stage
            for (int i = 0; i < 10; i++)
            {
                var plan = HousePlans.Get(plans[i % plans.Length]);
                var h = Developments.StartSpecHouse(d, d.Lots[i], plan, 0.6f, out m);
                Assert.NotNull(h, m); houses.Add(h);
                for (int k = 0; k < 9; k++) Step();
            }
            var first = houses[0];
            Assert.IsTrue(first.Complete, "the first starter house is finished: " + first.StatusLabel);
            Assert.Greater(houses[9].Progress[0], 0f, "the newest house has started its foundation");
            Assert.IsFalse(houses[8].Complete);
            Assert.Greater(Developments.LoanBalance, 0, "construction loans drawn");
            Assert.Greater(first.LoanBalance, 0);
            DevVisuals.RefreshNow();
            yield return Frames(2);
            Assert.GreaterOrEqual(DevVisuals.Houses.Count, 10);
            var mid = d.Lots[8].FrontMid;
            Shot(Cam(d.Tract.FrontMid + new Vector3(-25f, 45f, -35f), mid + new Vector3(0, 0, 0)), "dev_03_subdivision_stages");
            Shot(Cam(d.Lots[4].FrontMid + new Vector3(18f, 6f, -2f), d.Lots[2].FrontMid + Vector3.up * 2f), "dev_04_street_level");
            Shot(Cam(d.Lots[16 % d.Lots.Count].FrontMid + new Vector3(-14f, 7f, 6f), d.Lots[17 % d.Lots.Count].Center), "dev_05_framing_closeup");

            // list the finished house at value and let the market sell it
            int value = Developments.MarketValue(first, day);
            Assert.That(value, Is.InRange(150000, 260000), "900 sq ft new house value");
            Assert.IsTrue(Developments.List(first, value, out m), m);
            int wallet = Game.Wallet.Money, loan = first.LoanBalance;
            for (int i = 0; i < 60 && !first.Sold; i++) Step();
            Assert.IsTrue(first.Sold, "sold within 60 days at market value");
            Assert.AreEqual(0, first.LoanBalance, "loan paid off at closing");
            int net = first.SalePrice - Mathf.RoundToInt(first.SalePrice * Developments.CommissionPct) - Mathf.RoundToInt(first.SalePrice * Developments.ClosingPct);
            Assert.LessOrEqual(Mathf.Abs(Game.Wallet.Money - (wallet + net - loan)), 200000, "proceeds minus loan payoff (other houses keep drawing equity)");
            Debug.Log($"[Dev] spec sale {first.Lot.Address}: price ${first.SalePrice:N0}, cost ${first.CostSpent:N0}, profit ${Developments.Profit(first):N0}, DOM {first.SoldDay - first.ListedDay}");
            Assert.AreEqual(first.SalePrice - first.CostSpent, Developments.Profit(first));

            // overpriced listings sit
            Assert.AreEqual(int.MaxValue, Developments.DaysToSell(1.2f));
            Assert.Greater(Developments.DaysToSell(1.08f), Developments.DaysToSell(1.0f));
        }

        [UnityTest]
        public IEnumerator NeighborhoodCompletesWithSummary()
        {
            yield return Frames(3);
            var d = BuyAndPlat(out _);
            GameSettings.UnlimitedMoney = true;
            d.SiteWorkStartDay = 0; d.SiteWorkDays = 0; d.SiteWorkCost = Developments.SiteWorkCostFor(d);
            int day = 1;
            foreach (var l in d.Lots) Assert.NotNull(Developments.StartSpecHouse(d, l, HousePlans.Get("starter-900"), 0.35f, out var m), m);
            bool done = false; Developments.Completed += x => done = x == d;
            for (int i = 0; i < 260 && !done; i++)
            {
                day++; GameClock.I.Day = day; Projects.DayStep(day);
                foreach (var h in d.Houses) if (h.Complete && !h.Listed && !h.Sold) Developments.List(h, Mathf.RoundToInt(Developments.MarketValue(h, day) * 0.97f), out _);
                Developments.Step(day);
            }
            Assert.IsTrue(done && d.Complete, $"all lots sold ({d.SoldCount}/{d.Lots.Count})");
            Assert.NotNull(NeighborhoodComplete.I, "completion screen shown");
            var lines = NeighborhoodComplete.Summary(d);
            foreach (var l in lines) Debug.Log("[Dev] " + l);
            StringAssert.Contains("14 homes built and sold", lines[0]);
            DevVisuals.RefreshNow();
            yield return Frames(2);
            Shot(Cam(d.Tract.FrontMid + new Vector3(30f, 55f, -30f), d.Tract.Center), "dev_06_neighborhood_complete");
        }

        // ------------------------------------------------------------------ save

        [UnityTest]
        public IEnumerator SaveRoundTrip()
        {
            yield return Frames(3);
            var d = BuyAndPlat(out _);
            d.SiteWorkStartDay = 0; d.SiteWorkDays = 0;
            var h = Developments.StartSpecHouse(d, d.Lots[3], HousePlans.Get("ranch-1600"), 0.9f, out var m);
            Assert.NotNull(h, m);
            for (int i = 2; i < 30; i++) { GameClock.I.Day = i; Projects.DayStep(i); Developments.Step(i); }
            // a client house the player is working on site
            Company.Stage = CompanyStage.Builder;
            Builder.RefreshOffers();
            var job = Company.Offers.Find(j => j.Kind == JobKind.House);
            Company.Reputation = 90;
            Assert.IsTrue(Company.Accept(job, out m), m);
            var client = Projects.ForJob(job.Id);
            Projects.GoOnSite(client);
            Inspections.RuleFilter = r => false;
            Inspections.Inspect(InspectionPhase.Footing);
            float prog = h.Progress[(int)h.CurrentPhase.Value]; var phase = h.CurrentPhase.Value; int loan = h.LoanBalance, spent = h.CostSpent;

            Assert.IsTrue(SaveSystem.Save("test_development"), SaveSystem.LastMessage);
            Projects.LeaveSite(); Projects.ResetState(); Developments.ResetState(); DevLots.Provider = null; Inspections.ResetState();
            Assert.IsTrue(SaveSystem.Load("test_development"), SaveSystem.LastMessage);
            yield return Frames(2);
            Assert.AreEqual(1, Developments.All.Count);
            var d2 = Developments.All[0];
            Assert.AreEqual(14, d2.Lots.Count); Assert.IsTrue(d2.Tract.Claimed, "tract still owned");
            var h2 = Projects.Get(h.Id);
            Assert.NotNull(h2);
            Assert.AreEqual(phase, h2.CurrentPhase.Value); Assert.AreEqual(prog, h2.Progress[(int)phase], 1e-4f);
            Assert.AreEqual(loan, h2.LoanBalance); Assert.AreEqual(spent, h2.CostSpent); Assert.AreEqual(0.9f, h2.FinishLevel, 1e-4f);
            Assert.AreEqual(client.Id, Projects.OnSiteId, "player still on the client site");
            Assert.IsTrue(Inspections.IsPassed(InspectionPhase.Footing), "on-site inspection state restored");
            Assert.AreEqual(Projects.OnSite.Lot.Rect, Site.Lot.Value);
            Assert.IsTrue(Projects.OnSite.Lot.Claimed);
            Assert.NotNull(PlanOverlay.Root, "overlay back on the site");
            SaveSystem.Delete("test_development");
        }
    }
}
