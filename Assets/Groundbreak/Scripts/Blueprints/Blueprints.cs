using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The blueprint library: registers every plan into <see cref="HousePlans"/> (so the Plans app, overlay, Builder and
    /// Developments see them) and answers lookups by id, category, group, size and company stage. See Docs/BLUEPRINTS.md.</summary>
    public static class Blueprints
    {
        static List<Blueprint> _lib;
        static readonly Dictionary<HousePlan, Blueprint> _shadows = new Dictionary<HousePlan, Blueprint>();

        /// <summary>Every plan in the game (Development's + the library), registered.</summary>
        public static IReadOnlyList<HousePlan> All { get { Ensure(); return HousePlans.All; } }
        /// <summary>The library's own plans (Blueprint objects), in definition order.</summary>
        public static IReadOnlyList<Blueprint> Library { get { Ensure(); return _lib; } }

        public static HousePlan Get(string id) { Ensure(); return HousePlans.Get(id); }
        public static Blueprint GetBlueprint(string id) => Info(Get(id));

        public static List<HousePlan> OfCategory(string category) { var l = new List<HousePlan>(); foreach (var p in All) if (p.Category == category) l.Add(p); return l; }
        public static List<HousePlan> OfGroup(PlanGroup g) { var l = new List<HousePlan>(); foreach (var p in All) if (p.Group == g) l.Add(p); return l; }
        /// <summary>Plans a company at this stage may build.</summary>
        public static List<HousePlan> Buildable(CompanyStage stage) { var l = new List<HousePlan>(); foreach (var p in All) if (p.MinStage <= stage) l.Add(p); return l; }
        /// <summary>Plans of a category whose area is in [minSqFt, maxSqFt] and whose footprint fits a lot (frontage/depth in feet, 0 = any).</summary>
        public static List<HousePlan> Find(string category = null, float minSqFt = 0, float maxSqFt = float.MaxValue, float maxWidthFt = 0, float maxDepthFt = 0)
        {
            var l = new List<HousePlan>();
            foreach (var p in All)
                if ((category == null || p.Category == category) && p.SqFt >= minSqFt && p.SqFt <= maxSqFt && (maxWidthFt <= 0 || p.WidthFt <= maxWidthFt) && (maxDepthFt <= 0 || p.DepthFt <= maxDepthFt)) l.Add(p);
            return l;
        }
        /// <summary>Deterministic pick of a plan in a category (same seed → same plan), or null.</summary>
        public static HousePlan Pick(string category, int seed) { var l = OfCategory(category); return l.Count == 0 ? null : l[Mathf.Abs(seed) % l.Count]; }
        public static List<string> Categories { get { var l = new List<string>(); foreach (var p in All) if (!l.Contains(p.Category)) l.Add(p.Category); return l; } }

        /// <summary>The plan as a Blueprint (itself, or a cached Blueprint shadow of a plain HousePlan for fixtures/code checks).</summary>
        public static Blueprint Info(HousePlan p)
        {
            if (p == null) return null;
            if (p is Blueprint b) return b;
            if (_shadows.TryGetValue(p, out var s)) return s;
            s = new Blueprint
            {
                Id = p.Id, Name = p.Name, Style = p.Style, WidthFt = p.WidthFt, DepthFt = p.DepthFt, Floors = p.Floors, Beds = p.Beds, Baths2 = p.Baths2,
                WallHeightFt = p.WallHeightFt, Pitch = p.Pitch, Roof = p.Roof, Foundation = p.Foundation, StudSize = p.StudSize, FrontDoorFt = p.FrontDoorFt,
                BaseQuality = p.BaseQuality, Category = p.Category, Group = p.Group, MinStage = p.MinStage,
            };
            foreach (var r in p.Rooms) s.Room(r.Name, r.Ft.xMin, r.Ft.yMin, r.Ft.xMax, r.Ft.yMax, r.Floor);
            s.Finish();
            _shadows[p] = s;
            return s;
        }

        /// <summary>Registers the library (idempotent; re-registers if HousePlans was reset).</summary>
        public static void Ensure()
        {
            if (_lib == null) { BlueprintProducts.Register(); _lib = new List<Blueprint>(); BlueprintLibrary.Define(_lib); }
            if (HousePlans.Get(_lib[_lib.Count - 1].Id) == _lib[_lib.Count - 1]) return;
            foreach (var b in _lib) HousePlans.Register(b);
            // Development's own single-story plans get their types
            void Tag(string id, string cat) { var p = HousePlans.Get(id); if (p != null && !(p is Blueprint)) { p.Category = cat; p.Group = PlanGroup.Residential; p.MinStage = CompanyStage.Builder; } }
            Tag("starter-900", "starter"); Tag("cottage-1200", "cottage"); Tag("ranch-1600", "ranch");
            _shadows.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Ensure();
            Tablet.AddHelp("Plans app: filter by Residential / Outbuilding / Commercial / Industrial and type; the overlay shows walls, openings, stairs and MEP fixtures");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _lib = null; _shadows.Clear(); }
    }
}
