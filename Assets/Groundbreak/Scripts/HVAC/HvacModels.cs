using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Small mesh kit for HVAC part models: one MeshBuilder, one submesh per material, local space (meters).</summary>
    public sealed class HKit
    {
        public readonly MeshBuilder B = new MeshBuilder();
        readonly List<Material> _mats = new List<Material>();
        public Material[] Mats => _mats.ToArray();

        public int Mat(Material m) { int i = _mats.IndexOf(m); if (i < 0) { _mats.Add(m); i = _mats.Count - 1; } return i; }
        public int M(SurfaceKind k) => Mat(Materials.Get(k));
        public int M(SurfaceKind k, Color t) => Mat(Materials.Get(k, t));

        public void Box(float x0, float x1, float y0, float y1, float z0, float z1, int sub, int mask = MeshBuilder.AllFaces, bool inward = false)
            => B.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                        new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), Quaternion.identity, sub, Vector3.zero, mask, inward);

        public void BoxR(Vector3 c, Vector3 size, Quaternion r, int sub) => B.AddBox(c, size, r, sub);

        public void Cyl(Vector3 a, Vector3 b, float r, int sub, int seg = 24, bool cap = true)
        {
            Vector3 d = b - a; if (d.sqrMagnitude < 1e-10f) return;
            B.AddCylinder((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized), r, d.magnitude, seg, sub, cap);
        }

        /// <summary>Open thin-wall tube, visible from outside and inside.</summary>
        public void Tube(Vector3 a, Vector3 b, float r, int sub, int seg = 28)
        {
            Vector3 d = b - a; if (d.sqrMagnitude < 1e-10f) return;
            var rot = Quaternion.FromToRotation(Vector3.up, d.normalized);
            float h = d.magnitude * 0.5f; Vector3 c = (a + b) * 0.5f;
            B.AddLathe(c, rot, new[] { new Vector2(r, -h), new Vector2(r, h) }, seg, sub);
            B.AddLathe(c, rot, new[] { new Vector2(r * 0.985f, h), new Vector2(r * 0.985f, -h) }, seg, sub);
        }

        public void Ring(Vector3 c, Vector3 axis, float rOut, float rIn, float h, int sub, int seg = 28)
            => B.AddRing(c, Quaternion.FromToRotation(Vector3.up, axis.normalized), rOut, rIn, h, seg, sub);

        /// <summary>Thin strip between two points lying on a face with normal n.</summary>
        public void Strip(Vector3 a, Vector3 b, Vector3 n, float width, float thick, int sub)
        {
            Vector3 d = b - a; if (d.sqrMagnitude < 1e-10f) return;
            B.AddBox((a + b) * 0.5f, new Vector3(width, thick, d.magnitude), Quaternion.LookRotation(d.normalized, n), sub);
        }

        /// <summary>Quad strip between two closed rings (same count), double-sided.</summary>
        public void Loft(IList<Vector3> ra, IList<Vector3> rb, Vector3 axisCenter, int sub)
        {
            int n = ra.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 mid = (ra[i] + ra[j] + rb[i] + rb[j]) * 0.25f;
                Vector3 outN = mid - axisCenter; outN.y = 0f;
                if (outN.sqrMagnitude < 1e-10f) outN = Vector3.forward;
                B.AddQuad(ra[i], ra[j], rb[j], rb[i], sub, outN);
                B.AddQuad(ra[i], ra[j], rb[j], rb[i], sub, -outN);
            }
        }

        public Mesh Build(string name) { if (_mats.Count == 0) Mat(HvacLook.Galv); B.MinSubmeshCount = _mats.Count; return B.Build(name); }
    }

    /// <summary>Real-size procedural models for HVAC parts. Origin = center of the piece Size box, Y up, front +Z
    /// (boots, registers and grilles: the face/opening is at -Y so they read correctly in a ceiling).</summary>
    public static class HvacModels
    {
        static readonly Dictionary<string, (Mesh mesh, Material[] mats)> _cache = new Dictionary<string, (Mesh, Material[])>();
        static float I(float inches) => inches * Units.Inch;

        /// <summary>Builds (cached by key) and spawns a child "Visual" under parent with local identity.</summary>
        public static GameObject Spawn(string key, Transform parent, Action<HKit> build, string name = "Visual")
        {
            if (!_cache.TryGetValue(key, out var e) || e.mesh == null || Array.Exists(e.mats, m => m == null))
            {
                var k = new HKit();
                build(k);
                e = (k.Build(key), k.Mats);
                _cache[key] = e;
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = e.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = e.mats;
            return go;
        }

        /// <summary>Spawns the model for a part piece and swaps it in as the piece's visual.</summary>
        public static void Apply(Piece p, Action<HKit, Vector3> build)
        {
            var s = p.Size;
            string key = p.Def.Id + "|" + Mathf.RoundToInt(s.x * 1000) + "," + Mathf.RoundToInt(s.y * 1000) + "," + Mathf.RoundToInt(s.z * 1000);
            var go = Spawn(key, p.transform, k => build(k, s));
            MepAutoAttach.ReplaceVisual(p, go);
        }

        static int Galv(HKit k) => k.Mat(HvacLook.Galv);

        // ------------------------------------------------------------------ plenums / coil

        /// <summary>Supply plenum: galvanized box with Pittsburgh corner seams, cross-breaks, bottom flange and a 12x8 trunk collar on +X.</summary>
        public static void SupplyPlenum(HKit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2; int g = Galv(k);
            k.Box(-hx, hx, -hy, hy, -hz, hz, g, MeshBuilder.AllFaces & ~MeshBuilder.FaceNegY);
            CornerSeams(k, hx, hy, hz, g);
            CrossBreaks(k, hx, hy, hz, g, true);
            // bottom S-cleat flange
            float f = I(0.75f), t = I(0.05f);
            k.Box(-hx - f, hx + f, -hy, -hy + t, -hz - f, -hz, g); k.Box(-hx - f, hx + f, -hy, -hy + t, hz, hz + f, g);
            k.Box(-hx - f, -hx, -hy, -hy + t, -hz, hz, g); k.Box(hx, hx + f, -hy, -hy + t, -hz, hz, g);
            // trunk collar (drive cleats) on +X
            float py = hy - I(5.375f);
            RectCollarX(k, hx, py, I(12f), I(8f), I(1f), g);
        }

        public static Vector3 SupplyPlenumTrunkPort(Vector3 s) => new Vector3(s.x / 2 + I(1f), s.y / 2 - I(5.375f), 0f);

        /// <summary>Return drop / filter box beside the furnace: filter door on the front, 16x8 collar on top.</summary>
        public static void ReturnPlenum(HKit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2; int g = Galv(k);
            int door = k.M(SurfaceKind.PaintedSteel, new Color(0.72f, 0.74f, 0.75f)), white = k.M(SurfaceKind.PaintedWhite), dark = k.M(SurfaceKind.DarkPlastic);
            k.Box(-hx, hx, -hy, hy, -hz, hz, g);
            CornerSeams(k, hx, hy, hz, g);
            // filter rack door on the front with a pull and a filter label
            k.Box(-hx + I(1f), hx - I(1f), -hy + I(2f), hy - I(2f), hz, hz + I(0.08f), door);
            k.Box(-I(1.2f), I(1.2f), hy - I(4f), hy - I(3.5f), hz + I(0.08f), hz + I(0.4f), dark);
            k.Box(-hx + I(2f), hx - I(2f), -hy + I(4f), -hy + I(6f), hz + I(0.08f), hz + I(0.1f), white);
            // flange where it bolts to the furnace (+X)
            float f = I(0.75f), t = I(0.05f);
            k.Box(hx, hx + t, -hy, -hy + f, -hz, hz, g); k.Box(hx, hx + t, hy - f, hy, -hz, hz, g);
            // 16 (x) x 8 (z) collar on top
            float cw = I(16f) / 2, cd = I(8f) / 2, ch = I(1f), w = I(0.05f);
            k.Box(-cw - w, cw + w, hy, hy + ch, cd, cd + w, g); k.Box(-cw - w, cw + w, hy, hy + ch, -cd - w, -cd, g);
            k.Box(cw, cw + w, hy, hy + ch, -cd, cd, g); k.Box(-cw - w, -cw, hy, hy + ch, -cd, cd, g);
        }

        public static void Coil(HKit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int body = k.M(SurfaceKind.PaintedSteel), g = Galv(k), white = k.M(SurfaceKind.PaintedWhite), dark = k.M(SurfaceKind.DarkPlastic);
            int cu = k.M(SurfaceKind.Brass, MepLook.Copper), pvc = k.M(SurfaceKind.PaintedWhite, MepLook.PvcWhite);
            float fz = hz - I(0.4f);
            k.Box(-hx, hx, -hy, hy, -hz, fz, body);
            k.Box(-hx + I(0.6f), hx - I(0.6f), -hy + I(0.6f), hy - I(0.6f), fz, hz - I(0.05f), body); // access panel
            k.Box(-I(2.5f), I(2.5f), hy - I(6f), hy - I(3.5f), hz - I(0.05f), hz, white);                  // rating plate
            foreach (var (x, y) in new[] { (-hx + I(1.2f), -hy + I(1.2f)), (hx - I(1.2f), -hy + I(1.2f)), (-hx + I(1.2f), hy - I(1.2f)), (hx - I(1.2f), hy - I(1.2f)) })
                k.Cyl(new Vector3(x, y, hz - I(0.05f)), new Vector3(x, y, hz + I(0.06f)), I(0.18f), g, 8);
            // top / bottom flanges
            k.Box(-hx + I(1f), hx - I(1f), hy, hy + I(0.6f), -hz + I(1f), hz - I(1.4f), g, MeshBuilder.AllFaces & ~MeshBuilder.FacePosY);
            // refrigerant stubs with rubber grommets, condensate primary + plugged secondary
            Vector3 sp = CoilSuction(s) - new Vector3(0, 0, I(1.5f)), lp = CoilLiquid(s) - new Vector3(0, 0, I(1.5f)), cp = CoilDrain(s) - new Vector3(0, 0, I(1f));
            k.Ring(new Vector3(sp.x, sp.y, hz + I(0.1f)), Vector3.forward, I(0.85f), I(0.35f), I(0.2f), dark, 16);
            k.Ring(new Vector3(lp.x, lp.y, hz + I(0.1f)), Vector3.forward, I(0.6f), I(0.18f), I(0.2f), dark, 16);
            k.Cyl(sp, sp + new Vector3(0, 0, I(1.5f)), I(0.375f), cu, 14);
            k.Cyl(lp, lp + new Vector3(0, 0, I(1.5f)), I(0.1875f), cu, 12);
            k.Cyl(cp, cp + new Vector3(0, 0, I(1f)), I(0.525f), pvc, 14);
            Vector3 c2 = cp + new Vector3(-I(3f), 0, 0);
            k.Cyl(c2, c2 + new Vector3(0, 0, I(0.8f)), I(0.6f), pvc, 14);                       // plugged secondary drain
            k.Box(-hx + I(1f), -hx + I(6f), -hy + I(14f), -hy + I(15.5f), hz - I(0.05f), hz, white); // "liquid / suction" tag
        }

        public static Vector3 CoilSuction(Vector3 s) => new Vector3(-s.x / 2 + I(3.5f), -s.y / 2 + I(9f), s.z / 2 + I(1.5f));
        public static Vector3 CoilLiquid(Vector3 s) => new Vector3(-s.x / 2 + I(3.5f), -s.y / 2 + I(6f), s.z / 2 + I(1.5f));
        public static Vector3 CoilDrain(Vector3 s) => new Vector3(s.x / 2 - I(3f), -s.y / 2 + I(2.5f), s.z / 2 + I(1f));

        static void CornerSeams(HKit k, float hx, float hy, float hz, int g)
        {
            float w = I(0.4f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float x = sx * hx, z = sz * hz;
                    k.Box(Mathf.Min(x, x + sx * w), Mathf.Max(x, x + sx * w), -hy, hy, z - sz * I(0.05f) - I(0.025f), z - sz * I(0.05f) + I(0.025f), g);
                }
        }

        static void CrossBreaks(HKit k, float hx, float hy, float hz, int g, bool top)
        {
            float w = I(0.3f), t = I(0.03f), e = I(0.6f);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                float x = sx * (hx + t * 0.5f);
                k.Strip(new Vector3(x, -hy + e, -hz + e), new Vector3(x, hy - e, hz - e), new Vector3(sx, 0, 0), w, t, g);
                k.Strip(new Vector3(x, -hy + e, hz - e), new Vector3(x, hy - e, -hz + e), new Vector3(sx, 0, 0), w, t, g);
            }
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float z = sz * (hz + t * 0.5f);
                k.Strip(new Vector3(-hx + e, -hy + e, z), new Vector3(hx - e, hy - e, z), new Vector3(0, 0, sz), w, t, g);
                k.Strip(new Vector3(hx - e, -hy + e, z), new Vector3(-hx + e, hy - e, z), new Vector3(0, 0, sz), w, t, g);
            }
            if (top)
            {
                float y = hy + t * 0.5f;
                k.Strip(new Vector3(-hx + e, y, -hz + e), new Vector3(hx - e, y, hz - e), Vector3.up, w, t, g);
                k.Strip(new Vector3(hx - e, y, -hz + e), new Vector3(-hx + e, y, hz - e), Vector3.up, w, t, g);
            }
        }

        /// <summary>Rectangular collar frame on a +X face (w along Z, h along Y), projecting d.</summary>
        static void RectCollarX(HKit k, float x, float y, float w, float h, float d, int g)
        {
            float t = I(0.05f), hw = w / 2, hh = h / 2;
            k.Box(x, x + d, y + hh, y + hh + t, -hw - t, hw + t, g); k.Box(x, x + d, y - hh - t, y - hh, -hw - t, hw + t, g);
            k.Box(x, x + d, y - hh, y + hh, hw, hw + t, g); k.Box(x, x + d, y - hh, y + hh, -hw - t, -hw, g);
            // drive cleat flange on the plenum wall
            float f = I(0.8f);
            k.Box(x, x + t, y + hh, y + hh + f, -hw - f, hw + f, g); k.Box(x, x + t, y - hh - f, y - hh, -hw - f, hw + f, g);
            k.Box(x, x + t, y - hh, y + hh, hw, hw + f, g); k.Box(x, x + t, y - hh, y + hh, -hw - f, -hw, g);
        }

        // ------------------------------------------------------------------ takeoff / boots / registers / grilles

        /// <summary>Spin-in starting collar with damper: flange on the duct face at y = off (local y = collar axis).</summary>
        public static void Takeoff(HKit k, float off, float r)
        {
            int g = Galv(k), dark = k.M(SurfaceKind.MatteBlack);
            float fh = r + I(1f), t = I(0.04f), len = I(2.5f);
            k.Box(-fh, fh, off, off + t, -fh, fh, g);
            k.Ring(new Vector3(0, off + t + I(0.15f), 0), Vector3.up, r + I(0.35f), r, I(0.3f), g);  // spin-in bead
            k.Tube(new Vector3(0, off, 0), new Vector3(0, off + len, 0), r, g);
            k.Ring(new Vector3(0, off + I(1.7f), 0), Vector3.up, r + I(0.08f), r - I(0.01f), I(0.18f), g); // crimp bead
            // damper shaft + wing handle
            float dy = off + I(1.1f);
            k.Cyl(new Vector3(-r - I(0.5f), dy, 0), new Vector3(r + I(0.5f), dy, 0), I(0.09f), g, 8);
            k.Box(r + I(0.3f), r + I(0.45f), dy - I(0.25f), dy + I(0.25f), -I(0.9f), I(0.9f), dark);
            // dampener blade visible inside
            k.Box(-r * 0.95f, r * 0.95f, dy - I(0.02f), dy + I(0.02f), -r * 0.95f, r * 0.95f, g);
        }

        /// <summary>Straight register boot: wIn x 4 opening at -Y with nailing flange, transition up to a 6" round collar.</summary>
        public static void Boot(HKit k, Vector3 s, float wIn, float collarIn)
        {
            float hy = s.y / 2; int g = Galv(k);
            float ow = I(wIn) / 2, od = I(2f), r = I(collarIn) / 2;
            float y0 = -hy, y1 = y0 + I(3f), y2 = y0 + I(5.5f), y3 = hy;
            int sides = MeshBuilder.FacePosX | MeshBuilder.FaceNegX | MeshBuilder.FacePosZ | MeshBuilder.FaceNegZ;
            k.Box(-ow, ow, y0, y1, -od, od, g, sides);
            k.Box(-ow, ow, y0, y1, -od, od, g, sides, true);
            const int N = 36;
            var ra = new List<Vector3>(N); var rb = new List<Vector3>(N);
            for (int i = 0; i < N; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / N, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                float t = Mathf.Min(Mathf.Abs(c) > 1e-4f ? ow / Mathf.Abs(c) : 1e9f, Mathf.Abs(sn) > 1e-4f ? od / Mathf.Abs(sn) : 1e9f);
                ra.Add(new Vector3(c * t, y1, sn * t));
                rb.Add(new Vector3(c * r, y2, sn * r));
            }
            k.Loft(ra, rb, new Vector3(0, (y1 + y2) * 0.5f, 0), g);
            k.Tube(new Vector3(0, y2, 0), new Vector3(0, y3, 0), r, g);
            k.Ring(new Vector3(0, y3 - I(0.8f), 0), Vector3.up, r + I(0.08f), r - I(0.01f), I(0.18f), g);
            // nailing flange around the opening (in the ceiling plane)
            float f = I(1f), t2 = I(0.04f);
            k.Box(-ow - f, ow + f, y0, y0 + t2, od, od + f, g); k.Box(-ow - f, ow + f, y0, y0 + t2, -od - f, -od, g);
            k.Box(ow, ow + f, y0, y0 + t2, -od, od, g); k.Box(-ow - f, -ow, y0, y0 + t2, -od, od, g);
            // seam on the long side
            k.Box(-ow - I(0.15f), -ow, y0, y1, -I(0.2f), I(0.2f), g);
        }

        /// <summary>Stamped steel register, white: face plate at -Y with louvers, sleeve up into the boot, damper lever.</summary>
        public static void Register(HKit k, Vector3 s, float wIn)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int w = k.M(SurfaceKind.PaintedWhite), dark = k.M(SurfaceKind.DarkPlastic, new Color(0.35f, 0.35f, 0.36f));
            float ow = I(wIn) / 2, od = I(2f), y0 = -hy, t = I(0.06f), yp = y0 + t;
            k.Box(-hx, hx, y0, yp, od, hz, w); k.Box(-hx, hx, y0, yp, -hz, -od, w);
            k.Box(ow, hx, y0, yp, -od, od, w); k.Box(-hx, -ow, y0, yp, -od, od, w);
            // rolled outer edge
            float e = I(0.15f);
            k.Box(-hx, hx, y0, yp + e, hz - t, hz, w); k.Box(-hx, hx, y0, yp + e, -hz, -hz + t, w);
            k.Box(hx - t, hx, y0, yp + e, -hz, hz, w); k.Box(-hx, -hx + t, y0, yp + e, -hz, hz, w);
            // louvers (blades along the long axis), angled to throw air
            int n = 7;
            for (int i = 0; i < n; i++)
            {
                float z = -od + (i + 0.5f) * (2 * od / n);
                k.BoxR(new Vector3(0, y0 + I(0.3f), z), new Vector3(2 * ow, I(0.5f), I(0.04f)), Quaternion.Euler(30f, 0, 0), w);
            }
            // end stiffeners
            k.Box(-ow, -ow + I(0.2f), y0, y0 + I(0.55f), -od, od, w); k.Box(ow - I(0.2f), ow, y0, y0 + I(0.55f), -od, od, w);
            // sleeve that slips into the boot
            float sw = ow - I(0.08f), sd = od - I(0.08f), ts = I(0.03f);
            k.Box(-sw, sw, yp, hy, sd - ts, sd, w); k.Box(-sw, sw, yp, hy, -sd, -sd + ts, w);
            k.Box(sw - ts, sw, yp, hy, -sd, sd, w); k.Box(-sw, -sw + ts, yp, hy, -sd, sd, w);
            // damper lever
            k.Box(ow - I(1.2f), ow - I(0.5f), y0 - I(0.06f), y0, -I(0.2f), I(0.2f), dark);
        }

        /// <summary>Return filter grille + galvanized return box (can) above it with an 8x16 collar on top.</summary>
        public static void ReturnGrille(HKit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int w = k.M(SurfaceKind.PaintedWhite), g = Galv(k), filt = k.M(SurfaceKind.PaperBag, new Color(1.1f, 1.1f, 1.05f));
            float y0 = -hy, t = I(0.06f), yp = y0 + t, f = I(0.9f);
            // face frame
            k.Box(-hx - f, hx + f, y0, yp, hz, hz + f, w); k.Box(-hx - f, hx + f, y0, yp, -hz - f, -hz, w);
            k.Box(hx, hx + f, y0, yp, -hz, hz, w); k.Box(-hx - f, -hx, y0, yp, -hz, hz, w);
            // fixed 45° blades across the opening (long axis X)
            int n = Mathf.Max(6, Mathf.RoundToInt(2 * hz / I(0.6f)));
            for (int i = 0; i < n; i++)
            {
                float z = -hz + (i + 0.5f) * (2 * hz / n);
                k.BoxR(new Vector3(0, y0 + I(0.35f), z), new Vector3(2 * hx, I(0.6f), I(0.035f)), Quaternion.Euler(45f, 0, 0), w);
            }
            k.Box(-hx, hx, y0 + I(0.75f), y0 + I(1.75f), -hz, hz, filt, MeshBuilder.FaceNegY); // filter media behind the blades
            // hinge + latches
            k.Box(-hx - I(0.2f), -hx + I(0.2f), y0 - I(0.05f), y0, -hz + I(1f), hz - I(1f), w);
            k.Box(hx - I(0.6f), hx, y0 - I(0.08f), y0, -I(0.5f), I(0.5f), w);
            // box (open bottom), collar 16 (x) x 8 (z) on top
            float b0 = yp, b1 = hy - I(1.5f);
            int sides = MeshBuilder.FacePosX | MeshBuilder.FaceNegX | MeshBuilder.FacePosZ | MeshBuilder.FaceNegZ;
            k.Box(-hx, hx, b0, b1, -hz, hz, g, sides);
            k.Box(-hx, hx, b1 - I(0.04f), b1, -hz, hz, g, MeshBuilder.FacePosY);
            float cw = I(16f) / 2, cd = I(8f) / 2, tw = I(0.05f);
            k.Box(-cw - tw, cw + tw, b1, hy, cd, cd + tw, g); k.Box(-cw - tw, cw + tw, b1, hy, -cd - tw, -cd, g);
            k.Box(cw, cw + tw, b1, hy, -cd, cd, g); k.Box(-cw - tw, -cw, b1, hy, -cd, cd, g);
            CornerSeams(k, hx, (b1 - b0) / 2 - 0.0001f, hz, g);
        }

        // ------------------------------------------------------------------ caps

        /// <summary>Louvered wall cap with gravity damper flaps; tail collar goes into the wall (-Z).</summary>
        public static void WallCap(HKit k, Vector3 s, float ductIn)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, r = I(ductIn) / 2;
            int w = k.M(SurfaceKind.PaintedWhite), g = Galv(k), dark = k.M(SurfaceKind.DarkPlastic, new Color(0.3f, 0.3f, 0.3f));
            float z0 = -hz, tf = I(0.12f);
            k.Box(-hx, hx, -hy, hy, z0, z0 + tf, w);                                           // mounting flange
            float hood = s.z - tf;
            k.Box(-hx + I(0.5f), -hx + I(0.62f), -hy + I(0.6f), hy - I(0.5f), z0 + tf, z0 + tf + hood, w);
            k.Box(hx - I(0.62f), hx - I(0.5f), -hy + I(0.6f), hy - I(0.5f), z0 + tf, z0 + tf + hood, w);
            k.Box(-hx + I(0.5f), hx - I(0.5f), hy - I(0.62f), hy - I(0.5f), z0 + tf, z0 + tf + hood, w);
            k.Box(-r, r, -r, r, z0 + tf, z0 + tf + I(0.02f), dark, MeshBuilder.FacePosZ);      // duct opening behind flaps
            int n = 3; float span = 2 * hy - I(1.4f);
            for (int i = 0; i < n; i++)
            {
                float y = -hy + I(0.7f) + span * (n - 1 - i + 0.5f) / n;
                k.BoxR(new Vector3(0, y, z0 + tf + hood * 0.5f), new Vector3(2 * hx - I(1.3f), span / n * 1.05f, I(0.07f)), Quaternion.Euler(-28f, 0, 0), w);
            }
            k.Tube(new Vector3(0, 0, z0), new Vector3(0, 0, z0 - I(6f)), r * 0.97f, g);
        }

        public static Vector3 WallCapPort(Vector3 s) => new Vector3(0, 0, -s.z / 2 - I(6f));

        /// <summary>Roof jack: flashing plate, hood with screened opening, tail down through the roof deck.</summary>
        public static void RoofCap(HKit k, Vector3 s, float ductIn)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, r = I(ductIn) / 2;
            int b = k.M(SurfaceKind.MatteBlack), g = Galv(k), dark = k.M(SurfaceKind.DarkPlastic);
            k.Box(-hx, hx, -hy, -hy + I(0.06f), -hz, hz, b);
            float bw = r + I(1.5f);
            k.Box(-bw, bw, -hy, hy - I(0.5f), -bw, bw, b, MeshBuilder.AllFaces & ~MeshBuilder.FaceNegX);
            k.Box(-bw - I(0.6f), bw + I(0.3f), hy - I(0.5f), hy, -bw - I(0.3f), bw + I(0.3f), b);
            k.Box(-bw - 0.0005f, -bw, -hy + I(1f), hy - I(1.2f), -bw + I(0.4f), bw - I(0.4f), dark, MeshBuilder.FaceNegX);
            k.Tube(new Vector3(0, -hy, 0), new Vector3(0, -hy - I(6f), 0), r * 0.97f, g);
        }

        public static Vector3 RoofCapPort(Vector3 s) => new Vector3(0, -s.y / 2 - I(6f), 0);

        /// <summary>80k upflow gas furnace (21x40x29): cabinet, two front doors with louvers, burner sight glass, gas valve
        /// + black iron gas line, PVC/B-vent flue, supply collar on top. (Own copy: ProductModels maps every "hvac-" id to a condenser.)</summary>
        public static void Furnace(HKit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2, y0 = -hy;
            int body = k.M(SurfaceKind.PaintedSteel), dark = k.M(SurfaceKind.DarkPlastic), g = Galv(k), blk = k.M(SurfaceKind.MatteBlack), white = k.M(SurfaceKind.PaintedWhite);
            int glass = k.M(SurfaceKind.DarkPlastic, new Color(0.35f, 0.2f, 0.1f)), brass = k.M(SurfaceKind.Brass);
            float top = hy - I(3f), fz = hz - I(0.5f);
            k.Box(-hx, hx, y0, top, -hz, fz, body);
            float split = y0 + s.y * 0.45f;
            k.Box(-hx + I(0.75f), hx - I(0.75f), split + I(0.25f), top - I(0.75f), fz, hz - I(0.1f), body);   // burner door
            k.Box(-hx + I(0.75f), hx - I(0.75f), y0 + I(1f), split - I(0.25f), fz, hz - I(0.1f), body);        // blower door
            for (int c = -1; c <= 1; c += 2)
                for (int r = 0; r < 6; r++)
                {
                    float x = c * s.x * 0.22f, y = top - I(3f) - r * I(1.1f);
                    k.Box(x - I(2.5f), x + I(2.5f), y - I(0.2f), y + I(0.2f), hz - I(0.1f), hz - I(0.04f), dark);
                }
            k.Box(-I(2f), I(2f), split - I(4f), split - I(1.5f), hz - I(0.1f), hz - I(0.04f), white);   // rating label
            k.Box(-I(1f), I(1f), split + I(1f), split + I(1.6f), hz - I(0.1f), hz - I(0.02f), dark);    // door pull
            k.Box(-I(0.7f), I(0.7f), split + I(4f), split + I(5f), hz - I(0.1f), hz - I(0.06f), glass);   // flame sight glass
            k.Box(-hx + I(1f), hx - I(1f), top, hy - I(1.5f), -hz + I(1f), hz - I(8.5f), g);            // supply collar
            k.Tube(new Vector3(0f, top, hz - I(4.5f)), new Vector3(0f, hy + I(8f), hz - I(4.5f)), I(2.25f), g); // 4" B-vent flue
            k.Ring(new Vector3(0f, hy + I(2f), hz - I(4.5f)), Vector3.up, I(2.4f), I(2.2f), I(0.25f), g);
            // gas: black iron drop with drip leg into the right side, gas valve inside the burner door
            float gx = hx + I(1.2f), gz = hz - I(6f);
            k.Cyl(new Vector3(gx, split + I(6f), gz), new Vector3(gx, hy + I(10f), gz), I(0.42f), blk, 10);
            k.Cyl(new Vector3(gx, split + I(6f), gz), new Vector3(gx, split + I(2f), gz), I(0.42f), blk, 10);
            k.Cyl(new Vector3(gx, split + I(6f), gz), new Vector3(hx, split + I(6f), gz), I(0.42f), blk, 10);
            k.Box(gx - I(0.8f), gx + I(0.8f), split + I(8f), split + I(9.5f), gz - I(0.8f), gz + I(0.8f), brass); // shutoff
            k.Box(gx + I(0.3f), gx + I(2.3f), split + I(8.6f), split + I(8.9f), gz - I(0.2f), gz + I(0.2f), k.M(SurfaceKind.PaintedWhite, new Color(1.1f, 0.85f, 0.2f)));
            // power whip + switch box on the left side
            k.Box(-hx - I(1.5f), -hx, split + I(8f), split + I(12f), hz - I(6f), hz - I(4f), k.M(SurfaceKind.Galvanized));
        }

        // ------------------------------------------------------------------ misc

        public static void Pad(HKit k, Vector3 s)
        {
            int c = k.M(SurfaceKind.Concrete, new Color(1.08f, 1.08f, 1.05f)), cd = k.M(SurfaceKind.Concrete, new Color(0.9f, 0.9f, 0.88f));
            k.B.AddBevelBox(Vector3.zero, new Vector3(s.x, s.y, s.z), Quaternion.identity, I(0.6f), c, c);
            k.B.AddBevelBox(Vector3.zero, new Vector3(s.x - I(0.2f), s.y, s.z - I(0.2f)), Quaternion.Euler(0, 90, 0), I(0.6f), c, c);
            float hy = s.y / 2;
            for (int i = 1; i < 4; i++)
            {
                float x = -s.x / 2 + s.x * i / 4f;
                k.Box(x - I(0.06f), x + I(0.06f), hy, hy + 0.0006f, -s.z / 2 + I(1f), s.z / 2 - I(1f), cd, MeshBuilder.FacePosY);
                k.Box(-s.x / 2 + I(1f), s.x / 2 - I(1f), hy, hy + 0.0006f, x - I(0.06f), x + I(0.06f), cd, MeshBuilder.FacePosY);
            }
        }

        public static void Thermostat(HKit k, Vector3 s)
        {
            float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            int w = k.M(SurfaceKind.Porcelain), dark = k.M(SurfaceKind.DarkPlastic), lcd = k.M(SurfaceKind.Glass);
            int green = k.M(SurfaceKind.PaintedWhite, new Color(0.55f, 0.8f, 0.95f));
            k.Box(-hx, hx, -hy, hy, -hz, -hz + I(0.15f), w);
            k.B.AddBevelBox(new Vector3(0, 0, I(0.1f)), new Vector3(s.x - I(0.25f), s.y - I(0.25f), s.z - I(0.35f)), Quaternion.identity, I(0.35f), w, w);
            k.Box(-I(1.15f), I(1.15f), I(0.1f), I(1.5f), hz - I(0.2f), hz - I(0.12f), dark);
            k.Box(-I(1.0f), I(1.0f), I(0.25f), I(1.35f), hz - I(0.12f), hz - I(0.1f), green, MeshBuilder.FacePosZ);
            k.Box(-I(1.0f), I(1.0f), I(0.25f), I(1.35f), hz - I(0.1f), hz - I(0.09f), lcd, MeshBuilder.FacePosZ);
            for (int i = -1; i <= 1; i++)
                k.Box(i * I(0.8f) - I(0.3f), i * I(0.8f) + I(0.3f), -I(1.2f), -I(0.85f), hz - I(0.2f), hz - I(0.05f), dark);
        }

        /// <summary>Stock duct section (5' joint) for site piles: rect or round, along local Z.</summary>
        public static void DuctSection(HKit k, Vector3 s, bool rect, bool aluminum)
        {
            int g = aluminum ? k.M(SurfaceKind.BrushedSteel) : Galv(k);
            float hz = s.z / 2;
            if (rect)
            {
                int sides = MeshBuilder.FacePosX | MeshBuilder.FaceNegX | MeshBuilder.FacePosY | MeshBuilder.FaceNegY;
                k.Box(-s.x / 2, s.x / 2, -s.y / 2, s.y / 2, -hz, hz, g, sides);
                k.Box(-s.x / 2, s.x / 2, -s.y / 2, s.y / 2, -hz, hz, g, sides, true);
                k.Box(-s.x / 2 - I(0.3f), -s.x / 2, s.y / 2 - I(0.3f), s.y / 2, -hz, hz, g); // Pittsburgh seam
                k.Box(-s.x / 2 - I(0.06f), s.x / 2 + I(0.06f), -s.y / 2 - I(0.06f), s.y / 2 + I(0.06f), hz - I(1f), hz, g, sides); // S/drive end
            }
            else
            {
                float r = s.x / 2;
                k.Tube(new Vector3(0, 0, -hz), new Vector3(0, 0, hz), r, g);
                k.Box(-I(0.1f), I(0.1f), r - I(0.02f), r + I(0.1f), -hz, hz, g);              // snap-lock seam
                if (!aluminum) for (float z = hz - I(1.5f); z > hz - I(2.6f); z -= I(0.35f)) k.Ring(new Vector3(0, 0, z), Vector3.forward, r - I(0.05f), r - I(0.15f), I(0.15f), g); // crimped end
            }
        }

        /// <summary>Line set coil (two coils, black insulated suction + bare copper liquid) for the site pile.</summary>
        public static void LineSetCoil(HKit k, Vector3 s)
        {
            int cu = k.M(SurfaceKind.Brass, MepLook.Copper), foam = k.M(SurfaceKind.DarkPlastic, new Color(0.55f, 0.55f, 0.56f));
            float R = Mathf.Min(s.x, s.z) / 2 - I(1f);
            for (int i = 0; i < 4; i++) k.Ring(new Vector3(0, -s.y / 2 + I(1f) + i * I(1.6f), 0), Vector3.up, R, R - I(1.7f), I(1.6f), foam, 32);
            for (int i = 0; i < 4; i++) k.Ring(new Vector3(0, -s.y / 2 + I(0.4f) + i * I(1.4f), 0), Vector3.up, R - I(2f), R - I(2.4f), I(0.38f), cu, 32);
        }
    }
}
