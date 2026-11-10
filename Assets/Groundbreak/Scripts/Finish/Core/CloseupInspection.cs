using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Close-up inspections (insulation, drywall/fire, exterior, final finish) as DATA: each rule is an id, phase,
    /// code-style description and a check that returns violations at world positions. Running a phase drops a red
    /// marker (pin + flag + label) at every violation. Features register rules from BeforeSceneLoad initializers.
    /// (If a shared Inspections framework exists, it can wrap <see cref="Rules"/> / <see cref="Run"/>.)
    /// </summary>
    public static class CloseupInspection
    {
        public const string Insulation = "Insulation", Drywall = "Drywall", Exterior = "Exterior", Finish = "Finish";
        public static readonly string[] Phases = { Insulation, Drywall, Exterior, Finish };

        public struct Violation
        {
            public string RuleId, Message;
            public Vector3 Position;
            public Violation(string ruleId, Vector3 pos, string msg) { RuleId = ruleId; Position = pos; Message = msg; }
        }

        public class Rule
        {
            public string Id, Phase, Code, Description;
            public Func<IEnumerable<Violation>> Check;
        }

        public class Result
        {
            public string Phase;
            public readonly List<Violation> Violations = new List<Violation>();
            public int RulesChecked;
            public bool Passed => Violations.Count == 0;
            public override string ToString() => Passed ? $"{Phase} inspection PASSED ({RulesChecked} rules)" : $"{Phase} inspection FAILED: {Violations.Count} item(s)";
        }

        static readonly List<Rule> _rules = new List<Rule>();
        public static IReadOnlyList<Rule> Rules => _rules;
        public static readonly Dictionary<string, Result> Last = new Dictionary<string, Result>();

        public static void Register(Rule r) { _rules.RemoveAll(x => x.Id == r.Id); _rules.Add(r); }
        public static void Register(string id, string phase, string code, string description, Func<IEnumerable<Violation>> check) =>
            Register(new Rule { Id = id, Phase = phase, Code = code, Description = description, Check = check });

        /// <summary>Runs every rule of a phase, replaces that phase's red markers, returns the result.</summary>
        public static Result Run(string phase, bool markers = true)
        {
            var res = new Result { Phase = phase };
            foreach (var r in _rules)
            {
                if (r.Phase != phase) continue;
                res.RulesChecked++;
                try { foreach (var v in r.Check()) res.Violations.Add(v); }
                catch (Exception e) { Debug.LogException(e); }
            }
            Last[phase] = res;
            ClearMarkers(phase);
            if (markers) foreach (var v in res.Violations) SpawnMarker(phase, v);
            Game.Toast(res.ToString());
            return res;
        }

        // ------------------------------------------------------------- red markers

        static readonly Dictionary<string, List<GameObject>> _markers = new Dictionary<string, List<GameObject>>();
        static Material _red;

        public static void ClearMarkers(string phase = null)
        {
            foreach (var kv in _markers)
            {
                if (phase != null && kv.Key != phase) continue;
                foreach (var g in kv.Value) if (g != null) UnityEngine.Object.Destroy(g);
                kv.Value.Clear();
            }
        }

        public static int MarkerCount(string phase) => _markers.TryGetValue(phase, out var l) ? l.FindAll(g => g != null).Count : 0;

        static void SpawnMarker(string phase, Violation v)
        {
            if (_red == null)
            {
                _red = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.05f, 0.04f), hideFlags = HideFlags.DontUnloadUnusedAsset };
                _red.SetFloat("_Glossiness", 0.5f);
                _red.EnableKeyword("_EMISSION"); _red.SetColor("_EmissionColor", new Color(0.5f, 0.02f, 0.02f));
            }
            var root = new GameObject("InspectionMarker_" + v.RuleId);
            root.transform.position = v.Position;
            var b = new MeshBuilder();
            b.AddCylinder(new Vector3(0, 0.12f, 0), 0.006f, 0.24f, 8, 0);                        // pin
            b.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0f, 0.24f), new Vector2(0.025f, 0.26f), new Vector2(0.025f, 0.28f), new Vector2(0f, 0.30f) }, 12, 0); // head
            b.AddBox(new Vector3(0.05f, 0.21f, 0f), new Vector3(0.09f, 0.055f, 0.002f), 0);       // flag
            var go = new GameObject("Visual");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = b.Build("InspectionMarker");
            go.AddComponent<MeshRenderer>().sharedMaterial = _red;
            var lbl = root.AddComponent<InspectionMarkerLabel>(); lbl.Text = v.Message;
            root.AddComponent<FinishLayer>().Kind = "marker";
            if (!_markers.TryGetValue(phase, out var list)) _markers[phase] = list = new List<GameObject>();
            list.Add(root);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState() { _markers.Clear(); Last.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitApp()
        {
            Tablet.RegisterApp("Close-up Inspections", DrawApp);
            Tablet.AddHelp("Close-up Inspections app: call the inspector for insulation, drywall, exterior and finish. Red flags mark every failed item.");
        }

        static void DrawApp()
        {
            var t = Tablet.I; if (t == null) return;
            foreach (var ph in Phases)
            {
                GUILayout.BeginHorizontal();
                int n = 0; foreach (var r in _rules) if (r.Phase == ph) n++;
                GUILayout.Label($"{ph}: {n} rules", t.HeaderStyle, GUILayout.Width(260));
                if (GUILayout.Button("Request inspection", t.ButtonStyle, GUILayout.Width(220))) { var r = Run(ph); t.ShowMessage(r.ToString()); }
                if (GUILayout.Button("Clear flags", t.ButtonStyle, GUILayout.Width(140))) ClearMarkers(ph);
                GUILayout.EndHorizontal();
                if (Last.TryGetValue(ph, out var res))
                {
                    GUILayout.Label(res.ToString(), t.LabelStyle);
                    int shown = 0;
                    foreach (var v in res.Violations) { if (shown++ >= 12) { GUILayout.Label("   …", t.LabelStyle); break; } GUILayout.Label("   ✗ " + v.Message, t.LabelStyle); }
                }
                GUILayout.Space(6);
            }
            GUILayout.Label("Code rules:", t.HeaderStyle);
            foreach (var r in _rules) GUILayout.Label($"   [{r.Phase}] {r.Code}: {r.Description}", t.LabelStyle);
        }
    }

    /// <summary>Floating text over a red inspection flag (only when near and in front of the camera).</summary>
    public class InspectionMarkerLabel : MonoBehaviour
    {
        public string Text;
        static GUIStyle _s;
        void OnGUI()
        {
            if (Game.UIOpen || Game.Player == null || Game.Player.Cam == null) return;
            var cam = Game.Player.Cam;
            Vector3 p = transform.position + Vector3.up * 0.32f;
            if ((p - cam.transform.position).sqrMagnitude > 64f) return;
            var sp = cam.WorldToScreenPoint(p);
            if (sp.z <= 0f) return;
            if (_s == null) { _s = new GUIStyle(GUI.skin.box) { fontSize = 12, wordWrap = true }; _s.normal.textColor = new Color(1f, 0.85f, 0.85f); }
            GUI.Box(new Rect(sp.x - 110, Screen.height - sp.y - 40, 220, 38), Text, _s);
        }
    }
}
