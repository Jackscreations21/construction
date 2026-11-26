using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Builds the map's road surfaces (everything except the core town streets, which TownBuilder draws): pavement, junction
    /// squares, tapers where a street changes width, curbs, planting strips, 5' sidewalk squares, downtown 12' walks, driveway
    /// curb cuts and aprons, gravel shoulders, lane markings, crosswalks and stop bars. Geometry goes into per-cell models
    /// supplied by <see cref="MapWorld"/> (surfaces get a MeshCollider; markings do not).
    /// </summary>
    public static class MapRoads
    {
        public class Junction
        {
            public Vector3 P;
            public float HwNS, HwEW, RowNS, RowEW;   // pavement half widths / right-of-way halves of the crossing roads
            public bool N, S, E, W;                  // arms
            public bool CurbNS, CurbEW, Downtown;
            public readonly List<MapRoad> Roads = new List<MapRoad>();
            public int Arms => (N ? 1 : 0) + (S ? 1 : 0) + (E ? 1 : 0) + (W ? 1 : 0);
        }

        public static readonly List<Junction> Junctions = new List<Junction>();

        const float Top = Town.CurbHeight, Curb = Town.CurbWidth, Sq = 5f * Units.Foot, TaperLen = 14f, Seg = 40f;
        static Func<Vector3, LandModel> _surf, _mark;
        static readonly HashSet<(MapRoad, int, int)> _fixtureSquares = new HashSet<(MapRoad, int, int)>();

        /// <summary>Sidewalk squares that belong to ranch houses (repair fixtures): skipped in the street mesh.</summary>
        public static void AddFixtureSquare(MapRoad r, int side, Vector3 worldCenter) => _fixtureSquares.Add((r, side, Mathf.FloorToInt(r.Along(worldCenter) / Sq)));

        public static void ComputeJunctions()
        {
            Junctions.Clear();
            var roads = MapLayout.Roads;
            foreach (var ew in roads)
            {
                if (!ew.EW) continue;
                foreach (var ns in roads)
                {
                    if (ns.EW) continue;
                    float X = ns.Perp, Z = ew.Perp;
                    if (X < ew.Min - 0.1f || X > ew.Max + 0.1f || Z < ns.Min - 0.1f || Z > ns.Max + 0.1f) continue;
                    if (ew.Core && ns.Core) continue;                   // TownBuilder's own crossings
                    var j = Junctions.Find(q => (q.P - new Vector3(X, 0, Z)).sqrMagnitude < 1f);
                    if (j == null) { j = new Junction { P = new Vector3(X, 0, Z) }; Junctions.Add(j); }
                    j.HwNS = Mathf.Max(j.HwNS, ns.Half); j.HwEW = Mathf.Max(j.HwEW, ew.Half);
                    j.RowNS = Mathf.Max(j.RowNS, ns.RowHalf); j.RowEW = Mathf.Max(j.RowEW, ew.RowHalf);
                    j.E |= ew.Max > X + 1f; j.W |= ew.Min < X - 1f; j.N |= ns.Max > Z + 1f; j.S |= ns.Min < Z - 1f;
                    j.CurbNS |= ns.Curbs; j.CurbEW |= ew.Curbs; j.Downtown |= ew.Class == RoadClass.Downtown || ns.Class == RoadClass.Downtown;
                    if (!j.Roads.Contains(ew)) j.Roads.Add(ew);
                    if (!j.Roads.Contains(ns)) j.Roads.Add(ns);
                }
            }
        }

        static IEnumerable<Junction> On(MapRoad r)
        {
            foreach (var j in Junctions)
                if (j.Roads.Contains(r)) yield return j;
        }

        // ------------------------------------------------------------------ primitives (split into ≤ Seg pieces so each lands in one cell)

        static Vector3 W(MapRoad r, float along, float off, float y) => r.At(along, off, y);

        /// <summary>Horizontal quad on one side of the centreline, offsets d0..d1 (signed), heights y0 at d0 / y1 at d1.</summary>
        static void Quad(Func<Vector3, LandModel> to, Material m, MapRoad r, float a0, float a1, float d0, float y0, float d1, float y1)
        {
            for (float s = a0; s < a1 - 0.005f; s += Seg)
            {
                float e = Mathf.Min(a1, s + Seg);
                Vector3 p0 = W(r, s, d0, y0), p1 = W(r, e, d0, y0), p2 = W(r, e, d1, y1), p3 = W(r, s, d1, y1);
                var k = to((p0 + p2) * 0.5f);
                k.B.AddQuad(p0, p1, p2, p3, k.M(m), Vector3.up, r.EW ? Vector3.right : Vector3.forward);
            }
        }

        static void Box(Func<Vector3, LandModel> to, Material m, MapRoad r, float a0, float a1, float d0, float d1, float y0, float y1)
        {
            if (a1 - a0 < 0.01f || Mathf.Abs(d1 - d0) < 0.005f) return;
            for (float s = a0; s < a1 - 0.005f; s += Seg)
            {
                float e = Mathf.Min(a1, s + Seg);
                Vector3 p = W(r, s, d0, y0), q = W(r, e, d1, y1);
                Vector3 min = Vector3.Min(p, q), max = Vector3.Max(p, q);
                var k = to((min + max) * 0.5f);
                k.B.AddBox((min + max) / 2f, max - min, Quaternion.identity, k.M(m), r.EW ? Vector3.right : Vector3.forward);
            }
        }

        static void WBox(Func<Vector3, LandModel> to, Material m, Vector3 min, Vector3 max)
        {
            var k = to((min + max) * 0.5f);
            k.B.AddBox((min + max) / 2f, max - min, Quaternion.identity, k.M(m));
        }

        static List<(float, float)> Minus(float a0, float a1, List<(float, float)> cuts)
        {
            var res = new List<(float, float)>();
            cuts.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            float cur = a0;
            foreach (var (c0, c1) in cuts)
            {
                if (c1 <= cur || c0 >= a1) continue;
                if (c0 > cur) res.Add((cur, c0));
                cur = Mathf.Max(cur, c1);
            }
            if (a1 > cur) res.Add((cur, a1));
            return res;
        }

        // ------------------------------------------------------------------ build

        public static void Build(Func<Vector3, LandModel> surfaces, Func<Vector3, LandModel> markings)
        {
            _surf = surfaces; _mark = markings;
            ComputeJunctions();
            foreach (var r in MapLayout.Roads) if (!r.Core) Road(r);
            foreach (var j in Junctions) JunctionSquare(j);
            _surf = null; _mark = null;
        }

        static Material Asph => MapMaterials.Asphalt;
        static Material Conc => Materials.Get(SurfaceKind.Concrete);
        static Material Walk => Materials.Get(SurfaceKind.BroomedConcrete);
        static Material Grass => Materials.Get(SurfaceKind.Grass);
        static Material Gravel => Materials.Get(SurfaceKind.Gravel);
        static Material Yellow => MaterialLibrary.Get(new Color(0.95f, 0.75f, 0.12f));
        static Material White => MaterialLibrary.Get(new Color(0.92f, 0.92f, 0.90f));

        /// <summary>The collinear road joined at this road's min (end = -1) or max (end = +1) end, if any.</summary>
        static MapRoad Joined(MapRoad r, int end)
        {
            float a = end < 0 ? r.Min : r.Max;
            foreach (var o in MapLayout.Roads)
            {
                if (o == r || o.EW != r.EW || Mathf.Abs(o.Perp - r.Perp) > 0.1f) continue;
                if (end < 0 && Mathf.Abs(o.Max - a) < 0.5f) return o;
                if (end > 0 && Mathf.Abs(o.Min - a) < 0.5f) return o;
            }
            return null;
        }

        /// <summary>True when this road draws the taper at that joint (it is not core, and it is the map side / the wider one).</summary>
        static bool Tapers(MapRoad r, MapRoad o) => o != null && !r.Core && Mathf.Abs(o.Half - r.Half) > 0.05f && (o.Core || r.Half > o.Half);

        static void Road(MapRoad r)
        {
            float a0 = r.Min, a1 = r.Max, hw = r.Half;
            var js = new List<Junction>(On(r));
            var pavCuts = new List<(float, float)>();
            var sideCuts = new[] { new List<(float, float)>(), new List<(float, float)>() };   // [0] = side -1, [1] = side +1
            var lineCuts = new List<(float, float)>();
            foreach (var j in js)
            {
                float c = r.EW ? j.P.x : j.P.z;
                float oHw = r.EW ? j.HwNS : j.HwEW, oRow = r.EW ? j.RowNS : j.RowEW;
                bool oCurb = r.EW ? j.CurbNS : j.CurbEW;
                pavCuts.Add((c - oHw, c + oHw));
                lineCuts.Add((c - oRow - 1.5f, c + oRow + 1.5f));
                bool armMinus = r.EW ? j.S : j.W, armPlus = r.EW ? j.N : j.E;
                float cut = oCurb && r.Curbs ? oRow : oHw + (r.Curbs ? 0.3f : 0f);
                if (armMinus) sideCuts[0].Add((c - cut, c + cut));
                if (armPlus) sideCuts[1].Add((c - cut, c + cut));
                // a street that ends at the junction: its outer curbs continue around the corner to the far right-of-way line
            }
            // tapers at continuation joints
            var tapers = new List<(float from, float to, float hwFrom)>();
            foreach (int end in new[] { -1, 1 })
            {
                var o = Joined(r, end);
                if (o == null || !Tapers(r, o)) continue;
                float j0 = end < 0 ? a0 : a1, j1 = j0 - end * TaperLen;    // into this road
                tapers.Add((j0, j1, o.Half));
                float lo = Mathf.Min(j0, j1), hi = Mathf.Max(j0, j1);
                pavCuts.Add((lo, hi)); sideCuts[0].Add((lo, hi)); sideCuts[1].Add((lo, hi));
            }

            // driveways / entrances
            var gaps = new[] { new List<(float, float, bool)>(), new List<(float, float, bool)>() };   // (a0, a1, wide)
            foreach (var p in MapLayout.Parcels)
            {
                if (p.Road != r || p.Spec == null) continue;
                int side = SideOf(r, p.Lot);
                int si = side > 0 ? 1 : 0;
                if (p.Site == MapSite.Driveway)
                {
                    float c = r.Along(p.Lot.Local(p.Lot.Frontage / 2f - 1f - Town.DrivewayWidth / 2f, 0));
                    gaps[si].Add((c - Town.DrivewayWidth / 2f, c + Town.DrivewayWidth / 2f, false));
                }
                else if (p.Site == MapSite.ParkingLot || p.Site == MapSite.Gravel || p.Site == MapSite.Farmyard)
                {
                    float wide = p.Site == MapSite.Farmyard ? 5f : 8f;
                    foreach (float fx in Entrances(p))
                    {
                        float c = r.Along(p.Lot.Local(fx, 0));
                        gaps[si].Add((c - wide / 2f, c + wide / 2f, true));
                    }
                }
            }

            // ---- pavement
            foreach (var (s0, s1) in Minus(a0, a1, pavCuts)) Quad(_surf, Asph, r, s0, s1, -hw, 0.02f, hw, 0.02f);
            foreach (var (t0, t1, hf) in tapers)
            {
                float lo = Mathf.Min(t0, t1), hi = Mathf.Max(t0, t1);
                float wLo = t0 < t1 ? hf : hw, wHi = t0 < t1 ? hw : hf;
                Vector3 p0 = W(r, lo, -wLo, 0.02f), p1 = W(r, hi, -wHi, 0.02f), p2 = W(r, hi, wHi, 0.02f), p3 = W(r, lo, wLo, 0.02f);
                var k = _surf((p0 + p2) * 0.5f);
                k.B.AddPolygon(new[] { p0, p1, p2, p3 }, k.M(Asph), Vector3.up, r.EW ? Vector3.right : Vector3.forward);
                if (r.Curbs)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        // angled curb and a walk/grass wedge behind it up to the right-of-way line
                        Vector3 c0 = W(r, lo, side * (wLo + Curb / 2f), Top / 2f), c1 = W(r, hi, side * (wHi + Curb / 2f), Top / 2f);
                        var kk = _surf((c0 + c1) * 0.5f);
                        Vector3 d = c1 - c0;
                        kk.B.AddBox((c0 + c1) * 0.5f, new Vector3(Curb, Top, d.magnitude), Quaternion.LookRotation(d.normalized, Vector3.up), kk.M(Conc), d.normalized);
                        Vector3 e0 = W(r, lo, side * (wLo + Curb), Top), e1 = W(r, hi, side * (wHi + Curb), Top), e2 = W(r, hi, side * r.RowHalf, Top), e3 = W(r, lo, side * r.RowHalf, Top);
                        kk.B.AddPolygon(new[] { e0, e1, e2, e3 }, kk.M(r.Class == RoadClass.Downtown ? Walk : Grass), Vector3.up);
                    }
                else
                    for (int side = -1; side <= 1; side += 2) Quad(_surf, Gravel, r, lo, hi, side * Mathf.Max(wLo, wHi), 0.012f, side * (Mathf.Max(wLo, wHi) + 1.1f), 0.004f);
            }

            // ---- sides
            for (int si = 0; si < 2; si++)
            {
                int side = si == 0 ? -1 : 1;
                var spans = Minus(a0, a1, sideCuts[si]);
                var curbCuts = new List<(float, float)>(); var rampCuts = new List<(float, float)>();
                foreach (var (g0, g1, wide) in gaps[si]) { curbCuts.Add((g0 - 0.6f, g1 + 0.6f)); rampCuts.Add((g0, g1)); }
                foreach (var (s0, s1) in spans)
                {
                    switch (r.Class)
                    {
                        case RoadClass.Residential:
                        case RoadClass.Industrial:
                        {
                            float co = hw + Curb, so = co + Town.StripWidth, wo = so + Town.SidewalkWidth;
                            bool walks = r.Walks;
                            if (!walks) so = r.RowHalf;
                            foreach (var (c0, c1) in Minus(s0, s1, curbCuts))
                            {
                                Box(_surf, Conc, r, c0, c1, side * hw, side * co, 0f, Top);
                                Box(_surf, Grass, r, c0, c1, side * co, side * so, 0f, Top);
                            }
                            foreach (var (g0, g1, wide) in gaps[si])
                            {
                                float q0 = Mathf.Max(g0 - 0.6f, s0), q1 = Mathf.Min(g1 + 0.6f, s1);
                                if (q1 > q0) Quad(_surf, Conc, r, q0, q1, side * hw, 0.02f, side * (walks ? co + Town.StripWidth : r.RowHalf), Top);
                            }
                            if (walks)
                            {
                                for (int k = Mathf.FloorToInt(s0 / Sq); k * Sq < s1; k++)
                                {
                                    if (_fixtureSquares.Contains((r, side, k))) continue;
                                    float q0 = Mathf.Max(s0, k * Sq + 0.003f), q1 = Mathf.Min(s1, (k + 1) * Sq - 0.003f);
                                    Box(_surf, Walk, r, q0, q1, side * so, side * wo, 0f, Top);
                                }
                                foreach (var (r0, r1) in Minus(s0, s1, rampCuts))
                                    Quad(_surf, Grass, r, r0, r1, side * wo, Top, side * (wo + 0.95f), 0.002f);
                            }
                            break;
                        }
                        case RoadClass.Downtown:
                        {
                            float co = hw + Curb;
                            Box(_surf, Conc, r, s0, s1, side * hw, side * co, 0f, Top);
                            for (int k = Mathf.FloorToInt(s0 / Sq); k * Sq < s1; k++)
                            {
                                float q0 = Mathf.Max(s0, k * Sq + 0.003f), q1 = Mathf.Min(s1, (k + 1) * Sq - 0.003f);
                                Box(_surf, Walk, r, q0, q1, side * co, side * r.RowHalf, 0f, Top);
                            }
                            break;
                        }
                        default:   // highway / rural: gravel shoulder, entrances paved to the lot line
                        {
                            Quad(_surf, Gravel, r, s0, s1, side * hw, 0.012f, side * (hw + 1.1f), 0.004f);
                            foreach (var (g0, g1, wide) in gaps[si])
                            {
                                float q0 = Mathf.Max(g0, s0), q1 = Mathf.Min(g1, s1);
                                if (q1 > q0) Quad(_surf, wide ? Asph : Gravel, r, q0, q1, side * hw, 0.018f, side * r.RowHalf, 0.018f);
                            }
                            break;
                        }
                    }
                }
            }

            // ---- markings
            var lines = Minus(a0 + 1f, a1 - 1f, lineCuts);
            foreach (var (l0, l1) in lines)
            {
                bool dashed = r.Class == RoadClass.Rural;
                if (dashed)
                    for (float s = l0; s < l1; s += 12.2f) Quad(_mark, Yellow, r, s, Mathf.Min(l1, s + 3f), -0.05f, 0.025f, 0.05f, 0.025f);
                else
                    for (int k = -1; k <= 1; k += 2) Quad(_mark, Yellow, r, l0, l1, k * 0.08f - 0.05f, 0.025f, k * 0.08f + 0.05f, 0.025f);
                if (r.Class == RoadClass.Highway || r.Class == RoadClass.Rural)
                {
                    float e = r.Class == RoadClass.Highway ? 12f * Units.Foot : hw - 0.25f;
                    for (int side = -1; side <= 1; side += 2) Quad(_mark, White, r, l0, l1, side * e - 0.05f, 0.025f, side * e + 0.05f, 0.025f);
                }
                if (r.Class == RoadClass.Downtown)   // parking lane line + stall ticks
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float e = 11f * Units.Foot;
                        Quad(_mark, White, r, l0, l1, side * e - 0.05f, 0.025f, side * e + 0.05f, 0.025f);
                        for (float s = l0; s < l1; s += 6.7f) Quad(_mark, White, r, s, s + 0.1f, side * e, 0.025f, side * hw, 0.025f);
                    }
            }
            // dead ends (no junction, no continuation): a Type III barricade
            foreach (int end in new[] { -1, 1 })
            {
                float e = end < 0 ? a0 : a1;
                if (Joined(r, end) != null) continue;
                bool atJ = false; foreach (var j in js) if (Mathf.Abs((r.EW ? j.P.x : j.P.z) - e) < 1f) atJ = true;
                if (atJ || !MapLayout.Bounds.Contains(new Vector2(r.At(e).x, r.At(e).z) * 1f) || Mathf.Abs(e) > 700f) continue;
                float d = -end * 0.4f;
                Box(_mark, White, r, Mathf.Min(e + d, e + d + 0.1f), Mathf.Max(e + d, e + d + 0.1f), -hw + 0.4f, hw - 0.4f, 0.75f, 0.95f);
                Box(_mark, MaterialLibrary.Get(new Color(0.95f, 0.45f, 0.08f)), r, Mathf.Min(e + d, e + d + 0.1f), Mathf.Max(e + d, e + d + 0.1f), -hw + 0.4f, hw - 0.4f, 1.05f, 1.25f);
                for (int q = -1; q <= 1; q++) Box(_mark, White, r, e + d - 0.04f, e + d + 0.04f, q * (hw - 0.6f) - 0.04f, q * (hw - 0.6f) + 0.04f, 0f, 1.3f);
            }
            // stop bars on the minor approaches (this road stops when it is not the through road of a junction it ends at or crosses a bigger road)
            foreach (var j in js)
            {
                float c = r.EW ? j.P.x : j.P.z;
                float oRow = r.EW ? j.RowNS : j.RowEW;
                bool through = r.EW ? (j.E && j.W) : (j.N && j.S);
                bool major = r.Class == RoadClass.Highway || (r.Class == RoadClass.Downtown) || (r.EW && through && j.Arms == 4 && !IsBigger(j, r));
                if (major || (through && j.Arms == 3)) continue;
                // approach from the min side (traffic on the right = side -1 for EW heading +x... keep both directions simple: right-hand lanes)
                if (c - oRow - 1.4f > a0) Quad(_mark, White, r, c - oRow - 1.4f, c - oRow - 1.0f, r.EW ? -hw + 0.2f : 0.2f, 0.026f, r.EW ? -0.2f : hw - 0.2f, 0.026f);
                if (c + oRow + 1.4f < a1) Quad(_mark, White, r, c + oRow + 1.0f, c + oRow + 1.4f, r.EW ? 0.2f : -hw + 0.2f, 0.026f, r.EW ? hw - 0.2f : -0.2f, 0.026f);
            }
        }

        static bool IsBigger(Junction j, MapRoad r)
        {
            foreach (var o in j.Roads) if (o.EW != r.EW && (o.Class == RoadClass.Highway || o.Class == RoadClass.Downtown)) return true;
            return false;
        }

        static void JunctionSquare(Junction j)
        {
            float X = j.P.x, Z = j.P.z;
            Vector3 a = new Vector3(X - j.HwNS, 0.02f, Z - j.HwEW), b = new Vector3(X + j.HwNS, 0.02f, Z + j.HwEW);
            var k = _surf(j.P);
            k.B.AddQuad(new Vector3(a.x, 0.02f, a.z), new Vector3(a.x, 0.02f, b.z), new Vector3(b.x, 0.02f, b.z), new Vector3(b.x, 0.02f, a.z), k.M(Asph), Vector3.up);
            bool curbs = j.CurbNS && j.CurbEW;
            // corner pads (concrete at curb height) where two curbed arms meet
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    bool armX = sx > 0 ? j.E : j.W, armZ = sz > 0 ? j.N : j.S;
                    if (curbs && armX && armZ)
                    {
                        Vector3 p = new Vector3(X + sx * j.HwNS, 0, Z + sz * j.HwEW), q = new Vector3(X + sx * j.RowNS, Top, Z + sz * j.RowEW);
                        WBox(_surf, j.Downtown ? Walk : Conc, Vector3.Min(p, q), Vector3.Max(p, q));
                    }
                }
            // L corner (two perpendicular arms): close the outer side of the square with a concrete pad at curb height
            if (curbs && j.Arms == 2 && (j.E || j.W) && (j.N || j.S))
            {
                float sxo = j.E ? -1f : 1f, szo = j.N ? -1f : 1f;
                Vector3 p1 = new Vector3(X, 0, Z + szo * j.HwEW), q1 = new Vector3(X + sxo * j.RowNS, Top, Z + szo * j.RowEW);
                WBox(_surf, Conc, Vector3.Min(p1, q1), Vector3.Max(p1, q1));
                Vector3 p2 = new Vector3(X + sxo * j.HwNS, 0, Z), q2 = new Vector3(X + sxo * j.RowNS, Top, Z + szo * j.HwEW);
                WBox(_surf, Conc, Vector3.Min(p2, q2), Vector3.Max(p2, q2));
            }
            if (j.Downtown)
            {
                // ladder crosswalks on every arm
                var m = _mark(j.P);
                int white = m.M(White);
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    bool arm = dx > 0 ? j.E : dx < 0 ? j.W : dz > 0 ? j.N : j.S;
                    if (!arm) continue;
                    float hwAcross = dx != 0 ? j.HwEW : j.HwNS, off = (dx != 0 ? j.HwNS : j.HwEW) + 1.2f;
                    for (float t = -hwAcross + 0.3f; t < hwAcross - 0.3f; t += 0.9f)
                    {
                        Vector3 c = dx != 0 ? new Vector3(X + dx * (off + 1.5f), 0.026f, Z + t + 0.3f) : new Vector3(X + t + 0.3f, 0.026f, Z + dz * (off + 1.5f));
                        Vector3 size = dx != 0 ? new Vector3(3f, 0.004f, 0.6f) : new Vector3(0.6f, 0.004f, 3f);
                        m.B.AddBox(c, size, Quaternion.identity, white);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ helpers shared with MapWorld

        public static int SideOf(MapRoad r, TownLot lot) => ((r.EW ? lot.Center.z : lot.Center.x) - r.Perp) > 0 ? 1 : -1;

        /// <summary>Lot-local x positions of a parcel's vehicle entrances (parking lots / yards).</summary>
        public static IEnumerable<float> Entrances(MapParcel p)
        {
            float w = p.Lot.Frontage;
            if (p.Site == MapSite.Farmyard) { yield return -w / 2f + 26f; yield break; }
            if (w > 50f) { yield return -w / 2f + 10f; yield return w / 2f - 10f; }
            else yield return w / 2f - 7f;
        }
    }
}
