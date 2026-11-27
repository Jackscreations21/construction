using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public class PlanIssue
    {
        public string PlanId, Check, Message; public bool Error;
        public override string ToString() => $"{(Error ? "ERROR" : "warn")} {PlanId} [{Check}] {Message}";
    }

    /// <summary>Plan checker: rooms tile the footprint, room sizes (IRC R304/R311, ADA), openings inside their walls without overlaps and
    /// under the wall height, exits / egress windows, every room reachable, stairs fit (rise/run), floor + roof spans within limits,
    /// wall heights, roof pitch vs covering, MEP fixtures present and inside their rooms, plumbing fixture counts and sprinklers,
    /// a non-empty takeoff. Errors must be zero for every library plan; warnings are reported.</summary>
    public static class BlueprintValidator
    {
        public static List<PlanIssue> Validate(HousePlan plan)
        {
            var list = new List<PlanIssue>();
            void E(string check, string msg) => list.Add(new PlanIssue { PlanId = plan.Id, Check = check, Message = msg, Error = true });
            void Wn(string check, string msg) => list.Add(new PlanIssue { PlanId = plan.Id, Check = check, Message = msg, Error = false });
            var bp = Blueprints.Info(plan);   // the plan itself, or a Blueprint shadow of a plain HousePlan for the code/MEP checks
            bool own = plan is Blueprint;
            float W = plan.WidthFt, D = plan.DepthFt;

            if (string.IsNullOrEmpty(plan.Id) || string.IsNullOrEmpty(plan.Name)) E("meta", "missing id or name");
            if (string.IsNullOrEmpty(plan.Category)) E("meta", "missing category");
            if (W <= 0 || D <= 0) { E("footprint", "no footprint"); return list; }

            // ---- rooms tile each floor
            for (int f = 0; f < plan.Floors; f++)
            {
                float sum = 0f; var rooms = plan.Rooms.FindAll(r => r.Floor == f);
                if (rooms.Count == 0) { E("rooms", $"floor {f + 1} has no rooms"); continue; }
                foreach (var r in rooms)
                {
                    sum += r.Ft.width * r.Ft.height;
                    if (r.Ft.xMin < -0.01f || r.Ft.yMin < -0.01f || r.Ft.xMax > W + 0.01f || r.Ft.yMax > D + 0.01f) E("rooms", $"{r.Name} (floor {f + 1}) is outside the footprint");
                    if (r.Ft.width < 1.99f || r.Ft.height < 1.99f) E("rooms", $"{r.Name} is too thin ({r.Ft.width}' x {r.Ft.height}')");
                }
                for (int i = 0; i < rooms.Count; i++)
                    for (int j = i + 1; j < rooms.Count; j++)
                    {
                        var a = rooms[i].Ft; var b = rooms[j].Ft;
                        float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                        if (ox > 0.01f && oy > 0.01f) E("rooms", $"{rooms[i].Name} overlaps {rooms[j].Name} on floor {f + 1}");
                    }
                if (Mathf.Abs(sum - W * D) > 0.5f) E("rooms", $"floor {f + 1}: rooms cover {sum:0.#} of {W * D:0.#} sq ft (gap or overlap)");
            }

            // ---- room sizes
            foreach (var r in plan.Rooms)
            {
                var k = Blueprint.KindOf(r.Name); float a = r.Ft.width * r.Ft.height, mn = Mathf.Min(r.Ft.width, r.Ft.height);
                switch (k)
                {
                    case RoomKind.Bedroom: if (a < 70f || mn < 7f) E("room size", $"{r.Name} {r.Ft.width}x{r.Ft.height} < IRC R304 (70 sq ft, 7' min)"); break;
                    case RoomKind.Living: case RoomKind.Dining: case RoomKind.Office: if (mn < 7f) E("room size", $"{r.Name} narrower than 7' (IRC R304.2)"); break;
                    case RoomKind.Bath: if (mn < 5f || a < 35f) E("room size", $"{r.Name} {r.Ft.width}x{r.Ft.height} too small for a full bath (5'x7')"); break;
                    case RoomKind.HalfBath: if (mn < 3f || a < 15f) E("room size", $"{r.Name} too small for a WC + lav"); break;
                    case RoomKind.Restroom: if (mn < 7f || a < 56f) E("room size", $"{r.Name} too small for an ADA single-user restroom (60\" turning circle)"); break;
                    case RoomKind.Hall: if (mn < 3f) E("room size", $"{r.Name} narrower than 36\" (IRC R311.6)"); break;
                    case RoomKind.Corridor: if (mn < 3.67f) E("room size", $"{r.Name} narrower than 44\" (IBC 1020.2)"); break;
                    case RoomKind.Garage: if (mn < 10f || Mathf.Max(r.Ft.width, r.Ft.height) < 20f) E("room size", $"{r.Name} too small for a car (10'x20')"); break;
                    case RoomKind.Stall: if (mn < 10f) E("room size", $"{r.Name} smaller than a 10'x10' box stall"); break;
                }
            }

            // ---- walls
            for (int f = 0; f < plan.Floors; f++)
            {
                int first = plan.Walls.FindIndex(w => w.Floor == f);
                if (first < 0 || first + 3 >= plan.Walls.Count) { E("walls", $"floor {f + 1} missing exterior walls"); continue; }
                for (int s = 0; s < 4; s++) if (!plan.Walls[first + s].Exterior || plan.Walls[first + s].Floor != f) E("walls", $"floor {f + 1} exterior wall {s} out of order");
            }
            foreach (var w in plan.Walls)
            {
                if (w.LengthFt < 0.4f) E("walls", $"zero-length wall at {w.A}");
                foreach (var p in new[] { w.A, w.B }) if (p.x < -0.01f || p.y < -0.01f || p.x > W + 0.01f || p.y > D + 0.01f) E("walls", $"wall end {p} outside footprint");
            }

            // ---- openings
            float wallH = plan.WallHeightFt;
            for (int i = 0; i < plan.Openings.Count; i++)
            {
                var o = plan.Openings[i];
                if (o.Product == null) { E("openings", $"opening {i}: unknown product {o.ProductId}"); continue; }
                if (o.Wall < 0 || o.Wall >= plan.Walls.Count) { E("openings", $"opening {i} on missing wall {o.Wall}"); continue; }
                var w = plan.Walls[o.Wall];
                if (w.Floor != o.Floor) E("openings", $"{o.ProductId} floor {o.Floor} on a wall of floor {w.Floor}");
                float half = o.RoughWidthIn / 24f;
                if (o.CenterFt - half < -0.01f || o.CenterFt + half > w.LengthFt + 0.01f) E("openings", $"{o.ProductId} RO {o.RoughWidthIn:0.#}\" runs past the end of its {w.LengthFt:0.#}' wall");
                for (int j = i + 1; j < plan.Openings.Count; j++)
                {
                    var q = plan.Openings[j]; if (q.Wall != o.Wall) continue;
                    if (Mathf.Abs(q.CenterFt - o.CenterFt) < (q.RoughWidthIn + o.RoughWidthIn) / 24f + 0.24f) E("openings", $"{o.ProductId} and {q.ProductId} overlap on wall {o.Wall}");
                }
                if (o.IsOverhead && !w.Exterior) E("openings", $"{o.ProductId} on an interior wall");
                float floorH = bp != null && bp.MezzanineFt > 0f ? (o.Floor == 0 ? wallH : wallH - bp.MezzanineFt) : wallH;
                float header = o.IsOverhead ? (bp != null && bp.WallFrame != WallSystem.WoodStud ? 0.5f : 1f) : 0.5f;
                float head = o.IsDoor ? o.RoughHeightIn / 12f : (bp != null && !bp.UsesIrc && o.ProductId.Contains("storefront") ? 8.5f : 6.83f);
                if (head + header > floorH + 0.01f) E("openings", $"{o.ProductId} head {head:0.##}' + header {header}' doesn't fit the {floorH}' wall");
                if (o.IsDoor && !o.IsOverhead && w.Exterior && o.Floor > 0 && (bp == null || bp.MezzanineFt <= 0f)) E("openings", $"exterior door {o.ProductId} on an upper floor");
            }

            // ---- exits + egress
            int manDoors = 0, ext = 0;
            foreach (var o in plan.Openings) if (o.IsDoor && plan.Walls[o.Wall].Exterior && o.Floor == 0) { ext++; if (!o.IsOverhead) manDoors++; }
            if (ext == 0) E("exits", "no exterior door");
            if (bp != null)
            {
                bool needsManDoor = bp.Group != PlanGroup.Outbuilding || bp.Rooms.Exists(r => Blueprint.KindOf(r.Name) == RoomKind.Garage && bp.Rooms.Count > 2);
                if (manDoors < bp.ExitsRequired && (bp.Group != PlanGroup.Outbuilding)) E("exits", $"{manDoors} exit door(s), {bp.ExitsRequired} required (occupant load {bp.OccupantLoad})");
                if (bp.Group == PlanGroup.Residential && manDoors < bp.Units) E("exits", $"each dwelling unit needs its own egress door ({manDoors} for {bp.Units} units)");
                if (bp.Group == PlanGroup.Outbuilding && bp.Rooms.Exists(r => Blueprint.KindOf(r.Name) == RoomKind.Garage) && manDoors == 0) Wn("exits", "no walk door (only the overhead door)");
            }
            foreach (var r in plan.Rooms)
            {
                if (Blueprint.KindOf(r.Name) != RoomKind.Bedroom) continue;
                bool egress = false;
                foreach (var o in plan.Openings)
                {
                    if (o.Floor != r.Floor || !plan.Walls[o.Wall].Exterior) continue;
                    var p = plan.Walls[o.Wall].At(o.CenterFt);
                    if (p.x < r.Ft.xMin - 0.01f || p.x > r.Ft.xMax + 0.01f || p.y < r.Ft.yMin - 0.01f || p.y > r.Ft.yMax + 0.01f) continue;
                    if (o.IsDoor || o.ProductId == "win-sh-3660" || o.ProductId == "win-casement-2448" || o.ProductId == "win-dh-3254" && false) egress = true;
                }
                if (!egress) { if (own) E("egress", $"{r.Name} has no emergency escape opening (IRC R310: 5.7 sq ft, 24\" high, 20\" wide)"); else Wn("egress", $"{r.Name}: window is not egress-size (36x48 single-hung opens ~4.8 sq ft)"); }
            }

            // ---- reachability (doors, open-plan connections, stairs)
            var reach = new HashSet<PlanRoom>(); var queue = new Queue<PlanRoom>();
            foreach (var o in plan.Openings)
            {
                if (!o.IsDoor || !plan.Walls[o.Wall].Exterior) continue;
                var w = plan.Walls[o.Wall]; var n = new Vector2(-w.Dir.y, w.Dir.x);
                var p = w.At(o.CenterFt); var r = RoomAt(plan, o.Floor, p + n * 0.2f) ?? RoomAt(plan, o.Floor, p - n * 0.2f);
                if (r != null && reach.Add(r)) queue.Enqueue(r);
            }
            while (queue.Count > 0)
            {
                var r = queue.Dequeue(); var rk = Blueprint.KindOf(r.Name);
                foreach (var o in plan.Openings)
                {
                    if (!o.IsDoor || o.Floor != r.Floor || plan.Walls[o.Wall].Exterior) continue;
                    var w = plan.Walls[o.Wall]; var n = new Vector2(-w.Dir.y, w.Dir.x) * 0.2f; var p = w.At(o.CenterFt);
                    var a = RoomAt(plan, o.Floor, p + n); var b = RoomAt(plan, o.Floor, p - n);
                    var other = a == r ? b : b == r ? a : null;
                    if (other != null && reach.Add(other)) queue.Enqueue(other);
                }
                foreach (var o in plan.Rooms)
                {
                    if (reach.Contains(o)) continue;
                    if (o.Floor == r.Floor && (bp != null ? bp.Connected(r, o) : Blueprint.OpenPair(rk, Blueprint.KindOf(o.Name))) && SharedEdge(r.Ft, o.Ft) >= 3f) { reach.Add(o); queue.Enqueue(o); }
                    else if (rk == RoomKind.Stair && Blueprint.KindOf(o.Name) == RoomKind.Stair && Mathf.Abs(o.Floor - r.Floor) == 1 && o.Ft == r.Ft) { reach.Add(o); queue.Enqueue(o); }
                }
            }
            foreach (var r in plan.Rooms)
            {
                var k = Blueprint.KindOf(r.Name);
                if (k == RoomKind.Void || reach.Contains(r)) continue;
                E("access", $"{r.Name} (floor {r.Floor + 1}) can't be reached through doors/open plan/stairs");
            }

            // ---- stairs
            if (bp != null)
            {
                int full = bp.Stairs.FindAll(s => !s.Name.ToLower().Contains("half") && !s.Name.ToLower().Contains("down")).Count;
                if (plan.Floors > 1 && full == 0 && bp.Stairs.Count == 0) E("stairs", "two floors but no stair");
                foreach (var s in bp.Stairs)
                {
                    float minW = bp.UsesIrc ? 3f : (bp.OccupantLoad > 50 ? 3.67f : 3f);
                    float longSide = Mathf.Max(s.Ft.width, s.Ft.height);
                    if (s.WidthFt < minW - 0.01f) E("stairs", $"{s.Name} {s.WidthFt * 12f:0}\" wide < {minW * 12f:0}\"");
                    if (longSide < s.RunFt + 3f - 0.01f) E("stairs", $"{s.Name}: {s.Risers} risers @ {s.RiserIn:0.##}\" need {s.RunFt + 3f:0.#}' (run {s.RunFt:0.#}' + 3' landing), room is {longSide}'");
                    if (s.RiserIn > (bp.UsesIrc ? 7.75f : 7f) + 0.001f) E("stairs", $"{s.Name} riser {s.RiserIn:0.##}\" too tall");
                    // landings: an open room next to the bottom 3' on this floor, and next to the stairwell on the floor above
                    var stairRoom = bp.Rooms.Find(r => r.Floor == s.Floor && r.Ft == s.Ft);
                    var bottom = BottomZone(s);
                    if (!bp.Rooms.Exists(r => r.Floor == s.Floor && r != stairRoom && Blueprint.KindOf(r.Name) != RoomKind.Void && bp.Connected(r, stairRoom) && Touches(r.Ft, bottom)))
                        E("stairs", $"{s.Name}: nothing open at the bottom landing (floor {s.Floor + 1})");
                    bool upper = s.Split || s.Name.ToLower().Contains("half") || s.Name.ToLower().Contains("down") ? true :
                        bp.Rooms.Exists(r => r.Floor == s.Floor + 1 && r.Ft != s.Ft && Blueprint.KindOf(r.Name) != RoomKind.Void && Blueprint.OpenPair(RoomKind.Stair, Blueprint.KindOf(r.Name)) && Touches(r.Ft, TopZone(s)));
                    if (!upper) E("stairs", $"{s.Name}: no hall/landing at the top (floor {s.Floor + 2})");
                }
            }

            // ---- structure
            if (bp != null)
            {
                bool deck = plan.Floors > 1 || plan.Foundation == FoundationType.Crawlspace || plan.Foundation == FoundationType.Basement;
                if (deck && bp.FloorSpanFt > Blueprint.JoistSpanLimit(bp.FloorJoist) + 0.01f) E("spans", $"{bp.FloorJoist} joists span {bp.FloorSpanFt:0.#}' > {Blueprint.JoistSpanLimit(bp.FloorJoist):0.#}' (add a bearing line or deeper joists)");
                if (bp.RoofSpanFt > bp.RoofSpanLimit + 0.01f) E("spans", $"{bp.RoofFrame} roof spans {bp.RoofSpanFt:0.#}' > {bp.RoofSpanLimit:0.#}'");
                if (plan.WallHeightFt > bp.MaxWallHeightFt + 0.01f) E("walls", $"{plan.WallHeightFt}' {bp.WallFrame} {plan.StudSize} wall > {bp.MaxWallHeightFt}' limit");
                if (plan.Roof != RoofType.Flat && plan.Pitch < bp.MinPitch - 0.01f) E("roof", $"{plan.Pitch}/12 is too flat for {bp.Covering}");
                if (plan.Roof == RoofType.Flat && bp.Covering != RoofCovering.Tpo) E("roof", "flat roof needs a membrane (TPO)");
                if (plan.Roof == RoofType.Flat && bp.RoofFrame != RoofSystem.BarJoist) E("roof", "flat roof needs bar joists");
                if (bp.RoofFrame == RoofSystem.PostFrameTrusses && bp.WallFrame != WallSystem.PostFrame) E("roof", "post-frame trusses need post-frame walls");
                if (bp.WallFrame == WallSystem.PostFrame && plan.Foundation != FoundationType.PostFrame) E("foundation", "post-frame walls sit on embedded posts (FoundationType.PostFrame)");
                if (bp.MezzanineFt > 0f && bp.MezzanineFt > plan.WallHeightFt - 6.5f) E("walls", $"loft at {bp.MezzanineFt}' leaves < 6'6\" under the {plan.WallHeightFt}' eave");
            }

            // ---- MEP
            if (bp != null)
            {
                var fx = bp.Fixtures;
                int Count(string kind) => fx.FindAll(x => x.Kind == kind).Count;
                bool InRoom(PlanRoom r, string kind) => fx.Exists(x => x.Kind == kind && x.Floor == r.Floor && r.Ft.Contains(x.Ft));
                foreach (var x in fx)
                {
                    if (PlanFixture.Outside.Contains(x.Kind)) continue;
                    if (x.Ft.x < -0.01f || x.Ft.y < -0.01f || x.Ft.x > W + 0.01f || x.Ft.y > D + 0.01f) E("mep", $"{x.Kind} at {x.Ft} is outside the building");
                    if (x.Room != null) { var r = bp.Rooms.Find(q => q.Name == x.Room && q.Floor == x.Floor && q.Ft.Contains(x.Ft)); if (r == null) E("mep", $"{x.Kind} is not inside {x.Room}"); }
                }
                foreach (var r in bp.Rooms)
                {
                    var k = Blueprint.KindOf(r.Name);
                    if (k == RoomKind.Bath && (!InRoom(r, "WC") || !InRoom(r, "LAV") || !(InRoom(r, "TUB") || InRoom(r, "SHWR")))) E("mep", $"{r.Name} missing WC/lav/tub");
                    if ((k == RoomKind.HalfBath || k == RoomKind.Restroom) && (!InRoom(r, "WC") || !InRoom(r, "LAV"))) E("mep", $"{r.Name} missing WC/lav");
                    if (k == RoomKind.Kitchen && !InRoom(r, "KS")) E("mep", $"{r.Name} missing sink");
                    if (k == RoomKind.Laundry && r.Name.Contains("Laundry") && !InRoom(r, "WB")) E("mep", $"{r.Name} missing washer box");
                    if (bp.IsResidential && Blueprint.Habitable(k) && !InRoom(r, "SUP")) E("mep", $"{r.Name} has no supply register");
                }
                if (bp.IsResidential)
                {
                    if (Count("WH") < bp.Units) E("mep", "water heater missing");
                    if (Count("FURN") + Count("AH") < bp.Units) E("mep", "furnace / air handler missing");
                    if (Count("PNL") < bp.Units) E("mep", "electrical panel missing");
                    if (Count("SUP") == 0 || Count("RET") == 0) E("mep", "no registers/returns");
                }
                else if (bp.Group != PlanGroup.Outbuilding)
                {
                    if (Count("PNL") == 0) E("mep", "electrical panel missing");
                    if (Count("WC") < bp.WaterClosetsRequired) E("plumbing count", $"{Count("WC")} WCs < {bp.WaterClosetsRequired} required (IPC 2902.1, occupant load {bp.OccupantLoad})");
                    if (Count("LAV") < bp.LavatoriesRequired) E("plumbing count", $"{Count("LAV")} lavs < {bp.LavatoriesRequired} required");
                    if (bp.ServiceSinkRequired && Count("MS") == 0) E("plumbing count", "service (mop) sink required");
                    if (Count("DF") < bp.DrinkingFountainsRequired) E("plumbing count", "drinking fountain required");
                    if (bp.ConditionedSqFt > 0 && Count("RTU") + Count("FURN") == 0) E("mep", "conditioned space without HVAC");
                    if (fx.Exists(x => x.Kind == "WC" || x.Kind == "LAV") && Count("WH") == 0) E("mep", "fixtures without a water heater");
                    if (bp.Rooms.Exists(r => Blueprint.KindOf(r.Name) == RoomKind.CommKitchen) && (Count("HOOD") == 0 || Count("GI") == 0 || Count("3CS") == 0 || Count("HS") == 0)) E("mep", "commercial kitchen needs hood, grease interceptor, 3-comp + hand sinks");
                    if (bp.SprinklersRequired && Count("RISER") == 0) E("fire", "sprinklers required (IBC 903.2) but no riser");
                }
                else if (bp.Rooms.Exists(r => Blueprint.KindOf(r.Name) != RoomKind.Shed) && Count("PNL") == 0) E("mep", "outbuilding without a subpanel");
            }

            // ---- takeoff
            var t = plan.Takeoff;
            if (t.Lines.Count < 8) E("takeoff", $"only {t.Lines.Count} takeoff lines");
            if (t.MaterialTotal <= 0 || t.Total <= 0) E("takeoff", "no cost");
            foreach (var l in t.Lines) if (l.Qty <= 0 || l.UnitCost < 0 || float.IsNaN(l.Qty)) E("takeoff", $"bad line {l.Item} {l.Qty} @ {l.UnitCost}");
            if (plan.Foundation != FoundationType.Skids && t.ConcreteYards <= 0f) E("takeoff", "no concrete");
            if (bp != null && bp.WallFrame == WallSystem.WoodStud && t.Studs <= 0) E("takeoff", "wood-frame plan with no studs");
            if (bp != null && bp.SprinklersRequired && !t.Lines.Exists(l => l.Item.ToLower().Contains("sprinkler"))) E("takeoff", "sprinklers missing from the takeoff");
            if (!t.Lines.Exists(l => l.Phase == BuildPhase.Framing)) E("takeoff", "no framing/shell lines");

            // ---- metadata consistency
            if (bp != null && bp.IsResidential)
            {
                int beds = 0, full = 0, half = 0;
                foreach (var r in plan.Rooms) { var k = Blueprint.KindOf(r.Name); if (k == RoomKind.Bedroom) beds++; if (k == RoomKind.Bath) full++; if (k == RoomKind.HalfBath) half++; }
                if (beds != plan.Beds * bp.Units) E("meta", $"Beds = {plan.Beds} x {bp.Units} units but {beds} bedrooms drawn");
                if (full * 2 + half != plan.Baths2 * bp.Units) E("meta", $"Baths2 = {plan.Baths2} x {bp.Units} units but {full} full + {half} half baths drawn");
            }
            return list;
        }

        /// <summary>The bottom 3' of a flight (where you step on), as a rect.</summary>
        public static Rect BottomZone(PlanStair s)
        {
            var f = s.Ft;
            if (s.AlongY) return s.Reverse ? Rect.MinMaxRect(f.xMin, f.yMax - 3f, f.xMax, f.yMax) : Rect.MinMaxRect(f.xMin, f.yMin, f.xMax, f.yMin + 3f);
            return s.Reverse ? Rect.MinMaxRect(f.xMax - 3f, f.yMin, f.xMax, f.yMax) : Rect.MinMaxRect(f.xMin, f.yMin, f.xMin + 3f, f.yMax);
        }
        /// <summary>The top 3' of the stairwell (where you step off upstairs).</summary>
        public static Rect TopZone(PlanStair s)
        {
            var f = s.Ft;
            if (s.AlongY) return s.Reverse ? Rect.MinMaxRect(f.xMin, f.yMin, f.xMax, f.yMin + 3f) : Rect.MinMaxRect(f.xMin, f.yMax - 3f, f.xMax, f.yMax);
            return s.Reverse ? Rect.MinMaxRect(f.xMin, f.yMin, f.xMin + 3f, f.yMax) : Rect.MinMaxRect(f.xMax - 3f, f.yMin, f.xMax, f.yMax);
        }
        /// <summary>True when rect a shares an edge segment (≥ 2.5') with rect zone.</summary>
        static bool Touches(Rect a, Rect z)
        {
            float ox = Mathf.Min(a.xMax, z.xMax) - Mathf.Max(a.xMin, z.xMin), oy = Mathf.Min(a.yMax, z.yMax) - Mathf.Max(a.yMin, z.yMin);
            bool vert = (Mathf.Abs(a.xMax - z.xMin) < 0.01f || Mathf.Abs(a.xMin - z.xMax) < 0.01f) && oy >= 2.5f;
            bool hor = (Mathf.Abs(a.yMax - z.yMin) < 0.01f || Mathf.Abs(a.yMin - z.yMax) < 0.01f) && ox >= 2.5f;
            return vert || hor;
        }

        static PlanRoom RoomAt(HousePlan p, int floor, Vector2 pt)
        {
            foreach (var r in p.Rooms) if (r.Floor == floor && pt.x > r.Ft.xMin && pt.x < r.Ft.xMax && pt.y > r.Ft.yMin && pt.y < r.Ft.yMax) return r;
            return null;
        }

        static float SharedEdge(Rect a, Rect b)
        {
            if (Mathf.Abs(a.xMax - b.xMin) < 0.01f || Mathf.Abs(a.xMin - b.xMax) < 0.01f) return Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            if (Mathf.Abs(a.yMax - b.yMin) < 0.01f || Mathf.Abs(a.yMin - b.yMax) < 0.01f) return Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            return 0f;
        }

        /// <summary>Errors only, as text lines (empty list = plan passes).</summary>
        public static List<string> Errors(HousePlan p) { var l = new List<string>(); foreach (var i in Validate(p)) if (i.Error) l.Add(i.ToString()); return l; }
    }
}
