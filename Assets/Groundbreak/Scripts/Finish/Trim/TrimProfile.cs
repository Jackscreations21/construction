using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum TrimKind { Casing, Base, Crown }
    public enum TrimSpecies { Primed, Oak }

    /// <summary>
    /// Real molding cross-sections (US stock profiles) as closed polygons in meters:
    ///   x = distance proud of the wall (0 = back of the molding against the finished wall),
    ///   y = across the molding (base: up from the floor; casing: out from the jamb edge; crown: down from the ceiling).
    /// <see cref="FrontAt"/> gives the face depth at a given y (used for coping the mating piece).
    /// </summary>
    public class TrimProfile
    {
        public string Id, Name;
        public TrimKind Kind;
        public float Width, Thickness;           // nominal (meters)
        public Vector2[] Poly;                   // closed polygon (x proud, y across)
        public float SpringDeg;                  // crown: angle of the back chord to the wall (38 for 52/38 crown)
        public float Drop, Projection;           // crown: on the wall / on the ceiling
        public float PricePerFoot;               // primed; oak = 2.4x

        static readonly List<TrimProfile> _all = new List<TrimProfile>();
        public static IReadOnlyList<TrimProfile> All { get { Init(); return _all; } }
        public static TrimProfile Get(string id) { Init(); return _all.Find(p => p.Id == id); }

        /// <summary>Default profile per kind (colonial casing, 3-1/4" colonial base, 3-5/8" crown).</summary>
        public static TrimProfile Default(TrimKind k) => Get(k == TrimKind.Casing ? "casing-colonial" : k == TrimKind.Base ? "base-colonial" : "crown-358");

        /// <summary>Depth of the profile face at across-position y (max x of the polygon there).</summary>
        public float FrontAt(float y)
        {
            float best = 0f;
            for (int i = 0; i < Poly.Length; i++)
            {
                Vector2 a = Poly[i], b = Poly[(i + 1) % Poly.Length];
                if ((y < Mathf.Min(a.y, b.y) - 1e-6f) || (y > Mathf.Max(a.y, b.y) + 1e-6f)) continue;
                float x = Mathf.Abs(b.y - a.y) < 1e-7f ? Mathf.Max(a.x, b.x) : Mathf.Lerp(a.x, b.x, (y - a.y) / (b.y - a.y));
                best = Mathf.Max(best, x);
            }
            return best;
        }

        public float MaxY { get { float m = 0; foreach (var p in Poly) m = Mathf.Max(m, p.y); return m; } }

        static float I(float inches) => inches * Units.Inch;

        static bool _init;
        static void Init()
        {
            if (_init) return; _init = true;
            // ---- Casing ----
            // WM356 colonial casing 11/16" x 2-1/4": bead at the jamb edge, sloped field, ogee + roundover at the thick outer edge.
            _all.Add(new TrimProfile
            {
                Id = "casing-colonial", Name = "Colonial casing 11/16 x 2-1/4", Kind = TrimKind.Casing, Width = I(2.25f), Thickness = I(0.6875f), PricePerFoot = 1.1f,
                Poly = Front(I(2.25f), Pl(
                    P(0.30f, 0f), P(0.36f, 0.015f), P(0.40f, 0.05f), P(0.42f, 0.10f), P(0.42f, 0.16f), P(0.40f, 0.21f), // bead
                    P(0.37f, 0.24f), P(0.37f, 0.27f),                                                                  // quirk
                    P(0.40f, 0.30f), P(0.47f, 1.35f),                                                                  // sloped field
                    P(0.47f, 1.42f), P(0.50f, 1.42f),                                                                  // fillet step
                    Arc(new Vector2(0.68f, 1.42f), 0.18f, 180f, 90f, 5),                                              // cove
                    P(0.6875f, 1.62f), P(0.6875f, 2.10f),
                    Arc(new Vector2(0.5375f, 2.10f), 0.15f, 0f, 90f, 5)))                                             // outer roundover
            });
            // Craftsman flat casing 3/4" x 3-1/2" (S4S with eased edges).
            _all.Add(new TrimProfile
            {
                Id = "casing-craftsman", Name = "Craftsman flat casing 3/4 x 3-1/2", Kind = TrimKind.Casing, Width = I(3.5f), Thickness = I(0.75f), PricePerFoot = 1.6f,
                Poly = Front(I(3.5f), Pl(
                    P(0.69f, 0f), Arc(new Vector2(0.69f, 0.06f), 0.06f, -90f, 0f, 3), P(0.75f, 3.44f), Arc(new Vector2(0.69f, 3.44f), 0.06f, 0f, 90f, 3), P(0.69f, 3.5f)))
            });
            // ---- Base ----
            // WM623 colonial base 9/16" x 3-1/4".
            _all.Add(new TrimProfile
            {
                Id = "base-colonial", Name = "Colonial base 9/16 x 3-1/4", Kind = TrimKind.Base, Width = I(3.25f), Thickness = I(0.5625f), PricePerFoot = 1.2f,
                Poly = Front(I(3.25f), ColonialBaseFront(3.25f, 0.5625f))
            });
            // 5-1/4" colonial base 5/8".
            _all.Add(new TrimProfile
            {
                Id = "base-525", Name = "Colonial base 5/8 x 5-1/4", Kind = TrimKind.Base, Width = I(5.25f), Thickness = I(0.625f), PricePerFoot = 1.9f,
                Poly = Front(I(5.25f), ColonialBaseFront(5.25f, 0.625f))
            });
            // ---- Crown (52/38 spring) ----
            _all.Add(Crown("crown-358", "Crown 9/16 x 3-5/8 (52/38)", 3.625f, 0.5625f, 1.7f));
            _all.Add(Crown("crown-458", "Crown 11/16 x 4-5/8 (52/38)", 4.625f, 0.6875f, 2.6f));
        }

        static Vector2 P(float xIn, float yIn) => new Vector2(I(xIn), I(yIn));
        static List<Vector2> Pl(params object[] parts)
        {
            var l = new List<Vector2>();
            foreach (var o in parts) { if (o is Vector2 v) l.Add(v); else if (o is Vector2[] a) l.AddRange(a); }
            return l;
        }

        /// <summary>Arc points (inches in, meters out) from angle a0 to a1 (degrees) around center cIn.</summary>
        static Vector2[] Arc(Vector2 cIn, float rIn, float a0, float a1, int segs)
        {
            var l = new Vector2[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)segs) * Mathf.Deg2Rad;
                l[i] = new Vector2(I(cIn.x + rIn * Mathf.Cos(a)), I(cIn.y + rIn * Mathf.Sin(a)));
            }
            return l;
        }

        static List<Vector2> ColonialBaseFront(float h, float t)
        {
            // flat face to ~72% of the height, a small bead/fillet, then an ogee rolling back to a thin rounded top.
            float f = h * 0.72f;
            var l = new List<Vector2>
            {
                P(t - 0.04f, 0f), P(t, 0.04f), P(t, f),
                P(t - 0.05f, f + 0.03f), P(t - 0.05f, f + 0.08f),                                     // quirk
            };
            l.AddRange(Arc(new Vector2(t - 0.05f - 0.13f, f + 0.08f), 0.13f, 0f, 90f, 5));          // ovolo
            float y2 = f + 0.21f, x2 = t - 0.18f;
            l.AddRange(Arc(new Vector2(x2, y2 + 0.16f), 0.16f, -90f, -180f, 5));                   // cove
            float yTop = h, xt = x2 - 0.16f;
            l.Add(P(xt, yTop - 0.06f));
            l.AddRange(Arc(new Vector2(xt - 0.06f, yTop - 0.06f), 0.06f, 0f, 90f, 3));            // rounded top
            l.Add(P(0f, yTop));
            return l;
        }

        /// <summary>Profile from a front polyline (list of points or arcs) between y=0 and y=width; closes along the back (x=0).</summary>
        static Vector2[] Front(float width, List<Vector2> front) => Close(front, width);

        static Vector2[] Close(List<Vector2> front, float width)
        {
            var poly = new List<Vector2> { new Vector2(0f, 0f) };
            foreach (var p in front) if (poly.Count == 0 || (p - poly[poly.Count - 1]).sqrMagnitude > 1e-12f) poly.Add(p);
            if (poly[poly.Count - 1].x > 1e-5f) poly.Add(new Vector2(0f, width));
            return poly.ToArray();
        }


        static TrimProfile Crown(string id, string name, float wIn, float tIn, float price)
        {
            float spring = 38f * Mathf.Deg2Rad;
            float W = I(wIn), T = I(tIn);
            float drop = W * Mathf.Cos(spring), proj = W * Mathf.Sin(spring);
            // Board frame: s along the back chord from the wall contact (bottom) to the ceiling contact, t outward (into the room).
            Vector2 B = new Vector2(0f, drop), C = new Vector2(proj, 0f);
            Vector2 es = (C - B).normalized, et = new Vector2(drop, proj).normalized;
            Vector2 W2(float s, float t) => B + es * s + et * t;
            // face depth t(s) of a classic cove-and-ogee crown, sampled
            var front = new List<Vector2>();
            int N = 44;
            for (int i = 0; i <= N; i++)
            {
                float u = i / (float)N;
                front.Add(W2(u * W, CrownT(u) * T));
            }
            var poly = new List<Vector2>();
            // wall flat (vertical, on the wall) and ceiling flat (horizontal) on the back
            float flat = I(0.35f);
            poly.Add(B + new Vector2(0f, -flat * 0.0f));
            poly.Add(new Vector2(0f, drop - flat));       // up the wall flat
            poly.Add(new Vector2(proj - flat, 0f));       // hollow back chord to the ceiling flat
            poly.Add(C);
            poly.Add(new Vector2(front[front.Count - 1].x, 0f));            // top fillet meets the ceiling
            for (int i = front.Count - 1; i >= 0; i--) poly.Add(front[i]); // down the face back to the bottom
            poly.Add(new Vector2(0f, front[0].y));                          // bottom edge back to the wall
            // ensure y is measured down from the ceiling (already) and x proud of the wall
            return new TrimProfile { Id = id, Name = name, Kind = TrimKind.Crown, Width = drop, Thickness = proj, Poly = Dedup(poly), SpringDeg = 38f, Drop = drop, Projection = proj, PricePerFoot = price };
        }

        /// <summary>Normalized crown face depth along the board (0 = bottom/wall edge, 1 = top/ceiling edge).</summary>
        static float CrownT(float u)
        {
            if (u < 0.05f) return 0.55f;                                               // bottom fillet
            if (u < 0.14f) { float a = (u - 0.05f) / 0.09f; return 0.55f + 0.42f * Mathf.Sin(a * Mathf.PI); } // bead
            if (u < 0.18f) return 0.6f;                                                // quirk/fillet
            if (u < 0.82f)
            {
                float a = (u - 0.18f) / 0.64f;                                         // cyma recta: cove then ovolo
                return 0.6f - 0.36f * Mathf.Sin(a * Mathf.PI * 2f);
            }
            if (u < 0.9f) return 0.85f;                                                // top fillet
            return 0.55f;
        }

        static Vector2[] Dedup(List<Vector2> l)
        {
            var r = new List<Vector2>();
            foreach (var p in l) if (r.Count == 0 || (p - r[r.Count - 1]).sqrMagnitude > 1e-12f) r.Add(p);
            if ((r[0] - r[r.Count - 1]).sqrMagnitude < 1e-12f) r.RemoveAt(r.Count - 1);
            return r.ToArray();
        }
    }
}
