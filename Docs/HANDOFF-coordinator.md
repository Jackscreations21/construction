# Coordinator handoff (2026-11-09)

You are the coordinating session for Groundbreak (see `CLAUDE.md`, spec link inside). The repo is at
`C:\Users\jackf\OneDrive\Documents\realistic building game`. GitHub: https://github.com/Jackscreations21/construction (`main` pushed).
`gh` CLI is NOT installed: merge branches locally into `main`, run tests, `git push origin main`. The user said to merge PRs/branches yourself.

## Parallel milestone sessions (each in its own git worktree + branch, cut from main @ add53eb)
| name | session id | scope |
|---|---|---|
| concrete-forms | local_98ddaa20-122e-4c01-af0f-c5ea4f24133b | M2 layout, excavation, forms, rebar, Concrete Dispatch app, pour/finish/cure, `ConcreteElement` |
| shell-framing | local_f9be44bb-57b2-4dca-a2ca-b20d2ad9401c | M3 support graph, floor, walls/tilt-up, rough openings, stairs, roof framing, shingles |
| mep-systems | local_24f83b33-ad88-439a-a073-e80509d31ccc | M4 plumbing, electrical (circuit sim), HVAC, `Scripts/Networks/` |
| closeup-finish | local_0e3be36c-0455-4bc9-9e7e-6fa63167d4bb | M5 insulation, drywall, siding/brick veneer, interior finish |
| site-career | local_f6448317-3025-47c2-b2e4-b9915a46285c (HANDOFF READY @ 9267544, idle) | M6 landscaping, `Inspections.Register`, `SaveSystem.RegisterExtension`, career, audio, UI skin |
| site-career-2 | local_bf70d6ea-f984-47e2-ae51-4180f4a5c30d (branch `claude/festive-babbage-09d89f`, merges claude/site-career-92d50d first, terrain option 2) | M6 continuation |

Branches: `claude/concrete-forms-e5b4e0`, `claude/shell-framing-146d5f`, `claude/mep-systems-8b0d35`, `claude/closeup-finish-2a5558`, `claude/site-career-92d50d` (worktrees under `.claude/worktrees/`).

## User rules (must enforce)
- Pause everything if any plan limit (`get_usage` → plan.windows[].percentUsed) reaches **90%**.
- If any session's context reaches **40%**: it commits `WIP:`, writes `Docs/HANDOFF-<name>.md`, reports "HANDOFF READY"; then YOU start a fresh session
  (`start_session`, initiation user_asked, use_worktree true, `source_branch` = that session's branch) whose brief is: read the handoff file and continue.
  Lead session rule (user, 2026-11-09, replaces the old coordinator-40% rule): the coordinator is the **lead session**. When the lead's own
  context reaches **50%**, tell every running milestone session to commit WIP and write `Docs/HANDOFF-<name>.md` (on its branch), wait for
  their HANDOFF READY, then update this file as the lead handoff, commit + push, and give the user a ready-to-paste brief for a new lead
  session. Do NOT auto-start the next lead; the user starts it.
- Sessions may message each other (roster above). Note: Claude Desktop limits a session to ~10 cross-session messages per user message.

## Merging procedure
1. When a session reports done (or HANDOFF READY with useful partial work), `git merge --no-ff <branch>` into main in this checkout.
2. Resolve conflicts (expected hot spots: `PieceDefinition.cs`, `SurfaceKind.cs` (append-only enum), `ProceduralTextures.cs`, `Materials.cs`, `Tablet.cs`, `Hud.cs`, `GameBootstrap.cs`).
3. Run all PlayMode tests (command in CLAUDE.md, no -nographics), check `Logs/playmode.xml`, LOOK at `Logs/shots/*.png`.
4. Push main. Tell running sessions to `git merge main` into their branch when convenient.

## State of main
Milestone 1 complete: snap grid/angles, tool belt + tools, tablet (store with category tabs, tool shop, deliveries, bank, help),
76-item catalog with real models + procedural textures, extension points (`Catalog.Register`, `ToolFactory.Register`, `Tablet.RegisterApp/AddHelp`).
Tests: 32/33 after M5 merge (all milestones in). Known failure: PlaythroughTests.Milestone1Playthrough fails when run after PadInLotTests ("Place() failed · aim at a surface") — test-isolation bug, assigned to polish-site session.

## Merged milestones
- M2 concrete-forms → main @ ee1ecc9 (2026-11-09). Session done. Docs/CONCRETE.md.
- M4 mep-systems → main @ 75a838c (2026-11-09). Session done. Docs/MEP-BRIEF.md. Follow-up: bridge MepInspection rules into Inspections.Register(RoughMEP) (site-career-2).
- M3 shell-framing → main @ 0d0c5aa (2026-11-09). Session done. APIs: side-session-notes/shell-framing-api.md. Left: gable-end sheathing above plates, I-joist floor, valleys (API only), FramingInspection.Run() not yet registered with Inspections (site-career-2).
- M6 site-career(-2) → main @ 19a14bc (2026-11-09). Sessions idle/done. Shared inspection types renamed InspectionItem/InspectionFlags (framing owns InspectionIssue/InspectionMarkers). Bridges: Closeup (reflection, untested vs real M5), Mep, Framing. Nits: 40_pad_in_graded_lot shot too far away; gate grain sideways; pad trench walls keep grass cover.
- M5 closeup-finish → main @ a98fb60 (2026-11-09). Session done. Docs/HANDOFF-closeup-finish.md. ALL FIVE MILESTONES MERGED.
- Tutorials (user request): session local_1a7a0a35-5219-4bb0-9c3b-f63de2e38681, tablet "Tutorials" app with captured pictures per step; needs M5/M6 pictures after those merge.

## Lead decisions
- Terrain (2026-11-09): `Grade` (site-career) owns the lot; `SiteGround` (concrete, 4" cells) is the 60'x60' building pad inside a hole in Grade;
  `Grade.HeightAt` defers to SiteGround inside the pad; SaveSystem key "siteground" for SiteGround.H/Cover.
- App limitation: replacement sessions cannot be auto-started from cron/peer-message turns; they appear as proposal cards the user must click
  (or the user tells the lead "start it"). Proposal cards drop `source_branch`, so briefs must begin with `git merge --no-ff <old branch>`.

## Polish phase (user, 2026-11-09: "with the last of the 5 hour window have sessions refine the look and feel of everything")
Lead session id: `local_54073d16-4b24-4e7f-9bca-a4603ff30909` (earlier briefs wrongly said local_027e6b11 = previous coordinator).
| name | session id | scope |
|---|---|---|
| polish-site | local_56f26ea2-abd9-4e97-a344-29bc9195a1c1 | FIRST fix PadInLot→Milestone1Playthrough order failure; then lighting/sky/fog/ground, concrete, terrain, landscaping |
| polish-structure | local_5224938a-60c3-4b7f-8c41-f498a71f0f5d | framing, roofing, stairs, windows/doors, MEP materials + feel |
| polish-finish-ui | local_c28f719d-c5a3-434c-ad1c-d297805cf49e | finishes (Finish/), HUD/tablet/UISkin, player feel, view-models, audio |
| tutorials | local_1a7a0a35-5219-4bb0-9c3b-f63de2e38681 | tablet Tutorials app with captured pictures (all milestones now on main) |
All cut from main @ fb84616, own worktree branches. 90% plan-limit pause applies (5-hour window resets 2026-11-10 05:00 UTC).

## Polish merges
- tutorials → main @ f62cb3e (26 tutorials, 98 real pictures; HUD/tablet pictures need one non-batchmode run, see Docs/TUTORIALS.md).
- polish-site → main @ 1e1343d. Fixed the order failure (real bug: player buried on 6% slope fell through terrain → Landscaping/GroundSafety.cs). Terrain shader, haze, day cycle, particles.
- polish-structure → main (46/46 tests). Not done: window/door models, panel detail, duct seam line (Docs/HANDOFF-polish-structure.md).
- polish-finish-ui → main (50/50 tests). HUD/tablet IMGUI not screenshot-able in batchmode: needs a look in the editor.

## Status 2026-11-09 (end of polish window)
Everything merged and pushed; main 50/50 PlayMode tests, 0 compile errors. No sessions working. Open follow-ups:
window/door models, electrical panel detail, duct seam line (structure); faint dig dust (site); HUD/tablet visual check in the editor;
tutorial HUD/tablet pictures need one non-batchmode capture run (Docs/TUTORIALS.md).

## Business game phase (user, 2026-11-26) — see Docs/BUSINESS.md
User: "turn it into a full game where you make money": van repairs → warehouse + trucks → hire workers → houses → neighborhoods.
Also: "improve the placing system so it figures out where a piece should go by finding other pieces of wood or other stuff".
Shared core `Scripts/Business/Company.cs` written by the lead on main. Phase 1 sessions (own worktrees from main): town-repairs, yard-fleet,
crew-workers, smart-placement. Phase 2 (after merge): `Scripts/Development/` (builder/developer tiers, neighborhoods), business tutorials, balance,
plus leftovers (window/door models, panel detail, duct seams, dig dust, HUD/tablet look in editor).
Rules unchanged: nested sessions hand off at 40% context, lead at 50%, pause all at 90% plan usage.
| name | session id | scope |
|---|---|---|
| town-repairs | local_378f0892-0825-4f13-bb36-0154c9bc5575 | Scripts/Town + Repairs: town, drivable van, repair jobs, Jobs app; owns `Town` API |
| yard-fleet | local_91a3c222-2510-4367-8c6a-1ac497730909 | Scripts/Fleet + Yard: dealership, warehouse levels, finance/loans, contractor jobs |
| crew-workers | local_300959da-4030-4adb-a115-ed2c67f24fdd | Scripts/Crew: hiring, worker NPCs doing real tasks, payroll, scheduling |
| smart-placement | local_a1169bf4-9a64-42e5-b8c8-2622eb9a3d63 | Scripts/Building: context-aware snapping to neighboring pieces (only session editing PlacementController) |
All from main @ 26197ab (2026-11-26).

## Tracker artifact (user request 2026-11-26)
https://claude.ai/artifact/WwS89dEKZHeosQa3HZpPiW — done / being worked on / left to do. Rows live in its database: collection `items`
(fields: status "work"|"next"|"done", order, area, title, note) and doc `meta/info` {text}. On every merge or new session, update rows with the
ArtifactData tool (get the row first for its version, then update with if_version) and refresh `meta/info`. Main verified 50/50 with Company.cs.

## Extra parallel sessions (user 2026-11-26: "run anything you can in parallel but pause if any limit reaches 90"; chose "Start now anyway" at 72%)
| name | session id | scope |
|---|---|---|
| development | local_ca789e89-8199-43c4-a90d-7a7b60af366e | Scripts/Development: house plans, Builder draws, Developer land/subdivision/sales; compiles without Town/Crew (own ILotProvider/ICrewProgress), adapters after merge |
| structure-extras | local_97c176bc-8e90-4975-a92a-b82a0ede7a23 | window/door models, I-joist floors, roof valleys |
| mep-site-details | local_3ba35a70-5d6e-4c69-b03d-47369fc3b077 | panel detail, duct seams, dig dust |
| ui-capture | local_89767c4f-878f-4b62-beab-089d49732008 | windowed (non-batchmode) HUD/tablet capture + fixes, tablet layout for many apps, tutorial HUD pictures |
Monitor cron now every 5 min, pauses at 88% (usage climbs ~1 pt/min). After a pause/reset the user must say "go" to restart (app blocks auto-start).
Merge order hint: town-repairs first (others adapt to Town), then yard-fleet, crew-workers, development; ui-capture changes Tablet layout — merge it
before the business apps if possible, else re-check tab layout after.
User rule (2026-11-26): nested sessions run on Opus (`claude-opus-5-5`) — pass `model` when starting new ones; the 8 current sessions were switched with set_session_model (applies from each one's next turn).

## PAUSED 2026-11-26 ~17:17 UTC — plan 5-hour limit hit 95% (8 sessions burn ~4 pts/min; 5-min ticks were too slow)
All 8 sessions told to commit WIP and stop (stop_session used so the queued message runs). Resets 20:10 UTC. A one-shot cron at 15:14 local resumes
four sessions by SendMessage and recreates the monitor (pause threshold 85%). Lesson: with 8 sessions check usage every ~2 min or run at most 4.
Branches: check `git branch -a` for claude/town-repairs-*, yard-fleet-*, crew-workers-*, smart-placement-*, development-*, structure-extras-*,
mep-site-details-*, ui-capture-*; none merged yet.

## RESUMED 2026-11-26 ~20:11 UTC (user: "go", limit reset to 0%)
Running (Opus): town-repairs, yard-fleet, crew-workers, smart-placement — told to use at most one helper agent at a time.
Waiting (resume by SendMessage as slots free): development, structure-extras, mep-site-details, ui-capture.
Monitor cron every 4 min, pause threshold 85%. Parallelism doesn't add throughput when the plan limit is the bottleneck; keep it at 4.
