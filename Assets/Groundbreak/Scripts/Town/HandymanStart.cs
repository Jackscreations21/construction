using UnityEngine;

namespace Groundbreak
{
    /// <summary>New Career = Handyman stage: the town is built, the company gets its used work van, $2,500, and the player
    /// starts in the driveway at home (108 Main St). Free Build is untouched.</summary>
    public static class HandymanStart
    {
        public const int StartingCash = 2500;
        public const string VanTypeId = "van";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() { MainMenu.NewGameStarted += OnNewGame; }

        static void OnNewGame(GameMode mode)
        {
            if (mode != GameMode.Career) { Town.Destroy(); return; }
            Begin();
        }

        /// <summary>Sets up a fresh Handyman career (also used by tests).</summary>
        public static void Begin()
        {
            Town.Ensure();
            Game.Wallet = new Wallet(StartingCash);
            if (!Company.Owns(VanTypeId)) Company.AddVehicle(VanTypeId, "Work Van");
            VanCargo.LoadStarterKit();
            WorkVan.EnsureSpawned();
            RepairKit.GrantStarterTools();
            RepairJobs.GenerateOffers();
            MovePlayer(Town.Home.Parking + Town.Home.Rotation * new Vector3(-2.2f, 0f, 0f), Town.Home.ParkingYaw);
            Game.Toast("You're in business. Tablet (T) → Jobs for service calls. E at the van door to drive.");
        }

        public static void MovePlayer(Vector3 feet, float yaw)
        {
            var p = Game.Player; if (p == null) return;
            var cc = p.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            p.transform.position = feet + Vector3.up * 0.05f;
            p.SetLook(yaw, 0f);
            if (cc != null) cc.enabled = true;
        }
    }
}
