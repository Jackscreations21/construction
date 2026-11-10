using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Feeds the closeup-finish rules (<c>Groundbreak.CloseupInspection.Rules</c>: insulation, drywall, exterior, finish)
    /// into the shared <see cref="Inspections"/> framework, so the inspector NPC and Inspections app see them too.
    /// Bound by reflection so this compiles whether or not the finish branch is merged.
    /// Phase map: Insulation → Insulation, Drywall → Insulation (cover inspection before tape), Exterior → Final, Finish → Final.
    /// </summary>
    public static class CloseupBridge
    {
        public const string Prefix = "closeup:";
        public static int Bridged { get; private set; }

        public static InspectionPhase MapPhase(string phase)
        {
            switch (phase)
            {
                case "Insulation":
                case "Drywall": return InspectionPhase.Insulation;
                default: return InspectionPhase.Final;
            }
        }

        /// <summary>(Re)registers every closeup rule. Safe to call any time (same ids replace).</summary>
        public static int Sync()
        {
            Bridged = 0;
            var type = Type.GetType("Groundbreak.CloseupInspection") ?? FindType("Groundbreak.CloseupInspection");
            if (type == null) return 0;
            var rulesProp = type.GetProperty("Rules", BindingFlags.Public | BindingFlags.Static);
            if (!(rulesProp?.GetValue(null) is IEnumerable rules)) return 0;
            foreach (var rule in rules)
            {
                if (rule == null) continue;
                var rt = rule.GetType();
                string id = rt.GetField("Id")?.GetValue(rule) as string;
                string phase = rt.GetField("Phase")?.GetValue(rule) as string;
                string code = rt.GetField("Code")?.GetValue(rule) as string;
                string desc = rt.GetField("Description")?.GetValue(rule) as string;
                if (!(rt.GetField("Check")?.GetValue(rule) is Delegate check) || id == null) continue;
                string label = string.IsNullOrEmpty(code) ? desc : code + " " + desc;
                Inspections.Register(Prefix + id, MapPhase(phase), label, () => Convert(check));
                Bridged++;
            }
            return Bridged;
        }

        static IEnumerable<InspectionIssue> Convert(Delegate check)
        {
            var list = new List<InspectionIssue>();
            if (!(check.DynamicInvoke() is IEnumerable vs)) return list;
            foreach (var v in vs)
            {
                if (v == null) continue;
                var vt = v.GetType();
                string msg = vt.GetField("Message")?.GetValue(v) as string ?? "Close-up inspection item";
                var pos = vt.GetField("Position")?.GetValue(v);
                list.Add(pos is Vector3 p ? new InspectionIssue(msg, p) : new InspectionIssue(msg));
            }
            return list;
        }

        static Type FindType(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }

        // closeup rules register from BeforeSceneLoad; bridge them once everything has loaded
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() => Sync();
    }
}
