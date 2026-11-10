using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A graph node: fitting, box, panel, fixture hookup, register boot... Ports are its connection points.
    /// Often lives on a catalog Piece (fixture, box) or on its own GameObject (auto-inserted fittings).</summary>
    public class NetNode : MonoBehaviour
    {
        public string Type = "fitting";      // elbow90, tee, santee, wye, box-1g, panel, toilet, boot...
        public Trade Trade;
        public string DisplayName = "";
        public Piece Piece;                  // set when the node is a catalog piece
        public readonly List<NetPort> Ports = new List<NetPort>();
        GameObject _jointVis;

        public NetPort AddPort(string label, Vector3 localPos, Vector3 localDir, NetKind kind, float nominalIn, JoinMethod join = JoinMethod.None, int maxRuns = 1)
        {
            var p = new NetPort { Node = this, Label = label, LocalPos = localPos, LocalDir = localDir.normalized, Kind = kind, NominalIn = nominalIn, Join = join, MaxRuns = maxRuns };
            Ports.Add(p);
            return p;
        }

        public NetPort Port(string label) => Ports.Find(p => p.Label == label);

        void OnEnable() { if (!NetGraph.Nodes.Contains(this)) NetGraph.Nodes.Add(this); }
        void OnDisable() { NetGraph.Nodes.Remove(this); }
        void OnDestroy()
        {
            NetGraph.Nodes.Remove(this);
            foreach (var p in Ports) foreach (var r in new List<NetRun>(p.Runs)) if (r != null) r.Detach(p);
        }

        public string Label => string.IsNullOrEmpty(DisplayName) ? (Piece != null && Piece.Def != null ? Piece.Def.Name : Type) : DisplayName;

        /// <summary>Rebuilds joint decorations (crimp rings, purple primer, solder fillets, foil tape bands).</summary>
        public void RefreshJoints()
        {
            if (_jointVis != null) Destroy(_jointVis);
            var mm = new MepMesh { Origin = transform.position };
            var mats = new List<Material>();
            int Mat(Material m) { int i = mats.IndexOf(m); if (i < 0) { mats.Add(m); i = mats.Count - 1; } return i; }
            foreach (var p in Ports)
            {
                if (p.Run == null || !p.Joined) continue;
                var s = p.Run.Spec;
                Vector3 c = p.WorldPos, d = p.WorldDir;
                switch (p.Join)
                {
                    case JoinMethod.Crimp:
                        mm.Band(c + d * s.Width * 0.25f, c + d * s.Width * 0.75f, MepMesh.Circle(s.Radius * 1.18f), Mat(MepLook.Get(SurfaceKind.Brass, MepLook.Copper)));
                        break;
                    case JoinMethod.Solvent:
                        mm.Band(c - d * s.Width * 0.05f, c + d * s.Width * 0.22f, MepMesh.Circle(s.Radius * 1.015f), Mat(MepPrint.Primer));
                        break;
                    case JoinMethod.Sweat:
                        mm.Band(c - d * 0.001f, c + d * s.Width * 0.12f, MepMesh.Circle(s.Radius * 1.05f), Mat(MepLook.Get(SurfaceKind.Nickel)));
                        break;
                    case JoinMethod.Tape:
                        if (s.Shape == RunShape.Rect) mm.Band(c - d * 0.03f, c + d * 0.03f, MepMesh.Rect(s.Width * 1.02f, s.Height * 1.02f), Mat(MepPrint.FoilTape));
                        else mm.Band(c - d * 0.03f, c + d * 0.03f, MepMesh.Circle(s.Radius * 1.02f, 24), Mat(MepPrint.FoilTape));
                        break;
                }
            }
            if (mats.Count == 0) return;
            _jointVis = mm.Spawn(transform, "Joints", mats.ToArray());
        }
    }

    /// <summary>Static registry + queries for the MEP network graph.</summary>
    public static class NetGraph
    {
        public static readonly List<NetNode> Nodes = new List<NetNode>();
        public static readonly List<NetRun> Runs = new List<NetRun>();
        /// <summary>Raised after any run is added/removed/connected so solvers can re-evaluate.</summary>
        public static event Action Changed;
        public static int Version { get; private set; }
        public static void Touch() { Version++; Changed?.Invoke(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Nodes.Clear(); Runs.Clear(); Changed = null; Version = 0; }

        public static IEnumerable<NetNode> NodesOf(Trade t) { foreach (var n in Nodes) if (n != null && n.Trade == t) yield return n; }
        public static IEnumerable<NetRun> RunsOf(Trade t) { foreach (var r in Runs) if (r != null && r.Spec.Trade == t) yield return r; }

        /// <summary>Nearest port within radius that can accept the spec (free, compatible) — used for snapping run ends.</summary>
        public static NetPort NearestPort(Vector3 p, float radius, Func<NetPort, bool> filter = null)
        {
            NetPort best = null; float bd = radius * radius;
            foreach (var n in Nodes)
            {
                if (n == null) continue;
                foreach (var port in n.Ports)
                {
                    if (filter != null && !filter(port)) continue;
                    float d = (port.WorldPos - p).sqrMagnitude;
                    if (d < bd) { bd = d; best = port; }
                }
            }
            return best;
        }

        /// <summary>Runs touching a node (through any of its ports).</summary>
        public static List<NetRun> RunsAt(NetNode n)
        {
            var l = new List<NetRun>();
            foreach (var p in n.Ports) foreach (var r in p.Runs) if (r != null && !l.Contains(r)) l.Add(r);
            return l;
        }

        /// <summary>Breadth-first walk of connected nodes. pass(run) decides whether a run conducts (e.g. Romex only).</summary>
        public static List<NetNode> Connected(NetNode start, Func<NetRun, bool> pass = null, Func<NetNode, bool> through = null)
        {
            var seen = new HashSet<NetNode> { start };
            var order = new List<NetNode> { start };
            var q = new Queue<NetNode>(); q.Enqueue(start);
            while (q.Count > 0)
            {
                var n = q.Dequeue();
                if (n != start && through != null && !through(n)) continue;
                foreach (var r in RunsAt(n))
                {
                    if (pass != null && !pass(r)) continue;
                    var o = r.Other(n);
                    if (o != null && seen.Add(o)) { order.Add(o); q.Enqueue(o); }
                }
            }
            return order;
        }

        /// <summary>All runs reachable from a node (including runs with a free end).</summary>
        public static List<NetRun> ConnectedRuns(NetNode start, Func<NetRun, bool> pass = null)
        {
            var runs = new List<NetRun>();
            foreach (var n in Connected(start, pass))
                foreach (var r in RunsAt(n)) if ((pass == null || pass(r)) && !runs.Contains(r)) runs.Add(r);
            return runs;
        }

        /// <summary>Path of nodes from a to b (BFS), or null.</summary>
        public static List<NetNode> Path(NetNode a, NetNode b, Func<NetRun, bool> pass = null)
        {
            var prev = new Dictionary<NetNode, NetNode> { { a, null } };
            var q = new Queue<NetNode>(); q.Enqueue(a);
            while (q.Count > 0)
            {
                var n = q.Dequeue();
                if (n == b) break;
                foreach (var r in RunsAt(n))
                {
                    if (pass != null && !pass(r)) continue;
                    var o = r.Other(n);
                    if (o != null && !prev.ContainsKey(o)) { prev[o] = n; q.Enqueue(o); }
                }
            }
            if (!prev.ContainsKey(b)) return null;
            var path = new List<NetNode>();
            for (var c = b; c != null; c = prev[c]) path.Insert(0, c);
            return path;
        }

        /// <summary>The run between two adjacent nodes (or null).</summary>
        public static NetRun RunBetween(NetNode a, NetNode b, Func<NetRun, bool> pass = null)
        {
            foreach (var r in RunsAt(a)) if ((pass == null || pass(r)) && r.Other(a) == b) return r;
            return null;
        }
    }
}
