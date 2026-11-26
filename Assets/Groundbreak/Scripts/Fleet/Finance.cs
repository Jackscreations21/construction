using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    [Serializable]
    public class Loan
    {
        public string Id, Label;
        public string Kind;            // "bank" or "vehicle"
        public string CollateralUid;   // OwnedVehicle.Uid for vehicle loans
        public int Principal;
        public float Balance;
        public float Apr;
        public int TermDays, DaysPaid, Payment, Missed;
        public int DaysLeft => Mathf.Max(0, TermDays - DaysPaid);
    }

    [Serializable]
    public class LedgerEntry { public int day; public string cat; public int amount; }   // amount > 0 income, < 0 expense

    /// <summary>Company money: loans, the profit-and-loss ledger (built from Game.Wallet's log) and bankruptcy.
    /// Financing periods: one game day is treated as one business week, so the per-day rate is APR / 52 and a
    /// "60-day" loan behaves like a 60-week note.</summary>
    public static class Finance
    {
        public const string LoanLabel = "Loan payments";
        public const float BankApr = 0.095f, VehicleAprNew = 0.079f, VehicleAprUsed = 0.109f;
        public const int LateFee = 35, InsolventDaysToBankruptcy = 3, BankruptcyRepHit = 15;
        public static readonly int[] BankTerms = { 30, 60, 120 };
        public static readonly int[] VehicleTerms = { 36, 60, 84 };
        static readonly int[] CreditByStage = { 15000, 60000, 150000, 400000, 1500000 };

        public static readonly List<Loan> Loans = new List<Loan>();
        public static readonly List<LedgerEntry> Ledger = new List<LedgerEntry>();
        public static int InsolventDays, Bankruptcies, LastBankruptcyDay;
        public static string LastBankruptcyReport = "";
        public static event Action Changed;
        public static event Action WentBankrupt;

        static Wallet _wallet; static int _logIndex;

        static int Today => GameClock.I != null ? GameClock.I.Day : 1;

        // ------------------------------------------------------------------ loans

        public static float DailyRate(float apr) => apr / 52f;

        /// <summary>Level payment that amortizes the principal over termDays.</summary>
        public static int PaymentFor(int principal, float apr, int termDays)
        {
            if (principal <= 0 || termDays <= 0) return 0;
            double r = DailyRate(apr);
            if (r <= 0) return Mathf.CeilToInt(principal / (float)termDays);
            return (int)Math.Ceiling(principal * r / (1.0 - Math.Pow(1.0 + r, -termDays)));
        }

        public static int TotalInterest(int principal, float apr, int termDays) => PaymentFor(principal, apr, termDays) * termDays - principal;

        public static float CurrentBankApr => BankApr + 0.04f * Bankruptcies;

        public static int BankDebt { get { float s = 0; foreach (var l in Loans) if (l.Kind == "bank") s += l.Balance; return Mathf.CeilToInt(s); } }
        public static int TotalDebt { get { float s = 0; foreach (var l in Loans) s += l.Balance; return Mathf.CeilToInt(s); } }
        public static int DailyPayments { get { int s = 0; foreach (var l in Loans) s += Mathf.Min(l.Payment, Mathf.CeilToInt(l.Balance * (1f + DailyRate(l.Apr)))); return s; } }
        public static Loan LoanOn(string vehicleUid) => Loans.Find(l => l.Kind == "vehicle" && l.CollateralUid == vehicleUid);

        /// <summary>Bank line of credit: grows with the company's stage and track record, shrinks after a bankruptcy.</summary>
        public static int CreditLimit
        {
            get
            {
                int stage = Mathf.Clamp((int)Company.Stage, 0, CreditByStage.Length - 1);
                int cap = CreditByStage[stage];
                int limit = Mathf.Min(cap, cap / 2 + Company.LifetimeRevenue / 4);
                limit = Mathf.RoundToInt(limit * Mathf.Lerp(0.6f, 1.15f, Company.Reputation / 100f));
                for (int i = 0; i < Bankruptcies; i++) limit /= 2;
                return Mathf.Max(0, limit / 500 * 500);
            }
        }

        public static int AvailableCredit => Mathf.Max(0, CreditLimit - BankDebt);

        public static bool TakeBankLoan(int amount, int termDays, out string msg)
        {
            if (amount < 500) { msg = "Minimum loan is $500."; return false; }
            if (amount > AvailableCredit) { msg = $"The bank will lend up to ${AvailableCredit:N0} right now."; return false; }
            float apr = CurrentBankApr;
            var l = new Loan
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 6), Kind = "bank", Label = $"Bank loan ${amount:N0}",
                Principal = amount, Balance = amount, Apr = apr, TermDays = termDays, Payment = PaymentFor(amount, apr, termDays),
            };
            Loans.Add(l);
            Game.Wallet.Add(amount, "Loan proceeds: bank loan");
            msg = $"Loan funded: ${amount:N0} at {apr * 100f:0.0}% APR, ${l.Payment:N0}/day for {termDays} days.";
            Game.Toast(msg); Notify();
            return true;
        }

        public static Loan AddVehicleLoan(string vehicleUid, string vehicleName, int amount, bool used, int termDays)
        {
            float apr = (used ? VehicleAprUsed : VehicleAprNew) + 0.03f * Bankruptcies;
            var l = new Loan
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 6), Kind = "vehicle", CollateralUid = vehicleUid, Label = $"Auto loan: {vehicleName}",
                Principal = amount, Balance = amount, Apr = apr, TermDays = termDays, Payment = PaymentFor(amount, apr, termDays),
            };
            Loans.Add(l); Notify();
            return l;
        }

        public static bool PayOff(Loan l, out string msg)
        {
            int due = Mathf.CeilToInt(l.Balance);
            if (!Loans.Contains(l)) { msg = "That loan is already closed."; return false; }
            if (!Game.Wallet.TrySpend(due, "Loan payoff: " + l.Label)) { msg = $"Payoff is ${due:N0}; you have ${Game.Wallet.Money:N0}."; return false; }
            Loans.Remove(l);
            msg = $"Paid off {l.Label}."; Game.Toast(msg); Notify();
            return true;
        }

        static void ApplyPayments(bool paid)
        {
            for (int i = Loans.Count - 1; i >= 0; i--)
            {
                var l = Loans[i];
                float interest = l.Balance * DailyRate(l.Apr);
                if (paid)
                {
                    float pay = Mathf.Min(l.Payment, l.Balance + interest);
                    l.Balance = l.Balance + interest - pay; l.DaysPaid++; l.Missed = 0;
                    if (l.Balance < 0.5f) { Loans.RemoveAt(i); Game.Toast($"{l.Label} is paid off."); }
                }
                else { l.Balance += interest + LateFee; l.Missed++; }
            }
        }

        // ------------------------------------------------------------------ ledger / P&L

        public static string Categorize(string reason, bool income)
        {
            string r = reason ?? "";
            if (income)
            {
                if (r.StartsWith("Paid:") || r.StartsWith("Deposit") || r.StartsWith("Invoice") || r.Contains("payment received")) return "Job revenue";
                if (r.StartsWith("Loan proceeds")) return "Loan proceeds";
                if (r.StartsWith("Sold") || r.StartsWith("Auction")) return "Asset sales";
                return "Other income";
            }
            if (r.StartsWith(LoanLabel) || r.StartsWith("Loan payoff")) return "Loan payments";
            if (r.StartsWith("Supply") || r.StartsWith("Materials") || r.StartsWith("Bulk order") || r.Contains("Ready-mix") || r.Contains("concrete")) return "Materials";
            if (r.StartsWith("Vehicle:") || r.StartsWith("Down payment") || r.StartsWith("Rack")) return "Vehicles (purchase)";
            if (r.Contains("Fuel") || r.Contains("fuel")) return "Fuel";
            if (r.Contains("maintenance") || r.Contains("Service")) return "Vehicle maintenance";
            if (r.Contains("insurance") || r.Contains("Insurance")) return "Insurance";
            if (r.Contains("rent") || r.Contains("Rent") || r.StartsWith("Warehouse")) return "Warehouse";
            if (r.Contains("Wage") || r.Contains("wage") || r.Contains("Payroll") || r.Contains("payroll")) return "Payroll";
            if (r.StartsWith("Tool") || r.StartsWith("Shop equipment")) return "Tools & equipment";
            if (r.Contains("nspection") || r.Contains("ermit")) return "Permits & inspections";
            return "Other expenses";
        }

        /// <summary>Pulls new entries from Game.Wallet.Log into the ledger and returns them. Safe to call any time.</summary>
        public static List<LedgerEntry> SyncLedger()
        {
            var fresh = new List<LedgerEntry>();
            var w = Game.Wallet;
            if (w == null) return fresh;
            if (!ReferenceEquals(w, _wallet) || w.Log.Count < _logIndex) { _wallet = w; _logIndex = w.Log.Count; return fresh; }
            for (; _logIndex < w.Log.Count; _logIndex++)
            {
                string s = w.Log[_logIndex];
                if (string.IsNullOrEmpty(s) || s.Length < 3 || s[1] != '$') continue;
                bool income = s[0] == '+';
                int sp = s.IndexOf(' ');
                if (sp < 0 || !int.TryParse(s.Substring(2, sp - 2), out int amount)) continue;
                string reason = s.Substring(sp).Trim();
                var e = new LedgerEntry { day = Today, cat = Categorize(reason, income), amount = income ? amount : -amount };
                var last = Ledger.Count > 0 ? Ledger[Ledger.Count - 1] : null;
                if (last != null && last.day == e.day && last.cat == e.cat) last.amount += e.amount; else Ledger.Add(e);
                fresh.Add(new LedgerEntry { day = e.day, cat = reason, amount = e.amount });   // raw reason for payment matching
            }
            if (Ledger.Count > 4000) Ledger.RemoveRange(0, Ledger.Count - 4000);
            return fresh;
        }

        /// <summary>Totals by category for days in [fromDay, toDay]. Loan proceeds and loan principal are cash flow, shown separately.</summary>
        public static Dictionary<string, int> Totals(int fromDay, int toDay)
        {
            var d = new Dictionary<string, int>();
            foreach (var e in Ledger)
            {
                if (e.day < fromDay || e.day > toDay) continue;
                d[e.cat] = (d.TryGetValue(e.cat, out int v) ? v : 0) + e.amount;
            }
            return d;
        }

        public static int Revenue(int fromDay, int toDay)
        {
            int s = 0;
            foreach (var kv in Totals(fromDay, toDay)) if (kv.Value > 0 && kv.Key != "Loan proceeds") s += kv.Value;
            return s;
        }

        public static int Expenses(int fromDay, int toDay)
        {
            int s = 0;
            foreach (var kv in Totals(fromDay, toDay)) if (kv.Value < 0) s -= kv.Value;
            return s;
        }

        public static int Net(int fromDay, int toDay) => Revenue(fromDay, toDay) - Expenses(fromDay, toDay);

        // ------------------------------------------------------------------ day rollover / insolvency

        static void OnDay(int day)
        {
            if (!GameSettings.IsCareer || GameSettings.UnlimitedMoney) { SyncLedger(); return; }
            var fresh = SyncLedger();
            // Matched by label only: another feature's handler may already have changed today's amount.
            bool Paid(string label, int amount) => fresh.Exists(e => e.cat == label && e.amount < 0);

            int unpaid = 0; bool loansPaid = true;
            foreach (var (label, amount) in Company.DailyCostBreakdown())
            {
                if (Paid(label, amount)) continue;
                unpaid++;
                if (label == LoanLabel) loansPaid = false;
            }
            if (Loans.Count > 0) ApplyPayments(loansPaid);

            if (unpaid > 0)
            {
                InsolventDays++;
                if (InsolventDays >= InsolventDaysToBankruptcy) DeclareBankruptcy();
                else Game.Toast($"Bills unpaid {InsolventDays} day(s) running. Bankruptcy after {InsolventDaysToBankruptcy}.");
            }
            else InsolventDays = 0;
            Notify();
        }

        /// <summary>Chapter 7 style: financed vehicles are repossessed, the rest of the fleet (all but one van) and the
        /// warehouse contents are auctioned to pay the bank, the lease is terminated, remaining debt is discharged,
        /// reputation takes a hit and the company falls back to what it still owns.</summary>
        public static void DeclareBankruptcy()
        {
            var report = new List<string>();
            int repo = 0, sold = 0, proceeds = 0;

            foreach (var l in Loans.FindAll(x => x.Kind == "vehicle"))
            {
                var v = Company.Vehicles.Find(x => x.Uid == l.CollateralUid);
                if (v != null) { Company.Vehicles.Remove(v); Fleet.Forget(v.Uid); repo++; report.Add($"Repossessed: {v.Name}"); }
                Loans.Remove(l);
            }

            var keep = Company.Vehicles.Find(v => v.TypeId == "van");
            foreach (var v in Company.Vehicles.ToArray())
            {
                if (v == keep) continue;
                int price = Mathf.RoundToInt(Fleet.ResaleValue(v) * 0.6f);
                proceeds += price; sold++;
                report.Add($"Auctioned: {v.Name} for ${price:N0}");
                Company.Vehicles.Remove(v); Fleet.Forget(v.Uid);
            }

            int yardCash = Yard.Liquidate(report);
            proceeds += yardCash;
            if (proceeds > 0) Game.Wallet.Add(proceeds, "Auction proceeds (bankruptcy)");

            int discharged = 0;
            foreach (var l in Loans.ToArray())
            {
                int due = Mathf.CeilToInt(l.Balance);
                int pay = Mathf.Min(due, Game.Wallet.Money);
                if (pay > 0) Game.Wallet.TrySpend(pay, "Loan payoff: " + l.Label + " (bankruptcy)");
                discharged += due - pay;
                Loans.Remove(l);
            }
            if (discharged > 0) report.Add($"Debt discharged: ${discharged:N0}");

            Company.Reputation -= BankruptcyRepHit;
            report.Add($"Reputation -{BankruptcyRepHit}");
            Bankruptcies++; InsolventDays = 0; LastBankruptcyDay = Today;

            // The company shrinks back to what it still owns (the one case where the stage goes down).
            Company.Stage = CompanyStage.Handyman;
            Company.RecomputeStage();
            foreach (var j in Company.ActiveJobs.ToArray())
                if (Company.Blocker(new Job { MinStage = j.MinStage, RequiredVehicleType = j.RequiredVehicleType, RequiredTrade = j.RequiredTrade }) is string why && !why.StartsWith("Finish"))
                {
                    Company.ActiveJobs.Remove(j); Company.Reputation -= 2;
                    report.Add($"Lost job: {j.Title} ({why})");
                }

            LastBankruptcyReport = $"BANKRUPTCY (day {Today}): {repo} repossessed, {sold} auctioned.\n" + string.Join("\n", report);
            Game.Toast("Bankruptcy: assets sold, reputation -" + BankruptcyRepHit);
            Debug.Log("[Finance] " + LastBankruptcyReport);
            SyncLedger();
            WentBankrupt?.Invoke(); Company.NotifyChanged(); Notify();
        }

        static void Notify() => Changed?.Invoke();

        /// <summary>Start reading the wallet log from its current end (after a load replaced the wallet).</summary>
        public static void ResetLedgerCursor() { _wallet = Game.Wallet; _logIndex = _wallet != null ? _wallet.Log.Count : 0; }

        // ------------------------------------------------------------------ state

        public static void ResetState()
        {
            Loans.Clear(); Ledger.Clear(); InsolventDays = 0; Bankruptcies = 0; LastBankruptcyDay = 0; LastBankruptcyReport = "";
            _wallet = Game.Wallet; _logIndex = _wallet != null ? _wallet.Log.Count : 0;
        }

        [Serializable]
        public class Data
        {
            public List<Loan> loans = new List<Loan>(); public List<LedgerEntry> ledger = new List<LedgerEntry>();
            public int insolvent, bankruptcies, lastBkDay; public string bkReport;
        }

        public static Data Capture()
        {
            SyncLedger();
            return new Data { loans = new List<Loan>(Loans), ledger = new List<LedgerEntry>(Ledger), insolvent = InsolventDays, bankruptcies = Bankruptcies, lastBkDay = LastBankruptcyDay, bkReport = LastBankruptcyReport };
        }

        public static void Restore(Data d)
        {
            ResetState();
            if (d == null) return;
            Loans.AddRange(d.loans); Ledger.AddRange(d.ledger);
            InsolventDays = d.insolvent; Bankruptcies = d.bankruptcies; LastBankruptcyDay = d.lastBkDay; LastBankruptcyReport = d.bkReport ?? "";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { Loans.Clear(); Ledger.Clear(); InsolventDays = 0; Bankruptcies = 0; LastBankruptcyReport = ""; _wallet = null; _logIndex = 0; Changed = null; WentBankrupt = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Company.RegisterDailyCost(LoanLabel, () => DailyPayments);
            Company.DayStarted += OnDay;          // must see the day's charges before wear changes tomorrow's amounts
            Company.DayStarted += Fleet.OnDay;
        }
    }
}
