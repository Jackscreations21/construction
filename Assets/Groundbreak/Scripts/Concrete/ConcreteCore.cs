using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Game-time helpers for the concrete trade (set times, unload windows, cure days). Minutes since Day 1 00:00.</summary>
    public static class ConcreteTime
    {
        /// <summary>Extra minutes added by tests / time-skip ("wait for set") on top of the clock.</summary>
        public static float Now
        {
            get
            {
                var c = GameClock.I;
                if (c != null) return (c.Day - 1) * 1440f + c.Hours * 60f;
                return 7f * 60f + Time.time * 1440f / GameClock.RealSecondsPerDay;
            }
        }

        /// <summary>Advances the game clock (used by "wait" buttons and tests). Keeps the sun consistent.</summary>
        public static void Skip(float minutes)
        {
            var c = GameClock.I;
            if (c == null) return;
            c.Hours += minutes / 60f;
            while (c.Hours >= 24f) { c.Hours -= 24f; c.Day++; }
        }

        public static string Duration(float minutes)
        {
            if (minutes < 0) minutes = 0;
            int m = Mathf.RoundToInt(minutes);
            return m >= 60 ? $"{m / 60} h {m % 60:00} min" : $"{m} min";
        }
    }

    /// <summary>Registers concrete-trade materials, tools and the Concrete Dispatch tablet app, and boots the site ground.</summary>
    public static class ConcreteSetup
    {
        static readonly Color Pine = new Color(0.86f, 0.72f, 0.50f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            PieceDefinition Lum(string id, string name, float w, float h, float len, int price) => new PieceDefinition
            { Id = id, Name = name, Category = PieceCategory.Lumber, Size = new Vector3(Units.In(w), Units.In(h), Units.In(len)), Price = price, Cuttable = true, Color = Pine };
            PieceDefinition Con(string id, string name, int price, int units, Color c) => new PieceDefinition
            { Id = id, Name = name, Category = PieceCategory.Consumable, Price = price, UnitsPerPurchase = units, Color = c };

            Catalog.Register(
                Lum("2x4x16", "2x4 x 16'", 3.5f, 1.5f, 192f, 11),
                Lum("2x6x16", "2x6 x 16' (form board)", 5.5f, 1.5f, 192f, 17),
                Lum("2x8x8", "2x8 x 8'", 7.25f, 1.5f, 96f, 10),
                Lum("2x8x16", "2x8 x 16' (form board)", 7.25f, 1.5f, 192f, 22),
                Lum("2x10x16", "2x10 x 16' (form board)", 9.25f, 1.5f, 192f, 29),
                Lum("2x12x16", "2x12 x 16' (form board)", 11.25f, 1.5f, 192f, 38),
                Lum(FormSystem.StakeId, "Wood form stake 2x2 x 24\"", 1.5f, 1.5f, 24f, 2),
                Con(SiteGround.GravelId, "3/4\" crushed stone base (per cu yd, 27 cu ft)", 48, 27, new Color(0.55f, 0.54f, 0.5f)),
                Con(FormSystem.PolyId, "6 mil poly vapor barrier, 20' x 100' roll (sq ft)", 119, 2000, new Color(0.85f, 0.88f, 0.9f)),
                Con(FormSystem.FormOilId, "Form release oil, 5 gal (coats ~2000 sq ft)", 65, 2000, new Color(0.6f, 0.5f, 0.2f)),
                Con(RebarSystem.ChairId, "Rebar chairs 2\" (bag of 100)", 18, 100, new Color(0.5f, 0.5f, 0.5f)),
                Con(PourSystem.AnchorId, "1/2\" x 10\" J-bolt anchors w/ nut + washer (box of 25)", 32, 25, new Color(0.7f, 0.7f, 0.72f))
            );

            void T(string id, string name, int price, string desc, Func<Tool> make) =>
                ToolFactory.Register(new ToolShopItem { Id = id, Name = name, Price = price, Description = desc }, make);

            T("stakeline", "Stakes, Mason String & Line Level", 25, "Drive layout stakes, run string lines, check square by equal diagonals.", () => new StakeLineTool());
            T("marking-paint", "Marking Paint (inverted can)", 9, "Hold to spray layout marks on the ground. Scroll = color (white = dig, orange, pink).", () => new MarkingPaintTool());
            T("laser", "Rotary Laser Level + Grade Rod", 299, "Set up the laser, shoot grade, mark top-of-form on stakes. Scroll = slab height.", () => new LaserLevelTool());
            T("shovel", "Round-Point Shovel", 35, "Dig / backfill by hand; push and place wet concrete.", () => new ShovelTool());
            T("excavator", "Mini Excavator (rental, per day)", 325, "Dig footings and the slab box to a set depth; spread gravel base. 2 slots.", () => new MiniExcavatorTool());
            T("compactor", "Plate Compactor (rental, per day)", 95, "Hold to compact the subgrade / gravel base. 2 slots.", () => new PlateCompactorTool());
            T("formkit", "Form Setting Kit (sledge, duplex nails)", 45, "Click slab corners to set 2x forms; stake, brace, oil, poly, strip.", () => new FormTool());
            T("rebar-tool", "Rebar Cutter / Bender", 189, "Lay rebar on chairs; F = grid 18\" O.C. + footing bars.", () => new RebarTool());
            T("tiewire-twister", "Tie-Wire Twister + Reel", 24, "Tie rebar crossings (uses tie wire rolls).", () => new TieWireTwisterTool());
            T("chute", "Chute Signals + Wheelbarrow (6 cu ft)", 129, "Point the truck's chute; wheelbarrow concrete beyond its reach.", () => new ChuteTool());
            T("conc-rake", "Concrete Rake (come-along)", 39, "Hold to pull concrete into low spots.", () => new FinishTool(FinishOp.Rake));
            T("vibrator", "Pencil Concrete Vibrator", 389, "Hold to consolidate concrete (prevents honeycombing at the forms).", () => new FinishTool(FinishOp.Vibrate));
            T("screed", "Magnesium Screed 12'", 89, "Hold to strike off concrete level with the form tops. 2 slots.", () => new FinishTool(FinishOp.Screed));
            T("bullfloat", "Bull Float + Handles", 149, "Hold to float the surface right after screeding. 2 slots.", () => new FinishTool(FinishOp.BullFloat));
            T("edger", "Hand Edger 3/8\" radius", 18, "Hold along the forms after bleed water is gone.", () => new FinishTool(FinishOp.Edge));
            T("groover", "Hand Groover 1\" bit", 19, "Click two points to tool a control joint.", () => new GrooverTool());
            T("trowel", "Steel Finishing Trowel 14\"", 29, "Hold to trowel smooth. Right-click wet-sets a J-bolt anchor at the edge.", () => new FinishTool(FinishOp.Trowel));
            T("broom", "Concrete Finish Broom 24\"", 45, "Hold to broom a non-slip finish (instead of a hard trowel).", () => new FinishTool(FinishOp.Broom));

            Materials.Custom[SurfaceKind.WetConcrete] = () => ConcreteTextures.MakeMaterial(SurfaceKind.WetConcrete);
            Materials.Custom[SurfaceKind.TroweledConcrete] = () => ConcreteTextures.MakeMaterial(SurfaceKind.TroweledConcrete);
            Materials.Custom[SurfaceKind.BroomedConcrete] = () => ConcreteTextures.MakeMaterial(SurfaceKind.BroomedConcrete);
            Materials.Custom[SurfaceKind.VaporBarrier] = () => ConcreteTextures.MakeMaterial(SurfaceKind.VaporBarrier);
            Materials.CustomTileMeters[SurfaceKind.WetConcrete] = 2f;
            Materials.CustomTileMeters[SurfaceKind.TroweledConcrete] = 2f;
            Materials.CustomTileMeters[SurfaceKind.BroomedConcrete] = 1f;
            Materials.CustomTileMeters[SurfaceKind.VaporBarrier] = 1.5f;

            Tablet.RegisterApp("Concrete Dispatch", ConcreteDispatch.DrawApp);
            Tablet.AddHelp("Concrete: stake + string the corners (equal diagonals = square), laser the grade, dig, gravel, compact.");
            Tablet.AddHelp("Forms: Form kit - click corners, click the first corner to close. F = stake every 36\", RMB = kicker brace, J = oil, V = poly, X = strip.");
            Tablet.AddHelp("Rebar: F = 18\" grid on chairs; tie crossings with the twister. Order concrete in Concrete Dispatch (Takeoff).");
            Tablet.AddHelp("Pour: chute/wheelbarrow, rake, vibrate, screed, bull float; wait out bleed water; edge, groove joints, trowel or broom.");
        }

        /// <summary>Tells the framing support graph (shell-framing branch) that set concrete is foundation, via reflection so
        /// neither branch needs the other to compile: SupportGraph.FoundationProbe = p => on top of a ConcreteElement.</summary>
        static void HookFraming()
        {
            var t = Type.GetType("Groundbreak.SupportGraph");
            if (t == null) foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { t = asm.GetType("Groundbreak.SupportGraph"); if (t != null) break; }
            if (t == null) return;
            Func<Vector3, bool> probe = p => ConcreteElement.TryGetTop(p, out float y) && p.y <= y + 0.02f;
            var f = t.GetField("FoundationProbe", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (f != null && f.FieldType == typeof(Func<Vector3, bool>)) { f.SetValue(null, probe); return; }
            var pr = t.GetProperty("FoundationProbe", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (pr != null && pr.CanWrite && pr.PropertyType == typeof(Func<Vector3, bool>)) pr.SetValue(null, probe);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            HookFraming();
            if (UnityEngine.Object.FindAnyObjectByType<ConcreteSite>() != null) return;
            new GameObject("ConcreteSite").AddComponent<ConcreteSite>();
        }
    }

    /// <summary>Scene host for the concrete systems. Builds the diggable ground once the bootstrap scene exists.</summary>
    public class ConcreteSite : MonoBehaviour
    {
        public static ConcreteSite I;
        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }
        void Start() { SiteGround.Ensure(); }
        void Update() { ConcreteDispatch.Tick(); }
    }

    /// <summary>Small shared helpers for the concrete tools.</summary>
    public static class CTool
    {
        public static bool Aim(out RaycastHit hit, float range = 7f)
        {
            hit = default;
            return Game.Player != null && Game.Player.TryAim(out hit, range);
        }

        public static Vector3 SnapXZ(Vector3 p)
        {
            var s = Game.Snap.SnapPosition(p);
            return new Vector3(s.x, p.y, s.z);
        }

        public static GameObject Visual(string name, Mesh mesh, Material[] mats, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        /// <summary>Material list helper: submesh index per material.</summary>
        public sealed class Mats
        {
            public readonly List<Material> List = new List<Material>();
            public int this[Material m] { get { int i = List.IndexOf(m); if (i < 0) { List.Add(m); i = List.Count - 1; } return i; } }
            public int K(SurfaceKind k) => this[Materials.Get(k)];
            public int K(SurfaceKind k, Color tint) => this[Materials.Get(k, tint)];
            public int C(Color c) => this[MaterialLibrary.Get(c)];
            public Material[] Array => List.ToArray();
        }

        public static GameObject Build(string name, Action<MeshBuilder, Mats> author, Transform parent = null)
        {
            var b = new MeshBuilder(); var m = new Mats();
            author(b, m);
            b.MinSubmeshCount = m.List.Count;
            return Visual(name, b.Build(name), m.Array, parent);
        }

        public static bool PointInPolygon(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y + 1e-9f) + poly[i].x))
                    inside = !inside;
            }
            return inside;
        }

        public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float t = ab.sqrMagnitude > 1e-9f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        public static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>Takes n units of a consumable; toast if short.</summary>
        public static bool TakeStock(string id, int n, string what)
        {
            if (Game.Stock.TryTake(id, n)) return true;
            var d = Catalog.Get(id);
            Game.Toast($"Not enough {what} on site (need {n}, have {Game.Stock.Count(id)}) - order {(d != null ? d.Name : id)}");
            return false;
        }
    }

    /// <summary>Tool with a cooldown for held actions.</summary>
    public abstract class ConcreteToolBase : Tool
    {
        float _next;
        protected bool Ready(float interval)
        {
            if (Time.time < _next) return false;
            _next = Time.time + interval; return true;
        }
        protected string _status = "";
        public override void DrawGUI()
        {
            if (!string.IsNullOrEmpty(_status)) ToolUtil.CenterLabel(_status, new Color(1f, 0.95f, 0.7f), 60f);
        }
    }
}
