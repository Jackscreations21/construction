using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One punch-list line on a flip: a problem from the inspection or an optional upgrade, played as catalog defects.</summary>
    [Serializable]
    public class FlipItem
    {
        public string id;                          // FlipItemDef id
        public bool upgrade, planned;              // planned: crew may work on this upgrade
        public List<DefectState> defects = new List<DefectState>();
        public float crewHours;                    // labor hours crews have put into the current defect
        public bool done;                          // every defect fixed (kept in sync with the live defects)
        public float quality = 1f;
        public int matsCounted = -1;               // $ of parts used on its defects already in costSpent (-1: older save, not tracked yet)
        public FlipItemDef Def => FlipDefs.Get(id);
    }

    public enum FlipStatus { Listed, Owned, ForSale, Sold }

    /// <summary>A distressed house on the market, or one the company owns / sold.</summary>
    [Serializable]
    public class FlipHouse
    {
        public string id, lotId, address, seller, buyer, resident0;
        public int status;                          // FlipStatus
        public int listedDay, expiresDay, sqft;
        public int arvBase;                         // after-repair value at a neutral market (market cycle 1.0)
        public int asking;                          // seller's asking price (Listed)
        public int purchasePrice, boughtDay, buyClosing, points, loanBalance;
        public int costSpent;                       // purchase + closing + points + holding + crew materials + material orders
        public int holdingPaid, interestPaid;
        public int listPrice, saleListedDay, nextOfferDay = -1, offerPrice, offerExpires = -1, offers;
        public int salePrice, soldDay, netProceeds;
        public List<FlipItem> items = new List<FlipItem>();

        public FlipStatus Status { get => (FlipStatus)status; set => status = (int)value; }
        public TownLot Lot => Town.Find(lotId);
        public string JobId => "flip-" + id;
        public IEnumerable<FlipItem> Problems { get { foreach (var i in items) if (!i.upgrade) yield return i; } }
        public IEnumerable<FlipItem> UpgradeItems { get { foreach (var i in items) if (i.upgrade) yield return i; } }
        public bool HasOffer => offerPrice > 0 && offerExpires >= Flips.Today;
    }

    /// <summary>
    /// House flipping (Career, Contractor stage and up): distressed houses on the town map come up for sale; buy one (cash or a
    /// hard-money loan), carry it (taxes, vacant-home insurance, utilities, interest per day), fix the inspection's punch list with the
    /// normal repair steps (or send crew), choose upgrades, list it, take an offer. See Docs/FLIPS.md.
    /// </summary>
    public static class Flips
    {
        // real-world numbers (US, small Midwest town, 2026)
        public const float BuyClosingPct = 0.02f;               // title, recording, inspection, transfer
        public const float LoanToPrice = 0.90f, LoanApr = 0.115f, LoanPoints = 0.02f;   // hard-money: 90% of price, 11.5% interest-only, 2 points
        public const float TaxRate = 0.011f;                     // property tax per year of value
        public const int InsurancePerYear = 1900, UtilitiesPerDay = 6;  // vacant-dwelling policy; electric/water/gas minimums
        public const float CommissionPct = Developments.CommissionPct, SellClosingPct = Developments.ClosingPct;
        public const float DistressDiscount = 0.04f;              // vacancy / as-is stigma while any problem remains
        public const int LotValue = 30000;
        public const CompanyStage UnlockStage = CompanyStage.Contractor;

        public static readonly List<FlipHouse> All = new List<FlipHouse>();
        public static event Action Changed;
        public static int Today => GameClock.I != null ? GameClock.I.Day : 1;
        static int _nextId = 1;
        static bool _pruning;

        public static bool Unlocked => GameSettings.IsCareer ? Company.Stage >= UnlockStage : false;
        public static int ListingTarget => Company.Stage >= CompanyStage.Crew ? 4 : 3;
        public static int MaxOwned => Company.Stage >= CompanyStage.Builder ? 4 : Company.Stage >= CompanyStage.Crew ? 2 : 1;

        public static IEnumerable<FlipHouse> Listings { get { foreach (var f in All) if (f.Status == FlipStatus.Listed) yield return f; } }
        public static IEnumerable<FlipHouse> Owned { get { foreach (var f in All) if (f.Status == FlipStatus.Owned || f.Status == FlipStatus.ForSale) yield return f; } }
        public static int OwnedCount { get { int n = 0; foreach (var _ in Owned) n++; return n; } }
        public static FlipHouse Get(string id) => All.Find(f => f.id == id);
        public static FlipHouse At(string address) => All.Find(f => f.address == address && f.Status != FlipStatus.Sold);
        public static void NotifyChanged() { Changed?.Invoke(); Company.NotifyChanged(); }

        // ------------------------------------------------------------------ values

        /// <summary>$/sq ft for a renovated house in this part of Millbrook (comps).</summary>
        public static int PricePerSqFt(TownLot lot)
        {
            switch (lot != null ? lot.District : null)
            {
                case "school": return 162;
                case "north": return 158;
                case "eastside": return 148;
                case "westside": return 140;
                default: return 150;
            }
        }

        public static int SqFtOf(TownLot lot)
        {
            if (lot != null && lot.House != null) return Mathf.RoundToInt(lot.House.Width * lot.House.Depth / 0.092903f / 10f) * 10;
            return 1150;
        }

        /// <summary>After-repair value today (all problems fixed and every upgrade done).</summary>
        public static int Arv(FlipHouse f, int day) => Round500(f.arvBase * Developments.MarketCycle(day));

        public static float AverageQuality(FlipHouse f)
        {
            float q = 0f; int n = 0;
            foreach (var i in f.items) if (i.done) { q += i.quality; n++; }
            return n == 0 ? 1f : q / n;
        }

        /// <summary>Share of the problems' buyer discount that is fixed (1 = every problem fixed).</summary>
        public static float Condition(FlipHouse f)
        {
            float tot = 0f, ok = 0f;
            foreach (var i in f.Problems) { var d = i.Def; if (d == null) continue; tot += d.Value; if (i.done) ok += d.Value; }
            return tot <= 0f ? 1f : ok / tot;
        }

        public static bool AllProblemsFixed(FlipHouse f) { foreach (var i in f.Problems) if (!i.done) return false; return true; }

        /// <summary>What the house is worth today in its current state (appraisal / what buyers pay).</summary>
        public static int Value(FlipHouse f, int day)
        {
            float v = Arv(f, day);
            foreach (var i in f.items) { var d = i.Def; if (d != null && !i.done) v -= d.Value; }
            if (!AllProblemsFixed(f)) v -= f.arvBase * DistressDiscount;
            v *= 0.96f + 0.04f * AverageQuality(f);
            return Round500(v);
        }

        public static int Round500(float v) => Mathf.RoundToInt(v / 500f) * 500;

        /// <summary>Estimated cost to have every problem fixed and every upgrade done by contractors (shown on the listing).</summary>
        public static int RehabEstimate(FlipHouse f, bool includeUpgrades = true)
        {
            int s = 0;
            foreach (var i in f.items) if (!i.done && (includeUpgrades || !i.upgrade) && i.Def != null) s += FlipDefs.ContractorPrice(i.Def);
            return s;
        }

        // holding costs per day
        public static int DailyTax(FlipHouse f) => Mathf.RoundToInt(f.purchasePrice * TaxRate / 365f);
        public static int DailyInsurance(FlipHouse f) => Mathf.RoundToInt(InsurancePerYear / 365f);
        public static int DailyInterest(FlipHouse f) => Mathf.RoundToInt(f.loanBalance * LoanApr / 365f);
        public static int DailyHolding(FlipHouse f) => DailyTax(f) + DailyInsurance(f) + UtilitiesPerDay;
        public static int DailyCarry(FlipHouse f) => DailyHolding(f) + DailyInterest(f);

        /// <summary>Net sale proceeds minus everything put in (purchase, closing, points, holding, interest, crew materials).</summary>
        public static int Profit(FlipHouse f) => f.Status == FlipStatus.Sold ? f.netProceeds - f.costSpent : 0;

        // ------------------------------------------------------------------ the market (listings)

        static bool AddressBusy(TownLot lot)
        {
            foreach (var j in Company.Offers) if (j.Address == lot.Address) return true;
            foreach (var j in Company.ActiveJobs) if (j.Address == lot.Address) return true;
            foreach (var f in All) if (f.lotId == lot.Id) return true;            // listed, owned or flipped before
            return false;
        }

        /// <summary>Houses that can come on the market: enterable houses on the map (they have the repair spots), nobody's job there.</summary>
        public static List<TownLot> Candidates()
        {
            var l = new List<TownLot>();
            foreach (var lot in Town.Addresses) if (lot.ServiceCalls && !AddressBusy(lot)) l.Add(lot);
            return l;
        }

        static readonly string[] Sellers = { "First Prairie Bank (REO)", "Estate of R. Hollis", "Midwest Federal (foreclosure)", "Out-of-state heirs",
            "County tax sale", "Landlord selling as-is", "Relocating owner (as-is)", "Estate of D. Kowalski" };

        /// <summary>Makes a distressed listing at a lot (deterministic for a seed). Not added to the market.</summary>
        public static FlipHouse MakeListing(TownLot lot, int seed, int day)
        {
            var rng = new System.Random(seed);
            var f = new FlipHouse { id = _nextId++ + "-" + (seed & 0xffff).ToString("x4"), lotId = lot.Id, address = lot.Address, Status = FlipStatus.Listed };
            f.seller = Sellers[rng.Next(Sellers.Length)];
            f.sqft = SqFtOf(lot);
            f.arvBase = Round500(f.sqft * PricePerSqFt(lot) + LotValue);
            f.listedDay = day; f.expiresDay = day + 10 + rng.Next(12);
            // problems: the yard always, one big one (roof / siding / water heater), then 3-4 more
            var pool = new List<FlipItemDef>();
            foreach (var p in FlipDefs.Problems) if (!p.Always) pool.Add(p);
            foreach (var p in FlipDefs.Problems) if (p.Always) Add(f, p, rng);
            var big = new[] { "roof", "siding", "waterheater" };
            Add(f, FlipDefs.Get(big[rng.Next(big.Length)]), rng);
            int more = 3 + rng.Next(2);
            while (more > 0 && pool.Count > 0)
            {
                var p = pool[rng.Next(pool.Count)]; pool.Remove(p);
                if (f.items.Exists(i => i.id == p.Id)) continue;
                Add(f, p, rng); more--;
            }
            foreach (var u in FlipDefs.Upgrades) Add(f, u, rng);
            int asIs = Value(f, day);
            f.asking = Round500(asIs * (0.97f + (float)rng.NextDouble() * 0.06f));
            return f;
        }

        /// <summary>Puts a listing for this lot on the market now (tests, scripted events).</summary>
        public static FlipHouse AddListing(TownLot lot, int seed)
        {
            var f = MakeListing(lot, seed, Today);
            All.Add(f);
            FlipVisuals.Refresh(f);
            NotifyChanged();
            return f;
        }

        static void Add(FlipHouse f, FlipItemDef d, System.Random rng)
        {
            if (d == null) return;
            var it = new FlipItem { id = d.Id, upgrade = d.Upgrade, matsCounted = 0 };
            foreach (var did in d.Defs)
            {
                var jd = JobCatalog.Get(did); if (jd == null || jd.Where == null) continue;
                int cause = JobCatalog.IsTask(jd) ? 0 : rng.Next(jd.Causes.Count);
                it.defects.Add(new DefectState { type = (int)RepairType.Catalog, spot = (int)jd.Where.Spot, cause = cause, def = jd.Id, jobId = f.JobId });
            }
            f.items.Add(it);
        }

        /// <summary>Drops expired listings and tops the market up (new game day, stage change).</summary>
        public static void RefreshMarket(int day)
        {
            if (!Unlocked) return;
            for (int i = All.Count - 1; i >= 0; i--)
                if (All[i].Status == FlipStatus.Listed && day > All[i].expiresDay) { FlipVisuals.Clear(All[i]); All.RemoveAt(i); }
            int have = 0; foreach (var _ in Listings) have++;
            var cand = Candidates();
            var rng = new System.Random(day * 7919 + All.Count * 31 + 17);
            while (have < ListingTarget && cand.Count > 0)
            {
                var lot = cand[rng.Next(cand.Count)]; cand.Remove(lot);
                var f = MakeListing(lot, rng.Next(), day);
                All.Add(f); have++;
                FlipVisuals.Refresh(f);
            }
            NotifyChanged();
        }

        // ------------------------------------------------------------------ buying

        public static int DownPayment(FlipHouse f, bool financed) => financed ? f.asking - LoanAmount(f) : f.asking;
        public static int LoanAmount(FlipHouse f) => Round500(f.asking * LoanToPrice);
        public static int CashToClose(FlipHouse f, bool financed) =>
            DownPayment(f, financed) + Mathf.RoundToInt(f.asking * BuyClosingPct) + (financed ? Mathf.RoundToInt(LoanAmount(f) * LoanPoints) : 0);

        public static string BuyBlocker(FlipHouse f, bool financed)
        {
            if (f == null || f.Status != FlipStatus.Listed) return "Not for sale any more.";
            if (!Unlocked) return $"Flipping opens at company stage {UnlockStage}.";
            if (OwnedCount >= MaxOwned) return $"You can carry {MaxOwned} flip(s) at your company size. Sell one first.";
            if (Company.ActiveJobs.Exists(j => j.Address == f.address)) return "You have an open customer job at this house - finish it first.";
            int cash = CashToClose(f, financed);
            if (!GameSettings.UnlimitedMoney && Game.Wallet.Money < cash) return $"Needs ${cash:N0} cash to close (you have ${Game.Wallet.Money:N0}).";
            return null;
        }

        /// <summary>Closes on a listing: pays down payment + closing (+ loan points), takes the hard-money loan, the house's problems appear.</summary>
        public static bool Buy(FlipHouse f, bool financed, out string msg)
        {
            msg = BuyBlocker(f, financed);
            if (msg != null) return false;
            int loan = financed ? LoanAmount(f) : 0;
            int closing = Mathf.RoundToInt(f.asking * BuyClosingPct), pts = financed ? Mathf.RoundToInt(loan * LoanPoints) : 0;
            int cash = CashToClose(f, financed);
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(cash, $"Flip purchase {f.address}: ${f.asking:N0}" + (financed ? $" (hard-money loan ${loan:N0})" : " cash"))) { msg = "Not enough cash."; return false; }
            f.Status = FlipStatus.Owned; f.purchasePrice = f.asking; f.boughtDay = Today; f.buyClosing = closing; f.points = pts; f.loanBalance = loan;
            f.costSpent = f.asking + closing + pts;
            if (!Town.Exists && GameSettings.IsCareer) Town.Ensure();
            var lot = f.Lot; if (lot != null) { f.resident0 = lot.Resident; lot.Resident = "Vacant (your flip)"; }
            PruneOffers();
            Spawn(f);
            FlipVisuals.Refresh(f);
            msg = $"Bought {f.address} for ${f.asking:N0}. Cash to close ${cash:N0}" + (financed ? $", loan ${loan:N0} at {LoanApr * 100f:0.#}%" : "") + ".";
            Game.Toast(msg);
            NotifyChanged();
            return true;
        }

        /// <summary>Borrows the cash shortfall for a purchase from the company bank line (Fleet finance), when the bank allows it.</summary>
        public static bool BorrowShortfall(FlipHouse f, bool financed, out string msg)
        {
            int need = CashToClose(f, financed) - Game.Wallet.Money;
            if (need <= 0) { msg = "You already have the cash."; return false; }
            need = Mathf.CeilToInt(need / 500f) * 500;
            return Finance.TakeBankLoan(Mathf.Max(500, need), 60, out msg);
        }

        // ------------------------------------------------------------------ the punch list in the world

        static readonly Dictionary<string, Job> _jobs = new Dictionary<string, Job>();

        /// <summary>The (never-billed) job the house's defects belong to. Not on the job board; RepairUI works on its defects normally.</summary>
        public static Job JobFor(FlipHouse f)
        {
            if (_jobs.TryGetValue(f.id, out var j)) return j;
            j = new Job { Id = f.JobId, Kind = JobKind.Remodel, Title = "Flip: " + f.address, Client = Company.Name, Address = f.address, Accepted = true };
            var lot = f.Lot; if (lot != null) j.WorldPos = lot.Parking;
            _jobs[f.id] = j;
            return j;
        }

        public static List<RepairDefect> LiveDefects(FlipHouse f) => RepairJobs.DefectsFor(JobFor(f));

        public static CatalogDefect LiveDefect(FlipHouse f, DefectState s)
        {
            foreach (var d in RepairDefect.All) if (d != null && d.S == s) return d as CatalogDefect;
            return null;
        }

        /// <summary>Puts the house's problems and dated finishes in the world (needs the 3D town).</summary>
        public static bool Spawn(FlipHouse f)
        {
            if (f.Status != FlipStatus.Owned && f.Status != FlipStatus.ForSale) return false;
            var lot = f.Lot; if (lot == null || lot.House == null) return false;
            FlipsRuntime.Ensure();
            var job = JobFor(f);
            foreach (var it in f.items)
                foreach (var s in it.defects)
                {
                    if (LiveDefect(f, s) != null) continue;
                    s.jobId = job.Id;
                    try { RepairDefect.Create(s, job, lot.House); }
                    catch (Exception e) { Debug.LogError($"[Flips] could not spawn {s.def} at {f.address}: {e}"); }
                }
            return true;
        }

        public static void Despawn(FlipHouse f) { foreach (var d in LiveDefects(f)) d.Remove(); }

        /// <summary>Reads the live defects back into the punch list (done flags, workmanship). Returns true when something changed.</summary>
        public static bool Sync(FlipHouse f)
        {
            bool changed = false;
            foreach (var it in f.items)
            {
                if (it.defects.Count == 0) continue;
                CountParts(f, it);
                bool all = true; float q = 0f; int n = 0; bool any = false;
                foreach (var s in it.defects)
                {
                    var d = LiveDefect(f, s);
                    if (d == null) { all = false; continue; }
                    any = true;
                    if (!d.IsFixed) all = false; else { q += d.Quality; n++; }
                }
                if (!any) continue;                        // not in the world (no town): keep the saved flags
                if (all != it.done) { it.done = all; changed = true; if (all) Game.Toast($"Flip punch list: {it.Def?.Label} done"); }
                if (n > 0) it.quality = q / n;
            }
            return changed;
        }

        /// <summary>Our cost of the parts used so far on an item's defects (out of the van / stock), rounded to dollars.</summary>
        public static int PartsUsed(FlipItem it)
        {
            float c = 0f;
            foreach (var s in it.defects) if (s.used != null) foreach (var u in s.used) c += RepairCatalog.UnitCost(u.id) * u.n;
            return Mathf.RoundToInt(c);
        }

        /// <summary>Adds parts the player used on the item since the last look to the flip's costs (each use once).</summary>
        static void CountParts(FlipHouse f, FlipItem it)
        {
            int used = PartsUsed(it);
            if (it.matsCounted < 0) { it.matsCounted = used; return; }        // older save: start counting from here
            if (used > it.matsCounted) { f.costSpent += used - it.matsCounted; it.matsCounted = used; }
        }

        public static void SyncAll()
        {
            bool c = false;
            foreach (var f in Owned) if (Sync(f)) { c = true; FlipVisuals.Refresh(f); }
            if (c) NotifyChanged();
        }

        // ------------------------------------------------------------------ crew

        public static string TradeOf(FlipItem it) => it.Def != null ? it.Def.Trade : "laborer";

        public static IEnumerable<Worker> CrewOn(FlipHouse f) { foreach (var w in Company.Workers) if (w.AssignedJobId == f.JobId) yield return w; }

        /// <summary>Sends an idle (or any) worker to the flip. Crew work happens off screen, a day at a time.</summary>
        public static Worker AssignNext(FlipHouse f)
        {
            Worker pick = null;
            foreach (var w in Company.Workers) if (w.AssignedJobId != f.JobId && (string.IsNullOrEmpty(w.AssignedJobId) || w.AssignedJobId == Crew.SiteJobId)) { pick = w; break; }
            if (pick == null) return null;
            Crew.Assign(pick, f.JobId);
            NotifyChanged();
            return pick;
        }

        public static void Release(FlipHouse f) { foreach (var w in new List<Worker>(CrewOn(f))) Crew.Assign(w, null); NotifyChanged(); }

        /// <summary>What the crew works on next: unfinished problems (matching trades first), then planned upgrades.</summary>
        static FlipItem NextFor(FlipHouse f, Worker w)
        {
            FlipItem best = null; int bestScore = -1;
            foreach (var it in f.items)
            {
                if (it.done || (it.upgrade && !it.planned)) continue;
                int score = (it.upgrade ? 0 : 2) + (CrewTrades.Matches(w.Trade, TradeOf(it)) ? 1 : 0);
                if (score > bestScore) { best = it; bestScore = score; }
            }
            return best;
        }

        /// <summary>One day of crew work on every flip (8 h × speed per worker). Finished items are played through their defects.</summary>
        public static void CrewDay()
        {
            foreach (var f in new List<FlipHouse>(Owned))
            {
                foreach (var w in new List<Worker>(CrewOn(f)))
                {
                    if (!Crew.Available(w)) continue;
                    float hours = 8f * Crew.Speed(w, null);
                    int guard = 0;
                    while (hours > 0.01f && guard++ < 12)
                    {
                        var it = NextFor(f, w); if (it == null) break;
                        string trade = TradeOf(it);
                        float rate = Crew.Speed(w, trade) / Mathf.Max(0.1f, Crew.Speed(w, null));
                        float need = FlipDefs.BookHours(it.Def) - it.crewHours;
                        float give = Mathf.Min(hours * rate, need);
                        it.crewHours += give; hours -= give / Mathf.Max(0.05f, rate);
                        if (it.crewHours >= FlipDefs.BookHours(it.Def) - 0.01f)
                        {
                            FinishByCrew(f, it, Crew.Quality(w, trade), w);
                            if (!it.done) break;                 // blocked (weather): NextFor would hand out the same item again
                        }
                    }
                }
            }
        }

        /// <summary>Plays an item's defects to the end (job-catalog solver) with the crew's workmanship and bills its materials.</summary>
        public static void FinishByCrew(FlipHouse f, FlipItem it, float quality, Worker w = null)
        {
            int mats = 0;
            CountParts(f, it);                                   // parts the player already put in are in the costs...
            int playerParts = Mathf.Max(0, it.matsCounted);
            bool allFixed = true;
            foreach (var s in it.defects)
            {
                var jd = JobCatalog.Get(s.def);
                if (jd != null && s.cause < jd.Causes.Count) mats += Mathf.RoundToInt(jd.MaterialCostFor(jd.Causes[s.cause]));
                var d = LiveDefect(f, s);
                if (d != null) { JobCatalog.Solve(d); s.quality = Mathf.Min(s.quality, quality); if (!d.IsFixed) allFixed = false; }
            }
            // a step the weather blocks (outdoor paint/caulk in the cold, roof work in rain) leaves the item unfinished:
            // don't bill or call it done, or every crew day would bill its materials again
            if (!allFixed) { Game.Toast($"{it.Def?.Label} at {f.address}: waiting on the weather"); return; }
            mats = Mathf.Max(0, mats - playerParts);             // ...so the crew only buys the rest
            it.matsCounted = PartsUsed(it);                      // the solver's parts are billed here, not again by Sync
            it.done = true; it.quality = Mathf.Clamp01(quality);
            if (mats > 0 && !GameSettings.UnlimitedMoney) { Game.Wallet.TrySpend(mats, $"Materials for crew: {it.Def?.Label} ({f.address})"); }
            f.costSpent += mats;
            Game.Toast($"{(w != null ? w.Name : "Crew")} finished: {it.Def?.Label} at {f.address}");
            Sync(f);
            FlipVisuals.Refresh(f);
        }

        // ------------------------------------------------------------------ listing and selling

        /// <summary>Days until a buyer makes an offer: the Development market's days-on-market for the price, slower for a house with
        /// open problems, faster for a well-known company.</summary>
        public static int ExpectedDaysToOffer(FlipHouse f, int price, int day)
        {
            float ratio = price / (float)Mathf.Max(1, Value(f, day));
            int dts = Developments.DaysToSell(ratio);
            if (dts == int.MaxValue) return int.MaxValue;
            float cond = Condition(f);
            float k = (1.5f - 0.5f * cond) * (1.15f - 0.3f * Company.Reputation / 100f);
            return Mathf.Max(2, Mathf.RoundToInt(dts * k * 0.6f));
        }

        public static bool List(FlipHouse f, int price, out string msg)
        {
            if (f == null || (f.Status != FlipStatus.Owned && f.Status != FlipStatus.ForSale)) { msg = "You don't own it."; return false; }
            f.Status = FlipStatus.ForSale; f.listPrice = Mathf.Max(1000, Round500(price)); f.saleListedDay = Today;
            f.offerPrice = 0; f.offerExpires = -1;
            ScheduleOffer(f, Today);
            int exp = ExpectedDaysToOffer(f, f.listPrice, Today);
            msg = $"Listed {f.address} at ${f.listPrice:N0}." + (exp == int.MaxValue ? " Way over value: expect no offers." : $" Expect a first offer in about {exp} days.");
            Game.Toast(msg);
            FlipVisuals.Refresh(f);
            NotifyChanged();
            return true;
        }

        public static void Unlist(FlipHouse f)
        {
            if (f.Status != FlipStatus.ForSale) return;
            f.Status = FlipStatus.Owned; f.offerPrice = 0; f.nextOfferDay = -1;
            FlipVisuals.Refresh(f); NotifyChanged();
        }

        static void ScheduleOffer(FlipHouse f, int day)
        {
            int exp = ExpectedDaysToOffer(f, f.listPrice, day);
            if (exp == int.MaxValue) { f.nextOfferDay = -1; return; }
            var rng = new System.Random(Hash(f.id) + day * 13 + f.offers * 101);
            f.nextOfferDay = day + Mathf.Max(1, Mathf.RoundToInt(exp * (0.75f + (float)rng.NextDouble() * 0.5f)));
        }

        static int Hash(string s) { int h = 17; if (s != null) foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; }

        static readonly string[] Buyers = { "the Garcias", "a young couple (FHA loan)", "the Petersons", "a retired teacher", "a nurse relocating for work",
            "the Nguyens", "a first-time buyer", "a family of four (VA loan)" };

        /// <summary>A buyer's offer: at value (a bit under, better with reputation), at asking when the price is sharp, over asking in a
        /// bidding war when it's well under value. Never above value + 3%.</summary>
        public static int MakeOffer(FlipHouse f, int day)
        {
            var rng = new System.Random(Hash(f.id) * 7 + day);
            int value = Value(f, day);
            float r = f.listPrice / (float)Mathf.Max(1, value);
            float o;
            if (r <= 0.95f) o = f.listPrice * (1.0f + (float)rng.NextDouble() * 0.03f);
            else if (r <= 0.99f) o = f.listPrice;
            else o = value * (0.95f + (float)rng.NextDouble() * 0.04f + Company.Reputation / 100f * 0.02f);
            o = Mathf.Min(o, value * 1.03f, Mathf.Max(f.listPrice, value) * 1.03f);
            o = Mathf.Min(o, Mathf.Max(f.listPrice * 1.03f, value * 0.95f));
            f.buyer = Buyers[rng.Next(Buyers.Length)];
            return Round500(o);
        }

        public static bool AcceptOffer(FlipHouse f, out string msg)
        {
            if (f == null || !f.HasOffer) { msg = "No offer to accept."; return false; }
            Close(f, f.offerPrice, Today);
            msg = $"Sold {f.address} for ${f.salePrice:N0}. Profit ${Profit(f):N0}.";
            return true;
        }

        public static void DeclineOffer(FlipHouse f)
        {
            if (f == null || !f.HasOffer) return;
            f.offerPrice = 0; f.offerExpires = -1;
            ScheduleOffer(f, Today);
            NotifyChanged();
        }

        /// <summary>Closes the sale: 5.5% commission + 1.5% seller closing, the hard-money loan is paid off from the proceeds.</summary>
        public static void Close(FlipHouse f, int price, int day)
        {
            int commission = Mathf.RoundToInt(price * CommissionPct), closing = Mathf.RoundToInt(price * SellClosingPct);
            Game.Wallet.Add(price - commission - closing, $"Flip sold: {f.address} ${price:N0} (less {CommissionPct:P1} commission, {SellClosingPct:P1} closing)");
            int payoff = f.loanBalance;
            if (payoff > 0)
            {
                int paid = Mathf.Min(payoff, Mathf.Max(0, Game.Wallet.Money));
                if (paid > 0) Game.Wallet.TrySpend(paid, $"Hard-money loan payoff: {f.address}");
                Finance.AddShortfallNote(f.address, payoff - paid);              // the unpaid rest stays owed as company debt
            }
            f.netProceeds = price - commission - closing;    // the full purchase price is already in costSpent, so the payoff is not a cost
            f.loanBalance = 0;
            f.salePrice = price; f.soldDay = day; f.Status = FlipStatus.Sold; f.offerPrice = 0;
            Company.LifetimeRevenue += price;
            Company.Reputation += AllProblemsFixed(f) ? 1 : 0;
            Release(f);
            Despawn(f);
            var lot = f.Lot; if (lot != null) lot.Resident = BuyerName(f);
            FlipVisuals.Refresh(f);
            Game.Toast($"SOLD: {f.address} for ${price:N0} to {f.buyer}. Profit ${Profit(f):N0}");
            Sfx.Play("success", Vector3.zero, 0.7f);
            NotifyChanged();
        }

        static string BuyerName(FlipHouse f) => string.IsNullOrEmpty(f.buyer) ? "New owners" : char.ToUpper(f.buyer[0]) + f.buyer.Substring(1);

        // ------------------------------------------------------------------ daily

        static void DayStep(int day)
        {
            if (!GameSettings.IsCareer) return;
            CrewDay();
            foreach (var f in Owned)
            {
                int i = DailyInterest(f), h = DailyHolding(f);
                f.interestPaid += i; f.holdingPaid += h; f.costSpent += i + h;
                if (f.Status != FlipStatus.ForSale) continue;
                if (f.offerPrice > 0 && f.offerExpires < day) { f.offerPrice = 0; f.offerExpires = -1; ScheduleOffer(f, day); }
                if (f.offerPrice == 0 && f.nextOfferDay >= 0 && day >= f.nextOfferDay)
                {
                    f.offerPrice = MakeOffer(f, day); f.offerExpires = day + 3; f.offers++; f.nextOfferDay = -1;
                    Game.Toast($"Offer on {f.address}: ${f.offerPrice:N0} from {f.buyer} (Flips app)");
                }
            }
            RefreshMarket(day);
        }

        /// <summary>For tests and tools: run the day step without the clock.</summary>
        public static void Step(int day) => DayStep(day);

        /// <summary>Keeps service calls and contractor jobs away from houses the company owns.</summary>
        static void PruneOffers()
        {
            if (_pruning) return;
            _pruning = true;
            try
            {
                for (int i = Company.Offers.Count - 1; i >= 0; i--)
                {
                    var j = Company.Offers[i];
                    var f = At(j.Address);
                    if (f != null && f.Status != FlipStatus.Listed) Company.Offers.RemoveAt(i);
                }
            }
            finally { _pruning = false; }
        }

        // ------------------------------------------------------------------ save

        [Serializable] class Data { public List<FlipHouse> all = new List<FlipHouse>(); public int nextId = 1; }

        static string Save()
        {
            foreach (var f in Owned) Sync(f);
            return JsonUtility.ToJson(new Data { all = new List<FlipHouse>(All), nextId = _nextId });
        }

        static void Load(string json)
        {
            foreach (var f in All) { Despawn(f); FlipVisuals.Clear(f); RestoreResident(f); }
            All.Clear(); _jobs.Clear(); _nextId = 1;
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d.all != null) All.AddRange(d.all);
            _nextId = Mathf.Max(1, d.nextId);
        }

        static void RestoreResident(FlipHouse f) { var lot = f.Lot; if (lot != null && f.resident0 != null) lot.Resident = f.resident0; }

        static void AfterLoad()
        {
            Rebuild();
            // RepairJobs' own after-load hook removes every live defect (it may run after this one): put ours back next frame
            if (All.Count > 0) { FlipsRuntime.Ensure(); FlipsRuntime.RespawnSoon(); }
        }

        /// <summary>Puts every flip back in the world (after a load or when the town is rebuilt).</summary>
        public static void Rebuild()
        {
            if (!Town.Exists) return;
            foreach (var f in All)
            {
                var lot = f.Lot;
                if (lot != null && (f.Status == FlipStatus.Owned || f.Status == FlipStatus.ForSale)) lot.Resident = "Vacant (your flip)";
                else if (lot != null && f.Status == FlipStatus.Sold) lot.Resident = BuyerName(f);
                Spawn(f);
                FlipVisuals.Refresh(f);
            }
        }

        /// <summary>Forget every listing and flip and take their dressing and defects out of the world (new game, tests).</summary>
        public static void ResetState() { Load(null); NotifyChanged(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { All.Clear(); _jobs.Clear(); _nextId = 1; Changed = null; _pruning = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension("flips", Save, Load);
            SaveSystem.AfterLoad += AfterLoad;
            Company.RegisterDailyCost("Flip property tax + insurance + utilities", () => { int s = 0; foreach (var f in Owned) s += DailyHolding(f); return s; });
            Company.RegisterDailyCost("Flip hard-money loan interest", () => { int s = 0; foreach (var f in Owned) s += DailyInterest(f); return s; });
            Company.DayStarted += DayStep;
            Company.StageChanged += s => { if (Unlocked) RefreshMarket(Today); };
            Company.Changed += () => { if (!_pruning && All.Count > 0) PruneOffers(); };
            Town.Built += Rebuild;
            Town.Destroyed += FlipVisuals.ClearAll;
            MainMenu.NewGameStarted += m => ResetState();       // a new game starts with an empty market
        }
    }
}

namespace Groundbreak
{
    /// <summary>Keeps owned flips in step with the work done on their defects (punch-list flags, yard/roof dressing) about once a second.</summary>
    public class FlipsRuntime : MonoBehaviour
    {
        static FlipsRuntime _i;
        float _next;
        readonly System.Collections.Generic.Dictionary<string, string> _sig = new System.Collections.Generic.Dictionary<string, string>();

        public static void Ensure()
        {
            if (_i != null) return;
            var go = new GameObject("FlipsRuntime");
            _i = go.AddComponent<FlipsRuntime>();
        }

        bool _respawn;
        public static void RespawnSoon() { if (_i != null) _i._respawn = true; }

        void Update()
        {
            if (_respawn) { _respawn = false; Flips.Rebuild(); }
            if (Time.time < _next) return;
            _next = Time.time + 1f;
            Poll();
        }

        public void Poll()
        {
            bool changed = false;
            foreach (var f in Flips.Owned)
            {
                if (Flips.Sync(f)) changed = true;
                var sb = new System.Text.StringBuilder();
                foreach (var it in f.items)
                {
                    sb.Append(it.done ? '1' : '0');
                    if (it.id == "yard") foreach (var s in it.defects) sb.Append(string.Join(",", s.done));
                }
                string sig = sb.ToString();
                if (!_sig.TryGetValue(f.id, out var old) || old != sig) { _sig[f.id] = sig; if (old != null) FlipVisuals.Refresh(f); }
            }
            if (changed) Flips.NotifyChanged();
        }
    }
}
