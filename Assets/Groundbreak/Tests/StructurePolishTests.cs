using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Structure/MEP look polish: lumber tone + grade stamps, OSB panel stamps, nail heads. Screenshots in Logs/shots/polish_*.</summary>
    public class StructurePolishTests
    {
        static readonly Vector3 O = new Vector3(400f, 0f, 400f);

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        public static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        public static Camera Cam(Vector3 pos, Vector3 lookAt, float fov = 50f)
        {
            var cam = new GameObject("PolishCam").AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.01f;
            cam.enabled = false;
            return cam;
        }

        static GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("PolishRoot");
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var l = new GameObject("Sun").AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.1f; l.shadows = LightShadows.Soft;
                l.transform.SetParent(_root.transform);
                l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        [TearDown]
        public void TearDown() { if (_root != null) Object.Destroy(_root); }

        static Piece Lay(string id, float len, Vector3 pos, Quaternion rot)
        {
            var p = Piece.Create(Catalog.Get(id), len, pos, rot);
            p.transform.SetParent(_root.transform, true);
            return p;
        }

        [UnityTest]
        public IEnumerator LumberBunk_StampsAndTone()
        {
            // a bunk of 2x4s and a few PT boards laid flat (wide face up), plus a sheet of OSB beside them
            for (int i = 0; i < 10; i++)
                Lay(i < 8 ? "2x4x12" : "pt2x6x16", Units.In(i < 8 ? 144 : 192), O + new Vector3(i * 0.095f + (i >= 8 ? 0.02f * (i - 7) : 0f), 0.02f, 0f), Quaternion.identity);
            var osb = Lay("osb716", Units.In(96), O + new Vector3(-0.75f, 0.02f, 0.3f), Quaternion.identity);
            yield return Frames(3);

            var mr = osb.GetComponentInChildren<MeshRenderer>();
            bool hasStamp = false;
            foreach (var m in mr.sharedMaterials) if (m != null && m.name.StartsWith("GB_Stamp")) hasStamp = true;
            Assert.IsTrue(hasStamp, "OSB sheet carries a panel stamp submesh");

            var cam = Cam(O + new Vector3(0.1f, 1.6f, -2.6f), O + new Vector3(0.1f, 0f, 0.2f), 55f);
            cam.transform.SetParent(_root.transform, true);
            Shot(cam, "polish_01_bunk");
            cam.transform.position = O + new Vector3(0.35f, 0.55f, 1.25f);
            cam.transform.LookAt(O + new Vector3(0.35f, 0f, 1.85f));
            Shot(cam, "polish_02_stamp_closeup");
            cam.transform.position = O + new Vector3(-0.75f, 1.0f, -0.3f);
            cam.transform.LookAt(O + new Vector3(-0.75f, 0f, 0.4f));
            Shot(cam, "polish_03_osb_closeup");
        }

        [Test]
        public void TiltUp_CurveIsMonotonicAndLandsPlumb()
        {
            float prev = -1f;
            for (int i = 0; i <= 200; i++)
            {
                float a = WallAssembly.RaiseAngle(i / 200f);
                Assert.GreaterOrEqual(a, prev - 1e-4f, "never drops back while raising");
                Assert.Less(a - prev, 1.6f, "no jumps");
                prev = a;
            }
            Assert.AreEqual(0f, WallAssembly.RaiseAngle(0f), 1e-4f);
            Assert.AreEqual(90f, WallAssembly.RaiseAngle(1f), 1e-4f);
        }

        [UnityTest]
        public IEnumerator LayoutGhost_HasOutline()
        {
            for (int i = 0; i < 4; i++)
                Lay("2x4x12", Units.In(92.625f), O + new Vector3(6f + i * Units.In(16f), Units.In(92.625f) * 0.5f, 0f), Quaternion.LookRotation(Vector3.up, Vector3.forward));
            var job = FramingJob.Create("Ghost test");
            job.transform.SetParent(_root.transform, true);
            Vector3 gc = O + new Vector3(6f + 4 * Units.In(16f), Units.In(92.625f) * 0.5f, 0f);
            job.Add("Nail stud", ToolSets.Any, null, () => (gc, new Vector3(Units.In(3.5f), Units.In(92.625f), Units.In(1.5f)), Quaternion.identity));
            yield return Frames(3);
            var outline = GameObject.Find("LayoutGhost/Outline");
            Assert.IsNotNull(outline, "layout ghost draws an outline");
            var cam = Cam(O + new Vector3(6.6f, 1.3f, -1.6f), O + new Vector3(6.6f, 1.1f, 0f), 55f);
            cam.transform.SetParent(_root.transform, true);
            Shot(cam, "polish_05_layout_ghost");
            Object.Destroy(job.gameObject);
        }

        [UnityTest]
        public IEnumerator Nailing_LeavesHeads()
        {
            var p = Lay("2x4x12", Units.In(48), O + new Vector3(3f, 0.02f, 0f), Quaternion.identity);
            yield return Frames(2);
            int before = p.GetComponentsInChildren<MeshFilter>().Length;
            Physics.SyncTransforms();
            var col = p.GetComponentInChildren<Collider>();
            Assert.IsNotNull(col, "piece has a collider");
            for (int i = 0; i < 4; i++)
            {
                var start = O + new Vector3(3f + (i % 2 == 0 ? -0.012f : 0.014f), 1f, -0.25f + i * 0.17f);
                Assert.IsTrue(col.Raycast(new Ray(start, Vector3.down), out RaycastHit hit, 2f), "ray hits the board");
                FastenFeedback.Nailed(p, hit, 0f);
            }
            Assert.AreEqual(before + 4, p.GetComponentsInChildren<MeshFilter>().Length);
            var cam = Cam(O + new Vector3(3.15f, 0.30f, -0.35f), O + new Vector3(3f, 0.02f, 0.05f), 40f);
            cam.transform.SetParent(_root.transform, true);
            Shot(cam, "polish_04_nail_heads");
        }
    }
}
