# Handoff: small fixes (session flip-houses, branch `claude/nifty-swanson-4ed79b`)

The lead's list of 11. Screenshots looked at: `Logs/shots/smallfix_*.png`, `flip_03*/05*`, `followup_photo_hidden_helper.png`.

| # | Item | Result | Files |
|---|---|---|---|
| 1 | Stored stock hard to see on racking | FIXED: real-size loads pulled to the front beam (sheet stacks, lumber/rebar bundles with end paint, pallets of bags/cartons/block), yellow beam labels with name + count; racks fill chest-high level first | new `Yard/RackStock.cs`; `Yard/YardSite.cs` (shelf items go through RackStock; old private `Pallet` left unused) |
| 2 | Held tool visible beside open tablet | FIXED: view model hidden while `Tablet.I.IsOpen` | `Player/ViewModel.cs` (shared, small; `ModelShown` for tests) |
| 3 | Crew drywall/paint/dig/sod lack tests | FIXED: new tests; found a real bug: `DrywallTask.RoomFaces` missed the ceiling when the room point sat between joists (retries 8" over) | new `Tests/CrewTaskExtraTests.cs`; `Crew/CrewTask.cs` (shared, small) |
| 4 | Optional delivery fee | FIXED: `Fleet.ChargeSupplierDelivery` (default on), `SupplierDeliveryFee` $79 in Career, `StoreOrderTotal`; tablet supply store shows "Delivery $79 · order total" and charges it; own truck (`Haul`) free | `Fleet/Fleet.cs`; `UI/Tablet.cs` (shared, small). Not on Framing Plans / job / landscape / finish order buttons |
| 5 | Cross-gable wing has no UI button | FIXED: Framing Plans "Add garage wing" (after roof sheathing, gable roofs) and "Roof the garage wing"; main dry-in waits for the wing if there is one | `Framing/ShellProject.cs` (additive + one condition) |
| 6 | Work van polish | FIXED: sloped hood, rounded fenders, wheel arches + flares, bumper, lights, mirrors, shouldered tires on dished steel wheels; API, doors, cargo, colliders unchanged | `Town/Van/WorkVanModel.cs` |
| 7 | Boxy toilet | FIXED: shared real two-piece elongated toilet (15" rim, 30" tall, 28.6" deep, 19.7"x7.5" tank, lathe foot, seat/lid, lever, braided supply); used by the catalog prop (all states kept) and the town house | new `JobCatalog/ToiletModel.cs`; `JobCatalog/CatalogProps.cs`; `Town/HouseBuilder.cs` (shared, 2 lines) |
| 8 | Overlay room labels overlap fixtures | FIXED: labels shrink to fit their room and move to the clearest spot away from fixture markers/tags | new `Blueprints/PlanLabelLayout.cs`; `Blueprints/BlueprintOverlay.cs` (one call) |
| 9 | Flip follow-ups | FIXED: kitchen/bath visibly dated (orange oak, almond laminate, almond/rose tile) → renovated (white shaker, nickel pulls, quartz, subway tile); RepairUI shows a flip hint and ignores B on flip defects; parts the player uses on flip steps count in flip costs once (crew materials not double counted) | new `Flips/FlipInterior.cs`; `Flips/Flips.cs`, `FlipVisuals.cs`; `Repairs/RepairUI.cs` (shared, 2 small edits); `Tests/FlipTests.cs`; `Docs/FLIPS.md` |
| 10 | Speckled winter trees | FIXED (texture): twig texture redrawn as upward-forking twigs, mips average coverage so far crowns fade and limbs show. Branch geometry would belong in `Scripts/Map/Models/MapTrees` (map session's area) — SKIPPED | `Seasons/SeasonLook.cs` |
| 11 | Other handoffs' follow-ups | Photo: `PhotoRender.HideInPhotos` hook (nothing registers yet — the red guide line's owner should call it); close-up "before" photos no steeper than 35°. Subs: crew walks back to the truck before it leaves. Equipment: boom lift stops at the 10.5 m reach envelope; crane truss bundle shrinks as trusses are set. SKIPPED (too big / other areas): photo cross-career cleanup, sub trades/inspection rules, excavator dozing/collisions/pendulum, designer L-shapes/3D preview/"Edit in designer" (needs DevApps), all seasons-weather items (Seasons folder was being edited; weather seed needs a new-career hook) | `Photo/PhotoRender.cs`, `Photo/JobPhotos.cs`, `Subs/SubCrews.cs`, `Equipment/BoomLift.cs`, `Equipment/TruckCrane.cs`; tests `Tests/SmallFixesFollowupTests.cs`, `SubcontractorTests.cs`, `EquipmentTests.cs` |

Also: `Tests/MapFollowupTests.CatalogJobsReachTheWholeMap` was flaky (~1 in 80: the westside district has one enterable house); its 200 picks now use a fixed seed (assertion unchanged).

New tests: `SmallFixesUiTests.cs`, `SmallFixesModelTests.cs`, `SmallFixesSiteTests.cs`, `SmallFixesFollowupTests.cs`, `CrewTaskExtraTests.cs`, flip additions.

## Known leftovers
- Lid-off toilet: the house's own toilet stays visible under the catalog prop (pre-existing; needs fixture hiding for `JobPlaces.Toilet`).
- Sod crew test asserts 3 of 6 rolls (task roll grid vs `LaySod` snapping may report "Already sodded here").
- The delivery fee is only on the tablet supply store.
