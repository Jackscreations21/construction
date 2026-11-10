using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A graph edge: one length of pipe / cable / duct along a polyline (world points).
    /// Each end is connected to a NetPort or left free (open end). Child colliders let tools aim at it.</summary>
    public class NetRun : MonoBehaviour
    {
        public RunSpec Spec;
        public readonly List<Vector3> Points = new List<Vector3>();
        public NetPort A, B;
        /// <summary>Staple positions as distance (m) from end A (Romex, PEX on studs, line set straps).</summary>
        public readonly List<float> Staples = new List<float>();
        /// <summary>Free-form tags for trade logic (e.g. "switchleg", "homerun", "travelers").</summary>
        public readonly HashSet<string> Tags = new HashSet<string>();

        GameObject _vis;

        public static NetRun Create(RunSpec spec, IList<Vector3> pts, NetPort a = null, NetPort b = null)
        {
            var go = new GameObject(spec.Name + " run");
            go.transform.position = pts[0];
            var r = go.AddComponent<NetRun>();
            r.Spec = spec;
            r.Points.AddRange(pts);
            if (a != null) r.Attach(a, true);
            if (b != null) r.Attach(b, false);
            r.Rebuild();
            NetGraph.Runs.Add(r);
            NetGraph.Touch();
            return r;
        }

        public Vector3 StartPoint => Points[0];
        public Vector3 EndPoint => Points[Points.Count - 1];
        public NetNode NodeA => A != null ? A.Node : null;
        public NetNode NodeB => B != null ? B.Node : null;
        public NetNode Other(NetNode n) => NodeA == n ? NodeB : NodeB == n ? NodeA : null;
        public NetPort PortAt(NetNode n) => NodeA == n ? A : NodeB == n ? B : null;
        public bool FreeEndA => A == null;
        public bool FreeEndB => B == null;

        public float Length
        {
            get { float l = 0; for (int i = 1; i < Points.Count; i++) l += Vector3.Distance(Points[i - 1], Points[i]); return l; }
        }
        public float LengthFeet => Length / Units.Foot;

        /// <summary>Connects one end (atStart = end A) to a port; moves the end point onto the port.</summary>
        public void Attach(NetPort p, bool atStart)
        {
            if (p == null) return;
            if (atStart) { if (A != null) Detach(A); A = p; Points[0] = p.WorldPos; }
            else { if (B != null) Detach(B); B = p; Points[Points.Count - 1] = p.WorldPos; }
            if (p.Run == null) p.Run = this; else if (p.Run != this && !p.ExtraRuns.Contains(this)) p.ExtraRuns.Add(this);
            if (p.Join == JoinMethod.None) p.Joined = true;
            NetGraph.Touch();
        }

        public void Detach(NetPort p)
        {
            if (p == null) return;
            if (p.Run == this)
            {
                p.Run = null; p.Joined = false;
                if (p.ExtraRuns.Count > 0) { p.Run = p.ExtraRuns[0]; p.ExtraRuns.RemoveAt(0); p.Joined = p.Join == JoinMethod.None; }
            }
            p.ExtraRuns.Remove(this);
            if (A == p) A = null;
            if (B == p) B = null;
            if (p.Node != null) p.Node.RefreshJoints();
            NetGraph.Touch();
        }

        void OnDestroy()
        {
            NetGraph.Runs.Remove(this);
            var a = A; var b = B;
            if (a != null) Detach(a);
            if (b != null) Detach(b);
        }

        /// <summary>Slope of segment i in inches per foot (positive = falling from point i to i+1).</summary>
        public float SlopeInPerFt(int seg)
        {
            Vector3 d = Points[seg + 1] - Points[seg];
            float h = new Vector2(d.x, d.z).magnitude;
            if (h < 1e-4f) return float.PositiveInfinity; // vertical
            return Units.ToInches(-d.y) / (h / Units.Foot);
        }

        public bool SegmentIsHorizontal(int seg)
        {
            Vector3 d = Points[seg + 1] - Points[seg];
            return Mathf.Abs(d.y) < new Vector2(d.x, d.z).magnitude * 0.2f; // < ~11°: "horizontal" in code terms
        }

        /// <summary>World point at distance s from end A.</summary>
        public Vector3 PointAt(float s, out Vector3 dir)
        {
            dir = Vector3.forward;
            for (int i = 1; i < Points.Count; i++)
            {
                float l = Vector3.Distance(Points[i - 1], Points[i]);
                dir = (Points[i] - Points[i - 1]).normalized;
                if (s <= l || i == Points.Count - 1) return Points[i - 1] + dir * Mathf.Clamp(s, 0, l);
                s -= l;
            }
            return Points[0];
        }

        /// <summary>Distance along the run of the closest point to p.</summary>
        public float DistanceAlong(Vector3 p, out float offAxis)
        {
            float best = float.MaxValue, acc = 0f, at = 0f;
            for (int i = 1; i < Points.Count; i++)
            {
                Vector3 a = Points[i - 1], b = Points[i];
                float l = Vector3.Distance(a, b);
                float t = l > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, b - a) / (l * l)) : 0f;
                float d = Vector3.Distance(p, a + (b - a) * t);
                if (d < best) { best = d; at = acc + t * l; }
                acc += l;
            }
            offAxis = best;
            return at;
        }

        /// <summary>Cuts the run at the point closest to p. This run keeps end A; returns the new run holding end B.
        /// Both new ends are free (open). Returns null if the cut is too close to an end.</summary>
        public NetRun SplitAt(Vector3 p, float gap = 0.003f)
        {
            float s = DistanceAlong(p, out _);
            float L = Length;
            if (s < 0.02f || s > L - 0.02f) return null;
            var first = new List<Vector3>(); var second = new List<Vector3>();
            float acc = 0f; bool cut = false;
            first.Add(Points[0]);
            for (int i = 1; i < Points.Count; i++)
            {
                float l = Vector3.Distance(Points[i - 1], Points[i]);
                if (!cut && acc + l >= s)
                {
                    Vector3 d = (Points[i] - Points[i - 1]).normalized;
                    Vector3 c = Points[i - 1] + d * (s - acc);
                    first.Add(c - d * gap * 0.5f);
                    second.Add(c + d * gap * 0.5f);
                    cut = true;
                }
                if (cut) second.Add(Points[i]); else first.Add(Points[i]);
                acc += l;
            }
            var oldB = B;
            if (oldB != null) Detach(oldB);
            Points.Clear(); Points.AddRange(first);
            var nr = Create(Spec, second);
            foreach (var t in Tags) nr.Tags.Add(t);
            if (oldB != null) nr.Attach(oldB, false);
            // staples follow their side
            var keep = new List<float>();
            foreach (var st in Staples) { if (st < s) keep.Add(st); else nr.Staples.Add(st - s); }
            Staples.Clear(); Staples.AddRange(keep);
            Rebuild(); nr.Rebuild();
            if (oldB != null && oldB.Node != null) oldB.Node.RefreshJoints();
            NetGraph.Touch();
            return nr;
        }

        /// <summary>Polyline used for drawing (inline bends rounded).</summary>
        public List<Vector3> DrawPath()
        {
            if (Spec.BendsInline && Points.Count > 2) return MepMesh.RoundCorners(Points, Mathf.Max(Spec.MinBendRadius, Spec.Width * 3f), 8);
            return new List<Vector3>(Points);
        }

        public void Rebuild()
        {
            if (_vis != null) Destroy(_vis);
            foreach (Transform c in transform) if (c.name == "Seg") Destroy(c.gameObject);
            transform.position = Points[0];
            var mm = new MepMesh { Origin = Points[0] };
            var path = DrawPath();
            var mats = new List<Material> { MepLook.Of(Spec) };
            mm.Sweep(path, MepMesh.For(Spec), 0);
            if (Spec.Shape == RunShape.Round && Spec.Kind == NetKind.Duct)
            {
                // round duct: crimped male end bead + longitudinal seam hint every 5' section
                float L = Length, sec = 5 * Units.Foot;
                for (float s = sec; s < L - 0.05f; s += sec)
                {
                    var c = PointAt(s, out var d);
                    mm.Band(c - d * 0.01f, c + d * 0.01f, MepMesh.Circle(Spec.Radius * 1.012f, 24), 0);
                }
            }
            if (Staples.Count > 0)
            {
                mats.Add(MepLook.Get(SurfaceKind.Galvanized));
                foreach (var st in Staples)
                {
                    var c = PointAt(st, out var d);
                    var side = Vector3.Cross(d, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.right; side.Normalize();
                    float w = Spec.Width * 0.5f + 0.004f, h = Mathf.Max(Spec.Height, Spec.Width) * 0.5f + 0.002f;
                    Vector3 up = Vector3.Cross(side, d).normalized;
                    if (Vector3.Dot(up, Vector3.up) < 0) up = -up;
                    // the staple straddles the cable on its "front" (horizontal side) face; a plastic insulated staple + nails
                    Vector3 face = Mathf.Abs(d.y) > 0.7f ? side : up;
                    Vector3 across = Vector3.Cross(d, face).normalized;
                    var prof = MepMesh.Rect(0.004f, 0.0025f);
                    mm.Band(c + face * h - across * w, c + face * h + across * w, prof, 1);
                    mm.Band(c + across * w, c + across * w + face * h, prof, 1);
                    mm.Band(c - across * w, c - across * w + face * h, prof, 1);
                }
            }
            _vis = mm.Spawn(transform, "Visual", mats.ToArray());
            // colliders per straight segment
            for (int i = 1; i < Points.Count; i++)
            {
                Vector3 a = Points[i - 1], b = Points[i];
                float l = Vector3.Distance(a, b);
                if (l < 1e-4f) continue;
                var seg = new GameObject("Seg");
                seg.transform.SetParent(transform, false);
                seg.transform.position = (a + b) * 0.5f;
                seg.transform.rotation = Quaternion.LookRotation(b - a, Mathf.Abs((b - a).normalized.y) > 0.99f ? Vector3.forward : Vector3.up);
                var bc = seg.AddComponent<BoxCollider>();
                float th = Mathf.Max(Spec.Width, 0.02f);
                bc.size = new Vector3(th, Mathf.Max(Spec.Shape == RunShape.Round ? Spec.Width : Spec.Height, 0.02f), l);
            }
        }

        public void AddStaple(Vector3 worldPoint)
        {
            float s = DistanceAlong(worldPoint, out _);
            Staples.Add(s); Staples.Sort();
            Rebuild();
            NetGraph.Touch();
        }

        public static NetRun FromCollider(Collider c) => c != null ? c.GetComponentInParent<NetRun>() : null;
    }
}
