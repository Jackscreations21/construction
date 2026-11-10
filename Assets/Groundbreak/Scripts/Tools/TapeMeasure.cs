using UnityEngine;

namespace Groundbreak
{
    public class TapeMeasure : Tool
    {
        public override string Id => "tape";
        public override string Name => "Tape Measure";
        public override string Hint => "LMB: point A, LMB: point B (snaps to grid, Ctrl = free) · RMB: clear";

        public static float LastMeasurement;

        const float Range = 15f;
        Vector3 _a, _b;
        int _points; // 0 none, 1 A set, 2 both set
        float _live;
        LineRenderer _line;

        static Vector3 SnapPoint(Vector3 p) => GInput.Ctrl ? p : Game.Snap.SnapPosition(p);

        public override void OnEquip()
        {
            if (_line == null)
            {
                var go = new GameObject("TapeLine");
                go.layer = 2;
                _line = go.AddComponent<LineRenderer>();
                _line.positionCount = 2;
                _line.startWidth = 0.01f;
                _line.endWidth = 0.01f;
                _line.useWorldSpace = true;
                _line.sharedMaterial = MaterialLibrary.Get(new Color(1f, 0.85f, 0.1f));
                _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _line.gameObject.SetActive(_points > 0);
        }

        public override void OnUnequip()
        {
            if (_line != null) _line.gameObject.SetActive(false);
        }

        public override void Primary()
        {
            if (Game.Player == null || !Game.Player.TryAim(out var hit, Range)) return;
            var p = SnapPoint(hit.point);
            if (_points != 1) { _a = p; _points = 1; }
            else
            {
                _b = p; _points = 2;
                LastMeasurement = Vector3.Distance(_a, _b);
                Game.Toast("Measured " + Units.Format(LastMeasurement));
            }
        }

        public override void Secondary()
        {
            _points = 0;
            if (_line != null) _line.gameObject.SetActive(false);
        }

        public override void Tick()
        {
            if (_line == null) return;
            if (_points == 0) { _line.gameObject.SetActive(false); return; }
            Vector3 end = _b;
            if (_points == 1)
            {
                if (Game.Player != null && Game.Player.TryAim(out var hit, Range)) end = SnapPoint(hit.point);
                else end = _a;
            }
            _line.gameObject.SetActive(true);
            _line.SetPosition(0, _a);
            _line.SetPosition(1, end);
            _live = Vector3.Distance(_a, end);
        }

        public override void DrawGUI()
        {
            if (_points == 0)
            {
                if (LastMeasurement > 0f) ToolUtil.CenterLabel("Last: " + Units.Format(LastMeasurement), Color.white);
                return;
            }
            ToolUtil.CenterLabel((_points == 1 ? "~ " : "") + Units.Format(_live), new Color(1f, 0.85f, 0.1f));
        }
    }
}
