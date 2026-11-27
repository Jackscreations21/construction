using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// The job catalog: every job definition (service calls, remodel scopes, contractor builds on Fleet's verified job types, and
    /// new buildings from blueprint plans), the offer generator that feeds <see cref="Company.Offers"/>, and job creation through the
    /// existing systems (RepairJobs for service/scope, ContractorJobs for fleet, Development Projects for builds). Docs/JOBS.md.
    /// </summary>
    public static partial class JobCatalog
    {
        static readonly List<JobDef> _defs = new List<JobDef>();
        static readonly Dictionary<string, JobDef> _byId = new Dictionary<string, JobDef>();
        static readonly List<string> _recent = new List<string>();
        static int _nextId = 1;
        static bool _built;

        public const int RecentMemory = 16;
        public const string ServicePrefix = "jc-", FleetKey = "-jc_";

        public static IReadOnlyList<JobDef> All { get { EnsureBuilt(); return _defs; } }
        public static JobDef Get(string id) { EnsureBuilt(); return id != null && _byId.TryGetValue(id, out var d) ? d : null; }
        public static IReadOnlyList<string> Recent => _recent;

        /// <summary>Adds definitions (feature folders may call this; the same id replaces).</summary>
        public static void Register(params JobDef[] defs)
        {
            foreach (var d in defs)
            {
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                if (_byId.TryGetValue(d.Id, out var old)) _defs.Remove(old);
                _defs.Add(d); _byId[d.Id] = d;
            }
        }

        static void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            RegisterParts();
            DefinePlumbing(); DefineElectrical(); DefineHvac(); DefineCarpentry(); DefineDrywall(); DefineExterior(); DefineMasonry(); DefineFlooring();
            DefineScopeTasks(); DefineScopes(); DefineFleet(); DefineBuilds(); DefineLegacy();
        }

        // ------------------------------------------------------------------ builders used by the definition files

        static JobDef Svc(string id, string name, CompanyStage stage, params JobTrade[] trades)
        {
            var d = new JobDef { Id = id, Name = name, Stage = stage, Mode = JobMode.Service, Kind = JobKind.Repair, Category = trades.Length > 0 ? trades[0].ToString() : "" };
            d.Trade(trades);
            Register(d);
            return d;
        }

        /// <summary>A task inside a remodel scope (never offered on its own).</summary>
        static JobDef Task(string id, string name, params JobTrade[] trades)
        {
            var d = Svc(id, name, CompanyStage.Contractor, trades);
            d.Kind = JobKind.Remodel; d.Weight = 0f; d.Category = "task";
            return d;
        }

        public static bool IsTask(JobDef d) => d != null && d.Category == "task";
        public static bool IsLegacy(JobDef d) => d != null && d.Category == "legacy";

        // ------------------------------------------------------------------ describing (RepairJobs hooks)

        static int Hash(string s) { int h = 17; if (s != null) foreach (char ch in s) h = h * 31 + ch; return h & 0x7fffffff; }

        /// <summary>What the customer says, stable for a job.</summary>
        public static string Complaint(JobDef d, DefectState s)
        {
            if (d == null || d.Complaints.Count == 0) return d != null ? d.Name : "";
            int k = Hash((s != null ? s.jobId : "") + d.Id) % d.Complaints.Count;
            return d.Complaints[k];
        }

        public static (string title, string symptom) Describe(DefectState s)
        {
            var d = Get(s.def);
            return d == null ? ("Repair", "") : (d.Name, Complaint(d, s));
        }

        public static string[] ToolsFor(DefectState s)
        {
            var d = Get(s.def);
            return d == null ? new string[0] : d.RequiredTools.ToArray();
        }

        // ------------------------------------------------------------------ season / weather

        /// <summary>Game month 0..11. The career starts in April; 30-day months.</summary>
        public static int Month(int day) => ((Mathf.Max(1, day) - 1) / 30 + 3) % 12;
        public static Seasons SeasonOf(int day)
        {
            int m = Month(day);
            return m == 11 || m <= 1 ? Seasons.Winter : m <= 4 ? Seasons.Spring : m <= 7 ? Seasons.Summer : Seasons.Fall;
        }

        static int Today => GameClock.I != null ? GameClock.I.Day : 1;

        /// <summary>How likely the generator is to pick a definition today (0 = not eligible).</summary>
        public static float WeightFor(JobDef d, CompanyStage stage, int rep, int day, bool raining, float tempF)
        {
            if (d == null || d.Weight <= 0f || IsTask(d)) return 0f;
            if (d.Stage > stage || rep < d.MinRep) return 0f;
            if (d.Mode == JobMode.Build && PlanFor(d) == null && string.IsNullOrEmpty(d.FallbackFleet)) return 0f;
            if ((d.Season & SeasonOf(day)) == 0) return 0f;
            float w = d.Weight;
            int gap = (int)stage - (int)d.Stage;
            if (gap >= 2) w *= 0.35f; else if (gap == 1) w *= 0.7f;
            if (raining) w *= d.RainBoost;
            if (tempF < 40f) w *= d.ColdBoost;
            if (tempF > 85f) w *= d.HeatBoost;
            return w;
        }

        // ------------------------------------------------------------------ which job a Company.Job came from

        public static JobDef DefOf(Job j)
        {
            if (j == null || string.IsNullOrEmpty(j.Id)) return null;
            if (j.Id.StartsWith(ContractorJobs.IdPrefix))
            {
                int k = j.Id.IndexOf(FleetKey, StringComparison.Ordinal); if (k < 0) return null;
                int e = j.Id.LastIndexOf("-L", StringComparison.Ordinal);
                if (e <= k) return null;
                string key = j.Id.Substring(k + FleetKey.Length, e - k - FleetKey.Length);
                foreach (var d in All) if ((d.Mode == JobMode.Fleet || d.Mode == JobMode.Build) && KeyOf(d) == key) return d;
                return null;
            }
            if (j.Kind == JobKind.House) { var b = ReadBuild(j); return b != null ? Get(b.catalog) : null; }
            var p = RepairJobs.IsService(j) ? RepairJobs.Payload(j) : null;
            if (p != null && p.defects.Count > 0)
            {
                if (!string.IsNullOrEmpty(p.scope)) return Get(p.scope);
                var s0 = p.defects[0];
                if (s0.type != (int)RepairType.Catalog)
                {
                    foreach (var d in All) if (IsLegacy(d) && (int)d.LegacyType == s0.type && (int)d.Where.Spot == s0.spot) return d;
                    return null;
                }
                return Get(s0.def);
            }
            return null;
        }

        static string KeyOf(JobDef d) => d.Id.Replace('.', '_').Replace('-', '_');

        // ------------------------------------------------------------------ the generator

        static bool AddressBusy(string address)
        {
            foreach (var j in Company.Offers) if (j.Address == address) return true;
            foreach (var j in Company.ActiveJobs) if (j.Address == address) return true;
            if (RepairJobs.HasPendingCallback(address)) return true;   // the callback job will land at this house
            return false;
        }

        static bool OnBoard(JobDef d)
        {
            foreach (var j in Company.Offers) if (DefOf(j) == d) return true;
            foreach (var j in Company.ActiveJobs) if (DefOf(j) == d) return true;
            return false;
        }

        /// <summary>Service calls wanted on the board for a stage and reputation.</summary>
        public static int ServiceTarget(CompanyStage s, int rep) => s == CompanyStage.Handyman ? (rep < 40 ? 2 : rep < 70 ? 3 : 5) : s <= CompanyStage.Crew ? 2 + (rep >= 60 ? 1 : 0) : 2;
        public static int ContractTarget(CompanyStage s) => s < CompanyStage.Contractor ? 0 : s == CompanyStage.Contractor ? 3 : 4;
        public static int BuildTarget(CompanyStage s) => s < CompanyStage.Builder ? 0 : s == CompanyStage.Builder ? 2 : 3;

        static int Count(Func<JobDef, bool> f)
        {
            int n = 0;
            foreach (var j in Company.Offers) { var d = DefOf(j); if (d != null && f(d) && !j.Title.StartsWith("Callback")) n++; }
            return n;
        }

        /// <summary>Weighted pick among eligible definitions of the given modes, skipping recent picks and jobs already on the board.</summary>
        public static JobDef Pick(Func<JobDef, bool> filter, System.Random rng = null, CompanyStage? stage = null, int? rep = null)
        {
            var st = stage ?? Company.Stage; int r = rep ?? Company.Reputation; int day = Today;
            bool rain = Weather.IsRaining; float temp = Weather.TemperatureF;
            var pool = new List<(JobDef d, float w)>(); float total = 0f;
            for (int pass = 0; pass < 2 && pool.Count == 0; pass++)
                foreach (var d in All)
                {
                    if (!filter(d)) continue;
                    if (pass == 0 && (_recent.Contains(d.Id) || OnBoard(d))) continue;
                    float w = WeightFor(d, st, r, day, rain, temp);
                    if (w <= 0f) continue;
                    pool.Add((d, w)); total += w;
                }
            if (pool.Count == 0) return null;
            float x = (float)(rng != null ? rng.NextDouble() : UnityEngine.Random.value) * total;
            foreach (var (d, w) in pool) { x -= w; if (x <= 0f) return d; }
            return pool[pool.Count - 1].d;
        }

        static void Remember(JobDef d)
        {
            _recent.Remove(d.Id); _recent.Add(d.Id);
            while (_recent.Count > RecentMemory) _recent.RemoveAt(0);
        }

        /// <summary>Tops up the job board for the company's stage: service calls, contractor work and new builds. Replaces
        /// RepairJobs' built-in generator (RepairJobs.ExternalGenerator).</summary>
        public static void Generate()
        {
            if (!GameSettings.IsCareer && !ForceGenerate) return;
            var st = Company.Stage; int rep = Company.Reputation;
            int guard = 0;
            for (int have = Count(d => d.Mode == JobMode.Service); have < ServiceTarget(st, rep) && guard++ < 12;)
            {
                var d = Pick(x => x.Mode == JobMode.Service);
                if (d == null) break;
                var lot = FreeAddressFor(d);
                if (lot == null) { Remember(d); continue; }
                var defs = new List<JobDef> { d };
                if (st == CompanyStage.Handyman && rep >= 60 && UnityEngine.Random.value < 0.3f)
                {
                    var d2 = Pick(x => x.Mode == JobMode.Service && x != d && !IsLegacy(x) && x.Where != null && d.Where != null && x.Where.Name != d.Where.Name && x.Stage == CompanyStage.Handyman);
                    if (d2 != null) defs.Add(d2);
                }
                if (CreateService(lot, true, null, defs.ToArray()) != null) { foreach (var x in defs) Remember(x); have++; }
            }
            guard = 0;
            for (int have = Count(d => d.Mode == JobMode.Scope || d.Mode == JobMode.Fleet); have < ContractTarget(st) && guard++ < 12;)
            {
                var d = Pick(x => x.Mode == JobMode.Scope || x.Mode == JobMode.Fleet);
                if (d == null) break;
                Job j = null;
                try { j = d.Mode == JobMode.Scope ? CreateScope(d, FreeAddressFor(d), true) : CreateFleet(d, UnityEngine.Random.Range(1, 1000000), true); }
                catch (Exception e) { Debug.LogException(e); }
                Remember(d);
                if (j != null) have++;
            }
            guard = 0;
            for (int have = Count(d => d.Mode == JobMode.Build); have < BuildTarget(st) && guard++ < 8;)
            {
                var d = Pick(x => x.Mode == JobMode.Build);
                if (d == null) break;
                Job j = null;
                try { j = CreateBuild(d, true); } catch (Exception e) { Debug.LogException(e); }
                Remember(d);
                if (j != null) have++;
            }
            Company.NotifyChanged();
        }

        /// <summary>Lets tests run the generator outside Career mode.</summary>
        public static bool ForceGenerate;

        /// <summary>A customer address with no job on it whose house has every spot the definition needs.</summary>
        /// <summary>Optional: chooses among the free addresses (MapFollowup spreads calls over the map, weighted by travel).</summary>
        public static Func<List<TownLot>, TownLot> AddressPicker;

        public static TownLot FreeAddressFor(JobDef d)
        {
            var free = new List<TownLot>();
            foreach (var l in Town.Addresses)
            {
                if (AddressBusy(l.Address)) continue;
                if (l.House == null || !Fits(l.House, d)) continue;   // every catalog place is anchored on an enterable TownHouse (map lots without one get no service calls)
                free.Add(l);
            }
            if (free.Count == 0) return null;
            if (AddressPicker != null) { var picked = AddressPicker(free); if (picked != null) return picked; }
            return free[UnityEngine.Random.Range(0, free.Count)];
        }

        public static bool Fits(TownHouse h, JobDef d)
        {
            if (h == null || d == null) return false;
            if (d.Mode == JobMode.Scope) { foreach (var t in d.Tasks) if (!Fits(h, Get(t))) return false; return true; }
            if (d.Where == null) return true;
            foreach (var s in d.Where.Needs) if (!h.Has(s)) return false;
            return true;
        }

        // ------------------------------------------------------------------ creating jobs

        static int PickCause(JobDef d, System.Random rng)
        {
            float total = 0f; int rep = Company.Reputation;
            foreach (var c in d.Causes) if (rep >= c.MinRep) total += c.Weight;
            float x = (float)(rng != null ? rng.NextDouble() : UnityEngine.Random.value) * total;
            for (int i = 0; i < d.Causes.Count; i++)
            {
                if (rep < d.Causes[i].MinRep) continue;
                x -= d.Causes[i].Weight;
                if (x <= 0f) return i;
            }
            return 0;
        }

        /// <summary>Short random tag so job ids never collide with an earlier session's ids (RepairJobs caches payloads by id).</summary>
        static string UniqueTag() => Guid.NewGuid().ToString("N").Substring(0, 5);

        static int Round5(float v) => Mathf.RoundToInt(v / 5f) * 5;

        /// <summary>Service call at a house: one defect per definition. causes: fixed cause indices (tests) or null for random.</summary>
        public static Job CreateService(TownLot lot, bool addToOffers, int[] causes, params JobDef[] defs)
        {
            if (lot == null || defs == null || defs.Length == 0) return null;
            foreach (var d in defs) if (IsLegacy(d)) return CreateLegacy(d, lot, addToOffers, causes != null && causes.Length > 0 ? causes[0] : -1);
            var p = new RepairPayload { lotId = lot.Id };
            float quote = RepairJobs.TripCharge; int mats = 0;
            var titles = new List<string>(); var says = new List<string>();
            var job = new Job { Id = ServicePrefix + _nextId++ + "-" + UniqueTag(), Kind = JobKind.Repair };
            CompanyStage stage = CompanyStage.Handyman; int minRep = 0; string vehicle = HandymanStart.VanTypeId;
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                int cause = causes != null && i < causes.Length && causes[i] >= 0 ? Mathf.Min(causes[i], d.Causes.Count - 1) : PickCause(d, null);
                var s = new DefectState { type = (int)RepairType.Catalog, spot = (int)d.Where.Spot, cause = cause, def = d.Id, jobId = job.Id };
                p.defects.Add(s);
                titles.Add(d.Name); says.Add(Complaint(d, s));
                var c = d.Causes[cause];
                float mc = d.MaterialCostFor(c);
                quote += c.Hours * RepairJobs.LaborRate + mc * RepairJobs.Markup + d.Callout; mats += Mathf.RoundToInt(mc);
                if (d.Stage > stage) stage = d.Stage;
                minRep = Mathf.Max(minRep, d.MinRep);
                if (!string.IsNullOrEmpty(d.Vehicle)) vehicle = d.Vehicle;
            }
            job.MinStage = stage; job.MinReputation = minRep; job.RequiredVehicleType = vehicle;
            job.Client = lot.Resident; job.Address = lot.Address; job.WorldPos = lot.Parking;
            job.Title = titles.Count == 1 ? titles[0] : $"Punch list: {titles.Count} repairs";
            job.Description = "\"" + string.Join(" Also: ", says) + "\"";
            job.DueDay = Today + UnityEngine.Random.Range(2, 5);
            job.Payout = Round5(quote); job.MaterialBudget = mats;
            job.Payload = JsonUtility.ToJson(p);
            if (addToOffers) { Company.Offers.Add(job); Company.NotifyChanged(); }
            return job;
        }

        /// <summary>Remodel scope: every task becomes a defect at the house; billed time and materials through RepairJobs.</summary>
        public static Job CreateScope(JobDef d, TownLot lot, bool addToOffers)
        {
            if (d == null || lot == null || d.Mode != JobMode.Scope) return null;
            var p = new RepairPayload { lotId = lot.Id, scope = d.Id };
            var job = new Job { Id = RepairJobs.ScopePrefix + _nextId++ + "-" + UniqueTag(), Kind = JobKind.Remodel };
            float quote = RepairJobs.TripCharge + d.Callout, hours = 0f; int mats = 0;
            var lines = new List<string>();
            foreach (var tid in d.Tasks)
            {
                var t = Get(tid); if (t == null) continue;
                var s = new DefectState { type = (int)RepairType.Catalog, spot = (int)t.Where.Spot, cause = 0, def = t.Id, jobId = job.Id };
                p.defects.Add(s);
                var c = t.Causes[0]; float mc = t.MaterialCostFor(c);
                quote += c.Hours * RepairJobs.LaborRate + mc * RepairJobs.Markup; mats += Mathf.RoundToInt(mc); hours += c.Hours;
                lines.Add(t.Name);
            }
            job.Title = d.Name; job.Client = lot.Resident; job.Address = lot.Address; job.WorldPos = lot.Parking;
            job.Description = "\"" + Complaint(d, new DefectState { jobId = job.Id }) + "\" Scope: " + string.Join("; ", lines) + $". About {Mathf.CeilToInt(hours)} labor hours, billed time and materials.";
            job.MinStage = d.Stage; job.MinReputation = d.MinRep; job.RequiredVehicleType = d.Vehicle ?? "pickup";
            job.DueDay = Today + 3 + Mathf.CeilToInt(hours / 8f) * 2;
            job.Payout = Round5(quote); job.MaterialBudget = mats;
            job.Payload = JsonUtility.ToJson(p);
            if (addToOffers) { Company.Offers.Add(job); Company.NotifyChanged(); }
            return job;
        }

        /// <summary>Contract price of a fleet job from its definition.</summary>
        public static int FleetPrice(JobDef d)
        {
            float units = d.FleetType == "fence" ? d.L : d.FleetType == "reroof" ? Mathf.Round(ContractorJobs.RoofAreaSqFt(d.L, d.W, d.Pitch) / 100f) : d.W * d.L;
            return Mathf.RoundToInt((units * d.RatePerUnit + d.Callout) / 10f) * 10;
        }

        /// <summary>A Fleet contractor job (verified by ContractorJobs) sized and priced by the definition.</summary>
        public static Job CreateFleet(JobDef d, int seed, bool addToOffers)
        {
            if (d == null || d.Mode != JobMode.Fleet) return null;
            var j = ContractorJobs.Create(d.FleetType, seed);
            var s = ContractorJobs.SpecOf(j);
            if (s == null) return null;
            s.widthFt = d.W; s.lengthFt = d.L;
            if (d.H > 0) s.heightIn = d.H;
            if (d.Thick > 0) s.thicknessIn = d.Thick;
            if (d.Psi > 0) s.psi = d.Psi;
            if (d.FleetType == "fence") s.gates = Mathf.Max(1, d.Gates);
            s.pitch = d.Pitch;
            if (d.FleetType == "reroof") s.squares = Mathf.Round(ContractorJobs.RoofAreaSqFt(d.L, d.W, d.Pitch) / 100f);
            j.Payload = JsonUtility.ToJson(s);
            int lot = s.lotIndex;
            j.Id = $"{ContractorJobs.IdPrefix}{d.FleetType}{FleetKey}{KeyOf(d)}-L{lot}";
            j.Title = FleetTitle(d);
            if (d.Complaints.Count > 0) j.Description = d.Complaints[0] + " " + j.Description;
            j.Payout = FleetPrice(d);
            j.MinStage = d.Stage; j.MinReputation = Mathf.Max(j.MinReputation, d.MinRep);
            if (!string.IsNullOrEmpty(d.Vehicle)) j.RequiredVehicleType = d.Vehicle;
            if (d.DaysToBuild > 0) j.DueDay = Today + 2 + d.DaysToBuild;
            j.MaterialBudget = ContractorJobs.TakeoffCost(j);
            if (addToOffers) { Company.Offers.Add(j); Company.NotifyChanged(); }
            return j;
        }

        static string FleetTitle(JobDef d)
        {
            switch (d.FleetType)
            {
                case "fence": return $"{d.L:0} LF {d.Name}";
                case "reroof": return $"{d.Name}, {Mathf.Round(ContractorJobs.RoofAreaSqFt(d.L, d.W, d.Pitch) / 100f):0} squares";
                default: return $"{d.W:0}' x {d.L:0}' {d.Name}";
            }
        }

        // ------------------------------------------------------------------ legacy repair families (Scripts/Repairs) in the same generator

        static JobDef Legacy(string id, string name, RepairType type, HouseSpot spot, int causes, int rep, params JobTrade[] trades)
        {
            var d = Svc(id, name, CompanyStage.Handyman, trades);
            d.Category = "legacy"; d.LegacyType = type; d.LegacyCauses = causes; d.MinRep = rep;
            d.Where = new JobPlace { Name = spot.ToString(), Spot = spot, Needs = new[] { spot } };
            for (int i = 0; i < causes; i++) d.Cause("c" + i, "cause " + i, 1f, 1f, "");
            d.Weight = 0.8f;
            return d;
        }

        static Job CreateLegacy(JobDef d, TownLot lot, bool addToOffers, int cause)
        {
            if (cause < 0) cause = UnityEngine.Random.Range(0, d.LegacyCauses);
            if (d.LegacyType == RepairType.DrywallHole && Company.Reputation < 40) cause = DrywallHoleDefect.Small;
            return RepairJobs.CreateJob(lot, addToOffers, (d.LegacyType, d.Where.Spot, cause));
        }

        static void DefineLegacy()
        {
            Legacy("legacy.sink-leak-kitchen", "Leak under kitchen sink", RepairType.SinkLeak, HouseSpot.KitchenSink, 3, 0, JobTrade.Plumbing);
            Legacy("legacy.sink-leak-bath", "Leak under bathroom vanity", RepairType.SinkLeak, HouseSpot.BathVanity, 3, 0, JobTrade.Plumbing);
            Legacy("legacy.dead-outlet", "Dead outlet", RepairType.DeadOutlet, HouseSpot.OutletLiving, 4, 0, JobTrade.Electrical);
            Legacy("legacy.dead-outlet-kitchen", "Dead kitchen outlet", RepairType.DeadOutlet, HouseSpot.OutletKitchen, 3, 0, JobTrade.Electrical);
            Legacy("legacy.drywall-hole-living", "Hole in the living room wall", RepairType.DrywallHole, HouseSpot.DrywallLiving, 2, 0, JobTrade.Drywall);
            Legacy("legacy.drywall-hole-bedroom", "Hole in the bedroom wall", RepairType.DrywallHole, HouseSpot.DrywallBedroom, 2, 0, JobTrade.Drywall);
            Legacy("legacy.sticking-door", "Sticking bedroom door", RepairType.StickingDoor, HouseSpot.InteriorDoor, 2, 0, JobTrade.Carpentry);
            Legacy("legacy.deck-board", "Broken deck board", RepairType.DeckBoard, HouseSpot.DeckBoard, 1, 0, JobTrade.Carpentry);
            Legacy("legacy.fence-picket", "Broken fence picket", RepairType.FencePicket, HouseSpot.FencePicket, 1, 0, JobTrade.Carpentry);
            Legacy("legacy.shingles", "Missing shingles", RepairType.Shingles, HouseSpot.RoofShingles, 1, 40, JobTrade.Roofing);
            Legacy("legacy.window-caulk", "Window caulking", RepairType.Caulk, HouseSpot.WindowCaulk, 1, 0, JobTrade.Exterior);
            Legacy("legacy.tub-caulk", "Moldy tub caulk", RepairType.Caulk, HouseSpot.TubCaulk, 1, 0, JobTrade.Tile);
            Legacy("legacy.gutter", "Overflowing gutter", RepairType.Gutter, HouseSpot.Gutter, 2, 40, JobTrade.Exterior);
            Legacy("legacy.sidewalk", "Broken sidewalk square", RepairType.Sidewalk, HouseSpot.SidewalkSquare, 1, 55, JobTrade.Concrete);
        }

        // ------------------------------------------------------------------ playing a job through the public API (tests, crew NPCs)

        static bool Usable(CatalogDefect d, StepDef s) { var a = d.AvailableActions.Find(x => x.Id == s.Id); return a != null; }

        /// <summary>Does the next sensible step of a catalog defect: diagnose, open what the fix needs, the fix steps, close up, finish.
        /// Returns the step done, or null when nothing is left. Uses <see cref="RepairDefect.Perform(string, RepairContext, out string)"/>.</summary>
        public static string NextStep(CatalogDefect d, RepairContext ctx, out string msg)
        {
            msg = null;
            var def = d.Def ?? Get(d.S.def); var cause = d.ActualCause;
            if (def == null || cause == null) return null;
            // 1. diagnose once (opening what the check needs first)
            if (!d.HasState("diagnosed"))
                foreach (var s in def.Steps)
                {
                    if (s.Kind != StepKind.Diagnose || s.Test) continue;
                    if (Usable(d, s)) return Do(d, s, ctx, out msg);
                    foreach (var o in s.Need) if (!d.HasState(o)) { var os = def.Step(o); if (os != null && Usable(d, os)) return Do(d, os, ctx, out msg); }
                    foreach (var o in s.Need) if (!d.HasState(o)) { var os = def.Step(o); if (os != null) foreach (var o2 in os.Need) if (!d.HasState(o2)) { var os2 = def.Step(o2); if (os2 != null && Usable(d, os2)) return Do(d, os2, ctx, out msg); } }
                    break;
                }
            // 2. the unfinished fix steps and everything they depend on (earlier steps, open states, guards)
            if (!d.CauseFixed)
            {
                var need = new List<string>();
                void Want(string id, int depth)
                {
                    if (depth > 10 || need.Contains(id)) return;
                    var st = def.Step(id); if (st == null) return;
                    if (!st.Opens && d.DidStep(id)) return;
                    need.Add(id);
                    foreach (var a in st.After) Want(a, depth + 1);
                    foreach (var a in st.Need) Want(a, depth + 1);
                    if (!string.IsNullOrEmpty(st.Guard)) Want(st.Guard, depth + 1);
                }
                foreach (var f in cause.Fix) if (!d.DidStep(f) || def.Step(f)?.Opens == true && !d.HasState(f) && !d.DidStep(f)) Want(f, 0);
                foreach (var s in def.Steps)
                {
                    if (!need.Contains(s.Id)) continue;
                    if (s.Opens ? d.HasState(s.Id) : d.DidStep(s.Id)) continue;
                    if (Usable(d, s)) return Do(d, s, ctx, out msg);
                }
                return null;
            }
            // 3. close everything that is open
            foreach (var s in def.Steps)
                if (!string.IsNullOrEmpty(s.Closes) && d.HasState(s.Closes) && Usable(d, s)) return Do(d, s, ctx, out msg);
            // 4. finish steps, then a test
            foreach (var s in def.Steps)
                if (s.Kind == StepKind.Finish && !s.Test && !s.Optional && string.IsNullOrEmpty(s.Closes) && !d.DidStep(s.Id) && Usable(d, s)) return Do(d, s, ctx, out msg);
            foreach (var s in def.Steps)
                if (s.Test && !d.DidStep(s.Id) && Usable(d, s)) return Do(d, s, ctx, out msg);
            return null;
        }

        static string Do(CatalogDefect d, StepDef s, RepairContext ctx, out string msg) { d.Perform(s.Id, ctx, out msg); return s.Id; }

        /// <summary>Plays a catalog defect to the end. Returns the steps done.</summary>
        public static List<string> Solve(CatalogDefect d, RepairContext ctx = null, int maxSteps = 60)
        {
            ctx = ctx ?? RepairContext.Bypass;
            var done = new List<string>();
            for (int i = 0; i < maxSteps; i++) { var s = NextStep(d, ctx, out _); if (s == null) break; done.Add(s); }
            return done;
        }

        // ------------------------------------------------------------------ save

        [Serializable] class SaveData { public List<string> recent = new List<string>(); public int nextId = 1; }

        static string Save() => JsonUtility.ToJson(new SaveData { recent = new List<string>(_recent), nextId = _nextId });
        static void Load(string json)
        {
            _recent.Clear(); _nextId = 1;
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<SaveData>(json);
            if (d.recent != null) _recent.AddRange(d.recent);
            _nextId = Mathf.Max(1, d.nextId);
        }

        public static void ResetState() { _recent.Clear(); _nextId = 1; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _recent.Clear(); _nextId = 1; ForceGenerate = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            EnsureBuilt();
            SaveSystem.RegisterExtension("jobcatalog", Save, Load);
            RepairJobs.ExternalGenerator = Generate;
            Company.StageChanged += s => { if (GameSettings.IsCareer) Generate(); };
            Company.JobAccepted += OnBuildAccepted;
        }
    }
}
