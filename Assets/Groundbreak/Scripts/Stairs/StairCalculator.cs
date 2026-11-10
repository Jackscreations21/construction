using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Stair Calculator" tablet app: total rise in, riser/tread layout out. "Use for layout" sets the framing square's stair gauges.</summary>
    public static class StairCalculator
    {
        /// <summary>The layout the framing square uses (set with "Use for layout").</summary>
        public static StairResult Layout;
        public static bool HasLayout;

        static string _rise = "", _run = "10 1/4";
        static GUIStyle _field;

        public static void UseForLayout(StairResult r)
        {
            Layout = r;
            HasLayout = r.Valid && r.Risers >= 2;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            HasLayout = false;
            Tablet.RegisterApp("Stair Calculator", Draw);
            Tablet.AddHelp("Stairs: measure the total rise with the tape, open Stair Calculator, 'Use for layout', then Framing Square on a loose 2x12: LMB lay out, RMB cut (needs the circular saw)");
            ToolFactory.Register(new ToolShopItem
            {
                Id = "framingsquare",
                Name = "Framing Square + stair gauges",
                Price = 29,
                Description = "24\" steel square with brass stair gauges. LMB: lay out a stair stringer on a loose 2x12 (uses the Stair Calculator). RMB: cut it with your circular saw.",
            }, () => new FramingSquare());
        }

        public static void Draw()
        {
            var tab = Tablet.I;
            if (tab == null) return;
            GUIStyle L = tab.LabelStyle, Hd = tab.HeaderStyle, B = tab.ButtonStyle;
            int fs = L.fontSize;
            if (_field == null || _field.fontSize != fs) _field = new GUIStyle(GUI.skin.textField) { fontSize = fs };

            GUILayout.Label("Stair Calculator", Hd);
            GUILayout.Label("IRC R311.7: risers 7 3/4\" max, treads 10\" min, 36\" min width, 6' 8\" headroom, risers within 3/8\" of each other.", L);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Total rise (floor to floor):", L, GUILayout.Width(fs * 14));
            _rise = GUILayout.TextField(_rise, _field, GUILayout.Width(fs * 8));
            if (TapeMeasure.LastMeasurement > 0.01f &&
                GUILayout.Button("Use tape: " + Units.Format(TapeMeasure.LastMeasurement), B, GUILayout.Width(fs * 12)))
                _rise = StairMath.FormatIn(Units.ToInches(TapeMeasure.LastMeasurement)).TrimEnd('"');
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Unit run (tread, 10\" min):", L, GUILayout.Width(fs * 14));
            _run = GUILayout.TextField(_run, _field, GUILayout.Width(fs * 8));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            if (!StairMath.TryParseInches(_rise, out float rise))
            {
                GUILayout.Label("Enter the total rise, e.g. 48, 48 1/2 or 4' 0 1/2\" (measure it with the tape and press 'Use tape').", L);
                DrawCurrent(L);
                return;
            }
            if (!StairMath.TryParseInches(_run, out float run)) run = StairMath.DefaultRunIn;
            var r = StairMath.Calculate(rise, run);
            if (!r.Valid || r.Risers < 2)
            {
                GUILayout.Label("A rise of " + StairMath.FormatIn(rise) + " is a single step - no stringer needed.", L);
                DrawCurrent(L);
                return;
            }

            Line(L, "Risers", r.Risers + " @ " + StairMath.FormatIn(r.UnitRiseIn) + (r.RiseOk ? "" : "  (over 7 3/4\"!)"));
            Line(L, "Treads", r.Treads + " @ " + StairMath.FormatIn(r.UnitRunIn) + " run" + (r.RunOk ? "" : "  (under 10\"!)") +
                              "  - two 2x6 = " + StairMath.FormatIn(r.TreadDepthIn) + " deep, " + StairMath.FormatIn(r.NosingIn) + " nosing");
            Line(L, "Total run", StairMath.FormatFtIn(r.TotalRunIn) + "   (angle " + r.AngleDeg.ToString("0.0") + " deg)");
            Line(L, "Stringer", StairMath.FormatFtIn(r.StringerLengthIn) + " of 2x12 (hypotenuse " + StairMath.FormatFtIn(r.HypotenuseIn) + ") - cut from "
                                + (Catalog.Get(r.StockId) != null ? Catalog.Get(r.StockId).Name : r.StockId) + ", 3 for a 36\" stair");
            Line(L, "Throat", StairMath.FormatIn(r.ThroatIn) + (r.ThroatOk ? "  OK (5\" min on a 2x12)" : "  TOO THIN - use a longer run or add a stringer"));
            Line(L, "Drop", "cut " + StairMath.FormatIn(r.TreadThicknessIn) + " off the bottom (tread thickness) and " + StairMath.FormatIn(r.RiserThicknessIn) + " off the top (riser board)");
            GUILayout.Label(r.HeadroomNote, L);
            GUILayout.Space(6);
            if (GUILayout.Button("Use for layout (sets the framing square's stair gauges)", B))
            {
                UseForLayout(r);
                tab.ShowMessage("Framing square set: " + StairMath.FormatIn(r.UnitRiseIn) + " on the tongue, " + StairMath.FormatIn(r.UnitRunIn) + " on the blade.");
            }
            DrawCurrent(L);
        }

        static void Line(GUIStyle L, string k, string v)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(k, L, GUILayout.Width(L.fontSize * 7));
            GUILayout.Label(v, L);
            GUILayout.EndHorizontal();
        }

        static void DrawCurrent(GUIStyle L)
        {
            if (!HasLayout) return;
            GUILayout.Space(4);
            GUILayout.Label("Framing square gauges: " + Layout.Risers + " risers @ " + StairMath.FormatIn(Layout.UnitRiseIn) + " / " +
                            StairMath.FormatIn(Layout.UnitRunIn) + " run (total rise " + StairMath.FormatFtIn(Layout.TotalRiseIn) + ")", L);
        }
    }
}
