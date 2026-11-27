using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Business (Career) tutorials for the Tutorials tablet app: the van and service calls through to developing a
    /// neighborhood. Plain step-by-step text; every step has a real picture (bt_*.png in Resources/Tutorials/, captured by
    /// BusinessTutorialTests). Registered additively into the existing <see cref="Tutorials"/> registry under its own phase,
    /// listed right after "Getting started".</summary>
    public static partial class BusinessTutorials
    {
        public const string Phase = "Running the business";
        public static readonly string[] Ids =
        {
            "biz-handyman", "biz-van", "biz-hardware", "biz-warehouse", "biz-contractor", "biz-crew",
            "biz-finance", "biz-plans", "biz-builder", "biz-developer", "biz-reputation",
        };

        public static List<Tutorial> All()
        {
            var l = new List<Tutorial>();
            foreach (var id in Ids) { var t = Tutorials.Get(id); if (t != null) l.Add(t); }
            return l;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Tutorials.PhaseOrder.Remove(Phase);
            Tutorials.PhaseOrder.Insert(Mathf.Min(1, Tutorials.PhaseOrder.Count), Phase);
            Handyman(); Van(); Hardware(); Warehouse(); Contractor(); CrewTut(); Finance(); Plans(); BuilderTut(); Developer(); Reputation();
            Tablet.AddHelp("Tutorials app → 'Running the business': how to make money, from your first service call to selling a neighborhood.");
        }

        // ------------------------------------------------------------------ "show me where" locators

        static Vector3? VanSpot() => WorkVan.I != null ? WorkVan.I.transform.position : (Vector3?)null;
        static Vector3? StoreSpot() => Town.Exists && Town.Store != null ? Town.Store.Parking : (Vector3?)null;
        static Vector3? YardSpot() => YardSite.I != null ? YardSite.I.transform.position : (Vector3?)null;

        static Tutorial New(string id, string title, string summary) => Tutorials.New(id, Phase, title, summary);

        // content: BusinessTutorialContent.cs
    }
}
