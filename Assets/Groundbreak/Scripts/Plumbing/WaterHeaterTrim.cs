using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Installation detail on water heaters (the tank model already has the T&amp;P valve + discharge, gas valve and drip leg):
    /// brass drain valve, dielectric unions on both nipples, a full-port ball valve with a lever on the cold inlet once it is piped,
    /// a gas shutoff (yellow lever) + ground-joint union on the gas drop, an armored (MC) whip from the electric heater's junction box,
    /// and tankless isolation (service) valves with purge ports. Rebuilt on graph changes as the "WhTrim" child.</summary>
    public static class WaterHeaterTrim
    {
        static float I(float x) => Units.In(x);
        static readonly Dictionary<NetNode, int> _built = new Dictionary<NetNode, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _built.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => NetGraph.Changed += Apply;

        public static void Apply()
        {
            var dead = new List<NetNode>();
            foreach (var kv in _built) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var d in dead) _built.Remove(d);
            foreach (var n in NetGraph.NodesOf(Trade.Plumbing))
            {
                if (n == null || n.Type != "water-heater" || n.Piece == null || n.Piece.Def == null) continue;
                int h = 1;
                foreach (var p in n.Ports) h = h * 3 + (p.Run != null ? 1 : 0) + (p.Joined ? 1 : 0);
                if (_built.TryGetValue(n, out int old) && old == h && n.transform.Find("WhTrim") != null) continue;
                _built[n] = h;
                Build(n);
            }
        }

        public static void Build(NetNode n)
        {
            var t = n.transform;
            for (int i = t.childCount - 1; i >= 0; i--) if (t.GetChild(i).name == "WhTrim") Object.Destroy(t.GetChild(i).gameObject);
            var mm = new MepMesh { Origin = t.position };
            var mats = new List<Material>
            {
                Materials.Get(SurfaceKind.Brass), Materials.Get(SurfaceKind.PaintedSteel, new Color(1.0f, 0.82f, 0.1f)),
                Materials.Get(SurfaceKind.PaintedSteel, new Color(0.85f, 0.15f, 0.12f)), Materials.Get(SurfaceKind.PaintedSteel, new Color(0.2f, 0.35f, 0.9f)),
                Materials.Get(SurfaceKind.MatteBlack), Materials.Get(SurfaceKind.Galvanized), Materials.Get(SurfaceKind.Nickel),
            };
            const int Br = 0, Yel = 1, Red = 2, Blu = 3, Blk = 4, Galv = 5, Ni = 6;
            Vector3 W(Vector3 l) => t.TransformPoint(l);
            void Tube(Vector3 a, Vector3 b, float r, int sub, int seg = 14) => mm.Band(W(a), W(b), MepMesh.Circle(r, seg), sub);
            void Lever(Vector3 at, Vector3 dir, int sub) { Vector3 e = at + dir.normalized * I(3.5f); mm.Sweep(new[] { W(at), W(e) }, MepMesh.Rect(I(0.45f), I(0.12f)), sub, true, true, t.up); }

            string id = n.Piece.Def.Id;
            var s = n.Piece.Size; float hy = s.y / 2, y0 = -hy;
            bool tankless = id.Contains("tankless"), gas = id.Contains("gas");
            NetPort cold = n.Port("cold in"), hot = n.Port("hot out");

            if (tankless)
            {
                // isolation valve kit under the unit: ball valve with a purge port on each side
                foreach (var (port, handle) in new[] { (cold, Blu), (hot, Red) })
                {
                    if (port == null) continue;
                    Vector3 p = port.LocalPos;
                    Tube(p + Vector3.down * I(0.2f), p + Vector3.down * I(3.2f), I(0.62f), Br);                      // body
                    Tube(p + Vector3.down * I(0.5f), p + Vector3.down * I(0.9f), I(0.75f), Br, 6);                  // hex union nut
                    Tube(p + Vector3.down * I(1.8f), p + Vector3.down * I(1.8f) + Vector3.forward * I(1.3f), I(0.35f), Br); // purge port
                    Tube(p + Vector3.down * I(1.8f) + Vector3.forward * I(1.3f), p + Vector3.down * I(1.8f) + Vector3.forward * I(1.6f), I(0.42f), Ni, 6); // cap
                    Tube(p + Vector3.down * I(2.6f), p + Vector3.down * I(2.6f) + Vector3.left * I(0.8f), I(0.15f), Br);  // stem
                    Lever(p + Vector3.down * I(2.6f) + Vector3.left * I(0.8f), Vector3.forward, handle);
                }
            }
            else
            {
                // dielectric unions on both nipples (hex nut + steel half)
                foreach (var port in new[] { cold, hot })
                {
                    if (port == null) continue;
                    Vector3 p = port.LocalPos;
                    Tube(p + Vector3.up * I(0.2f), p + Vector3.up * I(0.9f), I(0.62f), Galv);
                    Tube(p + Vector3.up * I(0.9f), p + Vector3.up * I(1.6f), I(0.78f), Br, 6);
                }
                // cold-side full-port ball valve once the inlet is piped (the shutoff that isolates the heater)
                if (cold != null && cold.Run != null)
                {
                    Vector3 v = cold.LocalPos + Vector3.up * I(5f);
                    Tube(v - Vector3.up * I(1.3f), v + Vector3.up * I(1.3f), I(0.48f), Br, 6);   // threaded/sweat ends
                    Tube(v - Vector3.up * I(0.8f), v + Vector3.up * I(0.8f), I(0.72f), Br);       // ball housing
                    Tube(v, v + Vector3.forward * I(0.9f), I(0.2f), Br);                          // stem
                    Lever(v + Vector3.forward * I(0.9f), Vector3.left, Yel);                       // lever in line with the pipe = open
                    var l = v + Vector3.forward * I(0.9f) + Vector3.left * I(3.5f);
                    mm.Band(W(l), W(l + Vector3.forward * I(0.15f)), MepMesh.Circle(I(0.3f), 10), Yel);
                }
                // brass drain valve near the bottom of the tank, hose thread facing out
                float R = Mathf.Min(s.x, s.z) / 2 - I(0.4f);
                Vector3 dv = new Vector3(0, y0 + I(4f), R);
                Tube(dv - Vector3.forward * I(0.2f), dv + Vector3.forward * I(1.4f), I(0.42f), Br);
                Tube(dv + Vector3.forward * I(1.4f), dv + Vector3.forward * I(2.2f), I(0.5f), Br);
                Tube(dv + Vector3.forward * I(0.6f), dv + Vector3.forward * I(0.6f) + Vector3.up * I(0.9f), I(0.18f), Br);
                Tube(dv + Vector3.forward * I(0.6f) + Vector3.up * I(0.9f), dv + Vector3.forward * I(0.6f) + Vector3.up * I(1.1f), I(0.45f), Blk, 10);

                var diag = Quaternion.Euler(0f, 45f, 0f);
                if (gas)
                {
                    // on the black-iron gas drop: gas cock with a yellow lever, ground-joint union below it
                    Vector3 drop = diag * new Vector3(I(2.2f), 0, R + I(1.5f));
                    Vector3 gv = drop + Vector3.up * (y0 + I(26f));
                    Tube(gv - Vector3.up * I(1.1f), gv + Vector3.up * I(1.1f), I(0.6f), Br, 8);
                    Tube(gv, gv + diag * Vector3.forward * I(0.8f), I(0.18f), Br);
                    Lever(gv + diag * Vector3.forward * I(0.8f), Vector3.up, Yel);                  // in line with the pipe = open
                    Vector3 un = drop + Vector3.up * (y0 + I(20f));
                    Tube(un - Vector3.up * I(0.5f), un + Vector3.up * I(0.5f), I(0.78f), Blk, 6);
                    Tube(un - Vector3.up * I(0.9f), un - Vector3.up * I(0.5f), I(0.62f), Blk, 8);
                }
                else
                {
                    // armored (MC) whip from the junction box up to the ceiling box, with a connector at the heater
                    Vector3 jb = new Vector3(0, hy - I(3f) + I(1.5f), I(4.25f));
                    Tube(jb, jb + Vector3.up * I(0.6f), I(0.45f), Galv, 6);
                    var pts = new List<Vector3> { W(jb + Vector3.up * I(0.6f)), W(jb + Vector3.up * I(6f)), W(jb + new Vector3(0, I(12f), I(4f))), W(jb + new Vector3(0, I(36f), I(6f))) };
                    var path = MepMesh.RoundCorners(pts, I(4f), 6);
                    mm.Sweep(path, MepMesh.Circle(I(0.32f), 10), Galv);
                    // spiral interlock bumps every 1/2"
                    float len = 0; for (int i = 1; i < path.Count; i++) len += Vector3.Distance(path[i - 1], path[i]);
                    for (float u = I(0.3f); u < len - I(0.3f); u += I(0.5f))
                    {
                        Vector3 a = At(path, u, out Vector3 dir);
                        mm.Band(a - dir * I(0.06f), a + dir * I(0.06f), MepMesh.Circle(I(0.35f), 10), Galv);
                    }
                }
            }
            mm.Spawn(t, "WhTrim", mats.ToArray());
        }

        static Vector3 At(List<Vector3> path, float u, out Vector3 dir)
        {
            for (int i = 1; i < path.Count; i++)
            {
                float l = Vector3.Distance(path[i - 1], path[i]);
                dir = (path[i] - path[i - 1]).normalized;
                if (u <= l || i == path.Count - 1) return path[i - 1] + dir * Mathf.Min(u, l);
                u -= l;
            }
            dir = Vector3.up; return path[0];
        }
    }
}
