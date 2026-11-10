using System;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Saves the building pad (Concrete/SiteGround: heights, cover, compaction) under extension key "siteground".</summary>
    public static class SiteGroundSave
    {
        [Serializable] class Data { public int n; public string heights, cover, compacted; }

        static string Save()
        {
            var g = SiteGround.I;
            if (g == null) return null;
            var hb = new byte[g.H.Length * 4];
            Buffer.BlockCopy(g.H, 0, hb, 0, hb.Length);
            var cb = new byte[g.Cover.Length];
            for (int i = 0; i < cb.Length; i++) cb[i] = (byte)g.Cover[i];
            var kb = new byte[g.Compacted.Length];
            for (int i = 0; i < kb.Length; i++) kb[i] = g.Compacted[i] ? (byte)1 : (byte)0;
            return JsonUtility.ToJson(new Data
            {
                n = SiteGround.N,
                heights = Convert.ToBase64String(hb),
                cover = Convert.ToBase64String(cb),
                compacted = Convert.ToBase64String(kb),
            });
        }

        static void Load(string json)
        {
            var g = SiteGround.I;
            if (g == null) return;
            // reset to untouched grass first (load(null) = new game)
            Array.Clear(g.H, 0, g.H.Length);
            for (int i = 0; i < g.Cover.Length; i++) { g.Cover[i] = GroundCover.Grass; g.Compacted[i] = false; }
            if (!string.IsNullOrEmpty(json))
            {
                var d = JsonUtility.FromJson<Data>(json);
                if (d != null && d.n == SiteGround.N)
                {
                    var hb = Convert.FromBase64String(d.heights ?? "");
                    if (hb.Length == g.H.Length * 4) Buffer.BlockCopy(hb, 0, g.H, 0, hb.Length);
                    var cb = Convert.FromBase64String(d.cover ?? "");
                    if (cb.Length == g.Cover.Length) for (int i = 0; i < cb.Length; i++) g.Cover[i] = (GroundCover)cb[i];
                    var kb = Convert.FromBase64String(d.compacted ?? "");
                    if (kb.Length == g.Compacted.Length) for (int i = 0; i < kb.Length; i++) g.Compacted[i] = kb[i] != 0;
                }
            }
            g.Version++;
            g.Rebuild();
            if (Grade.Field != null) { Grade.Field.SnapToPad(); Grade.Field.MarkAllDirty(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => SaveSystem.RegisterExtension("siteground", Save, Load);
    }
}
