# Handoff: structure-extras (windows/doors, I-joist floor, roof valleys)

Branch: `claude/structure-extras-dfa71f`. Commits: `c09a34d` openings, `37e1829` I-joist floor, `dd7834e` roof valleys.
Tests: `Assets/Groundbreak/Tests/StructureExtrasTests.cs` (5 tests; shots `Logs/shots/extras_01..16`).

## 1. Window and door models (`Scripts/Framing/OpeningModels.cs`)
Plugged in through a new hook, `ProductModels.Overrides` (a list of `(def, size, MeshBuilder, mat) => bool`, first match wins).
- Vinyl `win-dh-*`, `win-sh-*`, `win-slider-*`: frame with a sloped sill and weep slots, jamb liners/parting stops, two sashes
  with meeting rails + interlocks, double-pane IGUs (spacer visible), cam lock + keeper, lifts, tilt latches (DH), half screen
  (charcoal mesh), 1-1/4" nailing fin with nail slots on the sheathing plane.
- `door-ext-steel*` six-panel prehung: 4-9/16" jambs, stops with compression weatherstrip, aluminum threshold + oak cap,
  three 4" hinges (knuckles inside), 2-1/8" face bores + 1" edge bores (no lockset: real prehung units ship bored), strikes,
  brickmold. `RoughOpening.Seat` puts these doors with the jamb flush to the sheathing (2x6 walls: extension jamb inside).
- `door-slider72`: vinyl frame, sill tracks, fixed + operable panels, interlocks, handle, sliding screen, fin.
- `garage-9x7`, `garage-16x7` (carriage + opener unchanged): 4 embossed sections, struts, end/center stiles, hinges,
  nylon rollers, 2" tracks with a 12" radius, horizontal run + rear hangers, torsion shaft, drums, cables, springs.
Sizes are unchanged, so they fit the same rough openings.

## 2. I-joist floor (`Scripts/Framing/IJoistFloor.cs`, `IJoistEnd.cs`)
- In Framing Plans, choose "Use I-joists" before starting. `Footprint.Make(..., iJoist)` picks 9-1/2" (span up to 16'6") or
  11-7/8" (up to 20') and sets the deck height. `ShellProject.StartFloor` sends I-joist projects to `IJoistFloor.Build(fp, openings)`.
- 1-1/4" LVL rim board, I-joists at 16" O.C. Past 20' of span: an interior bearing line (PT sill on the center footing) with a
  3-ply LVL girder, the joists in face-mount hangers, and OSB web stiffeners at every hanger. 2x4 squash blocks (joist depth +
  1/16") go under the jack studs of every opening. No mid-span blocking.
- Catalog: `ij9x16/20/24`, `ij12x16/20/24`, `lvlrim*`, `lvlbeam*`, `hanger-ij9/12`, `web-stiffener`. The I-joist model has
  flanges, an OSB web and a print strip.
- Inspection: a new `FramingInspection.Checks` hook. It checks span against depth, hangers on joists that frame into a beam,
  and web stiffeners.
- `FloorPlan` sill and subfloor steps were moved into `AddSills`/`AddSubfloor`. Behavior is the same.

## 3. Roof valleys (`Scripts/Roofing/CrossGable.cs`)
- `CrossGable.Frame(mainRoofInfo, centerX, widthFt, projectionFt)` builds a garage wing off the front eave wall, framed as a
  California (overlay) valley. Wing walls stand on the slab. Valley lines are chalked, 2x10 valley boards lie flat on the main
  deck, then come a 2x10 ridge, 2x8 commons with birdsmouths, valley jacks seat-cut onto the boards, fascia and OSB.
  The valleys are added to `RoofInfo.Valleys`, so the main dry-in lays the main-side ice & water.
- `CrossGable.Roof(cg)` adds wing-side ice & water, underlayment, main courses woven 12" across the valley, and wing
  courses through `ShingleMesh`, cut 2" back on a chalk line (closed-cut). Then the ridge cap.
- `RoofPlane.ExtraBounds` (new, additive) clips the shingle field at the valley cut line.
- Order: main framing, then main sheathing, then `CrossGable.Frame`, then main dry-in, main shingles, then `CrossGable.Roof`.

## Shared-file edits (small, additive)
`Visuals/ProductModels.cs` (Overrides list + 1 line in BuildInto), `Framing/Structural.cs` (`MemberRole.SquashBlock` appended),
`Framing/FramingInspection.cs` (Checks hook), `Framing/Footprint.cs` (IJoist fields, optional Make arg),
`Framing/ShellProject.cs` (floor toggle + dispatch), `Framing/RoughOpening.cs` (Seat depth for detailed doors),
`Roofing/RoofInfo.cs` (ExtraBounds).

## Not done / next
- No UI button for the cross-gable wing yet: it is API + test only. To add one, put a "Add garage wing" button in ShellApp
  after roof sheathing (call `CrossGable.Frame(RoofFraming.InfoOf(p.Roof), ...)`, then `CrossGable.Roof` after shingles).
- The wing front wall has no garage-door rough opening (studs only). The main eave inside the wing is not removed (hidden).
- Wing members aren't in the main roof inspection pass, and the main-roof shingles under the wing are not trimmed (hidden).
- Open metal (W) valley not built; closed-cut with an ice & water lining only.
- Still open from the polish handoff: electrical panel detail, duct seam lines, per-shingle tone variation.
