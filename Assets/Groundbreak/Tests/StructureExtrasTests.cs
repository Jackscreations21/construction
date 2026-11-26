using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Structure leftovers: detailed window/door/garage models, I-joist floor, roof valleys.
    /// Screenshots in Logs/shots/extras_*.</summary>
    public class StructureExtrasTests
    {
        static readonly Vector3 O = new Vector3(-400f, 0f, 400f);
        static GameObject _root;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ExtrasRoot");
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var l = new GameObject("Sun").AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.1f; l.shadows = LightShadows.Soft;
                l.transform.SetParent(_root.transform);
                l.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "ExtrasGround";
            ground.transform.SetParent(_root.transform);
            ground.transform.position = O + Vector3.down * 0.001f;
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            ground.GetComponent<Renderer>().sharedMaterial = Materials.Get(SurfaceKind.Concrete);
        }

        [TearDown]
        public void TearDown() { if (_root != null) Object.Destroy(_root); }

        static GameObject Model(string id, Vector3 pos)
        {
            var def = Catalog.Get(id);
            Assert.IsNotNull(def, id);
            var holder = new GameObject(id);
            holder.transform.SetParent(_root.transform);
            holder.transform.position = pos;
            ProductModels.Build(def, def.Size, holder.transform);
            return holder;
        }

        static void Snap(Vector3 camPos, Vector3 look, string name, float fov = 45f)
        {
            var cam = StructurePolishTests.Cam(camPos, look, fov);
            cam.transform.SetParent(_root.transform, true);
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.82f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            StructurePolishTests.Shot(cam, name);
        }

        [UnityTest]
        public IEnumerator OpeningModels_DetailedAndRealSize()
        {
            string[] ids = { "win-dh-3254", "win-sh-3648", "win-slider-4836", "door-ext-steel36", "door-slider72", "garage-9x7" };
            foreach (var id in ids) Assert.IsTrue(OpeningModels.Handles(Catalog.Get(id)), id + " uses the detailed model");

            var dh = Model("win-dh-3254", O + new Vector3(0f, 1.2f, 0f));
            var sl = Model("win-slider-4836", O + new Vector3(1.6f, 1.2f, 0f));
            var door = Model("door-ext-steel36", O + new Vector3(4f, Units.In(40f), 0f));
            var patio = Model("door-slider72", O + new Vector3(7f, Units.In(40f), 0f));
            var garage = Model("garage-9x7", O + new Vector3(12f, Units.In(42f), 0f));
            yield return Frames(2);

            // real sizes: window fin 1-1/4" past the frame, door unit fits its rough opening, glass present
            var dhB = dh.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(Units.In(32f + 2.5f), dhB.size.x, Units.In(0.1f), "DH window: 32\" unit + 1-1/4\" fins");
            var doorB = door.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
            Assert.Less(doorB.size.y, Units.In(82.5f + 2.3f), "door unit + brickmold height");
            float jambOut = Units.In(18.6875f);
            Assert.Less(jambOut * 2f, Units.In(38f), "outside of jambs fits the 38\" rough opening");
            bool glass = false;
            foreach (var m in dh.GetComponentInChildren<MeshRenderer>().sharedMaterials) if (m != null && m.name.Contains("Glass")) glass = true;
            Assert.IsTrue(glass, "window has glass");
            foreach (var g in new[] { dh, sl, door, patio, garage })
                Assert.Greater(g.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount, 500, g.name + " is a detailed model");

            Snap(O + new Vector3(0.35f, 1.45f, 1.3f), O + new Vector3(0f, 1.2f, 0f), "extras_01_dh_window_ext");
            Snap(O + new Vector3(-0.25f, 1.35f, -0.9f), O + new Vector3(0f, 1.25f, 0f), "extras_02_dh_window_int");
            Snap(O + new Vector3(0.3f, 1.4f, 0.55f), O + new Vector3(0.1f, 1.05f, 0f), "extras_03_dh_corner_closeup", 40f);
            Snap(O + new Vector3(1.9f, 1.3f, 1.5f), O + new Vector3(1.6f, 1.2f, 0f), "extras_04_slider_window");
            Snap(O + new Vector3(4.6f, 1.3f, 2.6f), O + new Vector3(4f, 1.0f, 0f), "extras_05_prehung_door_ext");
            Snap(O + new Vector3(4.5f, 1.2f, -1.1f), O + new Vector3(4.3f, 0.95f, 0f), "extras_06_prehung_door_int_bores", 40f);
            Snap(O + new Vector3(3.75f, 0.35f, 0.6f), O + new Vector3(4f, 0.02f, 0f), "extras_07_threshold_closeup", 45f);
            Snap(O + new Vector3(7.6f, 1.3f, 3.2f), O + new Vector3(7f, 1.0f, 0f), "extras_08_patio_slider");
            Snap(O + new Vector3(12.8f, 1.4f, 4.6f), O + new Vector3(12f, 1.1f, 0f), "extras_09_garage_ext", 50f);
            Snap(O + new Vector3(10.3f, 1.6f, -4.2f), O + new Vector3(12f, 1.6f, -0.5f), "extras_10_garage_tracks_springs", 60f);
        }

        /// <summary>Main-house stand-in: slab/deck block, OSB wall boxes and double top plates (roof bearing).</summary>
        static void StandIn(Footprint fp)
        {
            var root = new GameObject("ExtrasStandIn").transform;
            root.SetParent(_root.transform, true);
            root.SetPositionAndRotation(new Vector3(fp.Origin.x, 0f, fp.Origin.z), fp.Rotation);
            float L = fp.Length, W = fp.Width, Pt = fp.PlateTopY, wd = fp.WallDepth, deck = fp.DeckTopY, t = Units.In(1.5f);
            float wallTop = Pt - 2 * t;
            Framer.AttachMesh(root, "Walls", (MeshBuilder b, System.Func<SurfaceKind, int> s) =>
            {
                int osb = s(SurfaceKind.Osb), conc = s(SurfaceKind.Concrete);
                b.AddBox(new Vector3(L / 2, deck / 2 - 0.05f, W / 2), new Vector3(L + 0.02f, deck + 0.1f, W + 0.02f), conc);
                float h = wallTop - deck, yc = deck + h / 2, sh = Units.In(0.4375f);
                b.AddBox(new Vector3(L / 2, yc, wd / 2 - sh / 2), new Vector3(L, h, wd + sh), osb);
                b.AddBox(new Vector3(L / 2, yc, W - wd / 2 + sh / 2), new Vector3(L, h, wd + sh), osb);
                b.AddBox(new Vector3(wd / 2 - sh / 2, yc, W / 2), new Vector3(wd + sh, h, W - 2 * wd), osb);
                b.AddBox(new Vector3(L - wd / 2 + sh / 2, yc, W / 2), new Vector3(wd + sh, h, W - 2 * wd), osb);
            });
            string longId = RoofStock.Pick("2x4", L).id, endId = RoofStock.Pick("2x4", W - 2 * wd).id;
            foreach (var (y, role) in new[] { (Pt - t * 1.5f, MemberRole.TopPlate), (Pt - t * 0.5f, MemberRole.CapPlate) })
            {
                Framer.Make(longId, L, root, new Vector3(L / 2, y, wd / 2), Vector3.right, Vector3.up, role);
                Framer.Make(longId, L, root, new Vector3(L / 2, y, W - wd / 2), Vector3.right, Vector3.up, role);
                Framer.Make(endId, W - 2 * wd, root, new Vector3(wd / 2, y, W / 2), Vector3.forward, Vector3.up, role);
                Framer.Make(endId, W - 2 * wd, root, new Vector3(L - wd / 2, y, W / 2), Vector3.forward, Vector3.up, role);
            }
        }

        [UnityTest]
        public IEnumerator CrossGable_ValleysFramedAndClosedCut()
        {
            bool req = FramingJob.RequireStock;
            FramingJob.RequireStock = false;
            var fp = Footprint.Make(O + new Vector3(30f, 0f, 0f), 0f, 32f, 24f);
            StandIn(fp);
            var roofJob = RoofFraming.Trusses(fp, 6, 12, 12, false);
            var info = RoofFraming.InfoOf(roofJob);
            roofJob.RunAll();
            var sheath = RoofFraming.RoofSheathing(info); sheath.RunAll();
            yield return Frames(2);

            var cg = CrossGable.Frame(info, fp.Length / 2, 14f, 20f);
            cg.FramingJob.transform.SetParent(_root.transform, true);
            Assert.AreEqual(2, cg.Valleys.Count);
            Assert.GreaterOrEqual(info.Valleys.Count, 2, "valleys published on the main RoofInfo");
            // valley geometry: 45 deg in plan for equal pitches, from the inside corner to the wing ridge on the main deck
            var v = cg.Valleys[0];
            Vector3 d = v.B - v.A; d.y = 0f;
            Assert.AreEqual(45f, Vector3.Angle(d, fp.AlongX), 0.5f);
            // frame everything but the deck, photograph the valley framing, then sheathe
            var fj = cg.FramingJob;
            int deckSteps = fj.Steps.FindAll(s => s.Label.StartsWith("Wing roof sheathing")).Count;
            for (int i = 0; i < fj.Steps.Count - deckSteps; i++) fj.Steps[i].Do?.Invoke();
            fj.Next = fj.Steps.Count - deckSteps;
            yield return Frames(2);
            var v0 = cg.Valleys[0];
            Vector3 vm = Vector3.Lerp(v0.A, v0.B, 0.45f);
            Snap(vm + new Vector3(-1.3f, 1.3f, -2.2f), vm + new Vector3(0.5f, -0.1f, 0.2f), "extras_14_valley_framing", 55f);
            fj.RunAll();
            yield return Frames(2);
            Assert.IsTrue(cg.Framed);
            var members = cg.FramingJob.GetComponentsInChildren<StructuralMember>();
            int jacks = 0, valleyBoards = 0, commons = 0, ridge = 0;
            foreach (var m in members)
            {
                if (m.Role == MemberRole.JackRafter) jacks++;
                if (m.Role == MemberRole.ValleyRafter) valleyBoards++;
                if (m.Role == MemberRole.Rafter) commons++;
                if (m.Role == MemberRole.RidgeBoard) ridge++;
            }
            Assert.AreEqual(2, valleyBoards);
            Assert.AreEqual(1, ridge);
            Assert.Greater(jacks, 6, "valley jacks both sides");
            Assert.Greater(commons, 10, "commons over the wing");
            Vector3 vc = Vector3.Lerp(v.A, v.B, 0.35f);

            var dry = Roofing.DryIn(info); dry.RunAll();
            var sh = Roofing.Shingles(info); sh.RunAll();
            var wr = CrossGable.Roof(cg); wr.transform.SetParent(_root.transform, true); wr.RunAll();
            yield return Frames(3);
            Assert.IsTrue(cg.Roofed);
            Assert.IsNotNull(GameObject.Find("Main courses across the valley"), "main courses woven across the valley");
            Snap(vc + new Vector3(-1.2f, 2.2f, -2.6f), vc + new Vector3(0.2f, 0.1f, 0.2f), "extras_15_valley_closed_cut", 50f);
            Vector3 cen = fp.World(fp.Length / 2, 0f, 0f);
            Snap(cen + new Vector3(-9f, 7f, -14f), cen + new Vector3(0f, 2.8f, 0.5f), "extras_16_L_house_cross_gable", 55f);
            foreach (var r in Object.FindObjectsByType<RoofInfo>(FindObjectsSortMode.None)) Object.Destroy(r.gameObject);
            foreach (var j in new[] { roofJob, sheath, dry, sh }) if (j != null) Object.Destroy(j.gameObject);
            FramingJob.RequireStock = req;
        }

        static void RunSteps(FramingJob job, System.Func<BuildStep, bool> which)
        {
            foreach (var s in job.Steps) if (which(s)) s.Do?.Invoke();
        }

        [Test]
        public void IJoist_DepthFromSpan()
        {
            Assert.AreEqual(9.5f, IJoistFloor.DepthForSpan(14 * 12 - 2.5f), 1e-4f);
            Assert.AreEqual(11.875f, IJoistFloor.DepthForSpan(18 * 12 - 2.5f), 1e-4f);
            var fp = Footprint.Make(Vector3.zero, 0f, 24, 14, true);
            Assert.IsTrue(fp.IJoist);
            Assert.AreEqual(Units.In(1.5f + 9.5f + 0.71875f), fp.DeckTopY, 1e-4f, "deck height follows the I-joist depth");
        }

        [UnityTest]
        public IEnumerator IJoistFloor_GirderHangersStiffenersSquashBlocks_PassInspection()
        {
            var fp = Footprint.Make(O + new Vector3(-30f, 0f, 0f), 0f, 16, 28, true);
            Assert.AreEqual(11.875f, fp.IJoistDepthIn, 1e-4f, "28' wide: 11-7/8\" I-joists");
            var openings = new[] { new List<WallOpening>(), new List<WallOpening>(), new List<WallOpening>(), new List<WallOpening>() };
            openings[0].Add(new WallOpening { Product = Catalog.Get("door-ext-steel36"), CenterIn = 60f });
            openings[1].Add(new WallOpening { Product = Catalog.Get("win-sh-3648"), CenterIn = 100f });
            var job = IJoistFloor.Build(fp, openings);
            job.transform.root.SetParent(_root.transform, true);
            RunSteps(job, s => !s.Label.StartsWith("Glue"));
            yield return Frames(2);

            var root = job.transform.root;
            var members = root.GetComponentsInChildren<StructuralMember>();
            int ij = 0, hung = 0, beams = 0, squash = 0, rims = 0;
            foreach (var m in members)
            {
                string id = m.Piece != null ? m.Piece.Def.Id : "";
                if (id.StartsWith("ij")) { ij++; if (m.GetComponent<IJoistEnd>() != null) hung++; }
                if (m.Role == MemberRole.Beam) beams++;
                if (m.Role == MemberRole.SquashBlock) squash++;
                if (m.Role == MemberRole.RimJoist) { rims++; StringAssert.StartsWith("lvlrim12", id); }
            }
            Assert.Greater(ij, 20, "I-joists both sides of the girder");
            Assert.AreEqual(ij, hung, "every I-joist end at the girder is hung + stiffened");
            Assert.AreEqual(3, beams, "3-ply LVL girder");
            Assert.AreEqual(4, squash, "squash blocks under 2 openings x 2 jacks");
            Assert.GreaterOrEqual(rims, 4);

            // shots: overview before the subfloor, close-up of hangers + stiffeners, squash blocks at the rim
            Vector3 g = fp.World(Units.In(64f), 0f, fp.Width / 2);
            Snap(fp.World(-1.2f, 2.6f, -1.5f), fp.World(fp.Length * 0.45f, 0f, fp.Width * 0.45f), "extras_11_ijoist_floor_overview", 60f);
            Snap(g + new Vector3(0.55f, 0.75f, -0.75f), g + new Vector3(0f, 0.12f, 0f), "extras_12_ijoist_hangers_stiffeners", 45f);
            Vector3 sq = fp.World(Units.In(60f - 20.75f), 0f, Units.In(2f));
            Snap(sq + new Vector3(0.5f, 0.6f, 0.65f), sq + new Vector3(0f, 0.12f, 0f), "extras_13_ijoist_squash_blocks_rim", 50f);

            RunSteps(job, s => s.Label.StartsWith("Glue"));
            job.Next = job.Steps.Count;
            yield return Frames(2);
            float top = float.MinValue;
            foreach (var m in root.GetComponentsInChildren<StructuralMember>())
                if (m.Role == MemberRole.Subfloor && m.GetBox(out var c, out var h, out var r)) top = Mathf.Max(top, c.y + h.y);
            Assert.AreEqual(fp.DeckTopY, top, Units.In(0.1f), "subfloor top = footprint deck height");

            FramingInspection.Run();
            var mine = new HashSet<StructuralMember>(root.GetComponentsInChildren<StructuralMember>());
            var fails = FramingInspection.Last.FindAll(i => i.Member != null && mine.Contains(i.Member) && i.Severity != StructuralStatus.Ok);
            Assert.AreEqual(0, fails.Count, "I-joist floor inspection: " + string.Join("; ", fails.ConvertAll(i => i.Note)));

            // pull a stiffener off: inspection flags it
            foreach (var e in root.GetComponentsInChildren<IJoistEnd>()) { e.Stiffened = false; break; }
            FramingInspection.Run();
            Assert.IsTrue(FramingInspection.Last.Exists(i => i.Note.Contains("web stiffeners")), "missing stiffener is flagged");
        }
    }
}
