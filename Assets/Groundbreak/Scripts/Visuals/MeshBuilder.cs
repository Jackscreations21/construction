using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>
    /// Small procedural mesh builder. All geometry is authored at true size in meters.
    /// - Hard edges: every flat face gets its own vertices (split normals).
    /// - UVs are world-size meters (planar projection per face), so tiling materials keep a constant texel density.
    /// - Winding is resolved automatically from the intended normal (Unity front face = clockwise, normal = Cross(b-a, c-a)).
    /// - Optional <see cref="Transform"/> (TRS) is applied to everything added while it is set; UVs are computed before it.
    /// - Submeshes are plain indices; the caller supplies a matching material array.
    /// </summary>
    public class MeshBuilder
    {
        public const int FacePosX = 1, FaceNegX = 2, FacePosY = 4, FaceNegY = 8, FacePosZ = 16, FaceNegZ = 32, AllFaces = 63;

        readonly List<Vector3> verts = new List<Vector3>(2048);
        readonly List<Vector3> norms = new List<Vector3>(2048);
        readonly List<Vector2> uvs = new List<Vector2>(2048);
        readonly List<List<int>> subs = new List<List<int>>();

        Matrix4x4 xf = Matrix4x4.identity, nxf = Matrix4x4.identity;
        bool hasXf;

        /// <summary>Submesh count is at least this (so it can match a material array even if a submesh ended up empty).</summary>
        public int MinSubmeshCount;

        /// <summary>Added to every UV (meters). Used to de-correlate identical boards/sheets/bricks.</summary>
        public Vector2 UvOffset;

        public int VertexCount => verts.Count;
        public int SubmeshCount => subs.Count;

        /// <summary>Placement applied to all geometry added afterwards. May contain non-uniform scale (normals use inverse-transpose).</summary>
        public Matrix4x4 Transform
        {
            get => xf;
            set
            {
                xf = value;
                hasXf = value != Matrix4x4.identity;
                nxf = hasXf ? value.inverse.transpose : Matrix4x4.identity;
            }
        }

        public void ResetTransform() => Transform = Matrix4x4.identity;

        List<int> Sub(int i)
        {
            if (i < 0) i = 0;
            while (subs.Count <= i) subs.Add(new List<int>());
            return subs[i];
        }

        int V(Vector3 p, Vector3 n, Vector2 uv)
        {
            if (hasXf)
            {
                p = xf.MultiplyPoint3x4(p);
                n = nxf.MultiplyVector(n);
            }
            if (n.sqrMagnitude > 1e-12f) n.Normalize(); else n = Vector3.up;
            verts.Add(p); norms.Add(n); uvs.Add(uv + UvOffset);
            return verts.Count - 1;
        }

        /// <summary>Quad from 4 existing vertices (in order around the quad); winding chosen to agree with the vertex normals.</summary>
        void QuadIdx(int a, int b, int c, int d, int sub)
        {
            Vector3 g = Vector3.Cross(verts[c] - verts[a], verts[d] - verts[b]);
            Vector3 ns = norms[a] + norms[b] + norms[c] + norms[d];
            var t = Sub(sub);
            if (Vector3.Dot(g, ns) >= 0f) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
            else { t.Add(a); t.Add(c); t.Add(b); t.Add(a); t.Add(d); t.Add(c); }
        }

        void TriIdx(int a, int b, int c, int sub)
        {
            Vector3 g = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            Vector3 ns = norms[a] + norms[b] + norms[c];
            var t = Sub(sub);
            if (Vector3.Dot(g, ns) >= 0f) { t.Add(a); t.Add(b); t.Add(c); }
            else { t.Add(a); t.Add(c); t.Add(b); }
        }

        /// <summary>Picks the planar UV axes (meters) for a face with normal n. hint = preferred U direction (e.g. lumber length).</summary>
        static void FaceAxes(Vector3 n, Vector3 hint, out Vector3 u, out Vector3 v)
        {
            if (hint.sqrMagnitude > 1e-8f)
            {
                hint.Normalize();
                float d = Vector3.Dot(hint, n);
                if (Mathf.Abs(d) < 0.9f)
                {
                    u = (hint - n * d).normalized;
                    v = Vector3.Cross(n, u);
                    return;
                }
            }
            if (Mathf.Abs(n.y) > 0.7f)
            {
                Vector3 r = Vector3.right - n * n.x;
                if (r.sqrMagnitude < 1e-6f) r = Vector3.forward;
                u = r.normalized;
            }
            else
            {
                u = Vector3.Cross(Vector3.up, n).normalized;
            }
            v = Vector3.Cross(n, u);
        }

        static Vector2 Proj(Vector3 p, Vector3 u, Vector3 v) => new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v));

        // ------------------------------------------------------------------ primitives

        /// <summary>Flat quad (a,b,c,d in order around it). Facing follows normalHint (or the given order if hint is zero). Meter UVs.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int sub, Vector3 normalHint, Vector3 uHint = default)
        {
            Vector3 n = Vector3.Cross(c - a, d - b);
            if (n.sqrMagnitude < 1e-14f)
            {
                if (normalHint.sqrMagnitude < 1e-12f) return;
                n = normalHint;
            }
            n.Normalize();
            if (normalHint.sqrMagnitude > 1e-12f && Vector3.Dot(n, normalHint) < 0f) n = -n;
            FaceAxes(n, uHint, out Vector3 u, out Vector3 v);
            int i0 = V(a, n, Proj(a, u, v));
            int i1 = V(b, n, Proj(b, u, v));
            int i2 = V(c, n, Proj(c, u, v));
            int i3 = V(d, n, Proj(d, u, v));
            QuadIdx(i0, i1, i2, i3, sub);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, int sub, Vector3 normalHint, Vector3 uHint = default)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) return;
            n.Normalize();
            if (normalHint.sqrMagnitude > 1e-12f && Vector3.Dot(n, normalHint) < 0f) n = -n;
            FaceAxes(n, uHint, out Vector3 u, out Vector3 v);
            int i0 = V(a, n, Proj(a, u, v));
            int i1 = V(b, n, Proj(b, u, v));
            int i2 = V(c, n, Proj(c, u, v));
            TriIdx(i0, i1, i2, sub);
        }

        /// <summary>Convex planar polygon (fan triangulated), facing normalHint.</summary>
        public void AddPolygon(IList<Vector3> pts, int sub, Vector3 normalHint, Vector3 uHint = default)
        {
            int n = pts.Count;
            if (n < 3) return;
            Vector3 nn = Vector3.zero;
            for (int i = 0; i < n; i++) nn += Vector3.Cross(pts[i], pts[(i + 1) % n]);
            if (nn.sqrMagnitude < 1e-14f) nn = normalHint;
            if (nn.sqrMagnitude < 1e-14f) return;
            nn.Normalize();
            if (normalHint.sqrMagnitude > 1e-12f && Vector3.Dot(nn, normalHint) < 0f) nn = -nn;
            FaceAxes(nn, uHint, out Vector3 u, out Vector3 v);
            int b = verts.Count;
            for (int i = 0; i < n; i++) V(pts[i], nn, Proj(pts[i], u, v));
            for (int i = 1; i < n - 1; i++) TriIdx(b, b + i, b + i + 1, sub);
        }

        static readonly Vector3[] FaceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly Vector3[] FaceA = { Vector3.up, Vector3.up, Vector3.right, Vector3.right, Vector3.right, Vector3.right };
        static readonly Vector3[] FaceB = { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.up, Vector3.up };

        public void AddBox(Vector3 center, Vector3 size, int submesh) => AddBox(center, size, Quaternion.identity, submesh);

        /// <summary>Box with per-face meter UVs. uAxis (box-local) = preferred U direction, e.g. Vector3.forward for lumber grain
        /// along the length. faceMask = FacePosX|... ; inward flips normals (for wells / interiors).</summary>
        public void AddBox(Vector3 center, Vector3 size, Quaternion rot, int submesh, Vector3 uAxis = default, int faceMask = AllFaces, bool inward = false)
        {
            Vector3 h = size * 0.5f;
            Vector3 wu = rot * uAxis;
            for (int f = 0; f < 6; f++)
            {
                if ((faceMask & (1 << f)) == 0) continue;
                Vector3 cN = Vector3.Scale(FaceN[f], h), cA = Vector3.Scale(FaceA[f], h), cB = Vector3.Scale(FaceB[f], h);
                Vector3 p0 = center + rot * (cN - cA - cB);
                Vector3 p1 = center + rot * (cN - cA + cB);
                Vector3 p2 = center + rot * (cN + cA + cB);
                Vector3 p3 = center + rot * (cN + cA - cB);
                Vector3 wn = rot * FaceN[f];
                if (inward) wn = -wn;
                AddQuad(p0, p1, p2, p3, submesh, wn, wu);
            }
        }

        /// <summary>Prism: convex polygon in local XY extruded along local Z (centered, total = length).
        /// Side UVs run U along the extrusion axis (grain), caps are planar.</summary>
        public void AddExtrude(IList<Vector2> poly, Vector3 center, Quaternion rot, float length, int sideSub, int capSub = -1, bool caps = true)
        {
            int n = poly.Count;
            if (n < 3) return;
            if (capSub < 0) capSub = sideSub;
            float hz = length * 0.5f;
            Vector2 c2 = Vector2.zero;
            for (int i = 0; i < n; i++) c2 += poly[i];
            c2 /= n;
            Vector3 axis = rot * Vector3.forward;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % n];
                Vector2 e = q - p;
                if (e.sqrMagnitude < 1e-12f) continue;
                Vector2 n2 = new Vector2(e.y, -e.x);
                if (Vector2.Dot(n2, (p + q) * 0.5f - c2) < 0f) n2 = -n2;
                Vector3 a = center + rot * new Vector3(p.x, p.y, -hz);
                Vector3 b = center + rot * new Vector3(q.x, q.y, -hz);
                Vector3 c = center + rot * new Vector3(q.x, q.y, hz);
                Vector3 d = center + rot * new Vector3(p.x, p.y, hz);
                AddQuad(a, b, c, d, sideSub, rot * new Vector3(n2.x, n2.y, 0f), axis);
            }
            if (!caps) return;
            var front = new List<Vector3>(n);
            var back = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                front.Add(center + rot * new Vector3(poly[i].x, poly[i].y, hz));
                back.Add(center + rot * new Vector3(poly[i].x, poly[i].y, -hz));
            }
            Vector3 ru = rot * Vector3.right;
            AddPolygon(front, capSub, axis, ru);
            AddPolygon(back, capSub, -axis, ru);
        }

        /// <summary>Box whose 4 edges parallel to local Z are chamfered (eased edges of dimensional lumber).
        /// Long faces use sideSub with U along Z; the two square-cut ends (±Z) use capSub.</summary>
        public void AddBevelBox(Vector3 center, Vector3 size, Quaternion rot, float bevel, int sideSub, int capSub = -1)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f;
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(hx, hy) * 0.45f);
            if (b <= 1e-5f)
            {
                AddExtrude(new[] { new Vector2(hx, hy), new Vector2(-hx, hy), new Vector2(-hx, -hy), new Vector2(hx, -hy) }, center, rot, size.z, sideSub, capSub);
                return;
            }
            var oct = new[]
            {
                new Vector2(hx - b, hy), new Vector2(-hx + b, hy), new Vector2(-hx, hy - b), new Vector2(-hx, -hy + b),
                new Vector2(-hx + b, -hy), new Vector2(hx - b, -hy), new Vector2(hx, -hy + b), new Vector2(hx, hy - b),
            };
            AddExtrude(oct, center, rot, size.z, sideSub, capSub);
        }

        /// <summary>Surface of revolution around local Y. profile = (radius, y) points; traverse counter-clockwise in (r,y)
        /// (bottom outward, side upward, top inward) for outward-facing surfaces; reversed direction faces inward (bowls).
        /// Adjacent profile segments within smoothAngle share normals; a repeated point forces a hard edge.
        /// segments &lt;= 6 gives flat-shaded facets (square/hex shapes).</summary>
        public void AddLathe(Vector3 center, Quaternion rot, IList<Vector2> profile, int segments, int sub, float smoothAngle = 40f)
        {
            int n = profile.Count;
            if (n < 2 || segments < 3) return;
            bool faceted = segments <= 6;
            float rRef = 1e-4f;
            for (int i = 0; i < n; i++) rRef = Mathf.Max(rRef, profile[i].x);

            var segN = new Vector2[n - 1];
            var segOk = new bool[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 t = profile[i + 1] - profile[i];
                float len = t.magnitude;
                if (len > 1e-7f) { segN[i] = new Vector2(t.y, -t.x) / len; segOk[i] = true; }
            }

            float step = Mathf.PI * 2f / segments;
            float vAcc = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                if (!segOk[i]) continue;
                Vector2 p0 = profile[i], p1 = profile[i + 1];
                float len = (p1 - p0).magnitude;
                Vector2 n0 = segN[i], n1 = segN[i];
                if (i > 0 && segOk[i - 1] && Vector2.Angle(segN[i - 1], segN[i]) < smoothAngle) n0 = (segN[i - 1] + segN[i]).normalized;
                if (i < n - 2 && segOk[i + 1] && Vector2.Angle(segN[i], segN[i + 1]) < smoothAngle) n1 = (segN[i] + segN[i + 1]).normalized;

                if (!faceted)
                {
                    int b = verts.Count;
                    for (int s = 0; s <= segments; s++)
                    {
                        float th = s * step;
                        float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
                        V(center + rot * new Vector3(p0.x * cs, p0.y, p0.x * sn), rot * new Vector3(n0.x * cs, n0.y, n0.x * sn), new Vector2(th * rRef, vAcc));
                        V(center + rot * new Vector3(p1.x * cs, p1.y, p1.x * sn), rot * new Vector3(n1.x * cs, n1.y, n1.x * sn), new Vector2(th * rRef, vAcc + len));
                    }
                    for (int s = 0; s < segments; s++)
                    {
                        int a = b + s * 2;
                        QuadIdx(a, a + 1, a + 3, a + 2, sub);
                    }
                }
                else
                {
                    for (int s = 0; s < segments; s++)
                    {
                        float t0 = s * step, t1 = (s + 1) * step, tm = (s + 0.5f) * step;
                        Vector3 a = center + rot * new Vector3(p0.x * Mathf.Cos(t0), p0.y, p0.x * Mathf.Sin(t0));
                        Vector3 bq = center + rot * new Vector3(p1.x * Mathf.Cos(t0), p1.y, p1.x * Mathf.Sin(t0));
                        Vector3 c = center + rot * new Vector3(p1.x * Mathf.Cos(t1), p1.y, p1.x * Mathf.Sin(t1));
                        Vector3 d = center + rot * new Vector3(p0.x * Mathf.Cos(t1), p0.y, p0.x * Mathf.Sin(t1));
                        Vector3 hint = rot * new Vector3(segN[i].x * Mathf.Cos(tm), segN[i].y, segN[i].x * Mathf.Sin(tm));
                        AddQuad(a, bq, c, d, sub, hint);
                    }
                }
                vAcc += len;
            }
        }

        /// <summary>Flat disc in the local XZ plane of rot, facing local +Y (facingUp) or -Y.</summary>
        public void AddDisc(Vector3 center, Quaternion rot, float radius, int segments, int sub, bool facingUp)
        {
            if (radius <= 0f || segments < 3) return;
            var pts = new List<Vector3>(segments);
            float step = Mathf.PI * 2f / segments;
            for (int s = 0; s < segments; s++)
                pts.Add(center + rot * new Vector3(radius * Mathf.Cos(s * step), 0f, radius * Mathf.Sin(s * step)));
            AddPolygon(pts, sub, rot * (facingUp ? Vector3.up : Vector3.down), rot * Vector3.right);
        }

        /// <summary>Cylinder along world Y.</summary>
        public void AddCylinder(Vector3 center, float radius, float height, int segments, int submesh, bool capped = true)
            => AddFrustum(center, Quaternion.identity, radius, radius, height, segments, submesh, capped);

        /// <summary>Cylinder along rot * Y.</summary>
        public void AddCylinder(Vector3 center, Quaternion rot, float radius, float height, int segments, int submesh, bool capped = true)
            => AddFrustum(center, rot, radius, radius, height, segments, submesh, capped);

        public void AddFrustum(Vector3 center, Quaternion rot, float rBottom, float rTop, float height, int segments, int submesh, bool capped = true)
        {
            float h = height * 0.5f;
            AddLathe(center, rot, new[] { new Vector2(rBottom, -h), new Vector2(rTop, h) }, segments, submesh);
            if (!capped) return;
            AddDisc(center + rot * (Vector3.down * h), rot, rBottom, segments, submesh, false);
            AddDisc(center + rot * (Vector3.up * h), rot, rTop, segments, submesh, true);
        }

        /// <summary>Smooth rod along local Z (rebar, pipes along a length). U runs along the length, V around the circumference.</summary>
        public void AddRod(Vector3 center, Quaternion rot, float radius, float length, int segments, int sub, bool capped = true)
        {
            if (segments < 3 || radius <= 0f) return;
            float hz = length * 0.5f, step = Mathf.PI * 2f / segments;
            int b = verts.Count;
            for (int s = 0; s <= segments; s++)
            {
                float th = s * step;
                Vector3 r = new Vector3(Mathf.Cos(th), Mathf.Sin(th), 0f);
                Vector3 nn = rot * r;
                V(center + rot * (r * radius + Vector3.back * hz), nn, new Vector2(-hz, th * radius));
                V(center + rot * (r * radius + Vector3.forward * hz), nn, new Vector2(hz, th * radius));
            }
            for (int s = 0; s < segments; s++)
            {
                int a = b + s * 2;
                QuadIdx(a, a + 1, a + 3, a + 2, sub);
            }
            if (!capped) return;
            var f = new List<Vector3>(segments);
            var k = new List<Vector3>(segments);
            for (int s = 0; s < segments; s++)
            {
                Vector3 r = new Vector3(Mathf.Cos(s * step), Mathf.Sin(s * step), 0f) * radius;
                f.Add(center + rot * (r + Vector3.forward * hz));
                k.Add(center + rot * (r + Vector3.back * hz));
            }
            AddPolygon(f, sub, rot * Vector3.forward, rot * Vector3.right);
            AddPolygon(k, sub, rot * Vector3.back, rot * Vector3.right);
        }

        /// <summary>Thick annulus (washer / rose / grille ring) around rot * Y.</summary>
        public void AddRing(Vector3 center, Quaternion rot, float rOuter, float rInner, float height, int segments, int submesh)
        {
            float h = height * 0.5f;
            AddLathe(center, rot, new[]
            {
                new Vector2(rInner, -h), new Vector2(rOuter, -h), new Vector2(rOuter, h), new Vector2(rInner, h), new Vector2(rInner, -h)
            }, segments, submesh);
        }

        /// <summary>Flat rectangle (local XZ, facing rot*Y) with an elliptical hole, e.g. a countertop around a basin or a fan deck.</summary>
        public void AddRectWithEllipseHole(Vector3 center, Quaternion rot, float hx, float hz, Vector2 holeCenter, float a, float b, int perSide, int sub)
        {
            perSide = Mathf.Max(2, perSide);
            var rect = new List<Vector2>(perSide * 4);
            var corners = new[] { new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz), new Vector2(-hx, -hz) };
            for (int c = 0; c < 4; c++)
                for (int k = 0; k < perSide; k++)
                    rect.Add(Vector2.Lerp(corners[c], corners[(c + 1) % 4], k / (float)perSide));
            int n = rect.Count;
            var ell = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 d = rect[i] - holeCenter;
                float ang = Mathf.Atan2(d.y, d.x);
                ell[i] = holeCenter + new Vector2(a * Mathf.Cos(ang), b * Mathf.Sin(ang));
            }
            Vector3 up = rot * Vector3.up, ru = rot * Vector3.right;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                AddQuad(center + rot * new Vector3(ell[i].x, 0f, ell[i].y), center + rot * new Vector3(ell[j].x, 0f, ell[j].y),
                        center + rot * new Vector3(rect[j].x, 0f, rect[j].y), center + rot * new Vector3(rect[i].x, 0f, rect[i].y), sub, up, ru);
            }
        }

        /// <summary>Open-top tapered basin with inward-facing walls and floor (sink bowl, tub well, shower pan).</summary>
        public void AddWell(Vector3 topCenter, Vector2 topSize, Vector2 bottomSize, float depth, int sub)
        {
            float tx = topSize.x * 0.5f, tz = topSize.y * 0.5f, bx = bottomSize.x * 0.5f, bz = bottomSize.y * 0.5f;
            Vector3 c = topCenter;
            Vector3 T(float x, float z) => c + new Vector3(x, 0f, z);
            Vector3 B(float x, float z) => c + new Vector3(x, -depth, z);
            AddQuad(T(tx, -tz), T(tx, tz), B(bx, bz), B(bx, -bz), sub, Vector3.left);
            AddQuad(T(-tx, -tz), T(-tx, tz), B(-bx, bz), B(-bx, -bz), sub, Vector3.right);
            AddQuad(T(-tx, tz), T(tx, tz), B(bx, bz), B(-bx, bz), sub, Vector3.back);
            AddQuad(T(-tx, -tz), T(tx, -tz), B(bx, -bz), B(-bx, -bz), sub, Vector3.forward);
            AddQuad(B(-bx, -bz), B(bx, -bz), B(bx, bz), B(-bx, bz), sub, Vector3.up);
        }

        // ------------------------------------------------------------------ output

        public Mesh Build(string name = "Procedural")
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            int count = Mathf.Max(1, Mathf.Max(subs.Count, MinSubmeshCount));
            while (subs.Count < count) subs.Add(new List<int>());
            mesh.subMeshCount = count;
            for (int i = 0; i < count; i++) mesh.SetTriangles(subs[i], i, false);
            mesh.RecalculateBounds();
            if (verts.Count > 0) mesh.RecalculateTangents();
            return mesh;
        }
    }
}
