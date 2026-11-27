using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// The open land the map layout leaves between roads, lots, fields and woods (road verges, the strips behind lot rows, the corners of the
    /// strip and the industrial park...) was plain mown grass and read as empty. This fills it like real edge-of-town land: patches of unmown
    /// meadow (pasture texture blobs) with grass tufts, weeds and wildflowers, scrubby thickets with shrubs and volunteer trees, and the odd
    /// lone tree on the mown parts. Everything goes into the map cells (merged, streamed with them); trees get the usual trunk colliders.
    /// Deterministic. Capped (<see cref="MaxSpots"/>) so the map build stays fast.
    /// </summary>
    public static class MapGapFill
    {
        const float G = 4f;
        public static int MaxSpots = 9000;
        public static int Spots { get; private set; }
        public static int Trees { get; private set; }
        public static int Patches { get; private set; }
        public static float FreeArea { get; private set; }

        static Material _rough;
        /// <summary>Unmown meadow ground: the pasture texture a little darker / greener so it sits on the lawn without glowing. Named
        /// "MapField_Pasture…" so the seasons (tints, snow) treat it like every other pasture.</summary>
        public static Material RoughMeadow
        {
            get
            {
                if (_rough != null) return _rough;
                var src = MapMaterials.Field(FieldKind.Pasture);
                _rough = new Material(src) { name = "MapField_PastureRough", hideFlags = HideFlags.DontUnloadUnusedAsset };
                _rough.color = new Color(src.color.r * 0.70f, src.color.g * 0.80f, src.color.b * 0.62f, src.color.a);
                return _rough;
            }
        }

        static bool[] _cover;
        static int _nx, _nz;
        static Rect _b;

        static float Hash(int x, int z, int s) { unchecked { uint h = (uint)(x * 374761393 + z * 668265263 + s * 1442695041); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777215f; } }
        static float Noise(float x, float z, int s)
        {
            int x0 = Mathf.FloorToInt(x), z0 = Mathf.FloorToInt(z); float tx = x - x0, tz = z - z0;
            tx = tx * tx * (3 - 2 * tx); tz = tz * tz * (3 - 2 * tz);
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, z0, s), Hash(x0 + 1, z0, s), tx), Mathf.Lerp(Hash(x0, z0 + 1, s), Hash(x0 + 1, z0 + 1, s), tx), tz);
        }
        /// <summary>0..1 wildness of open land: mown below ~0.45, meadow above, thicket above ~0.72.</summary>
        static float Wild(float x, float z) => 0.65f * Noise(x / 70f, z / 70f, 17) + 0.35f * Noise(x / 23f, z / 23f, 29);

        /// <summary>True where nothing else is: inside the map bounds, off roads / lots / the build site / ponds / the creek and outside every
        /// field, meadow, woods and hill area.</summary>
        public static bool Free(Vector3 p)
        {
            if (_cover == null) return false;
            int x = Mathf.FloorToInt((p.x - _b.xMin) / G), z = Mathf.FloorToInt((p.z - _b.yMin) / G);
            if (x < 1 || z < 1 || x >= _nx - 1 || z >= _nz - 1) return false;
            return !_cover[z * _nx + x];
        }

        static bool OnOtherRoad(MapRoad self, Vector3 p)
        {
            foreach (var o in MapLayout.Roads)
            {
                if (o == self) continue;
                float along = o.Along(p), perp = o.EW ? p.z : p.x;
                if (along >= o.Min - 2f && along <= o.Max + 2f && Mathf.Abs(perp - o.Perp) <= o.RowHalf + 2f) return true;
            }
            return false;
        }

        static void Cover(Rect r)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((r.xMin - _b.xMin) / G), 0, _nx - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((r.xMax - _b.xMin) / G), 0, _nx - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((r.yMin - _b.yMin) / G), 0, _nz - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((r.yMax - _b.yMin) / G), 0, _nz - 1);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) _cover[z * _nx + x] = true;
        }

        /// <summary>Called by <see cref="MapWorld.Build"/> after roads, buildings, areas and street furniture.</summary>
        internal static void Build(System.Func<Vector3, LandModel> veg, System.Action<Vector3, float, int, bool> tree)
        {
            Spots = 0; Trees = 0; Patches = 0;
            _b = MapLayout.Bounds;
            _nx = Mathf.CeilToInt(_b.width / G); _nz = Mathf.CeilToInt(_b.height / G);
            _cover = new bool[_nx * _nz];
            for (int z = 0; z < _nz; z++)
                for (int x = 0; x < _nx; x++)
                {
                    var p = new Vector3(_b.xMin + (x + 0.5f) * G, 0, _b.yMin + (z + 0.5f) * G);
                    if (Blocking.Blocked(p)) _cover[z * _nx + x] = true;
                }
            foreach (var a in MapLayout.Areas)
                if (a.Kind != AreaKind.Pond) Cover(new Rect(a.R.x - 1f, a.R.y - 1f, a.R.width + 2f, a.R.height + 2f));
            // the build site and its margins are VacantLots' / BuildSiteFill's
            var bs = Town.BuildSiteRect; Cover(new Rect(bs.x - 2f, bs.y - 2f, bs.width + 4f, bs.height + 4f));
            int free = 0;
            foreach (var c in _cover) if (!c) free++;
            FreeArea = free * G * G;

            // spot probability per free 4 m cell, scaled so the total stays under the cap
            float want = 0f;
            for (int z = 0; z < _nz; z++)
                for (int x = 0; x < _nx; x++)
                {
                    if (_cover[z * _nx + x]) continue;
                    float w = Wild(_b.xMin + (x + 0.5f) * G, _b.yMin + (z + 0.5f) * G);
                    want += w < 0.45f ? 0.08f : w < 0.72f ? 1.1f : 1.6f;
                }
            float scale = want > MaxSpots ? MaxSpots / want : 1f;

            var rng = new System.Random(4242);
            // 1) meadow / thicket ground: irregular pasture-texture blobs where it is wild (read from far away and from the air)
            for (float z = _b.yMin + 10f; z < _b.yMax - 10f; z += 26f)
                for (float x = _b.xMin + 10f; x < _b.xMax - 10f; x += 26f)
                {
                    var c = new Vector3(x + (float)(rng.NextDouble() - 0.5) * 14f, 0, z + (float)(rng.NextDouble() - 0.5) * 14f);
                    if (!Free(c) || Wild(c.x, c.z) < 0.55f) continue;
                    // irregular outline: each spoke as long as the open land allows (6-16 m), wobbling
                    const int N = 16;
                    var pts = new List<Vector3>();
                    float ang0 = (float)rng.NextDouble() * 6.28f, stretch = 0.8f + (float)rng.NextDouble() * 0.6f;
                    float rmin = 99f;
                    for (int i = 0; i < N; i++)
                    {
                        float a = ang0 - i * Mathf.PI * 2f / N;
                        var dir = new Vector3(Mathf.Cos(a) * stretch, 0, Mathf.Sin(a));
                        float reach = 6f + 10f * Noise(c.x * 0.05f + Mathf.Cos(a) * 1.7f, c.z * 0.05f + Mathf.Sin(a) * 1.7f, 41);
                        float r = 1f;
                        while (r < reach && Free(c + dir * (r + 1f))) r += 1f;
                        rmin = Mathf.Min(rmin, r);
                        pts.Add(c + dir * r + Vector3.up * 0.02f);
                    }
                    if (rmin < 2f) continue;
                    var k = veg(c);
                    // fan from the centre (the outline is star-shaped around it, not convex)
                    int sub = k.M(RoughMeadow);
                    for (int i = 0; i < N; i++) k.B.AddTriangle(c + Vector3.up * 0.02f, pts[i], pts[(i + 1) % N], sub, Vector3.up, Vector3.right);
                    Patches++;
                }

            // 2) weeds, wildflowers, shrubs and trees cell by cell
            for (int z = 0; z < _nz; z++)
                for (int x = 0; x < _nx; x++)
                {
                    if (_cover[z * _nx + x]) continue;
                    float cx = _b.xMin + (x + 0.5f) * G, cz = _b.yMin + (z + 0.5f) * G;
                    float w = Wild(cx, cz);
                    float n = (w < 0.45f ? 0.08f : w < 0.72f ? 1.1f : 1.6f) * scale;
                    int spots = (int)n + (rng.NextDouble() < n - (int)n ? 1 : 0);
                    for (int i = 0; i < spots; i++)
                    {
                        var p = new Vector3(cx + (float)(rng.NextDouble() - 0.5) * G, 0, cz + (float)(rng.NextDouble() - 0.5) * G);
                        var k = veg(p);
                        if (w < 0.45f) Weeds.Tuft(k, p, rng, 0.25f + (float)rng.NextDouble() * 0.2f, WeedTone.Grass, false, 6);
                        else Weeds.Spot(k, p, rng, Mathf.Clamp01((w - 0.4f) * 2.2f), true, 7);
                        Spots++;
                    }
                    double t = rng.NextDouble();
                    if (w >= 0.72f && t < 0.05)
                    {   // volunteer box elder / ash / cedar in the thicket
                        var p = new Vector3(cx + (float)(rng.NextDouble() - 0.5) * 2f, 0, cz + (float)(rng.NextDouble() - 0.5) * 2f);
                        tree(p, 6f + (float)rng.NextDouble() * 8f, rng.Next(), rng.NextDouble() < 0.75); Trees++;
                    }
                    else if (w >= 0.6f && t < 0.16)
                    {
                        var p = new Vector3(cx, 0, cz); var k = veg(p);
                        MapTrees.Bush(new MapBuild { K = k, Rng = rng, Frame = Matrix4x4.identity }, p, 0.8f + (float)rng.NextDouble() * 0.9f, rng.Next());
                        k.B.ResetTransform();
                    }
                    else if (w < 0.45f && t < 0.0035)
                    {   // a lone shade tree on the mown land
                        tree(new Vector3(cx, 0, cz), 9f + (float)rng.NextDouble() * 7f, rng.Next(), true); Trees++;
                    }
                }
            // 3) unmown verges along the highway and the county roads: tall grass and chicory along the fence / lot line, where mowers stop
            int verge = 0;
            foreach (var r in MapLayout.Roads)
            {
                if (r.Core || (r.Class != RoadClass.Highway && r.Class != RoadClass.Rural)) continue;
                bool wide = r.Class == RoadClass.Rural && r.RowHalf - r.Half >= 6f;
                if (wide)   // county roads: rough unmown ground from past the gravel shoulder to the fence, in 40 m pieces
                    for (float a = r.Min + 6f; a < r.Max - 6f; a += 40f)
                        for (int side = -1; side <= 1; side += 2)
                        {
                            float a1 = Mathf.Min(a + 40f, r.Max - 6f);
                            Vector3 p0 = r.At(a, side * (r.Half + 4f), 0.018f), p1 = r.At(a1, side * (r.Half + 4f), 0.018f);
                            Vector3 q0 = r.At(a, side * (r.RowHalf - 0.2f), 0.018f), q1 = r.At(a1, side * (r.RowHalf - 0.2f), 0.018f);
                            var mid = (p0 + q1) * 0.5f;
                            if (!_b.Contains(new Vector2(mid.x, mid.z)) || OnOtherRoad(r, p0) || OnOtherRoad(r, p1) || OnOtherRoad(r, q0) || OnOtherRoad(r, q1)) continue;
                            var kq = veg(mid);
                            kq.B.AddQuad(p0, p1, q1, q0, kq.M(RoughMeadow), Vector3.up);
                        }
                for (float a = r.Min + 8f; a < r.Max - 8f && verge < 10000; a += wide ? 0.8f : 2.6f)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (rng.NextDouble() < 0.3) continue;
                        // highway: a strip along the lot line; county roads: the whole verge past the gravel shoulder, thicker toward the fence
                        float o = !wide ? r.RowHalf - 0.3f - (float)rng.NextDouble() * 1.8f
                            : Mathf.Lerp(r.Half + 4.3f, r.RowHalf - 0.3f, Mathf.Sqrt((float)rng.NextDouble()));
                        var p = r.At(a + (float)(rng.NextDouble() - 0.5) * 2f, side * o);
                        if (!_b.Contains(new Vector2(p.x, p.z)) || OnOtherRoad(r, p)) continue;
                        bool drive = false;
                        foreach (var l in Town.Lots) if ((l.Parking - p).sqrMagnitude < 49f || (l.FrontMid - p).sqrMagnitude < 25f && l.Category != "farm") { drive = true; break; }
                        if (drive) continue;
                        var k = veg(p);
                        if (rng.NextDouble() < 0.15) Weeds.Wildflowers(k, p, rng, 0.45f + (float)rng.NextDouble() * 0.3f);
                        else Weeds.Tuft(k, p, rng, 0.4f + (float)rng.NextDouble() * 0.35f, rng.NextDouble() < 0.4 ? WeedTone.Straw : WeedTone.Grass, rng.NextDouble() < 0.3, 6);
                        verge++; Spots++;
                    }
            }
            // 4) the backs of the big business lots (behind the strip, the industrial park, the civic buildings): unmown ground, weeds and
            //    a ragged tree line along the back lot line instead of a bare lawn
            int rear = 0;
            foreach (var p in MapLayout.Parcels)
            {
                var lot = p.Lot; var s = p.Spec;
                if (s == null || p.Ranch || lot == null || lot.Category == "residential" || lot.Category == "farm" || s.Type == "school" || s.Type == "storefront") continue;
                if (p.Site != MapSite.ParkingLot && p.Site != MapSite.Gravel && p.Site != MapSite.Lawn) continue;
                float W = lot.Frontage, D = lot.Depth;
                float d0 = p.Setback + Mathf.Max(24f, s.Depth) + (p.Site == MapSite.Gravel ? 9f : 4f), d1 = D - 0.6f;
                if (d1 - d0 < 6f || W < 12f) continue;
                Vector3 a = lot.Local(-W / 2f + 0.6f, d0), b = lot.Local(W / 2f - 0.6f, d1);
                var c = (a + b) * 0.5f;
                var k = veg(c);
                Vector3 c00 = lot.Local(-W / 2f + 0.6f, d0) + Vector3.up * 0.02f, c10 = lot.Local(W / 2f - 0.6f, d0) + Vector3.up * 0.02f,
                        c11 = lot.Local(W / 2f - 0.6f, d1) + Vector3.up * 0.02f, c01 = lot.Local(-W / 2f + 0.6f, d1) + Vector3.up * 0.02f;
                k.B.AddQuad(c00, c10, c11, c01, k.M(RoughMeadow), Vector3.up);
                int n = Mathf.Min(220, Mathf.RoundToInt((W - 1.2f) * (d1 - d0) / 7f));
                for (int i = 0; i < n; i++)
                {
                    var q = lot.Local(Mathf.Lerp(-W / 2f + 0.8f, W / 2f - 0.8f, (float)rng.NextDouble()), Mathf.Lerp(d0 + 0.5f, d1 - 0.2f, Mathf.Pow((float)rng.NextDouble(), 0.6f)));
                    Weeds.Spot(veg(q), q, rng, 0.7f, true, 6);
                    Spots++; rear++;
                }
                for (float x = -W / 2f + 3f; x < W / 2f - 2f; x += 7f + (float)rng.NextDouble() * 6f)
                    if (rng.NextDouble() < 0.6)
                    {
                        var tp = lot.Local(x, D - 1.5f - (float)rng.NextDouble() * 1.5f);
                        if (!Blocking.Blocked(tp, true)) { tree(tp, 8f + (float)rng.NextDouble() * 7f, rng.Next(), rng.NextDouble() < 0.75); Trees++; }
                    }
            }
            _cover = null;
            Debug.Log($"[Map] gap fill: {FreeArea / 10000f:0.0} ha open land, {Patches} meadow patches, {Spots} weed spots (scale {scale:0.00}), {Trees} trees");
        }
    }
}
