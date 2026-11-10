using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum GroundCover : byte { Grass, Dirt, Gravel }

    /// <summary>
    /// Diggable building-site ground: a 60' x 60' heightfield (4" cells) spanning x -30'..30', z 0'..60', replacing the
    /// flat bootstrap "Ground" plane (hidden at runtime; flat grass skirts continue it outward). Heights are per vertex,
    /// ground cover (grass/dirt/gravel) and compaction per cell. Everything else uses HeightAt / Dig / Raise.
    /// </summary>
    public class SiteGround : MonoBehaviour
    {
        public const string GravelId = "gravel34";
        public const float Cell = 4f * Units.Inch;
        public const int N = 180;                                        // cells per side (60')
        public static readonly Vector3 Origin = new Vector3(-30f * Units.Foot, 0f, 0f);
        public static float Size => N * Cell;

        public static SiteGround I { get; private set; }

        public float[] H;            // (N+1)^2 vertex heights (world Y)
        public GroundCover[] Cover;  // N*N
        public bool[] Compacted;     // N*N
        public int Version;          // bumps on every change

        Mesh _mesh;
        MeshCollider _col;
        bool _dirty;
        float _nextRebuild;

        public static SiteGround Ensure()
        {
            if (I != null) return I;
            var go = new GameObject("SiteGround");
            var g = go.AddComponent<SiteGround>();
            g.Init();
            return g;
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Init()
        {
            H = new float[(N + 1) * (N + 1)];
            Cover = new GroundCover[N * N];
            Compacted = new bool[N * N];

            // Hide the flat bootstrap plane and continue grass outward with four skirts.
            var plane = GameObject.Find("Ground");
            Material grass = Materials.Get(SurfaceKind.Grass);
            if (plane != null)
            {
                var r = plane.GetComponent<Renderer>(); if (r != null) r.enabled = false;
                var c = plane.GetComponent<Collider>(); if (c != null) c.enabled = false;
            }
            float far = 300f;
            float x0 = Origin.x, x1 = Origin.x + Size, z0 = Origin.z, z1 = Origin.z + Size;
            Skirt("SkirtSouth", new Vector3(-far, 0, -far), new Vector3(far, 0, z0), grass);
            Skirt("SkirtNorth", new Vector3(-far, 0, z1), new Vector3(far, 0, far), grass);
            Skirt("SkirtWest", new Vector3(-far, 0, z0), new Vector3(x0, 0, z1), grass);
            Skirt("SkirtEast", new Vector3(x1, 0, z0), new Vector3(far, 0, z1), grass);

            _mesh = new Mesh { name = "SiteGround", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { grass, Materials.Get(SurfaceKind.Dirt), Materials.Get(SurfaceKind.Gravel) };
            _col = gameObject.AddComponent<MeshCollider>();
            Rebuild();
        }

        void Skirt(string name, Vector3 min, Vector3 max, Material m)
        {
            var go = CTool.Build(name, (b, mats) =>
            {
                b.AddQuad(new Vector3(min.x, 0, min.z), new Vector3(min.x, 0, max.z), new Vector3(max.x, 0, max.z), new Vector3(max.x, 0, min.z), mats[m], Vector3.up);
            }, transform);
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3((min.x + max.x) * 0.5f, -0.05f, (min.z + max.z) * 0.5f);
            bc.size = new Vector3(max.x - min.x, 0.1f, max.z - min.z);
        }

        // ------------------------------------------------------------------ queries

        public static bool InPatch(Vector3 p) =>
            p.x > Origin.x + Cell && p.x < Origin.x + Size - Cell && p.z > Origin.z + Cell && p.z < Origin.z + Size - Cell;

        public static int VI(int x, int z) => z * (N + 1) + x;
        public static int CI(int x, int z) => z * N + x;
        public static Vector3 VertexPos(int x, int z, float y) => new Vector3(Origin.x + x * Cell, y, Origin.z + z * Cell);
        public static Vector3 CellCenter(int x, int z) => new Vector3(Origin.x + (x + 0.5f) * Cell, 0f, Origin.z + (z + 0.5f) * Cell);

        public static bool CellOf(Vector3 p, out int cx, out int cz)
        {
            cx = Mathf.FloorToInt((p.x - Origin.x) / Cell); cz = Mathf.FloorToInt((p.z - Origin.z) / Cell);
            return cx >= 0 && cz >= 0 && cx < N && cz < N;
        }

        /// <summary>Ground height (world Y) at any point; 0 outside the patch.</summary>
        public float HeightAt(Vector3 p)
        {
            float fx = (p.x - Origin.x) / Cell, fz = (p.z - Origin.z) / Cell;
            if (fx < 0 || fz < 0 || fx >= N || fz >= N) return 0f;
            int x = (int)fx, z = (int)fz; float tx = fx - x, tz = fz - z;
            float a = H[VI(x, z)], b = H[VI(x + 1, z)], c = H[VI(x, z + 1)], d = H[VI(x + 1, z + 1)];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        public static float GroundY(Vector3 p) => I != null ? I.HeightAt(p) : 0f;

        /// <summary>Average height of a cell (mean of its 4 corners).</summary>
        public float CellHeight(int x, int z) => 0.25f * (H[VI(x, z)] + H[VI(x + 1, z)] + H[VI(x, z + 1)] + H[VI(x + 1, z + 1)]);

        public GroundCover CoverAt(Vector3 p) => CellOf(p, out int x, out int z) ? Cover[CI(x, z)] : GroundCover.Grass;

        // ------------------------------------------------------------------ editing

        delegate float VertexOp(int vx, int vz, Vector3 pos, float h);

        float Apply(Vector3 center, float radius, bool square, VertexOp op, GroundCover? cover)
        {
            int r = Mathf.CeilToInt(radius / Cell) + 1;
            int cx = Mathf.RoundToInt((center.x - Origin.x) / Cell), cz = Mathf.RoundToInt((center.z - Origin.z) / Cell);
            float vol = 0f;
            for (int z = Mathf.Max(1, cz - r); z <= Mathf.Min(N - 1, cz + r); z++)
                for (int x = Mathf.Max(1, cx - r); x <= Mathf.Min(N - 1, cx + r); x++)
                {
                    Vector3 vp = VertexPos(x, z, 0);
                    float dx = vp.x - center.x, dz = vp.z - center.z;
                    bool inside = square ? (Mathf.Abs(dx) <= radius + 1e-4f && Mathf.Abs(dz) <= radius + 1e-4f) : (dx * dx + dz * dz <= radius * radius + 1e-6f);
                    if (!inside) continue;
                    int i = VI(x, z);
                    float nh = op(x, z, vp, H[i]);
                    if (Mathf.Abs(nh - H[i]) < 1e-6f) continue;
                    vol += (nh - H[i]) * Cell * Cell;
                    H[i] = nh;
                    if (cover.HasValue) MarkCellsAround(x, z, cover.Value);
                }
            if (vol != 0f) Touch();
            return vol;
        }

        void MarkCellsAround(int vx, int vz, GroundCover c)
        {
            for (int z = vz - 1; z <= vz; z++)
                for (int x = vx - 1; x <= vx; x++)
                    if (x >= 0 && z >= 0 && x < N && z < N) { int ci = CI(x, z); Cover[ci] = c; Compacted[ci] = false; }
        }

        void Touch() { _dirty = true; Version++; }

        /// <summary>Lowers ground to at most targetY within a circle (radius m). Returns removed volume (m³, positive).</summary>
        public float DigTo(Vector3 center, float radius, float targetY) =>
            -Apply(center, radius, false, (x, z, p, h) => Mathf.Min(h, targetY), GroundCover.Dirt);

        /// <summary>Lowers ground by `amount` (but never below minY). Returns removed volume (m³).</summary>
        public float DigBy(Vector3 center, float radius, float amount, float minY = -3f) =>
            -Apply(center, radius, false, (x, z, p, h) => Mathf.Max(minY, Mathf.Min(h, h - amount)), GroundCover.Dirt);

        /// <summary>Raises ground up to targetY with the given cover (gravel base, backfill). Returns added volume (m³).</summary>
        public float RaiseTo(Vector3 center, float radius, float targetY, GroundCover cover, float maxVolume = float.MaxValue)
        {
            float used = 0f;
            return Apply(center, radius, false, (x, z, p, h) =>
            {
                if (h >= targetY) return h;
                float add = targetY - h, v = add * Cell * Cell;
                if (used + v > maxVolume) { add = Mathf.Max(0f, (maxVolume - used) / (Cell * Cell)); v = add * Cell * Cell; }
                used += v; return h + add;
            }, cover);
        }

        /// <summary>Rectangle versions (axis-aligned, world min/max XZ) for machine work and tests.</summary>
        public float DigRect(Vector3 min, Vector3 max, float targetY) => RectOp(min, max, h => Mathf.Min(h, targetY), GroundCover.Dirt) * -1f;
        public float RaiseRect(Vector3 min, Vector3 max, float targetY, GroundCover cover) => RectOp(min, max, h => Mathf.Max(h, targetY), cover);

        float RectOp(Vector3 min, Vector3 max, System.Func<float, float> f, GroundCover cover)
        {
            int x0 = Mathf.Clamp(Mathf.RoundToInt((min.x - Origin.x) / Cell), 1, N - 1), x1 = Mathf.Clamp(Mathf.RoundToInt((max.x - Origin.x) / Cell), 1, N - 1);
            int z0 = Mathf.Clamp(Mathf.RoundToInt((min.z - Origin.z) / Cell), 1, N - 1), z1 = Mathf.Clamp(Mathf.RoundToInt((max.z - Origin.z) / Cell), 1, N - 1);
            float vol = 0f;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = VI(x, z); float nh = f(H[i]);
                    if (Mathf.Abs(nh - H[i]) < 1e-6f) continue;
                    vol += (nh - H[i]) * Cell * Cell; H[i] = nh;
                }
            for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++) { int ci = CI(x, z); Cover[ci] = cover; Compacted[ci] = false; }
            if (vol != 0f || true) Touch();
            return vol;
        }

        /// <summary>One pass of a plate compactor: settles loose fill ~3/8" once and marks the cells compacted.</summary>
        public int Compact(Vector3 center, float radius)
        {
            int n = 0;
            if (!CellOf(center, out int cx, out int cz)) return 0;
            int r = Mathf.CeilToInt(radius / Cell);
            for (int z = Mathf.Max(1, cz - r); z <= Mathf.Min(N - 2, cz + r); z++)
                for (int x = Mathf.Max(1, cx - r); x <= Mathf.Min(N - 2, cx + r); x++)
                {
                    int ci = CI(x, z);
                    if (Compacted[ci] || Cover[ci] == GroundCover.Grass) continue;
                    Compacted[ci] = true; n++;
                }
            if (n > 0)
            {
                // settle the vertices of newly compacted cells
                for (int z = Mathf.Max(1, cz - r); z <= Mathf.Min(N - 1, cz + r); z++)
                    for (int x = Mathf.Max(1, cx - r); x <= Mathf.Min(N - 1, cx + r); x++)
                        if (Compacted[CI(x, z)] && Compacted[CI(x - 1, z - 1)]) H[VI(x, z)] -= 0.0005f;
                Touch();
            }
            return n;
        }

        /// <summary>Fraction of non-grass cells inside the polygon that are compacted.</summary>
        public float CompactedFraction(Vector2[] poly)
        {
            int tot = 0, ok = 0;
            ForCellsIn(poly, (x, z) => { int ci = CI(x, z); if (Cover[ci] == GroundCover.Grass) return; tot++; if (Compacted[ci]) ok++; });
            return tot == 0 ? 0f : ok / (float)tot;
        }

        public static void ForCellsIn(Vector2[] poly, System.Action<int, int> f)
        {
            if (poly == null || poly.Length < 3) return;
            Vector2 mn = poly[0], mx = poly[0];
            foreach (var p in poly) { mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
            int x0 = Mathf.Max(0, Mathf.FloorToInt((mn.x - Origin.x) / Cell)), x1 = Mathf.Min(N - 1, Mathf.CeilToInt((mx.x - Origin.x) / Cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((mn.y - Origin.z) / Cell)), z1 = Mathf.Min(N - 1, Mathf.CeilToInt((mx.y - Origin.z) / Cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var c = CellCenter(x, z);
                    if (CTool.PointInPolygon(poly, new Vector2(c.x, c.z))) f(x, z);
                }
        }

        // ------------------------------------------------------------------ mesh

        void LateUpdate()
        {
            if (_dirty && Time.time >= _nextRebuild) { Rebuild(); _nextRebuild = Time.time + 0.15f; }
        }

        /// <summary>Forces the mesh/collider to match the heights now (tests call this before raycasting).</summary>
        public void Rebuild()
        {
            _dirty = false;
            int nv = (N + 1) * (N + 1);
            var v = new Vector3[nv]; var uv = new Vector2[nv];
            for (int z = 0; z <= N; z++)
                for (int x = 0; x <= N; x++)
                {
                    int i = VI(x, z); v[i] = VertexPos(x, z, H[i]); uv[i] = new Vector2(v[i].x, v[i].z);
                }
            var t = new List<int>[3] { new List<int>(N * N * 6), new List<int>(), new List<int>() };
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    var l = t[(int)Cover[CI(x, z)]];
                    int a = VI(x, z), b = VI(x + 1, z), c = VI(x + 1, z + 1), d = VI(x, z + 1);
                    l.Add(a); l.Add(d); l.Add(c); l.Add(a); l.Add(c); l.Add(b);
                }
            _mesh.Clear();
            _mesh.vertices = v; _mesh.uv = uv;
            _mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++) _mesh.SetTriangles(t[s], s, false);
            _mesh.RecalculateBounds(); _mesh.RecalculateNormals(); _mesh.RecalculateTangents();
            _col.sharedMesh = null; _col.sharedMesh = _mesh;
        }
    }
}
