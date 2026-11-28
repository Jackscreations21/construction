using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Saves drywall, paint and insulation (save key "drywall"): every <see cref="DrywallSurface"/> (sheets with tapers, holes and
    /// boxes not cut out yet, screws with their mud spots, every joint's coats / compound / mesh tape / sanding by segment,
    /// texture) and its per-texel finish layers (texture, primer, paint coats and colours, sheen, wet times — run-length
    /// encoded), corner bead, fire-rated faces; insulation batts (product, bay, split / stapled / inset), spray foam, poly vapor
    /// retarder, blown attic fill (depth field), eave baffles, attic rulers, air-sealed boxes and fire-blocked penetrations with
    /// their foam / fire-caulk beads. Each face is found again on the restored framing by its plane (<see cref="SaveGaps.FindPlane"/>);
    /// sheets go back without using stock. Loads after "shell" (its framing) and before "finish" (trim / cabinets sit on the board).
    /// </summary>
    public static class DrywallSave
    {
        public const string Key = "drywall";

        [Serializable] class SheetD { public string product; public Rect r; public int row; public bool tLo, tHi, fLo, fHi; public List<Rect> holes = new List<Rect>(), covered = new List<Rect>(); }
        [Serializable] class ScrewD { public Vector2 p; public int sheet, spots; public float spotTime; public bool sanded; }
        [Serializable] class SegD { public float a, b, coatTime; public int coats; public string compound; public bool mesh, sanded; }
        [Serializable] class SeamD { public string key; public bool beaded; public List<SegD> segs = new List<SegD>(); }
        [Serializable] class BeadD { public string seam; public bool paper; }
        [Serializable]
        class SurfD
        {
            public SaveGaps.PlaneRef plane = new SaveGaps.PlaneRef(); public Rect area; public int w, h;
            public List<SheetD> sheets = new List<SheetD>(); public List<ScrewD> screws = new List<ScrewD>(); public List<SeamD> seams = new List<SeamD>();
            public List<BeadD> beads = new List<BeadD>();
            public bool hasTexture; public string texture; public float textureTime;
            public string textured, primer, primerTime, paint, paintTime, sheen, colR, colG, colB, colA, prevR, prevG, prevB, prevA;
        }
        [Serializable] class BattD { public string product; public SaveGaps.PlaneRef plane = new SaveGaps.PlaneRef(); public Rect r; public float depth; public bool cut, stapled, inset; public int pieces, seed; }
        [Serializable] class FoamD { public SaveGaps.PlaneRef plane = new SaveGaps.PlaneRef(); public Rect r; public float depth, thick; public int seed; }
        [Serializable] class AtticD { public SaveGaps.PlaneRef plane = new SaveGaps.PlaneRef(); public int nx, ny; public string d; }
        [Serializable] class BaffleD { public SaveGaps.PlaneRef ceiling = new SaveGaps.PlaneRef(); public Vector2 bay; public Vector3 end; }
        [Serializable] class RulerD { public Vector3 pos, facing; }
        [Serializable] class LooseBeadD { public Vector3 a, b; public Color color; public float dia; public string kind; public bool foam; }
        [Serializable]
        class Data
        {
            public int version = 1;
            public List<SurfD> surfaces = new List<SurfD>();
            public List<SaveGaps.PlaneRef> fireRated = new List<SaveGaps.PlaneRef>();
            public List<BattD> batts = new List<BattD>(); public List<FoamD> foams = new List<FoamD>(); public List<SaveGaps.PlaneRef> poly = new List<SaveGaps.PlaneRef>();
            public List<AtticD> attics = new List<AtticD>(); public List<BaffleD> baffles = new List<BaffleD>(); public List<RulerD> rulers = new List<RulerD>();
            public List<Vector3> sealed_ = new List<Vector3>(), fireBlocked = new List<Vector3>();
            public List<LooseBeadD> beads = new List<LooseBeadD>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => SaveSystem.RegisterExtension(Key, Save, Load, 20);

        // ================================================================== save

        static string Save()
        {
            var d = new Data();
            foreach (var s in Drywall.Surfaces)
            {
                if (s == null || !s.gameObject.activeInHierarchy || s.Fp == null || s.Sheets.Count == 0) continue;
                d.surfaces.Add(SaveSurface(s));
            }
            foreach (var fp in DrywallRules.FireRatedFaces) if (fp != null) d.fireRated.Add(SaveGaps.Plane(fp));

            foreach (var b in Insulation.Batts)
                if (b != null && b.gameObject.activeInHierarchy && b.P != null)
                    d.batts.Add(new BattD { product = b.P.Id, plane = SaveGaps.Plane(b.Fp), r = b.R, depth = b.Depth, cut = b.CutAround, stapled = b.Stapled, inset = b.Inset, pieces = b.Pieces, seed = b.Seed });
            foreach (var f in Insulation.Foams)
                if (f != null && f.gameObject.activeInHierarchy) d.foams.Add(new FoamD { plane = SaveGaps.Plane(f.Fp), r = f.R, depth = f.Depth, thick = f.Thick, seed = f.Seed });
            foreach (var p in Insulation.Poly) if (p != null && p.gameObject.activeInHierarchy) d.poly.Add(SaveGaps.Plane(p.Fp));
            foreach (var a in Insulation.Attics)
                if (a != null && a.gameObject.activeInHierarchy && a.D != null) d.attics.Add(new AtticD { plane = SaveGaps.Plane(a.Fp), nx = a.Nx, ny = a.Ny, d = SaveGaps.Pack(a.D) });
            foreach (var g in Insulation.Baffles)
            {
                if (g == null || !g.activeInHierarchy) continue;
                var bd = BaffleBay(g);
                if (bd != null) d.baffles.Add(bd);
            }
            foreach (var fl in UnityEngine.Object.FindObjectsByType<FinishLayer>(FindObjectsSortMode.InstanceID))
            {
                if (fl == null || !fl.gameObject.activeInHierarchy) continue;
                var t = fl.transform;
                if (fl.Kind == "ruler") d.rulers.Add(new RulerD { pos = t.position, facing = -(t.rotation * Vector3.forward) });
                else if (t.parent == null && fl.gameObject.name.StartsWith("Bead_"))
                {
                    var mf = fl.GetComponent<MeshFilter>(); var mr = fl.GetComponent<MeshRenderer>();
                    if (mf == null || mf.sharedMesh == null || mr == null) continue;
                    var bnd = mf.sharedMesh.bounds;
                    float len = bnd.size.z;
                    Vector3 dir = t.rotation * Vector3.forward;
                    bool foam = mr.sharedMaterial == FinishMaterials.Get(InsulationLooks.CanFoam);
                    d.beads.Add(new LooseBeadD
                    {
                        a = t.position - dir * len * 0.5f, b = t.position + dir * len * 0.5f, dia = Mathf.Max(bnd.size.x, bnd.size.y),
                        color = mr.sharedMaterial != null ? mr.sharedMaterial.color : Color.white, kind = fl.Kind, foam = foam,
                    });
                }
            }
            d.sealed_.AddRange(Insulation.SealedBoxes); d.fireBlocked.AddRange(Insulation.FireBlocked);
            return JsonUtility.ToJson(d);
        }

        static SurfD SaveSurface(DrywallSurface s)
        {
            var sd = new SurfD { plane = SaveGaps.Plane(s.Fp), area = s.Area, w = s.W, h = s.H, hasTexture = s.TextureKind != null, texture = s.TextureKind, textureTime = s.TextureTime };
            foreach (var sh in s.Sheets)
                sd.sheets.Add(new SheetD { product = sh.P.Id, r = sh.R, row = sh.Row, tLo = sh.TaperLo, tHi = sh.TaperHi, fLo = sh.FilledLo, fHi = sh.FilledHi, holes = new List<Rect>(sh.Holes), covered = new List<Rect>(sh.Covered) });
            foreach (var sc in s.Screws) sd.screws.Add(new ScrewD { p = sc.P, sheet = s.Sheets.IndexOf(sc.Sheet), spots = sc.Spots, spotTime = sc.SpotTime, sanded = sc.Sanded });
            foreach (var seam in s.Seams)
            {
                var md = new SeamD { key = seam.Key, beaded = seam.Beaded };
                foreach (var g in seam.Segs) md.segs.Add(new SegD { a = g.A, b = g.B, coats = g.Coats, coatTime = g.CoatTime, compound = g.Compound, mesh = g.Mesh, sanded = g.Sanded });
                sd.seams.Add(md);
                if (seam.Kind == SeamKind.OutsideCorner && Drywall.HasBead(s, seam))
                    sd.beads.Add(new BeadD { seam = seam.Key, paper = BeadPaper(s, seam) });
            }
            sd.textured = SaveGaps.Rle(s.Textured); sd.primer = SaveGaps.Rle(s.Primer); sd.primerTime = SaveGaps.Rle(s.PrimerTime);
            sd.paint = SaveGaps.Rle(s.Paint); sd.paintTime = SaveGaps.Rle(s.PaintTime); sd.sheen = SaveGaps.Rle(s.Sheen);
            sd.colR = SaveGaps.Rle(s.PaintColor, 0); sd.colG = SaveGaps.Rle(s.PaintColor, 1); sd.colB = SaveGaps.Rle(s.PaintColor, 2);
            sd.prevR = SaveGaps.Rle(s.PrevColor, 0); sd.prevG = SaveGaps.Rle(s.PrevColor, 1); sd.prevB = SaveGaps.Rle(s.PrevColor, 2);
            sd.colA = SaveGaps.Rle(s.PaintColor, 3); sd.prevA = SaveGaps.Rle(s.PrevColor, 3);
            return sd;
        }

        static bool BeadPaper(DrywallSurface s, Seam seam)
        {
            var a = s.FrontWorld(seam.P0); var b = s.FrontWorld(seam.P1); var mid = (a + b) * 0.5f;
            foreach (var bd in Drywall.Beads)
            {
                if (bd.Go == null) continue;
                var dd = bd.B - bd.A; float t = Mathf.Clamp01(Vector3.Dot(mid - bd.A, dd) / Mathf.Max(1e-6f, dd.sqrMagnitude));
                if ((bd.A + dd * t - mid).magnitude < 0.04f) return bd.Paper;
            }
            return false;
        }

        /// <summary>The ceiling bay a baffle sits in (its position is the wall end of the bay).</summary>
        static BaffleD BaffleBay(GameObject g)
        {
            // the baffle was stapled over a ceiling bay: find that face by probing the joists under it
            var from = g.transform.position + Vector3.up * 0.05f;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, 1.5f))
            {
                var piece = h.collider.GetComponentInParent<Piece>();
                if (piece == null || piece.Def == null || piece.Def.Category != PieceCategory.Lumber) continue;
                var fp = FramePlane.FromPieceFace(piece, Vector3.down);
                if (fp == null || !fp.IsCeiling) continue;
                // the end point the baffle was installed at: back along its length direction to the plate side
                Vector3 along = g.transform.rotation * Quaternion.Euler(26.6f, 0f, 0f) * Vector3.forward;
                along = Vector3.ProjectOnPlane(along, Vector3.up).normalized;
                Vector3 end = g.transform.position;
                Vector2 uv = fp.ToUV(end + along * 0.3f);
                if (!fp.BayAt(uv, out var bay)) continue;
                return new BaffleD { ceiling = SaveGaps.Plane(fp), bay = bay.R.center, end = end };   // height comes from the joist tops on install
            }
            return null;
        }

        // ================================================================== load

        /// <summary>Removes the session's drywall, bead and insulation objects (what a quit loses; a load does this first).</summary>
        public static void Wipe()
        {
            foreach (var s in Drywall.Surfaces) if (s != null) SaveGaps.Kill(s.gameObject);
            Drywall.Surfaces.Clear();
            foreach (var b in Drywall.Beads) SaveGaps.Kill(b.Go);
            Drywall.Beads.Clear();
            DrywallRules.FireRatedFaces.Clear();
            foreach (var b in Insulation.Batts) if (b != null) SaveGaps.Kill(b.gameObject);
            foreach (var f in Insulation.Foams) if (f != null) SaveGaps.Kill(f.gameObject);
            foreach (var p in Insulation.Poly) if (p != null) SaveGaps.Kill(p.gameObject);
            foreach (var a in Insulation.Attics) if (a != null) SaveGaps.Kill(a.gameObject);
            foreach (var g in Insulation.Baffles) SaveGaps.Kill(g);
            Insulation.Batts.Clear(); Insulation.Foams.Clear(); Insulation.Poly.Clear(); Insulation.Attics.Clear(); Insulation.Baffles.Clear();
            Insulation.Rulers.Clear(); Insulation.SealedBoxes.Clear(); Insulation.FireBlocked.Clear();
            foreach (var fl in UnityEngine.Object.FindObjectsByType<FinishLayer>(FindObjectsSortMode.None))
                if (fl != null && (fl.Kind == "ruler" || (fl.transform.parent == null && fl.gameObject.name.StartsWith("Bead_")))) SaveGaps.Kill(fl.gameObject);
        }

        static void Load(string json)
        {
            Wipe();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            Physics.SyncTransforms();
            int missing = 0;

            // 1. sheets on every face (seams with corners need all the faces first)
            var made = new List<(DrywallSurface s, SurfD sd)>();
            foreach (var sd in d.surfaces)
            {
                try
                {
                    var fp = SaveGaps.FindPlane(sd.plane);
                    if (fp == null) { missing++; continue; }
                    var s = Drywall.SurfaceFor(fp, true);
                    foreach (var shd in sd.sheets)
                    {
                        var p = Drywall.Product(shd.product);
                        if (p == null) continue;
                        var sh = new DrywallSheet { P = p, R = shd.r, Row = shd.row, TaperLo = shd.tLo, TaperHi = shd.tHi, FilledLo = shd.fLo, FilledHi = shd.fHi };
                        sh.Holes.AddRange(shd.holes); sh.Covered.AddRange(shd.covered);
                        s.AddSheet(sh);
                    }
                    foreach (var scd in sd.screws)
                    {
                        if (scd.sheet < 0 || scd.sheet >= s.Sheets.Count) continue;
                        s.Screws.Add(new DrywallScrew { P = scd.p, Sheet = s.Sheets[scd.sheet], Spots = scd.spots, SpotTime = scd.spotTime, Sanded = scd.sanded });
                    }
                    s.TextureKind = sd.hasTexture ? sd.texture : null; s.TextureTime = sd.textureTime;
                    made.Add((s, sd));
                }
                catch (Exception e) { Debug.LogError("[Save] drywall face failed to restore: " + e); }
            }
            foreach (var fr in d.fireRated) { var fp = SaveGaps.FindPlane(fr); if (fp != null) Drywall.TagFireRated(fp); }

            // 2. geometry maps + seams, then the saved joint work, screws, texel layers
            foreach (var (s, _) in made) s.Refresh();
            foreach (var (s, sd) in made)
            {
                try
                {
                    var byKey = new Dictionary<string, SeamD>();
                    foreach (var md in sd.seams) byKey[md.key] = md;
                    foreach (var seam in s.Seams)
                    {
                        if (!byKey.TryGetValue(seam.Key, out var md)) continue;
                        seam.Beaded = md.beaded;
                        seam.Segs.Clear();
                        foreach (var g in md.segs) seam.Segs.Add(new SeamSeg { A = g.a, B = g.b, Coats = g.coats, CoatTime = g.coatTime, Compound = string.IsNullOrEmpty(g.compound) ? "dw-mud-ap" : g.compound, Mesh = g.mesh, Sanded = g.sanded });
                    }
                    s.RebuildScrews();
                    if (s.W == sd.w && s.H == sd.h)
                    {
                        SaveGaps.UnRle(sd.textured, s.Textured); SaveGaps.UnRle(sd.primer, s.Primer); SaveGaps.UnRle(sd.primerTime, s.PrimerTime);
                        SaveGaps.UnRle(sd.paint, s.Paint); SaveGaps.UnRle(sd.paintTime, s.PaintTime); SaveGaps.UnRle(sd.sheen, s.Sheen);
                        SaveGaps.UnRle(sd.colR, s.PaintColor, 0); SaveGaps.UnRle(sd.colG, s.PaintColor, 1); SaveGaps.UnRle(sd.colB, s.PaintColor, 2);
                        SaveGaps.UnRle(sd.prevR, s.PrevColor, 0); SaveGaps.UnRle(sd.prevG, s.PrevColor, 1); SaveGaps.UnRle(sd.prevB, s.PrevColor, 2);
                        SaveGaps.UnRle(sd.colA, s.PaintColor, 3); SaveGaps.UnRle(sd.prevA, s.PrevColor, 3);
                    }
                    else Debug.LogWarning("[Save] drywall face framing changed size: paint / primer / texture left off");
                    foreach (var bd in sd.beads)
                    {
                        var seam = s.Seams.Find(x => x.Key == bd.seam);
                        if (seam != null) { bool paper = bd.paper; SaveGaps.Replay(() => Drywall.InstallBead(s, seam, paper)); }
                    }
                    s.Changed(false);
                    s.Refresh();
                    Drywall.UpdateBeads(s);
                }
                catch (Exception e) { Debug.LogError("[Save] drywall finish failed to restore: " + e); }
            }

            // 3. insulation (batts read the board on their face: pressed flat behind drywall)
            foreach (var bd in d.batts)
            {
                var fp = SaveGaps.FindPlane(bd.plane); var p = Insulation.Product(bd.product);
                if (fp == null || p == null) { missing++; continue; }
                var go = new GameObject("Batt_" + p.Id);
                go.transform.SetPositionAndRotation(fp.Origin, fp.Rotation);
                go.AddComponent<FinishLayer>().Kind = "batt";
                var b = go.AddComponent<Batt>();
                b.P = p; b.Fp = fp; b.R = bd.r; b.Depth = bd.depth; b.Pieces = bd.pieces; b.Seed = bd.seed; b.CutAround = bd.cut; b.Stapled = bd.stapled; b.Inset = bd.inset;
                b.Obstacles.AddRange(fp.Obstacles(bd.r, bd.depth));
                go.GetComponent<FinishLayer>().Owner = b;
                b.Rebuild();
                Insulation.Batts.Add(b);
            }
            foreach (var fd in d.foams)
            {
                var fp = SaveGaps.FindPlane(fd.plane);
                if (fp == null) { missing++; continue; }
                var go = new GameObject("SprayFoam");
                go.transform.SetPositionAndRotation(fp.Origin, fp.Rotation);
                go.AddComponent<FinishLayer>().Kind = "spray-foam";
                var f = go.AddComponent<FoamFill>();
                f.Fp = fp; f.R = fd.r; f.Depth = fd.depth; f.Thick = fd.thick; f.Seed = fd.seed;
                f.Rebuild();
                Insulation.Foams.Add(f);
            }
            foreach (var pr in d.poly)
            {
                var fp = SaveGaps.FindPlane(pr);
                if (fp == null) { missing++; continue; }
                SaveGaps.Replay(() => Insulation.InstallPoly(fp));
            }
            foreach (var ad in d.attics)
            {
                var fp = SaveGaps.FindPlane(ad.plane);
                if (fp == null) { missing++; continue; }
                var af = Insulation.AtticFor(fp, true);
                var depth = SaveGaps.Unpack(ad.d);
                if (af.D != null && depth.Length == af.D.Length) Array.Copy(depth, af.D, depth.Length);
                af.Rebuild();
            }
            foreach (var bd in d.baffles)
            {
                var fp = SaveGaps.FindPlane(bd.ceiling);
                if (fp == null || !fp.BayAt(bd.bay, out var bay)) { missing++; continue; }
                SaveGaps.Replay(() => Insulation.InstallBaffle(fp, bay, bd.end));
            }
            foreach (var rd in d.rulers) SaveGaps.Replay(() => Insulation.InstallRuler(rd.pos, rd.facing));
            Insulation.SealedBoxes.AddRange(d.sealed_); Insulation.FireBlocked.AddRange(d.fireBlocked);
            var foamMat = FinishMaterials.Get(InsulationLooks.CanFoam);
            foreach (var bd in d.beads)
            {
                var go = Bead.Add(bd.a, bd.b, new Color(bd.color.r, bd.color.g, bd.color.b, 1f), bd.dia, null, bd.kind);
                if (go != null && bd.foam) go.GetComponent<MeshRenderer>().sharedMaterial = foamMat;
            }
            if (missing > 0) Debug.LogWarning($"[Save] drywall/insulation: {missing} item(s) had no matching framing face and were skipped");
        }
    }
}
