using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>
    /// Cheap countryside out to the horizon around the map (seen from the air the map used to end in a dark void): a patchwork of crop /
    /// hay / pasture / plowed fields (the map's own field materials, so they follow the seasons and take snow) from the map edge out to
    /// <see cref="Outer"/>, hedgerow tree lines and woodlots as low foliage blobs (bare in winter like every other tree), a few farmsteads
    /// (barn, house, silo) and the highways / county roads running on to the horizon. Flat quads, no colliders, a handful of draw calls.
    /// <see cref="FarRingCamera"/> pushes the far clip plane out for cameras high above the town so it actually renders.
    /// </summary>
    public static class FarRing
    {
        public static float Outer = 16000f;
        /// <summary>The site's grass skirt (SiteLook "FarGround") covers ±1200 m at y = 0; beyond it the ring lays its own ground.</summary>
        public const float SkirtHalf = 1200f;
        public static GameObject Root { get; private set; }
        public static int Fields { get; private set; }
        public static int TreeBlobs { get; private set; }
        public static int Vertices { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Root = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            MapWorld.Built -= Build; MapWorld.Built += Build;
            Town.Destroyed -= Hide; Town.Destroyed += Hide;
        }

        static void Hide() { if (Root != null) Root.SetActive(false); }

        /// <summary>Destroys the ring and its meshes (rebuilt on the next map build).</summary>
        public static void Clear()
        {
            if (Root == null) return;
            foreach (var mf in Root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) Object.Destroy(mf.sharedMesh);
            Object.Destroy(Root);
            Root = null;
        }

        static readonly FieldKind[] Mix = { FieldKind.Corn, FieldKind.Soybeans, FieldKind.Hay, FieldKind.Corn, FieldKind.Pasture, FieldKind.Wheat, FieldKind.Soybeans, FieldKind.Plowed, FieldKind.Hay, FieldKind.Corn };

        /// <summary>Rect r minus every cut (axis-aligned pieces).</summary>
        static List<Rect> Subtract(Rect r, List<Rect> cuts)
        {
            var cur = new List<Rect> { r };
            foreach (var cut in cuts)
            {
                var next = new List<Rect>();
                foreach (var a in cur)
                {
                    if (!a.Overlaps(cut)) { next.Add(a); continue; }
                    if (cut.yMin > a.yMin) next.Add(Rect.MinMaxRect(a.xMin, a.yMin, a.xMax, cut.yMin));
                    if (cut.yMax < a.yMax) next.Add(Rect.MinMaxRect(a.xMin, cut.yMax, a.xMax, a.yMax));
                    float y0 = Mathf.Max(a.yMin, cut.yMin), y1 = Mathf.Min(a.yMax, cut.yMax);
                    if (cut.xMin > a.xMin) next.Add(Rect.MinMaxRect(a.xMin, y0, cut.xMin, y1));
                    if (cut.xMax < a.xMax) next.Add(Rect.MinMaxRect(cut.xMax, y0, a.xMax, y1));
                }
                cur = next;
            }
            cur.RemoveAll(x => x.width < 1f || x.height < 1f);
            return cur;
        }

        public static void Build()
        {
            if (!MapWorld.Exists) return;
            // the countryside never changes: built once, hidden with the town and shown again on the next build (no rebuild, nothing to leak)
            if (Root != null) { Root.SetActive(true); return; }
            float t0 = Time.realtimeSinceStartup;
            Fields = 0; TreeBlobs = 0; Vertices = 0;
            Root = new GameObject("FarRing");
            Object.DontDestroyOnLoad(Root);
            var bd = MapLayout.Bounds;
            var hills = new List<Rect>();
            foreach (var a in MapLayout.Areas) if (a.Kind == AreaKind.Hill) hills.Add(a.R);
            var rng = new System.Random(9001);

            // pieces by direction so far-off quadrants frustum-cull
            var parts = new Dictionary<int, (LandModel ground, LandModel veg)>();
            (LandModel ground, LandModel veg) Part(Vector3 p)
            {
                int key = (p.x < bd.xMin ? 0 : p.x > bd.xMax ? 2 : 1) + 3 * (p.z < bd.yMin ? 0 : p.z > bd.yMax ? 2 : 1);
                if (Mathf.Abs(p.x - bd.center.x) > 3500f || Mathf.Abs(p.z - bd.center.y) > 3500f) key += 10;
                if (!parts.TryGetValue(key, out var v)) parts[key] = v = (new LandModel(), new LandModel());
                return v;
            }

            void FieldQuad(Rect r, float y, FieldKind kind, bool alongX)
            {
                var k = Part(new Vector3(r.center.x, 0, r.center.y)).ground;
                k.B.AddQuad(new Vector3(r.xMin, y, r.yMin), new Vector3(r.xMin, y, r.yMax), new Vector3(r.xMax, y, r.yMax), new Vector3(r.xMax, y, r.yMin),
                    k.M(MapMaterials.Field(kind)), Vector3.up, alongX ? Vector3.right : Vector3.forward);
                Fields++;
            }

            // a tree-line blob: a soft crown (lathe), sized like a hedgerow tree seen from far away
            void Blob(Vector3 p, float h, float r, Color tint)
            {
                var k = Part(p).veg;
                var prof = new[] { new Vector2(0.01f, h * 0.25f), new Vector2(r, h * 0.45f), new Vector2(r * 0.8f, h * 0.8f), new Vector2(0.01f, h) };
                k.B.AddLathe(p, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), prof, 6, k.M(LandSurface.Foliage, tint), 70f);

                TreeBlobs++;
            }
            Color[] tints = { new Color(0.72f, 0.86f, 0.60f), new Color(0.58f, 0.72f, 0.50f), new Color(0.80f, 0.92f, 0.66f), new Color(0.50f, 0.66f, 0.52f) };
            void Hedge(Vector3 a, Vector3 b, float spacing)
            {
                float len = Vector3.Distance(a, b);
                for (float s = rng.Next(4); s < len; s += spacing * (0.7f + (float)rng.NextDouble() * 0.6f))
                {
                    if (rng.NextDouble() < 0.18) continue;   // gaps in the line
                    var p = Vector3.Lerp(a, b, s / len) + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 3f;
                    bool inHill = false; foreach (var h in hills) if (h.Contains(new Vector2(p.x, p.z))) { inHill = true; break; }
                    if (inHill || bd.Contains(new Vector2(p.x, p.z))) continue;
                    Blob(p, 9f + (float)rng.NextDouble() * 8f, 4f + (float)rng.NextDouble() * 3f, tints[rng.Next(tints.Length)]);
                }
            }

            // grid of fields: 200 m near the map, growing to ~700 m toward the horizon
            var cx = bd.center.x; var cz = bd.center.y;
            float lim = Outer;
            var xs = new List<float>(); var zs = new List<float>();
            void Lines(List<float> list, float c, float lo, float hi)
            {
                for (float x = lo; x < hi - 50f; x += 180f + Mathf.Clamp01((Mathf.Abs(x - c) - 1200f) / 4000f) * 520f + (float)rng.NextDouble() * 60f) list.Add(x);
                list.Add(hi);
            }
            Lines(xs, cx, cx - lim, cx + lim); Lines(zs, cz, cz - lim, cz + lim);
            xs.Sort(); zs.Sort();
            var skirt = Rect.MinMaxRect(-SkirtHalf, -SkirtHalf, SkirtHalf, SkirtHalf);
            var cuts = new List<Rect>(hills) { bd };
            for (int i = 0; i < xs.Count - 1; i++)
                for (int j = 0; j < zs.Count - 1; j++)
                {
                    var cell = Rect.MinMaxRect(xs[i], zs[j], xs[i + 1], zs[j + 1]);
                    if (cell.width < 5f || cell.height < 5f) continue;
                    if (bd.Contains(cell.min) && bd.Contains(cell.max)) continue;
                    int s = rng.Next(1000);
                    var kind = Mix[s % Mix.Length];
                    bool alongX = s % 2 == 0;
                    float dist = Mathf.Max(Mathf.Abs(cell.center.x - cx), Mathf.Abs(cell.center.y - cz));
                    bool woods = s % 13 == 4 && dist < 5000f;
                    // inside the grass skirt the fields sit just above it (like the map's own fields); outside they are the ground
                    foreach (var piece in Subtract(cell, cuts))
                    {
                        // split the piece where it crosses the skirt edge so each part gets the right height
                        foreach (var sub in Subtract(piece, new List<Rect> { skirt })) FieldQuad(Inset(sub, 0f), 0f, woods ? FieldKind.Pasture : kind, alongX);
                        var inner = Rect.MinMaxRect(Mathf.Max(piece.xMin, skirt.xMin), Mathf.Max(piece.yMin, skirt.yMin), Mathf.Min(piece.xMax, skirt.xMax), Mathf.Min(piece.yMax, skirt.yMax));
                        if (inner.width > 1f && inner.height > 1f) FieldQuad(Inset(inner, 4f), 0.025f, woods ? FieldKind.Pasture : kind, alongX);
                    }
                    if (woods)
                    {   // a woodlot: blobs scattered over the cell
                        int n = Mathf.Clamp(Mathf.RoundToInt(cell.width * cell.height / 900f), 8, 140);
                        for (int t = 0; t < n; t++)
                        {
                            var p = new Vector3(Mathf.Lerp(cell.xMin + 6f, cell.xMax - 6f, (float)rng.NextDouble()), 0, Mathf.Lerp(cell.yMin + 6f, cell.yMax - 6f, (float)rng.NextDouble()));
                            bool skip = bd.Contains(new Vector2(p.x, p.z)); foreach (var h in hills) if (h.Contains(new Vector2(p.x, p.z))) skip = true;
                            if (!skip) Blob(p, 12f + (float)rng.NextDouble() * 8f, 5f + (float)rng.NextDouble() * 3f, tints[rng.Next(tints.Length)]);
                        }
                    }
                    else if (dist < 4200f)
                    {   // hedgerows along the north and east field edges (denser close in)
                        float spacing = dist < 2000f ? 15f : 30f;
                        if (s % 3 != 0) Hedge(new Vector3(cell.xMin, 0, cell.yMax), new Vector3(cell.xMax, 0, cell.yMax), spacing);
                        if (s % 4 != 1) Hedge(new Vector3(cell.xMax, 0, cell.yMin), new Vector3(cell.xMax, 0, cell.yMax), spacing);
                    }
                    if (!woods && dist < 6000f && s % 17 == 3) Farmstead(Part(new Vector3(cell.center.x, 0, cell.center.y)).veg, cell, rng, bd, hills);
                }

            // highways and county roads carry on to the horizon
            foreach (var r in MapLayout.Roads)
            {
                if (r.Core) continue;
                foreach (int end in new[] { -1, 1 })
                {
                    float along = end < 0 ? r.Min : r.Max;
                    var p = r.At(along);
                    bool atEdge = r.EW ? (end < 0 ? p.x <= bd.xMin + 15f : p.x >= bd.xMax - 15f) : (end < 0 ? p.z <= bd.yMin + 15f : p.z >= bd.yMax - 15f);
                    if (!atEdge) continue;
                    float far = end < 0 ? (r.EW ? cx - lim : cz - lim) : (r.EW ? cx + lim : cz + lim);
                    float a0 = Mathf.Min(along, far), a1 = Mathf.Max(along, far);
                    float hw = Mathf.Max(3.4f, r.Half);
                    var strip = r.EW ? Rect.MinMaxRect(a0, r.Perp - hw, a1, r.Perp + hw) : Rect.MinMaxRect(r.Perp - hw, a0, r.Perp + hw, a1);
                    foreach (var seg in Subtract(strip, hills))
                    {
                        var k = Part(new Vector3(seg.center.x, 0, seg.center.y)).ground;
                        int asphalt = k.M(MapMaterials.Asphalt), shoulder = k.M(MapMaterials.Gravel), line = k.M(MaterialLibrary.Get(new Color(0.85f, 0.7f, 0.15f)));
                        Vector3 u = r.EW ? Vector3.right : Vector3.forward;
                        k.B.AddQuad(new Vector3(seg.xMin, 0.05f, seg.yMin), new Vector3(seg.xMin, 0.05f, seg.yMax), new Vector3(seg.xMax, 0.05f, seg.yMax), new Vector3(seg.xMax, 0.05f, seg.yMin), asphalt, Vector3.up, u);
                        // gravel shoulders and a yellow centre line
                        var sh = r.EW ? new Vector3(0, 0, hw + 1.2f) : new Vector3(hw + 1.2f, 0, 0);
                        var c0 = new Vector3(seg.center.x, 0.045f, seg.center.y);
                        var len = r.EW ? new Vector3(seg.width, 0.01f, 2.4f) : new Vector3(2.4f, 0.01f, seg.height);
                        k.B.AddBox(c0 + sh, len, Quaternion.identity, shoulder, u); k.B.AddBox(c0 - sh, len, Quaternion.identity, shoulder, u);
                        var cl = r.EW ? new Vector3(seg.width, 0.004f, 0.25f) : new Vector3(0.25f, 0.004f, seg.height);
                        k.B.AddBox(new Vector3(seg.center.x, 0.052f, seg.center.y), cl, Quaternion.identity, line, u);
                    }
                }
            }

            foreach (var kv in parts)
            {
                var (g, v) = kv.Value;
                if (g.B.VertexCount > 0)
                {
                    var go = g.Make(Root.transform, "Fields " + kv.Key);
                    var mr = go.GetComponent<MeshRenderer>(); mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                    Vertices += g.B.VertexCount;
                }
                if (v.B.VertexCount > 0)
                {
                    var go = v.Make(Root.transform, "Trees " + kv.Key);
                    var mr = go.GetComponent<MeshRenderer>(); mr.shadowCastingMode = ShadowCastingMode.Off;
                    Vertices += v.B.VertexCount;
                }
            }
            Debug.Log($"[Map] far ring: {Fields} field quads, {TreeBlobs} tree-line blobs, {Vertices:N0} vertices to {Outer:0} m in {Time.realtimeSinceStartup - t0:0.00} s");
        }

        static Rect Inset(Rect r, float m) => r.width > 2 * m + 1f && r.height > 2 * m + 1f ? Rect.MinMaxRect(r.xMin + m, r.yMin + m, r.xMax - m, r.yMax - m) : r;

        /// <summary>Distant farmstead: red gambrel-ish barn, white two-storey house, a silo and a machine shed (boxes and a cylinder, real sizes).</summary>
        static void Farmstead(LandModel k, Rect cell, System.Random rng, Rect bd, List<Rect> hills)
        {
            var c = new Vector3(Mathf.Lerp(cell.xMin + 40f, cell.xMax - 40f, (float)rng.NextDouble()), 0, Mathf.Lerp(cell.yMin + 40f, cell.yMax - 40f, (float)rng.NextDouble()));
            foreach (var h in hills) if (h.Contains(new Vector2(c.x, c.z))) return;
            if (bd.Contains(new Vector2(c.x, c.z))) return;
            var rot = Quaternion.Euler(0, rng.Next(4) * 90f, 0);
            int red = k.M(MapMaterials.MetalPanel(new Color(0.62f, 0.14f, 0.10f))), roof = k.M(MapMaterials.Shingle(new Color(0.55f, 0.55f, 0.55f))),
                white = k.M(SurfaceKind.PaintedWhite), steel = k.M(MapMaterials.MetalPanel(new Color(0.8f, 0.8f, 0.78f))), yard = k.M(MapMaterials.Gravel);
            k.B.AddBox(c + Vector3.up * 0.04f, new Vector3(60f, 0.08f, 50f), rot, yard);
            k.B.AddBox(c + rot * new Vector3(-12f, 3.5f, 6f), new Vector3(12f, 7f, 22f), rot, red, rot * Vector3.forward);
            k.B.AddBox(c + rot * new Vector3(-12f, 8.2f, 6f), new Vector3(9f, 2.4f, 22.4f), rot, roof, rot * Vector3.forward);
            k.B.AddBox(c + rot * new Vector3(14f, 3.2f, -12f), new Vector3(9f, 6.4f, 8f), rot, white);
            k.B.AddBox(c + rot * new Vector3(14f, 7.0f, -12f), new Vector3(9.4f, 1.6f, 6f), rot, roof);
            k.B.AddCylinder(c + rot * new Vector3(-2f, 0f, 14f), 2.4f, 16f, 10, steel);
            k.B.AddBox(c + rot * new Vector3(10f, 3f, 14f), new Vector3(18f, 6f, 12f), rot, steel);
            for (int i = 0; i < 4; i++)
            {
                var tp = c + rot * new Vector3(24f + (float)rng.NextDouble() * 6f, 0, -20f + i * 9f);
                var prof = new[] { new Vector2(0.01f, 3f), new Vector2(5f, 6f), new Vector2(4f, 11f), new Vector2(0.01f, 14f) };
                k.B.AddLathe(tp, Quaternion.identity, prof, 6, k.M(LandSurface.Foliage, new Color(0.62f, 0.78f, 0.52f)), 70f);
            }
        }
    }

    /// <summary>
    /// Cameras high above the town (aerial views, screenshots, any fly-over) render out to the far ring: the far clip plane goes out to
    /// ~24 km (and the near plane to 0.3 m, which keeps depth precision) only while the camera is above <see cref="Altitude"/>, and is put
    /// back right after the camera renders. Ground-level cameras are never touched.
    /// </summary>
    public static class FarRingCamera
    {
        public static float Altitude = 60f, Far = 24000f;
        static readonly Dictionary<Camera, (float near, float far)> _saved = new Dictionary<Camera, (float, float)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            Camera.onPreCull -= Pre; Camera.onPreCull += Pre;
            Camera.onPostRender -= Post; Camera.onPostRender += Post;
        }

        static void Pre(Camera c)
        {
            if (c == null || c.orthographic || FarRing.Root == null || !FarRing.Root.activeInHierarchy || c.transform.position.y < Altitude || c.farClipPlane >= Far) return;
            _saved[c] = (c.nearClipPlane, c.farClipPlane);
            c.farClipPlane = Far;
            c.nearClipPlane = Mathf.Max(c.nearClipPlane, Mathf.Clamp(c.transform.position.y * 0.01f, 0.3f, 5f));
        }

        static void Post(Camera c)
        {
            if (c == null || !_saved.TryGetValue(c, out var s)) return;
            _saved.Remove(c);
            c.nearClipPlane = s.near; c.farClipPlane = s.far;
        }
    }
}
