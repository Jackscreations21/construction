using UnityEngine;

namespace Groundbreak
{
    /// <summary>Registers the roofing module: tools, truss products, rule tweaks, tablet help and the ShellProject hooks.</summary>
    public static class RoofModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            ToolFactory.Register(new ToolShopItem { Id = "roofnailer", Name = "Coil Roofing Nailer", Price = 279, Description = "Pneumatic coil nailer, 1-1/4\" roofing nails. Shingles, starter, drip edge, ridge vent. Shows course + nail line." }, () => new RoofingNailer());
            ToolFactory.Register(new ToolShopItem { Id = "hatchet", Name = "Roofing Hatchet", Price = 49, Description = "Shingle hatchet with an adjustable exposure gauge (5-5/8\" architectural / 5\" 3-tab) and a cutting blade." }, () => new RoofingHatchet());
            ToolFactory.Register(new ToolShopItem { Id = "capstapler", Name = "Cap Stapler", Price = 129, Description = "Hammer-tacker style cap stapler for synthetic underlayment: 12\" O.C. on laps, 24\" in the field." }, () => new CapStapler());
            ToolFactory.Register(new ToolShopItem { Id = "harness", Name = "Fall Protection Harness Kit", Price = 119, Description = "Harness + 25' lanyard + rope grab. LMB near a roof anchor to clip in (required above 6/12 without roof jacks)." }, () => new SafetyHarness());

            Catalog.Register(
                new PieceDefinition { Id = "truss-fink", Name = "Engineered Fink truss (24\" O.C., to 32' span)", Category = PieceCategory.Consumable, Price = 128, UnitsPerPurchase = 1, Color = new Color(0.86f, 0.72f, 0.50f) },
                new PieceDefinition { Id = "truss-gable", Name = "Gable-end truss (studs 16\" O.C.)", Category = PieceCategory.Consumable, Price = 168, UnitsPerPurchase = 1, Color = new Color(0.86f, 0.72f, 0.50f) }
            );

            // fly rafters / rake sub-fascia hang on lookouts; lookouts also tie into gable studs
            var sf = StructuralRules.Get(MemberRole.SubFascia);
            if (System.Array.IndexOf(sf.SupportedBy, MemberRole.Lookout) < 0)
            {
                var list = new System.Collections.Generic.List<MemberRole>(sf.SupportedBy) { MemberRole.Lookout, MemberRole.SubFascia, MemberRole.RidgeBoard };
                StructuralRules.Set(new RoleRule { Role = sf.Role, SupportedBy = list.ToArray(), Foundation = sf.Foundation, Requires = sf.Requires, MaxOCInches = sf.MaxOCInches, Allow24OC = sf.Allow24OC, Note = sf.Note });
            }

            Tablet.AddHelp("Roofing: trusses/rafters -> OSB deck -> drip edge, ice & water, underlayment -> starter, shingles, ridge vent, caps.");
            Tablet.AddHelp("Above 6/12 you slip without roof jacks or a harness clipped to a roof anchor (harness LMB near the anchor).");

            ShellProject.RoofFramingHook = p => (p.StickFrame || p.Hip) ? RoofFraming.StickFramed(p.Fp, p.PitchRise, p.Hip) : RoofFraming.Trusses(p.Fp, p.PitchRise);
            ShellProject.RoofSheathingHook = p => RoofFraming.InfoOf(p.Roof) != null ? RoofFraming.RoofSheathing(RoofFraming.InfoOf(p.Roof)) : null;
            ShellProject.DryInHook = p => RoofFraming.InfoOf(p.Roof) != null ? Roofing.DryIn(RoofFraming.InfoOf(p.Roof)) : null;
            ShellProject.ShinglesHook = p => RoofFraming.InfoOf(p.Roof) != null ? Roofing.Shingles(RoofFraming.InfoOf(p.Roof)) : null;
        }

        /// <summary>Roof plane the player is aiming at (hit within 30 cm of a plane's deck surface).</summary>
        public static bool AimRoof(float range, out RoofInfo info, out RoofPlane plane, out Vector3 local)
        {
            info = null; plane = null; local = Vector3.zero;
            if (Game.Player == null || !Game.Player.TryAim(out var hit, range)) return false;
            plane = RoofInfo.FindPlane(hit.point, 0.3f, 0.3f, out info, out local);
            return plane != null;
        }

        /// <summary>Is a framing job's layout ghost being aimed at (then FramingInteraction handles the click)?</summary>
        public static bool JobAimed() => FramingInteraction.I != null && FramingInteraction.I.AimedJob() != null;

        public static string CourseInfo(RoofPlane p, Vector3 local)
        {
            float E = ShingleMesh.Exposure;
            float v0 = ShingleMesh.FirstButt(p);
            int n = Mathf.Max(0, Mathf.FloorToInt((local.y - v0) / E));
            float butt = v0 + n * E;
            return p.Name + " " + p.PitchRise + "/12 - course " + (n + 1) + "/" + ShingleMesh.CourseCount(p) + ", butt " + Units.Format(Mathf.Max(0f, butt)) + " up the slope; nail line 6\" above the butt, 1\" in from each end";
        }
    }

    public class RoofingNailer : Tool
    {
        public override string Id => "roofnailer";
        public override string Name => "Coil Roofing Nailer";
        public override int Slots => 1;
        public override string Hint => "LMB on the blue layout: nail the step (shingles 4 nails each) - aim at the roof for course / nail line info";
        string _info = "";
        float _next;

        public override void Tick()
        {
            _info = RoofModule.AimRoof(5f, out _, out var p, out var l) ? RoofModule.CourseInfo(p, l) : "";
        }

        public override void Primary()
        {
            if (RoofModule.JobAimed() || Time.time < _next) return;   // FramingInteraction performs the step
            _next = Time.time + 0.12f;
            if (_info != "") Game.Toast(_info);
            ToolUtil.Wear(this, 0.0001f);
        }

        public override void DrawGUI() { if (_info != "") ToolUtil.CenterLabel(_info, new Color(0.9f, 0.95f, 1f), 30f); }
    }

    public class RoofingHatchet : Tool
    {
        public override string Id => "hatchet";
        public override string Name => "Roofing Hatchet";
        public float GaugeInches = 5.625f;
        public override string Hint => "LMB: nail / set the next course with the exposure gauge (" + GaugeInches + "\") - RMB: switch gauge 5-5/8\" (architectural) / 5\" (3-tab)";
        string _info = "";

        public override void Tick()
        {
            if (RoofModule.AimRoof(4f, out _, out var p, out var l))
            {
                float E = Units.In(GaugeInches);
                float v0 = ShingleMesh.FirstButt(p);
                int n = Mathf.Max(0, Mathf.FloorToInt((l.y - v0) / E));
                float next = v0 + (n + 1) * E;
                _info = "Gauge " + Units.Format(E) + ": next butt line " + Units.Format(Mathf.Max(0f, next)) + " up the slope" +
                        (Mathf.Abs(GaugeInches - 5.625f) > 0.01f ? "  (architectural shingles need 5-5/8\")" : "");
            }
            else _info = "";
        }

        public override void Primary()
        {
            if (RoofModule.JobAimed()) return;
            if (_info != "") Game.Toast(_info);
        }

        public override void Secondary()
        {
            GaugeInches = Mathf.Abs(GaugeInches - 5.625f) < 0.01f ? 5f : 5.625f;
            Game.Toast("Exposure gauge set to " + Units.Format(Units.In(GaugeInches)));
        }

        public override void DrawGUI() { if (_info != "") ToolUtil.CenterLabel(_info, new Color(1f, 0.9f, 0.6f), 30f); }
    }

    public class CapStapler : Tool
    {
        public override string Id => "capstapler";
        public override string Name => "Cap Stapler";
        public override string Hint => "LMB on the blue layout: roll out + staple an underlayment course (caps 12\" O.C. on laps, 24\" field)";

        public override void Primary()
        {
            if (RoofModule.JobAimed()) return;
            if (RoofModule.AimRoof(4f, out _, out var p, out var l))
                Game.Toast("Underlayment: 4\" horizontal laps, 6\" end laps, cap staples 12\" O.C. on laps / 24\" in the field");
        }
    }

    public class SafetyHarness : Tool
    {
        public override string Id => "harness";
        public override string Name => "Safety Harness";
        public override string Hint => RoofSafety.ClippedIn ? "Clipped in to the roof anchor - RMB: unclip" : "LMB: clip in to a roof anchor within reach (installed at the ridge with the roof deck)";

        public override void Primary()
        {
            if (Game.Player == null) return;
            RoofAnchor a = null;
            if (Game.Player.TryAim(out var hit, 4f)) a = RoofAnchor.Nearest(hit.point, 1.2f);
            if (a == null) a = RoofAnchor.Nearest(Game.Player.transform.position, 3f);
            if (a == null) { Game.Toast("No roof anchor within reach - install one at the ridge"); return; }
            RoofSafety.ClippedIn = true;
            RoofSafety.Anchor = a;
            Game.Toast("Clipped in to the roof anchor (" + Mathf.RoundToInt(RoofSafety.LanyardLength / Units.Foot) + "' lanyard + rope grab)");
        }

        public override void Secondary()
        {
            if (!RoofSafety.ClippedIn) return;
            RoofSafety.ClippedIn = false; RoofSafety.Anchor = null;
            Game.Toast("Unclipped from the roof anchor");
        }

        public override void DrawGUI()
        {
            ToolUtil.CenterLabel(RoofSafety.ClippedIn ? "HARNESS: clipped in" : "HARNESS: not clipped in", RoofSafety.ClippedIn ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.6f, 0.4f), 30f);
        }
    }
}
