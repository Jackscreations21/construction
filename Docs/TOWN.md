# Town API (session town-repairs, `Scripts/Town/`)

The town is a fixed, deterministic layout around the build site. **The layout (lots, addresses, parking spots) is always
available**, even before the 3D town exists, so you can generate jobs and pick lots without waiting for geometry.
The 3D town is built by `Town.Ensure()` — on a new Career game (`HandymanStart`) or when a save with a town loads.
Free Build has no town. Units are metres, y = 0 is ground.

## Map
```
            Oak St (x=-90)                         Maple Ave (x=90)
 oak-301 tract |                                     | maple-300 tract (3 ac)
 (3.5 ac)      |        BUILD SITE  main-101         |
 main-301      | store  x -55..55, z -52.5..95       | main-121 tract (2.8 ac)
 (1.2 ac)      | main-201   (pad at the origin)      |
===============+==== Main St (z=-60) ================+==========  x -230..230
 oak-205,207 H | main-102..116  (108 = player home)  | maple-206,208 H
 oak-201,203 F | cedar-101..111 H, cedar-113,115 F   | maple-202,204 F
===============+==== Cedar Ln (z=-170) ==============+==========
               | cedar-102..116 F (8 empty lots)     |
```
H = customer house, F = free lot. 17 customer houses, 1 player home, hardware store, 12 free house lots (20 m x 38 m ≈ 66' x 125'),
4 free tracts. Street section: 30' pavement, 6" curb, 4' planting strip, 5' sidewalk; lot line is `Town.RowHalf` (7.5 m) from the centreline.
`Town.BuildSiteRect` (x ±55, z -52.5..95) is reserved for the existing pad / Grade / career lots — the town builds nothing there.

## API (`Town`, static)
| Member | What |
|---|---|
| `IReadOnlyList<TownLot> Lots` | every parcel |
| `IEnumerable<TownLot> Addresses` | customer houses (`LotUse.Customer`) — where repair/remodel jobs happen |
| `IEnumerable<TownLot> FreeLots` | unclaimed empty lots and tracts (`LotUse.Free`) |
| `TownLot LotAt(Vector3 p)` | lot containing a world point, or null |
| `TownLot Find(string idOrAddress)` | `"main-108"` or `"108 Main St"` |
| `bool TryGetParking(idOrAddress, out pos, out yaw)` | driveway / parking spot for a vehicle (same as `lot.Parking`, `lot.ParkingYaw`) |
| `Vector3 HQPosition { get; set; }` | company base. Default: the home driveway (`Town.Home.Parking`). The warehouse feature sets it. Saved. |
| `TownLot Home / Store / BuildSite` | 108 Main St / 201 Main St (hardware store) / 101 Main St |
| `bool Claim(lot, LotUse use, string owner)` / `Release(lot)` | take a Free lot (use `LotUse.Company` for a warehouse/yard, `Customer`/`BuildSite` for builds). Saved under key `"town"`. |
| `Vector3 NearestRoadPoint(p)`, `bool OnRoad(p)`, `Streets` | street centrelines |
| `Ensure()`, `Destroy()`, `Exists`, `Root` | the 3D town |
| events `Built`, `Destroyed`, `LotsChanged` | |

`TownLot`: `Id`, `Address`, `Street`, `Number`, `Use`, `Owner`, `Resident`, `Rect` (world XZ), `Center`, `Front` (unit vector toward the street),
`FrontMid`, `Parking`, `ParkingYaw`, `FrontDoor`, `House`, `Frontage`, `Depth`, `AreaSqFt`, `Rotation` (+Z = toward the street),
`Local(x, depth)` (lot-local → world: x to the right when facing the street from the lot, depth back from the street lot line), `Contains(p)`.

Parking: customer houses and the home have a 12' concrete driveway on the lot's right side (`Parking` is 6.5 m in from the lot line, nose to the
house). The store's spot is in its parking lot. Free lots and the build site park at the curb, heading with traffic.

Jobs: set `Job.Address = lot.Address` and `Job.WorldPos = lot.Parking` (or the work position); `Town.Find(job.Address)` gets the lot back.

## Houses (`TownHouse`, `HouseSpot`)
`lot.House` (customer lots + home, only while the 3D town exists). One-storey ranch, slab floor at +0.2 m, 8' ceilings, gable roof, enterable:
living room + kitchen, bedroom, bath; rear deck, back fence. `house.Anchor(HouseSpot)` gives the world pose of 17 repair spots (kitchen sink
cabinet, outlets, breaker panel, drywall areas, doors, deck board, fence picket, roof, gutter, sidewalk square...); pose conventions are on the
`HouseSpot` enum. `Fixture(spot)` / `SetFixtureVisible(spot, bool)` swap the healthy part for a defect model.

## Work van and cargo (`Scripts/Town/Van/`)
- `WorkVan.I` — the player's cargo van (spawned at `Town.Home.Parking` when the company owns a `"van"`). `WorkVan.I.Driving`,
  `WorkVan.I.TeleportTo(pos, yaw)`, `WorkVan.FastTravel(TownLot)` (moves van + player, costs game time).
- `VanCargo` (static) — the Handyman-stage stockpile: `Count(id)`, `CanAdd(id, n, out why)`, `Add(id, n)`, `TryTake(id, n)`, `Entries`,
  `UsedCuFt/CapacityCuFt`, `UsedLb/PayloadLb`, `InReach(worldPos)` (van parked within 40 m), `RegisterItem(id, lb, cuFt)`, event `Changed`.
  Items are `Catalog` definitions; anything registered with `RegisterItem` is sold at the hardware store (tablet app "Hardware Store",
  usable when the van is parked at the store).
- `TownLadder` — the 24' extension ladder from the roof rack; `TownLadder.Place(foot, awayFromWall, topHeight)`.

## New career
`MainMenu.NewGameStarted` (new additive event) → `HandymanStart.Begin()`: builds the town, wallet $2,500, `Company.AddVehicle("van", "Work Van")`,
player in the home driveway.

## Tests
The town is not built in the shared test world unless a test calls `Town.Ensure()` / `HandymanStart.Begin()`; call `Town.Destroy()` and
`Company.ResetState()` in teardown. Town geometry stays outside `Town.BuildSiteRect`, but covers most of x ±230, z -230..150 beyond it.
