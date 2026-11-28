using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Yard equipment models (YardEquipmentModels): every item bought at the HQ warehouse, close-ups and a wide
    /// shot of each. Screenshots → Logs/shots/yardmodels_*.png.</summary>
    public class YardModelsTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Vector3 pos, Vector3 look, string name, float fov = 55f)
        {
            var cam = new GameObject("YardModelsTestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 600f;
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

        int _money, _rep;

        [SetUp]
        public void Begin()
        {
            _money = Game.Wallet.Money; _rep = Career.Reputation;
            GameSettings.Mode = GameMode.Career; GameSettings.UnlimitedMoney = false;
            Company.ResetState(); YardFleetRuntime.ResetAll();
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            Weather.ForceRain = false;
        }

        [TearDown]
        public void End()
        {
            Company.ResetState(); YardFleetRuntime.ResetAll();
            GameSettings.Mode = GameMode.FreeBuild; Weather.ForceRain = null;
            Career.Reputation = _rep;
            int d = _money - Game.Wallet.Money;
            if (d > 0) Game.Wallet.Add(d, "test restore"); else if (d < 0) Game.Wallet.TrySpend(-d, "test restore");
        }

        static void Cash(int amount) { if (Game.Wallet.Money < amount) Game.Wallet.Add(amount - Game.Wallet.Money, "test cash"); }

        /// <summary>Camera at a point in an equipment model's local frame, looking at another local point.</summary>
        static void ShotAt(YardSite s, string id, Vector3 camLocal, Vector3 lookLocal, string name, float fov = 55f)
        {
            Assert.IsTrue(s.TryEquipmentPose(id, out var p, out var r), id);
            Shot(p + r * camLocal, p + r * lookLocal, name, fov);
        }

        [UnityTest]
        public IEnumerator EquipmentModels_BayAndHQ_Screenshots()
        {
            yield return Frames(3);
            Cash(400000);
            // level 1 bay with what a bay can own
            Assert.IsTrue(Yard.Lease(1, out var msg), msg);
            foreach (var id in new[] { "sawstation", "compressor", "forklift" }) Assert.IsTrue(Yard.BuyEquipment(id, out msg), msg);
            yield return Frames(3);
            var s = YardSite.I; Assert.NotNull(s);
            Shot(s.InteriorCameraSpot, s.InteriorCameraLook, "yardmodels_0_bay", 70f);

            // largest premises with every item
            Cash(400000);
            Assert.IsTrue(Yard.Lease(3, out msg), msg);
            foreach (var e in Yard.Equipment) if (!Yard.HasEquipment(e.Id)) Assert.IsTrue(Yard.BuyEquipment(e.Id, out msg), msg);
            yield return Frames(4);
            s = YardSite.I; Assert.NotNull(s); Assert.AreEqual(3, s.Level);
            var eq = s.transform.Find("Contents/ShopEquipment"); Assert.NotNull(eq);
            var b = eq.GetComponent<Renderer>().bounds;
            Assert.That(b.size.y, Is.InRange(3.6f, 4.2f), "two-post lift is the tallest item (~12'-6\")");
            Assert.That(b.min.y, Is.InRange(s.transform.position.y + 0.05f, s.transform.position.y + 0.15f), "everything stands on the slab");
            Assert.NotNull(s.transform.Find("Contents/YardEquipment"), "fuel tank built in the yard");
            Assert.Greater(s.transform.Find("Contents/EquipColliders").GetComponents<BoxCollider>().Length, 6, "equipment is solid");

            ShotAt(s, "sawstation", new Vector3(0.55f, 1.62f, -1.15f), new Vector3(0.15f, 0.98f, 0.02f), "yardmodels_1_mitersaw", 50f);
            ShotAt(s, "sawstation", new Vector3(-2.3f, 2.3f, -4.4f), new Vector3(0.5f, 0.75f, -0.9f), "yardmodels_2_sawstation", 55f);
            ShotAt(s, "forklift", new Vector3(-2.7f, 1.75f, 3.3f), new Vector3(0f, 0.95f, -0.15f), "yardmodels_3_forklift", 55f);
            ShotAt(s, "forklift", new Vector3(2.4f, 2.4f, -3.2f), new Vector3(0f, 1.0f, -0.3f), "yardmodels_4_forklift_rear", 55f);
            ShotAt(s, "lift", new Vector3(-2.7f, 2.3f, 3.1f), new Vector3(0.5f, 1.3f, -0.3f), "yardmodels_5_lift", 60f);
            ShotAt(s, "compressor", new Vector3(-0.7f, 1.75f, -1.7f), new Vector3(0f, 1.1f, 0f), "yardmodels_6_compressor", 55f);
            ShotAt(s, "fueltank", new Vector3(-1.9f, 1.9f, -3.6f), new Vector3(0.1f, 1.0f, 0f), "yardmodels_7_fueltank", 55f);
            // wide view of the bay side of the warehouse: forklift, lift, saw station, compressor
            Assert.IsTrue(s.TryEquipmentPose("lift", out var lp, out var lr));
            var T = s.transform;
            Shot(lp + T.rotation * new Vector3(-4.5f, 4.2f, -3.6f), lp + T.rotation * new Vector3(9.0f, 0.6f, 1.5f), "yardmodels_8_wide", 70f);
            // fit-out: safety station, tool chest + job box
            var wb = T.Find("Shell/HQFitOut"); Assert.NotNull(wb);
            Assert.IsTrue(s.TryEquipmentPose("compressor", out var cp, out _));
            Shot(cp + T.rotation * new Vector3(-1.5f, 1.7f, -1.9f), cp + T.rotation * new Vector3(-3.0f, 0.75f, 1.1f), "yardmodels_9_toolchest", 60f);
            // safety station on the office demising wall (HQ local x = ox1 = -21.13, z = wz0 + 4.4)
            Shot(T.TransformPoint(new Vector3(-19.4f, 1.55f, 44.9f)), T.TransformPoint(new Vector3(-21.13f, 1.35f, 44.0f)), "yardmodels_10_safety", 55f);
        }
    }
}
