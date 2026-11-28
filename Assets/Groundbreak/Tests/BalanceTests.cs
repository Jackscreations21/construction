using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>
    /// Balance pass (Docs/BALANCE.md): headless economics from the real catalog and price data. A "decent player" pacing model
    /// (service calls → warehouse + pickup → two hires → yard + shop and client houses → HQ + 8 workers) is run day by day from the
    /// live numbers (job catalog, fleet, yard, wages, loans, plans) and must hit the pacing target; every stage must make money,
    /// and each feature is checked for exploits and traps: builder margins, spec houses, flips, subs vs own crew, equipment
    /// rent vs buy, loans, credit line, crew off-site materials, crew labor credit on houses, small-job reputation.
    /// Writes Logs/balance_report.txt and the chart Logs/shots/balance_pacing.png.
    /// </summary>
    public class BalanceTests
    {
        // ---- what a "decent player" does (assumptions, documented in Docs/BALANCE.md)
        const float HandymanCallsPerDay = 2f;      // service calls finished per game day as a Handyman (board holds 3)
        const float SideCallsPerDay = 1f;          // service calls alongside contractor work
        const float PlayerQuality = 0.9f;          // workmanship / satisfaction
        const float ContractorPace = 1.0f;         // contractor jobs finished on their scheduled days
        const int BufferDays = 7;                  // cash kept for a week of overhead before growing

        int _money, _rep, _day; GameMode _mode; bool _unlimited;
        readonly StringBuilder _report = new StringBuilder();

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(2);
            _money = Game.Wallet.Money; _rep = Career.Reputation; _mode = GameSettings.Mode; _unlimited = GameSettings.UnlimitedMoney;
            if (GameClock.I != null) _day = GameClock.I.Day;
            Weather.ForceRain = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Crew.ResetState(); Company.ResetState(); Finance.ResetState();
            Game.Wallet = new Wallet(_money);
            Career.Reputation = _rep; GameSettings.Mode = _mode; GameSettings.UnlimitedMoney = _unlimited;
            Weather.ForceRain = null;
            if (GameClock.I != null) GameClock.I.Day = _day;
            if (_report.Length > 0)
            {
                string dir = Path.Combine(Application.dataPath, "..", "Logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "balance_report.txt"), _report.ToString());
                Debug.Log("[Balance]\n" + _report);
                _report.Clear();
            }
            yield return Frames(2);
        }

        void Log(string s) => _report.AppendLine(s);

        // ================================================================== economics from the real data

        static float PayFactor(float q) => Mathf.Lerp(0.6f, 1.1f, Mathf.Clamp01(q));

        /// <summary>Average Handyman service call: (invoice, our parts cost), weighted by job rarity and cause likelihood.</summary>
        public static (float quote, float parts) ServiceAverage(int rep = 50)
        {
            float wq = 0f, wp = 0f, w = 0f;
            foreach (var d in JobCatalog.All)
            {
                if (d.Mode != JobMode.Service || JobCatalog.IsLegacy(d) || JobCatalog.IsTask(d) || d.Stage != CompanyStage.Handyman || d.MinRep > rep || d.Weight <= 0f) continue;
                float cw = 0f, q = 0f, p = 0f;
                foreach (var c in d.Causes)
                {
                    if (c.MinRep > rep) continue;
                    float mc = d.MaterialCostFor(c);
                    q += c.Weight * (c.Hours * RepairJobs.LaborRate + mc * RepairJobs.Markup + d.Callout + RepairJobs.TripCharge);
                    p += c.Weight * mc; cw += c.Weight;
                }
                if (cw <= 0f) continue;
                wq += d.Weight * q / cw; wp += d.Weight * p / cw; w += d.Weight;
            }
            return w > 0f ? (wq / w, wp / w) : (0f, 0f);
        }

        /// <summary>What one service call leaves in the bank (pay scaled by satisfaction, a tip at 90%+, less parts).</summary>
        public static float ServiceNet(float q)
        {
            var (quote, parts) = ServiceAverage();
            float tip = q >= 0.9f ? Mathf.RoundToInt(quote * 0.1f / 5f) * 5 : 0f;
            return quote * PayFactor(q) + tip - parts;
        }

        /// <summary>Insurance + maintenance + fuel for one vehicle on a working day (as Fleet charges it).</summary>
        public static float VehicleDaily(string typeId, float condition = 1f)
        {
            var t = FleetCatalog.Get(typeId);
            float fuelGal = t.Machine ? 3f : t.Mpg > 0 ? 30f / t.Mpg : 0f;
            return t.InsurancePerDay + t.MaintenancePerDay * Mathf.Lerp(1.8f, 1f, condition) + fuelGal * Fleet.FuelPrice;
        }

        public static float YardDaily(int level) { var l = Yard.Levels[level]; return l.RentPerDay + l.InsurancePerDay + 15 * level; }

        static float UsedTruckPayment(string typeId, int term) =>
            Finance.PaymentFor(Fleet.Price(typeId, true) - Fleet.DownPayment(typeId, true), Finance.VehicleAprUsed, term);

        static List<Job> _pickupJobs;
        /// <summary>Real contractor offers (built by the job catalog) a company with a pickup can take at reputation ≤ 30.</summary>
        public static List<Job> PickupJobs()
        {
            if (_pickupJobs != null) return _pickupJobs;
            _pickupJobs = new List<Job>();
            int seed = 101;
            foreach (var d in JobCatalog.All)
            {
                if (d.Mode != JobMode.Fleet || d.Stage != CompanyStage.Contractor || d.Vehicle != "pickup" || d.MinRep > 30) continue;
                var j = JobCatalog.CreateFleet(d, seed++, false);
                if (j != null) { j.DueDay = d.DaysToBuild; _pickupJobs.Add(j); }   // DueDay reused here as "scheduled days"
            }
            return _pickupJobs;
        }

        /// <summary>Net $ per game day the player makes building contractor jobs (pay at the player's quality, less materials).</summary>
        public static float ContractorJobNetPerDay()
        {
            float net = 0f, days = 0f;
            foreach (var j in PickupJobs()) { net += j.Payout * PayFactor(PlayerQuality) - j.MaterialBudget; days += Mathf.Max(1, j.DueDay) * ContractorPace; }
            return days > 0f ? net / days : 0f;
        }

        static Worker MakeWorker(string trade, int skill) =>
            new Worker { Uid = "bal-" + trade + skill, Name = "Balance " + trade, Trade = trade, Skill = skill, DailyWage = CrewTrades.Wage(trade, skill), Morale = 0.75f };

        /// <summary>Net $ per day one worker adds running contractor jobs off site (Crew's day simulation: pay at crew quality, less the
        /// job materials now charged as they go, less the wage).</summary>
        public static float CrewWorkerNetPerDay(Worker w)
        {
            float payout = 0f, mats = 0f, laborDays = 0f;
            foreach (var j in PickupJobs()) { payout += j.Payout; mats += j.MaterialBudget; laborDays += Crew.LaborDays(j); }
            if (laborDays <= 0f) return 0f;
            float perLaborDay = (payout * PayFactor(Crew.Quality(w, null)) - mats) / laborDays;
            return Crew.Speed(w, null) * perLaborDay - w.DailyWage;
        }

        /// <summary>Builder contract margin: everything the client pays (deposit + draws, final at quality q) less the plan's build cost.</summary>
        public static float ContractMargin(int price, HousePlan plan, float q = PlayerQuality)
        {
            float paid = price * (Builder.DepositPct + Builder.DrawPct[0] + Builder.DrawPct[1] + Builder.DrawPct[2] + Builder.DrawPct[3] + Builder.DrawPct[4] * PayFactor(q));
            return (paid - plan.Takeoff.Total) / price;
        }

        /// <summary>Most cash a client house ties up before the next draw comes in (deposit, phase paid at start, draw on inspection).</summary>
        public static int HouseWorkingCapital(int price, HousePlan plan)
        {
            float cash = price * Builder.DepositPct, low = 0f;
            for (int i = 0; i < 5; i++)
            {
                cash -= plan.Takeoff.PhaseCost((BuildPhase)i); low = Mathf.Min(low, cash);
                cash += price * Builder.DrawPct[i];
            }
            return Mathf.RoundToInt(-low);
        }

        static int BuildPrice(JobDef d, HousePlan plan) => Mathf.RoundToInt(plan.SqFt * d.PricePerSqFt / 1000f) * 1000;

        // ================================================================== the pacing model

        public class Pacing
        {
            public int Contractor = -1, Crew = -1, Builder = -1, Developer = -1;
            public float HandymanNet, ContractorNet, CrewNet, BuilderNet, DeveloperNet, WinterContractorNet;
            public readonly List<int> Cash = new List<int>();
        }

        /// <summary>Day by day: cash, reputation and stage of a decent player, from the live numbers. No bank loans (conservative).</summary>
        public static Pacing RunPacing(int maxDays = 720)
        {
            var r = new Pacing();
            float service = ServiceNet(PlayerQuality);
            int repService = Mathf.Min(1, Mathf.RoundToInt(Mathf.Lerp(-3f, 3f, PlayerQuality)));           // small-job cap
            int repJob = Mathf.RoundToInt(Mathf.Lerp(-3f, 3f, PlayerQuality));
            var jobs = PickupJobs();
            float avgDays = 0f; foreach (var j in jobs) avgDays += Mathf.Max(1, j.DueDay) * ContractorPace; avgDays /= Mathf.Max(1, jobs.Count);
            float jobNet = ContractorJobNetPerDay();

            float van = VehicleDaily("van");
            float pickup = VehicleDaily("pickup", Fleet.UsedCondition) + UsedTruckPayment("pickup", 84);
            var crew2 = new[] { MakeWorker("laborer", 2), MakeWorker("carpenter", 2) };
            var crew4 = new[] { MakeWorker("laborer", 2), MakeWorker("carpenter", 2), MakeWorker("carpenter", 3), MakeWorker("concrete", 2) };
            float crewNet2 = 0f, crewNet4 = 0f, payroll2 = 0f, payroll4 = 0f;
            foreach (var w in crew2) { crewNet2 += CrewWorkerNetPerDay(w); payroll2 += w.DailyWage; }
            foreach (var w in crew4) { crewNet4 += CrewWorkerNetPerDay(w); payroll4 += w.DailyWage; }
            float avgLabor = 0f; foreach (var j in jobs) avgLabor += Crew.LaborDays(j); avgLabor /= Mathf.Max(1, jobs.Count);
            float crewSpeed2 = 0f, crewQ2 = 0f; foreach (var w in crew2) { crewSpeed2 += Crew.Speed(w, null); crewQ2 += Crew.Quality(w, null) / crew2.Length; }
            float crewRep2 = crewSpeed2 / Mathf.Max(0.5f, avgLabor) * Mathf.RoundToInt(Mathf.Lerp(-3f, 3f, crewQ2));   // crew jobs finished per day x rep each

            // a client house at the Builder stage: the ranch at the catalog's $/sq ft
            var ranchDef = JobCatalog.Get("bld.ranch"); var ranch = JobCatalog.PlanFor(ranchDef);
            int ranchPrice = BuildPrice(ranchDef, ranch);
            float houseNet = ContractMargin(ranchPrice, ranch) * ranchPrice / ranch.TotalDays;
            int houseCapital = HouseWorkingCapital(ranchPrice, ranch);

            r.HandymanNet = HandymanCallsPerDay * service - van;
            float contractorOverhead = YardDaily(1) + pickup + van;
            r.ContractorNet = jobNet + SideCallsPerDay * service - contractorOverhead;
            r.WinterContractorNet = HandymanCallsPerDay * service * SeasonBusiness.PayFactor(Seasons.Winter) - contractorOverhead;   // no outdoor work at all
            r.CrewNet = r.ContractorNet + crewNet2;
            float builderOverhead = YardDaily(2) - YardDaily(1);
            r.BuilderNet = jobNet + SideCallsPerDay * service - contractorOverhead - builderOverhead + crewNet4 + houseNet;
            var crew8 = new List<Worker>(crew4); foreach (var w in crew4) crew8.Add(MakeWorker(w.Trade, w.Skill));
            float crewNet8 = 0f, payroll8 = 0f; foreach (var w in crew8) { crewNet8 += CrewWorkerNetPerDay(w); payroll8 += w.DailyWage; }
            r.DeveloperNet = jobNet + SideCallsPerDay * service - contractorOverhead - (YardDaily(3) - YardDaily(1)) + crewNet8 + 2f * houseNet;

            float cash = HandymanStart.StartingCash, rep = 50f;
            var stage = CompanyStage.Handyman;
            for (int day = 1; day <= maxDays; day++)
            {
                switch (stage)
                {
                    case CompanyStage.Handyman:
                        cash += r.HandymanNet; rep += HandymanCallsPerDay * repService;
                        if (cash >= Yard.Levels[1].MoveIn + Fleet.DownPayment("pickup", true) + BufferDays * contractorOverhead)
                        { cash -= Yard.Levels[1].MoveIn + Fleet.DownPayment("pickup", true); stage = CompanyStage.Contractor; r.Contractor = day; }
                        break;
                    case CompanyStage.Contractor:
                        cash += r.ContractorNet; rep += SideCallsPerDay * repService + repJob / avgDays;
                        if (cash >= BufferDays * (contractorOverhead + payroll2) + 3000) { stage = CompanyStage.Crew; r.Crew = day; }
                        break;
                    case CompanyStage.Crew:
                        cash += r.CrewNet; rep += SideCallsPerDay * repService + repJob / avgDays + crewRep2;
                        if (rep >= 65 && cash >= Yard.Levels[2].MoveIn + houseCapital + 2 * BufferDays * (contractorOverhead + builderOverhead + payroll4))
                        { cash -= Yard.Levels[2].MoveIn; stage = CompanyStage.Builder; r.Builder = day; }
                        break;
                    case CompanyStage.Builder:
                        cash += r.BuilderNet; rep += SideCallsPerDay * repService + repJob / avgDays + crewRep2 * 2f + repJob / (float)ranch.TotalDays;
                        if (rep >= 80 && cash >= Yard.Levels[3].MoveIn + houseCapital * 2 + 2 * BufferDays * (YardDaily(3) + payroll8))
                        { cash -= Yard.Levels[3].MoveIn; stage = CompanyStage.Developer; r.Developer = day; }
                        break;
                    default:
                        cash += r.DeveloperNet;
                        break;
                }
                rep = Mathf.Min(100f, rep);
                r.Cash.Add(Mathf.RoundToInt(cash));
                if (stage == CompanyStage.Developer && day > r.Developer + 60) break;
            }
            return r;
        }

        // ================================================================== tests

        [UnityTest]
        public IEnumerator Pacing_DecentPlayer_HitsStageTargets()
        {
            yield return Frames(2);
            var (quote, parts) = ServiceAverage();
            var p = RunPacing();
            Log("=== Balance pacing model (decent player) ===");
            Log($"Service call: invoice ${quote:0}, parts ${parts:0}, net ${ServiceNet(PlayerQuality):0} at {PlayerQuality:P0} satisfaction");
            Log($"Contractor jobs (pickup, {PickupJobs().Count} offers): net ${ContractorJobNetPerDay():0}/day while building");
            foreach (var j in PickupJobs()) Log($"  {j.Title,-44} pay ${j.Payout,7:N0}  materials ${j.MaterialBudget,6:N0}  ({(j.Payout - j.MaterialBudget) / (float)j.Payout:P0} over materials)  {j.DueDay} days");
            Log($"Daily overhead: van ${VehicleDaily("van"):0}, used pickup ${VehicleDaily("pickup", Fleet.UsedCondition):0} + loan ${UsedTruckPayment("pickup", 84):0}, bay ${YardDaily(1):0}, yard+shop ${YardDaily(2):0}, HQ ${YardDaily(3):0}");
            foreach (var w in new[] { MakeWorker("laborer", 2), MakeWorker("carpenter", 2), MakeWorker("carpenter", 3) })
                Log($"Crew {w.Trade} skill {w.Skill}: wage ${w.DailyWage}/day, speed {Crew.Speed(w, null):0.00}, net ${CrewWorkerNetPerDay(w):0}/day on contractor jobs");
            Log($"Daily net by stage: Handyman ${p.HandymanNet:0}, Contractor ${p.ContractorNet:0} (winter, calls only: ${p.WinterContractorNet:0}), Crew ${p.CrewNet:0}, Builder ${p.BuilderNet:0}, Developer ${p.DeveloperNet:0}");
            Log($"Weekly net by stage: Handyman ${p.HandymanNet * 7:N0}, Contractor ${p.ContractorNet * 7:N0}, Crew ${p.CrewNet * 7:N0}, Builder ${p.BuilderNet * 7:N0}, Developer ${p.DeveloperNet * 7:N0}");
            Log($"Stage reached on day: Contractor {p.Contractor}, Crew {p.Crew}, Builder {p.Builder}, Developer {p.Developer}");
            DrawChart(p, "balance_pacing");

            Assert.Greater(p.HandymanNet, 0f, "a handyman makes money");
            Assert.Greater(p.ContractorNet, p.HandymanNet, "the warehouse + truck pay for themselves");
            Assert.GreaterOrEqual(p.WinterContractorNet, 0f, "a contractor survives a winter week on service calls (no trap)");
            Assert.Greater(p.CrewNet, p.ContractorNet, "hiring a crew earns more");
            Assert.Greater(p.BuilderNet, p.CrewNet, "the Builder stage earns more");
            Assert.Greater(p.DeveloperNet, 0f, "a Developer's fixed costs are covered");
            Assert.That(p.Contractor, Is.InRange(7, 35), "Contractor in a few in-game weeks");
            Assert.That(p.Crew, Is.InRange(p.Contractor, p.Contractor + 30), "Crew soon after");
            Assert.That(p.Builder, Is.InRange(p.Contractor + 14, 180), "Builder within a season or two");
            Assert.That(p.Developer, Is.InRange(p.Builder + 20, 400), "Developer after that");
            Assert.That(p.Developer, Is.InRange(100, 120), "perfect play reaches Developer around day 105-115 (HQ move-in is the lever)");
        }

        [UnityTest]
        public IEnumerator NoDayOneShortcut_ToContractor()
        {
            yield return Frames(2);
            Company.ResetState(); Finance.ResetState(); Career.Reputation = 50;
            int credit = Finance.CreditLimit;
            int gate = Yard.Levels[1].MoveIn + Fleet.DownPayment("pickup", true);
            Log($"New company: bank line ${credit:N0} + ${HandymanStart.StartingCash:N0} cash vs ${gate:N0} for the bay + a used pickup's down payment");
            Assert.Less(HandymanStart.StartingCash + credit, gate, "a brand-new handyman can't borrow straight into the Contractor stage");
            Assert.Greater(credit, 0, "but the bank lends something");
        }

        [UnityTest]
        public IEnumerator Loans_CostMoney_ButAreNotRuinous()
        {
            yield return Frames(2);
            foreach (int term in Finance.BankTerms)
            {
                int interest = Finance.TotalInterest(10000, Finance.BankApr, term);
                Log($"Bank loan $10,000 / {term} days: ${Finance.PaymentFor(10000, Finance.BankApr, term)}/day, interest ${interest:N0}");
                Assert.Greater(interest, 0, "loans cost money");
                Assert.Less(interest, 10000 * 0.30f, "a bank loan never costs more than 30% of what you borrowed");
            }
            foreach (int term in Finance.VehicleTerms)
            {
                int principal = Fleet.Price("pickup", true) - Fleet.DownPayment("pickup", true);
                Assert.Greater(Finance.TotalInterest(principal, Finance.VehicleAprUsed, term), 0);
            }
            Assert.Less(UsedTruckPayment("pickup", 84), ContractorJobNetPerDay(), "a financed pickup pays for itself with contractor work");
        }

        [UnityTest]
        public IEnumerator BuilderContracts_MarginsInRealRange()
        {
            yield return Frames(2);
            Log("=== Builder contract margins (price vs plan build cost) ===");
            var fails = new List<string>();
            foreach (var d in JobCatalog.All)
            {
                if (d.Mode != JobMode.Build) continue;
                var plan = JobCatalog.PlanFor(d); if (plan == null) continue;
                int price = BuildPrice(d, plan);
                float m = ContractMargin(price, plan);
                Log($"  {d.Id,-18} {plan.Id,-18} ${d.PricePerSqFt}/sf  price ${price,10:N0}  cost ${plan.Takeoff.Total,10:N0}  margin {m:P0}");
                if (m < 0.10f || m > 0.36f) fails.Add($"{d.Id} ({plan.Id}) margin {m:P0}");
            }
            foreach (var kv in Builder.ContractPlans)
            {
                var c = Career.Contracts.Find(x => x.Id == kv.Key); var plan = HousePlans.Get(kv.Value);
                float m = ContractMargin(c.Price, plan);
                Log($"  contract {c.Id,-9} {plan.Id,-18} price ${c.Price,10:N0}  cost ${plan.Takeoff.Total,10:N0}  margin {m:P0}");
                if (m < 0.10f || m > 0.36f) fails.Add($"contract {c.Id} margin {m:P0}");
            }
            Assert.IsEmpty(fails, "builder margins outside 10-36% (a loss is a trap, more is an exploit): " + string.Join("; ", fails));
        }

        [UnityTest]
        public IEnumerator ContractorJobs_PayOverMaterials()
        {
            yield return Frames(2);
            int seed = 500;
            foreach (var d in JobCatalog.All)
            {
                if (d.Mode != JobMode.Fleet) continue;
                var j = JobCatalog.CreateFleet(d, seed++, false); if (j == null) continue;
                float m = (j.Payout - j.MaterialBudget) / (float)j.Payout;
                Assert.That(m, Is.InRange(0.25f, 0.85f), $"{d.Id}: pay ${j.Payout:N0} vs materials ${j.MaterialBudget:N0}");
            }
            var (quote, _) = ServiceAverage();
            Assert.That(quote, Is.InRange(120f, 500f), "average service call invoice");
        }

        [UnityTest]
        public IEnumerator SpecHouses_ProfitableNotAJackpot()
        {
            yield return Frames(2);
            Log("=== Spec houses (fresh neighborhood, average land, 14 days on market) ===");
            // land share per 70' lot from the Development constants: land + street/utility frontage + laterals + plat/engineering
            float acresPerLot = 70f * (Developments.MaxLotDepthFt + Developments.StreetRowFt / 2f) / 43560f;
            float landShare = acresPerLot * 38000f * (1f + Developments.LandClosingPct) + 35f * (Developments.StreetPerLf + Developments.UtilitiesPerLf)
                              + Developments.LateralsPerLot + Developments.EngineeringPerLot + Developments.PlatFee / 14f;
            foreach (var plan in HousePlans.All)
            {
                if (plan.Category == null || !(plan.Id.StartsWith("starter") || plan.Id.StartsWith("cottage") || plan.Id.StartsWith("ranch") || plan.Id.StartsWith("craftsman") || plan.Id.StartsWith("twostory") || plan.Id.StartsWith("estate"))) continue;
                float baseProfit = SpecProfit(plan, plan.BaseQuality, landShare, out int value);
                float lo = SpecProfit(plan, 0f, landShare, out _), hi = SpecProfit(plan, 1f, landShare, out _);
                Log($"  {plan.Id,-18} value ${value,9:N0}  profit ${baseProfit,9:N0} ({baseProfit / value:P0})  finish 0: ${lo:N0}  finish 1: ${hi:N0}");
                Assert.That(baseProfit / value, Is.InRange(-0.02f, 0.30f), plan.Id + " spec margin");
                Assert.Less(Mathf.Max(lo, hi) - baseProfit, value * 0.06f, plan.Id + ": no finish level is a jackpot");
            }
        }

        static float SpecProfit(HousePlan plan, float finish, float landShare, out int value)
        {
            var p = new HouseProject { Id = "bal-spec", PlanId = plan.Id, FinishLevel = finish, Quality = 0.8f, Kind = ProjectKind.Spec };
            float build = 0f; for (int i = 0; i < 5; i++) build += Projects.PhaseCost(p, (BuildPhase)i);
            int days = plan.TotalDays + 14;
            float interest = build * Developments.LoanToCost * Developments.LoanRate / 365f * days * 0.5f;
            float carry = (landShare + build * 0.5f) * Developments.PropertyTaxRate / 365f * days + Developments.BuildersRiskPerHouseDay * days;
            value = Developments.MarketValue(p, 135);   // market cycle at 1.0 on day 135 is +7%; use the mean below
            value = Mathf.RoundToInt(value / Developments.MarketCycle(135));
            float selling = value * (Developments.CommissionPct + Developments.ClosingPct);
            return value - build - landShare - Developments.PermitAndFees(plan) - interest - carry - selling;
        }

        [UnityTest]
        public IEnumerator Flips_RenovationPays_AsIsDoesNot()
        {
            yield return Frames(2);
            Log("=== Flip house (1,150 sq ft ranch, Old Town comps) ===");
            int arv = Flips.SqFtOf(null) * Flips.PricePerSqFt(null) + Flips.LotValue;
            var problems = new List<FlipItemDef>(); FlipItemDef roof = null;
            foreach (var d in FlipDefs.Problems) { if (d.Always) problems.Add(d); if (d.Id == "roof") roof = d; }
            problems.Add(roof);
            var others = FlipDefs.Problems.FindAll(d => !d.Always && d.Id != "roof" && d.Id != "siding" && d.Id != "waterheater");
            others.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < 4 && i < others.Count; i++) problems.Add(others[i]);                    // a bad house: the 4 worst extras
            var items = new List<FlipItemDef>(problems); items.AddRange(FlipDefs.Upgrades);

            float open = 0f; foreach (var d in items) open += d.Value;
            float asIs = arv - open - arv * Flips.DistressDiscount;
            foreach (float askK in new[] { 0.97f, 1.03f })
            {
                float price = asIs * askK;
                float parts = 0f, contractor = 0f;
                foreach (var d in items) { contractor += FlipDefs.ContractorPrice(d); foreach (var id in d.Defs) { var jd = JobCatalog.Get(id); if (jd != null && jd.Causes.Count > 0) parts += jd.MaterialCostFor(jd.Causes[0]); } }
                float own = FlipProfit(arv, price, parts, 35, 0.97f);
                float hired = FlipProfit(arv, price, contractor, 35, 0.97f);
                float flipped = FlipProfit(arv, price, 0f, 20, 0.97f * asIs / arv);   // relisted untouched at 97% of as-is value
                Log($"  ARV ${arv:N0}, asking {askK:0.00} x as-is = ${price:N0}: own labor (parts ${parts:N0}) profit ${own:N0} ({own / arv:P0}); all hired (${contractor:N0}) ${hired:N0}; resold as-is ${flipped:N0}");
                Assert.That(own / arv, Is.InRange(0.0f, 0.22f), "renovating a flip yourself pays, but it's no jackpot");
                Assert.Less(flipped, 0f, "buying and relisting untouched loses money (no exploit)");
                Assert.Greater(hired, -0.12f * arv, "even fully contracted out it's not a catastrophe");
            }
        }

        static float FlipProfit(int arv, float price, float rehab, int days, float saleOfArv)
        {
            float loan = price * Flips.LoanToPrice;
            float buy = price * (1f + Flips.BuyClosingPct) + loan * Flips.LoanPoints;
            float hold = days * (price * Flips.TaxRate / 365f + Flips.InsurancePerYear / 365f + Flips.UtilitiesPerDay + loan * Flips.LoanApr / 365f);
            float sale = arv * saleOfArv * (1f - Flips.CommissionPct - Flips.SellClosingPct);
            return sale - buy - rehab - hold;
        }

        [UnityTest]
        public IEnumerator Subcontractors_CostMoreThanOwnCrew_ButNotAbsurd()
        {
            yield return Frames(2);
            foreach (var c in SubCatalog.Companies)
            {
                var t = SubCatalog.Trade(c.Trade);
                float own = CrewTrades.Wage(t.CrewTrade, 3) / t.CrewPerDay;
                float best = t.Rate * c.PriceFactor * (1f - Subs.MaxDiscount);
                Assert.Greater(best, own, $"{c.Name}: sub price even with the repeat discount must beat your own crew's wages (${own:0.00}/{t.Unit})");
                Assert.Less(t.Rate * c.PriceFactor, own * 6f, $"{c.Name}: sub price not absurd");
            }
        }

        [UnityTest]
        public IEnumerator Equipment_RentVsBuy_BreakEvenSensible()
        {
            yield return Frames(2);
            foreach (var id in new[] { "miniex", "boomlift", "skidsteer", "boom" })
            {
                var e = Equipment.Get(id); Assert.NotNull(e, id);
                float buy, own;
                if (e.PriceUsed > 0) { buy = e.PriceUsed; own = e.InsuranceMaintPerDay; }
                else { var v = FleetCatalog.Get(e.FleetTypeId); buy = v.PriceUsed; own = v.InsurancePerDay + v.MaintenancePerDay; }
                float breakEven = buy / (e.RentPerDay - own);
                Log($"{e.Name}: rent ${e.RentPerDay}/day vs used ${buy:N0} + ${own}/day -> buying pays after {breakEven:0} rental days");
                Assert.That(breakEven, Is.InRange(45f, 400f), id + ": buy pays off after a sensible number of rental days");
            }
            Assert.Greater(Equipment.Get("miniex").RentPerDay + Equipment.DeliveryFee, 0);
        }

        [UnityTest]
        public IEnumerator Crew_OffSiteJobs_ChargeMaterials()
        {
            yield return Frames(2);
            GameSettings.Mode = GameMode.Career; GameSettings.UnlimitedMoney = false;
            Company.ResetState(); Crew.ResetState();
            Game.Wallet = new Wallet(100000);
            var job = PickupJobs()[0];
            var j = new Job { Id = "bal-crewjob", Title = "Balance test job", Kind = JobKind.Remodel, Payout = job.Payout, MaterialBudget = job.MaterialBudget, Accepted = true };
            Company.ActiveJobs.Add(j);
            var w = Company.Hire(MakeWorker("carpenter", 3)); w.AssignedJobId = j.Id;
            Crew.HereJobId = "elsewhere";
            var step = typeof(Crew).GetMethod("OnDayStarted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(step);
            int before = Game.Wallet.Money, paid = 0;
            for (int d = 0; d < 200 && !j.Done; d++) step.Invoke(null, new object[] { 1000 + d });
            Assert.IsTrue(j.Done, "the crew finished the job off site");
            foreach (var l in Game.Wallet.Log) if (l.Contains("Materials (crew job)") && l.StartsWith("-$")) paid += int.Parse(l.Substring(2, l.IndexOf(' ') - 2));
            Log($"Crew job: payout ${j.Payout:N0}, materials charged ${paid:N0} (budget ${j.MaterialBudget:N0}), net to the company ${Game.Wallet.Money - before:N0} before wages");
            Assert.AreEqual(j.MaterialBudget, paid, 2, "the job's materials are bought as the crew works");
            Assert.Less(Game.Wallet.Money - before, j.Payout * 1.1f - j.MaterialBudget + 1, "no free materials");
            Crew.HereJobId = Crew.SiteJobId;
        }

        [UnityTest]
        public IEnumerator Crew_OnHouses_CreditsSubLabor_Bounded()
        {
            yield return Frames(2);
            Company.ResetState(); Crew.ResetState();
            Assert.IsTrue(Projects.Crew is CrewHouseProgress, "integration sets the crew house progress");
            var plan = HousePlans.Get("ranch-1600");
            var p = new HouseProject { Id = "bal-house", PlanId = plan.Id, FinishLevel = plan.BaseQuality, Kind = ProjectKind.Client };
            for (int i = 0; i < 5; i++) p.Funded[i] = true;
            Assert.AreEqual(0, CrewLaborCredit.CreditFor(p), "no crew, no credit");
            var w = Company.Hire(MakeWorker("carpenter", 3)); w.AssignedJobId = p.Id;
            float total = 0f, crewDays = 0f;
            for (int ph = 0; ph < 5; ph++)
            {
                var bp = (BuildPhase)ph; float phaseCredit = 0f;
                p.Passed.Clear(); for (int k = 0; k < ph; k++) { if (k == 0) { p.Passed.Add((int)InspectionPhase.Footing); p.Passed.Add((int)InspectionPhase.UnderSlab); } else p.Passed.Add((int)HousePlan.InspectionFor((BuildPhase)k)); }
                Assert.AreEqual(bp, p.CurrentPhase.Value);
                p.Progress[ph] = 0f;
                for (int d = 0; d < 400 && p.Progress[ph] < 0.999f; d++)
                {
                    int c = CrewLaborCredit.CreditFor(p); phaseCredit += c; crewDays += 1f;
                    p.Progress[ph] = Mathf.Min(1f, p.Progress[ph] + Projects.Crew.DailyProgress(p, bp, d));
                }
                Assert.LessOrEqual(phaseCredit, CrewLaborCredit.PhaseLabor(p, bp) + 5, bp + ": credit never exceeds the phase's sub labor");
                total += phaseCredit;
            }
            float perDay = total / crewDays;
            Log($"Own carpenter (skill 3, ${w.DailyWage}/day) on the ranch: sub labor credit ${total:N0} over {crewDays:0} days = ${perDay:0}/day");
            Assert.Greater(perDay, w.DailyWage * 0.8f, "your own crew on a house is no longer a pure loss");
            Assert.Less(perDay, w.DailyWage * 2.5f, "nor a money machine");
        }

        [UnityTest]
        public IEnumerator Reputation_SmallJobsEarnLess()
        {
            yield return Frames(2);
            GameSettings.Mode = GameMode.Career;
            Company.ResetState(); Game.Wallet = new Wallet(1000); Career.Reputation = 50;
            var small = new Job { Id = "bal-small", Title = "Balance small", Payout = 300, Accepted = true };
            var big = new Job { Id = "bal-big", Title = "Balance big", Payout = 6000, Accepted = true };
            Company.ActiveJobs.Add(small); Company.ActiveJobs.Add(big);
            Company.Complete(small, 1f);
            Assert.AreEqual(51, Company.Reputation, "a perfect $300 service call: +1");
            Company.Complete(big, 1f);
            Assert.AreEqual(54, Company.Reputation, "a perfect $6,000 deck: +3");
            var bad = new Job { Id = "bal-bad", Title = "Balance bad", Payout = 300, Accepted = true };
            Company.ActiveJobs.Add(bad);
            Company.Complete(bad, 0f);
            Assert.AreEqual(51, Company.Reputation, "a botched small job still costs the full -3");
        }

        // ================================================================== chart (the "screenshot" of this feature)

        static void DrawChart(Pacing p, string name)
        {
            const int W = 1280, H = 720, M = 60;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var px = new Color32[W * H];
            var bg = new Color32(250, 248, 242, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            void Rect(int x0, int y0, int x1, int y1, Color32 c)
            {
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y++) for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x++) px[y * W + x] = c;
            }
            int days = p.Cash.Count; float max = 1f, min = 0f;
            foreach (var c in p.Cash) { max = Mathf.Max(max, c); min = Mathf.Min(min, c); }
            int X(int d) => M + Mathf.RoundToInt((W - 2 * M) * d / (float)Mathf.Max(1, days));
            int Y(float v) => M + Mathf.RoundToInt((H - 2 * M) * (v - min) / (max - min));
            Rect(M, M, W - M, M + 2, new Color32(60, 60, 60, 255));                   // axes
            Rect(M, M, M + 2, H - M, new Color32(60, 60, 60, 255));
            for (int k = 1; k <= 4; k++) { int y = Y(max * k / 4f); Rect(M, y, W - M, y + 1, new Color32(210, 210, 205, 255)); }
            var stageCol = new[] { new Color32(46, 125, 50, 255), new Color32(25, 118, 210, 255), new Color32(245, 124, 0, 255), new Color32(142, 36, 170, 255) };
            var stageDay = new[] { p.Contractor, p.Crew, p.Builder, p.Developer };
            for (int s = 0; s < 4; s++) if (stageDay[s] > 0) { int x = X(stageDay[s]); Rect(x - 2, M, x + 2, H - M, stageCol[s]); }
            int prevY = Y(p.Cash.Count > 0 ? p.Cash[0] : 0);
            for (int d = 0; d < days; d++)
            {
                int x = X(d), y = Y(p.Cash[d]);
                Rect(x, Mathf.Min(prevY, y) - 2, X(d + 1) + 1, Mathf.Max(prevY, y) + 2, new Color32(30, 30, 30, 255));
                prevY = y;
            }
            tex.SetPixels32(px); tex.Apply();
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
