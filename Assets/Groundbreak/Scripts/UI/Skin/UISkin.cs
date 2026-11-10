using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Consistent IMGUI look: rounded panels and buttons from generated 9-slice textures, procedural icons.
    /// Call <see cref="Apply"/> at the top of any OnGUI to use the skin; <see cref="Panel"/> draws a rounded panel.</summary>
    public static class UISkin
    {
        public static readonly Color Accent = new Color(1f, 0.60f, 0.12f), PanelColor = new Color(0.07f, 0.09f, 0.11f, 0.88f);
        static GUISkin _skin;
        static Texture2D _round, _btn, _btnHover, _btnActive, _btnOn, _field;
        static readonly Dictionary<string, Texture2D> _icons = new Dictionary<string, Texture2D>();
        static int _builtFor;

        static Texture2D Rounded(int size, int radius, Color fill, Color border, int borderW = 1, bool gradient = false)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    if (radius == 0) a = 1;
                    float edge = Mathf.Clamp01(radius - d - borderW + 0.5f);
                    bool inBorder = x < borderW || y < borderW || x >= size - borderW || y >= size - borderW;
                    Color c = fill;
                    if (gradient) c = Color.Lerp(fill * 0.85f, fill * 1.12f, y / (float)size);
                    c.a = fill.a;
                    Color o = (edge < 1f || inBorder) && borderW > 0 ? Color.Lerp(border, c, edge * (inBorder ? 0 : 1)) : c;
                    o.a *= a;
                    px[y * size + x] = o;
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static void Build()
        {
            int fs = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 50f));
            _builtFor = Screen.height;
            _round = Rounded(32, 10, Color.white, Color.white, 0);
            _btn = Rounded(32, 7, new Color(0.20f, 0.23f, 0.27f, 0.95f), new Color(0.36f, 0.40f, 0.46f), 1, true);
            _btnHover = Rounded(32, 7, new Color(0.27f, 0.31f, 0.36f, 0.97f), Accent, 1, true);
            _btnActive = Rounded(32, 7, new Color(0.85f, 0.48f, 0.08f, 1f), Accent, 1, true);
            _btnOn = Rounded(32, 7, new Color(0.92f, 0.55f, 0.12f, 1f), new Color(1f, 0.8f, 0.5f), 1, true);
            _field = Rounded(32, 5, new Color(0.04f, 0.05f, 0.06f, 0.9f), new Color(0.3f, 0.33f, 0.38f), 1);

            _skin = Object.Instantiate(GUI.skin);
            _skin.hideFlags = HideFlags.DontSave;
            var border = new RectOffset(9, 9, 9, 9);
            void Btn(GUIStyle s)
            {
                s.normal.background = _btn; s.hover.background = _btnHover; s.active.background = _btnActive;
                s.onNormal.background = _btnOn; s.onHover.background = _btnOn; s.onActive.background = _btnActive;
                s.focused.background = _btn; s.onFocused.background = _btnOn;
                s.border = border; s.padding = new RectOffset(10, 10, 6, 6); s.margin = new RectOffset(3, 3, 3, 3);
                s.fontSize = fs; s.fontStyle = FontStyle.Bold; s.alignment = TextAnchor.MiddleCenter;
                foreach (var st in new[] { s.normal, s.hover, s.active, s.focused }) st.textColor = new Color(0.93f, 0.94f, 0.96f);
                foreach (var st in new[] { s.onNormal, s.onHover, s.onActive, s.onFocused }) st.textColor = new Color(0.08f, 0.06f, 0.04f);
            }
            Btn(_skin.button); Btn(_skin.toggle);
            _skin.toggle.alignment = TextAnchor.MiddleLeft;
            _skin.label.fontSize = fs; _skin.label.normal.textColor = new Color(0.92f, 0.93f, 0.95f); _skin.label.wordWrap = true;
            _skin.box.normal.background = _round; _skin.box.border = new RectOffset(10, 10, 10, 10);
            _skin.textField.normal.background = _field; _skin.textField.focused.background = _field; _skin.textField.border = new RectOffset(6, 6, 6, 6);
            _skin.textField.fontSize = fs; _skin.textField.normal.textColor = Color.white; _skin.textField.padding = new RectOffset(8, 8, 5, 5);
            _skin.verticalScrollbar.normal.background = _field; _skin.verticalScrollbarThumb.normal.background = _btn;
            _skin.verticalScrollbar.fixedWidth = 12; _skin.verticalScrollbarThumb.fixedWidth = 12;
            _skin.verticalScrollbarThumb.border = new RectOffset(6, 6, 6, 6);
        }

        public static GUISkin Skin { get { if (_skin == null || _builtFor != Screen.height) Build(); return _skin; } }

        /// <summary>Use the Groundbreak skin for the rest of this OnGUI.</summary>
        public static void Apply() => GUI.skin = Skin;

        /// <summary>Rounded translucent panel.</summary>
        public static void Panel(Rect r, Color? color = null)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_round == null) Build();
            var prev = GUI.color; GUI.color = color ?? PanelColor;
            var style = Skin.box;
            style.Draw(r, GUIContent.none, false, false, false, false);
            GUI.color = prev;
        }

        static GUIStyle _info;
        /// <summary>Centered info panel at a screen y (tool readouts).</summary>
        public static void InfoBox(string text, float y)
        {
            if (_info == null || _info.fontSize != Mathf.Max(12, Screen.height / 52))
            {
                _info = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Screen.height / 52), alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _info.normal.textColor = Color.white;
            }
            float w = Mathf.Min(Screen.width * 0.6f, 760f);
            float h = _info.CalcHeight(new GUIContent(text), w - 24) + 14;
            var r = new Rect((Screen.width - w) / 2, y, w, h);
            Panel(r);
            GUI.Label(new Rect(r.x + 12, r.y + 7, r.width - 24, r.height - 14), text, _info);
        }

        /// <summary>Small procedural icons: check, cross, dot, warn, rain, sun, money, clock, star, house.</summary>
        public static Texture2D Icon(string name)
        {
            if (_icons.TryGetValue(name, out var t) && t != null) return t;
            const int N = 32;
            t = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear };
            var px = new Color[N * N];
            float Seg(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float k = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude); return Vector2.Distance(p, a + ab * k); }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f) / N; float a = 0; Color c = Color.white;
                    float r = Vector2.Distance(p, new Vector2(0.5f, 0.5f));
                    switch (name)
                    {
                        case "check": a = 1 - Mathf.Clamp01((Mathf.Min(Seg(p, new Vector2(.2f, .5f), new Vector2(.42f, .27f)), Seg(p, new Vector2(.42f, .27f), new Vector2(.82f, .75f))) - .07f) * N); c = new Color(.4f, .95f, .45f); break;
                        case "cross": a = 1 - Mathf.Clamp01((Mathf.Min(Seg(p, new Vector2(.22f, .22f), new Vector2(.78f, .78f)), Seg(p, new Vector2(.22f, .78f), new Vector2(.78f, .22f))) - .07f) * N); c = new Color(1f, .4f, .35f); break;
                        case "dot": a = 1 - Mathf.Clamp01((r - .18f) * N); c = new Color(.8f, .82f, .86f); break;
                        case "sun": a = Mathf.Max(1 - Mathf.Clamp01((r - .2f) * N), (r > .28f && r < .42f && Mathf.Repeat(Mathf.Atan2(p.y - .5f, p.x - .5f) / (Mathf.PI / 4), 1f) < .3f) ? 1 : 0); c = new Color(1f, .85f, .3f); break;
                        case "rain":
                        {
                            float cloud = Mathf.Min(Vector2.Distance(p, new Vector2(.38f, .62f)) - .17f, Vector2.Distance(p, new Vector2(.6f, .65f)) - .2f);
                            float drops = 1; for (int i = 0; i < 3; i++) drops = Mathf.Min(drops, Seg(p, new Vector2(.3f + i * .18f, .35f), new Vector2(.26f + i * .18f, .15f)) - .03f);
                            a = 1 - Mathf.Clamp01(Mathf.Min(cloud, drops) * N); c = p.y > .4f ? new Color(.8f, .85f, .92f) : new Color(.45f, .7f, 1f); break;
                        }
                        case "star":
                        {
                            float ang = Mathf.Atan2(p.y - .5f, p.x - .5f) + Mathf.PI / 2; float k = Mathf.Cos(5 * ang) * 0.5f + 0.5f;
                            a = 1 - Mathf.Clamp01((r - Mathf.Lerp(.2f, .44f, k)) * N); c = new Color(1f, .8f, .2f); break;
                        }
                        case "house":
                        {
                            bool body = p.x > .25f && p.x < .75f && p.y > .15f && p.y < .55f;
                            bool roof = p.y >= .55f && p.y < .85f && Mathf.Abs(p.x - .5f) < (.85f - p.y) * 1.25f;
                            a = body || roof ? 1 : 0; c = Accent; break;
                        }
                        default: a = 1 - Mathf.Clamp01((r - .3f) * N); c = Accent; break;
                    }
                    c.a = Mathf.Clamp01(a);
                    px[y * N + x] = c;
                }
            t.SetPixels(px); t.Apply();
            _icons[name] = t;
            return t;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { _skin = null; _round = null; _icons.Clear(); _info = null; }
    }
}
