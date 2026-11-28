using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Real-size procedural models of the company premises' shop equipment (saw station, forklift, air compressor,
    /// two-post lift, diesel tank on a skid) and fit-out details (rolling tool chest, job box, fire extinguisher + first-aid
    /// cabinet). Every model is authored in its own local frame: y = 0 on the floor, the working/front side toward local -Z
    /// and the wall (if any) behind it toward +Z. Geometry goes into a <see cref="SitePart"/> through MeshBuilder.Transform,
    /// so many items share one mesh; UVs stay in meters. Colliders are reported through <see cref="ColFn"/> in the
    /// caller's frame (yaw must be a multiple of 90° so boxes stay axis-aligned).</summary>
    internal static class YardEquipmentModels
    {
        public delegate void ColFn(Vector3 center, Vector3 size);

        struct Frame { public Vector3 O; public Quaternion Q; }

        static Frame Begin(SitePart p, Vector3 origin, float yaw)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            p.B.Transform = Matrix4x4.TRS(origin, q, Vector3.one);
            return new Frame { O = origin, Q = q };
        }

        static void End(SitePart p) => p.B.ResetTransform();

        /// <summary>Collider from a local min/max box.</summary>
        static void LCol(ColFn col, Frame f, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            if (col == null) return;
            var c = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
            var s = f.Q * new Vector3(x1 - x0, y1 - y0, z1 - z0);
            col(f.O + f.Q * c, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        /// <summary>Round tube / rod from a to b.</summary>
        static void Tube(SitePart p, Vector3 a, Vector3 b, float r, int sub, int segs = 10)
        {
            Vector3 d = b - a; float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 up = Mathf.Abs(d.y) > 0.95f * len ? Vector3.forward : Vector3.up;
            p.B.AddRod((a + b) * 0.5f, Quaternion.LookRotation(d / len, up), r, len, segs, sub);
        }

        /// <summary>Hose along a polyline, with a small ball at every bend so joints don't gap.</summary>
        static void Hose(SitePart p, IList<Vector3> pts, float r, int sub)
        {
            for (int i = 0; i + 1 < pts.Count; i++) Tube(p, pts[i], pts[i + 1], r, sub, 8);
            for (int i = 1; i + 1 < pts.Count; i++) p.B.AddCylinder(pts[i], Quaternion.identity, r * 1.02f, r * 2f, 8, sub);
        }

        /// <summary>Disc (cylinder) whose axis runs along X.</summary>
        static void DiscX(SitePart p, Vector3 c, float r, float w, int segs, int sub)
            => p.B.AddCylinder(c, Quaternion.Euler(0f, 0f, 90f), r, w, segs, sub);

        static void DiscZ(SitePart p, Vector3 c, float r, float w, int segs, int sub)
            => p.B.AddCylinder(c, Quaternion.Euler(90f, 0f, 0f), r, w, segs, sub);

        /// <summary>Partial disc (angle range in degrees, 0 = +Z, 90 = +Y) extruded along X: blade guards.</summary>
        static void SectorX(SitePart p, Vector3 c, float r, float fromDeg, float toDeg, float width, int sub)
        {
            var pts = new List<Vector2>();
            int n = 14;
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)n) * Mathf.Deg2Rad;
                // extrude frame Euler(0,90,0): poly u -> world -Z, v -> world Y
                pts.Add(new Vector2(-Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }
            if (toDeg - fromDeg <= 180f) pts.Add(Vector2.zero);   // wedge; wider sectors close with a chord (stays convex)
            p.B.AddExtrude(pts, c, Quaternion.Euler(0f, 90f, 0f), width, sub);
        }

        static void Lumber2x4(SitePart p, Vector3 center, float length, bool alongX, int side, int end)
        {
            const float w = 0.089f, t = 0.038f;
            p.B.AddBevelBox(center, new Vector3(w, t, length), alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity, 0.003f, side, end);
        }

        static void Label(Transform parent, Frame f, string text, Vector3 local, float height, Color color)
        {
            if (parent == null) return;
            var go = new GameObject("Label " + text.Replace('\n', ' '));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = f.O + f.Q * local;
            go.transform.localRotation = f.Q;
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font; tm.text = text; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontSize = 64; tm.characterSize = height / 6.4f; tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        // ================================================================== saw station

        /// <summary>Miter-saw station against a wall: a 12" sliding compound miter saw on a steel stand with 12' of plywood
        /// infeed/outfeed wings, an aluminium fence with tape and a flip stop, a shop vac on the dust port, cut-offs and a
        /// stack of cut 2x4s, plus a 10" cabinet table saw out in front (feeding toward the bench). Bench runs along local X;
        /// the wall is ~0.6 m behind the origin.</summary>
        public static void SawStation(SitePart p, Vector3 origin, float yaw, ColFn col)
        {
            var f = Begin(p, origin, yaw);
            int steel = p.M(SurfaceKind.PaintedSteel, new Color(0.18f, 0.19f, 0.21f));
            int alu = p.M(SurfaceKind.BrushedSteel), ply = p.M(SurfaceKind.Plywood);
            int yellow = p.M(SurfaceKind.PaintedSteel, new Color(0.98f, 0.76f, 0.08f)), black = p.M(SurfaceKind.MatteBlack);
            int plastic = p.M(SurfaceKind.DarkPlastic), glass = p.M(SurfaceKind.Glass), red = p.M(SurfaceKind.PaintedSteel, new Color(0.8f, 0.1f, 0.07f));
            int pine = p.M(SurfaceKind.PineLumber), endg = p.M(SurfaceKind.PineEndGrain);
            int vacRed = p.M(SurfaceKind.PaintedSteel, new Color(0.72f, 0.12f, 0.1f));

            const float wingL = 1.83f, cx = 0.45f, zF = -0.30f, zB = 0.25f, top = 0.905f;
            // --- centre stand: steel top plate on four square-tube legs, the saw bolted to it
            p.Box(-cx, cx, 0.80f, 0.82f, zF, zB, steel);
            foreach (float x in new[] { -cx + 0.02f, cx - 0.06f })
                foreach (float z in new[] { zF + 0.02f, zB - 0.06f })
                {
                    p.Box(x, x + 0.04f, 0.02f, 0.80f, z, z + 0.04f, steel);
                    p.Box(x - 0.02f, x + 0.06f, 0f, 0.02f, z - 0.02f, z + 0.06f, steel);   // foot
                }
            p.Box(-cx, cx, 0.20f, 0.23f, zF + 0.02f, zB - 0.02f, steel);                     // lower shelf
            // --- wings: plywood tops flush with the saw table, steel aprons, end legs
            foreach (int s in new[] { -1, 1 })
            {
                float a = s * cx, b = s * wingL;
                float xa = Mathf.Min(a, b), xb = Mathf.Max(a, b);
                p.Box(xa, xb, top - 0.019f, top, zF, zB, ply);
                p.Box(xa, xb, 0.80f, top - 0.019f, zF, zF + 0.04f, steel);
                p.Box(xa, xb, 0.80f, top - 0.019f, zB - 0.04f, zB, steel);
                float lx = s < 0 ? xa + 0.02f : xb - 0.06f;
                foreach (float z in new[] { zF + 0.02f, zB - 0.06f })
                {
                    p.Box(lx, lx + 0.04f, 0.02f, 0.80f, z, z + 0.04f, steel);
                    p.Box(lx - 0.02f, lx + 0.06f, 0f, 0.02f, z - 0.02f, z + 0.06f, steel);
                }
                p.Box(lx, lx + 0.04f, 0.18f, 0.22f, zF + 0.06f, zB - 0.06f, steel);            // stretcher
                // aluminium fence on the wing, in line with the saw fence
                float fa = s < 0 ? xa : xa + 0.30f, fb = s < 0 ? xb - 0.30f : xb;
                p.Box(fa, fb, top, top + 0.08f, 0.06f, 0.10f, alu);
            }
            p.Box(-wingL + 0.06f, -cx - 0.02f, 0.22f, 0.24f, zF + 0.04f, zB - 0.04f, ply);   // left wing lower shelf
            // tape on the right fence + tick marks
            p.Box(cx + 0.30f, wingL, top + 0.08f, top + 0.082f, 0.064f, 0.096f, yellow);
            for (float x = cx + 0.32f; x < wingL - 0.02f; x += 0.1016f) p.Box(x, x + 0.003f, top + 0.082f, top + 0.0825f, 0.064f, 0.08f, black);
            // flip stop on the right fence, flipped down
            const float stopX = 1.25f;
            p.Box(stopX - 0.04f, stopX + 0.04f, top + 0.08f, top + 0.12f, 0.055f, 0.11f, red);
            p.Box(stopX - 0.035f, stopX + 0.035f, top + 0.005f, top + 0.10f, 0.035f, 0.055f, red);
            p.CylY(stopX, 0.13f, top + 0.08f, top + 0.13f, 0.015f, 10, black);
            // a few cut blocks lying on the outfeed wing, against the fence
            for (int i = 0; i < 3; i++)
                Lumber2x4(p, new Vector3(stopX - 0.045f - 0.184f, top + 0.019f + (i == 2 ? 0.038f : 0f), 0.06f - 0.045f - (i == 1 ? 0.09f : 0f)), 0.368f, true, pine, endg);

            // --- the saw: 12" double-bevel sliding compound miter saw, head up, slid back
            p.Box(-0.25f, 0.25f, 0.82f, 0.89f, -0.22f, 0.18f, alu);                           // cast base
            foreach (int s in new[] { -1, 1 }) p.Box(s > 0 ? 0.25f : -0.34f, s > 0 ? 0.34f : -0.25f, 0.86f, top, -0.14f, 0.06f, alu);   // base extensions
            p.CylY(0f, -0.02f, 0.885f, top, 0.17f, 32, alu);                                    // turntable
            p.Box(-0.015f, 0.015f, top, top + 0.001f, -0.19f, 0.06f, plastic);                 // kerf plate
            p.Box(-0.20f, 0.20f, 0.86f, 0.885f, -0.245f, -0.215f, alu);                         // miter scale
            p.Box(-0.035f, 0.035f, 0.87f, 0.895f, -0.34f, -0.18f, alu);                         // miter arm
            p.Box(-0.03f, 0.03f, 0.86f, 0.93f, -0.37f, -0.33f, black);                          // miter handle
            p.Box(-0.29f, -0.03f, top, top + 0.09f, 0.06f, 0.085f, alu);                        // saw fence halves
            p.Box(0.03f, 0.29f, top, top + 0.09f, 0.06f, 0.085f, alu);
            p.Box(-0.07f, 0.07f, 0.88f, 1.06f, 0.11f, 0.24f, alu);                              // bevel tower
            p.CylZ(0.08f, 0.98f, 0.18f, 0.25f, 0.025f, 12, black);                              // bevel lock knob
            p.Box(-0.08f, 0.08f, 1.06f, 1.14f, 0.10f, 0.28f, alu);                              // rail housing
            foreach (float x in new[] { -0.04f, 0.04f }) Tube(p, new Vector3(x, 1.10f, 0.02f), new Vector3(x, 1.10f, 0.52f), 0.012f, alu);
            p.Box(-0.07f, 0.07f, 1.08f, 1.12f, 0.50f, 0.54f, alu);                              // rail end cap
            p.Box(-0.07f, 0.07f, 1.07f, 1.16f, 0.02f, 0.10f, alu);                              // head carriage
            DiscX(p, new Vector3(0f, 1.12f, 0.06f), 0.04f, 0.16f, 14, black);                  // arm pivot
            // head: blade, guards, motor, handle (blade centre ahead of and above the table)
            var hc = new Vector3(0f, 1.12f, -0.12f);
            DiscX(p, hc, 0.152f, 0.003f, 48, alu);                                              // 12" blade
            DiscX(p, hc, 0.03f, 0.03f, 12, black);                                              // arbor bolt / flange
            SectorX(p, hc, 0.175f, -25f, 200f, 0.07f, yellow);                                 // fixed upper guard
            SectorX(p, hc, 0.164f, 195f, 340f, 0.055f, glass);                                 // clear lower guard
            p.Box(0.035f, 0.10f, 1.06f, 1.22f, -0.20f, -0.04f, black);                          // gearbox
            DiscX(p, new Vector3(0.16f, 1.17f, -0.09f), 0.065f, 0.13f, 18, yellow);            // motor housing
            DiscX(p, new Vector3(0.235f, 1.17f, -0.09f), 0.06f, 0.02f, 18, black);             // motor end cap
            p.Box(0.13f, 0.19f, 1.235f, 1.245f, -0.13f, -0.05f, black);                         // brush cap strip
            Vector3 g0 = new Vector3(-0.05f, 1.31f, -0.31f), g1 = new Vector3(0.13f, 1.31f, -0.31f);
            Tube(p, g0, g1, 0.018f, black);                                                     // D-handle grip
            Tube(p, g0, new Vector3(-0.03f, 1.22f, -0.19f), 0.016f, black);
            Tube(p, g1, new Vector3(0.10f, 1.22f, -0.19f), 0.016f, black);
            p.Box(0.02f, 0.06f, 1.27f, 1.30f, -0.30f, -0.27f, red);                             // trigger
            Tube(p, new Vector3(0f, 1.22f, -0.01f), new Vector3(0f, 1.23f, 0.10f), 0.03f, plastic);   // dust port

            // --- shop vac under the outfeed wing, hose to the dust port
            const float vx = 1.0f, vz = -0.02f;
            p.CylY(vx, vz, 0.09f, 0.50f, 0.20f, 24, vacRed);
            p.CylY(vx, vz, 0.50f, 0.56f, 0.205f, 24, black);
            p.CylY(vx, vz, 0.56f, 0.64f, 0.12f, 18, black);                                    // motor head
            Tube(p, new Vector3(vx - 0.08f, 0.66f, vz), new Vector3(vx + 0.08f, 0.66f, vz), 0.012f, black);   // carry handle
            foreach (float a in new[] { 0f, 120f, 240f })
            {
                float r = a * Mathf.Deg2Rad;
                p.CylY(vx + Mathf.Sin(r) * 0.16f, vz + Mathf.Cos(r) * 0.16f, 0f, 0.09f, 0.035f, 10, black);   // casters
            }
            Hose(p, new[]
            {
                new Vector3(0f, 1.23f, 0.10f), new Vector3(0.05f, 1.22f, 0.30f), new Vector3(0.30f, 1.0f, 0.36f),
                new Vector3(0.62f, 0.70f, 0.34f), new Vector3(0.90f, 0.46f, 0.30f), new Vector3(vx, 0.40f, 0.22f), new Vector3(vx, 0.40f, 0.19f),
            }, 0.022f, plastic);

            // --- cut 2x4s: a sticker-stacked pile beside the outfeed end
            for (int i = 0; i < 2; i++) Lumber2x4(p, new Vector3(wingL + 0.45f + i * 0.8f, 0.019f, -0.02f), 0.55f, false, pine, endg);
            for (int layer = 0; layer < 3; layer++)
                for (int i = 0; i < 5 - (layer == 2 ? 2 : 0); i++)
                    Lumber2x4(p, new Vector3(wingL + 0.85f, 0.038f + 0.019f + layer * 0.038f, -0.20f + i * 0.092f), layer == 1 ? 1.22f : 1.12f, true, pine, endg);

            LCol(col, f, -wingL, wingL, 0f, top + 0.05f, zF, zB);
            LCol(col, f, -0.25f, 0.25f, top, 1.33f, -0.33f, 0.54f);
            LCol(col, f, wingL + 0.2f, wingL + 1.5f, 0f, 0.18f, -0.25f, 0.21f);

            // --- 10" cabinet table saw out front, operator at -Z, outfeed toward the bench
            TableSaw(p, f, new Vector3(0.1f, 0f, -2.05f), col);
            End(p);
        }

        static void TableSaw(SitePart p, Frame f, Vector3 o, ColFn col)
        {
            int cab = p.M(SurfaceKind.PaintedSteel, new Color(0.34f, 0.36f, 0.39f)), iron = p.M(SurfaceKind.BrushedSteel, new Color(0.62f, 0.63f, 0.64f));
            int black = p.M(SurfaceKind.MatteBlack), lam = p.M(SurfaceKind.PaintedWhite), glass = p.M(SurfaceKind.Glass);
            int steel = p.M(SurfaceKind.BrushedSteel), red = p.M(SurfaceKind.PaintedSteel, new Color(0.8f, 0.1f, 0.07f));
            int plastic = p.M(SurfaceKind.DarkPlastic);
            float x = o.x, z = o.z;
            const float top = 0.864f;
            p.Box(x - 0.30f, x + 0.30f, 0f, 0.10f, z - 0.30f, z + 0.30f, cab);                 // flared base
            p.Box(x - 0.26f, x + 0.26f, 0.10f, top - 0.03f, z - 0.26f, z + 0.26f, cab);        // cabinet
            p.Box(x - 0.56f, x + 0.56f, top - 0.03f, top, z - 0.34f, z + 0.34f, iron);         // cast-iron top + wings
            for (float gx = -0.52f; gx < 0.53f; gx += 0.08f)
                if (Mathf.Abs(gx) > 0.12f) p.Box(x + gx, x + gx + 0.006f, top - 0.0005f, top + 0.0005f, z - 0.30f, z + 0.30f, black);   // mitre slots read as dark lines
            p.Box(x + 0.56f, x + 1.30f, top - 0.025f, top, z - 0.34f, z + 0.34f, lam);         // extension table
            foreach (float zz in new[] { z - 0.30f, z + 0.27f }) p.Box(x + 1.24f, x + 1.27f, 0f, top - 0.025f, zz, zz + 0.03f, black);
            p.Box(x - 0.05f, x + 0.05f, top, top + 0.002f, z - 0.18f, z + 0.18f, black);       // throat plate
            DiscX(p, new Vector3(x, top - 0.06f, z), 0.127f, 0.003f, 40, steel);               // blade
            p.Box(x - 0.002f, x + 0.002f, top, top + 0.09f, z + 0.06f, z + 0.20f, steel);     // riving knife
            p.Box(x - 0.04f, x + 0.04f, top + 0.015f, top + 0.10f, z - 0.17f, z + 0.15f, glass);   // clear guard
            p.Box(x - 0.045f, x + 0.045f, top + 0.10f, top + 0.115f, z - 0.17f, z + 0.15f, black);
            // fence rails, rip fence, controls
            p.Box(x - 0.62f, x + 1.30f, top - 0.065f, top - 0.005f, z - 0.41f, z - 0.34f, steel);   // front rail
            p.Box(x - 0.62f, x + 1.30f, top - 0.05f, top - 0.005f, z + 0.34f, z + 0.38f, steel);    // rear rail
            p.Box(x + 0.40f, x + 0.52f, top - 0.04f, top + 0.08f, z - 0.47f, z - 0.34f, black); // fence head
            p.Box(x + 0.42f, x + 0.49f, top, top + 0.07f, z - 0.34f, z + 0.34f, black);        // fence body
            Tube(p, new Vector3(x + 0.46f, top + 0.08f, z - 0.42f), new Vector3(x + 0.46f, top + 0.17f, z - 0.36f), 0.012f, red);   // fence lock handle
            DiscZ(p, new Vector3(x, 0.55f, z - 0.27f), 0.09f, 0.025f, 20, black);             // height handwheel
            Tube(p, new Vector3(x + 0.06f, 0.55f, z - 0.28f), new Vector3(x + 0.06f, 0.55f, z - 0.34f), 0.012f, black);
            DiscX(p, new Vector3(x + 0.27f, 0.50f, z), 0.08f, 0.025f, 20, black);             // bevel handwheel
            p.Box(x - 0.30f, x - 0.26f, 0.30f, 0.66f, z - 0.15f, z + 0.15f, cab);              // motor cover
            p.Box(x - 0.48f, x - 0.34f, top - 0.16f, top - 0.07f, z - 0.45f, z - 0.41f, black);// switch box
            p.Box(x - 0.47f, x - 0.35f, top - 0.15f, top - 0.10f, z - 0.47f, z - 0.45f, red);  // stop paddle
            p.CylZ(x, 0.16f, z + 0.26f, z + 0.34f, 0.05f, 16, plastic);                        // 4" dust port
            LCol(col, f, x - 0.62f, x + 1.30f, 0f, top + 0.05f, z - 0.47f, z + 0.38f);
        }

        // ================================================================== forklift

        /// <summary>5,000 lb LP-gas cushion-tire warehouse forklift, forks lowered to the floor pointing +Z. ~2.25 m to the
        /// fork face, 42" forks, 1.1 m wide, overhead guard 2.1 m. Fork face at local z = +0.72.</summary>
        public static void Forklift(SitePart p, Vector3 origin, float yaw, ColFn col)
        {
            var f = Begin(p, origin, yaw);
            int yellow = p.M(SurfaceKind.PaintedSteel, new Color(0.96f, 0.70f, 0.06f));
            int dark = p.M(SurfaceKind.PaintedSteel, new Color(0.16f, 0.16f, 0.17f)), black = p.M(SurfaceKind.MatteBlack);
            int tire = p.M(SurfaceKind.Tire), rim = p.M(SurfaceKind.PaintedSteel, new Color(0.78f, 0.78f, 0.76f));
            int seat = p.M(SurfaceKind.DarkPlastic), chrome = p.M(SurfaceKind.BrushedSteel), glass = p.M(SurfaceKind.Glass);
            int amber = p.M(SurfaceKind.PaintedSteel, new Color(1f, 0.5f, 0.05f)), red = p.M(SurfaceKind.TruckPaint);
            int lpg = p.M(SurfaceKind.Galvanized, new Color(0.9f, 0.9f, 0.9f));

            // wheels: drive (front) 21x7x15 cushion, steer (rear) 16x6x10.5
            foreach (int s in new[] { -1, 1 })
            {
                var fw = new Vector3(s * 0.43f, 0.265f, 0.25f);
                DiscX(p, fw, 0.265f, 0.18f, 28, tire);
                DiscX(p, fw + new Vector3(s * 0.005f, 0, 0), 0.19f, 0.182f, 24, rim);
                DiscX(p, fw + new Vector3(s * 0.01f, 0, 0), 0.07f, 0.19f, 12, dark);
                var rw = new Vector3(s * 0.40f, 0.205f, -1.15f);
                DiscX(p, rw, 0.205f, 0.15f, 24, tire);
                DiscX(p, rw + new Vector3(s * 0.005f, 0, 0), 0.135f, 0.152f, 20, rim);
            }
            // frame + body
            p.Box(-0.33f, 0.33f, 0.10f, 0.60f, -1.0f, 0.50f, dark);                              // frame between wheels
            p.Box(-0.54f, 0.54f, 0.30f, 0.62f, -0.92f, -0.02f, yellow);                          // side body between axles
            p.Box(-0.55f, -0.535f, 0.36f, 0.40f, -0.70f, -0.25f, black);                         // step plate edges
            p.Box(0.535f, 0.55f, 0.36f, 0.40f, -0.70f, -0.25f, black);
            p.B.AddBevelBox(new Vector3(0f, 0.80f, -0.52f), new Vector3(1.10f, 0.36f, 0.96f), Quaternion.identity, 0.07f, yellow);   // hood
            foreach (int s in new[] { -1, 1 })                                                   // front fenders
            {
                p.Box(s > 0 ? 0.33f : -0.56f, s > 0 ? 0.56f : -0.33f, 0.56f, 0.60f, -0.02f, 0.40f, yellow);
                p.B.AddBox(new Vector3(s * 0.445f, 0.50f, 0.47f), new Vector3(0.23f, 0.03f, 0.16f), Quaternion.Euler(50f, 0f, 0f), yellow);
            }
            p.Box(-0.33f, 0.33f, 0.56f, 0.60f, -0.04f, 0.30f, black);                            // floor mat
            p.B.AddBevelBox(new Vector3(0f, 0.85f, 0.38f), new Vector3(1.0f, 0.50f, 0.15f), Quaternion.identity, 0.05f, yellow);   // cowl
            p.Box(-0.30f, 0.30f, 1.10f, 1.12f, 0.31f, 0.45f, black);                             // dash
            // counterweight: rounded rear, wraps over the steer wheels
            var cw = new List<Vector2>();
            const float cwR = 0.20f, cwX = 0.56f, cwZ0 = -1.50f, cwZ1 = -0.92f;
            cw.Add(new Vector2(cwX, -cwZ1)); cw.Add(new Vector2(-cwX, -cwZ1));
            for (int i = 0; i <= 6; i++) { float a = Mathf.PI + i / 6f * Mathf.PI / 2f; cw.Add(new Vector2(-cwX + cwR + Mathf.Cos(a) * cwR, -(cwZ0 + cwR) - Mathf.Sin(a) * cwR)); }
            for (int i = 0; i <= 6; i++) { float a = 1.5f * Mathf.PI + i / 6f * Mathf.PI / 2f; cw.Add(new Vector2(cwX - cwR + Mathf.Cos(a) * cwR, -(cwZ0 + cwR) - Mathf.Sin(a) * cwR)); }
            // extrude axis up: Euler(-90,0,0) maps poly (u,v) -> (u, ., -v)
            p.B.AddExtrude(cw, new Vector3(0f, 0.735f, 0f), Quaternion.Euler(-90f, 0f, 0f), 0.63f, yellow);
            p.Box(-0.30f, 0.30f, 0.10f, 0.42f, -1.40f, -0.92f, dark);                            // lower counterweight between steer wheels
            p.Box(-0.08f, 0.08f, 0.47f, 0.56f, -1.505f, -1.49f, black);                          // tow pin pocket
            foreach (int s in new[] { -1, 1 }) p.Box(s * 0.42f - 0.05f, s * 0.42f + 0.05f, 0.92f, 0.98f, -1.48f, -1.455f, red);   // tail lights
            // LP tank across the back, in its cradle
            var tank = new List<Vector2> { new Vector2(0f, -0.38f), new Vector2(0.10f, -0.375f), new Vector2(0.145f, -0.35f), new Vector2(0.155f, -0.31f), new Vector2(0.155f, 0.31f), new Vector2(0.145f, 0.35f), new Vector2(0.10f, 0.375f), new Vector2(0f, 0.38f) };
            var tc = new Vector3(0f, 1.24f, -1.18f);
            p.B.AddLathe(tc, Quaternion.Euler(0f, 0f, 90f), tank, 20, lpg);
            p.B.AddCylinder(tc + new Vector3(0.40f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), 0.09f, 0.06f, 12, chrome);   // valve collar
            foreach (float x in new[] { -0.2f, 0.2f })
            {
                p.B.AddRing(tc + new Vector3(x, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), 0.163f, 0.150f, 0.035f, 20, black);
                p.Box(x - 0.02f, x + 0.02f, 1.05f, 1.10f, -1.30f, -1.06f, black);
            }
            // seat
            p.B.AddBevelBox(new Vector3(0f, 1.035f, -0.38f), new Vector3(0.48f, 0.10f, 0.44f), Quaternion.Euler(0f, 90f, 0f), 0.03f, seat);
            p.B.AddBevelBox(new Vector3(0f, 1.32f, -0.62f), new Vector3(0.46f, 0.46f, 0.09f), Quaternion.Euler(-10f, 0f, 0f), 0.03f, seat);
            foreach (int s in new[] { -1, 1 }) p.B.AddBox(new Vector3(s * 0.25f, 1.15f, -0.50f), new Vector3(0.04f, 0.16f, 0.20f), Quaternion.identity, seat);   // hip restraints
            // steering: tilted column + wheel with spinner knob
            Vector3 c0 = new Vector3(0f, 1.08f, 0.36f), c1 = new Vector3(0f, 1.26f, 0.20f);
            Tube(p, c0, c1, 0.035f, black);
            Vector3 axis = (c1 - c0).normalized;
            var wr = Quaternion.FromToRotation(Vector3.up, axis);
            p.B.AddRing(c1, wr, 0.18f, 0.155f, 0.03f, 28, black);
            for (int i = 0; i < 3; i++) { var d = wr * (Quaternion.Euler(0f, i * 120f + 90f, 0f) * Vector3.forward); Tube(p, c1, c1 + d * 0.165f, 0.012f, black); }
            p.B.AddCylinder(c1, wr, 0.045f, 0.05f, 12, black);
            var kn = c1 + wr * (Quaternion.Euler(0f, 30f, 0f) * Vector3.forward) * 0.167f;
            p.B.AddCylinder(kn + axis * 0.035f, wr, 0.018f, 0.06f, 10, black);
            // hydraulic control levers with knobs
            for (int i = 0; i < 3; i++)
            {
                float x = 0.22f + i * 0.055f;
                Tube(p, new Vector3(x, 1.10f, 0.40f), new Vector3(x, 1.27f, 0.33f), 0.008f, chrome);
                p.B.AddCylinder(new Vector3(x, 1.29f, 0.325f), Quaternion.identity, 0.018f, 0.04f, 10, black);
            }
            // overhead guard
            foreach (int s in new[] { -1, 1 })
            {
                p.Strut(new Vector3(s * 0.50f, 0.95f, 0.44f), new Vector3(s * 0.50f, 2.08f, 0.33f), 0.06f, 0.06f, black);
                p.Strut(new Vector3(s * 0.50f, 1.04f, -1.0f), new Vector3(s * 0.50f, 2.08f, -0.78f), 0.06f, 0.06f, black);
                p.Box(s * 0.50f - 0.035f, s * 0.50f + 0.035f, 2.06f, 2.12f, -0.82f, 0.38f, black);  // side rails
            }
            p.Box(-0.535f, 0.535f, 2.06f, 2.12f, 0.33f, 0.38f, black);
            p.Box(-0.535f, 0.535f, 2.06f, 2.12f, -0.82f, -0.77f, black);
            for (float z = -0.68f; z < 0.30f; z += 0.11f) p.Box(-0.47f, 0.47f, 2.075f, 2.105f, z, z + 0.035f, black);   // guard slats
            Tube(p, new Vector3(-0.54f, 1.05f, 0.40f), new Vector3(-0.54f, 1.65f, 0.36f), 0.015f, yellow);              // grab handle
            foreach (int s in new[] { -1, 1 })                                                  // work lights
            {
                p.Box(s * 0.44f - 0.05f, s * 0.44f + 0.05f, 1.98f, 2.06f, 0.33f, 0.41f, black);
                p.Box(s * 0.44f - 0.04f, s * 0.44f + 0.04f, 1.99f, 2.05f, 0.41f, 0.415f, glass);
            }
            p.CylY(0.36f, -0.72f, 2.12f, 2.14f, 0.06f, 14, black);                              // strobe base
            p.CylY(0.36f, -0.72f, 2.14f, 2.22f, 0.045f, 14, amber);                             // amber strobe
            // mast (two-stage): outer + inner channels, cross ties, lift cylinder, tilt cylinders, chains
            foreach (int s in new[] { -1, 1 })
            {
                p.Box(s > 0 ? 0.37f : -0.45f, s > 0 ? 0.45f : -0.37f, 0.08f, 2.10f, 0.52f, 0.64f, dark);
                p.Box(s > 0 ? 0.30f : -0.37f, s > 0 ? 0.37f : -0.30f, 0.10f, 2.04f, 0.55f, 0.62f, dark);
                Tube(p, new Vector3(s * 0.47f, 0.66f, 0.18f), new Vector3(s * 0.47f, 0.92f, 0.55f), 0.035f, dark);        // tilt cylinder
                Tube(p, new Vector3(s * 0.47f, 0.80f, 0.37f), new Vector3(s * 0.47f, 0.92f, 0.55f), 0.018f, chrome);
                p.Box(s * 0.13f - 0.012f, s * 0.13f + 0.012f, 0.55f, 2.0f, 0.635f, 0.65f, black);  // lift chains
            }
            p.Box(-0.45f, 0.45f, 2.02f, 2.10f, 0.52f, 0.55f, dark);                              // outer top tie
            p.Box(-0.37f, 0.37f, 1.96f, 2.04f, 0.58f, 0.62f, dark);                              // inner header
            p.Box(-0.45f, 0.45f, 0.08f, 0.18f, 0.52f, 0.55f, dark);                              // bottom tie
            p.CylY(0f, 0.49f, 0.15f, 1.40f, 0.05f, 14, dark);                                    // lift cylinder
            p.CylY(0f, 0.49f, 1.40f, 1.96f, 0.032f, 14, chrome);                                 // ram
            p.CylZ(-0.13f, 1.99f, 0.60f, 0.66f, 0.05f, 12, dark);                                // chain sheaves
            p.CylZ(0.13f, 1.99f, 0.60f, 0.66f, 0.05f, 12, dark);
            // carriage + load backrest
            p.Box(-0.52f, 0.52f, 0.45f, 0.55f, 0.65f, 0.71f, dark);
            p.Box(-0.52f, 0.52f, 0.10f, 0.18f, 0.65f, 0.71f, dark);
            p.Box(-0.33f, 0.33f, 0.18f, 0.45f, 0.645f, 0.66f, dark);
            foreach (int s in new[] { -1, 1 }) p.Box(s > 0 ? 0.49f : -0.52f, s > 0 ? 0.52f : -0.49f, 0.55f, 1.25f, 0.66f, 0.70f, dark);
            p.Box(-0.52f, 0.52f, 1.22f, 1.25f, 0.66f, 0.70f, dark);
            p.Box(-0.52f, 0.52f, 0.86f, 0.88f, 0.66f, 0.70f, dark);
            for (int i = 0; i < 7; i++) { float x = -0.39f + i * 0.13f; p.Box(x - 0.012f, x + 0.012f, 0.55f, 1.22f, 0.67f, 0.69f, dark); }
            // forks: 42" x 4" x 1.75", resting on the floor, tapered tips
            foreach (int s in new[] { -1, 1 })
            {
                float fx = s * 0.30f;
                p.Box(fx - 0.05f, fx + 0.05f, 0.005f, 0.60f, 0.71f, 0.755f, black);             // shank
                p.Box(fx - 0.05f, fx + 0.05f, 0.47f, 0.58f, 0.63f, 0.71f, black);               // top hook over the carriage bar
                var blade = new[] { new Vector2(-0.71f, 0.005f), new Vector2(-1.79f, 0.005f), new Vector2(-1.79f, 0.017f), new Vector2(-1.62f, 0.05f), new Vector2(-0.71f, 0.05f) };
                p.B.AddExtrude(blade, new Vector3(fx, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), 0.10f, black);
            }
            LCol(col, f, -0.56f, 0.56f, 0f, 2.12f, -1.50f, 0.72f);
            End(p);
        }

        // ================================================================== air compressor

        /// <summary>60 gal vertical two-stage compressor against the wall: domed tank on welded feet, motor + finned
        /// cast-iron pump on the top plate, belt guard, pressure switch and gauges, a coiled air hose on a wall hook.</summary>
        public static void Compressor(SitePart p, Vector3 origin, float yaw, ColFn col)
        {
            var f = Begin(p, origin, yaw);
            int tankM = p.M(SurfaceKind.TruckPaint), black = p.M(SurfaceKind.MatteBlack), iron = p.M(SurfaceKind.PaintedSteel, new Color(0.22f, 0.22f, 0.24f));
            int motor = p.M(SurfaceKind.PaintedSteel, new Color(0.30f, 0.34f, 0.40f)), brass = p.M(SurfaceKind.Brass), steel = p.M(SurfaceKind.BrushedSteel);
            int glass = p.M(SurfaceKind.Glass), hoseM = p.M(SurfaceKind.PaintedSteel, new Color(0.98f, 0.62f, 0.08f)), plate = p.M(SurfaceKind.PaintedSteel, new Color(0.15f, 0.15f, 0.16f));
            const float r = 0.255f, y0 = 0.14f, y1 = 1.42f;
            var prof = new List<Vector2> { new Vector2(0f, y0), new Vector2(0.12f, y0 + 0.015f), new Vector2(0.20f, y0 + 0.045f), new Vector2(0.245f, y0 + 0.09f), new Vector2(r, y0 + 0.14f),
                                           new Vector2(r, y1 - 0.14f), new Vector2(0.245f, y1 - 0.09f), new Vector2(0.20f, y1 - 0.045f), new Vector2(0.12f, y1 - 0.015f), new Vector2(0f, y1) };
            p.B.AddLathe(Vector3.zero, Quaternion.identity, prof, 28, tankM);
            for (int i = 0; i < 3; i++)                                                         // welded feet
            {
                float a = (i * 120f + 60f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                p.Strut(dir * 0.17f + Vector3.up * 0.20f, dir * 0.27f + Vector3.up * 0.02f, 0.05f, 0.01f, plate);
                p.B.AddBox(dir * 0.28f + Vector3.up * 0.006f, new Vector3(0.09f, 0.012f, 0.09f), Quaternion.LookRotation(dir), plate);
            }
            p.CylY(0f, -0.24f, 0.17f, 0.22f, 0.012f, 8, brass);                                 // drain valve
            // top plate on saddle brackets
            p.Box(-0.30f, 0.30f, 1.44f, 1.46f, -0.26f, 0.24f, plate);
            foreach (float x in new[] { -0.2f, 0.2f }) p.Box(x - 0.02f, x + 0.02f, 1.36f, 1.44f, -0.18f, 0.18f, plate);
            // motor, axis along Z
            p.CylZ(-0.14f, 1.57f, -0.16f, 0.20f, 0.105f, 20, motor);
            p.Box(-0.22f, -0.06f, 1.46f, 1.49f, -0.12f, 0.16f, motor);
            p.Box(-0.20f, -0.10f, 1.66f, 1.72f, 0.02f, 0.12f, motor);                           // conduit box
            // pump: crankcase + two finned cylinders + heads
            p.Box(0.04f, 0.26f, 1.46f, 1.60f, -0.14f, 0.12f, iron);
            foreach (float pz in new[] { -0.06f, 0.06f })
            {
                float px = pz < 0 ? 0.12f : 0.18f;
                p.CylY(px, pz, 1.60f, 1.80f, 0.045f, 14, iron);
                for (float y = 1.62f; y < 1.78f; y += 0.022f) p.CylY(px, pz, y, y + 0.007f, 0.07f, 16, iron);
                p.Box(px - 0.07f, px + 0.07f, 1.80f, 1.84f, pz - 0.055f, pz + 0.055f, iron);
            }
            // belt guard (motor pulley -> flywheel) on the front
            p.CylZ(-0.14f, 1.57f, -0.25f, -0.20f, 0.08f, 18, black);
            p.CylZ(0.15f, 1.64f, -0.25f, -0.20f, 0.19f, 28, black);
            p.B.AddBox(new Vector3(0.005f, 1.605f, -0.225f), new Vector3(0.29f, 0.17f, 0.05f), Quaternion.Euler(0f, 0f, 14f), black);
            // discharge tube to the tank, pressure switch, gauges, regulator + coupler
            Hose(p, new[] { new Vector3(0.18f, 1.82f, 0.06f), new Vector3(0.30f, 1.82f, 0.10f), new Vector3(0.30f, 1.40f, 0.12f), new Vector3(0.22f, 1.30f, 0.12f) }, 0.009f, brass);
            p.Box(0.16f, 0.30f, 1.20f, 1.34f, -0.20f, -0.08f, plate);                           // pressure switch
            p.Box(0.20f, 0.26f, 1.34f, 1.37f, -0.18f, -0.11f, p.M(SurfaceKind.PaintedSteel, new Color(0.8f, 0.1f, 0.07f)));   // on/off lever
            foreach (var g in new[] { new Vector3(-0.08f, 1.24f, -0.27f), new Vector3(0.06f, 1.18f, -0.27f) })
            {
                p.CylZ(g.x, g.y, g.z, g.z + 0.03f, 0.032f, 16, steel);
                p.CylZ(g.x, g.y, g.z - 0.002f, g.z + 0.001f, 0.027f, 16, glass);
                Tube(p, g + new Vector3(0, 0, 0.03f), new Vector3(g.x, g.y, -0.20f), 0.008f, brass);
            }
            p.CylY(-0.08f, -0.28f, 1.08f, 1.16f, 0.025f, 10, black);                            // regulator knob
            Tube(p, new Vector3(-0.08f, 1.12f, -0.24f), new Vector3(-0.08f, 1.12f, -0.32f), 0.012f, brass);   // quick coupler
            // coiled air hose on a wall hook behind
            p.Box(0.36f, 0.40f, 1.40f, 1.44f, 0.24f, 0.30f, steel);
            p.B.AddRing(new Vector3(0.38f, 1.20f, 0.255f), Quaternion.Euler(90f, 0f, 0f), 0.21f, 0.16f, 0.07f, 28, hoseM);
            Hose(p, new[] { new Vector3(0.20f, 1.05f, 0.24f), new Vector3(0.05f, 0.95f, 0.20f), new Vector3(-0.08f, 1.0f, -0.20f), new Vector3(-0.08f, 1.12f, -0.30f) }, 0.009f, hoseM);
            LCol(col, f, -0.30f, 0.30f, 0f, 1.85f, -0.30f, 0.28f);
            End(p);
        }

        // ================================================================== two-post lift

        /// <summary>10,000 lb asymmetric two-post lift: 12' columns 11'+ apart on base plates, overhead beam with shut-off
        /// bar, carriages with swing arms parked along the drive-through (vehicles drive along local Z), power unit on the
        /// right column.</summary>
        public static void TwoPostLift(SitePart p, Vector3 origin, float yaw, ColFn col)
        {
            var f = Begin(p, origin, yaw);
            int blue = p.M(SurfaceKind.PaintedSteel, new Color(0.10f, 0.24f, 0.55f)), dark = p.M(SurfaceKind.PaintedSteel, new Color(0.17f, 0.17f, 0.18f));
            int steel = p.M(SurfaceKind.BrushedSteel), rubber = p.M(SurfaceKind.Tire), yellow = p.M(SurfaceKind.PaintedSteel, new Color(0.98f, 0.78f, 0.08f));
            int black = p.M(SurfaceKind.MatteBlack), white = p.M(SurfaceKind.PaintedWhite), hose = p.M(SurfaceKind.DarkPlastic);
            const float cx = 1.70f, H = 3.70f;
            foreach (int s in new[] { -1, 1 })
            {
                float x = s * cx;
                p.Box(x - 0.30f, x + 0.30f, 0f, 0.016f, -0.30f, 0.30f, blue);                    // base plate
                for (int i = 0; i < 4; i++) p.CylY(x + ((i & 1) == 0 ? -0.24f : 0.24f), (i & 2) == 0 ? -0.24f : 0.24f, 0.016f, 0.04f, 0.015f, 6, steel);   // anchor nuts
                p.Box(x - 0.15f, x + 0.15f, 0.016f, H, -0.19f, 0.19f, blue);                    // column
                p.Box(x - 0.16f, x + 0.16f, H, H + 0.015f, -0.20f, 0.20f, blue);                // cap
                float face = x - s * 0.15f;                                                     // inner face
                p.Box(Mathf.Min(face, face - s * 0.13f), Mathf.Max(face, face - s * 0.13f), 0.10f, 0.85f, -0.17f, 0.17f, dark);   // carriage
                float px = face - s * 0.10f;
                // swing arms: short front arm (-Z), long rear arm (+Z), parked along the drive-through
                foreach (int a in new[] { -1, 1 })
                {
                    float len = a < 0 ? 0.80f : 1.20f;
                    var piv = new Vector3(px, 0.16f, a * 0.13f);
                    var dir = new Vector3(-s * 0.22f, 0f, a).normalized;
                    var end = piv + dir * len;
                    p.CylY(piv.x, piv.z, 0.10f, 0.22f, 0.045f, 12, dark);                     // pivot pin boss
                    p.Strut(piv, piv + dir * (len * 0.62f), 0.11f, 0.08f, dark);               // outer tube
                    p.Strut(piv + dir * (len * 0.55f), end, 0.085f, 0.06f, steel);             // telescoping inner
                    p.CylY(end.x, end.z, 0.13f, 0.20f, 0.03f, 10, steel);                      // pad screw
                    p.CylY(end.x, end.z, 0.20f, 0.235f, 0.065f, 16, rubber);                   // rubber pad
                }
                p.Box(x - 0.152f, x + 0.152f, 1.35f, 1.65f, -0.192f, -0.19f, yellow);            // warning decal (front face)
                p.Box(x - 0.152f, x + 0.152f, 1.45f, 1.47f, -0.193f, -0.192f, black);
            }
            // overhead beam + padded shut-off bar + hydraulic line
            p.Box(-cx - 0.15f, cx + 0.15f, H + 0.015f, H + 0.17f, -0.08f, 0.08f, blue);
            Tube(p, new Vector3(-cx + 0.25f, H - 0.16f, 0.02f), new Vector3(cx - 0.25f, H - 0.16f, 0.02f), 0.035f, yellow);
            for (float x = -cx + 0.45f; x < cx - 0.3f; x += 0.5f) Tube(p, new Vector3(x, H - 0.16f, 0.02f), new Vector3(x + 0.22f, H - 0.16f, 0.02f), 0.036f, black);
            Tube(p, new Vector3(-cx + 0.25f, H - 0.16f, 0.02f), new Vector3(-cx + 0.15f, H - 0.16f, 0.02f), 0.012f, steel);
            Tube(p, new Vector3(cx - 0.25f, H - 0.16f, 0.02f), new Vector3(cx - 0.15f, H - 0.16f, 0.02f), 0.012f, steel);
            Tube(p, new Vector3(-cx, H + 0.19f, 0.06f), new Vector3(cx, H + 0.19f, 0.06f), 0.01f, hose);
            // power unit on the outside of the right column: motor, pump, reservoir, push button
            float ox = cx + 0.15f;
            p.Box(ox, ox + 0.02f, 0.95f, 1.70f, -0.12f, 0.12f, dark);                            // mounting plate
            p.CylY(ox + 0.12f, 0f, 1.32f, 1.66f, 0.09f, 18, black);                             // motor
            p.Box(ox + 0.03f, ox + 0.21f, 1.22f, 1.32f, -0.08f, 0.08f, steel);                  // pump block
            p.Box(ox + 0.04f, ox + 0.20f, 0.88f, 1.22f, -0.09f, 0.09f, white);                  // reservoir
            p.CylY(ox + 0.12f, 0.05f, 1.22f, 1.25f, 0.02f, 8, black);
            p.Box(ox + 0.02f, ox + 0.07f, 1.40f, 1.50f, -0.19f, -0.12f, dark);                  // push-button box
            p.CylX(1.45f, -0.155f, ox + 0.07f, ox + 0.085f, 0.015f, 10, p.M(SurfaceKind.PaintedSteel, new Color(0.1f, 0.6f, 0.15f)));
            Hose(p, new[] { new Vector3(ox + 0.12f, 1.24f, 0.09f), new Vector3(ox + 0.10f, 1.10f, 0.20f), new Vector3(ox - 0.05f, 0.9f, 0.20f) }, 0.01f, hose);
            Tube(p, new Vector3(-cx - 0.15f, 1.30f, -0.12f), new Vector3(-cx - 0.30f, 1.36f, -0.12f), 0.012f, steel);   // lock release handle
            p.CylX(1.36f, -0.12f, -cx - 0.36f, -cx - 0.28f, 0.02f, 10, black);
            foreach (int s in new[] { -1, 1 }) LCol(col, f, s * cx - 0.16f, s * cx + 0.16f, 0f, H, -0.20f, 0.20f);
            End(p);
        }

        // ================================================================== fuel tank

        /// <summary>500 gal single-wall diesel tank on a steel skid: domed horizontal tank on saddles, 12 V transfer pump
        /// with meter, hose and nozzle in its boot, vent, fill cap, clock gauge, DIESEL placard. Tank axis along local X.</summary>
        public static void FuelTank(SitePart p, Transform labels, Vector3 origin, float yaw, ColFn col)
        {
            var f = Begin(p, origin, yaw);
            int skid = p.M(SurfaceKind.PaintedSteel, new Color(0.2f, 0.2f, 0.21f)), tankM = p.M(SurfaceKind.PaintedSteel, new Color(0.78f, 0.12f, 0.10f));
            int galv = p.M(SurfaceKind.Galvanized), pump = p.M(SurfaceKind.PaintedSteel, new Color(0.30f, 0.36f, 0.32f)), black = p.M(SurfaceKind.MatteBlack);
            int hose = p.M(SurfaceKind.DarkPlastic), alu = p.M(SurfaceKind.BrushedSteel), yellow = p.M(SurfaceKind.PaintedSteel, new Color(0.98f, 0.8f, 0.1f));
            int glass = p.M(SurfaceKind.Glass), white = p.M(SurfaceKind.PaintedWhite);
            // skid: two channel runners, cross members, two saddles
            foreach (int s in new[] { -1, 1 })
            {
                p.Box(-1.10f, 1.10f, 0f, 0.15f, s * 0.48f - 0.04f, s * 0.48f + 0.04f, skid);
                p.Box(s * 1.10f - 0.01f, s * 1.10f + 0.01f, 0f, 0.15f, -0.52f, 0.52f, skid);   // end channels
                p.Box(s * 0.60f - 0.06f, s * 0.60f + 0.06f, 0.15f, 0.48f, -0.46f, 0.46f, skid); // saddle
            }
            p.Box(-0.04f, 0.04f, 0.10f, 0.15f, -0.48f, 0.48f, skid);
            const float tr = 0.585f, ty = 0.95f;
            var prof = new List<Vector2> { new Vector2(0f, -0.95f), new Vector2(0.40f, -0.94f), new Vector2(0.53f, -0.925f), new Vector2(tr, -0.89f),
                                           new Vector2(tr, 0.89f), new Vector2(0.53f, 0.925f), new Vector2(0.40f, 0.94f), new Vector2(0f, 0.95f) };
            p.B.AddLathe(new Vector3(0f, ty, 0f), Quaternion.Euler(0f, 0f, 90f), prof, 36, tankM);
            float tt = ty + tr;                                                                  // tank top
            // pump on its riser
            p.CylY(0.55f, 0f, tt - 0.04f, tt + 0.12f, 0.035f, 12, galv);
            p.Box(0.44f, 0.68f, tt + 0.12f, tt + 0.42f, -0.12f, 0.12f, pump);
            p.CylX(tt + 0.22f, 0f, 0.68f, 0.86f, 0.075f, 16, black);                            // motor
            p.Box(0.50f, 0.62f, tt + 0.30f, tt + 0.40f, -0.13f, -0.12f, white);                 // meter face
            p.Box(0.51f, 0.61f, tt + 0.31f, tt + 0.39f, -0.135f, -0.13f, glass);
            p.Box(0.47f, 0.60f, tt + 0.16f, tt + 0.29f, -0.19f, -0.12f, black);                 // nozzle boot
            p.Box(0.66f, 0.70f, tt + 0.25f, tt + 0.35f, -0.08f, 0.0f, black);                   // switch
            // nozzle hanging in the boot: aluminium body, yellow grip guard, spout down into the boot
            p.B.AddBox(new Vector3(0.535f, tt + 0.34f, -0.20f), new Vector3(0.05f, 0.08f, 0.16f), Quaternion.Euler(-25f, 0f, 0f), alu);
            p.B.AddBox(new Vector3(0.535f, tt + 0.40f, -0.25f), new Vector3(0.055f, 0.05f, 0.14f), Quaternion.Euler(-25f, 0f, 0f), yellow);
            Tube(p, new Vector3(0.535f, tt + 0.30f, -0.15f), new Vector3(0.535f, tt + 0.20f, -0.155f), 0.011f, alu);
            Hose(p, new[]
            {
                new Vector3(0.535f, tt + 0.38f, -0.29f), new Vector3(0.62f, tt + 0.30f, -0.45f), new Vector3(0.95f, ty + 0.30f, -0.62f),
                new Vector3(1.08f, ty - 0.05f, -0.62f), new Vector3(1.10f, ty - 0.30f, -0.40f), new Vector3(1.08f, ty - 0.10f, 0.0f),
                new Vector3(1.0f, tt - 0.05f, 0.0f), new Vector3(0.86f, tt + 0.30f, 0.0f), new Vector3(0.68f, tt + 0.33f, 0.04f),
            }, 0.016f, hose);
            // fittings on top
            p.CylY(-0.55f, 0.10f, tt - 0.04f, tt + 0.35f, 0.025f, 10, galv);                     // vent pipe
            p.CylY(-0.55f, 0.10f, tt + 0.35f, tt + 0.40f, 0.065f, 14, galv);                     // mushroom cap
            p.CylY(-0.15f, 0f, tt - 0.03f, tt + 0.05f, 0.075f, 16, galv);                        // fill cap
            p.CylY(0.10f, -0.05f, tt - 0.03f, tt + 0.03f, 0.065f, 16, galv);                     // clock gauge
            p.CylY(0.10f, -0.05f, tt + 0.03f, tt + 0.035f, 0.055f, 16, glass);
            // placard on the street side
            p.Box(-0.55f, 0.15f, ty - 0.16f, ty + 0.08f, -tr - 0.012f, -tr - 0.004f, white);
            p.Box(-0.55f, 0.15f, ty + 0.08f, ty + 0.13f, -tr - 0.010f, -tr - 0.004f, yellow);
            Label(labels, f, "DIESEL FUEL", new Vector3(-0.20f, ty - 0.02f, -tr - 0.016f), 0.09f, new Color(0.05f, 0.05f, 0.05f));
            Label(labels, f, "500 GAL   NO SMOKING", new Vector3(-0.20f, ty - 0.12f, -tr - 0.016f), 0.035f, new Color(0.7f, 0.05f, 0.05f));
            LCol(col, f, -1.10f, 1.10f, 0f, tt + 0.1f, -0.60f, 0.60f);
            End(p);
        }

        // ================================================================== fit-out details

        /// <summary>Rolling tool cabinet (26") with a top chest, drawers toward local -Z.</summary>
        public static void ToolChest(SitePart p, Vector3 origin, float yaw, ColFn col)
        {
            var f = Begin(p, origin, yaw);
            int red = p.M(SurfaceKind.TruckPaint), steel = p.M(SurfaceKind.BrushedSteel), black = p.M(SurfaceKind.MatteBlack);
            const float hw = 0.34f, hd = 0.23f;
            p.Box(-hw, hw, 0.13f, 0.95f, -hd + 0.01f, hd, red);
            float[] drawers = { 0.13f, 0.33f, 0.48f, 0.59f, 0.70f, 0.80f, 0.88f, 0.95f };
            for (int i = 0; i + 1 < drawers.Length; i++)
            {
                float a = drawers[i] + 0.006f, b = drawers[i + 1] - 0.006f;
                p.Box(-hw + 0.02f, hw - 0.02f, a, b, -hd, -hd + 0.01f, red);
                p.Box(-hw + 0.05f, hw - 0.05f, b - 0.022f, b - 0.012f, -hd - 0.022f, -hd, steel);   // full-width pull
            }
            Tube(p, new Vector3(hw + 0.04f, 0.85f, -0.15f), new Vector3(hw + 0.04f, 0.85f, 0.15f), 0.012f, steel);   // side handle
            p.Box(hw, hw + 0.04f, 0.84f, 0.86f, -0.16f, -0.14f, steel); p.Box(hw, hw + 0.04f, 0.84f, 0.86f, 0.14f, 0.16f, steel);
            p.Box(-hw, hw, 0.95f, 0.97f, -hd, hd, black);                                       // rubber top mat
            // top chest with lid
            p.Box(-hw + 0.01f, hw - 0.01f, 0.97f, 1.33f, -hd + 0.03f, hd, red);
            float[] td = { 0.97f, 1.06f, 1.13f, 1.20f, 1.27f };
            for (int i = 0; i + 1 < td.Length; i++)
            {
                p.Box(-hw + 0.03f, hw - 0.03f, td[i] + 0.006f, td[i + 1] - 0.006f, -hd + 0.02f, -hd + 0.03f, red);
                p.Box(-hw + 0.08f, hw - 0.08f, td[i + 1] - 0.025f, td[i + 1] - 0.016f, -hd + 0.005f, -hd + 0.02f, steel);
            }
            p.Box(-hw, hw, 1.33f, 1.37f, -hd + 0.02f, hd + 0.01f, red);                         // lid
            p.Box(-0.06f, 0.06f, 1.30f, 1.33f, -hd + 0.015f, -hd + 0.03f, steel);               // latch
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    p.Box(sx * (hw - 0.06f) - 0.03f, sx * (hw - 0.06f) + 0.03f, 0.10f, 0.13f, sz * (hd - 0.06f) - 0.03f, sz * (hd - 0.06f) + 0.03f, steel);
                    DiscX(p, new Vector3(sx * (hw - 0.06f), 0.05f, sz * (hd - 0.06f)), 0.05f, 0.035f, 14, black);
                }
            LCol(col, f, -hw, hw, 0f, 1.37f, -hd, hd);
            End(p);
        }

        /// <summary>Hand tools hung on an 8' pegboard: 4' level, framing hammer, hand saw, framing square, speed square,
        /// tape measure, combination wrenches, two F-clamps. Origin = left end of the pegboard face at floor level, tools
        /// toward local -Z, x along the board (0..2.44).</summary>
        public static void PegboardTools(SitePart p, Vector3 origin, float yaw)
        {
            Begin(p, origin, yaw);
            int alu = p.M(SurfaceKind.BrushedSteel), steel = p.M(SurfaceKind.Galvanized, new Color(0.7f, 0.72f, 0.74f)), black = p.M(SurfaceKind.MatteBlack);
            int yellow = p.M(SurfaceKind.PaintedSteel, new Color(0.98f, 0.78f, 0.1f)), wood = p.M(SurfaceKind.Oak), red = p.M(SurfaceKind.PaintedSteel, new Color(0.8f, 0.1f, 0.07f));
            int glass = p.M(SurfaceKind.Glass), green = p.M(SurfaceKind.PaintedSteel, new Color(0.5f, 0.85f, 0.2f));
            void Peg(float x, float y) => Tube(p, new Vector3(x, y, 0f), new Vector3(x, y, -0.06f), 0.003f, steel, 6);
            // 4' box level, hung flat across two pegs
            p.Box(0.15f, 1.37f, 1.98f, 2.04f, -0.04f, -0.015f, yellow);
            p.Box(0.15f, 0.19f, 1.98f, 2.04f, -0.042f, -0.013f, black); p.Box(1.33f, 1.37f, 1.98f, 2.04f, -0.042f, -0.013f, black);
            foreach (float x in new[] { 0.40f, 0.76f, 1.12f }) p.Box(x - 0.03f, x + 0.03f, 1.995f, 2.025f, -0.042f, -0.04f, green);   // vials
            Peg(0.45f, 1.975f); Peg(1.07f, 1.975f);
            // framing hammer, head up
            p.Box(0.30f, 0.33f, 1.42f, 1.78f, -0.035f, -0.01f, wood);
            p.Box(0.24f, 0.37f, 1.78f, 1.81f, -0.04f, -0.005f, steel);
            p.Box(0.36f, 0.42f, 1.79f, 1.80f, -0.04f, -0.005f, steel);                          // claw
            p.CylX(1.795f, -0.0225f, 0.21f, 0.24f, 0.015f, 10, steel);                           // face
            Peg(0.28f, 1.76f); Peg(0.35f, 1.76f);
            // hand saw, handle up
            var blade = new[] { new Vector2(0.52f, 1.70f), new Vector2(0.52f, 1.18f), new Vector2(0.60f, 1.18f), new Vector2(0.64f, 1.70f) };
            p.B.AddExtrude(blade, new Vector3(0f, 0f, -0.02f), Quaternion.identity, 0.002f, steel);
            p.Box(0.50f, 0.66f, 1.70f, 1.82f, -0.032f, -0.008f, wood);
            Peg(0.58f, 1.79f);
            // framing square (24" x 16") hung on one peg
            p.Box(0.80f, 0.85f, 1.25f, 1.86f, -0.015f, -0.012f, steel);
            p.Box(0.85f, 1.21f, 1.25f, 1.30f, -0.015f, -0.012f, steel);
            Peg(0.825f, 1.83f);
            // speed square + tape measure
            p.B.AddExtrude(new[] { new Vector2(1.30f, 1.55f), new Vector2(1.48f, 1.55f), new Vector2(1.30f, 1.73f) }, new Vector3(0f, 0f, -0.02f), Quaternion.identity, 0.012f, black);
            p.B.AddBevelBox(new Vector3(1.62f, 1.62f, -0.03f), new Vector3(0.085f, 0.085f, 0.04f), Quaternion.identity, 0.015f, yellow);
            p.Box(1.60f, 1.64f, 1.57f, 1.585f, -0.052f, -0.008f, black);
            Peg(1.62f, 1.67f);
            // combination wrenches, short to long
            for (int i = 0; i < 6; i++)
            {
                float x = 1.32f + i * 0.045f, len = 0.13f + i * 0.025f;
                p.Box(x - 0.008f, x + 0.008f, 1.40f - len, 1.40f, -0.02f, -0.014f, alu);
                p.CylZ(x, 1.40f - len, -0.022f, -0.012f, 0.014f + i * 0.002f, 10, alu);
                p.CylZ(x, 1.40f, -0.022f, -0.012f, 0.012f + i * 0.002f, 10, alu);
                Peg(x, 1.41f);
            }
            // two F-clamps, bars vertical
            foreach (float x in new[] { 1.85f, 1.97f })
            {
                p.Box(x - 0.012f, x + 0.012f, 1.25f, 1.85f, -0.03f, -0.024f, steel);
                p.Box(x - 0.012f, x + 0.07f, 1.80f, 1.83f, -0.035f, -0.019f, steel);           // fixed jaw
                p.Box(x - 0.012f, x + 0.07f, 1.50f, 1.53f, -0.035f, -0.019f, steel);           // sliding jaw
                p.Box(x + 0.03f, x + 0.05f, 1.36f, 1.50f, -0.04f, -0.014f, red);               // handle
                Peg(x - 0.02f, 1.86f);
            }
            // safety glasses + ear muffs
            p.Box(2.12f, 2.28f, 1.60f, 1.645f, -0.03f, -0.026f, glass);
            p.Box(2.12f, 2.28f, 1.645f, 1.655f, -0.032f, -0.024f, black);
            p.B.AddRing(new Vector3(2.20f, 1.88f, -0.035f), Quaternion.Euler(90f, 0f, 0f), 0.08f, 0.068f, 0.02f, 16, black);
            p.CylZ(2.12f, 1.87f, -0.07f, -0.02f, 0.035f, 12, red); p.CylZ(2.28f, 1.87f, -0.07f, -0.02f, 0.035f, 12, red);
            Peg(2.20f, 1.96f);
            End(p);
        }

        /// <summary>48" steel jobsite box on skids: lid with a raised panel, hinges at the back, hasp at the front, end handles.</summary>
        public static void JobBox(SitePart p, Vector3 origin, float yaw, ColFn col)
        {
            var f = Begin(p, origin, yaw);
            int paint = p.M(SurfaceKind.PaintedSteel, new Color(0.95f, 0.72f, 0.08f)), black = p.M(SurfaceKind.MatteBlack), steel = p.M(SurfaceKind.BrushedSteel);
            const float hw = 0.61f, hd = 0.30f;
            foreach (int s in new[] { -1, 1 }) p.Box(-hw + 0.02f, hw - 0.02f, 0f, 0.06f, s * (hd - 0.05f) - 0.03f, s * (hd - 0.05f) + 0.03f, black);
            p.Box(-hw, hw, 0.06f, 0.62f, -hd, hd, paint);
            p.Box(-hw - 0.012f, hw + 0.012f, 0.56f, 0.66f, -hd - 0.012f, hd + 0.012f, paint);   // lid skirt
            p.Box(-hw + 0.06f, hw - 0.06f, 0.66f, 0.69f, -hd + 0.05f, hd - 0.05f, paint);       // raised lid panel
            p.Box(-hw, hw, 0.555f, 0.56f, -hd - 0.013f, -hd, black);                            // lid seam shadow
            p.Box(-0.06f, 0.06f, 0.46f, 0.62f, -hd - 0.025f, -hd - 0.012f, steel);              // hasp
            p.Box(-0.02f, 0.02f, 0.48f, 0.53f, -hd - 0.035f, -hd - 0.025f, steel);
            foreach (float x in new[] { -0.4f, 0f, 0.4f }) p.Box(x - 0.05f, x + 0.05f, 0.59f, 0.64f, hd + 0.012f, hd + 0.022f, steel);   // hinges
            foreach (int s in new[] { -1, 1 })
            {
                Tube(p, new Vector3(s * (hw + 0.05f), 0.48f, -0.12f), new Vector3(s * (hw + 0.05f), 0.48f, 0.12f), 0.013f, black);
                p.Box(Mathf.Min(s * hw, s * (hw + 0.05f)), Mathf.Max(s * hw, s * (hw + 0.05f)), 0.47f, 0.49f, -0.13f, -0.11f, black);
                p.Box(Mathf.Min(s * hw, s * (hw + 0.05f)), Mathf.Max(s * hw, s * (hw + 0.05f)), 0.47f, 0.49f, 0.11f, 0.13f, black);
            }
            LCol(col, f, -hw, hw, 0f, 0.69f, -hd, hd);
            End(p);
        }

        /// <summary>Wall-mounted 10 lb ABC extinguisher (top at 4'-8") with its sign, and a first-aid cabinet. Origin on the
        /// wall face, items toward local -Z.</summary>
        public static void SafetyStation(SitePart p, Transform labels, Vector3 origin, float yaw)
        {
            var f = Begin(p, origin, yaw);
            int red = p.M(SurfaceKind.TruckPaint), black = p.M(SurfaceKind.MatteBlack), steel = p.M(SurfaceKind.BrushedSteel);
            int white = p.M(SurfaceKind.PaintedWhite), glass = p.M(SurfaceKind.Glass), yellow = p.M(SurfaceKind.PaintedSteel, new Color(0.98f, 0.8f, 0.1f));
            int signRed = p.M(SurfaceKind.PaintedSteel, new Color(0.78f, 0.06f, 0.05f));
            const float ex = -0.25f, ez = -0.08f;
            p.Box(ex - 0.05f, ex + 0.05f, 1.10f, 1.32f, -0.008f, 0f, steel);                   // wall bracket
            p.Box(ex - 0.07f, ex + 0.07f, 1.20f, 1.23f, ez - 0.07f, -0.008f, black);           // strap
            var prof = new List<Vector2> { new Vector2(0f, 0.92f), new Vector2(0.06f, 0.922f), new Vector2(0.065f, 0.93f), new Vector2(0.065f, 1.30f), new Vector2(0.055f, 1.34f), new Vector2(0.025f, 1.36f), new Vector2(0f, 1.362f) };
            p.B.AddLathe(new Vector3(ex, 0f, ez), Quaternion.identity, prof, 20, red);
            p.CylY(ex, ez, 1.36f, 1.40f, 0.018f, 10, steel);                                    // valve
            p.Strut(new Vector3(ex - 0.02f, 1.40f, ez), new Vector3(ex + 0.08f, 1.405f, ez), 0.025f, 0.008f, black);   // carry handle
            p.Strut(new Vector3(ex - 0.02f, 1.415f, ez), new Vector3(ex + 0.075f, 1.43f, ez), 0.022f, 0.008f, black);  // squeeze lever
            p.CylZ(ex, 1.385f, ez - 0.035f, ez - 0.015f, 0.016f, 12, steel);                   // gauge
            p.CylZ(ex, 1.385f, ez - 0.037f, ez - 0.034f, 0.013f, 12, glass);
            p.Box(ex - 0.03f, ex - 0.015f, 1.39f, 1.42f, ez - 0.012f, ez + 0.012f, yellow);    // pin tag
            Hose(p, new[] { new Vector3(ex + 0.02f, 1.39f, ez - 0.01f), new Vector3(ex + 0.075f, 1.33f, ez - 0.02f), new Vector3(ex + 0.075f, 1.02f, ez - 0.02f) }, 0.009f, black);
            p.CylY(ex + 0.075f, ez - 0.02f, 0.98f, 1.02f, 0.014f, 10, black);                   // nozzle
            p.Box(ex - 0.13f, ex + 0.13f, 1.62f, 1.84f, -0.006f, 0f, signRed);                  // sign
            Label(labels, f, "FIRE\nEXTINGUISHER", new Vector3(ex, 1.73f, -0.008f), 0.045f, Color.white);
            // first-aid cabinet
            const float fx = 0.22f;
            int green = p.M(SurfaceKind.PaintedSteel, new Color(0.05f, 0.5f, 0.25f)), grey = p.M(SurfaceKind.PaintedSteel, new Color(0.45f, 0.47f, 0.48f));
            p.Box(fx - 0.18f, fx + 0.18f, 1.18f, 1.60f, -0.12f, 0f, grey);                      // cabinet shell
            p.Box(fx - 0.17f, fx + 0.17f, 1.19f, 1.47f, -0.123f, -0.12f, white);                // door
            p.Box(fx - 0.17f, fx + 0.17f, 1.47f, 1.59f, -0.123f, -0.12f, green);                // ANSI green header
            p.Box(fx - 0.06f, fx + 0.06f, 1.31f, 1.35f, -0.125f, -0.123f, signRed);             // red cross
            p.Box(fx - 0.02f, fx + 0.02f, 1.27f, 1.39f, -0.125f, -0.123f, signRed);
            p.Box(fx + 0.13f, fx + 0.15f, 1.36f, 1.42f, -0.135f, -0.123f, steel);               // latch
            Label(labels, f, "FIRST AID", new Vector3(fx, 1.53f, -0.127f), 0.045f, Color.white);
            End(p);
        }
    }
}
