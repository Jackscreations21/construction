# Handoff: subcontractors (`Scripts/Subs/`, branch `claude/busy-satoshi-2c8778`)

The feature notes are in `Docs/SUBCONTRACTORS.md`.

In Career, from the Contractor stage, the "Subcontractors" app lists 3 local companies for each of 12 trades.
1. You get a quote measured from the real remaining work.
2. Accept it, and the sub is booked after its lead time. Cheap subs are often late.
3. The sub's truck and crew arrive and do the work through the crew task machinery.
4. When the work is done, the invoice is paid from the wallet.
5. Cheap subs leave real defects. A callback fixes them for free.
6. Repeat jobs earn up to 10% off.

## Shared-file edits (tiny, additive)
- `Crew/CrewNpc.cs`: added `public bool External;`. This marks an NPC that is not one of your workers.
- `Crew/CrewManager.cs`: `SyncNpcs` skips `External` NPCs. Before this, it destroyed any NPC whose worker wasn't in `Company.Workers`.

No other shared files were touched. The app registers itself:
- `Tablet.RegisterApp`
- `SetAppGroup("Business")`
- Career-only `SetAppVisible`

## How it hooks in
- Sub workers are temporary `Worker`s, never added to `Company.Workers`. There is no payroll and no stage effect.
- Each sub worker's `AssignedJobId` is a slot id `sub:<contract>:<n>`, so `CrewManager.PickTask` only hands them their own tasks.
- Their tasks sit in `Crew.Tasks` while live.
- `SubCrews.PurgeStrayTasks` removes any `sub:` task that the crew save restores.
- Subs recreate their tasks from the world on the next arrival, so the `"subs"` save only stores contracts and relationships.
- Inspections: on `Inspections.Completed`, a failed issue inside a finished sub's area flags that contract for a callback.

## Tests
`Tests/SubcontractorTests.cs`, around (-1600, 0, 1500):
- catalog and pricing
- stage gate
- premium framing sub (shots `subs_crew_working`, `subs_finished_wall`)
- cheap sub's defects, then callback
- late sub and save round-trip
- `SubcontractorsAppShot`: windowed run only (`subs_app`)

## Not done / ideas
- Only framing is exercised end to end in tests. The other trades' measuring and tasks use the APIs their own trade tests use, but have no sub test yet. Priority: drywall, insulation, electrical.
- One FramingJob is one task, so on a single wall only one sub hand works and the others stand by the truck. Splitting big layouts into helpers would look better.
- Crew members just disappear when the job is done. They don't walk back to the truck first.
- Concrete defects (no vibration) have no inspection rule; neither do paint and shingles.
- Excavation only digs the 12"x12" perimeter footing trench of the active slab form on the building pad.
- Plumbing and HVAC are priced per joint left to make, not per fixture. The game has no fixture plan in the world.

## Status at pause (2026-11-27, lead asked all sessions to pause for the night)
- Committed on `claude/busy-satoshi-2c8778`. The last full PlayMode run (main d68e427 merged): 127 total, 125 passed, 0 failed, 2 skipped (UICapture + `SubcontractorsAppShot`, windowed-only).
- Shots looked at: `subs_crew_working` (flatbed with door signs, a sub carpenter framing), `subs_finished_wall` (wall framed and braced).
- **Left to do, in order:**
  1. `git merge main` (main is now aa6ed35 with map-followup).
  2. One full-suite batchmode run through `unity-run.sh`. Check 0 `error CS` and 0 failed.
  3. One windowed run through `unity-run.sh` with `-testFilter Groundbreak.Tests.SubcontractorTests.SubcontractorsAppShot`, no `-batchmode`. Look at `Logs/shots/subs_app.png`: check the app layout, and that the trade buttons and company rows aren't clipped.
  4. Commit, then SendMessage the lead "subcontractors DONE @ <commit>" with the branch name and test counts.
