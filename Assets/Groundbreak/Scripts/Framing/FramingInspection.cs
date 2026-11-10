using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Groundbreak
{
    public class InspectionIssue
    {
        public StructuralMember Member;
        public StructuralStatus Severity;
        public string Note;
        public Vector3 Where;
    }

    /// <summary>Framing inspection: walks the support graph and the rules (load path, connectors, O.C. spacing,
    /// braced walls, flashed openings) and lists failures. InspectionMarkers shows them as red/yellow flags.</summary>
    public static class FramingInspection
    {
        public static readonly List<InspectionIssue> Last = new List<InspectionIssue>();
        public static bool HasRun;

        public static List<InspectionIssue> Run()
        {
            Last.Clear();
            SupportGraph.Rebuild();
            var grounded = SupportGraph.Grounded();
            foreach (var m in SupportGraph.Members)
            {
                if (m == null || !m.isActiveAndEnabled) continue;
                var st = SupportGraph.Evaluate(m, grounded);
                if (st == StructuralStatus.Ok) continue;
                foreach (var n in m.Notes)
                    Last.Add(new InspectionIssue { Member = m, Severity = st, Note = m.Role + ": " + n, Where = m.transform.position });
            }
            foreach (var w in WallAssembly.All)
                if (w.State == WallState.Standing)
                    Last.Add(new InspectionIssue { Severity = StructuralStatus.Warning, Note = w.Spec.Name + ": not plumbed and braced", Where = w.transform.TransformPoint(new Vector3(w.Length / 2, 2f, 0)) });
            foreach (var ro in RoughOpening.All)
                if (ro.Installed != null && (!ro.Flashed || !ro.Fastened))
                    Last.Add(new InspectionIssue { Severity = StructuralStatus.Warning, Note = ro.name + ": " + (!ro.Flashed ? "not flashed" : "not fastened"), Where = ro.Center });
            HasRun = true;
            return Last;
        }

        public static bool Passed => HasRun && Last.TrueForAll(i => i.Severity == StructuralStatus.Ok);

        public static string Report()
        {
            var sb = new StringBuilder();
            int red = 0, yellow = 0;
            foreach (var i in Last) { if (i.Severity == StructuralStatus.Unsupported) red++; else yellow++; }
            sb.AppendLine(Last.Count == 0 ? "PASSED - framing inspection" : $"FAILED - {red} structural, {yellow} corrections");
            int shown = 0;
            foreach (var i in Last) { if (shown++ >= 40) { sb.AppendLine("..."); break; } sb.AppendLine((i.Severity == StructuralStatus.Unsupported ? "[X] " : "[!] ") + i.Note); }
            return sb.ToString();
        }
    }

    /// <summary>Red (structural) and yellow (correction) inspection flags in the world; aim at one to read its note.
    /// Key I runs the inspection on demand.</summary>
    public class InspectionMarkers : MonoBehaviour
    {
        readonly List<GameObject> _flags = new List<GameObject>();
        string _aimNote = "";

        void Update()
        {
            if (!Game.UIOpen && GInput.GetKeyDown(KeyCode.I)) { FramingInspection.Run(); Refresh(); Game.Toast(FramingInspection.Last.Count == 0 ? "Framing inspection PASSED" : "Inspection: " + FramingInspection.Last.Count + " items flagged"); }
            _aimNote = "";
            if (Game.Player == null || _flags.Count == 0) return;
            var ray = Game.Player.AimRay;
            float best = 0.25f;
            for (int i = 0; i < _flags.Count && i < FramingInspection.Last.Count; i++)
            {
                Vector3 p = _flags[i].transform.position;
                float along = Vector3.Dot(p - ray.origin, ray.direction);
                if (along < 0 || along > 12f) continue;
                float d = Vector3.Cross(ray.direction, p - ray.origin).magnitude;
                if (d < best) { best = d; _aimNote = FramingInspection.Last[i].Note; }
            }
        }

        public void Refresh()
        {
            foreach (var f in _flags) if (f != null) Destroy(f);
            _flags.Clear();
            var red = ShellMaterials.Flat("insp-red", new Color(0.9f, 0.08f, 0.06f), 0.4f, 0.02f);
            var yellow = ShellMaterials.Flat("insp-yellow", new Color(1f, 0.8f, 0.05f), 0.4f, 0.02f);
            foreach (var i in FramingInspection.Last)
            {
                var mat = i.Severity == StructuralStatus.Unsupported ? red : yellow;
                var go = Framer.AttachMesh(null, "InspectionFlag", (MeshBuilder b, System.Func<Material, int> sub) =>
                {
                    int g = sub(mat), wire = sub(ShellMaterials.Flat("insp-wire", new Color(0.3f, 0.3f, 0.3f)));
                    b.AddCylinder(new Vector3(0, 0.15f, 0), Units.In(0.06f), 0.3f, 6, wire);
                    b.AddBox(new Vector3(0.045f, 0.26f, 0), new Vector3(0.09f, 0.07f, 0.002f), Quaternion.identity, g);
                });
                go.transform.position = i.Where + Vector3.up * 0.05f;
                _flags.Add(go);
            }
        }

        GUIStyle _s;
        void OnGUI()
        {
            if (string.IsNullOrEmpty(_aimNote) || Game.UIOpen) return;
            if (_s == null) { _s = new GUIStyle(GUI.skin.box) { fontSize = 14, wordWrap = true }; _s.normal.textColor = new Color(1f, 0.85f, 0.6f); }
            GUI.Box(new Rect(Screen.width * 0.5f + 20, Screen.height * 0.5f - 60, 380, 44), "Inspector: " + _aimNote, _s);
        }
    }
}
