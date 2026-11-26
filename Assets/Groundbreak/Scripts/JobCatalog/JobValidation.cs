using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public static partial class JobCatalog
    {
        static void RegisterParts() => JP.Register();

        static bool ToolExists(string id)
        {
            foreach (var alt in id.Split('|'))
            {
                bool found = false;
                foreach (var s in ToolFactory.ShopItems) if (s.Id == alt) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        /// <summary>Problems with a definition (empty list = valid): ids resolve, tools and materials exist, steps are reachable,
        /// every open state can be closed, hours and prices are in a sane range.</summary>
        public static List<string> Validate(JobDef d)
        {
            var e = new List<string>();
            void Err(string s) => e.Add(d.Id + ": " + s);
            if (string.IsNullOrEmpty(d.Name)) Err("no name");
            if (d.Complaints.Count == 0 && !IsLegacy(d)) Err("no customer description");
            switch (d.Mode)
            {
                case JobMode.Service:
                {
                    if (IsLegacy(d)) { if (d.LegacyCauses < 1) Err("legacy without causes"); break; }
                    if (d.Where == null) { Err("no location"); break; }
                    if (!CatalogProps.Exists(d.Prop)) Err("unknown model " + d.Prop);
                    if (d.Causes.Count < (IsTask(d) ? 1 : 2)) Err($"needs at least {(IsTask(d) ? 1 : 2)} hidden causes");
                    var ids = new HashSet<string>();
                    foreach (var s in d.Steps) if (!ids.Add(s.Id)) Err("duplicate step " + s.Id);
                    foreach (var c in d.Causes)
                    {
                        if (c.Weight <= 0f) Err($"cause {c.Id} has no weight");
                        if (string.IsNullOrEmpty(c.Finding)) Err($"cause {c.Id} has no finding");
                        if (c.Fix.Length == 0) Err($"cause {c.Id} has no fix steps");
                        foreach (var f in c.Fix) if (d.Step(f) == null) Err($"cause {c.Id} fix step '{f}' missing");
                        if (c.Hours < 0.2f || c.Hours > (IsTask(d) ? 40f : 10f)) Err($"cause {c.Id} hours {c.Hours} out of range");
                    }
                    foreach (var s in d.Steps)
                    {
                        foreach (var a in s.After) if (d.Step(a) == null) Err($"step {s.Id} after unknown '{a}'");
                        foreach (var n in s.Need) { var o = d.Step(n); if (o == null || !o.Opens) Err($"step {s.Id} needs '{n}' which is not an opening step"); }
                        if (!string.IsNullOrEmpty(s.Guard)) { var o = d.Step(s.Guard); if (o == null || !o.Opens) Err($"step {s.Id} guard '{s.Guard}' is not an opening step"); }
                        if (!string.IsNullOrEmpty(s.Closes)) { var o = d.Step(s.Closes); if (o == null || !o.Opens) Err($"step {s.Id} closes '{s.Closes}' which is not an opening step"); }
                        if (s.Opens && !d.Steps.Exists(x => x.Closes == s.Id)) Err($"open state '{s.Id}' is never closed");
                        if (!string.IsNullOrEmpty(s.Tool) && !ToolExists(s.Tool)) Err($"step {s.Id} tool '{s.Tool}' not in the tool shop");
                        foreach (var (id, n) in s.Mats) { if (Catalog.Get(id) == null) Err($"step {s.Id} material '{id}' not in the catalog"); if (n <= 0) Err($"step {s.Id} quantity {n}"); }
                        if (s.Seconds <= 0f || s.Seconds > 20f) Err($"step {s.Id} hold time {s.Seconds}");
                    }
                    var pr = d.PriceRange;
                    if (!IsTask(d) && (pr.x < 75 || pr.y > 3000)) Err($"service price {pr.x}-{pr.y} out of range");
                    break;
                }
                case JobMode.Scope:
                {
                    if (d.Tasks.Count == 0) Err("scope without tasks");
                    foreach (var t in d.Tasks) { var td = Get(t); if (td == null) Err("unknown task " + t); else if (!IsTask(td)) Err(t + " is not a task"); }
                    var names = new HashSet<string>();
                    foreach (var t in d.Tasks) { var td = Get(t); if (td != null && td.Where != null && !names.Add(td.Where.Name + td.Prop)) Err("two tasks at the same place: " + t); }
                    var pr = d.PriceRange;
                    if (pr.x < 500 || pr.y > 60000) Err($"scope price {pr.x}-{pr.y} out of range");
                    break;
                }
                case JobMode.Fleet:
                {
                    if (System.Array.IndexOf(ContractorJobs.Types, d.FleetType) < 0) Err("unknown fleet type " + d.FleetType);
                    if (d.L <= 0 || (d.FleetType != "fence" && d.W <= 0)) Err("no size");
                    int p = FleetPrice(d);
                    if (p < 1500 || p > 80000) Err($"price {p} out of range");
                    if (d.DaysToBuild < 1 || d.DaysToBuild > 30) Err("days " + d.DaysToBuild);
                    break;
                }
                case JobMode.Build:
                {
                    if (d.PlanIds.Count == 0) Err("no plan ids");
                    if (d.PricePerSqFt < 30 || d.PricePerSqFt > 400) Err("price per sq ft " + d.PricePerSqFt);
                    if (d.Stage < CompanyStage.Builder) Err("builds need Builder stage");
                    break;
                }
            }
            if (d.Mode != JobMode.Build && d.Stage >= CompanyStage.Builder) Err("only builds are Builder/Developer work");
            return e;
        }

        public static List<string> ValidateAll()
        {
            var e = new List<string>();
            foreach (var d in All) e.AddRange(Validate(d));
            return e;
        }

        public static int CountWhere(System.Func<JobDef, bool> f) { int n = 0; foreach (var d in All) if (f(d)) n++; return n; }
    }
}
