# Handoff: integration (Career as one game, Handyman → Developer)

Branch `claude/integration-66875b`. Glue code: `Assets/Groundbreak/Scripts/Integration/`. Test: `Tests/CareerPlaythroughTests.cs`.

## What it does
| Area | How |
|---|---|
| **Lots** (`LotBroker`) | One owner per town lot. Repairs: customer houses (RepairJobs already skips busy addresses). Contractor jobs (Fleet `TownHook.JobLotProvider` → `LotBroker.ContractorLot`): the first empty house lot no offer/active job names; claimed `BuildSite`/`job:<id>` on accept, re-tagged `client:<name>` on completion (the client keeps the lot and what was built). When every empty lot is used, contractor jobs overflow to "County Rd 12" (x ≈ -150.., z ≈ -420). Builder houses: empty house lots via `TownLotAdapter`; land: tracts ≥ 0.7 ac. `LotBroker.Reserved(lot)` = an offer or active job names its address. |
| **Warehouse / yard / HQ** (`LotBroker.SyncYard`) | Leasing used to build the premises on the player's home driveway (Town.HQPosition defaulted to home). Now the yard claims the smallest free lot that fits the level with its street side at -Z (YardSite has no rotation): level 1 + 2 → `main-301` (commercial lot by the hardware store), level 3 HQ → `main-121` (2.8 ac tract), releasing the old lot. Sets `Town.HQPosition` and `Yard.Position`. Runs on `Company.Changed` (before `Yard.Lease` rebuilds) and after loads. Claims are saved by the town (`"town"` key). |
| **Development lots** (`Development/TownLotAdapter.cs`, rewritten) | Direct `Town` calls instead of reflection. Free town house lots → client builds (falls back to Ridge Rd lots only when the town has none left); town tracts + fallback tracts → land. Skips lots other jobs reserve. |
| **Crew ↔ Development** (`CrewHouseProgress`, `CrewTownLink`) | `Projects.Crew = CrewHouseProgress`: subs alone keep the plan's phase days (identical to `SimpleCrewProgress` with nobody assigned); each assigned worker (Worker.AssignedJobId = client job id or spec project id) adds `Crew.Speed` for the phase trade / 4. Quality = half crew workmanship, half subs. `CrewManager.HereJobResolver = CrewTownLink.HereJob`: the job/project whose lot the player stands on, so `Crew.AddTask` tags tasks with that town job; crew arrival (`ArrivalOverride`) = the lot's parking spot. Crew app can assign workers to spec houses; `Crew.JobLabel` names them. |
| **One van** (`UnifiedVan`) | `VehicleModels.VanOverride` builds every "van" with the drivable `WorkVanModel`; `YardSite.SkipParked` leaves the work van's stall empty while the WorkVan exists in town. On the first lease the van is parked in its own stall (`YardSite.TryStall`). Trucks and extra vans park in the yard's stalls. |
| **Tablet** (`IntegrationRuntime.SetupTablet`, `JobsHub`) | One "Jobs" app with tabs Service calls / Contractor / Houses / Land (each tab is the owning feature's own screen); "Contractor Jobs" and "Builder" are removed as separate apps. Jobs, Van, Hardware Store, Dealership, Company, Warehouse, Crew, Development are in the "Business" group and Career-only (`Tablet.SetAppVisible`); "Job Board" is Career-only too. |
| **Developer jobs** (`LandOffers`) | Up to 2 "Develop N acres" offers (JobKind.Development, MinStage Developer) for platable land. Accept = `Developments.BuyLand` (back on the board if it fails); done when the subdivision sells out (`Developments.Completed`). |
| **Stage** | `Company.RecomputeStage()` once per game day in Career (reputation also moves outside `Company.Complete`). |

## Shared-file edits (all small, additive)
- `UI/Tablet.cs`: `RemoveApp(name)`, `SetAppVisible(name, Func<bool>)` (filters `AppNames`).
- `Fleet/VehicleModels.cs`: `VanOverride` hook. `Fleet/Jobs/ContractorJobsApp.cs`: `Draw` made public.
- `Yard/YardSite.cs`: `SkipParked` hook, `TryStall(i, out pos, out yaw)`.
- `Crew/Crew.cs`: `JobLabel` names spec-house projects. `Crew/CrewApp.cs`: spec houses in the assign cycle.
- `Development/TownLotAdapter.cs`: rewritten with direct Town calls. `Development/DevApps.cs`: `BuilderApp` internal.
- `Town/TownBindings.cs`: contractor lots from `LotBroker.ContractorLot` (was: customer house lots, which put sheds/decks inside the customers' houses).

## Tests
`CareerPlaythroughTests.HandymanToDeveloper_OneGame`: new Career → Handyman (van at home, $2,500, a GFCI service call billed) → warehouse
(on main-301, van parked in its stall, one van) + used pickup = Contractor (fence job on an empty town lot, built with the landscaping
system, client keeps the lot) → 2 hires = Crew (crew arrives at the town job, task tagged with it, finishes the job off site) → yard + shop,
rep 66 = Builder (client house on a town lot, crew speeds it up, finished off site) → HQ (moves to main-121), 8 workers, rep 85 = Developer
(land listing bought, platted, spec houses with crews) → save → load → every key restored → neighborhood sells out. Shots `Logs/shots/career_*.png`.

## Follow-ups (not done)
- SiteHud and the main-menu toast still say "Job Board" in Career; the old Career contract board (shed rep 0, houses at the origin pad)
  still exists next to the business jobs. Decide whether to retire it in the business career (TutorialCaptureTests expect it).
- Town "for sale" signs stay on lots after they are claimed.
- Work van does not drive itself back to the yard at night; extra vans are static models.
- Contractor jobs still use a 30 m square work area around the lot centre (fits the 20 m town lots for every job type except a little
  overhang of the verification area).
