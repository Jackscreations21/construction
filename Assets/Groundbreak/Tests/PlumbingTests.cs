using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Plumbing rough-in: a 2x6 wet wall with tub, toilet and vanity, PEX home runs from manifolds through bored
    /// studs, PVC DWV with stack vents through the roof, then the pressure test, water test and rough inspection.</summary>
    public class PlumbingTests
    {
        static readonly Vector3 O = new Vector3(40f, 0f, 40f);
        const float FL = 0.9f; // finished floor (crawlspace below)

        /// <summary>World point from inches: x along the wall, y above the floor, z out from the wall center (+Z = bathroom side).</summary>
        static Vector3 P(float x, float y, float z) => O + new Vector3(Units.In(x), FL + Units.In(y), Units.In(z));
        static float In(float v) => Units.In(v);

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

        static Camera Overview(Vector3 pos, Vector3 lookAt, float fov = 55f)
        {
            var cam = new GameObject("TestPlumbCam").AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.enabled = false;
            return cam;
        }

        static Piece Make(string id, float lengthIn, Vector3 pos, Quaternion rot)
        {
            var def = Catalog.Get(id);
            Assert.NotNull(def, "missing catalog item " + id);
            var p = Piece.Create(def, lengthIn > 0 ? In(lengthIn) : 0f, pos, rot);
            MepAutoAttach.AttachNow(p);
            return p;
        }

        static NetPort Port(Piece p, string label)
        {
            var n = p.GetComponent<NetNode>();
            Assert.NotNull(n, p.Def.Id + " has no plumbing node");
            var port = n.Port(label);
            Assert.NotNull(port, $"{p.Def.Id} has no port '{label}'");
            return port;
        }

        static RouteResult Route(string spec, NetPort a, NetPort b, float slope, params Vector3[] mid)
        {
            var s = RunSpecs.Get(spec);
            Assert.NotNull(s, "missing run spec " + spec);
            var pts = new List<Vector3>();
            pts.Add(a != null ? a.WorldPos : mid[0]);
            pts.AddRange(mid);
            if (b != null) pts.Add(b.WorldPos);
            var r = RunBuilder.Route(s, pts, a, b, slope);
            Assert.IsTrue(r.Ok, $"route {spec} failed: {string.Join("; ", r.Warnings)}");
            foreach (var w in r.Warnings) Debug.Log("[PlumbTest] route warning: " + w);
            return r;
        }

        static NetRun RunNear(Vector3 p, string spec)
        {
            NetRun best = null; float bd = 0.05f;
            foreach (var r in NetGraph.Runs)
            {
                if (r == null || r.Spec.Id != spec) continue;
                r.DistanceAlong(p, out float off);
                if (off < bd) { bd = off; best = r; }
            }
            Assert.NotNull(best, "no " + spec + " run near " + p);
            return best;
        }

        static readonly float[] StudX = { 0.75f, 16, 32, 48, 64, 80, 96, 112, 128, 143.25f };
        static readonly Quaternion StudRot = Quaternion.Euler(0, 90, 0) * Quaternion.Euler(-90, 0, 0);
        static readonly Quaternion PlateRot = Quaternion.Euler(0, 90, 0);
        readonly Dictionary<float, Piece> _studs = new Dictionary<float, Piece>();
        readonly List<Piece> _plates = new List<Piece>();

        void BoreStuds(float x0, float x1, float h, float z)
        {
            foreach (var sx in StudX)
                if (sx > Mathf.Min(x0, x1) + 0.5f && sx < Mathf.Max(x0, x1) - 0.5f)
                    StudHoles.Bore(_studs[sx], P(sx, h, z), Vector3.right, In(1f), "1\" auger");
        }

        void BorePlates(float x, float z)
        {
            foreach (var pl in _plates)
                StudHoles.Bore(pl, new Vector3(P(x, 0, z).x, pl.transform.position.y, P(x, 0, z).z), Vector3.up, In(2.5625f), "2-9/16\" hole saw");
        }

        static int MakeAllJoints()
        {
            int n = 0;
            foreach (var node in new List<NetNode>(NetGraph.Nodes))
                if (node != null && node.Trade == Trade.Plumbing)
                    foreach (var p in node.Ports) if (p.NeedsJoin) { JoinTool.MakeJoint(p); n++; }
            return n;
        }

        [UnityTest]
        public IEnumerator PlumbBathroom()
        {
            yield return Frames(5);
            MepStock.Free = true;

            // ---------------------------------------------------------------- framing: 2x6 wet wall, subfloor, roof deck
            _studs.Clear(); _plates.Clear();
            _plates.Add(Make("2x6x12", 144f, P(72f, 0.75f, 0f), PlateRot));
            foreach (var sx in StudX) _studs[sx] = Make("2x6x8", 92.625f, P(sx, 1.5f + 46.3125f, 0f), StudRot);
            _plates.Add(Make("2x6x12", 144f, P(72f, 1.5f + 92.625f + 0.75f, 0f), PlateRot));
            _plates.Add(Make("2x6x12", 144f, P(72f, 1.5f + 92.625f + 2.25f, 0f), PlateRot));
            Make("ply34", 0, P(40f, -0.375f, 21.25f), PlateRot);
            Make("ply34", 0, P(136f, -0.375f, 21.25f), PlateRot);
            var roofRot = Quaternion.AngleAxis(18.43f, Vector3.right) * PlateRot;
            Make("ply34", 0, P(64f, 114f, 0f), roofRot);
            Physics.SyncTransforms();

            // ---------------------------------------------------------------- fixtures + parts
            var tub = Make("pl-tub60", 0, P(24f, 8f, 32.75f), Quaternion.Euler(0, -90, 0));
            var toilet = Make("pl-toilet", 0, P(56f, 15f, 16.75f), Quaternion.identity);
            var vanity = Make("pl-vanity36", 0, P(89f, 17f, 13.25f), Quaternion.identity);
            var wh = Make("wh-gas50", 0, P(150f, 30f, 15f), Quaternion.identity);
            var hotMan = Make(PlumbCatalog.Manifold, 0, P(120f, 36f, 4.25f), Quaternion.identity);
            var coldMan = Make(PlumbCatalog.Manifold, 0, P(136f, 36f, 4.25f), Quaternion.identity);
            var valve = Make(PlumbCatalog.Shutoff, 0, P(145f, 12f, 4.25f), Quaternion.identity);
            var gauge = Make(PlumbCatalog.Gauge, 0, P(166f, 20f, 4.25f), Quaternion.identity);
            var ptrap = Make(PlumbCatalog.PTrap, 0, P(24.5f, -9.6f, 11.75f), Quaternion.identity);
            var cleanout = Make(PlumbCatalog.Cleanout, 0, P(-9f, -32f, -0.75f), Quaternion.Euler(0, -90, 0));
            Piece Boot(float x)
            {
                Assert.IsTrue(Physics.Raycast(P(x, 160f, -0.75f), Vector3.down, out var hit, 3f), "roof deck not found above x=" + x);
                Physics.SyncTransforms();
                return Make(PlumbCatalog.VentBoot, 0, hit.point, Quaternion.identity);
            }
            var bootTub = Boot(40f);
            var bootMain = Boot(89f);
            Physics.SyncTransforms();

            // real rough-in locations relative to the framing
            Assert.AreEqual(12f, Units.ToInches(Port(toilet, "flange").WorldPos.z - P(0, 0, 2.75f).z), 0.05f, "toilet flange must be 12\" from the wall");
            Assert.AreEqual(0f, Units.ToInches(Port(toilet, "flange").WorldPos.y - P(0, 0, 0).y), 0.05f, "closet flange at the floor");
            Assert.Greater(Port(vanity, "hot").WorldPos.x, Port(vanity, "cold").WorldPos.x, "vanity hot stub must be on the left facing the sink (+X)");
            Assert.AreEqual(18f, Units.ToInches(Port(vanity, "trap arm").WorldPos.y - FL - O.y), 0.05f, "lav trap arm at 18\" AFF");

            // ---------------------------------------------------------------- bore studs (1" auger for 1/2" PEX) and plates (2-9/16" hole saw for 2" stacks)
            float zPex = 0.875f, zStack = -0.75f;
            BoreStuds(133f, 50f, 4f, zPex);    // toilet cold
            BoreStuds(135f, 85f, 6.5f, zPex);  // vanity cold
            BoreStuds(137f, 21f, 24f, zPex);   // tub cold (runs above the lav stubs so no rise crosses it)
            BoreStuds(117f, 93f, 11.5f, zPex); // vanity hot
            BoreStuds(119f, 29.5f, 26.5f, zPex); // tub hot
            BorePlates(40f, zStack);
            BorePlates(89f, zStack);

            // ---------------------------------------------------------------- water supply: gauge -> main shutoff -> cold manifold + water heater -> hot manifold
            const string B34 = PlumbCatalog.Pex34Blue, R34 = PlumbCatalog.Pex34Red, B12 = PlumbCatalog.Pex12Blue, R12 = PlumbCatalog.Pex12Red;
            Route(B34, Port(gauge, "stub"), Port(valve, "in"), 0f, P(166f, -10f, 4.25f), P(145f, -10f, 4.25f));
            var main = Route(B34, Port(valve, "out"), Port(coldMan, "in"), 0f, P(145f, 36.5f, 4.25f)).Runs[0];
            var teeWh = RunBuilder.TeeInto(main, P(145f, 26f, 4.25f), Vector3.right, "tee");
            Assert.NotNull(teeWh, "tee into the main failed");
            Route(B34, teeWh, Port(wh, "cold in"), 0f, P(148f, 26f, 4.25f), P(148f, 66f, 4.25f), P(148f, 66f, 15f), P(145.5f, 66f, 15f));
            Route(R34, Port(wh, "hot out"), Port(hotMan, "in"), 0f, P(154.5f, 70f, 15f), P(154.5f, 70f, 4.25f), P(128f, 70f, 4.25f), P(128f, 36.5f, 4.25f));
            // home runs through the bored studs
            Route(B12, Port(coldMan, "out 1"), Port(toilet, "cold"), 0f, P(133f, 7.4f, 4.25f), P(129.6f, 4f, zPex), P(50f, 4f, zPex), P(50f, 7f, zPex));
            Route(B12, Port(coldMan, "out 2"), Port(vanity, "cold"), 0f, P(135f, 9.9f, 4.25f), P(131.6f, 6.5f, zPex), P(85f, 6.5f, zPex), P(85f, 21f, zPex));
            Route(B12, Port(coldMan, "out 3"), Port(tub, "cold"), 0f, P(137f, 27.4f, 4.25f), P(133.6f, 24f, zPex), P(21f, 24f, zPex), P(21f, 28f, zPex));
            Route(R12, Port(hotMan, "out 1"), Port(vanity, "hot"), 0f, P(117f, 14.9f, 4.25f), P(113.6f, 11.5f, zPex), P(93f, 11.5f, zPex), P(93f, 21f, zPex));
            Route(R12, Port(hotMan, "out 2"), Port(tub, "hot"), 0f, P(119f, 29.9f, 4.25f), P(115.6f, 26.5f, zPex), P(29.5f, 26.5f, zPex), P(29.5f, 28f, zPex));

            // ---------------------------------------------------------------- DWV
            const string S3 = PlumbCatalog.Pvc3, S2 = PlumbCatalog.Pvc2, S2V = PlumbCatalog.Pvc2Vent, S15 = PlumbCatalog.Pvc15;
            var s2 = RunSpecs.Get(S2); var s15 = RunSpecs.Get(S15);
            float BdY(float x) => -32f - (x + 6f) / 12f * 0.25f;
            // building drain: cleanout at the upper end, 1/4"/ft toward the sewer at +X
            Route(S3, Port(cleanout, "drain"), null, 0.25f, P(172f, -32f, zStack));
            Assert.AreEqual(BdY(172f), Units.ToInches(RunNear(P(100f, BdY(100f), zStack), S3).EndPoint.y - FL - O.y), 0.05f, "building drain slope");

            // stack bases: 3x3x2 wye + 1/8 bend up into the wall
            NetPort StackBase(float wyeX, float stackX, out Vector3 bend)
            {
                var c = P(wyeX, BdY(wyeX), zStack);
                var port = PlumbFit.ReducingTee(RunNear(c, S3), c, Vector3.up, "wye", s2);
                Assert.NotNull(port, "wye at " + wyeX);
                Assert.AreEqual(2f, port.NominalIn, 0.01f);
                Vector3 d = port.WorldDir;
                float t = (port.WorldPos.x - P(stackX, 0, 0).x) / -d.x;
                bend = port.WorldPos + d * t;
                return port;
            }
            // tub: shoe -> tailpiece -> P-trap -> trap arm -> 2x2x1-1/2 santee on its own stack vent
            Route(S15, Port(tub, "drain"), Port(ptrap, "inlet"), 0f);
            var trapOut = Port(ptrap, "outlet");
            float armLen = Units.ToInches(trapOut.WorldPos.z - P(0, 0, zStack).z) + (40f - 24.5f - 2.97f);
            float yTubSantee = Units.ToInches(trapOut.WorldPos.y - FL - O.y) - armLen / 12f * 0.25f - 0.15f;
            var tubSantee = PlumbFit.Create("santee", s2, P(40f, yTubSantee, zStack), new[] { Vector3.down, Vector3.up, Vector3.left }, s15);
            Route(S15, trapOut, tubSantee.Ports[2], 0.25f, P(24.5f, 0, zStack) + Vector3.up * (trapOut.WorldPos.y - FL - O.y));
            var tubWye = StackBase(51f, 40f, out var tubBend);
            Route(S2, tubWye, tubSantee.Ports[0], 0f, tubBend);
            Route(S2V, tubSantee.Ports[1], Port(bootTub, "vent"), 0f);
            // vanity: trap arm into a santee on the main stack
            var mainSantee = PlumbFit.Create("santee", s2, P(89f, 17.88f, zStack), new[] { Vector3.down, Vector3.up, Vector3.forward }, s15);
            Route(S15, Port(vanity, "trap arm"), mainSantee.Ports[2], 0f);
            var mainWye = StackBase(100f, 89f, out var mainBend);
            Route(S2, mainWye, mainSantee.Ports[0], 0f, mainBend);
            Route(S2V, mainSantee.Ports[1], Port(bootMain, "vent"), 0f);
            // toilet: 3" wye, 1/8 bend, closet bend up to the flange (graded up toward the toilet)
            var tw = P(83f, BdY(83f), zStack);
            var toiletWye = RunBuilder.TeeInto(RunNear(tw, S3), tw, Vector3.forward, "wye");
            Assert.NotNull(toiletWye, "toilet wye");
            var tp0 = toiletWye.WorldPos; var td = toiletWye.WorldDir; td.y = 0; td.Normalize();
            var tp1 = tp0 + td * ((P(0, 0, 14.75f).z - tp0.z) / td.z);
            Route(S3, toiletWye, Port(toilet, "flange"), -0.25f, tp1, new Vector3(P(56f, 0, 0).x, tp1.y, tp1.z));
            // test ball / cap at the sewer end
            NetRun outlet = null;
            foreach (var r in NetGraph.Runs) if (r != null && r.Spec.Id == S3 && (outlet == null || r.EndPoint.x > outlet.EndPoint.x)) outlet = r;
            RunBuilder.CapEnd(outlet, false);

            yield return Frames(2);
            Physics.SyncTransforms();

            // nothing glued/crimped yet -> both tests fail
            var g = gauge.GetComponent<PressureGauge>();
            Assert.NotNull(g, "gauge component");
            Assert.IsTrue(PressureTest.Pump(g), "should pump up with no open ends: " + g.Status);
            PressureTest.Advance(g, 5f);
            Assert.AreEqual(PressureGauge.TestState.Failed, g.State, "uncrimped system must lose pressure");
            Assert.IsFalse(DrainTest.Fill(), "unglued DWV must fail the water test");

            // ---------------------------------------------------------------- make every joint (crimp rings, primer + cement, thread the gauge)
            int joints = MakeAllJoints();
            Debug.Log("[PlumbTest] joints made: " + joints);
            Assert.Greater(joints, 40);
            yield return Frames(3);

            // ---------------------------------------------------------------- pressure test
            Assert.IsTrue(PressureTest.Pump(g), "pump: " + g.Status);
            Assert.AreEqual(100f, g.Psi, 0.01f);
            PressureTest.Advance(g, 8f);
            Assert.AreEqual(PressureGauge.TestState.Holding, g.State, g.Status);
            PressureTest.Advance(g, 8f);
            Debug.Log("[PlumbTest] pressure: " + g.Status);
            Assert.AreEqual(PressureGauge.TestState.Passed, g.State, g.Status + " leaks: " + string.Join("; ", g.Leaks));
            Assert.AreEqual(100f, g.Psi, 0.01f);

            // ---------------------------------------------------------------- water test
            bool filled = DrainTest.Fill();
            Assert.IsTrue(filled, "water test: " + DrainTest.Status + " | " + string.Join("; ", DrainTest.Issues));
            DrainTest.Advance(16f);
            Debug.Log("[PlumbTest] water test: " + DrainTest.Status + ", head " + DrainTest.HeadFt.ToString("0.0") + " ft");
            Assert.AreEqual(DrainTest.TestState.Passed, DrainTest.State, DrainTest.Status);
            Assert.Greater(DrainTest.HeadFt, 9f, "water column should reach the roof vent");
            foreach (var leg in PlumbSlope.Legs()) if (leg.Horizontal) Assert.IsTrue(leg.Ok, $"{leg.Run.Spec.Name} slope {leg.Slope:0.###} in/ft");

            // ---------------------------------------------------------------- rough inspection passes
            var findings = MepInspection.Run(Trade.Plumbing);
            foreach (var f in findings) Debug.Log("[PlumbTest] finding: " + f.Rule + " - " + f.Note);
            Assert.AreEqual(0, findings.Count, "clean rough-in should pass: " + string.Join(" | ", findings));
            Assert.IsTrue(MepInspection.LastPassed[Trade.Plumbing]);

            // ---------------------------------------------------------------- screenshots
            Shot(Overview(P(72f, 62f, -120f), P(76f, 18f, 0f)), "mep_plumbing_01_wetwall_back");
            Shot(Overview(P(60f, 70f, 150f), P(80f, 20f, 0f)), "mep_plumbing_02_bathroom_front");
            Shot(Overview(P(131f, 30f, 24f), P(129f, 31f, 3f), 50f), "mep_plumbing_03_manifolds_crimp");
            Shot(Overview(P(42f, 20f, 16f), P(50f, 7f, 4f), 50f), "mep_plumbing_03b_toilet_stub_crimp");
            Shot(Overview(P(104f, 14f, -22f), P(100f, 9f, 0f), 50f), "mep_plumbing_04_bored_studs");
            Shot(Overview(P(100f, -18f, -26f), P(93f, -28f, 0f), 50f), "mep_plumbing_05_stack_wye_primer");
            Shot(Overview(P(78f, -14f, 40f), P(70f, -28f, 6f), 55f), "mep_plumbing_06_toilet_drain");
            Shot(Overview(P(14f, 4f, -24f), P(30f, -2f, 4f), 55f), "mep_plumbing_07_tub_trap_valve");
            Shot(Overview(P(150f, 80f, 50f), P(150f, 55f, 12f), 55f), "mep_plumbing_08_water_heater");
            Shot(Overview(P(64f, 150f, 60f), P(64f, 116f, 0f), 55f), "mep_plumbing_09_roof_vents");
            Shot(Overview(P(166f, 26f, 22f), P(166f, 20f, 4f), 45f), "mep_plumbing_10_gauge");

            // ---------------------------------------------------------------- break one crimp: the gauge must drop and the inspector must find it
            var bad = Port(vanity, "hot");
            bad.Joined = false; bad.Node.RefreshJoints(); NetGraph.Touch();
            Assert.IsTrue(PressureTest.Pump(g), "a weeping joint still pumps up");
            PressureTest.Advance(g, 15f);
            Debug.Log("[PlumbTest] broken joint: " + g.Status + ", psi " + g.Psi);
            Assert.AreEqual(PressureGauge.TestState.Failed, g.State, "pressure test must fail with an uncrimped ring");
            Assert.Less(g.Psi, 98f, "gauge must drop");
            Assert.IsTrue(g.Leaks.Exists(l => l.Port == bad), "leak should be located at the uncrimped ring");
            findings = MepInspection.Run(Trade.Plumbing);
            Assert.IsTrue(findings.Exists(f => f.Rule == "pl-joints" && Vector3.Distance(f.Pos, bad.WorldPos) < 0.01f), "inspection must flag the unmade crimp");
            Assert.IsTrue(findings.Exists(f => f.Rule == "pl-pressure"), "inspection must fail the pressure test");
            Assert.Greater(MepInspection.MarkerCount, 0, "red marker shown");
            JoinTool.MakeJoint(bad);

            // an open stub (manifold valve left without cap) never builds pressure
            var spare = Port(coldMan, "out 4");
            spare.Capped = false;
            Assert.IsFalse(PressureTest.Pump(g), "open stub must not hold pressure");
            Assert.IsTrue(g.Leaks.Exists(l => l.Port == spare), "leak at the open stub");
            spare.Capped = true;

            // an unglued DWV joint fails the water test
            var dwvBad = tubSantee.Ports[2];
            dwvBad.Joined = false;
            Assert.IsFalse(DrainTest.Fill(), "unglued trap arm must fail the water test");
            Assert.IsTrue(DrainTest.Issues.Exists(l => l.Port == dwvBad), "water test locates the dry joint");
            JoinTool.MakeJoint(dwvBad);

            // over-bored stud (3-5/8" hole saw in a 2x6 = 66%)
            StudHoles.Bore(_studs[64f], P(64f, 40f, 0f), Vector3.right, In(3.625f), "3-5/8\" hole saw");
            findings = MepInspection.Run(Trade.Plumbing);
            Assert.IsTrue(findings.Exists(f => f.Rule == "overbored"), "over-bored stud must be flagged");
            Shot(Overview(P(74f, 44f, -9f), P(64.5f, 40f, 0f), 60f), "mep_plumbing_11_overbored_marker");
            // the other hookups attach with ports at their rough-in spots, and unconnected stubs get flagged
            int k = 0;
            foreach (var id in new[] { "pl-kitchen-sink", "pl-shower48", "wh-tankless", "wh-elec50", PlumbCatalog.HoseBib, PlumbCatalog.ClosetFlange })
            {
                var extra = Make(id, 0, O + new Vector3(-3f - 2f * k++, 1.2f, -3f), Quaternion.identity);
                var node = extra.GetComponent<NetNode>();
                Assert.NotNull(node, id + " has no node");
                Assert.Greater(node.Ports.Count, 0, id + " has no ports");
            }
            findings = MepInspection.Run(Trade.Plumbing, false);
            Assert.IsTrue(findings.Exists(f => f.Rule == "pl-open" && f.Note.Contains("not connected")), "unconnected fixture stubs flagged");
            Debug.Log("[PlumbTest] done");
        }
    }
}
