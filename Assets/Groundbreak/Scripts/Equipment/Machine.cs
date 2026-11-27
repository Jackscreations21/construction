using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One frame of operator input. Axes are -1..1. Keyboard layout (see <see cref="Machine.ReadKeys"/>):
    /// W/S Drive, A/D Turn, Left/Right Swing, Up/Down Boom, I/K Arm (stick / telescope), J/L Tool (J curl, L dump/open),
    /// Y/H Hoist, F Action, B Toggle.</summary>
    public struct MachineControls
    {
        public float Drive, Turn, Swing, Boom, Arm, Tool, Hoist;
        public bool Action, Toggle;
        public bool Any => Drive != 0 || Turn != 0 || Swing != 0 || Boom != 0 || Arm != 0 || Tool != 0 || Hoist != 0;
    }

    /// <summary>
    /// Base for operable heavy equipment (Scripts/Equipment). Climb in with E (like the work van), operate with keyboard
    /// "joysticks" through GInput, V toggles cab / outside camera, E climbs out. While operating, the on-foot rig is switched off
    /// and the player rides in the seat. The root and its colliders sit on layer 2 (stops the player, ignored by aim and
    /// ground rays). Subclasses build real-size meshes with moving child parts and implement <see cref="Operate"/>.
    /// </summary>
    public abstract class Machine : MonoBehaviour
    {
        public static readonly List<Machine> All = new List<Machine>();
        public static Machine Current { get; private set; }          // the machine the player is operating, or null

        public abstract string TypeId { get; }
        public abstract string DisplayName { get; }
        /// <summary>Operator seat (player feet) and eye, in SeatFrame local space.</summary>
        protected abstract Vector3 SeatLocal { get; }
        protected abstract Vector3 EyeLocal { get; }
        protected virtual Transform SeatFrame => transform;
        protected abstract string ControlHint { get; }
        /// <summary>Lines for the bottom-right gauge panel.</summary>
        protected virtual string Readout => "";
        /// <summary>Box (root local) used for collisions while travelling and for aiming at the machine to climb in.</summary>
        public Vector3 BodyCenter = new Vector3(0, 1, 0), BodySize = new Vector3(2, 2, 3);
        /// <summary>Ground contact footprint half extents (x, z) for the 4-point ground follow.</summary>
        protected Vector2 Footprint = new Vector2(0.8f, 1.0f);
        public float TravelSpeed = 1.5f, TurnRate = 30f, ChaseDistance = 9f;
        protected float EngineBasePitch = 0.8f;

        public bool Operating { get; private set; }
        public float Yaw => _yaw;
        public float Speed => _speed;
        public bool ChaseCamera;
        /// <summary>Input override for tests / scripted moves (used even when nobody is in the seat).</summary>
        public MachineControls? Auto;
        /// <summary>The trailer it came on (removed when the machine is first operated or returned).</summary>
        public GameObject Trailer;
        public bool Rented;

        protected float _yaw, _speed, _y, _pitch, _roll, _lookYaw, _lookPitch;
        protected float _hydraulicLoad;                               // 0..1 engine load for the sound
        AudioSource _engine;
        bool _rigOn, _beltOn, _feelOn, _ccOn, _vmWas; int _beltSlot = -1;
        static readonly RaycastHit[] _hits = new RaycastHit[24];
        static readonly Collider[] _cols = new Collider[16];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { All.Clear(); Current = null; }

        // ------------------------------------------------------------------ spawning

        public static T Spawn<T>(Vector3 pos, float yaw) where T : Machine
        {
            var go = new GameObject(typeof(T).Name) { layer = 2 };
            var m = go.AddComponent<T>();
            m.Init();
            m.TeleportTo(pos, yaw);
            return m;
        }

        void Init()
        {
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            Build();
            SetLayerRecursive(gameObject, 2);
            _engine = gameObject.AddComponent<AudioSource>();
            _engine.clip = Sfx.Clip("engine"); _engine.loop = true; _engine.playOnAwake = false; _engine.spatialBlend = 0.6f;
            _engine.rolloffMode = AudioRolloffMode.Linear; _engine.maxDistance = 45f; _engine.dopplerLevel = 0f;
        }

        protected abstract void Build();
        /// <summary>Move joints and do work for one frame. Called every frame (c is empty when nobody operates and no Auto).</summary>
        protected abstract void Operate(float dt, MachineControls c);
        protected virtual bool CanTravel(out string why) { why = null; return true; }
        protected virtual bool CanExit(out string why) { why = null; return true; }
        /// <summary>Joint positions for the save file.</summary>
        public virtual float[] Joints { get => new float[0]; set { } }
        protected virtual void OnEntered() { }

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);

        protected virtual void OnDestroy()
        {
            if (Operating) Leave(false);
            if (Trailer != null) Destroy(Trailer);
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        /// <summary>Adds a child part with its own mesh (pivot at local pos/rot of the parent).</summary>
        protected static Transform Part(Transform parent, string name, Vector3 localPos, Quaternion localRot, System.Action<MeshBuilder, CTool.Mats> author)
        {
            GameObject go;
            if (author != null) go = CTool.Build(name, author, parent);
            else { go = new GameObject(name); go.transform.SetParent(parent, false); }
            go.transform.localPosition = localPos; go.transform.localRotation = localRot;
            return go.transform;
        }

        protected BoxCollider AddBox(Vector3 center, Vector3 size, Transform on = null)
        {
            var bc = (on != null ? on.gameObject : gameObject).AddComponent<BoxCollider>();
            bc.center = center; bc.size = size;
            return bc;
        }

        public void TeleportTo(Vector3 pos, float yaw)
        {
            _speed = 0; _yaw = yaw; _y = pos.y; _pitch = _roll = 0;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            FollowGround(1f, true);
            AfterMove();
            Physics.SyncTransforms();
        }

        /// <summary>Called after the machine was moved (teleport, travel step): refresh world-space visuals such as cables.</summary>
        protected virtual void AfterMove() { }

        // ------------------------------------------------------------------ getting in and out

        public void Enter()
        {
            var p = Game.Player;
            if (Operating || p == null) return;
            if (Current != null && Current != this) return;
            var belt = p.GetComponent<ToolBelt>(); var feel = p.GetComponent<PlayerFeel>(); var cc = p.GetComponent<CharacterController>();
            _rigOn = p.enabled; _beltOn = belt != null && belt.enabled; _feelOn = feel != null && feel.enabled; _ccOn = cc != null && cc.enabled;
            _vmWas = ViewModel.Enabled;
            _beltSlot = -1;
            if (belt != null && belt.Active != null)
                for (int i = 0; i < ToolBelt.SlotCount; i++)
                    if (belt.Slots[i] == null) { _beltSlot = belt.ActiveIndex; belt.Select(i); break; }
            p.enabled = false;
            if (belt != null) belt.enabled = false;
            if (feel != null) feel.enabled = false;
            if (cc != null) cc.enabled = false;
            ViewModel.Enabled = false;
            Operating = true; Current = this; _lookYaw = _lookPitch = 0;
            TransitionFrame = Time.frameCount;
            if (Trailer != null) { Destroy(Trailer); Trailer = null; }
            if (_engine != null && _engine.clip != null) _engine.Play();
            Sfx.Play("thud", transform.position + Vector3.up, 0.4f);
            OnEntered();
            SeatPlayer();
        }

        public void Exit()
        {
            if (!Operating) return;
            if (Mathf.Abs(_speed) > 0.3f) { Game.Toast("Stop first"); return; }
            if (!CanExit(out string why)) { Game.Toast(why); return; }
            Leave(true);
        }

        /// <summary>Puts the player back on foot. placeBeside = next to the machine, else where the seat is.</summary>
        /// <summary>Frame of the last climb in/out: the same E press must not also climb back out/in (Update order is undefined).</summary>
        public static int TransitionFrame = -1;

        protected void Leave(bool placeBeside, Vector3? at = null)
        {
            Operating = false; _speed = 0;
            TransitionFrame = Time.frameCount;
            if (Current == this) Current = null;
            if (_engine != null) _engine.Stop();
            var p = Game.Player;
            ViewModel.Enabled = _vmWas;
            if (p == null) return;
            var belt = p.GetComponent<ToolBelt>(); var feel = p.GetComponent<PlayerFeel>(); var cc = p.GetComponent<CharacterController>();
            if (at.HasValue) p.transform.position = at.Value + Vector3.up * 0.05f;
            else if (placeBeside) p.transform.position = ExitSpot() + Vector3.up * 0.05f;
            p.SetLook(placeBeside ? _yaw : p.transform.eulerAngles.y, 0f);
            if (p.Cam != null) p.Cam.transform.localPosition = new Vector3(0f, PlayerRig.EyeHeight, 0f);
            p.enabled = _rigOn;
            if (belt != null) { belt.enabled = _beltOn; if (_beltSlot >= 0) belt.Select(_beltSlot); }
            if (feel != null) feel.enabled = _feelOn;
            if (cc != null) cc.enabled = _ccOn;
            _beltSlot = -1;
            Physics.SyncTransforms();
        }

        /// <summary>Hands the player back without the machine (Despawn / tests).</summary>
        public void ForceLeave() { if (Operating) Leave(true); }

        Vector3 ExitSpot()
        {
            float hx = BodySize.x * 0.5f + 0.7f, hz = BodySize.z * 0.5f + 0.7f;
            Vector3[] local =
            {
                new Vector3(-hx, 0, 0.3f), new Vector3(hx, 0, 0.3f), new Vector3(-hx, 0, -hz * 0.6f), new Vector3(hx, 0, -hz * 0.6f),
                new Vector3(0, 0, -hz), new Vector3(0, 0, hz), new Vector3(-hx - 1f, 0, 0), new Vector3(hx + 1f, 0, 0),
            };
            var flat = Quaternion.Euler(0, _yaw, 0);
            Vector3 first = Vector3.zero;
            for (int i = 0; i < local.Length; i++)
            {
                Vector3 w = transform.position + flat * (local[i] + new Vector3(BodyCenter.x, 0, BodyCenter.z));
                w.y = Ground(w, _y);
                if (i == 0) first = w;
                if (!Physics.CheckCapsule(w + Vector3.up * 0.4f, w + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return w;
            }
            return first;
        }

        protected void SeatPlayer()
        {
            var p = Game.Player; if (p == null) return;
            var f = SeatFrame;
            p.transform.SetPositionAndRotation(f.TransformPoint(SeatLocal), Quaternion.Euler(0, f.eulerAngles.y, 0));
            if (p.Cam == null) return;
            if (!ChaseCamera)
                p.Cam.transform.SetPositionAndRotation(f.TransformPoint(EyeLocal), Quaternion.Euler(0, f.eulerAngles.y, 0) * Quaternion.Euler(_lookPitch, _lookYaw, 0));
            else
            {
                var r = Quaternion.Euler(18f + _lookPitch, f.eulerAngles.y + _lookYaw, 0);
                Vector3 focus = transform.position + Vector3.up * 1.6f;
                p.Cam.transform.SetPositionAndRotation(focus - r * Vector3.forward * ChaseDistance, r);
            }
        }

        // ------------------------------------------------------------------ frame

        public static MachineControls ReadKeys()
        {
            var c = new MachineControls();
            if (GInput.GetKey(KeyCode.W)) c.Drive += 1; if (GInput.GetKey(KeyCode.S)) c.Drive -= 1;
            if (GInput.GetKey(KeyCode.D)) c.Turn += 1; if (GInput.GetKey(KeyCode.A)) c.Turn -= 1;
            if (GInput.GetKey(KeyCode.RightArrow)) c.Swing += 1; if (GInput.GetKey(KeyCode.LeftArrow)) c.Swing -= 1;
            if (GInput.GetKey(KeyCode.UpArrow)) c.Boom += 1; if (GInput.GetKey(KeyCode.DownArrow)) c.Boom -= 1;
            if (GInput.GetKey(KeyCode.I)) c.Arm += 1; if (GInput.GetKey(KeyCode.K)) c.Arm -= 1;
            if (GInput.GetKey(KeyCode.L)) c.Tool += 1; if (GInput.GetKey(KeyCode.J)) c.Tool -= 1;
            if (GInput.GetKey(KeyCode.Y)) c.Hoist += 1; if (GInput.GetKey(KeyCode.H)) c.Hoist -= 1;
            c.Action = GInput.GetKeyDown(KeyCode.F);
            c.Toggle = GInput.GetKeyDown(KeyCode.B);
            return c;
        }

        /// <summary>True while this machine's own seat logic owns the camera (false lets e.g. the boom lift hand back the rig).</summary>
        protected virtual bool SeatedNow => Operating;

        protected virtual void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            MachineControls c = default;
            if (Auto.HasValue) c = Auto.Value;
            else if (SeatedNow && !Game.UIOpen && !MainMenu.Showing) c = ReadKeys();
            if (SeatedNow && !Game.UIOpen)
            {
                if (GInput.GetKeyDown(KeyCode.V)) ChaseCamera = !ChaseCamera;
                if (GInput.GetKeyDown(KeyCode.E) && Time.frameCount != TransitionFrame) { Exit(); if (!Operating) return; }
                if (Cursor.lockState == CursorLockMode.Locked && Game.Player != null)
                {
                    Vector2 d = GInput.MouseDelta * Game.Player.Sensitivity;
                    _lookYaw = Mathf.Clamp(_lookYaw + d.x, -170f, 170f);
                    _lookPitch = Mathf.Clamp(_lookPitch - d.y, -70f, 70f);
                }
            }

            bool moved = TravelStep(dt, c.Drive, c.Turn);
            _hydraulicLoad = 0f;
            Operate(dt, c);
            if (moved) FollowGround(dt, false);
            Vector3 pos = transform.position; pos.y = _y;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(_pitch, _yaw, _roll));
            if (moved) AfterMove();

            if (_engine != null && _engine.isPlaying)
            {
                float load = Mathf.Clamp01(Mathf.Max(_hydraulicLoad, Mathf.Abs(_speed) / Mathf.Max(0.1f, TravelSpeed)));
                _engine.pitch = EngineBasePitch + load * 0.45f;
                _engine.volume = (0.16f + 0.16f * load) * GameSettings.MasterVolume;
            }
        }

        protected virtual void LateUpdate() { if (SeatedNow) SeatPlayer(); }

        bool TravelStep(float dt, float drive, float turn)
        {
            if ((drive != 0 || turn != 0) && !CanTravel(out string why))
            {
                if (Operating && Time.frameCount % 90 == 0) Game.Toast(why);
                drive = turn = 0;
            }
            _speed = Mathf.MoveTowards(_speed, drive * TravelSpeed, (drive != 0 ? 1.5f : 3f) * dt);
            float yawRate = turn * TurnRate * (drive < -0.1f ? -1f : 1f);
            if (Mathf.Abs(_speed) < 1e-4f && Mathf.Abs(yawRate) < 1e-3f) return false;
            if (yawRate != 0 || _speed != 0) _hydraulicLoad = Mathf.Max(_hydraulicLoad, 0.6f);
            float newYaw = _yaw + yawRate * dt;
            Vector3 cur = transform.position; cur.y = _y;
            Vector3 next = cur + Quaternion.Euler(0, newYaw, 0) * Vector3.forward * (_speed * dt);
            if (Blocked(next, newYaw) && !Blocked(cur, _yaw))
            {
                if (Mathf.Abs(_speed) > 0.5f) Sfx.Play("thud", cur + Vector3.up, 0.5f);
                _speed = 0; return false;
            }
            _yaw = Mathf.Repeat(newYaw, 360f);
            transform.position = next;
            return true;
        }

        bool Blocked(Vector3 pos, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            Vector3 c = BodyCenter; float lift = 0.4f;
            Vector3 size = BodySize; size.y -= lift; c.y += lift * 0.5f;
            int n = Physics.OverlapBoxNonAlloc(pos + rot * c, size * 0.5f - Vector3.one * 0.03f, _cols, rot, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = _cols[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                if (col is MeshCollider || col is TerrainCollider) continue;   // ground meshes (pad, lot field)
                if (Game.Player != null && col.transform.IsChildOf(Game.Player.transform)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Highest ground under p that the machine can climb (within 0.5 m of refY), else refY. Ignores this machine.</summary>
        protected float Ground(Vector3 p, float refY)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, refY + 1.4f, p.z), Vector3.down, _hits, 4.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform)) continue;
                float y = _hits[i].point.y;
                if (y <= refY + 0.5f && y > best) best = y;
            }
            return float.IsNegativeInfinity(best) ? refY : best;
        }

        protected void FollowGround(float dt, bool snap)
        {
            var flat = Quaternion.Euler(0, _yaw, 0);
            Vector3 c = transform.position;
            float hx = Footprint.x, hz = Footprint.y;
            float fl = Ground(c + flat * new Vector3(-hx, 0, hz), _y), fr = Ground(c + flat * new Vector3(hx, 0, hz), _y);
            float rl = Ground(c + flat * new Vector3(-hx, 0, -hz), _y), rr = Ground(c + flat * new Vector3(hx, 0, -hz), _y);
            float front = (fl + fr) * 0.5f, rear = (rl + rr) * 0.5f;
            float y = Mathf.Max((front + rear) * 0.5f, Mathf.Max(Mathf.Max(fl, fr), Mathf.Max(rl, rr)) - 0.15f);
            float pitch = -Mathf.Atan2(front - rear, hz * 2f) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2((fl + rl) * 0.5f - (fr + rr) * 0.5f, hx * 2f) * Mathf.Rad2Deg;
            float k = snap ? 1f : 1f - Mathf.Exp(-10f * dt);
            _y = Mathf.Lerp(_y, y, k); _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(pitch, -20, 20), k); _roll = Mathf.Lerp(_roll, Mathf.Clamp(roll, -15, 15), k);
            if (snap) { c.y = _y; transform.SetPositionAndRotation(c, Quaternion.Euler(_pitch, _yaw, _roll)); }
        }

        /// <summary>Settles onto the ground now (after the ground under it changed).</summary>
        public void Settle() { FollowGround(1f, true); }

        /// <summary>Is the ray aimed at this machine within reach?</summary>
        public bool Aimed(Ray ray, float reach, out float dist)
        {
            return VanSystems.RayBox(ray, transform, BodyCenter, BodySize + Vector3.one * 0.2f, reach, out dist, out _);
        }

        // ------------------------------------------------------------------ HUD

        GUIStyle _hint, _panel; int _styleH;

        protected virtual void OnGUI()
        {
            if (!Operating || Game.UIOpen) return;
            if (_hint == null || _styleH != Screen.height)
            {
                _styleH = Screen.height;
                int fs = Mathf.Max(11, Mathf.RoundToInt(Screen.height / 52f));
                _hint = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.UpperCenter, wordWrap = true, fontStyle = FontStyle.Bold };
                _panel = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.UpperLeft, wordWrap = true };
                _panel.normal.textColor = Color.white;
            }
            string hint = DisplayName + ":  " + ControlHint + "   ·   V camera   E climb out";
            var hr = new Rect(Screen.width * 0.08f, 10f, Screen.width * 0.84f, _hint.fontSize * 3.4f);
            _hint.normal.textColor = new Color(0, 0, 0, 0.85f);
            GUI.Label(new Rect(hr.x + 1, hr.y + 1, hr.width, hr.height), hint, _hint);
            _hint.normal.textColor = Color.white;
            GUI.Label(hr, hint, _hint);
            string ro = Readout;
            if (!string.IsNullOrEmpty(ro))
            {
                int lines = 1; foreach (char ch in ro) if (ch == '\n') lines++;
                float w = _panel.fontSize * 19f, h = _panel.fontSize * 1.45f * lines + 14f;
                var r = new Rect(Screen.width - w - 12f, Screen.height - h - 12f, w, h);
                UISkin.Panel(r);
                GUI.Label(new Rect(r.x + 8, r.y + 6, w - 16, h - 8), ro, _panel);
            }
        }
    }
}
