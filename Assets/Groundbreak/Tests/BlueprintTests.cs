using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Blueprint library: every plan passes the validator, takeoffs are sane, the on-site overlay builds for every plan and floor,
    /// and screenshots of a representative set (one+ per category, top-down and eye level) go to Logs/shots/bp_*.png.
    /// Overlays are drawn on a grass pad at x = -1500 so nothing else is in frame.</summary>
    public class BlueprintTests
    {
        static readonly Vector3 PadOrigin = new Vector3(-1500f, 0f, -1500f);

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        static Camera Cam(Vector3 pos, Vector3 look)
        {
            var cam = new GameObject("BpTestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = 55; cam.farClipPlane = 3000f;
            return cam;
        }

        static GameObject _pad;
        static void Pad()
        {
            if (_pad != null) return;
            var k = new LandModel();
            k.B.AddBox(PadOrigin + new Vector3(40f, -0.05f, 40f), new Vector3(260f, 0.1f, 260f), Quaternion.identity, k.M(SurfaceKind.Grass));
            _pad = k.Make(null, "BpTestPad");
        }

        static PlanPlacement At() => new PlanPlacement { Origin = PadOrigin, X = Vector3.right, Y = Vector3.forward };

        [SetUp]
        public void Begin() { if (GameClock.I != null) { GameClock.I.Hours = 11f; GameClock.I.Day = 1; } Weather.ForceRain = false; }

        [TearDown]
        public void Cleanup()
        {
            PlanOverlay.Hide(); Weather.ForceRain = null;
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c.name == "BpTestCam") Object.Destroy(c.gameObject);
        }

        [OneTimeTearDown]
        public void Done() { if (_pad != null) Object.Destroy(_pad); _pad = null; }

        // ------------------------------------------------------------------ library

        [Test]
        public void LibraryIsRegisteredWithCategoriesAndStages()
        {
            var lib = Blueprints.Library;
            Assert.GreaterOrEqual(lib.Count, 40, "at least 40 library plans");
            var ids = new HashSet<string>();
            foreach (var p in Blueprints.All) Assert.IsTrue(ids.Add(p.Id), "duplicate id " + p.Id);
            foreach (var b in lib)
            {
                Assert.AreSame(b, HousePlans.Get(b.Id), b.Id + " registered in HousePlans");
                Assert.IsFalse(string.IsNullOrEmpty(b.Category) || b.Category == "house", b.Id + " has a type");
                Assert.Greater(b.SqFt, 50f, b.Id + " sq ft");
            }
            foreach (PlanGroup g in System.Enum.GetValues(typeof(PlanGroup))) Assert.Greater(Blueprints.OfGroup(g).Count, 2, g + " plans");
            foreach (var cat in new[] { "starter", "ranch", "two-story", "split-level", "duplex", "townhome", "adu", "cottage", "custom", "garage", "shed",
                                        "pole-barn", "barn", "workshop", "retail", "restaurant", "office", "strip-mall", "gas-station", "warehouse" })
                Assert.Greater(Blueprints.OfCategory(cat).Count, 0, "category " + cat);
            Assert.GreaterOrEqual(Blueprints.OfCategory("garage").Count, 3, "1-, 2- and 3-car garages");
            Assert.GreaterOrEqual(Blueprints.OfCategory("warehouse").Count, 5, "several warehouses incl. the yard building");
            // stages: a handyman can only build a shed; a developer can build everything
            Assert.IsTrue(Blueprints.Buildable(CompanyStage.Handyman).TrueForAll(p => p.Category == "shed"));
            Assert.AreEqual(Blueprints.All.Count, Blueprints.Buildable(CompanyStage.Developer).Count);
            Assert.Less(Blueprints.Buildable(CompanyStage.Contractor).Count, Blueprints.Buildable(CompanyStage.Builder).Count);
            // lookups
            Assert.NotNull(Blueprints.Pick("garage", 7));
            Assert.Greater(Blueprints.Find("ranch", 1400, 1800, 80).Count, 2);
            var tw = Blueprints.GetBlueprint("twostory-2400");
            Assert.AreEqual(2400f, tw.SqFt, 0.01f, "Development's Elm stays 2,400 sq ft");
            Assert.AreEqual(1, tw.Stairs.Count, "the Elm now has a stair");
        }

        [Test]
        public void EveryPlanPassesTheValidator()
        {
            var sb = new StringBuilder(); int errors = 0, warns = 0;
            foreach (var p in Blueprints.All)
                foreach (var i in BlueprintValidator.Validate(p)) { if (i.Error) { errors++; sb.AppendLine(i.ToString()); } else warns++; }
            Debug.Log($"[Blueprints] validated {Blueprints.All.Count} plans: {errors} errors, {warns} warnings");
            Assert.AreEqual(0, errors, "validator errors:\n" + sb);
        }

        [Test]
        public void ValidatorCatchesBrokenPlans()
        {
            var b = new Blueprint { Id = "bad", Name = "Bad", Category = "ranch", WidthFt = 30, DepthFt = 20, Beds = 1, Baths2 = 2, FrontDoorFt = 8 };
            b.R("Living", 0, 0, 15, 20).R("Bed 2", 15, 0, 30, 12);   // gap behind the bedroom, no bath drawn
            b.Finish();
            var e = BlueprintValidator.Errors(b);
            Assert.IsTrue(e.Exists(s => s.Contains("[rooms]")), "gap found");
            Assert.IsTrue(e.Exists(s => s.Contains("[meta]")), "bath count mismatch found");
            var t = new Blueprint { Id = "bad2", Name = "Bad 2", Category = "two-story", WidthFt = 20, DepthFt = 20, Floors = 2, FrontDoorFt = 10 };
            t.R("Stair", 0, 0, 3, 8).R("Hall", 0, 8, 3, 20).R("Living", 3, 0, 20, 20);
            t.R("Stair", 0, 0, 3, 8, 1).R("Hall", 0, 8, 3, 20, 1).R("Living", 3, 0, 20, 20, 1);
            t.Finish();
            var e2 = BlueprintValidator.Errors(t);
            Assert.IsTrue(e2.Exists(s => s.Contains("[stairs]")), "an 8' stair room can't hold 14 risers: " + string.Join(" | ", e2));
        }

        [Test]
        public void TakeoffsAreSane()
        {
            var sb = new StringBuilder();
            foreach (var p in Blueprints.All)
            {
                var t = p.Takeoff; var b = Blueprints.Info(p);
                float per = t.Total / Mathf.Max(1f, p.SqFt);
                sb.AppendLine($"{p.Id,-18} {p.Category,-12} {p.SqFt,7:N0} sf  ${t.Total,10:N0}  ${per,5:0}/sf  {t.Lines.Count} lines  {t.ConcreteYards:0.#} yd  fixtures {b.Fixtures.Count}");
                Assert.GreaterOrEqual(t.Lines.Count, 8, p.Id);
                Assert.Greater(t.MaterialTotal, 0, p.Id);
                float lo, hi;
                switch (p.Group)
                {
                    case PlanGroup.Residential: lo = 90f; hi = 300f; break;
                    case PlanGroup.Outbuilding: lo = 15f; hi = 160f; break;
                    case PlanGroup.Commercial: lo = 90f; hi = 400f; break;
                    default: lo = 25f; hi = 220f; break;
                }
                Assert.That(per, Is.InRange(lo, hi), $"{p.Id} ${per:0}/sq ft outside {lo}-{hi} for {p.Group}");
            }
            Debug.Log("[Blueprints] takeoffs\n" + sb);
            // scale checks: a bigger building of the same kind costs more
            Assert.Greater(HousePlans.Get("warehouse-50000").Takeoff.Total, HousePlans.Get("warehouse-24000").Takeoff.Total);
            Assert.Greater(HousePlans.Get("garage-3car").Takeoff.Total, HousePlans.Get("garage-1car").Takeoff.Total);
            Assert.Greater(HousePlans.Get("shed-12x16").Takeoff.Total, HousePlans.Get("shed-8x10").Takeoff.Total);
        }

        [Test]
        public void CodeNumbersAreRight()
        {
            var diner = Blueprints.GetBlueprint("diner-3000");
            Assert.AreEqual("A-2", diner.OccLetter);
            Assert.That(diner.OccupantLoad, Is.InRange(50, 99), "diner under 100 occupants");
            Assert.AreEqual(2, diner.ExitsRequired);
            Assert.IsFalse(diner.SprinklersRequired);
            Assert.IsTrue(Blueprints.GetBlueprint("warehouse-24000").SprinklersRequired, "S-1 fire area > 12,000 sq ft");
            Assert.IsTrue(Blueprints.GetBlueprint("townhome-row4").SprinklersRequired, "IRC R313 townhouses");
            var elm = Blueprints.GetBlueprint("twostory-2400").Stairs[0];
            Assert.LessOrEqual(elm.RiserIn, 7.75f); Assert.AreEqual(10f, elm.TreadIn);
            var hq = Blueprints.GetBlueprint("yard-hq-7200").Stairs[0];
            Assert.LessOrEqual(hq.RiserIn, 7f, "IBC riser"); Assert.AreEqual(11f, hq.TreadIn, "IBC tread");
            var split = Blueprints.GetBlueprint("splitlevel-2280").Stairs[0];
            Assert.IsTrue(split.Split);
            Assert.AreEqual(2, Blueprints.GetBlueprint("garage-2car").GarageBays);
            Assert.AreEqual(3, Blueprints.GetBlueprint("garage-3car").GarageBays);
        }

        // ------------------------------------------------------------------ overlay

        [UnityTest]
        public IEnumerator OverlayBuildsForEveryPlanAndFloor()
        {
            yield return Frames(2);
            int shown = 0;
            foreach (var p in Blueprints.All)
                for (int f = 0; f < p.Floors; f++)
                {
                    var root = PlanOverlay.Show(p, At(), f);
                    Assert.NotNull(root, p.Id);
                    Assert.Greater(PlanOverlay.WallCount, 3, $"{p.Id} floor {f} walls");
                    Assert.AreEqual(p.Rooms.FindAll(r => r.Floor == f).Count, PlanOverlay.LabelCount, $"{p.Id} floor {f} room labels");
                    Assert.NotNull(root.transform.Find("BlueprintLayers"), $"{p.Id} blueprint layer");
                    var bp = Blueprints.Info(p);
                    Assert.AreEqual(bp.Fixtures.FindAll(x => x.Floor == f).Count, BlueprintOverlay.FixtureCount, $"{p.Id} fixtures drawn");
                    if (f == 0 && p.Category != "shed") Assert.Greater(BlueprintOverlay.FixtureCount, 0, p.Id + " MEP fixtures");
                    if (f == 0 && p.Floors > 1) Assert.Greater(BlueprintOverlay.StairCount, 0, p.Id + " stair drawn");
                    shown++;
                }
            PlanOverlay.Hide();
            Debug.Log($"[Blueprints] overlay built for {shown} plan floors");
        }

        static readonly (string id, int floor, bool eye)[] ShotList =
        {
            ("starter-1008", 0, true), ("ranch-1770-3car", 0, true), ("ranch-1500", 0, false), ("cottage-1200", 0, false), ("craftsman-1920", 0, false),
            ("twostory-2400", 0, false), ("twostory-2400", 1, false), ("estate-3500", 1, false), ("splitlevel-2280", 0, false), ("splitlevel-2280", 1, false),
            ("duplex-1664", 0, true), ("townhome-row4", 0, false), ("townhome-row4", 1, false), ("adu-480", 0, true), ("adu-carriage-672", 1, false),
            ("garage-3car", 0, true), ("garage-2car-shop", 0, false), ("shed-12x16", 0, true), ("polebarn-40x60", 0, false), ("barn-36x48", 0, true), ("barn-36x48", 1, false),
            ("workshop-40x60", 0, false), ("retail-2400", 0, true), ("diner-3000", 0, true), ("office-3000", 0, false), ("stripmall-6000", 0, false),
            ("gasstation-3200", 0, false), ("warehouse-12000", 0, true), ("warehouse-50000", 0, false), ("flex-6000", 0, false), ("yard-hq-7200", 0, true), ("yard-hq-7200", 1, false),
        };

        [UnityTest]
        public IEnumerator ScreenshotsOfRepresentativePlans()
        {
            yield return Frames(3);
            Pad();
            var cats = new HashSet<string>();
            foreach (var (id, floor, eye) in ShotList)
            {
                var p = HousePlans.Get(id); Assert.NotNull(p, id);
                cats.Add(p.Category);
                var at = At();
                PlanOverlay.Show(p, at, floor);
                yield return null;
                float w = p.WidthFt * Units.Foot, d = p.DepthFt * Units.Foot, s = Mathf.Max(w * 0.62f, d * 1.05f);
                var c = at.World(p.WidthFt / 2f, p.DepthFt / 2f);
                string tag = $"bp_{id}" + (p.Floors > 1 ? $"_f{floor + 1}" : "");
                Shot(Cam(c + new Vector3(0f, s + 3f, -s * 0.2f), c), tag);
                if (eye) Shot(Cam(at.World(-8f, -16f, 1.7f), c), tag + "_eye");
            }
            PlanOverlay.Hide();
            foreach (var cat in Blueprints.Categories) Assert.IsTrue(cats.Contains(cat), "screenshot for category " + cat);
        }
    }
}
