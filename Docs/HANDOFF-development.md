# Development feature (Builder + Developer stages) — `Scripts/Development/`

Branch `claude/development-3892f9`. No shared files edited (Company, Career, Inspections untouched; uses `internal`
`Inspections.Save/Load`, same assembly). Tests: `Tests/DevelopmentTests.cs` (7). Screenshots `Logs/shots/dev_*.png`.

## Files
- `HousePlan.cs` — `HousePlan` (rooms → derived wall lines; openings = front door + rule-based windows/interior doors),
  `Takeoff` (studs @16", plates, OSB, drywall 4x12, concrete by foundation type, joists, trusses, shingles, MEP allowances,
  labor per phase), `HousePlans` library: starter-900, cottage-1200, ranch-1600, craftsman-1920, twostory-2400, estate-3500.
  `BuildPhase` Foundation/Framing/RoughIn/Drywall/Finish ↔ inspections Footing(+UnderSlab)/Framing/RoughMEP/Insulation/Final.
- `DevLots.cs` — `DevLot` (Town conventions: Front, Local(x, depth)), `PlanPlacement`, `ILotProvider`, `DevLots.Provider`,
  `FallbackLotProvider` (12 lots on "Ridge Rd" z=420, x≥430; tracts A/B/C along z=600).
- `TownLotAdapter.cs` — reflection adapter onto `Town` (FreeLots / Find / Claim(lot, LotUse.BuildSite) / Release). Auto-selected
  when the Town type exists; Town tracts + fallback tracts are land for sale. Tests force the fallback provider.
- `HouseProject.cs` — `HouseProject` state, `Projects` (OnSite project = player-built: progress measured from real Pieces /
  ConcreteElement yards / ElBox / Drywall.Surfaces / doors+windows, 100% only when the inspection passes), `ICrewProgress` +
  `SimpleCrewProgress` (plan phase days, +10%/assigned worker). Off-site houses step once per game day (`Company.DayStarted`).
  **Crew session:** set `Projects.Crew = yourImpl` (deterministic per project/day); `Worker.AssignedJobId` may be a project Id.
- `Builder.cs` — Career contracts house/twostory/custom → `JobKind.House` offers (MinStage Builder) on free lots. Deposit 10%,
  draws 10/20/20/15% on Footing/Framing/RoughMEP/Insulation passes, final 25% + change orders via `Company.Complete`.
  Change orders after framing; warranty callback 30 days after CO (14-day window, ignored = rep hit). Shed/garage stay on
  Career's job board / outbuilding jobs.
- `Developments.cs` — buy land ($32–44k/ac + 2% closing), plat (50' ROW street, 60/70/80' lots, ≤130' deep, setbacks 25/7.5/20),
  site work ($630/lf street+utilities + $3.5k/lot laterals, 30 days), permits + impact fees per house, 80% LTC construction
  loan @8.5% + property tax/builder's risk via `Company.RegisterDailyCost`, market value (size × $185/sf × finish ×
  neighborhood completeness × 540-day ±7% cycle + lot), listing / days-to-sell / closing (5.5% + 1.5%), profit per house,
  `Completed` event when every lot is sold.
- `DevVisuals.cs` — `PlanOverlay` (wall lines, door/window ROs, room labels, stakes) and `DevVisuals` (roads, stakes, stage
  model of every off-site house: slab → studs → trusses → OSB → wrap/windows/shingles → siding → doors, drive, sod, shrubs).
- `DevApps.cs` — tablet apps "Plans", "Builder", "Development" (+ site-plan map); `NeighborhoodComplete` screen + summary.
- `DevSave.cs` — SaveSystem key `"development"` (re-applies the on-site lot/inspection state in `AfterLoad`).

## Plan format
Data-driven, documented in `Docs/PLANS.md` (fields, coordinates, rules for derived walls/openings, `HousePlans.Register`).
Finish grade scales finish costs: `HousePlan.FinishFactor(level)`; spec houses override with `HouseProject.FinishLevel`.

## How to test
- Batch suite (CLAUDE.md command): `DevelopmentTests` = takeoff hand count (starter-900), plan overlay, builder draws on
  inspections + change order + warranty, buy land + plat 14 lots + site work, spec street at staggered stages + sale,
  full neighborhood completion + summary, save round-trip. World shots: `Logs/shots/dev_01..06_*.png`.
- App shots need the windowed UI capture (`Docs/UI-CAPTURE.md`): `ui_tablet_plans/builder/development.png`.
- Apps: Plans in group "Plans"; Builder + Development in "Business". Builder/Development are Career-only.

## Not done / next
- IMGUI app screenshots can't be taken in batchmode (OnGUI doesn't run); world screenshots only.
- After the Town merge: check `TownLotAdapter` against the real API, and that `DevVisuals` roads only draw for fallback lots.
- Crew workers should drive `Projects.Crew`; balance pass on costs/prices with the Fleet finance app.
