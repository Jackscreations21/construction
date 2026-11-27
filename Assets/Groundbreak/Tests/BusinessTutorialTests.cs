using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>
    /// Business tutorials (Scripts/BusinessTutorials/): content checks, plus one career played from the van to a sold-out
    /// neighborhood that photographs every step. World pictures are camera renders (work in batchmode); tablet / HUD pictures
    /// need IMGUI, which only runs in a windowed editor. Pictures are written to Logs/shots/tut_pics/ in a normal run and to
    /// Resources/Tutorials/ only when GROUNDBREAK_CAPTURE_TUTORIALS=1 (see Docs/TUTORIALS.md).
    /// The Tutorials app itself is photographed to Logs/shots/tut_business_*.png (windowed run).
    /// </summary>
    public class BusinessTutorialTests
    {
        int _money; GameMode _mode; Vector3 _pos; float _yaw; int _day; float _hours;
        readonly List<GameObject> _built = new List<GameObject>();
        static readonly HashSet<string> Captured = new HashSet<string>();

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static string ShotDir { get { var d = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(d); return d; } }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!Application.isBatchMode) UICaptureTests.SetGameView(1600, 900);
            if (MainMenu.Showing) MainMenu.I.Hide();
            yield return Frames(2);
            _money = Game.Wallet.Money; _mode = GameSettings.Mode;
            if (Game.Player != null) { _pos = Game.Player.transform.position; _yaw = Game.Player.transform.eulerAngles.y; }
            if (GameClock.I != null) { _day = GameClock.I.Day; _hours = GameClock.I.Hours; }
            Weather.ForceRain = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Tablet.I != null && Tablet.I.IsOpen) Tablet.I.Close();
            if (WorkVan.I != null && WorkVan.I.Driving) WorkVan.I.Exit();
            foreach (var g in _built) if (g != null) Object.Destroy(g);
            _built.Clear();
            MainMenu.NewGame(GameMode.FreeBuild);
            WorkVan.Despawn(); Town.Destroy(); LotBroker.ReleaseAll();
            Crew.ResetState(); Projects.ResetState(); Developments.ResetState(); DevVisuals.RefreshNow();
            Company.ResetState(); YardFleetRuntime.ResetAll(); RepairJobs.ResetState();
            CrewManager.ArrivalOverride = null; Weather.ForceRain = null;
            if (NeighborhoodComplete.I != null) { Object.Destroy(NeighborhoodComplete.I.gameObject); NeighborhoodComplete.I = null; Game.UIOpen = false; }
            GameSettings.Mode = _mode; GameSettings.UnlimitedMoney = false;
            Game.Wallet = new Wallet(_money);
            if (GameClock.I != null) { GameClock.I.Day = _day; GameClock.I.Hours = _hours; }
            HandymanStart.MovePlayer(_pos, _yaw);
            if (MainMenu.Showing) MainMenu.I.Hide();
            yield return Frames(3);
        }

        // ------------------------------------------------------------------ capture helpers

        /// <summary>World picture from a camera at midday.</summary>
        static void World(Vector3 cam, Vector3 look, string name, float fov = 60f, bool light = false)
        {
            float h = GameClock.I.Hours; GameClock.I.Hours = 11.5f; Weather.ForceRain = false;
            GameObject lg = null;
            if (light)
            {
                lg = new GameObject("TutLight"); lg.transform.position = cam + Vector3.up * 0.15f;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 6f; l.intensity = 1.6f;
            }
            var c = new GameObject("TutCam").AddComponent<Camera>();
            c.transform.position = cam; c.transform.LookAt(look); c.enabled = false; c.fieldOfView = fov;
            c.nearClipPlane = 0.02f; c.farClipPlane = 1500f;
            var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
            c.targetTexture = rt; c.Render(); c.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(TutorialCapture.OutputPath(name), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt); Object.Destroy(c.gameObject);
            if (lg != null) Object.DestroyImmediate(lg);
            GameClock.I.Hours = h;
            Captured.Add(name);
        }

        /// <summary>The real screen (HUD / tablet). IMGUI only draws in a windowed editor: in batchmode this only checks the
        /// committed picture exists.</summary>
        static IEnumerator Screen(string name, bool toShots = false)
        {
            if (Application.isBatchMode)
            {
                // a batchmode capture run (world pictures only) may come before the first windowed run
                if (!toShots && !TutorialCapture.Enabled) Assert.IsTrue(TutorialCapture.Committed(name), name + ".png missing: run the windowed capture (Docs/TUTORIALS.md)");
                yield break;
            }
            yield return null;
            yield return new WaitForSecondsRealtime(0.25f);
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture(UnityEngine.Screen.width < 1200 ? 2 : 1);
            File.WriteAllBytes(toShots ? Path.Combine(ShotDir, name + ".png") : TutorialCapture.OutputPath(name), tex.EncodeToPNG());
            Debug.Log($"[BizTutorials] captured {name} {tex.width}x{tex.height}");
            Object.Destroy(tex);
            if (!toShots) Captured.Add(name);
        }

        /// <summary>Opens a tablet app (optionally scrolled down) and captures it.</summary>
        static IEnumerator App(string app, string name, float scroll = 0f, System.Action before = null)
        {
            Tablet.I.ShowApp(app);
            before?.Invoke();
            yield return Frames(2);
            Assert.AreEqual(app, Tablet.I.CurrentApp, app + " app missing");
            if (scroll > 0f) typeof(Tablet).GetField("_scroll", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(Tablet.I, new Vector2(0f, scroll));
            yield return Screen(name);
        }

        static void SetPrivate(System.Type t, string field, object value)
        {
            var f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(f, t.Name + "." + field);
            f.SetValue(null, value);
        }

        /// <summary>Stand the player at feet, looking at a point.</summary>
        static void LookFrom(Vector3 feet, Vector3 at)
        {
            Vector3 eye = feet + Vector3.up * 1.6f, d = at - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            HandymanStart.MovePlayer(feet, yaw);
            Game.Player.SetLook(yaw, pitch);
        }

        static IEnumerator NextDay() { GameClock.I.Day++; GameClock.I.Hours = 11.5f; yield return null; yield return null; }
        static void Grant(int dollars, string why) => Game.Wallet.Add(dollars, "Tutorial grant: " + why);

        // ------------------------------------------------------------------ content

        [Test]
        public void C_EveryBusinessTutorialHasStepsAndPictures()
        {
            TutorialPictures.ClearCache();
            var tuts = BusinessTutorials.All();
            Assert.GreaterOrEqual(tuts.Count, 11, "business tutorials registered");
            Assert.Contains(BusinessTutorials.Phase, Tutorials.PhaseOrder);
            Assert.AreEqual(1, Tutorials.PhaseOrder.IndexOf(BusinessTutorials.Phase), "business phase right after Getting started");
            var pics = new HashSet<string>();
            foreach (var t in tuts)
            {
                Assert.IsTrue(t.Available, t.Id);
                Assert.GreaterOrEqual(t.Steps.Count, 3, t.Id + " has steps");
                foreach (var s in t.Steps)
                {
                    Assert.IsNotEmpty(s.Title, t.Id); Assert.IsNotEmpty(s.Text, t.Id + " " + s.Title);
                    Assert.IsNotEmpty(s.Picture, t.Id + " " + s.Title + " has no picture");
                    Assert.IsTrue(pics.Add(s.Picture), "picture used twice: " + s.Picture);
                    var tex = TutorialPictures.Get(s.Picture);
                    Assert.NotNull(tex, $"{t.Id} / {s.Title}: picture {s.Picture} does not load");
                    Assert.Greater(tex.width, 600, s.Picture + " size");
                    if (!string.IsNullOrEmpty(s.OpenApp)) Assert.Contains(s.OpenApp, AppNamesCareer(), s.OpenApp + " is not a tablet app");
                }
            }
        }

        static List<string> AppNamesCareer()
        {
            var mode = GameSettings.Mode;
            GameSettings.Mode = GameMode.Career;
            try { return new List<string>((string[])typeof(Tablet).GetMethod("AppNames", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null)); }
            finally { GameSettings.Mode = mode; }
        }

        // ------------------------------------------------------------------ capture: one career, every step

        [UnityTest, Timeout(1200000)]
        public IEnumerator B_CaptureBusinessCareer()
        {
            Captured.Clear();
            MainMenu.NewGame(GameMode.Career);
            if (MainMenu.Showing) MainMenu.I.Hide();
            GameClock.I.Hours = 10f;
            yield return Frames(4);
            Assert.AreEqual(CompanyStage.Handyman, Company.Stage);
            Assert.NotNull(WorkVan.I);
            string msg;

            // ---------------- 1. handyman: van at home, the Jobs app, drive there, diagnose, fix, invoice
            var van = WorkVan.I.transform;
            LookFrom(van.position - van.forward * 7f + van.right * 4f, van.position + Vector3.up * 1.2f);
            yield return Frames(3);
            yield return Screen("bt_01_van_home");

            TownLot cust = null;
            foreach (var l in Town.Addresses) if (l != Town.Home && l.House != null && !Company.Offers.Exists(j => j.Address == l.Address)) { cust = l; break; }
            Assert.NotNull(cust, "a customer house");
            var repair = RepairJobs.CreateJob(cust, true, (RepairType.SinkLeak, HouseSpot.KitchenSink, SinkLeakDefect.CrackedTrap));
            JobsHub.Tab = 0;
            yield return App("Jobs", "bt_02_jobs_service_calls");
            Tablet.I.Close();
            Assert.IsTrue(Company.Accept(repair, out msg), msg);
            RepairJobs.SetWaypoint(cust);
            WorkVan.I.TeleportTo(cust.Parking, cust.ParkingYaw);
            yield return Frames(3);
            Vector3 p = van.position, f = van.forward, r = van.right;
            World(p + f * 8f + r * 6f + Vector3.up * 2.4f, p + Vector3.up * 1.1f - f * 1f, "bt_03_parked_at_call");

            var leak = RepairJobs.DefectsFor(repair).Find(d => d.Type == RepairType.SinkLeak);
            Assert.NotNull(leak);
            var dt = leak.transform;
            // first person at the sink: the repair panel shows the symptom and the actions
            Vector3 stand = dt.TransformPoint(new Vector3(0f, 0f, 0.95f));
            LookFrom(new Vector3(stand.x, cust.Center.y, stand.z), dt.TransformPoint(new Vector3(0f, 0.3f, 0f)));
            yield return Frames(4);
            Assert.IsTrue(leak.Perform("inspect", RepairContext.Bypass, out msg), msg);
            World(dt.TransformPoint(new Vector3(0.25f, 0.6f, 0.85f)), dt.TransformPoint(new Vector3(0f, 0.38f, -0.3f)), "bt_04_diagnose_leak", 60f, true);
            yield return Screen("bt_05_repair_panel");
            foreach (var a in new[] { "bucket", "trap-off", "trap-new", "inspect", "dry" })
                Assert.IsTrue(leak.Perform(a, RepairContext.Bypass, out msg), a + ": " + msg);
            Assert.IsTrue(leak.IsFixed, "sink fixed");
            World(dt.TransformPoint(new Vector3(0.25f, 0.6f, 0.85f)), dt.TransformPoint(new Vector3(0f, 0.38f, -0.3f)), "bt_06_fixed_leak", 60f, true);
            JobsHub.Tab = 0;
            yield return App("Jobs", "bt_07_invoice");
            Tablet.I.Close();
            Assert.NotNull(RepairJobs.SendInvoice(repair), "invoice");
            Assert.AreEqual(1, Company.JobsCompleted);
            yield return App("Company", "bt_A1_stage_checklist");   // handyman: the next-stage checklist is still open
            Tablet.I.Close();

            // ---------------- 2. the van: cargo, Van app, driving, fast travel
            WorkVan.I.TeleportTo(Town.Home.Parking, Town.Home.ParkingYaw);
            yield return Frames(2);
            p = van.position; f = van.forward; r = van.right;
            VanCargo.Add("2x4x8", 10); VanCargo.Add("ply34", 4); VanCargo.Add("mortar-s80", 10);
            WorkVan.I.SetRearDoors(true, true);
            yield return Frames(2);
            World(p - f * 6.2f - r * 1.3f + Vector3.up * 1.75f, p - f * 1.5f + Vector3.up * 1.15f, "bt_10_van_cargo");
            LookFrom(p - f * 4.2f, p - f * 1.5f + Vector3.up * 0.9f);
            yield return App("Van", "bt_11_van_app");
            Tablet.I.Close();
            WorkVan.I.SetRearDoors(false, true);
            bool chase = WorkVan.I.ChaseCamera; WorkVan.I.ChaseCamera = false;
            WorkVan.I.Enter();
            yield return Frames(4);
            Assert.IsTrue(WorkVan.I.Driving);
            var cam = Game.Player.Cam.transform;
            World(cam.position, cam.position + van.forward * 10f - Vector3.up * 1.0f, "bt_12_driving", 75f);
            yield return App("Van", "bt_13_fast_travel", 260f);
            Tablet.I.Close();
            WorkVan.I.Exit();
            WorkVan.I.ChaseCamera = chase;
            yield return Frames(2);

            // ---------------- 3. hardware store
            var store = Town.Store;
            Assert.NotNull(store, "hardware store lot");
            WorkVan.I.TeleportTo(store.Parking, store.ParkingYaw);
            yield return Frames(3);
            World(store.FrontMid + store.Front * 14f + Vector3.up * 5f - store.Rotation * Vector3.right * 8f, store.Center + Vector3.up * 2f, "bt_20_store_front");
            LookFrom(store.Parking + store.Front * 2f, store.Center + Vector3.up * 2f);
            yield return App("Hardware Store", "bt_21_store_app");
            Tablet.I.Close();
            VanCargo.Add(RepairCatalog.Trap112, 2);
            WorkVan.I.SetRearDoors(true, true);
            p = van.position; f = van.forward; r = van.right;
            World(p - f * 5.4f + r * 2.2f + Vector3.up * 1.6f, p - f * 1.6f + Vector3.up * 1.0f, "bt_22_loaded_at_store");
            WorkVan.I.SetRearDoors(false, true);

            // ---------------- 4. warehouse + truck
            Grant(150000, "months of service calls");
            Company.Reputation = 55;
            LookFrom(Town.Home.Parking + Town.Home.Front * 3f, Town.Home.Center + Vector3.up * 2f);
            yield return App("Warehouse", "bt_30_warehouse_app");
            Tablet.I.Close();
            Assert.IsTrue(Yard.Lease(1, out msg), msg);
            yield return Frames(3);
            Assert.NotNull(YardSite.I);
            World(YardSite.I.CameraSpot, YardSite.I.CameraLook, "bt_31_warehouse_bay", 55f);
            yield return App("Dealership", "bt_32_dealership");
            Tablet.I.Close();
            Assert.NotNull(Fleet.Buy("pickup", true, 0, out msg), msg);
            yield return Frames(3);
            Assert.AreEqual(CompanyStage.Contractor, Company.Stage);
            YardSite.I.RefreshContents();
            yield return Frames(2);
            World(YardSite.I.InteriorCameraSpot, YardSite.I.InteriorCameraLook, "bt_33_inside_bay", 70f);

            // ---------------- 5. contractor job: a fence
            var fence = ContractorJobs.Create("fence", 4242);
            Company.Offers.Add(fence);
            JobsHub.Tab = 1;
            yield return App("Jobs", "bt_40_jobs_contractor");
            Tablet.I.Close();
            var fenceLot = Town.Find(fence.Address);
            Assert.NotNull(fenceLot);
            Assert.IsTrue(Company.Accept(fence, out msg), msg);
            World(fenceLot.FrontMid + fenceLot.Front * 8f + Vector3.up * 6f, fenceLot.Center, "bt_41_empty_lot");
            foreach (var kv in ContractorJobs.Takeoff(fence)) Game.Stock.Add(kv.Key, kv.Value * Mathf.Max(1, Catalog.Get(kv.Key)?.UnitsPerPurchase ?? 1) + 12);
            var pts = ContractorJobs.FenceLine(fence);
            var runs = new List<LandscapeFeature>();
            for (int i = 0; i < 3; i++) { var run = Landscape.LayOut(LandscapeKind.Fence, pts[i], pts[i + 1]); Assert.NotNull(run); _built.Add(run.gameObject); runs.Add(run); }
            runs[0].CompleteAll();
            yield return Frames(2);
            World(fenceLot.FrontMid + fenceLot.Front * 6f + Vector3.up * 4f, fenceLot.Center, "bt_42_fence_going_up");
            for (int i = 1; i < runs.Count; i++) runs[i].CompleteAll();
            ContractorJobs.GateLine(fence, out var ga, out var gb);
            var gate = Landscape.LayOut(LandscapeKind.Gate, ga, gb); Assert.NotNull(gate); _built.Add(gate.gameObject); gate.CompleteAll();
            yield return Frames(3);
            Assert.IsTrue(ContractorJobs.TryComplete(fence, out msg), msg);
            World(fenceLot.FrontMid + fenceLot.Front * 6f + Vector3.up * 9f, fenceLot.Center, "bt_43_fence_done");

            // ---------------- 6. crew: hire, assign, tasks, at work
            Grant(60000, "more contractor work");
            if (Crew.Market.Count < 2) Crew.RefreshMarket(GameClock.I.Day);
            HandymanStart.MovePlayer(YardSite.I.PlayerSpawn, 0f);
            yield return Frames(2);
            yield return App("Crew", "bt_50_crew_hire", 0f, () => SetPrivate(typeof(CrewApp), "_tab", 1));
            var w1 = Crew.HireCandidate(Crew.Market[0], out msg); Assert.NotNull(w1, msg);
            var w2 = Crew.HireCandidate(Crew.Market[0], out msg); Assert.NotNull(w2, msg);
            Assert.AreEqual(CompanyStage.Crew, Company.Stage);
            var cj = ContractorJobs.Create("fence", 777); cj.RequiredVehicleType = "pickup"; Company.Offers.Add(cj);
            Assert.IsTrue(Company.Accept(cj, out msg), msg);
            Crew.Assign(w1, cj.Id); Crew.Assign(w2, cj.Id);
            yield return App("Crew", "bt_51_crew_assign", 0f, () => SetPrivate(typeof(CrewApp), "_tab", 0));
            var cjLot = Town.Find(cj.Address);
            GameClock.I.Hours = 5.5f;
            HandymanStart.MovePlayer(cjLot.Center, 0f);
            yield return new WaitForSeconds(0.7f);
            var task = Crew.AddTask(new StockTask { Ref = "2x4x8", Count = 4, A = cjLot.Parking, B = cjLot.Center });
            yield return App("Crew", "bt_52_crew_tasks", 0f, () => SetPrivate(typeof(CrewApp), "_tab", 2));
            Tablet.I.Close();
            Crew.RemoveTask(task);
            GameClock.I.Hours = 9.5f;
            yield return new WaitForSeconds(1.2f);
            var npc = CrewManager.NpcFor(w1);
            Assert.NotNull(npc, "worker at the job");
            World(cjLot.Parking + cjLot.Front * 3f + Vector3.up * 3f - cjLot.Rotation * Vector3.right * 6f, npc.transform.position + Vector3.up, "bt_53_crew_at_job");
            HandymanStart.MovePlayer(YardSite.I.PlayerSpawn, 0f);
            yield return new WaitForSeconds(0.5f);
            for (int d = 0; d < 60 && !cj.Done; d++) yield return NextDay();
            Assert.IsTrue(cj.Done, "crew finished the job");

            // ---------------- 7. money: Company app, daily costs, loans
            yield return App("Company", "bt_60_company");
            yield return App("Company", "bt_61_daily_costs", 380f);
            yield return App("Company", "bt_63_profit_loss", 640f);
            yield return App("Company", "bt_62_loans", 900f);
            Tablet.I.Close();

            // ---------------- 8. plans
            DevApps.SelectedPlanId = "starter-900";
            yield return App("Plans", "bt_70_plans");
            yield return App("Plans", "bt_71_plan_rooms", 420f);
            yield return App("Plans", "bt_72_takeoff", 1100f);
            Tablet.I.Close();

            // ---------------- 9. builder: a client house
            Grant(900000, "a year of jobs");
            Company.Reputation = 66;
            Assert.IsTrue(Yard.Lease(2, out msg), msg);
            yield return Frames(3);
            Assert.AreEqual(CompanyStage.Builder, Company.Stage);
            var houseOffer = Company.Offers.Find(j => Builder.IsHouseJob(j) && Company.Blocker(j) == null);
            Assert.NotNull(houseOffer, "client house offer");
            JobsHub.Tab = 2;
            yield return App("Jobs", "bt_80_jobs_houses");
            Tablet.I.Close();
            Assert.IsTrue(Company.Accept(houseOffer, out msg), msg);
            var house = Projects.ForJob(houseOffer.Id);
            Assert.NotNull(house);
            Crew.Assign(w1, houseOffer.Id); Crew.Assign(w2, houseOffer.Id);
            for (int d = 0; d < 400 && house.Overall < 0.08f; d++) yield return NextDay();
            DevVisuals.RefreshNow(); yield return null;
            { var dl = house.Lot; World(dl.FrontMid + dl.Front * 9f + Vector3.up * 7f, dl.Center, "bt_81_house_foundation"); }
            bool upShot = false;
            for (int d = 0; d < 400 && !houseOffer.Done; d++)
            {
                yield return NextDay();
                if (!upShot && house.Overall > 0.35f)
                {
                    DevVisuals.RefreshNow(); yield return null; var dl = house.Lot;
                    World(dl.FrontMid + dl.Front * 10f + Vector3.up * 8f, dl.Center + Vector3.up * 2f, "bt_82_house_going_up"); upShot = true;
                }
            }
            Assert.IsTrue(houseOffer.Done, "client house finished: " + house.StatusLabel);
            DevVisuals.RefreshNow(); yield return null;
            { var dl = house.Lot; World(dl.FrontMid + dl.Front * 12f + Vector3.up * 5f + Vector3.Cross(Vector3.up, dl.Front) * 6f, dl.Center + Vector3.up * 3f, "bt_83_house_handover"); }

            // ---------------- 10. developer: land, subdivide, spec houses, sell
            Grant(4000000, "years of houses");
            Company.Reputation = 85;
            Assert.IsTrue(Yard.Lease(3, out msg), msg);
            yield return Frames(3);
            Crew.RefreshMarket(GameClock.I.Day);
            while (Company.Workers.Count < 8 && Crew.Market.Count > 0) Assert.NotNull(Crew.HireCandidate(Crew.Market[0], out msg), msg);
            Assert.AreEqual(CompanyStage.Developer, Company.Stage);
            JobsHub.Tab = 3;
            yield return App("Jobs", "bt_90_jobs_land");
            Tablet.I.Close();
            var land = Company.Offers.Find(j => LandOffers.TractOf(j) != null);
            Assert.NotNull(land, "land listing");
            Assert.IsTrue(Company.Accept(land, out msg), msg);
            var dev = Developments.All.Find(x => x.TractId == LandOffers.TractOf(land));
            Assert.NotNull(dev);
            DevVisuals.RefreshNow(); yield return null;
            World(dev.Tract.FrontMid + dev.Tract.Front * 25f + Vector3.up * 45f, dev.Tract.Center, "bt_91_raw_land");
            Assert.Greater(Developments.Subdivide(dev, 70f, out msg), 0, msg);
            yield return App("Development", "bt_92_development_app");
            Tablet.I.Close();
            Assert.IsTrue(Developments.StartSiteWork(dev, out msg), msg);
            dev.SiteWorkStartDay = Projects.Day - dev.SiteWorkDays;
            foreach (var l in dev.Lots) Assert.NotNull(Developments.StartSpecHouse(dev, l, HousePlans.Get("starter-900"), 0.35f, out msg), msg);
            var specs = new List<HouseProject>(dev.Houses);
            for (int i = 0; i < Company.Workers.Count; i++) Crew.Assign(Company.Workers[i], specs[i % specs.Count].Id);
            for (int d = 0; d < 14; d++) yield return NextDay();
            DevVisuals.RefreshNow(); yield return Frames(2);
            World(dev.Tract.FrontMid + dev.Tract.Front * 25f + Vector3.up * 40f, dev.Tract.Center, "bt_93_spec_houses");
            bool listedShot = false;
            for (int d = 0; d < 400 && !land.Done; d++)
            {
                yield return NextDay();
                int day = GameClock.I.Day;
                foreach (var pr in Projects.All) if (pr.WarrantyJobId != null && !pr.WarrantyDone) Builder.ResolveWarranty(pr, out _);
                foreach (var h in dev.Houses)
                    if (h.Complete && !h.Listed && !h.Sold)
                    {
                        Developments.List(h, Mathf.RoundToInt(Developments.MarketValue(h, day) * 0.97f), out _);
                        if (!listedShot) { listedShot = true; if (NeighborhoodComplete.I != null) { Object.Destroy(NeighborhoodComplete.I.gameObject); NeighborhoodComplete.I = null; Game.UIOpen = false; } yield return App("Development", "bt_94_listing"); Tablet.I.Close(); }
                    }
            }
            Assert.IsTrue(land.Done, "neighborhood sold out");
            if (NeighborhoodComplete.I != null) { Object.Destroy(NeighborhoodComplete.I.gameObject); NeighborhoodComplete.I = null; Game.UIOpen = false; }
            DevVisuals.RefreshNow(); yield return Frames(2);
            World(dev.Tract.FrontMid + dev.Tract.Front * 25f + Vector3.up * 50f, dev.Tract.Center, "bt_95_sold_out");

            // ---------------- 11. reputation + stages
            YardSite.I?.RefreshContents(); yield return Frames(2);
            World(YardSite.I.CameraSpot, YardSite.I.CameraLook, "bt_A0_headquarters", 55f);

            // every business tutorial picture was produced by this run (world) or checked (screen, batchmode)
            if (!Application.isBatchMode)
                foreach (var t in BusinessTutorials.All()) foreach (var s in t.Steps) Assert.Contains(s.Picture, new List<string>(Captured), s.Picture + " not captured");

            // the Tutorials app showing business steps (windowed only)
            yield return TutorialsAppShots();
        }

        static IEnumerator TutorialsAppShots()
        {
            TutorialPictures.ClearCache();
            TutorialApp.Open = null;
            Tablet.I.ShowApp("Tutorials");
            yield return Frames(2);
            Assert.AreEqual("Tutorials", Tablet.I.CurrentApp);
            yield return Screen("tut_business_01_list", true);
            TutorialApp.Show("biz-handyman", 3);
            yield return Screen("tut_business_02_service_call", true);
            TutorialApp.Show("biz-crew", 0);
            yield return Screen("tut_business_03_crew", true);
            TutorialApp.Show("biz-developer", 4);
            yield return Screen("tut_business_04_developer", true);
            Tablet.I.Close();
            if (Application.isBatchMode) Debug.Log("[BizTutorials] batchmode: tut_business_* app screenshots need the windowed run");
        }
    }
}
