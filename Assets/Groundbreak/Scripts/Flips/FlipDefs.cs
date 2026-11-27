using System.Collections.Generic;
using UnityEngine;
using C = Groundbreak.RepairCatalog;

namespace Groundbreak
{
    /// <summary>One kind of thing that can be wrong with (or upgraded in) a flip house. Every entry plays through job-catalog
    /// definitions (<see cref="JobDef"/>), so the player fixes it with the same tools and steps as a customer's service call.</summary>
    public class FlipItemDef
    {
        public string Id, Label, Report;          // Report: what the inspector wrote on the condition report
        public bool Upgrade;
        public string[] Defs;                      // catalog definition ids (one defect each)
        public int Value;                          // problem: what buyers knock off while it's not fixed; upgrade: value it adds
        public string Trade;                       // crew trade that does it best
        public bool Always;                        // every distressed house has it
    }

    /// <summary>The flip punch-list library: problems (from the inspection) and optional upgrades, plus the two flip-only catalog
    /// tasks (yard trash-out, front landscaping) and their models.</summary>
    public static class FlipDefs
    {
        public const string TrashOut = "flip.trash-out", Landscape = "flip.landscape";

        // Buyer discounts: what a buyer's inspection/appraisal knocks off a ~1,200 sq ft ranch (US, 2026 contractor pricing incl. hassle).
        public static readonly List<FlipItemDef> Problems = new List<FlipItemDef>
        {
            P("yard", "Overgrown yard + trash-out", "Knee-high weeds, junk in the yard, a boarded-up window. Needs a full trash-out.", 4500, "laborer", TrashOut).Always(),
            P("roof", "Roof leak", "Active roof leak: missing/failed shingles or flashing. Tarped by the bank.", 8500, "roofer", "ext.roof-leak"),
            P("siding", "Damaged siding", "Front siding cracked/loose; water getting behind it.", 3000, "carpenter", "ext.siding"),
            P("fascia", "Rotten fascia", "Fascia board rotted behind the gutter.", 2500, "carpenter", "ext.fascia"),
            P("waterheater", "Failed water heater", "No hot water: the 40-gal electric water heater has failed.", 1800, "plumber", "plb.wh-no-hot"),
            P("ceiling", "Water-stained ceiling", "Ceiling stain in the bedroom from the old leak.", 1500, "drywall", "dw.ceiling-stain"),
            P("wall", "Damaged drywall", "Cracked drywall in the living room.", 900, "drywall", "dw.wall-crack"),
            P("floor", "Water-damaged floor", "Living room flooring buckled and stained; replace with vinyl plank.", 4000, "carpenter", "task.lvp-living"),
            P("gutter", "Leaking gutter", "Front gutter leaks / downspout off; water against the foundation.", 900, "roofer", "ext.gutter-leak"),
            P("fence", "Fence section down", "A back-yard fence section is leaning or broken.", 1500, "carpenter", "ext.fence-section"),
            P("sill", "Rotted window sill", "Living room window sill is soft and rotted.", 1200, "carpenter", "carp.rotted-sill"),
            P("smoke", "Smoke alarm missing/chirping", "Hall smoke alarm chirping: fails the sale inspection.", 300, "electrician", "el.smoke-chirp"),
        };

        // Upgrades: value added at sale (Remodeling cost-vs-value style returns on a small-town ranch).
        public static readonly List<FlipItemDef> Upgrades = new List<FlipItemDef>
        {
            U("kitchen", "New kitchen look", "Dated kitchen: 1990s oak cabinets and an old faucet.", 9000, "carpenter", "task.cabinets", "task.kitchen-faucet"),
            U("bath", "Bathroom refresh", "Dated bath: old vanity, old toilet, moldy grout.", 6500, "plumber", "task.vanity", "task.toilet", "task.regrout"),
            U("paint", "Fresh interior paint", "Scuffed, dark walls throughout.", 3500, "painter", "task.paint-living", "task.paint-bedroom"),
            U("flooring", "New tile bath floor", "Cracked, dated bath floor tile.", 2500, "carpenter", "task.bath-floor"),
            U("landscape", "Front landscaping", "No foundation plantings; bare front yard.", 3000, "landscaper", Landscape),
        };

        static FlipItemDef P(string id, string label, string report, int value, string trade, params string[] defs) =>
            new FlipItemDef { Id = id, Label = label, Report = report, Value = value, Trade = trade, Defs = defs };
        static FlipItemDef U(string id, string label, string report, int value, string trade, params string[] defs) =>
            new FlipItemDef { Id = id, Label = label, Report = report, Value = value, Trade = trade, Defs = defs, Upgrade = true };
        static FlipItemDef Always(this FlipItemDef d) { d.Always = true; return d; }

        public static FlipItemDef Get(string id)
        {
            foreach (var d in Problems) if (d.Id == id) return d;
            foreach (var d in Upgrades) if (d.Id == id) return d;
            return null;
        }

        /// <summary>What a contractor would charge for an item (labor at $85/h + parts with markup), for the estimate.</summary>
        public static int ContractorPrice(FlipItemDef item)
        {
            float p = 0f;
            foreach (var id in item.Defs)
            {
                var d = JobCatalog.Get(id); if (d == null || d.Causes.Count == 0) continue;
                var c = d.Causes[0];
                p += c.Hours * RepairJobs.LaborRate + d.MaterialCostFor(c) * RepairJobs.Markup;
            }
            return Mathf.RoundToInt(p / 50f) * 50;
        }

        public static float BookHours(FlipItemDef item)
        {
            float h = 0f;
            foreach (var id in item.Defs) { var d = JobCatalog.Get(id); if (d != null && d.Causes.Count > 0) h += d.Causes[0].Hours; }
            return h;
        }

        // ------------------------------------------------------------------ flip-only catalog tasks

        // house-local poses (TownHouse frame: +Z = street side, front wall at z = Depth/2, front door at x = FrontDoorX)
        static readonly JobPlace FrontYard = new JobPlace
        {
            Name = "front yard", Spot = HouseSpot.FrontDoor, Needs = new[] { HouseSpot.FrontDoor }, Room = "yard", Outdoor = true,
            Place = h => new Pose(new Vector3(-h.Width / 2f + 1.6f, 0f, h.Depth / 2f + 4.2f), Quaternion.identity),
        };
        static readonly JobPlace FrontBed = new JobPlace
        {
            Name = "front foundation bed", Spot = HouseSpot.FrontDoor, Needs = new[] { HouseSpot.FrontDoor }, Room = "yard", Outdoor = true,
            Place = h => new Pose(new Vector3((-h.Width / 2f + TownHouse.FrontDoorX - 0.6f) / 2f, 0f, h.Depth / 2f + 0.75f), Quaternion.identity),
        };

        static bool _registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() { _registered = false; Register(); }

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            var trash = Task(TrashOut, "Trash-out and yard cleanup", JobTrade.General)
                .At(FrontYard).Model("flip-trash", 2.4f, 1.0f, 1.6f)
                .Says("The yard is knee-high, there's junk everywhere and a window is boarded up.")
                .Cause("overgrown", "Overgrown, trash-filled yard", 1, 6f, "Weeds and saplings up to your knees, a dumped couch, tires and bags of trash. Plywood over the bedroom window.", "bag", "board", "weeds", "rake")
                .Prep("gloves", "Walk the yard, flag anything sharp", null, 2f)
                .Fix("bag", "Bag the trash and drag the junk to the dumpster", null, 8f).After("gloves")
                .Fix("board", "Pry the plywood off the boarded window", C.PryBar, 5f)
                .Fix("weeds", "Grub out the weeds and saplings", "spade", 10f).After("bag")
                .Fix("rake", "Rake out the dead grass and leaves", "rake", 8f).After("weeds")
                .Finish("sweep", "Sweep the walk and the stoop", C.Broom, 3f);
            var land = Task(Landscape, "Front foundation landscaping", JobTrade.General)
                .At(FrontBed).Model("flip-beds", 3.0f, 0.8f, 1.4f)
                .Says("Buyers like curb appeal: put in a bed of shrubs along the front.")
                .Cause("bed", "New foundation bed", 1, 5f, "Bare dirt along the front wall; plants and mulch are delivered on the driveway.", "edge", "soil", "plant", "mulch")
                .Fix("edge", "Cut a clean bed edge along the front", "spade", 8f)
                .Fix("soil", "Rake in topsoil, slope it away from the house", "rake", 8f).After("edge")
                .Fix("plant", "Plant the boxwoods and hydrangeas", "spade", 10f).After("soil")
                .Fix("mulch", "Spread 3\" of hardwood mulch", "rake", 6f).After("plant")
                .Finish("water", "Water everything in", C.Hose, 4f);
            JobCatalog.Register(trash, land);

            CatalogProps.Register("flip-trash", (m, c) =>
            {
                // bags of trash, a pile of old tires and a dumped couch (gone as the cleanup steps are done)
                if (!c.Did("bag"))
                {
                    int bag = m.Mat(SurfaceKind.DarkPlastic, new Color(0.12f, 0.12f, 0.13f));
                    for (int i = 0; i < 6; i++) m.L.B.AddLathe(new Vector3(-0.9f + i * 0.33f, 0f, (i % 2) * 0.35f), Quaternion.identity, LandModel.BlobProfile(0.24f, 0.62f, 6, 0.3f), 10, bag);
                    int tire = m.Mat(SurfaceKind.Tire);
                    for (int i = 0; i < 3; i++) m.L.B.AddRing(new Vector3(0.9f, 0.1f + i * 0.2f, -0.4f), Quaternion.identity, 0.33f, 0.19f, 0.19f, 18, tire);
                    int fab = m.Mat(new Color(0.42f, 0.36f, 0.28f));
                    m.Box(new Vector3(0f, 0.22f, -0.6f), new Vector3(1.9f, 0.44f, 0.85f), Quaternion.Euler(0, 8, 0), fab);
                    m.Box(new Vector3(0f, 0.6f, -0.95f), new Vector3(1.9f, 0.4f, 0.2f), Quaternion.Euler(-10, 8, 0), fab);
                }
                else if (!c.Did("sweep"))
                {
                    int bag = m.Mat(SurfaceKind.DarkPlastic, new Color(0.12f, 0.12f, 0.13f));
                    m.L.B.AddLathe(Vector3.zero, Quaternion.identity, LandModel.BlobProfile(0.2f, 0.5f, 6, 0.3f), 10, bag);
                }
            });
            CatalogProps.Register("flip-beds", (m, c) =>
            {
                var house = c.Defect != null ? c.Defect.House : null;
                float len = house != null ? Mathf.Max(1.5f, (TownHouse.FrontDoorX - 0.6f) - (-house.Width / 2f + 0.3f)) : 3f;
                float depth = 1.2f;
                if (c.Did("edge"))
                {
                    int dirt = m.Mat(SurfaceKind.Dirt);
                    m.Box(new Vector3(0, 0.01f, 0), new Vector3(len, 0.03f, depth), dirt);
                }
                var f0 = m.L.B.Transform;
                if (c.Did("plant"))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        float x = -len / 2f + 0.45f + i * (len - 0.9f) / 4f;
                        m.L.B.Transform = f0 * Matrix4x4.TRS(new Vector3(x, 0.02f, -0.05f), Quaternion.identity, Vector3.one);
                        if (i % 2 == 0) LandModels.Shrub(m.L, 31 + i, 0.75f, 0.7f, LandModels.LeafBox);
                        else LandModels.Shrub(m.L, 41 + i, 0.9f, 0.85f, new Color(0.7f, 0.9f, 0.6f), new Color(0.75f, 0.82f, 1f));
                        m.L.B.Transform = f0;
                    }
                }
                if (c.Did("mulch")) m.Box(new Vector3(0, 0.035f, 0), new Vector3(len - 0.04f, 0.03f, depth - 0.04f), m.L.M(LandSurface.Mulch));
                if (!c.Did("plant"))
                {
                    // plants on the driveway-side of the bed, waiting in their pots
                    for (int i = 0; i < 5; i++)
                    {
                        m.L.B.Transform = f0 * Matrix4x4.TRS(new Vector3(-len / 2f + 0.3f + i * 0.4f, 0f, depth / 2f + 0.5f), Quaternion.identity, Vector3.one);
                        LandModels.Pot(m.L, 0.16f, 0.24f);
                        m.L.B.Transform = f0 * Matrix4x4.TRS(new Vector3(-len / 2f + 0.3f + i * 0.4f, 0.24f, depth / 2f + 0.5f), Quaternion.identity, Vector3.one);
                        LandModels.Shrub(m.L, 51 + i, 0.4f, 0.4f, LandModels.LeafBox);
                        m.L.B.Transform = f0;
                    }
                }
            });
        }

        static JobDef Task(string id, string name, params JobTrade[] trades)
        {
            var d = new JobDef { Id = id, Name = name, Stage = CompanyStage.Contractor, Mode = JobMode.Service, Kind = JobKind.Remodel, Category = "task", Weight = 0f };
            d.Trade(trades);
            return d;
        }
    }
}
