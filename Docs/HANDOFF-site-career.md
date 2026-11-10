# Hand-off: site-career (Milestone 6 + platform) — branch `claude/site-career-92d50d`

## Built
- **Inspection framework** `Scripts/Inspection/`
  - `Inspections.Register(id, InspectionPhase, description, Func<IEnumerable<InspectionIssue>>)` (same id replaces). Phases: Footing, UnderSlab, Framing, RoughMEP, Insulation, Final.
  - `InspectionIssue(msg)` / `InspectionIssue(msg, worldPos)` → red wire flag + floating pointer at the spot, note shown on screen when near.
  - `Inspections.Evaluate(phase)` (dry run), `Inspect(phase)` (records, markers, `Completed` event), `CurrentPhase` / `CurrentPhaseLabel` (phase tracker),
    `RequiredPhases` (career narrows), `ChargeForScheduling` (re-inspection $75 + a day lost), `RuleFilter` (tests).
  - `InspectorNpc.Dispatch(phase)`: procedural inspector (hi-vis vest, hard hat, clipboard) walks issue spots + building corners, then signs.
  - Tablet app "Inspections". Own rules in `SiteRules`: loose framing members (Framing), grade 6"/10' away from foundation (Final, ≥400 sq ft), address/mailbox (Final).
  - `Site.Lot` / `Site.InLot(p)` — rules should only look at the lot.
- **Save/Load** `Scripts/Save/SaveSystem.cs`: pieces, stock+scrap, wallet+log, belt (tools, battery, durability, slots, nails), clock, snap, drop point, player, settings, extensions.
  `SaveSystem.RegisterExtension(key, Func<string> save, Action<string> load)` — load(null) = reset. Slots in `persistentDataPath/saves`. F5/F9, autosave every 5 game minutes (`SaveHotkeys`). `AfterLoad` event.
- **Landscaping** `Scripts/Landscaping/`: editable `Grade` heightfield (lazy, cuts a hole in the ground plane), covers (grass/dirt/topsoil/seeded/mulch/gravel/sand/lawn);
  tools shovel, spade, rake, hoe, hose, post-hole digger, wheelbarrow, sod cutter, tiller, landscaping hands (layout kit); staged jobs (`LandscapeFeature`):
  paver walk, gravel drive, privacy fence, gate, plants (dig/set/backfill/water), sod rolls (knit into lawn), retaining wall blocks (stack w/ setback), path lights, mailbox; seed+straw germination.
  Own textures (`LandSurface` enum + `LandMaterials`) instead of editing shared SurfaceKind. "Garden Center" tablet app. Bulk sold per cubic yard (`Bulk`).
- **Career** `Scripts/Career/`: `GameSettings` (Relaxed, UnlimitedMoney, InspectionsEnabled, WeatherEnabled, Mode), `Career` (5 contracts, job board app, deposit, pay on Final, reputation, unlocks, lot types flat/sloped/wooded),
  `Weather` (rain/temperature, `CanPourConcrete`, `CanRoof`, tarps protect sensitive piles), `MainMenu` (F10; skipped in batch mode), `SiteHud`, `SiteBoot`.
- **Audio** `Scripts/Audio/Sfx.cs`: synthesized clips, `Sfx.Play(name, pos)`, ambient wind/birds/rain, footsteps by surface, hammer/nailer/saw clicks.
- **UI skin** `Scripts/UI/Skin/UISkin.cs`: rounded 9-slice panels/buttons, procedural icons.

## Shared-file edits (small, additive)
- `UI/Tablet.cs`: `UISkin.Apply()` at top of OnGUI, rounded panel background, ToolShop hides career-locked tools (`Career.IsUnlocked`).
- `UI/Hud.cs`: `UISkin.Apply()`, rounded panels for the info box, belt slots and toast.

## State / next steps
See the bottom of this file for the latest test status.

## Status (latest run)
- 0 compile errors; PlayMode 6/6 pass (2 milestone-1 + SaveLoadRoundTrip, InspectionFailsThenPasses, CareerContractPaidOnFinal, LandscapingShowcase).
- Screenshots reviewed: `35_landscape_overview`, `36_walk_sod_closeup` — paver running bond, mulch bed with boxwoods/hostas, path lights, gravel drive, maple, fence read well.

## Known issues / next steps
1. Paver walk on convex grade: its sloped slab follows A→B linearly, so mid-run the side/edging shows above the lawn (looks like a black rail). Fix: sample grade along the run and raise/lower the field to the walk surface on both edges (backfill shoulders) after paving.
2. Sod: the test lays rolls on a gappy pattern (test coordinates), and the Sod texture is brighter than the lawn — darken `LandSurface.Sod`, lay contiguous rows in the test.
3. Fence reads as a flat brown panel from a distance — add gaps/shadow between pickets or slightly darker cedar.
4. Not done: inspector/marker/menu screenshots not yet reviewed (`20-22_*`, `30-34`, `37-39`); truck engine/backup beeper are synthesized (`Sfx.Play("truck"/"beeper")`) but DeliveryService doesn't call them (shared file) — hook later.
5. Autosave every 5 game minutes = every ~4 real seconds at 20 min/day (spec literal); `SaveSystem.AutosaveGameMinutes` is tunable.

## Exact next steps for the next session (in order)
1. `git merge main` (has Milestone 2 at ee1ecc9). Expected conflicts/overlaps:
   - **Terrain**: main's `SiteGround` (Scripts/Concrete/SiteGround.cs, see Docs/CONCRETE.md) hides the bootstrap "Ground" plane and adds a 60'x60' diggable patch.
     My `Grade` also replaces Ground (it cuts a hole in the "Ground" mesh). Pick ONE terrain: preferred is to make `Grade` a thin adapter over `SiteGround`
     (HeightAt → SiteGround.HeightAt, Raise/Flatten → Dig/Fill, Paint/PaintRect → SetSurface; GroundCover values Topsoil/Seeded/Mulch/Sand/Lawn need
     SiteGround surface support or a cover overlay), OR make SiteGround delegate to Grade. All my callers go through `Grade.*` / `Grade.Field.*`,
     so only Grade.cs (+ LandscapeFeature.LowerRun which writes `Grade.Field.H` directly) needs to change. SiteGround is only 60'x60'; career lots are larger.
   - **SurfaceKind**: main appended 4 values. I added none (landscaping uses its own `LandSurface` enum), so no conflict expected.
   - `Hud.cs` / `Tablet.cs`: my edits are small (skin calls, rounded panels, locked tools in Tool Shop). Re-apply if main changed them.
2. Re-run PlayMode tests; fix LandscapingShowcase/SaveLoadRoundTrip if the terrain changed (they call `Grade.Create(x0, z0, cells)` and `Grade.Field.SetHeights`).
3. Closeup-finish bridge: their rules are in `Scripts/Finish/Core/CloseupInspection.cs` (`CloseupInspection.Rules`: Id, Phase Insulation/Drywall/Exterior/Finish,
   Code, Description, Check → IEnumerable<Violation{RuleId, Position, Message}>). Add `Scripts/Inspection/CloseupBridge.cs` (once merged) that registers each as
   `Inspections.Register(id, map(phase), desc, () => check().Select(v => new InspectionIssue(v.Message, v.Position)))`.
   Phase map: Insulation→Insulation, Drywall→Insulation (or Final), Exterior→Final, Finish→Final.
4. Visual fixes from "Known issues" above; then look at all `Logs/shots/2x_*`, `3x_*` screenshots.
5. Hook truck sounds: a small additive call in DeliveryService (`Sfx.Play("truck", pos)` on arrival, `"beeper"` while backing up).

## Gotchas
- Tests share one world: put test content far from the origin but well inside the ±305 m ground (x=300 was at the world edge and the inspector never finished).
- `GameSettings.ShowMenuOnBoot`: the main menu is skipped when `Application.isBatchMode` is true, so tests never see it.
- `Object.GetInstanceID()` is a compile ERROR in this Unity (6000.6): use `GetHashCode()` or `GetEntityId()`.
- `LandModel.Mats` (not `Materials`), because the property name would shadow the static `Materials` class.
