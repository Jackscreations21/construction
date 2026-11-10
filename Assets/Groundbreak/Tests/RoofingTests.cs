using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Roof framing + roofing: Fink trusses on a 16x12 stand-in, deck, dry-in, shingles; a 10/12 stick-framed hip;
    /// fall protection. Screenshots go to Logs/shots/roof_*.png.</summary>
    public class RoofingTests
    {
        static float I(float inch) => inch * Units.Inch;
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1280, 720, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
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
            var cam = new GameObject("TestRoofCam").AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.enabled = false;
            return cam;
        }

        static void Snap(Vector3 pos, Vector3 look, string name, float fov = 60f)
        {
            var c = Cam(pos, look, fov);
            Shot(c, name);
            Object.Destroy(c.gameObject);
        }

        /// <summary>Stand-in shell: foundation block, sheathed wall boxes and real double top plates (Framer members) to bear on.</summary>
        static Transform StandIn(Footprint fp)
        {
            var root = new GameObject("StandInShell").transform;
            root.SetPositionAndRotation(new Vector3(fp.Origin.x, 0f, fp.Origin.z), fp.Rotation);
            float L = fp.Length, W = fp.Width, P = fp.PlateTopY, wd = fp.WallDepth, deck = fp.DeckTopY;
            float wallTop = P - I(3f);
            Framer.AttachMesh(root, "Walls", (MeshBuilder b, System.Func<SurfaceKind, int> s) =>
            {
                int osb = s(SurfaceKind.Osb), conc = s(SurfaceKind.Concrete);
                b.AddBox(new Vector3(L / 2, deck / 2 - 0.05f, W / 2), new Vector3(L + 0.02f, deck + 0.1f, W + 0.02f), conc);
                float h = wallTop - deck, yc = deck + h / 2, sh = I(0.4375f);
                b.AddBox(new Vector3(L / 2, yc, wd / 2 - sh / 2), new Vector3(L, h, wd + sh), osb);
                b.AddBox(new Vector3(L / 2, yc, W - wd / 2 + sh / 2), new Vector3(L, h, wd + sh), osb);
                b.AddBox(new Vector3(wd / 2 - sh / 2, yc, W / 2), new Vector3(wd + sh, h, W - 2 * wd), osb);
                b.AddBox(new Vector3(L - wd / 2 + sh / 2, yc, W / 2), new Vector3(wd + sh, h, W - 2 * wd), osb);
            });
            float t = I(1.5f);
            string longId = RoofStock.Pick("2x4", L).id, endId = RoofStock.Pick("2x4", W - 2 * wd).id;
            foreach (var (y, role) in new[] { (P - t * 1.5f, MemberRole.TopPlate), (P - t * 0.5f, MemberRole.CapPlate) })
            {
                Framer.Make(longId, L, root, new Vector3(L / 2, y, wd / 2), Vector3.right, Vector3.up, role);
                Framer.Make(longId, L, root, new Vector3(L / 2, y, W - wd / 2), Vector3.right, Vector3.up, role);
                Framer.Make(endId, W - 2 * wd, root, new Vector3(wd / 2, y, W / 2), Vector3.forward, Vector3.up, role);
                Framer.Make(endId, W - 2 * wd, root, new Vector3(L - wd / 2, y, W / 2), Vector3.forward, Vector3.up, role);
            }
            return root;
        }

        static List<StructuralMember> Members(MemberRole role)
        {
            var l = new List<StructuralMember>();
            foreach (var m in Object.FindObjectsByType<StructuralMember>(FindObjectsSortMode.None)) if (m.Role == role && m.isActiveAndEnabled) l.Add(m);
            return l;
        }

        static void AssertSupported(MemberRole role, params MemberRole[] allowed)
        {
            SupportGraph.Rebuild();
            var list = Members(role);
            Assert.Greater(list.Count, 0, "no " + role);
            int bad = 0;
            foreach (var m in list)
            {
                bool ok = false;
                foreach (var s in m.SupportedBy) if (allowed.Length == 0 || System.Array.IndexOf(allowed, s.Role) >= 0) { ok = true; break; }
                if (!ok) { bad++; Debug.Log("[RoofTest] unsupported " + role + " '" + m.name + "' at " + m.transform.position); }
            }
            Assert.AreEqual(0, bad, bad + " of " + list.Count + " " + role + " members have no support");
        }

        [UnityTest]
        public IEnumerator GableTrussRoof_Shingled()
        {
            yield return Frames(5);
            FramingJob.RequireStock = false;
            var fp = Footprint.Make(new Vector3(40f, 0f, 40f), 0f, 16f, 12f);
            StandIn(fp);
            var job = RoofFraming.Trusses(fp, 6);
            var info = RoofFraming.InfoOf(job);
            Assert.NotNull(info, "RoofInfo missing on the truss job");
            Assert.AreEqual(2, info.Planes.Count);
            Assert.IsTrue(info.HasRidge);

            // boom truck: set up, then lift the first few trusses and photograph mid-swing
            for (int i = 0; i < 6; i++) Assert.IsTrue(job.TryAdvance(null, out var msg), msg);
            yield return new WaitForSeconds(4.0f);
            Assert.IsTrue(job.TryAdvance(null, out _));
            yield return new WaitForSeconds(1.1f);
            Vector3 c = new Vector3(40f + fp.Length / 2, 2.5f, 40f + fp.Width / 2);
            Snap(c + new Vector3(-9f, 5.5f, -12f), c + new Vector3(0, 0.5f, -2f), "roof_01_boom_truck");
            job.RunAll();
            BoomTruck.RemoveAll();
            yield return Frames(3);
            Assert.IsTrue(job.Done);
            Assert.IsTrue(info.Framed);

            var trusses = Members(MemberRole.Truss);
            Assert.AreEqual(9, trusses.Count, "16' at 24\" O.C.: 2 gable-end + 7 common trusses");
            AssertSupported(MemberRole.Truss, MemberRole.TopPlate, MemberRole.CapPlate);
            foreach (var t in trusses)
            {
                Assert.IsTrue(t.Has(Connector.HurricaneTie), "truss without hurricane ties");
                Assert.GreaterOrEqual(t.GetComponentsInChildren<MeshFilter>().Length, 3, "truss visual + 2 ties");
            }
            Assert.GreaterOrEqual(Members(MemberRole.SubFascia).Count, 4, "sub-fascia + fly rafters");
            Assert.GreaterOrEqual(Members(MemberRole.Fascia).Count, 6, "eave + rake fascia");
            Assert.Greater(Members(MemberRole.Lookout).Count, 4, "rake lookouts");
            Assert.AreEqual(4, info.Rakes.Count);
            Snap(c + new Vector3(-7.5f, 4.5f, -7f), c + new Vector3(0, 0.6f, 0), "roof_02_trusses");
            Snap(new Vector3(40f - 0.9f, 3.1f, 40f - 1.3f), new Vector3(40f + 0.6f, 2.85f, 40f + 0.2f), "roof_03_truss_heel_rake");

            // deck
            var sh = RoofFraming.RoofSheathing(info);
            sh.RunAll();
            yield return Frames(2);
            Assert.IsTrue(info.Sheathed);
            var panels = Members(MemberRole.RoofSheathing);
            Debug.Log("[RoofTest] sheathing panels: " + panels.Count);
            Assert.Greater(panels.Count, 8);
            AssertSupported(MemberRole.RoofSheathing);
            Assert.AreEqual(1, RoofAnchor.All.Count, "roof anchor at the ridge");
            Snap(c + new Vector3(-7.5f, 5.5f, -8f), c + new Vector3(0, 1f, 0), "roof_04_deck");

            // dry-in
            var dry = Roofing.DryIn(info);
            dry.RunAll();
            yield return Frames(2);
            Assert.IsTrue(info.DriedIn);
            Snap(c + new Vector3(-7.5f, 5.5f, -8f), c + new Vector3(0, 1f, 0), "roof_05_dry_in");
            Snap(new Vector3(40f + 1.0f, 3.7f, 40f - 1.3f), new Vector3(40f + 1.8f, 2.95f, 40f + 0.1f), "roof_06_dry_in_eave");

            // shingles
            var sg = Roofing.Shingles(info);
            int courseSteps = 0; foreach (var s in sg.Steps) if (s.Label.StartsWith("Shingle course")) courseSteps++;
            Assert.Greater(courseSteps, 20, "shingle courses on both planes");
            // run up to the middle of the shingle courses for an in-progress picture
            int guard = 0;
            while (!sg.Done && guard++ < 200 && !(sg.Current.Label.StartsWith("Shingle course 9/"))) sg.TryAdvance(null, out _);
            yield return Frames(2);
            Snap(new Vector3(40f + 1.4f, 4.2f, 40f - 1.6f), new Vector3(40f + 2.2f, 3.1f, 40f + 0.6f), "roof_07_courses_in_progress");
            sg.RunAll();
            yield return Frames(3);
            Assert.IsTrue(info.Shingled);
            Snap(c + new Vector3(-8f, 5f, -11f), c + new Vector3(0, 1f, 0), "roof_08_shingled_overview");
            Snap(c + new Vector3(-3f, 1.0f, -10f), c + new Vector3(0, 1.3f, 0), "roof_09_shingled_10m");
            Snap(new Vector3(40f + 1.6f, 3.75f, 40f - 1.2f), new Vector3(40f + 2.0f, 2.95f, 40f + 0.0f), "roof_10_eave_closeup");
            Snap(new Vector3(40f - 1.1f, 3.6f, 40f - 0.6f), new Vector3(40f - 0.2f, 3.1f, 40f + 0.7f), "roof_11_rake_edge");
            Snap(new Vector3(40f + 1.2f, 4.9f, 40f + 0.4f), new Vector3(40f + 2.2f, 3.95f, 40f + 1.83f), "roof_12_ridge");
            Snap(new Vector3(40f + 2.0f, 2.3f, 40f - 1.0f), new Vector3(40f + 2.4f, 2.9f, 40f - 0.1f), "roof_13_eave_from_below");
        }

        [UnityTest]
        public IEnumerator StickFramedHip_10_12()
        {
            yield return Frames(5);
            FramingJob.RequireStock = false;
            var fp = Footprint.Make(new Vector3(75f, 0f, 40f), 0f, 20f, 14f);
            StandIn(fp);
            var job = RoofFraming.StickFramed(fp, 10, true);
            var info = RoofFraming.InfoOf(job);
            Assert.NotNull(info);
            Assert.AreEqual(4, info.Planes.Count);
            Assert.AreEqual(4, info.Hips.Count);
            job.RunAll();
            yield return Frames(3);
            Assert.AreEqual(4, Members(MemberRole.HipRafter).Count, "4 hip rafters");
            Assert.Greater(Members(MemberRole.JackRafter).Count, 8, "jack rafters");
            Assert.Greater(Members(MemberRole.Rafter).Count, 6, "common rafters");
            Assert.GreaterOrEqual(Members(MemberRole.RidgeBoard).Count, 1, "ridge board");
            Assert.Greater(Members(MemberRole.CeilingJoist).Count, 8, "ceiling joists");
            AssertSupported(MemberRole.Rafter, MemberRole.TopPlate, MemberRole.CapPlate, MemberRole.RidgeBoard);
            AssertSupported(MemberRole.HipRafter, MemberRole.TopPlate, MemberRole.CapPlate);
            AssertSupported(MemberRole.JackRafter, MemberRole.TopPlate, MemberRole.CapPlate);
            AssertSupported(MemberRole.CeilingJoist);
            Vector3 c = new Vector3(75f + fp.Length / 2, 3f, 40f + fp.Width / 2);
            Snap(c + new Vector3(-6f, 4f, -7f), c + new Vector3(0, 0.4f, 0), "roof_20_hip_framing");
            Snap(new Vector3(75f - 1.4f, 4.4f, 40f - 1.6f), new Vector3(75f + 0.9f, 3.0f, 40f + 0.9f), "roof_21_hip_corner_framing");

            RoofFraming.RoofSheathing(info).RunAll();
            yield return Frames(2);
            AssertSupported(MemberRole.RoofSheathing);
            Snap(c + new Vector3(-8f, 6f, -10f), c + new Vector3(0, 0.6f, 0), "roof_22_hip_deck");
            Roofing.DryIn(info).RunAll();
            Roofing.Shingles(info).RunAll();
            yield return Frames(3);
            Assert.IsTrue(info.Shingled);
            Snap(c + new Vector3(-6f, 3.5f, -8f), c + new Vector3(0, 0.8f, 0), "roof_23_hip_shingled");
            Snap(new Vector3(75f - 1.3f, 4.2f, 40f - 1.5f), new Vector3(75f + 0.6f, 3.1f, 40f + 0.4f), "roof_24_hip_corner_closeup");

            // fall protection: 10/12 without a harness -> slide; clipped in -> stay put
            if (RoofSafety.I == null) new GameObject("RoofSafetyTest").AddComponent<RoofSafety>();
            var front = info.Planes[0];
            var player = Game.Player;
            Assert.NotNull(player);
            var cc = player.GetComponent<CharacterController>();
            void Put(Vector3 p) { cc.enabled = false; player.transform.position = p; cc.enabled = true; }
            RoofSafety.ClippedIn = false;
            Put(front.At(front.EaveLength * 0.5f, front.SlopeLength * 0.6f, 0.12f));
            float v0 = front.ToLocal(player.transform.position).y;
            bool slipped = false;
            for (int i = 0; i < 30; i++) { yield return null; slipped |= RoofSafety.Slipping; }
            float v1 = front.ToLocal(player.transform.position).y;
            Debug.Log("[RoofTest] slip: v " + v0 + " -> " + v1 + " slipping=" + slipped);
            Assert.IsTrue(slipped, "player should slip on a 10/12 roof without protection");
            Assert.Less(v1, v0 - 0.05f, "player should slide down-slope");

            var anchor = RoofAnchor.Nearest(front.At(front.EaveLength * 0.5f, front.SlopeLength), 30f);
            Assert.NotNull(anchor, "anchor");
            Put(front.At(front.EaveLength * 0.5f, front.SlopeLength * 0.6f, 0.12f));
            var harness = new SafetyHarness();
            RoofSafety.ClippedIn = true; RoofSafety.Anchor = anchor;
            yield return Frames(20);
            Assert.IsFalse(RoofSafety.Slipping, "clipped in: no slipping");
            RoofSafety.ClippedIn = false; RoofSafety.Anchor = null;
            Put(new Vector3(0, 0.1f, -4f));
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator StickFramedGable_And_Jacks()
        {
            yield return Frames(5);
            FramingJob.RequireStock = false;
            var fp = Footprint.Make(new Vector3(40f, 0f, 75f), 0f, 16f, 12f);
            StandIn(fp);
            var job = RoofFraming.StickFramed(fp, 8, false);
            job.RunAll();
            yield return Frames(2);
            var info = RoofFraming.InfoOf(job);
            AssertSupported(MemberRole.CollarTie, MemberRole.Rafter);
            AssertSupported(MemberRole.GableStud, MemberRole.TopPlate, MemberRole.CapPlate);
            Vector3 c = new Vector3(40f + fp.Length / 2, 3f, 75f + fp.Width / 2);
            Snap(c + new Vector3(-5f, 3.5f, -6f), c + new Vector3(0, 0.4f, 0), "roof_30_stick_gable_framing");
            Snap(new Vector3(40f + 1.2f, 2.6f, 75f + 1.0f), new Vector3(40f + 2.4f, 3.2f, 75f + 1.8f), "roof_31_stick_gable_attic");
            RoofFraming.RoofSheathing(info).RunAll();
            var jacks = Roofing.RoofJacks(info, 0, 1.2f, 1f, 4f);
            jacks.RunAll();
            yield return Frames(2);
            Assert.GreaterOrEqual(RoofJack.All.Count, 3);
            var flash = Roofing.StepFlashing(info, 0, 2.5f, true, 0.3f, 1.5f);
            flash.RunAll();
            Assert.Greater(flash.Steps.Count, 3);
            yield return Frames(2);
            Snap(c + new Vector3(-6f, 4.5f, -7f), c + new Vector3(0, 0.8f, 0), "roof_32_jacks");
        }
    }
}
