# Subcontractors (`Scripts/Subs/`)

Career, from the **Contractor** stage: hire a local trade company to do one trade on the job you're standing on,
instead of doing it yourself or sending your own crew.

## How it plays
1. Tablet → **Subcontractors** (Business group). Pick a trade: excavation, concrete, framing, roofing, plumbing,
   electrical, HVAC, insulation, drywall, painting, flooring, landscaping. Each trade has three local companies:
   - a cheap outfit (`$`): books in 1-2 days, poor/fair work, often late
   - a standard shop (`$$`): good work, usually on time
   - a premium company (`$$$`): excellent work, always on time, booked 5-7 days out
2. **Request quote**: the sub measures the work actually left on the site, prices it at real US labor-only sub
   rates, and shows what your own crew would cost in wages for the same work. Subs always cost more.
3. **Accept**: the sub is booked after its lead time. Nothing is paid up front.
   - On the start day (7:00 AM) their truck pulls up, with the company name and phone on 24"x18" door signs.
   - A crew of 2-3 gets out and does the work visibly through the same task machinery as your own workers.
   - They work 7:00-3:30 and come back each day until done.
   - They are not your employees: never on payroll, never in the Crew app.
4. **Invoice** when the work is done, paid from your bank account.
   - If you can't pay, the invoice stays open and adds a 1.5% late fee every day.
5. **Quality and reliability matter**.
   - The quality stars are the skill of the hands they send, which drives the crew mistake model:
     missed nails, a stud line of drywall screws skipped, unstapled batts, unmade joints, grounds not spliced, shallow trenches.
   - Those defects are real world state, so the normal inspections fail.
   - **Call back to fix (warranty)** brings them back the next day with their foreman to fix it, free.
   - Unreliable subs call to say they're running late (1-3 days).
6. **Repeat business**: every finished job with a company takes 2% off their next price, up to 10%.

## Pricing (standard shop; cheap ×0.80-0.85, premium ×1.28-1.35)
| Trade | Unit | Rate | Minimum | Measured from |
|---|---|---|---|---|
| Excavation | cu yd | $95 | $450 | 12"x12" footing trench under each side of the formed slab (on the building pad) |
| Concrete | cu yd | $165 (+ ready-mix $165/yd billed to you) | $850 | the active form's takeoff |
| Framing | sq ft | $8 | $1,500 | framing layouts (FramingJob) left: plan area × steps remaining |
| Roofing | square | $185 | $900 | dry-in / shingle layouts left |
| Plumbing | joint | $38 | $950 | supply / drain / vent joints not made |
| Electrical | box | $85 | $850 | boxes not made up or without a device |
| HVAC | joint | $32 | $1,200 | duct / line-set joints not sealed |
| Insulation | sq ft | $0.55 | $450 | open stud bays in exterior walls |
| Drywall | sq ft | $1.95 | $900 | sheets left to hang in rooms (ceilings found overhead) + hung but unfinished faces |
| Painting | sq ft | $1.40 | $600 | hung drywall surfaces without 2 finish coats |
| Flooring | sq ft | $3.25 | $700 | subfloor rooms without finish flooring (LVP) |
| Landscaping | sq ft | $0.55 | $400 | staked landscape jobs left |

Subs bring labor and tools; materials come from your site stock, the same as your own crew.

## Code
| File | What |
|---|---|
| `SubCatalog.cs` | trades (unit, rate, minimum, own-crew productivity, crew trade, truck type, crew size) and 36 companies |
| `Subs.cs` | quote / accept / cancel, schedule and late roll, daily tick, finish + invoice, unpaid late fees, callback, inspection hook, save key `"subs"` |
| `SubCrews.cs` | per contract: temporary `Worker`s + `CrewNpc`s (`External`), the truck, the tasks; each worker owns a slot job id `sub:<contract>:<n>`; idle hands take unclaimed tasks |
| `SubScopes.cs` | per trade: `Measure` (remaining work), `Tasks` (crew tasks), `FixTasks` (callback) |
| `SubTasks.cs` | new crew tasks: insulate, staple fix, drywall re-screw, electrical make-up, MEP joints, landscape job, LVP floor, concrete pour + finish |
| `SubVisuals.cs` | door signs |
| `SubsApp.cs` | the tablet app |

Existing crew tasks reused: `FramingJobTask`, `DrywallTask`, `PaintTask`, `DigTask`, `FastenTask`.

Test hooks: `Subs.IgnoreWorkHours` (no lead time, any hour), `Subs.ForceOnTime`.

Tests: `Tests/SubcontractorTests.cs`, around (-1600, 0, 1500). Shots:
- `subs_crew_working`
- `subs_finished_wall`
- `subs_app` (windowed run only)
