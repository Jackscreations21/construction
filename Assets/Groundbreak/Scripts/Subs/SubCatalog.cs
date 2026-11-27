using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One trade a subcontractor can be hired for, with how it is priced (real US labor-only sub rates, 2025-26:
    /// the sub's install price incl. their overhead, insurance and profit; materials come from your site stock like your own crew).</summary>
    public class SubTradeInfo
    {
        public string Id, Label;
        public string Unit;              // "sq ft", "square", "joint", "box", "cu yd"
        public float Rate;               // $/unit for a standard-priced sub
        public int Minimum;              // trip / mobilization minimum ($)
        public float CrewPerDay;         // units one journeyman of your own crew does in a day (for the "your crew" comparison)
        public string CrewTrade;         // which Crew trade the sub's workers are (shirt color, task matching)
        public string Truck;             // VehicleModels type the sub shows up in
        public int CrewSize;             // workers the sub sends
        public string Scope;             // what the price covers
    }

    /// <summary>A local trade company you can hire for one trade on a job.</summary>
    public class SubCompany
    {
        public string Id, Name, Trade, Phone;
        public float PriceFactor = 1f;   // x the trade's standard rate
        public int Quality = 3;          // 1..5 = the skill of the hands they send (mistakes, inspection callbacks)
        public float Reliability = 0.85f;// chance they show up on the day they said
        public int LeadDays = 3;         // how far out they are booked
        public Color Color = Color.white;
        public string PriceLevel => PriceFactor < 0.92f ? "$" : PriceFactor < 1.15f ? "$$" : "$$$";
    }

    /// <summary>The trades and the local sub companies (fixed list: the same town every game).</summary>
    public static class SubCatalog
    {
        public static readonly List<SubTradeInfo> Trades = new List<SubTradeInfo>
        {
            T("excavation",  "Excavation",  "cu yd",   95f,   450, 4f,    "laborer",     "dump",    2, "Dig footing trenches to plan depth with a mini-excavator, spoils piled on site"),
            T("concrete",    "Concrete",    "cu yd",  165f,   850, 2.5f,  "concrete",    "flatbed", 3, "Place, consolidate and finish footings / slab (ready-mix billed separately by the plant)"),
            T("framing",     "Framing",     "sq ft",    8f,  1500, 60f,   "carpenter",   "flatbed", 3, "Frame floors, walls and roof from your layouts, sheathed and nailed to schedule"),
            T("roofing",     "Roofing",     "square", 185f,   900, 2.5f,  "roofer",      "boom",    3, "Underlayment, drip edge and shingles per 100 sq ft (square) of roof"),
            T("plumbing",    "Plumbing",    "joint",   38f,   950, 25f,   "plumber",     "van",     2, "Make up every supply, drain and vent joint (sweat, crimp, glue) ready for the pressure test"),
            T("electrical",  "Electrical",  "box",     85f,   850, 10f,   "electrician", "van",     2, "Make up each box (strip, grounds, neutrals, hots) and install the device"),
            T("hvac",        "HVAC",        "joint",   32f,  1200, 30f,   "hvac",        "van",     2, "Seal every duct joint (foil tape + mastic), braze the line set, glue the condensate"),
            T("insulation",  "Insulation",  "sq ft",  0.55f,  450, 800f,  "laborer",     "box",     2, "Batts in walls and ceilings to the energy code R-values, stapled / friction fit"),
            T("drywall",     "Drywall",     "sq ft",  1.95f,  900, 300f,  "drywall",     "box",     3, "Hang, screw, tape and finish (Level 4) per sq ft of board"),
            T("painting",    "Painting",    "sq ft",  1.40f,  600, 450f,  "painter",     "van",     2, "Texture, prime and two finish coats per sq ft of wall and ceiling"),
            T("flooring",    "Flooring",    "sq ft",  3.25f,  700, 200f,  "carpenter",   "box",     2, "Underlayment and finish flooring installed per sq ft"),
            T("landscaping", "Landscaping", "sq ft",  0.55f,  400, 900f,  "landscaper",  "pickup",  2, "Lay and roll sod per sq ft, watered in"),
        };

        static SubTradeInfo T(string id, string label, string unit, float rate, int min, float perDay, string crew, string truck, int size, string scope) =>
            new SubTradeInfo { Id = id, Label = label, Unit = unit, Rate = rate, Minimum = min, CrewPerDay = perDay, CrewTrade = crew, Truck = truck, CrewSize = size, Scope = scope };

        public static SubTradeInfo Trade(string id) => Trades.Find(t => t.Id == id);

        public static readonly List<SubCompany> Companies = new List<SubCompany>();
        public static SubCompany Get(string id) => Companies.Find(c => c.Id == id);
        public static IEnumerable<SubCompany> For(string trade) { foreach (var c in Companies) if (c.Trade == trade) yield return c; }

        static void C(string trade, string name, string phone, float price, int quality, float reliability, int lead, Color color) =>
            Companies.Add(new SubCompany
            {
                Id = trade + "-" + (Companies.FindAll(x => x.Trade == trade).Count + 1), Name = name, Trade = trade, Phone = phone,
                PriceFactor = price, Quality = quality, Reliability = reliability, LeadDays = lead, Color = color,
            });

        static SubCatalog()
        {
            // three per trade: a cheap outfit (fast to book, sloppy, often late), a solid standard shop, a premium company (booked out, near-perfect)
            Color red = new Color(0.72f, 0.16f, 0.12f), blue = new Color(0.14f, 0.3f, 0.62f), green = new Color(0.18f, 0.45f, 0.22f),
                  gray = new Color(0.4f, 0.42f, 0.45f), white = new Color(0.92f, 0.92f, 0.9f), black = new Color(0.12f, 0.12f, 0.13f),
                  orange = new Color(0.9f, 0.48f, 0.1f), tan = new Color(0.72f, 0.6f, 0.42f);
            C("excavation", "Dirt Cheap Digging", "555-0142", 0.82f, 1, 0.55f, 1, orange);
            C("excavation", "Red Clay Earthworks", "555-0187", 1.0f, 3, 0.86f, 3, red);
            C("excavation", "Benchmark Site Services", "555-0119", 1.3f, 5, 0.97f, 6, gray);
            C("concrete", "Quick Mud Concrete", "555-0163", 0.84f, 2, 0.6f, 2, gray);
            C("concrete", "Stonebridge Concrete", "555-0131", 1.0f, 3, 0.85f, 4, blue);
            C("concrete", "Level Line Foundations", "555-0175", 1.28f, 5, 0.96f, 7, black);
            C("framing", "Two Guys Framing", "555-0108", 0.8f, 1, 0.55f, 1, tan);
            C("framing", "Square & Plumb Framers", "555-0156", 1.0f, 3, 0.85f, 4, red);
            C("framing", "Timberline Structures", "555-0194", 1.3f, 5, 0.96f, 7, green);
            C("roofing", "Storm Chaser Roofing", "555-0121", 0.82f, 1, 0.5f, 1, black);
            C("roofing", "Summit Roofing Co.", "555-0148", 1.0f, 3, 0.87f, 3, blue);
            C("roofing", "Ridgecap Roofing", "555-0170", 1.32f, 5, 0.97f, 6, gray);
            C("plumbing", "Drip Fix Plumbing", "555-0135", 0.85f, 2, 0.6f, 1, blue);
            C("plumbing", "Main Line Plumbing", "555-0183", 1.0f, 3, 0.88f, 3, blue);
            C("plumbing", "Copperworks Mechanical", "555-0112", 1.35f, 5, 0.97f, 6, orange);
            C("electrical", "Sparky's Electric", "555-0127", 0.83f, 2, 0.58f, 1, orange);
            C("electrical", "Brightline Electric", "555-0161", 1.0f, 3, 0.86f, 3, white);
            C("electrical", "Ampere Electrical Contractors", "555-0199", 1.3f, 5, 0.97f, 6, blue);
            C("hvac", "Cool Breeze Heating & Air", "555-0144", 0.84f, 2, 0.6f, 2, white);
            C("hvac", "Comfort Pro HVAC", "555-0178", 1.0f, 3, 0.86f, 4, red);
            C("hvac", "Allseason Mechanical", "555-0116", 1.28f, 5, 0.96f, 7, green);
            C("insulation", "Fluff & Stuff Insulation", "555-0139", 0.82f, 1, 0.6f, 1, tan);
            C("insulation", "ThermoSeal Insulation", "555-0167", 1.0f, 3, 0.88f, 2, orange);
            C("insulation", "Envelope Energy Pros", "555-0191", 1.3f, 5, 0.97f, 5, green);
            C("drywall", "Mud Slingers Drywall", "555-0124", 0.8f, 1, 0.55f, 1, white);
            C("drywall", "Smooth Wall Drywall", "555-0152", 1.0f, 3, 0.86f, 3, gray);
            C("drywall", "Level Five Interiors", "555-0186", 1.3f, 5, 0.97f, 6, black);
            C("painting", "Weekend Warrior Painting", "555-0133", 0.8f, 1, 0.55f, 1, white);
            C("painting", "True Color Painters", "555-0159", 1.0f, 3, 0.87f, 3, blue);
            C("painting", "Finish Line Coatings", "555-0181", 1.3f, 5, 0.97f, 5, red);
            C("flooring", "Floor Bros", "555-0146", 0.84f, 2, 0.6f, 2, tan);
            C("flooring", "Heartwood Flooring", "555-0172", 1.0f, 3, 0.87f, 3, tan);
            C("flooring", "Plank & Tile Artisans", "555-0114", 1.32f, 5, 0.97f, 6, black);
            C("landscaping", "Mow Better Lawns", "555-0129", 0.82f, 1, 0.6f, 1, green);
            C("landscaping", "Greenway Landscaping", "555-0165", 1.0f, 3, 0.88f, 2, green);
            C("landscaping", "Evergreen Grounds", "555-0197", 1.3f, 5, 0.97f, 5, green);
        }

        public static string QualityLabel(int q)
        {
            switch (Mathf.Clamp(q, 1, 5)) { case 1: return "Poor"; case 2: return "Fair"; case 3: return "Good"; case 4: return "Very good"; default: return "Excellent"; }
        }

        public static string ReliabilityLabel(float r) => r < 0.65f ? "Often late" : r < 0.9f ? "Usually on time" : "Always on time";
    }
}
