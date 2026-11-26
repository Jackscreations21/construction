# Lead session handoff — Groundbreak (written 2026-11-26 ~20:45 UTC)

You are the **lead session**. Work in this checkout on `main`. Read `CLAUDE.md`, `Docs/BUSINESS.md`, then this file.
(`Docs/HANDOFF-coordinator.md` is the long history; this file is the current state and wins where they differ.)
Previous lead: `local_54073d16-4b24-4e7f-9bca-a4603ff30909` (context reached the 50% rule).

## What the user wants
A realistic first-person house-building game (Unity 6000.6.8f1, everything from code) that is also a full business game:
repairs out of a van → warehouse + trucks → hired workers → full houses → neighborhoods. Lots of blueprints and lots of different jobs.
The user is not a programmer: keep replies short and plain, say what changed in the game.

## User rules (enforce)
- Run work in parallel nested sessions, **on Opus** (`model: "claude-opus-5-5"`; verify with `get_session`).
- **Pause everything before plan usage reaches 90%** (`get_usage` → plan windows). Check every 10 min; if it climbs a lot (>~12 points per
  10 min or past 60%) check every 5 min. To pause: SendMessage each running session "Plan limit: commit WIP now and stop", then `stop_session`
  each right away (a queued message only runs after the in-flight turn is stopped). 8 Fable sessions burned 4 pts/min; 8 Opus ~1 pt/min.
- Nested sessions hand off at **40%** of their own context (commit, `Docs/HANDOFF-<name>.md`, report "HANDOFF READY"); you start a replacement.
- Lead hands off at **50%** of its own context: have nested sessions write handoffs, update this file, give the user a paste-ready brief. Don't start the next lead yourself.
- Keep the in-chat checklist (`update_plan`) showing everything left, and keep the tracker artifact current:
  https://claude.ai/artifact/WwS89dEKZHeosQa3HZpPiW — rows in its db: collection `items` {status "work"|"next"|"done", order, area, title, note, paused},
  doc `meta/info` {text}. Use `ArtifactData` (get for the version, then update with `if_version`).
- App limits: sessions can only be **started** in a turn the user typed (cron/peer-message turns can only propose cards); resuming an existing
  session by SendMessage works from any turn. `gh` is not installed: merge locally, `git push origin main`.

## Merge procedure
`git merge --no-ff <branch>`; run all PlayMode tests (command in CLAUDE.md, never `-nographics`, one Unity per folder at a time — check no run is going);
check `Logs/playmode.xml`; LOOK at new `Logs/shots/*.png`; push; tell running sessions to `git merge main`; update tracker + checklist.
Known merge noise: (1) every test run rewrites ~80 `Assets/Groundbreak/Resources/Tutorials/*.png` — `git checkout -- Assets/Groundbreak/Resources/Tutorials`
before merging/committing; on conflict keep main's. (2) `Scripts/Business.meta` / `Business/Company.cs.meta` add/add conflicts — keep main's.
(3) untracked Unity files blocking a merge (`ProjectSettings/PackageManagerSettings.asset`, .meta) — delete them and retry.

## State of main (local, NOT all pushed)
- Pushed (origin/main 4803bd1): all six trade milestones, tutorials, polish, `Company` core, **smart placement**, **yard-fleet** (vehicles, warehouse,
  finance, contractor jobs), **ui-capture** (tablet home grid + grouped sidebar, `Tablet.SetAppGroup(app, group)`, windowed capture — Docs/UI-CAPTURE.md). 64 tests + 1 skipped.
- Local only, on top: **crew-workers** merge (e52e878), a SaveSystem fix (batch-mode runs use a per-checkout save folder), **mep-site-details** merge (fc972ba, untested on main).
- **Blocking the push**: `SmartPlacementTests.SecondTopPlateSnapsFlush` fails intermittently on main (picks a 16" O.C. candidate instead of the flush cap;
  also reported failing alone on 4803bd1). The smart-placement session was asked to make it deterministic and report "smart-placement FIX DONE".
  When it does: merge `claude/smart-placement-851e8c`, run the full suite, look at `crew_*.png` and `detail_*.png` shots, push.

## Sessions (all Opus, worktrees under `.claude/worktrees/`)
| name | id | branch | state |
|---|---|---|---|
| town-repairs | local_378f0892-0825-4f13-bb36-0154c9bc5575 | claude/town-repairs-b14d3c | WORKING: town (streets, 17 houses, store) written; van + repair jobs in progress. Owns `Town` API (Docs/TOWN.md on its branch) |
| development | local_ca789e89-8199-43c4-a90d-7a7b60af366e | claude/development-3892f9 | WORKING: house plans, Builder draws, Developer land/subdivision/sales; own `ILotProvider`/`ICrewProgress` until Town/Crew adapters |
| structure-extras | local_97c176bc-8e90-4975-a92a-b82a0ede7a23 | claude/structure-extras-dfa71f | WORKING: window/door models, I-joist floors, roof valleys, + fix `StructuralRules.Classify` (doubled top plate shows red) |
| smart-placement | local_a1169bf4-9a64-42e5-b8c8-2622eb9a3d63 | claude/smart-placement-851e8c | fixing the flaky test above |
| mep-site-details | local_3ba35a70-5d6e-4c69-b03d-47369fc3b077 | claude/mep-site-details-92f734 | DONE @ 968b144, merged locally |
| crew-workers | local_300959da-4030-4adb-a115-ed2c67f24fdd | claude/crew-workers-ed35ce | DONE @ 97ebe12, merged locally. Hooks: `CrewManager.HereJobResolver`, `Crew.AddTask` (JobId) |
| yard-fleet | local_91a3c222-2510-4367-8c6a-1ac497730909 | claude/yard-fleet-6f0301 | DONE, merged + pushed. `TownHook.JobLotProvider` to wire after Town merge |
| ui-capture | local_89767c4f-878f-4b62-beab-089d49732008 | claude/ui-capture-79450e | DONE, merged + pushed |

## Left to do, in order
1. Land the snap-test fix; test + push main with crew and MEP details.
2. Merge town-repairs, structure-extras, development as they report DONE (town first if possible).
3. **Integration session**: wire real `Town` lots into yard-fleet (`TownHook.JobLotProvider`), development (`ILotProvider`) and crew (`HereJobResolver`,
   `ICrewProgress`); business apps call `Tablet.SetAppGroup(name, "Business")`; one van model/type between Town and Fleet; Career new-game flow end to end
   (Handyman → Contractor → Crew → Builder → Developer) with a playthrough test.
4. **Blueprints (user request, 1–2 sessions)**: "blueprints and full layouts for a ton of houses and buildings and warehouses" on development's plan format:
   residential (starters, ranches, two-stories, duplexes, townhomes) and non-residential (garages, sheds, barns, shops, offices, warehouses); full room layout,
   walls, openings, roof, MEP fixture locations, takeoff.
5. **Job catalog (user request, 1 session)**: many different repair jobs and new-build jobs per stage, data-driven, extending Repairs / Fleet-Jobs / Development.
6. Business tutorials with pictures (Tutorials app; windowed capture for tablet shots).
7. Balance pass (prices, wages, pay, growth pace) and small fixes: stop the Tutorials test rewriting Resources PNGs on normal runs; stored stock hard to see on
   warehouse racking; held tool visible beside the open tablet; crew drywall/paint/dig/sod tasks lack tests; optional delivery fee (`Fleet.DeliveryFee`).

## First things for the new lead
1. `get_session` "self" for your own id, then SendMessage every WORKING session (and smart-placement): "New lead session is `<your id>` — send DONE /
   HANDOFF READY / FIX DONE reports there from now on." They currently report to the previous lead.
2. `get_usage`; create the monitoring cron (CronCreate, every 5–10 min) with the pause rule above.
3. Set the in-chat checklist with `update_plan` from "Left to do".
4. Check `git log origin/main..main` and `git status` here, then continue at "Left to do" step 1.
