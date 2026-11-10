using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One real-world operation in sequence: "nail stud #7 to the plates", "snap a chalk line", "tilt the wall up".</summary>
    public class BuildStep
    {
        public string Label;
        /// <summary>Tool ids that can perform it (any match). Empty = any tool / no tool.</summary>
        public string[] Tools = new string[0];
        /// <summary>Stock taken when the step is done: lumber ids by piece, consumables by unit.</summary>
        public readonly Dictionary<string, int> Uses = new Dictionary<string, int>();
        public int Nails;
        /// <summary>Hammer swings needed instead of one nailer click (3 per nail).</summary>
        public int Clicks = 1;
        public Action Do;
        /// <summary>World box of what this step builds (for the layout ghost and aiming). Evaluated lazily.</summary>
        public Func<(Vector3 center, Vector3 size, Quaternion rot)> Ghost;

        public BuildStep Use(string id, int n = 1) { if (n > 0) Uses[id] = (Uses.TryGetValue(id, out var k) ? k : 0) + n; return this; }
    }

    public static class ToolSets
    {
        public static readonly string[] Fasten = { "nailer", "hammer" };
        public static readonly string[] Roof = { "roofnailer", "hatchet" };
        public static readonly string[] Staple = { "capstapler", "hammer" };
        public static readonly string[] Saw = { "saw" };
        public static readonly string[] Hands = { "place", "hammer", "nailer", "level", "speedsquare" };
        public static readonly string[] Chalk = { "chalkline" };
        public static readonly string[] Layout = { "speedsquare", "framingsquare", "tape" };
        public static readonly string[] Level = { "level" };
        public static readonly string[] Any = new string[0];
    }

    /// <summary>A planned piece of work (floor deck, a wall, the roof, a roofing layer...) as an ordered list of steps.
    /// The player performs steps with the right tool on the belt (aim at the blue layout ghost and click);
    /// tests and the "crew" option run them all with RunAll(). Stock is consumed per step.</summary>
    public class FramingJob : MonoBehaviour
    {
        public static readonly List<FramingJob> All = new List<FramingJob>();
        /// <summary>When false, steps don't check/take stock (tests that only care about geometry).</summary>
        public static bool RequireStock = true;

        public string Title = "Job";
        public readonly List<BuildStep> Steps = new List<BuildStep>();
        public int Next;
        public bool Done => Next >= Steps.Count;
        public event Action Completed;
        public BuildStep Current => Done ? null : Steps[Next];
        int _clicks;
        GameObject _ghost;

        public static FramingJob Create(string title, Transform parent = null)
        {
            var go = new GameObject("Job: " + title);
            if (parent != null) go.transform.SetParent(parent, false);
            var j = go.AddComponent<FramingJob>();
            j.Title = title;
            return j;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() { All.Remove(this); if (_ghost != null) Destroy(_ghost); }

        public BuildStep Add(string label, string[] tools, Action act, Func<(Vector3, Vector3, Quaternion)> ghost = null)
        {
            var s = new BuildStep { Label = label, Tools = tools ?? ToolSets.Any, Do = act, Ghost = ghost };
            Steps.Add(s);
            return s;
        }

        /// <summary>Total material takeoff for the remaining steps.</summary>
        public Dictionary<string, int> Takeoff(bool remainingOnly = true)
        {
            var t = new Dictionary<string, int>();
            for (int i = remainingOnly ? Next : 0; i < Steps.Count; i++)
                foreach (var kv in Steps[i].Uses) t[kv.Key] = (t.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            return t;
        }

        public static bool ToolFits(BuildStep s, string toolId)
        {
            if (s.Tools == null || s.Tools.Length == 0) return true;
            return Array.IndexOf(s.Tools, toolId) >= 0;
        }

        public string MissingStock(BuildStep s)
        {
            if (!RequireStock) return null;
            foreach (var kv in s.Uses)
                if (Game.Stock.Count(kv.Key) < kv.Value)
                {
                    var d = Catalog.Get(kv.Key);
                    return "Need " + kv.Value + " x " + (d != null ? d.Name : kv.Key) + " (" + Game.Stock.Count(kv.Key) + " on site)";
                }
            if (s.Nails > 0 && Game.Belt != null && Game.Belt.Nails < s.Nails && Game.Stock.Count("nails16d") < s.Nails) return "Out of nails";
            return null;
        }

        /// <summary>Perform the next step with a tool. Returns false with a message if it can't be done.</summary>
        public bool TryAdvance(string toolId, out string msg)
        {
            msg = "";
            var s = Current;
            if (s == null) { msg = Title + " is complete"; return false; }
            if (toolId != null && !ToolFits(s, toolId)) { msg = s.Label + " - use: " + string.Join(" / ", s.Tools); return false; }
            string miss = MissingStock(s);
            if (miss != null) { msg = miss; return false; }
            if (toolId == "hammer" && s.Nails > 0)
            {
                _clicks++;
                if (_clicks < Mathf.Max(3, s.Clicks * 3)) { msg = "Swing " + _clicks; return true; }
            }
            _clicks = 0;
            Perform(s);
            msg = s.Label;
            return true;
        }

        void Perform(BuildStep s)
        {
            if (RequireStock)
            {
                foreach (var kv in s.Uses) Game.Stock.TryTake(kv.Key, kv.Value);
                if (s.Nails > 0)
                {
                    if (Game.Belt != null && Game.Belt.Nails >= s.Nails) Game.Belt.Nails -= s.Nails;
                    else Game.Stock.TryTake("nails16d", s.Nails);
                }
            }
            Next++;
            try { s.Do?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            SupportGraph.Dirty = true;
            if (Done) { Completed?.Invoke(); if (_ghost != null) Destroy(_ghost); }
        }

        /// <summary>Crew mode / tests: do every remaining step (ignores tool choice).</summary>
        public void RunAll()
        {
            int guard = 0;
            while (!Done && guard++ < 100000)
            {
                var s = Current;
                string miss = MissingStock(s);
                if (miss != null) { Debug.LogWarning("[FramingJob] " + Title + ": " + miss); return; }
                Perform(s);
            }
        }

        void LateUpdate()
        {
            var s = Current;
            if (s == null || s.Ghost == null) { if (_ghost != null) _ghost.SetActive(false); return; }
            if (_ghost == null)
            {
                _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _ghost.name = "LayoutGhost";
                _ghost.layer = 2;
                Destroy(_ghost.GetComponent<Collider>());
                var r = _ghost.GetComponent<Renderer>();
                r.sharedMaterial = MaterialLibrary.Ghost(new Color(0.25f, 0.6f, 1f));
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var g = s.Ghost();
            _ghost.SetActive(true);
            _ghost.transform.SetPositionAndRotation(g.center, g.rot);
            _ghost.transform.localScale = g.size + Vector3.one * 0.004f;
            GhostOutline.Update(_ghost);
        }

        public bool GhostVisible => _ghost != null && _ghost.activeSelf;

        /// <summary>Is the player's aim ray close to the current step's ghost?</summary>
        public bool Aimed(Ray ray, float range)
        {
            var s = Current;
            if (s == null) return false;
            if (s.Ghost == null) return true;
            var g = s.Ghost();
            Vector3 local = Quaternion.Inverse(g.rot) * (ray.origin - g.center);
            Vector3 dir = Quaternion.Inverse(g.rot) * ray.direction;
            var b = new Bounds(Vector3.zero, g.size + Vector3.one * 0.15f);
            return b.IntersectRay(new Ray(local, dir), out float d) && d <= range;
        }
    }

    /// <summary>Routes clicks of the right tool to the job whose layout ghost is aimed. Lives on its own object.</summary>
    public class FramingInteraction : MonoBehaviour
    {
        public static FramingInteraction I;
        public string Status = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("FramingSystems");
            go.AddComponent<FramingInteraction>();
            var safety = System.Type.GetType("Groundbreak.RoofSafety"); // provided by the Roofing module
            if (safety != null) go.AddComponent(safety);
            go.AddComponent<InspectionMarkers>();
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public FramingJob AimedJob(float range = 8f)
        {
            if (Game.Player == null) return null;
            var ray = Game.Player.AimRay;
            FramingJob best = null;
            foreach (var j in FramingJob.All)
                if (!j.Done && j.Aimed(ray, range)) { best = j; break; }
            return best;
        }

        void Update()
        {
            Status = "";
            if (Game.UIOpen || Game.Belt == null) return;
            var tool = Game.Belt.Active;
            var job = AimedJob();
            if (job == null) return;
            var s = job.Current;
            Status = job.Title + ": " + s.Label + (FramingJob.ToolFits(s, tool != null ? tool.Id : "") ? "  [LMB]" : "  (needs " + string.Join("/", s.Tools) + ")");
            if (tool == null || tool.Id == "place") { if (!GInput.MouseDown(1)) return; }
            else if (!GInput.MouseDown(0)) return;
            if (!FramingJob.ToolFits(s, tool != null ? tool.Id : "")) return;
            if (!job.TryAdvance(tool != null ? tool.Id : null, out string msg)) Game.Toast(msg);
            else if (!msg.StartsWith("Swing")) Game.Toast(msg);
        }

        GUIStyle _style;
        void OnGUI()
        {
            if (string.IsNullOrEmpty(Status) || Game.UIOpen) return;
            if (_style == null) { _style = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter }; _style.normal.textColor = new Color(0.7f, 0.9f, 1f); }
            GUI.Label(new Rect(Screen.width * 0.5f - 300, Screen.height * 0.5f + 60, 600, 24), Status, _style);
        }
    }
}
