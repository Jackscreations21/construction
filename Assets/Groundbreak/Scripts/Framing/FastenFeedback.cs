using UnityEngine;

namespace Groundbreak
{
    /// <summary>Small physical feedback when fastening: a decaying camera recoil (applied after PlayerRig sets the
    /// camera's absolute pitch each Update, so it never accumulates) and the driven nail head left in the wood.</summary>
    public class FastenFeedback : MonoBehaviour
    {
        static FastenFeedback _inst;
        float _kick, _vel;

        /// <summary>degrees of upward camera kick (hammer blow ~0.6, nailer ~1.2).</summary>
        public static void Kick(float degrees)
        {
            if (Game.Player == null || Game.Player.Cam == null) return;
            if (_inst == null)
            {
                var go = new GameObject("FastenFeedback");
                Object.DontDestroyOnLoad(go);
                _inst = go.AddComponent<FastenFeedback>();
            }
            _inst._vel += degrees * 40f;
        }

        /// <summary>Nail driven at the aimed point: leave the head, kick the view.</summary>
        public static void Nailed(Piece p, RaycastHit hit, float kick)
        {
            LumberMarks.NailHead(p, hit);
            Kick(kick);
        }

        void LateUpdate()
        {
            // critically-damped-ish spring back to rest (snappy up, eases down over ~0.15 s)
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _vel += (-_kick * 900f - _vel * 45f) * dt;
            _kick += _vel * dt;
            if (Mathf.Abs(_kick) < 0.001f && Mathf.Abs(_vel) < 0.01f) { _kick = 0f; _vel = 0f; return; }
            var cam = Game.Player != null ? Game.Player.Cam : null;
            if (cam != null) cam.transform.localRotation = cam.transform.localRotation * Quaternion.Euler(-_kick, 0f, 0f);
        }
    }
}
