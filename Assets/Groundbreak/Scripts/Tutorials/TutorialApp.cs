using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Tablet app "Tutorials": tutorials grouped by phase in construction order; each step shows a large
    /// picture captured from gameplay, the instruction, the keys/tools, Prev/Next and progress, and "show me where".</summary>
    public static class TutorialApp
    {
        public static Tutorial Open;     // null = list view
        public static int StepIndex;
        static GUIStyle _keys, _small, _tag, _caption;
        static int _styleSize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Open = null; StepIndex = 0;
            Tablet.RegisterApp("Tutorials", Draw);
            Tablet.AddHelp("Tutorials app: step-by-step guides with pictures for every trade, in build order. 'Show me where' marks the spot.");
        }

        /// <summary>Jump straight to a tutorial step (also used by tests).</summary>
        public static void Show(string id, int step = 0)
        {
            Open = Tutorials.Get(id);
            StepIndex = Open != null ? Mathf.Clamp(step, 0, Open.Steps.Count - 1) : 0;
        }

        static void Styles(GUIStyle label)
        {
            if (_keys != null && _styleSize == label.fontSize) return;
            _styleSize = label.fontSize;
            _keys = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            _keys.normal.textColor = new Color(1f, 0.78f, 0.3f);
            _small = new GUIStyle(label) { fontSize = Mathf.Max(10, Mathf.RoundToInt(label.fontSize * 0.85f)) };
            _small.normal.textColor = new Color(0.75f, 0.78f, 0.8f);
            _tag = new GUIStyle(_small) { alignment = TextAnchor.MiddleRight };
            _caption = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _caption.normal.textColor = new Color(0.85f, 0.88f, 0.9f);
        }

        static void Draw()
        {
            var t = Tablet.I;
            var label = t != null ? t.LabelStyle : GUI.skin.label;
            var head = t != null ? t.HeaderStyle : GUI.skin.label;
            var btn = t != null ? t.ButtonStyle : GUI.skin.button;
            Styles(label);
            if (Open == null) DrawList(label, head, btn);
            else DrawStep(label, head, btn);
        }

        static void DrawList(GUIStyle label, GUIStyle head, GUIStyle btn)
        {
            GUILayout.Label("How to build a house, step by step. Phases are in real construction order.", label);
            foreach (var phase in Tutorials.PhaseOrder)
            {
                bool any = false;
                foreach (var tut in Tutorials.All)
                {
                    if (tut.Phase != phase) continue;
                    if (!any) { GUILayout.Space(6); GUILayout.Label(phase, head); any = true; }
                    GUILayout.BeginHorizontal();
                    int seen = Tutorials.Seen(tut);
                    string mark = seen >= tut.Steps.Count ? "✓ " : seen > 0 ? "• " : "";
                    if (GUILayout.Button(mark + tut.Title, btn, GUILayout.Width(label.fontSize * 20))) { Open = tut; StepIndex = Mathf.Min(seen, tut.Steps.Count - 1); if (seen >= tut.Steps.Count) StepIndex = 0; }
                    GUILayout.Label(tut.Summary, label);
                    GUILayout.FlexibleSpace();
                    string tag = $"{seen}/{tut.Steps.Count} steps";
                    if (!tut.Available) tag = "coming with " + tut.ComingWith + " · " + tag;
                    GUILayout.Label(tag, _tag, GUILayout.Width(label.fontSize * 16));
                    GUILayout.EndHorizontal();
                }
            }
        }

        static void DrawStep(GUIStyle label, GUIStyle head, GUIStyle btn)
        {
            var tut = Open;
            if (tut.Steps.Count == 0) { Open = null; return; }
            StepIndex = Mathf.Clamp(StepIndex, 0, tut.Steps.Count - 1);
            var s = tut.Steps[StepIndex];
            Tutorials.MarkSeen(tut, StepIndex);

            // header: back · title · step n of m
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀ All tutorials", btn, GUILayout.Width(label.fontSize * 9))) { Open = null; return; }
            GUILayout.Label(tut.Phase + "  ›  " + tut.Title, head);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Step {StepIndex + 1} of {tut.Steps.Count}", head);
            GUILayout.EndHorizontal();

            // progress bar: one segment per step
            Rect bar = GUILayoutUtility.GetRect(10, 8, GUILayout.ExpandWidth(true));
            float segW = bar.width / tut.Steps.Count;
            for (int i = 0; i < tut.Steps.Count; i++)
            {
                var r = new Rect(bar.x + i * segW + 1, bar.y, segW - 2, bar.height);
                Fill(r, i < StepIndex ? new Color(0.35f, 0.7f, 0.4f) : i == StepIndex ? new Color(1f, 0.7f, 0.2f) : new Color(1f, 1f, 1f, 0.18f));
            }
            GUILayout.Space(6);

            // picture + text: side by side on a wide tablet, stacked on a narrow one
            float inner = Screen.width * 0.7f - 70f;
            bool wide = inner >= 900f;
            if (wide)
            {
                float pw = inner * 0.6f, ph = Mathf.Min(pw * 9f / 16f, Screen.height * 0.45f);
                GUILayout.BeginHorizontal();
                Rect pic = GUILayoutUtility.GetRect(pw, ph, GUILayout.Width(pw), GUILayout.Height(ph));
                DrawPicture(pic, tut, s);
                GUILayout.Space(12);
                GUILayout.BeginVertical();
                StepText(s, label, head);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            else
            {
                float h = Mathf.Min(inner * 9f / 16f, Screen.height * 0.36f);
                Rect pic = GUILayoutUtility.GetRect(inner, h, GUILayout.ExpandWidth(true));
                DrawPicture(pic, tut, s);
                GUILayout.Space(6);
                StepText(s, label, head);
            }

            // show me where
            if (!string.IsNullOrEmpty(s.OpenApp) || s.Find != null)
            {
                GUILayout.BeginHorizontal();
                if (!string.IsNullOrEmpty(s.OpenApp) && GUILayout.Button("Open " + s.OpenApp, btn, GUILayout.Width(label.fontSize * 14)))
                    Tablet.I?.ShowApp(s.OpenApp);
                if (s.Find != null)
                {
                    Vector3? p = null;
                    try { p = s.Find(); } catch { p = null; }
                    if (p.HasValue)
                    {
                        if (GUILayout.Button("Show me where: " + s.Where, btn, GUILayout.Width(label.fontSize * 20)))
                        {
                            TutorialBeacon.Mark(p.Value, s.Where);
                            Tablet.I?.Close();
                        }
                    }
                    else GUILayout.Label("(" + s.Where + " isn't on site yet)", _small);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUI.enabled = StepIndex > 0;
            if (GUILayout.Button("◀ Prev", btn, GUILayout.Width(label.fontSize * 7))) StepIndex--;
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (StepIndex < tut.Steps.Count - 1)
            {
                if (GUILayout.Button("Next ▶", btn, GUILayout.Width(label.fontSize * 7))) StepIndex++;
            }
            else if (GUILayout.Button(NextTutorial(tut) != null ? "Next tutorial ▶" : "Done", btn, GUILayout.Width(label.fontSize * 10)))
            {
                var n = NextTutorial(tut);
                Open = n; StepIndex = 0;
            }
            GUILayout.EndHorizontal();
        }

        static void StepText(TutorialStep s, GUIStyle label, GUIStyle head)
        {
            GUILayout.Label(s.Title, head);
            GUILayout.Label(s.Text, label);
            if (!string.IsNullOrEmpty(s.Keys)) GUILayout.Label("▶ " + s.Keys, _keys);
        }

        static Tutorial NextTutorial(Tutorial t)
        {
            // next in phase order
            var ordered = new List<Tutorial>();
            foreach (var ph in Tutorials.PhaseOrder) foreach (var x in Tutorials.All) if (x.Phase == ph) ordered.Add(x);
            int i = ordered.IndexOf(t);
            return i >= 0 && i + 1 < ordered.Count ? ordered[i + 1] : null;
        }

        static void DrawPicture(Rect r, Tutorial tut, TutorialStep s)
        {
            Fill(r, new Color(0.08f, 0.09f, 0.1f));
            var tex = TutorialPictures.Get(s.Picture);
            if (tex != null)
            {
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
                return;
            }
            // placeholder: blueprint-style panel until the trade lands and its picture is captured
            var inner = new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8);
            Fill(inner, new Color(0.11f, 0.2f, 0.33f));
            var grid = new Color(1f, 1f, 1f, 0.07f);
            for (float x = inner.x; x < inner.xMax; x += 24) Fill(new Rect(x, inner.y, 1, inner.height), grid);
            for (float y = inner.y; y < inner.yMax; y += 24) Fill(new Rect(inner.x, y, inner.width, 1), grid);
            string msg = tut.Available ? "Picture not captured yet" : "Picture coming when " + tut.ComingWith + " is merged";
            GUI.Label(new Rect(inner.x, inner.y + inner.height * 0.35f, inner.width, inner.height * 0.15f), s.Title, _caption);
            GUI.Label(new Rect(inner.x, inner.y + inner.height * 0.5f, inner.width, inner.height * 0.15f), msg, _caption);
        }

        static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }

    /// <summary>"Show me where": a tall orange marker at a world point plus an on-screen pointer, for 40 s or until you reach it.</summary>
    public class TutorialBeacon : MonoBehaviour
    {
        public static TutorialBeacon Current;
        public string Label;
        float _until;
        GUIStyle _style;

        public static TutorialBeacon Mark(Vector3 p, string label)
        {
            if (Current != null) Destroy(Current.gameObject);
            var go = new GameObject("TutorialBeacon");
            go.transform.position = p;
            var b = new MeshBuilder();
            b.AddCylinder(new Vector3(0f, 3f, 0f), 0.06f, 6f, 12, 0);
            b.AddFrustum(new Vector3(0f, 0.35f, 0f), Quaternion.Euler(180f, 0f, 0f), 0.25f, 0.02f, 0.5f, 16, 0);
            go.AddComponent<MeshFilter>().sharedMesh = b.Build("TutorialBeacon");
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Ghost(new Color(1f, 0.55f, 0.1f));
            Current = go.AddComponent<TutorialBeacon>();
            Current.Label = label;
            Current._until = Time.time + 40f;
            Game.Toast("Marked: " + label);
            return Current;
        }

        void Update()
        {
            var pl = Game.Player;
            bool reached = pl != null && Vector2.Distance(new Vector2(pl.transform.position.x, pl.transform.position.z), new Vector2(transform.position.x, transform.position.z)) < 1.5f;
            if (Time.time > _until || reached) Destroy(gameObject);
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void OnGUI()
        {
            var pl = Game.Player;
            if (pl == null || pl.Cam == null || Game.UIOpen) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Screen.height / 45), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _style.normal.textColor = new Color(1f, 0.65f, 0.2f);
            }
            Vector3 target = transform.position + Vector3.up * 1.2f;
            Vector3 sp = pl.Cam.WorldToScreenPoint(target);
            float dist = Vector3.Distance(pl.transform.position, transform.position);
            string text = Label + "  " + Units.Format(dist);
            float w = 520, h = Mathf.Max(30, _style.fontSize * 1.6f);
            if (sp.z > 0)
            {
                float x = Mathf.Clamp(sp.x, w / 2, Screen.width - w / 2), y = Mathf.Clamp(Screen.height - sp.y, h, Screen.height - h * 3);
                Tag(new Rect(x - w / 2, y - h, w, h), "▼ " + text);
            }
            else Tag(new Rect(Screen.width / 2 - w / 2, Screen.height * 0.15f, w, h), "↻ turn around: " + text);
        }

        void Tag(Rect r, string text)
        {
            var size = _style.CalcSize(new GUIContent(text));
            var bg = new Rect(r.center.x - size.x / 2 - 8, r.y, size.x + 16, r.height);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(r, text, _style);
        }
    }
}
