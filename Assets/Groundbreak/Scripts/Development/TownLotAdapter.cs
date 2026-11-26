using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Thin adapter onto the Town feature (Scripts/Town, separate branch). Found by reflection so this folder compiles
    /// with or without it. Town free house lots become client-build lots; Town tracts (≥ 0.7 ac) plus the fallback tracts east of
    /// town are the land for sale. Claims go through Town.Claim(lot, LotUse.BuildSite, owner), which the Town saves itself.</summary>
    public class TownLotAdapter : ILotProvider
    {
        readonly Type _town, _lotUse;
        readonly FallbackLotProvider _fallback = new FallbackLotProvider();
        readonly Dictionary<string, DevLot> _cache = new Dictionary<string, DevLot>();
        readonly Dictionary<string, object> _raw = new Dictionary<string, object>();

        TownLotAdapter(Type town, Type lotUse) { _town = town; _lotUse = lotUse; }

        /// <summary>The adapter if the Town feature is compiled in, else null.</summary>
        public static TownLotAdapter TryCreate()
        {
            try
            {
                var asm = typeof(Company).Assembly;
                var town = asm.GetType("Groundbreak.Town"); var use = asm.GetType("Groundbreak.LotUse");
                if (town == null || use == null || town.GetProperty("FreeLots", BindingFlags.Public | BindingFlags.Static) == null) return null;
                return new TownLotAdapter(town, use);
            }
            catch (Exception e) { Debug.LogWarning("[Development] Town adapter unavailable: " + e.Message); return null; }
        }

        static object Get(object o, string name)
        {
            var t = o.GetType();
            var p = t.GetProperty(name); if (p != null) return p.GetValue(o);
            var f = t.GetField(name); return f?.GetValue(o);
        }

        DevLot Wrap(object tl)
        {
            string id = (string)Get(tl, "Id");
            _raw[id] = tl;
            if (!_cache.TryGetValue(id, out var d))
            {
                var area = Get(tl, "AreaSqFt");
                d = new DevLot { Id = id, Address = (string)Get(tl, "Address"), Rect = (Rect)Get(tl, "Rect"), Front = (Vector3)Get(tl, "Front") };
                d.IsTract = area != null && Convert.ToSingle(area) > 30000f;
                _cache[id] = d;
            }
            d.Claimed = Get(tl, "Use")?.ToString() != "Free";
            return d;
        }

        IEnumerable<DevLot> TownFree()
        {
            IEnumerable src = null;
            try { src = _town.GetProperty("FreeLots", BindingFlags.Public | BindingFlags.Static).GetValue(null) as IEnumerable; } catch { }
            if (src == null) yield break;
            foreach (var tl in src) { var d = Wrap(tl); if (!d.Claimed) yield return d; }
        }

        public IEnumerable<DevLot> FreeLots() { foreach (var d in TownFree()) if (!d.IsTract) yield return d; }
        public IEnumerable<DevLot> LandForSale()
        {
            foreach (var d in TownFree()) if (d.IsTract) yield return d;
            foreach (var d in _fallback.LandForSale()) yield return d;
        }

        public DevLot Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var f = _fallback.Find(id); if (f != null) return f;
            try
            {
                var tl = _town.GetMethod("Find", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null)?.Invoke(null, new object[] { id });
                return tl != null ? Wrap(tl) : null;
            }
            catch { return null; }
        }

        public bool Claim(DevLot lot, string owner)
        {
            if (lot == null) return false;
            if (_fallback.Find(lot.Id) != null) return _fallback.Claim(lot, owner);
            if (!_raw.TryGetValue(lot.Id, out var tl) && Find(lot.Id) != null) _raw.TryGetValue(lot.Id, out tl);
            if (tl == null) return false;
            try
            {
                var m = _town.GetMethod("Claim", BindingFlags.Public | BindingFlags.Static);
                bool ok = (bool)m.Invoke(null, new object[] { tl, Enum.Parse(_lotUse, "BuildSite"), owner });
                lot.Claimed = true; lot.Owner = owner;
                return ok;
            }
            catch (Exception e) { Debug.LogWarning("[Development] Town.Claim failed: " + e.Message); return false; }
        }

        public void Release(DevLot lot)
        {
            if (lot == null) return;
            if (_fallback.Find(lot.Id) != null) { _fallback.Release(lot); return; }
            if (!_raw.TryGetValue(lot.Id, out var tl)) return;
            try { _town.GetMethod("Release", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new[] { tl }); lot.Claimed = false; lot.Owner = null; }
            catch (Exception e) { Debug.LogWarning("[Development] Town.Release failed: " + e.Message); }
        }
    }
}
