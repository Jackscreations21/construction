using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A hole bored through a framing member (local space of the Piece).</summary>
    public struct BoredHole
    {
        public Vector3 LocalPos;   // center
        public Vector3 LocalAxis;  // bore direction (unit)
        public float Dia;          // meters
        public string Bit;
    }

    /// <summary>Holes drilled in a lumber Piece. Code limits (IRC R602.6): bored holes ≤ 60% of member depth in
    /// non-bearing walls, ≤ 40% in bearing walls (60% if doubled), ≥ 5/8" from the edge. Notches ≤ 25% / 40%.</summary>
    public class StudHoles : MonoBehaviour
    {
        public readonly List<BoredHole> Holes = new List<BoredHole>();
        /// <summary>Bearing wall member (stricter 40% limit). Unknown framing = treated as non-bearing but flagged above 40%.</summary>
        public bool Bearing;
        GameObject _vis;

        public Piece Piece => GetComponent<Piece>();

        /// <summary>Depth of the member across which the hole size is limited (the dimension perpendicular to both
        /// the bore axis and the member's length), in meters.</summary>
        public float DepthFor(Vector3 localAxis)
        {
            var s = Piece.Size;
            // lumber: X = wide face (3.5"), Y = thickness (1.5"), Z = length
            Vector3 a = new Vector3(Mathf.Abs(localAxis.x), Mathf.Abs(localAxis.y), Mathf.Abs(localAxis.z));
            if (a.y >= a.x) return s.x;   // bored through the 1.5" face -> limited by the 3.5" depth
            return s.y;
        }

        public float EdgeDistance(BoredHole h)
        {
            var s = Piece.Size;
            Vector3 a = new Vector3(Mathf.Abs(h.LocalAxis.x), Mathf.Abs(h.LocalAxis.y), 0);
            float half = (a.y >= a.x ? s.x : s.y) * 0.5f;
            float c = a.y >= a.x ? h.LocalPos.x : h.LocalPos.y;
            return half - Mathf.Abs(c) - h.Dia * 0.5f;
        }

        public float Ratio(BoredHole h) => h.Dia / Mathf.Max(1e-4f, DepthFor(h.LocalAxis));

        /// <summary>Bores a hole at a world point along a world axis (snapped to the member's thin axis).</summary>
        public static BoredHole Bore(Piece piece, Vector3 worldPoint, Vector3 worldAxis, float diaMeters, string bit)
        {
            var sh = piece.GetComponent<StudHoles>();
            if (sh == null) sh = piece.gameObject.AddComponent<StudHoles>();
            Vector3 la = piece.transform.InverseTransformDirection(worldAxis).normalized;
            // bore straight through the face closest to the drill direction (X or Y for lumber)
            la = Mathf.Abs(la.y) >= Mathf.Abs(la.x) ? Vector3.up : Vector3.right;
            Vector3 lp = piece.transform.InverseTransformPoint(worldPoint);
            if (la == Vector3.up) lp.y = 0; else lp.x = 0;
            var h = new BoredHole { LocalPos = lp, LocalAxis = la, Dia = diaMeters, Bit = bit };
            sh.Holes.Add(h);
            sh.Rebuild();
            NetGraph.Touch();
            return h;
        }

        public void Rebuild()
        {
            if (_vis != null) Destroy(_vis);
            if (Holes.Count == 0) return;
            var mm = new MepMesh { Origin = transform.position };
            var dark = MepLook.Get(SurfaceKind.MatteBlack, new Color(0.35f, 0.25f, 0.18f));
            var wood = MepLook.Get(SurfaceKind.PineEndGrain);
            foreach (var h in Holes)
            {
                Vector3 c = transform.TransformPoint(h.LocalPos), ax = transform.TransformDirection(h.LocalAxis);
                float half = (h.LocalAxis == Vector3.up ? Piece.Size.y : Piece.Size.x) * 0.5f + 0.0008f;
                // inner wall of the bore (end grain, inward-ish) + dark disc in the middle so it reads as a hole
                mm.Band(c - ax * half, c + ax * half, MepMesh.Circle(h.Dia * 0.5f, 18), 0, false);
                mm.Band(c - ax * 0.001f, c + ax * 0.001f, MepMesh.Circle(h.Dia * 0.5f * 0.98f, 18), 1, true);
            }
            _vis = mm.Spawn(transform, "Holes", new[] { wood, dark });
        }

        /// <summary>Does any hole in this piece let a run of clearance dia pass at worldPoint?</summary>
        public bool Passes(Vector3 worldPoint, float clearanceDia)
        {
            Vector3 lp = transform.InverseTransformPoint(worldPoint);
            foreach (var h in Holes)
            {
                Vector3 d = lp - h.LocalPos; d -= h.LocalAxis * Vector3.Dot(d, h.LocalAxis);
                if (d.magnitude + clearanceDia * 0.5f <= h.Dia * 0.5f + 0.004f) return true;
            }
            return false;
        }
    }

    public struct StudConflict { public NetRun Run; public Piece Piece; public Vector3 Point; public string Note; }

    /// <summary>Checks runs against framing: every lumber member a run passes through needs a bored hole big enough.</summary>
    public static class StudCheck
    {
        public static List<StudConflict> Find(NetRun run)
        {
            var o = new List<StudConflict>();
            if (run == null || run.Spec.Kind == NetKind.Duct) return o; // ducts run in joist bays/chases, not bored
            float clr = run.Spec.ClearanceDia;
            var pts = run.Points;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 a = pts[i - 1], b = pts[i];
                float l = Vector3.Distance(a, b);
                if (l < 1e-4f) continue;
                var hits = Physics.RaycastAll(a, (b - a) / l, l, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                var seen = new HashSet<Piece>();
                foreach (var h in hits)
                {
                    var p = h.collider.GetComponentInParent<Piece>();
                    if (p == null || p.Def == null || p.Def.Category != PieceCategory.Lumber || !seen.Add(p)) continue;
                    // the run must actually pass through (not just start inside a stud end)
                    var sh = p.GetComponent<StudHoles>();
                    Vector3 mid = h.point + (b - a) / l * Mathf.Min(p.Size.y, p.Size.x) * 0.5f;
                    if (sh == null || !sh.Passes(mid, clr))
                        o.Add(new StudConflict { Run = run, Piece = p, Point = h.point, Note = $"{run.Spec.Name} passes through {p.Def.Name} without a bored hole ≥ {Fittings.Fraction(Units.ToInches(clr))}\"" });
                }
            }
            return o;
        }

        /// <summary>All over-bored / too-close-to-edge holes in the scene.</summary>
        public static List<(StudHoles piece, BoredHole hole, string note)> OverBored()
        {
            var o = new List<(StudHoles, BoredHole, string)>();
            foreach (var sh in Object.FindObjectsByType<StudHoles>(FindObjectsSortMode.None))
                foreach (var h in sh.Holes)
                {
                    float r = sh.Ratio(h);
                    string depth = Units.Format(sh.DepthFor(h.LocalAxis)), dia = Fittings.Fraction(Units.ToInches(h.Dia)) + "\"";
                    if (r > 0.6f + 1e-3f) o.Add((sh, h, $"Over-bored: {dia} hole is {r * 100:0}% of the {depth} member (max 60%)"));
                    else if (sh.Bearing && r > 0.4f + 1e-3f) o.Add((sh, h, $"Over-bored bearing stud: {dia} hole is {r * 100:0}% (max 40%)"));
                    if (sh.EdgeDistance(h) < Units.In(0.625f) - 1e-4f) o.Add((sh, h, $"{dia} hole is closer than 5/8\" to the edge of the stud"));
                }
            return o;
        }
    }
}
