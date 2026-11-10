using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public class RouteResult
    {
        public readonly List<NetRun> Runs = new List<NetRun>();
        public readonly List<NetNode> Fittings = new List<NetNode>();
        public readonly List<string> Warnings = new List<string>();
        public float Length;
        public bool Ok => Runs.Count > 0;
    }

    /// <summary>Auto-inserted fittings (elbows, bends, tees, wyes, couplings, caps) as NetNodes with real-size visuals.</summary>
    public static class Fittings
    {
        /// <summary>Distance from fitting center to the socket face where the pipe end sits.</summary>
        public static float Leg(RunSpec s)
        {
            if (s.Kind == NetKind.Duct) return Mathf.Max(s.Width, s.Height) * 0.75f;
            if (s.IsDrain) return s.Width * 1.25f;
            return Mathf.Max(s.Width * 1.0f, 0.018f);
        }

        public static string NameFor(string type, RunSpec s, float angle)
        {
            bool dwv = s.IsDrain && s.Kind != NetKind.Condensate;
            string size = Fraction(s.NominalIn) + "\" ";
            switch (type)
            {
                case "elbow":
                    if (s.Kind == NetKind.Duct) return size + (s.Shape == RunShape.Rect ? "rect elbow " : "adjustable elbow ") + Mathf.RoundToInt(angle) + "°";
                    if (dwv) return size + (angle > 67 ? "1/4 bend (long sweep)" : angle > 33 ? "1/8 bend" : "1/16 bend");
                    return size + Mathf.RoundToInt(angle) + "° elbow";
                case "tee":
                    if (s.Kind == NetKind.Duct) return size + "takeoff tee";
                    return size + "tee";
                case "santee": return size + "sanitary tee";
                case "wye": return size + "wye";
                case "coupling": return size + "coupling";
                case "cap": return size + (s.IsDrain ? "test cap" : "cap");
                default: return size + type;
            }
        }

        public static string Fraction(float inches)
        {
            int whole = Mathf.FloorToInt(inches + 1e-4f);
            float f = inches - whole;
            string frac = f < 0.06f ? "" : f < 0.2f ? "1/8" : f < 0.3f ? "1/4" : f < 0.45f ? "3/8" : f < 0.55f ? "1/2" : f < 0.7f ? "5/8" : f < 0.8f ? "3/4" : "7/8";
            if (whole == 0) return frac.Length > 0 ? frac : "0";
            return frac.Length > 0 ? whole + "-" + frac : whole.ToString();
        }

        public static Material MaterialFor(RunSpec s)
        {
            if (s.Kind == NetKind.Duct) return MepLook.Get(SurfaceKind.Galvanized);
            switch (s.Join)
            {
                case JoinMethod.Crimp: return MepLook.Get(SurfaceKind.Brass);                 // brass PEX crimp fittings
                case JoinMethod.Sweat: return MepLook.Of(s);                                  // wrought copper
                default: return MepLook.Of(s);                                               // PVC white / ABS black
            }
        }

        /// <summary>Creates a fitting at center with one port per direction (world dirs). Port join method follows the spec.</summary>
        public static NetNode Create(string type, RunSpec spec, Vector3 center, IList<Vector3> dirs, float angle = 90f)
        {
            var go = new GameObject("Fitting " + type);
            go.transform.position = center;
            var node = go.AddComponent<NetNode>();
            node.Type = type; node.Trade = spec.Trade;
            node.DisplayName = NameFor(type, spec, angle);
            float leg = Leg(spec);
            for (int i = 0; i < dirs.Count; i++)
                node.AddPort("p" + i, dirs[i].normalized * leg, dirs[i], spec.Kind, spec.NominalIn, spec.Join);
            BuildVisual(node, spec, dirs, leg);
            var col = go.AddComponent<SphereCollider>();
            col.radius = leg * 0.9f;
            return node;
        }

        static void BuildVisual(NetNode node, RunSpec s, IList<Vector3> dirs, float leg)
        {
            Vector3 c = node.transform.position;
            var mm = new MepMesh { Origin = c };
            var mat = MaterialFor(s);
            bool socket = s.Kind != NetKind.Duct;
            float hubR = s.Join == JoinMethod.Crimp ? 1.02f : 1.13f;   // PEX insert fittings sit inside; hubs are bigger
            var body = MepMesh.For(s, socket ? (s.Join == JoinMethod.Crimp ? 0.8f : 1.06f) : 1.0f);
            var hub = MepMesh.For(s, hubR);
            if (dirs.Count == 2)
            {
                float ang = Vector3.Angle(dirs[0], -dirs[1]);
                if (ang < 2f) mm.Band(c + dirs[0] * leg * 0.98f, c + dirs[1] * leg * 0.98f, body, 0, false);
                else mm.Sweep(MepMesh.ElbowPath(c, dirs[0], dirs[1], leg * 0.98f, 10), body, 0, false, false);
            }
            else if (dirs.Count == 1)
            {
                mm.Band(c, c + dirs[0] * leg * 0.98f, hub, 0);
            }
            else
            {
                // run (first two, if opposite) + branches
                int a = 0, b = 1;
                for (int i = 0; i < dirs.Count; i++) for (int j = i + 1; j < dirs.Count; j++)
                    if (Vector3.Dot(dirs[i], dirs[j]) < Vector3.Dot(dirs[a], dirs[b])) { a = i; b = j; }
                mm.Band(c + dirs[a] * leg * 0.98f, c + dirs[b] * leg * 0.98f, body, 0);
                for (int i = 0; i < dirs.Count; i++)
                {
                    if (i == a || i == b) continue;
                    if (node.Type == "santee")
                    {
                        // sanitary tee: the branch sweeps down into the run
                        Vector3 down = Vector3.Dot(dirs[a], Vector3.down) > 0.5f ? dirs[a] : dirs[b];
                        mm.Sweep(MepMesh.ElbowPath(c, dirs[i], down, leg * 0.98f, 8), body, 0, false, false);
                    }
                    else mm.Band(c, c + dirs[i] * leg * 0.98f, body, 0);
                }
            }
            if (socket)
                for (int i = 0; i < dirs.Count; i++)
                {
                    float hl = s.Join == JoinMethod.Crimp ? leg * 0.25f : leg * 0.55f;
                    mm.Band(c + dirs[i] * (leg - hl), c + dirs[i] * leg, hub, 0, true);
                }
            mm.Spawn(node.transform, "Visual", new[] { mat });
        }
    }

    /// <summary>Pipe / cable / duct stock usage: consumes sticks or coil-feet from Game.Stock, keeps offcuts.</summary>
    public static class MepStock
    {
        static readonly Dictionary<string, float> _offcutFt = new Dictionary<string, float>();
        /// <summary>When true (tests / relaxed mode) routing never needs stock.</summary>
        public static bool Free;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { _offcutFt.Clear(); Free = false; }

        public static float AvailableFeet(RunSpec s)
        {
            if (Free || string.IsNullOrEmpty(s.StockId)) return float.PositiveInfinity;
            _offcutFt.TryGetValue(s.Id, out float off);
            return Game.Stock.Count(s.StockId) * s.FeetPerUnit + off;
        }

        public static bool Take(RunSpec s, float meters)
        {
            if (Free || string.IsNullOrEmpty(s.StockId)) return true;
            float ft = meters / Units.Foot;
            if (AvailableFeet(s) + 1e-3f < ft) return false;
            _offcutFt.TryGetValue(s.Id, out float off);
            while (off < ft) { Game.Stock.TryTake(s.StockId, 1); off += s.FeetPerUnit; }
            _offcutFt[s.Id] = off - ft;
            return true;
        }

        public static void Return(RunSpec s, float meters)
        {
            if (Free || string.IsNullOrEmpty(s.StockId)) return;
            _offcutFt.TryGetValue(s.Id, out float off);
            _offcutFt[s.Id] = off + meters / Units.Foot;
        }
    }

    /// <summary>Turns clicked points into runs + auto-inserted fittings, with drain slope applied.</summary>
    public static class RunBuilder
    {
        static readonly float[] StdAngles = { 22.5f, 45f, 90f };

        /// <summary>Applies a fall of slopeInPerFt to every near-horizontal segment, walking from the first point.</summary>
        public static List<Vector3> ApplySlope(IList<Vector3> pts, float slopeInPerFt)
        {
            var o = new List<Vector3>(pts);
            if (slopeInPerFt == 0f) return o;
            for (int i = 1; i < o.Count; i++)
            {
                Vector3 d = pts[i] - pts[i - 1];
                float h = new Vector2(d.x, d.z).magnitude;
                float dy = d.y;
                if (Mathf.Abs(dy) < h * 0.2f) dy = -Units.In(slopeInPerFt) * (h / Units.Foot);
                o[i] = new Vector3(o[i].x, o[i - 1].y + dy, o[i].z);
            }
            return o;
        }

        /// <summary>Routes through pts (world, already snapped). start/end ports (optional) are connected and their
        /// positions replace the first/last point. Elbows/bends are auto-inserted at corners unless the spec bends inline.
        /// slope (in/ft) is applied to horizontal legs of drain/condensate runs. Stock is consumed unless MepStock.Free.</summary>
        public static RouteResult Route(RunSpec spec, IList<Vector3> input, NetPort start = null, NetPort end = null, float slopeInPerFt = 0f, bool takeStock = true)
        {
            var res = new RouteResult();
            if (spec == null || input == null || input.Count < 2) { res.Warnings.Add("Need at least two points."); return res; }
            var pts = new List<Vector3>(input);
            if (start != null)
            {
                if (!start.Accepts(spec)) { res.Warnings.Add($"{spec.Name} doesn't fit {start.Node.Label} ({start.Label})."); return res; }
                pts[0] = start.WorldPos;
            }
            if (end != null)
            {
                if (!end.Accepts(spec)) { res.Warnings.Add($"{spec.Name} doesn't fit {end.Node.Label} ({end.Label})."); return res; }
                pts[pts.Count - 1] = end.WorldPos;
            }
            // remove duplicate points
            for (int i = pts.Count - 1; i > 0; i--) if ((pts[i] - pts[i - 1]).sqrMagnitude < 1e-6f) pts.RemoveAt(i);
            if (pts.Count < 2) { res.Warnings.Add("Run too short."); return res; }
            if (slopeInPerFt != 0f && (spec.IsDrain))
            {
                // keep the end on its port: slope is applied from the start; the last leg absorbs it if the end is fixed
                var sloped = ApplySlope(pts, slopeInPerFt);
                if (end != null) sloped[sloped.Count - 1] = end.WorldPos;
                pts = sloped;
            }

            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
            if (takeStock && !MepStock.Take(spec, total))
            {
                res.Warnings.Add($"Not enough {spec.Name} on site: need {total / Units.Foot:0.#} ft, have {MepStock.AvailableFeet(spec):0.#} ft.");
                return res;
            }
            res.Length = total;

            if (spec.BendsInline || pts.Count == 2)
            {
                if (spec.BendsInline) CheckBends(spec, pts, res);
                res.Runs.Add(NetRun.Create(spec, pts, start, end));
                return res;
            }

            // fittings at every corner
            NetPort prevPort = start;
            Vector3 segStart = pts[0];
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 dIn = (pts[i] - pts[i - 1]).normalized, dOut = (pts[i + 1] - pts[i]).normalized;
                float ang = Vector3.Angle(dIn, dOut);
                if (ang < 1f) continue; // straight through, no fitting
                float std = Nearest(ang);
                if (Mathf.Abs(std - ang) > 4f) res.Warnings.Add($"No standard fitting for {ang:0.#}° at corner {i}; used a {std}° fitting.");
                var fit = Fittings.Create("elbow", spec, pts[i], new[] { -dIn, dOut }, std);
                ChargeFitting(spec, fit, res);
                var pIn = fit.Ports[0];
                var seg = new List<Vector3> { segStart, pIn.WorldPos };
                res.Runs.Add(NetRun.Create(spec, seg, prevPort, pIn));
                prevPort = fit.Ports[1];
                segStart = prevPort.WorldPos;
            }
            res.Runs.Add(NetRun.Create(spec, new List<Vector3> { segStart, pts[pts.Count - 1] }, prevPort, end));
            return res;
        }

        static float Nearest(float ang)
        {
            float best = StdAngles[0];
            foreach (var a in StdAngles) if (Mathf.Abs(a - ang) < Mathf.Abs(best - ang)) best = a;
            return best;
        }

        static void CheckBends(RunSpec spec, List<Vector3> pts, RouteResult res)
        {
            float rMin = spec.MinBendRadius;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                float la = Vector3.Distance(pts[i], pts[i - 1]), lb = Vector3.Distance(pts[i], pts[i + 1]);
                if (Mathf.Min(la, lb) < rMin) res.Warnings.Add($"Bend at corner {i} is tighter than the {Units.Format(rMin)} minimum radius for {spec.Name}.");
            }
        }

        static void ChargeFitting(RunSpec spec, NetNode fit, RouteResult res)
        {
            if (MepStock.Free || spec.FittingPrice <= 0) return;
            if (!Game.Wallet.TrySpend(spec.FittingPrice, "Fitting: " + fit.DisplayName)) res.Warnings.Add("Couldn't pay for " + fit.DisplayName);
        }

        /// <summary>Cuts an existing run at point and inserts a tee (or sanitary tee / wye for DWV, takeoff for duct).
        /// Returns the branch port facing branchDir. type: "tee", "santee", "wye" (null = auto).</summary>
        public static NetPort TeeInto(NetRun run, Vector3 point, Vector3 branchDir, string type = null)
        {
            if (run == null) return null;
            var spec = run.Spec;
            float s = run.DistanceAlong(point, out _);
            Vector3 c = run.PointAt(s, out Vector3 d);
            float leg = Fittings.Leg(spec);
            if (s < leg * 1.2f || s > run.Length - leg * 1.2f) return null;
            branchDir = (branchDir - d * Vector3.Dot(branchDir, d));
            if (branchDir.sqrMagnitude < 1e-6f) return null;
            branchDir.Normalize();
            if (type == null)
            {
                if (spec.Kind == NetKind.Drain && Mathf.Abs(d.y) > 0.7f) type = "santee";          // horizontal branch into vertical stack
                else if (spec.Kind == NetKind.Drain) type = "wye";                                   // horizontal-to-horizontal
                else type = "tee";
            }
            Vector3 bdir = branchDir;
            if (type == "wye") bdir = (branchDir - d).normalized; // wye branch enters at 45° pointing downstream
            var second = run.SplitAt(c);
            if (second == null) return null;
            var fit = Fittings.Create(type, spec, c, new[] { -d, d, bdir });
            // trim the two halves back to the sockets
            run.Points[run.Points.Count - 1] = fit.Ports[0].WorldPos;
            second.Points[0] = fit.Ports[1].WorldPos;
            run.Attach(fit.Ports[0], false);
            second.Attach(fit.Ports[1], true);
            run.Rebuild(); second.Rebuild();
            if (!MepStock.Free) Game.Wallet.TrySpend(spec.FittingPrice + 2, "Fitting: " + fit.DisplayName);
            return fit.Ports[2];
        }

        /// <summary>Caps (or plugs) a free end of a run with a test cap fitting. Returns the cap node.</summary>
        public static NetNode CapEnd(NetRun run, bool atStart)
        {
            var spec = run.Spec;
            Vector3 p = atStart ? run.StartPoint : run.EndPoint;
            Vector3 d = atStart ? (run.Points[0] - run.Points[1]).normalized : (run.EndPoint - run.Points[run.Points.Count - 2]).normalized;
            var cap = Fittings.Create("cap", spec, p + d * Fittings.Leg(spec), new[] { -d });
            run.Attach(cap.Ports[0], atStart);
            cap.Ports[0].Joined = true; // push-on / threaded test cap
            run.Rebuild();
            return cap;
        }
    }
}
