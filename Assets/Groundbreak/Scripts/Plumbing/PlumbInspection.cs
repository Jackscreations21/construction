using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Rough plumbing inspection rules (IRC P-chapters / IPC, simplified) registered as data with MepInspection.</summary>
    public static class PlumbInspection
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            R("pl-joints", "Every joint made: PEX crimped, copper sweated, PVC/ABS primed + cemented (IPC 605/705)", UnmadeJoints);
            R("pl-open", "No open pipe ends or unconnected fixture stubs during the rough test (caps/plugs installed)", OpenEnds);
            R("pl-slope", "Horizontal drains slope toward the building drain: 1/4\"/ft up to 2-1/2\", 1/8\"/ft for 3\"+ (IPC 704.1)", Slopes);
            R("pl-trap", "Every fixture is trapped (toilet integral, tub/shower P-trap) (IPC 1002.1)", Traps);
            R("pl-vent", "Each trap is vented within the max trap-arm length: 1-1/2\" 6', 2\" 8', 3\" 12' (IPC 906.1)", Vents);
            R("pl-cleanout", "Cleanout at the base of the stack / upper end of the building drain (IPC 708)", Cleanouts);
            R("pl-pressure", "Water supply pressure test: 100 psi water (or 60 psi air) held 15 minutes (IPC 312.5)", Pressure);
            R("pl-water", "DWV water test: filled to the vent, holds 15 minutes (IPC 312.2)", Water);
            R("pl-hotleft", "Hot water on the left-hand stub/valve inlet (IPC 607.4)", HotLeft);
        }

        static void R(string id, string text, System.Func<IEnumerable<Finding>> check) =>
            MepInspection.Register(new MepRule { Id = id, Trade = Trade.Plumbing, Text = text, Check = check });

        static Finding F(Vector3 pos, string note) => new Finding { Pos = pos, Note = note };

        static IEnumerable<NetNode> PlumbNodes()
        {
            foreach (var n in new List<NetNode>(NetGraph.Nodes)) if (n != null && n.Trade == Trade.Plumbing) yield return n;
        }

        static bool PlumbKind(NetKind k) => k == NetKind.Supply || k == NetKind.Drain || k == NetKind.Vent;

        static IEnumerable<Finding> UnmadeJoints()
        {
            foreach (var n in PlumbNodes())
                foreach (var p in n.Ports)
                    if (PlumbKind(p.Kind) && p.NeedsJoin)
                        yield return F(p.WorldPos, $"Unmade {PressureTest.JoinName(p.Join)}: {n.Label} ({p.Label})");
        }

        static IEnumerable<Finding> OpenEnds()
        {
            foreach (var r in new List<NetRun>(NetGraph.RunsOf(Trade.Plumbing)))
            {
                if (r.FreeEndA) yield return F(r.StartPoint, $"Open end of {r.Spec.Name}");
                if (r.FreeEndB) yield return F(r.EndPoint, $"Open end of {r.Spec.Name}");
            }
            foreach (var n in PlumbNodes())
                foreach (var p in n.Ports)
                    if (PlumbKind(p.Kind) && p.Open)
                        yield return F(p.WorldPos, PlumbParts.IsFixture(n) ? $"{n.Label}: {p.Label} stub not connected" : $"{n.Label}: open {p.Label}");
        }

        static IEnumerable<Finding> Slopes()
        {
            foreach (var l in PlumbSlope.SlopeIssues()) yield return F(l.Pos, l.Note);
        }

        static IEnumerable<Finding> Traps()
        {
            foreach (var n in PlumbParts.Fixtures())
            {
                if (n.Type != "fixture-tub" && n.Type != "fixture-shower") continue; // toilet integral, lavs/sinks: trap on the trap-arm stub
                var p = n.Port("drain");
                if (p == null) continue;
                var r = p.Run;
                var other = r != null ? r.Other(n) : null;
                if (other == null || other.Type != "ptrap")
                    yield return F(p.WorldPos, $"{n.Label} has no P-trap on its drain");
            }
        }

        /// <summary>Max trap-arm (trap weir to vent) length by trap size, IPC table 906.1.</summary>
        public static float MaxTrapArmFt(float nominalIn) => nominalIn <= 1.25f ? 5f : nominalIn <= 1.5f ? 6f : nominalIn <= 2f ? 8f : nominalIn <= 3f ? 12f : 16f;

        /// <summary>Traps in the DWV system: (node holding the trap, trap port, size). Toilets/sinks/lavs at the fixture, tub/shower P-traps.</summary>
        public static IEnumerable<(NetNode node, NetPort port, float size)> Traps2()
        {
            foreach (var n in NetGraph.Nodes)
            {
                if (n == null || n.Trade != Trade.Plumbing) continue;
                if (n.Type == "ptrap") { var o = n.Port("outlet"); if (o != null) yield return (n, o, o.Run != null ? o.Run.Spec.NominalIn : o.NominalIn); }
                else if (n.Type == "fixture-toilet") { var o = n.Port("flange"); if (o != null) yield return (n, o, 3f); }
                else if (n.Type == "fixture-vanity" || n.Type == "fixture-kitchen-sink") { var o = n.Port("trap arm"); if (o != null) yield return (n, o, 1.5f); }
            }
        }

        static bool IsVented(NetNode n)
        {
            if (n.Type == "vent-roof") return true;
            foreach (var r in NetGraph.RunsAt(n)) if (r.Spec.Trade == Trade.Plumbing && r.Spec.Kind == NetKind.Vent) return true;
            return false;
        }

        /// <summary>Developed length (m) along the drain from the trap to the nearest vent connection, or infinity.</summary>
        public static float DistanceToVent(NetNode trap, NetPort trapPort)
        {
            var best = new Dictionary<NetNode, float> { { trap, 0f } };
            var open = new List<NetNode> { trap };
            var done = new HashSet<NetNode>();
            while (open.Count > 0)
            {
                open.Sort((a, b) => best[a].CompareTo(best[b]));
                var n = open[0]; open.RemoveAt(0);
                if (!done.Add(n)) continue;
                if (n != trap && IsVented(n)) return best[n];
                foreach (var r in NetGraph.RunsAt(n))
                {
                    if (!PlumbCatalog.IsDwv(r)) continue;
                    if (n == trap && trapPort != null && r != trapPort.Run) continue; // leave the trap through its outlet only
                    var o = r.Other(n);
                    if (o == null || done.Contains(o)) continue;
                    float d = best[n] + r.Length;
                    if (!best.TryGetValue(o, out var cur) || d < cur) { best[o] = d; open.Add(o); }
                }
            }
            return float.PositiveInfinity;
        }

        static IEnumerable<Finding> Vents()
        {
            foreach (var (node, port, size) in new List<(NetNode, NetPort, float)>(Traps2()))
            {
                if (port.Run == null) continue; // reported as an open stub
                float d = DistanceToVent(node, port);
                float max = MaxTrapArmFt(size) * Units.Foot;
                if (float.IsInfinity(d)) yield return F(port.WorldPos, $"{node.Label}: trap is not vented");
                else if (d > max + 0.01f) yield return F(port.WorldPos, $"{node.Label}: vent is {Units.Format(d)} from the trap (max {MaxTrapArmFt(size):0}' for {Fittings.Fraction(size)}\")");
            }
        }

        static IEnumerable<Finding> Cleanouts()
        {
            bool anyDwv = false;
            foreach (var _ in PlumbSlope.DwvRuns()) { anyDwv = true; break; }
            if (!anyDwv) yield break;
            var dist = PlumbSlope.Distances(out var outlet);
            foreach (var n in dist.Keys) if (n.Type == "cleanout") yield break;
            // point at the most upstream end of the building drain
            Vector3 pos = Vector3.zero; int far = -1;
            foreach (var r in PlumbSlope.DwvRuns())
            {
                if (r.Spec.Kind != NetKind.Drain || r.Spec.NominalIn < 3f) continue;
                foreach (var nn in new[] { r.NodeA, r.NodeB })
                    if (nn != null && dist.TryGetValue(nn, out int dd) && dd > far) { far = dd; pos = nn.transform.position; }
            }
            if (far < 0 && outlet != null) pos = outlet.transform.position;
            yield return F(pos, "No cleanout at the upper end of the building drain / base of the stack");
        }

        static IEnumerable<Finding> Pressure()
        {
            bool anySupply = false;
            foreach (var _ in NetGraph.Runs) { if (PlumbCatalog.IsSupply(_)) { anySupply = true; break; } }
            if (!anySupply) yield break;
            var gauges = PressureTest.Gauges();
            if (gauges.Count == 0)
            {
                Vector3 pos = Vector3.zero;
                foreach (var r in NetGraph.Runs) if (PlumbCatalog.IsSupply(r)) { pos = r.StartPoint; break; }
                yield return F(pos, "No test gauge on the water supply - pressure test not performed");
                yield break;
            }
            foreach (var g in gauges)
            {
                if (PressureTest.Passed(g)) continue;
                var leaks = PressureTest.FindLeaks(g.Node, out _);
                if (g.State == PressureGauge.TestState.Passed && leaks.Count > 0)
                    yield return F(leaks[0].Pos, "Pressure test no longer valid: " + leaks[0].Note);
                else if (g.State == PressureGauge.TestState.Failed)
                    yield return F(g.Leaks.Count > 0 ? g.Leaks[0].Pos : g.transform.position, "Pressure test failed: " + g.Status);
                else
                    yield return F(g.transform.position, "Pressure test not passed yet: " + g.Status);
            }
        }

        static IEnumerable<Finding> Water()
        {
            bool anyDwv = false;
            foreach (var _ in PlumbSlope.DwvRuns()) { anyDwv = true; break; }
            if (!anyDwv || DrainTest.Passed) yield break;
            var issues = DrainTest.Evaluate();
            if (issues.Count > 0) yield return F(issues[0].Pos, "Water test: " + issues[0].Note);
            else
            {
                PlumbSlope.Outlet(out var r, out bool atStart);
                yield return F(atStart ? r.StartPoint : r.EndPoint, "DWV water test not performed (" + DrainTest.Status + ")");
            }
        }

        public static bool? IsHotRun(NetRun r)
        {
            if (r == null || !PlumbCatalog.IsSupply(r.Spec) || !r.Spec.BendsInline) return null; // copper: color unknown
            return r.Spec.Hot;
        }

        static IEnumerable<Finding> HotLeft()
        {
            foreach (var n in PlumbParts.Fixtures())
            {
                var hot = n.Port("hot"); var cold = n.Port("cold");
                if (hot != null && IsHotRun(hot.Run) == false) yield return F(hot.WorldPos, $"{n.Label}: cold (blue) line on the left-hand HOT stub");
                if (cold != null && IsHotRun(cold.Run) == true) yield return F(cold.WorldPos, $"{n.Label}: hot (red) line on the right-hand COLD stub");
                var cin = n.Port("cold in"); var hout = n.Port("hot out");
                if (cin != null && IsHotRun(cin.Run) == true) yield return F(cin.WorldPos, $"{n.Label}: hot line on the cold inlet");
                if (hout != null && IsHotRun(hout.Run) == false) yield return F(hout.WorldPos, $"{n.Label}: cold line on the hot outlet");
            }
        }
    }
}
