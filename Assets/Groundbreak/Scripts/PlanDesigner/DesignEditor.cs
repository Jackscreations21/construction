using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A finding shown while drawing: plain-language text, error or tip, and where on the drawing to highlight it.</summary>
    public class DesignIssue
    {
        public string Text, Check; public bool Error; public int Floor = -1; public Rect? Area;
        public override string ToString() => (Error ? "PROBLEM " : "tip ") + Text;
    }

    /// <summary>Edits one <see cref="PlanDesign"/>: footprint, wall lines (split / move / erase), room boxes, names, doors, windows,
    /// stairs, floors, building systems. Every change is undoable. The finished plan (<see cref="Plan"/>), the validator's findings in
    /// plain language (<see cref="Issues"/>) and the numbers (sq ft, beds/baths, cost) are rebuilt after each change.
    /// All positions are plan feet; everything snaps to <see cref="SnapFt"/> (Game.Snap's grid, at least 6").</summary>
    public class DesignEditor
    {
        public PlanDesign Design { get; private set; }
        public string Message = "";
        readonly List<string> _undo = new List<string>(), _redo = new List<string>();
        CustomPlan _plan; List<DesignIssue> _issues; bool _dirty = true;

        public const float MinRoomFt = 2f, MaxSizeFt = 300f, MinSizeFt = 8f;

        public DesignEditor(PlanDesign d) { Design = d ?? NewBlank(PlanGroup.Residential); Design.Fix(); }

        // ------------------------------------------------------------------ starting points

        /// <summary>A blank lot: one room filling a footprint of a sensible size for the group, with an entry door.</summary>
        public static PlanDesign NewBlank(PlanGroup g, float w = 0f, float d = 0f)
        {
            var p = new PlanDesign(); p.ApplyGroupDefaults(g);
            if (w <= 0f) { w = g == PlanGroup.Residential ? 40f : g == PlanGroup.Outbuilding ? 24f : g == PlanGroup.Commercial ? 50f : 60f; }
            if (d <= 0f) { d = g == PlanGroup.Residential ? 30f : g == PlanGroup.Outbuilding ? 24f : g == PlanGroup.Commercial ? 60f : 80f; }
            p.width = w; p.depth = d;
            p.name = g == PlanGroup.Residential ? "My house" : g == PlanGroup.Outbuilding ? "My garage" : g == PlanGroup.Commercial ? "My store" : "My shop building";
            string first = g == PlanGroup.Residential ? "Living" : g == PlanGroup.Outbuilding ? "Garage" : g == PlanGroup.Commercial ? "Sales floor" : "Warehouse";
            p.rooms.Add(new DesignRoom { name = first, floor = 0, x0 = 0, y0 = 0, x1 = w, y1 = d });
            string door = g == PlanGroup.Residential ? "door-ext-fg36" : g == PlanGroup.Commercial ? "door-storefront36" : g == PlanGroup.Industrial ? "door-hm36" : "door-walk-36-steel";
            p.openings.Add(new DesignOpening { floor = 0, ext = true, side = 0, pos = Mathf.Round(w * 0.35f), product = door });
            p.AutoSizeStructure();
            return p;
        }

        public static DesignEditor Blank(PlanGroup g, float w = 0f, float d = 0f) => new DesignEditor(NewBlank(g, w, d));
        public static DesignEditor CopyOf(HousePlan p) => new DesignEditor(PlanDesign.From(p));

        // ------------------------------------------------------------------ snap + undo

        /// <summary>Plan grid in feet: Game.Snap's grid (G cycles it), never finer than 6" or coarser than 4'.</summary>
        public static float SnapFt
        {
            get
            {
                float g = Game.Snap != null ? Game.Snap.GridMeters / Units.Foot : 0f;
                return g < 0.5f ? 0.5f : Mathf.Min(g, 4f);
            }
        }
        public static float Snap(float ft) { float s = SnapFt; return Mathf.Round(ft / s) * s; }
        public static Vector2 Snap(Vector2 p) => new Vector2(Snap(p.x), Snap(p.y));
        /// <summary>Feet as the game shows lengths (Units.Format: 12' 6").</summary>
        public static string Ft(float ft) => Units.Format(ft * Units.Foot);

        public void Push() { _undo.Add(Design.ToJson()); if (_undo.Count > 120) _undo.RemoveAt(0); _redo.Clear(); }
        /// <summary>Forgets the last undo step (a drag that ended where it started).</summary>
        public void DropUndo() { if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1); }
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public bool Undo() { if (_undo.Count == 0) return false; _redo.Add(Design.ToJson()); Design = PlanDesign.FromJson(_undo[_undo.Count - 1]); _undo.RemoveAt(_undo.Count - 1); Changed(); return true; }
        public bool Redo() { if (_redo.Count == 0) return false; _undo.Add(Design.ToJson()); Design = PlanDesign.FromJson(_redo[_redo.Count - 1]); _redo.RemoveAt(_redo.Count - 1); Changed(); return true; }
        /// <summary>Restores a snapshot without touching the undo stacks (live drags rebuild from their start state).</summary>
        public void Restore(string json) { Design = PlanDesign.FromJson(json); Changed(); }
        public void Changed() { Design.AutoSizeStructure(); _dirty = true; }

        bool Fail(string m) { Message = m; return false; }
        bool Ok(string m) { Message = m; Changed(); return true; }

        // ------------------------------------------------------------------ derived

        public CustomPlan Plan { get { Rebuild(); return _plan; } }
        public List<DesignIssue> Issues { get { Rebuild(); return _issues; } }
        public int ErrorCount { get { int n = 0; foreach (var i in Issues) if (i.Error) n++; return n; } }
        public bool Valid => ErrorCount == 0;

        void Rebuild()
        {
            if (!_dirty && _plan != null) return;
            _dirty = false;
            try { _plan = Design.Build(); }
            catch (Exception e) { Debug.LogWarning("[PlanDesigner] build failed: " + e); _plan = null; }
            _issues = _plan != null ? DesignChecks.Run(Design, _plan) : new List<DesignIssue> { new DesignIssue { Text = "This drawing can't be turned into a plan yet.", Error = true, Check = "build" } };
        }

        public string StatsLine
        {
            get
            {
                var p = Plan; if (p == null) return "";
                var t = p.Takeoff;
                string use = p.IsResidential ? $"{p.Beds * p.Units} bed / {(p.Baths2 * p.Units % 2 == 0 ? (p.Baths2 * p.Units / 2).ToString() : (p.Baths2 * p.Units / 2) + ".5")} bath" : p.Category == PlanDesigner.Category ? p.Occupancy : p.Category;
                string gar = p.GarageSqFt > 0 && p.IsResidential ? $" · {p.GarageBays}-car garage" : "";
                return $"{p.SqFt:N0} sq ft · {use}{gar} · {p.Floors} {(p.Floors > 1 ? "floors" : "floor")} · est. build cost ${t.Total:N0} (${t.Total / Mathf.Max(1f, p.SqFt):0}/sq ft) · {p.TotalDays} crew days";
            }
        }

        // ------------------------------------------------------------------ queries

        public DesignRoom RoomAt(int floor, Vector2 p)
        {
            foreach (var r in Design.rooms) if (r.floor == floor && p.x > r.x0 && p.x < r.x1 && p.y > r.y0 && p.y < r.y1) return r;
            return null;
        }

        /// <summary>Nearest wall line of the finished plan on that floor within tol (ft).</summary>
        public PlanWall WallNear(int floor, Vector2 p, float tol, out float along)
        {
            along = 0f; PlanWall best = null; float bd = tol;
            var plan = Plan; if (plan == null) return null;
            foreach (var w in plan.Walls)
            {
                if (w.Floor != floor) continue;
                float t = Mathf.Clamp(Vector2.Dot(p - w.A, w.Dir), 0f, w.LengthFt);
                float d = Vector2.Distance(w.At(t), p);
                if (d < bd) { bd = d; best = w; along = t; }
            }
            return best;
        }

        /// <summary>Center (plan point) of a placed opening.</summary>
        public Vector2 PointOf(DesignOpening o) =>
            !o.ext ? new Vector2(o.x, o.y) : o.side == 0 ? new Vector2(o.pos, 0f) : o.side == 1 ? new Vector2(Design.width, o.pos) : o.side == 2 ? new Vector2(o.pos, Design.depth) : new Vector2(0f, o.pos);

        /// <summary>True when a finished-plan opening was placed by the player (not by the plan rules).</summary>
        public bool IsPlaced(PlanOpening o)
        {
            var plan = Plan; var p = plan.Walls[o.Wall].At(o.CenterFt);
            return Design.openings.Exists(d => d.floor == o.Floor && d.product == o.ProductId && (PointOf(d) - p).sqrMagnitude < 0.25f);
        }

        // ------------------------------------------------------------------ footprint

        /// <summary>Resizes the footprint (front-left corner stays). Rooms on the right/back edges stretch or shrink with it; anything left outside goes.</summary>
        public bool SetFootprint(float w, float d, bool undo = true)
        {
            w = Mathf.Clamp(Snap(w), MinSizeFt, MaxSizeFt); d = Mathf.Clamp(Snap(d), MinSizeFt, MaxSizeFt);
            if (Mathf.Approximately(w, Design.width) && Mathf.Approximately(d, Design.depth)) return false;
            if (undo) Push();
            ResizeNoUndo(w, d);
            return Ok($"Footprint {Ft(w)} x {Ft(d)}");
        }

        void ResizeNoUndo(float w, float d)
        {
            float ow = Design.width, od = Design.depth;
            foreach (var r in Design.rooms)
            {
                if (Mathf.Abs(r.x1 - ow) < 0.01f) r.x1 = w;
                if (Mathf.Abs(r.y1 - od) < 0.01f) r.y1 = d;
                r.x1 = Mathf.Min(r.x1, w); r.y1 = Mathf.Min(r.y1, d);
            }
            Design.rooms.RemoveAll(r => r.x1 - r.x0 < 0.49f || r.y1 - r.y0 < 0.49f);
            Design.width = w; Design.depth = d;
            Design.openings.RemoveAll(o => o.ext ? o.pos > (o.side % 2 == 0 ? w : d) - 1f : o.x > w - 0.5f || o.y > d - 0.5f);
            Design.suppressed.RemoveAll(s => s.x > w + 0.01f || s.y > d + 0.01f);
            foreach (var f in Floors()) if (Design.RoomsOn(f).Count == 0) Design.rooms.Add(new DesignRoom { name = f == 0 ? "Living" : "Hall", floor = f, x0 = 0, y0 = 0, x1 = w, y1 = d });
        }

        IEnumerable<int> Floors() { for (int f = 0; f < Design.floors; f++) yield return f; }

        // ------------------------------------------------------------------ walls

        /// <summary>Draws a wall: a horizontal or vertical line (whichever the drag is closer to) that splits every room it crosses.
        /// Plan rooms are rectangles, so the wall runs all the way across each room it cuts.</summary>
        public bool DrawWall(int floor, Vector2 a, Vector2 b)
        {
            a = Snap(a); b = Snap(b);
            bool horiz = Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.y - a.y);
            float c = horiz ? (a.y + b.y) / 2f : (a.x + b.x) / 2f; c = Snap(c);
            float lo = horiz ? Mathf.Min(a.x, b.x) : Mathf.Min(a.y, b.y), hi = horiz ? Mathf.Max(a.x, b.x) : Mathf.Max(a.y, b.y);
            if (hi - lo < 0.01f) { lo -= 0.1f; hi += 0.1f; }
            var hits = WallHits(floor, horiz, c, lo, hi);
            if (hits.Count == 0) return Fail("Draw the wall across a room (it must leave at least 2' on each side).");
            Push();
            foreach (var r in hits)
            {
                var n = r.Clone();
                if (horiz) { n.y0 = c; r.y1 = c; } else { n.x0 = c; r.x1 = c; }
                Design.rooms.Add(n);
                // the bigger part keeps the name, the other is a new room to name
                var small = (r.x1 - r.x0) * (r.y1 - r.y0) >= (n.x1 - n.x0) * (n.y1 - n.y0) ? n : r;
                small.name = NextName("Room", floor, small);
            }
            return Ok($"Wall drawn ({(horiz ? "across" : "front to back")} at {Ft(c)}): {hits.Count} room{(hits.Count > 1 ? "s" : "")} split. Name the new room with the Name tool.");
        }

        /// <summary>Rooms a wall at c (horizontal = constant y) between lo..hi would split.</summary>
        public List<DesignRoom> WallHits(int floor, bool horiz, float c, float lo, float hi)
        {
            var l = new List<DesignRoom>();
            foreach (var r in Design.rooms)
            {
                if (r.floor != floor) continue;
                float a0 = horiz ? r.y0 : r.x0, a1 = horiz ? r.y1 : r.x1, b0 = horiz ? r.x0 : r.y0, b1 = horiz ? r.x1 : r.y1;
                if (c < a0 + MinRoomFt - 0.01f || c > a1 - MinRoomFt + 0.01f) continue;
                if (Mathf.Min(hi, b1) - Mathf.Max(lo, b0) <= 0.01f && !(lo >= b0 && lo <= b1)) continue;
                if (IsStair(r)) continue;
                l.Add(r);
            }
            return l;
        }

        static bool IsStair(DesignRoom r) => Blueprint.KindOf(r.name) == RoomKind.Stair;

        /// <summary>Removes the wall between two rooms at p by joining them (only when together they make a rectangle).</summary>
        public bool EraseWall(int floor, Vector2 p)
        {
            const float tol = 1.2f;
            DesignRoom best1 = null, best2 = null; float bd = tol;
            var on = Design.RoomsOn(floor);
            for (int i = 0; i < on.Count; i++)
                for (int j = 0; j < on.Count; j++)
                {
                    if (i == j) continue; var a = on[i]; var b = on[j];
                    // a below/left of b, shared edge
                    if (Mathf.Abs(a.y1 - b.y0) < 0.01f)
                    {
                        float s0 = Mathf.Max(a.x0, b.x0), s1 = Mathf.Min(a.x1, b.x1);
                        if (s1 - s0 > 0.01f && p.x >= s0 && p.x <= s1 && Mathf.Abs(p.y - a.y1) < bd) { bd = Mathf.Abs(p.y - a.y1); best1 = a; best2 = b; }
                    }
                    if (Mathf.Abs(a.x1 - b.x0) < 0.01f)
                    {
                        float s0 = Mathf.Max(a.y0, b.y0), s1 = Mathf.Min(a.y1, b.y1);
                        if (s1 - s0 > 0.01f && p.y >= s0 && p.y <= s1 && Mathf.Abs(p.x - a.x1) < bd) { bd = Mathf.Abs(p.x - a.x1); best1 = a; best2 = b; }
                    }
                }
            if (best1 == null) return Fail("Click on an inside wall between two rooms to remove it.");
            if (IsStair(best1) || IsStair(best2)) return Fail("Stairs keep their walls. Delete the stair with the Erase tool on the stair itself.");
            var u = Rect.MinMaxRect(Mathf.Min(best1.x0, best2.x0), Mathf.Min(best1.y0, best2.y0), Mathf.Max(best1.x1, best2.x1), Mathf.Max(best1.y1, best2.y1));
            float area = (best1.x1 - best1.x0) * (best1.y1 - best1.y0) + (best2.x1 - best2.x0) * (best2.y1 - best2.y0);
            if (Mathf.Abs(u.width * u.height - area) > 0.05f)
                return Fail($"{best1.name} and {best2.name} don't line up, so joining them wouldn't make a rectangle. Move a wall so their edges match first.");
            Push();
            var big = area - (best1.x1 - best1.x0) * (best1.y1 - best1.y0) > (best1.x1 - best1.x0) * (best1.y1 - best1.y0) ? best2 : best1;
            var small = big == best1 ? best2 : best1;
            big.Ft = u; Design.rooms.Remove(small);
            DropDoorsOff(floor);
            return Ok($"Wall removed: {small.name} joined {big.name} ({Ft(u.width)} x {Ft(u.height)}).");
        }

        /// <summary>The wall line run under p: orientation, coordinate and the rooms on each side whose edge lies on it.</summary>
        public bool FindWallLine(int floor, Vector2 p, out bool horiz, out float c, out float lo, out float hi)
        {
            lo = hi = 0f;
            float bd = 1.2f; bool found = false, hz = false; float cv = 0f;
            foreach (var r in Design.RoomsOn(floor))
            {
                void Try(bool h, float cc, float a, float b)
                {
                    if (Mathf.Abs(cc) < 0.01f || Mathf.Abs(cc - (h ? Design.depth : Design.width)) < 0.01f) return;   // exterior
                    float along = h ? p.x : p.y, d = Mathf.Abs((h ? p.y : p.x) - cc);
                    if (along < a - 0.01f || along > b + 0.01f || d >= bd) return;
                    bd = d; hz = h; cv = cc; found = true;
                }
                Try(true, r.y0, r.x0, r.x1); Try(true, r.y1, r.x0, r.x1); Try(false, r.x0, r.y0, r.y1); Try(false, r.x1, r.y0, r.y1);
            }
            horiz = hz; c = cv;
            if (!found) return false;
            // contiguous run of collinear edges containing p
            var segs = new List<(float a, float b)>();
            foreach (var r in Design.RoomsOn(floor))
            {
                if (horiz) { if (Mathf.Abs(r.y0 - c) < 0.01f || Mathf.Abs(r.y1 - c) < 0.01f) segs.Add((r.x0, r.x1)); }
                else if (Mathf.Abs(r.x0 - c) < 0.01f || Mathf.Abs(r.x1 - c) < 0.01f) segs.Add((r.y0, r.y1));
            }
            float pa = horiz ? p.x : p.y; lo = pa; hi = pa;
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var s in segs)
                    if (s.a <= hi + 0.01f && s.b >= lo - 0.01f && (s.a < lo - 0.01f || s.b > hi + 0.01f)) { lo = Mathf.Min(lo, s.a); hi = Mathf.Max(hi, s.b); grew = true; }
            }
            return true;
        }

        /// <summary>Slides the inside wall under p to the new coordinate (plan x for a front-to-back wall, plan y for a side-to-side one).
        /// Every room edge on that wall line moves with it; doors on it move too. Rooms keep at least 2'.</summary>
        public bool MoveWall(int floor, Vector2 p, float newC, bool undo = true)
        {
            if (!FindWallLine(floor, p, out bool h, out float c, out float lo, out float hi)) return Fail("Grab an inside wall to move it.");
            newC = Snap(newC);
            var below = new List<DesignRoom>(); var above = new List<DesignRoom>();
            foreach (var r in Design.RoomsOn(floor))
            {
                float a = h ? r.x0 : r.y0, b = h ? r.x1 : r.y1;
                if (a < lo - 0.01f || b > hi + 0.01f) continue;
                if (Mathf.Abs((h ? r.y1 : r.x1) - c) < 0.01f) below.Add(r);
                else if (Mathf.Abs((h ? r.y0 : r.x0) - c) < 0.01f) above.Add(r);
            }
            if (below.Count == 0 || above.Count == 0) return Fail("That wall has rooms only on one side.");
            if (below.Exists(IsStair) || above.Exists(IsStair)) return Fail("Stairs are fixed in size: move the stair's neighbors' other walls, or erase and redraw the stair.");
            float min = float.MinValue, max = float.MaxValue;
            foreach (var r in below) min = Mathf.Max(min, (h ? r.y0 : r.x0) + MinRoomFt);
            foreach (var r in above) max = Mathf.Min(max, (h ? r.y1 : r.x1) - MinRoomFt);
            newC = Mathf.Clamp(newC, min, max);
            if (Mathf.Abs(newC - c) < 0.01f) return false;
            if (undo) Push();
            foreach (var r in below) { if (h) r.y1 = newC; else r.x1 = newC; }
            foreach (var r in above) { if (h) r.y0 = newC; else r.x0 = newC; }
            foreach (var o in Design.openings)
                if (!o.ext && o.floor == floor && (h ? Mathf.Abs(o.y - c) < 0.05f && o.x >= lo && o.x <= hi : Mathf.Abs(o.x - c) < 0.05f && o.y >= lo && o.y <= hi))
                { if (h) o.y = newC; else o.x = newC; }
            return Ok($"Wall moved to {Ft(newC)} ({Ft(Mathf.Abs(newC - c))} {(newC > c ? (h ? "back" : "right") : (h ? "forward" : "left"))}).");
        }

        // ------------------------------------------------------------------ rooms

        /// <summary>Draws a room box: carves the rectangle out of whatever rooms are there and names it. Leftover parts keep their names.</summary>
        public bool AddRoom(int floor, Rect r, string label, bool undo = true)
        {
            r = Rect.MinMaxRect(Mathf.Max(0f, Snap(r.xMin)), Mathf.Max(0f, Snap(r.yMin)), Mathf.Min(Design.width, Snap(r.xMax)), Mathf.Min(Design.depth, Snap(r.yMax)));
            if (r.width < MinRoomFt - 0.01f || r.height < MinRoomFt - 0.01f) return Fail("Drag a box at least 2' x 2' inside the footprint.");
            var touched = Design.RoomsOn(floor).FindAll(x => Overlap(x.Ft, r) > 0.01f);
            if (touched.Exists(IsStair) && Blueprint.KindOf(label) != RoomKind.Stair) return Fail("That box overlaps the stair. Draw it beside the stair.");
            if (undo) Push();
            string name = NameFor(label, floor, null);
            Carve(floor, r);
            Design.rooms.Add(new DesignRoom { name = name, floor = floor, x0 = r.xMin, y0 = r.yMin, x1 = r.xMax, y1 = r.yMax });
            DropDoorsOff(floor);
            return Ok($"{name} drawn: {Ft(r.width)} x {Ft(r.height)} ({r.width * r.height:0} sq ft).");
        }

        static float Overlap(Rect a, Rect b) => Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin)) * Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));

        /// <summary>Cuts rect r out of the rooms on a floor (guillotine: up to four leftover rectangles per room, joined again where they line up).</summary>
        void Carve(int floor, Rect r)
        {
            var made = new List<DesignRoom>();
            foreach (var x in Design.RoomsOn(floor))
            {
                if (Overlap(x.Ft, r) <= 0.01f) continue;
                var f = x.Ft; var i = Rect.MinMaxRect(Mathf.Max(f.xMin, r.xMin), Mathf.Max(f.yMin, r.yMin), Mathf.Min(f.xMax, r.xMax), Mathf.Min(f.yMax, r.yMax));
                Design.rooms.Remove(x);
                // two ways to cut the leftover; keep the one with the biggest single piece
                var a = new List<Rect> { Rect.MinMaxRect(f.xMin, f.yMin, f.xMax, i.yMin), Rect.MinMaxRect(f.xMin, i.yMax, f.xMax, f.yMax), Rect.MinMaxRect(f.xMin, i.yMin, i.xMin, i.yMax), Rect.MinMaxRect(i.xMax, i.yMin, f.xMax, i.yMax) };
                var b = new List<Rect> { Rect.MinMaxRect(f.xMin, f.yMin, i.xMin, f.yMax), Rect.MinMaxRect(i.xMax, f.yMin, f.xMax, f.yMax), Rect.MinMaxRect(i.xMin, f.yMin, i.xMax, i.yMin), Rect.MinMaxRect(i.xMin, i.yMax, i.xMax, f.yMax) };
                float Big(List<Rect> l) { float m = 0; foreach (var q in l) if (q.width > 0.01f && q.height > 0.01f) m = Mathf.Max(m, q.width * q.height); return m; }
                var pick = Big(a) >= Big(b) ? a : b;
                var pieces = new List<DesignRoom>();
                foreach (var q in pick) if (q.width > 0.01f && q.height > 0.01f) pieces.Add(new DesignRoom { name = x.name, floor = floor, x0 = q.xMin, y0 = q.yMin, x1 = q.xMax, y1 = q.yMax });
                pieces.Sort((p, q) => ((q.x1 - q.x0) * (q.y1 - q.y0)).CompareTo((p.x1 - p.x0) * (p.y1 - p.y0)));
                // a closed room (bedroom, bath...) can't be L-shaped: the smaller leftover parts become their own rooms to name
                bool open = Blueprint.IsOpen(Blueprint.KindOf(x.name));
                // each piece is added before the next is named, so NameFor/NextName see it and don't hand out the same name twice
                for (int k = 0; k < pieces.Count; k++)
                {
                    var pc = pieces[k]; float mn = Mathf.Min(pc.x1 - pc.x0, pc.y1 - pc.y0);
                    if (k > 0 && !open) pc.name = mn < 4f ? NameFor("Closet", floor, null) : NextName("Room", floor);
                    Design.rooms.Add(pc); made.Add(pc);
                }
            }
            // re-join leftovers that line up into one rectangle
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < made.Count && !merged; i++)
                    for (int j = i + 1; j < made.Count && !merged; j++)
                    {
                        var p = made[i]; var q = made[j]; if (p.name != q.name) continue;
                        var u = Rect.MinMaxRect(Mathf.Min(p.x0, q.x0), Mathf.Min(p.y0, q.y0), Mathf.Max(p.x1, q.x1), Mathf.Max(p.y1, q.y1));
                        if (Mathf.Abs(u.width * u.height - (p.x1 - p.x0) * (p.y1 - p.y0) - (q.x1 - q.x0) * (q.y1 - q.y0)) > 0.05f) continue;
                        p.Ft = u; Design.rooms.Remove(q); made.RemoveAt(j); merged = true;
                    }
            }
        }

        /// <summary>Names the room at p from the palette label (Bedroom → "Bed 2", Bath → "Bath 2"...).</summary>
        public bool NameRoom(int floor, Vector2 p, string label)
        {
            var r = RoomAt(floor, p);
            if (r == null) return Fail("Click inside a room to name it.");
            if (Blueprint.KindOf(label) == RoomKind.Stair) return Fail("Use the Stair tool: a stair needs the same opening on the floor above.");
            if (IsStair(r)) return Fail("That's the stair. Erase it with the Erase tool to use the space for something else.");
            Push();
            string old = r.name;
            r.name = NameFor(label, floor, r);
            return Ok($"{old} is now {r.name} ({Ft(r.x1 - r.x0)} x {Ft(r.y1 - r.y0)}).");
        }

        /// <summary>Sets a room's name directly (free text).</summary>
        public bool RenameRoom(DesignRoom r, string name)
        {
            if (r == null || string.IsNullOrWhiteSpace(name) || name == r.name) return false;
            Push(); r.name = name.Trim(); return Ok("Renamed to " + r.name);
        }

        /// <summary>The name a palette label becomes on this floor, numbered when one already exists.</summary>
        public string NameFor(string label, int floor, DesignRoom self)
        {
            var k = Blueprint.KindOf(label);
            if (label == "Bedroom")
            {
                int n = 1; foreach (var x in Design.rooms) if (x != self && Blueprint.KindOf(x.name) == RoomKind.Bedroom) n++;
                while (Design.rooms.Exists(x => x != self && x.name == "Bed " + n)) n++;
                return "Bed " + n;
            }
            if (k == RoomKind.Bath && label == "Bath")
            {
                int n = 1; foreach (var x in Design.rooms) if (x != self && Blueprint.KindOf(x.name) == RoomKind.Bath && !x.name.StartsWith("Master")) n++;
                if (n == 1) return "Bath";
                foreach (var x in Design.rooms) if (x != self && x.name == "Bath") x.name = "Bath 1";
                while (Design.rooms.Exists(x => x != self && x.name == "Bath " + n)) n++;
                return "Bath " + n;
            }
            if (!Design.rooms.Exists(x => x != self && x.name == label)) return label;
            if (Blueprint.IsOpen(k)) return label;   // open-plan pieces may share a name (they read as one room)
            return NextName(label, floor, self);
        }

        string NextName(string label, int floor, DesignRoom self = null)
        {
            int n = 2; while (Design.rooms.Exists(x => x != self && x.name == label + " " + n)) n++;
            return label == "Room" && !Design.rooms.Exists(x => x != self && x.name == "Room") ? "Room" : label + " " + n;
        }

        /// <summary>Erase tool: removes a placed/auto opening at p, else the stair at p, else the wall at p.</summary>
        public bool EraseAt(int floor, Vector2 p)
        {
            if (RemoveOpeningAt(floor, p, false)) return true;
            var r = RoomAt(floor, p);
            if (r != null && IsStair(r) && !NearAnyEdge(r, p)) return RemoveStair(r);
            return EraseWall(floor, p);
        }

        static bool NearAnyEdge(DesignRoom r, Vector2 p) => p.x - r.x0 < 0.8f || r.x1 - p.x < 0.8f || p.y - r.y0 < 0.8f || r.y1 - p.y < 0.8f;

        // ------------------------------------------------------------------ stairs + floors

        /// <summary>Stair length a full flight needs here (run + 3' landing), and its minimum width.</summary>
        public float StairLengthFt
        {
            get
            {
                var plan = Plan; float rise = plan != null ? plan.FloorToFloorIn(0) : Design.wallHeight * 12f + 12f;
                bool irc = Design.Group == PlanGroup.Residential || Design.Group == PlanGroup.Outbuilding;
                int risers = Mathf.CeilToInt(rise / (irc ? 7.75f : 7f) - 0.001f);
                return Mathf.Ceil(((risers - 1) * (irc ? 10f : 11f) / 12f + 3f) * 2f) / 2f;
            }
        }
        public float StairWidthFt => Design.Group == PlanGroup.Residential || Design.Group == PlanGroup.Outbuilding ? 3.5f : 4f;

        /// <summary>Draws a stair from floor f up to f+1 (adds the floor above if there isn't one).</summary>
        public bool AddStair(int floor, Rect r)
        {
            if (floor >= 2) return Fail("Stairs go up from floor 1 or 2.");
            r = Rect.MinMaxRect(Mathf.Max(0f, Snap(r.xMin)), Mathf.Max(0f, Snap(r.yMin)), Mathf.Min(Design.width, Snap(r.xMax)), Mathf.Min(Design.depth, Snap(r.yMax)));
            if (r.width < 2.5f || r.height < 2.5f) return Fail($"Drag the stair out: about {Ft(StairWidthFt)} wide x {Ft(StairLengthFt)} long.");
            Push();
            if (floor + 1 >= Design.floors) AddFloorNoUndo();
            Carve(floor, r); Carve(floor + 1, r);
            Design.rooms.Add(new DesignRoom { name = "Stair", floor = floor, x0 = r.xMin, y0 = r.yMin, x1 = r.xMax, y1 = r.yMax });
            Design.rooms.Add(new DesignRoom { name = "Stair", floor = floor + 1, x0 = r.xMin, y0 = r.yMin, x1 = r.xMax, y1 = r.yMax });
            DropDoorsOff(floor); DropDoorsOff(floor + 1);
            float len = Mathf.Max(r.width, r.height);
            return Ok($"Stair {Ft(Mathf.Min(r.width, r.height))} x {Ft(len)} from floor {floor + 1} to {floor + 2}" + (len < StairLengthFt - 0.01f ? $" (too short: needs {Ft(StairLengthFt)})." : "."));
        }

        bool RemoveStair(DesignRoom r)
        {
            Push();
            foreach (var x in Design.rooms.FindAll(x => IsStair(x) && x.Ft == r.Ft)) x.name = x.floor == 0 ? "Hall" : "Hall";
            return Ok("Stair removed (the space is a hall now).");
        }

        public bool AddFloor()
        {
            if (Design.floors >= 3) return Fail("Three floors is the most the designer draws.");
            Push(); AddFloorNoUndo();
            return Ok($"Floor {Design.floors} added. Split it into rooms, then draw a Stair on the floor below to reach it.");
        }

        void AddFloorNoUndo()
        {
            int f = Design.floors; Design.floors++;
            string n = Design.Group == PlanGroup.Outbuilding ? "Loft" : Design.Group == PlanGroup.Residential ? "Hall" : "Open office";
            Design.rooms.Add(new DesignRoom { name = n, floor = f, x0 = 0, y0 = 0, x1 = Design.width, y1 = Design.depth });
        }

        public bool RemoveTopFloor()
        {
            if (Design.floors <= 1) return Fail("A plan needs at least one floor.");
            Push();
            int top = Design.floors - 1;
            Design.rooms.RemoveAll(r => r.floor == top);
            Design.openings.RemoveAll(o => o.floor == top); Design.suppressed.RemoveAll(s => s.floor == top);
            Design.floors--;
            // only the stair that led up to the removed floor: keep the top of a stair arriving from the floor below
            foreach (var r in Design.rooms)
                if (r.floor == top - 1 && IsStair(r) && !Design.rooms.Exists(s => s.floor == top - 2 && IsStair(s) && s.Ft == r.Ft)) r.name = "Hall";
            return Ok($"Floor {top + 1} removed.");
        }

        // ------------------------------------------------------------------ openings

        public static bool IsWindow(string product) { var d = Catalog.Get(product); return d != null && d.Category == PieceCategory.Window; }
        public static bool IsOverhead(string product) { var d = Catalog.Get(product); return d != null && d.Category == PieceCategory.GarageDoor; }

        /// <summary>Places a door/window/garage door centered on the wall nearest p (interior doors on inside walls, windows and garage doors on outside walls).</summary>
        public bool PlaceOpening(int floor, Vector2 p, string product)
        {
            var def = Catalog.Get(product); if (def == null) return Fail("Unknown product " + product);
            var w = WallNear(floor, p, 1.5f, out float along);
            if (w == null) return Fail("Click on a wall.");
            bool win = IsWindow(product), oh = IsOverhead(product);
            float half = def.Size.x / Units.Foot / 2f + 0.3f;
            if (w.LengthFt < half * 2f + 0.2f) return Fail($"That wall is only {Ft(w.LengthFt)} long — too short for a {def.Name}.");
            along = Mathf.Clamp(Snap(along), half, w.LengthFt - half);
            var at = w.At(along);
            if (!w.Exterior && (win || oh)) return Fail(win ? "Windows go on outside walls." : "Garage doors go on outside walls.");
            if (w.Exterior && !win && floor > 0 && Design.mezzanine <= 0f) return Fail("Outside doors go on the ground floor (upstairs, use windows).");
            if (w.Exterior && !win && !oh && Catalog.Get(product).Name.ToLower().Contains("interior")) return Fail("That's an interior door: pick an exterior door for an outside wall.");
            Push();
            var o = new DesignOpening { floor = floor, product = product, ext = w.Exterior };
            if (w.Exterior)
            {
                int first = Plan.Walls.FindIndex(x => x.Floor == floor && x.Exterior);
                o.side = Plan.Walls.IndexOf(w) - first; o.pos = o.side % 2 == 0 ? at.x : at.y;
            }
            else { o.x = at.x; o.y = at.y; }
            // replaces anything the player already put there; an auto opening at the spot is dropped
            Design.openings.RemoveAll(x => x.floor == floor && (PointOf(x) - at).magnitude < half + 0.5f);
            foreach (var po in Plan.Openings) if (po.Floor == floor && po.Wall == Plan.Walls.IndexOf(w) && Mathf.Abs(po.CenterFt - along) < half + po.WidthFt / 2f && !IsPlaced(po))
                { var pp = w.At(po.CenterFt); Design.suppressed.Add(new DesignPoint { floor = floor, x = pp.x, y = pp.y }); }
            Design.openings.Add(o);
            Changed();
            var plan = Plan;
            bool placed = plan.Openings.Exists(x => x.Floor == floor && x.ProductId == product && (plan.Walls[x.Wall].At(x.CenterFt) - at).sqrMagnitude < 0.25f);
            if (!placed) { Undo(); _redo.Clear(); return Fail($"A {def.Name} doesn't fit there (too close to a corner or another opening)."); }
            return Ok($"{def.Name} placed {(w.Exterior ? "on the " + SideName(o.side) + " wall" : "between rooms")} at {Ft(along)}.");
        }

        public static string SideName(int s) => s == 0 ? "front" : s == 1 ? "right" : s == 2 ? "back" : "left";

        /// <summary>Removes the opening at p: a placed one is deleted, an auto-placed one is switched off.</summary>
        public bool RemoveOpeningAt(int floor, Vector2 p, bool complain = true)
        {
            var plan = Plan; if (plan == null) return false;
            foreach (var o in plan.Openings)
            {
                if (o.Floor != floor) continue;
                var w = plan.Walls[o.Wall]; var c = w.At(o.CenterFt);
                float along = Mathf.Abs(Vector2.Dot(p - c, w.Dir)), off = Mathf.Abs(Vector2.Dot(p - c, new Vector2(-w.Dir.y, w.Dir.x)));
                if (along > o.WidthFt / 2f + 0.2f || off > 1.2f) continue;
                Push();
                int n = Design.openings.RemoveAll(d => d.floor == floor && d.product == o.ProductId && (PointOf(d) - c).sqrMagnitude < 0.25f);
                if (n == 0) Design.suppressed.Add(new DesignPoint { floor = floor, x = c.x, y = c.y });
                return Ok($"{o.Product?.Name ?? o.ProductId} removed" + (n == 0 ? " (it was placed automatically; it stays off)." : "."));
            }
            return complain && Fail("Click on a door or window to remove it.");
        }

        /// <summary>Placed interior doors that are no longer on a wall line go away after rooms change.</summary>
        void DropDoorsOff(int floor)
        {
            Design.openings.RemoveAll(o =>
            {
                if (o.ext || o.floor != floor) return false;
                var p = new Vector2(o.x, o.y);
                bool Between(Vector2 d) { var a = RoomAt(floor, p + d); var b = RoomAt(floor, p - d); return a != null && b != null && a != b; }
                return !(Between(new Vector2(0f, 0.2f)) || Between(new Vector2(0.2f, 0f)));
            });
        }

        // ------------------------------------------------------------------ building settings (undoable)

        public void Set(Action<PlanDesign> change, string msg)
        {
            Push(); change(Design);
            if (Design.Foundation == FoundationType.PostFrame && Design.WallFrame != WallSystem.PostFrame) Design.SetWallPreset(2);
            Ok(msg);
        }
    }
}
