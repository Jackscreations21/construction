using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Marks a set concrete slab rebuilt from a save (it has no PourSystem behind it) and frees its mesh.</summary>
    public class RestoredSlab : MonoBehaviour
    {
        public Mesh Mesh;
        float _tint = -1f, _next;
        void OnDestroy() { if (Mesh != null) Destroy(Mesh); }

        /// <summary>Green concrete lightens as it dries (same curve as a PourSystem slab): keep the restored slab's colour current.</summary>
        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 1f;
            var el = GetComponent<ConcreteElement>();
            if (el == null) return;
            float tint = Mathf.Round(Mathf.Lerp(0.66f, 0.86f, Mathf.Clamp01(el.AgeDays / 5f)) * 20f) / 20f;
            if (Mathf.Abs(tint - _tint) > 0.02f) { _tint = tint; el.ApplyLook(GetComponent<MeshRenderer>()); }
        }
    }

    /// <summary>
    /// Saves set concrete (save key "slabs"): every <see cref="ConcreteElement"/> (outline, top, thickness, mix, pour time,
    /// cure lag, finish, curing method, defects, anchor bolts, control joints) and the cold-weather protection on it
    /// (curing blankets / heater from <see cref="SeasonRules"/>). On load each slab comes back as a "Concrete Slab" object
    /// with a real-size mesh of its outline, a collider and the same ConcreteElement data, so cure strength keeps counting
    /// and the blankets come off (back to stock) when it is strong enough. Plastic pours, forms and rebar are not saved.
    /// </summary>
    public static class SlabSave
    {
        public const string Key = "slabs";

        [Serializable]
        class SlabData
        {
            public string kind = "slab"; public float topY, thickIn, pouredAt, yards, cureLag; public int psi, finish;
            public bool covered, wetCured, stripped;
            public List<Vector2> outline = new List<Vector2>();
            public List<string> defects = new List<string>();
            public List<Vector3> anchors = new List<Vector3>();
            public List<Vector3> jointA = new List<Vector3>(), jointB = new List<Vector3>();
            public int blankets; public bool heater;
        }
        [Serializable] class Data { public int version = 1; public List<SlabData> slabs = new List<SlabData>(); }

        static readonly List<(ConcreteElement el, int blankets, bool heater)> _pending = new List<(ConcreteElement, int, bool)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _pending.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension(Key, Save, Load);
            // the "seasons" section (which clears protection) may load after this one: put the blankets back at the very end
            SaveSystem.AfterLoad += ApplyPending;
        }

        static string Save()
        {
            var d = new Data();
            foreach (var el in ConcreteElement.All)
            {
                if (el == null || !el.gameObject.activeInHierarchy || el.Outline == null || el.Outline.Length < 3) continue;
                if (ConcreteSave.Handles(el)) continue;   // saved with its pour (and forms) by the "concrete" section
                var sd = new SlabData
                {
                    kind = el.Kind, topY = el.TopY, thickIn = el.ThicknessInches, pouredAt = el.PouredAt, yards = el.Yards, cureLag = el.CureLagMinutes,
                    psi = el.Psi, finish = (int)el.Finish, covered = el.Covered, wetCured = el.WetCured, stripped = el.FormsStripped,
                    blankets = SeasonRules.BlanketsOn(el), heater = SeasonRules.HeaterOn(el),
                };
                sd.outline.AddRange(el.Outline);
                sd.defects.AddRange(el.Defects);
                foreach (var a in el.AnchorBolts) if (a != null) sd.anchors.Add(a.position);
                var ps = el.Pour != null ? el.Pour : el.GetComponent<PourSystem>();
                if (ps != null) foreach (var (a, b) in ps.Joints) { sd.jointA.Add(a); sd.jointB.Add(b); }
                var saved = el.GetComponent<RestoredSlabJoints>();
                if (saved != null) for (int i = 0; i < saved.A.Count; i++) { sd.jointA.Add(saved.A[i]); sd.jointB.Add(saved.B[i]); }
                d.slabs.Add(sd);
            }
            return JsonUtility.ToJson(d);
        }

        static void Load(string json)
        {
            _pending.Clear();
            // set slabs from the session being left (PourSystem slabs and restored ones); plastic pours are left alone
            var kill = new List<GameObject>();
            foreach (var el in ConcreteElement.All) if (el != null) kill.Add(el.gameObject);
            foreach (var g in kill) SaveGaps.Kill(g);
            foreach (var g in kill) { var el = g.GetComponent<ConcreteElement>(); if (el != null) UnityEngine.Object.DestroyImmediate(el); } // leave ConcreteElement.All now
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            foreach (var sd in d.slabs)
            {
                try
                {
                    var el = Build(sd);
                    if (el != null && (sd.blankets > 0 || sd.heater)) _pending.Add((el, sd.blankets, sd.heater));
                }
                catch (Exception e) { Debug.LogError("[Save] slab failed to restore: " + e); }
            }
            ApplyPending(); // in case "seasons" already loaded (AfterLoad repeats it after every section)
        }

        /// <summary>Curing blankets / heater to put back on a slab restored by another section (once "seasons" has loaded too).</summary>
        public static void QueueProtection(ConcreteElement el, int blankets, bool heater)
        {
            if (el == null) return;
            _pending.Add((el, blankets, heater));
            SeasonRules.RestoreProtection(el, blankets, heater);
        }

        static void ApplyPending()
        {
            foreach (var (el, n, heater) in _pending) if (el != null) SeasonRules.RestoreProtection(el, n, heater);
        }

        /// <summary>Rebuilds a set slab: outline mesh (top + edges), collider, ConcreteElement data, J-bolts, control joints.</summary>
        static ConcreteElement Build(SlabData sd)
        {
            if (sd.outline.Count < 3) return null;
            var poly = sd.outline.ToArray();
            float thick = Units.In(Mathf.Max(1f, sd.thickIn));
            var go = new GameObject("Concrete Slab");   // name: framing's default foundation probe
            var b = new MeshBuilder { MinSubmeshCount = 3 };
            float top = sd.topY, bottom = sd.topY - thick - 0.02f;
            foreach (var tri in Triangulate(poly))
                b.AddTriangle(new Vector3(poly[tri.x].x, top, poly[tri.x].y), new Vector3(poly[tri.y].x, top, poly[tri.y].y), new Vector3(poly[tri.z].x, top, poly[tri.z].y), 0, Vector3.up);
            float area = SignedArea(poly);
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], c = poly[(i + 1) % poly.Length];
                Vector2 e = (c - a); if (e.sqrMagnitude < 1e-8f) continue;
                Vector2 outN = area > 0 ? new Vector2(e.y, -e.x) : new Vector2(-e.y, e.x);
                b.AddQuad(new Vector3(a.x, top, a.y), new Vector3(c.x, top, c.y), new Vector3(c.x, bottom, c.y), new Vector3(a.x, bottom, a.y), 1, new Vector3(outN.x, 0f, outN.y), new Vector3(e.x, 0f, e.y));
            }
            var mesh = b.Build("RestoredSlab");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { Materials.Get((SurfaceKind)sd.finish), Materials.Get(SurfaceKind.Concrete), Materials.Get(SurfaceKind.ConcreteBlock) };
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<RestoredSlab>().Mesh = mesh;

            var el = go.AddComponent<ConcreteElement>();
            bool was = SeasonRules.Restoring;
            SeasonRules.Restoring = true;    // don't lay (and charge for) blankets again: the saved ones go back on below
            try { el.Init(null, poly, sd.topY, sd.thickIn, sd.psi, sd.pouredAt, (SurfaceKind)sd.finish, sd.yards); }
            finally { SeasonRules.Restoring = was; }
            el.Kind = sd.kind; el.Covered = sd.covered; el.WetCured = sd.wetCured; el.FormsStripped = sd.stripped; el.CureLagMinutes = sd.cureLag;
            el.Defects.AddRange(sd.defects);
            foreach (var p in sd.anchors)
            {
                var bolt = CTool.Build("AnchorBolt", (mb, m) =>
                {
                    int galv = m.K(SurfaceKind.Galvanized);
                    mb.AddRod(new Vector3(0, -3.5f * Units.Inch + 1.375f * Units.Inch, 0), Quaternion.Euler(-90, 0, 0), 0.25f * Units.Inch, 10f * Units.Inch, 8, galv);
                    mb.AddCylinder(new Vector3(0, 0.75f * Units.Inch, 0), 0.6f * Units.Inch, 0.08f * Units.Inch, 12, galv);
                    mb.AddCylinder(new Vector3(0, 1.0f * Units.Inch, 0), 0.43f * Units.Inch, 0.45f * Units.Inch, 6, galv);
                }, go.transform);
                bolt.transform.position = p;
                el.AnchorBolts.Add(bolt.transform);
            }
            if (sd.jointA.Count > 0)
            {
                var j = go.AddComponent<RestoredSlabJoints>();
                for (int i = 0; i < sd.jointA.Count && i < sd.jointB.Count; i++) { j.A.Add(sd.jointA[i]); j.B.Add(sd.jointB[i]); }
                CTool.Build("ControlJoints", (mb, m) =>
                {
                    int dark = m.C(new Color(0.22f, 0.22f, 0.21f));
                    for (int i = 0; i < j.A.Count; i++)
                    {
                        Vector3 dd = j.B[i] - j.A[i]; dd.y = 0; Vector3 s = Vector3.Cross(Vector3.up, dd.normalized) * (0.125f * Units.Inch);
                        Vector3 a = j.A[i] + Vector3.up * 0.0015f, c = j.B[i] + Vector3.up * 0.0015f;
                        mb.AddQuad(a - s, a + s, c + s, c - s, dark, Vector3.up);
                    }
                }, go.transform);
            }
            el.ApplyLook(mr);
            return el;
        }

        static float SignedArea(Vector2[] p)
        {
            float a = 0; for (int i = 0, j = p.Length - 1; i < p.Length; j = i++) a += (p[j].x + p[i].x) * (p[j].y - p[i].y);
            return -a * 0.5f; // > 0 = counter-clockwise in (x, z)
        }

        /// <summary>Ear-clipping triangulation of a simple polygon (L-shaped and other concave slabs too).</summary>
        static List<Vector3Int> Triangulate(Vector2[] p)
        {
            var res = new List<Vector3Int>();
            var idx = new List<int>(); for (int i = 0; i < p.Length; i++) idx.Add(i);
            bool ccw = SignedArea(p) > 0;
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int i0 = idx[(k + idx.Count - 1) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                    Vector2 a = p[i0], b = p[i1], c = p[i2];
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (ccw ? cross <= 1e-9f : cross >= -1e-9f) continue; // reflex or flat
                    bool inside = false;
                    foreach (int o in idx)
                    {
                        if (o == i0 || o == i1 || o == i2) continue;
                        if (InTri(p[o], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    res.Add(new Vector3Int(i0, i1, i2));
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // degenerate: fan the rest
            }
            for (int k = 1; k + 1 < idx.Count; k++) res.Add(new Vector3Int(idx[0], idx[k], idx[k + 1]));
            return res;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }

    /// <summary>Control joints of a restored slab (kept so the next save has them again).</summary>
    public class RestoredSlabJoints : MonoBehaviour
    {
        public readonly List<Vector3> A = new List<Vector3>(), B = new List<Vector3>();
    }
}
