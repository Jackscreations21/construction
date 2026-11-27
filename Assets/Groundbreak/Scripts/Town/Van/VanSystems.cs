using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Glue for the work van: spawns it when the town exists and the company owns one, removes it with the town,
    /// saves/loads it (SaveSystem key "van"), and handles the on-foot E interactions (drive, rear doors, ladder) with their prompts.</summary>
    public class VanSystems : MonoBehaviour
    {
        public static VanSystems I;
        /// <summary>The interaction prompt currently shown ("E: drive van"), or null.</summary>
        public static string Prompt { get; private set; }

        static bool _hasPending, _pendRear; static Vector3 _pendPos; static float _pendYaw;
        static bool _pendExit;   // the save was made while driving: the loaded player stands in the seat, step them out
        float _nextPoll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { I = null; Prompt = null; _hasPending = false; _pendExit = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension("van", Save, Load);
            Company.Changed += Refresh;
            Town.Built += Refresh;
            Town.Destroyed += Remove;
            SaveSystem.AfterLoad += Refresh;
            var go = new GameObject("VanSystems");
            DontDestroyOnLoad(go);
            I = go.AddComponent<VanSystems>();
        }

        static void Refresh()
        {
            bool fromSave = _hasPending;
            var v = WorkVan.EnsureSpawned();
            if (fromSave && !_hasPending)   // the van was just placed where the save left it
            {
                bool exit = _pendExit; _pendExit = false;
                if (exit && v != null && !v.Driving) v.PlacePlayerBeside();
            }
        }
        static void Remove() { WorkVan.Despawn(); TownLadder.ResetState(); }

        /// <summary>Pose a loaded save wants the van at (used once by <see cref="WorkVan.EnsureSpawned"/>).</summary>
        public static bool TakePending(ref Vector3 pos, ref float yaw, ref bool rearOpen)
        {
            if (!_hasPending) return false;
            _hasPending = false; pos = _pendPos; yaw = _pendYaw; rearOpen = _pendRear;
            return true;
        }

        // ------------------------------------------------------------------ save

        [Serializable] class CargoEntry { public string id; public int n; }
        [Serializable]
        class Data
        {
            public bool hasVan, rearOpen, driving; public Vector3 pos; public float yaw;
            public List<CargoEntry> cargo = new List<CargoEntry>();
            public int ladder; public Vector3 ladFoot, ladAway; public float ladTop;
        }

        static string Save()
        {
            var d = new Data();
            var v = WorkVan.I;
            if (v != null) { d.hasVan = true; d.pos = v.transform.position; d.yaw = v.Yaw; d.rearOpen = v.RearOpen; d.driving = v.Driving; }
            foreach (var kv in VanCargo.Entries) d.cargo.Add(new CargoEntry { id = kv.Key, n = kv.Value });
            var lad = TownLadder.Placed;
            if (lad != null && TownLadder.State == TownLadder.LadderState.Placed)
            {
                d.ladder = 1; d.ladFoot = lad.Foot; d.ladAway = lad.Away; d.ladTop = lad.TopHeight;   // a carried ladder saves as "on the rack"
            }
            if (!d.hasVan && d.cargo.Count == 0 && d.ladder == 0) return "";
            return JsonUtility.ToJson(d);
        }

        static void Load(string json)
        {
            WorkVan.Despawn(true);
            TownLadder.ResetState();
            _hasPending = false; _pendExit = false;
            if (string.IsNullOrEmpty(json)) { VanCargo.Clear(); return; }     // the van respawns at home when the town + company have one
            var d = JsonUtility.FromJson<Data>(json);
            var list = new List<KeyValuePair<string, int>>();
            foreach (var c in d.cargo) list.Add(new KeyValuePair<string, int>(c.id, c.n));
            VanCargo.SetAll(list);
            if (d.hasVan) { _hasPending = true; _pendPos = d.pos; _pendYaw = d.yaw; _pendRear = d.rearOpen; _pendExit = d.driving; }
            if (d.ladder == 1) TownLadder.Place(d.ladFoot, d.ladAway, d.ladTop);
            Refresh();
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Ray against a box given in a transform's local space (no colliders involved). localHit is in that local space.</summary>
        public static bool RayBox(Ray ray, Transform t, Vector3 center, Vector3 size, float maxDist, out float dist, out Vector3 localHit)
        {
            Vector3 o = t.InverseTransformPoint(ray.origin) - center, d = t.InverseTransformDirection(ray.direction), h = size * 0.5f;
            float tmin = 0f, tmax = maxDist;
            dist = 0f; localHit = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(d[i]) < 1e-6f) { if (o[i] < -h[i] || o[i] > h[i]) return false; continue; }
                float t1 = (-h[i] - o[i]) / d[i], t2 = (h[i] - o[i]) / d[i];
                if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
                tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) return false;
            }
            dist = tmin; localHit = o + d * tmin + center;
            return true;
        }

        // ------------------------------------------------------------------ watcher + on-foot interaction

        void Update()
        {
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 0.5f;
                bool want = Town.Exists && Company.Owns(WorkVan.TypeId);
                if (WorkVan.I == null) { if (want) WorkVan.EnsureSpawned(); }
                else if (!want) Remove();
            }
            Prompt = Interact();
        }

        static string Interact()
        {
            var p = Game.Player; var van = WorkVan.I;
            if (p == null || Game.UIOpen || MainMenu.Showing || !p.enabled) return null;
            if (van != null && van.Driving) return null;
            var lad = TownLadder.Placed;
            if (lad != null && lad.Climbing) return "W / S: climb    Space: let go";

            Ray ray = p.AimRay;
            bool world = p.TryAim(out RaycastHit wh, 6f);
            float worldDist = world ? wh.distance : float.MaxValue;
            bool e = GInput.GetKeyDown(KeyCode.E) && Time.frameCount != WorkVan.TransitionFrame;   // not the press that just got out

            if (lad != null && lad.Aimed(ray, TownLadder.Reach, out float ld) && ld < worldDist + 0.3f)
            {
                if (e) TownLadder.Take();
                return "E: take the ladder    (walk into it + W to climb)";
            }
            if (van != null)
            {
                var part = van.AimPart(ray, out float vd);
                if (part != WorkVan.Part.None && vd < worldDist + 0.05f)
                {
                    if (TownLadder.Carried)
                    {
                        if (e) TownLadder.Stow();
                        return "E: put the ladder on the rack";
                    }
                    switch (part)
                    {
                        case WorkVan.Part.Rack:
                            if (!van.LadderOnRack) return null;
                            if (e) TownLadder.Take();
                            return "E: take the 24' extension ladder";
                        case WorkVan.Part.RearDoors:
                            if (e) van.ToggleRearDoors();
                            return van.RearOpen ? "E: close rear doors" : "E: open rear doors";
                        default:
                            if (vd > 3f) return null;
                            if (e) van.Enter();
                            return "E: drive van";
                    }
                }
            }
            if (TownLadder.Carried)
            {
                if (world && worldDist <= TownLadder.Reach)
                {
                    if (e && !TownLadder.TryPlaceAt(wh.point, wh.normal, p.AimRay.direction, out string why)) Game.Toast(why);
                    return "Carrying ladder    E: stand it against this wall / eave";
                }
                return "Carrying ladder    aim at a wall or eave within 13'";
            }
            return null;
        }

        GUIStyle _style; int _styleH;

        void OnGUI()
        {
            if (string.IsNullOrEmpty(Prompt) || Game.UIOpen) return;
            if (_style == null || _styleH != Screen.height)
            {
                _styleH = Screen.height;
                _style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 42f)), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
            }
            var r = new Rect(0f, Screen.height * 0.58f, Screen.width, _style.fontSize * 1.6f);
            _style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), Prompt, _style);
            _style.normal.textColor = Color.white;
            GUI.Label(r, Prompt, _style);
        }
    }
}
