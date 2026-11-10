using UnityEngine;

namespace Groundbreak
{
    /// <summary>Keeps the player on top of the terrain. Raising the grade (sloped lots, fill, loading a save) can put the
    /// ground surface above the player's feet; the one-sided terrain colliders then let them drop through the world.
    /// When the feet end up buried more than a few inches (or the player falls far below the site) they're lifted onto the surface.</summary>
    public class GroundSafety : MonoBehaviour
    {
        public const float BuriedTolerance = 0.3f; // m below the surface before we step in
        public const float KillY = -40f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<GroundSafety>() != null) return;
            DontDestroyOnLoad(new GameObject("GroundSafety").AddComponent<GroundSafety>().gameObject);
        }

        void LateUpdate() => Check();

        /// <summary>Lifts the player onto the terrain if needed. Returns true when it moved them.</summary>
        public static bool Check()
        {
            var p = Game.Player;
            if (p == null) return false;
            Vector3 pos = p.transform.position;
            float h = SurfaceAt(pos.x, pos.z);
            if (pos.y >= h - BuriedTolerance && pos.y > KillY) return false;
            Debug.Log($"[GroundSafety] player at {pos} was below the ground ({h:F2} m) - lifted onto it");
            var cc = p.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            p.transform.position = new Vector3(pos.x, h + 0.05f, pos.z);
            if (cc != null) cc.enabled = true;
            Physics.SyncTransforms();
            return true;
        }

        /// <summary>Top of the walkable ground at (x,z): the lot grade, the building pad, or the flat ground plane.</summary>
        public static float SurfaceAt(float x, float z)
        {
            if (Grade.InPad(x, z) || (Grade.Field != null && Grade.Field.Contains(x, z))) return Grade.HeightAt(x, z);
            return 0f;
        }
    }
}
