using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public class WallOpening
    {
        public PieceDefinition Product;   // window or door from the catalog
        public float CenterIn;            // along the wall from its start, inches
        public bool IsDoor => Product != null && Product.Category == PieceCategory.Door;
        /// <summary>Rough opening: windows unit + 1/2", prehung doors unit + 2" wide x 82-1/2" tall.</summary>
        public float RoughWidthIn => IsDoor ? Units.ToInches(Product.Size.x) + 2f : Units.ToInches(Product.Size.x) + 0.5f;
        public float RoughHeightIn => IsDoor ? Units.ToInches(Product.Size.y) + 2.5f : Units.ToInches(Product.Size.y) + 0.5f;
        public const float HeaderBottomIn = 82.5f; // top of every rough opening above the subfloor
        public float SillTopIn => IsDoor ? 0f : HeaderBottomIn - RoughHeightIn;
    }

    public class WallSpec
    {
        public string Name = "Wall";
        public float LengthIn;
        public float DepthIn = 3.5f;          // 2x4 wall
        public string StudSize = "2x4";
        public float StudIn = 92.625f;        // precut stud
        public float OCIn = 16f;
        public bool CornerBackerStart, CornerBackerEnd; // California-corner backers where an end wall butts in
        public readonly List<WallOpening> Openings = new List<WallOpening>();
        public float PlateHeightIn => 1.5f + StudIn + 1.5f;                 // bottom plate + stud + top plate
    }

    public enum WallState { Flat, Standing, Braced }

    /// <summary>A wall framed as a unit. Its children (plates, studs, headers, sheathing) move together while tilting.
    /// Local frame: X along the wall, Y up from the deck, Z from the exterior face (0) to the interior face (depth).</summary>
    public class WallAssembly : MonoBehaviour
    {
        public static readonly List<WallAssembly> All = new List<WallAssembly>();
        public WallSpec Spec;
        public WallState State = WallState.Flat;
        public Vector3 StandingPos;
        public Quaternion StandingRot;
        public float Tilt { get; private set; } = 90f;
        public readonly List<RoughOpening> Openings = new List<RoughOpening>();
        public float Depth => Units.In(Spec.DepthIn);
        public float Length => Units.In(Spec.LengthIn);

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>0 = standing, 90 = lying flat on the deck (exterior face up). Pivots on the interior bottom edge.</summary>
        public void SetTilt(float deg)
        {
            Tilt = Mathf.Clamp(deg, 0f, 90f);
            transform.SetPositionAndRotation(StandingPos + Vector3.up * Depth * Mathf.Sin(Tilt * Mathf.Deg2Rad), StandingRot * Quaternion.Euler(Tilt, 0f, 0f));
            SupportGraph.Dirty = true;
        }

        public IEnumerator TiltUpRoutine(float seconds = 2.5f)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds; k = k * k * (3 - 2 * k);
                SetTilt(90f * (1f - k));
                yield return null;
            }
            SetTilt(0f);
        }

        /// <summary>How far out of plumb the wall is (inches over its height) – after tilting it's eyeballed; bracing fixes it.</summary>
        public float OutOfPlumbIn => State == WallState.Braced ? 0f : State == WallState.Standing ? 0.25f : 999f;

        public Vector3 LocalToWorld(Vector3 local) => transform.TransformPoint(local);
    }

    /// <summary>Plans a wall built flat on the deck, then tilted up, plumbed and braced.</summary>
    public static class WallPlan
    {
        static float I(float inch) => inch * Units.Inch;
        static readonly Vector3 X = Vector3.right, Y = Vector3.up, Z = Vector3.forward;

        public static WallAssembly Create(WallSpec spec, Vector3 standingPos, Quaternion standingRot)
        {
            var go = new GameObject("Wall: " + spec.Name);
            var w = go.AddComponent<WallAssembly>();
            w.Spec = spec; w.StandingPos = standingPos; w.StandingRot = standingRot;
            w.SetTilt(90f);
            return w;
        }

        struct Stick { public float X0, X1, Y0, Y1; public MemberRole Role; public string Label; }

        /// <summary>Everything the wall is made of, in wall-local inches (studs vertical).</summary>
        static List<Stick> Layout(WallSpec s, out List<float> marks)
        {
            var list = new List<Stick>();
            marks = new List<float>();
            float L = s.LengthIn, top = 1.5f + s.StudIn;
            // keep-out zones for openings (king to king)
            var zones = new List<(float a, float b)>();
            foreach (var o in s.Openings)
            {
                var hd = StructuralRules.HeaderFor(o.RoughWidthIn);
                float a = o.CenterIn - o.RoughWidthIn / 2, b = o.CenterIn + o.RoughWidthIn / 2;
                zones.Add((a - 1.5f * hd.jacks - 1.5f, b + 1.5f * hd.jacks + 1.5f));
            }
            bool InZone(float x0, float x1) { foreach (var z in zones) if (x1 > z.a - 0.01f && x0 < z.b + 0.01f) return true; return false; }

            list.Add(new Stick { X0 = 0, X1 = 1.5f, Y0 = 1.5f, Y1 = top, Role = MemberRole.Stud, Label = "end stud" });
            for (float c = s.OCIn; c + 0.75f < L - 1.5f - 0.5f; c += s.OCIn)
            {
                marks.Add(c);
                if (InZone(c - 0.75f, c + 0.75f)) continue;
                if (s.CornerBackerStart && c - 0.75f < 5.1f) continue;
                if (s.CornerBackerEnd && c + 0.75f > L - 5.1f) continue;
                list.Add(new Stick { X0 = c - 0.75f, X1 = c + 0.75f, Y0 = 1.5f, Y1 = top, Role = MemberRole.Stud, Label = "stud @ " + c + "\"" });
            }
            list.Add(new Stick { X0 = L - 1.5f, X1 = L, Y0 = 1.5f, Y1 = top, Role = MemberRole.Stud, Label = "end stud" });

            foreach (var o in s.Openings)
            {
                var hd = StructuralRules.HeaderFor(o.RoughWidthIn);
                float a = o.CenterIn - o.RoughWidthIn / 2, b = o.CenterIn + o.RoughWidthIn / 2;
                float hb = WallOpening.HeaderBottomIn;
                float depth = hd.depthIn;
                if (top - (hb + depth) < 3f && top - (hb + depth) > 0.01f && depth < 11.25f) depth = 11.25f; // no room for cripples: fill with a 2x12
                for (int j = 0; j < hd.jacks; j++)
                {
                    list.Add(new Stick { X0 = a - 1.5f * (j + 1), X1 = a - 1.5f * j, Y0 = 1.5f, Y1 = hb, Role = MemberRole.JackStud, Label = "jack stud" });
                    list.Add(new Stick { X0 = b + 1.5f * j, X1 = b + 1.5f * (j + 1), Y0 = 1.5f, Y1 = hb, Role = MemberRole.JackStud, Label = "jack stud" });
                }
                float ka = a - 1.5f * hd.jacks, kb = b + 1.5f * hd.jacks;
                list.Add(new Stick { X0 = ka - 1.5f, X1 = ka, Y0 = 1.5f, Y1 = top, Role = MemberRole.KingStud, Label = "king stud" });
                list.Add(new Stick { X0 = kb, X1 = kb + 1.5f, Y0 = 1.5f, Y1 = top, Role = MemberRole.KingStud, Label = "king stud" });
                list.Add(new Stick { X0 = ka, X1 = kb, Y0 = hb, Y1 = hb + depth, Role = MemberRole.Header, Label = "header " + hd.lumber + " (" + Mathf.RoundToInt(o.RoughWidthIn) + "\" span)" });
                if (top - (hb + depth) >= 3f)
                    foreach (var m in marks)
                        if (m - 0.75f > ka && m + 0.75f < kb)
                            list.Add(new Stick { X0 = m - 0.75f, X1 = m + 0.75f, Y0 = hb + depth, Y1 = top, Role = MemberRole.Cripple, Label = "top cripple" });
                if (!o.IsDoor)
                {
                    float sill = o.SillTopIn;
                    list.Add(new Stick { X0 = a, X1 = b, Y0 = sill - 1.5f, Y1 = sill, Role = MemberRole.RoughSill, Label = "rough sill" });
                    var cx = new List<float> { a + 0.75f, b - 0.75f };
                    foreach (var m in marks) if (m - 0.75f > a + 1.5f && m + 0.75f < b - 1.5f) cx.Add(m);
                    foreach (var c in cx)
                        list.Add(new Stick { X0 = c - 0.75f, X1 = c + 0.75f, Y0 = 1.5f, Y1 = sill - 1.5f, Role = MemberRole.Cripple, Label = "sill cripple" });
                }
            }
            if (s.CornerBackerStart) list.Add(new Stick { X0 = 1.5f, X1 = 5f, Y0 = 1.5f, Y1 = top, Role = MemberRole.CornerBacker, Label = "corner backer (flat)" });
            if (s.CornerBackerEnd) list.Add(new Stick { X0 = L - 5f, X1 = L - 1.5f, Y0 = 1.5f, Y1 = top, Role = MemberRole.CornerBacker, Label = "corner backer (flat)" });
            list.Sort((p, q) => p.X0.CompareTo(q.X0));
            return list;
        }

        /// <summary>Job: chalk line, plates + layout, every stick, square it, tilt up, nail off, plumb + brace, cut the door plate.</summary>
        public static FramingJob Frame(WallAssembly w)
        {
            var s = w.Spec;
            var root = w.transform;
            var job = FramingJob.Create("Frame " + s.Name, root);
            float d = s.DepthIn, L = s.LengthIn;
            var sticks = Layout(s, out var marks);
            System.Func<(Vector3, Vector3, Quaternion)> G(Vector3 cIn, Vector3 sizeIn) =>
                () => (root.TransformPoint(cIn * Units.Inch), sizeIn * Units.Inch, root.rotation);

            job.Add("Snap a chalk line for the bottom plate", ToolSets.Chalk, () =>
            {
                // the line marks the interior edge of the plate on the deck: drawn in world space, it stays on the deck
                var a = w.StandingPos + w.StandingRot * new Vector3(0, 0.0015f, I(d));
                var b = w.StandingPos + w.StandingRot * new Vector3(I(L), 0.0015f, I(d));
                ChalkLine.SnapWorld(a, b);
            }, G(new Vector3(L / 2, 0.75f, d / 2), new Vector3(L, 1.5f, d)));

            string plateStock = FloorPlan.PickStock(s.StudSize, I(L));
            job.Add("Cut plates to " + Units.Format(I(L)) + ", tack together, mark layout (X at " + s.OCIn + "\" O.C.)", ToolSets.Layout, () =>
            {
                var bp = Framer.Make(plateStock, I(L), root, new Vector3(L / 2, 0.75f, d / 2) * Units.Inch, X, Y, MemberRole.BottomPlate, Connector.Nailed, s.Name);
                var tp = Framer.Make(plateStock, I(L), root, new Vector3(L / 2, 1.5f + s.StudIn + 0.75f, d / 2) * Units.Inch, X, Y, MemberRole.TopPlate, Connector.Nailed, s.Name);
                LayoutMarks.Draw(bp.transform, tp.transform, root, s, marks, sticks.ConvertAll(k => (k.X0, k.X1, k.Role)));
            }, G(new Vector3(L / 2, 0.75f, d / 2), new Vector3(L, 1.5f, d))).Use(plateStock, 2);

            foreach (var k in sticks)
            {
                var st = k;
                float len = st.Y1 - st.Y0, wid = st.X1 - st.X0;
                var c = new Vector3((st.X0 + st.X1) / 2, (st.Y0 + st.Y1) / 2, d / 2);
                if (st.Role == MemberRole.Header)
                {
                    string ply = FloorPlan.PickStock("2x" + HeaderNominal(len), I(wid));
                    job.Add("Build + set " + st.Label + " (two plies + 1/2\" ply spacer)", ToolSets.Fasten, () =>
                    {
                        float plyT = 1.5f;
                        Framer.Make(ply, I(wid), root, new Vector3(c.x, c.y, plyT / 2) * Units.Inch, X, Z, MemberRole.Header, Connector.Nailed, s.Name);
                        Framer.Sheet("ply12", I(len), I(wid), root, new Vector3(c.x, c.y, d / 2) * Units.Inch, X, Z, MemberRole.Header, Connector.Nailed, s.Name);
                        Framer.Make(ply, I(wid), root, new Vector3(c.x, c.y, d - plyT / 2) * Units.Inch, X, Z, MemberRole.Header, Connector.Nailed, s.Name);
                    }, G(c, new Vector3(wid, len, d))).Use(ply, 2).Use("ply12", 1).Nails = 16;
                }
                else if (st.Role == MemberRole.RoughSill)
                {
                    string stock = FloorPlan.PickStock(s.StudSize, I(wid));
                    job.Add("Nail " + st.Label, ToolSets.Fasten, () => Framer.Make(stock, I(wid), root, c * Units.Inch, X, Y, st.Role, Connector.Nailed, s.Name),
                        G(c, new Vector3(wid, 1.5f, d))).Use(stock).Nails = 4;
                }
                else if (st.Role == MemberRole.CornerBacker)
                {
                    string stock = FloorPlan.PickStock(s.StudSize, I(len));
                    var cb = new Vector3(c.x, c.y, d - 0.75f);
                    job.Add("Nail " + st.Label, ToolSets.Fasten, () => Framer.Make(stock, I(len), root, cb * Units.Inch, Y, Z, st.Role, Connector.Nailed, s.Name),
                        G(cb, new Vector3(wid, len, 1.5f))).Use(stock).Nails = 8;
                }
                else
                {
                    string stock = FloorPlan.PickStock(s.StudSize, I(len));
                    job.Add("Nail " + st.Label + " (2 x 16d through each plate)", ToolSets.Fasten, () => Framer.Make(stock, I(len), root, c * Units.Inch, Y, X, st.Role, Connector.Nailed, s.Name),
                        G(c, new Vector3(wid, len, d))).Use(stock).Nails = st.Role == MemberRole.JackStud ? 8 : 4;
                }
            }

            job.Add("Square the wall: diagonals equal", ToolSets.Layout, null, G(new Vector3(L / 2, 48, d / 2), new Vector3(L, 1, 1)));
            job.Add("Tilt the wall up (lift together)", ToolSets.Hands, () =>
            {
                if (Application.isPlaying && !FramingJobRunner.Instant) FramingJobRunner.Run(w.TiltUpRoutine()); else w.SetTilt(0f);
                w.State = WallState.Standing;
            }, G(new Vector3(L / 2, 48, d / 2), new Vector3(L, 96, d)));
            job.Add("Nail the bottom plate to the deck (16d @ 16\" into joists/rim)", ToolSets.Fasten, null,
                () => (w.StandingPos + w.StandingRot * (new Vector3(L / 2, 0.75f, d / 2) * Units.Inch), new Vector3(L, 1.5f, d) * Units.Inch, w.StandingRot)).Nails = Mathf.CeilToInt(L / 16f);
            job.Add("Plumb with the 4' level and brace (2x4 kicker to a deck block)", ToolSets.Level.Length > 0 ? new[] { "level", "nailer", "hammer" } : ToolSets.Fasten, () =>
            {
                w.SetTilt(0f);
                AddBrace(w);
                w.State = WallState.Braced;
            }, () => (w.StandingPos + w.StandingRot * (new Vector3(L / 2, 48, d + 24) * Units.Inch), new Vector3(3.5f, 96, 30) * Units.Inch, w.StandingRot))
                .Use(FloorPlan.PickStock("2x4", I(120f))).Nails = 6;

            foreach (var o in s.Openings)
                if (o.IsDoor)
                {
                    var op = o;
                    job.Add("Cut the bottom plate out of the door opening", ToolSets.Saw, () => CutDoorPlate(w, op),
                        () => (w.StandingPos + w.StandingRot * (new Vector3(op.CenterIn, 0.75f, d / 2) * Units.Inch), new Vector3(op.RoughWidthIn, 1.5f, d) * Units.Inch, w.StandingRot));
                }
            job.Completed += () =>
            {
                foreach (var o in s.Openings) RoughOpening.Create(w, o);
            };
            return job;
        }

        static string HeaderNominal(float depthIn) => depthIn <= 5.6f ? "6" : depthIn <= 7.3f ? "8" : depthIn <= 9.3f ? "10" : "12";

        static void AddBrace(WallAssembly w)
        {
            // Diagonal 2x4 from high on a stud near the wall's middle down to a 2x4 block nailed to the deck, ~45°.
            var s = w.Spec;
            // beside a layout stud near the middle, clear of rough openings
            float x = Mathf.Round(s.LengthIn / 2f / s.OCIn) * s.OCIn + 1.5f;
            for (int k = 0; k < 12; k++)
            {
                float cand = (Mathf.Round(s.LengthIn / 2f / s.OCIn) + ((k % 2 == 0) ? k / 2 : -(k / 2 + 1))) * s.OCIn + 1.5f;
                if (cand < 8f || cand > s.LengthIn - 8f) continue;
                bool clear = true;
                foreach (var o in s.Openings) if (Mathf.Abs(cand - o.CenterIn) < o.RoughWidthIn / 2 + 8f) clear = false;
                if (clear) { x = cand; break; }
            }
            float d = s.DepthIn;
            Vector3 top = new Vector3(x, 72f, d), foot = new Vector3(x, 0.75f, d + 66f);
            Vector3 dir = (top - foot).normalized;
            float len = (top - foot).magnitude;
            var br = Framer.Make("2x4x10", I(len), w.transform, ((top + foot) / 2 + new Vector3(0, 0, 0.75f) * 0) * Units.Inch, dir, X, MemberRole.Brace, Connector.Nailed, s.Name);
            br.transform.SetParent(null, true); // the brace belongs to the deck, not the wall
            Framer.Make("2x4x925", I(12f), null, w.transform.TransformPoint(new Vector3(x, 0.75f, d + 70f) * Units.Inch), w.transform.rotation * Z, w.transform.rotation * Y, MemberRole.Blocking, Connector.Nailed, "brace block");
        }

        static void CutDoorPlate(WallAssembly w, WallOpening o)
        {
            foreach (var m in w.GetComponentsInChildren<StructuralMember>())
            {
                if (m.Role != MemberRole.BottomPlate) continue;
                var p = m.Piece;
                float a = Units.In(o.CenterIn - o.RoughWidthIn / 2), b = Units.In(o.CenterIn + o.RoughWidthIn / 2);
                Vector3 lp = m.transform.localPosition;
                float x0 = lp.x - p.Size.z / 2, x1 = lp.x + p.Size.z / 2;
                if (b <= x0 || a >= x1) continue;
                string id = p.Def.Id;
                Object.Destroy(m.gameObject);
                m.enabled = false;
                if (a - x0 > 0.01f) Framer.Make(id, a - x0, w.transform, new Vector3((x0 + a) / 2, lp.y, lp.z), X, Y, MemberRole.BottomPlate, Connector.Nailed, w.Spec.Name);
                if (x1 - b > 0.01f) Framer.Make(id, x1 - b, w.transform, new Vector3((b + x1) / 2, lp.y, lp.z), X, Y, MemberRole.BottomPlate, Connector.Nailed, w.Spec.Name);
                Game.Stock.Scrap.Add((id, b - a));
                break;
            }
        }

        // ------------------------------------------------------------------ building-level helpers

        /// <summary>The four exterior walls of a rectangular footprint: front/back (long, full length, with corner backers),
        /// left/right end walls between them. Openings are given per wall index (0 front, 1 right, 2 back, 3 left).</summary>
        public static WallAssembly[] ExteriorWalls(Footprint fp, List<WallOpening>[] openings = null)
        {
            float d = fp.WallDepth, L = fp.Length, W = fp.Width, y = fp.DeckTopY;
            float Lin = Units.ToInches(L), Win = Units.ToInches(W), din = Units.ToInches(d);
            var r = fp.Rotation;
            var specs = new[]
            {
                (new WallSpec { Name = "Front wall", LengthIn = Lin, DepthIn = din, CornerBackerStart = true, CornerBackerEnd = true }, fp.World(0, y, 0), r),
                (new WallSpec { Name = "Right wall", LengthIn = Win - 2 * din, DepthIn = din }, fp.World(L, y, d), r * Quaternion.Euler(0, -90, 0)),
                (new WallSpec { Name = "Back wall", LengthIn = Lin, DepthIn = din, CornerBackerStart = true, CornerBackerEnd = true }, fp.World(L, y, W), r * Quaternion.Euler(0, 180, 0)),
                (new WallSpec { Name = "Left wall", LengthIn = Win - 2 * din, DepthIn = din }, fp.World(0, y, W - d), r * Quaternion.Euler(0, 90, 0)),
            };
            var walls = new WallAssembly[4];
            for (int i = 0; i < 4; i++)
            {
                if (openings != null && i < openings.Length && openings[i] != null) specs[i].Item1.Openings.AddRange(openings[i]);
                walls[i] = Create(specs[i].Item1, specs[i].Item2, specs[i].Item3);
            }
            return walls;
        }

        /// <summary>Double top plate: end-wall caps run the full width and lap over the long walls' top plates at the corners.</summary>
        public static FramingJob CapPlates(WallAssembly[] walls)
        {
            var job = FramingJob.Create("Double top plates");
            foreach (var w in walls)
            {
                var ww = w;
                var s = w.Spec;
                bool endWall = !s.CornerBackerStart && !s.CornerBackerEnd;
                float x0 = endWall ? -s.DepthIn : s.DepthIn, x1 = endWall ? s.LengthIn + s.DepthIn : s.LengthIn - s.DepthIn;
                float len = x1 - x0;
                string stock = FloorPlan.PickStock(s.StudSize, I(len));
                var c = new Vector3((x0 + x1) / 2, s.PlateHeightIn + 0.75f, s.DepthIn / 2) * Units.Inch;
                job.Add("Cap plate on " + s.Name + (endWall ? " (laps the corners)" : ""), ToolSets.Fasten,
                    () => Framer.Make(stock, I(len), ww.transform, c, X, Y, MemberRole.CapPlate, Connector.Nailed, s.Name),
                    () => (ww.transform.TransformPoint(c), new Vector3(I(len), I(1.5f), I(s.DepthIn)), ww.transform.rotation * Quaternion.LookRotation(X, Y))).Use(stock).Nails = Mathf.CeilToInt(len / 16f) * 2;
            }
            return job;
        }

        /// <summary>7/16" OSB on the exterior face, sheets vertical, seams on stud centers, rough openings cut out.
        /// End walls also cover the long walls' end framing at the corners.</summary>
        public static FramingJob Sheathe(WallAssembly[] walls, float bottomBelowDeckIn = 0f)
        {
            var job = FramingJob.Create("Wall sheathing");
            const float sheetIn = 48f, tIn = 0.4375f;
            foreach (var w in walls)
            {
                var ww = w;
                var s = w.Spec;
                bool endWall = !s.CornerBackerStart && !s.CornerBackerEnd;
                float xa = endWall ? -s.DepthIn - tIn : 0f, xb = endWall ? s.LengthIn + s.DepthIn + tIn : s.LengthIn;
                float ytop = s.PlateHeightIn + 1.5f, ybot = ytop - 96f;
                var cols = new List<(float a, float b)>();
                if (xa < 0) cols.Add((xa, 0f));
                for (float a = Mathf.Max(0f, xa); a < xb - 0.01f; a += sheetIn) cols.Add((a, Mathf.Min(xb, a + sheetIn)));
                // merge a final sliver into the previous column if it still fits in one sheet
                var holes = new List<Rect>();
                foreach (var o in s.Openings) holes.Add(new Rect(o.CenterIn - o.RoughWidthIn / 2, o.SillTopIn, o.RoughWidthIn, WallOpening.HeaderBottomIn - o.SillTopIn));
                foreach (var col in cols)
                {
                    foreach (var piece in Subtract(new Rect(col.a, ybot, col.b - col.a, ytop - ybot), holes))
                    {
                        var pr = piece;
                        if (pr.width < 0.5f || pr.height < 0.5f) continue;
                        var c = new Vector3(pr.center.x, pr.center.y, -tIn / 2) * Units.Inch;
                        float gw = pr.width - 0.125f, gh = pr.height - 0.0625f;
                        job.Add("Sheathe " + s.Name + " (" + Mathf.RoundToInt(pr.width) + "\" x " + Mathf.RoundToInt(pr.height) + "\")", ToolSets.Fasten,
                            () => Framer.Sheet("osb716", I(gw), I(gh), ww.transform, c, Y, -Z, MemberRole.WallSheathing, Connector.Nailed, s.Name),
                            () => (ww.transform.TransformPoint(c), new Vector3(I(gw), I(tIn), I(gh)), ww.transform.rotation * Quaternion.LookRotation(Y, -Z)))
                            .Use("osb716").Nails = 24;
                    }
                }
            }
            return job;
        }

        /// <summary>Splits a rectangle into pieces that avoid the holes (horizontal bands between hole edges).</summary>
        public static List<Rect> Subtract(Rect r, List<Rect> holes)
        {
            var ys = new List<float> { r.yMin, r.yMax };
            foreach (var h in holes) if (h.Overlaps(r)) { ys.Add(Mathf.Clamp(h.yMin, r.yMin, r.yMax)); ys.Add(Mathf.Clamp(h.yMax, r.yMin, r.yMax)); }
            ys.Sort();
            var outRects = new List<Rect>();
            for (int i = 0; i + 1 < ys.Count; i++)
            {
                float y0 = ys[i], y1 = ys[i + 1];
                if (y1 - y0 < 0.01f) continue;
                var xs = new List<(float a, float b)> { (r.xMin, r.xMax) };
                foreach (var h in holes)
                {
                    if (h.yMax <= y0 + 0.001f || h.yMin >= y1 - 0.001f) continue;
                    var next = new List<(float, float)>();
                    foreach (var seg in xs)
                    {
                        if (h.xMax <= seg.a || h.xMin >= seg.b) { next.Add(seg); continue; }
                        if (h.xMin > seg.a) next.Add((seg.a, h.xMin));
                        if (h.xMax < seg.b) next.Add((h.xMax, seg.b));
                    }
                    xs = next;
                }
                foreach (var seg in xs) outRects.Add(new Rect(seg.a, y0, seg.b - seg.a, y1 - y0));
            }
            return outRects;
        }
    }

    /// <summary>Runs coroutines for plain C# job steps (wall tilt animation). Instant = true in tests skips animation.</summary>
    public class FramingJobRunner : MonoBehaviour
    {
        public static bool Instant;
        static FramingJobRunner _i;
        public static void Run(IEnumerator r)
        {
            if (_i == null) _i = new GameObject("FramingJobRunner").AddComponent<FramingJobRunner>();
            _i.StartCoroutine(r);
        }
    }
}
