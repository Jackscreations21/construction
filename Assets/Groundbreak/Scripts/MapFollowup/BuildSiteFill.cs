using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>
    /// Fills the open middle of the player's build site (<see cref="Town.BuildSiteRect"/>, the field across Main St from home) so it reads as a
    /// real lot waiting for work instead of a bare lawn: rough unmown grass with clover, dandelions and weed patches; knee-high meadow along the
    /// sides and the back; a contractor's staging area off the construction entrance (job-site office trailer 8'x32' on blocks, two porta-johns,
    /// a 30-yard roll-off, temporary power pole, sand / stone / topsoil stockpiles, block on pallets, lumber and OSB bunks on dunnage, rebar)
    /// on gravel laydown pads. The pad itself (and a margin around it) stays clear.
    /// <para>Everything is split into 12 m chunks. A chunk disappears as soon as anything is built or placed in it (any collider that is not the
    /// ground, a vehicle or a person) or its ground is dug, graded or covered (<see cref="SiteGround"/> / <see cref="Grade"/>), and comes back
    /// when it is clear again — so the whole lot stays buildable. Only the trailer, roll-off and porta-johns have colliders.</para>
    /// </summary>
    public class BuildSiteFill : MonoBehaviour
    {
        public static BuildSiteFill I { get; private set; }
        public const float ChunkSize = 12f;
        /// <summary>Filled part of the build site (the margins beyond it are VacantLots' tree / tall-grass bands).</summary>
        public static Rect Area => Rect.MinMaxRect(-41f, Town.BuildSiteRect.yMin + 3f, 41f, 86f);
        /// <summary>Kept clear: the 60'x60' pad and a margin (Free Build starts here; deliveries drop at (6, -2)).</summary>
        public static readonly Rect PadKeepout = Rect.MinMaxRect(-16f, -8f, 16f, 26f);

        public int Chunks => _chunks.Count;
        public int Hidden => _hidden.Count;
        public int Vertices { get; private set; }
        public int Rebuilds { get; private set; }
        /// <summary>Why the last blocked chunk is blocked (diagnostics).</summary>
        public static string LastBlock = "";

        class Chunk { public int Id; public Rect R; public readonly List<Item> Items = new List<Item>(); }
        class Item
        {
            public Vector3 Pos; public float Yaw; public System.Action<LandModel, MapBuild> Draw;
            public Vector3 ColCenter, ColSize; public GameObject Col; public int ChunkId;
        }

        readonly List<Chunk> _chunks = new List<Chunk>();
        readonly List<Item> _items = new List<Item>();
        HashSet<int> _hidden = new HashSet<int>();
        GameObject _weeds, _props;
        float _next;
        static readonly Collider[] _buf = new Collider[64];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { I = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            Town.Built -= Build; Town.Built += Build;
            Town.Destroyed -= Clear; Town.Destroyed += Clear;
        }

        static void Build()
        {
            Clear();
            if (Town.Root == null || Town.BuildSite == null) return;
            var go = new GameObject("BuildSiteFill");   // not under Town.Root: the town itself builds nothing on the player's site
            I = go.AddComponent<BuildSiteFill>();
            try { I.Init(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        static void Clear()
        {
            if (I == null) return;
            I.DropMeshes();
            Destroy(I.gameObject);
            I = null;
        }

        void DropMeshes()
        {
            foreach (var g in new[] { _weeds, _props })
                if (g != null) { var mf = g.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh); Destroy(g); }
            _weeds = _props = null;
        }

        void Init()
        {
            var a = Area;
            int id = 0;
            for (float z = a.yMin; z < a.yMax - 0.01f; z += ChunkSize)
                for (float x = a.xMin; x < a.xMax - 0.01f; x += ChunkSize)
                    _chunks.Add(new Chunk { Id = id++, R = Rect.MinMaxRect(x, z, Mathf.Min(a.xMax, x + ChunkSize), Mathf.Min(a.yMax, z + ChunkSize)) });
            Staging();
            foreach (var it in _items)
            {
                var ch = ChunkAt(it.Pos);
                if (ch == null) continue;
                it.ChunkId = ch.Id; ch.Items.Add(it);
                if (it.ColSize != Vector3.zero)
                {
                    it.Col = new GameObject("Collider");
                    it.Col.transform.SetParent(transform, false);
                    it.Col.transform.SetPositionAndRotation(it.Pos, Quaternion.Euler(0, it.Yaw, 0));
                    var bc = it.Col.AddComponent<BoxCollider>(); bc.center = it.ColCenter; bc.size = it.ColSize;
                }
            }
            _hidden = Blocked();
            if (_hidden.Count > 0) Debug.Log("[BuildSiteFill] blocked at start: " + LastBlock);
            Rebuild();
        }

        Chunk ChunkAt(Vector3 p)
        {
            foreach (var c in _chunks) if (c.R.Contains(new Vector2(p.x, p.z))) return c;
            return null;
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            CheckNow();
        }

        /// <summary>Re-checks every chunk now (tests); rebuilds the meshes when the set of blocked chunks changed.</summary>
        public void CheckNow()
        {
            var h = Blocked();
            if (h.SetEquals(_hidden)) return;
            _hidden = h;
            Rebuild();
        }

        public bool IsHidden(Vector3 p) { var c = ChunkAt(p); return c != null && _hidden.Contains(c.Id); }
        /// <summary>True where the fill puts weeds (inside the filled area, not the pad / entrance keep-outs).</summary>
        public bool IsFilled(Vector3 p) => ChunkAt(p) != null && Tall(p.x, p.z) > 0f;

        // ------------------------------------------------------------------ what blocks a chunk

        HashSet<int> Blocked()
        {
            var set = new HashSet<int>();
            string why = "";
            foreach (var c in _chunks) if (ChunkBlocked(c)) { set.Add(c.Id); why += LastBlock + "; "; }
            LastBlock = why;
            return set;
        }

        bool ChunkBlocked(Chunk c)
        {
            var r = c.R;
            var centre = new Vector3(r.center.x, 1.6f, r.center.y);
            int n = Physics.OverlapBoxNonAlloc(centre, new Vector3(r.width / 2f, 1.5f, r.height / 2f), _buf, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!Ignore(_buf[i])) { LastBlock = $"chunk {c.Id} at {r.center}: {_buf[i].name} ({_buf[i].bounds.center}, {_buf[i].bounds.size})"; return true; }
            // dug, filled, graded or stripped ground
            for (int ix = 0; ix <= 3; ix++)
                for (int iz = 0; iz <= 3; iz++)
                {
                    float x = Mathf.Lerp(r.xMin + 0.5f, r.xMax - 0.5f, ix / 3f), z = Mathf.Lerp(r.yMin + 0.5f, r.yMax - 0.5f, iz / 3f);
                    var p = new Vector3(x, 0, z);
                    if (Mathf.Abs(SiteGround.GroundY(p)) > 0.05f || Mathf.Abs(Grade.HeightAt(x, z)) > 0.05f) { LastBlock = $"chunk {c.Id}: ground height at {p}"; return true; }
                    if (SiteGround.I != null && SiteGround.CellOf(p, out int cx, out int cz) && SiteGround.I.Cover[SiteGround.CI(cx, cz)] != GroundCover.Grass) { LastBlock = $"chunk {c.Id}: ground cover at {p}"; return true; }
                }
            return false;
        }

        bool Ignore(Collider c)
        {
            if (c == null || c.isTrigger) return true;
            var t = c.transform;
            if (t.IsChildOf(transform)) return true;
            if (VacantLots.Root != null && t.IsChildOf(VacantLots.Root.transform)) return true;
            if (Town.Root != null && t.IsChildOf(Town.Root)) return true;
            if (MapWorld.Root != null && t.IsChildOf(MapWorld.Root.transform)) return true;
            if (SiteGround.I != null && t.IsChildOf(SiteGround.I.transform)) return true;
            // lot terrain: its shape is checked by height (a graded lot clears the dressing, a flat one does not)
            if ((Grade.Field != null && t.IsChildOf(Grade.Field.transform)) || t.name.StartsWith("GradeChunk") || t.name == "GradeField" || t.name == "Ground" || t.name == "FarGround") return true;
            var rb = c.attachedRigidbody; if (rb != null && !rb.isKinematic) return true;   // vans, trucks, loose things moving through
            if (c is CharacterController || c.GetComponentInParent<CharacterController>() != null) return true;   // the player
            var b = c.bounds;
            if (b.size.x > 30f || b.size.z > 30f || b.max.y < 0.12f) return true;          // ground planes, terrain, flat skirts
            return false;
        }

        // ------------------------------------------------------------------ meshes

        void Rebuild()
        {
            Rebuilds++;
            DropMeshes();
            var weeds = new LandModel(); var props = new LandModel();
            var mb = new MapBuild { K = props, Rng = new System.Random(1), Frame = Matrix4x4.identity };
            int spots = 0;
            foreach (var c in _chunks)
            {
                bool hide = _hidden.Contains(c.Id);
                foreach (var it in c.Items) if (it.Col != null) it.Col.SetActive(!hide);
                if (hide) continue;
                spots += Grow(weeds, c);
                foreach (var it in c.Items)
                {
                    var f = Matrix4x4.TRS(it.Pos, Quaternion.Euler(0, it.Yaw, 0), Vector3.one);
                    props.B.Transform = f; mb.Frame = f; mb.Rng = new System.Random(c.Id * 31 + 7);
                    it.Draw(props, mb);
                }
                props.B.ResetTransform();
            }
            Vertices = 0;
            if (weeds.B.VertexCount > 0) { _weeds = weeds.Make(transform, "Weeds"); _weeds.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off; Vertices += weeds.B.VertexCount; }
            if (props.B.VertexCount > 0) { _props = props.Make(transform, "Staging"); Vertices += props.B.VertexCount; }
            Spots = spots;
        }

        public int Spots { get; private set; }

        static float Hash(int x, int z, int s) { unchecked { uint h = (uint)(x * 374761393 + z * 668265263 + s * 1442695041); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777215f; } }
        static float Noise(float x, float z, int s)
        {
            int x0 = Mathf.FloorToInt(x), z0 = Mathf.FloorToInt(z); float tx = x - x0, tz = z - z0;
            tx = tx * tx * (3 - 2 * tx); tz = tz * tz * (3 - 2 * tz);
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, z0, s), Hash(x0 + 1, z0, s), tx), Mathf.Lerp(Hash(x0, z0 + 1, s), Hash(x0 + 1, z0 + 1, s), tx), tz);
        }

        /// <summary>The knee-high meadow strips (same as <see cref="Tall"/> ≥ 0.8).</summary>
        static readonly Rect[] MeadowRects = { Rect.MinMaxRect(-41f, -10f, -26f, 86f), Rect.MinMaxRect(26f, -10f, 41f, 86f), Rect.MinMaxRect(-26f, 72f, 26f, 86f) };

        /// <summary>0 = keep clear, 0.2..0.45 = rough lawn, 0.8+ = knee-high meadow.</summary>
        static float Tall(float x, float z)
        {
            if (PadKeepout.Contains(new Vector2(x, z))) return 0f;
            var site = Town.BuildSiteRect;
            if (Mathf.Abs(x) < 5.5f && z < site.yMin + 17f) return 0f;          // gravel construction entrance
            if (z < site.yMin + 2.2f) return 0f;                                 // silt fence line
            if (Mathf.Abs(x) >= 26f && z > -10f) return 0.85f;                   // unmown sides
            if (z >= 72f) return 0.9f;                                           // unmown back
            float n = Noise(x / 14f, z / 14f, 5);
            return n > 0.66f ? 0.7f : 0.3f;                                      // rough lawn with weedy patches
        }

        int Grow(LandModel k, Chunk c)
        {
            var rng = new System.Random(c.Id * 7919 + 3);
            var r = c.R;
            int spots = 0;
            const float step = 1.7f;
            for (float x = r.xMin + step * 0.5f; x < r.xMax; x += step)
                for (float z = r.yMin + step * 0.5f; z < r.yMax; z += step)
                {
                    float px = x + ((float)rng.NextDouble() - 0.5f) * step, pz = z + ((float)rng.NextDouble() - 0.5f) * step;
                    float t = Tall(px, pz);
                    if (t <= 0f) continue;
                    // meadow: ~1 spot per 3 m², rough lawn ~1 per 9 m², weedy lawn patches in between
                    float keep = t >= 0.8f ? 0.95f : t >= 0.6f ? 0.6f : 0.32f;
                    if (rng.NextDouble() > keep) continue;
                    if (StagingFootprint(px, pz)) continue;
                    var p = new Vector3(px, 0, pz);
                    if (t < 0.5f)
                    {
                        double q = rng.NextDouble();
                        if (q < 0.45) Weeds.Short(k, p, rng);
                        else if (q < 0.7) Weeds.Rosette(k, p, rng, false);
                        else if (q < 0.85) Weeds.Wildflowers(k, p, rng, 0.14f + (float)rng.NextDouble() * 0.1f);   // clover / dandelion height
                        else Weeds.Tuft(k, p, rng, 0.3f + (float)rng.NextDouble() * 0.2f, WeedTone.Grass, false);
                    }
                    else
                    {
                        Weeds.Spot(k, p, rng, t);
                        if (t >= 0.8f)
                        {   // the unmown strips are thick: a second plant beside it
                            var p2 = p + new Vector3(((float)rng.NextDouble() - 0.5f) * 1.4f, 0, ((float)rng.NextDouble() - 0.5f) * 1.4f);
                            if (!StagingFootprint(p2.x, p2.z)) { Weeds.Tuft(k, p2, rng, 0.45f + (float)rng.NextDouble() * 0.35f, rng.NextDouble() < 0.5 ? WeedTone.Straw : WeedTone.Grass, rng.NextDouble() < 0.35); spots++; }
                        }
                    }
                    spots++;
                }
            // unmown ground under the meadow strips (sides and back)
            foreach (var m in MeadowRects)
            {
                var q = Rect.MinMaxRect(Mathf.Max(r.xMin, m.xMin), Mathf.Max(r.yMin, m.yMin), Mathf.Min(r.xMax, m.xMax), Mathf.Min(r.yMax, m.yMax));
                if (q.width < 0.2f || q.height < 0.2f) continue;
                const float y = 0.015f;
                k.B.AddQuad(new Vector3(q.xMin, y, q.yMin), new Vector3(q.xMin, y, q.yMax), new Vector3(q.xMax, y, q.yMax), new Vector3(q.xMax, y, q.yMin), k.M(MapGapFill.RoughMeadow), Vector3.up);
            }
            // dry / worn patches in the rough lawn (not under the meadow)
            float tc = Tall(r.center.x, r.center.y);
            if (tc > 0f && tc < 0.8f && rng.NextDouble() < 0.6)
            {
                var pc = new Vector3(Mathf.Lerp(r.xMin + 2f, r.xMax - 2f, (float)rng.NextDouble()), 0, Mathf.Lerp(r.yMin + 2f, r.yMax - 2f, (float)rng.NextDouble()));
                if (Tall(pc.x, pc.z) > 0f && !StagingFootprint(pc.x, pc.z)) Weeds.Rosette(k, pc, rng, true);   // a dock plant where the lawn is worn
            }
            // a few volunteer shrubs in the meadow
            if (Tall(r.center.x, r.center.y) >= 0.8f && rng.NextDouble() < 0.45)
            {
                var mb = new MapBuild { K = k, Rng = rng, Frame = Matrix4x4.identity };
                var bp = new Vector3(Mathf.Lerp(r.xMin + 1.5f, r.xMax - 1.5f, (float)rng.NextDouble()), 0, Mathf.Lerp(r.yMin + 1.5f, r.yMax - 1.5f, (float)rng.NextDouble()));
                if (Tall(bp.x, bp.z) >= 0.8f) MapTrees.Bush(mb, bp, 0.7f + (float)rng.NextDouble() * 0.7f, rng.Next());
                k.B.ResetTransform();
            }
            return spots;
        }

        static void Patch(LandModel k, Vector3 c, float r, System.Random rng)
        {
            var pts = new List<Vector3>();
            const int n = 11;
            for (int i = 0; i < n; i++)
            {
                float a = -i * Mathf.PI * 2f / n, rr = r * (0.75f + (float)rng.NextDouble() * 0.25f);
                pts.Add(c + new Vector3(Mathf.Cos(a) * rr * 1.3f, 0.012f, Mathf.Sin(a) * rr));
            }
            k.B.AddPolygon(pts, Weeds.M(k, WeedTone.DryPatch), Vector3.up, Vector3.right);
        }

        // ------------------------------------------------------------------ staging area (real sizes)

        static readonly List<Rect> _footprints = new List<Rect>();
        static bool StagingFootprint(float x, float z) { foreach (var f in _footprints) if (f.Contains(new Vector2(x, z))) return true; return false; }

        void Add(Vector3 pos, float yaw, Vector2 clearRect, System.Action<LandModel, MapBuild> draw, Vector3 colCenter = default, Vector3 colSize = default)
        {
            _items.Add(new Item { Pos = pos, Yaw = yaw, Draw = draw, ColCenter = colCenter, ColSize = colSize });
            float hx = clearRect.x / 2f, hz = clearRect.y / 2f;
            bool rot = Mathf.Abs(Mathf.DeltaAngle(yaw, 90f)) < 45f || Mathf.Abs(Mathf.DeltaAngle(yaw, 270f)) < 45f;
            if (rot) { float t = hx; hx = hz; hz = t; }
            _footprints.Add(Rect.MinMaxRect(pos.x - hx, pos.z - hz, pos.x + hx, pos.z + hz));
        }

        void Staging()
        {
            _footprints.Clear();
            float z0 = Town.BuildSiteRect.yMin;   // -52.5: lot line on Main St
            // east: gravel laydown with the field office, toilets, roll-off and temp power
            Add(new Vector3(29f, 0, z0 + 17f), 0f, new Vector2(20f, 17f), (k, b) => Gravel(k, 20f, 17f));
            Add(new Vector3(30f, 0, z0 + 22.5f), 0f, new Vector2(12f, 6f), Trailer, new Vector3(0, 1.75f, 0), new Vector3(9.75f, 2.6f, 2.44f));
            Add(new Vector3(21.5f, 0, z0 + 11.5f), 0f, new Vector2(1.4f, 1.4f), (k, b) => PortaJohn(k, new Color(0.10f, 0.32f, 0.62f)), new Vector3(0, 1.15f, 0), new Vector3(1.12f, 2.3f, 1.12f));
            Add(new Vector3(23f, 0, z0 + 11.5f), 0f, new Vector2(1.4f, 1.4f), (k, b) => PortaJohn(k, new Color(0.12f, 0.45f, 0.20f)), new Vector3(0, 1.15f, 0), new Vector3(1.12f, 2.3f, 1.12f));
            Add(new Vector3(33.5f, 0, z0 + 11f), 90f, new Vector2(7.2f, 2.8f), RollOff, new Vector3(0, 0.95f, 0), new Vector3(2.44f, 1.9f, 6.7f));
            Add(new Vector3(19.5f, 0, z0 + 19.5f), 0f, new Vector2(1f, 1f), PowerPole);
            // west: stockpiles, block on pallets, lumber and OSB on dunnage, rebar
            Add(new Vector3(-29f, 0, z0 + 16f), 0f, new Vector2(24f, 18f), (k, b) => Gravel(k, 24f, 18f));
            Add(new Vector3(-36f, 0, z0 + 12.5f), 20f, new Vector2(6f, 6f), (k, b) => Stockpile(k, WeedLook.Snowy(LandMaterials.Get(LandSurface.Sand)), 2.4f, 1.15f));
            Add(new Vector3(-30f, 0, z0 + 11.5f), 70f, new Vector2(6f, 6f), (k, b) => Stockpile(k, WeedLook.Snowy(Materials.Get(SurfaceKind.Gravel)), 2.6f, 1.25f));
            Add(new Vector3(-23f, 0, z0 + 12.5f), 140f, new Vector2(8f, 8f), (k, b) => Stockpile(k, WeedLook.Snowy(LandMaterials.Get(LandSurface.Topsoil)), 3.2f, 1.5f));
            Add(new Vector3(-35f, 0, z0 + 20.5f), 0f, new Vector2(4.4f, 1.6f), (k, b) => BlockPallets(k, 3));
            Add(new Vector3(-26.5f, 0, z0 + 20.5f), 0f, new Vector2(5.6f, 1.6f), (k, b) => LumberBunk(k, 4.88f, 0.14f, 0.62f, SurfaceKind.PineLumber));
            Add(new Vector3(-26.5f, 0, z0 + 23f), 0f, new Vector2(3.2f, 1.6f), (k, b) => LumberBunk(k, 2.44f, 1.22f, 0.45f, SurfaceKind.Osb));
            Add(new Vector3(-20f, 0, z0 + 22.5f), 90f, new Vector2(6.4f, 1f), Rebar);
        }

        static Material Top(SurfaceKind s) => WeedLook.Snowy(Materials.Get(s), true);

        static void Gravel(LandModel k, float w, float d)
        {
            // stabilized gravel laydown (#57 stone over fabric), 2" proud of grade at the edges
            k.B.AddBox(new Vector3(0, 0.03f, 0), new Vector3(w, 0.06f, d), Quaternion.identity, k.M(SurfaceKind.Gravel), Vector3.forward);
        }

        /// <summary>8'x32' mobile field office on CMU piers: white siding, dark windows, steel door with PT steps, tongue and jack at one end.</summary>
        static void Trailer(LandModel k, MapBuild b)
        {
            var B = k.B;
            const float L = 9.75f, W = 2.44f, y0 = 0.55f, H = 2.6f;
            int body = k.M(SurfaceKind.PaintedWhite, new Color(0.93f, 0.92f, 0.88f)), roof = k.M(Top(SurfaceKind.Galvanized)), cmu = k.M(SurfaceKind.ConcreteBlock),
                glass = k.M(new Color(0.12f, 0.14f, 0.16f)), steel = k.M(SurfaceKind.PaintedSteel, new Color(0.55f, 0.58f, 0.6f)), pt = k.M(SurfaceKind.TreatedLumber),
                dark = k.M(SurfaceKind.DarkPlastic), stripe = k.M(new Color(0.12f, 0.28f, 0.55f));
            B.AddBox(new Vector3(0, y0 + H / 2f, 0), new Vector3(L, H, W), Quaternion.identity, body, Vector3.right);
            B.AddBox(new Vector3(0, y0 + H + 0.04f, 0), new Vector3(L + 0.08f, 0.08f, W + 0.08f), Quaternion.identity, roof, Vector3.right);
            B.AddBox(new Vector3(0, y0 + 0.35f, -W / 2f - 0.006f), new Vector3(L, 0.12f, 0.01f), Quaternion.identity, stripe);
            B.AddBox(new Vector3(0, y0 + 0.35f, W / 2f + 0.006f), new Vector3(L, 0.12f, 0.01f), Quaternion.identity, stripe);
            // steel chassis + CMU piers (8x8x16 blocks, two high) every 8'
            B.AddBox(new Vector3(0, y0 - 0.12f, 0), new Vector3(L - 0.4f, 0.24f, W - 0.5f), Quaternion.identity, dark);
            for (float x = -L / 2f + 0.6f; x <= L / 2f - 0.5f; x += 2.44f)
                foreach (float z in new[] { -0.85f, 0.85f })
                    B.AddBox(new Vector3(x, 0.2f, z), new Vector3(0.4f, 0.4f, 0.2f), Quaternion.identity, cmu, Vector3.right);
            // windows (front -Z faces the street) and door
            foreach (float x in new[] { -3.6f, -1.6f, 2.6f })
                B.AddBox(new Vector3(x, y0 + 1.45f, -W / 2f - 0.02f), new Vector3(1.1f, 0.9f, 0.04f), Quaternion.identity, glass);
            B.AddBox(new Vector3(0.6f, y0 + 1.0f, -W / 2f - 0.025f), new Vector3(0.91f, 2.0f, 0.05f), Quaternion.identity, steel);
            B.AddBox(new Vector3(0.9f, y0 + 1.0f, -W / 2f - 0.06f), new Vector3(0.05f, 0.05f, 0.05f), Quaternion.identity, dark);
            foreach (float x in new[] { -4.2f, 4.2f })
                B.AddBox(new Vector3(x, y0 + H - 0.5f, W / 2f + 0.02f), new Vector3(0.6f, 0.4f, 0.04f), Quaternion.identity, glass);
            // PT wood steps + landing
            B.AddBox(new Vector3(0.6f, y0 - 0.02f, -W / 2f - 0.6f), new Vector3(1.5f, 0.05f, 1.2f), Quaternion.identity, pt, Vector3.right);
            for (int i = 0; i < 3; i++)
                B.AddBox(new Vector3(0.6f, (i + 0.5f) * (y0 / 3f), -W / 2f - 1.2f - (2 - i) * 0.26f), new Vector3(1.2f, 0.04f, 0.26f), Quaternion.identity, pt, Vector3.right);
            foreach (float x in new[] { -0.15f, 1.35f })
                B.AddBox(new Vector3(x, y0 / 2f, -W / 2f - 0.6f), new Vector3(0.09f, y0, 0.09f), Quaternion.identity, pt, Vector3.up);
            // hitch tongue (A-frame) and tongue jack at the +X end
            foreach (float s in new[] { -1f, 1f })
                B.AddBox(new Vector3(L / 2f + 0.65f, y0 - 0.15f, s * 0.4f), new Vector3(1.5f, 0.1f, 0.08f), Quaternion.Euler(0, s * 28f, 0), dark);
            B.AddCylinder(new Vector3(L / 2f + 1.25f, 0f, 0), 0.04f, y0, 8, steel);
            B.AddBox(new Vector3(L / 2f + 1.25f, 0.02f, 0), new Vector3(0.3f, 0.04f, 0.3f), Quaternion.identity, pt);
            // AC unit on the end wall
            B.AddBox(new Vector3(-L / 2f - 0.25f, y0 + 1.9f, 0), new Vector3(0.5f, 0.6f, 0.75f), Quaternion.identity, k.M(SurfaceKind.Galvanized));
        }

        static void PortaJohn(LandModel k, Color shell)
        {
            var B = k.B;
            int body = k.M(SurfaceKind.DarkPlastic, shell), top = k.M(Top(SurfaceKind.PaintedWhite)), base0 = k.M(SurfaceKind.DarkPlastic), white = k.M(new Color(0.9f, 0.9f, 0.88f));
            B.AddBox(new Vector3(0, 0.06f, 0), new Vector3(1.12f, 0.12f, 1.12f), Quaternion.identity, base0);
            B.AddBox(new Vector3(0, 1.15f, 0), new Vector3(1.08f, 2.06f, 1.08f), Quaternion.identity, body, Vector3.right);
            B.AddBox(new Vector3(0, 2.24f, 0), new Vector3(1.14f, 0.12f, 1.14f), Quaternion.identity, top, Vector3.right);
            B.AddBox(new Vector3(0, 1.1f, -0.555f), new Vector3(0.8f, 1.85f, 0.03f), Quaternion.identity, body, Vector3.right);
            B.AddBox(new Vector3(0.25f, 1.3f, -0.575f), new Vector3(0.12f, 0.05f, 0.02f), Quaternion.identity, white);   // vacant/occupied latch
            B.AddCylinder(new Vector3(0.38f, 2.3f, 0.38f), 0.05f, 0.35f, 8, base0);                                   // vent stack
        }

        /// <summary>30-yard roll-off: 8' wide, 22' long, ~6' tall, open top, rails, a few loads of framing scrap and drywall in it.</summary>
        static void RollOff(LandModel k, MapBuild b)
        {
            var B = k.B;
            const float W = 2.44f, L = 6.7f, H = 1.83f, t = 0.08f;
            int steel = k.M(SurfaceKind.PaintedSteel, new Color(0.15f, 0.30f, 0.20f)), rail = k.M(SurfaceKind.DarkPlastic), scrap = k.M(SurfaceKind.PineLumber),
                gyp = k.M(new Color(0.88f, 0.88f, 0.85f)), osb = k.M(SurfaceKind.Osb);
            float y0 = 0.15f;
            foreach (float s in new[] { -1f, 1f })
            {
                B.AddBox(new Vector3(s * (W / 2f - t / 2f), y0 + H / 2f, 0), new Vector3(t, H, L), Quaternion.identity, steel, Vector3.forward);
                B.AddBox(new Vector3(0, y0 + H / 2f, s * (L / 2f - t / 2f)), new Vector3(W, H, t), Quaternion.identity, steel, Vector3.right);
                B.AddBox(new Vector3(s * 0.7f, 0.075f, 0), new Vector3(0.15f, 0.15f, L + 0.3f), Quaternion.identity, rail);
                for (float z = -L / 2f + 0.6f; z < L / 2f; z += 0.9f) B.AddBox(new Vector3(s * (W / 2f + 0.04f), y0 + H / 2f, z), new Vector3(0.06f, H, 0.1f), Quaternion.identity, steel);
            }
            B.AddBox(new Vector3(0, y0 + 0.05f, 0), new Vector3(W - 2 * t, 0.1f, L - 2 * t), Quaternion.identity, steel);
            // debris inside, the top ~0.5 m below the rim
            var rng = new System.Random(5);
            B.AddBox(new Vector3(0, y0 + 0.6f, 0), new Vector3(W - 0.2f, 1.0f, L - 0.2f), Quaternion.identity, k.M(SurfaceKind.Dirt));
            for (int i = 0; i < 26; i++)
            {
                var p = new Vector3(((float)rng.NextDouble() - 0.5f) * (W - 0.5f), y0 + 1.15f + (float)rng.NextDouble() * 0.35f, ((float)rng.NextDouble() - 0.5f) * (L - 0.6f));
                var rot = Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 50f, (float)rng.NextDouble() * 180f, ((float)rng.NextDouble() - 0.5f) * 40f);
                if (i % 3 == 0) B.AddBox(p, new Vector3(0.6f + (float)rng.NextDouble() * 0.6f, 0.013f, 0.5f + (float)rng.NextDouble() * 0.5f), rot, i % 2 == 0 ? gyp : osb);
                else B.AddBox(p, new Vector3(0.038f, 0.14f, 0.6f + (float)rng.NextDouble() * 1.6f), rot, scrap, Vector3.forward);
            }
        }

        /// <summary>Temporary power: 4x4 PT post, meter base, panel with GFCI receptacles.</summary>
        static void PowerPole(LandModel k, MapBuild b)
        {
            var B = k.B;
            int pt = k.M(SurfaceKind.TreatedLumber), gray = k.M(SurfaceKind.Galvanized), dark = k.M(SurfaceKind.DarkPlastic);
            B.AddBox(new Vector3(0, 1.5f, 0), new Vector3(0.089f, 3.0f, 0.089f), Quaternion.identity, pt, Vector3.up);
            B.AddBox(new Vector3(0, 1.25f, 0.12f), new Vector3(0.6f, 0.05f, 0.3f), Quaternion.identity, pt, Vector3.right);
            B.AddBox(new Vector3(0, 1.75f, -0.08f), new Vector3(0.3f, 0.4f, 0.1f), Quaternion.identity, gray);
            B.AddCylinder(new Vector3(0, 1.75f, -0.16f), Quaternion.Euler(90, 0, 0), 0.08f, 0.08f, 10, k.M(new Color(0.75f, 0.8f, 0.82f)));
            B.AddBox(new Vector3(0, 1.25f, -0.08f), new Vector3(0.35f, 0.45f, 0.12f), Quaternion.identity, gray);
            B.AddBox(new Vector3(0, 2.6f, -0.05f), new Vector3(0.03f, 0.8f, 0.03f), Quaternion.identity, dark);   // service mast
        }

        static void Stockpile(LandModel k, Material m, float r, float h)
        {
            var rng = new System.Random((int)(r * 1000));
            var prof = new[] { new Vector2(r, 0f), new Vector2(r * 0.82f, h * 0.28f), new Vector2(r * 0.52f, h * 0.72f), new Vector2(r * 0.2f, h * 0.96f), new Vector2(0f, h) };
            k.B.AddLathe(Vector3.zero, Quaternion.identity, prof, 16, k.M(m), 60f);
            // a second, smaller lobe where the truck tipped the last load
            var prof2 = new[] { new Vector2(r * 0.7f, 0f), new Vector2(r * 0.5f, h * 0.45f), new Vector2(0f, h * 0.7f) };
            k.B.AddLathe(new Vector3(r * 0.75f, 0, r * 0.2f), Quaternion.identity, prof2, 12, k.M(m), 60f);
            for (int i = 0; i < 3; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                Weeds.Tuft(k, new Vector3(Mathf.Cos(a) * r * 1.05f, 0, Mathf.Sin(a) * r * 1.05f), rng, 0.35f, WeedTone.Grass, false);
            }
        }

        /// <summary>Cubes of 8x8x16 CMU on 40"x48" pallets (90 block a cube, about 3' tall).</summary>
        static void BlockPallets(LandModel k, int n)
        {
            int pal = k.M(SurfaceKind.PineLumber), cmu = k.M(SurfaceKind.ConcreteBlock), cap = k.M(Top(SurfaceKind.ConcreteBlock)), wrap = k.M(new Color(0.85f, 0.87f, 0.9f));
            for (int i = 0; i < n; i++)
            {
                var c = new Vector3((i - (n - 1) / 2f) * 1.45f, 0, 0);
                k.B.AddBox(c + new Vector3(0, 0.07f, 0), new Vector3(1.02f, 0.14f, 1.22f), Quaternion.identity, pal, Vector3.right);
                k.B.AddBox(c + new Vector3(0, 0.14f + 0.41f, 0), new Vector3(0.98f, 0.82f, 1.2f), Quaternion.identity, cmu, Vector3.right);
                k.B.AddBox(c + new Vector3(0, 0.14f + 0.825f, 0), new Vector3(0.98f, 0.01f, 1.2f), Quaternion.identity, cap);
                k.B.AddBox(c + new Vector3(0, 0.6f, 0), new Vector3(1.0f, 0.06f, 1.22f), Quaternion.identity, wrap);   // shrink-wrap band
            }
        }

        /// <summary>A unit of lumber or sheet goods on three 4x4 dunnage sticks, two steel bands.</summary>
        static void LumberBunk(LandModel k, float len, float wid, float h, SurfaceKind s)
        {
            int dun = k.M(SurfaceKind.TreatedLumber), wood = k.M(s), cap = k.M(Top(s)), band = k.M(SurfaceKind.DarkPlastic);
            float w = Mathf.Max(1.1f, wid);
            foreach (float x in new[] { -len * 0.4f, 0f, len * 0.4f }) k.B.AddBox(new Vector3(x, 0.045f, 0), new Vector3(0.089f, 0.089f, w + 0.2f), Quaternion.identity, dun, Vector3.forward);
            k.B.AddBox(new Vector3(0, 0.09f + h / 2f, 0), new Vector3(len, h, w), Quaternion.identity, wood, Vector3.right);
            k.B.AddBox(new Vector3(0, 0.09f + h + 0.004f, 0), new Vector3(len, 0.008f, w), Quaternion.identity, cap, Vector3.right);
            foreach (float x in new[] { -len * 0.25f, len * 0.25f }) k.B.AddBox(new Vector3(x, 0.09f + h / 2f, 0), new Vector3(0.03f, h + 0.02f, w + 0.02f), Quaternion.identity, band);
        }

        static void Rebar(LandModel k, MapBuild b)
        {
            int bar = k.M(SurfaceKind.Rebar), wood = k.M(SurfaceKind.PineLumber);
            foreach (float x in new[] { -2f, 2f }) k.B.AddBox(new Vector3(x, 0.045f, 0), new Vector3(0.089f, 0.089f, 0.8f), Quaternion.identity, wood, Vector3.forward);
            var rng = new System.Random(11);
            for (int i = 0; i < 14; i++)
                k.B.AddBox(new Vector3(((float)rng.NextDouble() - 0.5f) * 0.1f, 0.1f + (i / 5) * 0.016f, -0.25f + (i % 5) * 0.12f + ((float)rng.NextDouble() - 0.5f) * 0.03f), new Vector3(6.1f, 0.016f, 0.016f), Quaternion.identity, bar, Vector3.right);
        }
    }
}
