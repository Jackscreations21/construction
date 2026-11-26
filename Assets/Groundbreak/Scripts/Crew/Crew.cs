using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Someone looking for work on today's labor market.</summary>
    [Serializable]
    public class Candidate
    {
        public string Name, Trade;
        public int Skill = 1, Wage = 160;
    }

    /// <summary>Crew-feature data about a hired <see cref="Worker"/> (the shared Worker class stays untouched), keyed by Worker.Uid.</summary>
    [Serializable]
    public class CrewInfo
    {
        public string Uid;
        public bool Overtime;       // works to 5:30 PM at time-and-a-half
        public bool Training;       // in class today; skill goes up at the next day start
        public int HiredDay, UnitsDone, Mistakes;
        public string Doing = "";   // what the worker is doing right now (UI)
    }

    /// <summary>Progress of a job the player is not at, worked by its assigned crew one day at a time.</summary>
    [Serializable]
    public class JobSim
    {
        public string JobId;
        public float Progress;      // 0..1
        public float QualitySum, QualityWeight;
        public float Quality => QualityWeight > 0f ? QualitySum / QualityWeight : 0.7f;
    }

    /// <summary>
    /// The Crew stage of the business: labor market, hiring/firing (through <see cref="Company.Hire"/>/<see cref="Company.Fire"/>),
    /// payroll (a <see cref="Company.RegisterDailyCost"/>), morale, training, the task queue, and day-by-day simulation of jobs
    /// the player isn't standing on. Worker NPCs live in <see cref="CrewManager"/>/<see cref="CrewNpc"/>. Saved as "crew".
    /// </summary>
    public static class Crew
    {
        public const float DayStartHour = 7f, DayEndHour = 15.5f, OvertimeEndHour = 17.5f;
        public const float OvertimePayFactor = 1f + 2f / 8f * 1.5f;     // 2 extra hours at time-and-a-half
        /// <summary>Job id for the lot the player builds on themselves (no Company job attached).</summary>
        public const string SiteJobId = "site";
        public const int MarketSize = 7;

        public static readonly List<Candidate> Market = new List<Candidate>();
        public static int MarketDay = -1;
        public static readonly List<CrewTask> Tasks = new List<CrewTask>();
        public static readonly List<JobSim> Sims = new List<JobSim>();
        static readonly Dictionary<string, CrewInfo> _info = new Dictionary<string, CrewInfo>();

        /// <summary>Which job the player is standing on right now (set by CrewManager): a Job.Id or <see cref="SiteJobId"/>.</summary>
        public static string HereJobId = SiteJobId;
        /// <summary>Tests: NPCs work regardless of the clock, and faster.</summary>
        public static bool IgnoreWorkHours;
        public static float SpeedMultiplier = 1f;
        /// <summary>Which jobs may be finished by off-site simulation. Houses and developments need the real building.</summary>
        public static Func<Job, bool> CanSimulate = j => j.Kind != JobKind.House && j.Kind != JobKind.Development;
        /// <summary>Set while it rains during working hours; costs morale at the next day start.</summary>
        public static bool RainedOnCrew;

        public static event Action Changed;
        public static void NotifyChanged() => Changed?.Invoke();

        static int Day => GameClock.I != null ? GameClock.I.Day : 1;

        // ------------------------------------------------------------------ workers

        public static CrewInfo Info(Worker w)
        {
            if (w == null) return new CrewInfo();
            if (!_info.TryGetValue(w.Uid ?? "", out var i)) { i = new CrewInfo { Uid = w.Uid, HiredDay = Day }; _info[w.Uid ?? ""] = i; }
            return i;
        }

        public static Worker Find(string uid) => Company.Workers.Find(w => w.Uid == uid);
        public static int MarketWage(Worker w) => CrewTrades.Wage(w.Trade, w.Skill);
        public static int DailyPay(Worker w) => Mathf.RoundToInt(w.DailyWage * (Info(w).Overtime ? OvertimePayFactor : 1f));
        public static int Payroll() { int n = 0; foreach (var w in Company.Workers) n += DailyPay(w); return n; }
        public static bool Available(Worker w) => w != null && !Info(w).Training;
        public static float EndHour(Worker w) => Info(w).Overtime ? OvertimeEndHour : DayEndHour;

        public static bool OnTheClock(Worker w)
        {
            if (IgnoreWorkHours) return true;
            float h = GameClock.I != null ? GameClock.I.Hours : 9f;
            return h >= DayStartHour && h < EndHour(w);
        }

        public static string JobLabel(string jobId)
        {
            if (string.IsNullOrEmpty(jobId)) return "Yard (idle)";
            if (jobId == SiteJobId) return "My build site";
            var j = Company.ActiveJobs.Find(x => x.Id == jobId);
            if (j != null) return j.Title;
            var p = Projects.Get(jobId);   // a spec house (Development) has a project id but no Job
            return p != null && p.Lot != null ? $"Spec house {p.Lot.Address}" : "(finished job)";
        }

        public static Worker HireCandidate(Candidate c, out string msg)
        {
            if (c == null || !Market.Contains(c)) { msg = "Not available any more"; return null; }
            Market.Remove(c);
            var w = Company.Hire(new Worker { Name = c.Name, Trade = c.Trade, Skill = c.Skill, DailyWage = c.Wage, Morale = 0.75f, AssignedJobId = SiteJobId });
            Info(w);
            msg = $"Hired {c.Name} ({CrewTrades.Get(c.Trade).Label}, {CrewTrades.SkillLabel(c.Skill)}) at ${c.Wage}/day";
            Game.Toast(msg);
            Changed?.Invoke();
            return w;
        }

        public static void FireWorker(Worker w)
        {
            if (w == null) return;
            foreach (var t in Tasks) t.Release(w.Uid);
            _info.Remove(w.Uid ?? "");
            Company.Fire(w);
            Changed?.Invoke();
        }

        public static void Assign(Worker w, string jobId)
        {
            if (w == null) return;
            foreach (var t in Tasks) t.Release(w.Uid);
            w.AssignedJobId = jobId;
            Company.NotifyChanged(); Changed?.Invoke();
        }

        /// <summary>A 5% raise: costs more every day, lifts morale now.</summary>
        public static void GiveRaise(Worker w)
        {
            w.DailyWage = Mathf.CeilToInt(w.DailyWage * 1.05f / 4f) * 4;
            w.Morale = Mathf.Clamp01(w.Morale + 0.08f);
            Company.NotifyChanged(); Changed?.Invoke();
        }

        /// <summary>Trade-school / certification course to reach the next skill level.</summary>
        public static int TrainingCost(Worker w) => w.Skill >= 5 ? 0 : 350 * w.Skill;

        /// <summary>Pay for a course: the worker is in class for the rest of the day (still paid) and comes back a level up.</summary>
        public static bool Train(Worker w, out string msg)
        {
            var i = Info(w);
            if (w.Skill >= 5) { msg = w.Name + " is already a master"; return false; }
            if (i.Training) { msg = w.Name + " is already in class"; return false; }
            int cost = TrainingCost(w);
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(cost, $"Training: {w.Name}")) { msg = $"Training costs ${cost:N0}"; return false; }
            i.Training = true;
            foreach (var t in Tasks) t.Release(w.Uid);
            w.Morale = Mathf.Clamp01(w.Morale + 0.05f);
            msg = $"{w.Name} is in class today (${cost:N0}); back tomorrow as {CrewTrades.SkillLabel(w.Skill + 1)}";
            Changed?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ skill -> speed and quality

        static bool Outside => true; // sites are open until dried in; rain slows everyone
        /// <summary>Work-rate multiplier (1 = a journeyman of the right trade on a good day).</summary>
        public static float Speed(Worker w, string taskTrade)
        {
            float s = 0.55f + 0.15f * Mathf.Clamp(w.Skill, 1, 5);
            if (!CrewTrades.Matches(w.Trade, taskTrade)) s *= 0.6f;
            s *= Mathf.Lerp(0.75f, 1.1f, Mathf.Clamp01(w.Morale));
            if (Outside && Weather.IsRaining) s *= 0.75f;
            return s;
        }

        static readonly float[] MistakeBySkill = { 0.25f, 0.16f, 0.08f, 0.025f, 0f };
        /// <summary>Chance that one unit of work is done wrong (missed nails, wide spacing, shallow trench...).</summary>
        public static float MistakeChance(Worker w, string taskTrade)
        {
            float m = MistakeBySkill[Mathf.Clamp(w.Skill, 1, 5) - 1];
            if (!CrewTrades.Matches(w.Trade, taskTrade)) m += 0.15f;
            if (w.Morale < 0.35f) m += 0.05f;
            return Mathf.Clamp01(m);
        }

        /// <summary>Expected workmanship 0..1 (used for simulated jobs).</summary>
        public static float Quality(Worker w, string taskTrade) =>
            Mathf.Clamp01(0.32f + 0.135f * w.Skill + (CrewTrades.Matches(w.Trade, taskTrade) ? 0f : -0.18f) + (w.Morale - 0.5f) * 0.2f);

        // ------------------------------------------------------------------ labor market

        public static void RefreshMarket(int day)
        {
            Market.Clear(); MarketDay = day;
            var r = new System.Random(day * 7919 + 17);
            for (int i = 0; i < MarketSize; i++)
            {
                // laborers and carpenters are the most common applicants; masters are rare
                string trade = i == 0 ? "laborer" : i == 1 ? "carpenter" : CrewTrades.All[r.Next(CrewTrades.All.Count)].Id;
                int roll = r.Next(100);
                int skill = roll < 30 ? 1 : roll < 60 ? 2 : roll < 84 ? 3 : roll < 96 ? 4 : 5;
                int ask = Mathf.RoundToInt(CrewTrades.Wage(trade, skill) * (0.94f + (float)r.NextDouble() * 0.14f) / 4f) * 4;
                Market.Add(new Candidate { Name = CrewTrades.RandomName(r), Trade = trade, Skill = skill, Wage = ask });
            }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ tasks

        public static T AddTask<T>(T task) where T : CrewTask
        {
            if (string.IsNullOrEmpty(task.Uid)) task.Uid = Guid.NewGuid().ToString("N").Substring(0, 8);
            if (string.IsNullOrEmpty(task.JobId)) task.JobId = HereJobId;
            Tasks.Add(task);
            Changed?.Invoke();
            return task;
        }

        public static void RemoveTask(CrewTask t) { Tasks.Remove(t); Changed?.Invoke(); }

        // ------------------------------------------------------------------ day rollover

        /// <summary>Labor days a job takes: about 35% of the price is labor, at ~$280 a worker-day.</summary>
        public static float LaborDays(Job j) => Mathf.Max(0.5f, j.Payout * 0.35f / 280f);
        public static JobSim SimFor(string jobId, bool create = false)
        {
            var s = Sims.Find(x => x.JobId == jobId);
            if (s == null && create) { s = new JobSim { JobId = jobId }; Sims.Add(s); }
            return s;
        }

        static void OnDayStarted(int day)
        {
            // 1. the day that just ended: off-site jobs move ahead by what their crew can do in a day
            foreach (var j in Company.ActiveJobs.ToArray())
            {
                if (j.Id == HereJobId || !CanSimulate(j)) continue;
                float days = 0f, q = 0f;
                foreach (var w in Company.Workers)
                {
                    if (w.AssignedJobId != j.Id || !Available(w)) continue;
                    float d = Speed(w, j.RequiredTrade) * (Info(w).Overtime ? 1.25f : 1f);
                    days += d; q += Quality(w, j.RequiredTrade) * d;
                }
                if (days <= 0f) continue;
                var sim = SimFor(j.Id, true);
                sim.Progress += days / LaborDays(j); sim.QualitySum += q; sim.QualityWeight += days;
                if (sim.Progress >= 1f)
                {
                    float quality = sim.Quality;
                    Sims.Remove(sim);
                    Company.Complete(j, quality);
                }
            }

            // 2. morale, training, quitting
            foreach (var w in Company.Workers.ToArray())
            {
                var i = Info(w);
                float m = w.Morale;
                float ratio = w.DailyWage / (float)Mathf.Max(1, MarketWage(w));
                m += Mathf.Clamp((ratio - 1f) * 0.5f, -0.08f, 0.06f);           // pay vs the going rate
                if (i.Overtime) m -= 0.04f;                                       // long days wear people down
                if (RainedOnCrew && !string.IsNullOrEmpty(w.AssignedJobId)) m -= 0.03f; // worked in the rain
                if (string.IsNullOrEmpty(w.AssignedJobId)) m -= 0.01f;           // sitting at the yard
                m += (0.75f - m) * 0.05f;                                         // drifts back toward content
                w.Morale = Mathf.Clamp01(m);
                if (i.Training) { i.Training = false; w.Skill = Mathf.Min(5, w.Skill + 1); Game.Toast($"{w.Name} finished training: now {CrewTrades.SkillLabel(w.Skill)}"); }
                if (w.Morale < 0.12f)
                {
                    Game.Toast($"{w.Name} quit (morale too low)");
                    FireWorker(w);
                }
            }
            RainedOnCrew = false;
            RefreshMarket(day);
            Company.NotifyChanged();
        }

        // ------------------------------------------------------------------ save

        [Serializable]
        class Data
        {
            public int marketDay; public bool rained;
            public List<Candidate> market = new List<Candidate>();
            public List<CrewInfo> info = new List<CrewInfo>();
            public List<JobSim> sims = new List<JobSim>();
            public List<CrewTask.Data> tasks = new List<CrewTask.Data>();
            public List<StagedPile.Data> staged = new List<StagedPile.Data>();
        }

        static string Save()
        {
            var d = new Data { marketDay = MarketDay, rained = RainedOnCrew, market = new List<Candidate>(Market), sims = new List<JobSim>(Sims) };
            foreach (var w in Company.Workers) d.info.Add(Info(w));
            foreach (var t in Tasks) d.tasks.Add(t.ToData());
            foreach (var p in StagedPile.All) d.staged.Add(p.ToData());
            return JsonUtility.ToJson(d);
        }

        static void Load(string json)
        {
            ResetState();
            if (string.IsNullOrEmpty(json)) { RefreshMarket(Day); return; }
            var d = JsonUtility.FromJson<Data>(json);
            MarketDay = d.marketDay; RainedOnCrew = d.rained;
            Market.AddRange(d.market); Sims.AddRange(d.sims);
            foreach (var i in d.info) { i.Doing = ""; _info[i.Uid ?? ""] = i; }
            foreach (var td in d.tasks) { var t = CrewTask.FromData(td); if (t != null) Tasks.Add(t); }
            foreach (var sd in d.staged) StagedPile.Restore(sd);
            Changed?.Invoke();
        }

        /// <summary>Clears crew-feature state (tests / new game). Company.Workers is owned by Company.</summary>
        public static void ResetState()
        {
            Market.Clear(); MarketDay = -1; Tasks.Clear(); Sims.Clear(); _info.Clear(); RainedOnCrew = false; HereJobId = SiteJobId;
            StagedPile.ClearAll();
            CrewManager.DespawnAll();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset()
        {
            Market.Clear(); MarketDay = -1; Tasks.Clear(); Sims.Clear(); _info.Clear(); RainedOnCrew = false; HereJobId = SiteJobId;
            IgnoreWorkHours = false; SpeedMultiplier = 1f; Changed = null;
            CanSimulate = j => j.Kind != JobKind.House && j.Kind != JobKind.Development;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension("crew", Save, Load);
            Company.RegisterDailyCost("Crew payroll", Payroll);
            Company.DayStarted += OnDayStarted;
            RefreshMarket(1);
        }
    }
}
