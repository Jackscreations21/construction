using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Ties the full map to the blueprint library: after the map layout exists (<see cref="MapLayout.AfterLayout"/>) every map building whose
    /// type has a matching plan carries that plan's id (<see cref="TownLot.PlanId"/>, picked by footprint size among the plans of its kind),
    /// and part of the one-storey houses on residential streets all over town become enterable ranches (so service calls reach the whole map).
    /// </summary>
    public static class MapPlans
    {
        /// <summary>Plan ids each map building type may be drawn from; the one closest to the building's footprint is used.</summary>
        public static readonly Dictionary<string, string[]> Candidates = new Dictionary<string, string[]>
        {
            { "ranch", new[] { "ranch-1500", "ranch-1600", "ranch-1650-1car", "starter-1008" } },
            { "ranch2", new[] { "ranch-1650-2car", "ranch-1770-2car", "ranch-1770-3car", "ranch-1600", "ranch-1500" } },
            { "twostory", new[] { "twostory-2240", "twostory-2400", "splitlevel-2280" } },
            { "cape", new[] { "cottage-1200", "starter-1008" } },
            { "foursquare", new[] { "craftsman-1920", "twostory-2240" } },
            { "farmhouse", new[] { "twostory-2400", "estate-3500" } },
            { "duplex", new[] { "duplex-1664" } },
            { "townhomes", new[] { "townhome-row4", "townhome-row6" } },
            { "storefront", new[] { "retail-2400", "retail-5000" } },
            { "grocery", new[] { "retail-5000" } },
            { "office", new[] { "office-3000" } },
            { "bank", new[] { "office-3000" } },
            { "postoffice", new[] { "retail-2400" } },
            { "diner", new[] { "diner-3000" } },
            { "fastfood", new[] { "diner-3000" } },
            { "gasstation", new[] { "gasstation-3200" } },
            { "stripmall", new[] { "stripmall-6000" } },
            { "autorepair", new[] { "flex-6000", "yard-shop-2400" } },
            { "warehouse", new[] { "warehouse-6000", "warehouse-12000", "warehouse-24000", "warehouse-50000", "flex-6000" } },
            { "metalshop", new[] { "workshop-30x40", "workshop-40x60", "yard-shop-2400" } },
            { "contractoryard", new[] { "yard-shop-2400", "yard-hq-7200" } },
            { "lumberyard", new[] { "warehouse-12000", "warehouse-24000" } },
            { "barn", new[] { "barn-36x48" } },
            { "machineshed", new[] { "polebarn-40x60", "polebarn-60x80", "polebarn-30x40" } },
            { "garage", new[] { "garage-2car", "garage-2car-shop", "garage-3car" } },
        };

        /// <summary>Most map one-storey houses that become enterable ranches (performance cap: each is a full interior).</summary>
        public static int MaxNewRanches = 24;
        public static int RanchesAdded { get; private set; }
        public static int PlansAssigned { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            MapLayout.AfterLayout -= OnLayout; MapLayout.AfterLayout += OnLayout;
            // keep the map's own default table pointing at real plans (other code may read it)
            foreach (var kv in Candidates) MapLayout.PlanByType[kv.Key] = kv.Value[0];
        }

        static void OnLayout()
        {
            RanchesAdded = 0; PlansAssigned = 0;
            try { AddRanches(); } catch (System.Exception e) { Debug.LogException(e); }
            try { AssignPlans(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>Every other one-storey ("ranch2") house on a residential street with a standard lot becomes an enterable ranch.</summary>
        static void AddRanches()
        {
            int n = 0;
            foreach (var p in MapLayout.Parcels)
            {
                if (RanchesAdded >= MaxNewRanches) break;
                if (p.Spec == null || p.Ranch || p.Spec.Type != "ranch2" || p.Road == null || p.Road.Class != RoadClass.Residential) continue;
                var lot = p.Lot;
                if (lot.Frontage < Town.LotFrontage - 0.05f || lot.Depth < Town.LotDepth - 0.05f) continue;
                if (n++ % 2 != 0) continue;
                p.Ranch = true;
                p.Spec.Type = "ranch"; p.Spec.Width = 11f; p.Spec.Depth = 8.5f;
                p.Setback = TownHouse.Setback; p.OffsetX = 0f;
                lot.TypeId = "ranch";
                lot.ServiceCalls = true;
                float dx = Town.LotFrontage / 2f - 1f - Town.DrivewayWidth / 2f;
                lot.Parking = lot.Local(dx, 6.5f);
                lot.ParkingYaw = Quaternion.LookRotation(-lot.Front).eulerAngles.y;
                lot.FrontDoor = lot.Local(TownHouse.FrontDoorX, TownHouse.Setback - 1.2f);
                RanchesAdded++;
            }
        }

        static void AssignPlans()
        {
            foreach (var p in MapLayout.Parcels)
            {
                if (p.Spec == null || p.Lot == null) continue;
                var id = PickFor(p.Spec);
                p.Lot.PlanId = id;   // null when nothing fits (civic buildings, motel, self storage...)
                if (id != null) PlansAssigned++;
            }
            // the core town's customer houses are the same ranch everywhere: on file as the 1,500 / 1,600 sq ft ranch
            foreach (var l in Town.CoreLots)
                if (l.Use == LotUse.Customer && string.IsNullOrEmpty(l.PlanId) && l != Town.Store)
                {
                    l.PlanId = HousePlans.Get("ranch-1500") != null && l.Number % 4 == 1 ? "ranch-1500" : "ranch-1600";
                    PlansAssigned++;
                }
        }

        /// <summary>The plan whose footprint is closest to the building's (square feet, then shape); null when the type has none.</summary>
        public static string PickFor(MapBuildingSpec s)
        {
            if (s == null || s.Type == null || !Candidates.TryGetValue(s.Type, out var ids)) return null;
            float ft2 = 1f / (Units.Foot * Units.Foot);
            float area = s.Width * s.Depth * ft2 * Mathf.Max(1, s.Stories);
            string best = null; float bestErr = float.MaxValue;
            foreach (var id in ids)
            {
                var plan = HousePlans.Get(id);
                if (plan == null) continue;
                float err = Mathf.Abs(Mathf.Log(Mathf.Max(100f, plan.SqFt) / Mathf.Max(100f, area)));
                if (s.Type == "townhomes") err = Mathf.Abs((plan.Id.EndsWith("6") ? 6 : 4) - s.Units);
                if (err < bestErr) { bestErr = err; best = id; }
            }
            return best;
        }

        /// <summary>The map parcel of a lot (building spec, origin, rotation), or null.</summary>
        public static MapParcel ParcelOf(TownLot lot)
        {
            if (lot == null) return null;
            foreach (var p in MapLayout.Parcels) if (p.Lot == lot && p.Spec != null) return p;
            return null;
        }

        /// <summary>Map buildings that carry a plan id that resolves.</summary>
        public static IEnumerable<TownLot> LotsWithPlans()
        {
            foreach (var l in Town.Lots) if (!string.IsNullOrEmpty(l.PlanId) && HousePlans.Get(l.PlanId) != null) yield return l;
        }
    }
}
