# Handoff: flip houses (session flip-houses)

Branch `claude/nifty-swanson-4ed79b`. Feature folder `Assets/Groundbreak/Scripts/Flips/`. Docs: `Docs/FLIPS.md` (how it plays, numbers, API).
Tests: `Tests/FlipTests.cs` (5 batch tests + `CaptureFlipsApp`, category UICapture, windowed only). Shots `Logs/shots/flip_*.png`.

## Shared-file edits
**None.** Everything uses extension points: `Tablet.RegisterApp/SetAppGroup/SetAppVisible/AddHelp`, `Company.RegisterDailyCost`,
`Company.DayStarted/StageChanged/Changed`, `SaveSystem.RegisterExtension("flips")` + `AfterLoad`, `Town.Built/Destroyed`,
`JobCatalog.Register` (two new tasks, weight 0), `CatalogProps.Register` (two models), `RepairDefect.Create`, `JobCatalog.Solve`,
`Crew.Assign/Speed/Quality`, `Finance.TakeBankLoan`, `Developments.DaysToSell/MarketCycle/CommissionPct/ClosingPct`.

## Design notes for whoever touches it next
- The punch list is a private `Job` (`flip-<id>`, never on the board or in `Company.ActiveJobs`, so it doesn't use a job slot); its
  defects live in `RepairDefect.All` and the normal RepairUI works on them. Pressing B (invoice) there does nothing (SendInvoice
  ignores jobs that aren't active). RepairUI's hint line still says "B sends the invoice" when everything is fixed — cosmetic.
- `RepairJobs.AfterLoad` destroys every live defect; Flips respawns its own on the next frame (`FlipsRuntime.RespawnSoon`).
- Owned houses keep `LotUse.Customer` (TownBuilder only builds houses on Customer lots; `Town.Load` frees any lot with an Owner,
  so flips don't claim lots). Offers at a flip address are pruned on `Company.Changed`.
- `Crew.JobLabel("flip-…")` shows "(finished job)" in the Crew app for a worker on a flip (shared file, not edited).

## Follow-ups (not done)
- Kitchen/bath upgrades barely change the look: the catalog task models (cabinet, sinkbase…) are small; a renovated kitchen
  should get new painted cabinet fronts/counter (see `flip_03_dated_kitchen` vs `flip_05_renovated_kitchen`).
- Crew work on a flip is simulated per day (no NPCs walk the flip house).
- Player material purchases at the hardware store aren't attributed to the flip's cost.
- App screenshots need the windowed run (Docs/UI-CAPTURE.md) with `-testFilter Groundbreak.Tests.FlipTests.CaptureFlipsApp`
  (not run by this session).
