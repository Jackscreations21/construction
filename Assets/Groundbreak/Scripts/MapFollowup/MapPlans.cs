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
            { "twostory", new[] { "twostory-2240", "twostory-2400" } },
            { "cape", new[] { "cottage-1200", "starter-1008" } },
            { "foursquare", new[] { "craftsman-1920", "twostory-2240" } },
            { "farmhouse", new[] { "twostory-2400", "estate-3500" } },
            { "duplex", new[] { "duplex-1664" } },
            { "townhomes", new[] { "townhome-row4", "townhome-row6" } },
            { "storefront", new[] { "storefront-24", "storefront-30", "retail-2400", "retail-5000" } },
            { "grocery", new[] { "grocery-20000", "retail-5000" } },
            { "office", new[] { "office-3000" } },
            { "bank", new[] { "bank-2800", "office-3000" } },
            { "postoffice", new[] { "retail-2400" } },
            { "diner", new[] { "diner-3000" } },
            { "fastfood", new[] { "diner-3000" } },
            { "gasstation", new[] { "gasstation-3200" } },
            { "stripmall", new[] { "stripmall-6000" } },
            { "autorepair", new[] { "flex-6000", "yard-shop-2400" } },
            { "warehouse", new[] { "warehouse-6000", "warehouse-12000", "warehouse-24000", "warehouse-50000", "flex-6000" } },
            { "metalshop", new[] { "workshop-30x40", "workshop-40x60", "yard-shop-2400", "warehouse-6000", "polebarn-60x80" } },
            { "contractoryard", new[] { "yard-shop-2400", "yard-hq-7200" } },
            { "lumberyard", new[] { "warehouse-6000", "warehouse-12000", "yard-shop-2400" } },
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

        /// <summary>A plan is on file only when the drawn building agrees with it: width and depth each within this fraction.</summary>
        public const float Tolerance = 0.15f;
        /// <summary>Most a map model is grown or shrunk (each way) to take a plan's real footprint; beyond that the building has no plan.</summary>
        public const float MaxResize = 1.8f;
        public static int Resized { get; private set; }
        public static int Dropped { get; private set; }

        static void AssignPlans()
        {
            Resized = 0; Dropped = 0;
            var done = new HashSet<TownLot>();
            foreach (var p in MapLayout.Parcels)
            {
                if (p.Spec == null || p.Lot == null) continue;
                if (!done.Add(p.Lot)) continue;    // farm outbuildings share the farmhouse's lot: the lot's plan is the main building's
                var id = Fit(p);
                p.Lot.PlanId = id;   // null when nothing fits (civic buildings, motel, self storage, narrow storefronts...)
                if (id != null) PlansAssigned++;
            }
            // the core town's customer houses are the same small ranch everywhere (HouseBuilder: 34'-38' x 28'): the plan that matches it
            string core = BestFor("ranch", 11f, HouseBuilder.D);
            foreach (var l in Town.CoreLots)
                if (l.Use == LotUse.Customer && string.IsNullOrEmpty(l.PlanId) && l != Town.Store && core != null)
                {
                    l.PlanId = core;
                    PlansAssigned++;
                }
        }

        static float Err(float w, float d, HousePlan pl) =>
            Mathf.Abs(Mathf.Log(Mathf.Max(1f, pl.WidthFt * Units.Foot) / Mathf.Max(1f, w))) + Mathf.Abs(Mathf.Log(Mathf.Max(1f, pl.DepthFt * Units.Foot) / Mathf.Max(1f, d)));

        /// <summary>True when a drawn footprint (m) agrees with a plan's (m) within the tolerance both ways.</summary>
        public static bool Agrees(float w, float d, float pw, float pd, float tol = Tolerance) =>
            Mathf.Abs(w - pw) <= tol * pw + 0.01f && Mathf.Abs(d - pd) <= tol * pd + 0.01f;

        /// <summary>A multi-story plan only goes on a building with that many stories (a one-story shop never gets the apartment-above plan).</summary>
        public static bool StoriesFit(HousePlan pl, int stories) => pl == null || stories <= 0 || pl.Floors <= 1 || pl.Floors == stories || (pl is Blueprint bp && bp.MezzanineFt > 0f);

        static List<HousePlan> Ranked(string type, float w, float d, int stories = 0)
        {
            var l = new List<HousePlan>();
            if (type == null || !Candidates.TryGetValue(type, out var ids)) return l;
            foreach (var id in ids) { var pl = HousePlans.Get(id); if (pl != null && StoriesFit(pl, type == "storefront" ? stories : 0)) l.Add(pl); }
            l.Sort((a, b) => Err(w, d, a).CompareTo(Err(w, d, b)));
            return l;
        }

        static string BestFor(string type, float w, float d)
        {
            foreach (var pl in Ranked(type, w, d)) if (Agrees(w, d, pl.WidthFt * Units.Foot, pl.DepthFt * Units.Foot)) return pl.Id;
            return null;
        }

        /// <summary>The plan for a map building, resizing the model to the plan's real footprint where the lot allows it (the model draws
        /// from the spec, so the building and the blueprint then agree). Null when no candidate plan fits the building.</summary>
        static string Fit(MapParcel p)
        {
            var s = p.Spec;
            var body = MapBodies.Body(s);
            var ranked = Ranked(s.Type, body.width, body.height, s.Stories);
            if (ranked.Count == 0) return null;
            foreach (var pl in ranked)
            {
                float pw = pl.WidthFt * Units.Foot, pd = pl.DepthFt * Units.Foot;
                if (TryResize(p, pl, pw, pd)) { Resized++; return pl.Id; }
                body = MapBodies.Body(s);
                if (Agrees(body.width, body.height, pw, pd)) return pl.Id;
            }
            Dropped++;
            return null;
        }

        static bool TryResize(MapParcel p, HousePlan pl, float pw, float pd)
        {
            var s = p.Spec; var lot = p.Lot;
            if (p.Ranch || s.Type == "ranch") return false;                 // HouseBuilder's enterable house has its own size
            if (MapBodies.HasSubBody(s.Type))
            {
                var before = s.PlanSize;
                s.PlanSize = new Vector2(pw, pd);
                var b = MapBodies.Body(s);
                if (Agrees(b.width, b.height, pw, pd, 0.02f)) { FixAccess(p); return true; }
                s.PlanSize = before;
                return false;
            }
            float rw = pw / Mathf.Max(0.1f, s.Width), rd = pd / Mathf.Max(0.1f, s.Depth);
            if (Mathf.Max(rw, 1f / rw) > MaxResize || Mathf.Max(rd, 1f / rd) > MaxResize) return false;
            if (s.Type == "storefront")
            {   // row building: its width is the lot (party walls both sides); only the depth can follow the plan
                if (!Agrees(s.Width, pd, pw, pd)) return false;
                if (p.Setback + pd > lot.Depth - 0.5f) return false;
                s.Depth = pd; s.BackYard = Mathf.Max(0f, lot.Depth - p.Setback - pd);
                return true;
            }
            // spec size whose drawn building (MapBodies.Body: walks, porticos, aprons, drive-thru lanes excluded) is the plan's footprint
            float ow = s.Width, od = s.Depth; var opl = s.PlanSize;
            if (s.Type == "fastfood") s.PlanSize = new Vector2(pw, pd);
            s.Width = pw; s.Depth = pd;
            for (int i = 0; i < 40; i++)
            {
                var bb = MapBodies.Body(s);
                float ew = pw - bb.width, ed = pd - bb.height;
                if (Mathf.Abs(ew) < 0.005f && Mathf.Abs(ed) < 0.005f) break;
                s.Width += ew; s.Depth += ed;
            }
            if (s.Type == "fastfood") s.Depth = Mathf.Max(s.Depth, pd + 5f);   // lane wraps behind the restaurant
            float nw = s.Width, nd = s.Depth;
            s.Width = ow; s.Depth = od;
            var check = MapBodies.Body(new MapBuildingSpec { Type = s.Type, Width = nw, Depth = nd, PlanSize = s.PlanSize });
            float margin = lot.Category == "residential" ? 1.5f : 1f;
            if (!Agrees(check.width, check.height, pw, pd, 0.03f) || Mathf.Max(nw / ow, ow / nw) > MaxResize || Mathf.Max(nd / od, od / nd) > MaxResize
                || Mathf.Abs(p.OffsetX) + nw / 2f > lot.Frontage / 2f - margin || p.Setback + nd > lot.Depth - 1f)
            { s.PlanSize = opl; return false; }
            pw = nw; pd = nd;
            s.Width = pw; s.Depth = pd; s.BackYard = Mathf.Max(0f, lot.Depth - p.Setback - pd);
            if (s.Type == "townhomes") s.Units = pl.Id.EndsWith("6") ? 6 : 4;
            if (s.Type == "stripmall") s.Units = 4;
            FixAccess(p);
            return true;
        }

        /// <summary>Parking spot after a resize (same rules as the layout).</summary>
        static void FixAccess(MapParcel p)
        {
            var s = p.Spec; var lot = p.Lot;
            switch (p.Site)
            {
                case MapSite.ParkingLot:
                case MapSite.Gravel:
                case MapSite.Farmyard:
                    lot.Parking = lot.Local(Mathf.Clamp(p.OffsetX + s.Width / 2f + 4f, -lot.Frontage / 2f + 3f, lot.Frontage / 2f - 3f), Mathf.Max(4f, p.Setback * 0.5f));
                    break;
            }
            if (s.Type == "gasstation") lot.Parking = lot.Local(p.OffsetX, p.Setback + Mathf.Max(24f, s.Depth) - MapBodies.GasStore(s).y - 9.5f);
        }

        /// <summary>The plan whose footprint is closest to the building's (width and depth); null when the type has none.</summary>
        public static string PickFor(MapBuildingSpec s)
        {
            if (s == null) return null;
            var b = MapBodies.Body(s);
            var r = Ranked(s.Type, b.width, b.height, s.Stories);
            return r.Count > 0 ? r[0].Id : null;
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
