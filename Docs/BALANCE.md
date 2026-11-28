# Career economy balance (balance pass, 2026-11-27)

All numbers come from the live data and are recomputed by `Assets/Groundbreak/Tests/BalanceTests.cs`, which writes
`Logs/balance_report.txt` and the cash-over-time chart `Logs/shots/balance_pacing.png` (stage changes as coloured lines:
green Contractor, blue Crew, orange Builder, purple Developer).

Time base: one game day = one calendar day of a 360-day career year (30-day months). Wages, rent, insurance, property tax
and equipment rental are per game day. Loans are the one stylised exception: `Finance` charges APR/52 per day
("a 60-day loan behaves like a 60-week note"), so loan terms are short but interest per day is higher than real.

## The "decent player" model (BalanceTests.RunPacing)
- Handyman: 2 service calls a day at 90% satisfaction (the board holds 3).
- Contractor: contractor jobs the pickup can do (deck, fences), finished on their scheduled days, plus 1 service call a day.
  Moves up once cash covers the bay move-in + a used pickup's 20% down payment + a week of overhead (no bank loan).
- Crew: hires a laborer and a carpenter (skill 2) once a week of payroll is in the bank; they run contractor jobs off site.
- Builder: Yard + shop at reputation 65 with the working capital for a client ranch; 4 workers; one house at a time.
- Developer: HQ + 8 workers at reputation 80.
The model never idles, never fails an inspection and never travels, so its days are the fast end; real play is slower.

## Key numbers per stage

| Stage | Fixed costs / day | Main income | Net / day (model) | Net / week | Reached (model) |
|---|---|---|---|---|---|
| Handyman | van $17 | service call: invoice avg $203, parts $22, $212 net each | $406 | $2,844 | start, $2,500 cash |
| Contractor | bay $97 (rent $70, insurance $12, utilities $15), used pickup $15 + loan $177, van $17 | contractor jobs $917/day net of materials + 1 call | $823 (winter, calls only: $75) | $5,758 | day 19 |
| Crew | + payroll laborer $152, carpenter $200 | each worker nets $150-225/day on off-site contractor jobs | $1,169 | $8,181 | day 26 |
| Builder | yard + shop $235/day, 4 workers | client houses at 13-35% margin + crew jobs | $2,577 | $18,042 | day ~54 |
| Developer | HQ $515/day, 8 workers | spec houses 6-23% profit each, client houses, crew jobs | $4,190 | $29,332 | day 111 (was ~85 with a $60k HQ) |

Other checks (all in BalanceTests):
- Service call average invoice $120-500 (is $203). Contractor jobs pay 25-85% over materials (fences ~55%, decks ~71%).
- Builder contract margin (client pays deposit + draws vs the plan's build cost) 10-36% for every catalog build and Career contract.
- Spec houses (fresh street, average land, 14 days on market): -2% to 30% profit; finish level can't add more than 6% of value.
  starter-900 6%, ranch-1600 16%, twostory-2400 19%, estate-3500 23%.
- Flip (1,150 sq ft, ARV $202,500, a bad house + every upgrade): fixing it yourself 10-15% of ARV, all contractors still +$13-22k,
  buying and relisting untouched loses $16-26k (no exploit).
- Subs: even the cheapest sub with the 10% repeat discount costs more per unit than your own journeyman's wages, never more than 6x.
- Equipment rent vs buy (used): mini excavator 127 rental days, boom lift 117, skid steer 116, crane truck 70 (45-400 allowed).
- Loans cost money: $10k bank loan = $290 interest over 30 days, $1,160 over 120 (under 30% of principal).
- A brand-new handyman's bank line ($4,000) + $2,500 cash can't buy the Contractor stage on day one ($7,900 needed).
- Crew off-site job: materials are charged; your own carpenter on a house earns back ~$242/day of sub labor (wage $240).
- Reputation: jobs under $1,000 give at most +1 (a perfect $6,000 job +3; a botched job still -3).

## What changed (old -> new) and why

| File | Value | Old -> new | Why |
|---|---|---|---|
| `Scripts/Yard/Yard.cs` | Rented bay rent / insurance per day | $340 / $35 -> $70 / $12 | Was ~5x real ($124k/yr for a 1,500 sq ft flex bay). Real 2026 Midwest: ~$2,100/mo + GL/property ~$4.3k/yr. At $390/day a contractor with no job that week lost money on service calls alone (trap). |
| `Scripts/Yard/Yard.cs` | Yard + shop rent / insurance | $950 / $70 -> $180 / $25 | 2,400 sq ft shop on a 0.7 ac fenced yard ~$5,400/mo. |
| `Scripts/Yard/Yard.cs` | Headquarters rent / insurance / move-in | $2,400 / $140 / $48,000 -> $420 / $50 / $60,000 | 7,200 sq ft + office on 1.7 ac ~$12,600/mo; move-in = 2 months deposit + first month + office fit-out. The higher move-in keeps Developer later after rent dropped. |
| `Scripts/Yard/Yard.cs` (pacing pass, 2026-11-28) | Headquarters move-in | $60,000 -> $100,000 | Perfect play reached Developer on day ~85, a bit fast. Move-in = 2 months deposit + first month (~$38k) + office/dock fit-out and racking (~$62k). First set to $125,000 (day 111 alone), then lowered to $100,000 once the fence-post, gravel and delivery-fee realism fixes were merged, since those slow the model too (day 122 at $125,000). The model should reach Developer around day 110 (Builder still day 54); `Pacing_DecentPlayer_HitsStageTargets` asserts Developer in days 100-120. Tutorial text updated to $100,000. |
| `Scripts/Equipment/Equipment.cs` (2026-11-28) | Rental billing | daily rate every day -> each 7-day block capped at the weekly rate | The weekly rate was shown but never billed. The rent-vs-buy check above still uses the daily rate (short rentals); on week-long rentals buying takes longer to pay off. |
| `Scripts/Fleet/Finance.cs` | Bank credit cap at Handyman | $15,000 -> $10,000 | A new company could borrow ~$6.5k on day 1 and skip the Handyman stage entirely. |
| `Scripts/Business/Company.cs` (shared Core, additive) | `Complete`: rep gain for jobs paying < `SmallJobPayout` ($1,000) | up to +3 -> up to +1 | Three perfect $150 calls a day took a handyman from 50 to 70 reputation in ~4 days. Bad work still costs the full -3. |
| `Scripts/Crew/Crew.cs` | Off-site crew jobs (`OnDayStarted`) + `JobSim.MaterialsCharged` | free materials -> job's `MaterialBudget` charged pro rata with progress ("Materials (crew job)") | Exploit: the payout includes materials (35-45% of a fence) but the simulation never bought any, so each worker netted ~$470/day. If you can't pay, the crew waits that day. |
| `Scripts/Balance/CrewLaborCredit.cs` (new) | Own crew on a house | wages only -> daily credit of the sub labor they did | Trap: house phases are funded with full sub labor; your crew only added speed, so every worker on a house was pure loss. Credit = phase labor x crew share of today's progress; bounded by the phase labor. |
| `Scripts/JobCatalog/DefsContract.cs` | `bld.pole-barn` $/sq ft | 45 -> 52 | Lost money (-2%) on polebarn-30x40. |
| same | `bld.diner` | 260 -> 345 | Lost 13% (a 3,000 sq ft diner costs $897k to build). |
| same | `bld.retail` | 190 -> 215 | 2% margin on the strip mall. |
| same | `bld.estate` | 225 -> 190 | 44% margin (exploit-level) on estate-3500. |
| same | `bld.colonial` | 180 -> 175 | 30-36% on the two-story plans. |
| `Scripts/Career/Career.cs` | Custom home contract price | $720,000 -> $660,000 | 39% margin on estate-3500 ($438k build cost); now 35%. |
| `Scripts/BusinessTutorials/BusinessTutorialContent.cs` | tutorial text only | "$375 a day" -> "$82 a day (rent $70 + insurance $12)", "$15k as a Handyman" -> "$10k", custom home "$720,000" -> "$660,000", HQ "$48,000" -> "$60,000" | Text quotes changed numbers (pictures not regenerated). |

## Chosen not to change
- **Starting cash $2,500, service rates ($85/h, $65 trip, parts +20%), wages, vehicle prices, sub rates, equipment rates,
  flip numbers, development land/site/permit numbers, seasonal pay**: already real US figures and inside every check above.
- **Loan math (APR/52 per game day)**: stylised but documented in `Finance`; loans cost 3-12% of principal over their short terms,
  which keeps them a tool and not free money. Changing it would reprice every existing save's loans.
- **Stage thresholds in `Company.RecomputeStage` (rep 65/80, 2/8 workers, warehouse levels)**: the model hits the pacing target with
  them; money, not reputation, is what gates each step now.
- **House takeoff costs ($116-142/sq ft for houses)**: cheap for high-end finishes, but tuning them moves every spec margin and the
  Blueprint/Development tests' $/sq ft bands; the contract prices were tuned instead.
- **Crew off-site jobs already in progress in an old save** are charged their materials for the progress so far on the next day.
