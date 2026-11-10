using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Groundbreak.Tests.FinishTestKit;

namespace Groundbreak.Tests
{
    /// <summary>Player feel + HUD polish: held-tool view-model per tool family, head bob/footsteps from the gait,
    /// new synthesized sounds, and screenshots of the first-person view with the HUD and the tablet.</summary>
    public class PolishFeelTests
    {
        [UnityTearDown] public IEnumerator TearDownScene() { if (Tablet.I != null && Tablet.I.IsOpen) Tablet.I.Close(); yield return CleanScene(); }

        static string ShotDir { get { var d = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(d); return d; } }

        /// <summary>Player camera + view-model camera into one 1280x720 image.</summary>
        static void PlayerShot(string name)
        {
            var pl = Game.Player; var vm = pl.GetComponent<ViewModel>();
            var rt = new RenderTexture(1280, 720, 24);
            var cams = new[] { pl.Cam, vm != null ? vm.Camera : null };
            foreach (var c in cams) { if (c == null) continue; var prev = c.targetTexture; c.targetTexture = rt; c.Render(); c.targetTexture = prev; }
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        /// <summary>Renders the 3D view + view-model into a screen-sized RT, then lets the IMGUI pass (HUD/tablet) draw
        /// into it: a capture behaviour with the highest GUI.depth redirects the target before the others draw, one with
        /// the lowest reads it back after. Times out (returns false) if OnGUI doesn't run in this mode.</summary>
        static IEnumerator ScreenShot(string name, System.Action<bool> done)
        {
            var go = new GameObject("PolishGuiCapture");
            var first = go.AddComponent<GuiCaptureBegin>(); var last = go.AddComponent<GuiCaptureEnd>();
            int w = Screen.width, h = Screen.height;
            var rt = new RenderTexture(w, h, 24);
            first.Rt = rt; last.Rt = rt; last.Name = name;
            first.Before = () =>
            {
                var pl = Game.Player; var vm = pl.GetComponent<ViewModel>();
                foreach (var c in new[] { pl.Cam, vm != null ? vm.Camera : null }) { if (c == null) continue; var prev = c.targetTexture; c.targetTexture = rt; c.Render(); c.targetTexture = prev; }
            };
            for (int i = 0; i < 60 && !last.Done; i++) yield return null;
            bool ok = last.Done;
            Object.Destroy(go); rt.Release(); Object.Destroy(rt);
            done(ok);
        }

        class GuiCaptureBegin : MonoBehaviour
        {
            public RenderTexture Rt; public System.Action Before; bool _done;
            void OnGUI()
            {
                GUI.depth = 10000;
                if (_done || Event.current.type != EventType.Repaint) return;
                _done = true; Before?.Invoke(); RenderTexture.active = Rt;
            }
        }

        class GuiCaptureEnd : MonoBehaviour
        {
            public RenderTexture Rt; public string Name; public bool Done;
            void OnGUI()
            {
                GUI.depth = -10000;
                if (Done || Event.current.type != EventType.Repaint || RenderTexture.active != Rt) return;
                var tex = new Texture2D(Rt.width, Rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Rt.width, Rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(ShotDir, Name + ".png"), tex.EncodeToPNG());
                Object.Destroy(tex); RenderTexture.active = null; Done = true;
            }
        }

        static Tool Give(string id)
        {
            var belt = Game.Belt;
            foreach (var t in belt.Owned) if (t.Id == id) { belt.Assign(t, 1); belt.Select(1); return t; }
            var nt = ToolFactory.Create(id);
            Assert.NotNull(nt, "tool " + id);
            belt.AddTool(nt); belt.Assign(nt, 1); belt.Select(1);
            return nt;
        }

        static IEnumerator BuildCorner()
        {
            // a finished-looking inside corner to stand in front of: framing + stand-in painted drywall + base
            var O = new Vector3(60f, 0f, 60f);
            Subfloor(O, 144f, 144f);
            StudWall(O, Vector3.right, Vector3.forward, 144f);
            yield return Frames(3);
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator ViewModelShowsHeldToolFamilies()
        {
            yield return Frames(5);
            var pl = Game.Player; Assert.NotNull(pl);
            var vm = pl.GetComponent<ViewModel>(); Assert.NotNull(vm, "ViewModel on player");
            Assert.NotNull(pl.GetComponent<PlayerFeel>(), "PlayerFeel on player");
            Assert.NotNull(vm.Camera, "view-model camera");
            Assert.AreEqual(0, pl.Cam.cullingMask & (1 << ViewModel.Layer), "player camera must not draw the view-model layer");

            yield return BuildCorner();
            AimPlayerAt(new Vector3(60f + 1.8f, 0.02f, 61.7f), new Vector3(60f + 1.8f, 1.1f, 60f));

            var cases = new (string id, string kind, float use)[]
            {
                ("hammer", "Hammer", 0.4f), ("tape", "Tape", 1f), ("saw", "CircSaw", 1f), ("level", "Level", 1f),
                ("finish-nailer", "Nailer", 0.1f), ("fin-drill", "Drill", 1f), ("paint-roller", "Roller", 0.25f),
                ("caulk-gun", "CaulkGun", 1f), ("taping-knives", "Trowel", 1f),
            };
            foreach (var c in cases)
            {
                if (ToolFactory.Create(c.id) == null) { Debug.Log("[Polish] tool not registered: " + c.id); continue; }
                Give(c.id);
                yield return Frames(2);
                Assert.AreEqual(c.kind, ViewModel.ShownKind, "view-model for " + c.id);
                vm.SkipEquip(); vm.SetUse(c.use); vm.Step(0f);
                PlayerShot("ui_vm_" + c.id);
            }
            // a tool with no model shows nothing (and doesn't throw)
            Game.Belt.Select(0);
            yield return Frames(2);
            Debug.Log("[Polish] slot 0 view-model: " + ViewModel.ShownKind);
        }

        [UnityTest]
        public IEnumerator GaitBobsHeadAndStepsOnHeelStrike()
        {
            yield return Frames(5);
            var pl = Game.Player; var feel = pl.GetComponent<PlayerFeel>(); var cc = pl.GetComponent<CharacterController>();
            yield return BuildCorner();
            AimPlayerAt(new Vector3(60.3f, 0.05f, 62.2f), new Vector3(70f, 1.6f, 62.2f));
            yield return Frames(10);
            float phase0 = feel.Phase;
            float minY = float.MaxValue, maxY = float.MinValue;
            // walk 3 m along +X at walking speed by driving the controller (no input in tests)
            float walked = 0f;
            Time.captureDeltaTime = 1f / 60f; // batch mode runs thousands of fps; CharacterController ignores sub-mm moves
            for (int i = 0; i < 20000 && walked < 3f; i++)
            {
                float step = pl.WalkSpeed * Time.deltaTime;
                cc.Move(Vector3.right * step + Vector3.down * 0.01f);
                walked += step;
                yield return null;
                float y = feel.CamOffset.y;
                if (walked > 0.8f) { minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
                if (i % 30 == 0) Debug.Log($"[Polish] walk i={i} dt={Time.deltaTime:F4} grounded={cc.isGrounded} speed={feel.Speed:F2} pos={pl.transform.position} phase={feel.Phase:F2}");
            }
            Time.captureDeltaTime = 0f;
            Debug.Log($"[Polish] gait phase advanced {feel.Phase - phase0:F2} rad, bob range {(maxY - minY) * 1000f:F1} mm, blend {feel.BobBlend:F2}");
            Assert.Greater(feel.Phase - phase0, Mathf.PI, "at least one step taken");
            Assert.Greater(maxY - minY, 0.005f, "head bobs while walking");
            Assert.Less(maxY - minY, 0.08f, "bob stays subtle");
            // standing still settles the bob back out
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 40; i++) yield return null;
            Time.captureDeltaTime = 0f;
            Assert.Less(feel.BobBlend, 0.05f, "bob settles when standing");

            // teleport must not count as walking
            float ph = feel.Phase;
            AimPlayerAt(new Vector3(61f, 0.05f, 63f), new Vector3(70f, 1.6f, 63f));
            yield return Frames(3);
            Assert.AreEqual(ph, feel.Phase, 0.01f, "teleport ignored");
        }

        [Test]
        public void NewSoundsSynthesize()
        {
            foreach (var n in new[] { "land", "equip", "ui-open", "ui-close", "toast", "staple", "screw", "roll", "scrape", "brick", "caulk", "spray", "cut", "snip", "tape", "knock" })
            {
                var c = Sfx.Clip(n);
                Assert.NotNull(c, "clip " + n);
                var d = new float[c.samples]; c.GetData(d, 0);
                float peak = 0; foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
                Assert.Greater(peak, 0.02f, "audible " + n);
                Assert.LessOrEqual(peak, 1f, "no clipping " + n);
            }
            foreach (var id in new[] { "finish-nailer", "screw-gun", "paint-roller", "caulk-gun", "cap-stapler", "brick-trowel", "miter-saw" })
                Assert.NotNull(Sfx.ToolSound(id, out _), "tool sound for " + id);
        }

        [UnityTest]
        public IEnumerator HudAndTabletScreenshots()
        {
            yield return Frames(5);
            yield return BuildCorner();
            AimPlayerAt(new Vector3(60f + 1.8f, 0.02f, 61.5f), new Vector3(60f + 1.8f, 1.3f, 60f));
            Give("hammer");
            var vm = Game.Player.GetComponent<ViewModel>(); vm.SkipEquip();
            Game.Toast("Base trim: coped inside corner fits tight");
            yield return Frames(20);
            bool ok = false;
            yield return ScreenShot("ui_hud", r => ok = r);
            PlayerShot("ui_hud_3d");
            Tablet.I.Open();
            yield return Frames(30);
            yield return ScreenShot("ui_tablet", r => ok &= r);
            Tablet.I.Close();
            yield return Frames(3);
            Assert.IsFalse(Game.UIOpen, "tablet closed releases UI");
            Debug.Log("[Polish] screen capture " + (ok ? "ok" : "not available in this run (3D shot only)"));
        }
    }
}
