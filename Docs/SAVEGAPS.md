# Save gaps

Fixes for the "Save/load gaps" in CODECHECK-A / CODECHECK-B. Code: `Assets/Groundbreak/Scripts/SaveGaps/`,
tests: `Assets/Groundbreak/Tests/SaveGapTests.cs`.

## Now saved

| Save key | What | How it comes back |
|---|---|---|
| `exterior` | Every `ExteriorWall`: house wrap sheets, cap staples, seam tape, opening I-cut + flashing steps, roll left; vinyl (courses, panels, every nail, lock/removed state, color); fiber cement; board-and-batten; brick veneer (courses, ties, weeps, lintels, mortar); trim boards and sealant joints (caulked beads). Every eave (soffit, fascia wrap, gutter + slope, downspout). Every deck (layout + stage). | The wall is found again on the restored framing by its face plane (normal, offset, a point on it); layer data is copied back and rebuilt. The deck is replayed stage by stage with stock unlimited (it was paid for). Deck lumber is left out of the plain piece list so it isn't doubled. |
| `mep` | Every run (spec, points, staples, tags, both end connections), every free-standing node (elbows, tees, wyes, sanitary tees incl. reducing ones, couplings, caps, duct takeoffs, duct end caps; unknown types as plain port nodes), every port's joint-made / test-cap / join method, bored holes (`StudHoles`), electrical box make-up / device / plate / wet / plug load, panel breakers (slot, type, label, on/tripped, landed home run), main + cover, meter set, pressure-gauge test state, DWV water test, last rough-inspection pass/fail. | Fixtures, boxes, panels and equipment are catalog pieces (already saved); `MepAutoAttach` rebuilds their ports and they are found by definition id + position. Free nodes are rebuilt with the same builders (`Fittings.Create`, `PlumbFit.Create`, takeoff / end-cap bodies), then runs are re-created on their ports. |
| `slabs` | Every set `ConcreteElement` (outline, top, thickness, psi, pour time, cure lag, finish, covered / wet cure / forms stripped, defects, anchor bolts, control joints) and the curing blankets / heater on it. | A "Concrete Slab" object with a real-size mesh of the outline and a collider, the same ConcreteElement data (cure keeps counting) and the protection put back without charging stock again (`SeasonRules.RestoreProtection`, applied after every section has loaded). |
| New Game | `MainMenu.NewGame` now calls `SaveSystem.ResetBaseState()`: site stock back to the starting materials, scrap emptied, starter tool belt with a full pouch, snap origin reset. The warehouse/yard stock was already reset by the `yardfleet` section. | |

`SaveSystem.Version` is 2. Version-1 files load unchanged: a missing section loads as empty (the session's runs / walls / slabs are cleared, nothing is restored for them).

## Adding a save section

1. In your feature folder, register once from `BeforeSceneLoad`:
   `SaveSystem.RegisterExtension("mykey", Save, Load);`
2. `Save()` returns JSON (`JsonUtility` on a `[Serializable]` DTO; never null-able nested classes, use has-flags).
3. `Load(json)` first clears the feature's runtime objects (deactivate + destroy, and clear static lists right away), then
   returns if `json` is null (old save or New Game) or rebuilds from the DTO. It runs after pieces, stock and belt are restored.
4. State that hangs off a piece: refer to it with `SaveGaps.Ref(piece)` and find it with `new SaveGaps.PieceIndex().Find(ref)`.
5. Pieces your feature rebuilds itself: `SaveSystem.RegisterPieceFilter("mykey", p => p.GetComponent<MyPart>() != null)`.
6. Needs another section loaded first? Do that part in `SaveSystem.AfterLoad` (see `SlabSave`).
7. Rebuild steps that toast: wrap in `SaveGaps.Quiet(...)`.

## Still not saved (player-placed or player-made)

- Concrete before it sets: forms (`FormOutline` / form boards' outline data), rebar mats and chairs, plastic pours, layout string lines.
- Roofing layers and `RoofInfo` (underlayment, shingles, drip edge, flashing), and roof framing data beyond plain pieces.
- Drywall (`DrywallSurface` sheets, tape, mud coats), paint (`PaintColors`), insulation batts / blown insulation.
- Interior finish: cabinets, countertops, plank and tile floors, trim pieces, door hanging, interior caulk beads.
- Stairs: cut stringers (`CutStringer`) and layout marks (`StringerLayoutMark`) reload as plain boards.
- Small leftovers: MEP stock offcut feet (`MepStock`), partial house-wrap tape / flashing-tape rolls, `ElLoad` box mounting (recomputed on demand).
- Statics that a New Game still keeps (CODECHECK-A): `StairCalculator` layout, `ShellProject.Current` / `ShellApp`, `PaintColors.Current` / `CurrentSheen`, the `FinishSupply` cart, `FinishStock.Unlimited`; the old `TapeMeasure` line object after a belt swap.
- A restored slab has no `PourSystem` behind it (it can't be finished again; landscaping tools that check for a `PourSystem` treat it as plain ground), and its cure tint is fixed at load.
