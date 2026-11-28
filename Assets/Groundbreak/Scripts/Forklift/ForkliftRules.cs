using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>What the yard forklift (Yard equipment "forklift", 5,000 lb class) changes in the business:
    /// <list type="bullet">
    /// <item>Hand handling: a truck driver hand-unloads (or hand-loads) up to <see cref="DriverAllowanceLb"/> of a load
    /// within the run. Anything heavier received into or loaded out of the warehouse (lifts of lumber, sheet units,
    /// pallets of block/shingles/bags) is broken down and carried by your crew without a forklift: <see cref="HandHoursPerTon"/>
    /// labor-hours per ton at <see cref="LaborRatePerHour"/> (burdened laborer). With a forklift it is set down whole: no labor charge.
    /// Loose bulk (dump truck tips it) and trusses (the boom truck sets them) never need it.</item>
    /// <item>Lift pricing: with a forklift the supplier ships full units/pallets, priced <see cref="LiftDiscount"/> lower than
    /// a piece tally (on top of the yard's bulk discount) on stock bought for the warehouse.</item>
    /// <item>Warehouse runs (supplier → warehouse, warehouse → site) take <see cref="TripFactorWithForklift"/> of the time.</item>
    /// </list></summary>
    public static class ForkliftRules
    {
        public const string Id = "forklift";
        public const float DriverAllowanceLb = 2000f;
        public const float HandHoursPerTon = 1.5f;
        public const int LaborRatePerHour = 30;
        public const float LiftDiscount = 0.03f;
        public const float TripFactorWithForklift = 0.5f;
        public const string UnloadReason = "Warehouse labor: hand unloading";
        public const string LoadReason = "Warehouse labor: hand loading";

        public static bool Has => Yard.HasEquipment(Id);
        /// <summary>Extra discount on warehouse stock orders (0 without a forklift).</summary>
        public static float ExtraDiscount => Has ? LiftDiscount : 0f;
        /// <summary>Multiplier on the run time of trips that load or unload at the warehouse.</summary>
        public static float TripFactor => Has ? TripFactorWithForklift : 1f;
        public static int CostPerTon => Mathf.RoundToInt(HandHoursPerTon * LaborRatePerHour);

        /// <summary>Weight of a cart (purchase units) that has to be handled at the yard: everything but loose bulk and trusses.</summary>
        public static float HandledLb(IEnumerable<KeyValuePair<string, int>> cart)
        {
            float lb = 0f;
            if (cart == null) return 0f;
            foreach (var kv in cart)
                if (kv.Value > 0 && !Cargo.IsBulk(kv.Key) && !Cargo.IsTruss(kv.Key)) lb += Cargo.UnitWeightLb(kv.Key) * kv.Value;
            return lb;
        }

        /// <summary>Crew labor-hours to hand-handle this load at the yard without a forklift (0 with one or under the driver's allowance).</summary>
        public static float HandHours(IEnumerable<KeyValuePair<string, int>> cart, bool forklift)
        {
            if (forklift) return 0f;
            float over = HandledLb(cart) - DriverAllowanceLb;
            return over <= 0f ? 0f : over / 2000f * HandHoursPerTon;
        }

        public static int HandlingFee(IEnumerable<KeyValuePair<string, int>> cart, bool forklift)
        {
            float h = HandHours(cart, forklift);
            return h <= 0f ? 0 : Mathf.CeilToInt(h * LaborRatePerHour / 5f) * 5;
        }

        /// <summary>Hand-handling labor for this load with the yard as it is now.</summary>
        public static int HandlingFee(IEnumerable<KeyValuePair<string, int>> cart) => HandlingFee(cart, Has);

        // ------------------------------------------------------------------ player-facing lines

        /// <summary>The line shown where the forklift is sold (Warehouse app, shop equipment).</summary>
        public static string ShopLine =>
            $"Sets down lifts and pallets whole: no crew hand-unloading/loading on warehouse loads over {DriverAllowanceLb:N0} lb " +
            $"(saves ${CostPerTon}/ton), {LiftDiscount * 100:0}% lift pricing on warehouse stock, warehouse runs take half the time.";

        /// <summary>The line shown on a warehouse load (receiving or loading out).</summary>
        public static string LoadLine(IEnumerable<KeyValuePair<string, int>> cart, bool loadingOut)
        {
            float lb = HandledLb(cart);
            if (Has) return $"Forklift: {lb:N0} lb {(loadingOut ? "loaded" : "unloaded")} in minutes, no handling labor, run time halved.";
            int fee = HandlingFee(cart, false);
            if (fee <= 0) return $"No forklift: the driver hand-{(loadingOut ? "loads" : "unloads")} up to {DriverAllowanceLb:N0} lb (this load {lb:N0} lb).";
            return $"No forklift: hand-{(loadingOut ? "loading" : "unloading")} {lb:N0} lb takes {HandHours(cart, false):0.#} crew hours (${fee:N0}). A forklift does it in minutes.";
        }

        /// <summary>Days for the forklift to pay for itself at a given daily use of the yard: tons received and tons
        /// loaded out per day (counted over the driver allowance) and dollars of warehouse stock bought per day.</summary>
        public static float PaybackDays(float tonsInPerDay, float tonsOutPerDay, float stockDollarsPerDay)
        {
            float saved = (tonsInPerDay + tonsOutPerDay) * CostPerTon + stockDollarsPerDay * LiftDiscount;
            var e = Yard.EquipmentOf(Id);
            return saved <= 0f ? float.PositiveInfinity : (e != null ? e.Price : 28000) / saved;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var e = Yard.EquipmentOf(Id);
            if (e != null) e.Description = ShopLine;
        }
    }
}
