using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Commercial / industrial builds on the map's tracts (pad sites), the new downtown storefront / bank / grocery plans,
    /// Ridge Rd on open ground, the tablet rules and the catalog build accept guard. Shots Logs/shots/mapcom_*.</summary>
    public class MapCommercialTests
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
            Game.UIOpen = false;
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

        static IEnumerable<JobDef> CommercialBuildDefs() => JobCatalog.All.Where(d => d.Mode == JobMode.Build)
            .Where(d => { var p = JobCatalog.PlanFor(d); return p != null && (p.Group == PlanGroup.Commercial || p.Group == PlanGroup.Industrial); });

        // ------------------------------------------------------------------ builds on tracts

        [UnityTest]
        public IEnumerator EveryCommercialAndIndustrialBuildFindsATract()
        {
            yield return BuildTown();
            var defs = CommercialBuildDefs().ToList();
            Assert.GreaterOrEqual(defs.Count, 5, "office, warehouse, retail, diner, gas station");
            foreach (var d in defs)
            {
                var plan = JobCatalog.PlanFor(d);
                var job = JobCatalog.CreateBuild(d, false);
                Assert.NotNull(job, $"{d.Id} ({plan.Id} {plan.WidthFt}'x{plan.DepthFt}') found no lot");
                var pl = JobCatalog.ReadBuild(job);
                var site = DevLots.Provider.Find(pl.lot);
                Assert.NotNull(site, d.Id + " site resolves by id");
                var tract = CommercialSites.TownLotOf(site);
                Assert.NotNull(tract, d.Id + " tract");
                Assert.AreEqual(tract.Address, job.Address, d.Id + " job address is the tract's");
                if (plan.Group == PlanGroup.Commercial) Assert.AreEqual("commercial", tract.Category, d.Id + " commercial build on a commercial lot");
                else Assert.IsTrue(tract.Category == "industrial" || tract.District == "strip", d.Id + " industrial build on an industrial lot, got " + tract.Category);
                // the building: inside the tract, facing the road, real setback, the parking / truck side clear
                bool ind = plan.Group == PlanGroup.Industrial;
                var at = PlanPlacement.On(site, plan);
                var back = -tract.Front;
                Assert.Greater(Vector3.Dot(at.Y, back), 0.99f, d.Id + " faces the road");
                float setback = (ind ? CommercialSites.IndustrialSetbackFt : CommercialSites.CommercialSetbackFt) * Units.Foot;
                foreach (var (x, y) in new[] { (0f, 0f), (plan.WidthFt, 0f), (0f, plan.DepthFt), (plan.WidthFt, plan.DepthFt) })
                {
                    var c = at.World(x, y);
                    Assert.IsTrue(tract.Rect.Contains(new Vector2(c.x, c.z)), $"{d.Id} corner ({x},{y}) outside the tract");
                    Assert.GreaterOrEqual(Vector3.Dot(c - tract.FrontMid, back), setback - 0.05f, $"{d.Id} front setback");
                }
                var X = Vector3.Cross(Vector3.up, back);
                float right = Vector3.Dot(at.World(plan.WidthFt, 0f) - tract.FrontMid, X);
                float clear = tract.Frontage / 2f - right;
                Assert.GreaterOrEqual(clear, (ind ? CommercialSites.IndustrialClearFt : CommercialSites.CommercialClearFt) * Units.Foot - 0.05f, d.Id + " clear side for parking / trucks");
                Debug.Log($"[MapCom] {d.Id}: {plan.Id} on {tract.Id} ({tract.Category}/{tract.District}, {tract.AreaSqFt:N0} sq ft), setback {Vector3.Dot(at.Origin - tract.FrontMid, back) / Units.Foot:0}', clear side {clear / Units.Foot:0}'");
            }
            // houses still never go on a tract or a commercial lot
            var house = JobCatalog.All.First(x => x.Mode == JobMode.Build && JobCatalog.PlanFor(x) != null && JobCatalog.PlanFor(x).Group == PlanGroup.Residential);
            foreach (var l in MapJobs.BuildLotsFor(JobCatalog.PlanFor(house)).Take(20))
            {
                Assert.IsFalse(CommercialSites.IsPadId(l.Id), "house on a pad site");
                var tl = Town.Find(l.Id);
                if (tl != null) Assert.AreEqual("residential", tl.Category);
            }
        }

        [UnityTest]
        public IEnumerator PadSiteClaimsAndReleasesItsTract()
        {
            yield return BuildTown();
            var plan = HousePlans.Get("stripmall-6000");
            Assert.NotNull(plan);
            var site = MapJobs.BuildLotsFor(plan).First(l => CommercialSites.IsPadId(l.Id));
            Assert.IsTrue(CommercialSites.IsPadId(site.Id));
            var tract = CommercialSites.TownLotOf(site);
            Assert.IsTrue(tract.IsFree);
            Assert.IsTrue(VacantLots.ByLot.TryGetValue(tract.Id, out var dressing) && dressing != null, "tract dressed as vacant land");
            Assert.IsTrue(dressing.activeSelf);
            // claim (as OnBuildAccepted does) → the tract is taken and its dressing hides; the project finds its pad again by id
            Assert.IsTrue(DevLots.Provider.Claim(site, Company.Name));
            Assert.IsFalse(tract.IsFree, "tract claimed");
            Assert.IsFalse(dressing.activeSelf, "vacant-lot dressing hidden on the claimed tract");
            Assert.IsFalse(MapJobs.BuildLotsFor(plan).Any(l => CommercialSites.TownLotOf(l) == tract), "a claimed tract is not offered again");
            var p = Projects.Create(ProjectKind.Client, site, plan);
            Assert.NotNull(p.Lot, "project resolves its pad");
            Assert.AreEqual(site.Rect, p.Lot.Rect);
            Assert.IsTrue(p.Lot.Claimed);
            DevVisuals.Refresh();
            PlanOverlay.Show(plan, p.Placement, 0);
            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            var at = p.Placement; var mid = at.World(plan.WidthFt / 2f, plan.DepthFt / 2f);
            var X = Vector3.Cross(Vector3.up, -tract.Front);
            Shot(tract.FrontMid + tract.Front * 30f + X * 25f + Vector3.up * 45f, mid, "mapcom_01_stripmall_pad_aerial", 60f, true);
            Shot(tract.FrontMid + tract.Front * 8f + X * 20f + Vector3.up * 1.7f, mid + Vector3.up * 1f, "mapcom_02_stripmall_pad_street");
            // release → free again, dressing back
            PlanOverlay.Hide();
            Projects.All.Remove(p);
            DevVisuals.Refresh();
            DevLots.Provider.Release(site);
            Assert.IsTrue(tract.IsFree, "released");
            Assert.IsTrue(dressing.activeSelf, "dressing back on release");

            // industrial: a warehouse on an industrial tract
            var wh = HousePlans.Get("warehouse-6000");
            var ws = MapJobs.BuildLotsFor(wh).First(l => CommercialSites.IsPadId(l.Id));
            var wt = CommercialSites.TownLotOf(ws);
            Assert.AreEqual("industrial", wt.Category);
            PlanOverlay.Show(wh, PlanPlacement.On(ws, wh), 0);
            yield return Frames(2);
            var wm = PlanPlacement.On(ws, wh).World(wh.WidthFt / 2f, wh.DepthFt / 2f);
            Shot(wt.FrontMid + wt.Front * 25f + Vector3.Cross(Vector3.up, -wt.Front) * 30f + Vector3.up * 40f, wm, "mapcom_03_warehouse_pad_aerial", 60f, true);
        }

        [UnityTest]
        public IEnumerator BuildAcceptIsUndoneWhenTheLotIsGone()
        {
            yield return BuildTown();
            Company.Stage = CompanyStage.Developer; Company.Reputation = 100;
            var d = JobCatalog.Get("bld.diner");
            Assert.NotNull(d);
            var job = JobCatalog.CreateBuild(d, true);
            Assert.NotNull(job);
            var site = DevLots.Provider.Find(JobCatalog.ReadBuild(job).lot);
            var tract = CommercialSites.TownLotOf(site);
            Assert.IsTrue(Town.Claim(tract, LotUse.BuildSite, "rival-builder"), "someone else takes the tract");
            int cash = Game.Wallet.Money;
            Company.Accept(job, out _);
            Assert.IsFalse(job.Accepted, "accept undone");
            Assert.IsFalse(Company.ActiveJobs.Contains(job), "not an active job");
            Assert.IsFalse(Company.Offers.Contains(job), "dead offer withdrawn");
            Assert.AreEqual(cash, Game.Wallet.Money, "no deposit paid");
            Assert.AreEqual("rival-builder", tract.Owner, "the other claim is untouched");
            Town.Release(tract);
        }

        // ------------------------------------------------------------------ new plans

        static readonly string[] NewPlans = { "storefront-24", "storefront-30", "bank-2800", "grocery-20000" };

        [Test]
        public void NewDowntownPlansPassTheChecks()
        {
            foreach (var id in NewPlans)
            {
                var b = Blueprints.GetBlueprint(id);
                Assert.NotNull(b, id + " registered");
                Assert.AreSame(b, HousePlans.Get(id));
                var errs = BlueprintValidator.Errors(b);
                Assert.IsEmpty(errs, id + ":\n" + string.Join("\n", errs));
                float per = b.Takeoff.Total / Mathf.Max(1f, b.SqFt);
                Assert.That(per, Is.InRange(90f, 400f), $"{id} ${per:0}/sq ft");
                Debug.Log($"[MapCom] {id}: {b.Summary} · ${b.Takeoff.Total:N0} (${per:0}/sf) · OL {b.OccupantLoad} · {b.Fixtures.Count} fixtures");
            }
            foreach (var id in new[] { "storefront-24", "storefront-30" })
            {
                var b = Blueprints.GetBlueprint(id);
                Assert.That(b.WidthFt, Is.InRange(20f, 30f));
                Assert.AreEqual(2, b.Floors);
                Assert.AreEqual(1, b.Stairs.Count, id + " stair");
                var s = b.Stairs[0];
                Assert.GreaterOrEqual(s.WidthFt * 12f, 36f, "stair at least 36\" wide");
                Assert.LessOrEqual(s.RiserIn, 7f, "IBC riser"); Assert.GreaterOrEqual(s.TreadIn, 11f, "IBC tread");
                Assert.IsTrue(b.Takeoff.Lines.Exists(l => l.Item.Contains("handrails")), "handrails in the takeoff");
                // party walls: no windows or doors on the side walls; the apartment entry is walled off from the shop
                foreach (var o in b.Openings)
                {
                    var w = b.Walls[o.Wall];
                    if (!w.Exterior) continue;
                    bool side = Mathf.Abs(w.A.x - w.B.x) < 0.01f;
                    Assert.IsFalse(side, $"{id}: {o.ProductId} on a party wall");
                }
                var entry = b.Rooms.Find(r => r.Name.StartsWith("Entry"));
                var shop = b.Rooms.Find(r => r.Name == "Sales floor");
                Assert.IsFalse(b.Connected(entry, shop), "apartment entry has a wall to the shop");
                Assert.IsTrue(b.Rooms.Exists(r => r.Floor == 1 && Blueprint.KindOf(r.Name) == RoomKind.Bedroom), "apartment above");
            }
            Assert.IsTrue(Blueprints.GetBlueprint("grocery-20000").SprinklersRequired, "20,000 sq ft mercantile is sprinklered");
            // copies in the plan designer keep the party walls and the private entry
            var copy = DesignEditor.CopyOf(HousePlans.Get("storefront-24")).Plan as Blueprint;
            Assert.NotNull(copy);
            Assert.IsTrue(copy.PartyWalls);
            Assert.IsEmpty(BlueprintValidator.Errors(copy), "designer copy passes");
        }

        [UnityTest]
        public IEnumerator DowntownBuildingsCarryTheNewPlans()
        {
            yield return BuildTown();
            var fronts = Town.Lots.Where(l => l.External && l.TypeId == "storefront").ToList();
            var narrow = fronts.Where(l => l.PlanId != null && l.PlanId.StartsWith("storefront-")).ToList();
            Debug.Log($"[MapCom] {fronts.Count} storefronts: {narrow.Count} with a storefront plan, {fronts.Count(l => l.PlanId == null)} without a plan");
            Assert.Greater(narrow.Count, 5, "narrow two-story storefronts have a plan");
            foreach (var l in narrow) Assert.AreEqual(2, MapPlans.ParcelOf(l).Spec.Stories, l.Id + " two-story plan on a two-story building");
            var bank = Town.Lots.First(l => l.TypeId == "bank" && l.District == "downtown");
            Assert.AreEqual("bank-2800", bank.PlanId, "downtown bank");
            var grocery = Town.Lots.First(l => l.TypeId == "grocery");
            Assert.AreEqual("grocery-20000", grocery.PlanId, "grocery");

            var sf = narrow.FirstOrDefault(l => l.PlanId == "storefront-24") ?? narrow[0];
            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            var X = Vector3.Cross(Vector3.up, -sf.Front);
            Shot(sf.FrontMid + sf.Front * 16f + X * 6f + Vector3.up * 1.7f, sf.Local(0f, 3f) + Vector3.up * 4.5f, "mapcom_10_storefront_street");
            Assert.NotNull(BuildingPlansApp.ShowOver(sf, 1), "upper floor blueprint over the storefront");
            Assert.IsTrue(BuildingPlansApp.Footprint(sf, out var c, out var rot, out _, out float roof));
            Shot(c + rot * Vector3.forward * 16f + Vector3.up * (roof + 14f), c + Vector3.up * roof, "mapcom_11_storefront_upper_blueprint");
            BuildingPlansApp.ShowOver(sf, 0);
            Shot(c + rot * Vector3.forward * 16f + Vector3.up * (roof + 14f), c + Vector3.up * roof, "mapcom_12_storefront_shop_blueprint");
            PlanOverlay.Hide();
            Assert.NotNull(BuildingPlansApp.ShowOver(bank, 0));
            Assert.IsTrue(BuildingPlansApp.Footprint(bank, out c, out rot, out _, out roof));
            Shot(c + rot * Vector3.forward * 22f + Vector3.up * (roof + 16f), c + Vector3.up * roof, "mapcom_13_bank_blueprint");
            PlanOverlay.Hide();
        }

        [UnityTest]
        public IEnumerator RidgeRdLotsAreOnOpenGround()
        {
            yield return BuildTown();
            var fb = new FallbackLotProvider();
            var lots = fb.FreeLots().ToList();
            Assert.Greater(lots.Count, 0);
            Assert.IsTrue(MapLayout.Roads.Any(r => r.Name == "Ridge Rd"), "Ridge Rd is a real street");
            foreach (var l in lots)
            {
                var inner = Rect.MinMaxRect(l.Rect.xMin + 0.1f, l.Rect.yMin + 0.1f, l.Rect.xMax - 0.1f, l.Rect.yMax - 0.1f);
                foreach (var a in MapLayout.Areas)
                    if (a.Kind == AreaKind.Woods || a.Kind == AreaKind.Field || a.Kind == AreaKind.Orchard)
                        Assert.IsFalse(a.R.Overlaps(inner), $"{l.Id} overlaps {a.Kind} {a.R}");
                foreach (var t in Town.Lots) Assert.IsFalse(t.Rect.Overlaps(inner), $"{l.Id} overlaps town lot {t.Id}");
            }
            var mid = lots[lots.Count / 2];
            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            Shot(new Vector3(500f, 60f, 330f), new Vector3(500f, 0f, 420f), "mapcom_20_ridge_rd_aerial", 60f, true);
        }

        // ------------------------------------------------------------------ tablet

        [UnityTest]
        public IEnumerator TabletRules()
        {
            yield return Frames(2);
            var t = Tablet.I;
            Assert.NotNull(t);
            t.Close();
            yield return Frames(3);
            // another full-screen UI holds UIOpen (main menu): T does not open the tablet over it
            Game.UIOpen = true;
            t.ToggleFromKey();
            Assert.IsFalse(t.IsOpen, "tablet stays shut over another full-screen UI");
            Game.UIOpen = false;
            t.ToggleFromKey();
            Assert.IsTrue(t.IsOpen, "T opens it normally");
            // the open app is kept by name when the visible app list changes
            t.ShowApp("Bank");
            Assert.AreEqual("Bank", t.CurrentApp);
            Tablet.SetAppVisible("Deliveries", () => false);   // an app listed before Bank disappears
            Assert.AreEqual("Bank", t.CurrentApp, "still on the Bank app");
            Tablet.SetAppVisible("Deliveries", null);
            Assert.AreEqual("Bank", t.CurrentApp);
            Tablet.SetAppVisible("Bank", () => false);         // the open app itself is hidden → Home
            Assert.AreEqual("Home", t.CurrentApp);
            Tablet.SetAppVisible("Bank", null);
            t.ToggleFromKey();
            Assert.IsFalse(t.IsOpen, "T closes it");
            yield return Frames(3);
            Assert.IsFalse(Game.UIOpen);
        }
    }
}
