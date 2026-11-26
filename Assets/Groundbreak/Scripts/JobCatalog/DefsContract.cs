using System;
using System.Collections.Generic;
using UnityEngine;
using C = Groundbreak.RepairCatalog;

namespace Groundbreak
{
    // Contractor scopes (remodels), Fleet contractor jobs and Builder/Developer new builds. Format: Docs/JOBS.md.
    public static partial class JobCatalog
    {
        static void DefineScopeTasks()
        {
            // two more service calls
            Svc("plb.low-pressure", "Low water pressure at the kitchen faucet", Hm, JobTrade.Plumbing).At(JobPlaces.KitchenSink).Model("sinkbase", 0.86f, 1.1f, 0.6f).Rare(0.8f)
                .Says("The kitchen faucet barely trickles, the bathroom is fine.")
                .Cause("aerator", "Clogged aerator", 6, 0.3f, "Pressure is good with the aerator off; its screen is packed with scale.", "aerator")
                .Cause("stop", "Angle stop partly closed", 2, 0.25f, "The hot stop is only a quarter open.", "stops")
                .Cause("cartridge", "Debris in the cartridge", 2, 0.75f, "Aerator is clean; flow is low on both sides: debris in the cartridge inlet.", "cartridge")
                .Diag("check", "Unscrew the aerator and run the faucet; check the stops", C.Pliers, 2f)
                .Fix("aerator", "Soak the aerator in vinegar, brush the screen, reinstall", C.Pliers, 3f)
                .Fix("stops", "Open both angle stops fully", null, 1f)
                .WaterOff("Close both angle stops")
                .Fix("cartridge", "Pull the cartridge, flush the lines into a bucket, reinstall", C.Pliers, 4f).Need("water-off").Guard("water-off", Flood)
                .WaterOn()
                .Finish("test", "Full flow, hot and cold", null, 1f).Test();

            Svc("el.afci-trip", "Bedroom breaker trips (AFCI)", Hm, JobTrade.Electrical).At(JobPlaces.Panel).Model("panel", 0.4f, 0.8f, 0.4f).Rep(30).Rare(0.7f)
                .Says("The bedroom circuit breaker with the test button keeps tripping when the vacuum runs.")
                .Cause("cord", "Damaged appliance cord", 4, 0.5f, "It trips only with the vacuum; the vacuum cord is chewed: tell the customer to replace it.", "advise")
                .Cause("neutral", "Shared neutral", 3, 2f, "The AFCI's neutral is tied to another circuit's neutral in a box: separate them.", "neutral")
                .Cause("breaker", "Failed AFCI breaker", 3, 0.75f, "It trips with nothing plugged in and the wiring tests clean: replace the AFCI.", "breaker")
                .Diag("probe", "Read the AFCI trip code and test the circuit unloaded", C.Meter, 4f)
                .Fix("advise", "Show the customer the damaged cord and run the circuit with a different vacuum", null, 2f)
                .PowerOff("the main breaker")
                .Prep("cover-off", "Remove the dead front", C.Screwdriver, 3f).Opens()
                .Fix("neutral", "Find the shared neutral and re-land it to its own circuit", C.Screwdriver, 6f).Need("power-off").Guard("power-off", Zap)
                .Fix("breaker", "Replace the AFCI breaker, pigtail to the neutral bar", C.Screwdriver, 4f).Need("cover-off", "power-off").Mat(JP.AfciBreaker).Guard("power-off", Zap)
                .Finish("cover-on", "Dead front back on", C.Screwdriver, 2f).Closes("cover-off")
                .PowerOn()
                .Finish("test", "Press the AFCI test button, reset, run the loads", null, 2f).Test();

            // ---- remodel scope tasks (each is one cause "scope"; steps are the real sequence)
            Task("task.vanity", "Replace the bathroom vanity and faucet", JobTrade.Plumbing, JobTrade.Cabinets).At(JobPlaces.Vanity).Model("sinkbase", 0.8f, 0.9f, 0.6f)
                .Says("New vanity, top and faucet.")
                .Cause("scope", "Vanity swap", 1, 4f, "The old vanity is particleboard swollen from leaks.", "demo", "set", "plumb")
                .WaterOff("Close the angle stops and open the faucet")
                .Fix("demo", "Disconnect the supplies and trap, cut the caulk, pull the old vanity", C.PryBar, 5f).Need("water-off")
                .Fix("set", "Set the new vanity level and screw it to the studs; set the top", C.Level, 5f).After("demo").Mat(JP.Vanity30).Mat(JP.Shims, 2)
                .Fix("plumb", "Install the faucet and drain, new supplies and a new P-trap", C.Pliers, 6f).After("set").Mat(JP.LavFaucet).Mat(C.Supply, 2).Mat(C.Trap114)
                .WaterOn()
                .Finish("caulk", "Caulk the top to the wall", C.CaulkGun, 2f).Mat(C.CaulkSilicone)
                .Finish("test", "Fill and drain the bowl, check for drips", null, 2f).Test();

            Task("task.toilet", "Replace the toilet", JobTrade.Plumbing).At(JobPlaces.Toilet).Model("toilet", 0.45f, 0.8f, 0.7f)
                .Says("New 1.28 gpf toilet.")
                .Cause("scope", "Toilet swap", 1, 2f, "Old 3.5 gpf toilet with a cracked tank.", "pull", "set")
                .WaterOff("Close the stop, flush and sponge dry")
                .Fix("pull", "Disconnect and lift out the old toilet, scrape the flange", C.Wrench, 5f).Need("water-off")
                .Fix("set", "New wax ring and bolts, set the bowl, mount the tank, connect the supply", C.Wrench, 7f).After("pull").Mat(JP.Toilet).Mat(JP.WaxRing).Mat(JP.ClosetBolts).Mat(JP.ToiletSupply)
                .WaterOn()
                .Finish("test", "Flush five times and check the base", null, 2f).Test();

            Task("task.tub-valve", "Replace the tub/shower valve", JobTrade.Plumbing).At(JobPlaces.TubValve).Model("tubspout", 1.0f, 0.8f, 0.2f)
                .Says("Pressure-balance valve.")
                .Cause("scope", "Valve swap", 1, 5f, "Old two-handle valve with no anti-scald protection.", "open", "valve", "close-wall")
                .Prep("main-off", "Shut off the main and drain down", null, 2f).Opens()
                .Fix("open", "Open the wall behind the valve from the closet side", C.JabSaw, 4f).Need("main-off")
                .Fix("valve", "Cut out the old valve, sweat in the pressure-balance valve", "torch", 8f).After("open").Mat(JP.TubValve).Need("main-off").Guard("main-off", Flood)
                .Fix("close-wall", "Patch the access opening", C.Knives, 5f).After("valve").Mat(C.PatchPanel).Mat(C.Compound)
                .Finish("main-on", "Main back on, purge air", null, 2f).Closes("main-off")
                .Finish("trim", "Install the trim and set the scald limit", C.Screwdriver, 2f)
                .Finish("test", "Run hot and cold, check the limit stop", null, 2f).Test();

            Task("task.regrout", "Regrout and seal the tub surround", JobTrade.Tile).At(JobPlaces.TubWall).Model("groutwall", 1.0f, 1.0f, 0.05f)
                .Says("Fresh grout.")
                .Cause("scope", "Regrout", 1, 4f, "Grout stained and cracked.", "rake", "grout", "seal")
                .Fix("rake", "Rake out the grout", JP.GroutSaw, 6f)
                .Fix("grout", "Regrout and caulk the tub joint", "grout-float", 5f).After("rake").Mat(JP.Grout).Mat(C.CaulkSilicone).Wait(180f)
                .Fix("seal", "Seal the grout", C.Brush, 3f).After("grout").Dry().Mat(JP.GroutSealer)
                .Finish("test", "Spray test", C.Hose, 1f).Test();

            Task("task.bath-floor", "Retile the bathroom floor", JobTrade.Tile).At(JobPlaces.BathFloor).Model("floorpatch", 0.95f, 0.05f, 0.95f)
                .Says("New porcelain floor.")
                .Cause("scope", "Floor tile", 1, 10f, "Old vinyl over sound plywood.", "tile-out", "set", "grout")
                .Fix("tile-out", "Tear out the old floor down to the subfloor, screw down cement board", C.PryBar, 8f).Opens().Mat(JP.CementBoard, 3).Mat(JP.Thinset)
                .Fix("set", "Lay out from the center and set 12x12 porcelain in thinset", "tile-saw", 10f).Need("tile-out").Mat(JP.FloorTile, 40).Mat(JP.Thinset, 2).Wait(480f)
                .Finish("close", "Clean the joints", null, 1f).Closes("tile-out")
                .Fix("grout", "Grout and seal", "grout-float", 6f).After("set").Dry().Mat(JP.Grout).Mat(JP.GroutSealer)
                .Finish("test", "Walk the floor and tap test", null, 1f).Test();

            Task("task.bath-fan", "Replace the bath exhaust fan", JobTrade.Electrical, JobTrade.Hvac).At(JobPlaces.BathCeiling).Model("fan", 0.3f, 0.1f, 0.3f)
                .Says("Quiet 80 CFM fan.")
                .Cause("scope", "Fan swap", 1, 2.5f, "Old 50 CFM fan, noisy, vented into the attic.", "out", "in")
                .PowerOff()
                .Fix("out", "Remove the old housing", C.Screwdriver, 4f).Need("power-off").Guard("power-off", Zap)
                .Fix("in", "Install the new housing, wire it, duct it to the roof cap with foil tape", C.Drill, 7f).After("out").Mat(JP.BathFan).Mat(JP.FoilTape).Mat(JP.WireNuts, 3).Need("power-off").Guard("power-off", Zap)
                .PowerOn()
                .Finish("test", "Tissue test", null, 1f).Test();

            Task("task.kitchen-faucet", "Install a new kitchen faucet", JobTrade.Plumbing).At(JobPlaces.KitchenSink).Model("sinkbase", 0.86f, 1.1f, 0.6f)
                .Says("Pull-down faucet.")
                .Cause("scope", "Faucet swap", 1, 1.5f, "Old two-handle faucet.", "out", "in")
                .WaterOff("Close both stops")
                .Fix("out", "Disconnect the supplies and remove the old faucet", C.Wrench, 4f).Need("water-off")
                .Fix("in", "Mount the new faucet and its hose weight, connect the supplies", C.Wrench, 5f).After("out").Mat(JP.KitFaucet).Mat(C.Supply, 2)
                .WaterOn()
                .Finish("test", "Run it, check under the sink", null, 1f).Test();

            Task("task.disposal", "Install a garbage disposal", JobTrade.Plumbing, JobTrade.Electrical).At(JobPlaces.KitchenSink).Model("sinkbase", 0.86f, 1.1f, 0.6f)
                .Says("New 1/2 HP disposal.")
                .Cause("scope", "Disposal install", 1, 2f, "Basket strainer only today.", "strainer", "mount", "wire")
                .PowerOff()
                .Fix("strainer", "Remove the basket strainer, set the disposal flange in plumber's putty", C.Pliers, 4f).Mat(JP.Plumbers)
                .Fix("mount", "Hang the disposal, connect the discharge and the dishwasher knockout", C.Screwdriver, 5f).After("strainer").Mat(JP.Disposal)
                .Fix("wire", "Wire the disposal to its switched receptacle", C.Screwdriver, 4f).After("mount").Need("power-off").Mat(JP.WireNuts, 2).Guard("power-off", Zap)
                .PowerOn()
                .Finish("test", "Run water and the disposal, check for leaks", null, 2f).Test();

            Task("task.kitchen-gfci", "GFCI protection at the counter", JobTrade.Electrical).At(JobPlaces.KitchenGfci).Model("receptacle", 0.2f, 0.2f, 0.1f)
                .Says("Counter receptacles to code.")
                .Cause("scope", "GFCI upgrade", 1, 1f, "The counter receptacles are ungrounded standard duplexes: first one in the run gets a GFCI protecting the rest.", "gfci")
                .PowerOff()
                .Prep("plate-off", "Pull the plate and the old receptacle", C.Screwdriver, 2f).Need("power-off").Opens()
                .Fix("gfci", "Install a GFCI: line to LINE, downstream to LOAD, label the protected outlets", C.Screwdriver, 5f).Need("plate-off", "power-off").Mat(C.Gfci).Guard("power-off", Zap)
                .Finish("plate-on", "Device and plate back in", C.Screwdriver, 2f).Closes("plate-off")
                .PowerOn()
                .Finish("test", "Press TEST, check the downstream outlets go dead, RESET", C.Tester, 2f).Test();

            Task("task.cabinets", "Paint the cabinets and new hardware", JobTrade.Painting, JobTrade.Cabinets).At(JobPlaces.KitchenCabinet).Model("cabinet", 0.4f, 0.7f, 0.1f)
                .Says("Painted cabinets.")
                .Cause("scope", "Cabinet refinish", 1, 14f, "Dated oak cabinets in good shape.", "prep", "prime", "paint", "hang")
                .Fix("prep", "Pull doors and hardware, degrease, scuff sand", C.Sander, 8f).Mat(JP.DropCloth)
                .Fix("prime", "Prime with bonding primer", C.Brush, 6f).After("prep").Mat(C.Primer, 2).Wait(120f)
                .Fix("paint", "Two coats of cabinet enamel", C.Roller, 8f).After("prime").Dry().Mat(JP.CabPaint, 3).Wait(240f)
                .Fix("hang", "Rehang the doors, install new pulls, adjust", C.Drill, 6f).After("paint").Dry().Mat(JP.Pulls, 20)
                .Finish("test", "Check every door gap", null, 1f).Test();

            Task("task.paint-living", "Paint the living room", JobTrade.Painting).At(JobPlaces.LivingWallFull).Model("wallpaint", 3.6f, 2.4f, 0.05f)
                .Says("New wall color.")
                .Cause("scope", "Repaint", 1, 7f, "Sound eggshell, scuffed.", "prep", "cut-in", "coat1", "coat2")
                .Prep("covered", "Move furniture, drop cloths, mask", null, 4f).Opens().Mat(JP.DropCloth).Mat(JP.Tape)
                .Fix("prep", "Patch and sand", C.Knives, 4f).Need("covered").Mat(C.Compound)
                .Fix("cut-in", "Cut in", C.Brush, 5f).Need("covered").After("prep").Mat(JP.PaintGal)
                .Fix("coat1", "First coat", C.Roller, 6f).Need("covered").After("cut-in").Mat(JP.RollerCover).Wait(120f)
                .Fix("coat2", "Second coat", C.Roller, 6f).Need("covered").After("coat1").Dry().Mat(JP.PaintGal)
                .Finish("uncover", "Clean up", null, 3f).Closes("covered")
                .Finish("test", "Walk-through", null, 1f).Test();

            Task("task.paint-bedroom", "Paint the bedroom", JobTrade.Painting).At(JobPlaces.BedroomWallFull).Model("wallpaint", 3.0f, 2.4f, 0.05f)
                .Says("New wall color.")
                .Cause("scope", "Repaint", 1, 6f, "Sound flat paint, nail holes.", "prep", "cut-in", "coat1", "coat2")
                .Prep("covered", "Move furniture, drop cloths, mask", null, 4f).Opens().Mat(JP.DropCloth).Mat(JP.Tape)
                .Fix("prep", "Patch and sand", C.Knives, 4f).Need("covered").Mat(C.Compound)
                .Fix("cut-in", "Cut in", C.Brush, 5f).Need("covered").After("prep").Mat(JP.PaintGal)
                .Fix("coat1", "First coat", C.Roller, 6f).Need("covered").After("cut-in").Mat(JP.RollerCover).Wait(120f)
                .Fix("coat2", "Second coat", C.Roller, 6f).Need("covered").After("coat1").Dry().Mat(JP.PaintGal)
                .Finish("uncover", "Clean up", null, 3f).Closes("covered")
                .Finish("test", "Walk-through", null, 1f).Test();

            Task("task.ceiling", "Paint the bedroom ceiling", JobTrade.Painting).At(JobPlaces.BedroomCeilingFront).Model("ceilingstain", 0.6f, 0.1f, 0.6f)
                .Says("Fresh ceiling.")
                .Cause("scope", "Ceiling", 1, 3f, "Yellowed ceiling with an old stain.", "prime", "paint-ceiling")
                .Fix("prime", "Spot-prime the stain", C.Brush, 3f).Mat(JP.StainBlock).Wait(30f)
                .Fix("paint-ceiling", "Roll the ceiling", C.Roller, 6f).After("prime").Dry().Mat(JP.CeilingPaint)
                .Finish("test", "Check in raking light", null, 1f).Test();

            Task("task.lvp-living", "New vinyl plank floor in the living room", JobTrade.Flooring).At(JobPlaces.LivingFloor).Model("floorpatch", 1.2f, 0.05f, 1.2f)
                .Says("LVP floor.")
                .Cause("scope", "LVP install", 1, 12f, "Old carpet over a flat slab.", "base-off", "tearout", "lay")
                .Prep("base-off", "Pull the baseboards and number them", C.PryBar, 4f).Opens()
                .Fix("tearout", "Pull carpet and pad, scrape the slab clean", C.Knife, 8f).Need("base-off")
                .Fix("lay", "Roll out underlayment and click the planks, 1/4\" gap at the walls, stagger the joints", "pull-bar", 12f).After("tearout").Need("base-off").Mat(JP.Underlay, 3).Mat(JP.LvpBox, 14)
                .Finish("base-on", "Reinstall the baseboards", "finish-nailer", 4f).Closes("base-off").Mat(JP.FinishNails, 60)
                .Finish("transition", "Install the transition at the hall", "miter-saw", 2f).Mat(JP.TMold)
                .Finish("test", "Walk the floor", null, 1f).Test();

            Task("task.window-living", "Replace the living room window", JobTrade.Carpentry, JobTrade.Exterior).At(JobPlaces.LivingWindow).Model("sash", 1.2f, 1.2f, 0.2f)
                .Says("Vinyl replacement window.")
                .Cause("scope", "Window swap", 1, 3f, "Old single-pane wood sashes, frame sound.", "stops-off", "out", "in", "trim")
                .Prep("stops-off", "Pull the interior stops", C.PryBar, 3f).Opens()
                .Fix("out", "Remove the sashes, balances and parting stops", C.PryBar, 4f).Need("stops-off")
                .Fix("in", "Set the replacement window in sealant, shim, screw through the jambs", C.Drill, 6f).After("out").Mat(JP.Window).Mat(JP.Shims, 4).Mat(C.CaulkExt)
                .Fix("trim", "Foam the gap (low-expansion), reinstall stops, caulk inside and out", "foam-gun", 4f).After("in").Mat(JP.Caulk)
                .Finish("stops-on", "Nail the stops", "brad-nailer", 2f).Closes("stops-off")
                .Finish("test", "Operate and lock the window", null, 1f).Test();

            Task("task.window-bedroom", "Replace the bedroom window", JobTrade.Carpentry, JobTrade.Exterior).At(JobPlaces.BedroomWindow).Model("sash", 1.2f, 1.2f, 0.2f)
                .Says("Vinyl replacement window.")
                .Cause("scope", "Window swap", 1, 3f, "Old single-pane wood sashes, frame sound.", "stops-off", "out", "in", "trim")
                .Prep("stops-off", "Pull the interior stops", C.PryBar, 3f).Opens()
                .Fix("out", "Remove the sashes, balances and parting stops", C.PryBar, 4f).Need("stops-off")
                .Fix("in", "Set the replacement window in sealant, shim, screw through the jambs", C.Drill, 6f).After("out").Mat(JP.Window).Mat(JP.Shims, 4).Mat(C.CaulkExt)
                .Fix("trim", "Foam the gap (low-expansion), reinstall stops, caulk inside and out", "foam-gun", 4f).After("in").Mat(JP.Caulk)
                .Finish("stops-on", "Nail the stops", "brad-nailer", 2f).Closes("stops-off")
                .Finish("test", "Operate and lock the window", null, 1f).Test();

            Task("task.entry-door", "Replace the front entry door", JobTrade.Carpentry).At(JobPlaces.EntryTrim).Model("brickmould", 1.2f, 2.2f, 0.2f)
                .Says("Prehung steel entry door.")
                .Cause("scope", "Door swap", 1, 6f, "Old door, rotted sill and brickmould.", "out", "pan", "set", "trim")
                .Fix("out", "Cut the nails and remove the old door and frame", JP.MultiTool, 6f)
                .Fix("pan", "Install a sill pan and flash the opening", C.Knife, 4f).After("out").Mat(JP.ZFlash).Mat(C.CaulkExt)
                .Fix("set", "Set the prehung unit plumb and square, shim at the hinges and lock, screw through", C.Level, 8f).After("pan").Mat(JP.EntryDoor).Mat(JP.Shims, 8).Mat(JP.HingeScrews, 4)
                .Fix("trim", "Insulate the gap, case inside, caulk the brickmould", "finish-nailer", 5f).After("set").Mat(JP.FinishNails, 40).Mat(C.CaulkExt)
                .Finish("test", "Even reveal, latches and deadbolt throw freely", null, 2f).Test();

            Task("task.siding-wall", "Reside the front wall", JobTrade.Exterior).At(JobPlaces.FrontWallSiding).Model("siding", 2.4f, 2.4f, 0.1f)
                .Says("New siding.")
                .Cause("scope", "Siding", 1, 16f, "Brittle, faded vinyl over sound sheathing.", "strip", "wrap", "hang")
                .Fix("strip", "Strip the old siding and trim from the wall", "siding-zip-tool", 8f)
                .Fix("wrap", "Housewrap, lapped shingle-style and taped, flash the windows", "cap-stapler", 6f).After("strip").Mat(JP.Housewrap)
                .Fix("hang", "Starter strip, J-channel, then the panels course by course, nails loose in the slots", "siding-nailer", 14f).After("wrap").Mat(JP.VinylPanel, 30).Mat(JP.SidingNails, 400)
                .Finish("test", "Panels slide freely, laps away from the street view", null, 1f).Test();

            Task("task.fence-bay", "Rebuild a fence section", JobTrade.Carpentry).At(JobPlaces.FenceBay).Model("fence", 2.6f, 1.9f, 0.2f)
                .Says("Rebuild one 8' bay.")
                .Cause("scope", "Fence bay", 1, 4f, "Rotted post and rails.", "post-out", "post-set", "concrete", "rail-new")
                .Fix("post-out", "Dig out the old post", "posthole", 6f)
                .Fix("post-set", "Set a new post plumb", C.Level, 4f).After("post-out").Mat(JP.Post4x4)
                .Fix("concrete", "Post concrete", "posthole", 3f).After("post-set").Mat(JP.PostMix, 2).Wait(30f)
                .Fix("rail-new", "New rails and pickets", C.Hammer, 6f).After("concrete").Mat(JP.Rail2x4, 3).Mat(C.Picket, 17).Mat(JP.ExtScrews, 20)
                .Finish("test", "Push test", null, 1f).Test();

            Task("task.fence-bay2", "Rebuild a second fence section", JobTrade.Carpentry).At(JobPlaces.FenceBay2).Model("fence", 2.6f, 1.9f, 0.2f)
                .Says("Rebuild one 8' bay.")
                .Cause("scope", "Fence bay", 1, 4f, "Rotted post and rails.", "post-out", "post-set", "concrete", "rail-new")
                .Fix("post-out", "Dig out the old post", "posthole", 6f)
                .Fix("post-set", "Set a new post plumb", C.Level, 4f).After("post-out").Mat(JP.Post4x4)
                .Fix("concrete", "Post concrete", "posthole", 3f).After("post-set").Mat(JP.PostMix, 2).Wait(30f)
                .Fix("rail-new", "New rails and pickets", C.Hammer, 6f).After("concrete").Mat(JP.Rail2x4, 3).Mat(C.Picket, 17).Mat(JP.ExtScrews, 20)
                .Finish("test", "Push test", null, 1f).Test();

            Task("task.gate", "Build a new gate", JobTrade.Carpentry).At(JobPlaces.FenceGate).Model("gate", 1.4f, 1.9f, 0.2f)
                .Says("New 4' gate.")
                .Cause("scope", "Gate", 1, 3f, "Old gate beyond repair.", "frame", "hang", "kit")
                .Fix("frame", "Build a Z-braced frame and clad it with pickets", C.Saw, 6f).Mat(JP.Rail2x4, 2).Mat(C.Picket, 8).Mat(JP.ExtScrews, 20)
                .Fix("hang", "Hang it on heavy T-hinges with a 1/2\" swing gap, mount the latch", C.Drill, 4f).After("frame").Mat(JP.GateHinge)
                .Fix("kit", "Add the anti-sag cable", C.Drill, 2f).After("hang").Mat(JP.GateKit)
                .Finish("test", "Swing and latch", null, 1f).Test();

            Task("task.retaining-wall", "Build a block retaining wall", JobTrade.Masonry, JobTrade.Concrete).At(JobPlaces.BackYardSlope).Model("retwall", 2.6f, 0.9f, 1.2f)
                .Says("2' segmental block wall, 8' long.")
                .Cause("scope", "Retaining wall", 1, 16f, "Eroding slope by the patio.", "trench", "base-course", "course2", "course3", "backfill", "cap")
                .Fix("trench", "Dig a 24\" wide trench, 6\" of compacted gravel base, level", C.Shovel, 8f).Mat(JP.Gravel, 12).Mat(JP.Fabric)
                .Fix("base-course", "Set the base course level and half-buried", C.Level, 8f).After("trench").Mat(JP.Block, 6)
                .Fix("course2", "Second course, staggered joints, setback lip", C.Sledge, 6f).After("base-course").Mat(JP.Block, 6)
                .Fix("course3", "Third and fourth courses, drain pipe behind at the base", C.Sledge, 8f).After("course2").Mat(JP.Block, 12).Mat(JP.Drain4)
                .Fix("backfill", "Backfill with drainage stone, compact in lifts", C.Tamper, 8f).After("course3").Mat(JP.Gravel, 16)
                .Fix("cap", "Glue the caps", C.CaulkGun, 3f).After("backfill").Mat(JP.BlockCap, 6).Mat(JP.BlockAdhesive, 2)
                .Finish("test", "Sight the wall for level and batter", C.Level, 1f).Test();

            Task("task.pergola", "Build a cedar pergola", JobTrade.Carpentry).At(JobPlaces.BackYard).Model("pergola", 3.6f, 2.9f, 4.0f)
                .Says("10' x 12' cedar pergola.")
                .Cause("scope", "Pergola", 1, 28f, "Open lawn behind the deck.", "footings", "posts", "beams", "rafters")
                .Fix("footings", "Dig four 10\" footings 36\" deep, set tubes, pour, set standoff bases", "posthole", 10f).Mat(JP.Sonotube, 4).Mat(JP.Concrete80, 8).Mat(JP.PostBase, 4).Wait(240f)
                .Fix("posts", "Stand the 6x6 cedar posts plumb and brace them", C.Level, 6f).After("footings").Dry().Mat(JP.Cedar4x4, 4).Mat(JP.ExtScrews, 16)
                .Fix("beams", "Through-bolt doubled 2x8 beams to the posts", JP.SocketSet, 8f).After("posts").Mat(JP.Cedar2x8, 4).Mat(JP.CarriageBolt, 8)
                .Fix("rafters", "Notch and fasten nine 2x6 rafters 16\" O.C. with hurricane ties", C.Saw, 10f).After("beams").Mat(JP.Cedar2x6, 9).Mat(JP.ExtScrews, 40)
                .Finish("stain", "Stain everything", C.Brush, 6f).Mat(JP.Stain, 2)
                .Finish("test", "Check plumb, remove the braces", C.Level, 1f).Test();

            Task("task.deck-ledger", "Re-lag and flash the deck ledger", JobTrade.Carpentry).At(JobPlaces.DeckLedger).Model("ledger", 3.6f, 0.3f, 0.2f)
                .Says("Ledger fasteners and flashing to code.")
                .Cause("scope", "Ledger", 1, 4f, "Nailed-only ledger, no flashing.", "flash", "lag")
                .Fix("flash", "Install Z-flashing over the ledger", "ext-snips", 4f).Mat(JP.ZFlash)
                .Fix("lag", "Structural ledger screws 16\" O.C. staggered", JP.ImpactDriver, 5f).Mat(JP.Lag, 12)
                .Finish("test", "Bounce test", null, 1f).Test();

            Task("task.deck-step", "Rebuild the deck step", JobTrade.Carpentry).At(JobPlaces.DeckStep).Model("deckstep", 1.1f, 0.3f, 0.6f)
                .Says("New treads and stringer ties.")
                .Cause("scope", "Step", 1, 2f, "Split treads.", "tread-off", "tread-new")
                .Fix("tread-off", "Pull the old treads", C.PryBar, 3f)
                .Fix("tread-new", "New 2x6 treads, two screws per stringer", C.Saw, 5f).After("tread-off").Mat(JP.TreadBoard, 2).Mat(C.DeckScrews, 8)
                .Finish("test", "Bounce test", null, 1f).Test();
        }

        static JobDef Scope(string id, string name, int rep, string vehicle, string say, params string[] tasks)
        {
            var d = new JobDef { Id = id, Name = name, Stage = Ct, Mode = JobMode.Scope, Kind = JobKind.Remodel, MinRep = rep, Vehicle = vehicle, Category = "remodel" };
            d.Says(say);
            d.Tasks.AddRange(tasks);
            foreach (var t in tasks) { var td = Get(t); if (td != null) foreach (var tr in td.Trades) d.Trade(tr); }
            Register(d);
            return d;
        }

        static void DefineScopes()
        {
            Scope("rem.bath-refresh", "Bathroom refresh", 20, "pickup", "Our bathroom is dated. New vanity, new toilet and the grout redone, please.", "task.vanity", "task.toilet", "task.regrout", "task.bath-fan");
            Scope("rem.bath-full", "Full bathroom remodel", 40, "pickup", "Gut the hall bath: new valve, tile floor, vanity, toilet and fan.", "task.tub-valve", "task.bath-floor", "task.vanity", "task.toilet", "task.regrout", "task.bath-fan").Callout = 350;
            Scope("rem.kitchen-refresh", "Kitchen refresh", 30, "pickup", "Paint the kitchen cabinets, new pulls, a new faucet, and GFCI protection at the counter.", "task.cabinets", "task.kitchen-faucet", "task.kitchen-gfci");
            Scope("rem.windows", "Window replacement package", 25, "pickup", "Replace the two front windows with new vinyl double-hungs.", "task.window-living", "task.window-bedroom");
            Scope("rem.entry-door", "Entry door replacement", 20, "pickup", "Our front door is drafty and the bottom is rotted. Put in a new steel door.", "task.entry-door");
            Scope("rem.interior-paint", "Interior repaint", 15, "pickup", "Paint the living room, the bedroom and the bedroom ceiling.", "task.paint-living", "task.paint-bedroom", "task.ceiling");
            Scope("rem.flooring", "Living room flooring", 25, "pickup", "Pull up the carpet and put down vinyl plank in the living room.", "task.lvp-living");
            Scope("rem.reside-front", "Re-side the front of the house", 35, "pickup", "The siding on the front is brittle and faded. Re-side the front wall.", "task.siding-wall").In(NotWinter);
            Scope("rem.fence-repair", "Fence and gate repair package", 15, "pickup", "Two fence sections are rotted and the gate is falling apart.", "task.fence-bay", "task.fence-bay2", "task.gate").In(NotWinter);
            Scope("rem.retaining-wall", "Block retaining wall", 35, "pickup", "Our back slope keeps washing onto the lawn. Build a short block wall.", "task.retaining-wall").In(NotWinter);
            Scope("rem.pergola", "Cedar pergola", 40, "flatbed", "We'd like a 10' x 12' cedar pergola in the back yard.", "task.pergola").In(NotWinter);
            Scope("rem.deck-safety", "Deck safety repair", 25, "pickup", "The home inspector flagged our deck ledger and steps.", "task.deck-ledger", "task.deck-step").In(NotWinter);
        }

        static JobDef Fleet(string id, string name, string type, float w, float l, float rate, int days, int rep, string vehicle, string say, CompanyStage stage = CompanyStage.Contractor)
        {
            var d = new JobDef { Id = id, Name = name, Stage = stage, Mode = JobMode.Fleet, Kind = type == "shed" || type == "garage" ? JobKind.Outbuilding : JobKind.Remodel,
                FleetType = type, W = w, L = l, RatePerUnit = rate, DaysToBuild = days, MinRep = rep, Vehicle = vehicle, Category = "fleet" };
            d.Says(say);
            d.Trade(type == "deck" || type == "fence" || type == "shed" ? JobTrade.Carpentry : type == "garage" ? JobTrade.Framing : type == "reroof" ? JobTrade.Roofing : JobTrade.Concrete);
            if (type == "driveway" || type == "patio" || type == "garage" || type == "reroof") d.In(NotWinter);
            Register(d);
            return d;
        }

        static void DefineFleet()
        {
            // decks: $/sq ft installed (PT framing + PT decking + guards) — US 2025 range $35-60/sq ft
            Fleet("deck.10x12", "low pressure-treated deck", "deck", 12, 10, 48, 4, 0, "pickup", "A small deck off the back door.").H = 30;
            Fleet("deck.12x16", "pressure-treated deck", "deck", 16, 12, 45, 5, 5, "flatbed", "We want a deck for the grill and a table.").H = 30;
            Fleet("deck.16x20", "large pressure-treated deck", "deck", 20, 16, 42, 8, 20, "flatbed", "Big deck for entertaining.").H = 36;
            Fleet("deck.12x20-high", "raised pressure-treated deck", "deck", 20, 12, 52, 7, 25, "flatbed", "Raised deck off the kitchen.").H = 36;
            // fences: $/LF incl. one gate
            var f1 = Fleet("fence.100", "cedar privacy fence", "fence", 0, 100, 42, 3, 0, "pickup", "Privacy fence for the dog."); f1.Gates = 1;
            var f2 = Fleet("fence.140", "cedar privacy fence", "fence", 0, 140, 40, 4, 5, "pickup", "Fence the whole back yard."); f2.Gates = 1;
            var f3 = Fleet("fence.180", "cedar privacy fence with two gates", "fence", 0, 180, 39, 5, 15, "pickup", "Fence the yard with a gate on each side."); f3.Gates = 2; f3.Callout = 450;
            // sheds
            Fleet("shed.8x12", "gable storage shed", "shed", 8, 12, 78, 5, 10, "flatbed", "A shed for the mower and garden tools.");
            Fleet("shed.10x12", "gable storage shed", "shed", 10, 12, 75, 6, 10, "flatbed", "A bigger shed for the tools.");
            Fleet("shed.12x16", "gable workshop shed", "shed", 12, 16, 72, 7, 20, "flatbed", "A workshop shed with room for a bench.");
            // garages ($/sq ft for slab + framing + roof + overhead door)
            Fleet("garage.20x20", "detached 2-car garage", "garage", 20, 20, 72, 18, 30, "boom", "A detached two-car garage.");
            Fleet("garage.22x24", "detached 2-car garage", "garage", 22, 24, 70, 20, 35, "boom", "Two-car garage with room for a workbench.");
            Fleet("garage.24x24", "detached 2-car garage", "garage", 24, 24, 68, 21, 40, "boom", "Big two-car garage.");
            // driveways & patios
            var dw1 = Fleet("drive.10x30", "concrete driveway", "driveway", 10, 30, 12, 7, 10, "dump", "Our driveway is cracked to pieces, replace it."); dw1.Thick = 4; dw1.Psi = 4000;
            var dw2 = Fleet("drive.12x40", "concrete driveway", "driveway", 12, 40, 11, 9, 15, "dump", "Tear out and replace the long driveway."); dw2.Thick = 4; dw2.Psi = 4000;
            var dw3 = Fleet("drive.16x40", "wide concrete driveway", "driveway", 16, 40, 11, 10, 25, "dump", "Two-car-wide driveway."); dw3.Thick = 4; dw3.Psi = 4000;
            var p1 = Fleet("patio.10x12", "concrete patio", "patio", 10, 12, 17, 5, 0, "dump", "A small patio for two chairs."); p1.Thick = 4; p1.Psi = 3000;
            var p2 = Fleet("patio.12x14", "concrete patio", "patio", 12, 14, 16, 6, 5, "dump", "Patio behind the house."); p2.Thick = 4; p2.Psi = 3000;
            var p3 = Fleet("patio.16x20", "large concrete patio", "patio", 16, 20, 15, 7, 15, "dump", "Big patio for the outdoor kitchen."); p3.Thick = 4; p3.Psi = 3000;
            // re-roofs ($/square, tear-off + architectural)
            Fleet("reroof.ranch", "re-roof (ranch)", "reroof", 26, 40, 520, 3, 15, "dump", "The roof is 25 years old and leaking.");
            Fleet("reroof.wide", "re-roof (wide ranch)", "reroof", 30, 48, 540, 4, 25, "dump", "Re-roof before the hail season.");
            Fleet("reroof.steep", "re-roof, steep 8/12", "reroof", 28, 44, 610, 4, 35, "dump", "Steep roof, needs a crew with harnesses.").Pitch = 8;
        }

        // ------------------------------------------------------------------ new builds (Builder / Developer)

        [Serializable] public class BuildPayload { public string contract, plan, lot, project, warrantyOf, catalog; }

        public static BuildPayload ReadBuild(Job j)
        {
            if (j == null || j.Kind != JobKind.House || string.IsNullOrEmpty(j.Payload)) return null;
            try { var b = JsonUtility.FromJson<BuildPayload>(j.Payload); return b != null && !string.IsNullOrEmpty(b.catalog) ? b : null; } catch { return null; }
        }

        /// <summary>The first plan of the definition that exists (blueprint ids are looked up at runtime; missing ids are skipped).</summary>
        public static HousePlan PlanFor(JobDef d)
        {
            if (d == null) return null;
            foreach (var id in d.PlanIds) { var p = HousePlans.Get(id); if (p != null) return p; }
            return null;
        }

        static JobDef Build(string id, string name, CompanyStage stage, string type, int perSqFt, int rep, string say, params string[] plans)
        {
            var d = new JobDef { Id = id, Name = name, Stage = stage, Mode = JobMode.Build, Kind = JobKind.House, BuildingType = type, PricePerSqFt = perSqFt, MinRep = rep, Category = "build" };
            d.Says(say); d.PlanIds.AddRange(plans); d.Trade(JobTrade.Framing, JobTrade.Concrete, JobTrade.General);
            Register(d);
            return d;
        }

        static void DefineBuilds()
        {
            const CompanyStage B = CompanyStage.Builder, D = CompanyStage.Developer;
            // houses: plan ids from Development (always present) first, blueprint ids as alternates
            Build("bld.starter", "Starter ranch for a young couple", B, "house", 165, 60, "First home on a budget: two bedrooms, one bath, nothing fancy.", "starter-900", "bp-starter-ranch");
            Build("bld.cottage", "Cottage for retirees", B, "house", 175, 62, "A one-level cottage on a crawlspace with a big front porch.", "cottage-1200", "bp-cottage");
            Build("bld.ranch", "Family ranch", B, "house", 170, 65, "Three-bedroom ranch with a hip roof and 9' ceilings.", "ranch-1600", "bp-ranch");
            Build("bld.craftsman", "Craftsman bungalow", B, "house", 185, 70, "Four-bedroom craftsman with tapered porch columns.", "craftsman-1920", "bp-craftsman");
            Build("bld.colonial", "Two-story colonial", B, "house", 180, 72, "Two-story colonial on a full basement.", "twostory-2400", "bp-colonial");
            Build("bld.estate", "Custom estate home", D, "house", 225, 85, "Custom five-bedroom estate, high-end finishes.", "estate-3500", "bp-estate");
            Build("bld.farmhouse", "Modern farmhouse", B, "house", 190, 75, "Board-and-batten modern farmhouse.", "bp-farmhouse", "farmhouse-2200", "craftsman-1920");
            Build("bld.duplex", "Duplex rental", D, "duplex", 160, 80, "Side-by-side duplex for an investor.", "bp-duplex", "duplex-2400", "twostory-2400");
            // outbuildings and commercial (blueprint plans; skipped when the blueprint isn't installed)
            var g = Build("bld.garage-apt", "Detached garage", B, "garage", 75, 60, "Detached 24' x 24' garage on a slab.", "bp-garage-24x24", "garage-24x24", "garage-2car"); g.FallbackFleet = "garage"; g.W = 24; g.L = 24;
            var barn = Build("bld.pole-barn", "Pole barn", B, "barn", 45, 62, "30' x 40' post-frame barn for equipment.", "bp-pole-barn-30x40", "pole-barn-30x40", "barn-30x40");
            var shop = Build("bld.workshop", "Workshop building", B, "shop", 85, 65, "Insulated 30' x 40' workshop with an overhead door.", "bp-shop-30x40", "shop-30x40", "workshop-30x40");
            Build("bld.office", "Small office building", D, "office", 210, 82, "Single-story professional office for a local firm.", "bp-office-small", "office-2400", "office-small");
            Build("bld.warehouse", "Warehouse", D, "warehouse", 95, 85, "Pre-engineered warehouse with dock doors.", "bp-warehouse", "warehouse-6000", "warehouse-10000");
            Build("bld.retail", "Retail strip building", D, "retail", 190, 88, "Three-unit retail strip on the main road.", "bp-retail-strip", "retail-strip", "strip-mall-3");
            barn.W = 30; barn.L = 40; shop.W = 30; shop.L = 40;
        }

        /// <summary>A new-build contract on a free development lot (Builder pays draws on inspections via its own hooks).</summary>
        public static Job CreateBuild(JobDef d, bool addToOffers)
        {
            if (d == null || d.Mode != JobMode.Build) return null;
            var plan = PlanFor(d);
            if (plan == null)
            {
                if (string.IsNullOrEmpty(d.FallbackFleet)) return null;
                var fd = new JobDef { Id = d.Id, Name = d.Name, Stage = d.Stage, Mode = JobMode.Fleet, FleetType = d.FallbackFleet, W = d.W, L = d.L, RatePerUnit = d.PricePerSqFt, DaysToBuild = 21, MinRep = d.MinRep, Vehicle = "boom" };
                fd.Says(d.Complaints.ToArray());
                var fj = CreateFleet(fd, UnityEngine.Random.Range(1, 1000000), false);
                if (fj == null) return null;
                fj.Id = $"{ContractorJobs.IdPrefix}{d.FallbackFleet}{FleetKey}{KeyOf(d)}-L{ContractorJobs.SpecOf(fj)?.lotIndex ?? 0}";
                if (addToOffers) { Company.Offers.Add(fj); Company.NotifyChanged(); }
                return fj;
            }
            DevLot lot = null;
            foreach (var l in DevLots.Provider.FreeLots())
            {
                if (l.FrontageFt < plan.WidthFt + 15f || l.DepthFt < plan.DepthFt + 45f) continue;
                if (Company.Offers.Exists(j => j.Address == l.Address) || Company.ActiveJobs.Exists(j => j.Address == l.Address)) continue;
                lot = l; break;
            }
            if (lot == null) return null;
            int price = Mathf.RoundToInt(plan.SqFt * d.PricePerSqFt / 1000f) * 1000;
            var job = new Job
            {
                Id = "house-jc-" + KeyOf(d) + "-" + _nextId++ + "-" + lot.Id, Kind = JobKind.House, Title = $"{d.Name}: {plan.Name}", Client = BuildClients[UnityEngine.Random.Range(0, BuildClients.Length)],
                Description = Complaint(d, null) + $" Plan: {plan.Name} ({plan.Summary}). Draws on inspections: 10% deposit, then 10/20/20/15%, final 25% at the CO.",
                Address = lot.Address, WorldPos = lot.Local(0, 2f), Payout = price, MinReputation = d.MinRep, MinStage = d.Stage,
                MaterialBudget = plan.Takeoff.MaterialTotal, DueDay = Today + 10 + plan.TotalDays,
                Payload = JsonUtility.ToJson(new BuildPayload { contract = d.Id, plan = plan.Id, lot = lot.Id, catalog = d.Id }),
            };
            if (addToOffers) { Company.Offers.Add(job); Company.NotifyChanged(); }
            return job;
        }

        static readonly string[] BuildClients = { "The Delgado family", "Chris and Morgan Avery", "Pat Fennimore", "Harbor Realty LLC", "The Whitfield trust", "Northgate Partners", "Dr. Anika Rao", "Bluebird Holdings" };

        /// <summary>Starts the Development HouseProject when a catalog build is accepted (same as Builder does for career contracts).</summary>
        static void OnBuildAccepted(Job j)
        {
            var pl = ReadBuild(j);
            if (pl == null || !string.IsNullOrEmpty(pl.project)) return;
            var lot = DevLots.Provider.Find(pl.lot); var plan = HousePlans.Get(pl.plan);
            if (lot == null || plan == null) return;
            DevLots.Provider.Claim(lot, Company.Name);
            var p = Projects.Create(ProjectKind.Client, lot, plan);
            p.JobId = j.Id; p.Client = j.Client; p.ContractPrice = j.Payout; p.DueDay = j.DueDay;
            pl.project = p.Id; j.Payload = JsonUtility.ToJson(pl);
            int deposit = Mathf.RoundToInt(p.ContractPrice * Builder.DepositPct);
            Game.Wallet.Add(deposit, $"Deposit: {j.Title}"); p.DrawsPaid |= 1 << 7;
            Company.LifetimeRevenue += deposit;
            Game.Toast($"Signed {j.Title}. Deposit ${deposit:N0}. Lot: {lot.Address}");
            DevVisuals.Refresh();
        }
    }
}
