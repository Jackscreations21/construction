using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Plumbing trade registration: pipe run specs (PEX-B, copper type L, PVC/ABS DWV), store catalog
    /// (coils, sticks, consumables, parts) and the tool-belt items. Prices are typical US big-box prices.</summary>
    public static class PlumbCatalog
    {
        public const string CrimpRings = "pex-crimp-rings", Solder = "solder", Cement = "pvc-cement", TestCaps = "pl-test-caps";

        // run spec ids
        public const string Pex12Red = "pex-12-red", Pex12Blue = "pex-12-blue", Pex34Red = "pex-34-red", Pex34Blue = "pex-34-blue";
        public const string Cu12 = "cu-l-12", Cu34 = "cu-l-34";
        public const string Pvc15 = "pvc-15", Pvc2 = "pvc-2", Pvc3 = "pvc-3", Abs3 = "abs-3";
        public const string Pvc15Vent = "pvc-15-vent", Pvc2Vent = "pvc-2-vent", Pvc3Vent = "pvc-3-vent";

        // placeable parts
        public const string Manifold = "pl-pex-manifold", Shutoff = "pl-shutoff", PTrap = "pl-ptrap", Cleanout = "pl-cleanout",
            HoseBib = "pl-hosebib", ClosetFlange = "pl-closet-flange", VentBoot = "pl-vent-boot", Gauge = "pl-test-gauge";

        static float I(float inches) => Units.In(inches);

        static RunSpec Pex(string id, string name, float nom, float odIn, bool hot, Color tint, string stock)
        {
            return new RunSpec
            {
                Id = id, Name = name, Trade = Trade.Plumbing, Kind = NetKind.Supply, Width = I(odIn), NominalIn = nom,
                Surface = SurfaceKind.PaintedWhite, Tint = tint, Join = JoinMethod.Crimp, BendsInline = true,
                MinBendRadius = I(odIn * 8f),             // PEX-B: 8 x OD cold-bend radius
                StockId = stock, FeetPerUnit = 100, CutTool = "tubing-cutter", FittingPrice = 4, Hot = hot,
            };
        }

        static RunSpec Copper(string id, string name, float nom, float odIn, string stock)
        {
            return new RunSpec
            {
                Id = id, Name = name, Trade = Trade.Plumbing, Kind = NetKind.Supply, Width = I(odIn), NominalIn = nom,
                Surface = SurfaceKind.Brass, Tint = MepLook.Copper, Join = JoinMethod.Sweat, MinBendRadius = I(odIn * 1.5f),
                StockId = stock, FeetPerUnit = 10, CutTool = "tubing-cutter", FittingPrice = 3,
            };
        }

        static RunSpec Dwv(string id, string name, NetKind kind, float nom, float odIn, bool abs, string stock)
        {
            return new RunSpec
            {
                Id = id, Name = name, Trade = Trade.Plumbing, Kind = kind, Width = I(odIn), NominalIn = nom,
                Surface = abs ? SurfaceKind.DarkPlastic : SurfaceKind.PaintedWhite, Tint = abs ? Color.white : MepLook.PvcWhite,
                Join = JoinMethod.Solvent, MinBendRadius = I(odIn), StockId = stock, FeetPerUnit = 10,
                CutTool = "pvc-saw", FittingPrice = nom >= 3f ? 9 : nom >= 2f ? 5 : 3,
            };
        }

        public static IEnumerable<RunSpec> Specs()
        {
            yield return Pex(Pex12Blue, "PEX-B 1/2\" blue (cold)", 0.5f, 0.625f, false, MepLook.PexBlue, "pex-12-blue-100");
            yield return Pex(Pex12Red, "PEX-B 1/2\" red (hot)", 0.5f, 0.625f, true, MepLook.PexRed, "pex-12-red-100");
            yield return Pex(Pex34Blue, "PEX-B 3/4\" blue (cold)", 0.75f, 0.875f, false, MepLook.PexBlue, "pex-34-blue-100");
            yield return Pex(Pex34Red, "PEX-B 3/4\" red (hot)", 0.75f, 0.875f, true, MepLook.PexRed, "pex-34-red-100");
            yield return Copper(Cu12, "Copper type L 1/2\"", 0.5f, 0.625f, "cu-l-12-10");
            yield return Copper(Cu34, "Copper type L 3/4\"", 0.75f, 0.875f, "cu-l-34-10");
            yield return Dwv(Pvc15, "PVC DWV 1-1/2\" (drain)", NetKind.Drain, 1.5f, 1.9f, false, "pvc-15-10");
            yield return Dwv(Pvc2, "PVC DWV 2\" (drain)", NetKind.Drain, 2f, 2.375f, false, "pvc-2-10");
            yield return Dwv(Pvc3, "PVC DWV 3\" (drain)", NetKind.Drain, 3f, 3.5f, false, "pvc-3-10");
            yield return Dwv(Pvc15Vent, "PVC DWV 1-1/2\" (vent)", NetKind.Vent, 1.5f, 1.9f, false, "pvc-15-10");
            yield return Dwv(Pvc2Vent, "PVC DWV 2\" (vent)", NetKind.Vent, 2f, 2.375f, false, "pvc-2-10");
            yield return Dwv(Pvc3Vent, "PVC DWV 3\" (vent)", NetKind.Vent, 3f, 3.5f, false, "pvc-3-10");
            yield return Dwv(Abs3, "ABS DWV 3\" (drain)", NetKind.Drain, 3f, 3.5f, true, "abs-3-10");
        }

        static PieceDefinition D(string id, string name, float wIn, float hIn, float dIn, int price, Color c, PieceCategory cat = PieceCategory.Plumbing, int units = 1) =>
            new PieceDefinition { Id = id, Name = name, Category = cat, Size = new Vector3(I(wIn), I(hIn), I(dIn)), Price = price, UnitsPerPurchase = units, Color = c };

        public static IEnumerable<PieceDefinition> Defs()
        {
            Color red = MepLook.PexRed, blue = MepLook.PexBlue, cu = new Color(0.85f, 0.55f, 0.35f), white = new Color(0.95f, 0.95f, 0.95f), black = new Color(0.12f, 0.12f, 0.12f);
            // ---- pipe stock (coils counted per 100' coil, sticks per 10' stick)
            yield return D("pex-12-blue-100", "PEX-B 1/2\" blue, 100' coil", 18, 4, 18, 42, blue);
            yield return D("pex-12-red-100", "PEX-B 1/2\" red, 100' coil", 18, 4, 18, 42, red);
            yield return D("pex-34-blue-100", "PEX-B 3/4\" blue, 100' coil", 22, 5, 22, 79, blue);
            yield return D("pex-34-red-100", "PEX-B 3/4\" red, 100' coil", 22, 5, 22, 79, red);
            yield return D("cu-l-12-10", "Copper type L 1/2\" x 10' stick", 0.625f, 0.625f, 120, 34, cu);
            yield return D("cu-l-34-10", "Copper type L 3/4\" x 10' stick", 0.875f, 0.875f, 120, 58, cu);
            yield return D("pvc-15-10", "PVC Sch 40 DWV 1-1/2\" x 10'", 1.9f, 1.9f, 120, 14, white);
            yield return D("pvc-2-10", "PVC Sch 40 DWV 2\" x 10'", 2.375f, 2.375f, 120, 19, white);
            yield return D("pvc-3-10", "PVC Sch 40 DWV 3\" x 10'", 3.5f, 3.5f, 120, 34, white);
            yield return D("abs-3-10", "ABS DWV 3\" x 10' (black)", 3.5f, 3.5f, 120, 38, black);
            // ---- consumables (JoinTool takes one per joint)
            yield return D(CrimpRings, "PEX copper crimp rings 1/2\"+3/4\" (bag of 100)", 0, 0, 0, 24, cu, PieceCategory.Consumable, 100);
            yield return D(Solder, "Lead-free solder 1 lb + flux + brush (~80 joints)", 0, 0, 0, 39, new Color(0.7f, 0.7f, 0.7f), PieceCategory.Consumable, 80);
            yield return D(Cement, "PVC purple primer + cement, pint each (~60 joints)", 0, 0, 0, 19, new Color(0.55f, 0.25f, 0.75f), PieceCategory.Consumable, 60);
            yield return D(TestCaps, "Test caps & plugs assortment (stub-outs, 1/2\"-3\")", 0, 0, 0, 15, new Color(0.9f, 0.4f, 0.1f), PieceCategory.Consumable, 20);
            // ---- placeable plumbing parts (auto-attached nodes + real-size models)
            yield return D(Manifold, "PEX manifold 3/4\" x 4 x 1/2\" with valves", 12, 4, 3, 59, cu);
            yield return D(Shutoff, "Main shutoff 3/4\" full-port ball valve (PEX)", 2.5f, 4.5f, 2.5f, 21, new Color(0.8f, 0.66f, 0.3f));
            yield return D(PTrap, "PVC P-trap 1-1/2\" (glue)", 2.4f, 7.6f, 9f, 9, white);
            yield return D(Cleanout, "Cleanout adapter 3\" + threaded plug", 4.5f, 4.5f, 6f, 11, white);
            yield return D(HoseBib, "Frost-free sillcock 1/2\" (hose bib)", 3f, 4f, 10f, 29, new Color(0.8f, 0.66f, 0.3f));
            yield return D(ClosetFlange, "PVC closet flange 3\" with test cap", 7f, 3f, 7f, 12, white);
            yield return D(VentBoot, "Vent through roof: pipe flashing boot", 12f, 3f, 12f, 16, black);
            yield return D(Gauge, "Test gauge 0-160 psi + pump fitting", 3f, 6f, 3f, 39, new Color(0.8f, 0.66f, 0.3f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            foreach (var s in Specs()) RunSpecs.Register(s);
            foreach (var d in Defs()) Catalog.Register(d);
            ToolFactory.Register(new ToolShopItem { Id = "test-gauge", Name = "Test gauge + hand pump", Price = 45, Description = "Pressure-test supply piping (100 psi water / 60 psi air for 15 min). RMB caps an open stub-out." },
                () => new PressureGaugeTool());
            Tablet.AddHelp("Plumbing: route PEX/copper/PVC with Pipe Layout · crimp/sweat/glue every joint · cap stubs (test gauge RMB) · pump the gauge (LMB) and hold 15 min · water-test the DWV from the MEP Plans tablet");
        }

        public static bool IsSupply(RunSpec s) => s != null && s.Trade == Trade.Plumbing && s.Kind == NetKind.Supply;
        public static bool IsDwv(RunSpec s) => s != null && s.Trade == Trade.Plumbing && (s.Kind == NetKind.Drain || s.Kind == NetKind.Vent);
        public static bool IsSupply(NetRun r) => r != null && IsSupply(r.Spec);
        public static bool IsDwv(NetRun r) => r != null && IsDwv(r.Spec);
    }
}
