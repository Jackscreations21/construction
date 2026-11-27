using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Sheet-metal fabrication detail on duct runs, as a "Detail" child of each run (rebuilt when the run changes):
    /// rectangular trunk = 5' sections with Pittsburgh corner seams, S-slips top/bottom + drive cleats on the sides with the
    /// tabs folded over, cross-breaks, gray mastic brushed over each joint. Round pipe = snap-lock seam per section, crimped male
    /// ends (bare until sealed), 3 hex-head sheet-metal screws per joint, foil tape once the run is sealed.
    /// Runs in an unconditioned attic (over ceiling joists, outside every room, or tagged "attic") get an R-8 foil-faced wrap
    /// with taped seams and the fiberglass showing at the cut ends.</summary>
    public static class DuctDetail
    {
        static float I(float x) => Units.In(x);
        const float Section = 60f; // inches: 5' sections

        static readonly Dictionary<NetRun, int> _built = new Dictionary<NetRun, int>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _built.Clear(); _mastic = null; _jacket = null; }

        static Material _mastic, _jacket;
        public static Material Mastic => _mastic != null ? _mastic : (_mastic = Materials.Get(SurfaceKind.PaintedWhite, new Color(0.44f, 0.45f, 0.43f)));
        public static Material Jacket
        {
            get
            {
                if (_jacket != null) return _jacket;
                _jacket = new Material(MepPrint.FoilTape) { name = "GB_DuctWrapJacket", hideFlags = HideFlags.DontUnloadUnusedAsset };
                _jacket.color = MepPrint.FoilTape.color * new Color(0.74f, 0.74f, 0.72f);
                if (_jacket.HasProperty("_Glossiness")) _jacket.SetFloat("_Glossiness", 0.35f);
                return _jacket;
            }
        }
        static Material Fiberglass => Materials.Get(SurfaceKind.PaintedWhite, new Color(1.15f, 0.92f, 0.45f));

        /// <summary>Call regularly (HvacSystem does, ~1 Hz and on every graph change).</summary>
        public static void Apply()
        {
            var dead = new List<NetRun>();
            foreach (var kv in _built) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var d in dead) _built.Remove(d);
            foreach (var r in NetGraph.RunsOf(Trade.Hvac))
            {
                if (r == null || r.Spec == null || r.Spec.Kind != NetKind.Duct || r.Spec.BendsInline || r.Points.Count < 2) continue;
                int h = Hash(r);
                if (_built.TryGetValue(r, out int old) && old == h && r.transform.Find("Detail") != null) continue;
                _built[r] = h;
                Build(r);
            }
        }

        /// <summary>Both ends joined (taped) or capped; a free end is an open duct. Tag "sealed" forces it (tests / mastic-sealed).</summary>
        static bool Sealed(NetRun r) => r.Tags.Contains("sealed") || (r.A != null && (r.A.Joined || r.A.Capped) && r.B != null && (r.B.Joined || r.B.Capped));

        public static bool InAttic(NetRun r)
        {
            if (r.Tags.Contains("attic")) return true;
            if (r.Tags.Contains("conditioned")) return false;
            Vector3 m = r.PointAt(r.Length * 0.5f, out _);
            if (RoomZone.At(m) != null) return false;
            float best = 1.3f; Piece hit = null;
            foreach (var h in Physics.RaycastAll(m, Vector3.down, 1.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(r.transform)) continue;
                var p = h.collider.GetComponentInParent<Piece>();
                if (p == null || p.Def == null || h.distance >= best) continue;
                best = h.distance; hit = p;
            }
            // over ceiling joists (horizontal lumber): unconditioned attic
            return hit != null && hit.Def.Category == PieceCategory.Lumber && Mathf.Abs(hit.transform.forward.y) < 0.5f;
        }

        static int Hash(NetRun r)
        {
            int h = r.Points.Count * 7 + (Sealed(r) ? 3 : 0) + (InAttic(r) ? 5 : 0);
            foreach (var p in r.Points) h = h * 31 + Mathf.RoundToInt(p.x * 500) * 3 + Mathf.RoundToInt(p.y * 500) * 5 + Mathf.RoundToInt(p.z * 500);
            return h;
        }

        static void Frame(Vector3 d, out Vector3 up, out Vector3 side)
        {
            up = Mathf.Abs(d.y) > 0.9f ? Vector3.forward : Vector3.up;
            side = Vector3.Cross(up, d).normalized;
            up = Vector3.Cross(d, side).normalized;
        }

        public static void Build(NetRun r)
        {
            for (int i = r.transform.childCount - 1; i >= 0; i--) { var c = r.transform.GetChild(i); if (c.name == "Detail") MepMesh.DestroyVisual(c.gameObject); }
            var s = r.Spec;
            var mm = new MepMesh { Origin = r.transform.position };
            var mats = new List<Material> { HvacLook.Galv, Mastic, MepPrint.FoilTape, Jacket, Fiberglass };
            const int G = 0, M = 1, T = 2, J = 3, FG = 4;
            bool round = s.Shape == RunShape.Round;
            float w = s.Width, h = round ? s.Width : s.Height, rad = s.Radius;
            float L = r.Length;

            if (InAttic(r))
            {
                // R-8 wrap: 2" blanket, foil-scrim jacket, 4' roll widths taped, fiberglass showing at the cut ends
                float t = I(2f);
                var path = r.DrawPath();
                var prof = round ? MepMesh.Circle(rad + t, 24) : MepMesh.Rect(w + 2 * t, h + 2 * t);
                var fgProf = round ? MepMesh.Circle(rad + t * 0.96f, 24) : MepMesh.Rect(w + 1.92f * t, h + 1.92f * t);
                Vector3 a0 = r.PointAt(I(1.5f), out var da), b0 = r.PointAt(L - I(1.5f), out var db);
                // trim the wrap back 1.5" from the fittings (taped to them in reality)
                var wp = new List<Vector3> { a0 };
                for (int i = 1; i < path.Count - 1; i++) wp.Add(path[i]);
                wp.Add(b0);
                mm.Sweep(wp, prof, J);
                mm.Band(a0 - da * I(0.3f), a0, fgProf, FG);
                mm.Band(b0, b0 + db * I(0.3f), fgProf, FG);
                var tape = round ? MepMesh.Circle(rad + t + I(0.04f), 24) : MepMesh.Rect(w + 2 * t + I(0.08f), h + 2 * t + I(0.08f));
                for (float x = 48f; x < L / Units.Inch - 6f; x += 48f)
                {
                    var c = r.PointAt(I(x), out var d);
                    mm.Band(c - d * I(1.5f), c + d * I(1.5f), tape, T);
                }
            }
            else if (round) BuildRound(r, mm, rad, L, Sealed(r), G, T);
            else BuildRect(r, mm, w, h, L, G, M);

            mm.Spawn(r.transform, "Detail", mats.ToArray());
        }

        static void BuildRect(NetRun r, MepMesh mm, float w, float h, float L, int G, int M)
        {
            float lip = I(0.07f);
            // Pittsburgh lock seams on two opposite corners along each straight segment
            for (int i = 1; i < r.Points.Count; i++)
            {
                Vector3 a = r.Points[i - 1], b = r.Points[i];
                float sl = Vector3.Distance(a, b);
                if (sl < I(4f)) continue;
                Vector3 d = (b - a) / sl; Frame(d, out var up, out var side);
                var rot = Quaternion.LookRotation(d, up);
                foreach (var k in new[] { 1f, -1f })
                {
                    Vector3 c = (a + b) * 0.5f + side * k * (w * 0.5f + lip * 0.5f) + up * k * (h * 0.5f - I(0.15f));
                    Box(mm, c, rot, new Vector3(lip, I(0.4f), sl - I(1f)), G);
                }
                // cross-breaks on the wide faces, per 5' section
                for (float s0 = 0; s0 < sl - I(6f); s0 += I(Section))
                {
                    float seg = Mathf.Min(I(Section), sl - s0) - I(1.5f);
                    Vector3 mid = a + d * (s0 + seg * 0.5f + I(0.75f));
                    float ang = Mathf.Atan2(w, seg) * Mathf.Rad2Deg, diag = Mathf.Sqrt(w * w + seg * seg) - I(1f);
                    foreach (var sgn in new[] { 1f, -1f })
                        foreach (var face in new[] { 1f, -1f })
                            Box(mm, mid + up * face * (h * 0.5f + I(0.012f)), rot * Quaternion.Euler(0, sgn * ang, 0), new Vector3(I(0.06f), I(0.02f), diag), G);
                }
            }
            // transverse joints every 5' along the run: S-slips top/bottom, drive cleats on the sides, mastic over it all
            for (float x = Section; x < L / Units.Inch - 4f; x += Section)
            {
                var c = r.PointAt(I(x), out var d);
                Frame(d, out var up, out var side);
                var rot = Quaternion.LookRotation(d, up);
                // mastic: brushed on in uneven overlapping strokes ~3-5" wide around the joint
                int seed = Mathf.RoundToInt(x * 13f);
                for (int st = 0; st < 3; st++)
                {
                    float len = I(2.6f + Hash01(seed + st) * 2.2f), shift = I((Hash01(seed + st * 7 + 3) - 0.5f) * 1.6f), th = I(0.025f + st * 0.012f);
                    Vector3 cc = c + d * shift;
                    Box(mm, cc + up * (h * 0.5f + th * 0.5f), rot, new Vector3(w + I(0.06f) - st * I(0.8f), th, len), M);
                    Box(mm, cc - up * (h * 0.5f + th * 0.5f), rot, new Vector3(w + I(0.06f) - st * I(0.8f), th, len * 0.9f), M);
                    Box(mm, cc + side * (w * 0.5f + th * 0.5f), rot, new Vector3(th, h + I(0.06f) - st * I(0.7f), len * 1.05f), M);
                    Box(mm, cc - side * (w * 0.5f + th * 0.5f), rot, new Vector3(th, h + I(0.06f) - st * I(0.7f), len * 0.95f), M);
                }
                // S-slip on top and bottom
                foreach (var k in new[] { 1f, -1f })
                    Box(mm, c + up * k * (h * 0.5f + I(0.07f)), rot, new Vector3(w + I(0.3f), I(0.1f), I(1.0f)), G);
                // drive cleats on the sides with the tabs folded over the slips
                foreach (var k in new[] { 1f, -1f })
                {
                    Box(mm, c + side * k * (w * 0.5f + I(0.08f)), rot, new Vector3(I(0.12f), h + I(0.4f), I(1.25f)), G);
                    foreach (var v in new[] { 1f, -1f })
                        Box(mm, c + side * k * (w * 0.5f - I(0.15f)) + up * v * (h * 0.5f + I(0.15f)), rot, new Vector3(I(0.55f), I(0.06f), I(1.25f)), G);
                }
            }
        }

        static void BuildRound(NetRun r, MepMesh mm, float rad, float L, bool sealed_, int G, int T)
        {
            // snap-lock seam per 5' section, the section's seam clocked a little differently each time
            int k = 0;
            for (float x = 0; x < L / Units.Inch - 2f; x += Section, k++)
            {
                float x1 = Mathf.Min(x + Section, L / Units.Inch);
                var c0 = r.PointAt(I(x + 1.5f), out var d0); var c1 = r.PointAt(I(x1 - 1.5f), out _);
                if (Vector3.Distance(c0, c1) < I(3f)) continue;
                Frame(d0, out var up, out var side);
                float ang = (20f + (k % 3) * 25f) * Mathf.Deg2Rad;
                Vector3 n = (up * Mathf.Cos(ang) + side * Mathf.Sin(ang)).normalized;
                var rot = Quaternion.LookRotation((c1 - c0).normalized, n);
                Box(mm, (c0 + c1) * 0.5f + n * (rad + I(0.03f)), rot, new Vector3(I(0.14f), I(0.07f), Vector3.Distance(c0, c1)), G);
            }
            var hex = MepMesh.Circle(I(0.125f), 6);
            for (float x = Section; x < L / Units.Inch - 3f; x += Section)
            {
                var c = r.PointAt(I(x), out var d);
                Frame(d, out var up, out var side);
                // lap: the plain (female) end of the upstream section over the crimped male end
                mm.Band(c - d * I(1.6f), c, MepMesh.Circle(rad * 1.008f + I(0.02f), 28), G);
                if (!sealed_)
                    for (int i = 0; i < 7; i++) // crimp corrugations on the exposed male end
                    {
                        Vector3 q = c + d * I(0.25f + i * 0.14f);
                        mm.Band(q - d * I(0.035f), q + d * I(0.035f), MepMesh.Circle(rad * 1.004f + I(0.01f), 28), G);
                    }
                // 3 #8 x 1/2" hex-washer zip screws through the lap
                for (int i = 0; i < 3; i++)
                {
                    float a = (i * 120f + 30f) * Mathf.Deg2Rad;
                    Vector3 n = up * Mathf.Cos(a) + side * Mathf.Sin(a);
                    Vector3 p = c - d * I(0.8f) + n * (rad * 1.008f + I(0.02f));
                    mm.Band(p, p + n * I(0.03f), MepMesh.Circle(I(0.2f), 12), G);       // washer
                    mm.Band(p + n * I(0.03f), p + n * I(0.13f), hex, G);              // hex head
                }
                if (sealed_) mm.Band(c - d * I(1.25f), c + d * I(1.25f), MepMesh.Circle(rad * 1.012f + I(0.045f), 28), T);
            }
        }

        static float Hash01(int n) { n = (n << 13) ^ n; return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f; }

        static void Box(MepMesh mm, Vector3 center, Quaternion rot, Vector3 size, int sub)
        {
            // MepMesh.Box orients by world up; sweep a rect explicitly in rot's frame instead
            Vector3 f = rot * Vector3.forward * (size.z * 0.5f);
            mm.Sweep(new[] { center - f, center + f }, MepMesh.Rect(size.x, size.y), sub, true, true, rot * Vector3.up);
        }
    }
}
