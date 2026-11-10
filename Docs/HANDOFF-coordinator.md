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
| site-career | local_f6448317-3025-47c2-b2e4-b9915a46285c | M6 landscaping, `Inspections.Register`, `SaveSystem.RegisterExtension`, career, audio, UI skin |

Find their branches with `git worktree list` / `git branch -a` (names contain the slugs above).

## User rules (must enforce)
- Pause everything if any plan limit (`get_usage` → plan.windows[].percentUsed) reaches **90%**.
- If any session's context reaches **40%**: it commits `WIP:`, writes `Docs/HANDOFF-<name>.md`, reports "HANDOFF READY"; then YOU start a fresh session
  (`start_session`, initiation user_asked, use_worktree true, `source_branch` = that session's branch) whose brief is: read the handoff file and continue.
  Same rule applies to you: at 40% context write a new coordinator handoff and start a fresh coordinator.
- Sessions may message each other (roster above). Note: Claude Desktop limits a session to ~10 cross-session messages per user message.

## Merging procedure
1. When a session reports done (or HANDOFF READY with useful partial work), `git merge --no-ff <branch>` into main in this checkout.
2. Resolve conflicts (expected hot spots: `PieceDefinition.cs`, `SurfaceKind.cs` (append-only enum), `ProceduralTextures.cs`, `Materials.cs`, `Tablet.cs`, `Hud.cs`, `GameBootstrap.cs`).
3. Run all PlayMode tests (command in CLAUDE.md, no -nographics), check `Logs/playmode.xml`, LOOK at `Logs/shots/*.png`.
4. Push main. Tell running sessions to `git merge main` into their branch when convenient.

## State of main
Milestone 1 complete: snap grid/angles, tool belt + tools, tablet (store with category tabs, tool shop, deliveries, bank, help),
76-item catalog with real models + procedural textures, extension points (`Catalog.Register`, `ToolFactory.Register`, `Tablet.RegisterApp/AddHelp`).
Tests: `PlaythroughTests.Milestone1Playthrough`, `PlaythroughTests.Showroom` (2/2 passing).
