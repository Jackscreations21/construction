using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Per-frame glue between the business features (see Docs/HANDOFF-integration.md):
    /// - the tablet: one "Jobs" app for every kind of offer, business apps in the "Business" group and hidden in Free Build;
    /// - crew arrival at town jobs (their truck pulls up at the lot's parking spot);
    /// - the work van parks in its yard stall when the company first leases premises; the yard redraws when the van comes and goes;
    /// - Company.RecomputeStage once a day (reputation also changes outside Company.Complete).
    /// </summary>
    public class IntegrationRuntime : MonoBehaviour
    {
        public static IntegrationRuntime I;

        /// <summary>Business apps: grouped under "Business", shown in Career only.</summary>
        public static readonly string[] BusinessApps = { "Jobs", "Van", "Hardware Store", "Dealership", "Company", "Warehouse", "Crew", "Development" };
        /// <summary>Apps whose screens now live as tabs of the "Jobs" app.</summary>
        public static readonly string[] MergedIntoJobs = { ContractorJobsApp.AppName, "Builder" };

        bool _vanWas; int _yardLevel; Vector3 _yardPos; Vector3? _arrivalSet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("IntegrationRuntime");
            DontDestroyOnLoad(go);
            I = go.AddComponent<IntegrationRuntime>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Company.DayStarted += _ => { if (GameSettings.IsCareer) Company.RecomputeStage(); };
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        // Start runs after every [RuntimeInitializeOnLoadMethod], so the feature apps (some register AfterSceneLoad) exist by now.
        void Start() { SetupTablet(); _yardLevel = Company.WarehouseLevel; _yardPos = Yard.Position; }

        public static void SetupTablet()
        {
            foreach (var a in MergedIntoJobs) Tablet.RemoveApp(a);
            Tablet.RegisterApp(JobsHub.AppName, JobsHub.Draw);
            foreach (var a in BusinessApps)
            {
                Tablet.SetAppGroup(a, "Business");
                Tablet.SetAppVisible(a, () => GameSettings.IsCareer);
            }
            // the original Career contract board: career only (in Free Build it was a placeholder screen)
            Tablet.SetAppVisible("Job Board", () => GameSettings.IsCareer);
        }

        void Update()
        {
            // the yard draws the work van's stall empty while the van exists in town; redraw when that changes
            bool van = WorkVan.I != null;
            if (van != _vanWas) { _vanWas = van; Yard.MarkDirty(); }

            // first lease, or a move to bigger premises: the van moves into its stall there (if nobody is driving it and it was
            // at home / at the old yard, not out on a job)
            if (Company.WarehouseLevel != _yardLevel)
            {
                bool first = _yardLevel == 0 && Company.WarehouseLevel > 0;
                bool atOldYard = WorkVan.I != null && Vector3.Distance(WorkVan.I.transform.position, _yardPos) < 120f;
                _yardLevel = Company.WarehouseLevel;
                if (Company.WarehouseLevel > 0 && (first || atOldYard)) ParkVanAtYard();
                _yardPos = Yard.Position;
            }

            // crew arrival point for town jobs
            var at = CrewTownLink.ArrivalFor(Crew.HereJobId);
            if (at.HasValue) { CrewManager.ArrivalOverride = at; _arrivalSet = at; }
            else if (_arrivalSet.HasValue) { if (CrewManager.ArrivalOverride == _arrivalSet) CrewManager.ArrivalOverride = null; _arrivalSet = null; }
        }

        /// <summary>Parks the work van in its own stall at the yard (the stall the yard would draw it in). False if it can't.</summary>
        public static bool ParkVanAtYard()
        {
            var v = UnifiedVan.DrivableVan;
            if (WorkVan.I == null || WorkVan.I.Driving || v == null) return false;
            var site = YardSite.I;
            if (site == null && Company.WarehouseLevel > 0) site = YardSite.Rebuild();
            if (site == null || !site.TryStall(Company.Vehicles.IndexOf(v), out var pos, out var yaw)) return false;
            WorkVan.I.TeleportTo(pos, yaw);
            return true;
        }
    }

    /// <summary>The one "Jobs" tablet app: every offer the company can see, by kind, in tabs. Each tab is the owning feature's own screen.</summary>
    public static class JobsHub
    {
        public const string AppName = "Jobs";
        static readonly string[] Tabs = { "Service calls", "Contractor", "Houses", "Land" };
        static int _tab;

        public static int Tab { get => _tab; set => _tab = Mathf.Clamp(value, 0, Tabs.Length - 1); }

        /// <summary>Offers + active jobs per tab, for the tab labels.</summary>
        static int Count(int tab)
        {
            int n = 0;
            foreach (var j in Company.Offers) if (TabOf(j) == tab) n++;
            foreach (var j in Company.ActiveJobs) if (TabOf(j) == tab) n++;
            return n;
        }

        public static int TabOf(Job j)
        {
            if (RepairJobs.IsService(j)) return 0;   // job-catalog remodel scopes ("jcr-") are drawn by the service-call screen
            switch (j.Kind)
            {
                case JobKind.Repair: return 0;
                case JobKind.Remodel: case JobKind.Outbuilding: return 1;
                case JobKind.House: return 2;
                default: return 3;
            }
        }

        public static void Draw()
        {
            var t = Tablet.I; if (t == null) return;
            if (!GameSettings.IsCareer) { GUILayout.Label("Jobs are for Career mode. (Main menu: F10)", t.HeaderStyle); return; }
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Tabs.Length; i++)
            {
                int n = Count(i);
                if (GUILayout.Toggle(_tab == i, n > 0 ? $"{Tabs[i]} ({n})" : Tabs[i], t.ButtonStyle) && _tab != i) _tab = i;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            switch (_tab)
            {
                case 0: RepairJobsApp.Draw(); break;
                case 1: ContractorJobsApp.Draw(); break;
                case 2: DevApps.BuilderApp(); break;
                default: DrawLand(t); break;
            }
        }

        static void DrawLand(Tablet t)
        {
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle; float fs = label.fontSize;
            if (Company.Stage < CompanyStage.Developer)
            {
                GUILayout.Label("Land development unlocks at the Developer stage: headquarters (warehouse level 3), 8+ workers and reputation 80.", header);
                return;
            }
            foreach (var j in new List<Job>(Company.ActiveJobs))
            {
                if (LandOffers.TractOf(j) == null) continue;
                GUILayout.Label("ACTIVE: " + j.Title, header);
                GUILayout.Label("Plat, site work, spec houses and sales: Development app.", label);
            }
            foreach (var j in new List<Job>(Company.Offers))
            {
                if (LandOffers.TractOf(j) == null) continue;
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label(j.Title, header);
                GUILayout.Label(j.Description, label);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                string why = Company.Blocker(j);
                GUI.enabled = why == null;
                if (GUILayout.Button(why == null ? "Buy land" : why, button, GUILayout.Width(fs * 9)))
                {
                    Company.Accept(j, out var m); t.ShowMessage(m);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (!Company.Offers.Exists(j => LandOffers.TractOf(j) != null)) GUILayout.Label("No land listed right now. New listings come in each morning.", label);
        }
    }
}
