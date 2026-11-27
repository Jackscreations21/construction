# Handoff: photo mode + job gallery (branch `claude/angry-chebyshev-e44506`)

Feature doc: `Docs/PHOTO.md`. Code: `Assets/Groundbreak/Scripts/Photo/` (+ `Resources/GroundbreakPhoto.shader`).
Tests: `Assets/Groundbreak/Tests/PhotoTests.cs` (5 batchmode tests + 1 windowed `UICapture` test).

## State
- Photo mode (P): freeze (timeScale 0), HUD hidden (Game.UIOpen + full-screen viewfinder drawn in front of all IMGUI),
  free-fly camera, lens/FOV, roll, real depth of field (32 aperture samples, verified to soften detail), time-of-day
  slider (restored on exit), exposure, vignette, flash, 7 filters, 5 frames, 1920x1080 PNG under persistentDataPath/Photos.
- Automatic before (on arrival with line of sight) / after (on Company.JobCompleted, same camera) / progress
  (Projects.PhaseCompleted) for repairs, contractor jobs and house builds; pay read from the wallet log, rating from
  invoice satisfaction / house quality / paid÷quoted.
- Portfolio tablet app (Business group): side-by-side or wipe slider, progress strip, my photos, favourites, delete with
  confirm. Index saved as SaveSystem extension `photos`.
- Reputation +1 at 5 / 15 / 30 documented jobs (max +3).
- Favourites (newest 3) in 8x10 desk frames on `YardSite.I.OfficeDesk`.

## Shared files edited
None.

## Status when paused (2026-11-26, lead asked all sessions to pause)
- main aa6ed35 merged in. Full PlayMode suite (batchmode): 131 tests, 129 passed, 0 failed, 2 skipped (windowed-only
  UICapture tests), 0 `error CS`. No photo folders were left under persistentDataPath after the run.
- Screenshots looked at: photo_mode_capture / _dof / _bw_instant / _sepia_mat, photo_before_after_repair,
  photo_before_after_build, photo_office_desk_frames, and (windowed) photo_portfolio_app / _slider / _album, photo_mode_ui.

## What is left
1. One windowed run (through unity-run.sh) of `Groundbreak.Tests.PhotoTests.Capture_PhotoModeUI_AndPortfolioApp`, then LOOK
   at `Logs/shots/photo_mode_ui.png`: the photo-mode panel overflowed sideways in the first capture (labels and the 2-column
   filter/frame grids were clipped); commit 3feb653 gives every control a fixed column width. Not yet re-captured.
2. SendMessage the lead "photo-mode DONE @ <commit>" with branch `claude/angry-chebyshev-e44506` and the test counts above.

## Known / follow-ups
- A translucent red line (a gameplay guide object from another system, not on the view-model layer) showed up in one
  photo-mode capture; photo mode does not hide world-space gameplay helpers yet. Could add a hook to hide them.
- Repair "before" photos are taken from the player's eye height, so close-up sink/outlet shots look steeply down.
- Loading a different save keeps the other career's PNGs on disk (they can be deleted from the app).
- Test runs: a `[SetUpFixture]` in PhotoTests.cs redirects every automatic job photo taken during the whole run into
  `persistentDataPath/Photos-testrun` and deletes it at the end.
