using UnityEngine;

namespace Groundbreak
{
    /// <summary>Procedural looks for insulation: fiberglass (pink / yellow), kraft facing, closed-cell spray foam,
    /// can foam beads, loose fill, 6-mil poly, foam eave baffles, cardboard depth rulers. All seamless (FinishNoise).</summary>
    public static class InsulationLooks
    {
        public const string Pink = "ins-glass-pink", Yellow = "ins-glass-yellow", Kraft = "ins-kraft", Foam = "ins-foam-cc",
            CanFoam = "ins-foam-can", LooseFill = "ins-loosefill", Poly = "ins-poly", Baffle = "ins-baffle", Ruler = "ins-ruler", Staple = "ins-staple";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            FinishMaterials.Register(Pink, new FinishMaterials.Spec { Gen = () => Fiberglass(new Color(0.95f, 0.56f, 0.62f), 4101), Tile = 0.22f, Gloss = 0.12f, Bump = 1.4f, NormalStrength = 7f });
            FinishMaterials.Register(Yellow, new FinishMaterials.Spec { Gen = () => Fiberglass(new Color(0.95f, 0.86f, 0.55f), 4111), Tile = 0.22f, Gloss = 0.12f, Bump = 1.4f, NormalStrength = 7f });
            FinishMaterials.Register(Kraft, new FinishMaterials.Spec { Gen = KraftPaper, Tile = 0.6f, Gloss = 0.2f, Bump = 0.5f, NormalStrength = 1.6f });
            FinishMaterials.Register(Foam, new FinishMaterials.Spec { Gen = () => FoamCells(new Color(0.93f, 0.84f, 0.56f), 4131, 26), Tile = 0.25f, Gloss = 0.18f, Bump = 1.0f, NormalStrength = 5f });
            FinishMaterials.Register(CanFoam, new FinishMaterials.Spec { Gen = () => FoamCells(new Color(0.95f, 0.88f, 0.62f), 4141, 40), Tile = 0.12f, Gloss = 0.25f, Bump = 0.8f, NormalStrength = 4f });
            FinishMaterials.Register(LooseFill, new FinishMaterials.Spec { Gen = LooseFillTex, Tile = 0.35f, Gloss = 0.08f, Bump = 1.5f, NormalStrength = 8f });
            FinishMaterials.Register(Poly, new FinishMaterials.Spec { Gen = PolyFilm, Tile = 1.2f, Gloss = 0.88f, Bump = 0.6f, NormalStrength = 2f, Mode = FinishMaterials.Blend.Transparent });
            FinishMaterials.Register(Baffle, new FinishMaterials.Spec { Gen = BaffleFoam, Tile = 0.2f, Gloss = 0.15f, Bump = 0.6f, NormalStrength = 3f });
            FinishMaterials.Register(Ruler, new FinishMaterials.Spec { Gen = RulerCard, Tile = 0.3048f, Gloss = 0.1f, Bump = 0.2f, NormalStrength = 1f });
            FinishMaterials.Register(Staple, new FinishMaterials.Spec { Gen = null, Tint = new Color(0.78f, 0.79f, 0.80f), Gloss = 0.7f, Metal = 0.9f });
        }

        /// <summary>Glass wool: tangled translucent fibers over a soft base, strong height so light catches the strands.</summary>
        public static FinishCanvas Fiberglass(Color baseC, int seed)
        {
            const int n = 512; var c = new FinishCanvas(n);
            var macro = FinishNoise.Field(n, 4, 4, 4, 0.55f, seed);
            var tuft = FinishNoise.Field(n, 7, 7, 4, 0.6f, seed + 3);
            var h = new float[n * n]; var lum = new float[n * n];
            for (int i = 0; i < n * n; i++) { h[i] = macro[i] * 0.5f + tuft[i] * 0.6f; lum[i] = 0f; }
            // strands: curved polylines, wrapped
            uint s = (uint)seed * 2654435761u;
            float Rnd() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216f; }
            for (int k = 0; k < 9000; k++)
            {
                float x = Rnd() * n, y = Rnd() * n, ang = Rnd() * Mathf.PI * 2f, curl = (Rnd() - 0.5f) * 0.25f;
                int len = 12 + (int)(Rnd() * 70f);
                float bright = Rnd() < 0.5f ? 0.35f + Rnd() * 0.4f : -0.25f - Rnd() * 0.3f;
                float hh = 0.25f + Rnd() * 0.5f;
                for (int t = 0; t < len; t++)
                {
                    int xi = ((int)x % n + n) % n, yi = ((int)y % n + n) % n, i = yi * n + xi;
                    lum[i] += bright * 0.35f; h[i] += hh * 0.35f;
                    x += Mathf.Cos(ang); y += Mathf.Sin(ang); ang += curl;
                }
            }
            var hb = FinishNoise.Blur(h, n, 1);
            for (int i = 0; i < n * n; i++)
            {
                float m = macro[i] - 0.5f, l = Mathf.Clamp(lum[i], -0.5f, 0.6f);
                float ao = Mathf.Lerp(0.55f, 1.08f, Mathf.Clamp01((hb[i] - 0.15f) * 1.1f));
                Color col = baseC * ao * (1f + m * 0.18f);
                col = Color.Lerp(col, l > 0 ? Color.Lerp(baseC, Color.white, 0.55f) : baseC * 0.62f, Mathf.Abs(l));
                c.Set(i, col);
                c.H[i] = hb[i] * 0.8f + h[i] * 0.2f;
                c.S[i] = 0.5f + Mathf.Max(0f, l) * 1.2f;
            }
            return c;
        }

        /// <summary>Kraft facing: brown paper with fibers, crinkles and faint asphalt-laminate mottling.</summary>
        static FinishCanvas KraftPaper()
        {
            // tile = 0.6 m; V (y) runs up the wall. Brown kraft with fine fibers, faint asphalt bleed-through mottling,
            // and long soft vertical wrinkles where the paper was rolled with the batt.
            const int n = 512; var c = new FinishCanvas(n);
            var mott = FinishNoise.Field(n, 4, 4, 4, 0.55f, 4201);
            var fib = FinishNoise.Field(n, 128, 128, 2, 0.5f, 4202);
            var streak = FinishNoise.Field(n, 96, 6, 2, 0.5f, 4204);
            var wr = FinishNoise.Field(n, 18, 2, 2, 0.5f, 4203);
            var baseC = new Color(0.64f, 0.47f, 0.30f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float m = mott[i] - 0.5f, f = fib[i] - 0.5f, s = streak[i] - 0.5f;
                    float w = wr[i]; float crease = Mathf.Pow(1f - Mathf.Abs(w - 0.5f) * 2f, 8f);
                    float spot = mott[i] > 0.68f ? (mott[i] - 0.68f) * 0.6f : 0f;
                    c.Set(i, baseC * (1f + m * 0.10f + f * 0.07f + s * 0.05f - spot - crease * 0.04f));
                    c.H[i] = w * 0.35f + crease * 0.25f + f * 0.12f;
                    c.S[i] = 0.9f + crease * 0.3f;
                }
            return c;
        }

        /// <summary>Spray foam: popcorn-like closed cells with lumps and slightly glossy skin.</summary>
        public static FinishCanvas FoamCells(Color baseC, int seed, int cells)
        {
            const int n = 256; var c = new FinishCanvas(n);
            var lump = FinishNoise.Field(n, 4, 4, 3, 0.5f, seed);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    FinishNoise.Worley((x + 0.5f) / n, (y + 0.5f) / n, cells, seed, out float f1, out float f2, out int id);
                    float cell = Mathf.Clamp01(1f - f1 * 1.4f);
                    float edge = Mathf.Clamp01((f2 - f1) * 4f);
                    float l = lump[i] - 0.5f;
                    float tone = 1f + l * 0.14f + (FinishNoise.Hf(id, 3, seed) - 0.5f) * 0.06f - (1f - edge) * 0.08f;
                    c.Set(i, baseC * tone);
                    c.H[i] = cell * 0.6f + lump[i] * 0.8f;
                    c.S[i] = 0.6f + cell * 0.8f;
                }
            return c;
        }

        /// <summary>Blown fiberglass: cotton-candy clumps (white with a faint pink cast), deep crevices.</summary>
        static FinishCanvas LooseFillTex()
        {
            // cotton-candy tufts: several octaves of soft blobs, deep soft shadows between them, wispy fibers on top
            const int n = 512; var c = new FinishCanvas(n);
            var big = FinishNoise.Field(n, 6, 6, 3, 0.55f, 4301);
            var mid = FinishNoise.Field(n, 18, 18, 3, 0.55f, 4302);
            var fine = FinishNoise.Field(n, 64, 64, 2, 0.5f, 4303);
            var baseC = new Color(0.84f, 0.79f, 0.77f);
            var h = new float[n * n];
            for (int i = 0; i < n * n; i++)
            {
                float b = Mathf.SmoothStep(0.3f, 0.75f, big[i]), m = Mathf.SmoothStep(0.35f, 0.7f, mid[i]);
                h[i] = b * 0.55f + m * 0.35f + (fine[i] - 0.5f) * 0.25f;
            }
            var fg = Fiberglass(baseC, 4304);   // reuse the strand pass for wisps
            for (int i = 0; i < n * n; i++)
            {
                float ao = Mathf.Lerp(0.5f, 1.04f, Mathf.Clamp01(h[i] * 1.25f));
                Color strand = new Color(fg.R[i], fg.G[i], fg.B[i]);
                c.Set(i, Color.Lerp(baseC, strand, 0.45f) * ao);
                c.H[i] = h[i] * 0.8f + fg.H[i] * 0.2f;
                c.S[i] = 0.5f;
            }
            return c;
        }

        /// <summary>6-mil poly: clear with a milky cast, wrinkles and creases (alpha = how much light it scatters).</summary>
        static FinishCanvas PolyFilm()
        {
            const int n = 256; var c = new FinishCanvas(n);
            var wr = FinishNoise.Field(n, 3, 6, 4, 0.6f, 4401);
            for (int i = 0; i < n * n; i++)
            {
                float w = wr[i];
                float crease = 1f - Mathf.Abs(w - 0.5f) * 2f; crease = Mathf.Pow(crease, 6f);
                c.Set(i, new Color(0.86f, 0.88f, 0.90f));
                c.A[i] = 0.16f + crease * 0.28f + (w - 0.5f) * 0.06f;
                c.H[i] = w * 0.8f + crease * 0.3f;
            }
            return c;
        }

        /// <summary>EPS foam baffle: white expanded-polystyrene beads.</summary>
        static FinishCanvas BaffleFoam()
        {
            const int n = 256; var c = new FinishCanvas(n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    FinishNoise.Worley((x + 0.5f) / n, (y + 0.5f) / n, 36, 4501, out float f1, out float f2, out int id);
                    float bead = Mathf.Clamp01(1f - f1 * 1.5f), seam = Mathf.Clamp01((f2 - f1) * 6f);
                    c.Set(i, new Color(0.95f, 0.95f, 0.96f) * (0.9f + bead * 0.1f - (1f - seam) * 0.05f + (FinishNoise.Hf(id, 1, 4502) - 0.5f) * 0.04f));
                    c.H[i] = bead * 0.5f;
                }
            return c;
        }

        /// <summary>Cardboard attic ruler: white card with black inch ticks (one foot per tile, ticks along V).</summary>
        static FinishCanvas RulerCard()
        {
            const int n = 256; var c = new FinishCanvas(n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float inch = y / (float)n * 12f, fr = inch - Mathf.Floor(inch);
                    bool tick = fr < 0.06f && x < (Mathf.RoundToInt(inch) % 6 == 0 ? n * 0.6f : n * 0.3f);
                    bool half = Mathf.Abs(fr - 0.5f) < 0.03f && x < n * 0.18f;
                    var col = (tick || half) ? new Color(0.08f, 0.08f, 0.08f) : new Color(0.93f, 0.92f, 0.88f);
                    c.Set(i, col);
                }
            return c;
        }
    }
}
