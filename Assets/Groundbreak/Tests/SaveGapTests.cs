using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Groundbreak.Tests.FinishTestKit;

namespace Groundbreak.Tests
{
    /// <summary>
    /// Save/load gaps (Docs/SAVEGAPS.md): exterior work (wrap, vinyl, brick, eaves, decks), MEP networks (runs, fittings,
    /// box make-up, breakers, bored holes, pressure test), curing blankets on set concrete, and New Game resetting stock + the
    /// tool belt. Each test builds the thing, saves to disk, wipes the world, loads and checks it came back.
    /// Screenshot: Logs/shots/savegap_house_reloaded.png (vinyl + eave + deck + condenser, meter and line set after a reload).
    /// </summary>
    public class SaveGapTests
    {
        const string Slot = "savegap_test";
        static float I(float inches) => Units.In(inches);

        HashSet<GameObject> _before;
        GameMode _mode; int _money;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(3);
            yield return MepTestScene.Clear();
            _before = new HashSet<GameObject>(UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects());
            _mode = GameSettings.Mode; _money = Game.Wallet.Money;
            if (GameClock.I != null) GameClock.I.Hours = 11f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (SaveSystem.Exists(Slot)) SaveSystem.Delete(Slot);
            // only what these tests make (a load may legitimately rebuild other systems' root objects)
            foreach (var w in Object.FindObjectsByType<ExteriorWall>(FindObjectsSortMode.None)) Object.Destroy(w.gameObject);
            foreach (var e in Object.FindObjectsByType<Eave>(FindObjectsSortMode.None)) Object.Destroy(e.gameObject);
            foreach (var d in Object.FindObjectsByType<Deck>(FindObjectsSortMode.None)) Object.Destroy(d.gameObject);
            foreach (var s in Object.FindObjectsByType<RestoredSlab>(FindObjectsSortMode.None)) Object.Destroy(s.gameObject);
            foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (_before != null && !_before.Contains(g) && (g.name == "SaveGapFoundation" || g.name == "SaveGap_Forms" || g.name == "Concrete Slab")) Object.Destroy(g);
            ExteriorWall.All.Clear(); Eaves.All.Clear(); DeckBuilder.All.Clear();
            yield return MepTestScene.Clear();
            yield return CleanScene();
            MepStock.Free = false;
            SeasonWeather.Override = null;
            SeasonRules.ResetState();
            GameSettings.Mode = _mode; Game.Wallet = new Wallet(_money);
            yield return Frames(2);
        }

        /// <summary>Destroys what a quit would lose: pieces, exterior layers, eaves, decks, MEP runs and fittings, slabs.</summary>
        static IEnumerator WipeWorld()
        {
            foreach (var w in Object.FindObjectsByType<ExteriorWall>(FindObjectsSortMode.None)) Object.Destroy(w.gameObject);
            foreach (var e in Object.FindObjectsByType<Eave>(FindObjectsSortMode.None)) Object.Destroy(e.gameObject);
            foreach (var d in Object.FindObjectsByType<Deck>(FindObjectsSortMode.None)) Object.Destroy(d.gameObject);
            foreach (var el in Object.FindObjectsByType<ConcreteElement>(FindObjectsSortMode.None)) Object.Destroy(el.gameObject);
            ExteriorWall.All.Clear(); Eaves.All.Clear(); DeckBuilder.All.Clear();
            yield return MepTestScene.Clear();
            SeasonRules.ResetState();
            yield return Frames(2);
            Assert.AreEqual(0, Object.FindObjectsByType<ExteriorWall>(FindObjectsSortMode.None).Length, "walls wiped");
            Assert.AreEqual(0, NetGraph.Runs.FindAll(r => r != null).Count, "runs wiped");
            Assert.AreEqual(0, ConcreteElement.All.Count, "slabs wiped");
        }

        static IEnumerator SaveWipeLoad()
        {
            Assert.IsTrue(SaveSystem.Save(Slot), SaveSystem.LastMessage);
            yield return WipeWorld();
            Assert.IsTrue(SaveSystem.Load(Slot), SaveSystem.LastMessage);
            yield return Frames(4);
            Physics.SyncTransforms();
        }

        static Piece P(string id, Vector3 pos, Quaternion rot, float len = 0f)
        {
            var def = Catalog.Get(id);
            Assert.NotNull(def, "missing catalog id " + id);
            var p = Piece.Create(def, len, pos, rot);
            p.Fasten();
            MepAutoAttach.AttachNow(p);
            return p;
        }

        // ------------------------------------------------------------------ exterior scene helpers (as in ExteriorTests)

        static void SheathWithHoles(Vector3 start, float lengthIn, float heightIn, params Rect[] holesIn)
        {
            float th = I(0.4375f);
            var rot = Quaternion.LookRotation(Vector3.up, Vector3.back);
            var holes = new List<Rect>(holesIn);
            for (float x = 0; x < lengthIn - 0.5f; x += 48f)
            {
                var sheet = Rect.MinMaxRect(x, 0f, Mathf.Min(lengthIn, x + 48f), heightIn);
                foreach (var c in PanelMesh.Cells(sheet, holes))
                {
                    var p = Piece.Create(Catalog.Get("osb716"), I(c.height), start - Vector3.forward * (th * 0.5f) + Vector3.right * I(c.center.x) + Vector3.up * I(c.center.y), rot);
                    p.SetSize(new Vector3(I(c.width), th, I(c.height)));
                    p.Fasten();
                }
            }
        }

        static void Foundation(Vector3 start, float lengthIn, float heightM)
        {
            var b = new MeshBuilder();
            float len = I(lengthIn);
            b.AddBox(new Vector3(start.x + len * 0.5f, heightM * 0.5f - I(2f), start.z + I(8f) * 0.5f - I(0.25f) * 0.5f), new Vector3(len + I(1f), heightM + I(4f), I(8.25f)), Quaternion.identity, 0);
            var go = new GameObject("SaveGapFoundation");
            go.AddComponent<MeshFilter>().sharedMesh = b.Build("Foundation");
            go.AddComponent<MeshRenderer>().sharedMaterial = Materials.Get(SurfaceKind.Concrete);
        }

        /// <summary>2x6 rafter tails (6/12, 12" overhang) over a wall facing -Z, a 2x6 fascia and an OSB roof deck.</summary>
        static Piece EaveFraming(Vector3 start, float lengthIn)
        {
            float plateTop = start.y + I(97.125f);
            float zOuter = start.z - I(0.4375f);
            float ang = Mathf.Atan(0.5f), sn = Mathf.Sin(ang), cs = Mathf.Cos(ang);
            var d = new Vector3(0f, -sn, -cs); var n = new Vector3(0f, cs, -sn);
            float hIn = -24f, hOut = 12f, slopeLen = I(hOut - hIn) / cs;
            Vector3 BottomAt(float h, float x) => new Vector3(x, plateTop - I(h) * 0.5f, zOuter - I(h));
            var rafRot = Quaternion.LookRotation(d, Vector3.Cross(d, n));
            for (float x = 0.75f; x <= lengthIn - 0.75f + 0.01f; x += 16f)
            {
                float xx = Mathf.Min(x, lengthIn - 0.75f);
                Lumber("2x6x12", slopeLen, (BottomAt(hIn, start.x + I(xx)) + BottomAt(hOut, start.x + I(xx))) * 0.5f + n * I(2.75f), rafRot);
                if (xx >= lengthIn - 0.75f) break;
            }
            Vector3 tailTop = BottomAt(hOut, 0f) + n * I(5.5f);
            var fascia = Lumber("2x6x12", I(lengthIn), new Vector3(start.x + I(lengthIn) * 0.5f, tailTop.y - I(2.75f), tailTop.z - I(0.75f)), Quaternion.LookRotation(Vector3.right, Vector3.Cross(Vector3.right, Vector3.up)));
            var deckRot = Quaternion.LookRotation(d, Vector3.Cross(d, Vector3.right));
            float deckLen = slopeLen + I(2f);
            for (float x = 0; x < lengthIn - 0.5f; x += 48f)
            {
                float wdt = Mathf.Min(48f, lengthIn - x);
                var mid = (BottomAt(hIn, 0f) + BottomAt(hOut + 1.5f, 0f)) * 0.5f + n * (I(5.5f) + I(0.22f));
                mid.x = start.x + I(x + wdt * 0.5f);
                var p = Piece.Create(Catalog.Get("osb716"), deckLen, mid, deckRot);
                p.SetSize(new Vector3(I(wdt), I(0.4375f), deckLen));
                p.Fasten();
            }
            return fascia;
        }

        static FramePlane SheathFace(Vector3 start, float uIn, float vIn)
        {
            var ray = new Ray(start + Vector3.right * I(uIn) + Vector3.up * I(vIn) + Vector3.back * 1f, Vector3.forward);
            Assert.IsTrue(Physics.Raycast(ray, out var hit, 2f), "no sheathing hit");
            var fp = FramePlane.FromHit(hit);
            Assert.NotNull(fp, "no frame plane on sheathing");
            return fp;
        }

        static ExteriorWall WallNear(Vector3 p)
        {
            ExteriorWall best = null; float bd = float.MaxValue;
            foreach (var w in ExteriorWall.All)
            {
                if (w == null) continue;
                float d = Vector3.Distance(w.Face.World(w.B.center), p);
                if (d < bd) { bd = d; best = w; }
            }
            return best;
        }

        static int NailCount(ExteriorWall w) { int n = 0; foreach (var c in w.Vinyl.Courses) n += c.Nails.Count; return n; }

        // ================================================================== 1. exterior

        [UnityTest]
        public IEnumerator ExteriorWrapSidingBrickEaveAndDeck_SurviveSaveLoad()
        {
            FinishStock.Unlimited = true;
            MepStock.Free = true;
            // --- wall A: 12' wall with a window, wrapped + flashed, vinyl, eave with soffit / fascia wrap / gutter / downspout
            var A = new Vector3(140f, I(8f), 60f);
            var ro = new Opening { LeftIn = 48f, WidthIn = 36.5f, SillIn = 36f, HeadIn = 84.5f };
            StudWall(A, Vector3.right, Vector3.forward, 144f, ro);
            SheathWithHoles(A, 144f, 97.125f, new Rect(48f, 36f, 36.5f, 48.5f));
            Foundation(A, 144f, I(8f));
            var fascia = EaveFraming(A, 144f);
            var win = Piece.Create(Catalog.Get("win-sh-3648"), 0f, A + Vector3.right * I(48f + 18.25f) + Vector3.up * I(60.25f) + Vector3.forward * I(3.25f * 0.5f - 0.75f - 0.4375f), Quaternion.LookRotation(Vector3.back, Vector3.up));
            win.Fasten();
            // --- wall C: raised 20' wall on a rim for the deck (as ExteriorTests.DeckAgainstWall)
            float fy = I(24f);
            var C = new Vector3(145f, fy, 60f);
            Lumber(ExteriorCatalog.PT2x10, I(144f), C + new Vector3(I(72f), I(4.625f), I(0.75f)), Quaternion.LookRotation(Vector3.right, Vector3.Cross(Vector3.right, Vector3.up)));
            Lumber(ExteriorCatalog.PT2x10, I(96f), C + new Vector3(I(192f), I(4.625f), I(0.75f)), Quaternion.LookRotation(Vector3.right, Vector3.Cross(Vector3.right, Vector3.up)));
            StudWall(new Vector3(C.x, fy + I(10f), C.z), Vector3.right, Vector3.forward, 240f);
            SheathWithHoles(C, 240f, 107.125f);
            Foundation(C, 240f, fy);
            // --- wall B: 8' brick veneer wall (part way up: base flashing, 9 courses, one tie row)
            var B = new Vector3(156f, I(8f), 60f);
            StudWall(B, Vector3.right, Vector3.forward, 96f);
            Sheathing(B, Vector3.right, Vector3.forward, 96f);
            Foundation(B, 96f, I(8f));
            // --- visible MEP: meter base on wall A, condenser on a pad in front of it with a line set to the wall
            var meter = P("el-meter200", A + new Vector3(I(112f), I(52f), 0f), Quaternion.Euler(0f, 180f, 0f));
            meter.transform.position = new Vector3(meter.transform.position.x, meter.transform.position.y, A.z - I(0.4375f) - I(1.2f) - meter.Size.z * 0.5f);
            var pad = HvacBuild.Place(HvacSpecs.Pad, new Vector3(A.x + I(20f), I(1.5f), A.z - I(30f)), Quaternion.identity);
            var cond = HvacBuild.Place("hvac-ac3t", new Vector3(A.x + I(20f), I(3f + 16f), A.z - I(30f)), Quaternion.identity);
            yield return Frames(3);
            Physics.SyncTransforms();

            var wA = ExteriorWall.For(SheathFace(A, 20f, 60f));
            Assert.AreEqual(1, wA.Openings.Count, "window opening");
            Assert.IsTrue(HouseWrap.WrapWall(wA), "wrap A: " + Game.ToastText);
            var e = Eaves.For(fascia);
            Assert.AreEqual(wA, e.Wall, "eave over wall A");
            Assert.IsTrue(Eaves.InstallSoffit(e)); Assert.IsTrue(Eaves.WrapFascia(e));
            Assert.IsTrue(VinylSiding.InstallAll(wA, "Sage"), "vinyl: " + Game.ToastText);
            Assert.IsTrue(Eaves.HangGutter(e)); Assert.IsTrue(Eaves.AddDownspout(e));
            Assert.Greater(ExteriorTrim.CaulkAll(wA), 0, "perimeter sealed");

            var wC = ExteriorWall.For(SheathFace(C, 30f, 50f));
            Assert.IsTrue(HouseWrap.WrapWall(wC));
            var deck = DeckBuilder.Plan(wC, wC.B.xMin + I(24f), 192f, 120f, fy + I(9.25f));
            Assert.IsTrue(DeckBuilder.BuildAll(deck), "deck: " + Game.ToastText);

            var wB = ExteriorWall.For(SheathFace(B, 20f, 60f));
            Assert.IsTrue(HouseWrap.WrapWall(wB));
            Assert.IsTrue(BrickVeneer.BaseFlashing(wB));
            for (int i = 0; i < 4; i++) Assert.IsTrue(BrickVeneer.LayCourse(wB));
            Assert.IsTrue(BrickVeneer.TieRow(wB));
            for (int i = 0; i < 5; i++) Assert.IsTrue(BrickVeneer.LayCourse(wB));

            var cs = HvacBuild.Port(cond, "suction"); var cl = HvacBuild.Port(cond, "liquid");
            float wallZ = A.z - I(0.4375f) - I(1.5f);
            foreach (var (spec, port) in new[] { (HvacSpecs.LineSuction, cs), (HvacSpecs.LineLiquid, cl) })
            {
                var r = HvacBuild.Route(spec, port, null, port.WorldPos + Vector3.up * I(8f), new Vector3(port.WorldPos.x, port.WorldPos.y + I(8f), wallZ - I(2f)), new Vector3(port.WorldPos.x, I(40f), wallZ - I(2f)), new Vector3(port.WorldPos.x, I(40f), wallZ + I(1f)));
                Assert.IsTrue(r.Ok, "line set: " + string.Join("; ", r.Warnings));
            }
            HvacBuild.SealAll();
            yield return Frames(3);

            // --- what must come back
            int walls = ExteriorWall.All.FindAll(x => x != null).Count;
            int eaves = Eaves.All.FindAll(x => x != null).Count, decks = DeckBuilder.All.FindAll(x => x != null).Count;
            int sheets = wA.Wrap.Sheets.Count, staples = wA.Wrap.Staples.Count, tapes = wA.Wrap.Tapes.Count;
            int courses = wA.Vinyl.Courses.Count, nails = NailCount(wA);
            float topLimit = wA.Vinyl.TopLimit;
            int caulked = wA.Trim.Joints.FindAll(j => j.Caulked).Count;
            int brickCourses = wB.Brick.Courses, ties = wB.Brick.Ties.Count, weeps = wB.Brick.Weeps.Count;
            int deckPieces = deck.Pieces.Count, deckStage = deck.Stage;
            float deckU0 = deck.U0; var cFace = wC.Face;
            var deckPos = new List<Vector3>(); foreach (var p in deck.Pieces) deckPos.Add(p.transform.position);
            float gutterStart = e.GutterYStart; int hangers = e.Hangers.Count;
            int runs = NetGraph.Runs.Count;
            Vector3 facePtA = wA.Face.World(wA.B.center);

            var file = SaveSystem.Capture();
            foreach (var pd in file.pieces) foreach (var dp in deckPos) Assert.Greater((pd.pos - dp).sqrMagnitude, 1e-8f, "deck lumber is rebuilt by the deck, not saved as loose pieces");

            yield return SaveWipeLoad();

            Assert.AreEqual(walls, ExteriorWall.All.FindAll(x => x != null).Count, "exterior walls restored");
            var a2 = WallNear(facePtA);
            Assert.NotNull(a2);
            Assert.AreEqual(wA.Face.FaceOffset, a2.Face.FaceOffset, 1e-3f, "wall A found on the same face");
            Assert.AreEqual(SidingKind.Vinyl, a2.Siding);
            Assert.AreEqual(sheets, a2.Wrap.Sheets.Count, "wrap sheets"); Assert.AreEqual(staples, a2.Wrap.Staples.Count, "cap staples"); Assert.AreEqual(tapes, a2.Wrap.Tapes.Count, "seam tape");
            Assert.IsTrue(a2.Wrap.Op(0).Flashed, "window flashing (sill, jambs, head, flap)");
            Assert.IsTrue(HouseWrap.AllSeamsTaped(a2));
            Assert.AreEqual(courses, a2.Vinyl.Courses.Count, "vinyl courses"); Assert.AreEqual(nails, NailCount(a2), "vinyl nails");
            Assert.AreEqual("Sage", a2.Vinyl.ColorName); Assert.AreEqual(topLimit, a2.Vinyl.TopLimit, 1e-4f);
            Assert.NotNull(a2.Vinyl.Courses[3].Go, "course meshes rebuilt");
            Assert.AreEqual(caulked, a2.Trim.Joints.FindAll(j => j.Caulked).Count, "sealant joints");
            Assert.Greater(a2.transform.Find("wrap").childCount, 0, "wrap mesh rebuilt");

            Assert.AreEqual(eaves, Eaves.All.Count, "eave restored");
            var e2 = Eaves.All.Find(x => x != null && Vector3.Distance(x.Fascia.transform.position, fascia.transform.position) < 0.01f);
            Assert.NotNull(e2, "eave on the restored fascia");
            Assert.IsTrue(e2.Soffit && e2.Wrapped && e2.Gutter && e2.Downspout, "soffit, wrap, gutter, downspout");
            Assert.AreEqual(gutterStart, e2.GutterYStart, 1e-4f); Assert.AreEqual(hangers, e2.Hangers.Count);
            Assert.AreEqual(a2, e2.Wall, "eave over the restored wall");
            Assert.Greater(e2.transform.childCount, 0, "eave meshes rebuilt");

            var b2 = WallNear(B + new Vector3(I(48f), I(48f), 0f));
            Assert.AreEqual(SidingKind.Brick, b2.Siding);
            Assert.AreEqual(brickCourses, b2.Brick.Courses, "brick courses"); Assert.AreEqual(ties, b2.Brick.Ties.Count, "ties"); Assert.AreEqual(weeps, b2.Brick.Weeps.Count, "weeps");
            Assert.Greater(b2.Brick.Lifts.FindAll(g => g != null).Count, 0, "brick lifts rebuilt");
            Assert.IsTrue(BrickVeneer.LayCourse(b2), "the restored veneer keeps going: " + Game.ToastText);

            Assert.AreEqual(decks, DeckBuilder.All.Count, "deck restored");
            var d2 = DeckBuilder.All.Find(x => x != null && x.Wall != null && x.Wall.Face.SameFace(cFace) && Mathf.Abs(x.U0 - deckU0) < 1e-3f);
            Assert.NotNull(d2, "deck on wall C");
            Assert.AreEqual(deckStage, d2.Stage); Assert.IsTrue(d2.Done);
            Assert.AreEqual(deckPieces, d2.Pieces.Count, "deck lumber rebuilt");
            for (int i = 0; i < deckPos.Count; i++) Assert.Less(Vector3.Distance(deckPos[i], d2.Pieces[i].transform.position), 0.002f, "deck piece " + i + " in place");
            int loose = 0;
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) foreach (var dp in deckPos) if (p.GetComponent<DeckPart>() == null && (p.transform.position - dp).sqrMagnitude < 1e-6f) loose++;
            Assert.AreEqual(0, loose, "no duplicate loose deck lumber");
            Assert.AreEqual(runs, NetGraph.Runs.Count, "line set restored");
            Assert.NotNull(Object.FindAnyObjectByType<ElMeter>(), "meter base back");

            if (GameClock.I != null) GameClock.I.Hours = 11.5f;
            yield return Frames(3);
            SiteLookTests.Shot(new Vector3(A.x - 2.6f, 2.4f, A.z - 8.2f), new Vector3(A.x + 5.2f, 1.0f, A.z - 0.6f), "savegap_house_reloaded", 62f);
            SiteLookTests.Shot(new Vector3(A.x + I(24f), 1.3f, A.z - 3.2f), new Vector3(A.x + I(60f), 1.3f, A.z), "savegap_vinyl_meter_reloaded", 55f);
            FinishStock.Unlimited = false;
        }

        // ================================================================== 2. MEP

        [UnityTest]
        public IEnumerator MepNetworks_SurviveSaveLoad()
        {
            MepStock.Free = true;
            var O = new Vector3(120f, 0f, 90f);
            var yaw180 = Quaternion.Euler(0f, 180f, 0f);
            // framing with a bored hole
            var stud = P("2x4x925", O + new Vector3(I(24f), I(1.5f + 46.3125f), 0f), Quaternion.Euler(-90f, 90f, 0f));
            StudHoles.Bore(stud, O + new Vector3(I(24f), I(24f), 0f), Vector3.right, I(0.75f), "3/4\" auger");

            // electrical: meter, panel, one receptacle box, a 12/2 home run landed on a 20 A breaker
            var meterP = P("el-meter200", O + new Vector3(0f, I(80f), I(6f)), Quaternion.identity);
            var panelP = P("el-panel200", O + new Vector3(0f, I(54f), -I(2f)), yaw180);
            var box = P("el-box-1g", O + new Vector3(I(40f), I(14f), -I(1.75f)), yaw180).GetComponent<ElBox>();
            var meter = meterP.GetComponent<ElMeter>(); var panel = panelP.GetComponent<ElPanel>();
            Assert.NotNull(box); Assert.NotNull(panel); Assert.NotNull(meter);
            var pb = panel.Node.Port("branch-bot"); var bp = box.Node.Port("top-a");
            var res = RunBuilder.Route(RunSpecs.Get("nm-12-2"), new List<Vector3> { pb.WorldPos, new Vector3(pb.WorldPos.x, I(24f), O.z), new Vector3(bp.WorldPos.x, I(24f), O.z), bp.WorldPos }, pb, bp);
            Assert.IsTrue(res.Ok, string.Join("; ", res.Warnings));
            var home = res.Runs[0];
            home.AddStaple(home.PointAt(I(10f), out _));
            ElTools.Strip(box); for (int k = 0; k < 3; k++) ElTools.Splice(box);
            StringAssert.StartsWith("Installed", ElTools.Install(box, ElDevice.Gfci));
            box.InstallPlate(); box.Wet = true;
            var brk = panel.Install("el-bkr-20-1p", 2); Assert.NotNull(brk);
            panel.Land(brk, home); brk.Label = "Garage receptacles";
            meter.SetUtility(true); panel.SetMain(true);

            // plumbing DWV: 2" PVC with two elbows, a wye and a reducing sanitary tee, test cap, all glued
            var pvc = RunSpecs.Get(PlumbCatalog.Pvc2);
            var dwv = RunBuilder.Route(pvc, new List<Vector3> { O + new Vector3(I(60f), I(30f), I(12f)), O + new Vector3(I(100f), I(30f), I(12f)), O + new Vector3(I(100f), I(30f), I(48f)), O + new Vector3(I(100f), I(70f), I(48f)) });
            Assert.IsTrue(dwv.Ok, string.Join("; ", dwv.Warnings));
            Assert.AreEqual(2, CountType("elbow"), "elbows at the two corners");
            Assert.NotNull(RunBuilder.TeeInto(dwv.Runs[0], O + new Vector3(I(80f), I(30f), I(12f)), Vector3.back, "wye"), "wye");
            RunBuilder.CapEnd(dwv.Runs[dwv.Runs.Count - 1], false);   // test cap on top of the stack (moves to the upper half when it is cut)
            Assert.NotNull(PlumbFit.ReducingTee(dwv.Runs[dwv.Runs.Count - 1], O + new Vector3(I(100f), I(55f), I(48f)), Vector3.right, "santee", RunSpecs.Get(PlumbCatalog.Pvc15)), "reducing santee");
            // supply: test gauge on a 1/2" PEX line with a cap, pumped and held 15 minutes (passed)
            var gaugeP = P(PlumbCatalog.Gauge, O + new Vector3(I(140f), I(40f), I(6f)), Quaternion.identity);
            var g = gaugeP.GetComponent<PressureGauge>();
            var stub = g.Node.Port("stub");
            var pex = RunBuilder.Route(RunSpecs.Get(PlumbCatalog.Pex12Blue), new List<Vector3> { stub.WorldPos, stub.WorldPos + Vector3.down * I(12f), stub.WorldPos + Vector3.down * I(12f) + Vector3.right * I(36f) }, stub, null);
            Assert.IsTrue(pex.Ok, string.Join("; ", pex.Warnings));
            RunBuilder.CapEnd(pex.Runs[0], false);

            // HVAC: 8x12 trunk with an elbow, end cap, a 6" takeoff with a round branch
            var trunk = HvacBuild.Route(HvacSpecs.Trunk8x12, null, null, O + new Vector3(I(60f), I(100f), I(60f)), O + new Vector3(I(160f), I(100f), I(60f)), O + new Vector3(I(160f), I(100f), I(120f)));
            Assert.IsTrue(trunk.Ok, string.Join("; ", trunk.Warnings));
            HvacBuild.EndCap(trunk.Runs[trunk.Runs.Count - 1], false);
            var to = HvacBuild.Takeoff(trunk.Runs[0], O + new Vector3(I(100f), I(100f), I(60f)), Vector3.up, 6f);
            Assert.NotNull(to, "takeoff");
            var col = to.Port("collar");
            Assert.IsTrue(HvacBuild.Route(HvacSpecs.Round6, col, null, col.WorldPos + Vector3.up * I(6f), col.WorldPos + Vector3.up * I(6f) + Vector3.back * I(30f)).Ok, "branch");

            foreach (var n in new List<NetNode>(NetGraph.Nodes)) foreach (var p in n.Ports) if (p.NeedsJoin) JoinTool.MakeJoint(p);
            Assert.IsTrue(PressureTest.Pump(g), "pump: " + g.Status);
            PressureTest.Advance(g, 16f);
            Assert.AreEqual(PressureGauge.TestState.Passed, g.State, g.Status);
            MepInspection.Run(Trade.Electrical, false);
            yield return Frames(3);

            // --- what must come back
            var runSig = Signatures();
            var nodeTypes = FreeNodeTypes();
            int joined = 0, open = 0; foreach (var n in NetGraph.Nodes) foreach (var p in n.Ports) { if (p.Run != null && p.Joined) joined++; if (p.NeedsJoin) open++; }
            int staples = 0; foreach (var r in NetGraph.Runs) staples += r.Staples.Count;
            Assert.AreEqual(0, open, "everything joined before the save");
            Vector3 homeA = home.StartPoint, homeB = home.EndPoint;
            bool elPassed = MepInspection.LastPassed.TryGetValue(Trade.Electrical, out bool ep) && ep;

            yield return SaveWipeLoad();

            CollectionAssert.AreEquivalent(runSig, Signatures(), "every run back with the same spec and end points");
            CollectionAssert.AreEquivalent(nodeTypes, FreeNodeTypes(), "fittings / takeoff / end cap back");
            int joined2 = 0, open2 = 0; foreach (var n in NetGraph.Nodes) foreach (var p in n.Ports) { if (p.Run != null && p.Joined) joined2++; if (p.NeedsJoin) open2++; }
            Assert.AreEqual(joined, joined2, "made joints"); Assert.AreEqual(0, open2, "no joint came back un-made");
            int staples2 = 0; foreach (var r in NetGraph.Runs) staples2 += r.Staples.Count;
            Assert.AreEqual(staples, staples2, "staples");

            var box2 = Object.FindAnyObjectByType<ElBox>();
            Assert.NotNull(box2);
            Assert.IsTrue(box2.MadeUp, "box make-up"); Assert.AreEqual(ElDevice.Gfci, box2.Device); Assert.IsTrue(box2.Plate); Assert.IsTrue(box2.Wet);
            var panel2 = Object.FindAnyObjectByType<ElPanel>();
            Assert.AreEqual(1, panel2.Breakers.Count, "breaker");
            var b2 = panel2.Breakers[0];
            Assert.AreEqual(2, b2.Slot); Assert.AreEqual(20, b2.Amps); Assert.AreEqual("Garage receptacles", b2.Label);
            Assert.NotNull(b2.Run, "home run landed");
            Assert.Less(Vector3.Distance(homeA, b2.Run.StartPoint), 0.002f); Assert.Less(Vector3.Distance(homeB, b2.Run.EndPoint), 0.002f);
            Assert.IsTrue(b2.Run.Tags.Contains("homerun"));
            Assert.AreEqual(0, panel2.Unlanded().Count);
            Assert.IsTrue(panel2.Main);
            Assert.IsTrue(Object.FindAnyObjectByType<ElMeter>().Utility, "meter set");
            var holes = Object.FindObjectsByType<StudHoles>(FindObjectsSortMode.None);
            Assert.AreEqual(1, holes.Length, "bored stud"); Assert.AreEqual(1, holes[0].Holes.Count);
            Assert.AreEqual(I(0.75f), holes[0].Holes[0].Dia, 1e-5f);
            var g2 = Object.FindAnyObjectByType<PressureGauge>();
            Assert.AreEqual(PressureGauge.TestState.Passed, g2.State, "pressure test result kept");
            Assert.IsTrue(PressureTest.Passed(g2), "the restored supply still holds");
            Assert.AreEqual(elPassed, MepInspection.LastPassed.TryGetValue(Trade.Electrical, out bool ep2) && ep2, "rough inspection result");
            var red = new List<NetNode>(NetGraph.Nodes).Find(n => n.Type == "santee");
            Assert.NotNull(red); Assert.AreEqual(1.5f, red.Ports[2].NominalIn, 0.01f, "reducing branch kept");
            yield return Frames(3);
            SiteLookTests.Shot(O + new Vector3(I(100f), 1.6f, -2.4f), O + new Vector3(I(100f), I(60f), I(50f)), "savegap_mep_reloaded", 65f);
        }

        static int CountType(string t) { int n = 0; foreach (var x in NetGraph.Nodes) if (x != null && x.Type == t && x.Piece == null) n++; return n; }

        static List<string> Signatures()
        {
            var l = new List<string>();
            foreach (var r in NetGraph.Runs)
                if (r != null) l.Add($"{r.Spec.Id} {Q(r.StartPoint)} {Q(r.EndPoint)} {r.Points.Count} {(r.A != null ? r.A.Node.Type + ":" + r.A.Label : "-")} {(r.B != null ? r.B.Node.Type + ":" + r.B.Label : "-")}");
            return l;
        }

        static string Q(Vector3 v) => $"{Mathf.Round(v.x * 500f)},{Mathf.Round(v.y * 500f)},{Mathf.Round(v.z * 500f)}";

        static List<string> FreeNodeTypes()
        {
            var l = new List<string>();
            foreach (var n in NetGraph.Nodes) if (n != null && n.GetComponent<Piece>() == null) l.Add(n.Type + " " + Q(n.transform.position) + " " + n.Ports.Count);
            return l;
        }

        // ================================================================== 3. curing blankets

        static DayWeather Cold() => new DayWeather { Day = 200, Sky = Sky.Clear, HighF = 36f, LowF = 22f, WindMph = 5f, GustMph = 6.5f, PrecipIn = 0, PrecipStart = 0, PrecipEnd = 24 };

        [UnityTest]
        public IEnumerator CuringBlanketsOnSlab_SurviveSaveLoad()
        {
            GameSettings.Mode = GameMode.Career;
            SeasonWeather.Override = Cold();
            Assert.IsTrue(SeasonWeather.RulesOn, "cold career weather");
            var o = new FormOutline { TopY = 4f * Units.Inch, Closed = true, Root = new GameObject("SaveGap_Forms") };
            Vector3 c0 = new Vector3(210f, 0f, 210f);
            o.Corners.Add(c0); o.Corners.Add(c0 + new Vector3(4f, 0, 0)); o.Corners.Add(c0 + new Vector3(4f, 0, 3f)); o.Corners.Add(c0 + new Vector3(0, 0, 3f));
            for (int i = 0; i < 4; i++)
            {
                var r = new FormRun { A = o.Corners[i], B = o.Corners[(i + 1) % 4] };
                Vector3 d = r.B - r.A; r.Length = d.magnitude; r.Dir = d / r.Length; r.Out = new Vector3(r.Dir.z, 0, -r.Dir.x);
                o.Runs.Add(r);
            }
            var pour = PourSystem.Begin(o);
            pour.Joints.Add((c0 + new Vector3(2f, o.TopY, 0f), c0 + new Vector3(2f, o.TopY, 3f)));
            int need = 3;
            while (Game.Stock.Count(SeasonRules.Blanket) > 0) Game.Stock.TryTake(SeasonRules.Blanket, 1);
            Game.Stock.Add(SeasonRules.Blanket, need + 2);
            var el = pour.Complete();
            Assert.NotNull(el, "slab set");
            if (SeasonRules.BlanketsOn(el) == 0) SeasonRules.OnPoured(el); // SeasonDirector not subscribed in this scene
            int on = SeasonRules.BlanketsOn(el);
            Assert.Greater(on, 0, "crew laid curing blankets on the cold pour");
            int stockAfter = Game.Stock.Count(SeasonRules.Blanket);
            el.PouredAt -= 30f; el.CureLagMinutes = 42f; el.Covered = true;
            float top = el.TopY, poured = el.PouredAt; var outline = el.Outline;
            yield return Frames(2);

            yield return SaveWipeLoad();

            ConcreteElement el2 = null;
            foreach (var x in ConcreteElement.All) if (x != null && x.Contains(c0 + new Vector3(1f, 0f, 1f))) el2 = x;
            Assert.NotNull(el2, "slab restored");
            Assert.AreEqual(top, el2.TopY, 1e-4f); Assert.AreEqual(poured, el2.PouredAt, 1e-3f); Assert.AreEqual(42f, el2.CureLagMinutes, 1e-3f);
            Assert.IsTrue(el2.Covered);
            Assert.AreEqual(outline.Length, el2.Outline.Length);
            for (int i = 0; i < outline.Length; i++) Assert.Less(Vector2.Distance(outline[i], el2.Outline[i]), 1e-3f);
            Assert.AreEqual(on, SeasonRules.BlanketsOn(el2), "curing blankets still on the slab");
            Assert.AreEqual(stockAfter, Game.Stock.Count(SeasonRules.Blanket), "blankets not charged again on load");
            Assert.NotNull(el2.GetComponent<MeshCollider>(), "slab has a collider");
            Assert.IsTrue(Physics.Raycast(new Vector3(c0.x + 1f, 2f, c0.z + 1f), Vector3.down, out var hit, 5f) && hit.collider.GetComponentInParent<ConcreteElement>() == el2, "raycasts land on the restored slab");
            Assert.AreEqual("Concrete Slab", el2.gameObject.name);

            // the cure keeps going: once strong enough the blankets come off back into stock
            el2.PouredAt -= 10f * 1440f;
            SeasonWeather.Override = new DayWeather { Day = 200, Sky = Sky.Clear, HighF = 60f, LowF = 45f, WindMph = 5f, GustMph = 6.5f, PrecipEnd = 24 };
            SeasonRules.TickCure();
            GameClock.I.Hours += 0.25f;
            SeasonRules.TickCure();
            Assert.AreEqual(0, SeasonRules.BlanketsOn(el2), "blankets came off the cured slab");
            Assert.AreEqual(stockAfter + on, Game.Stock.Count(SeasonRules.Blanket), "blankets back in site stock");
            yield return Frames(2);
            SiteLookTests.Shot(c0 + new Vector3(-2.5f, 2.5f, -3f), c0 + new Vector3(2f, 0f, 1.5f), "savegap_slab_reloaded", 60f);
            while (Game.Stock.Count(SeasonRules.Blanket) > 0) Game.Stock.TryTake(SeasonRules.Blanket, 1);
            Object.Destroy(o.Root);
        }

        // ================================================================== 4. new game

        [UnityTest]
        public IEnumerator NewGame_ResetsSiteStockYardStockAndToolBelt()
        {
            yield return Frames(2);
            Game.Stock.Add("2x6x16", 25); Game.Stock.Add("brick-modular", 400);
            Game.Stock.Scrap.Add(("2x4x8", 0.6f));
            Yard.Stock.Add("2x4x8", 40);
            var belt = Game.Belt;
            Assert.NotNull(belt);
            var stapler = FinishStock.Give("cap-stapler");
            Assert.NotNull(stapler);
            belt.Select(belt.IndexOf(stapler));
            belt.Nails = 17;
            Game.Snap.Origin = new Vector3(3f, 0f, 4f); Game.Snap.OriginYaw = 30f;
            Assert.Greater(belt.Owned.Count, ToolBelt.StarterTools.Length);

            MainMenu.NewGame(GameMode.FreeBuild);
            yield return Frames(3);

            Assert.AreEqual(0, Game.Stock.Count("2x6x16"), "site stock from the last career is gone");
            Assert.AreEqual(0, Game.Stock.Count("brick-modular"));
            foreach (var (id, n) in GameBootstrap.StarterStock) Assert.AreEqual(n, Game.Stock.Count(id), "starting material " + id);
            Assert.AreEqual(0, Game.Stock.Scrap.Count, "scrap pile empty");
            Assert.AreEqual(0, Yard.Stock.Count("2x4x8"), "warehouse stock empty");
            Assert.AreEqual(ToolBelt.StarterTools.Length, Game.Belt.Owned.Count, "only the starter tools");
            foreach (var id in ToolBelt.StarterTools) Assert.IsTrue(Game.Belt.Owned.Exists(t => t.Id == id), "starter tool " + id);
            Assert.IsFalse(Game.Belt.Owned.Exists(t => t.Id == "cap-stapler"), "bought tools don't carry over");
            Assert.AreEqual(ToolBelt.StarterNails, Game.Belt.Nails, "full nail pouch");
            Assert.AreEqual(0, Game.Belt.ActiveIndex);
            Assert.AreEqual(Vector3.zero, Game.Snap.Origin); Assert.AreEqual(0f, Game.Snap.OriginYaw);
        }

        /// <summary>A version-1 save (no "mep" / "exterior" / "slabs" sections) still loads, with those parts empty.</summary>
        [UnityTest]
        public IEnumerator OldSaveWithoutNewSections_Loads()
        {
            MepStock.Free = true;
            var O = new Vector3(125f, 0f, 120f);
            var pvc = RunSpecs.Get(PlumbCatalog.Pvc2);
            Assert.IsTrue(RunBuilder.Route(pvc, new List<Vector3> { O, O + Vector3.right, O + Vector3.right + Vector3.forward }).Ok);
            var file = SaveSystem.Capture();
            file.version = 1;
            file.ext.RemoveAll(kv => kv.key == MepSave.Key || kv.key == ExteriorSave.Key || kv.key == SlabSave.Key);
            System.IO.Directory.CreateDirectory(SaveSystem.Dir);
            System.IO.File.WriteAllText(SaveSystem.PathFor(Slot), JsonUtility.ToJson(file));
            Assert.IsTrue(SaveSystem.Load(Slot), SaveSystem.LastMessage);
            yield return Frames(2);
            Assert.AreEqual(0, NetGraph.Runs.FindAll(r => r != null && r.gameObject.activeInHierarchy).Count, "old save: no networks (and the session's runs were cleared)");
        }
    }
}
