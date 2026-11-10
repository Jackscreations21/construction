using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum Sheen { Flat, Eggshell, Satin, SemiGloss }

    /// <summary>Paint data: dry/recoat time, coverage per gallon, tool reach.</summary>
    public static class PaintRules
    {
        public static float DryHours = 2f;              // latex recoat
        public static float SqFtPerGallon = 375f;       // finish paint per coat
        public static float PrimerSqFtPerGallon = 300f; // PVA primer per coat (thirsty new board)
        public static float RollerWidthIn = 9f, RollerStrokeIn = 48f;
        public static float RollerEdgeIn = 2f;          // a roller can't get closer than ~2" to corners / edges
        public static float BrushBandIn = 3f;           // cut-in band
        public static float BrushRunIn = 48f;
        public static float SprayWidthIn = 24f, SprayStrokeIn = 96f;
        public static float Smoothness(Sheen s) => s == Sheen.Flat ? 0.08f : s == Sheen.Eggshell ? 0.2f : s == Sheen.Satin ? 0.3f : 0.48f;
    }

    public struct PaintColor { public string Name; public Color Color; public PaintColor(string n, Color c) { Name = n; Color = c; } }

    /// <summary>Palette of realistic interior paint colors + the current selection (shared by roller/brush/sprayer).</summary>
    public static class PaintColors
    {
        public static readonly List<PaintColor> All = new List<PaintColor>
        {
            new PaintColor("Ceiling White", FinishNoise.C(242, 242, 238)),
            new PaintColor("Chalk White", FinishNoise.C(238, 236, 228)),
            new PaintColor("Linen", FinishNoise.C(232, 224, 207)),
            new PaintColor("Warm Greige", FinishNoise.C(205, 196, 180)),
            new PaintColor("Pewter Gray", FinishNoise.C(170, 170, 165)),
            new PaintColor("Sage Green", FinishNoise.C(176, 186, 160)),
            new PaintColor("Harbor Blue", FinishNoise.C(122, 146, 166)),
            new PaintColor("Deep Navy", FinishNoise.C(52, 64, 88)),
            new PaintColor("Terracotta", FinishNoise.C(186, 112, 84)),
            new PaintColor("Butter Yellow", FinishNoise.C(240, 222, 160)),
            new PaintColor("Blush", FinishNoise.C(226, 196, 186)),
            new PaintColor("Charcoal", FinishNoise.C(66, 66, 66)),
        };
        public static int Current = 2;
        public static Sheen CurrentSheen = Sheen.Eggshell;
        public static PaintColor Selected => All[Mathf.Clamp(Current, 0, All.Count - 1)];
        public static PaintColor Get(string name) { var i = All.FindIndex(c => c.Name == name); return i >= 0 ? All[i] : All[0]; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Catalog.Register(
                FinishStock.Item(PaintJob.PrimerId, "PVA drywall primer, 5 gal", 89, 5, PrimerTint),
                FinishStock.Item(PaintJob.PaintId, "Interior latex paint, 1 gal (tinted at the counter)", 42, 1, new Color(0.9f, 0.88f, 0.84f)));
            FinishSupply.Add("Paint", PaintJob.PrimerId, PaintJob.PaintId);
            ToolFactory.Register(new ToolShopItem { Id = "paint-roller", Name = "Paint Roller 9\" (3/8\" nap) + Tray", Price = 18, Description = "Rolls 9\" x 4' strokes; can't reach the last ~2\" at corners and edges — cut in with the brush." }, () => new PaintRoller());
            ToolFactory.Register(new ToolShopItem { Id = "paint-brush", Name = "Angled Sash Brush 2-1/2\"", Price = 16, Description = "Cuts in along corners, ceilings, openings and boxes." }, () => new PaintBrush());
            ToolFactory.Register(new ToolShopItem { Id = "paint-sprayer", Name = "Airless Paint Sprayer", Price = 449, Description = "Fast, wide fan: primer, paint, or spray texture (orange peel / knockdown). Hold LMB." }, () => new PaintSprayer());
            Tablet.RegisterApp("Paint Colors", DrawApp);
            Tablet.AddHelp("Paint: PVA primer first on new drywall. Roller for fields, brush to cut in edges, sprayer for speed. [ / ] = color, Shift+[ / ] = sheen (or the Paint Colors app). First coat looks thin — roll a second after it dries.");
        }

        static readonly Color PrimerTint = new Color(0.955f, 0.955f, 0.945f);

        public static void Cycle(int dir) { Current = (Current + dir + All.Count) % All.Count; Game.Toast($"Paint: {Selected.Name} ({CurrentSheen})"); }
        public static void CycleSheen(int dir) { CurrentSheen = (Sheen)(((int)CurrentSheen + dir + 4) % 4); Game.Toast($"Sheen: {CurrentSheen}"); }

        static void DrawApp()
        {
            var t = Tablet.I; if (t == null) return;
            GUILayout.Label("Interior paint colors (tinted at the counter). Selected: " + Selected.Name + " · " + CurrentSheen, t.HeaderStyle);
            int cols = 4;
            for (int r = 0; r * cols < All.Count; r++)
            {
                GUILayout.BeginHorizontal();
                for (int k = r * cols; k < Mathf.Min(All.Count, r * cols + cols); k++)
                {
                    var prev = GUI.backgroundColor; GUI.backgroundColor = All[k].Color;
                    if (GUILayout.Button((k == Current ? "► " : "") + All[k].Name, t.ButtonStyle, GUILayout.Width(200), GUILayout.Height(54))) Current = k;
                    GUI.backgroundColor = prev;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            foreach (Sheen s in System.Enum.GetValues(typeof(Sheen)))
                if (GUILayout.Button((s == CurrentSheen ? "► " : "") + s, t.ButtonStyle, GUILayout.Width(150))) CurrentSheen = s;
            GUILayout.EndHorizontal();
            GUILayout.Label($"Coverage ≈ {PaintRules.SqFtPerGallon:0} sq ft/gal per coat · PVA primer ≈ {PaintRules.PrimerSqFtPerGallon:0} sq ft/gal · recoat after {PaintRules.DryHours:0} h", t.LabelStyle);
        }

        /// <summary>Swatch + name drawn by the paint tools while equipped.</summary>
        public static void DrawSwatch(string mode)
        {
            var r = new Rect(Screen.width - 250, Screen.height - 120, 230, 90);
            GUI.Box(r, "");
            var prev = GUI.color; GUI.color = mode == "Primer" ? PrimerTint : Selected.Color;
            GUI.DrawTexture(new Rect(r.x + 10, r.y + 10, 70, 70), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(new Rect(r.x + 90, r.y + 10, 140, 70), mode == "Primer" ? "PVA primer" : $"{Selected.Name}\n{CurrentSheen}\n[ ] color  ⇧[ ] sheen");
        }
    }

    /// <summary>Priming / painting scripted API (tools call the same).</summary>
    public static class PaintJob
    {
        public const string PrimerId = "pt-primer", PaintId = "pt-paint-gal";
        static float _primerGal, _paintGal;
        public static float GallonsUsedPrimer, GallonsUsedPaint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() { _primerGal = _paintGal = 0f; GallonsUsedPaint = GallonsUsedPrimer = 0f; }

        static bool Ready(DrywallSurface s, bool primer, out string why)
        {
            why = null;
            if (!Drywall.TextureDry(s)) { why = $"Texture is still wet ({DrywallRules.TextureDryHours:0} h)"; return false; }
            return true;
        }

        static bool Consume(int texels, bool primer)
        {
            if (texels <= 0) return true;
            float sq = texels * DrywallSurface.T * DrywallSurface.T / (Units.Foot * Units.Foot);
            float gal = sq / (primer ? PaintRules.PrimerSqFtPerGallon : PaintRules.SqFtPerGallon);
            if (primer) { GallonsUsedPrimer += gal; while (_primerGal < gal) { if (!FinishStock.Take(PrimerId)) return false; _primerGal += 1f; } _primerGal -= gal; }
            else { GallonsUsedPaint += gal; while (_paintGal < gal) { if (!FinishStock.Take(PaintId)) return false; _paintGal += 1f; } _paintGal -= gal; }
            return true;
        }

        static int Apply(DrywallSurface s, Rect r, bool primer, Color c, Sheen sh, float minEdge, float maxEdge)
        {
            s.Refresh();
            if (!Ready(s, primer, out var why)) { Game.Toast(why); return 0; }
            int n = s.CoatRect(r, primer, c, PaintRules.Smoothness(sh), minEdge, maxEdge);
            Consume(n, primer);
            return n;
        }

        /// <summary>Roller stroke (9" x 4') centered at uv — stays ~2" off edges and corners.</summary>
        public static int Roll(DrywallSurface s, Vector2 uv, bool primer, Color c, Sheen sh)
        {
            float w = Units.In(PaintRules.RollerWidthIn), l = Units.In(PaintRules.RollerStrokeIn);
            return Apply(s, new Rect(uv.x - w / 2, uv.y - l / 2, w, l), primer, c, sh, Units.In(PaintRules.RollerEdgeIn), float.MaxValue);
        }

        /// <summary>Brush cut-in: the band along edges/corners/openings within a 4' run around uv.</summary>
        public static int CutIn(DrywallSurface s, Vector2 uv, bool primer, Color c, Sheen sh)
        {
            float run = Units.In(PaintRules.BrushRunIn);
            return Apply(s, new Rect(uv.x - run / 2, uv.y - run / 2, run, run), primer, c, sh, 0f, Units.In(PaintRules.BrushBandIn));
        }

        /// <summary>Airless sprayer pass (2' x 8') — reaches everything.</summary>
        public static int Spray(DrywallSurface s, Vector2 uv, bool primer, Color c, Sheen sh)
        {
            float w = Units.In(PaintRules.SprayWidthIn), l = Units.In(PaintRules.SprayStrokeIn);
            return Apply(s, new Rect(uv.x - w / 2, uv.y - l / 2, w, l), primer, c, sh, 0f, float.MaxValue);
        }

        /// <summary>One full coat of PVA primer over the whole surface (cut in + rolled).</summary>
        public static int Prime(DrywallSurface s)
        {
            if (!Drywall.AllSanded(s)) Game.Toast("Joints aren't finished/sanded — they'll show through");
            return Apply(s, s.Area, true, DrywallSurface.PrimerColor, Sheen.Flat, 0f, float.MaxValue);
        }

        /// <summary>One full coat of paint over the whole surface (cut in + rolled). Unprimed board "flashes" over joints.</summary>
        public static int Paint(DrywallSurface s, PaintColor color, Sheen sheen)
        {
            if (s.Coverage(true, 1) < 0.5f) Game.Toast("No primer — paint will flash over the joints");
            return Apply(s, s.Area, false, color.Color, sheen, 0f, float.MaxValue);
        }
    }

    /// <summary>Shared base for roller / brush / sprayer.</summary>
    public abstract class PaintToolBase : FinishTool
    {
        public override float Range => 3.5f;
        protected bool PrimerMode => ModeName == "Primer";
        protected abstract int ApplyAt(DrywallSurface s, Vector2 uv);

        public override void Tick()
        {
            if (Game.UIOpen) return;
            if (GInput.GetKeyDown(KeyCode.LeftBracket)) { if (GInput.Shift) PaintColors.CycleSheen(-1); else PaintColors.Cycle(-1); }
            if (GInput.GetKeyDown(KeyCode.RightBracket)) { if (GInput.Shift) PaintColors.CycleSheen(1); else PaintColors.Cycle(1); }
        }

        public override void DrawGUI() { PaintColors.DrawSwatch(ModeName); }

        protected override void Use(RaycastHit hit)
        {
            var s = Drywall.SurfaceAt(hit);
            if (s == null) { Game.Toast("Aim at drywall"); return; }
            var uv = s.Fp.ToUV(hit.point);
            int n = ApplyAt(s, uv);
            if (n == 0) Game.Toast(PrimerMode ? "Nothing new to prime here (wet or out of reach)" : "Nothing new to paint here (wet, or out of this tool's reach)");
            ToolUtil.Wear(this, 0.0005f);
        }
    }

    public class PaintRoller : PaintToolBase
    {
        public override string Id => "paint-roller";
        public override string Name => "Paint Roller 9\"";
        static readonly string[] _m = { "Paint", "Primer" };
        public override string[] Modes => _m;
        protected override int ApplyAt(DrywallSurface s, Vector2 uv) => PaintJob.Roll(s, uv, PrimerMode, PaintColors.Selected.Color, PaintColors.CurrentSheen);
    }

    public class PaintBrush : PaintToolBase
    {
        public override string Id => "paint-brush";
        public override string Name => "Sash Brush";
        static readonly string[] _m = { "Paint", "Primer" };
        public override string[] Modes => _m;
        protected override int ApplyAt(DrywallSurface s, Vector2 uv) => PaintJob.CutIn(s, uv, PrimerMode, PaintColors.Selected.Color, PaintColors.CurrentSheen);
    }

    public class PaintSprayer : PaintToolBase
    {
        public override string Id => "paint-sprayer";
        public override string Name => "Airless Sprayer";
        public override int Slots => 2;
        static readonly string[] _m = { "Paint", "Primer", "Texture: orange peel", "Texture: knockdown" };
        public override string[] Modes => _m;
        float _cool;
        public override void PrimaryHeld()
        {
            if (Game.UIOpen || Mode >= 2) return;
            _cool -= Time.deltaTime; if (_cool > 0f) return; _cool = 0.25f;
            if (FinishAim.Aim(Range, out var hit)) { var s = Drywall.SurfaceAt(hit); if (s != null) ApplyAt(s, s.Fp.ToUV(hit.point)); }
        }
        protected override int ApplyAt(DrywallSurface s, Vector2 uv)
        {
            if (Mode >= 2)
            {
                string kind = Mode == 2 ? "orange-peel" : "knockdown";
                float w = Units.In(48f), l = Units.In(96f);
                return Drywall.ApplyTexture(s, kind, new Rect(uv.x - w / 2, uv.y - l / 2, w, l)) ? 1 : 0;
            }
            return PaintJob.Spray(s, uv, PrimerMode, PaintColors.Selected.Color, PaintColors.CurrentSheen);
        }
    }
}
