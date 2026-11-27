using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Real-size procedural models of the company's fleet vehicles. One mesh per vehicle (one submesh per material),
    /// UVs in meters via <see cref="MeshBuilder"/>. Origin = on the ground under the vehicle's center, front faces +Z,
    /// +X is the passenger (right) side. Wheels touch y = 0.
    /// </summary>
    public static class VehicleModels
    {
        // ================================================================== public API

        /// <summary>Optional: builds the "van" model instead of the built-in one (Integration uses the drivable WorkVan's model, so
        /// a van parked at the yard and the one you drive are the same vehicle). Same frame: origin on the ground, front +Z.</summary>
        public static System.Func<Transform, bool, GameObject> VanOverride;

        /// <summary>Builds the model as a child of parent (may be null). Origin = on the ground under the vehicle's center, front faces +Z.
        /// Adds BoxCollider(s) roughly matching body/bed so the player can't walk through. rack = add a ladder/lumber rack (van, pickup).</summary>
        public static GameObject Build(string typeId, Transform parent, bool rack = false)
        {
            string id = Normalize(typeId);
            if (id == "van" && VanOverride != null) { var o = VanOverride(parent, rack); if (o != null) return o; }
            string key = id + (rack ? "|rack" : "");
            if (!TryCache(key, out Entry e))
            {
                var k = new Kit();
                switch (id)
                {
                    case "pickup": Pickup(k, rack); break;
                    case "flatbed": Flatbed(k); break;
                    case "dump": Dump(k); break;
                    case "box": BoxTruck(k); break;
                    case "boom": Boom(k); break;
                    case "skidsteer": SkidSteer(k); break;
                    default: Van(k, rack); break;
                }
                if (k.Mats.Count == 0) k.M(SurfaceKind.PaintedWhite);
                k.B.MinSubmeshCount = k.Mats.Count;
                e = new Entry { Mesh = k.B.Build("Vehicle_" + key), Mats = k.Mats.ToArray(), Cols = k.Cols.ToArray() };
                Cache[key] = e;
            }

            var go = new GameObject("Vehicle_" + id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = e.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = e.Mats;
            for (int i = 0; i < e.Cols.Length; i++)
            {
                var c = go.AddComponent<BoxCollider>();
                c.center = e.Cols[i].center;
                c.size = e.Cols[i].size;
            }
            return go;
        }

        /// <summary>Overall (width, height, length) in meters of the built model, for parking layout. Width includes mirrors.</summary>
        public static Vector3 SizeOf(string typeId)
        {
            switch (Normalize(typeId))
            {
                case "pickup": return new Vector3(2.57f, 1.96f, 5.89f);
                case "flatbed": return new Vector3(2.57f, 2.06f, 7.30f);
                case "dump": return new Vector3(2.78f, 2.80f, 6.66f);
                case "box": return new Vector3(2.58f, 3.39f, 7.18f);
                case "boom": return new Vector3(2.78f, 3.60f, 10.07f);
                case "skidsteer": return new Vector3(2.59f, 2.68f, 6.13f);
                default: return new Vector3(2.54f, 2.56f, 5.99f); // van
            }
        }

        // ================================================================== cache

        struct Entry { public Mesh Mesh; public Material[] Mats; public Bounds[] Cols; }
        static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Cache.Clear();
        }

        static bool TryCache(string key, out Entry e)
        {
            if (!Cache.TryGetValue(key, out e)) return false;
            if (e.Mesh == null || e.Mats == null || e.Cols == null) return false;
            for (int i = 0; i < e.Mats.Length; i++) if (e.Mats[i] == null) return false;
            return true;
        }

        static string Normalize(string typeId)
        {
            string id = (typeId ?? "").Trim().ToLowerInvariant();
            switch (id)
            {
                case "van":
                case "pickup":
                case "flatbed":
                case "dump":
                case "box":
                case "boom":
                case "skidsteer":
                    return id;
            }
            Debug.LogWarning("VehicleModels: unknown vehicle type '" + typeId + "', using van.");
            return "van";
        }

        // ================================================================== kit

        static readonly Color Navy = new Color(0.10f, 0.19f, 0.38f);
        static readonly Color GrayTint = new Color(0.30f, 0.32f, 0.36f);
        static readonly Color OrangeTint = new Color(1f, 0.42f, 0.06f);
        static readonly Color AmberTint = new Color(1f, 0.62f, 0.10f);

        /// <summary>Material palette -> submesh index, min/max box helpers, collider list.</summary>
        sealed class Kit
        {
            public readonly MeshBuilder B = new MeshBuilder();
            public readonly List<Material> Mats = new List<Material>();
            public readonly List<Bounds> Cols = new List<Bounds>();
            readonly Dictionary<Material, int> index = new Dictionary<Material, int>();
            static Material fallback;

            public int Mat(Material m)
            {
                if (m == null)
                {
                    if (fallback == null) fallback = new Material(Shader.Find("Standard")) { name = "GB_VehicleFallback" };
                    m = fallback;
                }
                if (!index.TryGetValue(m, out int i)) { i = Mats.Count; Mats.Add(m); index[m] = i; }
                return i;
            }
            public int M(SurfaceKind kind) => Mat(Materials.Get(kind));
            public int M(SurfaceKind kind, Color tint) => Mat(Materials.Get(kind, tint));

            public int White => M(SurfaceKind.Porcelain);            // glossy white body paint
            public int Panel => M(SurfaceKind.PaintedWhite);         // satin white (box body, plates)
            public int Stripe => M(SurfaceKind.Porcelain, Navy);     // company stripe
            public int Gray => M(SurfaceKind.PaintedSteel, GrayTint);
            public int Orange => M(SurfaceKind.Porcelain, OrangeTint);
            public int Amber => M(SurfaceKind.Porcelain, AmberTint);
            public int Red => M(SurfaceKind.TruckPaint);
            public int Glass => M(SurfaceKind.Glass);
            public int Blk => M(SurfaceKind.MatteBlack);
            public int Dark => M(SurfaceKind.DarkPlastic);
            public int Tire => M(SurfaceKind.Tire);
            public int Rim => M(SurfaceKind.Galvanized);
            public int Chrome => M(SurfaceKind.Nickel);
            public int Steel => M(SurfaceKind.BrushedSteel);
            public int Wood => M(SurfaceKind.Oak);
            public int Treated => M(SurfaceKind.TreatedLumber);

            public void Box(float x0, float x1, float y0, float y1, float z0, float z1, int sub)
            {
                B.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                         new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), sub);
            }
            public void BoxU(float x0, float x1, float y0, float y1, float z0, float z1, int sub, Vector3 uHint)
            {
                B.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                         new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), Quaternion.identity, sub, uHint);
            }
            /// <summary>Same box on both sides (x mirrored).</summary>
            public void SymBox(float xa, float xb, float y0, float y1, float z0, float z1, int sub)
            {
                Box(xa, xb, y0, y1, z0, z1, sub);
                Box(-xa, -xb, y0, y1, z0, z1, sub);
            }
            public void BoxR(Vector3 c, Vector3 size, Quaternion r, int sub) => B.AddBox(c, size, r, sub);
            public void CylY(float x, float z, float y0, float y1, float r, int segs, int sub)
                => B.AddCylinder(new Vector3(x, (y0 + y1) * 0.5f, z), Quaternion.identity, r, Mathf.Abs(y1 - y0), segs, sub);
            public void CylX(float y, float z, float x0, float x1, float r, int segs, int sub)
                => B.AddCylinder(new Vector3((x0 + x1) * 0.5f, y, z), Quaternion.Euler(0f, 0f, 90f), r, Mathf.Abs(x1 - x0), segs, sub);
            public void CylZ(float x, float y, float z0, float z1, float r, int segs, int sub)
                => B.AddCylinder(new Vector3(x, y, (z0 + z1) * 0.5f), Quaternion.Euler(90f, 0f, 0f), r, Mathf.Abs(z1 - z0), segs, sub);

            /// <summary>Convex side profile given as (z, y) points, extruded across X from x0 to x1.</summary>
            public void Prof(IList<Vector2> zy, float x0, float x1, int sub)
            {
                var poly = new List<Vector2>(zy.Count);
                for (int i = 0; i < zy.Count; i++)
                {
                    var q = new Vector2(-zy[i].x, zy[i].y);
                    if (poly.Count > 0 && (poly[poly.Count - 1] - q).sqrMagnitude < 1e-8f) continue;
                    poly.Add(q);
                }
                if (poly.Count > 2 && (poly[0] - poly[poly.Count - 1]).sqrMagnitude < 1e-8f) poly.RemoveAt(poly.Count - 1);
                if (poly.Count < 3) return;
                B.AddExtrude(poly, new Vector3((x0 + x1) * 0.5f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), Mathf.Abs(x1 - x0), sub);
            }

            public void Col(float x0, float x1, float y0, float y1, float z0, float z1)
            {
                Cols.Add(new Bounds(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                                    new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0))));
            }
        }

        // ================================================================== shared parts

        /// <summary>Wheel with its axis along X. c = center, sd = +1 right / -1 left (outboard side), R = tire radius, w = tire width, ri = rim radius.</summary>
        static void Wheel(Kit k, Vector3 c, int sd, float R, float w, float ri, bool detail, int rimSub = -1)
        {
            if (rimSub < 0) rimSub = k.Rim;
            Quaternion rot = Quaternion.Euler(0f, 0f, sd > 0 ? -90f : 90f); // local +Y points outboard
            float hw = w * 0.5f, sh = Mathf.Min(0.035f, w * 0.2f);
            k.B.AddLathe(c, rot, new[]
            {
                new Vector2(ri, -hw), new Vector2(R - sh, -hw), new Vector2(R, -hw + sh),
                new Vector2(R, hw - sh), new Vector2(R - sh, hw), new Vector2(ri, hw),
            }, 16, k.Tire);
            k.B.AddCylinder(c, rot, ri + 0.002f, w - 0.06f, 14, rimSub);
            if (!detail) return;
            float face = hw - 0.03f;
            Vector3 o = rot * Vector3.up;
            int chrome = k.Chrome;
            k.B.AddCylinder(c + o * (face + 0.012f), rot, ri * 0.36f, 0.03f, 10, chrome);
            float lr = ri * 0.58f;
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                Vector3 local = new Vector3(Mathf.Cos(a) * lr, face + 0.008f, Mathf.Sin(a) * lr);
                k.B.AddCylinder(c + rot * local, rot, 0.014f, 0.018f, 5, chrome);
            }
        }

        static Vector2 ArcPt(float zc, float yc, float r, float a) => new Vector2(zc + Mathf.Cos(a) * r, yc + Mathf.Sin(a) * r);

        static Vector2 OuterPt(float zc, float yc, float ra, float H, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            float t = float.MaxValue;
            if (Mathf.Abs(c) > 1e-4f) t = Mathf.Min(t, ra / Mathf.Abs(c));
            if (s > 1e-4f) t = Mathf.Min(t, H / s);
            return new Vector2(zc + c * t, yc + s * t);
        }

        /// <summary>Fills the rectangle z in [zc-ra, zc+ra], y in [yc, yTop] minus the wheel circle (radius ra at zc,yc),
        /// as a slab from x0 to x1: a body panel with a round wheel opening.</summary>
        static void Arch(Kit k, float x0, float x1, float zc, float yc, float ra, float yTop, int sub)
        {
            const int N = 6;
            float H = Mathf.Max(yTop - yc, ra + 0.01f);
            float corner = Mathf.Atan2(H, ra);
            var pts = new List<Vector2>(6);
            for (int i = 0; i < N; i++)
            {
                float a0 = Mathf.PI * i / N, a1 = Mathf.PI * (i + 1) / N;
                pts.Clear();
                pts.Add(ArcPt(zc, yc, ra, a0));
                pts.Add(ArcPt(zc, yc, ra, a1));
                pts.Add(OuterPt(zc, yc, ra, H, a1));
                if (a0 < Mathf.PI - corner && Mathf.PI - corner < a1) pts.Add(new Vector2(zc - ra, yc + H));
                if (a0 < corner && corner < a1) pts.Add(new Vector2(zc + ra, yc + H));
                pts.Add(OuterPt(zc, yc, ra, H, a0));
                k.Prof(pts, x0, x1, sub);
            }
        }

        /// <summary>Lower body side slabs (both sides) from x = ±xi to ±xo with round wheel openings at wz (ascending z).</summary>
        static void SideSlab(Kit k, float xi, float xo, float zR, float zF, float yBot, float yTop, float[] wz, float yc, float ra, int sub)
        {
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float x0 = sd * xi, x1 = sd * xo;
                float z = zR;
                for (int i = 0; i < wz.Length; i++)
                {
                    float a = wz[i] - ra, b = wz[i] + ra;
                    if (a > z + 0.001f) k.Box(x0, x1, yBot, yTop, z, a, sub);
                    Arch(k, x0, x1, wz[i], yc, ra, yTop, sub);
                    z = b;
                }
                if (zF > z + 0.001f) k.Box(x0, x1, yBot, yTop, z, zF, sub);
            }
        }

        /// <summary>Dark wheel-well back plates on the body core.</summary>
        static void Liner(Kit k, float xc, float wz, float y0, float y1, float ra)
        {
            k.SymBox(xc, xc + 0.006f, y0, y1, wz - ra, wz + ra, k.Blk);
        }

        // ---- lofted bodies: cross-sections {z, x0,y0, x1,y1, x2,y2, x3,y3} (right half, bottom to top), mirrored about X.

        static float[] S(params float[] v) => v;

        static Vector3[] Ring(float[] s)
        {
            float z = s[0];
            return new[]
            {
                new Vector3(s[1], s[2], z), new Vector3(s[3], s[4], z), new Vector3(s[5], s[6], z), new Vector3(s[7], s[8], z),
                new Vector3(-s[7], s[8], z), new Vector3(-s[5], s[6], z), new Vector3(-s[3], s[4], z), new Vector3(-s[1], s[2], z),
            };
        }

        static Vector3 Hint(Vector3[] ra, Vector3[] rb, Vector3 mid)
        {
            float cy = 0f;
            for (int i = 0; i < 8; i++) cy += ra[i].y + rb[i].y;
            cy /= 16f;
            return new Vector3(mid.x, mid.y - cy, 0f);
        }

        /// <summary>Skins consecutive cross-sections (faces 0..2 = right side bottom to top, 3 = top, 4..6 = left side, 7 = bottom).</summary>
        static void Loft(Kit k, float[][] secs, int sub)
        {
            int n = secs.Length;
            var rings = new Vector3[n][];
            for (int i = 0; i < n; i++) rings[i] = Ring(secs[i]);
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    int jn = (j + 1) % 8;
                    Vector3 a = rings[i][j], b = rings[i][jn], c = rings[i + 1][jn], d = rings[i + 1][j];
                    Vector3 mid = (a + b + c + d) * 0.25f;
                    k.B.AddQuad(a, b, c, d, sub, Hint(rings[i], rings[i + 1], mid), Vector3.forward);
                }
            }
            k.B.AddPolygon(rings[0], sub, Vector3.back);
            k.B.AddPolygon(rings[n - 1], sub, Vector3.forward);
        }

        /// <summary>A quad lying on loft face j between sections sa and sb (u along z from sa to sb, v along the face edge), pushed out by off.</summary>
        static void Patch(Kit k, float[] sa, float[] sb, int j, float u0, float u1, float v0, float v1, int sub, float off)
        {
            Vector3[] ra = Ring(sa), rb = Ring(sb);
            int jn = (j + 1) % 8;
            Vector3 a = PatchPt(ra, rb, j, jn, u0, v0), b = PatchPt(ra, rb, j, jn, u1, v0);
            Vector3 c = PatchPt(ra, rb, j, jn, u1, v1), d = PatchPt(ra, rb, j, jn, u0, v1);
            Vector3 n = Vector3.Cross(c - a, d - b);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            if (Vector3.Dot(n, Hint(ra, rb, (a + b + c + d) * 0.25f)) < 0f) n = -n;
            Vector3 o = n * off;
            k.B.AddQuad(a + o, b + o, c + o, d + o, sub, n);
        }

        static Vector3 PatchPt(Vector3[] ra, Vector3[] rb, int j, int jn, float u, float v)
            => Vector3.Lerp(Vector3.Lerp(ra[j], ra[jn], v), Vector3.Lerp(rb[j], rb[jn], v), u);

        /// <summary>Tinted glazing on a loft face: dark backing + glass pane.</summary>
        static void Glaze(Kit k, float[] sa, float[] sb, int j, float u0, float u1, float v0, float v1)
        {
            if (u1 - u0 < 0.01f || v1 - v0 < 0.01f) return;
            Patch(k, sa, sb, j, u0, u1, v0, v1, k.Dark, 0.006f);
            Patch(k, sa, sb, j, u0, u1, v0, v1, k.Glass, 0.011f);
        }

        /// <summary>Side windows on both sides (upper side face: 1 on the right, 5 on the left).</summary>
        static void GlazeSides(Kit k, float[] sa, float[] sb, float u0, float u1, float v0, float v1)
        {
            Glaze(k, sa, sb, 1, u0, u1, v0, v1);
            Glaze(k, sa, sb, 5, u0, u1, 1f - v1, 1f - v0);
        }

        /// <summary>Thin dark seam line across loft face j (right) and/or its mirror (left) at world z. side: 0 both, +1 right, -1 left.</summary>
        static void LoftSeam(Kit k, float[] sa, float[] sb, float z, int j, int sub, int side = 0)
        {
            float len = Mathf.Abs(sb[0] - sa[0]);
            if (len < 1e-4f) return;
            float u = Mathf.InverseLerp(sa[0], sb[0], z), du = 0.005f / len;
            if (side >= 0) Patch(k, sa, sb, j, u - du, u + du, 0f, 1f, sub, 0.003f);
            if (side <= 0) Patch(k, sa, sb, 6 - j, u - du, u + du, 0f, 1f, sub, 0.003f);
        }

        static void FlatQuad(Kit k, float x0, float x1, float y0, float y1, float z, int dir, int sub)
        {
            k.B.AddQuad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), sub, new Vector3(0f, 0f, dir));
        }

        /// <summary>Tinted window on a flat face at z, facing dir (+1 = +Z, -1 = -Z).</summary>
        static void FlatGlaze(Kit k, float x0, float x1, float y0, float y1, float z, int dir)
        {
            FlatQuad(k, x0, x1, y0, y1, z + dir * 0.006f, dir, k.Dark);
            FlatQuad(k, x0, x1, y0, y1, z + dir * 0.011f, dir, k.Glass);
        }

        static void Rod(Kit k, Vector3 a, Vector3 b, float r, int segs, int sub)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            k.B.AddCylinder((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len), r, len, segs, sub);
        }

        // ================================================================== conventional cab (hood + cab), shared by pickup and trucks

        sealed class CabP
        {
            public float W;                                        // body half width
            public float zBack, zBrow, zWsTop, zCowl, zNose;       // cab back, roof brow, windshield top, windshield base, grille face
            public float yBot, ySill, yBelt, yRoof, yCowl, yNose;  // rocker, lower/upper body split, window sill, roof, hood at cowl, hood at nose
            public float axleZ, R, tireW, rimR, ra;                // front axle, tire radius/width, rim radius, wheel-arch radius
            public float doorRear, grilleY0, bumpY0, bumpY1, bumpD, stripeY0, stripeY1;
            public bool extended, chromeBumper, heavy;
        }

        static void ConvCab(Kit k, CabP p)
        {
            int white = k.White, dark = k.Dark, chrome = k.Chrome;
            float W = p.W, W2 = W - 0.006f;
            float tx = W - 0.03f - p.tireW * 0.5f;      // tire center
            float xc = tx - p.tireW * 0.5f - 0.03f;      // body core half width (between the tires)

            // lower body: core + side slabs with wheel openings
            k.Box(-xc, xc, p.yBot, p.ySill, p.zBack, p.zNose, white);
            SideSlab(k, xc, W, p.zBack, p.zNose, p.yBot, p.ySill, new[] { p.axleZ }, p.R, p.ra, white);
            Liner(k, xc, p.axleZ, Mathf.Min(p.yBot, p.R), p.R + p.ra, p.ra);
            for (int sd = -1; sd <= 1; sd += 2)
                Wheel(k, new Vector3(sd * tx, p.R, p.axleZ), sd, p.R, p.tireW, p.rimR, true);

            // upper body: cab greenhouse, windshield, hood
            float[] sBack = S(p.zBack, W2, p.ySill, W2, p.yBelt, W2 - 0.03f, p.yRoof - 0.13f, W2 - 0.12f, p.yRoof);
            float[] sBrow = S(p.zBrow, W2, p.ySill, W2, p.yBelt, W2 - 0.03f, p.yRoof - 0.13f, W2 - 0.12f, p.yRoof - 0.012f);
            float[] sTop = S(p.zWsTop, W2, p.ySill, W2, p.yBelt, W2 - 0.03f, p.yRoof - 0.13f, W2 - 0.12f, p.yRoof - 0.075f);
            float[] sCowl = S(p.zCowl, W2, p.ySill, W2 - 0.008f, p.yCowl - 0.06f, W2 - 0.03f, p.yCowl - 0.025f, W2 - 0.12f, p.yCowl);
            float[] sNose = S(p.zNose, W2 - 0.03f, p.ySill, W2 - 0.035f, p.yNose - 0.07f, W2 - 0.055f, p.yNose - 0.03f, W2 - 0.14f, p.yNose);
            Loft(k, new[] { sBack, sBrow, sTop, sCowl, sNose }, white);

            // glazing
            Glaze(k, sTop, sCowl, 3, 0.05f, 0.95f, 0.04f, 0.96f);                 // windshield
            Patch(k, sTop, sCowl, 3, 0.955f, 1f, 0.03f, 0.97f, dark, 0.004f);     // cowl / wiper strip
            float uD = Mathf.InverseLerp(p.zBack, p.zWsTop, p.doorRear);
            if (p.extended) GlazeSides(k, sBack, sTop, 0.06f, uD - 0.035f, 0.10f, 0.90f);
            GlazeSides(k, sBack, sTop, Mathf.Max(uD + 0.035f, 0.10f), 0.97f, 0.10f, 0.90f);
            GlazeSides(k, sTop, sCowl, 0.04f, 0.78f, 0.14f, 0.88f);
            FlatGlaze(k, -W2 * 0.62f, W2 * 0.62f, p.yBelt + 0.08f, p.yRoof - 0.20f, p.zBack, -1);

            // door seams + handles
            float zSeamF = Mathf.Min(p.axleZ - p.ra - 0.03f, p.zCowl - 0.02f);
            float[] seams = { p.doorRear, zSeamF };
            for (int i = 0; i < seams.Length; i++)
            {
                float z = seams[i];
                k.SymBox(W, W + 0.004f, p.yBot + 0.03f, p.ySill, z - 0.005f, z + 0.005f, dark);
                bool rear = z <= p.zWsTop;
                float[] a = rear ? sBack : sTop, b = rear ? sTop : sCowl;
                LoftSeam(k, a, b, z, 0, dark);
                if (i == 0) LoftSeam(k, a, b, z, 1, dark);
            }
            k.SymBox(W2 - 0.012f, W2 + 0.02f, p.yBelt - 0.13f, p.yBelt - 0.095f, p.doorRear + 0.06f, p.doorRear + 0.22f, p.chromeBumper ? chrome : dark);

            // company stripe on the lower body
            int stripe = k.Stripe;
            k.SymBox(W, W + 0.002f, p.stripeY0, p.stripeY1, p.zBack + 0.01f, p.axleZ - p.ra - 0.045f, stripe);
            if (p.zNose - 0.02f > p.axleZ + p.ra + 0.145f)
                k.SymBox(W, W + 0.002f, p.stripeY0, p.stripeY1, p.axleZ + p.ra + 0.045f, p.zNose - 0.02f, stripe);

            // grille
            float zn = p.zNose, gx = W * 0.56f, gy1 = p.yNose - 0.06f, gy0 = p.grilleY0;
            k.Box(-gx, gx, gy0, gy1, zn - 0.01f, zn + 0.02f, dark);
            int bars = Mathf.Max(2, Mathf.RoundToInt((gy1 - gy0) / 0.11f));
            for (int i = 1; i < bars; i++)
            {
                float y = Mathf.Lerp(gy0, gy1, i / (float)bars);
                k.Box(-gx + 0.03f, gx - 0.03f, y - 0.014f, y + 0.014f, zn + 0.02f, zn + 0.032f, chrome);
            }
            k.Box(-gx - 0.03f, gx + 0.03f, gy1, gy1 + 0.035f, zn - 0.005f, zn + 0.04f, chrome);
            k.Box(-gx - 0.03f, gx + 0.03f, gy0 - 0.035f, gy0, zn - 0.005f, zn + 0.04f, chrome);
            k.SymBox(gx, gx + 0.03f, gy0, gy1, zn - 0.005f, zn + 0.04f, chrome);

            // headlights + turn signals
            float hx0 = gx + 0.05f, hx1 = W - 0.06f, hy1 = gy1, hy0 = gy1 - 0.22f;
            k.SymBox(hx0, hx1, hy0, hy1, zn - 0.005f, zn + 0.018f, chrome);
            int glass = k.Glass;
            FlatQuad(k, hx0 + 0.008f, hx1 - 0.008f, hy0 + 0.008f, hy1 - 0.008f, zn + 0.024f, 1, glass);
            FlatQuad(k, -hx1 + 0.008f, -hx0 - 0.008f, hy0 + 0.008f, hy1 - 0.008f, zn + 0.024f, 1, glass);
            k.SymBox(hx0, hx1, hy0 - 0.075f, hy0 - 0.015f, zn - 0.005f, zn + 0.018f, k.Amber);

            // bumper, valance, plate
            k.Box(-(W + 0.012f), W + 0.012f, p.bumpY0, p.bumpY1, zn - 0.05f, zn + p.bumpD, p.chromeBumper ? chrome : dark);
            if (p.yBot < p.bumpY0 - 0.01f) k.Box(-(W - 0.02f), W - 0.02f, p.yBot - 0.02f, p.bumpY0, zn - 0.02f, zn + 0.03f, dark);
            k.Box(-0.155f, 0.155f, p.bumpY0 + 0.04f, p.bumpY0 + 0.19f, zn + p.bumpD, zn + p.bumpD + 0.004f, k.Panel);

            // mirrors
            float mz = p.zCowl - 0.12f, my = p.yCowl - 0.10f, mh = p.heavy ? 0.46f : 0.25f;
            k.SymBox(W2 - 0.03f, W + 0.12f, my - 0.01f, my + 0.025f, mz - 0.018f, mz + 0.018f, dark);
            k.SymBox(W + 0.09f, W + 0.27f, my, my + mh, mz - 0.045f, mz + 0.045f, dark);
            k.SymBox(W + 0.105f, W + 0.255f, my + 0.02f, my + mh - 0.02f, mz - 0.051f, mz - 0.045f, chrome);

            // roof marker lights (medium/heavy trucks)
            if (p.heavy)
            {
                int amber = k.Amber;
                for (int i = -2; i <= 2; i++)
                    k.Box(i * 0.3f - 0.035f, i * 0.3f + 0.035f, p.yRoof - 0.02f, p.yRoof + 0.035f, p.zBrow - 0.15f, p.zBrow - 0.05f, amber);
            }
        }

        // ================================================================== truck chassis parts

        static void Frame(Kit k, float z0, float z1, float yTop, float h)
        {
            int blk = k.Blk;
            k.SymBox(0.39f, 0.47f, yTop - h, yTop, z0, z1, blk);
            int n = Mathf.Max(2, Mathf.RoundToInt((z1 - z0) / 1.1f));
            for (int i = 0; i <= n; i++)
            {
                float z = Mathf.Lerp(z0 + 0.06f, z1 - 0.06f, i / (float)n);
                k.Box(-0.39f, 0.39f, yTop - h + 0.04f, yTop - 0.04f, z - 0.04f, z + 0.04f, blk);
            }
        }

        /// <summary>Rear axle with wheels. innerX &gt; 0 adds the inner wheel of a dual pair.</summary>
        static void RearAxle(Kit k, float z, float R, float tw, float ri, float outerX, float innerX)
        {
            int blk = k.Blk;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Wheel(k, new Vector3(sd * outerX, R, z), sd, R, tw, ri, true);
                if (innerX > 0f) Wheel(k, new Vector3(sd * innerX, R, z), sd, R, tw, ri, false);
            }
            k.CylX(R, z, -outerX, outerX, 0.055f, 8, blk);
            k.CylZ(0f, R, z - 0.17f, z + 0.17f, 0.17f, 10, blk);                       // differential
            k.SymBox(0.48f, 0.55f, R + 0.06f, R + 0.13f, z - 0.6f, z + 0.6f, blk);     // leaf springs
        }

        static void Mudflaps(Kit k, float z, float x0, float x1, float y0, float y1)
        {
            k.SymBox(x0, x1, y0, y1, z - 0.006f, z + 0.006f, k.Dark);
        }

        /// <summary>Steel-framed plank deck from z0 (rear) to z1 (front): sills on the frame, crossmember layer, rub rails, boards, pockets, tail lights.</summary>
        static void FlatDeck(Kit k, float z0, float z1, float halfW, float yTop, float yFrameTop, int boards)
        {
            int blk = k.Blk, wood = k.Wood;
            float xi = halfW - 0.05f;
            k.SymBox(0.38f, 0.48f, yFrameTop, yTop - 0.14f, z0 + 0.05f, z1 - 0.05f, blk);                 // long sills
            k.Box(-(halfW - 0.03f), halfW - 0.03f, yTop - 0.14f, yTop - 0.04f, z0 + 0.02f, z1 - 0.02f, blk); // crossmembers
            k.SymBox(xi, halfW, yTop - 0.15f, yTop, z0, z1, blk);                                          // side rub rails
            k.Box(-xi, xi, yTop - 0.15f, yTop, z0, z0 + 0.05f, blk);                                       // rear rail
            k.Box(-xi, xi, yTop - 0.15f, yTop, z1 - 0.05f, z1, blk);                                       // front rail
            float bw = 2f * xi / boards;
            for (int i = 0; i < boards; i++)
            {
                float x = -xi + i * bw;
                k.BoxU(x + 0.003f, x + bw - 0.003f, yTop - 0.04f, yTop, z0 + 0.05f, z1 - 0.05f, wood, Vector3.forward);
            }
            int n = Mathf.Max(2, Mathf.RoundToInt((z1 - z0) / 0.6f));
            for (int i = 0; i <= n; i++)                                                                   // stake pockets
            {
                float z = Mathf.Lerp(z0 + 0.15f, z1 - 0.15f, i / (float)n);
                k.SymBox(halfW, halfW + 0.02f, yTop - 0.13f, yTop - 0.02f, z - 0.04f, z + 0.04f, blk);
            }
            k.SymBox(halfW - 0.36f, halfW - 0.12f, yTop - 0.12f, yTop - 0.04f, z0 - 0.012f, z0, k.Red);   // tail lights
            k.SymBox(halfW - 0.50f, halfW - 0.40f, yTop - 0.12f, yTop - 0.04f, z0 - 0.012f, z0, k.Amber);
            k.Box(-0.155f, 0.155f, yTop - 0.145f, yTop - 0.02f, z0 - 0.006f, z0, k.Panel);                // plate
        }

        /// <summary>Removable stake-rack run: steel stakes in the rub-rail pockets + four board slats per section.
        /// alongZ: fix = x of the stakes; else fix = z of the stakes. inward = direction (±1) of the slat side.</summary>
        static void StakeRun(Kit k, bool alongZ, float fix, float inward, float a0, float a1, float yDeck, float h, int sections)
        {
            int blk = k.Blk, wood = k.Wood;
            float seg = (a1 - a0) / sections;
            for (int s = 0; s < sections; s++)
            {
                float s0 = a0 + s * seg + 0.015f, s1 = a0 + (s + 1) * seg - 0.015f;
                for (int e = 0; e < 2; e++)
                {
                    float c = e == 0 ? s0 + 0.14f : s1 - 0.14f;
                    if (alongZ) k.Box(fix - 0.02f, fix + 0.02f, yDeck - 0.08f, yDeck + h, c - 0.045f, c + 0.045f, blk);
                    else k.Box(c - 0.045f, c + 0.045f, yDeck - 0.08f, yDeck + h, fix - 0.02f, fix + 0.02f, blk);
                }
                float t0 = fix + inward * 0.02f, t1 = fix + inward * 0.042f;
                for (int i = 0; i < 4; i++)
                {
                    float y = yDeck + 0.07f + i * (h - 0.25f) / 3f;
                    if (alongZ) k.BoxU(t0, t1, y, y + 0.14f, s0, s1, wood, Vector3.forward);
                    else k.BoxU(s0, s1, y, y + 0.14f, t0, t1, wood, Vector3.right);
                }
            }
        }

        static void HeadacheRack(Kit k, float z, float halfW, float y0, float y1)
        {
            int blk = k.Blk;
            float xo = halfW - 0.06f, xin = xo - 0.07f;
            k.SymBox(xin, xo, y0, y1, z - 0.035f, z + 0.035f, blk);
            k.Box(-xin, xin, y1 - 0.07f, y1, z - 0.035f, z + 0.035f, blk);
            float ym = y0 + (y1 - y0) * 0.45f;
            k.Box(-xin, xin, ym - 0.03f, ym + 0.03f, z - 0.03f, z + 0.03f, blk);
            k.Box(-xin, xin, y0, ym - 0.03f, z - 0.004f, z + 0.004f, blk);          // solid lower plate
            for (int i = 1; i <= 5; i++)                                             // louvers behind the cab window
            {
                float y = Mathf.Lerp(ym + 0.03f, y1 - 0.07f, i / 6f);
                k.Box(-xin, xin, y - 0.02f, y + 0.02f, z - 0.006f, z + 0.006f, blk);
            }
        }

        // ================================================================== van (Transit 250 medium roof, 148" WB)

        static void Van(Kit k, bool rack)
        {
            const float W = 1.032f, W2 = 1.026f, zF = 2.93f, zR = -2.95f;
            const float R = 0.356f, tw = 0.235f, ri = 0.215f, ra = 0.42f;
            const float yBot = 0.30f, ySill = 1.10f, yBelt = 1.50f, yRoof = 2.558f;
            const float fz = 1.97f, rz = -1.789f;
            int white = k.White, dark = k.Dark, blk = k.Blk, chrome = k.Chrome, stripe = k.Stripe;
            float tx = W - 0.03f - tw * 0.5f, xc = tx - tw * 0.5f - 0.03f;

            // lower body
            k.Box(-xc, xc, yBot, ySill, zR, zF, white);
            SideSlab(k, xc, W, zR, zF, yBot, ySill, new[] { rz, fz }, R, ra, white);
            Liner(k, xc, rz, yBot, R + ra, ra);
            Liner(k, xc, fz, yBot, R + ra, ra);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Wheel(k, new Vector3(sd * tx, R, fz), sd, R, tw, ri, true);
                Wheel(k, new Vector3(sd * tx, R, rz), sd, R, tw, ri, true);
            }

            // upper body
            float[] sRear = S(zR, W2, ySill, W2, yBelt, 1.0f, 2.40f, 0.90f, yRoof);
            float[] sB = S(0.55f, W2, ySill, W2, yBelt, 1.0f, 2.40f, 0.90f, yRoof);
            float[] sBrow = S(0.95f, W2, ySill, W2, yBelt, 1.0f, 2.40f, 0.90f, 2.545f);
            float[] sTop = S(1.25f, W2, ySill, W2, yBelt, 1.0f, 2.40f, 0.90f, 2.47f);
            float[] sCowl = S(2.25f, W2, ySill, 1.014f, 1.45f, 1.0f, 1.49f, 0.90f, 1.52f);
            float[] sNose = S(zF, 1.0f, ySill, 0.995f, 1.13f, 0.975f, 1.17f, 0.88f, 1.20f);
            Loft(k, new[] { sRear, sB, sBrow, sTop, sCowl, sNose }, white);

            // glazing
            Glaze(k, sTop, sCowl, 3, 0.04f, 0.96f, 0.04f, 0.96f);
            Patch(k, sTop, sCowl, 3, 0.965f, 1f, 0.03f, 0.97f, dark, 0.004f);
            GlazeSides(k, sB, sTop, 0.08f, 0.97f, 0.07f, 0.92f);
            GlazeSides(k, sTop, sCowl, 0.03f, 0.80f, 0.12f, 0.88f);
            FlatGlaze(k, 0.10f, 0.82f, 1.55f, 2.18f, zR, -1);
            FlatGlaze(k, -0.82f, -0.10f, 1.55f, 2.18f, zR, -1);

            // front door seams (both sides)
            k.SymBox(W, W + 0.004f, yBot + 0.04f, ySill, 0.555f, 0.565f, dark);
            LoftSeam(k, sB, sTop, 0.56f, 0, dark);
            LoftSeam(k, sB, sTop, 0.56f, 1, dark);
            k.SymBox(W, W + 0.004f, yBot + 0.04f, ySill, 1.525f, 1.535f, dark);
            LoftSeam(k, sTop, sCowl, 1.53f, 0, dark);
            k.SymBox(1.02f, 1.045f, 1.30f, 1.335f, 0.62f, 0.80f, dark);

            // sliding side door (passenger side): rear seam, track, handle
            k.Box(W, W + 0.004f, yBot + 0.04f, ySill, -0.755f, -0.745f, dark);
            LoftSeam(k, sRear, sB, -0.75f, 0, dark, 1);
            LoftSeam(k, sRear, sB, -0.75f, 1, dark, 1);
            k.Box(1.022f, 1.038f, 1.40f, 1.43f, -2.10f, -0.75f, dark);
            k.Box(1.02f, 1.045f, 1.30f, 1.335f, 0.30f, 0.48f, dark);

            // company stripe
            k.SymBox(W, W + 0.002f, 0.56f, 0.74f, zR + 0.02f, rz - ra - 0.04f, stripe);
            k.SymBox(W, W + 0.002f, 0.56f, 0.74f, rz + ra + 0.04f, fz - ra - 0.04f, stripe);
            k.SymBox(W, W + 0.002f, 0.56f, 0.74f, fz + ra + 0.04f, zF - 0.05f, stripe);

            // front: grille, headlights, bumper
            k.Box(-0.50f, 0.50f, 0.74f, 1.06f, zF - 0.01f, zF + 0.015f, dark);
            for (int i = 0; i < 3; i++)
            {
                float y = 0.80f + i * 0.09f;
                k.Box(-0.48f, 0.48f, y, y + 0.03f, zF + 0.015f, zF + 0.026f, chrome);
            }
            k.Box(-0.52f, 0.52f, 1.06f, 1.09f, zF - 0.005f, zF + 0.03f, chrome);
            k.SymBox(0.56f, 0.99f, 0.86f, 1.08f, zF - 0.005f, zF + 0.018f, chrome);
            int glass = k.Glass;
            FlatQuad(k, 0.568f, 0.982f, 0.868f, 1.072f, zF + 0.024f, 1, glass);
            FlatQuad(k, -0.982f, -0.568f, 0.868f, 1.072f, zF + 0.024f, 1, glass);
            k.SymBox(0.88f, 0.99f, 0.775f, 0.845f, zF - 0.005f, zF + 0.018f, k.Amber);
            k.Box(-1.04f, 1.04f, 0.32f, 0.66f, zF - 0.05f, zF + 0.06f, dark);
            k.Box(-0.75f, 0.75f, 0.36f, 0.45f, zF + 0.06f, zF + 0.065f, blk);
            k.Box(-0.155f, 0.155f, 0.48f, 0.63f, zF + 0.06f, zF + 0.064f, k.Panel);

            // mirrors
            k.SymBox(0.99f, 1.12f, 1.56f, 1.60f, 1.78f, 1.82f, dark);
            k.SymBox(1.10f, 1.27f, 1.46f, 1.78f, 1.76f, 1.86f, dark);
            k.SymBox(1.115f, 1.255f, 1.48f, 1.76f, 1.754f, 1.76f, chrome);

            // rear: barn doors, hinges, lights, bumper
            k.Box(-1.03f, 1.03f, 0.34f, 0.50f, zR - 0.045f, zR + 0.03f, dark);
            k.Box(-0.005f, 0.005f, 0.52f, 2.42f, zR - 0.004f, zR, dark);
            k.SymBox(0.93f, 0.94f, 0.52f, 2.42f, zR - 0.004f, zR, dark);
            k.Box(-0.93f, 0.93f, 2.42f, 2.43f, zR - 0.004f, zR, dark);
            k.Box(-0.93f, 0.93f, 0.51f, 0.52f, zR - 0.004f, zR, dark);
            k.Box(-0.09f, -0.03f, 1.18f, 1.36f, zR - 0.03f, zR, dark);
            k.Box(-0.62f, -0.31f, 0.80f, 0.95f, zR - 0.005f, zR, k.Panel);
            k.SymBox(0.945f, 0.995f, 0.70f, 0.76f, zR - 0.02f, zR, dark);
            k.SymBox(0.945f, 0.995f, 2.10f, 2.16f, zR - 0.02f, zR, dark);
            k.SymBox(0.95f, 1.03f, 1.15f, 1.95f, zR - 0.012f, zR + 0.02f, k.Red);

            if (rack)
            {
                int alu = k.Rim;
                float[] bz = { -2.30f, -0.90f, 0.50f };
                for (int i = 0; i < bz.Length; i++)
                {
                    float z = bz[i];
                    k.SymBox(0.80f, 0.88f, 2.54f, 2.66f, z - 0.05f, z + 0.05f, blk);
                    k.Box(-0.92f, 0.92f, 2.66f, 2.70f, z - 0.03f, z + 0.03f, alu);
                    k.SymBox(0.90f, 0.925f, 2.70f, 2.79f, z - 0.03f, z + 0.03f, blk);
                }
                // extension ladder riding on the rack
                k.BoxU(0.15f, 0.175f, 2.70f, 2.78f, -2.75f, 0.91f, alu, Vector3.forward);
                k.BoxU(0.535f, 0.56f, 2.70f, 2.78f, -2.75f, 0.91f, alu, Vector3.forward);
                for (int i = 0; i < 12; i++)
                {
                    float z = -2.60f + i * 0.305f;
                    k.Box(0.175f, 0.535f, 2.728f, 2.752f, z - 0.0125f, z + 0.0125f, alu);
                }
            }

            k.Col(-W, W, yBot, yRoof, zR - 0.045f, 2.25f);
            k.Col(-W, W, yBot, 1.50f, 2.25f, zF + 0.06f);
        }

        // ================================================================== pickup (half-ton, extended cab, 8' bed)

        static void Pickup(Kit k, bool rack)
        {
            const float W = 1.0145f, R = 0.40f, tw = 0.265f, ri = 0.235f, ra = 0.47f;
            const float rz = -1.604f, bedR = -2.90f, bedF = -0.33f, bedIn = 0.828f, floorY = 0.86f, railY = 1.404f;
            int white = k.White, dark = k.Dark, blk = k.Blk, chrome = k.Chrome;

            var p = new CabP
            {
                W = W, zBack = -0.30f, zBrow = 0.50f, zWsTop = 0.80f, zCowl = 1.50f, zNose = 2.85f,
                yBot = 0.38f, ySill = 0.92f, yBelt = 1.36f, yRoof = 1.956f, yCowl = 1.34f, yNose = 1.26f,
                axleZ = 1.99f, R = R, tireW = tw, rimR = ri, ra = ra,
                doorRear = 0.06f, grilleY0 = 0.78f, bumpY0 = 0.46f, bumpY1 = 0.70f, bumpD = 0.09f, stripeY0 = 0.50f, stripeY1 = 0.66f,
                extended = true, chromeBumper = true, heavy = false,
            };
            ConvCab(k, p);

            // bed
            float tx = W - 0.03f - tw * 0.5f;
            k.Box(-0.70f, 0.70f, 0.50f, 0.82f, bedR + 0.05f, p.zBack, blk);                 // frame / underbody
            k.Box(-bedIn, bedIn, 0.82f, floorY, bedR + 0.06f, bedF, dark);                  // floor (bedliner)
            SideSlab(k, bedIn, W, bedR, bedF, 0.42f, railY, new[] { rz }, R, ra, white);
            k.SymBox(0.66f, 0.84f, floorY, 1.0f, rz - 0.45f, rz + 0.45f, dark);             // wheelhouses
            k.Box(-bedIn, bedIn, floorY, railY, bedF - 0.06f, bedF, white);                 // front wall
            k.Box(-bedIn, bedIn, 0.82f, railY, bedR, bedR + 0.06f, white);                  // tailgate
            k.Box(-0.10f, 0.10f, 1.27f, 1.33f, bedR - 0.012f, bedR, dark);                  // tailgate handle
            k.SymBox(0.81f, 1.02f, railY, railY + 0.016f, bedR, bedF, dark);                // rail caps
            k.Box(-0.81f, 0.81f, railY, railY + 0.016f, bedR - 0.003f, bedR + 0.063f, dark);
            k.SymBox(0.85f, W + 0.004f, 0.98f, 1.36f, bedR - 0.008f, bedR + 0.03f, k.Red); // tail lights
            k.Box(-1.0f, 1.0f, 0.50f, 0.70f, -2.9425f, bedR + 0.04f, chrome);               // rear bumper
            k.Box(-0.45f, 0.45f, 0.70f, 0.725f, -2.94f, bedR - 0.002f, dark);
            k.Box(-0.155f, 0.155f, 0.53f, 0.67f, -2.9465f, -2.9425f, k.Panel);
            for (int sd = -1; sd <= 1; sd += 2) Wheel(k, new Vector3(sd * tx, R, rz), sd, R, tw, ri, true);
            int stripe = k.Stripe;
            k.SymBox(W, W + 0.002f, p.stripeY0, p.stripeY1, bedR + 0.02f, rz - ra - 0.04f, stripe);
            k.SymBox(W, W + 0.002f, p.stripeY0, p.stripeY1, rz + ra + 0.04f, bedF - 0.02f, stripe);

            if (rack)
            {
                // over-cab lumber rack: 4 posts, side rails cantilevered over the cab, crossbars
                float y0 = railY + 0.016f;
                k.SymBox(0.89f, 0.95f, y0, 2.05f, -0.45f, -0.39f, blk);
                k.SymBox(0.89f, 0.95f, y0, 2.05f, -2.83f, -2.77f, blk);
                k.SymBox(0.89f, 0.95f, y0, y0 + 0.04f, -2.77f, -0.45f, blk);
                k.SymBox(0.89f, 0.95f, 2.05f, 2.10f, -2.83f, 1.25f, blk);
                float[] bz = { -2.80f, -1.61f, -0.42f, 0.40f, 1.22f };
                for (int i = 0; i < bz.Length; i++) k.Box(-0.98f, 0.98f, 2.10f, 2.14f, bz[i] - 0.025f, bz[i] + 0.025f, blk);
                k.SymBox(0.94f, 0.98f, 2.14f, 2.24f, -2.825f, -2.775f, blk);
                k.SymBox(0.94f, 0.98f, 2.14f, 2.24f, 1.195f, 1.245f, blk);
                Vector3 dir = new Vector3(0f, 0.30f, 0.62f);
                for (int sd = -1; sd <= 1; sd += 2)                                           // cantilever braces
                    k.BoxR(new Vector3(sd * 0.92f, 1.90f, -0.11f), new Vector3(0.04f, 0.04f, dir.magnitude), Quaternion.LookRotation(dir), blk);
            }

            k.Col(-W, W, 0.38f, 1.956f, -0.30f, 1.50f);
            k.Col(-W, W, 0.38f, 1.36f, 1.50f, 2.94f);
            k.Col(-W, W, 0.42f, floorY, -2.9425f, bedF);
            k.Col(bedIn, W, floorY, railY, bedR, bedF);
            k.Col(-W, -bedIn, floorY, railY, bedR, bedF);
            k.Col(-bedIn, bedIn, floorY, railY, bedR, bedR + 0.06f);
        }

        // ================================================================== flatbed (F-450-style chassis cab, dually, 12' x 8' stake bed)

        static void Flatbed(Kit k)
        {
            const float W = 1.015f, R = 0.405f, tw = 0.225f, ri = 0.25f, HW = 1.219f;
            const float rz = -2.03f, deckY = 1.016f, z0 = -3.65f, z1 = 0f;
            int blk = k.Blk, dark = k.Dark;

            var p = new CabP
            {
                W = W, zBack = 0.10f, zBrow = 1.20f, zWsTop = 1.50f, zCowl = 2.15f, zNose = 3.53f,
                yBot = 0.50f, ySill = 1.00f, yBelt = 1.44f, yRoof = 2.03f, yCowl = 1.44f, yNose = 1.36f,
                axleZ = 2.72f, R = R, tireW = tw, rimR = ri, ra = 0.48f,
                doorRear = 0.75f, grilleY0 = 0.82f, bumpY0 = 0.50f, bumpY1 = 0.76f, bumpD = 0.12f, stripeY0 = 0.60f, stripeY1 = 0.76f,
                extended = true, chromeBumper = true, heavy = false,
            };
            ConvCab(k, p);
            k.SymBox(W - 0.02f, W + 0.13f, 0.40f, 0.45f, p.zBack + 0.05f, 2.20f, dark);     // running boards

            // chassis
            Frame(k, -3.60f, 0.20f, 0.80f, 0.22f);
            float outerX = HW - 0.02f - tw * 0.5f, innerX = outerX - 0.25f;
            RearAxle(k, rz, R, tw, ri, outerX, innerX);
            k.CylZ(0f, 0.52f, rz, 0.15f, 0.04f, 8, blk);                                    // driveshaft
            k.Box(-0.95f, -0.55f, 0.45f, 0.75f, -1.20f, -0.30f, blk);                       // aft fuel tank
            Mudflaps(k, rz - R - 0.16f, innerX - 0.13f, HW - 0.01f, 0.20f, deckY - 0.15f);

            // body
            FlatDeck(k, z0, z1, HW, deckY, 0.80f, 8);
            HeadacheRack(k, z1 - 0.045f, HW, deckY, 2.06f);
            StakeRun(k, true, HW - 0.025f, -1f, z0 + 0.06f, z1 - 0.12f, deckY, 1.0f, 3);
            StakeRun(k, true, -(HW - 0.025f), 1f, z0 + 0.06f, z1 - 0.12f, deckY, 1.0f, 3);
            StakeRun(k, false, z0 + 0.025f, 1f, -(HW - 0.07f), HW - 0.07f, deckY, 1.0f, 2);

            k.Col(-W, W, 0.40f, 2.03f, 0.10f, 2.15f);
            k.Col(-W, W, 0.40f, 1.44f, 2.15f, 3.65f);
            k.Col(-HW, HW, 0.45f, deckY, z0, z1);
            k.Col(HW - 0.07f, HW, deckY, deckY + 1.0f, z0, z1);
            k.Col(-HW, -(HW - 0.07f), deckY, deckY + 1.0f, z0, z1);
            k.Col(-HW, HW, deckY, deckY + 1.0f, z0, z0 + 0.07f);
            k.Col(-HW, HW, deckY, 2.06f, z1 - 0.08f, z1);
        }

        // ================================================================== medium-duty conventional cab (dump, boom)

        /// <summary>Class 6/7 conventional cab with its back wall at zb. Front bumper ends at zb + 3.17.</summary>
        static CabP MediumCab(Kit k, float zb)
        {
            int blk = k.Blk;
            var p = new CabP
            {
                W = 1.12f, zBack = zb, zBrow = zb + 0.70f, zWsTop = zb + 1.30f, zCowl = zb + 1.75f, zNose = zb + 3.03f,
                yBot = 0.75f, ySill = 1.22f, yBelt = 1.74f, yRoof = 2.50f, yCowl = 1.74f, yNose = 1.62f,
                axleZ = zb + 2.35f, R = 0.52f, tireW = 0.28f, rimR = 0.29f, ra = 0.60f,
                doorRear = zb + 0.12f, grilleY0 = 0.95f, bumpY0 = 0.52f, bumpY1 = 0.84f, bumpD = 0.14f, stripeY0 = 0.85f, stripeY1 = 1.0f,
                extended = false, chromeBumper = true, heavy = true,
            };
            ConvCab(k, p);

            k.Box(-0.45f, 0.45f, 0.42f, 0.75f, zb + 1.55f, zb + 2.98f, blk);                 // engine / oil pan
            k.CylX(0.52f, p.axleZ, -0.95f, 0.95f, 0.05f, 8, blk);                           // front axle beam

            // driver side: fuel tank with step; passenger side: battery box with step
            k.CylZ(-0.90f, 0.50f, zb + 0.25f, zb + 1.45f, 0.22f, 14, k.Steel);
            k.CylZ(-0.90f, 0.50f, zb + 0.45f, zb + 0.50f, 0.227f, 14, blk);
            k.CylZ(-0.90f, 0.50f, zb + 1.20f, zb + 1.25f, 0.227f, 14, blk);
            k.Box(-1.16f, -0.80f, 0.722f, 0.746f, zb + 0.30f, zb + 1.40f, k.Rim);
            k.Box(0.80f, 1.14f, 0.38f, 0.72f, zb + 0.35f, zb + 1.35f, blk);
            k.Box(0.82f, 1.16f, 0.722f, 0.746f, zb + 0.30f, zb + 1.40f, k.Rim);
            return p;
        }

        // ================================================================== dump truck (single axle, 10' / 5 yd body)

        static void Dump(Kit k)
        {
            const float zb = 0.15f, R = 0.52f, tw = 0.28f, ri = 0.29f, HW = 1.219f;
            const float rz = -2.0f, bz0 = -3.30f, bz1 = -0.25f;
            int blk = k.Blk, gray = k.Gray;

            MediumCab(k, zb);

            // chassis
            Frame(k, -3.20f, zb + 0.30f, 1.0f, 0.28f);
            float outerX = HW - 0.02f - tw * 0.5f, innerX = outerX - 0.32f;
            RearAxle(k, rz, R, tw, ri, outerX, innerX);
            k.CylZ(0f, 0.60f, rz, zb + 0.2f, 0.045f, 8, blk);
            Mudflaps(k, rz - R - 0.20f, innerX - 0.16f, HW - 0.01f, 0.22f, 1.0f);
            k.Box(-1.10f, 1.10f, 0.55f, 0.65f, -3.30f, -3.22f, blk);                         // rear underride bar
            k.SymBox(0.39f, 0.47f, 0.65f, 0.74f, -3.29f, -3.23f, blk);

            // dump body
            k.SymBox(0.38f, 0.48f, 1.0f, 1.06f, bz0 + 0.10f, bz1 - 0.05f, blk);              // body sills
            k.Box(-0.25f, 0.25f, 0.80f, 1.06f, -1.30f, -0.50f, blk);                         // hoist
            k.CylX(1.03f, -3.05f, -0.60f, 0.60f, 0.04f, 8, blk);                             // rear hinge
            k.Box(-1.15f, 1.15f, 1.06f, 1.12f, bz0, bz1, gray);                              // floor
            k.SymBox(1.09f, 1.15f, 1.12f, 1.73f, bz0, bz1, gray);                            // sides
            k.SymBox(1.06f, 1.18f, 1.70f, 1.78f, bz0, bz1, gray);                            // top rails
            k.SymBox(1.15f, 1.19f, 1.06f, 1.14f, bz0, bz1, gray);                            // rub rails
            for (int i = 0; i < 5; i++)                                                       // side braces
            {
                float z = bz0 + 0.35f + i * 0.59f;
                k.SymBox(1.15f, 1.19f, 1.14f, 1.70f, z - 0.035f, z + 0.035f, gray);
            }
            k.Box(-1.09f, 1.09f, 1.12f, 1.70f, bz1 - 0.06f, bz1, gray);                      // front bulkhead
            k.Box(-0.95f, 0.95f, 1.70f, 2.66f, bz1 - 0.06f, bz1, gray);                      // tall front wall
            k.Box(-0.95f, 0.95f, 2.60f, 2.66f, bz1, 1.25f, gray);                            // cab shield
            k.Box(-0.95f, 0.95f, 2.545f, 2.60f, 1.21f, 1.25f, gray);                         // shield lip
            var gus = new[] { new Vector2(bz1, 2.10f), new Vector2(bz1, 2.60f), new Vector2(0.45f, 2.60f) };
            k.Prof(gus, 0.91f, 0.95f, gray);
            k.Prof(gus, -0.95f, -0.91f, gray);
            k.Box(-1.09f, 1.09f, 1.13f, 1.72f, bz0 + 0.01f, bz0 + 0.06f, gray);              // tailgate
            k.Box(-1.05f, 1.05f, 1.25f, 1.32f, bz0 - 0.03f, bz0 + 0.01f, gray);
            k.Box(-1.05f, 1.05f, 1.52f, 1.59f, bz0 - 0.03f, bz0 + 0.01f, gray);
            k.SymBox(1.07f, 1.20f, 1.0f, 1.82f, bz0 - 0.04f, bz0 + 0.10f, gray);             // rear corner posts
            k.SymBox(1.09f, 1.18f, 1.08f, 1.20f, bz0 - 0.05f, bz0 - 0.04f, k.Red);
            k.SymBox(1.09f, 1.18f, 1.24f, 1.30f, bz0 - 0.05f, bz0 - 0.04f, k.Amber);
            k.Box(-0.155f, 0.155f, 0.66f, 0.81f, -3.304f, -3.30f, k.Panel);

            // exhaust stack behind the cab (passenger side)
            k.CylY(1.03f, -0.05f, 0.95f, 1.85f, 0.10f, 12, k.Steel);
            k.CylY(1.03f, -0.05f, 1.85f, 2.80f, 0.052f, 10, k.Chrome);
            k.Box(0.96f, 1.02f, 1.55f, 1.59f, -0.05f, zb + 0.01f, blk);
            k.Box(0.96f, 1.02f, 2.20f, 2.24f, -0.05f, zb + 0.01f, blk);

            k.Col(-1.12f, 1.12f, 0.40f, 2.50f, zb, zb + 1.75f);
            k.Col(-1.12f, 1.12f, 0.40f, 1.74f, zb + 1.75f, zb + 3.17f);
            k.Col(-1.19f, 1.19f, 0.50f, 1.12f, bz0, bz1);
            k.Col(1.09f, 1.19f, 1.12f, 1.78f, bz0, bz1);
            k.Col(-1.19f, -1.09f, 1.12f, 1.78f, bz0, bz1);
            k.Col(-1.15f, 1.15f, 1.12f, 1.78f, bz0 - 0.03f, bz0 + 0.06f);
            k.Col(-1.15f, 1.15f, 1.12f, 2.66f, bz1 - 0.06f, bz1);
        }

        // ================================================================== boom truck (tandem axle, 20' flatbed, stowed telescoping crane)

        static void Boom(Kit k)
        {
            const float zb = 1.85f, R = 0.52f, tw = 0.28f, ri = 0.29f, HW = 1.219f;
            const float z0 = -5.05f, z1 = 1.05f, deckY = 1.22f;
            int blk = k.Blk, gray = k.Gray, white = k.White, steel = k.Steel;

            MediumCab(k, zb);

            // chassis
            Frame(k, -5.0f, zb + 0.30f, 1.02f, 0.30f);
            float outerX = HW - 0.02f - tw * 0.5f, innerX = outerX - 0.32f;
            RearAxle(k, -2.25f, R, tw, ri, outerX, innerX);
            RearAxle(k, -3.55f, R, tw, ri, outerX, innerX);
            k.CylZ(0f, 0.60f, -2.25f, zb + 0.2f, 0.045f, 8, blk);
            Mudflaps(k, -3.55f - R - 0.13f, innerX - 0.16f, HW - 0.01f, 0.22f, deckY - 0.15f);

            FlatDeck(k, z0, z1, HW, deckY, 1.02f, 8);

            // outriggers: front pair at the pedestal, rear pair behind the tandem
            float[] oz = { 1.37f, -4.55f };
            float[] oTop = { 1.42f, 1.06f };
            for (int i = 0; i < 2; i++)
            {
                float z = oz[i];
                k.Box(-1.03f, 1.03f, 0.80f, 1.00f, z - 0.15f, z + 0.15f, gray);               // cross beam
                k.SymBox(1.03f, 1.165f, 0.48f, oTop[i], z - 0.10f, z + 0.10f, gray);          // jack housings
                k.SymBox(1.065f, 1.13f, 0.30f, 0.48f, z - 0.035f, z + 0.035f, steel);         // jack legs (retracted)
                k.CylY(1.0975f, z, 0.27f, 0.30f, 0.15f, 12, blk);                             // pads
                k.CylY(-1.0975f, z, 0.27f, 0.30f, 0.15f, 12, blk);
            }

            // crane pedestal + turret
            k.Box(-0.62f, 0.62f, 1.02f, 1.34f, 1.10f, 1.72f, gray);
            k.CylY(0f, 1.41f, 1.34f, 1.46f, 0.30f, 16, blk);                                  // slew ring
            k.Box(-0.36f, 0.36f, 1.46f, 1.62f, 1.09f, 1.73f, white);
            var plate = new[] { new Vector2(1.72f, 1.62f), new Vector2(1.08f, 1.62f), new Vector2(1.08f, 3.30f), new Vector2(1.40f, 3.30f) };
            k.Prof(plate, 0.24f, 0.30f, white);
            k.Prof(plate, -0.30f, -0.24f, white);
            k.CylX(3.12f, 1.24f, -0.32f, 0.32f, 0.05f, 10, steel);                            // boom pivot pin
            k.Box(0.62f, 0.80f, 1.34f, 1.75f, 1.20f, 1.60f, gray);                            // operator console
            k.Box(0.80f, 0.83f, 1.60f, 1.72f, 1.25f, 1.55f, blk);

            // boom stowed rearward over the bed
            k.BoxU(-0.19f, 0.19f, 2.92f, 3.34f, -4.30f, 1.50f, white, Vector3.forward);
            k.Box(-0.16f, 0.16f, 2.95f, 3.31f, -4.62f, -4.30f, white);
            k.Box(-0.13f, 0.13f, 2.98f, 3.28f, -4.86f, -4.62f, white);
            k.Box(-0.15f, 0.15f, 2.90f, 3.34f, -5.02f, -4.86f, blk);                          // boom head
            k.CylX(2.96f, -4.96f, -0.09f, 0.09f, 0.13f, 12, steel);                           // sheave
            k.Box(-0.012f, 0.012f, 2.58f, 2.86f, -4.972f, -4.948f, steel);                    // hoist line
            k.Box(-0.08f, 0.08f, 2.34f, 2.58f, -5.01f, -4.91f, k.Orange);                     // hook block
            k.Box(-0.19f, 0.19f, 3.05f, 3.21f, -3.0f, 0.6f, k.Stripe);                        // (hidden inside boom; keeps stripe material stable)
            k.SymBox(0.19f, 0.193f, 3.05f, 3.21f, -3.0f, 0.6f, k.Stripe);                     // boom stripe
            k.CylX(3.47f, 0.90f, -0.17f, 0.17f, 0.12f, 12, blk);                              // winch drum
            k.SymBox(0.17f, 0.20f, 3.34f, 3.60f, 0.76f, 1.04f, gray);
            Vector3 c0 = new Vector3(0f, 1.70f, 1.10f), c1 = new Vector3(0f, 2.92f, -0.60f);  // lift cylinder
            Vector3 cm = Vector3.Lerp(c0, c1, 0.6f);
            Rod(k, c0, cm, 0.085f, 12, gray);
            Rod(k, cm, c1, 0.045f, 10, k.Chrome);

            // boom rest
            k.SymBox(0.22f, 0.30f, deckY, 2.86f, -3.94f, -3.86f, blk);
            k.Box(-0.30f, 0.30f, 2.86f, 2.92f, -3.96f, -3.84f, blk);

            k.Col(-1.12f, 1.12f, 0.40f, 2.50f, zb, zb + 1.75f);
            k.Col(-1.12f, 1.12f, 0.40f, 1.74f, zb + 1.75f, zb + 3.17f);
            k.Col(-HW, HW, 0.50f, deckY, z0, z1);
            k.Col(-0.80f, 0.80f, 0.50f, 3.30f, 1.08f, 1.74f);
            k.Col(-0.20f, 0.20f, 2.90f, 3.40f, -5.02f, 1.50f);
            k.Col(-0.30f, 0.30f, deckY, 2.92f, -3.96f, -3.84f);
        }

        // ================================================================== box truck (NPR-style cab-over, 16' van body)

        static void BoxTruck(Kit k)
        {
            const float W = 0.99f, W2 = 0.984f, zB = 1.69f, zTop = 2.95f, zCw = 3.40f, zFp = 3.52f;
            const float R = 0.385f, tw = 0.215f, ri = 0.22f, ra = 0.45f, fz = 2.47f, rz = -1.53f, HW = 1.219f;
            const float yBot = 0.55f, ySill = 0.90f, yBelt = 1.45f, yRoof = 2.25f;
            const float bz0 = -3.437f, bz1 = 1.44f, by0 = 1.10f, by1 = 3.39f;
            int white = k.White, dark = k.Dark, blk = k.Blk, chrome = k.Chrome, panel = k.Panel, alu = k.Rim;
            float tx = W - 0.03f - tw * 0.5f, xc = tx - tw * 0.5f - 0.03f;

            // cab lower
            k.Box(-xc, xc, yBot, ySill, zB, zFp, white);
            SideSlab(k, xc, W, zB, zFp, yBot, ySill, new[] { fz }, R, ra, white);
            Liner(k, xc, fz, R, R + ra, ra);
            for (int sd = -1; sd <= 1; sd += 2) Wheel(k, new Vector3(sd * tx, R, fz), sd, R, tw, ri, true);

            // cab upper
            float[] sB = S(zB, W2, ySill, W2, yBelt, W2 - 0.02f, 2.12f, W2 - 0.11f, yRoof);
            float[] sTop = S(zTop, W2, ySill, W2, yBelt, W2 - 0.02f, 2.12f, W2 - 0.11f, 2.20f);
            float[] sCw = S(zCw, W2, ySill, W2 - 0.004f, 1.40f, W2 - 0.02f, 1.45f, W2 - 0.11f, 1.48f);
            float[] sFp = S(zFp, W2 - 0.02f, ySill, W2 - 0.022f, 1.25f, W2 - 0.03f, 1.31f, W2 - 0.11f, 1.34f);
            Loft(k, new[] { sB, sTop, sCw, sFp }, white);
            Glaze(k, sTop, sCw, 3, 0.05f, 0.95f, 0.04f, 0.96f);
            Patch(k, sCw, sFp, 3, 0.05f, 0.95f, 0.03f, 0.97f, dark, 0.004f);                 // wiper panel
            GlazeSides(k, sB, sTop, 0.30f, 0.97f, 0.08f, 0.92f);
            GlazeSides(k, sTop, sCw, 0.04f, 0.78f, 0.12f, 0.88f);
            FlatGlaze(k, -0.45f, 0.45f, 1.60f, 2.00f, zB, -1);

            // doors
            k.SymBox(W, W + 0.004f, yBot + 0.03f, ySill, 1.995f, 2.005f, dark);
            LoftSeam(k, sB, sTop, 2.0f, 0, dark);
            LoftSeam(k, sB, sTop, 2.0f, 1, dark);
            k.SymBox(W, W + 0.004f, yBot + 0.03f, ySill, 3.375f, 3.385f, dark);
            LoftSeam(k, sTop, sCw, 3.38f, 0, dark);
            k.SymBox(W2 - 0.012f, W2 + 0.02f, 1.28f, 1.315f, 2.08f, 2.24f, dark);
            k.SymBox(W, W + 0.002f, 0.92f, 1.08f, 2.02f, 3.36f, k.Stripe);
            k.SymBox(0.80f, 1.0f, 0.42f, 0.47f, 2.95f, 3.40f, dark);                         // cab steps

            // front
            k.Box(-0.55f, 0.55f, 0.98f, 1.20f, zFp - 0.005f, zFp + 0.012f, dark);
            k.Box(-0.52f, 0.52f, 1.045f, 1.07f, zFp + 0.012f, zFp + 0.022f, chrome);
            k.Box(-0.52f, 0.52f, 1.115f, 1.14f, zFp + 0.012f, zFp + 0.022f, chrome);
            k.SymBox(0.60f, 0.92f, 0.98f, 1.20f, zFp - 0.005f, zFp + 0.015f, chrome);
            int glass = k.Glass;
            FlatQuad(k, 0.608f, 0.912f, 0.988f, 1.192f, zFp + 0.021f, 1, glass);
            FlatQuad(k, -0.912f, -0.608f, 0.988f, 1.192f, zFp + 0.021f, 1, glass);
            k.SymBox(0.92f, 0.975f, 0.98f, 1.20f, zFp - 0.03f, zFp + 0.015f, k.Amber);
            k.Box(-1.0f, 1.0f, 0.50f, 0.82f, zFp - 0.06f, zFp + 0.07f, dark);
            k.SymBox(0.55f, 0.75f, 0.60f, 0.70f, zFp + 0.07f, zFp + 0.076f, chrome);
            k.Box(-0.155f, 0.155f, 0.58f, 0.73f, zFp + 0.07f, zFp + 0.074f, panel);
            int amber = k.Amber;
            for (int i = -1; i <= 1; i++) k.Box(i * 0.3f - 0.035f, i * 0.3f + 0.035f, yRoof - 0.02f, yRoof + 0.035f, 2.55f, 2.65f, amber);

            // mirrors on arms
            k.SymBox(W2 - 0.04f, W + 0.22f, 1.455f, 1.485f, 2.905f, 2.935f, blk);
            k.SymBox(W2 - 0.04f, W + 0.22f, 2.08f, 2.11f, 2.905f, 2.935f, blk);
            k.SymBox(W + 0.16f, W + 0.30f, 1.44f, 2.13f, 2.88f, 2.96f, dark);
            k.SymBox(W + 0.172f, W + 0.288f, 1.47f, 2.10f, 2.874f, 2.88f, chrome);

            // chassis
            Frame(k, -3.45f, 1.90f, 0.82f, 0.22f);
            float outerX = HW - 0.03f - tw * 0.5f, innerX = outerX - 0.24f;
            RearAxle(k, rz, R, tw, ri, outerX, innerX);
            k.CylZ(0f, 0.52f, rz, 1.5f, 0.04f, 8, blk);
            Mudflaps(k, rz - R - 0.16f, innerX - 0.13f, HW - 0.01f, 0.20f, 1.0f);
            k.Box(-0.95f, -0.52f, 0.45f, 0.78f, -0.60f, 0.50f, alu);                         // fuel tank
            k.Box(0.52f, 0.92f, 0.48f, 0.78f, 0.0f, 0.60f, blk);                             // battery box
            k.SymBox(0.36f, 0.48f, 0.82f, 1.0f, bz0 + 0.05f, bz1 - 0.05f, blk);              // body sills
            k.Box(-1.20f, 1.20f, 1.0f, by0, bz0 + 0.01f, bz1 - 0.01f, blk);                  // floor crossmembers
            k.Box(-1.15f, 1.15f, 0.55f, 0.63f, -3.59f, -3.45f, blk);                         // rear step bumper
            k.SymBox(0.36f, 0.48f, 0.63f, 0.82f, -3.55f, -3.47f, blk);

            // van body
            k.Box(-HW, HW, by0, by1, bz0, bz1, panel);
            k.SymBox(HW, HW + 0.006f, by0 - 0.02f, by0 + 0.10f, bz0, bz1, alu);              // bottom rails
            k.SymBox(HW, HW + 0.006f, by1 - 0.08f, by1 + 0.004f, bz0, bz1, alu);             // top rails
            k.SymBox(HW, HW + 0.006f, by0 + 0.10f, by1 - 0.08f, bz1 - 0.08f, bz1 + 0.006f, alu); // front corner posts
            k.SymBox(HW, HW + 0.006f, by0 + 0.10f, by1 - 0.08f, bz0 - 0.006f, bz0 + 0.10f, alu); // rear corner posts
            k.SymBox(1.14f, HW, by0, by1 - 0.08f, bz1, bz1 + 0.006f, alu);                   // front face trim
            k.Box(-HW, HW, by1 - 0.08f, by1 + 0.004f, bz1, bz1 + 0.006f, alu);
            k.SymBox(HW, HW + 0.003f, 1.55f, 1.80f, bz0 + 0.15f, bz1 - 0.15f, k.Stripe);     // company stripe
            for (int i = -2; i <= 2; i++)                                                     // clearance lights
            {
                float x = i == -2 ? -1.0f : i == 2 ? 1.0f : i * 0.15f;
                k.Box(x - 0.04f, x + 0.04f, by1 - 0.06f, by1 - 0.02f, bz1 + 0.006f, bz1 + 0.014f, amber);
                k.Box(x - 0.04f, x + 0.04f, by1 - 0.09f, by1 - 0.05f, bz0 - 0.038f, bz0 - 0.03f, k.Red);
            }

            // roll-up rear door
            k.SymBox(1.10f, HW, by0, by1 - 0.14f, bz0 - 0.03f, bz0, alu);                    // door frame posts
            k.Box(-HW, HW, by1 - 0.14f, by1, bz0 - 0.03f, bz0, alu);                         // header
            k.Box(-HW, HW, by0 - 0.10f, by0, bz0 - 0.03f, bz0 + 0.02f, alu);                 // sill
            for (int i = 1; i <= 7; i++)                                                      // door panel joints
            {
                float y = by0 + i * 0.268f;
                k.Box(-1.10f, 1.10f, y - 0.006f, y + 0.006f, bz0 - 0.003f, bz0, dark);
            }
            k.Box(-0.07f, 0.07f, by0 + 0.02f, by0 + 0.10f, bz0 - 0.02f, bz0, blk);           // latch
            k.Box(0.30f, 0.33f, by0 + 0.02f, by0 + 0.34f, bz0 - 0.006f, bz0, blk);           // pull strap
            k.SymBox(0.80f, 1.0f, by0 - 0.085f, by0 - 0.02f, bz0 - 0.037f, bz0 - 0.03f, k.Red);
            k.Box(-0.155f, 0.155f, by0 - 0.095f, by0 - 0.005f, bz0 - 0.034f, bz0 - 0.03f, panel);

            k.Col(-W, W, 0.45f, yRoof, zB, 3.59f);
            k.Col(-HW, HW, 1.0f, by1, bz0 - 0.03f, bz1);
            k.Col(-1.15f, 1.15f, 0.40f, 1.0f, -3.59f, zB);
        }

        // ================================================================== skid steer on a 16' tandem equipment trailer

        static void SkidSteer(Kit k)
        {
            const float zR = -3.06f, zD = 1.81f, deckY = 0.61f, DW = 1.041f;
            const float R = 0.39f, tw = 0.215f, ri = 0.21f;
            int blk = k.Blk, alu = k.Rim, treated = k.Treated;
            float tx = DW + 0.03f + tw * 0.5f;
            float[] ax = { -1.55f, -0.69f };

            // ---- trailer frame + deck
            k.SymBox(0.96f, DW, 0.41f, deckY, zR, zD, blk);                                   // outer rails
            k.Box(-0.96f, 0.96f, 0.50f, 0.56f, zR + 0.05f, zD - 0.05f, blk);                 // crossmembers
            k.SymBox(0.30f, 0.36f, 0.42f, 0.50f, zR + 0.05f, zD - 0.05f, blk);               // stringers
            k.Box(-0.96f, 0.96f, 0.45f, deckY, zR, zR + 0.05f, blk);                         // rear rail
            k.Box(-0.96f, 0.96f, 0.45f, deckY, zD - 0.05f, zD, blk);                         // front rail
            k.Box(-DW, DW, deckY, deckY + 0.13f, zD - 0.04f, zD, blk);                       // front bump rail
            const int boards = 11;
            float bw = 1.92f / boards;
            for (int i = 0; i < boards; i++)
            {
                float x = -0.96f + i * bw;
                k.BoxU(x + 0.003f, x + bw - 0.003f, 0.56f, deckY, zR + 0.05f, zD - 0.05f, treated, Vector3.forward);
            }
            float[] pockets = { -2.85f, -2.40f, 0.15f, 0.70f, 1.25f, 1.70f };
            for (int i = 0; i < pockets.Length; i++)
                k.SymBox(DW, DW + 0.02f, 0.49f, 0.60f, pockets[i] - 0.04f, pockets[i] + 0.04f, blk);

            // ---- running gear
            for (int a = 0; a < ax.Length; a++)
            {
                k.CylX(R, ax[a], -tx, tx, 0.04f, 8, blk);
                for (int sd = -1; sd <= 1; sd += 2) Wheel(k, new Vector3(sd * tx, R, ax[a]), sd, R, tw, ri, true);
            }
            k.SymBox(0.90f, 0.955f, 0.33f, 0.41f, -1.78f, -0.46f, blk);                      // leaf springs
            Vector3 fDir = new Vector3(0f, -0.24f, 0.32f), rDir = new Vector3(0f, 0.24f, 0.32f);
            for (int sd = -1; sd <= 1; sd += 2)                                               // fenders
            {
                k.Box(sd * 1.045f, sd * 1.295f, 0.83f, 0.85f, -1.80f, -0.44f, blk);
                k.BoxR(new Vector3(sd * 1.17f, 0.72f, -0.28f), new Vector3(0.25f, 0.02f, 0.40f), Quaternion.LookRotation(fDir), blk);
                k.BoxR(new Vector3(sd * 1.17f, 0.72f, -1.96f), new Vector3(0.25f, 0.02f, 0.40f), Quaternion.LookRotation(rDir), blk);
            }

            // ---- tongue, coupler, jack
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 a = new Vector3(sd * 0.80f, 0.48f, zD - 0.02f), b = new Vector3(sd * 0.05f, 0.48f, 2.92f);
                Vector3 d = b - a;
                k.BoxR((a + b) * 0.5f, new Vector3(0.08f, 0.13f, d.magnitude), Quaternion.LookRotation(d), blk);
            }
            k.Box(-0.34f, 0.34f, 0.44f, 0.52f, 2.44f, 2.56f, blk);
            k.Box(-0.06f, 0.06f, 0.42f, 0.55f, 2.84f, 3.0f, blk);
            k.CylY(0f, 3.005f, 0.45f, 0.55f, 0.055f, 10, alu);                               // coupler socket
            k.CylY(0f, 2.50f, 0.22f, 0.95f, 0.032f, 10, blk);                                // jack
            k.CylY(0f, 2.50f, 0.012f, 0.22f, 0.024f, 8, alu);
            k.CylY(0f, 2.50f, 0f, 0.012f, 0.09f, 12, blk);
            k.Box(-0.01f, 0.14f, 0.93f, 0.95f, 2.49f, 2.51f, alu);
            k.Box(0.12f, 0.14f, 0.86f, 0.93f, 2.49f, 2.51f, alu);

            // ---- fold-up ramps (stowed upright)
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float xcn = sd * 0.72f;
                k.Box(xcn - 0.20f, xcn - 0.16f, deckY, 2.05f, zR + 0.01f, zR + 0.07f, blk);
                k.Box(xcn + 0.16f, xcn + 0.20f, deckY, 2.05f, zR + 0.01f, zR + 0.07f, blk);
                for (int i = 0; i < 6; i++)
                {
                    float y = 0.75f + i * 0.24f;
                    k.Box(xcn - 0.16f, xcn + 0.16f, y, y + 0.04f, zR + 0.02f, zR + 0.06f, blk);
                }
            }

            // ---- lights
            k.SymBox(0.80f, 0.94f, 0.50f, 0.58f, zR - 0.008f, zR, k.Red);
            k.Box(-0.155f, 0.155f, 0.46f, 0.60f, zR - 0.004f, zR, k.Panel);
            k.SymBox(DW, DW + 0.008f, 0.50f, 0.56f, zD - 0.20f, zD - 0.10f, k.Amber);

            // ---- the machine, sitting on the deck (wheel center 0.75 m behind the trailer middle)
            const float mz = -0.75f;
            k.B.Transform = Matrix4x4.Translate(new Vector3(0f, deckY, mz));
            Loader(k);
            k.B.ResetTransform();

            k.Col(-DW, DW, 0.33f, deckY, zR, zD);
            k.Col(1.045f, 1.295f, 0f, 0.85f, -2.12f, -0.12f);
            k.Col(-1.295f, -1.045f, 0f, 0.85f, -2.12f, -0.12f);
            k.Col(-0.40f, 0.40f, 0.30f, 0.62f, zD, 3.06f);
            k.Col(-0.92f, 0.92f, deckY, 2.05f, zR, zR + 0.08f);
            k.Col(-0.87f, 0.87f, deckY, deckY + 2.065f, mz - 1.575f, mz + 1.14f);
            k.Col(-0.94f, 0.94f, deckY, deckY + 0.64f, mz + 1.14f, mz + 1.905f);
        }

        /// <summary>S650-size skid-steer loader. Local origin = ground under the wheel center, front +Z, bucket flat on the ground.</summary>
        static void Loader(Kit k)
        {
            int white = k.White, gray = k.Gray, blk = k.Blk, dark = k.Dark, orange = k.Orange, chrome = k.Chrome;
            const float R = 0.42f, tw = 0.31f;

            for (int sd = -1; sd <= 1; sd += 2)
            {
                Wheel(k, new Vector3(sd * 0.708f, R, 0.575f), sd, R, tw, 0.21f, true, orange);
                Wheel(k, new Vector3(sd * 0.708f, R, -0.575f), sd, R, tw, 0.21f, true, orange);
            }

            // chassis, fenders, counterweight
            k.Box(-0.50f, 0.50f, 0.20f, 0.92f, -1.45f, 0.85f, gray);
            k.SymBox(0.50f, 0.86f, 0.88f, 0.92f, -1.10f, 1.02f, gray);
            k.Box(-0.72f, 0.72f, 0.30f, 0.92f, -1.575f, -1.42f, gray);

            // engine cover + tailgate
            k.Box(-0.60f, 0.60f, 0.92f, 1.52f, -1.56f, -0.78f, white);
            k.Box(-0.46f, 0.46f, 1.0f, 1.44f, -1.572f, -1.56f, dark);
            k.SymBox(0.48f, 0.58f, 1.30f, 1.42f, -1.568f, -1.56f, k.Red);
            k.Box(-0.60f, 0.60f, 0.93f, 0.99f, -1.563f, -1.56f, orange);

            // lift-arm uprights
            k.SymBox(0.62f, 0.82f, 0.92f, 1.74f, -1.50f, -1.08f, white);

            // cab (ROPS)
            k.Box(-0.42f, 0.42f, 1.52f, 1.98f, -0.80f, -0.74f, white);                       // rear wall
            FlatGlaze(k, -0.34f, 0.34f, 1.58f, 1.92f, -0.80f, -1);
            k.SymBox(0.42f, 0.48f, 1.22f, 1.98f, -0.80f, -0.74f, blk);                       // rear posts
            k.SymBox(0.42f, 0.48f, 0.92f, 1.98f, 0.50f, 0.56f, blk);                         // front posts
            k.SymBox(0.44f, 0.48f, 0.92f, 1.22f, -0.78f, 0.50f, white);                      // lower side panels
            k.Box(-0.42f, 0.42f, 0.92f, 1.0f, 0.50f, 0.56f, white);                          // door sill
            k.Box(-0.50f, 0.50f, 1.98f, 2.065f, -0.84f, 0.62f, white);                       // roof
            for (int i = 0; i < 6; i++)                                                       // side screens
            {
                float z = -0.62f + i * 0.21f;
                k.SymBox(0.452f, 0.468f, 1.22f, 1.98f, z - 0.008f, z + 0.008f, blk);
            }
            k.SymBox(0.45f, 0.47f, 1.472f, 1.488f, -0.74f, 0.50f, blk);
            k.SymBox(0.45f, 0.47f, 1.732f, 1.748f, -0.74f, 0.50f, blk);
            FlatGlaze(k, -0.42f, 0.42f, 1.0f, 1.98f, 0.53f, 1);                              // front door glass
            k.Box(-0.24f, 0.24f, 1.0f, 1.12f, -0.60f, -0.12f, dark);                         // seat
            k.Box(-0.24f, 0.24f, 1.12f, 1.62f, -0.70f, -0.58f, dark);
            k.SymBox(0.28f, 0.44f, 1.99f, 2.05f, 0.62f, 0.635f, chrome);                     // work lights

            // lift arms (lowered)
            k.SymBox(0.64f, 0.80f, 1.50f, 1.68f, -1.40f, 0.82f, white);
            k.SymBox(0.80f, 0.803f, 1.55f, 1.63f, -1.20f, 0.60f, orange);
            Vector3 legDir = new Vector3(0f, -1.11f, 0.34f);
            Quaternion legRot = Quaternion.LookRotation(legDir);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                k.BoxR(new Vector3(sd * 0.72f, 1.005f, 0.93f), new Vector3(0.14f, 0.16f, legDir.magnitude), legRot, white);
                Vector3 c0 = new Vector3(sd * 0.72f, 0.98f, -0.95f), c1 = new Vector3(sd * 0.72f, 1.50f, -0.25f);
                Vector3 cm = Vector3.Lerp(c0, c1, 0.6f);
                Rod(k, c0, cm, 0.05f, 10, blk);
                Rod(k, cm, c1, 0.028f, 8, chrome);
            }
            k.CylX(0.50f, 1.06f, -0.72f, 0.72f, 0.045f, 8, white);                           // arm cross tube
            k.Box(-0.58f, 0.58f, 0.20f, 0.80f, 1.06f, 1.14f, gray);                          // quick-attach plate

            // 74" bucket, flat on the ground
            k.Box(-0.92f, 0.92f, 0f, 0.62f, 1.14f, 1.17f, gray);
            k.Box(-0.92f, 0.92f, 0.004f, 0.026f, 1.17f, 1.86f, gray);
            k.Box(-0.915f, 0.915f, 0f, 0.032f, 1.84f, 1.905f, k.Steel);
            k.Box(-0.92f, 0.92f, 0.60f, 0.625f, 1.17f, 1.26f, gray);
            var side = new[]
            {
                new Vector2(1.15f, 0f), new Vector2(1.905f, 0f), new Vector2(1.45f, 0.45f), new Vector2(1.18f, 0.62f), new Vector2(1.15f, 0.62f),
            };
            k.Prof(side, 0.92f, 0.94f, gray);
            k.Prof(side, -0.94f, -0.92f, gray);
        }
    }
}
