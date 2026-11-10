using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum ElBoxKind { OneGang, TwoGang, Round }
    public enum ElDevice { None, Receptacle15, Receptacle20, Gfci, Switch, Switch3 }

    /// <summary>A blue PVC new-work box (1-gang / 2-gang wall box or 4" round ceiling box). Tracks box make-up:
    /// cables stripped, grounds spliced + pigtailed, neutrals spliced, hots spliced (wire nuts), device installed.
    /// Wall boxes: front faces local +Z (mount 1/2" proud of the stud face for drywall). Round box: opening faces -Y.</summary>
    public class ElBox : MonoBehaviour
    {
        // real exterior sizes (inches)
        public const float W1g = 2.375f, H1g = 4.0f, D1g = 3.125f, W2g = 4.0f, DRound = 4.0f, HRound = 2.25f;
        public static readonly List<ElBox> All = new List<ElBox>();

        public ElBoxKind Kind;
        public NetNode Node;
        public Piece Piece;

        // make-up
        public bool Stripped, Grounds, Neutrals, Hots;
        public ElDevice Device;
        public bool SwitchOn = true;
        public float PlugW;          // plug load on a receptacle (W)
        public bool Wet;             // bath / kitchen / outdoor / garage: needs GFCI protection
        public bool Countertop;      // kitchen countertop receptacle: 42-44" to center

        // circuit simulation results (written by ElSim every frame)
        [NonSerialized] public ElBreaker Circuit;
        [NonSerialized] public ElPanel CircuitPanel;
        [NonSerialized] public ElBox Parent;
        [NonSerialized] public NetRun Feed;
        [NonSerialized] public bool HotIn, NeutralIn, GroundIn;

        public bool HotOk => HotIn && Stripped && Hots;
        public bool NeutralOk => NeutralIn && Stripped && Neutrals;
        public bool GroundOk => GroundIn && Stripped && Grounds;
        public bool DevicePowered => Device != ElDevice.None && HotOk && NeutralOk;
        public bool IsSwitch => Device == ElDevice.Switch || Device == ElDevice.Switch3;
        public bool IsReceptacle => Device == ElDevice.Receptacle15 || Device == ElDevice.Receptacle20 || Device == ElDevice.Gfci;
        public bool MadeUp => Stripped && Grounds && Neutrals && Hots;

        public float VolumeCuIn => Kind == ElBoxKind.TwoGang ? 32f : 20.3f;
        public float FrontDist => Units.In(Kind == ElBoxKind.Round ? HRound * 0.5f : D1g * 0.5f);
        public Vector3 R => Vector3.right;
        public Vector3 U => Kind == ElBoxKind.Round ? Vector3.forward : Vector3.up;
        public Vector3 F => Kind == ElBoxKind.Round ? Vector3.down : Vector3.forward;
        /// <summary>World point from box frame coordinates in inches (r = right, u = up along the device, f = toward the opening).</summary>
        public Vector3 W(float r, float u, float f) => transform.TransformPoint((R * r + U * u + F * f) * Units.Inch);
        public Vector3 FrontWorld => transform.TransformPoint(F * FrontDist);

        public List<NetRun> Cables
        {
            get
            {
                var l = new List<NetRun>();
                if (Node == null) return l;
                foreach (var r in NetGraph.RunsAt(Node)) if (ElCatalog.IsBranch(r)) l.Add(r);
                return l;
            }
        }

        public string Label
        {
            get
            {
                string k = Kind == ElBoxKind.Round ? "Ceiling box" : Kind == ElBoxKind.TwoGang ? "2-gang box" : "Box";
                string d = Device == ElDevice.None ? "" : " · " + ShortDevice(Device);
                return k + d;
            }
        }

        public static string ShortDevice(ElDevice d)
        {
            switch (d)
            {
                case ElDevice.Receptacle15: return "15A duplex";
                case ElDevice.Receptacle20: return "20A duplex";
                case ElDevice.Gfci: return "GFCI";
                case ElDevice.Switch: return "switch";
                case ElDevice.Switch3: return "3-way";
                default: return "-";
            }
        }

        int _ver, _builtVer = -1, _builtGraph = -1;
        bool _builtSwitch;
        public void Changed() { _ver++; NetGraph.Touch(); }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void LateUpdate()
        {
            if (_builtVer == _ver && _builtGraph == NetGraph.Version && _builtSwitch == SwitchOn) return;
            _builtVer = _ver; _builtGraph = NetGraph.Version; _builtSwitch = SwitchOn;
            ElVisuals.RebuildGuts(this);
        }

        public void RebuildNow() { _builtVer = -1; LateUpdate(); }

        public static void Attach(Piece p, ElBoxKind kind)
        {
            if (p.GetComponent<ElBox>() != null) return;
            var node = p.gameObject.AddComponent<NetNode>();
            node.Trade = Trade.Electrical; node.Piece = p;
            node.Type = kind == ElBoxKind.Round ? "box-round" : kind == ElBoxKind.TwoGang ? "box-2g" : "box-1g";
            var b = p.gameObject.AddComponent<ElBox>();
            b.Kind = kind; b.Node = node; b.Piece = p;
            float I(float x) => Units.In(x);
            if (kind == ElBoxKind.Round)
            {
                float y = I(HRound * 0.5f - 0.2f);
                node.AddPort("ko-n", new Vector3(0, y, I(0.9f)), Vector3.up, NetKind.Power, 0, JoinMethod.None, 2);
                node.AddPort("ko-s", new Vector3(0, y, -I(0.9f)), Vector3.up, NetKind.Power, 0, JoinMethod.None, 2);
                node.AddPort("ko-e", new Vector3(I(0.9f), y, 0), Vector3.up, NetKind.Power, 0, JoinMethod.None, 2);
                node.AddPort("ko-w", new Vector3(-I(0.9f), y, 0), Vector3.up, NetKind.Power, 0, JoinMethod.None, 2);
            }
            else
            {
                float y = I(H1g * 0.5f - 0.2f), z = I(-D1g * 0.5f + 0.875f);
                float[] xs = kind == ElBoxKind.TwoGang ? new[] { -1.35f, -0.45f, 0.45f, 1.35f } : new[] { -0.45f, 0.45f };
                char c = 'a';
                foreach (var x in xs) { node.AddPort("top-" + c, new Vector3(I(x), y, z), Vector3.up, NetKind.Power, 0, JoinMethod.None, 2); c++; }
                c = 'a';
                foreach (var x in xs) { node.AddPort("bot-" + c, new Vector3(I(x), -y, z), Vector3.down, NetKind.Power, 0, JoinMethod.None, 2); c++; }
            }
            ElVisuals.BuildBoxBody(b);
            b.RebuildNow();
        }

        // ------------------------------------------------------------------ make-up steps
        public string Strip()
        {
            if (Cables.Count == 0) return "No cable in this box yet";
            if (Stripped) return "Already stripped";
            Stripped = true; Changed();
            return $"Stripped {Cables.Count} cable(s): sheath off, 3/4\" of insulation off each conductor";
        }

        /// <summary>Next lineman's-pliers step: grounds, then neutrals, then hots. Returns a message, consumes wire nuts.</summary>
        public string SpliceNext()
        {
            if (!Stripped) return "Strip the cables first (wire strippers)";
            int n = Cables.Count;
            bool needNut = n > 1;
            if (needNut && !MepStock.Free && !Game.Stock.TryTake(ElCatalog.WireNuts, 1)) return "Out of wire nuts";
            if (!Grounds) { Grounds = true; Changed(); return n > 1 ? "Grounds spliced + pigtailed (green nut)" : "Ground made up"; }
            if (!Neutrals) { Neutrals = true; Changed(); return n > 1 ? "Neutrals spliced + pigtail" : "Neutral made up"; }
            if (!Hots) { Hots = true; Changed(); return n > 1 ? "Hots spliced + pigtail" : "Hot made up"; }
            if (needNut && !MepStock.Free) Game.Stock.Add(ElCatalog.WireNuts, 1);
            return "Box already made up";
        }

        public void MakeUpAll() { Stripped = true; Grounds = Neutrals = Hots = true; Changed(); }
        public void RemoveGroundSplice() { Grounds = false; Changed(); }

        public string InstallDevice(ElDevice d)
        {
            if (Kind == ElBoxKind.Round) return "Ceiling boxes take a fixture, not a device";
            if (!MadeUp) return "Make up the box first (grounds, neutrals, hots)";
            if (Device != ElDevice.None) return "A device is already installed";
            string id = ElCatalog.DeviceId(d);
            if (id != null && !MepStock.Free && !Game.Stock.TryTake(id, 1)) return "No " + ElCatalog.DeviceName(d) + " in stock";
            Device = d; Changed();
            return "Installed " + ElCatalog.DeviceName(d);
        }

        public string RemoveDevice()
        {
            if (Device == ElDevice.None) return "No device";
            string id = ElCatalog.DeviceId(Device);
            if (id != null && !MepStock.Free) Game.Stock.Add(id, 1);
            Device = ElDevice.None; Changed();
            return "Device removed";
        }

        /// <summary>NEC 314.16 simplified box fill (cubic inches).</summary>
        public float FillCuIn()
        {
            float vol = 0f, largest = 0f;
            foreach (var r in Cables)
            {
                float v = ConductorVol(r.Spec.Awg);
                vol += r.Spec.Conductors * v;
                largest = Mathf.Max(largest, v);
            }
            if (largest > 0) vol += largest;                               // all grounds together = 1
            if (Device != ElDevice.None) vol += 2f * Mathf.Max(largest, 2f); // device yoke = 2
            return vol;
        }

        public static float ConductorVol(int awg)
        {
            switch (awg) { case 14: return 2.0f; case 12: return 2.25f; case 10: return 2.5f; case 8: return 3.0f; case 6: return 5.0f; default: return 2.0f; }
        }

        /// <summary>Height of the box center above the finished floor (first non-framing surface below).</summary>
        public float HeightAboveFloor()
        {
            Vector3 c = transform.position;
            var hits = Physics.RaycastAll(c, Vector3.down, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                var p = h.collider.GetComponentInParent<Piece>();
                if (p != null && (p.Def == null || p.Def.Category == PieceCategory.Lumber || p.Def.Category == PieceCategory.Electrical)) continue;
                if (NetRun.FromCollider(h.collider) != null) continue;
                best = Mathf.Min(best, h.distance);
            }
            return best == float.MaxValue ? c.y : best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); ElPanel.All.Clear(); ElMeter.All.Clear(); ElLoad.All.Clear(); }
    }

    /// <summary>One breaker in a panel space.</summary>
    public class ElBreaker
    {
        public string DefId;
        public int Slot, Amps, Poles;
        public ElCatalog.BreakerType Type;
        public bool On = true, Tripped;
        public NetRun Run;
        public string Label = "";
        // sim
        public float Over, LoadW, LoadA;
        public bool Live;
        public readonly List<ElBox> Boxes = new List<ElBox>();
        public readonly List<ElLoad> Loads = new List<ElLoad>();
        public readonly List<NetRun> Runs = new List<NetRun>();

        public bool Afci => Type == ElCatalog.BreakerType.Afci || Type == ElCatalog.BreakerType.DualFunction;
        public bool Gfci => Type == ElCatalog.BreakerType.Gfci || Type == ElCatalog.BreakerType.DualFunction;
        public string Name => $"{Amps}A {(Poles == 2 ? "2P" : "1P")}{(Type == ElCatalog.BreakerType.Standard ? "" : Type == ElCatalog.BreakerType.DualFunction ? " DF" : " " + Type.ToString().ToUpper())}";
        public string State => Tripped ? "TRIPPED" : On ? "ON" : "OFF";
        public void Reset() { Tripped = false; On = true; Over = 0; NetGraph.Touch(); }
    }

    /// <summary>200 A main-breaker load center: 30 spaces (odd left, even right), main breaker, service + branch knockouts.</summary>
    public class ElPanel : MonoBehaviour
    {
        public static readonly List<ElPanel> All = new List<ElPanel>();
        public const int Spaces = 30;
        public NetNode Node;
        public Piece Piece;
        public bool Main;
        public readonly List<ElBreaker> Breakers = new List<ElBreaker>();
        [NonSerialized] public bool ServiceLive;
        public bool Live => Main && ServiceLive;
        public int Version;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static void Attach(Piece p)
        {
            if (p.GetComponent<ElPanel>() != null) return;
            var node = p.gameObject.AddComponent<NetNode>();
            node.Trade = Trade.Electrical; node.Piece = p; node.Type = "panel";
            var pn = p.gameObject.AddComponent<ElPanel>();
            pn.Node = node; pn.Piece = p;
            Vector3 h = p.Size * 0.5f;
            float zc = -h.z + Units.In(1.75f);
            node.AddPort("service", new Vector3(-Units.In(4.5f), h.y - Units.In(0.3f), zc), Vector3.up, NetKind.Power, 2f, JoinMethod.None, 1);
            node.AddPort("branch-top", new Vector3(Units.In(2.5f), h.y - Units.In(0.3f), zc), Vector3.up, NetKind.Power, 0, JoinMethod.None, 30);
            node.AddPort("branch-bot", new Vector3(Units.In(2.5f), -h.y + Units.In(0.3f), zc), Vector3.down, NetKind.Power, 0, JoinMethod.None, 30);
            pn.Touch();
        }

        public void Touch() { Version++; NetGraph.Touch(); }

        public NetPort ServicePort => Node.Port("service");

        public ElBreaker BreakerAt(int slot)
        {
            foreach (var b in Breakers) if (b.Slot == slot || (b.Poles == 2 && b.Slot + 2 == slot)) return b;
            return null;
        }

        public bool SpaceFree(int slot, int poles)
        {
            if (slot < 1 || slot > Spaces || BreakerAt(slot) != null) return false;
            return poles == 1 || (slot + 2 <= Spaces && BreakerAt(slot + 2) == null);
        }

        public ElBreaker Install(string defId, int slot)
        {
            var d = ElCatalog.Breaker(defId);
            if (d == null) return null;
            var def = d.Value;
            if (!SpaceFree(slot, def.Poles)) return null;
            if (!MepStock.Free && !Game.Stock.TryTake(defId, 1)) return null;
            var b = new ElBreaker { DefId = defId, Slot = slot, Amps = def.Amps, Poles = def.Poles, Type = def.Type, On = true };
            Breakers.Add(b);
            Touch();
            return b;
        }

        public void Remove(ElBreaker b)
        {
            if (!Breakers.Remove(b)) return;
            if (!MepStock.Free) Game.Stock.Add(b.DefId, 1);
            Touch();
        }

        /// <summary>Cables landing in the panel (home runs), service cable excluded.</summary>
        public List<NetRun> HomeRuns
        {
            get
            {
                var l = new List<NetRun>();
                foreach (var p in Node.Ports) if (p.Label != "service") foreach (var r in p.Runs) if (ElCatalog.IsBranch(r) && !l.Contains(r)) l.Add(r);
                return l;
            }
        }

        public ElBreaker BreakerFor(NetRun r) { foreach (var b in Breakers) if (b.Run == r) return b; return null; }

        public List<NetRun> Unlanded() { var l = HomeRuns; l.RemoveAll(r => BreakerFor(r) != null); return l; }

        public string Land(ElBreaker b, NetRun r)
        {
            if (b == null || r == null) return "Nothing to land";
            var other = BreakerFor(r); if (other != null && other != b) other.Run = null;
            b.Run = r;
            r.Tags.Add("homerun");
            Touch();
            string warn = r.Spec.Ampacity + 0.01f < b.Amps ? $" - VIOLATION: {r.Spec.Awg} AWG on a {b.Amps} A breaker" : "";
            return $"Landed {r.Spec.Name} on space {b.Slot} ({b.Name}){warn}";
        }

        public bool ResetTripped()
        {
            bool any = false;
            foreach (var b in Breakers) if (b.Tripped) { b.Reset(); any = true; }
            if (any) Touch();
            return any;
        }

        public void SetMain(bool on) { Main = on; Touch(); }

        /// <summary>Panel-local position (meters) of the breaker face for a space.</summary>
        public Vector3 SpaceLocal(int slot)
        {
            int row = (slot - 1) / 2; bool left = slot % 2 == 1;
            float y = Piece.Size.y * 0.5f - Units.In(9.5f) - row * Units.In(1f) - Units.In(0.5f);
            return new Vector3((left ? -1 : 1) * Units.In(1.55f), y, Piece.Size.z * 0.5f - Units.In(1.2f));
        }

        void LateUpdate() { ElVisuals.UpdatePanel(this); }
    }

    /// <summary>200 A meter base. Utility = the power company has set the meter (service energized).</summary>
    public class ElMeter : MonoBehaviour
    {
        public static readonly List<ElMeter> All = new List<ElMeter>();
        public NetNode Node;
        public bool Utility;
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static void Attach(Piece p)
        {
            if (p.GetComponent<ElMeter>() != null) return;
            var node = p.gameObject.AddComponent<NetNode>();
            node.Trade = Trade.Electrical; node.Piece = p; node.Type = "meter";
            var m = p.gameObject.AddComponent<ElMeter>();
            m.Node = node;
            Vector3 h = p.Size * 0.5f;
            node.AddPort("load", new Vector3(0, -h.y + Units.In(0.3f), -h.z + Units.In(1f)), Vector3.down, NetKind.Power, 2f, JoinMethod.None, 1);
        }

        public void SetUtility(bool on) { Utility = on; NetGraph.Touch(); }
    }

    /// <summary>A fixture load mounted on a box: ceiling light, fan, porch light, smoke alarm. Lights get a real Light.</summary>
    public class ElLoad : MonoBehaviour
    {
        public static readonly List<ElLoad> All = new List<ElLoad>();
        public Piece Piece;
        public float Watts;
        public bool Switched;
        public ElBox Box;
        public Light Lamp;
        [NonSerialized] public bool On;
        [NonSerialized] public string Why = "";
        readonly List<Material> _lens = new List<Material>();
        bool _shown;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static void Attach(Piece p, float watts, bool light)
        {
            if (p.GetComponent<ElLoad>() != null) return;
            var l = p.gameObject.AddComponent<ElLoad>();
            l.Piece = p; l.Watts = watts; l.Switched = light;
            if (light)
            {
                var go = new GameObject("Lamp");
                go.transform.SetParent(p.transform, false);
                bool porch = p.Def.Id.Contains("porch");
                go.transform.localPosition = porch ? new Vector3(0, -Units.In(1f), Units.In(1f)) : new Vector3(0, -p.Size.y * 0.5f - Units.In(1f), 0);
                var lt = go.AddComponent<Light>();
                lt.type = LightType.Point;
                lt.color = new Color(1f, 0.80f, 0.58f);   // 2700 K warm white
                lt.intensity = 2.6f; lt.range = 8f;
                lt.shadows = LightShadows.Soft;
                lt.enabled = false;
                l.Lamp = lt;
                // per-fixture lens materials (porcelain shade / glass) so they can glow
                foreach (var r in p.GetComponentsInChildren<MeshRenderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && (mats[i].name.Contains("Porcelain") || mats[i].name.Contains("Glass")))
                        {
                            var m = new Material(mats[i]); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
                            mats[i] = m; l._lens.Add(m);
                        }
                    r.sharedMaterials = mats;
                }
            }
        }

        /// <summary>Mounts on the nearest box within reach if not set.</summary>
        public ElBox FindBox()
        {
            if (Box != null) return Box;
            float best = 0.35f;
            foreach (var b in ElBox.All)
            {
                if (b == null) continue;
                float d = Vector3.Distance(b.transform.position, transform.position);
                if (d < best) { best = d; Box = b; }
            }
            return Box;
        }

        public void Show(bool on)
        {
            On = on;
            if (Lamp != null && Lamp.enabled != on) Lamp.enabled = on;
            if (_shown == on) return;
            _shown = on;
            foreach (var m in _lens) m.SetColor("_EmissionColor", on ? new Color(1.6f, 1.35f, 1.0f) : Color.black);
        }
    }
}
