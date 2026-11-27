# Blueprint designer (Plan Designer) — handoff
1. Done: tablet app "Plan Designer" (Plans group) in `Scripts/PlanDesigner/`: start from a blank lot / copy any library plan / My plans; mouse tools
   (select + move wall, footprint, draw wall, room box, name room, door, window, garage door, stair, erase), floors 1-3, building systems
   (type, walls, foundation, roof + covering + pitch, wall height, finish, auto structure), undo/redo, live sq ft / beds / baths / cost / crew days,
   validator findings in plain language highlighted on the drawing. Saved plans persist (save extension `plan-designer`), register as `custom-*`
   (category `my-plan`) when they pass the validator, show in the Plans app under "My plans", work with the overlay, the Development app's spec list,
   `PlanDesigner.OfferToClient` (client contract via `JobCatalog.CreateBuild`) and crew building. See `Docs/PLAN-DESIGNER.md`.
2. Tests: `Tests/PlanDesignerTests.cs` — 8 batchmode tests (valid house through the API, plain-language findings, footprint/wall edits + undo,
   two-story with stair, all 46 library plans copy with no new errors, save/rename/delete/reload/in-use, client offer, camera-rendered overlay)
   + 1 windowed-only UI capture (`Category("UICapture")`). Shots: `designer_drawing_*`, `designer_overlay_top/eye`, windowed `designer_ui_*`.
3. Shared edits (small, additive): `Development/HousePlan.cs` (`HousePlans.Unregister`), `Development/DevApps.cs` ("My plans" filter),
   `UI/Tablet.cs` (T doesn't close the tablet while typing in a text field).
4. Limits: rectangular footprint and rooms (no L shapes, no angled walls). Auto window/door rules still run; players can switch single ones off.
   Copying two outbuilding plans can gain one extra auto opening (still valid).
5. Ideas: L-shaped footprints, 3D shell preview, plans that carry across save games, an "Edit in designer" button in the Plans app.
