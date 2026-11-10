using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Groundbreak.Tests.FinishTestKit;

namespace Groundbreak.Tests
{
    /// <summary>Exterior envelope: house wrap + flashing, vinyl, brick veneer, fiber cement, board-and-batten,
    /// soffit/fascia/gutters, exterior trim + sealant, a deck, and the exterior inspection. Screenshots in Logs/shots/ext_*.</summary>
    public class ExteriorTests
    {
        static float I(float inches) => Units.In(inches);

        // ------------------------------------------------------------------ scene helpers

        /// <summary>OSB sheets (48" wide, full height) on the outer face of a stud wall, cut around rough openings
        /// (holes in inches: x from the wall start, y from the wall bottom).</summary>
        static List<Piece> SheathWithHoles(Vector3 start, Vector3 dir, Vector3 inward, float lengthIn, float heightIn, params Rect[] holesIn)
        {
            dir.Normalize(); inward.Normalize();
            var ps = new List<Piece>();
            float th = I(0.4375f);
            var rot = Quaternion.LookRotation(Vector3.up, -inward);
            var holes = new List<Rect>(holesIn);
            for (float x = 0; x < lengthIn - 0.5f; x += 48f)
            {
                var sheet = Rect.MinMaxRect(x, 0f, Mathf.Min(lengthIn, x + 48f), heightIn);
                foreach (var c in PanelMesh.Cells(sheet, holes))
                {
                    var p = Piece.Create(Catalog.Get("osb716"), I(c.height), start - inward * (th * 0.5f) + dir * I(c.center.x) + Vector3.up * I(c.center.y), rot);
                    p.SetSize(new Vector3(I(c.width), th, I(c.height)));
                    p.Fasten(); ps.Add(p);
                }
            }
            return ps;
        }

        /// <summary>Concrete foundation wall under a wall (visual only), optionally with a brick ledge out front.</summary>
        static GameObject Foundation(Vector3 start, Vector3 dir, float lengthIn, float heightM, float frontIn)
        {
            var b = new MeshBuilder();
            float len = I(lengthIn);
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            float depth = I(8f) + I(frontIn);
            Vector3 c = start + dir * (len * 0.5f) + Vector3.Cross(Vector3.up, dir) * 0f;
            // the wall's inward side is +Z for these tests; front of the foundation sits frontIn" outside the stud face
            var center = new Vector3(c.x, heightM * 0.5f - I(2f), start.z + (I(8f) - I(frontIn)) * 0.5f + I(0.4375f) * 0f);
            b.AddBox(center, new Vector3(len + I(1f), heightM + I(4f), depth), Quaternion.identity, 0);
            var go = new GameObject("TestFoundation");
            go.AddComponent<MeshFilter>().sharedMesh = b.Build("Foundation");
            go.AddComponent<MeshRenderer>().sharedMaterial = Materials.Get(SurfaceKind.Concrete);
            return go;
        }

        /// <summary>2x6 rafter tails (6/12) over a wall facing -Z with a 12" overhang, a 2x6 fascia and OSB roof deck.</summary>
        static Piece EaveFraming(Vector3 start, float lengthIn)
        {
            float plateTop = start.y + I(97.125f);
            float zOuter = start.z - I(0.4375f);
            float ang = Mathf.Atan(0.5f), sn = Mathf.Sin(ang), cs = Mathf.Cos(ang);
            var d = new Vector3(0f, -sn, -cs);           // down the slope, outward
            var n = new Vector3(0f, cs, -sn);            // perpendicular, upward
            float hIn = -24f, hOut = 12f;
            float slopeLen = I(hOut - hIn) / cs;
            Vector3 BottomAt(float h, float x) => new Vector3(x, plateTop - I(h) * 0.5f, zOuter - I(h));
            var rafRot = Quaternion.LookRotation(d, Vector3.Cross(d, n));
            for (float x = 0.75f; x <= lengthIn - 0.75f + 0.01f; x += 16f)
            {
                float xx = Mathf.Min(x, lengthIn - 0.75f);
                var mid = (BottomAt(hIn, start.x + I(xx)) + BottomAt(hOut, start.x + I(xx))) * 0.5f + n * I(2.75f);
                Lumber("2x6x12", slopeLen, mid, rafRot);
                if (xx >= lengthIn - 0.75f) break;
            }
            // fascia: plumb 2x6 touching the top corners of the square-cut tails
            Vector3 tailTop = BottomAt(hOut, 0f) + n * I(5.5f);
            float fTop = tailTop.y, fz = tailTop.z - I(0.75f);
            var fascia = Lumber("2x6x12", I(lengthIn), new Vector3(start.x + I(lengthIn) * 0.5f, fTop - I(2.75f), fz), Quaternion.LookRotation(Vector3.right, Vector3.Cross(Vector3.right, Vector3.up)));
            // roof deck
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

        static Piece Window(Vector3 start, Opening o, string id)
        {
            var def = Catalog.Get(id);
            var c = start + Vector3.right * I(o.LeftIn + o.WidthIn * 0.5f) + Vector3.up * I((o.SillIn + o.HeadIn) * 0.5f) + Vector3.forward * I(3.25f * 0.5f - 0.75f - 0.4375f);
            var p = Piece.Create(def, 0f, c, Quaternion.LookRotation(Vector3.back, Vector3.up));
            p.Fasten();
            return p;
        }

        static FramePlane SheathFace(Vector3 start, float uIn, float vIn)
        {
            var ray = new Ray(start + Vector3.right * I(uIn) + Vector3.up * I(vIn) + Vector3.back * 1f, Vector3.forward);
            Assert.IsTrue(Physics.Raycast(ray, out var hit, 2f), "no sheathing hit");
            var fp = FramePlane.FromHit(hit);
            Assert.NotNull(fp, "no frame plane on sheathing");
            return fp;
        }

        static List<CloseupInspection.Violation> Near(CloseupInspection.Result r, ExteriorWall w, float grow = 1.5f)
        {
            var b = w.Face.WorldBounds(); b.Expand(grow);
            var l = new List<CloseupInspection.Violation>();
            foreach (var v in r.Violations) if (b.Contains(v.Position)) l.Add(v);
            return l;
        }

        static string Dump(List<CloseupInspection.Violation> l)
        {
            var s = ""; foreach (var v in l) s += $"\n  [{v.RuleId}] {v.Message}"; return s;
        }

        static IEnumerator Setup()
        {
            yield return Frames(5);
            FinishStock.Unlimited = true;
            if (GameClock.I != null) GameClock.I.Hours = 10f;
            yield return Frames(2);
        }

        // ------------------------------------------------------------------ cleanup (other suites measure the whole scene)

        HashSet<GameObject> _before;

        [UnitySetUp]
        public IEnumerator Snapshot()
        {
            yield return null;
            _before = new HashSet<GameObject>(UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects());
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            CloseupInspection.ClearMarkers();
            foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (_before != null && !_before.Contains(g) && g.GetComponent<PlayerRig>() == null) Object.Destroy(g);
            FinishStock.Unlimited = false;
            yield return null;
        }

        // ------------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator VinylWallWithWrapEavesAndInspection()
        {
            yield return Setup();
            var O = new Vector3(60f, I(8f), 0f);
            var ro = new Opening { LeftIn = 48f, WidthIn = 36.5f, SillIn = 36f, HeadIn = 84.5f };
            StudWall(O, Vector3.right, Vector3.forward, 144f, ro);
            SheathWithHoles(O, Vector3.right, Vector3.forward, 144f, 97.125f, new Rect(48f, 36f, 36.5f, 48.5f));
            Foundation(O, Vector3.right, 144f, I(8f), 0.25f);
            var fascia = EaveFraming(O, 144f);
            yield return Frames(3);
            Physics.SyncTransforms();

            var w = ExteriorWall.For(SheathFace(O, 20f, 60f));
            Assert.NotNull(w, "exterior wall");
            Debug.Log($"[Exterior] wall {w.Face} studs {w.Studs} openings {w.Openings.Count} studlines {w.StudLines.Count} depth {Units.Format(w.Depth)}");
            Assert.AreEqual(1, w.Openings.Count, "window rough opening found through the sheathing");
            Assert.AreEqual(36.5f, Units.ToInches(w.Openings[0].width), 0.3f);
            Assert.GreaterOrEqual(w.StudLines.Count, 9);

            // --- house wrap with the in-game cap stapler (tool path), then the scripted steps
            Assert.NotNull(FinishStock.Give("cap-stapler"));
            var stapler = (CapStaplerTool)FinishStock.Give("cap-stapler");
            Game.Belt.Select(Game.Belt.IndexOf(stapler));
            AimPlayerAt(O + new Vector3(I(20f), -I(8f), -2.2f), O + new Vector3(I(20f), 1.2f, -I(0.44f)));
            yield return Frames(3);
            stapler.Mode = 0; stapler.Primary();
            Assert.AreEqual(1, w.Wrap.Sheets.Count, "cap stapler wrapped a course");
            Assert.IsTrue(HouseWrap.Covered(w), "one 9' course covers an 8' wall");
            Assert.IsTrue(HouseWrap.TapeSeams(w));
            Assert.IsTrue(HouseWrap.CutOpening(w, 0));
            Assert.IsFalse(HouseWrap.FlashJambs(w, 0), "jambs before sill is refused (shingle order)");
            Assert.IsTrue(HouseWrap.FlashSill(w, 0));
            yield return Frames(2);
            var camW = Cam(O + new Vector3(I(66f), 1.5f, -3.6f), O + new Vector3(I(66f), 1.2f, 0f));
            Shot(camW, "ext_wrap_icut");
            Window(O, ro, "win-sh-3648");
            Assert.IsTrue(HouseWrap.FlashJambs(w, 0));
            Assert.IsTrue(HouseWrap.FlashHead(w, 0));
            yield return Frames(2);
            Shot(camW, "ext_wrapped_wall");

            // --- eave: soffit + fascia wrap before the siding (siding tops out under the soffit)
            var e = Eaves.For(fascia);
            Assert.NotNull(e, "eave from fascia");
            Debug.Log($"[Exterior] eave L={Units.Format(e.L)} overhang={Units.Format(e.FrontOff - e.WallOff)} wall={(e.Wall == w)}");
            Assert.AreEqual(w, e.Wall, "eave found the wall below");
            Assert.IsFalse(Eaves.WrapFascia(e), "fascia wrap needs the soffit first");
            Assert.IsTrue(Eaves.InstallSoffit(e));
            Assert.IsTrue(Eaves.WrapFascia(e));

            // --- vinyl: sequence enforcement, then everything
            Assert.IsNull(VinylSiding.HangCourse(w), "panels before starter/posts/J are refused");
            Assert.IsTrue(VinylSiding.InstallAll(w, "Sage"), "vinyl install: " + Game.ToastText);
            var vs = w.Vinyl;
            Debug.Log($"[Exterior] vinyl courses {vs.Courses.Count}, top limit {Units.Format(vs.TopLimit)}");
            Assert.GreaterOrEqual(vs.Courses.Count, 10);
            foreach (var c in vs.Courses) if (!c.SnapLocked) Assert.Greater(c.Nails.Count, 3, "course nailed at studs");
            Assert.IsTrue(Eaves.HangGutter(e));
            Assert.IsTrue(Eaves.AddDownspout(e));
            Assert.Greater(ExteriorTrim.CaulkAll(w), 0, "window perimeter sealed");
            yield return Frames(3);

            var camC = Cam(O + new Vector3(I(4f), 1.5f, -1.5f), O + new Vector3(I(40f), 1.62f, 0f), 55f);
            Shot(camC, "ext_vinyl_closeup");
            var camO = Cam(O + new Vector3(I(40f), 1.7f, -5.2f), O + new Vector3(I(72f), 1.45f, 0f));
            Shot(camO, "ext_vinyl_overview");
            var camE = Cam(O + new Vector3(I(150f), 1.4f, -1.6f), O + new Vector3(I(110f), 2.35f, -0.25f), 60f);
            Shot(camE, "ext_eave_gutter");

            // --- inspection: clean
            var res = CloseupInspection.Run(CloseupInspection.Exterior);
            var mine = Near(res, w);
            Assert.AreEqual(0, mine.Count, "vinyl wall should pass:" + Dump(mine));

            // --- a tight, off-center nail: flagged; zip it out and re-fit
            var c3 = vs.Courses[3];
            float p0 = c3.Panels[0].U0 + VinylSiding.SlotPitch * 0.5f;
            float badU = p0 + Mathf.Round((w.StudLines[4] - p0) / VinylSiding.SlotPitch) * VinylSiding.SlotPitch + I(0.45f);
            Assert.IsTrue(VinylSiding.NailAt(w, c3, badU, false, true), "freehand tight nail");
            res = CloseupInspection.Run(CloseupInspection.Exterior);
            mine = Near(res, w);
            Assert.IsTrue(mine.Exists(v => v.RuleId == "ext-vinyl-nail-tight"), "tight nail flagged" + Dump(mine));
            Assert.IsTrue(mine.Exists(v => v.RuleId == "ext-vinyl-nail-slot"), "off-center nail flagged" + Dump(mine));
            Assert.Greater(CloseupInspection.MarkerCount(CloseupInspection.Exterior), 0, "red markers");
            Shot(camC, "ext_vinyl_inspection_flag");
            Assert.IsTrue(VinylSiding.Unzip(w, 3));
            Assert.IsFalse(VinylSiding.Remove(w, 3), "course above must be unzipped to reach the nails");
            Assert.IsTrue(VinylSiding.Unzip(w, 4));
            yield return Frames(1);
            Shot(camC, "ext_vinyl_unzipped");
            Assert.IsTrue(VinylSiding.Remove(w, 3));
            Assert.IsTrue(VinylSiding.Refit(w, 3));
            Assert.IsTrue(VinylSiding.NailCourse(w, 3));
            Assert.IsTrue(VinylSiding.Relock(w, 4));
            res = CloseupInspection.Run(CloseupInspection.Exterior);
            mine = Near(res, w);
            Assert.AreEqual(0, mine.Count, "re-fitted course passes:" + Dump(mine));
            CloseupInspection.ClearMarkers();
        }

        [UnityTest]
        public IEnumerator BrickVeneerWall()
        {
            yield return Setup();
            var O = new Vector3(66f, I(8f), 12f);
            var ro = new Opening { LeftIn = 48f, WidthIn = 36.5f, SillIn = 36f, HeadIn = 84.5f };
            StudWall(O, Vector3.right, Vector3.forward, 144f, ro);
            SheathWithHoles(O, Vector3.right, Vector3.forward, 144f, 97.125f, new Rect(48f, 36f, 36.5f, 48.5f));
            Foundation(O, Vector3.right, 144f, I(8f), 6f);
            yield return Frames(3);
            Physics.SyncTransforms();
            var w = ExteriorWall.For(SheathFace(O, 20f, 60f));
            Assert.AreEqual(1, w.Openings.Count);
            Assert.IsFalse(BrickVeneer.LayCourse(w), "no brick before the house wrap");
            Assert.IsTrue(HouseWrap.WrapWall(w));
            Window(O, ro, "win-sh-3648");
            Assert.IsFalse(BrickVeneer.LayCourse(w), "first course needs base flashing");
            Assert.IsTrue(BrickVeneer.BaseFlashing(w));
            for (int i = 0; i < 4; i++) Assert.IsTrue(BrickVeneer.LayCourse(w), "course " + i);
            Assert.IsFalse(BrickVeneer.LayCourse(w), "tie row due at course 5");
            Assert.IsTrue(BrickVeneer.TieRow(w));
            for (int i = 0; i < 5; i++) Assert.IsTrue(BrickVeneer.LayCourse(w));
            yield return Frames(2);
            var camP = Cam(O + new Vector3(I(30f), 1.0f, -1.6f), O + new Vector3(I(40f), 0.45f, 0f), 55f);
            Shot(camP, "ext_brick_progress");
            Assert.IsTrue(BrickVeneer.LayAll(w));
            Assert.IsTrue(w.Brick.Complete);
            Debug.Log($"[Exterior] brick courses {w.Brick.Courses}, tie rows {w.Brick.TieRows.Count}, ties {w.Brick.Ties.Count}, weeps {w.Brick.Weeps.Count}, mortar bags {w.Brick.MortarUsed:F1}");
            Assert.GreaterOrEqual(w.Brick.Weeps.Count, 3);
            Assert.GreaterOrEqual(w.Brick.TieRows.Count, 4);
            Assert.IsTrue(w.Brick.Lintels.ContainsKey(0), "lintel over the window");
            ExteriorTrim.CaulkAll(w);
            yield return Frames(3);
            Shot(Cam(O + new Vector3(I(26f), 1.5f, -1.15f), O + new Vector3(I(52f), 1.0f, 0f), 55f), "ext_brick_closeup");
            Shot(Cam(O + new Vector3(I(72f), 1.6f, -4.8f), O + new Vector3(I(72f), 1.3f, 0f)), "ext_brick_overview");
            var res = CloseupInspection.Run(CloseupInspection.Exterior);
            var mine = Near(res, w);
            Assert.AreEqual(0, mine.Count, "brick wall passes:" + Dump(mine));
            // air space too small is flagged
            w.Brick.Air = I(0.5f);
            res = CloseupInspection.Run(CloseupInspection.Exterior);
            Assert.IsTrue(Near(res, w).Exists(v => v.RuleId == "ext-brick-airspace"));
            w.Brick.Air = I(1f);
            CloseupInspection.ClearMarkers();
        }

        [UnityTest]
        public IEnumerator FiberCementAndBoardBatten()
        {
            yield return Setup();
            var O = new Vector3(80f, I(8f), 24f);
            var ro = new Opening { LeftIn = 64f, WidthIn = 36.5f, SillIn = 36f, HeadIn = 84.5f };
            StudWall(O, Vector3.right, Vector3.forward, 192f, ro);
            SheathWithHoles(O, Vector3.right, Vector3.forward, 192f, 97.125f, new Rect(64f, 36f, 36.5f, 48.5f));
            Foundation(O, Vector3.right, 192f, I(8f), 0.25f);
            var O2 = new Vector3(O.x + 6.5f, I(8f), 24f);
            StudWall(O2, Vector3.right, Vector3.forward, 96f);
            Sheathing(O2, Vector3.right, Vector3.forward, 96f);
            Foundation(O2, Vector3.right, 96f, I(8f), 0.25f);
            yield return Frames(3);
            Physics.SyncTransforms();

            var w = ExteriorWall.For(SheathFace(O, 20f, 60f));
            Assert.IsTrue(HouseWrap.WrapWall(w));
            Window(O, ro, "win-sh-3648");
            Assert.IsNull(FiberCement.HangCourse(w), "no planks before starter/trim");
            Assert.IsTrue(FiberCement.InstallAll(w, "Iron gray"));
            Debug.Log($"[Exterior] fiber cement courses {w.FC.Courses.Count}, joints {w.FC.Courses.ConvertAll(c => c.Joints.Count).Sum()}");
            Assert.GreaterOrEqual(w.FC.Courses.Count, 12);
            bool anyJoint = false; foreach (var c in w.FC.Courses) if (c.Joints.Count > 0) anyJoint = true;
            Assert.IsTrue(anyJoint, "16' wall needs butt joints");
            // joints are staggered
            for (int k = 1; k < w.FC.Courses.Count; k++)
                foreach (var u in w.FC.Courses[k].Joints)
                    foreach (var u0 in w.FC.Courses[k - 1].Joints) Assert.Greater(Mathf.Abs(u - u0), I(15f), "staggered joints");
            Assert.Greater(ExteriorTrim.CaulkAll(w), 0);

            var w2 = ExteriorWall.For(SheathFace(O2, 20f, 60f));
            Assert.IsTrue(HouseWrap.WrapWall(w2));
            Assert.IsTrue(BoardBatten.InstallAll(w2, "Evening blue"));
            Assert.Greater(ExteriorTrim.CaulkAll(w2), 0);
            yield return Frames(3);
            Shot(Cam(O + new Vector3(I(30f), 1.5f, -1.4f), O + new Vector3(I(64f), 1.2f, 0f), 55f), "ext_fibercement_closeup");
            Shot(Cam(O + new Vector3(I(96f), 1.7f, -5.5f), O + new Vector3(I(96f), 1.3f, 0f)), "ext_fibercement");
            Shot(Cam(O2 + new Vector3(I(90f), 1.5f, -2.0f), O2 + new Vector3(I(44f), 1.3f, 0f), 55f), "ext_boardbatten");
            var res = CloseupInspection.Run(CloseupInspection.Exterior);
            var mine = Near(res, w); mine.AddRange(Near(res, w2));
            Assert.AreEqual(0, mine.Count, "fiber cement + B&B pass:" + Dump(mine));
            CloseupInspection.ClearMarkers();
        }

        [UnityTest]
        public IEnumerator DeckAgainstWall()
        {
            yield return Setup();
            float fy = I(24f);
            var R = new Vector3(100f, fy, 36f);
            // floor framing stand-in: a 2x10 band (rim) under the wall, flush with the stud face, on a foundation
            Lumber(ExteriorCatalog.PT2x10, I(240f) * 0.6f, R + new Vector3(I(72f), I(4.625f), I(0.75f)), Quaternion.LookRotation(Vector3.right, Vector3.Cross(Vector3.right, Vector3.up)));
            Lumber(ExteriorCatalog.PT2x10, I(240f) * 0.4f, R + new Vector3(I(144f + 48f), I(4.625f), I(0.75f)), Quaternion.LookRotation(Vector3.right, Vector3.Cross(Vector3.right, Vector3.up)));
            var O = new Vector3(R.x, fy + I(10f), R.z);
            StudWall(O, Vector3.right, Vector3.forward, 240f);
            SheathWithHoles(R, Vector3.right, Vector3.forward, 240f, 107.125f);
            Foundation(new Vector3(R.x, fy, R.z), Vector3.right, 240f, fy, 0.25f);
            yield return Frames(3);
            Physics.SyncTransforms();
            var w = ExteriorWall.For(SheathFace(R, 30f, 50f));
            Assert.IsTrue(HouseWrap.WrapWall(w));
            var d = DeckBuilder.Plan(w, w.B.xMin + I(24f), 192f, 120f, fy + I(9.25f));
            Assert.IsTrue(DeckBuilder.NextStage(d), "ledger");
            Debug.Log($"[Exterior] ledger lags {d.Lags.Count}, in framing {d.LagsInFraming}");
            Assert.AreEqual(d.Lags.Count, d.LagsInFraming, "lags hit the rim");
            Assert.IsTrue(DeckBuilder.BuildAll(d));
            Assert.IsTrue(d.Done);
            Assert.GreaterOrEqual(d.GuardHeight, I(36f));
            Assert.Less(d.BalusterGap, I(4f));
            yield return Frames(3);
            Shot(Cam(R + new Vector3(I(-40f), 2.6f, -5.6f), R + new Vector3(I(110f), 0.6f, -1.4f), 58f), "ext_deck");
            Shot(Cam(R + new Vector3(I(40f), -0.2f, -1.7f), R + new Vector3(I(70f), 0.05f, 0f), 62f), "ext_deck_ledger");
            var res = CloseupInspection.Run(CloseupInspection.Exterior);
            var mine = new List<CloseupInspection.Violation>();
            foreach (var v in res.Violations) if (v.RuleId.StartsWith("ext-deck") && Vector3.Distance(v.Position, R) < 12f) mine.Add(v);
            Assert.AreEqual(0, mine.Count, "deck passes:" + Dump(mine));
            CloseupInspection.ClearMarkers();
        }
    }

    static class ListExt
    {
        public static int Sum(this List<int> l) { int s = 0; foreach (var x in l) s += x; return s; }
    }
}
