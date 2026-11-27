# Handoff — seasons-weather (DONE 2026-11-27)

Branch `claude/happy-hopper-4811ae`. Design, rules, API and the list of shared-file edits: `Docs/SEASONS.md`.
Full PlayMode suite on this branch (main c55dc49 merged): 143 total, 141 passed, 0 failed, 2 skipped. The season tests are 12 (SeasonsTests 9 + SeasonsLookTests 3).
Screenshots checked: `Logs/shots/season_*.png`. Four distinct seasons, rain, snowstorm, hail, wind, storm damage (hail/wind/snow/flood) and the Weather app board.

## Known gaps / ideas for later
- `Climate.Seed` is always 1, so every career gets the same weather. Seed it at new-career start (needs a hook in the new-career flow).
- Curing blankets and thaw blankets work through stock and toasts only; there's no in-world model for them yet.
- Storm-damage props (roof tarps, flood sandbags) are small at street distance.
- Bare winter trees cast a solid canopy shadow.
- Winter arrives on career day 241 (about 80 real hours). A calendar-speed option may be wanted.
- The Weather app's IMGUI view has not been seen windowed, only its in-world board (same text).
