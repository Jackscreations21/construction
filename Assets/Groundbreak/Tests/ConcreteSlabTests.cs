using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Milestone 2: lays out, digs, forms, reinforces, orders, pours, finishes and cures a 12' x 16' slab.
    /// Screenshots go to Logs/shots/2x_*.png.</summary>
    public class ConcreteSlabTests
    {
        static float Ft(float f) => f * Units.Foot;
        static float In(float i) => i * Units.Inch;
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Vector3 pos, Vector3 look, string name)
        {
            var cam = new GameObject("ConcreteShotCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = 60f; cam.nearClipPlane = 0.03f; cam.enabled = false;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt); Object.Destroy(cam.gameObject);
        }

        [UnityTest]
        public IEnumerator SlabFromLayoutToCure()
        {
            yield return Frames(10);
            Assert.NotNull(SiteGround.I, "SiteGround not booted");
            Assert.NotNull(GameClock.I, "GameClock missing");
            GameClock.I.Hours = 7.5f;
            Game.Snap.GridIndex = 3; // 1"
            Game.Wallet.Add(20000, "Test float");

            // Slab: 16' (x) by 12' (z), inside faces of the forms
            Vector3 min = new Vector3(0f, 0f, Ft(8f)), max = new Vector3(Ft(16f), 0f, Ft(20f));
            Vector3 center = (min + max) * 0.5f;

            // ---------- layout: stakes, string, square check, laser, paint
            int a = SiteLayout.AddStake(new Vector3(min.x, 0, min.z)), b = SiteLayout.AddStake(new Vector3(max.x, 0, min.z));
            int c = SiteLayout.AddStake(new Vector3(max.x, 0, max.z)), d = SiteLayout.AddStake(new Vector3(min.x, 0, max.z));
            SiteLayout.AddLine(a, b); SiteLayout.AddLine(b, c); SiteLayout.AddLine(c, d); SiteLayout.AddLine(d, a);
            Assert.IsTrue(SiteLayout.TryDiagonals(out float d1, out float d2, out _), "no 4-stake loop found");
            Assert.AreEqual(20f, d1 / Units.Foot, 0.01f, "16x12 diagonal should be 20' (3-4-5)");
            Assert.AreEqual(d1, d2, In(0.125f), "diagonals not equal: not square");
            SiteLayout.SetLaser(new Vector3(Ft(-3f), 0, Ft(6f)));
            SiteLayout.SetSlabTop(In(4f));
            for (float x = 0; x <= Ft(16f); x += 0.15f)
            {
                SiteLayout.SprayMark(new Vector3(x, 0, min.z - In(14f)), Vector3.up, new Color(0.95f, 0.95f, 0.92f));
                SiteLayout.SprayMark(new Vector3(x, 0, max.z + In(14f)), Vector3.up, new Color(0.95f, 0.95f, 0.92f));
            }
            Debug.Log("[Concrete] " + SiteLayout.SquareReport());
            yield return Frames(2);
            Shot(new Vector3(Ft(8f), 3.2f, Ft(1f)), center, "20_layout_stakes_string");

            // ---------- excavation: strip 4" for the base, 12" wide thickened-edge trench to 12" below grade
            var g = SiteGround.I;
            g.DigRect(min, max, -In(4f));
            float tr = Ft(1f);
            g.DigRect(min, new Vector3(max.x, 0, min.z + tr), -In(12f));
            g.DigRect(new Vector3(min.x, 0, max.z - tr), max, -In(12f));
            g.DigRect(min, new Vector3(min.x + tr, 0, max.z), -In(12f));
            g.DigRect(new Vector3(max.x - tr, 0, min.z), max, -In(12f));
            // also check the hand shovel through the real aim path
            var shovel = new ShovelTool();
            Game.Player.transform.position = new Vector3(Ft(20f), 0, Ft(14f));
            Game.Player.SetLook(0f, 60f);
            g.Rebuild(); yield return new WaitForFixedUpdate(); yield return null;
            Assert.IsTrue(Game.Player.TryAim(out var aimHit, 6f), "player can't aim at the site ground");
            float before = g.HeightAt(aimHit.point);
            shovel.Primary(); shovel.Primary();
            Assert.Less(g.HeightAt(aimHit.point), before - In(1f), "shovel didn't dig");
            // gravel base (inside the trench ring) back up to grade, then compact
            Game.Stock.Add(SiteGround.GravelId, 27 * 3);
            float cf = GravelBase.SpreadRect(new Vector3(min.x + tr, 0, min.z + tr), new Vector3(max.x - tr, 0, max.z - tr), 0f);
            Debug.Log($"[Concrete] gravel placed {cf:0.0} cu ft");
            Assert.Greater(cf, 30f, "gravel not spread");
            for (float x = min.x; x <= max.x; x += 0.25f) for (float z = min.z; z <= max.z; z += 0.25f) g.Compact(new Vector3(x, 0, z), 0.28f);
            var poly = new[] { new Vector2(min.x, min.z), new Vector2(max.x, min.z), new Vector2(max.x, max.z), new Vector2(min.x, max.z) };
            Assert.Greater(g.CompactedFraction(poly), 0.9f, "base not compacted");
            g.Rebuild();
            yield return Frames(2);
            Shot(new Vector3(Ft(8f), 3.0f, Ft(2f)), center, "21_excavated_gravel");

            // ---------- forms: 2x6 boards on edge, stakes 36" O.C., kickers, oil, poly
            Game.Stock.Add("2x6x16", 8); Game.Stock.Add(FormSystem.StakeId, 80); Game.Stock.Add("2x4x8", 4);
            Game.Stock.Add(FormSystem.FormOilId, 2000); Game.Stock.Add(FormSystem.PolyId, 2000);
            string msg = FormSystem.BuildRectangle(min, max);
            Debug.Log("[Concrete] " + msg);
            var o = FormSystem.Active;
            Assert.NotNull(o, "forms not closed: " + msg);
            Assert.AreEqual(4, o.Runs.Count);
            Assert.AreEqual(192f, o.AreaSqFt, 1f, "12x16 = 192 sq ft");
            Assert.Greater(FormSystem.MaxRisk(o, out _), 0.99f, "unstaked forms must be at risk");
            int stakes = FormSystem.AutoStake(o);
            foreach (var r in o.Runs) for (float t = Ft(2f); t < r.Length; t += Ft(4f)) FormSystem.AddBrace(r, t, o);
            float risk = FormSystem.MaxRisk(o, out string why);
            Debug.Log($"[Concrete] stakes {stakes}, risk {risk} {why}");
            Assert.AreEqual(0f, risk, 0.001f, "forms still at blowout risk: " + why);
            Debug.Log("[Concrete] " + FormSystem.Oil(o));
            Debug.Log("[Concrete] " + FormSystem.LayPoly(o));
            float yards = FormSystem.TakeoffYards(o);
            Debug.Log($"[Concrete] takeoff {yards:0.00} yd, order {FormSystem.RecommendedOrder(o):0.00} yd");
            Assert.That(yards, Is.InRange(3.8f, 4.9f), "takeoff for a 4\" slab with 12x12 thickened edges should be ~4.3 yd");

            // ---------- rebar #4 @ 18" O.C. on chairs + 2 footing bars, all tied
            Game.Stock.Add("rebar-4", 24); Game.Stock.Add(RebarSystem.ChairId, 300); Game.Stock.Add("tiewire", 2);
            int bars = RebarSystem.AutoGrid(o, "rebar-4");
            int ties = RebarSystem.TieAllNear(center, 20f);
            Debug.Log($"[Concrete] bars {bars}, crossings {RebarSystem.Crossings.Count}, tied {ties}");
            Assert.Greater(bars, 15); Assert.AreEqual(1f, RebarSystem.TiedFraction, 0.001f, "not all tied");
            yield return Frames(2);
            Shot(new Vector3(Ft(-3f), 2.6f, Ft(4f)), center, "22_forms_rebar");
            Shot(new Vector3(Ft(1.5f), 1.0f, Ft(6.5f)), new Vector3(Ft(4f), -0.1f, Ft(10f)), "22b_forms_rebar_closeup");

            // ---------- order ready-mix through the dispatch API and wait for the truck
            var ord = new ConcreteOrder { Yards = FormSystem.RecommendedOrder(o), Psi = 3000, SlumpIn = 5 };
            int quote = ConcreteDispatch.Quote(ord, out string bd);
            Debug.Log($"[Concrete] quote ${quote}: {bd}");
            Assert.IsNotNull(ConcreteDispatch.Place(ord, ConcreteDispatch.LeadMinutes, out msg), msg);
            ConcreteTime.Skip(ConcreteDispatch.LeadMinutes - 2f);
            float until = Time.time + 40f; bool shot = false;
            while (ord.State != OrderState.OnSite && Time.time < until)
            {
                if (!shot && ord.Truck != null && (ord.Truck.transform.position - center).magnitude < 16f) { Shot(new Vector3(Ft(24f), 2.2f, Ft(-6f)), ord.Truck.transform.position + Vector3.up, "23_truck_backing_in"); shot = true; }
                yield return null;
            }
            Assert.AreEqual(OrderState.OnSite, ord.State, "truck never arrived");
            var truck = ord.Truck;

            // ---------- pour: chute where it reaches, wheelbarrow beyond, vibrate as we go
            var pour = PourSystem.Begin(o);
            until = Time.time + 150f; int frame = 0; bool midShot = false; int barrows = 0;
            while (!pour.Full && ord.YardsLeft > 0.001f && Time.time < until)
            {
                Vector3 p = pour.LowestPoint(truck.ChuteBase, ReadyMixTruck.ChuteReach - 0.3f);
                bool roomHere = o.TopY - SiteGround.GroundY(p) > 0 && (CTool.XZ(p - truck.ChuteBase).magnitude <= ReadyMixTruck.ChuteReach - 0.3f);
                Vector3 far = pour.LowestPoint(center);
                float roomNear = PourRoom(pour, p), roomFar = PourRoom(pour, far);
                if (roomHere && roomNear > In(0.75f) || roomFar <= In(0.75f))
                {
                    truck.AimChute(p);
                    pour.Discharge(truck, p, Time.deltaTime * 2f);
                }
                else if (frame % 6 == 0)
                {
                    float y = truck.Discharge(ChuteTool.BarrowCubicFeet / 27f);
                    pour.AddConcrete(far, y / FormSystem.YardsPerM3, ord); barrows++;
                }
                if (frame % 5 == 0) pour.Apply(FinishOp.Vibrate, p);
                if (!midShot && pour.FillFraction > 0.5f) { Shot(new Vector3(Ft(17f), 2.4f, Ft(2.5f)), center, "24_pouring"); midShot = true; }
                frame++;
                yield return null;
            }
            Debug.Log($"[Concrete] placed {pour.PlacedM3 * FormSystem.YardsPerM3:0.00} yd, fill {pour.FillFraction:P0}, full {pour.Full}, barrows {barrows}, drum left {ord.YardsLeft:0.00}");
            Assert.IsTrue(pour.Full || pour.FillFraction > 0.97f, "forms not filled");

            foreach (var p in pour.WorkPoints(0.3f, true)) pour.Apply(FinishOp.Vibrate, p);
            foreach (var p in pour.WorkPoints(0.4f)) { pour.Apply(FinishOp.Vibrate, p); pour.Apply(FinishOp.Rake, p); }
            for (int pass = 0; pass < 2; pass++) foreach (var p in pour.WorkPoints(0.5f)) pour.Apply(FinishOp.Screed, p); // strike off, then a second pass to fill the lows
            foreach (var p in pour.WorkPoints(0.5f)) pour.Apply(FinishOp.BullFloat, p);
            Game.Stock.Add(PourSystem.AnchorId, 25);
            int anchors = pour.AutoAnchors();
            Debug.Log($"[Concrete] screeded {pour.Screeded:P0}, floated {pour.Floated:P0}, edge vibrated {pour.EdgeVibrated:P0}, anchors {anchors} - {ConcreteDispatch.Release(ord)}");
            Assert.Greater(pour.Screeded, 0.9f); Assert.Greater(pour.Floated, 0.85f); Assert.Greater(anchors, 6);
            yield return Frames(3);
            Shot(new Vector3(Ft(-2.5f), 2.0f, Ft(5f)), center, "25_screeded_wet");

            // ---------- wait out the bleed water, then edge, joint, broom
            ConcreteTime.Skip(PourSystem.BleedEndMinutes + 5f);
            foreach (var p in pour.WorkPoints(0.2f, true)) pour.Apply(FinishOp.Edge, p);
            Assert.AreEqual("", pour.Groove(new Vector3(Ft(8f), 0, Ft(9f)), new Vector3(Ft(8f), 0, Ft(19f))));
            Assert.AreEqual("", pour.Groove(new Vector3(Ft(1f), 0, Ft(14f)), new Vector3(Ft(15f), 0, Ft(14f))));
            pour.JointSpans(out float su, out float sv, out float lim);
            Debug.Log($"[Concrete] panels {su:0.0}' x {sv:0.0}' (limit {lim:0.0}')");
            foreach (var p in pour.WorkPoints(0.35f)) pour.Apply(FinishOp.Broom, p);
            until = Time.time + 5f;
            while (pour.Element == null && Time.time < until) yield return null;
            var el = pour.Element;
            Assert.NotNull(el, "pour never completed: " + string.Join(" | ", pour.StatusLines()));
            Debug.Log("[Concrete] " + el.Cover());
            yield return Frames(3);
            Shot(new Vector3(Ft(-2.5f), 2.2f, Ft(5f)), center, "26_finished_broomed");

            // ---------- cure 7 days, strip forms
            int boardsBefore = Game.Stock.Count("2x6x16");
            Assert.That(FormSystem.Strip(o), Does.StartWith("Too green"), "forms should not strip before 24 h");
            ConcreteTime.Skip(7 * 1440f);
            string strip = FormSystem.Strip(o);
            Debug.Log("[Concrete] " + strip);
            Assert.IsTrue(o.Stripped, strip);
            Assert.Greater(Game.Stock.Count("2x6x16"), boardsBefore, "oiled form boards should come back reusable");
            yield return Frames(5);

            Assert.IsTrue(el.Cured, "not cured after 7 days");
            Assert.Greater(el.Strength01, 0.6f);
            Assert.IsTrue(ConcreteElement.TryGetTop(center, out float topY));
            Assert.AreEqual(o.TopY, topY, 0.001f);
            Assert.AreEqual(el, ConcreteElement.At(center));
            Assert.Greater(el.AnchorBolts.Count, 6);
            Debug.Log("[Concrete] " + el.Summary);
            foreach (var df in el.Defects) Debug.Log("[Concrete] defect: " + df);
            foreach (var df in el.Defects)
            {
                StringAssert.DoesNotContain("BLOWOUT", df); StringAssert.DoesNotContain("Cold joint", df);
                StringAssert.DoesNotContain("cracks", df); StringAssert.DoesNotContain("Honeycomb", df);
            }
            // ray from above must land on the slab top (sill plates snap to it)
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(Physics.Raycast(center + Vector3.up * 3f, Vector3.down, out var hit, 5f));
            Assert.AreEqual(o.TopY, hit.point.y, In(0.25f), "raycast didn't hit the slab top");

            Shot(new Vector3(Ft(-4f), 2.4f, Ft(3f)), center, "27_cured_stripped");
            Shot(new Vector3(Ft(-1.5f), 0.7f, Ft(6f)), new Vector3(Ft(2f), 0.05f, Ft(8.5f)), "28_slab_edge_closeup");
            Shot(new Vector3(Ft(8f), 9f, Ft(14f) - 0.01f), center, "29_slab_top_down");
        }

        static float PourRoom(PourSystem pour, Vector3 p) => pour.TopY - pour.SurfaceY(p);
    }
}
