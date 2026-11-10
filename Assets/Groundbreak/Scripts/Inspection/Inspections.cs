using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Code-inspection phases, in construction order.</summary>
    public enum InspectionPhase { Footing, UnderSlab, Framing, RoughMEP, Insulation, Final }

    /// <summary>One problem the inspector found. With a position, a red marker is planted there.</summary>
    public struct InspectionIssue
    {
        public string Message;
        public Vector3 Position;
        public bool HasPosition;
        /// <summary>Filled in by the framework: which rule produced this issue.</summary>
        public string RuleId;

        public InspectionIssue(string message) { Message = message; Position = default; HasPosition = false; RuleId = null; }
        public InspectionIssue(string message, Vector3 worldPosition) { Message = message; Position = worldPosition; HasPosition = true; RuleId = null; }
    }

    /// <summary>A code requirement checked during one inspection phase. Check returns the issues found (none = pass).</summary>
    public sealed class InspectionRule
    {
        public string Id;
        public InspectionPhase Phase;
        public string Description;
        public Func<IEnumerable<InspectionIssue>> Check;
    }

    public class InspectionResult
    {
        public InspectionPhase Phase;
        public bool Passed;
        public int Day;
        public int RulesChecked;
        public readonly List<InspectionIssue> Issues = new List<InspectionIssue>();
        public readonly List<string> Notes = new List<string>();
        public string Summary => Passed ? $"{Inspections.Label(Phase)} inspection PASSED ({RulesChecked} items checked)"
                                        : $"{Inspections.Label(Phase)} inspection FAILED: {Issues.Count} correction(s)";
    }

    /// <summary>
    /// Shared inspection framework. Trades plug in rules with <c>Inspections.Register(id, phase, description, check)</c>;
    /// the Inspections tablet app schedules an inspector who walks the lot, evaluates every rule of the phase and leaves
    /// red markers at each issue. A failed phase costs <see cref="ReinspectionFee"/> and a day to re-inspect.
    /// Phase progress: <see cref="CurrentPhase"/> is the first required phase not yet passed.
    /// </summary>
    public static class Inspections
    {
        public const int ReinspectionFee = 75;

        static readonly List<InspectionRule> _rules = new List<InspectionRule>();
        static readonly HashSet<InspectionPhase> _passed = new HashSet<InspectionPhase>();
        static readonly Dictionary<InspectionPhase, int> _fails = new Dictionary<InspectionPhase, int>();

        public static readonly InspectionPhase[] AllPhases = (InspectionPhase[])Enum.GetValues(typeof(InspectionPhase));
        /// <summary>Phases the current job needs (career contracts narrow this, e.g. a shed has no under-slab inspection).</summary>
        public static List<InspectionPhase> RequiredPhases = new List<InspectionPhase>(AllPhases);
        public static readonly List<InspectionResult> History = new List<InspectionResult>();
        /// <summary>Optional filter (tests/sandbox): only rules returning true are evaluated.</summary>
        public static Func<InspectionRule, bool> RuleFilter;

        public static event Action<InspectionResult> Completed;

        // ------------------------------------------------------------------ registration

        /// <summary>Adds a rule (same id replaces). Call from [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)].</summary>
        public static void Register(InspectionRule rule)
        {
            if (rule == null || string.IsNullOrEmpty(rule.Id) || rule.Check == null) throw new ArgumentException("rule needs Id and Check");
            _rules.RemoveAll(r => r.Id == rule.Id);
            _rules.Add(rule);
        }

        public static void Register(string id, InspectionPhase phase, string description, Func<IEnumerable<InspectionIssue>> check) =>
            Register(new InspectionRule { Id = id, Phase = phase, Description = description, Check = check });

        public static bool Unregister(string id) => _rules.RemoveAll(r => r.Id == id) > 0;

        public static IReadOnlyList<InspectionRule> Rules => _rules;
        public static IEnumerable<InspectionRule> RulesFor(InspectionPhase phase)
        {
            foreach (var r in _rules) if (r.Phase == phase && (RuleFilter == null || RuleFilter(r))) yield return r;
        }

        // ------------------------------------------------------------------ state

        public static bool IsPassed(InspectionPhase p) => _passed.Contains(p);
        public static int FailCount(InspectionPhase p) => _fails.TryGetValue(p, out var n) ? n : 0;
        /// <summary>True if the next inspection of this phase is a paid re-inspection.</summary>
        public static bool NeedsReinspection(InspectionPhase p) => !IsPassed(p) && FailCount(p) > 0;

        /// <summary>First required phase not yet passed; null when the job is fully inspected.</summary>
        public static InspectionPhase? CurrentPhase
        {
            get
            {
                foreach (var p in AllPhases) if (RequiredPhases.Contains(p) && !_passed.Contains(p)) return p;
                return null;
            }
        }

        public static string CurrentPhaseLabel => CurrentPhase.HasValue ? Label(CurrentPhase.Value) : "Complete - CO issued";

        public static string Label(InspectionPhase p)
        {
            switch (p)
            {
                case InspectionPhase.Footing: return "Footing";
                case InspectionPhase.UnderSlab: return "Under-slab";
                case InspectionPhase.Framing: return "Framing";
                case InspectionPhase.RoughMEP: return "Rough MEP";
                case InspectionPhase.Insulation: return "Insulation";
                case InspectionPhase.Final: return "Final";
            }
            return p.ToString();
        }

        public static void ResetState()
        {
            _passed.Clear(); _fails.Clear(); History.Clear();
            RequiredPhases = new List<InspectionPhase>(AllPhases);
            InspectionMarkers.ClearAll();
        }

        // ------------------------------------------------------------------ evaluation

        /// <summary>Runs every rule of the phase right now. No fees, no state change.</summary>
        public static InspectionResult Evaluate(InspectionPhase phase)
        {
            var res = new InspectionResult { Phase = phase, Day = GameClock.I != null ? GameClock.I.Day : 0 };
            foreach (var rule in new List<InspectionRule>(RulesFor(phase)))
            {
                res.RulesChecked++;
                int before = res.Issues.Count;
                try
                {
                    var found = rule.Check();
                    if (found != null)
                        foreach (var i in found)
                        {
                            var x = i; x.RuleId = rule.Id;
                            if (string.IsNullOrEmpty(x.Message)) x.Message = rule.Description;
                            res.Issues.Add(x);
                        }
                }
                catch (Exception e)
                {
                    res.Issues.Add(new InspectionIssue($"{rule.Description}: could not verify ({e.GetType().Name})") { RuleId = rule.Id });
                    Debug.LogWarning($"[Inspections] rule {rule.Id} threw: {e.Message}");
                }
                res.Notes.Add((res.Issues.Count == before ? "OK   " : "FIX  ") + rule.Description);
            }
            res.Passed = res.Issues.Count == 0;
            return res;
        }

        /// <summary>Performs and records an inspection (the inspector NPC calls this when the walk is done).
        /// Updates phase state, replaces markers, fires <see cref="Completed"/>.</summary>
        public static InspectionResult Inspect(InspectionPhase phase)
        {
            InspectionResult res;
            if (!GameSettings.InspectionsEnabled)
            {
                res = new InspectionResult { Phase = phase, Passed = true, Day = GameClock.I != null ? GameClock.I.Day : 0 };
                res.Notes.Add("Inspections are turned off - signed off automatically.");
            }
            else res = Evaluate(phase);

            if (res.Passed) _passed.Add(phase);
            else _fails[phase] = FailCount(phase) + 1;
            History.Add(res);
            InspectionMarkers.Show(phase, res.Issues);
            Game.Toast(res.Summary);
            Sfx.Play(res.Passed ? "success" : "error", Game.Player != null ? Game.Player.transform.position : Vector3.zero, 0.7f);
            try { Completed?.Invoke(res); } catch (Exception e) { Debug.LogException(e); }
            return res;
        }

        /// <summary>Charges the re-inspection fee and loses a day if this phase failed before. Returns false if the fee can't be paid.</summary>
        public static bool ChargeForScheduling(InspectionPhase phase, out string message)
        {
            if (!NeedsReinspection(phase)) { message = $"{Label(phase)} inspection scheduled."; return true; }
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(ReinspectionFee, $"Re-inspection fee ({Label(phase)})"))
            {
                message = $"Re-inspection fee is ${ReinspectionFee}; not enough money.";
                return false;
            }
            if (GameClock.I != null) { GameClock.I.Day += 1; GameClock.I.Hours = 8f; }
            message = $"Re-inspection booked for tomorrow 8:00 AM (${ReinspectionFee} fee, a day lost).";
            return true;
        }

        // ------------------------------------------------------------------ save

        [Serializable]
        class SaveData
        {
            public List<int> passed = new List<int>();
            public List<int> failPhase = new List<int>();
            public List<int> failCount = new List<int>();
            public List<int> required = new List<int>();
        }

        internal static string Save()
        {
            var d = new SaveData();
            foreach (var p in _passed) d.passed.Add((int)p);
            foreach (var kv in _fails) { d.failPhase.Add((int)kv.Key); d.failCount.Add(kv.Value); }
            foreach (var p in RequiredPhases) d.required.Add((int)p);
            return JsonUtility.ToJson(d);
        }

        internal static void Load(string json)
        {
            ResetState();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<SaveData>(json);
            foreach (var p in d.passed) _passed.Add((InspectionPhase)p);
            for (int i = 0; i < d.failPhase.Count && i < d.failCount.Count; i++) _fails[(InspectionPhase)d.failPhase[i]] = d.failCount[i];
            if (d.required != null && d.required.Count > 0) { RequiredPhases.Clear(); foreach (var p in d.required) RequiredPhases.Add((InspectionPhase)p); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset()
        {
            _passed.Clear(); _fails.Clear(); History.Clear(); RuleFilter = null; Completed = null;
            RequiredPhases = new List<InspectionPhase>(AllPhases);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension("inspections", Save, Load);
            SiteRules.RegisterAll();
        }
    }
}
