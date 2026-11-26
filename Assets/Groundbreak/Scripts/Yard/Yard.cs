using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public class YardLevelInfo
    {
        public int Level; public string Name, Description;
        public int MoveIn, RentPerDay, InsurancePerDay, StorageCuFt, Stalls; public float Discount;
    }

    public class EquipmentInfo { public string Id, Name, Description; public int Price, MinLevel; }

    /// <summary>The company's premises: leased level (bay → yard + shop → HQ), stored materials (bulk-bought at a
    /// discount, kept between jobs), shop equipment, and its daily costs. The physical place is built by YardSite.</summary>
    public static class Yard
    {
        public static readonly YardLevelInfo[] Levels =
        {
            null,
            new YardLevelInfo { Level = 1, Name = "Rented bay", RentPerDay = 340, InsurancePerDay = 35, MoveIn = 4500, StorageCuFt = 1800, Stalls = 3, Discount = 0.08f,
                Description = "30' x 50' bay in a flex building: 12x14 overhead door, small office, pallet racking, workbench. 3 parking stalls." },
            new YardLevelInfo { Level = 2, Name = "Yard + shop", RentPerDay = 950, InsurancePerDay = 70, MoveIn = 15000, StorageCuFt = 7000, Stalls = 8, Discount = 0.12f,
                Description = "Fenced 150' x 200' gravel yard with a 40' x 60' steel shop, lumber racks and aggregate bunkers. 8 truck stalls." },
            new YardLevelInfo { Level = 3, Name = "Headquarters", RentPerDay = 2400, InsurancePerDay = 140, MoveIn = 48000, StorageCuFt = 24000, Stalls = 14, Discount = 0.15f,
                Description = "60' x 120' warehouse with a two-storey office, loading dock and 4 doors on a 250' x 300' yard. 14+ stalls." },
        };

        public static readonly List<EquipmentInfo> Equipment = new List<EquipmentInfo>
        {
            new EquipmentInfo { Id = "sawstation", Name = "Saw station", Price = 2200, MinLevel = 1, Description = "Miter saw bench + cabinet table saw: cut lists at the shop." },
            new EquipmentInfo { Id = "compressor", Name = "Air compressor 60 gal", Price = 1600, MinLevel = 1, Description = "Runs pneumatic nailers and blows out lines." },
            new EquipmentInfo { Id = "forklift", Name = "Forklift 5,000 lb", Price = 28000, MinLevel = 1, Description = "Loads lifts of lumber and pallets: warehouse runs take half the time." },
            new EquipmentInfo { Id = "fueltank", Name = "Fuel tank 500 gal", Price = 4500, MinLevel = 2, Description = "Bulk diesel delivered to the yard: fuel costs 12% less." },
            new EquipmentInfo { Id = "lift", Name = "Two-post vehicle lift", Price = 9000, MinLevel = 2, Description = "Service the fleet in-house: maintenance and repairs cost 30% less." },
        };

        public static Vector3 Position = TownHook.FallbackHQ;
        public static readonly Stockpile Stock = new Stockpile();
        public static readonly List<string> OwnedEquipment = new List<string>();
        public static event Action Changed;
        public static bool Dirty;

        public static YardLevelInfo Current => Company.WarehouseLevel > 0 && Company.WarehouseLevel < Levels.Length ? Levels[Company.WarehouseLevel] : null;
        public static float Discount => Current != null ? Current.Discount : 0f;
        public static int CapacityCuFt => Current != null ? Current.StorageCuFt : 0;
        public static float FreeCuFt => Mathf.Max(0f, CapacityCuFt - StoredCuFt);
        public static bool HasEquipment(string id) => OwnedEquipment.Contains(id);
        public static EquipmentInfo EquipmentOf(string id) => Equipment.Find(e => e.Id == id);

        public static void MarkDirty() { Dirty = true; Changed?.Invoke(); }

        public static float StoredCuFt
        {
            get
            {
                float s = 0;
                foreach (var kv in Stock.Entries) if (kv.Value > 0) s += Cargo.UnitVolumeCuFt(kv.Key) * kv.Value / Cargo.UnitsPerPurchase(kv.Key);
                return s;
            }
        }

        public static int StockValue
        {
            get
            {
                float s = 0;
                foreach (var kv in Stock.Entries) if (kv.Value > 0) s += Cargo.PriceOf(kv.Key) * (float)kv.Value / Cargo.UnitsPerPurchase(kv.Key);
                return Mathf.RoundToInt(s);
            }
        }

        // ------------------------------------------------------------------ lease

        /// <summary>Leases the given level (move-in = deposit + first rent + fit-out). Moving up keeps the stock and equipment.</summary>
        public static bool Lease(int level, out string msg)
        {
            if (level < 1 || level >= Levels.Length) { msg = "No such premises."; return false; }
            if (level == Company.WarehouseLevel) { msg = "You already lease this."; return false; }
            var info = Levels[level];
            if (level < Company.WarehouseLevel && StoredCuFt > info.StorageCuFt) { msg = $"Too much stock to fit in a {info.Name.ToLower()}."; return false; }
            if (!Game.Wallet.TrySpend(info.MoveIn, $"Warehouse move-in: {info.Name}")) { msg = $"Move-in costs ${info.MoveIn:N0}; you have ${Game.Wallet.Money:N0}."; return false; }
            int before = Company.WarehouseLevel;
            Company.HasWarehouse = true;
            Company.WarehouseLevel = level;
            Position = TownHook.HQPosition;
            Company.RecomputeStage();
            msg = before == 0 ? $"Leased: {info.Name}. Rent ${info.RentPerDay:N0}/day." : $"Moved to: {info.Name}. Rent ${info.RentPerDay:N0}/day.";
            Game.Toast(msg);
            YardSite.Rebuild();
            MarkDirty();
            return true;
        }

        /// <summary>Ends the lease. Stock is sold to a salvage buyer at 50%, equipment at 40%. Returns the cash raised.</summary>
        public static int EndLease(List<string> report = null)
        {
            int cash = 0;
            foreach (var kv in new List<KeyValuePair<string, int>>(Stock.Entries))
                if (kv.Value > 0) cash += Mathf.RoundToInt(Cargo.PriceOf(kv.Key) * 0.5f * kv.Value / Cargo.UnitsPerPurchase(kv.Key));
            if (cash > 0) report?.Add($"Warehouse stock sold: ${cash:N0}");
            int eq = 0;
            foreach (var id in OwnedEquipment) { var e = EquipmentOf(id); if (e != null) eq += e.Price * 4 / 10; }
            if (eq > 0) report?.Add($"Shop equipment sold: ${eq:N0}");
            if (Company.HasWarehouse) report?.Add("Lease terminated: " + (Current != null ? Current.Name : "warehouse"));
            ClearStock(); OwnedEquipment.Clear();
            Company.HasWarehouse = false; Company.WarehouseLevel = 0;
            YardSite.Rebuild();
            MarkDirty();
            return cash + eq;
        }

        /// <summary>Bankruptcy: everything at the premises is auctioned and the lease ends.</summary>
        public static int Liquidate(List<string> report) => Company.HasWarehouse || OwnedEquipment.Count > 0 ? EndLease(report) : 0;

        public static bool BuyEquipment(string id, out string msg)
        {
            var e = EquipmentOf(id);
            if (e == null) { msg = "Unknown equipment."; return false; }
            if (HasEquipment(id)) { msg = "Already owned."; return false; }
            if (Company.WarehouseLevel < e.MinLevel) { msg = $"Needs {Levels[e.MinLevel].Name.ToLower()} or bigger."; return false; }
            if (!Game.Wallet.TrySpend(e.Price, "Shop equipment: " + e.Name)) { msg = $"{e.Name} costs ${e.Price:N0}."; return false; }
            OwnedEquipment.Add(id);
            msg = $"Installed at the shop: {e.Name}."; Game.Toast(msg); MarkDirty();
            return true;
        }

        static void ClearStock()
        {
            foreach (var kv in new List<KeyValuePair<string, int>>(Stock.Entries)) if (kv.Value > 0) Stock.TryTake(kv.Key, kv.Value);
        }

        /// <summary>Supplier delivers a bulk order straight to the warehouse (bulk discount, flat delivery fee).</summary>
        public static bool OrderDelivered(Dictionary<string, int> cart, out string msg)
        {
            if (!Company.HasWarehouse) { msg = "You have no warehouse."; return false; }
            var r = Cargo.Measure(cart);
            if (r.VolumeCuFt <= 0) { msg = "Nothing to order."; return false; }
            if (r.VolumeCuFt > FreeCuFt) { msg = $"The warehouse has {FreeCuFt:N0} cu ft free; this order is {r.VolumeCuFt:N0}."; return false; }
            int cost = Fleet.MaterialCost(cart, true) + Fleet.DeliveryFee;
            if (!Game.Wallet.TrySpend(cost, "Bulk order (delivered to warehouse)")) { msg = $"Order is ${cost:N0} incl. ${Fleet.DeliveryFee} delivery."; return false; }
            foreach (var kv in cart) if (kv.Value > 0) Stock.Add(kv.Key, kv.Value * Cargo.UnitsPerPurchase(kv.Key));
            msg = $"Bulk order delivered to the warehouse: ${cost:N0}."; Game.Toast(msg); MarkDirty();
            return true;
        }

        /// <summary>Puts leftover site stock back on the warehouse racking (counted in stock units, as in Game.Stock).</summary>
        public static int ReturnFromSite(string id, int units)
        {
            units = Mathf.Min(units, Game.Stock.Count(id));
            if (units <= 0 || !Company.HasWarehouse) return 0;
            float cuFt = Cargo.UnitVolumeCuFt(id) * units / Cargo.UnitsPerPurchase(id);
            if (cuFt > FreeCuFt) return 0;
            Game.Stock.TryTake(id, units); Stock.Add(id, units); MarkDirty();
            return units;
        }

        // ------------------------------------------------------------------ daily costs

        static int Rent() => Current != null ? Current.RentPerDay : 0;
        static int Insurance() => Current != null ? Current.InsurancePerDay : 0;
        static int Utilities() => Current == null ? 0 : 15 * Company.WarehouseLevel + (HasEquipment("compressor") ? 4 : 0) + (HasEquipment("lift") ? 3 : 0);

        // ------------------------------------------------------------------ state

        public static void ResetState() { ClearStock(); OwnedEquipment.Clear(); Position = TownHook.HQPosition; Dirty = true; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { OwnedEquipment.Clear(); Changed = null; Dirty = true; Position = TownHook.FallbackHQ; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Company.RegisterDailyCost("Warehouse rent", Rent);
            Company.RegisterDailyCost("Business insurance", Insurance);
            Company.RegisterDailyCost("Warehouse utilities", Utilities);
        }
    }
}
