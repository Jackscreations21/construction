using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The player's own plans ("My plans"). Saved designs persist in the save game (extension "plan-designer"), and every one
    /// that passes the validator is registered into <see cref="HousePlans"/> with an id "custom-*", so the Plans app, the plan overlay,
    /// client builds (<see cref="OfferToClient"/>), spec houses (Development app) and crews build it like a library plan.
    /// Drafts with problems are kept but not registered until they pass. See Docs/PLAN-DESIGNER.md.</summary>
    public static class PlanDesigner
    {
        public const string IdPrefix = "custom-", Category = "my-plan", SaveKey = "plan-designer";
        static readonly List<PlanDesign> _saved = new List<PlanDesign>();
        public static IReadOnlyList<PlanDesign> Saved => _saved;
        /// <summary>Raised when a plan is saved, renamed or deleted (or the list is reloaded).</summary>
        public static event Action Changed;

        public static PlanDesign GetDesign(string id) => _saved.Find(d => d.id == id);
        public static bool IsCustom(HousePlan p) => p != null && p.Id != null && p.Id.StartsWith(IdPrefix);
        /// <summary>The registered plan of a saved design (null while it's a draft with problems).</summary>
        public static HousePlan PlanOf(string id) { var p = HousePlans.Get(id); return IsCustom(p) ? p : null; }
        public static bool IsRegistered(string id) => PlanOf(id) != null;

        // ------------------------------------------------------------------ save / rename / delete

        static string UniqueId(string name)
        {
            string b = PlanDesign.IdFor(name), id = b; int n = 2;
            while (_saved.Exists(d => d.id == id) || (HousePlans.Get(id) != null && !IsCustom(HousePlans.Get(id)))) id = b + "-" + n++;
            return id;
        }

        /// <summary>Saves the editor's design (new id on first save or with asNew). Registers it when it passes the validator.
        /// A plan already used by a project or job can't be overwritten: save it as a new plan.</summary>
        public static bool Save(DesignEditor ed, bool asNew, out string msg)
        {
            var d = ed.Design;
            if (string.IsNullOrWhiteSpace(d.name)) d.name = "My plan";
            d.name = d.name.Trim();
            if (!asNew && !string.IsNullOrEmpty(d.id) && InUse(d.id, out string where) && GetDesign(d.id)?.ToJson() != d.ToJson())
            { msg = $"\"{GetDesign(d.id)?.name}\" is being built ({where}). Use Save as new so that job keeps its plan."; return false; }
            if (asNew || string.IsNullOrEmpty(d.id) || GetDesign(d.id) == null) d.id = UniqueId(d.name);
            if (string.IsNullOrEmpty(d.created)) d.created = DateTime.Now.ToString("yyyy-MM-dd");
            var copy = d.Clone();
            int i = _saved.FindIndex(x => x.id == copy.id);
            if (i >= 0) _saved[i] = copy; else _saved.Add(copy);
            bool ok = Register(copy, out int errors);
            ed.Changed();
            msg = ok ? $"Saved \"{copy.name}\" — it's in the Plans app under My plans and ready to build."
                     : $"Saved \"{copy.name}\" as a draft: fix {errors} problem{(errors == 1 ? "" : "s")} and save again to build it.";
            Changed?.Invoke();
            return true;
        }

        /// <summary>Registers (or, with problems, unregisters) one saved design. Returns true when it is buildable.</summary>
        static bool Register(PlanDesign d, out int errors, bool keepDraft = false)
        {
            Blueprints.Ensure();   // library first so "My plans" list after it
            var plan = d.Build();
            errors = 0; foreach (var i in BlueprintValidator.Validate(plan)) if (i.Error) errors++;
            if (errors > 0) { if (!keepDraft && !InUse(d.id, out _)) HousePlans.Unregister(d.id); else HousePlans.Register(plan); return false; }
            HousePlans.Register(plan);
            return true;
        }

        public static bool Rename(string id, string name, out string msg)
        {
            var d = GetDesign(id);
            if (d == null || string.IsNullOrWhiteSpace(name)) { msg = "Type a name first."; return false; }
            d.name = name.Trim();
            var p = HousePlans.Get(id); if (IsCustom(p)) p.Name = d.name;
            msg = $"Renamed to \"{d.name}\".";
            Changed?.Invoke();
            return true;
        }

        /// <summary>Deletes a saved plan (refused while a project or job uses it).</summary>
        public static bool Delete(string id, out string msg)
        {
            var d = GetDesign(id);
            if (d == null) { msg = "No such plan."; return false; }
            if (InUse(id, out string where)) { msg = $"\"{d.name}\" is used by {where}: it can't be deleted until that's gone."; return false; }
            _saved.Remove(d);
            HousePlans.Unregister(id);
            if (PlanOverlay.Plan != null && PlanOverlay.Plan.Id == id) PlanOverlay.Hide();
            msg = $"Deleted \"{d.name}\".";
            Changed?.Invoke();
            return true;
        }

        /// <summary>True when a house project, offer or active job refers to the plan id.</summary>
        public static bool InUse(string id, out string where)
        {
            where = null;
            var p = Projects.All.Find(x => x.PlanId == id);
            if (p != null) { where = "the project at " + (p.Lot != null ? p.Lot.Address : p.LotId); return true; }
            string key = "\"plan\":\"" + id + "\"";
            var j = Company.Offers.Find(x => x.Payload != null && x.Payload.Contains(key)) ?? Company.ActiveJobs.Find(x => x.Payload != null && x.Payload.Contains(key));
            if (j != null) { where = "the job \"" + j.Title + "\""; return true; }
            return false;
        }

        // ------------------------------------------------------------------ building it

        /// <summary>Puts a client contract for this plan on the Company job board (Career, Builder stage+): a free lot that fits,
        /// priced per sq ft like the job catalog's builds, with lender draws on inspections.</summary>
        public static Job OfferToClient(HousePlan plan, out string msg)
        {
            msg = null;
            if (plan == null || !IsCustom(plan)) { msg = "Save the plan (with no problems) first."; return null; }
            if (!GameSettings.IsCareer) { msg = "Client contracts are part of Career mode."; return null; }
            string baseId = plan.Group == PlanGroup.Residential ? (plan.SqFt > 3000f ? "bld.estate" : "bld.ranch") : plan.Group == PlanGroup.Outbuilding ? "bld.workshop"
                          : plan.Group == PlanGroup.Commercial ? "bld.office" : "bld.warehouse";
            var src = JobCatalog.Get(baseId);
            if (src == null) { msg = "No client type for this kind of building."; return null; }
            var stage = (CompanyStage)Mathf.Max((int)src.Stage, (int)plan.MinStage);
            if (Company.Stage < stage) { msg = $"Clients want a {stage}-stage company for this (you're {Company.Stage})."; return null; }
            var d = new JobDef { Id = src.Id, Name = "Custom build", Stage = stage, Mode = JobMode.Build, Kind = JobKind.House, BuildingType = src.BuildingType, PricePerSqFt = src.PricePerSqFt, MinRep = src.MinRep, Category = "build" };
            d.Says("A client saw your plan and wants it built on their lot.");
            d.PlanIds.Add(plan.Id); d.Trade(src.Trades.ToArray());
            var job = JobCatalog.CreateBuild(d, true);
            msg = job != null ? $"Client found: {job.Client} at {job.Address} for ${job.Payout:N0}. Sign it in the Builder / Jobs app."
                              : $"No free lot fits a {plan.WidthFt:0}' x {plan.DepthFt:0}' building right now.";
            return job;
        }

        /// <summary>Lays the plan out on the active site (Projects.OnSite), like the Plans app's overlay button.</summary>
        public static bool ShowOverlay(HousePlan plan, int floor, out string msg)
        {
            var on = Projects.OnSite;
            if (plan == null) { msg = "Nothing to show."; return false; }
            if (on == null) { msg = "Go to one of your building sites first (Builder app), then show the overlay."; return false; }
            PlanOverlay.Show(plan, PlanPlacement.On(on.Lot, plan), floor);
            msg = $"{plan.Name} laid out on {on.Lot.Address}.";
            return true;
        }

        // ------------------------------------------------------------------ persistence

        [Serializable] class SaveData { public List<PlanDesign> plans = new List<PlanDesign>(); }

        public static string SaveJson() => JsonUtility.ToJson(new SaveData { plans = new List<PlanDesign>(_saved) });

        /// <summary>Replaces the saved plans (null = new game: none) and re-registers them.</summary>
        public static void LoadJson(string json)
        {
            foreach (var d in _saved) HousePlans.Unregister(d.id);
            _saved.Clear();
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var s = JsonUtility.FromJson<SaveData>(json);
                    if (s?.plans != null) foreach (var d in s.plans) { if (d == null || string.IsNullOrEmpty(d.id)) continue; d.Fix(); _saved.Add(d); }
                }
                catch (Exception e) { Debug.LogWarning("[PlanDesigner] couldn't read saved plans: " + e.Message); }
            }
            // During a save-game load this extension runs before the projects/jobs that may build a draft: keep drafts with problems
            // registered until the whole save is in (SaveSystem.AfterLoad), then drop the ones nothing uses.
            foreach (var d in _saved) Register(d, out _, _loading);
            Changed?.Invoke();
        }

        static bool _loading;

        /// <summary>After a load: drafts with problems that no project, offer or job uses are unregistered again.</summary>
        public static void PruneDrafts()
        {
            _loading = false;
            foreach (var d in _saved)
            {
                var p = HousePlans.Get(d.id);
                if (!IsCustom(p) || InUse(d.id, out _)) continue;
                bool bad = false; foreach (var i in BlueprintValidator.Validate(p)) if (i.Error) { bad = true; break; }
                if (bad) HousePlans.Unregister(d.id);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void HookLoad()
        {
            SaveSystem.BeforeLoad += () => _loading = true;
            SaveSystem.AfterLoad += PruneDrafts;
        }

        // Registered at SubsystemRegistration so this extension loads before the ones whose projects refer to custom plan ids.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset()
        {
            _saved.Clear(); Changed = null; _loading = false;
            SaveSystem.RegisterExtension(SaveKey, SaveJson, LoadJson);
        }
    }
}
