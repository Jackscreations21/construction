using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>What a plan is for: drives the Plans app filters, the code rules the validator applies and the takeoff style.</summary>
    public enum PlanGroup { Residential, Outbuilding, Commercial, Industrial }
    /// <summary>How the exterior walls are built.</summary>
    public enum WallSystem { WoodStud, PostFrame, SteelFrame, Cmu }
    /// <summary>How the roof is framed. Trusses @ 24", rafters @ 16", post-frame trusses @ 8', PEMB rigid frames @ 25' bays, steel bar joists @ 5'.</summary>
    public enum RoofSystem { Trusses, Rafters, PostFrameTrusses, RigidFrame, BarJoist }
    public enum RoofCovering { Shingles, MetalPanels, StandingSeam, Tpo }

    /// <summary>What a room is, from its name (see <see cref="Blueprint.KindOf"/>).</summary>
    public enum RoomKind
    {
        Living, Kitchen, Dining, Bedroom, Bath, HalfBath, Laundry, Closet, Hall, Stair, Mech, Garage, Office, Flex, Void,
        Sales, Restroom, Storage, Corridor, Lobby, CommKitchen, Cooler, Janitor, BreakRoom, Warehouse, Shop, Barn, Stall, Loft, Shed
    }

    /// <summary>A flight of stairs on a plan: <see cref="Ft"/> is the stair's footprint on floor <see cref="Floor"/> (the same rect on the floor
    /// above is the stairwell opening). Split = split-entry (half flight up + half flight down side by side from a landing).</summary>
    public class PlanStair
    {
        public string Name; public int Floor; public Rect Ft; public float RiseIn; public bool Split, Reverse;
        public int Risers, Treads; public float RiserIn, TreadIn, RunFt, WidthFt;
        public bool AlongY => Ft.height >= Ft.width;
    }

    /// <summary>One MEP item on a plan (plan feet). Trade: Plumbing / Electrical / HVAC / Fire. Outside items (condensers, meters,
    /// grease interceptors) sit outside the footprint.</summary>
    public class PlanFixture
    {
        public string Kind, Trade, Note, Room; public int Floor; public Vector2 Ft;
        public string Label => Kind + (string.IsNullOrEmpty(Note) ? "" : " " + Note);
        public static readonly HashSet<string> Outside = new HashSet<string> { "AC", "HP", "MTR", "GI", "HB" };
    }

    /// <summary>A house plan extended for any building: outbuildings, shops, stores, restaurants, offices, warehouses.
    /// Same coordinates and room tiling rules as <see cref="HousePlan"/> (see Docs/PLANS.md + Docs/BLUEPRINTS.md); adds the structural
    /// system, explicit doors (overhead doors, storefronts, second exits), open-plan walls, stairs, MEP fixtures and a system-aware takeoff.</summary>
    public partial class Blueprint : HousePlan
    {
        public string Occupancy = "IRC R-3 (one-family dwelling)";   // code occupancy shown on the plan
        public string ConstructionType = "V-B (wood frame)";
        public int Units = 1;                                       // dwelling units / tenant spaces
        public WallSystem WallFrame = WallSystem.WoodStud;              // exterior wall system
        public RoofSystem RoofFrame = RoofSystem.Trusses;
        public RoofCovering Covering = RoofCovering.Shingles;
        public bool RidgeAlongY;                                    // gable ends face the street (garages, barns, shops)
        public string FloorJoist = "2x10";                          // 2x10, 2x12, I-joist 11-7/8, I-joist 14, Open-web 16
        public int BearingLines = 1;                                // girders/bearing walls dividing the floor joist span
        public string RafterSize = "2x10";                          // when RoofFrame == Rafters (@ 16" O.C.)
        public int InteriorColumnLines;                             // bar-joist / rigid-frame interior column lines across the span
        public float SlabIn = 4f;                                   // slab thickness (garages 4", shops 5", warehouses 6")
        public float MezzanineFt;                                   // > 0: floor 1 is a loft/mezzanine at this height inside WallHeightFt walls
        public bool Finished = true;                                // false: unfinished shell (no drywall/flooring), e.g. garages, barns
        public bool Conditioned = true;                             // heated/cooled
        public string Notes = "";
        public readonly Dictionary<string, float> LevelOffsetFt = new Dictionary<string, float>();   // split-level rooms below/above the main level
        public readonly List<PlanStair> Stairs = new List<PlanStair>();
        public readonly List<PlanFixture> Fixtures = new List<PlanFixture>();
        readonly List<(int floor, int side, float pos, string product)> _explicit = new List<(int, int, float, string)>();
        readonly List<(int floor, float x, float y, string product)> _explicitInt = new List<(int, float, float, string)>();
        readonly List<(string kind, int floor, float x, float y, string note)> _extraFixtures = new List<(string, int, float, float, string)>();

        public bool IsResidential => Group == PlanGroup.Residential;
        public bool UsesIrc => Group == PlanGroup.Residential || Group == PlanGroup.Outbuilding;

        // ------------------------------------------------------------------ authoring helpers (chainable)

        /// <summary>Adds a room (plan feet, x0..x1 across, y0..y1 back from the front wall).</summary>
        public Blueprint R(string name, float x0, float y0, float x1, float y1, int floor = 0) { Room(name, x0, y0, x1, y1, floor); return this; }
        /// <summary>Exterior door/overhead door: side 0 front, 1 right, 2 back, 3 left; pos = plan x (front/back) or plan y (left/right) of its center.</summary>
        public Blueprint Door(int side, float pos, string product, int floor = 0) { _explicit.Add((floor, side, pos, product)); return this; }
        /// <summary>Extra window at an exact spot (same side/pos convention as <see cref="Door"/>).</summary>
        public Blueprint Window(int side, float pos, string product, int floor = 0) { _explicit.Add((floor, side, pos, product)); return this; }
        /// <summary>Interior door centered at plan point (x, y), which must lie on an interior wall.</summary>
        public Blueprint DoorAt(float x, float y, string product, int floor = 0) { _explicitInt.Add((floor, x, y, product)); return this; }
        /// <summary>Extra MEP fixture beyond the room rules.</summary>
        public Blueprint Fixture(string kind, float x, float y, string note = null, int floor = 0) { _extraFixtures.Add((kind, floor, x, y, note)); return this; }
        public Blueprint Level(string room, float offsetFt) { LevelOffsetFt[room] = offsetFt; return this; }

        // ------------------------------------------------------------------ derived numbers

        /// <summary>Living / gross area: residential counts conditioned rooms only (no garage, voids); others count every floor area.</summary>
        public override float SqFt
        {
            get
            {
                float s = 0f;
                foreach (var r in Rooms)
                {
                    var k = KindOf(r.Name);
                    if (k == RoomKind.Void) continue;
                    if (IsResidential && k == RoomKind.Garage) continue;
                    // stairwells count on every floor they serve (ANSI Z765 measures each level to the outside of its walls)
                    s += r.Ft.width * r.Ft.height;
                }
                return s;
            }
        }

        public float GarageSqFt { get { float s = 0; foreach (var r in Rooms) if (KindOf(r.Name) == RoomKind.Garage) s += r.Ft.width * r.Ft.height; return s; } }
        public int GarageBays { get { int n = 0; foreach (var o in Openings) if (o.IsOverhead && Rooms.Exists(r => r.Floor == o.Floor && KindOf(r.Name) == RoomKind.Garage)) n += o.WidthFt >= 15f ? 2 : 1; return n; } }

        public override string Summary
        {
            get
            {
                string size = $"{WidthFt}'x{DepthFt}'";
                string roof = Roof == RoofType.Flat ? "flat roof" : $"{Roof} {Pitch}/12";
                if (IsResidential)
                    return $"{SqFt:N0} sq ft · {Beds} bed / {BathsLabel} bath" + (Units > 1 ? $" · {Units} units" : "") + $" · {Floors} {(Floors > 1 ? "stories" : "story")} · {size}"
                           + (GarageSqFt > 0 ? $" · {GarageBays}-car garage" : "") + $" · {Foundation} · {roof}";
                return $"{SqFt:N0} sq ft · {Category} · {size} · {WallHeightFt}' {(WallFrame == WallSystem.WoodStud ? "walls" : "eave")} · {Foundation} · {roof} · {Occupancy}";
            }
        }

        /// <summary>Clear span of the floor joists: front to back divided by the bearing lines; lofts/mezzanines span their narrow side.</summary>
        public float FloorSpanFt
        {
            get
            {
                if (MezzanineFt <= 0f) return DepthFt / (BearingLines + 1);
                float m = 0f;
                foreach (var r in Rooms) { var k = KindOf(r.Name); if (r.Floor == 1 && k != RoomKind.Void && k != RoomKind.Stair) m = Mathf.Max(m, Mathf.Min(r.Ft.width, r.Ft.height)); }
                return m / (BearingLines + 1);
            }
        }

        public float JoistDepthIn => FloorJoist.StartsWith("2x12") ? 11.25f : FloorJoist.StartsWith("2x8") ? 7.25f : FloorJoist.Contains("11-7/8") ? 11.875f
                                   : FloorJoist.Contains("14") ? 14f : FloorJoist.Contains("16") ? 16f : 9.25f;
        /// <summary>Finished floor to finished floor (in) between floor f and f+1: wall height + plates + floor joists + subfloor, or the mezzanine height.</summary>
        public float FloorToFloorIn(int f) => MezzanineFt > 0f ? MezzanineFt * 12f : WallHeightFt * 12f + 1.125f + JoistDepthIn + 0.75f;

        // ------------------------------------------------------------------ room kinds

        static bool Has(string n, string s) => n.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        static bool Starts(string n, string s) => n.StartsWith(s, StringComparison.OrdinalIgnoreCase);

        /// <summary>Room kind from its name. Order matters ("Master Bath" is a bath, "Master" a bedroom).</summary>
        public static RoomKind KindOf(string n)
        {
            if (Has(n, "open to below") || Starts(n, "Attic") || Has(n, "vault")) return RoomKind.Void;
            if (Has(n, "Restroom")) return RoomKind.Restroom;
            if (Has(n, "Powder") || Has(n, "Half bath")) return RoomKind.HalfBath;
            if (Has(n, "Bath")) return RoomKind.Bath;
            if (Has(n, "Walk-in cooler") || Has(n, "Cooler")) return RoomKind.Cooler;
            if (Has(n, "Closet") || Starts(n, "WIC") || Has(n, "Linen") || Has(n, "Pantry")) return RoomKind.Closet;
            if (Starts(n, "Master") || Starts(n, "Bed") || Has(n, "Bunk")) return RoomKind.Bedroom;
            if (Has(n, "Laundry") || Starts(n, "Mud")) return RoomKind.Laundry;
            if (Starts(n, "Cook line") || Starts(n, "Prep") || Starts(n, "Dish")) return RoomKind.CommKitchen;
            if (Has(n, "Kitchen")) return RoomKind.Kitchen;
            if (Has(n, "Living") || Starts(n, "Great") || Has(n, "Family") || Starts(n, "Den") || Has(n, "Rec room")) return RoomKind.Living;
            if (Has(n, "Dining") || Has(n, "Nook") || Has(n, "Breakfast") || Has(n, "Seating")) return RoomKind.Dining;
            if (Starts(n, "Stair")) return RoomKind.Stair;
            if (Has(n, "Corridor") || Has(n, "Vestibule")) return RoomKind.Corridor;
            if (Starts(n, "Hall") || Has(n, "Foyer") || Starts(n, "Entry") || Starts(n, "Landing") || Has(n, "Gallery")) return RoomKind.Hall;
            if (Starts(n, "Mech") || Has(n, "Utility") || Starts(n, "Electrical") || Has(n, "Riser") || Starts(n, "Server")) return RoomKind.Mech;
            if (Has(n, "Garage")) return RoomKind.Garage;
            if (Has(n, "Janitor")) return RoomKind.Janitor;
            if (Starts(n, "Break")) return RoomKind.BreakRoom;
            if (Has(n, "Lobby") || Has(n, "Reception") || Has(n, "Waiting")) return RoomKind.Lobby;
            if (Has(n, "Sales") || Has(n, "Showroom") || Has(n, "Retail") || Has(n, "Open office") || Has(n, "Store floor") || Has(n, "Counter")) return RoomKind.Sales;
            if (Has(n, "Office") || Has(n, "Study") || Has(n, "Conference") || Has(n, "Manager")) return RoomKind.Office;
            if (Has(n, "Warehouse") || Has(n, "Bay") || Has(n, "Dock") || Has(n, "Shell")) return RoomKind.Warehouse;
            if (Has(n, "Shop")) return RoomKind.Shop;
            if (Has(n, "Stall")) return RoomKind.Stall;
            if (Has(n, "Aisle") || Has(n, "Barn") || Has(n, "Equipment") || Has(n, "Lean")) return RoomKind.Barn;
            if (Has(n, "loft")) return RoomKind.Loft;
            if (Has(n, "Shed")) return RoomKind.Shed;
            if (Has(n, "Storage") || Has(n, "Stock") || Has(n, "Back room") || Has(n, "Tack") || Has(n, "Feed")) return RoomKind.Storage;
            return RoomKind.Flex;
        }

        /// <summary>Rooms that flow into each other with no wall between them (open plan).</summary>
        public static bool IsOpen(RoomKind k) => k == RoomKind.Living || k == RoomKind.Kitchen || k == RoomKind.Dining || k == RoomKind.Hall || k == RoomKind.Stair
                                                || k == RoomKind.Sales || k == RoomKind.Lobby || k == RoomKind.Corridor;
        public static bool OpenPair(RoomKind a, RoomKind b)
        {
            if (a == b && (a == RoomKind.Warehouse || a == RoomKind.Shop || a == RoomKind.Barn || a == RoomKind.Garage || a == RoomKind.Loft)) return true;
            if ((a == RoomKind.Stair && (b == RoomKind.Barn || b == RoomKind.Loft || b == RoomKind.Warehouse)) || (b == RoomKind.Stair && (a == RoomKind.Barn || a == RoomKind.Loft || a == RoomKind.Warehouse))) return true;
            if (!IsOpen(a) || !IsOpen(b)) return false;
            bool resA = a <= RoomKind.Stair, resB = b <= RoomKind.Stair;
            if (a == RoomKind.Stair || b == RoomKind.Stair) return (a == RoomKind.Stair ? b : a) == RoomKind.Hall || (a == RoomKind.Stair ? b : a) == RoomKind.Living
                                                               || (a == RoomKind.Stair ? b : a) == RoomKind.Corridor || (a == RoomKind.Stair ? b : a) == RoomKind.Lobby || a == b;
            if (a == RoomKind.Hall || b == RoomKind.Hall) return true;
            return resA == resB;
        }
        public static bool Habitable(RoomKind k) => k == RoomKind.Living || k == RoomKind.Kitchen || k == RoomKind.Dining || k == RoomKind.Bedroom || k == RoomKind.Flex || k == RoomKind.Office;
        public static bool NeedsDoor(RoomKind k) => !IsOpen(k) && k != RoomKind.Void && k != RoomKind.Loft;

        /// <summary>Dwelling unit / tenant space a room belongs to (units sit side by side along x).</summary>
        public int UnitOf(PlanRoom r) => Units <= 1 ? 0 : Mathf.Clamp(Mathf.FloorToInt(r.Ft.center.x / (WidthFt / Units)), 0, Units - 1);
        /// <summary>Open-plan connection inside one unit (no wall between them).</summary>
        public bool Connected(PlanRoom a, PlanRoom b) => UnitOf(a) == UnitOf(b) && OpenPair(KindOf(a.Name), KindOf(b.Name));
        /// <summary>Stairs (by room name) that climb toward plan y = 0 / x = 0 instead of away from it.</summary>
        public readonly HashSet<string> ReversedStairs = new HashSet<string>();
        public Blueprint StairDown(string name) { ReversedStairs.Add(name); return this; }

        public PlanRoom RoomAt(int floor, Vector2 p)
        {
            foreach (var r in Rooms) if (r.Floor == floor && p.x > r.Ft.xMin && p.x < r.Ft.xMax && p.y > r.Ft.yMin && p.y < r.Ft.yMax) return r;
            return null;
        }

        // ------------------------------------------------------------------ derivation

        public override HousePlan Finish()
        {
            base.Finish();          // exterior walls + merged interior wall lines
            RebuildInteriorWalls(); // split at room changes, drop open-plan segments
            Openings.Clear();
            DeriveStairs();
            // explicit exterior doors / windows first (they win over rule windows)
            foreach (var e in _explicit) AddExterior(e.floor, e.side, e.pos, e.product);
            if (IsResidential && FrontDoorFt > 0f) AddExterior(0, 0, FrontDoorFt, "door-ext-fg36");
            if (IsResidential && Units == 1) RearPatioDoor();
            foreach (var r in Rooms) RuleWindows(r);
            foreach (var e in _explicitInt) AddInteriorAt(e.floor, new Vector2(e.x, e.y), e.product);
            foreach (var r in Rooms) RuleDoor(r);
            BuildFixtures();
            return this;
        }

        void RebuildInteriorWalls()
        {
            var ext = Walls.FindAll(w => w.Exterior);
            Walls.Clear();
            for (int f = 0; f < Floors; f++)
            {
                foreach (var w in ext) if (w.Floor == f) Walls.Add(w);
                var lines = new Dictionary<(bool h, float c), List<(float a, float b)>>();
                foreach (var r in Rooms)
                {
                    if (r.Floor != f) continue;
                    void Edge(bool h, float c, float a, float b)
                    {
                        if (Mathf.Abs(c) < 0.01f || Mathf.Abs(c - (h ? DepthFt : WidthFt)) < 0.01f) return;
                        var key = (h, Mathf.Round(c * 100f) / 100f);
                        if (!lines.TryGetValue(key, out var l)) lines[key] = l = new List<(float, float)>();
                        l.Add((a, b));
                    }
                    Edge(true, r.Ft.yMin, r.Ft.xMin, r.Ft.xMax); Edge(true, r.Ft.yMax, r.Ft.xMin, r.Ft.xMax);
                    Edge(false, r.Ft.xMin, r.Ft.yMin, r.Ft.yMax); Edge(false, r.Ft.xMax, r.Ft.yMin, r.Ft.yMax);
                }
                var keys = new List<(bool h, float c)>(lines.Keys);
                keys.Sort((p, q) => p.h != q.h ? p.h.CompareTo(q.h) : p.c.CompareTo(q.c));
                foreach (var key in keys)
                {
                    var segs = lines[key];
                    var pts = new List<float>();
                    foreach (var s in segs) { pts.Add(s.a); pts.Add(s.b); }
                    pts.Sort();
                    float? runA = null; float runB = 0;
                    void Emit()
                    {
                        if (runA == null) return;
                        Walls.Add(key.h ? new PlanWall { Floor = f, A = new Vector2(runA.Value, key.c), B = new Vector2(runB, key.c) }
                                        : new PlanWall { Floor = f, A = new Vector2(key.c, runA.Value), B = new Vector2(key.c, runB) });
                        runA = null;
                    }
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        float p = pts[i], q = pts[i + 1];
                        if (q - p < 0.01f) continue;
                        float mid = (p + q) / 2f;
                        bool covered = segs.Exists(s => s.a <= p + 0.01f && s.b >= q - 0.01f);
                        var ra = RoomAt(f, key.h ? new Vector2(mid, key.c - 0.1f) : new Vector2(key.c - 0.1f, mid));
                        var rb = RoomAt(f, key.h ? new Vector2(mid, key.c + 0.1f) : new Vector2(key.c + 0.1f, mid));
                        bool keep = covered && (ra == null || rb == null || !Connected(ra, rb));
                        if (keep) { if (runA == null) runA = p; else if (p - runB > 0.01f) { Emit(); runA = p; } runB = q; }
                        else Emit();
                    }
                    Emit();
                }
            }
        }

        void DeriveStairs()
        {
            Stairs.Clear();
            foreach (var r in Rooms)
            {
                if (KindOf(r.Name) != RoomKind.Stair) continue;
                bool above = Rooms.Exists(o => o.Floor == r.Floor + 1 && o.Ft == r.Ft && KindOf(o.Name) == RoomKind.Stair);
                bool below = Rooms.Exists(o => o.Floor == r.Floor - 1 && o.Ft == r.Ft && KindOf(o.Name) == RoomKind.Stair);
                if (below) continue;   // stairwell opening of the flight below
                bool split = Has(r.Name, "split");
                bool half = !above && (Has(r.Name, "half") || Has(r.Name, "down") || Has(r.Name, " up"));
                if (!above && !half) continue;
                float rise = half ? FloorToFloorIn(r.Floor) / 2f : FloorToFloorIn(r.Floor);
                if (half && LevelOffsetFt.Count > 0) { float m = 0; foreach (var v in LevelOffsetFt.Values) m = Mathf.Max(m, Mathf.Abs(v)); if (m > 0) rise = m * 12f; }
                var s = new PlanStair { Name = r.Name, Floor = r.Floor, Ft = r.Ft, RiseIn = rise, Split = split, Reverse = ReversedStairs.Contains(r.Name) };
                float maxRiser = UsesIrc ? 7.75f : 7f, minTread = UsesIrc ? 10f : 11f;
                float flightRise = split ? rise / 2f : rise;
                s.Risers = Mathf.CeilToInt(flightRise / maxRiser - 0.001f); s.RiserIn = flightRise / s.Risers;
                s.Treads = s.Risers - 1; s.TreadIn = minTread; s.RunFt = s.Treads * minTread / 12f;
                s.WidthFt = split ? Mathf.Min(r.Ft.width, r.Ft.height) / 2f : Mathf.Min(r.Ft.width, r.Ft.height);
                Stairs.Add(s);
            }
        }

        // ------------------------------------------------------------------ openings

        int ExtWallIndex(int floor, int side) => Walls.FindIndex(w => w.Floor == floor && w.Exterior) + side;
        float AlongFromPos(int side, float pos) => side == 0 ? pos : side == 1 ? pos : side == 2 ? WidthFt - pos : DepthFt - pos;

        bool AddExterior(int floor, int side, float pos, string product) => TryAdd(floor, ExtWallIndex(floor, side), AlongFromPos(side, pos), product, false);

        bool TryAdd(int floor, int wi, float along, string product, bool clampToWall = true)
        {
            if (wi < 0 || wi >= Walls.Count) return false;
            var o = new PlanOpening { Floor = floor, Wall = wi, CenterFt = along, ProductId = product };
            if (o.Product == null) { Debug.LogWarning($"[Blueprints] {Id}: unknown product {product}"); return false; }
            float half = o.RoughWidthIn / 24f;
            float len = Walls[wi].LengthFt;
            if (clampToWall) o.CenterFt = Mathf.Clamp(along, half + 0.25f, len - half - 0.25f);
            if (o.CenterFt - half < -0.01f || o.CenterFt + half > len + 0.01f) return false;
            foreach (var x in Openings)
                if (x.Wall == wi && Mathf.Abs(x.CenterFt - o.CenterFt) < (x.RoughWidthIn + o.RoughWidthIn) / 24f + 0.5f) return false;
            Openings.Add(o);
            return true;
        }

        /// <summary>A sliding patio door on the back wall of the dining room (or family/great/living room).</summary>
        void RearPatioDoor()
        {
            foreach (var kind in new[] { RoomKind.Dining, RoomKind.Living, RoomKind.Kitchen })
                foreach (var r in Rooms)
                    if (r.Floor == 0 && KindOf(r.Name) == kind && r.Ft.yMax > DepthFt - 0.01f && r.Ft.width >= 9f && !LevelOffsetFt.ContainsKey(r.Name))
                    { if (AddExterior(0, 2, r.Ft.center.x, "door-slider72")) return; }
        }

        /// <summary>Room edges on the footprint: (side, a, b) in plan coordinates of that side (x for front/back, y for left/right).</summary>
        public IEnumerable<(int side, float a, float b)> ExteriorEdges(PlanRoom r)
        {
            if (r.Ft.yMin < 0.01f) yield return (0, r.Ft.xMin, r.Ft.xMax);
            if (r.Ft.xMax > WidthFt - 0.01f) yield return (1, r.Ft.yMin, r.Ft.yMax);
            if (r.Ft.yMax > DepthFt - 0.01f) yield return (2, r.Ft.xMin, r.Ft.xMax);
            if (r.Ft.xMin < 0.01f) yield return (3, r.Ft.yMin, r.Ft.yMax);
        }

        bool WallHasOverhead(int floor, int side)
        {
            int wi = ExtWallIndex(floor, side);
            return Openings.Exists(o => o.Wall == wi && o.IsOverhead);
        }

        void RuleWindows(PlanRoom r)
        {
            var k = KindOf(r.Name);
            string win = null; float spacing = 0f; int max = 1;
            bool res = IsResidential;
            switch (k)
            {
                case RoomKind.Living: win = "win-sh-3660"; spacing = 8f; max = 3; break;
                case RoomKind.Dining: win = Group == PlanGroup.Commercial ? "win-sh-3660" : "win-sh-3660"; spacing = 8f; max = Group == PlanGroup.Commercial ? 6 : 2; break;
                case RoomKind.Kitchen: win = "win-slider-4836"; spacing = 10f; break;
                case RoomKind.Bedroom: win = "win-sh-3660"; spacing = 9f; max = 2; break;   // egress: 5.7 sq ft net, sill ≤ 44"
                case RoomKind.Bath: case RoomKind.HalfBath: case RoomKind.Laundry: win = "win-sh-2436"; spacing = 6f; break;
                case RoomKind.Flex: case RoomKind.Office: case RoomKind.BreakRoom: win = res ? "win-sh-3648" : "win-sh-3660"; spacing = 9f; max = res ? 2 : 4; break;
                case RoomKind.Lobby: win = "win-storefront-7284"; spacing = 9f; max = 3; break;
                case RoomKind.Sales: win = "win-storefront-7284"; spacing = 8f; max = 8; break;
                case RoomKind.Garage: case RoomKind.Shop: win = "win-sh-3636"; spacing = 14f; max = 2; break;
                case RoomKind.Barn: win = "win-sh-3636"; spacing = 16f; max = 3; break;
                case RoomKind.Stall: win = "win-sh-2436"; spacing = 8f; break;
                case RoomKind.Shed: win = "win-sh-2436"; spacing = 8f; break;
                case RoomKind.Loft: win = "win-sh-3636"; spacing = 12f; break;
            }
            if (win == null) return;
            foreach (var (side, a, b) in ExteriorEdges(r))
            {
                float len = b - a; if (len < 6f) continue;
                if ((k == RoomKind.Garage || k == RoomKind.Shop || k == RoomKind.Barn) && WallHasOverhead(r.Floor, side)) continue;
                if ((k == RoomKind.Sales || k == RoomKind.Lobby) && side == 2) continue;
                string w = win;
                if ((k == RoomKind.Sales || k == RoomKind.Lobby) && side != 0) w = "win-sh-3660";
                if (k == RoomKind.Dining && Group == PlanGroup.Commercial && side == 0) w = "win-storefront-7284";
                if (k == RoomKind.Living && res && side == 0 && len >= 14f && r.Floor == 0) { AddExterior(r.Floor, side, (a + b) / 2f, "win-picture-6048"); continue; }
                int n = Mathf.Clamp(Mathf.FloorToInt(len / spacing), 1, max);
                if ((k == RoomKind.Sales || k == RoomKind.Lobby) && side != 0) n = Mathf.Min(n, 2);
                for (int i = 0; i < n; i++) AddExterior(r.Floor, side, a + len * (i + 0.5f) / n, w);
            }
        }

        // interior doors: the best shared edge of each room that needs one
        void RuleDoor(PlanRoom r)
        {
            var k = KindOf(r.Name);
            if (!NeedsDoor(k)) return;
            if (HasGoodDoor(r)) return;
            bool res = IsResidential || Group == PlanGroup.Outbuilding;
            string product = !res ? "door-int-sc36" : k == RoomKind.HalfBath || k == RoomKind.Closet ? "door-int-6p24" : k == RoomKind.Garage ? "door-fire20-36"
                           : k == RoomKind.Stall ? "door-stall48" : k == RoomKind.Mech && Group == PlanGroup.Outbuilding ? "door-int-sc36" : "door-int-6p30";
            if (k == RoomKind.Closet && Mathf.Min(r.Ft.width, r.Ft.height) < 3.5f && Mathf.Max(r.Ft.width, r.Ft.height) >= 5f) product = "door-bifold36";
            if (k == RoomKind.Cooler) product = "door-cooler36";
            if (k == RoomKind.Warehouse && !IsResidential) product = "door-hm36";
            var cands = new List<(float score, PlanRoom other, bool horiz, float c, float a, float b)>();
            foreach (var o in Rooms)
            {
                if (o == r || o.Floor != r.Floor || UnitOf(o) != UnitOf(r)) continue;
                var ok = KindOf(o.Name);
                if (ok == RoomKind.Void) continue;
                float score = DoorScore(k, r, ok, o);
                if (score <= 0) continue;
                // shared edge
                if (Mathf.Abs(r.Ft.yMax - o.Ft.yMin) < 0.01f || Mathf.Abs(r.Ft.yMin - o.Ft.yMax) < 0.01f)
                {
                    float c = Mathf.Abs(r.Ft.yMax - o.Ft.yMin) < 0.01f ? r.Ft.yMax : r.Ft.yMin;
                    float a = Mathf.Max(r.Ft.xMin, o.Ft.xMin), b = Mathf.Min(r.Ft.xMax, o.Ft.xMax);
                    if (b - a > 0.5f) cands.Add((score + (b - a), o, true, c, a, b));
                }
                if (Mathf.Abs(r.Ft.xMax - o.Ft.xMin) < 0.01f || Mathf.Abs(r.Ft.xMin - o.Ft.xMax) < 0.01f)
                {
                    float c = Mathf.Abs(r.Ft.xMax - o.Ft.xMin) < 0.01f ? r.Ft.xMax : r.Ft.xMin;
                    float a = Mathf.Max(r.Ft.yMin, o.Ft.yMin), b = Mathf.Min(r.Ft.yMax, o.Ft.yMax);
                    if (b - a > 0.5f) cands.Add((score + (b - a), o, false, c, a, b));
                }
            }
            cands.Sort((p, q) => q.score.CompareTo(p.score));
            foreach (var cd in cands)
            {
                var def = Catalog.Get(product); float w = def != null ? def.Size.x / Groundbreak.Units.Foot + 2f / 12f : 3f;
                if (cd.b - cd.a < w + 0.5f) continue;
                // bedrooms/offices: door near the corner (furniture wall stays free); others centered
                var tries = new List<float> { (cd.a + cd.b) / 2f };
                if (k == RoomKind.Bedroom || k == RoomKind.Office || k == RoomKind.Garage) { tries.Insert(0, cd.a + w / 2f + 0.5f); tries.Add(cd.b - w / 2f - 0.5f); }
                else { tries.Add(cd.a + w / 2f + 0.5f); tries.Add(cd.b - w / 2f - 0.5f); }
                foreach (var t in tries)
                    if (AddInteriorAt(r.Floor, cd.horiz ? new Vector2(t, cd.c) : new Vector2(cd.c, t), product)) return;
            }
        }

        static float DoorScore(RoomKind k, PlanRoom r, RoomKind ok, PlanRoom o)
        {
            bool targetPublic = IsOpen(ok) && ok != RoomKind.Stair;
            switch (k)
            {
                case RoomKind.Closet:
                    if (ok == RoomKind.Bedroom) return Has(r.Name, "Master") == Has(o.Name, "Master") ? 3200 : 2800; if (ok == RoomKind.Bath && Has(r.Name, "Master") && Has(o.Name, "Master")) return 3500;
                    if (ok == RoomKind.Hall) return 2000; if (ok == RoomKind.Kitchen && Has(r.Name, "Pantry")) return 3000; if (ok == RoomKind.Laundry || ok == RoomKind.Garage) return 1200;
                    return targetPublic ? 1000 : 0;
                case RoomKind.Bath:
                    if (Has(r.Name, "Master") && Has(o.Name, "Master") && ok == RoomKind.Bedroom) return 3500;
                    if (ok == RoomKind.Hall) return 2500; if (targetPublic) return 1500; if (ok == RoomKind.Bedroom) return 400; if (ok == RoomKind.Laundry) return 300; return 0;
                case RoomKind.HalfBath:
                    if (ok == RoomKind.Hall) return 2500; if (targetPublic) return 1500; if (ok == RoomKind.Laundry || ok == RoomKind.Office || ok == RoomKind.Shop || ok == RoomKind.Garage) return 800; return 0;
                case RoomKind.Bedroom:
                    if (ok == RoomKind.Hall) return 2500; if (targetPublic) return 1500; if (ok == RoomKind.Flex) return 600; return 0;
                case RoomKind.Laundry:
                    if (ok == RoomKind.Hall) return 2200; if (ok == RoomKind.Kitchen) return 2000; if (targetPublic) return 1600; if (ok == RoomKind.Garage) return 900; return 0;
                case RoomKind.Garage:
                    if (ok == RoomKind.Laundry) return 3000; if (ok == RoomKind.Hall) return 2500; if (ok == RoomKind.Kitchen) return 2000; if (targetPublic) return 1500;
                    if (ok == RoomKind.Shop || ok == RoomKind.Storage || ok == RoomKind.Garage) return 800; return 0;
                case RoomKind.Mech:
                    if (ok == RoomKind.Hall || ok == RoomKind.Corridor) return 2500; if (ok == RoomKind.Garage || ok == RoomKind.Warehouse || ok == RoomKind.Shop || ok == RoomKind.Barn) return 2200;
                    if (targetPublic) return 1500; if (ok == RoomKind.Laundry || ok == RoomKind.Storage) return 1000; if (ok == RoomKind.Restroom) return 0; return 200;
                case RoomKind.Restroom:
                    if (ok == RoomKind.Corridor) return 3000; if (ok == RoomKind.Hall) return 2600; if (ok == RoomKind.Lobby || ok == RoomKind.Sales || ok == RoomKind.Dining) return 2000;
                    if (ok == RoomKind.Warehouse || ok == RoomKind.Shop || ok == RoomKind.BreakRoom || ok == RoomKind.Office) return 1000; return 0;
                case RoomKind.Cooler: return ok == RoomKind.CommKitchen ? 3000 : ok == RoomKind.Storage || ok == RoomKind.Corridor || ok == RoomKind.Sales ? 1500 : 0;
                case RoomKind.CommKitchen:
                    if (ok == RoomKind.Dining) return 3000; if (ok == RoomKind.Corridor || ok == RoomKind.Sales) return 2500; if (ok == RoomKind.CommKitchen) return 2000; return 0;
                case RoomKind.Janitor:
                    if (ok == RoomKind.Corridor || ok == RoomKind.Hall) return 3000; if (ok == RoomKind.CommKitchen || ok == RoomKind.Storage || ok == RoomKind.Warehouse || ok == RoomKind.BreakRoom) return 2000; if (targetPublic) return 1500; return 0;
                case RoomKind.Office: case RoomKind.BreakRoom:
                    if (ok == RoomKind.Corridor || ok == RoomKind.Hall) return 2500; if (targetPublic) return 2000; if (ok == RoomKind.Warehouse || ok == RoomKind.Shop) return 1500; if (ok == RoomKind.BreakRoom || ok == RoomKind.Office) return 300; return 0;
                case RoomKind.Storage:
                    if (ok == RoomKind.Sales || ok == RoomKind.Corridor || ok == RoomKind.CommKitchen || ok == RoomKind.Warehouse || ok == RoomKind.Shop || ok == RoomKind.Barn) return 2200;
                    if (targetPublic || ok == RoomKind.Garage) return 1500; if (ok == RoomKind.BreakRoom || ok == RoomKind.Office) return 500; return 0;
                case RoomKind.Warehouse: case RoomKind.Shop:
                    if (ok == RoomKind.Corridor || ok == RoomKind.Hall || ok == RoomKind.Lobby) return 2500; if (ok == RoomKind.Office || ok == RoomKind.BreakRoom || ok == RoomKind.Sales) return 2000;
                    if (ok == RoomKind.Garage || ok == RoomKind.Storage) return 1000; return 0;
                case RoomKind.Stall: return ok == RoomKind.Barn ? 3000 : 0;
                case RoomKind.Flex:
                    if (ok == RoomKind.Hall) return 2500; if (targetPublic) return 2000; if (ok == RoomKind.Garage || ok == RoomKind.Shop) return 800; return 0;
                default:
                    if (ok == RoomKind.Hall || ok == RoomKind.Corridor) return 2500; if (targetPublic) return 1800; if (ok == RoomKind.Barn || ok == RoomKind.Shop || ok == RoomKind.Garage) return 1500; return 0;
            }
        }

        /// <summary>Interior door centered at a plan point on an interior wall of that floor.</summary>
        bool AddInteriorAt(int floor, Vector2 p, string product)
        {
            for (int i = 0; i < Walls.Count; i++)
            {
                var w = Walls[i];
                if (w.Floor != floor || w.Exterior) continue;
                bool h = Mathf.Abs(w.A.y - w.B.y) < 0.01f;
                if (h && Mathf.Abs(w.A.y - p.y) < 0.02f && p.x > Mathf.Min(w.A.x, w.B.x) && p.x < Mathf.Max(w.A.x, w.B.x))
                    return TryAdd(floor, i, Mathf.Abs(p.x - w.A.x), product, false);
                if (!h && Mathf.Abs(w.A.x - p.x) < 0.02f && p.y > Mathf.Min(w.A.y, w.B.y) && p.y < Mathf.Max(w.A.y, w.B.y))
                    return TryAdd(floor, i, Mathf.Abs(p.y - w.A.y), product, false);
            }
            return false;
        }

        /// <summary>Rooms on each side of an opening (interior openings) — the second is null for exterior openings.</summary>
        public (PlanRoom a, PlanRoom b) RoomsAt(PlanOpening o)
        {
            var w = Walls[o.Wall]; var p = w.At(o.CenterFt); var n = new Vector2(-w.Dir.y, w.Dir.x) * 0.2f;
            return (RoomAt(o.Floor, p + n), RoomAt(o.Floor, p - n));
        }

        public bool HasDoor(PlanRoom r)
        {
            foreach (var o in Openings)
            {
                if (!o.IsDoor || o.Floor != r.Floor) continue;
                var (a, b) = RoomsAt(o);
                if (a == r || b == r) return true;
            }
            return false;
        }

        /// <summary>True when an interior door already connects the room to a room it may open onto (not e.g. a bedroom's jack-and-jill bath door).</summary>
        bool HasGoodDoor(PlanRoom r)
        {
            var k = KindOf(r.Name);
            foreach (var o in Openings)
            {
                if (!o.IsDoor || o.Floor != r.Floor || Walls[o.Wall].Exterior) continue;
                var (a, b) = RoomsAt(o);
                var other = a == r ? b : b == r ? a : null;
                if (other != null && DoorScore(k, r, KindOf(other.Name), other) >= 1000f) return true;
            }
            return false;
        }

        public int DoorCount(PlanRoom r)
        {
            int n = 0;
            foreach (var o in Openings) { if (!o.IsDoor || o.Floor != r.Floor) continue; var (a, b) = RoomsAt(o); if (a == r || b == r) n++; }
            return n;
        }
    }
}
