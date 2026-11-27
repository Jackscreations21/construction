using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Groundbreak.Tests.FinishTestKit;

namespace Groundbreak.Tests
{
    /// <summary>The crew tasks CrewTests doesn't cover: a drywall hand hangs, screws, tapes, coats and sands a real framed
    /// room and a painter textures/primes/paints it; a laborer digs a footing trench on the pad; a landscaper lays and
    /// waters sod. Each runs worker NPCs through the public task API (Crew.AddTask) with Crew.IgnoreWorkHours and a
    /// speed multiplier, fast-forwarding the game clock while mud/texture/paint dries. Rooms + sod around (700, 0, -700);
    /// the trench is on the building pad (SiteGround is fixed at the origin) and the pad is restored afterwards.</summary>
    public class CrewTaskExtraTests
    {
        static readonly Vector3 Area = new Vector3(700f, 0f, -700f);

        int _money, _day; float _hours; bool _clockSaved;
        float[] _padH; GroundCover[] _padCover; bool[] _padComp; bool _madePad; float _bench;
        bool _madeField; int _sodAdded;
        readonly List<GameObject> _made = new List<GameObject>();

        void Setup(Vector3 at)
        {
            _money = Game.Wallet.Money;
            if (GameClock.I != null && !_clockSaved) { _day = GameClock.I.Day; _hours = GameClock.I.Hours; _clockSaved = true; }
            Company.ResetState(); Crew.ResetState(); CrewDefect.Log.Clear();
            GameSettings.Mode = GameMode.Career;
            Crew.IgnoreWorkHours = true; Crew.SpeedMultiplier = 20f;
            CrewManager.ArrivalOverride = at + new Vector3(-3f, 0, -3f);
            if (GameClock.I != null) GameClock.I.Hours = 8f;
            Weather.ForceRain = false;
            Site.Lot = new Rect(at.x - 15f, at.z - 15f, 30f, 30f);
            if (Game.Wallet.Money < 50000) Game.Wallet.Add(50000 - Game.Wallet.Money, "test payroll float");
            if (Game.Player != null)
            {
                var cc = Game.Player.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                Game.Player.transform.position = at + new Vector3(-12f, 0, -12f);
                if (cc != null) cc.enabled = true;
            }
        }

        static Worker HireTest(string uid, string trade, int skill)
        {
            var w = Company.Hire(new Worker { Uid = uid, Name = "Test " + uid, Trade = trade, Skill = skill, DailyWage = CrewTrades.Wage(trade, skill), Morale = 0.8f, AssignedJobId = Crew.SiteJobId });
            Crew.Info(w);
            return w;
        }

        /// <summary>Lets the crew work the task; while it waits (mud/texture/paint drying) the clock jumps ahead.</summary>
        static IEnumerator RunUntilDone(CrewTask task, float limitSeconds, float skipHours)
        {
            float nextSkip = 0f; int skips = 0;
            for (float t = 0; t < limitSeconds && !task.Done; t += Time.deltaTime)
            {
                if (!string.IsNullOrEmpty(task.Blocked) && Time.time >= nextSkip && skips < 30)
                {
                    FinishTime.Skip(skipHours);
                    task.RetryAt = 0f;
                    nextSkip = Time.time + 0.6f; skips++;
                }
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            Crew.ResetState(); Company.ResetState(); CrewDefect.Log.Clear();
            CrewManager.DespawnAll();
            Crew.IgnoreWorkHours = false; Crew.SpeedMultiplier = 1f; CrewManager.ArrivalOverride = null;
            GameSettings.Mode = GameMode.FreeBuild; Weather.ForceRain = null; Site.Lot = null; Inspections.RuleFilter = null;
            foreach (var s in Object.FindObjectsByType<DrywallSurface>(FindObjectsSortMode.None)) if (s != null) Object.Destroy(s.gameObject);
            foreach (var g in _made) if (g != null) Object.Destroy(g);
            _made.Clear();
            // sod + lot field
            foreach (var f in new List<LandscapeFeature>(LandscapeFeature.All))
                if (f != null && f.Kind == LandscapeKind.Sod && (f.A - (Area + new Vector3(30f, 0, 0))).sqrMagnitude < 100f) { LandscapeFeature.All.Remove(f); Object.Destroy(f.gameObject); }
            if (_sodAdded > 0) { Game.Stock.TryTake("sod-roll", Mathf.Min(_sodAdded, Game.Stock.Count("sod-roll"))); _sodAdded = 0; }
            if (_madeField) { Grade.Destroy(); _madeField = false; }
            // pad
            if (_padH != null && SiteGround.I != null)
            {
                var g = SiteGround.I;
                System.Array.Copy(_padH, g.H, _padH.Length); System.Array.Copy(_padCover, g.Cover, _padCover.Length); System.Array.Copy(_padComp, g.Compacted, _padComp.Length);
                g.Version++; g.Rebuild();
                SiteLayout.BenchmarkY = _bench;
                if (_madePad) Object.Destroy(g.gameObject);
            }
            _padH = null; _madePad = false;
            // clock + money
            if (GameClock.I != null && _clockSaved) { GameClock.I.Day = _day; GameClock.I.Hours = _hours; }
            _clockSaved = false;
            int d = _money - Game.Wallet.Money;
            if (d > 0) Game.Wallet.Add(d, "test restore"); else if (d < 0) Game.Wallet.TrySpend(-d, "test restore");
            yield return CleanScene();   // pieces, finish layers, test cameras, FinishStock.Unlimited, player spawn
        }

        // ------------------------------------------------------------------ drywall + paint

        [UnityTest]
        public IEnumerator CrewDrywallsThenPaintsRoom()
        {
            yield return Frames(5);
            Setup(Area);
            FinishStock.Unlimited = true;
            var O = Area + new Vector3(0f, 0.002f, 0f);   // outside corner of a 12' wall; the room is 12' x 12' toward -Z
            StudWall(O, Vector3.right, Vector3.back, 144f);
            CeilingJoists(O, Vector3.right, Vector3.back, 144f, 144f);
            yield return Frames(3);
            Physics.SyncTransforms();
            Vector3 center = O + new Vector3(Units.In(72f), 0f, -Units.In(72f));
            var faces = DrywallTask.RoomFaces(center);
            Assert.GreaterOrEqual(faces.Count, 2, "ceiling + wall seen from inside the room");
            Assert.IsTrue(faces[0].IsCeiling, "ceiling first");

            var hanger = HireTest("dwx5", "drywall", 5);
            var task = Crew.AddTask(new DrywallTask { A = center, Ref = "dw-12-4x12" });
            yield return RunUntilDone(task, 150f, 25f);
            Assert.IsTrue(task.Done, $"drywall task finished: {task.Status} ({task.UnitsDone} units)");
            var surfaces = new List<DrywallSurface>();
            foreach (var fp in faces)
            {
                var s = Drywall.SurfaceFor(fp);
                Assert.NotNull(s, "drywall hung on " + fp);
                surfaces.Add(s);
                Assert.Greater(s.Sheets.Count, 0, "sheets on " + fp);
                Assert.Greater(s.Screws.Count, 10, "screwed off " + fp);
                Assert.IsTrue(Drywall.AllCoated(s, 3, true), "taped + three coats, dry");
                Assert.IsTrue(Drywall.AllSanded(s), "sanded");
            }
            Assert.GreaterOrEqual(surfaces[0].Sheets.Count, 3, "12'x12' ceiling = three 4x12 sheets");
            Assert.Greater(Crew.Info(hanger).UnitsDone, 8);
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            foreach (var s in surfaces) s.Refresh();
            yield return Frames(3);
            Shot(Cam(center + new Vector3(-1.6f, 1.6f, -2.6f), O + new Vector3(Units.In(60f), 1.6f, 0f), 66f), "crewx_drywall_done");

            Crew.FireWorker(hanger);
            var painter = HireTest("ptx5", "painter", 5);
            var paint = Crew.AddTask(new PaintTask { A = center, Ref = "Sage Green" });
            yield return RunUntilDone(paint, 150f, 9f);
            Assert.IsTrue(paint.Done, $"paint task finished: {paint.Status} ({paint.UnitsDone} units)");
            foreach (var s in surfaces)
            {
                Assert.IsNotNull(s.TextureKind, "textured");
                Assert.Greater(s.Coverage(true, 1), 0.98f, "primed");
                Assert.Greater(s.Coverage(false, 2), 0.98f, "two coats of paint");
            }
            Assert.Greater(Crew.Info(painter).UnitsDone, 4);
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            foreach (var s in surfaces) s.Refresh();
            yield return Frames(3);
            Shot(Cam(center + new Vector3(-1.6f, 1.6f, -2.6f), O + new Vector3(Units.In(60f), 1.6f, 0f), 66f), "crewx_painted");
        }

        // ------------------------------------------------------------------ trench

        [UnityTest]
        public IEnumerator CrewDigsFootingTrench()
        {
            yield return Frames(5);
            if (SiteGround.I == null) { SiteGround.Ensure(); _madePad = true; }
            var g = SiteGround.I;
            _padH = (float[])g.H.Clone(); _padCover = (GroundCover[])g.Cover.Clone(); _padComp = (bool[])g.Compacted.Clone();
            _bench = SiteLayout.BenchmarkY;
            var at = new Vector3(-4.5f, 0f, 15.2f);           // back-left corner of the 60' pad, away from slabs/forms
            Setup(at);
            SiteLayout.BenchmarkY = g.HeightAt(at);
            var w = HireTest("labx5", "laborer", 5);
            var task = Crew.AddTask(new DigTask { A = at + new Vector3(-1.5f, 0, 0), B = at + new Vector3(1.5f, 0, 0), Radius = 0.15f, Depth = 0.3f });
            float before = g.HeightAt(at);
            yield return RunUntilDone(task, 120f, 1f);
            Assert.IsTrue(task.Done, $"trench dug: {task.Status} ({task.UnitsDone} units)");
            for (int i = 0; i < task.Stations; i++)
                Assert.LessOrEqual(task.GroundAt(i), task.TargetY + Units.In(1f), $"station {i} at plan depth");
            Assert.Greater(before - g.HeightAt(at), 0.25f, "a foot of dirt came out of the middle");
            Assert.AreEqual(0, task.Mistakes, "a skill-5 laborer digs to grade everywhere");
            Inspections.RuleFilter = r => r.Id == CrewRules.TrenchRuleId;
            var res = Inspections.Evaluate(InspectionPhase.Footing);
            Assert.IsTrue(res.Passed, "trench-depth rule passes");
            Assert.Greater(Crew.Info(w).UnitsDone, 5);
            g.Rebuild();
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            yield return Frames(3);
            Shot(Cam(at + new Vector3(-2.4f, 1.7f, -2.4f), at + new Vector3(0.3f, -0.2f, 0f), 60f), "crewx_trench");
        }

        // ------------------------------------------------------------------ sod

        [UnityTest]
        public IEnumerator CrewLaysAndWatersSod()
        {
            yield return Frames(5);
            var at = Area + new Vector3(30f, 0, 0);
            Setup(at);
            bool had = Grade.Field != null;
            if (!Grade.Ensure(at)) { Grade.Create(at.x - 20f, at.z - 20f, 80); _madeField = true; }
            else if (!had) _madeField = true;
            Assert.NotNull(Grade.Field);
            Grade.Field.PaintRect(at, new Vector2(2.6f, 2f), 0f, LandCover.Topsoil);   // stripped + topsoiled + raked
            Assert.AreEqual(LandCover.Topsoil, Grade.CoverAt(at.x, at.z));
            _sodAdded = 10; Game.Stock.Add("sod-roll", _sodAdded);
            int stock = Game.Stock.Count("sod-roll");
            var w = HireTest("lsx5", "landscaper", 5);
            // 2 rolls (5' long, along X) by 3 rows (2' wide): Yaw 90 lays the rolls' length along X
            var task = Crew.AddTask(new SodTask { A = at, B = new Vector3(Landscape.SodL + 0.01f, 0f, Landscape.SodW * 1.5f + 0.01f), Yaw = 90f });
            yield return RunUntilDone(task, 90f, 1f);
            Assert.IsTrue(task.Done, $"sod task finished: {task.Status} ({task.UnitsDone} units)");
            Assert.GreaterOrEqual(task.Laid, 3, "most of the 6 rolls laid");
            var sod = LandscapeFeature.All.FindAll(f => f != null && f.Kind == LandscapeKind.Sod && (f.A - at).sqrMagnitude < 16f);
            Assert.AreEqual(task.Laid, sod.Count, "one sod feature per roll laid");
            Assert.AreEqual(stock - task.Laid, Game.Stock.Count("sod-roll"), "rolls came out of site stock");
            foreach (var f in sod) Assert.GreaterOrEqual(f.WateredDay, 0, "watered in");
            _sodAdded = Mathf.Max(0, _sodAdded - task.Laid);
            Assert.Greater(Crew.Info(w).UnitsDone, 3);
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            yield return Frames(3);
            Shot(Cam(at + new Vector3(-3.2f, 2.2f, -3.2f), at, 58f), "crewx_sod");
        }
    }
}
