using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Heavy equipment (Scripts/Equipment): rent/buy rules and prices, the mini excavator digging a footing trench on the
    /// building pad (spoil pile + loading the dump truck), skid steer forks and grading, the boom lift raising the player,
    /// the crane truck's load chart and setting a roof truss. Screenshots Logs/shots/equip_*.png.
    /// Content sits around (-250, 0, 240) except the excavator / skid steer earthwork, which uses the building pad (restored after).</summary>
    public class EquipmentTests
    {
        static readonly Vector3 Area = new Vector3(-250f, 0f, 240f);
        int _money; GameMode _mode; CompanyStage _stage; Vector3 _playerPos; bool _reqStock;
        float[] _padH; GroundCover[] _padCover; bool[] _padComp; bool _madePad;
        readonly List<GameObject> _junk = new List<GameObject>();

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static float I(float inch) => inch * Units.Inch;
        static void Shot(Vector3 pos, Vector3 look, string name) => SiteLookTests.Shot(pos, look, name);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(2);
            _money = Game.Wallet.Money; _mode = GameSettings.Mode; _stage = Company.Stage; _reqStock = FramingJob.RequireStock;
            if (Game.Player != null) _playerPos = Game.Player.transform.position;
            Equipment.ResetState();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Equipment.ResetState();
            foreach (var l in new List<EquipmentLoad>(EquipmentLoad.All)) if (l != null) Object.Destroy(l.gameObject);
            foreach (var g in _junk) if (g != null) Object.Destroy(g);
            _junk.Clear();
            RestorePad();
            Company.ResetState();
            Company.Stage = _stage;
            Game.Wallet = new Wallet(_money);
            GameSettings.Mode = _mode;
            FramingJob.RequireStock = _reqStock;
            var p = Game.Player;
            if (p != null)
            {
                var cc = p.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                p.transform.position = _playerPos;
                if (cc != null) cc.enabled = true;
                p.enabled = true;
            }
            yield return Frames(2);
            Assert.AreEqual(0, Machine.All.Count, "machines left behind");
            Assert.IsNull(Machine.Current);
        }

        void SnapshotPad()
        {
            if (SiteGround.I == null) { SiteGround.Ensure(); _madePad = true; }
            var g = SiteGround.I;
            _padH = (float[])g.H.Clone(); _padCover = (GroundCover[])g.Cover.Clone(); _padComp = (bool[])g.Compacted.Clone();
        }

        void RestorePad()
        {
            if (_padH == null || SiteGround.I == null) { _padH = null; return; }
            var g = SiteGround.I;
            System.Array.Copy(_padH, g.H, _padH.Length); System.Array.Copy(_padCover, g.Cover, _padCover.Length); System.Array.Copy(_padComp, g.Compacted, _padComp.Length);
            g.Version++; g.Rebuild();
            if (_madePad) Object.Destroy(g.gameObject);
            _padH = null; _madePad = false;
        }

        static Bounds RenderBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<MeshRenderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------ business

        [UnityTest]
        public IEnumerator RentBuyAndFreeBuild()
        {
            GameSettings.Mode = GameMode.Career;
            Company.ResetState();
            Game.Wallet = new Wallet(200000);
            Assert.IsFalse(Equipment.Unlocked, "locked at the Handyman stage");
            Assert.IsFalse(Equipment.Rent("miniex", out _));
            Assert.IsNull(Equipment.Deliver("miniex"), "nothing to deliver before renting");

            Company.Stage = CompanyStage.Contractor;
            Assert.IsTrue(Equipment.Unlocked);
            var ex = Equipment.Get("miniex");
            Assert.That(ex.RentPerDay, Is.InRange(250, 500), "3.5 t mini excavator rents for ~$300-450/day in the US");
            Assert.That(Equipment.Get("boom").RentPerDay, Is.GreaterThan(ex.RentPerDay));
            int before = Game.Wallet.Money;
            Assert.IsTrue(Equipment.Rent("miniex", out string msg), msg);
            Assert.AreEqual(before - ex.RentPerDay - Equipment.DeliveryFee, Game.Wallet.Money, "first day + delivery charged");
            Assert.IsTrue(Equipment.IsRented("miniex"));
            var m = Equipment.Deliver("miniex", Area, 0f);
            Assert.NotNull(m);
            Assert.IsTrue(m.Rented);

            before = Game.Wallet.Money;
            Assert.IsTrue(Equipment.Buy("boomlift", true, out msg), msg);
            Assert.AreEqual(before - Equipment.Get("boomlift").PriceUsed, Game.Wallet.Money);
            Assert.IsTrue(Equipment.IsOwned("boomlift"));
            Assert.IsFalse(Equipment.Buy("skidsteer", false, out _), "skid steer is bought at the Dealership");
            Company.AddVehicle("skidsteer", "Skid steer");
            Assert.IsTrue(Equipment.IsOwned("skidsteer"), "the fleet's skid steer counts as owned");

            // save round trip keeps rentals, ownership and the machine on site
            string json = Equipment.Save();
            Equipment.ResetState();
            yield return Frames(2);
            Assert.AreEqual(0, Machine.All.Count);
            Equipment.Load(json);
            Assert.IsTrue(Equipment.IsRented("miniex"));
            Assert.IsTrue(Equipment.IsOwned("boomlift"));
            Assert.NotNull(Equipment.OnSite("miniex"), "machine back on site after load");

            before = Game.Wallet.Money;
            Assert.IsTrue(Equipment.Return("miniex", out msg), msg);
            Assert.AreEqual(before - Equipment.DeliveryFee, Game.Wallet.Money, "pick-up fee");
            Assert.IsFalse(Equipment.IsRented("miniex"));
            yield return Frames(2);
            Assert.IsNull(Equipment.OnSite("miniex"));

            // Free Build: every machine, free
            GameSettings.Mode = GameMode.FreeBuild;
            Company.ResetState();
            foreach (var t in Equipment.Catalog) Assert.IsTrue(Equipment.Available(t.Id), t.Id);
            before = Game.Wallet.Money;
            Assert.NotNull(Equipment.Deliver("boom", Area + new Vector3(0, 0, 20f), 90f));
            Assert.AreEqual(before, Game.Wallet.Money);
        }

        // ------------------------------------------------------------------ mini excavator

        [UnityTest]
        public IEnumerator MiniExcavator_DigsFootingTrench_SpoilAndTruck()
        {
            GameSettings.Mode = GameMode.FreeBuild;
            SnapshotPad();
            var g = SiteGround.I;
            // trench along x at z = 9 m on the pad, machine standing south of it facing north
            Vector3 stand = new Vector3(-1.5f, g.HeightAt(new Vector3(-1.5f, 0, 4.6f)), 4.6f);
            var ex = (MiniExcavator)Equipment.Deliver("miniex", stand, 0f);
            Assert.NotNull(ex);
            yield return Frames(3);

            // real size
            var b = RenderBounds(ex.gameObject);
            Assert.That(b.size.x, Is.InRange(1.5f, 2.3f), "about 5' 7\" wide (blade/tracks)");
            Assert.That(b.max.y - ex.transform.position.y, Is.InRange(2.2f, 3.6f), "canopy ~8' 2\" (arm may stand higher)");
            Vector3 p = ex.transform.position;
            Shot(p + new Vector3(4.2f, 2.2f, 5.2f), p + new Vector3(0, 1.1f, 0.6f), "equip_01_excavator_close");

            // controls actually move joints
            float boom0 = ex.BoomDeg, swing0 = ex.SwingDeg;
            ex.Auto = new MachineControls { Boom = 1f, Swing = 1f };
            yield return new WaitForSeconds(0.5f);
            ex.Auto = null;
            Assert.Greater(ex.BoomDeg, boom0 + 3f, "boom raises");
            Assert.Greater(Mathf.DeltaAngle(swing0, ex.SwingDeg), 3f, "house swings");

            // a shovel click removes ~0.003 m³; one bucket pass must be far more
            float grade = g.HeightAt(new Vector3(0, 0, 9f));
            float bottom = grade - I(30f);                        // 30" deep footing trench
            float first = ex.DigPass(new Vector3(-1.5f, grade, 9.0f), bottom);
            Assert.Greater(first, 0.05f, "one bucket pass digs a real bucketful");
            Assert.Greater(first, 15f * Mathf.PI * 0.13f * 0.13f * I(2f), "at least 15x a shovel scoop");
            Vector3 pile = p + new Vector3(-3.2f, 0, 1.0f);
            Assert.IsTrue(ex.PoseTipAt(pile + Vector3.up * 1.2f), "reaches the spoil spot");
            Vector3 dumpAt = ex.BucketTip; dumpAt.y = 0;
            pile = dumpAt;
            float pileY0 = g.HeightAt(pile);
            var before = (float[])g.H.Clone();
            float dumped = ex.Dump();
            Assert.Greater(dumped, 0.05f);
            float rise = 0f;
            for (int i = 0; i < before.Length; i++) rise = Mathf.Max(rise, g.H[i] - before[i]);
            Assert.Greater(rise, 0.05f, $"spoil pile beside the trench: dumped {dumped:0.000} m3 at {dumpAt} onPad={Earth.OnPad(dumpAt)} height {pileY0:0.00}->{g.HeightAt(pile):0.00} tip now {ex.BucketTip}");

            int passes = 0;
            for (float x = -3.0f; x <= 0.0f; x += 0.3f)
                for (int k = 0; k < 6; k++)
                {
                    if (ex.DigPass(new Vector3(x, grade, 9.0f), bottom) > 0f) passes++;
                    if (ex.BucketLoadM3 > ex.BucketCapacityM3Value() * 0.5f) { ex.PoseTipAt(pile + Vector3.up * 1.4f); ex.Dump(); }
                    if (k % 3 == 0) yield return null;
                }
            Assert.Greater(passes, 8);
            Assert.Less(g.HeightAt(new Vector3(-1.5f, 0, 9.0f)), grade - I(24f), "trench reached footing depth");
            Assert.Greater(ex.TotalDugM3, 0.5f);

            // mid-dig pose for the photo: bucket in the trench
            ex.DigPass(new Vector3(-0.6f, grade, 9.0f), bottom);
            yield return Frames(3);
            g.Rebuild();
            yield return Frames(2);
            Shot(p + new Vector3(3.6f, 3.4f, 1.0f), new Vector3(-1.3f, grade - 0.3f, 8.6f), "equip_02_excavator_digging");

            // load the dump truck
            var truck = Equipment.CallHaulTruck();
            Assert.NotNull(truck);
            yield return Frames(2);
            Vector3 bed = truck.transform.TransformPoint(new Vector3(0, 2.3f, -1.55f));
            float loadsBefore = truck.LoadYd;
            Assert.Greater(ex.DigPass(new Vector3(-2.0f, grade, 8.1f), bottom), 0.02f, "fresh bucketful");
            ex.PoseTipAt(bed);
            Assert.IsTrue(truck.OverBed(ex.BucketTip), "bucket over the dump body");
            Assert.Greater(ex.Dump(), 0f);
            Assert.Greater(truck.LoadYd, loadsBefore, "spoil went into the truck");
            ex.DigPass(new Vector3(-2.6f, grade, 8.1f), bottom);
            ex.PoseTipAt(bed + Vector3.up * 0.4f);
            yield return Frames(3);
            Vector3 tp = truck.transform.position;
            Shot(p + new Vector3(-4.5f, 4.5f, -4.0f), (p + tp) * 0.5f + Vector3.up * 1.2f, "equip_03_excavator_loading_truck");

            // climb in / out through the base class
            ex.Enter();
            yield return Frames(2);
            Assert.IsTrue(ex.Operating);
            Assert.AreSame(ex, Machine.Current);
            Assert.IsFalse(Game.Player.enabled, "on-foot rig off while operating");
            ex.Exit();
            yield return Frames(2);
            Assert.IsFalse(ex.Operating);
            Assert.IsTrue(Game.Player.enabled);
        }

        // ------------------------------------------------------------------ skid steer

        [UnityTest]
        public IEnumerator SkidSteer_ForksAndGrading()
        {
            GameSettings.Mode = GameMode.FreeBuild;
            Vector3 at = Area + new Vector3(15f, 0, 0);
            var ss = (SkidSteer)Equipment.Deliver("skidsteer", at, 0f);
            Assert.NotNull(ss);
            yield return Frames(3);
            var b = RenderBounds(ss.gameObject);
            Assert.That(b.size.x, Is.InRange(1.6f, 2.0f), "72\" bucket");
            Assert.That(b.max.y - at.y, Is.InRange(1.8f, 2.3f), "cab ~6' 6\"");
            Shot(at + new Vector3(3.2f, 1.8f, 4.2f), at + new Vector3(0, 0.9f, 0.4f), "equip_04_skidsteer_bucket");

            // forks: an OSB lift (1,760 lb) is fine, a full shingle pallet (3,100 lb) is over the 2,690 lb ROC
            ss.SetAttachment(SkidSteer.Attachment.Forks);
            ss.LiftDeg = 0f; ss.TiltDeg = 0f;
            yield return Frames(2);
            Vector3 tip = ss.CuttingEdge;
            var heavy = EquipmentLoad.Create("shingles", new Vector3(tip.x, at.y, tip.z) + ss.transform.forward * 0.25f, ss.Yaw);
            yield return Frames(2);
            Assert.IsFalse(ss.TryPickUp(), "over rated operating capacity");
            Object.Destroy(heavy.gameObject);
            yield return Frames(2);
            var osb = EquipmentLoad.Create("osb", new Vector3(tip.x, at.y, tip.z) + ss.transform.forward * 0.35f, ss.Yaw + 90f);
            yield return Frames(2);
            Assert.IsTrue(ss.TryPickUp(), "picks up the OSB lift");
            Assert.AreSame(osb, ss.Carried);
            ss.Joints = new[] { 35f, 8f, 1f };
            yield return Frames(3);
            Assert.Greater(osb.transform.position.y, at.y + 0.6f, "load lifted");
            Shot(at + new Vector3(-3.6f, 2.2f, 4.0f), at + new Vector3(0, 1.2f, 1.0f), "equip_05_skidsteer_forks");
            ss.Joints = new[] { 0f, 0f, 1f };
            ss.SetDownCarried();
            Assert.IsNull(ss.Carried);
            yield return Frames(2);

            // bucket grading on the pad: cut a high spot flat
            ss.SetAttachment(SkidSteer.Attachment.Bucket);
            SnapshotPad();
            var g = SiteGround.I;
            Vector3 padStart = new Vector3(3f, 0, 3f);
            float baseY = g.HeightAt(padStart + new Vector3(0, 0, 4f));
            g.RaiseTo(padStart + new Vector3(0, 0, 4f), 0.6f, baseY + 0.15f, GroundCover.Dirt);
            ss.TeleportTo(new Vector3(padStart.x, baseY, padStart.z), 0f);
            float moved = ss.GradePass(padStart + new Vector3(0, 0, 2f), padStart + new Vector3(0, 0, 6f), baseY);
            Assert.Greater(moved, 0.02f, "bucket cut the high spot");
            Assert.Less(g.HeightAt(padStart + new Vector3(0, 0, 4f)), baseY + 0.08f, "graded down near the edge height");
        }

        // ------------------------------------------------------------------ boom lift

        [UnityTest]
        public IEnumerator BoomLift_RaisesPlayerToWorkAtHeight()
        {
            GameSettings.Mode = GameMode.FreeBuild;
            Vector3 at = Area + new Vector3(30f, 0, 0);
            var bl = (BoomLift)Equipment.Deliver("boomlift", at, 0f);
            Assert.NotNull(bl);
            yield return Frames(3);
            var b = RenderBounds(bl.gameObject);
            Assert.That(b.size.z, Is.InRange(6.5f, 8.2f), "about 24' long stowed");
            Assert.Less(bl.PlatformHeight, 0.8f, "stowed platform near the ground");
            Shot(at + new Vector3(6.5f, 2.6f, 5.5f), at + new Vector3(0, 1.2f, 0), "equip_06_boomlift_stowed");

            bl.Enter();
            yield return Frames(2);
            Assert.IsTrue(bl.Operating);
            bl.Auto = new MachineControls { Boom = 1f };
            yield return new WaitForSeconds(1.0f);
            bl.Auto = null;
            Assert.Greater(bl.BoomDeg, 3f, "boom raises from the controls");
            bl.Joints = new[] { 30f, 62f, 5.0f };
            yield return Frames(3);
            Assert.Greater(bl.PlatformHeight, 9.0f, "platform up near 40'");
            Assert.Less(Mathf.Abs(Vector3.Angle(bl.Platform.up, Vector3.up)), 2f, "platform stays level");
            Assert.Greater(Game.Player.transform.position.y, at.y + 9f, "operator rides up with it");

            bl.StandInBasket();
            yield return Frames(4);
            Assert.IsFalse(bl.Operating);
            Assert.IsTrue(Game.Player.enabled, "player has their tools back");
            Assert.IsTrue(bl.PlayerInBasket(), "standing in the basket");
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(bl.PlayerInBasket(), "the basket floor holds the player");
            Vector3 pp = bl.Platform.position;
            Shot(at + new Vector3(-10f, 4f, -9f), (at + pp) * 0.5f + Vector3.up * 1f, "equip_07_boomlift_raised");
            bl.TakeControls();
            yield return Frames(2);
            Assert.IsTrue(bl.Operating);
            bl.Joints = new[] { 0f, 0f, 0f };
            yield return Frames(3);
            bl.Exit();
            yield return Frames(2);
            Assert.IsFalse(bl.Operating);
        }

        // ------------------------------------------------------------------ crane truck

        [UnityTest]
        public IEnumerator Crane_LoadChartAndSetsTruss()
        {
            GameSettings.Mode = GameMode.FreeBuild;
            FramingJob.RequireStock = false;
            Assert.AreEqual(24000f, TruckCrane.CapacityLb(Units.Foot * 10f), 300f);
            Assert.AreEqual(3900f, TruckCrane.CapacityLb(Units.Foot * 50f), 300f);
            Assert.Less(TruckCrane.CapacityLb(Units.Foot * 80f), 1f, "nothing beyond the chart");
            Assert.Greater(TruckCrane.CapacityLb(Units.Foot * 20f), TruckCrane.CapacityLb(Units.Foot * 40f));

            // a 16' x 12' stand-in shell to set trusses on, crane parked along the eave
            Vector3 o = Area + new Vector3(0, 0, 40f);
            var fp = Footprint.Make(o, 0f, 16f, 12f);
            _junk.Add(StandIn(fp).gameObject);
            var job = RoofFraming.Trusses(fp, 6, 12, 12, false);
            _junk.Add(job.gameObject);
            int guard = 0;
            while (job.Current != null && !job.Current.Label.StartsWith("Set") && guard++ < 10) Assert.IsTrue(job.TryAdvance(null, out var m0), m0);
            Assert.IsTrue(job.Current.Label.StartsWith("Set"), "first truss step");

            var crane = (TruckCrane)Equipment.Deliver("boom", o + new Vector3(fp.Length * 0.5f, 0, -5.5f), 90f);
            Assert.NotNull(crane);
            yield return Frames(3);
            var bb = RenderBounds(crane.gameObject);
            Assert.That(Mathf.Max(bb.size.x, bb.size.z), Is.InRange(9.0f, 11.5f), "~33' boom truck");
            Vector3 cp = crane.transform.position;
            Shot(cp + new Vector3(-7f, 3.5f, -9f), cp + new Vector3(0, 1.8f, 0), "equip_08_crane_close");

            crane.Auto = new MachineControls { Boom = 1f };
            yield return Frames(10);
            crane.Auto = null;
            Assert.Less(crane.BoomDeg, 1f, "no crane work before the outriggers are down");
            crane.SetOutriggers(true);
            yield return new WaitForSeconds(5.5f);
            Assert.IsTrue(crane.OutriggersDown);

            // overload: a block pallet (3,150 lb) far out at ~60' radius is over the chart
            var block = EquipmentLoad.Create("block", cp + crane.transform.rotation * new Vector3(0, 0, -19f), 0f);
            yield return Frames(2);
            crane.PoseHookAt(block.transform.position + block.PickLocal);
            Assert.Greater(crane.RadiusM, Units.Foot * 55f);
            Assert.IsFalse(crane.Rig(block), "load chart refuses 3,150 lb at ~60'");
            Object.Destroy(block.gameObject);

            // set the first truss with the crane
            var step = job.Current;
            int next = job.Next;
            Assert.IsNotNull(crane.TrussJob, "crane finds the truss job");
            Assert.IsTrue(crane.RigNextTruss(), "rigs a truss");
            Assert.NotNull(crane.HangingTruss);
            var ghost = step.Ghost();
            Vector3 target = ghost.center + Vector3.up * 1.2f;
            crane.PoseHookAt(target + (crane.HookPos - crane.HangingTruss.position));
            yield return Frames(3);
            Shot(o + new Vector3(-6f, 6.5f, -11f), ghost.center + Vector3.up * 0.5f, "equip_09_crane_setting_truss");
            Assert.IsFalse(crane.TrySetTruss(out _), "still 4' too high");
            crane.PoseHookAt(ghost.center + (crane.HookPos - crane.HangingTruss.position));
            yield return Frames(2);
            Assert.IsTrue(crane.TrySetTruss(out string msg), msg);
            Assert.AreEqual(next + 1, job.Next, "the truss step is done");
            Assert.IsNull(crane.HangingTruss);
            // a second one
            Assert.IsTrue(crane.RigNextTruss());
            var g2 = job.Current.Ghost();
            crane.PoseHookAt(g2.center + (crane.HookPos - crane.HangingTruss.position));
            yield return Frames(2);
            Assert.IsTrue(crane.TrySetTruss(out msg), msg);
            yield return Frames(3);
            Shot(o + new Vector3(fp.Length + 6f, 5.5f, -8f), o + new Vector3(fp.Length * 0.5f, 2.6f, 1.5f), "equip_10_crane_trusses_set");
        }

        static Transform StandIn(Footprint fp)
        {
            var root = new GameObject("EquipStandInShell").transform;
            root.SetPositionAndRotation(new Vector3(fp.Origin.x, 0f, fp.Origin.z), fp.Rotation);
            float L = fp.Length, W = fp.Width, P = fp.PlateTopY, wd = fp.WallDepth, deck = fp.DeckTopY;
            Framer.AttachMesh(root, "Walls", (MeshBuilder b, System.Func<SurfaceKind, int> s) =>
            {
                int osb = s(SurfaceKind.Osb), conc = s(SurfaceKind.Concrete);
                b.AddBox(new Vector3(L / 2, deck / 2 - 0.05f, W / 2), new Vector3(L + 0.02f, deck + 0.1f, W + 0.02f), conc);
                float h = P - deck, yc = deck + h / 2, sh = I(0.4375f);
                b.AddBox(new Vector3(L / 2, yc, wd / 2 - sh / 2), new Vector3(L, h, wd + sh), osb);
                b.AddBox(new Vector3(L / 2, yc, W - wd / 2 + sh / 2), new Vector3(L, h, wd + sh), osb);
                b.AddBox(new Vector3(wd / 2 - sh / 2, yc, W / 2), new Vector3(wd + sh, h, W - 2 * wd), osb);
                b.AddBox(new Vector3(L - wd / 2 + sh / 2, yc, W / 2), new Vector3(wd + sh, h, W - 2 * wd), osb);
            });
            return root;
        }
    }

    static class EquipTestExt
    {
        public static float BucketCapacityM3Value(this MiniExcavator ex) => MiniExcavator.BucketCapacityM3;
    }
}
