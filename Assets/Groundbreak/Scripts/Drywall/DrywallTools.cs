using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    static class DwReach
    {
        /// <summary>Hands can reach this world point (stilts add 18")?</summary>
        public static bool Ok(Vector3 p)
        {
            if (Game.Player == null) return true;
            float eye = Game.Player.transform.position.y + PlayerRig.EyeHeight + (Stilts.Worn ? Stilts.Lift : 0f);
            if (p.y <= eye + DrywallRules.ReachAboveEye) return true;
            Game.Toast("Too high to reach — put on the drywall stilts");
            return false;
        }

        public static string[] ProductNames()
        {
            var l = new string[Drywall.Products.Count];
            for (int i = 0; i < l.Length; i++) l[i] = Drywall.Products[i].Short;
            return l;
        }
    }

    /// <summary>Drywall T-square: measure, score &amp; snap (utility knife) and hang the next wall sheet of the layout
    /// (top row first, horizontal, butt ends on stud centers, openings cut out). RMB = sheet product.</summary>
    public class DrywallTSquare : FinishTool
    {
        public override string Id => "drywall-tsquare";
        public override string Name => "Drywall T-Square";
        public override float Range => 4f;
        static string[] _m;
        public override string[] Modes => _m ?? (_m = DwReach.ProductNames());
        public DrywallProduct Product => Drywall.Products[Mathf.Clamp(Mode, 0, Drywall.Products.Count - 1)];
        string _next = "";
        public override string Hint => $"[{Product.Short} · {Game.Stock.Count(Product.Id)} on site] LMB measure, cut & hang the next wall sheet {_next} · RMB sheet";

        public override void Tick()
        {
            if (Time.frameCount % 15 != 0 || !FinishAim.Aim(Range, out var hit)) return;
            var fp = FaceAim.Front(hit);
            _next = fp != null && fp.IsWall && Drywall.NextPlan(fp, Product, out var pl, out _) ? $"(row {pl.Row + 1}: {Units.Format(pl.R.width)} x {Units.Format(pl.R.height)})" : "";
        }

        protected override void Use(RaycastHit hit)
        {
            var fp = FaceAim.Front(hit);
            if (fp == null) { Game.Toast("Aim at the framing to cover"); return; }
            if (!fp.IsWall) { Game.Toast("Ceiling sheets go up with the drywall lift"); return; }
            if (!FinishStock.Require("utility-knife", "score and snap drywall")) return;
            if (!Drywall.NextPlan(fp, Product, out var pl, out var why)) { Game.Toast(why); return; }
            var sh = Drywall.HangSheet(fp, Product, pl);
            if (sh == null) return;
            ToolUtil.Wear(this, 0.0005f);
            string covered = sh.Covered.Count > 0 ? $" — {sh.Covered.Count} box(es) behind it: cut them out" : "";
            Game.Toast($"Hung {Product.Short} cut to {Units.Format(pl.R.width)} x {Units.Format(pl.R.height)} (row {pl.Row + 1}){covered}");
        }
    }

    /// <summary>Panel lift: raises ceiling sheets (perpendicular to the joists, ends on joist centers).</summary>
    public class DrywallLift : FinishTool
    {
        public override string Id => "drywall-lift";
        public override string Name => "Drywall Panel Lift";
        public override int Slots => 2;
        public override float Range => 5f;
        static string[] _m;
        public override string[] Modes => _m ?? (_m = DwReach.ProductNames());
        public DrywallProduct Product => Drywall.Products[Mathf.Clamp(Mode, 0, Drywall.Products.Count - 1)];
        public override string Hint => $"[{Product.Short} · {Game.Stock.Count(Product.Id)} on site] LMB crank the next ceiling sheet up · RMB sheet";

        protected override void Use(RaycastHit hit)
        {
            var fp = FaceAim.Front(hit);
            if (fp == null || !fp.IsCeiling) { Game.Toast("Aim at the ceiling joists"); return; }
            if (!FinishStock.Require("utility-knife", "score and snap drywall")) return;
            if (!Drywall.NextPlan(fp, Product, out var pl, out var why)) { Game.Toast(why); return; }
            if (Drywall.HangSheet(fp, Product, pl) != null) Game.Toast($"Ceiling sheet {Units.Format(pl.R.width)} x {Units.Format(pl.R.height)} lifted into place — screw it off");
        }
    }

    /// <summary>Auto-feed screw gun: LMB one screw snapped to the nearest framing line; Shift+LMB runs the whole line.</summary>
    public class ScrewGun : FinishTool
    {
        public override string Id => "screw-gun";
        public override string Name => "Drywall Screw Gun";
        public override bool Cordless => true;
        public override float Range => 3f;
        public override string Hint => $"LMB drive a screw (snaps to stud/joist) · Shift+LMB auto-feed the whole line ({DrywallRules.ScrewSpacingWallIn:0}\" walls / {DrywallRules.ScrewSpacingCeilingIn:0}\" ceilings) · screws {Game.Stock.Count(Drywall.ScrewsId)}";

        protected override void Use(RaycastHit hit)
        {
            var s = Drywall.SurfaceAt(hit);
            if (s == null) { Game.Toast("Aim at a hung sheet"); return; }
            if (!DwReach.Ok(hit.point)) return;
            var uv = s.Fp.ToUV(hit.point);
            if (GInput.Shift)
            {
                var sh = s.SheetAt(uv); if (sh == null) return;
                var lines = Drywall.ScrewLines(sh); if (lines.Count == 0) { Game.Toast("No framing behind this sheet?"); return; }
                float bu = lines[0]; foreach (var l in lines) if (Mathf.Abs(l - uv.x) < Mathf.Abs(bu - uv.x)) bu = l;
                int n = Drywall.ScrewLine(sh, bu);
                Game.Toast(n > 0 ? $"{n} screws down the line" : "That line is done");
                Battery = Mathf.Max(0f, Battery - n * 0.0008f);
            }
            else if (Drywall.DriveScrew(s, uv) != null) Battery = Mathf.Max(0f, Battery - 0.0008f);
            ToolUtil.Wear(this, 0.0002f);
        }
    }

    /// <summary>Cut-out router: one pass around a covered box.</summary>
    public class DrywallRouter : FinishTool
    {
        public override string Id => "drywall-router";
        public override string Name => "Cut-out Router";
        public override float Range => 3f;
        public override string Hint => "LMB route out the box behind the sheet (one pass)";

        protected override void Use(RaycastHit hit)
        {
            var s = Drywall.SurfaceAt(hit);
            if (s == null) { Game.Toast("Aim at a hung sheet over a box"); return; }
            if (!Drywall.CoveredNear(s, s.Fp.ToUV(hit.point), 0.45f, out var sh, out var box)) { Game.Toast("No covered box here"); return; }
            Drywall.CutOut(sh, box);
            Game.Toast("Box routed out");
            ToolUtil.Wear(this, 0.002f);
        }
    }

    /// <summary>Keyhole (jab) saw: three passes per box.</summary>
    public class KeyholeSaw : FinishTool
    {
        public override string Id => "keyhole-saw";
        public override string Name => "Keyhole Saw";
        public override float Range => 2.5f;
        readonly Dictionary<string, int> _passes = new Dictionary<string, int>();
        public override string Hint => "LMB saw the box cut-out (3 passes)";

        protected override void Use(RaycastHit hit)
        {
            var s = Drywall.SurfaceAt(hit);
            if (s == null) { Game.Toast("Aim at a hung sheet over a box"); return; }
            if (!Drywall.CoveredNear(s, s.Fp.ToUV(hit.point), 0.45f, out var sh, out var box)) { Game.Toast("No covered box here"); return; }
            string key = s.name + s.Fp.FaceOffset.ToString("F3") + ":" + box.center.ToString("F3");
            _passes.TryGetValue(key, out int p); p++;
            if (p < 3) { _passes[key] = p; Game.Toast($"Sawing… pass {p}/3"); return; }
            _passes.Remove(key);
            Drywall.CutOut(sh, box);
            Game.Toast("Box cut out");
            ToolUtil.Wear(this, 0.004f);
        }
    }

    /// <summary>Taping knives 6/10/12" + mud pan. Click a joint to apply its next coat (right knife required); click
    /// away from joints to spot screws. Also sets corner bead and skims (level 5).</summary>
    public class TapingKnives : FinishTool
    {
        public override string Id => "taping-knives";
        public override string Name => "Taping Knives";
        public override float Range => 3f;
        static readonly string[] _m = { "6\" knife · paper tape (all-purpose)", "6\" knife · mesh tape (setting compound)", "10\" knife · coat 2", "12\" knife · coat 3", "Set corner bead (metal)", "12\" knife · skim coat (level 5)" };
        public override string[] Modes => _m;
        public float KnifeIn => Mode <= 1 || Mode == 4 ? 6f : Mode == 2 ? 10f : 12f;

        protected override void Use(RaycastHit hit)
        {
            var s = Drywall.SurfaceAt(hit);
            if (s == null) { Game.Toast("Aim at hung drywall"); return; }
            s.Refresh();
            var uv = s.Fp.ToUV(hit.point);
            if (Mode == 5)
            {
                if (Drywall.ApplyTexture(s, "smooth")) Game.Toast($"Skim coat (level 5) — dry {DrywallRules.SkimDryHours:0} h before priming");
                return;
            }
            if (!Nearest(s, uv, Mode == 4, out var seam, out var seg))
            {
                if (Mode == 4) { Game.Toast("No outside corner here"); return; }
                int n = Drywall.SpotScrews(s, uv, 0.6f, out var w);
                Game.Toast(n > 0 ? $"Spotted {n} screw heads" : w ?? "Aim at a joint, or near screws to spot them");
                return;
            }
            if (!DwReach.Ok(s.FrontWorld(seam.At((seg.A + seg.B) * 0.5f)))) return;
            if (Mode == 4)
            {
                if (Drywall.InstallBead(s, seam, false) != null) Game.Toast("Corner bead set — mud it like a joint");
                else Game.Toast("Already beaded");
                return;
            }
            if (Drywall.Coat(s, seam, seg, KnifeIn, Mode == 1, Mode == 1 ? "dw-mud-set45" : "dw-mud-ap", out var why))
            {
                Game.Toast(seg.Coats == 1 ? $"Tape embedded with the 6\" knife ({(seg.Mesh ? "mesh" : "paper")})" : $"Coat {seg.Coats} feathered with the {KnifeIn:0}\" knife");
                Drywall.SpotScrews(s, uv, 0.45f, out _);
                ToolUtil.Wear(this, 0.0005f);
            }
            else Game.Toast(why);
        }

        static bool Nearest(DrywallSurface s, Vector2 uv, bool outsideOnly, out Seam best, out SeamSeg bestSeg)
        {
            best = null; bestSeg = null; float bd = Units.In(5f);
            foreach (var seam in s.Seams)
            {
                if (outsideOnly && seam.Kind != SeamKind.OutsideCorner) continue;
                Vector2 dir = (seam.P1 - seam.P0).normalized;
                float t = Vector2.Dot(uv - seam.P0, dir);
                if (t < -0.05f || t > seam.Length + 0.05f) continue;
                float d = Mathf.Abs(Vector2.Dot(uv - seam.P0, new Vector2(-dir.y, dir.x)));
                if (seam.IsCorner) d = Mathf.Max(0f, d - Units.In(2f));
                if (d >= bd) continue;
                foreach (var g in seam.Segs) if (t >= g.A - 0.05f && t <= g.B + 0.05f) { bd = d; best = seam; bestSeg = g; break; }
            }
            return best != null;
        }
    }

    /// <summary>Pole sander (flats) and sanding sponge (corners).</summary>
    public class SandingPole : FinishTool
    {
        public override string Id => "sanding-pole";
        public override string Name => "Sanding Pole";
        public override float Range => 4f;
        static readonly string[] _m = { "Pole sander (flats)", "Sanding sponge (corners)" };
        public override string[] Modes => _m;

        protected override void Use(RaycastHit hit)
        {
            var s = Drywall.SurfaceAt(hit);
            if (s == null) { Game.Toast("Aim at finished joints"); return; }
            int n = Drywall.Sand(s, s.Fp.ToUV(hit.point), 0.8f, Mode == 1, out var why);
            Game.Toast(n > 0 ? $"Sanded {n} spot(s) smooth" : why ?? "Nothing to sand here");
            ToolUtil.Wear(this, 0.001f);
        }
    }

    /// <summary>Drywall stilts: LMB put on / take off. While worn the camera and reach are 18" higher.</summary>
    public class Stilts : FinishTool
    {
        public const float Lift = 0.4572f; // 18"
        public static bool Worn;
        public override string Id => "stilts";
        public override string Name => "Drywall Stilts";
        public override string Hint => Worn ? "LMB take the stilts off (you're 18\" taller)" : "LMB strap on the stilts (+18\" reach for ceilings and high walls)";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() { Worn = false; }

        public override void Primary() { Toggle(); }
        protected override void Use(RaycastHit hit) { Toggle(); }

        public static void Toggle() { SetWorn(!Worn); }

        public static void SetWorn(bool on)
        {
            Worn = on;
            if (Game.Player != null && Game.Player.GetComponent<StiltsRig>() == null) Game.Player.gameObject.AddComponent<StiltsRig>();
            Game.Toast(on ? "Stilts on — watch your step" : "Stilts off");
        }
    }

    /// <summary>Raises the player camera while stilts are worn (runs after PlayerRig sets the camera each Update).</summary>
    [DefaultExecutionOrder(10000)]
    public class StiltsRig : MonoBehaviour
    {
        PlayerRig _rig;
        void Awake() { _rig = GetComponent<PlayerRig>(); }
        void Apply()
        {
            if (_rig == null || _rig.Cam == null || !Stilts.Worn) return;
            var t = _rig.Cam.transform; var p = t.localPosition;
            float baseY = PlayerRig.EyeHeight;
            if (p.y < baseY + Stilts.Lift - 0.01f) t.localPosition = new Vector3(p.x, p.y + Stilts.Lift, p.z);
        }
        void Update() { Apply(); }
        void LateUpdate() { Apply(); }
    }
}
