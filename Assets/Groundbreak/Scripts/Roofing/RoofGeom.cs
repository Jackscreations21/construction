using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>A 2D half-plane: keeps points p with Dot(p - Point, Normal) &gt;= 0.</summary>
    public struct HalfPlane
    {
        public Vector2 Point, Normal;
        public HalfPlane(Vector2 point, Vector2 normal) { Point = point; Normal = normal; }
        public float Eval(Vector2 p) => Vector2.Dot(p - Point, Normal);
        /// <summary>y &lt;= a + m x  (below a line).</summary>
        public static HalfPlane Below(float a, float m) => new HalfPlane(new Vector2(0f, a), new Vector2(m, -1f));
        /// <summary>y &gt;= a + m x  (above a line).</summary>
        public static HalfPlane Above(float a, float m) => new HalfPlane(new Vector2(0f, a), new Vector2(-m, 1f));
        public static HalfPlane XMin(float x) => new HalfPlane(new Vector2(x, 0f), Vector2.right);
        public static HalfPlane XMax(float x) => new HalfPlane(new Vector2(x, 0f), Vector2.left);
        public static HalfPlane YMin(float y) => new HalfPlane(new Vector2(0f, y), Vector2.up);
        public static HalfPlane YMax(float y) => new HalfPlane(new Vector2(0f, y), Vector2.down);
    }

    /// <summary>Small 2D helpers for cut lumber profiles, clipped sheathing and shingles.</summary>
    public static class Geo2
    {
        public static List<Vector2> Rect(float x0, float y0, float x1, float y1) =>
            new List<Vector2> { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };

        /// <summary>Sutherland-Hodgman clip of a convex polygon by one half-plane.</summary>
        public static List<Vector2> Clip(List<Vector2> poly, HalfPlane h)
        {
            var o = new List<Vector2>(poly.Count + 2);
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                float da = h.Eval(a), db = h.Eval(b);
                if (da >= 0f) o.Add(a);
                if ((da >= 0f) != (db >= 0f))
                {
                    float t = da / (da - db);
                    o.Add(Vector2.Lerp(a, b, t));
                }
            }
            return o;
        }

        public static List<Vector2> Clip(List<Vector2> poly, IList<HalfPlane> hs)
        {
            var p = poly;
            for (int i = 0; i < hs.Count && p.Count >= 3; i++) p = Clip(p, hs[i]);
            return p;
        }

        /// <summary>Clips a segment by half-planes; false if nothing is left.</summary>
        public static bool ClipSegment(ref Vector2 a, ref Vector2 b, IList<HalfPlane> hs)
        {
            float t0 = 0f, t1 = 1f;
            Vector2 d = b - a;
            foreach (var h in hs)
            {
                float fa = h.Eval(a), fd = Vector2.Dot(d, h.Normal);
                if (Mathf.Abs(fd) < 1e-9f) { if (fa < 0f) return false; continue; }
                float t = -fa / fd;
                if (fd > 0f) t0 = Mathf.Max(t0, t); else t1 = Mathf.Min(t1, t);
                if (t0 > t1) return false;
            }
            Vector2 na = a + d * t0, nb = a + d * t1;
            a = na; b = nb;
            return (b - a).sqrMagnitude > 1e-10f;
        }

        public static float Area(List<Vector2> p)
        {
            float s = 0f;
            for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += a.x * b.y - b.x * a.y; }
            return Mathf.Abs(s) * 0.5f;
        }

        public static Vector2 Centroid(IList<Vector2> p)
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < p.Count; i++) c += p[i];
            return p.Count > 0 ? c / p.Count : c;
        }

        /// <summary>Intersection of the lines a + s*da and b + t*db.</summary>
        public static Vector2 Intersect(Vector2 a, Vector2 da, Vector2 b, Vector2 db)
        {
            float den = da.x * db.y - da.y * db.x;
            if (Mathf.Abs(den) < 1e-9f) return a;
            float s = ((b.x - a.x) * db.y - (b.y - a.y) * db.x) / den;
            return a + da * s;
        }
    }

    /// <summary>Builds cut lumber (rafters, chords, webs, studs, panels) as convex prisms with the wood grain running along
    /// the member and end grain on the cut faces. All input is in the roof frame; MeshBuilder.Transform maps it to the piece.</summary>
    public class FrameKit
    {
        public readonly MeshBuilder B;
        readonly Func<SurfaceKind, int> _s;
        public FrameKit(MeshBuilder b, Func<SurfaceKind, int> s) { B = b; _s = s; }
        public int Sub(SurfaceKind k) => _s(k);

        /// <summary>Convex polygon (2D in axes U,V from origin) extruded along 'thick' from t0 to t1 (meters).
        /// Faces whose edge runs across the grain get end grain (cut ends).</summary>
        public void Prism(IList<Vector2> poly, Vector3 origin, Vector3 axisU, Vector3 axisV, Vector3 thick, float t0, float t1,
                          SurfaceKind face, Vector3 grain, SurfaceKind? cut = null, SurfaceKind? edge = null)
        {
            int n = poly.Count;
            if (n < 3) return;
            int sf = _s(face);
            int sc = _s(cut ?? (face == SurfaceKind.PineLumber ? SurfaceKind.PineEndGrain : face));
            int se = _s(edge ?? face);
            Vector2 c2 = Geo2.Centroid(poly);
            Vector3 P(Vector2 p, float t) => origin + axisU * p.x + axisV * p.y + thick * t;
            var top = new List<Vector3>(n); var bot = new List<Vector3>(n);
            for (int i = 0; i < n; i++) { top.Add(P(poly[i], t1)); bot.Add(P(poly[i], t0)); }
            Vector3 tn = thick.normalized;
            B.AddPolygon(top, sf, t1 >= t0 ? tn : -tn, grain);
            B.AddPolygon(bot, sf, t1 >= t0 ? -tn : tn, grain);
            Vector3 g = grain.normalized;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                Vector2 e = b - a;
                if (e.sqrMagnitude < 1e-12f) continue;
                Vector2 n2 = new Vector2(e.y, -e.x);
                if (Vector2.Dot(n2, (a + b) * 0.5f - c2) < 0f) n2 = -n2;
                Vector3 nh = axisU * n2.x + axisV * n2.y;
                Vector3 ed = (axisU * e.x + axisV * e.y).normalized;
                bool across = Mathf.Abs(Vector3.Dot(ed, g)) < 0.5f;
                B.AddQuad(P(a, t0), P(b, t0), P(b, t1), P(a, t1), across ? sc : se, nh, across ? tn : g);
            }
        }

        /// <summary>Thin galvanized plate (gusset, tie, clip) as a box.</summary>
        public void Plate(Vector3 center, Vector3 size, Quaternion rot)
            => B.AddBox(center, size, rot, _s(SurfaceKind.Galvanized));
    }

    /// <summary>Mesh builder for roofing layers with explicit UVs (meters) and arbitrary materials; 32-bit indices when large.</summary>
    public class RoofMesh
    {
        public readonly List<Vector3> V = new List<Vector3>(4096);
        public readonly List<Vector3> N = new List<Vector3>(4096);
        public readonly List<Vector2> UV = new List<Vector2>(4096);
        readonly List<List<int>> _subs = new List<List<int>>();
        readonly List<Material> _mats = new List<Material>();

        public int Mat(Material m)
        {
            int i = _mats.IndexOf(m);
            if (i < 0) { i = _mats.Count; _mats.Add(m); _subs.Add(new List<int>()); }
            return i;
        }

        public bool Empty => V.Count == 0;

        /// <summary>Convex polygon, fan-triangulated, facing 'hint'. Flat normal computed from the points.</summary>
        public void Poly(IList<Vector3> p, IList<Vector2> uv, Vector3 hint, int sub)
        {
            int n = p.Count;
            if (n < 3) return;
            Vector3 nn = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = p[i], b = p[(i + 1) % n];
                nn.x += (a.y - b.y) * (a.z + b.z);
                nn.y += (a.z - b.z) * (a.x + b.x);
                nn.z += (a.x - b.x) * (a.y + b.y);
            }
            if (nn.sqrMagnitude < 1e-16f) return;
            nn.Normalize();
            if (Vector3.Dot(nn, hint) < 0f) nn = -nn;
            int b0 = V.Count;
            for (int i = 0; i < n; i++) { V.Add(p[i]); N.Add(nn); UV.Add(uv[i]); }
            var t = _subs[sub];
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 g = Vector3.Cross(p[i] - p[0], p[i + 1] - p[0]);
                if (Vector3.Dot(g, nn) >= 0f) { t.Add(b0); t.Add(b0 + i); t.Add(b0 + i + 1); }
                else { t.Add(b0); t.Add(b0 + i + 1); t.Add(b0 + i); }
            }
        }

        static readonly Vector3[] _q = new Vector3[4];
        static readonly Vector2[] _qu = new Vector2[4];
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 hint, int sub)
        {
            _q[0] = a; _q[1] = b; _q[2] = c; _q[3] = d; _qu[0] = ua; _qu[1] = ub; _qu[2] = uc; _qu[3] = ud;
            Poly(_q, _qu, hint, sub);
        }

        /// <summary>Two-sided quad (sheet metal, membranes seen from both sides).</summary>
        public void Quad2(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 hint, int sub)
        {
            Quad(a, b, c, d, ua, ub, uc, ud, hint, sub);
            Quad(a, b, c, d, ua, ub, uc, ud, -hint, sub);
        }

        /// <summary>Box with meter UVs (U along the box's local X, or Z if longer).</summary>
        public void Box(Vector3 c, Vector3 size, Quaternion rot, int sub)
        {
            Vector3 h = size * 0.5f;
            Vector3 X = rot * Vector3.right, Y = rot * Vector3.up, Z = rot * Vector3.forward;
            void F(Vector3 n, Vector3 a, Vector3 b, float ha, float hb, float hn)
            {
                Vector3 o = c + n * hn;
                Vector3 p0 = o - a * ha - b * hb, p1 = o + a * ha - b * hb, p2 = o + a * ha + b * hb, p3 = o - a * ha + b * hb;
                Quad(p0, p1, p2, p3, new Vector2(-ha, -hb), new Vector2(ha, -hb), new Vector2(ha, hb), new Vector2(-ha, hb), n, sub);
            }
            F(Y, Z, X, h.z, h.x, h.y); F(-Y, Z, X, h.z, h.x, h.y);
            F(X, Z, Y, h.z, h.y, h.x); F(-X, Z, Y, h.z, h.y, h.x);
            F(Z, X, Y, h.x, h.y, h.z); F(-Z, X, Y, h.x, h.y, h.z);
        }

        /// <summary>Creates the renderer object (world-space vertices are converted into the parent's space).</summary>
        public GameObject Attach(Transform parent, string name, bool castShadows = true)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name };
            if (V.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            if (parent != null && (parent.position != Vector3.zero || parent.rotation != Quaternion.identity || parent.lossyScale != Vector3.one))
            {
                var inv = parent.worldToLocalMatrix;
                var lv = new List<Vector3>(V.Count); var ln = new List<Vector3>(N.Count);
                for (int i = 0; i < V.Count; i++) { lv.Add(inv.MultiplyPoint3x4(V[i])); ln.Add(inv.MultiplyVector(N[i]).normalized); }
                mesh.SetVertices(lv); mesh.SetNormals(ln);
            }
            else { mesh.SetVertices(V); mesh.SetNormals(N); }
            mesh.SetUVs(0, UV);
            mesh.subMeshCount = Mathf.Max(1, _subs.Count);
            for (int i = 0; i < _subs.Count; i++) mesh.SetTriangles(_subs[i], i, false);
            mesh.RecalculateBounds();
            if (V.Count > 0) mesh.RecalculateTangents();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = _mats.Count > 0 ? _mats.ToArray() : new[] { Materials.Get(SurfaceKind.PaintedWhite) };
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }

    /// <summary>Stock helpers for the roof takeoff.</summary>
    public static class RoofStock
    {
        /// <summary>Shortest stock of a size ("2x4", "2x6", "1x6"...) covering len, plus how many pieces (spliced when longer than any stock).</summary>
        public static (string id, int count) Pick(string size, float len)
        {
            string best = null; float bestLen = float.MaxValue, longest = 0f; string longestId = null;
            foreach (var d in Catalog.All)
            {
                if (d.Category != PieceCategory.Lumber || d.Id == null || !d.Id.StartsWith(size + "x")) continue;
                if (d.Size.z > longest) { longest = d.Size.z; longestId = d.Id; }
                if (d.Size.z + 0.0005f >= len && d.Size.z < bestLen) { best = d.Id; bestLen = d.Size.z; }
            }
            if (best != null) return (best, 1);
            if (longestId == null) return (size + "x16", Mathf.Max(1, Mathf.CeilToInt(len / (16 * Units.Foot))));
            return (longestId, Mathf.CeilToInt(len / longest - 0.001f));
        }

        /// <summary>Lumber size id one step deeper (2x6 -> 2x8 ...).</summary>
        public static string Deeper(string size)
        {
            switch (size) { case "2x4": return "2x6"; case "2x6": return "2x8"; case "2x8": return "2x10"; case "2x10": return "2x12"; default: return "2x12"; }
        }

        public static float Depth(string size)
        {
            switch (size) { case "2x4": return Units.In(3.5f); case "2x6": return Units.In(5.5f); case "2x8": return Units.In(7.25f); case "2x10": return Units.In(9.25f); default: return Units.In(11.25f); }
        }
    }

    /// <summary>Deterministic small RNG for repeatable shingle/tab variation.</summary>
    public struct RRand
    {
        uint _s;
        public RRand(int seed) { _s = (uint)seed * 747796405u + 2891336453u; if (_s == 0) _s = 1; }
        public float Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s & 0xFFFFFF) / (float)0x1000000; }
        public float Range(float a, float b) => a + (b - a) * Next();
        public static int Seed(int a, int b, int c) => a * 73856093 ^ b * 19349663 ^ c * 83492791;
    }
}
