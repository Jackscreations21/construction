# Lead session handoff — Groundbreak (written 2026-11-26 ~20:45 UTC)

You are the **lead session**. Work in this checkout on `main`. Read `CLAUDE.md`, `Docs/BUSINESS.md`, then this file.
(`Docs/HANDOFF-coordinator.md` is the long history; this file is the current state and wins where they differ.)
Previous lead: `local_54073d16-4b24-4e7f-9bca-a4603ff30909` (context reached the 50% rule). New lead: `local_98a2032f-a420-4a0a-9364-69a1031cb23c`, started and detached 2026-11-26; helpers were told to report to it; the old lead's usage check is stopped.

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

## State of main (updated 2026-11-26 ~20:55 UTC by lead local_98a2032f-a420-4a0a-9364-69a1031cb23c)
- **Pushed, origin/main fda9dc3, 100 tests pass + 1 skipped (UICapture, by design):** everything before, plus crew-workers, mep-site-details,
  the smart-placement test fix, development, town-repairs, structure-extras. Nothing is local-only.
- The earlier helper sessions (town-repairs, development, structure-extras, smart-placement, crew-workers, mep-site-details, yard-fleet, ui-capture)
  are all DONE and merged; their notes are `Docs/HANDOFF-<name>.md`.

## Sessions running (all Opus, own worktrees, started 20:51 UTC, report to the lead)
| name | id | branch | task |
|---|---|---|---|
| integration | local_05a7f175-4270-437c-a3a8-481b750b3c2a | claude/integration-* | wire Town lots/Crew/Development/Fleet, one van, tablet groups, Career playthrough test |
| blueprints | local_ff81f833-9625-43f6-9ed4-b74d82863730 | claude/blueprints-* | 40+ plans in `Scripts/Blueprints/` on the `HousePlan` format, `Docs/BLUEPRINTS.md` |
| job-catalog | local_ce07e7ff-f4d7-4356-98bc-59e1d71fee58 | claude/job-catalog-* | 60+ repairs, 25+ contractor jobs, build jobs, `Scripts/JobCatalog/`, `Docs/JOBS.md` |
| full-map | local_7e060d9b-6544-43be-a0c9-6c3deb424650 | claude/full-map-ee35f7 | user request: full map, nothing empty, many buildings with blueprints/jobs, `Scripts/Map/`, `Docs/MAP.md` |

Each reports "<name> DONE @ <commit>" or "<name> HANDOFF READY". Expect overlaps when merging: integration and full-map both touch `Scripts/Town/`;
job-catalog and full-map both lean on blueprint plan ids (looked up at runtime, missing ids tolerated).

## Left to do, in order
1. Merge integration, blueprints, job-catalog, full-map as they report (test, look at `career_*`, `bp_*`, `jobcat_*`, `map_*` shots, push, tell the others to merge main).
2. A short follow-up pass once all four are in: map buildings use the new plans and jobs; playthrough test still passes on the big map.
3. Business tutorials with pictures (Tutorials app; windowed capture for tablet shots).
4. Balance pass (prices, wages, pay, growth pace) and small fixes: stop the Tutorials test rewriting Resources PNGs on normal runs; stored stock hard to see on
   warehouse racking; held tool visible beside the open tablet; crew drywall/paint/dig/sod tasks lack tests; optional delivery fee (`Fleet.DeliveryFee`);
   cross-gable wing has no UI button; van model polish.
- The user wants the checklist of everything left written in every reply (the pinned list disappears) and the tracker artifact kept current.

## First things for the new lead
1. `get_session` "self" for your own id, then SendMessage every WORKING session (and smart-placement): "New lead session is `<your id>` — send DONE /
   HANDOFF READY / FIX DONE reports there from now on." They currently report to the previous lead.
2. `get_usage`; create the monitoring cron (CronCreate, every 5–10 min) with the pause rule above.
3. Set the in-chat checklist with `update_plan` from "Left to do".
4. Check `git log origin/main..main` and `git status` here, then continue at "Left to do" step 1.
