# Handoff: town-repairs (Handyman stage)

Branch `claude/town-repairs-b14d3c` (main merged in at 4803bd1). API doc: `Docs/TOWN.md`.

## Built
- **Town** (`Scripts/Town/`): `Town` API (lots, addresses, parking, `HQPosition`, `FreeLots`, `Claim`/`Release`, save key `"town"`),
  `TownBuilder` (streets with curbs, planting strip, 5' sidewalk squares, grass ramp, driveways + curb-cut aprons, double yellow lines, stop bars,
  street signs, barricades, mailboxes, trees, for-sale signs, Hartley's Hardware with parking lot and pixel-letter sign),
  `HouseBuilder` (17 customer ranches + the player's home: enterable, kitchen with open sink base, bath, bedroom, deck, fence, gutter,
  walkable roof, 17 `HouseSpot` repair anchors + fixtures), `TownDoor` (E to open/close, `Sticking`).
  About 290 renderers and 460k triangles for the whole town.
- **New career** (`HandymanStart`): `MainMenu.NewGameStarted` (an additive event on a shared file) → town, $2,500, `Company.AddVehicle("van")`, van starter
  kit, starter hand tools, first repair offers, player in the home driveway.
- **Work van** (`Scripts/Town/Van/`): real-size cargo van, E to drive (W/S/A/D, Space brake, V chase cam), `FastTravel`, `VanCargo` (volume +
  payload + roof rack + sheet limits), `TownLadder` (take/place/climb), tablet apps "Van" and "Hardware Store" (buy when parked at the store),
  save key `"van"`.
- **Repairs** (`Scripts/Repairs/`): 10 repair families with hidden causes to diagnose (sink leak, dead outlet, drywall hole, sticking door,
  deck board, fence picket, missing shingles, caulk, clogged gutter, broken sidewalk square), hold LMB with the right tool, Q/Z/scroll picks the
  action, B sends the invoice on site. The generator fills `Company.Offers` daily, scaled by reputation. Invoices are labour + materials +20% + trip charge,
  then satisfaction, tips and callbacks. Tablet app "Jobs" has navigate and drive-there buttons. Defect progress is saved in `Job.Payload`; the `"repairs"` key holds
  invoices, callbacks and the waypoint.
- **Bindings** (`TownBindings`): Fleet's `TownHook.JobLotProvider` uses real addresses. The Jobs, Van and Hardware Store apps are in the "Business" tablet group.

## Tests
`TownTests` (3), `WorkVanTests` (6: spawn, drive to a job, fast travel, cargo limits, ladder climb, save), `RepairJobTests` (6: generator,
sink, outlet, drywall + save round trip, exterior punch list + sidewalk, sloppy job → callback). Screenshots: `Logs/shots/town_*`, `van_*`, `repair_*`.
Last full run: 83/85 passed. UICapture is skipped by design. `SmartPlacementTests.SecondTopPlateSnapsFlush` fails only in the full run; it
passes alone (order-dependent; being checked against the repair tests).

## Left / ideas
- Van model polish (wheel placement, A-pillars); the sliding side door is model only; the cargo bay is not walkable.
- The repair screenshot framings were guessed in places (sink leak drip barely visible).
- Fleet has its own van model in `VehicleModels`; the drivable `WorkVan` is separate. Merge them if Fleet shows parked vans at the yard.
- Workers (Crew) could perform repairs through `RepairDefect.Perform(id, RepairContext)`.
