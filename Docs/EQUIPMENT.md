# Heavy equipment (`Assets/Groundbreak/Scripts/Equipment/`)

Machines the player climbs into and operates: mini excavator, skid steer (bucket + pallet forks), 40' telescopic boom lift,
crane truck (boom truck). They do real work through the existing ground, framing and roofing systems.

## Using them (player)
- **Get one:** tablet app **Equipment**. Career: from the Contractor stage; rent by the day (first day + $175 trailer delivery
  charged now, then the daily rate each new game day under "Equipment rental"), or buy the mini excavator / boom lift
  outright (insurance + upkeep per day). The skid steer and crane truck are fleet vehicles: buy them at the Dealership and
  they count as owned here. **Free Build:** every machine, "Bring to site", no charge.
- Delivered on a 20' equipment trailer to a clear spot near you (the trailer leaves when you first climb in). The crane truck drives in.
- Walk up, **E: climb in**. **E** climbs out (stopped). **V** cab / outside camera. Mouse looks around.

| Keys | Mini excavator | Skid steer | Boom lift | Crane truck |
|---|---|---|---|---|
| W/S A/D | tracks | drive / skid turn | drive (slow when raised) | drive (outriggers up, boom stowed) |
| ←/→ | swing house | – | turntable | swing |
| ↑/↓ | boom up/down | lift arms | boom up/down | boom up/down |
| I/K | stick out/in | – | telescope out/in | telescope out/in |
| J/L | bucket curl / dump | curl / dump | – | – |
| Y/H | – | – | – | hoist up / down |
| F | – | pick up / set down pallet | let go of controls (work from the basket) / take controls | rig load / set truss / release |
| B | blade up/down | swap bucket / forks | – | outriggers |

## How it hooks into the game
- **Digging** (`Earth`): on the building pad it cuts `SiteGround` (4" cells — footings, trenches, basements); on the lot
  it cuts `Grade.Field`. A bucket pass removes ~0.11 m³ (a shovel click is ~0.003 m³). Spoil dumps into a pile
  (`Earth.Deposit`, swell 1.25) or into a parked dump truck (`HaulTruck`, 5 cu yd loads; an owned dump truck is free,
  otherwise $385/load). The skid steer cuts high spots into its bucket and back-drags to fill low spots (grading).
- **Pallets** (`EquipmentLoad`): shingles (full 3,100 lb / half 1,550 lb), OSB lift (1,760 lb), CMU block (3,150 lb).
  Skid steer forks refuse anything over its 2,690 lb rated operating capacity. The crane can lift them to the roof.
- **Trusses:** the crane finds the nearest truss `FramingJob` whose current step is "Set truss…" / "Set gable-end truss…",
  rigs a truss from the bed bundle, and when the hanging truss is within 0.6 m / 0.5 m of the step's ghost, **F** sets it:
  `FramingJob.TryAdvance(null)` builds the real truss (stock still required in Career).
- **Load chart** (`TruckCrane.CapacityLb`): 24,000 lb at 10' … 3,900 lb at 50' … 2,100 lb at 66', none beyond. The load moment
  indicator blocks boom-down / telescope-out / hoist-up when over the chart.
- **Boom lift:** platform floor and rails are colliders; **F** hands the rig back so you stand in the basket at height with your tools.
- **Save:** key `"equipment"` (owned, rentals with days billed, machines on site with pose + joints).

## Code map
`Machine` (base: enter/exit, controls, travel, ground follow, HUD), `MiniExcavator`, `SkidSteer`, `BoomLift`, `TruckCrane`,
`EquipModels` (tracks, wheels, seat, `HydraulicRam` that really telescopes), `Earth` + `HaulTruck`, `EquipmentLoad`,
`Equipment` (catalog, rent/buy, delivery, tablet app, save, on-foot prompt). Tests: `Tests/EquipmentTests.cs`, shots `equip_*.png`.
