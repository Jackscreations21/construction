using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One room of a player design (plan feet, same coordinates as <see cref="PlanRoom"/>: x across from the front-left corner,
    /// y back from the front wall).</summary>
    [Serializable]
    public class DesignRoom
    {
        public string name; public int floor; public float x0, y0, x1, y1;
        public Rect Ft { get => Rect.MinMaxRect(x0, y0, x1, y1); set { x0 = value.xMin; y0 = value.yMin; x1 = value.xMax; y1 = value.yMax; } }
        public DesignRoom Clone() => (DesignRoom)MemberwiseClone();
    }

    /// <summary>A door/window the player placed. Exterior: side 0 front, 1 right, 2 back, 3 left and pos = plan x (front/back) or plan y
    /// (left/right) of its center, exactly like <see cref="Blueprint.Door"/>. Interior: (x, y) on an interior wall line.</summary>
    [Serializable]
    public class DesignOpening
    {
        public int floor; public bool ext; public int side; public float pos, x, y; public string product;
    }

    /// <summary>A plan point (an auto-placed opening the player removed).</summary>
    [Serializable] public class DesignPoint { public int floor; public float x, y; }
    [Serializable] public class DesignLevel { public string room; public float offset; }

    /// <summary>A player-drawn plan: everything the designer edits, serializable with JsonUtility. <see cref="Build"/> turns it into a
    /// <see cref="CustomPlan"/> (a <see cref="Blueprint"/>), so walls, auto windows/doors, stairs, MEP, code checks and takeoff work
    /// exactly like the library plans.</summary>
    [Serializable]
    public class PlanDesign
    {
        public string id = "", name = "My plan", basedOn = "", created = "";
        public int group;                                  // PlanGroup
        public float width = 40f, depth = 30f, wallHeight = 8f, pitch = 6f, quality = 0.6f, slabIn = 4f, mezzanine;
        public int floors = 1, units = 1, columnLines;
        public int foundation, roof, wallFrame, roofFrame, covering;   // FoundationType, RoofType, WallSystem, RoofSystem, RoofCovering
        public string stud = "2x6", joist = "2x10", rafter = "2x10", occupancy = "", construction = "";
        public int bearingLines = 1;
        public bool ridgeAlongY, autoStructure = true, finished = true, conditioned = true;
        public List<DesignRoom> rooms = new List<DesignRoom>();
        public List<DesignOpening> openings = new List<DesignOpening>();
        public List<DesignPoint> suppressed = new List<DesignPoint>();
        public List<string> stairsDown = new List<string>();
        public List<string> privateRooms = new List<string>();   // Blueprint.Private (apartment street entry beside a shop)
        public bool partyWalls;                                   // Blueprint.PartyWalls (row building: no side windows)
        public List<DesignLevel> levels = new List<DesignLevel>();

        public PlanGroup Group { get => (PlanGroup)group; set => group = (int)value; }
        public FoundationType Foundation { get => (FoundationType)foundation; set => foundation = (int)value; }
        public RoofType Roof { get => (RoofType)roof; set => roof = (int)value; }
        public WallSystem WallFrame { get => (WallSystem)wallFrame; set => wallFrame = (int)value; }
        public RoofSystem RoofFrame { get => (RoofSystem)roofFrame; set => roofFrame = (int)value; }
        public RoofCovering Covering { get => (RoofCovering)covering; set => covering = (int)value; }

        public string ToJson() => JsonUtility.ToJson(this);
        public static PlanDesign FromJson(string json) { var d = JsonUtility.FromJson<PlanDesign>(json); d.Fix(); return d; }
        public PlanDesign Clone() => FromJson(ToJson());

        /// <summary>Null lists from old/partial JSON become empty.</summary>
        public void Fix()
        {
            rooms ??= new List<DesignRoom>(); openings ??= new List<DesignOpening>(); suppressed ??= new List<DesignPoint>();
            stairsDown ??= new List<string>(); levels ??= new List<DesignLevel>(); privateRooms ??= new List<string>();
            if (string.IsNullOrEmpty(name)) name = "My plan";
            floors = Mathf.Clamp(floors, 1, 3); units = Mathf.Max(1, units);
        }

        public List<DesignRoom> RoomsOn(int floor) => rooms.FindAll(r => r.floor == floor);

        // ------------------------------------------------------------------ group presets

        /// <summary>Sets the structure/occupancy defaults a new building of this group starts with (same as the library factories).</summary>
        public void ApplyGroupDefaults(PlanGroup g)
        {
            Group = g; occupancy = ""; construction = ""; units = 1; mezzanine = 0f; columnLines = 0;
            switch (g)
            {
                case PlanGroup.Residential:
                    WallFrame = WallSystem.WoodStud; RoofFrame = RoofSystem.Trusses; Covering = RoofCovering.Shingles; Roof = RoofType.Gable;
                    Foundation = FoundationType.Slab; stud = "2x6"; wallHeight = 8f; pitch = 6f; quality = 0.6f; slabIn = 4f; finished = true; conditioned = true; ridgeAlongY = false; break;
                case PlanGroup.Outbuilding:
                    WallFrame = WallSystem.WoodStud; RoofFrame = RoofSystem.Trusses; Covering = RoofCovering.Shingles; Roof = RoofType.Gable;
                    Foundation = FoundationType.Slab; stud = "2x4"; wallHeight = 8f; pitch = 6f; quality = 0.5f; slabIn = 4f; finished = false; conditioned = false; ridgeAlongY = true; break;
                case PlanGroup.Commercial:
                    WallFrame = WallSystem.Cmu; RoofFrame = RoofSystem.BarJoist; Covering = RoofCovering.Tpo; Roof = RoofType.Flat;
                    Foundation = FoundationType.Slab; stud = "2x6"; wallHeight = 14f; pitch = 0.25f; quality = 0.6f; slabIn = 4f; finished = true; conditioned = true; ridgeAlongY = false; break;
                default:
                    WallFrame = WallSystem.SteelFrame; RoofFrame = RoofSystem.RigidFrame; Covering = RoofCovering.StandingSeam; Roof = RoofType.Gable;
                    Foundation = FoundationType.Slab; stud = "2x6"; wallHeight = 20f; pitch = 1f; quality = 0.5f; slabIn = 6f; finished = false; conditioned = false; ridgeAlongY = false; break;
            }
        }

        /// <summary>Wall-system presets the designer offers (each sets walls, roof framing, covering and, where needed, the foundation).</summary>
        public static readonly string[] WallPresets = { "Wood 2x6", "Wood 2x4", "Post-frame", "Steel (PEMB)", "Block (CMU)" };
        public int WallPreset => WallFrame == WallSystem.PostFrame ? 2 : WallFrame == WallSystem.SteelFrame ? 3 : WallFrame == WallSystem.Cmu ? 4 : stud == "2x4" ? 1 : 0;
        public void SetWallPreset(int i)
        {
            switch (i)
            {
                case 0: case 1:
                    WallFrame = WallSystem.WoodStud; stud = i == 0 ? "2x6" : "2x4";
                    if (RoofFrame != RoofSystem.Rafters) RoofFrame = RoofSystem.Trusses;
                    if (Roof == RoofType.Flat) Roof = RoofType.Gable;
                    if (Covering == RoofCovering.Tpo) Covering = RoofCovering.Shingles;
                    if (Foundation == FoundationType.PostFrame) Foundation = FoundationType.Slab;
                    wallHeight = Mathf.Min(wallHeight, stud == "2x4" ? 10f : Group == PlanGroup.Residential || Group == PlanGroup.Outbuilding ? 12f : 16f);
                    break;
                case 2:
                    WallFrame = WallSystem.PostFrame; RoofFrame = RoofSystem.PostFrameTrusses; Foundation = FoundationType.PostFrame; Covering = RoofCovering.MetalPanels;
                    if (Roof == RoofType.Flat || Roof == RoofType.Hip) Roof = RoofType.Gable; wallHeight = Mathf.Clamp(wallHeight, 8f, 20f); pitch = Mathf.Max(pitch, 3f); break;
                case 3:
                    WallFrame = WallSystem.SteelFrame; RoofFrame = RoofSystem.RigidFrame; Covering = RoofCovering.StandingSeam;
                    if (Foundation == FoundationType.PostFrame || Foundation == FoundationType.Skids) Foundation = FoundationType.Slab;
                    if (Roof == RoofType.Flat || Roof == RoofType.Hip || Roof == RoofType.Gambrel) Roof = RoofType.Gable; pitch = Mathf.Clamp(pitch, 0.5f, 4f); break;
                default:
                    WallFrame = WallSystem.Cmu; RoofFrame = RoofSystem.BarJoist; Roof = RoofType.Flat; Covering = RoofCovering.Tpo; pitch = 0.25f;
                    if (Foundation == FoundationType.PostFrame || Foundation == FoundationType.Skids) Foundation = FoundationType.Slab; wallHeight = Mathf.Min(wallHeight, 28f); break;
            }
        }

        /// <summary>Picking a roof shape keeps the framing/covering consistent (flat = bar joists + TPO membrane).</summary>
        public void SetRoof(RoofType r)
        {
            Roof = r;
            if (r == RoofType.Flat) { RoofFrame = RoofSystem.BarJoist; Covering = RoofCovering.Tpo; pitch = 0.25f; return; }
            if (RoofFrame == RoofSystem.BarJoist) RoofFrame = WallFrame == WallSystem.PostFrame ? RoofSystem.PostFrameTrusses : WallFrame == WallSystem.SteelFrame ? RoofSystem.RigidFrame : RoofSystem.Trusses;
            if (Covering == RoofCovering.Tpo) Covering = WallFrame == WallSystem.WoodStud ? RoofCovering.Shingles : RoofCovering.MetalPanels;
            if (pitch < 2f && Covering == RoofCovering.Shingles) pitch = 4f;
            if (pitch < 1f) pitch = 1f;
        }

        // ------------------------------------------------------------------ build

        static readonly string[] JoistSizes = { "2x10", "2x12", "I-joist 11-7/8", "I-joist 14", "Open-web 16" };

        /// <summary>Auto structure: the smallest joist and fewest bearing lines that carry the floor span.</summary>
        public void AutoSizeStructure()
        {
            if (!autoStructure) return;
            if (mezzanine > 0f) return;
            for (int bl = 1; bl <= 3; bl++)
                foreach (var j in JoistSizes)
                    if (depth / (bl + 1) <= Blueprint.JoistSpanLimit(j)) { bearingLines = bl; joist = j; return; }
            bearingLines = 3; joist = "Open-web 16";
        }

        static string Slug(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            var r = sb.ToString().Trim('-'); while (r.Contains("--")) r = r.Replace("--", "-");
            return r.Length > 24 ? r.Substring(0, 24).Trim('-') : r;
        }
        public static string IdFor(string name) => PlanDesigner.IdPrefix + (Slug(name).Length > 0 ? Slug(name) : "plan");

        /// <summary>Code occupancy shown on the plan: the design's own, or one derived from its group and rooms.</summary>
        public string OccupancyLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(occupancy)) return occupancy;
                switch (Group)
                {
                    case PlanGroup.Residential: return "IRC R-3 (one-family dwelling)";
                    case PlanGroup.Outbuilding: return "IRC accessory structure (U)";
                    case PlanGroup.Commercial:
                        bool dining = rooms.Exists(r => Blueprint.KindOf(r.name) == RoomKind.Dining || Blueprint.KindOf(r.name) == RoomKind.CommKitchen);
                        bool sales = rooms.Exists(r => Blueprint.KindOf(r.name) == RoomKind.Sales);
                        return dining ? "IBC A-2 (restaurant)" : sales ? "IBC M (mercantile)" : "IBC B (business)";
                    default: return "IBC S-1 (moderate-hazard storage)";
                }
            }
        }

        string ConstructionLabel => !string.IsNullOrEmpty(construction) ? construction
            : WallFrame == WallSystem.SteelFrame ? "II-B (pre-engineered steel)" : WallFrame == WallSystem.Cmu ? "II-B (noncombustible: CMU + steel joists)" : "V-B (wood frame)";

        /// <summary>The design as a finished, registrable plan (walls, openings, stairs, fixtures derived). Id falls back to "custom-draft".</summary>
        public CustomPlan Build()
        {
            Fix();
            int beds = 0, full = 0, half = 0;
            foreach (var r in rooms) { var k = Blueprint.KindOf(r.name); if (k == RoomKind.Bedroom) beds++; else if (k == RoomKind.Bath) full++; else if (k == RoomKind.HalfBath) half++; }
            var g = Group;
            var b = new CustomPlan
            {
                Design = this, Id = string.IsNullOrEmpty(id) ? PlanDesigner.IdPrefix + "draft" : id, Name = string.IsNullOrEmpty(name) ? "My plan" : name,
                Style = "Custom design", Category = PlanDesigner.Category, Group = g,
                MinStage = g == PlanGroup.Outbuilding ? CompanyStage.Contractor : CompanyStage.Builder,
                WidthFt = width, DepthFt = depth, Floors = floors, WallHeightFt = wallHeight, Pitch = pitch, Roof = Roof, Foundation = Foundation,
                StudSize = stud, FrontDoorFt = 0f, BaseQuality = quality, Beds = beds / units, Baths2 = (full * 2 + half) / units, Units = units,
                Occupancy = OccupancyLabel, ConstructionType = ConstructionLabel, WallFrame = WallFrame, RoofFrame = RoofFrame, Covering = Covering,
                RidgeAlongY = ridgeAlongY, FloorJoist = joist, BearingLines = bearingLines, RafterSize = rafter, InteriorColumnLines = columnLines,
                SlabIn = slabIn, MezzanineFt = mezzanine, Finished = finished, Conditioned = conditioned,
                Notes = string.IsNullOrEmpty(basedOn) ? "Drawn in the Plan Designer." : "Drawn in the Plan Designer from " + basedOn + ".",
            };
            if (g == PlanGroup.Industrial && b.RoofFrame == RoofSystem.RigidFrame && b.InteriorColumnLines == 0)
                b.InteriorColumnLines = Mathf.Max(0, Mathf.CeilToInt((ridgeAlongY ? width : depth) / 160f) - 1);
            var sorted = new List<DesignRoom>(rooms);
            sorted.Sort((p, q) => p.floor != q.floor ? p.floor.CompareTo(q.floor) : p.y0 != q.y0 ? p.y0.CompareTo(q.y0) : p.x0.CompareTo(q.x0));
            foreach (var r in sorted) b.R(r.name, r.x0, r.y0, r.x1, r.y1, r.floor);
            foreach (var o in openings)
            {
                if (o.ext) b.Door(o.side, o.pos, o.product, o.floor);   // Door/Window share one list
                else b.DoorAt(o.x, o.y, o.product, o.floor);
            }
            foreach (var s in stairsDown) b.StairDown(s);
            foreach (var s in privateRooms) b.Private(s);
            b.PartyWalls = partyWalls;
            foreach (var l in levels) b.Level(l.room, l.offset);
            foreach (var p in suppressed) b.Suppressed.Add((p.floor, new Vector2(p.x, p.y)));
            b.Finish();
            return b;
        }

        // ------------------------------------------------------------------ copy

        /// <summary>A design copied from any plan (library or custom): rooms, every door/window it has (as placed openings), structure.</summary>
        public static PlanDesign From(HousePlan plan)
        {
            if (plan is CustomPlan cp && cp.Design != null) { var c = cp.Design.Clone(); c.id = ""; c.name = cp.Name + " (copy)"; c.basedOn = cp.Name; return c; }
            var bp = Blueprints.Info(plan);
            var d = new PlanDesign
            {
                name = plan.Name.Replace("The ", "") + " (my version)", basedOn = plan.Name, group = (int)plan.Group, width = plan.WidthFt, depth = plan.DepthFt,
                wallHeight = plan.WallHeightFt, pitch = plan.Pitch, quality = plan.BaseQuality, floors = plan.Floors, stud = plan.StudSize,
                foundation = (int)plan.Foundation, roof = (int)plan.Roof, autoStructure = false,
            };
            if (bp != null)
            {
                d.units = bp.Units; d.wallFrame = (int)bp.WallFrame; d.roofFrame = (int)bp.RoofFrame; d.covering = (int)bp.Covering; d.ridgeAlongY = bp.RidgeAlongY;
                d.joist = bp.FloorJoist; d.bearingLines = bp.BearingLines; d.rafter = bp.RafterSize; d.columnLines = bp.InteriorColumnLines; d.slabIn = bp.SlabIn;
                d.mezzanine = bp.MezzanineFt; d.finished = bp.Finished; d.conditioned = bp.Conditioned; d.occupancy = bp.Occupancy; d.construction = bp.ConstructionType;
                foreach (var s in bp.ReversedStairs) d.stairsDown.Add(s);
                foreach (var s in bp.PrivateRooms) d.privateRooms.Add(s);
                d.partyWalls = bp.PartyWalls;
                foreach (var kv in bp.LevelOffsetFt) d.levels.Add(new DesignLevel { room = kv.Key, offset = kv.Value });
            }
            foreach (var r in plan.Rooms) d.rooms.Add(new DesignRoom { name = r.Name, floor = r.Floor, x0 = r.Ft.xMin, y0 = r.Ft.yMin, x1 = r.Ft.xMax, y1 = r.Ft.yMax });
            var src = bp ?? plan;
            foreach (var o in src.Openings)
            {
                var w = src.Walls[o.Wall]; var p = w.At(o.CenterFt);
                if (w.Exterior)
                {
                    int first = src.Walls.FindIndex(x => x.Floor == o.Floor && x.Exterior);
                    int side = Mathf.Clamp(o.Wall - first, 0, 3);
                    d.openings.Add(new DesignOpening { floor = o.Floor, ext = true, side = side, pos = side % 2 == 0 ? p.x : p.y, product = o.ProductId });
                }
                else d.openings.Add(new DesignOpening { floor = o.Floor, ext = false, x = p.x, y = p.y, product = o.ProductId });
            }
            return d;
        }
    }

    /// <summary>A plan drawn by the player. Builds like any <see cref="Blueprint"/>; auto openings the player removed are dropped after the rules run.</summary>
    public class CustomPlan : Blueprint
    {
        public PlanDesign Design;
        public readonly List<(int floor, Vector2 at)> Suppressed = new List<(int, Vector2)>();

        public override HousePlan Finish()
        {
            base.Finish();
            if (Suppressed.Count > 0)
                Openings.RemoveAll(o =>
                {
                    var p = Walls[o.Wall].At(o.CenterFt);
                    if (!Suppressed.Exists(s => s.floor == o.Floor && (p - s.at).sqrMagnitude < 0.36f)) return false;
                    // never drop one the player placed there
                    return Design == null || !Design.openings.Exists(d => d.floor == o.Floor && d.product == o.ProductId && (PlacedPoint(d) - p).sqrMagnitude < 0.25f);
                });
            return this;
        }

        Vector2 PlacedPoint(DesignOpening o) =>
            !o.ext ? new Vector2(o.x, o.y) : o.side == 0 ? new Vector2(o.pos, 0f) : o.side == 1 ? new Vector2(WidthFt, o.pos) : o.side == 2 ? new Vector2(o.pos, DepthFt) : new Vector2(0f, o.pos);
    }
}
