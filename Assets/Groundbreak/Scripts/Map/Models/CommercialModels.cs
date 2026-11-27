// CommercialModels — procedural town-map models for commercial and civic buildings (small-town USA).
// Registered types:
//   storefront  downtown Main Street brick block (1-3 stories, party walls, storefront + awning + cornice)
//   diner       stainless roadside diner with rounded corners, glass band, barrel roof and neon sign
//   bank        brick/stone bank with a 4-column classical portico, pediment and steps
//   office      2-3 storey brick or stucco office, ribbon or punched windows, entrance canopy
//   townhall    2-storey brick town hall, columned portico, clock cupola, flagpole
//   church      brick or white clapboard church, front steeple tower with louvered belfry and spire
//   postoffice  1-storey federal-style brick post office, flagpole, blue collection box
//   library     Carnegie-style brick library on a raised basement with entrance pavilion and steps
//   school      large 1-storey elementary school with window bands, entrance canopy, gym block, RTUs
//   firestation brick fire station with full-view glass apparatus doors, apron and hose tower
//   gasstation  convenience store + fuel canopy with pump islands + forecourt slab + price pylon
//   grocery     supermarket box with glass entry vestibule, sign, cart corral, loading docks
//   stripmall   row of retail bays under a continuous arcade, each with its own glass + sign panel
//   fastfood    1-storey fast-food restaurant, mansard or flat roof, drive-thru lane, menu board, pylon
//   motel       2-storey motel, exterior walkway + railing, stair, office, pylon sign
//   autorepair  auto repair shop with 3-4 overhead service doors, glass office, sign band
// Frame: origin at grade, middle of the front edge, +Z toward the street, footprint x ±Width/2, z -Depth..0.
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Groundbreak
{
    public static class CommercialModels
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            MapModels.Register("storefront", Storefront);
            MapModels.Register("diner", Diner);
            MapModels.Register("bank", Bank);
            MapModels.Register("office", Office);
            MapModels.Register("townhall", TownHall);
            MapModels.Register("church", Church);
            MapModels.Register("postoffice", PostOffice);
            MapModels.Register("library", Library);
            MapModels.Register("school", School);
            MapModels.Register("firestation", FireStation);
            MapModels.Register("gasstation", GasStation);
            MapModels.Register("grocery", Grocery);
            MapModels.Register("stripmall", StripMall);
            MapModels.Register("fastfood", FastFood);
            MapModels.Register("motel", Motel);
            MapModels.Register("autorepair", AutoRepair);
        }

        // =====================================================================================================
        // Shared kit
        // =====================================================================================================

        const int All = MeshBuilder.AllFaces;
        const int NoBack = All & ~MeshBuilder.FaceNegZ;
        const int Shown = All & ~MeshBuilder.FaceNegZ & ~MeshBuilder.FaceNegY;
        const int NoBottom = All & ~MeshBuilder.FaceNegY;
        const int Walls = All & ~MeshBuilder.FaceNegY & ~MeshBuilder.FacePosY;

        static readonly Color BuffBrick = new Color(0.86f, 0.74f, 0.55f);
        static readonly Color[] PaintedBrick =
        {
            new Color(0.86f, 0.82f, 0.72f), new Color(0.55f, 0.60f, 0.52f), new Color(0.92f, 0.91f, 0.88f),
            new Color(0.50f, 0.22f, 0.18f), new Color(0.62f, 0.66f, 0.70f), new Color(0.80f, 0.70f, 0.52f),
        };
        static readonly Color[] CorniceColors =
        {
            new Color(0.93f, 0.91f, 0.85f), new Color(0.25f, 0.32f, 0.27f), new Color(0.35f, 0.20f, 0.16f),
            new Color(0.55f, 0.52f, 0.48f), new Color(0.20f, 0.22f, 0.28f),
        };
        static readonly Color[] SignColors =
        {
            new Color(0.80f, 0.10f, 0.08f), new Color(0.10f, 0.45f, 0.20f), new Color(0.12f, 0.30f, 0.65f),
            new Color(0.90f, 0.45f, 0.08f), new Color(0.55f, 0.10f, 0.35f),
        };
        static readonly string[] ShopNames =
        {
            "NAILS", "PIZZA", "INSURANCE", "BARBER", "CLEANERS", "PHONE REPAIR", "TAX SERVICE", "VACANT", "DONUTS",
            "LAUNDROMAT", "CHINESE FOOD", "DENTAL", "PET GROOMING", "SUBS", "FLORIST", "BEAUTY SUPPLY", "TANNING",
            "REALTY", "VACANT", "MATTRESS", "VIDEO GAMES", "CHIROPRACTIC", "LIQUOR", "PAWN", "SHOE REPAIR",
        };

        sealed class Kit
        {
            public readonly MapBuild b;
            public readonly MapBuildingSpec s;
            public readonly int glass, dark, interior, curtain, blinds, lit, alum, bronze, trim, stone, conc, roof,
                black, galv, unit, white, accent, yellow, brass, rubber;

            public Kit(MapBuild b, MapBuildingSpec s)
            {
                this.b = b; this.s = s;
                if (b.Rng == null) b.Rng = new System.Random(s.Seed);
                var K = b.K;
                glass = K.M(MapMaterials.Glass);
                dark = K.M(MapMaterials.Flat(new Color(0.07f, 0.08f, 0.09f)));
                interior = K.M(MapMaterials.Flat(new Color(0.22f, 0.19f, 0.16f)));
                curtain = K.M(MapMaterials.Flat(new Color(0.56f, 0.50f, 0.40f)));
                blinds = K.M(MapMaterials.Flat(new Color(0.74f, 0.73f, 0.70f)));
                lit = K.M(MapMaterials.Emissive(new Color(1f, 0.94f, 0.80f)));
                alum = K.M(SurfaceKind.BrushedSteel);
                bronze = K.M(MapMaterials.Flat(new Color(0.19f, 0.15f, 0.11f)));
                trim = K.M(SurfaceKind.PaintedWhite, new Color(0.95f, 0.94f, 0.90f));
                stone = K.M(SurfaceKind.Concrete, new Color(1.08f, 1.03f, 0.92f));
                conc = K.M(MapMaterials.Concrete);
                roof = K.M(MapMaterials.Flat(new Color(0.36f, 0.36f, 0.37f)));
                black = K.M(SurfaceKind.MatteBlack);
                galv = K.M(SurfaceKind.Galvanized);
                unit = K.M(SurfaceKind.PaintedSteel, new Color(0.80f, 0.79f, 0.74f));
                white = K.M(MapMaterials.Flat(new Color(0.95f, 0.95f, 0.93f)));
                accent = K.M(MapMaterials.Flat(s.Accent));
                yellow = K.M(SurfaceKind.PaintedSteel, new Color(0.95f, 0.78f, 0.10f));
                brass = K.M(SurfaceKind.Brass);
                rubber = K.M(SurfaceKind.DarkPlastic);
            }

            public int Flat(Color c) => b.K.M(MapMaterials.Flat(c));
            public int Paint(Color c) => b.K.M(SurfaceKind.PaintedWhite, c);
            public int Glow(Color c) => b.K.M(MapMaterials.Emissive(c));
            public int Brick(Color c) => b.K.M(MapMaterials.BrickWall(c));
            /// <summary>What is behind a window pane: dark room, drawn curtains or blinds.</summary>
            public int Back() { float r = b.R(); return r < 0.6f ? dark : r < 0.8f ? curtain : blinds; }
        }

        static Color Pick(MapBuild b, Color[] a) => a[b.Rng.Next(a.Length)];

        static string SignOf(MapBuildingSpec s, string fallback) =>
            string.IsNullOrWhiteSpace(s.Sign) ? fallback : s.Sign.Trim().ToUpperInvariant();

        // ---- primitives (current frame: building frame, or a BeginLocal frame) ----------------------------

        static void Bx(Kit k, float x0, float y0, float z0, float x1, float y1, float z1, int sub, int mask = All)
        {
            float ax = Mathf.Min(x0, x1), bx = Mathf.Max(x0, x1);
            float ay = Mathf.Min(y0, y1), by = Mathf.Max(y0, y1);
            float az = Mathf.Min(z0, z1), bz = Mathf.Max(z0, z1);
            if (bx - ax < 1e-4f || by - ay < 1e-4f || bz - az < 1e-4f) return;
            k.b.B.AddBox(new Vector3((ax + bx) * 0.5f, (ay + by) * 0.5f, (az + bz) * 0.5f), new Vector3(bx - ax, by - ay, bz - az),
                Quaternion.identity, sub, Vector3.right, mask);
        }

        static void Solid(Kit k, float x0, float y0, float z0, float x1, float y1, float z1) =>
            k.b.Solid(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));

        /// <summary>Quad in the plane z = const, facing +Z (or -Z).</summary>
        static void QZ(Kit k, float x0, float y0, float x1, float y1, float z, int sub, bool back = false)
        {
            if (Mathf.Abs(x1 - x0) < 1e-4f || Mathf.Abs(y1 - y0) < 1e-4f) return;
            k.b.B.AddQuad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), sub,
                back ? Vector3.back : Vector3.forward);
        }

        /// <summary>Quad in the plane x = const, facing +X or -X.</summary>
        static void QX(Kit k, float x, float y0, float y1, float z0, float z1, int sub, bool plusX)
        {
            if (Mathf.Abs(z1 - z0) < 1e-4f || Mathf.Abs(y1 - y0) < 1e-4f) return;
            k.b.B.AddQuad(new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, y1, z1), new Vector3(x, y1, z0), sub,
                plusX ? Vector3.right : Vector3.left);
        }

        /// <summary>Horizontal quad at height y, facing up (or down).</summary>
        static void QY(Kit k, float x0, float z0, float x1, float z1, float y, int sub, bool up = true)
        {
            if (Mathf.Abs(x1 - x0) < 1e-4f || Mathf.Abs(z1 - z0) < 1e-4f) return;
            k.b.B.AddQuad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), sub,
                up ? Vector3.up : Vector3.down);
        }

        static void Cyl(Kit k, float x, float y0, float z, float r, float h, int segs, int sub, bool capped = true) =>
            k.b.B.AddCylinder(new Vector3(x, y0 + h * 0.5f, z), r, h, segs, sub, capped);

        static void Spread(float a, float c, float spacing, System.Action<float> f)
        {
            float len = c - a;
            if (len <= 0.1f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / spacing));
            for (int i = 0; i < n; i++) f(a + len * (i + 0.5f) / n);
        }

        /// <summary>Draw on a side wall (x = ±xw) as a local frame whose +Z faces out; f gets the local x of each spread position
        /// along z (zA..zB, building frame).</summary>
        static void OnSide(Kit k, bool right, float xw, float zA, float zB, float spacing, System.Action<float> f)
        {
            k.b.BeginLocal(new Vector3(xw, 0f, 0f), Quaternion.Euler(0f, right ? 90f : -90f, 0f));
            Spread(zA, zB, spacing, z => f(right ? -z : z));
            k.b.EndLocal();
        }

        /// <summary>Draw on the back wall (z = zw) as a local frame whose +Z faces -Z; f gets local x of positions along xA..xB.</summary>
        static void OnBack(Kit k, float zw, float xA, float xB, float spacing, System.Action<float> f)
        {
            k.b.BeginLocal(new Vector3(0f, 0f, zw), Quaternion.Euler(0f, 180f, 0f));
            Spread(xA, xB, spacing, x => f(-x));
            k.b.EndLocal();
        }

        // ---- signs ---------------------------------------------------------------------------------------

        /// <summary>Pixel letters fitted into maxW x maxH (bottom at y, centred on cx, protruding +Z from z). Long text wraps to 2 lines.</summary>
        static void Letters(Kit k, string t, float cx, float y, float z, float maxW, float maxH, int sub)
        {
            if (string.IsNullOrEmpty(t) || maxW <= 0.05f || maxH <= 0.02f) return;
            t = t.ToUpperInvariant();
            float w1 = MapText.Width(t, 1f);
            if (w1 < 1e-4f) return;
            float px = Mathf.Min(maxH / 7f, maxW / w1);
            if (px < maxH / 7f * 0.55f && t.IndexOf(' ') > 0)
            {
                int mid = t.Length / 2, cut = -1;
                for (int d = 0; d < t.Length && cut < 0; d++)
                {
                    if (mid - d > 0 && t[mid - d] == ' ') cut = mid - d;
                    else if (mid + d < t.Length - 1 && t[mid + d] == ' ') cut = mid + d;
                }
                if (cut > 0)
                {
                    float lh = maxH * 0.45f;
                    Letters(k, t.Substring(0, cut), cx, y + maxH - lh, z, maxW, lh, sub);
                    Letters(k, t.Substring(cut + 1), cx, y, z, maxW, lh, sub);
                    return;
                }
            }
            if (px < 0.008f) return;
            MapText.Draw(k.b, t, new Vector3(cx, y + (maxH - 7f * px) * 0.5f, z), px, sub);
        }

        // ---- facade parts (draw on a face at z = zf facing +Z in the current frame) ------------------------

        /// <summary>Punched window: backing + glass, frame, muntins/meeting rails, optional sill and head (lintel).</summary>
        static void Window(Kit k, float cx, float y0, float w, float h, float zf, int frame, int cols = 1, int rows = 2,
            int sill = -1, int head = -1, int back = -1)
        {
            float x0 = cx - w * 0.5f, x1 = cx + w * 0.5f, y1 = y0 + h, f = 0.06f, zt = zf + 0.07f;
            QZ(k, x0, y0, x1, y1, zf + 0.005f, back < 0 ? k.dark : back);
            QZ(k, x0, y0, x1, y1, zf + 0.03f, k.glass);
            Bx(k, x0 - f, y0 - f, zf, x0, y1 + f, zt, frame, Shown);
            Bx(k, x1, y0 - f, zf, x1 + f, y1 + f, zt, frame, Shown);
            Bx(k, x0, y1, zf, x1, y1 + f, zt, frame, Shown);
            Bx(k, x0, y0 - f, zf, x1, y0, zt, frame, Shown);
            for (int i = 1; i < cols; i++)
            {
                float x = x0 + w * i / cols;
                Bx(k, x - 0.022f, y0, zf, x + 0.022f, y1, zf + 0.05f, frame, Shown);
            }
            for (int j = 1; j < rows; j++)
            {
                float y = y0 + h * j / rows;
                Bx(k, x0, y - 0.025f, zf, x1, y + 0.025f, zf + 0.06f, frame, Shown);
            }
            if (sill >= 0) Bx(k, x0 - f - 0.06f, y0 - f - 0.09f, zf, x1 + f + 0.06f, y0 - f, zf + 0.11f, sill, NoBack);
            if (head >= 0) Bx(k, x0 - f - 0.1f, y1 + f, zf, x1 + f + 0.1f, y1 + f + 0.24f, zf + 0.045f, head, Shown);
        }

        /// <summary>Round-headed (semicircular arch) window: rectangle w x h plus a half-disc on top.</summary>
        static void ArchWindow(Kit k, float cx, float y0, float w, float h, float zf, int frame, int back = -1, int sill = -1, bool muntins = true)
        {
            float r = w * 0.5f, yS = y0 + h;
            const int seg = 8;
            var arc = new List<Vector3>(seg + 1);
            void Arc(float z, float rr)
            {
                arc.Clear();
                for (int i = 0; i <= seg; i++)
                {
                    float a = Mathf.PI * i / seg;
                    arc.Add(new Vector3(cx + Mathf.Cos(a) * rr, yS + Mathf.Sin(a) * rr, z));
                }
            }
            int bk = back < 0 ? k.dark : back;
            QZ(k, cx - r, y0, cx + r, yS, zf + 0.005f, bk);
            Arc(zf + 0.005f, r); k.b.B.AddPolygon(arc, bk, Vector3.forward);
            QZ(k, cx - r, y0, cx + r, yS, zf + 0.03f, k.glass);
            Arc(zf + 0.03f, r); k.b.B.AddPolygon(arc, k.glass, Vector3.forward);
            Bx(k, cx - r - 0.08f, y0 - 0.06f, zf, cx - r, yS, zf + 0.07f, frame, Shown);
            Bx(k, cx + r, y0 - 0.06f, zf, cx + r + 0.08f, yS, zf + 0.07f, frame, Shown);
            Bx(k, cx - r, y0 - 0.06f, zf, cx + r, y0, zf + 0.07f, frame, Shown);
            for (int i = 0; i < seg; i++)
            {
                float a0 = Mathf.PI * i / seg, a1 = Mathf.PI * (i + 1) / seg, am = (a0 + a1) * 0.5f, rr = r + 0.05f;
                var c = new Vector3(cx + Mathf.Cos(am) * rr, yS + Mathf.Sin(am) * rr, zf + 0.035f);
                float len = 2f * rr * Mathf.Sin((a1 - a0) * 0.5f) + 0.03f;
                k.b.B.AddBox(c, new Vector3(len, 0.11f, 0.07f), Quaternion.Euler(0f, 0f, am * Mathf.Rad2Deg + 90f), frame, Vector3.right, Shown);
            }
            if (muntins)
            {
                QZ(k, cx - 0.018f, y0, cx + 0.018f, yS + r, zf + 0.045f, frame);
                for (float y = y0 + 0.6f; y < yS - 0.1f; y += 0.6f) QZ(k, cx - r, y - 0.018f, cx + r, y + 0.018f, zf + 0.045f, frame);
                QZ(k, cx - r, yS - 0.02f, cx + r, yS + 0.02f, zf + 0.046f, frame);
            }
            if (sill >= 0) Bx(k, cx - r - 0.16f, y0 - 0.16f, zf, cx + r + 0.16f, y0 - 0.06f, zf + 0.12f, sill, NoBack);
        }

        /// <summary>Continuous window band (ribbon) with mullions; each pane gets its own backing for variety.</summary>
        static void Ribbon(Kit k, float x0, float x1, float y0, float h, float zf, int frame, float spacing, int back = -2)
        {
            if (x1 - x0 < 0.5f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / spacing));
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(x0, x1, i / (float)n), c = Mathf.Lerp(x0, x1, (i + 1) / (float)n);
                QZ(k, a, y0, c, y0 + h, zf + 0.005f, back == -2 ? k.Back() : back);
            }
            QZ(k, x0, y0, x1, y0 + h, zf + 0.03f, k.glass);
            Bx(k, x0 - 0.05f, y0 - 0.07f, zf, x1 + 0.05f, y0, zf + 0.09f, frame, NoBack);
            Bx(k, x0 - 0.05f, y0 + h, zf, x1 + 0.05f, y0 + h + 0.07f, zf + 0.07f, frame, Shown);
            for (int i = 0; i <= n; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)n);
                Bx(k, x - 0.035f, y0, zf, x + 0.035f, y0 + h, zf + 0.07f, frame, Shown);
            }
        }

        /// <summary>Door(s) with frame. glassLeaf = aluminium/glass storefront door, else a solid painted leaf. Optional glass transom.</summary>
        static void Door(Kit k, float cx, float y0, float w, float h, float zf, int frame, int leaf, bool glassLeaf, int leaves = 1, float transom = 0f)
        {
            float x0 = cx - w * 0.5f, x1 = cx + w * 0.5f, f = 0.08f, yt = y0 + h;
            float top = yt + (transom > 0f ? transom + f : 0f);
            Bx(k, x0 - f, y0, zf, x0, top + f, zf + 0.08f, frame, Shown);
            Bx(k, x1, y0, zf, x1 + f, top + f, zf + 0.08f, frame, Shown);
            Bx(k, x0, yt, zf, x1, yt + f, zf + 0.08f, frame, Shown);
            if (transom > 0f)
            {
                QZ(k, x0, yt + f, x1, top, zf + 0.005f, k.dark);
                QZ(k, x0, yt + f, x1, top, zf + 0.03f, k.glass);
                Bx(k, x0, top, zf, x1, top + f, zf + 0.08f, frame, Shown);
            }
            float lw = w / leaves;
            for (int i = 0; i < leaves; i++)
            {
                float a = x0 + i * lw, c = a + lw;
                if (glassLeaf)
                {
                    const float st = 0.09f;
                    QZ(k, a, y0, c, yt, zf + 0.005f, k.interior);
                    QZ(k, a, y0, c, yt, zf + 0.025f, k.glass);
                    Bx(k, a, y0, zf, a + st, yt, zf + 0.05f, frame, Shown);
                    Bx(k, c - st, y0, zf, c, yt, zf + 0.05f, frame, Shown);
                    Bx(k, a + st, yt - st, zf, c - st, yt, zf + 0.05f, frame, Shown);
                    Bx(k, a + st, y0, zf, c - st, y0 + 0.25f, zf + 0.05f, frame, Shown);
                    Bx(k, a + st, y0 + 0.95f, zf + 0.05f, c - st, y0 + 1.0f, zf + 0.09f, k.alum, Shown);
                }
                else
                {
                    Bx(k, a + 0.004f, y0, zf, c - 0.004f, yt, zf + 0.045f, leaf, Shown);
                    float hx = leaves == 1 ? c - 0.12f : (i == 0 ? c - 0.1f : a + 0.1f);
                    Bx(k, hx - 0.02f, y0 + 0.95f, zf + 0.045f, hx + 0.02f, y0 + 1.07f, zf + 0.1f, k.alum, Shown);
                }
            }
        }

        /// <summary>Sectional overhead door (garage / service bay), optional row of lites, with jamb trim.</summary>
        static void OverheadDoor(Kit k, float cx, float y0, float w, float h, float zf, int panel, bool lites, int frame)
        {
            float x0 = cx - w * 0.5f, x1 = cx + w * 0.5f;
            Bx(k, x0, y0, zf, x1, y0 + h, zf + 0.04f, panel, Shown);
            const int sec = 4;
            float sh = h / sec;
            for (int i = 1; i < sec; i++) QZ(k, x0, y0 + i * sh - 0.012f, x1, y0 + i * sh + 0.012f, zf + 0.041f, k.dark);
            if (lites)
            {
                int nl = Mathf.Max(3, Mathf.RoundToInt(w / 0.8f));
                float lw = (w - 0.3f) / nl, ly = y0 + sh * 2f + sh * 0.25f;
                for (int i = 0; i < nl; i++)
                {
                    float a = x0 + 0.15f + i * lw + 0.06f;
                    QZ(k, a, ly, a + lw - 0.12f, ly + sh * 0.5f, zf + 0.042f, k.dark);
                    QZ(k, a, ly, a + lw - 0.12f, ly + sh * 0.5f, zf + 0.046f, k.glass);
                }
            }
            Bx(k, x0 - 0.12f, y0, zf, x0, y0 + h + 0.12f, zf + 0.08f, frame, Shown);
            Bx(k, x1, y0, zf, x1 + 0.12f, y0 + h + 0.12f, zf + 0.08f, frame, Shown);
            Bx(k, x0, y0 + h, zf, x1, y0 + h + 0.12f, zf + 0.08f, frame, Shown);
        }

        /// <summary>Full-view aluminium/glass sectional door (fire apparatus bay).</summary>
        static void GlassOverheadDoor(Kit k, float cx, float w, float h, float zf, int frame)
        {
            float x0 = cx - w * 0.5f, x1 = cx + w * 0.5f;
            QZ(k, x0, 0f, x1, h, zf + 0.005f, k.interior);
            QZ(k, x0, 0f, x1, h, zf + 0.03f, k.glass);
            for (int i = 0; i <= 4; i++)
            {
                float y = h * i / 4f;
                Bx(k, x0, Mathf.Max(0f, y - 0.05f), zf, x1, Mathf.Min(h, y + 0.05f), zf + 0.06f, frame, Shown);
            }
            for (int i = 0; i <= 4; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / 4f);
                Bx(k, x - 0.04f, 0f, zf, x + 0.04f, h, zf + 0.055f, frame, Shown);
            }
        }

        /// <summary>Shed awning on a wall: top edge at (yTop, zWall), projecting proj and dropping drop. sub2 ≥ 0 = striped.</summary>
        static void Awning(Kit k, float x0, float x1, float yTop, float zWall, float proj, float drop, int sub, int sub2, float valance = 0.25f)
        {
            var B = k.b.B;
            float yB = yTop - drop, zB = zWall + proj;
            Vector3 n = new Vector3(0f, proj, drop).normalized;
            int ns = sub2 >= 0 ? Mathf.Max(2, Mathf.RoundToInt((x1 - x0) / 0.3f)) : 1;
            for (int i = 0; i < ns; i++)
            {
                float xa = Mathf.Lerp(x0, x1, i / (float)ns), xb = Mathf.Lerp(x0, x1, (i + 1) / (float)ns);
                int m = (i % 2 == 0 || sub2 < 0) ? sub : sub2;
                B.AddQuad(new Vector3(xa, yTop, zWall), new Vector3(xb, yTop, zWall), new Vector3(xb, yB, zB), new Vector3(xa, yB, zB), m, n);
                if (sub2 >= 0) QZ(k, xa, yB - valance, xb, yB, zB + 0.021f, m);
            }
            B.AddQuad(new Vector3(x0, yTop, zWall), new Vector3(x1, yTop, zWall), new Vector3(x1, yB, zB), new Vector3(x0, yB, zB), sub, -n);
            B.AddTriangle(new Vector3(x0, yTop, zWall), new Vector3(x0, yB, zB), new Vector3(x0, yB, zWall), sub, Vector3.left);
            B.AddTriangle(new Vector3(x1, yTop, zWall), new Vector3(x1, yB, zB), new Vector3(x1, yB, zWall), sub, Vector3.right);
            Bx(k, x0, yB - valance, zB - 0.02f, x1, yB, zB + 0.02f, sub);
        }

        // ---- roofs ---------------------------------------------------------------------------------------

        /// <summary>Flat roof membrane + parapet walls around x0..x1, z0 (back)..z1 (front), deck at y, with a coping cap.</summary>
        static void Parapets(Kit k, float x0, float x1, float z0, float z1, float y, float ph, int wall, int cap, float t = 0.25f, bool front = true)
        {
            QY(k, x0, z0, x1, z1, y + 0.02f, k.roof);
            float yt = y + ph;
            if (front) Bx(k, x0, y, z1 - t, x1, yt, z1, wall, NoBottom);
            Bx(k, x0, y, z0, x1, yt, z0 + t, wall, NoBottom);
            Bx(k, x0, y, z0 + t, x0 + t, yt, z1 - t, wall, NoBottom);
            Bx(k, x1 - t, y, z0 + t, x1, yt, z1 - t, wall, NoBottom);
            if (cap < 0) return;
            const float o = 0.03f, ch = 0.08f;
            if (front) Bx(k, x0, yt, z1 - t - o, x1, yt + ch, z1 + o, cap);
            Bx(k, x0, yt, z0 - o, x1, yt + ch, z0 + t + o, cap);
            Bx(k, x0 - o, yt, z0 + t, x0 + t + o, yt + ch, z1 - t, cap);
            Bx(k, x1 - t - o, yt, z0 + t, x1 + o, yt + ch, z1 - t, cap);
        }

        /// <summary>Packaged rooftop HVAC unit on a curb.</summary>
        static void Rtu(Kit k, float cx, float y, float cz, float sx = 2.0f, float sz = 1.3f, float h = 1.05f)
        {
            Bx(k, cx - sx * 0.5f - 0.05f, y, cz - sz * 0.5f - 0.05f, cx + sx * 0.5f + 0.05f, y + 0.25f, cz + sz * 0.5f + 0.05f, k.galv, NoBottom);
            Bx(k, cx - sx * 0.5f, y + 0.25f, cz - sz * 0.5f, cx + sx * 0.5f, y + 0.25f + h, cz + sz * 0.5f, k.unit, NoBottom);
            Cyl(k, cx + sx * 0.22f, y + 0.25f + h, cz, Mathf.Min(0.36f, sz * 0.3f), 0.08f, 10, k.black);
            QZ(k, cx - sx * 0.45f, y + 0.4f, cx - sx * 0.05f, y + 0.12f + h, cz + sz * 0.5f + 0.005f, k.galv);
            QZ(k, cx - sx * 0.45f, y + 0.4f, cx - sx * 0.05f, y + 0.12f + h, cz - sz * 0.5f - 0.005f, k.galv, true);
        }

        /// <summary>Gable roof with the ridge along Z (gable ends face ±Z). Draws both gable triangles in wallSub.</summary>
        static void GableZ(Kit k, float x0, float x1, float z0, float z1, float yE, float rise, float oh, int roofSub, int wallSub, int trimSub)
        {
            var B = k.b.B;
            float cx = (x0 + x1) * 0.5f, hw = (x1 - x0) * 0.5f, sl = rise / hw, yR = yE + rise, yO = yE - oh * sl;
            float za = z0 - oh, zb = z1 + oh;
            Vector3 nL = new Vector3(-rise, hw, 0f).normalized, nR = new Vector3(rise, hw, 0f).normalized;
            Vector3 l0 = new Vector3(x0 - oh, yO, za), l1 = new Vector3(x0 - oh, yO, zb);
            Vector3 r0 = new Vector3(x1 + oh, yO, za), r1 = new Vector3(x1 + oh, yO, zb);
            Vector3 t0 = new Vector3(cx, yR, za), t1 = new Vector3(cx, yR, zb);
            B.AddQuad(l0, l1, t1, t0, roofSub, nL, Vector3.forward);
            B.AddQuad(r0, r1, t1, t0, roofSub, nR, Vector3.forward);
            B.AddQuad(l0, l1, t1, t0, trimSub, -nL, Vector3.forward);
            B.AddQuad(r0, r1, t1, t0, trimSub, -nR, Vector3.forward);
            Bx(k, x0 - oh - 0.04f, yO - 0.2f, za, x0 - oh, yO + 0.02f, zb, trimSub);
            Bx(k, x1 + oh, yO - 0.2f, za, x1 + oh + 0.04f, yO + 0.02f, zb, trimSub);
            B.AddTriangle(new Vector3(x0, yE, z1), new Vector3(x1, yE, z1), new Vector3(cx, yR, z1), wallSub, Vector3.forward);
            B.AddTriangle(new Vector3(x0, yE, z0), new Vector3(x1, yE, z0), new Vector3(cx, yR, z0), wallSub, Vector3.back);
            // rake boards on both gable ends
            float run = hw + oh, len = Mathf.Sqrt(run * run + (yR - yO) * (yR - yO)) + 0.05f, ang = Mathf.Atan2(yR - yO, run) * Mathf.Rad2Deg;
            foreach (float z in new[] { zb - 0.02f, za + 0.02f })
            {
                B.AddBox(new Vector3(cx - run * 0.5f, (yO + yR) * 0.5f - 0.06f, z), new Vector3(len, 0.2f, 0.045f), Quaternion.Euler(0f, 0f, ang), trimSub);
                B.AddBox(new Vector3(cx + run * 0.5f, (yO + yR) * 0.5f - 0.06f, z), new Vector3(len, 0.2f, 0.045f), Quaternion.Euler(0f, 0f, -ang), trimSub);
            }
        }

        /// <summary>Hip roof over a rectangle (with overhang). Returns the ridge height.</summary>
        static float HipRoof(Kit k, float x0, float x1, float z0, float z1, float yE, float pitch, float oh, int roofSub, int soffit)
        {
            var B = k.b.B;
            x0 -= oh; x1 += oh; z0 -= oh; z1 += oh;
            float yO = yE - oh * pitch, w = x1 - x0, d = z1 - z0, cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            var a = new Vector3(x0, yO, z1); var bb = new Vector3(x1, yO, z1);
            var c = new Vector3(x1, yO, z0); var e = new Vector3(x0, yO, z0);
            float yR;
            if (w >= d)
            {
                float h = d * 0.5f; yR = yO + h * pitch;
                var r0 = new Vector3(x0 + h, yR, cz); var r1 = new Vector3(x1 - h, yR, cz);
                B.AddQuad(a, bb, r1, r0, roofSub, new Vector3(0f, 1f, pitch), Vector3.right);
                B.AddQuad(c, e, r0, r1, roofSub, new Vector3(0f, 1f, -pitch), Vector3.right);
                B.AddTriangle(bb, c, r1, roofSub, new Vector3(pitch, 1f, 0f), Vector3.forward);
                B.AddTriangle(e, a, r0, roofSub, new Vector3(-pitch, 1f, 0f), Vector3.forward);
            }
            else
            {
                float h = w * 0.5f; yR = yO + h * pitch;
                var r0 = new Vector3(cx, yR, z0 + h); var r1 = new Vector3(cx, yR, z1 - h);
                B.AddTriangle(a, bb, r1, roofSub, new Vector3(0f, 1f, pitch), Vector3.right);
                B.AddTriangle(c, e, r0, roofSub, new Vector3(0f, 1f, -pitch), Vector3.right);
                B.AddQuad(bb, c, r0, r1, roofSub, new Vector3(pitch, 1f, 0f), Vector3.forward);
                B.AddQuad(e, a, r1, r0, roofSub, new Vector3(-pitch, 1f, 0f), Vector3.forward);
            }
            QY(k, x0, z0, x1, z1, yO, soffit, false);
            Bx(k, x0, yO - 0.18f, z1 - 0.04f, x1, yO, z1, soffit, NoBack);
            Bx(k, x0, yO - 0.18f, z0, x1, yO, z0 + 0.04f, soffit, All & ~MeshBuilder.FacePosZ);
            Bx(k, x0, yO - 0.18f, z0, x0 + 0.04f, yO, z1, soffit, All & ~MeshBuilder.FacePosX);
            Bx(k, x1 - 0.04f, yO - 0.18f, z0, x1, yO, z1, soffit, All & ~MeshBuilder.FaceNegX);
            return yR;
        }

        // ---- classical parts -----------------------------------------------------------------------------

        /// <summary>Doric-ish column: square plinth, torus base, tapered shaft, echinus, abacus. y0 = base, h = total height.</summary>
        static void Column(Kit k, float x, float y0, float z, float h, float d, int sub)
        {
            float r = d * 0.5f;
            Bx(k, x - r - 0.08f, y0, z - r - 0.08f, x + r + 0.08f, y0 + 0.18f, z + r + 0.08f, sub, NoBottom);
            Bx(k, x - r - 0.1f, y0 + h - 0.18f, z - r - 0.1f, x + r + 0.1f, y0 + h, z + r + 0.1f, sub, All & ~MeshBuilder.FacePosY);
            var prof = new[]
            {
                new Vector2(r + 0.06f, 0.18f), new Vector2(r + 0.06f, 0.27f), new Vector2(r, 0.34f),
                new Vector2(r * 0.86f, h - 0.42f), new Vector2(r * 0.86f, h - 0.36f), new Vector2(r + 0.08f, h - 0.18f),
            };
            k.b.B.AddLathe(new Vector3(x, y0, z), Quaternion.identity, prof, 12, sub);
        }

        /// <summary>Triangular pediment (extruded) with raking cornices and an oculus.</summary>
        static void Pediment(Kit k, float cx, float y, float zBack, float zFront, float w, float rise, int stone, int roofSub)
        {
            var tri = new[] { new Vector2(-w * 0.5f, 0f), new Vector2(0f, rise), new Vector2(w * 0.5f, 0f) };
            k.b.B.AddExtrude(tri, new Vector3(cx, y, (zBack + zFront) * 0.5f), Quaternion.identity, zFront - zBack, roofSub, stone);
            float ang = Mathf.Atan2(rise, w * 0.5f) * Mathf.Rad2Deg, len = Mathf.Sqrt(rise * rise + w * w * 0.25f) + 0.1f;
            k.b.B.AddBox(new Vector3(cx - w * 0.25f, y + rise * 0.5f + 0.05f, zFront + 0.07f), new Vector3(len, 0.2f, 0.16f), Quaternion.Euler(0f, 0f, ang), stone);
            k.b.B.AddBox(new Vector3(cx + w * 0.25f, y + rise * 0.5f + 0.05f, zFront + 0.07f), new Vector3(len, 0.2f, 0.16f), Quaternion.Euler(0f, 0f, -ang), stone);
            if (rise > 0.9f)
            {
                k.b.B.AddDisc(new Vector3(cx, y + rise * 0.38f, zFront + 0.012f), Quaternion.Euler(90f, 0f, 0f), rise * 0.17f + 0.06f, 12, stone, true);
                k.b.B.AddDisc(new Vector3(cx, y + rise * 0.38f, zFront + 0.02f), Quaternion.Euler(90f, 0f, 0f), rise * 0.17f, 12, k.dark, true);
            }
        }

        /// <summary>Portico in front of a facade at zf: steps down to zOut, nCol columns, entablature with the sign in the frieze,
        /// pediment on top. Returns the top of the entablature.</summary>
        static float Portico(Kit k, float cx, float w, float zf, float zOut, float floorY, float colH, int nCol, string sign, int stone, int signSub, int pedRoof)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(floorY / 0.17f));
            float rise = floorY / steps, run = 0.3f;
            for (int i = 0; i < steps; i++)
                Bx(k, cx - w * 0.5f - 0.35f, 0f, zf, cx + w * 0.5f + 0.35f, (i + 1) * rise, zOut - i * run, stone, NoBottom);
            float zPlat = zOut - (steps - 1) * run;
            float d = Mathf.Clamp(colH / 9.5f, 0.45f, 0.95f), r = d * 0.5f;
            float zc = zPlat - r - 0.35f;
            for (int i = 0; i < nCol; i++)
            {
                float x = nCol == 1 ? cx : Mathf.Lerp(cx - w * 0.5f + r + 0.2f, cx + w * 0.5f - r - 0.2f, i / (float)(nCol - 1));
                Column(k, x, floorY, zc, colH, d, stone);
            }
            float e0 = floorY + colH, fe = zc + r + 0.15f;
            Bx(k, cx - w * 0.5f - 0.05f, e0, zf, cx + w * 0.5f + 0.05f, e0 + 0.45f, fe, stone, All & ~MeshBuilder.FaceNegZ & ~MeshBuilder.FacePosY);
            Bx(k, cx - w * 0.5f - 0.02f, e0 + 0.45f, zf, cx + w * 0.5f + 0.02f, e0 + 1.0f, fe - 0.04f, stone, Walls & ~MeshBuilder.FaceNegZ);
            Bx(k, cx - w * 0.5f - 0.25f, e0 + 1.0f, zf, cx + w * 0.5f + 0.25f, e0 + 1.25f, fe + 0.25f, stone, NoBack);
            Letters(k, sign, cx, e0 + 0.52f, fe - 0.04f, w - 1.0f, 0.42f, signSub);
            Pediment(k, cx, e0 + 1.25f, zf, fe + 0.25f, w + 0.5f, (w + 0.5f) * 0.5f * 0.27f, stone, pedRoof);
            return e0 + 1.25f;
        }

        /// <summary>Flagpole with concrete base and a US flag hanging toward +X, facing ±Z.</summary>
        static void Flagpole(Kit k, float x, float z, float h)
        {
            Cyl(k, x, 0f, z, 0.45f, 0.25f, 10, k.conc);
            k.b.B.AddFrustum(new Vector3(x, 0.25f + h * 0.5f, z), Quaternion.identity, 0.075f, 0.04f, h, 8, k.alum);
            Cyl(k, x, h + 0.25f, z, 0.07f, 0.12f, 8, k.brass);
            float L = Mathf.Clamp(h * 0.16f, 1.2f, 2.4f), Hf = L / 1.9f, top = h + 0.1f, fx = x + 0.06f, sh = Hf / 13f;
            int red = k.Flat(new Color(0.70f, 0.10f, 0.14f)), blue = k.Flat(new Color(0.12f, 0.16f, 0.40f));
            Bx(k, fx, top - Hf, z - 0.006f, fx + L, top, z + 0.006f, red);
            for (int i = 1; i < 13; i += 2) Bx(k, fx, top - (i + 1) * sh, z - 0.008f, fx + L, top - i * sh, z + 0.008f, k.white);
            Bx(k, fx, top - 7f * sh, z - 0.01f, fx + L * 0.4f, top, z + 0.01f, blue);
        }

        /// <summary>Post lantern (cast-iron post + glowing lantern).</summary>
        static void Lantern(Kit k, float x, float y0, float z, float h = 3.2f)
        {
            k.b.B.AddFrustum(new Vector3(x, y0 + h * 0.5f, z), Quaternion.identity, 0.09f, 0.05f, h, 8, k.black);
            Bx(k, x - 0.17f, y0 + h, z - 0.17f, x + 0.17f, y0 + h + 0.45f, z + 0.17f, k.lit);
            k.b.B.AddFrustum(new Vector3(x, y0 + h + 0.55f, z), Quaternion.identity, 0.26f, 0.03f, 0.2f, 4, k.black);
        }

        /// <summary>Analog clock face facing +Z centred at (x, yc, z), showing 10:10.</summary>
        static void Clock(Kit k, float x, float yc, float z, float r)
        {
            var rot = Quaternion.Euler(90f, 0f, 0f);
            k.b.B.AddDisc(new Vector3(x, yc, z), rot, r * 1.12f, 16, k.black, true);
            k.b.B.AddDisc(new Vector3(x, yc, z + 0.01f), rot, r, 16, k.white, true);
            for (int i = 0; i < 12; i += 3)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                var p = new Vector3(x - Mathf.Sin(a) * r * 0.84f, yc + Mathf.Cos(a) * r * 0.84f, z + 0.02f);
                k.b.B.AddBox(p, new Vector3(r * 0.07f, r * 0.2f, 0.02f), Quaternion.Euler(0f, 0f, i * 30f), k.black, Vector3.right, MeshBuilder.FacePosZ);
            }
            void Hand(float deg, float len, float wid)
            {
                float a = deg * Mathf.Deg2Rad;
                var p = new Vector3(x - Mathf.Sin(a) * len * 0.5f, yc + Mathf.Cos(a) * len * 0.5f, z + 0.03f);
                k.b.B.AddBox(p, new Vector3(wid, len, 0.02f), Quaternion.Euler(0f, 0f, deg), k.black, Vector3.right, MeshBuilder.FacePosZ);
            }
            Hand(305f, r * 0.55f, r * 0.08f);
            Hand(60f, r * 0.82f, r * 0.05f);
        }

        static List<Vector2> RoundedRect(float hx, float hy, float r, int seg)
        {
            r = Mathf.Clamp(r, 0.01f, Mathf.Min(hx, hy) - 0.01f);
            var p = new List<Vector2>(4 * (seg + 1));
            var c = new[] { new Vector2(hx - r, hy - r), new Vector2(-hx + r, hy - r), new Vector2(-hx + r, -hy + r), new Vector2(hx - r, -hy + r) };
            for (int q = 0; q < 4; q++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = (q * 90f + 90f * i / seg) * Mathf.Deg2Rad;
                    p.Add(c[q] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            return p;
        }

        static List<Vector2> HalfDisc(float r, float rise, int seg)
        {
            var p = new List<Vector2>(seg + 1);
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI * i / seg;
                p.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * rise));
            }
            return p;
        }

        // =====================================================================================================
        // storefront — Main Street commercial block
        // =====================================================================================================

        static void Storefront(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(5f, s.Width), D = Mathf.Max(8f, s.Depth), hw = W * 0.5f;
            int n = Mathf.Clamp(s.Stories, 1, 3);
            const float g = 4.6f, fh = 3.8f, zs = -0.25f, zi = -1.6f, zd = -1.45f, yT = 3.35f;
            float H = g + fh * (n - 1), pTop = H + (n == 1 ? 1.3f : 1.0f);
            int look = b.Rng.Next(3);
            int wall = look == 0 ? k.Brick(s.Color) : look == 1 ? k.Brick(BuffBrick) : k.Paint(Pick(b, PaintedBrick));
            int corn = k.Paint(b.R() < 0.2f ? s.Accent : Pick(b, CorniceColors));
            int sash = b.R() < 0.6f ? k.trim : corn;
            float fr = b.R();
            int frame = fr < 0.45f ? k.alum : fr < 0.75f ? k.bronze : k.Paint(s.Accent);
            float br = b.R();
            int bulk = br < 0.4f ? k.Paint(s.Accent) : br < 0.7f ? k.black : wall;
            int sill = b.R() < 0.7f ? k.stone : corn;
            float xs0 = -hw + 0.45f, xs1 = hw - 0.45f;

            // body: main block behind the shop-window zone + facade slab above the storefront, piers at the party walls
            Bx(k, -hw, 0f, -D, hw, H, zi, wall, Walls & ~MeshBuilder.FacePosZ);
            Bx(k, -hw, yT, zi, hw, H, 0f, wall, Walls & ~MeshBuilder.FaceNegZ);
            Bx(k, -hw, 0f, zi, xs0, yT, 0f, wall, Walls & ~MeshBuilder.FaceNegZ);
            Bx(k, xs1, 0f, zi, hw, yT, 0f, wall, Walls & ~MeshBuilder.FaceNegZ);
            Solid(k, -hw, 0f, -D, hw, pTop, 0f);
            Bx(k, -hw, yT, zs, hw, yT + 0.22f, 0.2f, corn, NoBack);              // storefront cornice
            Bx(k, -hw, g - 0.16f, 0f, hw, g, 0.08f, corn, NoBack);              // belt course

            // shop interior seen through the glass
            QZ(k, xs0, 0f, xs1, yT, zi, k.interior);
            QY(k, xs0, zi, xs1, zs, 0.01f, k.Flat(new Color(0.32f, 0.28f, 0.24f)));
            QY(k, xs0, zi, xs1, zs, yT, k.Flat(new Color(0.78f, 0.77f, 0.73f)), false);
            Bx(k, xs0 + 0.4f, yT - 0.09f, zi + 0.3f, xs1 - 0.4f, yT - 0.04f, zi + 0.5f, k.lit, All & ~MeshBuilder.FacePosY);

            // storefront: bulkheads, display glass, transoms, recessed entry
            const float rw = 0.95f;
            float ex = 0f;
            if (xs1 - xs0 > 5.2f) { float r = b.R(); ex = r < 0.3f ? xs0 + rw + 0.7f : r < 0.6f ? xs1 - rw - 0.7f : 0f; }
            float ra = ex - rw, rc = ex + rw;
            foreach (var (a, c) in new[] { (xs0, ra), (rc, xs1) })
            {
                if (c - a < 0.3f) continue;
                Bx(k, a, 0f, zs - 0.06f, c, 0.55f, zs + 0.06f, bulk, Shown);
                QZ(k, a, 0.55f, c, 2.7f, zs, k.glass);
                Bx(k, a, 0.55f, zs - 0.03f, c, 0.62f, zs + 0.06f, frame, Shown);
                Bx(k, a, 2.7f, zs - 0.03f, c, 2.8f, zs + 0.06f, frame, NoBack);
                Bx(k, a, yT - 0.07f, zs - 0.03f, c, yT, zs + 0.06f, frame, Shown);
                QZ(k, a, 2.8f, c, yT - 0.07f, zs, k.glass);
                int m = Mathf.Max(1, Mathf.CeilToInt((c - a) / 1.7f));
                for (int i = 0; i <= m; i++)
                {
                    float x = Mathf.Lerp(a, c, i / (float)m);
                    Bx(k, x - 0.035f, 0.55f, zs - 0.03f, x + 0.035f, yT, zs + 0.06f, frame, Shown);
                }
                int tm = Mathf.Max(1, Mathf.RoundToInt((c - a) / 0.6f));
                for (int i = 1; i < tm; i++)
                {
                    float x = Mathf.Lerp(a, c, i / (float)tm);
                    QZ(k, x - 0.015f, 2.8f, x + 0.015f, yT - 0.07f, zs + 0.02f, frame);
                }
                if (c - a > 1.4f)   // a display table / rack in the window
                {
                    float mx = (a + c) * 0.5f, mh = b.R(0.7f, 1.4f);
                    Bx(k, mx - 0.5f, 0f, zi + 0.05f, mx + 0.5f, mh, zi + 0.55f, k.Flat(Color.HSVToRGB(b.R(), 0.45f, 0.65f)), Shown);
                }
            }
            int tile = k.Flat(new Color(0.42f, 0.40f, 0.37f));
            QY(k, ra, zd, rc, zs, 0.015f, tile);
            QY(k, ra, zd, rc, zs, 2.7f, k.Flat(new Color(0.85f, 0.84f, 0.80f)), false);
            Bx(k, ra, 2.7f, zs - 0.03f, rc, 2.8f, zs + 0.06f, frame, NoBack);
            Bx(k, ra, yT - 0.07f, zs - 0.03f, rc, yT, zs + 0.06f, frame, Shown);
            QZ(k, ra, 2.8f, rc, yT - 0.07f, zs, k.glass);
            Bx(k, ra - 0.06f, 0f, zd, ra, 0.55f, zs, bulk, Walls);
            Bx(k, rc, 0f, zd, rc + 0.06f, 0.55f, zs, bulk, Walls);
            QX(k, ra, 0.55f, 2.7f, zd, zs, k.glass, true);
            QX(k, rc, 0.55f, 2.7f, zd, zs, k.glass, false);
            Bx(k, ra - 0.07f, 0.55f, zd, ra, 2.7f, zd + 0.07f, frame, Walls);
            Bx(k, rc, 0.55f, zd, rc + 0.07f, 2.7f, zd + 0.07f, frame, Walls);
            QZ(k, ra, 0f, rc, 2.7f, zd - 0.04f, k.interior);
            QZ(k, ra, 0f, rc, 2.7f, zd, k.glass);
            Bx(k, ra, 2.13f, zd, rc, 2.21f, zd + 0.06f, frame, Shown);
            Door(k, ex, 0f, 0.92f, 2.13f, zd, frame, frame, true);

            // sign band
            int board = b.R() < 0.5f ? k.Paint(s.Accent) : k.black;
            int letters = b.R() < 0.5f ? k.Paint(new Color(0.95f, 0.82f, 0.50f)) : k.white;
            Bx(k, xs0 + 0.1f, yT + 0.32f, 0f, xs1 - 0.1f, g - 0.28f, 0.04f, board, Shown);
            Letters(k, SignOf(s, ""), 0f, yT + 0.4f, 0.04f, xs1 - xs0 - 0.6f, g - 0.36f - (yT + 0.4f), letters);

            // cloth awning on some
            if (b.R() < 0.55f)
            {
                int aw = k.Flat(s.Accent);
                int aw2 = b.R() < 0.5f ? k.Flat(new Color(0.93f, 0.90f, 0.82f)) : -1;
                Awning(k, xs0, xs1, yT - 0.03f, 0.02f, 1.45f, 0.85f, aw, aw2);
                if (aw2 < 0 && b.R() < 0.6f) Letters(k, SignOf(s, ""), 0f, yT - 0.03f - 0.85f - 0.22f, 1.49f, (xs1 - xs0) * 0.8f, 0.17f, k.white);
            }

            // upper floors: tall double-hung windows with stone sills and lintels
            int nw = W < 7.6f ? (b.R() < 0.5f ? 2 : 3) : W < 9f ? 3 : (b.R() < 0.5f ? 3 : 4);
            float span = W - 1.6f, ww = Mathf.Clamp(span / nw - 0.6f, 0.7f, 1.05f);
            for (int f = 1; f < n; f++)
            {
                float y0 = g + (f - 1) * fh + 0.85f, wh = (f == n - 1 && n == 3) ? 1.85f : 2.05f;
                for (int i = 0; i < nw; i++)
                    Window(k, -hw + 0.8f + span * (i + 0.5f) / nw, y0, ww, wh, 0f, sash, 1, 2, sill, sill, k.Back());
            }

            // parapet + cornice
            Bx(k, -hw, H, -0.3f, hw, pTop, 0f, wall, Walls & ~MeshBuilder.FaceNegZ);
            if (b.R() < 0.65f)
            {   // pressed-metal cornice with brackets
                Bx(k, -hw, pTop - 0.8f, 0f, hw, pTop - 0.25f, 0.07f, corn, NoBack);
                int nb = Mathf.Max(2, Mathf.RoundToInt(W / 0.8f));
                for (int i = 0; i <= nb; i++)
                {
                    float x = Mathf.Lerp(-hw + 0.12f, hw - 0.12f, i / (float)nb);
                    Bx(k, x - 0.08f, pTop - 0.62f, 0.07f, x + 0.08f, pTop - 0.25f, 0.4f, corn, Shown);
                }
                Bx(k, -hw, pTop - 0.25f, -0.3f, hw, pTop, 0.48f, corn, NoBack);
            }
            else
            {   // corbelled brick with a stone coping
                for (int c = 0; c < 3; c++) Bx(k, -hw, pTop - 0.75f + c * 0.15f, 0f, hw, pTop - 0.6f + c * 0.15f, 0.05f + c * 0.05f, wall, NoBack);
                Spread(-hw + 0.2f, hw - 0.2f, 0.45f, x => Bx(k, x - 0.1f, pTop - 0.95f, 0f, x + 0.1f, pTop - 0.75f, 0.06f, wall, Shown));
                Bx(k, -hw, pTop - 0.08f, -0.3f, hw, pTop, 0.2f, k.stone, NoBack);
            }
            Bx(k, -hw, H, -D, -hw + 0.2f, H + 0.6f, -0.3f, wall, NoBottom);
            Bx(k, hw - 0.2f, H, -D, hw, H + 0.6f, -0.3f, wall, NoBottom);
            Bx(k, -hw + 0.2f, H, -D, hw - 0.2f, H + 0.6f, -D + 0.2f, wall, NoBottom);
            QY(k, -hw, -D, hw, -0.3f, H + 0.02f, k.roof);
            if (b.R() < 0.7f) Rtu(k, b.R(-hw + 1.5f, hw - 1.5f), H, -D * b.R(0.35f, 0.7f), 1.6f, 1.1f, 0.9f);

            // back (alley) side: steel door + upper windows
            k.b.BeginLocal(new Vector3(0f, 0f, -D), Quaternion.Euler(0f, 180f, 0f));
            Door(k, hw * 0.4f, 0f, 0.92f, 2.13f, 0f, k.unit, k.unit, false);
            for (int f = 1; f < n; f++)
            {
                float y0 = g + (f - 1) * fh + 0.9f;
                Window(k, -hw * 0.45f, y0, 0.85f, 1.6f, 0f, k.trim, 1, 2, k.stone, -1, k.Back());
                Window(k, hw * 0.45f, y0, 0.85f, 1.6f, 0f, k.trim, 1, 2, k.stone, -1, k.Back());
            }
            k.b.EndLocal();

            // projecting blade sign on some multi-storey buildings
            string word = SignOf(s, "");
            if (n >= 2 && word.Length > 0 && b.R() < 0.35f)
            {
                int sp = word.IndexOf(' ');
                if (sp > 0) word = word.Substring(0, sp);
                float xb = hw - 0.8f, y = g + 0.35f;
                Bx(k, xb - 0.025f, y + 1.05f, 0f, xb + 0.025f, y + 1.1f, 1.3f, k.black);
                Bx(k, xb - 0.04f, y, 0.25f, xb + 0.04f, y + 1.0f, 1.2f, board);
                k.b.BeginLocal(new Vector3(xb + 0.041f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
                Letters(k, word, -0.725f, y + 0.12f, 0f, 0.8f, 0.76f, letters);
                k.b.EndLocal();
                k.b.BeginLocal(new Vector3(xb - 0.041f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
                Letters(k, word, 0.725f, y + 0.12f, 0f, 0.8f, 0.76f, letters);
                k.b.EndLocal();
            }
        }

        // =====================================================================================================
        // diner — stainless roadside diner (long side to the street)
        // =====================================================================================================

        static void Diner(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(10f, s.Width), D = Mathf.Max(7f, s.Depth);
            // a deep lot (an 80-seat diner on a 50'x60' plan) gets a deeper car and a full-width kitchen / dining annex behind it
            bool big = D >= 12f;
            float L = Mathf.Clamp(W - (big ? 0.5f : 1.5f), 9f, 17f), hx = L * 0.5f;
            float bd = Mathf.Clamp(D - 3.0f, 4.2f, big ? 6.4f : 5.2f);
            float zF = big ? -1.3f : -2.4f; const float y0 = 0.75f, rad = 0.7f;
            float zB = zF - bd, cz = (zF + zB) * 0.5f;
            var up = Quaternion.Euler(-90f, 0f, 0f);    // extrude along +Y
            int steel = k.alum;
            int enamel = k.Paint(s.Color);
            int stripe = k.Paint(s.Accent);
            void Band(float ya, float yb, float inset, int sub, bool caps = true)
            {
                var p = RoundedRect(hx - inset, bd * 0.5f - inset, rad - inset, 4);
                b.B.AddExtrude(p, new Vector3(0f, (ya + yb) * 0.5f, cz), up, yb - ya, sub, sub, caps);
            }
            Band(0f, y0, 0.12f, k.b.K.M(SurfaceKind.ConcreteBlock, new Color(0.6f, 0.6f, 0.6f)), false);
            Band(y0, 1.55f, 0f, steel);
            Band(1.0f, 1.08f, -0.012f, stripe);
            Band(1.3f, 1.38f, -0.012f, stripe);
            Band(1.55f, 1.62f, -0.03f, steel);
            Band(1.62f, 2.45f, 0.14f, k.interior, false);
            Band(1.62f, 2.45f, 0.03f, k.glass, false);
            Band(2.45f, 3.0f, 0f, enamel);
            Band(2.98f, 3.06f, -0.1f, steel);
            Solid(k, -hx, 0f, zB, hx, 3.6f, zF);
            // glass-band mullions (front straight run and the ends)
            for (float x = -hx + rad; x <= hx - rad + 0.01f; x += (L - 2f * rad) / Mathf.Max(1, Mathf.RoundToInt((L - 2f * rad) / 0.95f)))
                Bx(k, x - 0.03f, 1.62f, zF - 0.06f, x + 0.03f, 2.45f, zF + 0.01f, steel, Shown);
            foreach (float sx in new[] { -1f, 1f })
                for (int i = 0; i < 3; i++)
                {
                    float z = Mathf.Lerp(zB + rad, zF - rad, i / 2f);
                    Bx(k, sx * hx - 0.04f, 1.62f, z - 0.03f, sx * hx + 0.04f, 2.45f, z + 0.03f, steel);
                }
            // lit interior: ceiling light strip + counter
            Bx(k, -hx + 1f, 2.36f, cz - 0.3f, hx - 1f, 2.42f, cz + 0.3f, k.lit);
            Bx(k, -hx + 1.2f, y0, cz - 0.55f, hx - 1.2f, y0 + 1.05f, cz - 0.1f, k.Paint(new Color(0.75f, 0.2f, 0.18f)));
            // barrel roof
            var arch = HalfDisc(bd * 0.5f + 0.05f, 0.55f, 8);
            b.B.AddExtrude(arch, new Vector3(0f, 3.06f, cz), Quaternion.Euler(0f, 90f, 0f), L - rad * 1.2f, k.Paint(new Color(0.62f, 0.64f, 0.66f)), steel);
            // vestibule + steps
            Bx(k, -1.1f, 0f, zF - 0.1f, 1.1f, 2.95f, zF + 1.4f, steel, Walls);
            Bx(k, -1.2f, 2.95f, zF - 0.1f, 1.2f, 3.12f, zF + 1.5f, steel, NoBottom);
            Bx(k, -1.1f, 2.82f, zF + 1.4f, 1.1f, 2.95f, zF + 1.43f, enamel, Shown);
            Door(k, 0f, y0, 0.9f, 1.98f, zF + 1.4f, steel, steel, true);
            for (int i = 0; i < 3; i++)
                Bx(k, -1.0f, 0f, zF + 1.4f, 1.0f, (i + 1) * 0.25f, zF + 1.4f + (3 - i) * 0.28f, k.conc, NoBottom);
            foreach (float sx in new[] { -1.05f, 1.05f })
                b.B.AddBox(new Vector3(sx, 0.95f, zF + 1.4f + 0.45f), new Vector3(0.04f, 0.04f, 1.0f), Quaternion.Euler(-28f, 0f, 0f), steel);
            // kitchen annex behind
            float kd = D + zB;   // depth left behind the car
            if (kd > 1.6f)
            {
                int cmu = k.b.K.M(SurfaceKind.ConcreteBlock, new Color(0.95f, 0.95f, 0.92f));
                float kx0 = big ? -hx + 0.2f : -hx + 1.0f, kx1 = big ? hx - 0.2f : Mathf.Min(hx - 1.0f, kx0 + Mathf.Max(5f, L * 0.55f));
                Bx(k, kx0, 0f, -D + 0.2f, kx1, 3.4f, zB + 0.3f, cmu, Walls);
                Parapets(k, kx0, kx1, -D + 0.2f, zB + 0.3f, 3.4f, 0.3f, cmu, k.alum, 0.2f);
                Cyl(k, kx1 - 1.2f, 3.4f, (-D + zB) * 0.5f, 0.35f, 0.7f, 10, k.galv);
                Rtu(k, kx0 + 1.6f, 3.4f, (-D + zB) * 0.5f, 1.4f, 1.0f, 0.8f);
                k.b.BeginLocal(new Vector3(0f, 0f, -D + 0.2f), Quaternion.Euler(0f, 180f, 0f));
                Door(k, -(kx0 + kx1) * 0.5f, 0f, 0.92f, 2.13f, 0f, k.unit, k.unit, false);
                k.b.EndLocal();
                Solid(k, kx0, 0f, -D + 0.2f, kx1, 3.7f, zB + 0.3f);
            }
            // neon rooftop sign
            string txt = SignOf(s, "DINER");
            int neon = k.Glow(Pick(b, new[] { new Color(1f, 0.25f, 0.35f), new Color(0.3f, 0.85f, 1f), new Color(1f, 0.55f, 0.15f), new Color(0.4f, 1f, 0.45f) }));
            float sw = Mathf.Min(L * 0.6f, 1.2f + MapText.Width(txt, 0.11f));
            float sz0 = zF - 0.45f;
            Bx(k, -sw * 0.5f, 3.75f, sz0, sw * 0.5f, 4.85f, sz0 + 0.18f, k.Paint(new Color(0.12f, 0.14f, 0.2f)));
            Bx(k, -sw * 0.5f + 0.06f, 4.74f, sz0 + 0.18f, sw * 0.5f - 0.06f, 4.78f, sz0 + 0.22f, neon);
            Bx(k, -sw * 0.5f + 0.06f, 3.82f, sz0 + 0.18f, sw * 0.5f - 0.06f, 3.86f, sz0 + 0.22f, neon);
            Bx(k, -sw * 0.5f + 0.06f, 3.82f, sz0 + 0.18f, -sw * 0.5f + 0.1f, 4.78f, sz0 + 0.22f, neon);
            Bx(k, sw * 0.5f - 0.1f, 3.82f, sz0 + 0.18f, sw * 0.5f - 0.06f, 4.78f, sz0 + 0.22f, neon);
            Letters(k, txt, 0f, 3.96f, sz0 + 0.18f, sw - 0.5f, 0.66f, neon);
            foreach (float px in new[] { -sw * 0.35f, sw * 0.35f }) Bx(k, px - 0.05f, 3.3f, sz0 + 0.04f, px + 0.05f, 3.76f, sz0 + 0.14f, k.black);
            // enamel band lettering
            Letters(k, "BREAKFAST - LUNCH - DINNER", 0f, 2.55f, zF, L - 2.5f, 0.34f, stripe);
        }

        // =====================================================================================================
        // bank — classical portico
        // =====================================================================================================

        static void Bank(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(10f, s.Width), D = Mathf.Max(10f, s.Depth), hw = W * 0.5f - 0.05f;
            int n = Mathf.Clamp(s.Stories, 1, 2);
            const float yF = 0.85f, pd = 4.2f, zf = -pd;
            float H = n == 1 ? 7.2f : 8.8f;
            bool stoneFace = b.R() < 0.35f;
            int wall = stoneFace ? k.stone : k.Brick(s.Color);
            int stone = k.stone;
            Bx(k, -hw, 0f, -D, hw, H, zf, wall, Walls);
            Solid(k, -hw, 0f, -D, hw, H + 0.9f, zf);
            Bx(k, -hw - 0.04f, 0f, -D, hw + 0.04f, yF + 0.2f, zf + 0.04f, stone, Walls);    // water table
            Bx(k, -hw - 0.04f, H - 0.55f, -D, hw + 0.04f, H - 0.2f, zf + 0.12f, stone, Walls);
            Bx(k, -hw - 0.04f, H - 0.2f, -D, hw + 0.04f, H, zf + 0.3f, stone, All & ~MeshBuilder.FaceNegY & ~MeshBuilder.FacePosY);
            QY(k, -hw - 0.04f, -D, hw + 0.04f, zf + 0.3f, H - 0.2f, stone, false);
            Parapets(k, -hw, hw, -D, zf, H, 0.9f, wall, stone);
            if (!stoneFace)
                for (float y = yF + 0.2f; y < H - 0.6f; y += 0.6f)
                {
                    bool longB = Mathf.RoundToInt((y - yF) / 0.6f) % 2 == 0;
                    float qw = longB ? 0.55f : 0.32f;
                    Bx(k, -hw - 0.03f, y, zf - qw, -hw + qw, y + 0.52f, zf + 0.03f, stone, Walls);
                    Bx(k, hw - qw, y, zf - qw, hw + 0.03f, y + 0.52f, zf + 0.03f, stone, Walls);
                }
            float pw = Mathf.Clamp(W * 0.6f, 6f, 11f);
            float colH = H - yF - 1.25f;
            Portico(k, 0f, pw, zf, 0f, yF, colH, 4, SignOf(s, "BANK"), stone, k.bronze, stone);
            Door(k, 0f, yF, 1.8f, 2.6f, zf, k.bronze, k.bronze, true, 2, 0.9f);
            Bx(k, -1.3f, yF, zf, -1.0f, yF + 3.8f, zf + 0.1f, stone, Shown);
            Bx(k, 1.0f, yF, zf, 1.3f, yF + 3.8f, zf + 0.1f, stone, Shown);
            Bx(k, -1.45f, yF + 3.8f, zf, 1.45f, yF + 4.15f, zf + 0.16f, stone, Shown);
            float wh = n == 1 ? H - yF - 2.4f : 2.6f;
            foreach (float x in new[] { -pw * 0.27f, pw * 0.27f })
                Window(k, x, yF + 0.8f, 1.2f, wh, zf, k.bronze, 2, 3, stone, stone, k.Back());
            if (hw - pw * 0.5f > 2.2f)
                foreach (float sx in new[] { -1f, 1f })
                {
                    float x = sx * (pw * 0.5f + (hw - pw * 0.5f) * 0.5f + 0.2f);
                    Window(k, x, yF + 0.8f, 1.3f, wh, zf, k.bronze, 2, 3, stone, stone, k.Back());
                    if (n == 2) Window(k, x, yF + 4.6f, 1.3f, 1.9f, zf, k.bronze, 2, 2, stone, stone, k.Back());
                }
            foreach (bool right in new[] { false, true })
                OnSide(k, right, right ? hw : -hw, -D + 1f, zf - 1f, 3.4f, lx =>
                {
                    Window(k, lx, yF + 0.8f, 1.3f, wh, 0f, k.bronze, 2, 3, stone, stone, k.Back());
                    if (n == 2) Window(k, lx, yF + 4.6f, 1.3f, 1.9f, 0f, k.bronze, 2, 2, stone, stone, k.Back());
                });
            Rtu(k, 0f, H, -D * 0.6f);
            foreach (float x in new[] { -pw * 0.5f - 0.8f, pw * 0.5f + 0.8f })
                if (Mathf.Abs(x) < hw - 0.2f) Lantern(k, x, 0f, -0.5f, 3.0f);
        }

        // =====================================================================================================
        // office — 2-3 storey office building
        // =====================================================================================================

        static void Office(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(10f, s.Width), D = Mathf.Max(10f, s.Depth), hw = W * 0.5f;
            int n = Mathf.Clamp(s.Stories < 2 ? 2 : s.Stories, 2, 3);
            const float g = 4.2f, fh = 3.7f;
            float H = g + fh * (n - 1);
            bool stucco = b.R() < 0.45f;
            int wall = stucco ? k.b.K.M(MapMaterials.Stucco(s.Color)) : k.Brick(s.Color);
            bool ribbon = b.R() < 0.5f;
            int frame = b.R() < 0.5f ? k.alum : k.bronze;
            int spandrel = stucco ? k.Paint(s.Accent) : wall;
            Bx(k, -hw, 0f, -D, hw, H, 0f, wall, Walls);
            Solid(k, -hw, 0f, -D, hw, H + 0.8f, 0f);
            Parapets(k, -hw, hw, -D, 0f, H, 0.8f, wall, k.alum);
            const float ew = 3.4f;   // entrance width
            for (int f = 0; f < n; f++)
            {
                float yb = f == 0 ? 0f : g + (f - 1) * fh, sy = yb + (f == 0 ? 0.75f : 0.85f), wh = f == 0 ? 2.5f : 1.85f;
                if (ribbon)
                {
                    if (f == 0)
                    {
                        Ribbon(k, -hw + 0.9f, -ew * 0.5f - 0.6f, sy, wh, 0f, frame, 1.5f);
                        Ribbon(k, ew * 0.5f + 0.6f, hw - 0.9f, sy, wh, 0f, frame, 1.5f);
                    }
                    else Ribbon(k, -hw + 0.9f, hw - 0.9f, sy, wh, 0f, frame, 1.5f);
                    if (stucco) Bx(k, -hw, sy + wh + 0.07f, 0f, hw, sy + wh + 0.2f, 0.06f, spandrel, Shown);
                }
                else
                {
                    Spread(-hw + 0.6f, hw - 0.6f, 2.4f, x =>
                    {
                        if (f == 0 && Mathf.Abs(x) < ew * 0.5f + 0.9f) return;
                        Window(k, x, sy, 1.5f, wh, 0f, frame, 2, 1, k.stone, stucco ? -1 : k.stone, k.Back());
                    });
                }
                float fsy = sy, fwh = wh;
                foreach (bool right in new[] { false, true })
                {
                    if (ribbon)
                    {
                        k.b.BeginLocal(new Vector3(right ? hw : -hw, 0f, 0f), Quaternion.Euler(0f, right ? 90f : -90f, 0f));
                        float a = right ? 0.9f : -D + 0.9f, c = right ? D - 0.9f : -0.9f;
                        Ribbon(k, a, c, fsy, fwh, 0f, frame, 1.5f);
                        k.b.EndLocal();
                    }
                    else OnSide(k, right, right ? hw : -hw, -D + 0.6f, -0.6f, 2.4f, lx => Window(k, lx, fsy, 1.5f, fwh, 0f, frame, 2, 1, k.stone, -1, k.Back()));
                }
            }
            // entrance: glass vestibule front + canopy with sign
            QZ(k, -ew * 0.5f, 0f, ew * 0.5f, 3.2f, 0.005f, k.interior);
            QZ(k, -ew * 0.5f, 0f, ew * 0.5f, 3.2f, 0.03f, k.glass);
            Bx(k, -ew * 0.5f - 0.08f, 0f, 0f, -ew * 0.5f, 3.2f, 0.08f, frame, Shown);
            Bx(k, ew * 0.5f, 0f, 0f, ew * 0.5f + 0.08f, 3.2f, 0.08f, frame, Shown);
            Bx(k, -ew * 0.5f, 2.35f, 0f, ew * 0.5f, 2.43f, 0.08f, frame, Shown);
            Door(k, 0f, 0f, 1.85f, 2.3f, 0.01f, frame, frame, true, 2);
            float proj = s.FrontYard > 0.5f ? Mathf.Min(1.8f, s.FrontYard * 0.5f) : 1.6f;
            int canopy = b.R() < 0.5f ? k.alum : k.Paint(s.Accent);
            Bx(k, -2.6f, 3.3f, 0f, 2.6f, 3.62f, proj, canopy);
            Letters(k, SignOf(s, ""), 0f, 3.33f, proj, 4.8f, 0.24f, canopy == k.alum ? k.bronze : k.white);
            foreach (float x in new[] { -2.35f, 2.35f }) Cyl(k, x, 0f, proj - 0.25f, 0.07f, 3.3f, 8, canopy == k.alum ? k.alum : k.black);
            QY(k, -2.4f, 0.1f, 2.4f, proj - 0.1f, 3.295f, k.lit, false);
            Letters(k, SignOf(s, ""), 0f, H + 0.1f, 0f, W * 0.5f, 0.55f, stucco ? k.bronze : k.white);
            Rtu(k, -hw * 0.4f, H, -D * 0.55f);
            Rtu(k, hw * 0.35f, H, -D * 0.4f, 1.6f, 1.1f, 0.9f);
        }

        // =====================================================================================================
        // townhall — brick civic building with portico, clock cupola, flagpole
        // =====================================================================================================

        static void TownHall(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(14f, s.Width), D = Mathf.Max(12f, s.Depth), hw = W * 0.5f - 0.05f;
            const float yF = 1.0f, f1 = 4.2f, pd = 5.4f, zf = -pd;
            float H = yF + f1 + 4.0f;
            int wall = k.Brick(s.Color), stone = k.stone;
            int slate = k.b.K.M(MapMaterials.Shingle(new Color(0.5f, 0.52f, 0.56f)));
            Bx(k, -hw, 0f, -D, hw, H, zf, wall, Walls);
            Solid(k, -hw, 0f, -D, hw, H + 2f, zf);
            Bx(k, -hw - 0.04f, 0f, -D, hw + 0.04f, yF + 0.2f, zf + 0.05f, stone, Walls);
            Bx(k, -hw - 0.03f, yF + f1 - 0.15f, -D, hw + 0.03f, yF + f1 + 0.1f, zf + 0.05f, stone, Walls);
            Bx(k, -hw - 0.05f, H - 0.45f, -D, hw + 0.05f, H, zf + 0.15f, stone, Walls);
            float zc = (-D + zf) * 0.5f;
            float yR = HipRoof(k, -hw, hw, -D, zf, H, 0.42f, 0.35f, slate, k.trim);
            // portico
            float pw = Mathf.Clamp(W * 0.45f, 8f, 14f);
            int nCol = pw > 10.5f ? 6 : 4;
            Portico(k, 0f, pw, zf, 0f, yF, H - yF - 1.25f, nCol, SignOf(s, "TOWN HALL"), stone, k.bronze, stone);
            // entry + windows behind the portico
            Door(k, 0f, yF, 1.9f, 2.7f, zf, k.trim, k.Paint(new Color(0.2f, 0.28f, 0.22f)), false, 2, 0.8f);
            Bx(k, -1.35f, yF + 3.8f, zf, 1.35f, yF + 4.0f, zf + 0.1f, stone, Shown);
            foreach (float x in new[] { -pw * 0.3f, pw * 0.3f })
            {
                Window(k, x, yF + 0.8f, 1.2f, 2.4f, zf, k.trim, 2, 2, stone, stone, k.Back());
                Window(k, x, yF + f1 + 0.7f, 1.2f, 2.1f, zf, k.trim, 2, 2, stone, stone, k.Back());
            }
            Window(k, 0f, yF + f1 + 0.7f, 1.2f, 2.1f, zf, k.trim, 2, 2, stone, stone, k.Back());
            // wings
            float wingA = pw * 0.5f + 0.6f;
            foreach (float sx in new[] { -1f, 1f })
                Spread(wingA, hw - 0.8f, 2.6f, x =>
                {
                    Window(k, sx * x, yF + 0.8f, 1.2f, 2.4f, zf, k.trim, 2, 2, stone, stone, k.Back());
                    Window(k, sx * x, yF + f1 + 0.7f, 1.2f, 2.1f, zf, k.trim, 2, 2, stone, stone, k.Back());
                });
            foreach (bool right in new[] { false, true })
                OnSide(k, right, right ? hw : -hw, -D + 0.8f, zf - 0.8f, 3.0f, lx =>
                {
                    Window(k, lx, yF + 0.8f, 1.2f, 2.4f, 0f, k.trim, 2, 2, stone, stone, k.Back());
                    Window(k, lx, yF + f1 + 0.7f, 1.2f, 2.1f, 0f, k.trim, 2, 2, stone, stone, k.Back());
                });
            OnBack(k, -D, -hw + 1f, hw - 1f, 3.2f, lx =>
            {
                Window(k, lx, yF + 0.8f, 1.2f, 2.4f, 0f, k.trim, 2, 2, stone, -1, k.Back());
                Window(k, lx, yF + f1 + 0.7f, 1.2f, 2.1f, 0f, k.trim, 2, 2, stone, -1, k.Back());
            });
            // clock cupola on the ridge
            int white = k.trim;
            float cs = 2.6f, h = cs * 0.5f, cb = yR - 0.7f;
            Bx(k, -h, cb, zc - h, h, yR + 0.9f, zc + h, white, Walls);
            Bx(k, -h - 0.12f, yR + 0.9f, zc - h - 0.12f, h + 0.12f, yR + 1.05f, zc + h + 0.12f, white);
            float h2 = h * 0.86f, ycl0 = yR + 1.05f, ycl1 = ycl0 + 2.0f;
            Bx(k, -h2, ycl0, zc - h2, h2, ycl1, zc + h2, white, Walls);
            Bx(k, -h2 - 0.1f, ycl1, zc - h2 - 0.1f, h2 + 0.1f, ycl1 + 0.15f, zc + h2 + 0.1f, white);
            float yb0 = ycl1 + 0.15f, yb1 = yb0 + 1.8f, h3 = h2 * 0.8f;
            Bx(k, -h3 + 0.2f, yb0, zc - h3 + 0.2f, h3 - 0.2f, yb1, zc + h3 - 0.2f, k.dark, Walls);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Bx(k, sx * h3 - 0.14f, yb0, zc + sz * h3 - 0.14f, sx * h3 + 0.14f, yb1, zc + sz * h3 + 0.14f, white, Walls);
            Bx(k, -h3 - 0.05f, yb0, zc - h3 - 0.05f, h3 + 0.05f, yb0 + 0.12f, zc + h3 + 0.05f, white);
            for (int f = 0; f < 4; f++)
            {
                k.b.BeginLocal(new Vector3(0f, 0f, zc), Quaternion.Euler(0f, f * 90f, 0f));
                Clock(k, 0f, (ycl0 + ycl1) * 0.5f, h2 + 0.005f, 0.72f);
                Bx(k, -h3, yb0 + 0.12f, h3 - 0.04f, h3, yb0 + 0.75f, h3 + 0.02f, white, Shown);   // belfry rail panel
                k.b.EndLocal();
            }
            Bx(k, -h3 - 0.2f, yb1, zc - h3 - 0.2f, h3 + 0.2f, yb1 + 0.2f, zc + h3 + 0.2f, white);
            var dome = new[]
            {
                new Vector2(h3 + 0.05f, 0f), new Vector2(h3, 0.35f), new Vector2(h3 * 0.78f, 0.9f), new Vector2(h3 * 0.45f, 1.3f),
                new Vector2(0.12f, 1.55f), new Vector2(0.08f, 1.9f), new Vector2(0.01f, 2.0f),
            };
            k.b.B.AddLathe(new Vector3(0f, yb1 + 0.2f, zc), Quaternion.Euler(0f, 22.5f, 0f), dome, 8, k.b.K.M(MapMaterials.Flat(new Color(0.36f, 0.56f, 0.48f))));
            float yv = yb1 + 2.2f;
            Bx(k, -0.02f, yv, zc - 0.02f, 0.02f, yv + 1.0f, zc + 0.02f, k.black);
            Bx(k, -0.45f, yv + 0.7f, zc - 0.015f, 0.45f, yv + 0.74f, zc + 0.015f, k.black);
            Bx(k, 0.25f, yv + 0.6f, zc - 0.01f, 0.45f, yv + 0.84f, zc + 0.01f, k.black);
            // flagpole + lanterns
            float fx = Mathf.Min(hw - 0.6f, Mathf.Max(pw * 0.5f + 1.6f, hw - 2.5f));
            Flagpole(k, fx, -1.0f, 10.5f);
            foreach (float x in new[] { -pw * 0.5f - 0.7f, pw * 0.5f + 0.7f }) Lantern(k, x, 0f, -0.4f, 3.0f);
        }

        // =====================================================================================================
        // church — gable nave + front steeple
        // =====================================================================================================

        static void Church(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(9f, s.Width), D = Mathf.Max(16f, s.Depth);
            bool clap = b.R() < 0.5f;
            int wall = clap ? k.b.K.M(MapMaterials.Siding(new Color(0.96f, 0.96f, 0.94f))) : k.Brick(s.Color);
            int trim = k.trim;
            int roofS = k.b.K.M(MapMaterials.Shingle(new Color(0.52f, 0.52f, 0.56f)));
            int spire = clap ? trim : k.b.K.M(MapMaterials.Flat(new Color(0.34f, 0.54f, 0.46f)));
            int glassBack = k.Flat(new Color(0.22f, 0.17f, 0.32f));
            float nw = Mathf.Min(W - 1f, 13f), nx = nw * 0.5f;
            float tw = Mathf.Clamp(nw * 0.36f, 3.6f, 4.8f), th = tw * 0.5f;
            const float zT = -1.2f, yF = 0.6f;
            float zN = zT - tw * 0.55f, zBk = -D + 0.3f;
            const float yE = 6.8f;
            float rise = nx * 0.95f, yR = yE + rise;
            // nave
            Bx(k, -nx, 0f, zBk, nx, yE, zN, wall, Walls);
            Solid(k, -nx, 0f, zBk, nx, yE, zN);
            Bx(k, -nx - 0.05f, 0f, zBk - 0.05f, nx + 0.05f, 0.6f, zN + 0.05f, k.stone, Walls);
            GableZ(k, -nx, nx, zBk, zN, yE, rise, 0.45f, roofS, wall, trim);
            if (clap)
            {
                Bx(k, -nx - 0.04f, yE - 0.35f, zBk - 0.04f, nx + 0.04f, yE, zN + 0.04f, trim, Walls);
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float z in new[] { zBk, zN })
                        Bx(k, sx * nx - 0.12f, 0.6f, z - 0.12f, sx * nx + 0.12f, yE - 0.35f, z + 0.12f, trim, Walls);
            }
            foreach (bool right in new[] { false, true })
                OnSide(k, right, right ? nx : -nx, zBk + 1.2f, zN - 1.2f, 3.6f,
                    lx => ArchWindow(k, lx, 1.5f, 1.3f, 3.2f, 0f, trim, glassBack, k.stone));
            // nave front, either side of the tower
            if (nx - th > 1.3f)
                foreach (float sx in new[] { -1f, 1f })
                    ArchWindow(k, sx * (th + (nx - th) * 0.5f), 1.5f, 0.9f, 2.4f, zN, trim, glassBack, k.stone);
            k.b.BeginLocal(new Vector3(0f, 0f, zBk), Quaternion.Euler(0f, 180f, 0f));
            k.b.B.AddDisc(new Vector3(0f, yE + rise * 0.35f, 0.01f), Quaternion.Euler(90f, 0f, 0f), 0.75f, 12, trim, true);
            k.b.B.AddDisc(new Vector3(0f, yE + rise * 0.35f, 0.02f), Quaternion.Euler(90f, 0f, 0f), 0.6f, 12, glassBack, true);
            Door(k, nx * 0.5f, 0f, 0.92f, 2.13f, 0f, trim, trim, false);
            k.b.EndLocal();
            // tower
            float tz = zT - th, yT1 = yR + 1.2f;
            Bx(k, -th, 0f, zT - tw, th, yT1, zT, wall, Walls);
            Solid(k, -th, 0f, zT - tw, th, yT1, zT);
            Bx(k, -th - 0.05f, 0f, zT - tw - 0.05f, th + 0.05f, 0.6f, zT + 0.05f, k.stone, Walls);
            Bx(k, -th - 0.12f, yT1 - 0.3f, zT - tw - 0.12f, th + 0.12f, yT1, zT + 0.12f, trim);
            if (clap)
                foreach (float sx in new[] { -1f, 1f })
                    Bx(k, sx * th - 0.12f, 0.6f, zT - 0.12f, sx * th + 0.12f, yT1 - 0.3f, zT + 0.12f, trim, Walls);
            for (int i = 0; i < 3; i++)
                Bx(k, -1.4f, 0f, zT, 1.4f, (i + 1) * 0.2f, zT + (3 - i) * 0.3f, k.conc, NoBottom);
            int doorC = b.R() < 0.6f ? k.Paint(new Color(0.55f, 0.10f, 0.08f)) : k.b.K.M(SurfaceKind.Oak);
            Door(k, 0f, yF, 1.7f, 2.5f, zT, trim, doorC, false, 2);
            ArchWindow(k, 0f, yF + 2.6f, 1.7f, 0.01f, zT, trim, glassBack, -1, false);
            ArchWindow(k, 0f, yE - 1.2f, 1.0f, 1.8f, zT, trim, glassBack, k.stone);
            if (b.R() < 0.5f) Clock(k, 0f, yR - 0.6f, zT + 0.01f, 0.75f);
            else
            {
                k.b.B.AddDisc(new Vector3(0f, yR - 0.6f, zT + 0.01f), Quaternion.Euler(90f, 0f, 0f), 0.85f, 14, trim, true);
                k.b.B.AddDisc(new Vector3(0f, yR - 0.6f, zT + 0.02f), Quaternion.Euler(90f, 0f, 0f), 0.7f, 14, glassBack, true);
            }
            // belfry with louvers
            float bh = th * 0.82f, yb0 = yT1, yb1 = yb0 + 3.2f;
            Bx(k, -bh, yb0, tz - bh, bh, yb1, tz + bh, wall, Walls);
            for (int f = 0; f < 4; f++)
            {
                k.b.BeginLocal(new Vector3(0f, 0f, tz), Quaternion.Euler(0f, f * 90f, 0f));
                float ow = bh * 2f - 0.9f, z = bh + 0.005f, ys = yb0 + 2.3f;
                QZ(k, -ow * 0.5f, yb0 + 0.35f, ow * 0.5f, ys, z, k.dark);
                var arc = new List<Vector3>();
                for (int i = 0; i <= 8; i++) { float a = Mathf.PI * i / 8; arc.Add(new Vector3(Mathf.Cos(a) * ow * 0.5f, ys + Mathf.Sin(a) * ow * 0.5f, z)); }
                k.b.B.AddPolygon(arc, k.dark, Vector3.forward);
                for (float y = yb0 + 0.45f; y < ys; y += 0.27f)
                    k.b.B.AddBox(new Vector3(0f, y, bh - 0.05f), new Vector3(ow, 0.025f, 0.2f), Quaternion.Euler(-35f, 0f, 0f), trim, Vector3.right, Shown);
                Bx(k, -bh - 0.02f, yb0, bh - 0.2f, -bh + 0.25f, yb1, bh + 0.06f, trim, Shown);
                Bx(k, bh - 0.25f, yb0, bh - 0.2f, bh + 0.02f, yb1, bh + 0.06f, trim, Shown);
                k.b.EndLocal();
            }
            Bx(k, -bh - 0.15f, yb1, tz - bh - 0.15f, bh + 0.15f, yb1 + 0.3f, tz + bh + 0.15f, trim);
            float ys0 = yb1 + 0.3f, sh = tw * 2.8f, r0 = bh * 1.06f;
            k.b.B.AddFrustum(new Vector3(0f, ys0 + sh * 0.5f, tz), Quaternion.Euler(0f, 22.5f, 0f), r0, 0.04f, sh, 8, spire, false);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    k.b.B.AddFrustum(new Vector3(sx * (bh - 0.05f), ys0 + 0.6f, tz + sz * (bh - 0.05f)), Quaternion.Euler(0f, 45f, 0f), 0.17f, 0.02f, 1.2f, 4, trim, false);
            float yx = ys0 + sh;
            Bx(k, -0.045f, yx - 0.1f, tz - 0.045f, 0.045f, yx + 1.1f, tz + 0.045f, k.brass);
            Bx(k, -0.32f, yx + 0.6f, tz - 0.045f, 0.32f, yx + 0.7f, tz + 0.045f, k.brass);
        }

        // =====================================================================================================
        // postoffice — 1-storey federal-style brick post office
        // =====================================================================================================

        static void PostOffice(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(12f, s.Width), D = Mathf.Max(12f, s.Depth), hw = W * 0.5f - 0.1f;
            const float yF = 0.9f;
            float zf = -Mathf.Clamp(D * 0.25f, 3.5f, 6f), H = 6.2f;
            int wall = k.Brick(s.Color), stone = k.stone;
            int slate = k.b.K.M(MapMaterials.Shingle(new Color(0.48f, 0.5f, 0.54f)));
            Bx(k, -hw, 0f, -D, hw, H, zf, wall, Walls);
            Solid(k, -hw, 0f, -D, hw, H + 1.5f, zf);
            Bx(k, -hw - 0.04f, 0f, -D, hw + 0.04f, yF + 0.15f, zf + 0.05f, stone, Walls);
            Bx(k, -hw - 0.04f, H - 1.05f, -D, hw + 0.04f, H - 0.45f, zf + 0.06f, stone, Walls);
            Bx(k, -hw - 0.08f, H - 0.45f, -D, hw + 0.08f, H, zf + 0.25f, stone, Walls);
            float yR = HipRoof(k, -hw, hw, -D, zf, H, 0.36f, 0.3f, slate, k.trim);
            Letters(k, SignOf(s, "UNITED STATES POST OFFICE"), 0f, H - 0.98f, zf + 0.06f, W * 0.78f, 0.42f, k.bronze);
            // entrance: steps with cheek walls, stone surround, double doors with fanlight
            int steps = 5;
            for (int i = 0; i < steps; i++)
                Bx(k, -2.2f, 0f, zf, 2.2f, (i + 1) * yF / steps, zf + (steps - i) * 0.3f, k.conc, NoBottom);
            foreach (float sx in new[] { -1f, 1f })
            {
                Bx(k, sx * 2.2f - 0.3f, 0f, zf, sx * 2.2f + 0.3f, yF + 0.45f, zf + steps * 0.3f + 0.1f, stone, NoBottom);
                Lantern(k, sx * 2.2f, yF + 0.45f, zf + steps * 0.3f - 0.2f, 1.2f);
            }
            Door(k, 0f, yF, 1.8f, 2.5f, zf, k.bronze, k.bronze, true, 2);
            ArchWindow(k, 0f, yF + 2.58f, 1.8f, 0.01f, zf, k.bronze, -1, -1, false);
            Bx(k, -1.4f, yF, zf, -1.05f, yF + 3.6f, zf + 0.12f, stone, Shown);
            Bx(k, 1.05f, yF, zf, 1.4f, yF + 3.6f, zf + 0.12f, stone, Shown);
            Bx(k, -1.6f, yF + 3.6f, zf, 1.6f, yF + 3.95f, zf + 0.2f, stone, Shown);
            foreach (float sx in new[] { -1f, 1f })
                Spread(2.4f, hw - 0.6f, 3.0f, x => ArchWindow(k, sx * x, yF + 0.55f, 1.4f, 2.4f, zf, k.trim, k.Back(), stone));
            foreach (bool right in new[] { false, true })
                OnSide(k, right, right ? hw : -hw, -D + 1f, zf - 1f, 3.2f, lx => ArchWindow(k, lx, yF + 0.55f, 1.3f, 2.3f, 0f, k.trim, k.Back(), stone));
            // mail dock at the back
            k.b.BeginLocal(new Vector3(0f, 0f, -D), Quaternion.Euler(0f, 180f, 0f));
            OverheadDoor(k, -hw * 0.4f, 1.1f, 2.75f, 2.75f, 0f, k.unit, false, k.trim);
            Bx(k, -hw * 0.4f - 2.0f, 0f, 0f, -hw * 0.4f + 2.0f, 1.1f, 1.8f, k.conc, NoBottom);
            Bx(k, -hw * 0.4f - 2.1f, 4.1f, 0f, -hw * 0.4f + 2.1f, 4.3f, 2.0f, k.unit);
            Door(k, hw * 0.4f, 0f, 0.92f, 2.13f, 0f, k.unit, k.unit, false);
            k.b.EndLocal();
            if (b.R() < 0.5f)
            {   // small cupola
                float cz = (-D + zf) * 0.5f;
                Bx(k, -0.9f, yR - 0.5f, cz - 0.9f, 0.9f, yR + 1.2f, cz + 0.9f, k.trim, Walls);
                for (int f = 0; f < 4; f++)
                {
                    k.b.BeginLocal(new Vector3(0f, 0f, cz), Quaternion.Euler(0f, f * 90f, 0f));
                    QZ(k, -0.4f, yR + 0.2f, 0.4f, yR + 1.0f, 0.905f, k.dark);
                    k.b.EndLocal();
                }
                k.b.B.AddFrustum(new Vector3(0f, yR + 1.2f + 0.6f, cz), Quaternion.Euler(0f, 45f, 0f), 1.4f, 0.05f, 1.2f, 4, slate, true);
                Bx(k, -0.02f, yR + 2.4f, cz - 0.02f, 0.02f, yR + 3.1f, cz + 0.02f, k.black);
            }
            // front walk, flagpole and collection box
            QY(k, -1.2f, zf + steps * 0.3f, 1.2f, 0f, 0.02f, k.conc);
            Flagpole(k, -Mathf.Min(hw - 1f, 5.5f), Mathf.Min(-0.8f, zf * 0.4f), 9.5f);
            MailBox(k, 2.4f, -0.6f);
        }

        static void MailBox(Kit k, float x, float z)
        {
            int blue = k.Flat(new Color(0.10f, 0.20f, 0.45f));
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Bx(k, x + sx * 0.235f - 0.025f, 0f, z + sz * 0.215f - 0.025f, x + sx * 0.235f + 0.025f, 0.25f, z + sz * 0.215f + 0.025f, blue);
            Bx(k, x - 0.27f, 0.25f, z - 0.26f, x + 0.27f, 1.05f, z + 0.26f, blue, NoBottom & ~MeshBuilder.FacePosY);
            k.b.B.AddExtrude(HalfDisc(0.27f, 0.22f, 8), new Vector3(x, 1.05f, z), Quaternion.identity, 0.52f, blue);
            Bx(k, x - 0.16f, 0.98f, z + 0.26f, x + 0.16f, 1.1f, z + 0.31f, blue);
            QZ(k, x - 0.13f, 0.6f, x + 0.13f, 0.77f, z + 0.262f, k.white);
        }

        // =====================================================================================================
        // library — Carnegie-style library
        // =====================================================================================================

        static void Library(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(12f, s.Width), D = Mathf.Max(12f, s.Depth), hw = W * 0.5f - 0.1f;
            int n = Mathf.Clamp(s.Stories, 1, 2);
            const float yF = 1.4f, zf = -3.6f, zp = -2.6f;
            float H = n == 1 ? 7.6f : 10.2f;
            int wall = k.Brick(s.Color), stone = k.stone;
            int roofS = b.R() < 0.5f ? k.b.K.M(MapMaterials.Shingle(new Color(0.75f, 0.42f, 0.32f))) : k.b.K.M(MapMaterials.Shingle(new Color(0.5f, 0.5f, 0.54f)));
            Bx(k, -hw, 0f, -D, hw, H, zf, wall, Walls);
            Solid(k, -hw, 0f, -D, hw, H + 1.5f, zf);
            Bx(k, -hw - 0.05f, 0f, -D, hw + 0.05f, yF, zf + 0.06f, stone, Walls);
            Bx(k, -hw - 0.05f, H - 0.5f, -D, hw + 0.05f, H, zf + 0.25f, stone, Walls);
            HipRoof(k, -hw, hw, -D, zf, H, 0.45f, 0.35f, roofS, k.trim);
            float pw = Mathf.Clamp(W * 0.3f, 4.5f, 6.5f);
            // basement windows + big reading-room windows
            foreach (float sx in new[] { -1f, 1f })
                Spread(pw * 0.5f + 0.6f, hw - 0.5f, 3.2f, x =>
                {
                    QZ(k, sx * x - 0.6f, 0.45f, sx * x + 0.6f, 1.05f, zf + 0.065f, k.dark);
                    Window(k, sx * x, yF + 0.7f, 1.7f, n == 1 ? 3.6f : 2.8f, zf, k.trim, 3, 3, stone, stone, k.Back());
                    if (n == 2) Window(k, sx * x, yF + 4.6f, 1.7f, 2.2f, zf, k.trim, 3, 2, stone, stone, k.Back());
                });
            foreach (bool right in new[] { false, true })
                OnSide(k, right, right ? hw : -hw, -D + 1f, zf - 1f, 3.4f, lx =>
                {
                    Window(k, lx, yF + 0.7f, 1.6f, n == 1 ? 3.6f : 2.8f, 0f, k.trim, 3, 3, stone, stone, k.Back());
                    if (n == 2) Window(k, lx, yF + 4.6f, 1.6f, 2.2f, 0f, k.trim, 3, 2, stone, stone, k.Back());
                });
            // entrance pavilion with pediment, arched doorway and sign
            Bx(k, -pw * 0.5f, 0f, zf, pw * 0.5f, H + 0.3f, zp, stone, Walls);
            Pediment(k, 0f, H + 0.3f, zf, zp + 0.2f, pw + 0.4f, (pw + 0.4f) * 0.5f * 0.3f, stone, roofS);
            Bx(k, -pw * 0.5f - 0.1f, H, zf, pw * 0.5f + 0.1f, H + 0.3f, zp + 0.2f, stone, NoBack);
            ArchWindow(k, 0f, yF, 2.2f, 2.6f, zp, stone, k.dark, -1, false);
            Door(k, 0f, yF, 1.8f, 2.4f, zp + 0.02f, k.bronze, k.b.K.M(SurfaceKind.Oak), false, 2);
            Letters(k, SignOf(s, "PUBLIC LIBRARY"), 0f, H - 1.25f, zp, pw - 0.5f, 0.55f, k.bronze);
            int steps = 8;
            for (int i = 0; i < steps; i++)
                Bx(k, -pw * 0.5f - 0.5f, 0f, zp, pw * 0.5f + 0.5f, (i + 1) * yF / steps, zp + (steps - i) * 0.3f, stone, NoBottom);
            foreach (float sx in new[] { -1f, 1f })
            {
                Bx(k, sx * (pw * 0.5f + 0.5f) - 0.3f, 0f, zp, sx * (pw * 0.5f + 0.5f) + 0.3f, yF + 0.5f, zp + steps * 0.3f, stone, NoBottom);
                Lantern(k, sx * (pw * 0.5f + 0.5f), yF + 0.5f, zp + steps * 0.3f - 0.3f, 1.4f);
            }
            // book return
            int drop = k.Flat(new Color(0.15f, 0.25f, 0.45f));
            Bx(k, pw * 0.5f + 1.6f, 0f, -0.9f, pw * 0.5f + 2.3f, 1.1f, -0.3f, drop);
            Bx(k, pw * 0.5f + 1.6f, 1.1f, -0.9f, pw * 0.5f + 2.3f, 1.2f, -0.3f, drop);
        }

        // =====================================================================================================
        // school — 1-storey elementary school + gym
        // =====================================================================================================

        static void School(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(40f, s.Width), D = Mathf.Max(20f, s.Depth), hw = W * 0.5f;
            const float H1 = 4.6f, H2 = 8.6f;
            int wall = k.Brick(s.Color);
            int panel = k.Paint(s.Accent);
            float gymW = Mathf.Clamp(W * 0.28f, 18f, 26f);
            const float zg = -2.0f;
            bool gymLeft = b.R() < 0.5f;
            float sgn = gymLeft ? -1f : 1f;   // mirror x so the gym can be on either end
            // main classroom block
            float m0 = gymLeft ? -hw + gymW : -hw, m1 = gymLeft ? hw : hw - gymW;
            Bx(k, m0, 0f, -D, m1, H1, 0f, wall, Walls);
            Solid(k, m0, 0f, -D, m1, H1 + 0.7f, 0f);
            Parapets(k, m0, m1, -D, 0f, H1, 0.7f, wall, k.alum);
            float ex = (m0 + m1) * 0.5f + sgn * -4f;     // entrance a bit toward the outer end
            const float ew = 8f;
            Ribbon(k, m0 + 1.2f, ex - ew * 0.5f - 1f, 0.95f, 2.0f, 0f, k.alum, 1.22f);
            Ribbon(k, ex + ew * 0.5f + 1f, m1 - 1.2f, 0.95f, 2.0f, 0f, k.alum, 1.22f);
            Bx(k, m0 + 1.1f, 0.6f, 0f, ex - ew * 0.5f - 0.9f, 0.88f, 0.05f, panel, Shown);
            Bx(k, ex + ew * 0.5f + 0.9f, 0.6f, 0f, m1 - 1.1f, 0.88f, 0.05f, panel, Shown);
            // side + back window bands
            {
                bool outerRight = !gymLeft;
                float xw = outerRight ? m1 : m0;
                k.b.BeginLocal(new Vector3(xw, 0f, 0f), Quaternion.Euler(0f, outerRight ? 90f : -90f, 0f));
                if (outerRight) Ribbon(k, 1.2f, D - 1.2f, 0.95f, 2.0f, 0f, k.alum, 1.22f);
                else Ribbon(k, -D + 1.2f, -1.2f, 0.95f, 2.0f, 0f, k.alum, 1.22f);
                k.b.EndLocal();
                k.b.BeginLocal(new Vector3(0f, 0f, -D), Quaternion.Euler(0f, 180f, 0f));
                Ribbon(k, -m1 + 1.2f, -m0 - 1.2f, 0.95f, 2.0f, 0f, k.alum, 1.22f);
                Door(k, -(m0 + m1) * 0.5f, 0f, 1.83f, 2.13f, 0f, k.unit, k.unit, false, 2);
                k.b.EndLocal();
            }
            // entrance: glass + doors + canopy with sign
            QZ(k, ex - ew * 0.5f + 0.5f, 0f, ex + ew * 0.5f - 0.5f, 3.4f, 0.005f, k.interior);
            QZ(k, ex - ew * 0.5f + 0.5f, 0f, ex + ew * 0.5f - 0.5f, 3.4f, 0.03f, k.glass);
            Spread(ex - ew * 0.5f + 0.5f, ex + ew * 0.5f - 0.5f, 1.0f, x => Bx(k, x - 0.035f, 2.35f, 0f, x + 0.035f, 3.4f, 0.07f, k.alum, Shown));
            Bx(k, ex - ew * 0.5f + 0.5f, 2.3f, 0f, ex + ew * 0.5f - 0.5f, 2.38f, 0.07f, k.alum, Shown);
            Door(k, ex - 1.4f, 0f, 1.83f, 2.25f, 0.01f, k.alum, k.alum, true, 2);
            Door(k, ex + 1.4f, 0f, 1.83f, 2.25f, 0.01f, k.alum, k.alum, true, 2);
            float proj = s.FrontYard > 0.5f ? Mathf.Clamp(s.FrontYard * 0.5f, 1.5f, 3.5f) : 3.0f;
            Bx(k, ex - ew * 0.5f - 0.5f, 3.65f, 0f, ex + ew * 0.5f + 0.5f, 4.25f, proj, panel);
            QY(k, ex - ew * 0.5f, 0.2f, ex + ew * 0.5f, proj - 0.2f, 3.645f, k.white, false);
            Spread(ex - ew * 0.5f, ex + ew * 0.5f, 2.0f, x => QY(k, x - 0.3f, proj * 0.5f - 0.3f, x + 0.3f, proj * 0.5f + 0.3f, 3.64f, k.lit, false));
            Letters(k, SignOf(s, "ELEMENTARY SCHOOL"), ex, 3.72f, proj, ew + 0.6f, 0.46f, k.white);
            foreach (float x in new[] { ex - ew * 0.5f - 0.2f, ex + ew * 0.5f + 0.2f }) Cyl(k, x, 0f, proj - 0.3f, 0.14f, 3.65f, 10, panel);
            Letters(k, SignOf(s, ""), ex, H1 + 0.12f, 0f, ew + 4f, 0.42f, k.white);
            // gym block
            float g0 = gymLeft ? -hw : hw - gymW, g1 = gymLeft ? -hw + gymW : hw;
            int metal = k.b.K.M(MapMaterials.MetalPanel(Color.Lerp(s.Accent, Color.white, 0.35f)));
            Bx(k, g0, 0f, -D, g1, 3.4f, zg, wall, Walls);
            Bx(k, g0, 3.4f, -D, g1, H2, zg, metal, Walls);
            Bx(k, g0 - 0.03f, 3.3f, -D - 0.03f, g1 + 0.03f, 3.45f, zg + 0.03f, k.stone, Walls);
            Solid(k, g0, 0f, -D, g1, H2 + 0.6f, zg);
            Parapets(k, g0, g1, -D, zg, H2, 0.6f, metal, k.alum);
            Ribbon(k, g0 + 1.5f, g1 - 1.5f, 6.2f, 1.3f, zg, k.alum, 1.5f);
            Door(k, (g0 + g1) * 0.5f, 0f, 1.83f, 2.13f, zg, k.unit, k.unit, false, 2);
            Bx(k, (g0 + g1) * 0.5f - 1.4f, 2.5f, zg, (g0 + g1) * 0.5f + 1.4f, 2.7f, zg + 1.0f, k.alum);
            Letters(k, "GYMNASIUM", (g0 + g1) * 0.5f, 3.7f, zg, gymW * 0.6f, 0.5f, k.Paint(s.Accent));
            {
                bool outerRight = gymLeft == false;
                float xw = outerRight ? g1 : g0;
                k.b.BeginLocal(new Vector3(xw, 0f, 0f), Quaternion.Euler(0f, outerRight ? 90f : -90f, 0f));
                if (outerRight) Ribbon(k, -zg + 1.5f, D - 1.5f, 6.2f, 1.3f, 0f, k.alum, 1.5f);
                else Ribbon(k, -D + 1.5f, zg - 1.5f, 6.2f, 1.3f, 0f, k.alum, 1.5f);
                k.b.EndLocal();
            }
            // rooftop units
            for (int i = 0; i < 6; i++)
                Rtu(k, Mathf.Lerp(m0 + 4f, m1 - 4f, (i % 3) / 2f), H1, -D * (i < 3 ? 0.3f : 0.7f));
            Rtu(k, (g0 + g1) * 0.5f - 3f, H2, (zg - D) * 0.5f, 2.6f, 1.6f, 1.2f);
            Rtu(k, (g0 + g1) * 0.5f + 3f, H2, (zg - D) * 0.5f, 2.6f, 1.6f, 1.2f);
            Flagpole(k, ex - sgn * (ew * 0.5f + 4f), proj, 10f);
        }

        // =====================================================================================================
        // firestation
        // =====================================================================================================

        static void FireStation(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(12f, s.Width), D = Mathf.Max(16f, s.Depth), hw = W * 0.5f;
            int nb = s.Units >= 2 ? Mathf.Clamp(s.Units, 2, 3) : (W >= 19f ? 3 : 2);
            const float bayP = 4.9f, doorW = 3.7f, doorH = 4.3f, H = 7.2f;
            float apron = Mathf.Clamp(D - 18f, 6f, 12f), zf = -apron;
            int wall = k.Brick(s.Color), stone = k.stone;
            int red = k.Paint(new Color(0.72f, 0.08f, 0.06f));
            int doorFrame = b.R() < 0.5f ? red : k.alum;
            Bx(k, -hw, 0f, -D, hw, H, zf, wall, Walls);
            Solid(k, -hw, 0f, -D, hw, H + 0.8f, zf);
            Parapets(k, -hw, hw, -D, zf, H, 0.8f, wall, stone);
            Bx(k, -hw - 0.04f, 0f, -D, hw + 0.04f, 0.6f, zf + 0.04f, stone, Walls);
            QY(k, -hw, zf, hw, 0f, 0.02f, k.conc);
            for (float z = zf + 3f; z < -0.5f; z += 3f) QY(k, -hw, z - 0.012f, hw, z + 0.012f, 0.025f, k.Flat(new Color(0.4f, 0.4f, 0.4f)));
            float bx0 = -hw + 0.5f, bx1 = bx0 + nb * bayP;
            for (int i = 0; i < nb; i++)
            {
                float cx = bx0 + bayP * (i + 0.5f);
                GlassOverheadDoor(k, cx, doorW, doorH, zf, doorFrame);
                Bx(k, cx - doorW * 0.5f - 0.25f, doorH, zf, cx + doorW * 0.5f + 0.25f, doorH + 0.3f, zf + 0.07f, stone, Shown);
                foreach (float sx in new[] { -1f, 1f }) Cyl(k, cx + sx * (doorW * 0.5f + 0.25f), 0f, zf + 0.35f, 0.11f, 1.1f, 8, k.yellow);
            }
            Bx(k, bx0, 4.85f, zf, bx1, 5.85f, zf + 0.06f, k.white, Shown);
            Letters(k, SignOf(s, "FIRE STATION"), (bx0 + bx1) * 0.5f, 4.95f, zf + 0.06f, bx1 - bx0 - 0.8f, 0.8f, red);
            // quarters / office
            if (hw - bx1 > 3.5f)
            {
                float qx0 = bx1 + 0.4f, qx1 = hw - 0.4f, dx = qx0 + 1.2f;
                Door(k, dx, 0f, 0.95f, 2.13f, zf, k.trim, red, false, 1, 0.5f);
                Bx(k, dx - 1.0f, 2.95f, zf, dx + 1.0f, 3.15f, zf + 1.1f, k.alum);
                Spread(dx + 1.0f, qx1, 1.9f, x => Window(k, x, 1.0f, 1.1f, 1.6f, zf, k.trim, 1, 2, stone, stone, k.Back()));
                Spread(qx0, qx1, 1.9f, x => Window(k, x, 4.6f, 1.1f, 1.5f, zf, k.trim, 1, 2, stone, stone, k.Back()));
                Flagpole(k, Mathf.Min(hw - 0.8f, (qx0 + qx1) * 0.5f + 1.5f), -0.9f, 9f);
            }
            OnSide(k, true, hw, -D + 1f, zf - 1f, 2.6f, lx =>
            {
                Window(k, lx, 1.0f, 1.1f, 1.6f, 0f, k.trim, 1, 2, stone, stone, k.Back());
                Window(k, lx, 4.6f, 1.1f, 1.5f, 0f, k.trim, 1, 2, stone, stone, k.Back());
            });
            OnSide(k, false, -hw, -D + 3.6f, zf - 1f, 3.5f, lx => Window(k, lx, 3.2f, 1.6f, 0.9f, 0f, k.trim, 2, 1, stone, -1, k.Back()));
            // back: apparatus doors (drive-through) + hose drying tower
            k.b.BeginLocal(new Vector3(0f, 0f, -D), Quaternion.Euler(0f, 180f, 0f));
            for (int i = 0; i < nb; i++) OverheadDoor(k, -(bx0 + bayP * (i + 0.5f)), 0f, doorW, doorH, 0f, k.unit, true, k.trim);
            k.b.EndLocal();
            float tx0 = -hw, tx1 = -hw + 3.2f, tz0 = -D, tz1 = -D + 3.2f, tH = 13.5f;
            Bx(k, tx0, H, tz0, tx1, tH, tz1, wall, Walls);
            Solid(k, tx0, H, tz0, tx1, tH, tz1);
            Parapets(k, tx0, tx1, tz0, tz1, tH, 0.5f, wall, stone, 0.2f);
            k.b.BeginLocal(new Vector3((tx0 + tx1) * 0.5f, 0f, tz1), Quaternion.identity);
            for (float y = H + 1.2f; y < tH - 1.2f; y += 2.2f) Window(k, 0f, y, 0.6f, 1.2f, 0f, k.trim, 1, 1, stone, -1, k.dark);
            k.b.EndLocal();
            Rtu(k, 1f, H, -D * 0.5f);
        }

        // =====================================================================================================
        // gasstation — convenience store + fuel canopy + forecourt + price pylon
        // =====================================================================================================

        static void GasStation(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(20f, s.Width), D = Mathf.Max(24f, s.Depth), hw = W * 0.5f;
            var gs = MapBodies.GasStore(s); float sw = gs.x, sd = gs.y, zS = -D + sd, sx0 = -sw * 0.5f, sx1 = sw * 0.5f;
            Color brandC = Pick(b, SignColors);
            int brand = k.Flat(brandC), brandGlow = k.Glow(brandC);
            int wall = b.R() < 0.5f ? k.b.K.M(SurfaceKind.ConcreteBlock, s.Color) : k.b.K.M(MapMaterials.Stucco(s.Color));
            // forecourt slab with joints
            QY(k, -hw, zS, hw, 0f, 0.02f, k.conc);
            int joint = k.Flat(new Color(0.42f, 0.42f, 0.42f));
            for (float x = -hw + 4.5f; x < hw - 1f; x += 4.5f) QY(k, x - 0.012f, zS, x + 0.012f, 0f, 0.025f, joint);
            for (float z = zS + 4.5f; z < -1f; z += 4.5f) QY(k, -hw, z - 0.012f, hw, z + 0.012f, 0.025f, joint);
            // store
            const float sH = 4.6f;
            Bx(k, sx0, 0f, -D, sx1, sH, zS, wall, Walls);
            Solid(k, sx0, 0f, -D, sx1, sH + 1.2f, zS);
            Parapets(k, sx0, sx1, -D, zS, sH, 0.6f, wall, k.alum);
            Bx(k, sx0, 0f, zS, sx1, 0.15f, zS + 1.6f, k.conc, NoBottom);
            Ribbon(k, sx0 + 1.2f, -1.4f, 0.75f, 2.2f, zS, k.alum, 1.5f, k.interior);
            Ribbon(k, 1.4f, sx1 - 1.2f, 0.75f, 2.2f, zS, k.alum, 1.5f, k.interior);
            Bx(k, sx0 + 1.2f, 0.15f, zS, -1.4f, 0.68f, zS + 0.05f, k.black, Shown);
            Bx(k, 1.4f, 0.15f, zS, sx1 - 1.2f, 0.68f, zS + 0.05f, k.black, Shown);
            Door(k, 0f, 0.15f, 1.83f, 2.13f, zS, k.alum, k.alum, true, 2, 0.5f);
            Bx(k, sx0, 3.1f, zS, sx1, 3.3f, zS + 1.5f, k.white);
            Bx(k, sx0 - 0.05f, 3.1f, zS + 1.5f, sx1 + 0.05f, 4.0f, zS + 1.62f, brand);
            QY(k, sx0, zS, sx1, zS + 1.5f, 3.095f, k.lit, false);
            Letters(k, "FOOD MART", 0f, 3.25f, zS + 1.62f, sw * 0.5f, 0.6f, k.white);
            Bx(k, sx0 + 0.3f, 0.15f, zS + 0.15f, sx0 + 1.4f, 1.35f, zS + 0.85f, k.white, NoBottom);
            Letters(k, "ICE", sx0 + 0.85f, 0.85f, zS + 0.85f, 0.8f, 0.3f, k.Flat(new Color(0.1f, 0.35f, 0.75f)));
            Bx(k, sx1 - 1.5f, 0.15f, zS + 0.15f, sx1 - 0.3f, 1.6f, zS + 0.9f, k.galv, NoBottom);
            for (int i = 0; i < 3; i++) Cyl(k, sx1 - 1.3f + i * 0.4f, 0.15f, zS + 0.5f, 0.15f, 0.45f, 8, k.white);
            Rtu(k, sx0 + 3f, sH, -D + sd * 0.5f);
            Rtu(k, sx1 - 3f, sH, -D + sd * 0.5f, 1.5f, 1.1f, 0.9f);
            // canopy
            int ni = W >= 34f ? 4 : W >= 26f ? 3 : 2;
            const float pitch = 7.0f, yU = 4.9f, yC = 5.8f;
            float cw = Mathf.Min(ni * pitch + 2f, W - 1f), zc0 = zS + 4.5f, zc1 = Mathf.Min(-1.5f, zc0 + 10f), cz = (zc0 + zc1) * 0.5f;
            Bx(k, -cw * 0.5f, yU, zc0, cw * 0.5f, yC, zc1, k.white, Walls | MeshBuilder.FacePosY);
            QY(k, -cw * 0.5f, zc0, cw * 0.5f, zc1, yU, k.Flat(new Color(0.88f, 0.88f, 0.86f)), false);
            Bx(k, -cw * 0.5f - 0.02f, yU + 0.25f, zc0 - 0.02f, cw * 0.5f + 0.02f, yU + 0.55f, zc1 + 0.02f, brand, Walls);
            float signW = Mathf.Min(cw * 0.6f, 1.0f + MapText.Width(SignOf(s, "GAS"), 0.1f));
            Bx(k, -signW * 0.5f, yU + 0.05f, zc1, signW * 0.5f, yC - 0.05f, zc1 + 0.06f, brandGlow, Shown);
            Letters(k, SignOf(s, "GAS"), 0f, yU + 0.15f, zc1 + 0.06f, signW - 0.5f, 0.65f, k.white);
            for (int i = 0; i < ni; i++)
            {
                float x = (i - (ni - 1) * 0.5f) * pitch;
                Bx(k, x - 0.6f, 0f, cz - 3.4f, x + 0.6f, 0.15f, cz + 3.4f, k.conc, NoBottom);
                foreach (float sz in new[] { -1f, 1f }) Cyl(k, x, 0.15f, cz + sz * 3.2f, 0.1f, 0.95f, 8, k.yellow);
                Bx(k, x - 0.22f, 0.15f, cz - 0.22f, x + 0.22f, yU, cz + 0.22f, k.white, Walls);
                Bx(k, x - 0.25f, 0.15f, cz - 0.25f, x + 0.25f, 1.0f, cz + 0.25f, brand, Walls);
                foreach (float pz in new[] { cz - 1.7f, cz + 1.7f })
                {
                    Bx(k, x - 0.3f, 0.15f, pz - 0.6f, x + 0.3f, 0.35f, pz + 0.6f, k.black, Walls);
                    Bx(k, x - 0.25f, 0.35f, pz - 0.55f, x + 0.25f, 1.75f, pz + 0.55f, k.white, Walls);
                    Bx(k, x - 0.27f, 1.75f, pz - 0.57f, x + 0.27f, 2.05f, pz + 0.57f, brand);
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        float fx = x + sx * 0.252f;
                        QX(k, fx, 1.2f, 1.5f, pz - 0.3f, pz + 0.3f, k.dark, sx > 0f);
                        QX(k, fx, 0.7f, 1.0f, pz - 0.35f, pz + 0.35f, k.Flat(new Color(0.15f, 0.15f, 0.15f)), sx > 0f);
                        for (int h = 0; h < 3; h++) Bx(k, fx - 0.04f, 0.9f, pz - 0.3f + h * 0.3f - 0.05f, fx + 0.04f, 1.12f, pz - 0.3f + h * 0.3f + 0.05f, k.black);
                    }
                }
                foreach (float lz in new[] { cz - 3f, cz - 1f, cz + 1f, cz + 3f })
                    foreach (float lx in new[] { x - pitch * 0.25f, x + pitch * 0.25f })
                        QY(k, lx - 0.35f, lz - 0.35f, lx + 0.35f, lz + 0.35f, yU - 0.005f, k.lit, false);
            }
            k.b.Lights.Add((new Vector3(0f, yU - 0.4f, cz), new Color(1f, 0.97f, 0.9f), 16f));
            // price pylon at the front corner
            float px = hw - 1.4f, pz0 = -1.0f;
            Bx(k, px - 0.25f, 0f, pz0 - 1.1f, px + 0.25f, 7.4f, pz0 + 1.1f, brand, Walls | MeshBuilder.FacePosY);
            Bx(k, px - 0.35f, 0f, pz0 - 1.2f, px + 0.35f, 1.1f, pz0 + 1.2f, k.b.K.M(SurfaceKind.ConcreteBlock), NoBottom);
            string[] grades = { "REG", "PLUS", "PREM", "DSL" };
            float baseP = 2.79f + b.R() * 0.9f;
            float[] adds = { 0f, 0.4f, 0.8f, 0.6f };
            int led = k.Glow(new Color(1f, 0.3f, 0.15f));
            foreach (bool right in new[] { true, false })
            {
                k.b.BeginLocal(new Vector3(right ? px + 0.25f : px - 0.25f, 0f, pz0), Quaternion.Euler(0f, right ? 90f : -90f, 0f));
                QZ(k, -1.0f, 5.7f, 1.0f, 7.25f, 0.006f, k.Glow(new Color(0.96f, 0.96f, 0.94f)));
                Letters(k, SignOf(s, "GAS"), 0f, 5.9f, 0.006f, 1.8f, 1.1f, brand);
                for (int r = 0; r < 4; r++)
                {
                    float y = 1.35f + r * 1.05f;
                    QZ(k, -1.0f, y, 1.0f, y + 0.9f, 0.006f, k.dark);
                    Letters(k, grades[3 - r], -0.55f, y + 0.3f, 0.007f, 0.75f, 0.3f, k.white);
                    string price = (baseP + adds[3 - r]).ToString("0.00", CultureInfo.InvariantCulture);
                    Letters(k, price, 0.45f, y + 0.15f, 0.007f, 0.95f, 0.6f, led);
                }
                k.b.EndLocal();
            }
            Solid(k, px - 0.35f, 0f, pz0 - 1.2f, px + 0.35f, 7.4f, pz0 + 1.2f);
        }

        // =====================================================================================================
        // grocery — supermarket box
        // =====================================================================================================

        static void Grocery(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(30f, s.Width), D = Mathf.Max(25f, s.Depth), hw = W * 0.5f - 0.04f;
            float zf = -Mathf.Clamp(D * 0.09f, 3.2f, 4.5f);
            const float H = 7.2f, H2 = 9.8f;
            bool stucco = b.R() < 0.4f;
            int wall = stucco ? k.b.K.M(MapMaterials.Stucco(s.Color)) : k.b.K.M(SurfaceKind.ConcreteBlock, s.Color);
            int wains = k.b.K.M(SurfaceKind.ConcreteBlock, s.Color * 0.7f);
            int band = k.Paint(s.Accent);
            Bx(k, -hw, 0f, -D, hw, H, zf, wall, Walls);
            Solid(k, -hw, 0f, -D, hw, H + 0.9f, zf);
            Bx(k, -hw - 0.03f, 0f, -D, hw + 0.03f, 1.2f, zf + 0.03f, wains, Walls);
            Bx(k, -hw - 0.04f, H - 0.75f, -D, hw + 0.04f, H - 0.35f, zf + 0.04f, band, Walls);
            Parapets(k, -hw, hw, -D, zf, H, 0.9f, wall, k.alum);
            Bx(k, -hw, 0f, zf, hw, 0.15f, 0f, k.conc, NoBottom);
            // entrance vestibule
            float r = b.R();
            float ex = r < 0.35f ? -hw * 0.35f : r < 0.7f ? hw * 0.35f : 0f;
            float vw = 11f, vd = Mathf.Min(2.6f, -zf - 0.6f), vh = 5.0f, va = ex - vw * 0.5f, vc = ex + vw * 0.5f, vz = zf + vd;
            QZ(k, va, 0.15f, vc, vh, zf + 0.01f, k.interior);
            QY(k, va, zf, vc, vz, 0.16f, k.Flat(new Color(0.5f, 0.48f, 0.44f)));
            QZ(k, va, 0.15f, vc, vh, vz, k.glass);
            QX(k, va, 0.15f, vh, zf, vz, k.glass, false);
            QX(k, vc, 0.15f, vh, zf, vz, k.glass, true);
            for (int i = 0; i <= 7; i++)
            {
                float x = Mathf.Lerp(va, vc, i / 7f);
                Bx(k, x - 0.05f, 0.15f, vz - 0.05f, x + 0.05f, vh, vz + 0.05f, k.alum, Walls);
            }
            foreach (float x in new[] { va, vc })
                Bx(k, x - 0.05f, 0.15f, zf, x + 0.05f, vh, vz, k.alum, Walls);
            foreach (float y in new[] { 2.4f, 3.6f })
                Bx(k, va, y - 0.04f, vz - 0.04f, vc, y + 0.04f, vz + 0.05f, k.alum, Walls);
            Bx(k, va - 0.25f, vh, zf, vc + 0.25f, vh + 0.4f, vz + 0.25f, k.alum);
            foreach (float dx in new[] { ex - 2.8f, ex + 2.8f })
            {
                QZ(k, dx - 1.0f, 0.15f, dx + 1.0f, 2.25f, vz + 0.06f, k.interior);
                Bx(k, dx - 1.1f, 2.25f, vz, dx + 1.1f, 2.4f, vz + 0.12f, k.black);
                Bx(k, dx - 1.1f, 0.15f, vz, dx - 1.0f, 2.25f, vz + 0.12f, k.black);
                Bx(k, dx + 1.0f, 0.15f, vz, dx + 1.1f, 2.25f, vz + 0.12f, k.black);
                Bx(k, dx - 0.04f, 0.15f, vz + 0.06f, dx + 0.04f, 2.25f, vz + 0.1f, k.alum, Shown);
            }
            // raised entry parapet with the store sign
            Bx(k, va - 1.2f, H, zf - 0.4f, vc + 1.2f, H2, zf, wall, NoBottom);
            Bx(k, va - 1.3f, H2, zf - 0.5f, vc + 1.3f, H2 + 0.12f, zf + 0.06f, k.alum);
            int signC = k.Glow(Pick(b, SignColors));
            Letters(k, SignOf(s, "SUPERMARKET"), ex, H + 0.2f, zf, vw + 1.6f, H2 - H - 0.5f, signC);
            // sidewalk canopies left and right of the vestibule
            int canopy = b.R() < 0.5f ? band : k.alum;
            float cd = Mathf.Min(2.8f, -zf - 0.4f);
            foreach (var (a, c) in new[] { (-hw + 2f, va - 0.4f), (vc + 0.4f, hw - 2f) })
            {
                if (c - a < 3f) continue;
                Bx(k, a, 3.6f, zf, c, 4.0f, zf + cd, canopy);
                Spread(a, c, 6f, x => Cyl(k, x, 0.15f, zf + cd - 0.25f, 0.1f, 3.45f, 8, k.alum));
                QY(k, a, zf, c, zf + cd, 3.595f, k.white, false);
            }
            // a few display windows + department signs
            Ribbon(k, vc + 1.2f, Mathf.Min(vc + 9f, hw - 2f), 0.95f, 2.0f, zf, k.alum, 1.5f, k.interior);
            Ribbon(k, Mathf.Max(va - 9f, -hw + 2f), va - 1.2f, 0.95f, 2.0f, zf, k.alum, 1.5f, k.interior);
            int dept = k.Flat(new Color(0.15f, 0.15f, 0.17f));
            Letters(k, "PHARMACY", Mathf.Min(vc + 5f, hw - 6f), 4.4f, zf, 7f, 0.55f, dept);
            Letters(k, "BAKERY - DELI", Mathf.Max(va - 5f, -hw + 6f), 4.4f, zf, 7f, 0.55f, dept);
            // cart corral by the door
            float cx = ex > 0f ? va - 3.0f : vc + 3.0f, cz = zf + 1.5f;
            for (int sd = -1; sd <= 1; sd += 2)
                foreach (float y in new[] { 0.55f, 1.0f })
                    Bx(k, cx - 2.3f, y - 0.025f, cz + sd * 0.55f - 0.025f, cx + 2.3f, y + 0.025f, cz + sd * 0.55f + 0.025f, k.galv);
            foreach (float x in new[] { cx - 2.3f, cx + 2.3f })
                foreach (float sd in new[] { -0.55f, 0.55f }) Bx(k, x - 0.03f, 0.15f, cz + sd - 0.03f, x + 0.03f, 1.0f, cz + sd + 0.03f, k.galv);
            Bx(k, cx + 2.25f, 0.975f, cz - 0.55f, cx + 2.35f, 1.025f, cz + 0.55f, k.galv);
            Bx(k, cx - 2.0f, 0.3f, cz - 0.3f, cx + 1.6f, 1.0f, cz + 0.3f, k.alum, NoBottom);
            Bx(k, cx + 1.6f, 1.0f, cz - 0.28f, cx + 1.7f, 1.06f, cz + 0.28f, k.Paint(new Color(0.8f, 0.1f, 0.08f)));
            // rooftop units
            for (int i = 0; i < 8; i++)
                Rtu(k, Mathf.Lerp(-hw + 6f, hw - 6f, (i % 4) / 3f), H, Mathf.Lerp(zf - 6f, -D + 6f, i < 4 ? 0.25f : 0.75f), 2.6f, 1.6f, 1.2f);
            // loading dock at the back
            k.b.BeginLocal(new Vector3(0f, 0f, -D), Quaternion.Euler(0f, 180f, 0f));
            float d0 = -(hw - 5f);
            for (int i = 0; i < 3; i++)
            {
                float dx = d0 + i * 4.5f;
                OverheadDoor(k, dx, 1.2f, 2.75f, 3.0f, 0.3f, k.unit, false, k.trim);
                Bx(k, dx - 1.65f, 0.9f, 0f, dx - 1.375f, 4.6f, 0.3f, k.rubber);
                Bx(k, dx + 1.375f, 0.9f, 0f, dx + 1.65f, 4.6f, 0.3f, k.rubber);
                Bx(k, dx - 1.65f, 4.2f, 0f, dx + 1.65f, 4.8f, 0.3f, k.rubber);
                foreach (float sx in new[] { -1f, 1f }) Bx(k, dx + sx * 0.9f - 0.12f, 0.85f, 0.3f, dx + sx * 0.9f + 0.12f, 1.15f, 0.42f, k.rubber);
                Bx(k, dx - 1.375f, 1.15f, 0.3f, dx + 1.375f, 1.2f, 0.35f, k.galv);
            }
            Bx(k, d0 - 2f, 4.9f, 0f, d0 + 11f, 5.1f, 1.6f, k.unit);
            Bx(k, d0 + 14f, 0f, 0.2f, d0 + 18f, 2.6f, 2.6f, k.Paint(new Color(0.25f, 0.42f, 0.3f)));
            Door(k, d0 + 20.5f, 0f, 0.92f, 2.13f, 0f, k.unit, k.unit, false);
            k.b.EndLocal();
        }

        // =====================================================================================================
        // stripmall — row of retail bays under an arcade
        // =====================================================================================================

        static void StripMall(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(18f, s.Width), D = Mathf.Max(12f, s.Depth), hw = W * 0.5f;
            int n = Mathf.Clamp(s.Units, 3, 8);
            float bw = W / n;
            const float zf = -3.2f, H = 5.0f, yA = 3.6f;
            bool brick = b.R() < 0.4f;
            int wall = brick ? k.Brick(s.Color) : k.b.K.M(MapMaterials.Stucco(s.Color));
            bool mansard = b.R() < 0.4f;
            float bandTop = mansard ? 4.6f : 6.1f;
            Bx(k, -hw, 0f, -D, hw, H, zf, wall, Walls);
            Solid(k, -hw, 0f, -D, hw, H + 0.5f, zf);
            Parapets(k, -hw, hw, -D, zf, H, 0.5f, wall, k.alum, 0.25f, false);
            Bx(k, -hw, 0f, zf, hw, 0.15f, 0f, k.conc, NoBottom);
            // arcade: roof, fascia (sign band), piers
            Bx(k, -hw, yA, zf, hw, yA + 0.35f, -0.5f, wall, MeshBuilder.FacePosY);
            QY(k, -hw, zf, hw, -0.5f, yA, k.white, false);
            Bx(k, -hw, 3.5f, -0.5f, hw, bandTop, 0f, wall, All & ~MeshBuilder.FaceNegZ);
            QZ(k, -hw, 3.5f, hw, bandTop, -0.5f, wall, true);
            if (mansard)
            {
                int sh = k.b.K.M(MapMaterials.Shingle(new Color(0.55f, 0.4f, 0.32f)));
                var B = k.b.B;
                B.AddQuad(new Vector3(-hw, bandTop, 0.15f), new Vector3(hw, bandTop, 0.15f), new Vector3(hw, 6.0f, -1.2f), new Vector3(-hw, 6.0f, -1.2f), sh, new Vector3(0f, 1.35f, 1.4f), Vector3.right);
                B.AddTriangle(new Vector3(-hw, bandTop, 0.15f), new Vector3(-hw, 6.0f, -1.2f), new Vector3(-hw, bandTop, -1.2f), wall, Vector3.left);
                B.AddTriangle(new Vector3(hw, bandTop, 0.15f), new Vector3(hw, 6.0f, -1.2f), new Vector3(hw, bandTop, -1.2f), wall, Vector3.right);
                Bx(k, -hw, bandTop - 0.04f, 0f, hw, bandTop + 0.04f, 0.2f, k.trim);
                Bx(k, -hw, bandTop, -1.2f, hw, 6.0f, zf + 0.2f, wall, MeshBuilder.FacePosY);
            }
            else Bx(k, -hw - 0.02f, bandTop, -0.55f, hw + 0.02f, bandTop + 0.1f, 0.05f, k.alum);
            for (int i = 0; i <= n; i++)
            {
                float x = Mathf.Clamp(-hw + i * bw, -hw + 0.22f, hw - 0.22f);
                Bx(k, x - 0.22f, 0.15f, -0.6f, x + 0.22f, 3.5f, -0.16f, wall, Walls);
                Bx(k, x - 0.15f, 0.15f, zf, x + 0.15f, H, zf + 0.12f, wall, Walls & ~MeshBuilder.FaceNegZ);
            }
            // tenants
            var names = new List<string>(ShopNames);
            for (int i = names.Count - 1; i > 0; i--) { int j = b.Rng.Next(i + 1); (names[i], names[j]) = (names[j], names[i]); }
            Color[] panelCols = { new Color(0.95f, 0.95f, 0.93f), new Color(0.12f, 0.14f, 0.18f), new Color(0.75f, 0.12f, 0.1f), new Color(0.12f, 0.3f, 0.6f), new Color(0.95f, 0.85f, 0.3f) };
            float py0 = mansard ? 3.62f : 4.3f, py1 = mansard ? 4.48f : 5.55f;
            for (int i = 0; i < n; i++)
            {
                float x0 = -hw + i * bw + 0.3f, x1 = -hw + (i + 1) * bw - 0.3f;
                string name = names[i % names.Count];
                bool vacant = name == "VACANT";
                // storefront
                Bx(k, x0, 0.15f, zf, x1, 0.6f, zf + 0.06f, k.alum, Shown);
                float dx = x1 - 0.75f;
                Ribbon(k, x0 + 0.05f, dx - 0.6f, 0.6f, 2.5f, zf, k.alum, 1.5f, vacant ? k.dark : k.b.R() < 0.5f ? k.interior : k.lit);
                Door(k, dx, 0.15f, 0.95f, 2.13f, zf, k.alum, k.alum, true, 1, 0.75f);
                // sign panel
                if (vacant)
                {
                    Bx(k, x0 + 0.2f, py0, 0f, x1 - 0.2f, py1, 0.05f, k.white, Shown);
                    QZ(k, x0 + 0.4f, 1.3f, x0 + 1.6f, 2.0f, zf + 0.04f, k.white);
                    Letters(k, "FOR LEASE", x0 + 1.0f, 1.45f, zf + 0.04f, 1.1f, 0.4f, k.Flat(new Color(0.75f, 0.1f, 0.08f)));
                }
                else
                {
                    Color pc = panelCols[b.Rng.Next(panelCols.Length)];
                    Color lc = pc.grayscale > 0.5f ? Pick(b, SignColors) : new Color(0.98f, 0.98f, 0.95f);
                    Bx(k, x0 + 0.2f, py0, 0f, x1 - 0.2f, py1, 0.05f, k.Flat(pc), Shown);
                    Letters(k, name, (x0 + x1) * 0.5f, py0 + 0.1f, 0.05f, x1 - x0 - 0.8f, py1 - py0 - 0.2f, k.Glow(lc));
                    if (b.R() < 0.5f) Letters(k, "OPEN", x0 + 0.8f, 1.9f, zf + 0.04f, 0.9f, 0.2f, k.Glow(new Color(1f, 0.2f, 0.2f)));
                }
                QY(k, (x0 + x1) * 0.5f - 0.25f, zf * 0.5f - 0.25f, (x0 + x1) * 0.5f + 0.25f, zf * 0.5f + 0.25f, yA - 0.005f, k.lit, false);
                Rtu(k, (x0 + x1) * 0.5f, H, -D * 0.55f, 1.4f, 1.0f, 0.85f);
            }
            // back doors
            OnBack(k, -D, -hw, hw, bw, lx => Door(k, lx, 0f, 0.92f, 2.13f, 0f, k.unit, k.unit, false));
            // centre tower with the plaza name
            string plaza = SignOf(s, "");
            if (plaza.Length > 0)
            {
                float tw = Mathf.Min(W * 0.4f, Mathf.Max(6f, MapText.Width(plaza, 0.1f) + 1.5f));
                float ty = mansard ? 6.0f : bandTop;
                Bx(k, -tw * 0.5f, 3.5f, -1.0f, tw * 0.5f, ty + 1.6f, 0.15f, wall, NoBack);
                Bx(k, -tw * 0.5f - 0.1f, ty + 1.6f, -1.1f, tw * 0.5f + 0.1f, ty + 1.75f, 0.25f, k.alum);
                Letters(k, plaza, 0f, ty + 0.3f, 0.15f, tw - 1.0f, 1.0f, brick ? k.white : k.Flat(new Color(0.15f, 0.15f, 0.18f)));
            }
        }

        // =====================================================================================================
        // fastfood — 1-storey restaurant with drive-thru
        // =====================================================================================================

        static void FastFood(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(16f, s.Width), D = Mathf.Max(16f, s.Depth), hw = W * 0.5f;
            const float lane = 4.0f, zf = -1.6f;
            float bx0 = -hw + 0.6f, bx1 = Mathf.Max(bx0 + 10f, hw - lane - 0.6f), cx = (bx0 + bx1) * 0.5f;
            float bd = s.PlanSize.y > 0f ? s.PlanSize.y : Mathf.Clamp(D * 0.5f, 9f, 13f), zb = zf - bd;   // plan on file: the restaurant is the plan's depth
            bool mansard = b.R() < 0.55f;
            float H = mansard ? 5.0f : 4.2f;
            int wall = b.R() < 0.5f ? k.Brick(s.Color) : k.b.K.M(MapMaterials.Stucco(s.Color));
            Color brandC = Pick(b, SignColors);
            int brand = k.Flat(brandC);
            Bx(k, bx0, 0f, zb, bx1, H, zf, wall, Walls);
            Solid(k, bx0, 0f, zb, bx1, H + 0.6f, zf);
            Bx(k, bx0 - 1f, 0f, zf, bx1, 0.15f, 0f, k.conc, NoBottom);
            // dining room glass
            Ribbon(k, bx0 + 2.6f, bx1 - 0.8f, 0.8f, 2.0f, zf, k.alum, 1.5f, k.lit);
            Door(k, bx0 + 1.5f, 0.15f, 0.95f, 2.13f, zf, k.alum, k.alum, true, 1, 0.5f);
            k.b.BeginLocal(new Vector3(bx0, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
            Ribbon(k, zb + 1.0f, zf - 1.0f, 0.8f, 2.0f, 0f, k.alum, 1.5f, k.lit);
            k.b.EndLocal();
            // drive-thru window on the lane side
            k.b.BeginLocal(new Vector3(bx1, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            float wz = -(zf - 2.8f);
            Window(k, wz, 0.95f, 1.3f, 1.0f, 0f, k.alum, 2, 1, k.alum, -1, k.lit);
            Bx(k, wz - 1.1f, 2.3f, 0f, wz + 1.1f, 2.5f, 0.8f, brand);
            Letters(k, "PICK UP", wz, 2.6f, 0f, 1.8f, 0.3f, brand);
            Door(k, -(zb + 2.0f), 0f, 0.92f, 2.13f, 0f, k.unit, k.unit, false);
            k.b.EndLocal();
            if (mansard)
            {
                int roofM = b.R() < 0.5f ? k.b.K.M(MapMaterials.MetalPanel(brandC)) : k.b.K.M(MapMaterials.Shingle(new Color(0.55f, 0.35f, 0.28f)));
                const float o = 0.6f, yLo = 3.3f, yHi = 5.0f;
                var B = k.b.B;
                Vector3 A(float x, float y, float z) => new Vector3(x, y, z);
                B.AddQuad(A(bx0 - o, yLo, zf + o), A(bx1 + o, yLo, zf + o), A(bx1, yHi, zf), A(bx0, yHi, zf), roofM, new Vector3(0f, o, yHi - yLo), Vector3.right);
                B.AddQuad(A(bx1 + o, yLo, zb - o), A(bx0 - o, yLo, zb - o), A(bx0, yHi, zb), A(bx1, yHi, zb), roofM, new Vector3(0f, o, -(yHi - yLo)), Vector3.right);
                B.AddQuad(A(bx1 + o, yLo, zf + o), A(bx1 + o, yLo, zb - o), A(bx1, yHi, zb), A(bx1, yHi, zf), roofM, new Vector3(yHi - yLo, o, 0f), Vector3.forward);
                B.AddQuad(A(bx0 - o, yLo, zb - o), A(bx0 - o, yLo, zf + o), A(bx0, yHi, zf), A(bx0, yHi, zb), roofM, new Vector3(-(yHi - yLo), o, 0f), Vector3.forward);
                QY(k, bx0 - o, zb - o, bx1 + o, zf + o, yLo, k.white, false);
                QY(k, bx0, zb, bx1, zf, yHi + 0.01f, k.roof);
                Bx(k, bx0 - o - 0.03f, yLo - 0.15f, zf + o - 0.03f, bx1 + o + 0.03f, yLo + 0.02f, zf + o + 0.03f, brand);
                Bx(k, bx0 - o - 0.03f, yLo - 0.15f, zb - o - 0.03f, bx1 + o + 0.03f, yLo + 0.02f, zb - o + 0.03f, brand);
                Bx(k, bx0 - o - 0.03f, yLo - 0.15f, zb - o, bx0 - o + 0.03f, yLo + 0.02f, zf + o, brand);
                Bx(k, bx1 + o - 0.03f, yLo - 0.15f, zb - o, bx1 + o + 0.03f, yLo + 0.02f, zf + o, brand);
                Bx(k, cx - 2.6f, 3.4f, zf + o, cx + 2.6f, 4.5f, zf + o + 0.15f, brand);
                Letters(k, SignOf(s, "BURGERS"), cx, 3.5f, zf + o + 0.15f, 4.8f, 0.9f, k.Glow(new Color(1f, 0.95f, 0.85f)));
                Rtu(k, cx, yHi, (zf + zb) * 0.5f, 1.8f, 1.2f, 0.9f);
            }
            else
            {
                Parapets(k, bx0, bx1, zb, zf, 4.2f, 0.8f, wall, brand);
                Bx(k, bx0 - 0.04f, 4.2f, zb - 0.04f, bx1 + 0.04f, 4.75f, zf + 0.04f, brand, Walls);
                Bx(k, bx0 + 0.3f, 2.85f, zf, bx0 + 2.7f, 5.8f, zf + 0.4f, brand, NoBottom);
                Bx(k, bx0 + 0.3f, 2.85f, zf + 0.4f, bx0 + 2.7f, 3.0f, zf + 1.6f, brand);
                Letters(k, SignOf(s, "BURGERS"), cx + 1.3f, 3.2f, zf, bx1 - bx0 - 4f, 0.8f, k.Glow(brandC));
                Rtu(k, cx, 4.2f, (zf + zb) * 0.5f, 1.8f, 1.2f, 0.9f);
            }
            // drive-thru lane: pavement, curb, arrows, clearance bar, menu board + speaker
            float lx0 = bx1, lx1 = hw;
            QY(k, lx0, -D, lx1, 0f, 0.012f, k.b.K.M(MapMaterials.Asphalt));
            Bx(k, lx1 - 0.25f, 0f, -D + 1f, lx1, 0.15f, -0.6f, k.conc, NoBottom);
            float ax = (lx0 + lx1) * 0.5f + 0.3f;
            foreach (float az in new[] { zb - 2f, zf - 4.5f })
            {
                QY(k, ax - 0.12f, az - 1.2f, ax + 0.12f, az, 0.016f, k.white);
                k.b.B.AddTriangle(new Vector3(ax - 0.4f, 0.016f, az), new Vector3(ax + 0.4f, 0.016f, az), new Vector3(ax, 0.016f, az + 0.7f), k.white, Vector3.up);
            }
            float barZ = zb - 0.6f;
            Bx(k, lx0 + 0.4f, 0f, barZ - 0.06f, lx0 + 0.52f, 3.0f, barZ + 0.06f, k.yellow);
            Bx(k, lx1 - 0.52f, 0f, barZ - 0.06f, lx1 - 0.4f, 3.0f, barZ + 0.06f, k.yellow);
            Bx(k, lx0 + 0.4f, 2.75f, barZ - 0.08f, lx1 - 0.4f, 2.9f, barZ + 0.08f, k.yellow);
            Letters(k, "CLEARANCE 9 FT", (lx0 + lx1) * 0.5f, 2.765f, barZ + 0.08f, lx1 - lx0 - 1.2f, 0.12f, k.black);
            k.b.BeginLocal(new Vector3(bx1 + 0.8f, 0f, zb + 2.2f), Quaternion.Euler(0f, 90f, 0f));
            Bx(k, -1.0f, 0f, -0.15f, 1.0f, 0.55f, 0.15f, k.b.K.M(SurfaceKind.ConcreteBlock));
            Bx(k, -1.05f, 0.55f, -0.12f, 1.05f, 2.1f, 0.12f, k.black);
            QZ(k, -0.95f, 0.65f, 0.95f, 2.0f, 0.121f, k.Glow(new Color(0.95f, 0.9f, 0.75f)));
            for (int i = 0; i < 4; i++) QZ(k, -0.85f, 0.85f + i * 0.28f, 0.85f, 0.88f + i * 0.28f, 0.123f, k.Flat(new Color(0.35f, 0.15f, 0.1f)));
            Bx(k, -0.95f, 1.75f, 0.121f, 0.95f, 1.98f, 0.125f, brand, Shown);
            Bx(k, 1.6f, 0f, -0.12f, 1.85f, 1.35f, 0.12f, k.black);
            Bx(k, 1.5f, 1.35f, -0.2f, 1.95f, 1.45f, 0.3f, brand);
            k.b.EndLocal();
            // pylon sign at the front corner
            float px = -hw + 1.0f, pz = -0.6f;
            Cyl(k, px, 0f, pz, 0.3f, 7.0f, 12, k.Paint(new Color(0.5f, 0.5f, 0.52f)));
            Bx(k, px - 1.4f, 7.0f, pz - 0.3f, px + 1.4f, 8.9f, pz + 0.3f, k.black);
            QZ(k, px - 1.32f, 7.08f, px + 1.32f, 8.82f, pz + 0.301f, k.Glow(brandC));
            QZ(k, px - 1.32f, 7.08f, px + 1.32f, 8.82f, pz - 0.301f, k.Glow(brandC), true);
            Letters(k, SignOf(s, "BURGERS"), px, 7.25f, pz + 0.301f, 2.4f, 1.3f, k.white);
            Solid(k, px - 0.3f, 0f, pz - 0.3f, px + 0.3f, 7f, pz + 0.3f);
        }

        // =====================================================================================================
        // motel — 2-storey motel with exterior walkway
        // =====================================================================================================

        static void Motel(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(24f, s.Width), D = Mathf.Max(12f, s.Depth), hw = W * 0.5f;
            const float zf = -3.4f, fl = 2.8f, H = 5.75f, walk = 1.8f;
            float rd = Mathf.Clamp(D - 3.6f, 7f, 10f), zb = zf - rd;
            float officeW = Mathf.Clamp(W * 0.18f, 6f, 9f), rx0 = -hw + officeW + 0.5f, rx1 = hw;
            float look = b.R();
            int wall = look < 0.4f ? k.b.K.M(MapMaterials.Stucco(s.Color)) : look < 0.7f ? k.Brick(s.Color) : k.b.K.M(MapMaterials.Siding(s.Color));
            int doorC = k.Paint(s.Accent);
            int rail = b.R() < 0.5f ? k.trim : k.Paint(s.Accent);
            int roofS = k.b.K.M(MapMaterials.Shingle(new Color(0.5f, 0.42f, 0.36f)));
            Bx(k, rx0, 0f, zb, rx1, H, zf, wall, Walls);
            Solid(k, rx0, 0f, zb, rx1, H + 1.5f, zf);
            Bx(k, rx0, 0f, zf, rx1, 0.15f, zf + walk + 0.2f, k.conc, NoBottom);
            Bx(k, rx0, fl - 0.2f, zf, rx1, fl, zf + walk, k.conc);
            Solid(k, rx0, fl - 0.2f, zf, rx1, fl, zf + walk);
            // posts + railing
            for (float x = rx0 + 0.15f; x <= rx1 - 0.1f; x += Mathf.Max(3.5f, (rx1 - rx0 - 0.3f) / Mathf.Max(1, Mathf.RoundToInt((rx1 - rx0 - 0.3f) / 4f))))
                Bx(k, x - 0.07f, 0.15f, zf + walk - 0.17f, x + 0.07f, H, zf + walk - 0.03f, rail, Walls);
            Bx(k, rx1 - 0.22f, 0.15f, zf + walk - 0.17f, rx1 - 0.08f, H, zf + walk - 0.03f, rail, Walls);
            float stairX1 = rx1 - 1.0f, stairX0 = stairX1 - 16 * 0.27f;
            foreach (float y in new[] { fl + 0.35f, fl + 0.65f, fl + 1.05f })
            {
                Bx(k, rx0, y - 0.025f, zf + walk - 0.1f, stairX1, y + 0.025f, zf + walk - 0.05f, rail);
                Bx(k, stairX1 + 0.9f, y - 0.025f, zf + walk - 0.1f, rx1, y + 0.025f, zf + walk - 0.05f, rail);
            }
            Bx(k, rx0, fl, zf + walk - 0.12f, rx0 + 0.05f, fl + 1.05f, zf + walk - 0.03f, rail);
            // rooms
            const float rw = 4.0f;
            int nRooms = Mathf.Min(14, Mathf.FloorToInt((rx1 - rx0) / rw));
            float rs = (rx1 - rx0) / Mathf.Max(1, nRooms);
            int lamp = k.Glow(new Color(1f, 0.85f, 0.6f));
            for (int f = 0; f < 2; f++)
            {
                float yb = f == 0 ? 0.15f : fl;
                for (int i = 0; i < nRooms; i++)
                {
                    float x0 = rx0 + i * rs;
                    Door(k, x0 + 0.9f, yb, 0.9f, 2.03f, zf, k.trim, doorC, false);
                    Window(k, x0 + 2.6f, yb + 0.95f, 1.5f, 1.15f, zf, k.alum, 2, 1, -1, -1, b.R() < 0.6f ? k.curtain : k.dark);
                    Bx(k, x0 + 2.1f, yb + 0.3f, zf, x0 + 3.1f, yb + 0.72f, zf + 0.06f, k.galv, Shown);
                    Bx(k, x0 + 1.5f, yb + 1.95f, zf, x0 + 1.62f, yb + 2.15f, zf + 0.1f, lamp);
                }
            }
            // roof (ridge along x, eave over the walkway)
            float zr0 = zb, zr1 = zf + walk, czr = (zr0 + zr1) * 0.5f, hsz = (zr1 - zr0) * 0.5f, cxr = (rx0 + rx1) * 0.5f, hl = (rx1 - rx0) * 0.5f;
            k.b.BeginLocal(new Vector3(cxr, 0f, czr), Quaternion.Euler(0f, 90f, 0f));
            GableZ(k, -hsz, hsz, -hl, hl, H, 1.5f, 0.4f, roofS, wall, k.trim);
            k.b.EndLocal();
            QY(k, rx0, zf, rx1, zf + walk, H - 0.01f, k.white, false);
            // stair (parallel to the facade, in front of the walkway, rising toward +X)
            float sz0 = zf + walk + 0.05f, sz1 = sz0 + 1.1f;
            for (int i = 0; i < 16; i++)
            {
                float x = stairX0 + i * 0.27f, y = (i + 1) * (fl / 16f);
                Bx(k, x, y - 0.04f, sz0, x + 0.3f, y, sz1, k.conc);
            }
            float sl = Mathf.Sqrt(16 * 0.27f * 16 * 0.27f + fl * fl), sa = Mathf.Atan2(fl, 16 * 0.27f) * Mathf.Rad2Deg;
            var mid = new Vector3((stairX0 + stairX1) * 0.5f, fl * 0.5f - 0.1f, 0f);
            foreach (float z in new[] { sz0 - 0.03f, sz1 + 0.03f })
            {
                k.b.B.AddBox(new Vector3(mid.x, mid.y, z), new Vector3(sl, 0.25f, 0.05f), Quaternion.Euler(0f, 0f, sa), rail);
                k.b.B.AddBox(new Vector3(mid.x, mid.y + 0.95f, z), new Vector3(sl, 0.05f, 0.05f), Quaternion.Euler(0f, 0f, sa), rail);
            }
            Bx(k, stairX1, fl - 0.2f, sz0, stairX1 + 0.9f, fl, sz1, k.conc);
            Bx(k, stairX1 + 0.8f, 0f, sz1 - 0.1f, stairX1 + 0.9f, fl - 0.2f, sz1, rail);
            // office
            float ox0 = -hw, ox1 = -hw + officeW, oz1 = -1.2f, oz0 = Mathf.Max(zb, oz1 - 8f);
            Bx(k, ox0, 0f, oz0, ox1, 3.6f, oz1, wall, Walls);
            Solid(k, ox0, 0f, oz0, ox1, 4.4f, oz1);
            Parapets(k, ox0, ox1, oz0, oz1, 3.6f, 0.7f, wall, k.alum);
            Bx(k, ox0 - 0.03f, 3.6f, oz1 - 0.3f, ox1 + 0.03f, 4.3f, oz1 + 0.04f, k.Paint(s.Accent), NoBottom);
            Letters(k, "OFFICE", (ox0 + ox1) * 0.5f, 3.7f, oz1 + 0.04f, officeW - 1f, 0.5f, k.white);
            Ribbon(k, ox0 + 0.6f, ox1 - 2.0f, 0.7f, 2.1f, oz1, k.alum, 1.4f, k.lit);
            Door(k, ox1 - 1.2f, 0f, 0.95f, 2.13f, oz1, k.alum, k.alum, true, 1, 0.5f);
            Letters(k, "VACANCY", ox0 + 1.6f, 2.2f, oz1 + 0.04f, 1.6f, 0.25f, k.Glow(new Color(1f, 0.2f, 0.25f)));
            // pylon sign
            float px = -hw + 1.8f, pz = -0.5f;
            foreach (float dx in new[] { -0.9f, 0.9f }) Bx(k, px + dx - 0.1f, 0f, pz - 0.1f, px + dx + 0.1f, 5.0f, pz + 0.1f, k.Paint(new Color(0.5f, 0.5f, 0.52f)));
            float sw = 3.4f;
            Bx(k, px - sw * 0.5f, 5.0f, pz - 0.25f, px + sw * 0.5f, 6.9f, pz + 0.25f, k.Paint(s.Accent));
            Bx(k, px - sw * 0.5f + 0.1f, 5.1f, pz + 0.25f, px + sw * 0.5f - 0.1f, 6.8f, pz + 0.27f, k.white, Shown);
            Letters(k, SignOf(s, "MOTEL"), px, 5.25f, pz + 0.27f, sw - 0.5f, 1.4f, k.Glow(Pick(b, SignColors)));
            Bx(k, px - 1.1f, 4.1f, pz - 0.12f, px + 1.1f, 4.75f, pz + 0.12f, k.black);
            Letters(k, b.R() < 0.7f ? "VACANCY" : "NO VACANCY", px, 4.22f, pz + 0.12f, 2.0f, 0.4f, k.Glow(new Color(1f, 0.2f, 0.3f)));
        }

        // =====================================================================================================
        // autorepair — service bays + office
        // =====================================================================================================

        static void AutoRepair(MapBuild b, MapBuildingSpec s)
        {
            var k = new Kit(b, s);
            float W = Mathf.Max(16f, s.Width), D = Mathf.Max(12f, s.Depth), hw = W * 0.5f - 0.03f;
            int nb = s.Units >= 3 ? Mathf.Clamp(s.Units, 3, 4) : (W >= 26f ? 4 : 3);
            float bayP = Mathf.Min(4.3f, (W - 6f) / nb);
            float doorW = Mathf.Min(3.05f, bayP - 0.9f);
            const float doorH = 3.66f, H = 5.4f;
            float apron = Mathf.Clamp(D * 0.3f, 5f, 9f), zf = -apron;
            bool metal = b.R() < 0.55f;
            int wall = metal ? k.b.K.M(MapMaterials.MetalPanel(s.Color)) : k.b.K.M(SurfaceKind.ConcreteBlock, s.Color);
            int cmu = k.b.K.M(SurfaceKind.ConcreteBlock, new Color(0.75f, 0.75f, 0.73f));
            Bx(k, -hw, 0f, -D, hw, H, zf, wall, Walls);
            Solid(k, -hw, 0f, -D, hw, H + 0.6f, zf);
            if (metal) Bx(k, -hw - 0.03f, 0f, -D - 0.03f, hw + 0.03f, 1.2f, zf + 0.03f, cmu, Walls);
            Parapets(k, -hw, hw, -D, zf, H, 0.6f, wall, k.alum);
            QY(k, -hw, zf, hw, 0f, 0.02f, k.conc);
            QY(k, -hw, zf + 0.3f, hw, zf + 0.33f, 0.025f, k.Flat(new Color(0.35f, 0.35f, 0.35f)));
            float bx1 = hw - 0.4f, bx0 = bx1 - nb * bayP;
            int doorC = b.R() < 0.5f ? k.white : k.Paint(s.Accent);
            for (int i = 0; i < nb; i++)
            {
                float cx = bx1 - bayP * (i + 0.5f);
                if (b.R() < 0.35f)
                {   // open bay: dark shop, lift posts, rolled-up door
                    QZ(k, cx - doorW * 0.5f, 0f, cx + doorW * 0.5f, doorH, zf + 0.005f, k.interior);
                    QZ(k, cx - doorW * 0.5f, doorH - 0.45f, cx + doorW * 0.5f, doorH, zf + 0.01f, doorC);
                    foreach (float sx in new[] { -1f, 1f })
                        Bx(k, cx + sx * (doorW * 0.5f - 0.3f) - 0.12f, 0f, zf + 0.01f, cx + sx * (doorW * 0.5f - 0.3f) + 0.12f, 3.2f, zf + 0.2f, k.Paint(new Color(0.85f, 0.15f, 0.1f)), Shown);
                    Bx(k, cx - doorW * 0.5f, 0f, zf, cx + doorW * 0.5f, 0.012f, zf + 0.2f, k.dark);
                    Bx(k, cx - doorW * 0.5f - 0.12f, 0f, zf, cx - doorW * 0.5f, doorH + 0.12f, zf + 0.08f, k.trim, Shown);
                    Bx(k, cx + doorW * 0.5f, 0f, zf, cx + doorW * 0.5f + 0.12f, doorH + 0.12f, zf + 0.08f, k.trim, Shown);
                    Bx(k, cx - doorW * 0.5f, doorH, zf, cx + doorW * 0.5f, doorH + 0.12f, zf + 0.08f, k.trim, Shown);
                }
                else OverheadDoor(k, cx, 0f, doorW, doorH, zf, doorC, true, k.trim);
                foreach (float sx in new[] { -1f, 1f }) Cyl(k, cx + sx * (doorW * 0.5f + 0.25f), 0f, zf + 0.3f, 0.1f, 1.0f, 8, k.yellow);
            }
            // office
            float ox0 = -hw + 0.5f, ox1 = bx0 - 0.4f;
            if (ox1 - ox0 > 2.5f)
            {
                Door(k, ox1 - 0.8f, 0f, 0.95f, 2.13f, zf, k.alum, k.alum, true, 1, 0.5f);
                Ribbon(k, ox0 + 0.2f, ox1 - 1.6f, 0.75f, 2.0f, zf, k.alum, 1.4f, k.interior);
                Letters(k, "BRAKES - TIRES - OIL", (ox0 + ox1) * 0.5f, 3.05f, zf, ox1 - ox0 - 0.4f, 0.28f, k.Flat(new Color(0.12f, 0.12f, 0.14f)));
                int tire = k.b.K.M(SurfaceKind.Tire);
                for (int t = 0; t < 2; t++)
                    for (int j = 0; j < 4 - t; j++)
                        Cyl(k, ox0 + 0.6f + t * 0.8f, j * 0.22f, zf + 0.7f, 0.33f, 0.21f, 10, tire);
            }
            // sign band across the front
            Bx(k, -hw + 0.3f, 4.0f, zf, hw - 0.3f, 5.1f, zf + 0.06f, k.white, Shown);
            Letters(k, SignOf(s, "AUTO REPAIR"), 0f, 4.12f, zf + 0.06f, W - 1.6f, 0.86f, k.Flat(Pick(b, SignColors)));
            OnBack(k, -D, -hw + 1f, hw - 1f, W, lx => Door(k, lx, 0f, 0.92f, 2.13f, 0f, k.unit, k.unit, false));
            OnSide(k, false, -hw, -D + 1f, zf - 1f, 3f, lx => Window(k, lx, 1.4f, 1.2f, 0.9f, 0f, k.alum, 2, 1, -1, -1, k.dark));
            Rtu(k, -hw * 0.4f, H, -D * 0.5f, 1.6f, 1.1f, 0.9f);
        }
    }
}
