using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum PieceCategory { Lumber, Sheet, Consumable, Masonry, Rebar, Door, GarageDoor, DoorHardware, Window, Plumbing, Mechanical, Electrical, Appliance }

    /// <summary>A buildable/purchasable material. Geometry convention: Size = (width X, height Y, length Z) in meters;
    /// lumber's long axis is local Z. A 2x4 lying flat on its wide face: X=3.5", Y=1.5", Z=length.</summary>
    public class PieceDefinition
    {
        public string Id;
        public string Name;
        public PieceCategory Category;
        public Vector3 Size;          // meters, full stock size
        public int Price;             // dollars per unit (per box for consumables)
        public int UnitsPerPurchase = 1; // e.g. a box of nails = 1000
        public bool Cuttable;         // can be cut along Z with a saw
        public Color Color;

        public bool IsPlaceable => Category != PieceCategory.Consumable;
        /// <summary>Fixtures/products stand upright as defined: Size = (width X, height Y, depth Z). Not cuttable.</summary>
        public bool IsProduct => Category >= PieceCategory.Door;

        public static string CategoryLabel(PieceCategory c)
        {
            switch (c)
            {
                case PieceCategory.Lumber: return "Lumber";
                case PieceCategory.Sheet: return "Sheet goods";
                case PieceCategory.Consumable: return "Fasteners & bags";
                case PieceCategory.Masonry: return "Brick & block";
                case PieceCategory.Rebar: return "Rebar";
                case PieceCategory.Door: return "Doors";
                case PieceCategory.GarageDoor: return "Garage doors";
                case PieceCategory.DoorHardware: return "Door hardware";
                case PieceCategory.Window: return "Windows";
                case PieceCategory.Plumbing: return "Plumbing";
                case PieceCategory.Mechanical: return "HVAC";
                case PieceCategory.Electrical: return "Electrical";
                case PieceCategory.Appliance: return "Appliances";
                default: return c.ToString();
            }
        }
    }

    public static class Catalog
    {
        static PieceDefinition L(string id, string name, float wIn, float hIn, float lenIn, int price, Color c) =>
            new PieceDefinition { Id = id, Name = name, Category = PieceCategory.Lumber, Size = new Vector3(Units.In(wIn), Units.In(hIn), Units.In(lenIn)), Price = price, Cuttable = true, Color = c };

        static readonly Color Pine = new Color(0.86f, 0.72f, 0.50f);
        static readonly Color Treated = new Color(0.55f, 0.62f, 0.42f);
        static readonly Color Osb = new Color(0.78f, 0.62f, 0.38f);
        static readonly Color Ply = new Color(0.90f, 0.78f, 0.58f);

        public static readonly List<PieceDefinition> All = new List<PieceDefinition>
        {
            L("2x4x8",   "2x4 x 8'",            3.5f, 1.5f, 96f,  4,  Pine),
            L("2x4x925", "2x4 stud 92-5/8\"",   3.5f, 1.5f, 92.625f, 4, Pine),
            L("2x4x10",  "2x4 x 10'",           3.5f, 1.5f, 120f, 6,  Pine),
            L("2x6x8",   "2x6 x 8'",            5.5f, 1.5f, 96f,  7,  Pine),
            L("2x6x12",  "2x6 x 12'",           5.5f, 1.5f, 144f, 11, Pine),
            L("pt2x4x8", "PT 2x4 x 8' (sill)",  3.5f, 1.5f, 96f,  7,  Treated),
            L("4x4x8",   "4x4 post x 8'",       3.5f, 3.5f, 96f,  13, Treated),
            new PieceDefinition { Id = "osb716", Name = "OSB 7/16\" 4x8", Category = PieceCategory.Sheet, Size = new Vector3(Units.In(48), Units.In(0.4375f), Units.In(96)), Price = 16, Cuttable = true, Color = Osb },
            new PieceDefinition { Id = "ply34",  Name = "Plywood 3/4\" 4x8", Category = PieceCategory.Sheet, Size = new Vector3(Units.In(48), Units.In(0.75f), Units.In(96)), Price = 48, Cuttable = true, Color = Ply },
            new PieceDefinition { Id = "nails16d", Name = "16d framing nails (box of 1000)", Category = PieceCategory.Consumable, Price = 45, UnitsPerPurchase = 1000, Color = Color.gray },
            new PieceDefinition { Id = "nails8d",  Name = "8d sheathing nails (box of 1000)", Category = PieceCategory.Consumable, Price = 35, UnitsPerPurchase = 1000, Color = Color.gray },
            new PieceDefinition { Id = "mortar-s80", Name = "Type S mortar mix, 80 lb bag", Category = PieceCategory.Consumable, Price = 12, UnitsPerPurchase = 1, Color = new Color(0.62f, 0.62f, 0.60f) },
            new PieceDefinition { Id = "tiewire",    Name = "Rebar tie wire, 16 ga roll (3.5 lb)", Category = PieceCategory.Consumable, Price = 9, UnitsPerPurchase = 1, Color = new Color(0.35f, 0.35f, 0.36f) },

            // ---- Masonry: Size = (width X, height Y, length Z), laid flat like lumber ----
            M("brick-modular", "Red clay brick, modular",           3.625f, 2.25f, 7.625f, 1, new Color(0.62f, 0.26f, 0.18f)),
            M("brick-used",    "Tumbled 'used' brick",              3.625f, 2.25f, 7.625f, 1, new Color(0.55f, 0.30f, 0.22f)),
            M("cmu-8x8x16",    "Concrete block (CMU) 8x8x16",       7.625f, 7.625f, 15.625f, 2, new Color(0.62f, 0.62f, 0.60f)),
            M("cmu-half",      "Concrete block, half 8x8x8",        7.625f, 7.625f, 7.625f, 2, new Color(0.62f, 0.62f, 0.60f)),
            M("cmu-6x8x16",    "Concrete block (CMU) 6x8x16",       5.625f, 7.625f, 15.625f, 2, new Color(0.62f, 0.62f, 0.60f)),

            // ---- Rebar: 20' sticks, cuttable ----
            R("rebar-4", "#4 rebar (1/2\") x 20'", 0.5f, 240f, 9),
            R("rebar-5", "#5 rebar (5/8\") x 20'", 0.625f, 240f, 14),
            R("rebar-3", "#3 rebar (3/8\") x 10'", 0.375f, 120f, 5),

            // ---- Doors (prehung unless noted) ----
            P("door-ext-steel36",   "Exterior steel 6-panel 36x80",        PieceCategory.Door, 36, 80, 1.75f, 329, White),
            P("door-ext-fg36",      "Exterior fiberglass craftsman 36x80", PieceCategory.Door, 36, 80, 1.75f, 649, Oak),
            P("door-ext-french72",  "Exterior French patio door 72x80",    PieceCategory.Door, 72, 80, 1.75f, 1199, White),
            P("door-slider72",      "Sliding patio door 72x80 (vinyl)",    PieceCategory.Door, 72, 80, 4.5f, 899, White),
            P("door-int-6p24",      "Interior 6-panel 24x80",              PieceCategory.Door, 24, 80, 1.375f, 129, White),
            P("door-int-6p30",      "Interior 6-panel 30x80",              PieceCategory.Door, 30, 80, 1.375f, 139, White),
            P("door-int-6p32",      "Interior 6-panel 32x80",              PieceCategory.Door, 32, 80, 1.375f, 149, White),
            P("door-int-shaker32",  "Interior shaker 32x80",               PieceCategory.Door, 32, 80, 1.375f, 189, White),
            P("door-bifold36",      "Bifold closet door 36x80",            PieceCategory.Door, 36, 80, 1.125f, 99, White),
            P("door-barn36",        "Barn door slab + track 36x84",        PieceCategory.Door, 36, 84, 1.375f, 329, Walnut),
            P("door-pocket30",      "Pocket door kit 30x80",               PieceCategory.Door, 30, 80, 1.375f, 189, White),

            // ---- Garage doors ----
            P("garage-9x7",         "Garage door 9x7 steel",               PieceCategory.GarageDoor, 108, 84, 2f, 899, White),
            P("garage-16x7",        "Garage door 16x7 steel",              PieceCategory.GarageDoor, 192, 84, 2f, 1499, White),
            P("garage-16x7-carr",   "Garage door 16x7 insulated carriage", PieceCategory.GarageDoor, 192, 84, 2f, 2499, Walnut),
            P("garage-opener",      "Garage door opener 1/2 hp belt",      PieceCategory.GarageDoor, 15, 8, 40, 279, Steel),

            // ---- Door hardware ----
            P("hw-passage-knob",    "Passage knob, satin nickel",          PieceCategory.DoorHardware, 2.5f, 2.5f, 3, 19, Nickel),
            P("hw-privacy-knob",    "Privacy knob, satin nickel",          PieceCategory.DoorHardware, 2.5f, 2.5f, 3, 24, Nickel),
            P("hw-privacy-lever",   "Privacy lever, matte black",          PieceCategory.DoorHardware, 5, 2.5f, 3, 34, Black),
            P("hw-entry-combo",     "Entry lever + deadbolt combo",        PieceCategory.DoorHardware, 5, 7, 3, 89, Nickel),
            P("hw-handleset",       "Entry handleset, brass",              PieceCategory.DoorHardware, 3, 14, 3, 159, Brass),
            P("hw-smart-lock",      "Smart keypad deadbolt",               PieceCategory.DoorHardware, 3, 6, 3, 199, Black),
            P("hw-hinges",          "Door hinges 3-pack",                  PieceCategory.DoorHardware, 3.5f, 3.5f, 0.5f, 12, Nickel),

            // ---- Windows ----
            P("win-sh-2436",        "Single-hung vinyl 24x36",             PieceCategory.Window, 24, 36, 3.25f, 189, White),
            P("win-sh-3648",        "Single-hung vinyl 36x48",             PieceCategory.Window, 36, 48, 3.25f, 259, White),
            P("win-sh-3660",        "Single-hung vinyl 36x60",             PieceCategory.Window, 36, 60, 3.25f, 319, White),
            P("win-dh-3254",        "Double-hung vinyl 32x54",             PieceCategory.Window, 32, 54, 3.25f, 299, White),
            P("win-casement-2448",  "Casement 24x48",                      PieceCategory.Window, 24, 48, 3.25f, 389, White),
            P("win-slider-4836",    "Sliding window 48x36",                PieceCategory.Window, 48, 36, 3.25f, 279, White),
            P("win-picture-6048",   "Picture window 60x48",                PieceCategory.Window, 60, 48, 3.25f, 449, White),
            P("win-bay-7248",       "Bay window 72x48",                    PieceCategory.Window, 72, 48, 18, 1899, White),
            P("win-hopper-3216",    "Basement hopper 32x16",               PieceCategory.Window, 32, 16, 3.25f, 149, White),
            P("win-skylight",       "Skylight 22x46",                      PieceCategory.Window, 22, 6, 46, 399, Steel),

            // ---- Plumbing ----
            P("wh-gas50",           "Water heater 50 gal gas",             PieceCategory.Plumbing, 20, 60, 20, 1149, White),
            P("wh-elec50",          "Water heater 50 gal electric",        PieceCategory.Plumbing, 20, 58, 20, 699, White),
            P("wh-tankless",        "Tankless gas water heater",           PieceCategory.Plumbing, 14, 27, 10, 1299, Steel),
            P("pl-toilet",          "Toilet, elongated",                   PieceCategory.Plumbing, 20, 30, 28, 219, White),
            P("pl-vanity36",        "Bath vanity 36\" with sink",          PieceCategory.Plumbing, 36, 34, 21, 449, Oak),
            P("pl-kitchen-sink",    "Kitchen sink, double bowl",           PieceCategory.Plumbing, 33, 10, 22, 189, Steel),
            P("pl-tub60",           "Alcove bathtub 60\"",                 PieceCategory.Plumbing, 60, 16, 30, 349, White),
            P("pl-shower48",        "Shower stall kit 48\"",               PieceCategory.Plumbing, 48, 78, 36, 599, White),
            P("pl-sump",            "Sump pump 1/3 hp",                    PieceCategory.Plumbing, 10, 12, 10, 159, Black),

            // ---- HVAC ----
            P("hvac-ac3t",          "AC condenser 3-ton 16 SEER",          PieceCategory.Mechanical, 30, 32, 30, 3199, Gray),
            P("hvac-hp3t",          "Heat pump 3-ton",                     PieceCategory.Mechanical, 30, 36, 30, 4299, Gray),
            P("hvac-furnace80",     "Gas furnace 80k BTU",                 PieceCategory.Mechanical, 21, 40, 29, 1699, Gray),
            P("hvac-airhandler",    "Air handler",                         PieceCategory.Mechanical, 21, 48, 21, 1399, Gray),
            P("hvac-minisplit",     "Mini-split 12k BTU (head + outdoor)", PieceCategory.Mechanical, 32, 11, 8, 1099, White),
            P("hvac-bathfan",       "Bath exhaust fan",                    PieceCategory.Mechanical, 10, 6, 10, 79, White),
            P("hvac-rangehood",     "Range hood 30\"",                     PieceCategory.Mechanical, 30, 6, 20, 199, Steel),

            // ---- Electrical ----
            P("el-panel200",        "200A main breaker panel",             PieceCategory.Electrical, 14, 30, 4, 349, Gray),
            P("el-meter200",        "200A meter base",                     PieceCategory.Electrical, 11, 15, 4, 149, Gray),
            P("el-ceiling-light",   "Ceiling light fixture",               PieceCategory.Electrical, 12, 5, 12, 49, White),
            P("el-ceiling-fan",     "Ceiling fan 52\"",                    PieceCategory.Electrical, 52, 12, 52, 179, Walnut),
            P("el-smoke",           "Smoke/CO detector",                   PieceCategory.Electrical, 5, 2, 5, 29, White),
            P("el-porch-light",     "Outdoor wall light",                  PieceCategory.Electrical, 7, 12, 8, 59, Black),

            // ---- Appliances ----
            P("ap-fridge",          "Refrigerator 36\"",                   PieceCategory.Appliance, 36, 70, 30, 1499, Steel),
            P("ap-range",           "Range 30\" gas",                      PieceCategory.Appliance, 30, 47, 28, 899, Steel),
            P("ap-dishwasher",      "Dishwasher 24\"",                     PieceCategory.Appliance, 24, 34, 24, 649, Steel),
            P("ap-washer",          "Washer",                              PieceCategory.Appliance, 27, 39, 30, 799, White),
            P("ap-dryer",           "Dryer",                               PieceCategory.Appliance, 27, 39, 30, 749, White),
        };

        static Color White => new Color(0.93f, 0.93f, 0.91f);
        static Color Oak => new Color(0.72f, 0.52f, 0.30f);
        static Color Walnut => new Color(0.40f, 0.27f, 0.17f);
        static Color Steel => new Color(0.70f, 0.72f, 0.74f);
        static Color Gray => new Color(0.55f, 0.57f, 0.58f);
        static Color Nickel => new Color(0.78f, 0.78f, 0.76f);
        static Color Black => new Color(0.12f, 0.12f, 0.13f);
        static Color Brass => new Color(0.80f, 0.66f, 0.30f);

        static PieceDefinition M(string id, string name, float wIn, float hIn, float lenIn, int price, Color c) =>
            new PieceDefinition { Id = id, Name = name, Category = PieceCategory.Masonry, Size = new Vector3(Units.In(wIn), Units.In(hIn), Units.In(lenIn)), Price = price, Cuttable = false, Color = c };

        static PieceDefinition R(string id, string name, float diaIn, float lenIn, int price) =>
            new PieceDefinition { Id = id, Name = name, Category = PieceCategory.Rebar, Size = new Vector3(Units.In(diaIn), Units.In(diaIn), Units.In(lenIn)), Price = price, Cuttable = true, Color = new Color(0.42f, 0.27f, 0.18f) };

        /// <summary>Product/fixture: Size = (width, height, depth) inches, stands upright, not cuttable.</summary>
        static PieceDefinition P(string id, string name, PieceCategory cat, float wIn, float hIn, float dIn, int price, Color c) =>
            new PieceDefinition { Id = id, Name = name, Category = cat, Size = new Vector3(Units.In(wIn), Units.In(hIn), Units.In(dIn)), Price = price, Cuttable = false, Color = c };

        /// <summary>Extension point: feature modules add their materials/products from their own files.
        /// Call from [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]. Duplicate ids are replaced.</summary>
        public static void Register(params PieceDefinition[] defs)
        {
            foreach (var d in defs)
            {
                int i = All.FindIndex(x => x.Id == d.Id);
                if (i >= 0) All[i] = d; else All.Add(d);
            }
        }

        public static PieceDefinition Get(string id) => All.Find(p => p.Id == id);
    }
}
