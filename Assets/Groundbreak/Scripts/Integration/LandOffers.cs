using System;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Developer-stage work on the job board: "Develop N acres at ..." offers (JobKind.Development) for land that is for sale.
    /// Accepting one buys the land through Developments.BuyLand (it fails, and the offer goes back on the board, when the
    /// company can't pay); the job completes when every house in that subdivision is sold (Developments.Completed). The pay is
    /// the house sales themselves, so the job's own Payout is 0; completing it still counts as a job and moves reputation.
    /// </summary>
    public static class LandOffers
    {
        public const string IdPrefix = "land-";
        public const int MaxOffers = 2;

        [Serializable] class Payload { public string tract; }
        public static string TractOf(Job j)
        {
            if (j == null || j.Kind != JobKind.Development || string.IsNullOrEmpty(j.Payload)) return null;
            try { return JsonUtility.FromJson<Payload>(j.Payload).tract; } catch { return null; }
        }

        /// <summary>Wide enough for Developments.Subdivide: a 50' street plus 100'-deep lots on both sides.</summary>
        public static bool Platable(DevLot t) =>
            t != null && (t.FrontageM - Developments.StreetRowFt * Units.Foot) / 2f >= 100f * Units.Foot && t.DepthM >= 60f * Units.Foot;

        /// <summary>Keeps up to two land listings on the board while the company is a Developer.</summary>
        public static void Refresh()
        {
            if (!GameSettings.IsCareer || Company.Stage < CompanyStage.Developer) return;
            int have = Company.Offers.FindAll(j => TractOf(j) != null).Count;
            if (have >= MaxOffers) return;
            foreach (var t in DevLots.Provider.LandForSale())
            {
                if (have >= MaxOffers) break;
                if (!Platable(t)) continue;
                if (t.Claimed || Company.Offers.Exists(j => TractOf(j) == t.Id) || Company.ActiveJobs.Exists(j => TractOf(j) == t.Id)) continue;
                int price = Developments.LandPrice(t);
                Company.Offers.Add(new Job
                {
                    Id = IdPrefix + t.Id + "-" + Projects.Day, Kind = JobKind.Development, Client = "Land listing",
                    Title = $"Develop {t.Acres:0.0} acres: {t.Address}",
                    Description = $"Raw land for sale at about ${price:N0} (+2% closing). Buy it, plat lots, put in the street and utilities, " +
                                  "build spec houses with your crews and sell them. Done when every house is sold.",
                    Address = t.Address, WorldPos = t.FrontMid, Payout = 0, MaterialBudget = price, MinStage = CompanyStage.Developer, MinReputation = 80,
                    Payload = JsonUtility.ToJson(new Payload { tract = t.Id }),
                });
                have++;
            }
            Company.NotifyChanged();
        }

        static void OnAccepted(Job j)
        {
            var id = TractOf(j); if (id == null) return;
            if (Developments.All.Exists(d => d.TractId == id)) return;         // reloaded / already bought
            var tract = DevLots.Provider.Find(id);
            if (!Developments.BuyLand(tract, out _, out var msg))
            {
                // could not close: the listing goes back on the board
                j.Accepted = false; Company.ActiveJobs.Remove(j); if (!Company.Offers.Contains(j)) Company.Offers.Add(j);
                Game.Toast("Land purchase failed: " + msg);
                Company.NotifyChanged();
                return;
            }
            Game.Toast($"Bought {tract.Address}. Plat it in the Development app.");
        }

        static void OnDevelopmentDone(Development d)
        {
            var j = Company.ActiveJobs.Find(x => TractOf(x) == d.TractId);
            if (j == null) return;
            float q = 0f; int n = 0;
            foreach (var h in d.Houses) { q += h.Quality; n++; }
            Company.Complete(j, n > 0 ? q / n : 0.8f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Company.JobAccepted += OnAccepted;
            Company.DayStarted += _ => Refresh();
            Company.StageChanged += s => Refresh();
            Developments.Completed += OnDevelopmentDone;
        }
    }
}
