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
            UISkin.Apply(); // site-career: shared rounded skin
            EnsureStyles();
            float sw = Screen.width, sh = Screen.height;
            float line = _label.fontSize * 1.5f;

            DrawCrosshair(sw, sh);

            // Top-right: snap, clock, money
            {
                string text = $"Grid: {Game.Snap.GridLabel}   Angle: {Game.Snap.AngleLabel}\n"
                            + (GameClock.I != null ? GameClock.I.TimeLabel + "\n" : "")
                            + $"${Game.Wallet.Money:N0}";
                float bw = _label.fontSize * 20, bh = line * 3 + 10;
                var r = new Rect(sw - bw - 12, 12, bw, bh);
                UISkin.Panel(r);
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
                    UISkin.Panel(r, active ? new Color(1f, 0.6f, 0.1f, 0.8f) : UISkin.PanelColor);
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

            DrawToast(sw, sh, line);
        }

        // ------------------------------------------------------------------ crosshair: dark outline for contrast on white
        // drywall, tightens and turns accent when the aim hits something within reach, pulses on click.
        float _aimK, _pulse;
        void DrawCrosshair(float sw, float sh)
        {
            if (Event.current.type == EventType.Repaint)
            {
                bool reach = Game.Player != null && Game.Player.TryAim(out var hit, 4f);
                _aimK = Mathf.MoveTowards(_aimK, reach ? 1f : 0f, Time.unscaledDeltaTime * 8f);
                if (Cursor.lockState == CursorLockMode.Locked && GInput.MouseDown(0)) _pulse = 1f;
                _pulse = Mathf.MoveTowards(_pulse, 0f, Time.unscaledDeltaTime * 6f);
            }
            float u = Mathf.Max(1f, Mathf.Round(sh / 720f));      // 1 px at 720p, 2 px at 1440p
            float len = Mathf.Round(Mathf.Max(5f, sh / 110f)), gap = Mathf.Round(Mathf.Lerp(len * 0.9f, len * 0.45f, _aimK) + _pulse * len * 0.8f);
            float th = 2f * u, cx = Mathf.Round(sw / 2f), cy = Mathf.Round(sh / 2f);
            Color fg = Color.Lerp(new Color(1f, 1f, 1f, 0.75f), new Color(1f, 0.82f, 0.55f, 0.95f), _aimK);
            Color ol = new Color(0f, 0f, 0f, 0.55f);
            void Arm(Rect r) { Fill(new Rect(r.x - u, r.y - u, r.width + 2 * u, r.height + 2 * u), ol); }
            var arms = new[]
            {
                new Rect(cx - gap - len, cy - th / 2, len, th), new Rect(cx + gap, cy - th / 2, len, th),
                new Rect(cx - th / 2, cy - gap - len, th, len), new Rect(cx - th / 2, cy + gap, th, len),
            };
            foreach (var r in arms) Arm(r);
            var dot = new Rect(cx - th / 2, cy - th / 2, th, th);
            Arm(dot);
            foreach (var r in arms) Fill(r, fg);
            Fill(dot, fg);
        }

        // ------------------------------------------------------------------ toast: slides down + fades, accent bar, soft chime
        string _lastToast; float _toastStart;
        void DrawToast(float sw, float sh, float line)
        {
            if (string.IsNullOrEmpty(Game.ToastText) || Time.time >= Game.ToastUntil) { _lastToast = null; return; }
            if (Game.ToastText != _lastToast)
            {
                if (_lastToast == null || Time.time - _toastStart > 0.3f) Sfx.Play("toast", Vector3.zero, 0.6f);
                _lastToast = Game.ToastText; _toastStart = Time.time;
            }
            float age = Time.time - _toastStart, left = Game.ToastUntil - Time.time;
            float a = Mathf.Clamp01(age / 0.15f) * Mathf.Clamp01(left / 0.35f);
            float slide = (1f - Mathf.Clamp01(age / 0.18f)); slide *= slide;
            float tw = Mathf.Min(sw * 0.6f, _center.fontSize * 34f);
            float th = Mathf.Max(line * 1.6f, _center.CalcHeight(new GUIContent(Game.ToastText), tw - 40) + line * 0.6f);
            var r = new Rect((sw - tw) / 2f, sh * 0.22f - slide * line, tw, th);
            var prev = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            UISkin.Panel(r, new Color(0.07f, 0.09f, 0.11f, 0.9f * a));
            Fill(new Rect(r.x + 8, r.y + 8, 3, r.height - 16), new Color(UISkin.Accent.r, UISkin.Accent.g, UISkin.Accent.b, a));
            GUI.Label(new Rect(r.x + 20, r.y, r.width - 34, r.height), Game.ToastText, _center);
            GUI.color = prev;
        }
    }
}
