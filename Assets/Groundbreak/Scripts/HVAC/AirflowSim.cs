using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A conditioned room: name + world bounds (floor to ceiling). Required airflow = floor area × ~1 CFM/ft²
    /// (a simplified Manual J/D rule of thumb) unless set explicitly.</summary>
    public class RoomZone : MonoBehaviour
    {
        public static readonly List<RoomZone> All = new List<RoomZone>();
        public const float CfmPerSqFt = 1.0f;
        public string RoomName = "Room";
        public Bounds Bounds;
        public float RequiredCfmOverride;

        public float FloorAreaSqFt => Bounds.size.x * Bounds.size.z / (Units.Foot * Units.Foot);
        public float RequiredCfm => RequiredCfmOverride > 0 ? RequiredCfmOverride : FloorAreaSqFt * CfmPerSqFt;

        public static RoomZone Create(string name, Bounds b, float requiredCfm = 0f)
        {
            var go = new GameObject("Room " + name);
            go.transform.position = b.center;
            var z = go.AddComponent<RoomZone>();
            z.RoomName = name; z.Bounds = b; z.RequiredCfmOverride = requiredCfm;
            return z;
        }

        /// <summary>Room containing p (registers sit in the ceiling/floor plane, so allow a few inches beyond the bounds).</summary>
        public static RoomZone At(Vector3 p)
        {
            RoomZone best = null; float bestVol = float.MaxValue;
            foreach (var z in All)
            {
                if (z == null) continue;
                var b = z.Bounds; b.Expand(new Vector3(0.02f, Units.In(6f), 0.02f));
                if (!b.Contains(p)) continue;
                float vol = z.Bounds.size.x * z.Bounds.size.y * z.Bounds.size.z;
                if (vol < bestVol) { bestVol = vol; best = z; }
            }
            return best;
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { All.Clear(); }
    }

    /// <summary>The house volume (rooms + attic + mechanical closet). Anything outside is "outdoors" for condensate / exhaust.</summary>
    public class HvacEnvelope : MonoBehaviour
    {
        public static readonly List<HvacEnvelope> All = new List<HvacEnvelope>();
        public Bounds Bounds;

        public static HvacEnvelope Create(Bounds b)
        {
            var go = new GameObject("House envelope");
            go.transform.position = b.center;
            var e = go.AddComponent<HvacEnvelope>(); e.Bounds = b;
            return e;
        }

        /// <summary>True when p is outdoors. Without an envelope: outside every room and below 6' (not in an attic).</summary>
        public static bool Outside(Vector3 p)
        {
            bool any = false;
            foreach (var e in All) { if (e == null) continue; any = true; if (e.Bounds.Contains(p)) return false; }
            if (any) return true;
            return RoomZone.At(p) == null && p.y < 6 * Units.Foot;
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { All.Clear(); }
    }

    // ===================================================================================== report types

    public enum TerminalKind { Boot, OpenEnd, ReturnGrille }

    /// <summary>Where supply air leaves the duct system.</summary>
    public class AirTerminal
    {
        public TerminalKind Kind;
        public NetNode Node;           // boot / grille (null for an open run end)
        public NetRun OpenRun;         // run whose free end dumps air
        public Vector3 Pos;
        public float Capacity, Flow, Delivered;
        public RoomZone Room;
        public bool HasRegister;
        public readonly List<NetPort> Joints = new List<NetPort>(); // un-taped joints the air passes on the way
        public string Label => Kind == TerminalKind.Boot ? (Node != null ? Node.Label : "boot") : Kind == TerminalKind.OpenEnd ? "open duct end" : "return grille";
    }

    public class UnitAirflow
    {
        public HvacEquipment Unit;
        public float RatedCfm, Delivered, Leaked, Dumped;
        public readonly List<AirTerminal> Terminals = new List<AirTerminal>();
        public readonly List<NetNode> ReturnGrilles = new List<NetNode>();
        public bool ReturnConnected => ReturnGrilles.Count > 0;
        public bool SupplyConnected;
    }

    public class RoomAirflow
    {
        public RoomZone Room;
        public float Required, Delivered;
        public int Boots, Registers;
        public bool ReturnInRoom, ReturnPath;
        public bool Ok => Delivered >= Required * 0.9f - 0.01f;
        public float Percent => Required > 0 ? Delivered / Required * 100f : 100f;
    }

    public class LineSetStatus
    {
        public HvacEquipment Condenser;
        public NetNode Coil;            // coil / air handler reached
        public bool Liquid, Suction, Insulated = true, Brazed = true;
        public bool Ok => Liquid && Suction && Insulated && Brazed && Coil != null;
        public string Note;
    }

    public class CondensateStatus
    {
        public NetNode Source;
        public bool Connected, Sloped = true, Terminated, Glued = true;
        public float MinSlope = float.PositiveInfinity;
        public Vector3 BadPoint, EndPoint;
        public bool Ok => Connected && Sloped && Terminated && Glued;
        public string Note;
    }

    public class ExhaustStatus
    {
        public HvacEquipment Fan;
        public NetNode Cap;
        public bool Ducted, Outside;
        public Vector3 EndPoint;
        public bool Ok => Ducted && Outside;
        public string Note;
    }

    public class HvacReport
    {
        public readonly List<UnitAirflow> Units = new List<UnitAirflow>();
        public readonly List<RoomAirflow> Rooms = new List<RoomAirflow>();
        public readonly List<NetPort> UntapedDuctJoints = new List<NetPort>();
        public readonly List<LineSetStatus> LineSets = new List<LineSetStatus>();
        public readonly List<CondensateStatus> Condensate = new List<CondensateStatus>();
        public readonly List<ExhaustStatus> Exhaust = new List<ExhaustStatus>();
        public readonly List<NetNode> DisconnectedBoots = new List<NetNode>();
        public float TotalLeaked;
        public RoomAirflow Room(string name) => Rooms.Find(r => r.Room != null && r.Room.RoomName == name);
    }

    // ===================================================================================== simulation

    /// <summary>Simple steady-state airflow model. From each furnace/air handler supply: walk plenum → trunk → takeoffs →
    /// branches → boots. Each terminal's capacity = smallest duct on its path (Manual D-ish CFM per size); the unit's rated CFM
    /// is shared by capacity and capped at it. Every un-taped joint passed leaks 10–15 %; open ends dump air.
    /// Also checks return path, line sets, condensate drains and exhaust fan terminations.</summary>
    public static class AirflowSim
    {
        static HvacReport _cache; static int _cacheVer = -1, _cacheFrame = -1;

        /// <summary>Cached per frame + graph version (cheap to call from OnGUI).</summary>
        public static HvacReport Current
        {
            get
            {
                if (_cache == null || _cacheVer != NetGraph.Version || _cacheFrame != Time.frameCount) { _cache = Evaluate(); _cacheVer = NetGraph.Version; _cacheFrame = Time.frameCount; }
                return _cache;
            }
        }

        /// <summary>Leak fraction of one un-taped joint (10–15 %, fixed per joint).</summary>
        public static float LeakOf(NetPort p)
        {
            int h = (p.Label != null ? p.Label.GetHashCode() : 0) ^ (p.Node != null ? Mathf.RoundToInt(p.Node.transform.position.x * 997f + p.Node.transform.position.z * 131f) : 0);
            return 0.10f + 0.05f * ((h & 0x7fff) / 32767f);
        }

        public static HvacReport Evaluate()
        {
            var rep = new HvacReport();
            foreach (var n in NetGraph.NodesOf(Trade.Hvac))
            {
                if (n == null) continue;
                foreach (var p in n.Ports) if (p.Kind == NetKind.Duct && p.NeedsJoin) rep.UntapedDuctJoints.Add(p);
                if (n.Type == "boot" && n.Port("collar") != null && n.Port("collar").Run == null) rep.DisconnectedBoots.Add(n);
            }

            foreach (var eq in HvacEquipment.All)
            {
                if (eq == null || eq.Node == null) continue;
                if (eq.Kind == HvacKind.Furnace || eq.Kind == HvacKind.AirHandler) rep.Units.Add(SupplySide(eq));
            }
            foreach (var u in rep.Units) rep.TotalLeaked += u.Leaked;

            // rooms
            var registers = new List<Piece>();
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (HvacParts.IsRegister(p)) registers.Add(p);
            bool anyReturn = rep.Units.Exists(u => u.ReturnConnected);
            foreach (var z in RoomZone.All)
            {
                if (z == null) continue;
                var ra = new RoomAirflow { Room = z, Required = z.RequiredCfm, ReturnPath = anyReturn };
                foreach (var u in rep.Units)
                {
                    foreach (var t in u.Terminals) if (t.Kind == TerminalKind.Boot && t.Room == z) { ra.Delivered += t.Delivered; ra.Boots++; }
                    foreach (var g in u.ReturnGrilles) if (RoomZone.At(g.transform.position) == z) ra.ReturnInRoom = true;
                }
                foreach (var r in registers) if (RoomZone.At(r.transform.position) == z) ra.Registers++;
                rep.Rooms.Add(ra);
            }

            foreach (var eq in HvacEquipment.All)
            {
                if (eq == null || eq.Node == null) continue;
                if (eq.Kind == HvacKind.Condenser) rep.LineSets.Add(LineSet(eq));
                if (eq.Kind == HvacKind.BathFan || eq.Kind == HvacKind.RangeHood) rep.Exhaust.Add(ExhaustOf(eq));
                var cp = eq.Node.Port("condensate");
                if (cp != null && (eq.Kind == HvacKind.Coil || eq.Kind == HvacKind.AirHandler)) rep.Condensate.Add(CondensateOf(eq.Node, cp));
            }
            return rep;
        }

        // ------------------------------------------------------------------ supply walk

        static UnitAirflow SupplySide(HvacEquipment eq)
        {
            var u = new UnitAirflow { Unit = eq, RatedCfm = eq.RatedCfm };
            var node = eq.Node;
            var seen = new HashSet<NetNode> { node };
            var sup = node.Port("supply");
            if (sup != null) Exit(sup, new List<NetPort>(), float.PositiveInfinity, u.Terminals, seen, 0);
            u.SupplyConnected = u.Terminals.Exists(t => t.Kind == TerminalKind.Boot);

            // distribute the blower's rated airflow by branch capacity
            float C = 0f; foreach (var t in u.Terminals) C += t.Capacity;
            foreach (var t in u.Terminals)
            {
                t.Flow = C > 0 ? Mathf.Min(t.Capacity, u.RatedCfm * t.Capacity / C) : 0f;
                float keep = 1f; foreach (var j in t.Joints) keep *= 1f - LeakOf(j);
                t.Delivered = t.Flow * keep;
                u.Leaked += t.Flow - t.Delivered;
                if (t.Kind == TerminalKind.Boot) u.Delivered += t.Delivered; else u.Dumped += t.Delivered;
                if (t.Kind == TerminalKind.Boot) t.Room = RoomZone.At(t.Pos);
            }

            // return side
            var rseen = new HashSet<NetNode> { node };
            var rterms = new List<AirTerminal>();
            foreach (var p in node.Ports)
                if (p.Kind == NetKind.Duct && p.Label.StartsWith("return")) Exit(p, new List<NetPort>(), float.PositiveInfinity, rterms, rseen, 0);
            foreach (var t in rterms) if (t.Kind == TerminalKind.ReturnGrille && t.Node != null) u.ReturnGrilles.Add(t.Node);
            return u;
        }

        static void Exit(NetPort q, List<NetPort> joints, float cap, List<AirTerminal> terms, HashSet<NetNode> seen, int depth)
        {
            if (depth > 200) return;
            if (q.Run == null)
            {
                var mate = HvacBuild.MateOf(q);
                if (mate != null) { Enter(mate.Node, mate, joints, cap, terms, seen, depth + 1); return; }
                if (q.Capped) return;
                var t = new AirTerminal { Kind = TerminalKind.OpenEnd, Pos = q.WorldPos, Capacity = Mathf.Min(cap, HvacSpecs.RoundCfm(q.NominalIn)) };
                t.Joints.AddRange(joints); terms.Add(t);
                return;
            }
            foreach (var r in q.Runs)
            {
                if (r == null || r.Spec.Kind != NetKind.Duct) continue;
                var j = new List<NetPort>(joints);
                if (q.NeedsJoin) j.Add(q);
                float c = r.Spec.Cfm > 0 ? Mathf.Min(cap, r.Spec.Cfm) : cap;
                var e = r.A == q ? r.B : r.A;
                if (e == null)
                {
                    var t = new AirTerminal { Kind = TerminalKind.OpenEnd, OpenRun = r, Pos = r.A == q ? r.EndPoint : r.StartPoint, Capacity = c };
                    t.Joints.AddRange(j); terms.Add(t);
                    continue;
                }
                if (e.NeedsJoin) j.Add(e);
                Enter(e.Node, e, j, c, terms, seen, depth + 1);
            }
        }

        static void Enter(NetNode n, NetPort via, List<NetPort> joints, float cap, List<AirTerminal> terms, HashSet<NetNode> seen, int depth)
        {
            if (n == null || !seen.Add(n)) return;
            if (n.Type == "boot")
            {
                var col = n.Port("collar");
                var t = new AirTerminal { Kind = TerminalKind.Boot, Node = n, Capacity = Mathf.Min(cap, HvacSpecs.RoundCfm(col != null ? col.NominalIn : 6f)) };
                t.Pos = n.Piece != null ? n.transform.TransformPoint(0, -n.Piece.Size.y / 2, 0) : n.transform.position;
                t.HasRegister = RegisterAt(t.Pos);
                t.Joints.AddRange(joints); terms.Add(t);
                return;
            }
            if (n.Type == "return-grille")
            {
                var t = new AirTerminal { Kind = TerminalKind.ReturnGrille, Node = n, Pos = n.transform.position, Capacity = 0f };
                terms.Add(t);
                return;
            }
            if (n.Type == "furnace" || n.Type == "airhandler" || n.Type == "exhaust-cap" || n.Type == "bathfan" || n.Type == "rangehood") return;
            foreach (var p in n.Ports)
                if (p != via && p.Kind == NetKind.Duct) Exit(p, joints, cap, terms, seen, depth + 1);
        }

        static bool RegisterAt(Vector3 face)
        {
            foreach (var c in Physics.OverlapSphere(face, Units.In(2f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var p = c.GetComponentInParent<Piece>();
                if (HvacParts.IsRegister(p)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ line set / condensate / exhaust

        /// <summary>Walks refrigerant runs from a port; returns the far port (on a coil/air handler) or null, collecting runs.</summary>
        static NetPort FollowPipe(NetPort start, NetKind kind, List<NetRun> runs, List<NetPort> joints, out Vector3 freeEnd)
        {
            freeEnd = start.WorldPos;
            var q = start; var seen = new HashSet<NetNode> { start.Node };
            for (int guard = 0; guard < 64; guard++)
            {
                if (q.NeedsJoin) joints.Add(q);
                NetRun r = null; foreach (var x in q.Runs) if (x != null && x.Spec.Kind == kind && !runs.Contains(x)) { r = x; break; }
                if (r == null) return null;
                runs.Add(r);
                var e = r.A == q ? r.B : r.A;
                if (e == null) { freeEnd = r.A == q ? r.EndPoint : r.StartPoint; return null; }
                if (e.NeedsJoin) joints.Add(e);
                var n = e.Node;
                if (n == null || !seen.Add(n)) return null;
                if (n.Type != "elbow" && n.Type != "coupling") return e;
                // pass through a 2-port fitting (elbow / coupling)
                NetPort next = null; foreach (var p in n.Ports) if (p != e) { next = p; break; }
                if (next == null) return e;
                q = next;
            }
            return null;
        }

        static LineSetStatus LineSet(HvacEquipment cond)
        {
            var st = new LineSetStatus { Condenser = cond };
            var n = cond.Node;
            NetNode liqCoil = null, sucCoil = null;
            var liq = n.Port("liquid"); var suc = n.Port("suction");
            if (liq != null)
            {
                var runs = new List<NetRun>(); var joints = new List<NetPort>();
                var far = FollowPipe(liq, NetKind.Refrigerant, runs, joints, out _);
                st.Liquid = far != null && far.Label == "liquid" && far.Node != null && (far.Node.Type == "coil" || far.Node.Type == "airhandler");
                if (st.Liquid) liqCoil = far.Node;
                if (joints.Count > 0) st.Brazed = false;
            }
            if (suc != null)
            {
                var runs = new List<NetRun>(); var joints = new List<NetPort>();
                var far = FollowPipe(suc, NetKind.Refrigerant, runs, joints, out _);
                st.Suction = far != null && far.Label == "suction" && far.Node != null && (far.Node.Type == "coil" || far.Node.Type == "airhandler");
                if (st.Suction) sucCoil = far.Node;
                foreach (var r in runs) if (!HvacSpecs.Insulated.Contains(r.Spec.Id)) st.Insulated = false;
                if (runs.Count == 0) st.Insulated = false;
                if (joints.Count > 0) st.Brazed = false;
            }
            st.Coil = liqCoil != null && liqCoil == sucCoil ? liqCoil : null;
            var notes = new List<string>();
            if (!st.Liquid) notes.Add("liquid line not connected to a coil");
            if (!st.Suction) notes.Add("suction line not connected to a coil");
            if (st.Liquid && st.Suction && st.Coil == null) notes.Add("lines go to different coils");
            if (!st.Insulated) notes.Add("suction line not insulated");
            if (!st.Brazed) notes.Add("joints not brazed");
            st.Note = notes.Count == 0 ? "OK" : string.Join(", ", notes);
            return st;
        }

        static CondensateStatus CondensateOf(NetNode src, NetPort port)
        {
            var st = new CondensateStatus { Source = src };
            var runs = new List<NetRun>(); var joints = new List<NetPort>();
            var far = FollowPipe(port, NetKind.Condensate, runs, joints, out Vector3 end);
            st.Connected = runs.Count > 0;
            st.Glued = joints.Count == 0;
            st.EndPoint = end;
            // slope of each horizontal leg in the direction of flow
            var at = port;
            foreach (var r in runs)
            {
                bool forward = r.A != null ? r.A.Node == at.Node : (r.B == null || r.B.Node != at.Node);
                for (int i = 0; i + 1 < r.Points.Count; i++)
                {
                    if (!r.SegmentIsHorizontal(i)) continue;
                    float sl = r.SlopeInPerFt(i); if (!forward) sl = -sl;
                    if (sl < st.MinSlope) st.MinSlope = sl;
                    if (sl < 0.125f - 0.005f && st.Sloped) { st.Sloped = false; st.BadPoint = (r.Points[i] + r.Points[i + 1]) * 0.5f; }
                }
                var other = forward ? r.B : r.A;
                if (other != null) { var nn = other.Node; NetPort next = null; if (nn != null) foreach (var p in nn.Ports) if (p != other) { next = p; break; } if (next != null) at = next; }
            }
            if (far != null) st.Terminated = far.Node != null && far.Node.Trade == Trade.Plumbing;
            else if (st.Connected) st.Terminated = HvacEnvelope.Outside(end) || NearDrain(end);
            var notes = new List<string>();
            if (!st.Connected) notes.Add("no condensate drain");
            else
            {
                if (!st.Sloped) notes.Add($"too flat ({MepAim.SlopeLabel(st.MinSlope)}, need 1/8\"/ft)");
                if (!st.Terminated) notes.Add("drain ends inside the house");
                if (!st.Glued) notes.Add("PVC joints not glued");
            }
            st.Note = notes.Count == 0 ? $"OK, slope ≥ {MepAim.SlopeLabel(st.MinSlope)}" : string.Join(", ", notes);
            return st;
        }

        static bool NearDrain(Vector3 p)
        {
            foreach (var n in NetGraph.NodesOf(Trade.Plumbing))
                foreach (var q in n.Ports)
                    if (q.Kind == NetKind.Drain && (q.WorldPos - p).sqrMagnitude < 0.3f * 0.3f) return true;
            return false;
        }

        static ExhaustStatus ExhaustOf(HvacEquipment fan)
        {
            var st = new ExhaustStatus { Fan = fan };
            var ex = fan.Node.Port("exhaust");
            if (ex == null) { st.Note = "no exhaust collar"; return st; }
            var runs = new List<NetRun>(); var joints = new List<NetPort>();
            var far = FollowPipe(ex, NetKind.Duct, runs, joints, out Vector3 end);
            st.Ducted = runs.Count > 0;
            st.EndPoint = far != null ? far.WorldPos : end;
            if (far != null && far.Node != null && far.Node.Type == "exhaust-cap")
            {
                st.Cap = far.Node;
                Vector3 capPos = far.Node.transform.position;
                st.Outside = HvacEnvelope.Outside(capPos) || HvacEnvelope.All.Count == 0;
            }
            if (!st.Ducted) st.Note = "not ducted";
            else if (st.Cap == null) st.Note = HvacEnvelope.Outside(st.EndPoint) ? "duct ends outside without a cap" : "duct dumps into the attic / house";
            else if (!st.Outside) st.Note = "cap is inside the house";
            else st.Note = "OK, vented outside at " + st.Cap.Label;
            if (st.Cap == null) st.Outside = false;
            return st;
        }
    }
}
