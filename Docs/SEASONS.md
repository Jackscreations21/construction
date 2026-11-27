# Seasons, weather and storms (`Scripts/Seasons/`)

A realistic upper-Midwest year (about 43°N — southern Wisconsin / Michigan) on top of the old `Weather` (rain + temperature) and `GameClock`.
The calendar is the job catalog's: **the career starts April 1, 30-day months** (`JobCatalog.Month(day)`), so winter starts on career day 241.

## Pieces
| File | What |
|---|---|
| `Climate.cs` | Monthly normals (highs/lows °F, wet days, daylight hours, wind), deterministic day generator (`Climate.Actual(day)`) with persistent temperature anomalies (cold snaps, heat waves), rain/snow/thunderstorms with start/end hours, severe storms (`StormKind` Hail May–Jul, Windstorm spring/fall, HeavySnow Dec–Mar, FloodRain Apr–Sep; ≥10 days apart, ~5–8 a year), ground state (`GroundAt(day)`: snow depth, frost depth ≈1.2·√freezing-degree-days with snow insulation, soil wetness/mud), and `Forecast(today, day, out pop)` — ~90% right a day out, worse further out, storms appear ~5 days ahead. Career days 1–3 are always fair and mild. `Climate.Seed` (saved) picks the weather. |
| `SeasonWeather.cs` | Static "now" facade: `Season`, `Month`, `Today`, `TempF`, `WindMph`, `GustMph`, `Precipitating/Raining/Snowing/Hailing`, `Cloud`, `StormNow`, `Ground`, `Summary`. Career = the climate; Free Build = the Weather app picker (`FreeMonth`, `FreeSky`, `FreeStorm`; default **June, clear, calm**). `Override` / `GroundOverride` for tests. When a test sets `Weather.ForceRain`, weather is calm and mild (rain as forced) and no rules apply. **Headless (batchmode) Career runs are always calm and fair** unless a test sets `SeasonWeather.BatchSimulate = true` or uses `Override`/`GroundOverride`, so other features' tests never meet cold or storms. Feeds the old `Weather` through `Weather.Provider`. Save key `"seasons"`. |
| `SeasonRules.cs` | The work rules, each returning a plain-language reason (null = OK). **Career only** (`SeasonWeather.RulesOn`: Career, weather on, not Relaxed, not forced). |
| `SeasonDirector.cs` | Per-frame: concrete cure lag, morning briefing toast, storm watch from the forecast, warnings when wind crosses the truss limit / ground freezes or thaws, storm trigger, business ticks. |
| `StormEvents.cs` | `StormEvents` (storm hits → damage on ~16 houses, a 7-day surge of matching catalog repairs at +40% titled "Storm damage: …"; finishing a job clears that house's damage) and `SeasonBusiness` (seasonal pay, demand weights, cold-snap furnace / heat-wave AC emergencies). |
| `WeatherApp.cs` | Tablet app "Weather": now, today's work OK/NO list, 7-day forecast, storm rush, business season; Free Build picker. `BuildBoard()` draws the same text in-world (screenshots — IMGUI doesn't render in batchmode). |
| `SeasonLook.cs`, `WeatherFx.cs`, `StormDamage.cs` | Visuals: season colours (fresh spring, identity summer, fall foliage, bare trees + dormant grass in winter), snow cover, wet look, sun path from daylight length, a flat grey overcast sky (gradient `Skybox/Panoramic` swapped in while raining/snowing/stormy; the procedural sky goes navy or orange instead); vacant-lot meadow tufts follow the grass; snow/hail/wind debris/lightning particles; storm damage props on houses. |

## Rules (real-world numbers)
| Work | Rule | Mitigation |
|---|---|---|
| Concrete order (`ConcreteDispatch.Place`) | No pour in rain/snow. Low in the next 24 h below **40°F** (ACI 306) blocks it unless protected. Never on frozen subgrade. | **Curing blankets** (1 per 150 sq ft ≈ 81 sq ft per yard) or a **propane heater** (heated enclosure), Garden Center → Outdoor. The crew lays them when the slab is poured and takes them back at 50% strength. |
| Concrete cure | Nurse-Saul maturity (datum 14°F, +10°F hydration heat): 35°F air ≈ 0.46× speed; blankets +20°F; heater keeps ≥65°F. Stored as `ConcreteElement.CureLagMinutes`. | blankets / heater |
| Digging (`SiteGround.DigTo/DigBy/DigRect`, crew `DigTask`) | Frozen ground (frost ≥ 2") blocks shovels, the mini excavator and trenching. | 2 **ground thaw blankets**: laid automatically on the next dig attempt, diggable after max(8 h, 1 h per inch of frost), stays thawed 5 days. |
| Roofing (FramingJob steps with shingle/underlayment/dry-in/flashing; repair actions on roofs) | No roofing in rain/snow, wind ≥ 30 mph, snow on the deck; < 40°F shingles: warning to hand-seal. | tarp and wait |
| Trusses / roof framing | Wind ≥ **25 mph** (gusts ≥ 37) stops them; snow too. | wait |
| Exterior paint / stain / caulk (exterior closeup tools, outdoor catalog repairs) | No coatings in rain; paint needs **50°F+**, caulk 40°F+. Siding stops at 35 mph wind. | wait for a warm dry afternoon |
| Crews (`Crew.Speed`) | Heat ≥90°F 0.8×, ≥95°F 0.7×; < 32°F 0.9×, < 20°F 0.8×; mud 0.85×; snow > 3" 0.85×; wind ≥ 25 mph 0.9× (rain's old 0.75× stays). | — |

## Business
- Seasonal pay and demand are off during career days 1–3 (`Climate.GraceDays`). Pay on new offers: summer ×1.10, spring ×1.05, fall ×1.0, winter ×0.90 (each job once; ids saved).
- Demand (catalog `JobDef.Weight`, restored outside Career): winter outdoor ×0.3 / indoor ×1.35; spring outdoor ×1.25; summer outdoor ×1.4 / indoor ×0.9.
- Cold snap (low ≤ 5°F or high ≤ 18°F): an "Emergency: no heat" furnace call at +30%, due tomorrow. Heat wave (high ≥ 90°F): "Emergency" weak-AC call at +25%.
- Storms: Hail → roof leak, shingles, siding, gutters, skylight; Windstorm → shingles, fence, siding, gate, pickets, fascia, soffit (+ fallen trees/limbs in the look);
  HeavySnow → ice-dam roof leaks, gutters, ceiling stains, no-heat; FloodRain → ceiling stains, gutters, roof leaks, walk cracks. 4–5 storm jobs stay on the board for 7 days at +40%.

## Shared-file edits (all one-line, additive)
`Career/Weather.cs` (`Weather.Provider`), `Concrete/ReadyMix.cs` (pour gate), `Concrete/ConcreteElement.cs` (`CureLagMinutes`), `Concrete/SiteGround.cs` (dig gates),
`Crew/CrewTask.cs` (DigTask frozen block), `Crew/Crew.cs` (speed multiplier), `Framing/FramingJob.cs` (`TryAdvance` gate), `Finish/Core/FinishTool.cs` (exterior tool gate),
`Repairs/RepairDefect.cs` (`CanStart` gate). New Garden Center items are appended to `LandCatalog.Items` at runtime.

## Tests
`Tests/SeasonsTests.cs` (climate normals, forecast accuracy, defaults calm, cold pours, frozen ground, rain/wind/heat rules, storm surge + damage, seasonal business,
`season_weather_app.png`) and `Tests/SeasonsLookTests.cs` (`season_spring/summer/fall/winter.png` same street, `season_rain`, `season_storm`, `season_damage`).
