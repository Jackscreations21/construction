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
Tests: 18/18 passing after M3 merge (M1, concrete, MEP x4, shell, roofing, stairs).

## Merged milestones
- M2 concrete-forms → main @ ee1ecc9 (2026-11-09). Session done. Docs/CONCRETE.md.
- M4 mep-systems → main @ 75a838c (2026-11-09). Session done. Docs/MEP-BRIEF.md. Follow-up: bridge MepInspection rules into Inspections.Register(RoughMEP) (site-career-2).
- M3 shell-framing → main @ 0d0c5aa (2026-11-09). Session done. APIs: side-session-notes/shell-framing-api.md. Left: gable-end sheathing above plates, I-joist floor, valleys (API only), FramingInspection.Run() not yet registered with Inspections (site-career-2).
- Tutorials (user request): session local_1a7a0a35-5219-4bb0-9c3b-f63de2e38681, tablet "Tutorials" app with captured pictures per step; needs M5/M6 pictures after those merge.

## Lead decisions
- Terrain (2026-11-09): `Grade` (site-career) owns the lot; `SiteGround` (concrete, 4" cells) is the 60'x60' building pad inside a hole in Grade;
  `Grade.HeightAt` defers to SiteGround inside the pad; SaveSystem key "siteground" for SiteGround.H/Cover.
- App limitation: replacement sessions cannot be auto-started from cron/peer-message turns; they appear as proposal cards the user must click
  (or the user tells the lead "start it"). Proposal cards drop `source_branch`, so briefs must begin with `git merge --no-ff <old branch>`.
