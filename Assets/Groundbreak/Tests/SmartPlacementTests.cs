using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Smart placement: the ghost works out where a piece belongs from the boards around the aim point.</summary>
    public class SmartPlacementTests
    {
        static readonly Vector3 Site = new Vector3(-7f, 0f, -18f);
        readonly List<GameObject> _made = new List<GameObject>();
        PieceDefinition _prevSel;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static float In(float i) => Units.In(i);

        static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1280, 720, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = prev; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        Piece Make(string id, float lengthIn, Vector3 center, Quaternion rot, MemberRole role)
        {
            var def = Catalog.Get(id);
            var p = Piece.Create(def, lengthIn > 0 ? In(lengthIn) : 0f, center, rot);
            p.gameObject.AddComponent<StructuralMember>().Role = role;
            _made.Add(p.gameObject);
            return p;
        }

        static readonly Quaternion AlongX = Quaternion.LookRotation(Vector3.right, Vector3.up);   // flat board along X
        static readonly Quaternion Upright = Quaternion.LookRotation(Vector3.up, Vector3.right);  // stud, 1.5" along X

        /// <summary>Plate (flat, along X, centred at x0) with studs at the given x offsets (inches from plate centre).</summary>
        Piece Plate(Vector3 c, float lenIn = 96f) => Make("2x4x8", lenIn, c + Vector3.up * In(0.75f), AlongX, MemberRole.BottomPlate);
        Piece Stud(Vector3 plateC, float xIn, float lenIn) =>
            Make("2x4x925", lenIn, plateC + new Vector3(In(xIn), In(1.5f) + In(lenIn) * 0.5f, 0f), Upright, MemberRole.Stud);

        static void PutPlayer(Vector3 feet)
        {
            var cc = Game.Player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            Game.Player.transform.position = feet;
            if (cc != null) cc.enabled = true;
        }

        static IEnumerator AimAt(Vector3 feet, Vector3 target)
        {
            for (int i = 0; i < 4; i++)
            {
                PutPlayer(feet);
                Vector3 d = target - Game.Player.Cam.transform.position;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                Game.Player.SetLook(yaw, pitch);
                yield return null;
            }
            yield return Frames(2);
        }

        static void Select(string id)
        {
            Game.Stock.Add(id, 4);
            Game.Placement.Select(Catalog.Get(id));
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(5);
            for (int i = 0; i < ToolBelt.SlotCount; i++)
                if (Game.Belt.Slots[i] != null && Game.Belt.Slots[i].Id == "place") { Game.Belt.Select(i); break; }
            yield return Frames(2);
            Assert.IsTrue(Game.Placement.Active, "place tool not active");
            _prevSel = Game.Placement.Selected;
            Game.Placement.SmartSnapOff = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var g in _made) if (g != null) Object.Destroy(g);
            _made.Clear();
            if (Game.Placement != null) { Game.Placement.SmartSnapOff = false; Game.Placement.Select(_prevSel); }
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator StudStandsOnPlateAtNext16OC()
        {
            Vector3 c = Site;
            Plate(c);
            Stud(c, -48f + 0.75f, 92.625f);   // end stud flush with the plate end
            Stud(c, -32f, 92.625f);           // 16" O.C. from the end
            yield return Frames(2);
            Select("2x4x925");

            // aim at the plate top 30" from the end: the next mark is 32"
            Vector3 aim = c + new Vector3(In(-48f + 30f), In(1.5f), 0f);
            yield return AimAt(c + new Vector3(In(-18f), 0f, -1.8f), aim);
            var pc = Game.Placement;
            Debug.Log("[SmartSnap] stud: " + pc.StatusLine);
            Assert.IsNotNull(pc.SnapTarget, "no snap target: " + pc.StatusLine);
            StringAssert.Contains("16\" O.C.", pc.SnapLabel);
            Assert.Greater(Vector3.Dot(pc.GhostRotation * Vector3.forward, Vector3.up), 0.99f, "stud not stood up");
            Vector3 want = c + new Vector3(In(-16f), In(1.5f) + In(92.625f) * 0.5f, 0f);
            Assert.Less(Vector3.Distance(pc.GhostPosition, want), 0.002f, "stud not on the 32\" mark: " + pc.GhostPosition + " want " + want);
            Shot(Game.Player.Cam, "smart_01_stud_16oc");

            // cycling offers another fit, and cycling back returns to the best one
            if (pc.SnapCandidateCount > 1)
            {
                pc.CycleSnap(1); yield return Frames(2);
                Assert.Greater(Vector3.Distance(pc.GhostPosition, want), 0.001f, "Q did not change the fit");
                pc.CycleSnap(-1); yield return Frames(2);
                Assert.Less(Vector3.Distance(pc.GhostPosition, want), 0.002f);
            }

            Assert.IsTrue(pc.Place(), "place failed: " + pc.StatusLine);
            yield return Frames(2);

            // modifier: smart snap off gives the plain grid pose (flat board, no target)
            pc.SmartSnapOff = true;
            yield return Frames(2);
            Assert.IsNull(pc.SnapTarget);
            Assert.AreEqual("", pc.SnapLabel);
            Assert.Less(Mathf.Abs(Vector3.Dot(pc.GhostRotation * Vector3.forward, Vector3.up)), 0.1f, "free placement should not stand the stud up");
            Shot(Game.Player.Cam, "smart_05_free_grid");
            pc.SmartSnapOff = false;
            yield return Frames(2);
            Assert.IsNotNull(pc.SnapTarget, "smart snap did not come back");
        }

        [UnityTest]
        public IEnumerator SecondTopPlateSnapsFlush()
        {
            Vector3 c = Site + new Vector3(4f, 0f, 0f);
            Plate(c);
            float studIn = 24f;   // short pony wall so the top plate is visible from eye height
            foreach (float x in new[] { -47.25f, -32f, -16f, 0f, 16f, 32f, 47.25f }) Stud(c, x, studIn);
            var top = Make("2x4x8", 96f, c + Vector3.up * In(1.5f + studIn + 0.75f), AlongX, MemberRole.TopPlate);
            yield return Frames(2);
            Select("2x4x8");

            Vector3 aim = top.transform.position + new Vector3(In(10f), In(0.75f), 0f);
            yield return AimAt(c + new Vector3(In(10f), 0f, -1.6f), aim);
            var pc = Game.Placement;
            Debug.Log("[SmartSnap] cap plate: " + pc.StatusLine);
            Assert.AreEqual(top, pc.SnapTarget, "not snapping to the top plate: " + pc.StatusLine);
            StringAssert.Contains("flush", pc.SnapLabel);
            Vector3 want = top.transform.position + Vector3.up * In(1.5f);
            Assert.Less(Vector3.Distance(pc.GhostPosition, want), 0.002f, "cap plate not flush: " + pc.GhostPosition + " want " + want);
            Assert.Less(Quaternion.Angle(pc.GhostRotation, top.transform.rotation), 0.5f);
            Shot(Game.Player.Cam, "smart_02_double_top_plate");
        }

        [UnityTest]
        public IEnumerator SheathingEdgeLandsOnStudCenter()
        {
            Vector3 c = Site + new Vector3(8f, 0f, 0f);
            Plate(c);
            var studX = new[] { -47.25f, -32f, -16f, 0f, 16f, 32f, 47.25f };
            foreach (float x in studX) Stud(c, x, 92.625f);
            Make("2x4x8", 96f, c + Vector3.up * In(1.5f + 92.625f + 0.75f), AlongX, MemberRole.TopPlate);
            yield return Frames(2);
            Select("osb716");

            Vector3 aim = c + new Vector3(In(2f) * 0f, 1.2f, -In(1.75f));   // exterior face of the middle stud
            yield return AimAt(c + new Vector3(0.3f, 0f, -2.6f), aim);
            var pc = Game.Placement;
            Debug.Log("[SmartSnap] sheet: " + pc.StatusLine);
            Assert.IsNotNull(pc.SnapTarget, "sheet did not find the framing: " + pc.StatusLine);
            Vector3 g = pc.GhostPosition;
            Assert.Greater(Vector3.Dot(pc.GhostRotation * Vector3.up, Vector3.back), 0.99f, "sheet should face out of the wall");
            Assert.AreEqual(-In(1.75f) - In(0.4375f) * 0.5f, g.z - c.z, 0.001f, "sheet not on the stud faces");
            float left = Units.ToInches(g.x - c.x) - 24f, right = left + 48f;
            bool OnLine(float e)
            {
                foreach (float x in studX) if (Mathf.Abs(e - x) < 0.05f) return true;
                return Mathf.Abs(Mathf.Abs(e) - 48f) < 0.05f;   // flush with the wall end
            }
            Assert.IsTrue(OnLine(left) || OnLine(right), "sheet edges at " + left + "\" / " + right + "\" are not on a stud center");
            Assert.AreEqual(0f, g.y - In(48f), 0.002f, "sheet bottom should sit at the bottom of the wall");
            Shot(Game.Player.Cam, "smart_03_sheathing_on_center");
        }

        [UnityTest]
        public IEnumerator BoardSplicesEndToEnd()
        {
            Vector3 c = Site + new Vector3(12f, 0f, 0f);
            var plate = Plate(c);
            yield return Frames(2);
            Select("2x4x8");

            Vector3 aim = c + new Vector3(In(48f + 30f), 0f, 0f);   // ground just past the end, in line
            yield return AimAt(c + new Vector3(In(48f + 30f), 0f, -1.8f), aim);
            var pc = Game.Placement;
            Debug.Log("[SmartSnap] splice: " + pc.StatusLine);
            Assert.AreEqual(plate, pc.SnapTarget, pc.StatusLine);
            StringAssert.Contains("splice", pc.SnapLabel);
            Vector3 want = plate.transform.position + Vector3.right * In(96f);
            Assert.Less(Vector3.Distance(pc.GhostPosition, want), 0.002f, "not spliced end to end: " + pc.GhostPosition + " want " + want);
            Shot(Game.Player.Cam, "smart_04_splice");
        }
    }
}
