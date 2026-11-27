# Plan Designer — draw your own house or building

Code: `Assets/Groundbreak/Scripts/PlanDesigner/`. Tests: `Assets/Groundbreak/Tests/PlanDesignerTests.cs` (shots `Logs/shots/designer_*.png`).

## For the player
Tablet → **Plans → Plan Designer**.
- **Start**: a blank lot (house 40x30, garage/shop 24x24, store 50x60, warehouse 60x80), or **Copy & edit** any library plan (the library plan is untouched), or open one of **My plans**.
- **Tools** (mouse): Select / move wall (click a room to select + rename it, drag an inside wall to slide it) · Footprint (drag the back-right corner) ·
  Draw wall (drag a line across rooms: splits them) · Room box (drag a box, named from the list on the right) · Name room · Door · Window · Garage door ·
  Stair (drag a box; adds the floor above if needed) · Erase (door/window, stair, or the wall between two rooms = join them).
- Floors: `Floor 1/2/3`, `+ Floor`, `− Floor`. Building: type (Residential / Outbuilding / Commercial / Industrial), walls (wood 2x6/2x4, post-frame, steel PEMB, CMU),
  foundation, roof shape + covering + pitch, wall height, finish level, auto structure (joists/girders sized for the span).
- Everything snaps to Game.Snap's grid (G cycles it; the designer never goes finer than 6"); lengths show in feet/inches (`Units.Format`).
  Walls are straight across or front-to-back only: plan rooms are rectangles (the 45° angle step isn't used here).
- **Undo / Redo** buttons or Ctrl+Z / Ctrl+Y.
- Live: sq ft, beds/baths, garage, estimated build cost from the takeoff ($ and $/sq ft), crew days, and the plan checker's findings in plain language
  (room unreachable, bedroom needs an egress window, bath too narrow, stairs missing/too short, span too long, no way in...). Problem rooms are tinted red;
  click a finding to jump to its floor and flash it. Doors/windows the plan rules add automatically are drawn lighter; the ones you place are solid.
- **Save** → My plans. A plan with no problems is registered as a real plan (`custom-*`): it appears in the Plans app under **My plans**, can be shown as the
  on-site overlay, is offered as a spec-house plan in the Development app (residential ≤ 60' wide), and **Find a client** puts a client contract for it on the
  job board (Career, Builder stage). Crews build it like any plan. A plan with problems is kept as a draft until fixed.
- Rename / Delete from the start screen. A plan used by a project or job can't be deleted or overwritten (Save as new instead).

## For code
- `PlanDesign` (serializable data): footprint, rooms (`DesignRoom`), placed openings (`DesignOpening`: exterior side/pos like `Blueprint.Door`, interior x/y like `DoorAt`),
  suppressed auto openings, structure/roof/foundation, group. `PlanDesign.Build()` → `CustomPlan : Blueprint` (so walls, auto windows/doors, stairs, MEP, code checks and
  takeoff are the library's own). `PlanDesign.From(plan)` copies any plan (rooms + every opening + structure).
- `DesignEditor`: `SetFootprint`, `DrawWall`, `MoveWall`, `EraseWall`, `AddRoom` (carve), `NameRoom`, `RenameRoom`, `PlaceOpening`, `RemoveOpeningAt`, `EraseAt`,
  `AddStair`, `AddFloor`, `RemoveTopFloor`, `Set(change)`, `Undo/Redo`; `Plan`, `Issues` (`DesignIssue`: text, error, floor, highlight rect), `ErrorCount`, `StatsLine`.
- `DesignChecks.Run`: `BlueprintValidator` findings rewritten in plain language + designer tips (unnamed rooms, a closed room in two parts, no kitchen/bath/bedroom).
- `PlanDesigner`: `Saved`, `Save(editor, asNew)`, `Rename`, `Delete`, `InUse`, `PlanOf(id)`, `OfferToClient(plan)` (clones the job catalog's build def with the custom
  plan id → `JobCatalog.CreateBuild`), `ShowOverlay`. Persistence: save extension `"plan-designer"` (registered at SubsystemRegistration so it loads before
  extensions whose projects refer to custom plan ids). `LoadJson(null)` clears and unregisters.
- `PlanPicture`: the blueprint drawing (shared by the canvas and `Render(plan, floor, w, h)` → Texture2D, used for My-plans thumbnails and test pictures).
- Ids: `custom-<slug>[-n]`, category `my-plan`.

## Shared files touched (small, additive)
- `Development/HousePlan.cs`: `HousePlans.Unregister(id)`.
- `Development/DevApps.cs`: Plans app group toolbar gets "My plans" (`InGroup` helper; filter `custom-*`).
- `UI/Tablet.cs`: the T hotkey doesn't close the tablet while a text field has keyboard focus (typing a plan name with a "t").

## Limits / ideas
- Rooms are rectangles (no L-shaped rooms, no angled walls); footprints are rectangles (no L-shaped houses).
- Auto rules still add windows/doors; the player can switch individual ones off (Erase) or place their own.
- Ideas: L-shaped footprints, a 3D preview of the shell, a plan library that carries across save games, sharing plans as text.
