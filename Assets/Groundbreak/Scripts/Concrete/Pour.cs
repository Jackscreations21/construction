using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum FinishOp { Rake, Vibrate, Screed, BullFloat, Edge, Trowel, Broom }

    /// <summary>
    /// Wet concrete inside one form outline, simulated on the site's 4" ground cells: a surface height per cell that
    /// flows with slump, gets raked / vibrated / screeded / floated / edged / grooved / troweled / broomed, sets over game
    /// minutes and finally becomes a <see cref="ConcreteElement"/>. Finishing timing and skipped steps produce defects.
    /// </summary>
    public class PourSystem : MonoBehaviour
    {
        public const string AnchorId = "jbolt12";
        // Set-time model (game minutes since that concrete was placed, scaled by the mix's SetRate)
        public const float WorkableMinutes = 75f;    // rake / vibrate / screed / bull float
        public const float BleedEndMinutes = 90f;    // bleed water gone: edging, jointing, troweling, brooming
        public const float FinalSetMinutes = 240f;   // too hard to finish
        public const float ColdJointMinutes = 90f;   // a stop longer than this between loads leaves a cold joint
        const ushort FVib = 1, FScreed = 2, FFloat = 4, FEdge = 8, FTrowel = 16, FBroom = 32, FGroove = 64;

        public static readonly List<PourSystem> All = new List<PourSystem>();
        public static PourSystem Latest => All.Count > 0 ? All[All.Count - 1] : null;

        public FormOutline Outline;
        public ConcreteElement Element;
        public float SetRate = 1f;
        public int Psi = 3000, Slump = 4;
        public bool Fiber;
        public float PlacedM3, CapacityM3;
        public readonly List<string> Defects = new List<string>();
        public readonly List<(Vector3 a, Vector3 b)> Joints = new List<(Vector3, Vector3)>();
        public readonly List<Transform> Anchors = new List<Transform>();

        int _x0, _z0, _w, _h;
        bool[] _in, _edge;
        float[] _ground, _top, _placed;
        ushort[] _flags;
        float _lastPlace = -1f, _coldJointAt = -1f;
        bool _started, _dirty, _bleedWarned;
        Mesh _mesh; MeshCollider _col; MeshRenderer _mr;
        float _nextMesh, _nextCol, _nextCheck;
        GameObject _jointGo, _crackGo;

        public bool Started => _started;
        public bool Plastic => _started && Element == null;
        float Now => ConcreteTime.Now;
        public float TopY => Outline.TopY;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); }

        /// <summary>The pour for the forms containing p (created on demand for the active forms).</summary>
        public static PourSystem ForPoint(Vector3 p)
        {
            foreach (var ps in All) if (ps.Outline.Contains(p) || ps.Element != null && ps.Element.Contains(p)) return ps;
            var o = FormSystem.Active;
            if (o == null || !o.Contains(p)) return null;
            return Begin(o);
        }

        public static PourSystem Begin(FormOutline o)
        {
            if (o.Pour != null) return o.Pour;
            var go = new GameObject("ConcretePour");
            var ps = go.AddComponent<PourSystem>();
            ps.Init(o);
            All.Add(ps);
            return ps;
        }

        void Init(FormOutline o)
        {
            Outline = o; o.Pour = this;
            var poly = o.Polygon;
            Vector2 mn = poly[0], mx = poly[0];
            foreach (var p in poly) { mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
            _x0 = Mathf.FloorToInt((mn.x - SiteGround.Origin.x) / SiteGround.Cell) - 1;
            _z0 = Mathf.FloorToInt((mn.y - SiteGround.Origin.z) / SiteGround.Cell) - 1;
            _w = Mathf.CeilToInt((mx.x - mn.x) / SiteGround.Cell) + 3;
            _h = Mathf.CeilToInt((mx.y - mn.y) / SiteGround.Cell) + 3;
            int n = _w * _h;
            _in = new bool[n]; _edge = new bool[n]; _ground = new float[n]; _top = new float[n]; _placed = new float[n]; _flags = new ushort[n];
            var g = SiteGround.I;
            for (int z = 0; z < _h; z++)
                for (int x = 0; x < _w; x++)
                {
                    int i = z * _w + x, gx = _x0 + x, gz = _z0 + z;
                    if (gx < 0 || gz < 0 || gx >= SiteGround.N || gz >= SiteGround.N) continue;
                    var c = SiteGround.CellCenter(gx, gz);
                    _in[i] = CTool.PointInPolygon(poly, new Vector2(c.x, c.z));
                    _ground[i] = g != null ? g.CellHeight(gx, gz) : 0f;
                    _top[i] = _ground[i];
                    _placed[i] = -1f;
                    if (_in[i]) CapacityM3 += Mathf.Max(0f, TopY - _ground[i]) * SiteGround.Cell * SiteGround.Cell;
                }
            for (int z = 0; z < _h; z++)
                for (int x = 0; x < _w; x++)
                {
                    int i = z * _w + x; if (!_in[i]) continue;
                    _edge[i] = !In(x - 1, z) || !In(x + 1, z) || !In(x, z - 1) || !In(x, z + 1);
                }
            _mesh = new Mesh { name = "ConcreteSurface", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = gameObject.AddComponent<MeshRenderer>();
            _mr.sharedMaterials = new[] { Materials.Get(SurfaceKind.WetConcrete), Materials.Get(SurfaceKind.Concrete), Materials.Get(SurfaceKind.ConcreteBlock) };
            _col = gameObject.AddComponent<MeshCollider>();
        }

        bool In(int x, int z) => x >= 0 && z >= 0 && x < _w && z < _h && _in[z * _w + x];
        float Thick(int i) => _top[i] - _ground[i];
        bool Cell(Vector3 p, out int i)
        {
            int x = Mathf.FloorToInt((p.x - SiteGround.Origin.x) / SiteGround.Cell) - _x0, z = Mathf.FloorToInt((p.z - SiteGround.Origin.z) / SiteGround.Cell) - _z0;
            i = z * _w + x;
            return In(x, z);
        }
        Vector3 Center(int i) { var c = SiteGround.CellCenter(_x0 + i % _w, _z0 + i / _w); c.y = _top[i]; return c; }
        float Age(int i) => _placed[i] < 0 ? 0f : (Now - _placed[i]) / SetRate;

        /// <summary>Cells (indices) inside radius r of p.</summary>
        List<int> Around(Vector3 p, float r)
        {
            var list = new List<int>();
            int cx = Mathf.FloorToInt((p.x - SiteGround.Origin.x) / SiteGround.Cell) - _x0, cz = Mathf.FloorToInt((p.z - SiteGround.Origin.z) / SiteGround.Cell) - _z0;
            int k = Mathf.CeilToInt(r / SiteGround.Cell);
            for (int z = cz - k; z <= cz + k; z++)
                for (int x = cx - k; x <= cx + k; x++)
                {
                    if (!In(x, z)) continue;
                    int i = z * _w + x; var c = Center(i);
                    if ((c.x - p.x) * (c.x - p.x) + (c.z - p.z) * (c.z - p.z) <= r * r) list.Add(i);
                }
            return list;
        }

        int NearestInside(Vector3 p)
        {
            if (Cell(p, out int i)) return i;
            int best = -1; float bd = float.MaxValue;
            for (int k = 0; k < _in.Length; k++) if (_in[k]) { var c = Center(k); float d = (c.x - p.x) * (c.x - p.x) + (c.z - p.z) * (c.z - p.z); if (d < bd) { bd = d; best = k; } }
            return best;
        }

        // ------------------------------------------------------------------ placing concrete

        /// <summary>Chute discharge for dt real seconds (~1/4 yd per game minute, a typical chute pour rate).</summary>
        public float Discharge(ReadyMixTruck truck, Vector3 at, float dt)
        {
            float rate = 0.25f * 1440f / GameClock.RealSecondsPerDay;   // yd per real second
            float yd = truck.Discharge(rate * dt);
            if (yd > 0) AddConcrete(at, yd / FormSystem.YardsPerM3, truck.Order);
            return yd;
        }

        /// <summary>Drops m³ of concrete at a point (it piles up, then flows with its slump).</summary>
        public void AddConcrete(Vector3 at, float m3, ConcreteOrder order)
        {
            if (m3 <= 0 || Element != null) return;
            float now = Now;
            if (!_started) FirstConcrete(order);
            if (_lastPlace >= 0 && now - _lastPlace > ColdJointMinutes * SetRate && _coldJointAt < 0)
            {
                _coldJointAt = _lastPlace;
                Defects.Add($"Cold joint: the pour stopped {ConcreteTime.Duration(now - _lastPlace)} - new concrete won't bond to the set concrete");
            }
            _lastPlace = now;
            int c = NearestInside(at); if (c < 0) return;
            var cells = Around(Center(c), 0.3f);
            if (cells.Count == 0) cells.Add(c);
            float dh = m3 / (cells.Count * SiteGround.Cell * SiteGround.Cell);
            foreach (var i in cells)
            {
                if (_placed[i] < 0 || Thick(i) < 0.002f) _placed[i] = now;
                _top[i] += dh;
                _flags[i] &= unchecked((ushort)~(FScreed | FFloat | FTrowel | FBroom));
            }
            PlacedM3 += m3;
            _dirty = true;
        }

        void FirstConcrete(ConcreteOrder order)
        {
            _started = true;
            if (order != null) { SetRate = order.SetRate; Psi = order.Psi; Slump = order.SlumpIn; Fiber = order.Fiber; }
            if (RebarSystem.Crossings.Count > 0 && RebarSystem.TiedFraction < 0.5f)
                Defects.Add($"Rebar only {RebarSystem.TiedFraction * 100:0}% tied: the mat got walked down to the bottom of the slab");
            if (RebarSystem.Bars.Count == 0 && !Fiber) Defects.Add("No reinforcement: cracks will open up instead of staying tight");
            if (Outline.Poly == null) Defects.Add("No vapor barrier: ground moisture will wick through the slab");
            if (SiteGround.I != null && SiteGround.I.CompactedFraction(Outline.Polygon) < 0.8f)
                Defects.Add("Subgrade/gravel not compacted: the slab will settle and crack");
        }

        // ------------------------------------------------------------------ simulation

        void Update()
        {
            if (Plastic) Flow();
            if (Plastic && Time.time >= _nextCheck) { _nextCheck = Time.time + 0.5f; CheckForms(); TryComplete(); }
            if (_dirty && Time.time >= _nextMesh) { _nextMesh = Time.time + 0.08f; RebuildMesh(); }
            if (Time.time >= _nextCol && _col != null && _mesh.vertexCount > 0) { _nextCol = Time.time + 0.5f; _col.sharedMesh = null; _col.sharedMesh = _mesh; }
        }

        float[] _delta;
        void Flow()
        {
            if (_delta == null) _delta = new float[_top.Length];
            float yield = (7 - Slump) * 0.25f * Units.Inch;   // stiffer mix holds a steeper pile
            bool moved = false;
            for (int it = 0; it < 3; it++)
            {
                System.Array.Clear(_delta, 0, _delta.Length);
                for (int z = 0; z < _h; z++)
                    for (int x = 0; x < _w; x++)
                    {
                        int i = z * _w + x; if (!_in[i]) continue;
                        float th = Thick(i); if (th < 0.0005f) continue;
                        if (Age(i) > WorkableMinutes) continue;
                        float out_ = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (!In(nx, nz)) continue;
                            int j = nz * _w + nx;
                            float diff = _top[i] - _top[j] - yield;
                            if (diff <= 0) continue;
                            float f = Mathf.Min(diff * 0.2f, th * 0.24f - out_ * 0f);
                            _delta[i] -= f; _delta[j] += f; out_ += f;
                            if (_placed[j] < 0 || Thick(j) < 0.002f) _placed[j] = _placed[i];
                        }
                    }
                for (int i = 0; i < _top.Length; i++) if (_delta[i] != 0) { _top[i] += _delta[i]; moved = true; }
            }
            if (moved) _dirty = true;
        }

        void CheckForms()
        {
            foreach (var r in Outline.Runs)
            {
                if (r.BlownOut) continue;
                float risk = FormSystem.BlowoutRisk(r, Outline, out string why);
                if (risk < 0.5f) continue;
                // concrete head against this run
                float head = 0, full = 0; int n = 0;
                for (float t = 0.2f; t < r.Length - 0.2f; t += 0.3f)
                    if (Cell(r.PointAt(t) - r.Out * 0.06f, out int i)) { head += Thick(i); full += TopY - _ground[i]; n++; }
                if (n == 0 || head < full * 0.6f) continue;
                r.BlownOut = true;
                foreach (var b in r.Boards)
                {
                    b.transform.position += r.Out * (2.5f * Units.Inch);
                    b.transform.rotation = Quaternion.AngleAxis(-12f, r.Dir) * b.transform.rotation;
                }
                float lost = 0;
                for (float t = 0.2f; t < r.Length - 0.2f; t += SiteGround.Cell)
                    if (Cell(r.PointAt(t) - r.Out * 0.06f, out int i)) { float d = Thick(i) * 0.5f; _top[i] -= d; lost += d * SiteGround.Cell * SiteGround.Cell; }
                PlacedM3 -= lost;
                _dirty = true;
                Defects.Add($"FORM BLOWOUT on a {Units.Format(r.Length)} run ({why}): edge bulged out, {lost * FormSystem.YardsPerM3:0.00} yd lost");
                Game.Toast("BLOWOUT! A form let go - " + why);
            }
        }

        // ------------------------------------------------------------------ finishing ops

        /// <summary>Applies a finishing tool at a point. Returns a status message (empty = fine).</summary>
        public string Apply(FinishOp op, Vector3 p)
        {
            if (!Plastic) return Element != null ? "Concrete has set" : "No concrete here yet";
            float r = op == FinishOp.Screed || op == FinishOp.BullFloat ? 0.6f : op == FinishOp.Edge ? 0.25f : op == FinishOp.Rake ? 0.4f : 0.35f;
            var cells = Around(p, r);
            cells.RemoveAll(i => Thick(i) < 0.002f);
            if (cells.Count == 0) return "Nothing to work here";
            float age = 0; foreach (var i in cells) age += Age(i); age /= cells.Count;
            switch (op)
            {
                case FinishOp.Rake:
                    if (age > WorkableMinutes) return "Too stiff to move";
                    Level(cells, 0.6f); return "";
                case FinishOp.Vibrate:
                    if (age > WorkableMinutes + 15f) return "Too stiff to vibrate";
                    foreach (var i in cells) _flags[i] |= FVib;
                    Level(cells, 0.2f); return "";
                case FinishOp.Screed:
                    if (age > WorkableMinutes) return "Too stiff to screed";
                    Screed(cells); return "";
                case FinishOp.BullFloat:
                    if (age > BleedEndMinutes) return "Too late for the bull float";
                    int fl = 0;
                    foreach (var i in cells) if ((_flags[i] & FScreed) != 0) { _flags[i] |= FFloat; fl++; }
                    Level(cells, 0.3f);
                    return fl == 0 ? "Screed it first" : "";
                case FinishOp.Edge:
                    cells.RemoveAll(i => !_edge[i]);
                    if (cells.Count == 0) return "Edge along the forms";
                    return Late(age, cells, FEdge);
                case FinishOp.Trowel: return Late(age, cells, FTrowel);
                case FinishOp.Broom: return Late(age, cells, FBroom);
            }
            return "";
        }

        string Late(float age, List<int> cells, ushort flag)
        {
            if (age > FinalSetMinutes) return "Too hard - it has set";
            if (age < BleedEndMinutes && !_bleedWarned)
            {
                _bleedWarned = true;
                Defects.Add("Finished with bleed water on the surface: weak, dusting top (wait for the sheen to go)");
            }
            foreach (var i in cells) _flags[i] |= flag;
            _dirty = true;
            return age < BleedEndMinutes ? "Bleed water still on the surface!" : "";
        }

        void Level(List<int> cells, float k)
        {
            float avg = 0; foreach (var i in cells) avg += _top[i]; avg /= cells.Count;
            foreach (var i in cells) _top[i] += (avg - _top[i]) * k;
            _dirty = true;
        }

        void Screed(List<int> cells)
        {
            float excess = 0;
            foreach (var i in cells) if (_top[i] > TopY) { excess += _top[i] - TopY; _top[i] = TopY; }
            // fill lows within the strip, carry the rest ahead (to the lowest cell left in the slab)
            cells.Sort((a, b) => _top[a].CompareTo(_top[b]));
            foreach (var i in cells)
            {
                if (excess <= 0) break;
                float need = TopY - _top[i]; if (need <= 0) continue;
                float f = Mathf.Min(need, excess); _top[i] += f; excess -= f;
            }
            while (excess > 1e-6f)
            {
                int low = -1; float lv = float.MaxValue;
                for (int i = 0; i < _top.Length; i++) if (_in[i] && (_flags[i] & FScreed) == 0 && _top[i] < TopY - 0.001f && _top[i] < lv) { lv = _top[i]; low = i; }
                if (low < 0) { PlacedM3 -= excess * SiteGround.Cell * SiteGround.Cell; break; }  // spills over the forms
                float f = Mathf.Min(TopY - _top[low], excess); _top[low] += f; excess -= f;
                if (_placed[low] < 0) _placed[low] = Now;
            }
            foreach (var i in cells) if (_top[i] >= TopY - 0.25f * Units.Inch) _flags[i] |= FScreed;
            _dirty = true;
        }

        /// <summary>Tools a control joint across the whole slab through a and b (joint lines run form to form).</summary>
        public string Groove(Vector3 a, Vector3 b)
        {
            if (!Plastic) return "Concrete has set";
            Vector2 d = CTool.XZ(b - a); if (d.magnitude < 0.05f) return "Pick two points along the joint";
            d.Normalize();
            float age = Age(NearestInside(a));
            if (age > FinalSetMinutes) return "Too hard to groove - you'd have to saw-cut";
            var poly = Outline.Polygon; float t0 = float.MaxValue, t1 = float.MinValue; Vector2 o = CTool.XZ(a);
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 e = poly[i] - poly[j]; float den = d.x * e.y - d.y * e.x; if (Mathf.Abs(den) < 1e-6f) continue;
                Vector2 ao = poly[j] - o; float t = (ao.x * e.y - ao.y * e.x) / den, s = (ao.x * d.y - ao.y * d.x) / den;
                if (s >= -1e-4f && s <= 1 + 1e-4f) { t0 = Mathf.Min(t0, t); t1 = Mathf.Max(t1, t); }
            }
            if (t0 >= t1) return "Joint must cross the slab";
            Vector2 p0 = o + d * t0, p1 = o + d * t1;
            Joints.Add((new Vector3(p0.x, TopY, p0.y), new Vector3(p1.x, TopY, p1.y)));
            var cells = new List<int>();
            for (float t = 0; t <= t1 - t0; t += SiteGround.Cell * 0.5f) if (Cell(new Vector3(p0.x + d.x * t, 0, p0.y + d.y * t), out int i) && !cells.Contains(i)) cells.Add(i);
            string m = Late(age, cells, FGroove);
            BuildJoints();
            return m;
        }

        /// <summary>Wet-sets a 1/2" J-bolt 1 3/4" in from the nearest form face (centered in a 2x4 sill), 2 3/4" exposed.</summary>
        public Transform WetSetAnchor(Vector3 p)
        {
            if (!Plastic) { Game.Toast("Anchors must be wet-set while the concrete is plastic"); return null; }
            var r = FormSystem.NearestRun(Outline, p, out float t);
            if (r == null) { Game.Toast("Set anchors near the edge of the slab"); return null; }
            if (!CTool.TakeStock(AnchorId, 1, "J-bolts")) return null;
            Vector3 pos = r.PointAt(t) - r.Out * (1.75f * Units.Inch); pos.y = TopY;
            var go = CTool.Build("AnchorBolt", (b, m) =>
            {
                int galv = m.K(SurfaceKind.Galvanized);
                b.AddRod(new Vector3(0, -3.5f * Units.Inch + 1.375f * Units.Inch, 0), Quaternion.Euler(-90, 0, 0), 0.25f * Units.Inch, 10f * Units.Inch - 0f, 8, galv);
                b.AddCylinder(new Vector3(0, 0.75f * Units.Inch, 0), 0.6f * Units.Inch, 0.08f * Units.Inch, 12, galv);    // washer
                b.AddCylinder(new Vector3(0, 1.0f * Units.Inch, 0), 0.43f * Units.Inch, 0.45f * Units.Inch, 6, galv);     // hex nut
            }, transform);
            go.transform.position = pos;
            Anchors.Add(go.transform);
            return go.transform;
        }

        /// <summary>Anchor layout per IRC R403.1.6: within 12" of each corner and max 6' O.C.</summary>
        public int AutoAnchors()
        {
            int n = 0;
            foreach (var r in Outline.Runs)
            {
                float a = 12f * Units.Inch, span = r.Length - 2 * a;
                int k = Mathf.Max(1, Mathf.CeilToInt(span / (72f * Units.Inch) - 1e-4f));
                for (int i = 0; i <= k; i++) if (WetSetAnchor(r.PointAt(a + span * i / k) - r.Out * 0.05f) != null) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ queries for tools / tests

        /// <summary>Concrete surface (or subgrade if empty) at a point inside the forms.</summary>
        public float SurfaceY(Vector3 p) => Cell(p, out int i) ? _top[i] : SiteGround.GroundY(p);

        public float FillFraction => CapacityM3 <= 0 ? 0 : Mathf.Clamp01(PlacedM3 / CapacityM3);
        public bool Full { get { for (int i = 0; i < _in.Length; i++) if (_in[i] && _top[i] < TopY - 0.25f * Units.Inch) return false; return true; } }

        /// <summary>The emptiest spot (most room below top of form) within `within` of `from` (XZ). Point is at form top.</summary>
        public Vector3 LowestPoint(Vector3 from, float within = 1e6f)
        {
            int best = -1; float room = -1f, bd = float.MaxValue;
            for (int i = 0; i < _in.Length; i++)
            {
                if (!_in[i]) continue;
                var c = Center(i); float d = CTool.XZ(c - from).magnitude; if (d > within) continue;
                float rm = TopY - _top[i];
                if (rm > room + 0.005f || (Mathf.Abs(rm - room) <= 0.005f && d < bd)) { room = rm; best = i; bd = d; }
            }
            if (best < 0) return from;
            var p = Center(best); p.y = TopY; return p;
        }

        /// <summary>Grid of points over the slab (for sweeping a tool, e.g. tests or the help overlay).</summary>
        public List<Vector3> WorkPoints(float spacing, bool edgesOnly = false)
        {
            var pts = new List<Vector3>();
            int step = Mathf.Max(1, Mathf.RoundToInt(spacing / SiteGround.Cell));
            for (int z = 0; z < _h; z++)
                for (int x = 0; x < _w; x++)
                {
                    int i = z * _w + x; if (!_in[i]) continue;
                    if (edgesOnly ? _edge[i] && ((x + z) % step == 0) : (x % step == 0 && z % step == 0)) { var c = Center(i); c.y = TopY; pts.Add(c); }
                }
            return pts;
        }

        float Coverage(ushort flag, bool edgesOnly = false)
        {
            int tot = 0, ok = 0;
            for (int i = 0; i < _in.Length; i++) { if (!_in[i] || (edgesOnly && !_edge[i]) || Thick(i) < 0.002f) continue; tot++; if ((_flags[i] & flag) != 0) ok++; }
            return tot == 0 ? 0 : ok / (float)tot;
        }
        public float Vibrated => Coverage(FVib);
        public float EdgeVibrated => Coverage(FVib, true);
        public float Screeded => Coverage(FScreed);
        public float Floated => Coverage(FFloat);
        public float Edged => Coverage(FEdge, true);
        public float Troweled => Coverage(FTrowel);
        public float Broomed => Coverage(FBroom);
        public float MinAge { get { float m = float.MaxValue; for (int i = 0; i < _in.Length; i++) if (_in[i] && _placed[i] >= 0) m = Mathf.Min(m, Age(i)); return m == float.MaxValue ? 0 : m; } }

        /// <summary>Average slab thickness in inches over the interior (excludes thickened edges).</summary>
        public float SlabThicknessIn
        {
            get
            {
                var vals = new List<float>();
                for (int i = 0; i < _in.Length; i++) if (_in[i] && !_edge[i]) vals.Add(TopY - _ground[i]);
                if (vals.Count == 0) return 4f; vals.Sort();
                return Units.ToInches(vals[vals.Count / 2]);
            }
        }

        /// <summary>Longest unjointed span (ft) along each principal direction of the slab, and the recommended max.</summary>
        public void JointSpans(out float spanU, out float spanV, out float limitFt)
        {
            limitFt = Mathf.Min(15f, 2.5f * SlabThicknessIn);   // ACI 302: 2-3x thickness (in) in feet
            Vector3 u3 = Outline.Runs[0].Dir; Vector2 u = new Vector2(u3.x, u3.z), v = new Vector2(-u.y, u.x);
            spanU = Span(u, v); spanV = Span(v, u);
        }

        float Span(Vector2 along, Vector2 perp)
        {
            var poly = Outline.Polygon;
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var p in poly) { float k = Vector2.Dot(p, along); mn = Mathf.Min(mn, k); mx = Mathf.Max(mx, k); }
            var cuts = new List<float> { mn, mx };
            foreach (var j in Joints)
            {
                Vector2 d = CTool.XZ(j.b - j.a).normalized;
                if (Mathf.Abs(Vector2.Dot(d, perp)) > 0.9f) cuts.Add(Vector2.Dot(CTool.XZ(j.a), along));   // joint runs across this direction
            }
            cuts.Sort();
            float best = 0; for (int i = 1; i < cuts.Count; i++) best = Mathf.Max(best, cuts[i] - cuts[i - 1]);
            return best / Units.Foot;
        }

        public List<string> StatusLines()
        {
            var l = new List<string>();
            if (!_started) { l.Add($"Waiting for concrete · forms hold {CapacityM3 * FormSystem.YardsPerM3:0.00} yd"); return l; }
            float age = MinAge;
            string stage = Element != null ? "SET" : age < WorkableMinutes ? "plastic - place, rake, vibrate, screed, bull float" : age < BleedEndMinutes ? "bleed water on the surface - wait" : age < FinalSetMinutes ? "ready to finish - edge, joint, trowel / broom" : "setting";
            l.Add($"Placed {PlacedM3 * FormSystem.YardsPerM3:0.00} of {CapacityM3 * FormSystem.YardsPerM3:0.00} yd ({FillFraction * 100:0}%) · {stage} · oldest concrete {ConcreteTime.Duration(age * SetRate)}");
            JointSpans(out float su, out float sv, out float lim);
            l.Add($"Vibrated {Vibrated * 100:0}% · screeded {Screeded * 100:0}% · bull floated {Floated * 100:0}% · edged {Edged * 100:0}% · troweled {Troweled * 100:0}% · broomed {Broomed * 100:0}%");
            l.Add($"Control joints: {Joints.Count} · panels up to {su:0.#}' x {sv:0.#}' (keep under {lim:0}') · anchors {Anchors.Count}");
            if (Element != null) l.Add(Element.Summary);
            foreach (var d in Defects) l.Add("DEFECT: " + d);
            return l;
        }

        // ------------------------------------------------------------------ completion

        void TryComplete()
        {
            if (Element != null || !_started || !Full && FillFraction < 0.98f) { if (_started && MinAge > FinalSetMinutes) Complete(); return; }
            float finish = Mathf.Max(Troweled, Broomed);
            if ((MinAge > BleedEndMinutes && finish >= 0.85f && Edged >= 0.7f) || MinAge > FinalSetMinutes) Complete();
        }

        /// <summary>Concrete has taken its initial set: grade the work and turn the pour into a ConcreteElement.</summary>
        public ConcreteElement Complete()
        {
            if (Element != null) return Element;
            if (EdgeVibrated < 0.7f) Defects.Add($"Honeycombing along the forms: only {EdgeVibrated * 100:0}% of the edge was vibrated");
            if (Screeded < 0.9f) Defects.Add($"Surface out of flat: only {Screeded * 100:0}% screeded");
            if (Floated < 0.8f) Defects.Add("Rough surface, aggregate not embedded: skipped the bull float");
            if (Edged < 0.7f) Defects.Add("Sharp, chipping edges: not edged");
            if (Mathf.Max(Troweled, Broomed) < 0.8f) Defects.Add("Unfinished patches on the surface");
            int low = 0, tot = 0; for (int i = 0; i < _in.Length; i++) if (_in[i]) { tot++; if (_top[i] < TopY - 0.5f * Units.Inch) low++; }
            if (tot > 0 && low / (float)tot > 0.03f) Defects.Add($"Short of the top of form over {low * 100 / tot}% of the slab (ran out of concrete?)");
            JointSpans(out float su, out float sv, out float lim);
            bool crackU = su > lim + 0.01f, crackV = sv > lim + 0.01f;
            if (crackU || crackV) Defects.Add($"Random shrinkage cracks: {Mathf.Max(su, sv):0.#}' between joints (max {lim:0}' for a {SlabThicknessIn:0}\" slab)");

            bool broom = Broomed > Troweled;
            Element = gameObject.AddComponent<ConcreteElement>();
            float placedAvg = 0; int n = 0; for (int i = 0; i < _in.Length; i++) if (_in[i] && _placed[i] >= 0) { placedAvg += _placed[i]; n++; }
            Element.Init(this, Outline.Polygon, TopY, SlabThicknessIn, Psi, n > 0 ? placedAvg / n : Now, broom ? SurfaceKind.BroomedConcrete : SurfaceKind.TroweledConcrete, PlacedM3 * FormSystem.YardsPerM3);
            Element.Defects.AddRange(Defects);
            Element.AnchorBolts.AddRange(Anchors);
            if (crackU || crackV) BuildCracks(crackU, crackV);
            if (_coldJointAt >= 0) BuildColdJoint();
            Game.Toast("Concrete has set - keep it moist to cure; strip forms after 24 h");
            RebuildMesh();
            return Element;
        }

        public void OnFormsStripped() { if (Element != null) Element.FormsStripped = true; RebuildMesh(); }

        // ------------------------------------------------------------------ visuals

        void RebuildMesh()
        {
            _dirty = false;
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            var top = new List<int>(); var side = new List<int>(); var honey = new List<int>();
            int vw = _w + 1;
            var vi = new int[(_w + 1) * (_h + 1)];
            for (int k = 0; k < vi.Length; k++) vi[k] = -1;
            int VertIdx(int x, int z)
            {
                int k = z * vw + x; if (vi[k] >= 0) return vi[k];
                float s = 0; int c = 0;
                for (int dz = -1; dz <= 0; dz++) for (int dx = -1; dx <= 0; dx++)
                    { int cx = x + dx, cz = z + dz; if (!In(cx, cz)) continue; int i = cz * _w + cx; if (Thick(i) < 0.002f) continue; s += _top[i]; c++; }
                var p = SiteGround.VertexPos(_x0 + x, _z0 + z, c > 0 ? s / c : 0);
                vi[k] = v.Count; v.Add(p); uv.Add(new Vector2(p.x, p.z));
                return vi[k];
            }
            bool HasC(int x, int z) => In(x, z) && Thick(z * _w + x) >= 0.002f;
            for (int z = 0; z < _h; z++)
                for (int x = 0; x < _w; x++)
                {
                    if (!HasC(x, z)) continue;
                    int a = VertIdx(x, z), b = VertIdx(x + 1, z), c = VertIdx(x + 1, z + 1), d = VertIdx(x, z + 1);
                    top.Add(a); top.Add(d); top.Add(c); top.Add(a); top.Add(c); top.Add(b);
                }
            // vertical faces where the slab ends (form faces, visible after stripping)
            for (int z = 0; z < _h; z++)
                for (int x = 0; x < _w; x++)
                {
                    if (!HasC(x, z)) continue;
                    int i = z * _w + x;
                    var list = _edge[i] && (_flags[i] & FVib) == 0 && Element != null ? honey : side;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (HasC(nx, nz)) continue;
                        // edge corners in vertex space
                        int ax = k == 0 ? x + 1 : x, az = k == 2 ? z + 1 : z, bx = k == 1 ? x : (k == 0 ? x + 1 : x + 1), bz = k == 3 ? z : (k == 2 ? z + 1 : z + 1);
                        if (k == 0) { ax = x + 1; az = z; bx = x + 1; bz = z + 1; }
                        if (k == 1) { ax = x; az = z + 1; bx = x; bz = z; }
                        if (k == 2) { ax = x + 1; az = z + 1; bx = x; bz = z + 1; }
                        if (k == 3) { ax = x; az = z; bx = x + 1; bz = z; }
                        var ta = v[VertIdx(ax, az)]; var tb = v[VertIdx(bx, bz)];
                        var ba = new Vector3(ta.x, _ground[i] - 0.02f, ta.z); var bb = new Vector3(tb.x, _ground[i] - 0.02f, tb.z);
                        Vector3 outN = new Vector3(nx - x, 0, nz - z);
                        Vector3 along = (tb - ta); along.y = 0; along.Normalize();
                        int s0 = v.Count;
                        foreach (var q in new[] { ta, tb, bb, ba })
                        { v.Add(q); uv.Add(new Vector2(Vector3.Dot(q, along), q.y)); }
                        // winding: outward normal
                        Vector3 nrm = Vector3.Cross(v[s0 + 1] - v[s0], v[s0 + 2] - v[s0]);
                        if (Vector3.Dot(nrm, outN) >= 0) { list.Add(s0); list.Add(s0 + 1); list.Add(s0 + 2); list.Add(s0); list.Add(s0 + 2); list.Add(s0 + 3); }
                        else { list.Add(s0); list.Add(s0 + 2); list.Add(s0 + 1); list.Add(s0); list.Add(s0 + 3); list.Add(s0 + 2); }
                    }
                }
            _mesh.Clear();
            _mesh.SetVertices(v); _mesh.SetUVs(0, uv);
            _mesh.subMeshCount = 3;
            _mesh.SetTriangles(top, 0, false); _mesh.SetTriangles(side, 1, false); _mesh.SetTriangles(honey, 2, false);
            _mesh.RecalculateBounds(); _mesh.RecalculateNormals(); _mesh.RecalculateTangents();
            if (Element != null) Element.ApplyLook(_mr);
        }

        void BuildJoints()
        {
            if (_jointGo != null) Destroy(_jointGo);
            _jointGo = CTool.Build("ControlJoints", (b, m) =>
            {
                int dark = m.C(new Color(0.22f, 0.22f, 0.21f));
                foreach (var j in Joints)
                {
                    Vector3 d = j.b - j.a; d.y = 0; Vector3 s = Vector3.Cross(Vector3.up, d.normalized) * (0.125f * Units.Inch);
                    Vector3 a = j.a + Vector3.up * 0.0015f, c = j.b + Vector3.up * 0.0015f;
                    b.AddQuad(a - s, a + s, c + s, c - s, dark, Vector3.up);
                }
            }, transform);
        }

        void BuildCracks(bool acrossU, bool acrossV)
        {
            var rnd = new System.Random(Joints.Count * 31 + 7);
            var poly = Outline.Polygon;
            Vector3 u3 = Outline.Runs[0].Dir; Vector2 u = new Vector2(u3.x, u3.z), v = new Vector2(-u.y, u.x);
            Vector2 c = Vector2.zero; foreach (var p in poly) c += p; c /= poly.Length;
            _crackGo = CTool.Build("Cracks", (b, m) =>
            {
                int dark = m.C(new Color(0.15f, 0.15f, 0.15f));
                void Crack(Vector2 dir, Vector2 across)
                {
                    float mn = float.MaxValue, mx = float.MinValue;
                    foreach (var p in poly) { float k = Vector2.Dot(p - c, dir); mn = Mathf.Min(mn, k); mx = Mathf.Max(mx, k); }
                    Vector2 prev = c + dir * mn + across * (float)(rnd.NextDouble() - 0.5) * 0.6f;
                    for (float t = mn + 0.15f; t <= mx; t += 0.15f)
                    {
                        Vector2 nxt = c + dir * t + across * ((float)(rnd.NextDouble() - 0.5) * 0.12f + Vector2.Dot(prev - c, across) * 0.9f);
                        Vector3 a = new Vector3(prev.x, TopY + 0.0015f, prev.y), e = new Vector3(nxt.x, TopY + 0.0015f, nxt.y);
                        Vector3 s = Vector3.Cross(Vector3.up, (e - a).normalized) * 0.0012f;
                        b.AddQuad(a - s, a + s, e + s, e - s, dark, Vector3.up);
                        prev = nxt;
                    }
                }
                if (acrossU) Crack(v, u);   // crack runs across the long direction
                if (acrossV) Crack(u, v);
            }, transform);
        }

        void BuildColdJoint()
        {
            // a seam along the boundary between concrete placed before and after the stop
            CTool.Build("ColdJoint", (b, m) =>
            {
                int dark = m.C(new Color(0.3f, 0.29f, 0.27f));
                for (int z = 0; z < _h; z++)
                    for (int x = 0; x < _w - 1; x++)
                    {
                        int i = z * _w + x, j = i + 1;
                        if (!_in[i] || !_in[j]) continue;
                        bool oi = _placed[i] <= _coldJointAt, oj = _placed[j] <= _coldJointAt;
                        if (oi == oj) continue;
                        var p = SiteGround.VertexPos(_x0 + x + 1, _z0 + z, TopY + 0.0015f);
                        b.AddQuad(p, p + new Vector3(0.002f, 0, 0), p + new Vector3(0.002f, 0, SiteGround.Cell), p + new Vector3(0, 0, SiteGround.Cell), dark, Vector3.up);
                    }
            }, transform);
        }
    }

    /// <summary>Finishing hand tools: hold LMB to work the concrete at the aim point.</summary>
    public class FinishTool : ConcreteToolBase
    {
        readonly FinishOp _op;
        public FinishTool(FinishOp op) { _op = op; }
        public override string Id => _op switch { FinishOp.Rake => "conc-rake", FinishOp.Vibrate => "vibrator", FinishOp.Screed => "screed", FinishOp.BullFloat => "bullfloat", FinishOp.Edge => "edger", FinishOp.Trowel => "trowel", _ => "broom" };
        public override string Name => _op switch { FinishOp.Rake => "Concrete Rake", FinishOp.Vibrate => "Pencil Vibrator", FinishOp.Screed => "Magnesium Screed", FinishOp.BullFloat => "Bull Float", FinishOp.Edge => "Hand Edger", FinishOp.Trowel => "Finishing Trowel", _ => "Finish Broom" };
        public override int Slots => _op == FinishOp.Screed || _op == FinishOp.BullFloat ? 2 : 1;
        public override bool Cordless => _op == FinishOp.Vibrate;
        public override string Hint
        {
            get
            {
                var p = PourSystem.Latest;
                string s = p == null ? "No pour yet" : p.StatusLines()[0];
                return (_op == FinishOp.Trowel ? "Hold LMB trowel · RMB wet-set J-bolt at the edge · " : "Hold LMB to work the concrete · ") + s;
            }
        }

        public override void PrimaryHeld()
        {
            if (!Ready(0.1f) || !CTool.Aim(out var hit)) return;
            var pour = PourSystem.ForPoint(hit.point);
            if (pour == null) { _status = "Aim at the concrete"; return; }
            if (_op == FinishOp.Vibrate) { if (Battery <= 0f) { _status = "Battery dead"; return; } Battery = Mathf.Max(0f, Battery - 0.0008f); }
            _status = pour.Apply(_op, hit.point);
        }

        public override void Secondary()
        {
            if (_op != FinishOp.Trowel || !CTool.Aim(out var hit)) return;
            var pour = PourSystem.ForPoint(hit.point);
            if (pour != null && pour.WetSetAnchor(hit.point) != null) Game.Toast("J-bolt wet-set, 2 3/4\" exposed");
        }
    }

    public class GrooverTool : ConcreteToolBase
    {
        public override string Id => "groover";
        public override string Name => "Hand Groover";
        bool _has; Vector3 _a;
        public override string Hint => _has ? "LMB second point of the joint (it runs form to form)" : "LMB first point of a control joint (snaps to grid)";

        public override void Primary()
        {
            if (!CTool.Aim(out var hit)) return;
            Vector3 p = CTool.SnapXZ(hit.point);
            if (!_has) { _a = p; _has = true; return; }
            _has = false;
            var pour = PourSystem.ForPoint(_a);
            if (pour == null) { Game.Toast("Groove fresh concrete"); return; }
            string m = pour.Groove(_a, p);
            Game.Toast(string.IsNullOrEmpty(m) ? "Control joint tooled" : m);
        }
    }
}
