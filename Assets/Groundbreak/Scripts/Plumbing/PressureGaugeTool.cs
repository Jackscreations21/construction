using UnityEngine;

namespace Groundbreak
{
    /// <summary>Test gauge + hand pump: LMB on a test gauge pumps the supply to 100 psi water (Shift = 60 psi air);
    /// RMB on an open stub or pipe end pushes on a test cap (RMB on a capped stub removes it).</summary>
    public class PressureGaugeTool : Tool
    {
        public override string Id => "test-gauge";
        public override string Name => "Test Gauge + Pump";
        public override string Hint => "LMB on a test gauge: pump 100 psi (Shift = 60 psi air) · RMB on an open stub/pipe end: test cap";

        static PressureGauge AimGauge()
        {
            var p = ToolUtil.AimPiece(MepAim.Range, out var hit);
            if (p != null) { var g = p.GetComponent<PressureGauge>(); if (g != null) return g; }
            PressureGauge best = null; float bd = 0.4f;
            if (hit.collider == null) return null;
            foreach (var g in PressureTest.Gauges()) { float d = Vector3.Distance(g.transform.position, hit.point); if (d < bd) { bd = d; best = g; } }
            return best;
        }

        public override void Primary()
        {
            var g = AimGauge();
            if (g == null) { Game.Toast("Aim at a test gauge (place one on a supply stub first)"); return; }
            PressureTest.Pump(g, GInput.Shift);
            Game.Toast(g.Status);
        }

        public override void Secondary()
        {
            if (!MepAim.Aim(out var hit)) return;
            var port = NetGraph.NearestPort(hit.point, 0.15f, p => p.Node != null && p.Node.Trade == Trade.Plumbing && p.Run == null);
            if (port != null)
            {
                if (port.Capped) { PlumbParts.UncapPort(port); Game.Toast("Test cap removed"); return; }
                if (!TakeCap()) return;
                PlumbParts.CapPort(port); Game.Toast($"Capped {port.Node.Label} {port.Label}"); return;
            }
            var r = NetRun.FromCollider(hit.collider);
            if (r == null || r.Spec.Trade != Trade.Plumbing) return;
            bool nearA = Vector3.Distance(hit.point, r.StartPoint) < Vector3.Distance(hit.point, r.EndPoint);
            if (nearA ? !r.FreeEndA : !r.FreeEndB) { Game.Toast("That end is connected"); return; }
            if (!TakeCap()) return;
            RunBuilder.CapEnd(r, nearA);
            Game.Toast("Test cap on " + r.Spec.Name);
        }

        static bool TakeCap()
        {
            if (MepStock.Free || Game.Stock.TryTake(PlumbCatalog.TestCaps, 1)) return true;
            Game.Toast("Out of test caps (buy a test cap assortment)");
            return false;
        }

        public override void DrawGUI()
        {
            var g = AimGauge();
            if (g != null) ToolUtil.CenterLabel($"Gauge {g.Psi:0} psi · {g.Status}", g.State == PressureGauge.TestState.Failed ? Color.red : g.State == PressureGauge.TestState.Passed ? Color.green : Color.white);
        }
    }
}
