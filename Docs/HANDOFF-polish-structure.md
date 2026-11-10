# Handoff: polish-structure (structure + MEP look and feel)

Branch: `claude/polish-structure-0007ff` (based on main fb84616). Commits, oldest first:
1. `687f549` Lumber grade stamps, OSB panel stamps, per-board tone, nail heads + recoil on fastening, softer OSB/pine textures
2. `3c2cdae` Gable-end OSB sheathing step (cut to the rake), calmer shingle blend
3. `f5f2abd` MEP: printed legends on PEX, PVC/ABS DWV and NM-B runs; streaky dauber primer; crinkled foil tape; calmer galvanized
4. `a1b5855` Feel: staged tilt-up with a landing rock and thud, pulsing outline on the layout ghost, pine saturation toned down

Tests: new `Assets/Groundbreak/Tests/StructurePolishTests.cs` (4 tests; screenshots `Logs/shots/polish_01..05`).
ShellFraming, Roofing, Stairs, Plumbing, Electrical, Hvac and StructurePolish all pass (run filtered after each step).
See the lead message for the full-suite result.

## New files (own folders)
- `Scripts/Framing/LumberMarks.cs`: board tone palette (`Tone(variant)`), ink stamps (5x7 bitmap font, worn ink,
  alpha-cutout decal quad, `AddStamp`), `Glyph(char)` (public font), `NailHead(piece, hit)`.
- `Scripts/Framing/FastenFeedback.cs`: `Kick(deg)` camera recoil, applied in LateUpdate after PlayerRig sets absolute pitch,
  and `Nailed(piece, hit, kick)`.
- `Scripts/Framing/GhostOutline.cs`: LineRenderer box edges on the FramingJob layout ghost, pulsing.
- `Scripts/Networks/MepPrint.cs`: `RunMaterial(spec)` adds the print strip for PEX, DWV and NM-B; `Primer` and `FoilTape`
  materials.

## Shared-file edits (small and additive)
- `Visuals/ProductModels.cs`: `Kit.Variant`; `Lumber()` gets a tone tint and a grade stamp; new `Sheet()` adds an OSB or
  plywood panel stamp.
- `Visuals/ProceduralTextures.cs`: PineSpec colors and knots (7 down to 4, lighter); OSB gap colors, softer strand edges,
  cavity 0.45 to 0.30, fewer chips, normal strength 3 to 2.2; Galvanized spangle tone range narrowed.
- `Visuals/Materials.cs`: Look values: PineLumber gloss 0.15 to 0.10; Osb gloss 0.08, bump 0.75; Galvanized metal 0.85 to 0.62.
- `Tools/FramingHammer.cs`, `Tools/CordlessFramingNailer.cs`: call FastenFeedback (kick per swing, nail head per nail).
- `Networks/NetRun.cs`, `Networks/NetGraph.cs`, `Plumbing/PlumbTests.cs`: use MepPrint materials.
- `Framing/WallPlan.cs`: `TiltUpRoutine` uses `WallAssembly.RaiseAngle(k)`, then a landing rock and `Sfx "thud"`.
- `Framing/FramingJob.cs`: one line, `GhostOutline.Update(_ghost)`.
- `Roofing/RoofInfo.cs`: `FramingTopAtWall` and `Slope` (set in MakeInfo).
- `Roofing/RoofFraming.cs`: `AddGableSheathing` steps (non-hip roofs) before the roof-anchor step in `RoofSheathing`
  (role WallSheathing, stock osb716).

## Not done / next ideas
- Window and door models: untouched.
- Electrical panel detail (breaker labels, door, knockouts) and the duct snap-lock seam line: untouched.
- Shingles now read as an even gray. A per-shingle tone variation would need a second shingle material variant in ShingleMesh.
- OSB at very close range still shows a slightly busy strand pattern; the strand sizes are already real (75-180 mm).
