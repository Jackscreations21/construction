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

## ONE Unity at a time, machine-wide (2026-11-27: 8 parallel runs crashed the user's 16 GB PC)
Every session, the lead included, starts Unity only through the lock wrapper in the shared notes folder:
`bash "C:/Users/jackf/AppData/Local/Claude-Data/claude_pzs8sxrjxfjjc/side-session-notes/local_54073d16-4b24-4e7f-9bca-a4603ff30909/unity-run.sh" <Unity args>`
(waits its turn, runs, releases). Put this rule in every new session brief. Windowed runs only after a batchmode run shows 0 `error CS`
(a windowed Unity with script errors pops a Safe Mode dialog on the user's screen). Prefer at most 4 helpers working at once.

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

## STATE: EVERYTHING ON THE LIST IS DONE (2026-11-27 ~19:40 UTC, lead local_98a2032f-a420-4a0a-9364-69a1031cb23c)
main is pushed and contains: all features (map + follow-up, 46 plans + Plan Designer, 153-job catalog, crews, fleet, subcontractors,
flip houses, seasons/weather/storms, heavy equipment, photo mode, business tutorials), the balance pass (`Docs/BALANCE.md`), the small-fixes
list (`Docs/HANDOFF-small-fixes.md`), map visual fixes + the map mesh-leak fix, and the full code check in two halves
(`Docs/CODECHECK-A.md` 192 files / 47 defects, `Docs/CODECHECK-B.md` 147 files / 26 defects).
**Final full PlayMode run on the combined main: 237 total, 232 passed, 0 failed, 5 skipped by design (windowed-only captures), 0 error CS, no crash.**
No sessions or background tasks are running. No usage cron is running.

Operating notes that still apply:
- ONE Unity at a time (16 GB PC): always `unity-run.sh` (section above). Unity needs the Unity Hub app running for its license (exit 198 otherwise).
- `start_session` only works in a user-typed turn; otherwise use Agent subagents with `isolation: worktree` (that is how balance, map fixes and
  both code-check halves were done) or resume an existing session by SendMessage.
- Normal test runs no longer rewrite tutorial PNGs (`GROUNDBREAK_CAPTURE_TUTORIALS=1` regenerates them). Unity sometimes touches
  `ProjectSettings/*.asset` during runs: do not commit those.

## Left to do (nothing is in progress; these are candidates for the user to choose from)
1. "Needs a decision" lists in `Docs/CODECHECK-A.md` and `Docs/CODECHECK-B.md`. Biggest: SAVE GAPS - exterior work (wrap, siding, brick, eaves,
   deck) and MEP networks (pipes, wiring, ducts, panels, test state) are not saved; New Game does not reset stock/scrap/tool belt; curing blankets
   and heaters lost on load. Also: forklift/saw station have no effect; a vehicle a job needs can be sold; every career has the same weather seed;
   stairs allow 24 in width and no handrail; attic minimum R-38 vs R-49; MapJobs never finds a lot for commercial/industrial builds.
2. Skipped small items: seasons-weather follow-ups, "Edit in designer" button, larger equipment/subs follow-ups (their HANDOFF docs).
3. Narrow downtown storefronts, the bank and the grocery carry no blueprint (no plan that narrow exists).
4. Features the user did NOT pick earlier: walkthroughs/callbacks, bidding, permits, safety/accidents, co-op.
5. Pacing check by a human: the balance model reaches Developer around day 85 with perfect play; HQ cost is the lever if that is too fast.
- The user wants the checklist of everything left written in every reply and the tracker artifact kept current.

## First things for the new lead
1. `get_session` "self" for your own id, then SendMessage every WORKING session (and smart-placement): "New lead session is `<your id>` — send DONE /
   HANDOFF READY / FIX DONE reports there from now on." They currently report to the previous lead.
2. `get_usage`; create the monitoring cron (CronCreate, every 5–10 min) with the pause rule above.
3. Set the in-chat checklist with `update_plan` from "Left to do".
4. Check `git log origin/main..main` and `git status` here, then continue at "Left to do" step 1.

## Update: save gaps round 1 merged
- Exterior (wrap, siding, brick, eaves, decks), MEP networks, set slabs + curing blankets now save; New Game resets site stock and tool belt. Save version 2, old saves load. See `Docs/SAVEGAPS.md`.
- Full suite on the merged tree: 242 total, 237 passed, 0 failed, 5 skipped by design.
- Still not saved (round 2, waiting on the user): wet concrete (forms, rebar, pours), roofing, drywall/paint/insulation, interior finishes, cut stair stringers.
- Unity Hub closes itself sometimes; exit 198 = reopen the Hub app.
