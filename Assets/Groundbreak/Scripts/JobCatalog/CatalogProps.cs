using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>State a prop model is drawn from.</summary>
    public class PropCtx
    {
        public JobDef Def;
        public CatalogDefect Defect;
        public bool Fixed, CauseFixed, Open, Diagnosed;
        public string CauseId = "";
        public Vector3 Size;
        public bool Did(string step) => Defect != null && Defect.DidStep(step);
        public bool Has(string state) => Defect != null && Defect.HasState(state);
        /// <summary>Damage is still visible (not repaired).</summary>
        public bool Bad => !CauseFixed;
    }

    /// <summary>
    /// Real-size models for catalog defects, drawn in the defect frame (metres). Conventions:
    /// wall props — origin on the finished wall face, +Z out of the wall, +Y up;
    /// floor/ground props — origin on the floor or grade, +Y up, +Z toward the viewer;
    /// ceiling props — origin on the ceiling surface, drawn downward (-Y).
    /// Each model shows the fixture, the visible damage while it is not repaired, and opened covers while a state is open.
    /// </summary>
    public static class CatalogProps
    {
        static readonly Dictionary<string, Action<RM, PropCtx>> _props = new Dictionary<string, Action<RM, PropCtx>>();
        public static bool Exists(string id) => id != null && _props.ContainsKey(id);
        public static IEnumerable<string> Ids => _props.Keys;
        public static void Register(string id, Action<RM, PropCtx> build) => _props[id] = build;

        public static void Build(RM m, string id, PropCtx c)
        {
            if (id != null && _props.TryGetValue(id, out var b)) { b(m, c); return; }
            int mk = m.Mat(new Color(0.9f, 0.5f, 0.1f));
            m.Box(new Vector3(0, 0.1f, 0), new Vector3(0.2f, 0.2f, 0.2f), mk);
        }

        static readonly Color Water = new Color(0.42f, 0.55f, 0.68f), Stain = new Color(0.55f, 0.45f, 0.3f), Rot = new Color(0.25f, 0.2f, 0.15f),
            Rust = new Color(0.5f, 0.3f, 0.15f), Wall = new Color(0.93f, 0.92f, 0.88f), Crack = new Color(0.15f, 0.14f, 0.13f), Orange = new Color(0.95f, 0.5f, 0.1f);
        static readonly Quaternion Qx = Quaternion.Euler(90, 0, 0), Qz = Quaternion.Euler(0, 0, 90);

        static void Puddle(RM m, Vector3 c, float r) => m.Disc(c + Vector3.up * 0.003f, r, m.Mat(Water), 16);
        static void CrackLine(RM m, Vector3 a, Vector3 b, Vector3 normal, int mat, int segs = 5, float jig = 0.02f)
        {
            var side = Vector3.Cross(normal, (b - a).normalized);
            var rnd = new System.Random(7);
            Vector3 prev = a;
            for (int i = 1; i <= segs; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)segs) + (i < segs ? side * (float)(rnd.NextDouble() - 0.5) * jig * 2f : Vector3.zero);
                var d = p - prev;
                m.Box((p + prev) / 2f, new Vector3(0.004f, 0.002f, d.magnitude), Quaternion.LookRotation(d, normal), mat);
                prev = p;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            // ================================================================ plumbing
            Register("toilet", (m, c) =>
            {
                int por = m.Mat(SurfaceKind.Porcelain), chrome = m.Mat(SurfaceKind.Nickel), braid = m.Mat(SurfaceKind.BrushedSteel), dark = m.Mat(SurfaceKind.DarkPlastic);
                // tank (back on the wall), bowl toward +Z, seat
                m.Box(new Vector3(0, 0.56f, 0.11f), new Vector3(0.43f, 0.38f, 0.2f), por);
                if (!c.Has("lid-off")) m.Box(new Vector3(0, 0.765f, 0.11f), new Vector3(0.45f, 0.03f, 0.22f), por);
                else
                {
                    m.Box(new Vector3(0.45f, 0.015f, 0.25f), new Vector3(0.45f, 0.03f, 0.22f), por);   // lid on the floor
                    var water = c.Has("water-off") ? 0.42f : 0.68f;
                    m.Box(new Vector3(0, water, 0.11f), new Vector3(0.39f, 0.004f, 0.16f), m.Mat(Water));
                    m.Cyl(new Vector3(-0.12f, 0.6f, 0.11f), 0.016f, 0.3f, m.Mat(SurfaceKind.DarkPlastic));    // fill valve
                    m.Cyl(new Vector3(0.03f, 0.43f, 0.11f), 0.035f, 0.012f, c.CauseId == "flapper" && c.Bad ? m.Mat(new Color(0.45f, 0.2f, 0.18f)) : dark); // flapper
                    m.Cyl(new Vector3(0.09f, 0.55f, 0.11f), 0.013f, 0.24f, m.Mat(SurfaceKind.PaintedWhite));  // overflow tube
                }
                m.Box(new Vector3(0, 0.2f, 0.36f), new Vector3(0.36f, 0.4f, 0.36f), por);
                m.Cyl(new Vector3(0, 0.39f, 0.43f), 0.19f, 0.02f, por, 18);
                m.Cyl(new Vector3(0, 0.41f, 0.43f), 0.2f, 0.018f, m.Mat(SurfaceKind.PaintedWhite), 18);
                m.Box(new Vector3(-0.18f, 0.66f, 0.2f), new Vector3(0.06f, 0.015f, 0.02f), chrome);              // trip lever
                // shut-off stop and supply on the wall, left
                m.Cyl(new Vector3(-0.15f, 0.16f, 0.03f), Qx, 0.012f, 0.06f, chrome);
                m.Tube(new Vector3(-0.15f, 0.17f, 0.06f), new Vector3(-0.15f, 0.37f, 0.08f), 0.006f, braid);
                if (c.CauseId == "stop" && c.Bad || c.CauseId == "wax" && c.Bad) Puddle(m, new Vector3(c.CauseId == "stop" ? -0.15f : 0.05f, 0, 0.3f), 0.18f);
                if (c.CauseId == "clog" && c.Bad) m.Disc(new Vector3(0, 0.36f, 0.45f), 0.13f, m.Mat(new Color(0.5f, 0.45f, 0.35f)), 14);
                else m.Disc(new Vector3(0, 0.33f, 0.45f), 0.11f, m.Mat(Water), 14);
            });

            Register("waterheater", (m, c) =>
            {
                int steel = m.Mat(SurfaceKind.PaintedWhite, new Color(0.85f, 0.85f, 0.82f)), brass = m.Mat(SurfaceKind.Brass), cu = m.Mat(new Color(0.72f, 0.45f, 0.25f));
                m.Cyl(new Vector3(0, 0.76f, 0.3f), 0.28f, 1.52f, steel, 20);
                m.Cyl(new Vector3(0, 0.03f, 0.3f), 0.31f, 0.06f, m.Mat(SurfaceKind.DarkPlastic), 20);                      // drain pan
                // element access panels (upper and lower)
                foreach (float y in new[] { 1.15f, 0.35f })
                {
                    if (c.Has("panel-off")) { m.Box(new Vector3(0, y, 0.58f), new Vector3(0.12f, 0.12f, 0.012f), m.Mat(SurfaceKind.Galvanized)); m.Cyl(new Vector3(0, y, 0.6f), Qx, 0.02f, 0.03f, m.Mat(SurfaceKind.Galvanized)); }
                    else m.Box(new Vector3(0, y, 0.585f), new Vector3(0.16f, 0.2f, 0.006f), steel);
                }
                // pipes, T&P valve and discharge pipe
                m.Tube(new Vector3(-0.1f, 1.52f, 0.3f), new Vector3(-0.1f, 2.2f, 0.3f), 0.011f, cu);
                m.Tube(new Vector3(0.1f, 1.52f, 0.3f), new Vector3(0.1f, 2.2f, 0.3f), 0.011f, cu);
                m.Box(new Vector3(0.28f, 1.35f, 0.3f), new Vector3(0.06f, 0.05f, 0.05f), brass);
                m.Tube(new Vector3(0.31f, 1.33f, 0.3f), new Vector3(0.31f, 0.15f, 0.3f), 0.011f, cu);
                if (c.Def.Id.Contains("relief")) m.Cyl(new Vector3(0.2f, 2.0f, 0.3f), 0.1f, 0.25f, m.Mat(SurfaceKind.Galvanized, new Color(0.6f, 0.7f, 0.85f)), 14); // expansion tank (if fitted)
                if (c.Bad && c.Def.Id.Contains("relief")) Puddle(m, new Vector3(0.31f, 0, 0.45f), 0.15f);
                m.Box(new Vector3(0, 1.62f, 0.3f), new Vector3(0.18f, 0.08f, 0.1f), m.Mat(SurfaceKind.DarkPlastic));          // j-box
            });

            Register("hosebib", (m, c) =>
            {
                int brass = m.Mat(SurfaceKind.Brass), cu = m.Mat(new Color(0.72f, 0.45f, 0.25f));
                m.Box(new Vector3(0, 0, 0.004f), new Vector3(0.1f, 0.1f, 0.008f), m.Mat(SurfaceKind.PaintedWhite));
                m.Cyl(new Vector3(0, 0, 0.04f), Qx, 0.014f, 0.07f, brass);
                m.Tube(new Vector3(0, 0, 0.07f), new Vector3(0, -0.06f, 0.09f), 0.012f, brass);
                m.Cyl(new Vector3(0, 0.045f, 0.06f), 0.02f, 0.012f, m.Mat(new Color(0.7f, 0.15f, 0.1f)), 10);
                if (c.Has("stem-out")) m.Tube(new Vector3(0.1f, -0.4f, 0.2f), new Vector3(0.1f, -0.4f, 0.5f), 0.008f, brass);
                if (c.Bad) { Puddle(m, new Vector3(0, -0.45f, 0.15f), 0.2f); m.Box(new Vector3(0, -0.2f, 0.012f), new Vector3(0.05f, 0.3f, 0.004f), m.Mat(Stain)); }
                if (c.Has("wall-open")) m.Tube(new Vector3(0, 0, -0.02f), new Vector3(0, 0, -0.3f), 0.011f, cu);
            });

            Register("sinkbase", (m, c) =>
            {
                bool kitchen = c.Def.Where != null && c.Def.Where.Spot == HouseSpot.KitchenSink;
                int pvc = m.Mat(SurfaceKind.PaintedWhite, c.CauseFixed ? Color.white : new Color(0.86f, 0.84f, 0.78f)), chrome = m.Mat(SurfaceKind.Nickel), braid = m.Mat(SurfaceKind.BrushedSteel);
                float r = kitchen ? 0.019f : 0.016f;
                m.Box(new Vector3(0, 0.72f, -0.30f), new Vector3(kitchen ? 0.5f : 0.36f, 0.16f, 0.36f), m.Mat(kitchen ? SurfaceKind.BrushedSteel : SurfaceKind.Porcelain));
                bool disposal = c.Def.Id.Contains("disposal");
                if (disposal)
                {
                    m.Cyl(new Vector3(0, 0.5f, -0.30f), 0.085f, 0.28f, m.Mat(SurfaceKind.MatteBlack), 16);
                    m.Cyl(new Vector3(0, 0.36f, -0.30f), 0.012f, 0.01f, m.Mat(SurfaceKind.BrushedSteel), 6);        // hex socket
                    m.Box(new Vector3(0.07f, 0.4f, -0.22f), new Vector3(0.02f, 0.02f, 0.012f), m.Mat(new Color(0.8f, 0.1f, 0.1f))); // reset button
                }
                else m.Cyl(new Vector3(0, 0.53f, -0.30f), r, 0.22f, pvc);
                m.Tube(new Vector3(0, 0.45f, -0.42f), new Vector3(0, 0.455f, -0.60f), r, pvc);
                if (!c.Has("trap-off"))
                {
                    var pts = new List<Vector3>();
                    for (int i = 0; i <= 8; i++) { float t = Mathf.PI * i / 8f; pts.Add(new Vector3(0, 0.40f - 0.06f * Mathf.Sin(t), -0.36f + 0.06f * Mathf.Cos(t))); }
                    m.Path(pts, r, pvc);
                }
                else { m.Tube(new Vector3(-0.28f, 0.13f, -0.2f), new Vector3(-0.16f, 0.13f, -0.24f), r, pvc); m.Cyl(new Vector3(0.25f, 0.2f, -0.15f), 0.13f, 0.18f, m.Mat(new Color(0.3f, 0.45f, 0.75f)), 14); }
                foreach (float sx in new[] { -0.1f, 0.1f })
                {
                    m.Cyl(new Vector3(sx, 0.40f, -0.585f), Qx, 0.012f, 0.05f, chrome);
                    m.Path(new[] { new Vector3(sx, 0.415f, -0.58f), new Vector3(sx, 0.52f, -0.56f), new Vector3(sx * 0.6f, 0.66f, -0.5f), new Vector3(sx * 0.4f, 0.8f, -0.47f) }, 0.006f, braid);
                }
                // faucet on the deck
                float top = 0.8f + (kitchen ? 0.14f : 0.05f);
                m.Cyl(new Vector3(0, top + 0.12f, -0.5f), 0.022f, 0.24f, chrome, 12);
                m.Tube(new Vector3(0, top + 0.24f, -0.5f), new Vector3(0, top + 0.26f, -0.32f), 0.014f, chrome);
                m.Box(new Vector3(0.06f, top + 0.2f, -0.5f), new Vector3(0.1f, 0.015f, 0.02f), chrome);
                if (c.Def.Id.Contains("faucet") && c.Bad) m.Cyl(new Vector3(0, top + 0.2f, -0.32f), 0.004f, 0.03f, m.Mat(Water), 6);
                if (c.Has("cartridge-out")) m.Cyl(new Vector3(0.3f, 0.86f, -0.1f), 0.015f, 0.08f, m.Mat(SurfaceKind.Brass), 8);
                if (c.Bad && !disposal) Puddle(m, new Vector3(0, 0.115f, -0.25f), 0.12f);
            });

            Register("tubspout", (m, c) =>
            {
                int chrome = m.Mat(SurfaceKind.Nickel);
                // origin = TubCaulk joint frame: +X along the joint, +Z into the room; valve on the wall 30" above the tub rim
                m.Cyl(new Vector3(0.55f, 0.6f, 0.01f), Qx, 0.09f, 0.012f, chrome, 18);              // escutcheon
                if (!c.Has("trim-off")) m.Box(new Vector3(0.55f, 0.6f, 0.05f), new Vector3(0.1f, 0.025f, 0.06f), chrome);
                else m.Cyl(new Vector3(0.55f, 0.6f, 0.03f), Qx, 0.02f, 0.06f, m.Mat(SurfaceKind.Brass), 10);
                m.Box(new Vector3(0.55f, 0.15f, 0.06f), new Vector3(0.05f, 0.04f, 0.12f), chrome);  // spout
                if (c.Bad) m.Cyl(new Vector3(0.55f, 0.1f, 0.11f), 0.003f, 0.04f, m.Mat(Water), 6);
            });

            // ================================================================ electrical
            Register("panel", (m, c) =>
            {
                int gray = m.Mat(SurfaceKind.PaintedSteel, new Color(0.62f, 0.63f, 0.62f)), blk = m.Mat(SurfaceKind.DarkPlastic);
                m.Box(new Vector3(0, 0, 0.04f), new Vector3(0.36f, 0.76f, 0.08f), gray);
                if (c.Has("cover-off")) { m.Box(new Vector3(0.5f, -1.0f, 0.3f), new Vector3(0.36f, 0.76f, 0.01f), gray); m.Box(new Vector3(0, 0, 0.081f), new Vector3(0.3f, 0.7f, 0.002f), m.Mat(SurfaceKind.Galvanized)); }
                else m.Box(new Vector3(-0.2f, 0, 0.25f), new Vector3(0.01f, 0.7f, 0.34f), gray);   // door swung open
                for (int i = 0; i < 10; i++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        bool tripped = c.Bad && i == 3 && s == 1 && !c.Def.Id.Contains("add");
                        m.Box(new Vector3(s * 0.05f, 0.28f - i * 0.055f, 0.09f), new Vector3(0.08f, 0.024f, 0.02f), blk);
                        m.Box(new Vector3(s * (tripped ? 0.035f : 0.06f), 0.28f - i * 0.055f, 0.103f), new Vector3(0.018f, 0.012f, 0.012f), tripped ? m.Mat(Orange) : blk);
                    }
            });

            Register("receptacle", (m, c) =>
            {
                bool gfci = c.Def.Id.Contains("gfci");
                int plate = m.Mat(SurfaceKind.PaintedWhite, Color.white), dev = m.Mat(SurfaceKind.PaintedWhite, new Color(0.96f, 0.96f, 0.93f));
                bool newBox = c.Def.Id.Contains("add") && !c.Did("box");
                if (newBox) { m.Box(new Vector3(0, 0, 0.002f), new Vector3(0.06f, 0.1f, 0.003f), m.Mat(Crack)); return; }   // pencil outline on the wall
                if (c.Has("plate-off"))
                {
                    m.Box(new Vector3(0, 0, -0.03f), new Vector3(0.06f, 0.09f, 0.06f), m.Mat(new Color(0.2f, 0.35f, 0.75f)));  // box
                    m.Box(new Vector3(0, 0, 0.06f), new Vector3(0.045f, 0.105f, 0.03f), dev);
                    m.Tube(new Vector3(-0.015f, 0.02f, 0.0f), new Vector3(-0.025f, 0.02f, 0.05f), 0.002f, m.Mat(Crack));
                    m.Tube(new Vector3(0.015f, 0.02f, 0.0f), new Vector3(0.025f, 0.02f, 0.05f), 0.002f, m.Mat(Color.white));
                    m.Tube(new Vector3(0f, -0.03f, 0.0f), new Vector3(0f, -0.04f, 0.05f), 0.002f, m.Mat(new Color(0.8f, 0.7f, 0.3f)));
                }
                else
                {
                    m.Box(new Vector3(0, 0, 0.003f), new Vector3(0.07f, 0.115f, 0.006f), plate);
                    m.Box(new Vector3(0, 0, 0.008f), new Vector3(0.034f, gfci ? 0.067f : 0.09f, 0.006f), dev);
                    if (gfci) { m.Box(new Vector3(0, 0.004f, 0.012f), new Vector3(0.012f, 0.008f, 0.004f), m.Mat(Crack)); m.Box(new Vector3(0, -0.01f, 0.012f), new Vector3(0.012f, 0.008f, 0.004f), m.Mat(new Color(0.8f, 0.1f, 0.1f))); }
                    if (c.Bad && c.Def.Id.Contains("loose")) m.Box(new Vector3(0, -0.02f, 0.012f), new Vector3(0.02f, 0.01f, 0.004f), m.Mat(Rot));
                }
            });

            Register("switch", (m, c) =>
            {
                int plate = m.Mat(SurfaceKind.PaintedWhite, Color.white);
                if (c.Has("plate-off"))
                {
                    m.Box(new Vector3(0, 0, -0.03f), new Vector3(0.06f, 0.09f, 0.06f), m.Mat(new Color(0.2f, 0.35f, 0.75f)));
                    m.Box(new Vector3(0, 0, 0.05f), new Vector3(0.035f, 0.1f, 0.025f), plate);
                    m.Tube(new Vector3(-0.012f, 0.03f, 0f), new Vector3(-0.02f, 0.03f, 0.04f), 0.002f, m.Mat(Crack));
                    m.Tube(new Vector3(0.012f, -0.03f, 0f), new Vector3(0.02f, -0.03f, 0.04f), 0.002f, m.Mat(Crack));
                }
                else
                {
                    m.Box(new Vector3(0, 0, 0.003f), new Vector3(0.07f, 0.115f, 0.006f), plate);
                    m.Box(new Vector3(0, c.Bad ? -0.004f : 0.006f, 0.011f), new Vector3(0.012f, 0.024f, 0.01f), plate);
                }
            });

            Register("ceilinglight", (m, c) =>
            {
                int glass = m.Mat(SurfaceKind.Glass), nickel = m.Mat(SurfaceKind.Nickel), white = m.Mat(SurfaceKind.PaintedWhite);
                if (c.Has("fixture-down") || (c.Def.Id.Contains("swap") && !c.Did("hang")))
                {
                    m.Cyl(new Vector3(0, -0.02f, 0), 0.05f, 0.04f, m.Mat(new Color(0.2f, 0.35f, 0.75f)), 10);   // box in the ceiling
                    m.Tube(new Vector3(-0.01f, -0.03f, 0), new Vector3(-0.03f, -0.25f, 0.02f), 0.002f, m.Mat(Crack));
                    m.Tube(new Vector3(0.01f, -0.03f, 0), new Vector3(0.03f, -0.25f, 0.02f), 0.002f, m.Mat(Color.white));
                    if (c.Has("fixture-down")) m.Cyl(new Vector3(0, -0.3f, 0.03f), 0.16f, 0.06f, c.Def.Id.Contains("swap") && c.Did("hang") ? nickel : white, 18);
                    return;
                }
                bool newFix = c.Def.Id.Contains("swap") && c.Did("hang");
                m.Cyl(new Vector3(0, -0.008f, 0), 0.17f, 0.016f, newFix ? m.Mat(SurfaceKind.MatteBlack) : white, 20);
                m.L.B.AddFrustum(new Vector3(0, -0.06f, 0), Quaternion.Euler(180, 0, 0), 0.15f, 0.08f, 0.09f, 18, glass);
                if (c.Bad && c.Def.Id.Contains("flicker")) m.Disc(new Vector3(0, -0.11f, 0), 0.04f, m.Mat(Stain));
            });

            Register("smoke", (m, c) =>
            {
                m.Cyl(new Vector3(0, -0.02f, 0), 0.065f, 0.04f, m.Mat(SurfaceKind.PaintedWhite, c.CauseFixed ? Color.white : new Color(0.9f, 0.86f, 0.7f)), 18);
                m.Disc(new Vector3(0, -0.041f, 0.03f), 0.004f, m.Mat(c.Bad ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.1f, 0.8f, 0.2f)));
            });

            Register("fan", (m, c) =>
            {
                int white = m.Mat(SurfaceKind.PaintedWhite);
                if (c.Has("grille-off")) { m.Box(new Vector3(0, 0.0f, 0), new Vector3(0.22f, 0.012f, 0.22f), m.Mat(SurfaceKind.Galvanized)); m.Cyl(new Vector3(0, -0.04f, 0), 0.06f, 0.05f, m.Mat(SurfaceKind.DarkPlastic), 12); }
                else for (int i = -3; i <= 3; i++) m.Box(new Vector3(0, -0.012f, i * 0.035f), new Vector3(0.26f, 0.014f, 0.018f), white);
                if (c.Bad) m.Disc(new Vector3(0.05f, -0.022f, 0.12f), 0.06f, m.Mat(new Color(0.55f, 0.55f, 0.5f)));
            });

            Register("doorbell", (m, c) =>
            {
                m.Box(new Vector3(0, 0, 0.008f), new Vector3(0.03f, 0.08f, 0.016f), m.Mat(SurfaceKind.Brass));
                m.Cyl(new Vector3(0, 0, 0.018f), Qx, 0.008f, 0.006f, m.Mat(c.Bad ? Crack : new Color(1f, 0.85f, 0.4f)), 10);
            });

            Register("extlight", (m, c) =>
            {
                int blk = m.Mat(SurfaceKind.MatteBlack);
                m.Box(new Vector3(0, 0, 0.01f), new Vector3(0.12f, 0.2f, 0.02f), blk);
                m.Box(new Vector3(0, -0.02f, 0.1f), new Vector3(0.16f, 0.26f, 0.16f), m.Mat(SurfaceKind.Glass));
                m.Box(new Vector3(0, 0.12f, 0.1f), new Vector3(0.2f, 0.03f, 0.2f), blk);
                if (c.Bad) m.Box(new Vector3(0, -0.15f, 0.1f), new Vector3(0.17f, 0.005f, 0.17f), m.Mat(Rust));
            });

            // ================================================================ HVAC
            Register("furnace", (m, c) =>
            {
                int cab = m.Mat(SurfaceKind.PaintedSteel, new Color(0.88f, 0.87f, 0.84f)), galv = m.Mat(SurfaceKind.Galvanized), pvc = m.Mat(SurfaceKind.PaintedWhite);
                m.Box(new Vector3(0, 0.62f, 0.38f), new Vector3(0.53f, 1.24f, 0.72f), cab);                   // upflow cabinet
                m.Box(new Vector3(0, 1.5f, 0.38f), new Vector3(0.53f, 0.5f, 0.72f), galv);                     // A-coil box
                m.Box(new Vector3(0, 2.05f, 0.38f), new Vector3(0.6f, 0.6f, 0.6f), galv);                      // supply plenum
                m.Box(new Vector3(0.45f, 0.4f, 0.38f), new Vector3(0.36f, 0.8f, 0.5f), galv);                  // return drop
                // filter slot
                if (!c.Has("filter-out")) m.Box(new Vector3(0.27f, 0.2f, 0.38f), new Vector3(0.03f, 0.02f, 0.66f), m.Mat(c.CauseId == "filter" && c.Bad ? new Color(0.35f, 0.33f, 0.3f) : Color.white));
                else m.Box(new Vector3(0.0f, 0.02f, 1.0f), new Vector3(0.4f, 0.025f, 0.5f), m.Mat(c.CauseFixed ? Color.white : new Color(0.35f, 0.33f, 0.3f)));
                if (c.Has("door-off")) { m.Box(new Vector3(0.6f, 0.4f, 0.9f), new Vector3(0.5f, 0.6f, 0.02f), cab); m.Cyl(new Vector3(0, 0.35f, 0.5f), Qx, 0.16f, 0.3f, m.Mat(SurfaceKind.DarkPlastic), 16); }
                else m.Box(new Vector3(0, 0.45f, 0.745f), new Vector3(0.5f, 0.7f, 0.01f), cab);
                // condensate drain + trap off the coil box
                m.Path(new[] { new Vector3(-0.2f, 1.3f, 0.75f), new Vector3(-0.2f, 1.3f, 0.85f), new Vector3(-0.2f, 1.18f, 0.85f), new Vector3(-0.2f, 1.18f, 0.9f), new Vector3(-0.2f, 0.05f, 0.9f) }, 0.011f, pvc);
                if (c.CauseId == "trap" && c.Bad || c.CauseId == "line" && c.Bad) Puddle(m, new Vector3(-0.1f, 0, 0.9f), 0.25f);
                if (c.CauseId == "pump") m.Box(new Vector3(-0.2f, 0.08f, 0.95f), new Vector3(0.2f, 0.15f, 0.12f), m.Mat(SurfaceKind.PaintedWhite));
            });

            Register("thermostat", (m, c) =>
            {
                if (c.Has("stat-off")) { m.Box(new Vector3(0, 0, 0.005f), new Vector3(0.1f, 0.08f, 0.01f), m.Mat(SurfaceKind.PaintedWhite)); for (int i = 0; i < 4; i++) m.Box(new Vector3(-0.03f + i * 0.02f, -0.01f, 0.012f), new Vector3(0.004f, 0.03f, 0.004f), m.Mat(new[] { Color.red, Color.white, Color.green, Color.yellow }[i])); return; }
                m.Box(new Vector3(0, 0, 0.012f), new Vector3(0.12f, 0.09f, 0.024f), m.Mat(SurfaceKind.PaintedWhite));
                m.Box(new Vector3(0, 0.005f, 0.025f), new Vector3(0.07f, 0.04f, 0.002f), m.Mat(c.Bad ? new Color(0.25f, 0.27f, 0.25f) : new Color(0.55f, 0.8f, 0.9f)));
            });

            Register("register", (m, c) =>
            {
                int white = m.Mat(SurfaceKind.PaintedWhite);
                m.Box(new Vector3(0, -0.006f, 0), new Vector3(0.3f, 0.012f, 0.15f), white);
                for (int i = -4; i <= 4; i++) m.Box(new Vector3(i * 0.028f, -0.014f, 0), new Vector3(0.006f, 0.01f, 0.13f), white);
                // attic hatch nearby and the duct above it while the hatch is open
                if (c.Has("hatch-open"))
                {
                    m.Box(new Vector3(0.9f, -0.01f, 0), new Vector3(0.56f, 0.02f, 0.76f), m.Mat(SurfaceKind.PineLumber));
                    m.Tube(new Vector3(0, 0.05f, 0), new Vector3(0.6f, 0.25f, 0), 0.075f, m.Mat(SurfaceKind.Galvanized));
                    if (c.Bad) m.Tube(new Vector3(0.9f, 0.25f, 0), new Vector3(1.6f, 0.3f, 0), 0.08f, m.Mat(new Color(0.75f, 0.75f, 0.72f)));
                    else m.Tube(new Vector3(0.6f, 0.25f, 0), new Vector3(1.6f, 0.3f, 0), 0.08f, m.Mat(new Color(0.75f, 0.75f, 0.72f)));
                }
                else m.Box(new Vector3(0.9f, -0.01f, 0), new Vector3(0.6f, 0.02f, 0.8f), white);           // hatch cover
            });

            Register("ventcap", (m, c) =>
            {
                int alu = m.Mat(SurfaceKind.Galvanized);
                m.Box(new Vector3(0, 0, 0.01f), new Vector3(0.17f, 0.17f, 0.02f), alu);
                m.Box(new Vector3(0, -0.02f, 0.06f), new Vector3(0.13f, 0.1f, 0.08f), alu);
                if (c.Bad) m.Box(new Vector3(0, -0.07f, 0.06f), new Vector3(0.1f, 0.02f, 0.06f), m.Mat(new Color(0.75f, 0.72f, 0.68f)));   // lint
            });

            // ================================================================ carpentry / interior
            Register("doorhw", (m, c) =>
            {
                // InteriorDoor frame: floor-level centre of the 32" opening, mid wall; +X along the opening, +Z into the living room. Hinges on the -X jamb.
                int hw = m.Mat(SurfaceKind.Nickel), jamb = m.Mat(SurfaceKind.PaintedWhite);
                foreach (float y in new[] { 0.18f, 1.0f, 1.84f })
                    m.Box(new Vector3(-0.402f, y, 0.0f), new Vector3(0.004f, 0.089f, 0.03f), c.Bad && y > 1.7f && c.CauseId != "strike" ? m.Mat(Rust) : hw);
                m.Box(new Vector3(0.402f, 1.0f, 0.0f), new Vector3(0.004f, 0.07f, 0.025f), hw);           // strike plate
                if (c.Bad) m.Box(new Vector3(0.3f, 2.025f, 0f), new Vector3(0.1f, 0.004f, 0.04f), m.Mat(new Color(0.6f, 0.55f, 0.5f)));   // rub mark on the head jamb
                if (c.Did("shim")) m.Box(new Vector3(-0.398f, 1.84f, 0.025f), new Vector3(0.006f, 0.1f, 0.01f), m.Mat(SurfaceKind.PineLumber));
                m.Box(new Vector3(0, 2.042f, 0), new Vector3(0.84f, 0.012f, 0.1f), jamb);
            });

            Register("sill", (m, c) =>
            {
                // WindowCaulk frame: centre of the sill line on the outside wall face
                float w = c.Size.x;
                bool rotted = c.Bad && !c.Did("cut-out");
                int mat = c.Did("new-sill") || c.Did("epoxy") ? m.Mat(SurfaceKind.PaintedWhite) : m.Mat(SurfaceKind.PaintedWhite, new Color(0.85f, 0.83f, 0.78f));
                if (!c.Did("cut-out") || c.Did("new-sill") || c.Did("epoxy"))
                    m.Box(new Vector3(0, -0.02f, 0.04f), new Vector3(w, 0.035f, 0.09f), Quaternion.Euler(-8, 0, 0), mat);
                if (rotted) { m.Box(new Vector3(-w * 0.25f, -0.003f, 0.05f), new Vector3(w * 0.3f, 0.004f, 0.07f), m.Mat(Rot)); m.Box(new Vector3(-w * 0.25f, -0.06f, 0.02f), new Vector3(w * 0.25f, 0.08f, 0.004f), m.Mat(Stain)); }
                if (c.Did("cut-out") && !c.Did("new-sill") && !c.Did("epoxy")) m.Box(new Vector3(0, -0.02f, 0.01f), new Vector3(w, 0.03f, 0.01f), m.Mat(SurfaceKind.PineLumber));
            });

            Register("brickmould", (m, c) =>
            {
                // FrontDoor frame: floor-level centre of the 36" opening, +Z outdoors
                int paint = m.Mat(SurfaceKind.PaintedWhite);
                foreach (int s in new[] { -1, 1 })
                {
                    bool bad = c.Bad && s == -1;
                    m.Box(new Vector3(s * 0.51f, 1.06f, 0.09f), new Vector3(0.05f, 2.12f, 0.03f), paint);
                    if (bad) m.Box(new Vector3(s * 0.51f, 0.15f, 0.106f), new Vector3(0.052f, 0.3f, 0.004f), m.Mat(Rot));
                    if (c.Did("dutchman") && s == -1) m.Box(new Vector3(s * 0.51f, 0.15f, 0.106f), new Vector3(0.052f, 0.3f, 0.004f), m.Mat(SurfaceKind.PaintedWhite, Color.white));
                }
                m.Box(new Vector3(0, 2.14f, 0.09f), new Vector3(1.07f, 0.05f, 0.03f), paint);
            });

            Register("floorpatch", (m, c) =>
            {
                float w = c.Size.x, d = c.Size.z;
                bool lvp = c.Def.Id.Contains("lvp") || c.Def.Id.Contains("plank");
                bool tile = c.Def.Id.Contains("tile");
                int a = m.Mat(tile ? SurfaceKind.Porcelain : SurfaceKind.Oak), b = m.Mat(tile ? SurfaceKind.Porcelain : SurfaceKind.Oak, new Color(0.85f, 0.8f, 0.75f));
                if (tile)
                {
                    float t = 0.305f;
                    for (int i = 0; i < 3; i++) for (int k = 0; k < 3; k++)
                    {
                        bool center = i == 1 && k == 1;
                        if (center && c.Has("tile-out")) { m.Box(new Vector3((i - 1) * t, 0.002f, (k - 1) * t), new Vector3(t - 0.006f, 0.003f, t - 0.006f), m.Mat(new Color(0.55f, 0.55f, 0.52f))); continue; }
                        m.Box(new Vector3((i - 1) * t, 0.005f, (k - 1) * t), new Vector3(t - 0.006f, 0.008f, t - 0.006f), a);
                        if (center && c.Bad) CrackLine(m, new Vector3(-0.12f, 0.01f, -0.1f), new Vector3(0.13f, 0.01f, 0.11f), Vector3.up, m.Mat(Crack));
                    }
                    m.Box(new Vector3(0, 0.002f, 0), new Vector3(3 * t, 0.003f, 3 * t), m.Mat(new Color(0.6f, 0.6f, 0.58f)));
                    return;
                }
                int n = Mathf.Max(3, Mathf.RoundToInt(d / (lvp ? 0.18f : 0.083f)));
                float pw = d / n;
                for (int i = 0; i < n; i++)
                {
                    bool lifted = c.Bad && lvp && i == n / 2;
                    m.Box(new Vector3(0, lifted ? 0.01f : 0.004f, -d / 2 + pw * (i + 0.5f)), new Vector3(w, 0.006f, pw - 0.002f), lifted ? Quaternion.Euler(0, 0, 1.2f) : Quaternion.identity, i % 2 == 0 ? a : b, Vector3.right);
                }
                if (c.Did("screw") || c.Did("shim-joist")) for (int i = -1; i <= 1; i++) m.Disc(new Vector3(i * 0.2f, 0.0085f, 0), 0.005f, m.Mat(SurfaceKind.Oak, new Color(0.7f, 0.6f, 0.5f)));
            });

            Register("transition", (m, c) =>
            {
                m.Box(new Vector3(0, 0.006f, 0), new Vector3(0.81f, 0.012f, 0.05f), c.Bad ? Quaternion.Euler(0, 0, 2.5f) : Quaternion.identity, m.Mat(SurfaceKind.Oak));
                if (c.Bad) m.Box(new Vector3(0.3f, 0.014f, 0.0f), new Vector3(0.03f, 0.004f, 0.05f), m.Mat(SurfaceKind.Nickel));
            });

            Register("deckstep", (m, c) =>
            {
                // deck stair at the outer edge of the deck: origin at grade below the deck edge, +Z away from the deck
                int pt = m.Mat(SurfaceKind.TreatedLumber), con = m.Mat(SurfaceKind.Concrete);
                m.Box(new Vector3(0, 0.02f, 0.45f), new Vector3(1.1f, 0.04f, 0.5f), con);                    // landing pad
                foreach (float x in new[] { -0.45f, 0.45f }) m.Box(new Vector3(x, 0.09f, 0.15f), new Vector3(0.038f, 0.18f, 0.28f), pt);   // stringers
                bool cracked = c.Bad && !c.Did("tread-off");
                if (!c.Did("tread-off") || c.Did("tread-new"))
                {
                    m.Box(new Vector3(0, 0.15f, 0.08f), new Vector3(1.0f, 0.025f, 0.14f), c.Did("tread-new") ? m.Mat(SurfaceKind.TreatedLumber, new Color(0.9f, 1f, 0.9f)) : pt);
                    m.Box(new Vector3(0, 0.15f, 0.22f), new Vector3(1.0f, 0.025f, 0.14f), pt);
                }
                if (cracked) CrackLine(m, new Vector3(-0.4f, 0.164f, 0.08f), new Vector3(0.35f, 0.164f, 0.09f), Vector3.up, m.Mat(Crack));
            });

            Register("sash", (m, c) =>
            {
                // origin on the inside wall face at the window sill line, +Z into the room
                int paint = m.Mat(SurfaceKind.PaintedWhite), glass = m.Mat(SurfaceKind.Glass);
                float w = 1.1f;
                float lower = c.Bad ? 0.0f : (c.Has("stops-off") ? 0.1f : 0.0f);
                m.Box(new Vector3(0, 0.02f, 0.03f), new Vector3(w + 0.1f, 0.025f, 0.08f), paint);       // stool
                m.Box(new Vector3(0, 0.3f + lower, -0.02f), new Vector3(w, 0.6f, 0.04f), paint);
                m.Box(new Vector3(0, 0.3f + lower, -0.02f), new Vector3(w - 0.1f, 0.5f, 0.045f), glass);
                m.Box(new Vector3(0, 0.88f, -0.06f), new Vector3(w, 0.6f, 0.04f), paint);
                m.Box(new Vector3(0, 0.88f, -0.06f), new Vector3(w - 0.1f, 0.5f, 0.045f), glass);
                if (c.Bad && c.CauseId == "paint") m.Box(new Vector3(0, 0.6f, 0.0f), new Vector3(w, 0.006f, 0.005f), m.Mat(Crack));
                if (c.Has("stops-off")) m.Box(new Vector3(0.2f, 0.01f, 0.4f), new Vector3(0.02f, 0.012f, 1.2f), paint);
            });

            Register("cabinet", (m, c) =>
            {
                // KitchenSink frame: base cabinet next to the sink base, door 15" wide to the right of the sink
                int oak = m.Mat(SurfaceKind.Oak), nickel = m.Mat(SurfaceKind.Nickel);
                bool drawer = c.Def.Id.Contains("drawer");
                if (drawer)
                {
                    float out_ = c.Bad ? 0.12f : 0f;
                    m.Box(new Vector3(0, 0.76f, 0.01f + out_), new Vector3(0.38f, 0.14f, 0.02f), c.Bad ? Quaternion.Euler(0, 0, 3f) : Quaternion.identity, oak);
                    m.Box(new Vector3(0, 0.76f, 0.03f + out_), new Vector3(0.1f, 0.012f, 0.012f), nickel);
                    return;
                }
                var rot = c.Bad ? Quaternion.Euler(0, 0, -2.5f) : Quaternion.identity;
                m.Box(new Vector3(0, 0.38f, 0.01f), new Vector3(0.38f, 0.55f, 0.02f), rot, oak);
                m.Box(new Vector3(0.15f, 0.55f, 0.03f), new Vector3(0.012f, 0.1f, 0.012f), nickel);
                if (c.Bad) m.Box(new Vector3(-0.17f, 0.58f, -0.005f), new Vector3(0.03f, 0.03f, 0.01f), m.Mat(new Color(0.8f, 0.75f, 0.6f)));   // stripped screw hole
            });

            Register("baseboard", (m, c) =>
            {
                float w = c.Size.x;
                m.Box(new Vector3(0, 0.07f, c.Bad ? 0.02f : 0.007f), new Vector3(w, 0.135f, 0.014f), c.Bad ? Quaternion.Euler(-4, 0, 0) : Quaternion.identity, m.Mat(SurfaceKind.PaintedWhite));
            });

            // ================================================================ drywall / paint
            Register("ceilingstain", (m, c) =>
            {
                if (c.Has("cut-open")) { m.Box(new Vector3(0, 0.05f, 0), new Vector3(0.4f, 0.1f, 0.4f), m.Mat(new Color(0.1f, 0.1f, 0.1f))); m.Box(new Vector3(0, 0.05f, 0.05f), new Vector3(0.03f, 0.03f, 0.5f), m.Mat(SurfaceKind.PineLumber)); return; }
                bool painted = c.Did("paint-ceiling");
                if (!painted) m.Disc(new Vector3(0, -0.003f, 0), 0.28f, m.Mat(c.Did("prime") ? Color.white : Stain), 20);
                if (!painted && !c.Did("prime")) m.Disc(new Vector3(0.03f, -0.004f, 0.02f), 0.18f, m.Mat(new Color(0.62f, 0.52f, 0.38f)), 18);
                if (c.Did("patch")) m.Box(new Vector3(0, -0.002f, 0), new Vector3(0.42f, 0.002f, 0.42f), m.Mat(Color.white));
            });

            Register("wallcrack", (m, c) =>
            {
                float w = c.Size.x, h = c.Size.y;
                bool pops = c.Def.Id.Contains("pop");
                int compound = m.Mat(new Color(0.88f, 0.88f, 0.86f));
                if (pops)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        var p = new Vector3(-w / 2 + 0.2f + (i % 3) * 0.4f, 0.4f + (i / 3) * 0.8f, 0.003f);
                        if (c.Bad && !c.Did("reset")) m.DiscZ(p, 0.012f, m.Mat(new Color(0.6f, 0.58f, 0.52f)), 10);
                        else if (c.Did("spot") && !c.Did("paint")) m.DiscZ(p, 0.035f, compound, 12);
                    }
                }
                else
                {
                    if (c.Bad && !c.Did("tape")) CrackLine(m, new Vector3(-w * 0.1f, h * 0.95f, 0.003f), new Vector3(w * 0.15f, h * 0.45f, 0.003f), Vector3.forward, m.Mat(Crack), 7, 0.03f);
                    if (c.Did("tape") && !c.Did("paint")) m.Box(new Vector3(0.0f, h * 0.7f, 0.003f), new Vector3(0.2f, h * 0.55f, 0.002f), Quaternion.Euler(0, 0, -20f), compound);
                }
                if (c.Did("paint") || c.Fixed) m.Box(new Vector3(0, h / 2f, 0.002f), new Vector3(w, h, 0.002f), m.Mat(Wall));
            });

            Register("wallpaint", (m, c) =>
            {
                float w = c.Size.x, h = c.Size.y;
                Color old = new Color(0.82f, 0.8f, 0.72f), fresh = new Color(0.72f, 0.8f, 0.82f);
                int coats = (c.Did("coat1") ? 1 : 0) + (c.Did("coat2") ? 1 : 0);
                Color col = coats == 0 ? old : coats == 1 ? Color.Lerp(old, fresh, 0.7f) : fresh;
                m.Box(new Vector3(0, h / 2f, 0.003f), new Vector3(w, h, 0.002f), m.Mat(SurfaceKind.PaintedWhite, col));
                if (coats == 0) { m.Box(new Vector3(-0.6f, 0.9f, 0.0045f), new Vector3(0.4f, 0.15f, 0.001f), m.Mat(new Color(0.6f, 0.58f, 0.52f))); m.DiscZ(new Vector3(0.8f, 1.4f, 0.0045f), 0.05f, m.Mat(new Color(0.6f, 0.58f, 0.52f))); }
                if (c.Has("covered")) { m.Box(new Vector3(0, 0.002f, 0.6f), new Vector3(w, 0.003f, 1.2f), m.Mat(new Color(0.92f, 0.9f, 0.85f))); m.Box(new Vector3(0, 0.003f, 0.005f), new Vector3(w, 0.006f, 0.012f), m.Mat(new Color(0.3f, 0.5f, 0.85f))); }
            });

            Register("cornerbead", (m, c) =>
            {
                m.Box(new Vector3(0, 0.6f, 0.01f), new Vector3(0.05f, 1.2f, 0.02f), m.Mat(Wall));
                if (c.Bad && !c.Did("bead-new")) m.Box(new Vector3(0, 0.45f, 0.022f), new Vector3(0.052f, 0.12f, 0.004f), m.Mat(new Color(0.6f, 0.6f, 0.6f)));
            });

            // ================================================================ exterior
            Register("siding", (m, c) =>
            {
                float w = c.Size.x, h = c.Size.y;
                Color sc = c.Defect != null && c.Defect.House != null ? c.Defect.House.Siding : Color.white;
                int vin = m.Mat(SurfaceKind.PaintedWhite, sc);
                int courses = Mathf.RoundToInt(h / 0.2f);
                for (int i = 0; i < courses; i++)
                {
                    bool hole = c.Bad && i == courses / 2 && !c.Did("panel-new");
                    if (c.Has("panel-off") && i == courses / 2) { m.Box(new Vector3(0, 0.1f + i * 0.2f, 0.005f), new Vector3(w, 0.2f, 0.004f), m.Mat(new Color(0.85f, 0.88f, 0.9f))); continue; }
                    m.Box(new Vector3(0, 0.1f + i * 0.2f, 0.012f), new Vector3(w, 0.2f, 0.01f), Quaternion.Euler(4, 0, 0), vin);
                    if (hole) { m.Box(new Vector3(0.2f, 0.1f + i * 0.2f, 0.02f), new Vector3(0.18f, 0.08f, 0.004f), m.Mat(Crack)); }
                }
            });

            Register("fascia", (m, c) =>
            {
                // Gutter frame: top of the front gutter, +X along it, +Z away from the fascia
                float w = c.Size.x;
                int paint = m.Mat(SurfaceKind.PaintedWhite);
                bool off = c.Did("cut-out") && !c.Did("fascia-new");
                if (!off) m.Box(new Vector3(0, -0.07f, -0.11f), new Vector3(w, 0.14f, 0.025f), c.Did("fascia-new") ? m.Mat(SurfaceKind.PaintedWhite, Color.white) : paint);
                else m.Box(new Vector3(0, -0.07f, -0.14f), new Vector3(w, 0.14f, 0.04f), m.Mat(SurfaceKind.PineLumber));        // rafter tails
                if (c.Bad && !off) m.Box(new Vector3(-w * 0.2f, -0.11f, -0.096f), new Vector3(w * 0.35f, 0.06f, 0.004f), m.Mat(Rot));
                if (!c.Has("gutter-off")) m.Box(new Vector3(0, -0.06f, -0.03f), new Vector3(w, 0.12f, 0.13f), m.Mat(SurfaceKind.PaintedWhite, new Color(0.96f, 0.96f, 0.95f)));
                else m.Box(new Vector3(0, -2.6f, 0.6f), new Vector3(w, 0.12f, 0.13f), m.Mat(SurfaceKind.PaintedWhite));
            });

            Register("roofpatch", (m, c) =>
            {
                // RoofShingles frame: +Y roof normal, +Z up-slope, +X along the eave
                int sh = m.Mat(SurfaceKind.DarkPlastic, new Color(0.35f, 0.34f, 0.33f)), sh2 = m.Mat(SurfaceKind.DarkPlastic, new Color(0.28f, 0.28f, 0.29f));
                for (int r = 0; r < 6; r++) for (int i = 0; i < 4; i++)
                {
                    bool missing = c.Bad && c.CauseId == "shingle" && r == 2 && i == 1;
                    bool lifted = c.Has("tabs-up") && r == 3 && (i == 1 || i == 2);
                    if (missing) { m.Box(new Vector3(-0.45f + i * 0.3f + (r % 2) * 0.15f, 0.004f, -0.35f + r * 0.14f), new Vector3(0.3f, 0.002f, 0.14f), m.Mat(new Color(0.15f, 0.25f, 0.4f))); continue; }
                    m.Box(new Vector3(-0.45f + i * 0.3f + (r % 2) * 0.15f, lifted ? 0.03f : 0.006f, -0.35f + r * 0.14f), new Vector3(0.298f, 0.005f, 0.14f), lifted ? Quaternion.Euler(-15, 0, 0) : Quaternion.identity, (r + i) % 3 == 0 ? sh2 : sh);
                }
                // plumbing vent pipe with boot
                m.Cyl(new Vector3(0.35f, 0.12f, 0.15f), 0.024f, 0.25f, m.Mat(SurfaceKind.PaintedWhite));
                m.L.B.AddFrustum(new Vector3(0.35f, 0.03f, 0.15f), Quaternion.identity, 0.12f, 0.03f, 0.06f, 14, c.Bad && c.CauseId == "boot" ? m.Mat(new Color(0.3f, 0.3f, 0.28f)) : m.Mat(SurfaceKind.MatteBlack));
                if (c.Bad && c.CauseId == "boot") m.Box(new Vector3(0.35f, 0.065f, 0.12f), new Vector3(0.05f, 0.004f, 0.005f), m.Mat(Crack));
                if (c.Bad && c.CauseId == "nailpop") m.Cyl(new Vector3(-0.2f, 0.02f, 0.05f), 0.012f, 0.02f, m.Mat(SurfaceKind.Galvanized), 8);
            });

            Register("skylight", (m, c) =>
            {
                // curb-mounted 2'x4' skylight on the roof (RoofShingles frame): curb, glass, step flashing up the sides, head and apron flashing
                int sh = m.Mat(SurfaceKind.DarkPlastic, new Color(0.35f, 0.34f, 0.33f)), alu = m.Mat(SurfaceKind.Galvanized), frame = m.Mat(SurfaceKind.MatteBlack);
                for (int r = 0; r < 8; r++) m.Box(new Vector3(0, 0.006f, -0.6f + r * 0.14f), new Vector3(1.2f, 0.005f, 0.14f), sh);
                m.Box(new Vector3(0, 0.08f, 0), new Vector3(0.66f, 0.15f, 1.28f), m.Mat(SurfaceKind.PineLumber));
                m.Box(new Vector3(0, 0.165f, 0), new Vector3(0.7f, 0.03f, 1.32f), frame);
                m.Box(new Vector3(0, 0.182f, 0), new Vector3(0.6f, 0.005f, 1.22f), m.Mat(SurfaceKind.Glass));
                bool open = c.Has("tear-back");
                for (int r = 0; r < 9; r++) foreach (int s2 in new[] { -1, 1 })
                {
                    bool gone = (c.Bad && s2 == -1 && r == 5 && !c.Did("flash-new")) || (open && s2 == -1 && r >= 4 && r <= 6);
                    if (!gone) m.Box(new Vector3(s2 * 0.37f, 0.06f, -0.58f + r * 0.14f), new Vector3(0.08f, 0.1f, 0.12f), alu);
                }
                m.Box(new Vector3(0, 0.03f, 0.7f), new Vector3(0.8f, 0.05f, 0.1f), alu);       // head flashing
                m.Box(new Vector3(0, 0.03f, -0.7f), new Vector3(0.8f, 0.05f, 0.1f), alu);      // apron
                if (c.Bad && c.CauseId == "seal") m.Box(new Vector3(0, 0.168f, -0.66f), new Vector3(0.6f, 0.004f, 0.01f), m.Mat(Crack));
            });

            Register("gutter", (m, c) =>
            {
                float w = c.Size.x;
                int alu = m.Mat(SurfaceKind.PaintedWhite, new Color(0.96f, 0.96f, 0.95f));
                m.Box(new Vector3(0, -0.06f, 0.0f), new Vector3(w, 0.12f, 0.13f), alu);
                // outlet and downspout to the ground at +X end
                bool split = c.Bad && c.CauseId == "elbow";
                m.Cyl(new Vector3(w / 2 - 0.1f, -0.16f, 0.02f), 0.04f, 0.08f, alu, 10);
                if (!split) m.Box(new Vector3(w / 2 - 0.1f, -1.4f, 0.02f), new Vector3(0.08f, 2.4f, 0.06f), alu);
                else { m.Box(new Vector3(w / 2 - 0.1f, -1.7f, 0.02f), new Vector3(0.08f, 1.8f, 0.06f), alu); m.Box(new Vector3(w / 2 + 0.1f, -2.55f, 0.15f), new Vector3(0.08f, 0.06f, 0.2f), alu); }
                if (c.Bad && c.CauseId == "seam") m.Cyl(new Vector3(0, -0.25f, 0.04f), 0.004f, 0.2f, m.Mat(Water), 6);
                if (c.Bad && c.CauseId == "pitch") m.Box(new Vector3(0, 0.0f, 0.0f), new Vector3(w * 0.8f, 0.004f, 0.1f), m.Mat(Water));
            });

            Register("ledger", (m, c) =>
            {
                // deck ledger on the house rim: origin at the top of the deck boards against the back wall, +Z out over the deck, +X along it
                float w = c.Size.x;
                int pt = m.Mat(SurfaceKind.TreatedLumber), galv = m.Mat(SurfaceKind.Galvanized);
                m.Box(new Vector3(0, -0.09f, 0.02f), new Vector3(w, 0.14f, 0.038f), c.Bad && c.CauseId == "rot" ? m.Mat(SurfaceKind.TreatedLumber, new Color(0.55f, 0.5f, 0.42f)) : pt);
                if (c.Did("flash") || !(c.CauseId == "rot" || c.CauseId == "flash")) m.Box(new Vector3(0, -0.02f, 0.03f), new Vector3(w, 0.01f, 0.06f), galv);
                for (int i = 0; i < 5; i++)
                {
                    float x = -w / 2 + 0.2f + i * (w - 0.4f) / 4f;
                    bool loose = c.Bad && c.CauseId == "lags";
                    m.Cyl(new Vector3(x, -0.05f, loose ? 0.06f : 0.045f), Qx, 0.012f, 0.02f, galv, 8);
                    m.Cyl(new Vector3(x, -0.12f, 0.045f), Qx, 0.012f, 0.02f, c.Did("lag") ? galv : m.Mat(Rust), 8);
                }
                if (c.Bad && c.CauseId != "lags") m.Box(new Vector3(0.2f, -0.1f, 0.041f), new Vector3(0.5f, 0.08f, 0.003f), m.Mat(Rot));
            });

            Register("fence", (m, c) =>
            {
                // FencePicket frame: base of a picket at grade, +Z toward the house; one 8' bay along X
                int ced = m.Mat(SurfaceKind.Oak, new Color(0.9f, 0.7f, 0.55f)), pt = m.Mat(SurfaceKind.TreatedLumber);
                float lean = c.Bad && c.CauseId == "post" && !c.Did("post-out") ? 8f : 0f;
                var rot = Quaternion.Euler(lean, 0, 0);
                bool postOut = c.Did("post-out") && !c.Did("post-set");
                if (!postOut) m.Box(rot * new Vector3(-1.22f, 0.95f, 0.06f), new Vector3(0.089f, 1.9f, 0.089f), rot, pt);
                m.Box(new Vector3(1.22f, 0.95f, 0.06f), new Vector3(0.089f, 1.9f, 0.089f), pt);
                foreach (float y in new[] { 0.3f, 1.0f, 1.65f })
                {
                    bool broken = c.Bad && c.CauseId == "rail" && y > 1f && !c.Did("rail-new");
                    if (broken) { m.Box(new Vector3(-0.6f, y, 0.06f), new Vector3(1.2f, 0.089f, 0.038f), Quaternion.Euler(0, 0, -6f), pt); continue; }
                    m.Box(rot * new Vector3(0, y, 0.06f), new Vector3(2.44f, 0.089f, 0.038f), rot, pt, Vector3.right);
                }
                for (int i = 0; i < 17; i++) m.Box(rot * new Vector3(-1.17f + i * 0.146f, 0.92f, 0.0f), new Vector3(0.14f, 1.83f, 0.016f), rot, ced, Vector3.up);
                if (c.Did("concrete")) m.Disc(new Vector3(-1.22f, 0.01f, 0.06f), 0.15f, m.Mat(SurfaceKind.Concrete));
            });

            Register("gate", (m, c) =>
            {
                int ced = m.Mat(SurfaceKind.Oak, new Color(0.9f, 0.7f, 0.55f)), pt = m.Mat(SurfaceKind.TreatedLumber), blk = m.Mat(SurfaceKind.MatteBlack);
                m.Box(new Vector3(-0.66f, 0.95f, 0.06f), new Vector3(0.089f, 1.9f, 0.089f), pt);
                m.Box(new Vector3(0.66f, 0.95f, 0.06f), new Vector3(0.089f, 1.9f, 0.089f), pt);
                float sag = c.Bad ? 2.5f : 0f;
                var rot = Quaternion.Euler(0, 0, sag);
                var o = new Vector3(-0.6f, 0, 0);
                Vector3 P(float x, float y) => o + rot * new Vector3(x, y, 0);
                for (int i = 0; i < 8; i++) m.Box(P(0.08f + i * 0.146f, 0.92f), new Vector3(0.14f, 1.75f, 0.016f), rot, ced);
                foreach (float y in new[] { 0.3f, 1.55f }) m.Box(P(0.6f, y) + new Vector3(0, 0, 0.03f), new Vector3(1.15f, 0.089f, 0.038f), rot, pt);
                if (c.Did("brace") || c.Did("kit")) m.Box(P(0.6f, 0.92f) + new Vector3(0, 0, 0.035f), new Vector3(1.5f, 0.06f, 0.012f), rot * Quaternion.Euler(0, 0, 47f), c.Did("kit") ? blk : pt);
                foreach (float y in new[] { 0.3f, 1.55f }) m.Box(new Vector3(-0.58f, y, 0.03f), new Vector3(0.15f, 0.03f, 0.01f), blk);
                m.Box(new Vector3(0.62f, 1.1f, 0.03f), new Vector3(0.1f, 0.03f, 0.02f), blk);                         // latch
            });

            Register("soffit", (m, c) =>
            {
                int alu = m.Mat(SurfaceKind.PaintedWhite);
                m.Box(new Vector3(0, -0.01f, 0), new Vector3(c.Size.x, 0.01f, 0.3f), alu);
                if (c.Bad && !c.Did("panel")) m.Box(new Vector3(0.1f, -0.016f, 0), new Vector3(0.2f, 0.004f, 0.15f), m.Mat(Crack));
                if (c.Did("screen")) m.Box(new Vector3(0.1f, -0.018f, 0), new Vector3(0.24f, 0.003f, 0.19f), m.Mat(SurfaceKind.Galvanized));
            });

            // ================================================================ concrete / masonry
            Register("step", (m, c) =>
            {
                // front stoop: origin at grade at the middle of the stoop's front edge, +Z away from the house; stoop toward -Z
                int con = m.Mat(SurfaceKind.BroomedConcrete);
                float h = c.Size.y;
                m.Box(new Vector3(0, h / 2f, -0.55f), new Vector3(1.82f, h + 0.004f, 1.12f), con);
                if (c.Bad && !c.Did("patch")) { CrackLine(m, new Vector3(-0.6f, h + 0.004f, -0.05f), new Vector3(-0.2f, h + 0.004f, -0.6f), Vector3.up, m.Mat(Crack), 6, 0.04f); m.Box(new Vector3(-0.5f, h * 0.6f, 0.002f), new Vector3(0.35f, h * 0.6f, 0.004f), m.Mat(new Color(0.5f, 0.5f, 0.48f))); }
                if (c.Did("chisel") && !c.Did("patch")) m.Box(new Vector3(-0.5f, h - 0.01f, -0.08f), new Vector3(0.36f, 0.03f, 0.16f), m.Mat(SurfaceKind.Concrete, new Color(0.6f, 0.6f, 0.58f)));
                if (c.Did("patch")) m.Box(new Vector3(-0.5f, h + 0.005f, -0.08f), new Vector3(0.36f, 0.003f, 0.16f), m.Mat(SurfaceKind.TroweledConcrete, new Color(0.85f, 0.85f, 0.82f)));
            });

            Register("slab", (m, c) =>
            {
                float w = c.Size.x, d = c.Size.z;
                int con = m.Mat(SurfaceKind.BroomedConcrete);
                bool walk = c.Def.Id.Contains("walk");
                m.Box(new Vector3(0, -0.002f, 0), new Vector3(w + 0.004f, 0.006f, d + 0.004f), con);
                if (c.Bad && c.Def.Id.Contains("spall") && !c.Did("resurface"))
                    for (int i = 0; i < 7; i++) m.Disc(new Vector3(-w * 0.3f + (i * 0.37f % w * 0.6f), 0.0015f, -d * 0.3f + (i * 0.53f % d * 0.6f)), 0.08f + (i % 3) * 0.04f, m.Mat(SurfaceKind.Gravel, new Color(0.65f, 0.63f, 0.6f)), 10);
                if (c.Did("resurface")) m.Box(new Vector3(0, 0.002f, 0), new Vector3(w, 0.003f, d), m.Mat(SurfaceKind.BroomedConcrete, new Color(0.88f, 0.88f, 0.85f)));
                if (walk && c.Bad && !c.Did("fill")) CrackLine(m, new Vector3(-w / 2, 0.0015f, 0.05f), new Vector3(w / 2, 0.0015f, -0.08f), Vector3.up, m.Mat(Crack), 6, 0.03f);
                if (c.Did("fill")) m.Box(new Vector3(0, 0.0016f, -0.015f), new Vector3(w, 0.002f, 0.02f), m.Mat(new Color(0.45f, 0.45f, 0.43f)));
            });

            Register("planter", (m, c) =>
            {
                // brick planter wall: origin at grade at the wall's centre front face, +Z toward the viewer; 6' x 24" high, 8" thick
                int brick = m.Mat(SurfaceKind.Brick), mortar = m.Mat(SurfaceKind.Mortar), raked = m.Mat(new Color(0.25f, 0.22f, 0.2f));
                float L = 1.83f, bh = 0.0571f, bl = 0.2032f, j = 0.0095f;
                m.Box(new Vector3(0, 0.3f, -0.1f), new Vector3(L, 0.6f, 0.19f), mortar);
                bool raking = c.Did("rake") && !c.Did("point");
                for (int r = 0; r < 8; r++)
                {
                    float y = j + r * (bh + j) + bh / 2f, off = (r % 2) * bl / 2f;
                    for (float x = -L / 2 + off; x < L / 2 - 0.05f; x += bl + j)
                    {
                        float w = Mathf.Min(bl, L / 2 - x);
                        m.Box(new Vector3(x + w / 2f, y, 0.005f), new Vector3(w, bh, 0.012f), brick);
                    }
                    if (c.Bad && !c.Did("point") && (r == 3 || r == 4)) m.Box(new Vector3(-0.3f, y + bh / 2f + j / 2f, 0.004f), new Vector3(0.9f, j, 0.004f), raking ? raked : m.Mat(new Color(0.55f, 0.53f, 0.5f)));
                }
                m.Box(new Vector3(0, 0.61f, -0.1f), new Vector3(L + 0.05f, 0.04f, 0.24f), m.Mat(SurfaceKind.Concrete));   // cap
                m.Box(new Vector3(0, 0.6f, -0.5f), new Vector3(L, 0.02f, 0.6f), m.Mat(SurfaceKind.Dirt));
            });

            Register("retwall", (m, c) =>
            {
                // segmental block retaining wall, 8' long: origin at grade at the wall's centre front face, +Z toward the low side
                int blk = m.Mat(SurfaceKind.ConcreteBlock, new Color(0.8f, 0.75f, 0.68f)), grav = m.Mat(SurfaceKind.Gravel);
                int courses = c.Did("course3") ? 4 : c.Did("course2") ? 3 : c.Did("base-course") ? 1 : 0;
                if (c.Did("trench")) m.Box(new Vector3(0, -0.01f, -0.1f), new Vector3(2.5f, 0.02f, 0.45f), grav);
                for (int r = 0; r < courses; r++)
                    for (int i = 0; i < 6; i++)
                        m.Box(new Vector3(-1.0f + i * 0.41f + (r % 2) * 0.2f, 0.1f + r * 0.2f, -0.15f - r * 0.02f), new Vector3(0.4f, 0.195f, 0.3f), blk);
                if (c.Did("cap")) m.Box(new Vector3(0.1f, 0.1f + courses * 0.2f, -0.2f), new Vector3(2.5f, 0.08f, 0.32f), m.Mat(SurfaceKind.Concrete));
                if (c.Did("backfill")) m.Box(new Vector3(0, courses * 0.1f, -0.9f), new Vector3(2.5f, courses * 0.2f, 1.2f), m.Mat(SurfaceKind.Dirt));
                if (courses == 0) m.Box(new Vector3(0, 0.01f, -0.1f), new Vector3(2.5f, 0.02f, 0.05f), m.Mat(new Color(1f, 0.3f, 0.6f)));   // paint line
            });

            Register("pergola", (m, c) =>
            {
                // 10' x 12' freestanding cedar pergola: origin at grade at its centre
                int ced = m.Mat(SurfaceKind.Oak, new Color(0.9f, 0.7f, 0.55f)), con = m.Mat(SurfaceKind.Concrete);
                float hx = 1.5f, hz = 1.8f, H = 2.44f;
                foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                {
                    if (c.Did("footings")) m.Cyl(new Vector3(sx * hx, 0.02f, sz * hz), 0.15f, 0.04f, con, 14);
                    if (c.Did("posts")) m.Box(new Vector3(sx * hx, H / 2f, sz * hz), new Vector3(0.14f, H, 0.14f), ced);
                }
                if (c.Did("beams")) foreach (int sx in new[] { -1, 1 }) m.Box(new Vector3(sx * hx, H + 0.1f, 0), new Vector3(0.09f, 0.24f, hz * 2f + 0.6f), ced);
                if (c.Did("rafters")) for (int i = 0; i < 9; i++) m.Box(new Vector3(0, H + 0.3f, -hz - 0.2f + i * (hz * 2f + 0.4f) / 8f), new Vector3(hx * 2f + 0.6f, 0.19f, 0.045f), ced);
                if (!c.Did("footings")) foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) m.Box(new Vector3(sx * hx, 0.15f, sz * hz), new Vector3(0.02f, 0.3f, 0.02f), m.Mat(new Color(1f, 0.3f, 0.6f)));
            });

            Register("groutwall", (m, c) =>
            {
                // TubCaulk frame: +X along the joint, +Z into the room, origin at the tub rim; tile above
                int tile = m.Mat(SurfaceKind.Porcelain), grout = m.Mat(c.Did("grout") ? new Color(0.85f, 0.85f, 0.82f) : c.Did("rake") ? new Color(0.35f, 0.35f, 0.33f) : new Color(0.5f, 0.45f, 0.35f));
                float t = 0.152f;
                for (int r = 0; r < 6; r++) for (int i = 0; i < 6; i++)
                    m.Box(new Vector3(-0.45f + i * t, 0.08f + r * t, 0.006f), new Vector3(t - 0.004f, t - 0.004f, 0.006f), tile);
                m.Box(new Vector3(0, 0.46f, 0.002f), new Vector3(6 * t, 6 * t, 0.003f), grout);
                if (c.Bad && !c.Did("rake") && c.CauseId == "leak") m.Box(new Vector3(0, 0.0f, 0.01f), new Vector3(0.6f, 0.012f, 0.012f), m.Mat(new Color(0.2f, 0.18f, 0.12f)));
            });
        }
    }
}
