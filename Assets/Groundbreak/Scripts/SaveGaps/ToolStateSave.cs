using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A tool with private per-instance state (beyond battery / durability and its public fields) can implement this to
    /// have it saved with the tool belt.</summary>
    public interface ISavedToolState
    {
        string SaveToolState();
        void LoadToolState(string state);
    }

    /// <summary>
    /// Per-tool-instance state saved with each owned tool (<see cref="SaveSystem.ToolData.state"/>): every public, writable
    /// float / int / bool / string / enum field a tool class declares (the caulk gun's <see cref="CaulkGun.FeetLeft"/>, the chute
    /// wheelbarrow's concrete, the landscaping wheelbarrow's load, a close-up tool's <see cref="CloseupTool.Mode"/>, the hatchet's
    /// gauge, the drill bit, the route tool's slope / height ...), the loaded mode by name (so the caulk gun gets the same tube type
    /// back even if tube types register in another order), and <see cref="ISavedToolState"/> for anything private.
    /// Older saves have no state: the tool comes back fresh (as before).
    /// </summary>
    public static class ToolStateSave
    {
        [Serializable] class KV { public string k, v; }
        [Serializable] class Data { public List<KV> f = new List<KV>(); public string mode, custom; }

        static readonly Dictionary<Type, List<FieldInfo>> _fields = new Dictionary<Type, List<FieldInfo>>();

        static List<FieldInfo> Fields(Type t)
        {
            if (_fields.TryGetValue(t, out var l)) return l;
            l = new List<FieldInfo>();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.DeclaringType == typeof(Tool) || f.IsInitOnly || f.IsLiteral) continue;
                var ft = f.FieldType;
                if (ft == typeof(float) || ft == typeof(int) || ft == typeof(bool) || ft == typeof(string) || ft.IsEnum) l.Add(f);
            }
            _fields[t] = l;
            return l;
        }

        public static string Save(Tool t)
        {
            if (t == null) return null;
            var d = new Data();
            try
            {
                foreach (var f in Fields(t.GetType()))
                {
                    var v = f.GetValue(t);
                    string s = v == null ? null : v is float fl ? fl.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(v, CultureInfo.InvariantCulture);
                    if (s != null) d.f.Add(new KV { k = f.Name, v = s });
                }
                if (t is CloseupTool ct && ct.Modes != null && ct.Modes.Length > 0) d.mode = ct.ModeName;
                if (t is ISavedToolState st) d.custom = st.SaveToolState();
            }
            catch (Exception e) { Debug.LogError("[Save] tool state of " + t.Id + " failed to save: " + e); }
            if (d.f.Count == 0 && d.mode == null && d.custom == null) return null;
            return JsonUtility.ToJson(d);
        }

        public static void Load(Tool t, string json)
        {
            if (t == null || string.IsNullOrEmpty(json)) return;
            try
            {
                var d = JsonUtility.FromJson<Data>(json);
                if (d == null) return;
                var fields = Fields(t.GetType());
                foreach (var kv in d.f)
                {
                    var f = fields.Find(x => x.Name == kv.k);
                    if (f == null) continue;   // renamed / removed field: skipped
                    var ft = f.FieldType;
                    try
                    {
                        object v = ft == typeof(float) ? float.Parse(kv.v, CultureInfo.InvariantCulture)
                                 : ft == typeof(int) ? int.Parse(kv.v, CultureInfo.InvariantCulture)
                                 : ft == typeof(bool) ? bool.Parse(kv.v)
                                 : ft == typeof(string) ? kv.v
                                 : Enum.Parse(ft, kv.v);
                        f.SetValue(t, v);
                    }
                    catch (Exception) { Debug.LogWarning($"[Save] tool {t.Id}: couldn't restore {kv.k} = {kv.v}"); }
                }
                if (t is CloseupTool ct && !string.IsNullOrEmpty(d.mode) && ct.Modes != null)
                {
                    int i = Array.IndexOf(ct.Modes, d.mode);
                    if (i >= 0) ct.Mode = i;
                }
                if (t is ISavedToolState st && d.custom != null) st.LoadToolState(d.custom);
            }
            catch (Exception e) { Debug.LogError("[Save] tool state of " + t.Id + " failed to load: " + e); }
        }
    }
}
