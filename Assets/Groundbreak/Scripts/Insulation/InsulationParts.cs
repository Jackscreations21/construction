using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Closed-cell spray foam in one bay: sprayed against the back of the cavity, lumpy, climbing the stud sides.</summary>
    public class FoamFill : MonoBehaviour
    {
        public FramePlane Fp;
        public Rect R;
        public float Depth, Thick;
        public int Seed;
        public float EffectiveR => Units.ToInches(Thick) * InsulationRules.ClosedCellRPerInch;

        public void Rebuild()
        {
            var sm = new SoftMesh();
            Rect r = R; float back = Depth > 0.01f ? Depth : Thick; int seed = Seed;
            float Z(float x, float y)
            {
                float e = Mathf.Min(Mathf.Min(x - r.xMin, r.xMax - x), Mathf.Min(y - r.yMin, r.yMax - y));
                float climb = Units.In(0.6f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / 0.035f)));
                float lump = (Mathf.PerlinNoise(x * 9f + seed, y * 9f) - 0.5f) * Units.In(0.8f) + (Mathf.PerlinNoise(x * 26f, y * 26f + seed) - 0.5f) * Units.In(0.25f);
                return Mathf.Max(-0.002f, back - Thick - climb - lump);
            }
            sm.Heightfield(R, null, Units.In(0.75f), Z, 0);
            var mesh = sm.Build("SprayFoam");
            var mf = GetComponent<MeshFilter>();
            var mats = new[] { FinishMaterials.Get(InsulationLooks.Foam) };
            if (mf == null)
            {
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                gameObject.AddComponent<MeshRenderer>().sharedMaterials = mats;
                var col = gameObject.AddComponent<BoxCollider>();
                col.center = new Vector3(R.center.x, R.center.y, back - 0.02f); col.size = new Vector3(R.width, R.height, 0.04f);
            }
            else PanelMesh.Replace(gameObject, mesh, mats);
        }

        void OnDestroy() { Insulation.Foams.Remove(this); }
    }

    /// <summary>6-mil poly vapor retarder over a wall face: translucent, wrinkled, lapped 6" at seams, stapled, cut at openings/boxes.</summary>
    public class PolySheet : MonoBehaviour
    {
        public FramePlane Fp;
        public readonly List<Rect> Holes = new List<Rect>();

        public void Rebuild()
        {
            Holes.Clear();
            foreach (var o in Fp.Openings())
            {
                var h = o; if (h.yMin < Fp.Bounds.yMin + Units.In(2f)) h.yMin = Fp.Bounds.yMin - 0.01f;
                Holes.Add(h);
            }
            foreach (var (c, o) in Insulation.Boxes(Fp)) Holes.Add(o.R);
            var sm = new SoftMesh { MinSubmeshes = 2 };
            float Z(float x, float y) => -0.0004f - (Mathf.PerlinNoise(x * 3f, y * 2.2f) - 0.5f) * 0.002f;
            sm.Heightfield(Fp.Bounds, Holes, Units.In(3f), Z, 0);
            // 10' roll width hung vertically: lap strip every 9'6"
            float roll = Units.Foot * 10f, lap = Units.In(InsulationRules.PolyLapIn);
            for (float u = Fp.Bounds.xMin + roll - lap; u < Fp.Bounds.xMax - 0.05f; u += roll - lap)
            {
                var strip = Rect.MinMaxRect(u, Fp.Bounds.yMin, Mathf.Min(u + lap, Fp.Bounds.xMax), Fp.Bounds.yMax);
                sm.Heightfield(strip, Holes, Units.In(3f), (x, y) => Z(x, y) - 0.0006f, 0);
            }
            // staples along the studs every 12"
            foreach (var u in Fp.FramingLinesU())
                for (float v = Fp.Bounds.yMin + Units.In(4f); v < Fp.Bounds.yMax; v += Units.Foot)
                {
                    bool inHole = false; foreach (var h in Holes) if (h.Contains(new Vector2(u, v))) inHole = true;
                    if (inHole) continue;
                    float hw = Units.In(0.02f), hl = Units.In(0.1875f), z = -0.0012f;
                    sm.FlatQuad(new Vector3(u - hw, v - hl, z), new Vector3(u + hw, v - hl, z), new Vector3(u + hw, v + hl, z), new Vector3(u - hw, v + hl, z), 1, Vector3.back, Vector3.right, Vector3.up);
                }
            var mesh = sm.Build("Poly");
            var mats = new[] { FinishMaterials.Get(InsulationLooks.Poly), FinishMaterials.Get(InsulationLooks.Staple) };
            var mf = GetComponent<MeshFilter>();
            if (mf == null)
            {
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = gameObject.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else PanelMesh.Replace(gameObject, mesh, mats);
        }

        void OnDestroy() { Insulation.Poly.Remove(this); }
    }

    /// <summary>
    /// Blown loose-fill fiberglass on a ceiling (attic side). Depth grid at 2" cells over the ceiling face, measured up
    /// from the ceiling plane (bottom of the joists = top of the ceiling drywall). Clumpy heightfield surface; joists
    /// poke through until it's deeper than they are.
    /// </summary>
    public class AtticFill : MonoBehaviour
    {
        public FramePlane Fp;          // ceiling face (N down); local z = up
        public float Cell = Units.In(2f);
        public int Nx, Ny;
        public float[] D;              // depth (m)
        float _bagVolume = Units.Foot * Units.Foot * Units.Foot * 50f, _volLeft;
        bool _dirty;

        public void Init(FramePlane fp)
        {
            Fp = fp;
            Nx = Mathf.Max(2, Mathf.CeilToInt(fp.Bounds.width / Cell) + 1);
            Ny = Mathf.Max(2, Mathf.CeilToInt(fp.Bounds.height / Cell) + 1);
            D = new float[Nx * Ny];
        }

        /// <summary>Ceiling drywall (or any finish) under this attic?</summary>
        public static bool CeilingBelow(FramePlane cf) => FinishHooks.CoveringThickness(cf, cf.Bounds.center) > 0.001f;

        public float AverageDepth { get { float s = 0; foreach (var d in D) s += d; return s / D.Length; } }
        /// <summary>Depth exceeded by 90% of the attic (thin spots fail).</summary>
        public float LowDepth
        {
            get { var c = (float[])D.Clone(); System.Array.Sort(c); return c[Mathf.Clamp(c.Length / 10, 0, c.Length - 1)]; }
        }
        public float RValue => Units.ToInches(LowDepth) * InsulationRules.LooseFillRPerInch;
        public float AreaSqFt => Fp.Bounds.width * Fp.Bounds.height / (Units.Foot * Units.Foot);

        /// <summary>Blow for dt seconds at a world point: a ~1 m wide pile rising ~6"/s at the center. Uses bags.</summary>
        public bool Blow(Vector3 world, float dt)
        {
            var uv = Fp.ToUV(world);
            float rad = 0.9f, rate = Units.In(6f) * dt, vol = 0f;
            for (int j = 0; j < Ny; j++)
                for (int i = 0; i < Nx; i++)
                {
                    float x = Fp.Bounds.xMin + i * Cell, y = Fp.Bounds.yMin + j * Cell;
                    float d2 = ((x - uv.x) * (x - uv.x) + (y - uv.y) * (y - uv.y)) / (rad * rad);
                    if (d2 > 1f) continue;
                    float add = rate * (1f - d2) * (1f - d2);
                    D[j * Nx + i] += add; vol += add * Cell * Cell;
                }
            _volLeft -= vol;
            while (_volLeft < 0f) { if (!FinishStock.Take(Insulation.LooseFillId)) { _dirty = true; return false; } _volLeft += _bagVolume; }
            _dirty = true;
            return true;
        }

        /// <summary>Level the attic to at least this depth (scripted install).</summary>
        public void FillTo(float depth)
        {
            float vol = 0f;
            for (int k = 0; k < D.Length; k++)
            {
                int i = k % Nx, j = k / Nx;
                float target = depth + (Mathf.PerlinNoise(i * 0.21f, j * 0.21f) - 0.3f) * Units.In(1.2f);
                if (D[k] < target) { vol += (target - D[k]) * Cell * Cell; D[k] = target; }
            }
            int bags = Mathf.CeilToInt(vol / _bagVolume);
            if (bags > 0) FinishStock.Take(Insulation.LooseFillId, bags);
            Rebuild();
        }

        void LateUpdate() { if (_dirty && Time.frameCount % 6 == 0) { _dirty = false; Rebuild(); } }

        float Sample(float x, float y)
        {
            float fx = (x - Fp.Bounds.xMin) / Cell, fy = (y - Fp.Bounds.yMin) / Cell;
            int i = Mathf.Clamp(Mathf.FloorToInt(fx), 0, Nx - 2), j = Mathf.Clamp(Mathf.FloorToInt(fy), 0, Ny - 2);
            float tx = Mathf.Clamp01(fx - i), ty = Mathf.Clamp01(fy - j);
            return Mathf.Lerp(Mathf.Lerp(D[j * Nx + i], D[j * Nx + i + 1], tx), Mathf.Lerp(D[(j + 1) * Nx + i], D[(j + 1) * Nx + i + 1], tx), ty);
        }

        public void Rebuild()
        {
            var sm = new SoftMesh();
            float Z(float x, float y)
            {
                float d = Sample(x, y);
                if (d < 0.003f) return 0.0005f; // bare: just above the ceiling (hidden by drywall top)
                float clump = (Mathf.PerlinNoise(x * 9f, y * 9f) - 0.5f) * Units.In(1.6f) + (Mathf.PerlinNoise(x * 27f + 7f, y * 27f) - 0.5f) * Units.In(0.7f) + (Mathf.PerlinNoise(x * 2.5f + 3f, y * 2.5f) - 0.5f) * Units.In(1.2f);
                return d + clump * Mathf.Clamp01(d / Units.In(3f));
            }
            var g = sm.Heightfield(Fp.Bounds, null, Units.In(1.5f), Z, 0, false);
            sm.Skirts(g, (x, y) => 0f, 0, 0.01f);
            var mesh = sm.Build("LooseFill");
            var mats = new[] { FinishMaterials.Get(InsulationLooks.LooseFill) };
            var mf = GetComponent<MeshFilter>();
            if (mf == null)
            {
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                gameObject.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            else PanelMesh.Replace(gameObject, mesh, mats);
        }

        void OnDestroy() { Insulation.Attics.Remove(this); }
    }
}
