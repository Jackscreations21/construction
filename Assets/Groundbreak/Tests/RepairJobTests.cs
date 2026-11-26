using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Handyman service calls: offer generator, diagnosis + repair procedures, van stock, invoice, callbacks, save round trip.</summary>
    public class RepairJobTests
    {
        int _money, _rep, _day; float _hours; GameMode _mode; bool _relaxed;
        Vector3 _pos; float _yaw;
        Tool[] _slots; List<Tool> _owned; int _active;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static RepairContext Ctx => RepairContext.Bypass;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(2);
            _money = Game.Wallet.Money; _rep = Career.Reputation; _mode = GameSettings.Mode; _relaxed = GameSettings.Relaxed;
            if (GameClock.I != null) { _day = GameClock.I.Day; _hours = GameClock.I.Hours; GameClock.I.Hours = 11f; }
            if (Game.Player != null) { _pos = Game.Player.transform.position; _yaw = Game.Player.transform.eulerAngles.y; }
            if (Game.Belt != null) { _slots = (Tool[])Game.Belt.Slots.Clone(); _owned = new List<Tool>(Game.Belt.Owned); _active = Game.Belt.ActiveIndex; }
            GameSettings.Mode = GameMode.Career; GameSettings.Relaxed = false;
            Company.ResetState();
            RepairJobs.ResetState();
            HandymanStart.Begin();
            RepairKit.GrantStarterTools();
            Company.Offers.RemoveAll(j => j.Kind == JobKind.Repair);
            Career.Reputation = 50;
            yield return Frames(2);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RepairJobs.ResetState();
            TownLadder.ResetState();
            WorkVan.Despawn();
            VanCargo.Clear();
            Town.Destroy();
            Company.ResetState();
            Game.Wallet = new Wallet(_money);
            Career.Reputation = _rep; GameSettings.Mode = _mode; GameSettings.Relaxed = _relaxed;
            if (GameClock.I != null) { GameClock.I.Day = _day; GameClock.I.Hours = _hours; }
            if (Game.Belt != null && _slots != null)
            {
                Game.Belt.Owned.Clear(); Game.Belt.Owned.AddRange(_owned);
                for (int i = 0; i < ToolBelt.SlotCount; i++) Game.Belt.Slots[i] = _slots[i];
                Game.Belt.Select(_active);
            }
            HandymanStart.MovePlayer(_pos, _yaw);
            yield return Frames(2);
        }

        // ------------------------------------------------------------------ helpers

        static TownLot Lot(int i) { var l = new List<TownLot>(Town.Addresses); return l[i % l.Count]; }

        static Job Accept(TownLot lot, params (RepairType, HouseSpot, int)[] defects)
        {
            var job = RepairJobs.CreateJob(lot, true, defects);
            Assert.IsNull(Company.Blocker(job), "van owner should be able to take a repair job");
            Assert.IsTrue(Company.Accept(job, out var msg), msg);
            Assert.AreEqual(defects.Length, RepairJobs.DefectsFor(job).Count, "defects spawned on accept");
            return job;
        }

        static RepairDefect Find(Job job, RepairType t) => RepairJobs.DefectsFor(job).Find(d => d.Type == t);

        static void Do(RepairDefect d, params string[] actions)
        {
            foreach (var a in actions) Assert.IsTrue(d.Perform(a, Ctx, out var msg), $"{d.Title}: '{a}' failed: {msg}");
        }

        /// <summary>Screenshot in the defect's own frame (camera and look-at are defect-local). Optional fill light for interiors.</summary>
        static void Shot(RepairDefect d, Vector3 cam, Vector3 look, string name, bool light = false, Transform frame = null)
        {
            var t = frame != null ? frame : d.transform;
            Vector3 c = t.TransformPoint(cam);
            GameObject lg = null;
            if (light)
            {
                lg = new GameObject("TestLight");
                lg.transform.position = c + Vector3.up * 0.15f;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 6f; l.intensity = 1.6f;
            }
            SiteLookTests.Shot(c, t.TransformPoint(look), name);
            if (lg != null) Object.DestroyImmediate(lg);
        }

        static RepairInvoice Bill(Job job)
        {
            int before = Game.Wallet.Money, done = Company.JobsCompleted;
            var inv = RepairJobs.SendInvoice(job);
            Assert.NotNull(inv);
            Assert.AreEqual(before + inv.paid + inv.tip, Game.Wallet.Money, "wallet grows by what the customer paid plus the tip");
            Assert.AreEqual(done + 1, Company.JobsCompleted);
            Assert.IsFalse(Company.ActiveJobs.Contains(job));
            Assert.AreEqual(0, RepairJobs.DefectsFor(job).Count, "defects removed after billing");
            return inv;
        }

        // ------------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator GeneratorScalesWithReputation()
        {
            Assert.IsTrue(RepairCatalog.Owns(RepairCatalog.Pliers), "starter tools granted");
            Career.Reputation = 30;
            RepairJobs.GenerateOffers();
            var offers = Company.Offers.FindAll(j => j.Kind == JobKind.Repair);
            Assert.AreEqual(2, offers.Count, "low reputation: two small calls");
            var seen = new HashSet<string>();
            foreach (var j in offers)
            {
                var lot = Town.Find(j.Address);
                Assert.NotNull(lot, j.Address);
                Assert.AreEqual(LotUse.Customer, lot.Use);
                Assert.IsTrue(seen.Add(j.Address), "one open job per address");
                Assert.AreEqual("van", j.RequiredVehicleType);
                Assert.Greater(j.Payout, TripChargeFloor);
                Assert.IsNull(Company.Blocker(j));
                Assert.AreEqual(1, RepairJobs.Payload(j).defects.Count, "small calls are single repairs");
            }
            Career.Reputation = 75;
            RepairJobs.GenerateOffers();
            offers = Company.Offers.FindAll(j => j.Kind == JobKind.Repair);
            Assert.AreEqual(5, offers.Count, "good reputation: five calls");
            seen.Clear();
            foreach (var j in offers) Assert.IsTrue(seen.Add(j.Address));
            RepairJobs.GenerateOffers();
            Assert.AreEqual(5, Company.Offers.FindAll(j => j.Kind == JobKind.Repair).Count, "top-up is idempotent");
            yield return null;
        }
        const int TripChargeFloor = RepairJobs.TripCharge;

        [UnityTest]
        public IEnumerator SinkLeak_CrackedTrap_UsesVanStock()
        {
            var lot = Lot(0);
            var job = Accept(lot, (RepairType.SinkLeak, HouseSpot.KitchenSink, SinkLeakDefect.CrackedTrap));
            var d = Find(job, RepairType.SinkLeak);
            Assert.Less(Vector3.Distance(d.transform.position, lot.House.Anchor(HouseSpot.KitchenSink).position), 0.01f, "spawned at the anchor");
            Assert.NotNull(WorkVan.I, "van spawned");
            WorkVan.I.TeleportTo(lot.Parking, lot.ParkingYaw);
            yield return Frames(2);

            var ctx = new RepairContext { IgnoreTool = true };          // real van stock
            Assert.IsTrue(d.Perform("inspect", ctx));
            StringAssert.Contains("crack", d.Reading);
            Shot(d, new Vector3(0.25f, 0.6f, 0.85f), new Vector3(0f, 0.38f, -0.3f), "repair_01_sink_leak", true);

            // wrong diagnosis: snugging the nuts does not fix a cracked trap
            Assert.IsTrue(d.Perform("tighten", ctx));
            Assert.IsFalse(d.IsFixed);

            int traps = VanCargo.Count(RepairCatalog.Trap112);
            Assert.GreaterOrEqual(traps, 1, "starter kit has a 1-1/2\" trap");
            Assert.IsTrue(d.Perform("bucket", ctx));
            Assert.IsTrue(d.Perform("trap-off", ctx));
            Assert.IsFalse(d.IsFixed);
            // the part is in the van: too far away -> refused
            WorkVan.I.TeleportTo(Town.Store.Parking, Town.Store.ParkingYaw);
            Assert.IsFalse(d.Perform("trap-new", ctx, out var why));
            StringAssert.Contains("van", why);
            WorkVan.I.TeleportTo(lot.Parking, lot.ParkingYaw);
            Assert.IsTrue(d.Perform("trap-new", ctx, out why), why);
            Assert.AreEqual(traps - 1, VanCargo.Count(RepairCatalog.Trap112), "trap taken from the van");
            Assert.IsTrue(d.IsFixed);
            Assert.IsTrue(d.Perform("inspect", ctx));
            Assert.IsTrue(d.Perform("dry", ctx));
            Assert.Greater(d.Quality, 0.95f);
            yield return Frames(1);
            Shot(d, new Vector3(0.25f, 0.6f, 0.85f), new Vector3(0f, 0.38f, -0.3f), "repair_02_sink_fixed", true);

            var inv = Bill(job);
            Assert.Greater(inv.materials, 0, "trap billed at cost + markup");
            Assert.Greater(inv.paid, RepairJobs.TripCharge);
            Assert.IsFalse(inv.willCallBack);
            Assert.AreEqual(0, RepairJobs.PendingCallbacks);
        }

        [UnityTest]
        public IEnumerator DeadOutlet_DiagnoseThenReplaceReceptacle()
        {
            var lot = Lot(1);
            var job = Accept(lot, (RepairType.DeadOutlet, HouseSpot.OutletLiving, DeadOutletDefect.FailedReceptacle));
            var d = (DeadOutletDefect)Find(job, RepairType.DeadOutlet);
            yield return Frames(1);
            Shot(d, new Vector3(0.25f, 0.2f, 0.8f), Vector3.zero, "repair_03_outlet_dead", true);
            Assert.IsFalse(d.IsFixed);
            Do(d, "look-panel");
            StringAssert.Contains("fully ON", d.Reading);
            // wrong guesses: cycling the breaker and resetting the GFCI do nothing
            Do(d, "brk-off", "brk-on", "gfci-reset");
            Assert.IsFalse(d.IsFixed);
            Do(d, "test");
            StringAssert.Contains("power reaches the box", d.Reading);
            Do(d, "brk-off", "pull", "test", "swap", "install");
            Assert.IsFalse(d.IsFixed, "breaker still off");
            Do(d, "brk-on", "test");
            Assert.IsTrue(d.IsFixed);
            Assert.Greater(d.Quality, 0.95f, "safe procedure: no shock");
            Bill(job);

            // tripped GFCI upstream: only the reset fixes it
            var job2 = Accept(Lot(2), (RepairType.DeadOutlet, HouseSpot.OutletLiving, DeadOutletDefect.TrippedGfci));
            var g = Find(job2, RepairType.DeadOutlet);
            Do(g, "brk-off", "brk-on");
            Assert.IsFalse(g.IsFixed);
            Do(g, "gfci-reset", "test");
            Assert.IsTrue(g.IsFixed);
            Bill(job2);

            // working live: shock = quality hit
            var job3 = Accept(Lot(3), (RepairType.DeadOutlet, HouseSpot.OutletKitchen, DeadOutletDefect.FailedReceptacle));
            var k = Find(job3, RepairType.DeadOutlet);
            Do(k, "pull", "swap", "install", "test");
            Assert.IsTrue(k.IsFixed);
            Assert.Less(k.Quality, 0.7f);
        }

        [UnityTest]
        public IEnumerator DrywallHole_Stages_AndSaveRoundTrip()
        {
            var lot = Lot(4);
            var job = Accept(lot, (RepairType.DrywallHole, HouseSpot.DrywallLiving, DrywallHoleDefect.Large));
            var d = Find(job, RepairType.DrywallHole);
            Vector3 cam = new Vector3(0.5f, 0.1f, 1.5f);
            yield return Frames(1);
            Shot(d, cam, Vector3.zero, "repair_04_drywall_hole", true);
            Assert.IsNull(d.AvailableActions.Find(a => a.Id == "c1"), "can't mud an open hole");
            Do(d, "square", "backing", "panel");
            var noSkip = new RepairContext { IgnoreTool = true, FreeMaterials = true };
            Assert.IsTrue(d.Perform("c1", noSkip, out var why), why);
            // drying is enforced for a real player
            Assert.IsFalse(d.Perform("c2", noSkip, out why));
            StringAssert.Contains("wet", why);
            yield return Frames(1);
            Shot(d, cam, Vector3.zero, "repair_05_drywall_patched_stages", true);

            // ---- save / load mid-job
            string id = job.Id;
            var file = SaveSystem.Capture();
            RepairJobs.ResetState();
            Assert.AreEqual(0, RepairDefect.All.Count);
            SaveSystem.Apply(file);
            yield return Frames(2);
            job = Company.ActiveJobs.Find(j => j.Id == id);
            Assert.NotNull(job, "job restored by Company");
            d = Find(job, RepairType.DrywallHole);
            Assert.NotNull(d, "defect respawned after load");
            Assert.IsTrue(d.Did("panel") && d.Did("c1") && !d.Did("c2"), "progress restored");
            Assert.Less(Vector3.Distance(d.transform.position, Town.Find(lot.Id).House.Anchor(HouseSpot.DrywallLiving).position), 0.01f);

            Do(d, "c2", "c3", "sand", "prime", "paint");
            Assert.IsTrue(d.IsFixed);
            Assert.Greater(d.Quality, 0.95f);
            yield return Frames(1);
            Shot(d, cam, Vector3.zero, "repair_06_drywall_painted", true);
            var inv = Bill(job);
            Assert.AreEqual(Mathf.RoundToInt(3f * RepairJobs.LaborRate), inv.labor, "3 book hours");
        }

        [UnityTest]
        public IEnumerator ExteriorPunchList_ThenSidewalk()
        {
            var lot = Lot(5);
            var job = Accept(lot, (RepairType.DeckBoard, HouseSpot.DeckBoard, 0), (RepairType.Shingles, HouseSpot.RoofShingles, 0),
                (RepairType.Gutter, HouseSpot.Gutter, GutterDefect.CloggedElbow), (RepairType.Caulk, HouseSpot.WindowCaulk, 0), (RepairType.FencePicket, HouseSpot.FencePicket, 0));
            yield return Frames(1);

            var deck = Find(job, RepairType.DeckBoard);
            Vector3 dc = new Vector3(0.9f, 0.9f, -0.2f), dl = new Vector3(0f, 0f, -0.45f);
            Shot(deck, dc, dl, "repair_07_deck_rot");
            Do(deck, "probe", "unscrew", "pry", "joist", "cut", "set", "fasten");
            Assert.IsTrue(deck.IsFixed);
            Shot(deck, dc, dl, "repair_08_deck_fixed");

            var sh = Find(job, RepairType.Shingles);
            Assert.IsTrue(sh.NeedsLadder);
            Assert.IsFalse(sh.Perform("seal", new RepairContext { IgnoreTool = true, FreeMaterials = true }, out var why), "no ladder, player on the ground");
            StringAssert.Contains("ladder", why);
            Vector3 sc = new Vector3(0.7f, 1.0f, -0.6f), sl = new Vector3(0f, 0f, 0.3f);
            Shot(sh, sc, sl, "repair_09_shingles_missing");
            Do(sh, "seal", "pull", "slide", "nail", "cement");
            Assert.IsTrue(sh.IsFixed);
            Shot(sh, sc, sl, "repair_10_shingles_fixed");

            var gu = Find(job, RepairType.Gutter);
            Shot(gu, new Vector3(0.9f, 0.6f, 0.8f), Vector3.zero, "repair_11_gutter_clogged");
            Do(gu, "scoop", "flush");
            Assert.IsFalse(gu.IsFixed, "plugged elbow found by the flush");
            StringAssert.Contains("elbow", gu.Reading);
            Do(gu, "elbow", "flush");
            Assert.IsTrue(gu.IsFixed);

            var ca = Find(job, RepairType.Caulk);
            Shot(ca, new Vector3(0.25f, 0.2f, 0.7f), Vector3.zero, "repair_14_caulk");
            Do(ca, "cut", "clean", "bead-acr", "tool");
            Assert.IsTrue(ca.IsFixed);
            Assert.Greater(ca.Quality, 0.95f);

            var fe = Find(job, RepairType.FencePicket);
            Shot(fe, new Vector3(0.8f, 1.2f, 1.8f), new Vector3(0f, 0.8f, 0f), "repair_15_fence");
            Do(fe, "pry", "nails", "set", "fasten");
            Assert.IsTrue(fe.IsFixed);

            Assert.IsTrue(RepairJobs.AllFixed(job));
            var inv = Bill(job);
            Assert.Greater(inv.satisfaction, 0.9f);
            Assert.Greater(inv.tip, 0, "happy customer tips");

            // ---- sidewalk square
            var job2 = Accept(Lot(6), (RepairType.Sidewalk, HouseSpot.SidewalkSquare, 0));
            var sw = Find(job2, RepairType.Sidewalk);
            Vector3 wc = new Vector3(1.5f, 1.5f, 1.8f);
            yield return Frames(1);
            Shot(sw, wc, Vector3.zero, "repair_12_sidewalk_broken");
            Assert.IsNull(sw.AvailableActions.Find(a => a.Id == "pour"), "no pour before forms");
            Do(sw, "break", "remove", "base", "forms", "pour", "screed", "float", "edge", "joint", "broom");
            Assert.IsTrue(sw.IsFixed);
            Assert.AreEqual(SidewalkDefect.Bags, sw.S.used.Find(u => u.id == RepairCatalog.Concrete80).n, "14 bags of 80-lb mix for 0.31 cu yd");
            yield return Frames(1);
            Shot(sw, wc, Vector3.zero, "repair_13_sidewalk_poured");
            Do(sw, "strip");
            Assert.Greater(sw.Quality, 0.95f);
            Bill(job2);
        }

        [UnityTest]
        public IEnumerator SloppyJob_ProducesCallback()
        {
            var lot = Lot(7);
            var job = Accept(lot, (RepairType.Caulk, HouseSpot.TubCaulk, 0), (RepairType.StickingDoor, HouseSpot.InteriorDoor, StickingDoorDefect.LooseHinge));
            var tub = Find(job, RepairType.Caulk);
            var door = Find(job, RepairType.StickingDoor);
            // caulk over the old bead with the wrong caulk; plane a door that only needed hinge screws and never rehang-test it
            Do(tub, "bead-acr");
            Assert.IsTrue(tub.IsFixed);
            Assert.Less(tub.Quality, 0.6f);
            Do(door, "pins", "plane", "rehang");
            Assert.IsFalse(door.IsFixed, "wrong repair does not fix a sagging door");
            Assert.IsTrue(door.WrongRepair);
            int rep = Career.Reputation;
            var inv = Bill(job);
            Assert.IsTrue(inv.willCallBack);
            Assert.Less(inv.satisfaction, 0.6f);
            Assert.AreEqual(0, inv.tip);
            Assert.AreEqual(1, RepairJobs.PendingCallbacks);
            Assert.LessOrEqual(Career.Reputation, rep, "unhappy customer does not raise reputation");

            int day = GameClock.I != null ? GameClock.I.Day : 1;
            RepairJobs.ProcessDay(day + 3);
            var cb = Company.Offers.Find(j => j.Title.StartsWith("Callback"));
            Assert.NotNull(cb, "callback job offered");
            Assert.AreEqual(0, cb.Payout);
            Assert.AreEqual(lot.Address, cb.Address);
            Assert.AreEqual(0, RepairJobs.PendingCallbacks);
            Assert.AreEqual(2, RepairJobs.Payload(cb).defects.Count);

            // do the callback properly: no charge, no further callback
            Assert.IsTrue(Company.Accept(cb, out var msg), msg);
            tub = Find(cb, RepairType.Caulk); door = Find(cb, RepairType.StickingDoor);
            Do(tub, "cut", "clean", "fill", "bead-sil", "tool");
            Do(door, "check", "hinge", "check");
            Assert.IsTrue(RepairJobs.AllFixed(cb));
            int before = Game.Wallet.Money;
            var inv2 = RepairJobs.SendInvoice(cb);
            Assert.AreEqual(before, Game.Wallet.Money, "callbacks are free");
            Assert.IsFalse(inv2.willCallBack);
            Assert.AreEqual(2, RepairJobs.Invoices.Count);
            yield return null;
        }
    }
}
