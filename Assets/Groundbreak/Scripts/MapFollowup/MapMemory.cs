using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Safety net for town / map rebuilds: meshes, materials and textures made from code are not freed with their GameObjects. The map,
    /// far ring, vacant lots and build-site fill destroy their own meshes; anything else the town left behind (core town, flip dressing,
    /// houses) is released one frame after the town is destroyed with <see cref="Resources.UnloadUnusedAssets"/> (only objects nothing
    /// references any more — cached C# references keep their assets alive).
    /// </summary>
    public class MapMemory : MonoBehaviour
    {
        static bool _pending;
        public static int Sweeps { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            Town.Destroyed -= Mark; Town.Destroyed += Mark;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<MapMemory>() != null) return;
            var go = new GameObject("MapMemory"); DontDestroyOnLoad(go); go.AddComponent<MapMemory>();
        }

        static void Mark() { _pending = true; }

        void LateUpdate()
        {
            if (!_pending) return;
            _pending = false;
            Sweeps++;
            Resources.UnloadUnusedAssets();
        }

        /// <summary>Tests: release unreferenced assets now.</summary>
        public static AsyncOperation SweepNow() { _pending = false; Sweeps++; return Resources.UnloadUnusedAssets(); }
    }
}
