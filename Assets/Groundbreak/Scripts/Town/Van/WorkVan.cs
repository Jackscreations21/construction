using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// The player's drivable cargo van (Handyman stage). Simple arcade driving through GInput: W/S throttle and reverse,
    /// A/D steer, Space brake, V chase camera, E get in / out. Follows the ground with downward raycasts and is stopped by
    /// anything taller than a curb. While driving, the on-foot rig (PlayerRig, ToolBelt, CharacterController...) is switched off
    /// and the player object rides in the driver's seat. See Docs/TOWN.md.
    /// </summary>
    public class WorkVan : MonoBehaviour
    {
        public static WorkVan I;
        public const string TypeId = "van";
        public const float TopSpeed = 35f * Mph, TopReverse = 8f * Mph, MaxSteerDeg = 38f;
        public const float Mph = 0.44704f;                 // m/s per mph
        public const float FastTravelMph = 25f;
        public enum Part { None, Cab, RearDoors, Rack }

        static readonly Vector3 SeatLocal = new Vector3(-0.48f, 0.66f, 0.95f), EyeLocal = new Vector3(-0.48f, 1.68f, 0.92f);
        static readonly Vector3 BodyCenter = new Vector3(0f, 1.24f, 0f), BodySize = new Vector3(WorkVanModel.Width, 1.72f, 5.96f);
        static readonly Vector3 RackCenter = new Vector3(0f, 2.30f, -0.9f), RackSize = new Vector3(2.0f, 0.40f, 4.0f);
        static readonly RaycastHit[] _hits = new RaycastHit[16];
        static readonly Collider[] _cols = new Collider[8];

        public bool Driving { get; private set; }
        public float SpeedMph => Mathf.Abs(_speed) / Mph;
        /// <summary>Signed speed in m/s (negative in reverse).</summary>
        public float Speed => _speed;
        public float Yaw => _yaw;
        public bool RearOpen { get; private set; }
        public bool ChaseCamera;
        /// <summary>True while the extension ladder is strapped to the roof rack.</summary>
        public bool LadderOnRack => TownLadder.State == TownLadder.LadderState.OnRack;

        float _speed, _steerDeg, _yaw, _y, _pitch, _roll, _doorT, _lookYaw, _lookPitch, _settleT;
        bool _auto, _aBrake; float _aThrottle, _aSteer;
        Transform _doorL, _doorR, _wheelFL, _wheelFR, _steering;
        GameObject _rackLadder, _cargo;
        AudioSource _engine;
        // on-foot rig state while driving
        bool _rigOn, _beltOn, _feelOn, _ccOn, _vmWas; int _beltSlot = -1;

        // ------------------------------------------------------------------ spawning

        /// <summary>Spawns the van in the home driveway (or where a loaded save left it) when the town exists and the company owns one.</summary>
        public static WorkVan EnsureSpawned()
        {
            if (I == null && Town.Exists && Company.Owns(TypeId))
            {
                Vector3 pos = Town.Home.Parking; float yaw = Town.Home.ParkingYaw; bool rear = false;
                VanSystems.TakePending(ref pos, ref yaw, ref rear);
                Spawn(pos, yaw);
                if (rear) I.SetRearDoors(true, true);
            }
            return I;
        }

        public static WorkVan Spawn(Vector3 pos, float yaw)
        {
            Despawn();
            var go = new GameObject("Work Van") { layer = 2 };
            var v = go.AddComponent<WorkVan>();
            I = v;
            v.Build();
            v.TeleportTo(pos, yaw);
            return v;
        }

        /// <summary>Removes the van now. A driving player is put back on their feet (beside it, or left where they are).</summary>
        public static void Despawn(bool keepPlayerInPlace = false)
        {
            var v = I; if (v == null) { I = null; return; }
            if (v.Driving) v.Leave(!keepPlayerInPlace);
            I = null;
            v.gameObject.SetActive(false);
            Destroy(v.gameObject);
        }

        void Build()
        {
            var tr = transform;
            WorkVanModel.BuildBody(tr);
            _doorL = WorkVanModel.BuildRearDoor(tr, true).transform;
            _doorR = WorkVanModel.BuildRearDoor(tr, false).transform;
            float r = WorkVanModel.TireRadius, tx = WorkVanModel.TrackHalf;
            _wheelFL = WorkVanModel.BuildWheel(tr, new Vector3(-tx, r, WorkVanModel.FrontAxleZ)).transform;
            _wheelFR = WorkVanModel.BuildWheel(tr, new Vector3(tx, r, WorkVanModel.FrontAxleZ)).transform;
            WorkVanModel.BuildWheel(tr, new Vector3(-tx, r, WorkVanModel.RearAxleZ));
            WorkVanModel.BuildWheel(tr, new Vector3(tx, r, WorkVanModel.RearAxleZ));
            _steering = WorkVanModel.BuildSteeringWheel(tr).transform;
            _steering.localRotation = Quaternion.Euler(-65f, 0f, 0f);
            _rackLadder = WorkVanModel.BuildRackLadder(tr);
            _rackLadder.SetActive(LadderOnRack);
            RebuildCargo();

            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            var bc = gameObject.AddComponent<BoxCollider>();     // layer 2: stops the player, ignored by aim / ground rays
            bc.center = BodyCenter; bc.size = BodySize;

            _engine = gameObject.AddComponent<AudioSource>();
            _engine.clip = Sfx.Clip("truck"); _engine.loop = true; _engine.playOnAwake = false; _engine.spatialBlend = 0.5f;
            _engine.rolloffMode = AudioRolloffMode.Linear; _engine.maxDistance = 50f; _engine.dopplerLevel = 0f;

            VanCargo.Changed += RebuildCargo;
            TownLadder.Changed += OnLadderChanged;
        }

        void OnDestroy()
        {
            VanCargo.Changed -= RebuildCargo;
            TownLadder.Changed -= OnLadderChanged;
            if (Driving) Leave(false);
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
            if (I == this) I = null;
        }

        void OnLadderChanged() { if (_rackLadder != null) _rackLadder.SetActive(LadderOnRack); }

        void RebuildCargo()
        {
            if (_cargo != null)
            {
                var mf = _cargo.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(_cargo);
            }
            _cargo = WorkVanModel.BuildCargo(transform);
        }

        // ------------------------------------------------------------------ public controls

        /// <summary>Puts the van (centre, on the ground) at a position and heading, stopped.</summary>
        public void TeleportTo(Vector3 pos, float yaw)
        {
            _speed = 0f; _steerDeg = 0f; _yaw = yaw; _y = pos.y; _pitch = _roll = 0f;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            FollowGround(1f, true);
            Physics.SyncTransforms();
            if (Driving) SeatPlayer();
        }

        /// <summary>Input override (tests, scripted driving): throttle -1..1 (negative = brake, then reverse), steer -1..1 (right positive).</summary>
        public void SetAutopilot(float throttle, float steer, bool brake)
        {
            _auto = true; _aThrottle = Mathf.Clamp(throttle, -1f, 1f); _aSteer = Mathf.Clamp(steer, -1f, 1f); _aBrake = brake;
        }

        public void ClearAutopilot() { _auto = false; }

        public void ToggleRearDoors() => SetRearDoors(!RearOpen);

        public void SetRearDoors(bool open, bool instant = false)
        {
            if (open != RearOpen) Sfx.Play("knock", transform.TransformPoint(0f, 1.2f, WorkVanModel.RearZ), 0.5f);
            RearOpen = open;
            if (instant) { _doorT = open ? 1f : 0f; PoseDoors(); }
        }

        void PoseDoors()
        {
            float a = Mathf.SmoothStep(0f, 1f, _doorT) * 105f;
            if (_doorL != null) _doorL.localRotation = Quaternion.Euler(0f, a, 0f);
            if (_doorR != null) _doorR.localRotation = Quaternion.Euler(0f, -a, 0f);
        }

        /// <summary>Which part of the van the ray points at (cab/body, rear doors, roof rack) and how far away.</summary>
        public Part AimPart(Ray ray, out float dist)
        {
            dist = float.MaxValue;
            Part part = Part.None;
            if (VanSystems.RayBox(ray, transform, BodyCenter, BodySize, 3.5f, out float d, out Vector3 local))
            {
                dist = d;
                part = local.z < WorkVanModel.RearZ + 0.08f ? Part.RearDoors : Part.Cab;
            }
            if (VanSystems.RayBox(ray, transform, RackCenter, RackSize, TownLadder.Reach, out float dr, out _) && dr < dist) { dist = dr; part = Part.Rack; }
            return part;
        }

        // ------------------------------------------------------------------ getting in and out

        public void Enter()
        {
            var p = Game.Player;
            if (Driving || p == null) return;
            if (TownLadder.Carried) { Game.Toast("Put the ladder on the rack first"); return; }
            if (TownLadder.Placed != null) TownLadder.Placed.Dismount();
            var rig = p; var belt = p.GetComponent<ToolBelt>(); var feel = p.GetComponent<PlayerFeel>(); var cc = p.GetComponent<CharacterController>();
            _rigOn = rig.enabled; _beltOn = belt != null && belt.enabled; _feelOn = feel != null && feel.enabled; _ccOn = cc != null && cc.enabled;
            _vmWas = ViewModel.Enabled;
            // put the tool away (an empty belt slot): no ghost preview, no tool sounds from the driver's seat
            _beltSlot = -1;
            if (belt != null && belt.Active != null)
                for (int i = 0; i < ToolBelt.SlotCount; i++)
                    if (belt.Slots[i] == null) { _beltSlot = belt.ActiveIndex; belt.Select(i); break; }
            rig.enabled = false;
            if (belt != null) belt.enabled = false;
            if (feel != null) feel.enabled = false;
            if (cc != null) cc.enabled = false;
            ViewModel.Enabled = false;
            Driving = true; _lookYaw = _lookPitch = 0f;
            TransitionFrame = Time.frameCount;
            if (RearOpen) SetRearDoors(false);
            if (_engine != null && _engine.clip != null) _engine.Play();
            SeatPlayer();
        }

        /// <summary>Get out on the driver's side (only when nearly stopped).</summary>
        public void Exit()
        {
            if (!Driving) return;
            if (Mathf.Abs(_speed) > 1.5f) { Game.Toast("Stop the van first (Space)"); return; }
            Leave(true);
        }

        /// <summary>Frame the player last got in/out: the same E press must not also get back out/in (Update order is undefined).</summary>
        public static int TransitionFrame = -1;

        void Leave(bool placeBeside)
        {
            Driving = false; _speed = 0f;
            TransitionFrame = Time.frameCount;
            if (_engine != null) _engine.Stop();
            var p = Game.Player;
            ViewModel.Enabled = _vmWas;
            if (p == null) return;
            var belt = p.GetComponent<ToolBelt>(); var feel = p.GetComponent<PlayerFeel>(); var cc = p.GetComponent<CharacterController>();
            if (placeBeside)
            {
                p.transform.position = ExitSpot() + Vector3.up * 0.05f;
                p.SetLook(_yaw, 0f);
            }
            else p.SetLook(p.transform.eulerAngles.y, 0f);
            if (p.Cam != null) p.Cam.transform.localPosition = new Vector3(0f, PlayerRig.EyeHeight, 0f);
            p.enabled = _rigOn;
            if (belt != null) { belt.enabled = _beltOn; if (_beltSlot >= 0) belt.Select(_beltSlot); }
            if (feel != null) feel.enabled = _feelOn;
            if (cc != null) cc.enabled = _ccOn;
            _beltSlot = -1;
            Physics.SyncTransforms();
        }

        /// <summary>Stands the (on-foot) player beside the driver's door.</summary>
        public void PlacePlayerBeside()
        {
            if (Driving || Game.Player == null) return;
            if (TownLadder.Placed != null) TownLadder.Placed.Dismount();
            HandymanStart.MovePlayer(ExitSpot(), _yaw);
        }

        Vector3 ExitSpot()
        {
            Vector3[] local =
            {
                new Vector3(-1.8f, 0f, 0.9f), new Vector3(-1.8f, 0f, -0.4f), new Vector3(1.8f, 0f, 0.9f), new Vector3(1.8f, 0f, -0.4f),
                new Vector3(-1.8f, 0f, -2.0f), new Vector3(0f, 0f, -4.0f), new Vector3(0f, 0f, 4.0f),
            };
            var flat = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 first = Vector3.zero;
            for (int i = 0; i < local.Length; i++)
            {
                Vector3 w = transform.position + flat * local[i];
                w.y = Ground(w, _y);
                if (i == 0) first = w;
                if (!Physics.CheckCapsule(w + Vector3.up * 0.4f, w + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return w;
            }
            return first;
        }

        void SeatPlayer()
        {
            var p = Game.Player; if (p == null) return;
            p.transform.SetPositionAndRotation(transform.TransformPoint(SeatLocal), Quaternion.Euler(0f, _yaw, 0f));
            if (p.Cam == null) return;
            if (!ChaseCamera)
                p.Cam.transform.SetPositionAndRotation(transform.TransformPoint(EyeLocal), transform.rotation * Quaternion.Euler(_lookPitch, _lookYaw, 0f));
            else
            {
                var r = Quaternion.Euler(14f + _lookPitch, _yaw + _lookYaw, 0f);
                p.Cam.transform.SetPositionAndRotation(transform.position + Vector3.up * 1.5f - r * Vector3.forward * 8.5f, r);
            }
        }

        // ------------------------------------------------------------------ fast travel

        /// <summary>Road distance (m) between two points: to the nearest street, along the street grid, and off it again.</summary>
        public static float RoadDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            Vector3 ra = Town.NearestRoadPoint(a), rb = Town.NearestRoadPoint(b);
            return (a - ra).magnitude + Mathf.Abs(ra.x - rb.x) + Mathf.Abs(ra.z - rb.z) + (b - rb).magnitude;
        }

        /// <summary>Game minutes it takes to drive a distance at 25 mph (the same clock cost as really driving it).</summary>
        public static float DriveGameMinutes(float meters) => meters / (FastTravelMph * Mph) * (24f * 60f / GameClock.RealSecondsPerDay);

        /// <summary>Drives the van to a lot's parking spot instantly, charging the drive time to the game clock.
        /// The player ends up beside the driver's door (or still at the wheel). False when there is no van.</summary>
        public static bool FastTravel(TownLot lot) => lot != null && FastTravel(lot.Parking, lot.ParkingYaw, lot.Address);

        public static bool FastTravel(Vector3 pos, float yaw, string label)
        {
            var v = I; if (v == null) return false;
            float minutes = DriveGameMinutes(RoadDistance(v.transform.position, pos));
            if (TownLadder.Carried) TownLadder.Stow();
            v.TeleportTo(pos, yaw);
            if (!v.Driving) v.PlacePlayerBeside();
            var clock = GameClock.I;
            if (clock != null)
            {
                clock.Hours += minutes / 60f;
                while (clock.Hours >= 24f) { clock.Hours -= 24f; clock.Day++; }
            }
            Game.Toast($"Drove to {label} ({Mathf.Max(1, Mathf.RoundToInt(minutes))} min)");
            return true;
        }

        // ------------------------------------------------------------------ driving

        void Update()
        {
            float dt = Time.deltaTime;
            float throttle = 0f, steer = 0f; bool brake = !Driving;
            if (_auto) { throttle = _aThrottle; steer = _aSteer; brake = _aBrake; }
            else if (Driving && !Game.UIOpen)
            {
                if (GInput.GetKey(KeyCode.W)) throttle += 1f;
                if (GInput.GetKey(KeyCode.S)) throttle -= 1f;
                if (GInput.GetKey(KeyCode.D)) steer += 1f;
                if (GInput.GetKey(KeyCode.A)) steer -= 1f;
                brake = GInput.GetKey(KeyCode.Space);
            }
            if (Driving && !Game.UIOpen)
            {
                if (GInput.GetKeyDown(KeyCode.V)) ChaseCamera = !ChaseCamera;
                if (GInput.GetKeyDown(KeyCode.E) && Time.frameCount != TransitionFrame) { Exit(); if (!Driving) return; }
                if (Cursor.lockState == CursorLockMode.Locked && Game.Player != null)
                {
                    Vector2 d = GInput.MouseDelta * Game.Player.Sensitivity;
                    _lookYaw = Mathf.Clamp(_lookYaw + d.x, -150f, 150f);
                    _lookPitch = Mathf.Clamp(_lookPitch - d.y, -60f, 60f);
                }
            }

            bool moved = false;
            for (float left = Mathf.Min(dt, 0.5f); left > 1e-5f; left -= 0.02f) moved |= Step(Mathf.Min(left, 0.02f), throttle, steer, brake);
            _settleT -= dt;
            if (moved || _settleT <= 0f) { FollowGround(dt, false); if (!moved) _settleT = 1f; }
            Vector3 pos = transform.position; pos.y = _y;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(_pitch, _yaw, _roll));

            // visuals
            float target = RearOpen ? 1f : 0f;
            if (!Mathf.Approximately(_doorT, target)) { _doorT = Mathf.MoveTowards(_doorT, target, dt * 1.6f); PoseDoors(); }
            if (_wheelFL != null) _wheelFL.localRotation = _wheelFR.localRotation = Quaternion.Euler(0f, _steerDeg, 0f);
            if (_steering != null) _steering.localRotation = Quaternion.Euler(-65f, 0f, 0f) * Quaternion.Euler(0f, _steerDeg * 6f, 0f);
            if (_engine != null && _engine.isPlaying)
            {
                _engine.pitch = 0.7f + Mathf.Abs(_speed) / TopSpeed * 0.9f;
                _engine.volume = (0.18f + 0.14f * Mathf.Abs(throttle)) * GameSettings.MasterVolume;
            }
        }

        void LateUpdate() { if (Driving) SeatPlayer(); }

        /// <summary>One physics step of the bicycle model (rear axle follows the heading). Returns true when the van moved.</summary>
        bool Step(float h, float throttle, float steer, bool brake)
        {
            float v = Mathf.Abs(_speed);
            float maxSteer = Mathf.Lerp(MaxSteerDeg, 6f, Mathf.InverseLerp(2f, TopSpeed, v));     // speed-sensitive steering
            _steerDeg = Mathf.MoveTowards(_steerDeg, steer * maxSteer, 80f * h);

            if (brake) _speed = Mathf.MoveTowards(_speed, 0f, 8f * h);
            else if (throttle > 0.01f)
            {
                if (_speed < -0.1f) _speed = Mathf.MoveTowards(_speed, 0f, 7f * throttle * h);
                else _speed = Mathf.Min(TopSpeed, _speed + throttle * 3.2f * (1f - 0.6f * Mathf.Clamp01(_speed / TopSpeed)) * h);
            }
            else if (throttle < -0.01f)
            {
                if (_speed > 0.1f) _speed = Mathf.MoveTowards(_speed, 0f, 7f * -throttle * h);
                else _speed = Mathf.Max(-TopReverse, _speed + throttle * 2.0f * h);
            }
            else _speed = Mathf.MoveTowards(_speed, 0f, 1.0f * h);
            if (Mathf.Abs(_speed) < 1e-4f) return false;

            float newYaw = _yaw + _speed * Mathf.Tan(_steerDeg * Mathf.Deg2Rad) / WorkVanModel.Wheelbase * Mathf.Rad2Deg * h;
            Vector3 cur = transform.position; cur.y = _y;
            Vector3 f0 = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward, f1 = Quaternion.Euler(0f, newYaw, 0f) * Vector3.forward;
            Vector3 rear = cur + f0 * WorkVanModel.RearAxleZ + f0 * (_speed * h);
            Vector3 next = rear - f1 * WorkVanModel.RearAxleZ;
            if (Blocked(next, newYaw) && !Blocked(cur, _yaw))
            {
                if (Mathf.Abs(_speed) > 1.5f) Sfx.Play("thud", cur + Vector3.up, Mathf.Clamp01(Mathf.Abs(_speed) / 6f));
                _speed = 0f;
                return false;
            }
            _yaw = Mathf.Repeat(newYaw, 360f);
            transform.position = next;
            return true;
        }

        /// <summary>Would the body (everything more than 0.38 m off the ground) overlap an obstacle at this pose?</summary>
        bool Blocked(Vector3 pos, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            int n = Physics.OverlapBoxNonAlloc(pos + rot * BodyCenter, BodySize * 0.5f - new Vector3(0.02f, 0.02f, 0.02f), _cols, rot, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (_cols[i] != null && !_cols[i].transform.IsChildOf(transform)) return true;
            return false;
        }

        /// <summary>Highest ground under a point that the wheels can climb (curbs, aprons), else the reference height.</summary>
        static float Ground(Vector3 p, float refY)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, refY + 1.2f, p.z), Vector3.down, _hits, 3.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                float y = _hits[i].point.y;
                if (y <= refY + 0.45f && y > best) best = y;
            }
            return float.IsNegativeInfinity(best) ? refY : best;
        }

        void FollowGround(float dt, bool snap)
        {
            var flat = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 c = transform.position;
            float tx = WorkVanModel.TrackHalf, fz = WorkVanModel.FrontAxleZ, rz = WorkVanModel.RearAxleZ;
            float fl = Ground(c + flat * new Vector3(-tx, 0f, fz), _y), fr = Ground(c + flat * new Vector3(tx, 0f, fz), _y);
            float rl = Ground(c + flat * new Vector3(-tx, 0f, rz), _y), rr = Ground(c + flat * new Vector3(tx, 0f, rz), _y);
            float front = (fl + fr) * 0.5f, rear = (rl + rr) * 0.5f;
            float y = Mathf.Lerp(rear, front, -rz / WorkVanModel.Wheelbase);
            float pitch = -Mathf.Atan2(front - rear, WorkVanModel.Wheelbase) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2((fr + rr) * 0.5f - (fl + rl) * 0.5f, tx * 2f) * Mathf.Rad2Deg;
            float k = snap ? 1f : 1f - Mathf.Exp(-14f * dt);
            _y = Mathf.Lerp(_y, y, k); _pitch = Mathf.Lerp(_pitch, pitch, k); _roll = Mathf.Lerp(_roll, roll, k);
            if (snap)
            {
                c.y = _y;
                transform.SetPositionAndRotation(c, Quaternion.Euler(_pitch, _yaw, _roll));
            }
        }

        // ------------------------------------------------------------------ HUD

        GUIStyle _big, _small; int _styleH;

        void OnGUI()
        {
            if (!Driving || Game.UIOpen) return;
            if (_big == null || _styleH != Screen.height)
            {
                _styleH = Screen.height;
                int fs = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 45f));
                _big = new GUIStyle(GUI.skin.label) { fontSize = fs * 2, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _big.normal.textColor = Color.white;
                _small = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                _small.normal.textColor = Color.white;
            }
            float fsz = _small.fontSize, w = fsz * 9f, h = fsz * 4.2f;
            var r = new Rect(Screen.width - w - 12f, Screen.height - h - 12f, w, h);
            UISkin.Panel(r);
            GUI.Label(new Rect(r.x, r.y + 2f, w, fsz * 2.6f), Mathf.RoundToInt(SpeedMph).ToString(), _big);
            GUI.Label(new Rect(r.x, r.y + fsz * 2.5f, w, fsz * 1.5f), "mph  " + (_speed < -0.1f ? "R" : Mathf.Abs(_speed) < 0.1f ? "P" : "D"), _small);
            var hint = new Rect(0f, 14f, Screen.width, fsz * 1.5f);
            const string text = "W/S throttle / reverse   A/D steer   Space brake   V camera   E get out (stopped)";
            var prev = _small.normal.textColor;
            _small.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(hint.x + 1f, hint.y + 1f, hint.width, hint.height), text, _small);
            _small.normal.textColor = prev;
            GUI.Label(hint, text, _small);
        }
    }
}
