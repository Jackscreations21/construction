using UnityEngine;

namespace Groundbreak
{
    /// <summary>Milestone 3 materials: framing lumber, engineered wood, connectors, sheathing and roofing.
    /// Registered through Catalog.Register (no edits to the core catalog).</summary>
    public static class ShellCatalog
    {
        static readonly Color Pine = new Color(0.86f, 0.72f, 0.50f);
        static readonly Color Treated = new Color(0.55f, 0.62f, 0.42f);
        static readonly Color Osb = new Color(0.78f, 0.62f, 0.38f);
        static readonly Color Galv = new Color(0.72f, 0.74f, 0.76f);

        static PieceDefinition L(string id, string name, float w, float h, float lenIn, int price, Color c) =>
            new PieceDefinition { Id = id, Name = name, Category = PieceCategory.Lumber, Size = new Vector3(Units.In(w), Units.In(h), Units.In(lenIn)), Price = price, Cuttable = true, Color = c };
        static PieceDefinition S(string id, string name, float thick, int price, Color c) =>
            new PieceDefinition { Id = id, Name = name, Category = PieceCategory.Sheet, Size = new Vector3(Units.In(48), Units.In(thick), Units.In(96)), Price = price, Cuttable = true, Color = c };
        static PieceDefinition C(string id, string name, int price, int units, Color c) =>
            new PieceDefinition { Id = id, Name = name, Category = PieceCategory.Consumable, Price = price, UnitsPerPurchase = units, Color = c };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterAll()
        {
            Catalog.Register(
                // dimensional lumber (actual sizes)
                L("2x4x12", "2x4 x 12'", 3.5f, 1.5f, 144, 8, Pine),
                L("2x4x16", "2x4 x 16'", 3.5f, 1.5f, 192, 11, Pine),
                L("2x6x10", "2x6 x 10'", 5.5f, 1.5f, 120, 9, Pine),
                L("2x6x16", "2x6 x 16'", 5.5f, 1.5f, 192, 15, Pine),
                L("2x8x12", "2x8 x 12'", 7.25f, 1.5f, 144, 14, Pine),
                L("2x8x16", "2x8 x 16'", 7.25f, 1.5f, 192, 19, Pine),
                L("2x10x12", "2x10 x 12'", 9.25f, 1.5f, 144, 19, Pine),
                L("2x10x16", "2x10 x 16'", 9.25f, 1.5f, 192, 26, Pine),
                L("2x12x12", "2x12 x 12'", 11.25f, 1.5f, 144, 24, Pine),
                L("2x12x16", "2x12 x 16'", 11.25f, 1.5f, 192, 33, Pine),
                L("1x6x16", "1x6 primed fascia x 16'", 5.5f, 0.75f, 192, 18, new Color(0.93f, 0.93f, 0.91f)),
                L("1x8x8", "1x8 riser board x 8'", 7.25f, 0.75f, 96, 9, Pine),
                L("pt2x6x16", "PT 2x6 x 16' (sill plate)", 5.5f, 1.5f, 192, 21, Treated),
                L("pt2x12x12", "PT 2x12 x 12' (stringer stock)", 11.25f, 1.5f, 144, 34, Treated),
                L("pt4x4x10", "PT 4x4 post x 10'", 3.5f, 3.5f, 120, 17, Treated),
                // engineered wood
                L("ijoist-9", "I-joist 9-1/2\" x 16'", 9.5f, 2.5f, 192, 42, Pine),
                L("lvl-9", "LVL beam 1-3/4 x 9-1/4 x 16'", 9.25f, 1.75f, 192, 89, new Color(0.88f, 0.70f, 0.48f)),
                S("subfloor34", "3/4\" T&G subfloor panel 4x8", 0.71875f, 42, new Color(0.80f, 0.70f, 0.42f)),
                S("ply12", "Plywood 1/2\" 4x8 (header spacer)", 0.5f, 36, new Color(0.90f, 0.78f, 0.58f)),
                S("osb716roof", "OSB 7/16\" roof sheathing 4x8", 0.4375f, 17, Osb),
                // connectors & consumables
                C("sill-gasket", "Sill gasket foam, 5.5\" x 50' roll", 14, 50, new Color(0.25f, 0.45f, 0.85f)),
                C("anchor-bolt", "Anchor bolts 1/2\" x 10\" w/ washer + nut", 3, 1, Galv),
                C("hanger-2x8", "Joist hanger 2x8 (LUS28)", 2, 1, Galv),
                C("hurricane-tie", "Hurricane tie (H2.5A)", 1, 1, Galv),
                C("h-clip", "Plywood H-clips 7/16\" (box of 250)", 22, 250, Galv),
                C("adhesive", "Subfloor construction adhesive, 28 oz tube", 9, 1, new Color(0.85f, 0.75f, 0.45f)),
                C("flash-tape", "Window flashing tape 4\" x 75'", 24, 75, new Color(0.2f, 0.2f, 0.22f)),
                C("shims", "Cedar shims (bundle of 42)", 6, 42, Pine),
                C("drip-edge", "Drip edge, white aluminum 10'", 9, 1, new Color(0.95f, 0.95f, 0.95f)),
                C("ice-water", "Ice & water shield, 36\" x 66' roll", 89, 1, new Color(0.15f, 0.15f, 0.16f)),
                C("underlayment", "Synthetic roof underlayment, 48\" x 250' roll", 119, 1, new Color(0.55f, 0.56f, 0.58f)),
                C("starter-strip", "Shingle starter strip (bundle, 105')", 42, 1, new Color(0.25f, 0.25f, 0.26f)),
                C("shingles-arch", "Architectural shingles, bundle (33.3 sq ft)", 38, 1, new Color(0.3f, 0.3f, 0.32f)),
                C("ridge-cap", "Ridge cap shingles, bundle (33 lin ft)", 65, 1, new Color(0.3f, 0.3f, 0.32f)),
                C("ridge-vent", "Ridge vent, 4' section", 14, 1, new Color(0.15f, 0.15f, 0.16f)),
                C("step-flash", "Step flashing 4x4x8, bundle of 100", 39, 100, Galv),
                C("roof-nails", "Roofing nails 1-1/4\" coil (7200)", 49, 7200, Galv),
                C("cap-staples", "Cap staples (box of 2000)", 29, 2000, Galv),
                C("roof-jack", "Roof jack (pair) + 2x8 plank", 25, 1, Galv)
            );
            FramingTools.RegisterAll();
        }
    }
}
