using C = Groundbreak.RepairCatalog;

namespace Groundbreak
{
    // Service calls: siding, trim, roofing, gutters, decks, fences, concrete and masonry. Format: Docs/JOBS.md.
    public static partial class JobCatalog
    {
        const Seasons NotWinter = Seasons.Spring | Seasons.Summer | Seasons.Fall;

        static void DefineExterior()
        {
            Svc("ext.siding", "Damaged vinyl siding", Hm, JobTrade.Exterior).At(JobPlaces.FrontSiding).Model("siding", 0.8f, 1.2f, 0.1f).Rep(10).In(NotWinter)
                .Says("The lawn mower threw a rock and cracked the siding by the front window.", "A piece of siding blew loose in the wind and is flapping.")
                .Cause("cracked", "Cracked panel", 5, 1.25f, "One course is cracked through; the wrap behind it is intact.", "panel-off", "panel-new")
                .Cause("unlocked", "Unlocked panel", 4, 0.5f, "The panel's lock unhooked from the course below in the wind; it's not damaged.", "relock")
                .Diag("look", "Look at the panel and its lock", null, 2f)
                .Fix("relock", "Hook the zip tool under the panel and snap it back into the lock", "siding-zip-tool", 2f)
                .Fix("panel-off", "Unzip the course above, pull the nails, remove the cracked panel", "siding-zip-tool", 4f).Opens()
                .Fix("panel-new", "Hang a matched panel: nails centered in the slots, 1/32\" loose, lock it in", C.Hammer, 5f).Need("panel-off").Mat(JP.VinylPanel).Mat(JP.SidingNails, 10)
                .Finish("zip", "Zip the course above back down", "siding-zip-tool", 2f).Closes("panel-off")
                .Finish("test", "Panels slide freely side to side and are locked", null, 1f).Test();

            Svc("ext.fascia", "Rotted fascia board", Hm, JobTrade.Exterior, JobTrade.Carpentry).At(JobPlaces.Fascia).Model("fascia", 2.4f, 0.3f, 0.3f).Rep(30).In(NotWinter)
                .Says("The board behind the gutter is rotting and the gutter is pulling away.", "Paint is peeling off the trim board under the roof and it's soft.")
                .Cause("fascia", "Rotted fascia only", 5, 3f, "The fascia is soft for 6 feet; rafter tails behind are solid.", "gutter-off", "cut-out", "fascia-new")
                .Cause("drip", "Missing drip edge", 3, 3.5f, "Water runs behind the gutter because there is no drip edge; the fascia is rotted.", "gutter-off", "cut-out", "fascia-new", "drip")
                .Diag("probe", "Probe the fascia and look under the shingle edge for drip edge", C.Screwdriver, 3f)
                .Prep("gutter-off", "Unscrew the hangers and lower the gutter section", C.Drill, 4f).Opens()
                .Fix("cut-out", "Pull the rotted fascia off the rafter tails", C.PryBar, 4f).Need("gutter-off")
                .Fix("fascia-new", "Cut and nail new primed fascia to each rafter tail, scarf the joint", C.Saw, 6f).After("cut-out").Mat(JP.Fascia).Mat(JP.FinishNails, 20)
                .Fix("drip", "Slide drip edge under the shingle and over the fascia", "ext-snips", 4f).After("fascia-new").Mat(JP.ZFlash)
                .Finish("gutter-on", "Rehang the gutter with hidden hangers every 24\", 1/4\" per 10' slope", C.Drill, 4f).Closes("gutter-off").Mat(C.Hanger, 4)
                .Finish("paint", "Caulk and paint the fascia", C.Brush, 4f).Mat(C.Paint)
                .Finish("test", "Run a hose in the gutter", C.Hose, 2f).Test();

            Svc("ext.skylight", "Skylight leaks", Hm, JobTrade.Roofing).At(JobPlaces.RoofSkylight).Model("skylight", 1.2f, 0.3f, 1.4f).Rep(45).Boost(rain: 2.5f).In(NotWinter)
                .Says("Water drips from around the skylight every time it rains hard.", "There's a stain spreading from the corner of the skylight well.")
                .Cause("flash", "Missing step flashing piece", 5, 2.5f, "A step flashing piece on the left side is missing and the felt is stained.", "tear-back", "flash-new", "reshingle")
                .Cause("seal", "Failed glazing seal", 3, 1.5f, "Flashing is good; the seal along the bottom of the glass is cracked.", "reseal")
                .Diag("hose", "Run a hose over the skylight bottom-up while someone watches inside", C.Hose, 4f)
                .Fix("reseal", "Clean and reseal the glazing joint", C.CaulkGun, 3f).Mat(C.CaulkSilicone)
                .Fix("tear-back", "Break the seals and pull the shingles beside the curb", C.FlatBar, 4f).Opens()
                .Fix("flash-new", "Weave in new step flashing, one per course", C.Hammer, 5f).Need("tear-back").Mat(JP.SkyFlash)
                .Fix("reshingle", "Reshingle the course and seal the tabs", "hatchet", 5f).After("flash-new").Mat(C.Shingles, 6).Mat(C.RoofNails, 24)
                .Finish("close", "Clean up the roof", null, 1f).Closes("tear-back")
                .Finish("test", "Hose test again", C.Hose, 2f).Test();

            Svc("ext.roof-leak", "Roof leak", Hm, JobTrade.Roofing).At(JobPlaces.Roof).Model("roofpatch", 1.2f, 0.3f, 1.0f).Rep(40).Boost(rain: 3f).In(NotWinter)
                .Says("The roof is leaking into the hallway when it rains.", "We have a drip coming through the ceiling light after storms.")
                .Cause("boot", "Cracked plumbing vent boot", 5, 1.5f, "The rubber collar on the plumbing vent boot is cracked open.", "tabs-up", "boot")
                .Cause("nailpop", "Popped nails", 3, 1f, "Two nails backed out and lifted the shingle above them.", "nails")
                .Cause("shingle", "Missing shingle", 3, 1.25f, "A shingle tab blew off and the mat underneath is exposed.", "tabs-up", "shingle")
                .Diag("look", "Inspect the roof plane above the stain", null, 3f)
                .Fix("nails", "Drive the nails back, add a new nail beside each, seal the heads", C.Hammer, 3f).Mat(C.RoofNails, 4).Mat(C.RoofCement)
                .Prep("tabs-up", "Break the seal strips and lift the tabs above", C.FlatBar, 3f).Opens()
                .Fix("boot", "Pull the old boot, slide on a new one under the upper shingles", C.FlatBar, 5f).Need("tabs-up").Mat(JP.PipeBoot).Mat(C.RoofNails, 6)
                .Fix("shingle", "Slide in a new shingle, nail under the course above", C.Hammer, 4f).Need("tabs-up").Mat(C.Shingles, 3).Mat(C.RoofNails, 8)
                .Finish("seal", "Press the tabs down and seal them with roofing cement", C.CaulkGun, 3f).Closes("tabs-up").Mat(C.RoofCement)
                .Finish("test", "Hose test above the leak", C.Hose, 2f).Test();

            Svc("ext.gutter-leak", "Gutter leaking at a seam / downspout", Hm, JobTrade.Exterior).At(JobPlaces.Gutter).Model("gutter", 2.4f, 0.3f, 0.2f).Rep(25).Boost(rain: 1.8f)
                .Says("The gutter drips at a seam and the downspout came apart.", "Water pours over the gutter in the middle during rain.")
                .Cause("seam", "Leaking seam", 4, 1f, "The end seam's sealant is cracked and the rivets are loose.", "seam")
                .Cause("elbow", "Disconnected downspout elbow", 4, 0.75f, "The bottom elbow has fallen off; the downspout dumps against the foundation.", "elbow")
                .Cause("pitch", "Wrong pitch, water pools", 2, 1.75f, "Water stands mid-run: the gutter sags between hangers.", "rehang")
                .Diag("hose", "Run the hose in the gutter and watch", C.Hose, 3f)
                .Fix("seam", "Clean the seam, re-rivet and seal with gutter sealant", JP.Rivet, 4f).Mat(JP.GutterSeal)
                .Fix("elbow", "Fit a new elbow and extension with zip screws", C.Drill, 3f).Mat(JP.Elbow).Mat(JP.ZipScrews, 4)
                .Fix("rehang", "Snap a line at 1/4\" per 10', reset the hangers", C.Drill, 6f).Mat(C.Hanger, 4)
                .Finish("test", "Hose test", C.Hose, 2f).Test();

            Svc("ext.deck-ledger", "Deck pulling away from the house", Hm, JobTrade.Carpentry).At(JobPlaces.DeckLedger).Model("ledger", 3.6f, 0.3f, 0.2f).Rep(35).In(NotWinter)
                .Says("There's a gap opening between the deck and the house.", "The deck feels bouncy next to the back door.")
                .Cause("lags", "Undersized / loose ledger fasteners", 5, 2f, "The ledger is held with nails only and has moved 1/2\" off the rim.", "lag")
                .Cause("rot", "Rotted ledger, no flashing", 3, 6f, "No flashing over the ledger; the ledger and the rim behind it are soft at one end.", "shore", "flash", "lag")
                .Diag("check", "Look under the deck at the ledger, its fasteners and flashing", C.Screwdriver, 3f)
                .Fix("shore", "Shore the deck with a temporary beam and posts, replace the rotted ledger section", C.Saw, 8f).Mat(C.Stud, 2).Mat(ExteriorLedgerBoard)
                .Fix("flash", "Install Z-flashing over the ledger, tucked behind the siding", "ext-snips", 4f).Mat(JP.ZFlash)
                .Fix("lag", "Pull it tight and drive structural ledger screws 16\" O.C., staggered", JP.ImpactDriver, 5f).Mat(JP.Lag, 12)
                .Finish("test", "Bounce test at the house", null, 2f).Test();

            Svc("ext.fence-section", "Fence section leaning", Hm, JobTrade.Carpentry).At(JobPlaces.FenceBay).Model("fence", 2.6f, 1.9f, 0.2f).Rep(10).In(NotWinter)
                .Says("A section of our fence is leaning after the storm.", "The fence rail broke and the pickets are hanging.")
                .Cause("post", "Rotted post at grade", 5, 3f, "The post snapped off at the ground line: rotted.", "post-out", "post-set", "concrete")
                .Cause("rail", "Broken rail", 4, 1f, "The top rail cracked at a knot; the posts are sound.", "rail-new")
                .Diag("check", "Push the posts and look at the rails", null, 2f)
                .Fix("rail-new", "Cut a new rail and screw it to the posts with rail brackets", C.Saw, 4f).Mat(JP.Rail2x4).Mat(JP.ExtScrews, 6)
                .Fix("post-out", "Unscrew the rails, dig out the broken post and its old footing", "posthole", 6f)
                .Fix("post-set", "Set a new PT post 24\" deep, plumb both ways", C.Level, 4f).After("post-out").Mat(JP.Post4x4)
                .Fix("concrete", "Fill with fast-setting post concrete and water, reattach the rails", "posthole", 4f).After("post-set").Mat(JP.PostMix, 2).Wait(30f)
                .Finish("test", "Push on it", null, 1f).Test();

            Svc("ext.gate-sag", "Gate sags and won't latch", Hm, JobTrade.Carpentry).At(JobPlaces.FenceGate).Model("gate", 1.4f, 1.9f, 0.2f).In(NotWinter)
                .Says("The back gate drags on the ground and won't latch.")
                .Cause("brace", "No diagonal brace", 5, 1f, "The gate has no diagonal; it racked down at the latch side.", "kit")
                .Cause("hinge", "Bent hinges / loose screws", 3, 1f, "The hinge screws pulled out of the post.", "hinges")
                .Cause("post", "Leaning hinge post", 2, 3f, "The hinge post leans 2\" out of plumb.", "post")
                .Diag("check", "Lift the latch side and watch the hinges and the post", C.Level, 2f)
                .Fix("kit", "Install an anti-sag cable kit from the top hinge corner to the bottom latch corner", C.Drill, 4f).Mat(JP.GateKit)
                .Fix("hinges", "Replace with heavy T-hinges on through-bolts", C.Drill, 4f).Mat(JP.GateHinge)
                .Fix("post", "Dig out and reset the hinge post plumb in post concrete", "posthole", 7f).Mat(JP.PostMix, 2).Mat(JP.Post4x4)
                .Finish("test", "Swing it: clears the ground, latches", null, 1f).Test();

            Svc("ext.soffit", "Animals in the soffit", Hm, JobTrade.Exterior).At(JobPlaces.Soffit).Model("soffit", 1.2f, 0.1f, 0.3f).Rep(25).Rare(0.7f)
                .Says("Birds (or squirrels?) are getting in under the roof edge — we hear scratching.")
                .Cause("hole", "Chewed soffit panel", 5, 1.5f, "A panel is chewed open; droppings in the soffit.", "panel")
                .Cause("gap", "Gap at the fascia", 3, 1f, "The panel dropped out of its J-channel and left a gap.", "screen", "panel")
                .Diag("look", "Inspect the soffit from the ladder", null, 2f)
                .Fix("screen", "Close the gap with hardware cloth", "staple-gun", 3f).Mat(JP.HardwareCloth)
                .Fix("panel", "Replace the soffit panel and lock it in the channel", "ext-snips", 4f).Mat(JP.SoffitPanel)
                .Finish("test", "Check the panels are tight", null, 1f).Test();

            Svc("ext.porch-light", "Porch light doesn't work", Hm, JobTrade.Electrical).At(JobPlaces.PorchLight).Model("extlight", 0.25f, 0.4f, 0.25f)
                .Says("The light by the front door stopped working, new bulbs didn't help.", "The porch light fixture is rusted out — please put up a new one.")
                .Cause("fixture", "Corroded fixture", 6, 1f, "The socket is rusted and the fixture's wires are brittle.", "swap")
                .Cause("switch", "Bad switch", 3, 0.5f, "Power reaches the switch but not the fixture: worn switch.", "switch")
                .Diag("probe", "Test the socket and the switch", C.Tester, 3f)
                .PowerOff()
                .Fix("swap", "Install the new lantern with a gasket, caulk the top and sides", C.Screwdriver, 5f).Mat(JP.PorchLight).Mat(JP.WireNuts, 3).Mat(C.CaulkExt).Need("power-off").Guard("power-off", Zap)
                .Fix("switch", "Replace the switch", C.Screwdriver, 3f).Mat(JP.Switch).Need("power-off").Guard("power-off", Zap)
                .PowerOn()
                .Finish("test", "Switch it on", null, 1f).Test();
        }

        // ledger lumber: Exterior feature's 2x10 if present, else a PT 2x8
        static string ExteriorLedgerBoard => Catalog.Get("pt2x10x12") != null ? "pt2x10x12" : JP.TreadBoard;

        static void DefineMasonry()
        {
            Svc("mas.cracked-step", "Cracked front step", Hm, JobTrade.Concrete).At(JobPlaces.Stoop).Model("step", 1.9f, 0.18f, 1.2f).Rep(20).In(NotWinter)
                .Says("The front step is cracked and a chunk broke off the edge.")
                .Cause("edge", "Spalled edge", 6, 2f, "The front edge has a loose chunk; the slab is solid.", "chisel", "bond", "patch")
                .Cause("settled", "Settled crack", 3, 1.5f, "A hairline settlement crack; no loose pieces.", "seal")
                .Diag("tap", "Tap the edge with a hammer for hollow spots", C.Hammer, 2f)
                .Fix("seal", "Clean the crack and fill with self-leveling sealant", C.CaulkGun, 3f).Mat(JP.CrackSeal)
                .Fix("chisel", "Chip out the loose concrete back to sound, undercut the edges", JP.ColdChisel, 5f)
                .Fix("bond", "Brush on bonding adhesive", C.Brush, 2f).After("chisel").Mat(JP.Bonding)
                .Fix("patch", "Form the edge, pack vinyl patcher, trowel and broom to match", C.MagFloat, 5f).After("bond").Mat(JP.Patcher).Wait(120f)
                .Finish("test", "Check the edge is solid", C.Hammer, 1f).Test();

            Svc("mas.spalled-walk", "Flaking (spalled) sidewalk", Hm, JobTrade.Concrete).At(JobPlaces.Sidewalk).Model("slab", 1.52f, 0.05f, 1.52f).Rep(25).In(NotWinter)
                .Says("The city sent us a notice: the sidewalk in front of the house is flaking apart.")
                .Cause("salt", "Salt-scaled surface", 6, 3f, "The top 1/8\" scaled from de-icing salt; the slab is sound.", "clean", "resurface")
                .Cause("trip", "Lifted edge trip hazard", 3, 1.5f, "The joint has a 3/4\" lip from a tree root: grind it down.", "grind")
                .Diag("check", "Sound the slab and measure any lip", C.Hammer, 2f)
                .Fix("grind", "Grind the lip down to a smooth 1:8 bevel", JP.Grinder, 5f)
                .Fix("clean", "Pressure-clean and saturate the slab (SSD)", C.Hose, 4f)
                .Fix("resurface", "Squeegee on resurfacer, broom finish", C.Broom, 6f).After("clean").Mat(JP.Resurfacer, 2).Wait(240f)
                .Finish("test", "Walk it: no lip, even finish", null, 1f).Test();

            Svc("mas.tuckpoint", "Crumbling mortar joints", Hm, JobTrade.Masonry).At(JobPlaces.Planter).Model("planter", 1.9f, 0.7f, 0.3f).Rep(30).In(NotWinter)
                .Says("The mortar between the bricks on our planter is crumbling out.")
                .Cause("joints", "Weathered joints", 6, 3f, "The mortar is sandy and recessed 1/2\" in two courses.", "rake", "point")
                .Cause("brick", "Spalled brick too", 2, 4f, "One brick face is spalled: replace it and repoint.", "rake", "brick", "point")
                .Diag("scratch", "Scratch the joints with a key and sound the bricks", C.Screwdriver, 2f)
                .Fix("rake", "Grind the joints out 3/4\" deep", JP.Grinder, 6f)
                .Fix("brick", "Cut out and reset the spalled brick", JP.ColdChisel, 5f).After("rake").Mat(JP.MortarN)
                .Fix("point", "Wet the joints and pack Type N mortar in two lifts, tool when thumbprint-hard", "brick-trowel", 6f).After("rake").Mat(JP.MortarN).Wait(60f)
                .Finish("clean", "Brush off the dried crumbs", C.Broom, 2f).Dry()
                .Finish("test", "Scratch test", C.Screwdriver, 1f).Test();

            Svc("mas.walk-crack", "Cracked front walk", Hm, JobTrade.Concrete).At(JobPlaces.FrontWalk).Model("slab", 0.9f, 0.05f, 1.2f).Rare(0.7f).In(NotWinter)
                .Says("The front walk has a crack weeds grow through.")
                .Cause("narrow", "Narrow crack", 6, 0.75f, "1/8\" crack, no displacement.", "clear", "fill")
                .Cause("wide", "Wide crack", 3, 1.25f, "1/2\" crack: needs backer rod first.", "clear", "rod", "fill")
                .Diag("check", "Measure the crack width", null, 1f)
                .Fix("clear", "Pull the weeds and blow out the crack", C.Knife, 2f)
                .Fix("rod", "Push backer rod into the crack", C.Knives, 2f).After("clear").Mat(JP.BackerRod)
                .Fix("fill", "Fill with self-leveling crack sealant", C.CaulkGun, 3f).After("clear").Mat(JP.CrackSeal)
                .Finish("test", "Check the bead is flush", null, 1f).Test();
        }
    }
}
