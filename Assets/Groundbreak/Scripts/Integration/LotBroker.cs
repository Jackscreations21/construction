using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// One place that decides which town lot each business feature uses, so no two jobs ever share a lot:
    /// - repair calls: customer houses (<see cref="Town.Addresses"/>, RepairJobs already skips busy addresses);
    /// - contractor jobs (Fleet): empty house lots, claimed on accept and kept by the client after the job;
    /// - client houses (Builder): empty house lots via <see cref="TownLotAdapter"/>;
    /// - land for development: empty tracts via <see cref="TownLotAdapter"/>;
    /// - the warehouse / yard / HQ: a commercial lot on the north side of Main St (Town.HQPosition follows it).
    /// A lot is taken when it is claimed in the Town (saved with the town) or named by an offer / active job's address.
    /// </summary>
    public static class LotBroker
    {
        /// <summary>Lots bigger than this (sq ft, ≈ 0.7 ac) are tracts: land for development or the yard, never a single job.</summary>
        public const float TractSqFt = 30000f;
        public const string JobOwnerPrefix = "job:", ClientOwnerPrefix = "client:", YardOwner = "company-yard";
        /// <summary>Where contractor jobs go once every empty town lot is used (a rural road south of town).</summary>
        public static Vector3 OverflowOrigin = new Vector3(-150f, 0f, -420f);

        static readonly Dictionary<string, int> _handedOut = new Dictionary<string, int>();   // lot id → frame (Create called twice before Offers.Add)

        public static bool IsTract(TownLot l) => l != null && l.AreaSqFt > TractSqFt;

        /// <summary>True when an offer or active job names this lot's address, or it was just handed to a job being created.</summary>
        public static bool Reserved(TownLot l)
        {
            if (l == null) return true;
            foreach (var j in Company.Offers) if (j.Address == l.Address) return true;
            foreach (var j in Company.ActiveJobs) if (j.Address == l.Address) return true;
            return _handedOut.TryGetValue(l.Id, out int f) && f == Time.frameCount;
        }

        /// <summary>Unclaimed, unreserved empty lots (not tracts), in a stable order.</summary>
        public static List<TownLot> OpenHouseLots()
        {
            var r = new List<TownLot>();
            foreach (var l in Town.FreeLots) if (!IsTract(l) && !Reserved(l)) r.Add(l);
            return r;
        }

        // ------------------------------------------------------------------ contractor jobs (Fleet TownHook.JobLotProvider)

        /// <summary>Lot center + address for the index-th contractor job: the first open empty house lot, else a rural overflow lot.</summary>
        public static (Vector3 center, string address) ContractorLot(int index)
        {
            var open = OpenHouseLots();
            if (open.Count > 0)
            {
                var l = open[0];
                _handedOut[l.Id] = Time.frameCount;
                return (l.Center, l.Address);
            }
            int col = index % 6, row = (index / 6) % 4;
            return (OverflowOrigin + new Vector3(-col * 45f, 0f, -row * 45f), $"{2000 + index * 10} County Rd 12");
        }

        static void OnAccepted(Job j)
        {
            if (!ContractorJobs.IsContractorJob(j)) return;
            var l = Town.Find(j.Address);
            if (l != null && l.IsFree) Town.Claim(l, LotUse.BuildSite, JobOwnerPrefix + j.Id);
        }

        static void OnCompleted(Job j)
        {
            // the client keeps the lot (and the deck / shed / garage on it): re-tag the claim so it is never offered again
            var l = Town.Find(j.Address);
            if (l != null && l.Owner == JobOwnerPrefix + j.Id) { Town.Release(l); Town.Claim(l, LotUse.BuildSite, ClientOwnerPrefix + j.Client); }
        }

        // ------------------------------------------------------------------ warehouse / yard / HQ

        /// <summary>(frontage, front apron, depth) in meters of the premises YardSite builds per level. Its local frame has the street at -Z.</summary>
        static Vector3 Footprint(int level)
        {
            switch (level)
            {
                case 1: return new Vector3(46f, 14.5f, 15.3f);     // flex building: 5 bays x 30', 50' deep, parking lot in front
                case 2: return new Vector3(46f, 8f, 61f);          // 150' x 200' fenced yard, apron through the gate
                default: return new Vector3(77f, 8f, 91.5f);       // 250' x 300' HQ yard
            }
        }

        public static TownLot YardLot { get { foreach (var l in Town.Lots) if (l.Owner == YardOwner) return l; return null; } }

        static bool Fits(TownLot l, int level)
        {
            var f = Footprint(level);
            return l != null && l.Front == Vector3.back && l.Frontage >= f.x + 1f && l.Depth >= f.z + 0.5f;
        }

        /// <summary>World position for YardSite on a lot: centered on the frontage, set back so the yard fits and its apron reaches the street.</summary>
        public static Vector3 YardOrigin(TownLot l, int level)
        {
            var f = Footprint(level);
            float back = Mathf.Clamp(l.Depth - f.z - 0.5f, 0f, f.y);
            return l.Local(0f, back);
        }

        static int _syncedLevel = -1;

        /// <summary>Puts the company premises on a lot that fits the leased level (moving it for the HQ) and points Town.HQPosition,
        /// TownHook and Yard at it. Idempotent; runs on every company change and after loads.</summary>
        public static void SyncYard(bool force = false)
        {
            if (!Town.Exists || TownHook.HQOverride.HasValue) return;   // no town (Free Build, isolated tests): Fleet's fallback HQ
            int level = Company.WarehouseLevel;
            if (!force && level == _syncedLevel) return;
            _syncedLevel = level;
            var cur = YardLot;
            if (level <= 0)
            {
                if (cur != null) Town.Release(cur);
                return;
            }
            var lot = Fits(cur, level) ? cur : null;
            if (lot == null)
            {
                // smallest free lot that fits (commercial lot next to the store first, then the Main St tract)
                float best = float.MaxValue;
                foreach (var l in Town.FreeLots)
                    if (Fits(l, level) && l.AreaSqFt < best && !Reserved(l)) { best = l.AreaSqFt; lot = l; }
                if (lot != null)
                {
                    if (cur != null) Town.Release(cur);
                    Town.Claim(lot, LotUse.Company, YardOwner);
                }
            }
            Vector3 pos = lot != null ? YardOrigin(lot, level) : TownHook.FallbackHQ;
            Town.HQPosition = pos;
            Yard.Position = pos;
        }

        /// <summary>Gives every claimed lot back (tests; a new game does this through the town's save extension).</summary>
        public static void ReleaseAll()
        {
            foreach (var l in Town.Lots) if (l.Owner != null) Town.Release(l);
            _handedOut.Clear(); _syncedLevel = -1;
        }

        // ------------------------------------------------------------------ boot

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _handedOut.Clear(); _syncedLevel = -1; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Company.JobAccepted += OnAccepted;
            Company.JobCompleted += OnCompleted;
            // Yard.Lease sets the level, then RecomputeStage → Changed, then rebuilds YardSite at Yard.Position: move it first
            Company.Changed += () => SyncYard();
            SaveSystem.AfterLoad += () => SyncYard(true);
        }
    }
}
