# Smart placement — handoff

Branch `claude/smart-placement-851e8c`. Full PlayMode suite: 54/54 (50 existing + 4 new).

## What it does
`Scripts/Building/SmartSnap.cs` looks at pieces near the aim point (one `OverlapSphereNonAlloc`, ≤32 neighbours, ≤8 candidates)
and proposes poses from their real geometry; `PlacementController` picks the best non-overlapping one.
- Lumber on a board face: stud stands up on a plate at the next on-center mark (spacing read from the studs already there,
  12/16/19.2/24", pulled from the plate end like a tape; last mark flush with the end), "gap"/"fits" readout to the plate above;
  doubled top plate flush (when studs are under the plate); jack flush against king (bottoms level); joist butted into a rim (tops flush).
- End grain: splice; plate across stud tops (stacks over the bottom plate if same length); rim across joist ends; corner turn.
- Boards lying on a surface: continue their O.C. layout, or splice / extend in line.
- Sheets: lie on the framing plane, edges on member centerlines or flush with end members, butt to existing sheets;
  walls start at the bottom of the wall (next course stacks on a sheet in the same column).
- Everything else: takes the orientation of the nearest same product, then faces flush / butted / corner.
- No neighbours → exactly the old grid + edge-snap pose (existing tests unchanged).

Controls: Q / Shift+Q cycle fits, hold Ctrl (or `SmartSnapOff = true`) = plain grid, scroll = manual rotation (position still aligns).
Feedback: pulsing yellow outline on the target piece, label next to the crosshair, "snap: …" in the status line, quiet `Sfx` click on change.
Tablet help line registered from `PlacementController`.

## Shared-file changes
`PlacementController.cs` (owned): additive public members `Select`, `CycleSnap`, `SmartSnapOff`, `SnapLabel`, `SnapTarget`,
`SnapCandidateCount`, `GhostPosition`, `GhostRotation`, `GhostValid`. `ExternalCheck` / `PiecePlaced` unchanged. `PlaceTool.Hint` mentions Q.

## Known follow-up (Framing)
`StructuralRules.Classify` calls every flat board a BottomPlate, so a doubled top plate (correctly snapped) shows red
"unsupported" through `ExternalCheck`. Pre-existing; fix belongs in `Scripts/Framing/Structural.cs` (e.g. flat board resting
on studs/top plate → TopPlate/CapPlate).
