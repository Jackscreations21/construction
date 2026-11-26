using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>MEP fixture layout by room rules: baths, kitchens, laundry, water heater, furnace/air handler, panel + meter, condenser,
    /// supply registers / returns; commercial restrooms, mop sink, RTUs, diffusers, hood, grease interceptor; shop unit heaters, sprinkler riser.
    /// Positions are plan feet on the room's floor; fixtures sit against the walls the way a plumber/HVAC tech would rough them in.</summary>
    public partial class Blueprint
    {
        // edge 0 = front (yMin), 1 = right (xMax), 2 = back (yMax), 3 = left (xMin); t measured from xMin (edges 0/2) or yMin (edges 1/3)
        public static Vector2 OnEdge(Rect r, int e, float t, float inset)
        {
            switch (e)
            {
                case 0: return new Vector2(r.xMin + t, r.yMin + inset);
                case 1: return new Vector2(r.xMax - inset, r.yMin + t);
                case 2: return new Vector2(r.xMin + t, r.yMax - inset);
                default: return new Vector2(r.xMin + inset, r.yMin + t);
            }
        }
        public static float EdgeLen(Rect r, int e) => e % 2 == 0 ? r.width : r.height;

        /// <summary>True when a door (interior or exterior) sits on that edge of the room.</summary>
        public bool EdgeHasDoor(PlanRoom r, int e)
        {
            foreach (var o in Openings)
            {
                if (!o.IsDoor || o.Floor != r.Floor) continue;
                var w = Walls[o.Wall]; var p = w.At(o.CenterFt);
                bool on = e == 0 ? Mathf.Abs(p.y - r.Ft.yMin) < 0.05f && p.x > r.Ft.xMin - 0.01f && p.x < r.Ft.xMax + 0.01f
                        : e == 2 ? Mathf.Abs(p.y - r.Ft.yMax) < 0.05f && p.x > r.Ft.xMin - 0.01f && p.x < r.Ft.xMax + 0.01f
                        : e == 1 ? Mathf.Abs(p.x - r.Ft.xMax) < 0.05f && p.y > r.Ft.yMin - 0.01f && p.y < r.Ft.yMax + 0.01f
                                 : Mathf.Abs(p.x - r.Ft.xMin) < 0.05f && p.y > r.Ft.yMin - 0.01f && p.y < r.Ft.yMax + 0.01f;
                if (on) return true;
            }
            return false;
        }

        bool EdgeExterior(PlanRoom r, int e) => e == 0 ? r.Ft.yMin < 0.01f : e == 1 ? r.Ft.xMax > WidthFt - 0.01f : e == 2 ? r.Ft.yMax > DepthFt - 0.01f : r.Ft.xMin < 0.01f;

        /// <summary>A window center on that exterior edge of the room, if any (plan point).</summary>
        Vector2? WindowOnEdge(PlanRoom r, int e)
        {
            foreach (var o in Openings)
            {
                if (o.IsDoor || o.Floor != r.Floor || !Walls[o.Wall].Exterior) continue;
                var p = Walls[o.Wall].At(o.CenterFt);
                if (r.Ft.Contains(OnEdge(r.Ft, e, 0, 0)) || true)
                {
                    bool on = e == 0 ? p.y < 0.05f && p.x > r.Ft.xMin && p.x < r.Ft.xMax : e == 2 ? p.y > DepthFt - 0.05f && p.x > r.Ft.xMin && p.x < r.Ft.xMax
                            : e == 1 ? p.x > WidthFt - 0.05f && p.y > r.Ft.yMin && p.y < r.Ft.yMax : p.x < 0.05f && p.y > r.Ft.yMin && p.y < r.Ft.yMax;
                    if (on && EdgeExterior(r, e)) return p;
                }
            }
            return null;
        }

        int BestEdge(PlanRoom r, bool longEdge, bool preferExterior, int avoid = -1)
        {
            int best = -1; float bestScore = float.MinValue;
            for (int e = 0; e < 4; e++)
            {
                if (e == avoid) continue;
                float len = EdgeLen(r.Ft, e);
                bool isLong = len >= EdgeLen(r.Ft, (e + 1) % 4) - 0.01f;
                float s = len * 0.01f;
                if (longEdge == isLong) s += 100;
                if (!EdgeHasDoor(r, e)) s += 50;
                if (preferExterior && EdgeExterior(r, e)) s += 20;
                if (s > bestScore) { bestScore = s; best = e; }
            }
            return best;
        }

        void Fx(string kind, string trade, PlanRoom r, Vector2 p, string note = null, int floor = -1) =>
            Fixtures.Add(new PlanFixture { Kind = kind, Trade = trade, Room = r?.Name, Floor = floor >= 0 ? floor : r != null ? r.Floor : 0, Ft = p, Note = note });

        const string Pl = "Plumbing", El = "Electrical", Hv = "HVAC", Fi = "Fire";

        void BuildFixtures()
        {
            Fixtures.Clear();
            foreach (var r in Rooms) RoomFixtures(r);
            if (IsResidential) for (int u = 0; u < Units; u++) DwellingMechanicals(u);
            else BuildingMechanicals();
            foreach (var e in _extraFixtures)
                Fx(e.kind, TradeOf(e.kind), RoomAt(e.floor, new Vector2(e.x, e.y)), new Vector2(e.x, e.y), e.note, e.floor);
        }

        public static string TradeOf(string kind) =>
            kind == "PNL" || kind == "MTR" ? El : kind == "RISER" ? Fi :
            kind == "FURN" || kind == "AH" || kind == "AC" || kind == "HP" || kind == "SUP" || kind == "RET" || kind == "RTU" || kind == "UH" || kind == "EF" || kind == "HOOD" || kind == "CU" || kind == "MINI" ? Hv : Pl;

        void RoomFixtures(PlanRoom r)
        {
            var k = KindOf(r.Name); var f = r.Ft;
            switch (k)
            {
                case RoomKind.Bath:
                {
                    bool master = Has(r.Name, "Master") && Mathf.Max(f.width, f.height) >= 10f;
                    int s = BestEdge(r, false, false);              // tub across a short wall without a door
                    int l = BestEdge(r, true, false);               // WC + lav along a long wall
                    float ll = EdgeLen(f, l);
                    bool sAtStart = (l % 2 == 0) ? s == 3 : s == 0; // short edge at the long edge's t = 0 end?
                    float T(float d) => sAtStart ? d : ll - d;
                    Fx("TUB", Pl, r, OnEdge(f, s, EdgeLen(f, s) / 2f, 1.25f), master ? "60\" soaker" : "60\" tub/shower");
                    Fx("WC", Pl, r, OnEdge(f, l, T(3.75f), 1.1f));
                    Fx("LAV", Pl, r, OnEdge(f, l, T(Mathf.Min(6.5f, ll - 1.6f)), 0.9f), master ? "double vanity" : null);
                    if (master) Fx("SHWR", Pl, r, OnEdge(f, (s + 2) % 4, EdgeLen(f, s) / 2f, 1.75f), "42x60 shower");
                    Fx("EF", Hv, r, f.center, "bath fan 80 cfm");
                    break;
                }
                case RoomKind.HalfBath:
                {
                    int l = BestEdge(r, true, false); float ll = EdgeLen(f, l);
                    Fx("WC", Pl, r, OnEdge(f, l, 1.5f, 1.1f));
                    if (ll >= 5f) Fx("LAV", Pl, r, OnEdge(f, l, Mathf.Min(4f, ll - 1.2f), 0.9f));
                    else Fx("LAV", Pl, r, OnEdge(f, (l + 2) % 4, ll / 2f, 0.9f));
                    Fx("EF", Hv, r, f.center, "fan 50 cfm");
                    break;
                }
                case RoomKind.Restroom:
                {
                    int l = BestEdge(r, true, false); float ll = EdgeLen(f, l);
                    Fx("WC", Pl, r, OnEdge(f, l, 1.5f, 1.2f), "ADA, grab bars 36\"+42\"");
                    Fx("LAV", Pl, r, OnEdge(f, l, Mathf.Max(4.5f, ll - 2f), 0.9f), "ADA wall-hung");
                    if (Group != PlanGroup.Outbuilding) Fx("FD", Pl, r, f.center + new Vector2(0.5f, 0.5f));
                    Fx("EF", Hv, r, f.center, "75 cfm");
                    break;
                }
                case RoomKind.Kitchen:
                {
                    int se = -1; Vector2? wp = null;
                    for (int e = 0; e < 4 && wp == null; e++) { wp = WindowOnEdge(r, e); if (wp != null) se = e; }
                    Vector2 sink;
                    if (wp != null) sink = Inset(wp.Value, se, 1.1f);
                    else { se = BestEdge(r, true, false); sink = OnEdge(f, se, EdgeLen(f, se) / 2f, 1.1f); }
                    Fx("KS", Pl, r, sink, "double bowl");
                    var along = se % 2 == 0 ? Vector2.right : Vector2.up;
                    var dw = sink + along * 2.25f; if (!f.Contains(dw)) dw = sink - along * 2.25f;
                    Fx("DW", Pl, r, dw);
                    int re = BestEdge(r, false, false, se); if (re < 0) re = (se + 1) % 4;
                    Fx("RNG", Pl, r, OnEdge(f, re, EdgeLen(f, re) / 2f, 1.1f), "gas range + hood");
                    Fx("REF", Pl, r, OnEdge(f, re, Mathf.Min(1.6f, EdgeLen(f, re) / 3f), 1.4f), "ice maker line");
                    break;
                }
                case RoomKind.Laundry:
                {
                    int e = BestEdge(r, true, true);
                    if (Has(r.Name, "Mud") && !Has(r.Name, "Laundry")) { Fx("SUP", Hv, r, f.center); break; }
                    Fx("WB", Pl, r, OnEdge(f, e, EdgeLen(f, e) / 2f, 1.2f), "washer box + dryer vent");
                    break;
                }
                case RoomKind.CommKitchen:
                {
                    int l = BestEdge(r, true, false); float ll = EdgeLen(f, l);
                    if (Starts(r.Name, "Cook line"))
                    {
                        Fx("HOOD", Hv, r, OnEdge(f, l, ll / 2f, 1.8f), $"Type I hood {Mathf.Min(ll - 2f, 14f):0}' + Ansul");
                        Fx("RNG", Pl, r, OnEdge(f, l, ll / 2f, 1.6f), "griddle/range/fryers (gas)");
                        Fx("HS", Pl, r, OnEdge(f, l, 1.2f, 1f), "hand sink");
                    }
                    else if (Starts(r.Name, "Dish"))
                    { Fx("DW", Pl, r, OnEdge(f, l, ll / 2f, 1.3f), "commercial dish machine"); Fx("3CS", Pl, r, OnEdge(f, l, Mathf.Max(1.5f, ll / 2f - 4f), 1.3f), "3-comp sink"); }
                    else
                    { Fx("3CS", Pl, r, OnEdge(f, l, ll / 2f, 1.3f), "3-comp sink"); Fx("HS", Pl, r, OnEdge(f, l, 1.2f, 1f), "hand sink"); Fx("KS", Pl, r, OnEdge(f, (l + 2) % 4, EdgeLen(f, l) / 2f, 1.2f), "prep sink (indirect)"); }
                    Fx("FD", Pl, r, f.center, "floor sink");
                    break;
                }
                case RoomKind.Janitor: Fx("MS", Pl, r, OnEdge(f, BestEdge(r, false, false), 1.3f, 1.2f), "mop sink"); break;
                case RoomKind.BreakRoom:
                {
                    int e = BestEdge(r, true, false);
                    Fx("KS", Pl, r, OnEdge(f, e, EdgeLen(f, e) / 2f, 1.1f), "break sink"); Fx("REF", Pl, r, OnEdge(f, e, 1.5f, 1.4f));
                    break;
                }
                case RoomKind.Cooler: Fx("CU", Hv, r, f.center, "condensing unit on roof"); break;
                case RoomKind.Stall: break;
            }
        }

        static Vector2 Inset(Vector2 p, int e, float d) => e == 0 ? p + new Vector2(0, d) : e == 1 ? p - new Vector2(d, 0) : e == 2 ? p - new Vector2(0, d) : p + new Vector2(d, 0);

        PlanRoom FirstRoom(System.Func<PlanRoom, bool> pred) { foreach (var r in Rooms) if (pred(r)) return r; return null; }
        PlanRoom Largest(System.Func<PlanRoom, bool> pred) { PlanRoom b = null; foreach (var r in Rooms) if (pred(r) && (b == null || r.Ft.width * r.Ft.height > b.Ft.width * b.Ft.height)) b = r; return b; }

        /// <summary>Water heater, furnace/air handler, condenser, panel + meter, registers and returns for dwelling unit u (units side by side along x).</summary>
        void DwellingMechanicals(int u)
        {
            float uw = WidthFt / Units, x0 = u * uw, x1 = x0 + uw;
            bool In(PlanRoom r) => r.Ft.center.x > x0 && r.Ft.center.x < x1;
            var mech = FirstRoom(r => In(r) && KindOf(r.Name) == RoomKind.Mech);
            var garage = FirstRoom(r => In(r) && KindOf(r.Name) == RoomKind.Garage);
            var laundry = FirstRoom(r => In(r) && KindOf(r.Name) == RoomKind.Laundry && !Starts(r.Name, "Mud"));
            var hall = Largest(r => In(r) && r.Floor == 0 && KindOf(r.Name) == RoomKind.Hall);
            var mid = new Vector2((x0 + x1) / 2f, DepthFt / 2f);
            if (Foundation == FoundationType.Basement)
            {
                Fx("FURN", Hv, null, mid + new Vector2(-1.5f, 2f), "96% gas, basement", 0);
                Fx("WH", Pl, null, mid + new Vector2(1.5f, 2f), "50 gal gas, basement", 0);
            }
            else if (mech != null) { Fx("FURN", Hv, mech, mech.Ft.center + new Vector2(-0.8f, 0), "96% gas"); Fx("WH", Pl, mech, mech.Ft.center + new Vector2(0.9f, 0), "50 gal gas"); }
            else if (garage != null)
            {
                int e = BestEdge(garage, false, false, 0);
                Fx("FURN", Hv, garage, OnEdge(garage.Ft, e, 2f, 1.5f), "on 18\" platform");
                Fx("WH", Pl, garage, OnEdge(garage.Ft, e, 4.5f, 1.5f), "50 gal gas, 18\" platform + bollard");
            }
            else if (Foundation == FoundationType.Crawlspace)
            {
                Fx("FURN", Hv, null, mid, "horizontal furnace in crawlspace", 0);
                var wr = laundry ?? hall ?? Largest(r => In(r) && r.Floor == 0);
                Fx("WH", Pl, wr, wr.Ft.center + new Vector2(0.8f, 0.8f), laundry != null ? "50 gal elec" : "tankless gas");
            }
            else
            {
                var ah = hall ?? laundry ?? Largest(r => In(r) && r.Floor == 0);
                Fx("AH", Hv, ah, ah.Ft.center, "air handler (closet) / attic");
                var wr = laundry ?? ah;
                Fx("WH", Pl, wr, wr.Ft.center + new Vector2(0.9f, 0.6f), "50 gal elec");
            }
            Fx(Foundation == FoundationType.Slab && garage == null && mech == null ? "HP" : "AC", Hv, null, new Vector2(Mathf.Clamp((x0 + x1) / 2f + uw / 4f, 2f, WidthFt - 2f), DepthFt + 3f), "3-ton condenser on pad");
            // panel + meter: garage wall next to the house, else mech/laundry, else an exterior hall/kitchen wall
            var pr = garage ?? mech ?? laundry ?? hall ?? Largest(r => In(r) && r.Floor == 0 && KindOf(r.Name) == RoomKind.Kitchen) ?? Largest(r => In(r) && r.Floor == 0);
            int pe = -1; for (int e = 0; e < 4; e++) if (EdgeExterior(pr, e) && e != 0) { pe = e; break; }
            if (pe < 0) pe = BestEdge(pr, false, false);
            var pp = OnEdge(pr.Ft, pe, Mathf.Min(2f, EdgeLen(pr.Ft, pe) / 2f), 0.4f);
            Fx("PNL", El, pr, pp, "200A main breaker panel");
            if (EdgeExterior(pr, pe)) Fx("MTR", El, null, Inset(pp, pe, -1.4f), "meter base", 0);
            Fx("HB", Pl, null, new Vector2(Mathf.Clamp(x0 + 2f, 1f, WidthFt - 1f), -1f), "hose bibb", 0);
            Fx("HB", Pl, null, new Vector2(Mathf.Clamp(x1 - 2f, 1f, WidthFt - 1f), DepthFt + 1f), "hose bibb", 0);
            // registers: supply per conditioned room (1 per ~150 sq ft, under windows), returns per floor
            foreach (var r in Rooms)
            {
                if (!In(r)) continue;
                var k = KindOf(r.Name);
                if (!(Habitable(k) || k == RoomKind.Bath || k == RoomKind.HalfBath || k == RoomKind.Laundry || (k == RoomKind.Hall && r.Ft.width * r.Ft.height > 80f))) continue;
                if (k == RoomKind.Laundry && Starts(r.Name, "Mud")) continue;
                Registers(r, 150f);
            }
            for (int fl = 0; fl < Floors; fl++)
            {
                var rr = Largest(r => In(r) && r.Floor == fl && KindOf(r.Name) == RoomKind.Hall) ?? Largest(r => In(r) && r.Floor == fl && (KindOf(r.Name) == RoomKind.Living || KindOf(r.Name) == RoomKind.Flex || KindOf(r.Name) == RoomKind.Bedroom));
                if (rr != null) Fx("RET", Hv, rr, rr.Ft.center + new Vector2(0, 0.6f), "20x25 return");
            }
        }

        void Registers(PlanRoom r, float sfPer)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(r.Ft.width * r.Ft.height / sfPer));
            int e = -1; for (int i = 0; i < 4 && e < 0; i++) if (EdgeExterior(r, (i + 2) % 4)) e = (i + 2) % 4;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = e >= 0 ? OnEdge(r.Ft, e, EdgeLen(r.Ft, e) * (i + 0.5f) / n, 1f)
                                   : new Vector2(r.Ft.xMin + r.Ft.width * (i + 0.5f) / n, r.Ft.center.y);
                Fx("SUP", Hv, r, p, IsResidential ? "4x12 floor/ceiling register" : "24x24 diffuser");
            }
        }

        public float ConditionedSqFt
        {
            get
            {
                float s = 0; foreach (var r in Rooms) if (IsConditionedRoom(r)) s += r.Ft.width * r.Ft.height; return s;
            }
        }

        public bool IsConditionedRoom(PlanRoom r)
        {
            var k = KindOf(r.Name);
            if (!Conditioned || k == RoomKind.Void || k == RoomKind.Cooler || k == RoomKind.Stall || k == RoomKind.Barn || k == RoomKind.Loft || k == RoomKind.Shed) return false;
            if (k == RoomKind.Warehouse || k == RoomKind.Shop || k == RoomKind.Garage || k == RoomKind.Storage && Group == PlanGroup.Industrial) return false;
            if (k == RoomKind.Stair && r.Floor > 0) return false;
            return true;
        }

        static float SfPerTon(RoomKind k) => k == RoomKind.CommKitchen ? 150f : k == RoomKind.Dining ? 200f : k == RoomKind.Sales ? 300f : 375f;

        /// <summary>Commercial/industrial/outbuilding mechanicals: panel, water heater, RTUs + diffusers, unit heaters, sprinkler riser, hose bibbs.</summary>
        void BuildingMechanicals()
        {
            bool outb = Group == PlanGroup.Outbuilding;
            var mech = FirstRoom(r => KindOf(r.Name) == RoomKind.Mech);
            var back = mech ?? Largest(r => KindOf(r.Name) == RoomKind.Storage) ?? Largest(r => KindOf(r.Name) == RoomKind.Warehouse || KindOf(r.Name) == RoomKind.Shop)
                       ?? Largest(r => KindOf(r.Name) == RoomKind.Garage || KindOf(r.Name) == RoomKind.Barn) ?? Largest(r => r.Floor == 0 && KindOf(r.Name) != RoomKind.Void);
            int area = Mathf.RoundToInt(SqFt);
            string service = outb ? (KindOf(back.Name) == RoomKind.Shed ? "30A sub (optional)" : area > 1500 ? "100A subpanel" : "60A subpanel")
                           : area > 12000 ? "800A 480Y/277V 3-phase" : area > 4000 ? "400A 208Y/120V 3-phase" : "200A 208Y/120V 3-phase";
            int pe = -1; for (int e = 1; e < 4 && pe < 0; e++) if (EdgeExterior(back, e)) pe = e;
            if (pe < 0) pe = BestEdge(back, false, false);
            var pp = OnEdge(back.Ft, pe, Mathf.Min(2.5f, EdgeLen(back.Ft, pe) / 2f), 0.4f);
            if (!(outb && KindOf(back.Name) == RoomKind.Shed))
            {
                Fx("PNL", El, back, pp, service);
                if (!outb && EdgeExterior(back, pe)) Fx("MTR", El, null, Inset(pp, pe, -1.4f), "CT cabinet + meter", 0);
            }
            // water heater where there are fixtures
            bool anyLav = Rooms.Exists(r => KindOf(r.Name) == RoomKind.Restroom || KindOf(r.Name) == RoomKind.Bath || KindOf(r.Name) == RoomKind.HalfBath || KindOf(r.Name) == RoomKind.CommKitchen || KindOf(r.Name) == RoomKind.BreakRoom);
            if (anyLav)
            {
                bool diner = Rooms.Exists(r => KindOf(r.Name) == RoomKind.CommKitchen);
                var wr = mech ?? FirstRoom(r => KindOf(r.Name) == RoomKind.Janitor) ?? Largest(r => KindOf(r.Name) == RoomKind.Storage) ?? Largest(r => KindOf(r.Name) == RoomKind.Warehouse || KindOf(r.Name) == RoomKind.Shop || KindOf(r.Name) == RoomKind.Garage)
                         ?? FirstRoom(r => KindOf(r.Name) == RoomKind.Restroom || KindOf(r.Name) == RoomKind.Bath || KindOf(r.Name) == RoomKind.HalfBath);
                Fx("WH", Pl, wr, wr.Ft.center + new Vector2(1f, 0.8f), diner ? "100 gal gas high-recovery" : area > 6000 ? "50 gal gas" : "20 gal elec");
                if (diner) Fx("GI", Pl, null, new Vector2(WidthFt * 0.75f, DepthFt + 6f), "1,000 gal grease interceptor", 0);
            }
            // cooling/heating
            float cond = 0f, tons = 0f; var condRooms = new List<PlanRoom>();
            foreach (var r in Rooms) if (IsConditionedRoom(r)) { float a = r.Ft.width * r.Ft.height; cond += a; tons += a / SfPerTon(KindOf(r.Name)); condRooms.Add(r); }
            if (cond > 0 && !outb)
            {
                int units = Mathf.Max(1, Mathf.CeilToInt(tons / 10f));
                condRooms.Sort((p, q) => (q.Ft.width * q.Ft.height).CompareTo(p.Ft.width * p.Ft.height));
                for (int i = 0; i < units; i++) { var r = condRooms[i % condRooms.Count]; Fx("RTU", Hv, r, r.Ft.center + new Vector2((i / condRooms.Count) * 4f, 0), $"{Mathf.Ceil(tons / units * 2f) / 2f:0.#}-ton gas/elec RTU on curb"); }
                foreach (var r in condRooms) { if (KindOf(r.Name) == RoomKind.Restroom || KindOf(r.Name) == RoomKind.Janitor || KindOf(r.Name) == RoomKind.Mech || KindOf(r.Name) == RoomKind.Closet) continue; Registers(r, 200f); }
                foreach (var r in condRooms) if (r.Ft.width * r.Ft.height >= 400f) Fx("RET", Hv, r, r.Ft.center + new Vector2(2f, 2f), "24x24 return grille");
            }
            else if (cond > 0 && outb)
            {
                var r = Largest(IsConditionedRoom);
                Fx("MINI", Hv, r, OnEdge(r.Ft, BestEdge(r, true, true), EdgeLen(r.Ft, BestEdge(r, true, true)) / 2f, 0.6f), "ductless mini-split heat pump");
                Fx("HP", Hv, null, new Vector2(WidthFt / 2f, DepthFt + 3f), "mini-split condenser", 0);
            }
            // unit heaters in shops / warehouses / heated garages
            foreach (var r in Rooms)
            {
                var k = KindOf(r.Name);
                bool heated = k == RoomKind.Warehouse || (k == RoomKind.Shop && Conditioned) || (k == RoomKind.Garage && outb && Conditioned);
                if (!heated) continue;
                float a = r.Ft.width * r.Ft.height; int n = Mathf.Max(1, Mathf.RoundToInt(a / (k == RoomKind.Warehouse ? 2500f : 1200f)));
                int cols = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(n * r.Ft.width / Mathf.Max(1f, r.Ft.height)))); int rows = Mathf.CeilToInt(n / (float)cols);
                for (int i = 0; i < n; i++)
                    Fx("UH", Hv, r, new Vector2(r.Ft.xMin + r.Ft.width * ((i % cols) + 0.5f) / cols, r.Ft.yMin + r.Ft.height * ((i / cols) + 0.5f) / rows), k == RoomKind.Warehouse ? "gas unit heater 150 MBH" : "gas unit heater 60 MBH");
                if (k == RoomKind.Shop && !outb) Fx("FD", Pl, r, r.Ft.center, "trench drain → oil/sand separator");
            }
            // barns: frost-free hydrant in the aisle
            foreach (var r in Rooms) if (KindOf(r.Name) == RoomKind.Barn && Has(r.Name, "Aisle")) Fx("HYD", Pl, r, r.Ft.center, "frost-free hydrant");
            // fire sprinklers + drinking fountain + hose bibbs
            if (SprinklersRequired) { var rr = mech ?? back; Fx("RISER", Fi, rr, OnEdge(rr.Ft, 0, 1.5f, 1.5f) + new Vector2(0, 0), "6\" sprinkler riser + FDC"); }
            if (!outb && DrinkingFountainsRequired > 0) { var c = FirstRoom(r => KindOf(r.Name) == RoomKind.Corridor) ?? Largest(r => KindOf(r.Name) == RoomKind.Sales || KindOf(r.Name) == RoomKind.Lobby || KindOf(r.Name) == RoomKind.BreakRoom || KindOf(r.Name) == RoomKind.Warehouse); if (c != null) Fx("DF", Pl, c, OnEdge(c.Ft, BestEdge(c, false, false), 1.5f, 0.8f), "hi-lo drinking fountain"); }
            if (!outb || anyLav) { Fx("HB", Pl, null, new Vector2(2f, -1f), "wall hydrant", 0); Fx("HB", Pl, null, new Vector2(WidthFt - 2f, DepthFt + 1f), "wall hydrant", 0); }
        }
    }
}
