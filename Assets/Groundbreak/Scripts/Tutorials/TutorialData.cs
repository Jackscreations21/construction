using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One step of a tutorial: what to do, why, which keys/tools, and a picture captured from real gameplay.</summary>
    public class TutorialStep
    {
        public string Title;
        /// <summary>Short instruction with the real-world reasoning.</summary>
        public string Text;
        /// <summary>Exact keys / tools, shown highlighted under the text.</summary>
        public string Keys;
        /// <summary>Picture name under Resources/Tutorials/ (no extension). Null/missing = placeholder.</summary>
        public string Picture;
        /// <summary>"Show me where": a tablet app to open...</summary>
        public string OpenApp;
        /// <summary>...or a world point to mark with a beacon (null result = nothing to show yet).</summary>
        public Func<Vector3?> Find;
        /// <summary>Text shown with the "show me where" button, e.g. "the nail pile".</summary>
        public string Where;
    }

    public class Tutorial
    {
        public string Id, Phase, Title, Summary;
        /// <summary>False for trades not merged yet: pictures are placeholders.</summary>
        public bool Available = true;
        /// <summary>Branch / milestone that will bring this trade (shown on placeholders).</summary>
        public string ComingWith;
        public readonly List<TutorialStep> Steps = new List<TutorialStep>();

        public Tutorial Step(string title, string text, string keys, string picture, string openApp = null, Func<Vector3?> find = null, string where = null)
        {
            Steps.Add(new TutorialStep { Title = title, Text = text, Keys = keys, Picture = picture, OpenApp = openApp, Find = find, Where = where });
            return this;
        }
    }

    /// <summary>Registry of tutorials. Phases are listed in real construction order; trades that land later call
    /// <see cref="Register"/> (or <see cref="Replace"/> to swap placeholder text) from their own BeforeSceneLoad hook.</summary>
    public static class Tutorials
    {
        public static readonly List<string> PhaseOrder = new List<string>
        {
            "Getting started", "Site & foundation", "Framing", "Roof", "Mechanical, electrical & plumbing",
            "Inspections", "Close-up & finish", "Outside & business",
        };

        public static readonly List<Tutorial> All = new List<Tutorial>();
        /// <summary>Steps viewed (tutorial id -> furthest step index reached).</summary>
        public static readonly Dictionary<string, int> Progress = new Dictionary<string, int>();

        public static Tutorial Register(Tutorial t)
        {
            int i = All.FindIndex(x => x.Id == t.Id);
            // a real (Available) tutorial registered by a trade module is never replaced by a placeholder
            if (i >= 0 && All[i].Available && !t.Available) return All[i];
            if (i >= 0) All[i] = t; else All.Add(t);
            if (!PhaseOrder.Contains(t.Phase)) PhaseOrder.Add(t.Phase);
            return t;
        }

        public static Tutorial Replace(Tutorial t) => Register(t);
        public static Tutorial Get(string id) => All.Find(t => t.Id == id);

        public static Tutorial New(string id, string phase, string title, string summary, bool available = true, string comingWith = null)
            => Register(new Tutorial { Id = id, Phase = phase, Title = title, Summary = summary, Available = available, ComingWith = comingWith });

        public static void MarkSeen(Tutorial t, int step)
        {
            Progress.TryGetValue(t.Id, out int k);
            if (step + 1 > k) Progress[t.Id] = step + 1;
        }

        public static int Seen(Tutorial t) => Progress.TryGetValue(t.Id, out int k) ? k : 0;

        /// <summary>Every picture name referenced by an available tutorial (the capture test checks they all exist).</summary>
        public static IEnumerable<string> PictureNames(bool availableOnly)
        {
            var seen = new HashSet<string>();
            foreach (var t in All)
            {
                if (availableOnly && !t.Available) continue;
                foreach (var s in t.Steps) if (!string.IsNullOrEmpty(s.Picture) && seen.Add(s.Picture)) yield return s.Picture;
            }
        }
    }

    /// <summary>Loads tutorial pictures from Resources/Tutorials (in the editor it falls back to the PNG on disk, so
    /// freshly captured pictures show before Unity has imported them).</summary>
    public static class TutorialPictures
    {
        public const string Folder = "Assets/Groundbreak/Resources/Tutorials";
        static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_cache.TryGetValue(name, out var t) && t != null) return t;
#if UNITY_EDITOR
            // editor: read the PNG on disk first so freshly captured pictures show before Unity re-imports them
            {
                string path = Path.Combine(Application.dataPath, "Groundbreak", "Resources", "Tutorials", name + ".png");
                if (File.Exists(path))
                {
                    t = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = name, wrapMode = TextureWrapMode.Clamp };
                    if (!t.LoadImage(File.ReadAllBytes(path))) t = null;
                }
            }
#endif
            if (t == null) t = Resources.Load<Texture2D>("Tutorials/" + name);
            if (t != null) _cache[name] = t;
            return t;
        }

        public static void ClearCache() => _cache.Clear();
    }
}
