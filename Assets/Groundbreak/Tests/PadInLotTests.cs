using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Terrain ownership: Grade owns the lot, SiteGround (4" cells) is the building pad cut out of it.</summary>
    public class PadInLotTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Vector3 pos, Vector3 look, string name)
        {
            var cam = new GameObject("TestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = 60;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt); Object.Destroy(cam.gameObject);
        }

        static string HitName(float x, float z)
        {
            return Physics.Raycast(new Vector3(x, 30f, z), Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore) ? hit.collider.name : "(nothing)";
        }

        static float Slope(float x, float z) => -0.06f * (z - 9f) + 0.25f * Mathf.Sin(x * 0.11f); // ~6% fall toward the street

        [UnityTest]
        public IEnumerator SlabPadInsideGradedLot()
        {
            yield return Frames(5);
            var g = SiteGround.I;
            Assert.NotNull(g, "SiteGround not booted");
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            Weather.ForceRain = false;
            var pad = SiteGround.PadRect;
            var ground = GameObject.Find("Ground");
            Assert.NotNull(ground);
            Assert.IsTrue(ground.GetComponent<Renderer>().enabled, "SiteGround must no longer hide the Ground plane");

            // graded lot around the pad
            Grade.Create(-40f, -25f, 160);
            Grade.Field.SetHeights(Slope);
            Grade.Field.RebuildDirty();

            // a footing trench dug in the pad (4" cells) with the concrete tools' API
            Vector3 tMin = new Vector3(-8f, 0, 10f), tMax = new Vector3(-6.5f, 0, 12f);
            g.DigRect(tMin, tMax, -0.3f);
            g.Rebuild();
            yield return new WaitForFixedUpdate(); yield return null;

            // inside the pad: Grade defers to SiteGround
            Assert.IsTrue(Grade.InPad(-7f, 11f));
            Assert.AreEqual(-0.3f, Grade.HeightAt(-7.25f, 11f), 1e-3f, "pad trench depth via Grade.HeightAt");
            Assert.AreEqual(g.HeightAt(new Vector3(3f, 0, 4f)), Grade.HeightAt(3f, 4f), 1e-5f);
            Assert.AreEqual(LandCover.Dirt, Grade.CoverAt(-7.25f, 11f));
            // outside: the lot field
            Assert.AreEqual(Grade.Field.HeightAt(20f, 30f), Grade.HeightAt(20f, 30f), 1e-5f);
            Assert.AreEqual(Slope(20f, 30f), Grade.HeightAt(20f, 30f), 0.05f, "lot slope outside the pad");
            // seam: the lot meets the pad edge
            for (float z = pad.yMin + 0.3f; z < pad.yMax; z += 1.7f)
            {
                Assert.AreEqual(Grade.HeightAt(pad.xMin + 0.01f, z), Grade.HeightAt(pad.xMin - 0.01f, z), 0.03f, $"west seam at z={z}");
                Assert.AreEqual(Grade.HeightAt(pad.xMax - 0.01f, z), Grade.HeightAt(pad.xMax + 0.01f, z), 0.03f, $"east seam at z={z}");
            }
            // grade edits never touch the pad
            float before = Grade.HeightAt(-5f, 5f);
            Grade.Field.Raise(new Vector3(-5f, 0, 5f), 1.5f, 0.5f);
            Grade.Field.RebuildDirty();
            Assert.AreEqual(before, Grade.HeightAt(-5f, 5f), 1e-5f, "Grade.Raise changed the pad");
            // colliders: pad, lot, plane beyond the field
            yield return new WaitForFixedUpdate();
            Assert.AreEqual("SiteGround", HitName(-7.25f, 11f));
            StringAssert.StartsWith("GradeChunk", HitName(20f, 30f));
            StringAssert.StartsWith("GradeChunk", HitName(pad.xMin - 0.1f, 4.03f), "clipped strip beside the pad");
            Assert.AreEqual("Ground", HitName(60f, 60f));

            yield return Frames(3);
            Shot(new Vector3(-17f, 4.5f, -5f), new Vector3(-6f, -0.4f, 9f), "40_pad_in_graded_lot");
            Shot(new Vector3(pad.xMin - 3.5f, 1.7f, 13.5f), new Vector3(pad.xMin + 1.5f, -0.3f, 11f), "41_pad_edge_trench");

            // save / load round-trips both terrains
            float hLot = Grade.HeightAt(20f, 30f);
            Assert.IsTrue(SaveSystem.Save("test_pad"), SaveSystem.LastMessage);
            g.RaiseRect(tMin, tMax, 0f, GroundCover.Grass); g.Rebuild();
            Grade.Destroy();
            Assert.AreEqual(0f, Grade.HeightAt(-7.25f, 11f), 1e-4f);
            yield return Frames(2);
            Assert.IsTrue(SaveSystem.Load("test_pad"), SaveSystem.LastMessage);
            yield return Frames(3);
            Assert.NotNull(Grade.Field, "grade field restored");
            Assert.AreEqual(-0.3f, Grade.HeightAt(-7.25f, 11f), 1e-3f, "pad trench restored");
            Assert.AreEqual(GroundCover.Dirt, g.CoverAt(new Vector3(-7.25f, 0, 11f)), "pad cover restored");
            Assert.AreEqual(hLot, Grade.HeightAt(20f, 30f), 1e-4f, "lot heights restored");

            // leave the shared world as we found it
            SaveSystem.Delete("test_pad");
            g.RaiseRect(tMin, tMax, 0f, GroundCover.Grass); g.Rebuild();
            Grade.Destroy();
            Weather.ForceRain = null;
            yield return null;
            Assert.IsTrue(ground.GetComponent<MeshCollider>() == null || HitName(60f, 60f) == "Ground");
            Assert.AreEqual("SiteGround", HitName(1f, 1f));
        }
    }
}
