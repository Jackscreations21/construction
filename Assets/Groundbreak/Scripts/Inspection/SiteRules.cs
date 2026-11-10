using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Site/landscaping inspection rules owned by this feature (other trades register their own).</summary>
    public static class SiteRules
    {
        /// <summary>Buildings at least this big (sq ft) are dwellings/garages that need grading + address numbers.</summary>
        public const float DwellingSqFt = 400f;
        /// <summary>IRC R401.3: fall a minimum of 6" within the first 10'.</summary>
        public static readonly float RequiredFall = Units.In(6), FallRun = 10 * Units.Foot;

        public static void RegisterAll()
        {
            Inspections.Register("site.loose-members", InspectionPhase.Framing,
                "All framing members and sheathing fastened (no loose lumber in the structure)", LooseMembers);
            Inspections.Register("site.grade-away", InspectionPhase.Final,
                "Lot graded away from foundation: 6\" fall within the first 10' (IRC R401.3)", GradeAway);
            Inspections.Register("site.address", InspectionPhase.Final,
                "Address numbers posted, visible from the street (IRC R319): mailbox or numbers", Address);
        }

        static IEnumerable<InspectionIssue> LooseMembers()
        {
            int n = 0;
            foreach (var p in Site.Pieces())
            {
                if (p.State != PieceState.Loose) continue;
                if (p.Def.Category != PieceCategory.Lumber && p.Def.Category != PieceCategory.Sheet) continue;
                if (p.transform.position.y < 0.12f && Mathf.Abs(Vector3.Dot(p.transform.forward, Vector3.up)) < 0.3f
                    && p.GetComponentInParent<MaterialPile>() != null) continue; // stacked material, not structure
                yield return new InspectionIssue($"{p.Def.Name} not fastened", p.transform.position);
                if (++n >= 25) yield break;
            }
        }

        /// <summary>Perimeter sample points around the building footprint with outward directions.</summary>
        public static List<(Vector3 at, Vector3 outward)> PerimeterSamples(Bounds b)
        {
            var list = new List<(Vector3, Vector3)>();
            float pad = Units.In(6);
            Vector3 mn = b.min - new Vector3(pad, 0, pad), mx = b.max + new Vector3(pad, 0, pad);
            int per = 3;
            for (int i = 0; i < per; i++)
            {
                float t = (i + 0.5f) / per;
                list.Add((new Vector3(Mathf.Lerp(mn.x, mx.x, t), 0, mn.z), Vector3.back));
                list.Add((new Vector3(Mathf.Lerp(mn.x, mx.x, t), 0, mx.z), Vector3.forward));
                list.Add((new Vector3(mn.x, 0, Mathf.Lerp(mn.z, mx.z, t)), Vector3.left));
                list.Add((new Vector3(mx.x, 0, Mathf.Lerp(mn.z, mx.z, t)), Vector3.right));
            }
            return list;
        }

        /// <summary>Fall (meters, positive = drains away) from the wall to 10' out at a sample.</summary>
        public static float FallAt(Vector3 at, Vector3 outward)
        {
            Vector3 far = at + outward * FallRun;
            return SurfaceHeight(at) - SurfaceHeight(far);
        }

        /// <summary>Finished surface: walks/drives where they are built, otherwise the graded soil.</summary>
        public static float SurfaceHeight(Vector3 p)
        {
            foreach (var f in LandscapeFeature.All)
                if (f != null && f.Covers(p) && f.Stage > 0) return f.SurfaceAt(p);
            return Grade.HeightAt(p.x, p.z);
        }

        static IEnumerable<InspectionIssue> GradeAway()
        {
            if (Site.FootprintSqFt() < DwellingSqFt || !Site.BuildingBounds(out var b)) yield break;
            int n = 0;
            foreach (var (at, outward) in PerimeterSamples(b))
            {
                float fall = FallAt(at, outward);
                if (fall + Units.In(0.25f) >= RequiredFall) continue;
                yield return new InspectionIssue($"Grade falls only {Units.Format(Mathf.Max(0, fall))} in 10' (need 6\")", at + outward * 1.5f);
                if (++n >= 8) yield break;
            }
        }

        static IEnumerable<InspectionIssue> Address()
        {
            if (Site.FootprintSqFt() < DwellingSqFt) yield break;
            foreach (var f in LandscapeFeature.All)
                if (f != null && f.Kind == LandscapeKind.Mailbox && Site.InLot(f.transform.position)) yield break;
            Vector3 p = Site.BuildingBounds(out var b) ? new Vector3(b.center.x, 0, b.min.z - 3f) : Site.LotCenter;
            yield return new InspectionIssue("No address numbers / mailbox at the street", p);
        }
    }
}
