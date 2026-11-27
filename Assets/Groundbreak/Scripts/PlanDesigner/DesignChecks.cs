using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Runs <see cref="BlueprintValidator"/> on a design's plan and rewrites each finding in plain language with the fix,
    /// plus a few designer tips (unnamed rooms, a room split in two, no kitchen/bath). Each finding knows which floor/rect to highlight.</summary>
    public static class DesignChecks
    {
        public static List<DesignIssue> Run(PlanDesign d, CustomPlan plan)
        {
            var list = new List<DesignIssue>();
            foreach (var i in BlueprintValidator.Validate(plan)) list.Add(Explain(i, plan));
            // designer tips (never block building)
            var seen = new HashSet<string>();
            foreach (var r in d.rooms)
            {
                var k = Blueprint.KindOf(r.name);
                if (r.name == "Room" || Regex.IsMatch(r.name, @"^Room \d+$"))
                    list.Add(new DesignIssue { Text = $"Name {r.name} (Name tool): what is it — bedroom, closet, hall...?", Check = "name", Floor = r.floor, Area = r.Ft });
                if (!Blueprint.IsOpen(k) && k != RoomKind.Void && !seen.Add(r.floor + "|" + r.name))
                    list.Add(new DesignIssue { Text = $"{r.name} is drawn as two separate parts. Rooms are rectangles: erase the wall between them, or name one part something else.", Check = "name", Floor = r.floor, Area = r.Ft });
            }
            if (d.Group == PlanGroup.Residential)
            {
                bool Has(RoomKind k) => d.rooms.Exists(r => Blueprint.KindOf(r.name) == k);
                if (!Has(RoomKind.Kitchen)) list.Add(new DesignIssue { Text = "No kitchen yet: name a room Kitchen.", Check = "rooms" });
                if (!Has(RoomKind.Bath)) list.Add(new DesignIssue { Text = "No full bath yet: a house needs at least one Bath (5' x 8' is typical).", Check = "rooms" });
                if (!Has(RoomKind.Bedroom)) list.Add(new DesignIssue { Text = "No bedrooms yet: name a room Bedroom (at least 70 sq ft, 7' wide).", Check = "rooms" });
            }
            list.Sort((a, b) => b.Error.CompareTo(a.Error));
            return list;
        }

        /// <summary>The room a validator message is about (longest room name found in the text), on the floor it mentions if any.</summary>
        static PlanRoom RoomIn(string msg, HousePlan plan)
        {
            int floor = -1; var fm = Regex.Match(msg, @"floor (\d)"); if (fm.Success) floor = int.Parse(fm.Groups[1].Value) - 1;
            PlanRoom best = null;
            foreach (var r in plan.Rooms)
            {
                if (floor >= 0 && r.Floor != floor) continue;
                if (!Regex.IsMatch(msg, @"(^|[^\w])" + Regex.Escape(r.Name) + @"($|[^\w])")) continue;
                if (best == null || r.Name.Length > best.Name.Length) best = r;
            }
            return best;
        }

        public static DesignIssue Explain(PlanIssue i, CustomPlan plan)
        {
            var m = i.Message; var r = RoomIn(m, plan);
            var d = new DesignIssue { Error = i.Error, Check = i.Check, Text = m, Floor = r != null ? r.Floor : -1, Area = r?.Ft };
            string n = r != null ? r.Name : "A room";
            switch (i.Check)
            {
                case "access":
                    d.Text = $"{n} can't be reached: add a door into it (Door tool) from a hall or living area, or erase the wall to open it up."; break;
                case "egress":
                    d.Text = $"{n} needs an emergency escape window: put a 36x60 window on one of its outside walls (IRC R310)."; break;
                case "room size":
                    switch (r != null ? Blueprint.KindOf(r.Name) : RoomKind.Flex)
                    {
                        case RoomKind.Bedroom: d.Text = $"{n} is too small for a bedroom ({Dim(r)}): needs 70 sq ft and 7' each way (IRC R304)."; break;
                        case RoomKind.Bath: d.Text = $"{n} is too narrow for a full bath ({Dim(r)}): needs at least 5' x 7' for a tub, toilet and sink."; break;
                        case RoomKind.HalfBath: d.Text = $"{n} is too small for a toilet + sink ({Dim(r)}): needs 3' wide and 15 sq ft."; break;
                        case RoomKind.Hall: d.Text = $"{n} is narrower than 36\" ({Dim(r)}): halls must be at least 3' wide."; break;
                        case RoomKind.Corridor: d.Text = $"{n} is narrower than 44\" ({Dim(r)}): commercial corridors must be 44\" wide."; break;
                        case RoomKind.Garage: d.Text = $"{n} is too small to park a car ({Dim(r)}): needs 10' x 20' per car."; break;
                        case RoomKind.Restroom: d.Text = $"{n} is too small for a wheelchair ({Dim(r)}): needs a 60\" turning circle (about 7' x 8')."; break;
                        default: d.Text = $"{n} is too small ({Dim(r)}): {m}"; break;
                    }
                    break;
                case "exits":
                    d.Text = m.StartsWith("no exterior door") ? "There's no way in: put an entry door on an outside wall of the ground floor (Door tool)."
                           : m.Contains("exit door(s)") ? "Too many people for one exit: add a second outside door (IBC: over 49 occupants need 2 exits). " + m
                           : "Exits: " + m;
                    if (r == null) d.Area = FrontEdge(plan);
                    d.Floor = 0; break;
                case "stairs":
                    if (m.Contains("no stair")) { d.Text = "The upper floor has no stair: draw one with the Stair tool (about 3'6\" x 14')."; d.Floor = 1; }
                    else if (m.Contains("landing (floor")) d.Text = $"Nothing open at the bottom of {n}: the stair must land in a hall or living area (at least 3' clear).";
                    else if (m.Contains("at the top")) d.Text = $"Nothing open at the top of {n}: put a hall upstairs where the stair arrives.";
                    else if (m.Contains("need")) d.Text = $"{n} is too short for a full flight: " + m.Substring(m.IndexOf(':') + 1).Trim() + ". Make it longer.";
                    else if (m.Contains("wide")) d.Text = $"{n} is too narrow: " + m.Substring(m.IndexOf(' ') + 1) + ". Stairs need 36\" clear (44\" commercial).";
                    else d.Text = "Stair: " + m;
                    break;
                case "spans":
                    if (m.Contains("joists")) d.Text = "Floor joists span too far: " + m + ". Turn on Auto structure, or make the building shallower.";
                    else d.Text = "Roof span too long: " + m + ". Make the building narrower front-to-back, or switch the walls to a steel building / post-frame.";
                    d.Area = new Rect(0, 0, plan.WidthFt, plan.DepthFt); break;
                case "rooms":
                    if (m.Contains("gap or overlap")) d.Text = "Rooms don't fill the whole floor (" + m.Substring(m.IndexOf(':') + 1).Trim() + "). Draw a room over the empty part.";
                    else if (m.Contains("too thin")) d.Text = $"{n} is thinner than 2': move a wall or erase it.";
                    else if (m.Contains("no rooms")) d.Text = "An upper floor has no rooms.";
                    else d.Text = "Rooms: " + m;
                    break;
                case "walls": d.Text = "Walls: " + m + ". Lower the wall height or pick a stronger wall type."; break;
                case "roof": d.Text = "Roof: " + m + (m.Contains("too flat") ? ". Raise the pitch or pick a metal roof." : "."); break;
                case "foundation": d.Text = "Foundation: " + m + "."; break;
                case "openings": d.Text = "A door or window doesn't fit: " + m + ". Remove it or move it along the wall."; break;
                case "mep": d.Text = "Plumbing/HVAC/electric: " + m + (r != null ? $" (make {n} bigger so the fixtures fit)." : "."); break;
                case "plumbing count": d.Text = "Code needs more restroom fixtures: " + m + ". Add a Restroom."; break;
                case "fire": d.Text = "Fire code: " + m + "."; break;
                case "meta": d.Text = "Plan data: " + m; break;
                case "takeoff": d.Text = "Estimate: " + m; break;
            }
            return d;
        }

        static string Dim(PlanRoom r) => r == null ? "" : $"{DesignEditor.Ft(r.Ft.width)} x {DesignEditor.Ft(r.Ft.height)}";
        static Rect FrontEdge(HousePlan p) => new Rect(0, 0, p.WidthFt, 1.5f);
    }
}
