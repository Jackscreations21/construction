using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Model polish: the two-piece toilet (catalog prop + house fixture shape), the work van body/wheels, and bare
    /// winter trees → Logs/shots/smallfix_*.png. Everything is built far from the site and removed in teardown.</summary>
    public class SmallFixesModelTests
    {
        static readonly Vector3 Spot = new Vector3(-900f, 60f, -900f);
        GameObject _root;
        float _hours = -1f; int _month; GameMode _mode;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(2);
            _month = SeasonWeather.FreeMonth; _mode = GameSettings.Mode;
            if (GameClock.I != null) { _hours = GameClock.I.Hours; GameClock.I.Hours = 11f; }
            _root = new GameObject("SmallFixesModels");
            _root.transform.position = Spot;
            // floor slab and a back wall (wall face on z = 0 of the root)
            var k = new LandModel();
            k.B.AddBox(new Vector3(0f, -0.05f, 4f), new Vector3(20f, 0.1f, 20f), k.M(SurfaceKind.Concrete));
            k.B.AddBox(new Vector3(0f, 1.2f, -0.05f), new Vector3(2.4f, 2.4f, 0.1f), k.M(SurfaceKind.PaintedWhite, new Color(0.85f, 0.88f, 0.92f)));
            k.Make(_root.transform, "Floor");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            SeasonWeather.FreeMonth = _month; GameSettings.Mode = _mode;
            if (GameClock.I != null && _hours >= 0f) GameClock.I.Hours = _hours;
            yield return Frames(2);
            SeasonLook.Refresh();
        }

        [UnityTest]
        public IEnumerator ToiletIsRealTwoPiece()
        {
            var m = new RM();
            CatalogProps.Build(m, "toilet", new PropCtx { Size = new Vector3(0.45f, 0.8f, 0.7f) });
            var go = m.L.Make(_root.transform, "Toilet");
            var bounds = go.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.That(bounds.size.x, Is.InRange(0.48f, 0.56f), "tank ~20\" wide");
            Assert.That(bounds.max.z, Is.InRange(0.70f, 0.76f), "28-30\" from the wall to the bowl front");
            Assert.That(ToiletModel.TankTop + ToiletModel.LidT, Is.InRange(0.74f, 0.79f), "29-31\" tall at the tank lid");
            Assert.That(ToiletModel.RimY, Is.InRange(0.37f, 0.39f), "15\" rim");

            // the house fixture uses the same shape (facing -Z, as in HouseBuilder): open tank, seat lid down
            var h = new LandModel();
            ToiletModel.Build(h.B, new Vector3(-1.0f, 0f, 1.6f), Quaternion.LookRotation(Vector3.back), h.M(SurfaceKind.Porcelain), h.M(SurfaceKind.PaintedWhite), h.M(SurfaceKind.Nickel), h.M(SurfaceKind.Nickel), false, false);
            h.Make(_root.transform, "ToiletOpenTank");
            yield return Frames(2);
            SiteLookTests.Shot(Spot + new Vector3(0.95f, 1.15f, 1.45f), Spot + new Vector3(-0.1f, 0.38f, 0.45f), "smallfix_toilet", 55f);
        }

        [UnityTest]
        public IEnumerator VanKeepsRealSize()
        {
            var t = new GameObject("Van").transform;
            t.SetParent(_root.transform, false);
            t.localPosition = new Vector3(0f, 0f, 6f);
            t.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var body = WorkVanModel.BuildBody(t);
            WorkVanModel.BuildRearDoor(t, true);
            WorkVanModel.BuildRearDoor(t, false);
            float r = WorkVanModel.TireRadius, x = WorkVanModel.TrackHalf;
            WorkVanModel.BuildWheel(t, new Vector3(-x, r, WorkVanModel.FrontAxleZ));
            WorkVanModel.BuildWheel(t, new Vector3(x, r, WorkVanModel.FrontAxleZ));
            WorkVanModel.BuildWheel(t, new Vector3(-x, r, WorkVanModel.RearAxleZ));
            WorkVanModel.BuildWheel(t, new Vector3(x, r, WorkVanModel.RearAxleZ));
            WorkVanModel.BuildRackLadder(t);
            var b = body.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.That(b.size.z, Is.InRange(5.9f, 6.1f), "full-size cargo van length");
            Assert.That(b.max.y, Is.InRange(2.05f, 2.5f), "roof + rack height");
            yield return Frames(2);
            var c = t.position;
            SiteLookTests.Shot(c + new Vector3(5.2f, 1.6f, 5.4f), c + new Vector3(1.2f, 0.9f, 0f), "smallfix_van", 55f);
            SiteLookTests.Shot(c + new Vector3(-6.5f, 1.4f, -3.2f), c + new Vector3(-1.0f, 0.8f, 0f), "smallfix_van_rear", 55f);
        }

        [UnityTest]
        public IEnumerator WinterTreeShowsBranches()
        {
            var tree = new LandModel();
            LandModels.Deciduous(tree, 11, 3f, LandModels.LeafMaple);
            var go = tree.Make(_root.transform, "WinterTree");
            go.transform.localPosition = new Vector3(0f, 0f, 9f);
            var tree2 = new LandModel();
            LandModels.Deciduous(tree2, 5, 2f, LandModels.LeafMaple);
            tree2.Make(_root.transform, "WinterTree2").transform.localPosition = new Vector3(-5f, 0f, 12f);
            GameSettings.Mode = GameMode.FreeBuild;
            SeasonWeather.FreeMonth = 0;          // January
            yield return Frames(2);
            SeasonLook.Refresh();
            yield return Frames(2);
            if (SeasonLook.I != null && GameSettings.WeatherEnabled) StringAssert.StartsWith("Winter/", SeasonLook.LookKey);
            SiteLookTests.Shot(Spot + new Vector3(4f, 2.0f, 22f), Spot + new Vector3(-1.5f, 5.5f, 9f), "smallfix_winter_tree", 60f);
        }
    }
}
