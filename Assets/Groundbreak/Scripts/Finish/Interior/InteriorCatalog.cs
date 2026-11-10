using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Interior finish materials (trim stock, flooring, tile, cabinets, countertops, fasteners) + loose-fastener
    /// bookkeeping (open a box, use nails one at a time).</summary>
    public static class InteriorSupplies
    {
        public const string Trade = "Interior finish";
        public static readonly int[] CabinetWidths = { 12, 15, 18, 21, 24, 27, 30, 33, 36 };
        static readonly Dictionary<string, int> _loose = new Dictionary<string, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _loose.Clear();

        /// <summary>Uses n fasteners/consumable units from an opened box, opening a new box (1 stock unit) when it runs out.</summary>
        public static bool UseFasteners(string id, int n)
        {
            if (FinishStock.Unlimited || n <= 0) return true;
            var d = Catalog.Get(id);
            int per = d != null ? Mathf.Max(1, d.UnitsPerPurchase) : 1;
            _loose.TryGetValue(id, out int left);
            while (left < n)
            {
                if (!FinishStock.Take(id)) { _loose[id] = left; return false; }
                left += per;
            }
            _loose[id] = left - n;
            return true;
        }

        /// <summary>Uses a quantity of an area/length consumable (e.g. 34 sq ft of underlayment from 100 sq ft rolls).</summary>
        public static bool UseAmount(string id, float amount, float perUnit)
        {
            if (FinishStock.Unlimited || amount <= 0f) return true;
            int units = Mathf.CeilToInt(amount / perUnit * 100f);   // track in 1/100 of a unit
            _loose.TryGetValue(id + "#", out int left);
            int need = units;
            int boxes = 0;
            while (left < need) { left += 100; boxes++; }
            if (boxes > 0 && !FinishStock.Has(id, boxes)) { FinishStock.Take(id, boxes); return false; }
            if (boxes > 0) FinishStock.Take(id, boxes);
            _loose[id + "#"] = left - need;
            return true;
        }

        static Color Wood => new Color(0.75f, 0.6f, 0.42f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            var defs = new List<PieceDefinition>();
            var ids = new List<string>();
            void Add(PieceDefinition d) { defs.Add(d); ids.Add(d.Id); }

            // trim stock by the stick
            foreach (var p in TrimProfile.All)
                foreach (TrimSpecies s in new[] { TrimSpecies.Primed, TrimSpecies.Oak })
                    foreach (var ft in TrimStock.Feet(s))
                        Add(FinishStock.Item(TrimStock.StockId(p, s, ft), $"{p.Name}, {(s == TrimSpecies.Oak ? "red oak" : "primed")} {ft}'",
                            Mathf.CeilToInt(p.PricePerFoot * ft * (s == TrimSpecies.Oak ? 2.4f : 1f)), 1, s == TrimSpecies.Oak ? Wood : Color.white,
                            Units.ToInches(p.Thickness), Units.ToInches(p.Width), ft * 12f));

            Add(FinishStock.Item("fin-shims", "Cedar shims (bundle of 42)", 5, 1, new Color(0.8f, 0.6f, 0.45f)));
            Add(FinishStock.Item("fin-nails-15ga", "15 ga angled finish nails 2-1/2\" (box of 1000)", 22, 1000, Color.gray));
            Add(FinishStock.Item("fin-brads-18ga", "18 ga brad nails 1-1/4\" (box of 1000)", 9, 1000, Color.gray));
            Add(FinishStock.Item("fin-caulk-painters", "Painter's caulk, white (10 oz tube)", 4, 1, Color.white));
            // flooring
            Add(FinishStock.Item("fin-lvp-oak", "LVP click plank 7\"x48\" natural oak (box of 10, 23.3 sq ft)", 62, 10, new Color(0.75f, 0.6f, 0.45f)));
            Add(FinishStock.Item("fin-lvp-gray", "LVP click plank 7\"x48\" weathered gray (box of 10, 23.3 sq ft)", 62, 10, new Color(0.6f, 0.58f, 0.55f)));
            Add(FinishStock.Item("fin-lvp-underlay", "LVP foam underlayment 2 mm (100 sq ft roll)", 35, 1, new Color(0.7f, 0.8f, 0.9f)));
            Add(FinishStock.Item("fin-hw-redoak", "Solid red oak strip 3/4\" x 2-1/4\", random lengths (bundle ≈ 20 sq ft)", 95, 1, Wood));
            Add(FinishStock.Item("fin-rosin", "Red rosin paper 36\" (500 sq ft roll)", 16, 1, new Color(0.7f, 0.35f, 0.3f)));
            Add(FinishStock.Item("fin-felt15", "15 lb felt underlayment (432 sq ft roll)", 25, 1, new Color(0.2f, 0.2f, 0.2f)));
            Add(FinishStock.Item("fin-cleats", "2\" flooring cleats (box of 1000)", 36, 1000, Color.gray));
            Add(FinishStock.Item("fin-tile-1224", "Porcelain tile 12\"x24\" (box of 8, 16 sq ft)", 44, 8, new Color(0.8f, 0.78f, 0.74f)));
            Add(FinishStock.Item("fin-tile-1212", "Porcelain tile 12\"x12\" (box of 15)", 32, 15, new Color(0.8f, 0.78f, 0.74f)));
            Add(FinishStock.Item("fin-cement-board", "1/4\" cement backer board 3'x5'", 13, 1, new Color(0.6f, 0.6f, 0.58f)));
            Add(FinishStock.Item("fin-backer-screws", "1-1/4\" backer board screws (box of 200)", 11, 200, Color.gray));
            Add(FinishStock.Item("fin-thinset", "Modified thinset mortar 50 lb (≈45 sq ft @ 1/4x3/8 notch)", 26, 1, new Color(0.62f, 0.62f, 0.6f)));
            Add(FinishStock.Item("fin-grout", "Sanded grout 25 lb (color at mixing)", 22, 1, new Color(0.62f, 0.62f, 0.6f)));
            Add(FinishStock.Item("fin-spacers", "Tile spacers 1/8\" (bag of 250)", 5, 250, Color.white));
            Add(FinishStock.Item("fin-tile-subway", "Subway tile 3\"x6\" glossy white (box of 80, 10 sq ft)", 26, 80, Color.white));
            Add(FinishStock.Item("fin-mastic", "Premixed tile mastic 1 gal (backsplash, ≈ 30 sq ft)", 18, 1, Color.white));
            // cabinets
            foreach (var w in CabinetWidths)
            {
                Add(FinishStock.Item($"fin-cab-b{w}", $"Base cabinet {w}\" (34-1/2\" H x 24\" D)", 140 + 7 * w, 1, Wood, w, 34.5f, 24f));
                Add(FinishStock.Item($"fin-cab-w{w}", $"Wall cabinet {w}\" (30\" H x 12\" D)", 100 + 5 * w, 1, Wood, w, 30f, 12f));
            }
            Add(FinishStock.Item("fin-cab-filler", "Cabinet filler strip 3\" x 96\"", 24, 1, Wood));
            Add(FinishStock.Item("fin-cab-screws", "Cabinet screws 2-1/2\" (box of 100)", 12, 100, Color.gray));
            Add(FinishStock.Item("fin-ct-laminate", "Laminate countertop, per linear ft (25-1/2\", built-up edge)", 25, 1, new Color(0.8f, 0.76f, 0.7f)));
            Add(FinishStock.Item("fin-ct-butcher", "Butcher block countertop, per linear ft (1-1/2\" maple)", 45, 1, Wood));
            Add(FinishStock.Item("fin-ct-granite", "Granite countertop 3 cm, per linear ft (fabricated)", 70, 1, Color.gray));
            Add(FinishStock.Item("fin-ct-quartz", "Quartz countertop 3 cm, per linear ft (fabricated)", 85, 1, Color.white));
            Add(FinishStock.Item("fin-sink-ss", "Stainless undermount sink 30\" single bowl", 189, 1, Color.gray));

            Catalog.Register(defs.ToArray());
            FinishSupply.Add(Trade, ids.ToArray());
            Tablet.AddHelp("Interior finish: hang prehung doors (finish nailer), case/base/crown with the miter saw (cope inside corners with the coping saw), nail off, caulk.");
            Tablet.AddHelp("Floors: LVP (pull bar), hardwood (flooring nailer), tile (notched trowel → tile saw → wait 24 h → grout float). Cabinets/countertops/backsplash with the drill.");
        }
    }
}
