using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Groundbreak.Tests.FinishTestKit;

namespace Groundbreak.Tests
{
    /// <summary>
    /// Milestone 5 end-to-end: a 12'x12' room corner built from real framing is insulated, drywalled (ceiling first; hang,
    /// tape, 3 coats with dry time, sand, texture, prime, paint), an interior door is hung and cased, and outside one wall
    /// is wrapped + sided with vinyl and the other wrapped + faced with brick veneer. Every phase passes inspection.
    /// </summary>
    public class CloseupIntegrationTests
    {
        [UnityTearDown] public IEnumerator TearDownScene() => CleanScene();

        static readonly Vector3 O = new Vector3(80f, 0f, 80f);   // SW outside corner of the room
        static Vector3 P(float xIn, float yIn, float zIn) => O + new Vector3(Units.In(xIn), Units.In(yIn), Units.In(zIn));

        [UnityTest]
        public IEnumerator InsulateDrywallPaintTrimAndSide()
        {
            yield return Frames(5);
            if (GameClock.I != null) GameClock.I.Hours = 11f;
            FinishStock.Unlimited = true;
            Game.Stock.Add("brick-modular", 3000); Game.Stock.Add("mortar-s80", 80);
            foreach (var t in new[] { "utility-knife", "staple-gun", "foam-gun", "caulk-gun", "drywall-tsquare", "drywall-lift", "screw-gun",
                                      "drywall-router", "taping-knives", "sanding-pole", "paint-roller", "paint-brush", "finish-nailer",
                                      "miter-saw", "coping-saw", "cap-stapler", "siding-nailer", "brick-trowel", "ext-snips" })
                FinishStock.Give(t);

            // ---------------------------------------------------------------- framing
            // Wall A (south, exterior): along +X, room to +Z, OSB outside.  Wall B (west, exterior): along +Z, room to +X.
            // Wall C (east partition): along +Z with a 34" door RO (32" prehung door).  2x6 ceiling joists 16" O.C. span N-S.
            StudWall(O, Vector3.right, Vector3.forward, 144f);
            Sheathing(O, Vector3.right, Vector3.forward, 144f);
            StudWall(P(0, 0, 3.5f), Vector3.forward, Vector3.right, 140.5f);
            Sheathing(P(0, 0, -0.4375f), Vector3.forward, Vector3.right, 144.4375f);
            StudWall(P(144, 0, 3.5f), Vector3.forward, Vector3.left, 140.5f, new Opening { LeftIn = 36f, WidthIn = 34f, HeadIn = 82.5f });
            CeilingJoists(O, Vector3.right, Vector3.forward, 144f, 144f);
            OutletBox(P(18.5f, 16f, 3.5f), Vector3.forward);            // receptacle box in wall A
            OutletBox(P(3.5f, 16f, 60f), Vector3.right);                 // receptacle box in wall B
            yield return Frames(3);
            Physics.SyncTransforms();
            InsulationInspection.Scope = new Bounds(P(72, 50, 72), new Vector3(6f, 4f, 6f));

            var roomCam = Cam(P(120, 62, 128), P(40, 44, 0), 70f);
            Shot(roomCam, "m5_01_framing");

            var wallA = Insulation.FaceFrom(P(64, 48, 60), Vector3.back);
            var wallB = Insulation.FaceFrom(P(60, 48, 67.5f), Vector3.left);
            var wallC = Insulation.FaceFrom(P(72, 48, 115.5f), Vector3.right);
            var ceil = Insulation.FaceFrom(P(64, 60, 70), Vector3.up);
            Assert.NotNull(wallA, "wall A face"); Assert.NotNull(wallB, "wall B face"); Assert.NotNull(wallC, "wall C face"); Assert.NotNull(ceil, "ceiling face");
            Assert.IsTrue(ceil.IsCeiling, "ceiling expected: " + ceil);
            Assert.AreEqual(1, wallC.Openings().Count, "door rough opening in wall C");

            // ---------------------------------------------------------------- insulation
            int battsA = Insulation.InsulateWall(wallA, "ins-r13-k15");
            int battsB = Insulation.InsulateWall(wallB, "ins-r13-k15");
            Insulation.AirSeal(wallA); Insulation.AirSeal(wallB);
            Debug.Log($"[M5] batts A={battsA} B={battsB}");
            Assert.Greater(battsA, 6); Assert.Greater(battsB, 6);
            yield return Frames(2);
            Shot(Cam(P(30, 40, 50), P(30, 30, 0), 60f), "m5_02_insulated_wall");
            var ins = CloseupInspection.Run(CloseupInspection.Insulation);
            foreach (var v in ins.Violations) Debug.Log("[M5] insulation: " + v.Message);
            Assert.IsTrue(ins.Passed, ins.ToString());

            // ---------------------------------------------------------------- drywall: ceiling first, then walls
            var sCeil = DrywallJob.Complete(ceil, "dw-12-4x12", "knockdown", "Ceiling White", Sheen.Flat);
            Assert.NotNull(sCeil, "ceiling drywall");

            // Wall A step by step so each stage is photographed.
            var sA = Drywall.HangSurface(wallA, "dw-12-4x12");
            Assert.NotNull(sA, "wall A drywall"); Assert.Greater(sA.Sheets.Count, 1);
            Drywall.CutOutBoxes(sA);
            Drywall.ScrewAll(sA);
            sA.Refresh(); yield return Frames(2);
            var wallCam = Cam(P(72, 56, 100), P(72, 46, 0), 60f);
            Shot(wallCam, "m5_03_hung_screwed");
            for (int coat = 1; coat <= 3; coat++)
            {
                Drywall.CoatAll(sA, coat);
                if (coat == 1) { sA.Refresh(); yield return Frames(2); Shot(wallCam, "m5_04_tape_coat1_wet"); }
                FinishTime.Skip(24.1f);
            }
            sA.Refresh(); yield return Frames(2);
            Shot(wallCam, "m5_05_coat3_dry");
            Drywall.SandAll(sA);
            Drywall.ApplyTexture(sA, "orange-peel"); FinishTime.Skip(8.1f);
            PaintJob.Prime(sA); FinishTime.Skip(2.1f);
            sA.Refresh(); yield return Frames(2);
            Shot(wallCam, "m5_06_primed");
            PaintJob.Paint(sA, PaintColors.Get("Sage Green"), Sheen.Eggshell); FinishTime.Skip(2.1f);
            PaintJob.Paint(sA, PaintColors.Get("Sage Green"), Sheen.Eggshell); FinishTime.Skip(2.1f);
            sA.Refresh();

            var sB = DrywallJob.Complete(wallB, "dw-12-4x12", "orange-peel", "Sage Green", Sheen.Eggshell);
            var sC = DrywallJob.Complete(wallC, "dw-12-4x12", "orange-peel", "Linen", Sheen.Eggshell);
            Assert.NotNull(sB); Assert.NotNull(sC);
            yield return Frames(2);
            Shot(wallCam, "m5_07_painted_wall");
            var dw = CloseupInspection.Run(CloseupInspection.Drywall);
            foreach (var v in dw.Violations) Debug.Log("[M5] drywall: " + v.Message);
            Assert.IsTrue(dw.Passed, dw.ToString());

            // ---------------------------------------------------------------- door + trim
            var door = DoorHanging.Hang(wallC, wallC.Openings()[0], "door-int-6p32");
            Assert.NotNull(door, "door hung");
            var casing = Casing.CaseDoor(door);
            Assert.Greater(casing.Count, 2, "casing pieces");
            WallTrim.RunBase(wallA); WallTrim.RunBase(wallB); WallTrim.RunBase(wallC);
            WallTrim.NailAll(); WallTrim.CaulkAll();
            yield return Frames(2);
            Shot(Cam(P(40, 60, 80), P(144, 42, 53), 60f), "m5_08_door_cased");
            Shot(roomCam, "m5_09_room");
            var fin = CloseupInspection.Run(CloseupInspection.Finish);
            foreach (var v in fin.Violations) Debug.Log("[M5] finish: " + v.Message);
            Assert.IsTrue(fin.Passed, fin.ToString());

            // ---------------------------------------------------------------- exterior: vinyl on A, brick on B
            Assert.IsTrue(Physics.Raycast(new Ray(P(72, 48, -40), Vector3.forward), out var hA, 3f), "no hit on wall A sheathing");
            Assert.IsTrue(Physics.Raycast(new Ray(P(-40, 48, 72), Vector3.right), out var hB, 3f), "no hit on wall B sheathing");
            var exA = ExteriorWall.FromHit(hA); var exB = ExteriorWall.FromHit(hB);
            Assert.NotNull(exA); Assert.NotNull(exB);
            Debug.Log($"[M5] exA studs={(exA.Studs != null ? exA.Studs.ToString() : "null")} lines={exA.StudLines.Count} face={exA.Face}");
            Debug.Log($"[M5] exB studs={(exB.Studs != null ? exB.Studs.ToString() : "null")} lines={exB.StudLines.Count} face={exB.Face}");
            Assert.IsTrue(HouseWrap.WrapWall(exA), "wrap A"); Assert.IsTrue(HouseWrap.WrapWall(exB), "wrap B");
            yield return Frames(2);
            var corner = Cam(P(-150, 70, -160), P(40, 40, 40), 55f);
            Shot(corner, "m5_10_wrapped");
            Assert.IsTrue(VinylSiding.InstallAll(exA, "White"), "vinyl on A");
            Assert.IsTrue(BrickVeneer.LayAll(exB), "brick on B");
            ExteriorTrim.CaulkAll(exA); ExteriorTrim.CaulkAll(exB);
            yield return Frames(2);
            Shot(corner, "m5_11_exterior_corner");
            Shot(Cam(P(60, 64, -70), P(60, 40, 0), 55f), "m5_12_vinyl");
            Shot(Cam(P(-70, 60, 50), P(0, 36, 60), 55f), "m5_13_brick");
            var ext = CloseupInspection.Run(CloseupInspection.Exterior);
            foreach (var v in ext.Violations) Debug.Log("[M5] exterior: " + v.Message);
            Assert.IsTrue(ext.Passed, ext.ToString());

            InsulationInspection.Scope = null;
        }
    }
}
