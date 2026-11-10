using UnityEngine;

namespace Groundbreak
{
    /// <summary>Chalk line reel: click two points on a surface to snap a blue line (stretched taut, plucked).</summary>
    public class ChalkLineTool : Tool
    {
        public override string Id => "chalkline";
        public override string Name => "Chalk Line";
        public override string Hint => _hasA ? "LMB: second point to snap (" + Units.Format(_len) + ") · RMB: cancel" : "LMB: hook the line on a point · RMB: cancel";
        bool _hasA; Vector3 _a, _n; float _len;

        public override void Primary()
        {
            if (Game.Player == null || !Game.Player.TryAim(out var hit, 12f)) return;
            Vector3 p = Game.Snap.SnapPosition(hit.point);
            p += hit.normal * Vector3.Dot(hit.point - p, hit.normal); // stay on the surface
            if (!_hasA) { _hasA = true; _a = p; _n = hit.normal; Game.Toast("Line hooked - walk the reel to the other end"); return; }
            ChalkLine.SnapWorld(_a, p, 0.12f, _n);
            Game.Toast("Snapped a chalk line " + Units.Format(Vector3.Distance(_a, p)));
            _hasA = false;
        }
        public override void Secondary() { _hasA = false; }
        public override void Tick()
        {
            if (_hasA && Game.Player != null && Game.Player.TryAim(out var hit, 12f)) _len = Vector3.Distance(_a, hit.point);
        }
    }

    /// <summary>Speed square: reads the angle / pitch of aimed lumber, and whether a member passes structural checks.</summary>
    public class SpeedSquareTool : Tool
    {
        public override string Id => "speedsquare";
        public override string Name => "Speed Square";
        public override string Hint => "Aim at lumber: pitch / plumb readout · LMB: check member (supports, spacing)";
        string _read = "";

        public override void Tick()
        {
            _read = "";
            var p = ToolUtil.AimPiece(4f, out _);
            if (p == null) return;
            Vector3 len = p.transform.forward;
            float deg = Mathf.Asin(Mathf.Clamp(Mathf.Abs(len.y), 0f, 1f)) * Mathf.Rad2Deg;
            float rise = Mathf.Tan(deg * Mathf.Deg2Rad) * 12f;
            _read = deg > 89.5f ? "Plumb (90°)" : deg < 0.5f ? "Level (0°)" : $"{deg:0.0}°  =  {rise:0.##}/12 pitch";
        }

        public override void Primary()
        {
            var p = ToolUtil.AimPiece(4f, out _);
            var m = p != null ? p.GetComponent<StructuralMember>() : null;
            if (m == null) { Game.Toast("Not a framing member"); return; }
            SupportGraph.Rebuild();
            var st = SupportGraph.Evaluate(m, SupportGraph.Grounded());
            Game.Toast(m.Role + ": " + (st == StructuralStatus.Ok ? "OK - bears on " + m.SupportedBy.Count + " member(s)" + (m.OnFoundation ? " + foundation" : "") : string.Join("; ", m.Notes)));
        }

        public override void DrawGUI() { if (_read != "") ToolUtil.CenterLabel(_read, new Color(0.9f, 0.95f, 1f), 30f); }
    }

    public static class FramingTools
    {
        public static void RegisterAll()
        {
            ToolFactory.Register(new ToolShopItem { Id = "chalkline", Name = "Chalk Line", Price = 15, Description = "Snap straight layout lines on decks, plates and roofs." }, () => new ChalkLineTool());
            ToolFactory.Register(new ToolShopItem { Id = "speedsquare", Name = "Speed Square", Price = 12, Description = "Mark square cuts, read pitch, check a member's support." }, () => new SpeedSquareTool());

            // Structural checks on the placement ghost (red = unsupported, yellow = will fail inspection) and product snapping.
            PlacementController.ExternalCheck = (def, pos, rot, size) =>
            {
                var prod = RoughOpening.CheckProduct(def, pos);
                if (prod.level != 0 || (def != null && def.IsProduct)) return prod;
                var st = SupportGraph.CheckGhost(def, pos, rot, size, out string why);
                return (st == StructuralStatus.Unsupported ? 2 : st == StructuralStatus.Warning ? 1 : 0, why);
            };
            PlacementController.PiecePlaced = p =>
            {
                if (p == null) return;
                RoughOpening.OnPlaced(p);
                if (p.GetComponent<StructuralMember>() == null)
                {
                    var m = p.gameObject.AddComponent<StructuralMember>();
                    m.Role = StructuralRules.Classify(p.Def, p.transform.rotation, p.Size);
                }
                SupportGraph.Dirty = true;
            };

            Tablet.AddHelp("Framing: aim at the blue layout ghost of a job and click with the tool it needs (nailer/hammer, chalk line, level, saw...)");
            Tablet.AddHelp("I - run the framing inspection (red = structural, yellow = correction; aim at a flag to read it)");
            ShellApp.Register();
        }
    }
}
