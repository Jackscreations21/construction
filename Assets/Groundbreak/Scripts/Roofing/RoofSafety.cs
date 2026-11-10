using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Fall protection: on a decked roof steeper than 6/12 the player slides down-slope unless clipped in to a roof
    /// anchor (SafetyHarness) or standing near a roof jack. Added by FramingInteraction.Boot.</summary>
    public class RoofSafety : MonoBehaviour
    {
        public static bool ClippedIn;
        public static RoofAnchor Anchor;
        public const int SteepRise = 6;
        public const float LanyardLength = 15f, JackRange = 2.5f;
        public static bool Slipping { get; private set; }
        public static RoofSafety I;

        float _speed, _toastAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ClippedIn = false; Anchor = null; Slipping = false; I = null; }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        /// <summary>The roof plane under the player's feet (null when not on a decked roof).</summary>
        public static RoofPlane PlaneUnderPlayer(out RoofInfo info)
        {
            info = null;
            if (Game.Player == null) return null;
            var p = RoofInfo.FindPlane(Game.Player.transform.position, 0.3f, 0.1f, out info, out _);
            if (p != null && (info == null || !info.HasDeck)) return null;
            return p;
        }

        void Update()
        {
            var pl = Game.Player;
            if (pl == null) { Slipping = false; return; }
            var cc = pl.GetComponent<CharacterController>();
            if (cc == null || !cc.enabled) return;
            Vector3 feet = pl.transform.position;
            if (ClippedIn && (Anchor == null || Vector3.Distance(feet, Anchor.transform.position) > LanyardLength))
            {
                ClippedIn = false; Anchor = null;
                Game.Toast("Lanyard out of reach - unclipped from the roof anchor");
            }
            var plane = PlaneUnderPlayer(out _);
            if (plane == null || plane.PitchRise <= SteepRise || ClippedIn || RoofJack.Near(feet, JackRange))
            {
                Slipping = false; _speed = 0f;
                return;
            }
            if (!Slipping)
            {
                Slipping = true;
                if (Time.time > _toastAt) { Game.Toast("You slipped! Use roof jacks or a harness above 6/12"); _toastAt = Time.time + 3f; }
            }
            _speed = Mathf.Min(_speed + 6f * Time.deltaTime, 5f);
            cc.Move(-plane.UpSlope * _speed * Time.deltaTime);
        }
    }

    /// <summary>A roof jack bracket nailed to a rafter through the deck. Standing within RoofSafety.JackRange of one prevents slipping.</summary>
    public class RoofJack : MonoBehaviour
    {
        public static readonly List<RoofJack> All = new List<RoofJack>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static bool Near(Vector3 p, float range)
        {
            foreach (var j in All) if (j != null && (j.transform.position - p).sqrMagnitude <= range * range) return true;
            return false;
        }

        static float I(float inch) => inch * Units.Inch;
        const float H = 0.009f;   // sits on top of the shingles

        static void Points(RoofPlane p, float u, float v, out Vector3 bb, out Vector3 bt, out Vector3 ae)
        {
            bb = p.At(u, v - 0.30f, H);
            bt = p.At(u, v + 0.06f, H);
            Vector3 o = p.OutHorizontal;
            float dx = Vector3.Dot(bb - bt, o);
            ae = bt + o * dx;
        }

        public static RoofJack Create(Transform parent, RoofPlane p, float u, float v)
        {
            Points(p, u, v, out var bb, out var bt, out var ae);
            Vector3 o = p.OutHorizontal;
            var m = new RoofMesh();
            int g = m.Mat(Materials.Get(SurfaceKind.Galvanized));
            m.Box((bb + bt) * 0.5f, new Vector3(I(2.75f), 0.003f, Vector3.Distance(bb, bt)), Quaternion.LookRotation(bt - bb, p.Normal), g);
            m.Box((bt + ae) * 0.5f + Vector3.up * 0.002f, new Vector3(I(2.5f), 0.004f, Vector3.Distance(bt, ae)), Quaternion.LookRotation(o, Vector3.up), g);
            m.Box((bb + ae) * 0.5f, new Vector3(I(2f), 0.004f, Vector3.Distance(bb, ae)), Quaternion.LookRotation(Vector3.up, o), g);
            m.Box(ae + Vector3.up * I(1.4f), new Vector3(I(2f), 0.004f, I(2.8f)), Quaternion.LookRotation(Vector3.up, o), g);
            // nail heads
            for (int i = 0; i < 3; i++) m.Box(Vector3.Lerp(bb, bt, 0.2f + i * 0.3f) + p.Normal * 0.002f, new Vector3(0.009f, 0.002f, 0.009f), Quaternion.LookRotation(bt - bb, p.Normal), g);
            var go = m.Attach(parent, "Roof jack");
            go.transform.position = Vector3.zero;
            var holder = new GameObject("RoofJack").transform;
            holder.SetParent(parent, false);
            holder.position = (bb + ae) * 0.5f;
            go.transform.SetParent(holder, true);
            return holder.gameObject.AddComponent<RoofJack>();
        }

        /// <summary>2x8 plank resting level on the jack arms (has a collider: you can stand on it).</summary>
        public static GameObject Plank(Transform parent, RoofPlane p, float u0, float u1, float v)
        {
            Points(p, (u0 + u1) * 0.5f, v, out var bb, out var bt, out var ae);
            Vector3 o = p.OutHorizontal;
            float w = I(7.25f), t = I(1.5f);
            Vector3 c = bt + o * (w * 0.5f + 0.012f);
            c.y = ae.y + 0.004f + t * 0.5f;
            var go = new GameObject("Roof jack plank (2x8)");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(c, Quaternion.LookRotation(p.AlongEave, Vector3.up));
            float len = (u1 - u0);
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(w, t, len);
            Framer.AttachMesh(go.transform, "Visual", (MeshBuilder b, System.Func<SurfaceKind, int> s) =>
                b.AddBevelBox(Vector3.zero, new Vector3(w, t, len), Quaternion.identity, I(0.12f), s(SurfaceKind.PineLumber), s(SurfaceKind.PineEndGrain)));
            go.AddComponent<RoofJack>();
            return go;
        }
    }

    /// <summary>Permanent / temporary roof anchor screwed through the deck at the ridge; the SafetyHarness clips in to it.</summary>
    public class RoofAnchor : MonoBehaviour
    {
        public static readonly List<RoofAnchor> All = new List<RoofAnchor>();
        public RoofInfo Roof;
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static RoofAnchor Nearest(Vector3 p, float range)
        {
            RoofAnchor best = null; float bd = range * range;
            foreach (var a in All) { if (a == null) continue; float d = (a.transform.position - p).sqrMagnitude; if (d <= bd) { bd = d; best = a; } }
            return best;
        }

        public static RoofAnchor Create(RoofInfo roof, Vector3 at)
        {
            var go = new GameObject("Roof anchor");
            Vector3 dir = roof.HasRidge ? (roof.Ridge.B - roof.Ridge.A).normalized : roof.Planes[0].AlongEave;
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir, Vector3.up));
            float th = roof.Planes.Count > 0 ? roof.Planes[0].AngleDeg : 26.6f;
            float rad = th * Mathf.Deg2Rad;
            Framer.AttachMesh(go.transform, "Visual", (MeshBuilder b, System.Func<SurfaceKind, int> s) =>
            {
                int g = s(SurfaceKind.Galvanized);
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    var c = new Vector3(sg * 0.1f * Mathf.Cos(rad), -0.1f * Mathf.Sin(rad) + 0.003f, 0f);
                    b.AddBox(c, new Vector3(0.2f, 0.003f, 0.08f), Quaternion.Euler(0, 0, -sg * th), g);
                    for (int i = 0; i < 3; i++)
                        b.AddCylinder(c + new Vector3(sg * (0.03f + i * 0.05f) * Mathf.Cos(rad), -(0.03f + i * 0.05f) * Mathf.Sin(rad) * 0f + 0.003f, 0.02f), Quaternion.Euler(0, 0, -sg * th), 0.006f, 0.004f, 8, g);
                }
                b.AddRing(new Vector3(0, 0.045f, 0), Quaternion.Euler(90, 0, 0), 0.04f, 0.028f, 0.012f, 16, g);
            });
            var a = go.AddComponent<RoofAnchor>();
            a.Roof = roof;
            return a;
        }
    }
}
