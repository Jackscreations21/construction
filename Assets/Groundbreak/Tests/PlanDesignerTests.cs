using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Plan Designer: a house drawn through the designer's API passes the validator, findings come back in plain language with
    /// highlights, edits undo, every library plan survives being copied, saved plans register as custom-* (Plans app, overlay, spec list,
    /// client builds) and survive save/reload, and pictures go to Logs/shots/designer_*.png (plan drawings + a camera-rendered overlay;
    /// the tablet UI shot needs a windowed run, see Docs/UI-CAPTURE.md).</summary>
    public class PlanDesignerTests
    {
        static readonly Vector3 PadOrigin = new Vector3(-1500f, 0f, 1500f);
        static string ShotDir { get { var d = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(d); return d; } }
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Png(Texture2D t, string name) { File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), t.EncodeToPNG()); Object.Destroy(t); }

        static void Picture(DesignEditor ed, int floor, string name, bool problems = true)
        {
            var hi = new List<Rect>();
            if (problems) foreach (var i in ed.Issues) if (i.Error && i.Area.HasValue && (i.Floor < 0 || i.Floor == floor)) hi.Add(i.Area.Value);
            Png(PlanPicture.Render(ed.Plan, floor, 960, 720, hi, ed.IsPlaced), name);
        }

        static string Dump(DesignEditor ed) { var sb = new System.Text.StringBuilder(); foreach (var i in ed.Issues) sb.AppendLine(i.ToString()); return sb.ToString(); }

        /// <summary>A 40' x 30' three-bed ranch drawn the way a player would: footprint, one wall across, room boxes, names.</summary>
        public static DesignEditor DrawRanch()
        {
            var ed = DesignEditor.Blank(PlanGroup.Residential, 40f, 30f);
            ed.Design.name = "Test Ranch";
            Assert.IsTrue(ed.DrawWall(0, new Vector2(1f, 16f), new Vector2(39f, 16.2f)), ed.Message);       // front living zone | back bedroom zone
            Assert.IsTrue(ed.AddRoom(0, Rect.MinMaxRect(26, 0, 40, 16), "Kitchen"), ed.Message);
            Assert.IsTrue(ed.AddRoom(0, Rect.MinMaxRect(0, 16, 12, 30), "Bedroom"), ed.Message);
            Assert.IsTrue(ed.AddRoom(0, Rect.MinMaxRect(12, 16, 18, 30), "Bath"), ed.Message);
            Assert.IsTrue(ed.AddRoom(0, Rect.MinMaxRect(28, 16, 40, 30), "Master"), ed.Message);
            Assert.IsTrue(ed.NameRoom(0, new Vector2(23f, 23f), "Bedroom"), ed.Message);                       // the leftover "Room" → Bed 2
            return ed;
        }

        [SetUp] public void Begin() { PlanDesigner.LoadJson(null); if (GameClock.I != null) GameClock.I.Hours = 11f; }

        [TearDown]
        public void Cleanup()
        {
            PlanOverlay.Hide(); PlanDesigner.LoadJson(null);
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c.name == "PdTestCam") Object.Destroy(c.gameObject);
            if (Tablet.I != null && Tablet.I.IsOpen) Tablet.I.Close();
        }

        [Test]
        public void DesignerBuildsAValidHouse()
        {
            var ed = DrawRanch();
            var names = ed.Design.rooms.ConvertAll(r => r.name);
            CollectionAssert.AreEquivalent(new[] { "Living", "Kitchen", "Bed 1", "Bath", "Bed 2", "Master" }, names, string.Join(", ", names));
            var p = ed.Plan;
            Assert.NotNull(p);
            Assert.AreEqual(0, ed.ErrorCount, Dump(ed));
            Assert.AreEqual(3, p.Beds); Assert.AreEqual(2, p.Baths2);
            Assert.AreEqual(1200f, p.SqFt, 0.5f);
            Assert.Greater(p.Takeoff.Total, 150000); Assert.Less(p.Takeoff.Total / p.SqFt, 400f, "$/sq ft");
            StringAssert.Contains("1,200 sq ft", ed.StatsLine); StringAssert.Contains("3 bed / 1 bath", ed.StatsLine);
            // auto doors + windows came from the plan rules; the entry door is the placed one
            Assert.IsTrue(p.Openings.Exists(o => o.IsDoor && p.Walls[o.Wall].Exterior && ed.IsPlaced(o)), "placed entry door");
            Assert.GreaterOrEqual(p.Openings.FindAll(o => o.IsDoor && !p.Walls[o.Wall].Exterior).Count, 4, "auto interior doors");
            // place a window myself: it replaces the auto one at that spot
            int before = p.Openings.Count;
            Assert.IsTrue(ed.PlaceOpening(0, new Vector2(6f, 30.4f), "win-sh-3648"), ed.Message);
            p = ed.Plan;
            Assert.IsTrue(p.Openings.Exists(o => o.ProductId == "win-sh-3648" && ed.IsPlaced(o)), "placed window");
            Assert.AreEqual(before, p.Openings.Count, "auto window at the spot was replaced");
            // Bed 1 still has its side egress window, so still no problems
            Assert.AreEqual(0, ed.ErrorCount, Dump(ed));
            Picture(ed, 0, "designer_drawing_ranch");
            Debug.Log("[PlanDesigner] ranch: " + ed.StatsLine);
        }

        [Test]
        public void FindingsArePlainLanguageAndHighlighted()
        {
            var ed = DrawRanch();
            // tiny bedroom, narrow bath
            Assert.IsTrue(ed.MoveWall(0, new Vector2(12f, 23f), 6f), ed.Message);    // Bed 1 → 6' wide, Bath grows
            var tooSmall = ed.Issues.Find(i => i.Text.Contains("too small for a bedroom"));
            Assert.NotNull(tooSmall, Dump(ed)); Assert.IsTrue(tooSmall.Error); Assert.IsTrue(tooSmall.Area.HasValue, "highlight area");
            Assert.AreEqual(6f, tooSmall.Area.Value.width, 0.01f);
            Picture(ed, 0, "designer_drawing_problems");
            ed.Undo();
            Assert.AreEqual(0, ed.ErrorCount, "undo restored a good plan\n" + Dump(ed));

            // no way in
            Assert.IsTrue(ed.RemoveOpeningAt(0, new Vector2(14f, 0.2f)), ed.Message);
            Assert.NotNull(ed.Issues.Find(i => i.Text.StartsWith("There's no way in")), Dump(ed));
            ed.Undo();

            // a bath too narrow for a tub
            Assert.IsTrue(ed.MoveWall(0, new Vector2(18f, 23f), 16f), ed.Message);
            Assert.NotNull(ed.Issues.Find(i => i.Text.Contains("too narrow for a full bath")), Dump(ed));
            ed.Undo();

            // bedroom without an escape window (auto windows switched off)
            foreach (var pt in new[] { new Vector2(0.2f, 23f), new Vector2(6f, 29.8f) }) ed.RemoveOpeningAt(0, pt);
            var egress = ed.Issues.Find(i => i.Check == "egress");
            Assert.NotNull(egress, Dump(ed)); StringAssert.Contains("emergency escape window", egress.Text); Assert.AreEqual("Bed 1", egress.Text.Split(' ')[0] + " " + egress.Text.Split(' ')[1]);
            ed.PlaceOpening(0, new Vector2(6f, 30f), "win-sh-3660");
            Assert.IsNull(ed.Issues.Find(i => i.Check == "egress"), Dump(ed));

            // second floor with no stair
            Assert.IsTrue(ed.AddFloor(), ed.Message);
            var stair = ed.Issues.Find(i => i.Check == "stairs" || i.Check == "access");
            Assert.NotNull(stair, Dump(ed));
            Assert.IsTrue(ed.Issues.Exists(i => i.Text.Contains("has no stair") || i.Text.Contains("can't be reached")), Dump(ed));
            ed.Undo();

            // a building too deep for trusses
            Assert.IsTrue(ed.SetFootprint(40f, 64f), ed.Message);
            var span = ed.Issues.Find(i => i.Check == "spans");
            Assert.NotNull(span, Dump(ed)); StringAssert.StartsWith("Roof span too long", span.Text);
            Debug.Log("[PlanDesigner] findings sample:\n" + Dump(ed));
        }

        [Test]
        public void EditingFootprintWallsAndUndo()
        {
            var ed = new DesignEditor(DrawRanch().Design);   // no drawing history: undo stops at the finished ranch
            string start = ed.Design.ToJson();
            Assert.IsTrue(ed.SetFootprint(44f, 32f), ed.Message);
            Assert.AreEqual(44f, ed.Design.width); Assert.AreEqual(32f, ed.Design.depth);
            var kitchen = ed.Design.rooms.Find(r => r.name == "Kitchen"); Assert.AreEqual(44f, kitchen.x1, 0.01f, "edge room stretched");
            var master = ed.Design.rooms.Find(r => r.name == "Master"); Assert.AreEqual(32f, master.y1, 0.01f);
            Assert.AreEqual(0, ed.ErrorCount, Dump(ed));
            // slide the living/kitchen wall
            Assert.IsTrue(ed.MoveWall(0, new Vector2(26f, 8f), 24f), ed.Message);
            Assert.AreEqual(24f, ed.Design.rooms.Find(r => r.name == "Kitchen").x0, 0.01f);
            // erase the Bed 2 / Master wall → joined
            int n = ed.Design.rooms.Count;
            Assert.IsTrue(ed.EraseWall(0, new Vector2(28f, 24f)), ed.Message);
            Assert.AreEqual(n - 1, ed.Design.rooms.Count);
            // undo all the way
            while (ed.CanUndo) ed.Undo();
            var back = new DesignEditor(PlanDesign.FromJson(start));
            Assert.AreEqual(back.Plan.Summary, ed.Plan.Summary);
            Assert.AreEqual(40f, ed.Design.width);
            Assert.IsTrue(ed.Redo());
            Assert.AreEqual(44f, ed.Design.width);
            // snapping: odd drags land on the grid
            Assert.IsTrue(ed.DrawWall(0, new Vector2(1f, 7.13f), new Vector2(10f, 7.2f)), ed.Message);
            Assert.IsTrue(ed.Design.rooms.Exists(r => Mathf.Abs(r.y1 - 7f) < 0.01f || Mathf.Abs(r.y1 - 7.5f) < 0.01f || Mathf.Abs(r.y1 - 7.0f) < 0.01f), "snapped to grid");
        }

        [Test]
        public void TwoStoryWithAStair()
        {
            var ed = DesignEditor.Blank(PlanGroup.Residential, 36f, 28f);
            ed.Design.name = "Test Two Story";
            ed.Design.Foundation = FoundationType.Basement; ed.Changed();
            Assert.IsTrue(ed.DrawWall(0, new Vector2(0f, 14f), new Vector2(36f, 14f)));
            Assert.IsTrue(ed.NameRoom(0, new Vector2(18f, 20f), "Kitchen"));
            Assert.IsTrue(ed.AddRoom(0, Rect.MinMaxRect(30, 14, 36, 22), "Powder"));
            Assert.IsTrue(ed.AddRoom(0, Rect.MinMaxRect(30, 22, 36, 28), "Laundry"));
            // stair along y in the hall zone, landing in the living room (front)
            float len = ed.StairLengthFt; Assert.Greater(len, 12f); Assert.Less(len, 18f);
            Assert.IsTrue(ed.AddStair(0, Rect.MinMaxRect(0, 6, 4, 6 + Mathf.Ceil(len))), ed.Message);
            Assert.AreEqual(2, ed.Design.floors);
            Assert.IsTrue(ed.Design.rooms.Exists(r => r.floor == 1 && r.name == "Stair"));
            // upstairs: hall up front, bedrooms and a bath behind
            Assert.IsTrue(ed.DrawWall(1, new Vector2(5f, 10f), new Vector2(36f, 10f)), ed.Message);
            Assert.IsTrue(ed.DrawWall(1, new Vector2(13f, 11f), new Vector2(13f, 27f)), ed.Message);
            Assert.IsTrue(ed.DrawWall(1, new Vector2(19f, 11f), new Vector2(19f, 27f)), ed.Message);
            foreach (var (x, n) in new[] { (8f, "Bedroom"), (16f, "Bath"), (27f, "Master") }) Assert.IsTrue(ed.NameRoom(1, new Vector2(x, 20f), n), ed.Message);
            var p = ed.Plan;
            Assert.AreEqual(1, p.Stairs.Count, Dump(ed));
            var errs = ed.Issues.FindAll(i => i.Error);
            Picture(ed, 0, "designer_drawing_twostory_f1");
            Picture(ed, 1, "designer_drawing_twostory_f2");
            Debug.Log("[PlanDesigner] two-story: " + ed.StatsLine + "\n" + Dump(ed));
            Assert.AreEqual(0, errs.Count, Dump(ed));
            Assert.AreEqual(2, p.Beds);
        }

        [Test]
        public void EveryLibraryPlanSurvivesBeingCopied()
        {
            var bad = new System.Text.StringBuilder(); int n = 0;
            foreach (var src in Blueprints.All)
            {
                if (PlanDesigner.IsCustom(src)) continue;
                var ed = DesignEditor.CopyOf(src);
                var p = ed.Plan; n++;
                Assert.NotNull(p, src.Id);
                Assert.AreEqual(src.Rooms.Count, p.Rooms.Count, src.Id + " rooms");
                Assert.AreEqual(src.SqFt, p.SqFt, 1f, src.Id + " sq ft");
                int srcErr = BlueprintValidator.Errors(src).Count;
                if (ed.ErrorCount > srcErr) bad.AppendLine($"{src.Id}: {ed.ErrorCount} errors\n" + Dump(ed));
                int so = Blueprints.Info(src).Openings.Count;
                if (Mathf.Abs(p.Openings.Count - so) > 0) bad.AppendLine($"{src.Id}: openings {p.Openings.Count} vs {so}");
            }
            Debug.Log($"[PlanDesigner] copied {n} library plans");
            Assert.AreEqual("", bad.ToString());
        }

        [UnityTest]
        public IEnumerator SaveRenameDeleteAndReload()
        {
            yield return Frames(2);
            var ed = DrawRanch();
            Assert.IsTrue(PlanDesigner.Save(ed, false, out var msg), msg);
            string id = ed.Design.id;
            StringAssert.StartsWith("custom-test-ranch", id);
            var reg = HousePlans.Get(id);
            Assert.NotNull(reg, "registered"); Assert.IsInstanceOf<CustomPlan>(reg); Assert.AreEqual(PlanGroup.Residential, reg.Group);
            Assert.IsTrue(Blueprints.Find(null, 1000, 1300).Contains(reg), "lookups see it");
            Assert.AreEqual(0, BlueprintValidator.Errors(reg).Count);
            // spec-house list in the Development app = residential plans ≤ 60' wide
            Assert.IsTrue(reg.Group == PlanGroup.Residential && reg.WidthFt <= 60f);
            // second save of the same name gets its own id
            var ed2 = DrawRanch();
            Assert.IsTrue(PlanDesigner.Save(ed2, false, out msg), msg);
            Assert.AreNotEqual(id, ed2.Design.id);
            // a draft with a problem is saved but not registered
            var bad = DesignEditor.Blank(PlanGroup.Residential); bad.Design.name = "Broken";
            bad.AddRoom(0, Rect.MinMaxRect(0, 20, 6, 30), "Bedroom");
            Assert.IsTrue(PlanDesigner.Save(bad, false, out msg)); StringAssert.Contains("draft", msg);
            Assert.IsNull(HousePlans.Get(bad.Design.id)); Assert.NotNull(PlanDesigner.GetDesign(bad.Design.id));
            // rename
            Assert.IsTrue(PlanDesigner.Rename(id, "Sunny Ranch", out msg));
            Assert.AreEqual("Sunny Ranch", HousePlans.Get(id).Name);
            // persists through the save extension
            CollectionAssert.Contains(new List<string>(SaveSystem.ExtensionKeys), PlanDesigner.SaveKey);
            var cap = SaveSystem.Capture();
            var kv = cap.ext.Find(x => x.key == PlanDesigner.SaveKey); Assert.NotNull(kv); StringAssert.Contains("Sunny Ranch", kv.value);
            string summary = HousePlans.Get(id).Summary; int cost = HousePlans.Get(id).Takeoff.Total;
            PlanDesigner.LoadJson(null);
            Assert.IsNull(HousePlans.Get(id), "new game: gone"); Assert.AreEqual(0, PlanDesigner.Saved.Count);
            PlanDesigner.LoadJson(kv.value);
            Assert.AreEqual(3, PlanDesigner.Saved.Count);
            Assert.NotNull(HousePlans.Get(id)); Assert.AreEqual(summary, HousePlans.Get(id).Summary); Assert.AreEqual(cost, HousePlans.Get(id).Takeoff.Total);
            Assert.AreEqual("Sunny Ranch", HousePlans.Get(id).Name);
            // in use by a job → can't delete or overwrite
            var job = new Job { Id = "pd-test-job", Title = "Test build", Kind = JobKind.House, Payload = "{\"contract\":\"x\",\"plan\":\"" + id + "\"}" };
            Company.Offers.Add(job);
            Assert.IsFalse(PlanDesigner.Delete(id, out msg), msg);
            var again = new DesignEditor(PlanDesigner.GetDesign(id).Clone()); again.SetFootprint(42f, 30f);
            Assert.IsFalse(PlanDesigner.Save(again, false, out msg), msg);
            Assert.IsTrue(PlanDesigner.Save(again, true, out msg), msg);
            Assert.AreNotEqual(id, again.Design.id);
            Company.Offers.Remove(job);
            Assert.IsTrue(PlanDesigner.Delete(id, out msg), msg);
            Assert.IsNull(HousePlans.Get(id)); Assert.IsNull(PlanDesigner.GetDesign(id));
        }

        [Test]
        public void ClientBuildUsesTheCustomPlan()
        {
            var ed = DrawRanch(); PlanDesigner.Save(ed, false, out _);
            var plan = HousePlans.Get(ed.Design.id);
            var mode = GameSettings.Mode; var stage = Company.Stage;
            try
            {
                GameSettings.Mode = GameMode.FreeBuild;
                Assert.IsNull(PlanDesigner.OfferToClient(plan, out var msg)); StringAssert.Contains("Career", msg);
                GameSettings.Mode = GameMode.Career; Company.Stage = CompanyStage.Builder;
                var job = PlanDesigner.OfferToClient(plan, out msg);
                Debug.Log("[PlanDesigner] client offer: " + msg);
                if (job == null) { StringAssert.Contains("No free lot", msg); Assert.Inconclusive(msg); }
                StringAssert.Contains("\"plan\":\"" + plan.Id + "\"", job.Payload);
                Assert.IsTrue(Company.Offers.Contains(job));
                Assert.AreEqual(Mathf.RoundToInt(plan.SqFt * JobCatalog.Get("bld.ranch").PricePerSqFt / 1000f) * 1000, job.Payout);
                Assert.IsTrue(PlanDesigner.InUse(plan.Id, out _));
                Company.Offers.Remove(job);
            }
            finally { GameSettings.Mode = mode; Company.Stage = stage; }
        }

        [UnityTest]
        public IEnumerator OverlayOfACustomPlanOnTheLot()
        {
            yield return Frames(3);
            var ed = DrawRanch();
            ed.Design.name = "Overlay Ranch";
            Assert.IsTrue(PlanDesigner.Save(ed, false, out var msg), msg);
            var plan = HousePlans.Get(ed.Design.id);
            var k = new LandModel();
            k.B.AddBox(PadOrigin + new Vector3(10f, -0.05f, 10f), new Vector3(80f, 0.1f, 80f), Quaternion.identity, k.M(SurfaceKind.Grass));
            var pad = k.Make(null, "PdTestPad");
            var at = new PlanPlacement { Origin = PadOrigin, X = Vector3.right, Y = Vector3.forward };
            var root = PlanOverlay.Show(plan, at, 0);
            Assert.NotNull(root); Assert.Greater(PlanOverlay.WallCount, 8); Assert.AreEqual(plan.Rooms.Count, PlanOverlay.LabelCount);
            Assert.Greater(PlanOverlay.OpeningCount, 8);
            Assert.Greater(BlueprintOverlay.FixtureCount, 5, "MEP layer on a custom plan");
            yield return null;
            var c = at.World(plan.WidthFt / 2f, plan.DepthFt / 2f);
            float s = Mathf.Max(plan.WidthFt * Units.Foot * 0.62f, plan.DepthFt * Units.Foot * 1.05f);
            Shot(c + new Vector3(0f, s + 3f, -s * 0.2f), c, "designer_overlay_top");
            Shot(at.World(-8f, -16f, 1.7f), c, "designer_overlay_eye");
            PlanOverlay.Hide(); Object.Destroy(pad);
        }

        static void Shot(Vector3 pos, Vector3 look, string name)
        {
            var cam = new GameObject("PdTestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = 55; cam.farClipPlane = 3000f;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            Png(tex, name); rt.Release(); Object.Destroy(rt); Object.Destroy(cam.gameObject);
        }

        /// <summary>The tablet app itself (IMGUI: windowed runs only, like UICaptureTests).</summary>
        [UnityTest, Category("UICapture")]
        public IEnumerator DesignerTabletScreenshots()
        {
            if (Application.isBatchMode) { Assert.Ignore("IMGUI does not render in -batchmode: run windowed, see Docs/UI-CAPTURE.md"); yield break; }
            UICaptureTests.SetGameView(1600, 900);
            if (MainMenu.Showing) MainMenu.I.Hide();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.NotNull(Tablet.I);
            var ed = DrawRanch(); PlanDesigner.Save(ed, false, out _);
            PlanDesignerApp.Open(new DesignEditor(ed.Design.Clone()));
            Tablet.I.ShowApp(PlanDesignerApp.AppName);
            foreach (var (tool, name) in new[] { (PlanDesignerApp.DTool.Name, "designer_ui_editor"), (PlanDesignerApp.DTool.Door, "designer_ui_doors") })
            {
                PlanDesignerApp.Tool = tool;
                yield return new WaitForSecondsRealtime(0.35f);
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture(); Png(tex, name);
            }
            // a problem highlighted
            PlanDesignerApp.Editor.MoveWall(0, new Vector2(12f, 23f), 6f);
            PlanDesignerApp.Tool = PlanDesignerApp.DTool.Move;
            yield return new WaitForSecondsRealtime(0.3f); yield return new WaitForEndOfFrame();
            Png(ScreenCapture.CaptureScreenshotAsTexture(), "designer_ui_problems");
            // start screen with My plans
            PlanDesignerApp.Open(null);
            yield return new WaitForSecondsRealtime(0.3f); yield return new WaitForEndOfFrame();
            Png(ScreenCapture.CaptureScreenshotAsTexture(), "designer_ui_start");
        }
    }
}
