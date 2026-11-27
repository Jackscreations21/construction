using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Groundbreak
{
    [Serializable]
    public class PhotoEntry
    {
        public string Id, File;           // File = name inside PhotoStore.Root
        public string Kind;               // "player", "before", "after", "progress"
        public string JobId, Caption;
        public int Day; public float Hours;
        public bool Favourite;
    }

    /// <summary>Before/after/progress photos of one job plus what the customer paid and thought.</summary>
    [Serializable]
    public class JobAlbum
    {
        public string JobId, Title, Client, Address;
        public int Kind;                  // JobKind
        public int AcceptedDay, DoneDay = -1, Pay;
        public float Rating = -1f;        // 0..5 stars, -1 = not finished
        public bool Done, HasPose;
        public Vector3 CamPos; public Quaternion CamRot = Quaternion.identity; public float Fov = 60f; public bool Flash;
        public string BeforeId, AfterId;
        public List<string> ProgressIds = new List<string>();
        public bool Documented => Done && !string.IsNullOrEmpty(BeforeId) && !string.IsNullOrEmpty(AfterId);
    }

    /// <summary>The photo index (saved under SaveSystem key "photos") and the PNG files under
    /// Application.persistentDataPath/Photos. Also the modest portfolio reputation bonus.</summary>
    public static class PhotoStore
    {
        public static readonly List<PhotoEntry> Photos = new List<PhotoEntry>();
        public static readonly List<JobAlbum> Albums = new List<JobAlbum>();
        public static int BonusesGiven;
        static int _next = 1;
        public static event Action Changed;

        /// <summary>Documented jobs (before + after) needed for each +1 reputation. Kept modest: +3 at most.</summary>
        public static readonly int[] BonusSteps = { 5, 15, 30 };

        /// <summary>Tests point this at a throwaway folder (still under persistentDataPath).</summary>
        public static string RootOverride;
        public static string Root => RootOverride ?? Path.Combine(Application.persistentDataPath, "Photos");

        public static string PathOf(PhotoEntry e) => e == null ? null : Path.Combine(Root, e.File);
        public static PhotoEntry Get(string id) => string.IsNullOrEmpty(id) ? null : Photos.Find(p => p.Id == id);
        public static JobAlbum AlbumOf(string jobId) => Albums.Find(a => a.JobId == jobId);
        public static int DocumentedJobs { get { int n = 0; foreach (var a in Albums) if (a.Documented) n++; return n; } }
        public static int NextBonusAt => BonusesGiven < BonusSteps.Length ? BonusSteps[BonusesGiven] : -1;

        /// <summary>Writes the texture as a PNG and adds it to the index.</summary>
        public static PhotoEntry Add(Texture2D tex, string kind, string jobId = null, string caption = null)
        {
            Directory.CreateDirectory(Root);
            int day = GameClock.I != null ? GameClock.I.Day : 0;
            float hours = GameClock.I != null ? GameClock.I.Hours : 12f;
            string id = $"p{_next++}";
            string file = $"{DateTime.Now:yyyyMMdd-HHmmss}-{kind}-{id}.png";
            File.WriteAllBytes(Path.Combine(Root, file), tex.EncodeToPNG());
            var e = new PhotoEntry { Id = id, File = file, Kind = kind, JobId = jobId, Caption = caption ?? "", Day = day, Hours = hours };
            Photos.Add(e);
            Changed?.Invoke();
            return e;
        }

        public static void Delete(PhotoEntry e)
        {
            if (e == null) return;
            try { var p = PathOf(e); if (File.Exists(p)) File.Delete(p); } catch (Exception ex) { Debug.LogWarning("[Photo] delete: " + ex.Message); }
            PhotoThumbs.Forget(e.Id);
            Photos.Remove(e);
            foreach (var a in Albums)
            {
                if (a.BeforeId == e.Id) a.BeforeId = null;
                if (a.AfterId == e.Id) a.AfterId = null;
                a.ProgressIds.Remove(e.Id);
            }
            Changed?.Invoke();
        }

        public static void DeleteAlbum(JobAlbum a)
        {
            if (a == null) return;
            var ids = new List<string> { a.BeforeId, a.AfterId }; ids.AddRange(a.ProgressIds);
            foreach (var id in ids) Delete(Get(id));
            Albums.Remove(a);
            Changed?.Invoke();
        }

        public static void ToggleFavourite(PhotoEntry e) { if (e == null) return; e.Favourite = !e.Favourite; Changed?.Invoke(); }
        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>+1 reputation each time the number of documented jobs reaches a step (career only).</summary>
        public static void CheckBonus()
        {
            int docs = DocumentedJobs;
            while (BonusesGiven < BonusSteps.Length && docs >= BonusSteps[BonusesGiven])
            {
                BonusesGiven++;
                if (GameSettings.IsCareer)
                {
                    Company.Reputation += 1;
                    Game.Toast($"Your portfolio of {docs} before/after jobs impresses customers: reputation +1");
                }
            }
        }

        /// <summary>Deletes every indexed file and clears the index (a brand-new game keeps no photos of another career).</summary>
        public static void Clear(bool deleteFiles)
        {
            if (deleteFiles) foreach (var e in new List<PhotoEntry>(Photos)) { try { var p = PathOf(e); if (File.Exists(p)) File.Delete(p); } catch { } }
            Photos.Clear(); Albums.Clear(); BonusesGiven = 0; _next = 1;
            PhotoThumbs.Clear();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ save

        [Serializable]
        class Data
        {
            public int next = 1, bonuses;
            public List<PhotoEntry> photos = new List<PhotoEntry>();
            public List<JobAlbum> albums = new List<JobAlbum>();
        }

        public static string SaveJson() => JsonUtility.ToJson(new Data { next = _next, bonuses = BonusesGiven, photos = new List<PhotoEntry>(Photos), albums = new List<JobAlbum>(Albums) });

        public static void LoadJson(string json)
        {
            Clear(false);
            if (!string.IsNullOrEmpty(json))
            {
                var d = JsonUtility.FromJson<Data>(json);
                if (d != null)
                {
                    _next = Mathf.Max(1, d.next); BonusesGiven = d.bonuses;
                    if (d.photos != null) Photos.AddRange(d.photos);
                    if (d.albums != null) Albums.AddRange(d.albums);
                    foreach (var a in Albums) if (a.ProgressIds == null) a.ProgressIds = new List<string>();
                }
            }
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { Photos.Clear(); Albums.Clear(); BonusesGiven = 0; _next = 1; Changed = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() { SaveSystem.RegisterExtension("photos", SaveJson, LoadJson); }
    }

    /// <summary>Small cache of PNGs loaded back for the Portfolio app and the office frames.</summary>
    public static class PhotoThumbs
    {
        static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        static readonly List<string> _order = new List<string>();
        const int Max = 48;

        public static Texture2D Get(PhotoEntry e)
        {
            if (e == null) return null;
            if (_cache.TryGetValue(e.Id, out var t) && t != null) return t;
            t = Load(e);
            if (t == null) return null;
            _cache[e.Id] = t; _order.Add(e.Id);
            while (_order.Count > Max) Forget(_order[0]);
            return t;
        }

        /// <summary>Loads the PNG into a new texture the caller owns (not cached), or null.</summary>
        public static Texture2D Load(PhotoEntry e)
        {
            string p = PhotoStore.PathOf(e);
            if (p == null || !File.Exists(p)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            if (!t.LoadImage(File.ReadAllBytes(p))) { UnityEngine.Object.Destroy(t); return null; }
            return t;
        }

        public static void Forget(string id)
        {
            if (_cache.TryGetValue(id, out var t) && t != null) UnityEngine.Object.Destroy(t);
            _cache.Remove(id); _order.Remove(id);
        }

        public static void Clear() { foreach (var id in new List<string>(_order)) Forget(id); _cache.Clear(); _order.Clear(); }
    }
}
