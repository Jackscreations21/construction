using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Stairs: stair math, landing + stairs support graph, framing square layout/cut. Screenshots in Logs/shots/.</summary>
    public class StairTests
    {
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

        static Camera Cam(Vector3 pos, Vector3 lookAt, float fov = 50f)
        {
            var cam = new GameObject("StairTestCam").AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.enabled = false;
            return cam;
        }

        [TearDown]
        public void TearDown() { FramingJob.RequireStock = true; }

        // ------------------------------------------------------------------ math

        [Test]
        public void StairMath_36InchRise()
        {
            var r = StairMath.Calculate(36f);
            Assert.AreEqual(5, r.Risers);
            Assert.AreEqual(7.2f, r.UnitRiseIn, 1e-3f);
            Assert.AreEqual("7 3/16\"", StairMath.FormatIn(r.UnitRiseIn)); // 7.2 -> nearest 1/32 = 7 6/32
            Assert.AreEqual(4, r.Treads);
            Assert.AreEqual(10.25f, r.UnitRunIn, 1e-4f);
            Assert.AreEqual(41f, r.TotalRunIn, 1e-3f);
            Assert.IsTrue(r.ThroatOk && r.RiseOk && r.RunOk);
            Assert.AreEqual("pt2x12x12", r.StockId);
        }

        [Test]
        public void StairMath_108InchRise()
        {
            var r = StairMath.Calculate(108f);
            Assert.AreEqual(14, r.Risers);
            Assert.AreEqual(108f / 14f, r.UnitRiseIn, 1e-4f);
            Assert.AreEqual("7 23/32\"", StairMath.FormatIn(r.UnitRiseIn));
            Assert.AreEqual(13, r.Treads);
            Assert.AreEqual(133.25f, r.TotalRunIn, 1e-3f);
            Assert.AreEqual(Mathf.Sqrt(133.25f * 133.25f + 108f * 108f), r.HypotenuseIn, 1e-2f);
            // 13'-10" of board: a 12' stringer is too short
            Assert.Greater(r.StringerLengthIn, 144f);
            Assert.AreEqual("2x12x16", r.StockId);
            // throat = 11.25 - R*T/sqrt(R^2+T^2) = 11.25 - 6.163 = 5.087"
            Assert.AreEqual(5.087f, r.ThroatIn, 0.01f);
            Assert.IsTrue(r.ThroatOk);
        }

        [Test]
        public void StairMath_EdgeCases()
        {
            var r48 = StairMath.Calculate(48f);
            Assert.AreEqual(7, r48.Risers);
            Assert.AreEqual("6 27/32\"", StairMath.FormatIn(r48.UnitRiseIn)); // 48/7 = 6.857 (~6 7/8 to the 1/16)
            Assert.AreEqual(61.5f, r48.TotalRunIn, 1e-3f);
            Assert.AreEqual(1f, r48.NosingIn, 1e-4f);
            // exact multiple of 7-3/4 keeps the count
            var r31 = StairMath.Calculate(31f);
            Assert.AreEqual(4, r31.Risers);
            Assert.AreEqual(7.75f, r31.UnitRiseIn, 1e-4f);
            Assert.IsTrue(r31.RiseOk);
            // min tread 10": a shorter run is clamped
            Assert.AreEqual(10f, StairMath.Calculate(48f, 9f).UnitRunIn, 1e-4f);
            // max riser at a 10" run: throat 11.25 - 7.75*10/12.65 = 5.12" (just passes)
            var steep = StairMath.Calculate(31f, 10f);
            Assert.AreEqual(5.123f, steep.ThroatIn, 0.01f);
            Assert.IsTrue(steep.ThroatOk);
            // parsing
            Assert.IsTrue(StairMath.TryParseInches("4' 0 1/2\"", out float a)); Assert.AreEqual(48.5f, a, 1e-4f);
            Assert.IsTrue(StairMath.TryParseInches("108", out float b)); Assert.AreEqual(108f, b, 1e-4f);
            Assert.IsTrue(StairMath.TryParseInches("10 1/4", out float c)); Assert.AreEqual(10.25f, c, 1e-4f);
            Assert.IsFalse(StairMath.TryParseInches("abc", out _));
            Assert.IsFalse(StairMath.Calculate(0f).Valid);
            // stringer profile: closed polygon starting at the floor, top seat one rise + tread below the landing
            var prof = StairMath.StringerProfileIn(r48);
            Assert.AreEqual(Vector2.zero, prof[0]);
            float topSeat = prof.Max(p => p.y);
            Assert.AreEqual(6 * r48.UnitRiseIn - 1.5f, topSeat, 1e-3f);
            Assert.IsNotEmpty(r48.HeadroomNote);
        }

        // ------------------------------------------------------------------ landing + stairs

        [UnityTest]
        public IEnumerator LandingAndStairs_GroundedAndPhotographed()
        {
            yield return Frames(5);
            FramingJob.RequireStock = false;
            float H = Units.In(48f);
            var center = new Vector3(-12f, 0f, 12f);
            var landing = StairBuilder.Landing(center, 0f, Units.In(48f), H);
            Assert.Greater(landing.Steps.Count, 10, "landing steps");
            var takeoff = landing.Takeoff();
            Assert.IsTrue(takeoff.ContainsKey("pt4x4x10") && takeoff.ContainsKey("2x8x12") && takeoff.ContainsKey("hanger-2x8") && takeoff.ContainsKey("subfloor34"));
            Assert.AreEqual(8, takeoff["hanger-2x8"], "2 hangers x (2 rims + 2 joists)");
            landing.RunAll();
            Assert.IsTrue(landing.Done, "landing not done");
            var info = landing.GetComponent<StairLanding>();
            Assert.NotNull(info.FrontBeam, "front beam");

            var bottom = StairBuilder.BottomFor(info.FrontFaceGround, 0f, H);
            var stairs = StairBuilder.Stairs(bottom, 0f, H, Units.In(36f), info.FrontBeam.transform);
            var r = StairMath.Calculate(48f);
            var st = stairs.Takeoff();
            Assert.AreEqual(3, st["pt2x12x12"], "three stringers");
            Assert.AreEqual(3, st["1x8x8"], "6 risers from 1x8x8 at 2 per board");
            Assert.AreEqual(3, st["pt2x6x16"], "12 tread boards at 5 per 16'");
            foreach (var s in stairs.Steps) Assert.IsNotNull(s.Tools);
            stairs.RunAll();
            Assert.IsTrue(stairs.Done, "stairs not done");
            yield return Frames(3);

            var members = landing.GetComponentsInChildren<StructuralMember>().Concat(stairs.GetComponentsInChildren<StructuralMember>()).ToList();
            Assert.AreEqual(3, members.Count(m => m.Role == MemberRole.Stringer), "stringers");
            Assert.AreEqual(2 * r.Treads, members.Count(m => m.Role == MemberRole.Tread), "tread boards");
            Assert.AreEqual(r.Treads, members.Count(m => m.Role == MemberRole.Riser), "risers");
            Assert.AreEqual(4, members.Count(m => m.Role == MemberRole.Post), "posts");
            Assert.AreEqual(4, members.Count(m => m.Role == MemberRole.Beam), "beam plies");
            Assert.AreEqual(1, members.Count(m => m.Role == MemberRole.Ledger), "hanger board");
            Assert.GreaterOrEqual(members.Count(m => m.Role == MemberRole.Joist), 2, "joists");

            SupportGraph.Dirty = true;
            var grounded = SupportGraph.Grounded();
            var problems = new List<string>();
            foreach (var m in members)
            {
                var status = SupportGraph.Evaluate(m, grounded);
                if (!grounded.Contains(m) || status == StructuralStatus.Unsupported)
                    problems.Add(m.name + ": " + string.Join("; ", m.Notes) + " supports=[" + string.Join(",", m.SupportedBy.Select(x => x.name)) + "]");
                else if (status != StructuralStatus.Ok) Debug.Log("[StairTests] " + m.name + " " + status + ": " + string.Join("; ", m.Notes));
            }
            Assert.IsEmpty(problems, "unsupported members:\n" + string.Join("\n", problems));
            foreach (var m in members.Where(m => m.Role == MemberRole.Tread || m.Role == MemberRole.Riser))
                Assert.IsTrue(m.SupportedBy.Any(s => s.Role == MemberRole.Stringer), m.name + " not carried by a stringer");

            // uniform rises: every tread top is k*R above the floor, the deck is one more rise above the top tread
            float topTread = members.Where(m => m.Role == MemberRole.Tread).Max(m => m.GetComponent<BoxCollider>().bounds.max.y);
            float deck = members.Where(m => m.Role == MemberRole.Subfloor).Max(m => m.GetComponent<BoxCollider>().bounds.max.y);
            Assert.AreEqual(48f, Units.ToInches(deck), 0.02f, "deck height");
            Assert.AreEqual(r.UnitRiseIn, Units.ToInches(deck - topTread), 0.05f, "top riser height");
            float firstTread = members.Where(m => m.Role == MemberRole.Tread).Min(m => m.GetComponent<BoxCollider>().bounds.max.y);
            Assert.AreEqual(r.UnitRiseIn, Units.ToInches(firstTread), 0.05f, "first riser height");

            Vector3 mid = bottom + new Vector3(0f, 0.6f, 1.0f);
            Shot(Cam(mid + new Vector3(-3.2f, 1.9f, -2.6f), mid + new Vector3(0f, -0.1f, 0.4f)), "stairs_01_overview");
            Shot(Cam(mid + new Vector3(2.6f, 1.2f, -2.4f), mid + new Vector3(0f, 0f, 0.4f)), "stairs_02_overview_right");
            // close-up of the outer stringer's sawtooth under the treads (left side)
            Vector3 left = bottom + new Vector3(-Units.In(18f), 0f, 0f);
            Shot(Cam(left + new Vector3(-1.3f, 0.55f, 0.35f), left + new Vector3(0f, 0.35f, 0.75f), 45f), "stairs_03_stringer_closeup");
            // under the landing: hanger board, stringer connectors, joist hangers
            Vector3 top = info.FrontFaceGround;
            Shot(Cam(top + new Vector3(-0.85f, 0.55f, -0.75f), top + new Vector3(0f, 0.95f, 0.2f), 55f), "stairs_04_top_connection");
            Shot(Cam(center + new Vector3(0.25f, 0.45f, 0.1f), center + new Vector3(-0.4f, 1.05f, 0.55f), 70f), "stairs_05_landing_hangers");

            Object.Destroy(stairs.gameObject);
            Object.Destroy(landing.gameObject);
            yield return null;
        }

        // ------------------------------------------------------------------ framing square

        [UnityTest]
        public IEnumerator FramingSquare_LayoutAndCut()
        {
            yield return Frames(5);
            Assert.NotNull(ToolFactory.Create("framingsquare"), "framing square not registered");
            Assert.IsTrue(ToolFactory.ShopItems.Any(i => i.Id == "framingsquare"), "framing square not in the shop");

            var fsq = new FramingSquare();
            var def = Catalog.Get("pt2x12x12");
            Assert.NotNull(def);
            var pos = new Vector3(-16f, Units.In(0.75f), 3f);
            var board = Piece.Create(def, 0f, pos, Quaternion.Euler(0f, 90f, 0f)); // flat on the ground, length along +X
            yield return null;

            StairCalculator.HasLayout = false;
            Assert.IsFalse(fsq.LayOut(board, out string msg), "layout without a calculator result should fail");
            StairCalculator.UseForLayout(StairMath.Calculate(48f));
            Assert.IsTrue(fsq.LayOut(board, out msg), msg);
            Assert.NotNull(board.GetComponent<StringerLayoutMark>());
            Assert.NotNull(board.transform.Find("StringerLayout"), "pencil lines");
            yield return null;
            Vector3 c = board.transform.position;
            Shot(Cam(c + new Vector3(-0.3f, 0.75f, -0.7f), c + new Vector3(-0.3f, 0f, 0f), 50f), "stairs_06_layout_pencil");

            var saw = Game.Belt.Owned.Find(t => t.Id == "saw");
            Assert.NotNull(saw, "default belt should own a saw");
            Game.Belt.Owned.Remove(saw);
            Assert.IsFalse(fsq.Cut(board, out _, out msg), "cut without a saw should fail");
            Game.Belt.Owned.Add(saw);
            float battery = saw.Battery;
            int scrap = Game.Stock.Scrap.Count;
            Assert.IsTrue(fsq.Cut(board, out var stringer, out msg), msg);
            Assert.NotNull(stringer);
            Assert.Less(saw.Battery, battery, "saw battery not drained");
            Assert.Greater(Game.Stock.Scrap.Count, scrap, "no scrap");
            var sm = stringer.GetComponent<StructuralMember>();
            Assert.NotNull(sm);
            Assert.AreEqual(MemberRole.Stringer, sm.Role);
            Assert.AreEqual(PieceState.Loose, stringer.State);
            Assert.NotNull(stringer.GetComponent<CutStringer>());
            yield return null;
            Assert.IsTrue(board == null, "original board should be gone");
            Assert.IsFalse(fsq.LayOut(stringer, out _), "can't lay out a cut stringer again");
            int visuals = stringer.GetComponentsInChildren<MeshFilter>().Count(f => f.sharedMesh != null && f.sharedMesh.vertexCount > 100);
            Assert.GreaterOrEqual(visuals, 1, "sawtooth mesh");

            Shot(Cam(c + new Vector3(-0.3f, 0.75f, -0.7f), c + new Vector3(-0.3f, 0f, 0f), 50f), "stairs_07_cut_stringer");
            Shot(Cam(c + new Vector3(0.6f, 0.35f, -0.45f), c + new Vector3(0.1f, 0f, 0f), 50f), "stairs_08_cut_closeup");
            Object.Destroy(stringer.gameObject);
            yield return null;
        }
    }
}
