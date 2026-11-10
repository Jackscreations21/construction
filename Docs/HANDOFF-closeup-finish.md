# Milestone 5 — close-up and finish (branch `claude/closeup-finish-2a5558`)

Built by the lead closeup-finish session (shared framework + integration) and three trade agents in worktrees whose branches are merged here:
`worktree-agent-a4a3f6485d0d4c045` (insulation/drywall/paint), `worktree-agent-aa71295fbd846a693` (exterior), `worktree-agent-a6c44e167bc35aaca` (interior finish carpentry).
main (M2 concrete, M4 MEP, M3 framing) is merged in. M6 is merged too. CloseupBridge registers the close-up rules and `InspectionBridgeTests` passes.

## Folders
- `Scripts/Finish/Core/`: shared framework (lead).
- `Scripts/Insulation/`, `Scripts/Drywall/` (paint is in `Drywall/Paint.cs`).
- `Scripts/Exterior/`.
- `Scripts/Finish/{Interior,Trim,Doors,Flooring,Cabinets}/`.
- Tests:
  - `Tests/FinishTestKit.cs`: real framing builders, `Shot`, and `CleanScene` for `[UnityTearDown]`.
  - `FinishCoreTests`, `InsulationDrywallTests`, `ExteriorTests`, `InteriorFinishTests`.
  - **`CloseupIntegrationTests`**: insulate → drywall (ceiling first, tape, 3 coats with dry time, sand, texture, prime, 2 paint coats) → hang and case a door + base → wrap + vinyl on one exterior wall, wrap + brick on the other. All four inspection phases must pass. Screenshots `Logs/shots/m5_*.png`.

## Shared framework (Scripts/Finish/Core)
- **`FramePlane`**: finds the coplanar framing face from a hit. It works with any Piece layout.
  - Coordinates and geometry: u,v plane, N/U/V, `Rotation`.
  - Framing queries: `Bays()`, `Openings()`, `Obstacles()` (boxes and pipes with `Proud`), `FramingLinesU/V`, `SnapToFramingU`.
  - Exclusions: lumber end grain and sheet edges are ignored, and so are `SiteGround` and other huge colliders.
  - Hooks: `ObstacleSources` for obstacles without colliders. `ExcludeMember` is a single delegate that the exterior code chains.
- **`FinishMaterials`**: a string-keyed procedural material registry (`FinishCanvas`, `FinishNoise`). No `SurfaceKind` edits.
- **Mesh and aiming helpers**: `PanelMesh` (slabs with holes), `FinishLayer` marker, `FinishAim`.
- **`CloseupTool`**: tool base. It was renamed from FinishTool because main's concrete code has a FinishTool.
- **Stock, time and supply**: `FinishStock` (Take/Give/Owns/Require/Item/Unlimited), `FinishTime` (Now/Skip), and the `FinishSupply` "Finish Supply" tablet app.
- **`CloseupInspection`**: rules as data for the phases Insulation / Drywall / Exterior / Finish. `Run(phase)` drops red flag markers, and there is a "Close-up Inspections" tablet app.
- **`CaulkGun`** (id `caulk-gun`): per-trade tubes via `RegisterTube`, drawn as `Bead` visuals.
- **`FinishHooks.CoveringThickness`**: drywall and exterior layers report their thickness so trim and cabinets sit on the finished face.

## Trade APIs
Exact signatures, tool ids, catalog ids and rule ids are in the side-session notes folder (`closeup-interior-api.md`, `closeup-exterior-api.md`, `closeup-finish-api.md`). Short form:
- **Insulation**: `Insulation.FaceFrom(point, dir)` (aim at a stud, not into an open bay), `InsulateWall(fp, "ins-r13-k15")`, `AirSeal(fp)`, `BlowAttic`, `InstallBaffle`, `InstallPoly`. Targets live in the `InsulationRules` data.
- **Drywall and paint**: `DrywallJob.Complete(fp, "dw-12-4x12", "orange-peel", "Linen", Sheen.Eggshell)`, or step by step: `Drywall.HangSurface` → `CutOutBoxes` → `ScrewAll` → `CoatAll(s,1..3)` + `FinishTime.Skip(24.1)` → `SandAll` → `ApplyTexture` → `PaintJob.Prime/Paint`. Hang the ceiling first.
- **Exterior**: `ExteriorWall.FromHit(hitOnSheathing)` → `HouseWrap.WrapWall` → `VinylSiding.InstallAll(w,"White")` / `BrickVeneer.LayAll(w)` / `FiberCement.InstallAll` / `BoardBatten.InstallAll`, plus `ExteriorTrim.CaulkAll`, `Eaves.For(fascia)`, and `DeckBuilder.Plan` / `BuildAll`.
- **Interior finish**: `DoorHanging.Hang(face, face.Openings()[0], "door-int-6p32")` → `Casing.CaseDoor(door)` → `WallTrim.RunBase` / `RunCrown` → `NailAll` / `CaulkAll`. Also `Lvp.Install`, `Hardwood.Install`, `TileWork.InstallAll`, `Cabinets.InstallRun`, `Countertops.Install` / `InstallBacksplash`.

## Shared-file edits
- No `Scripts/Core` or `Visuals` files were changed.
- Tool ids were checked against main: the exterior snips are `ext-snips` because MEP owns `tin-snips`. The house-wrap stapler is `cap-stapler`; roofing has `capstapler` for underlayment.

## Open items / next steps
1. **Pre-existing on main (not M5):** `PadInLotTests` followed by `PlaythroughTests.Milestone1Playthrough` makes the playthrough fail ("aim at a surface"). It reproduces with only those two classes. Everything else passes: 32/33 in the full suite, and all 10 close-up tests plus the playthrough pass together. Vinyl banding is fixed (float tolerance in the lap profile, `68abc4c`).
2. **Integration with site-career and framing:**
   - Wrap the `CloseupInspection` phases in site-career's `Inspections.Register(...)` (see `site-career-APIs.md`).
   - Use `RoughOpening.All` from framing, set `.Flashed`/`.Taped` when HouseWrap flashes an opening, and use `WallAssembly.All` for real walls.
3. **Minor visual polish:**
   - Hardwood still reads a bit orange in direct sun.
   - Knockdown ceiling texture is fairly bold.
   - Eave baffles look odd without a roof.
   - A sliver of the ProductModels door casing shows inside the jamb.
4. **Small Core wishes:**
   - Move `FaceAim.Front` (Insulation) into Core so every tool picks the stud face when aiming into an open bay.
   - Make `FramePlane.ExcludeMember` a list instead of a single delegate.
