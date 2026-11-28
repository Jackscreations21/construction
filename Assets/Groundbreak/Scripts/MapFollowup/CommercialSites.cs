using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Building sites for commercial and industrial client builds on the map's big commercial / industrial tracts (every strip and industrial
    /// map lot is over <see cref="LotBroker.TractSqFt"/>, so they are tracts, not single-building lots). A build claims the whole tract, but the
    /// building goes on a <b>pad site</b> inside it, laid out like a real site plan:
    /// <list type="bullet">
    /// <item>it faces the road (the tract's front);</item>
    /// <item>it is set back from the street: 70' for stores, restaurants, offices and gas stations (5' landscape strip, a 60' double-loaded
    /// parking bay, 5' walk) and 40' for warehouses (car parking in front);</item>
    /// <item>it sits 15' (commercial) / 10' (industrial) off the left side line, so the right side stays clear for the parking lot or the
    /// 30' truck drive; at least 20' stays behind it (rear setback).</item>
    /// </list>
    /// The pad is a <see cref="DevLot"/> sized like a Development lot around the building (7'-9" each side, 25' in front, 20' behind), so
    /// Development's <see cref="PlanPlacement.On"/> puts the building exactly there. Its id is <c>tractId@c60x50</c> (kind + plan feet), so it
    /// resolves again after a load; <see cref="Provider"/> wraps the town's lot provider to find, claim and release pads (the claim is the
    /// tract's, so the tract's meadow / for-sale dressing hides and comes back exactly like any claimed lot).
    /// </summary>
    public static class CommercialSites
    {
        /// <summary>Building front setback from the street line (ft).</summary>
        public const float CommercialSetbackFt = 70f, IndustrialSetbackFt = 40f;
        /// <summary>Building to the side lot line on its own side (ft).</summary>
        public const float CommercialSideFt = 15f, IndustrialSideFt = 10f;
        /// <summary>What must stay clear on the other side (ft): a 60' parking bay (two rows of 9'x18' stalls + 24' aisle) or a 30' truck drive.</summary>
        public const float CommercialClearFt = 60f, IndustrialClearFt = 30f;
        public const float RearSetbackFt = 20f;
        /// <summary>Pad margins around the building: Development's lot check wants frontage ≥ W+15' and depth ≥ D+45'; PlanPlacement sets the
        /// building 25' behind the pad's front line.</summary>
        const float PadSideFt = 7.75f, PadFrontFt = 25f, PadRearFt = 20.5f;

        static readonly Dictionary<string, DevLot> _pads = new Dictionary<string, DevLot>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() => _pads.Clear();

        /// <summary>Wraps the current lot provider once (idempotent), so pad ids resolve everywhere Development looks lots up.</summary>
        public static void Install()
        {
            if (!(DevLots.Provider is Provider)) DevLots.Provider = new Provider(DevLots.Provider);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            Install();
            SaveSystem.BeforeLoad += Install;   // a test or feature may have swapped the provider: projects on pads must still find them
        }

        public static bool IsPadId(string id) => !string.IsNullOrEmpty(id) && id.IndexOf('@') > 0;

        /// <summary>The town lot (tract) a DevLot belongs to: the tract of a pad, else the lot with the same id.</summary>
        public static TownLot TownLotOf(DevLot d) => d == null ? null : Town.Find(IsPadId(d.Id) ? d.Id.Substring(0, d.Id.IndexOf('@')) : d.Id);

        static bool Industrial(HousePlan plan) => plan != null && plan.Group == PlanGroup.Industrial;

        /// <summary>The pad site for a plan on a tract, or null when it doesn't fit with the setbacks and the clear side.</summary>
        public static DevLot PadFor(TownLot tract, HousePlan plan)
        {
            if (tract == null || plan == null || plan.WidthFt <= 0f || plan.DepthFt <= 0f) return null;
            bool ind = Industrial(plan);
            string id = tract.Id + "@" + (ind ? "i" : "c") + plan.WidthFt.ToString("0.###", CultureInfo.InvariantCulture) + "x" + plan.DepthFt.ToString("0.###", CultureInfo.InvariantCulture);
            return Pad(id);
        }

        /// <summary>Pad from its id (deterministic: the same id always gives the same rect), null when the tract is gone or too small.</summary>
        static DevLot Pad(string id)
        {
            int at = id.IndexOf('@');
            if (at <= 0 || at + 2 >= id.Length) return null;
            var tract = Town.Find(id.Substring(0, at));
            if (tract == null) return null;
            bool ind = id[at + 1] == 'i';
            var dims = id.Substring(at + 2).Split('x');
            if (dims.Length != 2 || !float.TryParse(dims[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float w)
                || !float.TryParse(dims[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float d)) return null;
            if (!Layout(tract, w, d, ind, out Rect rect)) return null;
            if (!_pads.TryGetValue(id, out var pad) || pad.Rect != rect || pad.Front != tract.Front)
            {
                pad = new DevLot { Id = id, Address = tract.Address, Rect = rect, Front = tract.Front, IsTract = false };
                _pads[id] = pad;
            }
            pad.Claimed = !tract.IsFree; pad.Owner = tract.Owner;
            return pad;
        }

        /// <summary>World rect of the pad for a w x d (ft) building on the tract; false when it doesn't fit.</summary>
        public static bool Layout(TownLot tract, float wFt, float dFt, bool industrial, out Rect rect)
        {
            rect = default;
            float ft = Units.Foot;
            float setback = industrial ? IndustrialSetbackFt : CommercialSetbackFt, side = industrial ? IndustrialSideFt : CommercialSideFt;
            float clear = industrial ? IndustrialClearFt : CommercialClearFt;
            float F = tract.Frontage / ft, D = tract.Depth / ft;
            if (side + wFt + clear > F || setback + dFt + Mathf.Max(RearSetbackFt, PadRearFt) > D) return false;
            float padW = wFt + 2f * PadSideFt, padD = dFt + PadFrontFt + PadRearFt;
            float padFront = setback - PadFrontFt;                       // pad front line behind the street line (ft)
            float xc = -F / 2f + (side - PadSideFt) + padW / 2f;         // viewer's left (seen from the street) is -X
            var back = -tract.Front;
            var X = Vector3.Cross(Vector3.up, back);                     // right hand of someone on the street looking at the lot
            var c = tract.FrontMid + back * ((padFront + padD / 2f) * ft) + X * (xc * ft);
            bool frontAlongX = Mathf.Abs(tract.Front.x) > 0.5f;
            float sx = (frontAlongX ? padD : padW) * ft, sz = (frontAlongX ? padW : padD) * ft;
            rect = new Rect(c.x - sx / 2f, c.z - sz / 2f, sx, sz);
            return true;
        }

        /// <summary>Lot provider wrapper: pads are found / claimed / released through their tract, everything else goes to the town provider.</summary>
        public class Provider : ILotProvider
        {
            readonly ILotProvider _inner;
            public Provider(ILotProvider inner) { _inner = inner ?? new FallbackLotProvider(); }
            public ILotProvider Inner => _inner;

            public IEnumerable<DevLot> FreeLots() => _inner.FreeLots();
            public IEnumerable<DevLot> LandForSale() => _inner.LandForSale();
            public DevLot Find(string id) => IsPadId(id) ? Pad(id) : _inner.Find(id);

            public bool Claim(DevLot lot, string owner)
            {
                if (lot == null) return false;
                if (!IsPadId(lot.Id)) return _inner.Claim(lot, owner);
                var tl = TownLotOf(lot);
                if (tl == null) return false;
                bool ok = Town.Claim(tl, LotUse.BuildSite, owner);
                lot.Claimed = !tl.IsFree; lot.Owner = tl.Owner ?? owner;
                return ok;
            }

            public void Release(DevLot lot)
            {
                if (lot == null) return;
                if (!IsPadId(lot.Id)) { _inner.Release(lot); return; }
                var tl = TownLotOf(lot);
                if (tl != null) Town.Release(tl);
                lot.Claimed = false; lot.Owner = null;
            }
        }
    }
}
