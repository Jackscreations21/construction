using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Shared behavior for landscaping tools: aim at ground or a landscape job, work the job's current stage
    /// if it calls for this tool, otherwise do the tool's own ground action. Shows a grade/slope readout.</summary>
    public abstract class LandTool : Tool
    {
        public const float Range = 6f;
        protected string _msg = ""; protected float _msgUntil;

        protected bool Aim(out RaycastHit hit, out LandscapeFeature f)
        {
            f = null; hit = default;
            if (Game.Player == null || !Game.Player.TryAim(out hit, Range)) return false;
            f = hit.collider.GetComponentInParent<LandscapeFeature>();
            return true;
        }

        protected void Say(string m) { _msg = m; _msgUntil = Time.time + 2.5f; }
        public void SayPublic(string m) => Say(m);

        /// <summary>Works the aimed feature if its current stage uses this tool. True if handled.</summary>
        protected bool TryWorkFeature(LandscapeFeature f)
        {
            if (f == null) return false;
            var c = f.Current;
            if (!c.HasValue || c.Value.Tool != Id) return false;
            f.Work(Id, out var msg); Say(msg);
            ToolUtil.Wear(this, 0.0005f);
            return true;
        }

        public override void Primary()
        {
            if (!Aim(out var hit, out var f)) return;
            if (TryWorkFeature(f)) return;
            if (!Grade.Ensure(hit.point)) { Say("Outside the graded site area"); return; }
            GroundPrimary(hit, f);
        }

        public override void Secondary()
        {
            if (!Aim(out var hit, out var f)) return;
            if (!Grade.Ensure(hit.point)) { Say("Outside the graded site area"); return; }
            GroundSecondary(hit, f);
        }

        protected virtual void GroundPrimary(RaycastHit hit, LandscapeFeature f) { if (f != null) Say(f.Hint); }
        protected virtual void GroundSecondary(RaycastHit hit, LandscapeFeature f) { }

        /// <summary>Slope readout at a point: percent, inches of fall per 10', and the fall measured away from the building.</summary>
        public static string SlopeReadout(Vector3 p)
        {
            Vector2 g = Grade.Gradient(p.x, p.z);
            float pct = g.magnitude * 100f;
            string s = $"Grade {Units.Format(Grade.HeightAt(p.x, p.z))}  slope {pct:0.0}% ({Units.Format(g.magnitude * 10 * Units.Foot)} per 10')";
            if (Site.BuildingBounds(out var b))
            {
                Vector3 closest = b.ClosestPoint(new Vector3(p.x, b.center.y, p.z));
                Vector3 away = new Vector3(p.x - closest.x, 0, p.z - closest.z);
                if (away.magnitude > 0.05f && away.magnitude < 6f)
                {
                    away.Normalize();
                    float fall = -(g.x * away.x + g.y * away.z) * 10 * Units.Foot;
                    bool ok = fall >= SiteRules.RequiredFall - Units.In(0.25f);
                    s += $"\nAway from house: {(fall >= 0 ? "falls " : "RISES ")}{Units.Format(Mathf.Abs(fall))} in 10' {(ok ? "- OK (6\" min)" : "- need 6\" fall")}";
                }
            }
            return s;
        }

        public override void DrawGUI()
        {
            var lines = new List<string>();
            if (Aim(out var hit, out var f))
            {
                if (f != null) lines.Add(f.Hint);
                else
                {
                    lines.Add(SlopeReadout(hit.point));
                    lines.Add("Ground: " + Grade.CoverAt(hit.point.x, hit.point.z) + (Grade.DirtOnHand > 0.01f ? $"   loose dirt on hand {Grade.DirtOnHand:0.00} yd³" : ""));
                }
            }
            if (Time.time < _msgUntil) lines.Add(_msg);
            if (lines.Count == 0) return;
            UISkin.InfoBox(string.Join("\n", lines), Screen.height * 0.58f);
        }

        protected static float Yd(float m3) => m3 / Grade.CubicYard;
    }

    // ---------------------------------------------------------------------- hand tools

    /// <summary>The one shovel: on the lot it grades (Grade); on the building pad or in wet concrete it is the concrete
    /// crew's shovel (Concrete/ShovelTool on SiteGround's 4" cells).</summary>
    public class LandShovelTool : LandTool
    {
        public override string Id => "shovel";
        public override string Name => "Round-point Shovel";
        public override string Hint => "LMB dig (lower 2\") · RMB fill from loose dirt / fill-dirt stock · works holes & excavation · on the pad: trenches, push wet concrete";
        readonly ShovelTool _pad = new ShovelTool();

        bool OnPad(out RaycastHit hit)
        {
            if (!Aim(out hit, out var f) || f != null) return false;
            if (hit.collider.GetComponentInParent<PourSystem>() != null) return true;
            return Grade.InPad(hit.point.x, hit.point.z);
        }

        public override void Primary()
        {
            if (OnPad(out var hit)) { _pad.Primary(); Sfx.Play("shovel", hit.point); ToolUtil.Wear(this, 0.0005f); return; }
            base.Primary();
        }

        public override void Secondary()
        {
            if (OnPad(out var hit)) { _pad.Secondary(); Sfx.Play("shovel", hit.point); return; }
            base.Secondary();
        }

        // registered after the concrete milestone's BeforeSceneLoad "shovel" so this unified one wins in the Tool Shop
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterUnified() =>
            ToolFactory.Register(new ToolShopItem { Id = "shovel", Name = "Round-point Shovel", Price = 32, Description = "Dig and fill the lot (grade readout, slope away from the house); trenches on the building pad; push wet concrete." }, () => new LandShovelTool());
        protected override void GroundPrimary(RaycastHit hit, LandscapeFeature f)
        {
            if (f != null) { Say(f.Hint); return; }
            float r = GInput.Shift ? 1.0f : 0.6f;
            float vol = -Grade.Field.Raise(hit.point, r, -Units.In(2));
            Grade.Field.Paint(hit.point, r * 0.8f, LandCover.Dirt, c => c == LandCover.Grass || c == LandCover.Lawn || c == LandCover.Topsoil);
            Grade.DirtOnHand += Yd(vol);
            Sfx.Play("shovel", hit.point);
            ToolUtil.Wear(this, 0.0005f);
        }
        protected override void GroundSecondary(RaycastHit hit, LandscapeFeature f)
        {
            float r = GInput.Shift ? 1.0f : 0.6f;
            float vol = Mathf.PI * r * r * 0.5f * Units.In(2);
            if (!Grade.TakeFill(vol)) { Say("No loose dirt - dig somewhere or order fill dirt"); return; }
            Grade.Field.Raise(hit.point, r, Units.In(2));
            Grade.Field.Paint(hit.point, r * 0.8f, LandCover.Dirt, c => c == LandCover.Grass || c == LandCover.Lawn);
            Sfx.Play("shovel", hit.point);
        }
    }

    public class SpadeTool : LandTool
    {
        public override string Id => "spade";
        public override string Name => "Edging Spade";
        public override string Hint => "LMB cut bed edge / excavate walks · RMB spread 3\" of mulch";
        protected override void GroundPrimary(RaycastHit hit, LandscapeFeature f)
        {
            if (f != null) { Say(f.Hint); return; }
            Grade.Field.Paint(hit.point, 0.4f, LandCover.Dirt, c => c == LandCover.Grass || c == LandCover.Lawn);
            Sfx.Play("shovel", hit.point);
        }
        protected override void GroundSecondary(RaycastHit hit, LandscapeFeature f)
        {
            float r = 0.75f, yd = Yd(Mathf.PI * r * r * Units.In(3));
            if (!Bulk.Take("mulch", yd)) { Say("No mulch on site (Garden Center: bulk)"); return; }
            Grade.Field.Paint(hit.point, r, LandCover.Mulch);
            Sfx.Play("gravel", hit.point, 0.5f);
        }
    }

    public class RakeTool : LandTool
    {
        public override string Id => "rake";
        public override string Name => "Landscape Rake";
        public override string Hint => "LMB smooth/level grade · RMB spread 2\" topsoil · screeds sand, sweeps joint sand";
        protected override void GroundPrimary(RaycastHit hit, LandscapeFeature f)
        {
            if (f != null) { Say(f.Hint); return; }
            Grade.Field.Smooth(hit.point, GInput.Shift ? 2.2f : 1.4f, 0.6f);
            Sfx.Play("rake", hit.point, 0.6f);
        }
        protected override void GroundSecondary(RaycastHit hit, LandscapeFeature f)
        {
            float r = 1.0f, vol = Mathf.PI * r * r * 0.5f * Units.In(2);
            if (!Bulk.Take("topsoil", Yd(vol))) { Say("No topsoil on site (Garden Center: bulk)"); return; }
            Grade.Field.Raise(hit.point, r, Units.In(2));
            Grade.Field.Paint(hit.point, r, LandCover.Topsoil, c => c != LandCover.Gravel && c != LandCover.Mulch);
            Sfx.Play("rake", hit.point, 0.6f);
        }
    }

    public class HoeTool : LandTool
    {
        public override string Id => "hoe";
        public override string Name => "Garden Hoe";
        public override string Hint => "LMB loosen soil / chop sod · RMB sow seed + cover with straw (on bare soil)";
        protected override void GroundPrimary(RaycastHit hit, LandscapeFeature f)
        {
            if (f != null) { Say(f.Hint); return; }
            Grade.Field.Paint(hit.point, 0.5f, LandCover.Dirt, c => c == LandCover.Grass || c == LandCover.Lawn);
            Sfx.Play("shovel", hit.point, 0.5f);
        }
        protected override void GroundSecondary(RaycastHit hit, LandscapeFeature f) => SowAt(hit.point, out var m, this);

        /// <summary>Seed + straw a 1.2 m radius patch. Needs bare soil (dirt/topsoil). 1 bag of seed and 1 bale cover 1,000 sq ft.</summary>
        public static bool SowAt(Vector3 p, out string msg, LandTool say = null)
        {
            var cover = Grade.CoverAt(p.x, p.z);
            if (cover != LandCover.Dirt && cover != LandCover.Topsoil) { msg = "Seed needs bare raked soil (strip/till and spread topsoil first)"; say?.SayPublic(msg); return false; }
            float r = 1.2f, sqft = Mathf.PI * r * r * 10.764f, units = sqft / 1000f;
            if (Bulk.OnHand("grass-seed") < units || Bulk.OnHand("straw-bale") < units) { msg = "Need grass seed and a straw bale on site"; say?.SayPublic(msg); return false; }
            Bulk.Take("grass-seed", units); Bulk.Take("straw-bale", units);
            Grade.Field.Paint(p, r, LandCover.Seeded, c => c == LandCover.Dirt || c == LandCover.Topsoil);
            LandscapeSystem.AddSeedPatch(p, r);
            Sfx.Play("rake", p, 0.5f);
            msg = "Seeded & strawed - keep it watered (hose) ~5 days";
            say?.SayPublic(msg);
            return true;
        }
    }

    public class HoseTool : LandTool
    {
        public override string Id => "hose";
        public override string Name => "Garden Hose";
        public override string Hint => "LMB water plants, sod and seeded ground";
        public override void Primary()
        {
            if (!Aim(out var hit, out var f)) return;
            if (TryWorkFeature(f)) return;
            int n = LandscapeSystem.WaterAround(hit.point, 2.5f);
            Sfx.Play("water", hit.point, 0.6f);
            Say(n > 0 ? $"Watered {n} item(s)" : "Nothing here needs water");
        }
    }

    public class PostHoleDiggerTool : LandTool
    {
        public override string Id => "posthole";
        public override string Name => "Post-hole Digger";
        public override string Hint => "LMB dig post holes on a fence/gate layout (2 bites per 24\" hole)";
        protected override void GroundPrimary(RaycastHit hit, LandscapeFeature f) => Say(f != null ? f.Hint : "Lay out a fence first (Landscaping hands)");
    }

    // ---------------------------------------------------------------------- big tools (2 slots)

    public class WheelbarrowTool : LandTool
    {
        public override string Id => "wheelbarrow";
        public override string Name => "Wheelbarrow";
        public override int Slots => 2;
        public static readonly string[] Loads = { "crusher-run", "gravel-57", "paver-sand", "topsoil", "mulch", "fill-dirt" };
        public int Load;
        public override string Hint => $"Load: {LandCatalog.Name(Loads[Load])} ({Bulk.OnHand(Loads[Load]):0.0} yd on site) · RMB change · LMB dump 6 cu ft";

        public override void Primary()
        {
            if (!Aim(out var hit, out var f)) return;
            if (f != null && f.Current.HasValue && f.Current.Value.Tool == Id)
            {
                int i = System.Array.IndexOf(Loads, f.Current.Value.Material);
                if (i >= 0) Load = i;
                TryWorkFeature(f); return;
            }
            if (!Grade.Ensure(hit.point)) return;
            string id = Loads[Load];
            if (!Bulk.Take(id, LandscapeFeature.LoadYd)) { Say($"No {LandCatalog.Name(id)} on site"); return; }
            float vol = LandscapeFeature.LoadYd * Grade.CubicYard, r = 0.9f;
            Grade.Field.Raise(hit.point, r, vol / (Mathf.PI * r * r * 0.5f));
            var cover = id == "mulch" ? LandCover.Mulch : id == "topsoil" || id == "fill-dirt" ? LandCover.Topsoil : id == "paver-sand" ? LandCover.Sand : LandCover.Gravel;
            Grade.Field.Paint(hit.point, r * 0.85f, cover);
            Sfx.Play("gravel", hit.point);
        }

        public override void Secondary() { Load = (Load + (GInput.Shift ? Loads.Length - 1 : 1)) % Loads.Length; Say("Load: " + LandCatalog.Name(Loads[Load])); }
    }

    public class SodCutterTool : LandTool
    {
        public override string Id => "sodcutter";
        public override string Name => "Sod Cutter (gas)";
        public override int Slots => 2;
        public override string Hint => "LMB strip an 18\" x 3' strip of turf ahead (bare dirt) · strips drive layouts";
        protected override void GroundPrimary(RaycastHit hit, LandscapeFeature f)
        {
            if (f != null) { Say(f.Hint); return; }
            Vector3 fwd = Game.Player.transform.forward; fwd.y = 0;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            int n = Grade.Field.PaintRect(hit.point + fwd.normalized * 0.45f, new Vector2(Units.In(9), 0.45f), yaw, LandCover.Dirt);
            Sfx.Play("engine", hit.point, 0.7f);
            if (n == 0) Say("Nothing to strip here");
        }
    }

    public class TillerTool : LandTool
    {
        public override string Id => "tiller";
        public override string Name => "Rear-tine Tiller";
        public override int Slots => 2;
        public override string Hint => "LMB till a 3' patch to loose soil (ready for topsoil / seed / beds)";
        protected override void GroundPrimary(RaycastHit hit, LandscapeFeature f)
        {
            if (f != null) { Say(f.Hint); return; }
            Grade.Field.Paint(hit.point, 0.9f, LandCover.Dirt, c => c != LandCover.Gravel && c != LandCover.Mulch);
            Grade.Field.Smooth(hit.point, 0.9f, 0.3f);
            Sfx.Play("engine", hit.point, 0.8f);
        }
    }

    // ---------------------------------------------------------------------- registration

    public static class LandToolRegistry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            ToolFactory.Register(new ToolShopItem { Id = "landhands", Name = "Landscaping Layout Kit", Price = 19, Description = "Stakes, mason's line, marking paint. Lay out walks, drives, fences, beds; set plants, sod, blocks, lights. RMB = item, scroll = rotate." }, () => new LandHandsTool());
            ToolFactory.Register(new ToolShopItem { Id = "shovel", Name = "Round-point Shovel", Price = 32, Description = "Dig and fill; grade readout with slope away from the house." }, () => new LandShovelTool());
            ToolFactory.Register(new ToolShopItem { Id = "spade", Name = "Edging Spade", Price = 34, Description = "Cut bed edges, excavate walk beds, spread mulch." }, () => new SpadeTool());
            ToolFactory.Register(new ToolShopItem { Id = "rake", Name = "Landscape Rake", Price = 29, Description = "Smooth and level grade, spread topsoil, screed sand." }, () => new RakeTool());
            ToolFactory.Register(new ToolShopItem { Id = "hoe", Name = "Garden Hoe", Price = 22, Description = "Loosen soil; sow grass seed and straw." }, () => new HoeTool());
            ToolFactory.Register(new ToolShopItem { Id = "hose", Name = "Garden Hose 75'", Price = 35, Description = "Water in plants, sod and seed." }, () => new HoseTool());
            ToolFactory.Register(new ToolShopItem { Id = "posthole", Name = "Post-hole Digger", Price = 45, Description = "Clamshell digger for 10\" fence post holes." }, () => new PostHoleDiggerTool());
            ToolFactory.Register(new ToolShopItem { Id = "wheelbarrow", Name = "Wheelbarrow 6 cu ft", Price = 129, Description = "Moves gravel, sand, topsoil, mulch. 2 slots." }, () => new WheelbarrowTool());
            ToolFactory.Register(new ToolShopItem { Id = "sodcutter", Name = "Sod Cutter (gas)", Price = 899, Description = "Strips turf in 18\" strips. 2 slots." }, () => new SodCutterTool());
            ToolFactory.Register(new ToolShopItem { Id = "tiller", Name = "Rear-tine Tiller", Price = 649, Description = "Tills soil for beds and lawns. 2 slots." }, () => new TillerTool());
            Tablet.AddHelp("Landscaping: buy tools in Tool Shop, materials in Garden Center. Aim a tool at a staked job to do its next step (the hint says which tool).");
        }
    }
}
