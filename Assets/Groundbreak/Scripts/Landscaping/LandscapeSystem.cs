using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Runs landscaping over time (seed germination, sod knitting, path lights), dresses delivered bulk/plant piles,
    /// persists landscape jobs, and provides the Garden Center tablet app.</summary>
    public class LandscapeSystem : MonoBehaviour
    {
        public static LandscapeSystem I;

        [Serializable] public class SeedPatch { public Vector3 p; public float r; public int sown, watered; }
        static readonly List<SeedPatch> _patches = new List<SeedPatch>();
        public static IReadOnlyList<SeedPatch> Patches => _patches;
        public const int GerminateDays = 5, SodKnitDays = 3;

        static int Day => GameClock.I != null ? GameClock.I.Day : 1;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public static void AddSeedPatch(Vector3 p, float r) => _patches.Add(new SeedPatch { p = p, r = r, sown = Day, watered = -1 });

        /// <summary>Waters plants, sod and seed within radius. Returns how many things got water.</summary>
        public static int WaterAround(Vector3 p, float radius)
        {
            int n = 0;
            foreach (var f in new List<LandscapeFeature>(LandscapeFeature.All))
            {
                if (f.Kind != LandscapeKind.Plant && f.Kind != LandscapeKind.Sod) continue;
                if (new Vector2(f.A.x - p.x, f.A.z - p.z).magnitude > radius) continue;
                var c = f.Current;
                if (c.HasValue && c.Value.Tool == "hose") f.Work("hose", out _);
                else if (c.HasValue) continue; // not planted yet
                f.WateredDay = Day; n++;
            }
            foreach (var s in _patches)
                if (new Vector2(s.p.x - p.x, s.p.z - p.z).magnitude < radius + s.r) { s.watered = Day; n++; }
            return n;
        }

        float _next;
        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            int day = Day;
            bool rain = Weather.IsRaining;
            int need = GameSettings.Relaxed ? 1 : GerminateDays;

            for (int i = _patches.Count - 1; i >= 0; i--)
            {
                var s = _patches[i];
                if (rain) s.watered = day;
                if (day - s.sown >= need && s.watered >= day - 2 && Grade.Field != null)
                {
                    Grade.Field.Paint(s.p, s.r, LandCover.Lawn, c => c == LandCover.Seeded);
                    _patches.RemoveAt(i);
                }
            }
            int knit = GameSettings.Relaxed ? 1 : SodKnitDays;
            foreach (var f in new List<LandscapeFeature>(LandscapeFeature.All))
            {
                if (f.Kind == LandscapeKind.Sod && rain && f.Done) f.WateredDay = day;
                if (f.Kind == LandscapeKind.Sod && f.Done && day - f.PlacedDay >= knit && f.WateredDay >= day - 2) KnitSod(f);
            }

            bool dark = GameClock.I != null && (GameClock.I.Hours < 6.6f || GameClock.I.Hours > 19.2f);
            foreach (var f in LandscapeFeature.All) if (f.Kind == LandscapeKind.PathLight) f.SetLight(dark);

            DressPiles();
        }

        /// <summary>Rooted sod becomes part of the lawn surface.</summary>
        public static void KnitSod(LandscapeFeature f)
        {
            Grade.Ensure(f.A);
            Grade.Field?.PaintRect(f.A, new Vector2(Landscape.SodW / 2, Landscape.SodL / 2), f.Yaw, LandCover.Lawn);
            Destroy(f.gameObject);
        }

        // ------------------------------------------------------------------ pile dressing

        static readonly HashSet<MaterialPile> _dressed = new HashSet<MaterialPile>();

        void DressPiles()
        {
            foreach (var pile in FindObjectsByType<MaterialPile>(FindObjectsSortMode.None))
            {
                if (pile == null || !LandCatalog.IsLand(pile.DefinitionId) || _dressed.Contains(pile)) continue;
                _dressed.Add(pile);
                int count = Game.Stock.Count(pile.DefinitionId);
                var t = pile.transform;
                foreach (var r in pile.GetComponentsInChildren<Renderer>()) r.enabled = false;
                string id = pile.DefinitionId;
                var root = new GameObject("Dressing").transform; root.SetParent(t, false);
                Material bulk = null;
                switch (id)
                {
                    case "fill-dirt": bulk = Materials.Get(SurfaceKind.Dirt); break;
                    case "topsoil": bulk = LandMaterials.Get(LandSurface.Topsoil); break;
                    case "crusher-run": bulk = Materials.Get(SurfaceKind.Gravel, new Color(0.82f, 0.82f, 0.85f)); break;
                    case "gravel-57": bulk = Materials.Get(SurfaceKind.Gravel); break;
                    case "paver-sand": bulk = LandMaterials.Get(LandSurface.Sand); break;
                    case "mulch": bulk = LandMaterials.Get(LandSurface.Mulch); break;
                }
                if (bulk != null)
                {
                    var m = bulk;
                    LandModels.Instance($"mound_{id}_{Mathf.Min(count, 12)}", root, k => LandModels.Mound(k, m, Mathf.Min(count, 12) * Grade.CubicYard, id.GetHashCode()));
                    continue;
                }
                if (id.StartsWith("tree-") || id.StartsWith("shrub-") || id.StartsWith("perennial-"))
                {
                    var spec = PlantSpec.For(id);
                    for (int i = 0; i < Mathf.Min(count, 6); i++)
                    {
                        var holder = new GameObject("Plant").transform; holder.SetParent(root, false);
                        holder.localPosition = new Vector3((i % 3) * (spec.Tree ? 1.3f : 0.6f), 0, (i / 3) * (spec.Tree ? 1.3f : 0.6f));
                        if (spec.Tree) { LandModels.Instance("ball" + Mathf.RoundToInt(spec.BallR * 100), holder, kk => LandModels.RootBall(kk, spec.BallR)); }
                        else LandModels.Instance(spec.Short == "Hosta" ? "pot1" : "pot3", holder, kk => LandModels.Pot(kk, spec.Short == "Hosta" ? 0.09f : 0.14f, spec.Short == "Hosta" ? 0.16f : 0.24f));
                        var pm = LandFeatureVisuals.PlantModel(id, i, holder);
                        pm.transform.localPosition = new Vector3(0, spec.Tree ? spec.BallR * 1.4f : (spec.Short == "Hosta" ? 0.15f : 0.22f), 0);
                    }
                    continue;
                }
                if (id == "sod-roll")
                {
                    LandModels.Instance("pallet_sod", root, k =>
                    {
                        int wood = k.M(SurfaceKind.PineLumber), soil = k.M(LandSurface.Topsoil), g = k.M(LandSurface.Sod);
                        k.B.AddBox(new Vector3(0, 0.06f, 0), new Vector3(1.1f, 0.12f, 1.2f), Quaternion.identity, wood);
                        // rolled sod: cylinders stacked in 4 layers
                        for (int layer = 0; layer < 4; layer++)
                            for (int r = 0; r < 4; r++)
                                k.B.AddLathe(new Vector3(-0.42f + r * 0.28f, 0.25f + layer * 0.24f, 0), Quaternion.Euler(90, 0, 0),
                                    new[] { new Vector2(0.001f, -0.3f), new Vector2(0.12f, -0.3f), new Vector2(0.125f, 0.3f), new Vector2(0.001f, 0.3f) }, 10, layer == 3 ? g : (r % 2 == 0 ? g : soil));
                    });
                    continue;
                }
                if (id == "retaining-block" || id == "paver-holland")
                {
                    bool blocks = id == "retaining-block";
                    LandModels.Instance("pallet_" + id, root, k =>
                    {
                        int wood = k.M(SurfaceKind.PineLumber);
                        int m = blocks ? k.M(LandSurface.RetainingBlock) : k.M(LandSurface.Paver, new Color(0.8f, 0.74f, 0.66f));
                        k.B.AddBox(new Vector3(0, 0.06f, 0), new Vector3(1.1f, 0.12f, 1.1f), Quaternion.identity, wood);
                        k.B.AddBox(new Vector3(0, 0.12f + 0.3f, 0), new Vector3(1.05f, 0.6f, 1.05f), Quaternion.identity, m);
                    });
                    continue;
                }
                foreach (var r in pile.GetComponentsInChildren<Renderer>()) r.enabled = true; // bags/boxes look fine for the rest
            }
        }

        // ------------------------------------------------------------------ save

        [Serializable] class Data { public List<LandscapeFeature.Data> features = new List<LandscapeFeature.Data>(); public List<SeedPatch> seeds = new List<SeedPatch>(); }

        static string Save()
        {
            var d = new Data();
            foreach (var f in LandscapeFeature.All) if (f != null) d.features.Add(f.ToData());
            d.seeds.AddRange(_patches);
            return JsonUtility.ToJson(d);
        }

        static void Load(string json)
        {
            foreach (var f in new List<LandscapeFeature>(LandscapeFeature.All)) if (f != null) { f.gameObject.SetActive(false); Destroy(f.gameObject); }
            LandscapeFeature.All.Clear();
            _patches.Clear();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            foreach (var fd in d.features) LandscapeFeature.FromData(fd);
            _patches.AddRange(d.seeds);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _patches.Clear(); _dressed.Clear(); LandscapeFeature.All.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            // landscape must load after the grade field, which is registered by Grade (BeforeSceneLoad order isn't guaranteed),
            // so features re-sample heights lazily in their visuals.
            SaveSystem.RegisterExtension("landscape", Save, Load);
            SaveSystem.AfterLoad += () => { foreach (var f in LandscapeFeature.All) if (f != null) f.Rebuild(); };
            Tablet.RegisterApp("Garden Center", GardenCenter);
        }

        // ------------------------------------------------------------------ Garden Center app

        static readonly Dictionary<string, int> _cart = new Dictionary<string, int>();
        static int _group;
        static readonly string[] Groups = { "Bulk", "Hardscape", "Lawn", "Plants", "Fence", "Outdoor" };

        static void GardenCenter()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle;
            float fs = label.fontSize;
            _group = GUILayout.Toolbar(_group, Groups, button);
            GUILayout.Space(6);
            var items = new List<(string id, string name, int price, int units)>();
            foreach (var i in LandCatalog.Items) if (i.Group == Groups[_group]) items.Add((i.Id, i.Name, i.Price, i.Units));
            if (Groups[_group] == "Fence") { foreach (var id in new[] { "4x4x8", "pt2x4x8" }) { var d = Catalog.Get(id); if (d != null) items.Add((d.Id, d.Name, d.Price, 1)); } }
            foreach (var (id, name, price, units) in items)
            {
                if (!Career.IsUnlocked(id)) { GUILayout.Label($"{name}  - locked ({Career.UnlockHint(id)})", label); continue; }
                _cart.TryGetValue(id, out int q);
                GUILayout.BeginHorizontal();
                GUILayout.Label(name, label, GUILayout.Width(fs * 22));
                GUILayout.Label("$" + price, label, GUILayout.Width(fs * 4));
                float onSite = units > 1 ? Game.Stock.Count(id) : Bulk.OnHand(id);
                GUILayout.Label(units > 1 ? $"{Game.Stock.Count(id)} pcs" : $"{onSite:0.#}", label, GUILayout.Width(fs * 5));
                int step = GInput.Shift ? 10 : 1;
                if (GUILayout.Button("-", button, GUILayout.Width(fs * 2.2f))) q = Mathf.Max(0, q - step);
                GUILayout.Label(q.ToString(), label, GUILayout.Width(fs * 2.5f));
                if (GUILayout.Button("+", button, GUILayout.Width(fs * 2.2f))) q += step;
                GUILayout.EndHorizontal();
                if (q > 0) _cart[id] = q; else _cart.Remove(id);
            }
            int total = 0;
            foreach (var kv in _cart) { var d = Catalog.Get(kv.Key); if (d != null) total += d.Price * kv.Value; }
            GUILayout.Space(6);
            GUILayout.Label($"Cart: ${total:N0}   (bulk is sold by the cubic yard; a 6 cu ft wheelbarrow load is 0.22 yd)", header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Order delivery (~90 s)", button)) Checkout(total, false);
            if (GUILayout.Button($"Rush (+${DeliveryService.RushFee})", button)) Checkout(total, true);
            if (GUILayout.Button("Clear", button)) _cart.Clear();
            GUILayout.EndHorizontal();
        }

        static void Checkout(int total, bool rush)
        {
            if (total <= 0 || DeliveryService.I == null) return;
            int cost = total + (rush ? DeliveryService.RushFee : 0);
            if (!Game.Wallet.TrySpend(cost, "Garden Center order")) { Tablet.I?.ShowMessage($"Need ${cost:N0}"); return; }
            DeliveryService.I.Order(new Dictionary<string, int>(_cart), rush);
            _cart.Clear();
            Tablet.I?.ShowMessage("Garden Center order placed.");
        }
    }
}
