using C = Groundbreak.RepairCatalog;

namespace Groundbreak
{
    // Service calls: plumbing, electrical, HVAC. Format: Docs/JOBS.md.
    public static partial class JobCatalog
    {
        const CompanyStage Hm = CompanyStage.Handyman, Ct = CompanyStage.Contractor;

        // common safe-work pairs
        static JobDef PowerOff(this JobDef d, string what = "the circuit") =>
            d.Prep("power-off", $"Switch off {what} at the panel and confirm dead with the non-contact tester", C.Tester, 3f).Opens().Snd("click");
        static JobDef PowerOn(this JobDef d) => d.Finish("power-on", "Turn the breaker back on", null, 1f).Closes("power-off");
        static JobDef WaterOff(this JobDef d, string label) => d.Prep("water-off", label, null, 2f).Opens().Snd("water");
        static JobDef WaterOn(this JobDef d, string label = "Open the valve slowly and check for drips") => d.Finish("water-on", label, null, 2f).Closes("water-off").Snd("water");
        const string Zap = "You get bitten by the live circuit — a sharp jolt up the arm. Always kill the breaker and test first!";
        const string Flood = "Water sprays out of the open line and soaks everything — the supply was still on!";

        static void DefinePlumbing()
        {
            Svc("plb.toilet-running", "Running toilet", Hm, JobTrade.Plumbing).At(JobPlaces.Toilet).Model("toilet", 0.45f, 0.8f, 0.7f)
                .Says("The toilet keeps running and refills by itself every few minutes, even in the middle of the night.",
                      "Our water bill doubled. The toilet hisses all the time and the handle has to be jiggled.")
                .Cause("flapper", "Warped flapper", 5, 0.5f, "Dye from the tank shows up in the bowl after ten minutes without a flush: the flapper is warped and seeping.", "flapper")
                .Cause("fill", "Fill valve not shutting off", 3, 0.75f, "The water rises past the fill line and spills down the overflow tube: the fill valve isn't shutting off.", "fill")
                .Cause("chain", "Lift chain caught", 2, 0.3f, "The lift chain is kinked under the flapper and holds it open a crack.", "chain")
                .Prep("lid-off", "Lift the tank lid off and lay it on a towel", null, 1.5f).Opens()
                .Diag("dye", "Put dye tablets in the tank, wait, and watch the bowl and the water level", null, 4f).Need("lid-off")
                .WaterOff("Close the toilet's shut-off stop and flush to empty the tank").Need("lid-off")
                .Fix("chain", "Free the chain and set about 1/2\" of slack", null, 2f).Need("lid-off").Say("Chain is free with a little slack; the flapper drops square on the seat.")
                .Fix("flapper", "Unhook the old flapper and fit a new 2\" flapper", null, 3f).Need("water-off").Mat(JP.Flapper)
                .Fix("fill", "Replace the fill valve: back off the locknut, set the new valve, cut the refill tube, set the float to the line", C.Pliers, 6f).Need("water-off").Mat(JP.FillValve).Guard("water-off", Flood)
                .WaterOn("Open the stop and let the tank fill to the line").After("lid-off")
                .Finish("lid-on", "Set the tank lid back on", null, 1f).Closes("lid-off")
                .Finish("test", "Flush twice and listen: the tank fills and goes quiet", null, 3f).Test();

            Svc("plb.toilet-rocking", "Toilet rocks and leaks at the base", Hm, JobTrade.Plumbing).At(JobPlaces.Toilet).Model("toilet", 0.45f, 0.8f, 0.7f).Rep(10)
                .Says("The toilet wobbles when you sit on it and there's water on the floor around the base after flushing.",
                      "There's a sewer smell in the bathroom and a stain spreading on the floor around the toilet.")
                .Cause("wax", "Failed wax ring", 5, 1.25f, "The bowl moves an eighth of an inch and the floor at the base is wet after a flush: the wax seal has failed.", "pull", "wax", "reset")
                .Cause("bolts", "Loose closet bolts", 3, 0.4f, "The caps are off the closet bolts and the nuts spin loose; the floor is dry under the bowl.", "snug")
                .Diag("check", "Rock the bowl, check the closet bolts, and flush with paper towels around the base", null, 3f)
                .Fix("snug", "Shim the bowl level and snug the closet nuts alternately (don't crack the porcelain)", C.Wrench, 3f).Mat(JP.Shims, 2)
                .WaterOff("Close the stop, flush, sponge the tank and bowl dry")
                .Fix("pull", "Disconnect the supply, pull the nuts and lift the toilet off onto a drop cloth", C.Wrench, 5f).Need("water-off").Guard("water-off", Flood).Snd("thud")
                .Fix("wax", "Scrape off the old wax, plug the drain with a rag, set new closet bolts and a wax ring", C.Knives, 5f).After("pull").Mat(JP.WaxRing).Mat(JP.ClosetBolts)
                .Fix("reset", "Pull the rag, set the bowl straight down on the bolts, rock it to seat, snug the nuts, cut the bolts", C.Wrench, 5f).After("wax").Snd("thud")
                .WaterOn("Reconnect the supply and open the stop")
                .Finish("caulk", "Caulk the base front and sides (leave the back open to show a leak)", C.CaulkGun, 3f).Mat(C.CaulkSilicone).Opt()
                .Finish("test", "Flush several times and check the base with a paper towel", null, 3f).Test();

            Svc("plb.toilet-clog", "Clogged toilet", Hm, JobTrade.Plumbing).At(JobPlaces.Toilet).Model("toilet", 0.45f, 0.8f, 0.7f)
                .Says("The toilet is clogged and nearly overflowed. The plunger didn't help.", "The toilet drains really slowly and gurgles.")
                .Cause("paper", "Paper wad in the trapway", 5, 0.5f, "The water drains a little after a few strokes of the flange plunger: soft paper clog in the trapway.", "plunge")
                .Cause("object", "Toy stuck in the trapway", 2, 0.75f, "The plunger moves water but nothing clears; the auger hits something hard at the top of the trapway.", "auger")
                .Diag("look", "Look at the bowl level and try a few plunger strokes", JP.Plunger, 3f)
                .Prep("towels", "Lay towels around the base", null, 1f)
                .Fix("plunge", "Plunge with a flange plunger: slow push, hard pull, a dozen strokes", JP.Plunger, 4f).After("towels").Snd("water")
                .Fix("auger", "Run the closet auger through the trapway and pull the toy out", JP.ClosetAuger, 5f).After("towels")
                .Finish("clean", "Pick up the wet towels and disinfect the floor", null, 2f)
                .Finish("test", "Flush with a few sheets of paper and watch it clear", null, 2f).Test();

            Svc("plb.supply-stop", "Leaking toilet shut-off valve", Hm, JobTrade.Plumbing).At(JobPlaces.Toilet).Model("toilet", 0.45f, 0.8f, 0.7f).Rep(15)
                .Says("The little valve behind the toilet drips and won't shut off all the way.", "There's a puddle behind the toilet and the valve handle is crusty.")
                .Cause("packing", "Loose packing nut", 3, 0.3f, "Water beads around the valve stem and stops when you snug the packing nut a quarter turn.", "packing")
                .Cause("stop", "Worn-out multi-turn stop", 5, 1f, "The stem leaks and the stop won't close; the old multi-turn valve is corroded inside.", "cut", "stop")
                .Cause("line", "Cracked supply line", 2, 0.4f, "The valve is dry. The old plastic supply line has a split at the coupling nut.", "line")
                .Diag("check", "Dry the valve and the supply with a rag and watch where the drip forms", null, 3f)
                .Fix("packing", "Snug the packing nut a quarter turn while holding the valve body", C.Wrench, 2f)
                .Prep("main-off", "Shut the house main and drain the lines at the lowest faucet", null, 3f).Opens()
                .Fix("cut", "Cut the old stop off below the compression ring", "tubing-cutter", 3f).Need("main-off").Guard("main-off", Flood)
                .Fix("stop", "Slide on the new 1/4-turn stop with a new ferrule and tighten", C.Wrench, 4f).After("cut").Mat(JP.AngleStop)
                .Fix("line", "Replace the supply with a braided toilet connector", C.Wrench, 2f).Mat(JP.ToiletSupply)
                .Finish("main-on", "Turn the main back on and bleed air at the faucets", null, 2f).Closes("main-off").Snd("water")
                .Finish("test", "Open and close the stop twice; check for drips with a dry tissue", null, 2f).Test();

            Svc("plb.vanity-drain", "Slow bathroom sink drain", Hm, JobTrade.Plumbing).At(JobPlaces.Vanity).Model("sinkbase", 0.7f, 0.9f, 0.6f)
                .Says("The bathroom sink takes forever to drain and stands full of water when we brush our teeth.", "The sink smells and drains slow even after the store-bought drain cleaner.")
                .Cause("stopper", "Hair on the pop-up stopper", 5, 0.4f, "With the pivot rod nut loose the stopper lifts out coated in hair and soap scum.", "stopper")
                .Cause("trap", "Packed P-trap", 3, 0.6f, "The stopper is clean, but the trap is full of sludge when you drop it.", "trap-off", "trap-clean")
                .Cause("arm", "Clog in the trap arm / wall line", 2, 1f, "Trap is clean and still no flow from the wall: the clog is past the trap arm.", "trap-off", "snake")
                .Diag("check", "Fill the bowl, pull the stopper and watch how it drains", null, 3f).Snd("water")
                .Fix("stopper", "Loosen the pivot nut, pull the stopper and clean off the hair", C.Pliers, 3f)
                .Prep("bucket", "Set a bucket under the trap", C.Bucket, 1f)
                .Fix("trap-off", "Loosen the slip nuts and drop the P-trap into the bucket", C.Pliers, 3f).After("bucket").Opens()
                .Fix("trap-clean", "Clean the trap out and check the washers", C.Bucket, 3f).Need("trap-off")
                .Fix("snake", "Feed the drum auger into the trap arm until it breaks through", JP.DrumAuger, 5f).Need("trap-off")
                .Finish("trap-on", "Reinstall the trap: hand tight plus a quarter turn", C.Pliers, 3f).Closes("trap-off")
                .Finish("test", "Run the faucet hard for a minute and check the joints", null, 2f).Test();

            Svc("plb.kitchen-faucet-drip", "Dripping kitchen faucet", Hm, JobTrade.Plumbing).At(JobPlaces.KitchenSink).Model("sinkbase", 0.86f, 1.1f, 0.6f)
                .Says("The kitchen faucet drips nonstop, you can hear it all night.", "The faucet won't shut off completely unless you push the handle hard.")
                .Cause("cartridge", "Worn cartridge seals", 6, 0.75f, "Drip continues with the handle fully closed; the cartridge seals are worn.", "cartridge")
                .Cause("oring", "Spout O-rings", 2, 0.5f, "Water seeps from under the spout when it runs, not from the outlet: O-rings.", "orings")
                .Diag("check", "Watch the spout and the base with the faucet off and on", null, 3f)
                .WaterOff("Close both angle stops under the sink and open the faucet to relieve pressure")
                .Prep("handle-off", "Pop the cap, unscrew the handle and the bonnet nut", C.Screwdriver, 3f).Need("water-off").Opens()
                .Fix("cartridge", "Pull the old cartridge, match it at the store, install the new one aligned to the notch", C.Pliers, 4f).Need("handle-off").Mat(JP.Cartridge)
                .Fix("orings", "Lift off the spout, replace and grease the O-rings", null, 3f).Need("handle-off").Mat(JP.BibWasher)
                .Finish("handle-on", "Reassemble the bonnet nut and handle", C.Screwdriver, 2f).Closes("handle-off")
                .WaterOn("Open the stops and run hot and cold")
                .Finish("test", "Shut it off and watch the spout for a full minute", null, 3f).Test();

            Svc("plb.disposal", "Garbage disposal hums but won't spin", Hm, JobTrade.Plumbing, JobTrade.Electrical).At(JobPlaces.KitchenSink).Model("sinkbase", 0.86f, 1.1f, 0.6f).Rep(5)
                .Says("The garbage disposal just hums when you flip the switch.", "The disposal stopped working completely. Nothing happens.")
                .Cause("jam", "Jammed flywheel", 5, 0.4f, "It hums and the flywheel won't turn: something is wedged between the impeller and the grind ring.", "free", "reset")
                .Cause("tripped", "Tripped thermal overload", 3, 0.3f, "It's silent, but the red reset button on the bottom has popped out.", "reset")
                .Cause("burnt", "Burnt-out motor", 2, 1.5f, "The motor won't run with the overload reset and the flywheel spins free: it's burnt out.", "unplug", "swap")
                .PowerOff("the disposal circuit")
                .Diag("check", "Check the reset button, try the flywheel from below with the hex wrench", JP.HexKey, 3f).Need("power-off")
                .Fix("free", "Work the hex wrench back and forth until the flywheel turns, pull the bone out with tongs", JP.HexKey, 3f).Need("power-off").Guard("power-off", Zap)
                .Fix("reset", "Press the red reset button on the bottom of the unit", null, 1f)
                .Fix("unplug", "Disconnect the drain and dishwasher hose, twist the unit off the mounting ring", C.Screwdriver, 4f).Need("power-off").Guard("power-off", Zap)
                .Fix("swap", "Wire the new disposal, hang it on the ring and reconnect the drain", C.Screwdriver, 8f).After("unplug").Mat(JP.Disposal).Mat(JP.WireNuts, 2)
                .PowerOn()
                .Finish("test", "Run cold water and the disposal for 30 seconds; check for leaks", null, 2f).Test();

            Svc("plb.wh-no-hot", "No hot water (electric water heater)", Hm, JobTrade.Plumbing, JobTrade.Electrical).At(JobPlaces.WaterHeater).Model("waterheater", 0.6f, 1.7f, 0.6f).Rep(20)
                .Says("We've had no hot water since yesterday. The tank is warm at the top but the showers go cold in a minute.",
                      "Only lukewarm water from every faucet. The water heater is about eight years old.")
                .Cause("lower", "Burnt-out lower element", 5, 1.25f, "Lower element reads open on the meter (no continuity); upper reads 12.8 ohms.", "drain", "lower", "refill")
                .Cause("upper", "Burnt-out upper element", 3, 1.25f, "Upper element reads open; with no upper heat the lower never gets power.", "drain", "upper", "refill")
                .Cause("eco", "Tripped high-limit (ECO)", 2, 0.5f, "Both elements read about 13 ohms. The red ECO reset on the upper thermostat had tripped.", "eco")
                .Cause("stat", "Failed upper thermostat", 2, 1f, "Elements are good, the ECO holds, but the upper thermostat never passes power down: bad thermostat.", "stat")
                .PowerOff("the 30 A double-pole breaker for the heater")
                .Prep("panel-off", "Remove both access panels and fold back the insulation", C.Screwdriver, 3f).Opens()
                .Diag("ohm", "Test each element for resistance and check the ECO reset", C.Meter, 4f).Need("panel-off", "power-off").Guard("power-off", Zap)
                .Fix("eco", "Press the red ECO reset on the upper thermostat (and find why it tripped)", null, 1f).Need("panel-off", "power-off")
                .Fix("stat", "Replace the upper thermostat: label the wires, swap the unit, rewire", C.Screwdriver, 5f).Need("panel-off", "power-off").Mat(JP.WhThermostat).Guard("power-off", Zap)
                .Prep("cold-off", "Close the cold supply to the tank", null, 1f).Opens()
                .Fix("drain", "Hose to the drain valve, open a hot faucet, drain below the element", C.Hose, 6f).Need("cold-off").Snd("water")
                .Fix("lower", "Unscrew the lower element with the element wrench, install the new one with a new gasket", JP.ElementWrench, 5f).After("drain").Mat(JP.Element).Need("power-off").Guard("power-off", Zap)
                .Fix("upper", "Unscrew the upper element, install the new one", JP.ElementWrench, 5f).After("drain").Mat(JP.Element).Need("power-off").Guard("power-off", Zap)
                .Fix("refill", "Close the drain, open the cold supply and a hot faucet until water runs steady (never power a dry element)", null, 4f).Need("cold-off").Snd("water")
                .Finish("cold-on", "Leave the cold supply open", null, 1f).Closes("cold-off")
                .Finish("panels-on", "Insulation and covers back on", C.Screwdriver, 2f).Closes("panel-off")
                .PowerOn()
                .Finish("test", "Wait an hour and check the temperature at the kitchen faucet (120°F)", null, 3f).Test().Wait(45f);

            Svc("plb.wh-relief", "Water heater relief valve dripping", Hm, JobTrade.Plumbing).At(JobPlaces.WaterHeater).Model("waterheater", 0.6f, 2.2f, 0.6f).Rep(20)
                .Says("The pipe on the side of the water heater keeps dripping into the pan.", "There's water around the water heater and the little pipe coming off the side is wet.")
                .Cause("valve", "Worn T&P valve seat", 4, 1f, "Static pressure is 65 psi, normal. The valve weeps steadily anyway: worn seat.", "drain", "valve", "refill")
                .Cause("expansion", "Thermal expansion (failed expansion tank)", 3, 1.25f, "Pressure climbs to 150 psi while the tank heats. The expansion tank is waterlogged: tap it, it's full.", "tank")
                .Cause("pressure", "Street pressure too high (PRV)", 2, 0.5f, "Static pressure reads 110 psi at the hose bib. The house PRV is set too high; adjust it to 60 psi.", "prv")
                .Diag("gauge", "Screw a pressure gauge onto the drain valve / hose bib and watch it through a heating cycle", "test-gauge", 4f)
                .Fix("prv", "Back the PRV adjusting screw off to 60 psi and lock it", C.Wrench, 2f)
                .PowerOff("the water heater breaker")
                .Prep("cold-off", "Close the cold supply to the tank", null, 1f).Opens()
                .Fix("drain", "Drain a few gallons below the valve", C.Hose, 4f).Need("cold-off")
                .Fix("valve", "Unscrew the T&P valve, tape the threads, install the new valve and the discharge pipe", "pipe-wrench", 5f).After("drain").Mat(JP.TpValve).Mat(JP.Ptfe)
                .Fix("tank", "Replace the expansion tank, pre-charged to the house pressure", "pipe-wrench", 5f).Need("cold-off").Mat(JP.ExpTank).Mat(JP.Ptfe)
                .Fix("refill", "Refill the tank and purge air at a hot faucet", null, 3f).Need("cold-off")
                .Finish("cold-on", "Leave the cold supply open", null, 1f).Closes("cold-off")
                .PowerOn()
                .Finish("test", "Watch the discharge pipe through a full heating cycle", null, 3f).Test();

            Svc("plb.hose-bib", "Leaking outside faucet", Hm, JobTrade.Plumbing).At(JobPlaces.HoseBib).Model("hosebib", 0.3f, 0.3f, 0.2f).Boost(cold: 2f).In(Seasons.All)
                .Says("The outside spigot drips constantly and won't shut off.", "When we turn on the hose bib water comes out around the handle — and now there's a wet spot inside the wall.")
                .Cause("washer", "Worn stem washer", 5, 0.5f, "Water runs out of the spout with the handle shut tight: the stem washer is worn.", "stem-out", "washer")
                .Cause("split", "Freeze-split pipe inside the wall", 2, 2f, "Frost-free stem leaks into the wall when the hose runs: the tube split from freezing (a hose was left on).", "open-wall", "sillcock")
                .Cause("packing", "Leaking packing", 3, 0.3f, "Water comes out around the handle stem only while it's open: packing nut.", "packing")
                .WaterOff("Close the interior shut-off for the hose bib and open the spigot to drain")
                .Diag("look", "Run the spigot and look at the spout, the handle stem and inside the wall", null, 3f)
                .Fix("packing", "Snug the packing nut a quarter turn", C.Wrench, 2f)
                .Fix("stem-out", "Unscrew the stem with the wrench", C.Wrench, 2f).Need("water-off").Guard("water-off", Flood)
                .Fix("washer", "Replace the stem washer and screw, grease, reinstall", C.Screwdriver, 3f).After("stem-out").Mat(JP.BibWasher)
                .Fix("open-wall", "Cut an access hole in the drywall behind it", C.JabSaw, 3f).Need("water-off")
                .Fix("sillcock", "Replace with a new 10\" frost-free sillcock with push-to-connect fitting, pitch it down to drain", "tubing-cutter", 6f).After("open-wall").Mat(JP.Sillcock).Mat(JP.PushCoupling)
                .WaterOn()
                .Finish("test", "Run it with the hose on, then shut it and watch the spout", null, 2f).Test();

            Svc("plb.tub-drip", "Dripping tub spout", Hm, JobTrade.Plumbing).At(JobPlaces.TubValve).Model("tubspout", 1.0f, 0.8f, 0.2f).Rep(10)
                .Says("The bathtub faucet drips all the time and stains the tub.", "The shower handle is stiff and the tub spout won't stop dripping.")
                .Cause("cartridge", "Worn valve cartridge", 6, 1f, "Drip continues with the handle off and the trim is tight: the cartridge seals are worn.", "cartridge")
                .Cause("diverter", "Worn diverter spout", 2, 0.4f, "Water squirts from the spout with the shower on; the cartridge doesn't drip: the spout diverter is shot.", "spout")
                .Diag("check", "Watch the spout with the valve off, then with the shower on", null, 3f)
                .Prep("main-off", "Turn off the house main (no stops on this valve)", null, 2f).Opens()
                .Prep("trim-off", "Pull the handle and escutcheon", C.Screwdriver, 3f).Opens()
                .Fix("cartridge", "Pull the retaining clip and old cartridge, install the matched cartridge in the same orientation", C.Pliers, 5f).Need("trim-off", "main-off").Mat(JP.TubCartridge).Guard("main-off", Flood)
                .Fix("spout", "Unscrew the tub spout and thread on a new diverter spout with tape", null, 3f).Mat(JP.Ptfe).Mat(JP.BibWasher)
                .Finish("trim-on", "Reinstall the escutcheon and handle, check the hot limit stop", C.Screwdriver, 3f).Closes("trim-off")
                .Finish("main-on", "Main back on, bleed air", null, 2f).Closes("main-off")
                .Finish("test", "Run the tub and the shower, then watch the spout", null, 2f).Test();
        }

        static void DefineElectrical()
        {
            Svc("el.breaker-trips", "Breaker keeps tripping", Hm, JobTrade.Electrical).At(JobPlaces.Panel).Model("panel", 0.4f, 0.8f, 0.4f).Rep(10)
                .Says("A breaker in the panel keeps tripping every time we run the microwave and the toaster.", "The bedroom breaker trips at random, sometimes in the middle of the night.")
                .Cause("overload", "Overloaded circuit", 5, 0.75f, "The circuit draws 17 A on a 15 A breaker with both appliances running: overload. Move a load to another circuit and explain it to the customer.", "advise")
                .Cause("breaker", "Worn-out breaker", 3, 0.75f, "It trips at 9 A and the breaker is warm and loose on the bus: bad breaker.", "breaker")
                .Cause("short", "Loose neutral at a receptacle", 2, 1.25f, "Meter shows an intermittent fault; one backstabbed receptacle in the run has a scorched neutral.", "device")
                .Diag("amps", "Clamp the meter on the circuit and run the loads", C.Meter, 4f)
                .Fix("advise", "Label the circuit and run the microwave on the dedicated kitchen circuit", null, 2f)
                .PowerOff("the main breaker")
                .Prep("cover-off", "Remove the panel dead front", C.Screwdriver, 3f).Opens()
                .Fix("breaker", "Unsnap the old breaker, move the wire to a new one, torque the lug", C.Screwdriver, 4f).Need("cover-off", "power-off").Mat(C.Breaker15).Guard("power-off", Zap)
                .Fix("device", "Find the bad receptacle and move the wires from the backstabs to the screws (new device)", C.Screwdriver, 5f).Need("power-off").Mat(C.Recept).Guard("power-off", Zap)
                .Finish("cover-on", "Dead front back on, label the directory", C.Screwdriver, 2f).Closes("cover-off")
                .PowerOn()
                .Finish("test", "Run the loads for five minutes", C.Meter, 3f).Test();

            Svc("el.gfci-chain", "Bathroom and kitchen outlets dead (GFCI)", Hm, JobTrade.Electrical).At(JobPlaces.KitchenGfci).Model("receptacle", 0.2f, 0.2f, 0.1f)
                .Says("Half the outlets in the kitchen and the bathroom stopped working. Nothing tripped in the panel.", "The outlet by the sink is dead and so is the one in the bathroom.")
                .Cause("tripped", "Tripped upstream GFCI", 4, 0.3f, "Tester shows no power at the counter; the GFCI that feeds them is tripped. It resets and holds.", "reset")
                .Cause("gfci", "Failed GFCI receptacle", 4, 0.75f, "The GFCI won't reset with power at the line terminals: the device is bad.", "plate-off", "gfci")
                .Cause("loose", "Loose line wire", 2, 0.75f, "The GFCI has no line power; the line hot is loose under its screw and blackened.", "plate-off", "tighten")
                .Diag("probe", "Test the outlets, find the GFCI that protects them and try the reset", C.Tester, 3f)
                .Fix("reset", "Press RESET firmly on the GFCI", null, 1f)
                .PowerOff()
                .Fix("plate-off", "Pull the cover plate and the device out of the box", C.Screwdriver, 3f).Need("power-off").Opens().Guard("power-off", Zap)
                .Fix("gfci", "Replace the GFCI: line to LINE, load to LOAD, pigtail grounds", C.Screwdriver, 5f).Need("plate-off", "power-off").Mat(C.Gfci).Guard("power-off", Zap)
                .Fix("tighten", "Strip back the burnt end and terminate it under the screw", "wire-strippers", 3f).Need("plate-off", "power-off").Guard("power-off", Zap)
                .Finish("plate-on", "Fold wires in, screw the device and plate on", C.Screwdriver, 2f).Closes("plate-off")
                .PowerOn()
                .Finish("test", "Press TEST then RESET and check every downstream outlet", C.Tester, 3f).Test();

            Svc("el.switch", "Light switch doesn't work", Hm, JobTrade.Electrical).At(JobPlaces.EntrySwitch).Model("switch", 0.15f, 0.2f, 0.1f)
                .Says("The switch by the front door stopped turning on the living room light.", "The light only comes on if you wiggle the switch.")
                .Cause("switch", "Worn-out switch", 5, 0.5f, "Power at the line terminal; nothing at the load side when on: the switch is worn out.", "plate-off", "switch")
                .Cause("backstab", "Loose backstab connection", 3, 0.5f, "The switch is fine but the hot is backstabbed and arcing; it flickers when you push the wire.", "plate-off", "screws")
                .Cause("bulb", "Burnt-out bulb", 2, 0.2f, "The switch passes power: the bulb in the fixture is just dead.", "bulb")
                .Diag("probe", "Flip the switch, check the bulb, test the switch terminals", C.Tester, 3f)
                .Fix("bulb", "Replace the bulb", null, 2f).Mat(JP.Bulb)
                .PowerOff()
                .Fix("plate-off", "Remove the plate and pull the switch out", C.Screwdriver, 2f).Need("power-off").Opens().Guard("power-off", Zap)
                .Fix("switch", "Replace the switch, wires under the screws clockwise", C.Screwdriver, 3f).Need("plate-off", "power-off").Mat(JP.Switch).Guard("power-off", Zap)
                .Fix("screws", "Move the wires from the backstabs to the screw terminals", C.Screwdriver, 3f).Need("plate-off", "power-off").Guard("power-off", Zap)
                .Finish("plate-on", "Switch and plate back on", C.Screwdriver, 2f).Closes("plate-off")
                .PowerOn()
                .Finish("test", "Flip it on and off a dozen times", null, 2f).Test();

            Svc("el.add-receptacle", "Add an outlet", Hm, JobTrade.Electrical).At(JobPlaces.NewOutlet).Model("receptacle", 0.2f, 0.2f, 0.1f).Rep(25).Rare(0.7f)
                .Says("We'd like another outlet on the living room wall for the TV; we're using extension cords now.")
                .Cause("standard", "Extend from the nearby receptacle", 6, 2f, "Stud finder: clear stud bay. The receptacle 6' away is mid-run on a 15 A circuit with room for the load.", "box", "fish", "wire-new", "wire-old")
                .Cause("blocked", "Fire block in the bay", 3, 2.75f, "A 2x4 fire block sits 24\" up in this bay: drill through it with the long bit before fishing.", "drill", "box", "fish", "wire-new", "wire-old")
                .Diag("scan", "Scan the wall with the stud finder and check the source outlet", JP.StudFinder, 3f)
                .PowerOff()
                .Fix("drill", "Drill through the fire block from the box hole", "ra-drill", 4f)
                .Fix("box", "Trace and cut the hole for an old-work box", C.JabSaw, 4f)
                .Fix("fish", "Fish 14/2 from the source outlet to the new hole", "fishtape", 5f).After("box").Mat(JP.Nm142, 8)
                .Fix("wire-new", "Set the old-work box and wire the new receptacle", C.Screwdriver, 5f).After("fish").Mat(JP.OldWorkBox).Mat(C.Recept).Mat(JP.Plate).Need("power-off").Guard("power-off", Zap)
                .Fix("wire-old", "Tie the new cable in at the source outlet", C.Screwdriver, 4f).After("fish").Need("power-off").Mat(JP.WireNuts, 2).Guard("power-off", Zap)
                .PowerOn()
                .Finish("test", "Plug in the tester: correct wiring, ground present", C.Tester, 2f).Test();

            Svc("el.fixture-swap", "Replace a ceiling light", Hm, JobTrade.Electrical).At(JobPlaces.LivingCeiling).Model("ceilinglight", 0.4f, 0.3f, 0.4f).Rare(0.8f)
                .Says("We bought a new ceiling light for the living room and need it installed.", "The old living room light is yellowed and cracked — please put up the new one we picked.")
                .Cause("standard", "Straight swap", 6, 0.75f, "The box is a metal pancake box fastened to the joist; the new fixture's bracket fits.", "down", "hang")
                .Cause("box", "Loose box / no ground", 3, 1.25f, "The old box is loose in the ceiling with no ground wire: replace with a fan/fixture rated old-work box.", "down", "rebox", "hang")
                .PowerOff("the lighting circuit")
                .Diag("look", "Drop the old globe and look at the box and wiring", C.Screwdriver, 3f).Need("power-off")
                .Fix("down", "Unwire the old fixture and lower it", C.Screwdriver, 3f).Need("power-off").Guard("power-off", Zap)
                .Fix("rebox", "Replace the box with a rated old-work box and add the ground", C.Screwdriver, 5f).After("down").Mat(JP.OldWorkBox)
                .Fix("hang", "Mount the bracket, connect black-to-black, white-to-white, ground, hang the new fixture", C.Screwdriver, 5f).After("down").Mat(JP.Fixture).Mat(JP.WireNuts, 3).Mat(JP.Bulb, 2).Need("power-off").Guard("power-off", Zap)
                .PowerOn()
                .Finish("test", "Switch it on", null, 1f).Test();

            Svc("el.light-flicker", "Flickering ceiling light", Hm, JobTrade.Electrical).At(JobPlaces.BedroomCeiling).Model("ceilinglight", 0.4f, 0.3f, 0.4f)
                .Says("The bedroom light flickers and sometimes goes out by itself.", "The ceiling light buzzes and blinks.")
                .Cause("bulb", "Loose bulb / incompatible dimmer", 3, 0.3f, "The bulb is loose in the socket; it's a non-dimmable LED on a dimmer.", "bulb")
                .Cause("socket", "Worn socket", 3, 0.75f, "The socket's center tab is flattened and scorched.", "fixture-down", "socket")
                .Cause("nut", "Loose wire nut", 4, 0.75f, "A wire nut in the box is loose on the neutral.", "fixture-down", "nut")
                .Diag("look", "Check the bulb and the socket tab with the power off", C.Tester, 3f)
                .Fix("bulb", "Fit a dimmable LED bulb", null, 2f).Mat(JP.Bulb)
                .PowerOff()
                .Prep("fixture-down", "Drop the fixture from the box", C.Screwdriver, 3f).Need("power-off").Opens()
                .Fix("socket", "Replace the lamp socket", C.Screwdriver, 4f).Need("fixture-down", "power-off").Mat(JP.Socket).Guard("power-off", Zap)
                .Fix("nut", "Re-twist the splice with a new wire nut", "lineman", 3f).Need("fixture-down", "power-off").Mat(JP.WireNuts).Guard("power-off", Zap)
                .Finish("fixture-up", "Remount the fixture", C.Screwdriver, 3f).Closes("fixture-down")
                .PowerOn()
                .Finish("test", "Run it for ten minutes, tap the fixture", null, 2f).Test();

            Svc("el.loose-outlet", "Loose outlet, plugs fall out", Hm, JobTrade.Electrical).At(JobPlaces.BedroomOutlet).Model("receptacle", 0.2f, 0.2f, 0.1f)
                .Says("The outlet in the bedroom is loose in the wall and plugs fall right out of it.", "The bedroom outlet sparks a little when we plug in the vacuum.")
                .Cause("worn", "Worn receptacle contacts", 5, 0.5f, "The contacts are spread: plugs pull out with no resistance.", "plate-off", "replace")
                .Cause("ears", "Box set too deep", 3, 0.6f, "The device ears don't reach the drywall; the receptacle wobbles. Add spacers/box extender.", "plate-off", "spacers")
                .Diag("check", "Plug in the tester and feel the grip", C.Tester, 2f)
                .PowerOff()
                .Fix("plate-off", "Pull the plate and device out", C.Screwdriver, 2f).Need("power-off").Opens().Guard("power-off", Zap)
                .Fix("replace", "Install a new tamper-resistant receptacle on the screws", C.Screwdriver, 4f).Need("plate-off", "power-off").Mat(C.Recept).Guard("power-off", Zap)
                .Fix("spacers", "Shim the device ears with spacers so it's tight to the wall", C.Screwdriver, 3f).Need("plate-off")
                .Finish("plate-on", "Plate back on", C.Screwdriver, 1f).Closes("plate-off")
                .PowerOn()
                .Finish("test", "Test with the plug-in tester", C.Tester, 2f).Test();

            Svc("el.smoke-chirp", "Smoke alarm chirping", Hm, JobTrade.Electrical).At(JobPlaces.HallCeiling).Model("smoke", 0.15f, 0.06f, 0.15f).Rare(1.2f)
                .Says("A smoke detector keeps chirping every minute and we can't figure out which one.", "The hallway smoke alarm beeps at night and won't stop.")
                .Cause("battery", "Low backup battery", 6, 0.25f, "Chirp every 40 s from the hall unit; the 9 V backup reads 7.4 V.", "battery")
                .Cause("old", "Alarm past its 10-year life", 4, 0.5f, "The date on the back is 2013: past its 10-year life. Replace the alarm.", "swap")
                .Diag("find", "Find which unit chirps and read the date on the back", null, 3f)
                .Fix("battery", "Replace the 9 V battery and hold TEST", null, 2f).Mat(JP.Battery9V)
                .PowerOff("the smoke alarm circuit")
                .Fix("swap", "Swap the alarm and its plug-in harness for a new unit", C.Screwdriver, 3f).Mat(JP.Smoke).Need("power-off").Guard("power-off", Zap)
                .PowerOn()
                .Finish("test", "Press and hold TEST: every alarm in the house sounds", null, 2f).Test();

            Svc("el.doorbell", "Doorbell doesn't ring", Hm, JobTrade.Electrical).At(JobPlaces.Doorbell).Model("doorbell", 0.1f, 0.15f, 0.1f).Rare(0.8f)
                .Says("The doorbell stopped working.", "Nobody hears delivery drivers, the doorbell is dead.")
                .Cause("button", "Corroded button", 5, 0.4f, "Jumping the two button wires rings the chime: the button is bad.", "button")
                .Cause("trans", "Dead transformer", 3, 0.75f, "No 16 V at the button or the chime: the transformer by the panel is dead.", "trans")
                .Diag("probe", "Pull the button, jump the wires, meter the transformer output", C.Meter, 3f)
                .Fix("button", "Install a new lighted button", C.Screwdriver, 2f).Mat(JP.BellButton)
                .PowerOff()
                .Fix("trans", "Replace the doorbell transformer at the junction box", C.Screwdriver, 5f).Mat(JP.BellTrans).Mat(JP.WireNuts, 2).Need("power-off").Guard("power-off", Zap)
                .PowerOn()
                .Finish("test", "Press the button: the chime rings", null, 1f).Test();

            Svc("el.bath-fan", "Bathroom fan loud or dead", Hm, JobTrade.Electrical, JobTrade.Hvac).At(JobPlaces.BathCeiling).Model("fan", 0.3f, 0.1f, 0.3f).Rep(10)
                .Says("The bathroom fan rattles like crazy.", "The bathroom fan doesn't pull anything and the mirror stays fogged.")
                .Cause("dirty", "Packed with dust", 4, 0.4f, "The blower wheel is caked with lint and the damper sticks.", "grille-off", "clean")
                .Cause("motor", "Worn motor bearings", 4, 1f, "The motor hums and the wheel turns slowly by hand: bearings are gone.", "grille-off", "motor")
                .Diag("look", "Hold a tissue to the grille, listen to the motor", null, 2f)
                .PowerOff()
                .Prep("grille-off", "Squeeze the springs and drop the grille", null, 2f).Opens()
                .Fix("clean", "Vacuum the housing and the wheel, free the damper", JP.WetVac, 3f).Need("grille-off", "power-off")
                .Fix("motor", "Unplug and swap the motor/blower assembly", C.Screwdriver, 4f).Need("grille-off", "power-off").Mat(JP.FanMotor).Guard("power-off", Zap)
                .Finish("grille-on", "Grille back on", null, 1f).Closes("grille-off")
                .PowerOn()
                .Finish("test", "Tissue test: it holds to the grille", null, 2f).Test();
        }

        static void DefineHvac()
        {
            Svc("hvac.weak-air", "Weak airflow / furnace short-cycles", Hm, JobTrade.Hvac).At(JobPlaces.Furnace).Model("furnace", 0.9f, 2.4f, 0.9f).Boost(cold: 1.6f, heat: 1.6f)
                .Says("The air coming out of the vents is weak and the furnace shuts off after a couple of minutes.", "Some rooms never warm up and the system runs all day.")
                .Cause("filter", "Clogged filter", 6, 0.3f, "The 1\" filter is gray and bowed; static pressure is way high.", "filter-out", "filter")
                .Cause("coil", "Dirty blower wheel", 2, 1.25f, "Filter is clean but the blower wheel is packed with dust.", "door-off", "wheel")
                .Diag("check", "Check the filter and measure the temperature rise", C.Meter, 3f)
                .Prep("filter-out", "Slide the old filter out", null, 1f).Opens()
                .Fix("filter", "Install a new 16x25x1 MERV 8 filter, arrow toward the furnace", null, 2f).Need("filter-out").Mat(JP.Filter)
                .Finish("filter-in", "Close the filter slot", null, 1f).Closes("filter-out")
                .PowerOff("the furnace switch")
                .Prep("door-off", "Remove the blower door", C.Screwdriver, 2f).Opens()
                .Fix("wheel", "Brush and vacuum the blower wheel", JP.WetVac, 5f).Need("door-off", "power-off").Guard("power-off", Zap)
                .Finish("door-on", "Blower door back on (the safety switch must close)", C.Screwdriver, 2f).Closes("door-off")
                .Finish("power-on", "Furnace switch back on", null, 1f).Closes("power-off")
                .Finish("test", "Run a heat cycle and check the temperature rise", C.Meter, 3f).Test();

            Svc("hvac.condensate", "Water leaking from the AC", Hm, JobTrade.Hvac).At(JobPlaces.Furnace).Model("furnace", 0.9f, 2.4f, 1.0f).Boost(heat: 2.5f).In(Seasons.Spring | Seasons.Summer | Seasons.Fall).Rep(15)
                .Says("There's water puddling around the furnace when the AC runs.", "The AC shut itself off and there's water by the furnace.")
                .Cause("trap", "Clogged condensate trap", 5, 0.75f, "The trap is full of algae slime and the drain pan is brim full.", "vac", "tabs")
                .Cause("line", "Clogged drain line", 3, 0.75f, "The trap is clear but the line downstream won't take water.", "vac", "flush")
                .Cause("pump", "Failed condensate pump", 2, 1f, "The pump's reservoir is full and the float switch clicks but the motor won't run.", "pump")
                .Diag("look", "Check the pan, the trap and pour water into the trap tee", C.Bucket, 3f)
                .Fix("vac", "Seal the wet/dry vac to the drain end and suck the clog out", JP.WetVac, 4f)
                .Fix("tabs", "Flush the trap with vinegar and drop tablets in the pan", null, 2f).Mat(JP.AlgaeTabs)
                .Fix("flush", "Flush the line with a hose until it runs clear", C.Hose, 3f)
                .PowerOff("the furnace")
                .Fix("pump", "Replace the condensate pump, run the safety switch to the thermostat circuit", C.Screwdriver, 6f).Mat(JP.CondPump).Need("power-off").Guard("power-off", Zap)
                .Finish("power-on", "Furnace back on", null, 1f).Closes("power-off")
                .Finish("clean", "Wet-vac the floor", JP.WetVac, 2f)
                .Finish("test", "Pour a quart into the pan and watch it drain", C.Bucket, 2f).Test();

            Svc("hvac.duct", "One room never gets air", Hm, JobTrade.Hvac).At(JobPlaces.BedroomCeiling).Model("register", 0.3f, 0.1f, 0.3f).Rep(20)
                .Says("The back bedroom is always freezing in winter and hot in summer. Barely any air comes out of the vent.", "No air comes out of the bedroom vent at all.")
                .Cause("off", "Flex duct fell off the takeoff", 5, 1f, "In the attic the flex duct has slipped off its takeoff collar; it's blowing into the insulation.", "hatch-open", "reattach")
                .Cause("crushed", "Crushed flex duct", 3, 1.5f, "The flex run is kinked flat under a stored box and crushed over a truss.", "hatch-open", "replace")
                .Cause("damper", "Damper closed", 2, 0.4f, "The balancing damper on the branch is cranked shut.", "hatch-open", "damper")
                .Diag("flow", "Hold a tissue to the register, then look at the duct run in the attic", null, 3f).Need("hatch-open")
                .Prep("hatch-open", "Open the attic hatch and set boards across the joists", null, 2f).Opens()
                .Fix("reattach", "Pull the liner over the collar, draw-band it, mastic, then the jacket", JP.ImpactDriver, 5f).Need("hatch-open").Mat(JP.DrawBands, 2).Mat(JP.Mastic)
                .Fix("replace", "Cut out the crushed section and run new 6\" insulated flex, banded and taped", C.Knife, 7f).Need("hatch-open").Mat(JP.Flex6).Mat(JP.DrawBands, 2).Mat(JP.FoilTape)
                .Fix("damper", "Open the damper and lock it", C.Screwdriver, 2f).Need("hatch-open")
                .Finish("hatch-close", "Close the hatch", null, 1f).Closes("hatch-open")
                .Finish("test", "Tissue test at the register", null, 2f).Test();

            Svc("hvac.thermostat", "Thermostat blank / system won't start", Hm, JobTrade.Hvac, JobTrade.Electrical).At(JobPlaces.Thermostat).Model("thermostat", 0.15f, 0.12f, 0.06f).Boost(cold: 1.5f, heat: 1.5f)
                .Says("The thermostat screen is blank and the heat won't come on.", "The thermostat says it's calling for heat but nothing happens.")
                .Cause("batt", "Dead batteries", 5, 0.25f, "The batteries read 0.9 V each.", "batt")
                .Cause("wire", "Loose R wire", 3, 0.5f, "24 V at the furnace board, nothing at the thermostat: the red wire has backed out of its terminal.", "stat-off", "wire")
                .Cause("stat", "Failed thermostat", 2, 0.75f, "24 V at R, jumping R to W fires the furnace: the thermostat itself is dead.", "stat-off", "stat")
                .Diag("meter", "Check the batteries and meter 24 V at R-C", C.Meter, 3f)
                .Fix("batt", "Replace the AA batteries", null, 1f).Mat(JP.AABatt)
                .PowerOff("the furnace")
                .Prep("stat-off", "Pull the thermostat off its sub-base", C.Screwdriver, 2f).Opens()
                .Fix("wire", "Re-strip and re-land the R wire", "wire-strippers", 2f).Need("stat-off")
                .Fix("stat", "Mount the new thermostat base, land R, W, Y, G, C", C.Screwdriver, 5f).Need("stat-off", "power-off").Mat(JP.Stat)
                .Finish("stat-on", "Snap the thermostat on", null, 1f).Closes("stat-off")
                .Finish("power-on", "Furnace back on", null, 1f).Closes("power-off")
                .Finish("test", "Call for heat and cool", null, 2f).Test();

            Svc("hvac.no-heat", "Furnace won't light", Hm, JobTrade.Hvac).At(JobPlaces.Furnace).Model("furnace", 0.9f, 2.4f, 0.9f).Boost(cold: 3f).In(Seasons.Fall | Seasons.Winter | Seasons.Spring).Rep(30)
                .Says("The furnace tries to start, clicks, and the burners shut off after a few seconds.", "No heat. The furnace light blinks three times.")
                .Cause("sensor", "Dirty flame sensor", 6, 0.75f, "Burners light then drop out after 4 s; flame signal 0.4 µA: dirty flame sensor.", "door-off", "sensor")
                .Cause("igniter", "Cracked hot surface igniter", 3, 1f, "The igniter never glows; it reads open on the meter, with a hairline crack.", "door-off", "igniter")
                .Diag("codes", "Read the blink code and watch a start-up through the sight glass", C.Meter, 4f)
                .PowerOff("the furnace")
                .Prep("door-off", "Pull the burner door", C.Screwdriver, 2f).Opens()
                .Fix("sensor", "Remove the flame sensor and polish the rod with emery cloth", C.Screwdriver, 4f).Need("door-off", "power-off").Mat(JP.Emery).Guard("power-off", Zap)
                .Fix("igniter", "Unplug and replace the hot surface igniter (don't touch the element)", C.Screwdriver, 4f).Need("door-off", "power-off").Mat(JP.Igniter).Guard("power-off", Zap)
                .Finish("door-on", "Burner door back on", C.Screwdriver, 1f).Closes("door-off")
                .Finish("power-on", "Power back on", null, 1f).Closes("power-off")
                .Finish("test", "Run three heat cycles; flame signal over 2 µA", C.Meter, 3f).Test();

            Svc("hvac.dryer-vent", "Dryer takes forever to dry", Hm, JobTrade.Hvac).At(JobPlaces.DryerVent).Model("ventcap", 0.2f, 0.2f, 0.15f).Rare(0.9f)
                .Says("Our dryer takes two or three cycles to dry a load and the laundry room gets hot.", "There's lint blowing out around the outside vent and the flap doesn't open.")
                .Cause("lint", "Vent run packed with lint", 6, 0.75f, "Barely any airflow at the hood; the run is packed with lint (a fire hazard).", "brush")
                .Cause("hood", "Stuck/broken hood flap", 3, 0.6f, "The run is clear but the hood flap is stuck shut with lint and the frame is cracked.", "hood")
                .Diag("flow", "Run the dryer and feel the flow at the outside hood", null, 3f)
                .Fix("brush", "Brush the whole run with the drill-driven vent brush, vacuum the lint", JP.VentBrush, 5f)
                .Fix("hood", "Replace the vent hood, seal it to the siding", C.Drill, 4f).Mat(JP.VentHood).Mat(C.CaulkExt)
                .Finish("clean", "Clean up the lint pile", JP.WetVac, 2f)
                .Finish("test", "Run the dryer: the flap blows wide open", null, 2f).Test();
        }
    }
}
