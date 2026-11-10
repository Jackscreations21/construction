using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Feeds the shell-framing structural check (<see cref="FramingInspection.Run"/>: load path, connectors, O.C. spacing,
    /// plumbed/braced walls, flashed openings) into the shared <see cref="Inspections"/> framework as the Framing-phase rule
    /// "framing:structure", so the inspector NPC and the career phase tracker include it.
    /// </summary>
    public static class FramingBridge
    {
        public const string RuleId = "framing:structure";

        public static void Register() =>
            Inspections.Register(RuleId, InspectionPhase.Framing,
                "Structure: continuous load path, connectors, stud/joist spacing, walls plumbed and braced, openings flashed (IRC R602, R703.4)", Check);

        static IEnumerable<InspectionItem> Check()
        {
            var list = new List<InspectionItem>();
            foreach (var i in FramingInspection.Run())
            {
                if (i == null || i.Severity == StructuralStatus.Ok) continue;
                string tag = i.Severity == StructuralStatus.Unsupported ? "Structural: " : "Correction: ";
                list.Add(i.Where != Vector3.zero ? new InspectionItem(tag + i.Note, i.Where) : new InspectionItem(tag + i.Note));
            }
            return list;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => Register();
    }
}
