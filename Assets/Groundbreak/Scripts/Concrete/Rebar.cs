using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Reinforcing steel inside the forms: bars on chairs, crossings tied with tie wire.</summary>
    public static class RebarSystem
    {
        public const string ChairId = "rebarchair2";
        public const int TiesPerRoll = 400;   // 3.5 lb roll of 16 ga, ~8" per saddle tie

        public class Bar { public Vector3 A, B; public string DefId; public GameObject Go; }
        public class Crossing { public Vector3 P; public bool Tied; public GameObject Go; }

        public static readonly List<Bar> Bars = new List<Bar>();
        public static readonly List<Crossing> Crossings = new List<Crossing>();
        public static readonly List<GameObject> Chairs = new List<GameObject>();
        static readonly Dictionary<string, float> _openStick = new Dictionary<string, float>();
        static int _tiesLeftInRoll;
        static Transform _root;
        static Transform Root { get { if (_root == null) _root = new GameObject("Rebar").transform; return _root; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Bars.Clear(); Crossings.Clear(); Chairs.Clear(); _openStick.Clear(); _tiesLeftInRoll = 0; _root = null; }

        public static float TiedFraction
        {
            get { if (Crossings.Count == 0) return 1f; int t = 0; foreach (var c in Crossings) if (c.Tied) t++; return t / (float)Crossings.Count; }
        }
        public static int TiedCount { get { int t = 0; foreach (var c in Crossings) if (c.Tied) t++; return t; } }

        /// <summary>Uses rebar sticks for a bar of length L (laps 40 bar diameters where it needs more than one stick).</summary>
        static bool TakeSteel(string id, float len)
        {
            var def = Catalog.Get(id); if (def == null) return false;
            float stick = def.Size.z, lap = 40f * def.Size.x;
            int pieces = Mathf.CeilToInt((len - lap) / (stick - lap) - 1e-4f); if (pieces < 1) pieces = 1;
            float total = len + (pieces - 1) * lap;
            _openStick.TryGetValue(id, out float open);
            float needNew = Mathf.Max(0f, total - open);
            int sticks = Mathf.CeilToInt(needNew / stick - 1e-4f);
            if (sticks > 0 && !CTool.TakeStock(id, sticks, def.Name)) return false;
            open = open + sticks * stick - total;
            _openStick[id] = open;
            return true;
        }

        public static Bar AddBar(Vector3 a, Vector3 b, string defId)
        {
            var def = Catalog.Get(defId);
            if (def == null || (b - a).magnitude < 0.1f) return null;
            if (!TakeSteel(defId, (b - a).magnitude)) return null;
            var bar = new Bar { A = a, B = b, DefId = defId };
            Vector3 d = b - a;
            bar.Go = CTool.Build("Rebar " + def.Name, (mb, m) => mb.AddRod((a + b) * 0.5f, Quaternion.LookRotation(d.normalized, Vector3.up), def.Size.x * 0.5f * 1.1f, d.magnitude, 8, m.K(SurfaceKind.Rebar)), Root);
            float r = def.Size.x * 0.5f;
            foreach (var o in Bars)
            {
                if (Mathf.Abs(((o.A.y + o.B.y) - (a.y + b.y)) * 0.5f) > 0.06f) continue;
                if (!Intersect(CTool.XZ(a), CTool.XZ(b), CTool.XZ(o.A), CTool.XZ(o.B), out Vector2 x)) continue;
                Crossings.Add(new Crossing { P = new Vector3(x.x, Mathf.Max(a.y, o.A.y) + r, x.y) });
            }
            Bars.Add(bar);
            return bar;
        }

        static bool Intersect(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2, out Vector2 x)
        {
            x = default;
            Vector2 r = p2 - p, s = q2 - q;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-6f) return false;
            Vector2 qp = q - p;
            float t = (qp.x * s.y - qp.y * s.x) / den, u = (qp.x * r.y - qp.y * r.x) / den;
            if (t < 0 || t > 1 || u < 0 || u > 1) return false;
            x = p + r * t; return true;
        }

        static void AddChair(Vector3 barPoint, float barRadius)
        {
            if (!Game.Stock.TryTake(ChairId, 1)) return;
            float gy = SiteGround.GroundY(barPoint);
            float h = Mathf.Max(0.02f, barPoint.y - barRadius - gy);
            var go = CTool.Build("RebarChair", (b, m) =>
            {
                int pl = m.C(new Color(0.42f, 0.43f, 0.45f));
                b.AddFrustum(new Vector3(0, h * 0.5f, 0), Quaternion.identity, 0.028f, 0.012f, h, 8, pl);
                b.AddBox(new Vector3(0, h - 0.004f, 0), new Vector3(0.03f, 0.008f, 0.012f), Quaternion.identity, pl);
            }, Root);
            go.transform.position = new Vector3(barPoint.x, gy, barPoint.z);
            Chairs.Add(go);
        }

        /// <summary>Slab mat: bars both ways at `spacing` O.C., 3" clear of the forms, at mid-depth on chairs.
        /// Plus 2 continuous bars in a thickened-edge trench if the subgrade drops at the forms. Returns bars placed.</summary>
        public static int AutoGrid(FormOutline o, string defId = "rebar-4", float spacing = 18f * Units.Inch)
        {
            if (o == null || !o.Closed) return 0;
            var def = Catalog.Get(defId); float rad = def.Size.x * 0.5f;
            var poly = o.Polygon;
            Vector2 c = Vector2.zero; foreach (var p in poly) c += p; c /= poly.Length;
            float interior = SiteGround.GroundY(new Vector3(c.x, 0, c.y));
            float y = (o.TopY + interior) * 0.5f;
            Vector3 u3 = o.Runs[0].Dir; Vector2 u = new Vector2(u3.x, u3.z), v = new Vector2(-u.y, u.x);
            int placed = 0;
            float clear = 3f * Units.Inch;
            for (int axis = 0; axis < 2; axis++)
            {
                Vector2 along = axis == 0 ? u : v, across = axis == 0 ? v : u;
                float mn = float.MaxValue, mx = float.MinValue;
                foreach (var p in poly) { float k = Vector2.Dot(p - c, across); mn = Mathf.Min(mn, k); mx = Mathf.Max(mx, k); }
                float span = mx - mn - 2 * clear;
                int n = Mathf.Max(1, Mathf.CeilToInt(span / spacing - 1e-4f));
                float layerY = y + (axis == 0 ? -rad : rad);   // bottom layer one way, top layer the other (bars sit on each other)
                for (int i = 0; i <= n; i++)
                {
                    float k = mn + clear + span * i / n;
                    Vector2 origin = c + across * k;
                    if (!Clip(poly, origin, along, out float t0, out float t1)) continue;
                    t0 += clear; t1 -= clear; if (t1 - t0 < 0.3f) continue;
                    Vector2 a = origin + along * t0, b = origin + along * t1;
                    var bar = AddBar(new Vector3(a.x, layerY, a.y), new Vector3(b.x, layerY, b.y), defId);
                    if (bar == null) return placed;
                    placed++;
                }
            }
            // chairs under every other crossing (~36" grid)
            int idx = 0;
            foreach (var cr in Crossings) { if ((idx++ % 2) == 0) AddChair(cr.P - Vector3.up * rad * 2f, rad); }

            // thickened edge: two continuous bars 3" up from the trench bottom
            var r0 = o.Runs[0];
            Vector3 probe = (r0.A + r0.B) * 0.5f - r0.Out * (6f * Units.Inch);
            float edgeG = SiteGround.GroundY(probe);
            if (interior - edgeG > 3f * Units.Inch)
            {
                float by = edgeG + 3f * Units.Inch + rad;
                foreach (float inset in new[] { 3.5f * Units.Inch, 8.5f * Units.Inch })
                    foreach (var r in o.Runs)
                    {
                        Vector3 a = r.A - r.Out * inset + r.Dir * (inset - 1.5f * Units.Inch), b = r.B - r.Out * inset - r.Dir * (inset - 1.5f * Units.Inch);
                        a.y = b.y = by;
                        var bar = AddBar(a, b, defId);
                        if (bar == null) return placed;
                        placed++;
                        for (float t = 0.4f; t < (b - a).magnitude; t += 1.2f) AddChair(a + (b - a).normalized * t, rad);
                    }
            }
            return placed;
        }

        /// <summary>Clips the infinite line origin + t*dir against the polygon; returns the first inside span.</summary>
        static bool Clip(Vector2[] poly, Vector2 origin, Vector2 dir, out float t0, out float t1)
        {
            var ts = new List<float>();
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], e = b - a;
                float den = dir.x * e.y - dir.y * e.x;
                if (Mathf.Abs(den) < 1e-6f) continue;
                Vector2 ao = a - origin;
                float t = (ao.x * e.y - ao.y * e.x) / den, s = (ao.x * dir.y - ao.y * dir.x) / den;
                if (s >= -1e-4f && s <= 1 + 1e-4f) ts.Add(t);
            }
            ts.Sort();
            t0 = t1 = 0;
            if (ts.Count < 2) return false;
            t0 = ts[0]; t1 = ts[ts.Count - 1];
            return true;
        }

        /// <summary>Saddle-ties one crossing (uses tie wire). Returns false if out of wire.</summary>
        public static bool Tie(Crossing c)
        {
            if (c.Tied) return true;
            if (_tiesLeftInRoll <= 0)
            {
                if (!CTool.TakeStock("tiewire", 1, "tie wire")) return false;
                _tiesLeftInRoll = TiesPerRoll;
            }
            _tiesLeftInRoll--;
            c.Tied = true;
            c.Go = CTool.Build("Tie", (b, m) =>
            {
                int w = m.K(SurfaceKind.MatteBlack, new Color(0.7f, 0.7f, 0.7f));
                b.AddRod(Vector3.zero, Quaternion.Euler(0, 45, 0), 0.0013f, 0.03f, 4, w);
                b.AddRod(Vector3.zero, Quaternion.Euler(0, -45, 0), 0.0013f, 0.03f, 4, w);
                b.AddRod(new Vector3(0, 0.008f, 0), Quaternion.Euler(-90, 0, 0), 0.0016f, 0.016f, 4, w);
            }, Root);
            c.Go.transform.position = c.P;
            return true;
        }

        public static Crossing NearestUntied(Vector3 p, float r)
        {
            Crossing best = null; float bd = r;
            foreach (var c in Crossings) { if (c.Tied) continue; float d = CTool.XZ(c.P - p).magnitude; if (d < bd) { bd = d; best = c; } }
            return best;
        }

        public static int TieAllNear(Vector3 p, float r)
        {
            int n = 0;
            foreach (var c in Crossings) if (!c.Tied && CTool.XZ(c.P - p).magnitude <= r) { if (!Tie(c)) break; n++; }
            return n;
        }

        public static int TiesLeftInRoll => _tiesLeftInRoll;
    }

    public class RebarTool : ConcreteToolBase
    {
        public override string Id => "rebar-tool";
        public override string Name => "Rebar Cutter / Bender";
        static readonly string[] Sizes = { "rebar-3", "rebar-4", "rebar-5" };
        int _size = 1;
        bool _hasStart; Vector3 _start;

        public override string Hint => $"{Catalog.Get(Sizes[_size]).Name} (scroll) · LMB start/end a bar on chairs · F = slab grid 18\" O.C. + footing bars · {RebarSystem.Bars.Count} bars, {RebarSystem.TiedCount}/{RebarSystem.Crossings.Count} tied";

        public override void Primary()
        {
            if (!CTool.Aim(out var hit)) return;
            var o = FormSystem.Active;
            Vector3 p = CTool.SnapXZ(hit.point);
            float y = o != null && o.Contains(p) ? (o.TopY + SiteGround.GroundY(p)) * 0.5f : SiteGround.GroundY(p) + 2f * Units.Inch;
            p.y = y;
            if (!_hasStart) { _start = p; _hasStart = true; Game.Toast("Bar start marked"); return; }
            _hasStart = false;
            var bar = RebarSystem.AddBar(_start, new Vector3(p.x, _start.y, p.z), Sizes[_size]);
            if (bar != null) Game.Toast($"Bar laid: {Units.Format((bar.B - bar.A).magnitude)}");
        }

        public override void Tick()
        {
            float s = GInput.Scroll; if (s != 0) _size = (_size + (s > 0 ? 1 : 2)) % Sizes.Length;
            if (GInput.GetKeyDown(KeyCode.F))
            {
                int n = RebarSystem.AutoGrid(FormSystem.Active, Sizes[_size]);
                Game.Toast(n > 0 ? $"Placed {n} bars on chairs · {RebarSystem.Crossings.Count} crossings to tie" : "Close and stake the forms first");
            }
        }
    }

    public class TieWireTwisterTool : ConcreteToolBase
    {
        public override string Id => "tiewire-twister";
        public override string Name => "Tie-Wire Twister";
        public override string Hint => $"Hold LMB over the mat to tie crossings · {RebarSystem.TiedCount}/{RebarSystem.Crossings.Count} tied · {RebarSystem.TiesLeftInRoll} ties left on the reel";

        public override void PrimaryHeld()
        {
            if (!Ready(0.18f) || !CTool.Aim(out var hit)) return;
            var c = RebarSystem.NearestUntied(hit.point, 0.45f);
            if (c != null) RebarSystem.Tie(c);
        }
    }
}
