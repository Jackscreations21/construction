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

## TODO: pictures to capture once these branches merge
Placeholder tutorials (Available = false, blueprint placeholder picture) — capture these PNGs and set them Available:

| Tutorial id | Picture | What it should show | Branch |
|---|---|---|---|
| insp-schedule | i_03_schedule | Inspections app with the phase list / next inspection scheduled | site-career (M6) |
| insp-schedule | i_04_failed | A failed inspection: red markers in the building + the issue list | site-career (M6) |
| fin-insulation | fin_01_batts | Wall bays filled with R-13/R-21 batts, split around a cable/pipe | close-up (M5) |
| fin-insulation | fin_02_attic | Attic: eave baffles + blown-in insulation to the depth rulers | close-up (M5) |
| fin-drywall | fin_03_hang | Ceiling and wall sheets hung, staggered joints, screws | close-up (M5) |
| fin-drywall | fin_04_mud | Taped and mudded seams, corner bead | close-up (M5) |
| fin-siding | fin_05_housewrap | Housewrap lapped and taped, folded into an opening | close-up (M5) |
| fin-siding | fin_06_siding | Siding courses, corner posts, J-channel at a window | close-up (M5) |
| fin-interior | fin_07_door | Prehung interior door with casing (`DoorHanging.Hang` + `Casing.CaseDoor`) | finish (M5) |
| fin-interior | fin_08_cabinets | Cabinet run + countertop + backsplash (`Cabinets.InstallRun`, `Countertops`) | finish (M5) |
| fin-interior | fin_09_flooring | Tile / hardwood / LVP floor (`TileWork`, `Hardwood`, `Lvp`) | finish (M5) |
| biz-landscape | biz_01_grade | Final grade sloping away from the foundation | site-career (M6) |
| biz-landscape | biz_02_lawn | Lawn / sod / plants around the house | site-career (M6) |
| biz-save | biz_03_save | Save/Load app on the tablet (IMGUI capture: `GuiPic(..., tablet: true)`) | site-career (M6) |
| biz-career | biz_04_career | Career app: lot / contract / budget (IMGUI capture) | site-career (M6) |

Also re-check the step text against the merged code (keys, tool names) when those branches land; the text was written from their API notes.
Once M6 lands, the "Inspections" tutorial can point at the Inspections app with `openApp`.
