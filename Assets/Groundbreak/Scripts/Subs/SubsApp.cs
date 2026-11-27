using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Subcontractors" tablet app: local trade companies by trade (price level, quality, reliability, lead time,
    /// your history with them), a quote for the remaining work on this site, booking, and the booked / finished subs
    /// (status, cancel, warranty callback).</summary>
    public static class SubsApp
    {
        public const string AppName = "Subcontractors";
        static int _tab;
        static readonly string[] Tabs = { "Find a sub", "Booked" };
        static string _trade = "framing";
        static readonly Dictionary<string, SubQuote> _quotes = new Dictionary<string, SubQuote>();
        static string _msg = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Tablet.RegisterApp(AppName, Draw);
            Tablet.SetAppGroup(AppName, "Business");
            Tablet.SetAppVisible(AppName, () => GameSettings.IsCareer);
            Tablet.AddHelp("Subcontractors app (Career, Contractor stage): get a quote from a local trade company for the work left on this site, book them, pay when they finish");
        }

        /// <summary>Tests / tutorials: open the app on a trade with quotes already fetched.</summary>
        public static void Show(string trade, int tab = 0)
        {
            _trade = trade; _tab = tab; _quotes.Clear();
            if (Subs.Unlocked) foreach (var c in SubCatalog.For(trade)) _quotes[c.Id] = Subs.Quote(c);
        }

        static void Draw()
        {
            var t = Tablet.I;
            var label = t != null ? t.LabelStyle : GUI.skin.label;
            var head = t != null ? t.HeaderStyle : GUI.skin.label;
            var btn = t != null ? t.ButtonStyle : GUI.skin.button;
            GUILayout.Label("Subcontractors", head);
            if (!GameSettings.IsCareer) { GUILayout.Label("Hiring subs is part of Career mode.", label); return; }
            if (Company.Stage < Subs.MinStage)
            {
                GUILayout.Label("Local trade companies won't bid for a handyman. Become a Contractor (rent a warehouse and buy a truck) to hire subs.", label);
                return;
            }
            Subs.CurrentSite(out var job, out _, out _);
            GUILayout.Label($"Site: {Crew.JobLabel(job)} · subs are paid on completion from your bank account (${Game.Wallet.Money:N0}) · they bring labor, you supply materials on site", label);
            _tab = GUILayout.Toolbar(_tab, new[] { Tabs[0], $"{Tabs[1]} ({Subs.Contracts.FindAll(c => c.Active).Count})" }, btn);
            GUILayout.Space(6);
            if (_tab == 0) DrawFind(label, btn); else DrawBooked(label, btn);
            if (!string.IsNullOrEmpty(_msg)) GUILayout.Label(_msg, label);
        }

        static void DrawFind(GUIStyle label, GUIStyle btn)
        {
            // trade picker: 12 trades in rows of 6
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = row * 6; i < Mathf.Min(SubCatalog.Trades.Count, row * 6 + 6); i++)
                {
                    var tr = SubCatalog.Trades[i];
                    bool sel = tr.Id == _trade;
                    if (GUILayout.Button(sel ? "[" + tr.Label + "]" : tr.Label, btn)) { _trade = tr.Id; _quotes.Clear(); _msg = ""; }
                }
                GUILayout.EndHorizontal();
            }
            var info = SubCatalog.Trade(_trade);
            if (info == null) return;
            GUILayout.Label($"{info.Label}: {info.Scope}. Typical ${Price(info.Rate)}/{info.Unit}, ${info.Minimum:N0} minimum.", label);
            foreach (var c in SubCatalog.For(_trade))
            {
                GUILayout.BeginVertical(GUI.skin.box);
                int done = Subs.JobsWith.TryGetValue(c.Id, out int n) ? n : 0;
                string rel = done > 0 ? $" · {done} job(s) together: {Mathf.RoundToInt(Subs.DiscountWith(c.Id) * 100)}% off" : "";
                GUILayout.Label($"{c.Name} ({c.Phone}) — {c.PriceLevel} · quality {SubCatalog.QualityLabel(c.Quality)} [{CrewTrades.Stars(c.Quality)}] · {SubCatalog.ReliabilityLabel(c.Reliability)} · booked {c.LeadDays} day(s) out{rel}", label);
                _quotes.TryGetValue(c.Id, out var q);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(q == null ? "Request quote" : "Re-measure", btn, GUILayout.Width(150))) { _quotes[c.Id] = Subs.Quote(c); _msg = ""; q = _quotes[c.Id]; }
                if (q != null)
                {
                    GUILayout.Label($"{q.Detail}: {Qty(q.Qty)} {q.Trade.Unit} → ${q.Price:N0}" + (q.Discount > 0 ? $" (${q.Discount:N0} repeat-customer discount)" : "")
                        + $" · your crew: ~{q.CrewDays:0.#} worker-days, ${q.CrewCost:N0} wages", label);
                }
                GUILayout.EndHorizontal();
                if (q != null)
                {
                    if (q.Blocker != null) GUILayout.Label(q.Blocker, label);
                    else if (GUILayout.Button($"Accept ${q.Price:N0} — book {c.Name}", btn))
                    {
                        Subs.Accept(q, out _msg);
                        _quotes.Remove(c.Id);
                        _tab = 1;
                    }
                }
                GUILayout.EndVertical();
            }
        }

        static void DrawBooked(GUIStyle label, GUIStyle btn)
        {
            if (Subs.Contracts.Count == 0) { GUILayout.Label("No subs booked. Get a quote on the Find a sub tab.", label); return; }
            int day = GameClock.I != null ? GameClock.I.Day : 1;
            foreach (var k in Subs.Contracts.ToArray())
            {
                var c = k.Company; var info = k.TradeInfo;
                GUILayout.BeginVertical(GUI.skin.box);
                string when = k.State == SubState.Scheduled ? (k.LateNotified ? $"late - now day {k.StartDay}" : $"day {k.PromisedDay}" + (k.PromisedDay <= day ? " (today)" : ""))
                    : k.State == SubState.Done ? $"finished day {k.FinishedDay}" : "on site";
                GUILayout.Label($"{info?.Label}: {c?.Name} · {Crew.JobLabel(k.JobId)} · {Qty(k.Qty)} {info?.Unit} · ${k.Price:N0}{(k.Paid ? " paid" : k.State == SubState.Done ? " UNPAID" : " on completion")} · {when}", label);
                GUILayout.Label("  " + k.Status, label);
                GUILayout.BeginHorizontal();
                if (k.State == SubState.Scheduled && GUILayout.Button("Cancel booking", btn, GUILayout.Width(170))) { Subs.Cancel(k); _msg = "Booking cancelled"; }
                if (k.State == SubState.Done && (k.Mistakes > 0 || k.InspectionFailed) && GUILayout.Button("Call back to fix (warranty)", btn, GUILayout.Width(250)))
                    Subs.RequestCallback(k, out _msg);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }

        static string Price(float rate) => rate < 10f ? rate.ToString("0.00") : rate.ToString("N0");
        static string Qty(float q) => q < 10f ? q.ToString("0.#") : q.ToString("N0");
    }
}
