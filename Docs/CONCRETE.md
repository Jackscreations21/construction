# concrete-forms (Milestone 2) — hand-over

Branch `claude/concrete-forms-e5b4e0`. All code in `Assets/Groundbreak/Scripts/Concrete/`, test `Assets/Groundbreak/Tests/ConcreteSlabTests.cs`.
PlayMode: 3/3 pass (existing 2 + `SlabFromLayoutToCure`). Shots: `Logs/shots/20_*`..`29_*`.

## Shared-file edits (small, additive)
- `Visuals/SurfaceKind.cs`: appended `WetConcrete, TroweledConcrete, BroomedConcrete, VaporBarrier` at the END.
- `Visuals/Materials.cs`: new `Materials.Custom` (Dictionary<SurfaceKind, Func<Material>>) checked in `Get(kind)`, and
  `Materials.CustomTileMeters` checked in `TileMeters`. Lets any feature supply materials for kinds it appends.
  Merge note: if another branch also appended SurfaceKinds, keep both blocks (order doesn't matter as long as they're after PaperBag).

## Runtime behavior other sessions should know
- `SiteGround` (diggable 60'x60' heightfield, 4" cells, x -30'..30', z 0'..60') HIDES the bootstrap "Ground" plane's renderer+collider
  and adds flat grass skirt quads with BoxColliders around it. Ground is still at y=0 everywhere until dug.
  API: `SiteGround.I`, `HeightAt(p)`, `GroundY(p)` (static), `DigTo/DigBy/RaiseTo/DigRect/RaiseRect`, `Compact`, `Cover[]`, `H[]`.
- Booted by `ConcreteSetup` (`AfterSceneLoad` creates `ConcreteSite`, which builds SiteGround in Start). No GameBootstrap edit.

## API for framing (sill plates)
`ConcreteElement` (MonoBehaviour on the slab, has a MeshCollider):
`All`, `event Created`, `TopY`, `Outline` (Vector2[] world XZ), `Contains(p)`, `At(p)`, `TryGetTop(p, out y)`,
`Cured` (>= 7 days), `Strength01`, `AnchorBolts` (List<Transform>, 1/2" J-bolts 1 3/4" in from the edge = centered on a 2x4 sill),
`Defects`, `ThicknessInches`, `Psi`.

## Not done / follow-ups
- Tools are logic-only (no first-person held models) except excavator, compactor, laser tripod, truck.
- Pump truck has no model (just unlimited reach). Wheelbarrow has no model.
- Excavator digs a round 12" swath, not a true bucket/arm IK.
- Forms are generated from an outline (corner clicks) rather than free-placed with the PlaceTool (PlacementController can't roll boards on edge).
- Spliced runs > 16' use cut boards; save/load not wired (SaveSystem.RegisterExtension from site-career would be the hook).

## Framing hook
`ConcreteSetup.HookFraming()` sets `SupportGraph.FoundationProbe` by reflection at AfterSceneLoad (no compile dependency on the framing branch). The pour GameObject is named "Concrete Slab" so framing's default name-based probe also works.
