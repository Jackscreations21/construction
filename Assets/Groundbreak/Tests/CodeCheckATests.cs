// Code check A (core and building side): regression tests for the defects fixed in that review. See Docs/CODECHECK-A.md.
using NUnit.Framework;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using UnityEngine.TestTools;
using UnityEngine;

// ===== Core =====

namespace Groundbreak.Tests
{
    /// <summary>Code check (Core group: Core, Audio, Tools, Player, UI, Save, Career, Logistics, Tutorials, SiteLook).</summary>
    public partial class CodeCheckATests
    {
        [Test]
        public void Core_UnitsFormatNoNegativeZero()
        {
            Assert.AreEqual("0\"", Units.Format(-0.0001f), "a tiny negative that rounds to 0 shows 0\", not -0\"");
            Assert.AreEqual("-1 1/2\"", Units.Format(-Units.In(1.5f)));
            Assert.AreEqual("7' 8 5/8\"", Units.Format(Units.In(92.625f)));
        }

        static DeliveryService.PendingDelivery Core_FindPending(string id)
        {
            foreach (var d in DeliveryService.I.Pending) if (d.Items.ContainsKey(id)) return d;
            return null;
        }

        /// <summary>Orders are paid when placed, so a delivery still on the road must be saved; and loading must cancel the
        /// trucks of the session being left (they used to keep running and deliver into the loaded game).</summary>
        [UnityTest]
        public IEnumerator Core_PendingDeliverySurvivesSaveLoadOnce()
        {
            for (int i = 0; i < 3; i++) yield return null;
            Assert.NotNull(DeliveryService.I, "delivery service booted");
            const string id = "rebar-3";
            const int qty = 3;
            int stockBefore = Game.Stock.Count(id);
            int pendingBefore = DeliveryService.I.Pending.Count;

            DeliveryService.I.Order(new Dictionary<string, int> { { id, qty } }, true);
            Assert.NotNull(Core_FindPending(id), "order scheduled");

            var file = SaveSystem.Capture();
            bool saved = false;
            foreach (var kv in file.ext) if (kv.key == "deliveries" && kv.value != null && kv.value.Contains(id)) saved = true;
            Assert.IsTrue(saved, "pending delivery is in the save file");

            SaveSystem.Apply(file);
            yield return null;
            Assert.AreEqual(pendingBefore + 1, DeliveryService.I.Pending.Count, "restored once: the pre-load delivery was cancelled, not duplicated");
            var d = Core_FindPending(id);
            Assert.NotNull(d, "delivery restored after load");
            Assert.Greater(d.ETA, 10f, "ETA carried over");

            // let it arrive: exactly one load lands in stock
            bool shot = false;
            float until = Time.time + 45f;
            while (Time.time < until && Game.Stock.Count(id) < stockBefore + qty)
            {
                if (!shot && d.TruckOnSite && d.ETA < 0.5f)
                {
                    var drop = DeliveryService.I.DropPoint;
                    SiteLookTests.Shot(drop + new Vector3(-9f, 4f, -9f), drop + Vector3.up, "codecheck_core_restored_delivery");
                    shot = true;
                }
                yield return null;
            }
            Assert.AreEqual(stockBefore + qty, Game.Stock.Count(id), "delivered once after load");
            for (int i = 0; i < 120; i++) yield return null; // a cancelled pre-load truck would have unloaded by now too
            Assert.AreEqual(stockBefore + qty, Game.Stock.Count(id), "no second delivery from the cancelled pre-load order");

            Game.Stock.TryTake(id, qty); // cleanup; the truck drives off and destroys itself
        }
    }
}

// ===== Visuals =====

namespace Groundbreak.Tests
{
    /// <summary>Code check (Visuals group: Visuals, Building, Stairs): mesh winding, IRC stair constants, stringer solid.</summary>
    public partial class CodeCheckATests
    {
        /// <summary>Every triangle's front face (Unity: Cross(b-a, c-a)) must agree with its vertex normals; optionally also face away from a center (convex shapes).</summary>
        static void Visuals_AssertWinding(Mesh mesh, Vector3? convexCenter)
        {
            var v = mesh.vertices; var nrm = mesh.normals;
            int tris = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    Vector3 g = Vector3.Cross(b - a, c - a);
                    if (g.sqrMagnitude < 1e-14f) continue;
                    tris++;
                    Vector3 vn = nrm[t[i]] + nrm[t[i + 1]] + nrm[t[i + 2]];
                    Assert.Greater(Vector3.Dot(g, vn), 0f, "triangle winding disagrees with its normals in " + mesh.name);
                    if (convexCenter.HasValue)
                        Assert.Greater(Vector3.Dot(g, (a + b + c) / 3f - convexCenter.Value), 0f, "face points inward in " + mesh.name);
                }
            }
            Assert.Greater(tris, 0, "mesh has no triangles");
        }

        [Test]
        public void Visuals_MeshBuilder_BoxAndCylinderFaceOutward()
        {
            var b = new MeshBuilder();
            b.AddBox(Vector3.zero, new Vector3(0.1f, 0.2f, 0.3f), Quaternion.Euler(10f, 20f, 30f), 0, Vector3.forward);
            var box = b.Build("Visuals_Box");
            var c = new MeshBuilder();
            c.AddCylinder(new Vector3(1f, 0.5f, -2f), Quaternion.Euler(0f, 0f, 90f), 0.05f, 0.4f, 16, 0);
            var cyl = c.Build("Visuals_Cyl");
            try
            {
                Visuals_AssertWinding(box, Vector3.zero);
                Visuals_AssertWinding(cyl, new Vector3(1f, 0.5f, -2f));
            }
            finally { Object.Destroy(box); Object.Destroy(cyl); }
        }

        [Test]
        public void Visuals_StairMath_IrcConstantsAndLayout()
        {
            // IRC R311.7: 7 3/4" max riser, 10" min tread, 36" min width, 6' 8" headroom; 5" throat on a cut 2x12 (AWC DCA 6)
            Assert.AreEqual(7.75f, StairMath.MaxRiserIn, 1e-4f);
            Assert.AreEqual(10f, StairMath.MinRunIn, 1e-4f);
            Assert.AreEqual(36f, StairMath.MinWidthIn, 1e-4f);
            Assert.AreEqual(80f, StairMath.MinHeadroomIn, 1e-4f);
            Assert.AreEqual(5f, StairMath.MinThroatIn, 1e-4f);
            Assert.AreEqual(11.25f, StairMath.StringerBoardIn, 1e-4f);

            var r = StairMath.Calculate(105f); // 8' 9" floor to floor
            Assert.IsTrue(r.CodeOk);
            Assert.AreEqual(14, r.Risers);
            Assert.AreEqual(7.5f, r.UnitRiseIn, 1e-4f);
            Assert.AreEqual(13, r.Treads);
            Assert.AreEqual(13f * StairMath.DefaultRunIn, r.TotalRunIn, 1e-3f);
            // R311.7.5.3: 3/4"..1 1/4" nosing where the tread depth is under 11"
            Assert.That(r.NosingIn, Is.InRange(0.75f, 1.25f));
            Assert.Greater(StringerMesh.SignedArea(StairMath.StringerProfileIn(r)), 0f, "stringer profile must be counter-clockwise");
            var lay = new StringerLayout(r);
            Assert.AreEqual(r.StringerLengthIn, lay.LengthIn, 1e-3f);
        }

        [UnityTest]
        public IEnumerator Visuals_StringerSolid_WindingAndShot()
        {
            yield return null;
            var r = StairMath.Calculate(36f);
            var lay = new StringerLayout(r);
            var b = new MeshBuilder { MinSubmeshCount = 2 };
            System.Func<Vector2, Vector3> toLocal = p => new Vector3(p.x * Units.Inch, p.y * Units.Inch, 0f);
            StringerMesh.Solid(b, lay, toLocal, Vector3.forward, 1.5f * Units.Inch, Vector3.right, 0, 1);
            var mesh = b.Build("Visuals_Stringer");
            var go = new GameObject("Visuals_StringerShot");
            try
            {
                Visuals_AssertWinding(mesh, null);
                var origin = new Vector3(300f, 40f, 300f);
                go.transform.position = origin;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { Materials.Get(SurfaceKind.TreatedLumber), Materials.Get(SurfaceKind.PineEndGrain) };
                yield return null;
                Vector3 mid = origin + mesh.bounds.center;
                SiteLookTests.Shot(mid + new Vector3(0.6f, 0.5f, -1.6f), mid, "codecheck_visuals_stringer");
            }
            finally { Object.Destroy(go); Object.Destroy(mesh); }
        }
    }
}

// ===== Concrete =====

namespace Groundbreak.Tests
{
    /// <summary>Code check (Concrete group: Concrete, Inspection, Insulation): pour/strip robustness, stock not lost or
    /// handed out for free by the insulation tools.</summary>
    public partial class CodeCheckATests
    {
        static readonly Vector3 Concrete_Far = new Vector3(300f, 50f, 300f);   // well away from the site pad and the house

        static FramePlane Concrete_Plane(Vector3 n, Vector3 u, Vector3 v, Rect bounds)
        {
            var fp = new FramePlane { N = n, U = u, V = v, Origin = Concrete_Far, Bounds = bounds };
            fp.FaceOffset = Vector3.Dot(fp.Origin, n);
            return fp;
        }

        static int Concrete_Drain(string id) { int n = Game.Stock.Count(id); if (n > 0) Game.Stock.TryTake(id, n); return n; }

        /// <summary>Set slab: the chute must not pull concrete out of the drum; a form board pulled with the pry bar must not
        /// break stripping; a destroyed pour leaves PourSystem.All.</summary>
        [UnityTest]
        public IEnumerator Concrete_SetSlabDischargeStripAndDestroy()
        {
            yield return null;
            var o = new FormOutline { TopY = 4f * Units.Inch, Closed = true, Root = new GameObject("Concrete_TestForms") };
            Vector3 c0 = new Vector3(200f, 0f, 200f);
            o.Corners.Add(c0); o.Corners.Add(c0 + new Vector3(2f, 0, 0)); o.Corners.Add(c0 + new Vector3(2f, 0, 2f)); o.Corners.Add(c0 + new Vector3(0, 0, 2f));
            for (int i = 0; i < 4; i++)
            {
                var r = new FormRun { A = o.Corners[i], B = o.Corners[(i + 1) % 4] };
                Vector3 d = r.B - r.A; r.Length = d.magnitude; r.Dir = d / r.Length;
                r.Out = new Vector3(r.Dir.z, 0, -r.Dir.x);
                o.Runs.Add(r);
            }
            var board = Piece.Create(Catalog.Get("2x6x16"), 2f, o.Runs[0].PointAt(1f), Quaternion.LookRotation(o.Runs[0].Dir));
            board.transform.SetParent(o.Root.transform, true);
            o.Runs[0].Boards.Add(board);

            var pour = PourSystem.Begin(o);
            var el = pour.Complete();
            Assert.NotNull(el, "pour did not set");

            var truckGo = new GameObject("Concrete_TestTruck");
            var truck = truckGo.AddComponent<ReadyMixTruck>();
            truck.Order = new ConcreteOrder { Yards = 3f, YardsLeft = 3f, State = OrderState.OnSite };
            float got = pour.Discharge(truck, c0 + new Vector3(1f, 0, 1f), 1f);
            Assert.AreEqual(0f, got, 1e-6f, "chute discharged into a slab that has already set");
            Assert.AreEqual(3f, truck.Order.YardsLeft, 1e-6f, "concrete vanished from the drum");

            // pry a form board off, then strip once the slab is a day old
            Object.Destroy(board.gameObject);
            yield return null;
            el.PouredAt -= 25f * 60f;
            string msg = null;
            Assert.DoesNotThrow(() => msg = FormSystem.Strip(o), "stripping with a pried-off board threw");
            Assert.IsTrue(o.Stripped, "forms not stripped: " + msg);

            Object.Destroy(truckGo);
            Object.Destroy(o.Root);
            Object.Destroy(pour.gameObject);
            yield return null;
            foreach (var ps in PourSystem.All) Assert.IsFalse(ReferenceEquals(ps, pour), "destroyed pour still in PourSystem.All");
        }

        /// <summary>A roll that runs out mid-bay keeps what was already unrolled (stock isn't silently lost).</summary>
        [UnityTest]
        public IEnumerator Concrete_InsulationRollPartialTakeKeepsRoll()
        {
            yield return null;
            const string id = "ins-r38-u15";
            var p = Insulation.Product(id);
            var field = typeof(Insulation).GetField("_rollLeft", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field, "_rollLeft field missing");
            var dict = (Dictionary<string, float>)field.GetValue(null);
            bool hadPrior = dict.TryGetValue(id, out float prior);
            bool unlimited = FinishStock.Unlimited;
            int saved = Concrete_Drain(id);
            try
            {
                FinishStock.Unlimited = false;
                dict.Remove(id);
                Game.Stock.Add(id, 1);
                var fp = Concrete_Plane(Vector3.back, Vector3.right, Vector3.up, new Rect(0f, 0f, 0.4f, 9.4f));
                var bay = new FramePlane.Bay { R = new Rect(0f, 0f, Units.In(14.5f), 9.2f), Depth = Units.In(11.25f) };   // 30' joist bay > one 25' roll
                var b = Insulation.FitBatt(fp, bay, id);
                Assert.IsNull(b, "fit should fail with only one roll for a 30' bay");
                Assert.AreEqual(0, Game.Stock.Count(id), "the one roll should be on the bay (unrolled)");
                Assert.IsTrue(dict.TryGetValue(id, out float left), "unrolled length was thrown away");
                Assert.AreEqual(p.Length, left, 1e-4f, "unrolled length was thrown away");
            }
            finally
            {
                FinishStock.Unlimited = unlimited;
                if (hadPrior) dict[id] = prior; else dict.Remove(id);
                Concrete_Drain(id); Game.Stock.Add(id, saved);
            }
        }

        /// <summary>Out of staples: poly is given back instead of being consumed for nothing.</summary>
        [UnityTest]
        public IEnumerator Concrete_InsulationPolyRefundedWithoutStaples()
        {
            yield return null;
            bool unlimited = FinishStock.Unlimited;
            int staples = Concrete_Drain(Insulation.StaplesId);
            int poly = Concrete_Drain(Insulation.PolyId);
            try
            {
                FinishStock.Unlimited = false;
                Game.Stock.Add(Insulation.PolyId, 500);
                var fp = Concrete_Plane(Vector3.back, Vector3.right, Vector3.up, new Rect(0f, 0f, 2f, 2.4f));
                Assert.IsNull(Insulation.InstallPoly(fp), "poly installed without staples");
                Assert.AreEqual(500, Game.Stock.Count(Insulation.PolyId), "poly consumed although nothing was installed");
            }
            finally
            {
                FinishStock.Unlimited = unlimited;
                Concrete_Drain(Insulation.PolyId); Game.Stock.Add(Insulation.PolyId, poly);
                Game.Stock.Add(Insulation.StaplesId, staples);
            }
        }

        /// <summary>The blower lays no loose fill when there are no bags on site, and does once a bag is there.</summary>
        [UnityTest]
        public IEnumerator Concrete_BlowerNeedsBags()
        {
            yield return null;
            bool unlimited = FinishStock.Unlimited;
            int bags = Concrete_Drain(Insulation.LooseFillId);
            var fp = Concrete_Plane(Vector3.down, Vector3.right, Vector3.forward, new Rect(0f, 0f, 1.2f, 1.2f));
            var go = new GameObject("Concrete_TestAttic");
            go.transform.SetPositionAndRotation(fp.Origin, fp.Rotation);
            var af = go.AddComponent<AtticFill>();
            af.Init(fp);
            try
            {
                FinishStock.Unlimited = false;
                Vector3 at = fp.World(0.6f, 0.6f);
                Assert.IsFalse(af.Blow(at, 0.5f), "blew without bags");
                Assert.AreEqual(0f, af.AverageDepth, 1e-6f, "loose fill appeared without any bags on site");
                Game.Stock.Add(Insulation.LooseFillId, 1);
                Assert.IsTrue(af.Blow(at, 0.5f), "blow failed with a bag on site");
                Assert.Greater(af.AverageDepth, 0f, "no loose fill after blowing");
                Assert.AreEqual(0, Game.Stock.Count(Insulation.LooseFillId), "bag not used");
                af.Rebuild();
                yield return null;
                SiteLookTests.Shot(fp.Origin + new Vector3(0.6f, 0.9f, -0.7f), fp.World(0.6f, 0.6f), "codecheck_concrete_blower");
            }
            finally
            {
                FinishStock.Unlimited = unlimited;
                Object.Destroy(go);
                Concrete_Drain(Insulation.LooseFillId); Game.Stock.Add(Insulation.LooseFillId, bags);
            }
        }
    }
}

// ===== Framing =====

namespace Groundbreak.Tests
{
    /// <summary>Code check (Framing group: Framing, Roofing): door plate cuts, cap-plate ghost, product seating, tilt-up coroutine.</summary>
    public partial class CodeCheckATests
    {
        /// <summary>Destroys every structural member / chalk line created since the snapshot (braces are unparented from the wall).</summary>
        static void Framing_Cleanup(HashSet<StructuralMember> before, int chalkBefore, params GameObject[] extra)
        {
            foreach (var m in new List<StructuralMember>(SupportGraph.Members))
                if (m != null && !before.Contains(m)) Object.Destroy(m.transform.root.gameObject);
            for (int i = ChalkLine.Lines.Count - 1; i >= chalkBefore && i >= 0; i--)
            {
                if (ChalkLine.Lines[i] != null) Object.Destroy(ChalkLine.Lines[i]);
                ChalkLine.Lines.RemoveAt(i);
            }
            foreach (var g in extra) if (g != null) Object.Destroy(g);
        }

        /// <summary>Two door openings cut in the same frame (crew RunAll) must leave 3 plate pieces, not overlapping re-cuts of the
        /// already-destroyed full plate; and a product seated in the opening gets its StructuralMember (no '??' on GetComponent).</summary>
        [UnityTest]
        public IEnumerator Framing_TwoDoorPlateCutsAndSeat()
        {
            yield return null;
            bool req = FramingJob.RequireStock, inst = FramingJobRunner.Instant;
            FramingJob.RequireStock = false; FramingJobRunner.Instant = true;
            var before = new HashSet<StructuralMember>(SupportGraph.Members);
            int chalk = ChalkLine.Lines.Count;
            var door = Catalog.Get("door-ext-steel36");
            Assert.IsNotNull(door, "door product in catalog");
            var spec = new WallSpec { Name = "CC two-door wall", LengthIn = 192f };   // 16' plates: one stock length
            spec.Openings.Add(new WallOpening { Product = door, CenterIn = 60f });
            spec.Openings.Add(new WallOpening { Product = door, CenterIn = 140f });
            var w = WallPlan.Create(spec, new Vector3(70f, 0f, -70f), Quaternion.identity);
            Piece unit = null;
            try
            {
                var job = WallPlan.Frame(w);
                job.RunAll();
                Assert.IsTrue(job.Done);

                float sum = 0f; int pieces = 0;
                foreach (var m in w.GetComponentsInChildren<StructuralMember>())
                    if (m.Role == MemberRole.BottomPlate && m.enabled) { sum += m.Piece.Size.z; pieces++; }
                float expect = Units.In(192f - 2f * spec.Openings[0].RoughWidthIn);
                Assert.AreEqual(3, pieces, "bottom plate cut out of both door openings");
                Assert.AreEqual(expect, sum, 0.002f, "plate pieces add up to the wall minus both rough openings");

                Assert.AreEqual(2, w.Openings.Count, "rough openings registered");
                var ro = w.Openings[0];
                unit = Piece.Create(door, 0f, ro.Center + ro.Outward, Quaternion.identity);
                ro.Seat(unit);
                var sm = unit.GetComponent<StructuralMember>();
                Assert.IsTrue(sm != null, "seated unit gets a StructuralMember");
                Assert.AreEqual(MemberRole.Product, sm.Role);
                Assert.IsTrue(sm.Has(Connector.Flashed));
            }
            finally
            {
                FramingJob.RequireStock = req; FramingJobRunner.Instant = inst;
            }
            yield return null;
            SiteLookTests.Shot(w.transform.TransformPoint(new Vector3(3f, 2.2f, 4.5f)), w.transform.TransformPoint(new Vector3(3f, 0.6f, 0f)), "codecheck_framing_two_door_plate");
            Framing_Cleanup(before, chalk, w.gameObject, unit != null ? unit.gameObject : null);
            yield return null;
        }

        /// <summary>The cap-plate layout ghost must run along its wall (it used to be turned 90 degrees, length across the wall depth).</summary>
        [UnityTest]
        public IEnumerator Framing_CapPlateGhostRunsAlongWall()
        {
            yield return null;
            var fp = Footprint.Make(new Vector3(-70f, 0f, 70f), 30f, 16, 12);
            var walls = WallPlan.ExteriorWalls(fp);
            var job = WallPlan.CapPlates(walls);
            try
            {
                Assert.AreEqual(4, job.Steps.Count);
                for (int i = 0; i < 4; i++)
                {
                    var g = job.Steps[i].Ghost();
                    Vector3 longAxis = g.rot * Vector3.right;   // the cube's X carries size.x (the plate length)
                    Assert.Greater(g.size.x, g.size.z, "size.x is the plate length");
                    Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(longAxis, walls[i].transform.right)), 0.001f, "cap plate ghost of " + walls[i].Spec.Name + " runs along the wall");
                }
            }
            finally
            {
                Object.Destroy(job.gameObject);
                foreach (var w in walls) Object.Destroy(w.gameObject);
            }
            yield return null;
        }

        /// <summary>The tilt-up coroutine runs on FramingJobRunner: destroying the wall mid-raise must end it, not throw.</summary>
        [Test]
        public void Framing_TiltUpStopsWhenWallDestroyed()
        {
            var w = WallPlan.Create(new WallSpec { Name = "CC tilt wall", LengthIn = 96f }, new Vector3(-70f, 0f, -70f), Quaternion.identity);
            var it = w.TiltUpRoutine(2f);
            Assert.IsTrue(it.MoveNext(), "first raise frame");
            Object.DestroyImmediate(w.gameObject);
            bool more = true;
            Assert.DoesNotThrow(() => more = it.MoveNext());
            Assert.IsFalse(more, "routine ends once the wall is gone");
        }
    }
}

// ===== Finish =====

namespace Groundbreak.Tests
{
    /// <summary>Code check (Finish group: Finish, Drywall).</summary>
    public partial class CodeCheckATests
    {
        /// <summary>A bare 4'x4' wall face (facing -Z) far from the bootstrap site, with one 1/2" sheet hung on it.</summary>
        static DrywallSurface Finish_TestWall(Vector3 origin)
        {
            var fp = new FramePlane
            {
                N = Vector3.back, V = Vector3.up, U = Vector3.right, Origin = origin,
                FaceOffset = Vector3.Dot(origin, Vector3.back), Bounds = new Rect(0f, 0f, Units.In(48f), Units.In(48f)),
            };
            var s = Drywall.SurfaceFor(fp, true);
            s.AddSheet(new DrywallSheet { P = Drywall.Product("dw-12-4x8"), R = fp.Bounds, TaperLo = true, TaperHi = true });
            s.Refresh();
            return s;
        }

        static readonly FieldInfo Finish_PaintGal = typeof(PaintJob).GetField("_paintGal", BindingFlags.NonPublic | BindingFlags.Static);
        static readonly FieldInfo Finish_MudLeft = typeof(Drywall).GetField("_mudLeft", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>Paint/primer and spray texture used to be applied first and "consumed" after (failing silently), so with
        /// nothing on site the wall still got painted/textured for free (and the crew's "need paint" check never fired).</summary>
        [UnityTest]
        public IEnumerator Finish_PaintAndTextureNeedStockOnSite()
        {
            yield return null;
            bool unl = FinishStock.Unlimited; FinishStock.Unlimited = false;
            Assert.NotNull(Finish_PaintGal, "PaintJob._paintGal"); Assert.NotNull(Finish_MudLeft, "Drywall._mudLeft");
            float galWas = (float)Finish_PaintGal.GetValue(null);
            var mud = (Dictionary<string, float>)Finish_MudLeft.GetValue(null);
            bool hadMud = mud.TryGetValue(Drywall.TextureMudId, out float mudWas);
            int paintStock = Game.Stock.Count(PaintJob.PaintId), mudStock = Game.Stock.Count(Drywall.TextureMudId);
            if (paintStock > 0) Game.Stock.TryTake(PaintJob.PaintId, paintStock);
            if (mudStock > 0) Game.Stock.TryTake(Drywall.TextureMudId, mudStock);
            Finish_PaintGal.SetValue(null, 0f); mud.Remove(Drywall.TextureMudId);
            var s = Finish_TestWall(new Vector3(200f, 0f, 50f));
            try
            {
                Assert.Greater(s.VisibleSqFt, 15f, "test sheet visible");
                Assert.IsTrue(Drywall.AllSanded(s));

                Assert.IsFalse(Drywall.ApplyTexture(s, "orange-peel"), "no texture mud on site");
                Assert.IsNull(s.TextureKind, "no texture without mud");

                Assert.AreEqual(0, PaintJob.Paint(s, PaintColors.Get("Sage Green"), Sheen.Eggshell), "no paint on site");
                Assert.AreEqual(0f, s.Coverage(false, 1), "nothing painted without paint");

                Game.Stock.Add(PaintJob.PaintId, 1);
                Assert.Greater(PaintJob.Paint(s, PaintColors.Get("Sage Green"), Sheen.Eggshell), 0, "with a gallon on site it paints");
                Assert.Greater(s.Coverage(false, 1), 0.95f);
                s.Refresh();
                yield return null;
                SiteLookTests.Shot(new Vector3(200.62f, 0.62f, 48.6f), new Vector3(200.62f, 0.62f, 50f), "codecheck_finish_paint_needs_stock");
            }
            finally
            {
                Object.Destroy(s.gameObject);
                int left = Game.Stock.Count(PaintJob.PaintId); if (left > 0) Game.Stock.TryTake(PaintJob.PaintId, left);
                if (paintStock > 0) Game.Stock.Add(PaintJob.PaintId, paintStock);
                if (mudStock > 0) Game.Stock.Add(Drywall.TextureMudId, mudStock);
                Finish_PaintGal.SetValue(null, galWas);
                if (hadMud) mud[Drywall.TextureMudId] = mudWas; else mud.Remove(Drywall.TextureMudId);
                FinishStock.Unlimited = unl;
            }
            yield return null;
        }

        /// <summary>Drywall.Beads kept beads whose GameObject was destroyed (scene cleanup / torn out), and HasBead still
        /// counted them: a new outside corner at that spot skipped its bead and passed the dw-bead inspection.</summary>
        [UnityTest]
        public IEnumerator Finish_DestroyedCornerBeadDoesNotCount()
        {
            yield return null;
            var s = Finish_TestWall(new Vector3(210f, 0f, 50f));
            var seam = new Seam { Kind = SeamKind.OutsideCorner, P0 = new Vector2(Units.In(48f), 0f), P1 = new Vector2(Units.In(48f), Units.In(48f)), Side = Vector2.left };
            var go = new GameObject("Finish_TestBead");
            var bd = new Drywall.CornerBead { A = s.FrontWorld(seam.P0), B = s.FrontWorld(seam.P1), Go = go };
            Drywall.Beads.Add(bd);
            try
            {
                Assert.IsTrue(Drywall.HasBead(s, seam), "bead present");
                Object.Destroy(go);
                yield return null;
                Assert.IsFalse(Drywall.HasBead(s, seam), "destroyed bead no longer counts");
            }
            finally
            {
                Drywall.Beads.Remove(bd);
                if (go != null) Object.Destroy(go);
                Object.Destroy(s.gameObject);
            }
            yield return null;
        }

        /// <summary>When no trim stick on site fits, the toast must name the first (shortest) fitting stick, not the last.</summary>
        [Test]
        public void Finish_TrimStockSuggestsShortestFittingStick()
        {
            var prof = TrimProfile.Get("crown-458");
            bool unl = FinishStock.Unlimited; FinishStock.Unlimited = false;
            string key = prof.Id + "|" + TrimSpecies.Oak;
            List<float> pileWas = TrimStock.Offcuts.TryGetValue(key, out var pile) ? new List<float>(pile) : null;
            if (pile != null) pile.Clear();
            var stockWas = new Dictionary<string, int>();
            foreach (var ft in TrimStock.Feet(TrimSpecies.Oak))
            {
                string id = TrimStock.StockId(prof, TrimSpecies.Oak, ft);
                int n = Game.Stock.Count(id); stockWas[id] = n; if (n > 0) Game.Stock.TryTake(id, n);
            }
            try
            {
                Assert.IsFalse(TrimStock.CutMany(prof, TrimSpecies.Oak, new[] { Units.In(36f) }));
                StringAssert.Contains("order 8' sticks", Game.ToastText);
            }
            finally
            {
                foreach (var kv in stockWas) if (kv.Value > 0) Game.Stock.Add(kv.Key, kv.Value);
                if (pileWas != null) { TrimStock.Offcuts[key].Clear(); TrimStock.Offcuts[key].AddRange(pileWas); }
                FinishStock.Unlimited = unl;
            }
        }

        /// <summary>Stilts lift the eye 18" exactly once (the old check could lift a crouched camera twice a frame).</summary>
        [UnityTest]
        public IEnumerator Finish_StiltsLiftCameraOnce()
        {
            yield return null;
            var rig = Game.Player; Assert.NotNull(rig);
            bool was = Stilts.Worn;
            try
            {
                Stilts.SetWorn(true);
                for (int i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(PlayerRig.EyeHeight + Stilts.Lift, rig.Cam.transform.localPosition.y, 0.03f, "standing eye + 18\"");
                Stilts.SetWorn(false);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.AreEqual(PlayerRig.EyeHeight, rig.Cam.transform.localPosition.y, 0.03f, "stilts off");
            }
            finally { Stilts.Worn = was; }
        }
    }
}

// ===== Exterior =====

namespace Groundbreak.Tests
{
    /// <summary>Code check (Exterior group: Exterior, Landscaping): fiber cement plank layout without stud lines,
    /// rebuilt exterior meshes freed, grade bump volume used by the fill/topsoil/wheelbarrow tools.</summary>
    public partial class CodeCheckATests
    {
        static float Exterior_I(float inches) => Units.In(inches);

        /// <summary>240" stud wall + OSB facing -Z at origin o; returns its ExteriorWall (found through the sheathing).</summary>
        static ExteriorWall Exterior_Wall(Vector3 o)
        {
            FinishTestKit.StudWall(o, Vector3.right, Vector3.forward, 240f);
            FinishTestKit.Sheathing(o, Vector3.right, Vector3.forward, 240f);
            Physics.SyncTransforms();
            var ray = new Ray(o + Vector3.right * Exterior_I(30f) + Vector3.up * Exterior_I(50f) + Vector3.back * 1f, Vector3.forward);
            Assert.IsTrue(Physics.Raycast(ray, out var hit, 2f), "no sheathing hit");
            var fp = FramePlane.FromHit(hit);
            Assert.NotNull(fp, "no frame plane on the sheathing");
            var w = ExteriorWall.For(fp);
            Assert.NotNull(w, "exterior wall");
            return w;
        }

        [UnityTest]
        public IEnumerator Exterior_FiberCementWithoutStudLines_LaysPositivePlanks_AndRebuildFreesMeshes()
        {
            for (int i = 0; i < 5; i++) yield return null;
            FinishStock.Unlimited = true;
            try
            {
                var o = new Vector3(-150f, 0f, 150f);
                var w = Exterior_Wall(o);
                yield return null;
                Assert.IsTrue(HouseWrap.WrapWall(w), "wrap");
                yield return null; // let the parts replaced during wrapping finish destroying
                Assert.AreEqual(1, w.Layer("wrap").childCount, "one live house-wrap part");

                // rebuilt layers must free their old mesh (was leaked on every rebuild / nail / course)
                var oldMesh = w.Layer("wrap").GetChild(0).GetComponent<MeshFilter>().sharedMesh;
                Assert.NotNull(oldMesh);
                HouseWrap.Rebuild(w);
                yield return null;
                Assert.IsTrue(oldMesh == null, "old house-wrap mesh destroyed with its rebuilt part");

                // no stud lines found (FindLast used to return 0 here: negative planks, and an endless loop with none at all)
                w.StudLines.Clear();
                Assert.IsTrue(ExteriorTrim.CornerBoards(w), "corner boards");
                Assert.IsTrue(FiberCement.Starter(w), "starter");
                var c = FiberCement.HangCourse(w);
                Assert.NotNull(c, "course hung");
                Assert.GreaterOrEqual(c.Planks.Count, 2, "a 20' run needs two planks");
                foreach (var p in c.Planks)
                {
                    Assert.Greater(p.U1 - p.U0, Exterior_I(12f), "plank has a real length");
                    Assert.LessOrEqual(p.U1 - p.U0, FiberCement.PlankLen + 1e-4f, "plank no longer than stock");
                }
                for (int i = 0; i + 1 < c.Planks.Count; i++)
                    Assert.Greater(c.Planks[i + 1].U0, c.Planks[i].U0, "planks run left to right");
                yield return null;
                SiteLookTests.Shot(o + new Vector3(Exterior_I(120f), 1.6f, -5f), o + new Vector3(Exterior_I(120f), 0.6f, 0f), "codecheck_exterior_fc_nostuds");
            }
            finally
            {
                FinishStock.Unlimited = false;
            }
            yield return FinishTestKit.CleanScene();
        }

        [Test]
        public void Exterior_GradeRaiseVolume_MatchesBumpFormula()
        {
            var go = new GameObject("Exterior_TestField");
            try
            {
                var f = go.AddComponent<GradeField>();
                f.Init(600f, 600f, 40); // far from the building pad
                float r = 5f, d = 0.1f;
                float vol = f.Raise(new Vector3(610f, 0f, 610f), r, d);
                float expect = LandTool.BumpVolumePerMeter(r) * d;
                Assert.AreEqual(expect, vol, expect * 0.05f, "cosine bump volume (the old pi r^2/2 was 68% too high)");
                Assert.Greater(Mathf.Abs(Mathf.PI * r * r * 0.5f * d - vol), expect * 0.3f, "old cone formula really was off");
            }
            finally
            {
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) Object.Destroy(mf.sharedMesh);
                Object.Destroy(go);
            }
        }
    }
}

// ===== Mep =====

namespace Groundbreak.Tests
{
    /// <summary>Code check (Mep group: Electrical, Plumbing, HVAC, Networks): rebuilt MEP visuals free their meshes,
    /// round ceiling box volume, MEP Plans tablet defers structural button actions.</summary>
    public partial class CodeCheckATests
    {
        static IEnumerator Mep_Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static Piece Mep_Place(string id, Vector3 pos, Quaternion rot)
        {
            var def = Catalog.Get(id);
            Assert.NotNull(def, "missing catalog id " + id);
            var p = Piece.Create(def, 0f, pos, rot);
            MepAutoAttach.AttachNow(p);
            return p;
        }

        static Mesh Mep_ChildMesh(Transform t, string child, string meshName = null)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name != child) continue;
                var mf = c.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (meshName != null && mf.sharedMesh.name != meshName) continue;
                return mf.sharedMesh;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator Mep_RunRebuildAndDestroy_FreeTheirMeshes()
        {
            var spec = RunSpecs.Get(PlumbCatalog.Pvc2);
            Assert.NotNull(spec);
            var run = NetRun.Create(spec, new List<Vector3> { new Vector3(70f, 1f, -70f), new Vector3(71.5f, 1f, -70f) });
            try
            {
                var first = Mep_ChildMesh(run.transform, "Visual");
                Assert.NotNull(first, "run has no visual mesh");
                run.AddStaple(new Vector3(70.7f, 1f, -70f)); // Rebuild
                yield return Mep_Frames(2);
                Assert.IsTrue(first == null, "the old run mesh leaked on Rebuild");
                var second = Mep_ChildMesh(run.transform, "Visual");
                Assert.NotNull(second, "rebuilt run lost its visual");
                Object.Destroy(run.gameObject);
                yield return Mep_Frames(2);
                Assert.IsTrue(second == null, "the run mesh leaked when the run was destroyed");
            }
            finally { if (run != null) Object.Destroy(run.gameObject); }
        }

        static int Mep_RootJoints()
        {
            int n = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.parent == null && t.name == "Joints") n++;
            return n;
        }

        [UnityTest]
        public IEnumerator Mep_DestroyFittingWithMadeJoints_NoOrphanJoints()
        {
            var spec = RunSpecs.Get(PlumbCatalog.Pex12Blue);
            Assert.NotNull(spec);
            int before = Mep_RootJoints();
            var c = new Vector3(76f, 1f, -70f);
            var fit = Fittings.Create("elbow", spec, c, new[] { Vector3.left, Vector3.forward });
            var p0 = fit.Ports[0]; var p1 = fit.Ports[1];
            var r0 = NetRun.Create(spec, new List<Vector3> { p0.WorldPos + Vector3.left * 0.5f, p0.WorldPos }, null, p0);
            var r1 = NetRun.Create(spec, new List<Vector3> { p1.WorldPos, p1.WorldPos + Vector3.forward * 0.5f }, p1, null);
            try
            {
                JoinTool.MakeJoint(p0); JoinTool.MakeJoint(p1);
                Assert.IsFalse(p0.NeedsJoin || p1.NeedsJoin);
                Object.Destroy(fit.gameObject); // detaching the 1st port used to re-spawn "Joints" under the dying fitting
                yield return Mep_Frames(3);
                Assert.AreEqual(before, Mep_RootJoints(), "a crimp-ring 'Joints' object was orphaned at the scene root");
                Assert.IsTrue(r0.FreeEndB && r1.FreeEndA, "runs not detached from the destroyed fitting");
            }
            finally
            {
                if (fit != null) Object.Destroy(fit.gameObject);
                if (r0 != null) Object.Destroy(r0.gameObject);
                if (r1 != null) Object.Destroy(r1.gameObject);
            }
            yield return Mep_Frames(2);
        }

        [UnityTest]
        public IEnumerator Mep_BoxDeviceRebuild_FreesOldMesh()
        {
            bool free = MepStock.Free;
            MepStock.Free = true;
            var p = Mep_Place("el-box-1g", new Vector3(72f, 0.36f, -70f), Quaternion.identity);
            try
            {
                var b = p.GetComponent<ElBox>();
                Assert.NotNull(b, "box did not attach");
                b.MakeUpAll();
                StringAssert.StartsWith("Installed", b.InstallDevice(ElDevice.Receptacle15));
                b.RebuildNow();
                var dev = Mep_ChildMesh(p.transform, "Device");
                Assert.NotNull(dev, "no device mesh");
                b.RebuildNow();
                yield return Mep_Frames(2);
                Assert.IsTrue(dev == null, "the old device mesh leaked on rebuild");
                Assert.NotNull(Mep_ChildMesh(p.transform, "Device"), "device mesh not rebuilt");
            }
            finally { MepStock.Free = free; if (p != null) Object.Destroy(p.gameObject); }
            yield return Mep_Frames(2);
        }

        [UnityTest]
        public IEnumerator Mep_PanelRebuild_FreesBodyMesh_Shot()
        {
            bool free = MepStock.Free;
            MepStock.Free = true;
            var pos = new Vector3(74f, 1.4f, -70f);
            var p = Mep_Place("el-panel200", pos, Quaternion.identity);
            try
            {
                var panel = p.GetComponent<ElPanel>();
                Assert.NotNull(panel, "panel did not attach");
                Assert.NotNull(panel.Install("el-bkr-20-1p-afci", 1), "breaker install failed");
                Assert.NotNull(panel.Install("el-bkr-30-2p", 2), "2-pole install failed");
                yield return Mep_Frames(3);
                var body = Mep_ChildMesh(p.transform, "Visual", "GB_ElPanelBody");
                var wires = Mep_ChildMesh(p.transform, "Wires");
                Assert.NotNull(body, "panel body not built");
                Assert.NotNull(wires, "panel wires (grounding electrode conductor) not built");
                panel.Touch(); // any graph change rebuilds the whole panel look
                yield return Mep_Frames(3);
                Assert.IsTrue(body == null, "the old panel body mesh leaked on rebuild");
                Assert.IsTrue(wires == null, "the old panel wires mesh leaked on rebuild");
                Assert.NotNull(Mep_ChildMesh(p.transform, "Visual", "GB_ElPanelBody"), "panel body not rebuilt");
                SiteLookTests.Shot(pos + new Vector3(0.15f, 0.1f, 0.75f), pos, "codecheck_mep_panel", 50f);
            }
            finally { MepStock.Free = free; if (p != null) Object.Destroy(p.gameObject); }
            yield return Mep_Frames(2);
        }

        [Test]
        public void Mep_BoxVolumes_MatchCatalog()
        {
            var go = new GameObject("Mep_BoxVolume");
            try
            {
                var b = go.AddComponent<ElBox>();
                b.Kind = ElBoxKind.Round;
                Assert.AreEqual(20f, b.VolumeCuIn, 1e-4f, "4\" round ceiling box is sold as 20 cu in");
                b.Kind = ElBoxKind.OneGang;
                Assert.AreEqual(20.3f, b.VolumeCuIn, 1e-4f);
                b.Kind = ElBoxKind.TwoGang;
                Assert.AreEqual(32f, b.VolumeCuIn, 1e-4f);
                // NEC 314.16(B): two 14/2 cables = 4 conductors x 2.0 + grounds 2.0 + device yoke 2 x 2.0 = 14 cu in
                Assert.AreEqual(2.0f, ElBox.ConductorVol(14), 1e-4f);
                Assert.AreEqual(2.25f, ElBox.ConductorVol(12), 1e-4f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Mep_TabletDefer_RunsAfterDrawNotImmediately()
        {
            bool ran = false;
            MepTablet.Defer(() => ran = true);
            Assert.IsFalse(ran, "deferred tablet action ran inside the IMGUI pass");
            // drop the queued test action so it never runs in a later tablet draw
            var f = typeof(MepTablet).GetField("_deferred", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(f);
            f.SetValue(null, null);
        }
    }
}
