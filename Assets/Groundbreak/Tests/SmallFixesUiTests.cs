using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Small UI fixes: held tool put away while the tablet is open, supplier delivery fee on tablet store
    /// orders (Career), and the "Add garage wing" (cross gable) stage in Framing Plans. Shots: Logs/shots/smallfix_*.</summary>
    public class SmallFixesUiTests
    {
        int _money; GameMode _mode; bool _unlimited, _chargeFee, _requireStock;
        Tool[] _slots; List<Tool> _owned; int _active;
        readonly List<GameObject> _spawned = new List<GameObject>();

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static string ShotDir { get { var d = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(d); return d; } }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(2);
            _money = Game.Wallet.Money; _mode = GameSettings.Mode; _unlimited = GameSettings.UnlimitedMoney;
            _chargeFee = Fleet.ChargeSupplierDelivery; _requireStock = FramingJob.RequireStock;
            if (Game.Belt != null) { _slots = (Tool[])Game.Belt.Slots.Clone(); _owned = new List<Tool>(Game.Belt.Owned); _active = Game.Belt.ActiveIndex; }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Tablet.I != null && Tablet.I.IsOpen) Tablet.I.Close();
            foreach (var g in _spawned) if (g != null) Object.Destroy(g);
            _spawned.Clear();
            Game.Wallet = new Wallet(_money);
            GameSettings.Mode = _mode; GameSettings.UnlimitedMoney = _unlimited;
            Fleet.ChargeSupplierDelivery = _chargeFee; FramingJob.RequireStock = _requireStock;
            if (Game.Belt != null && _slots != null)
            {
                Game.Belt.Owned.Clear(); Game.Belt.Owned.AddRange(_owned);
                for (int i = 0; i < ToolBelt.SlotCount; i++) Game.Belt.Slots[i] = _slots[i];
                Game.Belt.Select(_active);
            }
            yield return FinishTestKit.CleanScene();
        }

        // ------------------------------------------------------------------ (a) held tool vs tablet

        static void PlayerShot(string name)
        {
            var pl = Game.Player; var vm = pl.GetComponent<ViewModel>();
            var rt = new RenderTexture(1280, 720, 24);
            foreach (var c in new[] { pl.Cam, vm != null ? vm.Camera : null })
            { if (c == null) continue; var prev = c.targetTexture; c.targetTexture = rt; c.Render(); c.targetTexture = prev; }
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        [UnityTest]
        public IEnumerator HeldToolHiddenWhileTabletOpen()
        {
            yield return Frames(3);
            Assert.NotNull(Game.Player, "player"); Assert.NotNull(Tablet.I, "tablet");
            var vm = Game.Player.GetComponent<ViewModel>(); Assert.NotNull(vm, "view-model");
            var belt = Game.Belt;
            Tool hammer = belt.Owned.Find(t => t.Id == "hammer");
            if (hammer == null) { hammer = ToolFactory.Create("hammer"); Assert.NotNull(hammer, "hammer tool"); belt.AddTool(hammer); }
            belt.Assign(hammer, 1); belt.Select(1);
            yield return Frames(3);
            vm.SkipEquip(); vm.Step(0f);
            Assert.AreEqual("Hammer", ViewModel.ShownKind);
            Assert.IsTrue(vm.ModelShown, "tool shown with the tablet closed");
            PlayerShot("smallfix_vm_tablet_closed");

            Tablet.I.Open();
            yield return Frames(3);
            Assert.IsFalse(vm.ModelShown, "tool put away while the tablet is open");
            PlayerShot("smallfix_vm_tablet_open");

            Tablet.I.Close();
            yield return Frames(3);
            Assert.IsTrue(vm.ModelShown, "tool back in hand when the tablet closes");
        }

        // ------------------------------------------------------------------ (b) supplier delivery fee

        [Test]
        public void DeliveryFeeAppliesInCareerOnly()
        {
            GameSettings.Mode = GameMode.FreeBuild;
            Assert.AreEqual(0, Fleet.SupplierDeliveryFee, "Free Build delivers free");
            Assert.AreEqual(100, Fleet.StoreOrderTotal(100, false));
            GameSettings.Mode = GameMode.Career;
            Assert.AreEqual(Fleet.DeliveryFee, Fleet.SupplierDeliveryFee, "Career pays the supplier");
            Assert.AreEqual(100 + Fleet.DeliveryFee, Fleet.StoreOrderTotal(100, false));
            Assert.AreEqual(100 + Fleet.DeliveryFee + DeliveryService.RushFee, Fleet.StoreOrderTotal(100, true));
            Fleet.ChargeSupplierDelivery = false;
            Assert.AreEqual(0, Fleet.SupplierDeliveryFee, "setting off: no fee");
        }

        [UnityTest]
        public IEnumerator StoreCheckoutChargesDeliveryFee()
        {
            yield return Frames(2);
            Assert.NotNull(Tablet.I, "tablet"); Assert.NotNull(DeliveryService.I, "delivery service");
            var def = Catalog.Get("2x4x8"); Assert.NotNull(def, "2x4x8");
            GameSettings.Mode = GameMode.Career; GameSettings.UnlimitedMoney = false;
            Game.Wallet = new Wallet(10000);

            var cart = (Dictionary<string, int>)typeof(Tablet).GetField("_cart", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Tablet.I);
            var saved = new Dictionary<string, int>(cart);
            int pendingBefore = DeliveryService.I.Pending.Count;
            cart.Clear(); cart["2x4x8"] = 2;
            typeof(Tablet).GetMethod("Checkout", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Tablet.I, new object[] { false });

            Assert.AreEqual(10000 - def.Price * 2 - Fleet.DeliveryFee, Game.Wallet.Money, "materials + delivery fee charged");
            Assert.AreEqual(pendingBefore + 1, DeliveryService.I.Pending.Count, "delivery scheduled");

            // cancel the test delivery so it doesn't drop stock into later tests
            if (pendingBefore == 0) DeliveryService.I.StopAllCoroutines();
            DeliveryService.I.Pending.RemoveAt(DeliveryService.I.Pending.Count - 1);
            cart.Clear(); foreach (var kv in saved) cart[kv.Key] = kv.Value;
        }

        // ------------------------------------------------------------------ (c) cross-gable wing stage

        [UnityTest]
        public IEnumerator GarageWingStageInFramingPlans()
        {
            yield return Frames(2);
            FramingJob.RequireStock = false;
            var fp = Footprint.Make(new Vector3(520f, 0f, -520f), 0f, 32f, 24f);
            var p = new ShellProject { Fp = fp };
            p.Roof = RoofFraming.Trusses(fp, 6, 12, 12, false); _spawned.Add(p.Roof.gameObject);
            p.Roof.RunAll();
            Assert.IsFalse(p.CanAddWing, "no wing before the main roof is sheathed");
            p.RoofSheathing = RoofFraming.RoofSheathing(RoofFraming.InfoOf(p.Roof)); _spawned.Add(p.RoofSheathing.gameObject);
            p.RoofSheathing.RunAll();
            yield return Frames(2);

            p.Hip = true; Assert.IsFalse(p.CanAddWing, "hip roofs don't get the gable wing"); p.Hip = false;
            Assert.IsTrue(p.CanAddWing, "wing offered after roof sheathing, before dry-in");
            Assert.AreEqual(14f, p.WingWidthFt); Assert.AreEqual(20f, p.WingProjectionFt);

            var wing = p.StartWing();
            Assert.NotNull(wing, "wing framed"); _spawned.Add(wing.FramingJob.gameObject);
            Assert.Contains(wing.FramingJob, p.Jobs, "wing job listed in Framing Plans");
            Assert.AreEqual(2, wing.Valleys.Count, "two valleys");
            Assert.IsFalse(p.CanAddWing, "only one wing");
            Assert.IsFalse(p.CanRoofWing, "wing roofing waits for framing + main shingles");
            wing.FramingJob.RunAll();
            yield return Frames(2);
            Assert.IsTrue(wing.Framed, "wing framing done");
            Assert.IsFalse(p.CanRoofWing, "still waits for the main shingles");

            var cam = StructurePolishTests.Cam(fp.World(-5f, 8f, -14f), fp.World(fp.Length / 2f, 3f, -1f), 55f);
            _spawned.Add(cam.gameObject);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.72f, 0.82f);
            StructurePolishTests.Shot(cam, "smallfix_garage_wing");
        }
    }
}
