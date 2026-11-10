using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Panel schedule" section of the MEP Plans tablet app: utility/main, breakers per space (install from stock,
    /// land home runs, label, on/off/reset), circuit table (load vs rating, gauge vs breaker) and a schematic per circuit.</summary>
    public static class ElTablet
    {
        static int _pick;
        static GUIStyle L => Tablet.I != null ? Tablet.I.LabelStyle : GUI.skin.label;
        static GUIStyle B => Tablet.I != null ? Tablet.I.ButtonStyle : GUI.skin.button;
        static GUIStyle H => Tablet.I != null ? Tablet.I.HeaderStyle : GUI.skin.label;

        public static void Register()
        {
            MepTablet.AddSection(Trade.Electrical, "Panel schedule", Draw);
        }

        static List<string> StockBreakers()
        {
            var l = new List<string>();
            foreach (var b in ElCatalog.Breakers) if (MepStock.Free || Game.Stock.Count(b.Id) > 0) l.Add(b.Id);
            return l;
        }

        static void Draw()
        {
            if (ElPanel.All.Count == 0) { GUILayout.Label("No panel placed. Buy a 200A main breaker panel + meter base (Supply Store > Electrical).", L); return; }
            foreach (var m in ElMeter.All)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Meter base: " + (m.Utility ? "METER SET - utility power on" : "no meter (utility not connected)"), L);
                if (GUILayout.Button(m.Utility ? "Pull meter" : "Call utility to set meter", B, GUILayout.Width(260))) m.SetUtility(!m.Utility);
                GUILayout.EndHorizontal();
            }
            int pi = 0;
            foreach (var p in ElPanel.All)
            {
                pi++;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Panel {pi}: service {(p.ServiceLive ? "LIVE" : "dead")} · main {(p.Main ? "ON" : "OFF")}", H);
                if (GUILayout.Button(p.Main ? "Main OFF" : "Main ON", B, GUILayout.Width(140))) p.SetMain(!p.Main);
                GUILayout.EndHorizontal();

                var stock = StockBreakers();
                if (stock.Count > 0)
                {
                    _pick = Mathf.Clamp(_pick, 0, stock.Count - 1);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Breaker to install: " + ElCatalog.Breaker(stock[_pick])?.Name + (MepStock.Free ? "" : $" ({Game.Stock.Count(stock[_pick])})"), L);
                    if (GUILayout.Button("<", B, GUILayout.Width(40))) _pick = (_pick + stock.Count - 1) % stock.Count;
                    if (GUILayout.Button(">", B, GUILayout.Width(40))) _pick = (_pick + 1) % stock.Count;
                    GUILayout.EndHorizontal();
                }
                else GUILayout.Label("No breakers in stock.", L);

                var unl = p.Unlanded();
                for (int slot = 1; slot <= ElPanel.Spaces; slot++)
                {
                    var b = p.BreakerAt(slot);
                    if (b != null && b.Slot != slot) continue; // 2nd space of a 2-pole
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{slot,2}", L, GUILayout.Width(30));
                    if (b == null)
                    {
                        GUILayout.Label("— empty —", L, GUILayout.Width(220));
                        if (stock.Count > 0 && GUILayout.Button("Install", B, GUILayout.Width(90)))
                        {
                            var nb = p.Install(stock[Mathf.Clamp(_pick, 0, stock.Count - 1)], slot);
                            Game.Toast(nb != null ? "Installed " + nb.Name + " in space " + slot : "Doesn't fit there");
                        }
                    }
                    else
                    {
                        GUILayout.Label($"{b.Name} {b.State}", L, GUILayout.Width(220));
                        b.Label = GUILayout.TextField(b.Label ?? "", GUILayout.Width(160));
                        if (GUILayout.Button(b.Tripped ? "Reset" : b.On ? "Off" : "On", B, GUILayout.Width(70)))
                        { if (b.Tripped) b.Reset(); else b.On = !b.On; p.Touch(); }
                        if (b.Run == null)
                        {
                            if (unl.Count > 0 && GUILayout.Button("Land " + unl[0].Spec.Name.Split(' ')[1], B, GUILayout.Width(130))) Game.Toast(p.Land(b, unl[0]));
                        }
                        else
                        {
                            string ok = b.Run.Spec.Ampacity + 0.01f < b.Amps ? " VIOLATION" : "";
                            GUILayout.Label($"{b.Run.Spec.Name.Split(' ')[1]}{ok} · {b.LoadA:0.0}/{b.Amps} A", L, GUILayout.Width(200));
                            if (GUILayout.Button("Unland", B, GUILayout.Width(80))) { b.Run = null; p.Touch(); }
                        }
                        if (b.Run == null && GUILayout.Button("Remove", B, GUILayout.Width(80))) { p.Remove(b); GUILayout.EndHorizontal(); break; }
                    }
                    GUILayout.EndHorizontal();
                }
                if (unl.Count > 0) GUILayout.Label($"{unl.Count} home run(s) not landed.", L);

                GUILayout.Space(6);
                GUILayout.Label("Circuits", H);
                foreach (var b in p.Breakers)
                {
                    if (b.Run == null) continue;
                    GUILayout.Label($"#{b.Slot} {b.Label} - {b.Name} {b.State} · {b.LoadW:0} W = {b.LoadA:0.0} A of {b.Amps} A · {b.Run.Spec.Name}", L);
                    foreach (var x in b.Boxes)
                    {
                        int depth = 0; for (var u = x.Parent; u != null; u = u.Parent) depth++;
                        string st = !x.MadeUp ? "not made up" : !x.GroundOk ? "OPEN GROUND" : !x.NeutralOk ? "OPEN NEUTRAL" : x.HotOk ? "120 V" : "dead";
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(new string(' ', 4 + depth * 3) + "└ " + x.Label + " · " + st + (x.IsReceptacle && x.PlugW > 0 ? $" · {ElInteract.LoadName(x.PlugW)} {x.PlugW:0} W" : ""), L);
                        if (x.IsSwitch && GUILayout.Button(x.SwitchOn ? "Switch off" : "Switch on", B, GUILayout.Width(120))) { x.SwitchOn = !x.SwitchOn; x.Changed(); }
                        if (x.IsReceptacle && GUILayout.Button("Plug load", B, GUILayout.Width(110))) ElInteract.Use(x);
                        if (x.Kind != ElBoxKind.Round && GUILayout.Button(x.Wet ? "Wet ✓" : "Wet", B, GUILayout.Width(80))) { x.Wet = !x.Wet; x.Changed(); }
                        GUILayout.EndHorizontal();
                    }
                    foreach (var l in b.Loads) GUILayout.Label($"        ◦ {l.Piece.Def.Name}: {(l.On ? "ON" : l.Why)}", L);
                }
            }
        }
    }
}
