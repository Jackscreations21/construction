using System;
using System.Reflection;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Loose binding to the Town feature (written by another session) so Fleet/Yard compile without it.
    /// When a type Groundbreak.Town with a static HQPosition exists it is used; otherwise the fallbacks below apply.
    /// After the Town merge, set <see cref="JobLotProvider"/> to hand out real town lots.</summary>
    public static class TownHook
    {
        /// <summary>Where the warehouse goes when there is no Town: far from the origin build site.</summary>
        public static Vector3 FallbackHQ = new Vector3(-230f, 0f, -180f);
        /// <summary>First fallback job lot; lots are laid out on a 45 m grid from here.</summary>
        public static Vector3 FallbackJobOrigin = new Vector3(-150f, 0f, 60f);
        /// <summary>Optional: returns (lot center, address) for the index-th contractor job.</summary>
        public static Func<int, (Vector3 center, string address)> JobLotProvider;
        /// <summary>Optional override for the HQ position (tests, or Town binding by hand).</summary>
        public static Vector3? HQOverride;

        static Type _town; static bool _looked;
        static readonly string[] Streets = { "Maple St", "Oak Ave", "Birch Ln", "Cedar Ct", "Elm St" };

        static Type TownType
        {
            get
            {
                if (_looked) return _town;
                _looked = true;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _town = asm.GetType("Groundbreak.Town", false);
                    if (_town != null) break;
                }
                return _town;
            }
        }

        public static bool Present => TownType != null;

        public static Vector3 HQPosition
        {
            get
            {
                if (HQOverride.HasValue) return HQOverride.Value;
                var t = TownType;
                if (t != null)
                {
                    try
                    {
                        const BindingFlags f = BindingFlags.Public | BindingFlags.Static;
                        var p = t.GetProperty("HQPosition", f);
                        if (p != null && p.PropertyType == typeof(Vector3)) return (Vector3)p.GetValue(null);
                        var fi = t.GetField("HQPosition", f);
                        if (fi != null && fi.FieldType == typeof(Vector3)) return (Vector3)fi.GetValue(null);
                    }
                    catch (Exception e) { Debug.LogWarning("[TownHook] Town.HQPosition failed: " + e.Message); }
                }
                return FallbackHQ;
            }
        }

        /// <summary>Lot center (y = 0) and street address for the index-th contractor job. Lots are 45 m apart.</summary>
        public static Vector3 JobLot(int index, out string address)
        {
            index = Mathf.Abs(index);
            if (JobLotProvider != null)
            {
                var r = JobLotProvider(index);
                address = r.address; return r.center;
            }
            int col = index % 3, row = (index / 3) % 5;
            address = $"{100 + index * 12} {Streets[row % Streets.Length]}";
            return FallbackJobOrigin + new Vector3(-col * 45f, 0f, row * 45f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _looked = false; _town = null; JobLotProvider = null; HQOverride = null; }
    }
}
