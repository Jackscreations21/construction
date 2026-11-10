using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public class CircularSaw : Tool
    {
        public override string Id => "saw";
        public override string Name => "Circular Saw";
        public override int Slots => 2;
        public override bool Cordless => true;
        public override string Hint => "LMB: cut aimed loose lumber (keeps longer side) · RMB: cycle cut length " + CutLabel;

        const float Range = 4f, DrainPerCut = 0.01f;
        int _preset;

        static string CutLabel
        {
            get
            {
                var pc = Game.Placement;
                if (pc == null) return "";
                return pc.CutLength <= 0f ? "(full stock)" : "(" + Units.Format(pc.CutLength) + ")";
            }
        }

        public override void Primary()
        {
            if (Battery <= 0f) { Game.Toast("Saw battery dead"); return; }
            if (Durability <= 0f) { Game.Toast("Saw is broken"); return; }
            var p = ToolUtil.AimPiece(Range, out var hit);
            if (p == null) return;
            if (p.State != PieceState.Loose || p.Def == null || !p.Def.Cuttable) { Game.Toast("Can only cut loose lumber"); return; }

            float half = p.Size.z * 0.5f;
            float localZ = Mathf.Clamp(p.LocalZ(hit.point), -half, half);
            // Snap the distance measured from the nearest end
            float snappedZ = localZ < 0f
                ? -half + Game.Snap.SnapLength(localZ + half)
                : half - Game.Snap.SnapLength(half - localZ);
            snappedZ = Mathf.Clamp(snappedZ, -half, half);
            if (half - Mathf.Abs(snappedZ) < 0.003f) { Game.Toast("Too close to the end"); return; }

            int keep = snappedZ >= 0f ? -1 : +1; // keep the longer side
            Vector3 cutWorld = p.transform.position + p.transform.rotation * new Vector3(0, 0, snappedZ);
            float offcut = p.CutAt(cutWorld, keep);
            if (offcut <= 0f) return;
            Game.Stock.Scrap.Add((p.Def.Id, offcut));
            Battery = Mathf.Max(0f, Battery - DrainPerCut);
            ToolUtil.Wear(this, 0.001f);
            Game.Toast("Cut " + p.Def.Name + " to " + Units.Format(p.Size.z) + " · offcut " + Units.Format(offcut) + " to scrap");
        }

        public override void Secondary()
        {
            var pc = Game.Placement;
            if (pc == null) return;
            var presets = new List<float> { 0f, Units.In(92.625f), Units.In(96f), Units.In(48f) };
            if (TapeMeasure.LastMeasurement > 0.01f) presets.Add(TapeMeasure.LastMeasurement);
            float max = pc.Selected != null ? pc.Selected.Size.z : float.MaxValue;
            for (int tries = 0; tries < presets.Count; tries++)
            {
                _preset = (_preset + 1) % presets.Count;
                if (presets[_preset] <= max + 0.0001f) break;
            }
            float len = presets[_preset] <= max + 0.0001f ? presets[_preset] : 0f;
            pc.CutLength = len;
            Game.Toast("Cut length: " + (len <= 0f ? "full stock" : Units.Format(len)) + (_preset == 4 ? " (tape)" : ""));
        }
    }
}
