using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Spreads catalog work over the whole map with sensible travel: service calls pick among every enterable house in town (core, Birch St
    /// and the map houses <see cref="MapPlans"/> made enterable), contractor jobs among every empty house lot, builds among free lots in a
    /// district that matches the plan (houses on residential lots, diners/offices/gas stations on commercial lots, warehouses on industrial
    /// ones). Picks are weighted by distance from the company's base: a Handyman mostly works the neighborhood, a Developer works the county.
    /// </summary>
    public static class MapJobs
    {
        /// <summary>Typical one-way trip (m) by stage: weight = exp(-distance / scale).</summary>
        public static float TravelScale(CompanyStage s)
        {
            switch (s)
            {
                case CompanyStage.Handyman: return 380f;
                case CompanyStage.Contractor: return 520f;
                case CompanyStage.Crew: return 700f;
                default: return 1000f;
            }
        }

        public static Vector3 Base => Town.HQPosition;

        /// <summary>Weighted pick (closer = likelier, nothing is impossible).</summary>
        public static T Pick<T>(IList<T> items, System.Func<T, Vector3> pos, System.Random rng = null)
        {
            if (items == null || items.Count == 0) return default;
            float scale = TravelScale(Company.Stage), total = 0f;
            var w = new float[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                float d = Vector3.Distance(Flat(pos(items[i])), Flat(Base));
                w[i] = Mathf.Exp(-d / scale) + 0.02f;
                total += w[i];
            }
            float x = (float)(rng != null ? rng.NextDouble() : Random.value) * total;
            for (int i = 0; i < items.Count; i++) { x -= w[i]; if (x <= 0f) return items[i]; }
            return items[items.Count - 1];
        }

        static Vector3 Flat(Vector3 p) { p.y = 0; return p; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            JobCatalog.AddressPicker = list => Pick(list, l => l.Parking);
            JobCatalog.BuildLots = BuildLotsFor;
            Town.Built -= Bind; Town.Built += Bind;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bind() { TownHook.JobLotProvider = ContractorLot; }

        // ------------------------------------------------------------------ contractor jobs (decks, fences, sheds, garages...)

        static readonly Dictionary<string, int> _handed = new Dictionary<string, int>();

        /// <summary>An open empty house lot anywhere in town (weighted by travel), else LotBroker's rural overflow.</summary>
        public static (Vector3 center, string address) ContractorLot(int index)
        {
            var open = new List<TownLot>();
            foreach (var l in LotBroker.OpenHouseLots())
            {
                if (l.Category != "residential" || l.Frontage < 15f || l.Depth < 25f) continue;
                if (_handed.TryGetValue(l.Id, out int f) && f == Time.frameCount) continue;
                open.Add(l);
            }
            if (open.Count == 0) return LotBroker.ContractorLot(index);
            var pick = Pick(open, l => l.Center);
            _handed[pick.Id] = Time.frameCount;
            return (pick.Center, pick.Address);
        }

        // ------------------------------------------------------------------ builds

        /// <summary>Which lot category a plan belongs on.</summary>
        public static string CategoryFor(HousePlan plan)
        {
            if (plan == null) return "residential";
            switch (plan.Group)
            {
                case PlanGroup.Commercial: return "commercial";
                case PlanGroup.Industrial: return "industrial";
                default: return "residential";
            }
        }

        /// <summary>Free town lots for a build of this plan: matching category first (weighted by travel), then any other free lot.</summary>
        public static IEnumerable<DevLot> BuildLotsFor(HousePlan plan)
        {
            string cat = CategoryFor(plan);
            var match = new List<TownLot>();
            foreach (var l in Town.FreeLots)
            {
                if (LotBroker.IsTract(l) || LotBroker.Reserved(l)) continue;
                bool ok = l.Category == cat || (cat == "industrial" && l.Category == "commercial" && l.District == "strip");
                if (ok) match.Add(l);
            }
            var seen = new HashSet<string>();
            while (match.Count > 0)
            {
                var l = Pick(match, x => x.Center);
                match.Remove(l);
                var d = DevLots.Provider?.Find(l.Id);
                if (d != null && seen.Add(d.Id)) yield return d;
            }
            if (DevLots.Provider == null) yield break;
            // fallback: houses may go on any free lot that is not commercial / industrial; a diner or warehouse never lands in a subdivision
            foreach (var d in DevLots.Provider.FreeLots())
            {
                var tl = Town.Find(d.Id);
                string c = tl != null ? tl.Category : "residential";
                if ((cat == "residential") == (c == "residential") && seen.Add(d.Id)) yield return d;
            }
        }

        /// <summary>Number of distinct districts that hold an enterable house (service-call spread).</summary>
        public static int ServiceDistricts()
        {
            var s = new HashSet<string>();
            foreach (var l in Town.Addresses) if (l.House != null) s.Add(l.External ? l.District : "oldtown");
            return s.Count;
        }
    }
}
