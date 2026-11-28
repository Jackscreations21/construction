using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Code values and realism (IRC 2021): stair width / handrail / nosing / stringer length, attic R-49, cut stringer pickup.
    /// Exploit fixes live in CodeValuesExploitTests.cs and value/behaviour fixes in CodeValuesFixTests.cs (same partial class).
    /// Screenshot: Logs/shots/codevalues_stair_handrail*.png.</summary>
    public partial class CodeValuesTests
    {
        static IEnumerator CvFrames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void CvShot(Vector3 pos, Vector3 lookAt, string name, float fov = 50f)
        {
            var cam = new GameObject("CodeValuesCam").AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.02f;
            cam.enabled = false;
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
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt); Object.Destroy(cam.gameObject);
        }

        // ------------------------------------------------------------------ stair rules (math)

        [Test]
        public void Stair_RunCappedNosingAndLength()
        {
            // a run deeper than two 2x6 (11 1/4") is capped: never a negative nosing
            var deep = StairMath.Calculate(48f, 12.5f);
            Assert.AreEqual(StairMath.MaxRunIn, deep.UnitRunIn, 1e-4f);
            Assert.IsTrue(deep.RunCapped);
            Assert.GreaterOrEqual(deep.NosingIn, 0f);
            Assert.IsTrue(deep.NosingOk, "an 11 1/4\" tread needs no nosing (R311.7.5.3)");
            // 10 3/4" run under an 11" tread leaves a 1/2" nosing: not code
            Assert.IsFalse(StairMath.Calculate(48f, 10.75f).NosingOk);
            Assert.IsTrue(StairMath.Calculate(48f).NosingOk);
            Assert.IsTrue(StairMath.Calculate(48f).CodeOk);

            // handrail on 4 or more risers
            Assert.IsTrue(StairMath.Calculate(48f).HandrailRequired);
            Assert.IsTrue(StairMath.Calculate(31f).HandrailRequired);   // exactly 4 risers
            Assert.IsFalse(StairMath.Calculate(20f).HandrailRequired);  // 3 risers
            Assert.AreEqual(34f, StairMath.HandrailMinHeightIn); Assert.AreEqual(38f, StairMath.HandrailMaxHeightIn);

            // stringers need a 16' 2x12 at most: a 9' floor-to-floor fits, 11' doesn't (add a landing)
            Assert.IsTrue(StairMath.Calculate(108f).LengthOk);
            var tall = StairMath.Calculate(132f);
            Assert.Greater(tall.StringerLengthIn, 191f);
            Assert.IsFalse(tall.LengthOk);
            Assert.IsFalse(tall.CodeOk);
            StringAssert.Contains("landing", tall.BuildProblem);
            StairCalculator.UseForLayout(tall);
            Assert.IsFalse(StairCalculator.HasLayout, "framing square can't take a layout longer than a 16' board");
            StairCalculator.HasLayout = false;
        }

        [UnityTest]
        public IEnumerator Stair_BuilderRefusesNarrowAndTooLong()
        {
            yield return CvFrames(2);
            var narrow = StairBuilder.Stairs(new Vector3(-30f, 0f, 30f), 0f, Units.In(48f), Units.In(30f));
            Assert.AreEqual(0, narrow.Steps.Count, "30\" stair must be refused");
            StringAssert.Contains("36\"", StairBuilder.LastError);
            StringAssert.Contains("36\"", Game.ToastText);
            var tall = StairBuilder.Stairs(new Vector3(-30f, 0f, 34f), 0f, Units.In(132f), Units.In(36f));
            Assert.AreEqual(0, tall.Steps.Count, "stringer over 16' must be refused");
            StringAssert.Contains("landing", StairBuilder.LastError);
            var ok = StairBuilder.Stairs(new Vector3(-30f, 0f, 38f), 0f, Units.In(48f), Units.In(36f));
            Assert.Greater(ok.Steps.Count, 10);
            Assert.AreEqual("", StairBuilder.LastError);
            // 3 risers: no handrail steps
            var low = StairBuilder.Stairs(new Vector3(-30f, 0f, 42f), 0f, Units.In(20f), Units.In(36f));
            Assert.IsFalse(low.Steps.Any(s => s.Label.StartsWith("Handrail")), "3 risers need no handrail");
            Assert.IsTrue(ok.Steps.Any(s => s.Label.StartsWith("Handrail")), "7 risers need a handrail");
            foreach (var j in new[] { narrow, tall, ok, low }) Object.Destroy(j.gameObject);
            yield return null;
        }

        // ------------------------------------------------------------------ handrail built, inspected, photographed

        [UnityTest]
        public IEnumerator Stair_HandrailBuiltInspectedAndPhotographed()
        {
            yield return CvFrames(3);
            bool req = FramingJob.RequireStock;
            FramingJob.RequireStock = false;
            FramingJob landing = null, stairs = null;
            try
            {
                float H = Units.In(48f);
                var center = new Vector3(-24f, 0f, 24f);
                landing = StairBuilder.Landing(center, 0f, Units.In(48f), H);
                landing.RunAll();
                var info = landing.GetComponent<StairLanding>();
                var bottom = StairBuilder.BottomFor(info.FrontFaceGround, 0f, H);
                stairs = StairBuilder.Stairs(bottom, 0f, H, Units.In(36f), info.FrontBeam.transform);
                var take = stairs.Takeoff();
                Assert.IsTrue(take.ContainsKey(StairBuilder.BracketId) && take.ContainsKey(StairBuilder.BoltId), "brackets + bolts in the takeoff");
                Assert.IsTrue(StairBuilder.RailIds.Any(take.ContainsKey), "handrail stock in the takeoff");
                Assert.IsTrue(take.ContainsKey("pt4x4x10"), "post stock in the takeoff");
                stairs.RunAll();
                Assert.IsTrue(stairs.Done);
                yield return CvFrames(3);

                var asm = stairs.GetComponent<StairAssembly>();
                Assert.NotNull(asm);
                var members = stairs.GetComponentsInChildren<StructuralMember>().ToList();
                var rail = members.Single(m => m.Role == MemberRole.Handrail);
                var posts = members.Where(m => m.Role == MemberRole.Post).ToList();
                Assert.GreaterOrEqual(posts.Count, 2);
                // posts no more than 4' apart (a bracket every 4')
                var zs = posts.Select(p => Units.ToInches(stairs.transform.InverseTransformPoint(p.transform.position).z)).OrderBy(z => z).ToList();
                for (int i = 1; i < zs.Count; i++) Assert.LessOrEqual(zs[i] - zs[i - 1], StairBuilder.PostMaxSpacingIn + 0.01f, "post spacing");

                float top = StairBuilder.HandrailTopAboveNosingIn(asm, rail, out float dia, out float zLo, out float zHi);
                Assert.That(top, Is.InRange(34f, 38f), "handrail height above the nosing line");
                Assert.AreEqual(36f, top, 0.1f);
                Assert.That(dia, Is.InRange(1.25f, 2f), "graspable round rail");
                Assert.LessOrEqual(zLo, 0f, "rail reaches over the bottom riser");
                Assert.GreaterOrEqual(zHi, (asm.Result.Risers - 1) * asm.Result.UnitRunIn - 1f, "rail reaches over the top riser");
                // clearance to the posts' inside face >= 1 1/2"
                float railX = Units.ToInches(stairs.transform.InverseTransformPoint(rail.transform.position).x);
                Assert.GreaterOrEqual(18f - (railX + dia * 0.5f), StairMath.HandrailClearIn - 0.01f, "1 1/2\" hand clearance");

                // support graph: rail on the post brackets, posts on grade / bolted to the stringer
                SupportGraph.Dirty = true;
                var grounded = SupportGraph.Grounded();
                Assert.IsTrue(grounded.Contains(rail), "rail not carried: supports=[" + string.Join(",", rail.SupportedBy.Select(s => s.name)) + "]");
                foreach (var p in posts) Assert.IsTrue(grounded.Contains(p), p.name + " not carried");
                Assert.IsTrue(rail.SupportedBy.Any(s => s.Role == MemberRole.Post), "rail rests on the post brackets");

                var issues = FramingInspection.Run();
                var stairIssues = issues.Where(i => i.Note.Contains("andrail") || i.Note.StartsWith("Stair:")).Select(i => i.Note).ToList();
                Assert.IsEmpty(stairIssues, string.Join("\n", stairIssues));
                foreach (var m in members)
                    Assert.AreNotEqual(StructuralStatus.Unsupported, SupportGraph.Evaluate(m, grounded), m.name + ": " + string.Join("; ", m.Notes));

                // photos: three-quarter view from the bottom right, and a close-up of a post with its bracket
                Vector3 b = bottom;
                Vector3 right = stairs.transform.right, fwd = stairs.transform.forward;
                CvShot(b + right * 2.3f - fwd * 1.9f + Vector3.up * 1.6f, b + right * 0.25f + fwd * 0.85f + Vector3.up * 0.75f, "codevalues_stair_handrail", 55f);
                var midPost = posts.OrderBy(p => Vector3.Distance(p.transform.position, b + fwd * 0.8f)).First();
                Vector3 pp = midPost.transform.position;
                CvShot(pp - right * 0.9f - fwd * 0.55f + Vector3.up * 0.75f, pp - right * 0.06f + Vector3.up * 0.45f, "codevalues_stair_handrail_bracket", 50f);

                // without the handrail the stair fails inspection
                Object.DestroyImmediate(rail.gameObject);
                yield return null;
                issues = FramingInspection.Run();
                Assert.IsTrue(issues.Any(i => i.Note.Contains("no handrail")), "missing handrail must fail the stair inspection");
                Assert.IsFalse(FramingInspection.Passed);
            }
            finally
            {
                FramingJob.RequireStock = req;
                if (stairs != null) Object.Destroy(stairs.gameObject);
                if (landing != null) Object.Destroy(landing.gameObject);
            }
            yield return null;
        }

        /// <summary>Report: blueprint stairs vs the stair rules (36" width is a validator error already; a single flight whose
        /// stringer would need more than a 16' 2x12 is logged so the plan owner can add a landing).</summary>
        [Test]
        public void Stair_BlueprintStairsReport()
        {
            int n = 0, narrow = 0, longFlights = 0;
            foreach (var plan in Blueprints.All)
            {
                var bp = Blueprints.GetBlueprint(plan.Id);
                if (bp == null) continue;
                foreach (var s in bp.Stairs)
                {
                    n++;
                    float rise = s.Split ? s.RiseIn / 2f : s.RiseIn;
                    var r = StairMath.Calculate(rise);
                    if (s.WidthFt * 12f < StairMath.MinWidthIn - 0.01f) { narrow++; Debug.LogWarning($"[CodeValues] {bp.Id} {s.Name}: {s.WidthFt * 12f:0}\" wide < 36\""); }
                    if (!r.LengthOk) { longFlights++; Debug.LogWarning($"[CodeValues] {bp.Id} {s.Name}: {rise:0.#}\" rise in one flight needs a {r.StringerLengthIn:0}\" stringer (> 16')"); }
                }
            }
            Debug.Log($"[CodeValues] blueprint stairs: {n}, narrower than 36\": {narrow}, single flights over a 16' stringer: {longFlights}");
            Assert.AreEqual(0, narrow, "blueprint stairs narrower than 36\"");
        }

        // ------------------------------------------------------------------ attic R-49

        [Test]
        public void Insulation_AtticIsR49()
        {
            Assert.AreEqual(49f, InsulationRules.AtticMinR, 1e-4f, "IRC 2021 Table N1102.1.3, climate zone 4");
            Assert.AreEqual(19.6f, InsulationRules.AtticMinR / InsulationRules.LooseFillRPerInch, 0.01f, "19.6\" of blown fiberglass");
            // a crew/contractor takeoff at R-49 orders more bags than R-38 did
            Assert.Greater(Insulation.BagsFor(1000f, InsulationRules.AtticMinR), Insulation.BagsFor(1000f, 38f));
            Assert.AreEqual(Mathf.CeilToInt(1000f / (40f * 38f / 49f)), Insulation.BagsFor(1000f, 49f));
            Assert.NotNull(Insulation.Product("ins-r49-u15"), "R-49 ceiling batts for sale");
            Assert.AreEqual(49f, Insulation.Product("ins-r49-u23").R);
            Assert.NotNull(Catalog.Get("ins-r49-u23"));
        }

        // ------------------------------------------------------------------ cut stringer pickup

        [UnityTest]
        public IEnumerator Exploit_CutStringerPickupReturnsNoBoard()
        {
            yield return CvFrames(2);
            var def = Catalog.Get("pt2x12x12");
            var board = Piece.Create(def, 0f, new Vector3(-34f, Units.In(0.75f), 6f), Quaternion.Euler(0f, 90f, 0f));
            yield return null;
            var saw = Game.Belt.Owned.Find(t => t.Id == "saw");
            Assert.NotNull(saw, "default belt should own a saw");
            saw.Battery = Mathf.Max(saw.Battery, 50f);
            StairCalculator.UseForLayout(StairMath.Calculate(48f));
            var fsq = new FramingSquare();
            Assert.IsTrue(fsq.LayOut(board, out string msg), msg);
            Assert.IsTrue(fsq.Cut(board, out var stringer, out msg), msg);
            yield return null;
            int stock = Game.Stock.Count("pt2x12x12"), scrap = Game.Stock.Scrap.Count;
            PlaceTool.PickUp(stringer);
            yield return null;
            Assert.IsTrue(stringer == null, "picked-up stringer is gone");
            Assert.AreEqual(stock, Game.Stock.Count("pt2x12x12"), "a cut stringer must not come back as a whole 2x12");
            Assert.AreEqual(scrap, Game.Stock.Scrap.Count, "nor as scrap (its offcuts were scrapped when it was cut)");
            StringAssert.Contains("waste", Game.ToastText);
            // a plain full board still goes back into stock
            var plain = Piece.Create(def, 0f, new Vector3(-34f, Units.In(0.75f), 9f), Quaternion.Euler(0f, 90f, 0f));
            yield return null;
            PlaceTool.PickUp(plain);
            Assert.AreEqual(stock + 1, Game.Stock.Count("pt2x12x12"));
            Game.Stock.TryTake("pt2x12x12", 1);
            StairCalculator.HasLayout = false;
        }
    }
}
