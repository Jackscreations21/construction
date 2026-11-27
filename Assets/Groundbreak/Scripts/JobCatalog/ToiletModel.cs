using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Real-size two-piece elongated toilet on a 12" rough-in: 15" rim, 30" overall height, 28.6" deep, 20" x 7.5" tank.
    /// Shared by the town house bathroom fixture and the job-catalog "toilet" prop so the two coincide.
    /// Local frame: origin on the finished floor at the wall face on the toilet's centre line, +Z out of the wall toward
    /// the bowl front, +Y up, +X to the left of someone facing the toilet (the trip lever and supply side).
    /// Elliptical parts are lathes squashed through MeshBuilder.Transform (mesh-space only, never a GameObject scale).
    /// </summary>
    public static class ToiletModel
    {
        public const float RoughIn = 12f * Units.Inch;                  // wall to flange centre
        public const float RimY = 0.381f, SeatY = 0.403f, WaterY = 0.30f;
        public const float BowlZ = 0.48f, BowlK = 1.33f;                 // bowl centre and front-to-back elongation
        public const float TankW = 0.50f, TankD = 0.19f, TankZ = 0.12f, TankBottom = RimY + 0.004f, TankTop = 0.74f, LidT = 0.025f;
        public const float Depth = 0.73f, Height = TankTop + LidT, StopX = 0.15f, LeverY = 0.665f;
        const float Wall = 0.03f;                                         // tank wall thickness

        static readonly Vector2[] Inner =
        {
            new Vector2(0.140f, RimY), new Vector2(0.128f, 0.370f), new Vector2(0.118f, 0.340f), new Vector2(0.095f, 0.290f),
            new Vector2(0.060f, 0.245f), new Vector2(0.030f, 0.225f), new Vector2(0f, 0.220f),
        };

        /// <summary>Radius of the bowl's inside (across, before elongation) at height y.</summary>
        public static float InnerR(float y)
        {
            for (int i = 0; i + 1 < Inner.Length; i++)
            {
                Vector2 a = Inner[i], b = Inner[i + 1];
                if (y <= a.y && y >= b.y) return Mathf.Lerp(b.x, a.x, (y - b.y) / Mathf.Max(1e-5f, a.y - b.y));
            }
            return y > RimY ? Inner[0].x : 0f;
        }

        static Matrix4x4 Frame(MeshBuilder b, Vector3 origin, Quaternion rot) => b.Transform * Matrix4x4.TRS(origin, rot, Vector3.one);

        /// <summary>The whole fixture. tankLid=false leaves the tank open (hollow, for repairs); braid &lt; 0 skips the stop + supply line.</summary>
        public static void Build(MeshBuilder b, Vector3 origin, Quaternion rot, int porcelain, int seat, int chrome, int braid = -1, bool tankLid = true, bool lidUp = true)
        {
            var prev = b.Transform;
            var F = Frame(b, origin, rot);
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);                // extrude axis (local Z) -> +Y

            // pedestal foot (glazed trapway skirt), widest on the floor
            b.Transform = F * Matrix4x4.TRS(new Vector3(0f, 0f, 0.42f), Quaternion.identity, new Vector3(1f, 1f, 1.6f));
            b.AddLathe(Vector3.zero, Quaternion.identity, new[]
            {
                new Vector2(0.130f, 0f), new Vector2(0.127f, 0.015f), new Vector2(0.105f, 0.05f), new Vector2(0.092f, 0.11f),
                new Vector2(0.089f, 0.16f), new Vector2(0f, 0.16f),
            }, 22, porcelain);
            // rear shroud that carries the tank
            b.Transform = F * Matrix4x4.TRS(new Vector3(0f, 0f, 0.19f), Quaternion.identity, new Vector3(1f, 1f, 0.62f));
            b.AddLathe(Vector3.zero, Quaternion.identity, new[]
            {
                new Vector2(0.150f, 0f), new Vector2(0.140f, 0.03f), new Vector2(0.125f, 0.15f), new Vector2(0.135f, 0.30f),
                new Vector2(0.160f, 0.37f), new Vector2(0.160f, RimY + 0.004f), new Vector2(0f, RimY + 0.004f),
            }, 20, porcelain);
            // elongated bowl: underside, outer flare up to the rolled rim, glazed inside down to the outlet
            b.Transform = F * Matrix4x4.TRS(new Vector3(0f, 0f, BowlZ), Quaternion.identity, new Vector3(1f, 1f, BowlK));
            var bowl = new List<Vector2>
            {
                new Vector2(0f, 0.15f), new Vector2(0.07f, 0.15f), new Vector2(0.13f, 0.20f), new Vector2(0.163f, 0.27f),
                new Vector2(0.181f, 0.33f), new Vector2(0.186f, 0.366f), new Vector2(0.180f, RimY),
            };
            bowl.AddRange(Inner);
            b.AddLathe(Vector3.zero, Quaternion.identity, bowl, 28, porcelain);

            // seat (open-front ring is modern code; residential closed seat here) and lid
            b.Transform = F * Matrix4x4.TRS(new Vector3(0f, RimY, BowlZ - 0.005f), Quaternion.identity, new Vector3(1f, 1f, BowlK));
            b.AddLathe(Vector3.zero, Quaternion.identity, new[]
            {
                new Vector2(0.112f, 0.004f), new Vector2(0.172f, 0.004f), new Vector2(0.182f, 0.012f), new Vector2(0.176f, 0.021f),
                new Vector2(0.150f, 0.024f), new Vector2(0.122f, 0.021f), new Vector2(0.112f, 0.012f), new Vector2(0.112f, 0.004f),
            }, 28, seat);
            float hingeZ = BowlZ - 0.005f - 0.182f * BowlK + 0.012f;
            for (int s = -1; s <= 1; s += 2)
            {
                b.Transform = F;
                b.AddBox(new Vector3(s * 0.075f, RimY + 0.016f, hingeZ), new Vector3(0.03f, 0.024f, 0.04f), seat);
            }
            var lidProfile = new[] { new Vector2(0f, 0f), new Vector2(0.178f, 0f), new Vector2(0.186f, 0.008f), new Vector2(0.180f, 0.018f), new Vector2(0f, 0.022f) };
            float lidHalf = 0.186f * BowlK;
            if (lidUp)
                b.Transform = F * Matrix4x4.TRS(new Vector3(0f, SeatY + 0.012f, Mathf.Max(hingeZ, TankZ + TankD * 0.5f + 0.04f)), Quaternion.Euler(-93f, 0f, 0f), Vector3.one)
                                * Matrix4x4.TRS(new Vector3(0f, 0f, lidHalf), Quaternion.identity, new Vector3(1f, 1f, BowlK));
            else
                b.Transform = F * Matrix4x4.TRS(new Vector3(0f, SeatY, BowlZ - 0.005f), Quaternion.identity, new Vector3(1f, 1f, BowlK));
            b.AddLathe(Vector3.zero, Quaternion.identity, lidProfile, 28, seat);

            // tank: rounded box, 19.7" x 7.5" x 14"
            b.Transform = F;
            var tank = RoundRect(TankW, TankD, 0.035f, 3);
            float th = TankTop - TankBottom;
            var tc = new Vector3(0f, (TankTop + TankBottom) * 0.5f, TankZ);
            b.AddExtrude(tank, tc, up, th, porcelain, porcelain, tankLid);
            if (tankLid) TankLidLocal(b, new Vector3(0f, TankTop, TankZ), Quaternion.identity, porcelain);
            else
            {
                // hollow: glazed inside walls/floor and the top edge of the walls
                float ix = TankW * 0.5f - Wall, iz = TankD * 0.5f - Wall;
                b.AddWell(new Vector3(0f, TankTop, TankZ), new Vector2(ix * 2f, iz * 2f), new Vector2(ix * 2f, iz * 2f), th - 0.04f, porcelain);
                for (int i = 0; i < tank.Count; i++)
                {
                    Vector2 o0 = tank[i], o1 = tank[(i + 1) % tank.Count];
                    Vector2 i0 = new Vector2(Mathf.Clamp(o0.x, -ix, ix), Mathf.Clamp(o0.y, -iz, iz)), i1 = new Vector2(Mathf.Clamp(o1.x, -ix, ix), Mathf.Clamp(o1.y, -iz, iz));
                    b.AddQuad(Top(o0), Top(o1), Top(i1), Top(i0), porcelain, Vector3.up);
                }
            }

            // trip lever on the front-left of the tank
            float front = TankZ + TankD * 0.5f;
            b.AddCylinder(new Vector3(0.175f, LeverY, front + 0.006f), Quaternion.Euler(90f, 0f, 0f), 0.022f, 0.012f, 14, chrome);
            b.AddBox(new Vector3(0.14f, LeverY, front + 0.016f), new Vector3(0.075f, 0.013f, 0.012f), chrome);
            // closet-bolt caps either side of the foot
            for (int s = -1; s <= 1; s += 2)
                b.AddLathe(new Vector3(s * 0.118f, 0.01f, RoughIn), Quaternion.identity, new[] { new Vector2(0.018f, 0f), new Vector2(0.017f, 0.018f), new Vector2(0.008f, 0.03f), new Vector2(0f, 0.032f) }, 10, porcelain);

            // shut-off stop on the wall and braided supply up to the fill valve shank under the tank
            if (braid >= 0)
            {
                b.AddCylinder(new Vector3(StopX, 0.16f, 0.03f), Quaternion.Euler(90f, 0f, 0f), 0.012f, 0.06f, 10, chrome);   // stop body
                b.AddCylinder(new Vector3(StopX, 0.16f, 0.075f), Quaternion.Euler(90f, 0f, 0f), 0.018f, 0.02f, 10, chrome);  // oval handle
                b.AddCylinder(new Vector3(StopX, 0.19f, 0.045f), 0.008f, 0.04f, 8, chrome);                                     // outlet
                b.AddCylinder(new Vector3(StopX, TankBottom - 0.015f, TankZ), 0.016f, 0.03f, 8, chrome);                        // coupling nut
                Tube(b, new Vector3(StopX, 0.21f, 0.045f), new Vector3(StopX, 0.28f, 0.06f), 0.0055f, braid);
                Tube(b, new Vector3(StopX, 0.28f, 0.06f), new Vector3(StopX, TankBottom - 0.03f, TankZ), 0.0055f, braid);
            }
            b.Transform = prev;
        }

        static Vector3 Top(Vector2 p) => new Vector3(p.x, TankTop, TankZ - p.y);

        static void Tube(MeshBuilder b, Vector3 a, Vector3 c, float r, int mat)
        {
            Vector3 d = c - a; if (d.sqrMagnitude < 1e-8f) return;
            b.AddCylinder((a + c) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized), r, d.magnitude, 8, mat);
        }

        /// <summary>Tank lid lying flat with its underside at <paramref name="bottomCenter"/> (e.g. set on the floor while working).</summary>
        public static void TankLid(MeshBuilder b, Vector3 bottomCenter, Quaternion rot, int porcelain)
        {
            var prev = b.Transform;
            TankLidLocal(b, bottomCenter, rot, porcelain);
            b.Transform = prev;
        }

        static void TankLidLocal(MeshBuilder b, Vector3 bottomCenter, Quaternion rot, int porcelain)
        {
            var lid = RoundRect(TankW + 0.02f, TankD + 0.025f, 0.04f, 3);
            b.AddExtrude(lid, bottomCenter + rot * new Vector3(0f, LidT * 0.5f, 0f), rot * Quaternion.Euler(-90f, 0f, 0f), LidT, porcelain);
        }

        /// <summary>Flat water (or blockage) surface filling the bowl at height y.</summary>
        public static void Water(MeshBuilder b, Vector3 origin, Quaternion rot, float y, int mat)
        {
            var prev = b.Transform;
            b.Transform = Frame(b, origin, rot) * Matrix4x4.TRS(new Vector3(0f, y, BowlZ), Quaternion.identity, new Vector3(1f, 1f, BowlK));
            b.AddDisc(Vector3.zero, Quaternion.identity, Mathf.Max(0.01f, InnerR(y) - 0.002f), 24, mat, true);
            b.Transform = prev;
        }

        /// <summary>Rounded rectangle (w along X, d along Y), convex, counter-clockwise.</summary>
        static List<Vector2> RoundRect(float w, float d, float r, int seg)
        {
            var pts = new List<Vector2>(4 * (seg + 1));
            float hx = w * 0.5f - r, hy = d * 0.5f - r;
            Vector2[] cs = { new Vector2(hx, hy), new Vector2(-hx, hy), new Vector2(-hx, -hy), new Vector2(hx, -hy) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = (c + i / (float)seg) * Mathf.PI * 0.5f;
                    pts.Add(cs[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            return pts;
        }
    }
}
