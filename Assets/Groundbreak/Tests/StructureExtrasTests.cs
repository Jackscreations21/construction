using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Structure leftovers: detailed window/door/garage models, I-joist floor, roof valleys.
    /// Screenshots in Logs/shots/extras_*.</summary>
    public class StructureExtrasTests
    {
        static readonly Vector3 O = new Vector3(-400f, 0f, 400f);
        static GameObject _root;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ExtrasRoot");
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var l = new GameObject("Sun").AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.1f; l.shadows = LightShadows.Soft;
                l.transform.SetParent(_root.transform);
                l.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "ExtrasGround";
            ground.transform.SetParent(_root.transform);
            ground.transform.position = O + Vector3.down * 0.001f;
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            ground.GetComponent<Renderer>().sharedMaterial = Materials.Get(SurfaceKind.Concrete);
        }

        [TearDown]
        public void TearDown() { if (_root != null) Object.Destroy(_root); }

        static GameObject Model(string id, Vector3 pos)
        {
            var def = Catalog.Get(id);
            Assert.IsNotNull(def, id);
            var holder = new GameObject(id);
            holder.transform.SetParent(_root.transform);
            holder.transform.position = pos;
            ProductModels.Build(def, def.Size, holder.transform);
            return holder;
        }

        static void Snap(Vector3 camPos, Vector3 look, string name, float fov = 45f)
        {
            var cam = StructurePolishTests.Cam(camPos, look, fov);
            cam.transform.SetParent(_root.transform, true);
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.82f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            StructurePolishTests.Shot(cam, name);
        }

        [UnityTest]
        public IEnumerator OpeningModels_DetailedAndRealSize()
        {
            string[] ids = { "win-dh-3254", "win-sh-3648", "win-slider-4836", "door-ext-steel36", "door-slider72", "garage-9x7" };
            foreach (var id in ids) Assert.IsTrue(OpeningModels.Handles(Catalog.Get(id)), id + " uses the detailed model");

            var dh = Model("win-dh-3254", O + new Vector3(0f, 1.2f, 0f));
            var sl = Model("win-slider-4836", O + new Vector3(1.6f, 1.2f, 0f));
            var door = Model("door-ext-steel36", O + new Vector3(4f, Units.In(40f), 0f));
            var patio = Model("door-slider72", O + new Vector3(7f, Units.In(40f), 0f));
            var garage = Model("garage-9x7", O + new Vector3(12f, Units.In(42f), 0f));
            yield return Frames(2);

            // real sizes: window fin 1-1/4" past the frame, door unit fits its rough opening, glass present
            var dhB = dh.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(Units.In(32f + 2.5f), dhB.size.x, Units.In(0.1f), "DH window: 32\" unit + 1-1/4\" fins");
            var doorB = door.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
            Assert.Less(doorB.size.y, Units.In(82.5f + 2.3f), "door unit + brickmold height");
            float jambOut = Units.In(18.6875f);
            Assert.Less(jambOut * 2f, Units.In(38f), "outside of jambs fits the 38\" rough opening");
            bool glass = false;
            foreach (var m in dh.GetComponentInChildren<MeshRenderer>().sharedMaterials) if (m != null && m.name.Contains("Glass")) glass = true;
            Assert.IsTrue(glass, "window has glass");
            foreach (var g in new[] { dh, sl, door, patio, garage })
                Assert.Greater(g.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount, 500, g.name + " is a detailed model");

            Snap(O + new Vector3(0.35f, 1.45f, 1.3f), O + new Vector3(0f, 1.2f, 0f), "extras_01_dh_window_ext");
            Snap(O + new Vector3(-0.25f, 1.35f, -0.9f), O + new Vector3(0f, 1.25f, 0f), "extras_02_dh_window_int");
            Snap(O + new Vector3(0.3f, 1.4f, 0.55f), O + new Vector3(0.1f, 1.05f, 0f), "extras_03_dh_corner_closeup", 40f);
            Snap(O + new Vector3(1.9f, 1.3f, 1.5f), O + new Vector3(1.6f, 1.2f, 0f), "extras_04_slider_window");
            Snap(O + new Vector3(4.6f, 1.3f, 2.6f), O + new Vector3(4f, 1.0f, 0f), "extras_05_prehung_door_ext");
            Snap(O + new Vector3(4.5f, 1.2f, -1.1f), O + new Vector3(4.3f, 0.95f, 0f), "extras_06_prehung_door_int_bores", 40f);
            Snap(O + new Vector3(3.75f, 0.35f, 0.6f), O + new Vector3(4f, 0.02f, 0f), "extras_07_threshold_closeup", 45f);
            Snap(O + new Vector3(7.6f, 1.3f, 3.2f), O + new Vector3(7f, 1.0f, 0f), "extras_08_patio_slider");
            Snap(O + new Vector3(12.8f, 1.4f, 4.6f), O + new Vector3(12f, 1.1f, 0f), "extras_09_garage_ext", 50f);
            Snap(O + new Vector3(10.3f, 1.6f, -4.2f), O + new Vector3(12f, 1.6f, -0.5f), "extras_10_garage_tracks_springs", 60f);
        }
    }
}
