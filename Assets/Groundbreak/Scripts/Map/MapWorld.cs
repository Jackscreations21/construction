using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>
    /// Builds the 3D full map around the town (Docs/MAP.md) whenever the town is built (<see cref="Town.Built"/>), as its own root
    /// object ("Map"), removed again by <see cref="Town.Destroy"/>. Geometry is merged per 100 m cell (surfaces with a
    /// MeshCollider, markings, buildings, vegetation); every cell has one collider object (boxes for buildings, capsules for trees).
    /// <see cref="MapStreamer"/> hides cells far from the camera (the haze hides them anyway).
    /// </summary>
    public static class MapWorld
    {
        /// <summary>Build the map automatically with the town (tests that only need the core town may switch this off).</summary>
        public static bool AutoBuild = true;
        public static GameObject Root { get; private set; }
        public static bool Exists => Root != null;
        public static event Action Built;

        public struct Stats { public int Cells, Renderers, Triangles, Colliders, Buildings, Trees; public float Seconds; }
        public static Stats Last;

        public const float CellSize = 100f;
        const float Top = Town.CurbHeight;

        public class Cell
        {
            public Vector2Int Key;
            public LandModel Surf = new LandModel(), Mark = new LandModel(), Bld = new LandModel(), Veg = new LandModel();
            public readonly List<(Vector3 min, Vector3 max)> Boxes = new List<(Vector3, Vector3)>();
            public readonly List<Vector4> Trunks = new List<Vector4>();   // xyz base, w = height
            public GameObject Go;
            public Vector3 Center => new Vector3((Key.x + 0.5f) * CellSize, 0, (Key.y + 0.5f) * CellSize);
        }

        static Dictionary<Vector2Int, Cell> _cells;
        static Transform _root;
        static MapBuild _b;
        static int _buildings, _trees;
        /// <summary>Per-lot dressing objects (meadow, for-sale sign) shown only while the lot is free.</summary>
        static readonly List<(MapArea area, GameObject go)> _dressing = new List<(MapArea, GameObject)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Root = null; _cells = null; _dressing.Clear(); Built = null; AutoBuild = true; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            Town.Built -= OnTownBuilt; Town.Built += OnTownBuilt;
            Town.LotsChanged -= RefreshDressing; Town.LotsChanged += RefreshDressing;
            Town.Destroyed -= Destroy; Town.Destroyed += Destroy;
        }

        static void OnTownBuilt() { if (AutoBuild) Build(); }

        /// <summary>Removes the 3D map (also done automatically when the town is destroyed).</summary>
        public static void Destroy()
        {
            if (Root == null) return;
            Root.SetActive(false);
            UnityEngine.Object.Destroy(Root);
            Root = null; _cells = null; _dressing.Clear();
        }

        static Cell CellAt(Vector3 p)
        {
            var k = new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
            if (!_cells.TryGetValue(k, out var c)) { c = new Cell { Key = k }; _cells[k] = c; }
            return c;
        }

        /// <summary>Drawing context in world space for a cell model (frame = identity).</summary>
        static MapBuild Ctx(LandModel k, int seed)
        {
            _b.K = k; _b.Rng = new System.Random(seed); _b.Solids.Clear(); _b.Lights.Clear();
            _b.Frame = Matrix4x4.identity; k.B.ResetTransform();
            return _b;
        }

        /// <summary>Draws a registered model at a world pose into the cell at pos (buildings / props), with colliders.</summary>
        public static void Place(MapBuildingSpec spec, Vector3 pos, Quaternion rot, bool vegetation = false)
        {
            var cell = CellAt(pos);
            var k = vegetation ? cell.Veg : cell.Bld;
            var b = Ctx(k, spec.Seed);
            b.Frame = Matrix4x4.TRS(pos, rot, Vector3.one);
            k.B.Transform = b.Frame;
            MapModels.Draw(b, spec);
            k.B.ResetTransform();
            foreach (var (mn, mx) in b.Solids) cell.Boxes.Add(WorldAabb(b.Frame, mn, mx));
        }

        static (Vector3, Vector3) WorldAabb(Matrix4x4 m, Vector3 mn, Vector3 mx)
        {
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var p = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z));
                lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
            }
            return (lo, hi);
        }

        static MapBuildingSpec Prop(string type, int seed, string sign = "", int units = 1) => new MapBuildingSpec { Type = type, Seed = seed, Sign = sign, Units = units };

        // ------------------------------------------------------------------ build

        public static void Build()
        {
            if (Root != null || !Town.Exists) return;
            float t0 = Time.realtimeSinceStartup;
            MapLayout.Ensure();
            Root = new GameObject("Map");
            _root = Root.transform;
            _cells = new Dictionary<Vector2Int, Cell>();
            _b = new MapBuild();
            _buildings = 0; _trees = 0;
            _dressing.Clear();
            _parkSpots.Clear(); foreach (var l in Town.Lots) _parkSpots.Add(l.Parking);
            Blocking.Build();

            try
            {
                // ranch houses: their sidewalk square is a repair fixture (not part of the street mesh)
                var fixtures = new Dictionary<MapParcel, Vector3>();
                foreach (var p in MapLayout.Parcels)
                {
                    if (!p.Ranch) continue;
                    var r = p.Road; int side = MapRoads.SideOf(r, p.Lot);
                    float sq = 5f * Units.Foot, so = r.Half + Town.CurbWidth + Town.StripWidth, wo = so + Town.SidewalkWidth;
                    float a = r.Along(p.Lot.Local(-5f, 0f));
                    float c = (Mathf.Floor(a / sq) + 0.5f) * sq;
                    var w = r.At(c, side * (so + wo) / 2f, Top);
                    fixtures[p] = w;
                    MapRoads.AddFixtureSquare(r, side, w);
                }
                MapRoads.Build(p => CellAt(p).Surf, p => CellAt(p).Mark);

                var housesRoot = new GameObject("MapHouses").transform; housesRoot.SetParent(_root, false);
                var dressed = new HashSet<TownLot>();
                foreach (var p in MapLayout.Parcels)
                {
                    if (p.Spec == null) continue;
                    if (!dressed.Contains(p.Lot)) { dressed.Add(p.Lot); Site(p); }
                    if (p.Ranch) { HouseBuilder.Build(p.Lot, housesRoot, fixtures[p]); _buildings++; continue; }
                    Place(p.Spec, p.Origin, p.Rotation);
                    _buildings++;
                }
                foreach (var a in MapLayout.Areas) Area(a);
                Creek();
                StreetFurniture();
            }
            catch (Exception e) { Debug.LogError("[Map] build failed: " + e); }
            // open land between everything: meadow patches, weeds, shrubs, trees (MapGapFill)
            try { MapGapFill.Build(p => CellAt(p).Veg, (p, h, s, d) => Tree(p, h, s, d, true)); }
            catch (Exception e) { Debug.LogError("[Map] gap fill failed: " + e); }

            Finish();
            RefreshDressing();
            Root.AddComponent<MapStreamer>();
            Last.Seconds = Time.realtimeSinceStartup - t0;
            Debug.Log($"[Map] built: {Last.Cells} cells, {Last.Renderers} renderers, {Last.Triangles} triangles, {Last.Colliders} colliders, {Last.Buildings} buildings, {Last.Trees} trees in {Last.Seconds:0.00} s");
            _b = null;
            Built?.Invoke();
            try { RepairJobs.Respawn(); } catch (Exception e) { Debug.LogWarning("[Map] repair respawn: " + e.Message); }   // accepted calls at map ranches
        }

        static void Finish()
        {
            int rends = 0, tris = 0, cols = 0;
            foreach (var cell in _cells.Values)
            {
                var go = new GameObject($"Cell {cell.Key.x},{cell.Key.y}");
                go.transform.SetParent(_root, false);
                cell.Go = go;
                void Part(LandModel k, string name, bool collider, ShadowCastingMode shadows)
                {
                    if (k.B.VertexCount == 0) return;
                    var g = k.Make(go.transform, name);
                    var mr = g.GetComponent<MeshRenderer>(); mr.shadowCastingMode = shadows;
                    var mesh = g.GetComponent<MeshFilter>().sharedMesh;
                    for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)mesh.GetIndexCount(i) / 3;
                    rends++;
                    if (collider) { g.AddComponent<MeshCollider>().sharedMesh = mesh; cols++; }
                }
                Part(cell.Surf, "Surfaces", true, ShadowCastingMode.Off);
                Part(cell.Mark, "Markings", false, ShadowCastingMode.Off);
                Part(cell.Bld, "Buildings", false, ShadowCastingMode.On);
                Part(cell.Veg, "Vegetation", false, ShadowCastingMode.On);
                if (cell.Boxes.Count > 0 || cell.Trunks.Count > 0)
                {
                    // colliders stay active when the cell's renderers are hidden by the streamer
                    var cg = new GameObject($"Colliders {cell.Key.x},{cell.Key.y}");
                    cg.transform.SetParent(_root, false);
                    foreach (var (mn, mx) in cell.Boxes) { var bc = cg.AddComponent<BoxCollider>(); bc.center = (mn + mx) / 2f; bc.size = mx - mn; cols++; }
                    foreach (var t in cell.Trunks)
                    {
                        var cc = cg.AddComponent<CapsuleCollider>(); cc.radius = 0.25f; cc.height = Mathf.Min(5f, t.w); cc.center = new Vector3(t.x, t.y + cc.height / 2f, t.z); cols++;
                    }
                }
            }
            foreach (var r in _root.GetComponentsInChildren<MeshRenderer>(true))
                if (r.transform.parent == null || !r.transform.parent.name.StartsWith("Cell"))
                {
                    rends++;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tris += (int)mf.sharedMesh.GetIndexCount(i) / 3;
                }
            Last = new Stats { Cells = _cells.Count, Renderers = rends, Triangles = tris, Colliders = cols, Buildings = _buildings, Trees = _trees };
        }

        /// <summary>Shows (or re-hides by distance on the next streamer tick) every cell.</summary>
        public static void ShowAll(bool show)
        {
            foreach (var c in Cells) if (c.Go != null) c.Go.SetActive(show || !MapStreamer.Frozen);
        }

        /// <summary>Cell objects by key (MapStreamer toggles them).</summary>
        public static IEnumerable<Cell> Cells => _cells != null ? _cells.Values : (IEnumerable<Cell>)Array.Empty<Cell>();

        // ------------------------------------------------------------------ lot site work

        static float GroundY(MapParcel p) => p.Road != null && p.Road.Curbs ? Top : 0.03f;

        static void Site(MapParcel p)
        {
            var lot = p.Lot; var rot = lot.Rotation; float W = lot.Frontage, D = lot.Depth;
            var cell = CellAt(lot.Center);
            var k = cell.Surf;
            float y = GroundY(p);
            int seed = Mathf.Abs(lot.Id.GetHashCode());
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            void SlabLocal(float x0, float x1, float d0, float d1, float h, Material m)
            {
                Vector3 a = lot.Local(x0, d0), b = lot.Local(x1, d1);
                Vector3 mn = Vector3.Min(a, b), mx = Vector3.Max(a, b); mn.y = 0f; mx.y = h;
                var kk = CellAt((mn + mx) / 2f).Surf;
                kk.B.AddBox((mn + mx) / 2f, mx - mn, Quaternion.identity, kk.M(m), Mathf.Abs(lot.Front.x) > 0.5f ? Vector3.right : Vector3.forward);
            }
            var spec = p.Spec;
            float bx0 = p.OffsetX - spec.Width / 2f, bx1 = p.OffsetX + spec.Width / 2f;
            switch (p.Site)
            {
                case MapSite.Driveway:
                {
                    float dx = W / 2f - 1f - Town.DrivewayWidth / 2f;
                    float len = p.Ranch ? TownHouse.Setback : p.Setback + (spec.Type == "ranch2" || spec.Type == "twostory" ? 0f : 4f);
                    SlabLocal(dx - Town.DrivewayWidth / 2f, dx + Town.DrivewayWidth / 2f, 0f, len, y, Materials.Get(SurfaceKind.BroomedConcrete));
                    float doorX = p.Ranch ? TownHouse.FrontDoorX : p.OffsetX;
                    if (!p.Ranch) SlabLocal(Mathf.Min(doorX, dx - Town.DrivewayWidth / 2f) - 0.6f, Mathf.Min(doorX + 0.6f, dx - Town.DrivewayWidth / 2f), p.Setback - 3.2f, p.Setback - 2.0f, y, Materials.Get(SurfaceKind.Concrete));
                    // mailbox at the curb, beside the driveway
                    if (p.Road.Walks)
                    {
                        var mbPos = lot.Local(dx - Town.DrivewayWidth / 2f - 1.2f, -(p.Road.RowHalf - (p.Road.Half + Town.CurbWidth + 0.6f))) + Vector3.up * Top;
                        var mk = CellAt(mbPos).Bld; mk.B.Transform = Matrix4x4.TRS(mbPos, rot, Vector3.one); LandModels.Mailbox(mk); mk.B.ResetTransform();
                    }
                    // yard trees, foundation shrubs, a parked car half the time
                    Tree(lot.Local(-W / 2f + 3f + R() * 3f, 3f + R() * 2.5f), 7f + R() * 5f, seed, true);
                    Tree(lot.Local(-W / 2f + 3f + R() * (W - 6f), D - 4f - R() * 3f), 9f + R() * 6f, seed + 1, R() < 0.25f);
                    if (D > 30f) Tree(lot.Local(W / 2f - 3f - R() * 4f, D - 6f - R() * 4f), 8f + R() * 5f, seed + 2, false);
                    for (float sx = bx0 + 0.6f; sx < bx1 - 0.6f; sx += 1.6f + R() * 0.8f)
                        if (Mathf.Abs(sx - doorX) > 1.2f && sx < dx - Town.DrivewayWidth / 2f - 0.5f) Bush(lot.Local(sx, p.Setback - 0.7f), 0.8f + R() * 0.5f, seed + (int)(sx * 10));
                    if (!p.Ranch && p.Setback >= 13f && R() < 0.6f) Car(lot.Local(dx, p.Setback - 2.6f) + Vector3.up * y, Quaternion.Euler(0, lot.ParkingYaw, 0), seed);
                    if (!p.Ranch && R() < 0.5f && D >= 30f && p.Spec.BackYard > 8f)
                    {
                        // back yard fence
                        var kind = R() < 0.5f ? PropModels.FenceKind.Privacy : R() < 0.5f ? PropModels.FenceKind.ChainLink : PropModels.FenceKind.Picket;
                        Fence(lot.Local(-W / 2f + 0.3f, p.Setback + spec.Depth), lot.Local(-W / 2f + 0.3f, D - 0.3f), kind, seed);
                        Fence(lot.Local(-W / 2f + 0.3f, D - 0.3f), lot.Local(W / 2f - 0.3f, D - 0.3f), kind, seed + 1);
                        Fence(lot.Local(W / 2f - 0.3f, D - 0.3f), lot.Local(W / 2f - 0.3f, p.Setback + spec.Depth), kind, seed + 2);
                    }
                    break;
                }
                case MapSite.ParkingLot:
                {
                    if (p.Setback < 8f) break;                  // the model draws its own forecourt (gas station)
                    float x0 = -W / 2f + 1.5f, x1 = W / 2f - 1.5f, d0 = p.Road.Curbs ? 0f : 0f, d1 = p.Setback - 0.5f;
                    SlabLocal(x0, x1, d0, d1, y, MapMaterials.Asphalt);
                    // stalls: 9' x 18' rows facing the building, a 24' aisle in front of them
                    float stall = 9f * Units.Foot, sd = 18f * Units.Foot;
                    var mark = CellAt(lot.Center).Mark; int white = mark.M(MaterialLibrary.Get(new Color(0.92f, 0.92f, 0.9f)));
                    var rows = new List<(float d, int facing)>();
                    if (d1 - d0 > sd + 7.3f) rows.Add((d1 - 1.2f - sd, 1));
                    if (d1 - d0 > 2 * sd + 2 * 7.3f) { rows.Add((d1 - 1.2f - sd - 7.3f - sd, -1)); rows.Add((d1 - 1.2f - 2 * sd - 7.3f, 1)); }
                    foreach (var (rd, facing) in rows)
                        for (float sx = x0 + 3f; sx + stall < x1 - 3f; sx += stall)
                        {
                            Vector3 a = lot.Local(sx, rd), b = lot.Local(sx, rd + sd);
                            Vector3 mn = Vector3.Min(a, b), mx = Vector3.Max(a, b);
                            mn.y = y + 0.001f; mx.y = y + 0.004f;
                            if (Mathf.Abs(lot.Front.x) > 0.5f) { mn.z -= 0.05f; mx.z += 0.05f; } else { mn.x -= 0.05f; mx.x += 0.05f; }
                            mark.B.AddBox((mn + mx) / 2f, mx - mn, Quaternion.identity, white);
                            if (R() < 0.45f) Car(lot.Local(sx + stall / 2f, rd + sd / 2f) + Vector3.up * y, rot * Quaternion.Euler(0, facing > 0 ? 180f : 0f, 0), seed + (int)(sx * 7 + rd));
                        }
                    if (spec.Type != "grocery") Place(Prop("pylonsign", seed, spec.Sign), lot.Local(-W / 2f + 3f, 1.5f) + Vector3.up * y, rot);
                    for (int i = 0; i < 3; i++) Tree(lot.Local(-W / 2f + 1.2f + (W - 2.4f) * R(), d1 + 1f + R() * 2f), 6f + R() * 3f, seed + 40 + i, true, true);
                    if (spec.Type != "school") Place(Prop("dumpster", seed + 3), lot.Local(bx1 + 3f, p.Setback + spec.Depth - 3f), rot * Quaternion.Euler(0, 90, 0));
                    break;
                }
                case MapSite.Gravel:
                {
                    SlabLocal(-W / 2f + 1f, W / 2f - 1f, 0f, Mathf.Min(D - 1f, p.Setback + spec.Depth + 8f), 0.03f, MapMaterials.Gravel);
                    for (int i = 0; i < 2; i++) if (R() < 0.7f) Car(lot.Local(bx1 + 4f + i * 3f, p.Setback * 0.5f + 2f) + Vector3.up * 0.03f, rot * Quaternion.Euler(0, 180f, 0), seed + i);
                    if (p.Lot.Category == "industrial") Fence(lot.Local(-W / 2f + 0.3f, D - 0.3f), lot.Local(W / 2f - 0.3f, D - 0.3f), PropModels.FenceKind.ChainLink, seed);
                    if (!string.IsNullOrEmpty(spec.Sign) && spec.Type != "watertower") Place(Prop("pylonsign", seed, spec.Sign), lot.Local(-W / 2f + 3f, 1.5f), rot);
                    break;
                }
                case MapSite.Farmyard:
                {
                    float ex = -W / 2f + 26f;
                    SlabLocal(ex - 2.2f, ex + 2.2f, 0f, 70f, 0.03f, MapMaterials.Gravel);
                    SlabLocal(-W / 2f + 12f, -W / 2f + 66f, 34f, 40f, 0.03f, MapMaterials.Gravel);
                    Place(Prop("tractor", seed), lot.Local(ex + 6f, 60f), rot * Quaternion.Euler(0, 200f, 0));
                    Car(lot.Local(ex + 4f, 30f), rot * Quaternion.Euler(0, 160f, 0), seed * 3 + 1);
                    for (int i = 0; i < 4; i++) Tree(lot.Local(-W / 2f + 4f + i * 9f, 10f + R() * 6f), 11f + R() * 5f, seed + 10 + i, true);
                    var mbPos = lot.Local(ex - 3f, -(p.Road.RowHalf - p.Road.Half - 1.5f));
                    var mk = CellAt(mbPos).Bld; mk.B.Transform = Matrix4x4.TRS(mbPos, rot, Vector3.one); LandModels.Mailbox(mk); mk.B.ResetTransform();
                    // fence along the road frontage
                    Fence(lot.Local(-W / 2f + 1f, 1f), lot.Local(ex - 4f, 1f), PropModels.FenceKind.Board, seed);
                    Fence(lot.Local(ex + 4f, 1f), lot.Local(W / 2f - 1f, 1f), PropModels.FenceKind.BarbedWire, seed + 1);
                    for (int i = 0; i < 6; i++) Place(Prop("haybale", seed + i), lot.Local(-W / 2f + 70f + i * 2.2f, 80f + (i % 2) * 2.1f), rot);
                    break;
                }
                case MapSite.Lawn:
                {
                    SlabLocal(p.OffsetX - 1f, p.OffsetX + 1f, 0f, Mathf.Max(0.5f, p.Setback - 0.2f), y, Materials.Get(SurfaceKind.Concrete));
                    if (p.Setback > 6f)
                    {
                        Tree(lot.Local(-W / 2f + 3f, p.Setback * 0.5f), 10f + R() * 4f, seed, true);
                        Tree(lot.Local(W / 2f - 3f, p.Setback * 0.5f), 10f + R() * 4f, seed + 1, true);
                    }
                    for (float sx = bx0 + 0.8f; sx < bx1 - 0.8f; sx += 1.8f) if (Mathf.Abs(sx - p.OffsetX) > 2f) Bush(lot.Local(sx, p.Setback - 0.8f), 0.9f, seed + (int)(sx * 10));
                    break;
                }
            }
            // downtown: rear gravel lot behind the storefronts
            if (spec.Type == "storefront" && D > spec.Depth + 4f)
            {
                SlabLocal(-W / 2f + 0.05f, W / 2f - 0.05f, spec.Depth + 0.05f, D - 0.2f, 0.03f, MapMaterials.Gravel);
                if (R() < 0.4f) Place(Prop("dumpster", seed), lot.Local(0, spec.Depth + 2f) + Vector3.up * 0.03f, rot);
                if (R() < 0.6f) Car(lot.Local(0, spec.Depth + 8f) + Vector3.up * 0.03f, rot * Quaternion.Euler(0, 180, 0), seed + 5);
            }
        }

        // ------------------------------------------------------------------ scenery helpers

        static void Tree(Vector3 pos, float height, int seed, bool deciduous, bool collider = true)
        {
            var cell = CellAt(pos);
            var b = Ctx(cell.Veg, seed);
            if (deciduous) MapTrees.Deciduous(b, pos, height, seed); else MapTrees.Conifer(b, pos, height, seed);
            cell.Veg.B.ResetTransform();
            if (collider) cell.Trunks.Add(new Vector4(pos.x, pos.y, pos.z, height));
            _trees++;
        }

        static void Bush(Vector3 pos, float size, int seed)
        {
            var cell = CellAt(pos);
            var b = Ctx(cell.Veg, seed);
            MapTrees.Bush(b, pos, size, seed);
            cell.Veg.B.ResetTransform();
        }

        static readonly List<Vector3> _parkSpots = new List<Vector3>();

        /// <summary>A parked car — never on a lot's own parking spot (that is where the player's van goes).</summary>
        static void Car(Vector3 pos, Quaternion rot, int seed)
        {
            foreach (var q in _parkSpots) if ((q.x - pos.x) * (q.x - pos.x) + (q.z - pos.z) * (q.z - pos.z) < 5.5f * 5.5f) return;
            Place(Prop("car", seed), pos, rot);
        }

        static void Fence(Vector3 a, Vector3 c, PropModels.FenceKind kind, int seed)
        {
            var cell = CellAt((a + c) * 0.5f);
            var b = Ctx(cell.Bld, seed);
            PropModels.Fence(b, a, c, kind);
            cell.Bld.B.ResetTransform();
        }

        // ------------------------------------------------------------------ areas: fields, meadows, woods, ponds, hills

        static void Area(MapArea a)
        {
            switch (a.Kind)
            {
                case AreaKind.Field:
                    foreach (var r in a.Patch ? Blocking.Clip(a.R, true) : Blocking.Clip(a.R, false)) if (r.width > 8f && r.height > 8f) Field(r, a.Field, a.RowsAlongX, a.Seed, null);
                    break;
                case AreaKind.Meadow:
                {
                    var go = new GameObject("Dressing " + (a.LotId ?? "meadow"));
                    go.transform.SetParent(_root, false);
                    var k = new LandModel();
                    Field(a.R, a.Field, true, a.Seed, k);
                    var dlot = string.IsNullOrEmpty(a.LotId) ? null : Town.Find(a.LotId);
                    if (!string.IsNullOrEmpty(a.LotId) && (dlot == null || dlot.External))   // core free lots already have TownBuilder's sign
                    {
                        var lot = dlot;
                        Vector3 sp; Quaternion sr;
                        if (lot != null) { sp = lot.Local(-Mathf.Min(6f, lot.Frontage / 2f - 2f), 2.5f); sr = lot.Rotation; }
                        else { sp = new Vector3(a.R.center.x, 0, a.R.yMin + 3f); sr = Quaternion.LookRotation(Vector3.back); }
                        var b = Ctx(k, a.Seed); b.Frame = Matrix4x4.TRS(sp, sr, Vector3.one); k.B.Transform = b.Frame;
                        ForSaleSign(b, lot == null || lot.AreaSqFt > 30000f);
                        k.B.ResetTransform();
                        var rng = new System.Random(a.Seed);
                        for (int i = 0; i < 3; i++)
                        {
                            var bp = new Vector3(Mathf.Lerp(a.R.xMin + 2f, a.R.xMax - 2f, (float)rng.NextDouble()), 0, Mathf.Lerp(a.R.yMin + 2f, a.R.yMax - 2f, (float)rng.NextDouble()));
                            var bb = Ctx(k, a.Seed + i); MapTrees.Bush(bb, bp, 0.7f + (float)rng.NextDouble() * 0.6f, a.Seed + i); k.B.ResetTransform();
                        }
                    }
                    if (k.B.VertexCount > 0) { var g = k.Make(go.transform, "Meadow"); g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off; }
                    if (!string.IsNullOrEmpty(a.LotId)) _dressing.Add((a, go));
                    break;
                }
                case AreaKind.Woods:
                {
                    var rng = new System.Random(a.Seed * 7919 + 17);
                    foreach (var r in a.Patch ? Blocking.Clip(a.R, true) : new List<Rect> { a.R })
                    {
                        int n = Mathf.RoundToInt(r.width * r.height / 100f * a.Density);
                        for (int i = 0; i < n; i++)
                        {
                            var p = new Vector3(Mathf.Lerp(r.xMin, r.xMax, (float)rng.NextDouble()), 0, Mathf.Lerp(r.yMin, r.yMax, (float)rng.NextDouble()));
                            if (Blocking.Blocked(p)) continue;
                            float h = 9f + (float)rng.NextDouble() * 10f;
                            Tree(p, h, rng.Next(), rng.NextDouble() > 0.28, true);
                            if (rng.NextDouble() < 0.25) Bush(p + new Vector3((float)rng.NextDouble() * 3f - 1.5f, 0, (float)rng.NextDouble() * 3f - 1.5f), 1.2f + (float)rng.NextDouble(), rng.Next());
                        }
                    }
                    break;
                }
                case AreaKind.Pond: Pond(a); break;
                case AreaKind.Hill: Hill(a); break;
            }
        }

        static void ForSaleSign(MapBuild b, bool land)
        {
            int post = b.K.M(SurfaceKind.PineLumber), red = b.K.M(new Color(0.75f, 0.1f, 0.08f)), wht = b.K.M(new Color(0.95f, 0.95f, 0.93f));
            float w = land ? 1.8f : 0.61f, h = land ? 1.2f : 0.46f, top = land ? 2.2f : 1.2f;
            for (int s = land ? -1 : 0; s <= (land ? 1 : 0); s += 2)
                b.Box(new Vector3(s * w * 0.4f - 0.045f, 0, -0.045f), new Vector3(s * w * 0.4f + 0.045f, top, 0.045f), post, Vector3.up);
            b.Box(new Vector3(-w / 2f, top - h - 0.05f, 0.05f), new Vector3(w / 2f, top - 0.05f, 0.07f), red);
            MapText.Draw(b, land ? "LAND FOR SALE" : "FOR SALE", new Vector3(0, top - h * 0.6f, 0.07f), land ? 0.022f : 0.012f, wht);
        }

        /// <summary>Flat crop field / meadow with real 3D crop rows for corn and soybeans, round bales on hay.</summary>
        static void Field(Rect r, FieldKind kind, bool rowsAlongX, int seed, LandModel into)
        {
            const float y = 0.025f;
            // split into ≤ 100 m tiles so each lands in one cell
            for (float x = r.xMin; x < r.xMax - 0.01f; x += CellSize)
                for (float z = r.yMin; z < r.yMax - 0.01f; z += CellSize)
                {
                    float x1 = Mathf.Min(r.xMax, x + CellSize), z1 = Mathf.Min(r.yMax, z + CellSize);
                    var c = new Vector3((x + x1) / 2f, 0, (z + z1) / 2f);
                    var k = into ?? CellAt(c).Surf;
                    Vector3 u = rowsAlongX ? Vector3.right : Vector3.forward;
                    k.B.AddQuad(new Vector3(x, y, z), new Vector3(x, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z), k.M(MapMaterials.Field(kind)), Vector3.up, u);
                    var veg = into ?? CellAt(c).Veg;
                    if (kind == FieldKind.Corn || kind == FieldKind.Soybeans || kind == FieldKind.Wheat)
                    {
                        float spacing = 30f * Units.Inch, w = kind == FieldKind.Corn ? 0.42f : 0.55f, h = kind == FieldKind.Corn ? 2.1f : kind == FieldKind.Wheat ? 0.85f : 0.55f;
                        Color tint = kind == FieldKind.Corn ? new Color(0.62f, 0.80f, 0.42f) : kind == FieldKind.Wheat ? new Color(1.1f, 0.92f, 0.55f) : new Color(0.48f, 0.66f, 0.36f);
                        int sub = veg.M(LandSurface.Foliage, tint);
                        if (kind == FieldKind.Wheat)
                        {
                            veg.B.AddBox(new Vector3((x + x1) / 2f, h / 2f, (z + z1) / 2f), new Vector3(x1 - x - 1f, h, z1 - z - 1f), Quaternion.identity, veg.M(MapMaterials.Field(FieldKind.Wheat)), u);
                            continue;
                        }
                        float len0 = rowsAlongX ? x : z, len1 = rowsAlongX ? x1 : z1, a0 = rowsAlongX ? z : x, a1 = rowsAlongX ? z1 : x1;
                        for (float a = a0 + spacing; a < a1 - spacing * 0.5f; a += spacing)
                        {
                            Vector3 center = rowsAlongX ? new Vector3((len0 + len1) / 2f, h / 2f, a) : new Vector3(a, h / 2f, (len0 + len1) / 2f);
                            Vector3 size = rowsAlongX ? new Vector3(len1 - len0 - 1f, h, w) : new Vector3(w, h, len1 - len0 - 1f);
                            veg.B.AddBox(center, size, Quaternion.identity, sub, u);
                        }
                    }
                    else if (kind == FieldKind.Hay && into == null)
                    {
                        var rng = new System.Random(seed + (int)x * 31 + (int)z);
                        int n = Mathf.RoundToInt((x1 - x) * (z1 - z) / 900f);
                        for (int i = 0; i < n; i++)
                        {
                            var p = new Vector3(Mathf.Lerp(x + 3f, x1 - 3f, (float)rng.NextDouble()), 0, Mathf.Lerp(z + 3f, z1 - 3f, (float)rng.NextDouble()));
                            Place(Prop("haybale", rng.Next()), p, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), true);
                        }
                    }
                }
        }

        static void Pond(MapArea a)
        {
            Vector3 c = new Vector3(a.R.center.x, 0, a.R.center.y);
            float rx = a.R.width / 2f, rz = a.R.height / 2f;
            var cell = CellAt(c); var k = cell.Surf;
            int water = k.M(MapMaterials.Water), mud = k.M(SurfaceKind.Dirt), grass = k.M(SurfaceKind.Grass);
            const int N = 32;
            var pts = new List<Vector3>();
            for (int i = 0; i < N; i++)
            {
                float t = i * Mathf.PI * 2f / N, wob = 1f + 0.08f * Mathf.Sin(t * 3f + a.Seed) + 0.05f * Mathf.Sin(t * 5f);
                pts.Add(c + new Vector3(Mathf.Cos(t) * rx * wob, 0.06f, Mathf.Sin(t) * rz * wob));
            }
            k.B.AddPolygon(pts, water, Vector3.up);
            for (int i = 0; i < N; i++)
            {
                Vector3 p0 = pts[i], p1 = pts[(i + 1) % N];
                Vector3 o0 = c + (p0 - c) * 1.12f, o1 = c + (p1 - c) * 1.12f, g0 = c + (p0 - c) * 1.3f, g1 = c + (p1 - c) * 1.3f;
                o0.y = o1.y = 0.28f; g0.y = g1.y = 0.01f; p0.y = p1.y = 0.04f;
                k.B.AddQuad(p0, p1, o1, o0, mud, Vector3.up);
                k.B.AddQuad(o0, o1, g1, g0, grass, Vector3.up);
                if (i % 3 == 0) Bush(c + (pts[i] - c) * 1.18f, 0.9f, a.Seed + i);
            }
            Place(Prop("bench", a.Seed), c + new Vector3(0, 0, -rz * 1.45f), Quaternion.identity);
        }

        static float HillH(MapArea a, float x, float z)
        {
            float dx = (x - a.R.center.x) / (a.R.width / 2f), dz = (z - a.R.center.y) / (a.R.height / 2f);
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d >= 1f) return 0f;
            float s = Mathf.Cos(d * Mathf.PI) * 0.5f + 0.5f;
            // hills only rise outside the playable map (roads and fields at the edge stay at grade)
            var bd = MapLayout.Bounds;
            float ox = Mathf.Max(bd.xMin - x, x - bd.xMax, 0f), oz = Mathf.Max(bd.yMin - z, z - bd.yMax, 0f);
            s *= Mathf.SmoothStep(0f, 1f, (Mathf.Sqrt(ox * ox + oz * oz) - 30f) / 80f);
            return a.Height * s * (0.85f + 0.15f * Mathf.Sin(x * 0.02f + a.Seed) * Mathf.Cos(z * 0.017f));
        }

        static void Hill(MapArea a)
        {
            var k = new LandModel();
            int sub = k.M(MapMaterials.Field(FieldKind.Pasture));
            const int N = 20;
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    float x0 = a.R.xMin + a.R.width * i / N, x1 = a.R.xMin + a.R.width * (i + 1) / N, z0 = a.R.yMin + a.R.height * j / N, z1 = a.R.yMin + a.R.height * (j + 1) / N;
                    k.B.AddQuad(new Vector3(x0, HillH(a, x0, z0), z0), new Vector3(x0, HillH(a, x0, z1), z1), new Vector3(x1, HillH(a, x1, z1), z1), new Vector3(x1, HillH(a, x1, z0), z0), sub, Vector3.up);
                }
            var go = k.Make(_root, "Hill");
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            // wooded crest
            var rng = new System.Random(a.Seed * 31);
            var veg = new LandModel();
            for (int i = 0; i < 260; i++)
            {
                float x = Mathf.Lerp(a.R.xMin, a.R.xMax, (float)rng.NextDouble()), z = Mathf.Lerp(a.R.yMin, a.R.yMax, (float)rng.NextDouble());
                float h = HillH(a, x, z);
                if (h < a.Height * 0.15f) continue;
                var b = Ctx(veg, rng.Next());
                if (rng.NextDouble() < 0.6) MapTrees.Deciduous(b, new Vector3(x, h - 0.2f, z), 12f + (float)rng.NextDouble() * 8f, rng.Next());
                else MapTrees.Conifer(b, new Vector3(x, h - 0.2f, z), 14f + (float)rng.NextDouble() * 8f, rng.Next());
                veg.B.ResetTransform();
                _trees++;
            }
            if (veg.B.VertexCount > 0) veg.Make(go.transform, "HillTrees").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ creek

        static void Creek()
        {
            var pts = MapLayout.Creek;
            if (pts.Count < 2) return;
            var rng = new System.Random(77);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                int n = Mathf.CeilToInt((b - a).magnitude / 6f);
                for (int s = 0; s < n; s++)
                {
                    Vector3 p0 = Vector3.Lerp(a, b, s / (float)n), p1 = Vector3.Lerp(a, b, (s + 1) / (float)n);
                    var mid = (p0 + p1) * 0.5f;
                    var road = Blocking.RoadAt(mid);
                    var dir = (p1 - p0).normalized; var side = new Vector3(dir.z, 0, -dir.x);
                    var k = CellAt(mid).Surf;
                    if (road != null)
                    {
                        // culvert headwalls at the right-of-way lines
                        continue;
                    }
                    float hw = 1.6f + 0.4f * Mathf.Sin(mid.z * 0.05f);
                    void Strip(float o0, float y0, float o1, float y1, Material m) =>
                        k.B.AddQuad(p0 + side * o0 + Vector3.up * y0, p1 + side * o0 + Vector3.up * y0, p1 + side * o1 + Vector3.up * y1, p0 + side * o1 + Vector3.up * y1, k.M(m), Vector3.up, dir);
                    Strip(-hw, 0.05f, hw, 0.05f, MapMaterials.Water);
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        Strip(sd * hw, 0.03f, sd * (hw + 0.9f), 0.35f, Materials.Get(SurfaceKind.Dirt));
                        Strip(sd * (hw + 0.9f), 0.35f, sd * (hw + 2.6f), 0.02f, Materials.Get(SurfaceKind.Grass));
                    }
                    if (s % 2 == 0)
                    {
                        int sd2 = rng.NextDouble() < 0.5 ? -1 : 1;
                        var tp = mid + side * sd2 * (hw + 4f + (float)rng.NextDouble() * 8f);
                        if (!Blocking.Blocked(tp)) Tree(tp, 10f + (float)rng.NextDouble() * 9f, rng.Next(), true);
                        if (rng.NextDouble() < 0.6) Bush(mid + side * -sd2 * (hw + 1.8f), 1.1f, rng.Next());
                    }
                }
            }
            // culverts where the creek crosses a road
            foreach (var r in MapLayout.Roads)
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    float pa = r.EW ? a.z : a.x, pb = r.EW ? b.z : b.x;
                    if ((pa - r.Perp) * (pb - r.Perp) > 0f) continue;
                    float t = (r.Perp - pa) / (pb - pa);
                    var x = Vector3.Lerp(a, b, t);
                    float along = r.Along(x);
                    if (along < r.Min || along > r.Max) continue;
                    var k = CellAt(x).Surf; int conc = k.M(SurfaceKind.Concrete);
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        var c = r.At(along, sd * (r.Half + 2.2f), 0.4f);
                        Vector3 size = r.EW ? new Vector3(5f, 0.8f, 0.3f) : new Vector3(0.3f, 0.8f, 5f);
                        k.B.AddBox(c, size, Quaternion.identity, conc);
                    }
                }
        }

        // ------------------------------------------------------------------ street furniture

        static void StreetFurniture()
        {
            int seed = 500;
            foreach (var r in MapLayout.Roads)
            {
                if (r.Core) continue;
                var js = new List<float>();
                foreach (var j in MapRoads.Junctions) if (j.Roads.Contains(r)) js.Add(r.EW ? j.P.x : j.P.z);
                // driveway centres per side (to keep poles / lights out of them)
                var drives = new List<(float, int)>();
                foreach (var p in MapLayout.Parcels)
                    if (p.Road == r && p.Spec != null)
                    {
                        int side = MapRoads.SideOf(r, p.Lot);
                        if (p.Site == MapSite.Driveway) drives.Add((r.Along(p.Lot.Local(p.Lot.Frontage / 2f - 1f - Town.DrivewayWidth / 2f, 0)), side));
                        foreach (float ex in MapRoads.Entrances(p)) if (p.Site != MapSite.Driveway && p.Site != MapSite.None && p.Site != MapSite.Lawn) drives.Add((r.Along(p.Lot.Local(ex, 0)), side));
                    }
                bool Clear(float a, int side, float m)
                {
                    foreach (var j in js) if (Mathf.Abs(a - j) < 14f) return false;
                    foreach (var (d, s) in drives) if (s == side && Mathf.Abs(a - d) < m) return false;
                    return a > r.Min + 6f && a < r.Max - 6f && MapLayout.Bounds.Contains(new Vector2(r.At(a).x, r.At(a).z));
                }
                Quaternion Facing(int side) => Quaternion.LookRotation(r.EW ? new Vector3(0, 0, -side) : new Vector3(-side, 0, 0));
                float stripOff = r.Half + Town.CurbWidth + Town.StripWidth / 2f;
                switch (r.Class)
                {
                    case RoadClass.Downtown:
                    {
                        for (int side = -1; side <= 1; side += 2)
                            for (float a = r.Min + 8f; a < r.Max - 8f; a += 12.2f)
                            {
                                if (!Clear(a, side, 0f)) continue;
                                var tp = r.At(a, side * (r.Half + Town.CurbWidth + 0.9f), Top);
                                Tree(tp, 7f, seed++, true);
                                if (((int)(a / 12.2f)) % 2 == 0) Place(Prop("streetlight", seed++), r.At(a + 6f, side * (r.Half + Town.CurbWidth + 0.5f), Top), Facing(side));
                                else Place(Prop(seed % 3 == 0 ? "trashcan" : "bench", seed++), r.At(a + 6f, side * (r.Half + Town.CurbWidth + 1.6f), Top), Facing(-side));
                            }
                        // parallel-parked cars
                        var rng = new System.Random(r.GetHashCode());
                        for (int side = -1; side <= 1; side += 2)
                            for (float a = r.Min + 12f; a < r.Max - 10f; a += 6.7f)
                            {
                                if (!Clear(a, side, 0f) || rng.NextDouble() > 0.6) continue;
                                float yaw = r.EW ? (side < 0 ? 90f : 270f) : (side < 0 ? 0f : 180f);
                                Car(r.At(a + 3.35f, side * (r.Half - 1.25f), 0.02f), Quaternion.Euler(0, yaw, 0), rng.Next());
                            }
                        break;
                    }
                    case RoadClass.Residential:
                    case RoadClass.Industrial:
                    {
                        int k = 0;
                        for (float a = r.Min + 20f; a < r.Max - 10f; a += 48f, k++)
                        {
                            int side = k % 2 == 0 ? 1 : -1;
                            float aa = a; int tries = 0;
                            while (!Clear(aa, side, 3.5f) && tries++ < 6) aa += 3f;
                            if (tries <= 6) Place(Prop("streetlight", seed++), r.At(aa, side * stripOff, Top), Facing(side));
                            // power poles on the other side, wires between them
                        }
                        Poles(r, -1, r.Class == RoadClass.Residential ? r.Half + Town.CurbWidth + Town.StripWidth / 2f : r.RowHalf - 1.5f, Top, Clear, ref seed);
                        for (float a = r.Min + 60f; a < r.Max - 10f; a += 110f)
                            if (Clear(a, 1, 3f)) Place(Prop("hydrant", seed++), r.At(a, stripOff, Top), Facing(1));
                        break;
                    }
                    default:
                        Poles(r, 1, r.RowHalf - 1.2f, 0f, Clear, ref seed);
                        break;
                }
            }
            // junction signs: stop signs on minor approaches, street-name blades at one corner
            foreach (var j in MapRoads.Junctions)
            {
                MapRoad ew = null, ns = null;
                foreach (var r in j.Roads) { if (r.EW) ew = ew ?? r; else ns = ns ?? r; }
                if (ew == null || ns == null) continue;
                Vector3 corner = j.P + new Vector3(j.RowNS - 0.8f, j.CurbNS && j.CurbEW ? Top : 0f, j.RowEW - 0.8f);
                if (!j.CurbNS || !j.CurbEW) corner = j.P + new Vector3(j.HwNS + 2.5f, 0, j.HwEW + 2.5f);
                Place(Prop("streetsign", seed++, ew.Name.ToUpperInvariant() + "|" + ns.Name.ToUpperInvariant()), corner, Quaternion.identity);
                // which road stops: the lower class one (or the stem of a T)
                bool ewMajor = Rank(ew) > Rank(ns) || (Rank(ew) == Rank(ns) && j.E && j.W && !(j.N && j.S));
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    bool arm = dx > 0 ? j.E : dx < 0 ? j.W : dz > 0 ? j.N : j.S;
                    if (!arm) continue;
                    bool armIsEW = dx != 0;
                    if (armIsEW == ewMajor) continue;
                    var d = new Vector3(dx, 0, dz);
                    var right = new Vector3(-d.z, 0, d.x);     // right-hand side for traffic heading toward the junction (-d)
                    float along = (armIsEW ? j.RowNS : j.RowEW) + 1.0f, off = (armIsEW ? j.HwEW : j.HwNS) + 1.0f;
                    bool curbed = armIsEW ? j.CurbEW : j.CurbNS;
                    var pos = j.P + d * along + right * off + Vector3.up * (curbed ? Top : 0f);
                    Place(Prop("stopsign", seed++), pos, Quaternion.LookRotation(d));
                }
            }
        }

        static int Rank(MapRoad r) => r.Class == RoadClass.Highway ? 4 : r.Class == RoadClass.Downtown ? 3 : r.Class == RoadClass.Rural ? 1 : 2;

        /// <summary>Wood utility poles every ~45 m along one side, three conductors + a neutral between consecutive poles.</summary>
        static void Poles(MapRoad r, int side, float off, float y, Func<float, int, float, bool> clear, ref int seed)
        {
            Vector3? prev = null;
            int n = 0;
            for (float a = r.Min + 15f; a < r.Max - 5f; a += 45f)
            {
                float aa = a; int tries = 0;
                while (!clear(aa, side, 3f) && tries++ < 8) aa += 2.5f;
                if (tries > 8) { prev = null; continue; }
                var pos = r.At(aa, side * off, y);
                if (Blocking.Blocked(pos, true)) { prev = null; continue; }
                // the crossarm (model X) must cross the line: model +Z runs along the road
                var rot = Quaternion.LookRotation(r.EW ? Vector3.right : Vector3.forward);
                Place(Prop("powerpole", seed++, "", n % 5 == 2 ? 2 : 1), pos, rot);
                if (prev.HasValue)
                {
                    var cell = CellAt((prev.Value + pos) * 0.5f);
                    var b = Ctx(cell.Bld, seed);
                    int wire = cell.Bld.M(new Color(0.12f, 0.12f, 0.12f));
                    foreach (float x in PropModels.ConductorX)
                    {
                        var at = rot * new Vector3(x, PropModels.ConductorY, PropModels.ConductorZ);
                        PropModels.Wire(b, prev.Value + at, pos + at, 0.6f, wire);
                    }
                    var nt = rot * PropModels.NeutralPos;
                    PropModels.Wire(b, prev.Value + nt, pos + nt, 0.7f, wire);
                    var cm = rot * PropModels.CommPos;
                    PropModels.Wire(b, prev.Value + cm, pos + cm, 0.8f, wire);
                    cell.Bld.B.ResetTransform();
                }
                prev = pos; n++;
            }
        }

        /// <summary>Height of the crossarm insulator tops above the pole base (matches PropModels "powerpole").</summary>
        public static float PoleTop = 11.4f;

        // ------------------------------------------------------------------ free-lot dressing visibility

        static float _nextDevPoll;

        public static void RefreshDressing()
        {
            if (Root == null) return;
            HashSet<string> devFree = null;
            foreach (var (a, go) in _dressing)
            {
                if (go == null) continue;
                var lot = Town.Find(a.LotId);
                bool show;
                if (lot != null) show = lot.Use == LotUse.Free;
                else
                {
                    if (devFree == null)
                    {
                        devFree = new HashSet<string>();
                        try { foreach (var d in DevLots.Provider.LandForSale()) devFree.Add(d.Id); } catch { }
                    }
                    show = devFree.Contains(a.LotId) || devFree.Count == 0;
                }
                if (go.activeSelf != show) go.SetActive(show);
            }
        }

        internal static void PollDressing()
        {
            if (Time.unscaledTime < _nextDevPoll) return;
            _nextDevPoll = Time.unscaledTime + 3f;
            RefreshDressing();
        }
    }

    /// <summary>Hides map cells far from the camera (beyond the haze) and keeps free-lot dressing in sync.</summary>
    public class MapStreamer : MonoBehaviour
    {
        public static float ShowDistance = 460f;
        /// <summary>Stop streaming (screenshots / tests that look from far away; use <see cref="MapWorld.ShowAll"/>).</summary>
        public static bool Frozen;
        float _next;

        void Update()
        {
            MapWorld.PollDressing();
            if (Frozen || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position; p.y = 0;
            float d2 = (ShowDistance + MapWorld.CellSize * 0.71f) * (ShowDistance + MapWorld.CellSize * 0.71f);
            foreach (var c in MapWorld.Cells)
            {
                if (c.Go == null) continue;
                bool show = (c.Center - p).sqrMagnitude < d2;
                if (c.Go.activeSelf != show) c.Go.SetActive(show);
            }
        }
    }

    /// <summary>Coarse 4 m occupancy grid of roads, lots and the build site (keeps scatter trees off them) + rect clipping for fields.</summary>
    public static class Blocking
    {
        const float G = 4f;
        static bool[] _grid, _roadGrid;
        static int _nx, _nz;
        static Rect _b;
        static readonly List<Rect> _roads = new List<Rect>(), _lots = new List<Rect>();

        public static void Build()
        {
            _b = MapLayout.Bounds;
            _nx = Mathf.CeilToInt(_b.width / G); _nz = Mathf.CeilToInt(_b.height / G);
            _grid = new bool[_nx * _nz]; _roadGrid = new bool[_nx * _nz];
            _roads.Clear(); _lots.Clear();
            foreach (var r in MapLayout.Roads)
            {
                float m = r.RowHalf + 1f;
                var rr = r.EW ? Rect.MinMaxRect(r.Min - 1f, r.Perp - m, r.Max + 1f, r.Perp + m) : Rect.MinMaxRect(r.Perp - m, r.Min - 1f, r.Perp + m, r.Max + 1f);
                _roads.Add(rr); Fill(rr, true);
            }
            foreach (var l in Town.Lots)
            {
                var p = MapLayout.ParcelOf(l);
                if (p != null && p.Lot.Category == "farm")
                {
                    // farms: only the farmstead corner (house, barns, yard) blocks trees; the fields are the farm's own areas
                    Vector3 a = l.Local(-l.Frontage / 2f, 0f), c = l.Local(-l.Frontage / 2f + 90f, 92f);
                    Fill(Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.z, c.z), Mathf.Max(a.x, c.x), Mathf.Max(a.z, c.z)), false);
                }
                else Fill(l.Rect, false);
                _lots.Add(l.Rect);
            }
            Fill(Town.BuildSiteRect, false);
            foreach (var a in MapLayout.Areas) if (a.Kind == AreaKind.Pond) Fill(new Rect(a.R.x - 4f, a.R.y - 4f, a.R.width + 8f, a.R.height + 8f), false);
            var cr = MapLayout.Creek;
            for (int i = 0; i < cr.Count - 1; i++)
                for (float t = 0; t <= 1f; t += 0.02f)
                {
                    var p = Vector3.Lerp(cr[i], cr[i + 1], t);
                    Fill(new Rect(p.x - 3.5f, p.z - 3.5f, 7f, 7f), false);
                }
        }

        static void Fill(Rect r, bool road)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((r.xMin - _b.xMin) / G), 0, _nx - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((r.xMax - _b.xMin) / G), 0, _nx - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((r.yMin - _b.yMin) / G), 0, _nz - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((r.yMax - _b.yMin) / G), 0, _nz - 1);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) { _grid[z * _nx + x] = true; if (road) _roadGrid[z * _nx + x] = true; }
        }

        /// <summary>True on roads, lots, the build site, ponds and the creek (roadsOnly: only road corridors).</summary>
        public static bool Blocked(Vector3 p, bool roadsOnly = false)
        {
            if (_grid == null) return false;
            int x = Mathf.FloorToInt((p.x - _b.xMin) / G), z = Mathf.FloorToInt((p.z - _b.yMin) / G);
            if (x < 0 || z < 0 || x >= _nx || z >= _nz) return false;
            return roadsOnly ? _roadGrid[z * _nx + x] : _grid[z * _nx + x];
        }

        public static MapRoad RoadAt(Vector3 p)
        {
            foreach (var r in MapLayout.Roads)
            {
                float along = r.Along(p), perp = r.EW ? p.z : p.x;
                if (along >= r.Min - 1f && along <= r.Max + 1f && Mathf.Abs(perp - r.Perp) <= r.RowHalf + 1f) return r;
            }
            return null;
        }

        /// <summary>The parts of r that are clear of every road corridor (and of every lot when lots = true).</summary>
        public static List<Rect> Clip(Rect r, bool lots)
        {
            var cur = new List<Rect> { r };
            void Sub(Rect cut)
            {
                var next = new List<Rect>();
                foreach (var a in cur)
                {
                    if (!a.Overlaps(cut)) { next.Add(a); continue; }
                    if (cut.yMin > a.yMin) next.Add(Rect.MinMaxRect(a.xMin, a.yMin, a.xMax, cut.yMin));
                    if (cut.yMax < a.yMax) next.Add(Rect.MinMaxRect(a.xMin, cut.yMax, a.xMax, a.yMax));
                    float y0 = Mathf.Max(a.yMin, cut.yMin), y1 = Mathf.Min(a.yMax, cut.yMax);
                    if (cut.xMin > a.xMin) next.Add(Rect.MinMaxRect(a.xMin, y0, cut.xMin, y1));
                    if (cut.xMax < a.xMax) next.Add(Rect.MinMaxRect(cut.xMax, y0, a.xMax, y1));
                }
                cur = next;
            }
            foreach (var rr in _roads) Sub(rr);
            if (lots) { foreach (var l in _lots) Sub(l); Sub(Town.BuildSiteRect); }
            return cur;
        }
    }
}
