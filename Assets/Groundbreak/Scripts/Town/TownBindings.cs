using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Hooks the town into the other business features: Fleet's contractor jobs get real customer lots,
    /// and the town's tablet apps go in the "Business" group.</summary>
    public static class TownBindings
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            TownHook.JobLotProvider = i =>
            {
                List<TownLot> lots = Town.Addresses.Where(l => l.Category == "residential").ToList();
                var l = lots[i % lots.Count];
                return (l.Center, l.Address);
            };
            foreach (var app in new[] { "Jobs", "Van", "Hardware Store" }) Tablet.SetAppGroup(app, "Business");
        }
    }
}
