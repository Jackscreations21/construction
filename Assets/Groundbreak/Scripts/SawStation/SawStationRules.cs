using UnityEngine;

namespace Groundbreak
{
    /// <summary>What the yard saw station (Yard equipment "sawstation": a miter-saw bench with length stops + a cabinet table saw)
    /// changes in the business. Real shops use one to cut framing and trim packages from a cut list before the truck leaves:
    /// <list type="bullet">
    /// <item>Your own crew's carpentry goes <see cref="CrewSpeedBonus"/> faster: house framing and trim phases (CrewHouseProgress uses
    /// <see cref="Crew.Speed"/> with the carpenter trade), carpentry tasks on site, and off-site crew jobs that are wood builds
    /// (deck, fence, shed, garage). Concrete, roofing and subcontractors' own hands get nothing.</item>
    /// <item>Optimised cut lists waste less lumber: crew-built wood jobs buy <see cref="WasteSaving"/> less material.</item>
    /// </list></summary>
    public static class SawStationRules
    {
        public const string Id = "sawstation";
        public const float CrewSpeedBonus = 0.10f;
        public const float WasteSaving = 0.04f;
        static readonly string[] WoodJobTypes = { "deck", "fence", "shed", "garage" };

        public static bool Has => Yard.HasEquipment(Id);

        /// <summary>Contractor job types built mostly from cut lumber.</summary>
        public static bool IsWoodJob(Job j)
        {
            if (j == null) return false;
            string t = ContractorJobs.TypeOf(j);
            foreach (var w in WoodJobTypes) if (t == w) return true;
            return false;
        }

        /// <summary>Multiplier on a worker's speed at a task trade (house framing/trim and site carpentry). Subs bring their own saws.</summary>
        public static float CrewSpeed(Worker w, string taskTrade)
        {
            if (!Has || taskTrade != "carpenter" || w == null) return 1f;
            if (w.Uid != null && w.Uid.StartsWith("sub-")) return 1f;
            return 1f + CrewSpeedBonus;
        }

        /// <summary>Multiplier on an off-site crew job's daily progress.</summary>
        public static float JobSpeed(Job j) => Has && IsWoodJob(j) ? 1f + CrewSpeedBonus : 1f;

        /// <summary>Multiplier on the material bill of a crew-built job.</summary>
        public static float MaterialFactor(Job j) => Has && IsWoodJob(j) ? 1f - WasteSaving : 1f;

        /// <summary>The line shown where the saw station is sold (Warehouse app, shop equipment).</summary>
        public static string ShopLine =>
            $"Miter-saw bench with stops + cabinet table saw: the crew pre-cuts framing and trim from cut lists at the shop. " +
            $"Crew carpentry {CrewSpeedBonus * 100:0}% faster (house framing/trim, decks, fences, sheds, garages), " +
            $"{WasteSaving * 100:0}% less lumber bought on crew-built wood jobs.";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var e = Yard.EquipmentOf(Id);
            if (e != null) e.Description = ShopLine;
        }
    }
}
