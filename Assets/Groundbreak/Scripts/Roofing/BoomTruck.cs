using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Flatbed boom truck (telescoping crane behind the cab) that swings trusses from its bed onto the walls.
    /// Purely cosmetic: the truss is already placed when its step runs; only its "Lift" child (visual + hardware) flies in.</summary>
    public class BoomTruck : MonoBehaviour
    {
        public static readonly List<BoomTruck> All = new List<BoomTruck>();
        const float Dur = 2.6f, BaseLen = 5.4f;

        Transform _turret, _boom, _sec2, _sec3, _hook, _ram;
        LineRenderer _cable, _slingA, _slingB;
        readonly Queue<Transform> _queue = new Queue<Transform>();
        Transform _cur;
        float _t, _leaveT = -1f;
        Vector3 _start; Quaternion _startRot;
        float _yaw = 180f, _pitch = 4f, _ext;

        static Material Yellow => Materials.Get(SurfaceKind.PaintedWhite, new Color(1f, 0.74f, 0.08f));

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static BoomTruck Spawn(Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("Boom truck");
            go.transform.SetPositionAndRotation(pos, rot);
            var t = go.AddComponent<BoomTruck>();
            t.Build();
            return t;
        }

        static float I(float inch) => inch * Units.Inch;

        void Build()
        {
            var tr = transform;
            // chassis, cab, bed, wheels, outriggers
            Framer.AttachMesh(tr, "Chassis", (MeshBuilder b, System.Func<SurfaceKind, int> s) =>
            {
                int blk = s(SurfaceKind.MatteBlack), paint = s(SurfaceKind.TruckPaint), glass = s(SurfaceKind.Glass), steel = s(SurfaceKind.BrushedSteel);
                int tire = s(SurfaceKind.Tire), deck = s(SurfaceKind.TreatedLumber), gray = s(SurfaceKind.PaintedSteel), galv = s(SurfaceKind.Galvanized);
                b.AddBox(new Vector3(0.45f, 1.0f, -0.1f), new Vector3(0.12f, 0.28f, 8.8f), blk);
                b.AddBox(new Vector3(-0.45f, 1.0f, -0.1f), new Vector3(0.12f, 0.28f, 8.8f), blk);
                // cab + hood (conventional)
                b.AddBox(new Vector3(0, 2.05f, 3.15f), new Vector3(2.35f, 1.9f, 1.5f), paint);
                b.AddBox(new Vector3(0, 1.55f, 4.45f), new Vector3(2.1f, 0.95f, 1.1f), paint);
                b.AddBox(new Vector3(0, 2.62f, 3.92f), new Vector3(2.05f, 0.62f, 0.04f), glass);     // windshield
                b.AddBox(new Vector3(1.18f, 2.5f, 3.2f), new Vector3(0.04f, 0.6f, 0.9f), glass);
                b.AddBox(new Vector3(-1.18f, 2.5f, 3.2f), new Vector3(0.04f, 0.6f, 0.9f), glass);
                b.AddBox(new Vector3(0, 1.55f, 5.02f), new Vector3(1.4f, 0.7f, 0.04f), galv);       // grille
                b.AddBox(new Vector3(0, 0.9f, 5.12f), new Vector3(2.3f, 0.25f, 0.2f), steel);       // bumper
                b.AddBox(new Vector3(0.82f, 1.75f, 5.02f), new Vector3(0.25f, 0.18f, 0.04f), glass);
                b.AddBox(new Vector3(-0.82f, 1.75f, 5.02f), new Vector3(0.25f, 0.18f, 0.04f), glass);
                b.AddBox(new Vector3(1.0f, 1.75f, 2.4f), new Vector3(0.35f, 0.5f, 0.35f), steel);    // fuel tank
                b.AddCylinder(new Vector3(-1.05f, 3.0f, 2.35f), 0.07f, 1.4f, 10, steel);              // exhaust stack
                // flatbed: steel frame + wood deck
                b.AddBox(new Vector3(0, 1.22f, -1.6f), new Vector3(2.5f, 0.16f, 6.0f), gray);
                b.AddBox(new Vector3(0, 1.31f, -1.6f), new Vector3(2.4f, 0.03f, 5.9f), Quaternion.identity, deck, Vector3.forward);
                // crane pedestal behind the cab
                b.AddBox(new Vector3(0, 1.35f, 1.95f), new Vector3(2.4f, 0.4f, 0.9f), gray);
                // wheels: steer axle + tandem duals
                float[] axles = { 3.95f, -2.3f, -3.6f };
                for (int a = 0; a < axles.Length; a++)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (a == 0) Wheel(b, new Vector3(side * 1.02f, 0.52f, axles[a]), tire, steel);
                        else { Wheel(b, new Vector3(side * 0.86f, 0.52f, axles[a]), tire, steel); Wheel(b, new Vector3(side * 1.16f, 0.52f, axles[a]), tire, steel); }
                    }
                // outriggers deployed with pads
                foreach (float oz in new[] { 1.95f, -4.3f })
                    for (int side = -1; side <= 1; side += 2)
                    {
                        b.AddBox(new Vector3(side * 1.55f, 1.25f, oz), new Vector3(2.0f, 0.22f, 0.26f), Ysub(b, s));
                        b.AddBox(new Vector3(side * 2.45f, 0.65f, oz), new Vector3(0.18f, 1.2f, 0.18f), Ysub(b, s));
                        b.AddBox(new Vector3(side * 2.45f, 0.04f, oz), new Vector3(0.6f, 0.08f, 0.6f), deck);
                    }
                // truss bundle on the bed (lying flat)
                for (int l = 0; l < 5; l++)
                {
                    float y = 1.36f + l * I(1.6f);
                    float zc = -1.6f, half = 2.25f, rise = 1.05f;
                    int pine = s(SurfaceKind.PineLumber);
                    b.AddBox(new Vector3(-0.55f, y, zc), new Vector3(I(3.5f), I(1.5f), half * 2f), Quaternion.identity, pine, Vector3.forward);
                    float ang = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
                    float cl = Mathf.Sqrt(half * half + rise * rise);
                    b.AddBox(new Vector3(-0.55f + rise * 0.5f, y, zc - half * 0.5f), new Vector3(I(3.5f), I(1.5f), cl), Quaternion.Euler(0, ang, 0), pine, Vector3.forward);
                    b.AddBox(new Vector3(-0.55f + rise * 0.5f, y, zc + half * 0.5f), new Vector3(I(3.5f), I(1.5f), cl), Quaternion.Euler(0, -ang, 0), pine, Vector3.forward);
                }
            });

            // turret (yaws) -> boom (pitches) -> telescoping sections
            _turret = new GameObject("Turret").transform;
            _turret.SetParent(tr, false);
            _turret.localPosition = new Vector3(0, 1.55f, 1.95f);
            Framer.AttachMesh(_turret, "TurretMesh", (MeshBuilder b, System.Func<Material, int> m) =>
            {
                int y = m(Yellow), dk = m(Materials.Get(SurfaceKind.MatteBlack));
                b.AddCylinder(new Vector3(0, 0.15f, 0), 0.55f, 0.3f, 20, dk);
                b.AddBox(new Vector3(0, 0.7f, 0), new Vector3(0.7f, 0.9f, 0.7f), y);
                b.AddBox(new Vector3(0.42f, 0.55f, 0.1f), new Vector3(0.12f, 0.5f, 0.5f), dk);   // controls
            });
            _boom = new GameObject("Boom").transform;
            _boom.SetParent(_turret, false);
            _boom.localPosition = new Vector3(0, 1.05f, 0);
            Framer.AttachMesh(_boom, "Base", (MeshBuilder b, System.Func<Material, int> m) =>
            {
                b.AddBox(new Vector3(0, 0, BaseLen * 0.5f - 0.3f), new Vector3(0.5f, 0.55f, BaseLen), m(Yellow));
                b.AddCylinder(Vector3.zero, Quaternion.Euler(0, 0, 90), 0.2f, 0.62f, 14, m(Materials.Get(SurfaceKind.BrushedSteel)));
            });
            _sec2 = new GameObject("Section2").transform; _sec2.SetParent(_boom, false);
            Framer.AttachMesh(_sec2, "S2", (MeshBuilder b, System.Func<Material, int> m) => b.AddBox(new Vector3(0, 0, BaseLen * 0.5f), new Vector3(0.4f, 0.44f, BaseLen), m(Yellow)));
            _sec3 = new GameObject("Section3").transform; _sec3.SetParent(_sec2, false);
            Framer.AttachMesh(_sec3, "S3", (MeshBuilder b, System.Func<Material, int> m) =>
            {
                b.AddBox(new Vector3(0, 0, BaseLen * 0.5f), new Vector3(0.31f, 0.34f, BaseLen), m(Yellow));
                b.AddCylinder(new Vector3(0, -0.1f, BaseLen), Quaternion.Euler(0, 0, 90), 0.22f, 0.2f, 14, m(Materials.Get(SurfaceKind.MatteBlack)));   // sheave
            });
            _ram = new GameObject("LiftCylinder").transform; _ram.SetParent(_turret, false);
            Framer.AttachMesh(_ram, "Ram", (MeshBuilder b, System.Func<Material, int> m) =>
            {
                b.AddCylinder(new Vector3(0, 0, 0.5f), Quaternion.Euler(90, 0, 0), 0.11f, 1.0f, 12, m(Materials.Get(SurfaceKind.MatteBlack)));
                b.AddCylinder(new Vector3(0, 0, 1.4f), Quaternion.Euler(90, 0, 0), 0.06f, 1.0f, 12, m(Materials.Get(SurfaceKind.BrushedSteel)));
            });
            _hook = new GameObject("HookBlock").transform; _hook.SetParent(tr, false);
            Framer.AttachMesh(_hook, "Hook", (MeshBuilder b, System.Func<SurfaceKind, int> s) =>
            {
                b.AddBox(new Vector3(0, 0.12f, 0), new Vector3(0.16f, 0.3f, 0.12f), s(SurfaceKind.TruckPaint));
                b.AddRing(new Vector3(0, -0.1f, 0), Quaternion.Euler(90, 0, 0), 0.09f, 0.055f, 0.035f, 12, s(SurfaceKind.BrushedSteel));
            });
            _cable = Line("Cable", 0.012f, new Color(0.12f, 0.12f, 0.12f));
            _slingA = Line("SlingA", 0.02f, new Color(0.9f, 0.55f, 0.1f));
            _slingB = Line("SlingB", 0.02f, new Color(0.9f, 0.55f, 0.1f));
            Pose();
        }

        int _ysub = -1;
        int Ysub(MeshBuilder b, System.Func<SurfaceKind, int> s) => s(SurfaceKind.PaintedWhite);

        static void Wheel(MeshBuilder b, Vector3 c, int tire, int hub)
        {
            b.AddCylinder(c, Quaternion.Euler(0, 0, 90), 0.52f, 0.3f, 22, tire);
            b.AddCylinder(c + Vector3.right * Mathf.Sign(c.x) * 0.02f, Quaternion.Euler(0, 0, 90), 0.3f, 0.31f, 16, hub);
        }

        LineRenderer Line(string name, float w, Color c)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var l = go.AddComponent<LineRenderer>();
            l.positionCount = 2; l.startWidth = l.endWidth = w; l.useWorldSpace = true;
            l.sharedMaterial = MaterialLibrary.Get(c);
            return l;
        }

        /// <summary>Queue a truss "Lift" transform to be swung in from the bed.</summary>
        public void Lift(Transform lift)
        {
            if (lift == null) return;
            lift.position = BedPoint;
            lift.rotation = lift.parent.rotation * Quaternion.Euler(0, 90, 0) * Quaternion.Euler(0, 0, 90);
            _queue.Enqueue(lift);
        }

        Vector3 BedPoint => transform.TransformPoint(new Vector3(-0.55f, 1.4f + 0.12f * _queue.Count, -1.6f));

        /// <summary>Snap every queued truss into place (tests / crew mode).</summary>
        public void FinishNow()
        {
            if (_cur != null) { _cur.localPosition = Vector3.zero; _cur.localRotation = Quaternion.identity; _cur = null; }
            while (_queue.Count > 0) { var l = _queue.Dequeue(); if (l != null) { l.localPosition = Vector3.zero; l.localRotation = Quaternion.identity; } }
        }

        public static void FinishAll() { foreach (var t in All.ToArray()) t.FinishNow(); }

        /// <summary>Remove every boom truck immediately (tests / skipping the drive-off).</summary>
        public static void RemoveAll() { foreach (var t in All.ToArray()) { t.FinishNow(); Destroy(t.gameObject); } }

        /// <summary>Done: finish any lifts, stow the boom and drive off.</summary>
        public void Leave() { FinishNow(); _leaveT = 0f; }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_leaveT >= 0f)
            {
                _leaveT += dt;
                _yaw = Mathf.MoveTowardsAngle(_yaw, 180f, 90f * dt); _pitch = Mathf.MoveTowards(_pitch, 4f, 20f * dt); _ext = Mathf.MoveTowards(_ext, 0f, 4f * dt);
                if (_leaveT > 2f) transform.position += transform.forward * Mathf.Min(8f, (_leaveT - 2f) * 3f) * dt;
                Pose();
                if (_leaveT > 9f) Destroy(gameObject);
                return;
            }
            if (_cur == null && _queue.Count > 0) { _cur = _queue.Dequeue(); _t = 0f; if (_cur != null) { _start = _cur.position; _startRot = _cur.rotation; } }
            if (_cur != null)
            {
                _t += dt / Dur;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));
                Vector3 end = _cur.parent.position;
                Vector3 pos = Vector3.Lerp(_start, end, k) + Vector3.up * Mathf.Sin(Mathf.Clamp01(_t) * Mathf.PI) * 2.2f;
                _cur.position = pos;
                _cur.rotation = Quaternion.Slerp(_startRot, _cur.parent.rotation, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t * 1.4f - 0.1f)));
                AimAt(HookPoint(_cur));
                if (_t >= 1f) { _cur.localPosition = Vector3.zero; _cur.localRotation = Quaternion.identity; _cur = null; }
            }
            Pose();
        }

        Vector3 HookPoint(Transform lift)
        {
            var r = lift.GetComponentInChildren<MeshRenderer>();
            if (r == null) return lift.position + Vector3.up;
            var b = r.bounds;
            return new Vector3(b.center.x, b.max.y + 1.1f, b.center.z);
        }

        void AimAt(Vector3 hookWorld)
        {
            Vector3 tip = hookWorld + Vector3.up * 1.8f;
            Vector3 pivot = _turret.position + _turret.rotation * new Vector3(0, 1.05f, 0);
            Vector3 d = transform.InverseTransformDirection(tip - pivot);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float flat = new Vector2(d.x, d.z).magnitude;
            float pitch = Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg;
            float len = d.magnitude;
            _yaw = Mathf.MoveTowardsAngle(_yaw, yaw, 140f * Time.deltaTime);
            _pitch = Mathf.MoveTowards(_pitch, pitch, 60f * Time.deltaTime);
            _ext = Mathf.MoveTowards(_ext, Mathf.Clamp(len - BaseLen + 0.3f, 0f, 9.5f), 6f * Time.deltaTime);
        }

        void Pose()
        {
            if (_turret == null) return;
            _turret.localRotation = Quaternion.Euler(0, _yaw, 0);
            _boom.localRotation = Quaternion.Euler(-_pitch, 0, 0);
            _sec2.localPosition = new Vector3(0, 0, _ext * 0.5f);
            _sec3.localPosition = new Vector3(0, 0, _ext * 0.5f);
            Vector3 tip = _sec3.TransformPoint(new Vector3(0, -0.1f, BaseLen));
            // lift cylinder from the turret base to under the boom
            Vector3 a = _turret.TransformPoint(new Vector3(0, 0.35f, 0.45f));
            Vector3 bpt = _boom.TransformPoint(new Vector3(0, -0.3f, 1.9f));
            _ram.position = a;
            var dir = bpt - a;
            if (dir.sqrMagnitude > 1e-4f) _ram.rotation = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 hookPos;
            if (_cur != null)
            {
                hookPos = HookPoint(_cur);
                var r = _cur.GetComponentInChildren<MeshRenderer>();
                if (r != null)
                {
                    var b = r.bounds; Vector3 ax = _cur.forward * (b.extents.magnitude * 0.35f);
                    _slingA.enabled = _slingB.enabled = true;
                    _slingA.SetPosition(0, hookPos); _slingA.SetPosition(1, b.center + ax);
                    _slingB.SetPosition(0, hookPos); _slingB.SetPosition(1, b.center - ax);
                }
            }
            else
            {
                hookPos = tip + Vector3.down * 1.6f;
                _slingA.enabled = _slingB.enabled = false;
            }
            _hook.position = hookPos;
            _hook.rotation = transform.rotation;
            _cable.SetPosition(0, tip);
            _cable.SetPosition(1, hookPos + Vector3.up * 0.27f);
        }
    }
}
