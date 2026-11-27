using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Low-poly vegetation for large-scale scatter (thousands merged into map cell meshes). Real-ish proportions,
    /// irregular silhouettes: deciduous crowns are clusters of soft lathe blobs (wider at the bottom of the crown, darker
    /// underneath), conifers are layered drooping whorls. Positions are in the current MapBuild frame; everything is drawn
    /// upright and starts at y = pos.y. Triangle counts: Deciduous ~190-330, Conifer ~180-210, Bush 70.
    /// </summary>
    public static class MapTrees
    {
        static readonly Color[] LeafTints =
        {
            new Color(0.86f, 1.00f, 0.74f, 1f),   // fresh green
            new Color(0.72f, 0.86f, 0.60f, 1f),   // mid
            new Color(1.00f, 1.02f, 0.78f, 1f),   // yellow-green (honey locust, cottonwood)
            new Color(0.58f, 0.72f, 0.50f, 1f),   // dark (oak, maple in shade)
            new Color(0.80f, 0.92f, 0.66f, 1f),
        };
        static readonly Color[] ConiferTints =
        {
            new Color(0.52f, 0.70f, 0.62f, 1f),   // blue-green spruce
            new Color(0.46f, 0.60f, 0.52f, 1f),   // dark fir / cedar
            new Color(0.60f, 0.74f, 0.56f, 1f),   // pine
        };
        static readonly Color[] BushTints =
        {
            new Color(0.80f, 0.95f, 0.70f, 1f), new Color(0.64f, 0.80f, 0.56f, 1f), new Color(0.92f, 0.98f, 0.70f, 1f),
        };

        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        /// <summary>Soft blob around local Y: closed bottom (flattish), widest just below the middle, rounded top.
        /// 3 profile segments → 6 * segs triangles.</summary>
        static Vector2[] BlobProfile(float r, float h, float topRound)
        {
            return new[]
            {
                new Vector2(0.001f, -h * 0.42f),
                new Vector2(r, -h * 0.10f),
                new Vector2(r * topRound, h * 0.26f),
                new Vector2(0.001f, h * 0.5f),
            };
        }

        /// <summary>Broadleaf shade tree. height = total height (m), ~6-25.</summary>
        public static void Deciduous(MapBuild b, Vector3 pos, float height, int seed)
        {
            var rng = new System.Random(seed * 7919 + 17);
            float R() => (float)rng.NextDouble();
            height = Mathf.Max(1.5f, height);
            Color tint = LeafTints[rng.Next(LeafTints.Length)];
            int bark = b.K.M(LandSurface.Bark);
            int leaf = b.K.M(LandSurface.Foliage, tint), leafDk = b.K.M(LandSurface.Foliage, Shade(tint, 0.76f));

            float crownBase = height * (0.28f + 0.1f * R());        // where the crown starts (bottom of foliage)
            float crownH = height - crownBase;
            float crownR = height * (0.3f + 0.1f * R());             // half spread
            float trunkR = Mathf.Max(0.05f, height * 0.019f);

            // trunk with root flare, faceted (6 sides) and running up into the crown
            b.B.AddLathe(pos, Quaternion.Euler(0f, R() * 60f, 0f), new[]
            {
                new Vector2(trunkR * 1.4f, 0f), new Vector2(trunkR, height * 0.07f), new Vector2(trunkR * 0.5f, crownBase + crownH * 0.45f),
            }, 6, bark);

            // 2-3 scaffold limbs, visible under the crown edge
            int limbs = 2 + rng.Next(2);
            for (int i = 0; i < limbs; i++)
            {
                float a = (i + R() * 0.5f) * Mathf.PI * 2f / limbs;
                var dir = new Vector3(Mathf.Cos(a), 1.1f + R() * 0.5f, Mathf.Sin(a)).normalized;
                float len = crownR * (0.55f + 0.2f * R());
                Vector3 start = pos + Vector3.up * (crownBase * (0.85f + 0.1f * R()));
                b.B.AddFrustum(start + dir * (len * 0.5f), Quaternion.FromToRotation(Vector3.up, dir), trunkR * 0.55f, trunkR * 0.18f, len, 4, bark, false);
            }

            // crown: a ring of big low clumps (widest part of the crown), one or two mid clumps, one top clump
            int count = 4 + rng.Next(3) + (height > 12f ? 1 : 0);
            int ring = count - 2;
            float ringA = R() * Mathf.PI * 2f;
            for (int i = 0; i < count; i++)
            {
                Vector3 c; float r, h; int mat;
                if (i < ring)
                {
                    float a = ringA + (i + (R() - 0.5f) * 0.5f) * Mathf.PI * 2f / ring;
                    float d = crownR * (0.36f + 0.16f * R());
                    c = new Vector3(Mathf.Cos(a) * d, crownBase + crownH * (0.3f + 0.12f * R()), Mathf.Sin(a) * d);
                    r = crownR * (0.56f + 0.14f * R());
                    h = crownH * (0.52f + 0.12f * R());
                    mat = leafDk;
                }
                else if (i == ring)
                {
                    float a = R() * Mathf.PI * 2f, d = crownR * 0.2f * R();
                    c = new Vector3(Mathf.Cos(a) * d, crownBase + crownH * (0.5f + 0.06f * R()), Mathf.Sin(a) * d);
                    r = crownR * (0.62f + 0.1f * R());
                    h = crownH * 0.56f;
                    mat = leaf;
                }
                else
                {
                    float a = R() * Mathf.PI * 2f, d = crownR * 0.18f * R();
                    c = new Vector3(Mathf.Cos(a) * d, crownBase + crownH * (0.7f + 0.05f * R()), Mathf.Sin(a) * d);
                    r = crownR * (0.48f + 0.12f * R());
                    h = crownH * 0.5f;
                    mat = leaf;
                }
                // keep the top of every clump inside the tree height
                float top = c.y + h * 0.5f;
                if (top > height) c.y -= top - height;
                var rot = Quaternion.Euler(R() * 10f - 5f, R() * 360f, R() * 10f - 5f);
                b.B.AddLathe(pos + c, rot, BlobProfile(r, h, 0.7f + 0.15f * R()), 7, mat, 80f);
            }
        }

        /// <summary>Spruce / fir / pine: trunk + 4-5 stacked drooping whorls. height = total height (m).</summary>
        public static void Conifer(MapBuild b, Vector3 pos, float height, int seed)
        {
            var rng = new System.Random(seed * 6271 + 5);
            float R() => (float)rng.NextDouble();
            height = Mathf.Max(1.5f, height);
            Color tint = ConiferTints[rng.Next(ConiferTints.Length)];
            int bark = b.K.M(LandSurface.Bark);
            int leaf = b.K.M(LandSurface.Foliage, tint), leafDk = b.K.M(LandSurface.Foliage, Shade(tint, 0.74f));

            float trunkR = Mathf.Max(0.04f, height * 0.017f);
            b.B.AddLathe(pos, Quaternion.Euler(0f, R() * 72f, 0f), new[]
            {
                new Vector2(trunkR * 1.3f, 0f), new Vector2(trunkR, height * 0.1f), new Vector2(trunkR * 0.3f, height * 0.9f),
            }, 5, bark);

            int tiers = height < 7f ? 4 : 5;
            float bottom = height * (0.08f + 0.06f * R());
            float baseR = height * (0.2f + 0.05f * R());
            float span = height - bottom;
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)tiers;
                float y = bottom + span * t * 0.9f;
                float tierH = i == tiers - 1 ? height - y : span * 1.9f / tiers;
                float r = baseR * (1f - 0.78f * t) * (0.9f + 0.2f * R());
                Vector2[] prof = i < tiers - 2
                    ? new[] { new Vector2(r * 0.14f, tierH * 0.3f), new Vector2(r, 0f), new Vector2(r * 0.55f, tierH * 0.42f), new Vector2(0.001f, tierH) }
                    : new[] { new Vector2(r * 0.14f, tierH * 0.3f), new Vector2(r, 0f), new Vector2(0.001f, tierH) };
                var rot = Quaternion.Euler(R() * 6f - 3f, R() * 360f, R() * 6f - 3f);
                b.B.AddLathe(pos + Vector3.up * y, rot, prof, 7, i % 2 == 0 ? leafDk : leaf, 30f);
            }
        }

        /// <summary>Round shrub. size = width (m); height ≈ 0.75 * size.</summary>
        public static void Bush(MapBuild b, Vector3 pos, float size, int seed)
        {
            var rng = new System.Random(seed * 4421 + 3);
            float R() => (float)rng.NextDouble();
            size = Mathf.Max(0.2f, size);
            Color tint = BushTints[rng.Next(BushTints.Length)];
            int leaf = b.K.M(LandSurface.Foliage, tint), dark = b.K.M(LandSurface.Foliage, Shade(tint, 0.78f));
            float h = size * (0.65f + 0.2f * R());
            // main mound: flat bottom on the ground, widest low down
            b.B.AddLathe(pos, Quaternion.Euler(0f, R() * 360f, 0f), new[]
            {
                new Vector2(0.001f, 0f), new Vector2(size * 0.5f, h * 0.32f), new Vector2(size * 0.32f, h * 0.8f), new Vector2(0.001f, h),
            }, 7, dark, 70f);
            // a smaller lighter clump on top, off centre
            float a = R() * Mathf.PI * 2f, d = size * 0.07f;
            float r2 = size * (0.26f + 0.06f * R());
            b.B.AddLathe(pos + new Vector3(Mathf.Cos(a) * d, h * 0.45f, Mathf.Sin(a) * d), Quaternion.Euler(0f, R() * 360f, 0f), new[]
            {
                new Vector2(r2, 0f), new Vector2(r2 * 0.8f, h * 0.38f), new Vector2(0.001f, h * 0.62f),
            }, 7, leaf, 70f);
        }
    }
}
