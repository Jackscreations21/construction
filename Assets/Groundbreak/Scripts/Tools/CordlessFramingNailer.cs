using UnityEngine;

namespace Groundbreak
{
    public class CordlessFramingNailer : Tool
    {
        public override string Id => "nailer";
        public override string Name => "Cordless Framing Nailer";
        public override bool Cordless => true;
        public override string Hint => $"LMB: drive a nail (2 nails = fastened) · battery {Mathf.RoundToInt(Battery * 100)}%";

        const float Range = 3f, Cooldown = 0.15f, DrainPerNail = 0.005f;
        float _next;

        public override void Primary()
        {
            if (Time.time < _next) return;
            if (Battery <= 0f) { Game.Toast("Nailer battery dead"); return; }
            if (Durability <= 0f) { Game.Toast("Nailer is broken"); return; }
            var p = ToolUtil.AimPiece(Range, out _);
            if (p == null || p.State != PieceState.Loose) return;
            var belt = Game.Belt;
            if (belt == null || belt.Nails <= 0) { Game.Toast("Nail pouch empty - press E near a pile"); return; }
            _next = Time.time + Cooldown;
            belt.Nails--;
            p.Nails++;
            Battery = Mathf.Max(0f, Battery - DrainPerNail);
            ToolUtil.Wear(this, 0.0002f);
            if (p.Nails >= 2) { p.Fasten(); Game.Toast($"{p.Def.Name} fastened"); }
        }
    }
}
