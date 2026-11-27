using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Tablet app "Portfolio": every job with its before/after photos (side by side or a wipe slider), progress photos,
    /// date, address, pay and customer rating; the player's own photo-mode album; favourites and delete.</summary>
    public static class PortfolioApp
    {
        public const string Name = "Portfolio";
        static int _tab;              // 0 jobs, 1 my photos, 2 favourites
        static bool _sliderView;
        static readonly Dictionary<string, float> _wipe = new Dictionary<string, float>();
        static string _confirm, _viewing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Tablet.RegisterApp(Name, Draw);
            Tablet.SetAppGroup(Name, "Business");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _tab = 0; _sliderView = false; _wipe.Clear(); _confirm = _viewing = null; }

        /// <summary>Which page the app shows (0 jobs, 1 my photos, 2 favourites) and whether jobs use the wipe slider.</summary>
        public static void Show(int tab, bool slider) { _tab = tab; _sliderView = slider; _viewing = null; }

        public static string Stars(float rating)
        {
            if (rating < 0f) return "not rated";
            return $"{rating:0.#} / 5 stars";
        }

        static void Draw()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle;
            float fs = label.fontSize, cw = t.ContentWidth;

            int docs = PhotoStore.DocumentedJobs, favs = 0;
            foreach (var p in PhotoStore.Photos) if (p.Favourite) favs++;
            int mine = PhotoStore.Photos.FindAll(p => p.Kind == "player").Count;
            GUILayout.Label("Portfolio", header);
            int next = PhotoStore.NextBonusAt;
            GUILayout.Label($"{docs} documented job(s) with before + after photos · {PhotoStore.Photos.Count} photo(s). " +
                (next > 0 ? $"Customers like a portfolio: reputation +1 at {next} documented jobs ({PhotoStore.BonusesGiven} of {PhotoStore.BonusSteps.Length} bonuses earned)."
                          : "Portfolio bonus complete (+3 reputation)."), label);
            GUILayout.Label("Before photos are taken automatically when you first reach a job; the after photo from the same spot when it's done. " +
                            "Press P anywhere for photo mode.", label);
            GUILayout.Space(4);
            _tab = GUILayout.Toolbar(_tab, new[] { $"Jobs ({PhotoStore.Albums.Count})", $"My photos ({mine})", $"Favourites ({favs})" }, button, GUILayout.Height(fs * 2f));
            GUILayout.Space(6);

            if (_viewing != null)
            {
                var e = PhotoStore.Get(_viewing);
                if (e == null) _viewing = null;
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(e.Caption + $"  (Day {e.Day})", header);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Close", button, GUILayout.Width(fs * 6))) _viewing = null;
                    GUILayout.EndHorizontal();
                    Picture(PhotoThumbs.Get(e), cw, null);
                    GUILayout.Space(10);
                }
            }

            if (_tab == 0) DrawJobs(cw, label, header, button, fs);
            else DrawGrid(_tab == 1 ? PhotoStore.Photos.FindAll(p => p.Kind == "player") : PhotoStore.Photos.FindAll(p => p.Favourite), cw, label, button, fs);

            GUILayout.Space(8);
            GUILayout.Label($"<color=#8f98a3>Saved as PNG files in {PhotoStore.Root}</color>", new GUIStyle(label) { richText = true, wordWrap = true });
        }

        static void DrawJobs(float cw, GUIStyle label, GUIStyle header, GUIStyle button, float fs)
        {
            if (PhotoStore.Albums.Count == 0) { GUILayout.Label("No jobs yet. Accept a job on the job board and drive there.", label); return; }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Compare:", label, GUILayout.Width(fs * 6));
            _sliderView = GUILayout.Toolbar(_sliderView ? 1 : 0, new[] { "Side by side", "Slider" }, button, GUILayout.Width(fs * 16)) == 1;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            for (int i = PhotoStore.Albums.Count - 1; i >= 0; i--)
            {
                var a = PhotoStore.Albums[i];
                UISkin.Panel(GUILayoutUtility.GetRect(cw, 2f), new Color(1, 1, 1, 0.12f));
                GUILayout.Label(a.Title ?? a.JobId, header);
                string days = a.Done ? (a.DoneDay > a.AcceptedDay ? $"Day {a.AcceptedDay}–{a.DoneDay}" : $"Day {a.DoneDay}") : $"Started day {a.AcceptedDay} · in progress";
                GUILayout.Label($"{a.Address} · {a.Client} · {days}" + (a.Done ? $" · Paid ${a.Pay:N0} · Customer rating {Stars(a.Rating)}" : ""), label);

                var before = PhotoStore.Get(a.BeforeId); var after = PhotoStore.Get(a.AfterId);
                if (before == null && after == null)
                    GUILayout.Label(a.Done ? "No photos were taken for this job." : "Go to the job: the before photo is taken when you get there.", label);
                else if (_sliderView && before != null && after != null)
                {
                    float w = Mathf.Min(cw, 820f);
                    if (!_wipe.TryGetValue(a.JobId, out float v)) v = 0.5f;
                    var r = GUILayoutUtility.GetRect(w, w * 9f / 16f, GUILayout.Width(w), GUILayout.Height(w * 9f / 16f));
                    Wipe(r, PhotoThumbs.Get(before), PhotoThumbs.Get(after), v);
                    _wipe[a.JobId] = GUILayout.HorizontalSlider(v, 0f, 1f, GUILayout.Width(w));
                    GUILayout.Space(fs * 0.6f);
                }
                else
                {
                    float w = (cw - 12f) / 2f;
                    GUILayout.BeginHorizontal();
                    Picture(before != null ? PhotoThumbs.Get(before) : null, w, "BEFORE");
                    GUILayout.Space(8);
                    Picture(after != null ? PhotoThumbs.Get(after) : null, w, a.Done ? "AFTER" : "AFTER (when finished)");
                    GUILayout.EndHorizontal();
                }

                if (a.ProgressIds.Count > 0)
                {
                    GUILayout.Label("Progress", label);
                    float tw = (cw - 40f) / 5f;
                    GUILayout.BeginHorizontal();
                    int n = 0;
                    foreach (var id in a.ProgressIds)
                    {
                        var e = PhotoStore.Get(id); if (e == null) continue;
                        if (n++ == 5) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); n = 1; }
                        GUILayout.BeginVertical(GUILayout.Width(tw));
                        if (Picture(PhotoThumbs.Get(e), tw, null)) _viewing = e.Id;
                        GUILayout.Label(e.Caption, label);
                        GUILayout.EndVertical();
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                var fav = after ?? before;
                if (fav != null && GUILayout.Button(fav.Favourite ? "Unfavourite" : "Favourite", button, GUILayout.Width(fs * 9))) PhotoStore.ToggleFavourite(fav);
                if (_confirm == a.JobId)
                {
                    if (GUILayout.Button("Yes, delete these photos", button, GUILayout.Width(fs * 14))) { PhotoStore.DeleteAlbum(a); _confirm = null; Tablet.I.ShowMessage("Job photos deleted."); }
                    if (GUILayout.Button("Keep", button, GUILayout.Width(fs * 6))) _confirm = null;
                }
                else if (GUILayout.Button("Delete", button, GUILayout.Width(fs * 7))) _confirm = a.JobId;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
        }

        static void DrawGrid(List<PhotoEntry> list, float cw, GUIStyle label, GUIStyle button, float fs)
        {
            if (list.Count == 0)
            {
                GUILayout.Label(_tab == 1 ? "No photos yet. Press P for photo mode, then Enter (or the button) to take a picture." : "No favourites yet.", label);
                return;
            }
            const int cols = 3;
            float w = (cw - 16f * (cols - 1)) / cols;
            for (int i = list.Count - 1, c = 0; i >= 0; i--, c++)
            {
                if (c % cols == 0) GUILayout.BeginHorizontal();
                var e = list[i];
                GUILayout.BeginVertical(GUILayout.Width(w));
                if (Picture(PhotoThumbs.Get(e), w, e.Kind == "player" ? null : e.Kind.ToUpperInvariant())) _viewing = e.Id;
                GUILayout.Label($"{e.Caption}  (Day {e.Day})", label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(e.Favourite ? "Unfavourite" : "Favourite", button)) PhotoStore.ToggleFavourite(e);
                if (_confirm == e.Id) { if (GUILayout.Button("Really delete", button)) { PhotoStore.Delete(e); _confirm = null; } }
                else if (GUILayout.Button("Delete", button)) _confirm = e.Id;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                if (c % cols == cols - 1 || i == 0) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.Space(10); }
                else GUILayout.Space(16);
            }
        }

        /// <summary>A 16:9 picture box. Returns true when clicked.</summary>
        static bool Picture(Texture2D tex, float w, string tag)
        {
            float h = w * 9f / 16f;
            var r = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            if (Event.current.type == EventType.Repaint)
            {
                if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleAndCrop);
                else UISkin.Panel(r, new Color(0f, 0f, 0f, 0.35f));
            }
            if (tex == null) GUI.Label(new Rect(r.x + 10, r.y + r.height / 2 - 10, r.width - 20, 24), "no photo yet");
            if (!string.IsNullOrEmpty(tag)) Tag(r, tag);
            return tex != null && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        static void Wipe(Rect r, Texture2D before, Texture2D after, float v)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (after != null) GUI.DrawTexture(r, after, ScaleMode.StretchToFill);
            if (before != null)
            {
                GUI.BeginGroup(new Rect(r.x, r.y, r.width * v, r.height));
                GUI.DrawTexture(new Rect(0, 0, r.width, r.height), before, ScaleMode.StretchToFill);
                GUI.EndGroup();
            }
            var c = GUI.color; GUI.color = Color.white;
            GUI.DrawTexture(new Rect(r.x + r.width * v - 1.5f, r.y, 3f, r.height), Texture2D.whiteTexture);
            GUI.color = c;
            Tag(r, "BEFORE");
            Tag(new Rect(r.xMax - 92f, r.y, 92f, r.height), "AFTER");
        }

        static GUIStyle _tag;
        static void Tag(Rect r, string text)
        {
            if (_tag == null) { _tag = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 }; _tag.normal.textColor = Color.white; }
            var size = _tag.CalcSize(new GUIContent(text));
            var box = new Rect(r.x + 8, r.y + 8, size.x + 12, size.y + 4);
            if (Event.current.type == EventType.Repaint) UISkin.Panel(box, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(box.x + 6, box.y + 2, size.x, size.y), text, _tag);
        }
    }
}
