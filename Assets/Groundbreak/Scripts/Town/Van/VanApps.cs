using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Tablet apps for the work van: "Van" (cargo, roof rack, fast travel, load/unload at the build site) and
    /// "Hardware Store" (Hartley's: buy registered items straight into the van while parked there).</summary>
    public static class VanApps
    {
        public const float StoreRange = 30f, SiteRange = 25f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Tablet.RegisterApp("Van", DrawVan);
            Tablet.RegisterApp("Hardware Store", DrawStore);
            Tablet.AddHelp("Work van: E at the cab - get in / out,  W/S - throttle / reverse,  A/D - steer,  Space - brake,  V - chase camera");
            Tablet.AddHelp("Van: E at the rear doors - open / close,  E at the roof rack - take / stow the 24' ladder");
            Tablet.AddHelp("Ladder: E on a wall or eave - stand it,  walk into it + W / S - climb,  Space - let go,  E on it - pick up");
            Tablet.AddHelp("Tablet > Van - cargo and fast travel,  Tablet > Hardware Store - shop while parked at Hartley's (201 Main St)");
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        public static bool AtStore
        {
            get
            {
                var v = WorkVan.I; if (v == null || v.SpeedMph > 2f) return false;
                var s = Town.Store; Vector3 p = v.transform.position;
                return s != null && (s.Contains(p) || Flat(p, s.Parking) <= StoreRange);
            }
        }

        public static bool AtBuildSite
        {
            get
            {
                var v = WorkVan.I; if (v == null || v.SpeedMph > 2f) return false;
                var s = Town.BuildSite; Vector3 p = v.transform.position;
                return (s != null && s.Contains(p)) || (DeliveryService.I != null && Flat(p, DeliveryService.I.DropPoint) <= SiteRange);
            }
        }

        static void Bar(string text, float used, float cap, GUIStyle label)
        {
            GUILayout.Label(text, label);
            Rect r = GUILayoutUtility.GetRect(100f, label.fontSize * 0.7f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            float k = cap > 0f ? Mathf.Clamp01(used / cap) : 0f;
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.15f * prev.a); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = (k > 0.9f ? new Color(0.95f, 0.35f, 0.25f) : k > 0.7f ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.35f, 0.8f, 0.4f)) * new Color(1f, 1f, 1f, prev.a);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * k, r.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        static string Qty(PieceDefinition d, int n) => d != null && d.Category == PieceCategory.Consumable && d.UnitsPerPurchase > 1 ? n + " pcs" : n.ToString();

        static void Travel(Tablet t, string name, TownLot lot, GUIStyle button)
        {
            if (lot == null || !GUILayout.Button(name, button)) return;
            t.ShowMessage(WorkVan.FastTravel(lot) ? $"Drove to {lot.Address}." : "No van.");
        }

        static void DrawVan()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle; float fs = label.fontSize;
            if (!GameSettings.IsCareer) { GUILayout.Label("The work van is part of Career mode.", label); return; }
            var van = WorkVan.I;
            if (van == null) { GUILayout.Label("Your company has no van here yet.", label); return; }

            Vector3 pos = van.transform.position;
            var here = Town.LotAt(pos);
            GUILayout.Label("Work van - " + (van.Driving ? $"driving, {van.SpeedMph:0} mph" : here != null ? "parked at " + here.Address : "parked on the street"), header);
            Bar($"Cargo space: {VanCargo.UsedCuFt:0.#} / {VanCargo.CapacityCuFt:0} cu ft", VanCargo.UsedCuFt, VanCargo.CapacityCuFt, label);
            Bar($"Payload: {VanCargo.UsedLb:N0} / {VanCargo.PayloadLb:N0} lb", VanCargo.UsedLb, VanCargo.PayloadLb, label);
            GUILayout.Space(6);

            GUILayout.Label("Drive to (costs the drive time at 25 mph):", label);
            GUILayout.BeginHorizontal();
            Travel(t, "Home", Town.Home, button);
            Travel(t, "Hardware store", Town.Store, button);
            Travel(t, "Build site", Town.BuildSite, button);
            if (GUILayout.Button("HQ", button))
            {
                Vector3 hq = Town.HQPosition; var lot = Town.LotAt(hq);
                bool atParking = lot != null && Flat(hq, lot.Parking) < 1f;
                WorkVan.FastTravel(hq, atParking ? lot.ParkingYaw : van.Yaw, lot != null ? lot.Address + " (HQ)" : "HQ");
                t.ShowMessage("Drove to HQ.");
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // roof rack
            string rack = van.LadderOnRack ? "24' extension ladder" : TownLadder.Carried ? "(ladder in hand)" : "(ladder standing at the job)";
            foreach (var kv in VanCargo.RackEntries) rack += $", {kv.Value} x {Catalog.Get(kv.Key)?.Name ?? kv.Key}";
            GUILayout.Label($"Roof rack: {rack}   -   long stock {VanCargo.RackPieces}/{VanCargo.RackMaxPieces} pieces, {VanCargo.RackLb:N0}/{VanCargo.RackMaxLb:N0} lb", label);
            GUILayout.Space(6);

            bool site = AtBuildSite && !van.Driving;
            float nameW = fs * 20f, colW = fs * 5f, btnW = fs * 2.5f;
            GUILayout.BeginHorizontal();
            GUILayout.Label("In the van", header, GUILayout.Width(nameW));
            GUILayout.Label("Qty", header, GUILayout.Width(colW));
            GUILayout.Label("lb", header, GUILayout.Width(colW));
            GUILayout.Label("cu ft", header, GUILayout.Width(colW));
            if (site) GUILayout.Label("On site   (- unload, + load; Shift = 10)", header);
            GUILayout.EndHorizontal();

            var ids = new List<string>();
            foreach (var kv in VanCargo.Entries) ids.Add(kv.Key);
            if (site) foreach (var kv in Game.Stock.Entries) if (kv.Value > 0 && !ids.Contains(kv.Key)) ids.Add(kv.Key);
            if (ids.Count == 0) GUILayout.Label("The van is empty.", label);
            string move = null; int moveN = 0;
            foreach (var id in ids)
            {
                var d = Catalog.Get(id);
                int n = VanCargo.Count(id);
                var (lb, cu) = VanCargo.Spec(id);
                GUILayout.BeginHorizontal();
                GUILayout.Label(d?.Name ?? id, label, GUILayout.Width(nameW));
                GUILayout.Label(Qty(d, n), label, GUILayout.Width(colW));
                GUILayout.Label((lb * n).ToString("0.#"), label, GUILayout.Width(colW));
                GUILayout.Label(VanCargo.RidesOnRack(id) ? "rack" : (cu * n).ToString("0.##"), label, GUILayout.Width(colW));
                if (site)
                {
                    int step = (d != null ? Mathf.Max(1, d.UnitsPerPurchase) : 1) * (GInput.Shift ? 10 : 1);
                    GUILayout.Label(Qty(d, Game.Stock.Count(id)), label, GUILayout.Width(colW));
                    if (GUILayout.Button("-", button, GUILayout.Width(btnW))) { move = id; moveN = -Mathf.Min(step, n); }
                    if (GUILayout.Button("+", button, GUILayout.Width(btnW))) { move = id; moveN = Mathf.Min(step, Game.Stock.Count(id)); }
                }
                GUILayout.EndHorizontal();
            }
            if (move == null || moveN == 0) return;
            if (moveN < 0)
            {
                if (VanCargo.TryTake(move, -moveN)) Game.Stock.Add(move, -moveN);
            }
            else if (!VanCargo.CanAdd(move, moveN, out string why)) t.ShowMessage(why);
            else if (Game.Stock.TryTake(move, moveN)) VanCargo.Add(move, moveN);
        }

        static void DrawStore()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle; float fs = label.fontSize;
            if (!GameSettings.IsCareer) { GUILayout.Label("Hartley's Hardware is part of Career mode.", label); return; }
            GUILayout.Label("Hartley's Hardware & Lumber - 201 Main St", header);
            bool open = AtStore && !WorkVan.I.Driving;
            if (!open)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(WorkVan.I == null ? "You need your van to shop here." : "Drive to Hartley's (201 Main St) to shop.", label);
                GUILayout.FlexibleSpace();
                GUI.enabled = WorkVan.I != null;
                if (GUILayout.Button("Drive there", button, GUILayout.Width(fs * 9f)) && WorkVan.FastTravel(Town.Store)) t.ShowMessage("Parked at Hartley's.");
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            else GUILayout.Label($"Loads straight into the van: {VanCargo.UsedCuFt:0.#}/{VanCargo.CapacityCuFt:0} cu ft, {VanCargo.UsedLb:N0}/{VanCargo.PayloadLb:N0} lb.", label);
            GUILayout.Space(6);

            float nameW = fs * 22f, colW = fs * 6f, btnW = fs * 5f;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Item", header, GUILayout.Width(nameW));
            GUILayout.Label("Price", header, GUILayout.Width(colW));
            GUILayout.Label("Pack", header, GUILayout.Width(colW));
            GUILayout.Label("In van", header, GUILayout.Width(colW));
            GUILayout.EndHorizontal();

            string buy = null; int packs = 0, shown = 0;
            foreach (var id in VanCargo.RegisteredItems)
            {
                var d = Catalog.Get(id);
                if (d == null) continue;
                shown++;
                GUILayout.BeginHorizontal();
                GUILayout.Label(d.Name, label, GUILayout.Width(nameW));
                GUILayout.Label($"${d.Price}", label, GUILayout.Width(colW));
                GUILayout.Label(d.UnitsPerPurchase > 1 ? "x" + d.UnitsPerPurchase : "each", label, GUILayout.Width(colW));
                GUILayout.Label(Qty(d, VanCargo.Count(id)), label, GUILayout.Width(colW));
                GUI.enabled = open;
                if (GUILayout.Button("Buy 1", button, GUILayout.Width(btnW))) { buy = id; packs = 1; }
                if (GUILayout.Button("Buy 5", button, GUILayout.Width(btnW))) { buy = id; packs = 5; }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (shown == 0) GUILayout.Label("Nothing on the shelves yet.", label);
            if (buy != null && open) t.ShowMessage(Buy(buy, packs));
        }

        /// <summary>Buys packs of an item into the van. Returns the message for the customer.</summary>
        public static string Buy(string id, int packs)
        {
            var d = Catalog.Get(id);
            if (d == null || packs <= 0) return "Hartley's doesn't carry that.";
            int units = packs * Mathf.Max(1, d.UnitsPerPurchase), cost = packs * d.Price;
            if (!VanCargo.CanAdd(id, units, out string why)) return why;
            if (!Game.Wallet.TrySpend(cost, "Hartley's: " + d.Name)) return $"Not enough money: need ${cost:N0}, have ${Game.Wallet.Money:N0}.";
            VanCargo.Add(id, units);
            return $"Bought {packs} x {d.Name} (${cost:N0}) - loaded in the van.";
        }
    }
}
