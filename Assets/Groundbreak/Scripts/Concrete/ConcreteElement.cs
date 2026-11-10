using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// A finished (set) concrete slab or footing. Public API for other trades (sill plates, posts, slabs-on-grade):
    ///   ConcreteElement.All / Created event · TopY (world Y of the finished top) · Outline (world XZ polygon of the edge)
    ///   Contains(p) · At(p) · TryGetTop(p, out y) · Cured (≥ 7 days, OK to load/frame) · Strength01 (vs 28-day) ·
    ///   AnchorBolts (J-bolt transforms; bolt axis at transform.position, top of nut ~1 1/4" above TopY).
    /// The GameObject carries a MeshCollider, so raycasts / placement land on the slab.
    /// </summary>
    public class ConcreteElement : MonoBehaviour
    {
        static readonly List<ConcreteElement> _all = new List<ConcreteElement>();
        public static IReadOnlyList<ConcreteElement> All => _all;
        public static event Action<ConcreteElement> Created;

        public string Kind = "slab";
        public float TopY;
        public Vector2[] Outline;
        public float ThicknessInches;
        public int Psi;
        public float PouredAt;               // game minutes
        public float Yards;
        public SurfaceKind Finish;
        public bool Covered, WetCured, FormsStripped;
        public readonly List<string> Defects = new List<string>();
        public readonly List<Transform> AnchorBolts = new List<Transform>();
        public PourSystem Pour;
        float _lastTint = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _all.Clear(); Created = null; }

        public void Init(PourSystem pour, Vector2[] outline, float topY, float thicknessIn, int psi, float pouredAt, SurfaceKind finish, float yards)
        {
            Pour = pour; Outline = outline; TopY = topY; ThicknessInches = thicknessIn; Psi = psi; PouredAt = pouredAt; Finish = finish; Yards = yards;
            _all.Add(this);
            Created?.Invoke(this);
        }

        void OnDestroy() { _all.Remove(this); }

        public float AgeHours => (ConcreteTime.Now - PouredAt) / 60f;
        public float AgeDays => AgeHours / 24f;

        /// <summary>Compressive strength as a fraction of the 28-day design strength (ACI 209 moist-cured curve t/(4+0.85t)).
        /// Without moist curing the surface only reaches ~85%.</summary>
        public float Strength01
        {
            get
            {
                float t = Mathf.Max(0f, AgeDays);
                float s = t / (4f + 0.85f * t);
                return Mathf.Clamp01(s * (Covered || WetCured ? 1f : 0.85f));
            }
        }
        public float StrengthPsi => Strength01 * Psi;
        /// <summary>OK to frame on: 7 days of cure.</summary>
        public bool Cured => AgeDays >= 7f;

        public bool Contains(Vector3 p) => Outline != null && CTool.PointInPolygon(Outline, CTool.XZ(p));

        public static ConcreteElement At(Vector3 p)
        {
            foreach (var e in _all) if (e != null && e.Contains(p)) return e;
            return null;
        }

        public static bool TryGetTop(Vector3 p, out float topY)
        {
            var e = At(p); topY = e != null ? e.TopY : 0f; return e != null;
        }

        public string Cover()
        {
            if (Covered) return "Already covered";
            int sf = Mathf.CeilToInt(Area() * 1.1f);
            if (!CTool.TakeStock(FormSystem.PolyId, sf, "poly sheeting")) return "Not enough poly to cover the slab";
            Covered = true;
            if (AgeHours > 24f) Defects.Add("Curing started late (> 24 h): surface crazing");
            return $"Slab covered with poly ({sf} sq ft) - it will moist-cure";
        }

        public string WetCure()
        {
            if (WetCured) return "Already wet-curing";
            WetCured = true;
            if (AgeHours > 24f) Defects.Add("Curing started late (> 24 h): surface crazing");
            return "Hose on a soaker: keeping the slab wet for 7 days";
        }

        public float Area()
        {
            float a = 0; for (int i = 0, j = Outline.Length - 1; i < Outline.Length; j = i++) a += (Outline[j].x + Outline[i].x) * (Outline[j].y - Outline[i].y);
            return Mathf.Abs(a) * 0.5f / (Units.Foot * Units.Foot);
        }

        public string Summary =>
            $"{Kind} {Area():0} sq ft x {ThicknessInches:0}\" · {Yards:0.00} yd · {AgeDays:0.0} days old · {StrengthPsi:0} of {Psi} psi ({Strength01 * 100:0}%)" +
            (Cured ? " · CURED (ready to frame)" : " · curing") + (Covered ? " · covered" : WetCured ? " · wet cure" : "") + (FormsStripped ? " · forms stripped" : "");

        void Update()
        {
            if (Pour == null) return;
            var mr = GetComponent<MeshRenderer>();
            float tint = CureTint();
            if (Mathf.Abs(tint - _lastTint) > 0.02f) ApplyLook(mr);
        }

        /// <summary>Green concrete reads darker (moisture) and lightens as it dries over the first days.</summary>
        float CureTint() => Mathf.Round(Mathf.Lerp(0.66f, 0.86f, Mathf.Clamp01(AgeDays / 5f)) * 20f) / 20f;

        public void ApplyLook(MeshRenderer mr)
        {
            if (mr == null) return;
            _lastTint = CureTint();
            var c = new Color(_lastTint, _lastTint, _lastTint * 0.99f);
            var mats = mr.sharedMaterials;
            mats[0] = Materials.Get(Finish, c);
            if (mats.Length > 1) mats[1] = Materials.Get(SurfaceKind.Concrete, c);
            if (mats.Length > 2) mats[2] = Materials.Get(SurfaceKind.ConcreteBlock, c * 0.85f);
            mr.sharedMaterials = mats;
        }
    }
}
