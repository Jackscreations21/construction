using System;
using System.IO;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Where tutorial capture tests write their pictures. Normal test runs must NOT rewrite the committed PNGs under
    /// Assets/Groundbreak/Resources/Tutorials/, so they write to Logs/shots/tut_pics/ instead (still useful to look at).
    /// Set the environment variable GROUNDBREAK_CAPTURE_TUTORIALS=1 for the run to regenerate the committed pictures
    /// (see Docs/TUTORIALS.md).</summary>
    public static class TutorialCapture
    {
        public const string EnvVar = "GROUNDBREAK_CAPTURE_TUTORIALS";

        /// <summary>True only when the run was explicitly asked to regenerate the committed tutorial pictures.</summary>
        public static bool Enabled
        {
            get
            {
                var v = Environment.GetEnvironmentVariable(EnvVar);
                return !string.IsNullOrEmpty(v) && v != "0" && !v.Equals("false", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>The committed pictures (what the Tutorials app loads).</summary>
        public static string CommittedDir => Path.Combine(Application.dataPath, "Groundbreak", "Resources", "Tutorials");

        /// <summary>Scratch copy for normal runs (Logs/ is git-ignored).</summary>
        public static string ScratchDir => Path.Combine(Application.dataPath, "..", "Logs", "shots", "tut_pics");

        /// <summary>Folder a capture test should write a tutorial picture to in this run.</summary>
        public static string OutputDir
        {
            get { var d = Enabled ? CommittedDir : ScratchDir; Directory.CreateDirectory(d); return d; }
        }

        /// <summary>Path a picture should be written to in this run.</summary>
        public static string OutputPath(string name) => Path.Combine(OutputDir, name + ".png");

        /// <summary>The committed picture exists (what tests assert when they did not regenerate it).</summary>
        public static bool Committed(string name) => File.Exists(Path.Combine(CommittedDir, name + ".png"));
    }
}
