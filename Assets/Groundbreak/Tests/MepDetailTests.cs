using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>MEP + site detail pass: the 200 A load center (cover off / on with directory), device boxes, devices and cover plates,
    /// duct seams/joints, and dig/gravel/pour dust. Close-up screenshots in Logs/shots/detail_*.</summary>
    public class MepDetailTests
    {
        [UnitySetUp] public IEnumerator SetUp() { yield return MepTestScene.Clear(); }
        [UnityTearDown] public IEnumerator TearDown() { yield return MepTestScene.Clear(); }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static float I(float x) => Units.In(x);

        public static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        public static Camera Cam(Vector3 pos, Vector3 lookAt, float fov = 50f)
        {
            var cam = new GameObject("TestDetailCam").AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.01f;
            cam.enabled = false;
            return cam;
        }

        static Piece P(string id, Vector3 pos, Quaternion rot, float len = 0f)
        {
            var def = Catalog.Get(id);
            Assert.NotNull(def, "missing catalog id " + id);
            var p = Piece.Create(def, len, pos, rot);
            p.Fasten();
            MepAutoAttach.AttachNow(p);
            return p;
        }

        static NetRun Pull(string spec, List<Vector3> pts, NetPort a, NetPort b)
        {
            var res = RunBuilder.Route(RunSpecs.Get(spec), pts, a, b);
            Assert.IsTrue(res.Ok, "route failed: " + string.Join("; ", res.Warnings));
            return res.Runs[0];
        }

        [UnityTest]
        public IEnumerator PanelAndDevices()
        {
            yield return Frames(10);
            MepStock.Free = true;
            if (GameClock.I != null) GameClock.I.Hours = 11f;
            LogAssert.ignoreFailingMessages = false;

            Vector3 O = new Vector3(-30f, 0f, 30f);
            Vector3 X(float xIn, float yIn, float zIn = 0f) => O + new Vector3(I(xIn), I(yIn), I(zIn));
            var yaw180 = Quaternion.Euler(0, 180, 0);
            // backer: a sheet of plywood behind everything so the shots read like a wall
            var panelP = P("el-panel200", X(0, 54f, 0), yaw180);
            var panel = panelP.GetComponent<ElPanel>();
            Assert.NotNull(panel);
            ElBox Box(float x, float y) { var p = P("el-box-1g", X(x, y, -2f + ElBox.D1g / 2f - 0.5f), yaw180); return p.GetComponent<ElBox>(); }
            var o1 = Box(-20f, 16f);
            var o2 = Box(-34f, 16f);
            var s1 = Box(-20f, 48f);

            var pb = panel.Node.Port("branch-bot");
            Vector3 o1t = o1.Node.Port("top-b").WorldPos;
            var h1 = Pull("nm-12-2", new List<Vector3> { pb.WorldPos, new Vector3(pb.WorldPos.x, I(24f), pb.WorldPos.z), new Vector3(o1t.x, I(24f), pb.WorldPos.z), o1t }, pb, o1.Node.Port("top-b"));
            Vector3 a = o1.Node.Port("top-a").WorldPos, b2 = o2.Node.Port("top-b").WorldPos;
            Pull("nm-12-2", new List<Vector3> { a, new Vector3(a.x, I(26f), a.z), new Vector3(b2.x, I(26f), a.z), b2 }, o1.Node.Port("top-a"), o2.Node.Port("top-b"));
            var pt = panel.Node.Port("branch-top");
            Vector3 s1t = s1.Node.Port("top-b").WorldPos;
            var h2 = Pull("nm-14-2", new List<Vector3> { pt.WorldPos, new Vector3(pt.WorldPos.x, I(76f), pt.WorldPos.z), new Vector3(s1t.x, I(76f), pt.WorldPos.z), s1t }, pt, s1.Node.Port("top-b"));

            foreach (var bx in new[] { o1, o2, s1 }) { ElTools.Strip(bx); for (int k = 0; k < 3; k++) ElTools.Splice(bx); }
            StringAssert.StartsWith("Installed", ElTools.Install(o1, ElDevice.Receptacle20));
            StringAssert.StartsWith("Installed", ElTools.Install(o2, ElDevice.Gfci));
            StringAssert.StartsWith("Installed", ElTools.Install(s1, ElDevice.Switch));
            StringAssert.StartsWith("Cover plate", o1.InstallPlate());
            StringAssert.StartsWith("Cover plate", s1.InstallPlate());

            var b15 = panel.Install("el-bkr-15-1p-afci", 1);
            var b20 = panel.Install("el-bkr-20-1p-df", 2);
            var b30 = panel.Install("el-bkr-30-2p", 5);
            var bg = panel.Install("el-bkr-20-1p-gfci", 4);
            Assert.NotNull(b15); Assert.NotNull(b20); Assert.NotNull(b30); Assert.NotNull(bg);
            panel.Land(b15, h2); b15.Label = "Bedroom lights";
            panel.Land(b20, h1); b20.Label = "Bedroom receptacles";
            b30.Label = "Dryer"; bg.Label = "Bath";
            panel.SetMain(true);
            yield return Frames(4);

            Assert.IsNotNull(panelP.transform.Find("Wires"), "landed conductors");
            Assert.IsNotNull(panelP.transform.Find("Connectors"), "cable connectors");
            Assert.IsNull(panelP.transform.Find("DeadFront"), "cover off at rough-in");

            Vector3 pc = panelP.transform.position;
            Shot(Cam(pc + new Vector3(0.08f, 0.05f, -0.85f), pc + new Vector3(0, 0.02f, 0), 55f), "detail_01_panel_open");
            Shot(Cam(pc + new Vector3(-0.12f, 0.12f, -0.32f), pc + new Vector3(0, I(4f), 0), 50f), "detail_02_panel_breakers");
            Shot(Cam(pc + new Vector3(0.05f, -0.2f, -0.3f), pc + new Vector3(0, -I(10f), 0), 55f), "detail_03_panel_bottom_bars");

            // trim-out: dead-front cover with the door open and the directory filled in
            panel.SetCover(true);
            yield return Frames(3);
            Assert.IsNotNull(panelP.transform.Find("DeadFront"), "dead-front built");
            Shot(Cam(pc + new Vector3(0.25f, 0.05f, -0.95f), pc + new Vector3(0.04f, 0.02f, 0), 55f), "detail_04_panel_cover");
            Vector3 door = panelP.transform.TransformPoint(new Vector3(-I(5.0f), I(1f), I(6.0f)));
            Shot(Cam(door + new Vector3(-0.42f, 0.05f, -0.12f), door, 45f), "detail_05_panel_directory");

            // devices and plates
            Shot(Cam(o1.FrontWorld + new Vector3(0.05f, 0.04f, -0.28f), o1.FrontWorld, 45f), "detail_06_receptacle_plate");
            Shot(Cam(o2.FrontWorld + new Vector3(-0.07f, 0.05f, -0.22f), o2.FrontWorld, 45f), "detail_07_gfci_no_plate");
            Shot(Cam(s1.FrontWorld + new Vector3(0.06f, 0.03f, -0.25f), s1.FrontWorld, 45f), "detail_08_switch_plate");
            Shot(Cam(o2.transform.position + new Vector3(0.15f, 0.12f, 0.12f), o2.transform.position, 50f), "detail_09_box_back");
        }
            [UnityTest]
        public IEnumerator DuctSeamsAndJoints()
        {
            yield return Frames(10);
            MepStock.Free = true;
            if (GameClock.I != null) GameClock.I.Hours = 11f;
            Vector3 O = new Vector3(-30f, 0f, 48f);
            NetRun Run(string spec, float y, float z, params string[] tags)
            {
                var pts = new List<Vector3> { O + new Vector3(0, y, z), O + new Vector3(3.66f, y, z) };
                var res = RunBuilder.Route(RunSpecs.Get(spec), pts, null, null);
                Assert.IsTrue(res.Ok, "duct route failed: " + string.Join("; ", res.Warnings));
                foreach (var t in tags) res.Runs[0].Tags.Add(t);
                return res.Runs[0];
            }
            var trunk = Run(HvacSpecs.Trunk8x12, 1.2f, 0f, "conditioned");
            var raw = Run(HvacSpecs.Round6, 1.1f, -0.6f, "conditioned");
            var taped = Run(HvacSpecs.Round6, 1.1f, -1.1f, "conditioned", "sealed");
            var attic = Run(HvacSpecs.Round6, 1.1f, -1.6f, "attic");
            var to = HvacBuild.Takeoff(trunk, O + new Vector3(2.4f, 1.2f, 0f), Vector3.up, 6f);
            Assert.NotNull(to, "takeoff");
            yield return Frames(2);
            DuctDetail.Apply();
            yield return Frames(2);
            foreach (var r in NetGraph.RunsOf(Trade.Hvac))
                if (r != null && r.Spec.Kind == NetKind.Duct) Assert.IsNotNull(r.transform.Find("Detail"), "detail on " + r.Spec.Id);
            Assert.IsTrue(DuctDetail.InAttic(attic));
            Assert.IsFalse(DuctDetail.InAttic(trunk));

            Vector3 j = O + new Vector3(I(60f), 1.2f, 0f);
            Shot(Cam(j + new Vector3(0.25f, 0.35f, -0.55f), j, 50f), "detail_10_duct_rect_joint");
            Vector3 jr = O + new Vector3(I(60f), 1.1f, -0.6f);
            Shot(Cam(jr + new Vector3(0.12f, 0.15f, -0.32f), jr, 50f), "detail_11_duct_round_crimp_screws");
            Vector3 jt = O + new Vector3(I(60f), 1.1f, -1.1f);
            Shot(Cam(jt + new Vector3(0.12f, 0.15f, -0.32f), jt, 50f), "detail_12_duct_round_taped");
            Vector3 tk = to.transform.position;
            Shot(Cam(tk + new Vector3(-0.3f, 0.35f, -0.4f), tk, 50f), "detail_13_takeoff_collar");
            Shot(Cam(O + new Vector3(1.0f, 1.9f, -3.2f), O + new Vector3(1.8f, 1.1f, -0.8f), 55f), "detail_14_duct_overview_attic_wrap");
        }
            [UnityTest]
        public IEnumerator DigGravelPourDust()
        {
            yield return Frames(10);
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            Weather.ForceRain = false;
            Vector3 at = new Vector3(-36f, 0.02f, 60f);
            Vector3 eye = at + new Vector3(-0.6f, 1.63f, -3.0f);

            // scaling: a bucket throws more dust than a shovelful
            SiteFx.Clear(); SiteFx.WindOverride = Vector3.zero;
            SiteFx.Dig(at, 1f); int shovel = SiteFx.DustAlive;
            SiteFx.Clear();
            SiteFx.Dig(at, 1.8f); int bucket = SiteFx.DustAlive;
            Assert.Greater(bucket, shovel, "bucket dust should exceed shovel dust");
            SiteFx.Clear();

            // wind drift: a 2 m/s east wind carries the cloud east
            SiteFx.WindOverride = new Vector3(2f, 0, 0);
            SiteFx.Dig(at, 1f);
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(SiteFx.DustCentroid().x, at.x + 0.6f, "dust should drift downwind");
            SiteFx.Clear();

            // mid-dig: three shovelfuls, shot ~0.3 s after the last one, from standing eye height
            SiteFx.WindOverride = new Vector3(0.8f, 0, 0.3f);
            for (int i = 0; i < 3; i++) { SiteFx.Dig(at + new Vector3(0.05f * i, 0, 0), 1f); yield return new WaitForSeconds(0.35f); }
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(SiteFx.DustAlive, 10);
            Shot(Cam(eye, at + Vector3.up * 0.45f, 60f), "detail_15_dig_dust_shovel");
            yield return new WaitForSeconds(1.2f);
            Shot(Cam(eye, at + Vector3.up * 0.45f, 60f), "detail_16_dig_dust_drift");
            SiteFx.Clear();
            for (int i = 0; i < 2; i++) { SiteFx.Dig(at, 1.8f); yield return new WaitForSeconds(0.4f); }
            yield return new WaitForSeconds(0.25f);
            Shot(Cam(eye + new Vector3(0, 0.6f, -1.5f), at + Vector3.up * 0.5f, 60f), "detail_17_dig_dust_excavator");
            SiteFx.Clear();
            for (int i = 0; i < 4; i++) { SiteFx.Gravel(at + new Vector3(0.1f * i, 0, 0)); yield return new WaitForSeconds(0.12f); }
            yield return new WaitForSeconds(0.35f);
            Shot(Cam(eye, at + Vector3.up * 0.45f, 60f), "detail_18_gravel_dump_dust");
            SiteFx.Clear();
            for (int i = 0; i < 6; i++) { SiteFx.Concrete(at); yield return new WaitForSeconds(0.1f); }
            Shot(Cam(at + new Vector3(-0.4f, 1.2f, -1.6f), at + Vector3.up * 0.2f, 55f), "detail_19_concrete_spatter");
            SiteFx.Clear(); SiteFx.WindOverride = null; Weather.ForceRain = null;
        }
    }
}
