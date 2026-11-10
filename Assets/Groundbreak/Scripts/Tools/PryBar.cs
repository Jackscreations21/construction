using UnityEngine;

namespace Groundbreak
{
    public class PryBar : Tool
    {
        public override string Id => "prybar";
        public override string Name => "Pry Bar";
        public override string Hint => "Hold LMB on a fastened piece for 1s to pull it (50% chance lumber is reusable)";

        const float Range = 3f, HoldTime = 1f;
        Piece _target;
        float _held;
        int _lastHeldFrame = -10;

        public override void OnUnequip() { _target = null; _held = 0f; }

        public override void PrimaryHeld()
        {
            _lastHeldFrame = Time.frameCount;
            var p = ToolUtil.AimPiece(Range, out _);
            if (p == null || p.State != PieceState.Fastened) { _target = null; _held = 0f; return; }
            if (p != _target) { _target = p; _held = 0f; }
            _held += Time.deltaTime;
            if (_held < HoldTime) return;

            bool reusable = Random.value < 0.5f;
            string id = p.Def.Id;
            bool fullLength = Mathf.Abs(p.Size.z - p.Def.Size.z) < 0.002f;
            if (reusable && fullLength) { Game.Stock.Add(id, 1); Game.Toast("Pulled " + p.Def.Name + " - back in stock"); }
            else if (reusable) { Game.Stock.Scrap.Add((id, p.Size.z)); Game.Toast("Pulled " + p.Def.Name + " (" + Units.Format(p.Size.z) + ") - added to scrap"); }
            else { Game.Stock.Scrap.Add((id, p.Size.z)); Game.Toast("Pulled " + p.Def.Name + " - it split, scrap only"); }
            ToolUtil.Wear(this, 0.002f);
            Object.Destroy(p.gameObject);
            _target = null; _held = 0f;
        }

        public override void Tick()
        {
            // ToolBelt calls Tick before PrimaryHeld; reset if the button wasn't held last frame
            if (Time.frameCount - _lastHeldFrame > 1) { _held = 0f; _target = null; }
        }

        public override void DrawGUI()
        {
            if (_target == null || _held <= 0f) return;
            float w = 160f, x = (Screen.width - w) * 0.5f, y = Screen.height * 0.5f + 30f;
            GUI.Box(new Rect(x, y, w, 12f), "");
            GUI.Box(new Rect(x, y, w * Mathf.Clamp01(_held / HoldTime), 12f), "");
        }
    }
}
