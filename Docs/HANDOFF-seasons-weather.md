# Handoff — seasons-weather (paused for the night, 2026-11-27)

Branch `claude/happy-hopper-4811ae`, worktree `.claude/worktrees/happy-hopper-4811ae`. Read `Docs/SEASONS.md` first (design, rules, API, shared-file edits).
Reports go to the lead `local_98a2032f-a420-4a0a-9364-69a1031cb23c`. **Start Unity only through the shared lock script**:
`bash ".../side-session-notes/local_54073d16-4b24-4e7f-9bca-a4603ff30909/unity-run.sh" <Unity args>` (run in the background; one Unity at a time across all sessions).

## Done (committed as WIP)
- `Scripts/Seasons/`: Climate, SeasonWeather, SeasonRules, SeasonDirector, StormEvents (+SeasonBusiness), WeatherApp, written by the main session.
  SeasonLook, WeatherFx, StormDamage, written by a visuals subagent.
- The visuals subagent's first filtered run (`SeasonsLookTests`, 21:14) compiled with **0 error CS** and wrote `Logs/shots/season_*.png`.
  Nobody has looked at those shots yet. The subagent was iterating on the visuals and was stopped for the pause while its second run waited for the lock.
  Its last edits to SeasonLook/WeatherFx/StormDamage/SeasonsLookTests have **not been compiled**.
- Nine one-line additive shared-file edits, listed in `Docs/SEASONS.md`.

## Left to do, in order
1. Read SeasonLook.cs / WeatherFx.cs / StormDamage.cs / SeasonsLookTests.cs and check they compile by reading them carefully (the subagent never wrote its report).
2. Merge main (aa6ed35 or later: map-followup added `Scripts/MapFollowup` vacant-lot dressing). Make sure season tints and snow also reach the
   vacant-lot meadow and for-sale dressing materials (SeasonLook finds materials by name, so check the new ones' names).
3. Filtered run through the lock: `-testFilter "Groundbreak.Tests.SeasonsTests;Groundbreak.Tests.SeasonsLookTests"`. Fix failures.
   Neither `SeasonsTests.cs` (main session) has run yet, nor the Weather app board shot `season_weather_app.png`.
4. LOOK at every `Logs/shots/season_*.png`:
   - The same street must clearly read as spring, summer, fall and winter (bare trees, snow).
   - `season_rain` must look wet.
   - The storm shots and the damage shots (tarps, fallen limb, fence, sandbags) must show the storm and its damage.
   - `season_weather_app` text must be legible.
   Iterate.
5. Full PlayMode suite through the lock (once). Things to watch:
   - `JobCatalogTests`: in Career without ForceRain, SeasonBusiness rescales `JobDef.Weight`. Spring outdoor work ×1.25 could upset weight assertions;
     if it does, apply weights only after `Climate.GraceDays`, or restore them on `JobCatalog.ResetState`.
   - Career screenshots now get the spring tint, which is expected.
   - Default Free Build must look unchanged: June = identity tint.
6. Commit. `git checkout -- Assets/Groundbreak/Resources/Tutorials` and ProjectSettings noise before committing.
   Update SEASONS.md if things changed, then SendMessage the lead "seasons-weather DONE @ <commit>" with the branch and test counts.

## Known gaps / ideas
- `Climate.Seed` is always 1, so every career has the same weather. To vary it, set it at new-career start; that needs a hook in the new-career flow.
- Curing blankets and thaw blankets have no in-world model yet; they work through stock and toasts only.
- Winter arrives on career day 241, about 80 real hours in. Players may want a calendar-speed option.
