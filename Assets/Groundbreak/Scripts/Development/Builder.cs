using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Builder stage: the Career client contracts for full houses become Company jobs (JobKind.House) on real lots.
    /// Payment follows a real construction-loan draw schedule: 10% deposit at signing, then a draw released by the lender when
    /// each phase's inspection passes (foundation 10%, framing 20%, rough-in 20%, drywall 15%), and the final 25% plus change
    /// orders through Company.Complete at the certificate of occupancy. A warranty callback comes due 30 days later.</summary>
    public static class Builder
    {
        public static readonly float[] DrawPct = { 0.10f, 0.20f, 0.20f, 0.15f, 0.25f };
        public const float DepositPct = 0.10f;
        public const int WarrantyAfterDays = 30, WarrantyWindowDays = 14, WarrantyCost = 220;

        /// <summary>Career contract id → plan. Shed and garage stay on the Career job board / outbuilding jobs.</summary>
        public static readonly Dictionary<string, string> ContractPlans = new Dictionary<string, string>
        { { "house", "cottage-1200" }, { "twostory", "twostory-2400" }, { "custom", "estate-3500" } };

        [Serializable] class Payload { public string contract, plan, lot, project, warrantyOf; }
        static Payload Read(Job j) { try { return string.IsNullOrEmpty(j.Payload) ? null : JsonUtility.FromJson<Payload>(j.Payload); } catch { return null; } }
        public static bool IsHouseJob(Job j) => j != null && j.Kind == JobKind.House && Read(j)?.contract != null;
        public static bool IsWarranty(Job j) => j != null && j.Kind == JobKind.House && !string.IsNullOrEmpty(Read(j)?.warrantyOf);

        /// <summary>Puts each client house contract on the job board on a free lot (once per contract while not built).</summary>
        public static void RefreshOffers()
        {
            if (!GameSettings.IsCareer) return;
            foreach (var kv in ContractPlans)
            {
                var c = Career.Contracts.Find(x => x.Id == kv.Key); var plan = HousePlans.Get(kv.Value);
                if (c == null || plan == null || Career.Completed.Contains(c.Id)) continue;
                bool exists = Company.Offers.Exists(j => Read(j)?.contract == c.Id && j.Kind == JobKind.House) ||
                              Company.ActiveJobs.Exists(j => Read(j)?.contract == c.Id && j.Kind == JobKind.House) ||
                              Projects.All.Exists(p => p.Kind == ProjectKind.Client && p.Client == c.Client && !p.Complete);
                if (exists) continue;
                DevLot lot = null;
                foreach (var l in DevLots.Provider.FreeLots())
                    if (l.FrontageFt >= plan.WidthFt + 15f && l.DepthFt >= plan.DepthFt + 45f && !Company.Offers.Exists(j => Read(j)?.lot == l.Id)) { lot = l; break; }
                if (lot == null) continue;
                Company.Offers.Add(new Job
                {
                    Id = "house-" + c.Id + "-" + Projects.Day + "-" + lot.Id, Kind = JobKind.House, Title = c.Title, Client = c.Client,
                    Description = c.Description + $" Plan: {plan.Name} ({plan.Summary}). Draws on inspections.",
                    Address = lot.Address, WorldPos = lot.Local(0, 2f), Payout = c.Price, MinReputation = c.MinRep, MinStage = CompanyStage.Builder,
                    MaterialBudget = plan.Takeoff.MaterialTotal, Payload = JsonUtility.ToJson(new Payload { contract = c.Id, plan = plan.Id, lot = lot.Id }),
                });
            }
            Company.NotifyChanged();
        }

        static void OnAccepted(Job j)
        {
            var pl = Read(j);
            if (j.Kind != JobKind.House || pl == null || pl.contract == null || !string.IsNullOrEmpty(pl.project)) return;
            var lot = DevLots.Provider.Find(pl.lot); var plan = HousePlans.Get(pl.plan);
            var c = Career.Contracts.Find(x => x.Id == pl.contract);
            if (lot == null || plan == null || c == null) return;
            DevLots.Provider.Claim(lot, Company.Name);
            var p = Projects.Create(ProjectKind.Client, lot, plan);
            p.JobId = j.Id; p.Client = j.Client; p.ContractPrice = j.Payout; p.DueDay = Projects.Day + c.Days;
            j.DueDay = p.DueDay;
            pl.project = p.Id; j.Payload = JsonUtility.ToJson(pl);
            int deposit = Mathf.RoundToInt(p.ContractPrice * DepositPct);
            Game.Wallet.Add(deposit, $"Deposit: {j.Title}"); p.DrawsPaid |= 1 << 7;
            Company.LifetimeRevenue += deposit;
            Game.Toast($"Signed {j.Title}. Deposit ${deposit:N0}. Lot: {lot.Address}");
            DevVisuals.Refresh();
        }

        static void OnPhase(HouseProject p, BuildPhase ph)
        {
            if (p.Kind != ProjectKind.Client) return;
            var j = Company.ActiveJobs.Find(x => x.Id == p.JobId);
            if (ph < BuildPhase.Finish)
            {
                if ((p.DrawsPaid & (1 << (int)ph)) != 0) return;
                int draw = Mathf.RoundToInt(p.ContractPrice * DrawPct[(int)ph]);
                p.DrawsPaid |= 1 << (int)ph;
                Game.Wallet.Add(draw, $"Draw {(int)ph + 1} ({HousePlan.Label(ph)} inspection passed): {p.Lot.Address}");
                Company.LifetimeRevenue += draw;
                Game.Toast($"{HousePlan.Label(ph)} passed at {p.Lot.Address}: lender released ${draw:N0}");
                if (ph == BuildPhase.Framing) OfferChangeOrders(p);
                return;
            }
            if (j == null) return;
            p.DrawsPaid |= 1 << 4;
            j.Payout = Mathf.RoundToInt(p.ContractPrice * DrawPct[4]) + p.ChangeOrderTotal;
            Company.Complete(j, p.Quality);
            p.WarrantyDay = Projects.Day + WarrantyAfterDays;
            if (!Career.Completed.Contains(ContractFor(p))) Career.Completed.Add(ContractFor(p));
            DevVisuals.Refresh();
        }

        static string ContractFor(HouseProject p) { foreach (var kv in ContractPlans) if (kv.Value == p.PlanId) return kv.Key; return p.PlanId; }

        // ------------------------------------------------------------------ change orders

        /// <summary>Clients ask for changes once the house is framed (deterministic per plan).</summary>
        public static void OfferChangeOrders(HouseProject p)
        {
            if (p.ChangeOrders.Count > 0) return;
            float k = p.Plan.SqFt / 1500f;
            p.ChangeOrders.Add(new ChangeOrder { Title = "Upgrade to quartz countertops", Price = Mathf.RoundToInt(6800 * Mathf.Max(1f, k * 0.8f) / 100f) * 100, Days = 3 });
            p.ChangeOrders.Add(new ChangeOrder { Title = "Add a 12'x16' covered rear porch", Price = 18500, Days = 8 });
            p.ChangeOrders.Add(new ChangeOrder { Title = "Add 6 recessed lights + a circuit", Price = 1900, Days = 1 });
            foreach (var c in p.ChangeOrders) c.Cost = Mathf.RoundToInt(c.Price * 0.68f);
        }

        /// <summary>Signs a change order: price added to the final draw, cost paid now, schedule extended.</summary>
        public static bool AcceptChange(HouseProject p, ChangeOrder c, out string msg)
        {
            if (c.Accepted || c.Declined) { msg = "Already decided."; return false; }
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(c.Cost, $"Change order materials/labor: {c.Title}")) { msg = $"Need ${c.Cost:N0} to do the work."; return false; }
            c.Accepted = true; p.CostSpent += c.Cost; p.DueDay += c.Days;
            var j = Company.ActiveJobs.Find(x => x.Id == p.JobId); if (j != null) j.DueDay = p.DueDay;
            msg = $"Change order signed: {c.Title} (+${c.Price:N0} on the final draw, +{c.Days} days)";
            return true;
        }

        // ------------------------------------------------------------------ warranty

        static void DayStep(int day)
        {
            if (!GameSettings.IsCareer) return;
            if (Company.Stage >= CompanyStage.Builder) RefreshOffers();
            foreach (var p in Projects.All)
            {
                if (p.Kind != ProjectKind.Client || p.WarrantyDay < 0 || p.WarrantyDone) continue;
                if (p.WarrantyJobId == null && day >= p.WarrantyDay) IssueWarranty(p, day);
                var wj = Company.ActiveJobs.Find(x => x.Id == p.WarrantyJobId);
                if (wj != null && day > wj.DueDay) { p.WarrantyDone = true; Company.Complete(wj, 0f); Game.Toast($"Warranty call at {p.Lot.Address} ignored - reputation hit"); }
            }
        }

        public static Job IssueWarranty(HouseProject p, int day)
        {
            var j = new Job
            {
                Id = "warranty-" + p.Id, Kind = JobKind.House, Title = "Warranty callback: drywall nail pops + a sticking door", Client = p.Client,
                Address = p.Lot.Address, WorldPos = p.Lot.Local(0, 2f), Payout = 0, DueDay = day + WarrantyWindowDays, MinStage = CompanyStage.Builder,
                Description = "One-year builder warranty. Reset the popped screws, re-tape, touch up paint, plane the bedroom door.",
                Payload = JsonUtility.ToJson(new Payload { warrantyOf = p.Id }), Accepted = true,
            };
            p.WarrantyJobId = j.Id;
            Company.ActiveJobs.Add(j);   // an obligation, not an offer: it bypasses the job limit
            Game.Toast($"Warranty callback from {p.Client} at {p.Lot.Address} (fix within {WarrantyWindowDays} days)");
            Company.NotifyChanged();
            return j;
        }

        /// <summary>Send a carpenter for a day: costs labor + materials, keeps the client happy (reputation via Company.Complete).</summary>
        public static bool ResolveWarranty(HouseProject p, out string msg)
        {
            var j = Company.ActiveJobs.Find(x => x.Id == p.WarrantyJobId);
            if (j == null) { msg = "No open warranty call."; return false; }
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(WarrantyCost, "Warranty repair: " + p.Lot.Address)) { msg = $"Need ${WarrantyCost}"; return false; }
            p.CostSpent += WarrantyCost; p.WarrantyDone = true;
            Company.Complete(j, 1f);
            msg = "Warranty work done - client satisfied.";
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Company.JobAccepted += OnAccepted;
            Company.DayStarted += DayStep;
            Company.StageChanged += s => { if (s >= CompanyStage.Builder) RefreshOffers(); };
            Projects.PhaseCompleted += OnPhase;
        }
    }
}
