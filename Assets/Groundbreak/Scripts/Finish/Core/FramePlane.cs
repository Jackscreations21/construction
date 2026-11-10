using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// A framed face: the coplanar faces of framing members (studs+plates of a wall, the bottoms of ceiling joists,
    /// the faces of sheathing sheets...) found from one raycast hit. Works with ANY Piece layout, so finishes attach to
    /// whatever framing exists. 2D coordinates (u,v) are meters in the face plane:
    ///   N = outward normal (toward the person standing in front of the face),
    ///   V = "up" in the face (world up for walls; joist direction for ceilings/floors),
    ///   U = viewer's right = Cross(N, V).
    /// Local object frame for things built on the face: <see cref="Rotation"/> maps local x→U, y→V, z→-N
    /// (so local z is depth INTO the framing; a layer standing d proud of the face sits at local z = -d).
    /// </summary>
    public class FramePlane
    {
        public class Member
        {
            public Piece Piece;
            public Rect R;          // projected footprint on the face (u,v meters)
            public bool AlongV;     // long axis runs along V (studs in a wall, joists in a ceiling)
            public float Depth;     // member depth behind the face (meters)
            public float CenterU => R.center.x;
            public float CenterV => R.center.y;
        }

        public struct Bay
        {
            public Rect R;          // clear opening between members (u,v)
            public Member Left, Right;
            public float Depth;     // cavity depth (min of the two side members)
            public bool IsOpening;  // too wide for a framing bay (rough opening) or holds a door/window
        }

        public struct Obstacle
        {
            public Rect R;           // footprint on the face (u,v)
            public float Proud;      // how far its front sticks out past the face plane (m, negative = recessed)
            public Collider Collider;
            public string Name;
        }

        public Vector3 N, U, V;
        public float FaceOffset;     // dot(N, any point on the face)
        public Vector3 Origin;       // world point of (u,v) = (0,0) on the face
        public Rect Bounds;
        public readonly List<Member> Members = new List<Member>();
        public bool IsWall => Mathf.Abs(N.y) < 0.3f;
        public bool IsCeiling => N.y < -0.7f;
        public bool IsFloor => N.y > 0.7f;
        public Quaternion Rotation => Quaternion.LookRotation(-N, V);

        public Vector3 World(Vector2 uv, float proud = 0f) => Origin + U * uv.x + V * uv.y + N * proud;
        public Vector3 World(float u, float v, float proud = 0f) => Origin + U * u + V * v + N * proud;
        public Vector2 ToUV(Vector3 world) { var d = world - Origin; return new Vector2(Vector3.Dot(d, U), Vector3.Dot(d, V)); }
        public float ProudOf(Vector3 world) => Vector3.Dot(world, N) - FaceOffset;
        /// <summary>Transform-space position of the face origin and its rotation, for parenting built meshes.</summary>
        public Matrix4x4 LocalToWorld => Matrix4x4.TRS(Origin, Rotation, Vector3.one);

        /// <summary>Same face (normal and plane within tolerance, footprints overlapping)?</summary>
        public bool SameFace(FramePlane o, float tol = 0.006f)
        {
            if (o == null || Vector3.Dot(N, o.N) < 0.999f || Mathf.Abs(FaceOffset - o.FaceOffset) > tol) return false;
            var a = WorldBounds(); var b = o.WorldBounds(); a.Expand(0.05f);
            return a.Intersects(b);
        }

        public Bounds WorldBounds()
        {
            var b = new Bounds(World(Bounds.min), Vector3.zero);
            b.Encapsulate(World(Bounds.max)); b.Encapsulate(World(Bounds.xMin, Bounds.yMax)); b.Encapsulate(World(Bounds.xMax, Bounds.yMin));
            return b;
        }

        public override string ToString() => $"FramePlane N={N} off={FaceOffset:F3} {Units.Format(Bounds.width)} x {Units.Format(Bounds.height)}, {Members.Count} members";

        // ================================================================ discovery

        static bool IsFraming(Piece p) => p != null && p.Def != null && (p.Def.Category == PieceCategory.Lumber || p.Def.Category == PieceCategory.Sheet);

        /// <summary>Extra framing filter so other features can exclude their own lumber-like pieces (e.g. deck boards).</summary>
        public static Func<Piece, bool> ExcludeMember;

        /// <summary>Face of the framing member hit by a ray. Null if the hit isn't on a framing Piece.</summary>
        public static FramePlane FromHit(RaycastHit hit, float clusterGap = 0.65f)
        {
            var p = hit.collider != null ? hit.collider.GetComponentInParent<Piece>() : null;
            if (!IsFraming(p)) return null;
            return FromPieceFace(p, hit.normal, clusterGap);
        }

        /// <summary>Face of the given piece that points closest to <paramref name="approxNormal"/>, grown to all coplanar framing.</summary>
        public static FramePlane FromPieceFace(Piece p, Vector3 approxNormal, float clusterGap = 0.65f)
        {
            if (!IsFraming(p)) return null;
            Vector3 n = SnapToAxis(p.transform, approxNormal, out float half, p.Size);
            var fp = new FramePlane { N = n };
            fp.FaceOffset = Vector3.Dot(p.transform.position, n) + half;

            // Candidates: every framing piece with a face in this plane, facing N.
            var all = UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None);
            var cands = new List<Piece>();
            foreach (var q in all)
            {
                if (!IsFraming(q) || (ExcludeMember != null && ExcludeMember(q))) continue;
                if ((q.transform.position - p.transform.position).sqrMagnitude > 40f * 40f) continue;
                if (!AxisAligned(q.transform, n, q.Size, out float qh, out int axis)) continue;
                if (axis == 2 && q.Def.Category == PieceCategory.Lumber && q != p) continue; // cut ends (end grain) aren't a nailing face
                if (Mathf.Abs(Vector3.Dot(q.transform.position, n) + qh - fp.FaceOffset) > 0.006f) continue;
                cands.Add(q);
            }

            // Plane axes
            if (Mathf.Abs(n.y) < 0.7f) fp.V = Vector3.ProjectOnPlane(Vector3.up, n).normalized;
            else
            {
                // floors/ceilings: V along the majority member length direction (joists)
                Vector3 best = Vector3.zero; int bestCount = -1;
                foreach (var q in cands)
                {
                    Vector3 L = Vector3.ProjectOnPlane(q.transform.forward, n);
                    if (L.sqrMagnitude < 0.5f) continue; L.Normalize();
                    int count = 0; foreach (var r in cands) if (Mathf.Abs(Vector3.Dot(Vector3.ProjectOnPlane(r.transform.forward, n).normalized, L)) > 0.99f) count++;
                    if (count > bestCount) { bestCount = count; best = L; }
                }
                if (best == Vector3.zero) best = Vector3.ProjectOnPlane(Vector3.forward, n).normalized;
                // keep a stable sign: prefer +x / +z
                if (best.x + best.z * 0.5f < 0f) best = -best;
                fp.V = best;
            }
            fp.U = Vector3.Cross(n, fp.V).normalized;
            fp.Origin = n * fp.FaceOffset; // origin on the plane nearest world zero; Bounds are relative to it

            // Project, then keep the cluster connected to the hit piece.
            var mem = new List<Member>();
            foreach (var q in cands) mem.Add(fp.Project(q, n));
            var keep = new List<Member>(); var open = new Queue<Member>();
            var start = mem.Find(m => m.Piece == p);
            if (start == null) return null;
            keep.Add(start); open.Enqueue(start); mem.Remove(start);
            while (open.Count > 0)
            {
                var a = open.Dequeue();
                for (int i = mem.Count - 1; i >= 0; i--)
                {
                    if (RectGap(a.R, mem[i].R) > clusterGap) continue;
                    keep.Add(mem[i]); open.Enqueue(mem[i]); mem.RemoveAt(i);
                }
            }
            fp.Members.AddRange(keep);
            var b = keep[0].R;
            foreach (var m in keep) b = Union(b, m.R);
            fp.Bounds = b;
            return fp;
        }

        Member Project(Piece q, Vector3 n)
        {
            var t = q.transform; Vector3 h = q.Size * 0.5f;
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue, d0 = float.MaxValue, d1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = t.TransformPoint(new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
                var uv = ToUV(c); float d = Vector3.Dot(c, n);
                u0 = Mathf.Min(u0, uv.x); u1 = Mathf.Max(u1, uv.x); v0 = Mathf.Min(v0, uv.y); v1 = Mathf.Max(v1, uv.y);
                d0 = Mathf.Min(d0, d); d1 = Mathf.Max(d1, d);
            }
            Vector3 L = t.forward;
            bool alongV = Mathf.Abs(Vector3.Dot(L, V)) > 0.7f || (Mathf.Abs(Vector3.Dot(L, n)) > 0.7f && (v1 - v0) > (u1 - u0));
            // sheets: "along" their longer face dimension
            if (q.Def.Category == PieceCategory.Sheet) alongV = (v1 - v0) >= (u1 - u0);
            return new Member { Piece = q, R = Rect.MinMaxRect(u0, v0, u1, v1), AlongV = alongV, Depth = d1 - d0 };
        }

        static Vector3 SnapToAxis(Transform t, Vector3 approx, out float half, Vector3 size)
        {
            Vector3[] ax = { t.right, t.up, t.forward }; float[] hs = { size.x * 0.5f, size.y * 0.5f, size.z * 0.5f };
            int best = 0; float bd = -1f;
            for (int i = 0; i < 3; i++) { float d = Mathf.Abs(Vector3.Dot(ax[i], approx)); if (d > bd) { bd = d; best = i; } }
            half = hs[best];
            Vector3 n = ax[best] * Mathf.Sign(Vector3.Dot(ax[best], approx));
            return Clean(n);
        }

        static bool AxisAligned(Transform t, Vector3 n, Vector3 size, out float half, out int axis)
        {
            half = 0f; axis = -1;
            Vector3[] ax = { t.right, t.up, t.forward }; float[] hs = { size.x * 0.5f, size.y * 0.5f, size.z * 0.5f };
            for (int i = 0; i < 3; i++) if (Mathf.Abs(Vector3.Dot(ax[i], n)) > 0.999f) { half = hs[i]; axis = i; return true; }
            return false;
        }

        static Vector3 Clean(Vector3 v)
        {
            v.x = Mathf.Abs(v.x) < 1e-4f ? 0f : v.x; v.y = Mathf.Abs(v.y) < 1e-4f ? 0f : v.y; v.z = Mathf.Abs(v.z) < 1e-4f ? 0f : v.z;
            return v.normalized;
        }

        public static float RectGap(Rect a, Rect b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
            float dy = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
            return Mathf.Max(dx, dy);
        }
        public static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        public static bool Overlaps(Rect a, Rect b, float eps = 0.001f) => a.xMin < b.xMax - eps && b.xMin < a.xMax - eps && a.yMin < b.yMax - eps && b.yMin < a.yMax - eps;
        public static Rect Intersect(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));

        // ================================================================ framing queries

        /// <summary>Center lines (u) of members running along V — where screws/nails land when fastening across them
        /// (stud centers on a wall, joist centers on a ceiling). Optionally only those spanning v.</summary>
        public List<float> FramingLinesU(float? atV = null)
        {
            var l = new List<float>();
            foreach (var m in Members)
                if (m.AlongV && (atV == null || (atV >= m.R.yMin - 0.01f && atV <= m.R.yMax + 0.01f))) l.Add(m.CenterU);
            l.Sort();
            return l;
        }

        /// <summary>Center lines (v) of members running along U (plates, headers, blocking).</summary>
        public List<float> FramingLinesV(float? atU = null)
        {
            var l = new List<float>();
            foreach (var m in Members)
                if (!m.AlongV && (atU == null || (atU >= m.R.xMin - 0.01f && atU <= m.R.xMax + 0.01f))) l.Add(m.CenterV);
            l.Sort();
            return l;
        }

        /// <summary>Is there framing directly behind this face point (within tolerance)?</summary>
        public Member MemberAt(Vector2 uv, float tol = 0f)
        {
            foreach (var m in Members)
                if (uv.x >= m.R.xMin - tol && uv.x <= m.R.xMax + tol && uv.y >= m.R.yMin - tol && uv.y <= m.R.yMax + tol) return m;
            return null;
        }

        /// <summary>Nearest along-V member center to u (e.g. snap a screw or a sheet end to a stud center).</summary>
        public float SnapToFramingU(float u, float? atV = null)
        {
            float best = u, bd = float.MaxValue;
            foreach (var c in FramingLinesU(atV)) { float d = Mathf.Abs(c - u); if (d < bd) { bd = d; best = c; } }
            return best;
        }

        /// <summary>Cavities between members (stud bays / joist bays), split by headers, sills and blocking.</summary>
        public List<Bay> Bays()
        {
            var res = new List<Bay>();
            var along = Members.FindAll(m => m.AlongV);
            var cross = Members.FindAll(m => !m.AlongV && m.Piece.Def.Category != PieceCategory.Sheet);
            along.Sort((a, b) => a.R.xMin.CompareTo(b.R.xMin));
            foreach (var a in along)
            {
                var uncovered = new List<Vector2> { new Vector2(a.R.yMin, a.R.yMax) };
                foreach (var b in along)
                {
                    if (b == a || b.R.xMin < a.R.xMax - 0.005f) continue;
                    if (uncovered.Count == 0) break;
                    var hits = new List<Vector2>(); var rest = new List<Vector2>();
                    foreach (var iv in uncovered)
                    {
                        float lo = Mathf.Max(iv.x, b.R.yMin), hi = Mathf.Min(iv.y, b.R.yMax);
                        if (hi - lo > 0.01f)
                        {
                            hits.Add(new Vector2(lo, hi));
                            if (lo - iv.x > 0.01f) rest.Add(new Vector2(iv.x, lo));
                            if (iv.y - hi > 0.01f) rest.Add(new Vector2(hi, iv.y));
                        }
                        else rest.Add(iv);
                    }
                    uncovered = rest;
                    float w = b.R.xMin - a.R.xMax;
                    if (w < 0.02f) continue;
                    foreach (var h in hits)
                        foreach (var r in SubtractCross(Rect.MinMaxRect(a.R.xMax, h.x, b.R.xMin, h.y), cross))
                            res.Add(new Bay { R = r, Left = a, Right = b, Depth = Mathf.Min(a.Depth, b.Depth), IsOpening = w > 0.59f || HasProduct(r) });
                }
            }
            return res;
        }

        static List<Rect> SubtractCross(Rect r, List<Member> cross)
        {
            var parts = new List<Rect> { r };
            foreach (var c in cross)
            {
                var next = new List<Rect>();
                foreach (var p in parts)
                {
                    if (!(c.R.xMin < p.xMax - 0.01f && c.R.xMax > p.xMin + 0.01f) || c.R.yMax <= p.yMin + 0.001f || c.R.yMin >= p.yMax - 0.001f) { next.Add(p); continue; }
                    if (c.R.yMin - p.yMin > 0.03f) next.Add(Rect.MinMaxRect(p.xMin, p.yMin, p.xMax, c.R.yMin));
                    if (p.yMax - c.R.yMax > 0.03f) next.Add(Rect.MinMaxRect(p.xMin, c.R.yMax, p.xMax, p.yMax));
                }
                parts = next;
            }
            return parts;
        }

        /// <summary>The bay (or opening) containing a face point.</summary>
        public bool BayAt(Vector2 uv, out Bay bay)
        {
            foreach (var b in Bays()) if (b.R.Contains(uv)) { bay = b; return true; }
            bay = default; return false;
        }

        /// <summary>Rough openings (doors/windows): over-wide bays plus door/window products sitting in the wall.</summary>
        public List<Rect> Openings()
        {
            var l = new List<Rect>();
            foreach (var b in Bays()) if (b.IsOpening) l.Add(b.R);
            // merge vertically adjacent opening pieces of the same column (e.g. split by nothing) — keep as is
            return l;
        }

        bool HasProduct(Rect r)
        {
            var center = World(r.center, -0.05f);
            var size = new Vector3(Mathf.Max(0.01f, r.width - 0.02f), Mathf.Max(0.01f, r.height - 0.02f), 0.2f);
            foreach (var c in Physics.OverlapBox(center, size * 0.5f, Rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                var p = c.GetComponentInParent<Piece>();
                if (p != null && p.Def != null && (p.Def.Category == PieceCategory.Door || p.Def.Category == PieceCategory.Window || p.Def.Category == PieceCategory.GarageDoor)) return true;
            }
            return false;
        }

        // ================================================================ obstacles (boxes, pipes, ducts)

        /// <summary>Extension point: other features add obstacle footprints that have no collider (world bounds).</summary>
        public static readonly List<Func<IEnumerable<Bounds>>> ObstacleSources = new List<Func<IEnumerable<Bounds>>>();

        /// <summary>Things in the cavity behind the face (electrical boxes, pipes, ducts) — anything that isn't framing.
        /// Proud ≈ 0 means flush with the face: a box set for drywall sticks out ~1/2" (Proud ≈ +0.0127).</summary>
        public List<Obstacle> Obstacles(Rect? area = null, float depth = 0f)
        {
            var res = new List<Obstacle>();
            Rect a = area ?? Bounds;
            if (depth <= 0f) { depth = 0.09f; foreach (var m in Members) depth = Mathf.Max(depth, m.Depth); }
            Vector3 center = World(a.center, -depth * 0.5f + 0.02f);
            Vector3 half = new Vector3(Mathf.Max(0.001f, a.width * 0.5f - 0.006f), Mathf.Max(0.001f, a.height * 0.5f - 0.006f), depth * 0.5f + 0.02f);
            var seen = new HashSet<Collider>();
            foreach (var c in Physics.OverlapBox(center, half, Rotation, ~0, QueryTriggerInteraction.Collide))
            {
                if (seen.Contains(c)) continue; seen.Add(c);
                if (c.bounds.size.x > 20f || c.bounds.size.z > 20f) continue; // ground / terrain
                var p = c.GetComponentInParent<Piece>();
                if (p != null && IsFraming(p)) continue;
                if (c.GetComponentInParent<FinishLayer>() != null) continue;
                if (c.GetComponentInParent<PlayerRig>() != null) continue;
                if (p != null && p.Def != null && (p.Def.Category == PieceCategory.Door || p.Def.Category == PieceCategory.Window || p.Def.Category == PieceCategory.GarageDoor)) continue;
                res.Add(MakeObstacle(c.bounds, c, c.name));
            }
            foreach (var src in ObstacleSources)
                foreach (var b in src())
                {
                    var o = MakeObstacle(b, null, "obstacle");
                    if (Overlaps(o.R, a) && o.Proud > -depth - 0.02f && o.Proud < 0.1f) res.Add(o);
                }
            return res;
        }

        Obstacle MakeObstacle(Bounds b, Collider c, string name)
        {
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue, front = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var uv = ToUV(p);
                u0 = Mathf.Min(u0, uv.x); u1 = Mathf.Max(u1, uv.x); v0 = Mathf.Min(v0, uv.y); v1 = Mathf.Max(v1, uv.y);
                front = Mathf.Max(front, ProudOf(p));
            }
            return new Obstacle { R = Rect.MinMaxRect(u0, v0, u1, v1), Proud = front, Collider = c, Name = name };
        }
    }

    /// <summary>Marker on every GameObject a finish trade builds (drywall, batts, siding...). FramePlane ignores these when
    /// it looks for obstacles, and tools use <see cref="Kind"/> to recognize what they're aiming at.</summary>
    public class FinishLayer : MonoBehaviour
    {
        public string Kind;          // e.g. "batt", "drywall", "vinyl", "brick", "trim"
        public object Owner;         // the feature's own model object
    }
}
