# Handoff: job-catalog

Branch `claude/job-catalog-85b4dd`. Code: `Assets/Groundbreak/Scripts/JobCatalog/`. Tests: `Tests/JobCatalogTests.cs`. Format, generator and full id list: `Docs/JOBS.md`.

## What it is
A data-driven catalog (`JobDef`, fluent builder) of every job the company can be offered, played through the existing Repairs / Fleet / Development systems:

| Mode | Count | Played through |
|---|---|---|
| Service (repair calls) | 61 (+14 legacy Repairs families) | `RepairType.Catalog` → `CatalogDefect`: steps (prep / diagnose / fix / finish) with states (power off, water off, opened), hidden causes picked by weight, wrong-fix and skipped-step quality penalties, callbacks |
| Scope (remodel) | 12 scopes built from 25 tasks | one `jcr-` job with several catalog defects at one house (Kind Remodel, shown on the Service calls tab) |
| Fleet (contractor) | 22 variants (decks, fences, sheds, garages, driveways, patios, re-roofs) | `ContractorJobs.Create` with the catalog's dimensions/price; lots come from `LotBroker.ContractorLot` via `TownHook.JobLotProvider` |
| Build | 19 (8 houses, 11 outbuilding/commercial) | Development `HouseProject` on a free `DevLot` (TownLotAdapter, skips reserved lots), Builder's deposit + draw schedule; plan ids from the blueprints library, missing ids tolerated (no offer, or Fleet fallback for the garage) |

Generator (`JobCatalog.Generate`, hooked as `RepairJobs.ExternalGenerator`): per-stage targets, weighted by stage gap, reputation, season and weather; no repeats within the last 16 offers.

## Edits outside Scripts/JobCatalog (small, additive)
- `Repairs/RepairDefect.cs`: `RepairType.Catalog` (appended), `DefectState.def`, `AddFor` case.
- `Repairs/RepairJobs.cs`: `RepairPayload.scope`, `ScopePrefix`, `IsService(job)`, `ExternalGenerator` hook, catalog `Describe`/`ToolsFor`, callbacks keep `def`; Kind==Repair checks → `IsService`.
- `Repairs/RepairUI.cs`: two Kind checks → `RepairJobs.IsService`.
- `Integration/IntegrationRuntime.cs`: `JobsHub.TabOf` puts service scopes (`jcr-`) on the Service calls tab.

## Status
JobCatalogTests 7/7. Full PlayMode suite (main 9369180 merged): 109 total, 108 passed, 0 failed, 1 skipped (UICapture, by design).

## Follow-ups
- Blueprints library (`claude/blueprints-311272`) not merged here: until it is, only the six Development plans resolve, so builds like diner, gas station, office, warehouse, townhomes and ADU are skipped. They appear once it merges (ids already used).
- full-map lots: `FreeAddressFor` already requires an enterable `TownHouse` (map lots without one get no service calls). Exterior jobs on commercial lots (`TownLot.Category`) are a possible extension.
- Catalog ceiling stain is a flat disc; a soft-edged decal would read better.
