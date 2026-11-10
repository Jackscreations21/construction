using UnityEngine;

namespace Groundbreak
{
    /// <summary>First-person player: CharacterController + camera + ToolBelt.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerRig : MonoBehaviour
    {
        public const float Height = 1.8f, CrouchHeight = 1.1f, Radius = 0.3f, EyeHeight = 1.65f;
        public float WalkSpeed = 3.5f, SprintSpeed = 6f, CrouchSpeed = 1.8f, JumpSpeed = 4.5f, Gravity = 19.6f;
        public float Sensitivity = 0.1f; // degrees per pixel of mouse delta

        public Camera Cam { get; private set; }
        public Ray AimRay => Cam != null ? Cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : new Ray(transform.position, transform.forward);

        CharacterController _cc;
        float _yaw, _pitch, _vy, _currentHeight = Height;
        bool _wantLock = true;

        public static PlayerRig Spawn(Vector3 feetPos)
        {
            var go = new GameObject("Player");
            go.layer = 2; // Ignore Raycast
            go.transform.position = feetPos;
            var cc = go.AddComponent<CharacterController>();
            cc.height = Height; cc.radius = Radius; cc.center = new Vector3(0, Height * 0.5f, 0);
            cc.stepOffset = 0.3f; cc.skinWidth = 0.03f;

            var camGo = new GameObject("PlayerCamera");
            camGo.layer = 2;
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0, EyeHeight, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f; cam.farClipPlane = 1000f; cam.fieldOfView = 70f;
            camGo.AddComponent<AudioListener>();

            var rig = go.AddComponent<PlayerRig>();
            rig.Cam = cam;
            rig._cc = cc;
            rig._yaw = go.transform.eulerAngles.y;
            go.AddComponent<ToolBelt>();
            return rig;
        }

        void Awake()
        {
            Game.Player = this;
            _cc = GetComponent<CharacterController>();
        }

        void OnDestroy() { if (Game.Player == this) Game.Player = null; }

        public bool TryAim(out RaycastHit hit, float range = 6f)
        {
            return Physics.Raycast(AimRay, out hit, range, Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore);
        }

        /// <summary>Sets look direction directly (used by tests and cutscenes).</summary>
        public void SetLook(float yaw, float pitch)
        {
            _yaw = yaw; _pitch = Mathf.Clamp(pitch, -85f, 85f);
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
            if (Cam != null) Cam.transform.localRotation = Quaternion.Euler(_pitch, 0, 0);
        }

        void Update()
        {
            UpdateCursor();
            bool input = !Game.UIOpen && Cursor.lockState == CursorLockMode.Locked;

            if (input)
            {
                Vector2 d = GInput.MouseDelta * Sensitivity;
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
            }
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
            if (Cam != null) Cam.transform.localRotation = Quaternion.Euler(_pitch, 0, 0);

            Vector3 move = Vector3.zero;
            bool crouch = false, sprint = false, jump = false;
            if (!Game.UIOpen)
            {
                if (GInput.GetKey(KeyCode.W)) move.z += 1;
                if (GInput.GetKey(KeyCode.S)) move.z -= 1;
                if (GInput.GetKey(KeyCode.D)) move.x += 1;
                if (GInput.GetKey(KeyCode.A)) move.x -= 1;
                crouch = GInput.GetKey(KeyCode.C);
                sprint = GInput.Shift && !crouch;
                jump = GInput.GetKeyDown(KeyCode.Space);
            }
            move = Vector3.ClampMagnitude(move, 1f);
            float speed = crouch ? CrouchSpeed : sprint ? SprintSpeed : WalkSpeed;
            Vector3 vel = transform.TransformDirection(move) * speed;

            // Crouch: shrink controller (only stand up if there's headroom)
            float targetH = crouch ? CrouchHeight : Height;
            if (!crouch && _currentHeight < Height)
            {
                Vector3 top = transform.position + Vector3.up * (_currentHeight - Radius);
                if (Physics.SphereCast(top, Radius * 0.9f, Vector3.up, out _, Height - _currentHeight, Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore))
                    targetH = _currentHeight;
            }
            _currentHeight = Mathf.MoveTowards(_currentHeight, targetH, 4f * Time.deltaTime);
            _cc.height = _currentHeight;
            _cc.center = new Vector3(0, _currentHeight * 0.5f, 0);
            if (Cam != null) Cam.transform.localPosition = new Vector3(0, EyeHeight - (Height - _currentHeight), 0);

            if (_cc.isGrounded)
            {
                _vy = -2f;
                if (jump && !crouch) _vy = JumpSpeed;
            }
            else _vy -= Gravity * Time.deltaTime;
            vel.y = _vy;
            _cc.Move(vel * Time.deltaTime);
        }

        void UpdateCursor()
        {
            if (Game.UIOpen)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                return;
            }
            if (GInput.GetKeyDown(KeyCode.Escape)) _wantLock = !_wantLock;
            else if (!_wantLock && GInput.MouseDown(0)) _wantLock = true;
            Cursor.lockState = _wantLock ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_wantLock;
        }
    }
}
