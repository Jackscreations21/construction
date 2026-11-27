# Photo mode, job photos and the Portfolio (`Scripts/Photo/`)

## For the player
- **P** anywhere (not while the tablet or a menu is open) opens **photo mode**: the game freezes, the HUD disappears and the
  camera flies free.
  - **WASD** fly, **Q / E** down / up, **Shift** fast, **Ctrl** slow, **hold right mouse** to look, **scroll** zoom,
    **[ ]** roll, **F** focus on what's in the middle, **H** hide the panel, **Enter** (or the button) takes the photo,
    **P / Esc** leaves.
  - Panel: lens (shown as a real focal length, 12 mm – 170 mm full-frame equivalent), roll, **time of day** (5 AM – 9 PM;
    the clock goes back to the real time when you leave), exposure, vignette, **depth of field** (aperture f/1.4 – f/16 +
    focus distance), flash, 7 filters (natural, black & white, sepia, warm, cool, vivid, faded film) and 5 frames
    (instant print, gallery mat, 2.39:1 cinema bars, site corner marks). Rule-of-thirds guides on screen (not in the photo).
  - Photos are 1920x1080 PNGs saved in `%USERPROFILE%\AppData\LocalLow\<company>\<product>\Photos\` (Unity's
    `Application.persistentDataPath`), never inside the project.
- **Automatic job photos** (Career): when you accept a job and first get to it (close enough, with nothing in the way), a
  **before** photo is taken from where you stand looking at the work. When the job is finished (invoice sent / job
  completed) an **after** photo is taken from exactly the same camera. House builds start with the empty lot photographed
  from the street and add a **progress** photo each time a build phase finishes. Repair jobs use a small flash (they are
  usually indoors).
- **Portfolio** tablet app (Business group): every job with before/after **side by side** or as a **wipe slider**, progress
  photos, dates, address, client, what the customer paid and their **rating** (out of 5); your own photos in **My photos**;
  **Favourites**; delete (asks first).
- **Bonus (modest)**: reputation **+1** when 5, 15 and 30 jobs have both a before and an after photo (+3 at most).
- **Office**: your three newest favourites stand in 8x10 frames on the office desk at the company premises (rented bay,
  yard shop or HQ).

## For code
| File | What |
|---|---|
| `PhotoMode.cs` | `PhotoMode.I` (on the `PhotoSystems` object): `Enter()`, `Exit()`, `TakePhoto()`, `AutoFocus()`, public `Position/Yaw/Pitch/Hours/S`. Sets `Game.UIOpen` and `Time.timeScale = 0`, restores both (and `GameClock.Hours`) on exit. |
| `PhotoRender.cs` | `PhotoSettings`; `PhotoRender.Capture(pos, rot, settings, w, h)` → Texture2D. Depth of field = average of 32 views from points across the real aperture (focal length from FOV on a 24 mm-tall sensor ÷ f-number) with an off-axis projection that keeps the focus plane fixed. Filter/vignette/frame in `Resources/GroundbreakPhoto.shader` (`Hidden/Groundbreak/Photo`). The first-person tool layer (30) is never photographed. |
| `JobPhotos.cs` | Hooks `Company.JobAccepted/JobCompleted` and `Projects.PhaseCompleted`. `TargetOf(job)` (repair defects → contractor job lot → house lot → `Job.WorldPos`), `CheckArrival(job)`, `CaptureProgress(album, caption)`, `ExternalTarget` hook for other features. Pay is read back from the wallet log line `Company.Complete` writes; rating from the repair invoice's satisfaction, the house's quality, or paid ÷ quoted. |
| `PhotoStore.cs` | `PhotoEntry`, `JobAlbum`, index saved under SaveSystem key **`photos`**; `Add/Delete/DeleteAlbum/ToggleFavourite`, `CheckBonus()`, `RootOverride` for tests. `PhotoThumbs` caches loaded PNGs. |
| `PortfolioApp.cs` | the tablet app. `PortfolioApp.Show(tab, slider)` for screenshots. |
| `OfficeFrames.cs` | desk frames at `YardSite.I.OfficeDesk`. |

Loading a save replaces the index; PNG files of other careers stay on disk (the player can delete them in the app).

## Tests (`Assets/Groundbreak/Tests/PhotoTests.cs`)
Build site at x = 760, z = -760. Every test writes into its own folder under `persistentDataPath` and deletes it; a
`[SetUpFixture]` also points the whole run's automatic job photos (taken while other suites play jobs) at
`Photos-testrun`, deleted at the end. Screenshots: `photo_mode_capture`, `photo_mode_dof`, `photo_mode_bw_instant`,
`photo_mode_sepia_mat`, `photo_repair_before/after`, `photo_before_after_repair`, `photo_before_after_build`,
`photo_office_desk_frames`; windowed run only (`-testFilter Groundbreak.Tests.PhotoTests` without `-batchmode`, see
`Docs/UI-CAPTURE.md`): `photo_mode_ui`, `photo_portfolio_app`, `photo_portfolio_slider`, `photo_portfolio_album`.

## Shared files touched
None. Everything is in `Scripts/Photo/` (+ its `Resources/` shader) and `Tests/PhotoTests.cs`; it uses the existing
extension points (`Tablet.RegisterApp/SetAppGroup/AddHelp`, `SaveSystem.RegisterExtension`, `Company` / `Projects` events).
