using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One flat roof plane, described at the TOP SURFACE OF THE SHEATHING (what roofing is laid on).
    /// Plane coordinates: u along the eave (to the right when standing outside facing up-slope), v up the slope from
    /// the eave edge, h along the outward normal. Rakes are vertical in (u,v); a hip edge moves inward by v*cos(pitch).</summary>
    [System.Serializable]
    public class RoofPlane
    {
        public string Name = "Plane";
        /// <summary>World point: sheathing top surface at the eave edge, left end (u = 0, v = 0).</summary>
        public Vector3 EaveStart;
        public Vector3 AlongEave = Vector3.right;   // unit, horizontal
        public Vector3 UpSlope = Vector3.forward;   // unit, in the plane, toward the ridge
        public Vector3 Normal = Vector3.up;         // unit, outward
        public float EaveLength;                    // meters along the eave edge (v = 0)
        public float SlopeLength;                   // meters from the eave edge to the ridge / apex
        public int PitchRise = 6;                   // rise per 12" of run
        public bool HipStart, HipEnd;               // left / right edge is a hip (else a rake)
        /// <summary>Along-slope distance below the ridge left open in the deck for the ridge vent (0 = none).</summary>
        public float RidgeGap;
        /// <summary>u range of the ridge-vent slot at the top edge.</summary>
        public float VentU0, VentU1;
        /// <summary>Rafter / truss centerlines as u positions (sheathing joints land on these).</summary>
        public readonly List<float> FramingU = new List<float>();
        public float SheathingThickness = Units.In(0.4375f);
        /// <summary>Fascia (1x6) thickness outside the sheathing edge at eaves and rakes.</summary>
        public float FasciaOutset = Units.In(0.75f);

        public float Pitch => PitchRise / 12f;
        public float AngleDeg => Mathf.Atan(Pitch) * Mathf.Rad2Deg;
        public float Cos => Mathf.Cos(Mathf.Atan(Pitch));
        public float Sin => Mathf.Sin(Mathf.Atan(Pitch));
        /// <summary>Horizontal unit vector pointing out of the building past the eave.</summary>
        public Vector3 OutHorizontal { get { var d = -UpSlope; d.y = 0f; return d.normalized; } }
        /// <summary>Local frame: X along eave, Y normal, Z up-slope.</summary>
        public Quaternion Frame => Quaternion.LookRotation(UpSlope, Normal);

        public float UMin(float v) => HipStart ? v * Cos : 0f;
        public float UMax(float v) => EaveLength - (HipEnd ? v * Cos : 0f);
        public float WidthAt(float v) => Mathf.Max(0f, UMax(v) - UMin(v));

        public Vector3 At(float u, float v, float h = 0f) => EaveStart + AlongEave * u + UpSlope * v + Normal * h;

        /// <summary>(u, v, h) of a world point.</summary>
        public Vector3 ToLocal(Vector3 world)
        {
            var d = world - EaveStart;
            return new Vector3(Vector3.Dot(d, AlongEave), Vector3.Dot(d, UpSlope), Vector3.Dot(d, Normal));
        }

        public bool Contains(float u, float v, float margin = 0f) =>
            v >= -margin && v <= SlopeLength + margin && u >= UMin(v) - margin && u <= UMax(v) + margin;

        public float Area => (EaveLength + WidthAt(SlopeLength)) * 0.5f * SlopeLength;

        /// <summary>Plane outline as half-planes in (u,v), with extra overhang beyond rakes and eave, clipped at vTop.</summary>
        public List<HalfPlane> Bounds(float rakeOut, float eaveOut, float vTop)
        {
            float c = Cos;
            var l = new List<HalfPlane>(4);
            l.Add(HipStart ? new HalfPlane(Vector2.zero, new Vector2(1f, -c)) : HalfPlane.XMin(-rakeOut));
            l.Add(HipEnd ? new HalfPlane(new Vector2(EaveLength, 0f), new Vector2(-1f, -c)) : HalfPlane.XMax(EaveLength + rakeOut));
            l.Add(HalfPlane.YMin(-eaveOut));
            l.Add(HalfPlane.YMax(vTop));
            return l;
        }
    }

    /// <summary>A straight roof edge (ridge, hip, valley, rake) in world space at the sheathing top. A = low / start end.</summary>
    [System.Serializable]
    public struct RoofEdge
    {
        public Vector3 A, B;
        public int PlaneA, PlaneB;   // planes on either side (-1 = none)
        public RoofEdge(Vector3 a, Vector3 b, int pa, int pb) { A = a; B = b; PlaneA = pa; PlaneB = pb; }
        public float Length => Vector3.Distance(A, B);
    }

    /// <summary>The roof produced by roof framing (on the framing job's GameObject: <c>job.GetComponent&lt;RoofInfo&gt;()</c> or
    /// <see cref="RoofFraming.InfoOf"/>). Roofing jobs consume its planes; RoofSafety uses it to know where the player stands.</summary>
    public class RoofInfo : MonoBehaviour
    {
        public static readonly List<RoofInfo> All = new List<RoofInfo>();
        public static RoofInfo Latest => All.Count > 0 ? All[All.Count - 1] : null;

        public Footprint Footprint;
        public int PitchRise = 6;
        public bool Hip, Trussed;
        public float EaveOverhang, RakeOverhang;  // meters, to the face of the fascia
        public readonly List<RoofPlane> Planes = new List<RoofPlane>();
        public bool HasRidge;
        public RoofEdge Ridge;
        /// <summary>Roof frame (footprint) heights: top of the framing at the outer wall line and rise per run (gable sheathing).</summary>
        public float FramingTopAtWall, Slope;
        public readonly List<RoofEdge> Hips = new List<RoofEdge>();
        public readonly List<RoofEdge> Valleys = new List<RoofEdge>();
        public readonly List<RoofEdge> Rakes = new List<RoofEdge>();
        public Vector3 RidgeA => Ridge.A;
        public Vector3 RidgeB => Ridge.B;

        /// <summary>Progress flags (set by the jobs as they complete).</summary>
        public bool Framed, Sheathed, DriedIn, Shingled;
        /// <summary>True once any deck panel is down (something to stand on).</summary>
        public bool HasDeck;
        public FramingJob FramingJob, SheathingJob, DryInJob, ShinglesJob;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>The plane a world point is on (inside its outline, h within [-below, above] of the deck top).</summary>
        public RoofPlane PlaneAt(Vector3 world, float above, float below, out Vector3 local)
        {
            local = Vector3.zero;
            RoofPlane best = null; float bestH = float.MaxValue;
            foreach (var p in Planes)
            {
                var l = p.ToLocal(world);
                if (!p.Contains(l.x, l.y, 0.05f)) continue;
                if (l.z < -below || l.z > above) continue;
                if (Mathf.Abs(l.z) < bestH) { bestH = Mathf.Abs(l.z); best = p; local = l; }
            }
            return best;
        }

        public static RoofPlane FindPlane(Vector3 world, float above, float below, out RoofInfo info, out Vector3 local)
        {
            info = null; local = Vector3.zero;
            foreach (var r in All)
            {
                if (r == null) continue;
                var p = r.PlaneAt(world, above, below, out var l);
                if (p != null) { info = r; local = l; return p; }
            }
            return null;
        }

        public int IndexOf(RoofPlane p) => Planes.IndexOf(p);
    }
}
