# Handoff: heavy-equipment (paused for the night, 2026-11-27)

Branch `claude/competent-driscoll-b66764` (worktree `.claude/worktrees/competent-driscoll-b66764`). main aa6ed35 merged in.
Feature doc: `Docs/EQUIPMENT.md`. Code: `Assets/Groundbreak/Scripts/Equipment/`. Tests: `Assets/Groundbreak/Tests/EquipmentTests.cs`.
Unity ONLY via the shared `unity-run.sh` lock wrapper (see lead's rule), run in the background.

## Built
- `Machine` base (climb in/out like the van, GInput keyboard joysticks, V camera, ground follow, travel collision, HUD hint + gauges,
  engine sound, `Auto` input override for tests, `AfterMove` hook), `EquipModels` (tracks, wheels, seat, `HydraulicRam` that telescopes).
- `MiniExcavator`, `SkidSteer` (bucket + forks, 2,690 lb ROC), `BoomLift` (40', stand in the basket with F), `TruckCrane` (outriggers,
  load chart + LMI, rigging pallets, setting trusses on a roof truss `FramingJob`).
- `Earth` (cut/fill/deposit on `SiteGround` pad and `Grade` lot field) + `HaulTruck` (dump truck loading, $385/load if not owned).
- `EquipmentLoad` (shingle pallets, OSB lift, CMU block). `Equipment` (catalog with US rent/buy prices, trailer delivery, return,
  daily costs, tablet app "Equipment", save key "equipment", on-foot "E: climb into" prompt).
- **No shared/Core file edited.**

## Test status (filtered run of EquipmentTests, 0 compile errors)
- Pass: RentBuyAndFreeBuild, SkidSteer_ForksAndGrading, BoomLift_RaisesPlayerToWorkAtHeight.
- **Fail: MiniExcavator_DigsFootingTrench_SpoilAndTruck** at the spoil-pile check: `Dump()` reports >0.05 m³ dumped but the pad
  height at the dump point doesn't rise. The test now prints diagnostics (dump point, `Earth.OnPad`, heights, max rise anywhere).
  Suspects: `MiniExcavator.DumpAmount` takes the "off the lot" branch (done = bank when `!Earth.Workable`), or the deposit lands
  somewhere else. Run the filtered test once and read the message.
- **Fail: Crane_LoadChartAndSetsTruss** at `Assert.IsTrue(crane.OutriggersDown)`: the outriggers take ~4 s; the wait is now 5.5 s
  (not re-run yet). The crane wire rope was drawn at the spawn spot; fixed with `AfterMove` (not re-verified).
- Screenshots looked at: equip_01 excavator close, equip_05 skid steer forks, equip_07 boom lift raised (all good).
  Not yet produced / looked at: equip_02 digging, equip_03 loading truck, equip_08–10 crane.

## Left, in order
1. Filtered run (`-testFilter Groundbreak.Tests.EquipmentTests`), fix the excavator spoil + crane failures, look at all `equip_*.png`.
2. Merge latest main, ONE full PlayMode suite run via the wrapper, check `Logs/playmode.xml`.
3. Commit, SendMessage lead "heavy-equipment DONE @ <commit>" with branch and test counts.

## Known simplifications
Excavator blade only raises/lowers; boom lift doesn't enforce the 10.5 m reach limit; booms don't collide with the world;
crane bed truss bundle never shrinks; no hook pendulum.
