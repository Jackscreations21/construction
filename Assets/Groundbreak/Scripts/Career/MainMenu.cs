using UnityEngine;

namespace Groundbreak
{
    /// <summary>Main menu overlay (shown at start, F10 any time): New Career, Free Build, Load, Settings.</summary>
    public class MainMenu : MonoBehaviour
    {
        public static MainMenu I;
        public static bool Showing => I != null && I._show;
        bool _show; int _page; // 0 main, 1 load, 2 settings
        Vector2 _scroll;

        void Awake()
        {
            I = this;
            // batch mode (tests/CI) skips the menu so the sandbox boots straight into play
            _show = GameSettings.ShowMenuOnBoot && !Application.isBatchMode;
        }
        void OnDestroy() { if (I == this) I = null; }

        public void Show() { _show = true; _page = 0; }
        public void Hide() { _show = false; Game.UIOpen = false; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        void Update()
        {
            if (GInput.GetKeyDown(KeyCode.F10)) { if (_show) Hide(); else Show(); }
            if (_show) { Game.UIOpen = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        /// <summary>Starts a new game in the given mode: clears the build, resets all feature state and money.</summary>
        public static void NewGame(GameMode mode)
        {
            GameSettings.Mode = mode;
            SaveSystem.ClearPieces();
            SaveSystem.ResetExtensions();
            Game.Wallet = new Wallet(mode == GameMode.Career ? 25000 : 50000);
            if (GameClock.I != null) { GameClock.I.Day = 1; GameClock.I.Hours = 7f; }
            Game.Toast(mode == GameMode.Career ? "New career: open the tablet (T) → Job Board" : "Free Build: unlimited open site. Sandbox toggles in Settings (F10)");
        }

        void OnGUI()
        {
            if (!_show) return;
            UISkin.Apply();
            GUI.depth = -10;
            var prev = GUI.color; GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
            float w = Mathf.Min(560, Screen.width * 0.8f), h = Mathf.Min(620, Screen.height * 0.85f);
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            UISkin.Panel(r);
            var title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(GUI.skin.label.fontSize * 2.2f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = UISkin.Accent;
            GUILayout.BeginArea(new Rect(r.x + 24, r.y + 18, r.width - 48, r.height - 36));
            GUILayout.Label("GROUNDBREAK", title);
            GUILayout.Label("Build it right, from the dirt up.", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(14);
            float bh = GUI.skin.button.fontSize * 2.4f;
            if (_page == 0)
            {
                if (GUILayout.Button("New Career", GUILayout.Height(bh))) { NewGame(GameMode.Career); Hide(); Tablet.I?.Open(); }
                if (GUILayout.Button("Free Build", GUILayout.Height(bh))) { NewGame(GameMode.FreeBuild); Hide(); }
                if (GUILayout.Button("Continue (close menu)", GUILayout.Height(bh))) Hide();
                if (GUILayout.Button("Save (quick slot)", GUILayout.Height(bh))) { SaveSystem.Save(SaveSystem.QuickSlot); Game.Toast(SaveSystem.LastMessage); }
                if (GUILayout.Button("Load…", GUILayout.Height(bh))) _page = 1;
                if (GUILayout.Button("Settings…", GUILayout.Height(bh))) _page = 2;
                GUILayout.FlexibleSpace();
                GUILayout.Label("F5 quick save · F9 quick load · F10 menu · T tablet", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            }
            else if (_page == 1)
            {
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(h * 0.55f));
                var slots = SaveSystem.ListSlots();
                if (slots.Count == 0) GUILayout.Label("No saves yet.");
                foreach (var (slot, time, label) in slots)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{slot}  ·  {time:MMM d HH:mm}  ·  {label}");
                    if (GUILayout.Button("Load", GUILayout.Width(90))) { SaveSystem.Load(slot); Game.Toast(SaveSystem.LastMessage); Hide(); }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
                GUILayout.BeginHorizontal();
                for (int i = 1; i <= 3; i++)
                    if (GUILayout.Button($"Save to slot {i}")) { SaveSystem.Save("slot" + i); Game.Toast(SaveSystem.LastMessage); }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Back", GUILayout.Height(bh))) _page = 0;
            }
            else
            {
                GameSettings.Relaxed = GUILayout.Toggle(GameSettings.Relaxed, "  Relaxed mode (no cure/grow waits, no rain damage)");
                GameSettings.UnlimitedMoney = GUILayout.Toggle(GameSettings.UnlimitedMoney, "  Unlimited money (sandbox)");
                GameSettings.InspectionsEnabled = GUILayout.Toggle(GameSettings.InspectionsEnabled, "  Inspections required");
                GameSettings.WeatherEnabled = GUILayout.Toggle(GameSettings.WeatherEnabled, "  Weather (rain + temperature)");
                SaveHotkeys.AutosaveEnabled = GUILayout.Toggle(SaveHotkeys.AutosaveEnabled, "  Autosave every 5 game minutes");
                GUILayout.Label($"Volume {Mathf.RoundToInt(GameSettings.MasterVolume * 100)}%");
                GameSettings.MasterVolume = GUILayout.HorizontalSlider(GameSettings.MasterVolume, 0f, 1f);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Back", GUILayout.Height(bh))) _page = 0;
            }
            GUILayout.EndArea();
        }
    }

    /// <summary>Top-left site panel: current phase, weather, active contract and deadline. Also runs sandbox top-ups.</summary>
    public class SiteHud : MonoBehaviour
    {
        GUIStyle _s;
        void Update()
        {
            if (GameSettings.UnlimitedMoney && Game.Wallet.Money < 250000) Game.Wallet.Add(1000000 - Game.Wallet.Money, "Sandbox funds");
        }

        void OnGUI()
        {
            if (Game.UIOpen) return;
            int fs = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 52f));
            if (_s == null || _s.fontSize != fs) { _s = new GUIStyle(GUI.skin.label) { fontSize = fs, wordWrap = false }; _s.normal.textColor = Color.white; }
            float line = fs * 1.45f, icon = fs * 1.25f;
            var rows = new System.Collections.Generic.List<(string icon, string text)>
            {
                ("house", "Phase: " + Inspections.CurrentPhaseLabel + (InspectorNpc.Busy ? "  (inspector on site)" : "")),
                (Weather.IsRaining ? "rain" : "sun", Weather.Label),
            };
            if (GameSettings.IsCareer)
            {
                var c = Career.Active;
                rows.Add(("star", c != null ? $"{c.Title}  ·  {Career.DaysLeft} days left" : $"No job - Job Board (T)  ·  Rep {Career.Reputation}"));
            }
            else rows.Add(("dot", "Free Build" + (GameSettings.Relaxed ? " · relaxed" : "")));
            float w = fs * 24;
            var r = new Rect(12, 12, w, rows.Count * line + 12);
            UISkin.Panel(r);
            for (int i = 0; i < rows.Count; i++)
            {
                GUI.DrawTexture(new Rect(r.x + 8, r.y + 6 + i * line + (line - icon) / 2, icon, icon), UISkin.Icon(rows[i].icon));
                GUI.Label(new Rect(r.x + 14 + icon, r.y + 6 + i * line, w - icon - 20, line), rows[i].text, _s);
            }
        }
    }

    /// <summary>Adds this feature's runtime systems to every game (any empty scene + Play).</summary>
    public static class SiteBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<LandscapeSystem>() != null) return;
            var go = new GameObject("SiteCareerSystems");
            go.AddComponent<Sfx>();
            go.AddComponent<Weather>();
            go.AddComponent<LandscapeSystem>();
            go.AddComponent<SaveHotkeys>();
            go.AddComponent<SiteHud>();
            go.AddComponent<MainMenu>();
            Tablet.AddHelp("F5 quick save · F9 quick load · F10 main menu (new career / free build / load / settings)");
        }
    }
}
