using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Shared real-size parts for heavy equipment: hydraulic rams that really telescope (barrel on one pin, rod on the other,
    /// both re-aimed every frame, no scaling), rubber track assemblies, tyres and an operator seat. Colors: machine yellow,
    /// counterweight gray. All meshes via MeshBuilder with UVs in meters.
    /// </summary>
    public static class EquipModels
    {
        public static readonly Color Yellow = new Color(0.97f, 0.70f, 0.07f);
        public static readonly Color Gray = new Color(0.22f, 0.22f, 0.23f);
        public static readonly Color White = new Color(0.92f, 0.92f, 0.90f);
        public static readonly Color Orange = new Color(0.95f, 0.42f, 0.06f);

        public static int PaintY(CTool.Mats m) => m.K(SurfaceKind.PaintedSteel, Yellow);
        public static int PaintG(CTool.Mats m) => m.K(SurfaceKind.PaintedSteel, Gray);
        public static int Black(CTool.Mats m) => m.K(SurfaceKind.MatteBlack);
        public static int Chrome(CTool.Mats m) => m.K(SurfaceKind.BrushedSteel);
        public static int Rubber(CTool.Mats m) => m.K(SurfaceKind.Tire);
        public static int Glass(CTool.Mats m) => m.K(SurfaceKind.Glass);

        /// <summary>Profile (z, y) points as a convex prism extruded along X (width), centred at x.</summary>
        public static void SideProfile(MeshBuilder b, float x, float width, int sub, params Vector2[] zy)
        {
            var poly = new Vector2[zy.Length];
            for (int i = 0; i < zy.Length; i++) poly[i] = new Vector2(-zy[i].x, zy[i].y);
            b.AddExtrude(poly, new Vector3(x, 0, 0), Quaternion.Euler(0, 90, 0), width, sub);
        }

        /// <summary>Rubber track loop seen from the side: two idler drums joined by top and bottom runs, sprocket and rollers.
        /// x = track centre line, length = idler centre spacing, h = loop height, w = shoe width.</summary>
        public static void Track(MeshBuilder b, CTool.Mats m, float x, float length, float h, float w, float y0 = 0f)
        {
            int rub = Rubber(m), blk = Black(m), steel = Chrome(m);
            float r = h * 0.5f, cy = y0 + r;
            var rot = Quaternion.Euler(0, 0, 90);
            b.AddCylinder(new Vector3(x, cy, length * 0.5f), rot, r, w, 18, rub);
            b.AddCylinder(new Vector3(x, cy, -length * 0.5f), rot, r, w, 18, rub);
            b.AddBox(new Vector3(x, y0 + h - 0.025f, 0), new Vector3(w, 0.05f, length), rub);
            b.AddBox(new Vector3(x, y0 + 0.025f, 0), new Vector3(w, 0.05f, length), rub);
            // tread lugs on the bottom and top runs
            for (float z = -length * 0.5f; z <= length * 0.5f; z += 0.11f)
            {
                b.AddBox(new Vector3(x, y0 + 0.008f, z), new Vector3(w * 0.92f, 0.016f, 0.045f), rub);
                b.AddBox(new Vector3(x, y0 + h - 0.002f, z), new Vector3(w * 0.92f, 0.016f, 0.045f), rub);
            }
            // track frame, rollers, sprocket hub
            b.AddBox(new Vector3(x, cy, 0), new Vector3(w * 0.55f, h * 0.55f, length), blk);
            for (int i = -2; i <= 2; i++) b.AddCylinder(new Vector3(x, y0 + 0.09f, i * length * 0.18f), rot, 0.07f, w * 0.7f, 10, steel);
            b.AddCylinder(new Vector3(x, cy, -length * 0.5f), rot, r * 0.55f, w + 0.04f, 10, blk);
            for (int t = 0; t < 8; t++)
            {
                float a = t * Mathf.PI / 4f;
                b.AddBox(new Vector3(x, cy + Mathf.Sin(a) * r * 0.6f, -length * 0.5f + Mathf.Cos(a) * r * 0.6f), new Vector3(w + 0.05f, 0.05f, 0.05f), Quaternion.Euler(-a * Mathf.Rad2Deg, 0, 0), steel);
            }
        }

        /// <summary>Tyre + rim with the axle along X, centred at c.</summary>
        public static void Wheel(MeshBuilder b, CTool.Mats m, Vector3 c, float radius, float width, Color rim)
        {
            var rot = Quaternion.Euler(0, 0, 90);
            int rub = Rubber(m), rimSub = m.K(SurfaceKind.PaintedSteel, rim);
            b.AddCylinder(c, rot, radius, width, 22, rub);
            float side = Mathf.Sign(c.x == 0 ? 1 : c.x);
            b.AddCylinder(c + new Vector3(side * width * 0.02f, 0, 0), rot, radius * 0.6f, width * 1.02f, 16, rimSub);
            b.AddCylinder(c + new Vector3(side * width * 0.06f, 0, 0), rot, radius * 0.2f, width * 1.04f, 8, Chrome(m));
            for (int i = 0; i < 12; i++)   // tread blocks
            {
                float a = i * Mathf.PI / 6f;
                b.AddBox(c + new Vector3(0, Mathf.Sin(a) * radius, Mathf.Cos(a) * radius), new Vector3(width * 0.9f, 0.03f, 0.07f), Quaternion.Euler(-a * Mathf.Rad2Deg + 90f, 0, 0), rub);
            }
        }

        /// <summary>Suspension seat with armrests, facing +Z, seat cushion top at y.</summary>
        public static void Seat(MeshBuilder b, CTool.Mats m, Vector3 at)
        {
            int blk = Black(m), gray = PaintG(m);
            b.AddBox(at + new Vector3(0, -0.2f, 0), new Vector3(0.3f, 0.3f, 0.3f), gray);
            b.AddBevelBox(at + new Vector3(0, -0.04f, 0.02f), new Vector3(0.48f, 0.09f, 0.46f), Quaternion.identity, 0.03f, blk);
            b.AddBevelBox(at + new Vector3(0, 0.3f, -0.22f), new Vector3(0.46f, 0.6f, 0.1f), Quaternion.identity, 0.03f, blk);
            b.AddBox(at + new Vector3(-0.29f, 0.12f, 0.05f), new Vector3(0.08f, 0.07f, 0.36f), blk);
            b.AddBox(at + new Vector3(0.29f, 0.12f, 0.05f), new Vector3(0.08f, 0.07f, 0.36f), blk);
        }

        /// <summary>Joystick on a pod (pilot control), top at y.</summary>
        public static void Joystick(MeshBuilder b, CTool.Mats m, Vector3 at)
        {
            int blk = Black(m);
            b.AddBox(at + new Vector3(0, -0.08f, 0), new Vector3(0.12f, 0.06f, 0.16f), blk);
            b.AddCylinder(at + new Vector3(0, 0.02f, 0), 0.012f, 0.16f, 8, blk);
            b.AddCylinder(at + new Vector3(0, 0.11f, 0), 0.025f, 0.06f, 10, blk);
        }
    }

    /// <summary>A hydraulic cylinder between two pins. The barrel hangs on pin A and the chrome rod on pin B; every LateUpdate
    /// both are aimed at each other so the rod slides in and out of the barrel as the joint moves.</summary>
    public class HydraulicRam : MonoBehaviour
    {
        public Transform A, B;
        Transform _barrel, _rod;

        /// <summary>Creates the ram. barrelLen + rodLen must exceed the longest pin distance.</summary>
        public static HydraulicRam Make(Transform pinA, Transform pinB, float barrelR, float barrelLen, float rodR, float rodLen, Color paint)
        {
            var host = new GameObject("Ram");
            host.transform.SetParent(pinA, false);
            var ram = host.AddComponent<HydraulicRam>();
            ram.A = pinA; ram.B = pinB;
            ram._barrel = CTool.Build("Barrel", (b, m) =>
            {
                int p = m.K(SurfaceKind.PaintedSteel, paint);
                b.AddRod(new Vector3(0, 0, barrelLen * 0.5f), Quaternion.identity, barrelR, barrelLen, 14, p);
                b.AddCylinder(Vector3.zero, Quaternion.Euler(0, 0, 90), barrelR * 0.9f, barrelR * 2.2f, 10, p);   // pin eye
                b.AddRod(new Vector3(0, barrelR * 0.7f, barrelLen * 0.6f), Quaternion.identity, barrelR * 0.25f, barrelLen * 0.7f, 6, m.K(SurfaceKind.MatteBlack)); // hose
            }, pinA).transform;
            ram._rod = CTool.Build("Rod", (b, m) =>
            {
                int c = m.K(SurfaceKind.BrushedSteel);
                b.AddRod(new Vector3(0, 0, rodLen * 0.5f), Quaternion.identity, rodR, rodLen, 12, c);
                b.AddCylinder(Vector3.zero, Quaternion.Euler(0, 0, 90), rodR * 1.6f, rodR * 3.5f, 10, m.K(SurfaceKind.PaintedSteel, paint));
            }, pinB).transform;
            ram.Aim();
            return ram;
        }

        void LateUpdate() => Aim();

        public void Aim()
        {
            if (A == null || B == null || _barrel == null || _rod == null) return;
            Vector3 d = B.position - A.position;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector3 up = A.parent != null ? A.parent.up : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(d.normalized, up)) > 0.98f) up = A.parent != null ? A.parent.forward : Vector3.forward;
            _barrel.rotation = Quaternion.LookRotation(d, up);
            _rod.rotation = Quaternion.LookRotation(-d, up);
        }
    }
}
