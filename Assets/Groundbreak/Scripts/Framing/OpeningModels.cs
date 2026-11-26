using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Real-dimension procedural models for the shell's openings, plugged into ProductModels.Overrides:
    /// vinyl single/double-hung and slider windows (frame, sashes, meeting rails, insulated glass, nailing fin, half screen),
    /// a six-panel steel prehung exterior door (jambs, stops, weatherstrip, threshold, hinges, lockset bores, brickmold),
    /// a vinyl sliding patio door and a steel sectional garage door with tracks, rollers and torsion springs.
    /// All geometry is authored in inches here and converted to meters; origin = center of the catalog size box,
    /// +Z = exterior. Units keep the catalog sizes so they still snap into the same rough openings.
    /// </summary>
    public static class OpeningModels
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (!ProductModels.Overrides.Contains(Build)) ProductModels.Overrides.Add(Build);
        }

        /// <summary>True when this definition gets one of the detailed models below.</summary>
        public static bool Handles(PieceDefinition d) => Kind(d) != 0;

        static int Kind(PieceDefinition d)
        {
            if (d == null) return 0;
            string id = d.Id ?? "";
            switch (d.Category)
            {
                case PieceCategory.Window:
                    if (id.Contains("-dh-")) return 1;
                    if (id.Contains("-sh-")) return 2;
                    if (id.Contains("slider")) return 3;
                    return 0;
                case PieceCategory.Door:
                    if (id.Contains("ext") && id.Contains("steel")) return 4;
                    if (id.Contains("slider")) return 5;
                    return 0;
                case PieceCategory.GarageDoor:
                    return id.Contains("opener") || id.Contains("carr") ? 0 : 6;
            }
            return 0;
        }

        static bool Build(PieceDefinition d, Vector3 s, MeshBuilder b, Func<Material, int> mat)
        {
            int kind = Kind(d);
            if (kind == 0) return false;
            var k = new K(b, mat);
            float W = Units.ToInches(s.x), H = Units.ToInches(s.y), D = Units.ToInches(s.z);
            switch (kind)
            {
                case 1: HungWindow(k, W, H, D, true); break;
                case 2: HungWindow(k, W, H, D, false); break;
                case 3: SliderWindow(k, W, H, D); break;
                case 4: PrehungSixPanel(k, W, H); break;
                case 5: PatioSlider(k, W, H, D); break;
                case 6: Sectional(k, W, H, D); break;
            }
            return true;
        }

        // ================================================================== helpers (inches)

        static float I(float v) => v * Units.Inch;
        static Vector3 V(float x, float y, float z) => new Vector3(I(x), I(y), I(z));

        sealed class K
        {
            public readonly MeshBuilder B;
            readonly Func<Material, int> mat;
            public K(MeshBuilder b, Func<Material, int> m) { B = b; mat = m; }
            public int M(SurfaceKind s) => mat(Materials.Get(s));
            public int M(SurfaceKind s, Color tint) => mat(Materials.Get(s, tint));
            public int Mat(Material m) => mat(m);

            public void Box(float x0, float x1, float y0, float y1, float z0, float z1, int sub, Vector3 u = default)
            {
                if (Mathf.Abs(x1 - x0) < 1e-4f || Mathf.Abs(y1 - y0) < 1e-4f || Mathf.Abs(z1 - z0) < 1e-4f) return;
                B.AddBox(V((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                         new Vector3(I(Mathf.Abs(x1 - x0)), I(Mathf.Abs(y1 - y0)), I(Mathf.Abs(z1 - z0))), Quaternion.identity, sub, u);
            }

            /// <summary>Convex profile given as (z, y) points, extruded along X from x0 to x1.</summary>
            public void ExtrudeX(Vector2[] zy, float x0, float x1, int sub)
            {
                var poly = new Vector2[zy.Length];
                for (int i = 0; i < zy.Length; i++) poly[i] = new Vector2(-I(zy[i].x), I(zy[i].y));
                B.AddExtrude(poly, V((x0 + x1) * 0.5f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), I(x1 - x0), sub);
            }

            /// <summary>Round rod between two points (inches).</summary>
            public void Rod(Vector3 a, Vector3 b, float r, int segs, int sub)
            {
                Vector3 d = b - a;
                if (d.sqrMagnitude < 1e-6f) return;
                B.AddRod(V((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, (a.z + b.z) * 0.5f), Quaternion.LookRotation(d.normalized), I(r), I(d.magnitude), segs, sub);
            }
        }

        static Color VinylWhite => new Color(0.97f, 0.97f, 0.95f);

        static Material _screen;
        /// <summary>Charcoal fiberglass insect screen: see-through dark mesh.</summary>
        static Material ScreenMat()
        {
            if (_screen != null) return _screen;
            _screen = new Material(Materials.Get(SurfaceKind.Glass)) { name = "GB_ScreenMesh", hideFlags = HideFlags.DontUnloadUnusedAsset };
            _screen.color = new Color(0.08f, 0.08f, 0.09f, 0.5f);
            _screen.SetFloat("_Glossiness", 0.15f);
            return _screen;
        }

        /// <summary>Insulated glass unit (two 1/8" lites on a 1/2" spacer, 3/4" overall). Edges run 1/2" into the sash; the spacer
        /// shows 1/4" past the sightline.</summary>
        static void Igu(K k, float x0, float x1, float y0, float y1, float zc, int glass, int spacer)
        {
            float e = 0.5f;
            k.Box(x0 - e, x1 + e, y0 - e, y1 + e, zc - 0.375f, zc - 0.25f, glass);
            k.Box(x0 - e, x1 + e, y0 - e, y1 + e, zc + 0.25f, zc + 0.375f, glass);
            float sp = 0.25f, z0 = zc - 0.25f, z1 = zc + 0.25f;
            k.Box(x0 - e, x1 + e, y1 - sp, y1 + e, z0, z1, spacer);
            k.Box(x0 - e, x1 + e, y0 - e, y0 + sp, z0, z1, spacer);
            k.Box(x0 - e, x0 + sp, y0 + sp, y1 - sp, z0, z1, spacer);
            k.Box(x1 - sp, x1 + e, y0 + sp, y1 - sp, z0, z1, spacer);
        }

        /// <summary>Vinyl sash: stiles/rails of given widths, interior glazing bead, IGU. zc = sash center, d = sash depth.</summary>
        static void Sash(K k, float x0, float x1, float y0, float y1, float zc, float d, float sl, float sr, float st, float sb, int vinyl, int glass, int spacer)
        {
            float z0 = zc - d / 2, z1 = zc + d / 2;
            k.Box(x0, x0 + sl, y0, y1, z0, z1, vinyl, Vector3.up);
            k.Box(x1 - sr, x1, y0, y1, z0, z1, vinyl, Vector3.up);
            k.Box(x0 + sl, x1 - sr, y1 - st, y1, z0, z1, vinyl, Vector3.right);
            k.Box(x0 + sl, x1 - sr, y0, y0 + sb, z0, z1, vinyl, Vector3.right);
            float ix0 = x0 + sl, ix1 = x1 - sr, iy0 = y0 + sb, iy1 = y1 - st;
            // exterior bevel step: a 1/4" lip that reads as the sloped vinyl profile
            float lip = 0.25f, lz = 0.12f;
            k.Box(ix0 - lip, ix1 + lip, iy1, iy1 + lip, z1, z1 + lz, vinyl);
            k.Box(ix0 - lip, ix1 + lip, iy0 - lip, iy0, z1, z1 + lz, vinyl);
            k.Box(ix0 - lip, ix0, iy0, iy1, z1, z1 + lz, vinyl);
            k.Box(ix1, ix1 + lip, iy0, iy1, z1, z1 + lz, vinyl);
            // interior snap-in glazing bead
            float bw = 0.3f, bz = 0.35f;
            k.Box(ix0, ix1, iy1 - bw, iy1, z0, z0 + bz, vinyl);
            k.Box(ix0, ix1, iy0, iy0 + bw, z0, z0 + bz, vinyl);
            k.Box(ix0, ix0 + bw, iy0 + bw, iy1 - bw, z0, z0 + bz, vinyl);
            k.Box(ix1 - bw, ix1, iy0 + bw, iy1 - bw, z0, z0 + bz, vinyl);
            Igu(k, ix0, ix1, iy0, iy1, zc + 0.05f, glass, spacer);
        }

        /// <summary>Integral nailing fin, 1-1/4" wide, back face on the sheathing plane (zf), with pre-punched nail slots.</summary>
        static void NailingFin(K k, float x0, float x1, float y0, float y1, float zf, int vinyl, int dark)
        {
            const float w = 1.25f, t = 0.06f;
            k.Box(x0 - w, x1 + w, y1, y1 + w, zf, zf + t, vinyl);
            k.Box(x0 - w, x1 + w, y0 - w, y0, zf, zf + t, vinyl);
            k.Box(x0 - w, x0, y0, y1, zf, zf + t, vinyl);
            k.Box(x1, x1 + w, y0, y1, zf, zf + t, vinyl);
            float zs = zf + t, ze = zs + 0.01f, c = 0.6f;
            for (float x = x0 + 3f; x < x1 - 2f; x += 8f)
            {
                k.Box(x - 0.35f, x + 0.35f, y1 + c - 0.08f, y1 + c + 0.08f, zs, ze, dark);
                k.Box(x - 0.35f, x + 0.35f, y0 - c - 0.08f, y0 - c + 0.08f, zs, ze, dark);
            }
            for (float y = y0 + 3f; y < y1 - 2f; y += 8f)
            {
                k.Box(x0 - c - 0.08f, x0 - c + 0.08f, y - 0.35f, y + 0.35f, zs, ze, dark);
                k.Box(x1 + c - 0.08f, x1 + c + 0.08f, y - 0.35f, y + 0.35f, zs, ze, dark);
            }
        }

        /// <summary>Main frame: head, jambs and a sloped sill (exterior lower) with weep slots. Returns the daylight opening.</summary>
        static void WindowFrame(K k, float hx, float hy, float hz, float fw, float sillIn, float sillOut, int vinyl, int dark,
                                out float ox0, out float ox1, out float oy0, out float oy1)
        {
            k.Box(-hx, hx, hy - fw, hy, -hz, hz, vinyl, Vector3.right);
            k.Box(-hx, -hx + fw, -hy + sillOut * 0.5f, hy - fw, -hz, hz, vinyl, Vector3.up);
            k.Box(hx - fw, hx, -hy + sillOut * 0.5f, hy - fw, -hz, hz, vinyl, Vector3.up);
            k.ExtrudeX(new[]
            {
                new Vector2(-hz, -hy), new Vector2(hz, -hy), new Vector2(hz, -hy + sillOut),
                new Vector2(-hz + 0.75f, -hy + sillIn), new Vector2(-hz, -hy + sillIn)
            }, -hx + fw - 0.01f, hx - fw + 0.01f, vinyl);
            for (int side = -1; side <= 1; side += 2)
                k.Box(side * (hx - 6f) - 0.5f, side * (hx - 6f) + 0.5f, -hy + sillOut * 0.4f, -hy + sillOut * 0.4f + 0.2f, hz, hz + 0.01f, dark);
            ox0 = -hx + fw; ox1 = hx - fw; oy0 = -hy + sillIn; oy1 = hy - fw;
        }

        /// <summary>Aluminum half/insect screen in the exterior screen channel.</summary>
        static void Screen(K k, float x0, float x1, float y0, float y1, float z0, float z1, int frame, bool tabsBottom)
        {
            float w = 0.9f;
            k.Box(x0, x0 + w, y0, y1, z0, z1, frame, Vector3.up);
            k.Box(x1 - w, x1, y0, y1, z0, z1, frame, Vector3.up);
            k.Box(x0 + w, x1 - w, y1 - w, y1, z0, z1, frame, Vector3.right);
            k.Box(x0 + w, x1 - w, y0, y0 + w, z0, z1, frame, Vector3.right);
            float zc = (z0 + z1) * 0.5f;
            k.Box(x0 + w - 0.1f, x1 - w + 0.1f, y0 + w - 0.1f, y1 - w + 0.1f, zc - 0.01f, zc + 0.01f, k.Mat(ScreenMat()));
            int dark = k.M(SurfaceKind.DarkPlastic);
            if (tabsBottom)
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = Mathf.Lerp(x0, x1, s < 0 ? 0.2f : 0.8f);
                    k.Box(x - 0.5f, x + 0.5f, y0 - 0.35f, y0 + 0.2f, z1, z1 + 0.05f, dark); // pull tabs
                }
        }

        // ================================================================== hung windows

        static void HungWindow(K k, float W, float H, float D, bool doubleHung)
        {
            float hx = W / 2, hy = H / 2, hz = D / 2;
            int vinyl = k.M(SurfaceKind.PaintedWhite, VinylWhite), glass = k.M(SurfaceKind.Glass), spacer = k.M(SurfaceKind.BrushedSteel);
            int dark = k.M(SurfaceKind.DarkPlastic), hw = k.M(SurfaceKind.Nickel), alum = k.M(SurfaceKind.PaintedWhite, new Color(0.92f, 0.92f, 0.9f));
            WindowFrame(k, hx, hy, hz, 1.5f, 1.75f, 0.9f, vinyl, dark, out float ox0, out float ox1, out float oy0, out float oy1);

            // jamb liners: interior stop, parting stop (between the two sash tracks), exterior blind stop
            for (int side = -1; side <= 1; side += 2)
            {
                float xa = side < 0 ? ox0 : ox1 - 0.45f, xb = side < 0 ? ox0 + 0.45f : ox1;
                float xp0 = side < 0 ? ox0 : ox1 - 0.4f, xp1 = side < 0 ? ox0 + 0.4f : ox1;
                k.Box(xa, xb, oy0, oy1, -hz, -hz + 0.3f, vinyl, Vector3.up);
                k.Box(xp0, xp1, oy0, oy1, -0.08f, 0.08f, vinyl, Vector3.up);
                k.Box(xa, xb, oy0, oy1, hz - 0.2f, hz, vinyl, Vector3.up);
            }
            k.Box(ox0, ox1, oy1 - 0.45f, oy1, -hz, -hz + 0.3f, vinyl, Vector3.right);
            k.Box(ox0, ox1, oy1 - 0.45f, oy1, hz - 0.2f, hz, vinyl, Vector3.right);

            // sashes: upper rides the exterior track, lower the interior track; meeting rails side by side at mid height
            float mid = (oy0 + oy1) * 0.5f + 0.5f, sd = 1.0f, zU = 0.62f, zL = -0.62f;
            float sx0 = ox0 - 0.25f, sx1 = ox1 + 0.25f;
            Sash(k, sx0, sx1, mid - 0.7f, oy1 + 0.25f, zU, sd, 1.75f, 1.75f, 1.75f, 1.4f, vinyl, glass, spacer);
            Sash(k, sx0, sx1, oy0 - 0.1f, mid + 0.7f, zL, sd, 1.75f, 1.75f, 1.4f, 2.6f, vinyl, glass, spacer);
            // meeting-rail interlock fins
            k.Box(ox0 + 1.5f, ox1 - 1.5f, mid - 0.7f, mid + 0.25f, zL + sd / 2, zL + sd / 2 + 0.12f, vinyl, Vector3.right);
            k.Box(ox0 + 1.5f, ox1 - 1.5f, mid - 0.25f, mid + 0.7f, zU - sd / 2 - 0.12f, zU - sd / 2, vinyl, Vector3.right);

            // cam sash lock on the lower meeting rail, keeper on the upper
            float ly = mid + 0.7f;
            k.Box(-1.3f, 1.3f, ly, ly + 0.35f, zL - 0.4f, zL + 0.35f, hw);
            k.Box(-0.35f, 0.35f, ly + 0.35f, ly + 0.55f, zL - 0.25f, zL + 0.2f, hw);
            k.Box(0.2f, 2.1f, ly + 0.55f, ly + 0.72f, zL - 0.15f, zL + 0.1f, hw); // lever arm
            k.Box(-1.0f, 1.0f, ly, ly + 0.4f, zU - 0.35f, zU + 0.1f, hw);          // keeper
            // finger lifts on the lower sash bottom rail
            float zf = zL - sd / 2;
            for (int i = -1; i <= 1; i += 2)
            {
                float x = i * (ox1 - ox0) * 0.3f;
                k.Box(x - 1.1f, x + 1.1f, oy0 + 0.7f, oy0 + 1.2f, zf - 0.45f, zf, vinyl);
                k.Box(x - 1.1f, x + 1.1f, oy0 + 1.2f, oy0 + 1.35f, zf - 0.45f, zf - 0.25f, vinyl);
            }
            // tilt latches (double-hung only: both sashes tilt in for cleaning)
            if (doubleHung)
                for (int i = -1; i <= 1; i += 2)
                {
                    float tx = i < 0 ? sx0 + 1.05f : sx1 - 1.05f;
                    k.Box(tx - 0.45f, tx + 0.45f, ly, ly + 0.18f, zL - 0.3f, zL + 0.3f, vinyl);
                    k.Box(tx - 0.2f, tx + 0.2f, ly + 0.18f, ly + 0.3f, zL - 0.1f, zL + 0.2f, dark);
                }

            // half screen over the lower (operable) sash in the exterior channel
            Screen(k, ox0 - 0.1f, ox1 + 0.1f, oy0, mid + 0.7f, hz - 0.5f, hz - 0.22f, alum, true);
            NailingFin(k, -hx, hx, -hy, hy, hz - 0.75f, vinyl, dark);
        }

        // ================================================================== slider window

        static void SliderWindow(K k, float W, float H, float D)
        {
            float hx = W / 2, hy = H / 2, hz = D / 2;
            int vinyl = k.M(SurfaceKind.PaintedWhite, VinylWhite), glass = k.M(SurfaceKind.Glass), spacer = k.M(SurfaceKind.BrushedSteel);
            int dark = k.M(SurfaceKind.DarkPlastic), alum = k.M(SurfaceKind.PaintedWhite, new Color(0.92f, 0.92f, 0.9f));
            WindowFrame(k, hx, hy, hz, 1.5f, 1.6f, 0.9f, vinyl, dark, out float ox0, out float ox1, out float oy0, out float oy1);
            // head and sill tracks: interior stop, parting rail, exterior stop
            k.Box(ox0, ox1, oy1 - 0.45f, oy1, -hz, -hz + 0.3f, vinyl, Vector3.right);
            k.Box(ox0, ox1, oy1 - 0.4f, oy1, -0.08f, 0.08f, vinyl, Vector3.right);
            k.Box(ox0, ox1, oy1 - 0.45f, oy1, hz - 0.2f, hz, vinyl, Vector3.right);
            k.Box(ox0, ox1, oy0, oy0 + 0.35f, -0.08f, 0.08f, vinyl, Vector3.right);
            k.Box(ox0, ox1, oy0, oy0 + 0.3f, -hz, -hz + 0.25f, vinyl, Vector3.right);

            float mid = (ox0 + ox1) * 0.5f, sd = 1.0f, zE = 0.62f, zI = -0.62f;
            float sy0 = oy0 - 0.1f, sy1 = oy1 + 0.25f;
            // fixed lite on the exterior track (left), sliding sash on the interior track (right)
            Sash(k, ox0 - 0.25f, mid + 0.7f, sy0, sy1, zE, sd, 1.75f, 1.4f, 1.75f, 1.75f, vinyl, glass, spacer);
            Sash(k, mid - 0.7f, ox1 + 0.25f, sy0, sy1, zI, sd, 1.4f, 1.75f, 1.75f, 1.75f, vinyl, glass, spacer);
            k.Box(mid - 0.7f, mid + 0.25f, oy0 + 1.5f, oy1 - 1.5f, zI + sd / 2, zI + sd / 2 + 0.12f, vinyl, Vector3.up);
            k.Box(mid - 0.25f, mid + 0.7f, oy0 + 1.5f, oy1 - 1.5f, zE - sd / 2 - 0.12f, zE - sd / 2, vinyl, Vector3.up);
            // sweep latch on the meeting stile, integral pull on the jamb stile
            float cy = (oy0 + oy1) * 0.5f, zf = zI - sd / 2;
            int hw = k.M(SurfaceKind.Nickel);
            k.Box(mid - 0.7f, mid + 0.5f, cy - 1.3f, cy + 1.3f, zf - 0.35f, zf, hw);
            k.Box(mid - 0.35f, mid + 0.15f, cy - 0.3f, cy + 1.9f, zf - 0.55f, zf - 0.35f, hw);
            k.Box(ox1 - 1.3f, ox1 - 0.4f, cy - 3f, cy + 3f, zf - 0.4f, zf, vinyl);
            Screen(k, mid - 0.7f, ox1 + 0.1f, oy0, oy1, hz - 0.5f, hz - 0.22f, alum, false);
            NailingFin(k, -hx, hx, -hy, hy, hz - 0.75f, vinyl, dark);
        }

        // ================================================================== prehung six-panel exterior door

        /// <summary>Raised (embossed) field rising from the panel face (z) by h toward dir, with 4 bevels.</summary>
        static void Raised(K k, float x0, float x1, float y0, float y1, float z, float dir, float h, float inset, int sub)
        {
            float zi = z + dir * h;
            Vector3 o00 = V(x0, y0, z), o10 = V(x1, y0, z), o11 = V(x1, y1, z), o01 = V(x0, y1, z);
            Vector3 i00 = V(x0 + inset, y0 + inset, zi), i10 = V(x1 - inset, y0 + inset, zi);
            Vector3 i11 = V(x1 - inset, y1 - inset, zi), i01 = V(x0 + inset, y1 - inset, zi);
            var n = new Vector3(0f, 0f, dir);
            k.B.AddQuad(o01, o11, i11, i01, sub, n + Vector3.up * 0.5f);
            k.B.AddQuad(o00, o10, i10, i00, sub, n + Vector3.down * 0.5f);
            k.B.AddQuad(o00, o01, i01, i00, sub, n + Vector3.left * 0.5f);
            k.B.AddQuad(o10, o11, i11, i10, sub, n + Vector3.right * 0.5f);
            k.B.AddQuad(i00, i10, i11, i01, sub, n);
        }

        // Exterior prehung geometry (inches, product space): jamb depth 4-9/16" for a 2x4 wall with 7/16" sheathing + 1/2" drywall.
        public const float JambDepthIn = 4.5625f, JambExtIn = 2.1875f;

        static void PrehungSixPanel(K k, float W, float H)
        {
            float hy = H / 2, floor = -hy;
            int primed = k.M(SurfaceKind.PaintedWhite), steel = k.M(SurfaceKind.PaintedSteel, new Color(0.97f, 0.97f, 0.96f));
            int alum = k.M(SurfaceKind.BrushedSteel, new Color(0.85f, 0.84f, 0.8f)), oak = k.M(SurfaceKind.Oak), dark = k.M(SurfaceKind.DarkPlastic);
            int hw = k.M(SurfaceKind.Nickel), wood = k.M(SurfaceKind.PineLumber);

            float slabW = W - 0.25f, sx = slabW / 2, gap = 0.125f, jt = 0.6875f;
            float jIn = sx + gap, jOut = jIn + jt;                       // 36" door: 18" / 18-11/16" (37-3/8" outside of jambs)
            float zJ1 = JambExtIn, zJ0 = JambExtIn - JambDepthIn;        // exterior edge on the sheathing plane
            float slabE = zJ1 - 0.625f, slabI = slabE - 1.75f;            // slab closes against the exterior stop
            float capTop = floor + 1.5f, slabBot = capTop + 0.2f, slabTop = slabBot + 79.5f;
            float headIn = slabTop + gap, headTop = headIn + jt;          // unit height ~82" in an 82-1/2" rough opening

            // --- threshold: mill aluminum, sloped to the exterior, oak adjustable cap under the slab
            float zNose = zJ1 + 1.5f;
            k.ExtrudeX(new[]
            {
                new Vector2(zJ0, floor), new Vector2(zNose, floor), new Vector2(zNose, floor + 0.35f),
                new Vector2(slabE + 0.3f, floor + 1.25f), new Vector2(zJ0, floor + 1.25f)
            }, -jOut, jOut, alum);
            k.Box(-jIn, jIn, floor + 1.25f, capTop, slabI - 0.15f, slabE + 0.15f, oak, Vector3.right);
            for (int i = 0; i < 4; i++) // cap adjusting screws
            {
                float x = Mathf.Lerp(-jIn + 4f, jIn - 4f, i / 3f);
                k.B.AddDisc(V(x, capTop + 0.005f, (slabI + slabE) * 0.5f), Quaternion.identity, I(0.17f), 8, hw, true);
            }

            // --- jambs (primed finger-joint), integral stops with kerf-in compression weatherstrip
            k.Box(-jOut, -jIn, floor + 1.25f, headTop, zJ0, zJ1, primed, Vector3.up);
            k.Box(jIn, jOut, floor + 1.25f, headTop, zJ0, zJ1, primed, Vector3.up);
            k.Box(-jOut, jOut, headIn, headTop, zJ0, zJ1, primed, Vector3.right);
            float stopW = 0.5f;
            k.Box(-jIn, -jIn + stopW, capTop, headIn, slabE, zJ1, primed, Vector3.up);
            k.Box(jIn - stopW, jIn, capTop, headIn, slabE, zJ1, primed, Vector3.up);
            k.Box(-jIn + stopW, jIn - stopW, headIn - stopW, headIn, slabE, zJ1, primed, Vector3.right);
            float ws = 0.32f;
            k.Box(-jIn, -jIn + stopW + 0.05f, capTop, headIn, slabE - ws, slabE, dark, Vector3.up);
            k.Box(jIn - stopW - 0.05f, jIn, capTop, headIn, slabE - ws, slabE, dark, Vector3.up);
            k.Box(-jIn, jIn, headIn - stopW - 0.05f, headIn, slabE - ws, slabE, dark, Vector3.right);

            // --- slab: steel skins over a wood-framed foam core; six embossed panels
            float st = 4.75f, mull = 4.0f;
            float[] rails = { 4.5f, 4.5f, 7.5f, 8.5f };      // top, intermediate, lock, bottom
            float[] rows = { 12f, 0f, 19.5f };              // short top, tall middle (rest), bottom
            float avail = 79.5f - rails[0] - rails[1] - rails[2] - rails[3];
            rows[1] = avail - rows[0] - rows[2];
            float zc = (slabE + slabI) * 0.5f, T = 1.75f, core = T - 0.24f;
            float lx0 = -sx, lx1 = sx;
            float lockX = sx - 2.375f, knobY = floor + 36f, boltY = knobY + 5.5f, bore = 1.0625f, sq = 1.5f;

            k.Box(lx0, lx0 + st, slabBot, slabTop, slabI, slabE, steel, Vector3.up);   // hinge stile
            // lock stile, cut around the two 2-1/8" face bores
            float lsx0 = lx1 - st;
            float[] cuts = { slabBot, knobY - sq, knobY + sq, boltY - sq, boltY + sq, slabTop };
            for (int i = 0; i < cuts.Length - 1; i++)
            {
                if (i % 2 == 0) { k.Box(lsx0, lx1, cuts[i], cuts[i + 1], slabI, slabE, steel, Vector3.up); continue; }
                float by = (cuts[i] + cuts[i + 1]) * 0.5f;
                k.Box(lsx0, lockX - sq, cuts[i], cuts[i + 1], slabI, slabE, steel, Vector3.up);
                k.Box(lockX + sq, lx1, cuts[i], cuts[i + 1], slabI, slabE, steel, Vector3.up);
                Bore(k, lockX, by, slabE, slabI, sq, bore, steel, wood);
                // 1" edge bore for the latch / bolt, and the matching strike plate + mortise on the jamb
                k.B.AddDisc(V(lx1 + 0.004f, by, zc), Quaternion.Euler(0f, 0f, -90f), I(0.5f), 16, dark, true);
                k.Box(jIn - 0.04f, jIn, by - 1.2f, by + 1.2f, slabI + 0.2f, slabE - 0.15f, hw, Vector3.up);
                k.B.AddDisc(V(jIn - 0.045f, by, zc), Quaternion.Euler(0f, 0f, 90f), I(0.45f), 12, dark, true);
            }
            float y = slabTop;
            int r = 0;
            foreach (float rail in rails)
            {
                k.Box(lx0 + st, lsx0, y - rail, y, slabI, slabE, steel, Vector3.right);
                y -= rail;
                if (r < rows.Length)
                {
                    float ph = rows[r], pw = (slabW - 2 * st - mull) / 2;
                    k.Box(-mull / 2, mull / 2, y - ph, y, slabI, slabE, steel, Vector3.up);
                    for (int c = 0; c < 2; c++)
                    {
                        float px0 = c == 0 ? lx0 + st : mull / 2, px1 = px0 + pw;
                        k.Box(px0, px1, y - ph, y, zc - core / 2, zc + core / 2, steel);
                        Raised(k, px0, px1, y - ph, y, zc + core / 2, 1f, 0.14f, 1.3f, steel);
                        Raised(k, px0, px1, y - ph, y, zc - core / 2, -1f, 0.14f, 1.3f, steel);
                    }
                    y -= ph;
                }
                r++;
            }
            // door sweep (rubber fins) on the slab bottom
            k.Box(-sx + 0.2f, sx - 0.2f, slabBot - 0.2f, slabBot + 0.6f, slabE - 0.45f, slabE + 0.06f, dark, Vector3.right);

            // --- hinges: three 4" ball-bearing butts, knuckles on the interior (inswing)
            float[] hingeY = { slabTop - 9f, (slabTop - 9f + slabBot + 13f) * 0.5f, slabBot + 13f };
            foreach (float hyc in hingeY)
            {
                k.Box(-jIn, -sx, hyc - 2f, hyc + 2f, slabI, slabI + 1.0f, hw);       // leaves seen in the hinge gap
                float kx = -jIn + gap * 0.5f, kz = slabI - 0.12f;
                for (int n = 0; n < 5; n++)
                    k.B.AddCylinder(V(kx, hyc - 2f + 0.4f + n * 0.8f, kz), Quaternion.identity, I(0.26f), I(0.74f), 12, hw);
                k.B.AddCylinder(V(kx, hyc + 2.05f, kz), Quaternion.identity, I(0.2f), I(0.12f), 10, hw); // pin cap
            }

            // --- brickmold: 2" x 1-1/4" exterior casing, 3/16" reveal, stepped profile
            float rv = 0.1875f, bm = 2f;
            float cIn = jIn + rv, cOut = cIn + bm, cTop = headIn + rv + bm, cBot = floor + 0.35f;
            for (int side = -1; side <= 1; side += 2)
            {
                float a = side * cIn, b = side * cOut;
                k.Box(Mathf.Min(a, b), Mathf.Max(a, b), cBot, cTop, zJ1, zJ1 + 1.0f, primed, Vector3.up);
                float a2 = side * (cIn + 0.15f), b2 = side * (cIn + 0.85f);
                k.Box(Mathf.Min(a2, b2), Mathf.Max(a2, b2), cBot, cTop - 1.15f, zJ1 + 1.0f, zJ1 + 1.25f, primed, Vector3.up);
            }
            k.Box(-cOut, cOut, headIn + rv, cTop, zJ1, zJ1 + 1.0f, primed, Vector3.right);
            k.Box(-cIn - 0.85f, cIn + 0.85f, headIn + rv + 0.15f, headIn + rv + 0.85f, zJ1 + 1.0f, zJ1 + 1.25f, primed, Vector3.right);
        }

        /// <summary>Face bore through the slab: square plates with a round hole on both faces and an inward-facing tube
        /// (the wood lock block shows inside).</summary>
        static void Bore(K k, float x, float y, float zE, float zI, float half, float r, int skin, int block)
        {
            k.B.AddRectWithEllipseHole(V(x, y, zE), Quaternion.Euler(90f, 0f, 0f), I(half), I(half), Vector2.zero, I(r), I(r), 6, skin);
            k.B.AddRectWithEllipseHole(V(x, y, zI), Quaternion.Euler(-90f, 0f, 0f), I(half), I(half), Vector2.zero, I(r), I(r), 6, skin);
            float h = (zE - zI) * 0.5f;
            k.B.AddLathe(V(x, y, (zE + zI) * 0.5f), Quaternion.Euler(90f, 0f, 0f), new[] { new Vector2(I(r), I(h)), new Vector2(I(r), -I(h)) }, 24, block);
        }

        // ================================================================== sliding patio door

        static void PatioSlider(K k, float W, float H, float D)
        {
            float hy = H / 2, hz = D / 2, floor = -hy;
            float ux = (W - 0.5f) / 2, top = floor + 79.5f;        // 6'0" unit = 71-1/2" x 79-1/2"
            int vinyl = k.M(SurfaceKind.PaintedWhite, VinylWhite), glass = k.M(SurfaceKind.Glass), spacer = k.M(SurfaceKind.BrushedSteel);
            int dark = k.M(SurfaceKind.DarkPlastic), hw = k.M(SurfaceKind.Nickel), alum = k.M(SurfaceKind.PaintedWhite, new Color(0.92f, 0.92f, 0.9f));
            int sill = k.M(SurfaceKind.BrushedSteel, new Color(0.85f, 0.84f, 0.8f));
            float fw = 1.75f;
            k.Box(-ux, ux, top - fw, top, -hz, hz, vinyl, Vector3.right);
            k.Box(-ux, -ux + fw, floor, top - fw, -hz, hz, vinyl, Vector3.up);
            k.Box(ux - fw, ux, floor, top - fw, -hz, hz, vinyl, Vector3.up);
            float sTop = floor + 1.25f;
            k.ExtrudeX(new[]
            {
                new Vector2(-hz, floor), new Vector2(hz, floor), new Vector2(hz, floor + 0.55f),
                new Vector2(hz - 1.1f, sTop), new Vector2(-hz, sTop)
            }, -ux + fw - 0.01f, ux - fw + 0.01f, sill);
            float ox0 = -ux + fw, ox1 = ux - fw, oy1 = top - fw;
            float zI = -0.9f, zE = 0.9f, pd = 1.5f;
            // sill track rails (rollers ride these) and the head guide
            k.Box(ox0, ox1, sTop, sTop + 0.45f, zI - 0.12f, zI + 0.12f, sill, Vector3.right);
            k.Box(ox0, ox1, sTop, sTop + 0.45f, zE - 0.12f, zE + 0.12f, sill, Vector3.right);
            k.Box(ox0, ox1, sTop, sTop + 0.3f, hz - 0.45f, hz - 0.3f, sill, Vector3.right); // screen track
            k.Box(ox0, ox1, oy1 - 0.6f, oy1, -0.1f, 0.1f, vinyl, Vector3.right);
            k.Box(ox0, ox1, oy1 - 0.6f, oy1, -hz, -hz + 0.25f, vinyl, Vector3.right);

            float mid = 0f, py0 = sTop + 0.55f, py1 = oy1 + 0.5f;
            // fixed panel: exterior track, left; operable panel: interior track, right (slides behind the fixed one)
            Sash(k, ox0 - 0.5f, mid + 1.0f, py0, py1, zE, pd, 2.5f, 2.0f, 2.75f, 3.75f, vinyl, glass, spacer);
            Sash(k, mid - 1.0f, ox1 + 0.5f, py0, py1, zI, pd, 2.0f, 2.5f, 2.75f, 3.75f, vinyl, glass, spacer);
            // interlocking meeting stiles
            k.Box(mid + 0.6f, mid + 1.0f, py0 + 3.75f, py1 - 2.75f, zE - pd / 2 - 0.55f, zE - pd / 2, vinyl, Vector3.up);
            k.Box(mid - 1.0f, mid - 0.6f, py0 + 3.75f, py1 - 2.75f, zI + pd / 2, zI + pd / 2 + 0.55f, vinyl, Vector3.up);
            // operable panel: interior handle with thumb-turn lock, exterior recessed pull, keeper on the jamb
            float hxp = ox1 - 1.2f, hyp = floor + 38f, zf = zI - pd / 2;
            k.Box(hxp - 0.55f, hxp + 0.55f, hyp - 4.5f, hyp + 4.5f, zf - 0.35f, zf, hw);
            k.Box(hxp - 0.4f, hxp + 0.4f, hyp - 3.5f, hyp + 2.5f, zf - 1.3f, zf - 0.35f, hw);
            k.Box(hxp - 0.15f, hxp + 0.15f, hyp + 3f, hyp + 3.9f, zf - 0.75f, zf - 0.35f, hw);
            k.Box(hxp - 0.5f, hxp + 0.5f, hyp - 2.5f, hyp + 2.5f, zI + pd / 2, zI + pd / 2 + 0.25f, dark);
            k.Box(ox1 - 0.25f, ox1, hyp - 1.5f, hyp + 1.5f, zI - 0.4f, zI + 0.4f, hw);
            // sliding screen in the exterior track over the operable half (rollers + handle)
            float scz0 = hz - 0.4f, scz1 = hz - 0.08f;
            Screen(k, mid - 1.0f, ox1 + 0.1f, sTop + 0.3f, oy1 - 0.2f, scz0, scz1, alum, false);
            k.Box(ox1 - 2.4f, ox1 - 1.4f, hyp - 3f, hyp + 3f, scz1, scz1 + 0.6f, alum);
            NailingFin(k, -ux, ux, floor + 0.6f, top, JambExtIn, vinyl, dark);
        }

        // ================================================================== sectional garage door

        static void Sectional(K k, float W, float H, float D)
        {
            float hx = W / 2, hy = H / 2, hz = D / 2;
            int skin = k.M(SurfaceKind.PaintedSteel, new Color(0.97f, 0.97f, 0.96f));
            int back = k.M(SurfaceKind.PaintedWhite);
            int galv = k.M(SurfaceKind.Galvanized), dark = k.M(SurfaceKind.DarkPlastic), blk = k.M(SurfaceKind.MatteBlack);
            int nylon = k.M(SurfaceKind.Porcelain);

            // bottom astragal and four 2" insulated sections (front skin embossed with short raised panels)
            float seal = 0.35f;
            k.Box(-hx, hx, -hy, -hy + seal, -hz + 0.2f, hz - 0.35f, dark, Vector3.right);
            int sections = 4;
            float secH = (H - seal) / sections, g = 0.06f;
            int n = Mathf.Max(2, Mathf.RoundToInt(W / 24f));
            float m = 3f, gap = 3f, rail = 3.5f;
            float pw = (W - 2 * m - (n - 1) * gap) / n;
            float zFace = hz - 0.3f;
            for (int sct = 0; sct < sections; sct++)
            {
                float sb = -hy + seal + sct * secH + g, stp = -hy + seal + (sct + 1) * secH - g;
                k.Box(-hx, hx, sb, stp, -hz, zFace, back, Vector3.right);
                k.Box(-hx, hx, stp - rail, stp, zFace, hz, skin, Vector3.right);
                k.Box(-hx, hx, sb, sb + rail, zFace, hz, skin, Vector3.right);
                // tongue/groove joint: a rounded-over lip reads as the section seam
                k.Box(-hx, hx, stp - 0.18f, stp, hz - 0.12f, hz + 0.02f, skin, Vector3.right);
                for (int i = 0; i <= n; i++)
                {
                    float x0 = i == 0 ? -hx : -hx + m + i * pw + (i - 1) * gap;
                    float x1 = i == 0 ? -hx + m : (i == n ? hx : x0 + gap);
                    k.Box(x0, x1, sb + rail, stp - rail, zFace, hz, skin, Vector3.up);
                }
                for (int i = 0; i < n; i++)
                {
                    float x0 = -hx + m + i * (pw + gap);
                    Raised(k, x0, x0 + pw, sb + rail, stp - rail, zFace, 1f, 0.24f, 1.1f, skin);
                }
                if (sct > 0) k.Box(-hx, hx, sb - g - 0.1f, sb - g + 0.1f, -hz + 0.25f, hz - 0.5f, dark, Vector3.right); // joint seal
                // struts: top section always; every section on doors wider than 12'
                if (sct == sections - 1 || W > 144f)
                {
                    float sy = stp - 4.5f;
                    k.Box(-hx + 3f, hx - 3f, sy, sy + 3f, -hz - 0.08f, -hz, galv, Vector3.right);
                    k.Box(-hx + 3f, hx - 3f, sy + 2.92f, sy + 3f, -hz - 2.2f, -hz, galv, Vector3.right);
                    k.Box(-hx + 3f, hx - 3f, sy, sy + 0.08f, -hz - 2.2f, -hz, galv, Vector3.right);
                }
            }

            // end stiles and center stiles (galvanized) on the back face
            int hingeCols = W > 144f ? 4 : 2;
            for (int side = -1; side <= 1; side += 2)
                k.Box(side * (hx - 3f), side * hx, -hy + seal, hy, -hz - 0.06f, -hz, galv, Vector3.up);
            var colX = new List<float>();
            for (int j = 1; j < hingeCols; j++) colX.Add(-hx + W * j / hingeCols);
            foreach (float x in colX) k.Box(x - 1.5f, x + 1.5f, -hy + seal, hy, -hz - 0.06f, -hz, galv, Vector3.up);

            // rollers on every joint + top and bottom, graduated end hinges, center hinges
            float zr = -hz - 1.5f;
            var rollY = new List<float> { -hy + seal + 3f };
            for (int sct = 1; sct < sections; sct++) rollY.Add(-hy + seal + sct * secH);
            rollY.Add(hy - 3f);
            for (int side = -1; side <= 1; side += 2)
                foreach (float ry in rollY)
                {
                    float xa = side * (hx - 3f), xb = side * (hx - 0.4f);
                    k.Box(Mathf.Min(xa, xb), Mathf.Max(xa, xb), ry - 2.4f, ry + 2.4f, -hz - 0.14f, -hz - 0.06f, galv);
                    float xt0 = side * (hx - 2.6f), xt1 = side * (hx - 0.7f);
                    k.Box(Mathf.Min(xt0, xt1), Mathf.Max(xt0, xt1), ry - 0.45f, ry + 0.45f, zr - 0.4f, -hz - 0.14f, galv);
                    k.Rod(new Vector3(side * (hx - 2.4f), ry, zr), new Vector3(side * (hx + 1.0f), ry, zr), 0.2f, 8, galv);
                    k.B.AddCylinder(V(side * (hx + 1.1f), ry, zr), Quaternion.Euler(0f, 0f, 90f), I(0.98f), I(0.7f), 18, nylon);
                    k.B.AddCylinder(V(side * (hx + 1.1f), ry, zr), Quaternion.Euler(0f, 0f, 90f), I(0.35f), I(0.78f), 10, galv);
                }
            foreach (float x in colX)
                for (int sct = 1; sct < sections; sct++)
                {
                    float jy = -hy + seal + sct * secH;
                    k.Box(x - 1.3f, x + 1.3f, jy - 2.2f, jy + 2.2f, -hz - 0.12f, -hz - 0.06f, galv);
                    k.Rod(new Vector3(x - 1.3f, jy, -hz - 0.35f), new Vector3(x + 1.3f, jy, -hz - 0.35f), 0.24f, 8, galv);
                }
            // bottom brackets with the lift-cable loops
            for (int side = -1; side <= 1; side += 2)
            {
                float xa = side * (hx - 3.5f), xb = side * hx;
                k.Box(Mathf.Min(xa, xb), Mathf.Max(xa, xb), -hy + seal, -hy + seal + 6f, -hz - 0.2f, -hz - 0.06f, galv);
            }

            // tracks: 2" vertical, 12" radius, horizontal run back to the rear hangers
            float R = 12f, yv = hy - 3f, yh = yv + R, L = H + 18f - R;
            for (int side = -1; side <= 1; side += 2)
            {
                Track(k, side, hx, new Vector2(-hy + 0.5f, zr), new Vector2(yv, zr), galv);
                const int segs = 8;
                for (int i = 0; i < segs; i++)
                {
                    float a0 = 90f * i / segs * Mathf.Deg2Rad, a1 = 90f * (i + 1) / segs * Mathf.Deg2Rad;
                    var p0 = new Vector2(yv + R * Mathf.Sin(a0), zr - R + R * Mathf.Cos(a0));
                    var p1 = new Vector2(yv + R * Mathf.Sin(a1), zr - R + R * Mathf.Cos(a1));
                    Track(k, side, hx, p0, p1, galv);
                }
                Track(k, side, hx, new Vector2(yh, zr - R), new Vector2(yh, zr - R - L), galv);
                // flag bracket + jamb brackets tying the vertical track to the jamb, rear hanger to the ceiling joists
                float xo = side * (hx + 1.72f), xw = side * (hx + 3.2f);
                k.Box(Mathf.Min(xo, xw), Mathf.Max(xo, xw), yv - 10f, yv + 2f, zr - 1.2f, hz, galv);
                for (int j = 0; j < 3; j++)
                {
                    float by = -hy + 12f + j * 22f;
                    k.Box(Mathf.Min(xo, xw), Mathf.Max(xo, xw), by - 1f, by + 1f, zr - 0.8f, hz, galv);
                }
                float zEnd = zr - R - L + 2f;
                k.Box(side * (hx + 1.5f) - 0.6f, side * (hx + 1.5f) + 0.6f, yh + 1f, yh + 36f, zEnd - 0.08f, zEnd + 1.2f, galv);
            }

            // torsion spring assembly above the opening: 1" shaft, cable drums, bearing plates, oil-tempered springs
            float shY = hy + 13f, shZ = -hz - 4.5f;
            k.Rod(new Vector3(-hx - 3.5f, shY, shZ), new Vector3(hx + 3.5f, shY, shZ), 0.5f, 12, galv);
            for (int side = -1; side <= 1; side += 2)
            {
                float dx = side * (hx + 1.0f);
                k.B.AddCylinder(V(dx, shY, shZ), Quaternion.Euler(0f, 0f, 90f), I(2f), I(1.4f), 20, galv);
                k.B.AddCylinder(V(dx + side * 0.9f, shY, shZ), Quaternion.Euler(0f, 0f, 90f), I(2.4f), I(0.12f), 20, galv);
                float px = side * (hx + 3.0f);
                k.Box(px - 0.06f, px + 0.06f, shY - 4f, shY + 4f, shZ - 2f, hz, galv);
                // lift cable from the drum down past the rollers to the bottom bracket
                k.Rod(new Vector3(side * (hx + 0.3f), shY, shZ + 2f), new Vector3(side * (hx - 0.6f), -hy + seal + 3f, -hz - 0.4f), 0.06f, 6, galv);
            }
            k.Box(-0.06f, 0.06f, shY - 4f, shY + 4f, shZ - 2f, hz, galv); // center bearing plate
            int springs = W > 144f ? 2 : 1;
            for (int sp = 0; sp < springs; sp++)
            {
                float dir = sp == 0 ? -1f : 1f, x0 = dir * 2f, x1 = dir * 26f;
                for (float x = Mathf.Min(x0, x1); x <= Mathf.Max(x0, x1); x += 0.25f)
                    k.B.AddRing(V(x, shY, shZ), Quaternion.Euler(0f, 0f, 90f), I(1.125f), I(0.875f), I(0.22f), 12, blk);
                k.B.AddFrustum(V(x1 + dir * 0.9f, shY, shZ), Quaternion.Euler(0f, 0f, dir > 0 ? -90f : 90f), I(1.0f), I(1.4f), I(1.8f), 12, blk); // winding cone
                k.B.AddFrustum(V(x0 - dir * 0.6f, shY, shZ), Quaternion.Euler(0f, 0f, dir > 0 ? 90f : -90f), I(1.0f), I(1.3f), I(1.2f), 12, blk); // stationary cone
            }
        }

        /// <summary>One straight run of 2" garage track between two (y, z) points: web on the outside, two flanges around the rollers.</summary>
        static void Track(K k, int side, float hx, Vector2 p0, Vector2 p1, int sub)
        {
            Vector2 d = p1 - p0;
            float len = d.magnitude + 0.04f;
            if (len < 0.05f) return;
            Vector2 t = d.normalized, nn = new Vector2(-t.y, t.x);
            var T3 = new Vector3(0f, t.x, t.y);
            var N3 = new Vector3(0f, nn.x, nn.y);
            var rot = Quaternion.LookRotation(T3, N3);
            Vector2 mid = (p0 + p1) * 0.5f;
            k.B.AddBox(V(side * (hx + 1.66f), mid.x, mid.y), new Vector3(I(0.08f), I(2.2f), I(len)), rot, sub, Vector3.forward);
            for (int f = -1; f <= 1; f += 2)
            {
                Vector2 c = mid + nn * (1.08f * f);
                k.B.AddBox(V(side * (hx + 1.12f), c.x, c.y), new Vector3(I(1.1f), I(0.08f), I(len)), rot, sub, Vector3.forward);
            }
        }
    }
}
