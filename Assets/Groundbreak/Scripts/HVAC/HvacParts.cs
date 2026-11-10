using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum HvacKind { Furnace, AirHandler, Condenser, Coil, BathFan, RangeHood }

    /// <summary>Equipment data on an HVAC catalog piece (rated airflow / tonnage).</summary>
    public class HvacEquipment : MonoBehaviour
    {
        public HvacKind Kind;
        public float RatedCfm;
        public float Tons;
        public NetNode Node => GetComponent<NetNode>();
        public static readonly List<HvacEquipment> All = new List<HvacEquipment>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { All.Clear(); }
    }

    /// <summary>Attaches NetNode ports + models to HVAC equipment and parts (MepAutoAttach).</summary>
    public static class HvacParts
    {
        public const float FurnaceCfm = 1200f, AirHandlerCfm = 1200f, BathFanCfm = 80f, RangeHoodCfm = 300f;
        static float I(float i) => i * Units.Inch;
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        public static bool IsRegister(Piece p) => p != null && p.Def != null && (p.Def.Id == HvacSpecs.Reg4x10 || p.Def.Id == HvacSpecs.Reg4x12);
        public static bool IsThermostat(Piece p) => p != null && p.Def != null && p.Def.Id == HvacSpecs.Thermostat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            MepAutoAttach.Register("hvac-furnace80", Furnace);
            MepAutoAttach.Register("hvac-airhandler", AirHandler);
            MepAutoAttach.Register("hvac-ac3t", Condenser);
            MepAutoAttach.Register("hvac-hp3t", Condenser);
            MepAutoAttach.Register("hvac-bathfan", BathFan);
            MepAutoAttach.Register("hvac-rangehood", RangeHood);

            MepAutoAttach.Register(HvacSpecs.Coil, Coil);
            MepAutoAttach.Register(HvacSpecs.SupplyPlenum, SupplyPlenum);
            MepAutoAttach.Register(HvacSpecs.ReturnPlenum, ReturnPlenum);
            MepAutoAttach.Register(HvacSpecs.Boot4x10, p => Boot(p, 10f));
            MepAutoAttach.Register(HvacSpecs.Boot4x12, p => Boot(p, 12f));
            MepAutoAttach.Register(HvacSpecs.Reg4x10, p => HvacModels.Apply(p, (k, s) => HvacModels.Register(k, s, 10f)));
            MepAutoAttach.Register(HvacSpecs.Reg4x12, p => HvacModels.Apply(p, (k, s) => HvacModels.Register(k, s, 12f)));
            MepAutoAttach.Register(HvacSpecs.Grille14x20, Grille);
            MepAutoAttach.Register(HvacSpecs.Grille20x20, Grille);
            MepAutoAttach.Register(HvacSpecs.WallCap4, p => WallCap(p, 4f));
            MepAutoAttach.Register(HvacSpecs.RoofCap4, p => RoofCap(p, 4f));
            MepAutoAttach.Register(HvacSpecs.RoofCap7, p => RoofCap(p, 7f));
            MepAutoAttach.Register(HvacSpecs.Pad, p => HvacModels.Apply(p, HvacModels.Pad));
            MepAutoAttach.Register(HvacSpecs.Thermostat, p => HvacModels.Apply(p, HvacModels.Thermostat));
            MepAutoAttach.Register(HvacSpecs.Takeoff6, p => HvacModels.Apply(p, (k, s) => HvacModels.Takeoff(k, -s.y / 2, I(3f))));
            foreach (var id in new[] { "duct-8x12-5ft", "duct-8x16-5ft" })
                MepAutoAttach.Register(id, p => HvacModels.Apply(p, (k, s) => HvacModels.DuctSection(k, s, true, false)));
            foreach (var id in new[] { "duct-5-5ft", "duct-6-5ft", "duct-7-5ft", "duct-8-5ft", "duct-4-5ft" })
                MepAutoAttach.Register(id, p => HvacModels.Apply(p, (k, s) => HvacModels.DuctSection(k, s, false, false)));
            MepAutoAttach.Register("dryer-4-8ft", p => HvacModels.Apply(p, (k, s) => HvacModels.DuctSection(k, s, false, true)));
            MepAutoAttach.Register("lineset-38x34-25", p => HvacModels.Apply(p, HvacModels.LineSetCoil));
        }

        static NetNode Node(Piece p, string type, string display = null)
        {
            var n = p.gameObject.AddComponent<NetNode>();
            n.Type = type; n.Trade = Trade.Hvac; n.Piece = p;
            if (display != null) n.DisplayName = display;
            return n;
        }

        static HvacEquipment Equip(Piece p, HvacKind k, float cfm, float tons = 0f)
        {
            var e = p.gameObject.AddComponent<HvacEquipment>();
            e.Kind = k; e.RatedCfm = cfm; e.Tons = tons;
            return e;
        }

        // ------------------------------------------------------------------ equipment

        /// <summary>80k BTU upflow gas furnace: supply opening on top (plenum/coil mount), side return (left), bottom return blanked.</summary>
        public static void Furnace(Piece p)
        {
            var s = p.Size; float hx = s.x / 2, hy = s.y / 2;
            var n = Node(p, "furnace");
            Equip(p, HvacKind.Furnace, FurnaceCfm, 3f);
            n.AddPort("supply", V(0, hy - I(1.5f), -I(3.75f)), Vector3.up, NetKind.Duct, 0f);
            n.AddPort("return", V(-hx, -hy + I(14f), 0), Vector3.left, NetKind.Duct, 0f);
            n.AddPort("return-bottom", V(0, -hy, 0), Vector3.down, NetKind.Duct, 0f).Capped = true; // knock-out left in place
            HvacModels.Apply(p, HvacModels.Furnace);
        }

        /// <summary>ProductModels picks the condenser model for any id containing "ac" (every "hvac-" id). Rebuild the
        /// right generic product model from a copy of the definition with the "hvac-" prefix stripped.</summary>
        static void ProductVisual(Piece p)
        {
            var d = p.Def; string id = d.Id.StartsWith("hvac-") ? d.Id.Substring(5) : d.Id;
            if (id.Contains("ac") || id.Contains("hp")) return;
            var fake = new PieceDefinition { Id = id, Name = d.Name, Category = d.Category, Size = d.Size, Price = d.Price, Color = d.Color };
            MepAutoAttach.ReplaceVisual(p, ProductModels.Build(fake, p.Size, p.transform));
        }

        /// <summary>Multi-position air handler (3-ton coil inside): supply top, return bottom, line set + condensate on the front.</summary>
        public static void AirHandler(Piece p)
        {
            var s = p.Size; float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            float split = -hy + s.y * 0.42f;
            var n = Node(p, "airhandler");
            Equip(p, HvacKind.AirHandler, AirHandlerCfm, 3f);
            ProductVisual(p);
            n.AddPort("supply", V(0, hy, 0), Vector3.up, NetKind.Duct, 0f);
            n.AddPort("return", V(0, -hy, 0), Vector3.down, NetKind.Duct, 0f);
            n.AddPort("suction", V(hx - I(3f), split + I(6f), hz), Vector3.forward, NetKind.Refrigerant, 0.75f, JoinMethod.Sweat);
            n.AddPort("liquid", V(hx - I(3f), split + I(3.5f), hz), Vector3.forward, NetKind.Refrigerant, 0.375f, JoinMethod.Sweat);
            n.AddPort("condensate", V(-hx + I(3f), split + I(2f), hz), Vector3.forward, NetKind.Condensate, 0.75f, JoinMethod.Solvent);
        }

        /// <summary>Condenser / heat pump: brazed service valves (suction 3/4", liquid 3/8") on the right side.</summary>
        public static void Condenser(Piece p)
        {
            var s = p.Size; float hx = s.x / 2, hy = s.y / 2, hz = s.z / 2;
            float servZ0 = hz - I(1.5f) - Mathf.Min(I(10f), s.z * 0.35f), y = -hy + I(5f);
            var n = Node(p, "condenser");
            Equip(p, HvacKind.Condenser, 0f, 3f);
            n.AddPort("suction", V(hx, y, servZ0 + I(2.5f)), Vector3.right, NetKind.Refrigerant, 0.75f, JoinMethod.Sweat);
            n.AddPort("liquid", V(hx, y, servZ0 + I(5.5f)), Vector3.right, NetKind.Refrigerant, 0.375f, JoinMethod.Sweat);
        }

        public static void Coil(Piece p)
        {
            var s = p.Size;
            var n = Node(p, "coil");
            Equip(p, HvacKind.Coil, 0f, 3f);
            n.AddPort("bottom", V(0, -s.y / 2, 0), Vector3.down, NetKind.Duct, 0f);
            n.AddPort("top", V(0, s.y / 2, 0), Vector3.up, NetKind.Duct, 0f);
            n.AddPort("suction", HvacModels.CoilSuction(s), Vector3.forward, NetKind.Refrigerant, 0.75f, JoinMethod.Sweat);
            n.AddPort("liquid", HvacModels.CoilLiquid(s), Vector3.forward, NetKind.Refrigerant, 0.375f, JoinMethod.Sweat);
            n.AddPort("condensate", HvacModels.CoilDrain(s), Vector3.forward, NetKind.Condensate, 0.75f, JoinMethod.Solvent);
            HvacModels.Apply(p, HvacModels.Coil);
        }

        public static void BathFan(Piece p)
        {
            var s = p.Size; float hx = s.x / 2, hy = s.y / 2;
            float rr = Mathf.Min(I(1.9f), (s.y - I(1f)) * 0.5f);
            var n = Node(p, "bathfan");
            Equip(p, HvacKind.BathFan, BathFanCfm);
            ProductVisual(p);
            n.AddPort("exhaust", V(hx, hy - rr - I(0.3f), 0), Vector3.right, NetKind.Duct, 4f, JoinMethod.Tape);
        }

        public static void RangeHood(Piece p)
        {
            var s = p.Size; float hy = s.y / 2, hz = s.z / 2;
            var n = Node(p, "rangehood");
            Equip(p, HvacKind.RangeHood, RangeHoodCfm);
            ProductVisual(p);
            Vector3 c = V(0, hy, -hz + I(5f));
            n.AddPort("exhaust", c + V(0, I(2f), 0), Vector3.up, NetKind.Duct, 7f, JoinMethod.Tape);
            var col = HvacModels.Spawn("hood-collar", p.transform, k =>
            {
                int g = k.M(SurfaceKind.Galvanized);
                k.Tube(c, c + V(0, I(2f), 0), I(3.5f), g);
                k.Box(-I(4.5f), I(4.5f), c.y, c.y + I(0.05f), c.z - I(4.5f), c.z + I(4.5f), g);
            }, "Collar");
            col.transform.localPosition = Vector3.zero;
        }

        // ------------------------------------------------------------------ duct parts

        public static void SupplyPlenum(Piece p)
        {
            var s = p.Size;
            var n = Node(p, "plenum-supply");
            n.AddPort("unit", V(0, -s.y / 2, 0), Vector3.down, NetKind.Duct, 0f);
            n.AddPort("trunk", HvacModels.SupplyPlenumTrunkPort(s), Vector3.right, NetKind.Duct, 0f, JoinMethod.Tape);
            HvacModels.Apply(p, HvacModels.SupplyPlenum);
        }

        public static void ReturnPlenum(Piece p)
        {
            var s = p.Size;
            var n = Node(p, "plenum-return");
            n.AddPort("unit", V(s.x / 2, 0, 0), Vector3.right, NetKind.Duct, 0f);
            n.AddPort("duct", V(0, s.y / 2 + I(1f), 0), Vector3.up, NetKind.Duct, 0f, JoinMethod.Tape);
            HvacModels.Apply(p, HvacModels.ReturnPlenum);
        }

        public static void Boot(Piece p, float wIn)
        {
            var s = p.Size;
            var n = Node(p, "boot", $"Register boot 4x{wIn:0} x 6\"");
            n.AddPort("collar", V(0, s.y / 2, 0), Vector3.up, NetKind.Duct, 6f, JoinMethod.Tape);
            HvacModels.Apply(p, (k, sz) => HvacModels.Boot(k, sz, wIn, 6f));
        }

        public static void Grille(Piece p)
        {
            var s = p.Size;
            var n = Node(p, "return-grille");
            n.AddPort("duct", V(0, s.y / 2, 0), Vector3.up, NetKind.Duct, 16f, JoinMethod.Tape);
            HvacModels.Apply(p, HvacModels.ReturnGrille);
        }

        public static void WallCap(Piece p, float dIn)
        {
            var n = Node(p, "exhaust-cap");
            n.AddPort("duct", HvacModels.WallCapPort(p.Size), Vector3.back, NetKind.Duct, dIn, JoinMethod.Tape);
            HvacModels.Apply(p, (k, s) => HvacModels.WallCap(k, s, dIn));
        }

        public static void RoofCap(Piece p, float dIn)
        {
            var n = Node(p, "exhaust-cap");
            n.AddPort("duct", HvacModels.RoofCapPort(p.Size), Vector3.down, NetKind.Duct, dIn, JoinMethod.Tape);
            HvacModels.Apply(p, (k, s) => HvacModels.RoofCap(k, s, dIn));
        }
    }

    /// <summary>Build helpers used by tools and tests: place parts, mount them to equipment, cut in takeoffs, cap trunks, route.</summary>
    public static class HvacBuild
    {
        static float I(float i) => i * Units.Inch;

        public static Piece Place(string defId, Vector3 pos, Quaternion rot)
        {
            var def = Catalog.Get(defId);
            if (def == null) { Debug.LogWarning("[HVAC] unknown catalog id " + defId); return null; }
            var p = Piece.Create(def, 0f, pos, rot);
            MepAutoAttach.AttachNow(p);
            return p;
        }

        public static NetNode NodeOf(Piece p) => p != null ? p.GetComponent<NetNode>() : null;
        public static NetPort Port(Piece p, string label) { var n = NodeOf(p); return n != null ? n.Port(label) : null; }

        /// <summary>Moves/rotates a part so its port sits on the target port facing it (plenum on a furnace, coil under a plenum...).</summary>
        public static void MountTo(Piece part, string label, NetPort target)
        {
            var port = Port(part, label);
            if (port == null || target == null) return;
            Quaternion baseRot = target.Node.transform.rotation;
            part.transform.rotation = baseRot;
            Vector3 wd = part.transform.TransformDirection(port.LocalDir);
            part.transform.rotation = Quaternion.FromToRotation(wd, -target.WorldDir) * baseRot;
            part.transform.position += target.WorldPos - port.WorldPos;
            NetGraph.Touch();
        }

        /// <summary>A port bolted directly against another (no run between): plenum on furnace, coil on furnace.</summary>
        public static NetPort MateOf(NetPort p)
        {
            if (p == null || p.Run != null || p.Kind != NetKind.Duct || p.Node == null) return null;
            Vector3 wp = p.WorldPos, wd = p.WorldDir;
            foreach (var n in NetGraph.Nodes)
            {
                if (n == null || n == p.Node || n.Trade != Trade.Hvac) continue;
                foreach (var q in n.Ports)
                    if (q.Run == null && q.Kind == NetKind.Duct && (q.WorldPos - wp).sqrMagnitude < 0.04f * 0.04f && Vector3.Dot(q.WorldDir, wd) < -0.7f)
                        return q;
            }
            return null;
        }

        /// <summary>Cuts a round starting collar (takeoff) into a rectangular trunk at point, collar facing `face`
        /// (top or side of the trunk). Returns the takeoff node; its "collar" port takes the branch.</summary>
        public static NetNode Takeoff(NetRun trunk, Vector3 point, Vector3 face, float collarIn = 6f)
        {
            if (trunk == null || trunk.Spec.Kind != NetKind.Duct || trunk.Spec.Shape != RunShape.Rect) return null;
            var spec = trunk.Spec;
            float s = trunk.DistanceAlong(point, out _);
            if (s < I(5f) || s > trunk.Length - I(5f)) return null;
            Vector3 c = trunk.PointAt(s, out Vector3 d);
            face -= d * Vector3.Dot(face, d);
            if (face.sqrMagnitude < 1e-6f) return null;
            face.Normalize();
            Vector3 up = Mathf.Abs(d.y) > 0.9f ? Vector3.forward : Vector3.up;
            float off = Mathf.Abs(Vector3.Dot(face, (up - d * Vector3.Dot(up, d)).normalized)) > 0.7f ? spec.Height * 0.5f : spec.Width * 0.5f;
            if (!MepStock.Free && !Game.Stock.TryTake(HvacSpecs.Takeoff6, 1) && !Game.Wallet.TrySpend(9, "Starting collar")) { Game.Toast("No starting collars on site"); return null; }

            var second = trunk.SplitAt(c);
            if (second == null) return null;
            var go = new GameObject($"Takeoff {collarIn:0}\"");
            go.transform.SetPositionAndRotation(c, Quaternion.LookRotation(d, face));
            var node = go.AddComponent<NetNode>();
            node.Type = "takeoff"; node.Trade = Trade.Hvac;
            node.DisplayName = $"{collarIn:0}\" starting collar (takeoff)";
            var pin = node.AddPort("in", new Vector3(0, 0, -0.0015f), Vector3.back, NetKind.Duct, spec.NominalIn);
            var pout = node.AddPort("out", new Vector3(0, 0, 0.0015f), Vector3.forward, NetKind.Duct, spec.NominalIn);
            node.AddPort("collar", new Vector3(0, off + I(2.5f), 0), Vector3.up, NetKind.Duct, collarIn, JoinMethod.Tape);
            trunk.Points[trunk.Points.Count - 1] = pin.WorldPos;
            second.Points[0] = pout.WorldPos;
            trunk.Attach(pin, false);
            second.Attach(pout, true);
            trunk.Rebuild(); second.Rebuild();
            float r = I(collarIn) / 2;
            HvacModels.Spawn($"takeoff|{collarIn}|{Mathf.RoundToInt(off * 1000)}", go.transform, k => HvacModels.Takeoff(k, off, r));
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, off + I(1.25f), 0); bc.size = new Vector3(2 * r + I(1f), I(2.5f), 2 * r + I(1f));
            NetGraph.Touch();
            return node;
        }

        /// <summary>Sheet-metal end cap on a free duct end (screwed; the seam still needs foil tape).</summary>
        public static NetNode EndCap(NetRun run, bool atStart)
        {
            var spec = run.Spec;
            Vector3 p = atStart ? run.StartPoint : run.EndPoint;
            Vector3 d = atStart ? (run.Points[0] - run.Points[1]).normalized : (run.EndPoint - run.Points[run.Points.Count - 2]).normalized;
            var go = new GameObject("Duct end cap");
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(d, Mathf.Abs(d.y) > 0.9f ? Vector3.forward : Vector3.up));
            var node = go.AddComponent<NetNode>();
            node.Type = "endcap"; node.Trade = Trade.Hvac;
            node.DisplayName = (spec.Shape == RunShape.Rect ? $"{Units.ToInches(spec.Height):0}x{Units.ToInches(spec.Width):0}" : $"{spec.NominalIn:0}\"") + " end cap";
            var port = node.AddPort("p0", Vector3.zero, Vector3.back, NetKind.Duct, spec.NominalIn, JoinMethod.Tape);
            run.Attach(port, atStart);
            run.Rebuild();
            float w = spec.Width, h = spec.Shape == RunShape.Rect ? spec.Height : spec.Width;
            bool rect = spec.Shape == RunShape.Rect;
            HvacModels.Spawn($"endcap|{spec.Id}", go.transform, k =>
            {
                int g = k.M(SurfaceKind.Galvanized);
                float t = I(0.06f), L = I(1.5f);
                if (rect)
                {
                    int sides = MeshBuilder.FacePosX | MeshBuilder.FaceNegX | MeshBuilder.FacePosY | MeshBuilder.FaceNegY;
                    k.Box(-w / 2 - t, w / 2 + t, -h / 2 - t, h / 2 + t, -I(0.5f), L - I(0.5f), g, sides);
                    k.Box(-w / 2 - t, w / 2 + t, -h / 2 - t, h / 2 + t, L - I(0.5f) - t, L - I(0.5f), g, MeshBuilder.FacePosZ);
                    k.B.AddBox(new Vector3(0, 0, L - I(0.5f) + 0.0004f), new Vector3(w * 0.9f, I(0.15f), 0.0008f), Quaternion.Euler(0, 0, Mathf.Atan2(h, w) * Mathf.Rad2Deg), g);
                    k.B.AddBox(new Vector3(0, 0, L - I(0.5f) + 0.0004f), new Vector3(w * 0.9f, I(0.15f), 0.0008f), Quaternion.Euler(0, 0, -Mathf.Atan2(h, w) * Mathf.Rad2Deg), g);
                }
                else
                {
                    k.Tube(new Vector3(0, 0, -I(0.5f)), new Vector3(0, 0, L - I(0.5f)), w / 2 + t, g);
                    k.Cyl(new Vector3(0, 0, L - I(0.5f) - t), new Vector3(0, 0, L - I(0.5f)), w / 2 + t, g);
                }
            });
            var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(w, h, I(1.5f));
            NetGraph.Touch();
            return node;
        }

        /// <summary>Nearest HVAC run to a world point (optionally of one spec).</summary>
        public static NetRun RunNear(Vector3 p, string specId = null, float radius = 0.3f)
        {
            NetRun best = null; float bd = radius;
            foreach (var r in NetGraph.RunsOf(Trade.Hvac))
            {
                if (specId != null && r.Spec.Id != specId) continue;
                r.DistanceAlong(p, out float off);
                if (off < bd) { bd = off; best = r; }
            }
            return best;
        }

        /// <summary>Routes a run through world points between two ports (either may be null = free end).</summary>
        public static RouteResult Route(string specId, NetPort from, NetPort to, float slopeInPerFt, params Vector3[] mid)
        {
            var spec = RunSpecs.Get(specId);
            var pts = new List<Vector3>();
            if (from != null) pts.Add(from.WorldPos);
            pts.AddRange(mid);
            if (to != null) pts.Add(to.WorldPos);
            var res = RunBuilder.Route(spec, pts, from, to, spec != null && spec.IsDrain ? slopeInPerFt : 0f);
            foreach (var w in res.Warnings) Debug.Log("[HVAC] route " + specId + ": " + w);
            return res;
        }

        public static RouteResult Route(string specId, NetPort from, NetPort to, params Vector3[] mid) => Route(specId, from, to, 0f, mid);

        /// <summary>Makes every un-made HVAC joint (foil tape on duct, brazing on line set, PVC cement on condensate). Returns count.</summary>
        public static int SealAll()
        {
            int c = 0;
            foreach (var n in new List<NetNode>(NetGraph.NodesOf(Trade.Hvac)))
                foreach (var p in n.Ports)
                    if (p.NeedsJoin) { JoinTool.MakeJoint(p); c++; }
            return c;
        }

        /// <summary>Every un-made HVAC joint.</summary>
        public static List<NetPort> OpenJoints(NetKind? kind = null)
        {
            var l = new List<NetPort>();
            foreach (var n in NetGraph.NodesOf(Trade.Hvac))
                foreach (var p in n.Ports)
                    if (p.NeedsJoin && (!kind.HasValue || p.Kind == kind.Value)) l.Add(p);
            return l;
        }
    }
}
