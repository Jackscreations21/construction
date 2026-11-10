using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>What covers a ground cell.</summary>
    public enum GroundCover : byte { Grass, Dirt, Topsoil, Seeded, Mulch, Gravel, Sand, Lawn }

    /// <summary>
    /// Editable terrain for the site. Created lazily around the first grading edit (or the career lot); the flat 2000' ground
    /// plane gets a matching hole so the field can be cut below zero. Heights are meters at 0.5 m vertices; each cell has a
    /// <see cref="GroundCover"/>. Outside the field the ground is flat at y = 0.
    /// </summary>
    public static class Grade
    {
        public static GradeField Field { get; private set; }
        public const float Cell = 0.5f;
        public const int DefaultCells = 160; // 80 m = 262'

        public static float HeightAt(float x, float z) => Field != null ? Field.HeightAt(x, z) : 0f;
        public static GroundCover CoverAt(float x, float z) => Field != null ? Field.CoverAt(x, z) : GroundCover.Grass;
        public static Vector2 Gradient(float x, float z) => Field != null ? Field.Gradient(x, z) : Vector2.zero;

        /// <summary>Makes sure an editable field covers this point. Returns false if a field exists elsewhere and the point is outside it.</summary>
        public static bool Ensure(Vector3 near)
        {
            if (Field != null) return Field.Contains(near.x, near.z);
            Vector3 c = Site.Lot.HasValue ? Site.LotCenter : near;
            int cells = DefaultCells;
            if (Site.Lot.HasValue) cells = Mathf.Max(DefaultCells, Mathf.CeilToInt((Mathf.Max(Site.Lot.Value.width, Site.Lot.Value.height) + 30f) / Cell));
            float size = cells * Cell;
            float x0 = Mathf.Round((c.x - size / 2f) / 5f) * 5f, z0 = Mathf.Round((c.z - size / 2f) / 5f) * 5f;
            Create(x0, z0, cells);
            return Field.Contains(near.x, near.z);
        }

        public static void Create(float x0, float z0, int cells)
        {
            Destroy();
            var go = new GameObject("GradeField");
            Field = go.AddComponent<GradeField>();
            Field.Init(x0, z0, cells);
            CutGroundHole(new Rect(x0, z0, cells * Cell, cells * Cell));
        }

        public static void Destroy()
        {
            if (Field != null) UnityEngine.Object.Destroy(Field.gameObject);
            Field = null;
            RestoreGround();
        }

        // ------------------------------------------------------------------ ground plane hole

        static Mesh _origGround;

        static void CutGroundHole(Rect hole)
        {
            var g = GameObject.Find("Ground");
            if (g == null) return;
            var mf = g.GetComponent<MeshFilter>();
            if (mf == null) return;
            if (_origGround == null) _origGround = mf.sharedMesh;
            float scale = g.transform.lossyScale.x;
            float half = 5f * scale; // Unity plane is 10 units across
            float groundSize = half * 2f;
            var b = new MeshBuilder();
            void Rq(float ax, float az, float bx, float bz)
            {
                if (bx - ax < 0.001f || bz - az < 0.001f) return;
                b.AddQuad(new Vector3(ax, 0, az), new Vector3(ax, 0, bz), new Vector3(bx, 0, bz), new Vector3(bx, 0, az), 0, Vector3.up);
            }
            Rq(-half, -half, half, hole.yMin);
            Rq(-half, hole.yMax, half, half);
            Rq(-half, hole.yMin, hole.xMin, hole.yMax);
            Rq(hole.xMax, hole.yMin, half, hole.yMax);
            var mesh = b.Build("GroundWithHole");
            // match the plane's material tiling: its texture repeats groundSize/TileMeters times over UV 0..1
            var v = mesh.vertices; var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++) { uv[i] = new Vector2(v[i].x / groundSize, v[i].z / groundSize); v[i] /= scale; }
            mesh.vertices = v; mesh.uv = uv; mesh.RecalculateBounds(); mesh.RecalculateTangents();
            mf.sharedMesh = mesh;
            var mc = g.GetComponent<MeshCollider>();
            if (mc != null) mc.sharedMesh = mesh;
        }

        static void RestoreGround()
        {
            if (_origGround == null) return;
            var g = GameObject.Find("Ground");
            if (g == null) return;
            g.GetComponent<MeshFilter>().sharedMesh = _origGround;
            var mc = g.GetComponent<MeshCollider>();
            if (mc != null) mc.sharedMesh = _origGround;
        }

        // ------------------------------------------------------------------ dirt accounting

        /// <summary>Loose dirt on hand (cubic yards): digging adds, filling uses; more comes from "fill-dirt" stock (1 yd³ each).</summary>
        public static float DirtOnHand;
        public const float CubicYard = 0.764555f; // m³

        /// <summary>Takes dirt for filling (m³). Uses dirt on hand, then fill-dirt stock.</summary>
        public static bool TakeFill(float m3)
        {
            float yd = m3 / CubicYard;
            while (DirtOnHand < yd && Game.Stock.TryTake("fill-dirt", 1)) DirtOnHand += 1f;
            if (DirtOnHand < yd) return false;
            DirtOnHand -= yd;
            return true;
        }

        // ------------------------------------------------------------------ save

        [Serializable] class Data { public float x0, z0; public int cells; public string heights, cover; public float dirt; }

        internal static string Save()
        {
            if (Field == null) return JsonUtility.ToJson(new Data { cells = 0, dirt = DirtOnHand });
            var d = new Data { x0 = Field.X0, z0 = Field.Z0, cells = Field.Cells, dirt = DirtOnHand };
            var hb = new byte[Field.H.Length * 4];
            Buffer.BlockCopy(Field.H, 0, hb, 0, hb.Length);
            d.heights = Convert.ToBase64String(hb);
            d.cover = Convert.ToBase64String(Field.C);
            return JsonUtility.ToJson(d);
        }

        internal static void Load(string json)
        {
            Destroy();
            DirtOnHand = 0;
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            DirtOnHand = d.dirt;
            if (d.cells <= 0) return;
            Create(d.x0, d.z0, d.cells);
            var hb = Convert.FromBase64String(d.heights);
            if (hb.Length == Field.H.Length * 4) Buffer.BlockCopy(hb, 0, Field.H, 0, hb.Length);
            var cb = Convert.FromBase64String(d.cover);
            if (cb.Length == Field.C.Length) Array.Copy(cb, Field.C, cb.Length);
            Field.MarkAllDirty();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { Field = null; _origGround = null; DirtOnHand = 0; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => SaveSystem.RegisterExtension("grade", Save, Load);
    }

    /// <summary>The heightfield mesh (chunks of 20x20 cells, each with a MeshCollider and one submesh per ground cover).</summary>
    public class GradeField : MonoBehaviour
    {
        public float X0, Z0;
        public int Cells;
        public float[] H;   // (Cells+1)^2 vertex heights
        public byte[] C;    // Cells^2 covers
        const int Chunk = 20;
        int _chunksPerSide;
        MeshFilter[] _mf; MeshCollider[] _mc; MeshRenderer[] _mr; bool[] _dirty;
        static readonly int CoverCount = Enum.GetValues(typeof(GroundCover)).Length;
        Material[] _coverMats;

        public float Size => Cells * Grade.Cell;
        public int V => Cells + 1;
        public bool Contains(float x, float z) => x > X0 + Grade.Cell && z > Z0 + Grade.Cell && x < X0 + Size - Grade.Cell && z < Z0 + Size - Grade.Cell;

        public void Init(float x0, float z0, int cells)
        {
            X0 = x0; Z0 = z0; Cells = cells;
            H = new float[V * V]; C = new byte[cells * cells];
            _chunksPerSide = Mathf.CeilToInt(cells / (float)Chunk);
            int n = _chunksPerSide * _chunksPerSide;
            _mf = new MeshFilter[n]; _mc = new MeshCollider[n]; _mr = new MeshRenderer[n]; _dirty = new bool[n];
            _coverMats = new Material[CoverCount];
            for (int i = 0; i < CoverCount; i++) _coverMats[i] = CoverMaterial((GroundCover)i);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("GradeChunk" + i);
                go.transform.SetParent(transform, false);
                _mf[i] = go.AddComponent<MeshFilter>();
                _mr[i] = go.AddComponent<MeshRenderer>();
                _mc[i] = go.AddComponent<MeshCollider>();
                _dirty[i] = true;
            }
            RebuildDirty();
        }

        public static Material CoverMaterial(GroundCover c)
        {
            switch (c)
            {
                case GroundCover.Grass: return Materials.Get(SurfaceKind.Grass);
                case GroundCover.Dirt: return Materials.Get(SurfaceKind.Dirt);
                case GroundCover.Topsoil: return LandMaterials.Get(LandSurface.Topsoil);
                case GroundCover.Seeded: return LandMaterials.Get(LandSurface.Straw);
                case GroundCover.Mulch: return LandMaterials.Get(LandSurface.Mulch);
                case GroundCover.Gravel: return Materials.Get(SurfaceKind.Gravel);
                case GroundCover.Sand: return LandMaterials.Get(LandSurface.Sand);
                default: return LandMaterials.Get(LandSurface.Sod);
            }
        }

        public void MarkAllDirty() { for (int i = 0; i < _dirty.Length; i++) _dirty[i] = true; }

        void MarkDirtyCell(int cx, int cz)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = Mathf.Clamp((cx + dx) / Chunk, 0, _chunksPerSide - 1), z = Mathf.Clamp((cz + dz) / Chunk, 0, _chunksPerSide - 1);
                    _dirty[z * _chunksPerSide + x] = true;
                }
        }

        void LateUpdate() => RebuildDirty();

        public void RebuildDirty()
        {
            for (int i = 0; i < _dirty.Length; i++)
            {
                if (!_dirty[i]) continue;
                _dirty[i] = false;
                BuildChunk(i);
            }
        }

        float Hv(int x, int z) => H[Mathf.Clamp(z, 0, Cells) * V + Mathf.Clamp(x, 0, Cells)];

        void BuildChunk(int i)
        {
            int cx0 = (i % _chunksPerSide) * Chunk, cz0 = (i / _chunksPerSide) * Chunk;
            int cx1 = Mathf.Min(Cells, cx0 + Chunk), cz1 = Mathf.Min(Cells, cz0 + Chunk);
            int w = cx1 - cx0 + 1, d = cz1 - cz0 + 1;
            var verts = new Vector3[w * d]; var norms = new Vector3[w * d]; var uvs = new Vector2[w * d];
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                {
                    int gx = cx0 + x, gz = cz0 + z;
                    float wx = X0 + gx * Grade.Cell, wz = Z0 + gz * Grade.Cell;
                    verts[z * w + x] = new Vector3(wx, Hv(gx, gz), wz);
                    norms[z * w + x] = new Vector3(Hv(gx - 1, gz) - Hv(gx + 1, gz), 2f * Grade.Cell, Hv(gx, gz - 1) - Hv(gx, gz + 1)).normalized;
                    uvs[z * w + x] = new Vector2(wx, wz);
                }
            var tris = new List<int>[CoverCount];
            for (int k = 0; k < CoverCount; k++) tris[k] = new List<int>();
            for (int z = 0; z < d - 1; z++)
                for (int x = 0; x < w - 1; x++)
                {
                    int cover = C[(cz0 + z) * Cells + cx0 + x];
                    int a = z * w + x, b = a + 1, c = a + w, e = c + 1;
                    var t = tris[cover];
                    t.Add(a); t.Add(c); t.Add(e); t.Add(a); t.Add(e); t.Add(b);
                }
            var mats = new List<Material>(); var used = new List<int>();
            for (int k = 0; k < CoverCount; k++) if (tris[k].Count > 0) { used.Add(k); mats.Add(_coverMats[k]); }
            var mesh = _mf[i].sharedMesh;
            if (mesh == null) { mesh = new Mesh { name = "GradeChunk" }; _mf[i].sharedMesh = mesh; }
            mesh.Clear();
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs;
            mesh.subMeshCount = used.Count;
            for (int s = 0; s < used.Count; s++) mesh.SetTriangles(tris[used[s]], s, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            _mr[i].sharedMaterials = mats.ToArray();
            _mc[i].sharedMesh = null; _mc[i].sharedMesh = mesh;
        }

        // ------------------------------------------------------------------ queries

        public float HeightAt(float x, float z)
        {
            float fx = (x - X0) / Grade.Cell, fz = (z - Z0) / Grade.Cell;
            if (fx < 0 || fz < 0 || fx > Cells || fz > Cells) return 0f;
            int ix = Mathf.Min(Cells - 1, (int)fx), iz = Mathf.Min(Cells - 1, (int)fz);
            float tx = fx - ix, tz = fz - iz;
            float a = Hv(ix, iz), b = Hv(ix + 1, iz), c = Hv(ix, iz + 1), e = Hv(ix + 1, iz + 1);
            // matches triangle split (a,c,e)/(a,e,b)
            if (tz >= tx) return a + (e - c) * tx + (c - a) * tz;
            return a + (b - a) * tx + (e - b) * tz;
        }

        /// <summary>dh/dx, dh/dz (rise per meter) sampled over ±0.5 m.</summary>
        public Vector2 Gradient(float x, float z)
        {
            const float e = 0.5f;
            return new Vector2((HeightAt(x + e, z) - HeightAt(x - e, z)) / (2 * e), (HeightAt(x, z + e) - HeightAt(x, z - e)) / (2 * e));
        }

        public GroundCover CoverAt(float x, float z)
        {
            int cx = Mathf.FloorToInt((x - X0) / Grade.Cell), cz = Mathf.FloorToInt((z - Z0) / Grade.Cell);
            if (cx < 0 || cz < 0 || cx >= Cells || cz >= Cells) return GroundCover.Grass;
            return (GroundCover)C[cz * Cells + cx];
        }

        // ------------------------------------------------------------------ edits

        bool Interior(int x, int z) => x > 0 && z > 0 && x < Cells && z < Cells;

        /// <summary>Visits vertices within radius with weight (smooth falloff). Returns count touched.</summary>
        void ForVerts(Vector3 c, float radius, Action<int, int, float> f)
        {
            int x0 = Mathf.FloorToInt((c.x - radius - X0) / Grade.Cell), x1 = Mathf.CeilToInt((c.x + radius - X0) / Grade.Cell);
            int z0 = Mathf.FloorToInt((c.z - radius - Z0) / Grade.Cell), z1 = Mathf.CeilToInt((c.z + radius - Z0) / Grade.Cell);
            for (int z = Mathf.Max(1, z0); z <= Mathf.Min(Cells - 1, z1); z++)
                for (int x = Mathf.Max(1, x0); x <= Mathf.Min(Cells - 1, x1); x++)
                {
                    float wx = X0 + x * Grade.Cell, wz = Z0 + z * Grade.Cell;
                    float d = Mathf.Sqrt((wx - c.x) * (wx - c.x) + (wz - c.z) * (wz - c.z));
                    if (d > radius) continue;
                    float w = 0.5f + 0.5f * Mathf.Cos(Mathf.PI * d / radius);
                    f(x, z, w);
                    MarkDirtyCell(Mathf.Min(x, Cells - 1), Mathf.Min(z, Cells - 1));
                }
        }

        /// <summary>Raises (+) or lowers (−) the ground. Returns the volume moved in m³ (positive = fill added).</summary>
        public float Raise(Vector3 c, float radius, float delta)
        {
            float vol = 0; float area = Grade.Cell * Grade.Cell;
            ForVerts(c, radius, (x, z, w) => { H[z * V + x] += delta * w; vol += delta * w * area; });
            return vol;
        }

        public void Smooth(Vector3 c, float radius, float k)
        {
            var copy = (float[])H.Clone();
            ForVerts(c, radius, (x, z, w) =>
            {
                float avg = (copy[z * V + x - 1] + copy[z * V + x + 1] + copy[(z - 1) * V + x] + copy[(z + 1) * V + x]) * 0.25f;
                H[z * V + x] = Mathf.Lerp(copy[z * V + x], avg, k * w);
            });
        }

        public float Flatten(Vector3 c, float radius, float target, float k)
        {
            float vol = 0; float area = Grade.Cell * Grade.Cell;
            ForVerts(c, radius, (x, z, w) => { float o = H[z * V + x]; float n = Mathf.Lerp(o, target, k * w); H[z * V + x] = n; vol += (n - o) * area; });
            return vol;
        }

        /// <summary>Sets every interior vertex height from a function (used for sloped lots and tests).</summary>
        public void SetHeights(Func<float, float, float> h)
        {
            for (int z = 1; z < Cells; z++)
                for (int x = 1; x < Cells; x++)
                {
                    float edge = Mathf.Min(Mathf.Min(x, z), Mathf.Min(Cells - x, Cells - z)) * Grade.Cell;
                    float fade = Mathf.Clamp01(edge / 6f); // blend to 0 over the outer 6 m
                    H[z * V + x] = h(X0 + x * Grade.Cell, Z0 + z * Grade.Cell) * fade;
                }
            MarkAllDirty();
        }

        public int Paint(Vector3 c, float radius, GroundCover cover, Func<GroundCover, bool> onlyOver = null)
        {
            int n = 0;
            int x0 = Mathf.FloorToInt((c.x - radius - X0) / Grade.Cell), x1 = Mathf.FloorToInt((c.x + radius - X0) / Grade.Cell);
            int z0 = Mathf.FloorToInt((c.z - radius - Z0) / Grade.Cell), z1 = Mathf.FloorToInt((c.z + radius - Z0) / Grade.Cell);
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(Cells - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Cells - 1, x1); x++)
                {
                    float wx = X0 + (x + 0.5f) * Grade.Cell, wz = Z0 + (z + 0.5f) * Grade.Cell;
                    if ((wx - c.x) * (wx - c.x) + (wz - c.z) * (wz - c.z) > radius * radius) continue;
                    var cur = (GroundCover)C[z * Cells + x];
                    if (cur == cover || (onlyOver != null && !onlyOver(cur))) continue;
                    C[z * Cells + x] = (byte)cover; n++;
                    MarkDirtyCell(x, z);
                }
            return n;
        }

        /// <summary>Paints every cell whose center lies inside the oriented rectangle (center, half extents, yaw degrees).</summary>
        public int PaintRect(Vector3 center, Vector2 half, float yaw, GroundCover cover)
        {
            int n = 0; var inv = Quaternion.Euler(0, -yaw, 0);
            float r = half.magnitude + Grade.Cell;
            int x0 = Mathf.FloorToInt((center.x - r - X0) / Grade.Cell), x1 = Mathf.FloorToInt((center.x + r - X0) / Grade.Cell);
            int z0 = Mathf.FloorToInt((center.z - r - Z0) / Grade.Cell), z1 = Mathf.FloorToInt((center.z + r - Z0) / Grade.Cell);
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(Cells - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Cells - 1, x1); x++)
                {
                    var l = inv * new Vector3(X0 + (x + 0.5f) * Grade.Cell - center.x, 0, Z0 + (z + 0.5f) * Grade.Cell - center.z);
                    if (Mathf.Abs(l.x) > half.x || Mathf.Abs(l.z) > half.y) continue;
                    if (C[z * Cells + x] == (byte)cover) continue;
                    C[z * Cells + x] = (byte)cover; n++; MarkDirtyCell(x, z);
                }
            return n;
        }
    }
}
