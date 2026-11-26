using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The trade a catalog job belongs to (drives the Jobs app grouping and crew matching).</summary>
    public enum JobTrade { Plumbing, Electrical, Hvac, Carpentry, Drywall, Painting, Exterior, Roofing, Concrete, Masonry, Flooring, Tile, Cabinets, Framing, General }

    /// <summary>How a catalog job is played.</summary>
    public enum JobMode
    {
        /// <summary>Service call: one defect at a customer house, hidden cause, steps with hand tools (RepairType.Catalog defect).</summary>
        Service,
        /// <summary>Contractor scope (remodel): several service-style tasks at one house, billed time and materials.</summary>
        Scope,
        /// <summary>Contractor job built on a Fleet ContractorJobs type (deck, fence, shed, garage, driveway, patio, reroof), verified on the lot.</summary>
        Fleet,
        /// <summary>New building on a lot from a blueprint plan (Builder / Developer), run as a Development HouseProject with draws.</summary>
        Build,
    }

    public enum StepKind { Prep, Diagnose, Fix, Finish }

    [Flags] public enum Seasons { None = 0, Winter = 1, Spring = 2, Summer = 4, Fall = 8, All = 15 }

    /// <summary>One thing the worker does. Order is enforced by <see cref="After"/> (steps done before) and <see cref="Need"/> (a state that
    /// must be open right now, e.g. "water-off"). <see cref="Opens"/> starts such a state; a step with <see cref="Closes"/> ends it. A job is not
    /// fixed while any state is still open (cover left off, water left off).</summary>
    public class StepDef
    {
        public string Id, Label, Tool, Say, Sound = "click";
        public StepKind Kind;
        public float Seconds = 3f, WaitMinutes;
        public bool Opens, Dry, Optional, Test;
        public string Closes;
        public readonly List<string> After = new List<string>(), Need = new List<string>();
        public readonly List<(string id, int n)> Mats = new List<(string, int)>();
        public readonly Dictionary<string, string> SayIf = new Dictionary<string, string>();
        /// <summary>A state that should be open (power off, water off) or the step goes badly.</summary>
        public string Guard, Hazard;
    }

    /// <summary>A hidden real cause. Diagnosis shows <see cref="Finding"/>; it is fixed when every step in <see cref="Fix"/> is done.</summary>
    public class CauseDef
    {
        public string Id, Name, Finding;
        public float Weight = 1f, Hours = 1f;
        public int MinRep;
        public string[] Fix = new string[0];
    }

    /// <summary>Where a job happens. Service jobs: a <see cref="HouseSpot"/> anchor on a town house, optionally moved by
    /// <see cref="Place"/> (house-local pose). Any TownHouse with all <see cref="Needs"/> spots qualifies, so new map houses work too.</summary>
    public class JobPlace
    {
        public string Name;                         // "toilet", "water heater"...
        public HouseSpot Spot;
        public HouseSpot[] Needs = new HouseSpot[0];
        public Func<TownHouse, Pose> Place;         // house-local pose; null = the spot's own anchor
        public bool Outdoor, Ladder, HideFixture;
        public string Room = "";                    // "bath", "kitchen", "living", "bedroom", "exterior", "yard", "roof"
    }

    /// <summary>
    /// One job in the catalog (data, not a class per job). See Docs/JOBS.md for the format and how to add one.
    /// </summary>
    public class JobDef
    {
        public string Id, Name, Category = "";
        public JobMode Mode;
        public CompanyStage Stage;
        public JobKind Kind = JobKind.Repair;
        public readonly List<JobTrade> Trades = new List<JobTrade>();
        public readonly List<string> Complaints = new List<string>();     // what the customer says (one is picked)
        public readonly List<CauseDef> Causes = new List<CauseDef>();
        public readonly List<StepDef> Steps = new List<StepDef>();
        public readonly List<string> ExtraTools = new List<string>();
        public JobPlace Where;
        public string Prop;                          // CatalogProps model id
        public Vector3 PropSize = new Vector3(0.6f, 0.6f, 0.4f);
        public string Vehicle;                       // null: the work van (service) / any
        public int MinRep, Callout;                  // Callout: extra $ on the estimate (permits, dump fees)
        public float Weight = 1f;                    // rarity (relative)
        public Seasons Season = Seasons.All;
        public float RainBoost = 1f, ColdBoost = 1f, HeatBoost = 1f;

        // Scope (remodel): the service tasks done in this job
        public readonly List<string> Tasks = new List<string>();
        // Fleet
        public string FleetType;
        public float W, L, H, Thick; public int Gates, Pitch = 6, Psi;
        public float RatePerUnit;                    // $/sq ft, $/LF, $/square
        public int DaysToBuild;
        // Build
        public readonly List<string> PlanIds = new List<string>();
        public string FallbackFleet;                 // build an outbuilding with this Fleet type if no plan is found
        public int PricePerSqFt;
        public string BuildingType = "house";
        // Legacy repair families (Scripts/Repairs) offered through the same generator
        public RepairType LegacyType; public int LegacyCauses;        // house, garage, barn, shop, office, warehouse, retail, duplex

        StepDef _last;

        // ------------------------------------------------------------------ fluent builder

        public JobDef Says(params string[] lines) { Complaints.AddRange(lines); return this; }
        public JobDef At(JobPlace p) { Where = p; return this; }
        public JobDef Model(string prop, float w, float h, float d) { Prop = prop; PropSize = new Vector3(w, h, d); return this; }
        public JobDef Rep(int r) { MinRep = r; return this; }
        public JobDef Rare(float w) { Weight = w; return this; }
        public JobDef In(Seasons s) { Season = s; return this; }
        public JobDef Boost(float rain = 1f, float cold = 1f, float heat = 1f) { RainBoost = rain; ColdBoost = cold; HeatBoost = heat; return this; }
        public JobDef Trade(params JobTrade[] t) { foreach (var x in t) if (!Trades.Contains(x)) Trades.Add(x); return this; }
        public JobDef Tools(params string[] t) { ExtraTools.AddRange(t); return this; }
        public JobDef Van(string v) { Vehicle = v; return this; }

        /// <summary>Hidden cause: name, relative likelihood, book hours, what the diagnosis shows, the steps that fix it.</summary>
        public JobDef Cause(string id, string name, float weight, float hours, string finding, params string[] fix)
        {
            Causes.Add(new CauseDef { Id = id, Name = name, Weight = weight, Hours = hours, Finding = finding, Fix = fix });
            return this;
        }

        JobDef Add(StepKind k, string id, string label, string tool, float sec)
        {
            _last = new StepDef { Id = id, Label = label, Tool = tool, Seconds = sec, Kind = k };
            Steps.Add(_last);
            return this;
        }
        public JobDef Prep(string id, string label, string tool = null, float sec = 2f) => Add(StepKind.Prep, id, label, tool, sec);
        public JobDef Diag(string id, string label, string tool = null, float sec = 3f) { Add(StepKind.Diagnose, id, label, tool, sec); _last.Sound = "click"; return this; }
        public JobDef Fix(string id, string label, string tool = null, float sec = 4f) => Add(StepKind.Fix, id, label, tool, sec);
        public JobDef Finish(string id, string label, string tool = null, float sec = 2f) => Add(StepKind.Finish, id, label, tool, sec);

        // modifiers on the last step
        public JobDef Mat(string id, int n = 1) { _last.Mats.Add((id, n)); return this; }
        public JobDef After(params string[] ids) { _last.After.AddRange(ids); return this; }
        public JobDef Need(params string[] ids) { _last.Need.AddRange(ids); return this; }
        public JobDef Opens() { _last.Opens = true; return this; }
        public JobDef Closes(string id) { _last.Closes = id; return this; }
        public JobDef Say(string text) { _last.Say = text; return this; }
        public JobDef SayIf(string cause, string text) { _last.SayIf[cause] = text; return this; }
        public JobDef Guard(string openState, string hazard) { _last.Guard = openState; _last.Hazard = hazard; return this; }
        public JobDef Wait(float minutes) { _last.WaitMinutes = minutes; return this; }
        public JobDef Dry() { _last.Dry = true; return this; }
        public JobDef Opt() { _last.Optional = true; return this; }
        /// <summary>A check whose result depends on whether the job is fixed now (run water, flip the breaker on...).</summary>
        public JobDef Test() { _last.Test = true; return this; }
        public JobDef Snd(string s) { _last.Sound = s; return this; }

        // ------------------------------------------------------------------ derived data

        public StepDef Step(string id) => Steps.Find(s => s.Id == id);
        public CauseDef CauseById(string id) => Causes.Find(c => c.Id == id);

        /// <summary>Every tool the steps use (alternatives "a|b" kept whole) plus extra tools.</summary>
        public List<string> RequiredTools
        {
            get
            {
                var l = new List<string>();
                foreach (var s in Steps) if (!string.IsNullOrEmpty(s.Tool) && !l.Contains(s.Tool)) l.Add(s.Tool);
                foreach (var t in ExtraTools) if (!l.Contains(t)) l.Add(t);
                return l;
            }
        }

        /// <summary>Material ids any step may use.</summary>
        public List<string> RequiredMaterials
        {
            get
            {
                var l = new List<string>();
                foreach (var s in Steps) foreach (var (id, _) in s.Mats) if (!l.Contains(id)) l.Add(id);
                return l;
            }
        }

        /// <summary>Parts cost (our cost) of the steps that fix a cause plus the prep/finish steps that use parts.</summary>
        public float MaterialCostFor(CauseDef c)
        {
            float cost = 0f;
            foreach (var s in Steps)
            {
                bool used = s.Kind != StepKind.Fix || Array.IndexOf(c.Fix, s.Id) >= 0;
                if (!used || s.Optional) continue;
                foreach (var (id, n) in s.Mats) cost += RepairCatalog.UnitCost(id) * n;
            }
            return cost;
        }

        /// <summary>Book hours range over the possible causes (service) or the sum of the tasks (scope).</summary>
        public Vector2 HoursRange
        {
            get
            {
                if (Mode == JobMode.Scope)
                {
                    float lo = 0f, hi = 0f;
                    foreach (var t in Tasks) { var d = JobCatalog.Get(t); if (d == null) continue; var r = d.HoursRange; lo += r.x; hi += r.y; }
                    return new Vector2(lo, hi);
                }
                if (Mode == JobMode.Fleet || Mode == JobMode.Build) return new Vector2(DaysToBuild * 8f, DaysToBuild * 8f * 1.3f);
                float a = float.MaxValue, b = 0f;
                foreach (var c in Causes) { a = Mathf.Min(a, c.Hours); b = Mathf.Max(b, c.Hours); }
                return Causes.Count == 0 ? Vector2.zero : new Vector2(a, b);
            }
        }

        /// <summary>Invoice range: book labour + parts with markup + trip (service/scope), or the contract price range (fleet/build).</summary>
        public Vector2Int PriceRange
        {
            get
            {
                if (Mode == JobMode.Scope)
                {
                    int lo = RepairJobs.TripCharge + Callout, hi = RepairJobs.TripCharge + Callout;
                    foreach (var t in Tasks) { var d = JobCatalog.Get(t); if (d == null) continue; var r = d.PriceRange; lo += r.x - RepairJobs.TripCharge; hi += r.y - RepairJobs.TripCharge; }
                    return new Vector2Int(lo, hi);
                }
                if (Mode == JobMode.Fleet) { int p = JobCatalog.FleetPrice(this); return new Vector2Int(Mathf.RoundToInt(p * 0.9f), Mathf.RoundToInt(p * 1.1f)); }
                if (Mode == JobMode.Build)
                {
                    var plan = JobCatalog.PlanFor(this);
                    float sq = plan != null ? plan.SqFt : W * L;
                    return new Vector2Int(Mathf.RoundToInt(sq * PricePerSqFt * 0.92f), Mathf.RoundToInt(sq * PricePerSqFt * 1.08f));
                }
                int min = int.MaxValue, max = 0;
                foreach (var c in Causes)
                {
                    int v = Mathf.RoundToInt(c.Hours * RepairJobs.LaborRate + MaterialCostFor(c) * RepairJobs.Markup) + RepairJobs.TripCharge + Callout;
                    min = Mathf.Min(min, v); max = Mathf.Max(max, v);
                }
                return Causes.Count == 0 ? Vector2Int.zero : new Vector2Int(min, max);
            }
        }

        public override string ToString() => Id;
    }
}
