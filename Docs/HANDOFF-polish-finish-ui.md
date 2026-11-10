# Polish: finishes + UI/feel (branch `claude/polish-finish-ui-887f7a`)

## Done
- **First-person feel** in `Scripts/Player/PlayerFeel.cs`, added in `PlayerRig.Spawn`. It runs in LateUpdate and adds offsets on top of the pose PlayerRig writes, so PlayerRig's logic is unchanged.
  - Head bob, lateral sway and roll are driven by the gait (stride ≈ 0.72 m walking, ≈ 1.0 m sprinting, 0.5 m crouched). Bob is ≈ 26 mm peak-to-peak when walking and is lowest on the heel strike.
  - Footsteps call `Sfx.Footstep` on each heel strike: quieter when crouched, louder when sprinting.
  - Sprint adds a +6° FOV kick. It is additive, so FOV set by anything else is kept.
  - Landing gives a spring-damped dip plus a "land" sound.
  - Teleports (tests, respawn) are ignored. `PlayerFeel.BobScale` (0..1) is the comfort scale.
- **Held-tool view-model** in `Scripts/Player/ViewModel.cs`.
  - It renders on layer 30 with its own camera (55° FOV, depth-cleared), which is the child `ViewModelCamera` of the player camera. Tools never clip into walls.
  - The renderer is enabled only during that camera's render, so test cameras and overviews never show a floating tool.
  - Real-size models for each tool family: hammer/staplers, circular saw, drills/screw gun/router, nailers, tape, 4' level, pry bar, knives/trowels/float, utility/jab/coping saw, roller/sanding pole, brush, caulk/foam gun, snips/cutters, long-handle yard tools. Other tools show nothing.
  - Motion: sway that lags the mouse, gait bob, sprint tuck, landing, equip raise, idle breathing.
  - Use animations: hammer-style swing, nailer/drill recoil (plus motor buzz while held), roller/brush/knife strokes while held, and a poke for everything else.
  - Test hooks: `SetUse`, `SkipEquip`, `Step(0)`, `ShownKind`, `Camera`.
- **HUD** (`UI/Hud.cs`):
  - The crosshair has a dark outline, so it is now readable on white drywall. It tightens and warms to the accent color when the aim hits something within 4 m, and pulses on click.
  - Toasts slide and fade in, fade out, have an accent bar, size to their text and play a soft chime.
- **Tablet:** opens with a fade and slide plus a background dim, has an accent rule, and plays open/close sounds.
- **Tab tool list:** rounded panel, highlighted row, rich text for the 2-slot and battery labels.
- **Audio** (`Audio/Sfx.cs`):
  - 16 new synthesized clips: land, equip, ui-open, ui-close, toast, staple, screw, roll, scrape, brick, caulk, spray, cut, snip, tape, knock.
  - `Sfx.ToolSound(id)` gives every finish, exterior and MEP tool a click sound at the aim point.
  - Sprayers, rollers and drills repeat their sound while held.
  - The old distance-based footstep code is replaced by the gait-driven `Sfx.Footstep`.
- **Finishes:**
  - `DrywallJob.Complete/Finish` sheen is now `Sheen?`. With no value it uses the trade default: flat on ceilings, eggshell on walls.
  - Red-oak hardwood is less orange (handoff item) and its gloss is satin (0.32).
- **Tests:** `Tests/PolishFeelTests.cs` (4 tests).
  - What they cover: the view-model family for each tool, gait bob and steps, teleport ignored, the new clips are audible and don't clip, and the tablet closes and releases the UI.
  - Shots: `Logs/shots/ui_vm_<tool>.png` and `ui_hud_3d.png`.

## Notes / caveats
- **IMGUI can't be screenshotted in batch mode.** OnGUI doesn't run there, and `WaitForEndOfFrame` hangs. The HUD test tries a GUI-into-RenderTexture capture with a timeout, logs "not available", and keeps the 3D+view-model shot. Check the HUD and tablet visually in the editor.
- The gait test sets `Time.captureDeltaTime = 1/60`. Batch mode runs at about 5000 fps, and CharacterController ignores moves under 1 mm (minMoveDistance), so `isGrounded` stays false. The same can happen in a real build with uncapped frame rate above about 3000 fps; that's harmless.
- Shared-file edits, all small and additive:
  - `PlayerRig.Spawn` gets two `AddComponent` lines.
  - `Tablet.cs` gets the open transition and sounds.
  - `Hud.cs`: the crosshair and toast code moved into methods.
  - `ToolBelt.OnGUI` gets the list styling.

## Next ideas (not done)
- Drywall paint: subtle roller stipple in eggshell, plus a visible sheen difference between flat ceilings and eggshell walls under the room light.
- Trim: caulk line at the casing/wall joint. Cabinets: toe-kick shadow and crown.
- Hide the view-model while the tablet is open or during placement ghosts. Add a hands/glove model for the "place" tool.
- Belt slot icons (UISkin.Icon) per tool family.
