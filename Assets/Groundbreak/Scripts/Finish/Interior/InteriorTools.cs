using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    static class InteriorAim
    {
        public static T Find<T>(RaycastHit hit) where T : Component => hit.collider != null ? hit.collider.GetComponentInParent<T>() : null;

        public static HungDoor DoorNear(RaycastHit hit)
        {
            var d = Find<HungDoor>(hit);
            if (d != null) return d;
            HungDoor best = null; float bd = 1.5f;
            foreach (var h in HungDoor.All)
            {
                if (h == null) continue;
                var p = h.Base + Vector3.up * 1f;
                float dist = Vector3.Distance(p, hit.point);
                if (dist < bd) { bd = dist; best = h; }
            }
            return best;
        }

        /// <summary>Floor face under the hit (subfloor), even through a finish layer.</summary>
        public static FramePlane FloorAt(RaycastHit hit)
        {
            var fp = FinishAim.FaceBehind(hit);
            if (fp != null && fp.IsFloor) return fp;
            foreach (var h in FinishAim.SortedHits(new Ray(hit.point + Vector3.up * 0.05f, Vector3.down), 0.3f))
            {
                var f = FramePlane.FromHit(h);
                if (f != null && f.IsFloor) return f;
            }
            return null;
        }
    }

    /// <summary>15 ga angled finish nailer: hangs prehung doors (nails through jamb + shims) and nails off trim.</summary>
    public class FinishNailer : CloseupTool
    {
        public override string Id => "finish-nailer";
        public override string Name => "15 ga Finish Nailer";
        public override string[] Modes => new[] { "Hang prehung door", "Nail aimed trim", "Nail all trim" };
        static readonly string[] DoorIds = { "door-int-6p32", "door-int-shaker32", "door-int-6p30", "door-int-6p24", "door-bifold36", "door-barn36" };

        protected override void Use(RaycastHit hit)
        {
            switch (Mode)
            {
                case 0:
                {
                    var fp = FinishAim.FaceBehind(hit);
                    if (fp == null) { Game.Toast("Aim into a door rough opening"); return; }
                    var ro = DoorHanging.OpeningAt(fp, fp.ToUV(hit.point));
                    if (ro == null) { Game.Toast("Aim into a door rough opening"); return; }
                    foreach (var id in DoorIds)
                    {
                        var d = Catalog.Get(id); if (d == null || !FinishStock.Has(id)) continue;
                        float w = ro.Value.width - d.Size.x;
                        if (id.Contains("barn") || (w > Units.In(1.6f) && w < Units.In(2.75f))) { DoorHanging.Hang(fp, ro.Value, id, true); ToolUtil.Wear(this, 0.002f); return; }
                    }
                    Game.Toast($"No prehung door on site fits this {Units.Format(ro.Value.width)} opening (door + 2\") — order one");
                    return;
                }
                case 1:
                {
                    var t = InteriorAim.Find<TrimPiece>(hit);
                    if (t == null) { Game.Toast("Aim at a piece of trim"); return; }
                    if (t.Nailed) { Game.Toast("Already nailed off"); return; }
                    if (!InteriorSupplies.UseFasteners("fin-nails-15ga", 10)) return;
                    t.Nail(); ToolUtil.Wear(this, 0.0005f);
                    Game.Toast($"{t.Nails} nails, {t.NailsInFraming} into framing");
                    return;
                }
                default:
                    Game.Toast($"Nailed off {WallTrim.NailAll()} nails");
                    return;
            }
        }
    }

    /// <summary>18 ga brad nailer: small trim and casing-to-jamb brads.</summary>
    public class BradNailer : CloseupTool
    {
        public override string Id => "brad-nailer";
        public override string Name => "18 ga Brad Nailer";
        protected override void Use(RaycastHit hit)
        {
            var t = InteriorAim.Find<TrimPiece>(hit);
            if (t == null) { Game.Toast("Aim at trim to brad it"); return; }
            if (!InteriorSupplies.UseFasteners("fin-brads-18ga", 8)) return;
            if (!t.Nailed) t.Nail();
            Game.Toast("Bradded");
        }
    }

    /// <summary>10" compound miter saw: cut-and-fit casing, base and crown. RMB = profile, Shift+RMB = primed / red oak.</summary>
    public class MiterSaw : CloseupTool
    {
        public override string Id => "miter-saw";
        public override string Name => "10\" Compound Miter Saw";
        public override int Slots => 2;
        public TrimSpecies Species;
        static readonly string[] Ids = { "casing-colonial", "casing-craftsman", "base-colonial", "base-525", "crown-358", "crown-458" };
        public override string[] Modes { get { var m = new string[Ids.Length]; for (int i = 0; i < m.Length; i++) m[i] = TrimProfile.Get(Ids[i]).Name; return m; } }
        public override string Hint => $"[{ModeName}, {(Species == TrimSpecies.Oak ? "red oak" : "primed")}] LMB cut & fit (aim at door / wall) · RMB profile · Shift+RMB species";

        public override void Secondary()
        {
            if (GInput.Shift) { Species = Species == TrimSpecies.Oak ? TrimSpecies.Primed : TrimSpecies.Oak; Game.Toast("Trim: " + Species); return; }
            base.Secondary();
        }

        protected override void Use(RaycastHit hit)
        {
            var prof = TrimProfile.Get(Ids[Mathf.Clamp(Mode, 0, Ids.Length - 1)]);
            if (prof.Kind == TrimKind.Casing)
            {
                var d = InteriorAim.DoorNear(hit);
                if (d == null) { Game.Toast("Aim at a hung door to case it"); return; }
                Casing.CaseDoor(d, prof.Id, Species);
            }
            else
            {
                var fp = FinishAim.FaceBehind(hit);
                if (fp == null || !fp.IsWall) { Game.Toast("Aim at a wall"); return; }
                if (prof.Kind == TrimKind.Base) WallTrim.RunBase(fp, prof.Id, Species); else WallTrim.RunCrown(fp, prof.Id, Species);
            }
            ToolUtil.Wear(this, 0.001f);
        }
    }

    /// <summary>Coping saw: cope an inside corner (re-cut an inside miter as a cope).</summary>
    public class CopingSaw : CloseupTool
    {
        public override string Id => "coping-saw";
        public override string Name => "Coping Saw";
        protected override void Use(RaycastHit hit)
        {
            var t = InteriorAim.Find<TrimPiece>(hit);
            if (t == null) { Game.Toast("Aim at the trim at an inside corner"); return; }
            if (!CopeEnd(t, Vector3.Distance(hit.point, t.StartPoint) < Vector3.Distance(hit.point, t.EndPoint)))
                Game.Toast("Nothing to cope there — inside corners are coped automatically when you own this saw");
        }

        /// <summary>Re-cuts an inside-mitered end as a cope (its mate goes back to a square butt). Scripted use too.</summary>
        public static bool CopeEnd(TrimPiece t, bool start)
        {
            if ((start ? t.StartCut : t.EndCut) != TrimCut.InsideMiter) return false;
            Vector3 corner = start ? t.StartPoint : t.EndPoint;
            foreach (var m in TrimPiece.All)
            {
                if (m == null || m == t || m.Kind != t.Kind) continue;
                if (Vector3.Distance(m.StartPoint, corner) < Units.In(1.5f) && m.StartCut == TrimCut.InsideMiter) m.SetCut(true, TrimCut.Square);
                if (Vector3.Distance(m.EndPoint, corner) < Units.In(1.5f) && m.EndCut == TrimCut.InsideMiter) m.SetCut(false, TrimCut.Square);
            }
            t.SetCut(start, TrimCut.Cope);
            Game.Toast("Coped the inside corner");
            return true;
        }
    }

    /// <summary>Pneumatic flooring nailer (2" cleats): installs solid hardwood strip in the aimed room.</summary>
    public class FlooringNailer : CloseupTool
    {
        public override string Id => "flooring-nailer";
        public override string Name => "Pneumatic Flooring Nailer";
        public override int Slots => 2;
        protected override void Use(RaycastHit hit)
        {
            var fp = InteriorAim.FloorAt(hit);
            if (fp == null) { Game.Toast("Aim at the subfloor"); return; }
            Hardwood.Install(fp, Flooring.FindRoom(fp, hit.point));
        }
    }

    /// <summary>LVP tapping block / pull bar: installs click LVP in the aimed room.</summary>
    public class PullBar : CloseupTool
    {
        public override string Id => "pull-bar";
        public override string Name => "LVP Tapping Block & Pull Bar";
        public override string[] Modes => new[] { "Natural oak LVP", "Weathered gray LVP" };
        protected override void Use(RaycastHit hit)
        {
            var fp = InteriorAim.FloorAt(hit);
            if (fp == null) { Game.Toast("Aim at the subfloor"); return; }
            Lvp.Install(fp, Flooring.FindRoom(fp, hit.point), null, Mode == 0 ? "oak" : "gray");
        }
    }

    /// <summary>1/4" x 3/8" square-notch trowel: spreads thinset on cement backer.</summary>
    public class NotchedTrowel : CloseupTool
    {
        public override string Id => "notched-trowel";
        public override string Name => "Notched Trowel 1/4 x 3/8";
        protected override void Use(RaycastHit hit)
        {
            var t = InteriorAim.Find<TileFloor>(hit);
            if (t == null) { Game.Toast("Screw down cement backer for the tile area first (drill)"); return; }
            TileWork.Trowel(t);
        }
    }

    /// <summary>Wet tile saw: cuts and sets tile into fresh thinset; on a countertop, tiles the subway backsplash.</summary>
    public class TileSaw : CloseupTool
    {
        public override string Id => "tile-saw";
        public override string Name => "Wet Tile Saw";
        public override int Slots => 2;
        protected override void Use(RaycastHit hit)
        {
            var ct = InteriorAim.Find<Countertop>(hit);
            if (ct != null) { Countertops.InstallBacksplash(ct); return; }
            var t = InteriorAim.Find<TileFloor>(hit);
            if (t == null) { Game.Toast("Aim at troweled thinset (or a countertop for the backsplash)"); return; }
            TileWork.Set(t);
        }
    }

    /// <summary>Rubber grout float: grout (choose color with RMB) then sponge off the haze.</summary>
    public class GroutFloat : CloseupTool
    {
        public override string Id => "grout-float";
        public override string Name => "Grout Float & Sponge";
        public override string[] Modes => new[] { "Grout: Delorean gray", "Grout: Bright white", "Grout: Charcoal", "Grout: Sand", "Sponge off haze" };
        protected override void Use(RaycastHit hit)
        {
            var t = InteriorAim.Find<TileFloor>(hit);
            if (t == null) { Game.Toast("Aim at set tile"); return; }
            if (Mode == 4) TileWork.WipeHaze(t); else TileWork.Grout(t, ModeName.Substring("Grout: ".Length));
        }
    }

    /// <summary>Cordless drill/driver: hangs cabinets (screws through the hang rail into studs), sets countertops, screws
    /// cement backer. Shift+RMB changes the cabinet width.</summary>
    public class FinDrill : CloseupTool
    {
        public override string Id => "fin-drill";
        public override string Name => "Cordless Drill/Driver";
        public override bool Cordless => true;
        public override string[] Modes => new[] { "Base cabinet", "Wall cabinet", "Granite countertop", "Cement backer (tile)" };
        int _w = 6;
        public override string Hint => $"[{ModeName}{(Mode < 2 ? $" {InteriorSupplies.CabinetWidths[_w]}\"" : "")}] LMB · RMB mode · Shift+RMB width";

        public override void Secondary()
        {
            if (GInput.Shift) { _w = (_w + 1) % InteriorSupplies.CabinetWidths.Length; Game.Toast($"Cabinet width {InteriorSupplies.CabinetWidths[_w]}\""); return; }
            base.Secondary();
        }

        protected override void Use(RaycastHit hit)
        {
            Battery = Mathf.Max(0f, Battery - 0.01f);
            int w = InteriorSupplies.CabinetWidths[_w];
            if (Mode <= 1)
            {
                var fp = FinishAim.FaceBehind(hit);
                if (fp == null || !fp.IsWall) { Game.Toast("Aim at the wall where the cabinet goes"); return; }
                float u = fp.ToUV(hit.point).x;
                // butt against a neighbouring cabinet within 3"
                foreach (var c in CabinetUnit.All)
                {
                    if (c == null || c.IsWall != (Mode == 1) || c.Face == null || !c.Face.SameFace(fp)) continue;
                    float cu = fp.ToUV(c.transform.position).x; // cabinet origin = its +U end
                    if (Mathf.Abs(cu - u) < Units.In(3f)) u = cu;
                }
                Cabinets.InstallRun(fp, u, Mode == 0 ? new[] { w } : new int[0], Mode == 1 ? new[] { w } : new int[0]);
            }
            else if (Mode == 2)
            {
                var c = InteriorAim.Find<CabinetUnit>(hit);
                if (c == null || c.IsWall) { Game.Toast("Aim at a base cabinet"); return; }
                var run = new List<CabinetUnit>();
                foreach (var o in CabinetUnit.All) if (o != null && !o.IsWall && o.Face != null && o.Face.SameFace(c.Face)) run.Add(o);
                Countertops.Install(run, CounterType.Granite);
            }
            else
            {
                var fp = InteriorAim.FloorAt(hit);
                if (fp == null) { Game.Toast("Aim at the subfloor"); return; }
                var t = TileWork.Layout(fp, Flooring.FindRoom(fp, hit.point));
                if (t != null && !TileWork.Backer(t)) Object.Destroy(t.gameObject);
            }
        }
    }

    /// <summary>Tool shop registrations + the painter's caulk tube for the shared caulk gun.</summary>
    public static class InteriorToolRegistry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            ToolFactory.Register(new ToolShopItem { Id = "finish-nailer", Name = "15 ga Finish Nailer", Price = 189, Description = "Angled 15 ga finish nailer: hang prehung doors, nail casing/base/crown into framing." }, () => new FinishNailer());
            ToolFactory.Register(new ToolShopItem { Id = "brad-nailer", Name = "18 ga Brad Nailer", Price = 99, Description = "Brads for casing-to-jamb and small trim." }, () => new BradNailer());
            ToolFactory.Register(new ToolShopItem { Id = "miter-saw", Name = "10\" Compound Miter Saw", Price = 299, Description = "Cuts trim to length, miters and bevels. Required for casing, base and crown. 2 slots." }, () => new MiterSaw());
            ToolFactory.Register(new ToolShopItem { Id = "coping-saw", Name = "Coping Saw", Price = 15, Description = "Cope inside corners of base and crown (mitered inside corners open up)." }, () => new CopingSaw());
            ToolFactory.Register(new ToolShopItem { Id = "flooring-nailer", Name = "Pneumatic Flooring Nailer", Price = 349, Description = "Blind-nails 3/4\" hardwood through the tongue with 2\" cleats. 2 slots." }, () => new FlooringNailer());
            ToolFactory.Register(new ToolShopItem { Id = "tile-saw", Name = "Wet Tile Saw", Price = 399, Description = "Cuts porcelain/ceramic tile; sets floor tile and backsplash. 2 slots." }, () => new TileSaw());
            ToolFactory.Register(new ToolShopItem { Id = "notched-trowel", Name = "Notched Trowel 1/4 x 3/8", Price = 12, Description = "Square-notch trowel for thinset under 12x24 tile." }, () => new NotchedTrowel());
            ToolFactory.Register(new ToolShopItem { Id = "grout-float", Name = "Grout Float & Sponge", Price = 14, Description = "Rubber float to pack grout, sponge to wipe the haze." }, () => new GroutFloat());
            ToolFactory.Register(new ToolShopItem { Id = "pull-bar", Name = "LVP Tapping Block & Pull Bar", Price = 18, Description = "Taps click-lock planks tight; pull bar for the last board at the wall." }, () => new PullBar());
            ToolFactory.Register(new ToolShopItem { Id = "fin-drill", Name = "Cordless Drill/Driver", Price = 129, Description = "Hang cabinets into studs, set countertops, screw cement backer." }, () => new FinDrill());

            CaulkGun.RegisterTube(new CaulkGun.Tube
            {
                Name = "Painter's caulk (white)", StockId = "fin-caulk-painters", BeadColor = new Color(0.95f, 0.95f, 0.93f),
                Apply = (hit, gun) =>
                {
                    var t = hit.collider != null ? hit.collider.GetComponentInParent<TrimPiece>() : null;
                    if (t == null) { Game.Toast("Aim at painted trim (base, crown, casing)"); return false; }
                    if (t.Caulked) { Game.Toast("Already caulked"); return false; }
                    if (!t.Nailed) { Game.Toast("Nail it off before caulking"); return false; }
                    if (!gun.Consume(t.Length * (t.Kind == TrimKind.Crown ? 2f : 1f))) return false;
                    t.Caulk();
                    Game.Toast("Caulked the joint and filled the nail holes");
                    return true;
                }
            });
        }
    }
}
