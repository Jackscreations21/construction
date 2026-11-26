using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Groundbreak
{
    public class CargoReport
    {
        public float WeightLb, VolumeCuFt, LongestM;
        public bool HasBulk, HasTrusses;
        public readonly List<string> Problems = new List<string>();
        public bool Fits => Problems.Count == 0;
        public string Summary(VehicleType t) =>
            $"{WeightLb:N0} / {t.PayloadLb:N0} lb  ·  {VolumeCuFt:N0} / {t.VolumeCuFt:N0} cu ft  ·  longest {Units.Format(LongestM)}";
    }

    /// <summary>What fits on a vehicle: weight, volume and length of catalog items (quantities are purchase units,
    /// e.g. boxes of nails, cubic yards of gravel).</summary>
    public static class Cargo
    {
        const float CuFtPerM3 = 35.3147f;
        static readonly Regex LbInName = new Regex(@"(\d+(?:\.\d+)?)\s*lb", RegexOptions.IgnoreCase);

        // Loose bulk materials sold by the cubic yard: lb per yard.
        static readonly Dictionary<string, float> BulkLbPerYard = new Dictionary<string, float>
        {
            { "fill-dirt", 2200 }, { "topsoil", 2100 }, { "crusher-run", 2800 }, { "gravel-57", 2600 }, { "paver-sand", 2700 }, { "mulch", 700 },
        };

        public static string NameOf(string id)
        {
            var d = Catalog.Get(id); if (d != null) return d.Name;
            var l = LandCatalog.Get(id); return l != null ? l.Name : id;
        }

        public static int PriceOf(string id)
        {
            var d = Catalog.Get(id); if (d != null) return d.Price;
            var l = LandCatalog.Get(id); return l != null ? l.Price : 0;
        }

        /// <summary>Stock units per purchase unit (a box of nails is 1000 units of stock).</summary>
        public static int UnitsPerPurchase(string id)
        {
            var d = Catalog.Get(id);
            if (d != null) return d.Category == PieceCategory.Consumable ? Mathf.Max(1, d.UnitsPerPurchase) : 1;
            var l = LandCatalog.Get(id); return l != null ? Mathf.Max(1, l.Units) : 1;
        }

        public static bool IsBulk(string id)
        {
            if (BulkLbPerYard.ContainsKey(id)) return true;
            return NameOf(id).Contains("(1 cu yd)");
        }

        public static bool IsTruss(string id) => id.Contains("truss");

        public static float UnitLengthM(string id)
        {
            if (IsBulk(id)) return 0f;
            var d = Catalog.Get(id);
            if (d == null || d.Size == Vector3.zero) return IsTruss(id) ? 24 * Units.Foot : 0.5f;
            return Mathf.Max(d.Size.x, Mathf.Max(d.Size.y, d.Size.z));
        }

        public static float UnitVolumeCuFt(string id)
        {
            if (IsBulk(id)) return 27f;
            var d = Catalog.Get(id);
            if (d == null || d.Size == Vector3.zero) return 0.4f;
            float v = d.Size.x * d.Size.y * d.Size.z * CuFtPerM3;
            if (IsTruss(id)) v = Mathf.Min(v, UnitLengthM(id) / Units.Foot * 0.6f);   // trusses nest flat in a bundle
            return Mathf.Max(0.01f, v);
        }

        public static float UnitWeightLb(string id)
        {
            if (BulkLbPerYard.TryGetValue(id, out float perYard)) return perYard;
            if (IsBulk(id)) return 2400f;
            var d = Catalog.Get(id);
            string name = NameOf(id);
            var m = LbInName.Match(name);
            if (m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lb)) return lb;
            if (IsTruss(id)) return UnitLengthM(id) / Units.Foot * 4.2f;             // ~100 lb for a 24' fink truss
            if (d == null || d.Size == Vector3.zero) return 25f;
            float cuFt = d.Size.x * d.Size.y * d.Size.z * CuFtPerM3;
            switch (d.Category)
            {
                case PieceCategory.Lumber: return cuFt * (name.StartsWith("PT") || name.Contains("treated") ? 40f : 28f);
                case PieceCategory.Sheet: return cuFt * 38f;
                case PieceCategory.Masonry: return cuFt * (name.Contains("block") || name.Contains("CMU") ? 65f : 120f);
                case PieceCategory.Rebar: return cuFt * 490f * 0.785f;
                case PieceCategory.Consumable: return 25f;
                case PieceCategory.Door: case PieceCategory.Window: return cuFt * 22f;
                case PieceCategory.GarageDoor: return cuFt * 12f;
                default: return cuFt * 9f;
            }
        }

        public static CargoReport Measure(IEnumerable<KeyValuePair<string, int>> items)
        {
            var r = new CargoReport();
            if (items == null) return r;
            foreach (var kv in items)
            {
                if (kv.Value <= 0) continue;
                r.WeightLb += UnitWeightLb(kv.Key) * kv.Value;
                r.VolumeCuFt += UnitVolumeCuFt(kv.Key) * kv.Value;
                r.LongestM = Mathf.Max(r.LongestM, UnitLengthM(kv.Key));
                if (IsBulk(kv.Key)) r.HasBulk = true;
                if (IsTruss(kv.Key)) r.HasTrusses = true;
            }
            return r;
        }

        /// <summary>Checks a load against one vehicle. Problems are worded for the player.</summary>
        public static CargoReport Check(VehicleType t, bool rack, IEnumerable<KeyValuePair<string, int>> items)
        {
            var r = Measure(items);
            if (t == null) { r.Problems.Add("No vehicle."); return r; }
            if (t.Machine) { r.Problems.Add($"The {t.Name.ToLower()} carries no cargo."); return r; }
            float maxLen = rack && t.RackMaxItemLength > 0 ? t.RackMaxItemLength : t.MaxItemLength;
            if (r.WeightLb > t.PayloadLb + 0.5f) r.Problems.Add($"Overweight: {r.WeightLb:N0} lb load, {t.PayloadLb:N0} lb payload.");
            if (r.VolumeCuFt > t.VolumeCuFt + 0.05f) r.Problems.Add($"Won't fit: {r.VolumeCuFt:N0} cu ft load, {t.VolumeCuFt:N0} cu ft of space.");
            if (r.LongestM > maxLen + 0.01f)
                r.Problems.Add($"Too long: {Units.Format(r.LongestM)} stock, this truck takes {Units.Format(maxLen)}" +
                               (!rack && t.RackMaxItemLength > maxLen ? " (add a rack)." : " (use a flatbed or boom truck)."));
            if (r.HasBulk && !t.Bulk) r.Problems.Add("Loose gravel, sand and soil need the dump truck.");
            if (r.HasTrusses && !t.Crane) r.Problems.Add("Roof trusses need the boom truck.");
            return r;
        }

        public static bool Fits(string typeId, bool rack, IEnumerable<KeyValuePair<string, int>> items) =>
            Check(FleetCatalog.Get(typeId), rack, items).Fits;

        /// <summary>The cheapest vehicle type that can carry the load in one trip (no rack assumed), or null.</summary>
        public static string SmallestVehicleFor(IEnumerable<KeyValuePair<string, int>> items)
        {
            VehicleType best = null;
            foreach (var t in FleetCatalog.All)
                if (!t.Machine && Check(t, false, items).Fits && (best == null || t.PriceNew < best.PriceNew)) best = t;
            return best?.Id;
        }
    }
}
