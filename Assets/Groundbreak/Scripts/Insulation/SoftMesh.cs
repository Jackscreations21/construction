using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>
    /// Smooth-shaded mesh helper for soft finish goods (batts, foam, loose fill, kraft paper): heightfield grids with
    /// holes, skirts (side walls) around their outline, smooth normals. Geometry is in a FramePlane's local frame
    /// (x = U, y = V, z = depth into framing; proud = -z). UV0 = meters (x, y) for front faces and (along, z) for skirts.
    /// </summary>
    public class SoftMesh
    {
        public readonly List<Vector3> V = new List<Vector3>(4096);
        public readonly List<Vector3> N = new List<Vector3>(4096);
        public readonly List<Vector2> UV = new List<Vector2>(4096);
        public readonly List<Vector2> UV2 = new List<Vector2>(4096);
        readonly List<List<int>> _subs = new List<List<int>>();
        public int MinSubmeshes;

        List<int> Sub(int i) { while (_subs.Count <= i) _subs.Add(new List<int>()); return _subs[i]; }

        public int Add(Vector3 p, Vector3 n, Vector2 uv) { V.Add(p); N.Add(n); UV.Add(uv); UV2.Add(uv); return V.Count - 1; }

        /// <summary>Triangle with winding chosen so the face points along <paramref name="facing"/>.</summary>
        public void Tri(int a, int b, int c, int sub, Vector3 facing)
        {
            var g = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            var t = Sub(sub);
            if (Vector3.Dot(g, facing) >= 0f) { t.Add(a); t.Add(b); t.Add(c); } else { t.Add(a); t.Add(c); t.Add(b); }
        }

        public void Quad(int a, int b, int c, int d, int sub, Vector3 facing) { Tri(a, b, c, sub, facing); Tri(a, c, d, sub, facing); }

        /// <summary>Flat quad with its own vertices (meter UVs projected on the given axes).</summary>
        public void FlatQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int sub, Vector3 facing, Vector3 uAxis, Vector3 vAxis)
        {
            var n = Vector3.Cross(c - a, d - b).normalized; if (Vector3.Dot(n, facing) < 0) n = -n;
            int i0 = Add(a, n, new Vector2(Vector3.Dot(a, uAxis), Vector3.Dot(a, vAxis)));
            int i1 = Add(b, n, new Vector2(Vector3.Dot(b, uAxis), Vector3.Dot(b, vAxis)));
            int i2 = Add(c, n, new Vector2(Vector3.Dot(c, uAxis), Vector3.Dot(c, vAxis)));
            int i3 = Add(d, n, new Vector2(Vector3.Dot(d, uAxis), Vector3.Dot(d, vAxis)));
            Quad(i0, i1, i2, i3, sub, facing);
        }

        /// <summary>
        /// Heightfield over <paramref name="outer"/> minus <paramref name="holes"/>; z = <paramref name="z"/>(x,y).
        /// Grid lines at <paramref name="step"/> plus every hole edge. Front faces toward local -z (the room).
        /// Returns the grid so callers can add skirts. Smooth normals from the heightfield.
        /// </summary>
        public Grid Heightfield(Rect outer, IList<Rect> holes, float step, Func<float, float, float> z, int sub, bool facingRoom = true)
        {
            var xs = Lines(outer.xMin, outer.xMax, step, holes, true);
            var ys = Lines(outer.yMin, outer.yMax, step, holes, false);
            var g = new Grid { Xs = xs, Ys = ys, Idx = new int[xs.Count, ys.Count], Solid = new bool[xs.Count - 1, ys.Count - 1], Z = z, Outer = outer };
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < ys.Count; j++)
                {
                    var c = new Vector2((xs[i] + xs[i + 1]) * 0.5f, (ys[j] + ys[j + 1]) * 0.5f);
                    bool solid = true;
                    if (holes != null) foreach (var h in holes) if (h.Contains(c)) { solid = false; break; }
                    g.Solid[i, j] = solid;
                }
            Vector3 face = facingRoom ? Vector3.back : Vector3.forward;
            float e = Mathf.Max(0.002f, step * 0.25f);
            for (int i = 0; i < xs.Count; i++)
                for (int j = 0; j < ys.Count; j++)
                {
                    g.Idx[i, j] = -1;
                    if (!g.Touches(i, j)) continue;
                    float x = xs[i], y = ys[j];
                    float h0 = z(x, y);
                    float dx = (z(x + e, y) - z(x - e, y)) / (2f * e), dy = (z(x, y + e) - z(x, y - e)) / (2f * e);
                    Vector3 n = new Vector3(dx, dy, -1f).normalized; // surface z = f(x,y), room side is -z
                    if (!facingRoom) n = -n;
                    g.Idx[i, j] = Add(new Vector3(x, y, h0), n, new Vector2(x, y));
                }
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < ys.Count; j++)
                    if (g.Solid[i, j]) Quad(g.Idx[i, j], g.Idx[i + 1, j], g.Idx[i + 1, j + 1], g.Idx[i, j + 1], sub, face);
            return g;
        }

        static List<float> Lines(float a, float b, float step, IList<Rect> holes, bool xAxis)
        {
            var l = new List<float>();
            int n = Mathf.Max(1, Mathf.CeilToInt((b - a) / step));
            for (int i = 0; i <= n; i++) l.Add(Mathf.Lerp(a, b, i / (float)n));
            if (holes != null)
                foreach (var h in holes)
                {
                    float h0 = xAxis ? h.xMin : h.yMin, h1 = xAxis ? h.xMax : h.yMax;
                    if (h0 > a && h0 < b) l.Add(h0);
                    if (h1 > a && h1 < b) l.Add(h1);
                }
            l.Sort();
            var res = new List<float>();
            foreach (var v in l) if (res.Count == 0 || v - res[res.Count - 1] > 0.002f) res.Add(v);
            if (res[res.Count - 1] < b - 1e-5f) res[res.Count - 1] = b;
            return res;
        }

        public class Grid
        {
            public List<float> Xs, Ys;
            public int[,] Idx;
            public bool[,] Solid;
            public Func<float, float, float> Z;
            public Rect Outer;
            public bool IsSolid(int i, int j) => i >= 0 && j >= 0 && i < Xs.Count - 1 && j < Ys.Count - 1 && Solid[i, j];
            public bool Touches(int i, int j) => IsSolid(i - 1, j - 1) || IsSolid(i, j - 1) || IsSolid(i - 1, j) || IsSolid(i, j);
        }

        /// <summary>Side walls everywhere a solid grid cell borders empty space (outline and holes): from the
        /// heightfield edge down to z = <paramref name="zBack"/>(x,y). Slight outward bulge for a pillowy edge.</summary>
        public void Skirts(Grid g, Func<float, float, float> zBack, int sub, float bulge = 0f)
        {
            int nx = g.Xs.Count - 1, ny = g.Ys.Count - 1;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    if (!g.Solid[i, j]) continue;
                    if (!g.IsSolid(i - 1, j)) Skirt(g, g.Xs[i], g.Ys[j], g.Xs[i], g.Ys[j + 1], Vector3.left, zBack, sub, bulge);
                    if (!g.IsSolid(i + 1, j)) Skirt(g, g.Xs[i + 1], g.Ys[j], g.Xs[i + 1], g.Ys[j + 1], Vector3.right, zBack, sub, bulge);
                    if (!g.IsSolid(i, j - 1)) Skirt(g, g.Xs[i], g.Ys[j], g.Xs[i + 1], g.Ys[j], Vector3.down, zBack, sub, bulge);
                    if (!g.IsSolid(i, j + 1)) Skirt(g, g.Xs[i], g.Ys[j + 1], g.Xs[i + 1], g.Ys[j + 1], Vector3.up, zBack, sub, bulge);
                }
        }

        void Skirt(Grid g, float x0, float y0, float x1, float y1, Vector3 outward, Func<float, float, float> zBack, int sub, float bulge)
        {
            // 3 rows: front edge, mid (bulged), back
            float za = g.Z(x0, y0), zb = g.Z(x1, y1);
            float ba = zBack(x0, y0), bb = zBack(x1, y1);
            bool alongX = Mathf.Abs(y1 - y0) < 1e-6f;
            Vector2 U(float x, float y, float zz) => new Vector2(alongX ? x : y, zz);
            Vector3 P(float x, float y, float zz, float push) => new Vector3(x, y, zz) + outward * push;
            Vector3 fa = P(x0, y0, za, 0), fb = P(x1, y1, zb, 0);
            Vector3 ma = P(x0, y0, Mathf.Lerp(za, ba, 0.4f), bulge), mb = P(x1, y1, Mathf.Lerp(zb, bb, 0.4f), bulge);
            Vector3 ka = P(x0, y0, ba, 0), kb = P(x1, y1, bb, 0);
            Vector3 n1 = (outward - Vector3.forward * 0.6f).normalized, n2 = outward, n3 = (outward + Vector3.forward * 0.3f).normalized;
            int a0 = Add(fa, n1, U(x0, y0, za)), b0 = Add(fb, n1, U(x1, y1, zb));
            int a1 = Add(ma, n2, U(x0, y0, ma.z)), b1 = Add(mb, n2, U(x1, y1, mb.z));
            int a2 = Add(ka, n3, U(x0, y0, ba)), b2 = Add(kb, n3, U(x1, y1, bb));
            Quad(a0, b0, b1, a1, sub, outward);
            Quad(a1, b1, b2, a2, sub, outward);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetUVs(1, UV2);
            int count = Mathf.Max(1, Mathf.Max(_subs.Count, MinSubmeshes));
            while (_subs.Count < count) _subs.Add(new List<int>());
            m.subMeshCount = count;
            for (int i = 0; i < count; i++) m.SetTriangles(_subs[i], i, false);
            m.RecalculateBounds();
            if (V.Count > 0) m.RecalculateTangents();
            return m;
        }
    }
}
