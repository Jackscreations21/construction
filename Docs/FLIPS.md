# Flip houses (`Scripts/Flips/`)

Career, **Contractor stage and up**: buy a run-down house on the town map, fix it yourself or with your crew, add upgrades,
list it and sell it to a buyer. Tablet app **Flips** (group Business, Career only).

## How it plays
1. **For sale tab** — 3 distressed houses on the market (4 from the Crew stage), rotating: each listing stays 10–21 days, new ones
   come up each game day. Only enterable houses (`TownLot.ServiceCalls`: the core town houses and the Birch St map ranches),
   never one with a job on it. Each shows address, sq ft, district, seller (bank REO, estate, tax sale...), asking price,
   **ARV** (after-repair value), the condition report (problems + what buyers knock off for each), dated finishes (upgrades + what
   each adds), the contractor estimate, cash to close, daily holding cost, and a rough profit.
2. **Buy** — all cash, or a **hard-money loan**: 90% of the price, 11.5% interest-only, 2 points. Buyer closing 2%. If cash is
   short and the bank line (Fleet `Finance`) has room, a button borrows the shortfall (`Finance.TakeBankLoan`, 60 days).
   You can carry 1 flip at Contractor, 2 at Crew, 4 at Builder+.
3. **Holding costs** every game day via `Company.RegisterDailyCost`: property tax 1.1%/yr of the price, vacant-dwelling insurance
   $1,900/yr, utilities $6/day, loan interest. All of it (plus purchase, closing, points, crew materials) is tracked in `costSpent`.
4. **The house shows its problems**: knee-high grass and weeds, saplings, a dumped couch/tires/trash bags, plywood over the
   bedroom window, a blue tarp on a leaking roof, a 20-yard roll-off dumpster on the lawn, a yard sign (FOR SALE · BANK OWNED /
   COMING SOON · RENOVATION / FOR SALE · RENOVATED / SOLD). Every problem and dated finish is a **job-catalog defect** in the
   house (`RepairDefect.Create` on a private, never-billed `Job` with id `flip-<id>`), so you walk up to it and work it exactly
   like a service call (same tools, parts from the van, steps, diagnosis). The punch list ticks itself off as defects get fixed.
5. **Crew** — "Send a worker" assigns an idle worker (`Crew.Assign(w, "flip-<id>")`). Each game day each worker puts
   8 h × `Crew.Speed` into the next open item (problems first, matching trade first, then upgrades marked "Crew: yes"); when an
   item's book hours are reached it is played to the end with the job-catalog solver (`JobCatalog.Solve`), workmanship =
   `Crew.Quality`, and its parts are charged. Wages are the normal payroll.
6. **List** at any price. The app suggests the current value and shows the expected days to the first offer: the Development
   market's days-on-market for the price ratio (`Developments.DaysToSell`), ×(1.5 − 0.5×condition) (open problems scare buyers),
   ×(1.15 − 0.3×reputation/100), ±25%. Over 15% above value: no offers. Offers: bidding war over asking when listed ≥5% under
   value, at asking when sharp, else 95–99% of value (+ up to 2% for reputation); good for 3 days; decline → the next buyer later.
7. **Sale** — 5.5% commission + 1.5% seller closing, the hard-money loan is paid off from the proceeds, +1 reputation when every
   problem was fixed. The house goes back to normal with the buyers as its residents. **Sold tab**: purchase → sale, costs, profit.

## Values (real US numbers, small Midwest town, 2026)
- ARV = sq ft × comps $/sq ft (Old Town $150, Birch St school district $162, north $158, east $148, West Side $140)
  × market cycle (`Developments.MarketCycle`, ±7% over 540 days) + $30k lot. A ~1,150 sq ft ranch ≈ $200k.
- Value today = ARV − buyer discount of every open problem − value of every upgrade not done − 4% distress while any
  problem is open, × workmanship (0.96 + 0.04 × average quality).
- Asking price = as-is value × 0.97–1.03.

| Problem (`FlipDefs.Problems`) | catalog defs | buyers knock off |
|---|---|---|
| Overgrown yard + trash-out (every house) | `flip.trash-out` (new) | $4,500 |
| Roof leak | `ext.roof-leak` | $8,500 |
| Damaged siding | `ext.siding` | $3,000 |
| Rotten fascia | `ext.fascia` | $2,500 |
| Failed water heater | `plb.wh-no-hot` | $1,800 |
| Water-stained ceiling | `dw.ceiling-stain` | $1,500 |
| Damaged drywall | `dw.wall-crack` | $900 |
| Water-damaged floor | `task.lvp-living` | $4,000 |
| Leaking gutter | `ext.gutter-leak` | $900 |
| Fence section down | `ext.fence-section` | $1,500 |
| Rotted window sill | `carp.rotted-sill` | $1,200 |
| Smoke alarm missing/chirping | `el.smoke-chirp` | $300 |

Every house: the yard, one of roof / siding / water heater, and 3–4 more. Hidden causes are random per house (diagnose them).

| Upgrade (`FlipDefs.Upgrades`) | catalog tasks | adds |
|---|---|---|
| New kitchen look | `task.cabinets`, `task.kitchen-faucet` | $9,000 |
| Bathroom refresh | `task.vanity`, `task.toilet`, `task.regrout` | $6,500 |
| Fresh interior paint | `task.paint-living`, `task.paint-bedroom` | $3,500 |
| New tile bath floor | `task.bath-floor` | $2,500 |
| Front landscaping | `flip.landscape` (new) | $3,000 |

New catalog tasks (registered by `FlipDefs`, category `task`, weight 0 so the job generator never offers them, validated by
`JobCatalog.Validate`): `flip.trash-out` (walk the yard → bag the junk → pry off the window plywood (pry bar) → grub weeds (edging
spade) → rake → sweep) and `flip.landscape` (cut the bed edge → topsoil → plant boxwoods + hydrangeas → mulch → water).
Models `flip-trash` and `flip-beds` (`CatalogProps.Register`).

## Code
| File | What |
|---|---|
| `FlipDefs.cs` | problem/upgrade library, contractor estimate, book hours, the two new catalog tasks + models |
| `Flips.cs` | state (`FlipHouse`, `FlipItem`), market, buying + loan, defects in the world, crew days, listing/offers/sale, daily costs, save key `"flips"`; `FlipsRuntime` polls the defects once a second |
| `FlipVisuals.cs` | street dressing per house (merged mesh; the dumpster has a box collider) |
| `FlipInterior.cs` | kitchen + bath finishes on owned / for-sale flips, from the `kitchen` / `bath` done flags: dated = orange-stained raised-panel oak fronts, brass hardware, almond laminate counter; almond 4-1/4" tile with a dusty-rose row on the tub walls. Renovated = white shaker fronts, satin-nickel bar pulls, quartz counter; white 3x6 subway in running bond. Overlays a few mm in front of the house's cabinets / counter / tub surround, no colliders |
| `FlipApp.cs` | the tablet app |

API for other features/tests: `Flips.All/Listings/Owned/Get/At`, `RefreshMarket(day)`, `AddListing(lot, seed)`, `Buy`, `BuyBlocker`,
`CashToClose`, `Arv/Value/Condition/RehabEstimate`, `AssignNext/Release/CrewDay`, `List/Unlist/AcceptOffer/DeclineOffer`,
`Step(day)` (the day step without the clock), `ResetState()`, `LiveDefect(f, state)`, `PartsUsed(item)`.

Costs: parts used on a flip's defects (the `used` list each step records) are added to `costSpent` by `Sync`, once per use
(`FlipItem.matsCounted` remembers what is already counted, saved). Crew-finished items bill their book materials less any parts the
player already put in, and the solver's parts are marked counted so nothing is billed twice. RepairUI shows "Flip punch list item
done — see the Flips app" on flip defects and ignores B (flips are never invoiced).

Owned houses keep `LotUse.Customer` (so the town still builds the house); service calls/contractor offers at a flip address are
removed on every `Company.Changed` while the company owns it. `lot.Resident` shows "Vacant (your flip)", then the buyers.

## Tests
`Tests/FlipTests.cs` (Birch St map ranch, the westernmost one): market numbers, buying with the loan + daily costs + offer pruning,
crew + player renovation to ARV then listing, offer, sale and profit, unfixed houses sell for less and slower, parts used count in
the flip's costs once (player and crew), save round trip. Shots `Logs/shots/flip_01..06_*.png` (incl. `flip_03b_dated_bath`,
`flip_05b_renovated_bath`). `CaptureFlipsApp` (category UICapture, windowed only) → `flip_app_market.png`, `flip_app_my_flip.png`.

## Not done / ideas
- Crew work is simulated a day at a time (no worker NPCs walking the flip house).
- Store purchases themselves aren't attributed: parts count when they are used on a flip step (leftovers stay company stock).
- Only the kitchen fronts/counter and the tub walls change look; the vanity, toilet and faucet keep their catalog models.
- Showings/open houses, appraisal gaps, buyer financing falling through, and a seller counter-offer are not modelled.
