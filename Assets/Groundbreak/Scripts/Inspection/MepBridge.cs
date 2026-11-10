using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Feeds MEP's rough-in rules (<see cref="MepInspection.Rules"/>: plumbing, electrical, HVAC) into the shared
    /// <see cref="Inspections"/> framework as <see cref="InspectionPhase.RoughMEP"/> rules (ids "mep:&lt;id&gt;"), so the
    /// inspector NPC walks them and the Inspections app / career phase tracker counts them.
    /// </summary>
    public static class MepBridge
    {
        public const string Prefix = "mep:";
        public static int Bridged { get; private set; }

        /// <summary>(Re)registers every MEP rule. Safe to call any time (same ids replace).</summary>
        public static int Sync()
        {
            Bridged = 0;
            foreach (var r in MepInspection.Rules)
            {
                if (r == null || r.Check == null || string.IsNullOrEmpty(r.Id)) continue;
                var rule = r;
                Inspections.Register(Prefix + rule.Id, InspectionPhase.RoughMEP, $"{rule.Trade}: {rule.Text}", () => Convert(rule));
                Bridged++;
            }
            return Bridged;
        }

        static IEnumerable<InspectionItem> Convert(MepRule r)
        {
            var list = new List<InspectionItem>();
            var found = r.Check();
            if (found == null) return list;
            foreach (var f in found)
            {
                if (f == null) continue;
                string note = $"[{r.Trade}] {(string.IsNullOrEmpty(f.Note) ? r.Text : f.Note)}";
                list.Add(f.Pos != Vector3.zero ? new InspectionItem(note, f.Pos) : new InspectionItem(note));
            }
            return list;
        }

        // MEP rules register from BeforeSceneLoad; bridge them once everything has loaded
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() => Sync();
    }
}
