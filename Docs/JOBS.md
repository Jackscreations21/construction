# Job catalog (`Scripts/JobCatalog/`)

Every paid job in Groundbreak can be written as **data**: a `JobDef`, registered with `JobCatalog.Register` (or added to one of the
`Defs*.cs` files). The catalog's generator fills `Company.Offers` for every company stage, and each job plays through the systems
that already exist:

| Mode | Stage | Plays through | Verified by |
|---|---|---|---|
| `Service` | Handyman+ | `RepairJobs` (Job.Kind `Repair`, id `jc-N`): one `CatalogDefect` per job at a customer house | the defect's steps: `IsFixed` when the hidden cause's fix steps are done and every opened state (water off, cover off) is closed again |
| `Scope` (remodel) | Contractor | `RepairJobs` (Job.Kind `Remodel`, id `jcr-N`): one `CatalogDefect` per **task** at the same house, billed time and materials | each task as above |
| `Fleet` | Contractor | `ContractorJobs` (deck, fence, shed, garage, driveway, patio, reroof), sized and priced by the definition (id `cj-<type>-jc_<def>-L<lot>`) | `ContractorJobs.Verify` (deck stages, slab size/psi, roof, fence...) |
| `Build` | Builder / Developer | Development `HouseProject` on a free `DevLot` (Job.Kind `House`) with Builder's draw schedule | phase inspections; final draw at the CO |

The 14 hand-built repair families from `Scripts/Repairs/` are in the catalog as `legacy.*` entries, so one generator offers them all.

## The format

```csharp
Svc("plb.toilet-running", "Running toilet", CompanyStage.Handyman, JobTrade.Plumbing)   // id, name, stage, trade(s)
    .At(JobPlaces.Toilet).Model("toilet", 0.45f, 0.8f, 0.7f)        // where (house spot + layout) and the 3D model (w, h, d in m)
    .Says("The toilet keeps running ...", "Our water bill doubled ...")  // what the customer says (one is picked per job)
    .Rep(0).Rare(1f).In(Seasons.All).Boost(rain: 1f, cold: 1f, heat: 1f)  // reputation gate, rarity weight, seasons, weather boosts
    // hidden causes: id, name, likelihood, flat-rate book hours, what diagnosis shows, the step ids that fix it
    .Cause("flapper", "Warped flapper", 5, 0.5f, "Dye shows up in the bowl ...", "flapper")
    .Cause("fill", "Fill valve not shutting off", 3, 0.75f, "Water rises past the line ...", "fill")
    // steps: Prep / Diag / Fix / Finish (id, label, tool id or null for hands, hold seconds)
    .Prep("lid-off", "Lift the tank lid off", null, 1.5f).Opens()            // opens a state that must be closed again
    .Diag("dye", "Put dye in the tank and watch the bowl", null, 4f).Need("lid-off")
    .Prep("water-off", "Close the stop and flush").Opens().Need("lid-off")
    .Fix("flapper", "Fit a new 2\" flapper", null, 3f).Need("water-off").Mat(JP.Flapper)
    .Fix("fill", "Replace the fill valve", C.Pliers, 6f).Need("water-off").Mat(JP.FillValve).Guard("water-off", "Water sprays out ...")
    .Finish("water-on", "Open the stop").Closes("water-off")
    .Finish("lid-on", "Lid back on").Closes("lid-off")
    .Finish("test", "Flush and listen").Test();
```

Step modifiers (apply to the last step): `Mat(id, n)` parts used from the van, `After(ids)` steps that must be done first,
`Need(ids)` states that must be open now, `Opens()` / `Closes(id)`, `Guard(state, hazard)` (doing it without that state open costs
quality and makes a mess), `Wait(min)` + `Dry()` (curing time before the next step), `Say(text)` / `SayIf(cause, text)` result text,
`Test()` (result depends on whether it's fixed), `Opt()` (optional finish step).

**How it plays** (`CatalogDefect`): the customer's words are shown; diagnosis reveals the hidden cause's finding; doing a fix step that
belongs to a *different* cause is a wrong repair (−15% quality, the part is still billed); skipping diagnosis −10%, each skipped finish
step −8%; guards −20%. The invoice is book hours of the real cause × $85 + parts × 1.2 + $65 trip, scaled by satisfaction (Repairs).
Tools and parts are derived from the steps (`RequiredTools`, `RequiredMaterials`) and shown on the offer.

**Where** (`JobPlaces`): a `HouseSpot` anchor (the house must have it — new map houses built by `HouseBuilder` qualify automatically)
plus an optional house-local pose from the house's documented layout (toilet, water heater, furnace, ceilings, fence bays...).

**Models** (`CatalogProps`): ~45 real-size procedural models (toilet, water heater, panel, furnace, sash window, fence bay, gate,
skylight, brick planter, retaining wall, pergola ...) that redraw after every step (lid off, cover off, damage gone).
Add one with `CatalogProps.Register("id", (m, ctx) => ...)`.

**Contractor jobs on Fleet types**: `Fleet(id, name, type, widthFt, lengthFt, $/unit, days, rep, vehicle, say)`, unit = sq ft,
linear ft (fence) or squares (reroof). **Builds**: `Build(id, name, stage, buildingType, $/sq ft, rep, say, planIds...)`: the first
plan id that `HousePlans.Get` finds is used (ids from the blueprints library: `garage-2car`, `polebarn-30x40`, `workshop-30x40`, `office-3000`, `warehouse-6000`, `stripmall-6000`, `diner-3000`, `gasstation-3200`, `townhome-row4`, `adu-480` …; the six Development plans come first where they fit); missing blueprint ids are skipped, and a build with no plan is not offered (or uses
`FallbackFleet`).

## The generator (`JobCatalog.Generate`, replaces `RepairJobs.GenerateOffers`)
Runs at each new day (through `RepairJobs.ProcessDay`) and on stage changes. Board targets: service calls 2/3/5 by reputation for a
Handyman (2–3 later), contractor jobs 3 (Contractor) / 4 (Crew+), builds 2 (Builder) / 3 (Developer). Picks are weighted by
`Weight` × stage distance (older-stage work gets rarer) × weather (`RainBoost` when raining, `ColdBoost` < 40°F, `HeatBoost` > 85°F),
filtered by `Stage`, `MinRep` and `Season` (career starts in April, 30-day months), never repeating a definition from the last 16 picks
or one already on the board. At reputation 60+ a Handyman call may be a two-item punch list.

## Adding a job
1. Pick the mode. For a service call add a `Svc(...)` to the matching `Defs*.cs` (or call `JobCatalog.Register` from your own
   feature with a `JobDef`). Give **at least two causes** with different fixes.
2. Use tool ids from the tool shop (`ToolFactory`) and part ids from the `Catalog`; add new parts in `JobParts.cs` (`JP`).
3. Use an existing `JobPlaces` entry or add one; use an existing model or register a new one.
4. Run `JobCatalogTests.AllDefinitionsValidate`: it checks every id, tool, part, step link, open/close pair, price and hours range,
   that the place resolves on a real house and that every cause is solvable without a wrong repair.

## Job ids
### Service (61)

| id | name |
|---|---|
| `plb.low-pressure` | Low water pressure at the kitchen faucet |
| `el.afci-trip` | Bedroom breaker trips (AFCI) |
| `ext.siding` | Damaged vinyl siding |
| `ext.fascia` | Rotted fascia board |
| `ext.skylight` | Skylight leaks |
| `ext.roof-leak` | Roof leak |
| `ext.gutter-leak` | Gutter leaking at a seam / downspout |
| `ext.deck-ledger` | Deck pulling away from the house |
| `ext.fence-section` | Fence section leaning |
| `ext.gate-sag` | Gate sags and won't latch |
| `ext.soffit` | Animals in the soffit |
| `ext.porch-light` | Porch light doesn't work |
| `mas.cracked-step` | Cracked front step |
| `mas.spalled-walk` | Flaking (spalled) sidewalk |
| `mas.tuckpoint` | Crumbling mortar joints |
| `mas.walk-crack` | Cracked front walk |
| `carp.rotted-sill` | Rotted window sill |
| `carp.door-trim-rot` | Rotted door trim at the bottom |
| `carp.door-sag` | Door sags and rubs at the top |
| `carp.squeaky-floor` | Squeaky floor |
| `carp.stair-tread` | Cracked deck stair tread |
| `carp.window-sash` | Window won't stay open / painted shut |
| `carp.cabinet-door` | Cabinet door hangs crooked |
| `carp.drawer` | Kitchen drawer falls out |
| `carp.baseboard` | Loose baseboard |
| `dw.ceiling-stain` | Water stain on the ceiling |
| `dw.wall-crack` | Crack in the wall |
| `dw.nail-pops` | Nail pops in the drywall |
| `dw.repaint-room` | Repaint a room |
| `dw.corner-bead` | Dented outside corner |
| `flr.cracked-tile` | Cracked floor tile |
| `flr.regrout-tub` | Moldy, cracked shower grout |
| `flr.lvp-plank` | Plank floor lifting |
| `flr.transition` | Loose floor transition strip |
| `plb.toilet-running` | Running toilet |
| `plb.toilet-rocking` | Toilet rocks and leaks at the base |
| `plb.toilet-clog` | Clogged toilet |
| `plb.supply-stop` | Leaking toilet shut-off valve |
| `plb.vanity-drain` | Slow bathroom sink drain |
| `plb.kitchen-faucet-drip` | Dripping kitchen faucet |
| `plb.disposal` | Garbage disposal hums but won't spin |
| `plb.wh-no-hot` | No hot water (electric water heater) |
| `plb.wh-relief` | Water heater relief valve dripping |
| `plb.hose-bib` | Leaking outside faucet |
| `plb.tub-drip` | Dripping tub spout |
| `el.breaker-trips` | Breaker keeps tripping |
| `el.gfci-chain` | Bathroom and kitchen outlets dead (GFCI) |
| `el.switch` | Light switch doesn't work |
| `el.add-receptacle` | Add an outlet |
| `el.fixture-swap` | Replace a ceiling light |
| `el.light-flicker` | Flickering ceiling light |
| `el.loose-outlet` | Loose outlet, plugs fall out |
| `el.smoke-chirp` | Smoke alarm chirping |
| `el.doorbell` | Doorbell doesn't ring |
| `el.bath-fan` | Bathroom fan loud or dead |
| `hvac.weak-air` | Weak airflow / furnace short-cycles |
| `hvac.condensate` | Water leaking from the AC |
| `hvac.duct` | One room never gets air |
| `hvac.thermostat` | Thermostat blank / system won't start |
| `hvac.no-heat` | Furnace won't light |
| `hvac.dryer-vent` | Dryer takes forever to dry |

### Task (24)

| id | name |
|---|---|
| `task.vanity` | Replace the bathroom vanity and faucet |
| `task.toilet` | Replace the toilet |
| `task.tub-valve` | Replace the tub/shower valve |
| `task.regrout` | Regrout and seal the tub surround |
| `task.bath-floor` | Retile the bathroom floor |
| `task.bath-fan` | Replace the bath exhaust fan |
| `task.kitchen-faucet` | Install a new kitchen faucet |
| `task.disposal` | Install a garbage disposal |
| `task.cabinets` | Paint the cabinets and new hardware |
| `task.paint-living` | Paint the living room |
| `task.paint-bedroom` | Paint the bedroom |
| `task.ceiling` | Paint the bedroom ceiling |
| `task.lvp-living` | New vinyl plank floor in the living room |
| `task.window-living` | Replace the living room window |
| `task.window-bedroom` | Replace the bedroom window |
| `task.entry-door` | Replace the front entry door |
| `task.siding-wall` | Reside the front wall |
| `task.fence-bay` | Rebuild a fence section |
| `task.fence-bay2` | Rebuild a second fence section |
| `task.gate` | Build a new gate |
| `task.retaining-wall` | Build a block retaining wall |
| `task.pergola` | Build a cedar pergola |
| `task.deck-ledger` | Re-lag and flash the deck ledger |
| `task.deck-step` | Rebuild the deck step |

### Scope (12)

| id | name |
|---|---|
| `rem.bath-refresh` | Bathroom refresh |
| `rem.bath-full` | Full bathroom remodel |
| `rem.kitchen-refresh` | Kitchen refresh |
| `rem.windows` | Window replacement package |
| `rem.entry-door` | Entry door replacement |
| `rem.interior-paint` | Interior repaint |
| `rem.flooring` | Living room flooring |
| `rem.reside-front` | Re-side the front of the house |
| `rem.fence-repair` | Fence and gate repair package |
| `rem.retaining-wall` | Block retaining wall |
| `rem.pergola` | Cedar pergola |
| `rem.deck-safety` | Deck safety repair |

### Fleet (22)

| id | name |
|---|---|
| `deck.10x12` | low pressure-treated deck |
| `deck.12x16` | pressure-treated deck |
| `deck.16x20` | large pressure-treated deck |
| `deck.12x20-high` | raised pressure-treated deck |
| `fence.100` | cedar privacy fence |
| `fence.140` | cedar privacy fence |
| `fence.180` | cedar privacy fence with two gates |
| `shed.8x12` | gable storage shed |
| `shed.10x12` | gable storage shed |
| `shed.12x16` | gable workshop shed |
| `garage.20x20` | detached 2-car garage |
| `garage.22x24` | detached 2-car garage |
| `garage.24x24` | detached 2-car garage |
| `drive.10x30` | concrete driveway |
| `drive.12x40` | concrete driveway |
| `drive.16x40` | wide concrete driveway |
| `patio.10x12` | concrete patio |
| `patio.12x14` | concrete patio |
| `patio.16x20` | large concrete patio |
| `reroof.ranch` | re-roof (ranch) |
| `reroof.wide` | re-roof (wide ranch) |
| `reroof.steep` | re-roof, steep 8/12 |

### Build (19)

| id | name |
|---|---|
| `bld.starter` | Starter ranch for a young couple |
| `bld.cottage` | Cottage for retirees |
| `bld.ranch` | Family ranch |
| `bld.craftsman` | Craftsman bungalow |
| `bld.colonial` | Two-story colonial |
| `bld.estate` | Custom estate home |
| `bld.farmhouse` | Modern farmhouse |
| `bld.duplex` | Duplex rental |
| `bld.garage-apt` | Detached garage |
| `bld.pole-barn` | Pole barn |
| `bld.workshop` | Workshop building |
| `bld.office` | Small office building |
| `bld.warehouse` | Warehouse |
| `bld.retail` | Retail strip building |
| `bld.splitlevel` | Split-level home |
| `bld.adu` | Backyard ADU |
| `bld.townhomes` | Townhome row |
| `bld.diner` | Diner |
| `bld.gas-station` | Gas station and store |

### Legacy (14)

| id | name |
|---|---|
| `legacy.sink-leak-kitchen` | Leak under kitchen sink |
| `legacy.sink-leak-bath` | Leak under bathroom vanity |
| `legacy.dead-outlet` | Dead outlet |
| `legacy.dead-outlet-kitchen` | Dead kitchen outlet |
| `legacy.drywall-hole-living` | Hole in the living room wall |
| `legacy.drywall-hole-bedroom` | Hole in the bedroom wall |
| `legacy.sticking-door` | Sticking bedroom door |
| `legacy.deck-board` | Broken deck board |
| `legacy.fence-picket` | Broken fence picket |
| `legacy.shingles` | Missing shingles |
| `legacy.window-caulk` | Window caulking |
| `legacy.tub-caulk` | Moldy tub caulk |
| `legacy.gutter` | Overflowing gutter |
| `legacy.sidewalk` | Broken sidewalk square |
