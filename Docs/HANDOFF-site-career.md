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
