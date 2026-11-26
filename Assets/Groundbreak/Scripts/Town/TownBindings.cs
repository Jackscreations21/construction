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
            // contractor jobs (decks, sheds, garages, slabs...) go on empty lots, one job per lot (Integration/LotBroker)
            TownHook.JobLotProvider = LotBroker.ContractorLot;
            foreach (var app in new[] { "Jobs", "Van", "Hardware Store" }) Tablet.SetAppGroup(app, "Business");
        }
    }
}
