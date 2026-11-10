using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Plumbing" section of the MEP Plans tablet app: test gauge, water test, DWV slope table, fixture schematic.</summary>
    public static class PlumbTablet
    {
        static Vector2 _scroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() { MepTablet.AddSection(Trade.Plumbing, "Plumbing tests & schedule", Draw); }

        static GUIStyle L => Tablet.I != null ? Tablet.I.LabelStyle : GUI.skin.label;
        static GUIStyle H => Tablet.I != null ? Tablet.I.HeaderStyle : GUI.skin.label;
        static GUIStyle B => Tablet.I != null ? Tablet.I.ButtonStyle : GUI.skin.button;

        static void Draw()
        {
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(380));
            // ---- pressure test
            GUILayout.Label("Water supply pressure test", H);
            var gauges = PressureTest.Gauges();
            if (gauges.Count == 0) GUILayout.Label("No test gauge installed - place a \"Test gauge\" on a supply stub.", L);
            foreach (var g in gauges)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Gauge: {g.Psi:0} psi · {g.Status}", L);
                if (GUILayout.Button("Pump 100 psi water", B, GUILayout.Width(180))) PressureTest.Pump(g, false);
                if (GUILayout.Button("Pump 60 psi air", B, GUILayout.Width(150))) PressureTest.Pump(g, true);
                GUILayout.EndHorizontal();
                foreach (var l in g.Leaks) GUILayout.Label("   • " + l.Note, L);
            }
            // ---- water test
            GUILayout.Space(6);
            GUILayout.Label("DWV water test", H);
            GUILayout.BeginHorizontal();
            GUILayout.Label(DrainTest.Status, L);
            if (GUILayout.Button("Plug + fill to vent", B, GUILayout.Width(180))) DrainTest.Fill();
            if (GUILayout.Button("Drain", B, GUILayout.Width(90))) DrainTest.Drain();
            GUILayout.EndHorizontal();
            foreach (var l in DrainTest.Issues) GUILayout.Label("   • " + l.Note, L);
            // ---- slopes
            GUILayout.Space(6);
            GUILayout.Label("Drain slopes (toward the building drain)", H);
            foreach (var leg in PlumbSlope.Legs())
            {
                if (!leg.Horizontal) continue;
                float len = Vector3.Distance(leg.Run.Points[leg.Seg], leg.Run.Points[leg.Seg + 1]);
                GUILayout.Label($"{leg.Run.Spec.Name,-26} {Units.Format(len),10}   {MepAim.SlopeLabel(leg.Slope),8}   {(leg.Ok ? "OK" : "TOO FLAT / BACKWARDS")}", L);
            }
            // ---- fixture schedule
            GUILayout.Space(6);
            GUILayout.Label("Fixtures", H);
            foreach (var n in PlumbParts.Fixtures())
            {
                var parts = new List<string>();
                foreach (var p in n.Ports)
                {
                    string to = p.Run == null ? (p.Capped ? "capped" : "NOT CONNECTED") : p.Run.Spec.Name + " → " + (p.Run.Other(n) != null ? p.Run.Other(n).Label : "open end");
                    if (p.NeedsJoin) to += " (joint not made)";
                    parts.Add($"{p.Label}: {to}");
                }
                GUILayout.Label($"{n.Label} — " + string.Join(" · ", parts), L);
            }
            GUILayout.EndScrollView();
        }
    }
}
