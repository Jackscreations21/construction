using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum RoadClass { Residential, Downtown, Industrial, Highway, Rural }

    /// <summary>A map road: a <see cref="TownStreet"/> plus its cross-section. Core town streets are wrapped too (Core = true, drawn by TownBuilder).</summary>
    public class MapRoad
    {
        public TownStreet S;
        public RoadClass Class;
        public bool Core;
        public float Half => S.HalfWidth;                      // centreline → curb face / pavement edge
        public bool Curbs => Class == RoadClass.Residential || Class == RoadClass.Downtown || Class == RoadClass.Industrial;
        public bool Walks => Class == RoadClass.Residential || Class == RoadClass.Downtown;
        /// <summary>Centreline → private lot line (building face downtown).</summary>
        public float RowHalf;
        public bool EW => S.EastWest;
        public float Perp => EW ? S.A.z : S.A.x;
        public float Min => EW ? Mathf.Min(S.A.x, S.B.x) : Mathf.Min(S.A.z, S.B.z);
        public float Max => EW ? Mathf.Max(S.A.x, S.B.x) : Mathf.Max(S.A.z, S.B.z);
        public Vector3 At(float along, float off = 0f, float y = 0f) => EW ? new Vector3(along, y, Perp + off) : new Vector3(Perp + off, y, along);
        public float Along(Vector3 p) => EW ? p.x : p.z;
        public string Name => S.Name;
    }

    public class MapDistrict
    {
        public string Id, Name;
        public Rect Area;
        /// <summary>A ground-level viewpoint (eye position) and heading for screenshots / "go there" teleports.</summary>
        public Vector3 View; public float ViewYaw;
        public Vector3 Center => new Vector3(Area.center.x, 0, Area.center.y);
    }

    /// <summary>One map building on its lot.</summary>
    public class MapParcel
    {
        public TownLot Lot;
        public MapBuildingSpec Spec;           // null = vacant
        public float Setback;                  // lot line → building front
        public float OffsetX;                  // building centre offset along the frontage (lot-local x)
        public MapSite Site;
        public bool Ranch;                     // built as an enterable TownHouse (HouseBuilder) → repair service calls
        public MapRoad Road;
        public Vector3 Origin => Lot.Local(OffsetX, Setback);
        public Quaternion Rotation => Lot.Rotation;
    }

    public enum MapSite { None, Driveway, ParkingLot, Gravel, Farmyard, Lawn }
    public enum AreaKind { Field, Woods, Pond, Meadow, Hill, Orchard }

    /// <summary>Ground-cover / vegetation area (world XZ rect). Fields/meadows are flat quads; woods are tree scatter.</summary>
    public class MapArea
    {
        public AreaKind Kind;
        public Rect R;
        public FieldKind Field;
        public bool RowsAlongX = true;
        public float Density = 1f;             // woods: trees per 100 m²
        public float Height = 0f;              // hills: peak height
        public string LotId;                   // dressing of a Free lot: hidden once the lot is claimed
        public int Seed;
        /// <summary>Patchwork scenery (fields/hedges between farms): clipped against every road and lot when built.</summary>
        public bool Patch;
    }

    /// <summary>
    /// The full map layout (Docs/MAP.md): districts, roads, parcels, fields and woods around the core town. Deterministic and
    /// always available (like <see cref="Town"/>'s layout). Map lots are registered as <see cref="TownLot"/>s through
    /// <see cref="Town.ExtendLayout"/>, so <see cref="Town.Find"/>, <see cref="Town.Addresses"/>, <see cref="Town.FreeLots"/> and
    /// parking / fast travel work for every map building.
    /// </summary>
    public static class MapLayout
    {
        public static readonly List<MapRoad> Roads = new List<MapRoad>();
        public static readonly List<MapDistrict> Districts = new List<MapDistrict>();
        public static readonly List<MapParcel> Parcels = new List<MapParcel>();
        public static readonly List<MapArea> Areas = new List<MapArea>();
        /// <summary>Creek centreline (world XZ, y = 0), north to south.</summary>
        public static readonly List<Vector3> Creek = new List<Vector3>();
        /// <summary>Whole map extent (ground cover and scenery stop here; the far haze takes over).</summary>
        public static readonly Rect Bounds = Rect.MinMaxRect(-760f, -700f, 1180f, 980f);

        public static MapDistrict District(string id) => Districts.Find(d => d.Id == id);
        public static MapParcel ParcelOf(TownLot lot) => Parcels.Find(p => p.Lot == lot);
        public static MapRoad RoadOf(TownStreet s) => Roads.Find(r => r.S == s);

        /// <summary>District containing a world point (first match; downtown/strip before their surroundings), or null.</summary>
        public static MapDistrict DistrictAt(Vector3 p)
        {
            Ensure();
            foreach (var d in Districts) if (d.Area.Contains(new Vector2(p.x, p.z))) return d;
            return null;
        }

        /// <summary>The house plan a lot's building is drawn from (Docs/PLANS.md), or null when it has none / it is not registered.</summary>
        public static HousePlan PlanFor(TownLot lot) => lot == null || string.IsNullOrEmpty(lot.PlanId) ? null : HousePlans.Get(lot.PlanId);

        /// <summary>Default plan id per building type. Ids that no plan has yet are kept: other sessions register plans later.</summary>
        public static readonly Dictionary<string, string> PlanByType = new Dictionary<string, string>
        {
            { "ranch", "ranch-1600" }, { "ranch2", "ranch-1600" }, { "twostory", "twostory-2400" }, { "cape", "cottage-1200" },
            { "foursquare", "craftsman-1920" }, { "farmhouse", "farmhouse-2200" }, { "duplex", "duplex-2x1100" }, { "townhomes", "townhome-1400" },
            { "garage", "garage-24x24" }, { "storefront", "storefront-2story" }, { "office", "office-2story" }, { "diner", "diner" },
            { "metalshop", "metal-building-40x60" }, { "barn", "barn-gambrel" }, { "machineshed", "pole-barn-50x80" },
        };

        static bool _done;

        /// <summary>Makes sure the map is part of the town layout (cheap; normally done by Town's layout extension).</summary>
        public static void Ensure() { var _ = Town.Lots; if (!_done) Build(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Roads.Clear(); Districts.Clear(); Parcels.Clear(); Areas.Clear(); Creek.Clear(); _done = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { Town.ExtendLayout -= Build; Town.ExtendLayout += Build; }

        // ------------------------------------------------------------------ building the layout

        static MapRoad Road(string name, Vector3 a, Vector3 b, RoadClass c, bool openMin = false, bool openMax = false)
        {
            float half, row;
            switch (c)
            {
                case RoadClass.Downtown: half = 22f * Units.Foot; row = half + 0.15f + 12f * Units.Foot; break;   // 44' curb to curb, 12' walks
                case RoadClass.Industrial: half = 18f * Units.Foot; row = 10f; break;                            // 36' with curbs, no walks
                case RoadClass.Highway: half = 20f * Units.Foot; row = 15f; break;                               // 2 x 12' lanes + 8' shoulders
                case RoadClass.Rural: half = 11f * Units.Foot; row = 10f; break;                                 // 2 x 11' lanes, gravel shoulders
                default: half = Town.RoadHalf; row = Town.RowHalf; break;
            }
            var s = new TownStreet { Name = name, A = a, B = b, HalfWidth = half, OpenMin = openMin, OpenMax = openMax };
            Town.AddStreet(s);
            var r = new MapRoad { S = s, Class = c, RowHalf = row };
            Roads.Add(r);
            return r;
        }

        static MapRoad EW(string n, float z, float x0, float x1, RoadClass c) => Road(n, new Vector3(x0, 0, z), new Vector3(x1, 0, z), c);
        static MapRoad NS(string n, float x, float z0, float z1, RoadClass c) => Road(n, new Vector3(x, 0, z0), new Vector3(x, 0, z1), c);

        static void District(string id, string name, Rect area, Vector3 view, float yaw) =>
            Districts.Add(new MapDistrict { Id = id, Name = name, Area = area, View = view, ViewYaw = yaw });

        static readonly HashSet<string> _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static int _seed = 1234;

        /// <summary>A lot on one side of a road. side +1 = north (EW road) / east (NS road). a0..a1 along the road, depth back from the lot line.</summary>
        static Rect LotRect(MapRoad r, int side, float a0, float a1, float depth) => r.EW
            ? (side > 0 ? Rect.MinMaxRect(a0, r.Perp + r.RowHalf, a1, r.Perp + r.RowHalf + depth) : Rect.MinMaxRect(a0, r.Perp - r.RowHalf - depth, a1, r.Perp - r.RowHalf))
            : (side > 0 ? Rect.MinMaxRect(r.Perp + r.RowHalf, a0, r.Perp + r.RowHalf + depth, a1) : Rect.MinMaxRect(r.Perp - r.RowHalf - depth, a0, r.Perp - r.RowHalf, a1));

        /// <summary>True when a fill lot (houses / vacant) here would not overlap another lot, the build site or any street right-of-way.
        /// Bulk fill skips lots that fail (corners where a cross street's lots already are).</summary>
        static bool Fits(MapRoad r, int side, float a0, float a1, float depth)
        {
            var rect = LotRect(r, side, a0, a1, depth);
            var inner = Rect.MinMaxRect(rect.xMin + 0.05f, rect.yMin + 0.05f, rect.xMax - 0.05f, rect.yMax - 0.05f);
            if (inner.Overlaps(Town.BuildSiteRect)) return false;
            foreach (var l in Town.Lots) if (l.Rect.Overlaps(inner)) return false;
            foreach (var o in Roads)
            {
                var row = o.EW ? Rect.MinMaxRect(o.Min, o.Perp - o.RowHalf, o.Max, o.Perp + o.RowHalf) : Rect.MinMaxRect(o.Perp - o.RowHalf, o.Min, o.Perp + o.RowHalf, o.Max);
                if (row.Overlaps(inner)) return false;
            }
            return true;
        }

        static TownLot NewLot(MapRoad r, int side, float a0, float a1, float depth, int number, LotUse use, string resident, string district, string category)
        {
            Rect rect;
            Vector3 front;
            if (r.EW)
            {
                rect = side > 0 ? Rect.MinMaxRect(a0, r.Perp + r.RowHalf, a1, r.Perp + r.RowHalf + depth) : Rect.MinMaxRect(a0, r.Perp - r.RowHalf - depth, a1, r.Perp - r.RowHalf);
                front = side > 0 ? Vector3.back : Vector3.forward;
            }
            else
            {
                rect = side > 0 ? Rect.MinMaxRect(r.Perp + r.RowHalf, a0, r.Perp + r.RowHalf + depth, a1) : Rect.MinMaxRect(r.Perp - r.RowHalf - depth, a0, r.Perp - r.RowHalf, a1);
                front = side > 0 ? Vector3.left : Vector3.right;
            }
            string key = r.Name.Split(' ')[0].ToLowerInvariant();
            if (r.Name == "Route 9") key = "rt9"; else if (r.Name == "County Rd 9") key = "cr9";
            string id = key + "-" + number;
            while (_ids.Contains(id) || Town.Find(id) != null) { number += 2; id = key + "-" + number; }
            _ids.Add(id);
            var lot = Town.AddLot(id, r.Name, number, use, rect, front, resident);
            lot.District = district; lot.Category = category;
            // curb-side parking for this road's cross-section (Town's default assumes the 30' core street)
            float curbOff = r.RowHalf - r.Half + (r.Curbs ? 1.2f : -1.6f);
            lot.Parking = lot.FrontMid + front * curbOff;
            lot.ParkingYaw = Quaternion.LookRotation(Quaternion.Euler(0, 90, 0) * front).eulerAngles.y;
            lot.FrontDoor = lot.FrontMid - front * 3f;
            return lot;
        }

        /// <summary>Adds a building parcel. Parking/door are set from the site kind.</summary>
        static MapParcel Building(MapRoad r, int side, float a0, float a1, float depth, int number, string type, float w, float d, float setback,
            MapSite site, string district, string category, string resident, string sign = "", int stories = 1, int units = 1, bool jobSite = true,
            Color? color = null, Color? accent = null, float offsetX = 0f)
        {
            var lot = NewLot(r, side, a0, a1, depth, number, jobSite ? LotUse.Customer : LotUse.Company, resident, district, category);
            lot.TypeId = type;
            lot.ServiceCalls = false;
            if (PlanByType.TryGetValue(type, out var plan)) lot.PlanId = plan;
            int seed = _seed = _seed * 1103515245 + 12345 & 0x7fffffff;
            var spec = new MapBuildingSpec
            {
                Type = type, Width = w, Depth = d, Stories = stories, Units = units, Sign = sign, Seed = seed,
                Color = color ?? Palette(category, seed), Accent = accent ?? Accents[(seed / 7) % Accents.Length],
                LotWidth = lot.Frontage, FrontYard = setback, BackYard = Mathf.Max(0f, depth - setback - d),
            };
            var p = new MapParcel { Lot = lot, Spec = spec, Setback = setback, OffsetX = offsetX, Site = site, Road = r };
            switch (site)
            {
                case MapSite.Driveway:
                    lot.Parking = lot.Local(lot.Frontage / 2f - 1f - Town.DrivewayWidth / 2f, Mathf.Min(6.5f, setback - 1f));
                    lot.ParkingYaw = Quaternion.LookRotation(-lot.Front).eulerAngles.y;
                    lot.FrontDoor = lot.Local(offsetX, setback - 1.5f);
                    break;
                case MapSite.ParkingLot:
                case MapSite.Gravel:
                case MapSite.Farmyard:
                    lot.Parking = lot.Local(Mathf.Clamp(offsetX + w / 2f + 4f, -lot.Frontage / 2f + 3f, lot.Frontage / 2f - 3f) , Mathf.Max(4f, setback * 0.5f));
                    lot.ParkingYaw = Quaternion.LookRotation(-lot.Front).eulerAngles.y;
                    lot.FrontDoor = lot.Local(offsetX, setback - 1.5f);
                    break;
                default:
                    lot.FrontDoor = lot.Local(offsetX, Mathf.Max(-1f, setback - 1.5f));
                    break;
            }
            // gas station: pull up between the middle pump islands under the canopy (the price pylon owns the front corner)
            if (type == "gasstation") lot.Parking = lot.Local(offsetX, setback + d - Mathf.Min(11f, d * 0.33f) - 9.5f);
            Parcels.Add(p);
            return p;
        }

        /// <summary>A vacant lot (LotUse.Free) for new builds, dressed as meadow until claimed.</summary>
        static TownLot Vacant(MapRoad r, int side, float a0, float a1, float depth, int number, string district, string category, FieldKind cover = FieldKind.Pasture)
        {
            var lot = NewLot(r, side, a0, a1, depth, number, LotUse.Free, "", district, category);
            lot.ServiceCalls = false;
            var rr = lot.Rect;
            Areas.Add(new MapArea { Kind = AreaKind.Meadow, R = Rect.MinMaxRect(rr.xMin + 0.5f, rr.yMin + 0.5f, rr.xMax - 0.5f, rr.yMax - 0.5f), Field = cover, LotId = lot.Id, Seed = number });
            Parcels.Add(new MapParcel { Lot = lot, Road = r });
            return lot;
        }

        static readonly Color[] Sidings =
        {
            new Color(0.95f, 0.95f, 0.92f), new Color(0.80f, 0.82f, 0.80f), new Color(0.86f, 0.80f, 0.66f), new Color(0.70f, 0.78f, 0.68f),
            new Color(0.66f, 0.74f, 0.82f), new Color(0.93f, 0.88f, 0.76f), new Color(0.74f, 0.72f, 0.66f), new Color(0.58f, 0.62f, 0.60f),
            new Color(0.84f, 0.78f, 0.62f), new Color(0.47f, 0.55f, 0.63f), new Color(0.96f, 0.93f, 0.84f), new Color(0.62f, 0.52f, 0.44f),
        };
        static readonly Color[] Bricks = { new Color(1f, 1f, 1f), new Color(0.92f, 0.80f, 0.70f), new Color(1.05f, 0.95f, 0.85f), new Color(0.80f, 0.70f, 0.65f), new Color(1.1f, 1.05f, 0.95f) };
        static readonly Color[] Metals = { new Color(0.85f, 0.85f, 0.83f), new Color(0.62f, 0.68f, 0.72f), new Color(0.80f, 0.76f, 0.66f), new Color(0.55f, 0.20f, 0.16f), new Color(0.35f, 0.45f, 0.38f) };
        static readonly Color[] Accents =
        {
            new Color(0.12f, 0.25f, 0.18f), new Color(0.55f, 0.10f, 0.08f), new Color(0.12f, 0.18f, 0.35f), new Color(0.15f, 0.15f, 0.15f),
            new Color(0.85f, 0.80f, 0.70f), new Color(0.40f, 0.22f, 0.12f), new Color(0.20f, 0.35f, 0.45f),
        };
        static Color Palette(string category, int seed)
        {
            switch (category)
            {
                case "commercial": case "civic": return Bricks[seed % Bricks.Length];
                case "industrial": case "farm": return Metals[seed % Metals.Length];
                default: return Sidings[seed % Sidings.Length];
            }
        }

        static readonly string[] Shops =
        {
            "MAIN ST CAFE", "BARBER SHOP", "ANTIQUES", "BOOKS", "PHARMACY", "FLORIST", "BAKERY", "INSURANCE", "LAW OFFICE", "REALTY",
            "SHOES", "GIFTS", "TAVERN", "PIZZA", "THRIFT STORE", "JEWELERS", "DENTIST", "TAILOR", "HARDWARE", "FIVE & DIME", "QUILTS",
            "SPORTING GOODS", "TITLE CO", "VFW POST 112", "PHOTO STUDIO", "CANDY", "MUSIC", "OPTICAL", "COFFEE", "TOYS", "CPA", "SALON",
        };
        static int _shop;
        static string NextShop() => Shops[_shop++ % Shops.Length];

        static readonly string[] Families =
        {
            "The Harpers", "Gloria Mendez", "Ray & Donna Fisk", "Imani Cole", "The Novaks", "Walt Brennan", "Priya Shah", "Sam Ortiz",
            "The Lindgrens", "June Park", "Frank DeLuca", "Rosa Jimenez", "The Abernathys", "Kenji Mori", "Theo Grant", "Dolores Kim",
            "The Hendersons", "Mabel Fry", "Andre Lewis", "Ellie Watts", "The Duboises", "Hal Peterson", "Nora Quinn", "Victor Hale",
            "The Sorensens", "Bea Thornton", "Oscar Reyes", "Lena Vogel", "The Mitchells", "Gus Romano", "Tara Singh", "Cal Whitaker",
            "The Ashworths", "Ida Nguyen", "Boyd Carter", "Fern Gallagher", "The Ramseys", "Joe Kaminski", "Ana Costa", "Lyle Dawson",
        };
        static int _fam;
        static string NextFamily() => Families[_fam++ % Families.Length];

        static void Build()
        {
            if (_done) return;
            _done = true;
            _ids.Clear(); _seed = 1234; _shop = 0; _fam = 0;

            // ---- the core streets continue into the map (no barricades where they join)
            foreach (var s in Town.Streets)
            {
                s.OpenMin = s.OpenMax = true;
                Roads.Add(new MapRoad { S = s, Class = RoadClass.Residential, Core = true, RowHalf = Town.RowHalf });
            }

            // ---- road network (Docs/MAP.md)
            var mainW1 = EW("Main St", Town.MainZ, -255f, -Town.StreetEnd, RoadClass.Residential);
            var mainDT = EW("Main St", Town.MainZ, -505f, -255f, RoadClass.Downtown);
            var mainW2 = EW("Main St", Town.MainZ, -620f, -505f, RoadClass.Residential);
            var mainE = EW("Main St", Town.MainZ, Town.StreetEnd, 420f, RoadClass.Rural);
            var cedarW = EW("Cedar Ln", Town.CedarZ, -560f, -Town.StreetEnd, RoadClass.Residential);
            var cedarE = EW("Cedar Ln", Town.CedarZ, Town.StreetEnd, 420f, RoadClass.Industrial);
            var rt9 = EW("Route 9", -330f, -760f, 1180f, RoadClass.Highway);
            var birch = EW("Birch St", 250f, -560f, 420f, RoadClass.Residential);
            var elm = EW("Elm St", 370f, -330f, 420f, RoadClass.Residential);
            var cr9 = EW("County Rd 9", 600f, -620f, 1180f, RoadClass.Rural);
            var oakS = NS("Oak St", Town.OakX, -330f, -Town.StreetEnd, RoadClass.Residential);
            var oakS2 = NS("Oak St", Town.OakX, -700f, -330f, RoadClass.Rural);
            var mapleS = NS("Maple Ave", Town.MapleX, -330f, -Town.StreetEnd, RoadClass.Residential);
            var oakN = NS("Oak St", Town.OakX, Town.NorthEnd, 470f, RoadClass.Residential);
            var oakN2 = NS("Oak St", Town.OakX, 470f, 600f, RoadClass.Rural);
            var mapleN = NS("Maple Ave", Town.MapleX, Town.NorthEnd, 370f, RoadClass.Residential);
            var church = NS("Church St", -330f, -330f, 370f, RoadClass.Residential);
            var court = NS("Court St", -450f, -330f, 250f, RoadClass.Residential);
            var mill = NS("Mill Rd", 420f, -700f, 980f, RoadClass.Rural);
            var prairie = NS("Prairie Rd", -620f, -700f, 600f, RoadClass.Rural);

            // ---- districts (order matters for DistrictAt: specific first)
            District("downtown", "Downtown Main Street", Rect.MinMaxRect(-505f, -100f, -255f, -20f), new Vector3(-300f, 1.7f, -55f), 270f);
            District("strip", "Route 9 Commercial Strip", Rect.MinMaxRect(-330f, -420f, 240f, -245f), new Vector3(-60f, 1.7f, -325f), 90f);
            District("industrial", "Cedar Lane Industrial Park", Rect.MinMaxRect(232f, -322f, 420f, -67f), new Vector3(250f, 1.7f, -166f), 90f);
            District("school", "Birch Street School & Park", Rect.MinMaxRect(-560f, 120f, -240f, 330f), new Vector3(-300f, 1.7f, 245f), 270f);
            District("westside", "West Side", Rect.MinMaxRect(-560f, -250f, -232f, 120f), new Vector3(-300f, 1.7f, -166f), 270f);
            District("north", "North End", Rect.MinMaxRect(-240f, 150f, 420f, 470f), new Vector3(0f, 1.7f, 254f), 90f);
            District("eastside", "East Main", Rect.MinMaxRect(232f, -66f, 420f, 150f), new Vector3(245f, 1.7f, -56f), 90f);
            District("oldtown", "Old Town", Rect.MinMaxRect(-232f, -232f, 232f, 150f), new Vector3(-40f, 1.7f, -64f), 90f);
            District("devland", "Mill Road Countryside (development land)", Rect.MinMaxRect(420f, -330f, 1180f, 980f), new Vector3(430f, 1.7f, 300f), 60f);
            District("farms", "Farm Country", Bounds, new Vector3(-600f, 1.7f, 596f), 100f);

            BuildDowntown(mainDT, church, court);
            BuildWestSide(cedarW, church, court, mainW2);
            BuildSchoolPark(birch, court, church);
            BuildNorth(birch, elm, oakN, mapleN);
            BuildEastMain(mainE);
            BuildIndustrial(cedarE, rt9, mill);
            BuildStrip(rt9, oakS, mapleS);
            BuildFarms(rt9, cr9, prairie, oakS2, mill, oakN2);
            BuildDevLand(mill, rt9, cr9);
            BuildCoreFill();
            BuildCreek();
            AfterLayout?.Invoke();
        }

        /// <summary>Raised once the map layout (parcels, lots) exists and before the 3D map is built: post-process parcels here (MapFollowup).</summary>
        public static event Action AfterLayout;

        // ------------------------------------------------------------------ downtown

        static void BuildDowntown(MapRoad main, MapRoad church, MapRoad court)
        {
            const float D = 26f, Deep = 40f;   // building depth, lot depth (alley parking behind)
            // block faces along Main St between the cross streets (minus their right-of-way)
            var blocks = new List<(float a0, float a1)> { (-505f + 2f, court.S.A.x - court.RowHalf), (court.S.A.x + court.RowHalf, church.S.A.x - church.RowHalf), (church.S.A.x + church.RowHalf, -257f) };
            int num = 401;
            for (int side = 1; side >= -1; side -= 2)
            {
                num = side > 0 ? 401 : 400;
                for (int bi = blocks.Count - 1; bi >= 0; bi--)
                {
                    var (a0, a1) = blocks[bi];
                    // civic anchors
                    var anchors = new List<(string type, float w, float setback, string sign, string cat, int stories)>();
                    if (side > 0 && bi == 1) anchors.Add(("townhall", 34f, 12f, "TOWN HALL", "civic", 2));
                    if (side > 0 && bi == 2) anchors.Add(("bank", 16f, 3f, "FIRST STATE BANK", "commercial", 2));
                    if (side > 0 && bi == 0) anchors.Add(("church", 18f, 10f, "", "civic", 1));
                    if (side < 0 && bi == 2) anchors.Add(("diner", 18f, 4f, "BLUE PLATE DINER", "commercial", 1));
                    if (side < 0 && bi == 1) { anchors.Add(("library", 22f, 6f, "PUBLIC LIBRARY", "civic", 2)); anchors.Add(("postoffice", 18f, 6f, "US POST OFFICE", "civic", 1)); }
                    if (side < 0 && bi == 0) anchors.Add(("firestation", 22f, 8f, "FIRE DEPT", "civic", 2));
                    float x = a1;
                    int ai = 0, k = 0;
                    while (x - a0 > 7.4f)
                    {
                        float w; string type = "storefront", sign, cat = "commercial"; float setback = 0f; int stories;
                        bool anchor = ai < anchors.Count && (k == 2 + ai * 4 || x - a0 < anchors[ai].w + 8f);
                        if (anchor && x - a0 >= anchors[ai].w)
                        {
                            var an = anchors[ai++];
                            type = an.type; w = an.w; setback = an.setback; sign = an.sign; cat = an.cat; stories = an.stories;
                        }
                        else
                        {
                            w = new[] { 7.6f, 8.5f, 9.1f, 7.6f, 10.0f, 8.2f }[(k + bi) % 6];
                            if (x - a0 - w < 7.4f) w = x - a0;        // last building takes the rest of the block
                            if (w > 12.5f) w = 12.5f;
                            sign = NextShop(); stories = (k + bi) % 5 == 4 ? 1 : (k % 4 == 3 ? 3 : 2);
                            if (k % 6 == 3 && (bi + side) % 2 != 0) { Vacant(main, side, x - w, x, Deep, num, "downtown", "commercial", FieldKind.Pasture); num += 2; x -= w; k++; continue; }
                        }
                        float lotW = w;
                        Building(main, side, x - lotW, x, Deep, num, type, lotW - (type == "storefront" ? 0f : 4f), type == "storefront" ? D : Mathf.Min(D, Deep - setback - 2f), setback,
                            type == "storefront" ? MapSite.None : MapSite.Lawn, "downtown", cat, type == "storefront" ? sign : (sign.Length > 0 ? sign : "First Methodist Church"), sign, stories,
                            jobSite: true);
                        num += 2; x -= lotW; k++;
                    }
                }
            }
            // downtown lots share loading-zone stalls in the parking lane (one every 25 m) instead of a stall each
            foreach (var p in Parcels)
            {
                if (p.Road != main) continue;
                int side = (p.Lot.Center.z - main.Perp) > 0 ? 1 : -1;
                float a = main.Along(p.Lot.FrontMid), spot = Mathf.Round(a / 25f) * 25f;
                var blk = blocks.Find(b => a >= b.a0 - 1f && a <= b.a1 + 1f);
                spot = Mathf.Clamp(spot, blk.a0 + 4f, blk.a1 - 4f);
                p.Lot.Parking = main.At(spot, side * (main.Half - 1.25f), 0f);
            }
        }

        // ------------------------------------------------------------------ west side (older homes) + school/park

        static readonly string[] OldTypes = { "foursquare", "cape", "twostory", "foursquare", "twostory", "cape" };

        static void Houses(MapRoad r, int side, float a0, float a1, float lotW, float depth, int num, string district, string[] types, int vacantEvery = 0, float setback = 8f)
        {
            int k = 0;
            for (float x = a0; x + lotW <= a1 + 0.01f; x += lotW, k++, num += 2)
            {
                if (!Fits(r, side, x, x + lotW, depth)) continue;
                if (vacantEvery > 0 && k % vacantEvery == vacantEvery - 1) { Vacant(r, side, x, x + lotW, depth, num, district, "residential"); continue; }
                string t = types[(k + num / 2) % types.Length];
                float w = t == "townhomes" ? lotW - 4f : t == "duplex" ? Mathf.Min(16f, lotW - 4f) : t == "twostory" || t == "ranch2" ? Mathf.Min(13.5f, lotW - 5f) : Mathf.Min(10.5f, lotW - 6f);
                float d = t == "townhomes" ? 11f : t == "ranch2" ? 10f : 9f;
                int units = t == "townhomes" ? Mathf.Max(3, Mathf.FloorToInt((lotW - 4f) / 6.5f)) : t == "duplex" ? 2 : 1;
                int stories = t == "twostory" || t == "townhomes" || t == "foursquare" ? 2 : t == "duplex" ? 1 + k % 2 : 1;
                Building(r, side, x, x + lotW, depth, num, t, w, d, setback, MapSite.Driveway, district, "residential", t == "townhomes" ? "Townhome owners' association" : NextFamily(), "", stories, units);
            }
        }

        static void BuildWestSide(MapRoad cedarW, MapRoad church, MapRoad court, MapRoad mainW2)
        {
            float cx = church.Perp, kx = court.Perp, R = Town.RowHalf;
            // Cedar Ln west: both sides, three blocks (Oak-side end at -232)
            foreach (var (a0, a1, b) in new[] { (-556f, kx - R, 0), (kx + R, cx - R, 1), (cx + R, -233f, 2) })
            {
                Houses(cedarW, 1, a0, a1, 20f, 34f, 401 + b * 100, "westside", OldTypes, b == 1 ? 5 : 0);
                Houses(cedarW, -1, a0, a1, 20f, 36f, 400 + b * 100, "westside", OldTypes, b == 2 ? 4 : 0);
            }
            // Church St and Court St, south of Main St (between Cedar and the downtown back lots) and north of downtown
            Houses(church, -1, Town.CedarZ + R, -110f, 20f, 34f, 101, "westside", OldTypes);
            Houses(church, 1, Town.CedarZ + R, -110f, 20f, 34f, 102, "westside", OldTypes);
            Houses(court, -1, Town.CedarZ + R, -110f, 20f, 34f, 101, "westside", OldTypes);
            Houses(court, 1, Town.CedarZ + R, -110f, 20f, 34f, 102, "westside", OldTypes, 3);
            Houses(church, -1, 10f, 130f, 20f, 34f, 201, "westside", OldTypes, 5);
            Houses(church, 1, 10f, 130f, 20f, 34f, 202, "westside", OldTypes);
            Houses(court, 1, 10f, 130f, 20f, 34f, 202, "westside", OldTypes);
            Houses(court, -1, 10f, 130f, 20f, 34f, 201, "westside", OldTypes, 4);
            // south of Cedar on Church/Court down to Route 9
            Houses(church, -1, -310f, Town.CedarZ - R, 20f, 34f, 1, "westside", OldTypes, 3);
            Houses(church, 1, -240f, Town.CedarZ - R, 20f, 34f, 2, "westside", OldTypes);
            Houses(court, 1, -310f, Town.CedarZ - R, 20f, 34f, 2, "westside", OldTypes, 4);
            // west end of Main St: a few farmhouse-style homes beyond downtown
            Houses(mainW2, 1, -600f, -510f, 30f, 50f, 701, "westside", new[] { "farmhouse", "ranch2", "cape" }, 0, 14f);
            Houses(mainW2, -1, -600f, -510f, 30f, 50f, 700, "westside", new[] { "ranch2", "farmhouse", "twostory" }, 3, 14f);
        }

        static void BuildSchoolPark(MapRoad birch, MapRoad court, MapRoad church)
        {
            // Lincoln Elementary on the north side of Birch St (west of Church St)
            Building(birch, 1, -556f, -378f, 110f, 501, "school", 86f, 34f, 22f, MapSite.ParkingLot, "school", "civic", "Lincoln Elementary School", "LINCOLN ELEMENTARY", 1, 1);
            // ball fields behind the school are part of the lot; the park on the south side of Birch between Court and Church
            Areas.Add(new MapArea { Kind = AreaKind.Pond, R = Rect.MinMaxRect(-420f, 170f, -380f, 200f), Seed = 3 });
            // Birch St, north side east of Church St: houses up to Oak St
            Houses(birch, 1, church.Perp + Town.RowHalf, Town.OakX - Town.RowHalf, 20f, 38f, 601, "north", new[] { "twostory", "cape", "ranch2", "twostory" }, 6);
            Houses(birch, -1, church.Perp + Town.RowHalf, Town.OakX - Town.RowHalf - 4f, 20f, 38f, 600, "north", new[] { "ranch2", "twostory", "cape" }, 5);
            Houses(church, 1, 162f, 202f, 20f, 34f, 302, "north", new[] { "twostory", "cape" });
            Houses(church, -1, 258f, 360f, 20f, 34f, 401, "school", new[] { "duplex", "twostory" });
        }

        // ------------------------------------------------------------------ north end (newer subdivision-style streets)

        static void BuildNorth(MapRoad birch, MapRoad elm, MapRoad oakN, MapRoad mapleN)
        {
            float R = Town.RowHalf, ox = Town.OakX, mx = Town.MapleX;
            // Birch St between Oak and Maple: enterable ranch houses (repair service calls), both sides
            Ranches(birch, 1, ox + R + 1f, mx - R, 701);
            Ranches(birch, -1, ox + R + 1f, mx - R, 700);
            // Birch east of Maple to Mill Rd: two-storeys + duplexes + vacant lots
            Houses(birch, 1, mx + R, 408f, 22f, 38f, 801, "north", new[] { "twostory", "duplex", "twostory", "ranch2" }, 5);
            Houses(birch, -1, mx + R, 300f, 22f, 38f, 800, "north", new[] { "ranch2", "twostory", "cape" }, 4);
            // Elm St: townhome rows east of Maple, duplexes / two-storeys west of Oak, new-build lots between
            Houses(elm, -1, mx + R, 300f, 42f, 36f, 900, "north", new[] { "townhomes" });
            Houses(elm, 1, mx + R, 230f, 42f, 36f, 901, "north", new[] { "townhomes", "townhomes", "duplex" }, 4);
            Houses(elm, 1, 270f, 408f, 22f, 38f, 921, "north", new[] { "twostory", "duplex", "ranch2" }, 3);
            Houses(elm, 1, -322f, ox - R, 22f, 38f, 701, "north", new[] { "duplex", "twostory", "duplex", "cape" }, 4);
            Houses(elm, -1, -322f, ox - R, 22f, 38f, 700, "north", new[] { "twostory", "duplex", "ranch2" });
            for (float x = ox + R; x + 20f <= mx - R; x += 20f)
            {
                int n = 801 + (int)((x - ox) / 20f) * 2;
                Vacant(elm, 1, x, x + 20f, 38f, n, "north", "residential", FieldKind.Hay);
                Vacant(elm, -1, x, x + 20f, 38f, n - 1, "north", "residential", FieldKind.Pasture);
            }
            // Oak St / Maple Ave north extensions: side lots
            // side-street lots only where the Birch / Elm lots (z 204.5..295.5 and 324.5..415.5) leave room
            Houses(oakN, -1, 162f, 202f, 20f, 38f, 501, "north", new[] { "twostory", "cape" });
            Houses(oakN, 1, 162f, 202f, 20f, 38f, 502, "north", new[] { "ranch2", "twostory" });
            Houses(mapleN, -1, 162f, 202f, 20f, 38f, 501, "north", new[] { "cape", "ranch2" });
            Houses(mapleN, 1, 162f, 202f, 20f, 38f, 502, "north", new[] { "cape", "twostory", "ranch2" });
            Houses(oakN, -1, 420f, 460f, 20f, 38f, 701, "north", new[] { "ranch2", "farmhouse" });
            Houses(oakN, 1, 420f, 460f, 20f, 38f, 702, "north", new[] { "cape", "twostory" }, 2);
            // water tower on the hill behind Elm St
            Building(elm, 1, 234f, 266f, 40f, 1001, "watertower", 16f, 16f, 14f, MapSite.Gravel, "north", "civic", "Water Department", "MILLBROOK", 1, 1, jobSite: false);
        }

        static void Ranches(MapRoad r, int side, float a0, float a1, int num)
        {
            for (float x = a0; x + Town.LotFrontage <= a1 + 0.01f; x += Town.LotFrontage, num += 2)
            {
                var p = Building(r, side, x, x + Town.LotFrontage, Town.LotDepth, num, "ranch", 11f, 8.5f, TownHouse.Setback, MapSite.Driveway, "north", "residential", NextFamily());
                p.Ranch = true;
                p.Lot.ServiceCalls = true;
                // Town's own driveway conventions for an enterable ranch
                float dx = Town.LotFrontage / 2f - 1f - Town.DrivewayWidth / 2f;
                p.Lot.Parking = p.Lot.Local(dx, 6.5f);
                p.Lot.FrontDoor = p.Lot.Local(TownHouse.FrontDoorX, TownHouse.Setback - 1.2f);
            }
        }

        // ------------------------------------------------------------------ east main, industrial park, strip

        static void BuildEastMain(MapRoad mainE)
        {
            Building(mainE, 1, 240f, 285f, 60f, 1001, "metalshop", 28f, 22f, 22f, MapSite.Gravel, "eastside", "commercial", "Valley Farm & Feed", "FARM & FEED", 1);
            Building(mainE, 1, 290f, 320f, 50f, 1003, "church", 14f, 24f, 12f, MapSite.Lawn, "eastside", "civic", "Grace Community Church", "", 1);
            Houses(mainE, 1, 324f, 408f, 28f, 48f, 1005, "eastside", new[] { "farmhouse", "ranch2", "cape" }, 0, 14f);
            Houses(mainE, -1, 240f, 408f, 28f, 30f, 1002, "eastside", new[] { "ranch2", "twostory", "cape", "ranch2", "foursquare" }, 4, 12f);
            Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(236f, 16f, 408f, 145f), Density = 0.35f, Seed = 21 });
        }

        static void BuildIndustrial(MapRoad cedarE, MapRoad rt9, MapRoad mill)
        {
            Building(cedarE, 1, 242f, 332f, 58f, 1101, "lumberyard", 86f, 52f, 4f, MapSite.Gravel, "industrial", "industrial", "Tri-County Lumber", "TRI-COUNTY LUMBER");
            Building(cedarE, 1, 336f, 372f, 58f, 1103, "metalshop", 24f, 30f, 16f, MapSite.Gravel, "industrial", "industrial", "Kessler Welding & Fab", "KESSLER FAB");
            Building(cedarE, 1, 376f, 408f, 58f, 1105, "contractoryard", 30f, 44f, 6f, MapSite.Gravel, "industrial", "industrial", "Dunn Excavating", "DUNN EXCAVATING");
            Building(cedarE, -1, 242f, 310f, 72f, 1102, "concreteplant", 62f, 58f, 8f, MapSite.Gravel, "industrial", "industrial", "Valley Ready Mix", "VALLEY READY MIX");
            Building(cedarE, -1, 314f, 368f, 66f, 1104, "warehouse", 46f, 36f, 18f, MapSite.ParkingLot, "industrial", "industrial", "Midstate Distribution", "MIDSTATE");
            Building(cedarE, -1, 372f, 408f, 66f, 1106, "selfstorage", 34f, 56f, 6f, MapSite.ParkingLot, "industrial", "industrial", "Stor-All Self Storage", "STOR-ALL", 1, 3);
            // Route 9 frontage behind the industrial park: a warehouse and vacant industrial lots (company yards)
            Building(rt9, 1, 242f, 300f, 60f, 1201, "warehouse", 50f, 34f, 18f, MapSite.ParkingLot, "industrial", "industrial", "Riverside Supply", "RIVERSIDE SUPPLY");
            Vacant(rt9, 1, 304f, 356f, 60f, 1203, "industrial", "industrial", FieldKind.Pasture);
            Vacant(rt9, 1, 360f, 408f, 60f, 1205, "industrial", "industrial", FieldKind.Pasture);
        }

        static void BuildStrip(MapRoad rt9, MapRoad oakS, MapRoad mapleS)
        {
            float R = rt9.RowHalf, ox = Town.OakX, mx = Town.MapleX, oR = Town.RowHalf;
            // north side (between Route 9 and the Cedar Ln back lots)
            Building(rt9, 1, -300f, -240f, 70f, 501, "autorepair", 24f, 16f, 30f, MapSite.ParkingLot, "strip", "commercial", "Route 9 Auto Repair", "AUTO REPAIR");
            Building(rt9, 1, -236f, -170f, 70f, 503, "fastfood", 16f, 14f, 30f, MapSite.ParkingLot, "strip", "commercial", "Burger Barn", "BURGER BARN");
            Building(rt9, 1, -166f, ox - oR, 70f, 505, "stripmall", 54f, 18f, 34f, MapSite.ParkingLot, "strip", "commercial", "Oak Plaza", "OAK PLAZA", 1, 6);
            Building(rt9, 1, ox + oR, ox + oR + 52f, 70f, 507, "gasstation", 48f, 36f, 2f, MapSite.ParkingLot, "strip", "commercial", "Fill-N-Go", "FILL-N-GO");
            Building(rt9, 1, ox + oR + 56f, mx - oR, 74f, 509, "grocery", 60f, 34f, 34f, MapSite.ParkingLot, "strip", "commercial", "Harvest Foods", "HARVEST FOODS");
            Building(rt9, 1, mx + oR, mx + oR + 40f, 70f, 511, "bank", 18f, 16f, 30f, MapSite.ParkingLot, "strip", "commercial", "Prairie Credit Union", "CREDIT UNION", 1);
            Building(rt9, 1, mx + oR + 44f, 230f, 70f, 513, "fastfood", 16f, 14f, 30f, MapSite.ParkingLot, "strip", "commercial", "Taco Rancho", "TACO RANCHO");
            // south side
            Building(rt9, -1, -300f, -220f, 70f, 500, "motel", 60f, 14f, 22f, MapSite.ParkingLot, "strip", "commercial", "Starlite Motel", "STARLITE MOTEL", 2);
            Vacant(rt9, -1, -216f, -160f, 70f, 502, "strip", "commercial", FieldKind.Pasture);
            Building(rt9, -1, -156f, -100f, 70f, 504, "gasstation", 48f, 36f, 2f, MapSite.ParkingLot, "strip", "commercial", "Quik Stop", "QUIK STOP");
            Building(rt9, -1, -80f, -20f, 70f, 506, "metalshop", 30f, 24f, 26f, MapSite.Gravel, "strip", "commercial", "County Tractor Supply", "TRACTOR SUPPLY");
            Building(rt9, -1, -16f, 40f, 70f, 508, "office", 24f, 16f, 28f, MapSite.ParkingLot, "strip", "commercial", "Millbrook Medical Clinic", "MEDICAL CLINIC", 2);
            Vacant(rt9, -1, 44f, 100f, 70f, 510, "strip", "commercial", FieldKind.Hay);
            Building(rt9, -1, 104f, 170f, 70f, 512, "diner", 18f, 9f, 26f, MapSite.ParkingLot, "strip", "commercial", "Route 9 Diner", "ROUTE 9 DINER");
            Building(rt9, -1, 174f, 236f, 70f, 514, "autorepair", 22f, 14f, 30f, MapSite.ParkingLot, "strip", "commercial", "Speedy Lube", "SPEEDY LUBE");
            // Oak/Maple south blocks between Cedar back lots and Route 9: houses facing the side streets
            Houses(oakS, -1, -310f, -236f, 18f, 30f, 1, "strip", new[] { "ranch2", "cape" });
            Houses(mapleS, 1, -310f, -236f, 18f, 30f, 2, "strip", new[] { "cape", "ranch2", "twostory" }, 4);
            // buffer tree lines between the strip lots and the Cedar Ln back yards
            Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(-320f, -244f, 236f, -217f), Density = 0.6f, Seed = 31 });
        }

        // ------------------------------------------------------------------ farms + development land

        static void Farm(MapRoad r, int side, float a0, float a1, float depth, int num, string name, int seed)
        {
            var p = Building(r, side, a0, a1, depth, num, "farmhouse", 11f, 12f, 25f, MapSite.Farmyard, "farms", "farm", name, "", 2, 1, true, null, null, -(a1 - a0) / 2f + 14f);
            float w = a1 - a0;
            var lot = p.Lot;
            // outbuildings placed as extra parcels sharing the farm lot (not separate addresses)
            void Out(string type, float x, float depthBack, float bw, float bd, int units = 1)
            {
                var spec = new MapBuildingSpec { Type = type, Width = bw, Depth = bd, Units = units, Seed = seed + type.GetHashCode(), Color = type == "barn" ? new Color(0.62f, 0.14f, 0.10f) : Metals[MapModels.Pick(seed + bw.GetHashCode(), Metals.Length)], Accent = Color.white };
                Parcels.Add(new MapParcel { Lot = lot, Spec = spec, Setback = depthBack, OffsetX = x, Site = MapSite.None, Road = r });
            }
            Out("barn", -w / 2f + 40f, 40f, 14f, 24f);
            Out("silo", -w / 2f + 52f, 44f, 6f, 6f);
            if (seed % 2 == 0) Out("grainbins", -w / 2f + 68f, 34f, 26f, 10f, 3);
            Out("machineshed", -w / 2f + 26f, 72f, 20f, 15f);
            // fields fill the rest of the farm lot
            var R = lot.Rect;
            Vector3 c0 = lot.Local(-w / 2f + 90f, 0f), c1 = lot.Local(w / 2f, depth);
            var fr = Rect.MinMaxRect(Mathf.Min(c0.x, c1.x) + 4f, Mathf.Min(c0.z, c1.z) + 4f, Mathf.Max(c0.x, c1.x) - 4f, Mathf.Max(c0.z, c1.z) - 4f);
            if (fr.width > 20f && fr.height > 20f)
                Areas.Add(new MapArea { Kind = AreaKind.Field, R = fr, Field = (FieldKind)(seed % 4 == 3 ? 4 : seed % 3), RowsAlongX = !r.EW, Seed = seed });
            Vector3 b0 = lot.Local(-w / 2f, 90f), b1 = lot.Local(-w / 2f + 90f, depth);
            var br = Rect.MinMaxRect(Mathf.Min(b0.x, b1.x) + 4f, Mathf.Min(b0.z, b1.z) + 4f, Mathf.Max(b0.x, b1.x) - 4f, Mathf.Max(b0.z, b1.z) - 4f);
            if (br.width > 20f && br.height > 20f)
                Areas.Add(new MapArea { Kind = AreaKind.Field, R = br, Field = seed % 2 == 0 ? FieldKind.Pasture : FieldKind.Hay, RowsAlongX = r.EW, Seed = seed + 1 });
        }

        static void Fields(Rect r, int seed, float cell = 160f, float hedge = 8f)
        {
            // a patchwork of fields with hedgerow tree lines between them
            int nx = Mathf.Max(1, Mathf.RoundToInt(r.width / cell)), nz = Mathf.Max(1, Mathf.RoundToInt(r.height / cell));
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var f = Rect.MinMaxRect(r.xMin + r.width * i / nx + hedge / 2f, r.yMin + r.height * j / nz + hedge / 2f,
                                            r.xMin + r.width * (i + 1) / nx - hedge / 2f, r.yMin + r.height * (j + 1) / nz - hedge / 2f);
                    int s = seed + i * 7 + j * 13;
                    var kind = new[] { FieldKind.Corn, FieldKind.Soybeans, FieldKind.Wheat, FieldKind.Corn, FieldKind.Hay, FieldKind.Plowed, FieldKind.Soybeans, FieldKind.Pasture }[s % 8];
                    if (s % 11 == 5) Areas.Add(new MapArea { Kind = AreaKind.Woods, R = f, Density = 1.6f, Seed = s, Patch = true });
                    else Areas.Add(new MapArea { Kind = AreaKind.Field, R = f, Field = kind, RowsAlongX = s % 2 == 0, Seed = s, Patch = true });
                    // hedgerows on the north and east edges
                    if (j < nz - 1) Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(f.xMin, f.yMax + 0.5f, f.xMax, f.yMax + hedge - 0.5f), Density = 0.9f, Seed = s + 1, Patch = true });
                    if (i < nx - 1) Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(f.xMax + 0.5f, f.yMin, f.xMax + hedge - 0.5f, f.yMax), Density = 0.9f, Seed = s + 2, Patch = true });
                }
        }

        static void BuildFarms(MapRoad rt9, MapRoad cr9, MapRoad prairie, MapRoad oakS2, MapRoad mill, MapRoad oakN2)
        {
            Farm(rt9, -1, -600f, -430f, 180f, 2001, "Eckhart Farm", 2);
            Farm(rt9, -1, 260f, 410f, 200f, 2003, "Sandoval Dairy", 5);
            Farm(cr9, 1, -560f, -380f, 200f, 3001, "Whitlock Farm", 4);
            Farm(cr9, -1, -300f, -110f, 120f, 3002, "Brandt Orchard & Farm", 7);
            Farm(prairie, -1, -40f, 140f, 120f, 4001, "Haldane Farm", 8);
            Farm(oakS2, 1, -620f, -440f, 160f, 5002, "Okafor Farm", 3);
            Farm(mill, -1, 640f, 800f, 140f, 6001, "Lindqvist Farm", 6);
            // field patchworks between the farmsteads
            Fields(Rect.MinMaxRect(-750f, -310f, -640f, 590f), 101, 150f);                 // west of Prairie Rd
            Fields(Rect.MinMaxRect(-600f, 335f, -350f, 588f), 131, 130f);                  // north-west, south of County Rd 9
            Fields(Rect.MinMaxRect(-80f, 480f, 410f, 588f), 151, 120f);                    // north, south of County Rd 9
            Fields(Rect.MinMaxRect(-750f, -690f, -100f, -530f), 171, 160f);                // south-west
            Fields(Rect.MinMaxRect(-80f, -690f, 410f, -350f), 191, 160f);                  // south, between Oak Rd and Mill Rd
            Fields(Rect.MinMaxRect(-600f, 612f, 410f, 970f), 211, 170f);                   // north of County Rd 9
            Fields(Rect.MinMaxRect(-610f, -320f, -566f, 230f), 231, 90f, 6f);              // between Prairie Rd and the west side
            // woods: tree lines that close off views at the map edge
            Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(-760f, -700f, 1180f, -690f), Density = 2f, Seed = 41 });
            Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(-760f, 965f, 1180f, 980f), Density = 2f, Seed = 42 });
            Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(-760f, -690f, -752f, 965f), Density = 2f, Seed = 43 });
            Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(1170f, -690f, 1180f, 965f), Density = 2f, Seed = 44 });
            // hills beyond the edges
            foreach (var (x, z, h, s) in new[] { (-900f, 300f, 38f, 1), (-880f, -350f, 30f, 2), (0f, 1120f, 42f, 3), (650f, 1150f, 36f, 4), (1320f, 200f, 40f, 5), (1300f, -500f, 32f, 6), (200f, -860f, 34f, 7), (-500f, -870f, 28f, 8), (-400f, 1100f, 30f, 9) })
                Areas.Add(new MapArea { Kind = AreaKind.Hill, R = new Rect(x - 260f, z - 200f, 520f, 400f), Height = h, Seed = s });
        }

        static void BuildDevLand(MapRoad mill, MapRoad rt9, MapRoad cr9)
        {
            // Developer-stage land along Mill Rd (Town tracts, LotUse.Free, > 30,000 sq ft → "land for sale" in the Development app)
            Vacant(mill, 1, -40f, 140f, 150f, 7001, "devland", "land", FieldKind.Hay);
            Vacant(mill, 1, 160f, 360f, 150f, 7003, "devland", "land", FieldKind.Pasture);   // ends short of Ridge Rd's lots (z 372+)
            Vacant(rt9, 1, 680f, 860f, 150f, 7101, "devland", "land", FieldKind.Pasture);
            Vacant(rt9, -1, 680f, 900f, 160f, 7102, "devland", "land", FieldKind.Hay);
            // the Development feature's own tracts on County Rd 9 (x 430-1040, z 610-810): pasture with fence lines, no trees inside
            Areas.Add(new MapArea { Kind = AreaKind.Meadow, R = Rect.MinMaxRect(431f, 611f, 569f, 761f), Field = FieldKind.Pasture, LotId = "tract-a", Seed = 51 });
            Areas.Add(new MapArea { Kind = AreaKind.Meadow, R = Rect.MinMaxRect(601f, 611f, 779f, 761f), Field = FieldKind.Hay, LotId = "tract-b", Seed = 52 });
            Areas.Add(new MapArea { Kind = AreaKind.Meadow, R = Rect.MinMaxRect(801f, 611f, 1039f, 809f), Field = FieldKind.Pasture, LotId = "tract-c", Seed = 53 });
            // countryside around them
            Fields(Rect.MinMaxRect(600f, 410f, 1170f, 590f), 301, 150f);
            Fields(Rect.MinMaxRect(440f, -110f, 1170f, 390f), 331, 170f);
            Fields(Rect.MinMaxRect(820f, -320f, 1170f, -120f), 361, 160f);
            Fields(Rect.MinMaxRect(430f, -690f, 1170f, -345f), 381, 170f);
            Fields(Rect.MinMaxRect(430f, 820f, 1170f, 965f), 401, 150f);
            Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(1045f, 612f, 1168f, 815f), Density = 1.4f, Seed = 61 });
            Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(432f, 400f, 590f, 410f), Density = 1.0f, Seed = 62 });
            BuildRidgeRd();
        }

        /// <summary>Ridge Rd: the short residential street east of Mill Rd where Development's overflow house lots are
        /// (<see cref="FallbackLotProvider"/>, used only once every town lot is taken). The street is built and the woods, hedgerows and
        /// crop patches that overlapped those lots are cut back, so the lots are open mown ground fronting a real street.</summary>
        static void BuildRidgeRd()
        {
            var fb = new FallbackLotProvider();
            bool any = false; Rect box = default;
            foreach (var l in fb.FreeLots()) { box = any ? Rect.MinMaxRect(Mathf.Min(box.xMin, l.Rect.xMin), Mathf.Min(box.yMin, l.Rect.yMin), Mathf.Max(box.xMax, l.Rect.xMax), Mathf.Max(box.yMax, l.Rect.yMax)) : l.Rect; any = true; }
            if (!any) return;
            float z = FallbackLotProvider.RidgeZ;
            var ridge = EW("Ridge Rd", z, 420f, box.xMax + 3f, RoadClass.Residential);
            var clear = Rect.MinMaxRect(box.xMin - 2f, box.yMin - 3f, box.xMax + 4f, box.yMax + 3f);
            Carve(clear);
            // mown ground on both sides of the street (not tied to a lot: it stays when a lot is built on)
            float row = ridge.RowHalf;
            Areas.Add(new MapArea { Kind = AreaKind.Meadow, R = Rect.MinMaxRect(clear.xMin, z + row + 0.3f, clear.xMax, clear.yMax), Field = FieldKind.Pasture, Seed = 63 });
            Areas.Add(new MapArea { Kind = AreaKind.Meadow, R = Rect.MinMaxRect(clear.xMin, clear.yMin, clear.xMax, z - row - 0.3f), Field = FieldKind.Pasture, Seed = 64 });
        }

        /// <summary>Cuts a rect out of every woods / field / orchard area (the parts outside it are kept as up to four pieces).</summary>
        static void Carve(Rect cut)
        {
            for (int i = Areas.Count - 1; i >= 0; i--)
            {
                var a = Areas[i];
                if (a.Kind != AreaKind.Woods && a.Kind != AreaKind.Field && a.Kind != AreaKind.Orchard) continue;
                if (!a.R.Overlaps(cut)) continue;
                Areas.RemoveAt(i);
                var r = a.R;
                float y0 = Mathf.Max(r.yMin, cut.yMin), y1 = Mathf.Min(r.yMax, cut.yMax);
                foreach (var piece in new[]
                {
                    Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, cut.yMin), Rect.MinMaxRect(r.xMin, cut.yMax, r.xMax, r.yMax),
                    Rect.MinMaxRect(r.xMin, y0, cut.xMin, y1), Rect.MinMaxRect(cut.xMax, y0, r.xMax, y1),
                })
                {
                    if (piece.width < 3f || piece.height < 3f) continue;
                    Areas.Add(new MapArea { Kind = a.Kind, R = piece, Field = a.Field, RowsAlongX = a.RowsAlongX, Density = a.Density, Height = a.Height, LotId = a.LotId, Seed = a.Seed, Patch = a.Patch });
                }
            }
        }

        static void BuildCoreFill()
        {
            // the core town's own empty tracts and empty lots read as mown meadow until something is built (hidden once claimed)
            foreach (var l in Town.Lots)
            {
                if (l.External || l.Use != LotUse.Free) continue;
                var r = l.Rect;
                Areas.Add(new MapArea { Kind = AreaKind.Meadow, R = Rect.MinMaxRect(r.xMin + 0.5f, r.yMin + 0.5f, r.xMax - 0.5f, r.yMax - 0.5f), Field = l.AreaSqFt > 20000f ? FieldKind.Hay : FieldKind.Pasture, LotId = l.Id, Seed = l.Number });
            }
            // gaps around the build site and behind lots: tree lines
            Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(-226f, 148f, 226f, 156f), Density = 0.5f, Seed = 71 });
            Areas.Add(new MapArea { Kind = AreaKind.Woods, R = Rect.MinMaxRect(-55f, 97f, 55f, 145f), Density = 0.6f, Seed = 72 });
        }

        static void BuildCreek()
        {
            foreach (var (x, z) in new[] { (585f, 975f), (592f, 860f), (585f, 700f), (588f, 600f), (600f, 480f), (640f, 300f), (612f, 120f), (650f, -60f), (628f, -200f), (650f, -330f), (622f, -480f), (640f, -695f) })
                Creek.Add(new Vector3(x, 0f, z));
        }
    }
}
