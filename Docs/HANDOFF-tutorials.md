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

## Also done
- Old Career "Job Board" retired: `IntegrationRuntime.SetupTablet` shows it only while a legacy contract is active
  (`Career.Active != null`); the `biz-career` tutorial step and `TutorialCaptureTests.F_CloseupAndSite` now use the Jobs app
  for `biz_04_career` (the committed picture is still the old Job Board shot until the windowed run below).
- main (aa6ed35, map-followup) is merged.

## Left to do (paused for the night at commit below)
1. Batchmode compile check passed (0 `error CS`) after the Job Board change. Redo it before the windowed run if code changes again.
   The windowed run was stopped part-way for the night: 15 of 25 tablet pictures are committed but NOT looked at yet; rerun step 2 for all of them.
2. ONE windowed capture run (through `unity-run.sh`, only after step 1 is clean), with `GROUNDBREAK_CAPTURE_TUTORIALS=1`:
   `-testFilter "Groundbreak.Tests.BusinessTutorialTests;Groundbreak.Tests.TutorialCaptureTests.F_CloseupAndSite"`.
   This creates the 25 tablet pictures (bt_01, 02, 05, 07, 11, 13, 21, 30, 32, 40, 50-52, 60-63, 70-72, 80, 90, 92, 94, A1),
   the new biz_04_career, and `Logs/shots/tut_business_*.png`. Afterwards `git checkout` any OTHER trade PNG F_ rewrote (keep only biz_04_career).
3. LOOK at every bt_*.png (contact sheet). The Company / Plans scroll offsets in `B_CaptureBusinessCareer` (380 / 640 / 900;
   420 / 1100) are guesses: adjust if the daily costs / P&L / loans / rooms / takeoff aren't in view, then recapture.
   World pictures (20) were already checked and committed.
4. Commit the pictures, merge main again, ONE full PlayMode suite (batchmode, via the wrapper), check `git status` is clean after it.
5. SendMessage the lead "tutorials DONE @ <commit>" with branch and test counts.

## Notes
- The Builder app no longer exists (it is Jobs → Houses); the tutorials point to Jobs tabs accordingly.
- Re-roof job text in `ContractorJobs.cs` still says "Contractor Jobs app" (now Jobs → Contractor) — not changed here (not my folder).
- Run Unity only through the shared lock wrapper (see the lead handoff).
