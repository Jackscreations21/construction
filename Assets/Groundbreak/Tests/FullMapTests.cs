using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>The full map (Scripts/Map): layout, districts, road network, job-capable addresses, the 3D build and screenshots (Logs/shots/map_*).</summary>
    public class FullMapTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        float _hours = -1f;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (WorkVan.I != null) { WorkVan.I.ClearAutopilot(); WorkVan.Despawn(); }
            Town.Destroy();
            Company.ResetState();
            MapStreamer.Frozen = false;
            if (GameClock.I != null && _hours >= 0) GameClock.I.Hours = _hours;
            yield return Frames(2);
        }

        static readonly string[] DistrictIds = { "oldtown", "downtown", "westside", "school", "north", "eastside", "industrial", "strip", "farms", "devland" };

        [Test]
        public void LayoutHasDistrictsLotsAndPlans()
        {
            MapLayout.Ensure();
            foreach (var id in DistrictIds) Assert.NotNull(MapLayout.District(id), "missing district " + id);
            var lots = Town.Lots.ToList();
            Assert.Greater(lots.Count(l => l.External), 250, "map lots");
            // no two parcels overlap, every map lot is inside the map and off every street right-of-way
            for (int i = 0; i < lots.Count; i++)
                for (int j = i + 1; j < lots.Count; j++)
                    Assert.IsFalse(lots[i].Rect.Overlaps(lots[j].Rect), $"{lots[i].Id} overlaps {lots[j].Id}");
            foreach (var l in lots.Where(l => l.External))
            {
                Assert.IsTrue(MapLayout.Bounds.Overlaps(l.Rect), l.Id + " outside the map");
                foreach (var r in MapLayout.Roads)
                {
                    var row = r.EW ? Rect.MinMaxRect(r.Min, r.Perp - r.RowHalf + 0.05f, r.Max, r.Perp + r.RowHalf - 0.05f) : Rect.MinMaxRect(r.Perp - r.RowHalf + 0.05f, r.Min, r.Perp + r.RowHalf - 0.05f, r.Max);
                    Assert.IsFalse(row.Overlaps(l.Rect), $"{l.Id} is in the right-of-way of {r.Name}");
                }
                Assert.AreSame(l, Town.Find(l.Id)); Assert.AreSame(l, Town.Find(l.Address));
                Assert.AreSame(l, MapLayout.DistrictAt(l.Center) != null ? l : null, l.Id + " in no district");
                Assert.IsFalse(Town.BuildSiteRect.Overlaps(l.Rect), l.Id + " in the build site");
            }
            // job-capable addresses in every built-up district, free lots for new builds in several
            foreach (var id in DistrictIds.Where(d => d != "devland"))
                Assert.Greater(Town.Addresses.Count(l => l.External && l.District == id) + (id == "oldtown" ? 17 : 0), 0, "no job sites in " + id);
            foreach (var id in new[] { "downtown", "westside", "north", "industrial", "strip", "devland" })
                Assert.Greater(Town.FreeLots.Count(l => l.District == id), 0, "no free lots in " + id);
            Assert.Greater(Town.FreeLots.Count(l => l.District == "devland" && l.AreaSqFt > 30000f), 1, "development tracts");
            // building types / plans: every map building has a type; plan ids resolve where a plan exists
            var buildings = Town.Addresses.Where(l => l.External).ToList();
            Assert.Greater(buildings.Count, 200);
            foreach (var l in buildings) { Assert.IsFalse(string.IsNullOrEmpty(l.TypeId), l.Id + " has no type"); Assert.IsTrue(l.TypeId == "ranch" || MapModels.Has(l.TypeId), l.Id + " unknown model " + l.TypeId); }
            Assert.NotNull(MapLayout.PlanFor(buildings.First(l => l.TypeId == "ranch")), "ranch plan");
            Assert.IsNull(MapLayout.PlanFor(buildings.First(l => l.TypeId == "storefront" && HousePlans.Get(l.PlanId) == null) ), "missing plans are tolerated");
            // repairs only go to enterable houses; Fleet contractor jobs only to residential lots
            Assert.IsTrue(Town.Addresses.Where(l => l.ServiceCalls).All(l => !l.External || l.TypeId == "ranch"));
            Assert.Greater(Town.Addresses.Count(l => l.ServiceCalls && l.External), 8, "map ranches take service calls");
            Debug.Log($"[Map] {lots.Count} lots ({lots.Count(l => l.External)} map), {buildings.Count} map addresses, {Town.FreeLots.Count()} free, " +
                      string.Join(", ", DistrictIds.Select(d => d + " " + Town.Lots.Count(l => l.District == d))));
        }

        [Test]
        public void RoadNetworkIsConnected()
        {
            MapLayout.Ensure();
            var roads = MapLayout.Roads;
            bool Touch(MapRoad a, MapRoad b)
            {
                if (a.EW == b.EW) return Mathf.Abs(a.Perp - b.Perp) < 0.1f && (Mathf.Abs(a.Max - b.Min) < 0.6f || Mathf.Abs(b.Max - a.Min) < 0.6f);
                var ew = a.EW ? a : b; var ns = a.EW ? b : a;
                return ns.Perp >= ew.Min - 0.5f && ns.Perp <= ew.Max + 0.5f && ew.Perp >= ns.Min - 0.5f && ew.Perp <= ns.Max + 0.5f;
            }
            var seen = new HashSet<MapRoad> { roads.First(r => r.Core && r.Name == "Main St") };
            var q = new Queue<MapRoad>(seen);
            while (q.Count > 0) { var r = q.Dequeue(); foreach (var o in roads) if (!seen.Contains(o) && Touch(r, o)) { seen.Add(o); q.Enqueue(o); } }
            foreach (var r in roads) Assert.IsTrue(seen.Contains(r), $"{r.Name} ({r.S.A} → {r.S.B}) is not connected");
            foreach (var d in MapLayout.Districts)
                Assert.Less(Vector3.Distance(Town.NearestRoadPoint(d.View), new Vector3(d.View.x, 0, d.View.z)), 30f, d.Id + " view is far from any road");
            Assert.IsTrue(Town.OnRoad(new Vector3(-400f, 0, Town.MainZ + 5f)), "downtown Main St is 44' wide");
            Assert.IsTrue(Town.OnRoad(new Vector3(800f, 0, -330f)), "Route 9");
        }

        [UnityTest]
        public IEnumerator MapBuildsAndVanCanDriveEverywhere()
        {
            yield return Frames(2);
            Town.Ensure();
            yield return Frames(2);
            Assert.IsTrue(MapWorld.Exists, "map built with the town");
            var st = MapWorld.Last;
            Debug.Log($"[Map] stats: cells {st.Cells}, renderers {st.Renderers}, triangles {st.Triangles}, colliders {st.Colliders}, buildings {st.Buildings}, trees {st.Trees}, {st.Seconds:0.00} s");
            Assert.Greater(st.Buildings, 250); Assert.Greater(st.Trees, 1500);
            Assert.Less(st.Seconds, 25f, "map build time");

            // every lane of every map road: ground under the wheels and nothing in the way of a van body
            var cols = new Collider[8];
            int samples = 0;
            foreach (var r in MapLayout.Roads)
            {
                if (r.Core) continue;
                float lane = Mathf.Min(1.9f, r.Half - 1.2f);
                for (float a = r.Min + 2f; a <= r.Max - 2f; a += 4f)
                {
                    var c = r.At(a);
                    if (!MapLayout.Bounds.Contains(new Vector2(c.x, c.z))) continue;
                    if (Mathf.Abs(a - r.Min) < 3f || Mathf.Abs(a - r.Max) < 3f) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var p = r.At(a, side * lane, 0f);
                        Assert.IsTrue(Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 3f), $"{r.Name} no ground at {p}");
                        Assert.That(hit.point.y, Is.InRange(-0.02f, 0.2f), $"{r.Name} road height at {p}");
                        int n = Physics.OverlapBoxNonAlloc(p + Vector3.up * 1.25f, new Vector3(0.9f, 0.75f, 0.9f), cols, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                        for (int i = 0; i < n; i++)
                            Assert.Fail($"{r.Name} lane blocked at {p} by {cols[i].name} ({cols[i].bounds.center})");
                        samples++;
                    }
                }
            }
            Debug.Log($"[Map] {samples} lane samples clear");

            // the van: fast travel to a job site in every district, and a real drive along Route 9
            Company.AddVehicle("van", "Work Van");
            var van = WorkVan.EnsureSpawned();
            Assert.NotNull(van);
            foreach (var id in DistrictIds)
            {
                var lot = Town.Addresses.Where(l => l.District == id).OrderBy(l => l.Id).FirstOrDefault() ?? Town.Lots.First(l => l.District == id);
                Assert.IsTrue(WorkVan.FastTravel(lot), "fast travel to " + lot.Id);
                yield return Frames(3);
                var v = van.transform.position;
                Assert.Less(Vector3.Distance(new Vector3(v.x, 0, v.z), new Vector3(lot.Parking.x, 0, lot.Parking.z)), 1.5f, lot.Id);
                Assert.That(v.y, Is.InRange(-0.05f, 0.4f), lot.Id + " van height");
                Assert.Greater(WorkVan.RoadDistance(Town.Home.Parking, lot.Parking), 10f);
            }
            van.TeleportTo(new Vector3(-200f, 0.05f, -330f - 1.9f), 90f);
            yield return Frames(3);
            float x0 = van.transform.position.x;
            van.SetAutopilot(1f, 0f, false);
            for (int i = 0; i < 240; i++) yield return new WaitForFixedUpdate();
            van.SetAutopilot(0f, 0f, true);
            yield return Frames(5);
            Assert.Greater(van.transform.position.x - x0, 15f, "van drove east on Route 9");
            Assert.Less(Mathf.Abs(van.transform.position.z - (-331.9f)), 1.5f, "van stayed in its lane");
        }

        [UnityTest]
        public IEnumerator JobSitesFreeLotsAndRanches()
        {
            yield return Frames(2);
            Town.Ensure();
            yield return Frames(2);
            // a sample of job-capable buildings in every district: parking on a hard surface, clear of buildings and parked cars
            foreach (var id in DistrictIds)
                foreach (var lot in Town.Addresses.Where(l => l.External && l.District == id).Take(6))
                {
                    Assert.IsTrue(Physics.Raycast(lot.Parking + Vector3.up * 3f, Vector3.down, out var hit, 5f), lot.Id + " nothing under parking");
                    Assert.That(hit.point.y, Is.InRange(-0.02f, 0.3f), lot.Id + " parking height");
                    Assert.IsFalse(Physics.CheckBox(lot.Parking + Vector3.up * 1.3f, new Vector3(0.9f, 0.7f, 0.9f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), lot.Id + " parking blocked");
                    Assert.IsTrue(lot.Contains(lot.Parking) || Town.OnRoad(lot.Parking) || Town.LotAt(lot.Parking) == null, lot.Id + " parks on someone else's lot");
                }
            // map ranches are enterable TownHouses with repair anchors
            foreach (var p in MapLayout.Parcels.Where(p => p.Ranch))
            {
                Assert.NotNull(p.Lot.House, p.Lot.Id);
                Assert.IsTrue(p.Lot.Contains(p.Lot.House.Anchor(HouseSpot.KitchenSink).position));
            }
            // the generator can send a service call to a map ranch, never to a model-only building
            var offers = new List<TownLot>();
            for (int i = 0; i < 30; i++) foreach (var l in Town.Addresses) if (l.ServiceCalls) offers.Add(l);
            Assert.IsTrue(offers.All(l => l.House != null));
            // Fleet contractor jobs (Integration/LotBroker) land on real lots
            if (TownHook.JobLotProvider != null)
                for (int i = 0; i < 10; i++) { var (c, addr) = TownHook.JobLotProvider(i); Assert.NotNull(Town.Find(addr), addr); }
            // a free lot's meadow dressing disappears when it is claimed
            var free = Town.FreeLots.First(l => l.External && l.District == "north");
            var dressing = MapWorld.Root.transform.Find("Dressing " + free.Id);
            Assert.NotNull(dressing); Assert.IsTrue(dressing.gameObject.activeSelf);
            Assert.IsTrue(Town.Claim(free, LotUse.BuildSite, "test"));
            Assert.IsFalse(dressing.gameObject.activeSelf, "dressing hidden on a claimed lot");
            Town.Release(free);
            Assert.IsTrue(dressing.gameObject.activeSelf);
        }

        static void Shot(Vector3 pos, Vector3 look, string name, float fov = 60f, bool noFog = false)
        {
            bool fog = RenderSettings.fog;
            if (noFog) RenderSettings.fog = false;
            SiteLookTests.Shot(pos, look, name, fov);
            RenderSettings.fog = fog;
        }

        [UnityTest]
        public IEnumerator MapShots()
        {
            yield return Frames(2);
            Town.Ensure();
            if (GameClock.I != null) { _hours = GameClock.I.Hours; GameClock.I.Hours = 10.5f; }
            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            Shot(new Vector3(-250f, 520f, -820f), new Vector3(120f, 0f, 120f), "map_00_aerial_overview", 62f, true);
            Shot(new Vector3(-420f, 160f, -260f), new Vector3(-330f, 0f, -40f), "map_01_aerial_downtown", 55f, true);
            Shot(new Vector3(500f, 140f, 820f), new Vector3(650f, 0f, 600f), "map_02_aerial_devland", 60f, true);
            foreach (var d in MapLayout.Districts)
            {
                var f = Quaternion.Euler(0, d.ViewYaw, 0) * Vector3.forward;
                Shot(d.View, d.View + f * 50f + Vector3.down * 2f, "map_10_" + d.Id);
            }
            var th = Town.Lots.First(l => l.TypeId == "townhall");
            Shot(th.FrontMid + th.Front * 14f + Vector3.up * 1.7f + th.Rotation * Vector3.right * 12f, th.Local(0, 18f) + Vector3.up * 6f, "map_20_town_hall");
            var gr = Town.Lots.First(l => l.TypeId == "grocery");
            Shot(gr.Local(25f, 4f) + Vector3.up * 1.7f, gr.Local(0, 40f) + Vector3.up * 3f, "map_21_grocery_lot");
            var ly = Town.Lots.First(l => l.TypeId == "lumberyard");
            Shot(ly.FrontMid + ly.Front * 8f + Vector3.up * 1.8f, ly.Local(0, 30f) + Vector3.up * 2f, "map_22_lumber_yard");
            var farm = Town.Lots.First(l => l.Category == "farm");
            Shot(farm.Local(-farm.Frontage / 2f + 26f, -4f) + Vector3.up * 1.7f, farm.Local(-farm.Frontage / 2f + 40f, 50f) + Vector3.up * 4f, "map_23_farm");
            var tw = Town.Lots.First(l => l.TypeId == "townhomes");
            Shot(tw.FrontMid + tw.Front * 9f + Vector3.up * 1.7f + tw.Rotation * Vector3.right * 14f, tw.Local(0, 12f) + Vector3.up * 3f, "map_24_townhomes");
            var ranch = Town.Lots.First(l => l.TypeId == "ranch" && l.External);
            Shot(ranch.FrontMid + ranch.Front * 6f + Vector3.up * 1.7f + ranch.Rotation * Vector3.right * 6f, ranch.Local(0, 12f) + Vector3.up * 2f, "map_25_north_ranch");
            // looking out from the player's home driveway in four directions: no empty edges
            var home = Town.Home.Parking + Vector3.up * 1.7f;
            Shot(home, home + Vector3.forward * 50f, "map_30_home_north");
            Shot(home, home + Vector3.back * 50f, "map_31_home_south");
            Shot(home + Vector3.back * 10f, home + Vector3.left * 50f, "map_32_home_west");
            Shot(home + Vector3.back * 10f, home + Vector3.right * 50f, "map_33_home_east");
            Shot(new Vector3(450f, 1.7f, 605f), new Vector3(700f, 1f, 640f), "map_34_county_rd_tracts");
            Shot(new Vector3(-330f + 12f, 1.7f, Town.MainZ - 12f), new Vector3(-330f, 2f, Town.MainZ), "map_35_downtown_corner");
            Shot(new Vector3(612f, 1.7f, 140f), new Vector3(640f, 0.5f, 300f), "map_36_creek");
            MapWorld.ShowAll(false);
        }
    }
}
