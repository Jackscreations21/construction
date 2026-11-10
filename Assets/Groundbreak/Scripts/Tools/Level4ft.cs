using UnityEngine;

namespace Groundbreak
{
    public class Level4ft : Tool
    {
        public override string Id => "level";
        public override string Name => "4ft Level";
        public override string Hint => "Aim at a piece to read level / plumb";

        const float Range = 4f, Tolerance = 0.5f;

        public override void DrawGUI()
        {
            var p = ToolUtil.AimPiece(Range, out _);
            if (p == null) return;
            Vector3 axis = p.transform.forward; // long axis = local Z
            float elev = Mathf.Asin(Mathf.Clamp01(Mathf.Abs(axis.y))) * Mathf.Rad2Deg; // 0 = level, 90 = plumb
            bool plumb = elev > 45f;
            float off = plumb ? 90f - elev : elev;
            bool ok = off <= Tolerance;
            ToolUtil.CenterLabel((plumb ? "PLUMB  " : "LEVEL  ") + off.ToString("0.0") + "° off", ok ? Color.green : new Color(1f, 0.4f, 0.3f));
        }
    }
}
