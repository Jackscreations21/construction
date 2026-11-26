using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// What is in the work van: the Handyman-stage stockpile. Limited by BOTH volume and weight, like a real cargo van:
    /// ~250 cu ft body of which ~180 cu ft is usable with shelving, 3,200 lb payload. Stock longer than 10' rides on the roof
    /// rack (max 6 pieces up to 16', 150 lb); 4x8 sheets lie flat on the floor between the shelves (max 20).
    /// Counts are per stock UNIT (same as <see cref="Stockpile"/>): consumables count single nails, not boxes.
    /// See Docs/TOWN.md.
    /// </summary>
    public static class VanCargo
    {
        public const float BodyCuFt = 250f;
        /// <summary>Longest piece that fits inside with the doors shut (10').</summary>
        public const float MaxInsideLength = 3.05f;
        public const float RackMaxLength = 16f * Units.Foot + 0.01f;
        public const int RackMaxPieces = 6, MaxSheets = 20;
        public const float RackMaxLb = 150f;
        public const float ReachMeters = 40f;
        const float CuFtPerM3 = 35.3147f;

        static readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        static readonly Dictionary<string, (float lb, float cuFt)> _specs = new Dictionary<string, (float, float)>();
        static readonly List<string> _registered = new List<string>();
        static readonly List<KeyValuePair<string, int>> _starter = new List<KeyValuePair<string, int>>();

        /// <summary>Raised after any change to the cargo.</summary>
        public static event Action Changed;

        public static float CapacityCuFt => 180f;
        public static float PayloadLb => 3200f;

        // ------------------------------------------------------------------ item specs

        /// <summary>Weight (lb) and packed volume (cu ft) of ONE stock unit. Registered items are sold at the hardware store.</summary>
        public static void RegisterItem(string id, float lbEach, float cuFtEach)
        {
            if (string.IsNullOrEmpty(id)) return;
            _specs[id] = (Mathf.Max(0f, lbEach), Mathf.Max(0f, cuFtEach));
            if (!_registered.Contains(id)) _registered.Add(id);
        }

        public static void UnregisterItem(string id) { _specs.Remove(id); _registered.Remove(id); }
        public static bool IsRegistered(string id) => id != null && _specs.ContainsKey(id);
        public static IEnumerable<string> RegisteredItems => _registered;

        /// <summary>Registered weight/volume per unit, else an estimate from the catalog size and a typical density.</summary>
        public static (float lb, float cuFt) Spec(string id)
        {
            if (id != null && _specs.TryGetValue(id, out var s)) return s;
            var d = id != null ? Catalog.Get(id) : null;
            if (d == null) return (1f, 0.05f);
            float cu = d.Size.x * d.Size.y * d.Size.z * CuFtPerM3;
            if (d.Category == PieceCategory.Consumable || cu < 1e-5f)
            {
                // a box / bag / roll per purchase: ~5 lb, 0.1 cu ft
                int per = Mathf.Max(1, d.UnitsPerPurchase);
                return (5f / per, 0.1f / per);
            }
            float density;
            switch (d.Category)
            {
                case PieceCategory.Lumber: density = 28f; break;
                case PieceCategory.Sheet: density = 36f; break;
                case PieceCategory.Masonry: density = cu > 0.2f ? 72f : 120f; break;      // hollow block vs solid brick
                case PieceCategory.Rebar: density = 490f * 0.785f; break;                 // round steel bar in its bounding box
                case PieceCategory.Door: density = 18f; break;
                case PieceCategory.GarageDoor: density = 9f; break;
                case PieceCategory.DoorHardware: density = 40f; break;
                case PieceCategory.Window: density = 10f; break;
                case PieceCategory.Plumbing: density = 12f; break;
                case PieceCategory.Mechanical: density = 8f; break;
                case PieceCategory.Electrical: density = 10f; break;
                case PieceCategory.Appliance: density = 7f; break;
                default: density = 20f; break;
            }
            return (cu * density, cu);
        }

        /// <summary>Longest dimension of the catalog item in metres (0 when unknown).</summary>
        public static float LongestDimension(string id)
        {
            var d = id != null ? Catalog.Get(id) : null;
            return d == null ? 0f : Mathf.Max(d.Size.x, Mathf.Max(d.Size.y, d.Size.z));
        }

        /// <summary>A 4x8-ish panel (plywood, OSB, drywall): thin, at least ~4' x 8'.</summary>
        public static bool IsSheet(string id)
        {
            var d = id != null ? Catalog.Get(id) : null;
            if (d == null) return false;
            float a = d.Size.x, b = d.Size.y, c = d.Size.z;
            float max = Mathf.Max(a, Mathf.Max(b, c)), min = Mathf.Min(a, Mathf.Min(b, c)), mid = a + b + c - max - min;
            return min <= 0.06f && mid >= 1.15f && max >= 2.3f;
        }

        /// <summary>True for stock too long to go inside (over 10'): it is carried on the roof rack.</summary>
        public static bool RidesOnRack(string id) => LongestDimension(id) > MaxInsideLength;

        // ------------------------------------------------------------------ contents

        public static int Count(string id) => id != null && _counts.TryGetValue(id, out var n) ? n : 0;

        public static IEnumerable<KeyValuePair<string, int>> Entries
        {
            get { foreach (var kv in _counts) if (kv.Value > 0) yield return kv; }
        }

        /// <summary>Entries carried on the roof rack (long stock).</summary>
        public static IEnumerable<KeyValuePair<string, int>> RackEntries
        {
            get { foreach (var kv in _counts) if (kv.Value > 0 && RidesOnRack(kv.Key)) yield return kv; }
        }

        /// <summary>Volume used inside the van (roof-rack stock does not count).</summary>
        public static float UsedCuFt
        {
            get { float t = 0f; foreach (var kv in _counts) if (kv.Value > 0 && !RidesOnRack(kv.Key)) t += Spec(kv.Key).cuFt * kv.Value; return t; }
        }

        /// <summary>Total cargo weight including the roof rack.</summary>
        public static float UsedLb
        {
            get { float t = 0f; foreach (var kv in _counts) if (kv.Value > 0) t += Spec(kv.Key).lb * kv.Value; return t; }
        }

        public static int RackPieces
        {
            get { int t = 0; foreach (var kv in _counts) if (kv.Value > 0 && RidesOnRack(kv.Key)) t += kv.Value; return t; }
        }

        public static float RackLb
        {
            get { float t = 0f; foreach (var kv in _counts) if (kv.Value > 0 && RidesOnRack(kv.Key)) t += Spec(kv.Key).lb * kv.Value; return t; }
        }

        public static int SheetCount
        {
            get { int t = 0; foreach (var kv in _counts) if (kv.Value > 0 && IsSheet(kv.Key) && !RidesOnRack(kv.Key)) t += kv.Value; return t; }
        }

        static string NameOf(string id) => Catalog.Get(id)?.Name ?? id;

        /// <summary>Would n more units fit? <paramref name="why"/> says which limit is hit (volume, payload, roof rack, sheets).</summary>
        public static bool CanAdd(string id, int n, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(id)) { why = "Unknown item"; return false; }
            if (n <= 0) return true;
            var (lb, cu) = Spec(id);
            float usedLb = UsedLb;
            if (usedLb + lb * n > PayloadLb + 0.01f)
            {
                why = $"Too heavy: {NameOf(id)} x{n} is {lb * n:N0} lb, the van has {Mathf.Max(0f, PayloadLb - usedLb):N0} lb of its {PayloadLb:N0} lb payload left";
                return false;
            }
            if (RidesOnRack(id))
            {
                float len = LongestDimension(id);
                if (IsSheet(id)) { why = $"{NameOf(id)}: sheets over 10' don't fit in the van - order a delivery"; return false; }
                if (len > RackMaxLength) { why = $"{NameOf(id)} is {Units.Format(len)} long - the roof rack takes stock up to 16'. Order a delivery"; return false; }
                int pieces = RackPieces;
                if (pieces + n > RackMaxPieces) { why = $"Roof rack is full: {RackMaxPieces} long pieces max ({pieces} on it)"; return false; }
                float rackLb = RackLb;
                if (rackLb + lb * n > RackMaxLb + 0.01f) { why = $"Roof rack overloaded: {RackMaxLb:N0} lb max ({rackLb:N0} lb on it)"; return false; }
                return true;
            }
            if (IsSheet(id))
            {
                int sheets = SheetCount;
                if (sheets + n > MaxSheets) { why = $"Only {MaxSheets} sheets fit flat on the van floor ({sheets} loaded)"; return false; }
            }
            float usedCu = UsedCuFt;
            if (usedCu + cu * n > CapacityCuFt + 0.001f)
            {
                why = $"No room: {NameOf(id)} x{n} needs {cu * n:0.#} cu ft, the van has {Mathf.Max(0f, CapacityCuFt - usedCu):0.#} of {CapacityCuFt:0} cu ft left";
                return false;
            }
            return true;
        }

        /// <summary>Loads n units. False (and nothing changes) when they do not fit.</summary>
        public static bool Add(string id, int n)
        {
            if (n <= 0) return n == 0 && !string.IsNullOrEmpty(id);
            if (!CanAdd(id, n, out _)) return false;
            _counts[id] = Count(id) + n;
            Changed?.Invoke();
            return true;
        }

        public static bool TryTake(string id, int n = 1)
        {
            if (n <= 0) return n == 0;
            int have = Count(id);
            if (have < n) return false;
            if (have == n) _counts.Remove(id); else _counts[id] = have - n;
            Changed?.Invoke();
            return true;
        }

        public static void Clear()
        {
            if (_counts.Count == 0) return;
            _counts.Clear();
            Changed?.Invoke();
        }

        /// <summary>Replaces the contents without capacity checks (save games).</summary>
        public static void SetAll(IEnumerable<KeyValuePair<string, int>> contents)
        {
            _counts.Clear();
            if (contents != null)
                foreach (var kv in contents) if (!string.IsNullOrEmpty(kv.Key) && kv.Value > 0) _counts[kv.Key] = kv.Value;
            Changed?.Invoke();
        }

        /// <summary>True when the van exists, is parked (nobody driving) and within 40 m of the point: its stock can be used there.</summary>
        public static bool InReach(Vector3 worldPos)
        {
            var v = WorkVan.I;
            if (v == null || v.Driving) return false;
            Vector3 d = v.transform.position - worldPos; d.y = 0f;
            return d.sqrMagnitude <= ReachMeters * ReachMeters;
        }

        // ------------------------------------------------------------------ starter kit

        /// <summary>Features register what a new Handyman finds in the van (call at BeforeSceneLoad). Same id adds up.</summary>
        public static void AddStarter(string id, int n)
        {
            if (string.IsNullOrEmpty(id) || n <= 0) return;
            _starter.Add(new KeyValuePair<string, int>(id, n));
        }

        public static IEnumerable<KeyValuePair<string, int>> StarterKit => _starter;

        /// <summary>Empties the van and loads every starter entry that fits.</summary>
        public static void LoadStarterKit()
        {
            _counts.Clear();
            foreach (var kv in _starter)
                if (CanAdd(kv.Key, kv.Value, out _)) _counts[kv.Key] = Count(kv.Key) + kv.Value;
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset()
        {
            _counts.Clear(); _specs.Clear(); _registered.Clear(); _starter.Clear(); Changed = null;
        }
    }
}
