using UnityEngine;

namespace Groundbreak
{
    /// <summary>Photo mode (P): freezes the game (timeScale 0), hides the HUD (Game.UIOpen) and gives a free-fly camera with
    /// lens (field of view / focal length), roll, real depth of field, time of day, exposure, filters and frames. The
    /// shutter saves a 1920x1080 PNG under Application.persistentDataPath/Photos (see PhotoStore).</summary>
    public class PhotoMode : MonoBehaviour
    {
        public static PhotoMode I;
        public static bool Active => I != null && I._active;
        public const KeyCode Key = KeyCode.P;
        public const int CaptureWidth = 1920, CaptureHeight = 1080;
        public const float MaxRange = 120f;      // how far the camera may fly from where photo mode started
        public const float MinHours = 5f, MaxHours = 21f;

        public readonly PhotoSettings S = new PhotoSettings();
        public Vector3 Position;
        public float Yaw, Pitch;
        public float Hours = 12f;
        public bool HidePanel;
        public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);
        public PhotoEntry LastPhoto { get; private set; }

        bool _active;
        int _releaseFrame;
        float _prevTimeScale = 1f, _prevHours = -1f;
        int _prevMask = -1;
        Vector3 _start;
        RenderTexture _sample, _accum, _final;
        int _n; int _key;
        float _flashUntil, _msgUntil; string _msg = "";
        Vector2 _panelScroll;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; ReleaseRTs(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var go = new GameObject("PhotoSystems");
            DontDestroyOnLoad(go);
            go.AddComponent<PhotoMode>();
            go.AddComponent<JobPhotos>();
            go.AddComponent<OfficeFrames>();
            Tablet.AddHelp("P - photo mode: WASD fly, Q/E down/up, hold right mouse to look, scroll zoom, [ ] roll, F focus, Enter shutter, P/Esc exit");
        }

        // ------------------------------------------------------------------ enter / exit

        public bool CanEnter => !_active && !Game.UIOpen && !MainMenu.Showing && Game.Player != null && Game.Player.Cam != null;

        public bool Enter()
        {
            if (!CanEnter) return false;
            var cam = Game.Player.Cam;
            Position = _start = cam.transform.position;
            var e = cam.transform.eulerAngles;
            Yaw = e.y; Pitch = Mathf.DeltaAngle(0f, e.x);
            S.Fov = cam.fieldOfView; S.Roll = 0f;
            _prevTimeScale = Time.timeScale; Time.timeScale = 0f;
            _prevHours = GameClock.I != null ? GameClock.I.Hours : -1f;
            Hours = _prevHours >= 0f ? _prevHours : 12f;
            _prevMask = cam.cullingMask; cam.cullingMask = 0;        // the photo camera draws the view; keep the player camera cheap
            _active = true; _releaseFrame = 0; _n = 0;
            Game.UIOpen = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            AutoFocus();
            return true;
        }

        public void Exit()
        {
            if (!_active) return;
            _active = false;
            Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale;
            if (GameClock.I != null && _prevHours >= 0f) GameClock.I.Hours = _prevHours;
            if (Game.Player != null && Game.Player.Cam != null && _prevMask != -1) Game.Player.Cam.cullingMask = _prevMask;
            _prevMask = -1;
            _releaseFrame = Time.frameCount;                         // like the tablet: release UIOpen next frame
            ReleaseRTs();
        }

        // ------------------------------------------------------------------ shutter

        /// <summary>Takes the photo now (all depth-of-field samples, filter, frame) and saves it.</summary>
        public PhotoEntry TakePhoto()
        {
            var tex = PhotoRender.Capture(Position, Rotation, S, CaptureWidth, CaptureHeight);
            string where = "";
            try { var lot = Town.Exists ? Town.LotAt(Position) : null; if (lot != null) where = lot.Address; } catch { }
            string when = GameClock.I != null ? GameClock.I.TimeLabel : "";
            LastPhoto = PhotoStore.Add(tex, "player", null, string.IsNullOrEmpty(where) ? when : where + " · " + when);
            Destroy(tex);
            _flashUntil = Time.unscaledTime + 0.18f;
            _msg = "Saved " + LastPhoto.File; _msgUntil = Time.unscaledTime + 3f;
            Sfx.Play("click", Position, 0.8f);
            return LastPhoto;
        }

        /// <summary>Focus on whatever is in the middle of the frame.</summary>
        public void AutoFocus()
        {
            var r = Rotation * Quaternion.Euler(0, 0, -S.Roll);
            if (Physics.Raycast(Position, r * Vector3.forward, out var hit, 300f, ~(1 << PhotoRender.ViewModelLayer), QueryTriggerInteraction.Ignore))
                S.Focus = Mathf.Clamp(hit.distance, 0.3f, 100f);
        }

        // ------------------------------------------------------------------ frame loop

        void Update()
        {
            if (!_active)
            {
                if (_releaseFrame > 0 && Time.frameCount > _releaseFrame)
                {
                    _releaseFrame = 0;
                    if (Tablet.I == null || !Tablet.I.IsOpen) { Game.UIOpen = false; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
                }
                if (GInput.GetKeyDown(Key) && CanEnter) Enter();
                return;
            }

            Game.UIOpen = true;
            if (Tablet.I != null && Tablet.I.IsOpen) Tablet.I.Close();
            if (GInput.GetKeyDown(Key) || GInput.GetKeyDown(KeyCode.Escape)) { Exit(); return; }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool look = GInput.Mouse(1);
            Cursor.lockState = look ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !look;
            if (look)
            {
                var md = GInput.MouseDelta * 0.08f * (S.Fov / 60f);
                Yaw += md.x; Pitch = Mathf.Clamp(Pitch - md.y, -89f, 89f);
            }
            var move = Vector3.zero;
            if (GInput.GetKey(KeyCode.W)) move.z += 1; if (GInput.GetKey(KeyCode.S)) move.z -= 1;
            if (GInput.GetKey(KeyCode.D)) move.x += 1; if (GInput.GetKey(KeyCode.A)) move.x -= 1;
            if (GInput.GetKey(KeyCode.E)) move.y += 1; if (GInput.GetKey(KeyCode.Q)) move.y -= 1;
            if (move != Vector3.zero)
            {
                float speed = GInput.Shift ? 12f : GInput.Ctrl ? 0.6f : 3f;
                Position += (Rotation * new Vector3(move.x, 0f, move.z) + Vector3.up * move.y).normalized * speed * dt;
                var off = Position - _start;
                if (off.magnitude > MaxRange) Position = _start + off.normalized * MaxRange;
                Position.y = Mathf.Max(Position.y, _start.y - 30f);
            }
            float sc = GInput.Scroll;
            if (sc != 0f && !look) S.Fov = Mathf.Clamp(S.Fov - sc * 3f, 8f, 100f);
            if (GInput.GetKey(KeyCode.LeftBracket)) S.Roll = Mathf.Clamp(S.Roll - 30f * dt, -45f, 45f);
            if (GInput.GetKey(KeyCode.RightBracket)) S.Roll = Mathf.Clamp(S.Roll + 30f * dt, -45f, 45f);
            if (GInput.GetKeyDown(KeyCode.F)) AutoFocus();
            if (GInput.GetKeyDown(KeyCode.H)) HidePanel = !HidePanel;
            if (GInput.GetKeyDown(KeyCode.Return)) TakePhoto();
            if (GameClock.I != null && !Mathf.Approximately(GameClock.I.Hours, Hours)) GameClock.I.Hours = Hours;
        }

        void LateUpdate()
        {
            if (!_active) return;
            RenderPreview();
        }

        /// <summary>Live viewfinder: one lens sample per frame, averaged while the camera holds still (depth of field
        /// builds up over ~half a second), then the filter and frame.</summary>
        public void RenderPreview()
        {
            int w = Mathf.Clamp(Screen.width, 320, 1600), h = Mathf.Max(180, Mathf.RoundToInt(w * Screen.height / (float)Mathf.Max(1, Screen.width)));
            if (_final == null || _final.width != w || _final.height != h)
            {
                ReleaseRTs();
                _sample = PhotoRender.NewRT(w, h, false, 2);
                _accum = PhotoRender.NewRT(w, h, true, 1);
                _final = PhotoRender.NewRT(w, h, false, 1);
                _n = 0;
            }
            int key = Hash();
            if (key != _key) { _key = key; _n = 0; }
            int target = S.Dof ? PhotoRender.DofSamplesCapture : 1;
            for (int k = 0; k < 2 && _n < target; k++, _n++)
            {
                PhotoRender.RenderSample(Position, Rotation, S, _sample, PhotoRender.LensSample(S.Dof ? _n : 0, target));
                PhotoRender.Accumulate(_sample, _accum, _n);
            }
            PhotoRender.Finish(_accum, _final, S);
        }

        public RenderTexture Viewfinder => _final;

        int Hash()
        {
            unchecked
            {
                int h = Position.GetHashCode();
                h = h * 31 + Yaw.GetHashCode(); h = h * 31 + Pitch.GetHashCode();
                h = h * 31 + S.Fov.GetHashCode(); h = h * 31 + S.Roll.GetHashCode(); h = h * 31 + S.Dof.GetHashCode();
                h = h * 31 + S.FStop.GetHashCode(); h = h * 31 + S.Focus.GetHashCode(); h = h * 31 + S.Flash.GetHashCode();
                h = h * 31 + Hours.GetHashCode();
                return h;
            }
        }

        void ReleaseRTs()
        {
            foreach (var rt in new[] { _sample, _accum, _final }) if (rt != null) { rt.Release(); Destroy(rt); }
            _sample = _accum = _final = null;
        }

        // ------------------------------------------------------------------ UI

        GUIStyle _small, _title;
        float _cw = 300f;

        void OnGUI()
        {
            if (!_active) return;
            GUI.depth = -2000;                                     // in front of every other overlay
            UISkin.Apply();
            if (_final != null) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _final, ScaleMode.ScaleAndCrop, false);
            if (Time.unscaledTime < _flashUntil && Event.current.type == EventType.Repaint)
            {
                var c = GUI.color; GUI.color = new Color(1, 1, 1, (_flashUntil - Time.unscaledTime) / 0.18f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = c;
            }
            float fs = Mathf.Max(12f, Screen.height / 60f);
            if (_small == null || _small.fontSize != Mathf.RoundToInt(fs))
            {
                _small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs), wordWrap = true, richText = true };
                _title = new GUIStyle(_small) { fontSize = Mathf.RoundToInt(fs * 1.35f), fontStyle = FontStyle.Bold };
                _title.normal.textColor = UISkin.Accent;
            }
            // rule-of-thirds guides
            if (!HidePanel && Event.current.type == EventType.Repaint)
            {
                var c = GUI.color; GUI.color = new Color(1, 1, 1, 0.18f);
                for (int i = 1; i <= 2; i++)
                {
                    GUI.DrawTexture(new Rect(Screen.width * i / 3f, 0, 1, Screen.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0, Screen.height * i / 3f, Screen.width, 1), Texture2D.whiteTexture);
                }
                GUI.color = c;
            }
            if (Time.unscaledTime < _msgUntil) GUI.Label(new Rect(20, Screen.height - fs * 3f, Screen.width * 0.6f, fs * 2f), _msg, _small);
            if (HidePanel) { GUI.Label(new Rect(20, 16, 500, fs * 2f), "H - show controls", _small); return; }

            float pw = Mathf.Clamp(fs * 26f, 340f, 520f), ph = Screen.height - 40f;
            var panel = new Rect(Screen.width - pw - 20f, 20f, pw, ph);
            UISkin.Panel(panel, new Color(0.07f, 0.09f, 0.11f, 0.82f));
            GUILayout.BeginArea(new Rect(panel.x + 12, panel.y + 10, panel.width - 24, panel.height - 20));
            _panelScroll = GUILayout.BeginScrollView(_panelScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            _cw = panel.width - 24f - 22f;
            var W = GUILayout.Width(_cw);
            GUILayout.Label("PHOTO MODE", _title, W);
            GUILayout.Label("Game paused. " + (GameClock.I != null ? GameClock.I.TimeLabel : ""), _small, W);

            Slider($"Lens: {S.FocalMm:0} mm  (field of view {S.Fov:0}°)", ref S.Fov, 8f, 100f);
            Slider($"Roll: {S.Roll:0}°", ref S.Roll, -45f, 45f);
            if (GameClock.I != null) Slider("Time of day: " + HoursLabel(Hours), ref Hours, MinHours, MaxHours);
            Slider($"Exposure: {S.Exposure:0.00}x", ref S.Exposure, 0.4f, 2f);
            Slider($"Vignette: {S.Vignette * 100f:0}%", ref S.Vignette, 0f, 1f);
            S.Dof = GUILayout.Toggle(S.Dof, "Depth of field", W);
            if (S.Dof)
            {
                Slider($"Aperture: f/{S.FStop:0.0}", ref S.FStop, 1.4f, 16f);
                Slider($"Focus: {Units.Format(S.Focus)}", ref S.Focus, 0.3f, 60f);
                if (GUILayout.Button("Focus on center (F)", W)) AutoFocus();
            }
            S.Flash = GUILayout.Toggle(S.Flash, "Flash (for interiors)", W);
            GUILayout.Label("Filter", _small, W);
            S.Filter = GUILayout.SelectionGrid(S.Filter, PhotoRender.Filters, 2, W);
            GUILayout.Label("Frame", _small, W);
            S.Frame = GUILayout.SelectionGrid(S.Frame, PhotoRender.Frames, 2, W);
            GUILayout.Space(8);
            if (GUILayout.Button("Take photo (Enter)", W, GUILayout.Height(fs * 2.4f))) TakePhoto();
            if (GUILayout.Button("Exit photo mode (P)", W)) Exit();
            GUILayout.Space(6);
            GUILayout.Label("<color=#9aa3ad>WASD fly · Q/E down/up · Shift fast · Ctrl slow · hold right mouse to look · scroll zoom · [ ] roll · H hide panel</color>", _small, W);
            GUILayout.Label($"<color=#9aa3ad>Photos: {PhotoStore.Photos.Count}. See them in the tablet's Portfolio app.</color>", _small, W);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Slider(string label, ref float v, float min, float max)
        {
            GUILayout.Label(label, _small, GUILayout.Width(_cw));
            v = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(_cw));
            GUILayout.Space(4);
        }

        public static string HoursLabel(float hours)
        {
            int m = Mathf.FloorToInt(hours * 60f), h24 = (m / 60) % 24, h12 = h24 % 12 == 0 ? 12 : h24 % 12;
            return $"{h12}:{m % 60:00} {(h24 < 12 ? "AM" : "PM")}";
        }
    }
}
