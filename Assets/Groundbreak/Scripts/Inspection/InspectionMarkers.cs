using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Red correction flags planted by the inspector at each issue (wire-stake marking flag + a floating red
    /// pointer so it's visible from across the site). Their notes are drawn on screen when the player is near.</summary>
    public class InspectionMarkers : MonoBehaviour
    {
        static InspectionMarkers _i;
        readonly List<(GameObject go, string note, InspectionPhase phase)> _markers = new List<(GameObject, string, InspectionPhase)>();
        static Mesh _flagMesh, _pointerMesh;
        static Material[] _flagMats, _pointerMats;

        public static int Count => _i != null ? _i._markers.Count : 0;
        public static IEnumerable<Vector3> Positions
        {
            get { if (_i != null) foreach (var m in _i._markers) if (m.go != null) yield return m.go.transform.position; }
        }

        static InspectionMarkers Inst()
        {
            if (_i == null) _i = new GameObject("InspectionMarkers").AddComponent<InspectionMarkers>();
            return _i;
        }

        public static void ClearAll()
        {
            if (_i == null) return;
            foreach (var m in _i._markers) if (m.go != null) Destroy(m.go);
            _i._markers.Clear();
        }

        /// <summary>Replaces the markers of this phase with new ones for the given issues.</summary>
        public static void Show(InspectionPhase phase, List<InspectionIssue> issues)
        {
            var me = Inst();
            for (int k = me._markers.Count - 1; k >= 0; k--)
                if (me._markers[k].phase == phase) { if (me._markers[k].go != null) Destroy(me._markers[k].go); me._markers.RemoveAt(k); }
            foreach (var issue in issues)
            {
                if (!issue.HasPosition) continue;
                me._markers.Add((Spawn(issue.Position, issue.Message), issue.Message, phase));
            }
        }

        static void EnsureMeshes()
        {
            if (_flagMesh != null) return;
            var red = new Color(0.86f, 0.08f, 0.06f);
            var k = new LandModel();
            int wire = k.M(SurfaceKind.Galvanized), flag = k.M(red);
            // 36" wire stake with a 4x5" vinyl flag at the top (utility marking flag)
            k.B.AddCylinder(new Vector3(0, Units.In(18), 0), Units.In(0.06f), Units.In(36), 6, wire);
            k.B.AddBox(new Vector3(Units.In(2.5f), Units.In(33), 0), new Vector3(Units.In(5), Units.In(4), Units.In(0.04f)), Quaternion.Euler(0, 0, 0), flag);
            _flagMesh = k.BuildMesh("MarkerFlag"); _flagMats = k.Mats;

            var p = new LandModel();
            int r = p.M(red);
            p.B.AddFrustum(new Vector3(0, 0.15f, 0), Quaternion.identity, 0.0f, 0.12f, 0.3f, 12, r);
            p.B.AddLathe(new Vector3(0, 0.38f, 0), Quaternion.identity, LandModel.BlobProfile(0.07f, 0.14f), 12, r);
            _pointerMesh = p.BuildMesh("MarkerPointer"); _pointerMats = p.Mats;
        }

        static GameObject Spawn(Vector3 pos, string note)
        {
            EnsureMeshes();
            var root = new GameObject("InspectionMarker");
            root.transform.SetParent(Inst().transform, false);
            float ground = pos.y;
            if (Physics.Raycast(pos + Vector3.up * 0.05f, Vector3.down, out var hit, 50f, ~(1 << 2), QueryTriggerInteraction.Ignore)) ground = hit.point.y;
            root.transform.position = new Vector3(pos.x, ground, pos.z);
            var f = new GameObject("Flag");
            f.transform.SetParent(root.transform, false);
            f.transform.localRotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            f.AddComponent<MeshFilter>().sharedMesh = _flagMesh;
            f.AddComponent<MeshRenderer>().sharedMaterials = _flagMats;
            var ptr = new GameObject("Pointer");
            ptr.transform.SetParent(root.transform, false);
            ptr.transform.position = new Vector3(pos.x, Mathf.Max(pos.y, ground) + 0.9f, pos.z);
            ptr.AddComponent<MeshFilter>().sharedMesh = _pointerMesh;
            var mr = ptr.AddComponent<MeshRenderer>(); mr.sharedMaterials = _pointerMats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        void Update()
        {
            float t = Time.time;
            foreach (var m in _markers)
            {
                if (m.go == null) continue;
                var p = m.go.transform.Find("Pointer");
                if (p == null) continue;
                p.Rotate(0, 90f * Time.deltaTime, 0, Space.World);
                var lp = p.localPosition; lp.y = Mathf.Max(0.9f, lp.y) + Mathf.Sin(t * 3f) * 0.002f; p.localPosition = lp;
            }
        }

        GUIStyle _style;
        void OnGUI()
        {
            if (Game.UIOpen || Game.Player == null || Game.Player.Cam == null || _markers.Count == 0) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(12, Screen.height / 55), wordWrap = true, fontStyle = FontStyle.Bold };
                _style.normal.textColor = Color.white;
            }
            var cam = Game.Player.Cam;
            foreach (var m in _markers)
            {
                if (m.go == null) continue;
                Vector3 wp = m.go.transform.position + Vector3.up * 1.25f;
                float d = Vector3.Distance(cam.transform.position, wp);
                if (d > 10f) continue;
                Vector3 sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0) continue;
                float w = 260, h = 48;
                var r = new Rect(sp.x - w / 2, Screen.height - sp.y - h, w, h);
                UISkin.Panel(r, new Color(0.55f, 0.05f, 0.04f, 0.85f));
                GUI.Label(new Rect(r.x + 6, r.y + 2, r.width - 12, r.height - 4), m.note, _style);
            }
        }
    }
}
