using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Contractor Jobs" tablet app: the remodel/outbuilding job board, the active job's takeoff and live
    /// checklist, and the final-payment request.</summary>
    public static class ContractorJobsApp
    {
        public const string AppName = "Contractor Jobs";

        static readonly Dictionary<string, JobCheck> _checks = new Dictionary<string, JobCheck>();
        static float _nextCheck;
        static string _showScope;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _checks.Clear(); _nextCheck = 0f; _showScope = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Tablet.RegisterApp(AppName, Draw);
            Tablet.AddHelp("Contractor Jobs app (tablet) - decks, fences, sheds, garages, flatwork and re-roofs: accept a job, haul the takeoff to the staked lot, build it, then request final payment");
        }

        static string VehicleName(string id)
        {
            switch (id)
            {
                case "van": return "work van";
                case "pickup": return "pickup truck";
                case "flatbed": return "flatbed truck";
                case "dump": return "dump truck";
                case "box": return "box truck";
                case "boom": return "boom truck";
                case "skidsteer": return "skid steer + trailer";
            }
            return string.IsNullOrEmpty(id) ? "any vehicle" : id;
        }

        static void RefreshChecks()
        {
            // only between frames' layout passes, so the Layout and Repaint events see the same content
            if (Event.current == null || Event.current.type != EventType.Layout) return;
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;
            _checks.Clear();
            foreach (var j in Company.ActiveJobs)
                if (ContractorJobs.IsContractorJob(j)) _checks[j.Id] = ContractorJobs.Verify(j);
        }

        static void Draw()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle;
            float fs = label.fontSize;
            int today = GameClock.I != null ? GameClock.I.Day : 1;
            RefreshChecks();

            GUILayout.Label($"{Company.Name}  ·  {Company.Stage}  ·  reputation {Company.Reputation}  ·  day {today}", header);
            if (Company.Stage < CompanyStage.Contractor)
                GUILayout.Label("Remodels and outbuildings need a Contractor company: rent a warehouse bay and buy a truck.", label);
            GUILayout.Space(6);

            // ---------------- active jobs
            bool anyActive = false;
            foreach (var j in new List<Job>(Company.ActiveJobs))
            {
                if (!ContractorJobs.IsContractorJob(j)) continue;
                anyActive = true;
                DrawActive(j, today, label, header, button, fs);
                GUILayout.Space(10);
            }
            if (!anyActive) GUILayout.Label("No remodel or outbuilding job in progress.", label);

            // ---------------- offers
            GUILayout.Space(6);
            GUILayout.Label("Job board", header);
            bool anyOffer = false;
            foreach (var j in new List<Job>(Company.Offers))
            {
                if (!ContractorJobs.IsContractorJob(j)) continue;
                anyOffer = true;
                DrawOffer(j, today, label, header, button, fs);
                GUILayout.Space(6);
            }
            if (!anyOffer) GUILayout.Label("No offers right now - new requests come in each morning.", label);
        }

        static void DrawOffer(Job j, int today, GUIStyle label, GUIStyle header, GUIStyle button, float fs)
        {
            string blocker = Company.Blocker(j);
            GUILayout.BeginHorizontal();
            GUILayout.Label(j.Title, header, GUILayout.Width(fs * 24));
            GUILayout.Label($"${j.Payout:N0}", header, GUILayout.Width(fs * 6));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_showScope == j.Id ? "Hide scope" : "Scope", button, GUILayout.Width(fs * 7))) _showScope = _showScope == j.Id ? null : j.Id;
            GUI.enabled = blocker == null;
            if (GUILayout.Button("Accept", button, GUILayout.Width(fs * 7)))
            {
                Company.Accept(j, out var msg);
                Show(msg);
                _nextCheck = 0f;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label($"{j.Client}  ·  {j.Address}", label);
            GUILayout.Label($"Due day {j.DueDay} ({Mathf.Max(0, j.DueDay - today)} day(s))  ·  materials about ${j.MaterialBudget:N0}  ·  needs a {VehicleName(j.RequiredVehicleType)}", label);
            if (blocker != null)
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.5f);
                GUILayout.Label("Can't take it yet: " + blocker, label);
                GUI.color = prev;
            }
            if (_showScope == j.Id) GUILayout.Label(j.Description, label);
        }

        static void DrawActive(Job j, int today, GUIStyle label, GUIStyle header, GUIStyle button, float fs)
        {
            int left = j.DueDay - today;
            GUILayout.Label($"In progress: {j.Title}  ·  ${j.Payout:N0}", header);
            GUILayout.Label($"{j.Client}  ·  {j.Address}  ·  " + (left >= 0 ? $"due day {j.DueDay} ({left} day(s) left)" : $"LATE by {-left} day(s): pay drops 2% a day"), label);
            GUILayout.Label(j.Description, label);
            if (Game.Player != null)
            {
                Vector3 d = j.WorldPos - Game.Player.transform.position; d.y = 0f;
                GUILayout.Label($"The lot is {Units.Format(d.magnitude)} away - look for the orange-flagged stakes and the job sign.", label);
            }

            // takeoff
            GUILayout.Space(4);
            var takeoff = ContractorJobs.Takeoff(j);
            int catalogCost = ContractorJobs.CatalogCost(takeoff);
            GUILayout.Label($"Material takeoff  ·  about ${ContractorJobs.TakeoffCost(j):N0}", header);
            foreach (var kv in takeoff)
            {
                var def = Catalog.Get(kv.Key);
                if (def == null) continue;
                int have = Game.Stock.Count(kv.Key);
                int need = kv.Value * Mathf.Max(1, def.UnitsPerPurchase);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{kv.Value} x", label, GUILayout.Width(fs * 4));
                GUILayout.Label(def.Name, label, GUILayout.Width(fs * 26));
                GUILayout.Label($"${def.Price * kv.Value:N0}", label, GUILayout.Width(fs * 5));
                GUILayout.Label(have >= need ? "in stock" : $"have {have} of {need}", label);
                GUILayout.EndHorizontal();
            }
            float yards = ContractorJobs.ReadyMixYards(j);
            if (yards > 0f)
            {
                var spec = ContractorJobs.SpecOf(j);
                GUILayout.Label($"{yards:0.##} cu yd of {(spec != null ? spec.psi : 3000)} psi ready-mix - order it from Concrete Dispatch when the forms are ready", label);
            }
            if (takeoff.Count > 0 && DeliveryService.I != null && GUILayout.Button($"Order what's missing (${MissingCost(takeoff):N0})", button, GUILayout.Width(fs * 18)))
                OrderMissing(takeoff);

            // re-roof: the roofing stages are started here (there is no Framing Plans project for an existing house)
            var roof = ContractorJobSite.RoofOf(j);
            if (roof != null)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = roof.Sheathed && roof.DryInJob == null;
                if (GUILayout.Button("Lay out dry-in (drip edge, ice & water, underlayment)", button)) { Roofing.DryIn(roof); Show("Dry-in laid out on the roof - cap stapler and hammer."); }
                GUI.enabled = roof.DriedIn && roof.ShinglesJob == null;
                if (GUILayout.Button("Lay out shingles (starter, courses, ridge)", button)) { Roofing.Shingles(roof); Show("Shingle courses chalked - roofing nailer or hatchet."); }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            // live checklist
            GUILayout.Space(4);
            _checks.TryGetValue(j.Id, out var c);
            if (c == null) { GUILayout.Label("Checking the work...", label); return; }
            GUILayout.Label($"Client walk-through  ·  {c.Done.Count} of {c.Done.Count + c.Missing.Count} items  ·  workmanship {c.Quality * 100f:0}%", header);
            var prev = GUI.color;
            GUI.color = new Color(0.55f, 1f, 0.55f);
            foreach (var d in c.Done) GUILayout.Label("  [x] " + d, label);
            GUI.color = new Color(1f, 0.6f, 0.5f);
            foreach (var m in c.Missing) GUILayout.Label("  [  ] " + m, label);
            GUI.color = new Color(1f, 0.9f, 0.5f);
            foreach (var n in c.Notes) GUILayout.Label("  note: " + n, label);
            GUI.color = prev;

            GUI.enabled = c.Ok;
            if (GUILayout.Button(c.Ok ? "Request final payment" : "Request final payment (finish the list first)", button, GUILayout.Width(fs * 26)))
            {
                ContractorJobs.TryComplete(j, out var msg);
                Show(msg);
                _nextCheck = 0f;
            }
            GUI.enabled = true;
        }

        static void Show(string msg)
        {
            if (Tablet.I != null) Tablet.I.ShowMessage(msg);
            else Game.Toast(msg);
        }

        static Dictionary<string, int> MissingCart(Dictionary<string, int> takeoff)
        {
            var cart = new Dictionary<string, int>();
            foreach (var kv in takeoff)
            {
                var def = Catalog.Get(kv.Key);
                if (def == null) continue;
                int per = Mathf.Max(1, def.UnitsPerPurchase);
                int need = kv.Value * per - Game.Stock.Count(kv.Key);
                if (need > 0) cart[kv.Key] = Mathf.CeilToInt(need / (float)per);
            }
            return cart;
        }

        static int MissingCost(Dictionary<string, int> takeoff) => ContractorJobs.CatalogCost(MissingCart(takeoff));

        static void OrderMissing(Dictionary<string, int> takeoff)
        {
            var cart = MissingCart(takeoff);
            if (cart.Count == 0) { Show("Everything on the takeoff is already in stock."); return; }
            int cost = ContractorJobs.CatalogCost(cart);
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(cost, "Job materials")) { Show($"Need ${cost:N0} for the materials."); return; }
            DeliveryService.I.Order(cart, false);
            Show($"Ordered ${cost:N0} of materials - the truck drops them at the delivery point.");
        }
    }
}
