using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Visible storm damage at a lot (enterable TownHouse or a map house): blue tarp over torn-off shingles, siding knocked off to the sheathing,
    /// a fallen tree with its root plate, a blown-over fence bay, snow piles / ice dam with icicles, sandbags, a flood water line and puddles.
    /// Real sizes, UVs in metres. Everything lives under one root ("StormDamage", parented to the town so Town.Destroy removes it).
    /// </summary>
    public static class StormDamage
    {
        static GameObject _root;
        static readonly Dictionary<string, GameObject> _byLot = new Dictionary<string, GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _root = null; _byLot.Clear(); }

        public static int Count { get { Prune(); return _byLot.Count; } }
        public static bool Has(string lotId) { Prune(); return lotId != null && _byLot.ContainsKey(lotId); }
        public static IEnumerable<string> Lots { get { Prune(); return new List<string>(_byLot.Keys); } }

        static void Prune()
        {
            List<string> dead = null;
            foreach (var kv in _byLot) if (kv.Value == null) (dead ??= new List<string>()).Add(kv.Key);
            if (dead != null) foreach (var k in dead) _byLot.Remove(k);
        }

        public static void Clear(string lotId)
        {
            if (lotId != null && _byLot.TryGetValue(lotId, out var go)) { if (go != null) Object.Destroy(go); _byLot.Remove(lotId); }
        }

        public static void ClearAll()
        {
            foreach (var go in _byLot.Values) if (go != null) Object.Destroy(go);
            _byLot.Clear();
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }

        static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    _root = new GameObject("StormDamage");
                    if (Town.Root != null) _root.transform.SetParent(Town.Root, false);
                }
                return _root.transform;
            }
        }

        // ------------------------------------------------------------------ geometry of the house on a lot

        struct HouseGeo
        {
            public bool Ok;
            public Vector3 Origin;          // front-centre of the building at grade
            public Quaternion Rot;          // +Z toward the street
            public float Width, Depth, Setback;
            public Pose? RoofPose;          // on the front roof plane: +Y normal, +Z up-slope
            public Pose? WallPose;          // on the front wall face at ~1.3 m: +Z out of the wall
            public float EaveY;
            public Vector3 EavePoint;      // front of the gutter / eave edge, at its top
            public Pose? LowWall;          // front wall face near grade (flood line)
        }

        static readonly List<MeshCollider> _temp = new List<MeshCollider>();

        /// <summary>Raycast against the merged map building meshes (only boxes collide normally): temporary mesh colliders on the cells crossed.</summary>
        static bool RayMap(Ray ray, float max, out RaycastHit best)
        {
            best = default; bool any = false;
            if (MapWorld.Root != null)
                foreach (var mf in MapWorld.Root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.name != "Buildings" || mf.sharedMesh == null) continue;
                    var b = mf.sharedMesh.bounds; b.center = mf.transform.TransformPoint(b.center); b.Expand(0.5f);
                    if (!b.IntersectRay(ray, out float d0) || d0 > max) continue;
                    var mc = mf.GetComponent<MeshCollider>();
                    if (mc == null) { mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; _temp.Add(mc); }
                    if (mc.Raycast(ray, out var h, max) && (!any || h.distance < best.distance)) { best = h; any = true; }
                }
            // enterable houses and everything else with real colliders
            foreach (var h in Physics.RaycastAll(ray, max))
            {
                if (h.collider is BoxCollider && h.collider.transform.name.StartsWith("Colliders")) continue;   // map boxes are approximate
                if (h.collider.GetComponentInParent<TownHouse>() == null) continue;
                if (!any || h.distance < best.distance) { best = h; any = true; }
            }
            return any;
        }

        static void DropTemp() { foreach (var mc in _temp) if (mc != null) Object.Destroy(mc); _temp.Clear(); }

        static HouseGeo Geo(TownLot lot)
        {
            var g = new HouseGeo();
            if (lot.House != null)
            {
                var h = lot.House;
                g.Ok = true; g.Rot = h.transform.rotation; g.Width = h.Width; g.Depth = h.Depth;
                g.Origin = h.transform.position + g.Rot * new Vector3(0f, 0f, h.Depth / 2f);
                g.Setback = TownHouse.Setback;
                g.RoofPose = h.Anchor(HouseSpot.RoofShingles);
                var wc = h.Anchor(HouseSpot.WindowCaulk);
                g.WallPose = new Pose(wc.position + wc.rotation * Vector3.right * 1.6f + Vector3.up * 0.15f, wc.rotation);
                var gu = h.Anchor(HouseSpot.Gutter); g.EaveY = gu.position.y; g.EavePoint = gu.position + gu.rotation * Vector3.forward * 0.06f;
                g.LowWall = new Pose(new Vector3(wc.position.x, 0f, wc.position.z), wc.rotation);
                return g;
            }
            var p = MapLayout.ParcelOf(lot);
            if (p == null || p.Spec == null) { g.Rot = lot.Rotation; g.Origin = lot.Local(0f, lot.Depth * 0.45f); g.Setback = lot.Depth * 0.4f; return g; }
            g.Ok = true; g.Origin = p.Origin; g.Rot = p.Rotation; g.Width = p.Spec.Width; g.Depth = p.Spec.Depth; g.Setback = p.Setback;
            Vector3 fwd = g.Rot * Vector3.forward, right = g.Rot * Vector3.right;
            float guessEave = 0.5f + p.Spec.Stories * 2.8f;
            // roof: straight down a bit behind the front wall, a third of the way across
            var rp = g.Origin + right * (g.Width * 0.18f) - fwd * Mathf.Min(2.2f, g.Depth * 0.2f) + Vector3.up * 40f;
            if (RayMap(new Ray(rp, Vector3.down), 60f, out var hit) && hit.normal.y > 0.3f)
            {
                var upSlope = Vector3.ProjectOnPlane(-fwd, hit.normal).normalized;
                g.RoofPose = new Pose(hit.point, Quaternion.LookRotation(upSlope, hit.normal));
            }
            var ep = g.Origin - fwd * 0.35f + Vector3.up * 40f;
            g.EaveY = RayMap(new Ray(ep, Vector3.down), 60f, out var eh) ? eh.point.y : guessEave;
            g.EavePoint = new Vector3(g.Origin.x, g.EaveY, g.Origin.z) + fwd * 0.3f;
            if (RayMap(new Ray(g.Origin + fwd * 8f + Vector3.up * 0.3f, -fwd), 14f, out var lh)) g.LowWall = new Pose(lh.point, Quaternion.LookRotation(lh.normal, Vector3.up));
            // front wall at window height, off to one side of the door
            var wp = g.Origin + right * (g.Width * 0.28f) + fwd * 6f + Vector3.up * 1.3f;
            if (RayMap(new Ray(wp, -fwd), 12f, out var wh)) g.WallPose = new Pose(wh.point, Quaternion.LookRotation(wh.normal, Vector3.up));
            else g.WallPose = new Pose(g.Origin + right * (g.Width * 0.28f) + Vector3.up * 1.3f, g.Rot);
            return g;
        }

        // ------------------------------------------------------------------ API

        /// <summary>Puts this storm's damage on a lot (replacing any earlier damage there). Returns the holder object.</summary>
        public static GameObject Add(TownLot lot, StormKind kind, int seed)
        {
            if (lot == null || kind == StormKind.None) return null;
            Clear(lot.Id);
            var holder = new GameObject("Storm_" + lot.Id + "_" + kind);
            holder.transform.SetParent(Root, false);
            _byLot[lot.Id] = holder;
            var rng = new System.Random(seed * 7919 + lot.Id.GetHashCode());
            float R() => (float)rng.NextDouble();
            HouseGeo g;
            try { g = Geo(lot); } finally { DropTemp(); }
            Vector3 fwd = g.Rot * Vector3.forward, right = g.Rot * Vector3.right;
            float side = R() < 0.5f ? -1f : 1f;
            // front-yard point (lot-local depth measured from the street line)
            Vector3 Yard(float x, float depthFromStreet) => lot.Local(x, depthFromStreet);
            float yardDepth = Mathf.Max(5f, g.Setback);

            switch (kind)
            {
                case StormKind.Hail:
                    if (g.RoofPose.HasValue) RoofTarp(holder.transform, g.RoofPose.Value, 4.2f, 3.0f, rng);
                    if (g.WallPose.HasValue) SidingHole(holder.transform, g.WallPose.Value, rng);
                    LooseShingles(holder.transform, g.Origin + fwd * 1.5f + right * (g.Width * 0.15f), g.Rot, 6, rng);
                    LeafLitter(holder.transform, Yard(0f, yardDepth * 0.5f), g.Rot, Mathf.Min(10f, lot.Frontage * 0.6f), yardDepth * 0.6f, rng);
                    break;
                case StormKind.Windstorm:
                    if (g.RoofPose.HasValue) RoofTarp(holder.transform, g.RoofPose.Value, 3.3f, 2.4f, rng);
                    FallenTree(holder.transform, Yard(side * lot.Frontage * 0.22f, yardDepth * 0.55f), g.Rot * Quaternion.Euler(0f, side * (70f + R() * 30f), 0f), 8.5f + R() * 3f, rng);
                    FenceBay(holder.transform, Yard(-side * (lot.Frontage / 2f - 0.4f), yardDepth * 0.35f), g.Rot * Quaternion.Euler(0, 90f, 0), rng);
                    LooseShingles(holder.transform, g.Origin + fwd * 2f, g.Rot, 4, rng);
                    break;
                case StormKind.HeavySnow:
                    SnowPile(holder.transform, lot.Parking + right * 2.6f, 1.6f, 1.0f);
                    SnowPile(holder.transform, lot.Parking - right * 2.8f + fwd * 2f, 1.2f, 0.8f);
                    if (g.Ok) IceDam(holder.transform, g.EavePoint, g.Rot, g.Width * 0.8f, g.EaveY, rng);
                    FallenTree(holder.transform, Yard(side * lot.Frontage * 0.25f, yardDepth * 0.5f), g.Rot * Quaternion.Euler(0f, side * 80f, 0f), 4.5f, rng, limb: true);
                    break;
                case StormKind.FloodRain:
                    if (g.Ok)
                    {
                        var door = lot.FrontDoor; var toStreet = Vector3.ProjectOnPlane(lot.FrontMid - door, Vector3.up).normalized;
                        Sandbags(holder.transform, door + toStreet * 0.9f, Quaternion.LookRotation(toStreet, Vector3.up), 7);
                        var lw = g.LowWall ?? new Pose(g.Origin, g.Rot);
                        WaterLine(holder.transform, lw.position, lw.rotation, Mathf.Min(g.Width * 0.9f, 9f));
                    }
                    for (int i = 0; i < 5; i++) Puddle(holder.transform, Yard((R() - 0.5f) * lot.Frontage * 0.7f, yardDepth * (0.2f + R() * 0.6f)), 0.7f + R() * 1.2f, 0.45f + R() * 0.8f, R() * 180f);
                    LeafLitter(holder.transform, Yard(0f, yardDepth * 0.5f), g.Rot, Mathf.Min(10f, lot.Frontage * 0.6f), yardDepth * 0.5f, rng);
                    break;
            }
            return holder;
        }

        // ------------------------------------------------------------------ pieces

        static GameObject Make(Transform parent, LandModel k, string name, Vector3 pos, Quaternion rot)
        {
            var go = k.Make(parent, name);
            go.transform.SetPositionAndRotation(pos, rot);
            return go;
        }

        /// <summary>Blue poly tarp over a torn-off patch (rippled, battened with 2x4s along the edges), laid on the roof plane.</summary>
        static void RoofTarp(Transform parent, Pose roof, float w, float l, System.Random rng)
        {
            var k = new LandModel(); int tarp = k.M(LandSurface.Tarp, new Color(0.55f, 0.78f, 1.3f)), wood = k.M(SurfaceKind.PineLumber), felt = k.M(new Color(0.16f, 0.16f, 0.17f)), deck = k.M(SurfaceKind.Osb);
            // exposed felt / missing shingles around the tarp edge
            k.B.AddBox(new Vector3(0.35f, 0.005f, -0.15f), new Vector3(w + 0.9f, 0.01f, l + 0.4f), Quaternion.Euler(0, 4f, 0), felt);
            k.B.AddBox(new Vector3(-0.6f, 0.006f, 0.35f), new Vector3(w * 0.6f, 0.01f, l + 0.9f), Quaternion.Euler(0, -6f, 0), felt);
            // bare decking where the felt blew off too, peeking out below the tarp
            k.B.AddBox(new Vector3(w * 0.3f, 0.008f, -l / 2f - 0.25f), new Vector3(1.1f, 0.012f, 0.45f), Quaternion.Euler(0, 9f, 0), deck);
            k.B.AddBox(new Vector3(-w * 0.35f, 0.008f, -l / 2f - 0.15f), new Vector3(0.7f, 0.012f, 0.3f), Quaternion.Euler(0, -5f, 0), deck);
            const int nx = 8, nz = 6; var pts = new Vector3[(nx + 1) * (nz + 1)];
            for (int z = 0; z <= nz; z++) for (int x = 0; x <= nx; x++)
                {
                    float u = x / (float)nx - 0.5f, v = z / (float)nz - 0.5f;
                    float bump = (x == 0 || z == 0 || x == nx || z == nz) ? 0f : 0.03f + 0.05f * (float)rng.NextDouble() + 0.09f * Mathf.Abs(Mathf.Sin((u * 2.3f + v * 0.8f) * 9f));
                    pts[z * (nx + 1) + x] = new Vector3(u * w, 0.03f + bump, v * l);
                }
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
                {
                    int a = z * (nx + 1) + x;
                    k.B.AddQuad(pts[a], pts[a + nx + 1], pts[a + nx + 2], pts[a + 1], tarp, Vector3.up, Vector3.right);
                }
            // 2x4 battens screwed through the tarp along the edges (tarp wrapped around them)
            foreach (float zz in new[] { -l / 2f, l / 2f }) k.B.AddBox(new Vector3(0, 0.05f, zz), new Vector3(w + 0.2f, 0.05f, 0.089f), Quaternion.identity, wood);
            foreach (float xx in new[] { -w / 2f, w / 2f }) k.B.AddBox(new Vector3(xx, 0.05f, 0), new Vector3(0.089f, 0.05f, l), Quaternion.identity, wood);
            Make(parent, k, "RoofTarp", roof.position + roof.rotation * new Vector3(0f, 0.01f, 0.5f), roof.rotation * Quaternion.Euler(0f, (float)rng.NextDouble() * 10f - 5f, 0f));
        }

        /// <summary>Vinyl siding blown / hail-shattered off: bare OSB sheathing with torn house wrap and a couple of cracked courses hanging.</summary>
        static void SidingHole(Transform parent, Pose wall, System.Random rng)
        {
            var k = new LandModel(); int osb = k.M(SurfaceKind.Osb), wrap = k.M(new Color(0.92f, 0.93f, 0.95f)), vinyl = k.M(new Color(0.86f, 0.86f, 0.82f));
            k.B.AddBox(new Vector3(0, 0, 0.012f), new Vector3(1.5f, 0.75f, 0.012f), Quaternion.identity, osb);
            k.B.AddBox(new Vector3(-0.55f, 0.18f, 0.02f), new Vector3(0.45f, 0.35f, 0.004f), Quaternion.Euler(0, 0, 8f), wrap);
            for (int i = 0; i < 3; i++)
            {
                float ang = -12f - 18f * (float)rng.NextDouble(), x = -0.4f + 0.45f * i;
                k.B.AddBox(new Vector3(x, -0.45f - 0.08f * i, 0.04f), new Vector3(0.7f, 0.2f, 0.012f), Quaternion.Euler(0, 0, ang * (i % 2 == 0 ? 1 : -1)), vinyl);
            }
            Make(parent, k, "SidingDamage", wall.position, wall.rotation);
            // a few broken pieces on the ground below
            var g = new LandModel(); int v2 = g.M(new Color(0.86f, 0.86f, 0.82f));
            for (int i = 0; i < 4; i++) g.B.AddBox(new Vector3(((float)rng.NextDouble() - 0.5f) * 1.6f, 0.01f, 0.4f + (float)rng.NextDouble() * 0.8f), new Vector3(0.3f + 0.3f * (float)rng.NextDouble(), 0.012f, 0.2f), Quaternion.Euler(0, (float)rng.NextDouble() * 180f, 0), v2);
            var flat = Quaternion.LookRotation(Vector3.ProjectOnPlane(wall.rotation * Vector3.forward, Vector3.up).normalized, Vector3.up);
            Make(parent, g, "SidingPieces", new Vector3(wall.position.x, GroundY(wall.position), wall.position.z), flat);
        }

        static float GroundY(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, p.y + 0.5f, p.z), Vector3.down, out var h, 6f) && !(h.collider.GetComponentInParent<TownHouse>() != null && h.point.y > 0.3f)) return h.point.y;
            return 0f;
        }

        /// <summary>Torn 3-tab shingles (12" x 36") on the lawn.</summary>
        static void LooseShingles(Transform parent, Vector3 at, Quaternion rot, int n, System.Random rng)
        {
            var k = new LandModel(); int sh = k.M(RoofTextures.Shingle);
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(((float)rng.NextDouble() - 0.5f) * 5f, 0.012f, (float)rng.NextDouble() * 3f);
                k.B.AddBox(p, new Vector3(0.914f * (0.5f + 0.5f * (float)rng.NextDouble()), 0.006f, 0.305f), Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 8f, (float)rng.NextDouble() * 180f, 0), sh);
            }
            Make(parent, k, "LooseShingles", new Vector3(at.x, GroundY(at), at.z), rot);
        }

        /// <summary>Stripped leaves and small twigs scattered on the lawn.</summary>
        static void LeafLitter(Transform parent, Vector3 center, Quaternion rot, float w, float d, System.Random rng)
        {
            var k = new LandModel(); int leaf = k.M(LandSurface.Foliage, new Color(0.7f, 0.85f, 0.5f)), twig = k.M(LandSurface.Bark);
            for (int i = 0; i < 70; i++)
            {
                var p = new Vector3(((float)rng.NextDouble() - 0.5f) * w, 0.015f, ((float)rng.NextDouble() - 0.5f) * d);
                k.B.AddBox(p, new Vector3(0.07f, 0.004f, 0.05f), Quaternion.Euler(0, (float)rng.NextDouble() * 180f, 0), leaf);
            }
            for (int i = 0; i < 10; i++)
            {
                var p = new Vector3(((float)rng.NextDouble() - 0.5f) * w, 0.02f, ((float)rng.NextDouble() - 0.5f) * d);
                k.B.AddRod(p, Quaternion.Euler(0, (float)rng.NextDouble() * 180f, 0), 0.012f, 0.4f + 0.6f * (float)rng.NextDouble(), 5, twig);
            }
            Make(parent, k, "LeafLitter", new Vector3(center.x, GroundY(center), center.z), rot);
        }

        /// <summary>Uprooted tree lying along local +Z from its root plate (or a snapped limb when limb = true).</summary>
        static void FallenTree(Transform parent, Vector3 basePos, Quaternion rot, float length, System.Random rng, bool limb = false)
        {
            var k = new LandModel(); int bark = k.M(LandSurface.Bark), dirt = k.M(SurfaceKind.Dirt), wood = k.M(SurfaceKind.PineEndGrain);
            int leaf = k.M(LandSurface.Foliage, new Color(0.72f, 0.86f, 0.60f)), leafDk = k.M(LandSurface.Foliage, new Color(0.55f, 0.66f, 0.46f));
            float r0 = limb ? 0.13f : 0.24f + 0.04f * (float)rng.NextDouble(), r1 = r0 * 0.3f;
            var along = Quaternion.Euler(90f, 0f, 0f);   // lathe +Y → +Z
            k.B.AddFrustum(new Vector3(0, r0, length / 2f), along, r0, r1, length, 10, bark, false);
            if (limb) k.B.AddDisc(new Vector3(0, r0, 0), Quaternion.Euler(-90f, 0f, 0f), r0, 10, wood, true);   // fresh broken end
            else
            {
                // root plate torn out of the lawn, standing on edge, and the hole it left
                // snapped trunk: a jagged stump about 0.8 m tall with pale torn wood, the trunk's broken end beside it
                float sr = r0 * 1.12f;
                k.B.AddFrustum(new Vector3(0, 0.4f, -0.45f), Quaternion.identity, sr * 1.1f, sr, 0.8f, 10, bark, false);
                k.B.AddDisc(new Vector3(0, 0.8f, -0.45f), Quaternion.identity, sr, 10, wood, true);
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * Mathf.PI * 2f, hh = 0.12f + 0.3f * (float)rng.NextDouble();
                    k.B.AddFrustum(new Vector3(Mathf.Cos(a) * sr * 0.6f, 0.8f + hh / 2f, -0.45f + Mathf.Sin(a) * sr * 0.6f), Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 20f, 0, ((float)rng.NextDouble() - 0.5f) * 20f), sr * 0.35f, 0.01f, hh, 4, wood, false);
                }
                k.B.AddDisc(new Vector3(0, r0, 0.02f), Quaternion.Euler(-90f, 0f, 0f), r0, 10, wood, true);
                k.B.AddDisc(new Vector3(0, 0.012f, -0.45f), Quaternion.identity, 0.9f, 10, dirt, true);   // sawdust / torn sod around the stump
            }
            int branches = limb ? 4 : 8;
            for (int i = 0; i < branches; i++)
            {
                float t = 0.35f + 0.6f * i / branches, z = length * t, s = i % 2 == 0 ? 1f : -1f;
                float bl = (limb ? 1.0f : 1.8f) + 1.4f * (float)rng.NextDouble() * (1f - t * 0.5f);
                var dir = Quaternion.Euler(-(10f + 40f * (float)rng.NextDouble()), s * (35f + 30f * (float)rng.NextDouble()), 0f);
                var start = new Vector3(0, r0 * (1f - t * 0.6f), z);
                var mid = start + dir * Vector3.forward * (bl / 2f);
                k.B.AddRod(mid, dir, r0 * 0.28f * (1f - t * 0.5f), bl, 6, bark);
                var tip = start + dir * Vector3.forward * bl;
                float cr = (limb ? 0.6f : 0.9f) + 0.5f * (float)rng.NextDouble();
                k.B.AddLathe(tip, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), new[] { new Vector2(0.01f, -cr * 0.5f), new Vector2(cr, -cr * 0.1f), new Vector2(cr * 0.7f, cr * 0.35f), new Vector2(0.01f, cr * 0.55f) }, 7, i % 3 == 0 ? leafDk : leaf);
            }
            // crown at the tip
            float tipR = limb ? 1.0f : 1.8f;
            k.B.AddLathe(new Vector3(0, tipR * 0.6f, length + tipR * 0.4f), Quaternion.identity, new[] { new Vector2(0.01f, -tipR * 0.5f), new Vector2(tipR, -tipR * 0.1f), new Vector2(tipR * 0.7f, tipR * 0.35f), new Vector2(0.01f, tipR * 0.55f) }, 8, leaf);
            Make(parent, k, limb ? "FallenLimb" : "FallenTree", new Vector3(basePos.x, GroundY(basePos), basePos.z), rot);
        }

        /// <summary>One 8' bay of 6' dog-ear privacy fence blown over: two 4x4 posts (one snapped), rails, pickets flat and leaning.</summary>
        static void FenceBay(Transform parent, Vector3 at, Quaternion rot, System.Random rng)
        {
            var k = new LandModel(); int pt = k.M(SurfaceKind.TreatedLumber), cedar = k.M(LandSurface.Cedar);
            float post = Units.In(3.5f), bay = 8f * Units.Foot, h = 6f * Units.Foot;
            k.B.AddBox(new Vector3(0, h / 2f + 0.05f, 0), new Vector3(post, h + 0.1f, post), Quaternion.identity, pt);          // standing post
            k.B.AddBox(new Vector3(bay, 0.25f, 0), new Vector3(post, 0.5f, post), Quaternion.identity, pt);                   // snapped stub
            // the bay, hinged at the standing post's base line, leaning ~65° over
            var lean = Quaternion.Euler(-55f, 0f, 0f);
            var bayK = new List<(Vector3 c, Vector3 s)>();
            foreach (float ry in new[] { 0.3f, 1.5f }) bayK.Add((new Vector3(bay / 2f, ry, -0.05f), new Vector3(bay, Units.In(3.5f), Units.In(1.5f))));
            for (int i = 0; i < 17; i++)
            {
                if (i == 5 || i == 11 || (i == 14 && rng.NextDouble() < 0.6)) continue;  // missing pickets
                bayK.Add((new Vector3(Units.In(2.75f) + i * Units.In(5.6f), h / 2f, 0.03f), new Vector3(Units.In(5.5f), h, Units.In(0.625f))));
            }
            foreach (var (c, s) in bayK) k.B.AddBox(lean * c + new Vector3(0, 0.05f, 0), s, lean, s.x > 1f ? pt : cedar);
            k.B.AddBox(lean * new Vector3(bay, h / 2f, -0.05f) + new Vector3(0, 0.05f, 0), new Vector3(post, h - 0.5f, post), lean, pt);   // broken post top
            // loose pickets on the grass
            for (int i = 0; i < 3; i++) k.B.AddBox(new Vector3(0.6f + i * 1.3f, 0.01f, 0.9f + 0.4f * i), new Vector3(Units.In(5.5f), Units.In(0.625f), h), Quaternion.Euler(0, 20f + 40f * i, 0), cedar);
            Make(parent, k, "FenceDown", new Vector3(at.x, GroundY(at), at.z), rot);
        }

        static void SnowPile(Transform parent, Vector3 at, float r, float h)
        {
            var k = new LandModel(); int snow = k.M(SeasonLook.SnowMaterial);
            k.B.AddLathe(new Vector3(0, h * 0.5f, 0), Quaternion.identity, new[] { new Vector2(r * 1.15f, -h * 0.5f), new Vector2(r, -h * 0.15f), new Vector2(r * 0.6f, h * 0.35f), new Vector2(0.01f, h * 0.5f) }, 12, snow);
            k.B.AddLathe(new Vector3(r * 0.5f, h * 0.3f, r * 0.3f), Quaternion.identity, new[] { new Vector2(r * 0.7f, -h * 0.3f), new Vector2(r * 0.5f, h * 0.1f), new Vector2(0.01f, h * 0.3f) }, 10, snow);
            var go = Make(parent, k, "SnowPile", new Vector3(at.x, GroundY(at), at.z), Quaternion.Euler(0, at.x * 13f % 360f, 0));
            // plowed piles are long, lumpy ridges, not domes
            var mesh = go.GetComponent<MeshFilter>().sharedMesh; var v = mesh.vertices; float seed = at.x * 0.37f + at.z * 0.11f;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i]; float n = Mathf.PerlinNoise(p.x * 1.7f + seed, p.z * 1.7f + seed) - 0.5f;
                p.x *= 1.6f; p.z *= 0.8f; p.y = Mathf.Max(0f, p.y * (1f + n * 0.8f) + (p.y > 0.05f ? n * 0.15f : 0f));
                v[i] = p;
            }
            mesh.vertices = v; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }

        /// <summary>Ice dam along the front eave with a curtain of icicles; snow load on the roof edge.</summary>
        static void IceDam(Transform parent, Vector3 eaveFront, Quaternion rot, float width, float eaveY, System.Random rng)
        {
            var k = new LandModel(); int ice = k.M(SeasonLook.IceMaterial), snow = k.M(SeasonLook.SnowMaterial);
            k.B.AddBox(new Vector3(0, 0.08f, -0.25f), new Vector3(width, 0.16f, 0.55f), Quaternion.Euler(-20f, 0, 0), snow);
            k.B.AddBox(new Vector3(0, -0.02f, 0.02f), new Vector3(width, 0.09f, 0.14f), Quaternion.identity, ice);
            int n = Mathf.RoundToInt(width / 0.12f);
            for (int i = 0; i < n; i++)
            {
                if (rng.NextDouble() < 0.25) continue;
                float len = 0.2f + 0.8f * (float)(rng.NextDouble() * rng.NextDouble()), x = -width / 2f + (i + 0.5f) * width / n;
                k.B.AddLathe(new Vector3(x, -0.06f - len / 2f, 0.05f), Quaternion.identity, new[] { new Vector2(0.003f, -len / 2f), new Vector2(0.03f + len * 0.035f, len / 2f) }, 6, ice);
            }
            Make(parent, k, "IceDam", new Vector3(eaveFront.x, eaveY - 0.04f, eaveFront.z), rot);
        }

        /// <summary>Two courses of 14" x 26" sandbags (~40 lb) across the front of the door.</summary>
        static void Sandbags(Transform parent, Vector3 at, Quaternion rot, int perRow)
        {
            var k = new LandModel(); int bag = k.M(SurfaceKind.PaperBag, new Color(0.85f, 0.80f, 0.62f));
            float L = 0.62f, W = 0.33f, H = 0.13f;
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < perRow - row; i++)
                {
                    var c = new Vector3((i - (perRow - row - 1) / 2f) * L * 0.92f, H * 0.5f + row * H * 0.85f, 0);
                    k.B.AddLathe(c, Quaternion.Euler(0, 0, 90f), new[] { new Vector2(0.01f, -L / 2f), new Vector2(W * 0.45f, -L * 0.38f), new Vector2(W * 0.5f, 0f), new Vector2(W * 0.45f, L * 0.38f), new Vector2(0.01f, L / 2f) }, 8, bag);
                }
            // flatten the lathe bags: scale vertically by baking into a squashed transform
            var go = Make(parent, k, "Sandbags", new Vector3(at.x, GroundY(at), at.z), rot);
            var mf = go.GetComponent<MeshFilter>(); var mesh = mf.sharedMesh; var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i].y *= 0.55f;
            mesh.vertices = v; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }

        /// <summary>Muddy flood water line on the foundation / bottom siding course (about 16" high).</summary>
        static void WaterLine(Transform parent, Vector3 wallFront, Quaternion rot, float width)
        {
            var k = new LandModel(); int stain = k.M(new Color(0.36f, 0.31f, 0.24f)), line = k.M(new Color(0.25f, 0.21f, 0.16f));
            k.B.AddBox(new Vector3(0, 0.2f, 0.012f), new Vector3(width, 0.4f, 0.006f), Quaternion.identity, stain);
            k.B.AddBox(new Vector3(0, 0.41f, 0.016f), new Vector3(width, 0.025f, 0.006f), Quaternion.identity, line);
            Make(parent, k, "FloodLine", new Vector3(wallFront.x, GroundY(wallFront), wallFront.z), rot);
        }

        static Material _puddle;
        /// <summary>Muddy standing water: dark brown-grey, mirror-glossy.</summary>
        static Material PuddleWater
        {
            get
            {
                if (_puddle != null) return _puddle;
                _puddle = new Material(Shader.Find("Standard")) { name = "GB_PuddleWater", color = new Color(0.22f, 0.2f, 0.16f), hideFlags = HideFlags.DontUnloadUnusedAsset };
                _puddle.SetFloat("_Glossiness", 0.96f); _puddle.SetFloat("_Metallic", 0.1f);
                return _puddle;
            }
        }

        static void Puddle(Transform parent, Vector3 at, float a, float b, float yaw)
        {
            var k = new LandModel(); int water = k.M(PuddleWater), mud = k.M(SurfaceKind.Dirt, new Color(0.55f, 0.5f, 0.45f));
            var pts = new List<Vector3>(); var rim = new List<Vector3>();
            for (int i = 0; i < 16; i++)
            {
                float t = i / 16f * Mathf.PI * 2f, w = 1f + 0.12f * Mathf.Sin(t * 3f + a);
                pts.Add(new Vector3(Mathf.Cos(t) * a * w, 0.026f, Mathf.Sin(t) * b * w));
                rim.Add(new Vector3(Mathf.Cos(t) * (a * w + 0.18f), 0.02f, Mathf.Sin(t) * (b * w + 0.18f)));
            }
            k.B.AddPolygon(rim, mud, Vector3.up);
            k.B.AddPolygon(pts, water, Vector3.up);
            Make(parent, k, "Puddle", new Vector3(at.x, GroundY(at), at.z), Quaternion.Euler(0, yaw, 0));
        }
    }
}
