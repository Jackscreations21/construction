using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Warehouse" tablet app: lease/upgrade premises, shop equipment, stored stock, and material runs
    /// (supplier → site / warehouse with your own truck, warehouse → site, or supplier delivery to the warehouse).</summary>
    public static class WarehouseApp
    {
        static readonly Dictionary<string, int> _cart = new Dictionary<string, int>();
        static int _vehicle, _group, _dest;      // _dest: 0 job site, 1 warehouse, 2 load from warehouse → site
        static string _msg = "";
        static readonly string[] Groups = { "Lumber", "Sheet goods", "Concrete & masonry", "Bulk (cu yd)", "Fence & hardscape", "Doors & windows", "Other" };
        static readonly string[] Dests = { "Supplier → job site", "Supplier → warehouse (bulk discount)", "Warehouse → job site" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Tablet.RegisterApp("Warehouse", Draw);
            Tablet.AddHelp("Warehouse app (tablet) - lease a bay/yard/HQ, buy shop equipment, stock up at a bulk discount, send your trucks on material runs");
        }

        static int GroupOf(string id)
        {
            if (Cargo.IsBulk(id)) return 3;
            var d = Catalog.Get(id);
            if (d == null) return LandCatalog.Get(id) != null ? 4 : 6;
            if (LandCatalog.Get(id) != null) return 4;
            switch (d.Category)
            {
                case PieceCategory.Lumber: return 0;
                case PieceCategory.Sheet: return 1;
                case PieceCategory.Masonry: case PieceCategory.Rebar: return 2;
                case PieceCategory.Consumable: return d.Name.Contains("concrete") || d.Name.Contains("mortar") ? 2 : 6;
                case PieceCategory.Door: case PieceCategory.Window: case PieceCategory.GarageDoor: return 5;
                default: return 6;
            }
        }

        static void Draw()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle;
            float fs = label.fontSize;

            // ---- premises
            var cur = Yard.Current;
            GUILayout.Label(cur != null ? $"{cur.Name}: rent ${cur.RentPerDay:N0}/day · storage {Yard.StoredCuFt:N0}/{cur.StorageCuFt:N0} cu ft · bulk discount {cur.Discount * 100:0}%" : "No warehouse yet. Lease one to store materials, park trucks and become a Contractor.", header);
            for (int lv = 1; lv < Yard.Levels.Length; lv++)
            {
                var info = Yard.Levels[lv];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{info.Name}: {info.Description} Rent ${info.RentPerDay:N0}/day + insurance ${info.InsurancePerDay}/day.", label, GUILayout.Width(fs * 34));
                GUI.enabled = lv != Company.WarehouseLevel;
                if (GUILayout.Button(lv == Company.WarehouseLevel ? "Current" : $"Lease (${info.MoveIn:N0} move-in)", button, GUILayout.Width(fs * 11))) Yard.Lease(lv, out _msg);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if (YardSite.I != null && GUILayout.Button("Go to the warehouse", button, GUILayout.Width(fs * 11))) { YardFleetRuntime.GoToYard(out _msg); t.Close(); }
            if (Company.HasWarehouse && GUILayout.Button("End lease (sell stock 50%)", button, GUILayout.Width(fs * 13))) { int c = Yard.EndLease(); if (c > 0) Game.Wallet.Add(c, "Sold warehouse stock & equipment"); _msg = $"Lease ended, raised ${c:N0}."; }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_msg)) GUILayout.Label(_msg, header);

            if (Company.HasWarehouse)
            {
                GUILayout.Space(6);
                GUILayout.Label("Shop equipment", header);
                foreach (var e in Yard.Equipment)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{e.Name}: {e.Description}", label, GUILayout.Width(fs * 34));
                    GUI.enabled = !Yard.HasEquipment(e.Id) && Company.WarehouseLevel >= e.MinLevel;
                    if (GUILayout.Button(Yard.HasEquipment(e.Id) ? "Owned" : Company.WarehouseLevel < e.MinLevel ? "Needs yard" : $"Buy ${e.Price:N0}", button, GUILayout.Width(fs * 8))) Yard.BuyEquipment(e.Id, out _msg);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(6);
                GUILayout.Label($"Stored materials (value ${Yard.StockValue:N0})", header);
                bool any = false;
                foreach (var kv in new List<KeyValuePair<string, int>>(Yard.Stock.Entries))
                {
                    if (kv.Value <= 0) continue; any = true;
                    GUILayout.Label($"  {Cargo.NameOf(kv.Key)}: {kv.Value / Cargo.UnitsPerPurchase(kv.Key)}", label);
                }
                if (!any) GUILayout.Label("  (empty)", label);
                bool siteAny = false;
                foreach (var kv in Game.Stock.Entries) if (kv.Value > 0) siteAny = true;
                if (siteAny && GUILayout.Button("Return leftover site materials to the warehouse", button, GUILayout.Width(fs * 20)))
                {
                    int moved = 0;
                    foreach (var kv in new List<KeyValuePair<string, int>>(Game.Stock.Entries)) moved += Yard.ReturnFromSite(kv.Key, kv.Value) > 0 ? 1 : 0;
                    _msg = moved > 0 ? $"Moved {moved} material(s) back to the racking." : "Nothing fit in the warehouse.";
                }
            }

            // ---- material run
            GUILayout.Space(10);
            GUILayout.Label("Material run", header);
            _dest = GUILayout.SelectionGrid(_dest, Dests, 3, button);
            var trucks = Company.Vehicles.FindAll(v => { var vt = FleetCatalog.Get(v.TypeId); return vt != null && !vt.Machine; });
            if (trucks.Count == 0) { GUILayout.Label("You need a vehicle (Dealership).", label); return; }
            _vehicle = Mathf.Clamp(_vehicle, 0, trucks.Count - 1);
            var names = new string[trucks.Count];
            for (int i = 0; i < trucks.Count; i++) names[i] = trucks[i].Name + (Fleet.IsBusy(trucks[i].Uid) ? " (out)" : "");
            _vehicle = GUILayout.SelectionGrid(_vehicle, names, Mathf.Min(4, names.Length), button);
            var veh = trucks[_vehicle];
            var type = FleetCatalog.Get(veh.TypeId);

            if (_dest == 2)
            {
                foreach (var kv in new List<KeyValuePair<string, int>>(Yard.Stock.Entries))
                    if (kv.Value > 0) CartRow(kv.Key, kv.Value / Cargo.UnitsPerPurchase(kv.Key), label, button, fs);
            }
            else
            {
                _group = GUILayout.SelectionGrid(_group, Groups, 4, button);
                var ids = new List<string>();
                foreach (var d in Catalog.All) if (GroupOf(d.Id) == _group && Cargo.PriceOf(d.Id) > 0) ids.Add(d.Id);
                foreach (var li in LandCatalog.Items) if (!ids.Contains(li.Id) && GroupOf(li.Id) == _group) ids.Add(li.Id);
                foreach (var id in ids) CartRow(id, -1, label, button, fs);
            }

            var report = Cargo.Check(type, Fleet.HasRack(veh.Uid), _cart);
            GUILayout.Label($"{veh.Name}: {report.Summary(type)}", header);
            var prev = GUI.color;
            if (!report.Fits) { GUI.color = new Color(1f, 0.55f, 0.5f); foreach (var pr in report.Problems) GUILayout.Label("  " + pr, label); GUI.color = prev; }
            int cost = _dest == 2 ? 0 : Fleet.MaterialCost(_cart, _dest == 1);
            GUILayout.BeginHorizontal();
            GUI.enabled = report.Fits && report.WeightLb > 0;
            string go = _dest == 0 ? $"Pick up (${cost:N0}, no delivery fee)" : _dest == 1 ? $"Haul to warehouse (${cost:N0})" : "Load & drive to site";
            if (GUILayout.Button(go, button, GUILayout.Width(fs * 14)))
            {
                bool ok = _dest == 2 ? Fleet.HaulFromYard(veh, new Dictionary<string, int>(_cart), out _msg) : Fleet.Haul(veh, new Dictionary<string, int>(_cart), _dest == 1, out _msg);
                if (ok) _cart.Clear();
            }
            GUI.enabled = _dest == 1 && Company.HasWarehouse && _cart.Count > 0;
            if (GUILayout.Button($"Supplier delivers to warehouse (+${Fleet.DeliveryFee})", button, GUILayout.Width(fs * 15)))
            { if (Yard.OrderDelivered(new Dictionary<string, int>(_cart), out _msg)) _cart.Clear(); }
            GUI.enabled = true;
            if (GUILayout.Button("Clear", button, GUILayout.Width(fs * 4))) _cart.Clear();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            foreach (var trip in Fleet.Trips) GUILayout.Label($"  On the road: {Fleet.Find(trip.vehicleUid)?.Name} · {trip.label} · {Mathf.CeilToInt(trip.secondsLeft)} s", label);
        }

        static void CartRow(string id, int max, GUIStyle label, GUIStyle button, float fs)
        {
            int q = _cart.TryGetValue(id, out int n) ? n : 0;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Cargo.NameOf(id), label, GUILayout.Width(fs * 18));
            GUILayout.Label(max >= 0 ? $"in stock {max}" : $"${Cargo.PriceOf(id):N0}", label, GUILayout.Width(fs * 6));
            if (GUILayout.Button("-", button, GUILayout.Width(fs * 2))) q = Mathf.Max(0, q - 1);
            GUILayout.Label(q.ToString(), label, GUILayout.Width(fs * 3));
            if (GUILayout.Button("+", button, GUILayout.Width(fs * 2))) q++;
            if (GUILayout.Button("+10", button, GUILayout.Width(fs * 3))) q += 10;
            if (max >= 0) q = Mathf.Min(q, max);
            GUILayout.EndHorizontal();
            if (q > 0) _cart[id] = q; else _cart.Remove(id);
        }
    }
}
