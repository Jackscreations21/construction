using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Severe storms (Career): when a storm day arrives it visibly hits town (damage on map houses via <see cref="StormDamage"/>)
    /// and for a week the job board fills with matching repairs from the job catalog at storm premium pay.</summary>
    public static class StormEvents
    {
        public const int SurgeDays = 7;
        public const float Premium = 1.4f;
        public const string TitlePrefix = "Storm damage: ";

        public static StormKind Kind = StormKind.None;
        public static int StartDay = -1;
        public static readonly List<string> DamagedLots = new List<string>();
        public static readonly List<string> JobIds = new List<string>();
        public static bool Active => Kind != StormKind.None && SeasonWeather.Day < StartDay + SurgeDays;
        public static int DaysLeft => Active ? StartDay + SurgeDays - SeasonWeather.Day : 0;
        /// <summary>Storm jobs kept on the board during a surge.</summary>
        public static int Target => Company.Stage >= CompanyStage.Contractor ? 5 : 4;
        /// <summary>Tests: create jobs outside Career.</summary>
        public static bool Force;

        public static string[] DefsFor(StormKind k) => k switch
        {
            StormKind.Hail => new[] { "ext.roof-leak", "legacy.shingles", "ext.siding", "ext.gutter-leak", "ext.skylight" },
            StormKind.Windstorm => new[] { "legacy.shingles", "ext.fence-section", "ext.siding", "ext.gate-sag", "legacy.fence-picket", "ext.fascia", "ext.soffit" },
            StormKind.HeavySnow => new[] { "ext.roof-leak", "ext.gutter-leak", "legacy.gutter", "dw.ceiling-stain", "hvac.no-heat" },
            StormKind.FloodRain => new[] { "dw.ceiling-stain", "ext.gutter-leak", "ext.roof-leak", "mas.walk-crack", "legacy.gutter" },
            _ => new string[0]
        };

        public static string WhatHappened(StormKind k) => k switch
        {
            StormKind.Hail => "Golf-ball hail shredded shingles, dented siding and gutters across town",
            StormKind.Windstorm => "65 mph gusts tore off shingles, flattened fences and dropped trees and limbs",
            StormKind.HeavySnow => "A foot of heavy snow: ice dams, roof leaks, torn-down gutters and no-heat calls",
            StormKind.FloodRain => "Over 3\" of rain: leaking roofs, overflowing gutters, water-stained ceilings",
            _ => ""
        };

        /// <summary>Storm hits town now: damage on ~a dozen houses, the surge starts.</summary>
        public static void Trigger(StormKind k, int day, int seed = 0)
        {
            if (k == StormKind.None) return;
            End();
            Kind = k; StartDay = day;
            var rng = new System.Random(seed != 0 ? seed : day * 7349 + (int)k);
            var lots = Town.Addresses.Where(l => l.Category == "residential" || l.ServiceCalls).ToList();
            // enterable ranches first (they take the repair jobs), then other houses for the look
            var pick = lots.Where(l => l.ServiceCalls).OrderBy(_ => rng.Next()).Take(6).Concat(lots.Where(l => !l.ServiceCalls).OrderBy(_ => rng.Next()).Take(10));
            foreach (var l in pick) Damage(l, rng.Next());
            Game.Toast($"{Climate.StormName(k)}! {WhatHappened(k)}. Storm repair calls pay {Mathf.RoundToInt((Premium - 1f) * 100f)}% extra this week.");
        }

        static void Damage(TownLot l, int seed)
        {
            if (l == null || DamagedLots.Contains(l.Id)) return;
            try { StormDamage.Add(l, Kind, seed); } catch (Exception e) { Debug.LogWarning("[Seasons] storm damage at " + l.Id + ": " + e.Message); }
            DamagedLots.Add(l.Id);
        }

        public static void End()
        {
            StormDamage.ClearAll();
            Kind = StormKind.None; StartDay = -1; DamagedLots.Clear(); JobIds.Clear();
        }

        /// <summary>Keeps storm jobs on the board during the surge; clears damage at houses whose storm job got done; ends the surge.</summary>
        public static void Tick()
        {
            if (Kind == StormKind.None) return;
            if (!Active) { Game.Toast("Storm repair rush is over - back to normal work"); End(); return; }
            // finished jobs: the house is fixed
            for (int i = JobIds.Count - 1; i >= 0; i--)
            {
                var j = Find(JobIds[i]);
                if (j != null && !j.Done) continue;
                JobIds.RemoveAt(i);
                if (j != null) { var lot = Town.Find(j.Address); if (lot != null) { StormDamage.Clear(lot.Id); DamagedLots.Remove(lot.Id); } }
            }
            if (!GameSettings.IsCareer && !Force) return;
            int onBoard = Company.Offers.Count(o => o.Title != null && o.Title.StartsWith(TitlePrefix));
            for (int guard = 0; onBoard < Target && guard < 8; guard++)
                if (CreateJob() != null) onBoard++;
        }

        static Job Find(string id) => Company.Offers.Find(j => j.Id == id) ?? Company.ActiveJobs.Find(j => j.Id == id);

        public static Job CreateJob()
        {
            var defs = DefsFor(Kind).Select(JobCatalog.Get).Where(d => d != null && d.Stage <= Company.Stage).ToList();
            if (defs.Count == 0) return null;
            var d = defs[UnityEngine.Random.Range(0, defs.Count)];
            // prefer a damaged house that can take this job
            TownLot lot = null;
            foreach (var id in DamagedLots)
            {
                var l = Town.Find(id);
                if (l != null && l.ServiceCalls && !Company.Offers.Concat(Company.ActiveJobs).Any(j => j.Address == l.Address)) { lot = l; break; }
            }
            lot ??= JobCatalog.FreeAddressFor(d);
            if (lot == null) return null;
            var job = JobCatalog.CreateService(lot, false, null, d);
            if (job == null) return null;
            job.Payout = Mathf.RoundToInt(job.Payout * Premium);
            job.Title = TitlePrefix + job.Title;
            job.Description = WhatHappened(Kind) + ". " + job.Description;
            Company.Offers.Add(job); Company.NotifyChanged();
            JobIds.Add(job.Id); SeasonBusiness.MarkSeen(job);
            Damage(lot, job.Id.GetHashCode());
            return job;
        }

        // ------------------------------------------------------------------ save

        [Serializable] class Data { public int kind, start = -1; public List<string> lots = new List<string>(), jobs = new List<string>(), seen = new List<string>(); }
        public static string Save() => JsonUtility.ToJson(new Data { kind = (int)Kind, start = StartDay, lots = new List<string>(DamagedLots), jobs = new List<string>(JobIds), seen = SeasonBusiness.SeenList() });
        public static void Load(string json)
        {
            End();
            if (string.IsNullOrEmpty(json)) { SeasonBusiness.LoadSeen(null); return; }
            var d = JsonUtility.FromJson<Data>(json);
            SeasonBusiness.LoadSeen(d.seen);
            Kind = (StormKind)d.kind; StartDay = d.start; JobIds.AddRange(d.jobs);
            if (Kind == StormKind.None) return;
            int s = 1; foreach (var id in d.lots) Damage(Town.Find(id), StartDay * 31 + s++);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { Kind = StormKind.None; StartDay = -1; DamagedLots.Clear(); JobIds.Clear(); Force = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() { SeasonWeather.SaveStorms = Save; SeasonWeather.LoadStorms = Load; }
    }

    /// <summary>Seasonal business: busy summer, slow winter with more indoor/repair work (catalog weights), seasonal pay on new offers,
    /// emergency furnace calls in cold snaps and AC calls in heat waves.</summary>
    public static class SeasonBusiness
    {
        static readonly HashSet<string> _seen = new HashSet<string>();
        static readonly Dictionary<JobDef, float> _baseWeight = new Dictionary<JobDef, float>();
        static Seasons _appliedSeason = Seasons.None;
        public static bool Force;

        /// <summary>Pay multiplier for new offers by season (storm jobs carry their own premium).</summary>
        public static float PayFactor(Seasons s) => s switch { Seasons.Summer => 1.10f, Seasons.Spring => 1.05f, Seasons.Winter => 0.9f, _ => 1f };
        /// <summary>Demand multiplier on catalog weights: outdoor work (true) vs indoor work (false).</summary>
        public static float DemandFactor(Seasons s, bool outdoor) => s switch
        {
            Seasons.Winter => outdoor ? 0.3f : 1.35f,
            Seasons.Spring => outdoor ? 1.25f : 1f,
            Seasons.Summer => outdoor ? 1.4f : 0.9f,
            _ => 1f
        };

        public static bool ColdSnap => SeasonWeather.Today.LowF <= 5f || SeasonWeather.Today.HighF <= 18f;
        public static bool HeatWave => SeasonWeather.Today.HighF >= 90f;
        public const string EmergencyPrefix = "Emergency: ";

        public static void MarkSeen(Job j) { if (j != null) _seen.Add(j.Id); }
        public static List<string> SeenList() => new List<string>(_seen);
        public static void LoadSeen(List<string> l) { _seen.Clear(); if (l != null) _seen.UnionWith(l); }

        static bool Outdoor(JobDef d) => d.Where != null ? d.Where.Outdoor : d.Mode != JobMode.Service;

        public static void Tick()
        {
            if ((!GameSettings.IsCareer || !SeasonWeather.Simulated) && !Force) { if (_appliedSeason != Seasons.None) RestoreWeights(); return; }
            var season = SeasonWeather.Season;
            if (season != _appliedSeason) ApplyWeights(season);
            // seasonal pay on new offers
            foreach (var j in Company.Offers)
            {
                if (j == null || string.IsNullOrEmpty(j.Id) || _seen.Contains(j.Id)) continue;
                _seen.Add(j.Id);
                if (j.Title != null && (j.Title.StartsWith(StormEvents.TitlePrefix) || j.Title.StartsWith(EmergencyPrefix))) continue;
                j.Payout = Mathf.RoundToInt(j.Payout * PayFactor(season));
            }
            if (_seen.Count > 400) { var keep = new HashSet<string>(Company.Offers.Concat(Company.ActiveJobs).Select(j => j.Id)); _seen.IntersectWith(keep); }
            // cold snap / heat wave emergency calls
            if (ColdSnap) EnsureEmergency("hvac.no-heat", 1.3f, "Cold snap - furnace out, pipes at risk");
            if (HeatWave) { EnsureEmergency("hvac.weak-air", 1.25f, "Heat wave - AC can't keep up"); }
        }

        static void ApplyWeights(Seasons s)
        {
            foreach (var d in JobCatalog.All)
            {
                if (!_baseWeight.TryGetValue(d, out var w)) _baseWeight[d] = w = d.Weight;
                if (w > 0f) d.Weight = w * DemandFactor(s, Outdoor(d));
            }
            _appliedSeason = s;
        }

        /// <summary>Puts catalog weights back (tests / leaving Career).</summary>
        public static void RestoreWeights() { foreach (var kv in _baseWeight) kv.Key.Weight = kv.Value; _appliedSeason = Seasons.None; }

        static void EnsureEmergency(string defId, float premium, string why)
        {
            if (Company.Offers.Any(j => j.Title != null && j.Title.StartsWith(EmergencyPrefix))) return;
            var d = JobCatalog.Get(defId); if (d == null || d.Stage > Company.Stage) return;
            var lot = JobCatalog.FreeAddressFor(d); if (lot == null) return;
            var job = JobCatalog.CreateService(lot, false, null, d); if (job == null) return;
            job.Payout = Mathf.RoundToInt(job.Payout * premium);
            job.Title = EmergencyPrefix + job.Title; job.Description = why + ". " + job.Description;
            job.DueDay = SeasonWeather.Day + 1;
            Company.Offers.Add(job); Company.NotifyChanged(); _seen.Add(job.Id);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _seen.Clear(); _baseWeight.Clear(); _appliedSeason = Seasons.None; Force = false; }
    }
}
