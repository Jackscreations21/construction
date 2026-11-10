using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>
    /// Extrudes a molding profile along a straight run with real end cuts. Each profile point (x proud, y across) ends at
    /// t = -startExt(p) and t = length + endExt(p) along the run, so miters (ext = ±x), casing miters (ext = y) and copes
    /// (ext = -front of the mating profile at y) are exact. Curved profile sections are smooth-shaded; UVs are meters with
    /// U along the run (wood grain) and V around the profile.
    /// </summary>
    public sealed class TrimMeshBuilder
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int> _t = new List<int>();
        public float SmoothAngle = 32f;

        public void AddRun(Vector2[] poly, Vector3 origin, Vector3 d, Vector3 nOut, Vector3 a, float length,
                           Func<Vector2, float> startExt, Func<Vector2, float> endExt, float uOffset = 0f)
        {
            int n = poly.Length;
            if (n < 3) return;
            float area = 0f;
            for (int i = 0; i < n; i++) { Vector2 p = poly[i], q = poly[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
            float orient = area >= 0f ? 1f : -1f;
            // edge outward normals (2D)
            var en = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 e = poly[(i + 1) % n] - poly[i];
                en[i] = e.sqrMagnitude < 1e-14f ? Vector2.zero : new Vector2(e.y, -e.x).normalized * orient;
            }
            var ts = new float[n]; var te = new float[n];
            for (int i = 0; i < n; i++) { ts[i] = -(startExt?.Invoke(poly[i]) ?? 0f); te[i] = length + (endExt?.Invoke(poly[i]) ?? 0f); }
            Vector3 P(int i, float t) => origin + d * t + nOut * poly[i].x + a * poly[i].y;
            Vector3 N3(Vector2 m) => (nOut * m.x + a * m.y).normalized;

            float s = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (en[i] == Vector2.zero) continue;
                float len = (poly[j] - poly[i]).magnitude;
                Vector2 ni = en[i], nj = en[i];
                int ip = (i - 1 + n) % n;
                if (en[ip] != Vector2.zero && Vector2.Angle(en[ip], en[i]) < SmoothAngle) ni = (en[ip] + en[i]).normalized;
                if (en[j] != Vector2.zero && Vector2.Angle(en[i], en[j]) < SmoothAngle) nj = (en[i] + en[j]).normalized;
                int b = _v.Count;
                Add(P(i, ts[i]), N3(ni), new Vector2(ts[i] + uOffset, s));
                Add(P(j, ts[j]), N3(nj), new Vector2(ts[j] + uOffset, s + len));
                Add(P(j, te[j]), N3(nj), new Vector2(te[j] + uOffset, s + len));
                Add(P(i, te[i]), N3(ni), new Vector2(te[i] + uOffset, s));
                Vector3 face = N3(en[i]);
                Tri(b, b + 1, b + 2, face); Tri(b, b + 2, b + 3, face);
                s += len;
            }
            // caps
            var tris = Triangulate(poly);
            Cap(poly, tris, i => P(i, ts[i]), -d);
            Cap(poly, tris, i => P(i, te[i]), d);
        }

        void Cap(Vector2[] poly, List<int> tris, Func<int, Vector3> pos, Vector3 approxN)
        {
            int b = _v.Count;
            for (int i = 0; i < poly.Length; i++) Add(pos(i), approxN, new Vector2(poly[i].x, poly[i].y));
            for (int k = 0; k + 2 < tris.Count; k += 3) Tri(b + tris[k], b + tris[k + 1], b + tris[k + 2], approxN);
        }

        void Add(Vector3 p, Vector3 n, Vector2 uv) { _v.Add(p); _n.Add(n); _uv.Add(uv); }

        void Tri(int a, int b, int c, Vector3 want)
        {
            Vector3 g = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
            if (Vector3.Dot(g, want) >= 0f) { _t.Add(a); _t.Add(b); _t.Add(c); } else { _t.Add(a); _t.Add(c); _t.Add(b); }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65535) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _uv); m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            if (_v.Count > 0) m.RecalculateTangents();
            return m;
        }

        /// <summary>Ear-clipping triangulation of a simple polygon (either winding). Returns index triples.</summary>
        public static List<int> Triangulate(Vector2[] poly)
        {
            var res = new List<int>();
            int n = poly.Length;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            float area = 0f;
            for (int i = 0; i < n; i++) { Vector2 p = poly[i], q = poly[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
            float sgn = area >= 0f ? 1f : -1f;
            int guard = n * n + 10;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int i0 = idx[(k - 1 + idx.Count) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                    Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
                    float cr = ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * sgn;
                    if (cr <= 1e-12f) continue;
                    bool inside = false;
                    for (int m = 0; m < idx.Count && !inside; m++)
                    {
                        int im = idx[m];
                        if (im == i0 || im == i1 || im == i2) continue;
                        if (InTri(poly[im], a, b, c, sgn)) inside = true;
                    }
                    if (inside) continue;
                    res.Add(i0); res.Add(i1); res.Add(i2);
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // degenerate leftovers: fan the rest
                    for (int k = 1; k + 1 < idx.Count; k++) { res.Add(idx[0]); res.Add(idx[k]); res.Add(idx[k + 1]); }
                    return res;
                }
            }
            if (idx.Count == 3) { res.Add(idx[0]); res.Add(idx[1]); res.Add(idx[2]); }
            return res;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float sgn)
        {
            float c1 = ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) * sgn;
            float c2 = ((c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x)) * sgn;
            float c3 = ((a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x)) * sgn;
            return c1 >= 0f && c2 >= 0f && c3 >= 0f;
        }
    }
}
