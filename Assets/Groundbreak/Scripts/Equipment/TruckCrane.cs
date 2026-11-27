using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Tandem-axle boom truck (Manitex 1970C class on a conventional chassis): cab + hood, 20' flatbed with a banded bundle of
    /// roof trusses, crane pedestal behind the cab, 4 out-and-down outriggers (B), 360° turret with a ride-around operator seat,
    /// 4-section telescoping boom 25'-70' (7.6-21.3 m) on a lift ram, hoist drum and wire rope to a sheave block + hook.
    /// Load chart (outriggers fully down, 360°) with a load moment indicator. Rigs EquipmentLoad pallets and sets roof trusses
    /// for the nearest truss FramingJob: F rigs the next truss from the bed, fly it onto its layout mark, F sets it.
    /// Hierarchy: root (truck) → Turret (SwingDeg) → Boom (BoomDeg about local X at the pivot) → Fly1 → Fly2 → Fly3;
    /// Hook (root child, positioned plumb below the tip sheave every frame); Outrigger beams → legs.
    /// </summary>
    public class TruckCrane : Machine
    {
        public override string TypeId => "boom";
        public override string DisplayName => "Crane truck";

        // ---- geometry (meters, root local unless noted)
        static readonly Vector3 TurretLocal = new Vector3(0f, 1.9f, 1.05f);
        static readonly Vector3 PivotLocal = new Vector3(0f, 1.05f, -0.35f);    // turret local
        static readonly Vector3 TipLocal = new Vector3(0f, 0f, 7.75f);          // fly3 local: where the rope leaves the sheave
        const float RetractedLen = 7.6f;                                        // 25' boom
        public const float MaxExtend = 13.7f;                                    // → 70' (21.3 m)
        const float BedTop = 1.35f, HookBlockLb = 150f;
        static readonly float[] OrZ = { 0.15f, -5.3f };
        static readonly Vector3 BundleTopLocal = new Vector3(0f, 2.35f, -3.0f);

        public float SwingDeg = 180f, BoomDeg, ExtendM, CableM = 1.2f;

        Transform _turret, _boom, _fly1, _fly2, _fly3, _hook;
        readonly Transform[] _beams = new Transform[4], _legs = new Transform[4];
        LineRenderer _cable, _slingA, _slingB;
        float _out, _outTarget;
        float _nextToast;

        Transform _truss; float _trussLb, _trussH, _trussSpan; FramingJob _trussJob;

        public bool OutriggersDown => _out >= 0.999f;
        public Vector3 HookPos => _hook != null ? _hook.position : transform.position;
        public float RadiusM { get { Vector3 d = HookPos - _turret.position; d.y = 0f; return d.magnitude; } }
        public EquipmentLoad Rigged { get; private set; }
        /// <summary>The hanging truss (its position = truss centre, compared to the ghost centre by TrySetTruss), or null.</summary>
        public Transform HangingTruss => _truss;
        public float LoadLb => (Rigged != null ? Rigged.WeightLb : 0f) + (_truss != null ? _trussLb : 0f);
        public float ChartPercent => LoadLb / Mathf.Max(1f, CapacityLb(RadiusM));
        public bool BoomStowed => BoomDeg < 5f && ExtendM < 0.2f;
        public float BoomLength => RetractedLen + ExtendM;

        protected override Transform SeatFrame => _turret != null ? _turret : transform;
        protected override Vector3 SeatLocal => new Vector3(-0.85f, 0.55f, 0.1f);
        protected override Vector3 EyeLocal => new Vector3(-0.85f, 1.35f, 0.14f);
        protected override string ControlHint =>
            "W/S/A/D drive (outriggers up, boom stowed) · B outriggers · ←/→ swing · ↑/↓ boom · I/K telescope · Y/H hoist up/down · F rig / set truss / release";

        public override float[] Joints
        {
            get => new[] { SwingDeg, BoomDeg, ExtendM, CableM, _outTarget };
            set
            {
                if (value == null) return;
                if (value.Length > 0) SwingDeg = Mathf.Repeat(value[0], 360f);
                if (value.Length > 1) BoomDeg = value[1];
                if (value.Length > 2) ExtendM = value[2];
                if (value.Length > 3) CableM = value[3];
                if (value.Length > 4) _out = _outTarget = value[4] >= 0.5f ? 1f : 0f;
                PoseOutriggers();
                ApplyPose();
                Physics.SyncTransforms();
            }
        }

        // ------------------------------------------------------------------ load chart

        static readonly float[] ChartFt = { 10f, 15f, 20f, 25f, 30f, 40f, 50f, 60f, 66f };
        static readonly float[] ChartLb = { 24000f, 17500f, 13000f, 10100f, 8000f, 5500f, 3900f, 2700f, 2100f };

        /// <summary>Net capacity (lb) at a load radius, outriggers fully down, 360°, hook block deducted. 0 beyond 66'.</summary>
        public static float CapacityLb(float radiusM)
        {
            float ft = radiusM / Units.Foot;
            if (ft > 66.001f) return 0f;
            float gross = ChartLb[0];
            if (ft > ChartFt[0])
                for (int i = 1; i < ChartFt.Length; i++)
                    if (ft <= ChartFt[i]) { gross = Mathf.Lerp(ChartLb[i - 1], ChartLb[i], (ft - ChartFt[i - 1]) / (ChartFt[i] - ChartFt[i - 1])); break; }
                    else gross = ChartLb[i];
            return Mathf.Max(0f, gross - HookBlockLb);
        }

        // ------------------------------------------------------------------ build

        protected override void Build()
        {
            BodyCenter = new Vector3(0f, 1.6f, -0.5f);
            BodySize = new Vector3(2.6f, 3.2f, 10.7f);
            Footprint = new Vector2(1.0f, 4.0f);
            TravelSpeed = 4.5f; TurnRate = 18f; ChaseDistance = 16f;
            EngineBasePitch = 0.7f;

            Part(transform, "Truck", Vector3.zero, Quaternion.identity, AuthorTruck);
            Part(transform, "TrussBundle", Vector3.zero, Quaternion.identity, AuthorBundle);
            BuildOutriggers();

            _turret = Part(transform, "Turret", TurretLocal, Quaternion.identity, AuthorTurret);
            _boom = Part(_turret, "Boom", PivotLocal, Quaternion.identity, AuthorBase);
            _fly1 = Part(_boom, "Fly1", Vector3.zero, Quaternion.identity, (b, m) => AuthorFly(b, m, 0.42f, 0.52f, 7.3f, false));
            _fly2 = Part(_fly1, "Fly2", Vector3.zero, Quaternion.identity, (b, m) => AuthorFly(b, m, 0.36f, 0.44f, 7.4f, false));
            _fly3 = Part(_fly2, "Fly3", Vector3.zero, Quaternion.identity, (b, m) => AuthorFly(b, m, 0.30f, 0.36f, 7.45f, true));
            _hook = Part(transform, "Hook", Vector3.zero, Quaternion.identity, AuthorHook);

            var pinA = Part(_turret, "RamPinA", PivotLocal + new Vector3(0f, -1.0f, 0.6f), Quaternion.identity, null);
            var pinB = Part(_boom, "RamPinB", new Vector3(0f, -0.32f, 2.4f), Quaternion.identity, null);
            HydraulicRam.Make(pinA, pinB, 0.11f, 1.85f, 0.07f, 1.7f, EquipModels.Yellow);

            _cable = Line("WireRope", 0.016f, new Color(0.13f, 0.13f, 0.13f), 6);
            _slingA = Line("SlingA", 0.025f, new Color(0.15f, 0.35f, 0.9f), 3);
            _slingB = Line("SlingB", 0.025f, new Color(0.15f, 0.35f, 0.9f), 3);

            // colliders (layer 2 via the base)
            AddBox(new Vector3(0f, 2.0f, 3.3f), new Vector3(2.4f, 1.9f, 3.2f));             // cab + hood
            AddBox(new Vector3(0f, 1.05f, -2.7f), new Vector3(2.5f, 0.6f, 6.1f));           // bed
            AddBox(new Vector3(0f, 1.85f, -3.0f), new Vector3(1.7f, 1.0f, 6.6f));           // truss bundle
            AddBox(new Vector3(0f, 1.45f, 1.05f), new Vector3(1.1f, 0.8f, 1.3f));           // pedestal
            AddBox(new Vector3(0f, 0.6f, -0.1f), new Vector3(0.8f, 1.2f, 1.4f), _turret);
            AddBox(new Vector3(0f, 0f, 3.45f), new Vector3(0.48f, 0.6f, 7.5f), _boom);

            PoseOutriggers();
            ApplyPose();
        }

        LineRenderer Line(string name, float w, Color c, int n)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var l = go.AddComponent<LineRenderer>();
            l.positionCount = n; l.startWidth = l.endWidth = w; l.useWorldSpace = true;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            l.sharedMaterial = MaterialLibrary.Get(c);
            l.enabled = n == 6;
            return l;
        }

        void AuthorTruck(MeshBuilder b, CTool.Mats m)
        {
            int paint = m.K(SurfaceKind.PaintedSteel, new Color(0.93f, 0.93f, 0.92f));   // white fleet cab
            int blk = EquipModels.Black(m), chrome = EquipModels.Chrome(m), glass = EquipModels.Glass(m), yel = EquipModels.PaintY(m);
            int gray = EquipModels.PaintG(m), deck = m.K(SurfaceKind.TreatedLumber), galv = m.K(SurfaceKind.Galvanized);
            int red = m.C(new Color(0.85f, 0.06f, 0.05f)), amber = m.C(new Color(1f, 0.6f, 0.05f)), lamp = m.C(new Color(1f, 1f, 0.92f));
            int stripeW = m.C(new Color(0.95f, 0.95f, 0.95f));

            // frame rails + crossmembers
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.43f, 0.96f, -0.55f), new Vector3(0.09f, 0.28f, 10.5f), blk);
            for (float z = -5.6f; z < 4.6f; z += 1.2f) b.AddBox(new Vector3(0f, 0.96f, z), new Vector3(0.8f, 0.2f, 0.08f), blk);

            // wheels: steer axle singles, tandem drive duals (11R22.5)
            const float wr = 0.52f;
            foreach (int s in new[] { -1, 1 })
            {
                EquipModels.Wheel(b, m, new Vector3(s * 1.04f, wr, 3.95f), wr, 0.29f, new Color(0.8f, 0.8f, 0.8f));
                foreach (float z in new[] { -3.0f, -4.3f })
                {
                    EquipModels.Wheel(b, m, new Vector3(s * 0.77f, wr, z), wr, 0.28f, new Color(0.8f, 0.8f, 0.8f));
                    EquipModels.Wheel(b, m, new Vector3(s * 1.07f, wr, z), wr, 0.28f, new Color(0.8f, 0.8f, 0.8f));
                }
                b.AddBox(new Vector3(s * 0.92f, 0.45f, -4.98f), new Vector3(0.6f, 0.62f, 0.02f), blk);       // mud flap
            }
            foreach (float z in new[] { 3.95f, -3.0f, -4.3f }) b.AddBox(new Vector3(0f, wr, z), new Vector3(1.6f, 0.16f, 0.16f), gray);
            b.AddRod(new Vector3(0f, 0.75f, -0.5f), Quaternion.identity, 0.06f, 4.6f, 8, gray);             // drive shaft

            // hood (conventional), fenders, grille, bumper, headlights
            EquipModels.SideProfile(b, 0f, 2.0f, paint,
                new Vector2(3.35f, 1.1f), new Vector2(4.75f, 1.1f), new Vector2(4.82f, 1.35f), new Vector2(4.78f, 1.95f),
                new Vector2(4.5f, 2.15f), new Vector2(3.35f, 2.25f));
            foreach (int s in new[] { -1, 1 })
            {
                EquipModels.SideProfile(b, s * 1.06f, 0.36f, paint,
                    new Vector2(3.3f, 1.05f), new Vector2(4.65f, 1.05f), new Vector2(4.4f, 1.32f), new Vector2(3.55f, 1.32f));
                b.AddBox(new Vector3(s * 0.85f, 1.45f, 4.8f), new Vector3(0.28f, 0.16f, 0.04f), lamp);      // headlight
                b.AddBox(new Vector3(s * 0.85f, 1.32f, 4.79f), new Vector3(0.28f, 0.05f, 0.04f), amber);    // turn signal
                b.AddBox(new Vector3(s * 1.2f, 1.25f, 4.4f), new Vector3(0.03f, 0.06f, 0.12f), amber);      // side marker
            }
            b.AddBox(new Vector3(0f, 1.6f, 4.81f), new Vector3(1.1f, 0.8f, 0.04f), chrome);                // grille surround
            for (int i = 0; i < 9; i++) b.AddBox(new Vector3(0f, 1.27f + i * 0.08f, 4.835f), new Vector3(1.0f, 0.03f, 0.02f), blk);
            b.AddBox(new Vector3(0f, 0.75f, 4.85f), new Vector3(2.45f, 0.3f, 0.18f), chrome);
            b.AddBox(new Vector3(0f, 0.75f, 4.95f), new Vector3(0.3f, 0.15f, 0.01f), stripeW);             // plate

            // cab
            EquipModels.SideProfile(b, 0f, 2.36f, paint,
                new Vector2(1.75f, 1.15f), new Vector2(3.35f, 1.15f), new Vector2(3.35f, 2.25f), new Vector2(3.2f, 3.05f), new Vector2(1.75f, 3.05f));
            b.AddBox(new Vector3(0f, 2.64f, 3.3f), new Vector3(2.1f, 0.74f, 0.03f), Quaternion.Euler(-10.6f, 0, 0), glass);  // windshield
            b.AddBox(new Vector3(0f, 2.64f, 3.31f), new Vector3(0.04f, 0.76f, 0.035f), Quaternion.Euler(-10.6f, 0, 0), blk); // centre post
            b.AddBox(new Vector3(0f, 3.05f, 3.35f), new Vector3(2.2f, 0.05f, 0.3f), Quaternion.Euler(8, 0, 0), blk);      // sun visor
            b.AddBox(new Vector3(0f, 2.5f, 1.745f), new Vector3(1.3f, 0.45f, 0.02f), glass);                               // rear window
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(s * 1.185f, 2.62f, 2.6f), new Vector3(0.02f, 0.62f, 1.2f), glass);    // door glass
                b.AddBox(new Vector3(s * 1.186f, 2.0f, 2.0f), new Vector3(0.01f, 1.7f, 0.02f), blk);       // door seams
                b.AddBox(new Vector3(s * 1.186f, 2.0f, 3.25f), new Vector3(0.01f, 1.7f, 0.02f), blk);
                b.AddBox(new Vector3(s * 1.2f, 2.15f, 2.1f), new Vector3(0.03f, 0.04f, 0.18f), chrome);    // handle
                // west-coast mirror on a tube arm
                b.AddRod(new Vector3(s * 1.33f, 2.55f, 3.2f), Quaternion.Euler(0, 90, 0), 0.015f, 0.3f, 6, chrome);
                b.AddRod(new Vector3(s * 1.33f, 2.95f, 3.2f), Quaternion.Euler(0, 90, 0), 0.015f, 0.3f, 6, chrome);
                b.AddBevelBox(new Vector3(s * 1.5f, 2.75f, 3.2f), new Vector3(0.06f, 0.2f, 0.42f), Quaternion.Euler(-90, 0, 0), 0.015f, blk);
                b.AddBox(new Vector3(s * 1.5f, 2.75f, 3.17f), new Vector3(0.18f, 0.38f, 0.01f), chrome);
                // entry steps + grab handle
                b.AddBox(new Vector3(s * 1.1f, 0.5f, 2.6f), new Vector3(0.3f, 0.04f, 0.5f), galv);
                b.AddBox(new Vector3(s * 1.1f, 0.88f, 2.6f), new Vector3(0.3f, 0.04f, 0.5f), galv);
                b.AddBox(new Vector3(s * 1.0f, 0.7f, 2.6f), new Vector3(0.06f, 0.44f, 0.55f), blk);
                b.AddCylinder(new Vector3(s * 1.2f, 2.1f, 1.85f), 0.016f, 0.8f, 6, chrome);
            }
            for (int i = -2; i <= 2; i++) b.AddBox(new Vector3(i * 0.18f, 3.07f, 3.1f), new Vector3(0.08f, 0.04f, 0.05f), amber);   // roof markers
            b.AddCylinder(new Vector3(0.7f, 3.1f, 2.0f), 0.07f, 0.12f, 10, amber);                          // beacon

            // behind the cab: fuel tank (right), battery box (left), air tanks, exhaust stack
            b.AddRod(new Vector3(0.95f, 0.72f, 1.3f), Quaternion.identity, 0.29f, 0.9f, 18, chrome);
            b.AddBox(new Vector3(-0.95f, 0.72f, 1.3f), new Vector3(0.5f, 0.5f, 0.8f), blk);
            b.AddRod(new Vector3(-0.62f, 0.62f, -1.6f), Quaternion.identity, 0.12f, 0.9f, 10, gray);
            b.AddCylinder(new Vector3(0.95f, 2.45f, 1.62f), 0.065f, 2.2f, 10, chrome);
            b.AddCylinder(new Vector3(0.95f, 2.0f, 1.62f), 0.09f, 0.8f, 10, galv);                          // heat shield

            // crane pedestal (behind the cab)
            b.AddBevelBox(new Vector3(0f, 1.45f, 1.05f), new Vector3(1.1f, 0.8f, 1.3f), Quaternion.identity, 0.06f, yel);
            b.AddCylinder(new Vector3(0f, 1.87f, 1.05f), 0.58f, 0.06f, 24, yel);

            // flatbed: steel frame, wood deck, rub rails, stake pockets, rear sill + lights + underride guard
            b.AddBox(new Vector3(0f, 1.22f, -2.7f), new Vector3(2.5f, 0.16f, 6.1f), gray);
            b.AddBox(new Vector3(0f, BedTop - 0.025f, -2.7f), new Vector3(2.4f, 0.05f, 6.05f), Quaternion.identity, deck, Vector3.forward);
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(s * 1.24f, 1.25f, -2.7f), new Vector3(0.04f, 0.2f, 6.1f), gray);
                for (float z = 0.1f; z > -5.7f; z -= 0.6f) b.AddBox(new Vector3(s * 1.255f, 1.25f, z), new Vector3(0.03f, 0.12f, 0.07f), blk);
                b.AddBox(new Vector3(s * 0.95f, 1.2f, -5.77f), new Vector3(0.2f, 0.1f, 0.03f), red);        // tail lights
                b.AddBox(new Vector3(s * 1.2f, 1.25f, -2.7f), new Vector3(0.03f, 0.06f, 0.1f), amber);
            }
            for (int i = 0; i < 8; i++)   // rear sill warning stripes
                b.AddBox(new Vector3(-1.09f + i * 0.31f, 1.25f, -5.765f), new Vector3(0.31f, 0.12f, 0.02f), (i & 1) == 0 ? red : stripeW);
            b.AddBox(new Vector3(0f, 0.6f, -5.6f), new Vector3(2.1f, 0.12f, 0.1f), gray);                   // underride bar
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.6f, 0.85f, -5.6f), new Vector3(0.08f, 0.5f, 0.08f), gray);

            // boom rest (rear, right side, arm across to the centre line)
            b.AddBox(new Vector3(1.12f, 2.0f, -5.0f), new Vector3(0.12f, 1.3f, 0.12f), yel);
            b.AddBox(new Vector3(0.56f, 2.6f, -5.0f), new Vector3(1.24f, 0.08f, 0.14f), yel);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.28f, 2.7f, -5.0f), new Vector3(0.04f, 0.14f, 0.14f), yel);

            // outrigger housings (cross boxes under the bed) with warning stripes on the ends
            foreach (float z in OrZ)
            {
                b.AddBox(new Vector3(0f, 0.85f, z), new Vector3(2.3f, 0.28f, 0.3f), yel);
                foreach (int s in new[] { -1, 1 })
                    b.AddBox(new Vector3(s * 1.151f, 0.85f, z), new Vector3(0.005f, 0.28f, 0.3f), red);
            }
        }

        void AuthorBundle(MeshBuilder b, CTool.Mats m)
        {
            int wood = m.K(SurfaceKind.PineLumber), plate = m.K(SurfaceKind.Galvanized), band = m.K(SurfaceKind.MatteBlack);
            foreach (float z in new[] { -0.2f, -1.9f, -3.6f, -5.3f })
                b.AddBox(new Vector3(0f, BedTop + 0.045f, z), new Vector3(2.0f, 0.09f, 0.09f), wood);           // 4x4 dunnage
            const int layers = 10; float t = Units.In(1.5f);
            for (int i = 0; i < layers; i++)
            {
                float y = BedTop + 0.09f + t * 0.5f + i * t;
                b.Transform = Matrix4x4.TRS(new Vector3(0.02f * ((i % 3) - 1), y, -3.0f), Quaternion.Euler(0, 0, 90), Vector3.one);
                AuthorTrussShape(b, wood, plate, 6.6f, 1.6f);
            }
            b.ResetTransform();
            float top = BedTop + 0.09f + layers * t;
            foreach (float z in new[] { -1.2f, -4.8f })
            {
                b.AddBox(new Vector3(0f, top + 0.003f, z), new Vector3(1.7f, 0.006f, 0.035f), band);
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.85f, (top + BedTop) * 0.5f + 0.05f, z), new Vector3(0.006f, top - BedTop - 0.1f, 0.035f), band);
            }
        }

        /// <summary>Fink truss in the local YZ plane (span along Z, height along Y, 1.5" thick along X), centred at the origin.</summary>
        static void AuthorTrussShape(MeshBuilder b, int wood, int plate, float span, float h)
        {
            float t = Units.In(1.5f), d = Units.In(3.5f);
            float hs = span * 0.5f, yb = -h * 0.5f, yPeak = h * 0.5f - d * 0.5f;
            float H = hs * 0.86f;
            System.Func<float, float> yTop = z => yPeak - (yPeak - yb - d * 0.5f) * Mathf.Abs(z) / hs;
            float yc = yTop(H) - d * 0.6f;
            var peak = new Vector2(0f, yPeak);
            foreach (int s in new[] { -1, 1 })
            {
                Member(b, new Vector2(s * hs, yTop(hs)), peak, t, d, wood);                              // top chord incl. tail
                var bot = new Vector2(s * H / 3f, yc);
                var tq = new Vector2(s * H * 0.5f, yTop(H * 0.5f) - d * 0.4f);
                Member(b, bot, tq, t, d * 0.9f, wood);                                                  // W webs
                Member(b, bot, new Vector2(0f, yPeak - d * 0.5f), t, d * 0.9f, wood);
                b.AddBox(new Vector3(0f, yc + d * 0.2f, s * H), new Vector3(t + 0.004f, 0.13f, 0.2f), plate);   // heel plates
                b.AddBox(new Vector3(0f, yc, s * H / 3f), new Vector3(t + 0.004f, 0.1f, 0.14f), plate);
                b.AddBox(new Vector3(0f, tq.y, tq.x), new Vector3(t + 0.004f, 0.1f, 0.12f), plate);
            }
            Member(b, new Vector2(-H, yc), new Vector2(H, yc), t, d, wood);                              // bottom chord
            b.AddBox(new Vector3(0f, yPeak - 0.03f, 0f), new Vector3(t + 0.004f, 0.12f, 0.2f), plate);    // peak plate
        }

        static void Member(MeshBuilder b, Vector2 p, Vector2 q, float t, float d, int sub)
        {
            float dz = q.x - p.x, dy = q.y - p.y, len = Mathf.Sqrt(dz * dz + dy * dy);
            if (len < 1e-4f) return;
            float ang = Mathf.Atan2(dy, dz) * Mathf.Rad2Deg;
            b.AddBox(new Vector3(0f, (p.y + q.y) * 0.5f, (p.x + q.x) * 0.5f), new Vector3(t, d, len), Quaternion.Euler(-ang, 0f, 0f), sub, Vector3.forward);
        }

        void BuildOutriggers()
        {
            for (int i = 0; i < 4; i++)
            {
                int s = (i & 1) == 0 ? -1 : 1; float z = OrZ[i / 2];
                _beams[i] = Part(transform, "OutriggerBeam" + i, new Vector3(s * 1.15f, 0.85f, z), Quaternion.identity, (b, m) =>
                {
                    int yel = EquipModels.PaintY(m), red = m.C(new Color(0.85f, 0.06f, 0.05f)), wht = m.C(new Color(0.95f, 0.95f, 0.95f));
                    int blk = EquipModels.Black(m);
                    b.AddBox(new Vector3(-s * 0.95f, 0f, 0f), new Vector3(1.9f, 0.18f, 0.22f), yel);            // inner beam
                    for (int k = 0; k < 4; k++)                                                                    // stripes on the beam
                        b.AddBox(new Vector3(-s * (0.15f + k * 0.2f), 0f, 0f), new Vector3(0.1f, 0.185f, 0.225f), (k & 1) == 0 ? red : wht);
                    b.AddCylinder(new Vector3(s * 0.12f, -0.02f, 0f), 0.09f, 0.76f, 14, yel);                    // jack barrel
                    b.AddBox(new Vector3(s * 0.06f, 0f, 0f), new Vector3(0.14f, 0.2f, 0.2f), yel);
                    b.AddRod(new Vector3(s * 0.2f, 0.25f, 0f), Quaternion.Euler(90, 0, 0), 0.012f, 0.4f, 6, blk);  // hose
                });
                _legs[i] = Part(_beams[i], "Leg", new Vector3(s * 0.12f, 0f, 0f), Quaternion.identity, (b, m) =>
                {
                    int steel = EquipModels.Chrome(m), gray = EquipModels.PaintG(m);
                    b.AddCylinder(new Vector3(0f, -0.03f, 0f), 0.065f, 0.66f, 12, steel);                        // inner leg
                    b.AddCylinder(new Vector3(0f, -0.36f, 0f), 0.08f, 0.05f, 12, gray);                          // swivel ball
                    b.AddBox(new Vector3(0f, -0.38f, 0f), new Vector3(0.56f, 0.04f, 0.56f), gray);              // pad
                });
            }
        }

        void AuthorTurret(MeshBuilder b, CTool.Mats m)
        {
            int yel = EquipModels.PaintY(m), blk = EquipModels.Black(m), gray = EquipModels.PaintG(m), steel = EquipModels.Chrome(m);
            int wht = m.C(new Color(0.95f, 0.95f, 0.9f)), red = m.C(new Color(0.85f, 0.06f, 0.05f));
            b.AddCylinder(new Vector3(0f, -0.05f, 0f), 0.55f, 0.1f, 28, blk);                         // slewing bearing
            b.AddBevelBox(new Vector3(0f, 0.12f, -0.05f), new Vector3(1.1f, 0.25f, 1.15f), Quaternion.identity, 0.05f, yel);
            foreach (float x in new[] { -0.3f, 0.3f })
                EquipModels.SideProfile(b, x, 0.05f, yel,
                    new Vector2(-0.6f, 0.2f), new Vector2(0.5f, 0.2f), new Vector2(0.05f, 1.25f), new Vector2(-0.55f, 1.25f));
            b.AddCylinder(PivotLocal, Quaternion.Euler(0, 0, 90), 0.07f, 0.72f, 12, steel);           // boom foot pin
            b.AddBox(PivotLocal + new Vector3(0f, -1.0f, 0.6f), new Vector3(0.3f, 0.16f, 0.2f), yel);  // ram lug
            b.AddCylinder(PivotLocal + new Vector3(0f, -1.0f, 0.6f), Quaternion.Euler(0, 0, 90), 0.05f, 0.4f, 10, steel);
            // hoist: drum with flanges + hydraulic motor + planetary, on the right side
            var axX = Quaternion.Euler(0, 0, 90);
            b.AddBox(new Vector3(0.5f, 0.42f, 0.05f), new Vector3(0.05f, 0.5f, 0.5f), yel);
            b.AddBox(new Vector3(0.98f, 0.42f, 0.05f), new Vector3(0.05f, 0.5f, 0.5f), yel);
            b.AddCylinder(new Vector3(0.74f, 0.55f, 0.05f), axX, 0.17f, 0.42f, 18, blk);              // wound wire rope
            b.AddCylinder(new Vector3(0.545f, 0.55f, 0.05f), axX, 0.25f, 0.03f, 18, gray);
            b.AddCylinder(new Vector3(0.935f, 0.55f, 0.05f), axX, 0.25f, 0.03f, 18, gray);
            b.AddCylinder(new Vector3(1.06f, 0.55f, 0.05f), axX, 0.11f, 0.12f, 14, gray);            // motor
            b.AddRod(new Vector3(0.8f, 0.3f, -0.3f), Quaternion.Euler(10, 0, 0), 0.015f, 0.6f, 6, blk);
            // ride-around operator station on the left side
            b.AddBox(new Vector3(-0.85f, 0.02f, 0.05f), new Vector3(0.7f, 0.04f, 0.9f), m.K(SurfaceKind.Galvanized));  // floor plate
            b.AddBox(new Vector3(-0.6f, 0.15f, 0.05f), new Vector3(0.12f, 0.2f, 0.6f), yel);                             // bracket
            EquipModels.Seat(b, m, new Vector3(-0.85f, 0.55f, 0.05f));
            EquipModels.Joystick(b, m, new Vector3(-1.15f, 0.78f, 0.28f));
            EquipModels.Joystick(b, m, new Vector3(-0.55f, 0.78f, 0.28f));
            b.AddBox(new Vector3(-1.15f, 0.6f, 0.25f), new Vector3(0.1f, 0.3f, 0.12f), gray);
            b.AddBox(new Vector3(-0.55f, 0.6f, 0.25f), new Vector3(0.1f, 0.3f, 0.12f), gray);
            // load moment indicator display + load chart plate
            b.AddCylinder(new Vector3(-0.85f, 0.7f, 0.6f), 0.025f, 0.6f, 8, gray);
            b.AddBox(new Vector3(-0.85f, 1.05f, 0.6f), new Vector3(0.3f, 0.2f, 0.07f), Quaternion.Euler(15, 0, 0), gray);
            b.AddBox(new Vector3(-0.85f, 1.05f, 0.56f), new Vector3(0.24f, 0.14f, 0.01f), Quaternion.Euler(15, 0, 0), m.C(new Color(0.1f, 0.25f, 0.2f)));
            b.AddBox(new Vector3(-0.31f, 0.7f, -0.2f), new Vector3(0.01f, 0.4f, 0.3f), wht);
            b.AddBox(new Vector3(-0.312f, 0.88f, -0.2f), new Vector3(0.01f, 0.03f, 0.28f), red);
            b.AddCylinder(new Vector3(-0.85f, 0.25f, 0.5f), 0.03f, 0.4f, 8, blk);                    // hoses up the side
        }

        void AuthorBase(MeshBuilder b, CTool.Mats m)
        {
            int yel = EquipModels.PaintY(m), blk = EquipModels.Black(m), steel = EquipModels.Chrome(m);
            int red = m.C(new Color(0.85f, 0.06f, 0.05f)), wht = m.C(new Color(0.95f, 0.95f, 0.9f));
            b.AddBevelBox(new Vector3(0f, 0f, 3.45f), new Vector3(0.48f, 0.6f, 7.5f), Quaternion.identity, 0.04f, yel);
            b.AddCylinder(Vector3.zero, Quaternion.Euler(0, 0, 90), 0.15f, 0.54f, 14, yel);         // foot boss
            b.AddBox(new Vector3(0f, -0.36f, 2.4f), new Vector3(0.24f, 0.14f, 0.36f), yel);          // ram lug
            b.AddBox(new Vector3(0f, 0f, 7.17f), new Vector3(0.5f, 0.62f, 0.07f), blk);               // collar / wear pads
            b.AddCylinder(new Vector3(0f, 0.36f, 0f), Quaternion.Euler(0, 0, 90), 0.06f, 0.3f, 10, steel);   // rope guide roller
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(s * 0.241f, 0.18f, 4.0f), new Vector3(0.004f, 0.1f, 4.5f), blk);            // stencil band
                b.AddBox(new Vector3(s * 0.242f, -0.05f, 5.8f), new Vector3(0.004f, 0.26f, 1.2f), blk);          // model / capacity plate
                b.AddBox(new Vector3(s * 0.243f, -0.05f, 5.8f), new Vector3(0.004f, 0.18f, 1.05f), wht);
                b.AddCylinder(new Vector3(s * 0.245f, 0.05f, 1.2f), Quaternion.Euler(0, 0, 90), 0.09f, 0.008f, 14, wht); // angle indicator
                b.AddBox(new Vector3(s * 0.25f, 0.01f, 1.2f), new Vector3(0.004f, 0.08f, 0.01f), red);
            }
            b.AddBox(new Vector3(0.27f, -0.1f, 3.6f), new Vector3(0.05f, 0.1f, 5.6f), blk);           // hose tray
        }

        void AuthorFly(MeshBuilder b, CTool.Mats m, float w, float h, float end, bool head)
        {
            int yel = EquipModels.PaintY(m), blk = EquipModels.Black(m), red = m.C(new Color(0.85f, 0.06f, 0.05f)), steel = EquipModels.Chrome(m);
            float start = 0.4f, bodyEnd = head ? 7.3f : end;
            b.AddBevelBox(new Vector3(0f, 0f, (start + bodyEnd) * 0.5f), new Vector3(w, h, bodyEnd - start), Quaternion.identity, 0.03f, yel);
            foreach (int s in new[] { -1, 1 })
                for (int k = 0; k < 3; k++)   // section length marks (stencil panels)
                    b.AddBox(new Vector3(s * (w * 0.5f + 0.002f), h * 0.25f, bodyEnd - 0.4f - k * 0.18f), new Vector3(0.004f, 0.06f, 0.1f), k == 0 ? red : blk);
            if (!head)
            {
                b.AddBox(new Vector3(0f, 0f, end - 0.03f), new Vector3(w + 0.02f, h + 0.02f, 0.06f), blk);
                return;
            }
            // boom head: side plates, sheave, rope guard, anti-two-block switch + weight
            foreach (int s in new[] { -1, 1 })
                EquipModels.SideProfile(b, s * 0.13f, 0.03f, yel,
                    new Vector2(7.2f, -0.18f), new Vector2(7.6f, -0.2f), new Vector2(7.78f, -0.02f), new Vector2(7.7f, 0.2f), new Vector2(7.2f, 0.18f));
            b.AddCylinder(new Vector3(0f, 0f, 7.55f), Quaternion.Euler(0, 0, 90), 0.2f, 0.12f, 20, blk);
            b.AddCylinder(new Vector3(0f, 0f, 7.55f), Quaternion.Euler(0, 0, 90), 0.05f, 0.3f, 10, steel);
            b.AddBox(new Vector3(0f, 0.22f, 7.5f), new Vector3(0.24f, 0.03f, 0.35f), yel);
            b.AddBox(new Vector3(0.17f, -0.25f, 7.6f), new Vector3(0.06f, 0.1f, 0.06f), red);
            b.AddRod(new Vector3(0.17f, -0.45f, 7.65f), Quaternion.Euler(90, 0, 0), 0.006f, 0.3f, 4, steel);
            b.AddCylinder(new Vector3(0.17f, -0.62f, 7.65f), 0.05f, 0.08f, 10, red);
        }

        void AuthorHook(MeshBuilder b, CTool.Mats m)
        {
            // pick point (hook saddle) at the origin; block above
            int yel = EquipModels.PaintY(m), blk = EquipModels.Black(m), steel = EquipModels.Chrome(m), red = m.C(new Color(0.85f, 0.06f, 0.05f));
            float R = 0.1f, th = 0.045f; var c = new Vector2(0f, 0.08f);
            Vector2 prev = Vector2.zero;
            for (int i = 0; i <= 9; i++)
            {
                float a = (150f + i * 30f) * Mathf.Deg2Rad;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R;
                if (i > 0) Member(b, prev, p, th, th, steel);
                prev = p;
            }
            var top = c + new Vector2(Mathf.Cos(150f * Mathf.Deg2Rad), Mathf.Sin(150f * Mathf.Deg2Rad)) * R;
            Member(b, top, new Vector2(0f, 0.34f), th * 1.2f, th * 1.2f, steel);                                // shank
            b.AddBox(new Vector3(0f, 0.2f, 0.06f), new Vector3(0.01f, 0.012f, 0.13f), Quaternion.Euler(40, 0, 0), red);   // safety latch
            b.AddCylinder(new Vector3(0f, 0.36f, 0f), 0.05f, 0.06f, 12, steel);                                // swivel
            b.AddBevelBox(new Vector3(0f, 0.58f, 0f), new Vector3(0.12f, 0.38f, 0.32f), Quaternion.identity, 0.02f, blk);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.07f, 0.58f, 0f), new Vector3(0.02f, 0.4f, 0.34f), yel);  // side plates
            b.AddCylinder(new Vector3(0f, 0.6f, 0f), Quaternion.Euler(0, 0, 90), 0.05f, 0.18f, 10, steel);
            b.AddBox(new Vector3(0f, 0.8f, 0f), new Vector3(0.1f, 0.06f, 0.08f), steel);                       // dead-end becket
        }

        // ------------------------------------------------------------------ pose

        void PoseOutriggers()
        {
            float beam = Mathf.Clamp01(_out * 2f) * 1.55f, jack = Mathf.Clamp01(_out * 2f - 1f) * 0.45f;
            for (int i = 0; i < 4; i++)
            {
                if (_beams[i] == null) continue;
                int s = (i & 1) == 0 ? -1 : 1;
                _beams[i].localPosition = new Vector3(s * (1.15f + beam), 0.85f, OrZ[i / 2]);
                _legs[i].localPosition = new Vector3(s * 0.12f, -jack, 0f);
            }
        }

        float MinHookY()
        {
            float below = Rigged != null ? Rigged.PickLocal.y : 0f;
            if (_truss != null) below = Mathf.Max(below, 0.6f + _trussH);
            return transform.position.y + 0.05f + below;
        }

        void ApplyPose()
        {
            if (_turret == null) return;
            BoomDeg = Mathf.Clamp(BoomDeg, 0f, 78f);
            ExtendM = Mathf.Clamp(ExtendM, 0f, MaxExtend);
            _turret.localRotation = Quaternion.Euler(0f, SwingDeg, 0f);
            _boom.localRotation = Quaternion.Euler(-BoomDeg, 0f, 0f);
            float e = ExtendM / 3f;
            _fly1.localPosition = new Vector3(0f, 0f, e);
            _fly2.localPosition = new Vector3(0f, 0f, e);
            _fly3.localPosition = new Vector3(0f, 0f, e);
            Vector3 tip = _fly3.TransformPoint(TipLocal);
            CableM = Mathf.Clamp(CableM, 0.5f, 30f);
            CableM = Mathf.Min(CableM, Mathf.Max(0.5f, tip.y - MinHookY()));
            _hook.SetPositionAndRotation(tip - Vector3.up * CableM, Quaternion.Euler(0f, _turret.eulerAngles.y, 0f));
            OrientTruss();
            UpdateLines(tip);
        }

        void OrientTruss()
        {
            if (_truss == null) return;
            var job = _trussJob;
            if (job != null && job.Current != null && job.Current.Ghost != null && IsTrussStep(job.Current.Label))
                _truss.rotation = job.Current.Ghost().rot;
        }

        void UpdateLines(Vector3 tip)
        {
            if (_cable == null) return;
            _cable.SetPosition(0, _turret.TransformPoint(new Vector3(0.74f, 0.72f, 0.05f)));
            _cable.SetPosition(1, _boom.TransformPoint(new Vector3(0f, 0.42f, 0f)));
            _cable.SetPosition(2, _boom.TransformPoint(new Vector3(0f, 0.33f, 7.2f)));
            _cable.SetPosition(3, _fly3.TransformPoint(new Vector3(0f, 0.2f, 7.55f)));
            _cable.SetPosition(4, tip);
            _cable.SetPosition(5, _hook.position + Vector3.up * 0.8f);

            Vector3 hk = _hook.position;
            if (Rigged != null)
            {
                var lt = Rigged.transform; Vector3 s = Rigged.Size * 0.5f; float y = Rigged.Size.y;
                _slingA.enabled = _slingB.enabled = true;
                _slingA.SetPosition(0, lt.TransformPoint(new Vector3(-s.x, y, -s.z))); _slingA.SetPosition(1, hk); _slingA.SetPosition(2, lt.TransformPoint(new Vector3(s.x, y, s.z)));
                _slingB.SetPosition(0, lt.TransformPoint(new Vector3(s.x, y, -s.z))); _slingB.SetPosition(1, hk); _slingB.SetPosition(2, lt.TransformPoint(new Vector3(-s.x, y, s.z)));
            }
            else if (_truss != null)
            {
                float hs = _trussSpan * 0.5f * 0.43f, yq = _trussH * 0.5f - _trussH * 0.43f;
                _slingA.enabled = true; _slingB.enabled = false;
                _slingA.SetPosition(0, _truss.TransformPoint(new Vector3(0f, yq, -hs))); _slingA.SetPosition(1, hk); _slingA.SetPosition(2, _truss.TransformPoint(new Vector3(0f, yq, hs)));
            }
            else _slingA.enabled = _slingB.enabled = false;
        }

        // ------------------------------------------------------------------ operate

        void Lmi(string what)
        {
            if (Time.time < _nextToast) return;
            _nextToast = Time.time + 1.5f;
            Game.Toast(what);
        }

        protected override void Operate(float dt, MachineControls c)
        {
            if (c.Toggle) SetOutriggers(_outTarget < 0.5f);
            if (!Mathf.Approximately(_out, _outTarget))
            {
                _out = Mathf.MoveTowards(_out, _outTarget, dt * 0.25f);
                _hydraulicLoad = Mathf.Max(_hydraulicLoad, 0.6f);
                PoseOutriggers();
            }

            bool craneInput = c.Swing != 0 || c.Boom != 0 || c.Arm != 0 || c.Hoist != 0;
            if (craneInput && !OutriggersDown)
            {
                Lmi("Set the outriggers first (B) - crane functions locked");
                c.Swing = c.Boom = c.Arm = c.Hoist = 0;
                craneInput = false;
            }
            if (craneInput && LoadLb > 0f)
            {
                float pct = ChartPercent;
                if (pct >= 1f)
                {
                    bool blocked = false;
                    if (c.Boom < 0) { c.Boom = 0; blocked = true; }
                    if (c.Arm > 0) { c.Arm = 0; blocked = true; }
                    if (c.Hoist > 0 && LoadOnGround()) { c.Hoist = 0; blocked = true; }
                    if (blocked) Lmi("LMI: over the load chart");
                }
                else if (pct >= 0.9f && (c.Boom < 0 || c.Arm > 0)) Lmi($"LMI: {pct * 100f:0}% of chart - approaching capacity");
            }
            if (c.Swing != 0) SwingDeg = Mathf.Repeat(SwingDeg + c.Swing * 12f * dt, 360f);
            if (c.Boom != 0) BoomDeg += c.Boom * 5f * dt;
            if (c.Arm != 0) ExtendM += c.Arm * 0.5f * dt;
            if (c.Hoist != 0) CableM -= c.Hoist * 0.6f * dt;
            if (c.Swing != 0 || c.Boom != 0 || c.Arm != 0 || c.Hoist != 0) _hydraulicLoad = Mathf.Max(_hydraulicLoad, 0.75f);
            ApplyPose();
            if (c.Action) DoAction();
        }

        bool LoadOnGround()
        {
            if (Rigged == null) return false;
            return Physics.Raycast(Rigged.transform.position + Vector3.up * 0.05f, Vector3.down, 0.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        void DoAction()
        {
            if (_truss != null) { TrySetTruss(out string msg); if (!string.IsNullOrEmpty(msg)) Game.Toast(msg); return; }
            if (Rigged != null) { string what = Rigged.Label; Release(); Game.Toast("Set down: " + what); return; }
            if (TrussJob != null && HookNearBundle()) { RigNextTruss(); return; }
            EquipmentLoad best = null; float bd = 1.5f;
            foreach (var l in EquipmentLoad.All)
            {
                if (l == null || l.CarriedBy != null) continue;
                float d = Vector3.Distance(HookPos, l.transform.TransformPoint(l.PickLocal));
                if (d < bd) { bd = d; best = l; }
            }
            if (best != null) Rig(best);
            else Game.Toast(TrussJob != null ? "Swing the hook over the truss bundle on the bed to rig the next truss" : "Nothing within reach of the hook to rig");
        }

        protected override bool CanTravel(out string why)
        {
            why = null;
            if (_out > 0.001f || _outTarget > 0f) { why = "Raise the outriggers first (B)"; return false; }
            if (!BoomStowed) { why = "Stow the boom on its rest before driving"; return false; }
            return true;
        }

        public void SetOutriggers(bool down)
        {
            if (down == _outTarget > 0.5f) return;
            if (Mathf.Abs(Speed) > 0.1f) { Game.Toast("Stop the truck before working the outriggers"); return; }
            if (!down && (!BoomStowed || Rigged != null || _truss != null)) { Game.Toast("Stow the boom (and unhook the load) before raising the outriggers"); return; }
            _outTarget = down ? 1f : 0f;
        }

        // ------------------------------------------------------------------ IK

        /// <summary>Swings toward the point and picks boom angle + extension (steepest boom, extension only as needed) and cable
        /// so the hook's pick point lands on the point.</summary>
        public void PoseHookAt(Vector3 world)
        {
            Vector3 lp = transform.InverseTransformPoint(world) - TurretLocal;
            SwingDeg = Mathf.Repeat(Mathf.Atan2(lp.x, lp.z) * Mathf.Rad2Deg, 360f);
            float R = new Vector2(lp.x, lp.z).magnitude, ty = lp.y;
            float py = PivotLocal.y, pz = PivotLocal.z;
            float bestExt = 0f, bestDeg = 78f; bool found = false;
            float fallbackExt = -1f, fallbackDeg = 0f;
            for (float ext = 0f; ext <= MaxExtend + 1e-3f; ext += 0.05f)
            {
                float L = TipLocal.z + ext;
                float cos = (R - pz) / L;
                if (cos > 1f) continue;
                float deg = Mathf.Min(78f, Mathf.Acos(Mathf.Max(cos, 0f)) * Mathf.Rad2Deg);
                float h = py + L * Mathf.Sin(deg * Mathf.Deg2Rad);
                if (fallbackExt < 0f || h > py + (TipLocal.z + fallbackExt) * Mathf.Sin(fallbackDeg * Mathf.Deg2Rad)) { fallbackExt = ext; fallbackDeg = deg; }
                if (h - ty >= 1.0f) { bestExt = ext; bestDeg = deg; found = true; break; }
            }
            if (!found)
            {
                if (fallbackExt >= 0f) { bestExt = fallbackExt; bestDeg = fallbackDeg; }
                else { bestExt = MaxExtend; bestDeg = 0f; }   // out of reach: reach as far as possible
            }
            ExtendM = Mathf.Min(bestExt, MaxExtend); BoomDeg = bestDeg;
            CableM = 30f;   // provisional; set exactly below
            ApplyPose();
            Vector3 tip = _fly3.TransformPoint(TipLocal);
            CableM = tip.y - world.y;
            ApplyPose();
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ rigging

        public bool Rig(EquipmentLoad l)
        {
            if (l == null || Rigged != null || _truss != null) return false;
            Vector3 pick = l.transform.TransformPoint(l.PickLocal);
            if (Vector3.Distance(pick, HookPos) > 1.5f) { Game.Toast("Get the hook over the load first (within 5')"); return false; }
            float cap = CapacityLb(RadiusM);
            if (l.WeightLb > cap) { Lmi($"LMI: {l.WeightLb:N0} lb is over the chart ({cap:N0} lb at {Units.Format(RadiusM)})"); return false; }
            // take up the slack: hook to the pick point, load centred under the hook
            Vector3 tip = _fly3.TransformPoint(TipLocal);
            CableM = Mathf.Clamp(tip.y - pick.y, 0.5f, 30f);
            Rigged = l;   // before ApplyPose so MinHookY counts the load
            ApplyPose();
            l.transform.position += HookPos - l.transform.TransformPoint(l.PickLocal);
            l.PickUp(_hook);
            UpdateLines(tip);
            Game.Toast("Rigged: " + l.Label + $" ({l.WeightLb:N0} lb)");
            return true;
        }

        public void Release()
        {
            if (Rigged == null) return;
            var l = Rigged; Rigged = null;
            l.SetDown();
            UpdateLines(_fly3.TransformPoint(TipLocal));
        }

        // ------------------------------------------------------------------ trusses

        static bool IsTrussStep(string label) => label != null && (label.StartsWith("Set truss") || label.StartsWith("Set gable-end truss"));

        /// <summary>Nearest framing job (within 40 m) whose current step is setting a truss.</summary>
        public FramingJob TrussJob
        {
            get
            {
                FramingJob best = null; float bd = 40f * 40f;
                foreach (var j in FramingJob.All)
                {
                    if (j == null || j.Current == null || j.Current.Ghost == null || !IsTrussStep(j.Current.Label)) continue;
                    float d = (j.Current.Ghost().center - transform.position).sqrMagnitude;
                    if (d < bd) { bd = d; best = j; }
                }
                return best;
            }
        }

        bool HookNearBundle()
        {
            Vector3 top = transform.TransformPoint(BundleTopLocal), h = HookPos;
            return new Vector2(h.x - top.x, h.z - top.z).magnitude <= 2.5f && Mathf.Abs(h.y - top.y) < 3.5f;
        }

        /// <summary>Hangs the next truss of the nearest truss job on the hook (slung at its quarter points), turned like its layout.</summary>
        public bool RigNextTruss()
        {
            if (_truss != null || Rigged != null) return false;
            var job = TrussJob;
            if (job == null) { Game.Toast("No truss to set nearby"); return false; }
            var g = job.Current.Ghost();
            _trussSpan = Mathf.Max(1f, g.size.z); _trussH = Mathf.Max(0.3f, g.size.y);
            _trussLb = Mathf.Max(60f, _trussSpan / Units.Foot * 3.5f);
            _trussJob = job;
            float span = _trussSpan, h = _trussH;
            var go = CTool.Build("HangingTruss", (b, m) => AuthorTrussShape(b, m.K(SurfaceKind.PineLumber), m.K(SurfaceKind.Galvanized), span, h), _hook);
            go.layer = 2;
            _truss = go.transform;
            _truss.localPosition = new Vector3(0f, -(0.6f + h * 0.5f), 0f);
            _truss.rotation = g.rot;
            ApplyPose();
            Physics.SyncTransforms();
            Game.Toast("Truss rigged - fly it onto its layout mark, then F to set it");
            return true;
        }

        /// <summary>Sets the hanging truss when its centre is within 0.6 m horizontally / 0.5 m vertically of the layout ghost:
        /// the framing step runs (builds the real truss) and the hanging one is removed.</summary>
        public bool TrySetTruss(out string msg)
        {
            msg = "";
            if (_truss == null) { msg = "No truss on the hook"; return false; }
            var job = _trussJob;
            if (job == null || job.Current == null || job.Current.Ghost == null || !IsTrussStep(job.Current.Label))
            {
                msg = "That truss is no longer needed - set it down";
                DropTruss();
                return false;
            }
            var g = job.Current.Ghost();
            Vector3 d = _truss.position - g.center;
            float hd = new Vector2(d.x, d.z).magnitude;
            if (hd > 0.6f || Mathf.Abs(d.y) > 0.5f)
            {
                msg = $"Not on the mark yet: {Units.Format(hd)} to go, {Units.Format(Mathf.Abs(d.y))} {(d.y > 0 ? "high" : "low")}";
                return false;
            }
            bool ok = job.TryAdvance(null, out msg);
            if (ok) DropTruss();
            return ok;
        }

        void DropTruss()
        {
            if (_truss == null) return;
            var mf = _truss.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            _truss.gameObject.SetActive(false);
            Destroy(_truss.gameObject);
            _truss = null; _trussJob = null;
            if (_fly3 != null) UpdateLines(_fly3.TransformPoint(TipLocal));
        }

        protected override void LateUpdate()
        {
            OrientTruss();
            if (_fly3 != null) UpdateLines(_fly3.TransformPoint(TipLocal));
            base.LateUpdate();
        }

        // ------------------------------------------------------------------ HUD

        static string TrussName(string label)
        {
            if (label.StartsWith("Set gable-end truss")) return label.Contains("left") ? "Gable truss (left)" : "Gable truss (right)";
            int i = label.IndexOf('#');
            if (i < 0) return "Truss";
            int j = label.IndexOf(' ', i);
            return "Truss " + (j > i ? label.Substring(i, j - i) : label.Substring(i));
        }

        protected override string Readout
        {
            get
            {
                float cap = CapacityLb(RadiusM), pct = ChartPercent, load = LoadLb;
                string loadLine = $"Load {load:N0} lb / chart {cap:N0} lb ({pct * 100f:0}%)";
                if (load > 0f && pct >= 1f) loadLine = "<color=#ff4a3a>" + loadLine + "\nLMI: OVER THE LOAD CHART</color>";
                else if (load > 0f && pct >= 0.9f) loadLine = "<color=#ffc23a>" + loadLine + "  WARNING</color>";
                string outs = OutriggersDown ? "DOWN" : _out <= 0.001f ? "UP (stowed)" : (_outTarget > 0.5f ? "deploying..." : "retracting...");
                string s = $"Radius {Units.Format(RadiusM)}   boom {BoomDeg:0}°\n" +
                           $"Boom length {Units.Format(BoomLength)}\n" +
                           $"Hook height {Units.Format(Mathf.Max(0f, HookPos.y - transform.position.y))}\n" +
                           loadLine + "\n" +
                           $"Outriggers {outs}";
                var job = _truss != null ? _trussJob : TrussJob;
                if (job != null && job.Current != null && job.Current.Ghost != null && IsTrussStep(job.Current.Label))
                {
                    string name = TrussName(job.Current.Label);
                    if (_truss != null)
                    {
                        Vector3 d = _truss.position - job.Current.Ghost().center;
                        s += $"\n{name}: {Units.Format(new Vector2(d.x, d.z).magnitude)} left, {Units.Format(Mathf.Abs(d.y))} {(d.y >= 0 ? "high" : "low")}";
                    }
                    else s += $"\nNext: {name} - hook over the bed bundle, F";
                }
                return s;
            }
        }

        protected override void OnDestroy()
        {
            if (Rigged != null)
            {
                var l = Rigged; Rigged = null;
                try { if (l != null) l.SetDown(); } catch (System.Exception) { }
            }
            base.OnDestroy();
        }
    }
}
