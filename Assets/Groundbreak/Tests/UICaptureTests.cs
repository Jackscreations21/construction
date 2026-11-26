using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Real screenshots of the IMGUI HUD, Tab tool list, every tablet app and the main menu, saved to
    /// Logs/shots/ui_*.png. IMGUI (OnGUI) never runs in -batchmode, so this test is ignored there: run it with a windowed
    /// editor (see Docs/UI-CAPTURE.md). The Game view is set to 1600x900 and the screen is read after WaitForEndOfFrame.</summary>
    [Category("UICapture")]
    public class UICaptureTests
    {
        static string ShotDir { get { var d = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(d); return d; } }

        public static void SetGameView(uint w, uint h)
        {
            var pmw = System.Type.GetType("UnityEditor.PlayModeWindow, UnityEditor");
            var m = pmw?.GetMethod("SetCustomRenderingResolution", BindingFlags.Public | BindingFlags.Static);
            try { m?.Invoke(null, new object[] { w, h, "UICapture" }); } catch (System.Exception e) { Debug.Log("[UICapture] resolution: " + e.Message); }
        }

        /// <summary>Waits for this frame's OnGUI, then saves the screen. Returns the texture via callback (caller destroys) or null.</summary>
        static IEnumerator Shot(string name, System.Action<Texture2D> keep = null)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            Debug.Log($"[UICapture] {name} {tex.width}x{tex.height}");
            if (keep != null) keep(tex); else Object.Destroy(tex);
        }

        static void Crop(Texture2D src, int cx, int cy, int size, int scale, string name)
        {
            var dst = new Texture2D(size * scale, size * scale, TextureFormat.RGB24, false);
            for (int y = 0; y < size * scale; y++)
                for (int x = 0; x < size * scale; x++)
                    dst.SetPixel(x, y, src.GetPixel(cx - size / 2 + x / scale, cy - size / 2 + y / scale));
            dst.Apply();
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), dst.EncodeToPNG());
            Object.Destroy(dst);
        }

        public static string Slug(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (Application.isBatchMode) yield break;
            SetGameView(1600, 900);
            if (MainMenu.Showing) MainMenu.I.Hide();
            yield return new WaitForSecondsRealtime(0.3f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Tablet.I != null && Tablet.I.IsOpen) Tablet.I.Close();
            if (MainMenu.Showing) MainMenu.I.Hide();
            yield return null; yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureAllUI()
        {
            if (Application.isBatchMode) { Assert.Ignore("IMGUI does not render in -batchmode: run windowed, see Docs/UI-CAPTURE.md"); yield break; }
            yield return null;
            Assert.NotNull(Game.Player, "no player"); Assert.NotNull(Tablet.I, "no tablet");

            // look at the material piles near spawn so the HUD sits over a real scene
            var cc = Game.Player.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            Game.Player.transform.position = new Vector3(0f, 0f, -4f);
            if (cc != null) cc.enabled = true;
            Game.Player.SetLook(20f, 6f);
            if (Game.Belt != null) Game.Belt.Select(1);
            yield return new WaitForSecondsRealtime(0.4f);

            // HUD + belt + toast
            Game.Toast("Order placed. Truck in ~90 s.");
            yield return new WaitForSecondsRealtime(0.45f);
            Texture2D hud = null;
            yield return Shot("ui_hud_belt_toast", t => hud = t);
            Crop(hud, hud.width / 2, hud.height / 2, 80, 4, "ui_crosshair");
            Object.Destroy(hud);
            Game.ToastUntil = 0f;
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("ui_hud");

            // Tab tool list (held Tab): ToolBelt.Update resets _showList each frame, so set it after Update, before OnGUI
            var f = typeof(ToolBelt).GetField("_showList", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f, "ToolBelt._showList not found");
            yield return null;
            f.SetValue(Game.Belt, true);
            yield return Shot("ui_tool_list");
            yield return null;

            // every tablet app
            var names = (string[])typeof(Tablet).GetMethod("AppNames", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            var seen = new List<string>();
            Tablet.I.Open();
            yield return new WaitForSecondsRealtime(0.35f);
            yield return Shot("ui_tablet_home");
            foreach (var n in names)
            {
                Tablet.I.ShowApp(n);
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.AreEqual(n, Tablet.I.CurrentApp);
                yield return Shot("ui_tablet_" + Slug(n));
                seen.Add(n);
            }
            Tablet.I.Close();
            yield return new WaitForSecondsRealtime(0.2f);
            Debug.Log("[UICapture] tablet apps: " + string.Join(", ", seen));

            // main menu
            MainMenu.I.Show();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("ui_main_menu");
            MainMenu.I.Hide();
            yield return null;
        }
    }
}
