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

## Final status (2026-11-27): DONE
- main c1d7e79 merged. Full PlayMode suite (batchmode, via unity-run.sh): 131 tests, 129 passed, 0 failed, 2 skipped
  (windowed-only UICapture tests), 0 `error CS`. No photo folders left under persistentDataPath after the run.
- Windowed `PhotoTests.Capture_PhotoModeUI_AndPortfolioApp` passed; `photo_mode_ui.png` checked: panel fits its column.
- All screenshots looked at (photo_mode_*, photo_before_after_repair/build, photo_office_desk_frames, photo_portfolio_*).
