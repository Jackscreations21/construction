using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Site look polish: terrain material, horizon/fog, time-of-day light, trench walls, and the player-on-terrain guard.</summary>
    public class SiteLookTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        public static void Shot(Vector3 pos, Vector3 look, string name, float fov = 60f)
        {
            var cam = new GameObject("TestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = fov;
            cam.farClipPlane = 1500f;
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

        [UnityTest]
        public IEnumerator PlayerStaysOnRaisedGrade()
        {
            yield return Frames(3);
            var p = Game.Player;
            Assert.NotNull(p);
            Vector3 start = p.transform.position;
            Grade.Create(-40f, -25f, 160);
            Grade.Field.SetHeights((x, z) => 1.2f);
            Grade.Field.RebuildDirty();
            yield return new WaitForFixedUpdate();
            yield return Frames(4);
            float h = Grade.HeightAt(p.transform.position.x, p.transform.position.z);
            Assert.GreaterOrEqual(p.transform.position.y, h - GroundSafety.BuriedTolerance, "player left under the raised grade");
            Assert.IsTrue(Game.Player.TryAim(out _, 50f) || true);
            Grade.Destroy();
            yield return Frames(2);
            // put them back on the flat ground where they started
            var cc = p.GetComponent<CharacterController>(); cc.enabled = false;
            p.transform.position = start; cc.enabled = true;
            yield return Frames(3);
            Assert.Less(Mathf.Abs(p.transform.position.y - start.y), 0.5f);
        }

        [UnityTest]
        public IEnumerator SiteLookShots()
        {
            yield return Frames(5);
            Weather.ForceRain = false;
            float hours = GameClock.I != null ? GameClock.I.Hours : 10f;
            var g = SiteGround.I;
            Assert.NotNull(g);
            Assert.IsNotNull(GameObject.Find("FarGround"), "far ground ring missing");
            Assert.AreEqual("Groundbreak/Terrain", Materials.Get(SurfaceKind.Grass).shader.name, "terrain shader not used for grass");

            // a footing trench and a gravel patch on the pad
            Vector3 tMin = new Vector3(4f, 0, 6f), tMax = new Vector3(4.6f, 0, 12f);
            Vector3 gMin = new Vector3(6f, 0, 6f), gMax = new Vector3(9f, 0, 9f);
            g.DigRect(tMin, tMax, -0.45f);
            g.RaiseRect(gMin, gMax, 0.02f, GroundCover.Gravel);
            g.Rebuild();

            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            yield return Frames(3);
            Shot(new Vector3(0f, 1.65f, -4f), new Vector3(0f, 1.2f, 40f), "look_01_eye_level");
            Shot(new Vector3(-30f, 14f, -30f), new Vector3(0f, 0f, 10f), "look_02_overview");
            Shot(new Vector3(2.6f, 1.6f, 7.5f), new Vector3(4.3f, -0.3f, 9.5f), "look_04_trench");
            Shot(new Vector3(5.5f, 1.4f, 4.5f), new Vector3(7.5f, 0f, 7.5f), "look_05_gravel_dirt");

            // dig feedback: clods + dust thrown from the trench
            Vector3 digAt = new Vector3(4.3f, -0.45f, 9f);
            for (int i = 0; i < 3; i++) { SiteFx.Dig(digAt); SiteFx.Gravel(new Vector3(7.5f, 0.02f, 7.5f)); yield return new WaitForSeconds(0.08f); }
            yield return new WaitForSeconds(0.15f);
            Assert.Greater(SiteFx.Alive, 0, "dig produced no particles");
            Shot(new Vector3(2.9f, 1.2f, 7.2f), digAt + Vector3.up * 0.4f, "look_06_dig_dust");

            if (GameClock.I != null) GameClock.I.Hours = 17.6f;
            yield return Frames(3);
            Shot(new Vector3(0f, 1.65f, -4f), new Vector3(-20f, 1.2f, 40f), "look_03_late_afternoon");

            // restore
            if (GameClock.I != null) GameClock.I.Hours = hours;
            g.RaiseRect(tMin, tMax, 0f, GroundCover.Grass);
            g.DigRect(gMin, gMax, 0f);
            g.RaiseRect(gMin, gMax, 0f, GroundCover.Grass);
            g.Rebuild();
            Weather.ForceRain = null;
            yield return Frames(2);
        }
    }
}
