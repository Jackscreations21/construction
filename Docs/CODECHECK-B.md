# Code check, half B: business and world side (2026-11-27)

Scope: every `.cs` file under `Assets/Groundbreak/Scripts/` in Business, Town, Repairs, Fleet, Yard, Crew, Development,
Integration, JobCatalog, Blueprints, BusinessTutorials, Flips, Subs, Seasons, Equipment, PlanDesigner, Photo, Balance,
then Map and MapFollowup after merging main (map fixes commit 10d163d: FarRing, MapBodies, MapGapFill, BuildSiteFill,
Weeds, MapFixTests are included). 147 files, each read in full. Regression tests: `Assets/Groundbreak/Tests/CodeCheckBTests.cs`.

## Files reviewed

### Business
- [x] Business/Company.cs
### Town
- [x] Town/HandymanStart.cs
- [x] Town/HouseBuilder.cs
- [x] Town/Town.cs
- [x] Town/TownBindings.cs
- [x] Town/TownBuilder.cs
- [x] Town/TownDoor.cs
- [x] Town/TownHouse.cs
- [x] Town/Van/TownLadder.cs
- [x] Town/Van/VanApps.cs
- [x] Town/Van/VanCargo.cs
- [x] Town/Van/VanSystems.cs
- [x] Town/Van/WorkVan.cs
- [x] Town/Van/WorkVanModel.cs
### Repairs
- [x] Repairs/RepairCatalog.cs
- [x] Repairs/RepairDefect.cs
- [x] Repairs/RepairDefectsExterior.cs
- [x] Repairs/RepairDefectsInterior.cs
- [x] Repairs/RepairJobs.cs
- [x] Repairs/RepairUI.cs
### Fleet
- [x] Fleet/Cargo.cs
- [x] Fleet/CompanyApp.cs
- [x] Fleet/DealershipApp.cs
- [x] Fleet/Finance.cs
- [x] Fleet/Fleet.cs
- [x] Fleet/FleetCatalog.cs
- [x] Fleet/Jobs/ContractorJobSite.cs
- [x] Fleet/Jobs/ContractorJobs.cs
- [x] Fleet/Jobs/ContractorJobsApp.cs
- [x] Fleet/TownHook.cs
- [x] Fleet/VehicleModels.cs
### Yard
- [x] Yard/RackStock.cs
- [x] Yard/Site/SitePart.cs
- [x] Yard/WarehouseApp.cs
- [x] Yard/Yard.cs
- [x] Yard/YardFleetRuntime.cs
- [x] Yard/YardSite.cs
### Crew
- [x] Crew/Crew.cs
- [x] Crew/CrewApp.cs
- [x] Crew/CrewManager.cs
- [x] Crew/CrewNpc.cs
- [x] Crew/CrewTask.cs
- [x] Crew/CrewTrades.cs
- [x] Crew/CrewWork.cs
### Development
- [x] Development/Builder.cs
- [x] Development/DevApps.cs
- [x] Development/DevLots.cs
- [x] Development/DevSave.cs
- [x] Development/DevVisuals.cs
- [x] Development/Developments.cs
- [x] Development/HousePlan.cs
- [x] Development/HouseProject.cs
- [x] Development/TownLotAdapter.cs
### Integration
- [x] Integration/CrewHouseLink.cs
- [x] Integration/IntegrationRuntime.cs
- [x] Integration/LandOffers.cs
- [x] Integration/LotBroker.cs
- [x] Integration/UnifiedVan.cs
### JobCatalog
- [x] JobCatalog/CatalogDefect.cs
- [x] JobCatalog/CatalogProps.cs
- [x] JobCatalog/DefsContract.cs
- [x] JobCatalog/DefsExterior.cs
- [x] JobCatalog/DefsInterior.cs
- [x] JobCatalog/DefsMep.cs
- [x] JobCatalog/JobCatalog.cs
- [x] JobCatalog/JobDef.cs
- [x] JobCatalog/JobParts.cs
- [x] JobCatalog/JobPlaces.cs
- [x] JobCatalog/JobValidation.cs
- [x] JobCatalog/ToiletModel.cs
### Blueprints
- [x] Blueprints/Blueprint.cs
- [x] Blueprints/BlueprintCode.cs
- [x] Blueprints/BlueprintLibrary.cs
- [x] Blueprints/BlueprintMep.cs
- [x] Blueprints/BlueprintOverlay.cs
- [x] Blueprints/BlueprintProducts.cs
- [x] Blueprints/BlueprintTakeoff.cs
- [x] Blueprints/BlueprintValidator.cs
- [x] Blueprints/Blueprints.cs
- [x] Blueprints/PlanLabelLayout.cs
### BusinessTutorials
- [x] BusinessTutorials/BusinessTutorialContent.cs
- [x] BusinessTutorials/BusinessTutorials.cs
- [x] BusinessTutorials/TutorialCapture.cs
### Flips
- [x] Flips/FlipApp.cs
- [x] Flips/FlipDefs.cs
- [x] Flips/FlipInterior.cs
- [x] Flips/FlipVisuals.cs
- [x] Flips/Flips.cs
### Subs
- [x] Subs/SubCatalog.cs
- [x] Subs/SubCrews.cs
- [x] Subs/SubScopes.cs
- [x] Subs/SubTasks.cs
- [x] Subs/SubVisuals.cs
- [x] Subs/Subs.cs
- [x] Subs/SubsApp.cs
### Seasons
- [x] Seasons/Climate.cs
- [x] Seasons/SeasonDirector.cs
- [x] Seasons/SeasonLook.cs
- [x] Seasons/SeasonRules.cs
- [x] Seasons/SeasonWeather.cs
- [x] Seasons/StormDamage.cs
- [x] Seasons/StormEvents.cs
- [x] Seasons/WeatherApp.cs
- [x] Seasons/WeatherFx.cs
### Equipment
- [x] Equipment/BoomLift.cs
- [x] Equipment/Earth.cs
- [x] Equipment/EquipModels.cs
- [x] Equipment/Equipment.cs
- [x] Equipment/EquipmentLoad.cs
- [x] Equipment/Machine.cs
- [x] Equipment/MiniExcavator.cs
- [x] Equipment/SkidSteer.cs
- [x] Equipment/TruckCrane.cs
### PlanDesigner
- [x] PlanDesigner/DesignChecks.cs
- [x] PlanDesigner/DesignEditor.cs
- [x] PlanDesigner/PlanDesign.cs
- [x] PlanDesigner/PlanDesigner.cs
- [x] PlanDesigner/PlanDesignerApp.cs
- [x] PlanDesigner/PlanPicture.cs
### Photo
- [x] Photo/JobPhotos.cs
- [x] Photo/OfficeFrames.cs
- [x] Photo/PhotoMode.cs
- [x] Photo/PhotoRender.cs
- [x] Photo/PhotoStore.cs
- [x] Photo/PortfolioApp.cs
### Balance
- [x] Balance/CrewLaborCredit.cs
### Map (after `git merge main`, map fixes included)
- [x] Map/FarRing.cs
- [x] Map/MapBodies.cs
- [x] Map/MapGapFill.cs
- [x] Map/MapLayout.cs
- [x] Map/MapMaterials.cs
- [x] Map/MapModelApi.cs
- [x] Map/MapRoads.cs
- [x] Map/MapTextures.cs
- [x] Map/MapWorld.cs
- [x] Map/Models/CommercialModels.cs
- [x] Map/Models/MapText.cs
- [x] Map/Models/MapTrees.cs
- [x] Map/Models/PropModels.cs
- [x] Map/Models/ResidentialModels.cs
- [x] Map/Models/RuralIndustrialModels.cs
### MapFollowup
- [x] MapFollowup/BuildSiteFill.cs
- [x] MapFollowup/BuildingPlansApp.cs
- [x] MapFollowup/MapJobs.cs
- [x] MapFollowup/MapPlans.cs
- [x] MapFollowup/VacantLots.cs
- [x] MapFollowup/Weeds.cs

## Defects fixed (most serious first)

1. **Development/DevSave.cs**: Unity's JSON saver writes empty (null) text as "" and the code tests for null. After any load: warranty
   callbacks were never issued, every client house showed a "Warranty call" button, spec houses vanished from the crew "Assign"
   list, and crews assigned to a spec house never showed up there. Nulls are restored on load. (test)
2. **Flips/Flips.cs**: a flip item whose step the weather blocks (outdoor paint/caulk in the cold, roof work in rain) was "finished",
   billed its materials, reverted, and billed again up to 12 times per worker per night. Now it waits and is billed once when done.
3. **Seasons/StormEvents.cs**: storm damage was never cleared after the storm job was done (Complete removes the job before Tick looks),
   so the tarp stayed and a new storm job appeared at the house just fixed. Cleared on JobCompleted; no storm calls at the
   player's own flip (offer churn). (test)
4. **Subs/Subs.cs**: the other companies' stale quotes could book a second sub for the same trade on the same site (both invoice). (test)
5. **Development/HouseProject.cs**: workers on a spec house stayed assigned (on payroll, doing nothing) forever after it was finished. (test)
6. **Yard/YardSite.cs**: shop-equipment BoxColliders were added to the building shell on every contents refresh (every truck trip,
   purchase, van trip) and never removed. (test + screenshot `codecheck_b_yard.png`)
7. **Mesh leaks** (new `Landscaping/OwnedMesh.cs`, one line each in `LandModel.Make` and `SitePart.Finish`, both shared helpers):
   meshes built per object were never freed when their object was destroyed: the crew's held tool (every work unit), crew NPCs,
   DevVisuals and plan overlays, flip dressing, storm damage, repair defects, the yard contents/shell. Also `Photo/OfficeFrames.cs`. (test)
8. **Equipment/Equipment.cs**: loading a save while seated in a machine put the player beside the old machine instead of the saved
   spot; a fleet-owned skid steer/crane truck on site could vanish on load (ownership checked before the company save loaded).
9. **Town/Van/VanSystems.cs**: an autosave made while driving loaded the player inside the van body. The save now records driving
   and the player is stepped out beside the van.
10. **Equipment/Machine.cs, Town/Van/WorkVan.cs, VanSystems.cs**: the E press that climbs in could also climb straight back out in the
    same frame (Update order is undefined).
11. **JobCatalog/CatalogDefect.cs**: closing steps (power-on, water-on, plate-on...) that could never be done because the state was never
    opened cost 0.08 quality each, lowering pay on correct repairs (e.g. a switch whose cause was the bulb lost 16%).
12. **JobCatalog/JobCatalog.cs + Repairs/RepairJobs.cs** (new `HasPendingCallback`): new service calls were offered at a house with a
    pending no-charge callback, putting two defect sets on one house.
13. **Fleet/Finance.cs**: jobs lost in a bankruptcy left their workers assigned to them; "Fuel tank" and "Equipment rental" were filed
    under Fuel/Warehouse in the P&L. (test)
14. **Development/Developments.cs**: spec-house property tax counted the land share twice. (test)
15. **Crew/Crew.cs**: off-site progress records of jobs that ended another way were kept and saved forever.
16. **Subs/SubCrews.cs**: a warranty callback active when a save was loaded redid the original scope (usually nothing) and closed as
    "fixed" with the defects still there.
17. **Subs/SubTasks.cs**: the concrete sub poured in rain/cold/on frozen ground and got free ready-mix when the wallet was short.
18. **Seasons/SeasonRules.cs**: a pour on frozen ground asked for thaw blankets, but only digging could lay them.
19. **JobCatalog/DefsContract.cs**: a catalog build's schedule started when the offer was posted, not at signing (late penalty).
20. **Flips/Flips.cs**: a house with an open customer job could be bought as a flip. **Flips/FlipApp.cs**: an empty market was
    re-scanned (and fired change events) on every GUI event; now every 2 s, Layout pass only.
21. **Photo/JobPhotos.cs, PhotoMode.cs**: a failed photo save leaked the 1280x720 texture and the job "before" photo retried every
    0.4 s forever; Photo mode threw out of Update with no message.
22. **PlanDesigner/DesignEditor.cs**: removing the top floor also broke the stair coming up from the floor below; carving a box out of a
    closed room gave several leftovers the same name ("Room is drawn as two separate parts"). (tests)
23. **IMGUI**: "◂ Plans" returned inside BeginHorizontal; clicks that add/remove controls mid-event (plan designer edits, tool switch,
    save, floors; dealership buy; warehouse lease; crew hire/tasks; dev sign/buy land; equipment rent; building-plans list/Hide)
    now end the GUI event (`GUIUtility.ExitGUI`) instead of throwing "Getting control N's position" errors.
    **MapFollowup/BuildingPlansApp.cs**: the floor index was kept when switching to a plan with fewer floors (empty blueprint).
24. **Map/MapWorld.cs**: once every tract was bought, the LAND FOR SALE signs came back on the player's own developments.
25. **Map/Models/RuralIndustrialModels.cs**: lumber-shed post loop could never end for a shed under ~0.5 m long (hang); the last rack
    bundle stuck 2 m out of the shed; divide by zero in gable roofs at zero width.
26. **JobCatalog/CatalogProps.cs** (visual): props keyed on step/cause ids that don't exist (clogged toilet water, hose-bib stem and
    wall, faucet cartridge, deck-ledger flashing drawn before the work).

Shared/core files touched (small, additive): `Landscaping/LandModel.cs` (one line), new `Landscaping/OwnedMesh.cs`,
`Yard/Site/SitePart.cs` (one line). `Company.cs` was not changed.

## Needs a decision (not changed)

- ~~**Forklift does nothing**~~ Fixed (`Scripts/Forklift/ForkliftRules.cs`): without it the crew hand-handles warehouse loads over
  2,000 lb (1.5 labor-h/ton at $30/h, in and out), with it no labor, 3% lift pricing on warehouse stock and warehouse runs at half
  time. The saw station still has no effect.
- **End lease / sell trucks keeps the stage** (Yard.EndLease, Company.RecomputeStage never goes down): reach Contractor, end the lease,
  and stay Contractor with no rent. Block "End lease" in Career while Stage ≥ Contractor, or drop the stage like bankruptcy does.
- ~~**Selling a vehicle an active job needs**~~ Fixed (`Scripts/Forklift/JobVehicleLock.cs`): `Fleet.Sell` refuses the last vehicle of a
  type an active job needs and names the job; a job found without its vehicle (old save) is on hold (crew and sign-off).
- **Job app "Order what's missing"** (ContractorJobsApp): no supplier delivery fee (the store charges $79) and filed as "Other expenses".
- **Warehouse capacity ignores loads on the road** (Fleet.CheckRun): two trucks sent at once can overfill storage.
- **Downgrading the yard keeps equipment the smaller level can't have** (Yard.Lease): lift discount at a level-1 bay.
- **Lease change rebuilds the premises twice** (Yard.Lease calls YardSite.Rebuild, then YardFleetRuntime rebuilds again).
- **Loans forgiven at sale**: Developments.Close and Flips sale pay the loan only up to the cash on hand and zero the rest.
- **Spec house started before site work** never gets the site-work share in its cost (profit overstated). Require site work first?
- **CrewLaborCredit vs Projects.DayStep order**: both on Company.DayStarted, order undefined; about one day of credit per phase is lost.
- **Equipment rental pick-up fee / dump-truck haul-off fee** are waived when the wallet is short (blocking a return would keep the
  daily rent running, so this is a design call). Weekly rental rate is shown but never billed.
- **Off-site crew finishes service calls** without diagnosing anything (Crew.CanSimulate allows Repair). Intended?
- **Sub invoices full price when nothing is left to do** (an own crew finished the scope first); own crew and subs can work the same
  trade area at once.
- **Season look**: tinted materials copied while winter/snow is applied keep that look as their "original" (SeasonLook.Scan).
- **Every career has the same weather** (Climate.Seed is always 1). Randomize per new career (keep 1 in tests)?
- **Curing blankets/heater on a slab are lost on save/load** (SeasonRules saves only thaw times).
- **IMGUI pattern elsewhere**: other apps still change controls inside a click (CompanyApp borrow, ContractorJobsApp scope/accept,
  PhotoMode depth-of-field toggle, PortfolioApp tabs, IntegrationRuntime buy land, RepairUI accept). Harmless one-event errors;
  same `GUIUtility.ExitGUI()` fix if wanted.
- **MapJobs**: commercial/industrial build defs (warehouse, retail, diner, office, gas station) never find a lot: every industrial/strip
  map lot is over the 30,000 sq ft tract size and is skipped. Let those builds use tracts of their category?
- **Fallback Ridge Rd lots** overlap map woods (only used when no town lot is free).
- **Map model sizes**: rural/industrial builders don't clamp W/D (current specs are fine); `Mathf.Abs(seed) % n` indexes.
- **SkidSteer.OnDestroy** sets a child's parent while being destroyed without the try/catch TruckCrane uses.
- **Plan designer drafts** with errors may be unregistered on load before projects load (PlanDesigner extension loads first).
- **Catalog build accept** ignores a failed lot claim / missing plan (no path found that triggers it).

## Update 2026-11-28: "Needs a decision" resolved
Everything in the list above is fixed except:
- Off-site crews finishing service calls without diagnosing: left as is (intended simplification).
- Equipment pick-up / haul-off fee waived when the wallet is short: left as is (blocking a return would keep rent running).
Choices: saw station = own-crew carpentry 10% faster and 4% less material on crew-built wood jobs; weather seed rolled per new career (1 in tests);
"End lease" and downgrades blocked while the stage needs the premises; unpaid loan at a sale becomes a "Loan shortfall" note; "Order what's missing"
charges the $79 delivery fee under Materials; warehouse space counts loads on the road; downgrade sells equipment the smaller level can't hold at 40%;
weekly rental rate caps each 7-day block; subs invoice pro rata; commercial/industrial builds claim strip/industrial tracts (`MapFollowup/CommercialSites.cs`);
HQ move-in $100,000 for pacing (see `Docs/BALANCE.md`). Tests: `BusinessRulesTests`, `MapCommercialTests`.
