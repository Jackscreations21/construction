using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Finds the framed face the player means. Aiming into an open stud bay hits the sheathing at the back of the
    /// cavity (whose own "face" is useless), so this probes a few parallel rays around the aim and keeps the framing
    /// face closest to the viewer (the stud / joist faces). Finish layers (batts, drywall) resolve to their own face.
    /// </summary>
    public static class FaceAim
    {
        public static FramePlane Front(RaycastHit hit)
        {
            var fl = hit.collider.GetComponentInParent<FinishLayer>();
            if (fl != null)
            {
                if (fl.Owner is DrywallSheet sh) return sh.S.Fp;
                var b = hit.collider.GetComponentInParent<Batt>(); if (b != null) return b.Fp;
                var f = hit.collider.GetComponentInParent<FoamFill>(); if (f != null) return f.Fp;
            }
            Vector3 n = hit.normal;
            Vector3 a1 = Mathf.Abs(n.y) > 0.7f ? Vector3.right : Vector3.Cross(n, Vector3.up).normalized;
            Vector3 a2 = Mathf.Abs(n.y) > 0.7f ? Vector3.forward : Vector3.up;
            FramePlane best = null; float bestOff = float.MinValue;
            var start = hit.point + n * 0.25f;
            for (int i = -6; i <= 6; i++)
                foreach (var ax in new[] { a1, a2 })
                {
                    if (ax == a2 && i == 0) continue;
                    var o = start + ax * Units.In(2.5f) * i;
                    foreach (var h in FinishAim.SortedHits(new Ray(o, -n), 0.6f))
                    {
                        if (h.collider.GetComponentInParent<FinishLayer>() != null) continue;
                        var p = h.collider.GetComponentInParent<Piece>();
                        if (p == null || p.Def == null || (p.Def.Category != PieceCategory.Lumber && p.Def.Category != PieceCategory.Sheet)) continue;
                        float off = Vector3.Dot(h.point, n);
                        if (off > bestOff + 0.003f && Vector3.Dot(h.normal, n) > 0.95f)
                        {
                            var fp = FramePlane.FromHit(h);
                            if (fp != null) { best = fp; bestOff = off; }
                        }
                        break;
                    }
                }
            return best ?? FinishAim.FaceBehind(hit);
        }
    }
}
