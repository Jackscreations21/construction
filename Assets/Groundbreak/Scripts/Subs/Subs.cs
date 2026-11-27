using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum SubState { Scheduled, OnSite, Done, Callback }

    /// <summary>A price a sub gave for the remaining work of their trade on one site (not stored until accepted).</summary>
    public class SubQuote
    {
        public SubCompany Company;
        public SubTradeInfo Trade;
        public string JobId;             // Crew job id of the site (a Company job id or Crew.SiteJobId)
        public Vector3 Center, Arrival;
        public float Radius;
        public float Qty;                // in Trade.Unit
        public string Detail;            // what was measured ("3 walls, 412 sq ft of wall area")
        public int Price, ListPrice, Discount;
        public float CrewDays;           // your own crew's labor for the same work (journeyman worker-days)
        public int CrewCost;             // their wages for it
        public string Blocker;           // why it can't be booked (null = ok)
    }

    /// <summary>An accepted subcontract: booked, on site, done (invoiced) or back for a warranty callback.</summary>
    [Serializable]
    public class SubContract
    {
        public string Id, CompanyId, Trade, JobId;
        public Vector3 Center, Arrival;
        public float Radius, Qty;
        public string Detail;
        public int Price, BookedDay, PromisedDay, StartDay, FinishedDay, Mistakes, Callbacks, Skill, CrewSize;
        public SubState State;
        public bool Paid, LateNotified, InspectionFailed;
        public string Status = "";       // what the crew is doing (UI)
        public SubCompany Company => SubCatalog.Get(CompanyId);
        public SubTradeInfo TradeInfo => SubCatalog.Trade(Trade);
        public bool Active => State == SubState.Scheduled || State == SubState.OnSite || State == SubState.Callback;
    }

    /// <summary>
    /// Subcontractors (Career, Contractor stage and up): local trade companies you hire to do one trade on a job instead of
    /// doing it yourself or sending your crew. A quote is measured from the real remaining work on the site (<see cref="SubScopes"/>);
    /// accepted, the sub is booked out by their lead time (cheap outfits often show up late), arrives in their own truck with a
    /// temporary crew that works through the same <see cref="CrewTask"/> machinery as your own workers (never on payroll), and
    /// invoices when done - paid from <see cref="Game.Wallet"/>. Low-quality subs leave real defects (missed nails, missing screws,
    /// shallow trenches) that fail inspection: a callback is free warranty work. Repeat business earns a discount. Saved as "subs".
    /// </summary>
    public static class Subs
    {
        public const CompanyStage MinStage = CompanyStage.Contractor;
        public const float DiscountPerJob = 0.02f, MaxDiscount = 0.10f;
        public const float LateFeePerDay = 0.015f;     // 1.5% a day on an unpaid invoice

        public static readonly List<SubContract> Contracts = new List<SubContract>();
        /// <summary>Jobs finished per company (relationship): each one knocks 2% off their next price, up to 10%.</summary>
        public static readonly Dictionary<string, int> JobsWith = new Dictionary<string, int>();
        /// <summary>Tests: ignore the 7:00-3:30 work day and the booking lead time.</summary>
        public static bool IgnoreWorkHours;
        /// <summary>Tests: force the reliability roll (true = always on time, false = always late, null = roll).</summary>
        public static bool? ForceOnTime;
        public static event Action Changed;
        public static void NotifyChanged() => Changed?.Invoke();

        static int Day => GameClock.I != null ? GameClock.I.Day : 1;
        public static bool Unlocked => GameSettings.IsCareer && Company.Stage >= MinStage;

        public static float DiscountWith(string companyId) =>
            Mathf.Min(MaxDiscount, (JobsWith.TryGetValue(companyId ?? "", out int n) ? n : 0) * DiscountPerJob);

        public static SubContract Find(string id) => Contracts.Find(c => c.Id == id);
        public static SubContract ActiveFor(string trade, string jobId) => Contracts.Find(c => c.Active && c.Trade == trade && c.JobId == jobId);

        // ------------------------------------------------------------------ quote

        /// <summary>Where "this site" is for a quote: the job the player is standing on (Crew.HereJobId), its center and a radius.</summary>
        public static void CurrentSite(out string jobId, out Vector3 center, out float radius)
        {
            string id = string.IsNullOrEmpty(Crew.HereJobId) ? Crew.SiteJobId : Crew.HereJobId;
            jobId = id;
            radius = 15f; center = Vector3.zero;
            var j = Company.ActiveJobs.Find(x => x.Id == id);
            if (j != null && j.WorldPos != Vector3.zero) { center = new Vector3(j.WorldPos.x, 0f, j.WorldPos.z); return; }
            if (Site.Lot.HasValue)
            {
                center = Site.LotCenter;
                radius = Mathf.Max(10f, Mathf.Max(Site.Lot.Value.width, Site.Lot.Value.height) * 0.5f);
                return;
            }
            if (Game.Player != null) { var p = Game.Player.transform.position; center = new Vector3(p.x, 0f, p.z); }
        }

        public static SubQuote Quote(SubCompany c) { CurrentSite(out var job, out var center, out float r); return Quote(c, job, center, r); }

        public static SubQuote Quote(SubCompany c, string jobId, Vector3 center, float radius)
        {
            var t = SubCatalog.Trade(c.Trade);
            var q = new SubQuote { Company = c, Trade = t, JobId = jobId, Center = center, Radius = radius, Arrival = CrewManager.ArrivalPoint() };
            q.Qty = SubScopes.Measure(c.Trade, center, radius, out q.Detail);
            q.ListPrice = Mathf.RoundToInt(Mathf.Max(t.Minimum * c.PriceFactor, q.Qty * t.Rate * c.PriceFactor) / 5f) * 5;
            float disc = DiscountWith(c.Id);
            q.Price = Mathf.RoundToInt(q.ListPrice * (1f - disc) / 5f) * 5;
            q.Discount = q.ListPrice - q.Price;
            q.CrewDays = q.Qty / Mathf.Max(0.01f, t.CrewPerDay);
            q.CrewCost = Mathf.RoundToInt(q.CrewDays * CrewTrades.Wage(t.CrewTrade, 3));
            if (!GameSettings.IsCareer) q.Blocker = "Subcontractors are part of Career mode";
            else if (Company.Stage < MinStage) q.Blocker = "Needs company stage Contractor (warehouse + a truck)";
            else if (q.Qty <= 0.0001f) q.Blocker = "No " + t.Label.ToLowerInvariant() + " work left on this site";
            else if (ActiveFor(c.Trade, jobId) != null) q.Blocker = "A " + t.Label.ToLowerInvariant() + " sub is already booked here";
            return q;
        }

        // ------------------------------------------------------------------ accept / schedule

        public static SubContract Accept(SubQuote q, out string msg)
        {
            if (q == null) { msg = "No quote"; return null; }
            if (q.Blocker != null) { msg = q.Blocker; return null; }
            // the quote may be older than another booking (the app keeps the other companies' quotes): re-check
            if (ActiveFor(q.Trade.Id, q.JobId) != null) { msg = "A " + q.Trade.Label.ToLowerInvariant() + " sub is already booked here"; return null; }
            var c = q.Company;
            bool onTime = ForceOnTime ?? (UnityEngine.Random.value < c.Reliability);
            int late = onTime ? 0 : 1 + UnityEngine.Random.Range(0, c.Quality <= 2 ? 3 : 2);
            int lead = IgnoreWorkHours ? 0 : c.LeadDays;
            var k = new SubContract
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8), CompanyId = c.Id, Trade = c.Trade, JobId = q.JobId,
                Center = q.Center, Arrival = q.Arrival, Radius = q.Radius, Qty = q.Qty, Detail = q.Detail, Price = q.Price,
                BookedDay = Day, PromisedDay = Day + lead, StartDay = Day + lead + (IgnoreWorkHours ? 0 : late),
                Skill = c.Quality, CrewSize = q.Trade.CrewSize, State = SubState.Scheduled, Status = "Booked",
            };
            Contracts.Add(k);
            msg = lead == 0 ? $"{c.Name} is on the way" : $"{c.Name} booked for day {k.PromisedDay} (${k.Price:N0} on completion)";
            Game.Toast(msg);
            Changed?.Invoke();
            return k;
        }

        public static void Cancel(SubContract k)
        {
            if (k == null || k.State != SubState.Scheduled) return;
            SubCrews.Clear(k);
            Contracts.Remove(k);
            Game.Toast($"Cancelled {k.Company?.Name}");
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ the work day

        public static bool WorkHours()
        {
            if (IgnoreWorkHours) return true;
            float h = GameClock.I != null ? GameClock.I.Hours : 9f;
            return h >= Crew.DayStartHour && h < Crew.DayEndHour;
        }

        /// <summary>Called by <see cref="SubCrews"/> twice a second.</summary>
        public static void Tick()
        {
            foreach (var k in Contracts.ToArray())
            {
                if (k.State == SubState.Scheduled || k.State == SubState.Callback && !SubCrews.Present(k))
                {
                    if (!k.LateNotified && Day >= k.PromisedDay && k.StartDay > k.PromisedDay && WorkHours())
                    {
                        k.LateNotified = true;
                        k.Status = $"Running late - now day {k.StartDay}";
                        Game.Toast($"{k.Company?.Name} called: running late, they'll be there day {k.StartDay}");
                        Changed?.Invoke();
                    }
                    if (Day >= k.StartDay && WorkHours())
                    {
                        if (k.State == SubState.Scheduled) k.State = SubState.OnSite;
                        SubCrews.Arrive(k);
                        Game.Toast($"{k.Company?.Name} is on site ({k.TradeInfo?.Label})");
                        Changed?.Invoke();
                    }
                    continue;
                }
                if (k.State != SubState.OnSite && k.State != SubState.Callback) continue;
                if (!WorkHours()) { if (SubCrews.Present(k)) { SubCrews.SendHome(k); k.Status = "Gone for the day, back at 7:00 AM"; Changed?.Invoke(); } continue; }
                if (!SubCrews.Present(k)) SubCrews.Arrive(k);
                k.Status = SubCrews.Doing(k);
                if (SubCrews.WorkDone(k)) Finish(k);
            }
        }

        static void Finish(SubContract k)
        {
            k.Mistakes = SubCrews.MistakesOf(k);
            bool callback = k.State == SubState.Callback;
            SubCrews.SendHome(k);
            k.State = SubState.Done; k.FinishedDay = Day;
            var co = k.Company;
            if (callback)
            {
                k.InspectionFailed = false;
                k.Status = "Callback done - fixed at no charge";
                Game.Toast($"{co?.Name} fixed their work (warranty callback)");
            }
            else
            {
                JobsWith[k.CompanyId] = (JobsWith.TryGetValue(k.CompanyId, out int n) ? n : 0) + 1;
                k.Status = k.Mistakes > 0 ? $"Done - invoiced; {k.Mistakes} workmanship defect(s) left" : "Done - invoiced";
                TryPay(k, 0);
            }
            SubCrews.Clear(k, keepTasks: true);
            Changed?.Invoke();
        }

        /// <summary>Pays the invoice from the wallet. An unpaid invoice adds a late fee every day and costs reputation.</summary>
        static bool TryPay(SubContract k, int lateDays)
        {
            if (k.Paid) return true;
            int amount = k.Price + Mathf.RoundToInt(k.Price * LateFeePerDay * lateDays);
            if (GameSettings.UnlimitedMoney || Game.Wallet.TrySpend(amount, $"Sub invoice: {k.Company?.Name} ({k.TradeInfo?.Label})"))
            {
                k.Paid = true; if (lateDays > 0) k.Price = amount;
                Game.Toast($"Paid {k.Company?.Name} ${amount:N0}");
                return true;
            }
            Game.Toast($"Can't pay {k.Company?.Name}'s invoice (${amount:N0}) - 1.5% late fee a day");
            return false;
        }

        /// <summary>Free warranty callback: the sub comes back (tomorrow, if they're reliable) with a better hand and redoes their defects.</summary>
        public static bool RequestCallback(SubContract k, out string msg)
        {
            if (k == null || k.State != SubState.Done) { msg = "Only finished work can be called back"; return false; }
            if (k.Mistakes <= 0 && !k.InspectionFailed) { msg = "Nothing to fix"; return false; }
            var c = k.Company;
            k.State = SubState.Callback; k.Callbacks++;
            k.Skill = Mathf.Min(5, Mathf.Max(k.Skill, c != null ? c.Quality : 3) + 2);   // they send the foreman
            bool onTime = ForceOnTime ?? (UnityEngine.Random.value < (c != null ? c.Reliability : 0.8f));
            k.PromisedDay = Day + (IgnoreWorkHours ? 0 : 1);
            k.StartDay = k.PromisedDay + (onTime || IgnoreWorkHours ? 0 : 1);
            k.LateNotified = false;
            SubCrews.Reopen(k);
            k.Status = k.StartDay == Day ? "Coming back today" : $"Coming back day {k.StartDay}";
            msg = $"{c?.Name}: {k.Status.ToLowerInvariant()} to fix it (no charge)";
            Game.Toast(msg);
            Changed?.Invoke();
            return true;
        }

        static void OnDayStarted(int day)
        {
            foreach (var k in Contracts) if (k.State == SubState.Done && !k.Paid) TryPay(k, Mathf.Max(1, day - k.FinishedDay));
            // keep the list short: finished, paid, clean work older than 30 days drops off
            Contracts.RemoveAll(k => k.State == SubState.Done && k.Paid && k.Mistakes == 0 && !k.InspectionFailed && day - k.FinishedDay > 30);
            Changed?.Invoke();
        }

        /// <summary>A failed inspection with corrections inside a finished sub's work area flags it for a callback.</summary>
        static void OnInspection(InspectionResult r)
        {
            if (r == null || r.Passed) return;
            foreach (var k in Contracts)
            {
                if (k.State != SubState.Done || k.Mistakes <= 0) continue;
                foreach (var i in r.Issues)
                {
                    if (!i.HasPosition) continue;
                    Vector3 d = i.Position - k.Center; d.y = 0f;
                    if (d.magnitude > k.Radius + 3f) continue;
                    k.InspectionFailed = true;
                    k.Status = "Failed inspection - call them back (free)";
                    Game.Toast($"Inspection found {k.Company?.Name}'s defects: request a callback in the Subcontractors app");
                    break;
                }
            }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ save

        [Serializable] class Rel { public string id; public int n; }
        [Serializable]
        class Data
        {
            public List<SubContract> contracts = new List<SubContract>();
            public List<Rel> rel = new List<Rel>();
        }

        static string Save()
        {
            var d = new Data { contracts = new List<SubContract>(Contracts) };
            foreach (var kv in JobsWith) d.rel.Add(new Rel { id = kv.Key, n = kv.Value });
            return JsonUtility.ToJson(d);
        }

        static void Load(string json)
        {
            ResetState();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            foreach (var k in d.contracts) { if (k.State == SubState.OnSite || k.State == SubState.Callback) k.Status = "Back at 7:00 AM"; Contracts.Add(k); }
            foreach (var r in d.rel) JobsWith[r.id] = r.n;
            Changed?.Invoke();
        }

        public static void ResetState()
        {
            SubCrews.ClearAll();
            Contracts.Clear(); JobsWith.Clear();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset()
        {
            Contracts.Clear(); JobsWith.Clear(); IgnoreWorkHours = false; ForceOnTime = null; Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension("subs", Save, Load);
            Company.DayStarted += OnDayStarted;
            Inspections.Completed += OnInspection;
        }
    }
}
