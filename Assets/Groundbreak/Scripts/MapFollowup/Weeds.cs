using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Colour roles of meadow / weed / stockpile materials (each follows the seasons, see <see cref="WeedLook"/>).</summary>
    public enum WeedTone { Grass, DarkGrass, Straw, Seed, Leaf, Stem, FlowerWhite, FlowerYellow, FlowerBlue, FlowerPurple, DryPatch }

    /// <summary>
    /// Tall grass and weeds that read as weeds (not little cone trees): bent, tapering grass blades in loose tufts, low broadleaf rosettes
    /// (dock / plantain / burdock) with an occasional thin seed stalk, and wildflowers (Queen Anne's lace, goldenrod, chicory, clover).
    /// Real sizes (tufts 0.2-0.7 m), double-sided thin geometry, colours from <see cref="WeedLook"/> so they green up in spring, go tawny in
    /// the fall and straw-coloured in winter. Used by the vacant lots, the build-site fill, the map's open land and the flip houses.
    /// </summary>
    public static class Weeds
    {
        public static int M(LandModel k, WeedTone t) => k.M(WeedLook.Get(t));

        /// <summary>Random weed spot: mostly grass tufts, some broadleaf rosettes and wildflowers. tall = 0..1 (mown edge .. knee-high).</summary>
        public static void Spot(LandModel k, Vector3 c, System.Random rng, float tall = 0.6f, bool flowers = true, int blades = 0)
        {
            double r = rng.NextDouble();
            if (r < 0.58) Tuft(k, c, rng, 0.22f + tall * (0.25f + (float)rng.NextDouble() * 0.3f), rng.NextDouble() < 0.4 ? WeedTone.Straw : rng.NextDouble() < 0.5 ? WeedTone.Grass : WeedTone.DarkGrass, rng.NextDouble() < 0.3, blades);
            else if (r < 0.8) Rosette(k, c, rng, rng.NextDouble() < 0.25 * tall);
            else if (flowers) Wildflowers(k, c, rng, 0.25f + tall * 0.45f);
            else Tuft(k, c, rng, 0.2f + tall * 0.3f, WeedTone.Straw, false);
        }

        /// <summary>A tuft of 8-13 bent, tapering grass blades (height ≈ h), the outer ones arching out; optional seed stems.</summary>
        public static void Tuft(LandModel k, Vector3 c, System.Random rng, float h, WeedTone tone, bool seeds, int blades = 0)
        {
            var B = k.B;
            int sub = M(k, tone), alt = M(k, tone == WeedTone.Straw ? WeedTone.Grass : WeedTone.Straw);
            int n = blades > 0 ? blades : 10 + rng.Next(7);
            for (int i = 0; i < n; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, rr = (float)rng.NextDouble() * 0.09f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var b0 = c + dir * rr;
                float bh = h * (0.55f + (float)rng.NextDouble() * 0.5f);
                float bend = 0.25f + (float)rng.NextDouble() * 0.75f;          // 0 = upright, 1 = arching right over
                float w = 0.011f + (float)rng.NextDouble() * 0.011f;
                Blade(B, b0, dir, bh, w, bend, rng.NextDouble() < 0.18 ? alt : sub);
            }
            if (seeds)
            {
                int seed = M(k, WeedTone.Seed), stem = M(k, WeedTone.Straw);
                int ns = 1 + rng.Next(3);
                for (int i = 0; i < ns; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    float sh = h * (1.15f + (float)rng.NextDouble() * 0.35f);
                    var top = c + Vector3.up * sh + dir * sh * 0.18f;
                    Stalk(B, c, top, 0.003f, stem);
                    // a loose drooping panicle / timothy head: a few small spikelets hanging off the top
                    for (int j = 0; j < 3; j++)
                    {
                        var tip = top + Vector3.down * (0.03f + j * 0.035f) + dir * (0.02f + j * 0.015f) + new Vector3(0, 0, 0);
                        Stalk(B, top + Vector3.down * j * 0.03f, tip, 0.006f, seed);
                    }
                }
            }
        }

        /// <summary>One grass blade: two tapering segments bending away from the tuft centre. Double-sided.</summary>
        public static void Blade(MeshBuilder B, Vector3 b0, Vector3 dir, float h, float w, float bend, int sub)
        {
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var p1 = b0 + Vector3.up * (h * (0.6f - 0.12f * bend)) + dir * (h * 0.22f * bend);
            var p2 = b0 + Vector3.up * (h * (1f - 0.45f * bend * bend)) + dir * (h * (0.25f + 0.55f * bend) * bend + 0.02f);
            Vector3 a0 = b0 - side * w, a1 = b0 + side * w, m0 = p1 - side * w * 0.7f, m1 = p1 + side * w * 0.7f;
            var nrm = Vector3.Cross(side, p1 - b0);
            B.AddQuad(a0, a1, m1, m0, sub, nrm);
            B.AddQuad(a1, a0, m0, m1, sub, -nrm);
            var n2 = Vector3.Cross(side, p2 - p1);
            B.AddTriangle(m0, m1, p2, sub, n2);
            B.AddTriangle(m1, m0, p2, sub, -n2);
        }

        /// <summary>Thin stem from a to b (a flat cross of two quads, width w).</summary>
        public static void Stalk(MeshBuilder B, Vector3 a, Vector3 b, float w, int sub)
        {
            var d = b - a; if (d.sqrMagnitude < 1e-6f) return;
            var s1 = Vector3.Cross(d, Vector3.right).sqrMagnitude > 1e-6f ? Vector3.Cross(d, Vector3.right).normalized * w : Vector3.forward * w;
            var s2 = Vector3.Cross(d, s1).normalized * w;
            foreach (var s in new[] { s1, s2 })
            {
                var n = Vector3.Cross(s, d);
                B.AddQuad(a - s, a + s, b + s * 0.6f, b - s * 0.6f, sub, n);
                B.AddQuad(a + s, a - s, b - s * 0.6f, b + s * 0.6f, sub, -n);
            }
        }

        /// <summary>Low broadleaf rosette (dock / plantain / burdock): 5-8 flat leaves lifted 15-45°, sometimes a thin branched seed stalk.</summary>
        public static void Rosette(LandModel k, Vector3 c, System.Random rng, bool stalk)
        {
            var B = k.B;
            int leaf = M(k, WeedTone.Leaf), dark = M(k, WeedTone.DarkGrass);
            int n = 5 + rng.Next(4);
            float size = 0.13f + (float)rng.NextDouble() * 0.2f;
            float a0 = (float)rng.NextDouble() * 360f;
            for (int i = 0; i < n; i++)
            {
                float a = (a0 + i * 360f / n + (float)rng.NextDouble() * 20f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                float len = size * (0.7f + (float)rng.NextDouble() * 0.5f), wid = len * (0.28f + (float)rng.NextDouble() * 0.12f);
                float lift = (8f + (float)rng.NextDouble() * 22f) * Mathf.Deg2Rad;
                var fwd = dir * Mathf.Cos(lift) + Vector3.up * Mathf.Sin(lift);
                var b0 = c + Vector3.up * 0.01f;
                var mid = b0 + fwd * len * 0.45f;
                var tip = b0 + fwd * len + Vector3.down * len * 0.12f;   // leaf tips droop
                int sub = i % 3 == 0 ? dark : leaf;
                var nrm = Vector3.Cross(side, fwd);
                B.AddQuad(b0, mid + side * wid, tip, mid - side * wid, sub, nrm);
                B.AddQuad(b0, mid - side * wid, tip, mid + side * wid, sub, -nrm);
            }
            if (stalk)
            {   // curly dock: a thin stalk with a few rusty seed clusters on short side branches (top third)
                int stem = M(k, WeedTone.Stem), seed = M(k, WeedTone.Seed);
                float h = 0.5f + (float)rng.NextDouble() * 0.35f;
                var lean = new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 0.12f;
                var top = c + Vector3.up * h + lean;
                Stalk(B, c, top, 0.004f, stem);
                for (int j = 0; j < 4; j++)
                {
                    float t = 0.62f + j * 0.1f;
                    var p = Vector3.Lerp(c, top, t);
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    var e = p + new Vector3(Mathf.Cos(a) * 0.06f, 0.07f, Mathf.Sin(a) * 0.06f);
                    Stalk(B, p, e, 0.008f, seed);
                }
            }
        }

        /// <summary>A few wildflower stems (Queen Anne's lace umbels, goldenrod plumes, chicory / aster heads) with small leaves.</summary>
        public static void Wildflowers(LandModel k, Vector3 c, System.Random rng, float h)
        {
            var B = k.B;
            int stem = M(k, WeedTone.Stem), leaf = M(k, WeedTone.Grass);
            int kind = rng.Next(4);
            int head = M(k, kind == 0 ? WeedTone.FlowerWhite : kind == 1 ? WeedTone.FlowerYellow : kind == 2 ? WeedTone.FlowerBlue : WeedTone.FlowerPurple);
            int n = 3 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                var b0 = c + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.25f, 0, ((float)rng.NextDouble() - 0.5f) * 0.25f);
                float sh = h * (0.7f + (float)rng.NextDouble() * 0.45f);
                var top = b0 + Vector3.up * sh + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.12f, 0, ((float)rng.NextDouble() - 0.5f) * 0.12f);
                Stalk(B, b0, top, 0.0035f, stem);
                // two small leaves on the stem
                var mid = Vector3.Lerp(b0, top, 0.35f);
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                Blade(B, mid, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), 0.1f, 0.012f, 0.9f, leaf);
                switch (kind)
                {
                    case 0:   // flat white umbel 8-12 cm
                        B.AddDisc(top, Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 20f, 0, ((float)rng.NextDouble() - 0.5f) * 20f), 0.04f + (float)rng.NextDouble() * 0.02f, 7, head, true);
                        B.AddDisc(top - Vector3.up * 0.005f, Quaternion.identity, 0.035f, 7, stem, false);
                        break;
                    case 1:   // goldenrod: an arching plume
                        var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        Stalk(B, top - Vector3.up * 0.05f, top + Vector3.up * 0.1f + dir * 0.08f, 0.02f, head);
                        break;
                    default:  // chicory / aster: a few small flat heads
                        for (int j = 0; j < 2; j++)
                        {
                            var p = top - Vector3.up * j * 0.06f + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.06f, 0, ((float)rng.NextDouble() - 0.5f) * 0.06f);
                            B.AddDisc(p, Quaternion.Euler(-60f + (float)rng.NextDouble() * 30f, a * Mathf.Rad2Deg, 0), 0.018f, 6, head, true);
                            B.AddDisc(p, Quaternion.Euler(-60f + (float)rng.NextDouble() * 30f, a * Mathf.Rad2Deg, 0), 0.018f, 6, head, false);
                        }
                        break;
                }
            }
        }

        /// <summary>Low clover / mown-grass patch marker: a few short tufts (for mown edges).</summary>
        public static void Short(LandModel k, Vector3 c, System.Random rng)
        {
            Tuft(k, c, rng, 0.12f + (float)rng.NextDouble() * 0.1f, rng.NextDouble() < 0.5 ? WeedTone.Grass : WeedTone.DarkGrass, false);
        }
    }

    /// <summary>
    /// Season colours for the weed / meadow / stockpile materials made by the map follow-up (green in spring and summer, tawny in the fall,
    /// straw and bare stalks in winter; flat ground patches and stockpiles whiten under snow). Mirrors <see cref="SeasonLook"/>'s season and
    /// snow levels and only re-applies when they change. Materials are named "GBW_*" (SeasonLook leaves them alone).
    /// </summary>
    public class WeedLook : MonoBehaviour
    {
        static readonly Dictionary<WeedTone, Material> _tones = new Dictionary<WeedTone, Material>();
        static readonly List<(Material m, Color summer, int mode)> _snowy = new List<(Material, Color, int)>();   // mode 0 ground, 1 prop top
        static readonly Dictionary<Material, Material> _copies = new Dictionary<Material, Material>();
        public static WeedLook I;
        public static string Key { get; private set; } = "";
        string _applied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _tones.Clear(); _snowy.Clear(); _copies.Clear(); Key = ""; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("WeedLook"); DontDestroyOnLoad(go); go.AddComponent<WeedLook>();
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        static readonly Dictionary<WeedTone, Color> Summer = new Dictionary<WeedTone, Color>
        {
            { WeedTone.Grass, new Color(0.40f, 0.52f, 0.22f) }, { WeedTone.DarkGrass, new Color(0.27f, 0.39f, 0.16f) },
            { WeedTone.Straw, new Color(0.70f, 0.63f, 0.40f) }, { WeedTone.Seed, new Color(0.55f, 0.47f, 0.30f) },
            { WeedTone.Leaf, new Color(0.23f, 0.35f, 0.14f) }, { WeedTone.Stem, new Color(0.38f, 0.45f, 0.22f) },
            { WeedTone.FlowerWhite, new Color(0.93f, 0.92f, 0.86f) }, { WeedTone.FlowerYellow, new Color(0.95f, 0.78f, 0.18f) },
            { WeedTone.FlowerBlue, new Color(0.45f, 0.58f, 0.92f) }, { WeedTone.FlowerPurple, new Color(0.62f, 0.45f, 0.80f) },
            { WeedTone.DryPatch, new Color(0.52f, 0.48f, 0.32f) },
        };

        static Color Seasonal(WeedTone t, Seasons s, int snow)
        {
            var c = Summer[t];
            switch (s)
            {
                case Seasons.Spring:
                    if (t == WeedTone.Straw) c = Color.Lerp(c, Summer[WeedTone.Grass], 0.45f);
                    else if (t == WeedTone.Grass || t == WeedTone.Leaf || t == WeedTone.Stem) c = new Color(c.r * 1.05f, c.g * 1.18f, c.b * 0.9f);
                    else if (t == WeedTone.FlowerYellow) c = new Color(0.98f, 0.88f, 0.25f);   // dandelions, not goldenrod yet
                    break;
                case Seasons.Fall:
                    if (t == WeedTone.Grass) c = new Color(0.58f, 0.53f, 0.30f);
                    else if (t == WeedTone.DarkGrass) c = new Color(0.45f, 0.40f, 0.22f);
                    else if (t == WeedTone.Leaf) c = new Color(0.50f, 0.30f, 0.14f);      // dock and burdock go rusty
                    else if (t == WeedTone.Stem) c = new Color(0.50f, 0.40f, 0.24f);
                    else if (t == WeedTone.Straw) c = new Color(0.78f, 0.66f, 0.42f);
                    else if (t == WeedTone.Seed) c = new Color(0.45f, 0.30f, 0.18f);
                    else if (t == WeedTone.FlowerWhite || t == WeedTone.FlowerBlue) c = new Color(0.52f, 0.44f, 0.32f);   // gone to seed
                    break;
                case Seasons.Winter:
                    if (t == WeedTone.Grass || t == WeedTone.DarkGrass || t == WeedTone.Straw) c = t == WeedTone.DarkGrass ? new Color(0.52f, 0.45f, 0.32f) : new Color(0.68f, 0.60f, 0.44f);
                    else if (t == WeedTone.DryPatch) c = new Color(0.60f, 0.55f, 0.42f);
                    else c = new Color(0.40f, 0.32f, 0.24f);                                  // dead leaves, stalks, seed heads, dried flowers
                    if (snow >= 2 && t != WeedTone.DryPatch) c = Color.Lerp(c, new Color(0.9f, 0.91f, 0.93f), 0.12f * snow);   // frost / snow caught in it
                    break;
            }
            if (t == WeedTone.DryPatch && snow > 0) c = Color.Lerp(c, new Color(0.92f, 0.93f, 0.95f), 0.5f + 0.125f * snow);
            c.a = 1f;
            return c;
        }

        /// <summary>The shared material of a tone (created on first use, current season applied).</summary>
        public static Material Get(WeedTone t)
        {
            if (_tones.TryGetValue(t, out var m) && m != null) return m;
            m = new Material(Shader.Find("Standard")) { name = "GBW_" + t, hideFlags = HideFlags.DontUnloadUnusedAsset, enableInstancing = true };
            m.SetFloat("_Glossiness", t == WeedTone.Leaf ? 0.25f : 0.12f);
            m.color = Current(t);
            _tones[t] = m;
            return m;
        }

        /// <summary>A copy of a ground-like material (dirt / sand / gravel / stockpile / roof) that turns white under snow cover.
        /// top = only for top faces of props (less whitening).</summary>
        public static Material Snowy(Material src, bool top = false)
        {
            if (src == null) return null;
            if (_copies.TryGetValue(src, out var m) && m != null) return m;
            m = new Material(src) { name = "GBW_Snowy_" + src.name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            _copies[src] = m;
            _snowy.Add((m, src.color, top ? 1 : 0));
            ApplySnowy(m, src.color, top ? 1 : 0, SnowLevel());
            return m;
        }

        static void ApplySnowy(Material m, Color summer, int mode, int snow)
        {
            if (m == null) return;
            if (snow <= 0) { m.color = summer; return; }
            float k = mode == 0 ? 0.5f + 0.125f * snow : 0.3f + 0.1f * snow;
            var c = Color.Lerp(summer, new Color(0.93f, 0.94f, 0.96f, summer.a), k); c.a = summer.a;
            m.color = c;
        }

        static bool On => GameSettings.WeatherEnabled;
        static Seasons Season => On ? SeasonWeather.Season : Seasons.Summer;
        static int SnowLevel()
        {
            if (!On) return 0;
            float s = SeasonWeather.Ground.SnowIn;
            return s < 0.5f ? 0 : s < 1.5f ? 1 : s < 3f ? 2 : s < 6f ? 3 : 4;
        }
        static Color Current(WeedTone t) => Seasonal(t, Season, SnowLevel());

        void LateUpdate()
        {
            string key = On ? $"{SeasonWeather.Season}/{SnowLevel()}" : "off";
            if (key != _applied) { ApplyAll(); _applied = key; }
        }

        /// <summary>Re-applies the current season to every weed / snowy material now (tests).</summary>
        public static void ApplyAll()
        {
            var s = Season; int snow = SnowLevel();
            foreach (var kv in _tones) if (kv.Value != null) kv.Value.color = Seasonal(kv.Key, s, snow);
            foreach (var (m, summer, mode) in _snowy) ApplySnowy(m, summer, mode, snow);
            Key = On ? $"{s}/{snow}" : "off";
        }
    }
}
