using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Shared aiming helpers for insulation tools.</summary>
    static class InsAim
    {
        /// <summary>The framed face and bay under the aim (looks through finish layers like batts).</summary>
        public static bool Bay(RaycastHit hit, out FramePlane fp, out FramePlane.Bay bay, out Vector2 uv)
        {
            bay = default; uv = default;
            fp = FaceAim.Front(hit);
            if (fp == null)
            {
                var b = hit.collider.GetComponentInParent<Batt>();
                if (b != null) fp = b.Fp;
                var f = hit.collider.GetComponentInParent<FoamFill>();
                if (f != null) fp = f.Fp;
            }
            if (fp == null) return false;
            uv = fp.ToUV(hit.point);
            return fp.BayAt(uv, out bay);
        }
    }

    /// <summary>Utility knife: fits batts (score against a straightedge to the bay height/width), splits them around
    /// boxes and pipes. RMB cycles the batt product.</summary>
    public class UtilityKnife : CloseupTool
    {
        public override string Id => "utility-knife";
        public override string Name => "Utility Knife";
        public override float Range => 3f;
        static string[] _modes;
        public override string[] Modes { get { if (_modes == null) { _modes = new string[Insulation.Products.Count]; for (int i = 0; i < _modes.Length; i++) _modes[i] = Insulation.Products[i].Short; } return _modes; } }
        public BattProduct Product => Insulation.Products[Mathf.Clamp(Mode, 0, Insulation.Products.Count - 1)];
        public override string Hint => $"[{Product.Short} · {Game.Stock.Count(Product.Id)} on site] LMB fit batt in bay / split it around boxes & pipes · RMB batt type";

        protected override void Use(RaycastHit hit)
        {
            var batt = hit.collider.GetComponentInParent<Batt>();
            if (batt != null)
            {
                if (batt.Compressed) { Insulation.SplitAround(batt); Game.Toast("Batt split around the obstacles — no compression"); }
                else Game.Toast($"{batt.P.Short} fit · R-{batt.EffectiveR:0}{(batt.P.Faced && !batt.Stapled ? " · needs stapling" : "")}");
                return;
            }
            if (!InsAim.Bay(hit, out var fp, out var bay, out _)) { Game.Toast("Aim into an open stud or joist bay"); return; }
            var b = Insulation.FitBatt(fp, bay, Product.Id, false);
            if (b == null) return;
            ToolUtil.Wear(this, 0.001f);
            if (b.Compressed) Game.Toast($"Batt cut to {Units.Format(bay.R.height)} — it's jammed over a box/pipe: click it again to split around it");
            else Game.Toast($"Batt cut to {Units.Format(bay.R.height)} and pushed in{(b.P.Faced ? " — staple the flanges" : "")}");
        }
    }

    /// <summary>T50 staple gun: kraft flanges (face / inset), eave baffles, poly vapor retarder, attic depth rulers.</summary>
    public class StapleGun : CloseupTool
    {
        public override string Id => "staple-gun";
        public override string Name => "Staple Gun (T50)";
        public override float Range => 3f;
        static readonly string[] _m = { "Staple flanges (face)", "Staple flanges (inset)", "Eave baffle", "Poly vapor retarder", "Attic depth ruler" };
        public override string[] Modes => _m;

        protected override void Use(RaycastHit hit)
        {
            switch (Mode)
            {
                case 0:
                case 1:
                {
                    var b = hit.collider.GetComponentInParent<Batt>();
                    if (b == null) { Game.Toast("Aim at a kraft-faced batt"); return; }
                    if (Insulation.Staple(b, Mode == 1)) Game.Toast($"Flanges stapled every {InsulationRules.StapleSpacingIn:0}\"");
                    break;
                }
                case 2:
                {
                    var fp = FaceAim.Front(hit);
                    var cf = Insulation.CeilingFace(fp);
                    if (cf == null) { Game.Toast("Aim at ceiling joists at an exterior wall"); return; }
                    var eaves = Insulation.EaveBays(cf, InsulationInspection.ExteriorWalls());
                    (FramePlane.Bay bay, Vector3 end) best = default; float bd = 1.5f; bool found = false;
                    foreach (var e in eaves) { float d = (e.end - hit.point).magnitude; if (d < bd && !Insulation.HasBaffle(e.end)) { bd = d; best = e; found = true; } }
                    if (!found) { Game.Toast("No eave bay without a baffle near the aim (aim near the exterior wall)"); return; }
                    if (Insulation.InstallBaffle(cf, best.bay, best.end) != null) Game.Toast("Eave baffle stapled — keeps the soffit vent open");
                    break;
                }
                case 3:
                {
                    var fp = FaceAim.Front(hit);
                    if (fp == null || !fp.IsWall) { Game.Toast("Aim at an insulated wall"); return; }
                    if (Insulation.InstallPoly(fp) != null) Game.Toast("6-mil poly stapled over the wall, seams lapped 6\"");
                    break;
                }
                case 4:
                {
                    var fp = Insulation.CeilingFace(FaceAim.Front(hit));
                    if (fp == null) { Game.Toast("Aim at the attic joists"); return; }
                    int n = Insulation.InstallRulers(fp);
                    Game.Toast(n > 0 ? $"{n} depth ruler(s) stapled to the joists" : "Enough rulers here already");
                    break;
                }
            }
            ToolUtil.Wear(this, 0.0005f);
        }
    }

    /// <summary>Can-foam gun: gaps & cracks foam around boxes / penetrations, low-expansion foam at window/door shims.</summary>
    public class FoamGun : CloseupTool
    {
        public override string Id => "foam-gun";
        public override string Name => "Foam Gun";
        public override float Range => 3f;
        static readonly string[] _m = { "Seal boxes & penetrations", "Window/door shim gaps (low-expansion)" };
        public override string[] Modes => _m;

        protected override void Use(RaycastHit hit)
        {
            var fp = FaceAim.Front(hit);
            if (fp == null) { Game.Toast("Aim at framing"); return; }
            if (Mode == 0)
            {
                // nearest unsealed box or penetration
                float bd = 0.6f; FramePlane.Obstacle? box = null; Vector3 pen = default; bool isPen = false;
                foreach (var (c, o) in Insulation.Boxes(fp)) { float d = (c - hit.point).magnitude; if (d < bd && !Insulation.IsBoxSealed(c)) { bd = d; box = o; isPen = false; } }
                foreach (var p in Insulation.Penetrations(fp)) { float d = (p - hit.point).magnitude; if (d < bd && !Insulation.IsFireBlocked(p)) { bd = d; pen = p; isPen = true; } }
                if (isPen) { Game.Toast("Plate penetrations need fire caulk (caulk gun → Fire caulk) — foam isn't a fire block"); return; }
                if (box == null) { Game.Toast("No unsealed box near the aim"); return; }
                if (Insulation.SealBox(fp, box.Value)) Game.Toast("Box sealed with foam");
            }
            else
            {
                var uv = fp.ToUV(hit.point);
                foreach (var r in fp.Openings())
                {
                    var rr = r; rr.xMin -= 0.3f; rr.xMax += 0.3f; rr.yMin -= 0.3f; rr.yMax += 0.3f;
                    if (!rr.Contains(uv)) continue;
                    if (Insulation.FoamShimGaps(fp, r)) Game.Toast("Low-expansion foam in the shim gaps"); else Game.Toast("No window/door in this opening (or already foamed)");
                    return;
                }
                Game.Toast("Aim at a window or door rough opening");
            }
            ToolUtil.Wear(this, 0.001f);
        }
    }

    /// <summary>Two-part closed-cell spray foam rig: each click sprays ~2" into the aimed bay.</summary>
    public class SprayFoamKit : CloseupTool
    {
        public override string Id => "spray-foam-kit";
        public override string Name => "Spray Foam Kit";
        public override int Slots => 2;
        public override float Range => 3f;
        public override string Hint => $"LMB spray ~2\" closed-cell foam into the bay (R-{2f * InsulationRules.ClosedCellRPerInch:0} per pass) · refills on site: {Game.Stock.Count(Insulation.FoamKitId)} bd-ft";

        protected override void Use(RaycastHit hit)
        {
            if (!InsAim.Bay(hit, out var fp, out var bay, out _)) { Game.Toast("Aim into a bay"); return; }
            var f = Insulation.SprayFoam(fp, bay);
            if (f != null) Game.Toast($"Closed-cell foam {Units.Format(f.Thick)} thick · R-{f.EffectiveR:0}");
            ToolUtil.Wear(this, 0.002f);
        }
    }

    /// <summary>Rental blowing machine: HOLD on the attic (joist tops from above, or the hung ceiling from below with the
    /// hose through the hatch) to blow loose fill. Shows the depth / R at the aim.</summary>
    public class InsulationBlower : CloseupTool
    {
        public override string Id => "insulation-blower";
        public override string Name => "Insulation Blower";
        public override int Slots => 2;
        public override float Range => 8f;
        string _status = "";
        public override string Hint => $"HOLD LMB to blow loose fill (target R-{InsulationRules.AtticMinR:0} = {InsulationRules.AtticMinR / InsulationRules.LooseFillRPerInch:0.0}\") · bags on site {Game.Stock.Count(Insulation.LooseFillId)} {_status}";

        protected override void Use(RaycastHit hit) { Blow(hit, 0.1f); }

        public override void PrimaryHeld()
        {
            if (Game.UIOpen) return;
            if (FinishAim.Aim(Range, out var hit)) Blow(hit, Time.deltaTime);
        }

        Collider _lastCol; FramePlane _lastFace;

        void Blow(RaycastHit hit, float dt)
        {
            FramePlane cf;
            var af0 = hit.collider.GetComponentInParent<AtticFill>();
            if (af0 != null) cf = af0.Fp;
            else if (hit.collider == _lastCol && _lastFace != null) cf = _lastFace;
            else { cf = Insulation.CeilingFace(FaceAim.Front(hit)); _lastCol = hit.collider; _lastFace = cf; }
            if (cf == null) { _status = "· aim at the attic"; return; }
            if (!AtticFill.CeilingBelow(cf)) { Game.Toast("Hang the ceiling drywall first — loose fill rests on it"); return; }
            var af = Insulation.AtticFor(cf, true);
            if (!af.Blow(hit.point, dt)) return;
            var uv = cf.ToUV(hit.point);
            _status = $"· avg {Units.Format(af.AverageDepth)} = R-{Units.ToInches(af.AverageDepth) * InsulationRules.LooseFillRPerInch:0}";
            ToolUtil.Wear(this, 0.00002f);
        }
    }
}
