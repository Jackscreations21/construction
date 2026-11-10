using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Groundbreak.Tests.FinishTestKit;

namespace Groundbreak.Tests
{
    /// <summary>Interior finish: prehung door + casing, base (coped/mitered), crown, LVP, tile, hardwood, kitchen run.</summary>
    public class InteriorFinishTests
    {
        static readonly string[] ToolIds = { "finish-nailer", "brad-nailer", "miter-saw", "coping-saw", "flooring-nailer", "tile-saw", "notched-trowel", "grout-float", "pull-bar", "fin-drill", "caulk-gun" };
        Func<FramePlane, Vector2, float> _provider;
        readonly List<GameObject> _lights = new List<GameObject>();

        static float I(float x) => Units.In(x);

        readonly HashSet<GameObject> _before = new HashSet<GameObject>();

        [SetUp]
        public void SetUp()
        {
            FinishStock.Unlimited = true;
            _before.Clear();
            foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) _before.Add(g);
        }

        [TearDown]
        public void TearDown()
        {
            if (_provider != null) FinishHooks.CoveringProviders.Remove(_provider);
            foreach (var l in _lights) if (l != null) UnityEngine.Object.Destroy(l);
            FinishStock.Unlimited = false;
            WallTrim.Verbose = false;
            CloseupInspection.ClearMarkers();
            // remove everything this test built (framing, finishes, cameras) so later tests start clean
            foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (!_before.Contains(g) && g.GetComponent<PlayerRig>() == null && g.name != "Systems") UnityEngine.Object.Destroy(g);
        }

        void Cover(Vector3 center, float radius)
        {
            _provider = (fp, uv) =>
            {
                if (!(fp.IsWall || fp.IsCeiling)) return 0f;
                return (fp.World(uv) - center).magnitude < radius ? I(0.5f) : 0f;
            };
            FinishHooks.CoveringProviders.Add(_provider);
        }

        static FramePlane Face(Vector3 from, Vector3 dir)
        {
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Raycast(from, dir, out var hit, 3f), "no hit from " + from + " dir " + dir);
            var fp = FinishAim.FaceBehind(hit);
            Assert.NotNull(fp, "no frame plane at " + hit.point + " (" + hit.collider.name + ")");
            return fp;
        }

        static Rect UV(FramePlane fp, Vector3 a, Vector3 b)
        {
            var p = fp.ToUV(a); var q = fp.ToUV(b);
            return Rect.MinMaxRect(Mathf.Min(p.x, q.x), Mathf.Min(p.y, q.y), Mathf.Max(p.x, q.x), Mathf.Max(p.y, q.y));
        }

        /// <summary>Stand-in 1/2" drywall (taped + painted) on a framed face.</summary>
        static GameObject Drywall(FramePlane fp, Rect r, params Rect[] holes)
        {
            var mb = new MeshBuilder(); mb.MinSubmeshCount = 1;
            PanelMesh.AddSlab(mb, r, holes, -I(0.5f), I(0.5f), 0, 0, 0);
            return PanelMesh.Spawn(fp, "TestDrywall", mb.Build("TestDrywall"), new[] { FinishMaterials.Get(InteriorMaterials.WallFace) }, null, true, "test-drywall");
        }

        void Light(Vector3 p, float intensity = 1.1f, float range = 7f)
        {
            var go = new GameObject("TestRoomLight");
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = range; l.intensity = intensity; l.color = new Color(1f, 0.95f, 0.86f); l.shadows = LightShadows.Soft;
            go.transform.position = p; _lights.Add(go);
        }

        [UnityTest]
        public IEnumerator A_FinishedRoomCorner()
        {
            yield return Frames(5);
            if (GameClock.I != null) GameClock.I.Hours = 11f;
            foreach (var id in ToolIds) Assert.NotNull(FinishStock.Give(id), "tool " + id);
            var O = new Vector3(60f, 0.6f, 60f); // first floor deck above grade
            Vector3 W(float xIn, float yIn, float zIn) => O + new Vector3(I(xIn), I(yIn), I(zIn));
            Cover(W(72, 48, 40), 9f);

            // ---- framing: subfloor, wall A (door), wall B (inside corner), short partition C (outside corner), ceiling joists
            Subfloor(W(0, 0, -72), 192f, 240f);
            StudWall(O, Vector3.right, Vector3.forward, 144f, new Opening { LeftIn = 84f, WidthIn = 34f, SillIn = 0f, HeadIn = 82.5f });
            StudWall(W(0, 0, 3.5f), Vector3.forward, Vector3.right, 140.5f);
            StudWall(W(48, 0, 3.5f), Vector3.forward, Vector3.right, 36f);
            CeilingJoists(O, Vector3.right, Vector3.forward, 144f, 144f);
            yield return Frames(3);
            Physics.SyncTransforms();

            var aRoom = Face(W(32, 40, 30), Vector3.back);
            var aHall = Face(W(32, 40, -30), Vector3.forward);
            var bRoom = Face(W(30, 40, 35.5f), Vector3.left);
            var cWest = Face(W(30, 40, 19.5f), Vector3.right);
            var cEast = Face(W(70, 40, 19.5f), Vector3.left);
            var cEnd = Face(W(49.75f, 40, 70), Vector3.back);
            var ceil = Face(W(32f, 40, 30), Vector3.up);
            var floor = Face(W(30, 30, 30), Vector3.down);
            Assert.IsTrue(aRoom.IsWall && bRoom.IsWall && cEnd.IsWall, "walls");
            Assert.IsTrue(ceil.IsCeiling, "ceiling " + ceil);
            Assert.IsTrue(floor.IsFloor, "floor " + floor);
            var ros = aRoom.Openings();
            Assert.AreEqual(1, ros.Count, "door RO");
            var ro = ros[0];

            // ---- stand-in drywall + ceiling (another trade owns drywall)
            float top = 96.625f;
            Drywall(aRoom, UV(aRoom, W(3.5f, 0.5f, 3.5f), W(144, top, 3.5f)), UV(aRoom, W(84, 0, 3.5f), W(118, 82.5f, 3.5f)), UV(aRoom, W(48, 0, 3.5f), W(51.5f, top, 3.5f)));
            Drywall(aHall, UV(aHall, W(0, 0.5f, 0), W(144, top, 0)), UV(aHall, W(84, 0, 0), W(118, 82.5f, 0)));
            Drywall(bRoom, UV(bRoom, W(3.5f, 0.5f, 4f), W(3.5f, top, 144)));
            Drywall(cWest, UV(cWest, W(48, 0.5f, 4f), W(48, top, 39.5f)));
            Drywall(cEast, UV(cEast, W(51.5f, 0.5f, 4f), W(51.5f, top, 39.5f)));
            Drywall(cEnd, UV(cEnd, W(47.5f, 0.5f, 39.5f), W(52f, top, 39.5f)));
            Drywall(ceil, UV(ceil, W(0, 97.125f, 0), W(144, 97.125f, 144)));
            yield return Frames(2);
            Physics.SyncTransforms();

            // ---- 1. hang the prehung door, swinging into the room
            var door = DoorHanging.Hang(aRoom, ro, "door-int-6p32", true);
            Assert.NotNull(door, "door hung");
            Assert.AreEqual(4.5625f, Units.ToInches(door.JambWidth), 0.02f, "4-9/16 jamb");
            Assert.Greater(door.Shims, 4); Assert.Greater(door.JambNails, 8);
            Assert.AreEqual(0f, door.PlumbError, 1e-5f);
            yield return Frames(2);
            Physics.SyncTransforms();
            Light(W(72, 88, 60), 0.55f, 7f); Light(W(30, 88, 100), 0.45f, 6f); Light(W(100, 88, -36), 0.5f, 6f);
            var camDoorBare = Cam(W(101, 62, 60), W(101, 44, 3.5f));
            Shot(camDoorBare, "fin_01_door_hung_shims");

            // ---- 2. kitchen run on wall B: base + wall cabinets, quartz top, sink, subway backsplash
            float startU = bRoom.ToUV(W(3.5f, 0, 60)).x;
            var run = Cabinets.InstallRun(bRoom, startU, new[] { 18, 36, 24 }, new[] { 18, 36, 24 }, CabFinish.PaintedWhite, 0f, 1, true, true);
            Assert.AreEqual(6, run.Count, "cabinets");
            foreach (var c in run) Assert.GreaterOrEqual(c.ScrewsInStuds, 1, c.name + " screwed to studs");
            var ct = Countertops.Install(run, CounterType.Quartz);
            Assert.NotNull(ct); Assert.IsTrue(ct.HasSink, "sink cutout");
            Assert.AreEqual(1f, Units.ToInches(ct.FrontOverhang), 0.05f, "1\" overhang");
            var bs = Countertops.InstallBacksplash(ct, "Bright white");
            Assert.NotNull(bs); Assert.Greater(bs.Tiles, 60);
            Assert.AreEqual(18f, Units.ToInches(bs.Height), 0.6f, "backsplash to the wall cabinets");
            yield return Frames(2);
            Physics.SyncTransforms();

            // ---- 3. tile in the kitchen (backer, thinset, set with spacers, cure, grout, haze)
            var tileArea = UV(floor, W(4, 0, 60), W(76, 0, 140));
            var tile = TileWork.Layout(floor, tileArea);
            Assert.NotNull(tile);
            Assert.IsTrue(TileWork.Backer(tile)); Assert.IsTrue(TileWork.Trowel(tile)); Assert.IsTrue(TileWork.Set(tile));
            Assert.IsFalse(TileWork.Grout(tile), "grout must wait for the 24 h cure");
            yield return Frames(2);
            Shot(Cam(W(70, 40, 120), W(50, 0, 96)), "fin_04_tile_spacers");
            FinishTime.Skip(24f);
            Assert.IsTrue(TileWork.Grout(tile, "Delorean gray"));
            FinishTime.Skip(0.3f);
            Assert.IsTrue(TileWork.WipeHaze(tile));
            Assert.AreEqual(TileFloor.Stage.Clean, tile.Step);
            Assert.Greater(tile.Cuts, 0, "edge tiles cut on the wet saw");
            if (GameClock.I != null) GameClock.I.Hours = 11f;

            // ---- 4. hardwood in the hall (perpendicular to joists), 5. LVP in the room
            var hw = Hardwood.Install(floor, UV(floor, W(0, 0, -60), W(144, 0, -0.5f)));
            Assert.NotNull(hw); Assert.Greater(hw.Nails, 100); Assert.Greater(hw.FaceNails, 10);
            Assert.GreaterOrEqual(hw.MinStagger, I(6f) - 1e-4f, "hardwood stagger");
            yield return Frames(1);
            Physics.SyncTransforms();
            var lvp = Lvp.Install(floor, UV(floor, W(4, 0, 4), W(140, 0, 140)));
            Assert.NotNull(lvp);
            Assert.GreaterOrEqual(lvp.MinStagger, I(6f) - 1e-4f, "LVP stagger ≥ 6\"");
            Assert.AreEqual(0.25f, Units.ToInches(lvp.Gap), 0.01f);
            Assert.IsTrue(Flooring.TopAt(W(100, 0, 80), out float lvpTop) && lvpTop > 0.005f, "LVP top");
            yield return Frames(2);
            Physics.SyncTransforms();

            // ---- 6. casing both sides, 7. base (cope + miters), 8. crown
            var cas = Casing.CaseDoor(door);
            Assert.AreEqual(6, cas.Count, "casing pieces both sides");
            yield return Frames(1); Physics.SyncTransforms();
            WallTrim.Verbose = true;
            var bases = new List<TrimPiece>();
            foreach (var f in new[] { aRoom, bRoom, cWest, cEast, cEnd, aHall }) { bases.AddRange(WallTrim.RunBase(f)); yield return null; Physics.SyncTransforms(); }
            Debug.Log("[InteriorFinish] base pieces " + bases.Count + ": " + string.Join(", ", bases.ConvertAll(b => $"{b.StartCut}/{b.EndCut} {Units.Format(b.Length)}")));
            Assert.GreaterOrEqual(bases.Count, 10, "base pieces on every face");
            Assert.IsTrue(bases.Exists(b => b.StartCut == TrimCut.Cope || b.EndCut == TrimCut.Cope), "a coped inside corner");
            Assert.IsTrue(bases.Exists(b => b.StartCut == TrimCut.OutsideMiter || b.EndCut == TrimCut.OutsideMiter), "a mitered outside corner");
            Assert.IsFalse(bases.Exists(b => b.StartCut == TrimCut.InsideMiter || b.EndCut == TrimCut.InsideMiter), "no inside miters");
            var crowns = new List<TrimPiece>();
            foreach (var f in new[] { aRoom, bRoom, cWest, cEast, cEnd }) { crowns.AddRange(WallTrim.RunCrown(f)); yield return null; Physics.SyncTransforms(); }
            Debug.Log("[InteriorFinish] crown pieces " + crowns.Count + ": " + string.Join(", ", crowns.ConvertAll(b => $"{b.StartCut}/{b.EndCut} {Units.Format(b.Length)}")));
            Assert.GreaterOrEqual(crowns.Count, 6, "crown pieces on every face");
            Assert.IsTrue(crowns.Exists(b => b.StartCut == TrimCut.Cope || b.EndCut == TrimCut.Cope), "coped crown");
            Assert.Greater(WallTrim.NailAll(), 50, "nailed off");
            Assert.Greater(WallTrim.CaulkAll(), 20f, "caulked");
            Debug.Log($"[InteriorFinish] trim sticks used {TrimStock.SticksUsed}, saw cuts {TrimStock.SawCuts}");
            yield return Frames(3);

            // ---- inspection
            var res = CloseupInspection.Run(CloseupInspection.Finish);
            foreach (var v in res.Violations) Debug.Log("[InteriorFinish] violation " + v.RuleId + ": " + v.Message);
            Assert.IsTrue(res.Passed, res.ToString());

            // ---- screenshots
            if (GameClock.I != null) GameClock.I.Hours = 11f;
            yield return Frames(2);
            Shot(Cam(W(98, 62, 112), W(101, 44, 3.5f), 55f), "fin_02_door_casing_room");
            Shot(Cam(W(104, 62, -104), W(101, 44, 0), 55f), "fin_03_door_casing_hall");
            Shot(Cam(W(22, 14, 22), W(4, 2, 4), 50f), "fin_05_base_coped_corner");
            Shot(Cam(W(22, 82, 22), W(4, 95, 4), 50f), "fin_06_crown_coped_corner");
            Shot(Cam(W(66, 16, 58), W(51, 3, 40), 50f), "fin_07_base_outside_corner");
            Shot(Cam(W(66, 84, 60), W(51, 94, 40), 50f), "fin_08_crown_outside_corner");
            Shot(Cam(W(49.75f, 70, 84), W(49.75f, 90, 40), 50f), "fin_08b_crown_end_front");
            Shot(Cam(W(110, 40, 95), W(100, 0, 60), 55f), "fin_09_lvp");
            Shot(Cam(W(70, 34, 120), W(50, 0, 98), 55f), "fin_10_tile_grouted");
            Shot(Cam(W(30, 36, -44), W(60, 0, -24), 55f), "fin_11_hardwood");
            Shot(Cam(W(124, 64, 104), W(10, 46, 96), 60f), "fin_12_kitchen");
            Shot(Cam(W(138, 70, 138), W(30, 40, 20), 65f), "fin_13_room_overview");
        }

        [UnityTest]
        public IEnumerator B_InspectorFlagsBadWork()
        {
            yield return Frames(3);
            foreach (var id in ToolIds) FinishStock.Give(id);
            // no coping saw this time
            Game.Belt.Owned.RemoveAll(t => t != null && t.Id == "coping-saw");
            var O = new Vector3(140f, 0.6f, 60f);
            Vector3 W(float xIn, float yIn, float zIn) => O + new Vector3(I(xIn), I(yIn), I(zIn));
            Cover(W(48, 48, 40), 6f);
            Subfloor(W(0, 0, -24), 120f, 120f);
            StudWall(O, Vector3.right, Vector3.forward, 96f, new Opening { LeftIn = 40f, WidthIn = 34f, SillIn = 0f, HeadIn = 82.5f });
            StudWall(W(0, 0, 3.5f), Vector3.forward, Vector3.right, 92.5f);
            yield return Frames(2);
            Physics.SyncTransforms();
            var a = Face(W(32, 40, 30), Vector3.back);
            var b = Face(W(30, 40, 35.5f), Vector3.left);
            var floor = Face(W(30, 30, 30), Vector3.down);
            Drywall(a, UV(a, W(3.5f, 0.5f, 3.5f), W(96, 96.625f, 3.5f)), UV(a, W(40, 0, 3.5f), W(74, 82.5f, 3.5f)));
            Drywall(b, UV(b, W(3.5f, 0.5f, 4f), W(3.5f, 96.625f, 96)));
            yield return Frames(1); Physics.SyncTransforms();
            var door = DoorHanging.Hang(a, a.Openings()[0], "door-int-6p32", true, shim: false);
            Assert.NotNull(door);
            Assert.Greater(Mathf.Abs(door.PlumbError), I(0.1f));
            yield return Frames(1); Physics.SyncTransforms();
            var lvp = Lvp.Install(floor, UV(floor, W(4, 0, 4), W(90, 0, 90)), null, "gray", I(0.125f));
            Assert.NotNull(lvp);
            var b1 = WallTrim.RunBase(a);
            var b2 = WallTrim.RunBase(b);
            Assert.IsTrue(b2.Exists(t => t.StartCut == TrimCut.InsideMiter || t.EndCut == TrimCut.InsideMiter), "no coping saw -> inside miter");
            var res = CloseupInspection.Run(CloseupInspection.Finish);
            var ids = new HashSet<string>(); foreach (var v in res.Violations) ids.Add(v.RuleId);
            Debug.Log("[InteriorFinish] flagged: " + string.Join(", ", ids));
            Assert.IsTrue(ids.Contains("fin-coped-corners"), "inside miter flagged");
            Assert.IsTrue(ids.Contains("fin-door-plumb-swing"), "out-of-plumb door flagged");
            Assert.IsTrue(ids.Contains("fin-floor-gap"), "1/8\" gap flagged");
            Assert.IsTrue(ids.Contains("fin-casing-reveal"), "uncased door flagged");
            Assert.IsTrue(ids.Contains("fin-trim-nailed"), "un-nailed trim flagged");
            Assert.Greater(CloseupInspection.MarkerCount(CloseupInspection.Finish), 3, "red markers");
            // fix the corner with the coping saw
            FinishStock.Give("coping-saw");
            var bad = b2.Find(t => t.StartCut == TrimCut.InsideMiter || t.EndCut == TrimCut.InsideMiter);
            Assert.IsTrue(CopingSaw.CopeEnd(bad, bad.StartCut == TrimCut.InsideMiter));
            Assert.IsFalse(b1.Exists(t => t.StartCut == TrimCut.InsideMiter || t.EndCut == TrimCut.InsideMiter), "mate back to a butt");
            yield return Frames(2);
            Shot(Cam(W(30, 60, 60), W(4, 20, 4), 60f), "fin_14_inspection_flags");
            CloseupInspection.ClearMarkers();
        }
    }
}
