using UnityEngine;

namespace Groundbreak
{
    /// <summary>Grid + angle snap state. Cycled with G / Shift+G and R / Shift+R (input handled by PlacementController).</summary>
    public class SnapSettings
    {
        // Grid sizes in inches; 0 = off (free)
        public static readonly float[] GridInches = { 0f, 0.125f, 0.5f, 1f, 4f, 16f, 24f, 12f, 48f };
        public static readonly string[] GridLabels = { "Off", "1/8\"", "1/2\"", "1\"", "4\"", "16\" O.C.", "24\" O.C.", "1'", "4'" };
        // Angle steps in degrees; 0 = free
        public static readonly float[] AngleSteps = { 90f, 45f, 22.5f, 15f, 5f, 1f, 0f };

        public int GridIndex = 3;   // 1"
        public int AngleIndex = 1;  // 45 deg

        /// <summary>Local grid origin (set with O). Grid lines run through this point, aligned to world axes rotated by OriginYaw.</summary>
        public Vector3 Origin = Vector3.zero;
        public float OriginYaw = 0f;

        public float GridMeters => GridInches[GridIndex] * Units.Inch;
        public float AngleStep => AngleSteps[AngleIndex];
        public string GridLabel => GridLabels[GridIndex];
        public string AngleLabel => AngleStep <= 0f ? "Free" : AngleStep + "°";

        public void CycleGrid(int dir) => GridIndex = (GridIndex + dir + GridInches.Length) % GridInches.Length;
        public void CycleAngle(int dir) => AngleIndex = (AngleIndex + dir + AngleSteps.Length) % AngleSteps.Length;

        /// <summary>Snaps a world position to the grid (all three axes), relative to Origin/OriginYaw.</summary>
        public Vector3 SnapPosition(Vector3 world)
        {
            float g = GridMeters;
            if (g <= 0f) return world;
            Quaternion rot = Quaternion.Euler(0, OriginYaw, 0);
            Vector3 local = Quaternion.Inverse(rot) * (world - Origin);
            local.x = Mathf.Round(local.x / g) * g;
            local.y = Mathf.Round(local.y / g) * g;
            local.z = Mathf.Round(local.z / g) * g;
            return Origin + rot * local;
        }

        /// <summary>Snaps a single length (meters) to the grid step.</summary>
        public float SnapLength(float meters)
        {
            float g = GridMeters;
            return g <= 0f ? meters : Mathf.Round(meters / g) * g;
        }

        public float SnapAngle(float degrees)
        {
            float s = AngleStep;
            return s <= 0f ? degrees : Mathf.Round(degrees / s) * s;
        }
    }
}
