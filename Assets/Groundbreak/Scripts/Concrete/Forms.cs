using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Marks a Piece as part of a concrete form (board, stake or kicker) so other systems can ignore/strip it.</summary>
    public class FormPart : MonoBehaviour { public FormOutline Outline; public FormRun Run; public string Role; }

    public class FormRun
    {
        public Vector3 A, B, Dir, Out;   // A,B = inside face line at the corners; Out = horizontal outward normal
        public float Length;
        public PieceDefinition BoardDef;
        public readonly List<Piece> Boards = new List<Piece>();
        public readonly List<float> StakeT = new List<float>();
        public readonly List<Piece> Stakes = new List<Piece>();
        public readonly List<float> BraceT = new List<float>();
        public readonly List<Piece> Braces = new List<Piece>();
        public bool BlownOut;
        /// <summary>Board run: starts at t = BoardStart (negative = past corner A) and covers BoardLength.</summary>
        public float BoardStart, BoardLength;
        public Vector3 PointAt(float t) => A + Dir * t;
    }

    /// <summary>One slab/footing form: a closed outline of 2x form boards set to the top-of-slab elevation.</summary>
    public class FormOutline
    {
        public readonly List<Vector3> Corners = new List<Vector3>();
        public readonly List<FormRun> Runs = new List<FormRun>();
        public bool Closed, Oiled, Stripped;
        public float TopY;
        public GameObject Root, Poly;
        public PourSystem Pour;
        readonly List<GameObject> _markers = new List<GameObject>();

        public Vector2[] Polygon
        {
            get { var a = new Vector2[Corners.Count]; for (int i = 0; i < a.Length; i++) a[i] = CTool.XZ(Corners[i]); return a; }
        }
        public bool Contains(Vector3 p) => Closed && CTool.PointInPolygon(Polygon, CTool.XZ(p));
        public float AreaSqFt
        {
            get
            {
                var p = Polygon; float a = 0;
                for (int i = 0, j = p.Length - 1; i < p.Length; j = i++) a += (p[j].x + p[i].x) * (p[j].y - p[i].y);
                return Mathf.Abs(a) * 0.5f / (Units.Foot * Units.Foot);
            }
        }
        internal List<GameObject> Markers => _markers;
    }

    /// <summary>
    /// Builds forms from real lumber: 2x boards on edge along the outline (outside the inside-face line), wood stakes,
    /// 2x4 kicker braces. Computes the enclosed concrete volume from the ground heightfield (takeoff) and the blowout risk.
    /// </summary>
    public static class FormSystem
    {
        public const string StakeId = "formstake24";
        public const string PolyId = "poly6mil";
        public const string FormOilId = "formoil";
        public static readonly List<FormOutline> All = new List<FormOutline>();
        public static FormOutline Building;   // outline being drawn (not yet closed)

        /// <summary>The most recent closed outline whose forms are still standing.</summary>
        public static FormOutline Active { get { for (int i = All.Count - 1; i >= 0; i--) if (All[i].Closed && !All[i].Stripped) return All[i]; return null; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); Building = null; }

        static readonly float[] Widths = { 3.5f, 5.5f, 7.25f, 9.25f, 11.25f };
        static readonly string[] Prefix = { "2x4x", "2x6x", "2x8x", "2x10x", "2x12x" };

        // ------------------------------------------------------------------ outline

        /// <summary>Adds a snapped corner (inside face of the form). Clicking within 3" of the first corner closes the outline.</summary>
        public static string AddCorner(Vector3 p)
        {
            p = CTool.SnapXZ(p);
            if (Building == null)
            {
                Building = new FormOutline { TopY = SiteLayout.SlabTopY };
                Building.Root = new GameObject("ConcreteForms");
                All.Add(Building);
            }
            var o = Building;
            if (o.Corners.Count >= 3 && CTool.XZ(p - o.Corners[0]).magnitude < 3f * Units.Inch) return Close();
            p.y = o.TopY;
            o.Corners.Add(p);
            var mk = CTool.Build("FormCorner", (b, m) =>
            {
                b.AddBox(new Vector3(0, -0.15f, 0), new Vector3(0.012f, 0.3f, 0.012f), Quaternion.identity, m.K(SurfaceKind.PineLumber));
                b.AddBox(new Vector3(0.03f, -0.03f, 0), new Vector3(0.06f, 0.04f, 0.002f), Quaternion.identity, m.C(new Color(1f, 0.4f, 0f)));
            }, o.Root.transform);
            mk.transform.position = p;
            o.Markers.Add(mk);
            if (o.Corners.Count >= 2)
            {
                Vector3 a = o.Corners[o.Corners.Count - 2], c = p, d = c - a;
                o.Markers.Add(CTool.Build("FormLine", (b, m) => b.AddRod((a + c) * 0.5f, Quaternion.LookRotation(d.normalized), 0.0012f, d.magnitude, 4, m.C(new Color(1f, 0.25f, 0.55f)), false), o.Root.transform));
            }
            return $"Corner {o.Corners.Count} at top of form {Units.Format(o.TopY - SiteLayout.BenchmarkY)} above benchmark";
        }

        /// <summary>Convenience: whole rectangle (min/max world XZ corners) as an outline, closed.</summary>
        public static string BuildRectangle(Vector3 min, Vector3 max)
        {
            Building = null;
            AddCorner(new Vector3(min.x, 0, min.z));
            AddCorner(new Vector3(max.x, 0, min.z));
            AddCorner(new Vector3(max.x, 0, max.z));
            AddCorner(new Vector3(min.x, 0, max.z));
            return Close();
        }

        public static void UndoCorner()
        {
            var o = Building; if (o == null || o.Corners.Count == 0) return;
            o.Corners.RemoveAt(o.Corners.Count - 1);
            int remove = o.Corners.Count >= 1 ? 2 : 1;
            for (int k = 0; k < remove && o.Markers.Count > 0; k++) { Object.Destroy(o.Markers[o.Markers.Count - 1]); o.Markers.RemoveAt(o.Markers.Count - 1); }
        }

        static PieceDefinition[] StockFor(int wi)
        {
            var list = new List<PieceDefinition>();
            foreach (var d in Catalog.All)
                if (d.Category == PieceCategory.Lumber && d.Id.StartsWith(Prefix[wi]) && Mathf.Abs(Units.ToInches(d.Size.x) - Widths[wi]) < 0.05f)
                    list.Add(d);
            list.Sort((a, b) => a.Size.z.CompareTo(b.Size.z));
            return list.ToArray();
        }

        static List<float> Segments(float len, float maxLen)
        {
            var segs = new List<float>();
            int n = Mathf.CeilToInt(len / maxLen - 1e-4f);
            for (int i = 0; i < n; i++) segs.Add(len / n);
            return segs;
        }

        /// <summary>Closes the outline and sets the form boards (consumes lumber; offcuts go to scrap).</summary>
        public static string Close()
        {
            var o = Building;
            if (o == null || o.Corners.Count < 3) return "Need at least 3 corners";
            var poly = o.Polygon;
            int n = o.Corners.Count;
            var runs = new List<FormRun>();
            var need = new Dictionary<int, List<float>>();
            // Corner lapping: on an even outline, alternate runs carry "through" boards that pass both corners by 1 1/2"
            // (the shorter runs, so the long runs can be full stock lengths); odd outlines lap at the B end only.
            float L0 = (o.Corners[1] - o.Corners[0]).magnitude, L1 = (o.Corners[2 % n] - o.Corners[1]).magnitude;
            int throughParity = L0 <= L1 ? 0 : 1;
            for (int i = 0; i < n; i++)
            {
                var r = new FormRun { A = o.Corners[i], B = o.Corners[(i + 1) % n] };
                Vector3 d = r.B - r.A; d.y = 0; r.Length = d.magnitude; r.Dir = d / Mathf.Max(1e-5f, r.Length);
                Vector3 nrm = new Vector3(r.Dir.z, 0, -r.Dir.x);
                Vector3 mid = (r.A + r.B) * 0.5f;
                if (CTool.PointInPolygon(poly, CTool.XZ(mid + nrm * 0.05f))) nrm = -nrm;
                r.Out = nrm;
                // board depth: from top of form down to the outside grade + 1" set into the ground
                float g = Mathf.Min(SiteGround.GroundY(r.A + r.Out * 0.1f), SiteGround.GroundY(r.B + r.Out * 0.1f), SiteGround.GroundY(mid + r.Out * 0.1f));
                float depthIn = Units.ToInches(o.TopY - g) + 1f;
                int wi = Widths.Length - 1;
                for (int k = 0; k < Widths.Length; k++) if (Widths[k] >= depthIn) { wi = k; break; }
                var stock = StockFor(wi);
                if (stock.Length == 0) return "No " + Prefix[wi] + " lumber in the catalog";
                r.BoardDef = stock[stock.Length - 1];
                float lap = 1.5f * Units.Inch;
                if (n % 2 == 0) { bool through = i % 2 == throughParity; r.BoardStart = through ? -lap : 0f; r.BoardLength = r.Length + (through ? 2 * lap : 0f); }
                else { r.BoardStart = 0f; r.BoardLength = r.Length + lap; }
                if (!need.ContainsKey(wi)) need[wi] = new List<float>();
                need[wi].AddRange(Segments(r.BoardLength, stock[stock.Length - 1].Size.z));
                runs.Add(r);
            }
            // check lumber on site before cutting anything
            foreach (var kv in need)
            {
                var stock = StockFor(kv.Key);
                var avail = new Dictionary<string, int>();
                foreach (var d in stock) avail[d.Id] = Game.Stock.Count(d.Id);
                foreach (var need1 in kv.Value)
                {
                    PieceDefinition pick = null;
                    foreach (var d in stock) if (d.Size.z >= need1 - 1e-4f && avail[d.Id] > 0) { pick = d; break; }
                    if (pick == null)
                    {
                        string longest = stock[stock.Length - 1].Name;
                        return $"Not enough form lumber: need {Prefix[kv.Key].TrimEnd('x')} boards at least {Units.Format(need1)} long (e.g. {longest})";
                    }
                    avail[pick.Id]--;
                }
            }

            foreach (var mk in o.Markers) if (mk != null && mk.name == "FormLine") Object.Destroy(mk);
            foreach (var r in runs)
            {
                var stock = StockFor(System.Array.FindIndex(Prefix, p => r.BoardDef.Id.StartsWith(p)));
                float t = r.BoardStart;
                foreach (var cutLen in Segments(r.BoardLength, r.BoardDef.Size.z))
                {
                    PieceDefinition pick = null;
                    foreach (var d in stock) if (d.Size.z >= cutLen - 1e-4f && Game.Stock.Count(d.Id) > 0) { pick = d; break; }
                    Game.Stock.TryTake(pick.Id);
                    // up to 2' of overhang past the corner is left on (keeps the board reusable); more gets cut off
                    float len = pick.Size.z - cutLen <= 24f * Units.Inch ? pick.Size.z : cutLen;
                    if (pick.Size.z - len > 0.01f) Game.Stock.Scrap.Add((pick.Id, pick.Size.z - len));
                    float w = pick.Size.x;
                    Vector3 c = r.A + r.Dir * (t + len * 0.5f) + r.Out * (0.75f * Units.Inch);
                    c.y = o.TopY - w * 0.5f;
                    var piece = Piece.Create(pick, len, c, Quaternion.LookRotation(r.Dir, r.Out));
                    piece.transform.SetParent(o.Root.transform, true);
                    piece.gameObject.AddComponent<FormPart>().Run = r;
                    piece.GetComponent<FormPart>().Outline = o; piece.GetComponent<FormPart>().Role = "board";
                    r.Boards.Add(piece);
                    t += cutLen;
                }
            }
            o.Runs.AddRange(runs);
            o.Closed = true;
            Building = null;
            return $"Forms set: {o.Runs.Count} runs, {o.AreaSqFt:0} sq ft. Takeoff {TakeoffYards(o):0.00} cu yd. Now stake them (F).";
        }

        // ------------------------------------------------------------------ stakes / braces

        public static bool AddStake(FormRun r, float t, FormOutline o)
        {
            t = Mathf.Clamp(t, 0f, r.Length);
            foreach (var s in r.StakeT) if (Mathf.Abs(s - t) < 4f * Units.Inch) return false;
            if (!CTool.TakeStock(StakeId, 1, "form stakes")) return false;
            var def = Catalog.Get(StakeId);
            Vector3 p = r.PointAt(t) + r.Out * (2.25f * Units.Inch);
            p.y = o.TopY - 0.5f * Units.Inch - def.Size.z * 0.5f;   // driven, top 1/2" below top of form so the screed rides the boards
            var piece = Piece.Create(def, 0f, p, Quaternion.LookRotation(Vector3.up, r.Out));
            piece.transform.SetParent(o.Root.transform, true);
            var fp = piece.gameObject.AddComponent<FormPart>(); fp.Run = r; fp.Outline = o; fp.Role = "stake";
            piece.Fasten();
            foreach (var b in r.Boards) b.Fasten();   // duplex nails through the stake into the board
            r.StakeT.Add(t); r.StakeT.Sort(); r.Stakes.Add(piece);
            return true;
        }

        /// <summary>Stakes every run: 6" from each end and at most `spacing` on center (default 36").</summary>
        public static int AutoStake(FormOutline o, float spacing = 36f * Units.Inch)
        {
            if (o == null) return 0;
            int n = 0;
            foreach (var r in o.Runs)
            {
                float end = 6f * Units.Inch;
                float span = Mathf.Max(0f, r.Length - 2 * end);
                int k = Mathf.Max(1, Mathf.CeilToInt(span / spacing - 1e-4f));
                for (int i = 0; i <= k; i++) if (AddStake(r, end + span * i / k, o)) n++;
            }
            return n;
        }

        /// <summary>2x4 kicker from the top of the board down to a kicker stake 18" outside.</summary>
        public static bool AddBrace(FormRun r, float t, FormOutline o)
        {
            if (!CTool.TakeStock(StakeId, 1, "form stakes")) return false;
            if (!TakeKickerStock()) { Game.Stock.Add(StakeId, 1); return false; }
            var sdef = Catalog.Get(StakeId);
            Vector3 basePt = r.PointAt(t) + r.Out * (1.5f * Units.Inch + 18f * Units.Inch);
            float gy = SiteGround.GroundY(basePt);
            var ks = Piece.Create(sdef, 0f, new Vector3(basePt.x, gy + 4f * Units.Inch - sdef.Size.z * 0.5f, basePt.z), Quaternion.LookRotation(Vector3.up, r.Out));
            Vector3 top = r.PointAt(t) + r.Out * (1.5f * Units.Inch + 0.75f * Units.Inch); top.y = o.TopY - 1.5f * Units.Inch;
            Vector3 low = basePt - r.Out * (0.75f * Units.Inch + 0.75f * Units.Inch); low.y = gy + 2.5f * Units.Inch;
            Vector3 d = top - low;
            var k = Piece.Create(Catalog.Get("2x4x8"), d.magnitude, (top + low) * 0.5f, Quaternion.LookRotation(d.normalized, Vector3.Cross(d, r.Dir).normalized));
            foreach (var p in new[] { ks, k })
            {
                p.transform.SetParent(o.Root.transform, true);
                var fp = p.gameObject.AddComponent<FormPart>(); fp.Run = r; fp.Outline = o; fp.Role = "brace";
                p.Fasten(); r.Braces.Add(p);
            }
            r.BraceT.Add(t); r.BraceT.Sort();
            return true;
        }

        static float _kickerLeft;
        static bool TakeKickerStock()
        {
            float need = 30f * Units.Inch;
            if (_kickerLeft >= need) { _kickerLeft -= need; return true; }
            if (!CTool.TakeStock("2x4x8", 1, "2x4 x 8' for kickers")) return false;
            if (_kickerLeft > 0.05f) Game.Stock.Scrap.Add(("2x4x8", _kickerLeft));
            _kickerLeft = 96f * Units.Inch - need;
            return true;
        }

        /// <summary>0 = safe, 1 = will blow out. Depth of concrete against the run drives the required staking.</summary>
        public static float BlowoutRisk(FormRun r, FormOutline o, out string why)
        {
            why = "";
            float minG = float.MaxValue;
            for (float t = 0; t <= r.Length; t += 0.15f) minG = Mathf.Min(minG, SiteGround.GroundY(r.PointAt(t) - r.Out * 0.08f));
            float depth = o.TopY - minG;
            float din = Units.ToInches(depth) - 0.5f;   // 1/2" tolerance for settlement
            float allowed = din <= 8f ? 48f : din <= 16f ? 36f : 24f;
            float braceEvery = din <= 8f ? 0f : din <= 16f ? 72f : 48f;
            if (r.StakeT.Count == 0) { why = "no stakes"; return 1f; }
            float risk = 0f;
            float maxGap = 0f;
            for (int i = 1; i < r.StakeT.Count; i++) maxGap = Mathf.Max(maxGap, r.StakeT[i] - r.StakeT[i - 1]);
            float g0 = Units.ToInches(r.StakeT[0]), g1 = Units.ToInches(r.Length - r.StakeT[r.StakeT.Count - 1]);
            if (g0 > 12f || g1 > 12f) { risk += 0.5f; why = "end of run not staked within 12\""; }
            float mg = Units.ToInches(maxGap);
            if (mg > allowed) { risk += (mg / allowed - 1f) * 1.5f; why = $"stakes {mg:0}\" apart (max {allowed:0}\" for {din:0}\" of concrete)"; }
            if (braceEvery > 0f)
            {
                var bt = new List<float>(r.BraceT);
                if (bt.Count == 0) { risk += 0.6f; why = $"{din:0}\" deep pour needs kicker braces every {braceEvery:0}\""; }
                else
                {
                    float bg = Mathf.Max(Units.ToInches(bt[0]), Units.ToInches(r.Length - bt[bt.Count - 1]));
                    for (int i = 1; i < bt.Count; i++) bg = Mathf.Max(bg, Units.ToInches(bt[i] - bt[i - 1]));
                    if (bg > braceEvery) { risk += (bg / braceEvery - 1f); why = "kickers too far apart"; }
                }
            }
            return Mathf.Clamp01(risk);
        }

        public static float MaxRisk(FormOutline o, out string why)
        {
            why = ""; float m = 0;
            foreach (var r in o.Runs) { float k = BlowoutRisk(r, o, out string w); if (k > m) { m = k; why = w; } }
            return m;
        }

        // ------------------------------------------------------------------ takeoff

        /// <summary>Concrete volume (m³) enclosed by the forms: Σ over ground cells inside of (top of form - subgrade).</summary>
        public static float TakeoffM3(FormOutline o)
        {
            if (o == null || !o.Closed || SiteGround.I == null) return 0f;
            float v = 0f; var g = SiteGround.I;
            SiteGround.ForCellsIn(o.Polygon, (x, z) => v += Mathf.Max(0f, o.TopY - g.CellHeight(x, z)) * SiteGround.Cell * SiteGround.Cell);
            return v;
        }
        public const float YardsPerM3 = 1.30795f;
        public static float TakeoffYards(FormOutline o) => TakeoffM3(o) * YardsPerM3;

        /// <summary>What to order: takeoff + 10% waste, rounded up to 1/4 yard (how ready-mix is batched).</summary>
        public static float RecommendedOrder(FormOutline o) => Mathf.Ceil(TakeoffYards(o) * 1.10f * 4f - 0.01f) / 4f;

        // ------------------------------------------------------------------ poly, oil, strip

        public static string LayPoly(FormOutline o)
        {
            if (o == null || !o.Closed) return "Close the forms first";
            if (o.Poly != null) return "Poly already down";
            int sqft = Mathf.CeilToInt(o.AreaSqFt * 1.25f);   // 6" laps + run up the forms/trench walls
            if (!CTool.TakeStock(PolyId, sqft, "poly sheeting")) return "Not enough poly";
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
            return $"6 mil poly laid ({sqft} sq ft with laps)";
        }

        public static string Oil(FormOutline o)
        {
            if (o == null) return "No forms";
            if (o.Oiled) return "Forms already oiled";
            float sf = 0; foreach (var r in o.Runs) foreach (var b in r.Boards) sf += b.Size.z * b.Size.x / (Units.Foot * Units.Foot);
            if (!CTool.TakeStock(FormOilId, Mathf.CeilToInt(sf), "form oil")) return "Not enough form oil";
            o.Oiled = true;
            return $"Form release oil on {sf:0} sq ft of boards - they'll strip clean";
        }

        /// <summary>Strips the forms once the concrete is at least 24 h old. Oiled boards come back reusable.</summary>
        public static string Strip(FormOutline o)
        {
            if (o == null || o.Stripped) return "No forms to strip";
            var el = o.Pour != null ? o.Pour.Element : null;
            if (el == null) return "Pour and finish the concrete first";
            float age = el.AgeHours;
            if (age < 24f) return $"Too green to strip: concrete is {age:0} h old (wait 24 h)";
            int reused = 0, scrap = 0, stakes = 0;
            var rnd = new System.Random(o.Corners.Count * 7919 + Mathf.RoundToInt(o.TopY * 1000));
            foreach (var r in o.Runs)
            {
                foreach (var b in r.Boards)
                {
                    bool clean = o.Oiled || rnd.NextDouble() < 0.4;
                    bool full = b.Size.z >= b.Def.Size.z - 0.001f;
                    if (clean && full) { Game.Stock.Add(b.Def.Id, 1); reused++; }
                    else { Game.Stock.Scrap.Add((b.Def.Id, b.Size.z)); scrap++; }
                    Object.Destroy(b.gameObject);
                }
                foreach (var s in r.Stakes) { if (rnd.NextDouble() < 0.85) { Game.Stock.Add(StakeId, 1); stakes++; } Object.Destroy(s.gameObject); }
                foreach (var s in r.Braces)
                {
                    if (s.Def.Id == StakeId) { Game.Stock.Add(StakeId, 1); stakes++; } else Game.Stock.Scrap.Add((s.Def.Id, s.Size.z));
                    Object.Destroy(s.gameObject);
                }
                r.Boards.Clear(); r.Stakes.Clear(); r.Braces.Clear();
            }
            foreach (var mk in o.Markers) if (mk != null) Object.Destroy(mk);
            o.Stripped = true;
            o.Pour?.OnFormsStripped();
            return $"Forms stripped: {reused} boards reusable, {scrap} to scrap, {stakes} stakes back" + (o.Oiled ? "" : " (unoiled boards stuck to the concrete)");
        }

        public static FormRun NearestRun(FormOutline o, Vector3 p, out float t)
        {
            t = 0; FormRun best = null; float bd = float.MaxValue;
            if (o == null) return null;
            foreach (var r in o.Runs)
            {
                float tt = Mathf.Clamp(Vector3.Dot(p - r.A, r.Dir), 0, r.Length);
                Vector3 q = r.PointAt(tt);
                float d = CTool.XZ(q - p).magnitude;
                if (d < bd) { bd = d; best = r; t = tt; }
            }
            return bd < 0.6f ? best : null;
        }
    }

    public class FormTool : ConcreteToolBase
    {
        public override string Id => "formkit";
        public override string Name => "Form Setting Kit";

        public override string Hint
        {
            get
            {
                var o = FormSystem.Active;
                if (FormSystem.Building != null) return $"LMB next corner (click first corner to close) · Backspace undo · {FormSystem.Building.Corners.Count} corners";
                if (o == null) return "LMB set first form corner (inside face, snaps to grid; top = laser grade)";
                float risk = FormSystem.MaxRisk(o, out string why);
                return $"LMB stake at aim · F stake all 36\" O.C. · RMB kicker brace · J oil · V poly · X strip · {FormSystem.TakeoffYards(o):0.00} yd³ · blowout risk {(risk <= 0 ? "OK" : Mathf.RoundToInt(risk * 100) + "% (" + why + ")")}";
            }
        }

        public override void Primary()
        {
            if (!CTool.Aim(out var hit)) return;
            var o = FormSystem.Active;
            var part = hit.collider.GetComponentInParent<FormPart>();
            if (FormSystem.Building == null && o != null && part != null && part.Outline == o)
            {
                var r = FormSystem.NearestRun(o, hit.point, out float t);
                if (r != null && FormSystem.AddStake(r, t, o)) Game.Toast("Stake driven and nailed to the form");
                return;
            }
            Game.Toast(FormSystem.AddCorner(hit.point));
        }

        public override void Secondary()
        {
            var o = FormSystem.Active;
            if (o == null || !CTool.Aim(out var hit)) return;
            var r = FormSystem.NearestRun(o, hit.point, out float t);
            if (r != null && FormSystem.AddBrace(r, t, o)) Game.Toast("Kicker brace set");
        }

        public override void Tick()
        {
            var o = FormSystem.Active;
            if (GInput.GetKeyDown(KeyCode.Backspace)) FormSystem.UndoCorner();
            if (o == null) return;
            if (GInput.GetKeyDown(KeyCode.F)) Game.Toast($"Drove {FormSystem.AutoStake(o)} stakes");
            if (GInput.GetKeyDown(KeyCode.J)) Game.Toast(FormSystem.Oil(o));
            if (GInput.GetKeyDown(KeyCode.V)) Game.Toast(FormSystem.LayPoly(o));
            if (GInput.GetKeyDown(KeyCode.X)) Game.Toast(FormSystem.Strip(o));
        }
    }
}
