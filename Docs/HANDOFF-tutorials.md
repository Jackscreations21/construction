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
- No Core / UI / Integration / Map / Town files touched.

## Notes
- The Builder app no longer exists (it is Jobs → Houses); the tutorials point to Jobs tabs accordingly.
- Re-roof job text in `ContractorJobs.cs` still says "Contractor Jobs app" (now Jobs → Contractor) — not changed here (not my folder).
- Run Unity only through the shared lock wrapper (see the lead handoff).
