using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>The town: layout API, houses with repair anchors and fixtures, streets/driveways, store, and a look at it.</summary>
    public class TownTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        float _hours;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Town.Destroy();
            if (GameClock.I != null && _hours > 0) GameClock.I.Hours = _hours;
            yield return Frames(2);
        }

        [Test]
        public void LayoutApi()
        {
            Assert.AreEqual(17, Town.Addresses.Count());
            Assert.GreaterOrEqual(Town.FreeLots.Count(), 12);
            var home = Town.Home;
            Assert.NotNull(home); Assert.AreEqual(LotUse.PlayerHome, home.Use); Assert.AreEqual("108 Main St", home.Address);
            Assert.AreSame(home, Town.Find("108 main st"));
            Assert.AreSame(home, Town.LotAt(home.Parking));
            Assert.IsTrue(Town.TryGetParking("main-104", out var p, out _));
            Assert.AreSame(Town.Find("main-104"), Town.LotAt(p));
            Assert.IsNull(Town.LotAt(new Vector3(0, 0, Town.MainZ)), "street is not a lot");
            Assert.IsTrue(Town.OnRoad(new Vector3(0, 0, Town.MainZ)));
            Assert.AreEqual(Town.Home.Parking, Town.HQPosition);
            foreach (var a in Town.Lots)
                foreach (var b in Town.Lots)
                    if (a != b) Assert.IsFalse(a.Rect.Overlaps(b.Rect), $"{a.Id} overlaps {b.Id}");
            var free = Town.FreeLots.First();
            Assert.IsTrue(Town.Claim(free, LotUse.Company, "test"));
            Assert.IsFalse(Town.Claim(free, LotUse.Company, "test"));
            Town.Release(free);
            Assert.IsTrue(free.IsFree);
        }

        [UnityTest]
        public IEnumerator TownBuildsWithHousesAndAnchors()
        {
            yield return Frames(2);
            Town.Ensure();
            yield return Frames(2);
            Assert.IsTrue(Town.Exists);
            var spots = (HouseSpot[])Enum.GetValues(typeof(HouseSpot));
            var fixtures = new[] { HouseSpot.OutletLiving, HouseSpot.OutletKitchen, HouseSpot.BreakerPanel, HouseSpot.InteriorDoor, HouseSpot.FrontDoor,
                HouseSpot.DeckBoard, HouseSpot.SidewalkSquare, HouseSpot.FencePicket };
            foreach (var lot in Town.Lots.Where(l => l.Use == LotUse.Customer || l.Use == LotUse.PlayerHome))
            {
                var h = lot.House;
                Assert.NotNull(h, lot.Id + " has no house");
                foreach (var s in spots)
                {
                    Assert.IsTrue(h.Has(s), $"{lot.Id} missing {s}");
                    var a = h.Anchor(s).position;
                    if (s == HouseSpot.SidewalkSquare)
                    {
                        Assert.IsFalse(lot.Contains(a)); Assert.IsNull(Town.LotAt(a), "sidewalk square is in the right of way");
                        Assert.Less(Vector3.Distance(a, lot.FrontMid), 15f);
                    }
                    else Assert.IsTrue(lot.Contains(a), $"{lot.Id} {s} anchor {a} outside the lot");
                }
                foreach (var s in fixtures) Assert.NotNull(h.Fixture(s), $"{lot.Id} missing fixture {s}");
                Assert.Greater(h.EaveHeight, 2.5f); Assert.Greater(h.DeckBoardLength, 3f);

                // the van parks on a hard surface just above grade
                Assert.IsTrue(Physics.Raycast(lot.Parking + Vector3.up * 3f, Vector3.down, out var hit, 5f), lot.Id + " nothing under parking");
                Assert.That(hit.point.y, Is.InRange(0f, 0.2f), lot.Id + " parking height");
                Assert.AreEqual("TownStreets", hit.collider.name, lot.Id + " parking surface");

                // walk in through the (open) front door
                var door = h.Fixture(HouseSpot.FrontDoor).GetComponent<TownDoor>();
                Assert.NotNull(door); Assert.IsTrue(door.Open);
                yield return null;
                Vector3 from = lot.FrontDoor + Vector3.up * 1.2f, dir = -lot.Front;
                Assert.IsFalse(Physics.Raycast(from, dir, 3f), lot.Id + " front door blocked");
            }

            // nothing of the town inside the build site
            var site = Town.BuildSiteRect;
            var inner = Rect.MinMaxRect(site.xMin + 1, site.yMin + 1, site.xMax - 1, site.yMax - 1);
            int tris = 0;
            foreach (var r in Town.Root.GetComponentsInChildren<Renderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                if (r.name == "TownStreets" || r.name == "TownMarkings") continue;
                var b = r.bounds;
                Assert.IsFalse(inner.Overlaps(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z)), r.name + " inside the build site");
            }
            int renderers = Town.Root.GetComponentsInChildren<Renderer>().Length;
            Debug.Log($"[Town] {renderers} renderers, {tris} triangles");
            Assert.Less(tris, 600000); Assert.Less(renderers, 500);
        }

        [UnityTest]
        public IEnumerator TownShots()
        {
            yield return Frames(2);
            Town.Ensure();
            if (GameClock.I != null) { _hours = GameClock.I.Hours; GameClock.I.Hours = 11f; }
            yield return Frames(3);
            var h = Town.Find("main-104").House;
            var lot = h.Lot;
            SiteLookTests.Shot(new Vector3(-150, 120, -260), new Vector3(0, 0, -90), "town_01_aerial", 55f);
            SiteLookTests.Shot(new Vector3(-60, 1.6f, Town.MainZ - 2f), new Vector3(60, 1.2f, Town.MainZ - 2f), "town_02_main_st");
            SiteLookTests.Shot(lot.FrontMid + lot.Front * 6f + Vector3.up * 1.7f + lot.Rotation * Vector3.right * 4f, h.transform.position + Vector3.up * 2f, "town_03_house_front");
            SiteLookTests.Shot(h.transform.TransformPoint(new Vector3(-0.3f, 1.75f, 3.2f)), h.Anchor(HouseSpot.KitchenSink).position + Vector3.up * 0.6f, "town_04_living_kitchen");
            SiteLookTests.Shot(h.transform.TransformPoint(new Vector3(1.4f, 1.6f, -0.9f)), h.Anchor(HouseSpot.BathVanity).position + Vector3.up * 0.5f, "town_05_bath");
            SiteLookTests.Shot(h.transform.TransformPoint(new Vector3(4f, 2.2f, -12f)), h.Anchor(HouseSpot.DeckBoard).position, "town_06_backyard_deck_fence");
            var st = Town.Store;
            SiteLookTests.Shot(st.FrontMid + st.Front * 10f + Vector3.up * 2f, st.Local(0, 30) + Vector3.up * 3f, "town_07_store");
            var home = Town.Home;
            SiteLookTests.Shot(home.FrontMid + home.Front * 6f + Vector3.up * 1.8f + home.Rotation * Vector3.right * 10f, home.Parking, "town_08_home_driveway");
            SiteLookTests.Shot(new Vector3(Town.OakX + 14, 4f, Town.MainZ - 14), new Vector3(Town.OakX, 0, Town.MainZ), "town_09_intersection");
        }
    }
}
