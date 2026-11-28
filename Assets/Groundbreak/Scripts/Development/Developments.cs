using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A subdivision the company owns: a raw tract, platted into lots along a new street.</summary>
    [Serializable]
    public class Development
    {
        public string Id, TractId, Name;
        public int LandCost, BoughtDay, PlatDay = -1, SiteWorkStartDay = -1, SiteWorkDays = 30, CompletedDay = -1;
        public float LotFrontageFt = 70f;
        public List<DevLot> Lots = new List<DevLot>();
        public Rect StreetRect;                       // new street right-of-way (world XZ)
        public bool Platted, CompletionShown;
        public int SoftCosts, SiteWorkCost, InterestPaid, CarryPaid, Revenue;

        public DevLot Tract => DevLots.Provider.Find(TractId);
        public bool SiteWorkDone => SiteWorkStartDay >= 0 && Projects.Day >= SiteWorkStartDay + SiteWorkDays;
        public float SiteWorkProgress => SiteWorkStartDay < 0 ? 0f : Mathf.Clamp01((Projects.Day - SiteWorkStartDay) / (float)SiteWorkDays);
        public IEnumerable<HouseProject> Houses { get { foreach (var p in Projects.All) if (p.DevId == Id) yield return p; } }
        public int SoldCount { get { int n = 0; foreach (var h in Houses) if (h.Sold) n++; return n; } }
        public bool Complete => Platted && Lots.Count > 0 && SoldCount == Lots.Count;
        public float StreetLengthFt => Mathf.Max(StreetRect.width, StreetRect.height) / Units.Foot;
    }

    /// <summary>Developer stage: buy raw land, plat it, pay for site work, permits and impact fees, build spec houses from plans
    /// on a construction loan, and sell them on a simple housing market. Real-world 2026 US numbers (exurban land, per-lf site work,
    /// 8.5% construction loan at 80% loan-to-cost, 5.5% commission + 1.5% seller closing costs).</summary>
    public static class Developments
    {
        public static readonly List<Development> All = new List<Development>();
        public static event Action<Development> Completed;

        // ---- real-world numbers
        public const float StreetRowFt = 50f, PavementFt = 28f, FrontSetbackFt = 25f, SideSetbackFt = 7.5f, RearSetbackFt = 20f, MaxLotDepthFt = 130f;
        public const int PlatFee = 8500, EngineeringPerLot = 1200, StreetPerLf = 450, UtilitiesPerLf = 180, LateralsPerLot = 3500;
        public const int ImpactFeePerHouse = 9800, PermitBase = 1200;
        public const float PermitPerSqFt = 0.9f, LoanToCost = 0.80f, LoanRate = 0.085f, PropertyTaxRate = 0.011f;
        public const int BuildersRiskPerHouseDay = 3;
        public const float CommissionPct = 0.055f, ClosingPct = 0.015f, LandClosingPct = 0.02f;
        public const float BasePricePerSqFt = 185f;
        public const int LotPremium = 45000;

        public static int PricePerAcre(DevLot tract)
        {
            int h = 0; foreach (char ch in tract.Id) h = h * 31 + ch;
            return 32000 + Mathf.Abs(h % 13) * 1000;   // $32k–$44k / acre: farm ground at the edge of town
        }
        public static int LandPrice(DevLot tract) => Mathf.RoundToInt(tract.Acres * PricePerAcre(tract) / 100f) * 100;

        public static Development Get(string id) => All.Find(d => d.Id == id);
        public static DevLot FindLot(string id)
        {
            foreach (var d in All) foreach (var l in d.Lots) if (l.Id == id) return l;
            return DevLots.Provider.Find(id);
        }
        public static Development Of(HouseProject p) => p.DevId == null ? null : Get(p.DevId);
        /// <summary>Spec houses wait for site work (street + utilities) before they can start.</summary>
        public static bool LotReady(HouseProject p) { var d = Of(p); return d == null || d.SiteWorkDone; }

        // ------------------------------------------------------------------ land

        public static bool BuyLand(DevLot tract, out Development dev, out string msg)
        {
            dev = null;
            if (GameSettings.IsCareer && Company.Stage < CompanyStage.Developer) { msg = "Needs company stage: Developer"; return false; }
            if (tract == null || tract.Claimed) { msg = "That land is not for sale."; return false; }
            int price = LandPrice(tract), closing = Mathf.RoundToInt(price * LandClosingPct);
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(price + closing, $"Land: {tract.Address} ({tract.Acres:0.0} ac)")) { msg = $"Need ${price + closing:N0}"; return false; }
            DevLots.Provider.Claim(tract, Company.Name);
            dev = new Development { Id = "dev-" + tract.Id, TractId = tract.Id, Name = NameFor(tract), LandCost = price + closing, BoughtDay = Projects.Day };
            All.Add(dev);
            msg = $"Bought {tract.Acres:0.0} acres for ${price:N0} (+${closing:N0} closing)";
            DevVisuals.Refresh();
            return true;
        }

        static string NameFor(DevLot t) { string[] n = { "Willow Creek", "Harvest Ridge", "Stone Meadow", "Fox Hollow" }; int h = 0; foreach (char c in t.Id) h += c; return n[h % n.Length]; }

        /// <summary>Plats the tract: a 50' right-of-way street runs from the county road into the tract, lots of the chosen frontage
        /// on both sides (depth up to 130'). Pays the plat fee and engineering. Returns the lot count.</summary>
        public static int Subdivide(Development d, float frontageFt, out string msg)
        {
            if (d.Platted) { msg = "Already platted."; return d.Lots.Count; }
            var t = d.Tract;
            float row = StreetRowFt * Units.Foot, fr = frontageFt * Units.Foot;
            // tract frame: across = along the county road, back = into the tract
            Vector3 back = -t.Front, across = Vector3.Cross(Vector3.up, back);
            float width = t.FrontageM, depth = t.DepthM;
            float lotDepth = Mathf.Min(MaxLotDepthFt * Units.Foot, (width - row) / 2f);
            int perSide = Mathf.FloorToInt(depth / fr);
            if (lotDepth < 100f * Units.Foot || perSide < 1) { msg = "Tract too small for lots of that size."; return 0; }
            int cost = PlatFee + EngineeringPerLot * perSide * 2;
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(cost, $"Plat + engineering: {d.Name}")) { msg = $"Need ${cost:N0}"; return 0; }
            d.SoftCosts += cost; d.LotFrontageFt = frontageFt;
            Vector3 front0 = t.FrontMid;
            Rect R(Vector3 c, float alongAcross, float alongBack)
            {
                var a = c - across * alongAcross / 2f - back * alongBack / 2f; var b = c + across * alongAcross / 2f + back * alongBack / 2f;
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
            }
            d.StreetRect = R(front0 + back * (perSide * fr / 2f), row, perSide * fr);
            int num = 1;
            for (int i = 0; i < perSide; i++)
                for (int side = -1; side <= 1; side += 2)
                {
                    var c = front0 + back * (fr * (i + 0.5f)) + across * side * (row / 2f + lotDepth / 2f);
                    var lot = new DevLot
                    {
                        Id = d.Id + "-" + num, Address = $"{num * 2 + 100} {d.Name.Split(' ')[0]} Ct", Rect = R(c, lotDepth, fr),
                        Front = -across * side, Claimed = true, Owner = Company.Name,
                    };
                    d.Lots.Add(lot); num++;
                }
            d.Platted = true; d.PlatDay = Projects.Day;
            msg = $"Platted {d.Lots.Count} lots ({frontageFt:0}' x {lotDepth / Units.Foot:0}'), plat + engineering ${cost:N0}";
            DevVisuals.Refresh();
            return d.Lots.Count;
        }

        public static int SiteWorkCostFor(Development d) =>
            Mathf.RoundToInt(d.StreetLengthFt * (StreetPerLf + UtilitiesPerLf) + d.Lots.Count * LateralsPerLot);

        /// <summary>Hires the site contractor: clearing, street (28' pavement, curb, storm), water, sewer, power and lot laterals.</summary>
        public static bool StartSiteWork(Development d, out string msg)
        {
            if (!d.Platted) { msg = "Plat the land first."; return false; }
            if (d.SiteWorkStartDay >= 0) { msg = "Site work already under way."; return false; }
            int cost = SiteWorkCostFor(d);
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(cost, $"Site work: {d.Name} ({d.StreetLengthFt:0} lf street + utilities)")) { msg = $"Need ${cost:N0}"; return false; }
            d.SiteWorkCost = cost; d.SiteWorkStartDay = Projects.Day;
            // spec houses permitted before the site work got a land share without it: add their share now (LandShare = all-in / lots)
            if (d.Lots.Count > 0) foreach (var h in d.Houses) if (!h.Sold) h.CostSpent += cost / d.Lots.Count;
            msg = $"Site work started: ${cost:N0}, {d.SiteWorkDays} days";
            DevVisuals.Refresh();
            return true;
        }

        public static int LandShare(Development d) => d.Lots.Count == 0 ? 0 : (d.LandCost + d.SoftCosts + d.SiteWorkCost) / d.Lots.Count;
        public static int PermitAndFees(HousePlan plan) => ImpactFeePerHouse + PermitBase + Mathf.RoundToInt(plan.SqFt * PermitPerSqFt);

        /// <summary>Starts a spec house: building permit + impact fees now, phases funded 80% by the construction loan as they start.</summary>
        public static HouseProject StartSpecHouse(Development d, DevLot lot, HousePlan plan, float finishLevel, out string msg)
        {
            if (!d.Lots.Contains(lot)) { msg = "Not a lot in this development."; return null; }
            foreach (var h in d.Houses) if (h.LotId == lot.Id) { msg = "That lot already has a house."; return null; }
            if (plan.WidthFt > lot.FrontageFt - 2 * SideSetbackFt || plan.DepthFt > lot.DepthFt - FrontSetbackFt - RearSetbackFt) { msg = $"{plan.Name} doesn't fit inside the setbacks."; return null; }
            int fees = PermitAndFees(plan);
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(fees, $"Building permit + impact fees: {lot.Address}")) { msg = $"Need ${fees:N0} for permit + impact fees"; return null; }
            var p = Projects.Create(ProjectKind.Spec, lot, plan);
            p.DevId = d.Id; p.FinishLevel = Mathf.Clamp01(finishLevel); p.Client = "Spec";
            p.CostSpent = LandShare(d) + fees;
            msg = $"Permit pulled: {plan.Name} on {lot.Address} (${fees:N0} permit + impact fees)";
            DevVisuals.Refresh();
            return p;
        }

        /// <summary>Spec phases: the lender funds 80% of each phase cost, the company pays 20% equity.</summary>
        public static bool FundSpec(HouseProject p, BuildPhase ph, int cost)
        {
            int loan = Mathf.RoundToInt(cost * LoanToCost), equity = cost - loan;
            if (!GameSettings.UnlimitedMoney && !Game.Wallet.TrySpend(equity, $"Equity ({HousePlan.Label(ph)}): {p.Lot.Address}")) return false;
            p.LoanBalance += loan; p.CostSpent += cost;
            return true;
        }

        // ------------------------------------------------------------------ market

        /// <summary>Slow market cycle: ±7% over an 18-month (540-day) wave.</summary>
        public static float MarketCycle(int day) => 1f + 0.07f * Mathf.Sin(2f * Mathf.PI * day / 540f);

        /// <summary>Share of the development's lots with a finished house (completed streets/neighbors sell homes).</summary>
        public static float Completeness(Development d)
        {
            if (d == null || d.Lots.Count == 0) return 0f;
            int n = 0; foreach (var h in d.Houses) if (h.Complete) n++;
            return n / (float)d.Lots.Count;
        }

        /// <summary>Appraised value of a finished house today: size × $/sq ft × finish × neighborhood completeness × market cycle + lot.</summary>
        public static int MarketValue(HouseProject p, int day)
        {
            var plan = p.Plan; var d = Of(p);
            float finish = 0.85f + 0.35f * p.FinishLevel;
            float hood = 0.92f + 0.12f * Completeness(d) + (d != null && d.SiteWorkDone ? 0.02f : 0f);
            float workmanship = 0.9f + 0.1f * p.Quality;
            return Mathf.RoundToInt((plan.SqFt * BasePricePerSqFt * finish * hood * workmanship * MarketCycle(day) + LotPremium) / 1000f) * 1000;
        }

        /// <summary>Days on market a listing needs: ~2 weeks at value, much longer when overpriced; never above +15%.</summary>
        public static int DaysToSell(float askOverValue) => askOverValue > 1.15f ? int.MaxValue : 14 + Mathf.RoundToInt(Mathf.Max(0f, askOverValue - 0.97f) * 600f);

        public static bool List(HouseProject p, int price, out string msg)
        {
            if (p.Kind != ProjectKind.Spec || !p.Complete) { msg = "Only finished spec houses can be listed."; return false; }
            if (p.Sold) { msg = "Already sold."; return false; }
            p.Listed = true; p.ListPrice = Mathf.Max(1000, price); p.ListedDay = Projects.Day;
            msg = $"Listed {p.Lot.Address} at ${p.ListPrice:N0}";
            return true;
        }

        /// <summary>Closes the sale: commission + seller closing costs, construction loan paid off from the proceeds.</summary>
        public static void Close(HouseProject p, int price, int day)
        {
            var d = Of(p);
            int commission = Mathf.RoundToInt(price * CommissionPct), closing = Mathf.RoundToInt(price * ClosingPct);
            int net = price - commission - closing - p.LoanBalance;
            p.CostSpent += commission + closing;
            Game.Wallet.Add(price - commission - closing, $"Sale closed: {p.Lot.Address} ${price:N0} (less {CommissionPct:P1} commission, {ClosingPct:P1} closing)");
            if (p.LoanBalance > 0)
            {
                int paid = Mathf.Min(p.LoanBalance, Mathf.Max(0, Game.Wallet.Money));
                if (paid > 0) Game.Wallet.TrySpend(paid, $"Construction loan payoff: {p.Lot.Address}");
                Finance.AddShortfallNote(p.Lot.Address, p.LoanBalance - paid);     // the unpaid rest stays owed as company debt
            }
            p.LoanBalance = 0; p.Sold = true; p.Listed = false; p.SoldDay = day; p.SalePrice = price; p.NetProceeds = net;
            Company.LifetimeRevenue += price; if (d != null) d.Revenue += price;
            Company.Reputation += 1;
            Game.Toast($"SOLD: {p.Plan.Name} at {p.Lot.Address} for ${price:N0} (profit ${Profit(p):N0})");
            Sfx.Play("success", Vector3.zero, 0.7f);
            if (d != null && d.Complete && d.CompletedDay < 0) { d.CompletedDay = day; try { Completed?.Invoke(d); } catch (Exception e) { Debug.LogException(e); } }
            Company.NotifyChanged();
        }

        public static int Profit(HouseProject p) => (p.Sold ? p.SalePrice : 0) - p.CostSpent;

        // ------------------------------------------------------------------ daily

        public static int LoanBalance { get { int s = 0; foreach (var p in Projects.All) s += p.LoanBalance; return s; } }
        public static int DailyInterest(HouseProject p) => Mathf.RoundToInt(p.LoanBalance * LoanRate / 365f);
        public static int DailyCarry(HouseProject p)
        {
            if (p.Kind != ProjectKind.Spec || p.Sold) return 0;
            // CostSpent already includes the land share (StartSpecHouse), so don't add it a second time
            return Mathf.RoundToInt(p.CostSpent * PropertyTaxRate / 365f) + BuildersRiskPerHouseDay;
        }

        static void DayStep(int day)
        {
            foreach (var p in Projects.All.ToArray())
            {
                if (p.Kind != ProjectKind.Spec || p.Sold) continue;
                var d = Of(p);
                int i = DailyInterest(p), c = DailyCarry(p);
                p.CostSpent += i + c; if (d != null) { d.InterestPaid += i; d.CarryPaid += c; }
                if (p.Listed && day - p.ListedDay >= DaysToSell(p.ListPrice / (float)MarketValue(p, day))) Close(p, p.ListPrice, day);
            }
        }

        public static void ResetState() { All.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { All.Clear(); Completed = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Company.RegisterDailyCost("Construction loan interest", () => { int s = 0; foreach (var p in Projects.All) s += DailyInterest(p); return s; });
            Company.RegisterDailyCost("Property tax + builder's risk (spec houses)", () => { int s = 0; foreach (var p in Projects.All) s += DailyCarry(p); return s; });
            Company.DayStarted += d => { if (GameSettings.IsCareer) DayStep(d); };
        }

        /// <summary>For tests and tools: run the developer day step without the clock.</summary>
        public static void Step(int day) => DayStep(day);
    }
}
