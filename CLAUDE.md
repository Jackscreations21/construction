# Groundbreak — realistic first-person house-building game (Unity 6)

Spec (source of truth for features): https://claude.ai/artifact/GfRMwLRPPAxjUWXxt9LVe6 — read its trade-by-trade section for your area.
Code contracts: `Docs/CONTRACTS.md`.

## Engine & running
- Unity **6000.6.8f1**: `C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe`
- Built-in render pipeline, IMGUI (`OnGUI`) for UI, everything built from code by `GameBootstrap` (any empty scene + Play works).
- Run tests + screenshots (do NOT pass `-nographics`, cameras must render):
  `Unity.exe -batchmode -projectPath <worktree> -runTests -testPlatform PlayMode -testResults <worktree>\Logs\playmode.xml -logFile <worktree>\Logs\playmode.log`
  then `grep "error CS" Logs/playmode.log` and check `Logs/playmode.xml`. Screenshots land in `Logs/shots/` — LOOK at them after visual work.
- One Unity instance per project folder at a time (worktrees are separate folders, so parallel sessions are fine).

## Rules
- Namespace `Groundbreak`. Units: meters internally, show feet/inches via `Units.Format`. Imperial/US code values.
- All input through `GInput` (never `Input.*` / `Keyboard.current`). Ignore gameplay input when `Game.UIOpen`.
- Realism matters to the user: real dimensions, real sequence, real tools. Visuals: procedural textures via `Materials.Get(SurfaceKind)`;
  real-size meshes with UVs in **meters** via `MeshBuilder` / `ProductModels` — never stretch objects with transform scale.
- Snap everything to `Game.Snap` (grid + 45° angle steps).
- **Add, don't edit shared files.** Put your feature in its own folder `Assets/Groundbreak/Scripts/<Feature>/` and use the extension points:
  - `Catalog.Register(params PieceDefinition[])` — new materials/products (call from `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`)
  - `ToolFactory.Register(ToolShopItem, Func<Tool>)` — new tools (subclass `Tool`)
  - `Tablet.RegisterApp(name, Action draw)` / `Tablet.AddHelp(line)` — tablet apps (registering "Concrete Dispatch" replaces the placeholder)
  - New `SurfaceKind` values: append at the END of the enum only, and add their generator/material in your own partial-free helper if possible; keep edits to `ProceduralTextures.cs`/`Materials.cs` minimal and additive.
  If you must change a shared Core file, keep the change small and additive (new members, no renames) and say so in your final report.
- Add PlayMode tests for your feature under `Assets/Groundbreak/Tests/` (own file), including at least one screenshot of the result.
- Commit on your own branch with clear messages. Don't push to `main`; the coordinating session merges branches.
