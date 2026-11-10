using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Electrician's hand tools: wire strippers, lineman's pliers, non-contact voltage tester, multimeter, staple hammer.</summary>
    public static class ElTools
    {
        public static void Register()
        {
            ToolFactory.Register(new ToolShopItem { Id = "wire-strippers", Name = "Wire Strippers (Klein-style)", Price = 25, Description = "Strip NM sheath + conductors in a box; cut Romex." },
                () => new WireStrippers());
            ToolFactory.Register(new ToolShopItem { Id = "lineman", Name = "Lineman's Pliers 9\"", Price = 42, Description = "Twist splices + wire nuts, pigtail grounds, install devices, work the panel." },
                () => new LinemansPliers());
            ToolFactory.Register(new ToolShopItem { Id = "voltage-tester", Name = "Non-Contact Voltage Tester", Price = 25, Description = "Beeps and glows red near a live box or cable." },
                () => new VoltageTester());
            ToolFactory.Register(new ToolShopItem { Id = "multimeter", Name = "Digital Multimeter", Price = 59, Description = "Reads 120/240/0 V, finds open grounds and open neutrals at a box." },
                () => new Multimeter());
            ToolFactory.Register(new ToolShopItem { Id = "staple-hammer", Name = "Insulated Staple Hammer", Price = 32, Description = "Staple Romex within 8\" of every box and at least every 4-1/2'." },
                () => new StapleHammer());
            Tablet.AddHelp("Electrical: fish tape pulls Romex (snaps to box knockouts / panel) · strippers LMB box = strip, LMB cable = cut · pliers LMB box = next splice / install device, RMB = pick device, LMB panel = main / reset");
            Tablet.AddHelp("Electrical: E on a switch = flip, E on a receptacle = plug in a load, E on the panel = main / reset · staple hammer LMB on Romex · tester beeps near live wiring · multimeter LMB on a box");
        }

        public static ElBox AimBox(out RaycastHit hit)
        {
            if (!MepAim.Aim(out hit)) return null;
            return hit.collider.GetComponentInParent<ElBox>();
        }

        public static ElPanel AimPanel()
        {
            if (!MepAim.Aim(out var hit)) return null;
            return hit.collider.GetComponentInParent<ElPanel>();
        }

        /// <summary>Strippers on a box: strip all cables. Shocks if the box is live.</summary>
        public static string Strip(ElBox b)
        {
            if (ElSim.CheckShock(ElSim.BoxLive(b), b.transform.position, b.Label)) return "Shocked!";
            return b.Strip();
        }

        public static string Splice(ElBox b)
        {
            if (ElSim.CheckShock(ElSim.BoxLive(b), b.transform.position, b.Label)) return "Shocked!";
            return b.SpliceNext();
        }

        public static string Install(ElBox b, ElDevice d)
        {
            if (ElSim.CheckShock(ElSim.BoxLive(b), b.transform.position, b.Label)) return "Shocked!";
            return b.InstallDevice(d);
        }

        public static string CutRun(NetRun r, Vector3 at)
        {
            if (ElSim.CheckShock(ElSim.RunLive(r), at, r.Spec.Name)) return "Shocked!";
            return r.SplitAt(at) != null ? "Cut " + r.Spec.Name : "Too close to the end to cut";
        }

        public static bool Staple(NetRun r, Vector3 at)
        {
            if (r == null || !ElCatalog.IsBranch(r)) return false;
            if (!MepStock.Free && !Game.Stock.TryTake(ElCatalog.Staples, 1)) { Game.Toast("Out of cable staples"); return false; }
            r.AddStaple(at);
            return true;
        }
    }

    public class WireStrippers : Tool
    {
        public override string Id => "wire-strippers";
        public override string Name => "Wire Strippers";
        public override string Hint => "LMB box = strip cables · LMB Romex = cut · RMB Romex = pull it out";
        public override void Primary()
        {
            var b = ElTools.AimBox(out var hit);
            if (b != null) { Game.Toast(ElTools.Strip(b)); ToolUtil.Wear(this, 0.0005f); return; }
            var r = NetRun.FromCollider(hit.collider);
            if (r != null && r.Spec.Trade == Trade.Electrical) Game.Toast(ElTools.CutRun(r, hit.point));
        }
        public override void Secondary()
        {
            var r = MepAim.AimRun(out var hit);
            if (r == null || r.Spec.Trade != Trade.Electrical) return;
            if (ElSim.CheckShock(ElSim.RunLive(r), hit.point, r.Spec.Name)) return;
            MepStock.Return(r.Spec, r.Length * 0.9f);
            Object.Destroy(r.gameObject);
            Game.Toast("Pulled out " + r.Spec.Name);
        }
        public override void DrawGUI()
        {
            var b = ElTools.AimBox(out _);
            if (b != null) ToolUtil.CenterLabel($"{b.Label}: {b.Cables.Count} cable(s) · {(b.Stripped ? "stripped" : "not stripped")}", Color.white);
        }
    }

    public class LinemansPliers : Tool
    {
        public override string Id => "lineman";
        public override string Name => "Lineman's Pliers";
        public override string Hint => "LMB box = next splice (grounds > neutrals > hots) then install device · RMB = choose device · Ctrl+LMB = undo · LMB panel = main/reset";
        public ElDevice Choice = ElDevice.Receptacle15;
        static readonly ElDevice[] Choices = { ElDevice.Receptacle15, ElDevice.Receptacle20, ElDevice.Gfci, ElDevice.Switch, ElDevice.Switch3 };

        public override void Secondary()
        {
            int i = System.Array.IndexOf(Choices, Choice);
            for (int k = 1; k <= Choices.Length; k++)
            {
                var c = Choices[(i + k) % Choices.Length];
                if (MepStock.Free || Game.Stock.Count(ElCatalog.DeviceId(c)) > 0 || k == Choices.Length) { Choice = c; break; }
            }
            Game.Toast("Device: " + ElCatalog.DeviceName(Choice) + (MepStock.Free ? "" : $" ({Game.Stock.Count(ElCatalog.DeviceId(Choice))} in stock)"));
        }

        public override void Primary()
        {
            var b = ElTools.AimBox(out _);
            if (b != null)
            {
                if (GInput.Ctrl)
                {
                    if (ElSim.CheckShock(ElSim.BoxLive(b), b.transform.position, b.Label)) return;
                    if (b.Device != ElDevice.None) Game.Toast(b.RemoveDevice());
                    else if (b.Hots) { b.Hots = false; b.Changed(); Game.Toast("Hots un-spliced"); }
                    else if (b.Neutrals) { b.Neutrals = false; b.Changed(); Game.Toast("Neutrals un-spliced"); }
                    else if (b.Grounds) { b.RemoveGroundSplice(); Game.Toast("Ground splice removed"); }
                    return;
                }
                if (b.MadeUp && b.Device == ElDevice.None && b.Kind != ElBoxKind.Round) Game.Toast(ElTools.Install(b, Choice));
                else Game.Toast(ElTools.Splice(b));
                ToolUtil.Wear(this, 0.0003f);
                return;
            }
            var p = ElTools.AimPanel();
            if (p != null) Game.Toast(ElInteract.UsePanel(p));
        }

        public override void DrawGUI()
        {
            var b = ElTools.AimBox(out _);
            string next = b == null ? "" : !b.Stripped ? "strip first" : !b.Grounds ? "splice grounds" : !b.Neutrals ? "splice neutrals" : !b.Hots ? "splice hots" : b.Device == ElDevice.None && b.Kind != ElBoxKind.Round ? "install " + ElBox.ShortDevice(Choice) : "made up";
            ToolUtil.CenterLabel(b != null ? $"{b.Label} · next: {next}" : "Device: " + ElBox.ShortDevice(Choice), Color.white);
        }
    }

    public class VoltageTester : Tool
    {
        public override string Id => "voltage-tester";
        public override string Name => "Voltage Tester (NCVT)";
        public override string Hint => "Hold near a box or cable: beeps + glows red when live";
        public bool Detecting { get; private set; }
        AudioSource _src; float _nextBeep;

        public override void Tick()
        {
            Detecting = false;
            if (MepAim.Aim(out var hit, 1.2f)) Detecting = ElSim.LiveNear(hit.point, 0.12f);
            if (Detecting && Time.time > _nextBeep)
            {
                _nextBeep = Time.time + 0.25f;
                if (_src == null && Game.Player != null)
                {
                    _src = Game.Player.gameObject.AddComponent<AudioSource>();
                    _src.clip = Beep(); _src.spatialBlend = 0f; _src.volume = 0.25f;
                }
                if (_src != null) _src.Play();
            }
        }

        static AudioClip _beep;
        static AudioClip Beep()
        {
            if (_beep != null) return _beep;
            int rate = 44100, n = rate / 10;
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Sin(2 * Mathf.PI * 3200f * i / rate) * (1f - i / (float)n);
            _beep = AudioClip.Create("ncvt-beep", n, 1, rate, false);
            _beep.SetData(d, 0);
            return _beep;
        }

        public override void DrawGUI()
        {
            var r = new Rect(Screen.width * 0.5f - 14, Screen.height * 0.5f + 70, 28, 28);
            GUI.color = Detecting ? new Color(1f, 0.1f, 0.1f, 0.9f) : new Color(0.2f, 0.2f, 0.2f, 0.6f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            ToolUtil.CenterLabel(Detecting ? "BEEP BEEP - LIVE" : "no voltage detected", Detecting ? Color.red : Color.gray, 100f);
        }
    }

    public class Multimeter : Tool
    {
        public override string Id => "multimeter";
        public override string Name => "Multimeter";
        public override string Hint => "LMB on a box: measure H-N / H-G / N-G";
        string _reading = "";
        public override void Primary()
        {
            var b = ElTools.AimBox(out _);
            _reading = b != null ? ElSim.Measure(b) : "";
            if (b != null) Game.Toast(_reading);
        }
        public override void DrawGUI()
        {
            if (!string.IsNullOrEmpty(_reading)) ToolUtil.CenterLabel(_reading, _reading.Contains("OPEN") ? new Color(1f, 0.5f, 0.2f) : Color.green);
        }
    }

    public class StapleHammer : Tool
    {
        public override string Id => "staple-hammer";
        public override string Name => "Staple Hammer";
        public override string Hint => "LMB on Romex = insulated staple (within 8\" of boxes, every 4-1/2')";
        public override void Primary()
        {
            var r = MepAim.AimRun(out var hit);
            if (r == null || !ElCatalog.IsBranch(r)) return;
            if (ElTools.Staple(r, hit.point)) { ToolUtil.Wear(this, 0.0002f); Game.Toast("Stapled " + r.Spec.Name); }
        }
        public override void DrawGUI()
        {
            var r = MepAim.AimRun(out var hit);
            if (r == null || !ElCatalog.IsBranch(r)) return;
            float s = r.DistanceAlong(hit.point, out _);
            float near = float.MaxValue; foreach (var st in r.Staples) near = Mathf.Min(near, Mathf.Abs(st - s));
            ToolUtil.CenterLabel($"{r.Spec.Name} · {(near == float.MaxValue ? "no staples" : "nearest staple " + Units.Format(near))}", Color.white);
        }
    }
}
