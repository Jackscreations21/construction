using UnityEngine;

namespace Groundbreak
{
    /// <summary>A hinged door in a town building. Sits on the hinge pivot; the slab is a child along the pivot's local +X.
    /// E while aiming at it (within 3 m) swings it. Repairs set <see cref="Sticking"/> (opens only ~15° and drags).</summary>
    public class TownDoor : MonoBehaviour
    {
        public bool Open;
        public bool Sticking;
        public float OpenAngle = 85f;
        public float SwingSign = 1f;
        Quaternion _closed;
        float _angle;
        bool _init;

        public float Angle => _angle;
        float Target => Open ? (Sticking ? 15f : OpenAngle) : 0f;

        void Awake() { Init(); }

        void Init()
        {
            if (_init) return;
            _init = true; _closed = transform.localRotation; _angle = Target; Apply();
        }

        public void Toggle() { Open = !Open; Sfx.Play(Sticking ? "thud" : "click", transform.position, 0.6f); }

        /// <summary>Set the state without animating (building / tests / load).</summary>
        public void SetOpenImmediate(bool open) { Init(); Open = open; _angle = Target; Apply(); }

        void Update()
        {
            float speed = Sticking ? 35f : 170f;
            if (!Mathf.Approximately(_angle, Target)) { _angle = Mathf.MoveTowards(_angle, Target, speed * Time.deltaTime); Apply(); }
            if (Game.UIOpen || Game.Player == null || !GInput.GetKeyDown(KeyCode.E)) return;
            if (Game.Player.TryAim(out var hit, 3f) && hit.collider.GetComponentInParent<TownDoor>() == this) Toggle();
        }

        void Apply() { transform.localRotation = Quaternion.Euler(0f, _angle * SwingSign, 0f) * _closed; }
    }
}
