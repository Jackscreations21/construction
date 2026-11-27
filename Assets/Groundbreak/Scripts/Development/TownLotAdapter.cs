using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Development's lots on the real Town (direct calls; the reflection version predates the Town merge).
    /// Empty town house lots become client-build lots; town tracts (≥ 0.7 ac) plus the fallback tracts east of town are the land
    /// for sale. Lots another job already names (LotBroker.Reserved) are skipped. Claims go through
    /// Town.Claim(lot, LotUse.BuildSite, owner), which the Town saves itself. When every town lot is used, the fallback
    /// Ridge Rd lots take over so Builder offers never dry up.</summary>
    public class TownLotAdapter : ILotProvider
    {
        readonly FallbackLotProvider _fallback = new FallbackLotProvider();
        readonly Dictionary<string, DevLot> _cache = new Dictionary<string, DevLot>();

        /// <summary>Always available now that the Town is part of the game.</summary>
        public static TownLotAdapter TryCreate() => new TownLotAdapter();

        DevLot Wrap(TownLot tl)
        {
            if (!_cache.TryGetValue(tl.Id, out var d))
            {
                d = new DevLot { Id = tl.Id, Address = tl.Address, Rect = tl.Rect, Front = tl.Front, IsTract = LotBroker.IsTract(tl) };
                _cache[tl.Id] = d;
            }
            d.Claimed = !tl.IsFree;
            d.Owner = tl.Owner;
            return d;
        }

        IEnumerable<DevLot> TownOpen(bool tracts)
        {
            foreach (var tl in Town.FreeLots)
                if (LotBroker.IsTract(tl) == tracts && !LotBroker.Reserved(tl)) yield return Wrap(tl);
        }

        public IEnumerable<DevLot> FreeLots()
        {
            bool any = false;
            foreach (var d in TownOpen(false)) { any = true; yield return d; }
            if (!any) foreach (var d in _fallback.FreeLots()) yield return d;
        }

        public IEnumerable<DevLot> LandForSale()
        {
            foreach (var d in TownOpen(true)) yield return d;
            foreach (var d in _fallback.LandForSale()) yield return d;
        }

        public DevLot Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var f = _fallback.Find(id); if (f != null) return f;
            var tl = Town.Find(id);
            return tl != null ? Wrap(tl) : null;
        }

        public bool Claim(DevLot lot, string owner)
        {
            if (lot == null) return false;
            if (_fallback.Find(lot.Id) != null) return _fallback.Claim(lot, owner);
            var tl = Town.Find(lot.Id);
            if (tl == null) return false;
            bool ok = Town.Claim(tl, LotUse.BuildSite, owner);
            lot.Claimed = true; lot.Owner = tl.Owner ?? owner;   // already ours after a load (the town restores its own claims)
            return ok;
        }

        public void Release(DevLot lot)
        {
            if (lot == null) return;
            if (_fallback.Find(lot.Id) != null) { _fallback.Release(lot); return; }
            var tl = Town.Find(lot.Id);
            if (tl == null) return;
            Town.Release(tl);
            lot.Claimed = false; lot.Owner = null;
        }
    }
}
