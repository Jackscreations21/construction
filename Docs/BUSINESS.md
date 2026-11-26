# Groundbreak business game (user request, 2026-11-26)

> "Turn it into a full game where you make money. Start by doing repairs out of a van, then you can get a warehouse and
> trucks for certain jobs, then you can hire workers and eventually build full houses or neighborhoods."

Career mode becomes a business sim on top of the existing realistic building systems. Free Build is unchanged.

## Progression (`CompanyStage` in `Scripts/Business/Company.cs`)
| Stage | You have | Work you can take | Typical pay |
|---|---|---|---|
| 0 Handyman | a used work van, basic hand tools, ~$2,500 | service calls / repairs at customers' houses | $80 – $1,500 |
| 1 Contractor | warehouse (rented bay) + a truck (pickup/flatbed/dump) | remodels, decks, fences, sheds, garages, concrete flatwork | $2k – $50k |
| 2 Crew | 2+ hired workers | several jobs at once; workers do assigned tasks | same, in parallel |
| 3 Builder | yard + shop (warehouse level 2), rep 65 | full custom houses for clients (existing Career contracts) | $150k – $700k |
| 4 Developer | HQ (level 3), 8+ workers, rep 80 | buy land, subdivide, build spec houses and neighborhoods, sell them | $M |

Stage is derived by `Company.RecomputeStage()` from what the company owns; it never goes down.
Money = `Game.Wallet`. Reputation = `Career.Reputation` (`Company.Reputation` wraps it). Recurring costs via `Company.RegisterDailyCost`.

## Shared core (already on main — do not rewrite, extend additively and say so)
`Company` (static): `Stage`, `HasWarehouse`, `WarehouseLevel`, `Vehicles` (`OwnedVehicle`), `Workers` (`Worker`), `Offers` / `ActiveJobs` (`Job`),
`Accept(job)`, `Blocker(job)`, `Complete(job, quality)`, `AddVehicle`, `Hire`, `Fire`, `RegisterDailyCost`, events `Changed`, `StageChanged`,
`JobAccepted`, `JobCompleted`, `DayStarted`. Saved under SaveSystem key `"company"`. `Job.Payload` holds feature-specific JSON.

## Feature ownership (one folder each, own tests, own tablet apps)
- **`Scripts/Town/` + `Scripts/Repairs/` (session town-repairs)**: the town map (streets, customer houses with addresses, hardware store, the player's
  home driveway where the van starts), a drivable work van with limited cargo (shelving, ladder rack) + fast travel, and the repair-job loop:
  generated service calls (`JobKind.Repair`) with real defects to diagnose and fix using the existing trade systems, customer satisfaction, invoice.
  Owns `Town` (public API: `Town.Addresses`, `Town.LotAt`, `Town.HQPosition`, `Town.FreeLots`) — others build on it.
- **`Scripts/Fleet/` + `Scripts/Yard/` (session yard-fleet)**: buying/renting the warehouse and its upgrades (racking, shop tools, material storage that
  replaces on-site piles, office), vehicle catalog and dealership (van, pickup, flatbed, dump truck, box truck, boom truck, skid steer + trailer),
  vehicle capacity/requirements for jobs, fuel/maintenance/insurance, loans and the company finance app (P&L, daily costs), Contractor-tier jobs
  (`JobKind.Remodel`/`Outbuilding`): deck, fence, shed, garage, driveway/patio pour — verified by the existing inspection rules.
- **`Scripts/Crew/` (session crew-workers)**: hiring market, wages, skills/trades, worker NPCs (procedural models) that visibly perform assigned tasks
  using the same piece/state APIs the player uses (place + fasten pieces from a plan, carry materials, pour/finish, hang drywall...), task queue and
  assignments app, morale, training, payroll via `RegisterDailyCost`, multi-job scheduling.
- **Phase 2, after the three above merge — `Scripts/Development/`**: Builder + Developer tiers: client house contracts moved onto town lots, buying
  land, subdivision layout (streets, utilities, lots), spec houses from plans built by crews, home sales/market, neighborhoods; then tutorials for
  the whole business loop and a balance pass.

## Rules (in addition to CLAUDE.md)
- Career mode only; Free Build keeps everything unlocked. New game in Career starts at Handyman with the van.
- Realism still matters: real repair procedures, real vehicle payloads/bed sizes, real wages and prices (US), real job durations scaled by the game clock.
- Tablet apps via `Tablet.RegisterApp`. All persistent state via `SaveSystem.RegisterExtension`. Tests under `Assets/Groundbreak/Tests/` with screenshots.
- Tests share one world: put test content far from the origin and from other features; restore state in teardown (`Company.ResetState()`).
