using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// A repair defect driven by a <see cref="JobDef"/> (RepairType.Catalog). The definition's steps become RepairActions, the hidden cause
    /// is <see cref="RepairDefect.Cause"/> (index into JobDef.Causes), and the model comes from <see cref="CatalogProps"/>. Everything the
    /// Repairs feature does (hold-to-work UI, van parts, invoice, satisfaction, callbacks, save) works unchanged.
    /// Flags: "done:" steps are in S.done; open states (water off, cover off) are flags named after the step that opened them.
    /// </summary>
    public class CatalogDefect : RepairDefect
    {
        public JobDef Def { get; private set; }
        public CauseDef ActualCause => Def != null && Cause >= 0 && Cause < Def.Causes.Count ? Def.Causes[Cause] : null;

        JobDef D { get { if (Def == null) Def = JobCatalog.Get(S != null ? S.def : null); return Def; } }

        public override string Title => D != null ? D.Name : "Repair";
        public override string Symptom => D != null ? JobCatalog.Complaint(D, S) : "";
        public override bool NeedsLadder => D != null && D.Where != null && D.Where.Ladder;
        public override float BookHours => ActualCause != null ? ActualCause.Hours : 1f;

        /// <summary>Open states (cover off, water off) not closed again.</summary>
        public List<string> OpenStates { get { var l = new List<string>(); foreach (var s in D.Steps) if (s.Opens && Has(s.Id)) l.Add(s.Id); return l; } }

        public bool CauseFixed
        {
            get
            {
                var c = ActualCause; if (c == null) return false;
                foreach (var id in c.Fix) if (!Did(id)) return false;
                return true;
            }
        }

        public override bool IsFixed => CauseFixed && OpenStates.Count == 0;

        protected override float SkipPenalty
        {
            get
            {
                if (D == null) return 0f;
                float p = 0f;
                bool anyDiag = false, diagDone = false;
                foreach (var s in D.Steps)
                {
                    if (s.Kind == StepKind.Diagnose) { anyDiag = true; if (Did(s.Id)) diagDone = true; }
                    // a closing step ("power-on", "water-on") can only be done if its state was opened; leaving a state open
                    // already keeps the job from counting as fixed, so don't penalize closing steps that were never possible
                    if (s.Kind == StepKind.Finish && !s.Optional && string.IsNullOrEmpty(s.Closes) && !Did(s.Id)) p += 0.08f;
                }
                if (anyDiag && !diagDone) p += 0.1f;
                return p;
            }
        }

        public override string Observation
        {
            get
            {
                if (D == null) return "";
                var open = OpenStates;
                if (IsFixed) return "Fixed. " + (string.IsNullOrEmpty(S.reading) ? "" : S.reading);
                if (CauseFixed && open.Count > 0) return "Repaired — put it back together: " + D.Step(open[0]).Label.ToLower();
                if (!string.IsNullOrEmpty(S.reading)) return S.reading;
                return $"The customer says: {Symptom} Find out why before buying parts.";
            }
        }

        protected override Bounds ColliderBox
        {
            get
            {
                var sz = D != null ? D.PropSize : Vector3.one * 0.5f;
                return new Bounds(new Vector3(0, sz.y * 0.5f, 0), new Vector3(Mathf.Max(0.35f, sz.x), Mathf.Max(0.35f, sz.y), Mathf.Max(0.35f, sz.z)));
            }
        }

        protected override void Restore()
        {
            if (D != null && D.Where != null && D.Where.HideFixture) House.SetFixtureVisible(Spot, true);
        }

        void Place()
        {
            if (House == null || D == null || D.Where == null) return;
            if (D.Where.HideFixture) House.SetFixtureVisible(Spot, false);
            if (D.Where.Place == null) return;
            var lp = D.Where.Place(House);
            transform.SetPositionAndRotation(House.transform.TransformPoint(lp.position), House.transform.rotation * lp.rotation);
        }

        protected override void DefineActions()
        {
            if (D == null) { Debug.LogError("[JobCatalog] unknown definition " + S?.def); return; }
            Place();
            foreach (var st in D.Steps)
            {
                var s = st;
                var a = Act(s.Id, s.Label, s.Tool, s.Seconds, () => Available(s), () => Run(s));
                foreach (var (id, n) in s.Mats) a.Uses(id, n);
                if (s.Kind == StepKind.Diagnose || s.Test) a.Diag();
                if (s.WaitMinutes > 0f) a.Wait(s.WaitMinutes);
                if (s.Dry) a.Dry();
                a.Snd(s.Sound);
            }
        }

        bool Available(StepDef s)
        {
            foreach (var id in s.After) if (!Did(id)) return false;
            foreach (var id in s.Need) if (!Has(id)) return false;
            if (s.Opens) return !Has(s.Id);
            if (!string.IsNullOrEmpty(s.Closes)) return Has(s.Closes);
            if (s.Kind == StepKind.Fix) return !Did(s.Id);
            if (s.Kind == StepKind.Finish && !s.Test) return CauseFixed && !Did(s.Id);
            return true;
        }

        string Run(StepDef s)
        {
            var c = ActualCause;
            string text = s.Say;
            if (c != null && s.SayIf.TryGetValue(c.Id, out var t)) text = t;

            if (!string.IsNullOrEmpty(s.Guard) && !Has(s.Guard))
            {
                Lose(0.2f); Dirty(0.25f);
                string hz = s.Hazard ?? "That should not be done live.";
                if (s.Kind != StepKind.Fix) return hz;
                text = hz + " " + (text ?? "");
            }
            if (s.Opens) Set(s.Id);
            if (!string.IsNullOrEmpty(s.Closes)) Clear(s.Closes);

            switch (s.Kind)
            {
                case StepKind.Diagnose:
                    if (s.Test) return TestResult(s, text);
                    Set("diagnosed");
                    return text ?? (c != null ? c.Finding : "");
                case StepKind.Fix:
                    if (c != null && !s.Opens && Array.IndexOf(c.Fix, s.Id) < 0)
                    {
                        bool usedByAny = D.Causes.Exists(x => Array.IndexOf(x.Fix, s.Id) >= 0);
                        if (usedByAny)
                        {
                            S.wrongRepair = true; Lose(0.15f);
                            return (text ?? s.Label + " — done") + " But that was not what was wrong: the problem is still there.";
                        }
                    }
                    return text ?? s.Label + " — done.";
                default:
                    if (s.Test) return TestResult(s, text);
                    if (s.Id.Contains("clean") || s.Id.Contains("wipe") || s.Id.Contains("sweep")) S.mess = 0f;
                    return text ?? s.Label + " — done.";
            }
        }

        string TestResult(StepDef s, string text)
        {
            if (CauseFixed) { Set("verified"); return text ?? "Checked: it works properly now."; }
            var c = ActualCause;
            return "Still not right. " + (c != null ? c.Finding : "");
        }

        protected override void BuildModel(RM m)
        {
            if (D == null) return;
            var ctx = new PropCtx
            {
                Def = D, Defect = this, Fixed = IsFixed, CauseFixed = CauseFixed, Open = OpenStates.Count > 0,
                CauseId = ActualCause != null ? ActualCause.Id : "", Diagnosed = Has("diagnosed"), Size = D.PropSize,
            };
            CatalogProps.Build(m, D.Prop, ctx);
        }

        public bool DidStep(string id) => Did(id);
        public bool HasState(string id) => Has(id);
    }
}
