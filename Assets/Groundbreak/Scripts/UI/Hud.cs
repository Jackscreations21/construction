using UnityEngine;

namespace Groundbreak
{
    /// <summary>In-game HUD (IMGUI): crosshair, belt, nails, snap readout, clock, money, placement status, tool hint, toasts.</summary>
    public class Hud : MonoBehaviour
    {
        GUIStyle _label, _small, _center, _boxText;
        int _styleHeight;

        void EnsureStyles()
        {
            if (_label != null && _styleHeight == Screen.height) return;
            _styleHeight = Screen.height;
            int fs = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 45f));
            _label = new GUIStyle(GUI.skin.label) { fontSize = fs, wordWrap = false };
            _label.normal.textColor = Color.white;
            _small = new GUIStyle(_label) { fontSize = Mathf.Max(10, Mathf.RoundToInt(fs * 0.75f)), alignment = TextAnchor.UpperCenter, wordWrap = true };
            _center = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(fs * 1.15f), fontStyle = FontStyle.Bold, wordWrap = true };
            _boxText = new GUIStyle(_label) { alignment = TextAnchor.UpperRight };
        }

        static void Fill(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void ShadowLabel(Rect r, string text, GUIStyle style)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = prev;
            GUI.Label(r, text, style);
        }

        void OnGUI()
        {
            if (Game.UIOpen) return;
            EnsureStyles();
            float sw = Screen.width, sh = Screen.height;
            float line = _label.fontSize * 1.5f;

            // Crosshair
            float c = Mathf.Max(6f, sh / 90f);
            Fill(new Rect(sw / 2 - c, sh / 2 - 1, c * 2, 2), new Color(1, 1, 1, 0.85f));
            Fill(new Rect(sw / 2 - 1, sh / 2 - c, 2, c * 2), new Color(1, 1, 1, 0.85f));

            // Top-right: snap, clock, money
            {
                string text = $"Grid: {Game.Snap.GridLabel}   Angle: {Game.Snap.AngleLabel}\n"
                            + (GameClock.I != null ? GameClock.I.TimeLabel + "\n" : "")
                            + $"${Game.Wallet.Money:N0}";
                float bw = _label.fontSize * 20, bh = line * 3 + 10;
                var r = new Rect(sw - bw - 12, 12, bw, bh);
                Fill(r, new Color(0, 0, 0, 0.5f));
                GUI.Label(new Rect(r.x + 8, r.y + 5, r.width - 16, r.height - 10), text, _boxText);
            }

            // Bottom center: belt
            var belt = Game.Belt;
            if (belt != null && belt.Slots != null)
            {
                int n = belt.Slots.Length;
                float slotW = Mathf.Min(sw / (n + 2), _label.fontSize * 7f), slotH = slotW * 0.7f;
                float x0 = (sw - slotW * n) / 2f, y0 = sh - slotH - 12;
                for (int i = 0; i < n; i++)
                {
                    var t = belt.Slots[i];
                    var r = new Rect(x0 + i * slotW + 2, y0, slotW - 4, slotH);
                    bool active = i == belt.ActiveIndex;
                    bool continuation = t != null && i > 0 && belt.Slots[i - 1] == t;
                    Fill(r, active ? new Color(1f, 0.6f, 0.1f, 0.75f) : new Color(0, 0, 0, 0.5f));
                    GUI.Label(new Rect(r.x + 4, r.y + 2, 30, line), (i + 1).ToString(), _label);
                    if (t != null)
                    {
                        string name = continuation ? "(" + t.Name + ")" : t.Name;
                        GUI.Label(new Rect(r.x + 2, r.y + line * 0.8f, r.width - 4, r.height - line), name, _small);
                        if (t.Cordless)
                        {
                            var bar = new Rect(r.x + 4, r.yMax - 8, r.width - 8, 5);
                            Fill(bar, new Color(0.2f, 0.2f, 0.2f, 0.9f));
                            float b = Mathf.Clamp01(t.Battery);
                            Fill(new Rect(bar.x, bar.y, bar.width * b, bar.height), b > 0.25f ? new Color(0.3f, 0.9f, 0.3f) : new Color(0.95f, 0.25f, 0.2f));
                        }
                    }
                }
                ShadowLabel(new Rect(x0, y0 - line - 2, slotW * n, line), $"Nails: {belt.Nails} / {belt.NailCapacity}", _label);
            }

            // Bottom-left: placement status + tool hint
            {
                float y = sh - 12 - line;
                float leftW = Mathf.Max(200f, (sw - Mathf.Min(sw, _label.fontSize * 7f * 8)) / 2f - 20);
                var tool = belt != null ? belt.Active : null;
                if (tool != null && !string.IsNullOrEmpty(tool.Hint))
                {
                    ShadowLabel(new Rect(12, y, leftW, line), tool.Hint, _label);
                    y -= line;
                }
                var pc = Game.Placement;
                if (pc != null && pc.Active && !string.IsNullOrEmpty(pc.StatusLine))
                    ShadowLabel(new Rect(12, y, Mathf.Max(leftW, sw * 0.5f), line), pc.StatusLine, _label);
            }

            // Toast
            if (Time.time < Game.ToastUntil && !string.IsNullOrEmpty(Game.ToastText))
            {
                float tw = sw * 0.6f, th = line * 2;
                var r = new Rect((sw - tw) / 2f, sh * 0.25f, tw, th);
                Fill(r, new Color(0, 0, 0, 0.55f));
                GUI.Label(r, Game.ToastText, _center);
            }
        }
    }
}
