using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>How big the player's business is. Each stage unlocks bigger work (see Docs/BUSINESS.md).</summary>
    public enum CompanyStage
    {
        Handyman = 0,      // repairs out of a van
        Contractor = 1,    // owns a warehouse/yard + at least one truck: remodels, additions, sheds, garages
        Crew = 2,          // has hired workers: several jobs at once
        Builder = 3,       // full houses for clients
        Developer = 4,     // buys land, builds spec houses and whole neighborhoods
    }

    public enum JobKind { Repair, Remodel, Outbuilding, House, Development }

    [Serializable]
    public class OwnedVehicle
    {
        public string Uid;        // unique per vehicle
        public string TypeId;     // "van", "pickup", "flatbed", "dump", "boom" ... (catalog owned by the Fleet feature)
        public string Name;
        public float Condition = 1f;
    }

    [Serializable]
    public class Worker
    {
        public string Uid, Name;
        public string Trade;          // "laborer", "carpenter", "concrete", "plumber", "electrician", "hvac", "drywall", "painter", "roofer", "landscaper"
        public int Skill = 1;         // 1..5
        public int DailyWage = 160;
        public string AssignedJobId;  // null = idle at the yard
        public float Morale = 0.8f;
    }

    /// <summary>A piece of paid work offered to or held by the company. Feature folders generate these and register
    /// a handler per kind; this class only stores them.</summary>
    [Serializable]
    public class Job
    {
        public string Id, Title, Client, Description, Address;
        public JobKind Kind;
        public int Payout, MaterialBudget, DueDay, MinReputation;
        public CompanyStage MinStage;
        public string RequiredVehicleType;   // null = any vehicle
        public string RequiredTrade;         // null = player can do it alone
        public Vector3 WorldPos;             // where the work is (town coordinates)
        public string Payload;               // feature-specific JSON (defect list, contract id, lot id ...)
        public bool Accepted, Done;
    }

    /// <summary>The player's construction business: stage, fleet, warehouse, crew, job list, daily costs.
    /// Money lives in Game.Wallet, reputation in Career.Reputation. Saved under SaveSystem key "company".
    /// Feature folders extend it through the events and RegisterDailyCost; do not put feature logic here.</summary>
    public static class Company
    {
        public static string Name = "My Construction Co.";
        public static CompanyStage Stage = CompanyStage.Handyman;
        public static bool HasWarehouse;
        public static int WarehouseLevel;                   // 0 none, 1 rented bay, 2 yard + shop, 3 HQ
        public static readonly List<OwnedVehicle> Vehicles = new List<OwnedVehicle>();
        public static readonly List<Worker> Workers = new List<Worker>();
        public static readonly List<Job> Offers = new List<Job>();     // on the job board, not yet accepted
        public static readonly List<Job> ActiveJobs = new List<Job>();
        public static readonly List<string> CompletedJobIds = new List<string>();
        public static int JobsCompleted, LifetimeRevenue;
        /// <summary>Jobs paying less than this raise reputation by at most +1 (Docs/BALANCE.md).</summary>
        public const int SmallJobPayout = 1000;

        public static event Action Changed;                 // any state change (UI refresh)
        public static event Action<CompanyStage> StageChanged;
        public static event Action<Job> JobAccepted, JobCompleted;
        public static event Action<int> DayStarted;         // new game day (after daily costs were charged)

        static readonly List<(string label, Func<int> cost)> _dailyCosts = new List<(string, Func<int>)>();
        static int _lastDay = -1;

        public static int Reputation { get => Career.Reputation; set => Career.Reputation = Mathf.Clamp(value, 0, 100); }
        public static bool Owns(string vehicleTypeId) => Vehicles.Exists(v => v.TypeId == vehicleTypeId);
        public static int MaxActiveJobs => Stage >= CompanyStage.Crew ? 1 + Workers.Count / 2 : 1;
        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>Recurring costs (rent, wages, insurance, loan payments). Charged once per game day.</summary>
        public static void RegisterDailyCost(string label, Func<int> cost) { _dailyCosts.Add((label, cost)); }

        public static IEnumerable<(string label, int amount)> DailyCostBreakdown()
        {
            foreach (var (label, cost) in _dailyCosts) { int a = Mathf.Max(0, cost()); if (a > 0) yield return (label, a); }
        }

        public static OwnedVehicle AddVehicle(string typeId, string name)
        {
            var v = new OwnedVehicle { Uid = Guid.NewGuid().ToString("N").Substring(0, 8), TypeId = typeId, Name = name };
            Vehicles.Add(v); RecomputeStage(); return v;
        }

        public static Worker Hire(Worker w)
        {
            if (string.IsNullOrEmpty(w.Uid)) w.Uid = Guid.NewGuid().ToString("N").Substring(0, 8);
            Workers.Add(w); RecomputeStage(); return w;
        }

        public static void Fire(Worker w) { Workers.Remove(w); RecomputeStage(); }

        /// <summary>Why a job can't be taken, or null when it can.</summary>
        public static string Blocker(Job j)
        {
            if (j.Accepted) return "Already accepted";
            if (Stage < j.MinStage) return $"Needs company stage: {j.MinStage}";
            if (Reputation < j.MinReputation) return $"Needs reputation {j.MinReputation}";
            if (!string.IsNullOrEmpty(j.RequiredVehicleType) && !Owns(j.RequiredVehicleType)) return $"Needs a {j.RequiredVehicleType}";
            if (!string.IsNullOrEmpty(j.RequiredTrade) && !Workers.Exists(w => w.Trade == j.RequiredTrade)) return $"Needs a {j.RequiredTrade} on the crew";
            if (ActiveJobs.Count >= MaxActiveJobs) return "Finish a current job first (hire crew to run more at once)";
            return null;
        }

        public static bool Accept(Job j, out string msg)
        {
            msg = Blocker(j);
            if (msg != null) return false;
            j.Accepted = true; Offers.Remove(j); ActiveJobs.Add(j);
            msg = $"Accepted: {j.Title}";
            JobAccepted?.Invoke(j); Changed?.Invoke();
            return true;
        }

        /// <summary>Pays the job and moves it to history. quality 0..1 scales pay bonus/penalty and reputation.</summary>
        public static void Complete(Job j, float quality = 1f)
        {
            if (j.Done) return;
            j.Done = true; ActiveJobs.Remove(j); CompletedJobIds.Add(j.Id); JobsCompleted++;
            int day = GameClock.I != null ? GameClock.I.Day : 0;
            int late = j.DueDay > 0 ? Mathf.Max(0, day - j.DueDay) : 0;
            int pay = Mathf.RoundToInt(j.Payout * Mathf.Lerp(0.6f, 1.1f, Mathf.Clamp01(quality)) * (1f - Mathf.Min(0.3f, late * 0.02f)));
            Game.Wallet.Add(pay, $"Paid: {j.Title}");
            LifetimeRevenue += pay;
            int repGain = Mathf.RoundToInt(Mathf.Lerp(-3f, 3f, Mathf.Clamp01(quality)));
            if (repGain > 1 && j.Payout < SmallJobPayout) repGain = 1;      // a small service call earns less word of mouth than a deck
            Reputation += repGain - (late > 0 ? 1 : 0);
            foreach (var w in Workers) if (w.AssignedJobId == j.Id) w.AssignedJobId = null;
            Game.Toast($"Job done: {j.Title}. Paid ${pay:N0}");
            JobCompleted?.Invoke(j); RecomputeStage(); Changed?.Invoke();
        }

        /// <summary>Stage follows what the company actually owns and has done; it never goes down.</summary>
        public static void RecomputeStage()
        {
            var s = CompanyStage.Handyman;
            if (HasWarehouse && Vehicles.Exists(v => v.TypeId != "van")) s = CompanyStage.Contractor;
            if (s >= CompanyStage.Contractor && Workers.Count >= 2) s = CompanyStage.Crew;
            if (s >= CompanyStage.Crew && Reputation >= 65 && WarehouseLevel >= 2) s = CompanyStage.Builder;
            if (s >= CompanyStage.Builder && Reputation >= 80 && WarehouseLevel >= 3 && Workers.Count >= 8) s = CompanyStage.Developer;
            if (s > Stage)
            {
                Stage = s;
                Game.Toast($"Company grew: now a {Stage}!");
                StageChanged?.Invoke(Stage);
            }
            Changed?.Invoke();
        }

        /// <summary>Called every frame by CompanyClock; charges daily costs when the game day rolls over.</summary>
        public static void Tick()
        {
            if (GameClock.I == null) return;
            int day = GameClock.I.Day;
            if (_lastDay < 0) { _lastDay = day; return; }
            if (day == _lastDay) return;
            _lastDay = day;
            if (GameSettings.IsCareer && !GameSettings.UnlimitedMoney)
                foreach (var (label, amount) in DailyCostBreakdown())
                    if (!Game.Wallet.TrySpend(amount, label)) { Reputation -= 1; Game.Toast($"Couldn't pay {label} (${amount:N0})! Reputation -1"); }
            DayStarted?.Invoke(day);
            Changed?.Invoke();
        }

        public static void ResetState()
        {
            Name = "My Construction Co."; Stage = CompanyStage.Handyman; HasWarehouse = false; WarehouseLevel = 0;
            Vehicles.Clear(); Workers.Clear(); Offers.Clear(); ActiveJobs.Clear(); CompletedJobIds.Clear();
            JobsCompleted = 0; LifetimeRevenue = 0; _lastDay = -1;
        }

        // ------------------------------------------------------------------ save

        [Serializable]
        class Data
        {
            public string name; public int stage, whLevel, jobs, revenue; public bool wh;
            public List<OwnedVehicle> vehicles = new List<OwnedVehicle>();
            public List<Worker> workers = new List<Worker>();
            public List<Job> offers = new List<Job>(), active = new List<Job>();
            public List<string> done = new List<string>();
        }

        static string Save() => JsonUtility.ToJson(new Data
        {
            name = Name, stage = (int)Stage, wh = HasWarehouse, whLevel = WarehouseLevel, jobs = JobsCompleted, revenue = LifetimeRevenue,
            vehicles = new List<OwnedVehicle>(Vehicles), workers = new List<Worker>(Workers),
            offers = new List<Job>(Offers), active = new List<Job>(ActiveJobs), done = new List<string>(CompletedJobIds),
        });

        static void Load(string json)
        {
            ResetState();
            if (string.IsNullOrEmpty(json)) { Changed?.Invoke(); return; }
            var d = JsonUtility.FromJson<Data>(json);
            Name = d.name; Stage = (CompanyStage)d.stage; HasWarehouse = d.wh; WarehouseLevel = d.whLevel; JobsCompleted = d.jobs; LifetimeRevenue = d.revenue;
            Vehicles.AddRange(d.vehicles); Workers.AddRange(d.workers); Offers.AddRange(d.offers); ActiveJobs.AddRange(d.active); CompletedJobIds.AddRange(d.done);
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset()
        {
            ResetState(); _dailyCosts.Clear();
            Changed = null; StageChanged = null; JobAccepted = null; JobCompleted = null; DayStarted = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension("company", Save, Load);
            var go = new GameObject("CompanyClock");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<CompanyClock>();
        }
    }

    public class CompanyClock : MonoBehaviour
    {
        void Update() { Company.Tick(); }
    }
}
