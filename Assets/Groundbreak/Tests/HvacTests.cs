using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>HVAC trade: a 12'x12' room with ceiling joists, an upflow furnace + cased coil + plenum in the closet,
    /// 8x12 trunk in the attic with two 6" takeoffs to ceiling boots + registers, a ceiling return, line set and
    /// condensate to a condenser on a pad, a bath fan to a wall cap. Airflow + rough mechanical inspection.</summary>
    public class HvacTests
    {
        static readonly Vector3 O = new Vector3(-20f, 0f, 12f);
        const float C = 97.125f; // ceiling plane (bottom of the ceiling joists), inches
        static Vector3 W(float x, float y, float z) => O + new Vector3(x, y, z) * Units.Inch;
        static float Y(float inches) => O.y + inches * Units.Inch;

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
            var cam = new GameObject("TestHvacCam").AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.03f;
            cam.enabled = false;
            return cam;
        }

        static Piece Lumber(string id, float lenIn, Vector3 c, Quaternion r) => Piece.Create(Catalog.Get(id), lenIn * Units.Inch, c, r);

        static void BuildFraming()
        {
            var flat = Quaternion.identity;                       // plate along Z, 3.5" across X
            var flatX = Quaternion.Euler(0, 90, 0);               // plate along X, 3.5" across Z
            var studZ = Quaternion.Euler(-90, 0, 0);              // stud: depth along X, 1.5" along Z
            var studX = Quaternion.Euler(-90, 90, 0);             // stud: depth along Z, 1.5" along X
            float studLen = 92.625f, sy = 1.5f + studLen / 2;
            // west wall (between the room and the mechanical closet), x = -3.5..0, z = 0..147.5
            foreach (var (len, zc) in new[] { (96f, 48f), (51.5f, 121.75f) })
            {
                Lumber("2x4x8", len, W(-1.75f, 0.75f, zc), flat);
                Lumber("2x4x8", len, W(-1.75f, 94.875f, zc), flat);
                Lumber("2x4x8", len, W(-1.75f, 96.375f, zc), flat);
            }
            var zs = new List<float> { 0.75f };
            for (float z = 16; z < 146; z += 16) zs.Add(z);
            zs.Add(146.75f);
            foreach (var z in zs) Lumber("2x4x925", studLen, W(-1.75f, sy, z), studZ);
            // north (exterior) wall, z = 144..147.5
            foreach (var (len, xc) in new[] { (96f, 48f), (48f, 120f) })
            {
                Lumber("2x4x8", len, W(xc, 0.75f, 145.75f), flatX);
                Lumber("2x4x8", len, W(xc, 94.875f, 145.75f), flatX);
                Lumber("2x4x8", len, W(xc, 96.375f, 145.75f), flatX);
            }
            var xs = new List<float> { 0.75f };
            for (float x = 16; x < 143; x += 16) xs.Add(x);
            xs.Add(143.25f);
            foreach (var x in xs) Lumber("2x4x925", studLen, W(x, sy, 145.75f), studX);
            // 2x6 ceiling joists 16" o.c. spanning east-west, + rim joist on the north wall
            var joist = Quaternion.LookRotation(Vector3.right, Vector3.forward);
            for (float z = 0.75f; z < 140; z += 16) Lumber("2x6x12", 144f, W(70.25f, C + 2.75f, z <= 1 ? 0.75f : z - 0.75f), joist);
            Lumber("2x6x12", 144f, W(70.25f, C + 2.75f, 146.75f), joist);
        }

        class Site
        {
            public Piece Furnace, Coil, Plenum, ReturnDrop, Boot1, Boot2, Reg1, Reg2, Grille, Condenser, Pad, Fan, Cap, Tstat;
            public NetNode Takeoff1, Takeoff2;
            public RoomZone Room;
        }

        static Site Build()
        {
            var s = new Site();
            BuildFraming();
            s.Room = RoomZone.Create("Primary Bath", new Bounds(W(72, C / 2, 72), new Vector3(144, C, 144) * Units.Inch));
            var env = new Bounds(); env.SetMinMax(W(-54, 0, 0), W(147.5f, 200, 147.5f));
            HvacEnvelope.Create(env);

            // --- equipment in the closet: upflow furnace, cased coil, supply plenum, return drop
            s.Furnace = HvacBuild.Place("hvac-furnace80", W(-24, 20, 75.75f), Quaternion.identity);
            s.Coil = HvacBuild.Place(HvacSpecs.Coil, W(-24, 60, 72), Quaternion.identity);
            HvacBuild.MountTo(s.Coil, "bottom", HvacBuild.Port(s.Furnace, "supply"));
            s.Plenum = HvacBuild.Place(HvacSpecs.SupplyPlenum, W(-24, 90, 72), Quaternion.identity);
            HvacBuild.MountTo(s.Plenum, "unit", HvacBuild.Port(s.Coil, "top"));
            s.ReturnDrop = HvacBuild.Place(HvacSpecs.ReturnPlenum, W(-45, 14, 75), Quaternion.identity);
            HvacBuild.MountTo(s.ReturnDrop, "unit", HvacBuild.Port(s.Furnace, "return"));

            // --- 8x12 trunk across the attic on top of the joists, end cap
            var tp = HvacBuild.Port(s.Plenum, "trunk");
            Vector3 t0 = tp.WorldPos; float tx = W(72, 0, 0).x;
            var trunk = HvacBuild.Route(HvacSpecs.Trunk8x12, tp, null, new Vector3(tx, t0.y, t0.z), new Vector3(tx, t0.y, W(0, 0, 24).z));
            Assert.IsTrue(trunk.Ok, "trunk route failed: " + string.Join(";", trunk.Warnings));
            HvacBuild.EndCap(trunk.Runs[trunk.Runs.Count - 1], false);

            // --- two 6" takeoffs on top of the trunk
            Vector3 p1 = new Vector3(tx, t0.y, W(0, 0, 56).z), p2 = new Vector3(tx, t0.y, W(0, 0, 40).z);
            s.Takeoff1 = HvacBuild.Takeoff(HvacBuild.RunNear(p1, HvacSpecs.Trunk8x12), p1, Vector3.up, 6f);
            Assert.NotNull(s.Takeoff1, "takeoff 1");
            s.Takeoff2 = HvacBuild.Takeoff(HvacBuild.RunNear(p2, HvacSpecs.Trunk8x12), p2, Vector3.up, 6f);
            Assert.NotNull(s.Takeoff2, "takeoff 2");

            // --- ceiling boots between the joists, registers below, 6" branches
            s.Boot1 = HvacBuild.Place(HvacSpecs.Boot4x10, W(36, C + 3.75f, 56), Quaternion.identity);
            s.Boot2 = HvacBuild.Place(HvacSpecs.Boot4x10, W(108, C + 3.75f, 40), Quaternion.identity);
            s.Reg1 = HvacBuild.Place(HvacSpecs.Reg4x10, W(36, C + 0.5f - 0.06f, 56), Quaternion.identity);
            s.Reg2 = HvacBuild.Place(HvacSpecs.Reg4x10, W(108, C + 0.5f - 0.06f, 40), Quaternion.identity);
            float by = Y(122);
            foreach (var (to, boot) in new[] { (s.Takeoff1, s.Boot1), (s.Takeoff2, s.Boot2) })
            {
                var c = to.Port("collar"); var b = HvacBuild.Port(boot, "collar");
                var r = HvacBuild.Route(HvacSpecs.Round6, c, b, new Vector3(c.WorldPos.x, by, c.WorldPos.z), new Vector3(b.WorldPos.x, by, b.WorldPos.z));
                Assert.IsTrue(r.Ok, "branch route failed: " + string.Join(";", r.Warnings));
            }

            // --- ceiling return grille (14x20 between joists) back to the return drop in 8x16
            s.Grille = HvacBuild.Place(HvacSpecs.Grille14x20, W(24, C + 10.75f / 2 - 0.06f, 88), Quaternion.identity);
            var gp = HvacBuild.Port(s.Grille, "duct"); var rp = HvacBuild.Port(s.ReturnDrop, "duct");
            float ry = Y(124), zN = W(0, 0, 116).z;
            var ret = HvacBuild.Route(HvacSpecs.Trunk8x16, gp, rp,
                new Vector3(gp.WorldPos.x, ry, gp.WorldPos.z), new Vector3(gp.WorldPos.x, ry, zN),
                new Vector3(rp.WorldPos.x, ry, zN), new Vector3(rp.WorldPos.x, ry, rp.WorldPos.z));
            Assert.IsTrue(ret.Ok, "return route failed: " + string.Join(";", ret.Warnings));

            // --- condenser on a pad outside, line set (both lines) + condensate
            s.Pad = HvacBuild.Place(HvacSpecs.Pad, W(-96, 1.5f, 72), Quaternion.identity);
            s.Condenser = HvacBuild.Place("hvac-ac3t", W(-96, 3 + 16, 72), Quaternion.identity);
            var cs = HvacBuild.Port(s.Coil, "suction"); var cl = HvacBuild.Port(s.Coil, "liquid");
            var vs = HvacBuild.Port(s.Condenser, "suction"); var vl = HvacBuild.Port(s.Condenser, "liquid");
            float zA = W(0, 0, 90).z, zB = W(0, 0, 87.5f).z, xD = W(-66, 0, 0).x, xE = W(-68, 0, 0).x;
            var suc = HvacBuild.Route(HvacSpecs.LineSuction, cs, vs,
                new Vector3(cs.WorldPos.x, cs.WorldPos.y, zA), new Vector3(xD, cs.WorldPos.y, zA), new Vector3(xD, vs.WorldPos.y, zA), new Vector3(xD, vs.WorldPos.y, vs.WorldPos.z));
            var liq = HvacBuild.Route(HvacSpecs.LineLiquid, cl, vl,
                new Vector3(cl.WorldPos.x, cl.WorldPos.y, zB), new Vector3(xE, cl.WorldPos.y, zB), new Vector3(xE, vl.WorldPos.y, zB), new Vector3(xE, vl.WorldPos.y, vl.WorldPos.z));
            Assert.IsTrue(suc.Ok && liq.Ok, "line set route failed");
            var cd = HvacBuild.Port(s.Coil, "condensate");
            float zC = W(0, 0, 95).z;
            var cond = HvacBuild.Route(HvacSpecs.Condensate, cd, null, 0.25f,
                new Vector3(cd.WorldPos.x, cd.WorldPos.y, zC), new Vector3(cd.WorldPos.x, Y(10), zC), new Vector3(W(-60, 0, 0).x, Y(10), zC));
            Assert.IsTrue(cond.Ok, "condensate route failed");

            // --- bath fan in the ceiling near the exterior wall, 4" duct to a wall cap
            s.Fan = HvacBuild.Place("hvac-bathfan", W(96, C + 3, 136), Quaternion.identity);
            var fe = HvacBuild.Port(s.Fan, "exhaust");
            s.Cap = HvacBuild.Place(HvacSpecs.WallCap4, new Vector3(W(109, 0, 0).x, fe.WorldPos.y, W(0, 0, 148.75f).z), Quaternion.identity);
            var cp = HvacBuild.Port(s.Cap, "duct");
            var ex = HvacBuild.Route(HvacSpecs.Exhaust4, fe, cp, new Vector3(cp.WorldPos.x, fe.WorldPos.y, fe.WorldPos.z));
            Assert.IsTrue(ex.Ok, "exhaust route failed");

            s.Tstat = HvacBuild.Place(HvacSpecs.Thermostat, W(0.55f, 60, 96), Quaternion.Euler(0, 90, 0));
            return s;
        }

        static string Dump(IEnumerable<Finding> f) => string.Join("\n", f.Select(x => x.Rule + ": " + x.Note));

        [UnityTest]
        public IEnumerator DuctRoom()
        {
            yield return Frames(10);
            MepStock.Free = true;
            var s = Build();
            yield return Frames(3);

            // un-taped: every duct joint leaks
            var raw = AirflowSim.Evaluate();
            Assert.Greater(raw.UntapedDuctJoints.Count, 10, "expected many un-taped joints before sealing");
            float rawDelivered = raw.Room("Primary Bath").Delivered;

            int sealedCount = HvacBuild.SealAll();
            Assert.Greater(sealedCount, 10);
            yield return Frames(3);

            var rep = AirflowSim.Evaluate();
            var room = rep.Room("Primary Bath");
            Assert.NotNull(room, "room missing from report");
            var unit = rep.Units.Find(u => u.Unit.gameObject == s.Furnace.gameObject);
            Assert.NotNull(unit, "furnace not found");
            Assert.AreEqual(2, room.Boots, "both boots should be fed");
            Assert.AreEqual(2, room.Registers);
            Assert.IsTrue(room.Ok, $"room airflow LOW: {room.Delivered:0}/{room.Required:0}");
            Assert.Greater(room.Delivered, rawDelivered, "taping should raise delivered airflow");
            Assert.AreEqual(144f, room.Required, 1f);
            Assert.IsTrue(unit.ReturnConnected, "return not connected");
            Assert.AreEqual(0, rep.UntapedDuctJoints.Count);
            var ls = rep.LineSets.Find(x => x.Condenser.gameObject == s.Condenser.gameObject);
            Assert.NotNull(ls); Assert.IsTrue(ls.Ok, "line set: " + ls.Note);
            Assert.AreEqual(s.Coil.GetComponent<NetNode>(), ls.Coil);
            var cs = rep.Condensate.Find(x => x.Source.gameObject == s.Coil.gameObject);
            Assert.NotNull(cs); Assert.IsTrue(cs.Ok, "condensate: " + cs.Note);
            var es = rep.Exhaust.Find(x => x.Fan.gameObject == s.Fan.gameObject);
            Assert.NotNull(es); Assert.IsTrue(es.Ok, "exhaust: " + es.Note);
            Debug.Log($"[HVAC test] room {room.Delivered:0}/{room.Required:0} CFM, leaked {rep.TotalLeaked:0}, untaped before {raw.UntapedDuctJoints.Count}");

            var findings = MepInspection.Run(Trade.Hvac);
            Assert.IsEmpty(findings, "rough mechanical should pass:\n" + Dump(findings));
            Assert.IsTrue(MepInspection.LastPassed[Trade.Hvac]);

            // --- screenshots
            Shot(Cam(W(150, 165, -45), W(25, 92, 80), 55f), "mep_hvac_01_wide");
            Shot(Cam(W(-60, 190, -20), W(30, 100, 70)), "mep_hvac_02_closet_attic");
            Shot(Cam(W(118, 136, 12), W(86, 104, 46), 50f), "mep_hvac_03_takeoff_boot");
            Shot(Cam(W(100, 52, 8), W(96, 97, 44), 55f), "mep_hvac_04_registers_below");
            Shot(Cam(W(-30, 62, 150), W(-80, 22, 78), 55f), "mep_hvac_05_condenser_lineset");
            Shot(Cam(W(-34, 66, 150), W(-28, 50, 76), 55f), "mep_hvac_06_furnace");
            var trunkVis = HvacBuild.RunNear(W(72, 107, 30), HvacSpecs.Trunk8x12).transform.Find("Visual").GetComponent<MeshRenderer>().sharedMaterial;
            Debug.Log($"[HVAC test] trunk material {trunkVis.name} scale {trunkVis.mainTextureScale}");
            Shot(Cam(W(70, 128, 160), W(105, 100, 138), 50f), "mep_hvac_07_bathfan_cap");

            // --- leave one joint untaped: the takeoff collar on branch 1
            var col = s.Takeoff1.Port("collar");
            col.Joined = false; s.Takeoff1.RefreshJoints(); NetGraph.Touch();
            var rep2 = AirflowSim.Evaluate();
            Assert.Less(rep2.Room("Primary Bath").Delivered, room.Delivered - 5f, "untaped joint should leak");
            Assert.AreEqual(1, rep2.UntapedDuctJoints.Count);
            var f2 = MepInspection.Run(Trade.Hvac);
            Assert.IsTrue(f2.Any(f => f.Rule == "hvac-joints"), "untaped joint not flagged:\n" + Dump(f2));

            // --- disconnect boot 2: branch dumps into the attic, room goes LOW
            var bc = HvacBuild.Port(s.Boot2, "collar");
            var br = bc.Run;
            br.Detach(bc);
            br.Points[br.Points.Count - 1] += Vector3.up * Units.In(3f);
            br.Rebuild();
            NetGraph.Touch();
            yield return Frames(2);
            var rep3 = AirflowSim.Evaluate();
            var room3 = rep3.Room("Primary Bath");
            Assert.IsFalse(room3.Ok, $"room should be LOW with a boot disconnected ({room3.Delivered:0}/{room3.Required:0})");
            Assert.Less(room3.Delivered, rep2.Room("Primary Bath").Delivered);
            var f3 = MepInspection.Run(Trade.Hvac);
            Assert.IsTrue(f3.Any(f => f.Rule == "hvac-boots"), "disconnected boot not flagged:\n" + Dump(f3));
            Assert.IsTrue(f3.Any(f => f.Rule == "hvac-airflow"), "low airflow not flagged:\n" + Dump(f3));
            Assert.IsTrue(f3.Any(f => f.Rule == "hvac-open"), "open duct end not flagged:\n" + Dump(f3));
            Assert.IsFalse(MepInspection.LastPassed[Trade.Hvac]);
            Debug.Log("[HVAC test] failures:\n" + Dump(f3));
            Shot(Cam(W(118, 136, 12), W(86, 104, 46), 50f), "mep_hvac_08_failed_markers");
        }

        [UnityTest]
        public IEnumerator FlatCondensateAndAtticExhaustFail()
        {
            yield return Frames(5);
            MepStock.Free = true;
            var o = new Vector3(30f, 0f, 30f);
            var ah = HvacBuild.Place("hvac-airhandler", o + new Vector3(0, 0.61f, 0), Quaternion.identity);
            var cp = HvacBuild.Port(ah, "condensate");
            var flat = HvacBuild.Route(HvacSpecs.Condensate, cp, null, 0f, cp.WorldPos + Vector3.forward * 0.3f, cp.WorldPos + new Vector3(2f, 0, 0.3f));
            Assert.IsTrue(flat.Ok);
            var fan = HvacBuild.Place("hvac-bathfan", o + new Vector3(3f, 2.5f, 0), Quaternion.identity);
            var fe = HvacBuild.Port(fan, "exhaust");
            HvacBuild.Route(HvacSpecs.Exhaust4, fe, null, fe.WorldPos + Vector3.right * 0.8f);
            HvacBuild.SealAll();
            var rep = AirflowSim.Evaluate();
            var c = rep.Condensate.Find(x => x.Source == ah.GetComponent<NetNode>());
            Assert.NotNull(c);
            Assert.IsFalse(c.Sloped, "flat condensate should fail: " + c.Note);
            var e = rep.Exhaust.Find(x => x.Fan.gameObject == fan.gameObject);
            Assert.IsFalse(e.Ok, "exhaust dumping in the attic should fail");
            var f = MepInspection.Run(Trade.Hvac);
            Assert.IsTrue(f.Any(x => x.Rule == "hvac-condensate"), Dump(f));
            Assert.IsTrue(f.Any(x => x.Rule == "hvac-exhaust"), Dump(f));
            Assert.IsTrue(f.Any(x => x.Rule == "hvac-return"), Dump(f));
            foreach (var r in flat.Runs) Object.Destroy(r.gameObject);
            foreach (var r in NetGraph.RunsAt(fe.Node)) Object.Destroy(r.gameObject);
            Object.Destroy(ah.gameObject); Object.Destroy(fan.gameObject);
            yield return null;
        }
    }
}
