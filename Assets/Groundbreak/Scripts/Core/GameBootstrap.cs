using UnityEngine;

namespace Groundbreak
{
    /// <summary>Builds the whole prototype scene from code, so any empty scene works: press Play.</summary>
    public static class GameBootstrap
    {
        /// <summary>Materials on site at the start of every game (fresh boot and New Game).</summary>
        public static readonly (string id, int n)[] StarterStock =
        {
            ("2x4x8", 12), ("2x4x925", 16), ("pt2x4x8", 2), ("osb716", 4), ("nails16d", 500), ("nails8d", 500),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Game.ResetStatics();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<PlacementController>() != null) return; // already built

            // Remove default camera; the player rig brings its own.
            var oldCam = Camera.main; if (oldCam != null) Object.Destroy(oldCam.gameObject);

            // Lighting
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            }

            Materials.ApplyEnvironment();

            // Ground: 2000' x 2000' open workspace (fog hides the edge)
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = Vector3.one * (2000 * Units.Foot / 10f);
            // The plane's UVs span 0..1 over its size; scale tiling so grass keeps real-world size.
            float groundSize = 2000 * Units.Foot;
            var grass = new Material(Materials.Get(SurfaceKind.Grass));
            grass.mainTextureScale = Vector2.one * (groundSize / Materials.TileMeters(SurfaceKind.Grass));
            ground.GetComponent<Renderer>().sharedMaterial = grass;

            // Starting materials on site
            foreach (var (id, n) in StarterStock) Game.Stock.Add(id, n);

            // Systems
            var systems = new GameObject("Systems");
            systems.AddComponent<PlacementController>(); // Agent A
            systems.AddComponent<GameClock>();          // Agent C
            systems.AddComponent<DeliveryService>();    // Agent C
            systems.AddComponent<Tablet>();             // Agent C
            systems.AddComponent<Hud>();                // Agent C
            DeliveryService.SpawnInitialPiles();        // Agent C: visual piles for the starting stock

            PlayerRig.Spawn(new Vector3(0, 0, -4));     // Agent B (adds ToolBelt with default loadout)
        }
    }
}
