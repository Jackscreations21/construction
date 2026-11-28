using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Company" tablet app: stage and next-stage checklist, P&amp;L, daily costs, bank loan and vehicle loans.</summary>
    public static class CompanyApp
    {
        static int _period = 1;            // 0 today, 1 last 7 days, 2 all time
        static int _loanAmount = 5000, _loanTerm = 1;
        static string _msg = "";
        static readonly string[] Periods = { "Today", "Last 7 days", "All time" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Tablet.RegisterApp("Company", Draw);
            Tablet.AddHelp("Company app (tablet) - stage, profit & loss, daily costs, bank and vehicle loans. Unpaid bills 3 days running = bankruptcy");
        }

        /// <summary>What the company still needs for the next stage (empty at Developer).</summary>
        public static List<(string item, bool done)> NextStageChecklist()
        {
            var l = new List<(string, bool)>();
            bool truck = Company.Vehicles.Exists(v => v.TypeId != "van");
            switch (Company.Stage)
            {
                case CompanyStage.Handyman:
                    l.Add(("Lease a warehouse (Warehouse app)", Company.HasWarehouse));
                    l.Add(("Own a truck other than the van (Dealership)", truck)); break;
                case CompanyStage.Contractor:
                    l.Add(($"Hire 2 workers ({Company.Workers.Count}/2)", Company.Workers.Count >= 2)); break;
                case CompanyStage.Crew:
                    l.Add(($"Reputation 65 ({Company.Reputation})", Company.Reputation >= 65));
                    l.Add(("Yard + shop (warehouse level 2)", Company.WarehouseLevel >= 2)); break;
                case CompanyStage.Builder:
                    l.Add(($"Reputation 80 ({Company.Reputation})", Company.Reputation >= 80));
                    l.Add(("Headquarters (warehouse level 3)", Company.WarehouseLevel >= 3));
                    l.Add(($"8 workers ({Company.Workers.Count}/8)", Company.Workers.Count >= 8)); break;
            }
            return l;
        }

        static void Draw()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle;
            float fs = label.fontSize;
            int today = GameClock.I != null ? GameClock.I.Day : 1;
            Finance.SyncLedger();

            GUILayout.Label($"{Company.Name}  —  {Company.Stage}", header);
            GUILayout.Label($"Cash ${Game.Wallet.Money:N0}  ·  Debt ${Finance.TotalDebt:N0}  ·  Reputation {Company.Reputation}  ·  Jobs done {Company.JobsCompleted}  ·  Lifetime revenue ${Company.LifetimeRevenue:N0}", label);
            int assets = Yard.StockValue; foreach (var v in Company.Vehicles) assets += Fleet.ResaleValue(v);
            GUILayout.Label($"Fleet {Company.Vehicles.Count} vehicle(s) · Warehouse: {(Yard.Current != null ? Yard.Current.Name : "none")} · Crew {Company.Workers.Count} · Equity ≈ ${Game.Wallet.Money + assets - Finance.TotalDebt:N0}", label);
            if (Finance.InsolventDays > 0) { var c = GUI.color; GUI.color = new Color(1f, 0.5f, 0.45f); GUILayout.Label($"WARNING: bills unpaid {Finance.InsolventDays} day(s). Bankruptcy after {Finance.InsolventDaysToBankruptcy}.", header); GUI.color = c; }
            if (!string.IsNullOrEmpty(Finance.LastBankruptcyReport)) GUILayout.Label(Finance.LastBankruptcyReport, label);

            var next = NextStageChecklist();
            if (next.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label($"Next stage: {(CompanyStage)((int)Company.Stage + 1)}", header);
                foreach (var (item, done) in next)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(UISkin.Icon(done ? "check" : "dot"), GUILayout.Width(fs * 1.4f), GUILayout.Height(fs * 1.4f));
                    GUILayout.Label(item, label);
                    GUILayout.EndHorizontal();
                }
            }

            // ---- daily costs
            GUILayout.Space(8);
            int daily = 0;
            GUILayout.Label("Daily costs (charged each morning)", header);
            foreach (var (lbl, amount) in Company.DailyCostBreakdown()) { GUILayout.Label($"  {lbl,-24} ${amount:N0}", label); daily += amount; }
            GUILayout.Label(daily > 0 ? $"  Total ${daily:N0}/day" : "  none", label);

            // ---- P&L
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Profit & loss", header, GUILayout.Width(fs * 9));
            _period = GUILayout.SelectionGrid(_period, Periods, 3, button, GUILayout.Width(fs * 21));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            int from = _period == 0 ? today : _period == 1 ? today - 6 : int.MinValue;
            var totals = Finance.Totals(from, today);
            var keys = new List<string>(totals.Keys); keys.Sort((a, b) => totals[b].CompareTo(totals[a]));
            foreach (var k in keys) GUILayout.Label($"  {k,-24} {(totals[k] >= 0 ? "+" : "-")}${Mathf.Abs(totals[k]):N0}", label);
            if (keys.Count == 0) GUILayout.Label("  (no transactions yet)", label);
            int net = Finance.Net(from, today);
            GUILayout.Label($"  Revenue ${Finance.Revenue(from, today):N0}  ·  Expenses ${Finance.Expenses(from, today):N0}  ·  Net {(net >= 0 ? "+" : "-")}${Mathf.Abs(net):N0}  (loan proceeds excluded)", header);

            // ---- loans
            GUILayout.Space(8);
            GUILayout.Label($"Bank: line of credit ${Finance.CreditLimit:N0}, available ${Finance.AvailableCredit:N0} at {Finance.CurrentBankApr * 100:0.0}% APR", header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-1,000", button, GUILayout.Width(fs * 5))) _loanAmount = Mathf.Max(500, _loanAmount - 1000);
            GUILayout.Label($"${_loanAmount:N0}", header, GUILayout.Width(fs * 6));
            if (GUILayout.Button("+1,000", button, GUILayout.Width(fs * 5))) _loanAmount += 1000;
            if (GUILayout.Button("+10,000", button, GUILayout.Width(fs * 6))) _loanAmount += 10000;
            for (int i = 0; i < Finance.BankTerms.Length; i++)
                if (GUILayout.Toggle(_loanTerm == i, $" {Finance.BankTerms[i]} d", button, GUILayout.Width(fs * 4.5f))) _loanTerm = i;
            int term = Finance.BankTerms[_loanTerm];
            int pay = Finance.PaymentFor(_loanAmount, Finance.CurrentBankApr, term);
            if (GUILayout.Button($"Borrow (${pay:N0}/day)", button, GUILayout.Width(fs * 10))) { if (Finance.TakeBankLoan(_loanAmount, term, out _msg)) GUIUtility.ExitGUI(); }   // a loan row appears: end this GUI event
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label($"  Total interest over the term: ${Finance.TotalInterest(_loanAmount, Finance.CurrentBankApr, term):N0}", label);
            if (!string.IsNullOrEmpty(_msg)) GUILayout.Label(_msg, header);

            foreach (var l in Finance.Loans.ToArray())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"  {l.Label}: balance ${l.Balance:N0} · {l.Apr * 100:0.0}% · ${l.Payment:N0}/day · {l.DaysLeft} days left" + (l.Missed > 0 ? $" · {l.Missed} MISSED" : ""), label, GUILayout.Width(fs * 30));
                if (GUILayout.Button($"Pay off ${Mathf.CeilToInt(l.Balance):N0}", button, GUILayout.Width(fs * 9))) { if (Finance.PayOff(l, out _msg)) GUIUtility.ExitGUI(); }   // the row disappears
                GUILayout.EndHorizontal();
            }
        }
    }
}
