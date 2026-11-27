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

## State of main (updated 2026-11-27 ~01:50 UTC by lead local_98a2032f-a420-4a0a-9364-69a1031cb23c)
- **Pushed, origin/main a267cfc, 121 tests: 120 pass + 1 skipped (UICapture, by design).** Everything before, plus integration (9369180),
  job-catalog (492330a, 153 jobs), full-map (6ea95a7, ~260 buildings), blueprints (a267cfc, 46 plans). Nothing is local-only.
- All helper sessions so far are DONE and merged; notes in `Docs/HANDOFF-<name>.md`, plus `Docs/JOBS.md`, `Docs/MAP.md`, `Docs/BLUEPRINTS.md`.

## Sessions
Running since 2026-11-27 ~01:50 UTC (Opus, own worktrees): map-followup local_0a9e536b-d192-479a-b75d-c815596ca74b, tutorials local_226d21d9-1006-4a75-9928-8528661fa526.
- map-followup, task_cf34d273: dress empty lots, plan ids on map buildings, catalog jobs across the whole map, workers out of the road,
  SiteHud "Job Board" text, retire old Career Job Board, for-sale signs on claimed lots. Folder `Scripts/MapFollowup/`.
- tutorials, task_38148f7b: business tutorials with pictures, and stop normal test runs rewriting `Resources/Tutorials/*.png`. Folder `Scripts/BusinessTutorials/`.
Usage watch: session cron every 10 min (pause helpers at 85%). It dies with the lead session; re-create it.

## Left to do, in order
1. Merge map-followup and tutorials when they report (look at `followup_*`, `tut_business_*` shots).
2. Balance pass (prices, wages, pay, growth pace) and small fixes, after those two: stored stock hard to see on warehouse racking; held tool visible
   beside the open tablet; crew drywall/paint/dig/sod tasks lack tests; optional delivery fee (`Fleet.DeliveryFee`); cross-gable wing has no UI button;
   van model polish; boxy toilet model (`jobcat_toilet_*`); blueprint overlay room labels overlap fixture markers; map edge shows a dark void from the air.
3. User request (2026-11-26): after everything above is finished, have one or two sessions go through every bit of code checking for errors
   (split by folder, e.g. Core+building trades / Business+Town+Map+Integration; fix real bugs, add tests, report). Tracker row `n-codecheck`.
- The user wants the checklist of everything left written in every reply (the pinned list disappears) and the tracker artifact kept current.

## First things for the new lead
1. `get_session` "self" for your own id, then SendMessage every WORKING session (and smart-placement): "New lead session is `<your id>` — send DONE /
   HANDOFF READY / FIX DONE reports there from now on." They currently report to the previous lead.
2. `get_usage`; create the monitoring cron (CronCreate, every 5–10 min) with the pause rule above.
3. Set the in-chat checklist with `update_plan` from "Left to do".
4. Check `git log origin/main..main` and `git status` here, then continue at "Left to do" step 1.
