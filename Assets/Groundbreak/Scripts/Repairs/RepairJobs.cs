using System;
using System.Collections.Generic;
using UnityEngine;
using C = Groundbreak.RepairCatalog;

namespace Groundbreak
{
    /// <summary>What a repair Job carries in <see cref="Job.Payload"/>: the house and its defects with live progress.</summary>
    [Serializable]
    public class RepairPayload
    {
        public string lotId;
        public bool callback;
        public List<DefectState> defects = new List<DefectState>();
    }

    [Serializable]
    public class RepairInvoice
    {
        public string jobId, title, client, address;
        public int day, labor, materials, trip, total, paid, tip;
        public float satisfaction;
        public bool callback, willCallBack;
        public List<string> lines = new List<string>();
    }

    /// <summary>Service-call generator, defect spawning, invoicing, callbacks and the "repairs" save extension.</summary>
    public static class RepairJobs
    {
        public const int LaborRate = 85, TripCharge = 65;
        public const float Markup = 1.2f;

        [Serializable] class PendingCallback { public int day; public string title, client; public RepairPayload payload; }
        [Serializable]
        class Data
        {
            public List<PendingCallback> callbacks = new List<PendingCallback>();
            public List<RepairInvoice> invoices = new List<RepairInvoice>();
            public bool hasWp; public Vector3 wp; public string wpLabel; public int nextId;
        }

        static readonly Dictionary<string, RepairPayload> _payloads = new Dictionary<string, RepairPayload>();
        static readonly List<PendingCallback> _callbacks = new List<PendingCallback>();
        static readonly List<RepairInvoice> _invoices = new List<RepairInvoice>();
        static readonly Dictionary<int, (string title, string symptom)> _desc = new Dictionary<int, (string, string)>();
        static int _nextId = 1;

        public static IReadOnlyList<RepairInvoice> Invoices => _invoices;
        public static int PendingCallbacks => _callbacks.Count;
        public static Vector3? Waypoint;
        public static string WaypointLabel = "";

        static int Today => GameClock.I != null ? GameClock.I.Day : 1;

        // ------------------------------------------------------------------ describing defects

        static readonly (RepairType type, HouseSpot spot, int causes, float hours, int mat, int minRep)[] Kinds =
        {
            (RepairType.SinkLeak, HouseSpot.KitchenSink, 3, 0.6f, 8, 0), (RepairType.SinkLeak, HouseSpot.BathVanity, 3, 0.6f, 8, 0),
            (RepairType.DeadOutlet, HouseSpot.OutletLiving, 4, 0.75f, 6, 0), (RepairType.DeadOutlet, HouseSpot.OutletKitchen, 3, 0.75f, 12, 0),
            (RepairType.DrywallHole, HouseSpot.DrywallLiving, 2, 2.2f, 45, 0), (RepairType.DrywallHole, HouseSpot.DrywallBedroom, 2, 2.2f, 45, 0),
            (RepairType.StickingDoor, HouseSpot.InteriorDoor, 2, 1f, 8, 0),
            (RepairType.DeckBoard, HouseSpot.DeckBoard, 1, 1f, 30, 0), (RepairType.FencePicket, HouseSpot.FencePicket, 1, 0.5f, 8, 0),
            (RepairType.Shingles, HouseSpot.RoofShingles, 1, 1.5f, 12, 40), (RepairType.Caulk, HouseSpot.WindowCaulk, 1, 0.75f, 6, 0),
            (RepairType.Caulk, HouseSpot.TubCaulk, 1, 1f, 10, 0), (RepairType.Gutter, HouseSpot.Gutter, 2, 1.75f, 4, 40),
            (RepairType.Sidewalk, HouseSpot.SidewalkSquare, 1, 6f, 160, 55),
        };

        /// <summary>Title and customer wording for a defect that isn't spawned (job offers, callbacks).</summary>
        public static (string title, string symptom) Describe(DefectState s)
        {
            int key = s.type * 10000 + s.spot * 100 + s.cause;
            if (_desc.TryGetValue(key, out var d)) return d;
            var go = new GameObject("RepairDescribe");
            go.SetActive(false);
            var def = RepairDefect.AddFor(go, (RepairType)s.type);
            def.S = new DefectState { type = s.type, spot = s.spot, cause = s.cause };
            d = (def.Title, def.Symptom);
            UnityEngine.Object.Destroy(go);
            _desc[key] = d;
            return d;
        }

        /// <summary>Tools a repair of this type normally needs (shown on offers so the player can buy them first).</summary>
        public static string[] ToolsFor(DefectState s)
        {
            switch ((RepairType)s.type)
            {
                case RepairType.SinkLeak: return new[] { C.Pliers, C.Wrench, C.Bucket };
                case RepairType.DeadOutlet: return new[] { C.Tester, C.Screwdriver, C.Meter };
                case RepairType.DrywallHole: return s.cause == DrywallHoleDefect.Large ? new[] { C.JabSaw, C.Drill, C.Knives, C.Sander, C.Brush, C.Roller } : new[] { C.Knife, C.Knives, C.Sander, C.Brush, C.Roller };
                case RepairType.StickingDoor: return new[] { C.Drill, C.Hammer, C.Plane, C.Brush };
                case RepairType.DeckBoard: return new[] { C.Screwdriver, C.Drill, C.PryBar, C.Saw };
                case RepairType.FencePicket: return new[] { C.PryBar, C.Hammer, C.Level, C.Drill };
                case RepairType.Shingles: return new[] { C.FlatBar, C.Hammer, C.CaulkGun };
                case RepairType.Caulk: return new[] { C.Knife, C.Bucket, C.CaulkGun };
                case RepairType.Gutter: return new[] { C.Scoop, C.Hose, C.Screwdriver, C.Drill };
                default: return new[] { C.Sledge, C.Shovel, C.Tamper, C.Hammer, C.MixHoe, C.MagFloat, C.Edger, C.Groover, C.Broom };
            }
        }

        /// <summary>Tools the player doesn't own yet for a job.</summary>
        public static List<string> MissingTools(Job job)
        {
            var miss = new List<string>();
            var p = Payload(job); if (p == null) return miss;
            foreach (var s in p.defects) foreach (var t in ToolsFor(s)) if (!C.Owns(t) && !miss.Contains(t)) miss.Add(t);
            return miss;
        }

        // ------------------------------------------------------------------ payloads

        public static RepairPayload Payload(Job job)
        {
            if (job == null || job.Kind != JobKind.Repair || string.IsNullOrEmpty(job.Payload)) return null;
            if (_payloads.TryGetValue(job.Id, out var p)) return p;
            try { p = JsonUtility.FromJson<RepairPayload>(job.Payload); } catch (Exception) { p = null; }
            if (p != null) _payloads[job.Id] = p;
            return p;
        }

        static void Sync(Job job) { if (job != null && _payloads.TryGetValue(job.Id, out var p)) job.Payload = JsonUtility.ToJson(p); }

        public static TownLot LotOf(Job job) { var p = Payload(job); return Town.Find(p != null ? p.lotId : job?.Address); }

        public static List<RepairDefect> DefectsFor(Job job)
        {
            var l = new List<RepairDefect>();
            if (job != null) foreach (var d in RepairDefect.All) if (d != null && d.Job != null && d.Job.Id == job.Id) l.Add(d);
            return l;
        }

        public static bool AllFixed(Job job)
        {
            var l = DefectsFor(job);
            if (l.Count == 0) return false;
            foreach (var d in l) if (!d.IsFixed) return false;
            return true;
        }

        // ------------------------------------------------------------------ generating offers

        /// <summary>Builds a repair job at a customer's house. Deterministic causes for tests and scripted jobs.</summary>
        public static Job CreateJob(TownLot lot, bool addToOffers, params (RepairType type, HouseSpot spot, int cause)[] defects)
        {
            var p = new RepairPayload { lotId = lot.Id };
            float quote = TripCharge; int mats = 0;
            var titles = new List<string>(); var says = new List<string>();
            foreach (var (type, spot, cause) in defects)
            {
                var s = new DefectState { type = (int)type, spot = (int)spot, cause = cause };
                p.defects.Add(s);
                var (t, sym) = Describe(s);
                titles.Add(t); says.Add(sym);
                foreach (var k in Kinds) if (k.type == type && k.spot == spot) { quote += k.hours * LaborRate + k.mat * Markup; mats += k.mat; break; }
            }
            var job = new Job
            {
                Id = "rep-" + _nextId++, Kind = JobKind.Repair, MinStage = CompanyStage.Handyman, RequiredVehicleType = HandymanStart.VanTypeId,
                Client = lot.Resident, Address = lot.Address, WorldPos = lot.Parking,
                Title = titles.Count == 1 ? titles[0] : $"Punch list: {titles.Count} repairs",
                Description = "\"" + string.Join(" Also: ", says) + "\"",
                DueDay = Today + UnityEngine.Random.Range(2, 5), Payout = Mathf.RoundToInt(quote / 5f) * 5, MaterialBudget = mats,
            };
            foreach (var s in p.defects) s.jobId = job.Id;
            _payloads[job.Id] = p;
            job.Payload = JsonUtility.ToJson(p);
            if (addToOffers) { Company.Offers.Add(job); Company.NotifyChanged(); }
            return job;
        }

        static bool AddressBusy(TownLot lot)
        {
            foreach (var j in Company.Offers) if (j.Address == lot.Address) return true;
            foreach (var j in Company.ActiveJobs) if (j.Address == lot.Address) return true;
            foreach (var c in _callbacks) if (c.payload != null && c.payload.lotId == lot.Id) return true;
            return false;
        }

        /// <summary>Tops up Company.Offers with service calls sized to the company's reputation (2 small calls under 40, up to 5 with punch lists at 70+).</summary>
        public static void GenerateOffers()
        {
            int rep = Company.Reputation;
            int target = rep < 40 ? 2 : rep < 70 ? 3 : 5;
            int have = 0;
            foreach (var j in Company.Offers) if (j.Kind == JobKind.Repair && !j.Title.StartsWith("Callback")) have++;
            var free = new List<TownLot>();
            foreach (var l in Town.Addresses) if (l.ServiceCalls && !AddressBusy(l)) free.Add(l);   // ServiceCalls: map buildings without a TownHouse are skipped
            for (; have < target && free.Count > 0; have++)
            {
                int li = UnityEngine.Random.Range(0, free.Count);
                var lot = free[li]; free.RemoveAt(li);
                int n = rep >= 70 ? UnityEngine.Random.Range(1, 5) : rep >= 50 && UnityEngine.Random.value < 0.3f ? 2 : 1;
                var pool = new List<int>();
                for (int i = 0; i < Kinds.Length; i++) if (rep >= Kinds[i].minRep) pool.Add(i);
                var picks = new List<(RepairType, HouseSpot, int)>();
                bool outlet = false;
                while (picks.Count < n && pool.Count > 0)
                {
                    int pi = UnityEngine.Random.Range(0, pool.Count);
                    var k = Kinds[pool[pi]]; pool.RemoveAt(pi);
                    if (k.type == RepairType.DeadOutlet) { if (outlet) continue; outlet = true; }
                    if (k.type == RepairType.Sidewalk && n > 1) continue;                 // a day's work on its own
                    int cause = UnityEngine.Random.Range(0, k.causes);
                    if (k.type == RepairType.DrywallHole && rep < 40) cause = DrywallHoleDefect.Small;
                    picks.Add((k.type, k.spot, cause));
                }
                if (picks.Count > 0) CreateJob(lot, true, picks.ToArray());
            }
            Company.NotifyChanged();
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>Puts a job's defects on its house (needs the 3D town). False when the house isn't there yet.</summary>
        public static bool Spawn(Job job)
        {
            var p = Payload(job); if (p == null) return false;
            if (DefectsFor(job).Count > 0) return true;
            var lot = Town.Find(p.lotId);
            if (lot == null || lot.House == null) return false;
            foreach (var s in p.defects)
            {
                s.jobId = job.Id;
                try { RepairDefect.Create(s, job, lot.House); }
                catch (Exception e) { Debug.LogError($"[Repairs] could not spawn {(RepairType)s.type} at {lot.Address}: {e}"); }
            }
            return true;
        }

        /// <summary>Spawns defects for accepted repair jobs that have none (after a load or when the town is rebuilt).</summary>
        public static void Respawn()
        {
            if (!Town.Exists) return;
            foreach (var j in Company.ActiveJobs) if (j.Kind == JobKind.Repair) Spawn(j);
        }

        public static void DestroyDefects(Job job) { foreach (var d in DefectsFor(job)) d.Remove(); }

        public static void DestroyAll()
        {
            foreach (var d in new List<RepairDefect>(RepairDefect.All)) if (d != null) d.Remove();
            RepairDefect.All.Clear();
        }

        static void OnAccepted(Job job)
        {
            if (job.Kind != JobKind.Repair) return;
            if (!Town.Exists) Town.Ensure();
            Spawn(job);
            var lot = LotOf(job);
            if (lot != null) SetWaypoint(lot);
        }

        /// <summary>Called by a defect after every action.</summary>
        public static void DefectChanged(RepairDefect d, bool justFixed)
        {
            Sync(d.Job);
            if (!justFixed) return;
            Sfx.Play("success", d.transform.position, 0.7f);
            if (AllFixed(d.Job)) Game.Toast("All repairs here are done. Send the invoice (B, or the Jobs app).");
        }

        // ------------------------------------------------------------------ waypoint

        public static void SetWaypoint(TownLot lot) { Waypoint = lot.Parking; WaypointLabel = lot.Address; }
        public static void ClearWaypoint() { Waypoint = null; WaypointLabel = ""; }

        // ------------------------------------------------------------------ invoice

        /// <summary>The invoice as it would be sent now (nothing is charged).</summary>
        public static RepairInvoice PreviewInvoice(Job job)
        {
            var p = Payload(job);
            var inv = new RepairInvoice { jobId = job.Id, title = job.Title, client = job.Client, address = job.Address, day = Today, callback = p != null && p.callback };
            var live = DefectsFor(job);
            int total = p != null ? Mathf.Max(1, p.defects.Count) : 1, fixedN = 0;
            float labor = 0f, mats = 0f, q = 0f, mess = 0f;
            foreach (var d in live)
            {
                bool ok = d.IsFixed;
                float hrs = ok ? d.BookHours : 0f, mc = d.MaterialCost * Markup;
                if (ok) { fixedN++; q += d.Quality; }
                mess += d.Mess;
                labor += hrs * LaborRate; mats += mc;
                inv.lines.Add(ok ? $"{d.Title}: {hrs:0.##} h x ${LaborRate} = ${hrs * LaborRate:0}" + (mc > 0.5f ? $", parts ${mc:0}" : "")
                                 : $"{d.Title}: NOT FIXED" + (mc > 0.5f ? $", parts used ${mc:0}" : ""));
                if (ok && d.Quality < 0.6f) inv.willCallBack = true;
                if (!ok) inv.willCallBack = true;
            }
            if (live.Count < total) inv.willCallBack = true;
            inv.labor = Mathf.RoundToInt(labor); inv.materials = Mathf.RoundToInt(mats); inv.trip = TripCharge;
            inv.total = inv.callback ? 0 : inv.labor + inv.materials + inv.trip;
            if (inv.callback) inv.lines.Add("Callback: no charge");
            bool onTime = job.DueDay <= 0 || Today <= job.DueDay;
            float avgQ = q / total, fixedFrac = fixedN / (float)total, avgMess = live.Count > 0 ? mess / live.Count : 0f;
            inv.satisfaction = Mathf.Clamp01(avgQ * 0.45f + fixedFrac * 0.35f + (onTime ? 0.1f : 0f) + (1f - avgMess) * 0.1f);
            int late = job.DueDay > 0 ? Mathf.Max(0, Today - job.DueDay) : 0;
            inv.paid = Mathf.RoundToInt(inv.total * Mathf.Lerp(0.6f, 1.1f, inv.satisfaction) * (1f - Mathf.Min(0.3f, late * 0.02f)));   // what Company.Complete will pay
            inv.tip = !inv.callback && inv.satisfaction >= 0.9f ? Mathf.RoundToInt(inv.total * 0.1f / 5f) * 5 : 0;
            return inv;
        }

        /// <summary>Bills the customer: sets the job's payout to the invoice total, completes it through Company (which pays, scaled by
        /// satisfaction), adds a tip for a happy customer, schedules a callback for anything left unfixed or badly done.</summary>
        public static RepairInvoice SendInvoice(Job job)
        {
            if (job == null || job.Done || !Company.ActiveJobs.Contains(job) || job.Kind != JobKind.Repair) return null;
            var inv = PreviewInvoice(job);
            var p = Payload(job);
            var live = DefectsFor(job);
            int before = Game.Wallet.Money;
            job.Payout = inv.total;
            Company.Complete(job, inv.satisfaction);
            inv.paid = Game.Wallet.Money - before;
            if (inv.tip > 0) Game.Wallet.Add(inv.tip, $"Tip: {job.Client}");

            if (inv.willCallBack && p != null)
            {
                var cb = new RepairPayload { lotId = p.lotId, callback = true };
                var names = new List<string>();
                foreach (var s in p.defects)
                {
                    var d = live.Find(x => x.S == s);
                    if (d != null && d.IsFixed && d.Quality >= 0.6f) continue;
                    cb.defects.Add(new DefectState { type = s.type, spot = s.spot, cause = s.cause });
                    names.Add(Describe(s).title);
                }
                if (cb.defects.Count > 0)
                    _callbacks.Add(new PendingCallback { day = Today + UnityEngine.Random.Range(1, 4), title = string.Join(", ", names), client = job.Client, payload = cb });
                if (inv.callback) Company.Reputation -= 2;       // a second failed visit
            }
            DestroyDefects(job);
            _payloads.Remove(job.Id);
            _invoices.Insert(0, inv);
            while (_invoices.Count > 12) _invoices.RemoveAt(_invoices.Count - 1);
            if (Waypoint.HasValue && WaypointLabel == job.Address) ClearWaypoint();
            Game.Toast(inv.callback ? $"Callback closed for {job.Client}. Satisfaction {inv.satisfaction * 100f:0}%"
                : $"Invoice ${inv.total:N0} sent. {job.Client} paid ${inv.paid:N0}" + (inv.tip > 0 ? $" + ${inv.tip} tip" : "") + $" ({inv.satisfaction * 100f:0}% satisfied)");
            Company.NotifyChanged();
            return inv;
        }

        // ------------------------------------------------------------------ days: callbacks, expiry, top-up

        /// <summary>New-day housekeeping (Company.DayStarted): due callbacks become jobs, stale offers expire, the board is topped up.</summary>
        public static void ProcessDay(int day)
        {
            if (!GameSettings.IsCareer) return;
            for (int i = _callbacks.Count - 1; i >= 0; i--)
            {
                var c = _callbacks[i];
                if (c.day > day) continue;
                _callbacks.RemoveAt(i);
                var lot = Town.Find(c.payload.lotId); if (lot == null) continue;
                var job = new Job
                {
                    Id = "rep-" + _nextId++, Kind = JobKind.Repair, MinStage = CompanyStage.Handyman, RequiredVehicleType = HandymanStart.VanTypeId,
                    Client = lot.Resident, Address = lot.Address, WorldPos = lot.Parking, Title = "Callback: " + c.title,
                    Description = "\"It's doing the same thing again. I already paid you to fix this.\" No-charge return visit.", DueDay = day + 2, Payout = 0,
                };
                foreach (var s in c.payload.defects) s.jobId = job.Id;
                _payloads[job.Id] = c.payload;
                job.Payload = JsonUtility.ToJson(c.payload);
                Company.Offers.Insert(0, job);
                Game.Toast($"Callback from {lot.Resident}, {lot.Address}: {c.title}");
            }
            for (int i = Company.Offers.Count - 1; i >= 0; i--)
            {
                var j = Company.Offers[i];
                if (j.Kind != JobKind.Repair || j.DueDay >= day) continue;
                Company.Offers.RemoveAt(i);
                var p = Payload(j);
                _payloads.Remove(j.Id);
                if (p != null && p.callback) { Company.Reputation -= 4; Game.Toast($"You ignored a callback at {j.Address}. Reputation -4"); }
            }
            GenerateOffers();
        }

        // ------------------------------------------------------------------ save

        static string Save()
        {
            foreach (var j in Company.ActiveJobs) Sync(j);
            return JsonUtility.ToJson(new Data
            {
                callbacks = new List<PendingCallback>(_callbacks), invoices = new List<RepairInvoice>(_invoices),
                hasWp = Waypoint.HasValue, wp = Waypoint ?? Vector3.zero, wpLabel = WaypointLabel, nextId = _nextId,
            });
        }

        static void Load(string json)
        {
            DestroyAll();
            _payloads.Clear(); _callbacks.Clear(); _invoices.Clear(); ClearWaypoint(); _nextId = 1;
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d.callbacks != null) _callbacks.AddRange(d.callbacks);
            if (d.invoices != null) _invoices.AddRange(d.invoices);
            if (d.hasWp) { Waypoint = d.wp; WaypointLabel = d.wpLabel; }
            _nextId = Mathf.Max(1, d.nextId);
        }

        /// <summary>Forget callbacks, invoices and the waypoint and remove every spawned defect (new game, tests).</summary>
        public static void ResetState() => Load(null);

        // Defect progress itself lives in Job.Payload (saved by Company); after any load the defects are rebuilt from it.
        static void AfterLoad() { _payloads.Clear(); DestroyAll(); Respawn(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset()
        {
            _payloads.Clear(); _callbacks.Clear(); _invoices.Clear(); _desc.Clear(); RepairDefect.All.Clear();
            Waypoint = null; WaypointLabel = ""; _nextId = 1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension("repairs", Save, Load);
            SaveSystem.AfterLoad += AfterLoad;
            Company.JobAccepted += OnAccepted;
            Company.DayStarted += ProcessDay;
            Town.Built += Respawn;
            Town.Destroyed += () => RepairDefect.All.Clear();
        }
    }
}
