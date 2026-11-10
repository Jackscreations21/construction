using UnityEngine;

namespace Groundbreak
{
    /// <summary>Exterior envelope materials (Finish Supply "Exterior" tab), deck lumber, tools and the exterior sealant tube.</summary>
    public static class ExteriorCatalog
    {
        // ---- weather barrier
        public const string WrapRoll = "ext-wrap-9x100", WrapTape = "ext-wrap-tape", FlashTape = "ext-flash-tape", CapStaples = "ext-cap-staples";
        // ---- vinyl
        public const string VinylPanel = "ext-vinyl-d4", VinylStarter = "ext-vinyl-starter", VinylOCP = "ext-vinyl-ocp", VinylJ = "ext-vinyl-j", SidingNails = "ext-siding-nails";
        // ---- fiber cement / panels / trim
        public const string FCPlank = "ext-fc-plank", JointFlash = "ext-joint-flash", BBPanel = "ext-bb-panel", Batten = "ext-bb-batten", TrimBoard = "ext-trim-54x4";
        // ---- brick
        public const string Brick = "brick-modular", UsedBrick = "brick-used", Mortar = "mortar-s80", BrickTies = "ext-brick-ties", Weeps = "ext-weeps", ThroughWallFlash = "ext-brick-flash", Lintel = "ext-lintel";
        // ---- eaves
        public const string Soffit = "ext-soffit", FChannel = "ext-fchannel", Coil = "ext-coil", Gutter = "ext-gutter", Hangers = "ext-hangers", EndCaps = "ext-endcaps", Outlet = "ext-outlet", Downspout = "ext-downspout", Elbow = "ext-elbow", Splash = "ext-splash";
        // ---- sealant
        public const string Sealant = "ext-sealant";
        // ---- deck
        public const string PT2x8 = "pt2x8x12", PT2x10 = "pt2x10x12", PT6x6 = "pt6x6x10", Decking = "pt54x6x12", PT2x6 = "pt2x6x12", Baluster = "pt2x2x36", PT4x4 = "4x4x8", PT2x4 = "pt2x4x8",
            LedgerScrews = "ext-ledger-lags", JoistHanger = "ext-hanger-2x8", PostBase = "ext-post-base", ZFlash = "ext-zflash", Sonotube = "ext-sonotube", Concrete = "ext-concrete", DeckScrews = "ext-deck-screws";

        static readonly Color White = new Color(0.93f, 0.93f, 0.91f), Galv = new Color(0.72f, 0.74f, 0.75f), Bag = new Color(0.62f, 0.55f, 0.42f);

        static PieceDefinition PT(string id, string name, float wIn, float hIn, float lenIn, int price) =>
            new PieceDefinition { Id = id, Name = name, Category = PieceCategory.Lumber, Size = new Vector3(Units.In(wIn), Units.In(hIn), Units.In(lenIn)), Price = price, Cuttable = true, Color = new Color(0.55f, 0.62f, 0.42f) };

        static bool _done;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (_done) return; _done = true;
            Catalog.Register(
                FinishStock.Item(WrapRoll, "House wrap 9' x 100' roll", 179, 1, White),
                FinishStock.Item(WrapTape, "House wrap seam tape 1-7/8\" x 165'", 15, 1, new Color(0.6f, 0.7f, 0.82f)),
                FinishStock.Item(FlashTape, "Window flashing tape 4\" x 75' (butyl)", 32, 1, new Color(0.15f, 0.15f, 0.16f)),
                FinishStock.Item(CapStaples, "Cap staples 3/8\" (box of 2,000)", 36, 2000, Galv),

                FinishStock.Item(VinylPanel, "Vinyl siding D4 12'6\" panels (carton of 16, 2 squares)", 189, 16, White),
                FinishStock.Item(VinylStarter, "Vinyl starter strip 12'6\"", 9, 1, White),
                FinishStock.Item(VinylOCP, "Vinyl outside corner post 10'", 34, 1, White),
                FinishStock.Item(VinylJ, "Vinyl J-channel 5/8\" x 12'6\"", 8, 1, White),
                FinishStock.Item(SidingNails, "Siding coil nails 1-1/2\" galvanized (3,600)", 46, 3600, Galv),

                FinishStock.Item(FCPlank, "Fiber cement lap siding 8-1/4\" x 12' (primed)", 15, 1, new Color(0.86f, 0.86f, 0.83f)),
                FinishStock.Item(JointFlash, "Butt-joint flashing 6\" x 8\" (pack of 50)", 22, 50, new Color(0.2f, 0.2f, 0.2f)),
                FinishStock.Item(BBPanel, "Primed panel siding 4' x 8' x 7/16\" (smooth)", 46, 1, new Color(0.86f, 0.86f, 0.83f)),
                FinishStock.Item(Batten, "Primed batten 1x3 x 8'", 6, 1, new Color(0.86f, 0.86f, 0.83f)),
                FinishStock.Item(TrimBoard, "Exterior trim board 5/4x4 x 12' (primed)", 24, 1, White),

                FinishStock.Item(BrickTies, "Corrugated brick ties 7/8\" x 6\" 22 ga (box of 100)", 28, 100, Galv),
                FinishStock.Item(Weeps, "Cell-vent weeps for modular brick (pack of 10)", 12, 10, new Color(0.7f, 0.7f, 0.68f)),
                FinishStock.Item(ThroughWallFlash, "Through-wall flashing 12\" x 33' with drip edge", 78, 1, new Color(0.2f, 0.2f, 0.2f)),
                FinishStock.Item(Lintel, "Steel lintel angle 3-1/2 x 3-1/2 x 1/4 x 6'", 58, 1, new Color(0.3f, 0.3f, 0.32f)),

                FinishStock.Item(Soffit, "Vented vinyl soffit 12\" x 12' panel", 17, 1, White),
                FinishStock.Item(FChannel, "Soffit F-channel 12'6\"", 9, 1, White),
                FinishStock.Item(Coil, "Aluminum trim coil 24\" x 50' (white)", 112, 1, White),
                FinishStock.Item(Gutter, "5\" K-style aluminum gutter 10'", 15, 1, White),
                FinishStock.Item(Hangers, "Hidden gutter hangers with screws (10-pack)", 16, 10, White),
                FinishStock.Item(EndCaps, "5\" K-style end caps (pair)", 7, 1, White),
                FinishStock.Item(Outlet, "Gutter outlet 2x3\"", 8, 1, White),
                FinishStock.Item(Downspout, "Downspout 2x3\" x 10'", 13, 1, White),
                FinishStock.Item(Elbow, "Downspout elbow 2x3\" A/B", 5, 1, White),
                FinishStock.Item(Splash, "Concrete splash block", 9, 1, new Color(0.6f, 0.6f, 0.58f)),

                FinishStock.Item(Sealant, "Exterior sealant (white), 10 oz", 8, 1, White),

                FinishStock.Item(LedgerScrews, "Ledger lag screws 1/2\" x 4\" with washers (box of 25)", 32, 25, Galv),
                FinishStock.Item(JoistHanger, "Joist hanger 2x8, ZMAX", 2, 1, Galv),
                FinishStock.Item(PostBase, "Post base 6x6, ZMAX", 24, 1, Galv),
                FinishStock.Item(ZFlash, "Z-flashing 10'", 9, 1, Galv),
                FinishStock.Item(Sonotube, "Concrete form tube 10\" x 4'", 12, 1, new Color(0.75f, 0.6f, 0.4f)),
                FinishStock.Item(Concrete, "Concrete mix 80 lb", 7, 1, Bag),
                FinishStock.Item(DeckScrews, "Deck screws 2-1/2\" coated (5 lb, ~350)", 36, 350, new Color(0.45f, 0.35f, 0.25f)),

                PT(PT2x8, "PT 2x8 x 12'", 7.25f, 1.5f, 144f, 17),
                PT(PT2x10, "PT 2x10 x 12'", 9.25f, 1.5f, 144f, 23),
                PT(PT6x6, "PT 6x6 x 10'", 5.5f, 5.5f, 120f, 49),
                PT(Decking, "PT 5/4x6 decking x 12'", 5.5f, 1.0f, 144f, 13),
                PT(PT2x6, "PT 2x6 x 12'", 5.5f, 1.5f, 144f, 14),
                PT(Baluster, "PT 2x2 baluster 36\"", 1.5f, 1.5f, 36f, 2)
            );

            FinishSupply.Add("Exterior", WrapRoll, WrapTape, FlashTape, CapStaples, VinylPanel, VinylStarter, VinylOCP, VinylJ, SidingNails,
                FCPlank, JointFlash, BBPanel, Batten, TrimBoard, Brick, Mortar, BrickTies, Weeps, ThroughWallFlash, Lintel,
                Soffit, FChannel, Coil, Gutter, Hangers, EndCaps, Outlet, Downspout, Elbow, Splash, Sealant);
            FinishSupply.Add("Deck", PT2x10, PT2x8, PT6x6, Decking, PT2x6, Baluster, PT4x4, PT2x4, LedgerScrews, JoistHanger, PostBase, ZFlash, Sonotube, Concrete, DeckScrews);

            ToolFactory.Register(new ToolShopItem { Id = "cap-stapler", Name = "Hammer Cap Stapler + Utility Knife", Price = 39, Description = "Cap-staples house wrap, cuts openings (modified I-cut) and applies seam/flashing tape." }, () => new CapStaplerTool());
            ToolFactory.Register(new ToolShopItem { Id = "siding-nailer", Name = "Coil Siding Nailer", Price = 329, Description = "Starter, corner posts, J-channel, vinyl courses (nail in slot centers, not tight), fiber cement, panels, trim, soffit." }, () => new SidingNailerTool());
            ToolFactory.Register(new ToolShopItem { Id = "siding-zip-tool", Name = "Siding Zip Tool", Price = 8, Description = "Unlocks a vinyl course from the one below to remove or re-fit it." }, () => new ZipTool());
            ToolFactory.Register(new ToolShopItem { Id = "brick-trowel", Name = "Mason's Trowel, Line & Jointer", Price = 42, Description = "Base flashing + weeps, brick ties, one brick course per click (running bond, 3/8\" concave joints)." }, () => new BrickTrowelTool());
            ToolFactory.Register(new ToolShopItem { Id = "ext-snips", Name = "Vinyl / Aluminum Snips", Price = 29, Description = "Cuts vinyl and aluminum: trims panel ends to the 1/4\" gap, hangs gutters and downspouts." }, () => new TinSnipsTool());
            ToolFactory.Register(new ToolShopItem { Id = "coil-brake", Name = "Portable Coil Brake 10'6\"", Price = 1499, Description = "Bends aluminum trim coil to wrap fascia boards (and soffit returns). 2 slots." }, () => new CoilBrakeTool());
            ToolFactory.Register(new ToolShopItem { Id = "deck-kit", Name = "Deck Builder's Kit", Price = 149, Description = "Post-hole digger, string line, hanger nailer and impact bits: builds a deck stage by stage." }, () => new DeckKitTool());

            CaulkGun.RegisterTube(new CaulkGun.Tube { Name = "Exterior sealant (white)", StockId = Sealant, BeadColor = new Color(0.95f, 0.95f, 0.93f), Apply = ExteriorTrim.ApplySealant });

            Tablet.AddHelp("Exterior: wrap bottom-up (6\" laps, tape seams), I-cut + flash openings sill-jambs-head, then siding. Vinyl nails go in slot centers, not tight.");
            Tablet.AddHelp("Brick veneer: base flashing + weeps every 33\", ties every 16\" x 24\", 1\" air space. Deck: flash + lag the ledger, guards 36\" with <4\" gaps.");
        }
    }
}
