using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Milestone 3: frames a 12'x16' one-room building on the ground, stage by stage, and photographs it.</summary>
    public class ShellFramingTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        public static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        static Camera Cam(Vector3 pos, Vector3 lookAt, float fov = 60f)
        {
            var cam = new GameObject("TestCam").AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.enabled = false;
            return cam;
        }

        static int Count(MemberRole r)
        {
            int n = 0;
            foreach (var m in SupportGraph.Members) if (m.Role == r) n++;
            return n;
        }

        static int CountIn(Transform root, MemberRole r)
        {
            int n = 0;
            foreach (var m in root.GetComponentsInChildren<StructuralMember>()) if (m.Role == r) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator Frame12x16Shell()
        {
            yield return Frames(5);
            FramingJob.RequireStock = false;
            FramingJobRunner.Instant = true;
            if (Game.Player != null) Game.Player.transform.position = new Vector3(-10f, 0f, -10f); // out of the way

            var origin = new Vector3(20f, 0f, 20f);
            var fp = Footprint.Make(origin, 0f, 16, 12);
            var p = ShellProject.Start(fp);
            p.Openings[0].Add(new WallOpening { Product = Catalog.Get("door-ext-steel36"), CenterIn = 60f });
            p.Openings[0].Add(new WallOpening { Product = Catalog.Get("win-sh-3648"), CenterIn = 138f });
            p.Openings[2].Add(new WallOpening { Product = Catalog.Get("win-sh-3648"), CenterIn = 96f });
            p.Openings[1].Add(new WallOpening { Product = Catalog.Get("win-sh-2436"), CenterIn = 68.5f });
            Vector3 mid = fp.World(fp.Length / 2, 1.2f, fp.Width / 2);
            var overview = Cam(origin + new Vector3(-5.5f, 4.2f, -6.5f), mid);

            // --- Floor deck ---
            var floor = p.StartFloor();
            int half = floor.Steps.Count / 2;
            for (int i = 0; i < half; i++) floor.TryAdvance(null, out _);
            yield return Frames(2);
            Shot(overview, "30_floor_joists");
            Shot(Cam(fp.World(-0.8f, 0.6f, -0.9f), fp.World(1.2f, 0.1f, 1.0f)), "31_sill_rim_closeup");
            floor.RunAll();
            Assert.IsTrue(floor.Done);
            var froot = floor.transform.parent;
            Assert.AreEqual(11, CountIn(froot, MemberRole.Joist) - 2, "joists at 16\" O.C. between the end joists");
            Assert.AreEqual(4, CountIn(froot, MemberRole.SillPlate));
            Assert.AreEqual(7, CountIn(froot, MemberRole.Subfloor), "3 rows of 4x8 with staggered seams");
            yield return Frames(2);
            Shot(overview, "32_floor_deck");

            // --- Walls: one at a time, framed flat, tilted, braced ---
            var front = p.StartWall(0);
            int nail = 0; while (!front.Done && !front.Current.Label.StartsWith("Square")) { front.TryAdvance(null, out _); nail++; }
            yield return Frames(2);
            Shot(Cam(fp.World(1.5f, 3.2f, -1.6f), fp.World(2.6f, fp.DeckTopY, 1.3f)), "33_front_wall_flat");
            Shot(Cam(fp.World(1.2f, 1.1f, 0.5f), fp.World(1.4f, fp.DeckTopY, 0.0f)), "34_layout_marks_closeup");
            front.RunAll();
            var w0 = p.Walls[0];
            Assert.AreEqual(WallState.Braced, w0.State);
            Assert.AreEqual(0f, w0.Tilt, 0.01f);
            Assert.AreEqual(1, w0.Openings.Count == 2 ? 1 : 0, "front wall has door + window openings");
            for (int i = 1; i < 4; i++) p.StartWall(i).RunAll();
            Assert.IsTrue(p.WallsDone);
            p.StartCaps().RunAll();
            yield return Frames(2);
            Shot(overview, "35_walls_framed");
            Shot(Cam(fp.World(fp.Length * 0.25f, 1.7f, -2.2f), fp.World(fp.Length * 0.3f, 1.3f, 0f)), "36_door_window_framing");

            // headers sized by span
            int headers = 0; foreach (var w in p.Walls) headers += CountIn(w.transform, MemberRole.Header);
            Assert.GreaterOrEqual(headers, 4 * 3, "each opening has a 2-ply header with spacer");
            Assert.AreEqual(4, RoughOpening.All.Count, "rough openings registered");

            // --- Sheathing, flashing, units ---
            p.StartSheathing().RunAll();
            foreach (var j in p.FlashOpenings()) j.RunAll();
            foreach (var ro in new List<RoughOpening>(RoughOpening.All))
            {
                var def = ro.Spec.Product;
                Assert.IsTrue(ro.Fits(def, out string why), why);
                var piece = Piece.Create(def, 0f, ro.Center + ro.Outward * 1f, Quaternion.identity);
                RoughOpening.OnPlaced(piece);
                Assert.AreEqual(piece, ro.Installed, "unit snapped into " + ro.name);
            }
            foreach (var j in new List<FramingJob>(FramingJob.All)) if (!j.Done && j.Title.StartsWith("Install")) j.RunAll();
            yield return Frames(2);
            Shot(overview, "37_sheathed");

            // --- Roof (provided by the roofing module through ShellProject hooks) ---
            if (ShellProject.RoofFramingHook != null)
            {
                p.PitchRise = 6;
                p.StartRoof().RunAll();
                Shot(overview, "38_roof_framed");
                p.StartRoofSheathing()?.RunAll();
                p.StartDryIn()?.RunAll();
                Shot(overview, "39_dried_in");
                p.StartShingles()?.RunAll();
                yield return Frames(3);
            }
            else Debug.LogWarning("[Test] roofing module not present - roof skipped");

            // --- Inspection: no structural failures ---
            var issues = FramingInspection.Run();
            int red = 0;
            var area = new Bounds(fp.World(fp.Length / 2, 3f, fp.Width / 2), new Vector3(fp.Length + 3f, 8f, fp.Width + 3f));
            foreach (var i in issues)
            {
                if (!area.Contains(i.Where)) continue;
                if (i.Severity == StructuralStatus.Unsupported) red++;
                Debug.Log("[Inspection] " + i.Note + " @ " + i.Where);
            }
            Assert.AreEqual(0, red, "unsupported members:\n" + FramingInspection.Report());
            Debug.Log("[Test] members: " + SupportGraph.Members.Count + "  inspection: " + FramingInspection.Report());

            Shot(overview, "40_shell_overview");
            Shot(Cam(origin + new Vector3(fp.Length + 5f, 2.2f, fp.Width + 4f), mid), "41_shell_back");
            Shot(Cam(fp.World(fp.Length * 0.4f, 1.6f, -3.2f), fp.World(fp.Length * 0.4f, 1.6f, 0f), 55f), "42_front_elevation");
            Shot(Cam(fp.World(fp.Length * 0.5f, 1.65f, fp.Width * 0.5f), fp.World(0.3f, 1.4f, 0f), 75f), "43_interior");
        }

        [UnityTest]
        public IEnumerator GhostStructuralChecks()
        {
            yield return Frames(3);
            FramingJob.RequireStock = false;
            var fp = Footprint.Make(new Vector3(-30f, 0f, -30f), 0f, 8, 8);
            ShellProject.Start(fp).StartFloor().RunAll();
            yield return Frames(2);
            var stud = Catalog.Get("2x4x925");
            Quaternion upright = Quaternion.LookRotation(Vector3.up, Vector3.right);
            Vector3 size = new Vector3(stud.Size.x, stud.Size.y, stud.Size.z);

            // floating in mid-air: unsupported (red ghost, can't place)
            var st = SupportGraph.CheckGhost(stud, fp.World(1f, 5f, 1f), upright, size, out string why);
            Assert.AreEqual(StructuralStatus.Unsupported, st, why);

            // bottom plate on the deck, studs stand on it
            Framer.Make("2x4x8", Units.In(96f), null, fp.World(1.3f, fp.DeckTopY + Units.In(0.75f), 1f), Vector3.right, Vector3.up, MemberRole.BottomPlate);
            yield return null;
            float yc = fp.DeckTopY + Units.In(1.5f) + size.z / 2;
            st = SupportGraph.CheckGhost(stud, fp.World(Units.In(20f), yc, 1f), upright, size, out why);
            Assert.AreNotEqual(StructuralStatus.Unsupported, st, why);

            // a placed stud and a second one 20" away: wrong O.C. → warning ("will fail inspection")
            var s1 = Framer.Make("2x4x925", size.z, null, fp.World(Units.In(20f), yc, 1f), Vector3.up, Vector3.right, MemberRole.Stud);
            yield return null;
            st = SupportGraph.CheckGhost(stud, fp.World(Units.In(40f), yc, 1f), upright, size, out why);
            Assert.AreEqual(StructuralStatus.Warning, st, "20\" O.C. should warn: " + why);
            st = SupportGraph.CheckGhost(stud, fp.World(Units.In(36f), yc, 1f), upright, size, out why);
            Assert.AreEqual(StructuralStatus.Ok, st, "16\" O.C. is fine: " + why);

            // the placement controller uses the same check through its extension hook
            Assert.NotNull(PlacementController.ExternalCheck);
            var r = PlacementController.ExternalCheck(stud, fp.World(1f, 5f, 1f), upright, size);
            Assert.AreEqual(2, r.level);
            Object.Destroy(s1.gameObject);
        }

        [Test]
        public void HeaderSizing()
        {
            Assert.AreEqual("2x6", StructuralRules.HeaderFor(32f).lumber);
            Assert.AreEqual("2x8", StructuralRules.HeaderFor(38f).lumber);
            Assert.AreEqual("2x10", StructuralRules.HeaderFor(72f).lumber);
            Assert.AreEqual("2x12", StructuralRules.HeaderFor(74f).lumber);
            var o = new WallOpening { Product = Catalog.Get("win-sh-3648") };
            Assert.AreEqual(36.5f, o.RoughWidthIn, 0.01f);
            Assert.AreEqual(34f, o.SillTopIn, 0.01f);
            var d = new WallOpening { Product = Catalog.Get("door-ext-steel36") };
            Assert.AreEqual(38f, d.RoughWidthIn, 0.01f);
        }
    }
}
