using UnityEngine;

namespace Groundbreak
{
    /// <summary>Balance pass: a house phase is funded with the subcontractors' full labor (Takeoff.Labor) when it starts. When your
    /// own hired crew works the phase (CrewHouseProgress), they do part of that work, so the subs bill less: each game day the
    /// company is credited the share of the phase labor its crew did. Without this, putting your crew on a house only bought
    /// speed and their wages were pure loss (Docs/BALANCE.md). Bounded: the credit over a whole phase never exceeds its labor.</summary>
    public static class CrewLaborCredit
    {
        public const string Label = "Sub labor credit (own crew)";

        /// <summary>Sub labor ($) budgeted for a phase of a project (finish scaled by the chosen finish level, as in Projects.PhaseCost).</summary>
        public static int PhaseLabor(HouseProject p, BuildPhase ph)
        {
            var plan = p?.Plan; if (plan == null) return 0;
            float labor = plan.Takeoff.Labor[(int)ph];
            if (ph == BuildPhase.Finish) labor *= HousePlan.FinishFactor(p.FinishLevel) / HousePlan.FinishFactor(plan.BaseQuality);
            return Mathf.RoundToInt(labor);
        }

        /// <summary>Today's credit for one project: phase labor x the part of today's progress the own crew did.</summary>
        public static int CreditFor(HouseProject p)
        {
            if (p == null || p.Complete || p.Id == Projects.OnSiteId || p.Plan == null) return 0;
            var ph = p.CurrentPhase; if (!ph.HasValue) return 0;
            int i = (int)ph.Value;
            if (!p.Funded[i] || !Developments.LotReady(p)) return 0;
            float crew = CrewHouseProgress.CrewDays(p, ph.Value) / CrewHouseProgress.CrewSize;
            if (crew <= 0f) return 0;
            float days = Mathf.Max(1, p.Plan.PhaseDays(ph.Value));
            float today = Mathf.Min(1f - p.Progress[i], (1f + crew) / days);
            if (today <= 0f) return 0;
            float crewShare = crew / (1f + crew);
            return Mathf.RoundToInt(PhaseLabor(p, ph.Value) * today * crewShare);
        }

        /// <summary>Credits today's crew share for one project. Called by <see cref="Projects.DayStep"/> for each project after the
        /// phase is funded and before its progress moves, so the order is fixed: the day a phase starts and the day it finishes
        /// both earn their credit (it used to be a separate DayStarted handler in undefined order, losing about a day per phase).
        /// Returns the credit.</summary>
        public static int Apply(HouseProject p)
        {
            if (!GameSettings.IsCareer || !(Projects.Crew is CrewHouseProgress)) return 0;
            int c = CreditFor(p);
            if (c <= 0) return 0;
            p.CostSpent -= c;
            if (!GameSettings.UnlimitedMoney) Game.Wallet.Add(c, $"{Label}: {p.Lot?.Address}");
            return c;
        }
    }
}
