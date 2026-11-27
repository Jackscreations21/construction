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

## Status: DONE
- Full PlayMode suite (after merging main c1d7e79+): 130 tests, 129 passed, 0 failed, 1 skipped (UICapture, by design). 0 error CS.
- All 5 EquipmentTests pass. Screenshots `Logs/shots/equip_01..10_*.png` were looked at: excavator close, digging the trench,
  loading the dump truck; skid steer bucket + forks with an OSB lift; boom lift stowed + raised; crane close, swinging a truss onto
  the walls, trusses set.
- Fixes during testing: crane wire rope now follows the truck (`Machine.AfterMove` hook); tests wait for the 4 s outrigger deploy
  and load the truck from undug ground.

## Ideas / known simplifications
Excavator blade only raises/lowers (no dozing); boom lift doesn't enforce the 10.5 m reach limit; booms don't collide with the world;
crane bed truss bundle never shrinks; no hook pendulum. Crews could later operate machines (Crew dig tasks via `MiniExcavator.DigPass`).
