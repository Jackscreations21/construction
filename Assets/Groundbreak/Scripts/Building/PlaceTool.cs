using UnityEngine;

namespace Groundbreak
{
    /// <summary>Bare hands: place the selected material (ghost preview) or pick up loose pieces.</summary>
    public class PlaceTool : Tool
    {
        public override string Id => "place";
        public override string Name => "Hands / Place";

        public override string Hint =>
            "LMB place · RMB pick up · [ ] material · Scroll rotate · Alt+Scroll pitch · Ctrl free-snap · F repeat · Ctrl+Z undo · G grid · R angle · O origin";

        public override void OnEquip() { if (Game.Placement != null) Game.Placement.Active = true; }
        public override void OnUnequip() { if (Game.Placement != null) Game.Placement.Active = false; }

        public override void Primary()
        {
            if (Game.Placement != null) Game.Placement.Place();
        }

        public override void Secondary()
        {
            if (Game.Player == null) return;
            if (!Game.Player.TryAim(out RaycastHit hit, PlacementController.AimRange)) return;
            var piece = hit.collider != null ? hit.collider.GetComponentInParent<Piece>() : null;
            if (piece == null || piece.Def == null) return;
            if (piece.State != PieceState.Loose) { Game.Toast("It's nailed down - use the pry bar"); return; }

            if (piece.Size.z >= piece.Def.Size.z - 0.0005f)
            {
                Game.Stock.Add(piece.Def.Id, 1);
                Game.Toast("Picked up " + piece.Def.Name);
            }
            else
            {
                Game.Stock.Scrap.Add((piece.Def.Id, piece.Size.z));
                Game.Toast("Picked up " + Units.Format(piece.Size.z) + " offcut of " + piece.Def.Name + " (scrap)");
            }
            Object.Destroy(piece.gameObject);
        }
    }
}
