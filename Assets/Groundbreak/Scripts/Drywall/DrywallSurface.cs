using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public class DrywallSheet
    {
        public DrywallProduct P;
        public Rect R;                     // u,v on the surface's FramePlane
        public int Row;
        public bool TaperLo, TaperHi;      // factory tapered long edges at R.yMin / R.yMax (sheets hang long along U)
        public bool FilledLo, FilledHi;    // taper recess filled with tape + mud
        public readonly List<Rect> Holes = new List<Rect>();     // openings + cut-outs
        public readonly List<Rect> Covered = new List<Rect>();   // boxes covered, not cut out yet
        public GameObject Go;
        public DrywallSurface S;
        public bool InHole(Vector2 uv) { foreach (var h in Holes) if (h.Contains(uv)) return true; return false; }
        public bool Covers(Vector2 uv) => R.Contains(uv) && !InHole(uv);
        public const float TaperW = 0.0635f, TaperD = 0.0018f; // 2-1/2" wide, ~1/16" deep factory taper
        /// <summary>Front-face depth offset (into the wall) from the factory taper.</summary>
        public float Taper(float v)
        {
            float t = 0f;
            if (TaperLo && !FilledLo) t = Mathf.Max(t, 1f - Mathf.Clamp01((v - R.yMin) / TaperW));
            if (TaperHi && !FilledHi) t = Mathf.Max(t, 1f - Mathf.Clamp01((R.yMax - v) / TaperW));
            return t * TaperD;
        }
    }

    public class DrywallScrew { public Vector2 P; public DrywallSheet Sheet; public int Spots; public float SpotTime = -999f; public bool Sanded; }

    public enum SeamKind { Tapered, Butt, InsideCorner, OutsideCorner }

    public class SeamSeg { public float A, B; public int Coats; public float CoatTime = -999f; public string Compound = "dw-mud-ap"; public bool Mesh, Sanded; }

    /// <summary>A joint: flat (tapered / butt) between two sheets, or a corner where another drywall face meets this one.</summary>
    public class Seam
    {
        public SeamKind Kind;
        public Vector2 P0, P1;            // endpoints (u,v), axis-aligned
        public Vector2 Side;              // corners: unit direction of the band on this face; flat joints: zero (both sides)
        public bool Beaded;
        public string Key;
        public readonly List<SeamSeg> Segs = new List<SeamSeg>();
        public bool Vertical => Mathf.Abs(P1.x - P0.x) < 1e-4f;
        public float Length => (P1 - P0).magnitude;
        public Vector2 At(float t) => Vector2.Lerp(P0, P1, t / Mathf.Max(1e-5f, Length));
        public bool IsCorner => Kind == SeamKind.InsideCorner || Kind == SeamKind.OutsideCorner;
        public float WidthIn(int coat)
        {
            int c = Mathf.Clamp(coat - 1, 0, 2);
            switch (Kind)
            {
                case SeamKind.Butt: return DrywallRules.ButtWidth[c];
                case SeamKind.InsideCorner: return DrywallRules.CornerSide[c];
                case SeamKind.OutsideCorner: return DrywallRules.BeadSide[c];
                default: return DrywallRules.FlatWidth[c];
            }
        }
    }

    /// <summary>
    /// All drywall on one framed face (a wall or a ceiling): sheets (each its own mesh with tapered edges, gypsum core
    /// on cut edges, brown back paper), screws, joints, and a per-surface composite "skin" (albedo + smoothness, normal,
    /// detail mask) at 1/2" per texel that shows tape, wet/dry mud, sanding, texture, primer and paint exactly where
    /// they were applied. Paper fibers / orange peel / knockdown come from a tiling detail map in meters (UV1).
    /// Rendered opaque, so finished walls still receive shadows.
    /// </summary>
    public class DrywallSurface : MonoBehaviour
    {
        public FramePlane Fp;
        public readonly List<DrywallSheet> Sheets = new List<DrywallSheet>();
        public readonly List<DrywallScrew> Screws = new List<DrywallScrew>();
        public readonly List<Seam> Seams = new List<Seam>();
        public string TextureKind;          // null, "orange-peel", "knockdown", "smooth"
        public float TextureTime = -999f;
        public Rect Area;
        public const float T = 0.0127f;     // 1/2" texel
        public int W, H;
        public bool IsCeiling => Fp.IsCeiling;
        public float Thickness { get { float t = 0; foreach (var s in Sheets) t = Mathf.Max(t, s.P.Thick); return t; } }

        // per-texel layers
        public int[] SheetIdx;
        public float[] EdgeDist;            // meters to nearest edge / inside corner / hole (roller can't reach < 2")
        public bool[] Hidden;               // behind another drywall face (beyond an inside corner)
        public float[] Textured, Primer, PrimerTime, Paint, PaintTime, Sheen;
        public Color[] PaintColor, PrevColor;
        float[] _noise;

        Texture2D _alb, _nrm, _mask;
        Material _mat;
        GameObject _screwGo;
        bool _dirtySkin, _dirtyGeo;
        string _wetSig = "";
        float _nextCheck;

        public void Init(FramePlane fp)
        {
            Fp = fp;
            Area = fp.Bounds;
            W = Mathf.Max(2, Mathf.CeilToInt(Area.width / T)); H = Mathf.Max(2, Mathf.CeilToInt(Area.height / T));
            int n = W * H;
            SheetIdx = new int[n]; EdgeDist = new float[n]; Hidden = new bool[n];
            Textured = new float[n]; Primer = new float[n]; PrimerTime = new float[n]; Paint = new float[n]; PaintTime = new float[n]; Sheen = new float[n];
            PaintColor = new Color[n]; PrevColor = new Color[n];
            for (int i = 0; i < n; i++) { PrimerTime[i] = PaintTime[i] = -999f; SheetIdx[i] = -1; }
            _noise = new float[n];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x * T, v = y * T;
                    _noise[y * W + x] = (Mathf.PerlinNoise(u * 5.5f + 3.1f, v * 1.2f) - 0.5f) * 1.4f + (Mathf.PerlinNoise(u * 1.3f, v * 2.1f + 9f) - 0.5f) * 0.8f;
                }
            _alb = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "DW_Skin_A" };
            _nrm = new Texture2D(W, H, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "DW_Skin_N" };
            _mask = new Texture2D(W, H, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "DW_Skin_M" };
            _mat = new Material(Shader.Find("Standard")) { name = "DW_Surface" };
            _mat.mainTexture = _alb;
            _mat.SetFloat("_SmoothnessTextureChannel", 1f);
            _mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            _mat.SetFloat("_GlossMapScale", 1f);
            _mat.SetFloat("_Glossiness", 1f);
            _mat.SetTexture("_BumpMap", _nrm); _mat.SetFloat("_BumpScale", 1f); _mat.EnableKeyword("_NORMALMAP");
            _mat.SetTexture("_DetailMask", _mask);
            _mat.SetFloat("_UVSec", 1f);
            _mat.EnableKeyword("_DETAIL_MULX2");
            SetDetail(DrywallLooks.PaperDetail);
            _dirtyGeo = true; _dirtySkin = true;
        }

        void SetDetail(string key)
        {
            FinishMaterials.Textures(key, out var a, out var nm);
            _mat.SetTexture("_DetailAlbedoMap", a);
            _mat.SetTexture("_DetailNormalMap", nm);
            _mat.SetFloat("_DetailNormalMapScale", key == DrywallLooks.PaperDetail ? 0.3f : 0.8f);
            _mat.SetTextureScale("_DetailAlbedoMap", Vector2.one / FinishMaterials.Tile(key));
        }

        public Material SkinMaterial => _mat;

        // ------------------------------------------------------------------ texel helpers

        public Vector2 TexelUV(int x, int y) => new Vector2(Area.xMin + (x + 0.5f) * T, Area.yMin + (y + 0.5f) * T);
        public bool ToTexel(Vector2 uv, out int x, out int y)
        {
            x = Mathf.FloorToInt((uv.x - Area.xMin) / T); y = Mathf.FloorToInt((uv.y - Area.yMin) / T);
            return x >= 0 && y >= 0 && x < W && y < H;
        }
        public void TexelRange(Rect r, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((r.xMin - Area.xMin) / T), 0, W - 1); x1 = Mathf.Clamp(Mathf.CeilToInt((r.xMax - Area.xMin) / T), 0, W - 1);
            y0 = Mathf.Clamp(Mathf.FloorToInt((r.yMin - Area.yMin) / T), 0, H - 1); y1 = Mathf.Clamp(Mathf.CeilToInt((r.yMax - Area.yMin) / T), 0, H - 1);
        }
        public bool Visible(int i) => SheetIdx[i] >= 0 && !Hidden[i];

        public DrywallSheet SheetAt(Vector2 uv) { foreach (var s in Sheets) if (s.Covers(uv)) return s; return null; }

        /// <summary>Front-face position (world) at uv including the taper.</summary>
        public Vector3 FrontWorld(Vector2 uv, float extraProud = 0f)
        {
            var s = SheetAt(uv);
            float t = s != null ? s.P.Thick - s.Taper(uv.y) : Thickness;
            return Fp.World(uv, t + extraProud);
        }

        // ------------------------------------------------------------------ sheets

        public void AddSheet(DrywallSheet sh)
        {
            sh.S = this;
            Sheets.Add(sh);
            BuildSheetMesh(sh);
            BuildShadowSlab();
            Changed(true);
        }

        GameObject _shadow;
        /// <summary>Shadow-only slab just behind the board (into the framing): stops the sun from leaking through the
        /// thin sheets at wall/ceiling junctions because of shadow bias.</summary>
        void BuildShadowSlab()
        {
            var b = new MeshBuilder();
            foreach (var sh in Sheets)
            {
                var r = Rect.MinMaxRect(sh.R.xMin - 0.02f, sh.R.yMin - 0.02f, sh.R.xMax + 0.02f, sh.R.yMax + 0.02f);
                b.AddBox(new Vector3(r.center.x, r.center.y, 0.025f), new Vector3(r.width, r.height, 0.04f), 0);
            }
            if (_shadow == null)
            {
                _shadow = new GameObject("ShadowSlab");
                _shadow.transform.SetParent(transform, false);
                _shadow.transform.SetPositionAndRotation(Fp.Origin, Fp.Rotation);
                _shadow.AddComponent<MeshFilter>();
                var mr = _shadow.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                mr.sharedMaterial = FinishMaterials.Get(DrywallLooks.Back);
            }
            var mf = _shadow.GetComponent<MeshFilter>(); var old = mf.sharedMesh; mf.sharedMesh = b.Build("ShadowSlab"); if (old != null) Destroy(old);
        }

        public void Changed(bool geometry)
        {
            if (geometry) { _dirtyGeo = true; foreach (var o in Drywall.Surfaces) if (o != null && o != this) o._dirtyGeo = true; }
            _dirtySkin = true;
        }

        public void BuildSheetMesh(DrywallSheet sh)
        {
            var b = new MeshBuilder();
            float t = sh.P.Thick;
            // back + edges via PanelMesh (front goes to a throw-away submesh 3), then our tapered front in submesh 0
            PanelMesh.AddSlab(b, sh.R, sh.Holes, -t, t, 3, 1, 2);
            foreach (var c in PanelMesh.Cells(sh.R, sh.Holes))
            {
                var ys = new List<float> { c.yMin, c.yMax };
                if (sh.TaperLo && sh.R.yMin + DrywallSheet.TaperW > c.yMin && sh.R.yMin + DrywallSheet.TaperW < c.yMax) ys.Insert(1, sh.R.yMin + DrywallSheet.TaperW);
                if (sh.TaperHi && sh.R.yMax - DrywallSheet.TaperW > c.yMin && sh.R.yMax - DrywallSheet.TaperW < c.yMax) ys.Insert(ys.Count - 1, sh.R.yMax - DrywallSheet.TaperW);
                ys.Sort();
                for (int k = 0; k + 1 < ys.Count; k++)
                {
                    float y0 = ys[k], y1 = ys[k + 1];
                    float z0 = -t + sh.Taper(y0), z1 = -t + sh.Taper(y1);
                    b.AddQuad(new Vector3(c.xMin, y0, z0), new Vector3(c.xMax, y0, z0), new Vector3(c.xMax, y1, z1), new Vector3(c.xMin, y1, z1), 0, Vector3.back);
                }
            }
            b.MinSubmeshCount = 4;
            var mesh = b.Build("DrywallSheet");
            // UV0 for the front = normalized surface coords (composite skin); UV1 = meters (detail maps)
            var verts = mesh.vertices; var uv0 = mesh.uv; var uv1 = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++) uv1[i] = new Vector2(verts[i].x, verts[i].y);
            foreach (int i in mesh.GetTriangles(0)) uv0[i] = new Vector2((verts[i].x - Area.xMin) / Area.width, (verts[i].y - Area.yMin) / Area.height);
            mesh.uv = uv0; mesh.uv2 = uv1;
            mesh.subMeshCount = 3;
            mesh.RecalculateTangents();
            var mats = new[] { _mat, FinishMaterials.Get(DrywallLooks.Back), FinishMaterials.Get(DrywallLooks.Core) };
            if (sh.Go == null)
            {
                sh.Go = PanelMesh.Spawn(Fp, "Drywall_" + sh.P.Id, mesh, mats, transform, true, "drywall");
                sh.Go.GetComponent<FinishLayer>().Owner = sh;
            }
            else PanelMesh.Replace(sh.Go, mesh, mats);
        }

        // ------------------------------------------------------------------ screws

        public void RebuildScrews()
        {
            var b = new MeshBuilder();
            foreach (var s in Screws)
            {
                if (s.Spots > 0) continue;
                float t = s.Sheet.P.Thick - s.Sheet.Taper(s.P.y);
                // head set in a dimple just below the paper: drawn as the dark head + a shadowed dimple ring on the face
                var c = new Vector3(s.P.x, s.P.y, -t - 0.00025f);
                b.AddDisc(c, Quaternion.Euler(-90f, 0f, 0f), Units.In(0.16f), 10, 0, true);
                b.AddRing(new Vector3(s.P.x, s.P.y, -t - 0.00012f), Quaternion.Euler(-90f, 0f, 0f), Units.In(0.3f), Units.In(0.16f), 0.00005f, 12, 1);
            }
            b.MinSubmeshCount = 2;
            var mesh = b.Build("DrywallScrews");
            var mats = new[] { FinishMaterials.Get(DrywallLooks.Screw), FinishMaterials.Get(DrywallLooks.Back, new Color(1.25f, 1.28f, 1.32f)) };
            if (_screwGo == null)
            {
                _screwGo = new GameObject("Screws");
                _screwGo.transform.SetParent(transform, false);
                _screwGo.transform.SetPositionAndRotation(Fp.Origin, Fp.Rotation);
                _screwGo.AddComponent<MeshFilter>(); _screwGo.AddComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _screwGo.AddComponent<FinishLayer>().Kind = "screws";
            }
            var mf = _screwGo.GetComponent<MeshFilter>(); var old = mf.sharedMesh; mf.sharedMesh = mesh; if (old != null) Destroy(old);
            _screwGo.GetComponent<MeshRenderer>().sharedMaterials = mats;
        }

        // ------------------------------------------------------------------ seams

        static void SubtractHoles(List<Vector2> spans, IEnumerable<Rect> holes, bool vertical, float line)
        {
            foreach (var h in holes)
            {
                float lo = vertical ? h.yMin : h.xMin, hi = vertical ? h.yMax : h.xMax, a = vertical ? h.xMin : h.yMin, b = vertical ? h.xMax : h.yMax;
                if (line < a - 0.002f || line > b + 0.002f) continue;
                var next = new List<Vector2>();
                foreach (var s in spans)
                {
                    if (hi <= s.x || lo >= s.y) { next.Add(s); continue; }
                    if (lo > s.x) next.Add(new Vector2(s.x, lo));
                    if (hi < s.y) next.Add(new Vector2(hi, s.y));
                }
                spans.Clear(); spans.AddRange(next);
            }
        }

        /// <summary>Recomputes joints from sheet edges and neighbouring drywall faces; keeps finishing progress of
        /// joints that still exist (matched by key).</summary>
        public void RecomputeSeams()
        {
            var old = new Dictionary<string, Seam>();
            foreach (var s in Seams) old[s.Key] = s;
            Seams.Clear();
            const float eps = 0.004f;
            for (int i = 0; i < Sheets.Count; i++)
                for (int j = 0; j < Sheets.Count; j++)
                {
                    if (i == j) continue;
                    var A = Sheets[i]; var B = Sheets[j];
                    var holes = new List<Rect>(A.Holes); holes.AddRange(B.Holes);
                    if (Mathf.Abs(A.R.xMax - B.R.xMin) < eps)            // vertical joint: A left of B → butt (ends)
                    {
                        float lo = Mathf.Max(A.R.yMin, B.R.yMin), hi = Mathf.Min(A.R.yMax, B.R.yMax);
                        if (hi - lo < 0.02f) continue;
                        var spans = new List<Vector2> { new Vector2(lo, hi) };
                        SubtractHoles(spans, holes, true, A.R.xMax);
                        foreach (var sp in spans) if (sp.y - sp.x > 0.02f) AddSeam(old, SeamKind.Butt, new Vector2(A.R.xMax, sp.x), new Vector2(A.R.xMax, sp.y), Vector2.zero);
                    }
                    if (Mathf.Abs(A.R.yMax - B.R.yMin) < eps)            // horizontal joint: A below B
                    {
                        float lo = Mathf.Max(A.R.xMin, B.R.xMin), hi = Mathf.Min(A.R.xMax, B.R.xMax);
                        if (hi - lo < 0.02f) continue;
                        var spans = new List<Vector2> { new Vector2(lo, hi) };
                        SubtractHoles(spans, holes, false, A.R.yMax);
                        var kind = A.TaperHi && B.TaperLo ? SeamKind.Tapered : SeamKind.Butt;
                        foreach (var sp in spans) if (sp.y - sp.x > 0.02f) AddSeam(old, kind, new Vector2(sp.x, A.R.yMax), new Vector2(sp.y, A.R.yMax), Vector2.zero);
                    }
                }
            foreach (var c in Drywall.CornersOn(this))
                AddSeam(old, c.Kind, c.P0, c.P1, c.Side);
        }

        void AddSeam(Dictionary<string, Seam> old, SeamKind kind, Vector2 a, Vector2 b, Vector2 side)
        {
            string key = $"{kind}:{a.x:F3},{a.y:F3}-{b.x:F3},{b.y:F3}";
            if (old.TryGetValue(key, out var o)) { Seams.Add(o); return; }
            var s = new Seam { Kind = kind, P0 = a, P1 = b, Side = side, Key = key };
            int n = Mathf.Max(1, Mathf.CeilToInt(s.Length / Units.In(48f)));
            for (int k = 0; k < n; k++) s.Segs.Add(new SeamSeg { A = s.Length * k / n, B = s.Length * (k + 1) / n });
            Seams.Add(s);
        }

        // ------------------------------------------------------------------ geometry maps (sheet index, edges, hidden)

        void RebuildGeoMaps()
        {
            int n = W * H;
            for (int i = 0; i < n; i++) { SheetIdx[i] = -1; Hidden[i] = false; }
            for (int k = 0; k < Sheets.Count; k++)
            {
                var sh = Sheets[k];
                TexelRange(sh.R, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (sh.Covers(TexelUV(x, y))) SheetIdx[y * W + x] = k;
            }
            RecomputeSeams();
            // hidden: beyond inside corners (behind the other face)
            var src = new bool[n];
            foreach (var s in Seams)
            {
                if (s.Kind != SeamKind.InsideCorner) continue;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        var p = TexelUV(x, y);
                        float along = s.Vertical ? p.y : p.x, lo = s.Vertical ? Mathf.Min(s.P0.y, s.P1.y) : Mathf.Min(s.P0.x, s.P1.x), hi = s.Vertical ? Mathf.Max(s.P0.y, s.P1.y) : Mathf.Max(s.P0.x, s.P1.x);
                        if (along < lo - T || along > hi + T) continue;
                        float d = Vector2.Dot(p - s.P0, s.Side);
                        if (d < 0f) Hidden[y * W + x] = true;
                        if (Mathf.Abs(d) < T * 0.75f) src[y * W + x] = true;
                    }
            }
            // distance transform (chamfer 3-4) from edges: uncovered / hidden texels and corner lines
            const float BIG = 1e6f;
            var D = new float[n];
            for (int i = 0; i < n; i++) D[i] = (!Visible(i) || src[i]) ? 0f : BIG;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x; float d = D[i];
                    if (x > 0) d = Mathf.Min(d, D[i - 1] + 1f);
                    if (y > 0) d = Mathf.Min(d, D[i - W] + 1f);
                    if (x > 0 && y > 0) d = Mathf.Min(d, D[i - W - 1] + 1.414f);
                    if (x < W - 1 && y > 0) d = Mathf.Min(d, D[i - W + 1] + 1.414f);
                    if (x == 0 || y == 0 || x == W - 1 || y == H - 1) d = Mathf.Min(d, 1f);
                    D[i] = d;
                }
            for (int y = H - 1; y >= 0; y--)
                for (int x = W - 1; x >= 0; x--)
                {
                    int i = y * W + x; float d = D[i];
                    if (x < W - 1) d = Mathf.Min(d, D[i + 1] + 1f);
                    if (y < H - 1) d = Mathf.Min(d, D[i + W] + 1f);
                    if (x < W - 1 && y < H - 1) d = Mathf.Min(d, D[i + W + 1] + 1.414f);
                    if (x > 0 && y < H - 1) d = Mathf.Min(d, D[i + W - 1] + 1.414f);
                    D[i] = d;
                }
            for (int i = 0; i < n; i++) EdgeDist[i] = D[i] * T;
            RebuildScrews();
        }

        // ------------------------------------------------------------------ wet / dry

        public static float Now => FinishTime.Now;
        public static bool SegWet(SeamSeg g) => g.Coats > 0 && Now - g.CoatTime < DrywallRules.Compound(g.Compound).DryHours;
        public static float SegDryLeft(SeamSeg g) => Mathf.Max(0f, DrywallRules.Compound(g.Compound).DryHours - (Now - g.CoatTime));
        public static bool ScrewWet(DrywallScrew s) => s.Spots > 0 && Now - s.SpotTime < DrywallRules.Compound("dw-mud-ap").DryHours;

        string WetSignature()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in Seams) foreach (var g in s.Segs) sb.Append(SegWet(g) ? '1' : '0');
            foreach (var s in Screws) sb.Append(ScrewWet(s) ? '1' : '0');
            sb.Append(TextureKind != null && Now - TextureTime < DrywallRules.TextureDryHours ? 'T' : 't');
            bool pw = false; for (int i = 0; i < PaintTime.Length && !pw; i += 7) pw = Now - PaintTime[i] < PaintRules.DryHours || Now - PrimerTime[i] < PaintRules.DryHours;
            sb.Append(pw ? 'P' : 'p');
            return sb.ToString();
        }

        void LateUpdate()
        {
            if (Time.unscaledTime > _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.5f;
                var sig = WetSignature();
                if (sig != _wetSig) { _dirtySkin = true; }
            }
            Apply();
        }

        /// <summary>Applies pending geometry / skin changes now (tests call this before screenshots).</summary>
        public void Refresh() { if (!_dirtySkin && WetSignature() != _wetSig) _dirtySkin = true; Apply(); }

        void Apply()
        {
            if (_dirtyGeo) { _dirtyGeo = false; RebuildGeoMaps(); _dirtySkin = true; }
            if (_dirtySkin) { _dirtySkin = false; _wetSig = WetSignature(); RebuildSkin(); }
        }

        // ------------------------------------------------------------------ the composite skin

        static readonly Color TapeColor = new Color(0.90f, 0.88f, 0.80f), MeshTapeColor = new Color(0.93f, 0.92f, 0.80f);
        static readonly Color DryMud = new Color(0.925f, 0.925f, 0.91f), SandedMud = new Color(0.945f, 0.945f, 0.935f), WetMud = new Color(0.60f, 0.615f, 0.62f);
        public static readonly Color PrimerColor = new Color(0.955f, 0.955f, 0.945f);

        /// <summary>Mud fills the factory taper: once a joint along a tapered edge has a coat, flatten that edge.</summary>
        void FillTapers()
        {
            bool screwsDirty = false;
            foreach (var sh in Sheets)
            {
                bool lo = sh.FilledLo, hi = sh.FilledHi;
                foreach (var s in Seams)
                {
                    if (s.Vertical) continue;
                    bool coated = false; foreach (var g in s.Segs) if (g.Coats > 0) coated = true;
                    if (!coated) continue;
                    if (s.P1.x < sh.R.xMin || s.P0.x > sh.R.xMax) continue;
                    if (sh.TaperLo && Mathf.Abs(s.P0.y - sh.R.yMin) < 0.004f) lo = true;
                    if (sh.TaperHi && Mathf.Abs(s.P0.y - sh.R.yMax) < 0.004f) hi = true;
                }
                if (lo != sh.FilledLo || hi != sh.FilledHi) { sh.FilledLo = lo; sh.FilledHi = hi; BuildSheetMesh(sh); screwsDirty = true; }
            }
            if (screwsDirty) RebuildScrews();
        }

        void RebuildSkin()
        {
            FillTapers();
            int n = W * H;
            var dry = new float[n]; var wet = new float[n]; var hgt = new float[n]; var tape = new float[n]; var meshT = new float[n]; var line = new float[n];
            // joints
            foreach (var s in Seams)
            {
                Vector2 dir = (s.P1 - s.P0).normalized, nrm = new Vector2(-dir.y, dir.x);
                foreach (var g in s.Segs)
                {
                    Vector2 a = s.At(g.A), b = s.At(g.B);
                    float maxW = Units.In(s.WidthIn(Mathf.Max(1, g.Coats))) * (s.IsCorner ? 1f : 0.5f) + T * 2;
                    var bb = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - maxW, Mathf.Min(a.y, b.y) - maxW, Mathf.Max(a.x, b.x) + maxW, Mathf.Max(a.y, b.y) + maxW);
                    TexelRange(bb, out int x0, out int y0, out int x1, out int y1);
                    bool wetTop = SegWet(g);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int i = y * W + x;
                            if (SheetIdx[i] < 0) continue;
                            var p = TexelUV(x, y);
                            float t = Vector2.Dot(p - s.P0, dir);
                            if (t < g.A - T || t > g.B + T) continue;
                            float d = Vector2.Dot(p - s.P0, nrm);
                            if (s.IsCorner) { d = Vector2.Dot(p - s.P0, s.Side); if (d < -T * 0.5f) continue; d = Mathf.Max(0f, d); }
                            else d = Mathf.Abs(d);
                            if (g.Coats == 0) { if (d < T * 0.6f && !s.IsCorner) line[i] = 1f; continue; }
                            float endFade = 1f; // ends of a run feather off along the joint
                            for (int c = 1; c <= g.Coats; c++)
                            {
                                float half = Units.In(s.WidthIn(c)) * (s.IsCorner ? 1f : 0.5f);
                                float jitter = (Mathf.PerlinNoise(t * 7f + c * 3f, c) - 0.5f) * Units.In(0.6f);
                                float edge = half + jitter;
                                float cov = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - edge * 0.55f) / (edge * 0.45f)));
                                cov *= endFade;
                                if (cov <= 0f) continue;
                                bool isWet = wetTop && c == g.Coats;
                                float streak = g.Sanded ? 1f : 0.82f + 0.18f * Mathf.PerlinNoise(t * 1.5f + c * 11f, d * 160f + c);
                                if (isWet) wet[i] = Mathf.Max(wet[i], cov * (0.88f + 0.12f * streak)); else dry[i] = Mathf.Max(dry[i], cov * streak * (g.Sanded ? 1f : 0.97f));
                                hgt[i] += cov * 0.0006f;
                                if (!g.Sanded && !isWet) { float ridge = Mathf.Exp(-Mathf.Pow((d - edge * 0.98f) / 0.003f, 2f)); hgt[i] += ridge * 0.0005f; }
                                if (isWet) { float ridge = Mathf.Exp(-Mathf.Pow((d - edge) / 0.004f, 2f)); hgt[i] += ridge * 0.0009f; }
                            }
                            float th = Units.In(1f);
                            if (d < th)
                            {
                                float vis = g.Sanded ? 0f : g.Coats == 1 ? (wetTop ? 0.65f : 0.4f) : (wetTop ? 0.0f : 0.12f);
                                if (g.Mesh) meshT[i] = Mathf.Max(meshT[i], vis); else tape[i] = Mathf.Max(tape[i], vis);
                                if (!g.Sanded) hgt[i] += 0.0003f * Mathf.Clamp01((th - d) / T);
                            }
                        }
                }
            }
            // screw spots
            foreach (var sc in Screws)
            {
                if (sc.Spots == 0) continue;
                bool w = ScrewWet(sc);
                float r = Units.In(DrywallRules.SpotDia[Mathf.Clamp(sc.Spots - 1, 0, 2)]) * 0.5f;
                TexelRange(new Rect(sc.P.x - r - T, sc.P.y - r - T, 2 * r + 2 * T, 2 * r + 2 * T), out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = y * W + x; if (SheetIdx[i] < 0) continue;
                        var dd = TexelUV(x, y) - sc.P; dd.x *= 0.8f; // knife wipes stretch the spot sideways
                        float d = dd.magnitude * (0.85f + 0.3f * Mathf.PerlinNoise(sc.P.x * 40f + dd.x * 90f, sc.P.y * 40f + dd.y * 90f));
                        float cov = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - r * 0.45f) / (r * 0.55f)));
                        if (cov <= 0f) continue;
                        if (w) wet[i] = Mathf.Max(wet[i], cov); else dry[i] = Mathf.Max(dry[i], cov);
                        hgt[i] += cov * 0.0005f;
                    }
            }

            bool textured = TextureKind != null;
            bool texWet = textured && Now - TextureTime < DrywallRules.TextureDryHours;
            var A = new Color32[n]; var Nn = new Color32[n]; var M = new Color32[n];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    int si = SheetIdx[i];
                    Color col = si >= 0 ? Sheets[si].P.Face : new Color(0.5f, 0.5f, 0.5f);
                    float sm = 0.2f, mask = 1f;
                    if (line[i] > 0f) col *= 0.93f;
                    col = Color.Lerp(col, TapeColor, tape[i]);
                    col = Color.Lerp(col, MeshTapeColor, meshT[i]);
                    float mud = Mathf.Max(dry[i], wet[i]);
                    col = Color.Lerp(col, dry[i] > 0.99f && SandedLike(i) ? SandedMud : DryMud, dry[i]);
                    sm = Mathf.Lerp(sm, 0.06f, dry[i]);
                    col = Color.Lerp(col, WetMud, wet[i]);
                    sm = Mathf.Lerp(sm, 0.62f, wet[i]);
                    mask = Mathf.Lerp(1f, 0.25f, mud);
                    float h = hgt[i];
                    if (textured && Textured[i] > 0f)
                    {
                        col = Color.Lerp(col, texWet ? WetMud * 1.1f : DryMud, TextureKind == "knockdown" ? 0.9f : 0.97f);
                        sm = texWet ? 0.5f : 0.07f; mask = 1f; mud = 1f;
                    }
                    if (Primer[i] > 0f)
                    {
                        float a = Primer[i] >= 2f ? 1f : Mathf.Clamp01(0.86f + _noise[i] * 0.08f);
                        col = Color.Lerp(col, PrimerColor, a);
                        sm = Now - PrimerTime[i] < PaintRules.DryHours ? 0.45f : 0.1f;
                    }
                    if (Paint[i] > 0f)
                    {
                        Color under = PrevColor[i].a > 0.5f ? PrevColor[i] : col;
                        float a = Paint[i] >= 2f ? 1f : Mathf.Clamp01(0.66f + _noise[i] * 0.22f - mud * 0.08f);
                        Color pc = PaintColor[i];
                        float sheen = Sheen[i];
                        if (Primer[i] <= 0f)
                        {
                            // unprimed: paint soaks into paper differently than mud = "flashing" over joints
                            pc = mud > 0.3f ? pc * 1.05f : pc * 0.96f;
                            sheen = mud > 0.3f ? sheen * 0.45f : sheen * 1.1f;
                        }
                        col = Color.Lerp(under, pc, a);
                        sm = Now - PaintTime[i] < PaintRules.DryHours ? Mathf.Min(0.85f, sheen + 0.3f) : sheen;
                        if (Now - PaintTime[i] < PaintRules.DryHours) col *= 0.95f;
                        mask = Mathf.Max(mask, textured ? 1f : 0.55f);
                    }
                    A[i] = new Color32(B8(col.r), B8(col.g), B8(col.b), B8(sm));
                    M[i] = new Color32(255, 255, 255, B8(mask));
                    hgt[i] = h;
                }
            // normals from height (meters → slope per texel)
            float k = 1f / T * 2.2f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float hl = hgt[y * W + Mathf.Max(0, x - 1)], hr = hgt[y * W + Mathf.Min(W - 1, x + 1)];
                    float hd = hgt[Mathf.Max(0, y - 1) * W + x], hu = hgt[Mathf.Min(H - 1, y + 1) * W + x];
                    float nx = -(hr - hl) * 0.5f * k, ny = -(hu - hd) * 0.5f * k, inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + 1f);
                    Nn[i] = new Color32(B8(nx * inv * 0.5f + 0.5f), B8(ny * inv * 0.5f + 0.5f), B8(inv * 0.5f + 0.5f), 255);
                }
            _alb.SetPixels32(A); _alb.Apply(true);
            _nrm.SetPixels32(Nn); _nrm.Apply(true);
            _mask.SetPixels32(M); _mask.Apply(true);
            SetDetail(textured ? (TextureKind == "knockdown" ? DrywallLooks.Knockdown : TextureKind == "orange-peel" ? DrywallLooks.OrangePeel : DrywallLooks.Smooth) : DrywallLooks.PaperDetail);
            Drywall.UpdateBeads(this);
        }

        bool SandedLike(int i) => true;

        static byte B8(float v) => (byte)(v <= 0f ? 0 : v >= 1f ? 255 : (int)(v * 255f + 0.5f));

        // ------------------------------------------------------------------ texture / primer / paint per texel

        /// <summary>Marks texels in r textured (spray texture). Returns texels changed.</summary>
        public int TextureRect(Rect r)
        {
            TexelRange(r, out int x0, out int y0, out int x1, out int y1); int c = 0;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { int i = y * W + x; if (Visible(i) && Textured[i] < 1f) { Textured[i] = 1f; c++; } }
            if (c > 0) _dirtySkin = true;
            return c;
        }

        /// <summary>One coat of primer (primer=true) or paint over texels of r whose edge distance is within [minEdge, maxEdge].
        /// Wet texels (just coated) don't take another coat. Returns texels coated.</summary>
        public int CoatRect(Rect r, bool primer, Color color, float sheen, float minEdge, float maxEdge)
        {
            TexelRange(r, out int x0, out int y0, out int x1, out int y1); int c = 0;
            float now = Now;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * W + x;
                    if (!Visible(i)) continue;
                    var p = TexelUV(x, y); if (!r.Contains(p)) continue;
                    float e = EdgeDist[i]; if (e < minEdge || e > maxEdge) continue;
                    if (primer)
                    {
                        if (now - PrimerTime[i] < PaintRules.DryHours) continue;
                        Primer[i] = Mathf.Min(2f, Primer[i] + 1f); PrimerTime[i] = now; c++;
                    }
                    else
                    {
                        if (Paint[i] > 0f && !Same(PaintColor[i], color)) { PrevColor[i] = PaintColor[i]; PrevColor[i].a = 1f; Paint[i] = 0f; }
                        if (Paint[i] > 0f && now - PaintTime[i] < PaintRules.DryHours) continue;
                        Paint[i] = Mathf.Min(3f, Paint[i] + 1f); PaintTime[i] = now; PaintColor[i] = color; Sheen[i] = sheen; c++;
                    }
                }
            if (c > 0) _dirtySkin = true;
            return c;
        }

        static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.01f;

        /// <summary>Fraction of visible texels with at least n coats of primer (primer) or paint.</summary>
        public float Coverage(bool primer, int n)
        {
            int tot = 0, ok = 0;
            for (int i = 0; i < W * H; i++) { if (!Visible(i)) continue; tot++; if ((primer ? Primer[i] : Paint[i]) >= n) ok++; }
            return tot == 0 ? 0f : ok / (float)tot;
        }

        public float VisibleSqFt { get { int tot = 0; for (int i = 0; i < W * H; i++) if (Visible(i)) tot++; return tot * T * T / (Units.Foot * Units.Foot); } }

        void OnDestroy() { Drywall.Surfaces.Remove(this); }
    }
}
