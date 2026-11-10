using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Milestone 6 + platform: save/load, inspections, career payout, landscaping. Screenshots → Logs/shots/.</summary>
    public class SiteCareerTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Camera cam, string name, bool withGui = false)
        {
            var rt = new RenderTexture(1280, 720, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = prev; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        static Camera Cam(Vector3 pos, Vector3 look)
        {
            var cam = new GameObject("TestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = 60;
            return cam;
        }

        static void Daylight()
        {
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            Weather.ForceRain = false;
        }

        [TearDown]
        public void Cleanup()
        {
            Inspections.RuleFilter = null; Site.Lot = null; InspectorNpc.SpeedMultiplier = 1f;
            GameSettings.Mode = GameMode.FreeBuild; Weather.ForceRain = null;
        }

        // ------------------------------------------------------------------ save / load

        [UnityTest]
        public IEnumerator SaveLoadRoundTrip()
        {
            yield return Frames(5);
            Assert.NotNull(Game.Belt);
            var def = Catalog.Get("2x4x8");
            var a = Piece.Create(def, Units.In(60), new Vector3(200f, 0.5f, 200f), Quaternion.Euler(0, 45, 0));
            a.Fasten(); a.Nails = 3;
            var b = Piece.Create(Catalog.Get("osb716"), 0, new Vector3(201f, 0.2f, 200f), Quaternion.identity);
            Game.Stock.Add("mulch", 3);
            Game.Snap.GridIndex = 5; Game.Snap.AngleIndex = 2;
            GameClock.I.Day = 4; GameClock.I.Hours = 13.25f;
            string extState = "hello-42"; string loaded = null;
            SaveSystem.RegisterExtension("test-ext", () => extState, s => loaded = s);
            Grade.Create(180f, 180f, 60);
            Grade.Field.Raise(new Vector3(200f, 0, 205f), 2f, 0.4f);
            float h = Grade.HeightAt(200f, 205f);
            var tree = Landscape.MarkPlant("shrub-boxwood", new Vector3(203f, 0, 203f));
            tree.Stage = 2;

            int pieces = Object.FindObjectsByType<Piece>(FindObjectsSortMode.None).Length;
            int money = Game.Wallet.Money, mulch = Game.Stock.Count("mulch");
            Assert.IsTrue(SaveSystem.Save("test_roundtrip"), SaveSystem.LastMessage);
            Assert.IsTrue(File.Exists(SaveSystem.PathFor("test_roundtrip")));

            // mess everything up
            Object.Destroy(a.gameObject); Object.Destroy(b.gameObject);
            Game.Wallet.TrySpend(100, "test"); Game.Stock.TryTake("mulch", 2);
            Game.Snap.GridIndex = 1; GameClock.I.Day = 9; extState = "changed";
            Grade.Destroy();
            yield return Frames(2);

            Assert.IsTrue(SaveSystem.Load("test_roundtrip"), SaveSystem.LastMessage);
            yield return Frames(3);
            var all = Object.FindObjectsByType<Piece>(FindObjectsSortMode.None);
            Assert.AreEqual(pieces, all.Length, "piece count after load");
            Piece ra = null;
            foreach (var p in all) if (Vector3.Distance(p.transform.position, new Vector3(200f, 0.5f, 200f)) < 0.01f) ra = p;
            Assert.NotNull(ra, "fastened 2x4 not restored at its position");
            Assert.AreEqual(PieceState.Fastened, ra.State);
            Assert.AreEqual(3, ra.Nails);
            Assert.AreEqual(Units.In(60), ra.Size.z, 1e-4f, "cut length restored");
            Assert.AreEqual(45f, ra.transform.eulerAngles.y, 0.1f);
            Assert.AreEqual(money, Game.Wallet.Money);
            Assert.AreEqual(mulch, Game.Stock.Count("mulch"));
            Assert.AreEqual(5, Game.Snap.GridIndex); Assert.AreEqual(2, Game.Snap.AngleIndex);
            Assert.AreEqual(4, GameClock.I.Day); Assert.AreEqual(13.25f, GameClock.I.Hours, 0.01f);
            Assert.AreEqual("hello-42", loaded, "extension state");
            Assert.NotNull(Grade.Field, "grade field restored");
            Assert.AreEqual(h, Grade.HeightAt(200f, 205f), 1e-4f, "grade heights restored");
            bool foundPlant = false;
            foreach (var f in LandscapeFeature.All) if (f.Kind == LandscapeKind.Plant && f.ItemId == "shrub-boxwood" && f.Stage == 2) foundPlant = true;
            Assert.IsTrue(foundPlant, "landscape job restored");
            Assert.GreaterOrEqual(Game.Belt.Owned.Count, 1, "belt restored");
            SaveSystem.Delete("test_roundtrip");
            Grade.Destroy();
            foreach (var f in new List<LandscapeFeature>(LandscapeFeature.All)) Object.Destroy(f.gameObject);
            yield return null;
        }

        // ------------------------------------------------------------------ inspection

        [UnityTest]
        public IEnumerator InspectionFailsThenPasses()
        {
            yield return Frames(5);
            Daylight();
            Inspections.ResetState();
            Site.Lot = new Rect(110f, 110f, 20f, 20f);
            Inspections.RuleFilter = r => r.Id == "site.loose-members";
            var def = Catalog.Get("2x4x925");
            var studs = new List<Piece>();
            for (int i = 0; i < 4; i++)
                studs.Add(Piece.Create(def, 0, new Vector3(118f + i * Units.In(16), def.Size.z / 2, 120f), Quaternion.Euler(-90, 0, 0)));
            var plate = Piece.Create(Catalog.Get("2x4x8"), 0, new Vector3(118f + 1.2f, Units.In(0.75f), 120f), Quaternion.Euler(0, 90, 0));
            studs[0].Fasten(); plate.Fasten();

            InspectorNpc.SpeedMultiplier = 8f;
            var npc = InspectorNpc.Dispatch(InspectionPhase.Framing);
            Assert.NotNull(npc, "inspector not dispatched");
            var cam = Cam(new Vector3(116f, 2.2f, 115.5f), new Vector3(119.5f, 0.9f, 120f));
            float until = Time.time + 40f; bool walkShot = false;
            while (npc != null && npc.Result == null && Time.time < until)
            {
                if (!walkShot && npc.transform.position.z > 117.5f) {
                    Shot(cam, "20_inspector_walking"); walkShot = true;
                    var rs = npc.GetComponentsInChildren<Renderer>();
                    Debug.Log($"[Test] inspector at {npc.transform.position}, renderers {rs.Length}" + (rs.Length > 0 ? $", bounds {rs[0].bounds}, enabled {rs[0].enabled}" : ""));
                    Shot(Cam(npc.transform.position + new Vector3(2.2f, 1.6f, -2.2f), npc.transform.position + Vector3.up * 1f), "20b_inspector_closeup");
                } // in frame, by the wall
                yield return null;
            }
            Assert.NotNull(npc.Result, "inspection never finished: " + npc.Status + " at " + npc.transform.position + " t=" + Time.time);
            Assert.IsFalse(npc.Result.Passed, "loose studs should fail framing");
            Assert.AreEqual(3, npc.Result.Issues.Count, "3 loose studs");
            Assert.AreEqual(3, InspectionFlags.Count, "one red flag per loose stud");
            Assert.IsTrue(Inspections.NeedsReinspection(InspectionPhase.Framing));
            yield return Frames(2);
            Shot(cam, "21_inspection_failed_flags");

            foreach (var s in studs) s.Fasten();
            yield return new WaitForSeconds(0.6f);
            int money = Game.Wallet.Money, day = GameClock.I.Day;
            Assert.IsTrue(Inspections.ChargeForScheduling(InspectionPhase.Framing, out var msg), msg);
            Assert.AreEqual(money - Inspections.ReinspectionFee, Game.Wallet.Money, "re-inspection fee");
            Assert.AreEqual(day + 1, GameClock.I.Day, "a day lost");
            Daylight();
            npc = InspectorNpc.Dispatch(InspectionPhase.Framing);
            Assert.NotNull(npc);
            npc.FinishNow();
            Assert.IsTrue(npc.Result.Passed, "re-inspection should pass: " + string.Join("; ", npc.Result.Notes));
            Assert.IsTrue(Inspections.IsPassed(InspectionPhase.Framing));
            Assert.AreEqual(0, InspectionFlags.Count, "flags cleared after passing");
            yield return Frames(2);
            Shot(cam, "22_inspection_passed");
            Object.Destroy(npc.gameObject);
            foreach (var s in studs) Object.Destroy(s.gameObject);
            Object.Destroy(plate.gameObject);
            Inspections.ResetState();
        }

        // ------------------------------------------------------------------ career

        [UnityTest]
        public IEnumerator CareerContractPaidOnFinal()
        {
            yield return Frames(5);
            GameSettings.Mode = GameMode.Career;
            Career.ResetState();
            Inspections.RuleFilter = r => r.Id.StartsWith("site.");
            var shed = Career.Contracts.Find(c => c.Id == "shed");
            int start = Game.Wallet.Money, rep = Career.Reputation;
            Assert.IsTrue(Career.Accept(shed, out var msg), msg);
            Assert.AreEqual(start + shed.Price / 10, Game.Wallet.Money, "10% deposit");
            Assert.NotNull(Site.Lot);
            Assert.AreEqual(InspectionPhase.Framing, Inspections.CurrentPhase, "shed starts at framing");
            Assert.IsFalse(Career.Contracts.Find(c => c.Id == "garage") is var g && Career.CanTake(g), "can't take a second job");

            var lot = Site.Lot.Value;
            // tests share one world: hide pieces other tests left on this lot (e.g. the concrete slab's forms) so only our shed counts
            var others = Site.Pieces();
            foreach (var o in others) o.gameObject.SetActive(false);
            var p = Piece.Create(Catalog.Get("2x4x8"), 0, new Vector3(lot.center.x, 0.05f, lot.center.y), Quaternion.identity);
            p.Fasten();
            int paid = 0; Career.Paid += (c, amt) => paid = amt;
            Assert.IsTrue(Inspections.Inspect(InspectionPhase.Framing).Passed);
            int before = Game.Wallet.Money;
            Assert.IsTrue(Inspections.Inspect(InspectionPhase.Final).Passed);
            Assert.AreEqual(Mathf.RoundToInt(shed.Price * 0.9f), paid, "balance paid on final");
            Assert.AreEqual(before + paid, Game.Wallet.Money);
            Assert.Greater(Career.Reputation, rep, "reputation up for an on-time job");
            Assert.Contains("shed", Career.Completed);
            Assert.IsNull(Career.Active);
            Assert.IsFalse(Inspections.CurrentPhase.HasValue, "all phases signed off");
            Object.Destroy(p.gameObject);
            foreach (var o in others) if (o != null) o.gameObject.SetActive(true);
            Career.ResetState(); Inspections.ResetState();
            yield return null;
        }

        // ------------------------------------------------------------------ landscaping

        static void Stock(params (string id, int n)[] items) { foreach (var (id, n) in items) Game.Stock.Add(id, n); }

        /// <summary>Simple stand-in house (painted siding box + gable roof) so the yard reads in screenshots; corner posts are Pieces
        /// so inspection rules see the footprint.</summary>
        static GameObject MockHouse(Vector3 c, float w, float d, List<Piece> pieces)
        {
            var root = new GameObject("MockHouse");
            var k = new LandModel();
            int wall = k.M(SurfaceKind.PaintedWhite, new Color(0.86f, 0.84f, 0.78f)), roof = k.M(SurfaceKind.MatteBlack, new Color(1.6f, 1.6f, 1.7f)), conc = k.M(SurfaceKind.Concrete), door = k.M(SurfaceKind.Oak), glass = k.M(SurfaceKind.Glass);
            float h = 2.75f;
            k.B.AddBox(c + new Vector3(0, 0.2f, 0), new Vector3(w + 0.05f, 0.4f, d + 0.05f), Quaternion.identity, conc);
            k.B.AddBox(c + new Vector3(0, 0.4f + h / 2, 0), new Vector3(w, h, d), Quaternion.identity, wall);
            float top = 0.4f + h, ridge = 1.8f;
            var a = c + new Vector3(-w / 2 - 0.3f, top, -d / 2 - 0.3f); var b = c + new Vector3(w / 2 + 0.3f, top, -d / 2 - 0.3f);
            var r0 = c + new Vector3(-w / 2 - 0.3f, top + ridge, 0); var r1 = c + new Vector3(w / 2 + 0.3f, top + ridge, 0);
            var a2 = c + new Vector3(-w / 2 - 0.3f, top, d / 2 + 0.3f); var b2 = c + new Vector3(w / 2 + 0.3f, top, d / 2 + 0.3f);
            k.B.AddQuad(a, r0, r1, b, roof, new Vector3(0, 1, -1)); k.B.AddQuad(a2, b2, r1, r0, roof, new Vector3(0, 1, 1));
            k.B.AddTriangle(c + new Vector3(-w / 2, top, -d / 2), c + new Vector3(-w / 2, top + ridge - 0.2f, 0), c + new Vector3(-w / 2, top, d / 2), wall, Vector3.left);
            k.B.AddTriangle(c + new Vector3(w / 2, top, -d / 2), c + new Vector3(w / 2, top + ridge - 0.2f, 0), c + new Vector3(w / 2, top, d / 2), wall, Vector3.right);
            k.B.AddBox(c + new Vector3(0, 0.4f + 1.02f, -d / 2 - 0.02f), new Vector3(0.92f, 2.03f, 0.05f), Quaternion.identity, door);
            foreach (float x in new[] { -w / 3, w / 3 }) k.B.AddBox(c + new Vector3(x, 0.4f + 1.5f, -d / 2 - 0.02f), new Vector3(0.9f, 1.2f, 0.04f), Quaternion.identity, glass);
            k.Make(root.transform, "House");
            var post = Catalog.Get("4x4x8");
            foreach (var corner in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) })
            {
                var pc = Piece.Create(post, Units.In(12), c + Vector3.Scale(corner, new Vector3(w / 2 - 0.05f, 0, d / 2 - 0.05f)) + Vector3.up * 0.2f, Quaternion.Euler(-90, 0, 0));
                pc.Fasten(); pieces.Add(pc);
            }
            return root;
        }

        [UnityTest]
        public IEnumerator LandscapingShowcase()
        {
            yield return Frames(5);
            Daylight();
            Inspections.ResetState();
            var house = new Vector3(-240f, 0f, -20f);
            float hw = 9.75f, hd = 7.3f; // 32' x 24'
            Site.Lot = new Rect(house.x - 25f, house.z - 25f, 50f, 50f);
            Inspections.RuleFilter = r => r.Id.StartsWith("site.");
            var pieces = new List<Piece>();
            var mock = MockHouse(house, hw, hd, pieces);

            // --- grade: 6% fall away from the foundation (more than 6" in 10') ---
            Grade.Create(house.x - 30f, house.z - 30f, 120);
            var inner = new Rect(house.x - hw / 2, house.z - hd / 2, hw, hd);
            Grade.Field.SetHeights((x, z) =>
            {
                float dx = Mathf.Max(inner.xMin - x, 0, x - inner.xMax), dz = Mathf.Max(inner.yMin - z, 0, z - inner.yMax);
                return 0.12f - 0.06f * Mathf.Sqrt(dx * dx + dz * dz);
            });
            Grade.Field.Smooth(house + new Vector3(0, 0, -12f), 3f, 0.2f);
            yield return null;
            Site.BuildingBounds(out var bb);
            foreach (var (at, outward) in SiteRules.PerimeterSamples(bb))
                Assert.GreaterOrEqual(SiteRules.FallAt(at, outward), Units.In(5.75f), "grade must fall 6\" in 10' at " + at);
            Assert.IsNotEmpty(LandTool.SlopeReadout(house + new Vector3(0, 0, -hd / 2 - 1.5f)));

            Stock(("crusher-run", 16), ("gravel-57", 8), ("paver-sand", 2), ("paver-holland", 400), ("paver-edging", 6), ("poly-sand", 2),
                  ("sod-roll", 40), ("topsoil", 6), ("tree-maple", 2), ("tree-spruce", 1), ("shrub-boxwood", 6), ("shrub-hydrangea", 2), ("perennial-hosta", 4),
                  ("4x4x8", 8), ("postmix-50", 20), ("pt2x4x8", 12), ("picket-cedar", 80), ("gate-kit", 1), ("retaining-block", 30),
                  ("path-light", 4), ("mailbox-kit", 1), ("grass-seed", 2), ("straw-bale", 2), ("mulch", 3));

            // --- paver walk from the front door to the street ---
            var doorStep = house + new Vector3(0, 0, -hd / 2 - 0.05f);
            var walk = Landscape.LayOut(LandscapeKind.Walkway, doorStep, doorStep + new Vector3(0, 0, -6.5f));
            Assert.NotNull(walk);
            yield return null;
            Shot(Cam(doorStep + new Vector3(2.6f, 1.7f, -3.5f), doorStep + new Vector3(0, 0, -3f)), "30_walk_layout");
            for (int i = 0; i < walk.Stages()[0].Steps; i++) Assert.IsTrue(walk.Work("spade", out var m1), m1);
            Assert.IsTrue(walk.Work("wheelbarrow", out var m2), m2);
            yield return null;
            Shot(Cam(doorStep + new Vector3(2.0f, 1.6f, -3.5f), doorStep + new Vector3(0, -0.2f, -3f)), "31_walk_excavated_base");
            Assert.Greater(walk.CompleteAll(), 0);
            Assert.IsTrue(walk.Done, walk.Hint);
            Assert.Less(Game.Stock.Count("paver-holland"), 400, "pavers consumed");

            // --- gravel drive ---
            var drive = Landscape.LayOut(LandscapeKind.GravelDrive, house + new Vector3(hw / 2 + 3f, 0, -hd / 2 + 1f), house + new Vector3(hw / 2 + 3f, 0, -18f));
            drive.CompleteAll();
            Assert.IsTrue(drive.Done, drive.Hint);
            Assert.AreEqual(LandCover.Gravel, Grade.CoverAt(drive.Center.x, drive.Center.z));

            // --- sod the front yard (strip, topsoil, lay staggered rolls, water) ---
            var sodOrigin = house + new Vector3(-hw / 2 + 0.6f, 0, -hd / 2 - 1.6f);
            Grade.Field.PaintRect(sodOrigin + new Vector3(-1.4f, 0, -2.6f), new Vector2(2.6f, 3.2f), 0, LandCover.Topsoil);
            int laid = 0;
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 3; row++)
                {
                    var p = sodOrigin + new Vector3(-col * Landscape.SodW - 0.3f, 0, -row * Landscape.SodL - (col % 2) * Landscape.SodL / 2 - 0.8f);
                    if (Landscape.LaySod(p, 0, out var sm) != null) laid++; else Debug.Log("[Test] sod: " + sm);
                }
            Assert.GreaterOrEqual(laid, 8, "sod rolls laid");
            Assert.Greater(LandscapeSystem.WaterAround(sodOrigin + new Vector3(-3, 0, -4), 6f), 0);

            // --- trees and shrubs: dig, set, backfill, water ---
            var maple = Landscape.MarkPlant("tree-maple", house + new Vector3(-hw / 2 - 5f, 0, -hd / 2 - 6f));
            Assert.IsFalse(maple.Work("landhands", out var wrong), "must dig before setting the tree");
            maple.Work("shovel", out _); maple.Work("shovel", out _);
            yield return null;
            Shot(Cam(maple.A + new Vector3(2.2f, 1.5f, -2.2f), maple.A), "32_tree_hole");
            maple.Work("shovel", out _); maple.Work("shovel", out _);
            Assert.IsTrue(maple.Work("landhands", out var setMsg), setMsg);
            yield return null;
            Shot(Cam(maple.A + new Vector3(2.5f, 1.6f, -2.5f), maple.A + Vector3.up * 0.8f), "33_tree_set_rootball");
            maple.CompleteAll();
            Assert.IsTrue(maple.Done, maple.Hint);
            var spruce = Landscape.MarkPlant("tree-spruce", house + new Vector3(hw / 2 + 1.5f, 0, hd / 2 + 4f)); spruce.CompleteAll();
            for (int i = 0; i < 5; i++) Landscape.MarkPlant("shrub-boxwood", house + new Vector3(-hw / 2 + 0.9f + i * 0.9f, 0, -hd / 2 - 0.75f)).CompleteAll();
            Landscape.MarkPlant("shrub-hydrangea", house + new Vector3(-hw / 2 - 0.6f, 0, -hd / 2 - 0.4f)).CompleteAll();
            for (int i = 0; i < 3; i++) Landscape.MarkPlant("perennial-hosta", house + new Vector3(1.6f + i * 0.6f, 0, -hd / 2 - 0.6f)).CompleteAll();
            Grade.Field.PaintRect(house + new Vector3(0, 0, -hd / 2 - 0.75f), new Vector2(hw / 2, 0.55f), 0, LandCover.Mulch);
            Grade.Field.PaintRect(house + new Vector3(0.6f, 0, -hd / 2 - 0.75f), new Vector2(0.55f, 0.6f), 0, LandCover.Mulch);

            // --- privacy fence along the side yard + gate ---
            var fenceA = house + new Vector3(-hw / 2 - 0.2f, 0, hd / 2 - 0.5f);
            var fence = Landscape.LayOut(LandscapeKind.Fence, fenceA + new Vector3(-1.6f, 0, 0), fenceA + new Vector3(-1.6f - 7.3f, 0, 0));
            for (int i = 0; i < fence.Posts * 2; i++) Assert.IsTrue(fence.Work("posthole", out var hm), hm);
            for (int i = 0; i < 2; i++) fence.Work("landhands", out _);
            yield return null;
            Shot(Cam(fence.Center + new Vector3(1f, 1.8f, -4.5f), fence.Center), "34_fence_holes_posts");
            Assert.IsTrue(fence.Work("landhands", out _));
            fence.CompleteAll();
            Assert.IsTrue(fence.Done, fence.Hint);
            var gate = Landscape.LayOut(LandscapeKind.Gate, fenceA, fenceA + new Vector3(-1f, 0, 0));
            gate.CompleteAll();
            Assert.IsTrue(gate.Done, gate.Hint);

            // --- retaining wall on a gravel pad, path lights, mailbox ---
            var wallStart = house + new Vector3(hw / 2 + 0.8f, 0, hd / 2 + 1.2f);
            Grade.Field.PaintRect(wallStart + new Vector3(1.0f, 0, 0), new Vector2(1.4f, 0.3f), 0, LandCover.Gravel);
            LandscapeFeature prev = null; int blocks = 0;
            for (int i = 0; i < 6; i++) { var bl = Landscape.PlaceBlock(wallStart + new Vector3(i * Landscape.BlockW, 0, 0), 0, null, out var bm); if (bl != null) { blocks++; prev = bl; } else Debug.Log("[Test] block: " + bm); }
            var stacked = Landscape.PlaceBlock(Vector3.zero, 0, prev, out var stm);
            Assert.NotNull(stacked, stm);
            Assert.AreEqual(prev.A.y + Landscape.BlockH, stacked.A.y, 1e-4f, "course stacks 4\" up");
            Assert.GreaterOrEqual(blocks, 5);
            for (int i = 0; i < 3; i++) Landscape.PlacePoint(LandscapeKind.PathLight, "path-light", doorStep + new Vector3(0.7f, 0, -1.2f - i * 2f), 0, out _);
            var mailbox = Landscape.PlacePoint(LandscapeKind.Mailbox, "mailbox-kit", house + new Vector3(-1.5f, 0, -hd / 2 - 7.8f), 180, out var mbm);
            Assert.NotNull(mailbox, mbm);

            // --- seed + straw a back patch ---
            var seedSpot = house + new Vector3(0, 0, hd / 2 + 5f);
            Grade.Field.PaintRect(seedSpot, new Vector2(2f, 2f), 0, LandCover.Topsoil);
            Assert.IsTrue(HoeTool.SowAt(seedSpot, out var sowMsg), sowMsg);
            Assert.AreEqual(LandCover.Seeded, Grade.CoverAt(seedSpot.x, seedSpot.z));

            // --- final inspection (site rules) passes ---
            var res = Inspections.Inspect(InspectionPhase.Final);
            Assert.IsTrue(res.Passed, "final site inspection: " + string.Join("; ", res.Issues.ConvertAll(i => i.Message)));
            yield return Frames(4);

            Shot(Cam(house + new Vector3(-9f, 6.5f, -19f), house + new Vector3(-1f, 0, -6f)), "35_landscape_overview");
            Shot(Cam(doorStep + new Vector3(1.6f, 1.65f, -7.2f), doorStep + new Vector3(-0.5f, 0.3f, 0)), "36_walk_sod_closeup");
            Shot(Cam(sodOrigin + new Vector3(-1.2f, 6.5f, -2.6f), sodOrigin + new Vector3(-1.2f, 0, -2.3f)), "36b_sod_topdown");
            Shot(Cam(fence.Center + new Vector3(1.5f, 1.7f, -5f), fence.Center + new Vector3(1.5f, 0.9f, 0)), "37_fence_gate");
            Shot(Cam(wallStart + new Vector3(0.6f, 1.2f, -2.2f), wallStart + new Vector3(0.6f, 0.1f, 0)), "38_retaining_wall");
            Shot(Cam(maple.A + new Vector3(3.5f, 1.7f, -4f), maple.A + Vector3.up * 1.8f), "39_maple_shrubs");
            yield return null;

            // save → load keeps the yard
            int features = LandscapeFeature.All.Count;
            Assert.IsTrue(SaveSystem.Save("test_landscape"));
            Assert.IsTrue(SaveSystem.Load("test_landscape"));
            yield return Frames(2);
            Assert.AreEqual(features, LandscapeFeature.All.Count, "landscape features survive save/load");
            SaveSystem.Delete("test_landscape");
        }
    }
}
