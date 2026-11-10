using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Electrical rough-in: stud wall, meter + panel, boxes at code heights, bored studs, 12/2 receptacle circuit and
    /// 14/2 switched lighting circuit, staples, box make-up, devices, breakers. Then simulates power, switching, overload trip,
    /// open ground and working live.</summary>
    public class ElectricalTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static float I(float x) => Units.In(x);

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
            var cam = new GameObject("TestElCam").AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
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
            var s = RunSpecs.Get(spec);
            Assert.NotNull(s, "missing spec " + spec);
            var res = RunBuilder.Route(s, pts, a, b);
            foreach (var w in res.Warnings) Debug.Log("[El] route warning: " + w);
            Assert.IsTrue(res.Ok, "route failed: " + string.Join("; ", res.Warnings));
            return res.Runs[0];
        }

        [UnityTest]
        public IEnumerator WireRoom()
        {
            yield return Frames(10);
            MepStock.Free = true;
            if (GameClock.I != null) GameClock.I.Hours = 11f;

            Vector3 O = new Vector3(20f, 0f, 10f);
            float Wz = O.z;
            Vector3 X(float xIn, float yIn, float zOffIn = 0f) => new Vector3(O.x + I(xIn), I(yIn), Wz + I(zOffIn));

            // ---------------- framing: 8' wall of 2x4 studs 16" O.C., double top plate, two ceiling joists on posts
            var studRot = Quaternion.Euler(-90f, 90f, 0f);
            var plateRot = Quaternion.Euler(0f, 90f, 0f);
            float studLen = I(92.625f);
            var studs = new List<Piece>();
            var studX = new List<float> { 0.75f, 16.75f, 32.75f, 48.75f, 64.75f, 80.75f, 95.25f };
            foreach (var x in studX) studs.Add(P("2x4x925", X(x, 1.5f + 92.625f / 2f), studRot));
            var sole = P("2x4x8", X(48f, 0.75f), plateRot);
            var top1 = P("2x4x8", X(48f, 94.875f), plateRot);
            var top2 = P("2x4x8", X(48f, 96.375f), plateRot);
            var joistRot = Quaternion.Euler(0f, 0f, 90f);
            foreach (var jx in new[] { 32.75f, 48.75f })
            {
                P("2x6x8", X(jx, 97.125f + 2.75f, 1.75f - 48f), joistRot);
            }
            Assert.AreEqual(16f, Units.ToInches(studs[1].transform.position.x - studs[0].transform.position.x), 0.01f, "studs 16\" O.C.");

            // ---------------- service: meter base outside, 200 A panel flush in the first bay (front 1/2" proud)
            var yaw180 = Quaternion.Euler(0f, 180f, 0f);
            var meterP = P("el-meter200", X(8.75f, 80f, 1.75f + 2f), Quaternion.identity);
            var panelP = P("el-panel200", X(8.75f, 54f, 1.75f - 2f), yaw180);
            var meter = meterP.GetComponent<ElMeter>();
            var panel = panelP.GetComponent<ElPanel>();
            Assert.NotNull(meter, "meter not attached"); Assert.NotNull(panel, "panel not attached");

            // ---------------- boxes on the -X side of studs, front 1/2" proud of the stud face
            float boxZ = -1.75f - 0.5f + ElBox.D1g / 2f;
            float boxX(float stud) => stud - 0.75f - ElBox.W1g / 2f;
            ElBox Box(string id, float x, float y, float z, Quaternion r) { var p = P(id, X(x, y, z), r); var b = p.GetComponent<ElBox>(); Assert.NotNull(b, "box attach " + id); return b; }
            var s1 = Box("el-box-1g", boxX(32.75f), 48f, boxZ, yaw180);
            var o1 = Box("el-box-1g", boxX(48.75f), 14f, boxZ, yaw180);
            var o2 = Box("el-box-1g", boxX(64.75f), 14f, boxZ, yaw180);
            var o3 = Box("el-box-1g", boxX(80.75f), 14f, boxZ, yaw180);
            float ceilX = 32.75f - 0.75f - ElBox.DRound / 2f - 0.12f;
            var cb = Box("el-box-round", ceilX, 97.125f - 0.5f + ElBox.HRound / 2f, -36f, yaw180);
            var lightP = P("el-ceiling-light", X(ceilX, 97.125f - 0.5f - 2.5f, -36f), Quaternion.identity);
            var load = lightP.GetComponent<ElLoad>();
            Assert.NotNull(load, "light fixture not attached");
            Assert.NotNull(load.Lamp, "light fixture has no Light");
            yield return Frames(2);
            Assert.AreEqual(cb, load.FindBox(), "fixture should mount on the ceiling box");
            Assert.AreEqual(48f, Units.ToInches(s1.HeightAboveFloor()), 0.6f, "switch at 48\"");
            Assert.AreEqual(14f, Units.ToInches(o1.HeightAboveFloor()), 0.6f, "receptacle at 14\"");

            NetPort Port(ElBox b, string l) => b.Node.Port(l);
            Vector3 Wp(NetPort p) => p.WorldPos;

            // ---------------- bore 3/4" holes (center of the stud) before pulling
            float hole = I(0.75f);
            StudHoles.Bore(studs[1], X(16.75f, 24f), Vector3.right, hole, "3/4\" auger");
            StudHoles.Bore(studs[2], X(32.75f, 24f), Vector3.right, hole, "3/4\" auger");
            StudHoles.Bore(studs[3], X(48.75f, 26f), Vector3.right, hole, "3/4\" auger");
            StudHoles.Bore(studs[4], X(64.75f, 26f), Vector3.right, hole, "3/4\" auger");
            StudHoles.Bore(studs[1], X(16.75f, 74f), Vector3.right, hole, "3/4\" auger");
            var legPort = Port(s1, "top-a");
            float legX = Units.ToInches(legPort.WorldPos.x - O.x);
            StudHoles.Bore(top1, X(legX, 94.875f), Vector3.up, hole, "3/4\" auger");
            StudHoles.Bore(top2, X(legX, 96.375f), Vector3.up, hole, "3/4\" auger");

            // ---------------- service entrance cable: meter -> panel
            var mp = meter.Node.Port("load"); var sp = panel.ServicePort;
            Pull(ElCatalog.SerId, new List<Vector3> { Wp(mp), Wp(mp) + Vector3.down * I(2.5f), new Vector3(mp.WorldPos.x, mp.WorldPos.y - I(2.5f), Wz), new Vector3(sp.WorldPos.x, mp.WorldPos.y - I(2.5f), Wz), Wp(sp) }, mp, sp);

            // ---------------- 20 A receptacle circuit, 12/2: panel bottom -> O1 -> O2 -> O3
            var pb = panel.Node.Port("branch-bot");
            Vector3 o1b = Wp(Port(o1, "top-b"));
            var h1 = Pull("nm-12-2", new List<Vector3> { Wp(pb), new Vector3(pb.WorldPos.x, I(24f), Wz), new Vector3(o1b.x, I(24f), Wz), o1b }, pb, Port(o1, "top-b"));
            Vector3 o1a = Wp(Port(o1, "top-a")), o2b = Wp(Port(o2, "top-b"));
            var r12 = Pull("nm-12-2", new List<Vector3> { o1a, new Vector3(o1a.x, I(26f), Wz), new Vector3(o2b.x, I(26f), Wz), o2b }, Port(o1, "top-a"), Port(o2, "top-b"));
            Vector3 o2a = Wp(Port(o2, "top-a")), o3b = Wp(Port(o3, "top-b"));
            var r23 = Pull("nm-12-2", new List<Vector3> { o2a, new Vector3(o2a.x, I(26f), Wz), new Vector3(o3b.x, I(26f), Wz), o3b }, Port(o2, "top-a"), Port(o3, "top-b"));

            // ---------------- 15 A lighting circuit, 14/2: panel top -> switch box -> ceiling box
            var pt = panel.Node.Port("branch-top");
            Vector3 s1b = Wp(Port(s1, "top-b"));
            var h2 = Pull("nm-14-2", new List<Vector3> { Wp(pt), new Vector3(pt.WorldPos.x, I(74f), Wz), new Vector3(s1b.x, I(74f), Wz), s1b }, pt, Port(s1, "top-b"));
            var cport = cb.Node.Port("ko-w");
            Vector3 cp = Wp(cport), lp = Wp(legPort);
            var leg = Pull("nm-14-2", new List<Vector3> { lp, new Vector3(lp.x, I(100f), Wz), new Vector3(lp.x, I(100f), cp.z), new Vector3(cp.x, I(100f), cp.z), cp }, legPort, cport);

            // ---------------- staples within 8" of every box (5" up the riser), none needed on the bored horizontal legs
            void StapleNear(NetRun r, ElBox b)
            {
                bool atA = r.NodeA == b.Node;
                float s = atA ? I(5f) : r.Length - I(5f);
                Assert.IsTrue(ElTools.Staple(r, r.PointAt(s, out _)), "staple failed");
            }
            StapleNear(h1, o1); StapleNear(r12, o1); StapleNear(r12, o2); StapleNear(r23, o2); StapleNear(r23, o3);
            StapleNear(h2, s1); StapleNear(leg, s1); StapleNear(leg, cb);
            // the riser out of the panel top: one staple 12" up the stud
            h2.AddStaple(h2.PointAt(I(5f), out _));
            h1.AddStaple(h1.PointAt(I(10f), out _));

            // ---------------- make up boxes + devices
            foreach (var b in new[] { o1, o2, o3, s1, cb })
            {
                StringAssert.StartsWith("Stripped", ElTools.Strip(b));
                for (int k = 0; k < 3; k++) ElTools.Splice(b);
                Assert.IsTrue(b.MadeUp, "box not made up");
            }
            yield return Frames(2);
            Shot(Cam(o2.FrontWorld + new Vector3(I(-3f), I(4f), I(-11f)), o2.FrontWorld + new Vector3(0, 0, I(1.2f)), 45f), "mep_electrical_02_box_madeup");
            foreach (var b in new[] { o1, o2, o3 }) StringAssert.StartsWith("Installed", ElTools.Install(b, ElDevice.Receptacle20));
            StringAssert.StartsWith("Installed", ElTools.Install(s1, ElDevice.Switch));
            Assert.AreEqual(0, ElSim.ShockCount, "no shock while the meter isn't set");

            // ---------------- panel: breakers, land home runs, label
            var b15 = panel.Install("el-bkr-15-1p-afci", 1);
            var b20 = panel.Install("el-bkr-20-1p-df", 2);
            Assert.NotNull(b15); Assert.NotNull(b20);
            panel.Land(b15, h2); b15.Label = "Bedroom lights";
            panel.Land(b20, h1); b20.Label = "Bedroom receptacles";
            Assert.AreEqual(0, panel.Unlanded().Count, "all home runs landed");

            meter.SetUtility(true);
            panel.SetMain(false);
            yield return Frames(5);

            // ---------------- screenshots of the rough-in (daylight)
            var wide = Cam(X(48f, 56f, -105f), X(48f, 50f, 0f), 60f);
            Shot(wide, "mep_electrical_01_wired_wall");
            Shot(Cam(o2.FrontWorld + new Vector3(I(-4f), I(5f), I(-13f)), o2.FrontWorld + new Vector3(0, 0, I(1.5f)), 45f), "mep_electrical_02b_box_device");
            Shot(Cam(panel.transform.position + new Vector3(I(2f), I(6f), I(-22f)), panel.transform.position + new Vector3(0, I(5f), 0), 50f), "mep_electrical_03_panel");
            Shot(Cam(X(12f, 62f, -58f), X(28f, 66f, -8f), 60f), "mep_electrical_04_switch_and_leg");

            // ---------------- inspection: clean rough-in passes
            var findings = MepInspection.Run(Trade.Electrical, false);
            foreach (var f in findings) Debug.Log("[El] finding: " + f);
            Assert.AreEqual(0, findings.Count, "clean rough-in should pass: " + string.Join(" | ", findings));

            // ---------------- power: main OFF -> dark
            Assert.IsTrue(panel.ServiceLive, "service should be live once the meter is set");
            yield return Frames(3);
            Assert.IsFalse(load.Lamp.enabled, "light must be dark with the main off");
            Assert.IsFalse(ElSim.BoxLive(o1), "boxes dead with main off");

            // main ON + switch on -> light on
            panel.SetMain(true);
            s1.SwitchOn = true; s1.Changed();
            yield return Frames(3);
            Assert.IsTrue(b15.Live && b20.Live, "breakers live");
            Assert.IsTrue(load.Lamp.enabled, "light should be on: " + load.Why);
            Assert.IsTrue(o3.DevicePowered, "last receptacle powered");
            StringAssert.StartsWith("H-N 120 V · H-G 120 V", ElSim.Measure(o3));

            // switch off -> dark
            s1.SwitchOn = false; s1.Changed();
            yield return Frames(3);
            Assert.IsFalse(load.Lamp.enabled, "light should go dark with the switch off");
            s1.SwitchOn = true; s1.Changed();
            yield return Frames(3);
            Assert.IsTrue(load.Lamp.enabled);

            // lit room at night
            if (GameClock.I != null) GameClock.I.Hours = 21.5f;
            yield return Frames(3);
            Shot(Cam(X(64f, 60f, -96f), X(34f, 66f, -20f), 60f), "mep_electrical_05_room_lit");
            if (GameClock.I != null) GameClock.I.Hours = 11f;

            // ---------------- overload: two 1500 W heaters on the 20 A circuit -> trip
            o1.PlugW = 1500f; o3.PlugW = 1500f;
            float until = Time.time + 8f;
            while (!b20.Tripped && Time.time < until) yield return null;
            Assert.IsTrue(b20.Tripped, $"20 A breaker should trip at {b20.LoadA:0.0} A");
            yield return Frames(2);
            Assert.IsFalse(o1.DevicePowered || o2.DevicePowered || o3.DevicePowered, "receptacles lose power when the breaker trips");
            Assert.IsTrue(load.Lamp.enabled, "the lighting circuit stays on");
            o1.PlugW = 0f; o3.PlugW = 0f;
            b20.Reset();
            yield return Frames(3);
            Assert.IsTrue(o2.DevicePowered, "power back after reset");

            // ---------------- open ground
            o1.RemoveGroundSplice();
            yield return Frames(3);
            Assert.IsTrue(o2.DevicePowered, "open ground still powers the device");
            StringAssert.Contains("OPEN GROUND", ElSim.Measure(o2));
            var f2 = MepInspection.Run(Trade.Electrical);
            Assert.IsTrue(f2.Exists(f => f.Note.ToUpper().Contains("OPEN GROUND")), "inspection should report the open ground");
            Assert.Greater(MepInspection.MarkerCount, 0, "red markers shown");
            Shot(Cam(o1.FrontWorld + new Vector3(I(-6f), I(8f), I(-20f)), o1.FrontWorld, 50f), "mep_electrical_06_open_ground_marker");

            // ---------------- working a live box -> shock
            int shocks = ElSim.ShockCount;
            float hours = GameClock.I != null ? GameClock.I.Hours : 0f;
            string msg = ElTools.Splice(o1);
            Assert.AreEqual("Shocked!", msg);
            Assert.AreEqual(shocks + 1, ElSim.ShockCount, "shock event");
            if (GameClock.I != null) Assert.AreEqual(0.25f, GameClock.I.Hours - hours, 0.05f, "lost 15 game minutes");
            Assert.IsFalse(o1.Grounds, "nothing was done to the live box");

            // turn the breaker off -> safe to work
            b20.On = false;
            yield return Frames(2);
            ElTools.Splice(o1);
            Assert.IsTrue(o1.Grounds, "ground re-spliced with the breaker off");
            Assert.AreEqual(shocks + 1, ElSim.ShockCount);

            // gauge violation: 14 AWG on 20 A
            Assert.IsTrue(panel.Land(b15, h2).Length > 0);
            var b20b = panel.Install("el-bkr-20-1p", 3);
            panel.Land(b20b, h2);
            yield return Frames(2);
            var f3 = MepInspection.Run(Trade.Electrical, false);
            Assert.IsTrue(f3.Exists(f => f.Rule == "el-gauge"), "14 AWG on 20 A flagged");
            MepInspection.ClearMarkers();
        }
    }
}
