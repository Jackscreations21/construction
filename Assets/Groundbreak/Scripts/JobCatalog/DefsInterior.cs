using C = Groundbreak.RepairCatalog;

namespace Groundbreak
{
    // Service calls: carpentry, drywall and paint, flooring, tile, cabinets. Format: Docs/JOBS.md.
    public static partial class JobCatalog
    {
        static void DefineCarpentry()
        {
            Svc("carp.rotted-sill", "Rotted window sill", Hm, JobTrade.Carpentry, JobTrade.Exterior).At(JobPlaces.WindowSill).Model("sill", 1.3f, 0.2f, 0.2f).Rep(15).Boost(rain: 1.3f)
                .Says("The wood under the front window is soft and the paint is peeling off it.", "I pushed a screwdriver into the window sill and it went right in.")
                .Cause("surface", "Surface rot, small area", 5, 1.5f, "The screwdriver sinks 1/4\" in one spot; the rest is solid. Dig it out and fill with epoxy.", "dig", "epoxy", "sand")
                .Cause("deep", "Sill nose rotted through", 3, 3f, "Soft along half the nose, deep into the wood: cut out the sill nose and replace it with PVC.", "cut-out", "new-sill")
                .Diag("probe", "Probe the sill with the screwdriver along its length", C.Screwdriver, 3f)
                .Fix("dig", "Dig out the soft wood back to solid fibers and let it dry", C.Knife, 4f).Wait(30f)
                .Fix("epoxy", "Prime with consolidant and pack in two-part epoxy filler, shape it", C.Knives, 5f).After("dig").Dry().Mat(JP.Epoxy).Wait(60f)
                .Fix("sand", "Sand the epoxy flush", C.Sander, 3f).After("epoxy").Dry()
                .Fix("cut-out", "Cut out the sill nose with the multi-tool", JP.MultiTool, 5f)
                .Fix("new-sill", "Scarf in a PVC sill nose with PVC cement and stainless screws", C.Saw, 6f).After("cut-out").Mat(JP.PvcSill).Mat(JP.ExtScrews, 6)
                .Finish("caulk", "Caulk the joints to the window and the siding", C.CaulkGun, 3f).Mat(C.CaulkExt)
                .Finish("paint", "Prime and paint the sill", C.Brush, 4f).Mat(C.Primer).Mat(C.Paint)
                .Finish("test", "Probe again and check the drip edge sheds water", C.Screwdriver, 2f).Test();

            Svc("carp.door-trim-rot", "Rotted door trim at the bottom", Hm, JobTrade.Carpentry, JobTrade.Exterior).At(JobPlaces.EntryTrim).Model("brickmould", 1.2f, 2.2f, 0.2f).Rep(15)
                .Says("The trim around the front door is rotting at the bottom and the paint is bubbling.")
                .Cause("trim", "Rotted brickmould only", 5, 2f, "The bottom 12\" of the brickmould is soft; the jamb behind it is solid.", "cut", "dutchman")
                .Cause("jamb", "Rot into the jamb", 3, 3.5f, "The brickmould and the bottom of the jamb are soft: cut both back and splice in PVC.", "cut", "jamb", "dutchman")
                .Diag("probe", "Probe the trim and the jamb with the screwdriver", C.Screwdriver, 3f)
                .Fix("cut", "Square-cut the rotted brickmould above the rot with the multi-tool", JP.MultiTool, 4f)
                .Fix("jamb", "Cut back the jamb and glue/screw in a PVC jamb splice", JP.MultiTool, 6f).After("cut").Mat(JP.PvcTrim).Mat(JP.ExtScrews, 4)
                .Fix("dutchman", "Fit a PVC dutchman with a scarf joint, glue and nail", "finish-nailer", 5f).After("cut").Mat(JP.PvcTrim).Mat(JP.FinishNails, 12)
                .Finish("caulk", "Caulk the joints", C.CaulkGun, 3f).Mat(C.CaulkExt)
                .Finish("paint", "Prime and paint to match", C.Brush, 4f).Mat(C.Primer).Mat(C.Paint)
                .Finish("test", "Check the joint is tight and the door closes", null, 2f).Test();

            Svc("carp.door-sag", "Door sags and rubs at the top", Hm, JobTrade.Carpentry).At(JobPlaces.BedroomDoor).Model("doorhw", 0.9f, 2.1f, 0.2f)
                .Says("The bedroom door scrapes the top of the frame and won't latch.", "The door hangs crooked and you have to lift it to close it.")
                .Cause("screws", "Stripped top hinge screws", 6, 0.5f, "The top hinge leaf moves on the jamb: its screws are stripped in the jamb.", "screws")
                .Cause("hinge", "Bent hinge", 2, 0.75f, "The top hinge knuckle is bent and the pin is worn.", "hinge")
                .Cause("strike", "Strike out of line", 2, 0.75f, "The door hangs square; the latch hits low on the strike after the house settled.", "strike")
                .Diag("look", "Check the reveal at the head, the hinge screws and where the latch hits", C.Screwdriver, 3f)
                .Fix("screws", "Drive 3\" screws through the top hinge into the jack stud", C.Drill, 3f).Mat(JP.HingeScrews, 2)
                .Fix("hinge", "Swap the top hinge for a new 3-1/2\" hinge", C.Screwdriver, 4f).Mat(JP.Hinge)
                .Fix("strike", "Move the strike plate down, chisel the mortise, fill the old holes", C.Hammer, 5f).Mat(JP.Strike).Mat(JP.Dowels)
                .Finish("test", "Open and close the door: even gap, latches without lifting", null, 2f).Test();

            Svc("carp.squeaky-floor", "Squeaky floor", Hm, JobTrade.Carpentry, JobTrade.Flooring).At(JobPlaces.BedroomFloor).Model("floorpatch", 1.2f, 0.05f, 1.2f).Rare(0.8f)
                .Says("The floor in the bedroom squeaks every time we walk to the closet.", "The floor squeaks loud enough to wake the baby.")
                .Cause("subfloor", "Subfloor loose on the joist", 6, 1f, "The squeak is right over a joist: the subfloor moves on the nails.", "screw")
                .Cause("gap", "Gap between joist and subfloor (from below)", 3, 1.5f, "Over a joist with a gap: shim from below and glue.", "shim-joist")
                .Diag("find", "Walk the floor, find the joist with the stud finder", JP.StudFinder, 3f)
                .Fix("screw", "Drive break-off squeak screws through the carpet/floor into the joist", C.Drill, 4f).Mat(JP.SqueakKit)
                .Fix("shim-joist", "From the crawlspace, glue shims into the gap and tap them home", C.Hammer, 5f).Mat(JP.Shims, 4).Mat(JP.Adhesive)
                .Finish("test", "Walk it again", null, 2f).Test();

            Svc("carp.stair-tread", "Cracked deck stair tread", Hm, JobTrade.Carpentry).At(JobPlaces.DeckStep).Model("deckstep", 1.1f, 0.3f, 0.6f)
                .Says("One of the steps off the back deck cracked and somebody almost fell.", "The deck step is split and wobbles.")
                .Cause("tread", "Split tread", 6, 1f, "The front tread board is split along the grain; the stringers are sound.", "tread-off", "tread-new")
                .Cause("stringer", "Loose stringer", 2, 1.5f, "The tread is fine; the stringer pulled away from the rim joist.", "stringer")
                .Diag("check", "Stand on it, probe the stringers", C.Screwdriver, 3f)
                .Fix("tread-off", "Back the screws out and pry off the split tread", C.PryBar, 3f)
                .Fix("tread-new", "Cut a 2x6 PT tread to length, ease the edges, screw 2 per stringer", C.Saw, 5f).After("tread-off").Mat(JP.TreadBoard).Mat(C.DeckScrews, 4)
                .Fix("stringer", "Pull the stringer tight and fasten with structural screws and a stringer tie", JP.ImpactDriver, 4f).Mat(JP.ExtScrews, 6)
                .Finish("test", "Bounce test", null, 2f).Test();

            Svc("carp.window-sash", "Window won't stay open / painted shut", Hm, JobTrade.Carpentry).At(JobPlaces.LivingWindow).Model("sash", 1.2f, 1.2f, 0.2f)
                .Says("The living room window won't stay up — it slams shut.", "We can't open the front window at all.")
                .Cause("balance", "Broken spiral balance", 5, 1.25f, "The sash drops; the left spiral balance is unhooked and the spring is broken.", "stops-off", "balance")
                .Cause("paint", "Painted shut", 4, 0.75f, "The sash is painted to the stop all around.", "cut-paint")
                .Diag("check", "Try the sash, look at the balances and the paint line", null, 3f)
                .Fix("cut-paint", "Cut the paint seal with the utility knife and tap the sash free", C.Knife, 4f)
                .Prep("stops-off", "Pry off the interior stop and tilt the sash out", C.PryBar, 3f).Opens()
                .Fix("balance", "Replace both spiral balances and hook the sash shoes", C.Screwdriver, 5f).Need("stops-off").Mat(JP.SashBalance)
                .Finish("stops-on", "Nail the stop back, caulk and touch up", "brad-nailer", 3f).Closes("stops-off")
                .Finish("test", "Raise the sash: it stays where you leave it", null, 2f).Test();

            Svc("carp.cabinet-door", "Cabinet door hangs crooked", Hm, JobTrade.Cabinets).At(JobPlaces.KitchenCabinet).Model("cabinet", 0.4f, 0.7f, 0.1f).Rare(0.8f)
                .Says("A kitchen cabinet door hangs crooked and won't close.", "The hinge pulled out of a cabinet door.")
                .Cause("adjust", "Hinge out of adjustment", 4, 0.3f, "Hinges are tight; the door just needs the side and depth screws adjusted.", "adjust")
                .Cause("stripped", "Stripped hinge screws", 4, 0.75f, "The top hinge screws spin in the frame: fill with glued dowels and redrive.", "dowel", "redrive")
                .Cause("hinge", "Broken hinge", 2, 0.5f, "The hinge arm is cracked.", "hinge")
                .Diag("check", "Open the door and check each hinge", C.Screwdriver, 2f)
                .Fix("adjust", "Adjust the side and depth screws until the gaps are even", C.Screwdriver, 3f)
                .Fix("dowel", "Glue dowels into the stripped holes and trim flush", C.Hammer, 3f).Mat(JP.Dowels).Mat(JP.WoodGlue).Wait(30f)
                .Fix("redrive", "Redrive the hinge screws and adjust", C.Screwdriver, 2f).After("dowel").Dry()
                .Fix("hinge", "Replace the concealed hinges", C.Screwdriver, 3f).Mat(JP.CabHinge)
                .Finish("test", "Close it: even 1/8\" gaps", null, 1f).Test();

            Svc("carp.drawer", "Kitchen drawer falls out", Hm, JobTrade.Cabinets).At(JobPlaces.KitchenCabinet).Model("cabinet", 0.4f, 0.9f, 0.2f).Rare(0.6f)
                .Says("A kitchen drawer drags and fell off its track.")
                .Cause("slides", "Worn drawer slides", 6, 1f, "The old epoxy-coated slides have bent wheels.", "slides")
                .Cause("box", "Drawer box came apart", 3, 0.75f, "The drawer front joint split; the slides are fine.", "glue")
                .Diag("check", "Pull the drawer and look at the slides and the box", null, 2f)
                .Fix("slides", "Install new ball-bearing slides on the cabinet and the box", C.Screwdriver, 5f).Mat(JP.Slides)
                .Fix("glue", "Glue and clamp the box joint, pin nail it", "brad-nailer", 4f).Mat(JP.WoodGlue).Mat(JP.FinishNails, 6)
                .Finish("test", "Open and close it loaded", null, 1f).Test();

            Svc("carp.baseboard", "Loose baseboard", Hm, JobTrade.Carpentry).At(JobPlaces.BedroomBase).Model("baseboard", 1.5f, 0.15f, 0.05f).Rare(0.6f)
                .Says("The baseboard in the bedroom is pulling off the wall.")
                .Cause("nails", "Nails missed the studs", 6, 0.75f, "Every nail went into drywall only.", "nail")
                .Cause("swollen", "Water-damaged board", 3, 1.5f, "The bottom edge is swollen from a spill: replace the length.", "cut", "nail")
                .Diag("check", "Find the studs and look at the board", JP.StudFinder, 2f)
                .Fix("cut", "Cut in a new length with a scarf joint", "miter-saw", 4f).Mat(JP.Base)
                .Fix("nail", "Nail into the studs and the bottom plate", "finish-nailer", 3f).Mat(JP.FinishNails, 12)
                .Finish("caulk", "Fill holes, caulk the top, touch up", C.CaulkGun, 3f).Mat(JP.Caulk)
                .Finish("test", "Push on it", null, 1f).Test();
        }

        static void DefineDrywall()
        {
            Svc("dw.ceiling-stain", "Water stain on the ceiling", Hm, JobTrade.Drywall, JobTrade.Painting).At(JobPlaces.BedroomCeilingFront).Model("ceilingstain", 0.6f, 0.1f, 0.6f).Boost(rain: 2f).Rep(10)
                .Says("There's a big brown water stain on the bedroom ceiling.", "A stain showed up on the ceiling after the last storm and it's getting bigger.")
                .Cause("old", "Old dry stain (leak already fixed)", 4, 1f, "Moisture meter reads dry (8%); the roof was fixed last year. Seal and paint.", "prime", "paint-ceiling")
                .Cause("roof", "Active roof leak above", 3, 2f, "The stain reads wet (30%) and the attic decking above is wet: the nail pops on the roof need sealing before the ceiling is touched.", "roof-seal", "prime", "paint-ceiling")
                .Cause("sag", "Soaked drywall sagging", 2, 3f, "The drywall is soft and sagging: cut it out, dry it, patch.", "cut-open", "patch", "prime", "paint-ceiling")
                .Diag("meter", "Check the stain with the moisture meter and look in the attic", C.Meter, 3f)
                .Fix("roof-seal", "Seal the leak on the roof above (cement the nail heads, replace the cracked shingle)", C.CaulkGun, 5f).Mat(C.RoofCement)
                .Fix("cut-open", "Cut out the wet drywall to the joists", C.JabSaw, 4f).Opens()
                .Fix("patch", "Screw in a drywall patch on backers, tape and mud three coats", C.Knives, 6f).Need("cut-open").Mat(C.PatchPanel).Mat(C.Compound).Mat(C.DwTape, 8).Wait(120f)
                .Finish("close", "Sand the patch smooth", C.Sander, 3f).Closes("cut-open").Dry()
                .Fix("prime", "Spot-prime the stain with shellac stain blocker", C.Brush, 3f).Mat(JP.StainBlock).Wait(30f)
                .Fix("paint-ceiling", "Roll the whole ceiling with flat ceiling paint (spot paint flashes)", C.Roller, 6f).After("prime").Dry().Mat(JP.CeilingPaint)
                .Finish("clean", "Pick up the drop cloths", null, 1f)
                .Finish("test", "Look at the ceiling in raking light", null, 2f).Test();

            Svc("dw.wall-crack", "Crack in the wall", Hm, JobTrade.Drywall, JobTrade.Painting).At(JobPlaces.LivingWall).Model("wallcrack", 1.2f, 1.2f, 0.05f)
                .Says("There's a crack running from the corner of the doorway up toward the ceiling.", "A crack keeps coming back in the living room wall.")
                .Cause("tape", "Failed paper tape joint", 5, 2f, "The crack follows a taped seam; the tape is loose and bubbled.", "cut", "tape", "coats")
                .Cause("settle", "Seasonal settling crack", 4, 2.5f, "A diagonal crack from the corner of the opening: normal settling. Use mesh tape and setting compound so it stays closed.", "cut", "tape", "coats")
                .Diag("look", "Look at the crack, tap the tape, check the doorway corner", C.Knife, 2f)
                .Fix("cut", "V-cut the crack and cut away loose tape", C.Knife, 3f)
                .Fix("tape", "Bed new tape in setting compound", C.Knives, 4f).After("cut").Mat(C.DwTape, 4).Mat(C.Compound).Wait(60f)
                .Fix("coats", "Two finish coats, feathered 10\" wide, sand between", C.Knives, 5f).After("tape").Dry().Mat(C.Compound).Wait(90f)
                .Fix("paint", "Prime and paint the wall corner to corner", C.Roller, 5f).After("coats").Dry().Mat(C.Primer).Mat(C.Paint)
                .Finish("test", "Check it in raking light", null, 1f).Test();

            Svc("dw.nail-pops", "Nail pops in the drywall", Hm, JobTrade.Drywall, JobTrade.Painting).At(JobPlaces.BedroomWall).Model("wallcrack", 1.2f, 1.6f, 0.05f).Rare(0.9f)
                .Says("Little round bumps keep popping out of the bedroom wall.", "There are dimples and bumps all over one wall.")
                .Cause("nails", "Popped nails", 7, 1.25f, "The nail heads pushed through the mud as the studs dried.", "reset", "spot", "paint")
                .Cause("loose", "Loose board", 3, 1.5f, "The whole board moves: add screws along every stud.", "reset", "spot", "paint")
                .Diag("check", "Press around each pop, find the studs", JP.StudFinder, 2f)
                .Fix("reset", "Drive a drywall screw 1-1/2\" above and below each pop, set the nail", C.Drill, 4f).Mat(JP.DwScrewsBox, 12)
                .Fix("spot", "Spot the screws and nails with three coats of compound", C.Knives, 4f).After("reset").Mat(C.Compound).Wait(90f)
                .Fix("paint", "Sand, prime the spots and repaint the wall", C.Roller, 5f).After("spot").Dry().Mat(C.Primer).Mat(C.Paint)
                .Finish("test", "Raking light check", null, 1f).Test();

            Svc("dw.repaint-room", "Repaint a room", Hm, JobTrade.Painting).At(JobPlaces.LivingWallFull).Model("wallpaint", 3.6f, 2.4f, 0.05f).Rare(0.9f)
                .Says("We'd like the living room repainted — the walls are scuffed and we picked a new color.")
                .Cause("standard", "Walls in fair shape", 6, 6f, "Scuffs and a few nail holes; the old paint is sound eggshell.", "prep", "cut-in", "coat1", "coat2")
                .Cause("glossy", "Glossy oil paint underneath", 3, 8f, "The old paint is glossy oil: it needs a bonding primer first or the latex will peel.", "prep", "prime-all", "cut-in", "coat1", "coat2")
                .Diag("probe", "Rub the wall with a rag and alcohol to check what the old paint is", null, 2f)
                .Prep("covered", "Move furniture, lay drop cloths, mask trim and outlets", null, 4f).Opens().Mat(JP.DropCloth).Mat(JP.Tape)
                .Fix("prep", "Fill nail holes, sand, wipe down", C.Knives, 4f).Need("covered").Mat(C.Compound)
                .Fix("prime-all", "Roll on a bonding primer", C.Roller, 6f).Need("covered").After("prep").Mat(C.Primer, 3).Wait(60f)
                .Fix("cut-in", "Cut in at the ceiling, corners and trim", C.Brush, 5f).Need("covered").After("prep").Dry().Mat(JP.PaintGal)
                .Fix("coat1", "Roll the first coat", C.Roller, 6f).Need("covered").After("cut-in").Mat(JP.RollerCover).Wait(120f)
                .Fix("coat2", "Roll the second coat", C.Roller, 6f).Need("covered").After("coat1").Dry().Mat(JP.PaintGal)
                .Finish("uncover", "Pull tape, pick up the drop cloths, put the room back", null, 3f).Closes("covered")
                .Finish("test", "Walk-through with the customer", null, 2f).Test();

            Svc("dw.corner-bead", "Dented outside corner", Hm, JobTrade.Drywall).At(JobPlaces.LivingCorner).Model("cornerbead", 0.1f, 1.2f, 0.05f).Rare(0.6f)
                .Says("The vacuum cleaner smashed the corner of the wall by the hallway.")
                .Cause("dent", "Dented metal bead", 6, 1.5f, "The metal corner bead is dented and the mud cracked off it.", "cut-bead", "bead-new", "mud")
                .Cause("chip", "Chipped mud only", 3, 1f, "The bead is straight; only the mud chipped.", "mud")
                .Diag("check", "Sight down the corner with a straightedge", C.Level, 2f)
                .Fix("cut-bead", "Cut out the damaged bead section", JP.MultiTool, 3f)
                .Fix("bead-new", "Fasten a new length of bead", C.Hammer, 3f).After("cut-bead").Mat(JP.Bead)
                .Fix("mud", "Three coats of compound, sand, prime and paint", C.Knives, 5f).Mat(C.Compound).Mat(C.Paint).Wait(90f)
                .Finish("test", "Sight the corner", null, 1f).Test();
        }

        static void DefineFlooring()
        {
            Svc("flr.cracked-tile", "Cracked floor tile", Hm, JobTrade.Tile, JobTrade.Flooring).At(JobPlaces.BathFloor).Model("floorpatch", 0.95f, 0.05f, 0.95f).Rep(15)
                .Says("A floor tile in the bathroom is cracked.", "One of the bathroom tiles cracked and it clicks when you step on it.")
                .Cause("impact", "Impact crack, good bond", 5, 2f, "Only one tile is hollow; something heavy was dropped on it.", "grout-out", "tile-out", "set", "grout")
                .Cause("hollow", "Poor thinset coverage", 3, 2.5f, "The tile and its neighbor sound hollow: lift both, back-butter, reset.", "grout-out", "tile-out", "set", "grout")
                .Diag("tap", "Tap the tiles with a screwdriver handle for hollow spots", C.Screwdriver, 2f)
                .Fix("grout-out", "Rake the grout out around the tile", JP.GroutSaw, 4f)
                .Fix("tile-out", "Score an X, chisel the broken tile and old thinset out", JP.ColdChisel, 5f).After("grout-out").Opens()
                .Fix("set", "Back-butter the new tile with thinset and set it level", "notched-trowel", 4f).Need("tile-out").Mat(JP.FloorTile).Mat(JP.Thinset).Wait(240f)
                .Finish("close", "Clean the joints", null, 1f).Closes("tile-out")
                .Fix("grout", "Grout the joints and sponge clean", "grout-float", 4f).After("set").Dry().Mat(JP.Grout)
                .Finish("test", "Tap test", C.Screwdriver, 1f).Test();

            Svc("flr.regrout-tub", "Moldy, cracked shower grout", Hm, JobTrade.Tile).At(JobPlaces.TubWall).Model("groutwall", 1.0f, 1.0f, 0.05f).Rep(20)
                .Says("The grout in the tub surround is black and cracked.", "Water gets behind the tile in the shower — there's a soft spot in the wall.")
                .Cause("grout", "Worn grout", 6, 3f, "The grout is crumbling but the tiles are tight.", "rake", "grout", "seal")
                .Cause("leak", "Water behind the tile at the tub joint", 3, 4f, "The bottom row is loose and the joint at the tub was grouted, not caulked.", "rake", "reset", "grout", "seal")
                .Diag("tap", "Press on the tiles and check the joint at the tub", null, 2f)
                .Fix("rake", "Rake out the old grout with the grout saw/multi-tool", JP.GroutSaw, 6f)
                .Fix("reset", "Reset the loose bottom row with thinset", "notched-trowel", 5f).After("rake").Mat(JP.Thinset).Wait(240f)
                .Fix("grout", "Grout the joints, caulk the tub joint with silicone", "grout-float", 5f).After("rake").Dry().Mat(JP.Grout).Mat(C.CaulkSilicone).Wait(180f)
                .Fix("seal", "Seal the grout", C.Brush, 3f).After("grout").Dry().Mat(JP.GroutSealer)
                .Finish("test", "Spray the walls and look for weeping joints", C.Hose, 2f).Test();

            Svc("flr.lvp-plank", "Plank floor lifting", Hm, JobTrade.Flooring).At(JobPlaces.LivingFloor).Model("floorpatch", 1.2f, 0.05f, 1.2f).Rare(0.7f)
                .Says("The vinyl plank floor buckled near the front door.", "A plank in the living room floor lifted up and there's a gap.")
                .Cause("expansion", "No expansion gap at the wall", 5, 2f, "The planks are tight to the wall: no 1/4\" gap. Trim the edge and re-click.", "base-off", "trim", "relay")
                .Cause("damaged", "Damaged plank", 3, 2f, "One plank's locking edge is broken.", "base-off", "relay")
                .Diag("check", "Pull the baseboard corner and look at the edge", C.PryBar, 2f)
                .Prep("base-off", "Pull the baseboard on that wall", C.PryBar, 3f).Opens()
                .Fix("trim", "Undo the row and trim 1/4\" off the wall edge", JP.MultiTool, 4f).Need("base-off")
                .Fix("relay", "Re-click the planks with a new plank where needed", "pull-bar", 5f).Need("base-off").Mat(JP.LvpBox)
                .Finish("base-on", "Nail the baseboard back on", "finish-nailer", 3f).Closes("base-off").Mat(JP.FinishNails, 12)
                .Finish("test", "Walk the floor", null, 1f).Test();

            Svc("flr.transition", "Loose floor transition strip", Hm, JobTrade.Flooring).At(JobPlaces.BedroomDoor).Model("transition", 0.9f, 0.05f, 0.1f).Rare(0.6f)
                .Says("The metal strip at the bedroom doorway came loose and is a trip hazard.")
                .Cause("track", "Track screws pulled", 6, 0.5f, "The T-molding track's screws pulled out of the slab anchors.", "track")
                .Cause("broken", "Broken molding", 3, 0.75f, "The T-molding is cracked: replace it.", "track", "molding")
                .Diag("check", "Lift the strip and look at the track", null, 1f)
                .Fix("track", "Re-anchor the track with construction adhesive and screws", C.Drill, 3f).Mat(JP.Adhesive)
                .Fix("molding", "Cut and snap in a new T-molding", "miter-saw", 3f).Mat(JP.TMold)
                .Finish("test", "Step on it", null, 1f).Test();
        }
    }
}
