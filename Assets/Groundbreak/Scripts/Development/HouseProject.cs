using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum ProjectKind { Client, Spec }

    [Serializable]
    public class ChangeOrder { public string Title; public int Price, Cost, Days; public bool Accepted, Declined; }

    /// <summary>One house being built on one lot: a client contract (Builder) or a spec house (Developer).</summary>
    [Serializable]
    public class HouseProject
    {
        public string Id, LotId, PlanId, JobId, DevId, Client;
        public ProjectKind Kind;
        public float[] Progress = new float[5];          // 0..1 per BuildPhase
        public List<int> Passed = new List<int>();       // InspectionPhase values signed off
        public string InspectionState;                   // Inspections save JSON while the player works another lot
        public int StartDay, DoneDay = -1, DueDay;
        public float Quality = 0.8f, FinishLevel = 0.6f;
        public bool[] Funded = new bool[5];
        public int CostSpent;                             // everything spent on this house (materials, labor, fees, land share, interest)
        // client contract
        public int ContractPrice, DrawsPaid;              // DrawsPaid = bitmask of BuildPhase draws released (+ bit 7 deposit)
        public List<ChangeOrder> ChangeOrders = new List<ChangeOrder>();
        public int WarrantyDay = -1; public string WarrantyJobId; public bool WarrantyDone;
        // spec sale
        public bool Listed, Sold; public int ListPrice, ListedDay, SoldDay, SalePrice, NetProceeds, LoanBalance;

        public HousePlan Plan => HousePlans.Get(PlanId);
        public DevLot Lot => Developments.FindLot(LotId);
        public bool Complete => IsPassed(InspectionPhase.Final);
        public bool IsPassed(InspectionPhase p) => Passed.Contains((int)p);
        public bool PhaseDone(BuildPhase b)
        {
            if (!IsPassed(HousePlan.InspectionFor(b))) return false;
            return b != BuildPhase.Foundation || Plan == null || !Plan.RequiredInspections.Contains(InspectionPhase.UnderSlab) || IsPassed(InspectionPhase.UnderSlab);
        }
        public BuildPhase? CurrentPhase { get { for (int i = 0; i < 5; i++) if (!PhaseDone((BuildPhase)i)) return (BuildPhase)i; return null; } }
        public float Overall { get { float s = 0; for (int i = 0; i < 5; i++) s += PhaseDone((BuildPhase)i) ? 1f : Progress[i]; return s / 5f; } }
        public string StatusLabel => Sold ? "Sold" : Listed ? "Listed" : Complete ? "Finished (CO issued)" : CurrentPhase.HasValue ? HousePlan.Label(CurrentPhase.Value) + $" {Progress[(int)CurrentPhase.Value]:P0}" : "";
        public PlanPlacement Placement => PlanPlacement.On(Lot, Plan);
        public int ChangeOrderTotal { get { int s = 0; foreach (var c in ChangeOrders) if (c.Accepted) s += c.Price; return s; } }
    }

    /// <summary>How fast crews advance a house the player isn't standing at. The Crew feature replaces <see cref="Projects.Crew"/>.
    /// Must be deterministic for a given project/day.</summary>
    public interface ICrewProgress
    {
        /// <summary>Fraction of the phase finished in one working day.</summary>
        float DailyProgress(HouseProject p, BuildPhase phase, int day);
        /// <summary>Workmanship 0..1 for the finished house.</summary>
        float Quality(HouseProject p);
    }

    /// <summary>Default: subcontractors at the plan's phase durations; hired workers speed it up a little.</summary>
    public class SimpleCrewProgress : ICrewProgress
    {
        public float DailyProgress(HouseProject p, BuildPhase phase, int day)
        {
            int assigned = 0; foreach (var w in Company.Workers) if (w.AssignedJobId == p.Id || w.AssignedJobId == p.JobId) assigned++;
            return (1f + 0.1f * Mathf.Min(assigned, 5)) / Mathf.Max(1, p.Plan.PhaseDays(phase));
        }
        public float Quality(HouseProject p)
        {
            if (Company.Workers.Count == 0) return 0.8f;
            float s = 0; foreach (var w in Company.Workers) s += w.Skill; return Mathf.Clamp01(0.55f + 0.08f * s / Company.Workers.Count);
        }
    }

    /// <summary>All house projects. The one the player works on is <see cref="OnSite"/>: its progress is measured from the real
    /// pieces on the lot and the inspection results. Every other project advances once per game day through <see cref="Crew"/>.</summary>
    public static class Projects
    {
        public static readonly List<HouseProject> All = new List<HouseProject>();
        public static ICrewProgress Crew = new SimpleCrewProgress();
        public static string OnSiteId;
        public static HouseProject OnSite => All.Find(p => p.Id == OnSiteId);
        public static event Action<HouseProject, BuildPhase> PhaseCompleted;
        public static event Action<HouseProject> Changed;
        static int _seq;

        public static HouseProject Get(string id) => All.Find(p => p.Id == id);
        public static HouseProject ForJob(string jobId) => All.Find(p => p.JobId == jobId);

        public static HouseProject Create(ProjectKind kind, DevLot lot, HousePlan plan)
        {
            var p = new HouseProject { Id = "hp" + (++_seq) + "-" + lot.Id, Kind = kind, LotId = lot.Id, PlanId = plan.Id, StartDay = Day, FinishLevel = plan.BaseQuality };
            All.Add(p); Changed?.Invoke(p);
            return p;
        }

        public static int Day => GameClock.I != null ? GameClock.I.Day : 1;

        // ------------------------------------------------------------------ on-site (player-built)

        /// <summary>Makes this the player's job site: lot boundary, required inspections and their state, plan overlay.</summary>
        public static void GoOnSite(HouseProject p)
        {
            LeaveSite();
            if (p == null) return;
            OnSiteId = p.Id;
            var lot = p.Lot;
            Site.Lot = lot.Rect; Site.LotName = p.Plan.Name + " @ " + lot.Address;
            Inspections.Load(p.InspectionState);
            Inspections.RequiredPhases = p.Plan.RequiredInspections;
            PlanOverlay.Show(p.Plan, p.Placement, 0);
            DevVisuals.Refresh();
        }

        public static void LeaveSite()
        {
            var cur = OnSite;
            if (cur != null) { cur.InspectionState = Inspections.Save(); Measure(cur); }
            OnSiteId = null;
            PlanOverlay.Hide();
        }

        static void OnInspection(InspectionResult r)
        {
            var p = OnSite;
            if (p == null || !r.Passed || p.IsPassed(r.Phase)) return;
            MarkPassed(p, r.Phase);
            p.InspectionState = Inspections.Save();
        }

        /// <summary>Records a passed inspection and fires PhaseCompleted for phases that are now closed.</summary>
        public static void MarkPassed(HouseProject p, InspectionPhase ph)
        {
            if (p.IsPassed(ph)) return;
            var before = new bool[5]; for (int i = 0; i < 5; i++) before[i] = p.PhaseDone((BuildPhase)i);
            p.Passed.Add((int)ph);
            for (int i = 0; i < 5; i++)
                if (!before[i] && p.PhaseDone((BuildPhase)i))
                {
                    p.Progress[i] = 1f;
                    if (i == 4)
                    {
                        p.DoneDay = Day; p.Quality = p.Id == OnSiteId ? QualityFromInspections(p) : Crew.Quality(p);
                        // spec houses have no Job, so Company.Complete never frees their crew: release them here
                        foreach (var w in Company.Workers) if (w.AssignedJobId == p.Id) w.AssignedJobId = null;
                    }
                    try { PhaseCompleted?.Invoke(p, (BuildPhase)i); } catch (Exception e) { Debug.LogException(e); }
                }
            Changed?.Invoke(p);
        }

        static float QualityFromInspections(HouseProject p)
        {
            int fails = 0; foreach (var ph in Inspections.AllPhases) fails += Inspections.FailCount(ph);
            return Mathf.Clamp01(1f - 0.12f * fails);
        }

        /// <summary>Measures phase progress of a player-built house from what is really on the lot. A phase only reads 100% once its
        /// inspection passed; until then measured progress tops out at 95%.</summary>
        public static void Measure(HouseProject p)
        {
            var plan = p.Plan; var t = plan.Takeoff; var lot = p.Lot;
            float yards = 0; foreach (var c in ConcreteElement.All) if (c != null && c.Outline != null && c.Outline.Length > 0 && lot.Contains(new Vector3(c.Outline[0].x, 0, c.Outline[0].y))) yards += c.Yards;
            int framing = 0, mep = 0, drywall = 0, finish = 0;
            foreach (var pc in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (pc == null || pc.Def == null || !pc.gameObject.activeInHierarchy || !lot.Contains(pc.transform.position)) continue;
                var cat = pc.Def.Category; string id = pc.Def.Id ?? "";
                if (id.StartsWith("dw-")) { drywall++; continue; }
                if ((cat == PieceCategory.Lumber || cat == PieceCategory.Sheet) && pc.State == PieceState.Fastened) framing++;
                else if (cat == PieceCategory.Plumbing || cat == PieceCategory.Mechanical || cat == PieceCategory.Electrical) mep++;
                else if (cat == PieceCategory.Door || cat == PieceCategory.Window || cat == PieceCategory.Appliance || cat == PieceCategory.GarageDoor) finish++;
            }
            foreach (var b in ElBox.All) if (b != null && lot.Contains(b.transform.position)) mep++;
            foreach (var s in Drywall.Surfaces) if (s != null && lot.Contains(s.transform.position)) drywall += s.Sheets.Count;
            float[] m =
            {
                yards / Mathf.Max(1f, t.ConcreteYards),
                framing / (float)Mathf.Max(1, t.FramingPieces),
                mep / (float)Mathf.Max(1, Mathf.CeilToInt(plan.SqFt / 100f) + 10 + plan.Baths2 + 2),
                drywall / (float)Mathf.Max(1, t.DrywallSheets),
                finish / (float)Mathf.Max(1, t.Windows + t.ExtDoors + t.IntDoors + 4),
            };
            for (int i = 0; i < 5; i++) p.Progress[i] = p.PhaseDone((BuildPhase)i) ? 1f : Mathf.Min(0.95f, m[i]);
        }

        // ------------------------------------------------------------------ off-site day step

        /// <summary>Phase cost is paid when work on the phase begins. Returns false (stall) if it can't be paid.</summary>
        public static Func<HouseProject, BuildPhase, int, bool> FundPhase = (p, ph, cost) =>
        {
            if (GameSettings.UnlimitedMoney || Game.Wallet.TrySpend(cost, $"{HousePlan.Label(ph)}: {p.Plan.Name} @ {p.Lot.Address}")) { p.CostSpent += cost; return true; }
            return false;
        };

        public static int PhaseCost(HouseProject p, BuildPhase ph)
        {
            int c = p.Plan.Takeoff.PhaseCost(ph);
            if (ph == BuildPhase.Finish) c = Mathf.RoundToInt(c * HousePlan.FinishFactor(p.FinishLevel) / HousePlan.FinishFactor(p.Plan.BaseQuality));
            return c;
        }

        /// <summary>Advances every off-site project by one working day. Deterministic.</summary>
        public static void DayStep(int day)
        {
            foreach (var p in All.ToArray())
            {
                if (p.Complete || p.Id == OnSiteId) continue;
                if (!Developments.LotReady(p)) continue;
                var ph = p.CurrentPhase; if (!ph.HasValue) continue;
                int i = (int)ph.Value;
                if (!p.Funded[i])
                {
                    if (!(p.Kind == ProjectKind.Spec ? Developments.FundSpec(p, ph.Value, PhaseCost(p, ph.Value)) : FundPhase(p, ph.Value, PhaseCost(p, ph.Value)))) { Game.Toast($"{p.Lot.Address}: can't fund {HousePlan.Label(ph.Value)}, work stopped"); continue; }
                    p.Funded[i] = true;
                }
                p.Progress[i] = Mathf.Min(1f, p.Progress[i] + Crew.DailyProgress(p, ph.Value, day));
                if (p.Progress[i] >= 0.999f)
                {
                    // the crew calls for the inspection; the inspector signs it off
                    if (ph.Value == BuildPhase.Foundation) { MarkPassed(p, InspectionPhase.Footing); if (p.Plan.RequiredInspections.Contains(InspectionPhase.UnderSlab)) MarkPassed(p, InspectionPhase.UnderSlab); }
                    else MarkPassed(p, HousePlan.InspectionFor(ph.Value));
                }
                Changed?.Invoke(p);
            }
            var on = OnSite; if (on != null) Measure(on);
        }

        public static void ResetState() { All.Clear(); OnSiteId = null; _seq = 0; }
        public static int Seq { get => _seq; set => _seq = value; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { ResetState(); Crew = new SimpleCrewProgress(); PhaseCompleted = null; Changed = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Inspections.Completed += OnInspection;
            Company.DayStarted += d => { if (GameSettings.IsCareer) DayStep(d); };
        }
    }
}
