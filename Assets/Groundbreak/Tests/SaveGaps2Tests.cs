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
    /// Save/load gaps round 2 (Docs/SAVEGAPS.md): concrete before it sets, the house shell + roofing, drywall / paint / insulation,
    /// interior finish, cut stringers, partly used rolls / offcuts, New Game leaks, and old saves still loading. Each test builds the
    /// thing, saves to disk, wipes what a quit would lose, loads, and checks it came back without charging stock again. Only plain
    /// values are kept across the wipe (no references to pre-wipe objects).
    /// Screenshots (Logs/shots): savegap2_footing_rebar_wetpour, savegap2_layout_forms, savegap2_roof_partial_shingles,
    /// savegap2_roofed_room_interior, savegap2_drywall_insulation, savegap2_kitchen_finish, savegap2_stringers.
    /// </summary>
    public class SaveGaps2Tests
    {
        const string Slot = "savegap2_test";
        static float I(float inches) => Units.In(inches);
        static float Ft(float feet) => feet * Units.Foot;

        GameMode _mode; int _money; bool _requireStock, _instant; int _grid; Vector3 _snapO; float _snapYaw;
        readonly List<GameObject> _lights = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(3);
            yield return MepTestScene.Clear();
            _mode = GameSettings.Mode; _money = Game.Wallet.Money;
            _requireStock = FramingJob.RequireStock; _instant = FramingJobRunner.Instant;
            _grid = Game.Snap.GridIndex; _snapO = Game.Snap.Origin; _snapYaw = Game.Snap.OriginYaw;
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
            foreach (var l in _lights) if (l != null) Object.Destroy(l);
            _lights.Clear();
            WipeAll();
            BoomTruck.RemoveAll();
            yield return MepTestScene.Clear();
            yield return CleanScene();
            MepStock.Free = false;
            SeasonWeather.Override = null;
            SeasonRules.ResetState();
            FramingJob.RequireStock = _requireStock; FramingJobRunner.Instant = _instant;
            Game.Snap.GridIndex = _grid; Game.Snap.Origin = _snapO; Game.Snap.OriginYaw = _snapYaw;
            GameSettings.Mode = _mode; Game.Wallet = new Wallet(_money);
            yield return Frames(2);
        }

        /// <summary>What a quit loses: every piece, the shell project, concrete work, drywall / insulation, finish, slabs.</summary>
        static void WipeAll()
        {
            SaveSystem.ClearPieces();
            ShellSave.Wipe();
            foreach (var j in new List<FramingJob>(FramingJob.All)) if (j != null) SaveGaps.Kill(j.transform.root.gameObject);
            foreach (var w in Object.FindObjectsByType<WallAssembly>(FindObjectsSortMode.None)) SaveGaps.Kill(w.gameObject);
            foreach (var a in new List<RoofAnchor>(RoofAnchor.All)) if (a != null) SaveGaps.Kill(a.gameObject);
            ConcreteSave.Wipe();
            foreach (var el in new List<ConcreteElement>(ConcreteElement.All)) if (el != null) { SaveGaps.Kill(el.gameObject); Object.DestroyImmediate(el); }
            DrywallSave.Wipe();
            FinishSave.Wipe();
            foreach (var cs in Object.FindObjectsByType<StringerLayoutMark>(FindObjectsSortMode.None)) SaveGaps.Kill(cs.gameObject);
        }

        static IEnumerator WipeWorld()
        {
            WipeAll();
            yield return Frames(2);
            Assert.AreEqual(0, Object.FindObjectsByType<Piece>(FindObjectsSortMode.None).Length, "pieces wiped");
            Assert.IsNull(ShellProject.Current, "shell wiped");
            Assert.AreEqual(0, RoofInfo.All.Count, "roof info wiped");
            Assert.AreEqual(0, FormSystem.All.Count + PourSystem.All.Count + RebarSystem.Bars.Count + SiteLayout.Stakes.Count, "concrete wiped");
            Assert.AreEqual(0, Drywall.Surfaces.Count + Insulation.Batts.Count, "drywall / insulation wiped");
            Assert.AreEqual(0, CabinetUnit.All.Count + TrimPiece.All.Count + HungDoor.All.Count + Countertop.All.Count, "finish wiped");
        }

        static IEnumerator SaveWipeLoad()
        {
            Assert.IsTrue(SaveSystem.Save(Slot), SaveSystem.LastMessage);
            yield return WipeWorld();
            Assert.IsTrue(SaveSystem.Load(Slot), SaveSystem.LastMessage);
            yield return Frames(4);
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ stock bookkeeping

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
            var all = Object.FindObjectsByType<Piece>(FindObjectsSortMode.None);
            var seen = new Dictionary<string, List<Vector3>>();
            int dup = 0;
            foreach (var p in all)
            {
                if (p == null || p.Def == null || !p.gameObject.activeInHierarchy) continue;
                if (!seen.TryGetValue(p.Def.Id, out var l)) seen[p.Def.Id] = l = new List<Vector3>();
                foreach (var q in l) if ((q - p.transform.position).sqrMagnitude < 1e-6f) { dup++; break; }
                l.Add(p.transform.position);
            }
            return dup;
        }

        static int ActivePieces() { int n = 0; foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (p != null && p.gameObject.activeInHierarchy) n++; return n; }

        void Light(Vector3 p, float intensity = 1f, float range = 9f)
        {
            var go = new GameObject("SaveGap2Light");
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = range; l.intensity = intensity; l.color = new Color(1f, 0.95f, 0.88f); l.shadows = LightShadows.Soft;
            go.transform.position = p; _lights.Add(go);
        }

        // ================================================================== 1. concrete before it sets

        [UnityTest]
        public IEnumerator ConcreteLayoutFormsRebarAndWetPour_SurviveSaveLoad()
        {
            Assert.NotNull(SiteGround.I, "site ground");
            Game.Snap.GridIndex = 3; Game.Snap.Origin = Vector3.zero; Game.Snap.OriginYaw = 0f;
            // footing pad A (4' x 8', staked, kickers, oiled, poly, rebar mat tied, half poured + worked) and pad B (4' x 4', full, set, forms on)
            Vector3 aMin = new Vector3(Ft(-26f), 0f, Ft(40f)), aMax = new Vector3(Ft(-22f), 0f, Ft(48f));
            Vector3 bMin = new Vector3(Ft(-20f), 0f, Ft(40f)), bMax = new Vector3(Ft(-16f), 0f, Ft(44f));
            int st0 = SiteLayout.AddStake(aMin), st1 = SiteLayout.AddStake(new Vector3(aMax.x, 0, aMin.z)), st2 = SiteLayout.AddStake(aMax), st3 = SiteLayout.AddStake(new Vector3(aMin.x, 0, aMax.z));
            SiteLayout.AddLine(st0, st1); SiteLayout.AddLine(st1, st2); SiteLayout.AddLine(st2, st3); SiteLayout.AddLine(st3, st0);
            SiteLayout.SetLaser(new Vector3(Ft(-28f), 0f, Ft(36f)));
            SiteLayout.SetSlabTop(I(4f));
            for (float x = aMin.x; x <= aMax.x; x += 0.4f) SiteLayout.SprayMark(new Vector3(x, SiteGround.GroundY(new Vector3(x, 0, aMin.z - I(10f))), aMin.z - I(10f)), Vector3.up, new Color(1f, 0.45f, 0.05f));

            foreach (var id in new[] { "2x6x12", "2x8x12", "2x10x12", "2x12x12" }) Game.Stock.Add(id, 10);   // whatever depth the ground there needs
            Game.Stock.Add(FormSystem.StakeId, 80); Game.Stock.Add("2x4x8", 6);
            Game.Stock.Add(FormSystem.FormOilId, 500); Game.Stock.Add(FormSystem.PolyId, 500);
            Game.Stock.Add("rebar-4", 30); Game.Stock.Add(RebarSystem.ChairId, 200); Game.Stock.Add("tiewire", 2); Game.Stock.Add(PourSystem.AnchorId, 10);

            StringAssert.StartsWith("Forms set", FormSystem.BuildRectangle(aMin, aMax));
            var oA = FormSystem.Active;
            Assert.Greater(FormSystem.AutoStake(oA), 8);
            FormSystem.AddBrace(oA.Runs[0], oA.Runs[0].Length * 0.5f, oA);
            FormSystem.Oil(oA); FormSystem.LayPoly(oA);
            Assert.Greater(RebarSystem.AutoGrid(oA, "rebar-4"), 4, "rebar mat");
            int tied = RebarSystem.TieAllNear((aMin + aMax) * 0.5f + Vector3.left * 0.4f, 0.5f);
            Assert.Greater(tied, 0); Assert.Less(RebarSystem.TiedCount, RebarSystem.Crossings.Count, "some crossings left untied");

            StringAssert.StartsWith("Forms set", FormSystem.BuildRectangle(bMin, bMax));
            var oB = FormSystem.Active;
            FormSystem.AutoStake(oB);

            var ord = new ConcreteOrder { Yards = 2f, Psi = 3500, SlumpIn = 5 };
            var pA = PourSystem.Begin(oA);
            Vector3 cA = (aMin + aMax) * 0.5f;
            for (int k = 0; k < 6; k++) pA.AddConcrete(new Vector3(cA.x - 0.3f, 0f, aMin.z + 0.4f + k * 0.25f), pA.CapacityM3 * 0.09f, ord);
            yield return Frames(20);   // let it flow
            foreach (var p in pA.WorkPoints(0.3f)) pA.Apply(FinishOp.Vibrate, p);
            pA.Apply(FinishOp.Screed, new Vector3(cA.x - 0.3f, 0f, aMin.z + 0.5f));
            Assert.IsNotNull(pA.WetSetAnchor(new Vector3(aMin.x + 0.05f, 0f, aMin.z + 0.6f)), "J-bolt wet-set: " + Game.ToastText);
            Assert.IsTrue(pA.Plastic && pA.FillFraction > 0.2f && pA.FillFraction < 0.9f, "pad A part poured and still plastic: " + pA.FillFraction);

            var pB = PourSystem.Begin(oB);
            for (int guard = 0; guard < 60 && !pB.Full; guard++) pB.AddConcrete(pB.LowestPoint((bMin + bMax) * 0.5f), pB.CapacityM3 * 0.08f, ord);
            foreach (var p in pB.WorkPoints(0.4f)) pB.Apply(FinishOp.Screed, p);
            var elB = pB.Complete();
            Assert.NotNull(elB, "pad B set");
            elB.PouredAt -= 60f * 6f;   // six hours old: still green and darkish
            yield return Frames(3);

            // --- what must come back
            var stock = Snap();
            int stakes = SiteLayout.Stakes.Count, lines = SiteLayout.Lines.Count, paint = SiteLayout.Paint.Count;
            float bench = SiteLayout.BenchmarkY, slabTop = SiteLayout.SlabTopAboveBenchmark;
            int formPieces = 0; foreach (var f in Object.FindObjectsByType<FormPart>(FindObjectsSortMode.None)) formPieces++;
            int aStakes = 0, aBraces = 0, aBoards = 0; foreach (var r in oA.Runs) { aStakes += r.Stakes.Count; aBraces += r.Braces.Count; aBoards += r.Boards.Count; }
            var boardPos = new List<Vector3>(); foreach (var r in oA.Runs) foreach (var b in r.Boards) boardPos.Add(b.transform.position);
            int bars = RebarSystem.Bars.Count, crossings = RebarSystem.Crossings.Count, tiedN = RebarSystem.TiedCount, chairs = RebarSystem.Chairs.Count;
            Vector3 bar0a = RebarSystem.Bars[0].A, bar0b = RebarSystem.Bars[0].B;
            float placedA = pA.PlacedM3, vibA = pA.Vibrated, screedA = pA.Screeded, capA = pA.CapacityM3; int defA = pA.Defects.Count, anchorsA = pA.Anchors.Count;
            float surfA = pA.SurfaceY(new Vector3(cA.x - 0.3f, 0f, aMin.z + 0.5f));
            float topB = elB.TopY, pouredB = elB.PouredAt; int defB = elB.Defects.Count;
            Vector3 cB = (bMin + bMax) * 0.5f;
            int pieces = ActivePieces();

            yield return SaveWipeLoad();

            Assert.AreEqual(stakes, SiteLayout.Stakes.Count, "layout stakes"); Assert.AreEqual(lines, SiteLayout.Lines.Count, "string lines");
            Assert.IsTrue(SiteLayout.LaserSet, "laser"); Assert.AreEqual(bench, SiteLayout.BenchmarkY, 1e-4f, "benchmark"); Assert.AreEqual(slabTop, SiteLayout.SlabTopAboveBenchmark, 1e-5f);
            Assert.AreEqual(paint, SiteLayout.Paint.Count, "marking paint");
            Assert.AreEqual(2, FormSystem.All.Count, "two form outlines");
            var a2 = FormSystem.All.Find(o => o.Contains(cA)); var b2 = FormSystem.All.Find(o => o.Contains(cB));
            Assert.NotNull(a2); Assert.NotNull(b2);
            Assert.IsTrue(a2.Closed && a2.Oiled && a2.Poly != null && !a2.Stripped, "pad A: closed, oiled, poly");
            int aStakes2 = 0, aBraces2 = 0, aBoards2 = 0; foreach (var r in a2.Runs) { aStakes2 += r.Stakes.Count; aBraces2 += r.Braces.Count; aBoards2 += r.Boards.Count; }
            Assert.AreEqual(aStakes, aStakes2, "stakes"); Assert.AreEqual(aBraces, aBraces2, "kicker parts"); Assert.AreEqual(aBoards, aBoards2, "form boards");
            int formPieces2 = 0; foreach (var f in Object.FindObjectsByType<FormPart>(FindObjectsSortMode.None)) formPieces2++;
            Assert.AreEqual(formPieces, formPieces2, "form pieces carry FormPart again");
            Assert.AreEqual(0, DuplicatePieces(), "no doubled form boards");
            Assert.AreEqual(pieces, ActivePieces(), "same number of pieces");
            foreach (var bp in boardPos)
            {
                int n = 0; foreach (var r in a2.Runs) foreach (var b in r.Boards) if ((b.transform.position - bp).sqrMagnitude < 1e-6f) n++;
                Assert.AreEqual(1, n, "board in place");
            }
            Assert.AreEqual(bars, RebarSystem.Bars.Count, "bars"); Assert.AreEqual(crossings, RebarSystem.Crossings.Count, "crossings");
            Assert.AreEqual(tiedN, RebarSystem.TiedCount, "tied crossings"); Assert.AreEqual(chairs, RebarSystem.Chairs.Count, "chairs");
            Assert.Less(Vector3.Distance(bar0a, RebarSystem.Bars[0].A) + Vector3.Distance(bar0b, RebarSystem.Bars[0].B), 1e-4f, "bar in place");
            Assert.NotNull(RebarSystem.Bars[0].Go, "bar mesh");

            Assert.AreEqual(2, PourSystem.All.Count, "both pours");
            var pA2 = a2.Pour; var pB2 = b2.Pour;
            Assert.NotNull(pA2); Assert.NotNull(pB2);
            Assert.IsTrue(pA2.Plastic, "pad A still plastic");
            Assert.AreEqual(placedA, pA2.PlacedM3, 1e-4f, "placed concrete"); Assert.AreEqual(capA, pA2.CapacityM3, 1e-4f);
            // (fractions of the cells holding concrete: the plastic concrete keeps flowing a little between the save and the check)
            Assert.AreEqual(vibA, pA2.Vibrated, 0.03f, "vibrated cells"); Assert.AreEqual(screedA, pA2.Screeded, 0.03f, "screeded cells");
            Assert.Greater(pA2.Vibrated, 0.5f, "vibrator passes kept");
            Assert.AreEqual(defA, pA2.Defects.Count); Assert.AreEqual(anchorsA, pA2.Anchors.Count, "wet-set J-bolt");
            Assert.AreEqual(surfA, pA2.SurfaceY(new Vector3(cA.x - 0.3f, 0f, aMin.z + 0.5f)), I(0.25f), "concrete surface where it was");
            Assert.NotNull(pB2.Element, "pad B set"); Assert.AreEqual(pB2, pB2.Element.Pour, "set slab keeps its PourSystem");
            Assert.AreEqual(topB, pB2.Element.TopY, 1e-4f); Assert.AreEqual(pouredB, pB2.Element.PouredAt, 1e-3f); Assert.AreEqual(defB, pB2.Element.Defects.Count);
            int atB = 0; foreach (var el in ConcreteElement.All) if (el != null && el.Contains(cB)) atB++;
            Assert.AreEqual(1, atB, "one slab at pad B (not doubled by the slabs section)");
            Assert.IsTrue(Physics.Raycast(new Vector3(cB.x, 2f, cB.z), Vector3.down, out var hit, 5f) && hit.collider.GetComponentInParent<PourSystem>() == pB2, "raycast lands on the restored pour");
            AssertStockSame(stock, "concrete");

            // behaves like the original: strip the forms once 24 h old (needs o.Pour.Element), plastic concrete still workable
            pB2.Element.PouredAt -= 60f * 20f;
            StringAssert.StartsWith("Forms stripped", FormSystem.Strip(b2), "strip the restored forms");
            Assert.IsTrue(pB2.Element.FormsStripped);
            Assert.AreEqual("", pA2.Apply(FinishOp.Rake, new Vector3(cA.x - 0.3f, 0f, aMin.z + 0.5f)), "restored wet concrete can still be raked");

            if (GameClock.I != null) GameClock.I.Hours = 11f;
            yield return Frames(3);
            SiteLookTests.Shot(new Vector3(cA.x + 0.9f, 1.45f, aMin.z - 1.5f), new Vector3(cA.x - 0.1f, 0f, cA.z - 0.2f), "savegap2_footing_rebar_wetpour", 60f);
            SiteLookTests.Shot(new Vector3(Ft(-30f), 3.2f, Ft(34f)), new Vector3(Ft(-21f), 0f, Ft(44f)), "savegap2_layout_forms", 62f);
        }

        // ================================================================== 2. the house shell, roof and roofing (+ a finished room inside)

        [UnityTest]
        public IEnumerator ShellRoofAndRoofing_SurviveSaveLoad()
        {
            FramingJob.RequireStock = false;
            FramingJobRunner.Instant = true;
            if (Game.Player != null) Game.Player.transform.position = new Vector3(-10f, 0f, -10f);
            var origin = new Vector3(140f, 0f, 140f);
            var fp = Footprint.Make(origin, 0f, 16, 12);
            var p = ShellProject.Start(fp);
            p.Openings[0].Add(new WallOpening { Product = Catalog.Get("door-ext-steel36"), CenterIn = 50f });
            p.Openings[0].Add(new WallOpening { Product = Catalog.Get("win-sh-3648"), CenterIn = 138f });
            p.Openings[1].Add(new WallOpening { Product = Catalog.Get("win-sh-2436"), CenterIn = 68.5f });
            p.StartFloor().RunAll();
            for (int i = 0; i < 4; i++) p.StartWall(i).RunAll();
            p.StartCaps().RunAll();
            p.StartSheathing().RunAll();
            foreach (var j in p.FlashOpenings()) j.RunAll();
            foreach (var ro in new List<RoughOpening>(RoughOpening.All))
            {
                var unit = Piece.Create(ro.Spec.Product, 0f, ro.Center + ro.Outward * 1f, Quaternion.identity);
                RoughOpening.OnPlaced(unit);
                Assert.AreEqual(unit, ro.Installed, "unit set in " + ro.name);
            }
            int installed = 0;
            foreach (var j in new List<FramingJob>(FramingJob.All)) if (!j.Done && j.Title.StartsWith("Install")) { if (installed++ == 0) { j.TryAdvance(null, out _); j.TryAdvance(null, out _); } else j.RunAll(); }
            p.PitchRise = 6;
            p.StartRoof().RunAll();
            p.StartRoofSheathing().RunAll();
            p.StartDryIn().RunAll();
            var sg = p.StartShingles();
            int half = sg.Steps.Count / 2;
            for (int guard = 0; sg.Next < half && guard < 5000; guard++) Assert.IsTrue(sg.TryAdvance(null, out string why), why);
            BoomTruck.RemoveAll();
            yield return Frames(3);
            Physics.SyncTransforms();

            // a finished room inside: back wall + right wall drywalled and painted, cabinets on the back wall, LVP floor
            FinishStock.Unlimited = true;
            foreach (var id in new[] { "fin-drill", "level", "pull-bar" }) FinishStock.Give(id);
            var room = fp.World(fp.Length * 0.5f, fp.DeckTopY + 1.2f, fp.Width * 0.5f);
            var back = Insulation.FaceFrom(room, fp.AlongZ);                                                            // stud at 96" on the back wall
            var right = Insulation.FaceFrom(fp.World(fp.Length * 0.5f, fp.DeckTopY + 1.2f, fp.WallDepth + I(32f)), fp.AlongX);   // stud at 32" on the right wall
            var floor = Insulation.FaceFrom(room, Vector3.down);
            Assert.NotNull(back, "back wall face"); Assert.NotNull(right, "right wall face"); Assert.NotNull(floor, "floor face");
            Assert.IsTrue(back.IsWall && right.IsWall && floor.IsFloor);
            foreach (var w in new[] { back, right })
            {
                var prod = Drywall.Product("dw-12-4x12");
                foreach (var pl in Drywall.PlanLayout(w, prod)) Drywall.HangSheet(w, prod, pl);
                var s = Drywall.SurfaceFor(w);
                Assert.NotNull(s, "drywall on " + w);
                Drywall.CutOutBoxes(s); Drywall.ScrewAll(s);
                DrywallJob.Finish(s, "orange-peel", "Sage Green");
            }
            if (GameClock.I != null) GameClock.I.Hours = 11f;
            Physics.SyncTransforms();
            float startU = back.Bounds.xMin + I(30f);
            var run = Cabinets.InstallRun(back, startU, new[] { 24, 36, 30 }, new[] { 24, 36, 30 }, CabFinish.Oak, 0f, 1, true, false);
            Assert.AreEqual(6, run.Count, "cabinets: " + Game.ToastText);
            var ct = Countertops.Install(run, CounterType.Granite);
            Assert.NotNull(ct, "countertop");
            Physics.SyncTransforms();
            var area = Flooring.FindRoom(floor, room);
            var lvp = Lvp.Install(floor, area, null, "oak");
            Assert.NotNull(lvp, "LVP: " + Game.ToastText);
            FinishStock.Unlimited = false;
            yield return Frames(3);

            // --- what must come back
            var stock = Snap();
            var jobs = new List<string>(); foreach (var j in p.Jobs) jobs.Add(j.Title + " " + j.Next + "/" + j.Steps.Count);
            var info = p.Roof.GetComponent<RoofInfo>();
            bool framed = info.Framed, sheathed = info.Sheathed, driedIn = info.DriedIn, shingled = info.Shingled;
            int planes = info.Planes.Count;
            int dryMeshes = p.DryIn.GetComponentsInChildren<MeshRenderer>().Length, shingleMeshes = p.Shingles.GetComponentsInChildren<MeshRenderer>().Length;
            int shingleNext = p.Shingles.Next;
            var roles = new Dictionary<MemberRole, int>();
            foreach (var m in Object.FindObjectsByType<StructuralMember>(FindObjectsSortMode.None)) { roles.TryGetValue(m.Role, out int n); roles[m.Role] = n + 1; }
            int pieces = ActivePieces();
            int chalk = ChalkLine.Lines.FindAll(g => g != null && g.activeInHierarchy).Count;
            Assert.Greater(chalk, 10, "plate lines + shingle course lines snapped");
            int ros = RoughOpening.All.Count; int rosInstalled = RoughOpening.All.FindAll(r => r.Installed != null).Count;
            int shimmed = RoughOpening.All.FindAll(r => r.Shimmed).Count, taped = RoughOpening.All.FindAll(r => r.Taped).Count;
            int surfaces = Drywall.Surfaces.Count, cabs = CabinetUnit.All.Count, floors = FloorInstall.All.Count, lvpPieces = lvp.Pieces;
            float paintCov = Drywall.Surfaces[0].Coverage(false, 2);
            var truss = new List<int>(); foreach (var m in Object.FindObjectsByType<StructuralMember>(FindObjectsSortMode.None)) if (m.Role == MemberRole.Truss) truss.Add(m.GetComponentsInChildren<MeshFilter>().Length);
            Assert.Greater(truss.Count, 4, "trusses");

            yield return SaveWipeLoad();

            var p2 = ShellProject.Current;
            Assert.NotNull(p2, "shell project back");
            var jobs2 = new List<string>(); foreach (var j in p2.Jobs) jobs2.Add(j.Title + " " + j.Next + "/" + j.Steps.Count);
            CollectionAssert.AreEqual(jobs, jobs2, "every stage job back with the same progress");
            Assert.AreEqual(1, RoofInfo.All.Count, "roof info back");
            var info2 = RoofInfo.All[0];
            Assert.AreEqual(info2, p2.Roof.GetComponent<RoofInfo>());
            Assert.AreEqual(planes, info2.Planes.Count, "roof planes");
            Assert.AreEqual(framed, info2.Framed); Assert.AreEqual(sheathed, info2.Sheathed); Assert.AreEqual(driedIn, info2.DriedIn); Assert.AreEqual(shingled, info2.Shingled);
            Assert.IsTrue(info2.DriedIn && !info2.Shingled, "dried in, shingles half done");
            Assert.AreEqual(dryMeshes, p2.DryIn.GetComponentsInChildren<MeshRenderer>().Length, "drip edge / ice & water / underlayment meshes");
            Assert.AreEqual(shingleMeshes, p2.Shingles.GetComponentsInChildren<MeshRenderer>().Length, "chalk lines / starter / courses laid so far");
            Assert.AreEqual(shingleNext, p2.Shingles.Next, "shingle job resumes where it stopped");
            Assert.AreEqual(1, RoofAnchor.All.Count, "roof anchor");
            var roles2 = new Dictionary<MemberRole, int>();
            foreach (var m in Object.FindObjectsByType<StructuralMember>(FindObjectsSortMode.None)) { roles2.TryGetValue(m.Role, out int n); roles2[m.Role] = n + 1; }
            foreach (var kv in roles) { roles2.TryGetValue(kv.Key, out int n); Assert.AreEqual(kv.Value, n, "members with role " + kv.Key); }
            var truss2 = new List<int>(); foreach (var m in Object.FindObjectsByType<StructuralMember>(FindObjectsSortMode.None)) if (m.Role == MemberRole.Truss) truss2.Add(m.GetComponentsInChildren<MeshFilter>().Length);
            CollectionAssert.AreEquivalent(truss, truss2, "trusses come back as trusses (webs + chords), not boxes");
            Assert.AreEqual(pieces, ActivePieces(), "same number of pieces (none doubled, none lost)");
            Assert.AreEqual(0, DuplicatePieces(), "no doubled pieces");
            Assert.AreEqual(ros, RoughOpening.All.Count, "rough openings"); Assert.AreEqual(rosInstalled, RoughOpening.All.FindAll(r => r.Installed != null).Count, "units linked to their openings");
            Assert.AreEqual(shimmed, RoughOpening.All.FindAll(r => r.Shimmed).Count); Assert.AreEqual(taped, RoughOpening.All.FindAll(r => r.Taped).Count);
            Assert.AreEqual(WallState.Braced, p2.Walls[0].State, "walls standing + braced");
            Assert.AreEqual(surfaces, Drywall.Surfaces.Count, "drywall faces"); Assert.AreEqual(cabs, CabinetUnit.All.Count, "cabinets"); Assert.AreEqual(floors, FloorInstall.All.Count, "floor");
            Assert.AreEqual(lvpPieces, FloorInstall.All[0].Pieces, "LVP laid the same");
            Assert.AreEqual(paintCov, Drywall.Surfaces[0].Coverage(false, 2), 1e-4f, "two coats of paint");
            Assert.AreEqual(1, Countertop.All.Count);
            Assert.AreEqual(chalk, ChalkLine.Lines.FindAll(g => g != null && g.activeInHierarchy).Count, "chalk lines back once (not doubled by the replay)");
            AssertStockSame(stock, "shell");

            // the roofer carries on: the next shingle course goes on the restored roof
            Assert.IsTrue(p2.Shingles.TryAdvance(null, out string msg), msg);
            Assert.AreEqual(shingleNext + 1, p2.Shingles.Next);

            if (GameClock.I != null) GameClock.I.Hours = 11f;
            Light(fp.World(fp.Length * 0.45f, fp.DeckTopY + 2.2f, fp.Width * 0.45f), 1.1f, 9f);
            yield return Frames(3);
            SiteLookTests.Shot(fp.World(-4.5f, 5.5f, -6f), fp.World(fp.Length * 0.5f, 2.5f, fp.Width * 0.5f), "savegap2_roof_partial_shingles", 60f);
            SiteLookTests.Shot(fp.World(fp.Length * 0.25f, fp.DeckTopY + 1.55f, fp.Width * 0.18f), fp.World(fp.Length * 0.62f, fp.DeckTopY + 1.0f, fp.Width), "savegap2_roofed_room_interior", 72f);
        }

        // ================================================================== 3. drywall, paint, insulation

        [UnityTest]
        public IEnumerator DrywallPaintAndInsulation_SurviveSaveLoad()
        {
            FinishStock.Unlimited = true;
            var O = new Vector3(60f, Units.In(0.75f) + 0.002f, 140f);
            Vector3 In(float x, float y, float z) => O + new Vector3(I(x), I(y), -I(z));
            StudWall(O, Vector3.right, Vector3.back, 144f, new Opening { LeftIn = 88f, WidthIn = 38f, SillIn = 0f, HeadIn = 82.5f });
            Sheathing(O, Vector3.right, Vector3.back, 144f);
            CeilingJoists(O, Vector3.right, Vector3.back, 144f, 144f);
            Subfloor(O + Vector3.back * I(144f) + Vector3.down * 0.0005f, 144f, 144f);
            OutletBox(In(18f, 16f, 3.5f), Vector3.back);
            yield return Frames(3);
            Physics.SyncTransforms();
            var wall = Insulation.FaceFrom(In(32f, 48f, 30f), Vector3.forward);
            var ceil = Insulation.FaceFrom(In(32f, 60f, 40f), Vector3.up);
            Assert.NotNull(wall); Assert.NotNull(ceil); Assert.IsTrue(ceil.IsCeiling);
            Assert.Greater(Insulation.InsulateWall(wall, "ins-r13-k15", true, true), 4, "batts");
            Insulation.AirSeal(wall);
            Assert.NotNull(Insulation.InstallPoly(wall), "poly");
            var cs = Drywall.HangSurface(ceil, "dw-12-4x12");
            var ws = Drywall.HangSurface(wall, "dw-12-4x12");
            Assert.NotNull(cs); Assert.NotNull(ws);
            Drywall.CutOutBoxes(ws); Drywall.ScrewAll(cs); Drywall.ScrewAll(ws);
            DrywallJob.Finish(cs, "knockdown", "Ceiling White");              // ceiling: taped, 3 coats, sanded, knockdown, primed, 2 coats flat
            Drywall.CoatAll(ws, 1); FinishTime.Skip(25f); Drywall.CoatAll(ws, 2);   // wall: two coats, the second still wet
            // half the wall primed with a roller stroke + one stroke of paint (a texel layer that isn't uniform)
            PaintJob.Roll(ws, new Vector2(ws.Area.xMin + 0.6f, ws.Area.yMin + 1.2f), true, DrywallSurface.PrimerColor, Sheen.Flat);
            Assert.NotNull(Insulation.BlowAttic(ceil, 38f), "attic fill: " + Game.ToastText);
            Assert.Greater(Insulation.InstallRulers(ceil), 0, "attic rulers");
            FinishStock.Unlimited = false;
            if (GameClock.I != null) GameClock.I.Hours = 11f;
            yield return Frames(3);
            cs.Refresh(); ws.Refresh();

            var stock = Snap();
            int batts = Insulation.Batts.Count, stapled = Insulation.Batts.FindAll(b => b.Stapled).Count, split = Insulation.Batts.FindAll(b => b.CutAround).Count;
            int sealedBoxes = Insulation.SealedBoxes.Count, poly = Insulation.Poly.Count, rulers = Insulation.Rulers.Count;
            float attic = Insulation.Attics[0].AverageDepth;
            int looseBeads = 0; foreach (var fl in Object.FindObjectsByType<FinishLayer>(FindObjectsSortMode.None)) if (fl.transform.parent == null && fl.name.StartsWith("Bead_")) looseBeads++;
            int cSheets = cs.Sheets.Count, wSheets = ws.Sheets.Count, cScrews = cs.Screws.Count, wScrews = ws.Screws.Count, wHoles = 0; foreach (var sh in ws.Sheets) wHoles += sh.Holes.Count;
            int wCoats = 0, wWet = 0; foreach (var s in ws.Seams) foreach (var g in s.Segs) { wCoats += g.Coats; if (DrywallSurface.SegWet(g)) wWet++; }
            int cSanded = 0; foreach (var s in cs.Seams) foreach (var g in s.Segs) if (g.Sanded) cSanded++;
            float cPaint = cs.Coverage(false, 2), wPrimer = ws.Coverage(true, 1);
            string cTex = cs.TextureKind;
            Vector2 painted = cs.Area.center; cs.ToTexel(painted, out int px, out int py); Color cCol = cs.PaintColor[py * cs.W + px];
            Vector3 wallPt = wall.World(wall.Bounds.center), ceilPt = ceil.World(ceil.Bounds.center);

            yield return SaveWipeLoad();

            Assert.AreEqual(2, Drywall.Surfaces.Count, "both drywall faces");
            var cs2 = Drywall.Surfaces.Find(s => s.IsCeiling); var ws2 = Drywall.Surfaces.Find(s => !s.IsCeiling);
            Assert.NotNull(cs2); Assert.NotNull(ws2);
            Assert.Less(Vector3.Distance(ceilPt, cs2.Fp.World(cs2.Fp.Bounds.center)), 0.01f, "ceiling on the same face");
            Assert.AreEqual(cSheets, cs2.Sheets.Count, "ceiling sheets"); Assert.AreEqual(wSheets, ws2.Sheets.Count, "wall sheets");
            int wHoles2 = 0; foreach (var sh in ws2.Sheets) wHoles2 += sh.Holes.Count;
            Assert.AreEqual(wHoles, wHoles2, "door + box cut-outs");
            Assert.AreEqual(cScrews, cs2.Screws.Count, "ceiling screws"); Assert.AreEqual(wScrews, ws2.Screws.Count, "wall screws");
            int wCoats2 = 0, wWet2 = 0; foreach (var s in ws2.Seams) foreach (var g in s.Segs) { wCoats2 += g.Coats; if (DrywallSurface.SegWet(g)) wWet2++; }
            Assert.AreEqual(wCoats, wCoats2, "joint coats"); Assert.AreEqual(wWet, wWet2, "second coat still wet");
            int cSanded2 = 0; foreach (var s in cs2.Seams) foreach (var g in s.Segs) if (g.Sanded) cSanded2++;
            Assert.AreEqual(cSanded, cSanded2, "sanded joints");
            Assert.AreEqual(cTex, cs2.TextureKind, "texture");
            Assert.AreEqual(cPaint, cs2.Coverage(false, 2), 1e-4f, "ceiling paint coats"); Assert.AreEqual(wPrimer, ws2.Coverage(true, 1), 1e-4f, "primer stroke on the wall");
            cs2.ToTexel(painted, out px, out py);
            var c2 = cs2.PaintColor[py * cs2.W + px];
            Assert.Less(Mathf.Abs(c2.r - cCol.r) + Mathf.Abs(c2.g - cCol.g) + Mathf.Abs(c2.b - cCol.b), 1e-4f, "paint colour");
            Assert.AreEqual(batts, Insulation.Batts.Count, "batts"); Assert.AreEqual(stapled, Insulation.Batts.FindAll(b => b.Stapled).Count, "stapled");
            Assert.AreEqual(split, Insulation.Batts.FindAll(b => b.CutAround).Count, "split around the box");
            Assert.AreEqual(sealedBoxes, Insulation.SealedBoxes.Count, "air-sealed boxes"); Assert.AreEqual(poly, Insulation.Poly.Count, "poly");
            Assert.AreEqual(rulers, Insulation.Rulers.Count, "rulers");
            Assert.AreEqual(1, Insulation.Attics.Count); Assert.AreEqual(attic, Insulation.Attics[0].AverageDepth, 1e-4f, "blown-in depth");
            int looseBeads2 = 0; foreach (var fl in Object.FindObjectsByType<FinishLayer>(FindObjectsSortMode.None)) if (fl.transform.parent == null && fl.name.StartsWith("Bead_")) looseBeads2++;
            Assert.AreEqual(looseBeads, looseBeads2, "can-foam beads around the box");
            Assert.AreEqual(0.5f, Units.ToInches(FinishHooks.CoveringThickness(Drywall.Surfaces.Find(s => !s.IsCeiling).Fp, new Vector2(ws2.Fp.Bounds.xMin + 0.3f, ws2.Fp.Bounds.yMin + 1.2f))), 0.01f, "board thickness reported to other trades");
            AssertStockSame(stock, "drywall");

            // carry on: third coat on the wall once the second has dried
            FinishTime.Skip(25f);
            Assert.Greater(Drywall.CoatAll(ws2, 3), 0, "third coat on the restored joints");
            if (GameClock.I != null) GameClock.I.Hours = 11f;
            Light(In(64f, 84f, 100f), 0.9f, 10f);
            yield return Frames(3);
            cs2.Refresh(); ws2.Refresh();
            SiteLookTests.Shot(In(110f, 60f, 128f), In(52f, 52f, 0f), "savegap2_drywall_insulation", 66f);
        }

        // ================================================================== 4. interior finish

        [UnityTest]
        public IEnumerator InteriorFinish_SurviveSaveLoad()
        {
            if (GameClock.I != null) GameClock.I.Hours = 10f;
            FinishStock.Unlimited = true;
            foreach (var id in new[] { "finish-nailer", "brad-nailer", "miter-saw", "coping-saw", "flooring-nailer", "tile-saw", "notched-trowel", "grout-float", "pull-bar", "fin-drill", "caulk-gun", "level" }) Assert.NotNull(FinishStock.Give(id), id);
            var O = new Vector3(100f, 0.6f, 60f);
            Vector3 W(float x, float y, float z) => O + new Vector3(I(x), I(y), I(z));
            Subfloor(W(0, 0, -72), 192f, 240f);
            StudWall(O, Vector3.right, Vector3.forward, 144f, new Opening { LeftIn = 84f, WidthIn = 34f, SillIn = 0f, HeadIn = 82.5f });
            StudWall(W(0, 0, 3.5f), Vector3.forward, Vector3.right, 140.5f);
            CeilingJoists(O, Vector3.right, Vector3.forward, 144f, 144f);
            yield return Frames(3);
            Physics.SyncTransforms();
            FramePlane Face(Vector3 from, Vector3 dir) { var f = Insulation.FaceFrom(from, dir); Assert.NotNull(f, "face from " + from); return f; }
            var aRoom = Face(W(32, 40, 30), Vector3.back);
            var aHall = Face(W(32, 40, -30), Vector3.forward);
            var bRoom = Face(W(30, 40, 35.5f), Vector3.left);
            var ceil = Face(W(32, 40, 30), Vector3.up);
            var floor = Face(W(30, 30, 30), Vector3.down);
            Assert.IsTrue(floor.IsFloor && ceil.IsCeiling);
            DrywallJob.Complete(ceil, "dw-12-4x12", "smooth", "Ceiling White");
            foreach (var f in new[] { aRoom, aHall, bRoom }) DrywallJob.Complete(f, "dw-12-4x12", null, "Warm Greige");
            if (GameClock.I != null) GameClock.I.Hours = 10f;
            Physics.SyncTransforms();
            var ro = aRoom.Openings()[0];
            var door = DoorHanging.Hang(aRoom, ro, "door-int-6p32", true);
            Assert.NotNull(door, "door: " + Game.ToastText);
            Physics.SyncTransforms();
            float startU = bRoom.ToUV(W(3.5f, 0, 60)).x;
            var run = Cabinets.InstallRun(bRoom, startU, new[] { 18, 36, 24 }, new[] { 18, 36, 24 }, CabFinish.PaintedWhite, 0f, 1, true, true);
            Assert.AreEqual(6, run.Count);
            var ct = Countertops.Install(run, CounterType.Quartz);
            Assert.NotNull(Countertops.InstallBacksplash(ct, "Bright white"));
            Physics.SyncTransforms();
            Rect UV(FramePlane f, Vector3 a, Vector3 b) { var p = f.ToUV(a); var q = f.ToUV(b); return Rect.MinMaxRect(Mathf.Min(p.x, q.x), Mathf.Min(p.y, q.y), Mathf.Max(p.x, q.x), Mathf.Max(p.y, q.y)); }
            var tile = TileWork.Layout(floor, UV(floor, W(4, 0, 60), W(76, 0, 140)));
            Assert.IsTrue(TileWork.Backer(tile) && TileWork.Trowel(tile) && TileWork.Set(tile));
            FinishTime.Skip(24.1f); Assert.IsTrue(TileWork.Grout(tile, "Charcoal")); FinishTime.Skip(0.3f); Assert.IsTrue(TileWork.WipeHaze(tile));
            if (GameClock.I != null) GameClock.I.Hours = 10f;
            Physics.SyncTransforms();
            var lvp = Lvp.Install(floor, UV(floor, W(80, 0, 4), W(140, 0, 140)), null, "gray", -1f, 5);
            Assert.NotNull(lvp, "LVP: " + Game.ToastText);
            Physics.SyncTransforms();
            Assert.AreEqual(6, Casing.CaseDoor(door).Count, "casing");
            Physics.SyncTransforms();
            var bases = new List<TrimPiece>(); foreach (var f in new[] { aRoom, bRoom }) { bases.AddRange(WallTrim.RunBase(f)); yield return null; Physics.SyncTransforms(); }
            Assert.Greater(bases.Count, 2, "baseboard");
            WallTrim.NailAll();
            Assert.Greater(WallTrim.CaulkAll(), 5f, "caulked");
            FinishStock.Unlimited = false;
            yield return Frames(3);

            var stock = Snap();
            int cabs = CabinetUnit.All.Count; var cabPos = new List<Vector3>(); foreach (var c in CabinetUnit.All) cabPos.Add(c.transform.position);
            bool sink = Countertop.All[0].HasSink; Vector3 ctPos = Countertop.All[0].transform.position; int splashTiles = Backsplash.All[0].Tiles;
            int floors = 0; foreach (var f in Object.FindObjectsByType<FloorInstall>(FindObjectsSortMode.None)) floors++;
            int lvpPieces = lvp.Pieces, tiles = tile.Tiles; var tileStep = tile.Step; string grout = tile.GroutName;
            string doorId = door.DefId; float jamb = door.JambWidth; Vector3 through = door.Through, doorPos = door.transform.position;
            int trim = TrimPiece.All.Count, caulked = TrimPiece.All.FindAll(t => t.Caulked).Count, nails = 0; foreach (var t in TrimPiece.All) nails += t.Nails;
            int casings = door.Casings.Count;
            int caulkBeads = 0; foreach (var fl in Object.FindObjectsByType<FinishLayer>(FindObjectsSortMode.None)) if (fl.Kind == "fin-caulk") caulkBeads++;
            int doorPieces = 0; foreach (var pc in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (pc.Def.Id == doorId) doorPieces++;
            int pieces = ActivePieces();

            yield return SaveWipeLoad();

            Assert.AreEqual(cabs, CabinetUnit.All.Count, "cabinets");
            foreach (var cp in cabPos) Assert.IsTrue(CabinetUnit.All.Exists(c => Vector3.Distance(c.transform.position, cp) < 0.002f), "cabinet in place at " + cp);
            Assert.AreEqual(1, Countertop.All.Count); Assert.AreEqual(sink, Countertop.All[0].HasSink, "sink cut-out");
            Assert.Less(Vector3.Distance(ctPos, Countertop.All[0].transform.position), 0.002f, "countertop in place");
            Assert.AreEqual(1, Backsplash.All.Count); Assert.AreEqual(splashTiles, Backsplash.All[0].Tiles, "backsplash tiles");
            int floors2 = 0; foreach (var f in Object.FindObjectsByType<FloorInstall>(FindObjectsSortMode.None)) floors2++;
            Assert.AreEqual(floors, floors2, "floors");
            var lvp2 = FloorInstall.All.Find(f => f.Type == FloorType.Lvp);
            Assert.NotNull(lvp2); Assert.AreEqual(lvpPieces, lvp2.Pieces, "LVP laid the same"); Assert.AreEqual("gray", lvp2.LvpColor);
            Assert.AreEqual(1, TileFloor.All.Count); var tile2 = TileFloor.All[0];
            Assert.AreEqual(tileStep, tile2.Step, "tile stage"); Assert.AreEqual(tiles, tile2.Tiles, "tiles"); Assert.AreEqual(grout, tile2.GroutName, "grout colour");
            Assert.AreEqual(1, HungDoor.All.Count, "door"); var door2 = HungDoor.All[0];
            Assert.AreEqual(doorId, door2.DefId); Assert.AreEqual(jamb, door2.JambWidth, 1e-4f); Assert.Greater(Vector3.Dot(through, door2.Through), 0.99f, "swing side");
            Assert.Less(Vector3.Distance(doorPos, door2.transform.position), 0.002f);
            int doorPieces2 = 0; foreach (var pc in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (pc.Def.Id == doorId && pc.gameObject.activeInHierarchy) doorPieces2++;
            Assert.AreEqual(doorPieces, doorPieces2, "door unit not doubled");
            Assert.AreEqual(trim, TrimPiece.All.Count, "trim pieces"); Assert.AreEqual(caulked, TrimPiece.All.FindAll(t => t.Caulked).Count, "caulked trim");
            int nails2 = 0; foreach (var t in TrimPiece.All) nails2 += t.Nails;
            Assert.AreEqual(nails, nails2, "trim nails"); Assert.AreEqual(casings, door2.Casings.Count, "casings on the door");
            int caulkBeads2 = 0; foreach (var fl in Object.FindObjectsByType<FinishLayer>(FindObjectsSortMode.None)) if (fl.Kind == "fin-caulk" && fl.gameObject.activeInHierarchy) caulkBeads2++;
            Assert.AreEqual(caulkBeads, caulkBeads2, "painter's caulk beads");
            Assert.AreEqual(pieces, ActivePieces(), "same number of pieces");
            AssertStockSame(stock, "finish");

            if (GameClock.I != null) GameClock.I.Hours = 11f;
            Light(W(72, 88, 60), 0.6f, 7f); Light(W(30, 88, 100), 0.5f, 6f);
            yield return Frames(3);
            SiteLookTests.Shot(W(124, 64, 104), W(10, 40, 90), "savegap2_kitchen_finish", 62f);
        }

        // ================================================================== 5. cut stringers and layout marks

        [UnityTest]
        public IEnumerator CutStringersAndLayoutMarks_SurviveSaveLoad()
        {
            Assert.NotNull(FinishStock.Give("saw"), "circular saw");
            var r = StairMath.Calculate(56f);
            StairCalculator.UseForLayout(r);
            Assert.IsTrue(StairCalculator.HasLayout);
            var def = Catalog.Get(r.StockId);
            Assert.NotNull(def, "stringer stock " + r.StockId);
            var O = new Vector3(70f, 0.8f, 175f);
            var flat = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            var b1 = Piece.Create(def, def.Size.z, O, flat);
            var b2 = Piece.Create(def, def.Size.z, O + Vector3.right * 0.5f, flat);
            var b3 = Piece.Create(def, def.Size.z, O + Vector3.right * 1.0f, flat);
            var square = new FramingSquare();
            Assert.IsTrue(square.LayOut(b1, out string m1), m1); Assert.IsTrue(square.LayOut(b2, out string m2), m2); Assert.IsTrue(square.LayOut(b3, out string m3), m3);
            Assert.IsTrue(square.Cut(b1, out var s1, out m1), m1);
            Assert.IsTrue(square.Cut(b2, out var s2, out m2), m2);
            s2.transform.SetPositionAndRotation(O + new Vector3(0.6f, 0.6f, 1.4f), Quaternion.Euler(-30f, 0f, 0f));   // set in place on the stairwell
            s2.Fasten(); s2.Nails = 6;
            yield return Frames(2);

            var stock = Snap();
            Vector3 p1 = s1.transform.position, p2 = s2.transform.position; Quaternion q2 = s2.transform.rotation;
            int v1 = s1.transform.Find("Visual").GetComponent<MeshFilter>().sharedMesh.vertexCount;
            int risers = r.Risers; int pieces = ActivePieces();
            Vector3 p3 = b3.transform.position;

            yield return SaveWipeLoad();

            var cut = Object.FindObjectsByType<CutStringer>(FindObjectsSortMode.None);
            Assert.AreEqual(2, cut.Length, "two cut stringers back");
            CutStringer c1 = null, c2 = null;
            foreach (var c in cut) { if ((c.transform.position - p1).sqrMagnitude < 1e-6f) c1 = c; if ((c.transform.position - p2).sqrMagnitude < 1e-6f) c2 = c; }
            Assert.NotNull(c1, "loose stringer in place"); Assert.NotNull(c2, "installed stringer in place");
            Assert.AreEqual(risers, c1.Result.Risers, "layout kept");
            Assert.AreEqual(v1, c1.transform.Find("Visual").GetComponent<MeshFilter>().sharedMesh.vertexCount, "sawtooth mesh, not a plain board");
            var pc1 = c1.GetComponent<Piece>(); var pc2 = c2.GetComponent<Piece>();
            Assert.AreEqual(PieceState.Loose, pc1.State); Assert.AreEqual(PieceState.Fastened, pc2.State); Assert.AreEqual(6, pc2.Nails);
            Assert.Less(Quaternion.Angle(q2, c2.transform.rotation), 0.01f);
            Assert.AreEqual(MemberRole.Stringer, c1.GetComponent<StructuralMember>().Role);
            var marks = Object.FindObjectsByType<StringerLayoutMark>(FindObjectsSortMode.None);
            Assert.AreEqual(1, marks.Length, "the laid-out board keeps its pencil layout");
            Assert.Less(Vector3.Distance(p3, marks[0].transform.position), 1e-3f);
            Assert.NotNull(marks[0].transform.Find("StringerLayout"), "pencil lines drawn");
            Assert.AreEqual(pieces, ActivePieces(), "no plain 2x12 doubles");
            AssertStockSame(stock, "stairs");
            // and the marked board can still be cut after the load
            Assert.IsTrue(new FramingSquare().Cut(marks[0].GetComponent<Piece>(), out _, out string m4), m4);
            yield return Frames(2);
            SiteLookTests.Shot(O + new Vector3(0.5f, 1.6f, -1.6f), O + new Vector3(0.6f, 0.2f, 0.8f), "savegap2_stringers", 60f);
        }

        // ================================================================== 6. offcuts and partly used rolls

        static FieldInfo Priv(System.Type t, string name) => t.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        [UnityTest]
        public IEnumerator PipeOffcutsAndPartRolls_SurviveSaveLoad()
        {
            var pvc = RunSpecs.Get(PlumbCatalog.Pvc2);
            Assert.IsFalse(string.IsNullOrEmpty(pvc.StockId), "PVC is stock-tracked");
            Game.Stock.Add(pvc.StockId, 2);
            Assert.IsTrue(MepStock.Take(pvc, 3f * Units.Foot));
            float pvcFeet = MepStock.AvailableFeet(pvc);
            Priv(typeof(HouseWrap), "_tapeFeet").SetValue(null, 37.5f);    // seam tape left on the open roll
            Priv(typeof(HouseWrap), "_flashFeet").SetValue(null, 12.25f);  // flashing tape left on its roll
            Game.Stock.Add("rebar-4", 4); Game.Stock.Add("tiewire", 1);
            var bar = RebarSystem.AddBar(new Vector3(-6f, 0.05f, 30f), new Vector3(-6f + Ft(7f), 0.05f, 30f), "rebar-4");
            Assert.NotNull(bar);
            float openStick = ((Dictionary<string, float>)Priv(typeof(RebarSystem), "_openStick").GetValue(null))["rebar-4"];
            TrimStock.Offcuts["fin-trim-test"] = new List<float> { 0.42f, 1.1f };
            yield return Frames(2);

            var stock = Snap();
            Assert.IsTrue(SaveSystem.Save(Slot), SaveSystem.LastMessage);
            yield return WipeWorld();
            // a fresh session: every counter empty
            ((Dictionary<string, float>)Priv(typeof(MepStock), "_offcutFt").GetValue(null)).Clear();
            Priv(typeof(HouseWrap), "_tapeFeet").SetValue(null, 0f); Priv(typeof(HouseWrap), "_flashFeet").SetValue(null, 0f);
            ((Dictionary<string, float>)Priv(typeof(RebarSystem), "_openStick").GetValue(null)).Clear();
            TrimStock.Offcuts.Clear();
            Assert.IsTrue(SaveSystem.Load(Slot), SaveSystem.LastMessage);
            yield return Frames(3);

            Assert.AreEqual(pvcFeet, MepStock.AvailableFeet(pvc), 1e-3f, "7' PVC offcut kept");
            Assert.AreEqual(37.5f, (float)Priv(typeof(HouseWrap), "_tapeFeet").GetValue(null), 1e-4f, "seam tape left on the roll");
            Assert.AreEqual(12.25f, (float)Priv(typeof(HouseWrap), "_flashFeet").GetValue(null), 1e-4f, "flashing tape left");
            Assert.AreEqual(openStick, ((Dictionary<string, float>)Priv(typeof(RebarSystem), "_openStick").GetValue(null))["rebar-4"], 1e-4f, "rebar stick offcut");
            Assert.IsTrue(TrimStock.Offcuts.TryGetValue("fin-trim-test", out var offs) && offs.Count == 2 && Mathf.Abs(offs[1] - 1.1f) < 1e-5f, "trim offcuts");
            Assert.AreEqual(1, RebarSystem.Bars.Count, "loose bar back");
            AssertStockSame(stock, "leftovers");
            TrimStock.Offcuts.Remove("fin-trim-test");
        }

        // ================================================================== 7. New Game leaks

        [UnityTest]
        public IEnumerator NewGame_ResetsStairCalculatorShellPaintCartAndUnlimited()
        {
            yield return Frames(2);
            StairCalculator.UseForLayout(StairMath.Calculate(100f));
            Assert.IsTrue(StairCalculator.HasLayout);
            ShellProject.Start(Footprint.Make(new Vector3(150f, 0f, 60f), 0f, 12, 12)).StartFloor();
            PaintColors.Current = 7; PaintColors.CurrentSheen = Sheen.SemiGloss;
            var cart = (IDictionary)Priv(typeof(FinishSupply), "_cart").GetValue(null);
            cart["dw-12-4x8"] = 30;
            FinishStock.Unlimited = true;
            var pvc = RunSpecs.Get(PlumbCatalog.Pvc2);
            Game.Stock.Add(pvc.StockId, 1); MepStock.Take(pvc, 2f * Units.Foot);
            Priv(typeof(HouseWrap), "_tapeFeet").SetValue(null, 20f);
            var orphan = new GameObject("TapeLine"); orphan.AddComponent<LineRenderer>(); orphan.SetActive(false);

            MainMenu.NewGame(GameMode.FreeBuild);
            yield return Frames(3);

            Assert.IsFalse(StairCalculator.HasLayout, "stair calculator layout reset");
            Assert.IsNull(ShellProject.Current, "no shell project carried over");
            Assert.AreEqual(0, FramingJob.All.FindAll(j => j != null && j.Title.StartsWith("Floor deck")).Count, "old floor job gone");
            Assert.AreEqual(2, PaintColors.Current, "paint colour back to the default"); Assert.AreEqual(Sheen.Eggshell, PaintColors.CurrentSheen);
            Assert.AreEqual(0, cart.Count, "finish supply cart empty");
            Assert.IsFalse(FinishStock.Unlimited, "unlimited stock flag off");
            Assert.AreEqual(Game.Stock.Count(pvc.StockId) * pvc.FeetPerUnit, MepStock.AvailableFeet(pvc), 1e-3f, "no pipe offcut carried over");
            Assert.AreEqual(0f, (float)Priv(typeof(HouseWrap), "_tapeFeet").GetValue(null), "no tape roll carried over");
            Assert.IsTrue(orphan == null, "orphaned tape-measure line removed");
        }

        // ================================================================== old saves

        /// <summary>Version-2 (round 1) and version-1 saves, without the new sections, still load; the new parts come back empty.</summary>
        [UnityTest]
        public IEnumerator OldSavesWithoutRound2Sections_Load()
        {
            Game.Snap.GridIndex = 3; Game.Snap.Origin = Vector3.zero; Game.Snap.OriginYaw = 0f;
            foreach (var id in new[] { "2x6x12", "2x8x12", "2x10x12", "2x12x12" }) Game.Stock.Add(id, 6);
            Game.Stock.Add(FormSystem.StakeId, 20);
            StringAssert.StartsWith("Forms set", FormSystem.BuildRectangle(new Vector3(Ft(-26f), 0f, Ft(52f)), new Vector3(Ft(-22f), 0f, Ft(56f))));
            Assert.AreEqual(1, FormSystem.All.Count);
            var formArea = new Bounds(new Vector3(Ft(-24f), 0f, Ft(54f)), new Vector3(Ft(5f), 2f, Ft(5f)));
            var lumber = Lumber("2x4x8", Ft(8f), new Vector3(95f, 0.05f, 95f), Quaternion.LookRotation(Vector3.right, Vector3.up));
            yield return Frames(2);
            string[] round2 = { ShellSave.Key, ConcreteSave.Key, DrywallSave.Key, FinishSave.Key, StairSave.Key, LeftoversSave.Key };
            for (int version = 2; version >= 1; version--)
            {
                var file = SaveSystem.Capture();
                file.version = version;
                file.ext.RemoveAll(kv => System.Array.IndexOf(round2, kv.key) >= 0);
                if (version == 1) file.ext.RemoveAll(kv => kv.key == MepSave.Key || kv.key == ExteriorSave.Key || kv.key == SlabSave.Key);
                System.IO.Directory.CreateDirectory(SaveSystem.Dir);
                System.IO.File.WriteAllText(SaveSystem.PathFor(Slot), JsonUtility.ToJson(file));
                Assert.IsTrue(SaveSystem.Load(Slot), "version " + version + ": " + SaveSystem.LastMessage);
                yield return Frames(2);
                Assert.AreEqual(0, FormSystem.All.Count, "v" + version + ": no forms section -> forms cleared");
                Assert.IsNull(ShellProject.Current, "v" + version + ": no shell");
                Assert.AreEqual(0, Drywall.Surfaces.Count + CabinetUnit.All.Count, "v" + version + ": no interior");
                int boards = 0; foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (p.gameObject.activeInHierarchy && formArea.Contains(p.transform.position)) boards++;
                Assert.AreEqual(0, boards, "v" + version + ": no stray form boards");
                Assert.IsTrue(System.Array.Exists(Object.FindObjectsByType<Piece>(FindObjectsSortMode.None), p => p.Def.Id == "2x4x8" && (p.transform.position - new Vector3(95f, 0.05f, 95f)).sqrMagnitude < 1e-4f), "v" + version + ": plain pieces load");
            }
        }
    }
}
