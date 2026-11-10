using UnityEngine;

namespace Groundbreak
{
    /// <summary>Static service registry. Each system registers itself in Awake.</summary>
    public static class Game
    {
        public static SnapSettings Snap = new SnapSettings();
        public static Wallet Wallet = new Wallet(5000);
        public static Stockpile Stock = new Stockpile();

        // Set by their owners (see Docs/CONTRACTS.md)
        public static PlacementController Placement; // Agent A
        public static PlayerRig Player;              // Agent B
        public static ToolBelt Belt;                 // Agent B

        /// <summary>True while a full-screen UI (tablet) is open: gameplay input must be ignored and cursor unlocked.</summary>
        public static bool UIOpen;

        /// <summary>Short message shown by the HUD for a few seconds.</summary>
        public static void Toast(string msg) { ToastText = msg; ToastUntil = Time.time + 3f; }
        public static string ToastText = "";
        public static float ToastUntil;

        public static void ResetStatics()
        {
            Snap = new SnapSettings(); Wallet = new Wallet(5000); Stock = new Stockpile();
            Placement = null; Player = null; Belt = null; UIOpen = false; ToastText = ""; ToastUntil = 0;
        }
    }
}
