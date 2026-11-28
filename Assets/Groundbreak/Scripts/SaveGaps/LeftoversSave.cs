using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Small things a save used to drop (save key "leftovers"):
    /// <list type="bullet">
    /// <item>Partly used material: MEP pipe / cable / duct offcut feet, house-wrap seam tape and flashing tape left on the open
    /// rolls, rebar stick offcuts and ties left on the reel, the kicker 2x4 being cut up, joint compound / joint tape left, primer
    /// and paint left in the open cans, insulation roll left, loose interior fasteners / sheet goods, trim offcuts, gravel owed.
    /// These live in each trade's private counters; they are read and written by field name (a renamed field is skipped with a
    /// warning, never an error).</item>
    /// <item>StructuralMember data (role, connectors, group) of plain pieces, so framing placed by hand or by a job the shell
    /// doesn't replay keeps its role for the support graph and inspections.</item>
    /// </list>
    /// A new game (and an old save without this section) resets the counters. <see cref="MainMenu.NewGameStarted"/> also resets the
    /// session statics a new game used to keep: the Stair Calculator layout, the paint colour / sheen, the Finish Supply cart,
    /// <see cref="FinishStock.Unlimited"/>, the Framing Plans size choice and orphaned tape-measure lines.
    /// </summary>
    public static class LeftoversSave
    {
        public const string Key = "leftovers";

        [Serializable] class NumD { public string type, field; public float v; }
        [Serializable] class MapD { public string type, field; public List<string> keys = new List<string>(); public List<float> vals = new List<float>(); }
        [Serializable] class ListMapD { public string type, field; public List<string> keys = new List<string>(); public List<int> counts = new List<int>(); public List<float> vals = new List<float>(); }
        [Serializable] class MemberD { public SaveGaps.PieceRef piece = new SaveGaps.PieceRef(); public int role, connectors; public string group; }
        [Serializable] class ChalkD { public Vector3 a, b, n; public float widthIn; }
        [Serializable]
        class Data
        {
            public int version = 1;
            public List<NumD> nums = new List<NumD>(); public List<MapD> maps = new List<MapD>(); public List<ListMapD> listMaps = new List<ListMapD>();
            public List<MemberD> members = new List<MemberD>();
            public List<ChalkD> chalk = new List<ChalkD>();
            public float paintPrimerUsed, paintUsed; public int trimCuts, trimSticks;
        }

        /// <summary>(type, private static field) of every partial-use counter: float / int, Dictionary&lt;string, float|int&gt; or
        /// Dictionary&lt;string, List&lt;float&gt;&gt;.</summary>
        static readonly (Type type, string field)[] Fields =
        {
            (typeof(MepStock), "_offcutFt"),
            (typeof(HouseWrap), "_tapeFeet"), (typeof(HouseWrap), "_flashFeet"),
            (typeof(RebarSystem), "_openStick"), (typeof(RebarSystem), "_tiesLeftInRoll"),
            (typeof(FormSystem), "_kickerLeft"),
            (typeof(GravelBase), "_owedCf"),
            (typeof(Drywall), "_mudLeft"), (typeof(Drywall), "_tapeFeetPaper"), (typeof(Drywall), "_tapeFeetMesh"),
            (typeof(PaintJob), "_primerGal"), (typeof(PaintJob), "_paintGal"),
            (typeof(Insulation), "_rollLeft"), (typeof(Insulation), "_seed"),
            (typeof(InteriorSupplies), "_loose"),
            (typeof(TrimStock), "Offcuts"),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension(Key, Save, Load, 40);
            MainMenu.NewGameStarted += _ => ResetSession();
        }

        static FieldInfo F(Type t, string name)
        {
            var f = t.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) Debug.LogWarning($"[Save] leftovers: {t.Name}.{name} not found (renamed?) - skipped");
            return f;
        }

        // ================================================================== save

        static string Save()
        {
            var d = new Data();
            foreach (var (t, name) in Fields)
            {
                var f = F(t, name); if (f == null) continue;
                var v = f.GetValue(null);
                if (v is float fl) d.nums.Add(new NumD { type = t.Name, field = name, v = fl });
                else if (v is int i) d.nums.Add(new NumD { type = t.Name, field = name, v = i });
                else if (v is Dictionary<string, float> df) { var m = new MapD { type = t.Name, field = name }; foreach (var kv in df) { m.keys.Add(kv.Key); m.vals.Add(kv.Value); } d.maps.Add(m); }
                else if (v is Dictionary<string, int> di) { var m = new MapD { type = t.Name, field = name }; foreach (var kv in di) { m.keys.Add(kv.Key); m.vals.Add(kv.Value); } d.maps.Add(m); }
                else if (v is Dictionary<string, List<float>> dl)
                {
                    var m = new ListMapD { type = t.Name, field = name };
                    foreach (var kv in dl) { m.keys.Add(kv.Key); m.counts.Add(kv.Value != null ? kv.Value.Count : 0); if (kv.Value != null) m.vals.AddRange(kv.Value); }
                    d.listMaps.Add(m);
                }
            }
            d.paintPrimerUsed = PaintJob.GallonsUsedPrimer; d.paintUsed = PaintJob.GallonsUsedPaint;
            d.trimCuts = TrimStock.SawCuts; d.trimSticks = TrimStock.SticksUsed;

            // roles of plain pieces (the ones SaveSystem writes to its piece list)
            foreach (var m in UnityEngine.Object.FindObjectsByType<StructuralMember>(FindObjectsSortMode.InstanceID))
            {
                var p = m != null ? m.Piece : null;
                if (p == null || p.Def == null || !p.gameObject.activeInHierarchy || !m.enabled) continue;
                if (ShellSave.IsShellPiece(p) || JobSave.IsJobPiece(p) || p.GetComponent<CutStringer>() != null || p.GetComponent<FormPart>() != null || p.GetComponent<DeckPart>() != null) continue;
                if (p.GetComponentInParent<MaterialPile>() != null || p.GetComponentInParent<HungDoor>() != null) continue;
                d.members.Add(new MemberD { piece = SaveGaps.Ref(p), role = (int)m.Role, connectors = (int)m.Connectors, group = m.Group });
            }

            // snapped chalk lines (the line's first quad is the crisp core: a-side, a+side, b+side, b-side, lifted off the surface)
            foreach (var go in ChalkLine.Lines)
            {
                if (go == null || !go.activeInHierarchy) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < 4) continue;
                var v = mf.sharedMesh.vertices;
                Vector3 m0 = (v[0] + v[1]) * 0.5f, m1 = (v[2] + v[3]) * 0.5f, side = (v[1] - v[0]) * 0.5f;
                Vector3 dir = (m1 - m0).normalized;
                Vector3 n = Vector3.Cross(dir, side).normalized;
                if (dir.sqrMagnitude < 0.5f || n.sqrMagnitude < 0.5f) continue;
                d.chalk.Add(new ChalkD { a = go.transform.TransformPoint(m0 - n * 0.0012f), b = go.transform.TransformPoint(m1 - n * 0.0012f), n = go.transform.TransformDirection(n), widthIn = (v[1] - v[0]).magnitude / Units.Inch });
            }
            return JsonUtility.ToJson(d);
        }

        // ================================================================== load

        static void Load(string json)
        {
            var d = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Data>(json);
            // counters: back to empty first (new game / old save), then the saved values
            foreach (var (t, name) in Fields)
            {
                var f = F(t, name); if (f == null) continue;
                var v = f.GetValue(null);
                if (v is float) f.SetValue(null, 0f);
                else if (v is int) f.SetValue(null, name == "_seed" ? 1 : 0);
                else if (v is IDictionary dict) dict.Clear();
            }
            PaintJob.GallonsUsedPrimer = PaintJob.GallonsUsedPaint = 0f;
            TrimStock.SawCuts = TrimStock.SticksUsed = 0;
            // chalk lines: all of them come from this list (lines the shell replay just snapped again included)
            foreach (var go in ChalkLine.Lines) SaveGaps.Kill(go);
            ChalkLine.Lines.Clear();
            if (d == null) return;
            foreach (var c in d.chalk) ChalkLine.SnapWorld(c.a, c.b, c.widthIn, c.n.sqrMagnitude > 0.5f ? c.n : Vector3.up);
            foreach (var n in d.nums)
            {
                var f = Find(n.type, n.field); if (f == null) continue;
                if (f.FieldType == typeof(float)) f.SetValue(null, n.v);
                else if (f.FieldType == typeof(int)) f.SetValue(null, Mathf.RoundToInt(n.v));
            }
            foreach (var m in d.maps)
            {
                var f = Find(m.type, m.field); if (f == null) continue;
                var v = f.GetValue(null);
                for (int i = 0; i < m.keys.Count && i < m.vals.Count; i++)
                {
                    if (v is Dictionary<string, float> df) df[m.keys[i]] = m.vals[i];
                    else if (v is Dictionary<string, int> di) di[m.keys[i]] = Mathf.RoundToInt(m.vals[i]);
                }
            }
            foreach (var m in d.listMaps)
            {
                var f = Find(m.type, m.field); if (f == null) continue;
                if (!(f.GetValue(null) is Dictionary<string, List<float>> dl)) continue;
                int k = 0;
                for (int i = 0; i < m.keys.Count && i < m.counts.Count; i++)
                {
                    var l = new List<float>();
                    for (int j = 0; j < m.counts[i] && k < m.vals.Count; j++) l.Add(m.vals[k++]);
                    dl[m.keys[i]] = l;
                }
            }
            PaintJob.GallonsUsedPrimer = d.paintPrimerUsed; PaintJob.GallonsUsedPaint = d.paintUsed;
            TrimStock.SawCuts = d.trimCuts; TrimStock.SticksUsed = d.trimSticks;

            var pieces = new SaveGaps.PieceIndex();
            foreach (var md in d.members)
            {
                var p = pieces.Find(md.piece);
                if (p == null || p.GetComponent<StructuralMember>() != null) continue;
                var m = p.gameObject.AddComponent<StructuralMember>();
                m.Role = (MemberRole)md.role; m.Connectors = (Connector)md.connectors; m.Group = md.group ?? "";
            }
            SupportGraph.Dirty = true;
        }

        static FieldInfo Find(string type, string field)
        {
            foreach (var (t, name) in Fields) if (t.Name == type && name == field) return F(t, name);
            return null;
        }

        // ================================================================== new game

        /// <summary>Session state that isn't part of a save but must not leak from the last career into a new game.</summary>
        public static void ResetSession()
        {
            StairCalculator.Layout = default; StairCalculator.HasLayout = false;
            Set(typeof(StairCalculator), "_rise", ""); Set(typeof(StairCalculator), "_run", "10 1/4");
            PaintColors.Current = 2; PaintColors.CurrentSheen = Sheen.Eggshell;
            var cart = F(typeof(FinishSupply), "_cart")?.GetValue(null) as IDictionary; cart?.Clear();
            Set(typeof(FinishSupply), "_trade", 0);
            FinishStock.Unlimited = false;
            Set(typeof(ShellApp), "_lenFt", 16); Set(typeof(ShellApp), "_widFt", 12); Set(typeof(ShellApp), "_doorIdx", 0); Set(typeof(ShellApp), "_winIdx", 0); Set(typeof(ShellApp), "_iJoist", false);
            if (ShellProject.Current != null) ShellSave.Wipe();
            TapeMeasure.LastMeasurement = 0f;
            // tape-measure lines of tapes that left the belt with the old career
            var keep = new HashSet<LineRenderer>();
            var lineField = typeof(TapeMeasure).GetField("_line", BindingFlags.Instance | BindingFlags.NonPublic);
            if (Game.Belt != null && lineField != null)
                foreach (var t in Game.Belt.Owned) if (t is TapeMeasure tm && lineField.GetValue(tm) is LineRenderer lr && lr != null) keep.Add(lr);
            foreach (var lr in UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (lr != null && lr.gameObject.name == "TapeLine" && lr.transform.parent == null && !keep.Contains(lr)) UnityEngine.Object.Destroy(lr.gameObject);
        }

        static void Set(Type t, string name, object value)
        {
            var f = F(t, name);
            if (f != null && f.FieldType == value.GetType()) f.SetValue(null, value);
        }
    }
}
