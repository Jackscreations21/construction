# Handoff: full-map — branch claude/full-map-ee35f7 (API + layout in Docs/MAP.md)

## State: done
- `Scripts/Map/`: MapLayout (10 districts, ~20 roads, map lots registered as TownLots via `Town.ExtendLayout`), MapRoads,
  MapWorld (100 m cells, MapStreamer, blocking grid), MapMaterials/MapTextures, and the model library in `Models/`:
  Residential (9 types), Commercial (16), RuralIndustrial (11: warehouse, metalshop, lumberyard, concreteplant, selfstorage,
  contractoryard, watertower, barn, silo, grainbins, machineshed), Props (24 + Wire/Fence), MapText, MapTrees.
- `Tests/FullMapTests.cs` (5 tests, 26 screenshots `Logs/shots/map_*.png`) pass; full PlayMode suite: see the commit message / report.
- Build stats (test machine): 318 cells, ~1,020 renderers, ~3.2 M triangles in total (only cells within 460 m are drawn),
  ~6,600 colliders, ~260 map buildings, ~5,900 trees, built in ~2-3 s.

## Shared-file edits (all additive)
- `Town/Town.cs`: TownLot.External/District/Category/TypeId/PlanId/ServiceCalls; TownStreet.HalfWidth/OpenMin/OpenMax/External;
  Town.ExtendLayout/AddLot/AddStreet/AllStreets/CoreLots; NearestRoadPoint/OnRoad use all streets.
- `Town/TownBuilder.cs`: iterates CoreLots; no barricade at open street ends.
- `Town/RepairJobs.cs`: only lots with ServiceCalls get repair offers (map buildings without an enterable house don't).
- `Tests/TownTests.cs`: core-only lot counts.
- `Integration/LotBroker.cs` (SyncYard): prefers core lots over map lots for the yard (warehouse stays on main-301).

## Notes / follow-ups
- Bulk house fill skips any lot that would overlap another lot or a right-of-way (`MapLayout.Fits`); explicit landmark lots don't.
- Hills only rise > 30 m outside `MapLayout.Bounds`; power poles face along the road (crossarm across the line).
- Gas stations park between the middle pump islands. Plan ids such as `duplex-2x1100` resolve to null until someone registers them.
- Possible tuning: triangle budget (fields' 3D crop rows and trees dominate), more props in the dev tracts.
