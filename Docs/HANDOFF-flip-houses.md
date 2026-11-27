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

## Status when paused (2026-11-27, lead's "pause for the night")
- Feature complete and committed. main aa6ed35 (map-followup) is merged into this branch.
- Filtered run `FlipTests` + `JobCatalogTests`: all pass (UICapture skipped in batch). Exterior/kitchen shots looked at.
- Last full suite (after the main merge): 131 total, 128 passed, **1 failed**, 2 skipped. The failure was
  `TownTests.TownBuildsWithHousesAndAnchors` ("Overgrowth inside the build site"): a market listing left over from an earlier test
  (Career playthrough) was dressed on a core-town house and some grass fell outside its lot. **Fixed, not yet re-run**:
  grass/saplings are clipped to the lot, the dumpster is skipped if it would leave the lot, listings are only dressed while
  flipping is unlocked, and `MainMenu.NewGameStarted` resets the flip market.
- A first full-suite attempt before that crashed in `FullMapTests` (GPU out of memory with 7 Unity editors open) — environment.

- 2026-11-27 resume: main c1d7e79 merged. Two full-suite attempts through unity-run.sh failed before running any test: "No valid Unity Editor license found" (exit 198). Needs the license re-activated in Unity Hub.

## Left to do (in order)
1. One full PlayMode run through the lock wrapper (`unity-run.sh`, see CLAUDE.md command). Expect 0 failures; check `flip_*.png`.
2. If 0 error CS: windowed app capture through the wrapper (no `-batchmode`):
   `-testFilter Groundbreak.Tests.FlipTests.CaptureFlipsApp` → `Logs/shots/flip_app_market.png`, `flip_app_my_flip.png`; look at them.
3. Merge main again if it moved, commit, SendMessage the lead "flip-houses DONE @ <commit>" with branch and test counts.

## Follow-ups (not done)
- Kitchen/bath upgrades barely change the look: the catalog task models (cabinet, sinkbase…) are small; a renovated kitchen
  should get new painted cabinet fronts/counter (see `flip_03_dated_kitchen` vs `flip_05_renovated_kitchen`).
- Crew work on a flip is simulated per day (no NPCs walk the flip house).
- Player material purchases at the hardware store aren't attributed to the flip's cost.
- App screenshots need the windowed run (Docs/UI-CAPTURE.md) with `-testFilter Groundbreak.Tests.FlipTests.CaptureFlipsApp`
  (not run by this session).
