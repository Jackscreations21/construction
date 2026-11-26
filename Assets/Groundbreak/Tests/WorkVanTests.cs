using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Work van: spawn + real size, driving to a job with the autopilot inputs, getting out, fast travel,
    /// cargo limits, the extension ladder, and the save round trip.</summary>
    public class WorkVanTests
    {
        int _money, _day; GameMode _mode; float _hours, _timeScale; Vector3 _playerPos; bool _chase;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(3);
            _money = Game.Wallet.Money; _mode = GameSettings.Mode; _timeScale = Time.timeScale;
            if (GameClock.I != null) { _hours = GameClock.I.Hours; _day = GameClock.I.Day; }
            if (Game.Player != null) _playerPos = Game.Player.transform.position;
            GameSettings.Mode = GameMode.Career;
            HandymanStart.Begin();
            WorkVan.EnsureSpawned();
            VanCargo.Clear();
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            yield return Frames(3);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _timeScale;
            if (WorkVan.I != null) { WorkVan.I.ClearAutopilot(); WorkVan.I.ChaseCamera = _chase; }
            WorkVan.Despawn();                      // also puts a driving player back on their feet
            TownLadder.ResetState();
            Town.Destroy();
            Company.ResetState();
            VanCargo.Clear();
            VanCargo.UnregisterItem("van-test-bag"); VanCargo.UnregisterItem("van-test-foam");
            Game.Wallet = new Wallet(_money);
            GameSettings.Mode = _mode;
            if (GameClock.I != null) { GameClock.I.Hours = _hours; GameClock.I.Day = _day; }
            var p = Game.Player;
            if (p != null)
            {
                var cc = p.GetComponent<CharacterController>();
                cc.enabled = false; p.transform.position = _playerPos; cc.enabled = true;
                p.enabled = true;
            }
            yield return Frames(2);
            Assert.IsNull(WorkVan.I, "van left behind after teardown");
        }

        static Bounds VanBounds(WorkVan van)
        {
            var rs = van.GetComponentsInChildren<MeshRenderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        [UnityTest]
        public IEnumerator SpawnsInHomeDrivewayAtRealSize()
        {
            var van = WorkVan.I;
            Assert.NotNull(van, "van did not spawn");
            Assert.IsTrue(Company.Owns("van"));
            var home = Town.Home;
            Assert.Less(Flat(van.transform.position, home.Parking), 0.3f, "not in the home driveway");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(van.Yaw, home.ParkingYaw)), 1f);
            Assert.IsTrue(van.LadderOnRack);

            // parked nose to the house on Main St: the van's length runs along world Z
            var b = VanBounds(van);
            Assert.That(b.size.z, Is.InRange(5.8f, 6.2f), "length should be about 5.99 m");
            Assert.That(b.size.x, Is.InRange(2.0f, 2.5f), "width should be 2.03 m body plus mirrors");
            Assert.That(b.size.y, Is.InRange(2.1f, 2.6f), "height with rack and ladder");
            Assert.Less(Mathf.Abs(b.min.y - van.transform.position.y), 0.05f, "tyres should sit on the ground");

            Vector3 p = van.transform.position, f = van.transform.forward, r = van.transform.right;
            SiteLookTests.Shot(p + f * 6.5f - r * 4.5f + Vector3.up * 1.9f, p + Vector3.up * 1.1f, "van_01_driveway");

            // some stock, rear doors open
            Assert.IsTrue(VanCargo.Add("2x4x8", 10));
            Assert.IsTrue(VanCargo.Add("ply34", 6));
            Assert.IsTrue(VanCargo.Add("mortar-s80", 30));
            Assert.IsTrue(VanCargo.Add("2x6x12", 3));
            van.SetRearDoors(true, true);
            yield return Frames(2);
            Assert.IsTrue(van.RearOpen);
            SiteLookTests.Shot(p - f * 6.2f - r * 1.3f + Vector3.up * 1.75f, p - f * 1.5f + Vector3.up * 1.15f, "van_02_rear_open_cargo");
            van.SetRearDoors(false, true);

            // aiming at the cab from beside the driver's door finds the cab; from behind, the rear doors
            Vector3 eye = p - r * 2.5f + f * 0.9f + Vector3.up * 1.6f;
            Assert.AreEqual(WorkVan.Part.Cab, van.AimPart(new Ray(eye, r), out float d1));
            Assert.Less(d1, 3f);
            Assert.AreEqual(WorkVan.Part.RearDoors, van.AimPart(new Ray(p - f * 5f + Vector3.up * 1.4f, f), out _));

            // driver's seat
            _chase = van.ChaseCamera; van.ChaseCamera = false;
            van.Enter();
            yield return Frames(3);
            Assert.IsTrue(van.Driving);
            Assert.IsFalse(Game.Player.enabled, "on-foot rig should be off while driving");
            Assert.IsFalse(Game.Player.GetComponent<CharacterController>().enabled);
            Assert.IsFalse(VanCargo.InReach(van.transform.position), "cargo is not in reach while the van is being driven");
            var cam = Game.Player.Cam.transform;
            Assert.Less(Vector3.Distance(cam.position, van.transform.TransformPoint(new Vector3(-0.48f, 1.68f, 0.92f))), 0.05f);
            SiteLookTests.Shot(cam.position, cam.position + cam.forward * 10f - Vector3.up * 1.2f, "van_03_driver_view", 75f);
            van.Exit();
            Assert.IsFalse(van.Driving);
        }

        // ------------------------------------------------------------------ driving

        struct Wp { public Vector3 P; public bool Rev, Slow; }
        static Wp W(float x, float z, bool rev, bool slow) => new Wp { P = new Vector3(x, 0f, z), Rev = rev, Slow = slow };

        float _travelled, _maxStep;

        /// <summary>Tiny waypoint follower on the public driving inputs (throttle / steer / brake).</summary>
        IEnumerator Follow(WorkVan van, List<Wp> path, float timeout)
        {
            int i = 0; float t0 = Time.time; Vector3 last = van.transform.position;
            while (i < path.Count && Time.time - t0 < timeout)
            {
                Vector3 pos = van.transform.position;
                float step = Flat(pos, last); _travelled += step; _maxStep = Mathf.Max(_maxStep, step); last = pos;
                var wp = path[i];
                Vector3 to = wp.P - pos; to.y = 0f;
                float dist = to.magnitude;
                Vector3 fwd = van.transform.forward; fwd.y = 0f;
                float along = Vector3.Dot(to, wp.Rev ? -fwd : fwd);
                if (dist < (i == path.Count - 1 ? 1.0f : 2.0f) || (along < 0f && dist < 4.5f)) { i++; continue; }

                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, speed = van.Speed;
                float target = wp.Rev ? 2.2f : wp.Slow ? 3.2f : 7f;
                float throttle, steer; bool brake;
                if (!wp.Rev)
                {
                    steer = Mathf.Clamp(Mathf.DeltaAngle(van.Yaw, bearing) / 20f, -1f, 1f);
                    throttle = speed < -0.1f ? 1f : speed < target ? 0.7f : 0f;
                    brake = speed > target + 1.2f;
                }
                else
                {
                    // backing up: the tail follows the bearing, so the wheel turns the other way
                    steer = Mathf.Clamp(-Mathf.DeltaAngle(van.Yaw + 180f, bearing) / 20f, -1f, 1f);
                    throttle = speed > 0.1f ? -1f : -speed < target ? -0.8f : 0f;
                    brake = -speed > target + 1f;
                }
                van.SetAutopilot(throttle, steer, brake);
                yield return null;
            }
            van.SetAutopilot(0f, 0f, true);
            for (float t = Time.time; Mathf.Abs(van.Speed) > 0.05f && Time.time - t < 5f;) yield return null;
            Assert.AreEqual(path.Count, i, "the van did not get through its route in time (stuck at waypoint " + i + " of " + path.Count + ")");
        }

        [UnityTest]
        public IEnumerator DrivesToAJobAndGetsOut()
        {
            var van = WorkVan.I;
            Assert.NotNull(van);
            var home = Town.Home; var job = Town.Find("main-104");
            Assert.NotNull(job);
            Vector3 hp = home.Parking, tp = job.Parking;
            const float R = 7.5f;
            float lane = Town.MainZ + 2.3f, zc = lane - R;      // westbound lane of Main St; turn centres

            var path = new List<Wp>();
            // back out of the driveway, swinging the tail east so the van ends up facing west in its lane
            path.Add(W(hp.x, zc, true, true));
            for (int k = 1; k <= 5; k++) { float a = (180f - 18f * k) * Mathf.Deg2Rad; path.Add(W(hp.x + R + R * Mathf.Cos(a), zc + R * Mathf.Sin(a), true, true)); }
            path.Add(W(hp.x + R + 3f, lane, true, true));
            // west along Main St, left turn into the customer's driveway, up to the parking spot
            path.Add(W(hp.x - 4f, lane, false, false));
            path.Add(W(tp.x + R + 5f, lane, false, false));
            for (int k = 0; k <= 5; k++) { float a = (90f + 18f * k) * Mathf.Deg2Rad; path.Add(W(tp.x + R + R * Mathf.Cos(a), zc + R * Mathf.Sin(a), false, true)); }
            path.Add(W(tp.x, zc - 3f, false, true));
            path.Add(W(tp.x, tp.z, false, true));

            van.Enter();
            yield return Frames(2);
            Assert.IsTrue(van.Driving);
            Time.timeScale = 3f;
            _travelled = 0f; _maxStep = 0f;
            yield return Follow(van, path, 150f);
            Time.timeScale = _timeScale;
            van.ClearAutopilot();
            yield return Frames(3);

            Assert.Less(Flat(van.transform.position, tp), 4f, "van should end in the customer's driveway");
            Assert.Greater(_travelled, 50f, "the van should have driven there");
            Assert.Less(_maxStep, 4f, "the van jumped (teleported) on the way");
            Assert.IsTrue(job.Contains(van.transform.position), "van is not on the job lot");
            Vector3 p = van.transform.position;
            SiteLookTests.Shot(p + new Vector3(-5.5f, 2.0f, 8.5f), p + Vector3.up * 1.0f, "van_04_parked_at_job");

            // get out: on the ground beside the van, walking again
            van.Exit();
            yield return Frames(5);
            Assert.IsFalse(van.Driving);
            var player = Game.Player;
            Assert.IsTrue(player.enabled, "PlayerRig back on");
            Assert.IsTrue(player.GetComponent<CharacterController>().enabled, "CharacterController back on");
            Assert.IsTrue(player.GetComponent<ToolBelt>().enabled, "ToolBelt back on");
            float side = Flat(player.transform.position, p);
            Assert.That(side, Is.InRange(1.2f, 4.5f), "player should stand beside the van");
            Assert.Less(Mathf.Abs(player.transform.position.y - p.y), 0.6f, "player should be on the ground");
            Assert.IsTrue(VanCargo.InReach(player.transform.position));
        }

        [UnityTest]
        public IEnumerator FastTravelMovesVanAndCostsDriveTime()
        {
            var van = WorkVan.I;
            Assert.NotNull(van);
            var lot = Town.Find("cedar-105");
            Assert.NotNull(lot);
            var clock = GameClock.I;
            Assert.NotNull(clock);
            clock.Hours = 9f; int day = clock.Day;
            float meters = WorkVan.RoadDistance(van.transform.position, lot.Parking);
            Assert.Greater(meters, 100f);
            float minutes = WorkVan.DriveGameMinutes(meters);
            Assert.AreEqual(meters / 11.176f * 1.2f, minutes, 0.05f, "25 mph, 1.2 game minutes per real second");

            Assert.IsTrue(WorkVan.FastTravel(lot));
            yield return Frames(3);
            Assert.Less(Flat(van.transform.position, lot.Parking), 0.3f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(van.Yaw, lot.ParkingYaw)), 1f);
            float passed = (clock.Day - day) * 24f * 60f + (clock.Hours - 9f) * 60f;
            Assert.That(passed, Is.InRange(minutes - 0.01f, minutes + 1f), "clock should advance by the drive time");
            Assert.IsFalse(van.Driving);
            Assert.That(Flat(Game.Player.transform.position, van.transform.position), Is.InRange(1.2f, 4.5f), "player should arrive beside the van");
            StringAssert.Contains(lot.Address, Game.ToastText);

            // midnight rollover
            clock.Hours = 23.99f; day = clock.Day;
            Assert.IsTrue(WorkVan.FastTravel(Town.Home));
            Assert.AreEqual(day + 1, clock.Day);
            Assert.Less(clock.Hours, 1f);

            // still at the wheel when fast travelling while driving
            van.Enter();
            Assert.IsTrue(WorkVan.FastTravel(Town.Store));
            yield return Frames(2);
            Assert.IsTrue(van.Driving);
            Assert.IsTrue(VanApps.AtStore);
            van.Exit();
            Assert.IsFalse(van.Driving);
        }

        [UnityTest]
        public IEnumerator CargoLimits()
        {
            int changes = 0;
            System.Action onChange = () => changes++;
            VanCargo.Changed += onChange;
            try
            {
                Assert.AreEqual(180f, VanCargo.CapacityCuFt); Assert.AreEqual(3200f, VanCargo.PayloadLb);

                // weight: 80 lb bags, 40 of them = payload
                VanCargo.RegisterItem("van-test-bag", 80f, 0.6f);
                Assert.IsTrue(VanCargo.IsRegistered("van-test-bag"));
                Assert.IsTrue(VanCargo.Add("van-test-bag", 40));
                Assert.AreEqual(3200f, VanCargo.UsedLb, 0.5f);
                Assert.IsFalse(VanCargo.CanAdd("van-test-bag", 1, out string why));
                StringAssert.Contains("heavy", why);
                Assert.IsFalse(VanCargo.Add("van-test-bag", 1));
                Assert.AreEqual(40, VanCargo.Count("van-test-bag"));
                Assert.Greater(changes, 0);

                // TryTake
                Assert.IsTrue(VanCargo.TryTake("van-test-bag", 38));
                Assert.IsFalse(VanCargo.TryTake("van-test-bag", 3));
                Assert.AreEqual(2, VanCargo.Count("van-test-bag"));
                VanCargo.Clear();
                Assert.AreEqual(0f, VanCargo.UsedLb);

                // volume: bulky light boxes, 10 cu ft each
                VanCargo.RegisterItem("van-test-foam", 1f, 10f);
                Assert.IsFalse(VanCargo.Add("van-test-foam", 19), "19 x 10 cu ft does not fit in 180");
                Assert.AreEqual(0, VanCargo.Count("van-test-foam"));
                Assert.IsTrue(VanCargo.Add("van-test-foam", 18));
                Assert.AreEqual(180f, VanCargo.UsedCuFt, 0.01f);
                Assert.IsFalse(VanCargo.CanAdd("2x4x8", 1, out why));
                StringAssert.Contains("room", why);
                VanCargo.Clear();

                // long stock rides on the roof rack: 6 pieces max, up to 16'
                Assert.IsFalse(VanCargo.RidesOnRack("2x4x10"), "10' stock fits inside");
                Assert.IsTrue(VanCargo.RidesOnRack("2x6x12"));
                Assert.IsTrue(VanCargo.Add("2x6x12", 6));
                Assert.AreEqual(6, VanCargo.RackPieces);
                Assert.AreEqual(0f, VanCargo.UsedCuFt, 0.001f, "rack stock takes no room inside");
                Assert.Greater(VanCargo.UsedLb, 80f);
                Assert.IsFalse(VanCargo.CanAdd("2x6x12", 1, out why));
                StringAssert.Contains("rack", why);
                VanCargo.Clear();
                Assert.IsFalse(VanCargo.CanAdd("rebar-4", 1, out why), "20' rebar is too long for the rack");
                StringAssert.Contains("16'", why);

                // sheets: 20 flat on the floor
                Assert.IsTrue(VanCargo.IsSheet("ply34"));
                Assert.IsTrue(VanCargo.Add("ply34", 20));
                Assert.AreEqual(20, VanCargo.SheetCount);
                Assert.IsFalse(VanCargo.CanAdd("osb716", 1, out why));
                StringAssert.Contains("sheets", why);
                Assert.IsTrue(VanCargo.Add("2x4x8", 10), "studs still fit beside the sheets");

                // estimates from the catalog: a 2x4x8 is about 10 lb, a sheet of 3/4 ply about 70 lb
                Assert.That(VanCargo.Spec("2x4x8").lb, Is.InRange(7f, 13f));
                Assert.That(VanCargo.Spec("ply34").lb, Is.InRange(55f, 85f));

                // reach
                Assert.NotNull(WorkVan.I);
                Assert.IsTrue(VanCargo.InReach(WorkVan.I.transform.position + new Vector3(20f, 0f, 20f)));
                Assert.IsFalse(VanCargo.InReach(WorkVan.I.transform.position + new Vector3(41f, 0f, 0f)));

                // starter kit
                bool plyInKit = false;
                foreach (var kv in VanCargo.StarterKit) if (kv.Key == "ply34") plyInKit = true;
                VanCargo.LoadStarterKit();
                if (!plyInKit) Assert.AreEqual(0, VanCargo.Count("ply34"), "LoadStarterKit starts from an empty van");

                // hardware store: only while parked at Hartley's
                VanCargo.Clear();
                Assert.IsFalse(VanApps.AtStore);
                Assert.IsTrue(WorkVan.FastTravel(Town.Store));
                Assert.IsTrue(VanApps.AtStore);
                int money = Game.Wallet.Money;
                VanApps.Buy("2x4x8", 5);
                Assert.AreEqual(5, VanCargo.Count("2x4x8"));
                Assert.AreEqual(money - 5 * Catalog.Get("2x4x8").Price, Game.Wallet.Money);
            }
            finally
            {
                VanCargo.Changed -= onChange;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator LadderStandsAtTheEaveAndCanBeClimbed()
        {
            var van = WorkVan.I;
            Assert.NotNull(van);
            var lot = Town.Find("main-104");
            var house = lot.House;
            Assert.NotNull(house, "customer house missing");
            Assert.IsTrue(house.Has(HouseSpot.LadderFront), "house has no LadderFront anchor");
            Assert.IsTrue(WorkVan.FastTravel(lot));
            var pose = house.Anchor(HouseSpot.LadderFront);
            Vector3 away = pose.rotation * Vector3.forward;

            var lad = TownLadder.Place(pose.position, away, house.EaveHeight);
            Assert.NotNull(lad);
            Assert.AreSame(lad, TownLadder.Placed);
            Assert.AreEqual(TownLadder.LadderState.Placed, TownLadder.State);
            Assert.IsFalse(van.LadderOnRack);
            Assert.AreEqual(90f - TownLadder.LeanDegrees, Vector3.Angle(lad.transform.up, Vector3.up), 0.5f, "4:1 lean");
            Assert.GreaterOrEqual(lad.Length + 0.01f, lad.TopHeight / Mathf.Sin(TownLadder.LeanDegrees * Mathf.Deg2Rad) + TownLadder.AboveEave, "3' above the eave");
            Assert.LessOrEqual(lad.Length, TownLadder.MaxLength + 0.001f);
            yield return Frames(2);
            Vector3 right = Vector3.Cross(Vector3.up, away);
            SiteLookTests.Shot(pose.position + away * 6.5f + right * 3.5f + Vector3.up * 1.7f, pose.position + Vector3.up * 1.6f, "van_05_ladder_on_house");

            // scripted climb
            HandymanStart.MovePlayer(pose.position + away * 0.5f, Quaternion.LookRotation(-away).eulerAngles.y);
            yield return Frames(3);
            lad.ClimbOverride = 1f;
            lad.Mount();
            Assert.IsTrue(lad.Climbing);
            float maxY = float.MinValue, t0 = Time.time;
            while (lad != null && lad.Climbing && Time.time - t0 < 20f)
            {
                maxY = Mathf.Max(maxY, Game.Player.transform.position.y);
                yield return null;
            }
            for (int i = 0; i < 3; i++) { maxY = Mathf.Max(maxY, Game.Player.transform.position.y); yield return null; }
            lad.ClimbOverride = 0f;
            Assert.GreaterOrEqual(maxY, pose.position.y + lad.TopHeight - 0.05f, "the climb should bring the player's feet up to the eave");
            Assert.IsFalse(lad.Climbing, "the player should have stepped off onto the roof");
            Assert.Greater(Game.Player.transform.position.y, pose.position.y + lad.TopHeight - 0.3f, "player should be standing on the roof");

            // pick it up and put it back on the rack
            Assert.IsTrue(TownLadder.Take());
            Assert.IsTrue(TownLadder.Carried);
            Assert.IsNull(TownLadder.Placed);
            TownLadder.Stow();
            Assert.IsTrue(van.LadderOnRack);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SaveRoundTripKeepsVanPoseAndCargo()
        {
            var van = WorkVan.I;
            Assert.NotNull(van);
            var lot = Town.Find("main-112");
            van.TeleportTo(lot.Parking, lot.ParkingYaw);
            Assert.IsTrue(VanCargo.Add("2x4x8", 12));
            Assert.IsTrue(VanCargo.Add("ply34", 3));
            Assert.IsTrue(VanCargo.Add("nails16d", 2000));
            yield return Frames(2);
            Vector3 saved = van.transform.position;

            var file = SaveSystem.Capture();
            van.TeleportTo(Town.Home.Parking, Town.Home.ParkingYaw);
            VanCargo.Clear();
            SaveSystem.Apply(file);
            yield return Frames(3);

            van = WorkVan.I;
            Assert.NotNull(van, "van missing after load");
            Assert.Less(Vector3.Distance(van.transform.position, saved), 0.3f, "van should be where it was saved");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(van.Yaw, lot.ParkingYaw)), 1f);
            Assert.AreEqual(12, VanCargo.Count("2x4x8"));
            Assert.AreEqual(3, VanCargo.Count("ply34"));
            Assert.AreEqual(2000, VanCargo.Count("nails16d"));
            Assert.IsTrue(van.LadderOnRack);
            Assert.AreEqual(1, Object.FindObjectsByType<WorkVan>(FindObjectsSortMode.None).Length, "exactly one van after load");

            // reset (new game) clears the cargo
            VanCargo.Clear();
            Assert.AreEqual(0f, VanCargo.UsedCuFt);
        }
    }
}
