# Handoff: map-followup (map + blueprints + job catalog tied together)

Branch `claude/eloquent-tharp-51b6ca`. Code: `Assets/Groundbreak/Scripts/MapFollowup/`. Tests: `Tests/MapFollowupTests.cs` (shots `Logs/shots/followup_*.png`).

## What it does
| Area | How |
|---|---|
| **Vacant lots** (`VacantLots`) | Every free town lot (core + map, 65 lots) gets one merged mesh, no colliders: for-sale sign (core lots; map lots keep MapWorld's), iron pins with orange caps + 4' lath with pink flagging at the corners, dirt/dry patches, tall-grass and weed clumps (denser along the lot lines), brush, a few trees at the back/side, sometimes an old post-and-barbed-wire fence (posts 8' o.c., leaning, missing strands), dirt piles with weeds and a 12'x30' gravel entrance. Hidden on claim (`Town.LotsChanged`), back on release. `VacantLotStreamer` hides it beyond `MapStreamer.ShowDistance` (follows `Frozen`). |
| **Build-site margins** | (VacantLots has its own scene root, not under `Town.Root`, so TownTests' "nothing of the town inside the build site" still holds.) The player's pad (`Town.BuildSiteRect`) keeps its middle clear: tree rows and tall grass on the back and sides, silt fence along Main St with a 20'x50' gravel construction entrance, a 4'x8' job sign. |
| **For-sale bug** | `TownBuilder.ForSaleSigns = false`: the core town's sign was baked into the merged street mesh and never went away; VacantLots draws hideable per-lot signs instead. |
| **Plans on buildings** (`MapPlans`) | After `MapLayout.AfterLayout`, every map building whose type matches a plan family gets the plan closest in sq ft (`MapPlans.Candidates`): houses, duplex, townhomes (row4/row6 by units), storefronts/grocery (retail), office/bank, diner/fast food, gas station, strip mall, warehouses (by size), metal shops (workshop), auto repair (flex), contractor yards, lumber yard, barns, machine sheds. Core customer houses: ranch-1500/1600. **~240 buildings have a plan**; civic buildings, motel, self storage, farms' silos have none. `MapLayout.PlanByType` now points at real ids. |
| **More service-call houses** | Every other one-storey `ranch2` house on a residential street with a standard 20x38 m lot becomes an enterable ranch (`MapParcel.Ranch`, HouseBuilder interior): 9 more, in West Side and North End (cap `MaxNewRanches` = 24). |
| **Jobs across the map** (`MapJobs`) | `JobCatalog.AddressPicker`: service calls/scopes pick among all enterable houses weighted by `exp(-distance from HQ / scale)` (380 m Handyman … 1,000 m Builder+). `TownHook.JobLotProvider = MapJobs.ContractorLot`: contractor jobs on any open residential lot in town, same weighting (falls back to LotBroker's overflow). `DefsContract.BuildLots = MapJobs.BuildLotsFor`: builds go on a free lot of the plan's category (diner/office/gas station → commercial, warehouses → industrial or the strip), houses never on commercial lots and vice versa. |
| **Town Plans app** (`BuildingPlansApp`) | Tablet app listing the nearest buildings with a plan (the one you stand on first): plan summary, IMGUI floor plan, "Lay the blueprint over the building" (`ShowOver`: PlanOverlay with its front wall on the building's front wall, 0.4 m above the roof). HUD hint `BuildingPlanHint` names the plan when you walk onto such a lot. |
| **Small fixes** | Crew arrival on an empty/claimed lot = inside the lot (2.5 m past the lot line), never the curb parking spot in the street. HUD line/toast say "Jobs" (stage, active jobs, rep). The old Career "Job Board" is **not** retired: `TutorialCaptureTests.F_CloseupAndSite` photographs it for the `biz_04_career` tutorial picture and the tutorials session is reworking those; retire it there (one line in `IntegrationRuntime.SetupTablet`: `SetAppVisible("Job Board", () => GameSettings.IsCareer && Career.Active != null)`). |

## Shared-file edits (all small, additive)
- `Map/MapLayout.cs`: `public static event Action AfterLayout` raised at the end of the layout build.
- `Town/TownBuilder.cs`: `public static bool ForSaleSigns = true` guard on the baked free-lot sign.
- `JobCatalog/JobCatalog.cs`: `AddressPicker` hook in `FreeAddressFor`.
- `JobCatalog/DefsContract.cs`: `BuildLots` hook in `CreateBuild`.
- `Integration/CrewHouseLink.cs`: `CrewTownLink.ArrivalFor` keeps the crew off the street.
- `Career/MainMenu.cs`: HUD row + new-career toast text.
- `Tests/FullMapTests.cs`: the "missing plans are tolerated" check uses any building without a plan (every storefront has one now).
- `Tests/CareerPlaythroughTests.cs`: crew arrival within 9 m of the lot's parking **and off the road** (was < 4 m of the curb spot, which is in the street).

## Tests
`MapFollowupTests` (4): vacant lots dressed/hidden on claim + 6 shots; plans on buildings + diner/townhome blueprint shots; catalog jobs reach ≥3 districts / map lots / commercial builds + outer-map service call shot; crew arrival off the road.

## Status
Full PlayMode suite (main 01bd5aa merged, run through the shared unity-run.sh lock): 125 total, 124 passed, 0 failed, 1 skipped (UICapture, by design).
Dressing size: 65 lots + build-site margins = ~1.03 M vertices (grass blades and trees); meshes are destroyed with the town.

## Follow-ups
- Commercial/industrial buildings have plans but no interiors, so service calls there aren't possible yet (catalog places need a `TownHouse`). A commercial interior (diner kitchen, office RTU on the roof) would open the catalog's commercial work.
- More enterable houses: only `ranch2` on standard lots convert; two-storeys/capes would need HouseBuilder variants.
- The Town Plans app's IMGUI is not in the batchmode shots (run UICaptureTests windowed to see `ui_tablet_Town Plans`).
- Dressing could be thinned (fewer blades per clump) if the frame budget gets tight.
- Plans are larger than some map models (e.g. the diner model vs diner-3000), so the overlay overhangs the building.
