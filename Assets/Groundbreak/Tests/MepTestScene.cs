using System.Collections;
using UnityEngine;

namespace Groundbreak.Tests
{
    /// <summary>Clears pieces / MEP networks left by other tests so each MEP test (and the tests after it) start clean.</summary>
    public static class MepTestScene
    {
        public static IEnumerator Clear()
        {
            foreach (var m in Object.FindObjectsByType<InspectionMarker>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            foreach (var r in Object.FindObjectsByType<NetRun>(FindObjectsSortMode.None)) Object.Destroy(r.gameObject);
            foreach (var n in Object.FindObjectsByType<NetNode>(FindObjectsSortMode.None)) Object.Destroy(n.gameObject);
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) Object.Destroy(p.gameObject);
            MepInspection.ClearMarkers();
            MepInspection.LastFindings.Clear();
            yield return null;
            yield return null;
        }
    }
}
