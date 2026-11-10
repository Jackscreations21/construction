using UnityEngine;

namespace Groundbreak
{
    /// <summary>First-person feel layered on top of <see cref="PlayerRig"/> (which resets the camera pose every Update;
    /// this adds offsets in LateUpdate): gait-driven head bob, lateral sway and roll, sprint FOV kick, landing dip,
    /// footsteps on the heel strike. Teleports (tests, respawn) are ignored.</summary>
    [DefaultExecutionOrder(100)]
    public class PlayerFeel : MonoBehaviour
    {
        /// <summary>0 = no bob/sway (comfort), 1 = default.</summary>
        public static float BobScale = 1f;
        public const float SprintFovKick = 6f;

        /// <summary>Gait phase in radians: advances π per step. Read by the view-model.</summary>
        public float Phase { get; private set; }
        /// <summary>0..1 how much of the walk cycle is showing (0 standing, 1 walking).</summary>
        public float BobBlend { get; private set; }
        public bool Sprinting { get; private set; }
        public float LandDip => _dip;
        /// <summary>Horizontal ground speed, m/s.</summary>
        public float Speed { get; private set; }
        /// <summary>Camera offset applied this frame (bob + landing dip), local to the rig.</summary>
        public Vector3 CamOffset { get; private set; }

        PlayerRig _rig; CharacterController _cc;
        Vector3 _lastPos; bool _init, _wasGrounded = true;
        float _airTime, _fallSpeed, _dip, _dipVel, _fovApplied, _speedSm;
        int _stepIndex;

        void Awake() { _rig = GetComponent<PlayerRig>(); _cc = GetComponent<CharacterController>(); }

        void LateUpdate()
        {
            if (_rig == null || _rig.Cam == null || _cc == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var pos = transform.position;
            if (!_init) { _lastPos = pos; _init = true; }
            Vector3 d = pos - _lastPos; _lastPos = pos;
            float horiz = new Vector2(d.x, d.z).magnitude;
            bool teleport = horiz / dt > 20f || Mathf.Abs(d.y) / dt > 30f;
            if (teleport) { horiz = 0; BobBlend = 0; _speedSm = 0; _airTime = 0; _wasGrounded = true; }

            bool grounded = _cc.isGrounded;
            Speed = horiz / dt;
            _speedSm = Mathf.Lerp(_speedSm, Speed, 1f - Mathf.Exp(-12f * dt));
            bool crouched = _cc.height < PlayerRig.Height - 0.2f;
            Sprinting = grounded && !crouched && _speedSm > _rig.WalkSpeed + 0.6f;

            // ---- gait: stride length grows with speed (≈0.75 m walking, ≈1.0 m sprinting, ≈0.5 m crouched)
            float stride = crouched ? 0.5f : Mathf.Lerp(0.72f, 1.0f, Mathf.InverseLerp(_rig.WalkSpeed, _rig.SprintSpeed, _speedSm));
            bool moving = grounded && _speedSm > 0.35f && !teleport;
            if (moving)
            {
                Phase += horiz / stride * Mathf.PI;
                int idx = Mathf.FloorToInt(Phase / Mathf.PI);
                if (idx != _stepIndex)
                {
                    _stepIndex = idx;
                    float loud = crouched ? 0.45f : Sprinting ? 1.25f : 1f;
                    Sfx.Footstep(pos, loud);
                }
            }
            else
            {
                // settle to the nearest standing pose (phase multiple of π) so the head doesn't freeze mid-bob
                _stepIndex = Mathf.FloorToInt(Phase / Mathf.PI);
            }
            BobBlend = Mathf.MoveTowards(BobBlend, moving ? Mathf.Clamp01(_speedSm / _rig.WalkSpeed) : 0f, dt * 4f);

            // ---- landing: spring-damped dip proportional to fall speed
            if (!grounded) { _airTime += dt; _fallSpeed = Mathf.Max(_fallSpeed, -_cc.velocity.y); }
            else
            {
                if (!_wasGrounded && _airTime > 0.18f && !teleport)
                {
                    _dipVel -= Mathf.Clamp(_fallSpeed * 0.06f, 0.15f, 0.7f);
                    Sfx.Footstep(pos, Mathf.Clamp(_fallSpeed / 5f, 0.8f, 1.6f));
                    Sfx.Play("land", pos, Mathf.Clamp01(_fallSpeed / 8f) * 0.6f);
                }
                _airTime = 0; _fallSpeed = 0;
            }
            _wasGrounded = grounded;
            float k = 160f, c = 2f * Mathf.Sqrt(k) * 0.7f; // slightly under-damped
            _dipVel += (-k * _dip - c * _dipVel) * dt;
            _dip = Mathf.Clamp(_dip + _dipVel * dt, -0.12f, 0.05f);

            // ---- apply to camera (PlayerRig already wrote the base pose this frame)
            float s = BobScale * BobBlend;
            float amp = crouched ? 0.012f : Sprinting ? 0.042f : 0.026f;
            float bobY = -amp * 0.5f * (1f + Mathf.Cos(2f * Phase)) * s;    // lowest on heel strike
            float bobX = Mathf.Sin(Phase) * amp * 0.55f * s;                  // weight shifts side to side, one cycle per two steps
            float roll = Mathf.Sin(Phase) * (Sprinting ? 0.7f : 0.35f) * s;
            var cam = _rig.Cam.transform;
            CamOffset = new Vector3(bobX, bobY + _dip, 0f);
            cam.localPosition += CamOffset;
            cam.localRotation *= Quaternion.Euler(Mathf.Cos(2f * Phase) * 0.25f * s, 0f, roll);

            // ---- FOV: remove last frame's kick, add this frame's (keeps any FOV set by others intact)
            float want = Sprinting && !Game.UIOpen ? SprintFovKick : 0f;
            float kick = Mathf.Lerp(_fovApplied, want, 1f - Mathf.Exp(-6f * dt));
            _rig.Cam.fieldOfView += kick - _fovApplied;
            _fovApplied = kick;
        }
    }
}
