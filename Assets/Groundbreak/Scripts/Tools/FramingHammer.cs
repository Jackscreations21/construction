using UnityEngine;

namespace Groundbreak
{
    public class FramingHammer : Tool
    {
        public override string Id => "hammer";
        public override string Name => "Framing Hammer";
        public override string Hint => "LMB: swing at a loose piece (3 swings = 1 nail, 2 nails = fastened)";

        const float Range = 3f, Cooldown = 0.35f;
        const int SwingsPerNail = 3, NailsToFasten = 2;
        float _nextSwing;
        int _swings;
        Piece _target;

        public override void OnUnequip() { _swings = 0; _target = null; }

        public override void Primary()
        {
            if (Time.time < _nextSwing) return;
            if (Durability <= 0f) { Game.Toast("Hammer is broken"); return; }
            var p = ToolUtil.AimPiece(Range, out RaycastHit hit);
            if (p == null || p.State != PieceState.Loose) return;
            _nextSwing = Time.time + Cooldown;
            if (p != _target) { _target = p; _swings = 0; }
            var belt = Game.Belt;
            if (belt == null || belt.Nails <= 0) { Game.Toast("Nail pouch empty - press E near a pile"); return; }
            _swings++;
            FastenFeedback.Kick(0.5f);
            ToolUtil.Wear(this, 0.0005f);
            if (_swings < SwingsPerNail) return;
            _swings = 0;
            belt.Nails--;
            p.Nails++;
            FastenFeedback.Nailed(p, hit, 0.3f);
            if (p.Nails >= NailsToFasten) { p.Fasten(); Game.Toast($"{p.Def.Name} fastened"); _target = null; }
        }

        public override void DrawGUI()
        {
            if (_target != null && _swings > 0)
                ToolUtil.CenterLabel(new string('|', _swings) + new string('.', SwingsPerNail - _swings), Color.white);
        }
    }
}
