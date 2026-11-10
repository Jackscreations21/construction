using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>What covers a ground cell.</summary>
    public enum LandCover : byte { Grass, Dirt, Topsoil, Seeded, Mulch, Gravel, Sand, Lawn }

    /// <summary>
    /// Editable terrain for the site. Created lazily around the first grading edit (or the career lot); the flat 2000' ground
    /// plane gets a matching hole so the field can be cut below zero. Heights are meters at 0.5 m vertices; each cell has a
    /// <see cref="LandCover"/>. Outside the field the ground is flat at y = 0.
    /// </summary>
    public static class Grade
    {
        public static GradeField Field { get; private set; }
        public const float Cell = 0.5f;
        public const int DefaultCells = 160; // 80 m = 262'

        /// <summary>Ground height anywhere: the building pad (SiteGround, 4" cells) inside its footprint, else the lot field, else 0.</summary>
        public static float HeightAt(float x, float z)
        {
            if (InPad(x, z)) return PadHeight(x, z);
            return Field != null ? Field.HeightAt(x, z) : 0f;
        }

        public static LandCover CoverAt(float x, float z)
        {
            if (InPad(x, z))
            {
                switch (SiteGround.I.CoverAt(new Vector3(x, 0, z)))
                {
                    case GroundCover.Dirt: return LandCover.Dirt;
                    case GroundCover.Gravel: return LandCover.Gravel;
                    default: return LandCover.Grass;
                }
            }
            return Field != null ? Field.CoverAt(x, z) : LandCover.Grass;
        }

        /// <summary>dh/dx, dh/dz (rise per meter) sampled over +-0.5 m.</summary>
        public static Vector2 Gradient(float x, float z)
        {
            const float e = 0.5f;
            return new Vector2((HeightAt(x + e, z) - HeightAt(x - e, z)) / (2 * e), (HeightAt(x, z + e) - HeightAt(x, z - e)) / (2 * e));
        }

        // ------------------------------------------------------------------ building pad (SiteGround)

        /// <summary>True when (x,z) is on the high-res building pad (edges inclusive). Grade never edits or draws there.</summary>
        public static bool InPad(float x, float z)
        {
            if (SiteGround.I == null) return false;
            var r = SiteGround.PadRect;
            return x >= r.xMin && x <= r.xMax && z >= r.yMin && z <= r.yMax;
        }

        /// <summary>True when (x,z) is strictly inside the pad (not on its edge).</summary>
        public static bool InPadInterior(float x, float z)
        {
            if (SiteGround.I == null) return false;
            var r = SiteGround.PadRect;
            return x > r.xMin + 1e-4f && x < r.xMax - 1e-4f && z > r.yMin + 1e-4f && z < r.yMax - 1e-4f;
        }

        /// <summary>Pad height, clamped onto the pad so its far edges are sampled too.</summary>
        public static float PadHeight(float x, float z)
        {
            var r = SiteGround.PadRect;
            const float e = 1e-4f;
            return SiteGround.I.HeightAt(new Vector3(Mathf.Clamp(x, r.xMin, r.xMax - e), 0, Mathf.Clamp(z, r.yMin, r.yMax - e)));
        }

        /// <summary>Height of the pad edge nearest (x,z): what the lot grades into around the pad.</summary>
        public static float PadEdgeHeight(float x, float z)
        {
            var r = SiteGround.PadRect;
            return PadHeight(Mathf.Clamp(x, r.xMin, r.xMax), Mathf.Clamp(z, r.yMin, r.yMax));
        }

        /// <summary>Distance (m) from (x,z) to the pad rectangle (0 inside); +inf without a pad.</summary>
        public static float DistanceToPad(float x, float z)
        {
            if (SiteGround.I == null) return float.PositiveInfinity;
            var r = SiteGround.PadRect;
            float dx = Mathf.Max(0f, Mathf.Max(r.xMin - x, x - r.xMax)), dz = Mathf.Max(0f, Mathf.Max(r.yMin - z, z - r.yMax));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static void OnPadCreated(SiteGround g)
        {
            if (Field != null) { Field.SnapToPad(); Field.MarkAllDirty(); }
            RefreshGroundHoles();
        }

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
            RefreshGroundHoles();
        }

        public static void Destroy()
        {
            if (Field != null) UnityEngine.Object.Destroy(Field.gameObject);
            Field = null;
            RefreshGroundHoles();
        }

        // ------------------------------------------------------------------ ground plane hole

        static Mesh _origGround;

        /// <summary>Re-cuts the bootstrap "Ground" plane around the lot field and the building pad (or restores it when neither exists).</summary>
        public static void RefreshGroundHoles()
        {
            var holes = new List<Rect>();
            if (Field != null) holes.Add(new Rect(Field.X0, Field.Z0, Field.Size, Field.Size));
            if (SiteGround.I != null) holes.Add(SiteGround.PadRect);
            if (holes.Count == 0) { RestoreGround(); return; }
            CutGroundHoles(holes);
        }

        static void CutGroundHoles(List<Rect> holes)
        {
            var g = GameObject.Find("Ground");
            if (g == null) return;
            var mf = g.GetComponent<MeshFilter>();
            if (mf == null) return;
            if (_origGround == null) _origGround = mf.sharedMesh;
            float scale = g.transform.lossyScale.x;
            float half = 5f * scale; // Unity plane is 10 units across
            float groundSize = half * 2f;
            // grid decomposition: every hole edge splits the plane; keep the cells no hole covers
            var xs = new List<float> { -half, half }; var zs = new List<float> { -half, half };
            foreach (var h in holes)
            {
                xs.Add(Mathf.Clamp(h.xMin, -half, half)); xs.Add(Mathf.Clamp(h.xMax, -half, half));
                zs.Add(Mathf.Clamp(h.yMin, -half, half)); zs.Add(Mathf.Clamp(h.yMax, -half, half));
            }
            xs.Sort(); zs.Sort();
            var b = new MeshBuilder();
            for (int iz = 0; iz < zs.Count - 1; iz++)
                for (int ix = 0; ix < xs.Count - 1; ix++)
                {
                    float ax = xs[ix], bx = xs[ix + 1], az = zs[iz], bz = zs[iz + 1];
                    if (bx - ax < 0.001f || bz - az < 0.001f) continue;
                    float cx = (ax + bx) * 0.5f, cz = (az + bz) * 0.5f;
                    bool covered = false;
                    foreach (var h in holes) if (h.Contains(new Vector2(cx, cz))) { covered = true; break; }
                    if (covered) continue;
                    b.AddQuad(new Vector3(ax, 0, az), new Vector3(ax, 0, bz), new Vector3(bx, 0, bz), new Vector3(bx, 0, az), 0, Vector3.up);
                }
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
        static void Init()
        {
            SaveSystem.RegisterExtension("grade", Save, Load);
            SiteGround.Created -= OnPadCreated;
            SiteGround.Created += OnPadCreated;
        }
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
        static readonly int CoverCount = Enum.GetValues(typeof(LandCover)).Length;
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
            for (int i = 0; i < CoverCount; i++) _coverMats[i] = CoverMaterial((LandCover)i);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("GradeChunk" + i);
                go.transform.SetParent(transform, false);
                _mf[i] = go.AddComponent<MeshFilter>();
                _mr[i] = go.AddComponent<MeshRenderer>();
                _mc[i] = go.AddComponent<MeshCollider>();
                _dirty[i] = true;
            }
            SnapToPad();
            RebuildDirty();
        }

        /// <summary>Vertices on/inside the building pad take the pad's height, so the lot meets the pad without a seam.</summary>
        public void SnapToPad()
        {
            if (SiteGround.I == null) return;
            for (int z = 0; z <= Cells; z++)
                for (int x = 0; x <= Cells; x++)
                {
                    float wx = X0 + x * Grade.Cell, wz = Z0 + z * Grade.Cell;
                    if (Grade.InPad(wx, wz)) H[z * V + x] = Grade.PadHeight(wx, wz);
                }
        }

        public static Material CoverMaterial(LandCover c)
        {
            switch (c)
            {
                case LandCover.Grass: return Materials.Get(SurfaceKind.Grass);
                case LandCover.Dirt: return Materials.Get(SurfaceKind.Dirt);
                case LandCover.Topsoil: return LandMaterials.Get(LandSurface.Topsoil);
                case LandCover.Seeded: return LandMaterials.Get(LandSurface.Straw);
                case LandCover.Mulch: return LandMaterials.Get(LandSurface.Mulch);
                case LandCover.Gravel: return Materials.Get(SurfaceKind.Gravel);
                case LandCover.Sand: return LandMaterials.Get(LandSurface.Sand);
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
            bool pad = SiteGround.I != null;
            Rect pr = pad ? SiteGround.PadRect : default;
            List<Vector3> xv = null; List<Vector3> xn = null; List<Vector2> xu = null; // extra verts for cells clipped by the pad
            for (int z = 0; z < d - 1; z++)
                for (int x = 0; x < w - 1; x++)
                {
                    int cover = C[(cz0 + z) * Cells + cx0 + x];
                    var t = tris[cover];
                    if (pad)
                    {
                        float ax = X0 + (cx0 + x) * Grade.Cell, az = Z0 + (cz0 + z) * Grade.Cell, bx = ax + Grade.Cell, bz = az + Grade.Cell;
                        float ix0 = Mathf.Max(ax, pr.xMin), ix1 = Mathf.Min(bx, pr.xMax), iz0 = Mathf.Max(az, pr.yMin), iz1 = Mathf.Min(bz, pr.yMax);
                        if (ix1 - ix0 > 1e-4f && iz1 - iz0 > 1e-4f)
                        {
                            // the cell overlaps the pad: draw only the parts outside it (up to 4 strips), heights pad-aware
                            if (xv == null) { xv = new List<Vector3>(); xn = new List<Vector3>(); xu = new List<Vector2>(); }
                            int baseIndex = w * d;
                            void Piece(float px0, float pz0, float px1, float pz1)
                            {
                                if (px1 - px0 < 1e-4f || pz1 - pz0 < 1e-4f) return;
                                int s0 = baseIndex + xv.Count;
                                foreach (var q in new[] { new Vector2(px0, pz0), new Vector2(px1, pz0), new Vector2(px0, pz1), new Vector2(px1, pz1) })
                                {
                                    float hy = Grade.HeightAt(q.x, q.y);
                                    var gr = Grade.Gradient(q.x, q.y);
                                    xv.Add(new Vector3(q.x, hy, q.y)); xn.Add(new Vector3(-gr.x, 1f, -gr.y).normalized); xu.Add(q);
                                }
                                t.Add(s0); t.Add(s0 + 2); t.Add(s0 + 3); t.Add(s0); t.Add(s0 + 3); t.Add(s0 + 1);
                            }
                            Piece(ax, az, bx, iz0);          // south strip
                            Piece(ax, iz1, bx, bz);          // north strip
                            Piece(ax, iz0, ix0, iz1);        // west
                            Piece(ix1, iz0, bx, iz1);        // east
                            continue;
                        }
                    }
                    int a = z * w + x, b = a + 1, c = a + w, e = c + 1;
                    t.Add(a); t.Add(c); t.Add(e); t.Add(a); t.Add(e); t.Add(b);
                }
            if (xv != null)
            {
                var av = new List<Vector3>(verts); av.AddRange(xv); verts = av.ToArray();
                var an = new List<Vector3>(norms); an.AddRange(xn); norms = an.ToArray();
                var au = new List<Vector2>(uvs); au.AddRange(xu); uvs = au.ToArray();
            }
            var mats = new List<Material>(); var used = new List<int>();
            for (int k = 0; k < CoverCount; k++) if (tris[k].Count > 0) { used.Add(k); mats.Add(_coverMats[k]); }
            var mesh = _mf[i].sharedMesh;
            if (mesh == null) { mesh = new Mesh { name = "GradeChunk" }; _mf[i].sharedMesh = mesh; }
            mesh.Clear();
            if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
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

        public LandCover CoverAt(float x, float z)
        {
            int cx = Mathf.FloorToInt((x - X0) / Grade.Cell), cz = Mathf.FloorToInt((z - Z0) / Grade.Cell);
            if (cx < 0 || cz < 0 || cx >= Cells || cz >= Cells) return LandCover.Grass;
            return (LandCover)C[cz * Cells + cx];
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
                    if (Grade.InPad(wx, wz)) continue; // the building pad is SiteGround's (concrete tools dig it)
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
                    float wx = X0 + x * Grade.Cell, wz = Z0 + z * Grade.Cell;
                    float v = h(wx, wz) * fade;
                    // grade the lot into the building pad: blend to the pad edge height over a 4 m apron
                    float pd = Grade.DistanceToPad(wx, wz);
                    if (pd < 4f) v = Mathf.Lerp(Grade.PadEdgeHeight(wx, wz), v, Mathf.SmoothStep(0f, 1f, pd / 4f));
                    H[z * V + x] = v;
                }
            SnapToPad();
            MarkAllDirty();
        }

        public int Paint(Vector3 c, float radius, LandCover cover, Func<LandCover, bool> onlyOver = null)
        {
            int n = 0;
            int x0 = Mathf.FloorToInt((c.x - radius - X0) / Grade.Cell), x1 = Mathf.FloorToInt((c.x + radius - X0) / Grade.Cell);
            int z0 = Mathf.FloorToInt((c.z - radius - Z0) / Grade.Cell), z1 = Mathf.FloorToInt((c.z + radius - Z0) / Grade.Cell);
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(Cells - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Cells - 1, x1); x++)
                {
                    float wx = X0 + (x + 0.5f) * Grade.Cell, wz = Z0 + (z + 0.5f) * Grade.Cell;
                    if ((wx - c.x) * (wx - c.x) + (wz - c.z) * (wz - c.z) > radius * radius) continue;
                    if (Grade.InPadInterior(wx, wz)) continue;
                    var cur = (LandCover)C[z * Cells + x];
                    if (cur == cover || (onlyOver != null && !onlyOver(cur))) continue;
                    C[z * Cells + x] = (byte)cover; n++;
                    MarkDirtyCell(x, z);
                }
            return n;
        }

        /// <summary>Paints every cell whose center lies inside the oriented rectangle (center, half extents, yaw degrees).</summary>
        public int PaintRect(Vector3 center, Vector2 half, float yaw, LandCover cover)
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
