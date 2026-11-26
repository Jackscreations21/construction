# yard-fleet handoff (Contractor stage)

Branch `claude/yard-fleet-6f0301`. Folders: `Scripts/Fleet/`, `Scripts/Fleet/Jobs/`, `Scripts/Yard/`. Tests: `Tests/FleetYardTests.cs`, `Tests/ContractorJobsTests.cs`.
**No shared/Core file was edited.**

## What exists
- **FleetCatalog**: van, pickup, flatbed, dump, box, boom, skidsteer, with real payload, bed size, cu ft, longest stock (with or without a rack), new and used price, mpg, and insurance/maintenance per day.
- **Cargo**: `Check(type, rack, items)` checks weight, volume and length. Loose bulk (cu yd) needs `dump`; trusses need `boom`; the skid steer carries no cargo.
- **Fleet**: `Buy(type, used, termDays)`: cash, or a down payment plus an auto loan; calls `Company.AddVehicle`. Also `Sell`, `AddRack`, `Service`, and `Haul` (supplier → site or warehouse; materials + fuel, no delivery fee). `HaulFromYard` loads a truck from the warehouse and takes it to the site. Trips take `Fleet.TripSeconds`. Daily costs: "Vehicle insurance", "Vehicle maintenance", "Fuel".
- **Finance**: bank loan within a credit line (scaled by stage, revenue and reputation), vehicle loans, and daily level payments ("Loan payments"; 1 game day = 1 business week for the APR). The P&L ledger is built from the `Game.Wallet.Log` reasons (`Finance.Categorize`). Bills unpaid 3 days running trigger `DeclareBankruptcy`: financed trucks repossessed, the fleet (except one van) and warehouse contents auctioned, the lease ended, debt discharged, reputation -15, and the stage reset to what the company still owns. **Bankruptcy is the one place that sets `Company.Stage` down.**
- **Yard**: levels 1–3 with move-in cost, rent, insurance, storage cu ft and bulk discount. `Lease` sets `HasWarehouse`/`WarehouseLevel` and calls `RecomputeStage`. Also stock (`Yard.Stock`), equipment (saw station, compressor, forklift, fuel tank, lift; the fuel tank and lift lower costs), `OrderDelivered` (supplier delivery to the warehouse, $79 fee), `ReturnFromSite`, and `EndLease`.
- **YardSite**: the walkable premises at `Yard.Position` (from `TownHook.HQPosition`, fallback (-230, 0, -180)). Levels: flex bay with neighbour bays, fenced yard + 40x60 shop, HQ 60x120 with a two-storey office and dock. Each has racking, lumber racks, bunkers, an office, a workbench/tool storage and parking stalls; stored stock, parked vehicles and equipment are rebuilt on change.
- **Tablet apps**: "Dealership", "Company" (stage + next-stage checklist, daily costs, P&L, loans), "Warehouse" (lease, equipment, stock, material runs), "Contractor Jobs".
- **ContractorJobs** (Fleet/Jobs): deck, fence, shed, garage, driveway, patio, re-roof. Each has a scope, takeoff, required vehicle, deadline and payout ($2k–50k), and is verified against the real trade systems on the job lot. The lot is set up on accept and rebuilt after load. `TryComplete` calls `Company.Complete`.
- **Save**: key `"yardfleet"` (stock, equipment, vehicle info, trips, loans, ledger). Jobs persist via Company.

## After the Town merge
- `TownHook` finds `Groundbreak.Town.HQPosition` by reflection. Set `TownHook.JobLotProvider` to hand out real town lots for contractor jobs.
- Replace `YardFleetRuntime.GoToYard` (a teleport) with the Town fast-travel/driving controller.
- The supplier store in `Tablet.cs` has no delivery fee. If the lead wants owning a truck to matter more, add one in Career mode there; `Fleet.DeliveryFee = 79` is ready.

## Known simplifications
- Vehicles in the yard are static models: no driving here, by design (the town session owns driving).
- Bay interior stock is drawn from real piece models, capped at 24 per stack.
