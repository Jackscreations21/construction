using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Trim stock (8'/12'/16' sticks) consumed by length on the miter saw, with an offcut pile that later
    /// pieces are cut from first (just like a carpenter's cut list).</summary>
    public static class TrimStock
    {
        public static readonly Dictionary<string, List<float>> Offcuts = new Dictionary<string, List<float>>();
        public static int SawCuts, SticksUsed;
        public const float Kerf = 0.003f;           // 1/8" blade
        public static readonly int[] PrimedFeet = { 8, 12, 16 }, OakFeet = { 8, 16 };
        public static int[] Feet(TrimSpecies s) => s == TrimSpecies.Oak ? OakFeet : PrimedFeet;
        public static string StockId(TrimProfile p, TrimSpecies s, int ft) => $"fin-trim-{p.Id}-{(s == TrimSpecies.Oak ? "oak" : "p")}-{ft}";
        public static float MaxStick => 16 * Units.Foot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Offcuts.Clear(); SawCuts = 0; SticksUsed = 0; }

        /// <summary>Plans and takes the sticks for all the pieces (each length = longest point + kerf). Uses offcuts first,
        /// then the shortest stick that fits (preferring what's on site). All-or-nothing; toasts what's missing.</summary>
        public static bool CutMany(TrimProfile p, TrimSpecies s, IList<float> lengths, List<float> stickOut = null)
        {
            string key = p.Id + "|" + s;
            if (!Offcuts.TryGetValue(key, out var pile)) Offcuts[key] = pile = new List<float>();
            var off = new List<float>(pile);
            var take = new Dictionary<string, int>();
            var sticks = new List<float>();
            var order = new List<float>(lengths); order.Sort((a, b) => b.CompareTo(a));
            foreach (var need0 in order)
            {
                float need = need0 + Kerf;
                if (need > MaxStick + 0.001f) { Game.Toast($"{Units.Format(need0)} is longer than a 16' stick — splice it"); return false; }
                int best = -1;
                for (int i = 0; i < off.Count; i++) if (off[i] >= need && (best < 0 || off[i] < off[best])) best = i;
                if (best >= 0) { sticks.Add(off[best]); float rem = off[best] - need; off.RemoveAt(best); if (rem > Units.In(6f)) off.Add(rem); continue; }
                int ft = -1;
                foreach (var f in Feet(s))
                {
                    if (f * Units.Foot < need) continue;
                    string id = StockId(p, s, f);
                    int have = FinishStock.Unlimited ? 9999 : Game.Stock.Count(id);
                    take.TryGetValue(id, out int used);
                    if (have > used) { ft = f; break; }
                    if (ft < 0) ft = -f; // remember the first fitting size in case nothing is on site
                }
                if (ft < 0)
                {
                    string id = StockId(p, s, -ft);
                    Game.Toast($"Need more {p.Name}{(s == TrimSpecies.Oak ? " (oak)" : "")} — order {-ft}' sticks on the tablet (Finish Supply)");
                    return false;
                }
                string sid = StockId(p, s, ft);
                take[sid] = (take.TryGetValue(sid, out int c) ? c : 0) + 1;
                float L = ft * Units.Foot;
                sticks.Add(L);
                if (L - need > Units.In(6f)) off.Add(L - need);
            }
            foreach (var kv in take) if (!FinishStock.Take(kv.Key, kv.Value)) return false;
            foreach (var kv in take) SticksUsed += kv.Value;
            pile.Clear(); pile.AddRange(off);
            SawCuts += lengths.Count * 2;
            stickOut?.AddRange(sticks);
            return true;
        }
    }

    /// <summary>Door casing: legs + mitered head with a consistent reveal off the jamb edge, both sides of the wall.</summary>
    public static class Casing
    {
        public static readonly float DefaultReveal = Units.In(0.1875f);

        /// <summary>Cases a hung door. sides: 0 = both, +1 = stop side only, -1 = swing side only.
        /// Needs the miter saw (cuts) — nail afterwards with <see cref="TrimPiece.Nail"/> / <see cref="WallTrim.NailAll"/>.
        /// Returns the new pieces (3 per side) or an empty list.</summary>
        public static List<TrimPiece> CaseDoor(HungDoor door, string profileId = "casing-colonial", TrimSpecies sp = TrimSpecies.Primed, int sides = 0, float reveal = -1f)
        {
            var res = new List<TrimPiece>();
            if (door == null) return res;
            if (door.IsBarn) { Game.Toast("Barn doors hang on the wall — the opening is wrapped in drywall, no casing"); return res; }
            if (!FinishStock.Require("miter-saw", "cut casing miters")) return res;
            var prof = TrimProfile.Get(profileId) ?? TrimProfile.Default(TrimKind.Casing);
            if (reveal < 0f) reveal = DefaultReveal;
            float W = prof.MaxY;
            for (int s = -1; s <= 1; s += 2)
            {
                if (sides != 0 && sides != s) continue;
                if (door.Casings.Exists(c => c != null && Vector3.Dot(c.Out, door.Through * s) > 0.9f)) continue; // already cased
                Vector3 outN = door.Through * s;
                Vector3 plane = door.Base + outN * (door.JambWidth * 0.5f);
                float xL = door.JambIn + reveal;
                float headY = door.Base.y + door.HeadIn + reveal;
                float floorL = FloorAt(plane - door.Across * (xL + W * 0.5f), door.Base.y);
                float floorR = FloorAt(plane + door.Across * (xL + W * 0.5f), door.Base.y);
                float legL = headY - floorL, legR = headY - floorR, head = 2f * xL;
                if (!TrimStock.CutMany(prof, sp, new[] { legL + W, legR + W, head + 2f * W })) return res;
                var face = Vector3.Dot(door.FaceA.N, outN) > 0.5f ? door.FaceA : door.FaceB;
                Vector3 baseL = plane - door.Across * xL; baseL.y = floorL;
                Vector3 baseR = plane + door.Across * xL; baseR.y = floorR;
                Vector3 headO = plane - door.Across * xL; headO.y = headY;
                var a = TrimPiece.Create(prof, sp, baseL, Vector3.up, outN, -door.Across, legL, TrimCut.Square, TrimCut.CasingMiter, face);
                var b = TrimPiece.Create(prof, sp, baseR, Vector3.up, outN, door.Across, legR, TrimCut.Square, TrimCut.CasingMiter, face);
                var h = TrimPiece.Create(prof, sp, headO, door.Across, outN, Vector3.up, head, TrimCut.CasingMiter, TrimCut.CasingMiter, face);
                foreach (var t in new[] { a, b, h })
                {
                    t.Door = door; t.Reveal = reveal; t.FaceCover = 0f; t.transform.SetParent(door.transform, true);
                    door.Casings.Add(t); res.Add(t);
                }
            }
            if (res.Count > 0) Game.Toast($"Cased the door: {res.Count} pieces of {prof.Name}, {Units.Format(reveal)} reveal");
            return res;
        }

        static float FloorAt(Vector3 p, float fallback)
        {
            float y = WallTrim.FloorBelow(new Vector3(p.x, fallback + 0.4f, p.z), 0.8f, out bool ok);
            return ok ? Mathf.Max(y, fallback) : fallback;
        }
    }

    /// <summary>Baseboard and crown along wall faces: finds the clear runs between corners, casings and cabinets,
    /// copes inside corners (coping saw), miters outside corners, splices long walls with a scarf over a stud.</summary>
    public static class WallTrim
    {
        enum Block { Corner, Butt, Opening }
        /// <summary>Log the probed spans/segments of every run (debugging).</summary>
        public static bool Verbose;
        struct Span { public float U0, U1; public Block T; }

        /// <summary>Runs baseboard along the bottom of a (finished) wall face. Pass the framing face (e.g. FinishAim.FaceBehind).</summary>
        public static List<TrimPiece> RunBase(FramePlane fp, string profileId = "base-colonial", TrimSpecies sp = TrimSpecies.Primed)
            => RunFace(fp, TrimProfile.Get(profileId) ?? TrimProfile.Default(TrimKind.Base), sp);

        /// <summary>Runs crown at the wall/ceiling junction of a wall face.</summary>
        public static List<TrimPiece> RunCrown(FramePlane fp, string profileId = "crown-358", TrimSpecies sp = TrimSpecies.Primed)
            => RunFace(fp, TrimProfile.Get(profileId) ?? TrimProfile.Default(TrimKind.Crown), sp);

        /// <summary>Nails every un-nailed trim piece (finish nailer; brads too for casing if owned). Returns nails driven.</summary>
        public static int NailAll()
        {
            if (!FinishStock.Require("finish-nailer", "nail trim")) return 0;
            int n = 0;
            foreach (var t in TrimPiece.All.ToArray())
            {
                if (t == null || t.Nailed) continue;
                if (!InteriorSupplies.UseFasteners("fin-nails-15ga", 12)) break;
                n += t.Nail();
            }
            return n;
        }

        /// <summary>Caulks every trim piece with painter's caulk (needs the caulk gun and tubes). Returns feet of bead.</summary>
        public static float CaulkAll()
        {
            float m = 0f;
            foreach (var t in TrimPiece.All.ToArray()) if (t != null && !t.Caulked) m += t.Caulk();
            return m / Units.Foot;
        }

        public static List<TrimPiece> RunFace(FramePlane fp, TrimProfile prof, TrimSpecies sp)
        {
            var res = new List<TrimPiece>();
            if (fp == null || !fp.IsWall) { Game.Toast("Aim at a wall"); return res; }
            if (!FinishStock.Require("miter-saw", "cut trim to length")) return res;
            bool crown = prof.Kind == TrimKind.Crown;
            float c = FinishHooks.CoveringThickness(fp, fp.Bounds.center);
            float u0 = fp.Bounds.xMin - c, u1 = fp.Bounds.xMax + c;
            float uMid = (u0 + u1) * 0.5f;

            // reference height: finished floor / ceiling at the face
            float refY;
            if (!crown)
            {
                refY = FloorBelow(fp.World(uMid, fp.Bounds.yMin + 0.4f, c + 0.03f), 1.0f, out bool ok);
                if (!ok) refY = fp.World(0, fp.Bounds.yMin).y;
            }
            else
            {
                refY = CeilingAbove(fp.World(uMid, fp.Bounds.yMax - 0.4f, c + 0.03f), 1.0f, out bool ok);
                if (!ok) refY = fp.World(0, fp.Bounds.yMax).y;
            }
            float probeY = crown ? refY - Units.In(1f) : refY + Units.In(1.5f);
            float probeProud = c + Units.In(0.2f);
            float vProbe = probeY - fp.World(0, 0).y; // walls: V = world up

            // blocked spans along U
            var spans = new List<Span>();
            Probe(fp, u0, u1, vProbe, probeProud, spans);
            if (!crown)
                foreach (var ro in fp.Openings())
                    if (ro.yMin < fp.Bounds.yMin + Units.In(3f)) spans.Add(new Span { U0 = ro.xMin - c, U1 = ro.xMax + c, T = Block.Opening });
            spans.Sort((a, b) => a.U0.CompareTo(b.U0));

            // clear segments
            float cur = u0; Block curT = Block.Corner; bool curIsExtent = true;
            var segs = new List<(float a, float b, bool aExt, Block aT, bool bExt, Block bT)>();
            foreach (var s in spans)
            {
                if (s.U0 >= u1) break;
                if (s.U1 <= cur) continue;
                if (s.U0 > cur + Units.In(1f) && s.U0 < u1) segs.Add((cur, Mathf.Min(s.U0, u1), curIsExtent, curT, false, s.T));
                if (s.U1 > cur) { cur = s.U1; curT = s.T; curIsExtent = false; }
                if (cur >= u1) break;
            }
            if (u1 - cur > Units.In(1f)) segs.Add((cur, u1, curIsExtent, curT, true, Block.Corner));

            if (Verbose)
            {
                var sb = new System.Text.StringBuilder($"[WallTrim] {prof.Kind} face N={fp.N} u {u0:F3}..{u1:F3} refY {refY:F3} spans:");
                foreach (var s in spans) sb.Append($" [{s.U0:F3},{s.U1:F3} {s.T}]");
                sb.Append(" segs:"); foreach (var g in segs) sb.Append($" [{g.a:F3},{g.b:F3}]");
                Debug.Log(sb.ToString());
            }
            float W = prof.MaxY;
            Vector3 across = crown ? Vector3.down : Vector3.up;
            bool warnedOpening = false;
            foreach (var sg in segs)
            {
                Vector3 o = fp.World(sg.a, 0f, c); o.y = refY;
                float len = sg.b - sg.a;
                // already trimmed?
                bool dup = false;
                foreach (var t in TrimPiece.All)
                {
                    if (t == null || t.Kind != prof.Kind || Vector3.Dot(t.Out, fp.N) < 0.99f) continue;
                    if (Mathf.Abs(Vector3.Dot(t.Origin - o, fp.N)) > 0.01f || Mathf.Abs(t.Origin.y - o.y) > 0.05f) continue;
                    float ta = Vector3.Dot(t.Origin - o, fp.U), tb = ta + Vector3.Dot(t.Dir, fp.U) * t.Length;
                    if (Mathf.Min(Mathf.Max(ta, tb), len) - Mathf.Max(Mathf.Min(ta, tb), 0f) > Units.In(1f)) { dup = true; break; }
                }
                if (dup) continue;
                TrimCut sc = EndCut(sg.aExt, sg.aT, o, prof, fp, true, ref warnedOpening);
                TrimCut ec = EndCut(sg.bExt, sg.bT, o + fp.U * len, prof, fp, false, ref warnedOpening);
                // split into sticks (scarf over a stud) if longer than 16'
                var cuts = new List<float> { 0f };
                float margin = W * 2f + Units.In(1f);
                while (len - cuts[cuts.Count - 1] + margin > TrimStock.MaxStick)
                {
                    float target = cuts[cuts.Count - 1] + TrimStock.MaxStick - margin;
                    float best = target;
                    foreach (var su in fp.FramingLinesU())
                    {
                        float t = su - sg.a;
                        if (t < target && t > cuts[cuts.Count - 1] + Units.In(24f) && (best == target || t > best)) best = t;
                    }
                    cuts.Add(best);
                }
                cuts.Add(len);
                var lens = new List<float>();
                for (int i = 0; i + 1 < cuts.Count; i++) lens.Add(cuts[i + 1] - cuts[i] + margin);
                if (!TrimStock.CutMany(prof, sp, lens)) return res;
                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    var t = TrimPiece.Create(prof, sp, o + fp.U * cuts[i], fp.U, fp.N, across, cuts[i + 1] - cuts[i],
                        i == 0 ? sc : TrimCut.Scarf, i + 2 == cuts.Count ? ec : TrimCut.Scarf, fp);
                    t.FaceCover = c;
                    res.Add(t);
                }
            }
            if (res.Count > 0) Game.Toast($"{prof.Name}: {res.Count} piece(s) cut and fit — nail them off");
            return res;
        }

        static TrimCut EndCut(bool extent, Block t, Vector3 corner, TrimProfile prof, FramePlane fp, bool start, ref bool warned)
        {
            if (extent) return TrimCut.OutsideMiter;
            if (t == Block.Butt) return TrimCut.Square;
            if (t == Block.Opening)
            {
                if (!warned) { Game.Toast("Base stops at an uncased door opening — case the door first"); warned = true; }
                return TrimCut.Square;
            }
            // inside corner: is the perpendicular wall already trimmed here?
            TrimPiece mate = null; bool mateStart = false;
            foreach (var p in TrimPiece.All)
            {
                if (p == null || p.Kind != prof.Kind || Mathf.Abs(Vector3.Dot(p.Out, fp.N)) > 0.1f) continue;
                Vector3 a = p.StartPoint, b = p.EndPoint;
                if (Flat(a - corner) < Units.In(1.5f) && Mathf.Abs(a.y - corner.y) < 0.05f) { mate = p; mateStart = true; break; }
                if (Flat(b - corner) < Units.In(1.5f) && Mathf.Abs(b.y - corner.y) < 0.05f) { mate = p; mateStart = false; break; }
            }
            if (mate == null) return TrimCut.Square;
            if (FinishStock.Owns("coping-saw")) return TrimCut.Cope;
            Game.Toast("No coping saw — the inside corner was mitered and will open up (inspector will flag it)");
            mate.SetCut(mateStart, TrimCut.InsideMiter);
            return TrimCut.InsideMiter;
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        static void Probe(FramePlane fp, float u0, float u1, float v, float proud, List<Span> spans)
        {
            float pad = 0.3f, L = (u1 - u0) + 2f * pad;
            Vector3 a = fp.World(u0 - pad, v, proud), b = fp.World(u1 + pad, v, proud);
            var lo = new Dictionary<Collider, float>(); var hi = new Dictionary<Collider, float>(); var type = new Dictionary<Collider, Block>();
            foreach (var h in FinishAim.SortedHits(new Ray(a, fp.U), L))
            {
                if (!Classify(h.collider, out var bt)) continue;
                float u = u0 - pad + h.distance;
                if (!lo.ContainsKey(h.collider)) { lo[h.collider] = u; type[h.collider] = bt; }
            }
            foreach (var h in FinishAim.SortedHits(new Ray(b, -fp.U), L))
            {
                if (!Classify(h.collider, out var bt)) continue;
                float u = u1 + pad - h.distance;
                if (!hi.ContainsKey(h.collider)) { hi[h.collider] = u; type[h.collider] = bt; }
            }
            foreach (var kv in type)
            {
                bool hasL = lo.TryGetValue(kv.Key, out var x), hasR = hi.TryGetValue(kv.Key, out var y);
                float s = hasL ? x : float.NegativeInfinity;
                float e = hasR ? y : float.PositiveInfinity;
                // one-sided hits beyond the face's ends belong to the neighbouring construction (seen edge-on): ignore their far side
                if (hasL && !hasR && x < u0 + 0.005f) e = x + 0.001f;
                if (hasR && !hasL && y > u1 - 0.005f) s = y - 0.001f;
                if (e < s) { var t = s; s = e; e = t; }
                spans.Add(new Span { U0 = s, U1 = e, T = kv.Value });
            }
        }

        static bool Classify(Collider c, out Block t)
        {
            t = Block.Corner;
            if (c == null || c.GetComponentInParent<PlayerRig>() != null) return false;
            var fl = c.GetComponentInParent<FinishLayer>();
            if (fl != null)
            {
                string k = fl.Kind ?? "";
                if (k.StartsWith("fin-trim-casing") || k.StartsWith("fin-cab")) { t = Block.Butt; return true; }
                if (k.StartsWith("fin-") || k.StartsWith("bead") || k == "marker") return false;
                t = Block.Corner; return true;
            }
            var p = c.GetComponentInParent<Piece>();
            if (p != null && p.Def != null)
            {
                if (p.Def.Category == PieceCategory.Lumber || p.Def.Category == PieceCategory.Sheet || p.Def.Category == PieceCategory.Masonry) { t = Block.Corner; return true; }
                t = Block.Butt; return true;
            }
            if (c.bounds.size.x > 20f || c.bounds.size.z > 20f) return false;
            t = Block.Butt; return true;
        }

        /// <summary>Top of the floor under a point (finished floor if any, else subfloor). Ignores trim and beads.</summary>
        public static float FloorBelow(Vector3 from, float dist, out bool ok)
        {
            foreach (var h in FinishAim.SortedHits(new Ray(from, Vector3.down), dist))
            {
                if (h.normal.y < 0.7f) continue;
                var fl = h.collider.GetComponentInParent<FinishLayer>();
                if (fl != null && ((fl.Kind ?? "").StartsWith("fin-trim") || (fl.Kind ?? "").StartsWith("fin-cab") || (fl.Kind ?? "").StartsWith("fin-backsplash"))) continue;
                if (h.collider.GetComponentInParent<PlayerRig>() != null) continue;
                ok = true; return h.point.y;
            }
            ok = false; return from.y - dist;
        }

        public static float CeilingAbove(Vector3 from, float dist, out bool ok)
        {
            foreach (var h in FinishAim.SortedHits(new Ray(from, Vector3.up), dist))
            {
                if (h.normal.y > -0.7f) continue;
                var fl = h.collider.GetComponentInParent<FinishLayer>();
                if (fl != null && ((fl.Kind ?? "").StartsWith("fin-trim") || (fl.Kind ?? "").StartsWith("fin-cab"))) continue;
                ok = true; return h.point.y;
            }
            ok = false; return from.y + dist;
        }
    }
}
