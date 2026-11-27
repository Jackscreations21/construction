// Residential map building models (exterior only, one merged mesh per map cell).
// Registered types:
//   "twostory"   center-hall / side-entry colonial, 2 storeys, side gable, shutters, optional portico, optional 2-car garage wing (W >= 13 m)
//   "foursquare" American foursquare, 2.5 storeys, hip roof + hipped front dormer, full-width craftsman porch, brick or lap siding
//   "cape"       1.5-storey cape cod, 12/12 side gable, two gable dormers, center door
//   "ranch2"     modern ranch with front-facing 2-car garage (16' door) inside the footprint; gable (projecting garage) or hip variant
//   "duplex"     side-by-side 2-unit, mirrored doors with stoops, 1 or 2 storeys (spec.Stories)
//   "townhomes"  row of spec.Units 2-storey townhouses, alternating siding / brick fronts, fire-wall parapets, one long gable
//   "farmhouse"  white 2-storey farmhouse, wraparound porch, standing-seam metal roof, front cross gable, rear L wing
//   "garage"     detached 24x24' 2-car garage, 16x7' overhead door facing +Z
//   "shed"       10x12' backyard shed on skids with double doors
// Frame: origin at grade, middle of the FRONT edge; +Z = street; footprint x -W/2..W/2, z -D..0; metres.
// Stoops / porches / steps project into +Z (front yard) by at most ~3.0 m (see the per-type notes in the code).
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public static class ResidentialModels
    {
        const float SH = 2.74f;        // floor-to-floor: 8'1" ceiling + 11-7/8" floor system
        const float WH = 2.46f;        // top storey: floor to top of wall plate
        const float Heel = 0.12f;      // roof top surface above the plate at the wall line
        const float RoofTh = 0.15f;    // roof plane thickness (sheathing + rafter tails, shows at rakes / eaves)
        const float WinW = 0.914f, WinH = 1.37f, Sill = 0.75f;   // 36x54" double-hung, 30" sill
        const float DoorW = 0.914f, DoorH = 2.032f;              // 3'0" x 6'8" entry door
        const float GDoorW = 4.877f, GDoorH = 2.134f;            // 16'x7' overhead garage door
        const float Tread = 0.28f, MaxRiser = 0.19f;             // 11" treads, 7-1/2" max risers
        const int Sides = MeshBuilder.AllFaces & ~MeshBuilder.FacePosY & ~MeshBuilder.FaceNegY;

        static readonly Color[] DoorColors =
        {
            new Color(0.50f, 0.10f, 0.08f), new Color(0.10f, 0.14f, 0.28f), new Color(0.07f, 0.07f, 0.07f),
            new Color(0.12f, 0.26f, 0.17f), new Color(0.92f, 0.92f, 0.90f), new Color(0.45f, 0.30f, 0.18f), new Color(0.72f, 0.58f, 0.20f),
        };
        static readonly Color[] ShutterColors =
        {
            new Color(0.07f, 0.07f, 0.07f), new Color(0.10f, 0.20f, 0.14f), new Color(0.11f, 0.15f, 0.27f),
            new Color(0.36f, 0.10f, 0.10f), new Color(0.30f, 0.38f, 0.46f), new Color(0.92f, 0.92f, 0.90f),
        };
        static readonly Color[] RoofTints =
        {
            new Color(0.55f, 0.55f, 0.55f), new Color(0.75f, 0.75f, 0.75f), new Color(0.42f, 0.40f, 0.40f),
            new Color(0.72f, 0.62f, 0.52f), new Color(0.55f, 0.58f, 0.62f),
        };
        static readonly Color[] MetalRoofColors =
        {
            new Color(0.22f, 0.23f, 0.24f), new Color(0.12f, 0.12f, 0.13f), new Color(0.70f, 0.72f, 0.73f),
            new Color(0.45f, 0.12f, 0.10f), new Color(0.16f, 0.28f, 0.20f),
        };
        static readonly Color[] BrickTints =
        {
            Color.white, new Color(0.85f, 0.72f, 0.66f), new Color(0.72f, 0.62f, 0.58f), new Color(0.95f, 0.85f, 0.75f),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            MapModels.Register("twostory", DrawTwoStory);
            MapModels.Register("foursquare", DrawFoursquare);
            MapModels.Register("cape", DrawCape);
            MapModels.Register("ranch2", DrawRanch2);
            MapModels.Register("duplex", DrawDuplex);
            MapModels.Register("townhomes", DrawTownhomes);
            MapModels.Register("farmhouse", DrawFarmhouse);
            MapModels.Register("garage", DrawGarage);
            MapModels.Register("shed", DrawShed);
        }

        // =====================================================================================================================
        // kit: per-building materials
        // =====================================================================================================================

        sealed class Kit
        {
            public MapBuild b; public MapBuildingSpec s;
            public int wall, trim, glass, dark, found, conc, roof, ridge, door, shutter, brass, black, deck, ceiling, gdoor, brick, flue;
        }

        static Kit NewKit(MapBuild b, MapBuildingSpec s, Material wallMat, Material roofMat = null, Material ridgeMat = null)
        {
            var K = b.K;
            var k = new Kit { b = b, s = s };
            k.wall = K.M(wallMat);
            k.trim = K.M(SurfaceKind.PaintedWhite, new Color(0.97f, 0.96f, 0.93f));
            k.glass = K.M(MapMaterials.Glass);
            k.dark = K.M(new Color(0.09f, 0.11f, 0.13f));
            k.found = K.M(SurfaceKind.Concrete);
            k.conc = k.found;
            if (roofMat == null)
            {
                Color t = Pick(b, RoofTints);
                roofMat = MapMaterials.Shingle(t);
                ridgeMat = MapMaterials.Shingle(Dim(t, 0.8f));
            }
            k.roof = K.M(roofMat);
            k.ridge = K.M(ridgeMat ?? roofMat);
            Color dc = b.R() < 0.3f ? s.Accent : Pick(b, DoorColors);
            k.door = K.M(SurfaceKind.PaintedSteel, dc);
            Color sc = b.R() < 0.3f ? s.Accent : Pick(b, ShutterColors);
            k.shutter = K.M(SurfaceKind.PaintedWhite, sc);
            k.brass = K.M(SurfaceKind.Brass);
            k.black = K.M(SurfaceKind.MatteBlack);
            k.deck = K.M(SurfaceKind.PaintedWhite, new Color(0.46f, 0.48f, 0.48f));
            k.ceiling = K.M(SurfaceKind.PaintedWhite, new Color(0.90f, 0.95f, 0.96f));
            k.gdoor = K.M(SurfaceKind.PaintedSteel, new Color(0.95f, 0.95f, 0.93f));
            k.brick = K.M(MapMaterials.BrickWall(new Color(0.85f, 0.75f, 0.70f)));
            k.flue = K.M(new Color(0.55f, 0.32f, 0.24f));
            return k;
        }

        static T Pick<T>(MapBuild b, T[] a) => a[b.Rng.Next(a.Length)];
        static Color Dim(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, 1f);
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        // =====================================================================================================================
        // primitives
        // =====================================================================================================================

        /// <summary>Axis-aligned box between two corners in the current drawing frame.</summary>
        static void Bx(Kit k, Vector3 a, Vector3 c, int sub, Vector3 u = default, int mask = MeshBuilder.AllFaces)
        {
            Vector3 mn = Vector3.Min(a, c), mx = Vector3.Max(a, c), sz = mx - mn;
            if (sz.x < 1e-4f || sz.y < 1e-4f || sz.z < 1e-4f) return;
            k.b.B.AddBox((mn + mx) * 0.5f, sz, Quaternion.identity, sub, u == default ? Vector3.right : u, mask);
        }

        /// <summary>A wall face: origin on the wall plane (y = 0), U along the wall, N out of the wall.
        /// Facade coordinates: s along U, y up, d out from the wall face.</summary>
        struct Fc
        {
            public Vector3 O, U, N; public int Hide;
            public Fc(Vector3 o, Vector3 u, Vector3 n, int hide) { O = o; U = u; N = n; Hide = hide; }
            public Vector3 P(float s, float y, float d) => O + U * s + Vector3.up * y + N * d;
            public static Fc Front(float cx, float z) => new Fc(new Vector3(cx, 0f, z), Vector3.right, Vector3.forward, MeshBuilder.FaceNegZ);
            /// <summary>Back wall: s = -(x - cx).</summary>
            public static Fc Back(float cx, float z) => new Fc(new Vector3(cx, 0f, z), Vector3.left, Vector3.back, MeshBuilder.FacePosZ);
            /// <summary>Right (+X) wall: s = -(z - cz).</summary>
            public static Fc Right(float x, float cz) => new Fc(new Vector3(x, 0f, cz), Vector3.back, Vector3.right, MeshBuilder.FaceNegX);
            /// <summary>Left (-X) wall: s = z - cz.</summary>
            public static Fc Left(float x, float cz) => new Fc(new Vector3(x, 0f, cz), Vector3.forward, Vector3.left, MeshBuilder.FacePosX);
        }

        /// <summary>Box on a facade (s0..s1 along the wall, y0..y1, d0..d1 out of the wall). Back face skipped unless all.</summary>
        static void FB(Kit k, Fc f, float s0, float s1, float y0, float y1, float d0, float d1, int sub, bool all = false)
            => Bx(k, f.P(s0, y0, d0), f.P(s1, y1, d1), sub, f.U, all ? MeshBuilder.AllFaces : MeshBuilder.AllFaces & ~f.Hide);

        /// <summary>Thick planar convex polygon (roof planes, canopies): top face, underside th lower, trimmed edges.</summary>
        static void Plate(Kit k, Vector3[] p, float th, int top, int under, int edge, Vector3 uHint)
        {
            var pts = new List<Vector3>(p.Length);
            foreach (var q in p)
                if (pts.Count == 0 || (pts[pts.Count - 1] - q).sqrMagnitude > 1e-8f) pts.Add(q);
            if (pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-8f) pts.RemoveAt(pts.Count - 1);
            int n = pts.Count;
            if (n < 3) return;
            Vector3 nrm = Vector3.zero, cen = Vector3.zero;
            for (int i = 0; i < n; i++) { nrm += Vector3.Cross(pts[i], pts[(i + 1) % n]); cen += pts[i]; }
            cen /= n;
            if (nrm.sqrMagnitude < 1e-12f) return;
            nrm.Normalize();
            if (nrm.y < 0f) nrm = -nrm;
            var B = k.b.B;
            B.AddPolygon(pts, top, nrm, uHint);
            var low = new Vector3[n];
            for (int i = 0; i < n; i++) low[i] = pts[i] + Vector3.down * th;
            B.AddPolygon(low, under, -nrm, uHint);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 o = (pts[i] + pts[j]) * 0.5f - cen;
                o -= nrm * Vector3.Dot(o, nrm);
                B.AddQuad(pts[i], pts[j], low[j], low[i], edge, o, pts[j] - pts[i]);
            }
        }

        // =====================================================================================================================
        // building parts
        // =====================================================================================================================

        /// <summary>Wall body on a foundation band: concrete band 0..F (2 cm proud), water-table board, walls F..top, corner boards, collider.</summary>
        static void Body(Kit k, float x0, float x1, float zb, float zf, float F, float top, bool corners,
                         int wallSub = -1, int foundSub = -1, bool waterTable = true, bool solid = true)
        {
            int w = wallSub < 0 ? k.wall : wallSub, fd = foundSub < 0 ? k.found : foundSub;
            if (F > 0.01f) Bx(k, V(x0 - 0.02f, 0f, zb - 0.02f), V(x1 + 0.02f, F, zf + 0.02f), fd, Vector3.right, MeshBuilder.AllFaces & ~MeshBuilder.FaceNegY);
            Bx(k, V(x0, F, zb), V(x1, top, zf), w, Vector3.right, Sides);
            if (waterTable && F > 0.05f) Bx(k, V(x0 - 0.03f, F, zb - 0.03f), V(x1 + 0.03f, F + 0.1f, zf + 0.03f), k.trim, Vector3.right, Sides);
            if (corners) CornerBoards(k, x0, x1, zb, zf, F, top);
            if (solid) k.b.Solid(V(x0, 0f, zb), V(x1, top, zf));
        }

        static void CornerBoards(Kit k, float x0, float x1, float zb, float zf, float F, float top)
        {
            for (int cx = 0; cx < 2; cx++)
                for (int cz = 0; cz < 2; cz++)
                {
                    float xa = cx == 0 ? x0 - 0.025f : x1 - 0.11f, xb = cx == 0 ? x0 + 0.11f : x1 + 0.025f;
                    float za = cz == 0 ? zb - 0.025f : zf - 0.11f, zz = cz == 0 ? zb + 0.11f : zf + 0.025f;
                    Bx(k, V(xa, F, za), V(xb, top, zz), k.trim, Vector3.up, Sides);
                }
        }

        /// <summary>Double-hung window: casing, head cap, sill, dark interior + glass, sash rails, optional grilles and shutters (shW = one shutter's width).</summary>
        static void Win(Kit k, Fc f, float s, float sill, float w, float h, float shW = 0f, bool grille = false, bool simple = false)
        {
            const float cw = 0.09f, cd = 0.03f;
            float s0 = s - w * 0.5f, s1 = s + w * 0.5f, top = sill + h;
            FB(k, f, s0 - cw, s0, sill, top, 0f, cd, k.trim);
            FB(k, f, s1, s1 + cw, sill, top, 0f, cd, k.trim);
            FB(k, f, s0 - cw - 0.02f, s1 + cw + 0.02f, top, top + 0.12f, 0f, cd + 0.012f, k.trim);
            FB(k, f, s0 - cw - 0.03f, s1 + cw + 0.03f, sill - 0.045f, sill, 0f, 0.065f, k.trim);
            FB(k, f, s0, s1, sill, top, 0f, 0.004f, k.dark);
            FB(k, f, s0, s1, sill, top, 0.004f, 0.008f, k.glass);
            float m = sill + h * 0.5f;
            FB(k, f, s0, s1, m - 0.03f, m + 0.03f, 0f, 0.022f, k.trim);
            if (!simple)
            {
                FB(k, f, s0, s1, sill, sill + 0.07f, 0f, 0.02f, k.trim);
                FB(k, f, s0, s1, top - 0.05f, top, 0f, 0.02f, k.trim);
            }
            if (grille)
            {
                FB(k, f, s - 0.012f, s + 0.012f, sill, top, 0.008f, 0.014f, k.trim);
                FB(k, f, s0, s1, sill + h * 0.25f - 0.012f, sill + h * 0.25f + 0.012f, 0.008f, 0.014f, k.trim);
                FB(k, f, s0, s1, sill + h * 0.75f - 0.012f, sill + h * 0.75f + 0.012f, 0.008f, 0.014f, k.trim);
            }
            if (shW > 0.2f)
            {
                float a = s0 - cw - 0.03f, c = s1 + cw + 0.03f;
                FB(k, f, a - shW, a, sill, top, 0f, 0.03f, k.shutter);
                FB(k, f, c, c + shW, sill, top, 0f, 0.03f, k.shutter);
            }
        }

        /// <summary>6' sliding patio door (two 36" panels).</summary>
        static void Slider(Kit k, Fc f, float s, float y0)
        {
            const float w = 1.83f, h = 2.03f, cw = 0.09f;
            float s0 = s - w * 0.5f, s1 = s + w * 0.5f, top = y0 + h;
            FB(k, f, s0 - cw, s0, y0, top, 0f, 0.03f, k.trim);
            FB(k, f, s1, s1 + cw, y0, top, 0f, 0.03f, k.trim);
            FB(k, f, s0 - cw - 0.02f, s1 + cw + 0.02f, top, top + 0.12f, 0f, 0.04f, k.trim);
            FB(k, f, s0, s1, y0, top, 0f, 0.004f, k.dark);
            FB(k, f, s0, s1, y0, top, 0.004f, 0.008f, k.glass);
            FB(k, f, s - 0.035f, s + 0.035f, y0, top, 0f, 0.025f, k.trim);
            FB(k, f, s0, s1, y0, y0 + 0.09f, 0f, 0.025f, k.trim);
            FB(k, f, s0, s1, top - 0.06f, top, 0f, 0.025f, k.trim);
        }

        /// <summary>Entry door: casing + crosshead, slab with raised panels or lites, knob, porch light.
        /// style 0 six-panel, 1 craftsman (3 lites), 2 half-lite.</summary>
        static void Door(Kit k, Fc f, float s, float y0, int style, bool sidelites = false, bool transom = false, bool hingeLeft = true)
        {
            const float w = DoorW, h = DoorH, cw = 0.1f;
            float sl = sidelites ? 0.38f : 0f, tr = transom ? 0.36f : 0f;
            float s0 = s - w * 0.5f - sl, s1 = s + w * 0.5f + sl, top = y0 + h + tr;
            float d0 = s - w * 0.5f, d1 = s + w * 0.5f;
            // casing + crosshead
            FB(k, f, s0 - cw, s0, y0, top, 0f, 0.035f, k.trim);
            FB(k, f, s1, s1 + cw, y0, top, 0f, 0.035f, k.trim);
            FB(k, f, s0 - cw - 0.04f, s1 + cw + 0.04f, top, top + 0.16f, 0f, 0.05f, k.trim);
            FB(k, f, s0 - cw - 0.07f, s1 + cw + 0.07f, top + 0.16f, top + 0.21f, 0f, 0.08f, k.trim);
            // slab
            FB(k, f, d0, d1, y0, y0 + h, 0f, 0.012f, k.door);
            float pw = (w - 0.32f) * 0.5f;
            switch (style)
            {
                case 1: // craftsman: 3 lites over 2 tall panels, dentil shelf
                    {
                        float lw = (w - 0.3f) / 3f;
                        for (int i = 0; i < 3; i++)
                        {
                            float a = d0 + 0.11f + i * (lw + 0.04f);
                            FB(k, f, a, a + lw, y0 + 1.5f, y0 + 1.9f, 0.012f, 0.014f, k.dark);
                            FB(k, f, a, a + lw, y0 + 1.5f, y0 + 1.9f, 0.014f, 0.017f, k.glass);
                        }
                        FB(k, f, d0 + 0.05f, d1 - 0.05f, y0 + 1.40f, y0 + 1.46f, 0.012f, 0.05f, k.door);
                        for (int c = 0; c < 2; c++)
                        {
                            float a = d0 + 0.11f + c * (pw + 0.1f);
                            FB(k, f, a, a + pw, y0 + 0.15f, y0 + 1.30f, 0.012f, 0.022f, k.door);
                        }
                        break;
                    }
                case 2: // half-lite
                    {
                        FB(k, f, d0 + 0.12f, d1 - 0.12f, y0 + 1.05f, y0 + 1.9f, 0.012f, 0.014f, k.dark);
                        FB(k, f, d0 + 0.12f, d1 - 0.12f, y0 + 1.05f, y0 + 1.9f, 0.014f, 0.017f, k.glass);
                        for (int c = 0; c < 2; c++)
                        {
                            float a = d0 + 0.11f + c * (pw + 0.1f);
                            FB(k, f, a, a + pw, y0 + 0.15f, y0 + 0.92f, 0.012f, 0.022f, k.door);
                        }
                        break;
                    }
                default: // six-panel
                    {
                        float[] r0 = { 0.12f, 0.92f, 1.66f }, r1 = { 0.80f, 1.56f, 1.92f };
                        for (int c = 0; c < 2; c++)
                            for (int r = 0; r < 3; r++)
                            {
                                float a = d0 + 0.11f + c * (pw + 0.1f);
                                FB(k, f, a, a + pw, y0 + r0[r], y0 + r1[r], 0.012f, 0.022f, k.door);
                            }
                        break;
                    }
            }
            if (sidelites)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    float a = sg < 0 ? s0 : d1 + 0.05f, c = sg < 0 ? d0 - 0.05f : s1;
                    FB(k, f, sg < 0 ? d0 - 0.05f : d1, sg < 0 ? d0 : d1 + 0.05f, y0, y0 + h, 0f, 0.03f, k.trim);
                    FB(k, f, a, c, y0, y0 + 0.3f, 0f, 0.015f, k.door);
                    FB(k, f, a, c, y0 + 0.3f, y0 + h, 0f, 0.004f, k.dark);
                    FB(k, f, a, c, y0 + 0.3f, y0 + h, 0.004f, 0.008f, k.glass);
                }
            if (transom)
            {
                FB(k, f, s0, s1, y0 + h, y0 + h + 0.06f, 0f, 0.03f, k.trim);
                FB(k, f, s0, s1, y0 + h + 0.06f, top, 0f, 0.004f, k.dark);
                FB(k, f, s0, s1, y0 + h + 0.06f, top, 0.004f, 0.008f, k.glass);
            }
            float kx = hingeLeft ? d1 - 0.08f : d0 + 0.08f;
            FB(k, f, kx - 0.03f, kx + 0.03f, y0 + 0.86f, y0 + 1.0f, 0.012f, 0.075f, k.brass);
            float lx = hingeLeft ? s1 + cw + 0.2f : s0 - cw - 0.2f;
            FB(k, f, lx - 0.07f, lx + 0.07f, y0 + 1.65f, y0 + 1.95f, 0f, 0.14f, k.black);
        }

        /// <summary>Sectional overhead door (4 sections, raised short panels, optional top-row windows) with jamb trim.</summary>
        static void GarageDoor(Kit k, Fc f, float s, float y0, float w, float h, bool windows)
        {
            const float cw = 0.14f;
            float s0 = s - w * 0.5f, s1 = s + w * 0.5f, top = y0 + h;
            FB(k, f, s0 - cw, s0, y0, top, 0f, 0.04f, k.trim);
            FB(k, f, s1, s1 + cw, y0, top, 0f, 0.04f, k.trim);
            FB(k, f, s0 - cw, s1 + cw, top, top + cw, 0f, 0.04f, k.trim);
            FB(k, f, s0, s1, y0, top, 0f, 0.015f, k.gdoor);
            const int sec = 4;
            float sh = h / sec;
            int cols = w > 3.5f ? 8 : 4;
            const float gap = 0.07f;
            float pw = (w - gap * (cols + 1)) / cols;
            for (int r = 0; r < sec; r++)
            {
                float ya = y0 + r * sh;
                if (r > 0) FB(k, f, s0, s1, ya - 0.006f, ya + 0.006f, 0.015f, 0.018f, k.dark);
                for (int c = 0; c < cols; c++)
                {
                    float px = s0 + gap + c * (pw + gap);
                    if (windows && r == sec - 1)
                    {
                        FB(k, f, px, px + pw, ya + 0.09f, ya + sh - 0.09f, 0.015f, 0.018f, k.dark);
                        FB(k, f, px, px + pw, ya + 0.09f, ya + sh - 0.09f, 0.018f, 0.021f, k.glass);
                    }
                    else FB(k, f, px, px + pw, ya + 0.08f, ya + sh - 0.08f, 0.015f, 0.026f, k.gdoor);
                }
            }
        }

        /// <summary>Steps down from topY to grade in front of a facade, starting d0 out. Returns how far they reach (d).</summary>
        static float Steps(Kit k, Fc f, float s, float topY, float w, float d0, int sub)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(topY / MaxRiser));
            float rh = topY / n;
            for (int i = 1; i < n; i++)
                FB(k, f, s - w * 0.5f, s + w * 0.5f, 0f, topY - i * rh, d0, d0 + i * Tread, sub, true);
            return d0 + (n - 1) * Tread;
        }

        /// <summary>Concrete stoop: landing just under the threshold (F) + steps. Returns its projection from the wall.</summary>
        static float Stoop(Kit k, Fc f, float s, float F, float w, float depth, int sub)
        {
            float top = Mathf.Max(0.02f, F - 0.03f);
            FB(k, f, s - w * 0.5f, s + w * 0.5f, 0f, top, 0f, depth, sub, true);
            return Steps(k, f, s, top, w, depth, sub);
        }

        /// <summary>Shed-roof door canopy with two brackets; y = top at the wall.</summary>
        static void Hood(Kit k, Fc f, float s, float y, float w, float proj)
        {
            var pts = new[] { f.P(s - w * 0.5f, y, 0f), f.P(s + w * 0.5f, y, 0f), f.P(s + w * 0.5f, y - proj * 0.3f, proj), f.P(s - w * 0.5f, y - proj * 0.3f, proj) };
            Plate(k, pts, 0.1f, k.roof, k.trim, k.trim, f.U);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                float a = s + sg * (w * 0.5f - 0.1f);
                FB(k, f, a - 0.03f, a + 0.03f, y - 0.42f, y - 0.1f, 0f, proj * 0.7f, k.trim);
            }
        }

        static void Chimney(Kit k, float x, float z, float wx, float wz, float y0, float y1)
        {
            Bx(k, V(x - wx * 0.5f, y0, z - wz * 0.5f), V(x + wx * 0.5f, y1, z + wz * 0.5f), k.brick, Vector3.right, MeshBuilder.AllFaces & ~MeshBuilder.FaceNegY);
            Bx(k, V(x - wx * 0.5f - 0.05f, y1, z - wz * 0.5f - 0.05f), V(x + wx * 0.5f + 0.05f, y1 + 0.08f, z + wz * 0.5f + 0.05f), k.conc);
            Bx(k, V(x - 0.12f, y1 + 0.08f, z - 0.12f), V(x + 0.12f, y1 + 0.3f, z + 0.12f), k.flue);
        }

        static void Downspout(Kit k, float x, float zWall, int sgn, float eY, float zE)
        {
            float a = zWall + sgn * 0.02f, c = zWall + sgn * 0.09f;
            Bx(k, V(x - 0.04f, 0.1f, a), V(x + 0.04f, eY - 0.32f, c), k.trim);
            Bx(k, V(x - 0.04f, eY - 0.32f, a), V(x + 0.04f, eY - 0.16f, zE + sgn * 0.1f), k.trim);
            Bx(k, V(x - 0.04f, 0.02f, a), V(x + 0.04f, 0.1f, zWall + sgn * 0.45f), k.trim);
        }

        struct RoofInfo { public float eaveY, ridgeY; }

        /// <summary>Gable roof with the ridge along X over walls x0..x1, z zb..zf (current frame). Shingled planes with trimmed
        /// rakes, ridge cap, fascia, K-style gutters + downspouts, siding gable triangles (endL = x0 end, endR = x1 end), vents.</summary>
        static RoofInfo Gable(Kit k, float x0, float x1, float zf, float zb, float plate, float pitch, float eave, float rake,
                              bool endL = true, bool endR = true, bool gutters = true, int gableSub = -1, bool vent = true,
                              int roofSub = -1, int ridgeSub = -1)
        {
            int roof = roofSub < 0 ? k.roof : roofSub, ridge = ridgeSub < 0 ? k.ridge : ridgeSub, gs = gableSub < 0 ? k.wall : gableSub;
            float zc = (zf + zb) * 0.5f, half = (zf - zb) * 0.5f;
            float eY = plate + Heel - eave * pitch, rY = plate + Heel + half * pitch;
            float ex0 = x0 - rake, ex1 = x1 + rake, zF = zf + eave, zB = zb - eave;
            Plate(k, new[] { V(ex0, eY, zF), V(ex1, eY, zF), V(ex1, rY, zc), V(ex0, rY, zc) }, RoofTh, roof, k.trim, k.trim, Vector3.right);
            Plate(k, new[] { V(ex1, eY, zB), V(ex0, eY, zB), V(ex0, rY, zc), V(ex1, rY, zc) }, RoofTh, roof, k.trim, k.trim, Vector3.right);
            Bx(k, V(ex0, rY - 0.10f, zc - 0.12f), V(ex1, rY + 0.035f, zc + 0.12f), ridge);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                float zE = sg > 0 ? zF : zB, zW = sg > 0 ? zf : zb;
                Bx(k, V(ex0, eY - 0.2f, zE), V(ex1, eY + 0.005f, zE + sg * 0.025f), k.trim);
                if (!gutters) continue;
                Bx(k, V(ex0, eY - 0.17f, zE + sg * 0.025f), V(ex1, eY - 0.04f, zE + sg * 0.15f), k.trim);
                Downspout(k, sg > 0 ? x1 - 0.15f : x0 + 0.15f, zW, sg, eY, zE);
                if (x1 - x0 > 14f) Downspout(k, sg > 0 ? x0 + 0.15f : x1 - 0.15f, zW, sg, eY, zE);
            }
            for (int e = 0; e < 2; e++)
            {
                if (e == 0 ? !endL : !endR) continue;
                float x = e == 0 ? x0 : x1;
                Vector3 n = e == 0 ? Vector3.left : Vector3.right;
                k.b.B.AddPolygon(new[] { V(x, plate, zb), V(x, plate + Heel - 0.03f, zb), V(x, rY - 0.05f, zc), V(x, plate + Heel - 0.03f, zf), V(x, plate, zf) },
                                 gs, n, Vector3.forward);
                if (vent && rY - plate > 1.3f)
                {
                    var f = e == 0 ? Fc.Left(x, zc) : Fc.Right(x, zc);
                    float vy = plate + (rY - plate) * 0.5f;
                    FB(k, f, -0.3f, 0.3f, vy - 0.38f, vy + 0.38f, 0f, 0.03f, k.trim);
                    FB(k, f, -0.22f, 0.22f, vy - 0.3f, vy + 0.3f, 0.03f, 0.035f, k.dark);
                    for (int i = 1; i < 6; i++)
                        FB(k, f, -0.22f, 0.22f, vy - 0.3f + i * 0.1f - 0.01f, vy - 0.3f + i * 0.1f + 0.01f, 0.03f, 0.05f, k.trim);
                }
            }
            return new RoofInfo { eaveY = eY, ridgeY = rY };
        }

        /// <summary>Gable roof with the ridge along Z (front-facing gable) centred on x = cx, walls x cx±halfW, z zBack..zFront.</summary>
        static RoofInfo GableZ(Kit k, float cx, float halfW, float zFront, float zBack, float plate, float pitch, float eave, float rake,
                               bool endFront, bool endBack, bool gutters, int gableSub = -1, bool vent = true, int roofSub = -1, int ridgeSub = -1)
        {
            float cz = (zFront + zBack) * 0.5f, L = zFront - zBack;
            // local +X -> building -Z (so local x0 = front end), local +Z -> building +X
            k.b.BeginLocal(V(cx, 0f, cz), Quaternion.Euler(0f, 90f, 0f));
            var r = Gable(k, -L * 0.5f, L * 0.5f, halfW, -halfW, plate, pitch, eave, rake, endFront, endBack, gutters, gableSub, vent, roofSub, ridgeSub);
            k.b.EndLocal();
            return r;
        }

        static void HipCap(Kit k, Vector3 a, Vector3 c, int sub)
        {
            Vector3 d = c - a;
            float len = d.magnitude;
            if (len < 0.1f) return;
            k.b.B.AddBox((a + c) * 0.5f + Vector3.up * 0.02f, new Vector3(0.22f, 0.06f, len), Quaternion.LookRotation(d / len), sub, Vector3.forward);
        }

        /// <summary>Hip roof over walls x0..x1, z zb..zf (current frame): 4 planes, ridge + hip caps, fascia, gutters.</summary>
        static RoofInfo Hip(Kit k, float x0, float x1, float zf, float zb, float plate, float pitch, float eave, bool gutters = true, int roofSub = -1)
        {
            int roof = roofSub < 0 ? k.roof : roofSub;
            float ex0 = x0 - eave, ex1 = x1 + eave, zF = zf + eave, zB = zb - eave;
            float eY = plate + Heel - eave * pitch;
            float lx = ex1 - ex0, lz = zF - zB, xc = (ex0 + ex1) * 0.5f, zc = (zF + zB) * 0.5f;
            Vector3 A = V(ex0, eY, zF), Bq = V(ex1, eY, zF), C = V(ex1, eY, zB), D = V(ex0, eY, zB);
            Vector3 r0, r1; float rY;
            if (lx >= lz)
            {
                float r = lz * 0.5f; rY = eY + r * pitch;
                r0 = V(ex0 + r, rY, zc); r1 = V(ex1 - r, rY, zc);
                Plate(k, new[] { A, Bq, r1, r0 }, RoofTh, roof, k.trim, k.trim, Vector3.right);
                Plate(k, new[] { C, D, r0, r1 }, RoofTh, roof, k.trim, k.trim, Vector3.right);
                Plate(k, new[] { D, A, r0 }, RoofTh, roof, k.trim, k.trim, Vector3.forward);
                Plate(k, new[] { Bq, C, r1 }, RoofTh, roof, k.trim, k.trim, Vector3.forward);
                HipCap(k, A, r0, k.ridge); HipCap(k, D, r0, k.ridge); HipCap(k, Bq, r1, k.ridge); HipCap(k, C, r1, k.ridge);
            }
            else
            {
                float r = lx * 0.5f; rY = eY + r * pitch;
                r0 = V(xc, rY, zF - r); r1 = V(xc, rY, zB + r);
                Plate(k, new[] { A, Bq, r0 }, RoofTh, roof, k.trim, k.trim, Vector3.right);
                Plate(k, new[] { Bq, C, r1, r0 }, RoofTh, roof, k.trim, k.trim, Vector3.forward);
                Plate(k, new[] { C, D, r1 }, RoofTh, roof, k.trim, k.trim, Vector3.right);
                Plate(k, new[] { D, A, r0, r1 }, RoofTh, roof, k.trim, k.trim, Vector3.forward);
                HipCap(k, A, r0, k.ridge); HipCap(k, Bq, r0, k.ridge); HipCap(k, C, r1, k.ridge); HipCap(k, D, r1, k.ridge);
            }
            if ((r1 - r0).sqrMagnitude > 0.01f) HipCap(k, r0, r1, k.ridge);
            // fascia
            Bx(k, V(ex0, eY - 0.2f, zF), V(ex1, eY + 0.005f, zF + 0.025f), k.trim);
            Bx(k, V(ex0, eY - 0.2f, zB - 0.025f), V(ex1, eY + 0.005f, zB), k.trim);
            Bx(k, V(ex0 - 0.025f, eY - 0.2f, zB), V(ex0, eY + 0.005f, zF), k.trim);
            Bx(k, V(ex1, eY - 0.2f, zB), V(ex1 + 0.025f, eY + 0.005f, zF), k.trim);
            if (gutters)
            {
                Bx(k, V(ex0 - 0.15f, eY - 0.17f, zF + 0.025f), V(ex1 + 0.15f, eY - 0.04f, zF + 0.15f), k.trim);
                Bx(k, V(ex0 - 0.15f, eY - 0.17f, zB - 0.15f), V(ex1 + 0.15f, eY - 0.04f, zB - 0.025f), k.trim);
                Bx(k, V(ex0 - 0.15f, eY - 0.17f, zB), V(ex0 - 0.025f, eY - 0.04f, zF), k.trim);
                Bx(k, V(ex1 + 0.025f, eY - 0.17f, zB), V(ex1 + 0.15f, eY - 0.04f, zF), k.trim);
                Downspout(k, x1 - 0.15f, zf, 1, eY, zF);
                Downspout(k, x0 + 0.15f, zb, -1, eY, zB);
            }
            return new RoofInfo { eaveY = eY, ridgeY = rY };
        }

        /// <summary>Gable (or hipped) dormer on the front slope of a ridge-along-X roof whose wall line is zWall.</summary>
        static void Dormer(Kit k, float cx, float zWall, float plate, float pitch, float zFace, float dw, int nWin, float winW, float winH, float dPitch, bool hip)
        {
            float yb = plate + Heel + (zWall - zFace) * pitch;      // main roof surface at the dormer face
            float sill = yb + 0.25f;
            float dPlate = sill + winH + 0.22f;
            float dRise = dw * 0.5f * dPitch;
            float zMeet = zWall - (dPlate + 0.05f - plate - Heel) / pitch;
            float zEnd = zWall - (dPlate + Heel + dRise + 0.15f - plate - Heel) / pitch;
            Bx(k, V(cx - dw * 0.5f, yb - 0.5f, zMeet), V(cx + dw * 0.5f, dPlate, zFace), k.wall, Vector3.right, Sides);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                float e = cx + sg * dw * 0.5f;
                Bx(k, V(e - sg * 0.1f, yb - 0.05f, zFace - 0.1f), V(e + sg * 0.025f, dPlate, zFace + 0.025f), k.trim, Vector3.up, Sides);
            }
            var f = Fc.Front(cx, zFace);
            float pitchW = winW + 0.2f;
            for (int i = 0; i < nWin; i++)
                Win(k, f, (i - (nWin - 1) * 0.5f) * pitchW, sill, winW, winH, 0f, true, true);
            float L = zFace - zEnd;
            k.b.BeginLocal(V(cx, 0f, (zFace + zEnd) * 0.5f), Quaternion.Euler(0f, 90f, 0f));
            if (!hip) Gable(k, -L * 0.5f, L * 0.5f, dw * 0.5f, -dw * 0.5f, dPlate, dPitch, 0.15f, 0.2f, true, false, false, -1, false);
            else Hip(k, -L * 0.5f, L * 0.5f, dw * 0.5f, -dw * 0.5f, dPlate, dPitch, 0.2f, false);
            k.b.EndLocal();
        }

        /// <summary>Porch column from yb (floor) to yt (beam). style 0 square post, 1 round Tuscan, 2 craftsman tapered on a brick pier.</summary>
        static void Column(Kit k, float x, float z, float yb, float yt, int style, int pierSub)
        {
            switch (style)
            {
                case 1:
                    {
                        Bx(k, V(x - 0.16f, yb, z - 0.16f), V(x + 0.16f, yb + 0.18f, z + 0.16f), k.trim);
                        float a = yb + 0.18f, c = yt - 0.14f;
                        k.b.B.AddFrustum(V(x, (a + c) * 0.5f, z), Quaternion.identity, 0.12f, 0.1f, c - a, 10, k.trim, false);
                        Bx(k, V(x - 0.15f, c, z - 0.15f), V(x + 0.15f, yt, z + 0.15f), k.trim);
                        break;
                    }
                case 2:
                    {
                        float yp = yb + 0.95f;
                        Bx(k, V(x - 0.24f, 0f, z - 0.24f), V(x + 0.24f, yp, z + 0.24f), pierSub, Vector3.right, MeshBuilder.AllFaces & ~MeshBuilder.FaceNegY);
                        Bx(k, V(x - 0.27f, yp, z - 0.27f), V(x + 0.27f, yp + 0.06f, z + 0.27f), k.conc);
                        float a = yp + 0.06f, c = yt - 0.12f;
                        k.b.B.AddFrustum(V(x, (a + c) * 0.5f, z), Quaternion.Euler(0f, 45f, 0f), 0.2f * 1.4142f, 0.12f * 1.4142f, c - a, 4, k.trim, false);
                        Bx(k, V(x - 0.17f, c, z - 0.17f), V(x + 0.17f, yt, z + 0.17f), k.trim);
                        break;
                    }
                default:
                    Bx(k, V(x - 0.11f, yb, z - 0.11f), V(x + 0.11f, yb + 0.25f, z + 0.11f), k.trim);
                    Bx(k, V(x - 0.075f, yb + 0.25f, z - 0.075f), V(x + 0.075f, yt - 0.12f, z + 0.075f), k.trim, Vector3.up, Sides);
                    Bx(k, V(x - 0.11f, yt - 0.12f, z - 0.11f), V(x + 0.11f, yt, z + 0.11f), k.trim);
                    break;
            }
        }

        /// <summary>36" porch railing (top + bottom rail, 2x2 balusters ~6" o.c.) along an axis-aligned run a..c at floor F.</summary>
        static void Rail(Kit k, Vector3 a, Vector3 c, float F)
        {
            Vector3 d = c - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.3f) return;
            d /= len;
            bool alongX = Mathf.Abs(d.x) > Mathf.Abs(d.z);
            Vector3 p = alongX ? V(0f, 0f, 0.035f) : V(0.035f, 0f, 0f);
            Bx(k, V(a.x, F + 0.84f, a.z) - p, V(c.x, F + 0.92f, c.z) + p, k.trim);
            Bx(k, V(a.x, F + 0.06f, a.z) - p * 0.8f, V(c.x, F + 0.12f, c.z) + p * 0.8f, k.trim);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 0.15f));
            for (int i = 0; i < n; i++)
            {
                Vector3 q = a + d * (len * (i + 0.5f) / n);
                Bx(k, V(q.x - 0.019f, F + 0.12f, q.z - 0.019f), V(q.x + 0.019f, F + 0.84f, q.z + 0.019f), k.trim, Vector3.up, Sides);
            }
        }

        /// <summary>Column x positions: corners, flanking the stairs, gaps subdivided to at most maxGap.</summary>
        static List<float> ColumnLayout(float x0, float x1, float stairX, float stairW, float maxGap)
        {
            var l = new List<float> { x0 + 0.2f, x1 - 0.2f };
            float sa = stairX - stairW * 0.5f - 0.2f, sb = stairX + stairW * 0.5f + 0.2f;
            if (sa - l[0] > 1.0f) l.Add(sa);
            if (l[1] - sb > 1.0f) l.Add(sb);
            l.Sort();
            var o = new List<float>();
            for (int i = 0; i < l.Count; i++)
            {
                o.Add(l[i]);
                if (i == l.Count - 1) break;
                float gap = l[i + 1] - l[i];
                if (!(l[i] < stairX && l[i + 1] > stairX))   // never put a column in the stair opening
                {
                    int n = Mathf.FloorToInt(gap / maxGap);
                    for (int j = 1; j <= n; j++) o.Add(l[i] + gap * j / (n + 1));
                }
            }
            return o;
        }

        /// <summary>Full-width front porch on a wall at zWall: deck, skirt, steps at stairX, columns, beams, ceiling, railings,
        /// hipped shed roof (pitch) with fascia + gutter. Projects depth + steps + ~0.3 m eave beyond zWall.</summary>
        static void Porch(Kit k, float x0, float x1, float zWall, float depth, float F, float beamY, float pitch,
                          List<float> cols, float stairX, float stairW, int style, bool rail, int roofSub, int pierSub)
        {
            float zE = zWall + depth, zc = zE - 0.17f;
            Bx(k, V(x0, F - 0.2f, zWall), V(x1, F, zE), k.deck);
            Bx(k, V(x0 - 0.02f, F - 0.25f, zE - 0.03f), V(x1 + 0.02f, F - 0.02f, zE + 0.01f), k.trim);
            Bx(k, V(x0 + 0.04f, 0f, zWall), V(x1 - 0.04f, F - 0.2f, zE - 0.06f), pierSub, Vector3.right, Sides);
            Steps(k, Fc.Front(stairX, zE), 0f, F, stairW, 0f, k.conc);
            foreach (float cx in cols) Column(k, cx, zc, F, beamY, style, pierSub);
            Bx(k, V(x0, beamY, zc - 0.1f), V(x1, beamY + 0.28f, zc + 0.1f), k.trim);
            Bx(k, V(x0 + 0.1f, beamY, zWall), V(x0 + 0.3f, beamY + 0.28f, zc), k.trim);
            Bx(k, V(x1 - 0.3f, beamY, zWall), V(x1 - 0.1f, beamY + 0.28f, zc), k.trim);
            float yc = beamY + 0.27f;
            k.b.B.AddQuad(V(x0, yc, zWall), V(x1, yc, zWall), V(x1, yc, zc), V(x0, yc, zc), k.ceiling, Vector3.down);
            if (rail)
            {
                float inset = style == 2 ? 0.26f : 0.13f;
                for (int i = 0; i + 1 < cols.Count; i++)
                {
                    float a = cols[i], c = cols[i + 1];
                    if (a < stairX && c > stairX) continue;
                    Rail(k, V(a + inset, F, zc), V(c - inset, F, zc), F);
                }
                Rail(k, V(x0 + 0.2f, F, zWall + 0.05f), V(x0 + 0.2f, F, zc - inset), F);
                Rail(k, V(x1 - 0.2f, F, zWall + 0.05f), V(x1 - 0.2f, F, zc - inset), F);
            }
            const float o = 0.3f;
            float yE = beamY + 0.28f + RoofTh, yLow = yE - o * pitch, yWall = yE + depth * pitch, zR = zE + o, run = depth + o;
            float xa = x0 - o, xb = x1 + o;
            Plate(k, new[] { V(xa, yLow, zR), V(xb, yLow, zR), V(xb - run, yWall, zWall), V(xa + run, yWall, zWall) }, RoofTh, roofSub, k.trim, k.trim, Vector3.right);
            Plate(k, new[] { V(xa, yLow, zWall), V(xa, yLow, zR), V(xa + run, yWall, zWall) }, RoofTh, roofSub, k.trim, k.trim, Vector3.forward);
            Plate(k, new[] { V(xb, yLow, zR), V(xb, yLow, zWall), V(xb - run, yWall, zWall) }, RoofTh, roofSub, k.trim, k.trim, Vector3.forward);
            HipCap(k, V(xa, yLow, zR), V(xa + run, yWall, zWall), k.ridge);
            HipCap(k, V(xb, yLow, zR), V(xb - run, yWall, zWall), k.ridge);
            Bx(k, V(xa, yLow - 0.2f, zR), V(xb, yLow + 0.005f, zR + 0.025f), k.trim);
            Bx(k, V(xa - 0.025f, yLow - 0.2f, zWall), V(xa, yLow + 0.005f, zR), k.trim);
            Bx(k, V(xb, yLow - 0.2f, zWall), V(xb + 0.025f, yLow + 0.005f, zR), k.trim);
            Bx(k, V(xa, yLow - 0.17f, zR + 0.025f), V(xb, yLow - 0.04f, zR + 0.15f), k.trim);
        }

        // =====================================================================================================================
        // types
        // =====================================================================================================================

        /// <summary>Colonial. Portico/stoop + steps project up to ~2.5 m in +Z.</summary>
        static void DrawTwoStory(MapBuild b, MapBuildingSpec s)
        {
            float W = Mathf.Max(7.5f, s.Width), D = Mathf.Max(7f, s.Depth), hw = W * 0.5f;
            var k = NewKit(b, s, MapMaterials.Siding(s.Color));
            float F = b.R(0.45f, 0.65f);
            float plate = F + SH + WH;
            float pitch = Pick(b, new[] { 7f, 8f, 9f }) / 12f;
            bool garage = W >= 13f && b.R() < 0.75f;
            int gSide = b.R() < 0.5f ? -1 : 1;
            float gw = garage ? Mathf.Clamp(W * 0.4f, 5.6f, 7.3f) : 0f;
            float mx0 = -hw, mx1 = hw;
            if (garage) { if (gSide < 0) mx0 += gw; else mx1 -= gw; }
            float mw = mx1 - mx0, mc = (mx0 + mx1) * 0.5f;

            Body(k, mx0, mx1, -D, 0f, F, plate, true);

            int bays = mw >= 10f ? 5 : 3;
            float bp = (mw - 0.8f) / bays;
            float shW = b.R() < 0.75f ? Mathf.Min(WinW * 0.5f, (bp - WinW - 0.28f) * 0.5f - 0.02f) : 0f;
            int doorBay = bays / 2;
            if (bays == 3 && b.R() < 0.4f) doorBay = garage ? (gSide < 0 ? 0 : 2) : (b.R() < 0.5f ? 0 : 2);
            var front = Fc.Front(0f, 0f);
            float doorX = 0f;
            for (int i = 0; i < bays; i++)
            {
                float x = mx0 + 0.4f + (i + 0.5f) * bp;
                if (i == doorBay) doorX = x;
                else Win(k, front, x, F + Sill, WinW, WinH, shW, true);
                Win(k, front, x, F + SH + Sill, WinW, WinH, shW, true);
            }
            bool portico = b.R() < 0.5f;
            bool lites = !portico && doorBay == bays / 2 && bp > 2.1f && b.R() < 0.6f;
            Door(k, front, doorX, F, lites ? 0 : Pick(b, new[] { 0, 0, 1, 2 }), lites, !portico && !lites && b.R() < 0.4f);
            if (portico)
            {
                const float pw = 2.6f, pd = 1.5f;
                Stoop(k, front, doorX, F, pw, pd, k.conc);
                float yb = F + 2.35f;
                for (int sg = -1; sg <= 1; sg += 2) Column(k, doorX + sg * (pw * 0.5f - 0.2f), pd - 0.2f, F - 0.03f, yb, 1, k.conc);
                Bx(k, V(doorX - pw * 0.5f, yb, pd - 0.35f), V(doorX + pw * 0.5f, yb + 0.25f, pd - 0.05f), k.trim);
                Bx(k, V(doorX - pw * 0.5f, yb, 0f), V(doorX - pw * 0.5f + 0.15f, yb + 0.25f, pd - 0.05f), k.trim);
                Bx(k, V(doorX + pw * 0.5f - 0.15f, yb, 0f), V(doorX + pw * 0.5f, yb + 0.25f, pd - 0.05f), k.trim);
                k.b.B.AddQuad(V(doorX - pw * 0.5f, yb + 0.24f, 0f), V(doorX + pw * 0.5f, yb + 0.24f, 0f), V(doorX + pw * 0.5f, yb + 0.24f, pd - 0.05f),
                              V(doorX - pw * 0.5f, yb + 0.24f, pd - 0.05f), k.ceiling, Vector3.down);
                GableZ(k, doorX, pw * 0.5f, pd, 0f, yb + 0.25f, 6f / 12f, 0.12f, 0.15f, true, false, false, k.trim, false);
            }
            else Stoop(k, front, doorX, F, lites ? 2.2f : 1.6f, 1.2f, k.conc);

            // side walls (none on the garage side: the garage roof runs into it)
            for (int sg = -1; sg <= 1; sg += 2)
            {
                if (garage && sg == gSide) continue;
                var f = sg < 0 ? Fc.Left(mx0, -D * 0.5f) : Fc.Right(mx1, -D * 0.5f);
                for (int fl = 0; fl < 2; fl++)
                    for (int j = -1; j <= 1; j += 2)
                        Win(k, f, j * D * 0.25f, F + fl * SH + Sill, WinW, WinH, 0f, false, true);
            }
            // back
            var back = Fc.Back(0f, -D);
            for (int i = 0; i < bays; i++)
            {
                float x = mx0 + 0.4f + (i + 0.5f) * bp;
                Win(k, back, -x, F + SH + Sill, WinW, WinH, 0f, false, true);
                if (i == bays / 2) { Slider(k, back, -x, F); Stoop(k, back, -x, F, 2.2f, 1.0f, k.conc); }
                else if (i == 0 || i == bays - 1) Win(k, back, -x, F + Sill, WinW, WinH, 0f, false, true);
            }
            var ri = Gable(k, mx0, mx1, 0f, -D, plate, pitch, 0.35f, 0.25f);

            if (garage)
            {
                float gx0 = gSide < 0 ? -hw : hw - gw, gx1 = gx0 + gw, gc = (gx0 + gx1) * 0.5f;
                const float zg = -0.6f;
                float gd = Mathf.Min(D - 0.6f, 7.3f);
                float gPlate = 0.1f + SH;
                Body(k, gx0, gx1, zg - gd, zg, 0.1f, gPlate, true);
                GarageDoor(k, Fc.Front(gc, zg), 0f, 0.1f, Mathf.Min(GDoorW, gw - 0.7f), GDoorH, b.R() < 0.5f);
                var gsF = gSide < 0 ? Fc.Left(gx0, zg - gd * 0.5f) : Fc.Right(gx1, zg - gd * 0.5f);
                Win(k, gsF, 0f, 1.1f, WinW, 1.07f, 0f, false, true);
                Gable(k, gx0, gx1, zg, zg - gd, gPlate, 6f / 12f, 0.3f, 0.25f, gSide < 0, gSide > 0, true);
            }
            if (b.R() < 0.55f)
            {
                float cx = garage ? (gSide < 0 ? mx1 - 0.9f : mx0 + 0.9f) : (b.R() < 0.5f ? mx0 + 0.9f : mx1 - 0.9f);
                Chimney(k, cx, -D * 0.5f, 0.75f, 0.55f, plate - 0.5f, ri.ridgeY + 0.9f);
            }
        }

        /// <summary>American foursquare. Porch (2.1 m) + steps project ~2.9 m in +Z.</summary>
        static void DrawFoursquare(MapBuild b, MapBuildingSpec s)
        {
            float W = Mathf.Max(7.5f, s.Width), D = Mathf.Max(7.5f, s.Depth), hw = W * 0.5f;
            bool brick = b.R() < 0.45f;
            var k = NewKit(b, s, brick ? MapMaterials.BrickWall(Pick(b, BrickTints)) : MapMaterials.Siding(s.Color));
            if (brick) k.found = k.wall;
            float F = b.R(0.6f, 0.75f);
            float plate = F + SH + WH;
            float pitch = Pick(b, new[] { 6f, 7f, 8f }) / 12f;
            Body(k, -hw, hw, -D, 0f, F, plate, !brick);

            var front = Fc.Front(0f, 0f);
            int ds = b.R() < 0.5f ? -1 : 1;
            float dx = ds * W * 0.25f;
            Door(k, front, dx, F, Pick(b, new[] { 1, 2 }), false, false, ds > 0);
            Win(k, front, -dx - 0.475f, F + 0.6f, 0.86f, 1.5f, 0f, false, false);
            Win(k, front, -dx + 0.475f, F + 0.6f, 0.86f, 1.5f, 0f, false, false);
            for (int sg = -1; sg <= 1; sg += 2) Win(k, front, sg * W * 0.25f, F + SH + Sill, WinW, WinH, 0f, true);
            Win(k, front, 0f, F + SH + 1.0f, 0.6f, 0.9f, 0f, false, true);

            const float pd = 2.1f;
            float beamY = F + 2.3f;
            const float stairW = 1.6f;
            var cols = ColumnLayout(-hw, hw, dx, stairW, 3.2f);
            int style = brick ? 2 : (b.R() < 0.5f ? 2 : 0);
            Porch(k, -hw, hw, 0f, pd, F, beamY, 0.25f, cols, dx, stairW, style, true, k.roof, brick ? k.wall : k.brick);

            for (int sg = -1; sg <= 1; sg += 2)
            {
                var f = sg < 0 ? Fc.Left(-hw, -D * 0.5f) : Fc.Right(hw, -D * 0.5f);
                for (int fl = 0; fl < 2; fl++)
                    for (int j = -1; j <= 1; j += 2)
                        Win(k, f, j * D * 0.25f, F + fl * SH + Sill, WinW, WinH, 0f, false, true);
            }
            var back = Fc.Back(0f, -D);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                Win(k, back, sg * W * 0.28f, F + SH + Sill, WinW, WinH, 0f, false, true);
                Win(k, back, sg * W * 0.28f, F + (sg < 0 ? 1.07f : Sill), WinW, sg < 0 ? 1.07f : WinH, 0f, false, true);
            }
            Door(k, back, 0f, F, 2, false, false, true);
            Stoop(k, back, 0f, F, 1.4f, 1.1f, k.conc);

            var ri = Hip(k, -hw, hw, 0f, -D, plate, pitch, 0.55f);
            Dormer(k, 0f, 0f, plate, pitch, -0.55f, 2.1f, 2, 0.6f, 0.75f, pitch, true);
            if (b.R() < 0.7f) Chimney(k, -ds * (hw - 0.7f), -D * 0.55f, 0.6f, 0.75f, plate - 0.5f, ri.ridgeY + 0.5f);
        }

        /// <summary>Cape cod. Stoop + steps project ~1.8 m in +Z.</summary>
        static void DrawCape(MapBuild b, MapBuildingSpec s)
        {
            float W = Mathf.Max(8f, s.Width), D = Mathf.Max(7f, s.Depth), hw = W * 0.5f;
            var k = NewKit(b, s, MapMaterials.Siding(s.Color));
            float F = b.R(0.45f, 0.6f);
            float plate = F + WH;
            const float pitch = 1f;   // 12/12
            Body(k, -hw, hw, -D, 0f, F, plate, true);

            var front = Fc.Front(0f, 0f);
            Door(k, front, 0f, F, Pick(b, new[] { 0, 0, 2 }), false, b.R() < 0.3f);
            Stoop(k, front, 0f, F, 1.6f, 1.2f, k.conc);
            float inner = Mathf.Max(1.55f, W * 0.17f), outer = Mathf.Min(hw - 1.0f, W * 0.36f);
            float free = outer - inner - WinW - 0.18f;
            float shW = b.R() < 0.75f ? Mathf.Clamp((free - 0.05f) * 0.5f - 0.03f, 0f, 0.42f) : 0f;
            for (int sg = -1; sg <= 1; sg += 2)
            {
                Win(k, front, sg * inner, F + Sill, WinW, WinH, shW, true);
                Win(k, front, sg * outer, F + Sill, WinW, WinH, shW, true);
            }
            var ri = Gable(k, -hw, hw, 0f, -D, plate, pitch, 0.25f, 0.15f, true, true, true, -1, false);
            for (int sg = -1; sg <= 1; sg += 2)
                Dormer(k, sg * W * 0.26f, 0f, plate, pitch, -0.45f, 1.45f, 1, 0.71f, 1.07f, pitch, false);
            // gable ends: ground windows + half-storey windows
            float us = Mathf.Min(0.9f, D * 0.11f);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                var f = sg < 0 ? Fc.Left(-hw, -D * 0.5f) : Fc.Right(hw, -D * 0.5f);
                for (int j = -1; j <= 1; j += 2)
                {
                    Win(k, f, j * D * 0.25f, F + Sill, WinW, WinH, 0f, false, true);
                    Win(k, f, j * us, plate + 0.85f, 0.71f, 1.07f, 0f, true, true);
                }
            }
            var back = Fc.Back(0f, -D);
            for (int sg = -1; sg <= 1; sg += 2) Win(k, back, sg * W * 0.3f, F + Sill, WinW, WinH, 0f, false, true);
            Door(k, back, 0f, F, 2, false, false, true);
            Stoop(k, back, 0f, F, 1.4f, 1.0f, k.conc);
            float chx = b.R() < 0.5f ? 0f : (b.R() < 0.5f ? -hw + 0.8f : hw - 0.8f);
            Chimney(k, chx, -D * 0.5f, 0.9f, 0.7f, plate, ri.ridgeY + 0.75f);
        }

        /// <summary>Modern ranch with an attached front-entry 2-car garage. Garage front at z = 0; stoop + steps project ~0.6 m.</summary>
        static void DrawRanch2(MapBuild b, MapBuildingSpec s)
        {
            float W = Mathf.Max(11f, s.Width), D = Mathf.Max(7.5f, s.Depth), hw = W * 0.5f;
            var k = NewKit(b, s, MapMaterials.Siding(s.Color));
            float F = b.R(0.3f, 0.5f);
            float plate = F + WH;
            bool hip = b.R() < 0.5f;
            float pitch = (hip ? Pick(b, new[] { 5f, 6f }) : Pick(b, new[] { 6f, 7f })) / 12f;
            int g = b.R() < 0.5f ? -1 : 1;
            float gw = Mathf.Clamp(W * 0.42f, 6.4f, Mathf.Min(7.3f, W - 5.5f));
            float gx0 = g < 0 ? -hw : hw - gw, gx1 = gx0 + gw, gc = (gx0 + gx1) * 0.5f;
            float mx0 = g < 0 ? gx1 : -hw, mx1 = g < 0 ? hw : gx0, mw = mx1 - mx0;
            float r = hip ? 0f : 1.2f;          // gable variant: house front recessed behind the garage
            float gin = g < 0 ? gx1 : gx0, far = g < 0 ? mx1 : mx0, dir = -g;

            // garage (slab at grade) + house
            Body(k, gx0, gx1, -D, 0f, 0.1f, plate, true);
            GarageDoor(k, Fc.Front(gc, 0f), 0f, 0.1f, GDoorW, GDoorH, b.R() < 0.4f);
            Body(k, mx0, mx1, -D, -r, F, plate, true);

            var front = Fc.Front(0f, -r);
            float doorX = gin + dir * 1.3f;
            Door(k, front, doorX, F, Pick(b, new[] { 1, 2, 0 }), false, false, g > 0);
            Stoop(k, front, doorX, F, 1.6f, 1.1f, k.conc);
            int pn = mw > 7.5f ? 3 : 2;
            float pc = gin + dir * (1.3f + 0.75f + pn * 0.425f + 0.2f);
            for (int i = 0; i < pn; i++) Win(k, front, pc + (i - (pn - 1) * 0.5f) * 0.85f, F + 0.6f, 0.76f, 1.5f, 0f, true, false);
            float shW = b.R() < 0.5f ? 0.38f : 0f;
            float pEdge = Mathf.Abs(pc - gin) + pn * 0.425f + 0.2f;
            float w1 = mw - 1.3f;
            if (w1 - 1.0f > pEdge) Win(k, front, gin + dir * w1, F + Sill, WinW, WinH, shW);
            if (w1 - 3.1f > pEdge) Win(k, front, gin + dir * (w1 - 2.0f), F + Sill, WinW, WinH, shW);

            // outer sides: garage man door + window, house end windows
            var gF = g < 0 ? Fc.Left(gx0, -D * 0.5f) : Fc.Right(gx1, -D * 0.5f);
            Door(k, gF, D * 0.2f, 0.1f, 2, false, false, true);
            Win(k, gF, -D * 0.2f, 1.1f, WinW, 1.07f, 0f, false, true);
            float hcz = (-r - D) * 0.5f, hd = D - r;
            var hF = g < 0 ? Fc.Right(mx1, hcz) : Fc.Left(mx0, hcz);
            for (int j = -1; j <= 1; j += 2) Win(k, hF, j * hd * 0.25f, F + Sill, WinW, WinH, 0f, false, true);
            // back
            var back = Fc.Back(0f, -D);
            float mc = (mx0 + mx1) * 0.5f;
            Slider(k, back, -mc, F);
            Stoop(k, back, -mc, F, 2.2f, 1.0f, k.conc);
            Win(k, back, -(far - dir * 1.2f), F + Sill, WinW, WinH, 0f, false, true);
            Win(k, back, -(gin + dir * 0.9f), F + 1.07f, 0.76f, 1.07f, 0f, false, true);
            Win(k, back, -gc, 1.2f, WinW, 0.9f, 0f, false, true);

            if (hip) Hip(k, -hw, hw, 0f, -D, plate, pitch, 0.45f);
            else
            {
                Gable(k, mx0, mx1, -r, -D, plate, pitch, 0.35f, 0.3f, true, true, true);
                GableZ(k, gc, gw * 0.5f, 0f, -D, plate, pitch, 0.35f, 0.3f, true, true, true);
            }
        }

        /// <summary>Side-by-side duplex. Stoops + steps project ~1.7 m in +Z.</summary>
        static void DrawDuplex(MapBuild b, MapBuildingSpec s)
        {
            float W = Mathf.Max(10f, s.Width), D = Mathf.Max(7.5f, s.Depth), hw = W * 0.5f;
            var k = NewKit(b, s, MapMaterials.Siding(s.Color));
            int st = Mathf.Clamp(s.Stories, 1, 2);
            float F = b.R(0.4f, 0.6f);
            float plate = F + (st - 1) * SH + WH;
            bool hip = b.R() < 0.35f;
            float pitch = Pick(b, new[] { 5f, 6f, 7f }) / 12f;
            Body(k, -hw, hw, -D, 0f, F, plate, true);
            Bx(k, V(-0.07f, F + 0.1f, -0.01f), V(0.07f, plate, 0.025f), k.trim);   // party-line board

            bool outerDoors = b.R() < 0.35f;
            int style = Pick(b, new[] { 0, 1, 2 });
            float a = outerDoors ? 0.9f : 2.1f, c = outerDoors ? hw - 2.3f : hw - 0.9f;
            float spacing = (c - a) * 0.5f;
            float shW = b.R() < 0.6f ? Mathf.Min(0.38f, (spacing - WinW - 0.24f - 0.05f) * 0.5f) : 0f;
            var front = Fc.Front(0f, 0f);
            var back = Fc.Back(0f, -D);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                float dx = sg * (outerDoors ? hw - 1.3f : 1.1f);
                Door(k, front, dx, F, style, false, false, sg < 0);
                Stoop(k, front, dx, F, 1.4f, 1.1f, k.conc);
                if (st == 2)
                {
                    Hood(k, front, dx, F + 2.5f, 1.5f, 0.85f);
                    Win(k, front, dx, F + SH + Sill, WinW * 0.8f, WinH, 0f, false, true);
                }
                for (int j = 0; j < 2; j++)
                {
                    float x = sg * (a + (c - a) * (j == 0 ? 0.25f : 0.75f));
                    Win(k, front, x, F + Sill, WinW, WinH, shW, true);
                    if (st == 2) Win(k, front, x, F + SH + Sill, WinW, WinH, shW, true);
                }
                // back of this unit
                Slider(k, back, -sg * hw * 0.45f, F);
                Stoop(k, back, -sg * hw * 0.45f, F, 2.2f, 1.0f, k.conc);
                Win(k, back, -sg * hw * 0.82f, F + Sill, WinW, WinH, 0f, false, true);
                if (st == 2)
                    for (int j = 0; j < 2; j++) Win(k, back, -sg * hw * (j == 0 ? 0.3f : 0.75f), F + SH + Sill, WinW, WinH, 0f, false, true);
                // end wall
                var f = sg < 0 ? Fc.Left(-hw, -D * 0.5f) : Fc.Right(hw, -D * 0.5f);
                for (int fl = 0; fl < st; fl++)
                    for (int j = -1; j <= 1; j += 2) Win(k, f, j * D * 0.25f, F + fl * SH + Sill, WinW, WinH, 0f, false, true);
            }
            if (hip) Hip(k, -hw, hw, 0f, -D, plate, pitch, 0.4f);
            else Gable(k, -hw, hw, 0f, -D, plate, pitch, 0.35f, 0.25f);
        }

        /// <summary>Townhouse row. Units alternate siding colours / brick fronts; some interior units set back 0.45 m.
        /// Stoops + steps project ~2.1 m in +Z (brick fronts add 0.1 m).</summary>
        static void DrawTownhomes(MapBuild b, MapBuildingSpec s)
        {
            int N = Mathf.Clamp(s.Units < 2 ? 4 : s.Units, 2, 8);
            float W = Mathf.Max(N * 5f, s.Width), D = Mathf.Max(8f, s.Depth), hw = W * 0.5f, uw = W / N;
            var k = NewKit(b, s, MapMaterials.Siding(s.Color));
            int st = s.Stories >= 3 ? 3 : 2;
            float F = b.R(0.5f, 0.75f);
            float plate = F + (st - 1) * SH + WH;
            float pitch = Pick(b, new[] { 6f, 7f, 8f }) / 12f;
            Color[] pal =
            {
                s.Color, Color.Lerp(s.Color, new Color(0.93f, 0.90f, 0.82f), 0.6f),
                Color.Lerp(s.Color, new Color(0.42f, 0.50f, 0.58f), 0.55f), Color.Lerp(s.Color, new Color(0.60f, 0.62f, 0.55f), 0.5f),
            };
            int brickSub = b.K.M(MapMaterials.BrickWall(Pick(b, BrickTints)));
            bool stagger = b.R() < 0.6f;
            int off0 = b.Rng.Next(3), parOff = b.Rng.Next(2);
            var back = Fc.Back(0f, -D);
            for (int i = 0; i < N; i++)
            {
                float x0 = -hw + i * uw, x1 = x0 + uw, uc = (x0 + x1) * 0.5f;
                bool brick = (i + off0) % 3 == 2;
                int sub = b.K.M(MapMaterials.Siding(pal[(i + off0) % pal.Length]));
                float zf = (stagger && i > 0 && i < N - 1 && i % 2 == 1) ? -0.45f : 0f;
                Body(k, x0, x1, -D, zf, F, plate, false, sub, -1, true, false);
                float face = zf;
                if (brick) { Bx(k, V(x0, 0f, zf), V(x1, plate, zf + 0.1f), brickSub, Vector3.right, Sides); face = zf + 0.1f; }
                var f = Fc.Front(uc, face);
                int ds = i % 2 == 0 ? 1 : -1;
                float dS = ds * (uw * 0.5f - 0.85f);
                bool transom = b.R() < 0.4f;
                Door(k, f, dS, F, Pick(b, new[] { 0, 1, 2 }), false, transom, ds > 0);
                Stoop(k, f, dS, F, 1.3f, 1.1f, k.conc);
                Hood(k, f, dS, F + (transom ? 2.8f : 2.45f), 1.4f, 0.8f);
                float wsx = -ds * uw * 0.17f;
                Win(k, f, wsx - 0.475f, F + 0.65f, 0.86f, 1.45f, 0f, false, true);
                Win(k, f, wsx + 0.475f, F + 0.65f, 0.86f, 1.45f, 0f, false, true);
                float shW = (!brick && b.R() < 0.5f) ? 0.36f : 0f;
                for (int fl = 1; fl < st; fl++)
                    for (int j = -1; j <= 1; j += 2) Win(k, f, j * uw * 0.22f, F + fl * SH + Sill, WinW, WinH, shW, false, true);
                float bx = uc + ds * uw * 0.18f;
                Slider(k, back, -bx, F);
                Stoop(k, back, -bx, F, 2.0f, 1.0f, k.conc);
                for (int fl = 1; fl < st; fl++) Win(k, back, -uc, F + fl * SH + Sill, WinW, WinH, 0f, false, true);
            }
            CornerBoards(k, -hw, hw, -D, 0f, F, plate);
            k.b.Solid(V(-hw, 0f, -D), V(hw, plate, 0f));
            for (int sg = -1; sg <= 1; sg += 2)
            {
                var f = sg < 0 ? Fc.Left(-hw, -D * 0.5f) : Fc.Right(hw, -D * 0.5f);
                for (int fl = 0; fl < st; fl++)
                    for (int j = -1; j <= 1; j += 2) Win(k, f, j * D * 0.25f, F + fl * SH + Sill, WinW, WinH, 0f, false, true);
            }
            const float eave = 0.35f;
            var ri = Gable(k, -hw, hw, 0f, -D, plate, pitch, eave, 0.25f);
            // masonry fire-wall parapets through the roof between some units
            float eY = ri.eaveY, rY = ri.ridgeY, zc = -D * 0.5f, zE = eave, zBk = -D - eave;
            var prof = new[]
            {
                new Vector2(-zE, eY - 0.3f), new Vector2(-zE, eY + 0.4f), new Vector2(-zc, rY + 0.4f),
                new Vector2(-zBk, eY + 0.4f), new Vector2(-zBk, eY - 0.3f),
            };
            for (int i = 0; i + 1 < N; i++)
            {
                if ((i + parOff) % 2 != 0) continue;
                float x = -hw + (i + 1) * uw;
                k.b.B.AddExtrude(prof, V(x, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), 0.3f, brickSub);
                Plate(k, new[] { V(x - 0.2f, eY + 0.46f, zE + 0.05f), V(x + 0.2f, eY + 0.46f, zE + 0.05f), V(x + 0.2f, rY + 0.46f, zc), V(x - 0.2f, rY + 0.46f, zc) },
                      0.06f, k.conc, k.conc, k.conc, Vector3.right);
                Plate(k, new[] { V(x + 0.2f, eY + 0.46f, zBk - 0.05f), V(x - 0.2f, eY + 0.46f, zBk - 0.05f), V(x - 0.2f, rY + 0.46f, zc), V(x + 0.2f, rY + 0.46f, zc) },
                      0.06f, k.conc, k.conc, k.conc, Vector3.right);
            }
        }

        /// <summary>White farmhouse with a wraparound porch (front porch 2.1 m in +Z, + steps: ~2.9 m), standing-seam roof, rear wing.</summary>
        static void DrawFarmhouse(MapBuild b, MapBuildingSpec s)
        {
            float W = Mathf.Max(8f, s.Width), D = Mathf.Max(8f, s.Depth), hw = W * 0.5f;
            Color roofC = Pick(b, MetalRoofColors);
            var metal = MapMaterials.MetalPanel(roofC);
            var k = NewKit(b, s, MapMaterials.Siding(new Color(0.95f, 0.95f, 0.93f)), metal, metal);
            int ws = b.R() < 0.5f ? 1 : -1;                    // wrap side; geometry below is written for ws = +1 and mirrored by X()
            float X(float x) => ws * x;
            Vector3 P(float x, float y, float z) => V(X(x), y, z);
            void BxC(float ax0, float ax1, float y0, float y1, float z0, float z1, int sub) => Bx(k, P(ax0, y0, z0), P(ax1, y1, z1), sub);
            Fc SideF(float cxw, float cz, bool plusSide) => (plusSide == (ws > 0)) ? Fc.Right(X(cxw), cz) : Fc.Left(X(cxw), cz);

            const float pd = 2.1f;
            float F = b.R(0.55f, 0.75f);
            float plate = F + SH + WH;
            float pitch = Pick(b, new[] { 10f, 12f }) / 12f;
            float bx0 = -hw, bx1 = hw - pd, bw = bx1 - bx0, bc = (bx0 + bx1) * 0.5f;
            float Dm = Mathf.Min(D, Mathf.Clamp(D * 0.62f, 6.5f, 8.5f));
            float sideLen = Mathf.Clamp(Dm - 0.6f, 2.6f, 4.8f);

            Body(k, Mathf.Min(X(bx0), X(bx1)), Mathf.Max(X(bx0), X(bx1)), -Dm, 0f, F, plate, true);
            const float wW = 0.86f, wH = 1.5f, wS = 0.7f;
            float shW = b.R() < 0.5f ? 0.4f : 0f;
            var front = Fc.Front(0f, 0f);
            Door(k, front, X(bc), F, b.R() < 0.5f ? 2 : 0, false, true, ws > 0);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                Win(k, front, X(bc + sg * bw * 0.3f), F + wS, wW, wH, shW, true);
                Win(k, front, X(bc + sg * bw * 0.3f), F + SH + wS, wW, wH, shW, true);
            }
            Win(k, front, X(bc), F + SH + wS, wW, wH, 0f, true);
            // body side walls (the +side faces onto the wrap porch)
            for (int p = 0; p < 2; p++)
            {
                var f = SideF(p == 0 ? bx0 : bx1, -Dm * 0.5f, p == 1);
                for (int fl = 0; fl < 2; fl++)
                    for (int j = -1; j <= 1; j += 2) Win(k, f, j * Dm * 0.25f, F + fl * SH + wS, wW, wH, 0f, false, true);
            }
            var back = Fc.Back(0f, -Dm);
            Win(k, back, -X(bx1 - 1.2f), F + wS, wW, wH, 0f, false, true);
            Win(k, back, -X(bx1 - 1.2f), F + SH + wS, wW, wH, 0f, false, true);

            var ri = Gable(k, Mathf.Min(X(bx0), X(bx1)), Mathf.Max(X(bx0), X(bx1)), 0f, -Dm, plate, pitch, 0.3f, 0.2f);
            // front cross gable with an attic window
            const float cg = 2.0f;
            GableZ(k, X(bc), cg, 0f, -(cg + 0.15f), plate, pitch, 0.3f, 0.25f, true, false, false, -1, false);
            Win(k, front, X(bc), plate + 0.3f, 0.6f, 0.75f, 0f, true, true);
            if (b.R() < 0.6f) Chimney(k, X(bx0 + 0.8f), -Dm * 0.5f, 0.7f, 0.6f, plate - 0.5f, ri.ridgeY + 0.8f);

            // rear L wing (1 storey) on the side away from the wrap porch
            if (D - Dm >= 2.5f)
            {
                float ww = Mathf.Min(bw * 0.62f, 5.5f), wx0 = bx0, wx1 = bx0 + ww, wc = (wx0 + wx1) * 0.5f, wz = (-Dm - D) * 0.5f;
                float wPlate = F + WH;
                Body(k, Mathf.Min(X(wx0), X(wx1)), Mathf.Max(X(wx0), X(wx1)), -D, -Dm, F, wPlate, true);
                GableZ(k, X(wc), ww * 0.5f, -Dm + 0.2f, -D, wPlate, pitch, 0.3f, 0.2f, false, true, true);
                var wf = SideF(wx1, wz, true);
                Win(k, wf, 0f, F + wS, wW, wH, 0f, false, true);
                var wo = SideF(wx0, wz, false);
                for (int j = -1; j <= 1; j += 2) Win(k, wo, j * (D - Dm) * 0.22f, F + wS, wW, wH, 0f, false, true);
                var wb = Fc.Back(0f, -D);
                Door(k, wb, -X(wc), F, 2, false, false, true);
                Stoop(k, wb, -X(wc), F, 1.4f, 1.0f, k.conc);
            }

            // ---- wraparound porch
            float beamY = F + 2.4f, zc = pd - 0.17f, yc = beamY + 0.27f;
            const float pp = 0.25f, o = 0.3f;
            BxC(-hw, hw, F - 0.2f, F, 0f, pd, k.deck);
            BxC(bx1, hw, F - 0.2f, F, -sideLen, 0f, k.deck);
            BxC(-hw + 0.04f, hw - 0.04f, 0f, F - 0.2f, 0f, pd - 0.06f, k.found);
            BxC(bx1 + 0.04f, hw - 0.06f, 0f, F - 0.2f, -sideLen, 0f, k.found);
            BxC(-hw - 0.02f, hw + 0.02f, F - 0.25f, F - 0.02f, pd - 0.03f, pd + 0.01f, k.trim);
            BxC(hw - 0.03f, hw + 0.02f, F - 0.25f, F - 0.02f, -sideLen, pd, k.trim);
            Steps(k, Fc.Front(X(bc), pd), 0f, F, 1.8f, 0f, k.conc);
            var cols = ColumnLayout(-hw, hw, bc, 1.8f, 2.8f);
            foreach (float cx in cols) Column(k, X(cx), zc, F, beamY, 0, k.found);
            float zLast = -sideLen + 0.2f;
            int nSide = Mathf.Max(1, Mathf.CeilToInt((zc - zLast) / 2.8f));
            var sideZ = new List<float> { zc };
            for (int j = 1; j <= nSide; j++) { float z = zc - (zc - zLast) * j / nSide; sideZ.Add(z); Column(k, X(hw - 0.2f), z, F, beamY, 0, k.found); }
            BxC(-hw, hw, beamY, beamY + 0.28f, zc - 0.1f, zc + 0.1f, k.trim);
            BxC(hw - 0.3f, hw - 0.1f, beamY, beamY + 0.28f, -sideLen, zc, k.trim);
            BxC(-hw + 0.1f, -hw + 0.3f, beamY, beamY + 0.28f, 0f, zc, k.trim);
            BxC(bx1, hw - 0.1f, beamY, beamY + 0.28f, zLast - 0.1f, zLast + 0.1f, k.trim);
            k.b.B.AddQuad(P(-hw, yc, 0f), P(hw, yc, 0f), P(hw, yc, zc), P(-hw, yc, zc), k.ceiling, Vector3.down);
            k.b.B.AddQuad(P(bx1, yc, -sideLen), P(hw - 0.1f, yc, -sideLen), P(hw - 0.1f, yc, 0f), P(bx1, yc, 0f), k.ceiling, Vector3.down);
            for (int i = 0; i + 1 < cols.Count; i++)
            {
                float a = cols[i], c = cols[i + 1];
                if (a < bc && c > bc) continue;
                Rail(k, P(a + 0.13f, F, zc), P(c - 0.13f, F, zc), F);
            }
            for (int j = 0; j + 1 < sideZ.Count; j++) Rail(k, P(hw - 0.2f, F, sideZ[j] - 0.13f), P(hw - 0.2f, F, sideZ[j + 1] + 0.13f), F);
            Rail(k, P(-hw + 0.2f, F, 0.05f), P(-hw + 0.2f, F, zc - 0.13f), F);
            Rail(k, P(bx1 + 0.05f, F, zLast), P(hw - 0.33f, F, zLast), F);
            // hipped wrap roof: front plane, end hip, side plane, back hip
            float yE = beamY + 0.28f + RoofTh, yLow = yE - o * pp, yWall = yE + pd * pp;
            float xa = -hw - o, xb = hw + o, zR = pd + o, zB = -sideLen - o, run = pd + o;
            int mr = k.roof;
            Plate(k, new[] { P(xa, yLow, zR), P(xb, yLow, zR), P(bx1, yWall, 0f), P(xa + run, yWall, 0f) }, RoofTh, mr, k.trim, k.trim, Vector3.right);
            Plate(k, new[] { P(xa, yLow, 0f), P(xa, yLow, zR), P(xa + run, yWall, 0f) }, RoofTh, mr, k.trim, k.trim, Vector3.forward);
            Plate(k, new[] { P(bx1, yWall, 0f), P(xb, yLow, zR), P(xb, yLow, zB), P(bx1, yWall, zB + run) }, RoofTh, mr, k.trim, k.trim, Vector3.forward);
            Plate(k, new[] { P(xb, yLow, zB), P(bx1, yLow, zB), P(bx1, yWall, zB + run) }, RoofTh, mr, k.trim, k.trim, Vector3.right);
            HipCap(k, P(xa, yLow, zR), P(xa + run, yWall, 0f), k.ridge);
            HipCap(k, P(xb, yLow, zR), P(bx1, yWall, 0f), k.ridge);
            HipCap(k, P(xb, yLow, zB), P(bx1, yWall, zB + run), k.ridge);
            BxC(xa, xb, yLow - 0.2f, yLow + 0.005f, zR, zR + 0.025f, k.trim);
            BxC(xa, xb, yLow - 0.17f, yLow - 0.04f, zR + 0.025f, zR + 0.15f, k.trim);
            BxC(xb, xb + 0.025f, yLow - 0.2f, yLow + 0.005f, zB, zR + 0.025f, k.trim);
            BxC(xb + 0.025f, xb + 0.15f, yLow - 0.17f, yLow - 0.04f, zB, zR + 0.15f, k.trim);
            BxC(xa - 0.025f, xa, yLow - 0.2f, yLow + 0.005f, 0f, zR, k.trim);
            BxC(bx1, xb, yLow - 0.2f, yLow + 0.005f, zB - 0.025f, zB, k.trim);
        }

        /// <summary>Detached 2-car garage (24x24'). Nothing projects in +Z beyond the 0.3 m eave.</summary>
        static void DrawGarage(MapBuild b, MapBuildingSpec s)
        {
            float W = Mathf.Max(5.5f, s.Width), D = Mathf.Max(5.5f, s.Depth), hw = W * 0.5f;
            var k = NewKit(b, s, MapMaterials.Siding(s.Color));
            const float F = 0.12f;
            float plate = F + SH;
            Body(k, -hw, hw, -D, 0f, F, plate, true);
            GarageDoor(k, Fc.Front(0f, 0f), 0f, F, Mathf.Min(GDoorW, W - 1.2f), GDoorH, b.R() < 0.4f);
            int ds = b.R() < 0.5f ? -1 : 1;
            var df = ds < 0 ? Fc.Left(-hw, -D * 0.5f) : Fc.Right(hw, -D * 0.5f);
            Door(k, df, D * 0.2f, F, 2, false, false, true);
            Stoop(k, df, D * 0.2f, F, 1.2f, 1.0f, k.conc);
            var of = ds < 0 ? Fc.Right(hw, -D * 0.5f) : Fc.Left(-hw, -D * 0.5f);
            Win(k, of, 0f, 1.2f, WinW, 1.07f, 0f, false, true);
            Win(k, Fc.Back(0f, -D), 0f, 1.2f, WinW, 1.07f, 0f, false, true);
            if (b.R() < 0.5f) GableZ(k, 0f, hw, 0f, -D, plate, 7f / 12f, 0.3f, 0.25f, true, true, true);
            else Gable(k, -hw, hw, 0f, -D, plate, 6f / 12f, 0.3f, 0.25f);
        }

        /// <summary>10x12' shed on skids with double doors. A 0.35 m ramp step projects in +Z.</summary>
        static void DrawShed(MapBuild b, MapBuildingSpec s)
        {
            float W = Mathf.Max(2.4f, s.Width), D = Mathf.Max(2.4f, s.Depth), hw = W * 0.5f;
            var k = NewKit(b, s, MapMaterials.Siding(s.Color));
            int pt = b.K.M(SurfaceKind.TreatedLumber);
            const float F = 0.3f;
            float plate = F + 2.0f;
            // 4x4 treated skids, 2x6 floor frame, walls, corner boards
            for (int sg = -1; sg <= 1; sg += 2)
                Bx(k, V(sg * (hw - 0.25f) - 0.045f, 0f, -D - 0.05f), V(sg * (hw - 0.25f) + 0.045f, 0.09f, 0.05f), pt, Vector3.forward);
            Bx(k, V(-hw - 0.02f, 0.09f, -D - 0.02f), V(hw + 0.02f, F, 0.02f), pt, Vector3.right);
            Bx(k, V(-hw, F, -D), V(hw, plate, 0f), k.wall, Vector3.right, Sides);
            CornerBoards(k, -hw, hw, -D, 0f, F, plate);
            k.b.Solid(V(-hw, 0f, -D), V(hw, plate, 0f));

            var f = Fc.Front(0f, 0f);
            float dw = Mathf.Min(1.52f, W - 0.8f), dh = 1.83f, y0 = F;
            FB(k, f, -dw * 0.5f - 0.09f, -dw * 0.5f, y0, y0 + dh, 0f, 0.03f, k.trim);
            FB(k, f, dw * 0.5f, dw * 0.5f + 0.09f, y0, y0 + dh, 0f, 0.03f, k.trim);
            FB(k, f, -dw * 0.5f - 0.11f, dw * 0.5f + 0.11f, y0 + dh, y0 + dh + 0.1f, 0f, 0.04f, k.trim);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                float l0 = sg < 0 ? -dw * 0.5f : 0.006f, l1 = sg < 0 ? -0.006f : dw * 0.5f;
                FB(k, f, l0, l1, y0, y0 + dh, 0f, 0.02f, k.wall);
                FB(k, f, l0, l1, y0, y0 + 0.1f, 0.02f, 0.045f, k.trim);
                FB(k, f, l0, l1, y0 + dh - 0.1f, y0 + dh, 0.02f, 0.045f, k.trim);
                FB(k, f, l0, l0 + 0.09f, y0 + 0.1f, y0 + dh - 0.1f, 0.02f, 0.045f, k.trim);
                FB(k, f, l1 - 0.09f, l1, y0 + 0.1f, y0 + dh - 0.1f, 0.02f, 0.045f, k.trim);
                // diagonal Z-brace from the hinge-side bottom to the latch-side top
                float ddx = (l1 - l0) - 0.18f, ddy = dh - 0.2f;
                float len = Mathf.Sqrt(ddx * ddx + ddy * ddy) - 0.06f;
                float ang = Mathf.Atan2(ddy, ddx) * Mathf.Rad2Deg * (sg < 0 ? 1f : -1f);
                k.b.BeginLocal(V((l0 + l1) * 0.5f, y0 + dh * 0.5f, 0.0325f), Quaternion.Euler(0f, 0f, ang));
                Bx(k, V(-len * 0.5f, -0.045f, -0.0125f), V(len * 0.5f, 0.045f, 0.0125f), k.trim);
                k.b.EndLocal();
                float hx = sg * 0.06f;
                FB(k, f, hx - 0.012f, hx + 0.012f, y0 + 0.85f, y0 + 1.05f, 0.045f, 0.07f, k.black);
            }
            Bx(k, V(-dw * 0.5f, 0f, 0f), V(dw * 0.5f, 0.15f, 0.35f), pt);
            Win(k, Fc.Right(hw, -D * 0.5f), 0f, F + 0.85f, 0.6f, 0.6f, 0.26f, true, true);
            Win(k, Fc.Left(-hw, -D * 0.5f), 0f, F + 0.85f, 0.6f, 0.6f, 0.26f, true, true);
            GableZ(k, 0f, hw, 0f, -D, plate, 8f / 12f, 0.15f, 0.15f, true, true, false);
        }
    }
}
