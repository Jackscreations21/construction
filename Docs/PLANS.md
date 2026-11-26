# House plan format (`Scripts/Development/HousePlan.cs`)

Plans are data. A plan lists its rooms; walls, openings and the material takeoff are derived from them.

## Coordinates
Plan feet. **x** runs left → right as seen from the street, **y** runs back from the front wall (y = 0 is the front).
`PlanPlacement.On(lot, plan, frontSetbackFt = 25)` centers the plan on the lot frontage and maps plan feet to world
(`World(xFt, yFt, upM)`).

## Adding a plan
```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void AddMyPlans() => HousePlans.Register(
    new HousePlan { Id = "saltbox-1400", Name = "The Hemlock (saltbox)", Style = "Saltbox", WidthFt = 36, DepthFt = 28,
                    Floors = 1, Beds = 3, Baths2 = 4, WallHeightFt = 8, Pitch = 8, Roof = RoofType.Gable,
                    Foundation = FoundationType.Crawlspace, StudSize = "2x6", FrontDoorFt = 12, BaseQuality = 0.6f }
        .Room("Living", 0, 0, 20, 14).Room("Kitchen", 20, 0, 36, 14)
        .Room("Bed 1", 0, 14, 12, 28).Room("Bath", 12, 14, 18, 28).Room("Bed 2", 18, 14, 27, 28).Room("Bed 3", 27, 14, 36, 28));
```
`Register` calls `Finish()` (derives walls + openings); the same `Id` replaces an existing plan.

### Fields
| Field | Meaning |
|---|---|
| `WidthFt`, `DepthFt` | rectangular footprint (outside of framing) |
| `Floors` | stories; rooms take a `floor` argument (0 = ground) |
| `WallHeightFt`, `StudSize` | 8/9 ft; `"2x4"` or `"2x6"` exterior walls (partitions are 2x4) |
| `Pitch`, `Roof` | rise per 12; `Gable` (ridge along x) or `Hip` |
| `Foundation` | `Slab` (footing + 4" slab), `Crawlspace` (footing + 32" stem wall), `Basement` (24" footing, 8' wall, slab) |
| `Beds`, `Baths2` | counts; `Baths2` is half-baths x2 (5 = 2.5 baths) |
| `FrontDoorFt` | front door center along the front wall |
| `BaseQuality` | 0..1 finish level the plan is drawn for (spec houses can override) |

### Rules
- Rooms must **tile** the footprint per floor (no gaps/overlaps). Interior walls are room edges not on the footprint boundary,
  merged where collinear. Exterior walls are always indices 0 front, 1 right, 2 back, 3 left per floor (counter-clockwise).
- Openings: the front door (`door-ext-fg36`, ground floor); a window centered on every room edge on an exterior wall
  that is ≥ 6' long (bath/laundry/powder 24x36, kitchen 48x36 slider, living/great/family 36x60, others 36x48); one interior
  door per private room (bed/master/bath/powder/laundry/study/mud) on its longest interior edge that opens onto a shared room
  (24" for baths, 30" otherwise). Openings closer than half their widths + 1' are skipped.
- Room names drive the rules: start bedrooms with "Bed" or "Master"; baths contain "Bath".
- Need a custom opening? Add to `plan.Openings` after `Finish()` (`Wall` = index in `plan.Walls`, `CenterFt` along that wall).

## What reads a plan
`Takeoff` (Plans app, costs), `PlanOverlay` (on-site wall/opening lines), `DevVisuals.HouseModel` (off-site stage model),
`Projects.Measure` (progress vs. takeoff counts), `HousePlan.RequiredInspections` / `PhaseDays`.
