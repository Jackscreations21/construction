using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Site layout: corner stakes, mason string lines, squaring by diagonals, laser benchmark/grade, paint marks.</summary>
    public static class SiteLayout
    {
        public class Stake { public Vector3 Ground; public GameObject Go; public GameObject GradeMark; }
        public class Line { public int A, B; public GameObject Go; }

        public static readonly List<Stake> Stakes = new List<Stake>();
        public static readonly List<Line> Lines = new List<Line>();
        public static readonly List<GameObject> Paint = new List<GameObject>();

        public static bool LaserSet;
        public static Vector3 LaserPos;
        public static GameObject LaserGo;
        /// <summary>Benchmark elevation (world Y) the laser was zeroed on (existing grade at the laser).</summary>
        public static float BenchmarkY;
        /// <summary>Finished top of slab / top of form above the benchmark (meters). Default 4".</summary>
        public static float SlabTopAboveBenchmark = 4f * Units.Inch;
        public static float SlabTopY => BenchmarkY + SlabTopAboveBenchmark;

        const float StakeAbove = 12f * Units.Inch;
        static readonly Color StringColor = new Color(1f, 0.25f, 0.55f);   // fluorescent pink mason line

        static Transform _root;
        static Transform Root
        {
            get
            {
                if (_root == null) _root = new GameObject("SiteLayout").transform;
                return _root;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Stakes.Clear(); Lines.Clear(); Paint.Clear(); LaserSet = false; LaserGo = null; BenchmarkY = 0; SlabTopAboveBenchmark = 4f * Units.Inch; _root = null; }

        /// <summary>Drives a 2x2 layout stake at a (grid-snapped) ground point. Returns the stake index.</summary>
        public static int AddStake(Vector3 p)
        {
            p = CTool.SnapXZ(p);
            p.y = SiteGround.GroundY(p);
            foreach (var s in Stakes) if (CTool.XZ(s.Ground - p).magnitude < 0.02f) return Stakes.IndexOf(s);
            var st = new Stake { Ground = p };
            st.Go = CTool.Build("LayoutStake", (b, m) =>
            {
                int wood = m.K(SurfaceKind.PineLumber), paint = m.C(new Color(1f, 0.45f, 0.05f));
                float w = 1.5f * Units.Inch;
                b.AddBox(new Vector3(0, StakeAbove * 0.5f - 0.05f, 0), new Vector3(w, StakeAbove + 0.1f, w), Quaternion.identity, wood, Vector3.up);
                b.AddBox(new Vector3(0, StakeAbove - 0.02f, 0), new Vector3(w + 0.002f, 0.04f, w + 0.002f), Quaternion.identity, paint);
                // 16d nail in the top marking the exact corner
                b.AddCylinder(new Vector3(0, StakeAbove + 0.015f, 0), 0.002f, 0.03f, 6, m.C(new Color(0.6f, 0.6f, 0.6f)));
            }, Root);
            st.Go.transform.position = p;
            Stakes.Add(st);
            if (LaserSet) MarkGrade(Stakes.Count - 1);
            return Stakes.Count - 1;
        }

        public static void AddLine(int a, int b)
        {
            if (a == b || a < 0 || b < 0 || a >= Stakes.Count || b >= Stakes.Count) return;
            foreach (var l in Lines) if ((l.A == a && l.B == b) || (l.A == b && l.B == a)) return;
            var line = new Line { A = a, B = b };
            Vector3 pa = StringPoint(a), pb = StringPoint(b);
            line.Go = CTool.Build("StringLine", (bld, m) =>
            {
                Vector3 d = pb - pa;
                bld.AddRod((pa + pb) * 0.5f, Quaternion.LookRotation(d.normalized, Vector3.up), 0.0012f, d.magnitude, 4, m.C(StringColor), false);
            }, Root);
            Lines.Add(line);
        }

        /// <summary>String height: at the laser's top-of-form mark if set, else 8" above grade (nail height).</summary>
        static Vector3 StringPoint(int i)
        {
            var s = Stakes[i];
            float y = LaserSet ? SlabTopY : s.Ground.y + StakeAbove;
            return new Vector3(s.Ground.x, Mathf.Min(y, s.Ground.y + StakeAbove), s.Ground.z);
        }

        /// <summary>Finds a closed 4-stake loop of lines and returns its diagonals (meters). Equal = square.</summary>
        public static bool TryDiagonals(out float d1, out float d2, out int[] quad)
        {
            d1 = d2 = 0; quad = null;
            var adj = new Dictionary<int, List<int>>();
            foreach (var l in Lines)
            {
                if (!adj.ContainsKey(l.A)) adj[l.A] = new List<int>();
                if (!adj.ContainsKey(l.B)) adj[l.B] = new List<int>();
                adj[l.A].Add(l.B); adj[l.B].Add(l.A);
            }
            foreach (var a in adj.Keys)
                foreach (var b in adj[a])
                    foreach (var c in adj[b])
                    {
                        if (c == a) continue;
                        foreach (var d in adj[c])
                        {
                            if (d == a || d == b) continue;
                            if (!adj[d].Contains(a)) continue;
                            quad = new[] { a, b, c, d };
                            d1 = CTool.XZ(Stakes[a].Ground - Stakes[c].Ground).magnitude;
                            d2 = CTool.XZ(Stakes[b].Ground - Stakes[d].Ground).magnitude;
                            return true;
                        }
                    }
            return false;
        }

        public static string SquareReport()
        {
            if (!TryDiagonals(out float d1, out float d2, out _)) return "Run string around 4 stakes to check square";
            float diff = Mathf.Abs(d1 - d2);
            return $"Diagonals {Units.Format(d1)} / {Units.Format(d2)} - " + (diff <= 0.125f * Units.Inch + 1e-4f ? "SQUARE" : "out " + Units.Format(diff));
        }

        /// <summary>Sets up the rotary laser on its tripod; the benchmark is existing grade at the tripod.</summary>
        public static void SetLaser(Vector3 p)
        {
            p.y = SiteGround.GroundY(p);
            LaserPos = p; BenchmarkY = p.y; LaserSet = true;
            if (LaserGo != null) Object.Destroy(LaserGo);
            LaserGo = new GameObject("RotaryLaser");
            LaserGo.transform.SetParent(Root, false);
            LaserGo.transform.position = p;
            CTool.Build("Tripod", (b, m) =>
            {
                int alu = m.K(SurfaceKind.BrushedSteel), yel = m.C(new Color(0.95f, 0.75f, 0.1f)), blk = m.K(SurfaceKind.MatteBlack);
                for (int k = 0; k < 3; k++)
                {
                    float a = k * 120f;
                    Vector3 foot = Quaternion.Euler(0, a, 0) * new Vector3(0.45f, 0, 0);
                    Vector3 top = new Vector3(0, 1.25f, 0);
                    Vector3 d = top - foot;
                    b.AddRod((top + foot) * 0.5f, Quaternion.LookRotation(d.normalized), 0.014f, d.magnitude, 8, alu);
                }
                b.AddCylinder(new Vector3(0, 1.27f, 0), 0.08f, 0.04f, 16, yel);
                b.AddBox(new Vector3(0, 1.36f, 0), new Vector3(0.16f, 0.14f, 0.16f), Quaternion.identity, yel);
            }, LaserGo.transform);
            var head = CTool.Build("LaserHead", (b, m) =>
            {
                b.AddCylinder(new Vector3(0, 0.03f, 0), 0.05f, 0.06f, 16, m.K(SurfaceKind.MatteBlack));
                b.AddBox(new Vector3(0.04f, 0.03f, 0), new Vector3(0.02f, 0.02f, 0.02f), Quaternion.identity, m.C(new Color(1f, 0.1f, 0.1f)));
            }, LaserGo.transform);
            head.transform.localPosition = new Vector3(0, 1.43f, 0);
            head.AddComponent<Spinner>().DegPerSec = 600f;
            for (int i = 0; i < Stakes.Count; i++) MarkGrade(i);
            RebuildLines();
        }

        /// <summary>Red marking-tape line on a stake at the top-of-form elevation (shot with the grade rod).</summary>
        public static void MarkGrade(int i)
        {
            var s = Stakes[i];
            if (s.GradeMark != null) Object.Destroy(s.GradeMark);
            float y = SlabTopY;
            if (y > s.Ground.y + StakeAbove || y < s.Ground.y - 0.05f) return;
            s.GradeMark = CTool.Build("GradeMark", (b, m) =>
                b.AddBox(Vector3.zero, new Vector3(0.042f, 0.004f, 0.042f), Quaternion.identity, m.C(new Color(0.9f, 0.05f, 0.05f))), s.Go.transform);
            s.GradeMark.transform.position = new Vector3(s.Ground.x, y, s.Ground.z);
        }

        public static void SetSlabTop(float aboveBenchmark)
        {
            SlabTopAboveBenchmark = Mathf.Clamp(aboveBenchmark, -12f * Units.Inch, 12f * Units.Inch);
            for (int i = 0; i < Stakes.Count; i++) MarkGrade(i);
            RebuildLines();
        }

        static void RebuildLines()
        {
            var old = new List<Line>(Lines);
            foreach (var l in old) if (l.Go != null) Object.Destroy(l.Go);
            Lines.Clear();
            foreach (var l in old) AddLine(l.A, l.B);
        }

        /// <summary>Rod reading (meters below the laser beam) at a ground point. Beam is 1.43 m above the benchmark.</summary>
        public static float RodReading(Vector3 p) => BenchmarkY + 1.43f - SiteGround.GroundY(p);

        public static void SprayMark(Vector3 p, Vector3 normal, Color c)
        {
            var go = CTool.Build("PaintMark", (b, m) =>
            {
                int mat = m.C(c);
                var pts = new List<Vector3>();
                for (int k = 0; k < 10; k++)
                {
                    float a = k * 36f * Mathf.Deg2Rad, r = 0.045f * (0.8f + 0.4f * Mathf.PerlinNoise(p.x * 7 + k, p.z * 7));
                    pts.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
                }
                b.AddPolygon(pts, mat, Vector3.up);
            }, Root);
            go.transform.position = p + normal * 0.004f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            Paint.Add(go);
        }
    }

    public class Spinner : MonoBehaviour
    {
        public float DegPerSec = 90f; public Vector3 Axis = Vector3.up;
        void Update() => transform.Rotate(Axis, DegPerSec * Time.deltaTime, Space.Self);
    }

    // ============================================================== tools

    public class StakeLineTool : ConcreteToolBase
    {
        public override string Id => "stakeline";
        public override string Name => "Stakes & String";
        int _chainStart = -1, _last = -1;

        public override string Hint => "LMB drive stake + run string from last · RMB close loop to first stake · " + SiteLayout.SquareReport();

        public override void Primary()
        {
            if (!CTool.Aim(out var hit)) return;
            int i = SiteLayout.AddStake(hit.point);
            if (_last >= 0 && _last != i) SiteLayout.AddLine(_last, i);
            if (_chainStart < 0) _chainStart = i;
            _last = i;
            Game.Toast($"Stake {SiteLayout.Stakes.Count} driven · {SiteLayout.SquareReport()}");
        }

        public override void Secondary()
        {
            if (_last >= 0 && _chainStart >= 0 && _last != _chainStart) SiteLayout.AddLine(_last, _chainStart);
            _chainStart = _last = -1;
            Game.Toast(SiteLayout.SquareReport());
        }

        public override void Tick() { _status = SiteLayout.SquareReport(); }
    }

    public class LaserLevelTool : ConcreteToolBase
    {
        public override string Id => "laser";
        public override string Name => "Rotary Laser + Grade Rod";
        public override string Hint => SiteLayout.LaserSet
            ? $"LMB shoot rod at aim · RMB move laser · Scroll top of slab = {Units.Format(SiteLayout.SlabTopAboveBenchmark)} above benchmark"
            : "LMB set up the laser on its tripod (benchmark = grade there)";

        public override void Primary()
        {
            if (!CTool.Aim(out var hit, 30f)) return;
            if (!SiteLayout.LaserSet) { SiteLayout.SetLaser(hit.point); Game.Toast("Laser set up. Benchmark = existing grade at the tripod."); return; }
            float cut = SiteGround.GroundY(hit.point) - SiteLayout.BenchmarkY;
            Game.Toast($"Rod reads {Units.Format(SiteLayout.RodReading(hit.point))} · grade is {(cut >= 0 ? "+" : "-")}{Units.Format(Mathf.Abs(cut))} vs benchmark · top of slab {Units.Format(SiteLayout.SlabTopY - SiteGround.GroundY(hit.point))} above here");
        }

        public override void Secondary()
        {
            if (CTool.Aim(out var hit, 30f)) SiteLayout.SetLaser(hit.point);
        }

        public override void Tick()
        {
            float s = GInput.Scroll;
            if (s != 0f && SiteLayout.LaserSet) SiteLayout.SetSlabTop(SiteLayout.SlabTopAboveBenchmark + s * 0.5f * Units.Inch);
            _status = SiteLayout.LaserSet && CTool.Aim(out var hit, 30f) ? $"Rod: {Units.Format(SiteLayout.RodReading(hit.point))}" : "";
        }
    }

    public class MarkingPaintTool : ConcreteToolBase
    {
        public override string Id => "marking-paint";
        public override string Name => "Marking Paint";
        static readonly Color[] Colors = { new Color(0.95f, 0.95f, 0.92f), new Color(1f, 0.45f, 0.05f), new Color(1f, 0.25f, 0.6f) };
        static readonly string[] Names = { "white (proposed excavation)", "orange", "pink (survey)" };
        int _c;
        Vector3 _lastMark = Vector3.one * 9999f;
        public override string Hint => "Hold LMB to spray · Scroll color: " + Names[_c];

        public override void PrimaryHeld()
        {
            if (!CTool.Aim(out var hit)) return;
            if ((hit.point - _lastMark).sqrMagnitude < 0.05f * 0.05f) return;
            SiteLayout.SprayMark(hit.point, hit.normal, Colors[_c]);
            _lastMark = hit.point;
        }

        public override void Tick()
        {
            float s = GInput.Scroll;
            if (s != 0) _c = (_c + (s > 0 ? 1 : 2)) % Colors.Length;
        }
    }
}
