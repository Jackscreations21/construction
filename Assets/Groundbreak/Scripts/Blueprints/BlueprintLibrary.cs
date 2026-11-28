using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The plan library: every Blueprint, drawn room by room in plan feet (x left→right seen from the street, y back from the front wall).
    /// Rooms tile each floor; walls, openings, stairs and MEP fixtures are derived by <see cref="Blueprint.Finish"/>. Dimensions follow
    /// common US practice: IRC R304 rooms, R311 stairs (7-3/4" risers, 10" treads, 36" wide), R310 egress windows, 10'x20' parking bays,
    /// IBC 1020 44" corridors and 1011 stairs (7" / 11"), ADA single-user restrooms (60" turning circle). See Docs/BLUEPRINTS.md.</summary>
    public static class BlueprintLibrary
    {
        public static void Define(List<Blueprint> l)
        {
            // ------------------------------------------------------------ residential: Development's ids redrawn with stairs/halls
            l.Add(Ranch1600());
            l.Add(Cottage1200());
            l.Add(Craftsman1920());
            l.Add(TwoStory("twostory-2400", "The Elm (two-story)", "Colonial", 30f, 30f, 9f, true, FoundationType.Basement, 0.7f));
            l.Add(Estate3500());
            // ------------------------------------------------------------ starter / ranch / two-story / split
            l.Add(Starter1008());
            l.Add(Ranch("ranch-1500", "The Juniper (ranch)", 0, 21, false, 0.5f));
            l.Add(Ranch("ranch-1650-1car", "The Maple (ranch, 1-car)", 14, 26, false, 0.55f));
            l.Add(Ranch("ranch-1650-2car", "The Oak (ranch, 2-car)", 22, 26, false, 0.6f));
            l.Add(Ranch("ranch-1770-2car", "The Pine (ranch, 2-car)", 22, 30, false, 0.6f));
            l.Add(Ranch("ranch-1770-3car", "The Spruce (ranch, 3-car, 4 bed)", 32, 30, true, 0.65f));
            l.Add(TwoStory("twostory-2240", "The Hickory (two-story)", "Traditional", 28f, 28f, 8f, false, FoundationType.Crawlspace, 0.6f));
            l.Add(SplitLevel2280());
            // ------------------------------------------------------------ multi-family / accessory dwellings
            l.Add(Duplex1664());
            l.Add(TownhomeRow("townhome-row4", "Sycamore Row (4 townhomes)", 4));
            l.Add(TownhomeRow("townhome-row6", "Sycamore Row (6 townhomes)", 6));
            l.Add(Adu480());
            l.Add(Carriage672());
            // ------------------------------------------------------------ outbuildings
            l.Add(Garage("garage-1car", "1-car detached garage", 14, 24, new[] { (7f, "garage-9x7") }, "2x4"));
            l.Add(Garage("garage-2car", "2-car detached garage", 24, 24, new[] { (12f, "garage-16x7") }, "2x4"));
            l.Add(Garage2CarShop());
            l.Add(Garage("garage-3car", "3-car detached garage", 34, 24, new[] { (11f, "garage-16x7"), (27f, "garage-9x7") }, "2x6"));
            l.Add(Shed("shed-8x10", "8x10 storage shed", 10, 8, CompanyStage.Handyman, FoundationType.Skids));
            l.Add(Shed("shed-10x12", "10x12 storage shed", 12, 10, CompanyStage.Contractor, FoundationType.Skids));
            l.Add(Shed("shed-12x16", "12x16 storage shed", 12, 16, CompanyStage.Contractor, FoundationType.Slab));
            l.Add(PoleBarn30x40());
            l.Add(PoleBarn40x60());
            l.Add(PoleBarn60x80());
            l.Add(Barn36x48());
            l.Add(Workshop30x40());
            l.Add(Workshop40x60());
            // ------------------------------------------------------------ commercial
            l.Add(Retail("retail-2400", "Main Street retail shop", 40, 60, 40));
            l.Add(Retail("retail-5000", "Retail store (5,000 sq ft)", 50, 100, 70));
            l.Add(Diner3000());
            l.Add(Office3000());
            l.Add(StripMall6000());
            l.Add(GasStation3200());
            l.Add(Storefront("storefront-24", "Main Street storefront 24' (shop + apartment above)", 24f));
            l.Add(Storefront("storefront-30", "Main Street storefront 30' (shop + apartment above)", 30f));
            l.Add(Bank2800());
            l.Add(Grocery20000());
            // ------------------------------------------------------------ industrial
            l.Add(Warehouse("warehouse-6000", "Warehouse 100x60", 100, 60, 20, 24, 0, CompanyStage.Builder,
                front: new[] { 50f, 75f }, frontDoor: "oh-12x14", docks: new float[0]));
            l.Add(Warehouse("warehouse-12000", "Warehouse 120x100 (3 docks)", 120, 100, 24, 30, 0, CompanyStage.Builder,
                front: new[] { 60f, 90f }, frontDoor: "oh-14x14", docks: new[] { 30f, 45f, 60f }));
            l.Add(Warehouse("warehouse-24000", "Distribution warehouse 200x120 (6 docks)", 200, 120, 28, 30, 1, CompanyStage.Developer,
                front: new[] { 70f, 100f, 130f, 160f }, frontDoor: "oh-14x14", docks: new[] { 40f, 55f, 70f, 85f, 100f, 115f }));
            l.Add(Warehouse("warehouse-50000", "Distribution center 250x200 (8 docks)", 250, 200, 32, 40, 2, CompanyStage.Developer,
                front: new[] { 80f, 120f, 160f, 200f }, frontDoor: "oh-14x14", docks: new[] { 40f, 55f, 70f, 85f, 100f, 115f, 130f, 145f }));
            l.Add(Flex6000());
            l.Add(YardBay1500());
            l.Add(YardShop2400());
            l.Add(YardHq7200());
        }

        // ================================================================== factories

        static Blueprint House(string id, string name, string style, string cat, float w, float d, int beds, int baths2, float quality, CompanyStage stage = CompanyStage.Builder) =>
            new Blueprint
            {
                Id = id, Name = name, Style = style, Category = cat, Group = PlanGroup.Residential, MinStage = stage, WidthFt = w, DepthFt = d,
                Beds = beds, Baths2 = baths2, BaseQuality = quality, StudSize = "2x6", Occupancy = "IRC R-3 (one-family dwelling)", ConstructionType = "V-B (wood frame)",
            };

        static Blueprint Outbuilding(string id, string name, string style, string cat, float w, float d, CompanyStage stage) =>
            new Blueprint
            {
                Id = id, Name = name, Style = style, Category = cat, Group = PlanGroup.Outbuilding, MinStage = stage, WidthFt = w, DepthFt = d,
                BaseQuality = 0.5f, StudSize = "2x4", Occupancy = "IRC accessory structure (U)", ConstructionType = "V-B (wood frame)",
                Finished = false, Conditioned = false, RidgeAlongY = true, Pitch = 6f, WallHeightFt = 8f,
            };

        static Blueprint Commercial(string id, string name, string style, string cat, float w, float d, string occupancy, CompanyStage stage) =>
            new Blueprint
            {
                Id = id, Name = name, Style = style, Category = cat, Group = PlanGroup.Commercial, MinStage = stage, WidthFt = w, DepthFt = d,
                BaseQuality = 0.6f, StudSize = "2x6", Occupancy = occupancy, ConstructionType = "II-B (noncombustible: CMU + steel joists)",
                WallFrame = WallSystem.Cmu, RoofFrame = RoofSystem.BarJoist, Roof = RoofType.Flat, Covering = RoofCovering.Tpo, Pitch = 0.25f,
                WallHeightFt = 14f, SlabIn = 4f,
            };

        static Blueprint Industrial(string id, string name, string cat, float w, float d, float eave, CompanyStage stage) =>
            new Blueprint
            {
                Id = id, Name = name, Style = "Pre-engineered metal building", Category = cat, Group = PlanGroup.Industrial, MinStage = stage,
                WidthFt = w, DepthFt = d, BaseQuality = 0.5f, StudSize = "2x6", Occupancy = "IBC S-1 (moderate-hazard storage)",
                ConstructionType = "II-B (pre-engineered steel)", WallFrame = WallSystem.SteelFrame, RoofFrame = RoofSystem.RigidFrame,
                Covering = RoofCovering.StandingSeam, Pitch = 1f, WallHeightFt = eave, SlabIn = 6f, Finished = false,
            };

        // mirrored units (duplex / townhomes): rooms are drawn in unit coordinates and flipped for every other unit
        static void UR(Blueprint b, float a, float uw, bool m, string n, float x0, float y0, float x1, float y1, int f = 0) =>
            b.R(n, m ? a + uw - x1 : a + x0, y0, m ? a + uw - x0 : a + x1, y1, f);
        static float UX(float a, float uw, bool m, float x) => m ? a + uw - x : a + x;

        // ================================================================== residential

        /// <summary>Ranch: [garage] | public module (great room, laundry, pantry, kitchen, dining) | bedroom wing, all 30' deep.</summary>
        static Blueprint Ranch(string id, string name, float gw, float pw, bool bed4, float q)
        {
            float W = gw + pw + 29f;
            var b = House(id, name, "Ranch", "ranch", W, 30f, bed4 ? 4 : 3, 4, q);
            b.WallHeightFt = gw > 0 ? 9f : 8f; b.Roof = RoofType.Hip; b.Pitch = 6f;
            if (gw > 0)
            {
                b.R("Garage", 0, 0, gw, 30);
                if (gw >= 30) b.Door(0, 10.5f, "garage-16x7").Door(0, 25.5f, "garage-9x7");
                else b.Door(0, gw / 2f, gw >= 20 ? "garage-16x7" : "garage-9x7");
                b.Door(3, 15f, "door-ext-steel36");   // garage service door
            }
            float x = gw;
            b.R("Great room", x, 0, x + pw, 16);
            b.R("Laundry", x, 16, x + 6, 24).R("Pantry", x, 24, x + 6, 30);
            if (pw < 24) b.R("Kitchen / nook", x + 6, 16, x + pw, 30);
            else { float k = Mathf.Floor((pw - 6f) / 2f); b.R("Kitchen", x + 6, 16, x + 6 + k, 30).R("Dining", x + 6 + k, 16, x + pw, 30); }
            float a = gw + pw;
            b.R("Bed 2", a, 0, a + 12, 13).R("Closet", a + 12, 0, a + 17, 5).R("Bath", a + 12, 5, a + 17, 13).R("Bed 3", a + 17, 0, a + 29, 13);
            b.R("Hall", a, 13, a + 21, 17).R("WIC (master)", a + 21, 13, a + 29, 17);
            b.R(bed4 ? "Bed 4" : "Office", a, 17, a + 8, 30).R("Master Bath", a + 8, 17, a + 16, 30).R("Master", a + 16, 17, a + 29, 30);
            b.FrontDoorFt = gw + pw - 3f;
            b.Notes = "Slab on grade, 2x6 walls, hip trusses. Bedroom wing: hall bath + master suite with walk-in closet.";
            return b;
        }

        static Blueprint Starter1008()
        {
            var b = House("starter-1008", "The Aspen II (starter, 3 bed)", "Ranch", "starter", 36, 28, 3, 2, 0.45f);
            b.Pitch = 5f;
            b.R("Living", 0, 0, 16, 15).R("Kitchen / dining", 0, 15, 16, 28);
            b.R("Bed 2", 16, 0, 26, 12).R("Bed 3", 26, 0, 36, 12);
            b.R("Hall", 16, 12, 30, 16).R("Linen", 30, 12, 36, 16);
            b.R("Bath", 16, 16, 21, 24).R("Laundry", 16, 24, 21, 28).R("Master", 21, 16, 36, 28);
            b.FrontDoorFt = 13.5f;
            b.Notes = "1,008 sq ft starter: 36x28 slab ranch, one hall bath, laundry off the kitchen.";
            return b;
        }

        static Blueprint Ranch1600()
        {
            var b = House("ranch-1600", "The Cedar (ranch)", "Ranch", "ranch", 50, 32, 3, 4, 0.6f);
            b.WallHeightFt = 9f; b.Roof = RoofType.Hip; b.Pitch = 6f; b.FrontDoorFt = 16f;
            b.R("Living", 0, 0, 20, 16).R("Kitchen", 20, 0, 36, 16).R("Dining", 36, 0, 50, 16);
            b.R("Master", 0, 16, 14, 32).R("Hall", 14, 16, 40, 20).R("Bed 3", 40, 16, 50, 32);
            b.R("Master Bath", 14, 20, 20, 32).R("Laundry", 20, 20, 26, 32).R("Bath", 26, 20, 32, 32).R("Bed 2", 32, 20, 40, 32);
            b.Notes = "Development's Cedar redrawn with a bedroom hall (the original had no way into Bed 2).";
            return b;
        }

        static Blueprint Cottage1200()
        {
            var b = House("cottage-1200", "The Birch (cottage)", "Cottage", "cottage", 40, 30, 3, 4, 0.55f);
            b.Pitch = 7f; b.Foundation = FoundationType.Crawlspace;
            b.R("Living", 0, 0, 16, 14).R("Kitchen / dining", 0, 14, 16, 30);
            b.R("Bed 2", 16, 0, 28, 11).R("Bed 3", 28, 0, 40, 11);
            b.R("Hall", 16, 11, 32, 15).R("Laundry", 32, 11, 40, 15);
            b.R("Bath", 16, 15, 22, 23).R("WIC (master)", 16, 23, 22, 30).R("Master", 22, 15, 34, 30).R("Master Bath", 34, 15, 40, 30);
            b.FrontDoorFt = 13.5f;
            b.Notes = "Crawlspace cottage, 7/12 roof, 2x10 joists on a center girder.";
            return b;
        }

        static Blueprint Craftsman1920()
        {
            var b = House("craftsman-1920", "The Dogwood (craftsman)", "Craftsman", "custom", 48, 40, 4, 4, 0.7f);
            b.Pitch = 7f; b.Foundation = FoundationType.Crawlspace; b.BearingLines = 2;
            b.R("Living", 0, 0, 22, 16).R("Dining", 0, 16, 11, 28).R("Kitchen", 11, 16, 22, 28).R("Family room", 0, 28, 22, 40);
            b.R("Bed 2", 22, 0, 35, 12).R("Bed 3", 35, 0, 48, 12);
            b.R("Hall", 22, 12, 44, 16).R("Linen", 44, 12, 48, 16);
            b.R("Hall (rear)", 22, 16, 26, 28).R("Bath", 26, 16, 32, 24).R("Laundry", 32, 16, 38, 24).R("Bed 4", 38, 16, 48, 28);
            b.R("WIC (master)", 26, 24, 38, 28).R("Master", 22, 28, 38, 40).R("Master Bath", 38, 28, 48, 40);
            b.FrontDoorFt = 18f;
            b.Notes = "48x40 crawlspace craftsman: 2x10 joists on two girder lines (13'-4\" spans), family room across the back.";
            return b;
        }

        /// <summary>Center-hall two-story, 40' wide: stair along x from the foyer, bedrooms up. s = stair end x.</summary>
        static Blueprint TwoStory(string id, string name, string style, float s, float D, float wall, bool bed4, FoundationType fnd, float q)
        {
            var b = House(id, name, style, "two-story", 40, D, bed4 ? 4 : 3, 5, q);
            b.Floors = 2; b.WallHeightFt = wall; b.Pitch = 6f; b.Foundation = fnd; b.FrontDoorFt = 20f;
            // first floor
            b.R("Living", 0, 0, 14, 16).R("Foyer", 14, 0, 26, 14).R("Study", 26, 0, 40, 14);
            b.R("Stair", 14, 14, s, 18).R("Hall", s, 14, 36, 18).R("Powder", 36, 14, 40, 18);
            b.R("Family room", 0, 16, 14, D).R("Dining", 14, 18, 24, D).R("Kitchen", 24, 18, 40, D);
            // second floor
            b.R("Bed 2", 0, 0, 13, 10, 1).R("Bed 3", 13, 0, 27, 10, 1).R(bed4 ? "Bed 4" : "Bonus room", 27, 0, 40, 10, 1);
            b.R("Linen", 0, 10, 4, 14, 1).R("Hall", 4, 10, 40, 14, 1);
            b.R("Master", 0, 14, 14, D, 1).R("Stair", 14, 14, s, 18, 1);
            b.R("Master Bath", 14, 18, s - 6, D, 1).R("WIC (master)", s - 6, 18, s, D, 1);
            b.R("Hall 2", s, 14, s + 4, D, 1).R("Bath", s + 4, 14, 40, 20, 1).R("Laundry", s + 4, 20, 40, D, 1);
            b.Notes = $"Two-story, {wall}' walls: straight stair from the foyer ({(wall >= 9 ? "16" : "14")} risers), upstairs laundry, master suite over the family room.";
            return b;
        }

        static Blueprint Estate3500()
        {
            var b = House("estate-3500", "The Fir (custom estate)", "Custom", "custom", 50, 35, 5, 7, 0.85f);
            b.Floors = 2; b.WallHeightFt = 9f; b.Roof = RoofType.Hip; b.Pitch = 8f; b.Foundation = FoundationType.Basement; b.FloorJoist = "2x12"; b.FrontDoorFt = 19f;
            b.R("Study", 0, 0, 13, 14).R("Foyer", 13, 0, 25, 14).R("Living", 25, 0, 50, 19);
            b.R("Powder", 0, 14, 5, 19).R("Gallery", 5, 14, 25, 19);
            b.R("Family room", 0, 19, 13, 35).R("Stair", 13, 19, 17, 35).R("Kitchen", 17, 19, 31, 35);
            b.R("Pantry", 31, 19, 36, 24).R("Laundry / mud", 31, 24, 36, 35).R("Dining", 36, 19, 50, 35);
            b.Door(2, 33.5f, "door-ext-steel36");   // mud room back door
            b.R("Bed 2", 0, 0, 12, 15, 1).R("Linen", 12, 0, 18, 6, 1).R("Bath", 12, 6, 18, 15, 1).R("Bed 3", 18, 0, 30, 15, 1);
            b.R("Bed 4", 30, 0, 42, 15, 1).R("Bath 2", 42, 0, 50, 8, 1).R("Linen 2", 42, 8, 50, 15, 1);
            b.R("Hall", 0, 15, 50, 19, 1);
            b.R("Bed 5", 0, 19, 13, 35, 1).R("Stair", 13, 19, 17, 35, 1).R("Master Bath", 17, 19, 30, 28, 1).R("WIC (master)", 17, 28, 30, 35, 1).R("Master", 30, 19, 50, 35, 1);
            b.StairDown("Stair");   // climbs from the family room (back) up to the hall
            b.Notes = "Custom two-story on a full basement: 2x12 joists (17'-6\" spans), stair rising from the family room, Bed 4 with its own bath.";
            return b;
        }

        static Blueprint SplitLevel2280()
        {
            var b = House("splitlevel-2280", "The Laurel (split-entry)", "Split-level", "split-level", 50, 30, 4, 6, 0.6f);
            b.Floors = 2; b.Pitch = 5f; b.FrontDoorFt = 25.5f;
            // lower level (tuck-under garage)
            b.R("Garage", 0, 0, 22, 30).Door(0, 11f, "garage-16x7");
            b.R("Stair (split entry)", 22, 0, 29, 8).R("Rec room", 29, 0, 50, 16);
            b.R("Mech", 22, 8, 29, 16).R("Laundry", 22, 16, 29, 24).R("Bath 2", 22, 24, 29, 30);
            b.R("Hall", 29, 16, 33, 30).R("Bed 4", 33, 16, 50, 30);
            // main level
            b.R("Bed 2", 0, 0, 11, 12, 1).R("Bed 3", 11, 0, 22, 12, 1).R("Stair (split entry)", 22, 0, 29, 8, 1).R("Living", 29, 0, 50, 16, 1);
            b.R("Linen", 0, 12, 4, 16, 1).R("Hall 2", 4, 12, 22, 16, 1).R("Hall", 22, 8, 29, 16, 1);
            b.R("Master", 0, 16, 14, 30, 1).R("Bath", 14, 16, 22, 22, 1).R("Master Bath", 14, 22, 22, 30, 1);
            b.R("Kitchen", 22, 16, 36, 30, 1).R("Dining", 36, 16, 50, 30, 1);
            b.Notes = "Split-entry (bi-level): front door on a landing, half flight up to the main level and half flight down to the rec room; tuck-under 2-car garage.";
            return b;
        }

        static Blueprint Duplex1664()
        {
            const float uw = 26f;
            var b = House("duplex-1664", "The Alder (side-by-side duplex)", "Duplex", "duplex", uw * 2, 32, 2, 2, 0.5f, CompanyStage.Developer);
            b.Units = 2; b.Pitch = 5f; b.Occupancy = "IRC R-3 (two-family dwelling, R302.3 1-hr separation)";
            for (int u = 0; u < 2; u++)
            {
                float a = u * uw; bool m = u == 1;
                UR(b, a, uw, m, "Living", 0, 0, 15, 16); UR(b, a, uw, m, "Kitchen / dining", 0, 16, 15, 32);
                UR(b, a, uw, m, "Bed 2", 15, 0, 26, 12); UR(b, a, uw, m, "Hall", 15, 12, 19, 22);
                UR(b, a, uw, m, "Bath", 19, 12, 26, 18); UR(b, a, uw, m, "Laundry", 19, 18, 26, 22); UR(b, a, uw, m, "Master", 15, 22, 26, 32);
                b.Door(0, UX(a, uw, m, 12.5f), "door-ext-fg36").Door(2, UX(a, uw, m, 3f), "door-ext-steel36");
            }
            b.Notes = "Two 832 sq ft units, mirrored so the bedrooms share the 1-hour party wall.";
            return b;
        }

        static Blueprint TownhomeRow(string id, string name, int n)
        {
            const float uw = 20f;
            var b = House(id, name, "Townhome", "townhome", uw * n, 40, 3, 5, 0.6f, CompanyStage.Developer);
            b.Units = n; b.Floors = 2; b.Pitch = 6f; b.BearingLines = 2;
            b.Occupancy = "IRC R-3 townhouses (R302.2 2-hr common wall, R313 sprinklers)";
            for (int u = 0; u < n; u++)
            {
                float a = u * uw; bool m = u % 2 == 1;
                UR(b, a, uw, m, "Foyer", 0, 0, 4, 14); UR(b, a, uw, m, "Living", 4, 0, 20, 16); UR(b, a, uw, m, "Stair", 0, 14, 4, 30);
                UR(b, a, uw, m, "Powder", 4, 16, 9, 20); UR(b, a, uw, m, "Coat closet", 9, 16, 15, 20); UR(b, a, uw, m, "Pantry", 15, 16, 20, 20);
                UR(b, a, uw, m, "Kitchen", 4, 20, 20, 30); UR(b, a, uw, m, "Dining", 0, 30, 20, 40);
                UR(b, a, uw, m, "Master", 0, 0, 13, 14, 1); UR(b, a, uw, m, "Master Bath", 13, 0, 20, 9, 1); UR(b, a, uw, m, "WIC (master)", 13, 9, 20, 14, 1);
                UR(b, a, uw, m, "Stair", 0, 14, 4, 30, 1); UR(b, a, uw, m, "Hall", 4, 14, 9, 30, 1);
                UR(b, a, uw, m, "Bath", 9, 14, 20, 22, 1); UR(b, a, uw, m, "Linen", 9, 22, 13, 26, 1); UR(b, a, uw, m, "Hall 2", 9, 26, 13, 30, 1);
                UR(b, a, uw, m, "Laundry", 13, 22, 20, 30, 1);
                UR(b, a, uw, m, "Bed 2", 0, 30, 9, 40, 1); UR(b, a, uw, m, "Bed 3", 9, 30, 20, 40, 1);
                b.Door(0, UX(a, uw, m, 2f), "door-ext-fg36").Door(2, UX(a, uw, m, 10f), "door-ext-steel36");
            }
            b.Notes = $"{n} two-story 1,600 sq ft townhomes (20' wide), alternate units mirrored; NFPA 13D sprinklers per IRC R313.";
            return b;
        }

        static Blueprint Adu480()
        {
            var b = House("adu-480", "The Hazel (backyard cottage / ADU)", "Cottage", "adu", 20, 24, 1, 2, 0.5f, CompanyStage.Crew);
            b.Pitch = 6f; b.FrontDoorFt = 10f; b.Occupancy = "IRC R-3 (accessory dwelling unit)";
            b.R("Living", 0, 0, 12, 12).R("Kitchen / dining", 0, 12, 12, 24);
            b.R("Bedroom", 12, 0, 20, 11).R("Hall", 12, 11, 15, 17).R("Bath", 15, 11, 20, 18);
            b.R("Laundry", 12, 17, 15, 24).R("Utility", 15, 18, 20, 24);
            b.Notes = "480 sq ft backyard ADU: one bedroom, stacked laundry, utility closet for the furnace + water heater.";
            return b;
        }

        static Blueprint Carriage672()
        {
            var b = House("adu-carriage-672", "The Linden (carriage house)", "Carriage house", "adu", 28, 24, 1, 2, 0.55f, CompanyStage.Crew);
            b.Floors = 2; b.Pitch = 8f; b.FloorJoist = "Open-web 16"; b.BearingLines = 0; b.Occupancy = "IRC R-3 (ADU over garage, R302.6 separation)";
            b.R("Garage", 0, 0, 24, 24).R("Entry", 24, 0, 28, 9).R("Stair", 24, 9, 28, 24);
            b.Door(0, 12f, "garage-16x7").Door(0, 26f, "door-ext-fg36");
            b.R("Bedroom", 0, 0, 12, 12, 1).R("Kitchen / dining", 0, 12, 12, 24, 1);
            b.R("Bath", 12, 0, 19, 7, 1).R("Laundry", 19, 0, 24, 7, 1).R("Hall", 12, 7, 24, 12, 1).R("Living", 12, 12, 24, 24, 1);
            b.R("Mech", 24, 0, 28, 9, 1).R("Stair", 24, 9, 28, 24, 1);
            b.Notes = "672 sq ft apartment over a clear-span 2-car garage (16\" open-web floor trusses span 24'); type X ceiling under the unit.";
            return b;
        }

        // ================================================================== outbuildings

        static Blueprint Garage(string id, string name, float w, float d, (float x, string door)[] doors, string studs)
        {
            var b = Outbuilding(id, name, "Detached garage", "garage", w, d, CompanyStage.Contractor);
            b.StudSize = studs; b.SlabIn = 4f;
            b.R("Garage", 0, 0, w, d);
            foreach (var (x, door) in doors) b.Door(0, x, door);
            b.Door(1, d - 5f, "door-ext-steel36");
            b.Notes = "Monolithic slab with thickened edge, 2x walls, trusses; 60A subpanel fed from the house.";
            return b;
        }

        static Blueprint Garage2CarShop()
        {
            var b = Outbuilding("garage-2car-shop", "2-car garage + workshop", "Detached garage", "garage", 24, 36, CompanyStage.Contractor);
            b.StudSize = "2x6"; b.WallHeightFt = 9f; b.Conditioned = true;
            b.R("Garage", 0, 0, 24, 24).R("Shop", 0, 24, 24, 36);
            b.Door(0, 12f, "garage-16x7").Door(1, 30f, "door-ext-steel36");
            b.Notes = "2-car garage with a 12x24 heated workshop behind it.";
            return b;
        }

        static Blueprint Shed(string id, string name, float w, float d, CompanyStage st, FoundationType fnd)
        {
            var b = Outbuilding(id, name, "Shed", "shed", w, d, st);
            b.WallHeightFt = 8f; b.Foundation = fnd; b.SlabIn = fnd == FoundationType.Slab ? 4f : 0f; b.Pitch = 6f;
            b.R("Shed", 0, 0, w, d).Door(0, w / 2f, "door-shed-dbl60");
            b.Notes = fnd == FoundationType.Skids ? "On 4x6 pressure-treated skids over a gravel pad; 2x4 walls, 3/4\" T&G floor." : "On a 4\" slab; 2x4 walls.";
            return b;
        }

        static Blueprint PostFrame(Blueprint b, float eave)
        {
            b.WallFrame = WallSystem.PostFrame; b.RoofFrame = RoofSystem.PostFrameTrusses; b.Foundation = FoundationType.PostFrame;
            b.Covering = RoofCovering.MetalPanels; b.Pitch = 4f; b.WallHeightFt = eave; b.StudSize = "2x6"; b.SlabIn = 4f;
            b.ConstructionType = "V-B (post-frame)";
            return b;
        }

        static Blueprint PoleBarn30x40()
        {
            var b = PostFrame(Outbuilding("polebarn-30x40", "Pole barn 30x40", "Pole barn", "pole-barn", 30, 40, CompanyStage.Contractor), 14f);
            b.R("Pole barn", 0, 0, 30, 40).Door(0, 15f, "oh-12x12").Door(1, 6f, "door-walk-36-steel");
            b.Notes = "6x6 posts 8' O.C. in 18\" concrete-collared holes, 2x6 girts, trusses 8' O.C., 29 ga. steel.";
            return b;
        }

        static Blueprint PoleBarn40x60()
        {
            var b = PostFrame(Outbuilding("polebarn-40x60", "Pole barn 40x60 + storage room", "Pole barn", "pole-barn", 40, 60, CompanyStage.Crew), 14f);
            b.R("Pole barn", 0, 0, 40, 48).R("Storage room", 0, 48, 40, 60);
            b.Door(0, 10f, "oh-12x12").Door(0, 30f, "oh-12x12").Door(1, 6f, "door-walk-36-steel");
            b.Notes = "Two 12x12 overhead doors in the gable end, enclosed storage room across the back.";
            return b;
        }

        static Blueprint PoleBarn60x80()
        {
            var b = PostFrame(Outbuilding("polebarn-60x80", "Pole barn 60x80 (equipment)", "Pole barn", "pole-barn", 60, 80, CompanyStage.Crew), 16f);
            b.R("Pole barn", 0, 0, 60, 80);
            b.Door(0, 15f, "oh-14x14").Door(0, 45f, "oh-14x14").Door(1, 40f, "door-slide-12x12").Door(3, 10f, "door-walk-36-steel").Door(1, 70f, "door-walk-36-steel");
            b.Notes = "60' clear-span trusses for combines/RVs; two 14x14 doors + a 12x12 slider on the side.";
            return b;
        }

        static Blueprint Barn36x48()
        {
            var b = PostFrame(Outbuilding("barn-36x48", "Horse barn 36x48 (6 stalls + hay loft)", "Barn", "barn", 36, 48, CompanyStage.Crew), 16f);
            b.Floors = 2; b.MezzanineFt = 8.5f; b.Roof = RoofType.Gambrel; b.Pitch = 8f;
            b.R("Stall 1", 0, 0, 12, 12).R("Stall 2", 0, 12, 12, 24).R("Stall 6", 0, 24, 12, 36).R("Tack room", 0, 36, 12, 48);
            b.R("Aisle", 12, 0, 24, 48);
            b.R("Stall 3", 24, 0, 36, 12).R("Stall 4", 24, 12, 36, 24).R("Stall 5", 24, 24, 36, 34).R("Stair", 24, 34, 28, 48).R("Feed room", 28, 34, 36, 48);
            b.Door(0, 18f, "door-slide-10x10").Door(2, 18f, "door-slide-10x10").Door(1, 41f, "door-walk-36-steel");
            b.R("Hay loft west", 0, 0, 12, 48, 1).R("Open to below", 12, 0, 24, 44, 1).R("Loft catwalk", 12, 44, 24, 48, 1);
            b.R("Hay loft east", 24, 0, 36, 34, 1).R("Stair", 24, 34, 28, 48, 1).R("Hay loft NE", 28, 34, 36, 48, 1);
            b.Notes = "Center-aisle horse barn: 12x12 box stalls with sliding grille doors, gambrel roof, hay loft at 8'-6\" over the stalls, open over the aisle.";
            return b;
        }

        static Blueprint Workshop30x40()
        {
            var b = Outbuilding("workshop-30x40", "Workshop 30x40 (wood frame)", "Workshop", "workshop", 30, 40, CompanyStage.Contractor);
            b.StudSize = "2x6"; b.WallHeightFt = 12f; b.SlabIn = 5f; b.Conditioned = true; b.Finished = true;
            b.R("Shop", 0, 0, 30, 32).R("Office", 0, 32, 12, 40).R("Half bath", 12, 32, 18, 40).R("Storage", 18, 32, 30, 40);
            b.Door(0, 15f, "oh-10x10").Door(0, 4f, "door-ext-steel36");
            b.Notes = "12' 2x6 walls for a 10x10 door, 5\" slab, office + half bath + storage across the back.";
            return b;
        }

        static Blueprint Workshop40x60()
        {
            var b = PostFrame(Outbuilding("workshop-40x60", "Workshop 40x60 (post frame)", "Workshop", "workshop", 40, 60, CompanyStage.Crew), 14f);
            b.SlabIn = 5f; b.Conditioned = true; b.Finished = true;
            b.R("Shop", 0, 0, 40, 44).R("Office", 0, 44, 14, 60).R("Half bath", 14, 44, 20, 52).R("Mech", 14, 52, 20, 60).R("Parts storage", 20, 44, 40, 60);
            b.Door(0, 10f, "oh-12x12").Door(0, 30f, "oh-12x12").Door(0, 20f, "door-walk-36-steel");
            b.Notes = "Post-frame shop with two 12x12 doors, heated, office/bath/parts room across the back.";
            return b;
        }

        // ================================================================== commercial

        static Blueprint Retail(string id, string name, float W, float D, float salesD)
        {
            var b = Commercial(id, name, "Retail", "retail", W, D, "IBC M (mercantile)", CompanyStage.Builder);
            float hw = W / 2f, y = salesD;
            b.R("Sales floor", 0, 0, W, y).R("Stockroom", 0, y, hw, D);
            b.R("Corridor", hw, y, W, y + 4).R("Restroom", hw, y + 4, hw + 8, y + 12).R("Janitor", hw + 8, y + 4, hw + 12, y + 12);
            b.R("Office", hw + 12, y + 4, W, D).R("Mech", hw, y + 12, hw + 12, D);
            b.Door(0, W / 2f, "door-storefront72").Door(2, hw / 2f, "door-hm36-panic");
            b.Notes = "CMU bearing walls, steel bar joists + TPO roof, storefront glazing across the front, stockroom with a rear exit.";
            return b;
        }

        static Blueprint Diner3000()
        {
            var b = Commercial("diner-3000", "Diner (80 seats)", "Restaurant", "restaurant", 50, 60, "IBC A-2 (restaurant)", CompanyStage.Builder);
            b.R("Dining room", 0, 0, 30, 30).R("Counter seating", 30, 0, 50, 14);
            b.R("Cook line", 30, 14, 50, 28).R("Prep", 30, 28, 50, 42).R("Walk-in cooler", 30, 42, 36, 52).R("Dish", 36, 42, 50, 52).R("Dry storage", 30, 52, 50, 60);
            b.R("Restroom (men)", 0, 30, 8, 38).R("Restroom (women)", 0, 38, 8, 46).R("Office", 0, 46, 8, 54).R("Janitor", 0, 54, 8, 60);
            b.R("Corridor", 8, 30, 12, 60).R("Break room", 12, 30, 22, 40).R("Mech", 12, 40, 22, 60).R("Storage", 22, 30, 30, 60);
            b.Door(0, 15f, "door-storefront72").Door(3, 15f, "door-hm36-panic").Door(2, 40f, "door-hm36");
            b.Notes = "Under 100 occupants (no sprinklers required), Type I hood over the cook line, 1,000 gal grease interceptor.";
            return b;
        }

        static Blueprint Office3000()
        {
            var b = Commercial("office-3000", "Professional office building", "Office", "office", 60, 50, "IBC B (business)", CompanyStage.Builder);
            b.WallFrame = WallSystem.WoodStud; b.RoofFrame = RoofSystem.Trusses; b.Roof = RoofType.Hip; b.Covering = RoofCovering.Shingles; b.Pitch = 5f;
            b.WallHeightFt = 10f; b.ConstructionType = "V-B (wood frame)";
            b.R("Office 1", 0, 0, 12, 14).R("Office 2", 12, 0, 24, 14).R("Reception", 24, 0, 40, 14).R("Conference room", 40, 0, 60, 14);
            b.R("Corridor", 0, 14, 60, 20).R("Open office", 0, 20, 28, 50).R("Corridor 2", 28, 20, 34, 50);
            b.R("Restroom", 34, 20, 42, 28).R("Restroom 2", 42, 20, 50, 28).R("Janitor", 50, 20, 54, 28).R("Mech", 54, 20, 60, 28);
            b.R("Corridor 3", 34, 28, 60, 32).R("Break room", 34, 32, 46, 50).R("Office 3", 46, 32, 60, 50);
            b.Door(0, 32f, "door-storefront36").Door(2, 14f, "door-hm36");
            b.Notes = "Wood-frame office: 10' walls, hip trusses, 6' corridors, two ADA restrooms.";
            return b;
        }

        static Blueprint StripMall6000()
        {
            const float uw = 25f;
            var b = Commercial("stripmall-6000", "Strip mall (4 tenant spaces)", "Strip center", "strip-mall", uw * 4, 60, "IBC M (mercantile, multi-tenant)", CompanyStage.Developer);
            b.Units = 4; b.WallHeightFt = 16f;
            for (int u = 0; u < 4; u++)
            {
                float a = u * uw;
                b.R("Retail space", a, 0, a + uw, 44).R("Stockroom", a, 44, a + 15, 60).R("Restroom", a + 15, 44, a + uw, 52).R("Janitor", a + 15, 52, a + uw, 60);
                b.Door(0, a + 12.5f, "door-storefront36").Door(2, a + 7.5f, "door-hm36");
            }
            b.Notes = "Four 1,500 sq ft bays with storefronts, demising walls, a restroom and rear service door in each.";
            return b;
        }

        static Blueprint GasStation3200()
        {
            var b = Commercial("gasstation-3200", "Gas station convenience store", "Convenience store", "gas-station", 64, 50, "IBC M (mercantile)", CompanyStage.Developer);
            b.R("Store floor", 0, 0, 64, 32).R("Walk-in cooler", 0, 32, 24, 44).R("Stockroom", 0, 44, 24, 50).R("Corridor", 24, 32, 28, 50);
            b.R("Restroom (men)", 28, 32, 36, 40).R("Restroom (women)", 36, 32, 44, 40).R("Janitor", 44, 32, 48, 40).R("Office", 48, 32, 56, 40).R("Mech", 56, 32, 64, 40);
            b.R("Storage", 28, 40, 64, 50);
            b.Door(0, 32f, "door-storefront72").Door(2, 46f, "door-hm36");
            b.Notes = "C-store with a walk-in beer cave; the fuel canopy, tanks and dispensers are a separate site package.";
            return b;
        }

        /// <summary>Two-story downtown row building, W' wide x 70' deep between party walls: a shop on the ground floor (storefront, stockroom,
        /// ADA restroom, rear exit) and a two-bedroom apartment upstairs reached from its own street door. The apartment entry is walled off
        /// from the shop; the stair is 48" wide (IBC 1011.2 minimum 36" for under 50 occupants), 7" max risers / 11" treads, a 3' landing at
        /// the bottom and the upstairs hall at the top, with blocking for handrails on both sides.</summary>
        static Blueprint Storefront(string id, string name, float W)
        {
            const float D = 70f, sx = 4f;     // stair / entry column on the right, 4' wide
            float c = W - sx;
            var b = Commercial(id, name, "Main Street storefront", "storefront", W, D, "IBC M (shop) / R-3 (dwelling above, 1-hr separation per IBC 508.4)", CompanyStage.Builder);
            b.Floors = 2; b.WallHeightFt = 12f; b.FloorJoist = "Open-web 16"; b.BearingLines = 2; b.PartyWalls = true;
            b.ConstructionType = "III-B (brick/CMU party walls, wood floor trusses)";
            // ground floor: the shop
            b.R("Sales floor", 0, 0, c, 46).R("Entry (apartment)", c, 0, W, 6).R("Stair", c, 6, W, 31).R("Storage", c, 31, W, 46);
            b.R("Restroom", 0, 46, 10, 54).R("Janitor", 0, 54, 10, 59).R("Mech", 0, 59, 10, D).R("Corridor", 10, 46, 14, D).R("Stockroom", 14, 46, W, D);
            b.Private("Entry (apartment)");
            // storefront: shop door near the apartment door, display windows across the rest of the front (head 8'-6")
            b.Door(0, c - 3f, "door-storefront36").Door(0, c + sx / 2f, "door-ext-steel36").Door(2, 12f, "door-hm36-panic");
            for (float x = 4f; x <= c - 3f - 5.2f; x += 7f) b.Window(0, x, "win-storefront-7284");
            // upper floor: the apartment
            b.R("Living", 0, 0, c, 18, 1).R("Coat closet", c, 0, W, 6, 1).R("Stair", c, 6, W, 31, 1).R("Kitchen", 0, 18, c, 31, 1);
            b.R("Pantry", 0, 31, 8, 40, 1).R("Bath", 8, 31, c, 40, 1).R("Hall", c, 31, W, 50, 1);
            b.R("Study", 0, 40, 12, 50, 1).R("Laundry", 12, 40, c, 50, 1);
            b.R("Hall 2", 0, 50, W, 54, 1).R("Bed 2", 0, 54, 11, D, 1).R("Master", 11, 54, W, D, 1);
            b.Notes = $"Zero-lot-line row building {W}'x{D}': storefront below, two-bedroom apartment above with its own street door and a straight " +
                      "48\" stair (24 risers @ 6-3/4\", 11\" treads, handrails both sides); 12' floors, 16\" open-web floor trusses on two bearing lines, " +
                      "type X ceiling over the shop, bedrooms with egress windows on the back wall.";
            return b;
        }

        /// <summary>Small bank branch 40'x70': banking lobby and teller line up front, offices, a safe deposit room, staff corridor to the back.</summary>
        static Blueprint Bank2800()
        {
            var b = Commercial("bank-2800", "Bank branch (2,800 sq ft)", "Bank", "bank", 40, 70, "IBC B (business)", CompanyStage.Builder);
            b.WallHeightFt = 14f;
            b.R("Banking lobby", 0, 0, 28, 30).R("Manager office", 28, 0, 40, 15).R("Loan office", 28, 15, 40, 30);
            b.R("Teller line", 0, 30, 24, 44).R("Corridor", 24, 30, 28, 70);
            b.R("Safe deposit storage", 0, 44, 24, 56).R("Break room", 0, 56, 24, 64).R("Records storage", 0, 64, 24, 70);
            b.R("Conference room", 28, 30, 40, 44).R("Restroom", 28, 44, 40, 52).R("Restroom 2", 28, 52, 40, 60).R("Janitor", 28, 60, 40, 64).R("Mech", 28, 64, 40, 70);
            b.Door(0, 14f, "door-storefront72").Door(2, 26f, "door-hm36-panic");
            b.Notes = "Masonry bank branch: 14' banking hall, teller line behind a secured door, reinforced safe deposit / cash room (UL vault door by the bank's vendor), " +
                      "two ADA restrooms, staff exit at the back. The night drop and ATM are a separate package.";
            return b;
        }

        /// <summary>Neighborhood grocery 200'x100' (20,000 sq ft): sales floor, receiving + stockroom with a dock, walk-in cooler and freezer
        /// behind the cases, offices / break room / restrooms off a back corridor. Sprinklered (IBC 903.2.7: M fire area over 12,000 sq ft).</summary>
        static Blueprint Grocery20000()
        {
            var b = Commercial("grocery-20000", "Grocery store (20,000 sq ft)", "Grocery", "grocery", 200, 100, "IBC M (mercantile)", CompanyStage.Developer);
            b.WallHeightFt = 18f; b.InteriorColumnLines = 1; b.SlabIn = 5f;
            b.R("Sales floor", 0, 0, 200, 70);
            b.R("Receiving stockroom", 0, 70, 70, 100).R("Walk-in cooler", 70, 70, 100, 100).R("Walk-in cooler (freezer)", 100, 70, 120, 100).R("Corridor", 120, 70, 124, 100);
            b.R("Office", 124, 70, 140, 86).R("Cash office", 140, 70, 152, 86).R("Break room", 152, 70, 176, 86).R("Mech", 176, 70, 200, 86);
            b.R("Corridor 2", 124, 86, 200, 90);
            b.R("Restroom (men)", 124, 90, 136, 100).R("Restroom (women)", 136, 90, 148, 100).R("Janitor", 148, 90, 156, 100).R("Supply storage", 156, 90, 200, 100);
            b.Door(0, 60f, "door-storefront72").Door(0, 140f, "door-storefront72");
            b.Door(2, 30f, "oh-dock-9x10").Door(2, 50f, "door-hm36").Door(2, 122f, "door-hm36-panic").Door(1, 88f, "door-hm36-panic");
            b.Notes = "CMU and steel bar-joist grocery, 18' clear, one interior column line; entrance and exit vestibules, dock door with leveler at receiving, " +
                      "walk-in cooler and freezer loaded from the stockroom behind the dairy/frozen cases, NFPA 13 sprinklers.";
            return b;
        }

        // ================================================================== industrial

        /// <summary>PEMB warehouse with a front-left office block ow wide x 24 deep (2 offices, corridor, 2 restrooms, janitor, mech).</summary>
        static Blueprint Warehouse(string id, string name, float W, float D, float eave, float ow, int cols, CompanyStage st, float[] front, string frontDoor, float[] docks)
        {
            var b = Industrial(id, name, "warehouse", W, D, eave, st);
            b.InteriorColumnLines = cols;
            OfficeBlock(b, ow);
            b.R("Warehouse", ow, 0, W, D).R("Warehouse (west)", 0, 24, ow, D);
            b.Door(0, ow / 4f, "door-storefront36").Door(1, D / 2f, "door-hm36-panic").Door(3, D - 10f, "door-hm36-panic");
            foreach (var x in front) b.Door(0, x, frontDoor);
            foreach (var x in docks) b.Door(2, x, "oh-dock-9x10");
            b.Notes = $"Pre-engineered steel building, {eave}' eave, {(cols > 0 ? cols + " interior column line(s)" : "clear span")}, 6\" slab, office block {ow}'x24'"
                      + (docks.Length > 0 ? $", {docks.Length} dock doors with levelers" : "") + ".";
            return b;
        }

        static void OfficeBlock(Blueprint b, float ow)
        {
            b.R("Office", 0, 0, ow / 2f, 12).R("Office 2", ow / 2f, 0, ow, 12).R("Corridor", 0, 12, ow, 16);
            b.R("Restroom", 0, 16, 8, 24).R("Restroom 2", 8, 16, 16, 24).R("Janitor", 16, 16, ow, 20).R("Mech", 16, 20, ow, 24);
        }

        static Blueprint Flex6000()
        {
            const float uw = 30f;
            var b = Industrial("flex-6000", "Flex building (4 office/warehouse units)", "warehouse", uw * 4, 50, 18, CompanyStage.Builder);
            b.Units = 4; b.Occupancy = "IBC B / S-1 (flex office-warehouse)";
            for (int u = 0; u < 4; u++)
            {
                float a = u * uw;
                b.R("Office", a, 0, a + 14, 14).R("Restroom", a + 14, 0, a + 22, 8).R("Janitor", a + 14, 8, a + 22, 14).R("Warehouse", a + 22, 0, a + uw, 14).R("Warehouse bay", a, 14, a + uw, 50);
                b.Door(0, a + 7f, "door-storefront36").Door(2, a + 12f, "oh-12x14").Door(2, a + 24f, "door-hm36");
            }
            b.Notes = "Four 1,500 sq ft units: front office + restroom, rear 12x14 grade-level door.";
            return b;
        }

        static Blueprint YardBay1500()
        {
            var b = Industrial("yard-bay-1500", "Contractor yard equipment bay", "warehouse", 30, 50, 16, CompanyStage.Contractor);
            b.Occupancy = "IBC S-1 (contractor storage)"; b.Notes = "The small version of the company yard building: one drive-through bay, parts room, restroom.";
            b.R("Equipment bay", 0, 0, 30, 40).R("Parts storage", 0, 40, 18, 50).R("Restroom", 18, 40, 26, 50).R("Mech", 26, 40, 30, 50);
            b.Door(0, 13f, "oh-12x14").Door(0, 25f, "door-hm36").Door(2, 9f, "door-hm36");
            return b;
        }

        static Blueprint YardShop2400()
        {
            var b = Industrial("yard-shop-2400", "Contractor yard shop (2 bays)", "warehouse", 40, 60, 18, CompanyStage.Builder);
            b.Occupancy = "IBC S-1 / F-1 (repair garage)"; b.Notes = "Two drive-in bays for trucks and equipment, heated, parts room, break room, office.";
            b.R("Shop", 0, 0, 40, 44).R("Parts storage", 0, 44, 16, 60).R("Restroom", 16, 44, 24, 52).R("Janitor", 16, 52, 24, 60);
            b.R("Break room", 24, 44, 40, 52).R("Office", 24, 52, 40, 60);
            b.Door(0, 10f, "oh-12x14").Door(0, 30f, "oh-12x14").Door(0, 20f, "door-hm36").Door(2, 32f, "door-hm36");
            return b;
        }

        static Blueprint YardHq7200()
        {
            var b = Industrial("yard-hq-7200", "Company yard HQ (offices + warehouse + mezzanine)", "warehouse", 120, 60, 24, CompanyStage.Builder);
            b.Occupancy = "IBC B / S-1 (contractor headquarters)"; b.Floors = 2; b.MezzanineFt = 12f; b.FloorJoist = "Open-web 16"; b.BearingLines = 0;
            // ground floor: office wing + warehouse
            b.R("Lobby", 0, 0, 16, 14).R("Office 1", 16, 0, 28, 14).R("Office 2", 28, 0, 40, 14).R("Corridor", 0, 14, 40, 20);
            b.R("Restroom", 0, 20, 8, 28).R("Restroom 2", 8, 20, 16, 28).R("Corridor 2", 16, 20, 20, 60).R("Stair", 20, 20, 24, 42).R("Storage 2", 20, 42, 24, 60);
            b.R("Office 3", 0, 28, 16, 40).R("Office 4", 0, 40, 16, 52).R("Janitor", 0, 52, 16, 56).R("Storage", 0, 56, 16, 60);
            b.R("Conference room", 24, 20, 40, 34).R("Break room", 24, 34, 40, 48).R("Mech", 24, 48, 40, 60);
            b.R("Warehouse", 40, 0, 120, 60);
            // mezzanine level over the office wing
            b.R("Open office", 0, 0, 40, 20, 1).R("Corridor", 16, 20, 20, 60, 1).R("Stair", 20, 20, 24, 42, 1).R("Storage 3", 20, 42, 24, 60, 1);
            b.R("Office 5", 0, 20, 16, 32, 1).R("Office 6", 0, 32, 16, 44, 1).R("Records storage", 0, 44, 16, 60, 1);
            b.R("Conference room 2", 24, 20, 40, 40, 1).R("File storage", 24, 40, 40, 60, 1).R("Open to below", 40, 0, 120, 60, 1);
            b.Door(0, 12f, "door-storefront36").Door(0, 60f, "oh-14x14").Door(0, 90f, "oh-14x14").Door(2, 70f, "oh-dock-9x10").Door(2, 100f, "oh-dock-9x10").Door(1, 30f, "door-hm36");
            b.Notes = "Like the company's own yard building: two-level office wing (mezzanine at 12' on a 22' IBC stair: two flights, 11 + 10 risers @ 6-7/8\", with a 44\" landing between them) beside a 80x60 warehouse with drive-in and dock doors.";
            return b;
        }
    }
}
