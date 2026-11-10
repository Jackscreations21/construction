# Hand-off: polish-site — branch `claude/polish-site-8abb45`

## Fixed
- **Test isolation (`PlaythroughTests.Milestone1Playthrough` after `PadInLotTests`)**: a game bug. `Grade.SetHeights` on the 6% test slope
  put the lot surface ~0.78 m above the spawned player (0, 0, -4). The terrain colliders are one-sided, so the player fell out of the world
  and the next test's aim raycast hit nothing. New `Landscaping/GroundSafety.cs` (AfterSceneLoad, LateUpdate) lifts a player whose feet are
  >0.3 m under `Grade.HeightAt` (or below y -40) back onto the surface. Logged as `[GroundSafety] ...`. Regression test `SiteLookTests.PlayerStaysOnRaisedGrade`.

## Look
- **Terrain shader** `Assets/Groundbreak/Shaders/GroundbreakTerrain.shader` ("Groundbreak/Terrain", built-in surface shader, Standard lighting).
  Grass/Dirt/Gravel go through it via the `Materials.Custom` extension point (`SiteLook/SiteLook.cs`), and Topsoil/Sod/Sand/Straw via `LandMaterials`.
  - High-pass: divides out each texture's own 25-50 cm tone (that tone repeated every 2 m tile and was the "checker" at range).
  - Anti-tiling: a second rotated/scaled sample blended by world noise.
  - Macro variation: 3 rotated octaves of a generated tileable noise (`SiteLook.Macro`), ~7 / 33 / 150 m, tinting between two colours (dry/lush) and brightness.
  - Steep faces (trench/hole walls) use a world-projected texture (`_WorldTile`) instead of the smeared top-down UVs.
  - Properties are Standard-compatible (`_MainTex`, `_BumpMap`, `_Glossiness`...), so `mainTextureScale` copies like the bootstrap ground still work.
- **Atmosphere** (`SiteLook` component, `DefaultExecutionOrder(900)`, overrides GameClock/Weather each LateUpdate): sun colour warms below
  ~30° elevation, sun intensity ramps over -2..18° (GameClock's curve went dark too early), trilight ambient follows daylight,
  exp² haze (0.0042, rain 0.009) coloured to the sky horizon, skybox exposure/tint per daylight and rain. Shadows: 4 cascades, 120 m, VeryHigh, StableFit.
- **Far ground**: a grass ring from the 1000' bootstrap plane out to 1.2 km so the horizon dissolves into haze instead of a hard line.
- **Trench walls**: `SiteGround.Rebuild` renders grass cells steeper than ~1:2 as dirt (cut walls no longer keep grass).
- **Gate grain**: gate boards' cedar grain now runs vertically (cedar texture grain is along V); frame rails/brace along their length.
- `40_pad_in_graded_lot` shot moved closer.
- **Concrete**: broomed/troweled slabs on the terrain shader (metre-scale mottling, no tile repeat; broom keeps straight lines: `_AntiTile` 0),
  broom streaks/bump toned down, slab albedo 0.92 (was clipping in sun). `ConcreteTextures.cs`.
- **Dirt**: Worley clod relief cut from 0.25 to 0.07 in `ProceduralTextures.Dirt` (it read as cobblestones). Small edit to a shared Visuals file.
- **Marking paint**: soft alpha-blended spray blots with overspray (`Concrete/SprayPaint.cs`), instead of solid white polygons.

## Status
- Branch `claude/polish-site-8abb45`. Full PlayMode suite **35/35** (33 + 2 new `SiteLookTests`); 0 compile errors. Shots reviewed:
  `look_01..06`, `40/41`, `26`, `28`, trench close-ups.
- Known: `look_06_dig_dust` showed no particles before `cullingMode = AlwaysSimulate` was added; not re-checked after that change.

## Feel
- `SiteLook/SiteFx.cs`: pooled world-space particles. `SiteFx.Dig(p)`, `Gravel(p)`, `Concrete(p)`, `Burst(...)`.
  Hooked into the pad shovel (dig/backfill), mini excavator (dig/gravel), grading shovel (dig/fill), and the chute discharge (slurry spatter).

## Shared-file edits
None in Core. `Visuals/ProceduralTextures.cs` (Dirt clod relief, 2 numbers). In my area: `Concrete/SiteGround.cs`, `Concrete/Excavation.cs`, `Concrete/Pour.cs` (1-line hooks), `Landscaping/*`.

## Ideas not done
- Colour grading / tonemapping pass (would need a CommandBuffer on every camera; built-in pipeline has no post stack here).
- Grass blades / tufts near the camera (geometry or a billboard layer) for a 3D lawn edge.
- Wet-concrete sheen and bleed water; tire ruts and footprints in dirt.
