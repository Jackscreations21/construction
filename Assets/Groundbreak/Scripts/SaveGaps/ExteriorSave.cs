using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Saves exterior work (save key "exterior"): every <see cref="ExteriorWall"/> (house wrap sheets / staples / seam tape /
    /// opening flashing, vinyl, fiber cement, board-and-batten, brick veneer, trim and sealant joints), every eave (soffit,
    /// fascia wrap, gutter, downspout) and every deck. Walls are found again from the restored framing by their face plane;
    /// their layers are restored from data and rebuilt. Decks are rebuilt stage by stage from their layout (their lumber is
    /// left out of the plain piece list, see <see cref="SaveSystem.RegisterPieceFilter"/>), without charging stock again.
    /// </summary>
    public static class ExteriorSave
    {
        public const string Key = "exterior";

        [Serializable] class WrapSheet { public Rect r; public int order, course; }
        [Serializable] class OpData { public int i; public bool cut, sill, jambs, head, flapDown; public List<string> steps = new List<string>(); }
        [Serializable] class VPanel { public float u0, u1; public int layer; }
        [Serializable] class VNail { public float u, v, slot, gap; public bool inFraming; }
        [Serializable] class VCourse { public int index; public float v0, gapL, gapR; public bool locked = true, removed, snap; public List<VPanel> panels = new List<VPanel>(); public List<VNail> nails = new List<VNail>(); }
        [Serializable] class VinylData { public string color = "White"; public bool starter, posts, jOpen, jTop, complete; public float starterV, bottomV, topLimit, runL, runR; public List<VCourse> courses = new List<VCourse>(); }
        [Serializable] class FCCourse { public int index; public float v0; public List<Vector2> planks = new List<Vector2>(), nails = new List<Vector2>(); public List<float> joints = new List<float>(); public List<bool> flashed = new List<bool>(); }
        [Serializable] class FCData { public string color = "Arctic white"; public float exposure, bottomV, topLimit, runL, runR; public bool starter, complete; public List<FCCourse> courses = new List<FCCourse>(); }
        [Serializable] class BBData { public string color = "Arctic white"; public bool panels, battens; public float spacing; public List<float> battenU = new List<float>(); }
        [Serializable]
        class BrickData
        {
            public string brickId; public float air, baseV, topV, mortar; public bool flashing; public int courses, maxCourses;
            public List<float> tieRows = new List<float>(), weeps = new List<float>(); public List<Vector2> ties = new List<Vector2>();
            public List<int> lintelKeys = new List<int>(); public List<float> lintelVals = new List<float>();
        }
        [Serializable] class TrimBoardData { public Rect r; public string kind; }
        [Serializable] class TrimJointData { public Vector2 a, b; public string what; public bool caulked, sidingJoint; public float proud; }
        [Serializable] class TrimData { public bool corners, casings; public List<TrimBoardData> boards = new List<TrimBoardData>(); public List<TrimJointData> joints = new List<TrimJointData>(); }
        [Serializable]
        class WallData
        {
            public Vector3 n, point; public float offset; public int siding;
            public List<WrapSheet> sheets = new List<WrapSheet>(); public List<Vector2> staples = new List<Vector2>(); public List<Rect> tapes = new List<Rect>();
            public List<OpData> ops = new List<OpData>(); public float rollFeet;
            public bool hasVinyl, hasFC, hasBB, hasBrick;
            public VinylData vinyl = new VinylData(); public FCData fc = new FCData(); public BBData bb = new BBData(); public BrickData brick = new BrickData();
            public TrimData trim = new TrimData();
        }
        [Serializable]
        class EaveData
        {
            public SaveGaps.PieceRef fascia; public bool soffit, wrapped, gutter, downspout, outletAtEnd = true;
            public float slope = 0.25f, gyStart, gyEnd; public List<float> hangers = new List<float>();
        }
        [Serializable] class DeckData { public int wall = -1; public float u0, width, depth, surfaceY, joistSpacing; public int stage; }
        [Serializable] class Data { public int version = 1; public List<WallData> walls = new List<WallData>(); public List<EaveData> eaves = new List<EaveData>(); public List<DeckData> decks = new List<DeckData>(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension(Key, Save, Load);
            SaveSystem.RegisterPieceFilter("deck", p => p.GetComponent<DeckPart>() != null);
        }

        // ================================================================== save

        static string Save()
        {
            var d = new Data();
            var walls = new List<ExteriorWall>();
            foreach (var w in ExteriorWall.All)
            {
                if (w == null || !w.gameObject.activeInHierarchy || w.Face == null) continue;
                walls.Add(w);
                d.walls.Add(SaveWall(w));
            }
            foreach (var e in Eaves.All)
            {
                if (e == null || !e.gameObject.activeInHierarchy || e.Fascia == null) continue;
                d.eaves.Add(new EaveData
                {
                    fascia = SaveGaps.Ref(e.Fascia), soffit = e.Soffit, wrapped = e.Wrapped, gutter = e.Gutter, downspout = e.Downspout, outletAtEnd = e.OutletAtEnd,
                    slope = e.SlopeInPer10, gyStart = e.GutterYStart, gyEnd = e.GutterYEnd, hangers = new List<float>(e.Hangers),
                });
            }
            foreach (var dk in DeckBuilder.All)
            {
                if (dk == null || !dk.gameObject.activeInHierarchy) continue;
                d.decks.Add(new DeckData { wall = walls.IndexOf(dk.Wall), u0 = dk.U0, width = dk.Width, depth = dk.Depth, surfaceY = dk.SurfaceY, joistSpacing = dk.JoistSpacing, stage = dk.Stage });
            }
            return JsonUtility.ToJson(d);
        }

        static WallData SaveWall(ExteriorWall w)
        {
            var wd = new WallData { n = w.Face.N, offset = w.Face.FaceOffset, point = w.Face.World(w.B.center), siding = (int)w.Siding, rollFeet = w.Wrap.RollFeetLeft };
            foreach (var s in w.Wrap.Sheets) wd.sheets.Add(new WrapSheet { r = s.R, order = s.Order, course = s.Course });
            wd.staples.AddRange(w.Wrap.Staples); wd.tapes.AddRange(w.Wrap.Tapes);
            foreach (var kv in w.Wrap.Ops)
                wd.ops.Add(new OpData { i = kv.Key, cut = kv.Value.Cut, sill = kv.Value.Sill, jambs = kv.Value.Jambs, head = kv.Value.Head, flapDown = kv.Value.FlapDown, steps = new List<string>(kv.Value.Steps) });
            if (w.Vinyl != null)
            {
                var s = w.Vinyl; wd.hasVinyl = true;
                var v = wd.vinyl;
                v.color = s.ColorName; v.starter = s.Starter; v.posts = s.Posts; v.jOpen = s.JOpenings; v.jTop = s.JTop; v.complete = s.Complete;
                v.starterV = s.StarterV; v.bottomV = s.BottomV; v.topLimit = s.TopLimit; v.runL = s.RunL; v.runR = s.RunR;
                foreach (var c in s.Courses)
                {
                    var vc = new VCourse { index = c.Index, v0 = c.V0, gapL = c.GapL, gapR = c.GapR, locked = c.Locked, removed = c.Removed, snap = c.SnapLocked };
                    foreach (var p in c.Panels) vc.panels.Add(new VPanel { u0 = p.U0, u1 = p.U1, layer = p.Layer });
                    foreach (var n in c.Nails) vc.nails.Add(new VNail { u = n.U, v = n.V, slot = n.SlotOffset, gap = n.HeadGap, inFraming = n.InFraming });
                    v.courses.Add(vc);
                }
            }
            if (w.FC != null)
            {
                var s = w.FC; wd.hasFC = true;
                var f = wd.fc;
                f.color = s.ColorName; f.exposure = s.Exposure; f.starter = s.Starter; f.complete = s.Complete; f.bottomV = s.BottomV; f.topLimit = s.TopLimit; f.runL = s.RunL; f.runR = s.RunR;
                foreach (var c in s.Courses)
                {
                    var fcc = new FCCourse { index = c.Index, v0 = c.V0, nails = new List<Vector2>(c.Nails), joints = new List<float>(c.Joints), flashed = new List<bool>(c.JointFlashed) };
                    foreach (var p in c.Planks) fcc.planks.Add(new Vector2(p.U0, p.U1));
                    f.courses.Add(fcc);
                }
            }
            if (w.BB != null)
            {
                wd.hasBB = true;
                wd.bb = new BBData { color = w.BB.ColorName, panels = w.BB.Panels, battens = w.BB.Battens, spacing = w.BB.BattenSpacing, battenU = new List<float>(w.BB.BattenU) };
            }
            if (w.Brick != null)
            {
                var s = w.Brick; wd.hasBrick = true;
                var b = wd.brick;
                b.brickId = s.BrickId; b.air = s.Air; b.flashing = s.Flashing; b.courses = s.Courses; b.baseV = s.BaseV; b.topV = s.TopV; b.mortar = s.MortarUsed; b.maxCourses = s.MaxCourses;
                b.tieRows.AddRange(s.TieRows); b.ties.AddRange(s.Ties); b.weeps.AddRange(s.Weeps);
                foreach (var kv in s.Lintels) { b.lintelKeys.Add(kv.Key); b.lintelVals.Add(kv.Value); }
            }
            wd.trim.corners = w.Trim.Corners; wd.trim.casings = w.Trim.Casings;
            foreach (var bd in w.Trim.Boards) wd.trim.boards.Add(new TrimBoardData { r = bd.R, kind = bd.Kind });
            foreach (var j in w.Trim.Joints) wd.trim.joints.Add(new TrimJointData { a = j.A, b = j.B, what = j.What, caulked = j.Caulked, sidingJoint = j.SidingJoint, proud = j.Proud });
            return wd;
        }

        // ================================================================== load

        static void Wipe()
        {
            foreach (var dk in UnityEngine.Object.FindObjectsByType<Deck>(FindObjectsSortMode.None)) SaveGaps.Kill(dk.gameObject);
            DeckBuilder.All.Clear();
            foreach (var e in UnityEngine.Object.FindObjectsByType<Eave>(FindObjectsSortMode.None)) SaveGaps.Kill(e.gameObject);
            Eaves.All.Clear();
            foreach (var w in UnityEngine.Object.FindObjectsByType<ExteriorWall>(FindObjectsSortMode.None)) SaveGaps.Kill(w.gameObject);
            ExteriorWall.All.Clear();
        }

        static void Load(string json)
        {
            Wipe();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            Physics.SyncTransforms();

            var walls = new ExteriorWall[d.walls.Count];
            int missing = 0;
            for (int i = 0; i < d.walls.Count; i++)
            {
                try
                {
                    var w = FindWall(d.walls[i]);
                    if (w == null) { missing++; continue; }
                    walls[i] = w;
                    RestoreWall(w, d.walls[i]);
                }
                catch (Exception e) { Debug.LogError("[Save] exterior wall failed to restore: " + e); }
            }
            Physics.SyncTransforms();

            var pieces = new SaveGaps.PieceIndex();
            foreach (var ed in d.eaves)
            {
                try
                {
                    var e = Eaves.For(pieces.Find(ed.fascia));
                    if (e == null) { missing++; continue; }
                    e.Soffit = ed.soffit; e.Wrapped = ed.wrapped; e.Gutter = ed.gutter; e.Downspout = ed.downspout; e.OutletAtEnd = ed.outletAtEnd;
                    e.SlopeInPer10 = ed.slope; e.GutterYStart = ed.gyStart; e.GutterYEnd = ed.gyEnd;
                    e.Hangers.Clear(); e.Hangers.AddRange(ed.hangers);
                    Eaves.Rebuild(e);
                }
                catch (Exception ex) { Debug.LogError("[Save] eave failed to restore: " + ex); }
            }

            bool unlimited = FinishStock.Unlimited;
            FinishStock.Unlimited = true;   // the deck was paid for when it was built
            try
            {
                foreach (var dd in d.decks)
                {
                    var w = dd.wall >= 0 && dd.wall < walls.Length ? walls[dd.wall] : null;
                    if (w == null) { missing++; continue; }
                    SaveGaps.Quiet(() =>
                    {
                        var dk = DeckBuilder.Plan(w, dd.u0, dd.width / Units.Inch, dd.depth / Units.Inch, dd.surfaceY);
                        if (dd.joistSpacing > 0f) dk.JoistSpacing = dd.joistSpacing;
                        for (int s = 0; s < dd.stage && !dk.Done; s++) if (!DeckBuilder.NextStage(dk)) { Debug.LogWarning("[Save] deck stage " + s + " failed to rebuild"); break; }
                    });
                }
            }
            finally { FinishStock.Unlimited = unlimited; }
            if (missing > 0) Debug.LogWarning($"[Save] exterior: {missing} wall(s)/eave(s)/deck(s) had no matching framing and were skipped");
        }

        /// <summary>The exterior wall on the restored framing face with this plane, containing the saved point.</summary>
        static ExteriorWall FindWall(WallData wd)
        {
            var n = wd.n.normalized;
            var cands = new List<(Piece p, float d)>();
            foreach (var q in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (q == null || q.Def == null || !q.gameObject.activeInHierarchy) continue;
                if (q.Def.Category != PieceCategory.Lumber && q.Def.Category != PieceCategory.Sheet) continue;
                if (q.GetComponent<DeckPart>() != null || q.GetComponentInParent<MaterialPile>() != null) continue;
                float dist = Vector3.Distance(q.transform.position, wd.point);
                if (dist > 40f || !FaceAt(q, n, wd.offset)) continue;
                cands.Add((q, dist));
            }
            cands.Sort((a, b) => a.d.CompareTo(b.d));
            int tries = 0;
            foreach (var (q, _) in cands)
            {
                if (tries++ > 24) break;
                var fp = FramePlane.FromPieceFace(q, n);
                if (fp == null || !fp.IsWall || Vector3.Dot(fp.N, n) < 0.999f || Mathf.Abs(fp.FaceOffset - wd.offset) > 0.006f) continue;
                var wb = fp.WorldBounds(); wb.Expand(0.1f);
                if (!wb.Contains(wd.point)) continue;
                var w = ExteriorWall.For(fp);
                if (w != null && Vector3.Dot(w.Face.N, n) > 0.999f && Mathf.Abs(w.Face.FaceOffset - wd.offset) < 0.006f) return w;
            }
            return null;
        }

        static bool FaceAt(Piece q, Vector3 n, float offset)
        {
            var t = q.transform; Vector3[] ax = { t.right, t.up, t.forward }; float[] hs = { q.Size.x * 0.5f, q.Size.y * 0.5f, q.Size.z * 0.5f };
            for (int i = 0; i < 3; i++)
            {
                float dot = Vector3.Dot(ax[i], n);
                if (Mathf.Abs(dot) < 0.999f) continue;
                return Mathf.Abs(Vector3.Dot(t.position, n) + hs[i] - offset) < 0.006f;
            }
            return false;
        }

        static void RestoreWall(ExteriorWall w, WallData wd)
        {
            w.Siding = (SidingKind)wd.siding;
            var st = w.Wrap;
            st.Sheets.Clear(); st.Staples.Clear(); st.Tapes.Clear(); st.Ops.Clear();
            foreach (var s in wd.sheets) st.Sheets.Add(new HouseWrap.Sheet { R = s.r, Order = s.order, Course = s.course });
            st.Staples.AddRange(wd.staples); st.Tapes.AddRange(wd.tapes);
            foreach (var o in wd.ops)
            {
                var op = st.Op(o.i);
                op.Cut = o.cut; op.Sill = o.sill; op.Jambs = o.jambs; op.Head = o.head; op.FlapDown = o.flapDown;
                op.Steps.Clear(); op.Steps.AddRange(o.steps);
            }
            st.RollFeetLeft = wd.rollFeet;

            w.Trim.Corners = wd.trim.corners; w.Trim.Casings = wd.trim.casings;
            w.Trim.Boards.Clear(); w.Trim.Joints.Clear();
            foreach (var b in wd.trim.boards) w.Trim.Boards.Add(new ExteriorTrim.Board { R = b.r, Kind = b.kind });
            foreach (var j in wd.trim.joints) w.Trim.Joints.Add(new ExteriorTrim.Joint { A = j.a, B = j.b, What = j.what, Caulked = j.caulked, SidingJoint = j.sidingJoint, Proud = j.proud });

            if (wd.hasVinyl)
            {
                var v = wd.vinyl;
                var s = new VinylSiding.State
                {
                    ColorName = v.color, Starter = v.starter, Posts = v.posts, JOpenings = v.jOpen, JTop = v.jTop, Complete = v.complete,
                    StarterV = v.starterV, BottomV = v.bottomV, TopLimit = v.topLimit, RunL = v.runL, RunR = v.runR,
                };
                foreach (var vc in v.courses)
                {
                    var c = new VinylSiding.Course { Index = vc.index, V0 = vc.v0, GapL = vc.gapL, GapR = vc.gapR, Locked = vc.locked, Removed = vc.removed, SnapLocked = vc.snap };
                    foreach (var p in vc.panels) c.Panels.Add(new VinylSiding.Panel { U0 = p.u0, U1 = p.u1, Layer = p.layer });
                    foreach (var n in vc.nails) c.Nails.Add(new VinylSiding.Nail { U = n.u, V = n.v, SlotOffset = n.slot, HeadGap = n.gap, InFraming = n.inFraming });
                    s.Courses.Add(c);
                }
                w.Vinyl = s;
            }
            if (wd.hasFC)
            {
                var f = wd.fc;
                var s = new FiberCement.State { ColorName = f.color, Exposure = f.exposure > 0f ? f.exposure : Units.In(7f), Starter = f.starter, Complete = f.complete, BottomV = f.bottomV, TopLimit = f.topLimit, RunL = f.runL, RunR = f.runR };
                foreach (var fc in f.courses)
                {
                    var c = new FiberCement.Course { Index = fc.index, V0 = fc.v0 };
                    foreach (var p in fc.planks) c.Planks.Add(new FiberCement.Plank { U0 = p.x, U1 = p.y });
                    c.Nails.AddRange(fc.nails); c.Joints.AddRange(fc.joints); c.JointFlashed.AddRange(fc.flashed);
                    s.Courses.Add(c);
                }
                w.FC = s;
            }
            if (wd.hasBB)
            {
                var s = new BoardBatten.State { ColorName = wd.bb.color, Panels = wd.bb.panels, Battens = wd.bb.battens };
                if (wd.bb.spacing > 0f) s.BattenSpacing = wd.bb.spacing;
                s.BattenU.AddRange(wd.bb.battenU);
                w.BB = s;
            }
            if (wd.hasBrick)
            {
                var b = wd.brick;
                var s = new BrickVeneer.State { BrickId = string.IsNullOrEmpty(b.brickId) ? ExteriorCatalog.Brick : b.brickId, Air = b.air, Flashing = b.flashing, Courses = b.courses, BaseV = b.baseV, TopV = b.topV, MortarUsed = b.mortar, MaxCourses = b.maxCourses };
                s.TieRows.AddRange(b.tieRows); s.Ties.AddRange(b.ties); s.Weeps.AddRange(b.weeps);
                for (int i = 0; i < b.lintelKeys.Count && i < b.lintelVals.Count; i++) s.Lintels[b.lintelKeys[i]] = b.lintelVals[i];
                w.Brick = s;
            }

            HouseWrap.Rebuild(w);
            if (w.Trim.Boards.Count > 0) ExteriorTrim.Rebuild(w);
            if (w.Vinyl != null) VinylSiding.RebuildAll(w);
            if (w.FC != null) FiberCement.Rebuild(w);
            if (w.BB != null) BoardBatten.Rebuild(w);
            if (w.Brick != null) BrickVeneer.RebuildAll(w);
            // sealant beads (laid when caulked, same placement as ExteriorTrim.Seal)
            float zb = w.WrapZ - 0.0005f;
            foreach (var j in w.Trim.Joints)
            {
                if (!j.Caulked) continue;
                float z = zb - j.Proud + Units.In(0.05f);
                Bead.Add(w.L2W(new Vector3(j.A.x, j.A.y, z)), w.L2W(new Vector3(j.B.x, j.B.y, z)), new Color(0.95f, 0.95f, 0.93f), Units.In(0.25f), w.Layer("caulk"), "ext-sealant");
            }
        }
    }
}
