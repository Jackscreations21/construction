using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The permitted job site. In Career mode this is the contract's lot; in Free Build there is no lot (everything counts).
    /// Inspection rules should only look at things where <see cref="InLot"/> is true.</summary>
    public static class Site
    {
        /// <summary>Lot rectangle in world XZ (meters). Null = no lot (Free Build).</summary>
        public static Rect? Lot;
        public static string LotName = "";

        public static bool InLot(Vector3 p) => !Lot.HasValue || Lot.Value.Contains(new Vector2(p.x, p.z));

        public static Vector3 LotCenter => Lot.HasValue ? new Vector3(Lot.Value.center.x, 0f, Lot.Value.center.y) : Vector3.zero;

        /// <summary>All building pieces on the lot.</summary>
        public static List<Piece> Pieces()
        {
            var list = new List<Piece>();
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                if (p != null && p.Def != null && p.gameObject.activeInHierarchy && InLot(p.transform.position)) list.Add(p);
            return list;
        }

        /// <summary>World bounds of every building piece on the lot (fastened or not). False if there are none.</summary>
        public static bool BuildingBounds(out Bounds b)
        {
            b = default; bool any = false;
            foreach (var p in Pieces())
            {
                var c = p.GetComponent<Collider>();
                if (c == null) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            return any;
        }

        /// <summary>Footprint area of the building (square feet), from <see cref="BuildingBounds"/>.</summary>
        public static float FootprintSqFt()
        {
            if (!BuildingBounds(out var b)) return 0f;
            return (b.size.x / Units.Foot) * (b.size.z / Units.Foot);
        }
    }
}
