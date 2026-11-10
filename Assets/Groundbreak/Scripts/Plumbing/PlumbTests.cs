using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A problem found by a plumbing test: where the water (or air) gets out, or what the inspector points at.</summary>
    public class PlumbLeak
    {
        public Vector3 Pos;
        public string Note;
        public NetPort Port;
        public NetRun Run;
        public override string ToString() => Note;
    }

    /// <summary>DWV flow direction and slope: every run drains toward the building drain outlet (lowest DWV end).</summary>
    public static class PlumbSlope
    {
        public static IEnumerable<NetRun> DwvRuns() { foreach (var r in NetGraph.Runs) if (PlumbCatalog.IsDwv(r)) yield return r; }

        /// <summary>The outlet: the lowest end of any DWV run (where the building drain leaves / gets the test ball).</summary>
        public static bool Outlet(out NetRun run, out bool atStart)
        {
            run = null; atStart = false; float best = float.MaxValue;
            foreach (var r in DwvRuns())
            {
                if (r.StartPoint.y < best) { best = r.StartPoint.y; run = r; atStart = true; }
                if (r.EndPoint.y < best) { best = r.EndPoint.y; run = r; atStart = false; }
            }
            return run != null;
        }

        /// <summary>Hop distance of every DWV node from the outlet node (BFS over drain/vent runs).</summary>
        public static Dictionary<NetNode, int> Distances(out NetNode outletNode)
        {
            var dist = new Dictionary<NetNode, int>();
            outletNode = null;
            if (!Outlet(out var r, out bool atStart)) return dist;
            var port = atStart ? r.A : r.B;
            var q = new Queue<NetNode>();
            if (port != null) { outletNode = port.Node; dist[outletNode] = 0; q.Enqueue(outletNode); }
            else
            {
                var other = atStart ? r.NodeB : r.NodeA; // free (open) outlet: start from the run's other node
                if (other != null) { dist[other] = 1; q.Enqueue(other); }
            }
            while (q.Count > 0)
            {
                var n = q.Dequeue();
                foreach (var run in NetGraph.RunsAt(n))
                {
                    if (!PlumbCatalog.IsDwv(run)) continue;
                    var o = run.Other(n);
                    if (o != null && !dist.ContainsKey(o)) { dist[o] = dist[n] + 1; q.Enqueue(o); }
                }
            }
            return dist;
        }

        /// <summary>True when end B of the run is downstream (closer to the outlet).</summary>
        public static bool FlowsToB(NetRun r, Dictionary<NetNode, int> dist)
        {
            int da = r.NodeA != null && dist.TryGetValue(r.NodeA, out var a) ? a : int.MaxValue;
            int db = r.NodeB != null && dist.TryGetValue(r.NodeB, out var b) ? b : int.MaxValue;
            if (da != db) return db < da;
            return r.EndPoint.y <= r.StartPoint.y;
        }

        /// <summary>Fall in inches per foot in the direction of flow (negative = graded backwards), infinity for vertical.</summary>
        public static float FlowSlope(NetRun r, int seg, bool toB)
        {
            float s = r.SlopeInPerFt(seg);
            if (float.IsInfinity(s)) return s;
            return toB ? s : -s;
        }

        public struct Leg { public NetRun Run; public int Seg; public float Slope; public float Min; public bool Horizontal; public bool Ok; public Vector3 Mid; }

        public static List<Leg> Legs()
        {
            var o = new List<Leg>();
            var dist = Distances(out _);
            foreach (var r in DwvRuns())
            {
                bool toB = FlowsToB(r, dist);
                for (int i = 0; i + 1 < r.Points.Count; i++)
                {
                    bool hz = r.SegmentIsHorizontal(i);
                    float s = FlowSlope(r, i, toB);
                    float min = DrainRules.MinSlope(r.Spec);
                    bool ok = !hz || (r.Spec.Kind == NetKind.Vent ? s >= -0.01f : s >= min - 0.01f);
                    o.Add(new Leg { Run = r, Seg = i, Slope = s, Min = min, Horizontal = hz, Ok = ok, Mid = (r.Points[i] + r.Points[i + 1]) * 0.5f });
                }
            }
            return o;
        }

        public static IEnumerable<PlumbLeak> SlopeIssues()
        {
            foreach (var l in Legs())
            {
                if (l.Ok) continue;
                string what = l.Slope < -0.01f ? "graded backwards (away from the building drain)" : $"too flat: {MepAim.SlopeLabel(l.Slope)} (min {Fittings.Fraction(l.Min)}\"/ft)";
                yield return new PlumbLeak { Pos = l.Mid, Run = l.Run, Note = $"{l.Run.Spec.Name} leg {what}" };
            }
        }
    }

    /// <summary>The test gauge on a supply system: pump it to 100 psi water (or 60 psi air) and it must hold 15 game minutes.</summary>
    public class PressureGauge : MonoBehaviour
    {
        public enum TestState { Idle, Holding, Passed, Failed }
        public NetNode Node;
        public Transform Needle;
        public Vector3 DialAxis = Vector3.forward;
        public float Psi, TestPsi = 100f, Minutes;
        public bool Air;
        public TestState State;
        public string Status = "Not pumped";
        public readonly List<PlumbLeak> Leaks = new List<PlumbLeak>();
        public int PassedVersion = -1;

        void Update()
        {
            if (State == TestState.Holding && GameClock.I != null)
                PressureTest.Advance(this, Time.deltaTime * 1440f / GameClock.RealSecondsPerDay);
            UpdateNeedle();
        }

        /// <summary>0 psi at 7:30, 160 psi at 4:30 (270° sweep, clockwise as seen from the front).</summary>
        public void UpdateNeedle()
        {
            if (Needle != null) Needle.rotation = Quaternion.AngleAxis(-135f + Mathf.Clamp(Psi, 0f, 160f) / 160f * 270f, DialAxis);
        }
    }

    /// <summary>Supply pressure test: leaks = un-made joints (they weep and the gauge drops), open stubs / free pipe ends
    /// (it never builds pressure).</summary>
    public static class PressureTest
    {
        public const float HoldMinutes = 15f;
        public const float LeakPsiPerMinute = 7f;

        public static List<PressureGauge> Gauges()
        {
            var l = new List<PressureGauge>(Object.FindObjectsByType<PressureGauge>());
            l.RemoveAll(g => g == null || g.Node == null);
            return l;
        }

        /// <summary>Everything wrong with the supply network the gauge (or any node) is on.</summary>
        public static List<PlumbLeak> FindLeaks(NetNode start, out int openCount)
        {
            var o = new List<PlumbLeak>(); openCount = 0;
            if (start == null) return o;
            System.Func<NetRun, bool> pass = PlumbCatalog.IsSupply;
            foreach (var n in NetGraph.Connected(start, pass))
                foreach (var p in n.Ports)
                {
                    if (p.Kind != NetKind.Supply) continue;
                    if (p.Open) { openCount++; o.Add(new PlumbLeak { Pos = p.WorldPos, Port = p, Note = $"Open {p.Label} at {n.Label} - cap it or connect it" }); }
                    else if (p.NeedsJoin) o.Add(new PlumbLeak { Pos = p.WorldPos, Port = p, Note = $"Leak: {JoinName(p.Join)} not made at {n.Label} ({p.Label})" });
                }
            foreach (var r in NetGraph.ConnectedRuns(start, pass))
            {
                if (r.FreeEndA) { openCount++; o.Add(new PlumbLeak { Pos = r.StartPoint, Run = r, Note = $"Open end of {r.Spec.Name} - cap it" }); }
                if (r.FreeEndB) { openCount++; o.Add(new PlumbLeak { Pos = r.EndPoint, Run = r, Note = $"Open end of {r.Spec.Name} - cap it" }); }
            }
            return o;
        }

        public static string JoinName(JoinMethod j)
        {
            switch (j)
            {
                case JoinMethod.Crimp: return "crimp ring";
                case JoinMethod.Sweat: return "sweat joint";
                case JoinMethod.Solvent: return "glue joint";
                case JoinMethod.Thread: return "threaded joint";
                case JoinMethod.Tape: return "taped seam";
                default: return "joint";
            }
        }

        /// <summary>Pump the system up. Returns false (and fails the test) when it won't build pressure.</summary>
        public static bool Pump(PressureGauge g, bool air = false)
        {
            if (g == null) return false;
            g.Air = air; g.TestPsi = air ? 60f : 100f; g.Minutes = 0f;
            g.Leaks.Clear();
            g.Leaks.AddRange(FindLeaks(g.Node, out int open));
            if (open > 0)
            {
                g.Psi = 0f; g.State = PressureGauge.TestState.Failed; g.UpdateNeedle();
                g.Status = $"Won't build pressure: {g.Leaks.Find(l => l.Port == null || l.Port.Open)?.Note ?? g.Leaks[0].Note}";
                return false;
            }
            g.Psi = g.TestPsi; g.State = PressureGauge.TestState.Holding; g.UpdateNeedle();
            g.Status = $"Pumped to {g.TestPsi:0} psi {(air ? "air" : "water")} - holding";
            return true;
        }

        /// <summary>Advances the hold by game minutes. Each un-made joint bleeds pressure; 15 minutes without a drop = pass.</summary>
        public static void Advance(PressureGauge g, float minutes)
        {
            if (g == null || g.State != PressureGauge.TestState.Holding) return;
            g.Leaks.Clear();
            g.Leaks.AddRange(FindLeaks(g.Node, out int open));
            float rate = open > 0 ? 200f : LeakPsiPerMinute * g.Leaks.Count * (g.Air ? 0.6f : 1f);
            g.Psi = Mathf.Max(0f, g.Psi - rate * minutes);
            g.UpdateNeedle();
            g.Minutes += minutes;
            if (g.Psi < g.TestPsi - 2f)
            {
                g.State = PressureGauge.TestState.Failed;
                g.Status = $"FAILED: dropped to {g.Psi:0} psi in {g.Minutes:0} min - {(g.Leaks.Count > 0 ? g.Leaks[0].Note : "leak")}";
            }
            else if (g.Minutes >= HoldMinutes)
            {
                g.State = PressureGauge.TestState.Passed;
                g.PassedVersion = NetGraph.Version;
                g.Status = $"PASSED: held {g.Psi:0} psi for {HoldMinutes:0} min";
            }
            else g.Status = $"Holding {g.Psi:0} psi · {g.Minutes:0.0}/{HoldMinutes:0} min";
        }

        /// <summary>Passed and nothing has opened up since (re-checked against the current network).</summary>
        public static bool Passed(PressureGauge g) => g != null && g.State == PressureGauge.TestState.Passed && FindLeaks(g.Node, out _).Count == 0;
    }

    /// <summary>DWV water test: plug the building drain (test ball / caps), fill the system to the vent through the roof
    /// and watch it 15 minutes. Fails on un-glued joints, open ends, flat or back-graded drains, or no vent through the roof.</summary>
    public static class DrainTest
    {
        public enum TestState { Idle, Holding, Passed, Failed }
        public static TestState State;
        public static float Minutes, HeadFt;
        public static string Status = "Not filled";
        public static readonly List<PlumbLeak> Issues = new List<PlumbLeak>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { State = TestState.Idle; Minutes = 0; HeadFt = 0; Status = "Not filled"; Issues.Clear(); }

        public static List<PlumbLeak> Evaluate()
        {
            var o = new List<PlumbLeak>();
            var runs = new List<NetRun>(PlumbSlope.DwvRuns());
            if (runs.Count == 0) { o.Add(new PlumbLeak { Note = "No drain/waste/vent piping to test" }); return o; }
            var dist = PlumbSlope.Distances(out var outlet);
            PlumbSlope.Outlet(out var outRun, out bool atStart);
            if (outlet == null) o.Add(new PlumbLeak { Pos = atStart ? outRun.StartPoint : outRun.EndPoint, Run = outRun, Note = "Building drain outlet is open - plug it with a test ball/cap" });
            var seenNodes = new HashSet<NetNode>();
            foreach (var r in runs)
            {
                bool reached = (r.NodeA != null && dist.ContainsKey(r.NodeA)) || (r.NodeB != null && dist.ContainsKey(r.NodeB));
                if (!reached) o.Add(new PlumbLeak { Pos = r.PointAt(r.Length * 0.5f, out _), Run = r, Note = $"{r.Spec.Name} isn't connected to the building drain" });
                if (r.FreeEndA && !(r == outRun && atStart)) o.Add(new PlumbLeak { Pos = r.StartPoint, Run = r, Note = $"Open end of {r.Spec.Name} - water runs out here" });
                if (r.FreeEndB && !(r == outRun && !atStart)) o.Add(new PlumbLeak { Pos = r.EndPoint, Run = r, Note = $"Open end of {r.Spec.Name} - water runs out here" });
                if (r.NodeA != null) seenNodes.Add(r.NodeA);
                if (r.NodeB != null) seenNodes.Add(r.NodeB);
            }
            bool vent = false; float top = float.MinValue;
            foreach (var n in seenNodes)
            {
                if (n.Type == "vent-roof") { vent = true; top = Mathf.Max(top, n.transform.position.y); }
                foreach (var p in n.Ports)
                {
                    if (p.Kind != NetKind.Drain && p.Kind != NetKind.Vent) continue;
                    if (p.Open) o.Add(new PlumbLeak { Pos = p.WorldPos, Port = p, Note = $"Open {p.Label} at {n.Label} - water runs out (cap it)" });
                    else if (p.NeedsJoin) o.Add(new PlumbLeak { Pos = p.WorldPos, Port = p, Note = $"Joint not primed + glued at {n.Label} ({p.Label}) - it weeps" });
                }
            }
            o.AddRange(PlumbSlope.SlopeIssues());
            if (!vent)
            {
                Vector3 pos = outRun.EndPoint; float hi = float.MinValue;
                foreach (var r in runs) foreach (var pt in r.Points) if (pt.y > hi) { hi = pt.y; pos = pt; }
                o.Add(new PlumbLeak { Pos = pos, Note = "No vent through the roof - the stack must terminate above the roof" });
            }
            float low = atStart ? outRun.StartPoint.y : outRun.EndPoint.y;
            HeadFt = vent ? (top - low) / Units.Foot : 0f;
            return o;
        }

        /// <summary>Fill to the vent. Returns false (failed) when water shows up somewhere it shouldn't.</summary>
        public static bool Fill()
        {
            Issues.Clear();
            Issues.AddRange(Evaluate());
            Minutes = 0f;
            if (Issues.Count > 0)
            {
                State = TestState.Failed;
                Status = "FAILED: " + Issues[0].Note + (Issues.Count > 1 ? $" (+{Issues.Count - 1} more)" : "");
                return false;
            }
            State = TestState.Holding;
            Status = $"Filled to the vent ({HeadFt:0.#} ft of head) - holding";
            return true;
        }

        public static void Advance(float minutes)
        {
            if (State != TestState.Holding) return;
            Issues.Clear();
            Issues.AddRange(Evaluate());
            Minutes += minutes;
            if (Issues.Count > 0) { State = TestState.Failed; Status = "FAILED: water level dropped - " + Issues[0].Note; }
            else if (Minutes >= PressureTest.HoldMinutes) { State = TestState.Passed; Status = $"PASSED: water held at the vent for {PressureTest.HoldMinutes:0} min"; }
            else Status = $"Holding at the vent · {Minutes:0.0}/{PressureTest.HoldMinutes:0} min";
        }

        public static void Drain() { State = TestState.Idle; Minutes = 0f; Status = "Drained"; Issues.Clear(); }

        public static bool Passed => State == TestState.Passed && Evaluate().Count == 0;
    }

    /// <summary>Per-frame plumbing system on the "MEP Systems" object: water-test clock, manifold handle colors.</summary>
    public class PlumbSim : MonoBehaviour
    {
        int _ver = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() { MepBootstrap.AddSystem(go => go.AddComponent<PlumbSim>()); }

        void Update()
        {
            if (DrainTest.State == DrainTest.TestState.Holding)
                DrainTest.Advance(Time.deltaTime * 1440f / GameClock.RealSecondsPerDay);
            if (_ver == NetGraph.Version) return;
            _ver = NetGraph.Version;
            foreach (var n in new List<NetNode>(NetGraph.Nodes))
                if (n != null && n.Trade == Trade.Plumbing) Primer(n);
            foreach (var n in NetGraph.Nodes)
            {
                if (n == null || n.Type != "manifold" || n.Piece == null) continue;
                var inlet = n.Port("in");
                bool hot = inlet != null && inlet.Run != null && inlet.Run.Spec.Hot;
                var tag = n.GetComponent<PlumbTag>();
                if (tag == null) tag = n.gameObject.AddComponent<PlumbTag>();
                if (tag.Hot != hot || tag.CapMask != CapMask(n)) { tag.Hot = hot; tag.CapMask = CapMask(n); PlumbParts.BuildManifold(n.Piece, n, hot); }
            }
        }

        /// <summary>Purple primer smear around every glued PVC/ABS joint: on the pipe just outside the hub and on the hub rim
        /// (smooth 32-sided bands so it reads as a solid purple ring).</summary>
        static void Primer(NetNode n)
        {
            var old = n.transform.Find("Primer");
            bool any = false;
            foreach (var p in n.Ports) if (p.Join == JoinMethod.Solvent && p.Joined && p.Run != null) { any = true; break; }
            if (old != null) { old.name = "Primer (old)"; Object.Destroy(old.gameObject); }
            if (!any) return;
            bool hub = n.Piece == null || n.Type == "ptrap" || n.Type == "cleanout";
            var mm = new MepMesh { Origin = n.transform.position };
            foreach (var p in n.Ports)
            {
                if (p.Join != JoinMethod.Solvent || !p.Joined || p.Run == null) continue;
                var s = p.Run.Spec;
                Vector3 c = p.WorldPos, d = p.WorldDir;
                mm.Band(c - d * 0.001f, c + d * s.Width * 0.3f, MepMesh.Circle(s.Radius * 1.022f, 32), 0);
                if (hub) mm.Band(c - d * s.Width * 0.14f, c + d * 0.0005f, MepMesh.Circle(s.Radius * 1.16f, 32), 0);
            }
            mm.Spawn(n.transform, "Primer", new[] { MepLook.Get(SurfaceKind.PaintedWhite, MepLook.PrimerPurple) });
        }

        static int CapMask(NetNode n) { int m = 0; for (int i = 0; i < n.Ports.Count; i++) if (n.Ports[i].Run == null && n.Ports[i].Capped) m |= 1 << i; return m; }
    }

    /// <summary>Small state holder on plumbing nodes (manifold handle color...).</summary>
    public class PlumbTag : MonoBehaviour { public bool Hot; public int CapMask; }
}
