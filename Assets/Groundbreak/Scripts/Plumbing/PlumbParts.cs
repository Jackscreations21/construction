using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Plumbing hookups on catalog fixtures (water heaters, toilet, vanity, sinks, tub, shower) at their real rough-in
    /// locations, plus the plumbing parts' nodes and real-size models (PEX manifold, main shutoff, P-trap, cleanout, sillcock,
    /// closet flange, vent-through-roof boot, test gauge). Fixture supply ports adapt their join method to the pipe that lands
    /// on them (crimp for PEX, sweat for copper), the way a plumber picks the matching adapter.</summary>
    public static class PlumbParts
    {
        static float I(float inches) => Units.In(inches);

        /// <summary>Fixture ports whose join method follows the pipe that lands on them.</summary>
        static readonly HashSet<NetPort> _adaptive = new HashSet<NetPort>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _adaptive.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            MepAutoAttach.Register("wh-gas50", WaterHeater);
            MepAutoAttach.Register("wh-elec50", WaterHeater);
            MepAutoAttach.Register("wh-tankless", Tankless);
            MepAutoAttach.Register("pl-toilet", Toilet);
            MepAutoAttach.Register("pl-vanity36", Vanity);
            MepAutoAttach.Register("pl-kitchen-sink", KitchenSink);
            MepAutoAttach.Register("pl-tub60", Tub);
            MepAutoAttach.Register("pl-shower48", Shower);
            MepAutoAttach.Register(PlumbCatalog.Manifold, Manifold);
            MepAutoAttach.Register(PlumbCatalog.Shutoff, Shutoff);
            MepAutoAttach.Register(PlumbCatalog.PTrap, PTrap);
            MepAutoAttach.Register(PlumbCatalog.Cleanout, Cleanout);
            MepAutoAttach.Register(PlumbCatalog.HoseBib, HoseBib);
            MepAutoAttach.Register(PlumbCatalog.ClosetFlange, ClosetFlange);
            MepAutoAttach.Register(PlumbCatalog.VentBoot, VentBoot);
            MepAutoAttach.Register(PlumbCatalog.Gauge, Gauge);
            NetGraph.Changed += OnGraphChanged;
        }

        /// <summary>Adapter logic: a supply stub takes the join of the pipe landing on it (PEX crimp / copper sweat).</summary>
        static void OnGraphChanged()
        {
            _adaptive.RemoveWhere(p => p.Node == null);
            foreach (var p in _adaptive)
            {
                if (p.Run == null || p.Joined || p.Run.Spec == null) continue;
                if (p.Join != p.Run.Spec.Join)
                {
                    p.Join = p.Run.Spec.Join;
                    if (p.Join == JoinMethod.None) p.Joined = true;
                }
            }
        }

        // ============================================================ node helpers

        static NetNode Node(Piece p, string type)
        {
            var n = p.gameObject.AddComponent<NetNode>();
            n.Type = type; n.Trade = Trade.Plumbing; n.Piece = p;
            return n;
        }

        static NetPort Sup(NetNode n, string label, Vector3 lp, Vector3 ld, float nom, bool adaptive = true)
        {
            var port = n.AddPort(label, lp, ld, NetKind.Supply, nom, JoinMethod.Crimp);
            if (adaptive) _adaptive.Add(port);
            return port;
        }

        static NetPort Drn(NetNode n, string label, Vector3 lp, Vector3 ld, float nom) =>
            n.AddPort(label, lp, ld, NetKind.Drain, nom, JoinMethod.Solvent);

        public static bool IsFixture(NetNode n) => n != null && n.Trade == Trade.Plumbing && (n.Type.StartsWith("fixture-") || n.Type == "water-heater");

        public static IEnumerable<NetNode> Fixtures()
        {
            foreach (var n in NetGraph.Nodes) if (IsFixture(n)) yield return n;
        }

        /// <summary>World-space mesh helper for a piece: all points given in the piece's local frame.</summary>
        sealed class Draw
        {
            public readonly MepMesh M;
            readonly Transform t;
            readonly List<Material> mats = new List<Material>();
            public Draw(Transform tr) { t = tr; M = new MepMesh { Origin = tr.position }; }
            public int Mat(Material m) { int i = mats.IndexOf(m); if (i < 0) { mats.Add(m); i = mats.Count - 1; } return i; }
            public int Mat(SurfaceKind k) => Mat(MepLook.Get(k));
            public int Mat(SurfaceKind k, Color c) => Mat(MepLook.Get(k, c));
            public Vector3 W(Vector3 local) => t.TransformPoint(local);
            public Vector3 D(Vector3 local) => t.TransformDirection(local);
            public void Tube(Vector3 a, Vector3 b, float r, int sub, bool caps = true, int seg = 16) => M.Band(W(a), W(b), MepMesh.Circle(r, seg), sub, caps);
            public void Path(IList<Vector3> localPts, float r, float bend, int sub)
            {
                var w = new List<Vector3>(); foreach (var p in localPts) w.Add(W(p));
                M.Sweep(MepMesh.RoundCorners(w, bend, 8), MepMesh.Circle(r, 16), sub);
            }
            /// <summary>Rectangular bar from a to b (local), w = side width, h = height (frames keep h toward world up).</summary>
            public void Bar(Vector3 a, Vector3 b, float w, float h, int sub) => M.Band(W(a), W(b), MepMesh.Rect(w, h), sub);
            public GameObject Spawn(string name) => M.Spawn(t, name, mats.Count > 0 ? mats.ToArray() : new[] { MepLook.Get(SurfaceKind.PaintedWhite) });
        }

        static void AddTrim(Piece p, Draw d)
        {
            var old = p.transform.Find("PlumbTrim");
            if (old != null) Object.Destroy(old.gameObject);
            d.Spawn("PlumbTrim");
        }

        static void Replace(Piece p, Draw d) => MepAutoAttach.ReplaceVisual(p, d.Spawn("Visual"));

        /// <summary>Angle stop (chrome) + braided riser stub at a fixture supply port pointing out of the wall.</summary>
        static void AngleStop(Draw d, Vector3 port, Vector3 outDir, float riserTop, int chrome, int handle)
        {
            Vector3 body = port + outDir * I(1.4f);
            d.Tube(port + outDir * I(0.1f), body, I(0.32f), chrome);                                  // stub-out nipple
            d.Tube(body - Vector3.up * I(0.4f), body + Vector3.up * I(0.5f), I(0.42f), chrome);       // angle stop body
            d.Tube(body + outDir * I(0.3f), body + outDir * I(1.0f), I(0.18f), chrome);               // stem
            d.Bar(body + outDir * I(1.0f), body + outDir * I(1.25f), I(1.2f), I(0.5f), handle);       // oval handle
            if (riserTop > body.y + I(1f))
                d.Tube(body + Vector3.up * I(0.5f), new Vector3(body.x, riserTop, body.z), I(0.19f), chrome, true, 8); // braided riser
        }

        // ============================================================ water heaters

        static void WaterHeater(Piece p)
        {
            var n = Node(p, "water-heater");
            float hy = p.Size.y / 2;
            Sup(n, "cold in", new Vector3(-I(4.5f), hy, 0), Vector3.up, 0.75f);
            Sup(n, "hot out", new Vector3(I(4.5f), hy, 0), Vector3.up, 0.75f);
            // dielectric nipples' threaded tops + shutoff on the cold side
            var d = new Draw(p.transform);
            int brass = d.Mat(SurfaceKind.Brass);
            d.Tube(new Vector3(-I(4.5f), hy - I(0.3f), 0), new Vector3(-I(4.5f), hy, 0), I(0.62f), brass);
            d.Tube(new Vector3(I(4.5f), hy - I(0.3f), 0), new Vector3(I(4.5f), hy, 0), I(0.62f), brass);
            AddTrim(p, d);
        }

        static void Tankless(Piece p)
        {
            var n = Node(p, "water-heater");
            float hy = p.Size.y / 2;
            Sup(n, "cold in", new Vector3(0, -hy, 0), Vector3.down, 0.75f);
            Sup(n, "hot out", new Vector3(I(3.5f), -hy, 0), Vector3.down, 0.75f);
        }

        // ============================================================ fixtures

        static void Toilet(Piece p)
        {
            var n = Node(p, "fixture-toilet");
            Vector3 h = p.Size * 0.5f;
            // 12" rough-in: flange center 12" from the wall behind the tank; supply 6" left of center, ~7" AFF
            var cold = Sup(n, "cold", new Vector3(-I(6f), -h.y + I(7f), -h.z + I(2f)), Vector3.back, 0.5f);
            Drn(n, "flange", new Vector3(0, -h.y, -h.z + I(12f)), Vector3.down, 3f);
            var d = new Draw(p.transform);
            int chrome = d.Mat(SurfaceKind.Nickel), white = d.Mat(SurfaceKind.PaintedWhite, MepLook.PvcWhite);
            AngleStop(d, cold.LocalPos, Vector3.forward, -h.y + I(14f), chrome, white);
            // closet flange ring just visible around the bowl's foot
            d.Tube(new Vector3(0, -h.y + I(0.05f), -h.z + I(12f)), new Vector3(0, -h.y + I(0.3f), -h.z + I(12f)), I(3.5f), white, true, 24);
            AddTrim(p, d);
        }

        static void Vanity(Piece p)
        {
            var n = Node(p, "fixture-vanity");
            Vector3 h = p.Size * 0.5f;
            // hot on the LEFT as you face the fixture (+X local when facing -Z), 21" AFF, 4" off center; trap arm 18" AFF
            var hot = Sup(n, "hot", new Vector3(I(4f), -h.y + I(21f), -h.z + I(2f)), Vector3.back, 0.5f);
            var cold = Sup(n, "cold", new Vector3(-I(4f), -h.y + I(21f), -h.z + I(2f)), Vector3.back, 0.5f);
            var arm = Drn(n, "trap arm", new Vector3(0, -h.y + I(18f), -h.z + I(3f)), Vector3.back, 1.5f);
            var d = new Draw(p.transform);
            int chrome = d.Mat(SurfaceKind.Nickel), white = d.Mat(SurfaceKind.PaintedWhite, MepLook.PvcWhite);
            float counter = h.y - I(5f);
            AngleStop(d, hot.LocalPos, Vector3.forward, counter, chrome, white);
            AngleStop(d, cold.LocalPos, Vector3.forward, counter, chrome, white);
            // 1-1/4" tailpiece + P-trap from the basin drain back to the trap arm
            float basinBottom = h.y - I(4f) - I(6.2f);
            var trap = new List<Vector3>
            {
                new Vector3(0, basinBottom, I(1f)), new Vector3(0, arm.LocalPos.y - I(4.5f), I(1f)),
                new Vector3(0, arm.LocalPos.y - I(4.5f), -I(2.5f)), new Vector3(0, arm.LocalPos.y, -I(2.5f)), arm.LocalPos + Vector3.forward * I(0.6f),
            };
            d.Path(trap, I(0.75f), I(1.6f), white);
            AddTrim(p, d);
        }

        static void KitchenSink(Piece p)
        {
            var n = Node(p, "fixture-kitchen-sink");
            Vector3 h = p.Size * 0.5f;
            float floor = h.y - I(36f); // rim at 36" counter height
            var hot = Sup(n, "hot", new Vector3(I(4f), floor + I(21f), -h.z + I(2f)), Vector3.back, 0.5f);
            var cold = Sup(n, "cold", new Vector3(-I(4f), floor + I(21f), -h.z + I(2f)), Vector3.back, 0.5f);
            Drn(n, "trap arm", new Vector3(0, floor + I(18f), -h.z + I(3f)), Vector3.back, 1.5f);
            var d = new Draw(p.transform);
            int chrome = d.Mat(SurfaceKind.Nickel), white = d.Mat(SurfaceKind.PaintedWhite, MepLook.PvcWhite);
            AngleStop(d, hot.LocalPos, Vector3.forward, -h.y, chrome, white);
            AngleStop(d, cold.LocalPos, Vector3.forward, -h.y, chrome, white);
            AddTrim(p, d);
        }

        /// <summary>Pressure-balance mixing valve in the wall behind a tub/shower: hot inlet on the left, cold on the right,
        /// riser to the shower head and drop to the tub spout.</summary>
        static void MixingValve(Piece p, NetNode n, Vector3 v, Vector3 left, Vector3 outOfWall, float headY, float spoutY)
        {
            Sup(n, "hot", v + left * I(2f), left, 0.5f);
            Sup(n, "cold", v - left * I(2f), -left, 0.5f);
            var d = new Draw(p.transform);
            int brass = d.Mat(SurfaceKind.Brass), cu = d.Mat(SurfaceKind.Brass, MepLook.Copper), chrome = d.Mat(SurfaceKind.Nickel);
            d.Tube(v - Vector3.up * I(1.5f), v + Vector3.up * I(1.5f), I(1.1f), brass);           // valve body
            d.Tube(v - left * I(2f), v + left * I(2f), I(0.45f), brass);                            // inlet bosses
            d.Tube(v, v + outOfWall * I(2.5f), I(0.9f), d.Mat(SurfaceKind.DarkPlastic));           // plaster guard / cartridge
            d.Tube(v + Vector3.up * I(1.5f), new Vector3(v.x, headY, v.z), I(0.3125f), cu);         // shower riser
            d.Tube(new Vector3(v.x, headY, v.z), new Vector3(v.x, headY, v.z) + outOfWall * I(1.6f), I(0.45f), brass); // drop-ear elbow
            if (spoutY < v.y)
            {
                d.Tube(v - Vector3.up * I(1.5f), new Vector3(v.x, spoutY, v.z), I(0.3125f), cu);
                d.Tube(new Vector3(v.x, spoutY, v.z), new Vector3(v.x, spoutY, v.z) + outOfWall * I(3f), I(0.3125f), cu);
                d.Tube(new Vector3(v.x, spoutY, v.z) + outOfWall * I(2.6f), new Vector3(v.x, spoutY, v.z) + outOfWall * I(3.4f), I(0.5f), chrome);
            }
            AddTrim(p, d);
        }

        static void Tub(Piece p)
        {
            var n = Node(p, "fixture-tub");
            Vector3 h = p.Size * 0.5f;
            float zc = (I(3f) - I(4f)) * 0.5f;         // tub well center (back wall 3", apron 4")
            float endW = I(3.5f);
            // valve in the end wall at the drain end (-X), 28" AFF; facing that wall, left = -Z local
            var v = new Vector3(-h.x - I(1.875f), -h.y + I(28f), zc);
            Drn(n, "drain", new Vector3(-h.x + endW + I(8f), -h.y, zc), Vector3.down, 1.5f);
            MixingValve(p, n, v, Vector3.back, Vector3.right, -h.y + I(78f), -h.y + I(20f));
        }

        static void Shower(Piece p)
        {
            var n = Node(p, "fixture-shower");
            Vector3 h = p.Size * 0.5f;
            float zc = (I(2f) - I(3f)) * 0.5f;
            var v = new Vector3(0, -h.y + I(48f), -h.z - I(1.875f));
            Drn(n, "drain", new Vector3(0, -h.y, zc), Vector3.down, 2f);
            MixingValve(p, n, v, Vector3.right, Vector3.forward, -h.y + I(78f), v.y + 1f);
        }

        // ============================================================ parts

        static readonly Color HandleBlue = new Color(0.15f, 0.3f, 0.9f), HandleRed = new Color(0.9f, 0.15f, 0.12f);

        /// <summary>Copper PEX manifold: 1" trunk along X, 3/4" inlet on +X, four 1/2" outlets pointing down, each with a
        /// quarter-turn valve (closed = capped until a line is crimped on).</summary>
        static void Manifold(Piece p)
        {
            var n = Node(p, "manifold");
            n.DisplayName = "PEX manifold";
            n.AddPort("in", new Vector3(I(6f), I(0.5f), 0), Vector3.right, NetKind.Supply, 0.75f, JoinMethod.Crimp);
            for (int i = 0; i < 4; i++)
            {
                var o = n.AddPort("out " + (i + 1), new Vector3(I(-3f + 2f * i), -I(1.9f), 0), Vector3.down, NetKind.Supply, 0.5f, JoinMethod.Crimp);
                o.Capped = true; // valve closed
            }
            BuildManifold(p, n, false);
        }

        public static void BuildManifold(Piece p, NetNode n, bool hot)
        {
            var d = new Draw(p.transform);
            int cu = d.Mat(SurfaceKind.Brass, MepLook.Copper), brass = d.Mat(SurfaceKind.Brass), plas = d.Mat(SurfaceKind.DarkPlastic);
            int handle = d.Mat(SurfaceKind.PaintedSteel, hot ? HandleRed : HandleBlue);
            float y = I(0.5f);
            d.Tube(new Vector3(-I(5.5f), y, 0), new Vector3(I(5f), y, 0), I(0.56f), cu);
            d.Tube(new Vector3(-I(5.9f), y, 0), new Vector3(-I(5.4f), y, 0), I(0.62f), cu);              // end cap
            d.Tube(new Vector3(I(4.9f), y, 0), new Vector3(I(5.5f), y, 0), I(0.62f), cu);                 // reducing adapter
            d.Tube(new Vector3(I(5.5f), y, 0), new Vector3(I(6f), y, 0), I(0.40f), brass);                // 3/4" PEX barb
            for (int i = 0; i < 4; i++)
            {
                float x = I(-3f + 2f * i);
                d.Tube(new Vector3(x, y, 0), new Vector3(x, -I(1.3f), 0), I(0.3f), brass);
                d.Tube(new Vector3(x, -I(0.2f), 0), new Vector3(x, -I(0.95f), 0), I(0.5f), brass);         // ball valve body
                d.Bar(new Vector3(x, -I(0.55f), I(0.45f)), new Vector3(x, -I(0.55f), I(1.6f)), I(0.45f), I(0.18f), handle);
                d.Tube(new Vector3(x, -I(1.3f), 0), new Vector3(x, -I(1.9f), 0), I(0.27f), brass);          // 1/2" barb
                var port = n.Ports[1 + i];
                if (port.Run == null && port.Capped) d.Tube(new Vector3(x, -I(1.75f), 0), new Vector3(x, -I(2.1f), 0), I(0.36f), d.Mat(SurfaceKind.PaintedWhite, new Color(1f, 0.5f, 0.1f)));
            }
            // mounting brackets back to the wall (-Z)
            d.Bar(new Vector3(-I(4.5f), y, -I(1.5f)), new Vector3(-I(4.5f), y, I(0.2f)), I(0.6f), I(1.6f), plas);
            d.Bar(new Vector3(I(4f), y, -I(1.5f)), new Vector3(I(4f), y, I(0.2f)), I(0.6f), I(1.6f), plas);
            Replace(p, d);
        }

        /// <summary>3/4" full-port brass ball valve along local Y with a red lever (main shutoff).</summary>
        static void Shutoff(Piece p)
        {
            var n = Node(p, "valve");
            n.DisplayName = "Main shutoff valve";
            n.AddPort("in", new Vector3(0, -I(2.25f), 0), Vector3.down, NetKind.Supply, 0.75f, JoinMethod.Crimp);
            n.AddPort("out", new Vector3(0, I(2.25f), 0), Vector3.up, NetKind.Supply, 0.75f, JoinMethod.Crimp);
            var d = new Draw(p.transform);
            int brass = d.Mat(SurfaceKind.Brass), red = d.Mat(SurfaceKind.PaintedSteel, HandleRed), steel = d.Mat(SurfaceKind.BrushedSteel);
            d.Tube(new Vector3(0, -I(2.25f), 0), new Vector3(0, I(2.25f), 0), I(0.42f), brass);
            d.Tube(new Vector3(0, -I(1.5f), 0), new Vector3(0, I(1.5f), 0), I(0.55f), brass, true, 6);   // hex ends
            d.Tube(new Vector3(0, -I(0.8f), 0), new Vector3(0, I(0.8f), 0), I(0.75f), brass, true, 20);  // ball housing
            d.Tube(new Vector3(0, 0, I(0.7f)), new Vector3(0, 0, I(1.2f)), I(0.2f), steel);              // stem
            d.Bar(new Vector3(0, 0, I(1.25f)), new Vector3(0, I(3.6f), I(1.25f)), I(0.6f), I(0.2f), red); // lever (open = in line)
            Replace(p, d);
        }

        /// <summary>1-1/2" PVC P-trap: inlet up (from the tub shoe / tailpiece), outlet (trap arm) toward -Z.</summary>
        static void PTrap(Piece p)
        {
            var n = Node(p, "ptrap");
            n.DisplayName = "P-trap 1-1/2\"";
            var inlet = new Vector3(0, I(3.6f), I(2.5f));
            var outlet = new Vector3(0, I(1.0f), -I(4.5f));
            Drn(n, "inlet", inlet, Vector3.up, 1.5f);
            Drn(n, "outlet", outlet, Vector3.back, 1.5f);
            var d = new Draw(p.transform);
            int white = d.Mat(SurfaceKind.PaintedWhite, MepLook.PvcWhite), ink = d.Mat(SurfaceKind.DarkPlastic);
            float r = I(0.95f);
            d.Path(new List<Vector3> { inlet - Vector3.up * I(1.1f), new Vector3(0, -I(2.8f), I(2.5f)), new Vector3(0, -I(2.8f), -I(1f)), new Vector3(0, I(1f), -I(1f)), outlet + Vector3.forward * I(1.2f) }, r, I(1.75f), white);
            d.Tube(inlet - Vector3.up * I(1.2f), inlet, r * 1.13f, white);       // inlet hub
            d.Tube(outlet + Vector3.forward * I(1.3f), outlet, r * 1.13f, white); // outlet hub
            d.Bar(new Vector3(I(0.96f), -I(1.4f), I(1f)), new Vector3(I(0.96f), -I(1.4f), I(1.8f)), I(0.02f), I(0.3f), ink); // print line
            Replace(p, d);
        }

        /// <summary>Cleanout adapter (3") with a threaded plug and square nut; the drain lands on the -Z socket.</summary>
        static void Cleanout(Piece p)
        {
            var n = Node(p, "cleanout");
            n.DisplayName = "Cleanout 3\"";
            Drn(n, "drain", new Vector3(0, 0, -I(3f)), Vector3.back, 3f);
            var d = new Draw(p.transform);
            int white = d.Mat(SurfaceKind.PaintedWhite, MepLook.PvcWhite), plug = d.Mat(SurfaceKind.PaintedWhite, new Color(0.92f, 0.9f, 0.85f));
            d.Tube(new Vector3(0, 0, -I(3f)), new Vector3(0, 0, I(1.4f)), I(1.98f), white, true, 24);
            d.Tube(new Vector3(0, 0, I(1.4f)), new Vector3(0, 0, I(2.1f)), I(1.82f), plug, true, 24);
            for (int i = 0; i < 6; i++) // thread ridges
                d.Tube(new Vector3(0, 0, I(1.45f + i * 0.1f)), new Vector3(0, 0, I(1.5f + i * 0.1f)), I(1.86f), plug, true, 24);
            d.Bar(new Vector3(0, 0, I(2.1f)), new Vector3(0, 0, I(2.9f)), I(1.1f), I(1.1f), plug);   // square drive nut
            Replace(p, d);
        }

        /// <summary>Frost-free sillcock: faucet head on the outside (+Z), long stem through the rim joist, threaded inlet at -Z.</summary>
        static void HoseBib(Piece p)
        {
            var n = Node(p, "hosebib");
            n.DisplayName = "Hose bib (sillcock)";
            var port = Sup(n, "inlet", new Vector3(0, 0, -I(5f)), Vector3.back, 0.5f, false);
            port.Join = JoinMethod.Thread;
            var d = new Draw(p.transform);
            int brass = d.Mat(SurfaceKind.Brass), cu = d.Mat(SurfaceKind.Brass, MepLook.Copper), red = d.Mat(SurfaceKind.PaintedSteel, HandleRed);
            d.Tube(new Vector3(0, 0, -I(5f)), new Vector3(0, 0, -I(4.2f)), I(0.5f), brass, true, 6);
            d.Tube(new Vector3(0, 0, -I(4.2f)), new Vector3(0, 0, I(2.8f)), I(0.36f), cu);
            d.Bar(new Vector3(0, 0, I(2.8f)), new Vector3(0, 0, I(3.05f)), I(2.6f), I(2.6f), brass);       // wall flange
            d.Tube(new Vector3(0, 0, I(3f)), new Vector3(0, 0, I(4.2f)), I(0.7f), brass);                  // head
            d.Tube(new Vector3(0, 0, I(3.6f)), new Vector3(0, -I(1.6f), I(3.6f)), I(0.42f), brass);         // spout
            d.Tube(new Vector3(0, -I(1.3f), I(3.6f)), new Vector3(0, -I(1.9f), I(3.6f)), I(0.52f), brass);  // hose threads
            d.Tube(new Vector3(0, 0, I(4.2f)), new Vector3(0, 0, I(4.7f)), I(0.15f), brass);
            d.Tube(new Vector3(0, 0, I(4.7f)), new Vector3(0, 0, I(5f)), I(1.2f), red, true, 8);            // wheel handle
            Replace(p, d);
        }

        static void ClosetFlange(Piece p)
        {
            var n = Node(p, "closet-flange");
            n.DisplayName = "Closet flange 3\"";
            Drn(n, "drain", new Vector3(0, -I(1.5f), 0), Vector3.down, 3f);
            var top = n.AddPort("top (test cap)", new Vector3(0, I(1.5f), 0), Vector3.up, NetKind.Drain, 3f, JoinMethod.None);
            top.Capped = true;
            var d = new Draw(p.transform);
            int white = d.Mat(SurfaceKind.PaintedWhite, MepLook.PvcWhite), cap = d.Mat(SurfaceKind.PaintedWhite, new Color(1f, 0.55f, 0.15f)), steel = d.Mat(SurfaceKind.Galvanized);
            d.Tube(new Vector3(0, -I(1.5f), 0), new Vector3(0, I(1.2f), 0), I(1.98f), white, true, 24);
            d.Tube(new Vector3(0, I(1.15f), 0), new Vector3(0, I(1.45f), 0), I(3.5f), white, true, 32);
            d.Tube(new Vector3(0, I(1.2f), 0), new Vector3(0, I(1.38f), 0), I(3.3f), steel, true, 32);   // steel ring
            d.Tube(new Vector3(0, I(1.45f), 0), new Vector3(0, I(1.5f), 0), I(1.75f), cap, true, 24);     // knock-out test cap
            Replace(p, d);
        }

        /// <summary>Vent-through-roof: the vent pipe lands on the port 6" under the deck and the boot shows it continuing
        /// 12" above the roof through a galvanized flashing plate with a rubber collar (laid on the roof plane found below).</summary>
        static void VentBoot(Piece p)
        {
            var n = Node(p, "vent-roof");
            n.DisplayName = "Vent through roof";
            n.AddPort("vent", new Vector3(0, -I(6f), 0), Vector3.down, NetKind.Vent, 0f, JoinMethod.None);
            Physics.SyncTransforms();
            Vector3 normal = Vector3.up;
            foreach (var h in Physics.RaycastAll(p.transform.position + Vector3.up * 0.4f, Vector3.down, 0.8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var pc = h.collider.GetComponentInParent<Piece>();
                if (pc == null || pc == p || pc.Def == null || pc.Def.Category != PieceCategory.Sheet) continue;
                normal = h.normal; break;
            }
            BuildBoot(p, normal);
        }

        static void BuildBoot(Piece p, Vector3 normal)
        {
            var d = new Draw(p.transform);
            int white = d.Mat(SurfaceKind.PaintedWhite, MepLook.PvcWhite), galv = d.Mat(SurfaceKind.Galvanized), rubber = d.Mat(SurfaceKind.DarkPlastic);
            Vector3 c = p.transform.position;
            float r = I(1.1875f);
            d.M.Band(c - Vector3.up * I(6f), c + Vector3.up * I(12f), MepMesh.Circle(r, 20), white);
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, normal);
            if (down.sqrMagnitude < 1e-4f) down = Vector3.forward;
            down.Normalize();
            var rot = Quaternion.LookRotation(down, normal);
            d.M.Box(c + normal * I(0.1f), new Vector3(I(12f), I(0.06f), I(14f)), rot, galv);
            for (int i = 0; i < 4; i++) // stepped rubber cone
            {
                float y0 = I(0.2f + i * 0.9f), y1 = y0 + I(0.9f);
                d.M.Band(c + Vector3.up * y0, c + Vector3.up * y1, MepMesh.Circle(r + I(1.9f - i * 0.5f), 20), rubber);
            }
            Replace(p, d);
        }

        /// <summary>Test gauge assembly: threaded onto a stub (port at the bottom), dial facing +Z, side valve for the pump.</summary>
        static void Gauge(Piece p)
        {
            var n = Node(p, "test-gauge");
            n.DisplayName = "Test gauge";
            var port = n.AddPort("stub", new Vector3(0, -I(3f), 0), Vector3.down, NetKind.Supply, 0f, JoinMethod.Thread);
            var g = p.gameObject.AddComponent<PressureGauge>();
            g.Node = n;
            var d = new Draw(p.transform);
            int brass = d.Mat(SurfaceKind.Brass), blk = d.Mat(SurfaceKind.MatteBlack), face = d.Mat(SurfaceKind.PaintedWhite), red = d.Mat(SurfaceKind.PaintedSteel, HandleRed);
            d.Tube(new Vector3(0, -I(3f), 0), new Vector3(0, I(0.4f), 0), I(0.42f), brass, true, 6);
            d.Tube(new Vector3(0, -I(1.6f), 0), new Vector3(I(1.4f), -I(1.6f), 0), I(0.3f), brass);   // pump inlet valve
            d.Tube(new Vector3(I(1.1f), -I(1.6f), 0), new Vector3(I(1.5f), -I(1.6f), 0), I(0.38f), red, true, 8);
            d.Tube(new Vector3(0, I(1.6f), -I(0.45f)), new Vector3(0, I(1.6f), I(0.35f)), I(1.3f), blk, true, 28);  // case
            d.Tube(new Vector3(0, I(1.6f), I(0.35f)), new Vector3(0, I(1.6f), I(0.38f)), I(1.18f), face, true, 28); // dial
            for (int k = 0; k <= 8; k++) // tick marks every 20 psi, 0 at 7:30 clockwise to 160 at 4:30 (seen from the front)
            {
                float a = (-135f + k * 33.75f) * Mathf.Deg2Rad;
                var dir = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f);
                var ctr = new Vector3(0, I(1.6f), I(0.39f));
                d.Tube(ctr + dir * I(k % 2 == 0 ? 0.8f : 0.92f), ctr + dir * I(1.08f), I(0.035f), blk, true, 6);
            }
            Replace(p, d);
            // needle as its own child so it can turn with pressure
            var nd = new Draw(p.transform);
            int needle = nd.Mat(SurfaceKind.PaintedSteel, HandleRed);
            Vector3 pivot = p.transform.TransformPoint(new Vector3(0, I(1.6f), I(0.42f)));
            nd.M.Origin = pivot;
            nd.Bar(new Vector3(0, I(1.6f), I(0.42f)), new Vector3(0, I(2.55f), I(0.42f)) + Vector3.forward * 0.0001f, I(0.08f), I(0.03f), needle);
            g.Needle = nd.Spawn("Needle").transform;
            g.DialAxis = p.transform.forward;
        }

        // ============================================================ caps

        /// <summary>Puts a test cap/plug on an open port (closed = holds pressure / water).</summary>
        public static void CapPort(NetPort p)
        {
            if (p == null || p.Node == null || p.Run != null) return;
            p.Capped = true;
            var old = p.Node.transform.Find("TestCap " + p.Label);
            if (old != null) Object.Destroy(old.gameObject);
            float r = Mathf.Max(I(0.4f), I(p.NominalIn) * 0.75f);
            var mm = new MepMesh { Origin = p.WorldPos };
            mm.Band(p.WorldPos - p.WorldDir * I(0.2f), p.WorldPos + p.WorldDir * I(0.9f), MepMesh.Circle(r, 16), 0);
            mm.Spawn(p.Node.transform, "TestCap " + p.Label, new[] { MepLook.Get(SurfaceKind.PaintedWhite, new Color(1f, 0.5f, 0.1f)) });
            NetGraph.Touch();
        }

        public static void UncapPort(NetPort p)
        {
            if (p == null || p.Node == null) return;
            p.Capped = false;
            var old = p.Node.transform.Find("TestCap " + p.Label);
            if (old != null) Object.Destroy(old.gameObject);
            NetGraph.Touch();
        }
    }
}
