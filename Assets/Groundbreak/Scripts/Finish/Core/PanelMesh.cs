using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Geometry helpers for flat finish goods (sheets, batts, films) laid on a <see cref="FramePlane"/>.
    /// Everything is built in the plane's local frame: x = U, y = V, z = depth into the framing (z = -proud).</summary>
    public static class PanelMesh
    {
        /// <summary>Splits rect minus holes into non-overlapping solid cells (grid split on all hole edges).</summary>
        public static List<Rect> Cells(Rect outer, IList<Rect> holes)
        {
            var xs = new List<float> { outer.xMin, outer.xMax };
            var ys = new List<float> { outer.yMin, outer.yMax };
            var hs = new List<Rect>();
            if (holes != null)
                foreach (var h0 in holes)
                {
                    var h = FramePlane.Intersect(h0, outer);
                    if (h.width <= 0.0005f || h.height <= 0.0005f) continue;
                    hs.Add(h); xs.Add(h.xMin); xs.Add(h.xMax); ys.Add(h.yMin); ys.Add(h.yMax);
                }
            xs.Sort(); ys.Sort();
            var cells = new List<Rect>();
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                if (xs[i + 1] - xs[i] < 0.0005f) continue;
                for (int j = 0; j + 1 < ys.Count; j++)
                {
                    if (ys[j + 1] - ys[j] < 0.0005f) continue;
                    var c = Rect.MinMaxRect(xs[i], ys[j], xs[i + 1], ys[j + 1]);
                    bool inHole = false;
                    foreach (var h in hs) if (h.Contains(c.center)) { inHole = true; break; }
                    if (!inHole) cells.Add(c);
                }
            }
            // merge horizontally adjacent cells in the same row to cut vertex count
            cells.Sort((a, b) => a.yMin != b.yMin ? a.yMin.CompareTo(b.yMin) : a.xMin.CompareTo(b.xMin));
            var merged = new List<Rect>();
            foreach (var c in cells)
            {
                if (merged.Count > 0)
                {
                    var l = merged[merged.Count - 1];
                    if (Mathf.Abs(l.yMin - c.yMin) < 1e-5f && Mathf.Abs(l.yMax - c.yMax) < 1e-5f && Mathf.Abs(l.xMax - c.xMin) < 1e-5f)
                    { merged[merged.Count - 1] = Rect.MinMaxRect(l.xMin, l.yMin, c.xMax, l.yMax); continue; }
                }
                merged.Add(c);
            }
            return merged;
        }

        /// <summary>A flat slab with rectangular holes: front face (toward the viewer, at local z = zFront), back face
        /// (z = zFront + thickness), and edge faces around the outside and every hole. Submeshes: front, back, edge.
        /// uHint = texture U direction in plane space (e.g. Vector3.right for horizontal grain).</summary>
        public static void AddSlab(MeshBuilder b, Rect outer, IList<Rect> holes, float zFront, float thickness, int subFront, int subBack, int subEdge, Vector3 uHint = default)
        {
            if (uHint == default) uHint = Vector3.right;
            var cells = Cells(outer, holes);
            float z0 = zFront, z1 = zFront + thickness;
            foreach (var c in cells)
            {
                b.AddQuad(new Vector3(c.xMin, c.yMin, z0), new Vector3(c.xMax, c.yMin, z0), new Vector3(c.xMax, c.yMax, z0), new Vector3(c.xMin, c.yMax, z0), subFront, Vector3.back, uHint);
                if (subBack >= 0)
                    b.AddQuad(new Vector3(c.xMin, c.yMin, z1), new Vector3(c.xMax, c.yMin, z1), new Vector3(c.xMax, c.yMax, z1), new Vector3(c.xMin, c.yMax, z1), subBack, Vector3.forward, uHint);
            }
            if (subEdge < 0 || thickness <= 0f) return;
            // Edges: for each cell side, emit an edge face where the neighbor is empty (outside or hole).
            foreach (var c in cells)
            {
                Edge(b, cells, c, new Vector2(c.xMin, c.yMin), new Vector2(c.xMin, c.yMax), Vector3.left, z0, z1, subEdge);
                Edge(b, cells, c, new Vector2(c.xMax, c.yMin), new Vector2(c.xMax, c.yMax), Vector3.right, z0, z1, subEdge);
                Edge(b, cells, c, new Vector2(c.xMin, c.yMin), new Vector2(c.xMax, c.yMin), Vector3.down, z0, z1, subEdge);
                Edge(b, cells, c, new Vector2(c.xMin, c.yMax), new Vector2(c.xMax, c.yMax), Vector3.up, z0, z1, subEdge);
            }
        }

        static void Edge(MeshBuilder b, List<Rect> cells, Rect self, Vector2 p, Vector2 q, Vector3 n, float z0, float z1, int sub)
        {
            // Split the side at neighbor boundaries; emit the uncovered parts.
            bool vertical = Mathf.Abs(p.x - q.x) < 1e-6f;
            float a0 = vertical ? p.y : p.x, a1 = vertical ? q.y : q.x;
            var covered = new List<Vector2>();
            foreach (var o in cells)
            {
                if (o == self) continue;
                if (vertical)
                {
                    bool touch = n.x < 0 ? Mathf.Abs(o.xMax - p.x) < 1e-5f : Mathf.Abs(o.xMin - p.x) < 1e-5f;
                    if (touch) covered.Add(new Vector2(Mathf.Max(a0, o.yMin), Mathf.Min(a1, o.yMax)));
                }
                else
                {
                    bool touch = n.y < 0 ? Mathf.Abs(o.yMax - p.y) < 1e-5f : Mathf.Abs(o.yMin - p.y) < 1e-5f;
                    if (touch) covered.Add(new Vector2(Mathf.Max(a0, o.xMin), Mathf.Min(a1, o.xMax)));
                }
            }
            covered.RemoveAll(c => c.y - c.x <= 1e-5f);
            covered.Sort((x, y) => x.x.CompareTo(y.x));
            float cur = a0;
            void Emit(float s, float e)
            {
                if (e - s < 1e-5f) return;
                Vector3 A = vertical ? new Vector3(p.x, s, z0) : new Vector3(s, p.y, z0);
                Vector3 B = vertical ? new Vector3(p.x, e, z0) : new Vector3(e, p.y, z0);
                b.AddQuad(A, B, new Vector3(B.x, B.y, z1), new Vector3(A.x, A.y, z1), sub, n);
            }
            foreach (var c in covered) { Emit(cur, c.x); cur = Mathf.Max(cur, c.y); }
            Emit(cur, a1);
        }

        /// <summary>Makes a child GameObject on the face with a mesh + materials and (optionally) a MeshCollider.</summary>
        public static GameObject Spawn(FramePlane fp, string name, Mesh mesh, Material[] mats, Transform parent = null, bool collider = true, string kind = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(fp.Origin, fp.Rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
            if (collider && mesh.vertexCount > 0) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            var fl = go.AddComponent<FinishLayer>(); fl.Kind = kind ?? name;
            return go;
        }

        /// <summary>Replaces mesh/materials/collider of an object made with <see cref="Spawn"/>.</summary>
        public static void Replace(GameObject go, Mesh mesh, Material[] mats)
        {
            var mf = go.GetComponent<MeshFilter>(); var old = mf.sharedMesh; mf.sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = mats;
            var mc = go.GetComponent<MeshCollider>(); if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = mesh; }
            if (old != null && old != mesh) Object.Destroy(old);
        }
    }
}
