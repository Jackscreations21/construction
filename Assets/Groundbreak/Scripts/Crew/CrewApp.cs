using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Crew" tablet app: your workers (assignment, overtime, raise, training, fire), today's labor market,
    /// and the task queue per job.</summary>
    public static class CrewApp
    {
        static int _tab;
        static readonly string[] Tabs = { "Crew", "Hire", "Tasks" };
        static string _taskJob;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Tablet.RegisterApp("Crew", Draw);
            Tablet.AddHelp("K: point at work and give the crew an order (fasten, build a layout, drywall, paint, dig, sod, stock material)");
            Tablet.AddHelp("Crew app: hire from the daily labor market, assign workers to jobs, overtime, training, task queue");
        }

        static void Draw()
        {
            var t = Tablet.I;
            var label = t != null ? t.LabelStyle : GUI.skin.label;
            var head = t != null ? t.HeaderStyle : GUI.skin.label;
            var btn = t != null ? t.ButtonStyle : GUI.skin.button;
            GUILayout.Label("Crew", head);
            if (!GameSettings.IsCareer) { GUILayout.Label("Hiring a crew is part of Career mode. (Main menu: F10)", label); return; }
            int day = GameClock.I != null ? GameClock.I.Day : 1;
            if (Crew.MarketDay != day) Crew.RefreshMarket(day);
            GUILayout.Label($"{Company.Workers.Count} on payroll · ${Crew.Payroll():N0}/day · work day 7:00 AM - 3:30 PM (overtime to 5:30) · stage: {Company.Stage}", label);
            _tab = GUILayout.Toolbar(_tab, Tabs, btn);
            GUILayout.Space(6);
            if (_tab == 0) DrawCrew(label, btn);
            else if (_tab == 1) DrawMarket(label, btn);
            else DrawTasks(label, btn);
        }

        static List<string> JobChoices()
        {
            var l = new List<string> { Crew.SiteJobId };
            foreach (var j in Company.ActiveJobs) l.Add(j.Id);
            foreach (var p in Projects.All) if (p.JobId == null && !p.Complete) l.Add(p.Id);   // spec houses have no Job
            l.Add(null);
            return l;
        }

        static void DrawCrew(GUIStyle label, GUIStyle btn)
        {
            if (Company.Workers.Count == 0) { GUILayout.Label("Nobody hired yet. Open the Hire tab.", label); return; }
            foreach (var w in Company.Workers.ToArray())
            {
                var i = Crew.Info(w);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"{w.Name} — {CrewTrades.Get(w.Trade).Label}, {CrewTrades.SkillLabel(w.Skill)} [{CrewTrades.Stars(w.Skill)}] · ${w.DailyWage}/day (market ${Crew.MarketWage(w)}) · morale {Mathf.RoundToInt(w.Morale * 100)}%", label);
                string doing = i.Training ? "In training today" : CrewManager.NpcFor(w) != null ? CrewManager.NpcFor(w).Doing : (string.IsNullOrEmpty(w.AssignedJobId) ? "At the yard" : "Off site / off the clock");
                GUILayout.Label($"Job: {Crew.JobLabel(w.AssignedJobId)} · {doing} · {i.UnitsDone} tasks done, {i.Mistakes} mistakes", label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Assign: next job", btn))
                {
                    var ch = JobChoices(); int k = ch.IndexOf(w.AssignedJobId);
                    Crew.Assign(w, ch[(k + 1) % ch.Count]);
                }
                if (GUILayout.Button(i.Overtime ? "Overtime: ON" : "Overtime: off", btn)) { i.Overtime = !i.Overtime; Crew.NotifyChanged(); }
                if (GUILayout.Button("Raise 5%", btn)) Crew.GiveRaise(w);
                if (w.Skill < 5 && GUILayout.Button($"Train (${Crew.TrainingCost(w):N0})", btn)) { Crew.Train(w, out var m); Tablet.I?.ShowMessage(m); }
                if (GUILayout.Button("Fire", btn)) { Crew.FireWorker(w); Tablet.I?.ShowMessage($"{w.Name} let go"); }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            var sims = new List<string>();
            foreach (var s in Crew.Sims) sims.Add($"{Crew.JobLabel(s.JobId)} {Mathf.RoundToInt(s.Progress * 100)}%");
            if (sims.Count > 0) GUILayout.Label("Off-site progress: " + string.Join(" · ", sims), label);
            if (CrewDefect.Log.Count > 0)
            {
                var e = CrewDefect.Log[CrewDefect.Log.Count - 1];
                GUILayout.Label($"Last mistake (day {e.Day}, {e.Worker}): {e.Note}", label);
            }
        }

        static void DrawMarket(GUIStyle label, GUIStyle btn)
        {
            GUILayout.Label("Today's labor market (new applicants every morning). Wages are per 8-hour day.", label);
            foreach (var c in Crew.Market.ToArray())
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{c.Name} — {CrewTrades.Get(c.Trade).Label}, {CrewTrades.SkillLabel(c.Skill)} [{CrewTrades.Stars(c.Skill)}] · asks ${c.Wage}/day", label);
                if (GUILayout.Button("Hire", btn, GUILayout.Width(90))) { Crew.HireCandidate(c, out var m); Tablet.I?.ShowMessage(m); }
                GUILayout.EndHorizontal();
            }
            if (Crew.Market.Count == 0) GUILayout.Label("Everyone's been hired. Check back tomorrow.", label);
        }

        static void DrawTasks(GUIStyle label, GUIStyle btn)
        {
            var ch = JobChoices(); ch.Remove(null);
            if (_taskJob == null || !ch.Contains(_taskJob)) _taskJob = Crew.HereJobId;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Job: " + Crew.JobLabel(_taskJob), label);
            if (GUILayout.Button("Next job", btn, GUILayout.Width(120))) _taskJob = ch[(ch.IndexOf(_taskJob) + 1) % ch.Count];
            GUILayout.EndHorizontal();

            GUILayout.Label("Add work (or point at it on site and press K):", label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fasten all loose pieces", btn)) Crew.AddTask(new FastenTask { JobId = _taskJob });
            foreach (var j in FramingJob.All)
                if (j != null && !j.Done && !Crew.Tasks.Exists(x => x is FramingJobTask f && f.Job == j && !x.Done))
                {
                    if (GUILayout.Button("Build: " + j.Title, btn)) { var ft = FramingJobTask.For(j); ft.JobId = _taskJob; Crew.AddTask(ft); }
                    break;
                }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            int n = 0;
            foreach (var task in Crew.Tasks.ToArray())
            {
                if (task.JobId != _taskJob) continue;
                n++;
                GUILayout.BeginHorizontal(GUI.skin.box);
                var who = task.ClaimedBy != null ? Crew.Find(task.ClaimedBy) : null;
                GUILayout.Label($"{task.Title} · {CrewTrades.Get(task.Trade).Label} · {task.Status}{(who != null ? " · " + who.Name : "")} · {task.UnitsDone} done", label);
                if (task.Done && GUILayout.Button("Redo", btn, GUILayout.Width(70))) task.Reopen();
                if (GUILayout.Button("X", btn, GUILayout.Width(40))) Crew.RemoveTask(task);
                GUILayout.EndHorizontal();
            }
            if (n == 0) GUILayout.Label("No tasks queued for this job.", label);
            if (GUILayout.Button("Clear finished tasks", btn)) { Crew.Tasks.RemoveAll(x => x.Done); Crew.NotifyChanged(); }
        }
    }
}
