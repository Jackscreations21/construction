using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum FloorType { Lvp, Hardwood, Tile }

    /// <summary>An installed floor on a subfloor face: area (u,v of the floor FramePlane, already inside the expansion gap),
    /// holes (walls, cabinets, other floors), top height and the facts the inspector checks.</summary>
    public class FloorInstall : MonoBehaviour
    {
        public static readonly List<FloorInstall> All = new List<FloorInstall>();
        public FloorType Type;
        public FramePlane Floor;
        public Rect Area;                 // laid area (u,v), inside the gap
        public readonly List<Rect> Holes = new List<Rect>();
        public float TopY;                // finished surface height (world y)
        public float Gap;                 // expansion gap used at walls (m)
        public float MinStagger = float.MaxValue; // shortest end-joint offset between adjacent rows (m)
        public int Pieces, FreshPieces, Cuts, Nails, FaceNails;
        public bool RowsAlongU;
        public float SqFt => Area.width * Area.height / (Units.Foot * Units.Foot);

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public bool Covers(Vector3 world)
        {
            var uv = Floor.ToUV(world);
            if (!Area.Contains(uv)) return false;
            foreach (var h in Holes) if (h.Contains(uv)) return false;
            return true;
        }
    }

    /// <summary>Shared flooring helpers: room finding, obstacle holes, local frame, row layout.</summary>
    public static class Flooring
    {
        public static float SubfloorTop(FramePlane floor) => floor.FaceOffset;

        /// <summary>Local frame on the floor: x = U, y = up, z = V.</summary>
        public static Quaternion Frame(FramePlane floor) => Quaternion.LookRotation(floor.V, floor.N);

        /// <summary>Finished floor top under a world point, if any floor covers it.</summary>
        public static bool TopAt(Vector3 world, out float y)
        {
            y = 0f;
            foreach (var f in FloorInstall.All) if (f != null && f.Covers(world)) { y = f.TopY; return true; }
            return false;
        }

        /// <summary>Room rectangle (u,v) around a point on the floor: distance to the first wall/obstacle in ±U and ±V,
        /// measured 1-1/2" above the subfloor (finished wall faces, cabinets...).</summary>
        public static Rect FindRoom(FramePlane floor, Vector3 point, float maxReach = 12f)
        {
            Vector3 p = point; p.y = SubfloorTop(floor) + Units.In(1.5f);
            float R(Vector3 dir)
            {
                foreach (var h in FinishAim.SortedHits(new Ray(p, dir), maxReach))
                {
                    if (Ignore(h.collider)) continue;
                    return h.distance;
                }
                return maxReach;
            }
            var uv = floor.ToUV(p);
            return Rect.MinMaxRect(uv.x - R(-floor.U), uv.y - R(-floor.V), uv.x + R(floor.U), uv.y + R(floor.V));
        }

        static bool Ignore(Collider c)
        {
            if (c == null || c.GetComponentInParent<PlayerRig>() != null) return true;
            var fl = c.GetComponentInParent<FinishLayer>();
            if (fl != null)
            {
                string k = fl.Kind ?? "";
                if (k.StartsWith("fin-floor") || k.StartsWith("fin-trim") || k.StartsWith("bead") || k.StartsWith("fin-caulk") || k == "marker") return true;
            }
            if (c.bounds.size.x > 20f || c.bounds.size.z > 20f) return true;
            return false;
        }

        /// <summary>Footprints of everything standing on the floor inside the area (wall plates + drywall, cabinets,
        /// door jambs, other finished floors), grown by the gap. Pass to the installers as holes.</summary>
        public static List<Rect> AutoHoles(FramePlane floor, Rect area, float gap)
        {
            var res = new List<Rect>();
            float y0 = SubfloorTop(floor);
            var center = floor.World(area.center, Units.In(1.25f));
            var half = new Vector3(area.width * 0.5f, area.height * 0.5f, Units.In(1.1f));
            var rot = Quaternion.LookRotation(-floor.N, floor.V);
            var seen = new HashSet<Collider>();
            foreach (var c in Physics.OverlapBox(center, new Vector3(half.x, half.y, half.z), rot, ~0, QueryTriggerInteraction.Ignore))
            {
                if (seen.Contains(c)) continue; seen.Add(c);
                var fl = c.GetComponentInParent<FinishLayer>();
                bool otherFloor = fl != null && (fl.Kind ?? "").StartsWith("fin-floor");
                if (!otherFloor && Ignore(c)) continue;
                var pc = c.GetComponentInParent<Piece>();
                if (pc != null && pc.Def != null && pc.Def.Category == PieceCategory.Sheet && c.bounds.max.y <= y0 + 0.002f) continue; // the subfloor itself
                if (c.bounds.max.y <= y0 + 0.001f) continue;
                Rect r = FootUV(floor, c.bounds);
                float g = otherFloor ? 0f : gap;
                r = Rect.MinMaxRect(r.xMin - g, r.yMin - g, r.xMax + g, r.yMax + g);
                if (FramePlane.Overlaps(r, area)) res.Add(FramePlane.Intersect(r, Rect.MinMaxRect(area.xMin - 1f, area.yMin - 1f, area.xMax + 1f, area.yMax + 1f)));
            }
            return res;
        }

        static Rect FootUV(FramePlane fp, Bounds b)
        {
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var uv = fp.ToUV(p);
                u0 = Mathf.Min(u0, uv.x); u1 = Mathf.Max(u1, uv.x); v0 = Mathf.Min(v0, uv.y); v1 = Mathf.Max(v1, uv.y);
            }
            return Rect.MinMaxRect(u0, v0, u1, v1);
        }

        /// <summary>Joist direction under the floor (world, in plane): lumber found just below the subfloor, or — if the
        /// subfloor sits on nothing we can see — perpendicular to the sheets' long edges (how plywood is laid).</summary>
        public static Vector3 JoistDirection(FramePlane floor, Rect area)
        {
            var center = floor.World(area.center, -0.15f);
            var half = new Vector3(area.width * 0.5f, area.height * 0.5f, 0.13f);
            int nu = 0, nv = 0;
            foreach (var c in Physics.OverlapBox(center, half, Quaternion.LookRotation(-floor.N, floor.V), ~0, QueryTriggerInteraction.Ignore))
            {
                var p = c.GetComponentInParent<Piece>();
                if (p == null || p.Def == null || p.Def.Category != PieceCategory.Lumber) continue;
                Vector3 L = Vector3.ProjectOnPlane(p.transform.forward, floor.N);
                if (L.sqrMagnitude < 0.5f) continue;
                if (Mathf.Abs(Vector3.Dot(L.normalized, floor.U)) > 0.7f) nu++; else nv++;
            }
            if (nu + nv > 0) return nu > nv ? floor.U : floor.V;
            return floor.U; // FramePlane.V runs along the sheets' long edges; joists cross them
        }

        public static GameObject Spawn(FramePlane floor, string name, Mesh mesh, Material[] mats, string kind)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(floor.Origin, Frame(floor));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            if (mesh.vertexCount > 0) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<FinishLayer>().Kind = kind;
            return go;
        }

        /// <summary>Holes in row space (a = along the rows, b = across).</summary>
        public static List<Rect> ToRowSpace(IList<Rect> holes, bool alongU)
        {
            var l = new List<Rect>();
            if (holes != null) foreach (var h in holes) l.Add(alongU ? h : Rect.MinMaxRect(h.yMin, h.xMin, h.yMax, h.xMax));
            return l;
        }

        /// <summary>Row widths across the area: full widths, last row ripped; if the rip would be skinny (&lt; 1/3 board),
        /// split the difference between the first and last rows (what installers do).</summary>
        public static List<float> RowWidths(float span, float w)
        {
            var l = new List<float>();
            int n = Mathf.Max(1, Mathf.CeilToInt(span / w - 1e-4f));
            float rem = span - (n - 1) * w;
            for (int i = 0; i < n; i++) l.Add(w);
            if (n == 1) { l[0] = span; return l; }
            if (rem < w / 3f) { float r = (w + rem) * 0.5f; l[0] = r; l[n - 1] = r; for (int i = 1; i < n - 1; i++) l[i] = w; }
            else l[n - 1] = rem;
            return l;
        }
    }
}
