using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Builds the 3D town: streets (30' asphalt, 6" curbs, 4' planting strip, 5' sidewalk in 5' squares, a grass ramp down to the
    /// lawns), driveways with curb cuts, houses (<see cref="HouseBuilder"/>), the hardware store with its parking lot, mailboxes,
    /// trees, for-sale signs, street signs and barricades. Street surfaces are one merged mesh with one MeshCollider.
    /// </summary>
    public static class TownBuilder
    {
        const float Sq = 5f * Units.Foot;                                     // sidewalk square
        const float Top = Town.CurbHeight;
        const float CurbOut = Town.RoadHalf + Town.CurbWidth;
        const float StripOut = CurbOut + Town.StripWidth;
        const float WalkOut = StripOut + Town.SidewalkWidth;
        const float RampLen = 0.95f;
        const float DriveX = Town.LotFrontage / 2f - 1f - Town.DrivewayWidth / 2f; // lot-local x of the driveway centre

        static LandModel _g;   // street surfaces (with collider)
        static LandModel _d;   // decor (no collider)
        static int _asph, _conc, _grass, _drive, _yellow, _white;

        class Gap { public float a0, a1; public bool curb, ramp; }

        public static void Build(Transform root)
        {
            _g = new LandModel(); _d = new LandModel();
            _asph = _g.M(LandSurface.Asphalt); _conc = _g.M(SurfaceKind.Concrete); _grass = _g.M(SurfaceKind.Grass); _drive = _g.M(SurfaceKind.BroomedConcrete);
            _yellow = _d.M(new Color(0.95f, 0.75f, 0.12f)); _white = _d.M(new Color(0.92f, 0.92f, 0.90f));

            // sidewalk squares that belong to houses (fixtures), snapped to the 5' grid
            var fixtureSquares = new Dictionary<TownLot, Vector3>();
            foreach (var lot in Town.CoreLots)
            {
                if (lot.Use != LotUse.Customer && lot.Use != LotUse.PlayerHome) continue;
                var st = StreetOf(lot); if (st == null) continue;
                float a = Along(st, lot.Local(-5f, 0f));
                float c = (Mathf.Floor(a / Sq) + 0.5f) * Sq;
                fixtureSquares[lot] = World(st, c, SideOf(st, lot), (StripOut + WalkOut) / 2f, Top);
            }

            foreach (var st in Town.Streets) Street(st, fixtureSquares);
            Intersections();
            foreach (var lot in Town.CoreLots) LotDressing(lot, root);

            var ground = _g.Make(root, "TownStreets");
            ground.AddComponent<MeshCollider>().sharedMesh = ground.GetComponent<MeshFilter>().sharedMesh;
            _d.Make(root, "TownMarkings");

            foreach (var lot in Town.CoreLots)
                if (lot.Use == LotUse.Customer || lot.Use == LotUse.PlayerHome)
                    HouseBuilder.Build(lot, root, fixtureSquares[lot]);
            Store(Town.Store, root);
            _g = null; _d = null;
        }

        // ------------------------------------------------------------------ street frame helpers

        static TownStreet StreetOf(TownLot lot) { foreach (var s in Town.Streets) if (s.Name == lot.Street) return s; return null; }
        static float Along(TownStreet s, Vector3 p) => s.EastWest ? p.x : p.z;
        static float Perp(TownStreet s) => s.EastWest ? s.A.z : s.A.x;
        static int SideOf(TownStreet s, TownLot lot) => ((s.EastWest ? lot.Center.z : lot.Center.x) - Perp(s)) > 0 ? 1 : -1;
        static Vector3 World(TownStreet s, float along, int side, float d, float y) =>
            s.EastWest ? new Vector3(along, y, s.A.z + side * d) : new Vector3(s.A.x + side * d, y, along);

        /// <summary>Box between along a0..a1 and offsets d0..d1 (from the centreline, on one side), y0..y1.</summary>
        static void SBox(LandModel k, TownStreet s, int side, float a0, float a1, float d0, float d1, float y0, float y1, int sub)
        {
            if (a1 - a0 < 0.01f) return;
            Vector3 p = World(s, a0, side, d0, y0), q = World(s, a1, side, d1, y1);
            Vector3 min = Vector3.Min(p, q), max = Vector3.Max(p, q);
            k.B.AddBox((min + max) / 2f, max - min, Quaternion.identity, sub, s.EastWest ? Vector3.right : Vector3.forward);
        }

        static void SQuad(LandModel k, TownStreet s, int side, float a0, float a1, float d0, float y0, float d1, float y1, int sub)
        {
            if (a1 - a0 < 0.01f) return;
            k.B.AddQuad(World(s, a0, side, d0, y0), World(s, a1, side, d0, y0), World(s, a1, side, d1, y1), World(s, a0, side, d1, y1), sub, Vector3.up,
                s.EastWest ? Vector3.right : Vector3.forward);
        }

        static List<(float, float)> Intervals(TownStreet s)
        {
            float a0 = Mathf.Min(Along(s, s.A), Along(s, s.B)), a1 = Mathf.Max(Along(s, s.A), Along(s, s.B));
            var cuts = new List<float>();
            foreach (var o in Town.Streets) if (o.EastWest != s.EastWest) cuts.Add(Perp(o));
            cuts.Sort();
            var list = new List<(float, float)>();
            float cur = a0;
            foreach (var c in cuts) { if (c - Town.RowHalf > cur) list.Add((cur, c - Town.RowHalf)); cur = Mathf.Max(cur, c + Town.RowHalf); }
            if (a1 > cur) list.Add((cur, a1));
            return list;
        }

        static List<(float, float)> Minus(float a0, float a1, List<Gap> gaps, bool curb)
        {
            var res = new List<(float, float)>();
            float cur = a0;
            gaps.Sort((x, y) => x.a0.CompareTo(y.a0));
            foreach (var g in gaps)
            {
                if (curb ? !g.curb : !g.ramp) continue;
                if (g.a1 <= cur || g.a0 >= a1) continue;
                if (g.a0 > cur) res.Add((cur, g.a0));
                cur = Mathf.Max(cur, g.a1);
            }
            if (a1 > cur) res.Add((cur, a1));
            return res;
        }

        // ------------------------------------------------------------------ streets

        static void Street(TownStreet s, Dictionary<TownLot, Vector3> fixtures)
        {
            float a0 = Mathf.Min(Along(s, s.A), Along(s, s.B)), a1 = Mathf.Max(Along(s, s.A), Along(s, s.B));
            var spans = Intervals(s);
            // pavement: east-west streets run through the intersections, north-south ones stop at them
            if (s.EastWest) SQuad(_g, s, 1, a0, a1, -Town.RoadHalf, 0.02f, Town.RoadHalf, 0.02f, _asph);
            else
            {
                float cur = a0;
                var cross = new List<float>(); foreach (var o in Town.Streets) if (o.EastWest) cross.Add(Perp(o)); cross.Sort();
                foreach (var c in cross) { SQuad(_g, s, 1, cur, c - Town.RoadHalf, -Town.RoadHalf, 0.02f, Town.RoadHalf, 0.02f, _asph); cur = c + Town.RoadHalf; }
                SQuad(_g, s, 1, cur, a1, -Town.RoadHalf, 0.02f, Town.RoadHalf, 0.02f, _asph);
            }
            // double yellow centre line, broken at intersections
            var lineSpans = new List<(float, float)>();
            {
                float cur = a0;
                var cross = new List<float>(); foreach (var o in Town.Streets) if (o.EastWest != s.EastWest) cross.Add(Perp(o)); cross.Sort();
                foreach (var c in cross) { lineSpans.Add((cur, c - Town.RowHalf - 1f)); cur = c + Town.RowHalf + 1f; }
                lineSpans.Add((cur, a1 - 1f));
            }
            foreach (var (l0, l1) in lineSpans)
                for (int k = -1; k <= 1; k += 2) SQuad(_d, s, 1, l0, l1, k * 0.08f - 0.05f, 0.024f, k * 0.08f + 0.05f, 0.024f, _yellow);

            for (int side = -1; side <= 1; side += 2)
            {
                var gaps = new List<Gap>();
                var fixK = new HashSet<int>();
                foreach (var lot in Town.CoreLots)
                {
                    if (lot.Street != s.Name || SideOf(s, lot) != side) continue;
                    if (lot.Use == LotUse.Customer || lot.Use == LotUse.PlayerHome)
                    {
                        float c = Along(s, lot.Local(DriveX, 0));
                        gaps.Add(new Gap { a0 = c - Town.DrivewayWidth / 2f - 0.6f, a1 = c + Town.DrivewayWidth / 2f + 0.6f, curb = true });
                        gaps.Add(new Gap { a0 = c - Town.DrivewayWidth / 2f, a1 = c + Town.DrivewayWidth / 2f, ramp = true });
                        if (fixtures.TryGetValue(lot, out var f)) fixK.Add(Mathf.FloorToInt(Along(s, f) / Sq));
                    }
                    else if (lot.Use == LotUse.Store)
                    {
                        float c = Along(s, lot.Center);
                        gaps.Add(new Gap { a0 = c - 12f, a1 = c + 12f, curb = true });
                        gaps.Add(new Gap { a0 = c - lot.Frontage / 2f, a1 = c + lot.Frontage / 2f, ramp = true });
                    }
                    else if (lot.Use == LotUse.BuildSite)
                    {
                        float c = Along(s, lot.Center);
                        gaps.Add(new Gap { a0 = c - 4f, a1 = c + 4f, curb = true });
                    }
                }
                foreach (var (i0, i1) in spans)
                {
                    foreach (var (c0, c1) in Minus(i0, i1, gaps, true))
                    {
                        SBox(_g, s, side, c0, c1, Town.RoadHalf, CurbOut, 0f, Top, _conc);
                        SBox(_g, s, side, c0, c1, CurbOut, StripOut, 0f, Top, _grass);
                    }
                    foreach (var g in gaps)
                    {
                        if (!g.curb) continue;
                        float g0 = Mathf.Max(g.a0, i0), g1 = Mathf.Min(g.a1, i1);
                        SQuad(_g, s, side, g0, g1, Town.RoadHalf, 0.02f, StripOut, Top, _conc); // apron
                    }
                    // sidewalk squares
                    for (int k = Mathf.FloorToInt(i0 / Sq); k * Sq < i1; k++)
                    {
                        if (fixK.Contains(k)) continue;
                        float q0 = Mathf.Max(i0, k * Sq + 0.003f), q1 = Mathf.Min(i1, (k + 1) * Sq - 0.003f);
                        SBox(_g, s, side, q0, q1, StripOut, WalkOut, 0f, Top, _drive);
                    }
                    foreach (var (r0, r1) in Minus(i0, i1, gaps, false))
                        SQuad(_g, s, side, r0, r1, WalkOut, Top, WalkOut + RampLen, 0.002f, _grass);
                }
            }
            // barricades at both dead ends
            foreach (float end in new[] { a0, a1 })
            {
                if (end == a0 ? s.OpenMin : s.OpenMax) continue;   // the map continues the street here
                float dir = end == a0 ? -1f : 1f;
                for (int side = -1; side <= 1; side += 2)
                    SBox(_d, s, side, end + dir * 0.3f - 0.05f, end + dir * 0.3f + 0.05f, 0.5f, Town.RoadHalf - 0.3f, 0.75f, 0.95f, side > 0 ? _white : _d.M(new Color(0.95f, 0.45f, 0.08f)));
                for (int p = -1; p <= 1; p++)
                    SBox(_d, s, 1, end + dir * 0.3f - 0.04f, end + dir * 0.3f + 0.04f, p * 3.5f - 0.04f, p * 3.5f + 0.04f, 0f, 1.0f, _white);
            }
            // stop bars on the north-south streets
            if (!s.EastWest)
                foreach (var o in Town.Streets)
                    if (o.EastWest)
                    {
                        float z = Perp(o);
                        SQuad(_d, s, 1, z - Town.RowHalf - 1.3f, z - Town.RowHalf - 0.9f, 0.2f, 0.024f, Town.RoadHalf - 0.2f, 0.024f, _white);
                        SQuad(_d, s, -1, z + Town.RowHalf + 0.9f, z + Town.RowHalf + 1.3f, 0.2f, 0.024f, Town.RoadHalf - 0.2f, 0.024f, _white);
                    }
        }

        static void Intersections()
        {
            int post = _d.M(SurfaceKind.Galvanized), blade = _d.M(new Color(0.05f, 0.42f, 0.18f));
            foreach (var ns in Town.Streets)
            {
                if (ns.EastWest) continue;
                foreach (var ew in Town.Streets)
                {
                    if (!ew.EastWest) continue;
                    float X = ns.A.x, Z = ew.A.z;
                    for (int sx = -1; sx <= 1; sx += 2)
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            Vector3 p = new Vector3(X + sx * Town.RoadHalf, 0, Z + sz * Town.RoadHalf), q = new Vector3(X + sx * Town.RowHalf, Top, Z + sz * Town.RowHalf);
                            Vector3 min = Vector3.Min(p, q), max = Vector3.Max(p, q);
                            _g.B.AddBox((min + max) / 2f, max - min, Quaternion.identity, _conc);
                        }
                    // street sign on the north-east corner: two blades
                    Vector3 c = new Vector3(X + Town.RoadHalf + 1.2f, Top, Z + Town.RoadHalf + 1.2f);
                    _d.B.AddBox(c + Vector3.up * 1.4f, new Vector3(0.05f, 2.8f, 0.05f), Quaternion.identity, post);
                    _d.B.AddBox(c + Vector3.up * 2.75f, new Vector3(0.76f, 0.15f, 0.01f), Quaternion.identity, blade);
                    _d.B.AddBox(c + Vector3.up * 2.55f, new Vector3(0.01f, 0.15f, 0.76f), Quaternion.identity, blade);
                }
            }
        }

        // ------------------------------------------------------------------ lots

        static void LotDressing(TownLot lot, Transform root)
        {
            var rot = lot.Rotation;
            int seed = Mathf.Abs(lot.Id.GetHashCode());
            if (lot.Use == LotUse.Customer || lot.Use == LotUse.PlayerHome)
            {
                // driveway: 12' broom-finished slab from the lot line to the house front
                _g.B.AddBox(lot.Local(DriveX, TownHouse.Setback / 2f) + Vector3.up * Top / 2f, new Vector3(Town.DrivewayWidth, Top, TownHouse.Setback), rot, _drive, Vector3.forward);
                // grass ramp beside the driveway is already left out; a short flare back down from the slab sides
                var mb = LandModels.Instance("town-mailbox", root, LandModels.Mailbox, "Mailbox " + lot.Number);
                mb.transform.position = lot.Local(DriveX - Town.DrivewayWidth / 2f - 1.2f, -(Town.RowHalf - (CurbOut + StripOut) / 2f)) + Vector3.up * Top;
                mb.transform.rotation = rot;
                Tree(root, lot.Local(-6.5f, 4.5f), seed % 3, 2.0f + (seed % 5) * 0.15f);
                Tree(root, lot.Local(4f - (seed % 7), 30f), (seed / 5) % 3, 2.6f);
                for (int i = 0; i < 3; i++)
                {
                    float x = new[] { -5.0f, -0.3f, 2.4f }[i];
                    int v = (seed + i) % 3;
                    var sh = LandModels.Instance("town-shrub" + v, root, k => LandModels.Shrub(k, 300 + v, 0.9f, 0.75f, LandModels.LeafBox), "Shrub");
                    sh.transform.position = lot.Local(x, TownHouse.Setback - 0.7f);
                    sh.transform.rotation = rot;
                }
            }
            else if (lot.Use == LotUse.Free)
            {
                int post = _d.M(SurfaceKind.PineLumber), red = _d.M(new Color(0.75f, 0.1f, 0.08f)), wht = _d.M(new Color(0.95f, 0.95f, 0.93f));
                Vector3 p = lot.Local(-Mathf.Min(6f, lot.Frontage / 2f - 2f), 2.5f);
                _d.B.AddBox(p + Vector3.up * 0.6f, new Vector3(0.09f, 1.2f, 0.09f), rot, post, Vector3.up);
                _d.B.AddBox(p + Vector3.up * 0.95f + rot * new Vector3(0, 0, 0.06f), new Vector3(0.61f, 0.46f, 0.02f), rot, red);
                _d.B.AddBox(p + Vector3.up * 0.95f + rot * new Vector3(0, 0, 0.072f), new Vector3(0.5f, 0.12f, 0.004f), rot, wht);
                if (lot.AreaSqFt > 20000f)
                    for (int i = 0; i < 4; i++)
                        Tree(root, lot.Local(-lot.Frontage / 2f + 6f + i * (lot.Frontage - 12f) / 3f, lot.Depth - 6f - (i % 2) * 8f), (seed + i) % 3, 2.8f);
            }
        }

        static void Tree(Transform root, Vector3 pos, int v, float scale)
        {
            int s = Mathf.Clamp(Mathf.RoundToInt(scale * 2f), 2, 7);
            var t = LandModels.Instance("town-tree" + v + "-" + s, root, k => LandModels.Deciduous(k, 81 + v, s / 2f, LandModels.LeafMaple), "Tree");
            t.transform.position = pos;
            t.transform.rotation = Quaternion.Euler(0, v * 117f, 0);
            var col = t.AddComponent<CapsuleCollider>(); col.radius = 0.25f; col.height = 5f; col.center = Vector3.up * 2.5f;
        }

        // ------------------------------------------------------------------ hardware store

        static void Store(TownLot lot, Transform root)
        {
            if (lot == null) return;
            var go = new GameObject("Hartley's Hardware");
            go.transform.SetParent(root, false);
            go.transform.position = lot.FrontMid; go.transform.rotation = lot.Rotation;
            var k = new LandModel();
            int cmu = k.M(SurfaceKind.ConcreteBlock, new Color(0.95f, 0.90f, 0.80f)), paint = k.M(SurfaceKind.PaintedWhite, new Color(0.93f, 0.93f, 0.9f));
            int glass = k.M(SurfaceKind.Glass), alum = k.M(SurfaceKind.BrushedSteel), red = k.M(new Color(0.70f, 0.08f, 0.06f)), wht = k.M(new Color(0.97f, 0.97f, 0.95f));
            int asph = k.M(LandSurface.Asphalt), conc = k.M(SurfaceKind.Concrete), shelf = k.M(SurfaceKind.PaintedSteel, new Color(0.25f, 0.45f, 0.75f));
            float hw = 12.2f, z0 = -26f, z1 = -41.2f, H = 5.5f, Hp = 6.2f, t = 0.2f;
            var cols = new List<(Vector3, Vector3)>();
            void B(Vector3 a, Vector3 b, int sub, bool col = false)
            {
                Vector3 min = Vector3.Min(a, b), max = Vector3.Max(a, b);
                k.B.AddBox((min + max) / 2f, max - min, Quaternion.identity, sub);
                if (col) cols.Add((min, max));
            }
            // parking lot (asphalt at curb height) with stall stripes; the apron in front was built with the street
            B(new Vector3(-lot.Frontage / 2f + 0.2f, 0, 0), new Vector3(lot.Frontage / 2f - 0.2f, Top, z0), asph, true);
            foreach (float row0 in new[] { -6.25f, -17.5f })
                for (float x = -8f - 1.37f - 2.74f * 4; x <= 20f; x += 2.74f)
                    B(new Vector3(x - 0.05f, Top, row0), new Vector3(x + 0.05f, Top + 0.003f, row0 - 5.5f), wht);
            // slab + walls (front wall has a storefront)
            B(new Vector3(-hw, 0, z0), new Vector3(hw, Top + 0.05f, z1), conc, true);
            float y0 = Top + 0.05f;
            B(new Vector3(-hw, y0, z1), new Vector3(hw, Hp, z1 + t), cmu, true);
            B(new Vector3(-hw, y0, z1), new Vector3(-hw + t, Hp, z0), cmu, true);
            B(new Vector3(hw - t, y0, z1), new Vector3(hw, Hp, z0), cmu, true);
            B(new Vector3(-hw, y0, z0 - t), new Vector3(-5f, Hp, z0), cmu, true);
            B(new Vector3(5f, y0, z0 - t), new Vector3(hw, Hp, z0), cmu, true);
            B(new Vector3(-5f, y0 + 3.0f, z0 - t), new Vector3(5f, Hp, z0), cmu, true);
            B(new Vector3(-5f, y0, z0 - t), new Vector3(-0.9f, y0 + 0.4f, z0), cmu, true);
            B(new Vector3(0.9f, y0, z0 - t), new Vector3(5f, y0 + 0.4f, z0), cmu, true);
            B(new Vector3(-5f, y0 + 0.4f, z0 - 0.11f), new Vector3(-0.9f, y0 + 3.0f, z0 - 0.09f), glass);
            B(new Vector3(0.9f, y0 + 0.4f, z0 - 0.11f), new Vector3(5f, y0 + 3.0f, z0 - 0.09f), glass);
            cols.Add((new Vector3(-5f, y0, z0 - 0.15f), new Vector3(-0.9f, y0 + 3f, z0)));
            cols.Add((new Vector3(0.9f, y0, z0 - 0.15f), new Vector3(5f, y0 + 3f, z0)));
            for (float x = -5f; x <= 5.01f; x += 1.025f) if (Mathf.Abs(x) > 0.85f) B(new Vector3(x - 0.03f, y0, z0 - 0.13f), new Vector3(x + 0.03f, y0 + 3.0f, z0 - 0.07f), alum);
            B(new Vector3(-5f, y0 + 2.95f, z0 - 0.13f), new Vector3(5f, y0 + 3.05f, z0 - 0.07f), alum);
            // inside faces painted, roof
            B(new Vector3(-hw + t, y0, z1 + t), new Vector3(-hw + t + 0.005f, H, z0 - t), paint);
            B(new Vector3(hw - t - 0.005f, y0, z1 + t), new Vector3(hw - t, H, z0 - t), paint);
            B(new Vector3(-hw + t, y0, z1 + t), new Vector3(hw - t, H, z1 + t + 0.005f), paint);
            B(new Vector3(-hw, H, z1), new Vector3(hw, H + 0.3f, z0), k.M(SurfaceKind.DarkPlastic), true);
            // gondola shelving + checkout counter
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 5f;
                B(new Vector3(x - 0.6f, y0, z1 + 2f), new Vector3(x + 0.6f, y0 + 1.8f, z0 - 5f), shelf, true);
                for (int s = 0; s < 4; s++) B(new Vector3(x - 0.65f, y0 + 0.25f + s * 0.45f, z1 + 2f), new Vector3(x + 0.65f, y0 + 0.28f + s * 0.45f, z0 - 5f), shelf);
            }
            B(new Vector3(6f, y0, z0 - 2f), new Vector3(9f, y0 + 1.0f, z0 - 2.8f), k.M(SurfaceKind.Oak), true);
            // sign: red panel with white pixel letters, facing the street (+Z); text runs toward -X
            B(new Vector3(-6.2f, y0 + 3.3f, z0 + 0.02f), new Vector3(6.2f, y0 + 5.25f, z0 + 0.12f), red);
            Text(k, "HARTLEY'S", new Vector3(0, y0 + 4.3f, z0 + 0.13f), 0.11f, wht);
            Text(k, "HARDWARE & LUMBER", new Vector3(0, y0 + 3.5f, z0 + 0.13f), 0.065f, wht);
            k.Make(go.transform, "Store");
            foreach (var (min, max) in cols) { var bc = go.AddComponent<BoxCollider>(); bc.center = (min + max) / 2f; bc.size = max - min; }
            var l = new GameObject("StoreLight").AddComponent<Light>();
            l.transform.SetParent(go.transform, false); l.transform.localPosition = new Vector3(0, H - 0.6f, (z0 + z1) / 2f);
            l.type = LightType.Point; l.range = 16f; l.intensity = 1.1f; l.shadows = LightShadows.None;
        }

        static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            { 'H', new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" } },
            { 'A', new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" } },
            { 'R', new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" } },
            { 'T', new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" } },
            { 'L', new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" } },
            { 'E', new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" } },
            { 'Y', new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" } },
            { '\'', new[] { "00100", "00100", "01000", "00000", "00000", "00000", "00000" } },
            { 'S', new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" } },
            { 'D', new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" } },
            { 'W', new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" } },
            { '&', new[] { "01100", "10010", "10100", "01000", "10101", "10010", "01101" } },
            { 'U', new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" } },
            { 'M', new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" } },
            { 'B', new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" } },
        };

        /// <summary>Pixel-font text centred at c (bottom of the line), facing +Z, reading toward -X.</summary>
        static void Text(LandModel k, string s, Vector3 c, float px, int sub)
        {
            float adv = 6 * px, width = s.Length * adv - px;
            for (int i = 0; i < s.Length; i++)
            {
                if (!Glyphs.TryGetValue(s[i], out var g)) continue;
                float left = c.x + width / 2f - i * adv;    // glyph's left edge as seen from the street (+X side)
                for (int row = 0; row < 7; row++)
                {
                    string r = g[row];
                    int col = 0;
                    while (col < 5)
                    {
                        if (r[col] != '1') { col++; continue; }
                        int start = col; while (col < 5 && r[col] == '1') col++;
                        float xa = left - start * px, xb = left - col * px, y = c.y + (6 - row) * px;
                        k.B.AddBox(new Vector3((xa + xb) / 2f, y + px / 2f, c.z + 0.01f), new Vector3(xa - xb - px * 0.08f, px * 0.92f, 0.02f), Quaternion.identity, sub);
                    }
                }
            }
        }
    }
}
