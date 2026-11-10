using System.Linq;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>All tutorial text, in real construction order. Picture names map to PNGs in Resources/Tutorials/
    /// (captured by TutorialCaptureTests). Trades that are not merged yet are marked Available = false and show
    /// a placeholder picture; see Docs/TUTORIALS.md for the capture TODO list.</summary>
    public static class TutorialContent
    {
        const string GS = "Getting started", SITE = "Site & foundation", FR = "Framing", ROOF = "Roof",
            MEP = "Mechanical, electrical & plumbing", INSP = "Inspections", FIN = "Close-up & finish", BIZ = "Outside & business";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Tutorials.Progress.Clear();
            GettingStarted();
            SiteAndConcrete();
            Framing();
            Roof();
            Mechanical();
            Inspections();
            Later();
        }

        // ------------------------------------------------------------------ locators for "show me where"

        static Vector3? Pile(string id)
        {
            foreach (var p in Object.FindObjectsByType<MaterialPile>(FindObjectsSortMode.None)) if (p.DefinitionId == id) return p.transform.position;
            return null;
        }
        static Vector3? AnyPile()
        {
            var p = Object.FindObjectsByType<MaterialPile>(FindObjectsSortMode.None);
            return p.Length > 0 ? p[0].transform.position : (Vector3?)null;
        }
        static Vector3? Drop() => DeliveryService.I != null ? DeliveryService.I.DropPoint : (Vector3?)null;
        static Vector3? Origin() => Game.Snap.Origin;
        static Vector3? Stakes() => SiteLayout.Stakes.Count > 0 ? SiteLayout.Stakes[0].Ground : (Vector3?)null;
        static Vector3? Slab() => ConcreteElement.All.Count > 0 ? Center(ConcreteElement.All[ConcreteElement.All.Count - 1].Outline, ConcreteElement.All[ConcreteElement.All.Count - 1].TopY) : (Vector3?)null;
        static Vector3 Center(Vector2[] o, float y) { var c = Vector2.zero; foreach (var v in o) c += v; c /= Mathf.Max(1, o.Length); return new Vector3(c.x, y, c.y); }
        /// <summary>The blue layout ghost of the job the framing project wants next.</summary>
        static Vector3? NextJob()
        {
            var j = ShellProject.Current?.Active;
            if (j == null) foreach (var f in FramingJob.All) if (f != null && !f.Done) { j = f; break; }
            var g = j?.Current?.Ghost;
            return g != null ? g().center : (Vector3?)null;
        }
        static Vector3? Mep(Trade t) { var n = NetGraph.NodesOf(t).FirstOrDefault(); return n != null ? n.transform.position : (Vector3?)null; }
        static Vector3? RoofAnchorPos() => RoofAnchor.All.Count > 0 ? RoofAnchor.All[0].transform.position : (Vector3?)null;

        // ------------------------------------------------------------------ content

        static void GettingStarted()
        {
            Tutorials.New("start-move", GS, "Moving around the site", "Walk, look, crouch and use the mouse cursor.")
                .Step("Look around", "You start on an empty lot with a starter stack of lumber, sheathing and nails. Walk the site first, like a real builder does before any work: where trucks can get in, where the ground is flat, where you'll stack material.",
                      "WASD move · Mouse look · Space jump · C crouch · Esc frees the cursor", "gs_01_first_view")
                .Step("Your tool belt", "The bottom of the screen is your tool belt: 8 slots. Big tools such as the circular saw take 2 slots, so you can't carry everything. Swap in what the job needs.",
                      "1-8 select a slot · Tab cycle tools · the active tool's hint shows above the belt", "gs_02_tool_belt");

            Tutorials.New("start-place", GS, "Placing and fastening lumber", "Pick a material, place it, fasten it with nails.")
                .Step("Pick a material", "Equip Hands / Place. A see-through ghost shows where the piece will go: green = OK, yellow = it works but an inspector will flag it, red = it can't go there (unsupported or overlapping).",
                      "Hands / Place tool · [ / ] previous / next material · the status line shows stock and cut length", "gs_03_place_ghost")
                .Step("Turn and stand it up", "Lumber comes in flat. Scroll turns it in the angle step; Alt+Scroll tips it up, the way you stand a stud on a plate.",
                      "Scroll = yaw · Alt+Scroll = tilt (stand a stud up) · LMB place · RMB pick a loose piece back up", "gs_03_place_ghost")
                .Step("Repeat at 16\" on center", "Framing repeats on a layout: studs and joists at 16\" or 24\" on center so sheets (4' x 8') land on a framing member at every edge. F places the same piece one grid step over.",
                      "F repeat one grid step · Ctrl+Z undo the last loose piece", "gs_04_repeat_16oc")
                .Step("Nail it off", "A placed piece is loose until it's nailed. The framing hammer takes 3 swings per nail; the cordless framing nailer is one pull. Two nails fasten a piece. Refill your pouch at the nail pile.",
                      "Framing hammer or nailer: LMB on the piece · E at the nail pile = refill pouch", "gs_05_nailed", null, () => Pile("nails16d"), "the nail pile");

            Tutorials.New("start-snap", GS, "Snapping, grid and origin", "Line things up exactly: grid size, angle step, origin.")
                .Step("Grid size", "Everything snaps to a grid measured from the grid origin. 1\" for layout, 16\" or 24\" O.C. for framing, 4' for sheets. The current grid and angle show bottom-left.",
                      "G / Shift+G grid size · R / Shift+R angle step (90°, 45° ... free)", "gs_06_snapping")
                .Step("Set the origin", "Aim at a corner and press O: the grid now starts there and turns with it, so your layout runs square to the building, not to the world.",
                      "O set grid origin at the aimed point · hold Ctrl = grid only (no edge / surface snap)", "gs_06_snapping", null, Origin, "the grid origin");

            Tutorials.New("start-store", GS, "Tablet, store and deliveries", "Buy material and tools, get them delivered.")
                .Step("Open the tablet", "The site tablet is your office: supply store, tool shop, deliveries, bank, plans and these tutorials. While it's open the game ignores movement and building keys.",
                      "T open / close the tablet · Esc closes it", "gs_07_tablet_store", "Supply Store")
                .Step("Order material", "Order what the next stage needs: a lumberyard truck is free but takes about 90 s; rush costs $" + DeliveryService.RushFee + " and takes about 20 s. Order a little extra (5-10%) for cuts and mistakes, like a real takeoff.",
                      "Supply Store: pick a category · + / - (Shift = 10) · Order standard or Rush", "gs_07_tablet_store", "Supply Store")
                .Step("Set the drop point", "The truck unloads at the orange drop marker. Put it close to where you'll work but out of the way of the building and the concrete truck.",
                      "Deliveries app: 'Set drop marker where I'm looking' (aim first, then open the tablet)", "gs_08_delivery_truck", null, Drop, "the delivery drop point")
                .Step("Buy tools", "Each trade needs its own tools: layout stakes and a laser for the site, a rebar bender, finishing tools for concrete, a nailer for framing, a crimper and torch for plumbing. Bought tools go on your belt.",
                      "Tool Shop app: Buy · Bank app shows every purchase", "gs_09_tool_shop", "Tool Shop");
        }

        static void SiteAndConcrete()
        {
            Tutorials.New("site-layout", SITE, "Layout and excavation", "Stake the corners, square them, set grade, dig, base and compact.")
                .Step("Stakes and string", "Drive a stake at each corner and pull mason string between them. The string lines are the edges of the slab; everything else is measured from them.",
                      "Stakes, Mason String & Line Level: LMB drive a stake · LMB stake to stake = string", "c_01_layout", null, Stakes, "your layout stakes")
                .Step("Square by the diagonals", "A box is square when both diagonals are equal. For 12' x 16' the diagonal is exactly 20' (a 3-4-5 triangle). Nudge a corner stake until the two diagonals match within 1/8\".",
                      "Tape measure: click corner to corner · the stake tool reports both diagonals", "c_02_square")
                .Step("Shoot grade with the laser", "Set the rotary laser on its tripod and read the grade rod at each stake: it tells you how much to dig and where the top of the form (and slab) will be. Mark top-of-form on the stakes.",
                      "Rotary Laser Level: LMB set up / shoot · Scroll = slab height", "c_03_laser")
                .Step("Dig", "Strip the topsoil 4\" for the gravel base, and dig a 12\" wide trench to 12\" below grade around the edge: the thickened edge is the footing that carries the walls. Use the mini excavator for the bulk, the shovel to clean up.",
                      "Mini Excavator (2 slots): hold LMB · Round-point shovel: LMB dig · Marking paint: hold LMB to mark", "c_04_excavated")
                .Step("Gravel base and compaction", "Spread 4\" of gravel inside the trench ring and compact it with the plate compactor. A loose base settles and cracks the slab.",
                      "Order gravel (cu yd) · Excavator spreads it · Plate compactor: hold LMB over every spot", "c_05_gravel");

            Tutorials.New("site-forms", SITE, "Forms", "Set 2x forms, stake, brace, oil and lay poly.")
                .Step("Set the forms", "Forms are 2x lumber on edge, inside faces exactly on the string lines. Click each corner, then the first corner again to close the box.",
                      "Form Setting Kit: LMB each corner · LMB the first corner to close", "c_06_forms")
                .Step("Stake and brace", "Wet concrete pushes hard: an unstaked form blows out. Stake every 36\" and add kicker braces; the form tool shows any section still at risk.",
                      "F = stake every 36\" · RMB = kicker brace", "c_07_forms_closeup")
                .Step("Oil and vapor barrier", "Oil the inside faces so the boards strip clean and can be reused, and lay 6-mil poly over the base so ground moisture can't wick up into the slab.",
                      "J = oil the forms · V = lay poly", "c_07_forms_closeup");

            Tutorials.New("site-rebar", SITE, "Rebar", "Lay #4 bar on chairs and tie every crossing.")
                .Step("Lay the grid", "#4 rebar at 18\" on center each way, held up on 2\" chairs so it ends up in the middle of the slab, plus continuous bars in the thickened edge.",
                      "Rebar Cutter / Bender: F = 18\" grid + footing bars", "c_08_rebar")
                .Step("Tie the crossings", "Tie every crossing with tie wire so the mat doesn't move when the concrete hits it and when you walk on it.",
                      "Tie-Wire Twister: LMB on a crossing (uses tie wire rolls)", "c_09_rebar_closeup");

            Tutorials.New("site-pour", SITE, "Ordering and pouring concrete", "Takeoff, dispatch, pour, finish and cure.")
                .Step("Takeoff and order", "Concrete is sold by the cubic yard. The takeoff measures your forms (slab plus thickened edge); order about 10% extra. 3000 psi at a 5\" slump is normal for a slab. Pour on a dry day between 40 and 95°F.",
                      "Concrete Dispatch app: Takeoff · pick yards / psi / slump · Place order", "c_10_dispatch", "Concrete Dispatch")
                .Step("The truck arrives", "The ready-mix truck backs in and you have about 90 minutes before the load starts to set. Plan the pour so the chute reaches as much as possible.",
                      "Chute Signals: LMB aims the chute", "c_11_truck")
                .Step("Place and consolidate", "Fill the far side first. Where the chute doesn't reach, wheelbarrow it (6 cu ft a load). Vibrate along the forms so there are no voids (honeycombing).",
                      "Chute + Wheelbarrow · Concrete rake: hold LMB · Pencil vibrator: hold LMB", "c_12_pouring")
                .Step("Screed and bull float", "Strike off the top with the screed riding on the form tops, fill lows and strike again, then bull float right away to push the rock down and bring up cream. Set J-bolts at the edge while it's wet.",
                      "Screed (2 slots): hold LMB · Bull float: hold LMB · Trowel RMB = set a J-bolt", "c_13_screeded")
                .Step("Edge, joints, broom", "Wait until the bleed water is gone. Then run the edger along the forms, groove control joints so the slab cracks where you want it, and broom a non-slip finish (or hard trowel indoors).",
                      "Edger: hold LMB · Groover: click two points · Broom or trowel: hold LMB", "c_14_finished")
                .Step("Cure and strip", "Keep it from drying too fast. Forms can come off after 24 hours; the slab reaches working strength in about 7 days. Oiled boards come back to your stock.",
                      "Form kit: X = strip forms (after 24 h)", "c_15_cured", null, Slab, "the slab");
        }

        static void Framing()
        {
            Tutorials.New("frame-floor", FR, "Floor framing", "Sills, rim, joists at 16\" O.C. and the subfloor.")
                .Step("Start the project", "In Framing Plans set the building size, pick the door and windows, then start the project at the grid origin. It lays out each stage in order as blue ghosts.",
                      "O set the origin at the corner · Framing Plans app: size · doors / windows · Start project here", "f_00_plans", "Framing Plans")
                .Step("Sills on the foundation", "Snap chalk lines for the sill plates, then lay pressure-treated sills over the anchor bolts. Treated lumber because it touches concrete/ground.",
                      "Chalk Line: LMB the ghost · Hands / Place or nailer on each blue ghost", "f_01_sills", null, NextJob, "the next blue layout ghost")
                .Step("Rim and joists at 16\" O.C.", "Rim joists box the floor; joists run across the short span at 16\" on center with hangers or end nailing. Crown every joist up.",
                      "Aim at the blue ghost · LMB with the tool it names (nailer / hammer)", "f_02_joists", null, NextJob, "the next blue layout ghost")
                .Step("Subfloor", "Glue and nail 4' x 8' tongue-and-groove sheets across the joists with staggered seams, so no two end joints line up on the same joist.",
                      "Adhesive + nailer on each sheet ghost", "f_03_deck", null, NextJob, "the next blue layout ghost");

            Tutorials.New("frame-walls", FR, "Wall framing and tilt-up", "Lay out plates, frame flat on the deck, stand and brace.")
                .Step("Lay out the plates", "Snap a line for the bottom plate, tack the top and bottom plates together and mark every stud, king, jack and cripple. Studs at 16\" O.C. from the corner.",
                      "Chalk Line · Speed Square / tape: LMB the layout ghost", "f_04_layout_marks")
                .Step("Frame it flat", "Build the wall lying on the deck: end studs, studs on the marks, then the openings. It's faster and safer than framing in the air.",
                      "Nailer or hammer: LMB each ghost", "f_05_wall_flat", null, NextJob, "the next blue layout ghost")
                .Step("Square, tilt, plumb, brace", "Square the wall by its diagonals, tilt it up, nail the bottom plate to the deck, plumb it with the level and brace it. One wall at a time; the next wall frames on the open deck.",
                      "Level: LMB to plumb · nailer: brace", "f_06_walls_standing")
                .Step("Double top plate", "The cap plate overlaps the corners and ties the walls together; it's also where trusses or rafters will land.",
                      "Nailer on each cap plate ghost", "f_07_caps");

            Tutorials.New("frame-openings", FR, "Openings, sheathing and units", "Headers, kings and jacks; sheathe; flash and set doors and windows.")
                .Step("Headers, kings and jacks", "An opening interrupts studs, so a header carries the load to jack studs, with king studs beside them. Header size grows with span (2x6 for 3', 2x10 for 6'). Windows also get a rough sill and cripples.",
                      "Framing Plans picks the header size · nailer on each ghost", "f_08_openings")
                .Step("Wall sheathing", "7/16\" OSB on the outside braces the walls against racking. Sheets run past the openings and get cut out after.",
                      "Nailer on each sheet ghost", "f_09_sheathed")
                .Step("Flash and install units", "Flash each rough opening (sill pan, sides, head, shingle-lapped so water runs out), then set the window or door unit, level and plumb.",
                      "Flashing tape job · carry the unit to the opening: it snaps in", "f_10_units");

            Tutorials.New("frame-stairs", FR, "Stairs", "Measure the rise, calculate, lay out and cut stringers.")
                .Step("Measure and calculate", "Measure the total rise floor to floor. The calculator divides it into equal risers no taller than 7-3/4\" with treads at least 10\", and checks the throat left in the 2x12.",
                      "Tape measure: click floor and landing · Stair Calculator app: 'Use for layout'", "s_01_calculator", "Stair Calculator")
                .Step("Lay out the stringer", "Hold the framing square on the 2x12 at rise and run and step it down the board, marking every tread and riser.",
                      "Framing Square on a loose 2x12: LMB lay out", "s_02_layout")
                .Step("Cut it", "Cut the sawtooth with the circular saw and finish the corners by hand so you don't over-cut. Cut one, check it, then use it as the pattern.",
                      "Framing Square: RMB cut (needs the circular saw on your belt)", "s_03_cut")
                .Step("Set stringers and treads", "Hang three stringers from the landing, then risers and treads. Every rise must match within 3/8\" or people trip.",
                      "Aim at the blue ghosts · nailer", "s_04_stairs");
        }

        static void Roof()
        {
            Tutorials.New("roof-frame", ROOF, "Roof framing", "Trusses with the boom truck, or stick framing.")
                .Step("Pick the roof", "In Framing Plans choose pitch (4/12 to 12/12), trusses or stick framing, and gable or hip. Trusses are engineered and fast; stick framing is cut on site.",
                      "Framing Plans app: Pitch - / + · Stick frame / Use trusses · Hip / Gable", "r_00_plans", "Framing Plans")
                .Step("Set trusses with the boom truck", "Mark truss layout at 24\" O.C. on the top plates. The boom truck lifts each truss; you nail it with hurricane ties at both heels and brace it.",
                      "Aim at the truss ghost · nailer / hammer", "r_01_boom_truck")
                .Step("Bracing, fascia and rakes", "Continuous lateral bracing keeps the trusses straight. Sub-fascia, lookouts and fly rafters frame the overhangs.",
                      "Nailer on each ghost", "r_02_trusses")
                .Step("Stick framing", "Rafters cut with a birdsmouth bear on the top plate and meet at a ridge board; ceiling joists and collar ties keep the walls from spreading. Hips need hip and jack rafters.",
                      "Saw + framing square for the cuts · nailer", "r_03_stick_framed");

            Tutorials.New("roof-cover", ROOF, "Roof sheathing and shingles", "Deck, dry-in, shingles and staying on the roof.")
                .Step("Roof deck", "7/16\" OSB with staggered seams and H-clips between rafters at the unsupported edges. Leave the 1/8\" gaps for swelling.",
                      "Nailer on each sheet ghost", "r_04_deck")
                .Step("Stay on the roof", "Above 6/12 you slide off without roof jacks or a harness clipped to the roof anchor at the ridge.",
                      "Harness: LMB near the roof anchor · roof jacks: nail into a rafter", "r_05_jacks", null, RoofAnchorPos, "the roof anchor")
                .Step("Dry-in", "Drip edge at the eaves, ice & water shield along the eave, synthetic underlayment lapped down-slope, then drip edge on the rakes over the underlayment. Now the house is dry.",
                      "Coil roofing nailer · Cap stapler: LMB on each course ghost", "r_06_dry_in")
                .Step("Starter and courses", "Starter strip at the eave and rakes, then shingles course by course, each at the 5-5/8\" exposure, staggering joints.",
                      "Coil Roofing Nailer or Roofing Hatchet (exposure gauge) on each course", "r_07_courses")
                .Step("Ridge vent and caps", "Ridge vent over the slot at the peak, then cap shingles. The finished roof sheds water from the ridge down.",
                      "Roofing nailer on the ridge ghosts", "r_08_shingled");
        }

        static void Mechanical()
        {
            Tutorials.New("mep-plumbing", MEP, "Plumbing rough-in", "Bore, run PEX supply and PVC drains and vents, then test.")
                .Step("Set fixtures and bore", "Set the tub, toilet flange and vanity at their real rough-in spots (toilet 12\" from the wall). Bore studs no bigger than 60% of the stud (40% bearing walls in 2x4 at the center).",
                      "Cordless Drill: LMB on a stud · RMB change bit (1\" auger for 1/2\" PEX, hole saw for 2\" pipe)", "p_01_bored_studs")
                .Step("PEX supply", "Water comes in through the main shutoff, cold to a manifold and to the water heater, hot out to its own manifold, then a home run to each fixture: blue cold, red hot.",
                      "Pipe & fittings kit: LMB points · RMB finish · Scroll = material · PEX crimp tool: hold LMB on each joint", "p_02_manifolds", null, () => Mep(Trade.Plumbing), "the plumbing")
                .Step("Drains and vents (DWV)", "Drains slope 1/4\" per foot to the building drain; every fixture gets a trap and a vent, and stacks go up through the roof. Wyes and long sweeps where flow changes direction.",
                      "Pipe kit: Alt+Scroll = drain slope · PVC primer + cement: hold LMB on each joint", "p_03_stack_wye")
                .Step("Toilet and tub drains", "3\" from the closet flange to the drain; the tub drains through a P-trap to a sanitary tee on its own vent.",
                      "Pipe kit + PVC cement", "p_04_toilet_drain")
                .Step("Vents through the roof", "Vents keep traps from siphoning dry. They go up through the roof deck in a vent boot.",
                      "Pipe kit up to the roof boot", "p_05_roof_vents")
                .Step("Test it", "Cap every stub, pump the gauge to 100 psi and hold 15 minutes: any drop means a joint you didn't crimp. Fill the DWV with water to the roof and check for leaks. Then call the rough-in inspection.",
                      "Test gauge: RMB cap · LMB pump · MEP Plans app: water test, inspection", "p_06_gauge", "MEP Plans");

            Tutorials.New("mep-electrical", MEP, "Electrical rough-in", "Boxes, Romex, the panel, then make up and test.")
                .Step("Boxes and runs", "Mount boxes on the studs (receptacles 12-16\" up, switches 48\"). Bore the studs and pull NM cable (Romex) from the panel to each box. 14 AWG on 15 A circuits, 12 AWG on 20 A.",
                      "Fish Tape: LMB points, snaps to box knockouts and the panel · RMB finish", "e_01_wired_wall", null, () => Mep(Trade.Electrical), "the electrical")
                .Step("Staple it", "Staple the cable within 8\" of each box and at least every 4-1/2' so it can't hang loose in the wall.",
                      "Insulated Staple Hammer: LMB on the Romex", "e_01_wired_wall")
                .Step("Make up the boxes", "Strip the sheath, splice the grounds and pigtail them, splice the neutrals, then install the device: black to brass, white to silver, bare to green.",
                      "Wire Strippers: LMB box = strip · Lineman's pliers: LMB next splice / install device · RMB pick device", "e_02_box_madeup")
                .Step("The panel", "Each home run lands on a breaker sized for its wire. Grounds and neutrals land on their bars.",
                      "Lineman's pliers: LMB the panel = main / reset", "e_03_panel")
                .Step("Power up and test", "Turn on the main and flip the switches. The voltage tester beeps near live wiring; the multimeter finds open grounds and neutrals at a box.",
                      "E on a switch = flip · E on a receptacle = plug in · Voltage tester · Multimeter: LMB on a box", "e_04_room_lit")
                .Step("Fix what the inspector finds", "Any open ground, overfilled box or missing staple gets a red marker. Fix it and run the inspection again.",
                      "MEP Plans app: Electrical inspection", "e_05_marker", "MEP Plans");

            Tutorials.New("mep-hvac", MEP, "HVAC", "Furnace, trunk and branches, returns, condenser, bath fan.")
                .Step("Set the equipment", "Furnace in the closet or attic, condenser outside on a pad, then a plenum on the furnace.",
                      "Hands / Place the furnace and condenser", "h_01_furnace", null, () => Mep(Trade.Hvac), "the HVAC equipment")
                .Step("Trunk and takeoffs", "An 8x12 trunk runs from the plenum to an end cap; a takeoff collar is cut into the trunk for each branch.",
                      "Duct layout: LMB points · Takeoff punch: LMB on the trunk", "h_02_trunk")
                .Step("Branches and registers", "6\" round pipe from each takeoff to a boot and register in each room. Every joint gets UL 181 foil tape.",
                      "Duct layout · Tin snips · Foil tape: hold LMB on each joint", "h_03_takeoff_boot")
                .Step("Return air", "A return grille brings air back to the return drop so the system isn't starved.",
                      "Duct layout to the return drop", "h_04_registers")
                .Step("Line set and condensate", "Copper line set from the condenser to the coil, and a condensate drain sloped to a drain.",
                      "Duct layout: line set and condensate", "h_05_condenser")
                .Step("Bath fan", "Bath fans must exhaust outside through a wall or roof cap, never into the attic.",
                      "Duct layout to the roof cap · foil tape", "h_06_bathfan");
        }

        static void Inspections()
        {
            Tutorials.New("insp-framing", INSP, "Framing and rough-in inspections", "Run the inspection, read the flags, fix them.")
                .Step("Framing inspection", "The framing inspection checks every member is supported down to the foundation, spacing, headers and connectors. Red flags are structural; yellow are corrections.",
                      "I run the framing inspection · aim at a flag to read it", "i_01_framing_flags")
                .Step("Rough MEP inspection", "Plumbing, electrical and HVAC each have a rough inspection: joints made, tests passed, studs not over-bored, cables stapled. Red markers show exactly where.",
                      "MEP Plans app: Inspect", "i_02_mep_markers", "MEP Plans");
            Tutorials.New("insp-schedule", INSP, "Scheduling inspections", "Footing, under-slab, framing, rough MEP, insulation, final.", false, "site-career (M6)")
                .Step("Call the inspector", "Each phase has to pass before you cover it up: footing before the pour, framing and rough MEP before insulation, insulation before drywall, and the final before move-in.",
                      "Inspections app: schedule the next phase", "i_03_schedule")
                .Step("Pass or fix", "A failed inspection lists every issue with a marker. Fix them and reschedule.",
                      "Aim at a red marker to read it", "i_04_failed");
        }

        static void Later()
        {
            Tutorials.New("fin-insulation", FIN, "Insulation", "Batts in the walls, blown-in in the attic.", false, "close-up (M5)")
                .Step("Wall batts", "Friction-fit R-13 (2x4) or R-21 (2x6) batts, split around wires and pipes, no gaps or compression.", "Insulation tool: LMB a stud bay", "fin_01_batts")
                .Step("Attic", "Baffles at the eaves to keep the soffit vents open, then blow in to the depth on the rulers.", "Insulation blower: hold LMB", "fin_02_attic");
            Tutorials.New("fin-drywall", FIN, "Drywall", "Hang, tape, mud and sand.", false, "close-up (M5)")
                .Step("Hang", "Ceilings first, then walls, sheets horizontal with staggered joints, screwed every 12\" into the framing.", "Drywall screw gun: LMB on each sheet", "fin_03_hang")
                .Step("Tape and mud", "Tape every seam, three coats of mud feathered wider each time, corner bead on outside corners, then sand.", "Taping knife: hold LMB on a seam", "fin_04_mud");
            Tutorials.New("fin-siding", FIN, "Siding and trim outside", "Housewrap, siding, corners and soffit.", false, "close-up (M5)")
                .Step("Housewrap", "Wrap the walls, lapped shingle-style, taped at the seams and folded into the openings.", "Cap stapler on each course", "fin_05_housewrap")
                .Step("Siding", "Start strip at the bottom, courses level, corners and J-channel at the openings.", "Siding nailer on each course", "fin_06_siding");
            Tutorials.New("fin-interior", FIN, "Interior finish", "Doors, trim, cabinets, flooring, paint.", false, "close-up / finish (M5)")
                .Step("Hang interior doors", "Set prehung doors plumb on shims, nail the jambs, then case both sides with a 3/16\" reveal.", "Finish nailer · miter saw · level", "fin_07_door")
                .Step("Cabinets and counters", "Cabinets go in before the flooring: wall cabinets first, then bases, level and shimmed, then countertops and backsplash.", "Cabinet install from the plans", "fin_08_cabinets")
                .Step("Flooring", "Tile on backer and thinset, grout after 24 h; hardwood or LVP with an expansion gap at the walls.", "Flooring tools", "fin_09_flooring");
            Tutorials.New("biz-landscape", BIZ, "Landscaping and grading", "Grade away from the house, then lawn and plants.", false, "site-career (M6)")
                .Step("Final grade", "The ground must slope away from the foundation (6\" in the first 10') so water runs off.", "Shovel / skid steer: grade", "biz_01_grade")
                .Step("Lawn and plants", "Topsoil, seed or sod, and plantings.", "Landscaping tools", "biz_02_lawn");
            Tutorials.New("biz-save", BIZ, "Saving and loading", "Keep your progress.", false, "site-career (M6)")
                .Step("Save", "Save from the tablet; every piece, your stock and your belt are saved.", "Save app: Save / Load", "biz_03_save");
            Tutorials.New("biz-career", BIZ, "Career mode", "Bid jobs, build to the contract, get paid.", false, "site-career (M6)")
                .Step("Take a job", "Pick a lot and a contract. Budget the materials and the schedule, pass every inspection, and get paid at the final.", "Career app", "biz_04_career");
        }
    }
}
