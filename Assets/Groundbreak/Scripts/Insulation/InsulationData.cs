using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A fiberglass batt / roll product.</summary>
    public class BattProduct
    {
        public string Id, Name;
        public float R, ThickIn, WidthIn, LengthIn;   // LengthIn = precut length (93") or roll length
        public bool Faced, Roll, Yellow, HighDensity;
        public int Price, PerBag;
        public float Thick => Units.In(ThickIn);
        public float Width => Units.In(WidthIn);
        public float Length => Units.In(LengthIn);
        public string Short => $"R-{R:0} {(Faced ? "kraft" : "unfaced")} {WidthIn:0}\"{(Roll ? " roll" : "")}";
    }

    /// <summary>
    /// Insulation code targets and climate as DATA (IRC N1102 / R402 style). Change these to model another
    /// climate zone; inspections and tools read them live.
    /// </summary>
    public static class InsulationRules
    {
        /// <summary>IECC climate zone 1..8. Zones 5+ (and marine 4C) need a Class I/II vapor retarder on the warm side.</summary>
        public static int ClimateZone = 4;
        /// <summary>Cold climate = vapor retarder required on exterior walls (kraft facing or 6-mil poly).</summary>
        public static bool ColdClimate { get => ClimateZone >= 5; set => ClimateZone = value ? 6 : 4; }
        public static float WallMinR = 13f;          // exterior wall cavity
        public static float AtticMinR = 38f;         // ceiling / attic
        public static float LooseFillRPerInch = 2.5f; // blown fiberglass
        public static float ClosedCellRPerInch = 6.5f;
        public static float RulerEverySqFt = 300f;   // attic depth markers: one per 300 sq ft
        public static float StapleSpacingIn = 8f;    // kraft flanges
        public static float PolyLapIn = 6f;          // poly seams lapped 6"
        public static bool RequireBoxAirSeal = true; // boxes in exterior walls sealed (air barrier)
        public static float EaveBaffleReach = 0.35f; // a ceiling bay end this close to an exterior wall is an eave bay (m)
    }

    public static partial class Insulation
    {
        public static readonly List<BattProduct> Products = new List<BattProduct>
        {
            B("ins-r13-k15", "R-13 kraft-faced batt 15\" x 93\" (bag of 10)", 13, 3.5f, 15, 93, true, false, false, 55, 10),
            B("ins-r13-u15", "R-13 unfaced batt 15\" x 93\" (bag of 10)", 13, 3.5f, 15, 93, false, false, false, 49, 10),
            B("ins-r13-k23", "R-13 kraft-faced batt 23\" x 93\" (bag of 10)", 13, 3.5f, 23, 93, true, false, false, 79, 10),
            B("ins-r13-u23", "R-13 unfaced batt 23\" x 93\" (bag of 10)", 13, 3.5f, 23, 93, false, false, false, 72, 10),
            B("ins-r15-k15", "R-15 high-density kraft batt 15\" x 93\" (bag of 10)", 15, 3.5f, 15, 93, true, false, true, 75, 10),
            B("ins-r19-k15", "R-19 kraft-faced batt 15\" x 93\" (bag of 8)", 19, 6.25f, 15, 93, true, false, false, 62, 8),
            B("ins-r19-k23", "R-19 kraft-faced batt 23\" x 93\" (bag of 8)", 19, 6.25f, 23, 93, true, false, false, 88, 8),
            B("ins-r21-k15", "R-21 high-density kraft batt 15\" x 93\" (bag of 8)", 21, 5.5f, 15, 93, true, false, true, 79, 8),
            B("ins-r30-k15", "R-30 kraft-faced roll 15\" x 25'", 30, 10f, 15, 300, true, true, false, 49, 1),
            B("ins-r30-u23", "R-30 unfaced roll 23\" x 25'", 30, 10f, 23, 300, false, true, false, 69, 1),
            B("ins-r38-u15", "R-38 unfaced roll 15\" x 25'", 38, 12f, 15, 300, false, true, false, 59, 1),
            B("ins-r38-u23", "R-38 unfaced roll 23\" x 25'", 38, 12f, 23, 300, false, true, false, 84, 1),
        };

        static BattProduct B(string id, string name, float r, float t, float w, float l, bool faced, bool roll, bool hd, int price, int per) =>
            new BattProduct { Id = id, Name = name, R = r, ThickIn = t, WidthIn = w, LengthIn = l, Faced = faced, Roll = roll, HighDensity = hd, Yellow = hd, Price = price, PerBag = per };

        public static BattProduct Product(string id) => Products.Find(p => p.Id == id);

        // consumables
        public const string FoamKitId = "ins-foamkit", CanFoamId = "ins-canfoam", CanFoamWwId = "ins-canfoam-ww", FireCaulkId = "ins-firecaulk",
            BaffleId = "ins-baffle", PolyId = "ins-poly6", LooseFillId = "ins-loosefill", RulerId = "ins-ruler", StaplesId = "ins-staples";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterCatalog()
        {
            var ids = new List<string>();
            foreach (var p in Products)
            {
                var c = p.Faced ? new Color(0.66f, 0.49f, 0.31f) : p.Yellow ? new Color(0.95f, 0.86f, 0.55f) : new Color(0.96f, 0.62f, 0.66f);
                Catalog.Register(FinishStock.Item(p.Id, p.Name, p.Price, p.PerBag, c, p.WidthIn, p.ThickIn, p.LengthIn));
                ids.Add(p.Id);
            }
            Catalog.Register(
                FinishStock.Item(FoamKitId, "Closed-cell spray foam refill, 2-part (200 board-ft)", 420, 200, new Color(0.93f, 0.84f, 0.56f)),
                FinishStock.Item(CanFoamId, "Gaps & cracks foam sealant, 12 oz can", 7, 1, new Color(0.95f, 0.88f, 0.62f)),
                FinishStock.Item(CanFoamWwId, "Window & door low-expansion foam, 12 oz can", 9, 1, new Color(0.95f, 0.9f, 0.7f)),
                FinishStock.Item(FireCaulkId, "Fire caulk (red), 10.1 oz tube", 12, 1, new Color(0.8f, 0.1f, 0.08f)),
                FinishStock.Item(BaffleId, "Eave baffle, foam 22.5\" x 48\"", 2, 1, new Color(0.95f, 0.95f, 0.96f), 22.5f, 1.5f, 48f),
                FinishStock.Item(PolyId, "6-mil poly sheeting 10' x 100' roll", 92, 1000, new Color(0.85f, 0.87f, 0.9f)),
                FinishStock.Item(LooseFillId, "Loose-fill fiberglass, 28 lb bag", 42, 1, new Color(0.95f, 0.9f, 0.88f)),
                FinishStock.Item(RulerId, "Attic depth rulers (10-pack)", 8, 10, new Color(0.93f, 0.92f, 0.88f)),
                FinishStock.Item(StaplesId, "T50 staples 3/8\" (box of 1250)", 6, 1250, new Color(0.78f, 0.79f, 0.8f)));
            ids.AddRange(new[] { FoamKitId, CanFoamId, CanFoamWwId, FireCaulkId, BaffleId, PolyId, LooseFillId, RulerId, StaplesId });
            FinishSupply.Add("Insulation", ids.ToArray());

            ToolFactory.Register(new ToolShopItem { Id = "utility-knife", Name = "Utility Knife", Price = 12, Description = "Cuts batts to the bay (score against a straightedge), splits them around boxes and pipes; scores drywall with the T-square." }, () => new UtilityKnife());
            ToolFactory.Register(new ToolShopItem { Id = "staple-gun", Name = "Staple Gun (T50)", Price = 32, Description = "Staples kraft flanges, eave baffles, poly vapor barrier and attic rulers." }, () => new StapleGun());
            ToolFactory.Register(new ToolShopItem { Id = "foam-gun", Name = "Foam Gun", Price = 39, Description = "Can-foam gun: air-seals boxes, plate penetrations and window/door shim gaps." }, () => new FoamGun());
            ToolFactory.Register(new ToolShopItem { Id = "spray-foam-kit", Name = "Spray Foam Kit (gun + hoses)", Price = 85, Description = "2-part closed-cell spray foam rig: ~2\" lumpy foam per pass (rim joists, odd bays). Uses foam refills." }, () => new SprayFoamKit());
            ToolFactory.Register(new ToolShopItem { Id = "insulation-blower", Name = "Insulation Blower (rental)", Price = 120, Description = "Rental blowing machine + hose (flat fee). Hold on the attic to blow loose-fill fiberglass." }, () => new InsulationBlower());

            CaulkGun.RegisterTube(new CaulkGun.Tube { Name = "Fire caulk (red)", StockId = FireCaulkId, BeadColor = new Color(0.78f, 0.08f, 0.06f), Apply = (hit, gun) => FireCaulkAt(hit, gun) });

            Tablet.AddHelp("Insulation: utility knife (RMB = batt type) fits a batt into the aimed bay; click it again to split around boxes/pipes. Staple gun staples kraft flanges, baffles, poly, rulers. Foam gun air-seals boxes/penetrations, caulk gun 'Fire caulk' fire-blocks plate penetrations. Blower: hold on the attic.");
        }
    }
}
