using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Development houses the player isn't standing at are built by the company's own crew plus subcontractors.
    /// Subs alone keep the plan's phase schedule (same as Development's SimpleCrewProgress); every hired worker assigned to the
    /// house (Worker.AssignedJobId = the client job id or the spec project id) adds their real Crew.Speed for the phase's trade,
    /// so a framing crew of four carpenters roughly doubles the framing pace. Quality is the assigned crew's workmanship
    /// (Crew.Quality), or the subs' 0.8 when nobody is assigned. Deterministic per project/day (no RNG).
    /// </summary>
    public class CrewHouseProgress : ICrewProgress
    {
        /// <summary>A "standard" phase crew: this many journeymen double the subs' pace.</summary>
        public const float CrewSize = 4f;

        public static string TradeFor(BuildPhase ph)
        {
            switch (ph)
            {
                case BuildPhase.Foundation: return "concrete";
                case BuildPhase.Framing: return "carpenter";
                case BuildPhase.RoughIn: return "electrician";
                case BuildPhase.Drywall: return "drywall";
                default: return "carpenter";      // trim, doors, cabinets
            }
        }

        public static bool AssignedTo(Worker w, HouseProject p) =>
            w != null && !string.IsNullOrEmpty(w.AssignedJobId) && (w.AssignedJobId == p.Id || (p.JobId != null && w.AssignedJobId == p.JobId));

        /// <summary>Summed worker-days the assigned crew puts in today (0 when nobody is assigned).</summary>
        public static float CrewDays(HouseProject p, BuildPhase ph)
        {
            float s = 0f; string trade = TradeFor(ph);
            foreach (var w in Company.Workers)
                if (AssignedTo(w, p) && Crew.Available(w)) s += Crew.Speed(w, trade) * (Crew.Info(w).Overtime ? 1.25f : 1f);
            return s;
        }

        public float DailyProgress(HouseProject p, BuildPhase phase, int day)
        {
            int days = p.Plan != null ? Mathf.Max(1, p.Plan.PhaseDays(phase)) : 10;
            return (1f + CrewDays(p, phase) / CrewSize) / days;
        }

        public float Quality(HouseProject p)
        {
            float q = 0f, n = 0f;
            foreach (var w in Company.Workers)
                if (AssignedTo(w, p)) { q += Crew.Quality(w, w.Trade); n += 1f; }
            return n > 0f ? Mathf.Clamp01(0.5f * (q / n) + 0.5f * 0.8f) : 0.8f;   // half the work is still subbed out
        }
    }

    /// <summary>Wires the Crew feature to the town and to Development: crew progress for houses, which job the player is at,
    /// and where the crew's truck pulls up.</summary>
    public static class CrewTownLink
    {
        /// <summary>The job (or spec house project) whose lot the player is standing on, or null to let CrewManager decide.</summary>
        public static string HereJob()
        {
            if (Game.Player == null) return null;
            Vector3 pos = Game.Player.transform.position;
            foreach (var p in Projects.All)
            {
                var lot = p.Lot;
                if (lot != null && !p.Complete && lot.Contains(pos)) return p.JobId ?? p.Id;
            }
            if (!Town.Exists) return null;
            var tl = Town.LotAt(pos);
            if (tl == null) return null;
            foreach (var j in Company.ActiveJobs) if (j.Address == tl.Address) return j.Id;
            return tl == Town.BuildSite ? Crew.SiteJobId : null;
        }

        /// <summary>Where workers arrive for a town job: the lot's parking spot (the crew truck parks there), else CrewManager's default.</summary>
        public static Vector3? ArrivalFor(string jobId)
        {
            if (string.IsNullOrEmpty(jobId) || jobId == Crew.SiteJobId) return null;
            var j = Company.ActiveJobs.Find(x => x.Id == jobId);
            var tl = j != null ? Town.Find(j.Address) : null;
            if (tl != null) return tl.Parking + tl.Rotation * new Vector3(-2.5f, 0f, 0f);
            var p = Projects.Get(jobId) ?? Projects.ForJob(jobId);
            if (p != null && p.Lot != null) return p.Lot.FrontMid - p.Lot.Front * 2f;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Projects.Crew = new CrewHouseProgress();
            CrewManager.HereJobResolver = HereJob;
        }
    }
}
