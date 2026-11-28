using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Groundbreak.Tests.FinishTestKit;

namespace Groundbreak.Tests
{
    /// <summary>
    /// Save/load gaps round 3 (Docs/SAVEGAPS.md): stair-builder stairs + landing (non-shell framing jobs), ready-mix orders /
    /// trucks and a form outline still being drawn, per-tool state (caulk tube), insulation marks / foam-shim memory / part bag,
    /// hardwood underlayment + countertop overhangs, a stale shell project, and version-3 saves without the new sections.
    /// Each test builds the thing, saves to disk, wipes what a quit would lose, loads and checks it came back the same without
    /// charging stock or money again. Screenshot (Logs/shots): savegap3_stairbuilder_stair.
    /// </summary>
    public class SaveGaps3Tests
    {
        const string Slot = "savegap3_test";
        static float I(float inches) => Units.In(inches);
        static float Ft(float feet) => feet * Units.Foot;

        GameMode _mode; int _money; bool _requireStock, _instant; int _grid; Vector3 _snapO; float _snapYaw;
        readonly List<string> _ownedBefore = new List<string>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(3);
            yield return MepTestScene.Clear();
            _mode = GameSettings.Mode; _money = Game.Wallet.Money;
            _requireStock = FramingJob.RequireStock; _instant = FramingJobRunner.Instant;
            _grid = Game.Snap.GridIndex; _snapO = Game.Snap.Origin; _snapYaw = Game.Snap.OriginYaw;
            _ownedBefore.Clear();
            if (Game.Belt != null) foreach (var t in Game.Belt.Owned) if (t != null) _ownedBefore.Add(t.Id);
            GameSettings.Mode = GameMode.FreeBuild;
            SeasonWeather.Override = null;
            if (GameClock.I != null) GameClock.I.Hours = 10f;
            WipeAll();
            yield return Frames(2);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (SaveSystem.Exists(Slot)) SaveSystem.Delete(Slot);
            WipeAll();
            BoomTruck.RemoveAll();
            yield return MepTestScene.Clear();
            yield return CleanScene();
            // tools a test added to the belt (loads rebuild the belt from the save)
            if (Game.Belt != null)
            {
                var keep = new List<string>(_ownedBefore);
                foreach (var t in new List<Tool>(Game.Belt.Owned))
                {
                    if (t == null) continue;
                    if (keep.Remove(t.Id)) continue;
                    for (int i = 0; i < ToolBelt.SlotCount; i++) if (Game.Belt.Slots[i] == t) Game.Belt.Slots[i] = null;
                    Game.Belt.Owned.Remove(t);
                }
            }
            MepStock.Free = false;
            SeasonWeather.Override = null;
            SeasonRules.ResetState();
            FramingJob.RequireStock = _requireStock; FramingJobRunner.Instant = _instant;
            Game.Snap.GridIndex = _grid; Game.Snap.Origin = _snapO; Game.Snap.OriginYaw = _snapYaw;
            GameSettings.Mode = _mode; Game.Wallet = new Wallet(_money);
            yield return Frames(2);
        }

        /// <summary>What a quit loses: every piece, every job, the shell, concrete work, trucks, drywall / insulation, finish.</summary>
        static void WipeAll()
        {
            SaveSystem.ClearPieces();
            ShellSave.Wipe();
            JobSave.Wipe();
            foreach (var j in new List<FramingJob>(FramingJob.All)) if (j != null) SaveGaps.Kill(j.transform.root.gameObject);
            foreach (var w in Object.FindObjectsByType<WallAssembly>(FindObjectsSortMode.None)) SaveGaps.Kill(w.gameObject);
            foreach (var a in new List<RoofAnchor>(RoofAnchor.All)) if (a != null) SaveGaps.Kill(a.gameObject);
            ConcreteSave.Wipe();
            ReadyMixSave.Wipe();
            foreach (var el in new List<ConcreteElement>(ConcreteElement.All)) if (el != null) { SaveGaps.Kill(el.gameObject); Object.DestroyImmediate(el); }
            DrywallSave.Wipe();
            FinishSave.Wipe();
            var marks = (System.Collections.IList)typeof(Insulation).GetField("_exterior", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null); marks.Clear();
            ((List<Vector3>)typeof(Insulation).GetField("_shimmed", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
        }

        static IEnumerator SaveWipeLoad()
        {
            Assert.IsTrue(SaveSystem.Save(Slot), SaveSystem.LastMessage);
            WipeAll();
            yield return Frames(2);
            Assert.AreEqual(0, ActivePieces(), "pieces wiped");
            Assert.AreEqual(0, Object.FindObjectsByType<JobRecipe>(FindObjectsSortMode.None).Length, "jobs wiped");
            Assert.AreEqual(0, ConcreteDispatch.Orders.Count, "orders wiped");
            Assert.IsTrue(SaveSystem.Load(Slot), SaveSystem.LastMessage);
            yield return Frames(4);
            Physics.SyncTransforms();
        }

        class StockSnap { public Dictionary<string, int> counts = new Dictionary<string, int>(); public int scrap; }

        static StockSnap Snap()
        {
            var s = new StockSnap();
            foreach (var kv in Game.Stock.Entries) if (kv.Value != 0) s.counts[kv.Key] = kv.Value;
            s.scrap = Game.Stock.Scrap.Count;
            return s;
        }

        static void AssertStockSame(StockSnap a, string what)
        {
            var b = Snap();
            var diff = new List<string>();
            foreach (var kv in a.counts) { b.counts.TryGetValue(kv.Key, out int n); if (n != kv.Value) diff.Add($"{kv.Key} {kv.Value}->{n}"); }
            foreach (var kv in b.counts) if (!a.counts.ContainsKey(kv.Key)) diff.Add($"{kv.Key} 0->{kv.Value}");
            Assert.AreEqual(0, diff.Count, what + ": stock changed on load (charged twice?): " + string.Join(", ", diff));
            Assert.AreEqual(a.scrap, b.scrap, what + ": scrap pile changed on load");
        }

        static int DuplicatePieces()
        {
            var seen = new Dictionary<string, List<Vector3>>();
            int dup = 0;
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (p == null || p.Def == null || !p.gameObject.activeInHierarchy) continue;
                if (!seen.TryGetValue(p.Def.Id, out var l)) seen[p.Def.Id] = l = new List<Vector3>();
                foreach (var q in l) if ((q - p.transform.position).sqrMagnitude < 1e-6f) { dup++; break; }
                l.Add(p.transform.position);
            }
            return dup;
        }

        static int ActivePieces() { int n = 0; foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (p != null && p.gameObject.activeInHierarchy) n++; return n; }

        static FramingJob JobTitled(string prefix)
        {
            foreach (var j in FramingJob.All) if (j != null && j.gameObject.activeInHierarchy && j.Title.StartsWith(prefix)) return j;
            return null;
        }

        static int Seats(Component c) { int n = 0; foreach (Transform t in c.transform) if (t.name.StartsWith("SeatContact")) n++; return n; }

        // ================================================================== 1. stair builder stairs + landing

        [UnityTest]
        public IEnumerator StairBuilderStairsAndLanding_SurviveSaveLoad()
        {
            FramingJob.RequireStock = false;
            float H = I(48f);
            var center = new Vector3(-12f, 0f, 12f);
            var landing = StairBuilder.Landing(center, 0f, I(48f), H);
            landing.RunAll();
            var info = landing.GetComponent<StairLanding>();
            Assert.NotNull(info.FrontBeam, "front beam");
            var bottom = StairBuilder.BottomFor(info.FrontFaceGround, 0f, H);
            var stairs = StairBuilder.Stairs(bottom, 0f, H, I(36f), info.FrontBeam.transform);
            stairs.RunAll();
            Assert.IsTrue(landing.Done && stairs.Done);
            // a second, free-standing stair half built: stringers cut and set, no risers / treads yet
            var stairs2 = StairBuilder.Stairs(new Vector3(-16f, 0f, 9f), 90f, I(30f));
            stairs2.ReplayTo(6);
            Assert.AreEqual(6, stairs2.Next);
            // the player pulled one tread board back off
            StructuralMember pulled = null;
            foreach (var m in stairs.GetComponentsInChildren<StructuralMember>()) if (m.name == "Tread 2 front") pulled = m;
            Assert.NotNull(pulled, "tread 2 front");
            SaveGaps.Kill(pulled.gameObject);
            yield return Frames(3);
            Physics.SyncTransforms();

            var stock = Snap();
            var r = StairMath.Calculate(48f);
            int pieces = ActivePieces();
            var cut = new List<(Vector3 pos, int verts, int seats)>();
            foreach (var cs in Object.FindObjectsByType<CutStringer>(FindObjectsSortMode.None))
            {
                var mf = cs.transform.Find("Visual")?.GetComponent<MeshFilter>();
                Assert.NotNull(mf, "stringer visual");
                cut.Add((cs.transform.position, mf.sharedMesh.vertexCount, Seats(cs)));
            }
            Assert.AreEqual(6, cut.Count, "six builder stringers");
            int treads = 0; foreach (var m in stairs.GetComponentsInChildren<StructuralMember>()) if (m.Role == MemberRole.Tread) treads++;
            Assert.AreEqual(2 * r.Treads - 1, treads);
            string t1 = stairs.Title, t2 = stairs2.Title;
            Vector3 frontBeamPos = info.FrontBeam.transform.position;

            yield return SaveWipeLoad();

            var l2 = JobTitled("Stair landing"); var s1 = JobTitled(t1);
            FramingJob s2 = null; foreach (var j in FramingJob.All) if (j != null && j.Title == t2 && j != s1) s2 = j;
            if (t1 == t2) { foreach (var j in FramingJob.All) if (j != null && j.Title == t1 && j.Done) s1 = j; foreach (var j in FramingJob.All) if (j != null && j.Title == t1 && !j.Done) s2 = j; }
            Assert.NotNull(l2, "landing job back"); Assert.NotNull(s1, "stairs job back"); Assert.NotNull(s2, "half-built stairs back");
            Assert.IsTrue(l2.Done && s1.Done, "finished jobs finished");
            Assert.AreEqual(6, s2.Next, "half-built stair at the same step");
            var info2 = l2.GetComponent<StairLanding>();
            Assert.NotNull(info2, "landing info"); Assert.NotNull(info2.FrontBeam, "landing front beam");
            Assert.Less(Vector3.Distance(frontBeamPos, info2.FrontBeam.transform.position), 1e-3f);

            var cut2 = Object.FindObjectsByType<CutStringer>(FindObjectsSortMode.None);
            Assert.AreEqual(cut.Count, cut2.Length, "same stringers (no plain 2x12 doubles)");
            foreach (var c in cut)
            {
                CutStringer hit = null;
                foreach (var cs in cut2) if ((cs.transform.position - c.pos).sqrMagnitude < 1e-6f) hit = cs;
                Assert.NotNull(hit, "stringer back at " + c.pos);
                Assert.AreEqual(c.verts, hit.transform.Find("Visual").GetComponent<MeshFilter>().sharedMesh.vertexCount, "sawtooth mesh, not a box");
                Assert.AreEqual(c.seats, Seats(hit), "seat contact boxes");
                Assert.Greater(Seats(hit), 0);
            }
            int treads2 = 0; foreach (var m in s1.GetComponentsInChildren<StructuralMember>()) if (m.Role == MemberRole.Tread) treads2++;
            Assert.AreEqual(treads, treads2, "the pulled tread stays off");
            Assert.AreEqual(pieces, ActivePieces(), "same number of pieces");
            Assert.AreEqual(0, DuplicatePieces(), "no doubles");
            AssertStockSame(stock, "stairs");

            // the built stair still passes: every member grounded, treads / risers carried by the stringers
            SupportGraph.Dirty = true;
            var members = new List<StructuralMember>(l2.GetComponentsInChildren<StructuralMember>());
            members.AddRange(s1.GetComponentsInChildren<StructuralMember>());
            var grounded = SupportGraph.Grounded();
            var problems = new List<string>();
            foreach (var m in members)
            {
                var status = SupportGraph.Evaluate(m, grounded);
                if (!grounded.Contains(m) || status == StructuralStatus.Unsupported) problems.Add(m.name + ": " + string.Join("; ", m.Notes));
            }
            Assert.IsEmpty(problems, "unsupported after load:\n" + string.Join("\n", problems));
            foreach (var m in members)
                if (m.Role == MemberRole.Tread || m.Role == MemberRole.Riser)
                    Assert.IsTrue(m.SupportedBy.Exists(x => x.Role == MemberRole.Stringer), m.name + " not carried by a stringer");

            if (GameClock.I != null) GameClock.I.Hours = 11f;
            yield return Frames(3);
            var mid = (bottom + center) * 0.5f;
            SiteLookTests.Shot(new Vector3(bottom.x + 2.6f, 1.5f, bottom.z - 1.4f), new Vector3(mid.x, 0.55f, mid.z - 0.3f), "savegap3_stairbuilder_stair", 60f);

            // and the half-built stair can be finished after the load
            s2.RunAll();
            Assert.IsTrue(s2.Done, "finish the half-built stair");
        }

        // ================================================================== 2. ready-mix orders + form outline being drawn

        [UnityTest]
        public IEnumerator ReadyMixOrdersAndFormOutlineBeingDrawn_SurviveSaveLoad()
        {
            Assert.NotNull(SiteGround.I, "site ground");
            Game.Wallet = new Wallet(50000);
            Game.Snap.GridIndex = 3; Game.Snap.Origin = Vector3.zero; Game.Snap.OriginYaw = 0f;
            foreach (var id in new[] { "2x6x12", "2x8x12", "2x10x12", "2x12x12" }) Game.Stock.Add(id, 6);
            float now = ConcreteTime.Now;
            var sched = new ConcreteOrder { Yards = 6.5f, Psi = 4000, SlumpIn = 5, Fiber = true, Retarder = true, Cost = 1290, YardsLeft = 6.5f, State = OrderState.Scheduled, ScheduledFor = now + 600f };
            ConcreteDispatch.Orders.Add(sched);
            var onSite = new ConcreteOrder { Yards = 3f, Psi = 3000, SlumpIn = 4, Pump = true, Cost = 1400, YardsLeft = 1.75f, State = OrderState.EnRoute, ScheduledFor = now - 30f, StandbyCharged = 0 };
            ConcreteDispatch.Orders.Add(onSite);
            var truck = ReadyMixTruck.Dispatch(onSite);
            truck.StopAllCoroutines();
            var truckPos = new Vector3(4f, 0f, -6f); var truckRot = Quaternion.Euler(0f, 30f, 0f);
            truck.transform.SetPositionAndRotation(truckPos, truckRot);
            onSite.Truck = truck; onSite.State = OrderState.OnSite; onSite.ArrivedAt = now - 20f;
            // three corners of a form outline, not closed yet
            var c0 = new Vector3(Ft(-26f), 0f, Ft(52f)); var c1 = new Vector3(Ft(-22f), 0f, Ft(52f)); var c2 = new Vector3(Ft(-22f), 0f, Ft(56f)); var c3 = new Vector3(Ft(-26f), 0f, Ft(56f));
            FormSystem.AddCorner(c0); FormSystem.AddCorner(c1); FormSystem.AddCorner(c2);
            Assert.NotNull(FormSystem.Building);
            var corners = new List<Vector3>(FormSystem.Building.Corners);
            float topY = FormSystem.Building.TopY;
            int markers = FormSystem.Building.Root.transform.childCount;
            yield return Frames(2);

            int money = Game.Wallet.Money;
            var stock = Snap();
            float etaBefore = sched.ScheduledFor - ConcreteTime.Now;

            yield return SaveWipeLoad();

            Assert.AreEqual(money, Game.Wallet.Money, "nothing charged again");
            Assert.AreEqual(2, ConcreteDispatch.Orders.Count, "both orders");
            var s = ConcreteDispatch.Orders.Find(o => o.State == OrderState.Scheduled);
            var o2 = ConcreteDispatch.Orders.Find(o => o.State == OrderState.OnSite);
            Assert.NotNull(s, "scheduled order"); Assert.NotNull(o2, "truck on site");
            Assert.AreEqual(6.5f, s.Yards, 1e-4f); Assert.AreEqual(4000, s.Psi); Assert.AreEqual(5, s.SlumpIn); Assert.IsTrue(s.Fiber && s.Retarder && !s.Accelerator);
            Assert.AreEqual(1290, s.Cost); Assert.AreEqual(6.5f, s.YardsLeft, 1e-4f);
            Assert.AreEqual(etaBefore, s.ScheduledFor - ConcreteTime.Now, 3f, "same time left before it arrives");
            Assert.AreEqual(1.75f, o2.YardsLeft, 1e-4f, "concrete left in the drum"); Assert.IsTrue(o2.Pump);
            Assert.AreEqual(now - 20f, o2.ArrivedAt, 1e-3f, "unload clock kept");
            Assert.NotNull(o2.Truck, "truck parked");
            Assert.Less(Vector3.Distance(truckPos, o2.Truck.transform.position), 1e-3f, "truck where it stood");
            Assert.AreEqual(1, Object.FindObjectsByType<ReadyMixTruck>(FindObjectsSortMode.None).Length, "one truck on site");
            yield return Frames(5);
            Assert.AreEqual(OrderState.OnSite, o2.State, "still on site (not backing in again)");
            Assert.Less(Vector3.Distance(truckPos, o2.Truck.transform.position), 1e-3f, "truck didn't move");

            var b = FormSystem.Building;
            Assert.NotNull(b, "outline being drawn");
            Assert.AreEqual(corners.Count, b.Corners.Count);
            for (int i = 0; i < corners.Count; i++) Assert.Less(Vector3.Distance(corners[i], b.Corners[i]), 1e-4f, "corner " + i);
            Assert.AreEqual(topY, b.TopY, 1e-5f); Assert.AreEqual(markers, b.Root.transform.childCount, "corner stakes + string lines");
            AssertStockSame(stock, "readymix");
            // keep drawing: last corner, then click the first to close
            FormSystem.AddCorner(c3);
            StringAssert.StartsWith("Forms set", FormSystem.AddCorner(c0));
            Assert.IsNull(FormSystem.Building);
            Assert.IsTrue(FormSystem.Active != null && FormSystem.Active.Closed, "closed after the load");
        }

        // ================================================================== 3. per-tool state

        [UnityTest]
        public IEnumerator CaulkTubeAndToolState_SurviveSaveLoad()
        {
            var belt = Game.Belt;
            Assert.NotNull(belt, "tool belt");
            var gun = ToolFactory.Create("caulk-gun") as CaulkGun;
            Assert.NotNull(gun, "caulk gun");
            gun.FeetLeft = 13.5f;
            int tube = CaulkGun.Tubes.Count > 1 ? CaulkGun.Tubes.Count - 1 : 0;
            gun.Mode = tube;
            string tubeName = gun.ModeName;
            belt.Owned.Add(gun);
            var barrow = ToolFactory.Create("wheelbarrow") as WheelbarrowTool;
            if (barrow != null) { barrow.Load = 3; belt.Owned.Add(barrow); }
            var chute = ToolFactory.Create("chute") as ChuteTool;
            if (chute != null) { chute.BarrowYards = 0.08f; belt.Owned.Add(chute); }
            yield return Frames(1);

            yield return SaveWipeLoad();

            CaulkGun gun2 = null; WheelbarrowTool barrow2 = null; ChuteTool chute2 = null;
            foreach (var t in Game.Belt.Owned) { if (t is CaulkGun g) gun2 = g; if (t is WheelbarrowTool w) barrow2 = w; if (t is ChuteTool c) chute2 = c; }
            Assert.NotNull(gun2, "caulk gun back on the belt");
            Assert.AreNotSame(gun, gun2);
            Assert.AreEqual(13.5f, gun2.FeetLeft, 1e-4f, "feet left in the tube");
            Assert.AreEqual(tubeName, gun2.ModeName, "same tube loaded");
            if (barrow != null) { Assert.NotNull(barrow2); Assert.AreEqual(3, barrow2.Load, "wheelbarrow load type"); }
            if (chute != null) { Assert.NotNull(chute2); Assert.AreEqual(0.08f, chute2.BarrowYards, 1e-5f, "concrete in the wheelbarrow"); }
        }

        // ================================================================== 4. insulation marks, foam-shim memory, part bag

        [UnityTest]
        public IEnumerator InsulationMarksShimMemoryAndPartBag_SurviveSaveLoad()
        {
            FinishStock.Unlimited = true;
            var O = new Vector3(60f, I(0.75f) + 0.002f, 140f);
            Vector3 In(float x, float y, float z) => O + new Vector3(I(x), I(y), -I(z));
            StudWall(O, Vector3.right, Vector3.back, 144f, new Opening { LeftIn = 88f, WidthIn = 38f, SillIn = 0f, HeadIn = 82.5f });
            Sheathing(O, Vector3.right, Vector3.back, 144f);
            CeilingJoists(O, Vector3.right, Vector3.back, 144f, 144f);
            Subfloor(O + Vector3.back * I(144f) + Vector3.down * 0.0005f, 144f, 144f);
            yield return Frames(3);
            Physics.SyncTransforms();
            var wall = Insulation.FaceFrom(In(32f, 48f, 30f), Vector3.forward);
            var ceil = Insulation.FaceFrom(In(32f, 60f, 40f), Vector3.up);
            Assert.NotNull(wall); Assert.NotNull(ceil);
            Assert.IsTrue(Insulation.IsExterior(wall), "sheathed wall reads exterior");
            Insulation.MarkExterior(wall, false);   // the player marked it as an interior wall
            Assert.IsFalse(Insulation.IsExterior(wall));
            var shimPoint = wall.World(new Vector2(wall.Bounds.xMin + I(107f), wall.Bounds.yMin + I(41f)), 0f);
            var shimmed = (List<Vector3>)typeof(Insulation).GetField("_shimmed", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            shimmed.Add(shimPoint);
            Assert.NotNull(Drywall.HangSurface(ceil, "dw-12-4x12"));
            var af = Insulation.BlowAttic(ceil, 38f);
            Assert.NotNull(af, "attic fill: " + Game.ToastText);
            Assert.IsTrue(af.Blow(ceil.World(ceil.Bounds.center, 0f), 0.05f), "a short blow");
            var volField = typeof(AtticFill).GetField("_volLeft", BindingFlags.Instance | BindingFlags.NonPublic);
            float volLeft = (float)volField.GetValue(af);
            Assert.Greater(volLeft, 0f, "part of a bag left in the blower");
            FinishStock.Unlimited = false;
            yield return Frames(3);
            Vector3 wallPt = wall.World(wall.Bounds.center);

            yield return SaveWipeLoad();

            var wall2 = Insulation.FaceFrom(In(32f, 48f, 30f), Vector3.forward);
            Assert.NotNull(wall2);
            Assert.IsFalse(Insulation.IsExterior(wall2), "interior mark kept");
            shimmed = (List<Vector3>)typeof(Insulation).GetField("_shimmed", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.AreEqual(1, shimmed.Count, "foam-shimmed opening remembered");
            Assert.Less(Vector3.Distance(shimPoint, shimmed[0]), 1e-4f);
            Assert.AreEqual(1, Insulation.Attics.Count, "attic back");
            Assert.AreEqual(volLeft, (float)volField.GetValue(Insulation.Attics[0]), 1e-6f, "part bag kept");
        }

        // ================================================================== 5. hardwood over felt, countertop overhangs

        [UnityTest]
        public IEnumerator HardwoodFeltAndCountertopOverhangs_SurviveSaveLoad()
        {
            if (GameClock.I != null) GameClock.I.Hours = 10f;
            FinishStock.Unlimited = true;
            foreach (var id in new[] { "finish-nailer", "flooring-nailer", "level", "fin-drill" }) Assert.NotNull(FinishStock.Give(id), id);
            var O = new Vector3(130f, 0.6f, 60f);
            Vector3 W(float x, float y, float z) => O + new Vector3(I(x), I(y), I(z));
            Subfloor(W(0, 0, -72), 192f, 240f);
            StudWall(O, Vector3.right, Vector3.forward, 144f);
            StudWall(W(0, 0, 3.5f), Vector3.forward, Vector3.right, 140.5f);
            yield return Frames(3);
            Physics.SyncTransforms();
            FramePlane Face(Vector3 from, Vector3 dir) { var f = Insulation.FaceFrom(from, dir); Assert.NotNull(f, "face from " + from); return f; }
            var aRoom = Face(W(32, 40, 30), Vector3.back);
            var bRoom = Face(W(30, 40, 35.5f), Vector3.left);
            var floor = Face(W(30, 30, 30), Vector3.down);
            foreach (var f in new[] { aRoom, bRoom }) DrywallJob.Complete(f, "dw-12-4x12", null, "Warm Greige");
            if (GameClock.I != null) GameClock.I.Hours = 10f;
            Physics.SyncTransforms();
            float startU = bRoom.ToUV(W(3.5f, 0, 60)).x;
            var run = Cabinets.InstallRun(bRoom, startU, new[] { 18, 36, 24 }, new[] { 18, 36, 24 }, CabFinish.PaintedWhite, 0f, 1, true, true);
            var ct = Countertops.Install(run, CounterType.Granite);
            Assert.NotNull(ct, "countertop");
            Physics.SyncTransforms();
            float oMin = ct.OverhangXMin, oMax = ct.OverhangXMax, length = ct.Length, front = ct.FrontOverhang;
            Assert.AreEqual(1f, Units.ToInches(oMax), 0.01f, "1\" past the free end");
            // later a wall end goes up right at the top's free end: measuring again on load would give a flush end
            var stud = Lumber("2x4x8", I(30f), ct.transform.TransformPoint(new Vector3(ct.RunXMax + I(1.05f) + I(0.75f), I(1f), I(12f))), Quaternion.LookRotation(Vector3.up, ct.transform.right));

            // hardwood over 15 lb felt (no rosin paper on site)
            FinishStock.Unlimited = false;
            if (Game.Stock.Count("fin-rosin") > 0) Game.Stock.TryTake("fin-rosin", Game.Stock.Count("fin-rosin"));
            Game.Stock.Add("fin-felt15", 1); Game.Stock.Add("fin-hw-redoak", 6); Game.Stock.Add("fin-cleats", 6); Game.Stock.Add("fin-nails-15ga", 6);
            Rect UV(FramePlane f, Vector3 a, Vector3 b) { var p = f.ToUV(a); var q = f.ToUV(b); return Rect.MinMaxRect(Mathf.Min(p.x, q.x), Mathf.Min(p.y, q.y), Mathf.Max(p.x, q.x), Mathf.Max(p.y, q.y)); }
            var hw = Hardwood.Install(floor, UV(floor, W(80, 0, 4), W(140, 0, 76)));
            Assert.NotNull(hw, "hardwood: " + Game.ToastText);
            var mr = hw.GetComponentInChildren<MeshRenderer>();
            var felt = FinishMaterials.Get(InteriorMaterials.Underlay);
            Assert.AreEqual(felt, mr.sharedMaterials[mr.sharedMaterials.Length - 1], "laid over felt");
            int strips = hw.Pieces;
            yield return Frames(3);
            var stock = Snap();

            yield return SaveWipeLoad();

            Assert.AreEqual(1, Countertop.All.Count, "countertop back");
            var ct2 = Countertop.All[0];
            Assert.AreEqual(oMin, ct2.OverhangXMin, 1e-5f, "end overhang (wall end) kept");
            Assert.AreEqual(oMax, ct2.OverhangXMax, 1e-5f, "free-end overhang kept, not measured again");
            Assert.AreEqual(length, ct2.Length, 1e-5f, "same top length");
            Assert.AreEqual(front, ct2.FrontOverhang, 1e-5f, "front overhang");
            var hw2 = FloorInstall.All.Find(f => f.Type == FloorType.Hardwood);
            Assert.NotNull(hw2, "hardwood back"); Assert.AreEqual(strips, hw2.Pieces, "same strips");
            var mr2 = hw2.GetComponentInChildren<MeshRenderer>();
            Assert.AreEqual(felt, mr2.sharedMaterials[mr2.sharedMaterials.Length - 1], "still over felt, not rosin paper");
            AssertStockSame(stock, "finish");
        }

        // ================================================================== 6. stale shell project

        [UnityTest]
        public IEnumerator StaleShellProject_IsNotSavedOrReplayed()
        {
            FramingJob.RequireStock = false;
            var p = ShellProject.Start(Footprint.Make(new Vector3(150f, 0f, 60f), 0f, 12, 12));
            p.StartFloor();
            // the world is cleared without the project (e.g. a scene clear): its jobs are gone, the static isn't
            foreach (var j in new List<FramingJob>(p.Jobs)) if (j != null) SaveGaps.Kill(j.transform.root.gameObject);
            yield return Frames(2);
            Assert.AreSame(p, ShellProject.Current);
            Assert.IsTrue(ShellSave.IsStale(p));
            Assert.IsTrue(SaveSystem.Save(Slot), SaveSystem.LastMessage);
            Assert.IsNull(ShellProject.Current, "stale project forgotten");
            Assert.IsTrue(SaveSystem.Load(Slot), SaveSystem.LastMessage);
            yield return Frames(2);
            Assert.IsNull(ShellProject.Current, "not planned again on load");
            Assert.IsNull(JobTitled("Floor deck"), "no floor job replayed");
        }

        // ================================================================== 7. version-3 saves

        /// <summary>A version-3 save (round 2) has no "jobs" / "readymix" / "insulation2" sections and no tool state: it loads,
        /// the new parts come back empty, tools come back fresh.</summary>
        [UnityTest]
        public IEnumerator Version3SaveWithoutRound3Sections_Loads()
        {
            FramingJob.RequireStock = false;
            var landing = StairBuilder.Landing(new Vector3(-12f, 0f, 12f), 0f, I(48f), I(48f));
            landing.RunAll();
            ConcreteDispatch.Orders.Add(new ConcreteOrder { Yards = 2f, Cost = 500, YardsLeft = 2f, State = OrderState.Scheduled, ScheduledFor = ConcreteTime.Now + 300f });
            var gun = ToolFactory.Create("caulk-gun") as CaulkGun; gun.FeetLeft = 9f; Game.Belt.Owned.Add(gun);
            var lumber = Lumber("2x4x8", Ft(8f), new Vector3(95f, 0.05f, 95f), Quaternion.LookRotation(Vector3.right, Vector3.up));
            yield return Frames(2);
            var file = SaveSystem.Capture();
            file.version = 3;
            string[] round3 = { JobSave.Key, ReadyMixSave.Key, InsulationExtraSave.Key };
            file.ext.RemoveAll(kv => System.Array.IndexOf(round3, kv.key) >= 0);
            foreach (var td in file.owned) td.state = null;
            System.IO.Directory.CreateDirectory(SaveSystem.Dir);
            System.IO.File.WriteAllText(SaveSystem.PathFor(Slot), JsonUtility.ToJson(file));
            Assert.IsTrue(SaveSystem.Load(Slot), SaveSystem.LastMessage);
            yield return Frames(2);
            Assert.AreEqual(0, Object.FindObjectsByType<JobRecipe>(FindObjectsSortMode.None).Length, "no jobs section -> jobs cleared");
            Assert.AreEqual(0, ConcreteDispatch.Orders.Count, "no readymix section -> no orders");
            Assert.IsNull(FormSystem.Building);
            CaulkGun gun2 = null; foreach (var t in Game.Belt.Owned) if (t is CaulkGun g) gun2 = g;
            Assert.NotNull(gun2, "caulk gun back"); Assert.AreEqual(0f, gun2.FeetLeft, "no tool state -> fresh tube");
            Assert.IsTrue(System.Array.Exists(Object.FindObjectsByType<Piece>(FindObjectsSortMode.None), q => q.Def.Id == "2x4x8" && (q.transform.position - new Vector3(95f, 0.05f, 95f)).sqrMagnitude < 1e-4f), "plain pieces load");
        }
    }
}
