using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Crew stage: hiring and payroll, worker NPCs fastening and framing through the real APIs, a low-skill
    /// mistake caught by the framing inspection, and the crew save round-trip. Test area around (420, 0, 420).</summary>
    public class CrewTests
    {
        static readonly Vector3 Area = new Vector3(420f, 0f, 420f);
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        readonly List<GameObject> _made = new List<GameObject>();

        static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
            cam.targetTexture = prev; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        Camera Cam(Vector3 pos, Vector3 look)
        {
            var cam = new GameObject("TestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = 55; cam.nearClipPlane = 0.03f;
            _made.Add(cam.gameObject);
            return cam;
        }

        void Setup(Vector3 at)
        {
            Company.ResetState(); Crew.ResetState(); CrewDefect.Log.Clear();
            GameSettings.Mode = GameMode.Career;
            Crew.IgnoreWorkHours = true; Crew.SpeedMultiplier = 10f;
            CrewManager.ArrivalOverride = at + new Vector3(-3f, 0, -3f);
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            Weather.ForceRain = false;
            Site.Lot = new Rect(at.x - 15f, at.z - 15f, 30f, 30f);
            if (Game.Player != null) Game.Player.transform.position = at + new Vector3(-12f, 0, -12f);
        }

        Worker HireTest(string uid, string trade, int skill)
        {
            var w = Company.Hire(new Worker { Uid = uid, Name = "Test " + uid, Trade = trade, Skill = skill, DailyWage = CrewTrades.Wage(trade, skill), Morale = 0.8f, AssignedJobId = Crew.SiteJobId });
            Crew.Info(w);
            return w;
        }

        List<Piece> LayLooseFrame(Vector3 at, int n)
        {
            // a deck-style frame of 2x4s on blocks, set by hand and not nailed yet
            var def = Catalog.Get("2x4x8");
            var list = new List<Piece>();
            for (int i = 0; i < n; i++)
            {
                var p = Piece.Create(def, 0, at + new Vector3(i * Units.In(16f), Units.In(0.75f), 0), Quaternion.identity);
                list.Add(p); _made.Add(p.gameObject);
            }
            return list;
        }

        [TearDown]
        public void Teardown()
        {
            Crew.ResetState(); Company.ResetState(); CrewDefect.Log.Clear();
            Crew.IgnoreWorkHours = false; Crew.SpeedMultiplier = 1f; CrewManager.ArrivalOverride = null;
            GameSettings.Mode = GameMode.FreeBuild; Weather.ForceRain = null; Site.Lot = null; Inspections.RuleFilter = null;
            FramingJob.RequireStock = true; FramingJobRunner.Instant = false;
            foreach (var g in _made) if (g != null) Object.Destroy(g);
            _made.Clear();
        }

        [UnityTest]
        public IEnumerator HiringChargesPayrollOnNewDayAndReachesCrewStage()
        {
            yield return Frames(5);
            Setup(Area);
            Crew.IgnoreWorkHours = false;
            Crew.RefreshMarket(GameClock.I.Day);
            Assert.AreEqual(Crew.MarketSize, Crew.Market.Count, "labor market is stocked");
            foreach (var c in Crew.Market)
            {
                Assert.That(c.Skill, Is.InRange(1, 5));
                Assert.That(c.Wage, Is.InRange(100, 450), "realistic US daily wage");
            }
            Assert.Greater(CrewTrades.Wage("electrician", 5), CrewTrades.Wage("laborer", 1));

            Company.HasWarehouse = true; Company.WarehouseLevel = 1; Company.AddVehicle("pickup", "F-250");
            var a = Crew.HireCandidate(Crew.Market[0], out _);
            Assert.AreEqual(CompanyStage.Contractor, Company.Stage);
            var b = Crew.HireCandidate(Crew.Market[0], out _);
            Assert.NotNull(a); Assert.NotNull(b);
            Assert.AreEqual(CompanyStage.Crew, Company.Stage, "2 workers + warehouse + truck = Crew stage");
            Crew.Info(b).Overtime = true;
            int payroll = a.DailyWage + Mathf.RoundToInt(b.DailyWage * Crew.OvertimePayFactor);
            Assert.AreEqual(payroll, Crew.Payroll());

            int money = Game.Wallet.Money;
            yield return null;
            int day = GameClock.I.Day;
            Company.Tick(); // establishes today
            GameClock.I.Day = day + 1;
            yield return Frames(2);
            Assert.Less(Game.Wallet.Money, money - payroll + 1, "payroll was charged on the new day");
            Assert.IsTrue(Game.Wallet.Log.Exists(l => l.Contains("Crew payroll") && l.Contains(payroll.ToString())), "wallet log has the payroll line");
            Assert.Less(b.Morale, 0.8f, "overtime wears on morale");
            Assert.AreEqual(day + 1, Crew.MarketDay, "market refreshed for the new day");

            // training: one level up the next morning
            int skill = a.Skill;
            if (skill < 5)
            {
                Assert.IsTrue(Crew.Train(a, out var m), m);
                GameClock.I.Day = day + 2;
                yield return Frames(2);
                Assert.AreEqual(skill + 1, a.Skill);
            }
            GameClock.I.Day = day;
        }

        [UnityTest]
        public IEnumerator WorkerFastensPlacedFraming()
        {
            yield return Frames(5);
            var at = Area + new Vector3(0, 0, 0);
            Setup(at);
            var pieces = LayLooseFrame(at, 8);
            int nails = Game.Stock.Count("nails16d");
            if (nails < 50) { Game.Stock.Add("nails16d", 500); nails = Game.Stock.Count("nails16d"); }
            var w = HireTest("carp5", "carpenter", 5);
            Crew.AddTask(new FastenTask { A = at, Radius = 10f });
            var cam = Cam(at + new Vector3(-2.2f, 1.9f, -2.6f), at + new Vector3(2.5f, 0.4f, 0.5f));
            bool shot = false;
            for (float t = 0; t < 40f && pieces.Exists(p => p.State == PieceState.Loose); t += Time.deltaTime)
            {
                var npc = CrewManager.NpcFor(w);
                if (!shot && npc != null && npc.WorkElapsed > 0.06f && pieces.FindAll(p => p.State == PieceState.Fastened).Count >= 3)
                { Shot(cam, "crew_fastening"); shot = true; }
                yield return null;
            }
            foreach (var p in pieces) { Assert.AreEqual(PieceState.Fastened, p.State, p.name + " fastened"); Assert.GreaterOrEqual(p.Nails, 2); }
            Assert.AreEqual(nails - pieces.Count * 2, Game.Stock.Count("nails16d"), "used real nails from site stock");
            Assert.GreaterOrEqual(System.Array.FindAll(pieces[0].GetComponentsInChildren<Transform>(), x => x.name == "NailHead").Length, 2, "nail heads left in the wood");
            Assert.IsTrue(shot, "screenshot of the worker mid-task");
            Assert.Greater(Crew.Info(w).UnitsDone, 7);
        }

        [UnityTest]
        public IEnumerator WorkerFramesWallFromLayout()
        {
            yield return Frames(5);
            var at = Area + new Vector3(0, 0, 14f);
            Setup(at);
            FramingJob.RequireStock = false; FramingJobRunner.Instant = true;
            var spec = new WallSpec { Name = "Crew test wall", LengthIn = 120f };
            spec.Openings.Add(new WallOpening { Product = Catalog.Get("win-sh-3648"), CenterIn = 60f });
            var wall = WallPlan.Create(spec, at, Quaternion.identity);
            _made.Add(wall.gameObject);
            var job = WallPlan.Frame(wall);
            var w = HireTest("carp4", "carpenter", 5);
            var helper = HireTest("lab2", "laborer", 3);
            var task = Crew.AddTask(FramingJobTask.For(job));
            Crew.AddTask(new FastenTask { A = at, Radius = 6f });   // the laborer nails off anything left loose
            var cam = Cam(at + new Vector3(4.8f, 2.4f, -3.6f), at + new Vector3(1.4f, 0.5f, 0.6f));
            bool shot = false;
            for (float t = 0; t < 90f && !job.Done; t += Time.deltaTime)
            {
                var npc = CrewManager.NpcFor(w);
                if (!shot && npc != null && npc.WorkElapsed > 0.06f && job.Next > job.Steps.Count / 2) { Shot(cam, "crew_framing_wall"); shot = true; }
                yield return null;
            }
            Assert.IsTrue(job.Done, $"wall framed by the crew ({job.Next}/{job.Steps.Count}) {task.Status}");
            yield return Frames(30);
            int studs = 0;
            foreach (var m in wall.GetComponentsInChildren<StructuralMember>()) if (m.Role == MemberRole.Stud) studs++;
            Assert.GreaterOrEqual(studs, 5, "studs at 16\" O.C.");
            Assert.AreEqual(WallState.Braced, wall.State, "stood up, plumbed and braced");
            Assert.IsTrue(shot, "screenshot of the crew framing");
            Shot(Cam(at + new Vector3(5.2f, 2.1f, -4.4f), at + new Vector3(1.5f, 1.1f, 0)), "crew_wall_done");
        }

        [UnityTest]
        public IEnumerator InspectionFindsLowSkillMistakes()
        {
            yield return Frames(5);
            var at = Area + new Vector3(14f, 0, 0);
            Setup(at);
            Site.Lot = new Rect(at.x - 4f, at.z - 4f, 12f, 8f);
            if (Game.Stock.Count("nails16d") < 100) Game.Stock.Add("nails16d", 500);
            var pieces = LayLooseFrame(at, 14);
            var w = HireTest("green1", "laborer", 1);   // a green laborer doing carpentry: slow, and misses nails
            var task = Crew.AddTask(new FastenTask { A = at, Radius = 8f });
            for (float t = 0; t < 60f && !task.Done; t += Time.deltaTime) yield return null;
            Assert.IsTrue(task.Done, task.Status);
            int loose = pieces.FindAll(p => p.State == PieceState.Loose).Count;
            Assert.Greater(loose, 0, "a skill-1 hand on the wrong trade leaves pieces with one nail");
            Assert.Greater(CrewDefect.Log.Count, 0);
            Assert.AreEqual(loose, Crew.Info(w).Mistakes);

            Inspections.RuleFilter = r => r.Id == "site.loose-members";
            var res = Inspections.Evaluate(InspectionPhase.Framing);
            Assert.IsFalse(res.Passed, "inspector catches the missed fasteners");
            Assert.IsTrue(res.Issues.Exists(i => i.Message.Contains("not fastened")), "issue names the loose member");

            // a journeyman goes back over it
            Crew.FireWorker(w);
            HireTest("carp3", "carpenter", 5);
            task.Reopen();
            for (float t = 0; t < 40f && pieces.Exists(p => p.State == PieceState.Loose); t += Time.deltaTime) yield return null;
            Assert.IsTrue(Inspections.Evaluate(InspectionPhase.Framing).Passed, "fixed after the redo");
        }

        [UnityTest]
        public IEnumerator CrewSaveRoundTrip()
        {
            yield return Frames(5);
            var at = Area + new Vector3(14f, 0, 14f);
            Setup(at);
            Crew.IgnoreWorkHours = false;
            if (GameClock.I != null) GameClock.I.Hours = 20f;   // after hours: nobody on site to change things
            var a = HireTest("sv1", "drywall", 3);
            var b = HireTest("sv2", "painter", 2);
            Crew.Info(a).Overtime = true; Crew.Info(b).Mistakes = 2;
            b.AssignedJobId = null;
            Crew.AddTask(new FastenTask { A = at, Radius = 5f });
            Crew.AddTask(new DigTask { A = at, B = at + Vector3.right * 2f, Radius = 0.15f, Depth = 0.3f });
            Crew.AddTask(new StockTask { A = at, Ref = "2x4x8", Count = 6, Moved = 2 });
            Crew.Sims.Add(new JobSim { JobId = "job-x", Progress = 0.4f, QualitySum = 1.2f, QualityWeight = 1.5f });
            if (Game.Stock.Count("2x4x8") < 3) Game.Stock.Add("2x4x8", 3);
            StagedPile.At(at, 0, "2x4x8").Add(2);
            int market = Crew.Market.Count;
            Assert.IsTrue(SaveSystem.Save("test_crew"), SaveSystem.LastMessage);

            Crew.ResetState(); Company.ResetState();
            Assert.AreEqual(0, Crew.Tasks.Count);
            Assert.IsTrue(SaveSystem.Load("test_crew"), SaveSystem.LastMessage);
            yield return Frames(2);
            Assert.AreEqual(2, Company.Workers.Count);
            var a2 = Crew.Find("sv1"); var b2 = Crew.Find("sv2");
            Assert.AreEqual("drywall", a2.Trade); Assert.AreEqual(3, a2.Skill);
            Assert.IsTrue(Crew.Info(a2).Overtime); Assert.AreEqual(2, Crew.Info(b2).Mistakes);
            Assert.IsTrue(string.IsNullOrEmpty(b2.AssignedJobId), "idle at the yard");
            Assert.AreEqual(3, Crew.Tasks.Count);
            Assert.IsTrue(Crew.Tasks[1] is DigTask d && Mathf.Abs(d.Depth - 0.3f) < 1e-4f);
            Assert.IsTrue(Crew.Tasks[2] is StockTask s && s.Moved == 2 && s.Count == 6);
            Assert.AreEqual(0.4f, Crew.SimFor("job-x").Progress, 1e-4f);
            Assert.AreEqual(market, Crew.Market.Count);
            Assert.AreEqual(2, StagedPile.Total("2x4x8"));
            SaveSystem.Delete("test_crew");
        }
    }
}
