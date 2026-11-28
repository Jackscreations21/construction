# Code check A: core and building side

Scope: every `.cs` file under `Assets/Groundbreak/Scripts/` except the half-B folders (Business, Town, Repairs, Fleet, Yard,
Crew, Development, Integration, JobCatalog, Blueprints, Map, MapFollowup, BusinessTutorials, Flips, Subs, Seasons, Equipment,
PlanDesigner, Photo, Balance). Each file below was read completely. Regression tests: `Assets/Groundbreak/Tests/CodeCheckATests.cs`.

## Files reviewed (192)

### Core, Audio, Tools, Player, UI, Save, Career, Logistics, Tutorials, SiteLook (38)
- [x] Core/Economy.cs
- [x] Core/Game.cs
- [x] Core/GameBootstrap.cs
- [x] Core/GInput.cs
- [x] Core/Piece.cs
- [x] Core/PieceDefinition.cs
- [x] Core/SnapSettings.cs
- [x] Core/Tool.cs
- [x] Core/Units.cs
- [x] Audio/Sfx.cs
- [x] Audio/TruckSounds.cs
- [x] Tools/CircularSaw.cs
- [x] Tools/CordlessFramingNailer.cs
- [x] Tools/FramingHammer.cs
- [x] Tools/Level4ft.cs
- [x] Tools/PryBar.cs
- [x] Tools/TapeMeasure.cs
- [x] Tools/ToolFactory.cs
- [x] Player/PlayerFeel.cs
- [x] Player/PlayerRig.cs
- [x] Player/ToolBelt.cs
- [x] Player/ViewModel.cs
- [x] UI/Hud.cs
- [x] UI/Tablet.cs
- [x] UI/Skin/UISkin.cs
- [x] Save/SaveSystem.cs
- [x] Save/SiteGroundSave.cs
- [x] Career/Career.cs
- [x] Career/GameSettings.cs
- [x] Career/MainMenu.cs
- [x] Career/Weather.cs
- [x] Logistics/DeliveryService.cs
- [x] Logistics/GameClock.cs
- [x] Tutorials/TutorialApp.cs
- [x] Tutorials/TutorialContent.cs
- [x] Tutorials/TutorialData.cs
- [x] SiteLook/SiteFx.cs
- [x] SiteLook/SiteLook.cs

### Visuals, Building, Stairs (13)
- [x] Visuals/Materials.cs
- [x] Visuals/MeshBuilder.cs
- [x] Visuals/ProceduralTextures.cs
- [x] Visuals/ProductModels.cs
- [x] Visuals/SurfaceKind.cs
- [x] Building/PlaceTool.cs
- [x] Building/PlacementController.cs
- [x] Building/SmartSnap.cs
- [x] Stairs/FramingSquare.cs
- [x] Stairs/StairBuilder.cs
- [x] Stairs/StairCalculator.cs
- [x] Stairs/StairMath.cs
- [x] Stairs/StringerMesh.cs

### Concrete, Inspection, Insulation (29)
- [x] Concrete/ConcreteCore.cs
- [x] Concrete/ConcreteElement.cs
- [x] Concrete/ConcreteTextures.cs
- [x] Concrete/Excavation.cs
- [x] Concrete/Forms.cs
- [x] Concrete/Layout.cs
- [x] Concrete/Pour.cs
- [x] Concrete/ReadyMix.cs
- [x] Concrete/Rebar.cs
- [x] Concrete/SiteGround.cs
- [x] Concrete/SprayPaint.cs
- [x] Inspection/CloseupBridge.cs
- [x] Inspection/FramingBridge.cs
- [x] Inspection/InspectionFlags.cs
- [x] Inspection/Inspections.cs
- [x] Inspection/InspectionsApp.cs
- [x] Inspection/InspectorNpc.cs
- [x] Inspection/MepBridge.cs
- [x] Inspection/Site.cs
- [x] Inspection/SiteRules.cs
- [x] Insulation/Batt.cs
- [x] Insulation/FaceAim.cs
- [x] Insulation/Insulation.cs
- [x] Insulation/InsulationData.cs
- [x] Insulation/InsulationInspection.cs
- [x] Insulation/InsulationLooks.cs
- [x] Insulation/InsulationParts.cs
- [x] Insulation/InsulationTools.cs
- [x] Insulation/SoftMesh.cs

### Framing, Roofing (28)
- [x] Framing/FastenFeedback.cs
- [x] Framing/FloorPlan.cs
- [x] Framing/Footprint.cs
- [x] Framing/Framer.cs
- [x] Framing/FramingInspection.cs
- [x] Framing/FramingJob.cs
- [x] Framing/FramingTools.cs
- [x] Framing/GhostOutline.cs
- [x] Framing/IJoistEnd.cs
- [x] Framing/IJoistFloor.cs
- [x] Framing/LumberMarks.cs
- [x] Framing/Markup.cs
- [x] Framing/OpeningModels.cs
- [x] Framing/RoughOpening.cs
- [x] Framing/ShellCatalog.cs
- [x] Framing/ShellProject.cs
- [x] Framing/Structural.cs
- [x] Framing/WallPlan.cs
- [x] Roofing/BoomTruck.cs
- [x] Roofing/CrossGable.cs
- [x] Roofing/RoofFraming.cs
- [x] Roofing/RoofGeom.cs
- [x] Roofing/RoofInfo.cs
- [x] Roofing/RoofSafety.cs
- [x] Roofing/RoofTextures.cs
- [x] Roofing/RoofTools.cs
- [x] Roofing/Roofing.cs
- [x] Roofing/ShingleMesh.cs

### Finish, Drywall (28)
- [x] Drywall/Drywall.cs
- [x] Drywall/DrywallData.cs
- [x] Drywall/DrywallInspection.cs
- [x] Drywall/DrywallLooks.cs
- [x] Drywall/DrywallSurface.cs
- [x] Drywall/DrywallTools.cs
- [x] Drywall/Paint.cs
- [x] Finish/Cabinets/Cabinets.cs
- [x] Finish/Cabinets/Countertops.cs
- [x] Finish/Core/CaulkGun.cs
- [x] Finish/Core/CloseupInspection.cs
- [x] Finish/Core/FinishMaterials.cs
- [x] Finish/Core/FinishTool.cs
- [x] Finish/Core/FramePlane.cs
- [x] Finish/Core/PanelMesh.cs
- [x] Finish/Doors/DoorHanging.cs
- [x] Finish/Flooring/Flooring.cs
- [x] Finish/Flooring/PlankFloors.cs
- [x] Finish/Flooring/TileFloor.cs
- [x] Finish/Interior/FinishGeom.cs
- [x] Finish/Interior/InteriorCatalog.cs
- [x] Finish/Interior/InteriorInspection.cs
- [x] Finish/Interior/InteriorMaterials.cs
- [x] Finish/Interior/InteriorTools.cs
- [x] Finish/Trim/TrimInstall.cs
- [x] Finish/Trim/TrimMesh.cs
- [x] Finish/Trim/TrimPiece.cs
- [x] Finish/Trim/TrimProfile.cs

### Exterior, Landscaping (23)
- [x] Exterior/BrickVeneer.cs
- [x] Exterior/Deck.cs
- [x] Exterior/Eaves.cs
- [x] Exterior/ExteriorCatalog.cs
- [x] Exterior/ExteriorInspection.cs
- [x] Exterior/ExteriorLooks.cs
- [x] Exterior/ExteriorTools.cs
- [x] Exterior/ExteriorTrim.cs
- [x] Exterior/ExteriorWall.cs
- [x] Exterior/FiberCement.cs
- [x] Exterior/HouseWrap.cs
- [x] Exterior/VinylSiding.cs
- [x] Landscaping/Grade.cs
- [x] Landscaping/GroundSafety.cs
- [x] Landscaping/LandCatalog.cs
- [x] Landscaping/LandFeatureVisuals.cs
- [x] Landscaping/LandHandsTool.cs
- [x] Landscaping/LandMaterials.cs
- [x] Landscaping/LandModel.cs
- [x] Landscaping/LandModels.cs
- [x] Landscaping/LandTools.cs
- [x] Landscaping/LandscapeFeature.cs
- [x] Landscaping/LandscapeSystem.cs

### Electrical, Plumbing, HVAC, Networks (33)
- [x] Electrical/ElCatalog.cs
- [x] Electrical/ElLabels.cs
- [x] Electrical/ElModel.cs
- [x] Electrical/ElPanelLook.cs
- [x] Electrical/ElRules.cs
- [x] Electrical/ElSim.cs
- [x] Electrical/ElTablet.cs
- [x] Electrical/ElTools.cs
- [x] Electrical/ElVisuals.cs
- [x] Plumbing/PlumbCatalog.cs
- [x] Plumbing/PlumbFit.cs
- [x] Plumbing/PlumbInspection.cs
- [x] Plumbing/PlumbParts.cs
- [x] Plumbing/PlumbTablet.cs
- [x] Plumbing/PlumbTests.cs
- [x] Plumbing/PressureGaugeTool.cs
- [x] Plumbing/WaterHeaterTrim.cs
- [x] HVAC/AirflowSim.cs
- [x] HVAC/DuctDetail.cs
- [x] HVAC/HvacCatalog.cs
- [x] HVAC/HvacInspection.cs
- [x] HVAC/HvacLook.cs
- [x] HVAC/HvacModels.cs
- [x] HVAC/HvacParts.cs
- [x] Networks/MepCore.cs
- [x] Networks/MepMesh.cs
- [x] Networks/MepPrint.cs
- [x] Networks/MepTools.cs
- [x] Networks/NetGraph.cs
- [x] Networks/NetRun.cs
- [x] Networks/NetTypes.cs
- [x] Networks/RunBuilder.cs
- [x] Networks/StudBoring.cs

## Defects fixed (most serious first)
1. Logistics/DeliveryService.cs: deliveries still on the way were not saved (the order was already paid), and the old session's delivery coroutines kept delivering into a loaded game. Now saved as extension `deliveries`; load or new game cancels running deliveries and reschedules the saved ones.
2. Exterior/FiberCement.cs: `FindLast` returned 0 when no stud was in reach, giving negative plank lengths and an endless loop that hung the game.
3. Concrete/Forms.cs, Pour.cs: stripping forms, the blowout check and staking threw MissingReferenceException after the pry bar removed a form board, stake or brace.
4. Concrete/Pour.cs, ReadyMix.cs: discharging the chute or dumping a wheelbarrow onto a slab that had already set took the concrete and lost it.
5. Concrete/Pour.cs: destroyed pours stayed in `PourSystem.All`, so a new pour on that outline could never start; `Groove` could index -1.
6. Insulation/InsulationParts.cs: the attic blower added depth before checking for bags, so loose fill was free with no bags on site.
7. Insulation/Insulation.cs: rolls taken for a multi-roll bay were lost when a later roll was missing; poly and baffles were consumed when staples ran out.
8. Drywall/Paint.cs: paint and primer went on with no stock on site (the `Consume` result was ignored), so the crew's "out of paint" checks never fired.
9. Drywall/Drywall.cs: wall texture went on with no mud on site; `HasBead` counted destroyed corner beads, so a new corner could skip its bead and still pass inspection.
10. Framing/WallPlan.cs: two door cuts in the same frame (crew RunAll) cut the same already-destroyed plate again, leaving duplicate bottom plates.
11. Framing/RoughOpening.cs: `GetComponent ?? AddComponent` (Unity fake-null) never added the StructuralMember in the Editor, so seated windows/doors kept no Product/Flashed member.
12. Framing/WallPlan.cs: the cap-plate layout ghost was rotated 90 degrees (ran across the wall depth).
13. Framing/WallPlan.cs: the wall tilt-up coroutine threw every frame if the wall was destroyed mid-raise.
14. Roofing/RoofFraming.cs: fly rafters over 16' (houses 30' and wider) took 1 board instead of 2.
15. Landscaping/LandTools.cs: the grade tools assumed a bump volume of pi*r^2/2, but the cosine falloff gives 0.934*r^2. Shovel fill and rake topsoil charged about 68% more than they placed, and the wheelbarrow placed about 59% of each load.
16. Networks/NetGraph.cs: destroying a fitting with made joints spawned an orphan "Joints" object at the scene root.
17. IMGUI layout mismatches (control count changing between Layout and the click event): UI/Tablet.cs (switching apps), Tutorials/TutorialApp.cs (missing EndHorizontal), Stairs/StairCalculator.cs (fields changed mid-draw), Electrical/ElTablet.cs (panel schedule edits and a `break` that skipped EndHorizontal), Networks/MepCore.cs (tabs and inspection buttons), Plumbing/PlumbTablet.cs (pump/fill/drain), Finish/Core/CloseupInspection.cs (request inspection). Deferred via `MepTablet.Defer` or `GUIUtility.ExitGUI`.
18. Electrical/ElModel.cs: the 4" round ceiling box returned 20.3 cu in instead of the catalog's 20.
19. Drywall/DrywallTools.cs: crouching on stilts lifted the camera twice per frame (about +36").
20. Finish/Trim/TrimInstall.cs: the "order N' sticks" hint named the longest fitting stick instead of the shortest.
21. Exterior/HouseWrap.cs: flashing calls did not check the opening index.
22. Landscaping: `?.` on Unity objects (`Grade.Field`, `Tablet.I`) skipped the destroyed check; `HoeTool.SowAt` could throw before a lot field existed.
23. Career/MainMenu.cs: the Load page re-read and parsed every save file several times per frame.
24. Core/Units.cs: tiny negative values showed as `-0"`.
25. Core/GInput.cs: F1-F12 were not mapped for the new Input System path (latent; the project uses the legacy input manager today).
26. Mesh/material/texture leaks on rebuild or destroy: Exterior parts, eaves and deck hardware (`ExtOwnedMesh`), landscaping feature visuals, the Grade ground cut, MEP boxes, panels, runs, joints, stud holes, primer, duct detail, water heater trim and fittings (`MepMesh.DestroyVisual`), ElLoad lens materials, DrywallSurface skins, inspection markers, the tutorial beacon, the placement highlight material.
27. Concrete/Forms.cs: `_kickerLeft` was not reset with the other statics.

Shared Core files changed: `Core/GInput.cs` (one additive mapping line) and `Core/Units.cs` (sign of a rounded zero). No renames or signature changes.

## Needs a decision
### Save/load gaps
- Exterior work (wrap, siding, brick, eaves, deck) is not saved.
- MEP networks (runs, boxes, panels, breakers, bored holes, test state) are not saved.
- Cut and laid-out stair stringers (`CutStringer`, `StringerLayoutMark`) probably reload as plain boards.
- `MainMenu.NewGame` does not reset `Game.Stock`/`Scrap`, the tool belt or the snap origin, so material and tools carry into a new career.
- Statics never reset between games: `StairCalculator` layout, `ShellProject.Current`/`ShellApp`, `PaintColors.Current`/`CurrentSheen`, the `FinishSupply` cart, `FinishStock.Unlimited`.
- `SaveSystem.Apply` replaces belt tools; the old TapeMeasure's LineRenderer object is orphaned.

### Material takeoff / exploits
- Picking up a cut stringer returns a whole uncut 2x12 (its offcuts were already added to scrap).
- Wall plates, sills, rims, ceiling joists and rafters longer than 16' are built as one piece from a 16' stock id (no splice or length cap).
- Roofing valley ice and water charges 2x the valley length; a 36" strip uses 1x.
- `RebarSystem.AutoGrid` can lay a second full mat if F is pressed twice; the first-pour rebar check looks at all rebar on site, not this outline.
- `GravelBase.SpreadRect` lays a full base even when gravel on site is short.
- `CoatAll`/`SpotScrews` spot screws without using mud; `CaulkGun.Secondary` throws away the rest of a tube on switch.
- `MepStock.Take` ignores the `TryTake` result; `HvacBuild.Takeoff` charges for a collar before `SplitAt` can fail.
- Backsplash counts a ripped top row as 1 cut instead of 1 per tile.

### Code values and realism
- No stair handrail (IRC R311.7.8, 34"-38" on 4+ risers); `StairBuilder.Stairs` allows widths down to 24" (IRC minimum 36"); stair run above 11-1/4" gives negative nosing; stringers over 192" not handled.
- NEC 210.52 receptacle spacing (12' rule, 6' from a door) is not implemented; no "staple within 12" of the panel" check.
- The GFCI rule assumes every downstream receptacle is on LOAD-side wiring.
- `AtticMinR = 38` for climate zone 4; 2018+ IRC requires R-49.
- Gravel drive is 6" total (typical 8-12"); fence posts in 24" holes (shallow for a 6' privacy fence, ignores frost depth).
- `InteriorInspection.Nailed` description says 16" o.c., the check uses length/24".
- `RoofSafety.LanyardLength = 15` m (about 49') vs the shop text "25' lanyard".
- Wall header label shows the HeaderFor size even when the depth was bumped to 2x12.
- `SiteLayout.StringPoint` doc comment says 8", code uses 12".
- `DeckKitTool` hint says ledger top at the aim point; the aim height is used as the deck surface (1" off).
- `Units.Format` shows values under 1" as `0 1/2"`.

### Behaviour/UI
- `ShellApp.Draw` "Crew $" button runs `RunAll()` inside the IMGUI click event (possible layout mismatch error).
- `Tablet.Update`: T opens the tablet over another full-screen UI holding `Game.UIOpen` (e.g. the main menu); `_app` is an index into visible apps, so `SetAppVisible` can switch the open app.
- `PlacementController.UpdateGhost` reads `GInput.Ctrl` while the UI is open.
- `AirflowSim.Current` recomputes every frame while the HVAC tab is open; `AirflowSim.ExhaustOf` passes any exhaust cap when no envelope is defined.
- `RunBuilder.Route` does not check the end port is free (API callers only).
- `NetRun.SplitAt` exactly on an interior vertex leaves a 1.5 mm doubled-back segment (visual).
- `SiteGround.Compact` lowers compacted cells again on each pass; pry-bar-pulled stakes still count in `FormRun.StakeT`; `AutoAnchors` puts anchors outside runs shorter than 24"; rebar chairs one bar radius short (visual).
- `DrywallInspection.Ends`: `MemberAt` can pick a crosswise member; `TrimPiece.Nail` crown top-nail counts any collider as framing; `DoorHanging.CutPlateInOpening` doesn't add the new plates to `fp.Members`.
- `ExteriorWall.FromHit` can return a destroyed wall; `LandscapeSystem.DressPiles` never resizes a mound; `GradeField.PaintRect` doesn't skip the pad; `Eaves.AddDownspout` length uses the gutter end height; deck boards over 24' wide leave gaps (not reachable today).
- Side effects of the fixes: `ApplyTexture("smooth")` on a fully skimmed wall now returns false without mud; shovel fill and rake topsoil use less material per click and wheelbarrow mounds are about 1.7x taller (same volume).

### Remaining leaks and allocations
- `UISkin.Build` makes new textures and a GUISkin on every screen-height change (destroying them could break cached styles elsewhere).
- `ChalkLine.Lines` grows forever; `InspectionMarkers.Refresh` rebuilds flag meshes without destroying the old ones; FramingJob ghost lambdas throw if their walls are destroyed while the job is unfinished.
- Meshes never destroyed for TrimPiece, Bead, Countertop, Cabinet, HungDoor frame, TileFloor layers, MEP fitting bodies and fixture trim; corner beads are not parented to their surface.
- `ElLabels.Text` caches one material/texture per distinct label string forever; `ElVisuals._panelHash` keeps destroyed panels as keys.
- `DeliveryService.RebuildPiles` rebuilds every pile on every stock change; `ToolUtil.CenterLabel` and `MainMenu` allocate a GUIStyle per OnGUI call.

## Update 2026-11-28: "Needs a decision" resolved
Everything in the list above is fixed except:
- NEC 210.52 receptacle spacing and the GFCI LOAD-side rule: skipped, the electrical model has no room/wall-line data and no LINE/LOAD terminals.
- Ceiling joists and hips over 16 ft stay single pieces (they were already charged the right board count).
- `Fleet/Jobs/ContractorJobSite.cs` builds its own plates and was not checked for the splice rule.
- Guards on open stair sides over 30 in (IRC R312) are not modelled.
Chosen values: stairs 36 in minimum, handrail on 4+ risers at 34-38 in, tread run capped at 11-1/4 in, stringer over 16 ft refused (add a landing),
attic R-49, gravel drive 8 in, fence posts 36 in deep on 10 ft posts, roof kit is a 50 ft rope lifeline + rope grab ($169), 20 ft 2x8/2x10/2x12 stock.
Save items: see `Docs/SAVEGAPS.md` (round 3). The Tablet items were fixed in `Scripts/UI`. Tests: `CodeValuesTests`, `CodeValuesExploitTests`, `StairToolTests`.
