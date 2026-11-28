using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Stair Layout Kit: aim at a raised landing's rim, measure, ghost, start the guided stair job, build it; width limits;
    /// too-tall rise asks for a landing. Screenshots: Logs/shots/stairtool_*.png.</summary>
    public class StairToolTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Vector3 pos, Vector3 look, string name, float fov = 55f)
        {
            var cam = new GameObject("StairToolCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.enabled = false;
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

        static float GroundY(float x, float z)
        {
            Assert.IsTrue(Physics.Raycast(new Vector3(x, 40f, z), Vector3.down, out var h, 80f, Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore), "no ground at " + x + "," + z);
            return h.point.y;
        }

        /// <summary>Stands the player on the ground at feet and points the crosshair at target.</summary>
        static IEnumerator AimAt(Vector3 feet, Vector3 target)
        {
            var pl = Game.Player;
            pl.transform.position = feet;
            Physics.SyncTransforms();
            Vector3 eye = feet + Vector3.up * PlayerRig.EyeHeight;
            Vector3 d = target - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            pl.SetLook(yaw, pitch);
            yield return new WaitForFixedUpdate();
            yield return null;
            pl.transform.position = feet;   // undo any controller drift
            pl.SetLook(yaw, pitch);
            Physics.SyncTransforms();
        }

        StairLayoutKit Kit()
        {
            Assert.NotNull(Game.Belt, "no tool belt");
            Assert.IsTrue(ToolFactory.ShopItems.Any(i => i.Id == StairLayoutKit.ToolId), "kit not in the tool shop");
            var kit = FinishStock.Give(StairLayoutKit.ToolId) as StairLayoutKit;
            Assert.NotNull(kit, "kit not created by the ToolFactory");
            // equip it on the belt like the player would (so the Place tool's ghost isn't showing)
            int slot = Game.Belt.IndexOf(kit);
            if (slot < 0) { slot = ToolBelt.SlotCount - 1; Game.Belt.Assign(kit, slot); }
            Game.Belt.Select(slot);
            Assert.AreSame(kit, Game.Belt.Active, "kit equipped");
            return kit;
        }

        [TearDown]
        public void TearDown() { FramingJob.RequireStock = true; Game.UIOpen = false; }

        [UnityTest]
        public IEnumerator Kit_MeasuresLandingEdge_StartsAndBuildsStairs()
        {
            yield return Frames(5);
            FramingJob.RequireStock = false;
            Game.Snap.GridIndex = 3; Game.Snap.AngleIndex = 1;
            float riseIn = 52f;
            float gx = -16f, gz = 6f;
            float gy = GroundY(gx, gz);
            var center = new Vector3(gx, gy, gz);
            var landing = StairBuilder.Landing(center, 0f, Units.In(48f), Units.In(riseIn));
            landing.RunAll();
            Assert.IsTrue(landing.Done, "landing built");
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();

            var kit = Kit();
            Assert.AreEqual(36f, kit.WidthIn, 1e-3f, "default width 36\"");
            // narrower than 36" can't be selected
            kit.ChangeWidth(-1); Assert.AreEqual(36f, kit.WidthIn, 1e-3f, "- at 36\" stays 36\"");
            kit.SetWidthIn(30f); Assert.AreEqual(36f, kit.WidthIn, 1e-3f, "30\" clamps to 36\"");
            kit.ChangeWidth(1); Assert.AreEqual(42f, kit.WidthIn, 1e-3f, "6\" steps");
            kit.ChangeWidth(-1); Assert.AreEqual(36f, kit.WidthIn, 1e-3f);

            // stand in front of the landing (front beam faces -Z) and aim at the rim, 4" under the deck
            float faceZ = gz - Units.In(24f);
            Vector3 target = new Vector3(gx + 0.1f, gy + Units.In(riseIn - 4f), faceZ);
            yield return AimAt(new Vector3(gx + 0.4f, GroundY(gx + 0.4f, faceZ - 3.2f), faceZ - 3.2f), target);
            Assert.IsTrue(Game.Player.TryAim(out var aimHit, StairLayoutKit.Range), "can't aim at the landing");
            Debug.Log("[StairToolTests] aimed at " + aimHit.collider.name + " " + aimHit.point);

            kit.Tick();
            var p = kit.Plan;
            Debug.Log("[StairToolTests] plan: rise " + Units.ToInches(p.RiseM) + "\" msg='" + p.Message + "' hint: " + kit.Hint);
            Assert.IsTrue(p.Measured, "not measured: " + p.Message);
            Assert.IsTrue(p.CanStart, "can't start: " + p.Message);
            Assert.AreEqual(riseIn, Units.ToInches(p.TopY - gy), 0.05f, "deck top found");
            Assert.AreEqual(riseIn, Units.ToInches(p.RiseM), 2f, "measured rise (ground may slope a little)");
            Assert.AreEqual(0f, Mathf.DeltaAngle(p.Yaw, 0f), 0.01f, "stairs run up toward +Z");
            Assert.NotNull(p.Support, "hangs from the landing's front beam");
            var r = StairMath.Calculate(Units.ToInches(p.RiseM));
            Assert.AreEqual(7, r.Risers, "4' 4\" rise = 7 risers");
            Assert.AreEqual(r.Risers, p.R.Risers);
            StringAssert.Contains(r.Risers + " risers", kit.Hint);
            StringAssert.Contains("handrail required", kit.Hint);
            StringAssert.Contains("total run", kit.Hint);
            Assert.IsTrue(kit.GhostVisible, "footprint ghost shown");
            Vector3 mid = p.Bottom + new Vector3(0f, 0.6f, 0.8f);
            Shot(mid + new Vector3(-3.4f, 1.9f, -2.8f), mid + new Vector3(0f, -0.1f, 0.4f), "stairtool_ghost");

            // UI open: nothing starts
            int jobsBefore = FramingJob.All.Count;
            Game.UIOpen = true; kit.Primary(); Game.UIOpen = false;
            Assert.AreEqual(jobsBefore, FramingJob.All.Count, "no job while the tablet is open");

            kit.Primary();
            var job = kit.LastJob;
            Assert.NotNull(job, "stair job not started: " + Game.ToastText);
            Assert.AreEqual("", StairBuilder.LastError);
            var asm = job.GetComponent<StairAssembly>();
            Assert.NotNull(asm);
            Assert.AreEqual(r.Risers, asm.Result.Risers, "riser count");
            Assert.AreEqual(36f, asm.WidthIn, 1e-3f);
            Assert.IsTrue(job.Steps.Any(s => s.Label.StartsWith("Handrail")), "handrail steps (4+ risers)");
            Assert.IsTrue(job.Steps.Any(s => s.Label.Contains("hanger board")), "hung from the rim");
            Assert.IsTrue(StairCalculator.HasLayout && StairCalculator.Layout.Risers == r.Risers, "framing square gauges follow the job");
            Game.Belt.Select(1);   // put the kit away (hammer in hand) for the photo of the finished stairs

            job.RunAll();
            Assert.IsTrue(job.Done, "stairs not done");
            yield return Frames(3);
            var members = job.GetComponentsInChildren<StructuralMember>();
            Assert.AreEqual(3, members.Count(m => m.Role == MemberRole.Stringer));
            Assert.AreEqual(1, members.Count(m => m.Role == MemberRole.Handrail));
            Assert.AreEqual(2 * r.Treads, members.Count(m => m.Role == MemberRole.Tread));
            float topTread = members.Where(m => m.Role == MemberRole.Tread).Max(m => m.GetComponent<BoxCollider>().bounds.max.y);
            Assert.AreEqual(r.UnitRiseIn, Units.ToInches(gy + Units.In(riseIn) - topTread), 0.3f, "top riser matches the deck");
            Shot(mid + new Vector3(-3.4f, 1.9f, -2.8f), mid + new Vector3(0f, -0.1f, 0.4f), "stairtool_built");

            Object.Destroy(job.gameObject);
            Object.Destroy(landing.gameObject);
            StairCalculator.HasLayout = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Kit_TooTallRise_SaysAddALanding_ThenLaysOutLanding()
        {
            yield return Frames(3);
            FramingJob.RequireStock = false;
            Game.Snap.GridIndex = 3; Game.Snap.AngleIndex = 1;
            float gx = -16f, gz = 22f;
            float gy = GroundY(gx, gz);
            float topIn = 140f;   // 11' 8": 19 risers, a 16'+ stringer
            var plat = new GameObject("TallPlatform");
            var bc = plat.AddComponent<BoxCollider>();
            bc.size = new Vector3(2f, 0.3f, 2f);
            plat.transform.position = new Vector3(gx, gy + Units.In(topIn) - 0.15f, gz);
            Physics.SyncTransforms();

            var kit = Kit();
            kit.Mode = StairLayoutKit.KitMode.Stairs;
            float faceZ = gz - 1f;
            yield return AimAt(new Vector3(gx, GroundY(gx, faceZ - 3f), faceZ - 3f), new Vector3(gx, gy + Units.In(topIn) - 0.1f, faceZ));
            kit.Tick();
            var p = kit.Plan;
            Debug.Log("[StairToolTests] tall plan: rise " + Units.ToInches(p.RiseM) + "\" msg='" + p.Message + "'");
            Assert.IsTrue(p.Measured, p.Message);
            Assert.IsTrue(p.NeedsLanding, "a 140\" rise needs a landing");
            StringAssert.Contains("add a landing", p.Message);
            int before = FramingJob.All.Count;
            kit.Primary();
            Assert.IsNull(kit.LastJob, "too-long stair must be refused");
            StringAssert.Contains("landing", Game.ToastText);
            yield return null;
            Assert.AreEqual(before, FramingJob.All.Count, "refused job cleaned up");

            // landing mode splits the flight
            kit.Secondary();
            Assert.AreEqual(StairLayoutKit.KitMode.Landing, kit.Mode);
            kit.Tick();
            p = kit.Plan;
            Assert.IsTrue(p.CanStart, p.Message);
            Assert.AreEqual(p.R.Risers, p.UpperRisers + p.LowerRisers);
            Assert.IsTrue(p.UpperR.LengthOk && p.LowerR.LengthOk, "both flights fit on stock stringers");
            kit.Primary();
            Assert.NotNull(kit.LastJob, "landing job: " + Game.ToastText);
            var info = kit.LastJob.GetComponent<StairLanding>();
            Assert.NotNull(info);
            Assert.AreEqual(Units.ToInches(p.LandingHeightM), Units.ToInches(info.HeightM), 0.01f);
            Assert.Greater(kit.LastJob.Steps.Count, 10);
            Game.Belt.Select(1);
            Object.Destroy(kit.LastJob.gameObject);
            Object.Destroy(plat);
            yield return null;
        }
    }
}
