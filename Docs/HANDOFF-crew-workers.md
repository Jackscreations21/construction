# Crew stage (`Scripts/Crew/`, branch `claude/crew-workers-ed35ce`)

Hiring workers who do real work on site. Career mode only. No shared files were edited: `Company.cs` is untouched, and per-worker
crew data lives in `Crew.Info(worker)`, keyed by `Worker.Uid`.

## Files
- `CrewTrades.cs`: 10 trades with shirt colors and US daily wages (skill 1 to 5, rounded to 50¢/hr); `Matches(workerTrade, taskTrade)`.
  - Anyone can do laborer work.
  - Carpenters can do roofing.
- `Crew.cs`: the core of the feature.
  - **Hiring**: a labor market of 7 candidates that refreshes on every `Company.DayStarted` and is deterministic per day. `HireCandidate` and `FireWorker` go through `Company.Hire/Fire`.
  - **Payroll**: `"Crew payroll"` is registered with `Company.RegisterDailyCost`. Overtime (to 5:30 PM) pays 2 h at 1.5×.
  - **Morale**: changes with pay vs. the market rate, overtime, rain and idle days, then drifts back toward 0.75. A worker quits below 0.12.
  - **Training**: costs $350 × skill. The worker is in class for the rest of the day and gains +1 skill at the next day start.
  - **Skill model**: `Speed()`, `MistakeChance()` (25/16/8/2.5/0% by skill, +15% on the wrong trade) and `Quality()`.
  - **Off-site jobs**: on `DayStarted`, each active job the player isn't at advances by the summed crew speed ÷ `LaborDays(job)` (about 35% of the payout at $280/worker-day). At 100% it calls `Company.Complete(job, avgQuality)`. `Crew.CanSimulate` excludes House and Development jobs.
  - **Save key** `"crew"`: market, CrewInfo, tasks, job sims and staged piles.
- `CrewTask.cs`: a task queue in `Crew.Tasks`, each task tagged with `JobId`. `Crew.SiteJobId = "site"` means the player's own lot.
  - Each task finds its next `WorkUnit` from the real world state, so the player and the crew can work the same thing, and a reloaded save just carries on.

  | Task | Real API used | Low-skill mistake |
  |---|---|---|
  | `FastenTask` | `Piece.Nails` / `Piece.Fasten` plus `nails16d` stock and `LumberMarks.NailHead`, like `FramingHammer` | drives 1 nail and leaves the piece Loose, which the `site.loose-members` and framing rules flag |
  | `StockTask` | carries units from the delivery pile to a `StagedPile` | none |
  | `FramingJobTask` | `FramingJob.TryAdvance(tool)`, the player's click: wall from layout, floor, caps, wall/roof sheathing, roofing | one fresh piece left Loose / under-nailed |
  | `DrywallTask` | `Drywall.NextPlan` / `HangSheet`, `CutOutBoxes`, `ScrewLine`, `CoatAll` ×3 (waits for mud to dry), `SandAll`; ceiling first | skips one stud line of screws, which `dw-screws` flags |
  | `PaintTask` | `Drywall.ApplyTexture`, `PaintJob.Prime`, `PaintJob.Paint` ×2 (waits for dry times) | none |
  | `DigTask` | `SiteGround.DigBy` in 2" bites to benchmark − depth | stops 2–4" shallow; new Footing rule `crew.trench-depth` measures the pad |
  | `SodTask` | `Landscape.LaySod` per roll, then `LandscapeSystem.WaterAround` | skips a roll |

- `CrewWork.cs`:
  - pile lookup and nail marks
  - `CrewDefect` log (UI only; the defects themselves are world state)
  - the `crew.trench-depth` inspection rule
  - `StagedPile`: material stacked where it's needed. It is still counted in `Game.Stock` and shrinks when stock is used.
- `CrewNpc.cs`: the worker model is the inspector's build, plus:
  - a trade-colored shirt, with a hi-vis vest for laborers
  - a hard hat (white for leads and masters, blue for electricians, yellow for everyone else)
  - a leather tool belt
  - a hip pivot, so the worker can bend and kneel

  Behavior:
  - **Steering**: no NavMesh. The worker walks straight at the target, side-steps obstacles found by a knee-high ray, keeps apart from other workers, and teleports after 5 s stuck.
  - **Ground**: walks on the ground, a slab or a deck, but not on top of walls.
  - **Carrying**: lumber on the shoulder, sheets on edge, other items in front.
  - **Tools and animation**: shows the tool and animation per job: hammer, drill, saw, level, roller, knife, sander, shovel, hose, kneel, place. Sounds come from `Sfx`.
  - **Work time**: `unit.Seconds / Crew.Speed`. Then the unit's `Do` runs; `Sloppy` is rolled from a per-worker seeded RNG.
  - **Fetching**: skips piles more than 50 m from the work.
- `CrewManager.cs`:
  - Spawns the crew assigned to the current job ("here") during work hours. "Here" comes from `HereJobResolver`; by default it is the nearest `ActiveJob.WorldPos` within 45 m of the player, else `"site"`.
  - Sends workers home after hours, hands out tasks (own trade first) and sets `RainedOnCrew`.
  - **K key**: a point-and-assign menu that opens with `Game.UIOpen`. It offers build the aimed layout, fasten, drywall/paint this room, a trench (two K presses), a footing pad, sod, or stock the selected material here.
- `CrewApp.cs`: the "Crew" tablet app, with tabs:
  - **Crew**: assign, overtime, raise, train, fire, current activity, mistakes, off-site progress
  - **Hire**
  - **Tasks**: per job; add, redo, remove

## Hooks for other features
- **Town**: set `CrewManager.HereJobResolver`, so the crew shows up at the job the player drove to.
- **Development**: queue `FramingJobTask.For(job)` and the other tasks with `JobId = job.Id`.
- **Spawn point**: `CrewManager.ArrivalOverride` sets where workers arrive; the default is the delivery drop point.

## Tests (`Tests/CrewTests.cs`, area around (420, 0, 420))
- Hiring, payroll on a new day, the Crew stage, and training.
- A carpenter fastening 8 loose 2x4s with real nails.
- A carpenter and a laborer framing a 10' wall with a window from its layout, until it is braced.
- A skill-1 laborer missing nails, the inspection failing, then a redo by a skilled worker passing it.
- A save round-trip.

Screenshots: `Logs/shots/crew_fastening.png`, `crew_framing_wall.png`, `crew_wall_done.png`.

## Not done / next
- Drywall, paint, dig and sod tasks have no PlayMode test of their own yet. They use the APIs their trades' tests exercise.
- Only one worker works a task at a time; others on the job take other tasks. Big tasks could be split for helpers.
- Delivery piles still draw all stock, including units also shown on staged piles.
- There is no climbing or ladders: workers do high work from the floor.
