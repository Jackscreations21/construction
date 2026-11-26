using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A plain hand tool for service work. The work itself is driven by <see cref="RepairInteraction"/>.</summary>
    public class RepairHandTool : Tool
    {
        readonly string _id, _name, _hint;
        public RepairHandTool(string id, string name, string hint) { _id = id; _name = name; _hint = hint; }
        public override string Id => _id;
        public override string Name => _name;
        public override string Hint => _hint;
    }

    /// <summary>Repair parts and service tools (ids starting "rp-" are new; everything else reuses the trade catalogs).</summary>
    public static class RepairCatalog
    {
        // new tools
        public const string Pliers = "rp-pliers", Wrench = "rp-wrench", Screwdriver = "rp-screwdriver", Bucket = "rp-bucket", Plane = "rp-plane",
            FlatBar = "rp-flatbar", Scoop = "rp-scoop", Sledge = "rp-sledge", Tamper = "rp-tamper", MixHoe = "rp-mixhoe", MagFloat = "rp-magfloat";
        // existing tools (other features)
        public const string Tester = "voltage-tester", Meter = "multimeter", Knife = "utility-knife", JabSaw = "keyhole-saw", Knives = "taping-knives",
            Sander = "sanding-pole", Brush = "paint-brush", Roller = "paint-roller", Drill = "fin-drill", CaulkGun = "caulk-gun", Hammer = "hammer",
            PryBar = "prybar", Saw = "saw", Level = "level", Hose = "hose", Shovel = "shovel", Edger = "edger", Groover = "groover", Broom = "broom";

        // new materials
        public const string Trap112 = "rp-ptrap-112", Trap114 = "rp-ptrap-114", Supply = "rp-supply", MeshPatch = "rp-mesh-patch", Compound = "rp-compound-qt",
            PatchPanel = "rp-drywall-2x2", Furring = "rp-furring", Primer = "rp-primer-qt", Paint = "rp-paint-qt", Screws3 = "rp-screws-3in",
            DeckScrews = "rp-deck-screws", Deck8 = "rp-deckboard-8", Deck12 = "rp-deckboard-12", Deck16 = "rp-deckboard-16", Shingles = "rp-shingles-3tab",
            RoofCement = "rp-roof-cement", CaulkExt = "rp-caulk-ext", CaulkSilicone = "rp-caulk-silicone", Hanger = "rp-gutter-hanger",
            GravelBag = "rp-gravel-bag", Concrete80 = "rp-concrete-80";
        // existing materials
        public const string Recept = "el-dev-dup15", Gfci = "el-dev-gfci", Breaker15 = "el-bkr-15-1p", DwScrews = "dw-screws", DwTape = "dw-tape-paper",
            RoofNails = "roof-nails", Picket = "picket-cedar", Stud = "2x4x8", Stake = "formstake24";

        static PieceDefinition Con(string id, string name, int price, int units, Color c) => new PieceDefinition
        { Id = id, Name = name, Category = PieceCategory.Consumable, Price = price, UnitsPerPurchase = units, Color = c, Size = new Vector3(0.2f, 0.15f, 0.3f) };
        static PieceDefinition Lum(string id, string name, float w, float h, float lenIn, int price, Color c) => new PieceDefinition
        { Id = id, Name = name, Category = PieceCategory.Lumber, Size = new Vector3(Units.In(w), Units.In(h), Units.In(lenIn)), Price = price, Cuttable = true, Color = c };

        static void Item(PieceDefinition d, float lb, float cuFt)
        {
            if (Catalog.Get(d.Id) == null) Catalog.Register(d);   // ids owned by another feature keep that feature's definition
            VanCargo.RegisterItem(d.Id, lb, cuFt);
        }

        static void T(string id, string name, int price, string desc) =>
            ToolFactory.Register(new ToolShopItem { Id = id, Name = name, Price = price, Description = desc }, () => new RepairHandTool(id, name, desc));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Color white = new Color(0.93f, 0.93f, 0.9f), steel = new Color(0.7f, 0.72f, 0.74f), gray = new Color(0.6f, 0.6f, 0.58f),
                treated = new Color(0.55f, 0.62f, 0.42f), cedar = new Color(0.72f, 0.5f, 0.32f), black = new Color(0.12f, 0.12f, 0.13f);
            // per stock unit: weight (lb), volume (cu ft)
            Item(Con(Trap112, "P-trap kit 1-1/2\" PVC (kitchen)", 7, 1, white), 0.6f, 0.08f);
            Item(Con(Trap114, "P-trap kit 1-1/4\" PVC (lavatory)", 6, 1, white), 0.5f, 0.07f);
            Item(Con(Supply, "Braided faucet supply 3/8\" comp x 1/2\" FIP x 20\"", 8, 1, steel), 0.3f, 0.02f);
            Item(Con(MeshPatch, "Self-adhesive wall patch 6\" x 6\"", 5, 1, steel), 0.1f, 0.01f);
            Item(Con(Compound, "Lightweight joint compound, 1 qt", 9, 1, white), 2.5f, 0.04f);
            Item(Con(PatchPanel, "Drywall patch panel 1/2\" x 2' x 2'", 7, 1, white), 7f, 0.17f);
            Item(Lum(Furring, "1x3 furring strip x 8'", 2.5f, 0.75f, 96f, 3, new Color(0.86f, 0.72f, 0.5f)), 2.5f, 0.1f);
            Item(Con(Primer, "Stain-blocking primer, 1 qt", 13, 1, white), 2.8f, 0.04f);
            Item(Con(Paint, "Interior paint, colour-matched, 1 qt", 19, 1, white), 2.8f, 0.04f);
            Item(Con(Screws3, "#9 x 3\" construction screws (box of 50)", 9, 50, steel), 0.02f, 0.0004f);
            Item(Con(DeckScrews, "#9 x 2-1/2\" coated deck screws (1 lb, ~75)", 11, 75, new Color(0.45f, 0.4f, 0.3f)), 0.014f, 0.0003f);
            Item(Lum(Deck8, "5/4x6 PT deck board x 8'", 5.5f, 1f, 96f, 9, treated), 14f, 0.31f);
            Item(Lum(Deck12, "5/4x6 PT deck board x 12'", 5.5f, 1f, 144f, 14, treated), 21f, 0.46f);
            Item(Lum(Deck16, "5/4x6 PT deck board x 16'", 5.5f, 1f, 192f, 19, treated), 28f, 0.61f);
            Item(Con(Shingles, "3-tab shingles, bundle of 26 (33.3 sq ft)", 34, 26, black), 2.5f, 0.045f);
            Item(Con(RoofCement, "Roofing cement, 10 oz tube", 5, 1, black), 0.9f, 0.02f);
            Item(Con(CaulkExt, "Siliconized acrylic window & door caulk, 10 oz", 5, 1, white), 0.9f, 0.02f);
            Item(Con(CaulkSilicone, "100% silicone kitchen & bath sealant, 10 oz", 9, 1, white), 0.9f, 0.02f);
            Item(Con(Hanger, "Hidden gutter hanger 5\" with screw", 2, 1, steel), 0.2f, 0.02f);
            Item(Con(GravelBag, "All-purpose gravel, 0.5 cu ft bag", 6, 1, gray), 50f, 0.5f);
            Item(Con(Concrete80, "Concrete mix, 80 lb bag (0.6 cu ft)", 6, 1, gray), 80f, 0.6f);
            // owned by other features: fallback definitions only when theirs is not registered (they replace ours if they load later)
            Item(Con(Recept, "Duplex receptacle 15A TR, white", 2, 1, white), 0.2f, 0.01f);
            Item(Con(Gfci, "GFCI receptacle 20A TR/WR, white", 22, 1, white), 0.4f, 0.02f);
            Item(Con(Breaker15, "15A single-pole breaker", 9, 1, black), 0.3f, 0.01f);
            Item(Con(DwScrews, "Drywall screws 1-1/4\" coarse thread (5 lb, ~1000)", 32, 1000, black), 0.005f, 0.0001f);
            Item(Con(DwTape, "Paper joint tape, 500' roll", 6, 500, white), 0.002f, 0.0001f);
            Item(Con(RoofNails, "Roofing nails 1-1/4\" coil (7200)", 49, 7200, steel), 0.004f, 0.0001f);
            Item(Con(Picket, "Cedar dog-ear picket 5/8\"x5-1/2\"x6'", 4, 1, cedar), 3.5f, 0.14f);
            Item(Lum(Stud, "2x4 x 8'", 3.5f, 1.5f, 96f, 4, new Color(0.86f, 0.72f, 0.5f)), 9f, 0.29f);
            Item(Lum(Stake, "Wood form stake 2x2 x 24\"", 1.5f, 1.5f, 24f, 2, new Color(0.86f, 0.72f, 0.5f)), 1f, 0.03f);

            T(Pliers, "Tongue-and-Groove Pliers 10\"", 17, "Slip nuts and trap fittings: hand tight plus a quarter turn.");
            T(Wrench, "Adjustable Wrench 8\"", 14, "Supply-line and compression nuts.");
            T(Screwdriver, "6-in-1 Screwdriver", 9, "Cover plates, device screws, breaker lugs, probing for rot.");
            T(Bucket, "Bucket & Rags", 6, "Catches trap water; carries gutter muck.");
            T(Plane, "Block Plane", 32, "Planes a sticking door edge to the scribe line.");
            T(FlatBar, "Flat Bar (shingle bar)", 12, "Breaks shingle seals and pulls roofing nails; cat's paw end.");
            T(Scoop, "Gutter Scoop", 7, "Scoops leaves and shingle grit out of a K-style gutter.");
            T(Sledge, "Sledgehammer 10 lb", 42, "Breaks out a 4\" sidewalk slab.");
            T(Tamper, "Hand Tamper 8\" x 8\"", 38, "Compacts the gravel base under small flatwork.");
            T(MixHoe, "Mortar Hoe + Mixing Tub", 34, "Mixes bagged concrete a few bags at a time.");
            T(MagFloat, "Magnesium Hand Float 16\"", 24, "Floats small slabs after screeding.");

            foreach (var (id, n) in new[] { (CaulkExt, 2), (CaulkSilicone, 2), (Trap112, 1), (Trap114, 1), (Supply, 2), (Recept, 3), (Gfci, 1), (MeshPatch, 2),
                         (Compound, 1), (Primer, 1), (Paint, 1), (Screws3, 50), (DeckScrews, 75), (DwScrews, 100), (DwTape, 50), (RoofNails, 100) })
                VanCargo.AddStarter(id, n);
        }

        /// <summary>Shop name of a tool id ("a|b" alternatives show the first).</summary>
        public static string ToolName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "hands";
            string first = id.Split('|')[0];
            foreach (var i in ToolFactory.ShopItems) if (i.Id == first) { int p = i.Name.IndexOf(" ("); return p > 0 ? i.Name.Substring(0, p) : i.Name; }
            return first;
        }

        public static bool ToolMatches(string required, string inHand)
        {
            if (string.IsNullOrEmpty(required)) return true;
            if (inHand == null) return false;
            foreach (var t in required.Split('|')) if (t == inHand) return true;
            return false;
        }

        public static bool Owns(string toolId)
        {
            if (Game.Belt == null) return false;
            foreach (var alt in toolId.Split('|')) foreach (var t in Game.Belt.Owned) if (t != null && t.Id == alt) return true;
            return false;
        }

        public static string MatName(string id) { var d = Catalog.Get(id); return d != null ? d.Name : id; }
        /// <summary>Our cost for one stock unit (a box price divided by its count).</summary>
        public static float UnitCost(string id) { var d = Catalog.Get(id); return d == null ? 0f : d.Price / (float)Mathf.Max(1, d.UnitsPerPurchase); }

        /// <summary>Shortest stocked deck board that covers a length.</summary>
        public static string DeckBoardFor(float meters) => meters <= 8f * Units.Foot + 0.01f ? Deck8 : meters <= 12f * Units.Foot + 0.01f ? Deck12 : Deck16;
    }

    /// <summary>What a new handyman carries.</summary>
    public static class RepairKit
    {
        public static readonly string[] StarterTools =
        {
            RepairCatalog.Screwdriver, RepairCatalog.Pliers, RepairCatalog.Wrench, RepairCatalog.Knife, RepairCatalog.Tester, RepairCatalog.Drill,
            RepairCatalog.CaulkGun, RepairCatalog.Bucket, RepairCatalog.Knives, RepairCatalog.Brush,
        };

        /// <summary>Gives the basic service hand tools the player does not own yet (idempotent). Extra tools sit in the Tab tool list.</summary>
        public static void GrantStarterTools()
        {
            if (Game.Belt == null) return;
            foreach (var id in StarterTools)
            {
                if (RepairCatalog.Owns(id)) continue;
                var t = ToolFactory.Create(id);
                if (t != null) Game.Belt.AddTool(t);
            }
        }
    }
}
