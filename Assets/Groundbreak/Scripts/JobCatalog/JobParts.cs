using UnityEngine;

namespace Groundbreak
{
    /// <summary>Parts and service tools the catalog jobs use (ids "jc-..."). Real product descriptions and typical US big-box prices (2025).
    /// Ids owned by other features are only given a fallback definition when missing.</summary>
    public static class JP
    {
        // ---- tools (new hand tools; everything else reuses trade tools)
        public const string Plunger = "jc-plunger", ClosetAuger = "jc-closet-auger", DrumAuger = "jc-drum-auger", ElementWrench = "jc-element-wrench",
            MultiTool = "jc-multitool", ColdChisel = "jc-cold-chisel", GroutSaw = "jc-grout-saw", StudFinder = "jc-stud-finder", WetVac = "jc-wet-vac",
            HexKey = "jc-hex-key", SocketSet = "jc-socket-set", Grinder = "jc-grinder", VentBrush = "jc-vent-brush", PuttyKnife = "jc-putty-knife",
            ImpactDriver = "jc-impact", Rivet = "jc-pop-rivet";

        // ---- plumbing parts
        public const string Flapper = "jc-flapper", FillValve = "jc-fill-valve", WaxRing = "jc-wax-ring", ClosetBolts = "jc-closet-bolts",
            ToiletSupply = "jc-toilet-supply", AngleStop = "jc-angle-stop", Cartridge = "jc-cartridge", TubCartridge = "jc-tub-cartridge",
            Element = "jc-wh-element", WhThermostat = "jc-wh-thermostat", TpValve = "jc-tp-valve", ExpTank = "jc-exp-tank", Ptfe = "jc-ptfe",
            Sillcock = "jc-sillcock", BibWasher = "jc-bib-washer", PushCoupling = "jc-push-coupling", Disposal = "jc-disposal", Plumbers = "jc-putty";
        // ---- electrical
        public const string Switch = "jc-switch", Breaker20 = "jc-breaker-20", AfciBreaker = "jc-afci-15", WireNuts = "jc-wirenuts", OldWorkBox = "jc-oldwork-box",
            Nm142 = "jc-nm-14-2", Fixture = "jc-flush-fixture", Bulb = "jc-led-bulb", Smoke = "jc-smoke-alarm", Battery9V = "jc-9v", BellTrans = "jc-bell-transformer",
            BellButton = "jc-bell-button", FanMotor = "jc-fan-motor", BathFan = "jc-bath-fan", PorchLight = "jc-porch-light", Socket = "jc-lamp-socket", Plate = "jc-cover-plate";
        // ---- HVAC
        public const string Filter = "jc-filter-16x25", FlameSensor = "jc-flame-sensor", Igniter = "jc-igniter", Emery = "jc-emery", CondPump = "jc-cond-pump",
            PvcTrap = "jc-pvc-34-fittings", AlgaeTabs = "jc-algae-tabs", Flex6 = "jc-flex-6", DrawBands = "jc-draw-bands", Mastic = "jc-mastic", FoilTape = "jc-foil-tape",
            Stat = "jc-thermostat", AABatt = "jc-aa-batteries", VentHood = "jc-vent-hood", VentDuct = "jc-vent-duct-4", Capacitor = "jc-capacitor";
        // ---- carpentry
        public const string PvcSill = "jc-pvc-sill", Epoxy = "jc-wood-epoxy", PvcTrim = "jc-pvc-1x4", HingeScrews = "jc-hinge-screws", Hinge = "jc-door-hinge",
            Shims = "jc-shims", SqueakKit = "jc-squeak-kit", Adhesive = "jc-const-adhesive", TreadBoard = "jc-pt-2x6x8", SashBalance = "jc-sash-balance",
            CabHinge = "jc-cab-hinge", Slides = "jc-drawer-slides", WoodGlue = "jc-wood-glue", FinishNails = "jc-finish-nails", WoodFiller = "jc-wood-filler",
            Dowels = "jc-dowels", Strike = "jc-strike-plate", Base = "jc-base-356", ExtScrews = "jc-ext-screws";
        // ---- drywall / paint
        public const string PaintGal = "jc-paint-gal", CeilingPaint = "jc-ceiling-paint", DropCloth = "jc-drop-cloth", Tape = "jc-painters-tape", RollerCover = "jc-roller-cover",
            Bead = "jc-corner-bead", StainBlock = "jc-shellac-primer", DwScrewsBox = "jc-dw-screws-small", Caulk = "jc-painters-caulk";
        // ---- exterior / roofing
        public const string VinylPanel = "jc-vinyl-panel", SidingNails = "jc-siding-nails", Fascia = "jc-fascia-1x6", StepFlash = "jc-step-flashing", PipeBoot = "jc-pipe-boot",
            GutterSeal = "jc-gutter-sealant", Elbow = "jc-downspout-elbow", ZipScrews = "jc-zip-screws", Lag = "jc-ledger-lok", ZFlash = "jc-z-flashing",
            Post4x4 = "jc-pt-4x4x8", Rail2x4 = "jc-pt-2x4x8", PostMix = "jc-postmix", GateKit = "jc-gate-kit", GateHinge = "jc-gate-hinges", SoffitPanel = "jc-soffit",
            HardwareCloth = "jc-hardware-cloth", Housewrap = "jc-housewrap", Shingle = "jc-arch-bundle", IceWater = "jc-ice-water", SkyFlash = "jc-skylight-flash";
        // ---- concrete / masonry
        public const string Patcher = "jc-vinyl-patcher", Bonding = "jc-bonding-adhesive", Resurfacer = "jc-resurfacer", CrackSeal = "jc-crack-sealant",
            BackerRod = "jc-backer-rod", MortarN = "jc-mortar-n", Block = "jc-wall-block", BlockCap = "jc-wall-cap", BlockAdhesive = "jc-block-adhesive",
            Gravel = "jc-gravel-57", Fabric = "jc-drain-fabric", Drain4 = "jc-drain-pipe";
        // ---- flooring / tile / cabinets
        public const string FloorTile = "jc-porcelain-12", Thinset = "jc-thinset", Grout = "jc-grout", GroutSealer = "jc-grout-sealer", LvpBox = "jc-lvp-box",
            TMold = "jc-t-molding", Underlay = "jc-underlayment";
        // ---- remodel products
        public const string Vanity30 = "jc-vanity-30", Toilet = "jc-toilet", LavFaucet = "jc-lav-faucet", SubwayTile = "jc-subway-box", CementBoard = "jc-cement-board",
            KitFaucet = "jc-kitchen-faucet", Window = "jc-window-dh", EntryDoor = "jc-entry-door", Cedar4x4 = "jc-cedar-6x6", Cedar2x8 = "jc-cedar-2x8", Cedar2x6 = "jc-cedar-2x6",
            Sonotube = "jc-sonotube", Concrete80 = RepairCatalog.Concrete80, Stain = "jc-deck-stain", PostBase = "jc-post-base", CarriageBolt = "jc-carriage-bolts",
            CabPaint = "jc-cabinet-paint", Pulls = "jc-cabinet-pulls", TubValve = "jc-tub-valve";

        static PieceDefinition Con(string id, string name, int price, int units, Color c) => new PieceDefinition
        { Id = id, Name = name, Category = PieceCategory.Consumable, Price = price, UnitsPerPurchase = units, Color = c, Size = new Vector3(0.2f, 0.15f, 0.3f) };

        static void I(string id, string name, int price, float lb, float cuFt, int units = 1) => I(Con(id, name, price, units, new Color(0.8f, 0.8f, 0.78f)), lb, cuFt);
        static void I(PieceDefinition d, float lb, float cuFt)
        {
            if (Catalog.Get(d.Id) == null) Catalog.Register(d);
            VanCargo.RegisterItem(d.Id, lb, cuFt);
        }

        static void T(string id, string name, int price, string desc)
        {
            foreach (var s in ToolFactory.ShopItems) if (s.Id == id) return;
            ToolFactory.Register(new ToolShopItem { Id = id, Name = name, Price = price, Description = desc }, () => new RepairHandTool(id, name, desc));
        }

        public static void Register()
        {
            T(Plunger, "Flange Toilet Plunger", 14, "Seals in the toilet outlet; push slowly, pull hard.");
            T(ClosetAuger, "Closet Auger 3'", 39, "Snakes through the toilet trapway without scratching the porcelain.");
            T(DrumAuger, "Drum Auger 1/4\" x 25'", 45, "Clears sink drains past the trap arm.");
            T(ElementWrench, "Water Heater Element Wrench 1-1/2\"", 9, "Socket for screw-in heating elements.");
            T(MultiTool, "Oscillating Multi-Tool", 99, "Plunge-cuts rotten trim, drywall, grout and nails.");
            T(ColdChisel, "Cold Chisel & 3 lb Hand Sledge", 29, "Chips out loose concrete and mortar.");
            T(GroutSaw, "Grout Saw / Rake", 9, "Rakes old grout out of tile joints.");
            T(StudFinder, "Stud & Joist Finder", 25, "Finds framing behind drywall and floors.");
            T(WetVac, "Wet/Dry Vac 6 gal", 89, "Sucks out clogged condensate lines, cleans up water.");
            T(HexKey, "Disposal Hex Wrench 1/4\"", 4, "Turns the disposal flywheel from below to free a jam.");
            T(SocketSet, "Socket Set + Ratchet 3/8\" & 1/2\"", 59, "Lag screws, ledger bolts, carriage bolts.");
            T(Grinder, "Angle Grinder 4-1/2\" + Diamond Blade", 89, "Grinds out mortar joints and trip edges in concrete.");
            T(VentBrush, "Dryer Vent Brush Kit (drill-driven)", 29, "Spins lint out of a 4\" dryer vent run.");
            T(PuttyKnife, "Stiff Putty Knife 1-1/2\"", 7, "Scrapes old caulk, sealant and paint.");
            T(ImpactDriver, "Cordless Impact Driver", 129, "Drives structural screws and lags.");
            T(Rivet, "Pop Rivet Tool", 18, "Joins aluminum gutter and downspout sections.");

            // plumbing
            I(Flapper, "Toilet flapper 2\" universal", 9, 0.2f, 0.01f);
            I(FillValve, "Fill valve, adjustable (anti-siphon)", 13, 0.6f, 0.03f);
            I(WaxRing, "Wax ring with horn", 6, 1f, 0.02f);
            I(ClosetBolts, "Closet bolts 5/16\" with caps", 5, 0.2f, 0.005f);
            I(ToiletSupply, "Braided toilet supply 3/8\" comp x 7/8\" ballcock x 12\"", 8, 0.3f, 0.01f);
            I(AngleStop, "1/4-turn angle stop 1/2\" x 3/8\" compression", 12, 0.4f, 0.01f);
            I(Cartridge, "Single-handle faucet cartridge", 24, 0.2f, 0.01f);
            I(TubCartridge, "Tub/shower valve cartridge", 29, 0.3f, 0.01f);
            I(Element, "Water heater element 4500 W 240 V screw-in", 18, 0.6f, 0.02f);
            I(WhThermostat, "Water heater thermostat (upper, with ECO)", 24, 0.3f, 0.01f);
            I(TpValve, "T&P relief valve 3/4\", 150 psi / 210°F", 22, 0.8f, 0.01f);
            I(ExpTank, "Thermal expansion tank 2 gal", 45, 6f, 0.4f);
            I(Ptfe, "PTFE thread tape 1/2\" x 520\"", 2, 0.05f, 0.002f);
            I(Sillcock, "Frost-free sillcock 1/2\" x 10\"", 32, 1.2f, 0.03f);
            I(BibWasher, "Faucet washer & packing assortment", 4, 0.1f, 0.003f);
            I(PushCoupling, "Push-to-connect coupling 1/2\"", 9, 0.2f, 0.005f);
            I(Disposal, "Garbage disposal 1/2 HP", 119, 12f, 0.7f);
            I(Plumbers, "Plumber's putty 14 oz", 5, 0.9f, 0.01f);
            // electrical
            I(Switch, "Single-pole toggle switch 15A, white", 3, 0.1f, 0.003f);
            I(Breaker20, "20A single-pole breaker", 10, 0.3f, 0.01f);
            I(AfciBreaker, "15A combination AFCI breaker", 45, 0.4f, 0.01f);
            I(WireNuts, "Wire connectors (bag of 25)", 5, 0.008f, 0.0004f, 25);
            I(OldWorkBox, "Old-work box, 1-gang 18 cu in", 3, 0.1f, 0.01f);
            I(Nm142, "NM-B 14/2 with ground (sold by the foot, 25' coil)", 28, 0.06f, 0.002f, 25);
            I(Fixture, "Flush-mount ceiling light 13\"", 39, 3f, 0.3f);
            I(Bulb, "LED A19 60 W equivalent", 3, 0.1f, 0.01f);
            I(Smoke, "Smoke alarm, hardwired with battery backup", 25, 0.6f, 0.05f);
            I(Battery9V, "9 V battery", 4, 0.1f, 0.002f);
            I(BellTrans, "Doorbell transformer 16 V 10 VA", 18, 0.8f, 0.01f);
            I(BellButton, "Lighted doorbell button", 9, 0.1f, 0.003f);
            I(FanMotor, "Bath fan motor/blower replacement", 35, 1.5f, 0.06f);
            I(BathFan, "Bath exhaust fan 80 CFM", 69, 6f, 0.5f);
            I(PorchLight, "Outdoor wall lantern", 45, 3f, 0.4f);
            I(Socket, "Lamp socket replacement kit", 6, 0.1f, 0.003f);
            I(Plate, "Wall plate, 1-gang", 1, 0.05f, 0.002f);
            // HVAC
            I(Filter, "Furnace filter 16x25x1 MERV 8", 12, 1f, 0.12f);
            I(FlameSensor, "Furnace flame sensor", 15, 0.1f, 0.003f);
            I(Igniter, "Hot surface igniter (silicon nitride)", 35, 0.1f, 0.003f);
            I(Emery, "Emery cloth roll", 4, 0.1f, 0.003f);
            I(CondPump, "Condensate pump 1/30 HP with safety switch", 55, 3f, 0.15f);
            I(PvcTrap, "3/4\" PVC fittings + primer/cement (condensate trap kit)", 9, 0.3f, 0.01f);
            I(AlgaeTabs, "Condensate pan tablets", 6, 0.2f, 0.003f);
            I(Flex6, "Insulated flex duct 6\" x 25' R-6", 45, 6f, 1.2f);
            I(DrawBands, "Duct draw bands (10 pack)", 8, 0.04f, 0.001f, 10);
            I(Mastic, "Duct mastic 1 qt", 12, 2.5f, 0.04f);
            I(FoilTape, "UL 181 foil tape 2-1/2\" x 60 yd", 10, 0.6f, 0.02f);
            I(Stat, "Programmable thermostat 5-wire", 35, 0.6f, 0.03f);
            I(AABatt, "AA batteries (pair)", 3, 0.1f, 0.002f);
            I(VentHood, "Dryer vent hood 4\" louvered", 14, 0.6f, 0.08f);
            I(VentDuct, "4\" rigid aluminum vent pipe x 8'", 11, 1f, 0.25f);
            I(Capacitor, "Run capacitor 45/5 µF", 18, 0.3f, 0.01f);
            // carpentry
            I(PvcSill, "PVC sill nose 5/4 x 6 x 8'", 45, 8f, 0.3f);
            I(Epoxy, "Two-part wood epoxy repair kit", 28, 1.2f, 0.03f);
            I(PvcTrim, "PVC trim 1x4 x 8'", 24, 4f, 0.15f);
            I(HingeScrews, "3\" hinge screws (6 pack)", 3, 0.03f, 0.001f, 6);
            I(Hinge, "Door hinge 3-1/2\" satin nickel", 5, 0.4f, 0.003f);
            I(Shims, "Composite shims (bundle of 12)", 4, 0.04f, 0.002f, 12);
            I(SqueakKit, "Floor squeak repair kit (break-off screws)", 25, 0.5f, 0.01f);
            I(Adhesive, "Construction adhesive 10 oz", 5, 0.9f, 0.02f);
            I(TreadBoard, "2x6 x 8' pressure-treated (step tread)", 9, 15f, 0.5f);
            I(SashBalance, "Spiral sash balance (pair)", 28, 1f, 0.05f);
            I(CabHinge, "Concealed cabinet hinge (pair)", 9, 0.3f, 0.005f);
            I(Slides, "Ball-bearing drawer slides 18\" (pair)", 15, 2f, 0.04f);
            I(WoodGlue, "Wood glue 8 oz", 5, 0.6f, 0.01f);
            I(FinishNails, "15 ga finish nails 2\" (box of 1000)", 14, 0.003f, 0.0001f, 1000);
            I(WoodFiller, "Exterior wood filler 8 oz", 6, 0.6f, 0.01f);
            I(Dowels, "Hardwood dowels 1/4\" (bag)", 4, 0.1f, 0.003f);
            I(Strike, "Strike plate, satin nickel", 3, 0.1f, 0.001f);
            I(Base, "Baseboard 3-1/2\" primed x 8'", 9, 3f, 0.1f);
            I(ExtScrews, "Exterior structural screws 3\" (box of 50)", 12, 0.02f, 0.0004f, 50);
            // drywall / paint
            I(PaintGal, "Interior eggshell paint, 1 gal", 38, 11f, 0.15f);
            I(CeilingPaint, "Flat ceiling paint, 1 gal", 30, 11f, 0.15f);
            I(DropCloth, "Canvas drop cloth 9' x 12'", 18, 4f, 0.2f);
            I(Tape, "Painter's tape 1.88\" x 60 yd", 7, 0.3f, 0.01f);
            I(RollerCover, "Roller cover 3/8\" nap", 6, 0.2f, 0.02f);
            I(Bead, "Metal corner bead 8'", 4, 1.2f, 0.06f);
            I(StainBlock, "Shellac stain-blocking primer (aerosol)", 12, 1f, 0.02f);
            I(DwScrewsBox, "Drywall screws 1-5/8\" (1 lb, ~200)", 8, 0.005f, 0.0001f, 200);
            I(Caulk, "Painter's acrylic caulk 10 oz", 4, 0.9f, 0.02f);
            // exterior / roofing
            I(VinylPanel, "Vinyl siding panel D4 x 12'6\" (matched)", 12, 2.5f, 0.1f);
            I(SidingNails, "Roofing/siding nails 1-1/2\" galv (1 lb)", 6, 0.01f, 0.0002f, 100);
            I(Fascia, "Primed fascia 1x6 x 12' (finger-joint)", 22, 8f, 0.25f);
            I(StepFlash, "Step flashing 5\" x 7\" aluminum (100 pack)", 25, 0.03f, 0.0005f, 100);
            I(PipeBoot, "Pipe flashing boot 1-1/2\" - 3\"", 12, 0.8f, 0.1f);
            I(GutterSeal, "Gutter seam sealant 10 oz", 8, 0.9f, 0.02f);
            I(Elbow, "Downspout elbow 2x3 \"A\"", 6, 0.3f, 0.03f);
            I(ZipScrews, "Zip screws 1/2\" (bag of 50)", 6, 0.01f, 0.0002f, 50);
            I(Lag, "Ledger screws 5\" structural (12 pack)", 30, 0.25f, 0.002f, 12);
            I(ZFlash, "Z-flashing 10'", 9, 1.5f, 0.1f);
            I(Post4x4, "4x4 x 8' pressure-treated post", 12, 28f, 0.9f);
            I(Rail2x4, "2x4 x 8' pressure-treated rail", 5, 10f, 0.3f);
            I(PostMix, "Fast-setting post concrete 50 lb", 7, 50f, 0.4f);
            I(GateKit, "Anti-sag gate kit (cable + turnbuckle)", 20, 2f, 0.03f);
            I(GateHinge, "Heavy T-hinges 8\" (pair)", 18, 2.5f, 0.03f);
            I(SoffitPanel, "Vented vinyl soffit panel 12'", 15, 3f, 0.2f);
            I(HardwareCloth, "Hardware cloth 1/4\" x 24\" x 5'", 9, 1f, 0.05f);
            I(Housewrap, "Housewrap 9' x 100'", 159, 20f, 1.2f);
            I(Shingle, "Architectural shingles, bundle (33.3 sq ft)", 42, 70f, 1f);
            I(IceWater, "Ice & water shield 36\" x 66' roll", 89, 40f, 1f);
            I(SkyFlash, "Skylight step-flashing kit", 75, 5f, 0.3f);
            // concrete / masonry
            I(Patcher, "Vinyl concrete patcher 40 lb", 17, 40f, 0.4f);
            I(Bonding, "Concrete bonding adhesive 1 qt", 12, 2f, 0.04f);
            I(Resurfacer, "Concrete resurfacer 40 lb", 24, 40f, 0.4f);
            I(CrackSeal, "Self-leveling concrete crack sealant 10 oz", 9, 0.9f, 0.02f);
            I(BackerRod, "Backer rod 3/8\" x 20'", 6, 0.1f, 0.02f);
            I(MortarN, "Type N mortar mix 60 lb", 9, 60f, 0.5f);
            I(Block, "Segmental retaining wall block 4\" x 12\" x 7\"", 4, 35f, 0.3f);
            I(BlockCap, "Retaining wall cap 16\"", 6, 30f, 0.2f);
            I(BlockAdhesive, "Masonry/landscape block adhesive 10 oz", 7, 0.9f, 0.02f);
            I(Gravel, "#57 drainage stone 0.5 cu ft bag", 6, 50f, 0.5f);
            I(Fabric, "Drainage fabric 3' x 50'", 25, 5f, 0.2f);
            I(Drain4, "4\" perforated drain pipe x 10'", 12, 4f, 0.3f);
            // flooring / tile
            I(FloorTile, "Porcelain floor tile 12\" x 12\" (matched)", 3, 4.5f, 0.05f);
            I(Thinset, "Modified thinset mortar 25 lb", 18, 25f, 0.3f);
            I(Grout, "Sanded grout 10 lb", 16, 10f, 0.12f);
            I(GroutSealer, "Grout sealer 1 pt", 12, 1f, 0.02f);
            I(LvpBox, "Luxury vinyl plank, box (~20 sq ft)", 45, 40f, 0.6f);
            I(TMold, "T-molding transition 72\"", 28, 2f, 0.06f);
            I(Underlay, "Flooring underlayment 100 sq ft", 30, 8f, 0.5f);
            // remodel products
            I(Vanity30, "30\" vanity with cultured marble top", 389, 90f, 9f);
            I(Toilet, "Two-piece elongated toilet 1.28 gpf", 229, 85f, 6f);
            I(LavFaucet, "Lavatory faucet 4\" centerset with drain", 89, 4f, 0.2f);
            I(SubwayTile, "Ceramic subway tile 3\" x 6\" (box, 10 sq ft)", 25, 25f, 0.3f);
            I(CementBoard, "Cement board 1/2\" x 3' x 5'", 12, 45f, 0.6f);
            I(KitFaucet, "Pull-down kitchen faucet", 189, 8f, 0.5f);
            I(Window, "Vinyl replacement window double-hung 36\" x 60\" Low-E", 329, 55f, 8f);
            I(EntryDoor, "Prehung steel entry door 36\" x 80\"", 389, 120f, 14f);
            I(Cedar4x4, "Rough cedar 6x6 x 10'", 89, 60f, 2.5f);
            I(Cedar2x8, "Cedar 2x8 x 14'", 62, 30f, 1.2f);
            I(Cedar2x6, "Cedar 2x6 x 12'", 34, 20f, 0.8f);
            I(Sonotube, "Concrete form tube 10\" x 4'", 12, 3f, 2.2f);
            I(Stain, "Exterior semi-transparent stain 1 gal", 42, 10f, 0.15f);
            I(PostBase, "Standoff post base 6x6", 22, 3f, 0.05f);
            I(CarriageBolt, "Carriage bolts 1/2\" x 8\" galv (4 pack)", 14, 1.2f, 0.01f, 4);
            I(CabPaint, "Cabinet & trim enamel 1 qt", 26, 3f, 0.04f);
            I(Pulls, "Cabinet pulls (10 pack)", 29, 2f, 0.03f, 10);
            I(TubValve, "Pressure-balance tub/shower valve with trim", 149, 5f, 0.2f);
            // already owned by Repairs, used here too
            I(RepairCatalog.Concrete80, "Concrete mix, 80 lb bag (0.6 cu ft)", 6, 80f, 0.6f);
        }
    }
}
