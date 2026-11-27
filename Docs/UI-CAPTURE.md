# Capturing the real HUD and tablet (IMGUI)

The HUD, Tab tool list, tablet and main menu are IMGUI (`OnGUI`). **OnGUI never runs in `-batchmode`**, so the normal
test run can't photograph them. Capture them with a short **windowed** editor run (same command, minus `-batchmode`).
An editor window opens on screen for 1-5 minutes and closes by itself; don't click in it while it runs.

## UI shots (`Logs/shots/ui_*.png`)
Test: `Assets/Groundbreak/Tests/UICaptureTests.cs` (category `UICapture`, ignored in batchmode).

```bash
"C:/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Unity.exe" -projectPath <worktree> -runTests -testPlatform PlayMode \
  -testFilter Groundbreak.Tests.UICaptureTests -testResults <worktree>/Logs/ui.xml -logFile <worktree>/Logs/ui.log
```
About 1 minute after the import. Output:
- `ui_hud_belt_toast`, `ui_hud`, `ui_hud_place` (Hands/Place status line), `ui_crosshair` (4x zoom of the center)
- `ui_tool_list` (Tab held; the test sets `ToolBelt._showList` by reflection between Update and OnGUI)
- `ui_tablet_home` and `ui_tablet_<app>` for **every registered app** (names come from `Tablet.AppNames()`, so new apps are picked up automatically)
- `ui_main_menu`

How it works: the Game view is set to 1600x900 (`PlayModeWindow.SetCustomRenderingResolution` via reflection),
then each shot waits for `WaitForEndOfFrame` (after OnGUI) and reads the screen with `ScreenCapture.CaptureScreenshotAsTexture`.

## Tutorial pictures
The HUD/tablet tutorial pictures (gs_01..07, gs_09, c_10, f_00, r_00, s_01, biz_*, and `Logs/shots/tut_app_*`) come from
`TutorialCaptureTests` and need the same windowed run (see `Docs/TUTORIALS.md`). Both at once (~4 minutes):
```bash
... -testFilter "Groundbreak.Tests.UICaptureTests;Groundbreak.Tests.TutorialCaptureTests" ...
```
Set `GROUNDBREAK_CAPTURE_TUTORIALS=1` for that run (otherwise tutorial pictures go to `Logs/shots/tut_pics/`), then commit the changed PNGs under `Assets/Groundbreak/Resources/Tutorials/`.

## Notes
- One Unity per project folder: close any editor open on the worktree first.
- Look at every PNG after UI changes; the batchmode suite cannot catch clipped or overlapping IMGUI.
- Tablet layout: header, grouped sidebar (Home + groups Buy / Plans / Site office / More / Learn) and a Home icon grid.
  New apps: `Tablet.RegisterApp(name, draw)`, optionally `Tablet.SetAppGroup(name, "Business")` to put it in its own group
  (otherwise it lands under "More"). Size wide content with `Tablet.I.ContentWidth` (px), not `Screen.width`.
