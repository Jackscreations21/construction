using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    [Serializable]
    public class VehicleInfo { public string uid; public bool rack, used; public int paid, boughtDay; }

    [Serializable]
    public class Trip
    {
        public string vehicleUid, label;
        public List<string> ids = new List<string>(); public List<int> qty = new List<int>();
        public bool toYard;
        public float secondsLeft;
    }

    /// <summary>The company's vehicles beyond what Company stores: purchase/sale, racks, service, running costs and
    /// hauling materials (supplier → site or warehouse, warehouse → site) within each truck's real cargo limits.</summary>
    public static class Fleet
    {
        public const float FuelPrice = 3.65f;          // $/gal
        public const float SupplierRoundTripMiles = 12f;
        public const int DeliveryFee = 79;             // what the supplier charges to deliver (see Tablet store hook)
        public const float UsedCondition = 0.72f;
        /// <summary>Real seconds for a supplier/warehouse run. Tests set this to 0.</summary>
        public static float TripSeconds = 45f;

        public static readonly List<VehicleInfo> Infos = new List<VehicleInfo>();
        public static readonly List<Trip> Trips = new List<Trip>();
        public static event Action Changed;

        static int Today => GameClock.I != null ? GameClock.I.Day : 1;
        static void Notify() { Yard.MarkDirty(); Changed?.Invoke(); }

        // ------------------------------------------------------------------ queries

        public static VehicleInfo InfoOf(string uid)
        {
            var i = Infos.Find(x => x.uid == uid);
            if (i == null) { i = new VehicleInfo { uid = uid, used = true, boughtDay = Today }; Infos.Add(i); }   // e.g. the starter van
            return i;
        }

        public static bool HasRack(string uid) { var i = Infos.Find(x => x.uid == uid); return i != null && i.rack; }
        public static void Forget(string uid) { Infos.RemoveAll(x => x.uid == uid); Trips.RemoveAll(t => t.vehicleUid == uid); }
        public static bool IsBusy(string uid) => Trips.Exists(t => t.vehicleUid == uid);
        public static OwnedVehicle Find(string uid) => Company.Vehicles.Find(v => v.Uid == uid);

        public static int Price(string typeId, bool used) { var t = FleetCatalog.Get(typeId); return t == null ? 0 : used ? t.PriceUsed : t.PriceNew; }
        public static int DownPayment(string typeId, bool used) => Mathf.CeilToInt(Price(typeId, used) * (used ? 0.20f : 0.10f) / 100f) * 100;

        public static int ResaleValue(OwnedVehicle v)
        {
            var t = FleetCatalog.Get(v.TypeId); if (t == null) return 0;
            var i = InfoOf(v.Uid);
            float baseValue = i.used ? t.PriceUsed * 0.9f : t.PriceNew * 0.78f;
            float age = Mathf.Pow(0.9985f, Mathf.Max(0, Today - i.boughtDay));
            return Mathf.RoundToInt(baseValue * age * Mathf.Lerp(0.55f, 1f, v.Condition) / 50f) * 50 + (i.rack ? t.RackPrice / 3 : 0);
        }

        // ------------------------------------------------------------------ dealership

        /// <summary>Buys a vehicle. financeTermDays = 0 pays cash; otherwise a down payment now and an auto loan for the rest.</summary>
        public static OwnedVehicle Buy(string typeId, bool used, int financeTermDays, out string msg)
        {
            var t = FleetCatalog.Get(typeId);
            if (t == null) { msg = "Unknown vehicle."; return null; }
            int price = Price(typeId, used);
            int upfront = financeTermDays > 0 ? DownPayment(typeId, used) : price;
            if (!Game.Wallet.TrySpend(upfront, (financeTermDays > 0 ? "Down payment: " : "Vehicle: ") + t.Name))
            { msg = $"Need ${upfront:N0} {(financeTermDays > 0 ? "down" : "cash")}; you have ${Game.Wallet.Money:N0}."; return null; }

            int n = 1; foreach (var o in Company.Vehicles) if (o.TypeId == typeId) n++;
            var v = Company.AddVehicle(typeId, n > 1 ? $"{t.Name} #{n}" : t.Name);
            v.Condition = used ? UsedCondition : 1f;
            Infos.RemoveAll(x => x.uid == v.Uid);
            Infos.Add(new VehicleInfo { uid = v.Uid, used = used, paid = price, boughtDay = Today });
            if (financeTermDays > 0)
            {
                var l = Finance.AddVehicleLoan(v.Uid, v.Name, price - upfront, used, financeTermDays);
                msg = $"Bought {(used ? "used" : "new")} {t.Name}: ${upfront:N0} down, ${l.Payment:N0}/day for {financeTermDays} days.";
            }
            else msg = $"Bought {(used ? "used" : "new")} {t.Name} for ${price:N0}.";
            Game.Toast(msg); Company.NotifyChanged(); Notify();
            return v;
        }

        /// <summary>Sells a vehicle; an auto loan on it is paid off from the proceeds first.</summary>
        public static bool Sell(OwnedVehicle v, out string msg)
        {
            if (v == null || !Company.Vehicles.Contains(v)) { msg = "Not in the fleet."; return false; }
            if (IsBusy(v.Uid)) { msg = "That truck is out on a run."; return false; }
            int value = ResaleValue(v);
            var loan = Finance.LoanOn(v.Uid);
            int owed = loan != null ? Mathf.CeilToInt(loan.Balance) : 0;
            if (owed > value + Game.Wallet.Money) { msg = $"You owe ${owed:N0} on it and it sells for ${value:N0}; you can't cover the difference."; return false; }
            Game.Wallet.Add(value, "Sold vehicle: " + v.Name);
            if (loan != null) { Game.Wallet.TrySpend(owed, "Loan payoff: " + loan.Label); Finance.Loans.Remove(loan); }
            Company.Vehicles.Remove(v); Forget(v.Uid);
            msg = $"Sold {v.Name} for ${value:N0}" + (owed > 0 ? $" (${owed:N0} went to the lender)." : ".");
            Game.Toast(msg); Company.NotifyChanged(); Notify();
            return true;
        }

        public static bool AddRack(OwnedVehicle v, out string msg)
        {
            var t = FleetCatalog.Get(v.TypeId);
            if (t == null || t.RackMaxItemLength <= 0) { msg = "No rack is made for this vehicle."; return false; }
            var i = InfoOf(v.Uid);
            if (i.rack) { msg = "Already has a rack."; return false; }
            if (!Game.Wallet.TrySpend(t.RackPrice, "Rack: " + v.Name)) { msg = $"The rack is ${t.RackPrice:N0}."; return false; }
            i.rack = true;
            msg = $"Rack installed: {v.Name} now carries stock up to {Units.Format(t.RackMaxItemLength)}.";
            Game.Toast(msg); Notify();
            return true;
        }

        public static int ServiceCost(OwnedVehicle v)
        {
            var t = FleetCatalog.Get(v.TypeId); if (t == null) return 0;
            float cost = (1f - v.Condition) * t.PriceNew * 0.08f;
            if (Yard.HasEquipment("lift")) cost *= 0.7f;      // own service bay: parts only
            return Mathf.CeilToInt(cost / 10f) * 10;
        }

        public static bool Service(OwnedVehicle v, out string msg)
        {
            int cost = ServiceCost(v);
            if (cost <= 0) { msg = "Nothing to service."; return false; }
            if (!Game.Wallet.TrySpend(cost, "Service: " + v.Name)) { msg = $"Service costs ${cost:N0}."; return false; }
            v.Condition = 1f;
            msg = $"{v.Name} serviced for ${cost:N0}."; Game.Toast(msg); Notify();
            return true;
        }

        // ------------------------------------------------------------------ daily running costs

        static int InsurancePerDay()
        {
            int s = 0;
            foreach (var v in Company.Vehicles) { var t = FleetCatalog.Get(v.TypeId); if (t != null) s += t.InsurancePerDay; }
            return s;
        }

        static int MaintenancePerDay()
        {
            float s = 0;
            foreach (var v in Company.Vehicles)
            {
                var t = FleetCatalog.Get(v.TypeId); if (t == null) continue;
                s += t.MaintenancePerDay * Mathf.Lerp(1.8f, 1f, v.Condition);       // worn trucks cost more to keep running
            }
            if (Yard.HasEquipment("lift")) s *= 0.7f;
            return Mathf.RoundToInt(s);
        }

        static int FuelPerDay()
        {
            bool working = Company.ActiveJobs.Count > 0;
            float gallons = 0;
            foreach (var v in Company.Vehicles)
            {
                var t = FleetCatalog.Get(v.TypeId); if (t == null) continue;
                if (t.Machine) gallons += working ? 3f : 0f;                        // skid steer burns ~1.5 gal/h when it works
                else if (t.Mpg > 0) gallons += (working ? 30f : 8f) / t.Mpg;
            }
            float cost = gallons * FuelPrice;
            if (Yard.HasEquipment("fueltank")) cost *= 0.88f;                       // bulk fuel delivered to the yard
            return Mathf.RoundToInt(cost);
        }

        public static void OnDay(int day)
        {
            foreach (var v in Company.Vehicles) v.Condition = Mathf.Max(0.2f, v.Condition - 0.004f);
        }

        // ------------------------------------------------------------------ hauling

        static int TripFuel(VehicleType t) => t.Mpg > 0 ? Mathf.CeilToInt(SupplierRoundTripMiles / t.Mpg * FuelPrice) : 0;

        public static int MaterialCost(IEnumerable<KeyValuePair<string, int>> cart, bool toYard)
        {
            float s = 0;
            foreach (var kv in cart) if (kv.Value > 0) s += Cargo.PriceOf(kv.Key) * kv.Value;
            if (toYard) s *= 1f - Yard.Discount;
            return Mathf.CeilToInt(s);
        }

        /// <summary>Send one of your trucks to the supplier: pay for the materials (no delivery fee), the truck brings them
        /// to the job site or to the warehouse racking. Fails with the cargo problems if the load doesn't fit.</summary>
        public static bool Haul(OwnedVehicle v, Dictionary<string, int> cart, bool toYard, out string msg)
        {
            if (!CheckRun(v, cart, toYard, out var t, out var report, out msg)) return false;
            int cost = MaterialCost(cart, toYard), fuel = TripFuel(t);
            if (Game.Wallet.Money < cost + fuel) { msg = $"Materials ${cost:N0} + fuel ${fuel:N0}; you have ${Game.Wallet.Money:N0}."; return false; }
            Game.Wallet.TrySpend(cost, toYard ? "Materials (warehouse stock)" : "Materials (picked up)");
            if (fuel > 0) Game.Wallet.TrySpend(fuel, "Fuel: supplier run");
            Start(v, cart, toYard, toYard ? "Supplier → warehouse" : "Supplier → job site");
            msg = $"{v.Name} is on a supplier run ({report.WeightLb:N0} lb). Materials ${cost:N0}, fuel ${fuel:N0}, no delivery fee.";
            Game.Toast(msg);
            return true;
        }

        /// <summary>Load a truck from the warehouse racking and take it to the job site.</summary>
        public static bool HaulFromYard(OwnedVehicle v, Dictionary<string, int> cart, out string msg)
        {
            if (!Company.HasWarehouse) { msg = "You have no warehouse."; return false; }
            if (!CheckRun(v, cart, false, out var t, out var report, out msg)) return false;
            foreach (var kv in cart)
                if (kv.Value > 0 && Yard.Stock.Count(kv.Key) < kv.Value * Cargo.UnitsPerPurchase(kv.Key))
                { msg = $"Not enough {Cargo.NameOf(kv.Key)} in the warehouse."; return false; }
            int fuel = TripFuel(t);
            if (fuel > 0 && !Game.Wallet.TrySpend(fuel, "Fuel: warehouse run")) { msg = $"Fuel for the run is ${fuel:N0}."; return false; }
            foreach (var kv in cart) if (kv.Value > 0) Yard.Stock.TryTake(kv.Key, kv.Value * Cargo.UnitsPerPurchase(kv.Key));
            Start(v, cart, false, "Warehouse → job site");
            msg = $"{v.Name} loaded from the warehouse ({report.WeightLb:N0} lb), on its way to the site.";
            Game.Toast(msg);
            return true;
        }

        static bool CheckRun(OwnedVehicle v, Dictionary<string, int> cart, bool toYard, out VehicleType t, out CargoReport report, out string msg)
        {
            t = v != null ? FleetCatalog.Get(v.TypeId) : null; report = null;
            if (v == null || t == null || !Company.Vehicles.Contains(v)) { msg = "Pick a vehicle from your fleet."; return false; }
            if (IsBusy(v.Uid)) { msg = $"{v.Name} is already out on a run."; return false; }
            bool any = false; foreach (var kv in cart) if (kv.Value > 0) any = true;
            if (!any) { msg = "Nothing to load."; return false; }
            report = Cargo.Check(t, HasRack(v.Uid), cart);
            if (!report.Fits) { msg = string.Join(" ", report.Problems); return false; }
            if (toYard)
            {
                if (!Company.HasWarehouse) { msg = "You have no warehouse to stock."; return false; }
                if (report.VolumeCuFt > Yard.FreeCuFt) { msg = $"The warehouse has {Yard.FreeCuFt:N0} cu ft free; this load is {report.VolumeCuFt:N0}."; return false; }
            }
            msg = null; return true;
        }

        static void Start(OwnedVehicle v, Dictionary<string, int> cart, bool toYard, string label)
        {
            var trip = new Trip { vehicleUid = v.Uid, toYard = toYard, label = label, secondsLeft = TripSeconds };
            foreach (var kv in cart) if (kv.Value > 0) { trip.ids.Add(kv.Key); trip.qty.Add(kv.Value); }
            v.Condition = Mathf.Max(0.2f, v.Condition - 0.001f);
            Trips.Add(trip);
            if (TripSeconds <= 0f) Tick(0f);
            Notify();
        }

        /// <summary>Advances runs; called every frame by FleetRuntime.</summary>
        public static void Tick(float dt)
        {
            for (int i = Trips.Count - 1; i >= 0; i--)
            {
                var t = Trips[i];
                t.secondsLeft -= dt;
                if (t.secondsLeft > 0f) continue;
                Trips.RemoveAt(i);
                var stock = t.toYard ? Yard.Stock : Game.Stock;
                for (int k = 0; k < t.ids.Count; k++) stock.Add(t.ids[k], t.qty[k] * Cargo.UnitsPerPurchase(t.ids[k]));
                var v = Find(t.vehicleUid);
                Game.Toast($"{(v != null ? v.Name : "Truck")} unloaded: {t.label}");
                Notify();
            }
        }

        // ------------------------------------------------------------------ state

        public static void ResetState() { Infos.Clear(); Trips.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { ResetState(); Changed = null; TripSeconds = 45f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Company.RegisterDailyCost("Vehicle insurance", InsurancePerDay);
            Company.RegisterDailyCost("Vehicle maintenance", MaintenancePerDay);
            Company.RegisterDailyCost("Fuel", FuelPerDay);
        }
    }
}
