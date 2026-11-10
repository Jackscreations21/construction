using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Groundbreak.Tests.FinishTestKit;

namespace Groundbreak.Tests
{
    /// <summary>Insulation + drywall + paint on a real 12' exterior stud wall (door RO, two boxes, a drain pipe through
    /// the plates) under 12'x12' ceiling joists. Screenshots at every stage in Logs/shots/idw_*.png.</summary>
    public class InsulationDrywallTests
    {
        [UnityEngine.TestTools.UnityTearDown] public System.Collections.IEnumerator TearDownScene() => FinishTestKit.CleanScene();

        static readonly Vector3 O = new Vector3(60f, Units.In(0.75f) + 0.002f, 60f);   // outside corner of the wall on top of the 3/4" subfloor
        static Vector3 In(float x, float y, float zIntoRoom) => O + new Vector3(Units.In(x), Units.In(y), -Units.In(zIntoRoom));

        static void SetHour(float h)
        {
            if (GameClock.I == null) return;
            if (h < GameClock.I.Hours) GameClock.I.Day++;
            GameClock.I.Hours = h;
        }

        static GameObject Pipe(Vector3 bottom, float len, float dia)
        {
            var go = new GameObject("DrainPipe");
            go.transform.position = bottom + Vector3.up * len * 0.5f;
            var cc = go.AddComponent<CapsuleCollider>(); cc.direction = 1; cc.height = len; cc.radius = dia * 0.5f;
            var b = new MeshBuilder(); b.AddCylinder(Vector3.zero, dia * 0.5f, len, 20, 0);
            var v = new GameObject("Visual"); v.transform.SetParent(go.transform, false);
            v.AddComponent<MeshFilter>().sharedMesh = b.Build("pipe");
            v.AddComponent<MeshRenderer>().sharedMaterial = Materials.Get(SurfaceKind.PaintedWhite);
            return go;
        }

        static Light WorkLight(Vector3 p)
        {
            var l = new GameObject("WorkLight").AddComponent<Light>();
            l.type = LightType.Point; l.range = 11f; l.intensity = 0.8f; l.color = new Color(1f, 0.96f, 0.9f); l.shadows = LightShadows.Soft;
            l.transform.position = p;
            return l;
        }

        static Tool Equip(string id)
        {
            var t = FinishStock.Give(id);
            Assert.NotNull(t, "tool " + id);
            Game.Belt.Assign(t, 1);
            Game.Belt.Select(1);
            return t;
        }

        static string Violations(CloseupInspection.Result r)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var v in r.Violations) sb.Append(v.RuleId).Append(": ").Append(v.Message).Append(" | ");
            return sb.ToString();
        }

        [UnityTest]
        public IEnumerator InsulateDrywallTapePaintWall()
        {
            yield return Frames(5);
            var savedSlots = (Tool[])Game.Belt.Slots.Clone();
            FinishStock.Unlimited = true;
            SetHour(9.5f);
            InsulationInspection.Scope = new Bounds(O + new Vector3(1.8f, 1.5f, -1.8f), new Vector3(5f, 5f, 5f));

            // ---------------- framing: exterior wall (OSB outside), door RO, joists, subfloor, boxes, pipe
            StudWall(O, Vector3.right, Vector3.back, 144f, new Opening { LeftIn = 88f, WidthIn = 38f, SillIn = 0f, HeadIn = 82.5f });
            Sheathing(O, Vector3.right, Vector3.back, 144f);
            CeilingJoists(O, Vector3.right, Vector3.back, 144f, 144f);
            Subfloor(O + Vector3.back * Units.In(144f) + Vector3.down * 0.0005f, 144f, 144f);
            var box1 = OutletBox(In(18f, 16f, 3.5f), Vector3.back);
            var box2 = OutletBox(In(130.25f, 42f, 3.5f), Vector3.back);
            var pipe = Pipe(In(40f, -2f, 1.75f), 2.62f, Units.In(1.9f));
            var light = WorkLight(In(64f, 84f, 112f));
            yield return Frames(3);
            Physics.SyncTransforms();

            var wall = Insulation.FaceFrom(In(32f, 48f, 30f), Vector3.forward);
            Assert.NotNull(wall, "wall face");
            Assert.IsTrue(wall.IsWall && Insulation.IsExterior(wall), "interior face of a sheathed wall is exterior: " + wall);
            var ceil = Insulation.FaceFrom(In(32f, 60f, 40f), Vector3.up);
            Assert.NotNull(ceil, "ceiling face"); Assert.IsTrue(ceil.IsCeiling, "ceiling: " + ceil);
            Assert.AreEqual(2, Insulation.Penetrations(wall).Count, "pipe through bottom + top plate");
            Assert.AreEqual(2, Insulation.Boxes(wall).Count, "two boxes");

            var overview = Cam(In(76f, 66f, 128f), In(70f, 46f, 0f), 62f);
            var close = Cam(In(34f, 58f, 46f), In(30f, 38f, 3.5f), 58f);
            Shot(overview, "idw_01_bare_framing");

            // ---------------- insulation, first pass with mistakes
            var knife = (UtilityKnife)Equip("utility-knife");
            knife.Mode = 1; // R-13 unfaced 15" for the first bay
            AimPlayerAt(In(8f, 0f, 40f), In(8f, 50f, 3.5f));   // bay 1 (0.75"-16")
            yield return Frames(2);
            Game.Belt.Active.Primary();
            Assert.AreEqual(1, Insulation.Batts.Count, "knife fit a batt: " + Game.ToastText);
            foreach (var bay in wall.Bays())
                if (!bay.IsOpening && bay.R.height > Units.In(3f) && Insulation.BattIn(wall, bay) == null) Insulation.FitBatt(wall, bay, "ins-r13-k15", false);
            Batt skip = null;
            foreach (var b in Insulation.Batts) { if (skip == null && b.P.Faced && !b.Compressed && b.R.height > 2f) { skip = b; continue; } Insulation.Staple(b); }
            yield return Frames(2);
            var res = CloseupInspection.Run(CloseupInspection.Insulation);
            Debug.Log("[IDW] first insulation inspection: " + res + " — " + Violations(res));
            Assert.IsFalse(res.Passed);
            var rules = new HashSet<string>(); foreach (var v in res.Violations) rules.Add(v.RuleId);
            Assert.IsTrue(rules.Contains("ins-compress"), "compressed over box/pipe flagged");
            Assert.IsTrue(rules.Contains("ins-staple"), "unstapled batt flagged");
            Assert.IsTrue(rules.Contains("ins-firestop"), "plate penetrations flagged");
            Assert.IsTrue(rules.Contains("ins-airseal"), "boxes not sealed flagged");
            Assert.Greater(CloseupInspection.MarkerCount(CloseupInspection.Insulation), 3);
            yield return Frames(2);
            Shot(overview, "idw_03_insulation_fail_markers");
            Shot(close, "idw_03b_compressed_closeup");

            // ---------------- fix: split around, staple, air-seal, fire caulk (one with the caulk gun)
            Equip("utility-knife");
            AimPlayerAt(In(24f, 0f, 36f), In(24f, 30f, 3.5f));
            yield return Frames(2);
            Game.Belt.Active.Primary();   // click the compressed batt over box 1 → split around
            Assert.IsFalse(Insulation.BattAt(wall, wall.ToUV(In(18f, 16f, 3.5f))).Compressed, "knife split the batt: " + Game.ToastText);
            foreach (var b in Insulation.Batts) { Insulation.SplitAround(b); Insulation.Staple(b); }
            var gun = (CaulkGun)Equip("caulk-gun");
            for (int i = 0; i < CaulkGun.Tubes.Count; i++) if (CaulkGun.Tubes[i].Name == "Fire caulk (red)") gun.Mode = i;
            Assert.AreEqual("Fire caulk (red)", gun.Active.Name);
            AimPlayerAt(In(40f, 0f, 30f), In(40f, 2f, 3.5f));
            yield return Frames(2);
            gun.Primary();
            Assert.AreEqual(1, Insulation.FireBlocked.Count, "caulk gun fire-blocked the bottom plate penetration: " + Game.ToastText);
            Insulation.AirSeal(wall);
            yield return Frames(2);
            res = CloseupInspection.Run(CloseupInspection.Insulation);
            Debug.Log("[IDW] second insulation inspection: " + res + " — " + Violations(res));
            Assert.IsTrue(res.Passed, Violations(res));
            Assert.AreEqual(0, CloseupInspection.MarkerCount(CloseupInspection.Insulation));
            yield return Frames(2);
            Shot(close, "idw_02_insulated_closeup");
            Shot(Cam(In(70f, 62f, 60f), In(56f, 50f, 0f), 70f), "idw_02b_insulated_wall");
            Shot(Cam(In(46f, 20f, 18f), In(38f, 2f, 2f), 60f), "idw_02c_pipe_firecaulk");

            // ---------------- drywall: walls refused until the ceiling is hung
            Assert.IsNull(Drywall.HangNext(wall, "dw-12-4x12"));
            StringAssert.Contains("ceiling", Game.ToastText);
            var lift = Equip("drywall-lift");
            AimPlayerAt(In(32f, 0f, 60f), In(32f, 97f, 60f));
            yield return Frames(2);
            lift.Primary();
            var cs = Drywall.SurfaceFor(ceil);
            Assert.NotNull(cs, "lift hung a ceiling sheet: " + Game.ToastText);
            Drywall.HangSurface(ceil, "dw-12-4x12");
            Assert.AreEqual(3, cs.Sheets.Count, "12'x12' ceiling = three 4x12 sheets");
            var ws = Drywall.HangSurface(wall, "dw-12-4x12");
            Assert.NotNull(ws);
            Assert.AreEqual(2, ws.Sheets.Count, "two horizontal 4x12 rows");
            Assert.AreEqual(96.625f, Units.ToInches(ws.Sheets[0].R.yMax - wall.Bounds.yMin), 0.05f, "top row tight to the ceiling drywall");
            Assert.AreEqual(0.625f, Units.ToInches(ws.Sheets[1].R.yMin - wall.Bounds.yMin), 0.05f, "bottom row 5/8\" off the floor");
            Assert.AreEqual(1, ws.Sheets[1].Holes.Count, "door opening cut out");
            Assert.AreEqual(2, ws.Sheets[0].Covered.Count + ws.Sheets[1].Covered.Count, "both boxes covered until cut out");
            Assert.AreEqual(0.5f, Units.ToInches(FinishHooks.CoveringThickness(wall, new Vector2(wall.Bounds.xMin + 0.3f, wall.Bounds.yMin + 1.2f))), 0.01f);
            yield return Frames(3);
            ws.Refresh(); cs.Refresh();
            res = CloseupInspection.Run(CloseupInspection.Drywall);
            Debug.Log("[IDW] first drywall inspection: " + res + " — " + Violations(res));
            rules.Clear(); foreach (var v in res.Violations) rules.Add(v.RuleId);
            Assert.IsTrue(rules.Contains("dw-boxes") && rules.Contains("dw-screws"));
            yield return Frames(2);
            Shot(overview, "idw_05_drywall_fail_markers");

            // cut-outs: router on box 1, keyhole saw (3 passes) on box 2
            var router = Equip("drywall-router");
            AimPlayerAt(In(18f, 0f, 36f), In(18f, 16f, 4f));
            yield return Frames(2);
            router.Primary();
            var saw = Equip("keyhole-saw");
            AimPlayerAt(In(128f, 0f, 30f), In(130.25f, 42f, 4f));
            yield return Frames(2);
            saw.Primary(); saw.Primary();
            Assert.AreEqual(1, ws.Sheets[0].Covered.Count + ws.Sheets[1].Covered.Count, "keyhole saw needs 3 passes");
            saw.Primary();
            Assert.AreEqual(0, ws.Sheets[0].Covered.Count + ws.Sheets[1].Covered.Count, "both boxes cut out: " + Game.ToastText);

            // screws: one with the gun, then auto-feed everything
            var sg = Equip("screw-gun");
            AimPlayerAt(In(64f, 0f, 30f), In(64f, 40f, 4f));
            yield return Frames(2);
            sg.Primary();
            Assert.AreEqual(1, ws.Screws.Count, "screw gun drove one screw: " + Game.ToastText);
            Drywall.ScrewAll(cs); Drywall.ScrewAll(ws);
            Assert.Greater(ws.Screws.Count, 40); Assert.Greater(cs.Screws.Count, 80);
            res = CloseupInspection.Run(CloseupInspection.Drywall);
            Debug.Log("[IDW] drywall inspection after cut-outs + screws: " + res + " — " + Violations(res));
            Assert.IsTrue(res.Passed, Violations(res));
            yield return Frames(3);
            Shot(overview, "idw_04_hung_screwed");
            Shot(close, "idw_04b_screws_closeup");

            // stilts raise the eye 18"
            var st = Equip("stilts");
            st.Primary();
            yield return Frames(3);
            Assert.IsTrue(Stilts.Worn);
            Assert.Greater(Game.Player.Cam.transform.localPosition.y, PlayerRig.EyeHeight + 0.4f, "stilts lift the camera");
            st.Primary();

            // ---------------- tape + 3 coats
            var kn = (TapingKnives)Equip("taping-knives");
            kn.Mode = 2; // 10" knife on an untaped joint → wrong knife
            AimPlayerAt(In(40f, 0f, 36f), In(40f, 48.625f, 4f));
            yield return Frames(2);
            kn.Primary();
            StringAssert.Contains("6\"", Game.ToastText);
            kn.Mode = 0;
            kn.Primary();
            StringAssert.Contains("Tape", Game.ToastText);
            Assert.Greater(Drywall.CoatAll(cs, 1), 0);
            Assert.Greater(Drywall.CoatAll(ws, 1), 0);
            Assert.AreEqual(0, Drywall.CoatAll(ws, 2), "coat 2 refused while coat 1 is wet");
            yield return Frames(3);
            Shot(close, "idw_06_coat1_wet");
            Shot(overview, "idw_06b_coat1_wet_overview");
            FinishTime.Skip(24.2f);
            Drywall.CoatAll(cs, 2); Drywall.CoatAll(ws, 2);
            FinishTime.Skip(24.2f);
            yield return Frames(2);
            ws.Refresh();
            Shot(close, "idw_06c_coat2_dry");
            Drywall.CoatAll(cs, 3); Drywall.CoatAll(ws, 3);
            Assert.IsTrue(Drywall.AllCoated(ws, 3, false));
            FinishTime.Skip(24.2f);
            Assert.IsTrue(Drywall.AllCoated(ws, 3, true));
            ws.Refresh(); cs.Refresh();
            yield return Frames(3);
            Shot(close, "idw_07_coat3_dry");
            Shot(overview, "idw_07b_coat3_dry_overview");

            // sand (pole on flats, sponge in corners) + texture
            var sp = (SandingPole)Equip("sanding-pole");
            AimPlayerAt(In(30f, 0f, 36f), In(30f, 48.625f, 4f));
            yield return Frames(2);
            sp.Primary();
            Drywall.SandAll(cs); Drywall.SandAll(ws);
            Assert.IsTrue(Drywall.AllSanded(ws) && Drywall.AllSanded(cs));
            Assert.IsTrue(Drywall.ApplyTexture(ws, "orange-peel"));
            Assert.IsTrue(Drywall.ApplyTexture(cs, "knockdown"));
            FinishTime.Skip(8.2f);
            SetHour(9f);
            yield return Frames(2);
            ws.Refresh(); cs.Refresh();
            Shot(close, "idw_07c_textured");

            // ---------------- prime + paint
            PaintJob.Prime(ws); PaintJob.Prime(cs);
            FinishTime.Skip(2.2f);
            var color = PaintColors.Get("Sage Green");
            PaintColors.Current = PaintColors.All.IndexOf(color);
            var roller = Equip("paint-roller");
            AimPlayerAt(In(60f, 0f, 40f), In(60f, 50f, 4f));
            yield return Frames(2);
            roller.Primary();
            Assert.Greater(ws.Coverage(false, 1), 0.01f, "roller stroke painted: " + Game.ToastText);
            PaintJob.Paint(ws, color, Sheen.Eggshell);
            PaintJob.Paint(cs, PaintColors.Get("Ceiling White"), Sheen.Flat);
            ws.Refresh(); cs.Refresh();
            yield return Frames(3);
            Shot(close, "idw_08_paint_coat1");
            FinishTime.Skip(2.2f);
            PaintJob.Paint(ws, color, Sheen.Eggshell);
            PaintJob.Paint(cs, PaintColors.Get("Ceiling White"), Sheen.Flat);
            FinishTime.Skip(2.2f);
            ws.Refresh(); cs.Refresh();
            Assert.Greater(ws.Coverage(false, 2), 0.98f, "two full coats on the wall");
            Assert.Greater(PaintJob.GallonsUsedPaint, 0.3f);
            SetHour(10f);
            yield return Frames(3);
            Shot(close, "idw_09_painted_closeup");
            Shot(Cam(In(40f, 62f, 40f), In(40f, 96f, 4f), 55f), "idw_09c_corner");
            Shot(overview, "idw_09b_painted_overview");
            res = CloseupInspection.Run(CloseupInspection.Drywall);
            Assert.IsTrue(res.Passed, Violations(res));

            // ---------------- attic: baffles, blown loose fill to R-38, depth rulers
            var cf = Insulation.CeilingFace(ceil);
            foreach (var (bay, end) in Insulation.EaveBays(cf, InsulationInspection.ExteriorWalls())) Insulation.InstallBaffle(cf, bay, end);
            Assert.Greater(Insulation.Baffles.Count, 5, "eave baffles along the exterior wall");
            var af = Insulation.BlowAttic(ceil, InsulationRules.AtticMinR);
            Assert.NotNull(af);
            Insulation.InstallRulers(ceil);
            res = CloseupInspection.Run(CloseupInspection.Insulation);
            Assert.IsTrue(res.Passed, Violations(res));
            Assert.GreaterOrEqual(af.RValue, 38f);
            yield return Frames(2);
            Shot(Cam(In(120f, 128f, 100f), In(40f, 104f, 10f), 60f), "idw_10_attic_loosefill");

            // ---------------- leave the scene as we found it for the other tests
            Object.Destroy(light.gameObject);
            var area = InsulationInspection.Scope.Value;
            foreach (var pc in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (area.Contains(pc.transform.position)) Object.Destroy(pc.gameObject);
            foreach (var fl in Object.FindObjectsByType<FinishLayer>(FindObjectsSortMode.None)) if (fl != null && area.Contains(fl.transform.position)) Object.Destroy(fl.gameObject);
            foreach (var ds in Object.FindObjectsByType<DrywallSurface>(FindObjectsSortMode.None)) Object.Destroy(ds.gameObject);
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c.name == "FinishTestCam") Object.Destroy(c.gameObject);
            Object.Destroy(box1); Object.Destroy(box2); Object.Destroy(pipe);
            CloseupInspection.ClearMarkers();
            InsulationInspection.Scope = null;
            FinishStock.Unlimited = false;
            Stilts.Worn = false;
            Game.Belt.Slots = savedSlots;
            Game.Belt.Select(0);
            AimPlayerAt(new Vector3(0f, 0f, -4f), new Vector3(0f, 1.2f, 0f));
            yield return Frames(3);
        }
    }
}
