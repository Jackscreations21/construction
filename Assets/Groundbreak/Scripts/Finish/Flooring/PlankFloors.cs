using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Luxury vinyl plank (click-lock, floating): 7" x 48" planks over 2 mm foam, rows along the room's long
    /// direction, staggered end joints (≥ 6", random, last row's offcut starts the next row), 1/4" expansion gap.</summary>
    public static class Lvp
    {
        public static readonly float PlankW = Units.In(7f), PlankL = Units.In(48f), Thick = 0.005f, UnderlayT = 0.002f;

        /// <summary>Installs LVP over <paramref name="area"/> (u,v on the floor face, to the finished wall faces). Holes default to
        /// <see cref="Flooring.AutoHoles"/> (walls, cabinets, other floors). Needs the pull bar / tapping block.</summary>
        public static FloorInstall Install(FramePlane floor, Rect area, IList<Rect> holes = null, string color = "oak", float gap = -1f, int seed = 1)
        {
            if (floor == null || !floor.IsFloor) { Game.Toast("Aim at the subfloor"); return null; }
            if (!FinishStock.Require("pull-bar", "tap and pull the click joints tight")) return null;
            if (gap < 0f) gap = Units.In(0.25f);
            if (holes == null) holes = Flooring.AutoHoles(floor, area, gap);
            var spec = new PlankFloors.Spec
            {
                Type = FloorType.Lvp, W = PlankW, FixedL = PlankL, Thick = Thick, UnderT = UnderlayT, Bevel = 0.0009f, Gap = gap,
                AlongU = area.width >= area.height, MinPiece = Units.In(8f), MinStagger = Units.In(6f), Seed = seed,
                Mat = InteriorMaterials.LvpPrefix + color, UnderMat = InteriorMaterials.Underlay,
                Tints = new[] { Color.white, new Color(0.93f, 0.91f, 0.88f), new Color(1.05f, 1.03f, 1.0f), new Color(0.88f, 0.86f, 0.83f) },
            };
            float sqft = (area.width * area.height) / (Units.Foot * Units.Foot);
            int boxes = Mathf.CeilToInt(sqft * 1.08f / 23.33f);
            if (!FinishStock.Has("fin-lvp-" + color, boxes)) { Game.Toast($"Need about {boxes} boxes of LVP ({sqft:F0} sq ft + waste) on site"); return null; }
            if (!InteriorSupplies.UseAmount("fin-lvp-underlay", sqft, 100f)) return null;
            var f = PlankFloors.Lay(floor, area, holes, spec, "fin-lvp-" + color);
            if (f != null) Game.Toast($"LVP down: {f.Pieces} pieces ({f.FreshPieces} planks), shortest end-joint stagger {Units.Format(f.MinStagger)}");
            return f;
        }
    }

    /// <summary>3/4" x 2-1/4" solid red oak strip: rosin paper (or 15 lb felt), strips perpendicular to the joists, random
    /// lengths nested for ≥ 6" stagger, first/last rows face-nailed, the rest blind-nailed through the tongue every 8-10"
    /// with the flooring nailer (2" cleats). 1/2" expansion gap.</summary>
    public static class Hardwood
    {
        public static readonly float StripW = Units.In(2.25f), Thick = Units.In(0.75f);

        public static FloorInstall Install(FramePlane floor, Rect area, IList<Rect> holes = null, float gap = -1f, int seed = 2)
        {
            if (floor == null || !floor.IsFloor) { Game.Toast("Aim at the subfloor"); return null; }
            if (!FinishStock.Require("flooring-nailer", "blind-nail the strips")) return null;
            if (!FinishStock.Require("finish-nailer", "face-nail the first and last rows")) return null;
            if (gap < 0f) gap = Units.In(0.5f);
            if (holes == null) holes = Flooring.AutoHoles(floor, area, gap);
            Vector3 joists = Flooring.JoistDirection(floor, area);
            bool alongU = Mathf.Abs(Vector3.Dot(joists, floor.U)) < 0.5f; // strips run across the joists
            float sqft = (area.width * area.height) / (Units.Foot * Units.Foot);
            string paper = FinishStock.Has("fin-rosin") || FinishStock.Unlimited ? "fin-rosin" : "fin-felt15";
            if (!InteriorSupplies.UseAmount(paper, sqft, paper == "fin-rosin" ? 500f : 432f)) return null;
            if (!InteriorSupplies.UseAmount("fin-hw-redoak", sqft * 1.07f, 20f)) return null;
            var spec = new PlankFloors.Spec
            {
                Type = FloorType.Hardwood, W = StripW, FixedL = 0f, Thick = Thick, UnderT = 0.0004f, Bevel = 0.0005f, Gap = gap,
                AlongU = alongU, MinPiece = Units.In(10f), MinStagger = Units.In(6f), Seed = seed,
                Mat = InteriorMaterials.HardwoodPrefix + "redoak", UnderMat = paper == "fin-rosin" ? InteriorMaterials.Rosin : InteriorMaterials.Underlay,
                Tints = new[] { new Color(0.92f, 0.9f, 0.88f), new Color(0.8f, 0.72f, 0.66f), Color.white, new Color(0.88f, 0.78f, 0.7f), new Color(0.95f, 0.86f, 0.78f) },
            };
            var f = PlankFloors.Lay(floor, area, holes, spec, "fin-hw-redoak");
            if (f == null) return null;
            InteriorSupplies.UseFasteners("fin-cleats", f.Nails);
            InteriorSupplies.UseFasteners("fin-nails-15ga", f.FaceNails);
            Game.Toast($"Hardwood down: {f.Pieces} strips, {f.Nails} cleats blind-nailed, {f.FaceNails} face nails at the walls");
            return f;
        }
    }

    /// <summary>Row layout + mesh for plank floors (LVP, hardwood).</summary>
    public static class PlankFloors
    {
        public class Spec
        {
            public FloorType Type; public float W, FixedL, Thick, UnderT, Bevel, Gap, MinPiece, MinStagger; public bool AlongU; public int Seed;
            public string Mat, UnderMat; public Color[] Tints;
        }

        static float RandomStrip(System.Random r)
        {
            // nested random-length bundle: 12" to 84", mostly 2'-4'
            float x = (float)(r.NextDouble() + r.NextDouble()) * 0.5f;
            return Units.In(Mathf.Round(Mathf.Lerp(12f, 84f, x * x * 1.2f > 1f ? 1f : x * x * 1.2f + 0.08f)));
        }

        public static FloorInstall Lay(FramePlane floor, Rect area0, IList<Rect> holes, Spec s, string name)
        {
            var rnd = new System.Random(s.Seed);
            Rect area = Rect.MinMaxRect(area0.xMin + s.Gap, area0.yMin + s.Gap, area0.xMax - s.Gap, area0.yMax - s.Gap);
            if (area.width < s.W || area.height < s.W) { Game.Toast("Area too small for flooring"); return null; }
            var rowHoles = Flooring.ToRowSpace(holes, s.AlongU);
            float a0 = s.AlongU ? area.xMin : area.yMin, a1 = s.AlongU ? area.xMax : area.yMax;
            float b0 = s.AlongU ? area.yMin : area.xMin, b1 = s.AlongU ? area.yMax : area.xMax;
            var widths = Flooring.RowWidths(b1 - b0, s.W);

            var mb = new MeshBuilder();
            int nt = s.Tints.Length;
            mb.MinSubmeshCount = nt + 1;
            int pieces = 0, fresh = 0, cuts = 0, nails = 0, faceNails = 0;
            float minStag = float.MaxValue;
            var prevJoints = new List<float>();
            float offcut = 0f;
            float yb = s.UnderT, yt = s.UnderT + s.Thick, endGap = 0.0003f;
            float b = b0;
            for (int r = 0; r < widths.Count; r++)
            {
                float bw = widths[r], rb0 = b, rb1 = b + bw;
                b = rb1;
                if (bw < s.W - 0.001f) cuts++; // ripped row
                // choose the run of lengths for this row
                List<float> lens = null; float bestScore = -1f; bool usedOffcut = false;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    var L = new List<float>();
                    bool useOff = attempt == 0 && offcut >= s.MinPiece && r > 0;
                    float x = 0f, span = a1 - a0;
                    float first = r == 0 && s.FixedL > 0f ? s.FixedL
                                : useOff ? offcut
                                : s.FixedL > 0f ? Mathf.Lerp(s.MinPiece, s.FixedL, (float)rnd.NextDouble())
                                : RandomStrip(rnd);
                    while (x < span - 1e-4f)
                    {
                        float l = L.Count == 0 ? first : (s.FixedL > 0f ? s.FixedL : RandomStrip(rnd));
                        if (x + l > span) l = span - x;
                        L.Add(l); x += l;
                    }
                    // score: stagger vs previous row joints and the last piece length
                    float stag = float.MaxValue; float acc = a0;
                    for (int i = 0; i + 1 < L.Count; i++) { acc += L[i]; foreach (var pj in prevJoints) stag = Mathf.Min(stag, Mathf.Abs(acc - pj)); }
                    bool okLast = L[L.Count - 1] >= s.MinPiece || L.Count == 1;
                    float score = (okLast ? 1f : 0f) + Mathf.Min(stag, Units.In(24f));
                    if (stag >= s.MinStagger && okLast) { lens = L; usedOffcut = useOff; break; }
                    if (score > bestScore) { bestScore = score; lens = L; usedOffcut = useOff; }
                }
                // place pieces
                var joints = new List<float>();
                float pos = a0;
                for (int i = 0; i < lens.Count; i++)
                {
                    float l = lens[i];
                    bool full = s.FixedL > 0f ? Mathf.Abs(l - s.FixedL) < 1e-4f : true;
                    if (i == 0 && usedOffcut) { /* from the last row's offcut */ }
                    else fresh++;
                    if (!full || i == lens.Count - 1) cuts++;
                    if (i == lens.Count - 1 && s.FixedL > 0f) offcut = s.FixedL - l - 0.003f;
                    else if (i == lens.Count - 1) offcut = 0f;
                    float p0 = pos, p1 = pos + l;
                    pos = p1;
                    if (i + 1 < lens.Count) joints.Add(p1);
                    // notch the plank around holes (2D), keep a hairline end joint where pieces meet
                    var rect = Rect.MinMaxRect(p0, rb0, p1, rb1);
                    var cells = PanelMesh.Cells(rect, rowHoles);
                    if (cells.Count != 1 || cells[0].width < l - 1e-4f || cells[0].height < bw - 1e-4f) cuts++;
                    mb.UvOffset = new Vector2((float)rnd.NextDouble() * 3f, (float)rnd.NextDouble() * 3f);
                    int sub = rnd.Next(nt);
                    bool any = false;
                    foreach (var cell in cells)
                    {
                        if (cell.width < 0.004f || cell.height < 0.004f) continue;
                        float e0 = cell.xMin + (Mathf.Abs(cell.xMin - p0) < 1e-4f && p0 > a0 + 1e-4f ? endGap * 0.5f : 0f);
                        float e1 = cell.xMax - (Mathf.Abs(cell.xMax - p1) < 1e-4f && p1 < a1 - 1e-4f ? endGap * 0.5f : 0f);
                        if (s.AlongU) FinishGeom.EasedTile(mb, e0, cell.yMin, e1, cell.yMax, yb, yt, s.Bevel, sub, false);
                        else FinishGeom.EasedTile(mb, cell.yMin, e0, cell.yMax, e1, yb, yt, s.Bevel, sub, true);
                        any = true;
                    }
                    if (any)
                    {
                        pieces++;
                        int n = Mathf.Max(2, Mathf.CeilToInt(l / Units.In(9f)));
                        if (s.Type == FloorType.Hardwood && (r == 0 || r == widths.Count - 1)) faceNails += n; else if (s.Type == FloorType.Hardwood) nails += n;
                    }
                }
                foreach (var pj in prevJoints) foreach (var j in joints) minStag = Mathf.Min(minStag, Mathf.Abs(j - pj));
                if (prevJoints.Count == 0 && r > 0) { }
                prevJoints = joints;
            }
            // underlayment (or paper) under everything
            mb.UvOffset = Vector2.zero;
            foreach (var c in PanelMesh.Cells(area, holes))
                FinishGeom.Box(mb, c.xMin, c.xMax, 0f, s.UnderT, c.yMin, c.yMax, nt);
            var mats = new Material[nt + 1];
            for (int i = 0; i < nt; i++) mats[i] = FinishMaterials.Get(s.Mat, s.Tints[i]);
            mats[nt] = FinishMaterials.Get(s.UnderMat);
            var go = Flooring.Spawn(floor, "Floor_" + name, mb.Build("Floor_" + name), mats, "fin-floor-" + s.Type.ToString().ToLowerInvariant());
            var fi = go.AddComponent<FloorInstall>();
            fi.Type = s.Type; fi.Floor = floor; fi.Area = area; fi.Gap = s.Gap; fi.RowsAlongU = s.AlongU;
            if (holes != null) fi.Holes.AddRange(holes);
            fi.TopY = Flooring.SubfloorTop(floor) + yt;
            fi.MinStagger = minStag; fi.Pieces = pieces; fi.FreshPieces = fresh; fi.Cuts = cuts; fi.Nails = nails; fi.FaceNails = faceNails;
            if (s.Type == FloorType.Lvp && s.FixedL > 0f) InteriorSupplies.UseFasteners("fin-lvp-" + s.Mat.Substring(InteriorMaterials.LvpPrefix.Length), fresh);
            return fi;
        }
    }
}
