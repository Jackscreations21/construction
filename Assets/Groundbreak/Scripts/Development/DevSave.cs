using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Saves projects, developments, claimed fallback lots and the on-site project under SaveSystem key "development".</summary>
    public static class DevSave
    {
        [Serializable]
        class Data
        {
            public List<HouseProject> projects = new List<HouseProject>();
            public List<Development> devs = new List<Development>();
            public List<string> claimed = new List<string>();
            public string onSite; public int seq;
        }

        public static string Save()
        {
            var d = new Data { onSite = Projects.OnSiteId, seq = Projects.Seq };
            var on = Projects.OnSite; if (on != null) on.InspectionState = Inspections.Save();
            d.projects.AddRange(Projects.All); d.devs.AddRange(Developments.All);
            var prov = DevLots.Provider;
            foreach (var p in Projects.All) if (prov.Find(p.LotId) is DevLot l && l.Claimed) d.claimed.Add(l.Id);
            foreach (var dv in Developments.All) d.claimed.Add(dv.TractId);
            return JsonUtility.ToJson(d);
        }

        public static void Load(string json)
        {
            PlanOverlay.Hide();
            var prov = DevLots.Provider;
            foreach (var p in Projects.All) prov.Release(prov.Find(p.LotId));
            foreach (var dv in Developments.All) prov.Release(prov.Find(dv.TractId));
            Projects.ResetState(); Developments.ResetState();
            if (!string.IsNullOrEmpty(json))
            {
                var d = JsonUtility.FromJson<Data>(json);
                Projects.All.AddRange(d.projects); Developments.All.AddRange(d.devs); Projects.Seq = d.seq;
                // JsonUtility writes null strings as "": restore the nulls the rest of the code tests for (spec houses have no JobId,
                // client houses no DevId, WarrantyJobId is null until the callback is issued)
                foreach (var p in Projects.All)
                {
                    if (p.JobId == "") p.JobId = null;
                    if (p.DevId == "") p.DevId = null;
                    if (p.WarrantyJobId == "") p.WarrantyJobId = null;
                }
                foreach (var id in d.claimed) prov.Claim(prov.Find(id), Company.Name);
                var on = Projects.Get(d.onSite);
                if (on != null)
                {
                    // the "inspections" extension may load after us: restore the on-site state explicitly
                    Projects.OnSiteId = on.Id;
                    Site.Lot = on.Lot.Rect; Site.LotName = on.Plan.Name + " @ " + on.Lot.Address;
                    Inspections.Load(on.InspectionState); Inspections.RequiredPhases = on.Plan.RequiredInspections;
                    PlanOverlay.Show(on.Plan, on.Placement, 0);
                }
            }
            DevVisuals.Refresh();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension("development", Save, Load);
            // the inspections extension restores the last active site's state; re-apply ours after every load
            SaveSystem.AfterLoad += () =>
            {
                var on = Projects.OnSite; if (on == null) return;
                Inspections.Load(on.InspectionState); Inspections.RequiredPhases = on.Plan.RequiredInspections;
                Site.Lot = on.Lot.Rect;
            };
        }
    }
}
