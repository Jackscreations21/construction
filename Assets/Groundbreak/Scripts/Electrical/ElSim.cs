using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Circuit simulation. Utility (meter set) -> SER -> panel main -> breaker -> home run -> boxes (BFS through
    /// Romex). A box passes hot/neutral/ground downstream only where its conductors are spliced. Lights follow the nearest
    /// switch upstream (3-way pair = XOR, travelers need /3). Overloaded breakers trip after a short delay.</summary>
    public class ElSim : MonoBehaviour
    {
        public static ElSim I;
        public static int ShockCount;
        public static float LastShockTime = -10f;
        public static event Action<Vector3> Shocked;
        public const float Volts = 120f;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { I = null; ShockCount = 0; LastShockTime = -10f; Shocked = null; }

        void Update() { Solve(Time.deltaTime); }

        /// <summary>Recomputes every circuit. dt advances overload trip timers.</summary>
        public static void Solve(float dt)
        {
            foreach (var b in ElBox.All) { b.Circuit = null; b.CircuitPanel = null; b.Parent = null; b.Feed = null; b.HotIn = b.NeutralIn = b.GroundIn = false; }
            foreach (var p in ElPanel.All)
            {
                if (p == null) continue;
                p.ServiceLive = ServiceLive(p);
                foreach (var br in p.Breakers) SolveCircuit(p, br, dt);
            }
            foreach (var l in ElLoad.All)
            {
                if (l == null) continue;
                var box = l.FindBox();
                bool on = false; string why = "no box";
                if (box != null)
                {
                    if (!box.HotOk) why = box.HotIn ? "hot not spliced at box" : "no power";
                    else if (!box.NeutralOk) why = "open neutral";
                    else { on = SwitchState(box, out why); }
                }
                if (on && !l.Switched) why = "on";
                l.Why = on ? "on" : why;
                l.Show(on && l.Switched);
                if (!l.Switched) l.On = on;
            }
        }

        static bool ServiceLive(ElPanel p)
        {
            var sp = p.ServicePort;
            if (sp == null) return false;
            foreach (var r in sp.Runs)
            {
                var o = r.Other(p.Node);
                var m = o != null ? o.GetComponent<ElMeter>() : null;
                if (m != null && m.Utility) return true;
            }
            return false;
        }

        static void SolveCircuit(ElPanel p, ElBreaker br, float dt)
        {
            br.Boxes.Clear(); br.Loads.Clear(); br.Runs.Clear();
            br.Live = p.Live && br.On && !br.Tripped;
            if (br.Run == null) { br.LoadW = br.LoadA = 0; br.Over = 0; return; }
            br.Runs.Add(br.Run);
            var first = br.Run.Other(p.Node);
            var fb = first != null ? first.GetComponent<ElBox>() : null;
            if (fb != null)
            {
                var q = new Queue<ElBox>();
                fb.Parent = null; fb.Feed = br.Run; fb.Circuit = br; fb.CircuitPanel = p;
                q.Enqueue(fb); br.Boxes.Add(fb);
                while (q.Count > 0)
                {
                    var b = q.Dequeue();
                    if (b.Parent == null) { b.HotIn = br.Live; b.NeutralIn = true; b.GroundIn = true; }
                    else { b.HotIn = b.Parent.HotOk; b.NeutralIn = b.Parent.NeutralOk; b.GroundIn = b.Parent.GroundOk; }
                    foreach (var r in NetGraph.RunsAt(b.Node))
                    {
                        if (!ElCatalog.IsBranch(r) || r == b.Feed) continue;
                        if (!br.Runs.Contains(r)) br.Runs.Add(r);
                        var o = r.Other(b.Node);
                        var ob = o != null ? o.GetComponent<ElBox>() : null;
                        if (ob == null || ob.Circuit != null) continue;
                        ob.Parent = b; ob.Feed = r; ob.Circuit = br; ob.CircuitPanel = p;
                        br.Boxes.Add(ob); q.Enqueue(ob);
                    }
                }
            }
            // loads
            float w = 0f;
            foreach (var b in br.Boxes) if (b.IsReceptacle && b.DevicePowered) w += b.PlugW;
            foreach (var l in ElLoad.All)
            {
                if (l == null) continue;
                var box = l.FindBox();
                if (box == null || box.Circuit != br) continue;
                br.Loads.Add(l);
                if (box.HotOk && box.NeutralOk && SwitchState(box, out _)) w += l.Watts;
            }
            br.LoadW = w;
            br.LoadA = w / (br.Poles == 2 ? 240f : Volts);
            if (br.Live && br.LoadA > br.Amps + 0.01f)
            {
                float ratio = br.LoadA / br.Amps;
                float delay = Mathf.Lerp(3f, 0.2f, Mathf.Clamp01((ratio - 1f) / 2f)); // inverse-time, compressed to game time
                br.Over += dt;
                if (br.Over >= delay)
                {
                    br.Tripped = true; br.Over = 0f; br.Live = false;
                    Game.Toast($"Breaker {br.Slot} ({br.Name}{(string.IsNullOrEmpty(br.Label) ? "" : " " + br.Label)}) TRIPPED - {br.LoadA:0.0} A on a {br.Amps} A circuit");
                    NetGraph.Touch();
                }
            }
            else br.Over = 0f;
        }

        /// <summary>Is a light at this box switched on? Nearest switch upstream controls it; a 3-way pair is XOR and needs /3 travelers.</summary>
        public static bool SwitchState(ElBox box, out string why)
        {
            why = "on";
            for (var s = box.Parent; s != null; s = s.Parent)
            {
                if (s.Device == ElDevice.Switch) { if (!s.SwitchOn) why = "switch off"; return s.SwitchOn; }
                if (s.Device == ElDevice.Switch3)
                {
                    ElBox other = null;
                    for (var t = s.Parent; t != null; t = t.Parent) if (t.Device == ElDevice.Switch3) { other = t; break; }
                    if (other == null) { if (!s.SwitchOn) why = "switch off"; return s.SwitchOn; }
                    for (var t = s; t != other; t = t.Parent)
                        if (t.Feed == null || t.Feed.Spec.Conductors < 3) { why = "3-way travelers need /3 cable between the switches"; return false; }
                    bool on = s.SwitchOn ^ other.SwitchOn;
                    if (!on) why = "3-way off";
                    return on;
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ live checks / measurements

        public static bool BoxLive(ElBox b) => b != null && b.HotIn;

        public static bool RunLive(NetRun r)
        {
            if (r == null || r.Spec.Trade != Trade.Electrical) return false;
            if (r.Spec.Id == ElCatalog.SerId)
            {
                foreach (var n in new[] { r.NodeA, r.NodeB }) { var m = n != null ? n.GetComponent<ElMeter>() : null; if (m != null && m.Utility) return true; }
                return false;
            }
            foreach (var p in ElPanel.All) { var br = p.BreakerFor(r); if (br != null && br.Live) return true; }
            foreach (var n in new[] { r.NodeA, r.NodeB })
            {
                var b = n != null ? n.GetComponent<ElBox>() : null;
                if (b != null && b.HotOk) return true;
            }
            return false;
        }

        public static bool LiveNear(Vector3 p, float radius)
        {
            foreach (var b in ElBox.All) if (b != null && b.HotIn && Vector3.Distance(b.transform.position, p) < radius) return true;
            foreach (var r in NetGraph.RunsOf(Trade.Electrical))
            {
                r.DistanceAlong(p, out float d);
                if (d < radius && RunLive(r)) return true;
            }
            foreach (var pn in ElPanel.All) if (pn != null && pn.ServiceLive && Vector3.Distance(pn.transform.position, p) < radius + 0.3f) return true;
            return false;
        }

        /// <summary>Multimeter reading at a box (at the device if made up, else at the incoming cable).</summary>
        public static string Measure(ElBox b)
        {
            if (b == null) return "";
            bool h, n, g;
            if (b.Stripped && b.Hots) { h = b.HotOk; n = b.NeutralOk; g = b.GroundOk; }
            else { h = b.HotIn; n = b.NeutralIn; g = b.GroundIn; }
            if (!b.Stripped) return b.HotIn ? "Sheathed cable - strip to measure (tester says LIVE)" : "Sheathed cable - 0 V";
            int hn = h && n ? 120 : 0, hg = h && g ? 120 : 0, ng = 0;
            string s = $"H-N {hn} V · H-G {hg} V · N-G {ng} V";
            if (b.Circuit != null && b.Circuit.Poles == 2) s = (h ? "L1-L2 240 V · " : "L1-L2 0 V · ") + s;
            if (h && !g) s += "  OPEN GROUND";
            if (h && !n) s += "  OPEN NEUTRAL";
            if (!h) s += "  (dead)";
            return s;
        }

        // ------------------------------------------------------------------ shock

        /// <summary>Called by tools before working a box/cable. Returns true (and shocks the player) if it is live.</summary>
        public static bool CheckShock(bool live, Vector3 at, string what)
        {
            if (!live) return false;
            ShockCount++;
            LastShockTime = Time.time;
            if (GameClock.I != null)
            {
                GameClock.I.Hours += 0.25f;
                while (GameClock.I.Hours >= 24f) { GameClock.I.Hours -= 24f; GameClock.I.Day++; }
            }
            Game.Toast($"ZAP! {what} was live. Shut off the breaker first. (lost 15 min recovering)");
            Shocked?.Invoke(at);
            return true;
        }

        void OnGUI()
        {
            float t = Time.time - LastShockTime;
            if (t < 0f || t > 0.7f) return;
            float a = 1f - t / 0.7f;
            GUI.color = new Color(0.85f, 0.92f, 1f, a * 0.85f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }

    /// <summary>E while aiming: flip a switch, cycle a receptacle's plug load, operate the panel main / reset trips.</summary>
    public class ElInteract : MonoBehaviour
    {
        public static readonly float[] PlugLoads = { 0f, 60f, 600f, 1500f };

        void Update()
        {
            if (Game.UIOpen || Game.Player == null || !GInput.GetKeyDown(KeyCode.E)) return;
            if (!Game.Player.TryAim(out var hit, 2.5f)) return;
            var box = hit.collider.GetComponentInParent<ElBox>();
            if (box != null) { Game.Toast(Use(box)); return; }
            var panel = hit.collider.GetComponentInParent<ElPanel>();
            if (panel != null) Game.Toast(UsePanel(panel));
        }

        public static string Use(ElBox box)
        {
            if (box.IsSwitch) { box.SwitchOn = !box.SwitchOn; box.Changed(); return "Switch " + (box.SwitchOn ? "ON" : "OFF"); }
            if (box.IsReceptacle)
            {
                int i = Array.IndexOf(PlugLoads, box.PlugW);
                box.PlugW = PlugLoads[(i + 1) % PlugLoads.Length]; box.Changed();
                return box.PlugW <= 0 ? "Unplugged" : $"Plugged in {LoadName(box.PlugW)} ({box.PlugW:0} W)";
            }
            return box.Label;
        }

        public static string LoadName(float w) => w >= 1500 ? "space heater" : w >= 600 ? "shop vac" : w > 0 ? "work light" : "nothing";

        public static string UsePanel(ElPanel p)
        {
            if (p.ResetTripped()) return "Reset tripped breaker(s)";
            p.SetMain(!p.Main);
            return "Main breaker " + (p.Main ? "ON" : "OFF");
        }
    }
}
