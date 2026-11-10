using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>Swept-profile mesh builder for pipes, cables, ducts and fittings. Real size, UVs in meters
    /// (U along the path, V around the profile). Several submeshes; caller supplies matching materials.</summary>
    public class MepMesh
    {
        readonly List<Vector3> v = new List<Vector3>(1024);
        readonly List<Vector3> n = new List<Vector3>(1024);
        readonly List<Vector2> uv = new List<Vector2>(1024);
        readonly List<List<int>> subs = new List<List<int>>();
        public Vector3 Origin; // subtracted from all points (mesh local space = world - Origin)

        List<int> Sub(int i) { while (subs.Count <= i) subs.Add(new List<int>()); return subs[i]; }

        // ---------------------------------------------------------------- profiles
        public struct Profile { public Vector2[] P; public Vector2[] N; public bool Closed; }

        public static Profile Circle(float r, int seg = 14)
        {
            var p = new Vector2[seg + 1]; var nn = new Vector2[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                nn[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p[i] = nn[i] * r;
            }
            return new Profile { P = p, N = nn, Closed = true };
        }

        /// <summary>Rectangle w x h with hard edges (duplicated corners), x = side, y = up.</summary>
        public static Profile Rect(float w, float h)
        {
            float x = w * 0.5f, y = h * 0.5f;
            var c = new[] { new Vector2(x, -y), new Vector2(x, y), new Vector2(-x, y), new Vector2(-x, -y) };
            var nrm = new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            var p = new List<Vector2>(); var nn = new List<Vector2>();
            for (int i = 0; i < 4; i++) { p.Add(c[i]); nn.Add(nrm[i]); p.Add(c[(i + 1) % 4]); nn.Add(nrm[i]); }
            return new Profile { P = p.ToArray(), N = nn.ToArray(), Closed = false /* explicit strips */ };
        }

        /// <summary>Stadium (flat NM cable): width w, thickness t.</summary>
        public static Profile Stadium(float w, float t, int capSeg = 5)
        {
            float r = t * 0.5f, cx = w * 0.5f - r;
            var p = new List<Vector2>(); var nn = new List<Vector2>();
            for (int i = 0; i <= capSeg; i++) { float a = -Mathf.PI / 2 + Mathf.PI * i / capSeg; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p.Add(new Vector2(cx, 0) + d * r); nn.Add(d); }
            for (int i = 0; i <= capSeg; i++) { float a = Mathf.PI / 2 + Mathf.PI * i / capSeg; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p.Add(new Vector2(-cx, 0) + d * r); nn.Add(d); }
            p.Add(p[0]); nn.Add(nn[0]);
            return new Profile { P = p.ToArray(), N = nn.ToArray(), Closed = true };
        }

        public static Profile For(RunSpec s, float grow = 1f)
        {
            switch (s.Shape)
            {
                case RunShape.Flat: return Stadium(s.Width * grow, s.Height * grow);
                case RunShape.Rect: return Rect(s.Width * grow, s.Height * grow);
                default: return Circle(s.Radius * grow, s.Width > 0.08f ? 24 : 14);
            }
        }

        // ---------------------------------------------------------------- frames
        /// <summary>Frames along a path: side (profile x) and up (profile y), keeping "up" toward world up where possible.</summary>
        static void Frames(IList<Vector3> path, out Vector3[] side, out Vector3[] up)
        {
            int c = path.Count; side = new Vector3[c]; up = new Vector3[c];
            Vector3 prevUp = Vector3.up;
            for (int i = 0; i < c; i++)
            {
                Vector3 t = Tangent(path, i);
                Vector3 u = Vector3.up - t * Vector3.Dot(Vector3.up, t);
                if (u.sqrMagnitude < 0.04f) { u = prevUp - t * Vector3.Dot(prevUp, t); if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(t, Vector3.right); }
                u.Normalize();
                if (i > 0 && Vector3.Dot(u, prevUp) < 0f && Mathf.Abs(t.y) > 0.98f) u = -u;
                up[i] = u; side[i] = Vector3.Cross(u, t).normalized; prevUp = u;
            }
        }

        static Vector3 Tangent(IList<Vector3> p, int i)
        {
            int c = p.Count;
            Vector3 a = i > 0 ? (p[i] - p[i - 1]).normalized : Vector3.zero;
            Vector3 b = i < c - 1 ? (p[i + 1] - p[i]).normalized : Vector3.zero;
            Vector3 t = a + b;
            if (t.sqrMagnitude < 1e-8f) t = b.sqrMagnitude > 0 ? b : a;
            return t.sqrMagnitude > 0 ? t.normalized : Vector3.forward;
        }

        // ---------------------------------------------------------------- sweep
        /// <summary>Sweeps a profile along a path (smooth path: pass pre-rounded points). Caps optional.</summary>
        public void Sweep(IList<Vector3> path, Profile prof, int sub, bool capStart = true, bool capEnd = true)
        {
            int c = path.Count; if (c < 2) return;
            Frames(path, out var side, out var up);
            // miter scale at corners so the profile keeps thickness
            int pc = prof.P.Length;
            float perim = 0f; var vacc = new float[pc];
            for (int j = 1; j < pc; j++) { perim += (prof.P[j] - prof.P[j - 1]).magnitude; vacc[j] = perim; }
            bool strips = !prof.Closed; // rect: pairs of verts form separate flat strips
            float ulen = 0f;
            int baseIdx = v.Count;
            for (int i = 0; i < c; i++)
            {
                if (i > 0) ulen += (path[i] - path[i - 1]).magnitude;
                float miter = 1f;
                if (i > 0 && i < c - 1)
                {
                    float cos = Vector3.Dot((path[i] - path[i - 1]).normalized, (path[i + 1] - path[i]).normalized);
                    miter = 1f / Mathf.Max(0.5f, Mathf.Sqrt((1f + Mathf.Clamp(cos, -1f, 1f)) * 0.5f));
                }
                Vector3 t = Tangent(path, i);
                for (int j = 0; j < pc; j++)
                {
                    Vector2 q = prof.P[j];
                    Vector3 off = side[i] * q.x + up[i] * q.y;
                    // stretch only the component along the bend direction
                    if (miter > 1.0001f)
                    {
                        Vector3 bendDir = ((path[i + 1] - path[i]).normalized - (path[i] - path[i - 1]).normalized);
                        bendDir -= t * Vector3.Dot(bendDir, t);
                        if (bendDir.sqrMagnitude > 1e-8f) { bendDir.Normalize(); off += bendDir * Vector3.Dot(off, bendDir) * (miter - 1f); }
                    }
                    Vector3 nn = side[i] * prof.N[j].x + up[i] * prof.N[j].y;
                    v.Add(path[i] + off - Origin); n.Add(nn.normalized); uv.Add(new Vector2(ulen, vacc[j]));
                }
            }
            var tri = Sub(sub);
            for (int i = 0; i < c - 1; i++)
            {
                int r0 = baseIdx + i * pc, r1 = r0 + pc;
                for (int j = 0; j < pc - 1; j++)
                {
                    if (strips && (j % 2 == 1)) continue;
                    int a = r0 + j, b = r0 + j + 1, d = r1 + j, e = r1 + j + 1;
                    AddQuadOriented(a, b, e, d, tri);
                }
            }
            if (capStart) Cap(path[0], -Tangent(path, 0), side[0], up[0], prof, sub);
            if (capEnd) Cap(path[c - 1], Tangent(path, c - 1), side[c - 1], up[c - 1], prof, sub);
        }

        void AddQuadOriented(int a, int b, int c, int d, List<int> tri)
        {
            Vector3 g = Vector3.Cross(v[c] - v[a], v[d] - v[b]);
            Vector3 ns = n[a] + n[b] + n[c] + n[d];
            if (Vector3.Dot(g, ns) >= 0f) { tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(a); tri.Add(c); tri.Add(d); }
            else { tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(a); tri.Add(d); tri.Add(c); }
        }

        void Cap(Vector3 center, Vector3 normal, Vector3 side, Vector3 up, Profile prof, int sub)
        {
            int b = v.Count;
            v.Add(center - Origin); n.Add(normal); uv.Add(Vector2.zero);
            for (int j = 0; j < prof.P.Length; j++)
            {
                Vector2 q = prof.P[j];
                v.Add(center + side * q.x + up * q.y - Origin); n.Add(normal); uv.Add(q);
            }
            var tri = Sub(sub);
            for (int j = 0; j < prof.P.Length - 1; j++)
            {
                int a = b + 1 + j, c = b + 2 + j;
                Vector3 g = Vector3.Cross(v[a] - v[b], v[c] - v[b]);
                if (Vector3.Dot(g, normal) >= 0) { tri.Add(b); tri.Add(a); tri.Add(c); } else { tri.Add(b); tri.Add(c); tri.Add(a); }
            }
            if (!prof.Closed)
            {
                int a = b + prof.P.Length, c = b + 1; // close rect cap fan
                Vector3 g = Vector3.Cross(v[a] - v[b], v[c] - v[b]);
                if (Vector3.Dot(g, normal) >= 0) { tri.Add(b); tri.Add(a); tri.Add(c); } else { tri.Add(b); tri.Add(c); tri.Add(a); }
            }
        }

        /// <summary>Straight cylinder/band from a to b (fitting hubs, crimp rings, couplings).</summary>
        public void Band(Vector3 a, Vector3 b, Profile prof, int sub, bool caps = true) => Sweep(new[] { a, b }, prof, sub, caps, caps);

        public void Box(Vector3 center, Vector3 size, Quaternion rot, int sub)
        {
            Vector3 f = rot * Vector3.forward * size.z * 0.5f;
            var prof = Rect(size.x, size.y);
            // orient the rect profile to rot: build path along rot.forward, frames use world up so only valid when rot is yaw-only or forward is vertical.
            Sweep(new[] { center - f, center + f }, prof, sub);
        }

        /// <summary>Rounds the corners of a polyline with arcs of the given radius (quadratic bezier, steps per corner).</summary>
        public static List<Vector3> RoundCorners(IList<Vector3> pts, float radius, int steps = 6)
        {
            var o = new List<Vector3>();
            if (pts.Count < 3 || radius <= 0f) { o.AddRange(pts); return o; }
            o.Add(pts[0]);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i - 1], p = pts[i], b = pts[i + 1];
                Vector3 da = (a - p), db = (b - p);
                float la = da.magnitude, lb = db.magnitude;
                if (la < 1e-5f || lb < 1e-5f) continue;
                da /= la; db /= lb;
                float ang = Vector3.Angle(-da, db) * Mathf.Deg2Rad;
                if (ang < 0.01f) { o.Add(p); continue; }
                float cut = Mathf.Min(radius * Mathf.Tan(ang * 0.5f), la * 0.45f, lb * 0.45f);
                Vector3 s = p + da * cut, e = p + db * cut;
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    o.Add((1 - t) * (1 - t) * s + 2 * (1 - t) * t * p + t * t * e);
                }
            }
            o.Add(pts[pts.Count - 1]);
            return o;
        }

        /// <summary>Points of a circular arc elbow from port A (center + dirA*leg) bending to dirB, used by fittings.</summary>
        public static List<Vector3> ElbowPath(Vector3 center, Vector3 dirA, Vector3 dirB, float leg, int steps = 8)
        {
            var pts = new List<Vector3> { center + dirA * leg, center, center + dirB * leg };
            var r = RoundCorners(pts, leg * 0.9f, steps);
            return r;
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = Mathf.Max(1, subs.Count);
            for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i, false);
            m.RecalculateBounds();
            if (v.Count > 0) m.RecalculateTangents();
            return m;
        }

        public int SubmeshCount => subs.Count;

        /// <summary>Creates a child GameObject with this mesh under parent at Origin.</summary>
        public GameObject Spawn(Transform parent, string name, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = Origin; go.transform.rotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = Build(name);
            var mr = go.AddComponent<MeshRenderer>();
            var arr = new Material[Mathf.Max(1, subs.Count)];
            for (int i = 0; i < arr.Length; i++) arr[i] = mats[Mathf.Min(i, mats.Length - 1)];
            mr.sharedMaterials = arr;
            return go;
        }
    }

    /// <summary>Material helpers for MEP visuals (tinted shared Materials; no new SurfaceKinds needed).</summary>
    public static class MepLook
    {
        public static readonly Color PexRed = new Color(0.86f, 0.16f, 0.13f);
        public static readonly Color PexBlue = new Color(0.16f, 0.36f, 0.86f);
        public static readonly Color PexWhite = new Color(1f, 1f, 0.98f);
        public static readonly Color Copper = new Color(0.95f, 0.62f, 0.45f);
        public static readonly Color PvcWhite = new Color(1.05f, 1.05f, 1.06f);
        public static readonly Color RomexYellow = new Color(1.15f, 0.95f, 0.30f);
        public static readonly Color RomexWhite = new Color(1.06f, 1.06f, 1.04f);
        public static readonly Color RomexOrange = new Color(1.2f, 0.55f, 0.18f);
        public static readonly Color RomexBlack = new Color(0.18f, 0.18f, 0.19f);
        public static readonly Color BoxBlue = new Color(0.25f, 0.42f, 0.95f);
        public static readonly Color PrimerPurple = new Color(0.55f, 0.25f, 0.75f);

        public static Material Of(RunSpec s) => Materials.Get(s.Surface, s.Tint);
        public static Material Get(SurfaceKind k, Color tint) => Materials.Get(k, tint);
        public static Material Get(SurfaceKind k) => Materials.Get(k);

        static Material _marker;
        public static Material Marker
        {
            get
            {
                if (_marker != null) return _marker;
                _marker = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.05f, 0.05f) };
                _marker.EnableKeyword("_EMISSION");
                _marker.SetColor("_EmissionColor", new Color(0.8f, 0f, 0f));
                return _marker;
            }
        }
    }
}
