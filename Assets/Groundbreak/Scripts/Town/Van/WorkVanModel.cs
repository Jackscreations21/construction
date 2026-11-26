using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Real-size model of a full-size low-roof cargo van, 148" wheelbase (5.99 m x 2.03 m x 2.10 m, 29" tyres).
    /// Local frame: origin on the ground under the middle of the body, +Z forward, +X right (driver sits on the left).</summary>
    public static class WorkVanModel
    {
        public const float Length = 5.99f, Width = 2.03f, RoofY = 2.10f, FloorTop = 0.635f;
        public const float Wheelbase = 148f * Units.Inch, FrontAxleZ = 1.975f, RearAxleZ = FrontAxleZ - Wheelbase;
        public const float TireRadius = 14.5f * Units.Inch, TireWidth = 0.235f, TrackHalf = 0.87f;
        public const float RearZ = -2.96f, NoseZ = 2.96f, CabZ = 0.55f, CowlZ = 1.95f, HingeX = 0.90f;
        public const float RackY = 2.23f;                         // top of the crossbars
        public const float ShelfDepth = 14f * Units.Inch;
        const float PX = 0.995f;                                  // side panel centre (40 mm panels)

        static readonly Quaternion SideRot = Quaternion.Euler(0f, -90f, 0f);
        static readonly float[] TierY = { 0.92f, 1.30f, 1.66f };
        static readonly float[] TierClear = { 0.34f, 0.32f, 0.24f };

        static void Box(MeshBuilder b, float x0, float x1, float y0, float y1, float z0, float z1, int sub)
            => b.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), sub);

        /// <summary>Convex side-profile polygon given as (z, y) points, extruded across X (centred on x, thick wide).</summary>
        static void Side(MeshBuilder b, float x, float thick, int sub, params Vector2[] zy)
            => b.AddExtrude(zy, new Vector3(x, 0f, 0f), SideRot, thick, sub);

        static Vector2 P(float z, float y) => new Vector2(z, y);

        public static GameObject BuildBody(Transform parent)
        {
            var m = new LandModel(); var b = m.B;
            int W = m.M(SurfaceKind.PaintedWhite), G = m.M(SurfaceKind.Glass), D = m.M(SurfaceKind.DarkPlastic), K = m.M(SurfaceKind.MatteBlack),
                S = m.M(SurfaceKind.BrushedSteel), Gal = m.M(SurfaceKind.Galvanized), Ply = m.M(SurfaceKind.Plywood), Red = m.M(SurfaceKind.TruckPaint);
            float arch = 0.43f;

            for (int sx = -1; sx <= 1; sx += 2)
            {
                float x = sx * PX, xo = x - 0.02f, xi = x + 0.02f;
                Box(b, xo, xi, 0.80f, RoofY, RearZ, CabZ, W);                                   // cargo side, above the arches
                Box(b, xo, xi, 0.30f, 0.80f, RearZ, RearAxleZ - arch, W);                       // behind the rear wheel
                Box(b, xo, xi, 0.30f, 0.80f, RearAxleZ + arch, FrontAxleZ - arch, W);           // between the wheels (cargo + cab door bottom)
                Box(b, xo, xi, 0.80f, 1.22f, CabZ, CowlZ, W);                                   // cab door below the glass
                Box(b, xo, xi, 1.22f, RoofY, CabZ, 0.64f, W);                                   // B pillar
                Side(b, x, 0.04f, W, P(0.64f, 1.92f), P(1.31f, 1.92f), P(1.22f, 2.08f), P(0.64f, 2.08f));     // door header
                Side(b, x, 0.04f, W, P(1.83f, 1.22f), P(1.97f, 1.22f), P(1.24f, 2.06f), P(1.31f, 1.92f));     // A pillar
                Side(b, x, 0.012f, G, P(0.64f, 1.22f), P(1.83f, 1.22f), P(1.31f, 1.92f), P(0.64f, 1.92f));    // door glass
                foreach (float az in new[] { RearAxleZ, FrontAxleZ })
                    Box(b, x - 0.026f, x + 0.026f, 0.79f, 0.84f, az - arch - 0.03f, az + arch + 0.03f, K);     // arch trim
                // mirror, door handle
                b.AddBox(new Vector3(sx * 1.07f, 1.38f, 1.80f), new Vector3(0.12f, 0.04f, 0.05f), D);
                b.AddBox(new Vector3(sx * 1.17f, 1.42f, 1.80f), new Vector3(0.07f, 0.26f, 0.17f), D);
                b.AddBox(new Vector3(sx * 1.02f, 1.12f, 0.72f), new Vector3(0.02f, 0.03f, 0.14f), D);
                // rear corner pillar + tail light, headlight
                Box(b, sx * HingeX, sx * 1.015f, 0.30f, RoofY, RearZ, RearZ + 0.06f, W);
                b.AddBox(new Vector3(sx * 0.957f, 1.35f, RearZ - 0.005f), new Vector3(0.09f, 0.55f, 0.02f), Red);
                b.AddBox(new Vector3(sx * 0.76f, 0.90f, NoseZ - 0.005f), new Vector3(0.36f, 0.18f, 0.05f), S);
                b.AddBox(new Vector3(sx * 0.76f, 0.90f, NoseZ + 0.022f), new Vector3(0.34f, 0.16f, 0.006f), G);
                // wheel housings inside
                Box(b, sx * 0.685f, sx * 0.975f, FloorTop, 0.87f, RearAxleZ - 0.46f, RearAxleZ + 0.46f, W);
                Box(b, sx * 0.685f, sx * 0.975f, FloorTop, 0.86f, FrontAxleZ - 0.43f, CowlZ, D);
                // A pillar along the windshield
                b.AddBox(new Vector3(sx * 0.955f, 1.635f, 1.60f), new Vector3(0.10f, 1.10f, 0.07f), Quaternion.Euler(-42.3f, 0f, 0f), W);
                // seats
                float seatX = sx * 0.48f;
                Box(b, seatX - 0.22f, seatX + 0.22f, FloorTop, 0.91f, 0.76f, 1.20f, K);
                Box(b, seatX - 0.26f, seatX + 0.26f, 0.91f, 1.05f, 0.73f, 1.23f, D);
                b.AddBox(new Vector3(seatX, 1.36f, 0.70f), new Vector3(0.50f, 0.66f, 0.12f), Quaternion.Euler(-10f, 0f, 0f), D);
                b.AddBox(new Vector3(seatX, 1.77f, 0.655f), new Vector3(0.26f, 0.18f, 0.10f), D);
            }

            // roof, floor, underbody, drivetrain
            Box(b, -1.015f, 1.015f, 2.06f, RoofY, RearZ, 1.22f, W);
            Box(b, -0.975f, 0.975f, 0.56f, 0.62f, RearZ, CowlZ, K);
            b.AddBox(new Vector3(0f, 0.6275f, (RearZ + CabZ) * 0.5f), new Vector3(1.90f, 0.015f, CabZ - RearZ - 0.06f), Quaternion.identity, Ply, Vector3.forward);
            Box(b, -0.74f, 0.74f, 0.30f, 0.56f, -2.5f, 1.55f, K);
            b.AddCylinder(new Vector3(0f, TireRadius, RearAxleZ), Quaternion.Euler(0f, 0f, 90f), 0.06f, 1.6f, 10, K);
            Box(b, -0.15f, 0.15f, 0.17f, 0.50f, RearAxleZ - 0.16f, RearAxleZ + 0.16f, K);           // differential: 0.17 m ground clearance
            Box(b, -0.74f, 0.74f, 0.30f, 0.80f, FrontAxleZ - arch, FrontAxleZ + arch, K);           // engine bay between the front wheels

            // nose: fenders/hood, grille, bumpers, lights
            Box(b, -1.015f, 1.015f, 0.30f, 0.80f, FrontAxleZ + arch, NoseZ, W);
            Side(b, 0f, Width, W, P(CowlZ, 0.80f), P(NoseZ, 0.80f), P(NoseZ, 0.95f), P(2.80f, 1.08f), P(CowlZ, 1.25f));
            Box(b, -0.55f, 0.55f, 0.55f, 0.89f, NoseZ - 0.01f, NoseZ + 0.015f, K);
            for (int i = 0; i < 3; i++) Box(b, -0.5f, 0.5f, 0.615f + i * 0.1f, 0.64f + i * 0.1f, NoseZ + 0.015f, NoseZ + 0.024f, S);
            Box(b, -1.015f, 1.015f, 0.32f, 0.56f, 2.90f, 3.0f, D);                                   // front bumper
            Box(b, -1.0f, 1.0f, 0.34f, 0.54f, -3.0f, -2.91f, D);                                    // rear step bumper
            Box(b, -0.15f, 0.15f, 0.38f, 0.53f, -3.004f, -3.0f, W);                                 // plate
            Box(b, -HingeX, HingeX, 2.02f, RoofY, RearZ, RearZ + 0.06f, W);                         // rear header

            // windshield, header, dash, column
            b.AddBox(new Vector3(0f, 1.635f, 1.60f), new Vector3(1.84f, 1.04f, 0.012f), Quaternion.Euler(-42.3f, 0f, 0f), G);
            Box(b, -1.015f, 1.015f, 2.00f, RoofY, 1.19f, 1.29f, W);
            Box(b, -0.975f, 0.975f, 0.95f, 1.25f, 1.49f, CowlZ, D);
            Box(b, -0.69f, -0.27f, 1.24f, 1.33f, 1.55f, 1.78f, D);
            Box(b, -0.15f, 0.15f, 0.86f, 1.16f, 1.38f, 1.50f, D);
            b.AddBox(new Vector3(-0.48f, 1.22f, 1.50f), new Vector3(0.07f, 0.07f, 0.22f), Quaternion.Euler(25f, 0f, 0f), K);

            // sliding side door (right): proud panel, seams, track, handle
            Box(b, 1.015f, 1.027f, 0.34f, 2.0f, -0.71f, 0.51f, W);
            Box(b, 1.013f, 1.021f, 0.34f, 2.0f, -0.728f, -0.712f, K);
            Box(b, 1.013f, 1.021f, 0.34f, 2.0f, 0.512f, 0.528f, K);
            Box(b, 1.013f, 1.023f, 1.20f, 1.235f, -2.65f, -0.75f, K);
            Box(b, 1.027f, 1.045f, 1.13f, 1.17f, 0.30f, 0.46f, D);

            // roof ladder rack: two crossbars on feet + side rails
            foreach (float z in new[] { -2.35f, 0.55f })
            {
                Box(b, -0.975f, 0.975f, RackY - 0.04f, RackY, z - 0.025f, z + 0.025f, Gal);
                for (int sx = -1; sx <= 1; sx += 2) Box(b, sx * 0.905f, sx * 0.955f, RoofY, RackY - 0.04f, z - 0.03f, z + 0.03f, K);
            }
            for (int sx = -1; sx <= 1; sx += 2) b.AddRod(new Vector3(sx * 0.93f, RackY + 0.02f, -0.9f), Quaternion.identity, 0.016f, 3.2f, 8, Gal);

            // steel shelving, 14" deep, 3 tiers (the right unit stops at the sliding door)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                float z0 = -2.80f, z1 = sx < 0 ? 0.40f : -0.80f, xc = sx * (0.975f - ShelfDepth * 0.5f), xf = sx * (0.975f - ShelfDepth);
                foreach (float z in new[] { z0, (z0 + z1) * 0.5f, z1 })
                {
                    Box(b, xf, xf + sx * 0.03f, FloorTop, 1.92f, z - 0.015f, z + 0.015f, Gal);
                    Box(b, sx * 0.945f, sx * 0.975f, FloorTop, 1.92f, z - 0.015f, z + 0.015f, Gal);
                }
                foreach (float y in TierY)
                {
                    b.AddBox(new Vector3(xc, y, (z0 + z1) * 0.5f), new Vector3(ShelfDepth, 0.02f, z1 - z0), Gal);
                    b.AddBox(new Vector3(xf + sx * 0.008f, y + 0.035f, (z0 + z1) * 0.5f), new Vector3(0.015f, 0.07f, z1 - z0), Gal);
                }
            }
            // a few parts bins that are always on the top shelf
            Color[] binCol = { new Color(0.80f, 0.12f, 0.10f), new Color(0.10f, 0.30f, 0.75f), new Color(0.95f, 0.75f, 0.10f) };
            int k = 0;
            foreach (var s in AllSlots(true))
                b.AddBox(s.Base + Vector3.up * 0.08f, new Vector3(0.28f, 0.16f, 0.40f), m.M(SurfaceKind.Porcelain, binCol[k++ % 3]));

            return m.Make(parent, "Body");
        }

        /// <summary>One rear barn door; mesh origin at the hinge (bottom, outer skin), closed door runs toward +X*dir.</summary>
        public static GameObject BuildRearDoor(Transform parent, bool left)
        {
            float dir = left ? 1f : -1f;
            var m = new LandModel(); var b = m.B;
            int W = m.M(SurfaceKind.PaintedWhite), D = m.M(SurfaceKind.DarkPlastic), S = m.M(SurfaceKind.BrushedSteel), G = m.M(SurfaceKind.Glass), K = m.M(SurfaceKind.MatteBlack);
            float w = HingeX - 0.005f;
            b.AddBox(new Vector3(dir * w * 0.5f, 1.29f, 0.0225f), new Vector3(w, 1.46f, 0.045f), W);
            b.AddBox(new Vector3(dir * w * 0.5f, 1.62f, -0.004f), new Vector3(w - 0.24f, 0.44f, 0.006f), K);     // rear window (dark)
            b.AddBox(new Vector3(dir * w * 0.5f, 1.62f, -0.008f), new Vector3(w - 0.26f, 0.42f, 0.004f), G);
            b.AddBox(new Vector3(dir * (w - 0.07f), 1.15f, -0.012f), new Vector3(0.05f, 0.18f, 0.02f), D);
            foreach (float y in new[] { 0.80f, 1.80f }) b.AddBox(new Vector3(dir * 0.012f, y, -0.01f), new Vector3(0.03f, 0.10f, 0.02f), S);
            var go = m.Make(parent, left ? "RearDoorL" : "RearDoorR");
            go.transform.localPosition = new Vector3(left ? -HingeX : HingeX, 0f, RearZ);
            return go;
        }

        public static GameObject BuildWheel(Transform parent, Vector3 localPos)
        {
            var m = new LandModel(); var b = m.B;
            int T = m.M(SurfaceKind.Tire), S = m.M(SurfaceKind.BrushedSteel), K = m.M(SurfaceKind.MatteBlack);
            var along = Quaternion.Euler(0f, 0f, 90f);
            b.AddCylinder(Vector3.zero, along, TireRadius, TireWidth, 24, T);
            b.AddCylinder(Vector3.zero, along, 0.215f, TireWidth + 0.008f, 16, S);
            b.AddCylinder(Vector3.zero, along, 0.07f, TireWidth + 0.03f, 10, K);
            var go = m.Make(parent, "Wheel");
            go.transform.localPosition = localPos;
            return go;
        }

        /// <summary>Steering wheel (15" rim) around local Y; the caller tilts it toward the driver.</summary>
        public static GameObject BuildSteeringWheel(Transform parent)
        {
            var m = new LandModel(); var b = m.B;
            int D = m.M(SurfaceKind.DarkPlastic);
            b.AddRing(Vector3.zero, Quaternion.identity, 0.19f, 0.162f, 0.03f, 20, D);
            b.AddCylinder(Vector3.zero, 0.055f, 0.04f, 12, D);
            b.AddBox(Vector3.zero, new Vector3(0.34f, 0.02f, 0.035f), D);
            b.AddBox(new Vector3(0f, 0f, -0.085f), new Vector3(0.035f, 0.02f, 0.17f), D);
            var go = m.Make(parent, "SteeringWheel");
            go.transform.localPosition = new Vector3(-0.48f, 1.27f, 1.40f);
            return go;
        }

        public static GameObject BuildRackLadder(Transform parent)
        {
            var m = new LandModel();
            TownLadder.AddLadder(m.B, m.M(SurfaceKind.BrushedSteel), m.M(SurfaceKind.DarkPlastic), TownLadder.ClosedLength);
            var go = m.Make(parent, "RackLadder");
            go.transform.localPosition = new Vector3(-0.45f, RackY + 0.12f, -2.75f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);     // rails along the van, lying flat
            return go;
        }

        // ------------------------------------------------------------------ cargo shown on the shelves

        public struct ShelfSlot { public Vector3 Base; public float Clear; }

        /// <summary>Shelf positions (0.5 m apart). fixedBins = the top-shelf spots that always hold a parts bin.</summary>
        static IEnumerable<ShelfSlot> AllSlots(bool fixedBins)
        {
            for (int tier = 0; tier < 3; tier++)
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    int n = sx < 0 ? 6 : 4;
                    for (int i = 0; i < n; i++)
                    {
                        bool bin = tier == 2 && (sx < 0 ? i % 2 == 0 : i % 2 == 1);
                        if (bin != fixedBins) continue;
                        yield return new ShelfSlot { Base = new Vector3(sx * (0.975f - ShelfDepth * 0.5f), TierY[tier] + 0.01f, -2.55f + i * 0.5f), Clear = TierClear[tier] };
                    }
                }
        }

        static List<ShelfSlot> _slots;
        static List<ShelfSlot> Slots
        {
            get
            {
                if (_slots != null) return _slots;
                _slots = new List<ShelfSlot>(AllSlots(false));
                var r = new System.Random(4242);
                for (int i = _slots.Count - 1; i > 0; i--) { int j = r.Next(i + 1); var t = _slots[i]; _slots[i] = _slots[j]; _slots[j] = t; }
                return _slots;
            }
        }

        /// <summary>Cheap picture of the load: boxes/bins/buckets on the shelves by how full the van is, the sheet stack and
        /// boards on the floor, long stock on the rack. One mesh, rebuilt when the cargo changes.</summary>
        public static GameObject BuildCargo(Transform parent)
        {
            var m = new LandModel(); var b = m.B;
            int card = m.M(SurfaceKind.PaperBag), white = m.M(SurfaceKind.PaintedWhite), pine = m.M(SurfaceKind.PineLumber), ply = m.M(SurfaceKind.Plywood);
            Color[] cols = { new Color(0.15f, 0.45f, 0.20f), new Color(0.85f, 0.40f, 0.08f), new Color(0.30f, 0.32f, 0.36f), new Color(0.10f, 0.30f, 0.75f) };
            var slots = Slots;
            float used = VanCargo.UsedCuFt;
            int n = used > 0.01f ? Mathf.Clamp(Mathf.CeilToInt(used / VanCargo.CapacityCuFt * slots.Count), 1, slots.Count) : 0;
            for (int i = 0; i < n; i++)
            {
                var s = slots[i]; var r = new System.Random(977 + i * 31);
                float f = (float)r.NextDouble();
                switch (r.Next(4))
                {
                    case 0:
                    {
                        float h = s.Clear * Mathf.Lerp(0.55f, 0.9f, f);
                        b.AddBox(s.Base + Vector3.up * h * 0.5f, new Vector3(0.27f + f * 0.05f, h, 0.32f + f * 0.12f), card); break;
                    }
                    case 1:
                        b.AddBox(s.Base + Vector3.up * 0.08f, new Vector3(0.28f, 0.16f, 0.40f), m.M(SurfaceKind.Porcelain, cols[r.Next(cols.Length)])); break;
                    case 2:
                    {
                        float h = Mathf.Min(0.37f, s.Clear - 0.02f);
                        b.AddCylinder(s.Base + Vector3.up * h * 0.5f, 0.145f, h, 14, white); break;       // 5 gal bucket
                    }
                    default:
                        b.AddBox(s.Base + new Vector3(0f, 0.07f, -0.11f), new Vector3(0.24f, 0.14f, 0.19f), card);
                        b.AddBox(s.Base + new Vector3(0f, 0.05f, 0.11f), new Vector3(0.20f, 0.10f, 0.17f), m.M(SurfaceKind.Porcelain, cols[r.Next(cols.Length)])); break;
                }
            }

            // floor: sheets flat between the shelves, boards on top
            float top = FloorTop + 0.003f;
            int sheets = VanCargo.SheetCount;
            if (sheets > 0)
            {
                float h = sheets * 0.5f * Units.Inch;
                b.AddBox(new Vector3(0f, top + h * 0.5f, -1.62f), new Vector3(1.22f, h, 2.44f), Quaternion.identity, ply, Vector3.forward);
                top += h;
            }
            int boards = 0, rack = 0;
            foreach (var kv in VanCargo.Entries)
            {
                var d = Catalog.Get(kv.Key);
                if (d == null) continue;
                float len = VanCargo.LongestDimension(kv.Key);
                if (VanCargo.RidesOnRack(kv.Key))
                {
                    for (int i = 0; i < kv.Value && rack < VanCargo.RackMaxPieces; i++, rack++)
                        b.AddBox(new Vector3(0.15f + (rack % 3) * 0.2f, RackY + 0.02f + (rack / 3) * 0.04f, -0.9f), new Vector3(0.14f, 0.038f, len), Quaternion.identity, pine, Vector3.forward);
                }
                else if (d.Category == PieceCategory.Lumber)
                {
                    for (int i = 0; i < kv.Value && boards < 16; i++, boards++)
                        b.AddBox(new Vector3(-0.42f + (boards % 8) * 0.12f, top + 0.02f + (boards / 8) * 0.04f, -1.3f), new Vector3(0.089f, 0.038f, Mathf.Min(len, 3.0f)), Quaternion.identity, pine, Vector3.forward);
                }
            }
            return m.Make(parent, "Cargo");
        }
    }
}
