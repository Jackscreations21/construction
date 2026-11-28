using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Saves concrete work before (and while) it sets (save key "concrete"): site layout (stakes, mason lines, laser benchmark,
    /// top-of-slab setting, marking paint), every form outline (corners, runs, boards / stakes / kickers with their nails, oil,
    /// poly, stripped), the rebar mat (bars, crossings tied or not, chairs) and every pour with its full per-cell state
    /// (<see cref="PourSystem.SavedState"/>: placed concrete, finishing passes, set times), plastic or set. A set pour comes
    /// back as the same PourSystem + ConcreteElement it was (cure colour keeps updating, forms strip off it, finishing tools and
    /// landscaping see a PourSystem), with its anchors, joints, cracks and curing blankets; set slabs whose pour is saved here
    /// are left out of the "slabs" section. Form boards are left out of the plain piece list (rebuilt here with their roles).
    /// Loads after "slabs" and "siteground" (order 10): the slab section clears every ConcreteElement first, and the vapor
    /// barrier is draped over the restored ground.
    /// </summary>
    public static class ConcreteSave
    {
        public const string Key = "concrete";

        [Serializable] class PaintD { public Vector3 pos, normal; public Color color; }
        [Serializable]
        class LayoutD
        {
            public List<Vector3> stakes = new List<Vector3>(); public List<int> lineA = new List<int>(), lineB = new List<int>();
            public bool laser; public Vector3 laserPos; public float benchmark, slabTop = 4f * Units.Inch;
            public List<PaintD> paint = new List<PaintD>();
        }
        [Serializable] class PieceD { public string id, role; public Vector3 size, pos; public Quaternion rot; public int state, nails, run; }
        [Serializable]
        class RunD
        {
            public Vector3 a, b, dir, outN; public float length, boardStart, boardLength; public string boardDef; public bool blownOut;
            public List<float> stakeT = new List<float>(), braceT = new List<float>();
        }
        [Serializable]
        class OutlineD
        {
            public float topY; public bool closed, oiled, stripped, poly;
            public List<Vector3> corners = new List<Vector3>(); public List<RunD> runs = new List<RunD>(); public List<PieceD> pieces = new List<PieceD>();
        }
        [Serializable]
        class PourD
        {
            public int outline = -1; public PourSystem.SavedState state = new PourSystem.SavedState();
            public bool set; public string kind = "slab"; public float thickIn, pouredAt, yards, cureLag; public int finish;
            public bool covered, wetCured, stripped; public List<string> elDefects = new List<string>();
            public List<Vector3> anchors = new List<Vector3>(); public int blankets; public bool heater;
        }
        [Serializable] class BarD { public Vector3 a, b; public string def; }
        [Serializable] class CrossD { public Vector3 p; public bool tied; }
        [Serializable] class ChairD { public Vector3 pos; public float h; }
        [Serializable]
        class Data
        {
            public int version = 1;
            public LayoutD layout = new LayoutD();
            public List<OutlineD> outlines = new List<OutlineD>();
            public List<PourD> pours = new List<PourD>();
            public List<BarD> bars = new List<BarD>(); public List<CrossD> crossings = new List<CrossD>(); public List<ChairD> chairs = new List<ChairD>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension(Key, Save, Load, 10);
            SaveSystem.RegisterPieceFilter("forms", p => p.GetComponent<FormPart>() != null);
        }

        /// <summary>Is this set slab saved (with its pour) by this section?</summary>
        public static bool Handles(ConcreteElement el) => el != null && el.Pour != null && el.Pour.Outline != null && FormSystem.All.Contains(el.Pour.Outline);

        // ================================================================== save

        static string Save()
        {
            var d = new Data();
            // layout
            var L = d.layout;
            foreach (var s in SiteLayout.Stakes) L.stakes.Add(s.Ground);
            foreach (var l in SiteLayout.Lines) { L.lineA.Add(l.A); L.lineB.Add(l.B); }
            L.laser = SiteLayout.LaserSet; L.laserPos = SiteLayout.LaserPos; L.benchmark = SiteLayout.BenchmarkY; L.slabTop = SiteLayout.SlabTopAboveBenchmark;
            foreach (var g in SiteLayout.Paint)
            {
                if (g == null || !g.activeInHierarchy) continue;
                var mr = g.GetComponentInChildren<MeshRenderer>();
                Color c = mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.color : Color.white;
                var n = g.transform.rotation * Vector3.up;
                L.paint.Add(new PaintD { pos = g.transform.position - n * 0.004f, normal = n, color = new Color(c.r / 0.86f, c.g / 0.86f, c.b / 0.86f, 1f) });
            }

            // forms (closed outlines; one still being drawn is left out)
            var outlines = new List<FormOutline>();
            foreach (var o in FormSystem.All)
            {
                if (o == null || !o.Closed) continue;
                outlines.Add(o);
                var od = new OutlineD { topY = o.TopY, closed = o.Closed, oiled = o.Oiled, stripped = o.Stripped, poly = o.Poly != null };
                od.corners.AddRange(o.Corners);
                for (int i = 0; i < o.Runs.Count; i++)
                {
                    var r = o.Runs[i];
                    od.runs.Add(new RunD
                    {
                        a = r.A, b = r.B, dir = r.Dir, outN = r.Out, length = r.Length, boardStart = r.BoardStart, boardLength = r.BoardLength,
                        boardDef = r.BoardDef != null ? r.BoardDef.Id : null, blownOut = r.BlownOut, stakeT = new List<float>(r.StakeT), braceT = new List<float>(r.BraceT),
                    });
                    foreach (var p in r.Boards) AddPiece(od, p, i, "board");
                    foreach (var p in r.Stakes) AddPiece(od, p, i, "stake");
                    foreach (var p in r.Braces) AddPiece(od, p, i, "brace");
                }
                d.outlines.Add(od);
            }

            // pours over those forms (plastic or set)
            foreach (var ps in PourSystem.All)
            {
                if (ps == null || !ps.gameObject.activeInHierarchy) continue;
                int oi = outlines.IndexOf(ps.Outline);
                if (oi < 0) continue;
                var pd = new PourD { outline = oi, state = ps.SaveState() };
                foreach (var a in ps.Anchors) if (a != null) pd.anchors.Add(a.position);
                var el = ps.Element;
                if (el != null)
                {
                    pd.set = true; pd.kind = el.Kind; pd.thickIn = el.ThicknessInches; pd.pouredAt = el.PouredAt; pd.yards = el.Yards; pd.cureLag = el.CureLagMinutes;
                    pd.finish = (int)el.Finish; pd.covered = el.Covered; pd.wetCured = el.WetCured; pd.stripped = el.FormsStripped;
                    pd.elDefects.AddRange(el.Defects);
                    pd.blankets = SeasonRules.BlanketsOn(el); pd.heater = SeasonRules.HeaterOn(el);
                }
                d.pours.Add(pd);
            }

            // rebar
            foreach (var b in RebarSystem.Bars) d.bars.Add(new BarD { a = b.A, b = b.B, def = b.DefId });
            foreach (var c in RebarSystem.Crossings) d.crossings.Add(new CrossD { p = c.P, tied = c.Tied });
            foreach (var g in RebarSystem.Chairs)
            {
                if (g == null) continue;
                var mf = g.GetComponent<MeshFilter>();
                d.chairs.Add(new ChairD { pos = g.transform.position, h = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.max.y : 0.05f });
            }
            return JsonUtility.ToJson(d);
        }

        static void AddPiece(OutlineD od, Piece p, int run, string role)
        {
            if (p == null || p.Def == null || !p.gameObject.activeInHierarchy) return;
            od.pieces.Add(new PieceD { id = p.Def.Id, role = role, size = p.Size, pos = p.transform.position, rot = p.transform.rotation, state = (int)p.State, nails = p.Nails, run = run });
        }

        // ================================================================== load

        /// <summary>Removes the session's layout, forms, rebar and pours (what a quit loses; a load does this first).</summary>
        public static void Wipe()
        {
            foreach (var s in SiteLayout.Stakes) SaveGaps.Kill(s.Go);
            foreach (var l in SiteLayout.Lines) SaveGaps.Kill(l.Go);
            foreach (var g in SiteLayout.Paint) SaveGaps.Kill(g);
            SaveGaps.Kill(SiteLayout.LaserGo);
            SiteLayout.Stakes.Clear(); SiteLayout.Lines.Clear(); SiteLayout.Paint.Clear();
            SiteLayout.LaserSet = false; SiteLayout.LaserGo = null; SiteLayout.BenchmarkY = 0f; SiteLayout.SlabTopAboveBenchmark = 4f * Units.Inch;

            foreach (var ps in new List<PourSystem>(PourSystem.All))
            {
                if (ps == null) continue;
                var el = ps.GetComponent<ConcreteElement>();
                SaveGaps.Kill(ps.gameObject);
                if (el != null) UnityEngine.Object.DestroyImmediate(el);   // leave ConcreteElement.All now
            }
            PourSystem.All.Clear();
            foreach (var o in FormSystem.All) if (o != null) SaveGaps.Kill(o.Root);
            if (FormSystem.Building != null) SaveGaps.Kill(FormSystem.Building.Root);
            FormSystem.All.Clear(); FormSystem.Building = null;

            foreach (var b in RebarSystem.Bars) SaveGaps.Kill(b.Go);
            foreach (var c in RebarSystem.Crossings) SaveGaps.Kill(c.Go);
            foreach (var g in RebarSystem.Chairs) SaveGaps.Kill(g);
            RebarSystem.Bars.Clear(); RebarSystem.Crossings.Clear(); RebarSystem.Chairs.Clear();
        }

        static void Load(string json)
        {
            Wipe();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            try { RestoreLayout(d.layout); } catch (Exception e) { Debug.LogError("[Save] site layout failed to restore: " + e); }

            var outlines = new List<FormOutline>();
            foreach (var od in d.outlines)
            {
                FormOutline o = null;
                try { o = RestoreOutline(od); } catch (Exception e) { Debug.LogError("[Save] form outline failed to restore: " + e); }
                outlines.Add(o);
            }

            Transform rebarRoot = null;
            Transform RebarRoot() { if (rebarRoot == null) rebarRoot = new GameObject("Rebar").transform; return rebarRoot; }
            foreach (var b in d.bars)
            {
                var def = Catalog.Get(b.def);
                if (def == null) continue;
                Vector3 dd = b.b - b.a;
                var go = CTool.Build("Rebar " + def.Name, (mb, m) => mb.AddRod((b.a + b.b) * 0.5f, Quaternion.LookRotation(dd.normalized, Vector3.up), def.Size.x * 0.5f * 1.1f, dd.magnitude, 8, m.K(SurfaceKind.Rebar)), RebarRoot());
                RebarSystem.Bars.Add(new RebarSystem.Bar { A = b.a, B = b.b, DefId = b.def, Go = go });
            }
            foreach (var c in d.crossings)
            {
                var cr = new RebarSystem.Crossing { P = c.p, Tied = c.tied };
                if (c.tied)
                {
                    cr.Go = CTool.Build("Tie", (b, m) =>
                    {
                        int w = m.K(SurfaceKind.MatteBlack, new Color(0.7f, 0.7f, 0.7f));
                        b.AddRod(Vector3.zero, Quaternion.Euler(0, 45, 0), 0.0013f, 0.03f, 4, w);
                        b.AddRod(Vector3.zero, Quaternion.Euler(0, -45, 0), 0.0013f, 0.03f, 4, w);
                        b.AddRod(new Vector3(0, 0.008f, 0), Quaternion.Euler(-90, 0, 0), 0.0016f, 0.016f, 4, w);
                    }, RebarRoot());
                    cr.Go.transform.position = c.p;
                }
                RebarSystem.Crossings.Add(cr);
            }
            foreach (var ch in d.chairs)
            {
                float h = Mathf.Max(0.02f, ch.h);
                var go = CTool.Build("RebarChair", (b, m) =>
                {
                    int pl = m.C(new Color(0.42f, 0.43f, 0.45f));
                    b.AddFrustum(new Vector3(0, h * 0.5f, 0), Quaternion.identity, 0.028f, 0.012f, h, 8, pl);
                    b.AddBox(new Vector3(0, h - 0.004f, 0), new Vector3(0.03f, 0.008f, 0.012f), Quaternion.identity, pl);
                }, RebarRoot());
                go.transform.position = ch.pos;
                RebarSystem.Chairs.Add(go);
            }

            foreach (var pd in d.pours)
            {
                try
                {
                    var o = pd.outline >= 0 && pd.outline < outlines.Count ? outlines[pd.outline] : null;
                    if (o == null) continue;
                    RestorePour(o, pd);
                }
                catch (Exception e) { Debug.LogError("[Save] pour failed to restore: " + e); }
            }
        }

        static void RestoreLayout(LayoutD L)
        {
            var map = new int[L.stakes.Count];
            for (int i = 0; i < L.stakes.Count; i++)
            {
                int k = SiteLayout.AddStake(L.stakes[i]);
                map[i] = k;
                var s = SiteLayout.Stakes[k];
                s.Ground = L.stakes[i];
                if (s.Go != null) s.Go.transform.position = L.stakes[i];
            }
            for (int i = 0; i < L.lineA.Count && i < L.lineB.Count; i++)
            {
                int a = L.lineA[i], b = L.lineB[i];
                if (a >= 0 && a < map.Length && b >= 0 && b < map.Length) SiteLayout.AddLine(map[a], map[b]);
            }
            if (L.laser)
            {
                SiteLayout.SetLaser(L.laserPos);
                SiteLayout.LaserPos = L.laserPos; SiteLayout.BenchmarkY = L.benchmark;
                if (SiteLayout.LaserGo != null) SiteLayout.LaserGo.transform.position = new Vector3(L.laserPos.x, L.laserPos.y, L.laserPos.z);
            }
            if (L.laser) SiteLayout.SetSlabTop(L.slabTop);   // grade marks + string heights at the saved top of slab
            else SiteLayout.SlabTopAboveBenchmark = L.slabTop;
            foreach (var p in L.paint) SiteLayout.SprayMark(p.pos, p.normal.sqrMagnitude > 0.5f ? p.normal : Vector3.up, p.color);
        }

        static FormOutline RestoreOutline(OutlineD od)
        {
            var o = new FormOutline { TopY = od.topY, Closed = od.closed, Oiled = od.oiled, Stripped = od.stripped, Root = new GameObject("ConcreteForms") };
            o.Corners.AddRange(od.corners);
            foreach (var rd in od.runs)
            {
                var r = new FormRun { A = rd.a, B = rd.b, Dir = rd.dir, Out = rd.outN, Length = rd.length, BoardStart = rd.boardStart, BoardLength = rd.boardLength, BlownOut = rd.blownOut };
                r.BoardDef = string.IsNullOrEmpty(rd.boardDef) ? null : Catalog.Get(rd.boardDef);
                r.StakeT.AddRange(rd.stakeT); r.BraceT.AddRange(rd.braceT);
                o.Runs.Add(r);
            }
            foreach (var pd in od.pieces)
            {
                var def = Catalog.Get(pd.id);
                if (def == null || pd.run < 0 || pd.run >= o.Runs.Count) continue;
                var r = o.Runs[pd.run];
                var p = Piece.Create(def, pd.size.z, pd.pos, pd.rot);
                if ((p.Size - pd.size).sqrMagnitude > 1e-8f) p.SetSize(pd.size);
                p.State = (PieceState)pd.state; p.Nails = pd.nails;
                p.transform.SetParent(o.Root.transform, true);
                var fp = p.gameObject.AddComponent<FormPart>(); fp.Outline = o; fp.Run = r; fp.Role = pd.role;
                if (pd.role == "stake") r.Stakes.Add(p); else if (pd.role == "brace") r.Braces.Add(p); else r.Boards.Add(p);
            }
            if (!o.Stripped)
                foreach (var c in o.Corners)
                {
                    var mk = CTool.Build("FormCorner", (b, m) =>
                    {
                        b.AddBox(new Vector3(0, -0.15f, 0), new Vector3(0.012f, 0.3f, 0.012f), Quaternion.identity, m.K(SurfaceKind.PineLumber));
                        b.AddBox(new Vector3(0.03f, -0.03f, 0), new Vector3(0.06f, 0.04f, 0.002f), Quaternion.identity, m.C(new Color(1f, 0.4f, 0f)));
                    }, o.Root.transform);
                    mk.transform.position = c;
                    o.Markers.Add(mk);
                }
            if (od.poly && SiteGround.I != null)
            {
                var g = SiteGround.I;
                o.Poly = CTool.Build("VaporBarrier", (b, m) =>
                {
                    int mat = m.K(SurfaceKind.VaporBarrier);
                    SiteGround.ForCellsIn(o.Polygon, (x, z) =>
                    {
                        Vector3 P(int vx, int vz) => SiteGround.VertexPos(vx, vz, g.H[SiteGround.VI(vx, vz)] + 0.004f);
                        b.AddQuad(P(x, z), P(x, z + 1), P(x + 1, z + 1), P(x + 1, z), mat, Vector3.up);
                    });
                }, o.Root.transform);
            }
            FormSystem.All.Add(o);
            return o;
        }

        static void RestorePour(FormOutline o, PourD pd)
        {
            var ps = PourSystem.Begin(o);
            if (!ps.LoadState(pd.state))
            {
                Debug.LogWarning("[Save] a pour's forms no longer match the saved concrete; it was left out");
                o.Pour = null; PourSystem.All.Remove(ps); SaveGaps.Kill(ps.gameObject);
                return;
            }
            foreach (var a in pd.anchors) ps.Anchors.Add(AnchorBolt(a, ps.transform));
            if (pd.set)
            {
                var el = ps.gameObject.AddComponent<ConcreteElement>();
                bool was = SeasonRules.Restoring;
                SeasonRules.Restoring = true;    // the saved blankets go back on without charging stock again
                try { el.Init(ps, o.Polygon, o.TopY, pd.thickIn, ps.Psi, pd.pouredAt, (SurfaceKind)pd.finish, pd.yards); }
                finally { SeasonRules.Restoring = was; }
                ps.Element = el;
                el.Kind = pd.kind; el.Covered = pd.covered; el.WetCured = pd.wetCured; el.FormsStripped = pd.stripped; el.CureLagMinutes = pd.cureLag;
                el.Defects.AddRange(pd.elDefects);
                el.AnchorBolts.AddRange(ps.Anchors);
                if (pd.blankets > 0 || pd.heater) SlabSave.QueueProtection(el, pd.blankets, pd.heater);
            }
            ps.FinishRestore();
        }

        /// <summary>1/2" J-bolt visual (as wet-set by the trowel), axis at p, nut ~1-1/4" above the slab top.</summary>
        public static Transform AnchorBolt(Vector3 p, Transform parent)
        {
            var bolt = CTool.Build("AnchorBolt", (mb, m) =>
            {
                int galv = m.K(SurfaceKind.Galvanized);
                mb.AddRod(new Vector3(0, -3.5f * Units.Inch + 1.375f * Units.Inch, 0), Quaternion.Euler(-90, 0, 0), 0.25f * Units.Inch, 10f * Units.Inch, 8, galv);
                mb.AddCylinder(new Vector3(0, 0.75f * Units.Inch, 0), 0.6f * Units.Inch, 0.08f * Units.Inch, 12, galv);
                mb.AddCylinder(new Vector3(0, 1.0f * Units.Inch, 0), 0.43f * Units.Inch, 0.45f * Units.Inch, 6, galv);
            }, parent);
            bolt.transform.position = p;
            return bolt.transform;
        }
    }
}
