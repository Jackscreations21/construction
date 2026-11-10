using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Other trades' inspection rules show up in the shared Inspections framework.</summary>
    public class InspectionBridgeTests
    {
        [UnityTest]
        public IEnumerator MepRulesAreRoughMepInspections()
        {
            yield return null;
            Assert.Greater(MepInspection.Rules.Count, 0, "MEP registered no rules");
            MepBridge.Sync();
            Assert.AreEqual(MepInspection.Rules.Count, MepBridge.Bridged, "every MEP rule bridged");
            var bridged = Inspections.Rules.Where(r => r.Id.StartsWith(MepBridge.Prefix)).ToList();
            Assert.AreEqual(MepInspection.Rules.Count, bridged.Count);
            Assert.IsTrue(bridged.All(r => r.Phase == InspectionPhase.RoughMEP), "MEP rules belong to the rough-in inspection");
            foreach (var t in new[] { Trade.Plumbing, Trade.Electrical, Trade.Hvac })
                Assert.IsTrue(MepInspection.Rules.Any(r => r.Trade == t) == bridged.Any(r => r.Description.StartsWith(t + ":")), "trade " + t);
            // dry run of the rough-in phase must not throw (empty world parts are fine)
            var res = Inspections.Evaluate(InspectionPhase.RoughMEP);
            Assert.NotNull(res);
            Debug.Log($"[Test] MEP bridged {MepBridge.Bridged}, rough-in dry run: {res.Issues.Count} issue(s)");
            var fr = Inspections.Rules.FirstOrDefault(r => r.Id == FramingBridge.RuleId);
            Assert.NotNull(fr, "framing structure rule bridged");
            Assert.AreEqual(InspectionPhase.Framing, fr.Phase);
            Assert.NotNull(fr.Check(), "framing check runs");
            Assert.AreEqual(InspectionPhase.Insulation, CloseupBridge.MapPhase("Drywall"));
            Assert.AreEqual(InspectionPhase.Final, CloseupBridge.MapPhase("Exterior"));
        }
    }
}
