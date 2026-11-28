# Full map (session full-map, `Scripts/Map/`)

The town (`Docs/TOWN.md`) is now the middle of a whole small American town — "Millbrook" — and its countryside.
Like the town, the **layout is deterministic and always available** (lots, addresses, parking) and the **3D map is built
with the town** (`Town.Ensure()` → `Town.Built` → `MapWorld.Build()`), removed by `Town.Destroy()`. Units are metres, y = 0 is grade.

## Layout (x east, z north; the build pad is at the origin)
```
 z=980 ─ woods / hills ───────────────────────────────────────────────────────────────── Mill Rd (x=420) runs z -700..980
 z=600 ═══ County Rd 9 (x -620..1180) ═══  Whitlock Farm · Brandt Farm · Lindqvist Farm │ dev tracts A/B/C (x 430-1040, z 610-810)
         fields + hedgerows                                                            │  pasture, creek between A and B
 z=470   Oak St (rural north of 470)                                                   │
 z=370 ═ Elm St (x -330..420): duplexes · new-build lots · townhome rows · water tower │  devland tracts on Mill Rd
 z=250 ═ Birch St (x -560..420): LINCOLN ELEMENTARY · park + pond · 10 enterable ranches (Oak..Maple) · two-storeys
 z=150 ───────────── Old Town (TOWN.md) north edge, Oak St / Maple Ave continue north ──────────────────────────
 z=-60 ═ Main St: Prairie Rd ─ west homes ─ DOWNTOWN x -505..-255 (44' street, storefronts, town hall, bank,
         church, diner, library, post office, fire dept) ─ hardware store ─ Old Town ─ East Main (farm & feed, church) ─ Mill Rd
 z=-170 ═ Cedar Ln: West Side older homes (foursquares, capes) │ Old Town │ industrial park x 232..420 (lumber yard,
         Valley Ready Mix concrete plant, warehouses, welding shop, excavating yard, self storage)
 z=-330 ═ Route 9 highway (x -760..1180): commercial strip x -330..240 (auto repair, Burger Barn, Oak Plaza strip mall,
         Fill-N-Go gas, Harvest Foods grocery, credit union, Taco Rancho, Starlite Motel, Quik Stop, Tractor Supply,
         medical clinic, Route 9 Diner, Speedy Lube) · industrial frontage · farms
 z=-700 ─ fields / woods (Oak St continues south as a rural road)
 x=-620 Prairie Rd (rural, z -700..600); Church St x=-330, Court St x=-450 (downtown cross streets)
```
Districts (`MapLayout.Districts`, `MapLayout.DistrictAt(p)`): `downtown`, `strip`, `industrial`, `school`, `westside`,
`north`, `eastside`, `oldtown`, `devland`, `farms` (each has `Name`, `Area`, a ground-level `View` + `ViewYaw`).

Road classes (`MapRoad.Class`, cross-sections in real feet): Residential 30' + 6" curbs + 4' strip + 5' walk (same as the core),
Downtown 44' (two 11' lanes + 8' parking lanes) + 12' walks to the building faces, Industrial 36' with curbs, Highway 2×12' + 8'
shoulders, Rural 2×11' + gravel shoulders. Junctions (X/T/L) get pavement squares, corner pads, stop signs, street-name blades;
width changes get a 14 m taper. A creek runs north–south east of Mill Rd with culverts at the roads.

## API (all static)
| Member | What |
|---|---|
| `Town.Lots / Addresses / FreeLots / Find / LotAt / TryGetParking` | now include every map lot (`TownLot.External == true`) |
| `TownLot.District`, `.Category` | district id; `"residential"`, `"commercial"`, `"civic"`, `"industrial"`, `"farm"`, `"land"` |
| `TownLot.TypeId` | building model type (`"ranch"` = enterable TownHouse; `"twostory"`, `"foursquare"`, `"cape"`, `"ranch2"`, `"duplex"`, `"townhomes"`, `"farmhouse"`, `"storefront"`, `"diner"`, `"bank"`, `"office"`, `"townhall"`, `"church"`, `"postoffice"`, `"library"`, `"school"`, `"firestation"`, `"gasstation"`, `"grocery"`, `"stripmall"`, `"fastfood"`, `"motel"`, `"autorepair"`, `"warehouse"`, `"metalshop"`, `"lumberyard"`, `"concreteplant"`, `"selfstorage"`, `"contractoryard"`, `"watertower"`), null on empty land |
| `TownLot.PlanId`, `MapLayout.PlanFor(lot)` | optional `HousePlan` id (Docs/PLANS.md) — looked up at runtime, **may be null** (e.g. `"duplex-2x1100"` until someone registers it). Defaults per type: `MapLayout.PlanByType` |
| `TownLot.ServiceCalls` | true only for lots with an enterable `TownHouse` (core houses + the 10 map ranches on Birch St). The repair generator skips the rest |
| `Town.CoreLots`, `Town.AllStreets` | core-only lots (TownBuilder); core + map streets (navigation, `NearestRoadPoint`, `OnRoad`) |
| `MapLayout.Roads / Parcels / Areas / Creek / Bounds` | the layout data (`MapParcel`: lot, `MapBuildingSpec`, setback, site kind) |
| `MapWorld.Exists / Root / Built / Last` | the 3D map and its build stats (cells, renderers, triangles, colliders, trees, seconds) |
| `MapWorld.AutoBuild` | set false to build only the core town |
| `MapModels.Register(type, (MapBuild, MapBuildingSpec) => ...)` | add / replace a building model (frame: origin at the front-centre at grade, +Z to the street) |
| `MapWorld.Place(spec, pos, rot)` | draw a registered model into the map during a build |
| `MapStreamer.ShowDistance / Frozen`, `MapWorld.ShowAll(bool)` | distance hiding of map cells (colliders always stay) |

Jobs: every map building except the water tower is a `LotUse.Customer` address with a `TypeId` — send any job there with
`Job.Address = lot.Address`, `Job.WorldPos = lot.Parking`. Filter by `Category`/`TypeId`/`District` (Fleet contractor jobs only use
`Category == "residential"`). Free lots: house lots in every residential district, downtown/strip/industrial commercial lots, and
`devland` tracts (> 30,000 sq ft → the Development app's land for sale). Their meadow + for-sale dressing hides as soon as the lot is claimed.

Commercial / industrial client builds (map-commercial, `MapFollowup/CommercialSites.cs`): every strip and industrial map lot is a tract
(> 30,000 sq ft), so a commercial build claims a **commercial** tract and an industrial build an **industrial** tract (strip tracts as a fallback),
never development land. The building goes on a pad site inside it: facing the road, 70' back from the street for stores/restaurants/offices/gas
stations (landscape strip + 60' parking bay + walk), 40' for warehouses; 15' / 10' off the left side line so the right side stays clear (60' parking
bay / 30' truck drive), 20' rear setback. The pad is a `DevLot` with id `tractId@c60x50` that `CommercialSites.Provider` (wrapping
`DevLots.Provider`) finds, claims and releases through the tract, so the tract's dressing hides on claim and comes back on release, and projects
find their pad after a load. `CommercialSites.TownLotOf(devLot)` gives the town lot of any DevLot.

Ridge Rd: the overflow house lots of Development's `FallbackLotProvider` (x 430-567, z 373-467, only used once every town lot is taken) now front
a real residential street "Ridge Rd" off Mill Rd; woods / hedgerows / crop patches there are cut back (`MapLayout.Carve`) to mown ground, and the
Mill Rd devland tract `mill-7003` ends at z 360 so it no longer overlaps them.

Parking: houses park on the driveway (right side), businesses in their lot, downtown lots share a loading-zone stall every 25 m in the
parking lane, farms in the farmyard; parked scenery cars never sit on a lot's `Parking` spot.

## Building it / performance
`MapWorld.Build()` merges everything per 100 m cell (Surfaces with a MeshCollider, Markings, Buildings, Vegetation) plus one collider
object per cell (boxes for buildings/props, capsules for trees); `MapStreamer` hides cells more than 460 m from the camera (the haze is
opaque there). Models: `Scripts/Map/Models/` (Residential, Commercial, RuralIndustrial, Props, MapTrees, MapText). Materials:
`MapMaterials` (+ procedural `MapTextures`: brick wall, R-panel steel, stucco, crop fields, water).

Measured (FullMapTests, `MapWorld.Last`): 318 cells, ~1,020 renderers, ~3.2 M triangles in total (crop rows and trees dominate;
only cells within `ShowDistance` render), ~6,600 colliders, ~260 buildings, ~5,900 trees, ~2-3 s to build.
Hills (`AreaKind.Hill`) only rise beyond 30 m outside `MapLayout.Bounds`. Power poles: model +Z runs along the road, so the crossarm
(model X) crosses the line; wires attach at `PropModels.ConductorX/ConductorY/ConductorZ`, `NeutralPos`, `CommPos`.
