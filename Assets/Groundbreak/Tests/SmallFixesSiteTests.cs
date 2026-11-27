using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Small site fixes: stored stock on the warehouse pallet racks reads from the aisle (pallets, sheet stacks,
    /// lumber bundles, bags/cartons/buckets, load labels), and blueprint-overlay room labels stay off the fixture markers.
    /// Screenshots: Logs/shots/smallfix_racking*.png, smallfix_overlay_labels.png.</summary>
    public class SmallFixesSiteTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            PlanOverlay.Hide();
            Company.ResetState(); YardFleetRuntime.ResetAll();
            GameSettings.Mode = GameMode.FreeBuild;
            yield return null;
        }

        [UnityTest]
        public IEnumerator RackedStockReadsFromTheAisle()
        {
            yield return Frames(3);
            Company.ResetState(); YardFleetRuntime.ResetAll();
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            Company.HasWarehouse = true; Company.WarehouseLevel = 1;
            var stock = new Dictionary<string, int>
            {
                { "2x4x8", 40 }, { "2x4x925", 36 }, { "brick-modular", 300 }, { "cmu-8x8x16", 60 }, { "dw-12-4x8", 14 },
                { "dw-mud-ap", 6 }, { "mortar-s80", 12 }, { "nails16d", 6000 }, { "osb716", 20 }, { "ply34", 15 },
            };
            foreach (var kv in stock) Yard.Stock.Add(kv.Key, kv.Value);
            var s = YardSite.Rebuild();
            Assert.NotNull(s, "rented bay built");
            yield return Frames(3);
            s = YardSite.I;

            var loads = new List<MeshRenderer>();
            foreach (var r in s.GetComponentsInChildren<MeshRenderer>()) if (r.name.StartsWith("ShelfLoad ")) loads.Add(r);
            int labels = 0;
            foreach (var t in s.GetComponentsInChildren<TextMesh>()) if (t.name == "LoadLabelText") labels++;
            Assert.GreaterOrEqual(loads.Count, 8, "every non-long item is a load on a beam level");
            Assert.GreaterOrEqual(labels, loads.Count, "each load has a label on the beam");
            foreach (var r in loads)
            {
                var size = r.bounds.size;
                Assert.Greater(size.x, 0.3f, r.name + " has real size"); Assert.Less(size.x, 2.6f, r.name + " fits the 96\" bay");
            }

            // the back-wall rack (largest local z), chest-high level first
            Bounds back = default; bool any = false; float maxZ = float.MinValue;
            foreach (var r in loads) maxZ = Mathf.Max(maxZ, s.transform.InverseTransformPoint(r.bounds.center).z);
            foreach (var r in loads)
            {
                var lc = s.transform.InverseTransformPoint(r.bounds.center);
                if (lc.z < maxZ - 0.8f) continue;
                var lb = new Bounds(lc, r.bounds.size);
                if (!any) { back = lb; any = true; } else back.Encapsulate(lb);
            }
            Assert.IsTrue(any);
            float front = back.min.z;
            Vector3 W(Vector3 local) => s.transform.TransformPoint(local);
            SiteLookTests.Shot(W(new Vector3(back.center.x, 1.65f, front - 4.4f)), W(new Vector3(back.center.x, 1.45f, front + 0.3f)), "smallfix_racking", 62f);
            SiteLookTests.Shot(W(new Vector3(back.min.x + 1.6f, 1.5f, front - 2.2f)), W(new Vector3(back.min.x + 1.2f, 1.3f, front + 0.4f)), "smallfix_racking_close", 60f);
        }

        [UnityTest]
        public IEnumerator OverlayRoomLabelsClearFixtureMarkers()
        {
            yield return Frames(3);
            var lot = DevLots.Provider.Find("ridge-101");
            var plan = HousePlans.Get("ranch-1600");
            Assert.NotNull(lot); Assert.NotNull(plan);
            var at = PlanPlacement.On(lot, plan);
            var root = PlanOverlay.Show(plan, at, 0);
            Assert.NotNull(root);
            Assert.Greater(BlueprintOverlay.FixtureCount, 0, "fixture markers drawn");
            Assert.Greater(PlanLabelLayout.MovedCount, 0, "room labels sitting on fixtures were moved");
            Debug.Log($"[SmallFix] room labels moved: {PlanLabelLayout.MovedCount}, min clearance to fixtures {PlanLabelLayout.MinFixtureClearanceFt:0.00} ft");
            // every room label stays inside its room
            foreach (Transform c in root.transform)
            {
                if (c.name != "PlanLabel") continue;
                var tm = c.GetComponent<TextMesh>();
                if (tm == null || !tm.text.Contains("' x ")) continue;
                var room = plan.Rooms.Find(r => r.Floor == 0 && tm.text.StartsWith(r.Name + "\n"));
                if (room == null) continue;
                Vector3 d = c.position - at.Origin;
                var ft = new Vector2(Vector3.Dot(d, at.X), Vector3.Dot(d, at.Y)) / Units.Foot;
                Assert.IsTrue(room.Ft.Contains(ft), $"{room.Name} label inside the room ({ft})");
            }
            yield return Frames(2);
            var mid = at.World(plan.WidthFt / 2, plan.DepthFt / 2);
            SiteLookTests.Shot(mid + new Vector3(0, 19f, -9f), mid + new Vector3(0, 0, -1f), "smallfix_overlay_labels");
        }
    }
}
