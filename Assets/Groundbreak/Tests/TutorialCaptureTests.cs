using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Captures every tutorial picture from real gameplay: each test stages a trade with the real game systems
    /// (same calls as the trade tests) and saves PNGs to Assets/Groundbreak/Resources/Tutorials/. The MEP trades reuse
    /// their own rough-in tests and keep their best shots. Last, it photographs the Tutorials app itself (Logs/shots/tut_app_*).
    /// Tests run in name order (A_, B_ ...).</summary>
    public class TutorialCaptureTests
    {
        static float Ft(float f) => f * Units.Foot;
        static float In(float i) => i * Units.Inch;
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static string PicDir => Path.Combine(Application.dataPath, "Groundbreak", "Resources", "Tutorials");
        static string ShotDir => Path.Combine(Application.dataPath, "..", "Logs", "shots");
        const int W = 1280, H = 720;

        static void Save(Texture2D tex, string dir, string name)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        }

        static Camera Cam(Vector3 pos, Vector3 look, float fov = 60f)
        {
            var cam = new GameObject("TutorialCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look);
            cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.enabled = false;
            return cam;
        }

        /// <summary>Renders a camera to a tutorial picture.</summary>
        static void Pic(Vector3 pos, Vector3 look, string name, float fov = 60f)
        {
            var cam = Cam(pos, look, fov);
            Pic(cam, name);
            Object.Destroy(cam.gameObject);
        }

        static void Pic(Camera cam, string name)
        {
            var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = null;
            Save(tex, PicDir, name);
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        /// <summary>Captures the real screen (player camera + IMGUI HUD / tablet). IMGUI (OnGUI) never runs in batchmode, so these
        /// pictures are captured by one run WITHOUT -batchmode (see Docs/TUTORIALS.md); batchmode runs only check the PNG exists.</summary>
        static IEnumerator GuiPic(Camera cam, string name, bool hud, bool tablet, bool toShots = false)
        {
            string dir = toShots ? ShotDir : PicDir;
            if (Application.isBatchMode)
            {
                Debug.Log("[Tutorials] batchmode: IMGUI picture not recaptured: " + name);
                if (!toShots) Assert.IsTrue(File.Exists(Path.Combine(dir, name + ".png")), name + ".png missing: run TutorialCaptureTests once without -batchmode");
                yield break;
            }
            yield return null;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture(Screen.width < 1200 ? 2 : 1);
            Debug.Log($"[Tutorials] captured {name} {tex.width}x{tex.height}");
            Save(tex, dir, name);
            Object.Destroy(tex);
        }

        static void CopyShot(string shot, string pic)
        {
            string src = Path.Combine(ShotDir, shot + ".png");
            Assert.IsTrue(File.Exists(src), "trade test shot missing: " + shot);
            Directory.CreateDirectory(PicDir);
            File.Copy(src, Path.Combine(PicDir, pic + ".png"), true);
        }

        static int Slot(string id)
        {
            for (int i = 0; i < ToolBelt.SlotCount; i++) if (Game.Belt.Slots[i] != null && Game.Belt.Slots[i].Id == id) return i;
            return -1;
        }

        static void PutPlayer(Vector3 feet, float yaw, float pitch)
        {
            var cc = Game.Player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            Game.Player.transform.position = feet;
            if (cc != null) cc.enabled = true;
            Game.Player.SetLook(yaw, pitch);
        }

        readonly List<GameObject> _cleanup = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!Application.isBatchMode)
            {
                // editor Game view: render at 1600x900 so tablet/HUD captures are sharp (PlayModeWindow is editor-only, use reflection)
                var pmw = System.Type.GetType("UnityEditor.PlayModeWindow, UnityEditor");
                var m = pmw?.GetMethod("SetCustomRenderingResolution", BindingFlags.Public | BindingFlags.Static);
                try { m?.Invoke(null, new object[] { 1600u, 900u, "Tutorials" }); } catch (System.Exception e) { Debug.Log("[Tutorials] resolution: " + e.Message); }
            }
            yield return Frames(3);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            FramingJob.RequireStock = true;
            FramingJobRunner.Instant = false;
            foreach (var g in _cleanup) if (g != null) Object.Destroy(g);
            _cleanup.Clear();
            if (Tablet.I != null && Tablet.I.IsOpen) Tablet.I.Close();
            yield return Frames(3);
        }

        // ================================================================== getting started

        [UnityTest, Timeout(600000)]
        public IEnumerator A_GettingStarted()
        {
            yield return Frames(10);
            Debug.Log($"[Tutorials] screen {Screen.width}x{Screen.height}");
            Assert.NotNull(Game.Player); Assert.NotNull(Game.Belt); Assert.NotNull(Tablet.I);
            GameClock.I.Hours = 10f;
            Game.Wallet.Add(20000, "Tutorial float");

            // first view: standing at the start looking over the material stacks
            PutPlayer(new Vector3(2f, 0f, -6f), 20f, 8f);
            int hammer = Slot("hammer");
            Game.Belt.Select(hammer >= 0 ? hammer : 0);
            yield return Frames(4);
            yield return GuiPic(Game.Player.Cam, "gs_01_first_view", true, false);
            Game.Belt.Select(Slot("place"));
            yield return Frames(3);
            yield return GuiPic(Game.Player.Cam, "gs_02_tool_belt", true, false);

            // place tool ghost, then a run at 16" O.C.
            var start = new Vector3(-2f, 0f, -12f);
            PutPlayer(start, 0f, 45f);
            Game.Snap.GridIndex = 5; // 16" O.C.
            yield return Frames(4);
            Assert.IsTrue(Game.Placement.Active, "place tool not active");
            yield return GuiPic(Game.Player.Cam, "gs_03_place_ghost", true, false);
            var before = new HashSet<Piece>(Object.FindObjectsByType<Piece>(FindObjectsSortMode.None));
            Assert.IsTrue(Game.Placement.Place(), "Place failed: " + Game.Placement.StatusLine);
            for (int i = 0; i < 4; i++) { Game.Placement.Repeat(); yield return Frames(2); }
            yield return Frames(2);
            yield return GuiPic(Game.Player.Cam, "gs_04_repeat_16oc", true, false);
            yield return GuiPic(Game.Player.Cam, "gs_06_snapping", true, false);

            // hammer the aimed board
            Game.Belt.Select(Slot("hammer"));
            yield return Frames(2);
            Game.Player.TryAim(out var hit, 6f);
            var target = hit.collider != null ? hit.collider.GetComponent<Piece>() : null;
            Assert.NotNull(target, "not aiming at a board");
            for (int i = 0; i < 12 && target.State != PieceState.Fastened; i++) { Game.Belt.Active.Primary(); yield return new WaitForSeconds(0.4f); }
            Assert.AreEqual(PieceState.Fastened, target.State);
            yield return GuiPic(Game.Player.Cam, "gs_05_nailed", true, false);
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (!before.Contains(p)) _cleanup.Add(p.gameObject);
            Game.Snap.GridIndex = 3;

            // tablet: supply store with a cart, tool shop
            PutPlayer(new Vector3(2f, 0f, -6f), 20f, 8f);
            Tablet.I.ShowApp("Supply Store");
            yield return Frames(3);
            Assert.AreEqual("Supply Store", Tablet.I.CurrentApp);
            yield return GuiPic(Game.Player.Cam, "gs_07_tablet_store", false, true);
            Tablet.I.ShowApp("Tool Shop");
            yield return Frames(3);
            yield return GuiPic(Game.Player.Cam, "gs_09_tool_shop", false, true);
            Tablet.I.Close();
            yield return Frames(3);

            // rush delivery: photograph the truck at the drop point
            DeliveryService.I.Order(new Dictionary<string, int> { { "2x4x8", 10 } }, true);
            Vector3 drop = DeliveryService.I.DropPoint;
            float until = Time.time + 60f; bool shot = false;
            while (Time.time < until && DeliveryService.I.Pending.Count > 0)
            {
                var dt = GameObject.Find("DeliveryTruck");
                if (!shot && dt != null && (dt.transform.position - drop).magnitude < 4f)
                {
                    Vector3 tp = dt.transform.position;
                    Pic(tp + dt.transform.right * 7f - dt.transform.forward * 4f + Vector3.up * 3f, tp + Vector3.up * 1.2f, "gs_08_delivery_truck", 55f);
                    shot = true;
                }
                yield return null;
            }
            Assert.IsTrue(shot, "delivery truck never arrived");
        }

        // ================================================================== site & concrete

        [UnityTest, Timeout(900000)]
        public IEnumerator B_SiteAndConcrete()
        {
            yield return Frames(5);
            Assert.NotNull(SiteGround.I, "SiteGround not booted");
            GameClock.I.Hours = 7.5f;
            Game.Snap.GridIndex = 3;
            Game.Wallet.Add(20000, "Tutorial float");
            PutPlayer(new Vector3(6f, 0f, -12f), 0f, 0f);

            // a 16' x 12' slab on the left side of the pad, away from the concrete test's slab
            Vector3 min = new Vector3(-8.2f, 0f, 10.4f), max = min + new Vector3(Ft(16f), 0f, Ft(12f));
            Vector3 center = (min + max) * 0.5f;

            int a = SiteLayout.AddStake(new Vector3(min.x, 0, min.z)), b = SiteLayout.AddStake(new Vector3(max.x, 0, min.z));
            int c = SiteLayout.AddStake(new Vector3(max.x, 0, max.z)), d = SiteLayout.AddStake(new Vector3(min.x, 0, max.z));
            SiteLayout.AddLine(a, b); SiteLayout.AddLine(b, c); SiteLayout.AddLine(c, d); SiteLayout.AddLine(d, a);
            Vector3 laser = new Vector3(max.x + Ft(4f), 0f, min.z - Ft(3f));
            SiteLayout.SetLaser(laser);
            SiteLayout.SetSlabTop(In(4f));
            for (float x = min.x; x <= max.x; x += 0.15f)
            {
                SiteLayout.SprayMark(new Vector3(x, 0, min.z - In(14f)), Vector3.up, new Color(0.95f, 0.95f, 0.92f));
                SiteLayout.SprayMark(new Vector3(x, 0, max.z + In(14f)), Vector3.up, new Color(0.95f, 0.95f, 0.92f));
            }
            yield return Frames(2);
            Pic(new Vector3(center.x + 0.5f, 3.4f, min.z - 4.2f), center, "c_01_layout");
            Pic(new Vector3(center.x, 9f, center.z - 0.01f), center, "c_02_square", 50f);
            Pic(laser + new Vector3(1.6f, 1.6f, -2.4f), laser + new Vector3(-1.2f, 0.7f, 1.2f), "c_03_laser", 55f);

            var g = SiteGround.I;
            g.DigRect(min, max, -In(4f));
            float tr = Ft(1f);
            g.DigRect(min, new Vector3(max.x, 0, min.z + tr), -In(12f));
            g.DigRect(new Vector3(min.x, 0, max.z - tr), max, -In(12f));
            g.DigRect(min, new Vector3(min.x + tr, 0, max.z), -In(12f));
            g.DigRect(new Vector3(max.x - tr, 0, min.z), max, -In(12f));
            g.Rebuild();
            yield return Frames(2);
            Pic(new Vector3(center.x + 0.5f, 3.0f, min.z - 3.5f), center, "c_04_excavated");
            Game.Stock.Add(SiteGround.GravelId, 27 * 3);
            GravelBase.SpreadRect(new Vector3(min.x + tr, 0, min.z + tr), new Vector3(max.x - tr, 0, max.z - tr), 0f);
            for (float x = min.x; x <= max.x; x += 0.25f) for (float z = min.z; z <= max.z; z += 0.25f) g.Compact(new Vector3(x, 0, z), 0.28f);
            g.Rebuild();
            yield return Frames(2);
            Pic(new Vector3(center.x + 0.5f, 3.0f, min.z - 3.5f), center, "c_05_gravel");

            foreach (var fid in new[] { "2x6x16", "2x8x16", "2x10x16", "2x12x16" }) if (Catalog.Get(fid) != null) Game.Stock.Add(fid, 8);
            Game.Stock.Add(FormSystem.StakeId, 80); Game.Stock.Add("2x4x8", 4);
            Game.Stock.Add(FormSystem.FormOilId, 2000); Game.Stock.Add(FormSystem.PolyId, 2000);
            string msg = FormSystem.BuildRectangle(min, max);
            var o = FormSystem.Active;
            Assert.NotNull(o, "forms not closed: " + msg);
            yield return Frames(2);
            Pic(new Vector3(min.x - 1.5f, 2.4f, min.z - 2.8f), center, "c_06_forms");
            FormSystem.AutoStake(o);
            foreach (var r in o.Runs) for (float t = Ft(2f); t < r.Length; t += Ft(4f)) FormSystem.AddBrace(r, t, o);
            FormSystem.Oil(o);
            FormSystem.LayPoly(o);
            yield return Frames(2);
            Pic(new Vector3(min.x + 1.6f, 0.9f, min.z - 1.4f), new Vector3(min.x + 0.9f, 0.05f, min.z + 0.4f), "c_07_forms_closeup", 55f);

            Game.Stock.Add("rebar-4", 24); Game.Stock.Add(RebarSystem.ChairId, 300); Game.Stock.Add("tiewire", 2);
            RebarSystem.AutoGrid(o, "rebar-4");
            RebarSystem.TieAllNear(center, 20f);
            yield return Frames(2);
            Pic(new Vector3(min.x - 1.2f, 2.6f, min.z - 2.2f), center, "c_08_rebar");
            Pic(new Vector3(min.x + 1.0f, 0.75f, min.z - 0.4f), new Vector3(min.x + 1.6f, -0.1f, min.z + 0.9f), "c_09_rebar_closeup", 55f);

            // dispatch app with the takeoff for these forms
            Tablet.I.ShowApp("Concrete Dispatch");
            yield return Frames(3);
            PutPlayer(new Vector3(center.x, 0f, min.z - 3f), 0f, 20f);
            yield return Frames(2);
            yield return GuiPic(Game.Player.Cam, "c_10_dispatch", false, true);
            Tablet.I.Close();
            yield return Frames(3);
            PutPlayer(new Vector3(6f, 0f, -12f), 0f, 0f);

            var ord = new ConcreteOrder { Yards = FormSystem.RecommendedOrder(o), Psi = 3000, SlumpIn = 5 };
            Assert.IsNotNull(ConcreteDispatch.Place(ord, ConcreteDispatch.LeadMinutes, out msg), msg);
            ConcreteTime.Skip(ConcreteDispatch.LeadMinutes - 2f);
            float until = Time.time + 60f; bool shot = false;
            while (ord.State != OrderState.OnSite && Time.time < until)
            {
                if (false) { }
                yield return null;
            }
            Assert.AreEqual(OrderState.OnSite, ord.State, "truck never arrived");
            var truck = ord.Truck;
            Vector3 toTruck = truck.ChuteBase - center; toTruck.y = 0f; toTruck.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, toTruck);
            Vector3 farCam = center - toTruck * 4.5f + side * 2.5f + Vector3.up * 3.2f;
            Pic(farCam, (center + truck.transform.position) * 0.5f + Vector3.up * 0.8f, "c_11_truck", 60f);

            var pour = PourSystem.Begin(o);
            until = Time.time + 150f; int frame = 0; bool midShot = false;
            while (!pour.Full && ord.YardsLeft > 0.001f && Time.time < until)
            {
                Vector3 p = pour.LowestPoint(truck.ChuteBase, ReadyMixTruck.ChuteReach - 0.3f);
                bool roomHere = o.TopY - SiteGround.GroundY(p) > 0 && (CTool.XZ(p - truck.ChuteBase).magnitude <= ReadyMixTruck.ChuteReach - 0.3f);
                Vector3 far = pour.LowestPoint(center);
                float roomNear = pour.TopY - pour.SurfaceY(p), roomFar = pour.TopY - pour.SurfaceY(far);
                if (roomHere && roomNear > In(0.75f) || roomFar <= In(0.75f)) { truck.AimChute(p); pour.Discharge(truck, p, Time.deltaTime * 2f); }
                else if (frame % 6 == 0) { float y = truck.Discharge(ChuteTool.BarrowCubicFeet / 27f); pour.AddConcrete(far, y / FormSystem.YardsPerM3, ord); }
                if (frame % 5 == 0) pour.Apply(FinishOp.Vibrate, p);
                if (!midShot && pour.FillFraction > 0.5f)
                {
                    Pic(farCam, (center + truck.ChuteBase) * 0.5f, "c_12_pouring", 55f);
                    midShot = true;
                }
                frame++;
                yield return null;
            }
            Assert.IsTrue(pour.Full || pour.FillFraction > 0.97f, "forms not filled");
            foreach (var p in pour.WorkPoints(0.3f, true)) pour.Apply(FinishOp.Vibrate, p);
            foreach (var p in pour.WorkPoints(0.4f)) { pour.Apply(FinishOp.Vibrate, p); pour.Apply(FinishOp.Rake, p); }
            for (int pass = 0; pass < 2; pass++) foreach (var p in pour.WorkPoints(0.5f)) pour.Apply(FinishOp.Screed, p);
            foreach (var p in pour.WorkPoints(0.5f)) pour.Apply(FinishOp.BullFloat, p);
            Game.Stock.Add(PourSystem.AnchorId, 25);
            pour.AutoAnchors();
            ConcreteDispatch.Release(ord);
            yield return Frames(3);
            Pic(center - toTruck * 3.8f - side * 2.2f + Vector3.up * 2.4f, center, "c_13_screeded", 60f);

            ConcreteTime.Skip(PourSystem.BleedEndMinutes + 5f);
            foreach (var p in pour.WorkPoints(0.2f, true)) pour.Apply(FinishOp.Edge, p);
            pour.Groove(new Vector3(center.x, 0, min.z + Ft(1f)), new Vector3(center.x, 0, max.z - Ft(1f)));
            pour.Groove(new Vector3(min.x + Ft(1f), 0, center.z), new Vector3(max.x - Ft(1f), 0, center.z));
            foreach (var p in pour.WorkPoints(0.35f)) pour.Apply(FinishOp.Broom, p);
            until = Time.time + 5f;
            while (pour.Element == null && Time.time < until) yield return null;
            Assert.NotNull(pour.Element, "pour never completed");
            yield return Frames(3);
            Pic(new Vector3(min.x - 0.8f, 2.2f, min.z - 2.2f), center, "c_14_finished");

            ConcreteTime.Skip(7 * 1440f);
            string strip = FormSystem.Strip(o);
            Assert.IsTrue(o.Stripped, strip);
            yield return Frames(5);
            Pic(new Vector3(min.x - 1.4f, 2.4f, min.z - 2.6f), center, "c_15_cured");
        }

        // ================================================================== framing, roof, inspection

        [UnityTest, Timeout(900000)]
        public IEnumerator C_FramingAndRoof()
        {
            yield return Frames(5);
            FramingJob.RequireStock = false;
            FramingJobRunner.Instant = true;
            GameClock.I.Hours = 11f;
            PutPlayer(new Vector3(-10f, 0f, -10f), 0f, 0f);

            var origin = new Vector3(28f, 0f, 28f);
            Game.Snap.Origin = origin; Game.Snap.OriginYaw = 0f;
            var fp = Footprint.Make(origin, 0f, 16, 12);
            var p = ShellProject.Start(fp);
            p.Openings[0].Add(new WallOpening { Product = Catalog.Get("door-ext-steel36"), CenterIn = 60f });
            p.Openings[0].Add(new WallOpening { Product = Catalog.Get("win-sh-3648"), CenterIn = 138f });
            p.Openings[2].Add(new WallOpening { Product = Catalog.Get("win-sh-3648"), CenterIn = 96f });
            p.Openings[1].Add(new WallOpening { Product = Catalog.Get("win-sh-2436"), CenterIn = 68.5f });
            Vector3 mid = fp.World(fp.Length / 2, 1.2f, fp.Width / 2);
            var overview = Cam(fp.World(-2.0f, 3.0f, -2.6f), fp.World(fp.Length * 0.55f, 0.4f, fp.Width * 0.5f), 55f);
            _cleanup.Add(overview.gameObject);

            var floor = p.StartFloor();
            Tablet.I.ShowApp("Framing Plans");
            PutPlayer(origin + new Vector3(-3f, 0f, -4f), 35f, 15f);
            yield return Frames(3);
            yield return GuiPic(Game.Player.Cam, "f_00_plans", false, true);
            Tablet.I.Close();
            yield return Frames(3);
            PutPlayer(new Vector3(-10f, 0f, -10f), 0f, 0f);

            // sills + rim first, then joists, then the deck
            int guard = 0;
            while (!floor.Done && guard++ < 400 && !floor.Current.Label.ToLower().Contains("joist")) floor.TryAdvance(null, out _);
            yield return Frames(2);
            Pic(fp.World(-1.2f, 1.6f, -1.6f), fp.World(1.6f, 0.1f, 1.4f), "f_01_sills", 55f);
            int half = floor.Steps.Count / 2;
            while (!floor.Done && floor.Next < half) floor.TryAdvance(null, out _);
            yield return Frames(2);
            Pic(overview, "f_02_joists");
            floor.RunAll();
            yield return Frames(2);
            Pic(overview, "f_03_deck");

            var front = p.StartWall(0);
            guard = 0;
            while (!front.Done && guard++ < 400 && !front.Current.Label.StartsWith("Square")) front.TryAdvance(null, out _);
            yield return Frames(2);
            Pic(fp.World(1.2f, 1.1f, 0.5f), fp.World(1.4f, fp.DeckTopY, 0.0f), "f_04_layout_marks", 55f);
            Pic(fp.World(1.5f, 3.2f, -1.6f), fp.World(2.6f, fp.DeckTopY, 1.3f), "f_05_wall_flat");
            front.RunAll();
            p.StartWall(1).RunAll();
            yield return Frames(2);
            Pic(overview, "f_06_walls_standing");
            p.StartWall(2).RunAll(); p.StartWall(3).RunAll();
            p.StartCaps().RunAll();
            yield return Frames(2);
            Pic(fp.World(-1.4f, 3.4f, -1.2f), fp.World(1.0f, 2.4f, 1.0f), "f_07_caps", 55f);
            Pic(fp.World(fp.Length * 0.25f, 1.7f, -2.2f), fp.World(fp.Length * 0.3f, 1.3f, 0f), "f_08_openings");

            p.StartSheathing().RunAll();
            yield return Frames(2);
            Pic(overview, "f_09_sheathed");
            foreach (var j in p.FlashOpenings()) j.RunAll();
            foreach (var ro in new List<RoughOpening>(RoughOpening.All))
            {
                if (ro == null || ro.Installed != null || !ro.Flashed) continue;
                var def = ro.Spec.Product;
                var piece = Piece.Create(def, 0f, ro.Center + ro.Outward * 1f, Quaternion.identity);
                RoughOpening.OnPlaced(piece);
            }
            foreach (var j in new List<FramingJob>(FramingJob.All)) if (j != null && !j.Done && j.Title.StartsWith("Install")) j.RunAll();
            yield return Frames(2);
            Pic(fp.World(fp.Length * 0.4f, 1.6f, -3.6f), fp.World(fp.Length * 0.4f, 1.5f, 0f), "f_10_units", 55f);

            // inspection flags: a stud floating in the room is flagged red
            var floater = Framer.Make("2x4x925", Units.In(92.625f), null, fp.World(fp.Length * 0.5f, fp.DeckTopY + 1.6f, fp.Width * 0.5f), Vector3.up, Vector3.right, MemberRole.Stud);
            yield return null;
            FramingInspection.Run();
            var markers = Object.FindFirstObjectByType<InspectionMarkers>();
            Assert.NotNull(markers, "InspectionMarkers missing");
            markers.Refresh();
            yield return Frames(2);
            Assert.Greater(FramingInspection.Last.Count, 0, "floating stud not flagged");
            Vector3 fl = FramingInspection.Last[0].Where;
            float bestD = float.MaxValue;
            foreach (var iss in FramingInspection.Last) { float dd = Vector3.Distance(iss.Where, floater.transform.position); if (dd < bestD) { bestD = dd; fl = iss.Where; } }
            Pic(fl + new Vector3(-0.9f, 0.7f, -1.1f), fl + new Vector3(0f, 0.15f, 0f), "i_01_framing_flags", 55f);
            Object.Destroy(floater.gameObject);
            yield return null;
            FramingInspection.Run(); markers.Refresh();

            // roof: plans, boom truck, trusses, deck, jacks, dry-in, shingles
            Tablet.I.ShowApp("Framing Plans");
            PutPlayer(origin + new Vector3(-3f, 0f, -4f), 35f, 25f);
            yield return Frames(3);
            yield return GuiPic(Game.Player.Cam, "r_00_plans", false, true);
            Tablet.I.Close();
            yield return Frames(3);
            PutPlayer(new Vector3(-10f, 0f, -10f), 0f, 0f);
            Assert.NotNull(ShellProject.RoofFramingHook, "roofing module missing");
            p.PitchRise = 6;
            var roof = p.StartRoof();
            Vector3 c = fp.World(fp.Length / 2, 2.5f, fp.Width / 2);
            for (int i = 0; i < 6 && !roof.Done; i++) roof.TryAdvance(null, out _);
            yield return new WaitForSeconds(4f);
            roof.TryAdvance(null, out _);
            yield return new WaitForSeconds(1.1f);
            Pic(c + new Vector3(-9f, 5.5f, -12f), c + new Vector3(0, 0.5f, -2f), "r_01_boom_truck");
            roof.RunAll();
            BoomTruck.RemoveAll();
            yield return Frames(3);
            Pic(c + new Vector3(-5.5f, 3.2f, -5.5f), c + new Vector3(0, 0.6f, 0), "r_02_trusses");
            var info = RoofFraming.InfoOf(roof);
            Assert.NotNull(info, "no RoofInfo");
            p.StartRoofSheathing().RunAll();
            yield return Frames(2);
            Pic(c + new Vector3(-5.5f, 3.8f, -6f), c + new Vector3(0, 0.8f, 0), "r_04_deck");
            var jacks = Roofing.RoofJacks(info, 0, 1.2f, 1f, 4f);
            jacks.RunAll();
            yield return Frames(2);
            Pic(c + new Vector3(-4.5f, 4.6f, -6.5f), c + new Vector3(-0.5f, 1.3f, -1.2f), "r_05_jacks", 50f);
            foreach (var jk in new List<RoofJack>(RoofJack.All)) if (jk != null) Object.Destroy(jk.gameObject);
            yield return null;
            p.StartDryIn().RunAll();
            yield return Frames(2);
            Pic(c + new Vector3(-5.5f, 3.8f, -6f), c + new Vector3(0, 0.8f, 0), "r_06_dry_in");
            var sg = p.StartShingles();
            guard = 0;
            while (!sg.Done && guard++ < 300 && !sg.Current.Label.StartsWith("Shingle course 9/")) sg.TryAdvance(null, out _);
            yield return Frames(2);
            Pic(fp.World(1.4f, 4.2f, -1.6f), fp.World(2.2f, 3.1f, 0.6f), "r_07_courses");
            sg.RunAll();
            yield return Frames(3);
            Pic(c + new Vector3(-6f, 3f, -7.5f), c + new Vector3(0, 0.9f, 0), "r_08_shingled");

            // stick-framed hip on its own stand-in walls
            var fp2 = Footprint.Make(new Vector3(28f, 0f, 60f), 0f, 20f, 14f);
            _cleanup.Add(StandIn(fp2).gameObject);
            var hip = RoofFraming.StickFramed(fp2, 8, true);
            hip.RunAll();
            yield return Frames(3);
            Vector3 c2 = new Vector3(28f + fp2.Length / 2, 3f, 60f + fp2.Width / 2);
            Pic(c2 + new Vector3(-6.5f, 4.5f, -7.5f), c2 + new Vector3(0, 0.6f, 0), "r_03_stick_framed");
        }

        /// <summary>Stand-in shell for the stick-framed roof (same as RoofingTests.StandIn).</summary>
        static Transform StandIn(Footprint fp)
        {
            var root = new GameObject("TutorialStandIn").transform;
            root.SetPositionAndRotation(new Vector3(fp.Origin.x, 0f, fp.Origin.z), fp.Rotation);
            float L = fp.Length, Wd = fp.Width, P = fp.PlateTopY, wd = fp.WallDepth, deck = fp.DeckTopY;
            float wallTop = P - In(3f);
            Framer.AttachMesh(root, "Walls", (MeshBuilder b, System.Func<SurfaceKind, int> s) =>
            {
                int osb = s(SurfaceKind.Osb), conc = s(SurfaceKind.Concrete);
                b.AddBox(new Vector3(L / 2, deck / 2 - 0.05f, Wd / 2), new Vector3(L + 0.02f, deck + 0.1f, Wd + 0.02f), conc);
                float h = wallTop - deck, yc = deck + h / 2, sh = In(0.4375f);
                b.AddBox(new Vector3(L / 2, yc, wd / 2 - sh / 2), new Vector3(L, h, wd + sh), osb);
                b.AddBox(new Vector3(L / 2, yc, Wd - wd / 2 + sh / 2), new Vector3(L, h, wd + sh), osb);
                b.AddBox(new Vector3(wd / 2 - sh / 2, yc, Wd / 2), new Vector3(wd + sh, h, Wd - 2 * wd), osb);
                b.AddBox(new Vector3(L - wd / 2 + sh / 2, yc, Wd / 2), new Vector3(wd + sh, h, Wd - 2 * wd), osb);
            });
            float t = In(1.5f);
            string longId = RoofStock.Pick("2x4", L).id, endId = RoofStock.Pick("2x4", Wd - 2 * wd).id;
            foreach (var (y, role) in new[] { (P - t * 1.5f, MemberRole.TopPlate), (P - t * 0.5f, MemberRole.CapPlate) })
            {
                Framer.Make(longId, L, root, new Vector3(L / 2, y, wd / 2), Vector3.right, Vector3.up, role);
                Framer.Make(longId, L, root, new Vector3(L / 2, y, Wd - wd / 2), Vector3.right, Vector3.up, role);
                Framer.Make(endId, Wd - 2 * wd, root, new Vector3(wd / 2, y, Wd / 2), Vector3.forward, Vector3.up, role);
                Framer.Make(endId, Wd - 2 * wd, root, new Vector3(L - wd / 2, y, Wd / 2), Vector3.forward, Vector3.up, role);
            }
            return root;
        }

        // ================================================================== stairs

        [UnityTest, Timeout(600000)]
        public IEnumerator D_Stairs()
        {
            yield return Frames(5);
            FramingJob.RequireStock = false;
            GameClock.I.Hours = 11f;
            float Hh = In(48f);

            // calculator app with a 48" rise
            StairCalculator.UseForLayout(StairMath.Calculate(48f));
            Tablet.I.ShowApp("Stair Calculator");
            PutPlayer(new Vector3(-24f, 0f, 20f), 0f, 10f);
            yield return Frames(3);
            yield return GuiPic(Game.Player.Cam, "s_01_calculator", false, true);
            Tablet.I.Close();
            yield return Frames(3);
            PutPlayer(new Vector3(-10f, 0f, -10f), 0f, 0f);

            // framing square layout and cut on a loose PT 2x12
            var fsq = new FramingSquare();
            var pos = new Vector3(-26f, In(0.75f), 24f);
            var board = Piece.Create(Catalog.Get("pt2x12x12"), 0f, pos, Quaternion.Euler(0f, 90f, 0f));
            yield return null;
            Assert.IsTrue(fsq.LayOut(board, out string msg), msg);
            yield return null;
            Pic(pos + new Vector3(-0.3f, 0.75f, -0.7f), pos + new Vector3(-0.3f, 0f, 0f), "s_02_layout", 50f);
            if (Game.Belt.Owned.Find(t => t.Id == "saw") == null) Game.Belt.AddTool(ToolFactory.Create("saw"));
            Assert.IsTrue(fsq.Cut(board, out var stringer, out msg), msg);
            yield return null;
            Pic(pos + new Vector3(0.2f, 0.7f, -0.8f), pos + new Vector3(0f, 0f, 0f), "s_03_cut", 50f);
            _cleanup.Add(stringer.gameObject);

            var center = new Vector3(-24f, 0f, 28f);
            var landing = StairBuilder.Landing(center, 0f, In(48f), Hh);
            landing.RunAll();
            var linfo = landing.GetComponent<StairLanding>();
            var bottom = StairBuilder.BottomFor(linfo.FrontFaceGround, 0f, Hh);
            var stairs = StairBuilder.Stairs(bottom, 0f, Hh, In(36f), linfo.FrontBeam.transform);
            stairs.RunAll();
            yield return Frames(3);
            Vector3 m = bottom + new Vector3(0f, 0.6f, 1.0f);
            Pic(m + new Vector3(-2.5f, 1.6f, -2.1f), m + new Vector3(0f, -0.1f, 0.4f), "s_04_stairs");
            _cleanup.Add(stairs.gameObject); _cleanup.Add(landing.gameObject);
        }

        // ================================================================== MEP: reuse the trade rough-in tests and keep their shots

        /// <summary>True when a trade test already took this shot earlier in this Unity session (full suite run): reuse it
        /// instead of running that test twice (the trade tests expect a fresh session).</summary>
        static bool FreshShot(string shot)
        {
            string f = Path.Combine(ShotDir, shot + ".png");
            if (!File.Exists(f)) return false;
            var sessionStart = System.DateTime.Now.AddSeconds(-Time.realtimeSinceStartup);
            return File.GetLastWriteTime(f) >= sessionStart;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator E_Mep()
        {
            yield return Frames(5);
            yield return MepTestScene.Clear();
            if (!FreshShot("mep_plumbing_11_overbored_marker")) yield return new PlumbingTests().PlumbBathroom();
            CopyShot("mep_plumbing_04_bored_studs", "p_01_bored_studs");
            CopyShot("mep_plumbing_03_manifolds_crimp", "p_02_manifolds");
            CopyShot("mep_plumbing_05_stack_wye_primer", "p_03_stack_wye");
            CopyShot("mep_plumbing_06_toilet_drain", "p_04_toilet_drain");
            CopyShot("mep_plumbing_09_roof_vents", "p_05_roof_vents");
            CopyShot("mep_plumbing_10_gauge", "p_06_gauge");
            CopyShot("mep_plumbing_11_overbored_marker", "i_02_mep_markers");
            yield return MepTestScene.Clear();

            if (!FreshShot("mep_electrical_06_open_ground_marker")) yield return new ElectricalTests().WireRoom();
            CopyShot("mep_electrical_01_wired_wall", "e_01_wired_wall");
            CopyShot("mep_electrical_02_box_madeup", "e_02_box_madeup");
            CopyShot("mep_electrical_03_panel", "e_03_panel");
            CopyShot("mep_electrical_05_room_lit", "e_04_room_lit");
            CopyShot("mep_electrical_06_open_ground_marker", "e_05_marker");
            yield return MepTestScene.Clear();

            if (!FreshShot("mep_hvac_07_bathfan_cap")) yield return new HvacTests().DuctRoom();
            CopyShot("mep_hvac_06_furnace", "h_01_furnace");
            CopyShot("mep_hvac_02_closet_attic", "h_02_trunk");
            CopyShot("mep_hvac_03_takeoff_boot", "h_03_takeoff_boot");
            CopyShot("mep_hvac_04_registers_below", "h_04_registers");
            CopyShot("mep_hvac_05_condenser_lineset", "h_05_condenser");
            CopyShot("mep_hvac_07_bathfan_cap", "h_06_bathfan");
            yield return MepTestScene.Clear();
        }

        // ================================================================== the app itself

        [UnityTest, Timeout(300000)]
        public IEnumerator Z_TutorialsApp()
        {
            yield return Frames(5);
            TutorialPictures.ClearCache();

            // every picture for the trades on main exists
            var missing = new List<string>();
            foreach (var n in Tutorials.PictureNames(true)) if (!File.Exists(Path.Combine(PicDir, n + ".png"))) missing.Add(n);
            Assert.IsEmpty(missing, "tutorial pictures missing: " + string.Join(", ", missing));
            foreach (var t in Tutorials.All)
            {
                Assert.Greater(t.Steps.Count, 0, t.Id + " has no steps");
                Assert.Contains(t.Phase, Tutorials.PhaseOrder);
                foreach (var s in t.Steps) { Assert.IsNotEmpty(s.Title); Assert.IsNotEmpty(s.Text); }
            }
            Assert.NotNull(TutorialPictures.Get("c_12_pouring"), "picture loader failed");

            PutPlayer(new Vector3(2f, 0f, -6f), 20f, 8f);
            TutorialApp.Open = null;
            Tablet.I.ShowApp("Tutorials");
            yield return Frames(3);
            Assert.AreEqual("Tutorials", Tablet.I.CurrentApp);
            yield return GuiPic(Game.Player.Cam, "tut_app_01_list", false, true, true);

            TutorialApp.Show("site-pour", 2);
            yield return Frames(3);
            yield return GuiPic(Game.Player.Cam, "tut_app_02_step", false, true, true);
            TutorialApp.Show("frame-walls", 1);
            yield return Frames(3);
            yield return GuiPic(Game.Player.Cam, "tut_app_03_step_framing", false, true, true);
            TutorialApp.Show("fin-drywall", 0);
            yield return Frames(3);
            yield return GuiPic(Game.Player.Cam, "tut_app_04_placeholder", false, true, true);
            TutorialApp.Show("start-place", 3);
            yield return Frames(3);
            yield return GuiPic(Game.Player.Cam, "tut_app_05_show_me_where", false, true, true);
            if (!Application.isBatchMode) Assert.Greater(Tutorials.Seen(Tutorials.Get("start-place")), 3, "progress not tracked"); // the app only draws (and records progress) when OnGUI runs

            // "show me where" puts a beacon on the nail pile and closes the tablet
            var step = Tutorials.Get("start-place").Steps[3];
            var where = step.Find();
            Assert.IsTrue(where.HasValue, "nail pile not found");
            TutorialBeacon.Mark(where.Value, step.Where);
            Tablet.I.Close();
            yield return Frames(4);
            Assert.NotNull(TutorialBeacon.Current);
            PutPlayer(where.Value + new Vector3(-3f, 0f, -5f), 30f, 5f);
            yield return Frames(3);
            yield return GuiPic(Game.Player.Cam, "tut_app_06_beacon", true, false, true);
            Object.Destroy(TutorialBeacon.Current.gameObject);
        }
    }
}
