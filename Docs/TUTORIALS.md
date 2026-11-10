# Tutorials app (tablet) — how it works and what's left

Branch `claude/tutorials`. Code: `Assets/Groundbreak/Scripts/Tutorials/`, pictures: `Assets/Groundbreak/Resources/Tutorials/*.png`,
capture test: `Assets/Groundbreak/Tests/TutorialCaptureTests.cs`.

## Pieces
- `TutorialData.cs` — `Tutorial` (Id, Phase, Title, Summary, Available, ComingWith, Steps), `TutorialStep`
  (Title, Text, Keys, Picture, OpenApp, Find, Where), registry `Tutorials` (PhaseOrder, All, Register/Replace/Get, Progress),
  `TutorialPictures.Get(name)` (Resources.Load, with an editor fallback that reads the PNG straight off disk).
- `TutorialContent.cs` — all step text, in real construction order, plus the "show me where" locators.
- `TutorialApp.cs` — the IMGUI tablet app "Tutorials" (list grouped by phase → step view with a large picture,
  text, keys line, progress bar, Prev/Next, "Open <app>" and "Show me where") and `TutorialBeacon` (orange marker + on-screen pointer).
- Shared-file edit (small, additive): `Tablet.ShowApp(name)` and `Tablet.CurrentApp`.

## Capturing pictures
- 3D pictures (`Pic(...)`, `CopyShot(...)`) are rendered by a test camera and work in the normal batchmode test run.
- Pictures that show the real HUD / tablet (`GuiPic(...)`: gs_01..07, gs_09, c_10, f_00, r_00, s_01, and the app screenshots
  `Logs/shots/tut_app_*.png`) need IMGUI, and **OnGUI never runs in batchmode**. Capture them with one run WITHOUT `-batchmode`
  (an editor window opens for a few minutes; the test sets the Game view to 1600x900 and uses `ScreenCapture`):
  ```bash
  "C:/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Unity.exe" -projectPath <worktree> -runTests -testPlatform PlayMode -testFilter Groundbreak.Tests.TutorialCaptureTests -testResults <worktree>/Logs/tut.xml -logFile <worktree>/Logs/tut.log
  ```
  In batchmode those steps are skipped and the test only asserts the PNG exists.
- The MEP pictures are copies of the plumbing / electrical / HVAC tests' own shots (`E_Mep` reruns those tests).
- In the editor, `TutorialPictures.Get` reads the PNG from disk first, so new captures show before Unity re-imports them.

## Adding a trade's tutorial later
From your module (BeforeSceneLoad) or by editing `TutorialContent.cs`:
```csharp
Tutorials.New("fin-drywall", "Close-up & finish", "Drywall", "Hang, tape, mud and sand.")
    .Step("Hang", "why + what", "exact keys/tools", "fin_03_hang", openApp: null, find: () => somePoint, where: "the drywall stack");
```
A real (Available) tutorial is never overwritten by a placeholder with the same id. Then add the shot to `TutorialCaptureTests`
(stage it with the real systems, `Pic(pos, look, "fin_03_hang")`, or `CopyShot("<trade test shot>", "fin_03_hang")`)
and flip `available` to true (drop the `false, "..."` arguments in `TutorialContent.Later()`).
`Z_TutorialsApp` fails if an Available tutorial references a picture that doesn't exist.

## Status
All milestones (M1-M6) are on main and merged here: every tutorial is Available and every step has a real picture
(26 tutorials, 98 pictures). The M5/M6 pictures are copies of those trades' own test shots (`F_CloseupAndSite`), plus real
IMGUI captures of the Inspections, Garden Center and Job Board apps and the main menu.
- `CopyShot` keeps the committed picture when the trade test didn't run in this session (filtered run).
- `F_CloseupAndSite` switches to Career mode just for the Job Board capture (the board is empty in Free Build).
- In the non-batchmode capture run, `SetUp` hides M6's boot main menu.
- Known on main (not this branch): `PlaythroughTests.Milestone1Playthrough` fails when it runs after `PadInLotTests`.
