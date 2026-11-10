using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>HVAC run specs (trunk, round pipe, exhaust, line set, condensate) and the Mechanical catalog
    /// (duct stock, plenums, boots, registers, grilles, caps, pad, thermostat, consumables).</summary>
    public static class HvacSpecs
    {
        // ---- run spec ids
        public const string Trunk8x12 = "duct-rect-8x12", Trunk8x16 = "duct-rect-8x16";
        public const string Round5 = "duct-round-5", Round6 = "duct-round-6", Round7 = "duct-round-7", Round8 = "duct-round-8";
        public const string Exhaust4 = "duct-exhaust-4", Dryer4 = "duct-dryer-4";
        public const string LineLiquid = "lineset-liquid-38", LineSuction = "lineset-suction-34", SuctionBare = "refrig-suction-34-bare";
        public const string Condensate = "condensate-pvc-34";

        // ---- part ids (no "ac"/"hp" in ids: ProductModels treats those as condensers)
        public const string SupplyPlenum = "mech-plenum-sup", ReturnPlenum = "mech-plenum-ret", Coil = "mech-coil3t";
        public const string Boot4x10 = "mech-boot-4x10", Boot4x12 = "mech-boot-4x12";
        public const string Reg4x10 = "mech-reg-4x10", Reg4x12 = "mech-reg-4x12";
        public const string Grille14x20 = "mech-rgrille-14x20", Grille20x20 = "mech-rgrille-20x20";
        public const string WallCap4 = "mech-wallcap4", RoofCap4 = "mech-roofcap4", RoofCap7 = "mech-roofcap7";
        public const string Pad = "mech-pad", Thermostat = "mech-tstat";
        public const string Takeoff6 = "mech-takeoff6";
        public const string FoilTape = "foil-tape", Screws = "duct-screws";

        /// <summary>Specs whose suction line is wrapped in closed-cell foam.</summary>
        public static readonly HashSet<string> Insulated = new HashSet<string> { LineSuction };

        public static float In(float i) => Units.In(i);
        static readonly Color Galv = Color.white;

        /// <summary>Approximate design airflow of a round duct at ~0.08 in.w.c./100 ft (Manual D friction chart).</summary>
        public static float RoundCfm(float nominalIn)
        {
            if (nominalIn <= 0) return 200f;
            if (nominalIn <= 4.1f) return 50f;
            if (nominalIn <= 5.1f) return 75f;
            if (nominalIn <= 6.1f) return 110f;
            if (nominalIn <= 7.1f) return 150f;
            if (nominalIn <= 8.1f) return 200f;
            return 200f + (nominalIn - 8f) * 80f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            RunSpecs.Register(
                Rect(Trunk8x12, "8x12 galvanized trunk", 12f, 8f, 650f, "duct-8x12-5ft"),
                Rect(Trunk8x16, "8x16 galvanized trunk", 16f, 8f, 900f, "duct-8x16-5ft"),
                Round(Round5, "5\" round galvanized pipe", 5f, "duct-5-5ft"),
                Round(Round6, "6\" round galvanized pipe", 6f, "duct-6-5ft"),
                Round(Round7, "7\" round galvanized pipe", 7f, "duct-7-5ft"),
                Round(Round8, "8\" round galvanized pipe", 8f, "duct-8-5ft"),
                new RunSpec
                {
                    Id = Exhaust4, Name = "4\" galvanized exhaust duct (bath fan)", Trade = Trade.Hvac, Kind = NetKind.Duct, Shape = RunShape.Round,
                    Width = In(4f), NominalIn = 4f, Surface = SurfaceKind.Galvanized, Tint = Galv, Join = JoinMethod.Tape,
                    StockId = "duct-4-5ft", FeetPerUnit = 5f, CutTool = "tin-snips", FittingPrice = 5, Cfm = 80f
                },
                new RunSpec
                {
                    Id = Dryer4, Name = "4\" rigid aluminum dryer vent", Trade = Trade.Hvac, Kind = NetKind.Duct, Shape = RunShape.Round,
                    Width = In(4.05f), NominalIn = 4f, Surface = SurfaceKind.BrushedSteel, Tint = Color.white, Join = JoinMethod.Tape,
                    StockId = "dryer-4-8ft", FeetPerUnit = 8f, CutTool = "tin-snips", FittingPrice = 6, Cfm = 120f
                },
                new RunSpec
                {
                    Id = LineLiquid, Name = "3/8\" liquid line (line set)", Trade = Trade.Hvac, Kind = NetKind.Refrigerant, Shape = RunShape.Round,
                    Width = In(0.375f), NominalIn = 0.375f, Surface = SurfaceKind.Brass, Tint = MepLook.Copper, Join = JoinMethod.Sweat,
                    BendsInline = true, MinBendRadius = In(2f), StockId = "lineset-38x34-25", FeetPerUnit = 25f, CutTool = "tubing-cutter", FittingPrice = 0
                },
                new RunSpec
                {
                    // 3/4" ACR suction line in 1/2"-wall black closed-cell foam (Armaflex): OD ≈ 1-3/4"
                    Id = LineSuction, Name = "3/4\" insulated suction line (line set)", Trade = Trade.Hvac, Kind = NetKind.Refrigerant, Shape = RunShape.Round,
                    Width = In(1.75f), NominalIn = 0.75f, Surface = SurfaceKind.DarkPlastic, Tint = new Color(0.55f, 0.55f, 0.56f), Join = JoinMethod.Sweat,
                    BendsInline = true, MinBendRadius = In(4f), StockId = null, FeetPerUnit = 25f, CutTool = "tubing-cutter", FittingPrice = 0
                },
                new RunSpec
                {
                    Id = SuctionBare, Name = "3/4\" ACR copper (bare)", Trade = Trade.Hvac, Kind = NetKind.Refrigerant, Shape = RunShape.Round,
                    Width = In(0.75f), NominalIn = 0.75f, Surface = SurfaceKind.Brass, Tint = MepLook.Copper, Join = JoinMethod.Sweat,
                    BendsInline = true, MinBendRadius = In(3f), StockId = "refrig-cu34-20", FeetPerUnit = 20f, CutTool = "tubing-cutter", FittingPrice = 0
                },
                new RunSpec
                {
                    Id = Condensate, Name = "3/4\" PVC condensate", Trade = Trade.Hvac, Kind = NetKind.Condensate, Shape = RunShape.Round,
                    Width = In(1.05f), NominalIn = 0.75f, Surface = SurfaceKind.PaintedWhite, Tint = MepLook.PvcWhite, Join = JoinMethod.Solvent,
                    StockId = "cond-pvc34-10", FeetPerUnit = 10f, CutTool = "pvc-saw", FittingPrice = 1
                });

            Catalog.Register(
                // duct stock (5' sections)
                D("duct-8x12-5ft", "Galvanized rect duct 8x12 x 5'", 12, 8, 60, 38),
                D("duct-8x16-5ft", "Galvanized rect duct 8x16 x 5'", 16, 8, 60, 46),
                D("duct-5-5ft", "5\" round galvanized pipe x 5'", 5, 5, 60, 11),
                D("duct-6-5ft", "6\" round galvanized pipe x 5'", 6, 6, 60, 13),
                D("duct-7-5ft", "7\" round galvanized pipe x 5'", 7, 7, 60, 16),
                D("duct-8-5ft", "8\" round galvanized pipe x 5'", 8, 8, 60, 19),
                D("duct-4-5ft", "4\" galvanized exhaust duct x 5'", 4, 4, 60, 9),
                D("dryer-4-8ft", "4\" rigid aluminum dryer vent x 8'", 4, 4, 96, 14),
                D("lineset-38x34-25", "Line set 3/8 x 3/4 x 25' (insulated)", 14, 6, 14, 259),
                D("refrig-cu34-20", "ACR copper 3/4\" x 20' (bare)", 0.75f, 0.75f, 240, 95),
                D("cond-pvc34-10", "3/4\" PVC sch 40 x 10' (condensate)", 1.05f, 1.05f, 120, 4),
                // parts
                D(SupplyPlenum, "Supply plenum 20x20x48 (galvanized)", 20, 48, 20, 89),
                D(ReturnPlenum, "Return drop / filter box 18x16x28", 18, 28, 16, 79),
                D(Coil, "Cased evaporator coil 3-ton (upflow)", 21, 26, 21.5f, 899),
                D(Takeoff6, "Starting collar 6\" w/ damper (takeoff)", 8, 3, 8, 9),
                D(Boot4x10, "Register boot 4x10 x 6\" (straight)", 12, 7.5f, 6, 14),
                D(Boot4x12, "Register boot 4x12 x 6\" (straight)", 14, 7.5f, 6, 16),
                D(Reg4x10, "Floor/ceiling register 4x10, white", 11.75f, 1f, 5.75f, 9),
                D(Reg4x12, "Floor/ceiling register 4x12, white", 13.75f, 1f, 5.75f, 11),
                D(Grille14x20, "Return filter grille 14x20 + box", 20, 10.75f, 14, 39),
                D(Grille20x20, "Return filter grille 20x20 + box", 20, 10.75f, 20, 49),
                D(WallCap4, "Wall vent cap 4\" w/ damper", 7, 7, 2.5f, 15),
                D(RoofCap4, "Roof jack 4\" w/ damper", 12, 6, 12, 29),
                D(RoofCap7, "Roof jack 7\" w/ damper (range hood)", 14, 7, 14, 39),
                D(Pad, "Condenser pad 36x36x3 (composite)", 36, 3, 36, 45),
                D(Thermostat, "Programmable thermostat", 3.6f, 4.4f, 1.1f, 79),
                // consumables
                new PieceDefinition { Id = FoilTape, Name = "Foil tape UL 181A-P, 2.5\" x 60 yd (~60 joints)", Category = PieceCategory.Consumable, Price = 24, UnitsPerPurchase = 60, Color = new Color(0.8f, 0.8f, 0.82f) },
                new PieceDefinition { Id = Screws, Name = "#8 x 1/2\" zip screws (box of 500)", Category = PieceCategory.Consumable, Price = 18, UnitsPerPurchase = 500, Color = Color.gray });
        }

        static RunSpec Rect(string id, string name, float wIn, float hIn, float cfm, string stock) => new RunSpec
        {
            Id = id, Name = name, Trade = Trade.Hvac, Kind = NetKind.Duct, Shape = RunShape.Rect,
            Width = In(wIn), Height = In(hIn), NominalIn = wIn, Surface = SurfaceKind.Galvanized, Tint = Galv,
            Join = JoinMethod.Tape, StockId = stock, FeetPerUnit = 5f, CutTool = "tin-snips", FittingPrice = 28, Cfm = cfm
        };

        static RunSpec Round(string id, string name, float dIn, string stock) => new RunSpec
        {
            Id = id, Name = name, Trade = Trade.Hvac, Kind = NetKind.Duct, Shape = RunShape.Round,
            Width = In(dIn), NominalIn = dIn, Surface = SurfaceKind.Galvanized, Tint = Galv, Join = JoinMethod.Tape,
            StockId = stock, FeetPerUnit = 5f, CutTool = "tin-snips", FittingPrice = 6, Cfm = RoundCfm(dIn)
        };

        static PieceDefinition D(string id, string name, float wIn, float hIn, float dIn, int price) => new PieceDefinition
        {
            Id = id, Name = name, Category = PieceCategory.Mechanical, Size = new Vector3(In(wIn), In(hIn), In(dIn)),
            Price = price, Cuttable = false, Color = new Color(0.75f, 0.77f, 0.78f)
        };
    }
}
