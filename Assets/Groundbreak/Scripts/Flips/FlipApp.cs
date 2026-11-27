using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Flips" tablet app: distressed houses for sale (address, asking price, condition report, ARV), the houses you own
    /// (punch list, upgrades, crew, holding costs, listing, offers) and the ones you sold (profit).</summary>
    public static class FlipApp
    {
        public const string AppName = "Flips";
        static int _tab;
        static float _nextRefresh;
        static readonly string[] Tabs = { "For sale", "My flips", "Sold" };
        static readonly Dictionary<string, bool> _financed = new Dictionary<string, bool>();
        static readonly Dictionary<string, string> _priceText = new Dictionary<string, string>();
        static string _msg = "";
        static string _open;
        static GUIStyle _card;
        /// <summary>A dark card (the default box skin is white, which hides the tablet's white text).</summary>
        static GUIStyle Card
        {
            get
            {
                if (_card != null && _card.normal.background != null) return _card;
                var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.07f)); tex.Apply();
                _card = new GUIStyle { padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(0, 0, 4, 6) };
                _card.normal.background = tex;
                return _card;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Tablet.RegisterApp(AppName, Draw);
            Tablet.SetAppGroup(AppName, "Business");
            Tablet.SetAppVisible(AppName, () => GameSettings.IsCareer);
            Tablet.AddHelp("Flips app (Contractor stage+): buy a run-down house, fix its punch list yourself (same repair steps as service calls) or send crew, add upgrades, list it and take an offer");
        }

        /// <summary>Which tab to show (tests, tutorials): 0 for sale, 1 my flips, 2 sold.</summary>
        public static void ShowTab(int tab, string openFlipId = null) { _tab = Mathf.Clamp(tab, 0, 2); if (openFlipId != null) _open = openFlipId; }

        static void Draw()
        {
            var t = Tablet.I;
            var label = t != null ? t.LabelStyle : GUI.skin.label;
            var head = t != null ? t.HeaderStyle : GUI.skin.label;
            var btn = t != null ? t.ButtonStyle : GUI.skin.button;
            GUILayout.Label("Flips — buy, renovate, sell", head);
            if (!GameSettings.IsCareer) { GUILayout.Label("House flipping is part of Career mode.", label); return; }
            if (!Flips.Unlocked) { GUILayout.Label($"Unlocks at company stage {Flips.UnlockStage} (warehouse + a truck). Banks won't lend a handyman on a flip.", label); return; }
            Flips.SyncAll();
            int day = Flips.Today;
            // every couple of seconds at most (not every GUI event when the market has run dry), and only in the Layout pass so
            // the listings don't change between Layout and Repaint
            if ((Event.current == null || Event.current.type == EventType.Layout) && (Time.unscaledTime >= _nextRefresh || Time.unscaledTime < _nextRefresh - 5f)
                && !Flips.Listings.GetEnumerator().MoveNext()) { _nextRefresh = Time.unscaledTime + 2f; Flips.RefreshMarket(day); }
            GUILayout.Label($"Cash ${Game.Wallet.Money:N0} · flips owned {Flips.OwnedCount}/{Flips.MaxOwned} · reputation {Company.Reputation} · market {(Developments.MarketCycle(day) - 1f) * 100f:+0.0;-0.0}%", label);
            _tab = GUILayout.Toolbar(_tab, Tabs, btn);
            if (_msg.Length > 0) GUILayout.Label("<color=#ffe08a>" + _msg + "</color>", label);
            GUILayout.Space(4);
            if (_tab == 0) DrawMarket(label, btn, day);
            else if (_tab == 1) DrawOwned(label, btn, day);
            else DrawSold(label);
        }

        // ------------------------------------------------------------------ for sale

        static void DrawMarket(GUIStyle label, GUIStyle btn, int day)
        {
            GUILayout.Label("Distressed houses on the market in Millbrook. ARV = after-repair value (comps for a renovated house). Buyers pay a 2% closing cost; a hard-money loan covers 90% of the price at 11.5% interest-only + 2 points.", label);
            foreach (var f in new List<FlipHouse>(Flips.Listings))
            {
                GUILayout.BeginVertical(Card);
                var lot = f.Lot;
                GUILayout.Label($"<b>{f.address}</b> · {f.sqft:N0} sq ft ranch · {(lot != null ? lot.District : "")} · seller: {f.seller} · on the market {day - f.listedDay} days", label);
                int arv = Flips.Arv(f, day), rehab = Flips.RehabEstimate(f, false), up = Flips.RehabEstimate(f, true) - rehab;
                GUILayout.Label($"Asking <b>${f.asking:N0}</b> · ARV ${arv:N0} · repairs (contractor est.) ${rehab:N0} + upgrades ${up:N0} · {f.asking / (float)arv * 100f:0}% of ARV", label);
                GUILayout.Label("<color=#c9a0a0>Condition report:</color>", label);
                foreach (var it in f.Problems) { var d = it.Def; if (d != null) GUILayout.Label($"   • {d.Label}: {d.Report}  <color=#9aa3ad>(buyers knock ${d.Value:N0} off)</color>", label); }
                foreach (var it in f.UpgradeItems) { var d = it.Def; if (d != null) GUILayout.Label($"   ◦ {d.Report} <color=#9aa3ad>Upgrade adds ${d.Value:N0}</color>", label); }
                bool fin = !_financed.TryGetValue(f.id, out var v) || v;
                int cash = Flips.CashToClose(f, fin);
                int projected = arv - f.asking - Mathf.RoundToInt(f.asking * Flips.BuyClosingPct) - Mathf.RoundToInt(arv * (Flips.CommissionPct + Flips.SellClosingPct)) - (fin ? Mathf.RoundToInt(Flips.LoanAmount(f) * Flips.LoanPoints) : 0);
                GUILayout.Label($"Cash to close ${cash:N0}" + (fin ? $" (loan ${Flips.LoanAmount(f):N0}, ~${Mathf.RoundToInt(Flips.LoanAmount(f) * Flips.LoanApr / 365f + f.asking * Flips.TaxRate / 365f + Flips.InsurancePerYear / 365f + Flips.UtilitiesPerDay):N0}/day to hold)" : $" (~${Mathf.RoundToInt(f.asking * Flips.TaxRate / 365f + Flips.InsurancePerYear / 365f + Flips.UtilitiesPerDay):N0}/day to hold)") +
                    $" · profit if you do the work yourself: about ${projected:N0} before materials and holding", label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(fin ? "Financing: hard-money loan" : "Financing: all cash", btn, GUILayout.Width(300))) _financed[f.id] = !fin;
                string why = Flips.BuyBlocker(f, fin);
                GUI.enabled = why == null;
                if (GUILayout.Button($"Buy for ${f.asking:N0}", btn, GUILayout.Width(200))) { Flips.Buy(f, fin, out _msg); if (f.Status == FlipStatus.Owned) { _tab = 1; _open = f.id; } }
                GUI.enabled = true;
                if (why != null && cash > Game.Wallet.Money && Finance.AvailableCredit >= cash - Game.Wallet.Money)
                    if (GUILayout.Button($"Borrow ${cash - Game.Wallet.Money:N0} from the bank", btn, GUILayout.Width(260))) Flips.BorrowShortfall(f, fin, out _msg);
                if (GUILayout.Button("Waypoint", btn, GUILayout.Width(110)) && lot != null) RepairJobs.SetWaypoint(lot);
                GUILayout.EndHorizontal();
                if (why != null) GUILayout.Label("<color=#ff9a8a>" + why + "</color>", label);
                GUILayout.EndVertical();
            }
            if (!Flips.Listings.GetEnumerator().MoveNext()) GUILayout.Label("Nothing on the market today. New listings come up every few days.", label);
        }

        // ------------------------------------------------------------------ owned

        static void DrawOwned(GUIStyle label, GUIStyle btn, int day)
        {
            var owned = new List<FlipHouse>(Flips.Owned);
            if (owned.Count == 0) { GUILayout.Label("You don't own a flip yet. Buy one on the For sale tab.", label); return; }
            foreach (var f in owned)
            {
                GUILayout.BeginVertical(Card);
                bool open = _open == f.id || owned.Count == 1;
                int value = Flips.Value(f, day), arv = Flips.Arv(f, day);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{f.address}</b> · bought day {f.boughtDay} for ${f.purchasePrice:N0} · {(f.Status == FlipStatus.ForSale ? $"LISTED at ${f.listPrice:N0}" : "renovating")} · value now <b>${value:N0}</b> (ARV ${arv:N0}) · condition {Flips.Condition(f) * 100f:0}%", label);
                if (!open && GUILayout.Button("Open", btn, GUILayout.Width(80))) _open = f.id;
                GUILayout.EndHorizontal();
                GUILayout.Label($"Into it so far ${f.costSpent:N0} · holding ${Flips.DailyCarry(f):N0}/day (tax ${Flips.DailyTax(f)}, insurance ${Flips.DailyInsurance(f)}, utilities ${Flips.UtilitiesPerDay}, interest ${Flips.DailyInterest(f)}) · loan ${f.loanBalance:N0}", label);
                if (open) DrawPunchList(f, label, btn, day);
                GUILayout.EndVertical();
            }
        }

        static void DrawPunchList(FlipHouse f, GUIStyle label, GUIStyle btn, int day)
        {
            GUILayout.Label("<color=#c9a0a0>Punch list</color> (walk up to each problem and fix it like a service call, or send crew):", label);
            foreach (var it in f.Problems) Row(f, it, label, btn);
            GUILayout.Label("<color=#a0c9a0>Upgrades</color> (optional, each adds value when done):", label);
            foreach (var it in f.UpgradeItems) Row(f, it, label, btn);

            var crew = new List<Worker>(Flips.CrewOn(f));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Crew here: {(crew.Count == 0 ? "nobody" : string.Join(", ", crew.ConvertAll(w => $"{w.Name} ({CrewTrades.Get(w.Trade).Label})")))}", label);
            if (Company.Workers.Count > 0 && GUILayout.Button("Send a worker", btn, GUILayout.Width(150))) { var w = Flips.AssignNext(f); _msg = w != null ? $"{w.Name} will work on {f.address} each day." : "Everybody is busy on other jobs (Crew app)."; }
            if (crew.Count > 0 && GUILayout.Button("Pull crew off", btn, GUILayout.Width(140))) Flips.Release(f);
            GUILayout.EndHorizontal();

            if (f.Status == FlipStatus.Owned)
            {
                int suggested = Flips.Round500(Flips.Value(f, day) * 0.99f);
                if (!_priceText.TryGetValue(f.id, out var txt)) txt = suggested.ToString();
                GUILayout.BeginHorizontal();
                GUILayout.Label("List price $", label, GUILayout.Width(90));
                txt = GUILayout.TextField(txt, GUILayout.Width(120));
                _priceText[f.id] = txt;
                int.TryParse(txt.Replace(",", "").Replace("$", ""), out int price);
                int exp = price > 0 ? Flips.ExpectedDaysToOffer(f, price, day) : int.MaxValue;
                if (GUILayout.Button($"Use value (${suggested:N0})", btn, GUILayout.Width(250))) _priceText[f.id] = suggested.ToString();
                GUI.enabled = price > 0;
                if (GUILayout.Button("List it for sale", btn, GUILayout.Width(150))) Flips.List(f, price, out _msg);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (price > 0)
                    GUILayout.Label(exp == int.MaxValue ? "<color=#ff9a8a>More than 15% over value: buyers won't look at it.</color>"
                        : $"At ${price:N0} ({price / (float)Mathf.Max(1, Flips.Value(f, day)) * 100f:0}% of value) expect a first offer in about {exp} days. Net after 5.5% commission + 1.5% closing: ${Mathf.RoundToInt(price * (1f - Flips.CommissionPct - Flips.SellClosingPct)):N0}." +
                          (Flips.AllProblemsFixed(f) ? "" : " Open problems scare buyers off and cut the price."), label);
            }
            else
            {
                if (f.HasOffer)
                {
                    int net = Mathf.RoundToInt(f.offerPrice * (1f - Flips.CommissionPct - Flips.SellClosingPct));
                    GUILayout.Label($"<b>Offer: ${f.offerPrice:N0}</b> from {f.buyer} (good until day {f.offerExpires}) · you'd net ${net:N0} before the loan payoff · profit about ${net - f.costSpent:N0}", label);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Accept offer", btn, GUILayout.Width(150))) Flips.AcceptOffer(f, out _msg);
                    if (GUILayout.Button("Decline", btn, GUILayout.Width(110))) Flips.DeclineOffer(f);
                    GUILayout.EndHorizontal();
                }
                else GUILayout.Label(f.nextOfferDay >= 0 ? $"Listed day {f.saleListedDay}. Showings going on; next offer expected around day {f.nextOfferDay}." : "Listed, but no buyer is interested at this price. Lower it.", label);
                if (GUILayout.Button("Take it off the market (change price / keep working)", btn, GUILayout.Width(400))) Flips.Unlist(f);
            }
        }

        static void Row(FlipHouse f, FlipItem it, GUIStyle label, GUIStyle btn)
        {
            var d = it.Def; if (d == null) return;
            string state = it.done ? $"<color=#8fe28f>done ({it.quality * 100f:0}%)</color>" : it.crewHours > 0f ? $"crew {it.crewHours:0.#}/{FlipDefs.BookHours(d):0.#} h" : "<color=#ff9a8a>to do</color>";
            GUILayout.BeginHorizontal();
            GUILayout.Label($"   {(it.done ? "[x]" : "[ ]")} {d.Label} — {state} · {FlipDefs.BookHours(d):0.#} book hours · {(it.upgrade ? "+" : "-")}${d.Value:N0}", label);
            if (it.upgrade && !it.done && GUILayout.Button(it.planned ? "Crew: yes" : "Crew: no", btn, GUILayout.Width(100))) it.planned = !it.planned;
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ sold

        static void DrawSold(GUIStyle label)
        {
            int n = 0, total = 0;
            foreach (var f in Flips.All)
            {
                if (f.Status != FlipStatus.Sold) continue;
                n++; total += Flips.Profit(f);
                GUILayout.Label($"{f.address}: bought ${f.purchasePrice:N0} (day {f.boughtDay}) → sold ${f.salePrice:N0} (day {f.soldDay}) to {f.buyer} · in ${f.costSpent:N0} · <b>profit ${Flips.Profit(f):N0}</b>", label);
            }
            GUILayout.Label(n == 0 ? "No flips sold yet." : $"{n} flip(s) sold · total profit ${total:N0}", label);
        }
    }
}
