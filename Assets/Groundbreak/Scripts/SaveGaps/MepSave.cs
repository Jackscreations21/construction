using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Saves the MEP networks (save key "mep"): every run (pipe / cable / duct: spec, points, staples, tags, both end
    /// connections), every node that is not a catalog piece (auto-inserted elbows / tees / wyes / caps, reducing fittings,
    /// duct takeoffs and end caps) and the per-port state of all nodes (joint made, test cap, adaptive join), bored holes in
    /// framing, electrical box make-up / devices / plates, panel breakers (and the home run each is landed on), the meter,
    /// pressure-gauge test state, the DWV water test and the last rough-inspection results.
    /// Fixtures, boxes, panels and equipment are catalog pieces (saved by SaveSystem); their ports are rebuilt by
    /// <see cref="MepAutoAttach"/> and found again by definition id + position.
    /// </summary>
    public static class MepSave
    {
        public const string Key = "mep";

        [Serializable] class PortData { public string label; public Vector3 lp, ld; public int kind, join, maxRuns = 1; public float nom; public bool joined, capped; }
        [Serializable] class NodeData
        {
            public bool onPiece; public SaveGaps.PieceRef piece;
            public string type, display, spec; public int trade;
            public Vector3 pos; public Quaternion rot = Quaternion.identity;
            public List<PortData> ports = new List<PortData>();
        }
        [Serializable] class RunData
        {
            public string spec; public List<Vector3> pts = new List<Vector3>(); public List<float> staples = new List<float>(); public List<string> tags = new List<string>();
            public int nodeA = -1, portA = -1, nodeB = -1, portB = -1;
        }
        [Serializable] class BoxData { public int node; public bool stripped, grounds, neutrals, hots, switchOn = true, plate, wet, countertop; public int device; public float plugW; }
        [Serializable] class BreakerData { public string defId, label; public int slot, amps, poles, type, run = -1; public bool on = true, tripped; }
        [Serializable] class PanelData { public int node; public bool main, cover; public List<BreakerData> breakers = new List<BreakerData>(); }
        [Serializable] class MeterData { public int node; public bool utility; }
        [Serializable] class GaugeData { public int node; public float psi, testPsi = 100f, minutes; public bool air; public int state; public string status; }
        [Serializable] class HoleData { public SaveGaps.PieceRef piece; public bool bearing; public List<Vector3> pos = new List<Vector3>(), axis = new List<Vector3>(); public List<float> dia = new List<float>(); public List<string> bit = new List<string>(); }
        [Serializable]
        class Data
        {
            public int version = 1;
            public List<NodeData> nodes = new List<NodeData>();
            public List<RunData> runs = new List<RunData>();
            public List<BoxData> boxes = new List<BoxData>();
            public List<PanelData> panels = new List<PanelData>();
            public List<MeterData> meters = new List<MeterData>();
            public List<GaugeData> gauges = new List<GaugeData>();
            public List<HoleData> holes = new List<HoleData>();
            public int drainState; public float drainMinutes, drainHead; public string drainStatus = "Not filled";
            public List<int> passedTrade = new List<int>(); public List<bool> passed = new List<bool>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => SaveSystem.RegisterExtension(Key, Save, Load);

        static float I(float x) => Units.In(x);

        // ================================================================== save

        static bool Saved(Piece p) => p != null && p.gameObject.activeInHierarchy && p.GetComponentInParent<MaterialPile>() == null;

        static string Save()
        {
            var d = new Data();
            var index = new Dictionary<NetNode, int>();
            foreach (var n in NetGraph.Nodes)
            {
                if (n == null || !n.isActiveAndEnabled) continue;
                var piece = n.GetComponent<Piece>();
                if (piece != null && !Saved(piece)) continue;
                var nd = new NodeData
                {
                    onPiece = piece != null, piece = SaveGaps.Ref(piece), type = n.Type, display = n.DisplayName, trade = (int)n.Trade,
                    pos = n.transform.position, rot = n.transform.rotation, spec = SpecOf(n),
                };
                foreach (var p in n.Ports)
                    nd.ports.Add(new PortData { label = p.Label, lp = p.LocalPos, ld = p.LocalDir, kind = (int)p.Kind, join = (int)p.Join, maxRuns = p.MaxRuns, nom = p.NominalIn, joined = p.Joined, capped = p.Capped });
                index[n] = d.nodes.Count;
                d.nodes.Add(nd);
            }

            var runIndex = new Dictionary<NetRun, int>();
            foreach (var r in NetGraph.Runs)
            {
                if (r == null || !r.gameObject.activeInHierarchy || r.Spec == null || r.Points.Count < 2) continue;
                var rd = new RunData { spec = r.Spec.Id };
                rd.pts.AddRange(r.Points); rd.staples.AddRange(r.Staples); rd.tags.AddRange(r.Tags);
                End(r.A, index, out rd.nodeA, out rd.portA);
                End(r.B, index, out rd.nodeB, out rd.portB);
                runIndex[r] = d.runs.Count;
                d.runs.Add(rd);
            }

            foreach (var b in ElBox.All)
            {
                if (b == null || b.Node == null || !index.TryGetValue(b.Node, out int ni)) continue;
                d.boxes.Add(new BoxData { node = ni, stripped = b.Stripped, grounds = b.Grounds, neutrals = b.Neutrals, hots = b.Hots, switchOn = b.SwitchOn, plate = b.Plate, wet = b.Wet, countertop = b.Countertop, device = (int)b.Device, plugW = b.PlugW });
            }
            foreach (var pn in ElPanel.All)
            {
                if (pn == null || pn.Node == null || !index.TryGetValue(pn.Node, out int ni)) continue;
                var pd = new PanelData { node = ni, main = pn.Main, cover = pn.Cover };
                foreach (var br in pn.Breakers)
                    pd.breakers.Add(new BreakerData { defId = br.DefId, label = br.Label, slot = br.Slot, amps = br.Amps, poles = br.Poles, type = (int)br.Type, on = br.On, tripped = br.Tripped, run = br.Run != null && runIndex.TryGetValue(br.Run, out int ri) ? ri : -1 });
                d.panels.Add(pd);
            }
            foreach (var m in ElMeter.All)
                if (m != null && m.Node != null && index.TryGetValue(m.Node, out int ni)) d.meters.Add(new MeterData { node = ni, utility = m.Utility });
            foreach (var g in PressureTest.Gauges())
                if (index.TryGetValue(g.Node, out int ni))
                    d.gauges.Add(new GaugeData { node = ni, psi = g.Psi, testPsi = g.TestPsi, minutes = g.Minutes, air = g.Air, state = (int)g.State, status = g.Status });
            foreach (var sh in UnityEngine.Object.FindObjectsByType<StudHoles>(FindObjectsSortMode.None))
            {
                var p = sh != null ? sh.GetComponent<Piece>() : null;
                if (!Saved(p) || sh.Holes.Count == 0) continue;
                var hd = new HoleData { piece = SaveGaps.Ref(p), bearing = sh.Bearing };
                foreach (var h in sh.Holes) { hd.pos.Add(h.LocalPos); hd.axis.Add(h.LocalAxis); hd.dia.Add(h.Dia); hd.bit.Add(h.Bit ?? ""); }
                d.holes.Add(hd);
            }
            d.drainState = (int)DrainTest.State; d.drainMinutes = DrainTest.Minutes; d.drainHead = DrainTest.HeadFt; d.drainStatus = DrainTest.Status;
            foreach (var kv in MepInspection.LastPassed) { d.passedTrade.Add((int)kv.Key); d.passed.Add(kv.Value); }
            return JsonUtility.ToJson(d);
        }

        static void End(NetPort p, Dictionary<NetNode, int> index, out int node, out int port)
        {
            node = -1; port = -1;
            if (p == null || p.Node == null || !index.TryGetValue(p.Node, out node)) { node = -1; return; }
            port = p.Node.Ports.IndexOf(p);
            if (port < 0) node = -1;
        }

        /// <summary>The run spec a free-standing fitting was made for (from a run on it, else the best match of its first port).</summary>
        static string SpecOf(NetNode n)
        {
            foreach (var p in n.Ports) foreach (var r in p.Runs) if (r != null && r.Spec != null && Mathf.Abs(r.Spec.NominalIn - p.NominalIn) < 0.01f) return r.Spec.Id;
            foreach (var p in n.Ports) foreach (var r in p.Runs) if (r != null && r.Spec != null) return r.Spec.Id;
            return n.Ports.Count > 0 ? GuessSpec(n.Trade, n.Ports[0].Kind, n.Ports[0].NominalIn, n.Ports[0].Join)?.Id : null;
        }

        static RunSpec GuessSpec(Trade t, NetKind kind, float nom, JoinMethod join)
        {
            RunSpec best = null;
            foreach (var s in RunSpecs.All)
            {
                if (s.Trade != t || !NetPort.KindsCompatible(s.Kind, kind) || Mathf.Abs(s.NominalIn - nom) > 0.01f) continue;
                if (best == null || (s.Join == join && best.Join != join)) best = s;
            }
            return best;
        }

        // ================================================================== load

        /// <summary>Removes every run and free-standing fitting (load and New Game); piece nodes go with their pieces.</summary>
        static void Wipe()
        {
            foreach (var r in UnityEngine.Object.FindObjectsByType<NetRun>(FindObjectsSortMode.None)) { NetGraph.Runs.Remove(r); SaveGaps.Kill(r.gameObject); }
            NetGraph.Runs.Clear();
            foreach (var n in UnityEngine.Object.FindObjectsByType<NetNode>(FindObjectsSortMode.None))
                if (n != null && n.GetComponent<Piece>() == null) SaveGaps.Kill(n.gameObject);
            MepInspection.ClearMarkers();
            MepInspection.LastFindings.Clear();
            MepInspection.LastPassed.Clear();
            DrainTest.Drain(); DrainTest.Status = "Not filled"; DrainTest.HeadFt = 0f;
        }

        static void Load(string json)
        {
            Wipe();
            if (string.IsNullOrEmpty(json)) { NetGraph.Touch(); return; }
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) { NetGraph.Touch(); return; }

            var pieces = new SaveGaps.PieceIndex();
            var nodes = new NetNode[d.nodes.Count];
            int missing = 0;
            for (int i = 0; i < d.nodes.Count; i++)
            {
                var nd = d.nodes[i];
                try { nodes[i] = nd.onPiece ? PieceNode(nd, pieces) : Rebuild(nd); }
                catch (Exception e) { Debug.LogError($"[Save] MEP node {nd.type} failed to restore: {e}"); }
                if (nodes[i] == null) { missing++; continue; }
                var n = nodes[i];
                if (!string.IsNullOrEmpty(nd.display)) n.DisplayName = nd.display;
                for (int k = 0; k < nd.ports.Count && k < n.Ports.Count; k++)
                {
                    var p = n.Ports[k]; var pd = nd.ports[k];
                    p.Join = (JoinMethod)pd.join; p.NominalIn = pd.nom;
                }
            }

            var runs = new NetRun[d.runs.Count];
            for (int i = 0; i < d.runs.Count; i++)
            {
                var rd = d.runs[i];
                var spec = RunSpecs.Get(rd.spec);
                if (spec == null || rd.pts.Count < 2) { missing++; continue; }
                var a = PortOf(nodes, rd.nodeA, rd.portA); var b = PortOf(nodes, rd.nodeB, rd.portB);
                var r = NetRun.Create(spec, rd.pts, a, b);
                // Attach moved the end points onto the ports; put the saved (identical up to float noise) points back exactly
                for (int k = 0; k < rd.pts.Count && k < r.Points.Count; k++) r.Points[k] = rd.pts[k];
                foreach (var t in rd.tags) r.Tags.Add(t);
                r.Staples.AddRange(rd.staples);
                r.Rebuild();
                runs[i] = r;
            }

            // joint / test-cap state once the runs are on their ports (Attach resets Joined)
            for (int i = 0; i < d.nodes.Count; i++)
            {
                var n = nodes[i]; if (n == null) continue;
                var nd = d.nodes[i];
                for (int k = 0; k < nd.ports.Count && k < n.Ports.Count; k++)
                {
                    var p = n.Ports[k]; var pd = nd.ports[k];
                    p.Joined = pd.joined;
                    if (pd.capped != p.Capped)
                    {
                        if (n.Trade == Trade.Plumbing && p.Run == null) { if (pd.capped) PlumbParts.CapPort(p); else PlumbParts.UncapPort(p); }
                        else p.Capped = pd.capped;
                    }
                }
                n.RefreshJoints();
            }

            foreach (var bd in d.boxes)
            {
                var b = Node(nodes, bd.node)?.GetComponent<ElBox>(); if (b == null) continue;
                b.Stripped = bd.stripped; b.Grounds = bd.grounds; b.Neutrals = bd.neutrals; b.Hots = bd.hots; b.SwitchOn = bd.switchOn;
                b.Plate = bd.plate; b.Wet = bd.wet; b.Countertop = bd.countertop; b.Device = (ElDevice)bd.device; b.PlugW = bd.plugW;
                b.Changed();
            }
            foreach (var pd in d.panels)
            {
                var pn = Node(nodes, pd.node)?.GetComponent<ElPanel>(); if (pn == null) continue;
                pn.Breakers.Clear();
                foreach (var br in pd.breakers)
                    pn.Breakers.Add(new ElBreaker
                    {
                        DefId = br.defId, Label = br.label ?? "", Slot = br.slot, Amps = br.amps, Poles = br.poles, Type = (ElCatalog.BreakerType)br.type,
                        On = br.on, Tripped = br.tripped, Run = br.run >= 0 && br.run < runs.Length ? runs[br.run] : null,
                    });
                pn.Main = pd.main; pn.Cover = pd.cover;
                pn.Touch();
            }
            foreach (var md in d.meters) { var m = Node(nodes, md.node)?.GetComponent<ElMeter>(); if (m != null) m.Utility = md.utility; }

            foreach (var hd in d.holes)
            {
                var p = pieces.Find(hd.piece); if (p == null) { missing++; continue; }
                var sh = p.GetComponent<StudHoles>();
                if (sh == null) sh = p.gameObject.AddComponent<StudHoles>();
                sh.Holes.Clear(); sh.Bearing = hd.bearing;
                for (int k = 0; k < hd.pos.Count; k++)
                    sh.Holes.Add(new BoredHole { LocalPos = hd.pos[k], LocalAxis = k < hd.axis.Count ? hd.axis[k] : Vector3.right, Dia = k < hd.dia.Count ? hd.dia[k] : I(0.75f), Bit = k < hd.bit.Count ? hd.bit[k] : "" });
                sh.Rebuild();
            }

            DrainTest.State = (DrainTest.TestState)d.drainState; DrainTest.Minutes = d.drainMinutes; DrainTest.HeadFt = d.drainHead; DrainTest.Status = d.drainStatus ?? "Not filled";
            for (int i = 0; i < d.passedTrade.Count && i < d.passed.Count; i++) MepInspection.LastPassed[(Trade)d.passedTrade[i]] = d.passed[i];

            NetGraph.Touch();
            foreach (var gd in d.gauges)
            {
                var g = Node(nodes, gd.node)?.GetComponent<PressureGauge>(); if (g == null) continue;
                g.Psi = gd.psi; g.TestPsi = gd.testPsi; g.Minutes = gd.minutes; g.Air = gd.air; g.State = (PressureGauge.TestState)gd.state; g.Status = gd.status ?? "";
                if (g.State == PressureGauge.TestState.Passed) g.PassedVersion = NetGraph.Version;
                g.UpdateNeedle();
            }
            if (missing > 0) Debug.LogWarning($"[Save] MEP: {missing} node(s)/run(s)/hole set(s) could not be restored");
        }

        static NetNode Node(NetNode[] nodes, int i) => i >= 0 && i < nodes.Length ? nodes[i] : null;
        static NetPort PortOf(NetNode[] nodes, int n, int p)
        {
            var node = Node(nodes, n);
            return node != null && p >= 0 && p < node.Ports.Count ? node.Ports[p] : null;
        }

        static NetNode PieceNode(NodeData nd, SaveGaps.PieceIndex pieces)
        {
            var p = pieces.Find(nd.piece);
            if (p == null) return null;
            MepAutoAttach.AttachNow(p);
            foreach (var n in p.GetComponents<NetNode>()) if (n.Type == nd.type) return n;
            return p.GetComponent<NetNode>();
        }

        // ------------------------------------------------------------------ free-standing nodes

        static NetNode Rebuild(NodeData nd)
        {
            var spec = RunSpecs.Get(nd.spec) ?? (nd.ports.Count > 0 ? GuessSpec((Trade)nd.trade, (NetKind)nd.ports[0].kind, nd.ports[0].nom, (JoinMethod)nd.ports[0].join) : null);
            switch (nd.type)
            {
                case "elbow": case "tee": case "santee": case "wye": case "coupling": case "cap":
                    if (spec != null && nd.ports.Count > 0) return Fitting(nd, spec);
                    break;
                case "takeoff":
                    if (spec != null) return Takeoff(nd, spec);
                    break;
                case "endcap":
                    if (spec != null) return EndCap(nd, spec);
                    break;
            }
            return Generic(nd);
        }

        static NetNode Fitting(NodeData nd, RunSpec spec)
        {
            var dirs = new Vector3[nd.ports.Count];
            for (int i = 0; i < dirs.Length; i++) dirs[i] = nd.rot * nd.ports[i].ld;
            int bi = -1;
            for (int i = 0; i < nd.ports.Count; i++) if (Mathf.Abs(nd.ports[i].nom - spec.NominalIn) > 0.01f) { bi = i; break; }
            if (bi >= 0)
            {
                var br = GuessSpec(spec.Trade, spec.Kind, nd.ports[bi].nom, spec.Join);
                if (br != null) return PlumbFit.Create(nd.type, spec, nd.pos, dirs, br, bi);
            }
            float angle = dirs.Length == 2 ? Vector3.Angle(-dirs[0], dirs[1]) : 90f;
            return Fittings.Create(nd.type, spec, nd.pos, dirs, angle);
        }

        /// <summary>A starting collar on a rectangular trunk (same body as <see cref="HvacBuild.Takeoff"/>, without cutting the trunk).</summary>
        static NetNode Takeoff(NodeData nd, RunSpec spec)
        {
            var collar = nd.ports.Find(p => p.label == "collar");
            float collarIn = collar != null && collar.nom > 0f ? collar.nom : 6f;
            float off = collar != null ? collar.lp.y - I(2.5f) : spec.Height * 0.5f;
            var go = new GameObject($"Takeoff {collarIn:0}\"");
            go.transform.SetPositionAndRotation(nd.pos, nd.rot);
            var node = go.AddComponent<NetNode>();
            node.Type = "takeoff"; node.Trade = Trade.Hvac;
            node.DisplayName = $"{collarIn:0}\" starting collar (takeoff)";
            node.AddPort("in", new Vector3(0, 0, -0.0015f), Vector3.back, NetKind.Duct, spec.NominalIn);
            node.AddPort("out", new Vector3(0, 0, 0.0015f), Vector3.forward, NetKind.Duct, spec.NominalIn);
            node.AddPort("collar", new Vector3(0, off + I(2.5f), 0), Vector3.up, NetKind.Duct, collarIn, JoinMethod.Tape);
            float r = I(collarIn) / 2;
            HvacModels.Spawn($"takeoff|{collarIn}|{Mathf.RoundToInt(off * 1000)}", go.transform, k => HvacModels.Takeoff(k, off, r));
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, off + I(1.25f), 0); bc.size = new Vector3(2 * r + I(1f), I(2.5f), 2 * r + I(1f));
            return node;
        }

        /// <summary>A sheet-metal end cap on a duct end (same body as <see cref="HvacBuild.EndCap"/>).</summary>
        static NetNode EndCap(NodeData nd, RunSpec spec)
        {
            var go = new GameObject("Duct end cap");
            go.transform.SetPositionAndRotation(nd.pos, nd.rot);
            var node = go.AddComponent<NetNode>();
            node.Type = "endcap"; node.Trade = Trade.Hvac;
            node.DisplayName = (spec.Shape == RunShape.Rect ? $"{Units.ToInches(spec.Height):0}x{Units.ToInches(spec.Width):0}" : $"{spec.NominalIn:0}\"") + " end cap";
            node.AddPort("p0", Vector3.zero, Vector3.back, NetKind.Duct, spec.NominalIn, JoinMethod.Tape);
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
            return node;
        }

        /// <summary>Unknown node type (a future trade part): ports only, so the network still connects.</summary>
        static NetNode Generic(NodeData nd)
        {
            var go = new GameObject("Fitting " + nd.type);
            go.transform.SetPositionAndRotation(nd.pos, nd.rot);
            var node = go.AddComponent<NetNode>();
            node.Type = nd.type; node.Trade = (Trade)nd.trade;
            foreach (var p in nd.ports) node.AddPort(p.label, p.lp, p.ld, (NetKind)p.kind, p.nom, (JoinMethod)p.join, Mathf.Max(1, p.maxRuns));
            var col = go.AddComponent<SphereCollider>(); col.radius = 0.03f;
            return node;
        }
    }
}
