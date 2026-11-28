# Blueprints — plan library for houses, outbuildings, commercial and industrial buildings

Code: `Assets/Groundbreak/Scripts/Blueprints/`. Tests: `Assets/Groundbreak/Tests/BlueprintTests.cs` (shots `Logs/shots/bp_*.png`).

## Lookups
- `HousePlans.Get(id)` works for every plan (all are registered into `HousePlans`, so the Plans app, overlay, Builder and Developments see them).
- `Blueprints.Get(id)`, `GetBlueprint(id)` (as `Blueprint`), `OfCategory("garage")`, `OfGroup(PlanGroup.Industrial)`, `Buildable(CompanyStage)`,
  `Find(category, minSqFt, maxSqFt, maxWidthFt, maxDepthFt)`, `Pick(category, seed)`, `Categories`, `Info(plan)` (Blueprint view of a plain HousePlan).
- Every `HousePlan` now has `Category` (type id), `Group` (Residential / Outbuilding / Commercial / Industrial) and `MinStage` (lowest `CompanyStage` that may build it).

## Plans (50)
| group | ids |
|---|---|
| Residential (Builder) | starter-900 (Development, plain), starter-1008, ranch-1500, ranch-1600, ranch-1650-1car, ranch-1650-2car, ranch-1770-2car, ranch-1770-3car, cottage-1200, craftsman-1920, twostory-2240, twostory-2400, estate-3500, splitlevel-2280 |
| Residential (Developer) | duplex-1664, townhome-row4, townhome-row6 |
| Residential (Crew) | adu-480, adu-carriage-672 |
| Outbuilding | garage-1car, garage-2car, garage-2car-shop, garage-3car (Contractor); shed-8x10 (Handyman), shed-10x12, shed-12x16 (Contractor); polebarn-30x40 (Contractor), polebarn-40x60, polebarn-60x80, barn-36x48 (Crew); workshop-30x40 (Contractor), workshop-40x60 (Crew) |
| Commercial | retail-2400, retail-5000, diner-3000, office-3000, storefront-24, storefront-30, bank-2800 (Builder); stripmall-6000, gasstation-3200, grocery-20000 (Developer) |
| Industrial | warehouse-6000, warehouse-12000, flex-6000, yard-shop-2400, yard-hq-7200 (Builder); warehouse-24000, warehouse-50000 (Developer); yard-bay-1500 (Contractor) |

Development's ranch-1600, cottage-1200, craftsman-1920, twostory-2400 and estate-3500 are redrawn as Blueprints under the same id, name and footprint.
The originals had unreachable rooms, baths under 5' wide and two-stories with no stair.

## Downtown plans (map-commercial)
- **storefront-24 / storefront-30**: two-story Main Street row building 24'/30' x 70' (fits the 25'-33' downtown lots). Shop below (storefront glazing,
  sales floor, storage, ADA restroom, janitor, mech, stockroom, rear exit corridor); two-bedroom apartment above with its **own street door** into an
  entry hall that is walled off from the shop (`Blueprint.Private`), a straight 48" stair (24 risers @ 6-3/4", 11" treads, 3' landing at the bottom,
  the upstairs hall at the top, handrails both sides in the takeoff), bedrooms with egress windows on the back wall. `PartyWalls = true`: no windows
  on the side walls. 12' floors, 16" open-web floor trusses on two bearing lines, type X ceiling at the 1-hr occupancy separation. Only two-story
  map storefronts get these (`MapPlans.StoriesFit`); 1- and 3-story ones keep retail-2400 / no plan.
- **bank-2800**: 40'x70' branch: 14' banking lobby, teller line behind a secured door, manager / loan / conference offices, safe deposit storage,
  break room, two ADA restrooms, staff corridor to a rear exit. On the downtown First State Bank.
- **grocery-20000**: 200'x100': 14,000 sq ft sales floor with entrance + exit, receiving stockroom with a dock door, walk-in cooler and freezer behind
  the cases, offices / break room / restrooms / mech off a back corridor, sprinklered (M fire area > 12,000 sq ft). On Harvest Foods.
- Format additions: `Blueprint.Private(room)` (walled off except to stairs), `Blueprint.PartyWalls`; building takeoffs of full upper floors
  (no mezzanine) now include floor framing, subfloor, girders, the type X separation ceiling and the stairs. Both flags copy into the Plan Designer.

## Format additions (`Blueprint : HousePlan`, all additive)
- Systems: `WallFrame` (WoodStud / PostFrame / SteelFrame / Cmu), `RoofFrame` (Trusses / Rafters / PostFrameTrusses / RigidFrame / BarJoist), `Covering`,
  `FloorJoist` + `BearingLines`, `InteriorColumnLines`, `SlabIn`, `MezzanineFt` (floor 1 is a loft/mezzanine), `Units` (side-by-side dwellings/tenants),
  `Occupancy`, `ConstructionType`, `Finished`, `Conditioned`, `RidgeAlongY`, `Notes`.
- New enum values: `RoofType.Shed/Flat/Gambrel`, `FoundationType.PostFrame/Skids`. Overhead doors (`PieceCategory.GarageDoor`) count as doors and frame to their exact size.
- Authoring: `R(name, x0, y0, x1, y1, floor)`; `Door/Window(side 0 front,1 right,2 back,3 left, pos, product)`; `DoorAt`, `Fixture`, `Level`, `StairDown`.
- Room kinds come from names (`Blueprint.KindOf`): "Bath", "Powder", "Restroom", "Closet/WIC/Linen/Pantry", "Master/Bed", "Laundry/Mud", "Kitchen", "Living/Great/Family/Den",
  "Dining/Nook/Seating", "Stair", "Corridor", "Hall/Foyer/Entry/Gallery", "Mech/Utility", "Garage", "Sales/Retail/Store floor/Open office", "Office/Conference",
  "Warehouse/Bay", "Shop", "Stall", "Aisle/Barn", "loft", "Storage/Stock/Tack/Feed", "Open to below" (void), "Cook line/Prep/Dish", "Cooler", "Janitor", "Break".
- `Finish()` derives: interior walls that split where rooms change (no wall between open-plan rooms in the same unit), stairs (rise/run from wall height + joist depth,
  IRC 7-3/4" / 10" or IBC 7" / 11"; "split" = split entry), the front door, a rear patio slider, windows by room rules (egress 36x60 in bedrooms, picture window
  in large living rooms, storefront on sales floors), interior doors (bedroom → hall, master bath → master, garage → laundry with a 20-min door…) and MEP fixtures.
- MEP (`BlueprintMep.cs`): WC/LAV/TUB/SHWR, kitchen sink/DW/range/fridge, washer box, water heater, furnace or air handler (basement → mech → garage → crawl → closet),
  condenser, panel + meter, registers (1 per 150 sq ft) and returns. For commercial/industrial: restrooms, mop sink, drinking fountain, RTUs by sq ft/ton,
  diffusers, unit heaters, hood + grease interceptor + 3-compartment and hand sinks, and the sprinkler riser.
- Code checks (`BlueprintCode.cs`): IBC occupant load, IPC fixture counts, sprinklers (IBC 903.2, IRC R313 townhouses), exits, joist/rafter/roof span limits, wall height limits, minimum pitch per covering.
- Takeoff (`BlueprintTakeoff.cs`): houses use Development's takeoff corrected for joists, girders, stairs, garage type X, multi-unit and openings. Other buildings use a per-system takeoff:
  slab/post-frame/skids foundations; wood, post-frame, PEMB or CMU shell; bar joists + TPO; MEP; finishes; parking; labor.
- Validator (`BlueprintValidator.Validate/Errors`): rooms tile each floor, room sizes, openings inside walls with no overlaps and headers that fit, exits + egress,
  every room reachable (doors / open plan / stairs), stairs fit with landings top and bottom, spans, walls, roof, fixtures, plumbing counts, a non-empty takeoff, and beds/baths matching what's drawn.
- Overlay: `PlanOverlay.Shown` event → `BlueprintOverlay` adds stair treads with "UP nR @ x"", color-coded fixture markers (plumbing blue, electrical yellow,
  HVAC orange, fire red) and an occupancy/structure title.
- Plans app: Residential / Outbuilding / Commercial / Industrial toolbar + type grid. Spec-house list = residential plans ≤ 60' wide.

## Shared files touched (additive)
- `Development/HousePlan.cs`: virtual `SqFt/Summary/Finish/ComputeTakeoff`; `Category/Group/MinStage`; new enum values; `PlanOpening.IsOverhead`.
- `Development/DevVisuals.cs`: `PlanOverlay.Shown`.
- `Development/DevApps.cs`: group/type filters.
- `Tests/DevelopmentTests.cs`: plan count ≥ 6; the $/sq ft check covers Development's six ids only.

## Adding a plan
Add a factory in `BlueprintLibrary.cs` and `l.Add(...)` it, then run `BlueprintTests`. `EveryPlanPassesTheValidator` prints each error with its check name.
