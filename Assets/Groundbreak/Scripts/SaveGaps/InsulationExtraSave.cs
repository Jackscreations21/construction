using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Insulation bookkeeping the "drywall" section didn't keep (save key "insulation2", order 25: after "drywall" rebuilds the
    /// attic fills): faces marked exterior / interior with <see cref="Insulation.MarkExterior"/>, the openings already
    /// foam-shimmed (so the foam gun doesn't foam them again and charge another can) and the partly used loose-fill bag in each
    /// attic's blower. These are private fields of the insulation trade; they are read and written by name (a renamed field is
    /// skipped with a warning). A new game or an old save clears them.
    /// </summary>
    public static class InsulationExtraSave
    {
        public const string Key = "insulation2";

        [Serializable] class MarkD { public SaveGaps.PlaneRef plane = new SaveGaps.PlaneRef(); public bool ext; }
        [Serializable] class AtticD { public SaveGaps.PlaneRef plane = new SaveGaps.PlaneRef(); public float volLeft; }
        [Serializable]
        class Data
        {
            public int version = 1;
            public List<MarkD> marks = new List<MarkD>(); public List<Vector3> shimmed = new List<Vector3>(); public List<AtticD> attics = new List<AtticD>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => SaveSystem.RegisterExtension(Key, Save, Load, 25);

        static FieldInfo SF(string name)
        {
            var f = typeof(Insulation).GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) Debug.LogWarning("[Save] insulation2: Insulation." + name + " not found (renamed?) - skipped");
            return f;
        }

        static FieldInfo VolLeft()
        {
            var f = typeof(AtticFill).GetField("_volLeft", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null || f.FieldType != typeof(float)) { Debug.LogWarning("[Save] insulation2: AtticFill._volLeft not found (renamed?) - skipped"); return null; }
            return f;
        }

        static List<(FramePlane fp, bool ext)> Marks() => SF("_exterior")?.GetValue(null) as List<(FramePlane fp, bool ext)>;
        static List<Vector3> Shimmed() => SF("_shimmed")?.GetValue(null) as List<Vector3>;

        static string Save()
        {
            var d = new Data();
            var marks = Marks();
            if (marks != null) foreach (var (fp, ext) in marks) if (fp != null) d.marks.Add(new MarkD { plane = SaveGaps.Plane(fp), ext = ext });
            var sh = Shimmed();
            if (sh != null) d.shimmed.AddRange(sh);
            var vf = VolLeft();
            if (vf != null)
                foreach (var a in Insulation.Attics)
                    if (a != null && a.gameObject.activeInHierarchy && a.Fp != null) d.attics.Add(new AtticD { plane = SaveGaps.Plane(a.Fp), volLeft = (float)vf.GetValue(a) });
            return JsonUtility.ToJson(d);
        }

        static void Load(string json)
        {
            var marks = Marks(); marks?.Clear();
            var sh = Shimmed(); sh?.Clear();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            int missing = 0;
            foreach (var m in d.marks)
            {
                var fp = SaveGaps.FindPlane(m.plane);
                if (fp == null) { missing++; continue; }
                Insulation.MarkExterior(fp, m.ext);
            }
            sh?.AddRange(d.shimmed);
            var vf = VolLeft();
            if (vf != null)
                foreach (var ad in d.attics)
                {
                    var fp = SaveGaps.FindPlane(ad.plane);
                    var af = fp != null ? Insulation.AtticFor(fp, false) : null;
                    if (af == null) { missing++; continue; }
                    vf.SetValue(af, ad.volLeft);
                }
            if (missing > 0) Debug.LogWarning($"[Save] insulation2: {missing} marked face(s) / attic(s) not found on the restored framing");
        }
    }
}
