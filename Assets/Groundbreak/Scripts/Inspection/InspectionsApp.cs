using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Inspections" tablet app: phase checklist, schedule the inspector, last result notes.</summary>
    public static class InspectionsApp
    {
        static InspectionPhase? _view;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Tablet.RegisterApp("Inspections", Draw);
            Tablet.AddHelp("Inspections app (tablet) - schedule the inspector; red flags mark corrections, re-inspection costs $" + Inspections.ReinspectionFee + " + a day");
        }

        static void Draw()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle;
            float fs = label.fontSize;

            GUILayout.Label("Current phase: " + Inspections.CurrentPhaseLabel, header);
            if (!GameSettings.InspectionsEnabled) GUILayout.Label("Inspections are OFF (settings) - every phase is signed off automatically.", label);
            if (InspectorNpc.Busy) GUILayout.Label(InspectorNpc.Active.Status, header);
            GUILayout.Space(6);

            foreach (var p in Inspections.AllPhases)
            {
                bool req = Inspections.RequiredPhases.Contains(p);
                int rules = 0; foreach (var _ in Inspections.RulesFor(p)) rules++;
                string state = !req ? "not required" : Inspections.IsPassed(p) ? "PASSED" :
                               Inspections.FailCount(p) > 0 ? $"FAILED x{Inspections.FailCount(p)}" : "not inspected";
                GUILayout.BeginHorizontal();
                var prev = GUI.color;
                GUI.color = Inspections.IsPassed(p) ? new Color(0.5f, 1f, 0.5f) : Inspections.FailCount(p) > 0 ? new Color(1f, 0.55f, 0.5f) : Color.white;
                GUILayout.Label(UISkin.Icon(Inspections.IsPassed(p) ? "check" : Inspections.FailCount(p) > 0 ? "cross" : "dot"), GUILayout.Width(fs * 1.4f), GUILayout.Height(fs * 1.4f));
                GUILayout.Label($"{Inspections.Label(p)}", header, GUILayout.Width(fs * 9));
                GUI.color = prev;
                GUILayout.Label($"{state}  ·  {rules} item(s)", label, GUILayout.Width(fs * 14));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Checklist", button, GUILayout.Width(fs * 7))) _view = _view == p ? (InspectionPhase?)null : p;
                bool canSchedule = req && !Inspections.IsPassed(p) && !InspectorNpc.Busy && Inspections.CurrentPhase == p;
                GUI.enabled = canSchedule;
                string lbl = Inspections.NeedsReinspection(p) ? $"Re-inspect (${Inspections.ReinspectionFee})" : "Schedule";
                if (GUILayout.Button(lbl, button, GUILayout.Width(fs * 9))) Schedule(p);
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                if (_view == p)
                {
                    foreach (var r in Inspections.RulesFor(p)) GUILayout.Label("    • " + r.Description, label);
                    if (rules == 0) GUILayout.Label("    (no items registered for this phase yet)", label);
                }
            }

            GUILayout.Space(10);
            if (Inspections.History.Count > 0)
            {
                var last = Inspections.History[Inspections.History.Count - 1];
                GUILayout.Label($"Last inspection - Day {last.Day}: {last.Summary}", header);
                foreach (var n in last.Notes) GUILayout.Label("  " + n, label);
                foreach (var i in last.Issues) GUILayout.Label("  CORRECTION: " + i.Message, label);
                if (InspectionMarkers.Count > 0 && GUILayout.Button("Clear red flags", button, GUILayout.Width(fs * 10))) InspectionMarkers.ClearAll();
            }
        }

        public static void Schedule(InspectionPhase p)
        {
            if (!Inspections.ChargeForScheduling(p, out var msg)) { Tablet.I?.ShowMessage(msg); return; }
            if (!GameSettings.InspectionsEnabled) { Inspections.Inspect(p); Tablet.I?.ShowMessage(msg); return; }
            InspectorNpc.Dispatch(p);
            Tablet.I?.ShowMessage(msg + " The inspector is walking the site.");
        }
    }
}
