# Handoff: business tutorials (branch `claude/pensive-satoshi-f33889`)

## What's here
- **Business tutorials** in the existing Tutorials tablet app, phase **"Running the business"** (listed right after
  "Getting started"): 11 tutorials, 45 steps, one real in-game picture per step (`Resources/Tutorials/bt_*.png`).
  First service call · van & fast travel · hardware store · warehouse & truck · contractor job · crew (hire, assign,
  tasks, payroll, morale) · money (Company app, daily costs, P&L, loans, bankruptcy) · Plans app · client house ·
  land & neighborhood development · reputation & stages.
  Code: `Assets/Groundbreak/Scripts/BusinessTutorials/` — `BusinessTutorials.cs` (registration, phase, locators),
  `BusinessTutorialContent.cs` (all text; numbers taken from the game code), `TutorialCapture.cs` (where capture tests write).
- **Tests** `Assets/Groundbreak/Tests/BusinessTutorialTests.cs`:
  - `B_CaptureBusinessCareer` plays one career (van → sold-out neighborhood) and photographs every step. World pictures
    are camera renders; tablet pictures are real IMGUI captures (windowed run only; in batchmode they are only checked to exist).
    Also `Logs/shots/tut_business_*.png` (the Tutorials app showing business steps, windowed run).
  - `C_EveryBusinessTutorialHasStepsAndPictures`: every tutorial has steps, every step has text and its own picture that
    loads, every "Open <app>" names a real Career app.
- **Fix: normal test runs no longer rewrite the tutorial PNGs.** Capture tests write to `Logs/shots/tut_pics/` unless
  `GROUNDBREAK_CAPTURE_TUTORIALS=1` is set. How to regenerate: `Docs/TUTORIALS.md` → "Regenerating the pictures".

## Shared-file edits (small)
- `Assets/Groundbreak/Tests/TutorialCaptureTests.cs`: its 3 write sites now go through `TutorialCapture.OutputDir/OutputPath`
  (and the batchmode existence check uses `TutorialCapture.Committed`). Nothing else changed.
- `Docs/TUTORIALS.md` (new sections), `Docs/UI-CAPTURE.md` (one line: set the variable for capture runs).
- `Scripts/UI/Skin/UISkin.cs` (bug fix): `GUI.skin.box` was a plain white texture (meant only for `UISkin.Panel`'s tinting),
  so apps that draw `GUI.skin.box` directly (Crew rows, Dealership cards, crew K-menu, close-up tags, paint swatch) showed
  white boxes with unreadable white text. The skin's box is now a dark rounded panel; `Panel()` uses its own white style.
- `Scripts/Integration/IntegrationRuntime.cs`: one line, Job Board visible only while a legacy contract is active.
- `Scripts/Tutorials/TutorialContent.cs`: the `biz-career` step text points at the Jobs app.

## Also done
- Old Career "Job Board" retired: `IntegrationRuntime.SetupTablet` shows it only while a legacy contract is active
  (`Career.Active != null`); the `biz-career` tutorial step and `TutorialCaptureTests.F_CloseupAndSite` now use the Jobs app
  for `biz_04_career` (the committed picture is still the old Job Board shot until the windowed run below).
- main (aa6ed35, map-followup) is merged.

## Status: DONE
- All 45 step pictures captured (20 world renders + 25 real tablet/HUD captures from a windowed run) and looked at;
  `Logs/shots/tut_business_01..04.png` show the Tutorials app with business steps.
- Full PlayMode suite (batchmode, after merging main): 133 total, 131 passed, 0 failed, 2 skipped.
  `git status` after the run: no tutorial PNG touched (only Unity's own ProjectSettings rewrite, reverted).

## Notes
- The Builder app no longer exists (it is Jobs → Houses); the tutorials point to Jobs tabs accordingly.
- Re-roof job text in `ContractorJobs.cs` still says "Contractor Jobs app" (now Jobs → Contractor) — not changed here (not my folder).
- Run Unity only through the shared lock wrapper (see the lead handoff).
