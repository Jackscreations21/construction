using UnityEngine;

namespace Groundbreak
{
    /// <summary>Rectangular building frame in plan. Local frame: origin = outside corner of the framing at ground,
    /// +X along Length, +Z along Width (into the building), Y up (world heights).</summary>
    [System.Serializable]
    public struct Footprint
    {
        public Vector3 Origin;
        public float Yaw;
        public float Length, Width;      // outside of framing, meters
        public float DeckTopY;           // world Y of the subfloor top
        public float PlateTopY;          // world Y of the top of the double top plate (roof bearing)
        public float WallDepth;          // 3-1/2" for 2x4 walls, 5-1/2" for 2x6

        public Quaternion Rotation => Quaternion.Euler(0f, Yaw, 0f);
        public Vector3 AlongX => Rotation * Vector3.right;
        public Vector3 AlongZ => Rotation * Vector3.forward;
        /// <summary>World point from building-local (x along length, y = world height, z across).</summary>
        public Vector3 World(float x, float y, float z) { var p = Origin + Rotation * new Vector3(x, 0f, z); p.y = y; return p; }

        public static Footprint Make(Vector3 origin, float yaw, float lengthFt, float widthFt)
        {
            float deck = Units.In(1.5f + 7.25f + 0.71875f);                 // sill + 2x8 rim + 3/4 subfloor
            float plate = deck + Units.In(1.5f + 92.625f + 1.5f + 1.5f);    // bottom plate + stud + double top plate
            return new Footprint { Origin = origin, Yaw = yaw, Length = lengthFt * Units.Foot, Width = widthFt * Units.Foot, DeckTopY = origin.y + deck, PlateTopY = origin.y + plate, WallDepth = Units.In(3.5f) };
        }
    }
}
