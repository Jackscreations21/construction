using System.Collections.Generic;
using UnityEngine;
using static Groundbreak.FinishNoise;

namespace Groundbreak
{
    /// <summary>Procedural, seamless looks for the exterior envelope (keys prefixed "ext-"): spunbond house wrap with a
    /// printed band, seam & flashing tape, embossed vinyl, cedar-embossed fiber cement, rough-sawn panel siding,
    /// vented soffit. Colors come from tinting (vinyl/fiber cement colors).</summary>
    public static class ExteriorLooks
    {
        public const string Wrap = "ext-wrap", Tape = "ext-tape", FlashTape = "ext-flash", Vinyl = "ext-vinyl", FiberCement = "ext-fc",
            PanelSiding = "ext-bb", Soffit = "ext-soffit", Trim = "ext-trim", Alum = "aluminum-white";

        /// <summary>Vinyl colors (name, sRGB).</summary>
        public static readonly (string name, Color color)[] VinylColors =
        {
            ("White", new Color(0.93f, 0.93f, 0.91f)),
            ("Clay", new Color(0.80f, 0.74f, 0.62f)),
            ("Gray", new Color(0.62f, 0.64f, 0.63f)),
            ("Sage", new Color(0.60f, 0.66f, 0.54f)),
            ("Wicker", new Color(0.84f, 0.78f, 0.62f)),
            ("Slate blue", new Color(0.47f, 0.55f, 0.63f)),
            ("Pebble", new Color(0.74f, 0.72f, 0.66f)),
        };

        /// <summary>Fiber cement factory colors (and primed).</summary>
        public static readonly (string name, Color color)[] FiberColors =
        {
            ("Primed", new Color(0.86f, 0.86f, 0.83f)),
            ("Arctic white", new Color(0.92f, 0.92f, 0.90f)),
            ("Iron gray", new Color(0.36f, 0.38f, 0.39f)),
            ("Evening blue", new Color(0.26f, 0.32f, 0.40f)),
            ("Khaki brown", new Color(0.55f, 0.48f, 0.38f)),
            ("Mountain sage", new Color(0.53f, 0.57f, 0.48f)),
        };

        public static Color VinylColor(string name)
        {
            foreach (var c in VinylColors) if (string.Equals(c.name, name, System.StringComparison.OrdinalIgnoreCase)) return c.color;
            return VinylColors[0].color;
        }
        public static Color FiberColor(string name)
        {
            foreach (var c in FiberColors) if (string.Equals(c.name, name, System.StringComparison.OrdinalIgnoreCase)) return c.color;
            return FiberColors[0].color;
        }

        static bool _done;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (_done) return; _done = true;
            FinishMaterials.Register(Wrap, new FinishMaterials.Spec { Gen = WrapGen, Tile = 1.0f, Gloss = 0.22f, Bump = 0.5f, NormalStrength = 1.5f });
            FinishMaterials.Register(Tape, new FinishMaterials.Spec { Gen = () => FinishMaterials.FineSurface(C(150, 178, 208), 5101, 0.02f, 64), Tile = 0.3f, Gloss = 0.6f, Bump = 0.2f });
            FinishMaterials.Register(FlashTape, new FinishMaterials.Spec { Gen = FlashGen, Tile = 0.4f, Gloss = 0.45f, Bump = 0.6f, NormalStrength = 2f });
            FinishMaterials.Register(Vinyl, new FinishMaterials.Spec { Gen = VinylGen, Tile = 0.8f, Gloss = 0.32f, Bump = 0.9f, NormalStrength = 3f });
            FinishMaterials.Register(FiberCement, new FinishMaterials.Spec { Gen = CedarGen, Tile = 0.6f, Gloss = 0.1f, Bump = 0.6f, NormalStrength = 1.6f });
            FinishMaterials.Register(PanelSiding, new FinishMaterials.Spec { Gen = RoughSawnGen, Tile = 0.6f, Gloss = 0.08f, Bump = 0.6f, NormalStrength = 1.8f });
            FinishMaterials.Register(Soffit, new FinishMaterials.Spec { Gen = SoffitGen, Tile = Units.In(4f), Gloss = 0.3f, Bump = 0.8f, NormalStrength = 3f });
            FinishMaterials.Register(Trim, new FinishMaterials.Spec { Gen = () => FinishMaterials.FineSurface(C(240, 240, 236), 5111, 0.02f, 80), Tile = 0.5f, Gloss = 0.28f, Bump = 0.3f });
        }

        // ================================================================== generators

        static int Wp(int i, int n) { i %= n; return i < 0 ? i + n : i; }

        /// <summary>Spunbond polyethylene: random continuous filaments, soft wrinkles from handling/stapling, and a printed
        /// band of generic text-free marks across the middle of each 1 m tile.</summary>
        static FinishCanvas WrapGen()
        {
            const int n = 512; var c = new FinishCanvas(n);
            var fib = new float[n * n];
            var rng = new System.Random(4401);
            for (int k = 0; k < 6500; k++)
            {
                float x = (float)rng.NextDouble() * n, y = (float)rng.NextDouble() * n;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, len = 10f + (float)rng.NextDouble() * 50f;
                float w = 0.05f + (float)rng.NextDouble() * 0.08f;
                for (float s = 0; s < len; s += 0.7f)
                {
                    x += Mathf.Cos(a) * 0.7f; y += Mathf.Sin(a) * 0.7f; a += ((float)rng.NextDouble() - 0.5f) * 0.12f;
                    fib[Wp((int)y, n) * n + Wp((int)x, n)] += w;
                }
            }
            fib = Blur(fib, n, 1);
            var wr = Field(n, 9, 3, 3, 0.5f, 4402);
            var macro = Field(128, 3, 3, 3, 0.5f, 4403);
            Color baseC = C(228, 231, 230), ink = C(70, 118, 186);
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) / n;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n; int i = y * n + x;
                    float f = Mathf.Clamp01(fib[i] * 1.6f);
                    float r = 1f - Mathf.Abs(wr[i] * 2f - 1f);
                    float crease = Mathf.Pow(r, 7f);
                    float m = Sample(macro, 128, u, v) - 0.5f;
                    Color col = baseC * (1f + f * 0.07f + m * 0.03f - crease * 0.03f);
                    // printed band: v 0.47..0.53, repeat 4x per tile
                    float ink01 = PrintMark(u * 4f % 1f, (v - 0.47f) / 0.06f);
                    col = Color.Lerp(col, ink, ink01 * 0.85f);
                    c.Set(i, col);
                    c.H[i] = f * 0.35f + (wr[i] - 0.5f) * 0.5f - crease * 0.35f;
                    c.S[i] = 1f - f * 0.3f + ink01 * 0.2f;
                }
            }
            return c;
        }

        /// <summary>Generic print mark in a unit cell (x 0..1 along the band, y 0..1 across): a ring emblem and two bars.</summary>
        static float PrintMark(float x, float y)
        {
            if (y < 0f || y > 1f) return 0f;
            float a = 0f;
            // ring emblem
            float dx = (x - 0.1f) * 4.2f, dy = y - 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
            a = Mathf.Max(a, Smooth(0.06f, 0.0f, Mathf.Abs(d - 0.36f)) );
            a = Mathf.Max(a, Smooth(0.2f, 0.12f, d) * 0.9f);
            // two bars
            if (x > 0.2f && x < 0.9f)
            {
                float edge = Smooth(0.2f, 0.215f, x) * Smooth(0.9f, 0.885f, x);
                a = Mathf.Max(a, edge * Smooth(0.06f, 0.0f, Mathf.Abs(y - 0.33f) - 0.09f));
                if (x < 0.7f) a = Mathf.Max(a, edge * Smooth(0.06f, 0.0f, Mathf.Abs(y - 0.72f) - 0.05f) * 0.8f);
            }
            return Mathf.Clamp01(a);
        }

        /// <summary>Black butyl flashing tape: matte, faint crinkles.</summary>
        static FinishCanvas FlashGen()
        {
            const int n = 256; var c = new FinishCanvas(n);
            var cr = Field(n, 12, 12, 3, 0.55f, 4501);
            var fine = Field(n, 64, 64, 2, 0.5f, 4502);
            for (int i = 0; i < n * n; i++)
            {
                float r = 1f - Mathf.Abs(cr[i] * 2f - 1f);
                c.Set(i, C(38, 38, 40) * (1f + (fine[i] - 0.5f) * 0.15f + Mathf.Pow(r, 5f) * 0.4f));
                c.H[i] = Mathf.Pow(r, 4f) * 0.8f + fine[i] * 0.2f;
                c.S[i] = 0.8f + r * 0.3f;
            }
            return c;
        }

        /// <summary>Vinyl woodgrain emboss: fine horizontal grain streaks, almost no color change (tinted per color).</summary>
        static FinishCanvas VinylGen()
        {
            const int n = 512; var c = new FinishCanvas(n);
            var grain = Field(n, 2, 48, 4, 0.55f, 4601);
            var streak = Field(n, 1, 110, 2, 0.5f, 4602);
            var warp = Field(128, 2, 4, 2, 0.5f, 4603);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x; float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float w = (Sample(warp, 128, u, v) - 0.5f) * 0.05f;
                    float g = Sample(grain, n, u, v + w) - 0.5f, s = Sample(streak, n, u, v + w) - 0.5f;
                    float line = Mathf.Pow(1f - Mathf.Abs(s * 2f), 6f);
                    c.Set(i, Color.white * (0.97f + g * 0.04f - line * 0.03f));
                    c.H[i] = g * 0.9f - line * 0.6f;
                    c.S[i] = 1f - line * 0.3f;
                }
            return c;
        }

        /// <summary>Fiber cement cedar emboss: deeper grain, occasional long fissures, matte primed/painted.</summary>
        static FinishCanvas CedarGen()
        {
            const int n = 512; var c = new FinishCanvas(n);
            var grain = Field(n, 3, 64, 4, 0.6f, 4701);
            var ring = Field(n, 2, 24, 3, 0.5f, 4702);
            var fis = Field(n, 1, 70, 2, 0.5f, 4703);
            for (int i = 0; i < n * n; i++)
            {
                float g = grain[i] - 0.5f, r = ring[i] - 0.5f;
                float f = Mathf.Pow(1f - Mathf.Abs(fis[i] * 2f - 1f), 14f);
                float band = Mathf.Sin(r * 40f) * 0.5f;
                c.Set(i, Color.white * (0.95f + g * 0.06f + band * 0.02f - f * 0.15f));
                c.H[i] = g * 0.6f + band * 0.3f - f * 0.8f;
                c.S[i] = 1f - f * 0.5f;
            }
            return c;
        }

        /// <summary>Rough-sawn panel siding: fuzzy grain plus band-saw marks across the grain (painted).</summary>
        static FinishCanvas RoughSawnGen()
        {
            const int n = 512; var c = new FinishCanvas(n);
            var grain = Field(n, 3, 70, 4, 0.6f, 4801);
            var fuzz = Field(n, 96, 96, 2, 0.5f, 4802);
            var wob = Field(128, 2, 2, 2, 0.5f, 4803);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x; float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float saw = Mathf.Sin((u * 26f + (Sample(wob, 128, u, v) - 0.5f) * 0.6f) * Mathf.PI * 2f);
                    float g = grain[i] - 0.5f, fz = fuzz[i] - 0.5f;
                    c.Set(i, Color.white * (0.94f + g * 0.06f + saw * 0.005f + fz * 0.03f));
                    c.H[i] = g * 0.8f + saw * 0.08f + fz * 0.35f;
                    c.S[i] = 0.9f + fz * 0.2f;
                }
            return c;
        }

        /// <summary>Vented vinyl soffit, one 4" segment per tile across (y): perforation rows in the middle band.</summary>
        static FinishCanvas SoffitGen()
        {
            const int n = 256; var c = new FinishCanvas(n);
            var fine = Field(n, 32, 32, 2, 0.5f, 4901);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x; float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float hole = 0f;
                    if (v > 0.3f && v < 0.7f)
                    {
                        // 5 x 4 holes per tile (about 3/16" pitch)
                        float gx = u * 10f, gy = (v - 0.3f) / 0.4f * 4f;
                        float off = (Mathf.FloorToInt(gy) % 2) * 0.5f;
                        float fx = (gx + off) % 1f - 0.5f, fy = gy % 1f - 0.5f;
                        float d = Mathf.Sqrt(fx * fx + fy * fy * 1.6f);
                        hole = Smooth(0.28f, 0.2f, d);
                    }
                    c.Set(i, Color.white * (0.97f + (fine[i] - 0.5f) * 0.03f) * (1f - hole * 0.85f));
                    c.H[i] = (fine[i] - 0.5f) * 0.2f - hole * 1.2f;
                    c.S[i] = 1f - hole * 0.8f;
                }
            return c;
        }
    }

    /// <summary>Small mesh helpers for face-local exterior geometry (x = u, y = v, z = -proud).</summary>
    public static class ExtGeo
    {
        public static readonly Vector3 Out = Vector3.back; // face-local outward normal

        /// <summary>Axis-aligned box from min/max (face-local).</summary>
        public static void Box(MeshBuilder b, float x0, float x1, float y0, float y1, float z0, float z1, int sub, Vector3 uHint = default, int mask = MeshBuilder.AllFaces)
        {
            if (x1 - x0 <= 1e-5f || y1 - y0 <= 1e-5f || z1 - z0 <= 1e-5f) return;
            b.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, y1 - y0, z1 - z0), Quaternion.identity, sub, uHint == default ? Vector3.right : uHint, mask);
        }

        /// <summary>Flat quad facing outward (-z) at depth z.</summary>
        public static void Front(MeshBuilder b, float x0, float x1, float y0, float y1, float z, int sub, Vector3 uHint = default)
        {
            if (x1 - x0 <= 1e-5f || y1 - y0 <= 1e-5f) return;
            b.AddQuad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), sub, Out, uHint == default ? Vector3.right : uHint);
        }

        /// <summary>Thin sheet (double-sided) swept along an axis: profile points p map to origin + px*p.x + py*p.y.</summary>
        public static void Sheet(MeshBuilder b, IList<Vector2> prof, Vector3 origin, Vector3 px, Vector3 py, Vector3 axis, float len, int sub, bool both = true)
        {
            Vector3 ax = axis.normalized * len;
            for (int i = 0; i + 1 < prof.Count; i++)
            {
                Vector2 p = prof[i], q = prof[i + 1];
                Vector2 e = q - p; if (e.sqrMagnitude < 1e-12f) continue;
                Vector3 A = origin + px * p.x + py * p.y, B = origin + px * q.x + py * q.y;
                Vector3 n = Vector3.Cross(B - A, ax).normalized;
                b.AddQuad(A, B, B + ax, A + ax, sub, n, axis);
                if (both) b.AddQuad(A, B, B + ax, A + ax, sub, -n, axis);
            }
        }

        /// <summary>Closed solid sweep of a simple polygon (any shape, no caps): side quads with outward normals.</summary>
        public static void Tube(MeshBuilder b, IList<Vector2> poly, Vector3 origin, Vector3 px, Vector3 py, Vector3 axis, float len, int sub)
        {
            float area = 0f;
            for (int i = 0; i < poly.Count; i++) { var p = poly[i]; var q = poly[(i + 1) % poly.Count]; area += p.x * q.y - q.x * p.y; }
            float s = area >= 0f ? 1f : -1f;
            Vector3 ax = axis.normalized * len;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % poly.Count];
                Vector2 e = q - p; if (e.sqrMagnitude < 1e-12f) continue;
                Vector2 n2 = new Vector2(e.y, -e.x) * s;
                Vector3 A = origin + px * p.x + py * p.y, B = origin + px * q.x + py * q.y;
                b.AddQuad(A, B, B + ax, A + ax, sub, px * n2.x + py * n2.y, axis);
            }
        }

        /// <summary>Intervals [a0,a1] minus the given cuts.</summary>
        public static List<Vector2> Subtract(float a0, float a1, List<Vector2> cuts)
        {
            var res = new List<Vector2> { new Vector2(a0, a1) };
            foreach (var c in cuts)
            {
                var next = new List<Vector2>();
                foreach (var r in res)
                {
                    if (c.y <= r.x || c.x >= r.y) { next.Add(r); continue; }
                    if (c.x > r.x) next.Add(new Vector2(r.x, c.x));
                    if (c.y < r.y) next.Add(new Vector2(c.y, r.y));
                }
                res = next;
            }
            res.RemoveAll(r => r.y - r.x < 1e-4f);
            return res;
        }

        public static float In(float inches) => Units.In(inches);

        public static Mesh Build(MeshBuilder b, string name, int subs)
        {
            b.MinSubmeshCount = subs;
            return b.Build(name);
        }
    }
}
