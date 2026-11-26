using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Construction phases a house goes through, each closed by an inspection (and a lender draw).</summary>
    public enum BuildPhase { Foundation, Framing, RoughIn, Drywall, Finish }
    public enum RoofType { Gable, Hip, Shed, Flat, Gambrel }   // Shed/Flat/Gambrel: Blueprints (non-residential)
    public enum FoundationType { Slab, Crawlspace, Basement, PostFrame, Skids }   // PostFrame/Skids: Blueprints (pole barns, sheds)

    /// <summary>A room on a plan, in plan feet. Plan x runs left→right as seen from the street, y runs back from the front wall.</summary>
    public class PlanRoom
    {
        public string Name; public int Floor; public Rect Ft;
        public bool Private => Name.StartsWith("Bed") || Name.StartsWith("Master") || Name.Contains("Bath") || Name == "Powder" || Name == "Laundry" || Name == "Study" || Name == "Mud";
        public bool Wet => Name.Contains("Bath") || Name == "Powder" || Name == "Laundry";
    }

    public class PlanOpening
    {
        public int Floor, Wall; public float CenterFt; public string ProductId;
        public PieceDefinition Product => Catalog.Get(ProductId);
        public bool IsDoor => Product != null && (Product.Category == PieceCategory.Door || Product.Category == PieceCategory.GarageDoor);
        /// <summary>Overhead / sliding vehicle door: framed to the door size (no clearance).</summary>
        public bool IsOverhead => Product != null && Product.Category == PieceCategory.GarageDoor;
        public float WidthFt => Product != null ? Product.Size.x / Units.Foot : 3f;
        public float HeightFt => Product != null ? Product.Size.y / Units.Foot : 4f;
        /// <summary>Rough opening width per WallOpening: windows unit + 1/2", prehung doors unit + 2".</summary>
        public float RoughWidthIn => WidthFt * 12f + (IsOverhead ? 0f : IsDoor ? 2f : 0.5f);
        public float RoughHeightIn => HeightFt * 12f + (IsOverhead ? 0f : IsDoor ? 2.5f : 0.5f);
    }

    /// <summary>One wall line on a plan floor (plan feet). Exterior walls are the four footprint edges (0 front, 1 right, 2 back, 3 left).</summary>
    public class PlanWall
    {
        public int Floor; public Vector2 A, B; public bool Exterior;
        public float LengthFt => Vector2.Distance(A, B);
        public Vector2 Dir => (B - A).normalized;
        public Vector2 At(float ft) => A + Dir * ft;
    }

    /// <summary>One takeoff line: quantity of a material with its unit cost and the phase it's bought for.</summary>
    public struct TakeoffLine
    {
        public BuildPhase Phase; public string Item, Unit; public float Qty; public int UnitCost;
        public int Cost => Mathf.RoundToInt(Qty * UnitCost);
    }

    /// <summary>Material takeoff computed from the plan's real dimensions (IRC framing at 16" O.C., 4x8 sheathing, 4x12 drywall,
    /// architectural shingles in 3-bundle squares). Labor/sub costs are per phase so the total lands near real US builder cost.</summary>
    public class Takeoff
    {
        public float SqFt, FootprintSqFt, PerimeterFt, ExtWallLf, IntWallLf, ExtWallNetSqFt, OpeningSqFt;
        public int Studs, Corners, SheathingSheets, DrywallSheets, Trusses, Joists, SubfloorSheets, RoofSheathingSheets, ShingleBundles, Windows, ExtDoors, IntDoors;
        public float PlateLf, ConcreteYards, RoofSqFt, RoofSquares, WallInsulationSqFt, AtticInsulationSqFt;
        public readonly List<TakeoffLine> Lines = new List<TakeoffLine>();
        public readonly int[] Labor = new int[5];

        public int Materials(BuildPhase p) { int s = 0; foreach (var l in Lines) if (l.Phase == p) s += l.Cost; return s; }
        public int PhaseCost(BuildPhase p) => Materials(p) + Labor[(int)p];
        public int MaterialTotal { get { int s = 0; foreach (var l in Lines) s += l.Cost; return s; } }
        public int Total { get { int s = 0; for (int i = 0; i < 5; i++) s += PhaseCost((BuildPhase)i); return s; } }
        /// <summary>Count of framing members (studs + plates in stock lengths + joists + trusses + sheets) a framed house contains.</summary>
        public int FramingPieces => Studs + Mathf.CeilToInt(PlateLf / 12f) + Joists + Trusses + SheathingSheets + SubfloorSheets + RoofSheathingSheets;
    }

    /// <summary>A house plan: footprint, rooms, wall lines with openings, roof, foundation, required inspections, takeoff.
    /// Walls are derived from the rooms (rooms must tile the footprint); openings come from a front door per plan plus rules:
    /// a window centered on each room's exterior edge (≥ 6'), and a door on the longest interior edge of each private room.</summary>
    public class HousePlan
    {
        public string Id, Name, Style;
        public float WidthFt, DepthFt, WallHeightFt = 8f, Pitch = 6f;   // pitch = rise per 12
        public int Floors = 1, Beds, Baths2;                            // Baths2 = half-baths count x2 (5 = 2.5 baths)
        public RoofType Roof = RoofType.Gable;
        public FoundationType Foundation = FoundationType.Slab;
        public string StudSize = "2x6";
        public float FrontDoorFt;                                       // along the front wall
        public float BaseQuality = 0.6f;                                // 0..1 finish level the plan is drawn for
        public string Category = "house";                               // type id for lookups/filters (Blueprints: ranch, garage, warehouse...)
        public PlanGroup Group = PlanGroup.Residential;                  // Residential / Outbuilding / Commercial / Industrial
        public CompanyStage MinStage = CompanyStage.Builder;            // lowest company stage that can build it
        public readonly List<PlanRoom> Rooms = new List<PlanRoom>();
        public readonly List<PlanWall> Walls = new List<PlanWall>();
        public readonly List<PlanOpening> Openings = new List<PlanOpening>();
        Takeoff _takeoff;

        public virtual float SqFt => WidthFt * DepthFt * Floors;
        public float WallDepthFt => StudSize == "2x6" ? 5.5f / 12f : 3.5f / 12f;
        public string BathsLabel => (Baths2 % 2 == 0 ? (Baths2 / 2).ToString() : (Baths2 / 2) + ".5");
        public virtual string Summary => $"{SqFt:N0} sq ft · {Beds} bed / {BathsLabel} bath · {Floors} {(Floors > 1 ? "stories" : "story")} · {WidthFt}'x{DepthFt}' · {Foundation} · {Roof} {Pitch}/12";
        public Takeoff Takeoff => _takeoff ?? (_takeoff = ComputeTakeoff());

        public List<InspectionPhase> RequiredInspections
        {
            get
            {
                var l = new List<InspectionPhase> { InspectionPhase.Footing };
                if (Foundation != FoundationType.Crawlspace) l.Add(InspectionPhase.UnderSlab);
                l.AddRange(new[] { InspectionPhase.Framing, InspectionPhase.RoughMEP, InspectionPhase.Insulation, InspectionPhase.Final });
                return l;
            }
        }

        /// <summary>The inspection that closes each phase.</summary>
        public static InspectionPhase InspectionFor(BuildPhase p) =>
            p == BuildPhase.Foundation ? InspectionPhase.Footing : p == BuildPhase.Framing ? InspectionPhase.Framing :
            p == BuildPhase.RoughIn ? InspectionPhase.RoughMEP : p == BuildPhase.Drywall ? InspectionPhase.Insulation : InspectionPhase.Final;

        /// <summary>Finish-phase cost multiplier for a finish level 0..1 (builder grade ≈ 0.9, standard ≈ 1.25, custom ≈ 1.6).</summary>
        public static float FinishFactor(float level) => 0.4f + 1.4f * Mathf.Clamp01(level);

        public static string Label(BuildPhase p) => p == BuildPhase.RoughIn ? "Rough-in" : p.ToString();

        /// <summary>Working days a normal crew needs for each phase (scaled by size).</summary>
        public int PhaseDays(BuildPhase p)
        {
            float k = Mathf.Sqrt(SqFt / 1500f);
            int[] baseDays = { Foundation == FoundationType.Basement ? 14 : 9, 14, 12, 12, 18 };
            return Mathf.Max(3, Mathf.RoundToInt(baseDays[(int)p] * k));
        }
        public int TotalDays { get { int s = 0; for (int i = 0; i < 5; i++) s += PhaseDays((BuildPhase)i); return s; } }

        // ------------------------------------------------------------------ build-up

        public HousePlan Room(string name, float x0, float y0, float x1, float y1, int floor = 0)
        { Rooms.Add(new PlanRoom { Name = name, Floor = floor, Ft = Rect.MinMaxRect(x0, y0, x1, y1) }); return this; }

        /// <summary>Derives wall lines and openings from the rooms. Called once by the registry.</summary>
        public virtual HousePlan Finish()
        {
            Walls.Clear(); Openings.Clear(); _takeoff = null;
            for (int f = 0; f < Floors; f++)
            {
                // exterior: 0 front (y=0), 1 right (x=W), 2 back (y=D), 3 left (x=0); each runs counter-clockwise seen from above
                Walls.Add(new PlanWall { Floor = f, A = new Vector2(0, 0), B = new Vector2(WidthFt, 0), Exterior = true });
                Walls.Add(new PlanWall { Floor = f, A = new Vector2(WidthFt, 0), B = new Vector2(WidthFt, DepthFt), Exterior = true });
                Walls.Add(new PlanWall { Floor = f, A = new Vector2(WidthFt, DepthFt), B = new Vector2(0, DepthFt), Exterior = true });
                Walls.Add(new PlanWall { Floor = f, A = new Vector2(0, DepthFt), B = new Vector2(0, 0), Exterior = true });
                // interior: merge collinear room edges that are not on the footprint boundary
                var lines = new Dictionary<(bool horiz, float c), List<(float a, float b)>>();
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
                var keys = new List<(bool horiz, float c)>(lines.Keys);
                keys.Sort((p, q) => p.horiz != q.horiz ? p.horiz.CompareTo(q.horiz) : p.c.CompareTo(q.c));
                foreach (var key in keys)
                {
                    var segs = lines[key]; segs.Sort((p, q) => p.a.CompareTo(q.a));
                    float ca = segs[0].a, cb = segs[0].b;
                    void Emit() => Walls.Add(key.horiz ? new PlanWall { Floor = f, A = new Vector2(ca, key.c), B = new Vector2(cb, key.c) }
                                                       : new PlanWall { Floor = f, A = new Vector2(key.c, ca), B = new Vector2(key.c, cb) });
                    for (int i = 1; i < segs.Count; i++)
                    {
                        if (segs[i].a <= cb + 0.01f) cb = Mathf.Max(cb, segs[i].b);
                        else { Emit(); ca = segs[i].a; cb = segs[i].b; }
                    }
                    Emit();
                }
            }
            // front door, then rule-based windows and interior doors
            AddOpening(0, 0, FrontDoorFt, "door-ext-fg36");
            foreach (var r in Rooms)
            {
                var edges = new[] { (0, r.Ft.yMin < 0.01f, r.Ft.xMin, r.Ft.xMax), (1, r.Ft.xMax > WidthFt - 0.01f, r.Ft.yMin, r.Ft.yMax),
                                    (2, r.Ft.yMax > DepthFt - 0.01f, WidthFt - r.Ft.xMax, WidthFt - r.Ft.xMin), (3, r.Ft.xMin < 0.01f, DepthFt - r.Ft.yMax, DepthFt - r.Ft.yMin) };
                foreach (var (wall, on, a, b) in edges)
                {
                    if (!on || b - a < 6f) continue;
                    string win = r.Name.Contains("Bath") || r.Name == "Powder" || r.Name == "Laundry" ? "win-sh-2436"
                               : r.Name == "Kitchen" ? "win-slider-4836" : r.Name.StartsWith("Living") || r.Name.StartsWith("Great") || r.Name.StartsWith("Family") ? "win-sh-3660" : "win-sh-3648";
                    AddOpening(r.Floor, wall, (a + b) / 2f, win);
                }
                if (!r.Private) continue;
                // door on the longest interior edge
                PlanWall best = null; float bestCenter = 0, bestLen = 0;
                foreach (var w in Walls)
                {
                    if (w.Exterior || w.Floor != r.Floor) continue;
                    bool horiz = Mathf.Abs(w.A.y - w.B.y) < 0.01f;
                    float a, b;
                    if (horiz && (Mathf.Abs(w.A.y - r.Ft.yMin) < 0.01f || Mathf.Abs(w.A.y - r.Ft.yMax) < 0.01f)) { a = Mathf.Max(r.Ft.xMin, Mathf.Min(w.A.x, w.B.x)); b = Mathf.Min(r.Ft.xMax, Mathf.Max(w.A.x, w.B.x)); }
                    else if (!horiz && (Mathf.Abs(w.A.x - r.Ft.xMin) < 0.01f || Mathf.Abs(w.A.x - r.Ft.xMax) < 0.01f)) { a = Mathf.Max(r.Ft.yMin, Mathf.Min(w.A.y, w.B.y)); b = Mathf.Min(r.Ft.yMax, Mathf.Max(w.A.y, w.B.y)); }
                    else continue;
                    if (b - a < 3f) continue;
                    // prefer an edge that opens onto a shared (public) room: hall, living, kitchen
                    float mid = (a + b) / 2f;
                    Vector2 probe = horiz ? new Vector2(mid, w.A.y + (Mathf.Abs(w.A.y - r.Ft.yMin) < 0.01f ? -0.5f : 0.5f))
                                          : new Vector2(w.A.x + (Mathf.Abs(w.A.x - r.Ft.xMin) < 0.01f ? -0.5f : 0.5f), mid);
                    var other = Rooms.Find(o => o.Floor == r.Floor && o != r && o.Ft.Contains(probe));
                    float score = (b - a) + (other != null && !other.Private ? 1000f : 0f);
                    if (score > bestLen) { bestLen = score; best = w; bestCenter = mid; }
                }
                if (best == null) continue;
                float along = Mathf.Abs(best.A.y - best.B.y) < 0.01f ? Mathf.Abs(bestCenter - best.A.x) : Mathf.Abs(bestCenter - best.A.y);
                AddOpening(r.Floor, Walls.IndexOf(best), along, r.Name.Contains("Bath") || r.Name == "Powder" ? "door-int-6p24" : "door-int-6p30");
            }
            return this;
        }

        void AddOpening(int floor, int wallIndexOnFloor, float centerFt, string product)
        {
            int wi = wallIndexOnFloor;
            if (wi < 4) wi = Walls.FindIndex(w => w.Floor == floor) + wallIndexOnFloor; // exterior index relative to the floor
            var o = new PlanOpening { Floor = floor, Wall = wi, CenterFt = centerFt, ProductId = product };
            if (floor > 0 && product.StartsWith("door-ext")) return;
            foreach (var x in Openings)
                if (x.Wall == wi && Mathf.Abs(x.CenterFt - centerFt) < (x.WidthFt + o.WidthFt) / 2f + 1f) return; // keep clear of an existing opening
            float half = o.WidthFt / 2f + 0.5f;
            o.CenterFt = Mathf.Clamp(centerFt, half, Walls[wi].LengthFt - half);
            Openings.Add(o);
        }

        // ------------------------------------------------------------------ takeoff

        protected virtual Takeoff ComputeTakeoff()
        {
            var t = new Takeoff();
            float W = WidthFt, D = DepthFt, H = WallHeightFt;
            t.FootprintSqFt = W * D; t.SqFt = SqFt; t.PerimeterFt = 2 * (W + D);
            t.Corners = 4 * Floors;
            int intWalls = 0;
            foreach (var w in Walls)
            {
                if (w.Exterior) t.ExtWallLf += w.LengthFt; else { t.IntWallLf += w.LengthFt; intWalls++; }
                t.Studs += Mathf.FloorToInt(w.LengthFt * 12f / 16f) + 1;
            }
            foreach (var o in Openings)
            {
                t.Studs += 2;   // extra king + jack each side beyond the layout stud removed
                if (Walls[o.Wall].Exterior) t.OpeningSqFt += o.WidthFt * o.HeightFt;
                if (o.IsDoor) { if (Walls[o.Wall].Exterior) t.ExtDoors++; else t.IntDoors++; } else t.Windows++;
            }
            t.Studs += 2 * t.Corners + 2 * intWalls;    // corner backers + partition backers
            t.PlateLf = 3f * (t.ExtWallLf + t.IntWallLf); // bottom plate + double top plate
            t.ExtWallNetSqFt = t.ExtWallLf * H - t.OpeningSqFt;
            t.SheathingSheets = Mathf.CeilToInt(t.ExtWallNetSqFt / 32f * 1.10f);
            float drywallSqFt = t.ExtWallNetSqFt + 2f * t.IntWallLf * H + t.FootprintSqFt * Floors;
            t.DrywallSheets = Mathf.CeilToInt(drywallSqFt / 48f * 1.12f);

            // concrete (yd³): footing + stem/foundation wall + slab
            float footing, wall, slab = 0f;
            switch (Foundation)
            {
                case FoundationType.Slab: footing = t.PerimeterFt * (16f / 12f) * 1f; wall = 0f; slab = t.FootprintSqFt * (4f / 12f); break;
                case FoundationType.Crawlspace: footing = t.PerimeterFt * (20f / 12f) * (10f / 12f); wall = t.PerimeterFt * (8f / 12f) * (32f / 12f); break;
                default: footing = t.PerimeterFt * 2f * 1f; wall = t.PerimeterFt * (8f / 12f) * 8f; slab = t.FootprintSqFt * (4f / 12f); break;
            }
            t.ConcreteYards = (footing + wall + slab) / 27f;

            // floor systems: crawl/basement first floor + every upper floor, 2x10 @ 16" spanning the depth (one mid bearing line)
            int decks = Floors - 1 + (Foundation == FoundationType.Slab ? 0 : 1);
            t.Joists = decks * 2 * (Mathf.FloorToInt(W * 12f / 16f) + 1);
            t.SubfloorSheets = Mathf.CeilToInt(decks * t.FootprintSqFt / 32f * 1.05f);

            // roof: 12" overhangs, pitch factor sqrt(1 + (rise/12)^2); trusses @ 24" across the width, spanning the depth
            float pf = Mathf.Sqrt(1f + Pitch * Pitch / 144f);
            t.RoofSqFt = (W + 2f) * (D + 2f) * pf;
            t.RoofSquares = t.RoofSqFt / 100f;
            t.ShingleBundles = Mathf.CeilToInt(t.RoofSquares * 1.10f * 3f);
            t.RoofSheathingSheets = Mathf.CeilToInt(t.RoofSqFt / 32f * 1.08f);
            t.Trusses = Mathf.FloorToInt(W * 12f / 24f) + 1;
            t.WallInsulationSqFt = t.ExtWallNetSqFt;
            t.AtticInsulationSqFt = t.FootprintSqFt;

            // finish grade: a custom plan's trim, flooring, cabinets and fixtures cost more per unit than a starter's
            float fq = FinishFactor(BaseQuality);
            void L(BuildPhase p, string item, float qty, string unit, int cost) { if (qty > 0) t.Lines.Add(new TakeoffLine { Phase = p, Item = item, Qty = qty, Unit = unit, UnitCost = p == BuildPhase.Finish ? Mathf.RoundToInt(cost * fq) : cost }); }
            var F = BuildPhase.Foundation; var Fr = BuildPhase.Framing; var R = BuildPhase.RoughIn; var Dw = BuildPhase.Drywall; var Fi = BuildPhase.Finish;
            L(F, "Ready-mix concrete 3000 psi", Mathf.Ceil(t.ConcreteYards * 1.05f), "yd³", 165);
            L(F, "#4 rebar x 20'", Mathf.Ceil(t.PerimeterFt * 3f / 20f * 1.1f), "pcs", 9);
            L(F, "Anchor bolts 1/2\" x 10\"", Mathf.Ceil(t.PerimeterFt / 6f) + 8, "pcs", 2);
            if (Foundation != FoundationType.Crawlspace) L(F, "Gravel base + 6 mil vapor barrier", t.FootprintSqFt, "sq ft", 1);
            L(Fr, StudSize + " precut studs", t.Studs, "pcs", StudSize == "2x6" ? 8 : 4);
            L(Fr, StudSize + " plates (12' stock)", Mathf.Ceil(t.PlateLf / 12f), "pcs", StudSize == "2x6" ? 11 : 7);
            L(Fr, "OSB 7/16\" wall sheathing 4x8", t.SheathingSheets, "sheets", 16);
            L(Fr, "2x10 floor joists", t.Joists, "pcs", 22);
            L(Fr, "3/4\" T&G subfloor 4x8", t.SubfloorSheets, "sheets", 48);
            L(Fr, $"Roof trusses {D + 2f:0}' span {Pitch}/12", t.Trusses, "pcs", Mathf.RoundToInt(6f * D));
            L(Fr, "OSB 7/16\" roof sheathing 4x8", t.RoofSheathingSheets, "sheets", 16);
            L(Fr, "Architectural shingles (bundle)", t.ShingleBundles, "bundles", 42);
            L(Fr, "Windows", t.Windows, "pcs", 280);
            L(Fr, "Exterior doors", t.ExtDoors, "pcs", 600);
            L(R, "Plumbing rough (per bath/kitchen fixture group)", Baths2 + 2, "groups", 2600);
            L(R, "Electrical rough (circuits)", Mathf.Ceil(SqFt / 100f) + 10, "circuits", 260);
            L(R, "HVAC: furnace + AC, ducts", 1, "system", Mathf.RoundToInt(5000 + SqFt * 3.5f));
            L(Dw, "R-21 wall batts", t.WallInsulationSqFt, "sq ft", 1);
            L(Dw, "R-49 attic blown-in", t.AtticInsulationSqFt, "sq ft", 1);
            L(Dw, "Drywall 1/2\" 4x12", t.DrywallSheets, "sheets", 21);
            L(Dw, "Mud, tape, screws (per sheet)", t.DrywallSheets, "sheets", 4);
            L(Fi, "Siding + trim (sq ft wall)", t.ExtWallNetSqFt, "sq ft", 4);
            L(Fi, "Interior doors (prehung)", t.IntDoors, "pcs", 140);
            L(Fi, "Flooring (LVP/carpet/tile mix)", SqFt, "sq ft", 5);
            L(Fi, "Kitchen cabinets + counters", 1, "kitchen", Mathf.RoundToInt(7000 + SqFt * 3f));
            L(Fi, "Paint, trim, fixtures, appliances", SqFt, "sq ft", 6);
            L(Fi, "Driveway, sod, landscaping", 1, "lot", 6500);
            // crew/sub labor per phase (US 2026 rates)
            t.Labor[0] = Mathf.RoundToInt(t.ConcreteYards * 120f + 2500f + SqFt * 10f);   // + general conditions: supervision, survey, temp power, dumpsters, toilets
            t.Labor[1] = Mathf.RoundToInt(SqFt * 9f + t.RoofSquares * 90f);
            t.Labor[2] = Mathf.RoundToInt(SqFt * 11f);
            t.Labor[3] = Mathf.RoundToInt(t.DrywallSheets * 32f + SqFt * 1.2f);
            t.Labor[4] = Mathf.RoundToInt(SqFt * 12f * fq);
            return t;
        }
    }

    /// <summary>The plan library (data). 900 sq ft starter up to a 3,500 sq ft custom.</summary>
    public static class HousePlans
    {
        static List<HousePlan> _all;
        public static IReadOnlyList<HousePlan> All { get { if (_all == null) Build(); return _all; } }
        public static HousePlan Get(string id) { foreach (var p in All) if (p.Id == id) return p; return null; }
        public static void Register(HousePlan p) { if (_all == null) Build(); _all.RemoveAll(x => x.Id == p.Id); _all.Add(p.Finish()); }

        static void Build()
        {
            _all = new List<HousePlan>();
            var starter = new HousePlan { Id = "starter-900", Name = "The Aspen (starter ranch)", Style = "Ranch", WidthFt = 30, DepthFt = 30, Beds = 2, Baths2 = 2, Pitch = 5, FrontDoorFt = 8, BaseQuality = 0.45f }
                .Room("Living", 0, 0, 16, 16).Room("Kitchen", 16, 0, 30, 16)
                .Room("Bed 1", 0, 16, 12, 30).Room("Bath", 12, 16, 18, 30).Room("Bed 2", 18, 16, 30, 30);
            var cottage = new HousePlan { Id = "cottage-1200", Name = "The Birch (cottage)", Style = "Cottage", WidthFt = 40, DepthFt = 30, Beds = 3, Baths2 = 4, Pitch = 7, Foundation = FoundationType.Crawlspace, FrontDoorFt = 9, BaseQuality = 0.55f }
                .Room("Living", 0, 0, 18, 16).Room("Kitchen", 18, 0, 40, 16)
                .Room("Bed 1", 0, 16, 12, 30).Room("Bath 1", 12, 16, 18, 30).Room("Bed 2", 18, 16, 28, 30).Room("Bath 2", 28, 16, 32, 30).Room("Bed 3", 32, 16, 40, 30);
            var ranch = new HousePlan { Id = "ranch-1600", Name = "The Cedar (ranch)", Style = "Ranch", WidthFt = 50, DepthFt = 32, Beds = 3, Baths2 = 4, WallHeightFt = 9, Roof = RoofType.Hip, Pitch = 6, FrontDoorFt = 10, BaseQuality = 0.6f }
                .Room("Living", 0, 0, 20, 18).Room("Kitchen", 20, 0, 36, 18).Room("Dining", 36, 0, 50, 18)
                .Room("Bed 1", 0, 18, 14, 32).Room("Bath 1", 14, 18, 20, 32).Room("Laundry", 20, 18, 26, 32).Room("Bath 2", 26, 18, 32, 32).Room("Bed 2", 32, 18, 40, 32).Room("Bed 3", 40, 18, 50, 32);
            var craftsman = new HousePlan { Id = "craftsman-1920", Name = "The Dogwood (craftsman)", Style = "Craftsman", WidthFt = 48, DepthFt = 40, Beds = 4, Baths2 = 4, Pitch = 7, Foundation = FoundationType.Crawlspace, FrontDoorFt = 10, BaseQuality = 0.7f }
                .Room("Living", 0, 0, 20, 20).Room("Kitchen", 20, 0, 34, 20).Room("Dining", 34, 0, 48, 20)
                .Room("Master", 0, 20, 16, 40).Room("Bath 1", 16, 20, 22, 40).Room("Bed 2", 22, 20, 32, 40).Room("Bath 2", 32, 20, 36, 40)
                .Room("Bed 3", 36, 20, 48, 30).Room("Bed 4", 36, 30, 48, 40);
            var two = new HousePlan { Id = "twostory-2400", Name = "The Elm (two-story)", Style = "Colonial", WidthFt = 40, DepthFt = 30, Floors = 2, Beds = 4, Baths2 = 5, WallHeightFt = 9, Pitch = 6, Foundation = FoundationType.Basement, FrontDoorFt = 20, BaseQuality = 0.7f }
                .Room("Living", 0, 0, 18, 16).Room("Kitchen", 18, 0, 40, 16).Room("Family", 0, 16, 26, 30).Room("Powder", 26, 16, 31, 30).Room("Laundry", 31, 16, 40, 30)
                .Room("Bed 2", 0, 0, 16, 16, 1).Room("Bath 2", 16, 0, 24, 16, 1).Room("Bed 3", 24, 0, 40, 16, 1)
                .Room("Bed 4", 0, 16, 14, 30, 1).Room("Bath 1", 14, 16, 22, 30, 1).Room("Master", 22, 16, 40, 30, 1);
            var estate = new HousePlan { Id = "estate-3500", Name = "The Fir (custom estate)", Style = "Custom", WidthFt = 50, DepthFt = 35, Floors = 2, Beds = 5, Baths2 = 7, WallHeightFt = 9, Roof = RoofType.Hip, Pitch = 8, Foundation = FoundationType.Basement, FrontDoorFt = 24, BaseQuality = 0.85f }
                .Room("Great room", 0, 0, 24, 20).Room("Kitchen", 24, 0, 50, 20).Room("Study", 0, 20, 14, 35).Room("Powder", 14, 20, 20, 35).Room("Mud", 20, 20, 28, 35).Room("Dining", 28, 20, 50, 35)
                .Room("Master", 0, 0, 20, 18, 1).Room("Master Bath", 20, 0, 30, 18, 1).Room("Bed 2", 30, 0, 50, 18, 1)
                .Room("Bed 3", 0, 18, 16, 35, 1).Room("Bath 2", 16, 18, 24, 35, 1).Room("Bed 4", 24, 18, 38, 35, 1).Room("Bath 3", 38, 18, 42, 35, 1).Room("Bed 5", 42, 18, 50, 35, 1);
            foreach (var p in new[] { starter, cottage, ranch, craftsman, two, estate }) _all.Add(p.Finish());
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _all = null; }
    }
}
