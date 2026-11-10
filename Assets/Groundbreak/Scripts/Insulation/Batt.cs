using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// One fiberglass batt friction-fit in a framing bay. Soft pillowy mesh: fluffy fiberglass bulging slightly
    /// proud of the studs (unfaced) or kraft paper with flanges on the stud faces (faced), slight sag and lumps.
    /// States: placed (cut to bay length), split around obstacles, stapled.
    /// </summary>
    public class Batt : MonoBehaviour
    {
        public BattProduct P;
        public FramePlane Fp;
        public Rect R;                  // bay rect (u,v) it fills
        public float Depth;             // cavity depth
        public bool CutAround;          // split/notched around boxes and pipes
        public bool Stapled, Inset;
        public int Pieces = 1;          // batts used (tall bays need 2)
        public readonly List<FramePlane.Obstacle> Obstacles = new List<FramePlane.Obstacle>();
        public int Seed;

        /// <summary>Thickness actually in the cavity.</summary>
        public float FillThick => Mathf.Min(P.Thick, Depth > 0.01f ? Depth : P.Thick);
        /// <summary>R-value after compression (a thick batt squeezed into a shallow cavity loses R).</summary>
        public float EffectiveR => P.Thick <= Depth + 0.006f || Depth < 0.01f ? P.R : P.R * Mathf.Pow(Depth / P.Thick, 0.6f);
        /// <summary>Obstacles the batt is jammed over (not split around) = compressed insulation.</summary>
        public bool Compressed => !CutAround && Obstacles.Count > 0;

        static bool IsBox(FramePlane.Obstacle o) => o.Proud > -0.006f;

        public void Rebuild()
        {
            var sm = new SoftMesh { MinSubmeshes = 3 };
            float depth = FillThick;
            var holes = new List<Rect>();
            if (CutAround)
                foreach (var o in Obstacles)
                    if (IsBox(o)) holes.Add(Expand(o.R, Units.In(0.125f)));
            float bulge = P.Faced ? (Stapled ? 0.007f : 0.013f) : 0.011f;
            // once drywall is hung over it the batt is pressed flat behind the board
            bool pressed = FinishHooks.CoveringThickness(Fp, R.center) > 0.001f;
            if (pressed) bulge = 0f;
            float recess = Mathf.Max(0f, (Depth > 0.01f ? Depth : depth) - P.Thick);  // thin batt in deep cavity sits back
            if (P.Faced && Inset && Stapled) recess += Units.In(0.25f);
            Rect r = R;
            int seed = Seed;
            float Edge(float x, float y)
            {
                float e = Mathf.Min(Mathf.Min(x - r.xMin, r.xMax - x), Mathf.Min(y - r.yMin, r.yMax - y));
                foreach (var h in holes)
                {
                    float dx = Mathf.Max(h.xMin - x, x - h.xMax), dy = Mathf.Max(h.yMin - y, y - h.yMax);
                    float d = Mathf.Max(dx, dy); e = Mathf.Min(e, Mathf.Max(0f, d));
                }
                return e;
            }
            // fluffy glass-wool front: pillowy roll at every edge, lumps, slight sag
            float Glass(float x, float y)
            {
                float e = Edge(x, y);
                float pillow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / 0.05f));
                float lump = (Mathf.PerlinNoise(x * 7f + seed, y * 7f) - 0.5f) * (P.Faced ? 0.004f : 0.013f)
                           + (Mathf.PerlinNoise(x * 3.1f + 5f, y * 2.3f + seed) - 0.5f) * (P.Faced ? 0.002f : 0.012f)
                           + (Mathf.PerlinNoise(x * 23f, y * 23f + seed) - 0.5f) * (P.Faced ? 0.0012f : 0.005f);
                float sag = (Mathf.PerlinNoise(x * 1.3f + seed * 0.3f, y * 0.9f) - 0.5f) * 0.007f;
                float z = recess + 0.004f - pillow * (bulge + 0.004f) + lump * pillow + sag * pillow;
                // jammed over an obstacle: the batt is pushed out in a lump (compressed insulation)
                if (!CutAround)
                    foreach (var o in Obstacles)
                    {
                        float dx = Mathf.Max(0f, Mathf.Max(o.R.xMin - x, x - o.R.xMax)), dy = Mathf.Max(0f, Mathf.Max(o.R.yMin - y, y - o.R.yMax));
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float push = Mathf.Max(0f, o.Proud + 0.02f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / 0.08f)));
                        z = Mathf.Min(z, recess - push + (IsBox(o) ? 0f : 0.004f));
                    }
                return pressed ? Mathf.Max(z, 0.0015f) : z;
            }
            // kraft facing rides on the glass: smoother pillow, soft creases, never inside the fluff
            float Paper(float x, float y)
            {
                float e = Edge(x, y);
                float pillow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / 0.035f));
                float crease = Mathf.Pow(Mathf.Abs(Mathf.Sin(y * 7f + Mathf.PerlinNoise(x * 3f, y * 2f + seed) * 6f)), 10f) * 0.0028f;
                float wave = (Mathf.PerlinNoise(x * 2.2f + seed, y * 1.1f) - 0.5f) * 0.004f;
                float z = recess + 0.002f - pillow * (bulge + 0.002f) + wave * pillow - crease * pillow;
                z = Mathf.Min(z, Glass(x, y) - 0.0015f);
                return pressed ? Mathf.Max(z, 0.0004f) : z;
            }
            float Back(float x, float y) => recess + depth;
            var g = sm.Heightfield(R, holes, Units.In(1f), Glass, 0);
            sm.Skirts(g, Back, 0, 0.007f);
            if (P.Faced)
            {
                // paper stops ~5/16" short of the studs where it folds into the flanges: pink fluff shows in the fold
                float side = Stapled && !Inset ? Units.In(0.3f) : Units.In(0.15f);
                var pr = Rect.MinMaxRect(R.xMin + side, R.yMin, R.xMax - side, R.yMax);
                var pg = sm.Heightfield(pr, holes, Units.In(1f), Paper, 1);
                sm.Skirts(pg, (x, y) => Paper(x, y) + 0.003f, 1, 0f);
            }

            if (P.Faced && (!Inset || !Stapled))
            {
                // flanges: ~3/4" paper tabs over the stud faces (folded loosely until stapled)
                float fw = Units.In(0.75f), zf = pressed ? 0.0002f : Stapled ? -0.0005f : -0.004f;
                var segs = FlangeSpans();
                foreach (var s in segs)
                {
                    float x0 = s.x < 0 ? R.xMin - fw : R.xMax, x1 = s.x < 0 ? R.xMin : R.xMax + fw;
                    float tilt = Stapled ? 0f : 0.003f;
                    Vector3 a = new Vector3(x0, s.y, zf - (s.x < 0 ? 0 : tilt)), b = new Vector3(x1, s.y, zf - (s.x < 0 ? tilt : 0));
                    Vector3 c = new Vector3(x1, s.z, b.z), d = new Vector3(x0, s.z, a.z);
                    sm.FlatQuad(a, b, c, d, 1, Vector3.back, Vector3.right, Vector3.up);
                    if (Stapled)
                    {
                        float sp = Units.In(InsulationRules.StapleSpacingIn);
                        float xc = (x0 + x1) * 0.5f;
                        for (float y = s.y + Units.In(2f); y < s.z - Units.In(1f); y += sp)
                        {
                            float hw = Units.In(0.02f), hl = Units.In(0.1875f);
                            sm.FlatQuad(new Vector3(xc - hw, y - hl, zf - 0.0004f), new Vector3(xc + hw, y - hl, zf - 0.0004f),
                                        new Vector3(xc + hw, y + hl, zf - 0.0004f), new Vector3(xc - hw, y + hl, zf - 0.0004f), 2, Vector3.back, Vector3.right, Vector3.up);
                        }
                    }
                }
            }
            var mesh = sm.Build("Batt");
            var glass = FinishMaterials.Get(P.Yellow ? InsulationLooks.Yellow : InsulationLooks.Pink);
            var mats = new[] { glass, FinishMaterials.Get(InsulationLooks.Kraft), FinishMaterials.Get(InsulationLooks.Staple) };
            var mf = GetComponent<MeshFilter>();
            if (mf == null)
            {
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                gameObject.AddComponent<MeshRenderer>().sharedMaterials = mats;
                var col = gameObject.AddComponent<BoxCollider>();
                col.center = new Vector3(R.center.x, R.center.y, recess + depth * 0.5f - 0.004f);
                col.size = new Vector3(R.width, R.height, depth + 0.008f);
            }
            else PanelMesh.Replace(gameObject, mesh, mats);
        }

        /// <summary>Flange runs (x = side -1/+1, y..z = v range) — interrupted where a hole meets the stud.</summary>
        List<Vector3> FlangeSpans()
        {
            return new List<Vector3> { new Vector3(-1, R.yMin, R.yMax), new Vector3(1, R.yMin, R.yMax) };
        }

        static Rect Expand(Rect r, float m) => Rect.MinMaxRect(r.xMin - m, r.yMin - m, r.xMax + m, r.yMax + m);

        /// <summary>World point at the batt's face center (for markers).</summary>
        public Vector3 FaceCenter => Fp.World(R.center, 0.02f);

        void OnDestroy() { Insulation.Batts.Remove(this); }
    }
}
