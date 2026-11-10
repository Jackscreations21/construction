using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Attaches MEP behaviour (NetNode ports, custom visuals) to catalog Pieces as they appear in the scene,
    /// whether placed with the Place tool, spawned by tests or loaded. Trades register attachers by definition id.</summary>
    public class MepAutoAttach : MonoBehaviour
    {
        public static MepAutoAttach I;
        static readonly Dictionary<string, Action<Piece>> _byId = new Dictionary<string, Action<Piece>>();
        readonly HashSet<Piece> _seen = new HashSet<Piece>();
        float _next;

        /// <summary>Register an attacher for a definition id (exact). Call from BeforeSceneLoad.</summary>
        public static void Register(string defId, Action<Piece> attach) => _byId[defId] = attach;

        /// <summary>Runs the attacher for this piece now (idempotent). Tests call this right after Piece.Create.</summary>
        public static void AttachNow(Piece p)
        {
            if (p == null || p.Def == null) return;
            if (I != null && !I._seen.Add(p)) return;
            if (p.GetComponent<NetNode>() != null) return;
            if (_byId.TryGetValue(p.Def.Id, out var a)) a(p);
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.3f;
            foreach (var p in FindObjectsByType<Piece>(FindObjectsSortMode.None))
                if (p.Def != null && _byId.ContainsKey(p.Def.Id) && !_seen.Contains(p)) AttachNow(p);
        }

        /// <summary>Helper for attachers: replaces the piece's generic visual with a custom one.</summary>
        public static void ReplaceVisual(Piece p, GameObject newVisual)
        {
            foreach (Transform c in p.transform) if (c.name == "Visual" && c.gameObject != newVisual) Destroy(c.gameObject);
            if (newVisual != null) { newVisual.name = "Visual"; newVisual.transform.SetParent(p.transform, true); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { I = null; }
    }

    // ===================================================================================== inspection

    public class Finding
    {
        public Trade Trade;
        public string Rule;
        public string Note;
        public Vector3 Pos;
        public override string ToString() => $"[{Trade}] {Note}";
    }

    /// <summary>A rough-inspection rule as data: id, trade, the code text it enforces, and a check that yields findings.</summary>
    public class MepRule
    {
        public string Id;
        public Trade Trade;
        public string Text;
        public Func<IEnumerable<Finding>> Check;
    }

    /// <summary>Rough plumbing / electrical / mechanical inspection. Rules are registered data; failures get red markers + notes.</summary>
    public static class MepInspection
    {
        static readonly List<MepRule> _rules = new List<MepRule>();
        public static IReadOnlyList<MepRule> Rules => _rules;
        public static readonly List<Finding> LastFindings = new List<Finding>();
        public static readonly Dictionary<Trade, bool> LastPassed = new Dictionary<Trade, bool>();
        static readonly List<GameObject> _markers = new List<GameObject>();

        public static void Register(MepRule r)
        {
            _rules.RemoveAll(x => x.Id == r.Id);
            _rules.Add(r);
        }

        /// <summary>Runs every rule for the trade (null = all trades). Returns findings (empty = pass) and shows markers.</summary>
        public static List<Finding> Run(Trade? trade = null, bool showMarkers = true)
        {
            var list = new List<Finding>();
            foreach (var r in _rules)
            {
                if (trade.HasValue && r.Trade != trade.Value) continue;
                try
                {
                    foreach (var f in r.Check())
                    {
                        if (f == null) continue;
                        f.Trade = r.Trade; if (string.IsNullOrEmpty(f.Rule)) f.Rule = r.Id;
                        list.Add(f);
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[MEP] rule {r.Id} threw: {e.Message}"); }
            }
            LastFindings.RemoveAll(f => !trade.HasValue || f.Trade == trade.Value);
            LastFindings.AddRange(list);
            foreach (Trade t in Enum.GetValues(typeof(Trade)))
                if (!trade.HasValue || trade.Value == t) LastPassed[t] = !list.Exists(f => f.Trade == t);
            if (showMarkers) ShowMarkers(LastFindings);
            return list;
        }

        public static void ShowMarkers(List<Finding> findings)
        {
            ClearMarkers();
            foreach (var f in findings)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "InspectionMarker";
                go.layer = 2;
                UnityEngine.Object.Destroy(go.GetComponent<Collider>());
                go.transform.position = f.Pos;
                go.transform.localScale = Vector3.one * 0.07f;
                go.GetComponent<Renderer>().sharedMaterial = MepLook.Marker;
                var m = go.AddComponent<InspectionMarker>(); m.Note = f.Note;
                _markers.Add(go);
            }
        }

        public static void ClearMarkers()
        {
            foreach (var m in _markers) if (m != null) UnityEngine.Object.Destroy(m);
            _markers.Clear();
        }

        public static int MarkerCount => _markers.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { LastFindings.Clear(); LastPassed.Clear(); _markers.Clear(); }
    }

    /// <summary>Red inspection marker with a floating note (drawn when within 8 m and in front of the camera).</summary>
    public class InspectionMarker : MonoBehaviour
    {
        public string Note;
        static GUIStyle _st;
        void OnGUI()
        {
            var cam = Game.Player != null ? Game.Player.Cam : Camera.main;
            if (cam == null || Game.UIOpen) return;
            Vector3 sp = cam.WorldToScreenPoint(transform.position);
            if (sp.z < 0.1f || sp.z > 8f) return;
            if (_st == null) { _st = new GUIStyle(GUI.skin.box) { fontSize = 13, wordWrap = true, alignment = TextAnchor.MiddleLeft }; _st.normal.textColor = new Color(1f, 0.8f, 0.8f); }
            GUI.Box(new Rect(sp.x + 10, Screen.height - sp.y - 12, 260, 40), Note, _st);
        }
    }

    // ===================================================================================== tablet

    /// <summary>"MEP Plans" tablet app: per-trade sections registered by each trade + the rough inspection tab.</summary>
    public static class MepTablet
    {
        static readonly List<(Trade trade, string name, Action draw)> _sections = new List<(Trade, string, Action)>();
        static int _tab;
        static readonly string[] Tabs = { "Plumbing", "Electrical", "HVAC", "Inspection" };

        public static void AddSection(Trade t, string name, Action draw)
        {
            _sections.RemoveAll(s => s.trade == t && s.name == name);
            _sections.Add((t, name, draw));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Tablet.RegisterApp("MEP Plans", Draw);
            Tablet.AddHelp("MEP: route tools - LMB add point (snaps to grid / fittings / boxes), RMB finish, Scroll = material, Shift+Scroll = height, Alt+Scroll = drain slope");
            Tablet.AddHelp("MEP: join tools (crimper, torch, PVC cement, foil tape) - hold LMB on a joint · cut tools - LMB on a run · drill - LMB on a stud, RMB = change bit");
        }

        static GUIStyle L => Tablet.I != null ? Tablet.I.LabelStyle : GUI.skin.label;
        static GUIStyle H => Tablet.I != null ? Tablet.I.HeaderStyle : GUI.skin.label;
        static GUIStyle B => Tablet.I != null ? Tablet.I.ButtonStyle : GUI.skin.button;

        static void Draw()
        {
            _tab = GUILayout.Toolbar(_tab, Tabs, B);
            GUILayout.Space(6);
            if (_tab < 3)
            {
                var t = (Trade)_tab;
                DrawPlan(t);
                foreach (var s in _sections) if (s.trade == t) { GUILayout.Space(8); GUILayout.Label(s.name, H); s.draw?.Invoke(); }
                return;
            }
            GUILayout.BeginHorizontal();
            foreach (Trade t in Enum.GetValues(typeof(Trade)))
                if (GUILayout.Button("Call rough " + t.ToString().ToLower() + " inspection", B)) MepInspection.Run(t);
            if (GUILayout.Button("Clear markers", B)) MepInspection.ClearMarkers();
            GUILayout.EndHorizontal();
            foreach (var kv in MepInspection.LastPassed)
                GUILayout.Label($"{kv.Key}: {(kv.Value ? "PASSED" : "FAILED")}", H);
            foreach (var f in MepInspection.LastFindings) GUILayout.Label("• " + f, L);
            GUILayout.Space(6);
            GUILayout.Label("Rules checked:", H);
            foreach (var r in MepInspection.Rules) GUILayout.Label($"{r.Trade}: {r.Text}", L);
        }

        /// <summary>Top-down schematic of every run of a trade (colored by spec), nodes as dots.</summary>
        static void DrawPlan(Trade t)
        {
            var rect = GUILayoutUtility.GetRect(200, 260, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.08f, 0.14f, 0.24f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            Bounds b = new Bounds(); bool any = false;
            foreach (var r in NetGraph.RunsOf(t)) foreach (var p in r.Points) { if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p); }
            if (!any) { GUI.Label(new Rect(rect.x + 10, rect.y + 10, 400, 30), "Nothing routed yet.", L); return; }
            float span = Mathf.Max(b.size.x, b.size.z, 1f) * 1.15f;
            float sc = Mathf.Min(rect.width, rect.height) / span;
            Vector2 Map(Vector3 p) => new Vector2(rect.center.x + (p.x - b.center.x) * sc, rect.center.y - (p.z - b.center.z) * sc);
            foreach (var r in NetGraph.RunsOf(t))
            {
                Color c = r.Spec.Tint; c.a = 1f;
                if (r.Spec.Surface == SurfaceKind.DarkPlastic) c = new Color(0.5f, 0.5f, 0.5f);
                if (r.Spec.Surface == SurfaceKind.Galvanized) c = new Color(0.75f, 0.8f, 0.85f);
                for (int i = 1; i < r.Points.Count; i++) Line(Map(r.Points[i - 1]), Map(r.Points[i]), Mathf.Clamp(r.Spec.Width * sc, 2f, 10f), c);
            }
            foreach (var n in NetGraph.NodesOf(t))
            {
                var p = Map(n.transform.position);
                GUI.color = new Color(1f, 0.9f, 0.3f);
                GUI.DrawTexture(new Rect(p.x - 3, p.y - 3, 6, 6), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 6, rect.yMax - 22, 300, 20), $"Plan view · {Units.Format(span)} across", L);
        }

        static void Line(Vector2 a, Vector2 b, float w, Color c)
        {
            var m = GUI.matrix;
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            float len = (b - a).magnitude;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.color = c;
            GUI.DrawTexture(new Rect(a.x, a.y - w * 0.5f, len, w), Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = Color.white;
        }
    }

    /// <summary>Creates the MEP systems object at scene start and registers the shared rules.</summary>
    public static class MepBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (UnityEngine.Object.FindAnyObjectByType<MepAutoAttach>() != null) return;
            var go = new GameObject("MEP Systems");
            go.AddComponent<MepAutoAttach>();
            foreach (var a in _systems) a(go);
        }

        static readonly List<Action<GameObject>> _systems = new List<Action<GameObject>>();
        /// <summary>Trades add their MonoBehaviour systems (circuit sim, water sim...) to the "MEP Systems" object.
        /// Call from BeforeSceneLoad.</summary>
        public static void AddSystem(Action<GameObject> add) { _systems.Add(add); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Rules()
        {
            foreach (Trade t in Enum.GetValues(typeof(Trade)))
            {
                var trade = t;
                MepInspection.Register(new MepRule
                {
                    Id = "bored-" + trade, Trade = trade, Text = "Pipes/cables pass through framing only in bored holes",
                    Check = () => StudFindings(trade)
                });
            }
            MepInspection.Register(new MepRule
            {
                Id = "overbored", Trade = Trade.Plumbing, Text = "Bored holes ≤ 60% of stud depth (40% bearing), ≥ 5/8\" from edges (IRC R602.6)",
                Check = OverBoredFindings
            });
            MepInspection.Register(new MepRule
            {
                Id = "overbored-el", Trade = Trade.Electrical, Text = "Bored holes ≤ 60% of stud depth (40% bearing), ≥ 5/8\" from edges (IRC R602.6)",
                Check = OverBoredFindings
            });
        }

        static IEnumerable<Finding> StudFindings(Trade t)
        {
            foreach (var r in new List<NetRun>(NetGraph.RunsOf(t)))
                foreach (var c in StudCheck.Find(r)) yield return new Finding { Note = c.Note, Pos = c.Point };
        }

        static IEnumerable<Finding> OverBoredFindings()
        {
            foreach (var (sh, h, note) in StudCheck.OverBored())
                yield return new Finding { Note = note, Pos = sh.transform.TransformPoint(h.LocalPos) };
        }
    }
}
