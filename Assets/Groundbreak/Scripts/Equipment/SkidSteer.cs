using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Bobcat S650 class skid-steer loader: 4 x 12-16.5 tyres, ROPS cab with side screens and front door, rear engine
    /// compartment, radius-lift loader arms on rear towers, lift + tilt rams, quick-attach plate with a 72" dirt bucket or
    /// 48" pallet forks. Hierarchy: root → Arms (pitch about the rear pivot) → Attach (tilt about the arm hinge pins) →
    /// Bucket / Forks. The attachment self-levels (TiltDeg is relative to the machine, not the arms).
    /// </summary>
    public class SkidSteer : Machine
    {
        public enum Attachment { Bucket, Forks }

        public override string TypeId => "skidsteer";
        public override string DisplayName => "Skid steer";
        protected override string ControlHint => "W/S drive · A/D skid-steer turn · ↑/↓ lift arms · J/L curl/dump · F pick up/set down · B swap bucket/forks";
        protected override Vector3 SeatLocal => new Vector3(0f, 1.00f, -0.12f);
        protected override Vector3 EyeLocal => new Vector3(0f, 1.74f, -0.04f);

        public Attachment Tool { get; private set; }
        public float LiftDeg, TiltDeg;
        public float BucketLoadM3;
        public const float BucketCapacityM3 = 0.38f;
        public GroundCover LoadCover = GroundCover.Dirt;
        public const float RatedCapacityLb = 2690f;
        public EquipmentLoad Carried { get; private set; }

        public const float LiftMax = 85f, TiltMin = -45f, TiltMax = 30f;
        const float LiftRate = 24f, TiltRate = 42f;

        // geometry (meters, root local unless noted)
        static readonly Vector3 ArmPivot = new Vector3(0f, 1.30f, -0.85f);
        static readonly Vector2 HingeLocal = new Vector2(1.973f, -1.06f);        // arms local (z, y) at LiftDeg 0 → pin 0.24 m up
        const float ArmX = 0.78f, WheelX = 0.69f, WheelZ = 0.575f, WheelR = 0.42f, WheelW = 0.30f;
        static readonly Vector3 EdgeLocal = new Vector3(0f, -0.24f, 0.91f);       // bucket cutting edge (attach local)
        static readonly Vector3 ForkTipLocal = new Vector3(0f, -0.22f, 1.34f);    // fork tips midpoint (attach local)
        const float TineTopY = -0.20f, ForkHeelZ = 0.17f, TineX = 0.30f;

        public Transform Arms { get; private set; }
        public Transform Attach { get; private set; }
        Transform _bucket, _forks;
        HydraulicRam[] _rams = new HydraulicRam[0];
        GameObject _heap; int _heapLevel = -1; GroundCover _heapCover;
        float _toastT;

        public Vector3 CuttingEdge => Attach == null ? transform.position
            : Attach.TransformPoint(Tool == Attachment.Forks ? ForkTipLocal : EdgeLocal);

        // ================================================================== build

        protected override void Build()
        {
            TravelSpeed = 3.3f; TurnRate = 70f; ChaseDistance = 7f;
            Footprint = new Vector2(WheelX, WheelZ);
            BodyCenter = new Vector3(0f, 1.0f, 0.30f); BodySize = new Vector3(1.70f, 2.0f, 3.1f);
            EngineBasePitch = 0.85f;

            Part(transform, "Body", Vector3.zero, Quaternion.identity, BuildBody);
            Arms = Part(transform, "Arms", ArmPivot, Quaternion.identity, BuildArms);
            Attach = Part(Arms, "Attach", V3(HingeLocal), Quaternion.identity, BuildQuickAttach);
            _bucket = Part(Attach, "Bucket", Vector3.zero, Quaternion.identity, BuildBucket);
            _forks = Part(Attach, "Forks", Vector3.zero, Quaternion.identity, BuildForks);

            var rams = new System.Collections.Generic.List<HydraulicRam>();
            foreach (float s in new[] { -1f, 1f })
            {
                var la = Part(transform, "LiftRamA", new Vector3(s * 0.70f, 0.70f, -1.05f), Quaternion.identity, null);
                var lb = Part(Arms, "LiftRamB", new Vector3(s * 0.70f, 0.00f, 1.00f), Quaternion.identity, null);
                rams.Add(SizedRam(la, lb, 0.045f, 12, i => { LiftDeg = LiftMax * i / 12f; TiltDeg = 0; }));
                var ta = Part(Arms, "TiltRamA", new Vector3(s * 0.66f, -0.02f, 1.40f), Quaternion.identity, null);
                var tb = Part(Attach, "TiltRamB", new Vector3(s * 0.62f, 0.50f, 0.04f), Quaternion.identity, null);
                rams.Add(SizedRam(ta, tb, 0.040f, 34, i => { LiftDeg = LiftMax * (i / 5) / 6f; TiltDeg = Mathf.Lerp(TiltMin, TiltMax, (i % 5) / 4f); }));
            }
            _rams = rams.ToArray();

            LiftDeg = 0f; TiltDeg = 0f;
            SetAttachment(Attachment.Bucket);
            ApplyPose();

            AddBox(new Vector3(0f, 0.55f, -0.12f), new Vector3(1.68f, 0.95f, 2.10f));     // chassis + tyres
            AddBox(new Vector3(0f, 1.42f, -0.05f), new Vector3(0.96f, 1.12f, 1.00f));     // cab
            AddBox(new Vector3(0f, 1.08f, -0.80f), new Vector3(1.04f, 0.56f, 0.72f));     // engine cover
        }

        HydraulicRam SizedRam(Transform a, Transform b, float r, int n, System.Action<int> pose)
        {
            float mn = float.MaxValue, mx = 0f;
            for (int i = 0; i <= n; i++)
            {
                pose(i); ApplyPose(false);
                float d = Vector3.Distance(a.position, b.position);
                mn = Mathf.Min(mn, d); mx = Mathf.Max(mx, d);
            }
            float barrel = Mathf.Max(mn * 0.92f, mx * 0.5f + 0.03f);
            return HydraulicRam.Make(a, b, r, barrel, r * 0.55f, mx - barrel + 0.08f, EquipModels.Yellow);
        }

        static Vector3 V3(Vector2 zy) => new Vector3(0f, zy.y, zy.x);

        static Quaternion YZRot(Vector2 d)
        {
            Vector3 f = new Vector3(0f, d.y, d.x).normalized;
            return Quaternion.LookRotation(f, Vector3.Cross(f, Vector3.right));
        }

        static void Beam(MeshBuilder b, int sub, float x, float w, Vector2 p1, Vector2 p2, float h1, float h2)
        {
            Vector2 d = (p2 - p1).normalized, n = new Vector2(-d.y, d.x);
            EquipModels.SideProfile(b, x, w, sub, p1 + n * h1, p2 + n * h2, p2 - n * h2, p1 - n * h1);
        }

        static void Plate(MeshBuilder b, int sub, float x, float w, Vector2 p, Vector2 q, float t)
        {
            Vector2 d = q - p;
            b.AddBox(new Vector3(x, (p.y + q.y) * 0.5f, (p.x + q.x) * 0.5f), new Vector3(w, t, d.magnitude), YZRot(d), sub);
        }

        static void Boss(MeshBuilder b, int sub, Vector3 c, float r, float w) => b.AddCylinder(c, Quaternion.Euler(0, 0, 90), r, w, 12, sub);

        void BuildBody(MeshBuilder b, CTool.Mats m)
        {
            int y = EquipModels.PaintY(m), g = EquipModels.PaintG(m), blk = EquipModels.Black(m), steel = EquipModels.Chrome(m);
            int glass = EquipModels.Glass(m), white = m.K(SurfaceKind.PaintedSteel, EquipModels.White);
            int red = m.K(SurfaceKind.PaintedSteel, new Color(0.75f, 0.06f, 0.05f)), amber = m.K(SurfaceKind.PaintedSteel, EquipModels.Orange);
            int plastic = m.K(SurfaceKind.DarkPlastic);

            // wheels (12-16.5 on 8-bolt rims) and axle tubes
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    EquipModels.Wheel(b, m, new Vector3(sx * WheelX, WheelR, sz * WheelZ), WheelR, WheelW, EquipModels.Gray);
                    b.AddCylinder(new Vector3(sx * 0.55f, WheelR, sz * WheelZ), Quaternion.Euler(0, 0, 90), 0.09f, 0.10f, 12, blk);
                }
            // main frame (chain cases inside), belly pan
            b.AddBox(new Vector3(0f, 0.53f, -0.12f), new Vector3(1.04f, 0.56f, 2.06f), y);
            b.AddBox(new Vector3(0f, 0.23f, -0.12f), new Vector3(0.90f, 0.06f, 1.90f), g);
            foreach (float sx in new[] { -1f, 1f })
            {
                b.AddBox(new Vector3(sx * 0.53f, 0.48f, 0f), new Vector3(0.04f, 0.30f, 1.60f), g);            // chain case covers
                b.AddBox(new Vector3(sx * 0.69f, 0.875f, 0f), new Vector3(0.32f, 0.025f, 2.0f), blk);       // fenders
                b.AddBox(new Vector3(sx * 0.69f, 0.875f, -1.0f), new Vector3(0.32f, 0.10f, 0.02f), blk);
                b.AddBox(new Vector3(sx * 0.69f, 0.875f, 1.0f), new Vector3(0.32f, 0.10f, 0.02f), blk);
                // rear lift-arm towers + pivot boss, lift ram base lug
                b.AddBox(new Vector3(sx * 0.66f, 1.08f, -0.85f), new Vector3(0.12f, 0.62f, 0.32f), y);
                Boss(b, steel, ArmPivot + new Vector3(sx * 0.70f, 0, 0), 0.05f, 0.22f);
                b.AddBox(new Vector3(sx * 0.66f, 0.70f, -1.05f), new Vector3(0.10f, 0.14f, 0.14f), y);
                Boss(b, steel, new Vector3(sx * 0.70f, 0.70f, -1.05f), 0.04f, 0.16f);
                // tail lights + rear work lights
                b.AddBox(new Vector3(sx * 0.40f, 1.12f, -1.165f), new Vector3(0.12f, 0.08f, 0.02f), red);
                b.AddBox(new Vector3(sx * 0.40f, 1.02f, -1.165f), new Vector3(0.12f, 0.05f, 0.02f), amber);
                // grab handles on the front of the frame and cab posts
                b.AddRod(new Vector3(sx * 0.43f, 1.25f, 0.585f), Quaternion.Euler(-90f, 0, 0), 0.015f, 0.50f, 8, blk);
            }
            // front: step with tread plate
            b.AddBox(new Vector3(0f, 0.74f, 0.90f), new Vector3(0.80f, 0.04f, 0.16f), steel);
            for (int i = 0; i < 6; i++) b.AddBox(new Vector3(-0.30f + i * 0.12f, 0.765f, 0.90f), new Vector3(0.06f, 0.01f, 0.12f), blk);
            b.AddBox(new Vector3(0f, 0.55f, 0.85f), new Vector3(0.96f, 0.36f, 0.06f), y);
            // engine compartment + rear door with grille
            b.AddBox(new Vector3(0f, 1.05f, -0.80f), new Vector3(1.04f, 0.50f, 0.72f), y);
            b.AddBox(new Vector3(0f, 1.32f, -0.72f), new Vector3(0.96f, 0.06f, 0.60f), Quaternion.Euler(-6f, 0, 0), y);
            b.AddBox(new Vector3(0f, 0.98f, -1.16f), new Vector3(0.70f, 0.52f, 0.02f), g);
            for (int i = 0; i < 9; i++) b.AddBox(new Vector3(0f, 0.78f + i * 0.045f, -1.172f), new Vector3(0.62f, 0.018f, 0.01f), blk);
            b.AddBox(new Vector3(0.30f, 1.06f, -1.18f), new Vector3(0.06f, 0.025f, 0.02f), steel);        // door latch
            b.AddCylinder(new Vector3(0.36f, 1.42f, -0.92f), 0.035f, 0.18f, 10, blk);                      // exhaust
            b.AddCylinder(new Vector3(-0.32f, 1.36f, -0.95f), 0.06f, 0.04f, 10, blk);                     // fuel cap
            b.AddBox(new Vector3(0f, 0.36f, -1.16f), new Vector3(0.80f, 0.12f, 0.06f), g);                 // rear tie-down bumper
            // decals: black stripe + white model plate on the engine sides
            foreach (float sx in new[] { -1f, 1f })
            {
                b.AddBox(new Vector3(sx * 0.522f, 1.10f, -0.80f), new Vector3(0.004f, 0.08f, 0.66f), blk);
                b.AddBox(new Vector3(sx * 0.524f, 1.10f, -0.95f), new Vector3(0.004f, 0.045f, 0.20f), white);
            }

            // ---- ROPS / FOPS cab
            const float cx = 0.47f, z0 = -0.42f, z1 = 0.55f, yb = 0.81f, yt = 1.98f;
            float ph = yt - yb;
            foreach (float sx in new[] { -1f, 1f })
            {
                b.AddBox(new Vector3(sx * cx, yb + ph * 0.5f, z0), new Vector3(0.06f, ph, 0.08f), y);
                b.AddBox(new Vector3(sx * cx, yb + ph * 0.5f, z1), new Vector3(0.06f, ph, 0.06f), y);
                b.AddBox(new Vector3(sx * cx, 1.12f, (z0 + z1) * 0.5f), new Vector3(0.05f, 0.04f, z1 - z0), y);      // side rail
                // side screen: lower panel + vertical bar grid + upper glass strip
                b.AddBox(new Vector3(sx * cx, 0.96f, (z0 + z1) * 0.5f), new Vector3(0.02f, 0.30f, z1 - z0), y);
                for (int i = 1; i < 12; i++)
                    b.AddBox(new Vector3(sx * cx, 1.50f, z0 + (z1 - z0) * i / 12f), new Vector3(0.012f, 0.72f, 0.012f), blk);
                for (int i = 0; i < 3; i++)
                    b.AddBox(new Vector3(sx * cx, 1.26f + i * 0.24f, (z0 + z1) * 0.5f), new Vector3(0.012f, 0.012f, z1 - z0), blk);
                b.AddBox(new Vector3(sx * cx, 1.93f, (z0 + z1) * 0.5f), new Vector3(0.05f, 0.06f, z1 - z0), y);
                // work lights at the front roof corners
                b.AddBox(new Vector3(sx * 0.36f, 1.93f, z1 + 0.04f), new Vector3(0.14f, 0.08f, 0.06f), blk);
                b.AddBox(new Vector3(sx * 0.36f, 1.93f, z1 + 0.072f), new Vector3(0.11f, 0.055f, 0.006f), white);
                b.AddBox(new Vector3(sx * 0.36f, 1.93f, z0 - 0.045f), new Vector3(0.10f, 0.06f, 0.05f), blk);
            }
            b.AddBevelBox(new Vector3(0f, yt + 0.03f, (z0 + z1) * 0.5f + 0.02f), new Vector3(1.0f, 0.06f, 1.06f), Quaternion.identity, 0.02f, y);
            b.AddBox(new Vector3(0f, yt + 0.065f, (z0 + z1) * 0.5f), new Vector3(0.70f, 0.012f, 0.70f), blk);             // roof decal panel
            b.AddBox(new Vector3(0f, 1.40f, z0 - 0.005f), new Vector3(0.88f, 0.70f, 0.01f), glass);                         // rear window
            b.AddBox(new Vector3(0f, 1.06f, z0 - 0.01f), new Vector3(0.88f, 0.10f, 0.03f), y);
            b.AddBox(new Vector3(0f, 1.94f, z1), new Vector3(0.90f, 0.06f, 0.06f), y);                                      // front header
            // front door: frame + glass + handle + wiper
            float dz = z1 + 0.04f;
            b.AddBox(new Vector3(0f, 1.40f, dz), new Vector3(0.84f, 1.02f, 0.006f), glass);
            b.AddBox(new Vector3(-0.42f, 1.40f, dz), new Vector3(0.03f, 1.06f, 0.03f), blk);
            b.AddBox(new Vector3(0.42f, 1.40f, dz), new Vector3(0.03f, 1.06f, 0.03f), blk);
            b.AddBox(new Vector3(0f, 1.91f, dz), new Vector3(0.87f, 0.03f, 0.03f), blk);
            b.AddBox(new Vector3(0f, 0.89f, dz), new Vector3(0.87f, 0.03f, 0.03f), blk);
            b.AddBox(new Vector3(0f, 1.20f, dz), new Vector3(0.84f, 0.02f, 0.02f), blk);
            b.AddBox(new Vector3(0.34f, 1.30f, dz + 0.03f), new Vector3(0.03f, 0.14f, 0.03f), steel);
            b.AddBox(new Vector3(0.10f, 1.55f, dz + 0.012f), new Vector3(0.012f, 0.55f, 0.01f), Quaternion.Euler(0, 0, 25f), blk);

            // ---- interior: seat, seat bar, two joysticks, instrument panels, floor
            b.AddBox(new Vector3(0f, 0.83f, 0.10f), new Vector3(0.88f, 0.03f, 0.85f), plastic);
            b.AddBox(new Vector3(0f, 0.90f, -0.15f), new Vector3(0.40f, 0.14f, 0.40f), g);
            EquipModels.Seat(b, m, new Vector3(0f, 1.00f, -0.12f));
            foreach (float sx in new[] { -1f, 1f })
            {
                b.AddBevelBox(new Vector3(sx * 0.33f, 1.08f, 0.12f), new Vector3(0.14f, 0.14f, 0.36f), Quaternion.identity, 0.02f, plastic);
                EquipModels.Joystick(b, m, new Vector3(sx * 0.33f, 1.28f, 0.20f));
                b.AddBox(new Vector3(sx * 0.33f, 1.80f, 0.42f), new Vector3(0.18f, 0.14f, 0.12f), Quaternion.Euler(30f, 0, 0), plastic);   // overhead panels
                b.AddBox(new Vector3(sx * 0.33f, 1.78f, 0.36f), new Vector3(0.14f, 0.08f, 0.005f), Quaternion.Euler(30f, 0, 0), glass);
                b.AddRod(new Vector3(sx * 0.27f, 1.32f, 0.02f), Quaternion.Euler(-60f, 0, 0), 0.015f, 0.42f, 8, blk);   // seat bar arms
            }
            b.AddRod(new Vector3(0f, 1.18f, 0.20f), Quaternion.Euler(0, 90f, 0), 0.018f, 0.54f, 8, blk);              // seat bar (down)
        }

        void BuildArms(MeshBuilder b, CTool.Mats m)
        {
            int y = EquipModels.PaintY(m), steel = EquipModels.Chrome(m), blk = EquipModels.Black(m);
            Vector2 piv = Vector2.zero, knee = new Vector2(1.45f, 0.15f), hinge = HingeLocal;
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * ArmX;
                Beam(b, y, x, 0.08f, piv, knee, 0.10f, 0.09f);
                Beam(b, y, x, 0.08f, knee - new Vector2(0.05f, 0f), hinge, 0.10f, 0.07f);
                Boss(b, y, new Vector3(x, knee.y, knee.x), 0.10f, 0.08f);
                Boss(b, y, new Vector3(x, 0f, 0f), 0.10f, 0.09f);
                Boss(b, y, new Vector3(x, hinge.y, hinge.x), 0.075f, 0.09f);
                Boss(b, steel, new Vector3(sx * 0.73f, hinge.y, hinge.x), 0.035f, 0.14f);
                // lift ram + tilt ram lugs on the inside of the arm
                Boss(b, y, new Vector3(sx * 0.71f, 0.00f, 1.00f), 0.06f, 0.08f);
                Boss(b, y, new Vector3(sx * 0.68f, -0.02f, 1.40f), 0.06f, 0.10f);
                // hydraulic lines to the tilt rams along the arm
                Plate(b, blk, sx * 0.70f, 0.025f, new Vector2(0.10f, 0.10f), new Vector2(1.30f, 0.05f), 0.025f);
                // arm decal
                b.AddBox(new Vector3(x + sx * 0.041f, 0.05f, 0.75f), new Vector3(0.004f, 0.07f, 0.45f), blk);
            }
            // cross tube behind the attachment
            Vector2 tube = new Vector2(1.895f, -0.88f);
            b.AddRod(new Vector3(0f, tube.y, tube.x), Quaternion.Euler(0, 90f, 0), 0.065f, ArmX * 2f, 14, y);
        }

        void BuildQuickAttach(MeshBuilder b, CTool.Mats m)
        {
            int y = EquipModels.PaintY(m), steel = EquipModels.Chrome(m), blk = EquipModels.Black(m);
            b.AddBox(new Vector3(0f, 0.25f, 0.08f), new Vector3(1.40f, 0.56f, 0.04f), y);
            b.AddBox(new Vector3(0f, 0.53f, 0.07f), new Vector3(1.40f, 0.04f, 0.08f), y);
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float x in new[] { sx * 0.60f, sx * 0.68f })
                    EquipModels.SideProfile(b, x, 0.025f, y, new Vector2(-0.07f, -0.07f), new Vector2(0.06f, -0.07f), new Vector2(0.06f, 0.58f), new Vector2(-0.02f, 0.58f));
                Boss(b, steel, new Vector3(sx * 0.64f, 0.50f, 0.04f), 0.03f, 0.12f);
                Boss(b, steel, new Vector3(sx * 0.66f, 0f, 0f), 0.04f, 0.16f);
                // quick-tach levers + wedges
                b.AddRod(new Vector3(sx * 0.30f, 0.62f, 0.02f), Quaternion.Euler(-30f, 0, 0), 0.014f, 0.20f, 6, blk);
                b.AddBox(new Vector3(sx * 0.30f, -0.02f, 0.08f), new Vector3(0.08f, 0.06f, 0.05f), steel);
            }
        }

        static readonly Vector2[] BucketSide =
        {
            new Vector2(0.12f, -0.24f), new Vector2(0.90f, -0.24f), new Vector2(0.74f, 0.10f), new Vector2(0.38f, 0.42f), new Vector2(0.11f, 0.42f),
        };

        void BuildBucket(MeshBuilder b, CTool.Mats m)
        {
            int y = EquipModels.PaintY(m), steel = EquipModels.Chrome(m), blk = EquipModels.Black(m);
            const float w = 1.83f;
            foreach (float x in new[] { -w * 0.5f + 0.006f, w * 0.5f - 0.006f }) EquipModels.SideProfile(b, x, 0.012f, y, BucketSide);
            Plate(b, y, 0f, w - 0.02f, new Vector2(0.88f, -0.234f), new Vector2(0.20f, -0.234f), 0.012f);       // floor
            Plate(b, y, 0f, w - 0.02f, new Vector2(0.20f, -0.234f), new Vector2(0.12f, -0.15f), 0.012f);        // heel
            Plate(b, y, 0f, w - 0.02f, new Vector2(0.12f, -0.15f), new Vector2(0.11f, 0.42f), 0.012f);          // back
            Plate(b, y, 0f, w - 0.02f, new Vector2(0.11f, 0.42f), new Vector2(0.38f, 0.44f), 0.012f);           // spill guard
            b.AddBox(new Vector3(0f, -0.232f, 0.895f), new Vector3(w, 0.018f, 0.05f), steel);                   // bolt-on cutting edge
            for (int i = -6; i <= 6; i++) b.AddBox(new Vector3(i * 0.14f, -0.222f, 0.895f), new Vector3(0.02f, 0.006f, 0.02f), blk);  // edge bolts
            foreach (float x in new[] { -0.6f, -0.2f, 0.2f, 0.6f }) b.AddBox(new Vector3(x, -0.245f, 0.50f), new Vector3(0.05f, 0.012f, 0.70f), steel);   // wear strips
            foreach (float x in new[] { -0.6f, 0.6f })                                                        // attach mounting frame
                b.AddBox(new Vector3(x, 0.15f, 0.105f), new Vector3(0.12f, 0.60f, 0.02f), y);
            b.AddBox(new Vector3(0f, 0.48f, 0.12f), new Vector3(1.40f, 0.05f, 0.05f), y);
            foreach (float x in new[] { -w * 0.5f, w * 0.5f })                                                // side cutting edges
                Plate(b, steel, x, 0.02f, BucketSide[1], BucketSide[2], 0.05f);
        }

        void BuildForks(MeshBuilder b, CTool.Mats m)
        {
            int y = EquipModels.PaintY(m), steel = EquipModels.Chrome(m), blk = EquipModels.Black(m);
            // 48" carriage frame with back rest
            b.AddBox(new Vector3(0f, 0.02f, 0.13f), new Vector3(1.22f, 0.10f, 0.05f), y);
            b.AddBox(new Vector3(0f, 0.44f, 0.13f), new Vector3(1.22f, 0.10f, 0.05f), y);
            foreach (float x in new[] { -0.58f, -0.20f, 0.20f, 0.58f }) b.AddBox(new Vector3(x, 0.55f, 0.135f), new Vector3(0.05f, 1.06f, 0.04f), y);
            b.AddBox(new Vector3(0f, 1.06f, 0.135f), new Vector3(1.22f, 0.05f, 0.04f), y);
            foreach (float x in new[] { -0.6f, 0.6f }) b.AddBox(new Vector3(x, 0.23f, 0.11f), new Vector3(0.12f, 0.46f, 0.02f), y);
            // two 48" forged tines (4" x 1.5")
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * TineX;
                b.AddBox(new Vector3(x, 0.11f, ForkHeelZ - 0.02f), new Vector3(0.10f, 0.66f, 0.04f), steel);           // shank
                b.AddBox(new Vector3(x, TineTopY - 0.02f, (ForkHeelZ + ForkTipLocal.z - 0.10f) * 0.5f), new Vector3(0.10f, 0.04f, ForkTipLocal.z - 0.10f - ForkHeelZ), steel);
                b.AddBox(new Vector3(x, TineTopY - 0.025f, ForkTipLocal.z - 0.05f), new Vector3(0.10f, 0.03f, 0.10f), Quaternion.Euler(-4f, 0, 0), steel);   // tapered tip
                b.AddBox(new Vector3(x, 0.47f, ForkHeelZ - 0.045f), new Vector3(0.11f, 0.06f, 0.02f), blk);            // hook
            }
        }

        // ================================================================== pose

        void ApplyPose(bool aim = true)
        {
            if (Arms == null) return;
            Arms.localRotation = Quaternion.Euler(-LiftDeg, 0f, 0f);
            Attach.localRotation = Quaternion.Euler(LiftDeg - TiltDeg, 0f, 0f);   // self-levelling: TiltDeg is relative to the machine
            if (aim) foreach (var r in _rams) if (r != null) r.Aim();
        }

        public void SetAttachment(Attachment a)
        {
            if (Carried != null && a != Attachment.Forks) return;
            Tool = a;
            if (_bucket != null) _bucket.gameObject.SetActive(a == Attachment.Bucket);
            if (_forks != null) _forks.gameObject.SetActive(a == Attachment.Forks);
            if (a == Attachment.Forks) BucketLoadM3 = 0f;
            RefreshHeap();
        }

        public override float[] Joints
        {
            get => new[] { LiftDeg, TiltDeg, Tool == Attachment.Forks ? 1f : 0f };
            set
            {
                if (value == null) return;
                if (value.Length > 0) LiftDeg = Mathf.Clamp(value[0], 0f, LiftMax);
                if (value.Length > 1) TiltDeg = Mathf.Clamp(value[1], TiltMin, TiltMax);
                if (value.Length > 2) SetAttachment(value[2] > 0.5f ? Attachment.Forks : Attachment.Bucket);
                ApplyPose();
            }
        }

        // ================================================================== forks

        static float Opening(EquipmentLoad l) => l.Kind == "osb" ? 0.09f : 0.122f;

        /// <summary>Finds a load whose fork openings line up with the tines. over = it is heavier than the rated capacity.</summary>
        EquipmentLoad FindLoad(out Vector3 snapPos)
        {
            snapPos = Vector3.zero;
            if (Tool != Attachment.Forks || _forks == null) return null;
            Vector3 fwd = Attach.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return null;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 heel = Attach.TransformPoint(new Vector3(0f, TineTopY, ForkHeelZ));
            float tipAlong = ForkTipLocal.z - ForkHeelZ;
            EquipmentLoad best = null; float bestD = float.MaxValue;
            foreach (var l in EquipmentLoad.All)
            {
                if (l == null || l.CarriedBy != null) continue;
                // forks must run along the openings: pallets between the stringers (load Z), OSB lifts between the dunnage (load X)
                Vector3 axis = l.Kind == "osb" ? l.transform.right : l.transform.forward;
                axis.y = 0f; axis.Normalize();
                if (Mathf.Abs(Vector3.Dot(axis, fwd)) < Mathf.Cos(20f * Mathf.Deg2Rad)) continue;
                float depth = l.Kind == "osb" ? l.Size.x : l.Size.z;
                Vector3 c = l.transform.position;
                Vector3 rel = c - heel;
                float along = Vector3.Dot(rel, fwd), lateral = Vector3.Dot(rel, right);
                float nearFace = along - depth * 0.5f;
                if (Mathf.Abs(lateral) > 0.25f) continue;
                if (nearFace < -0.05f || nearFace > tipAlong + 0.5f) continue;     // ahead of the carriage, tips within 0.5 m of the opening
                float openY = c.y + Opening(l) - 0.03f;                           // middle of the fork pocket
                if (Mathf.Abs((heel.y - 0.02f) - openY) > 0.15f) continue;
                float score = Mathf.Abs(lateral) + Mathf.Abs(nearFace);
                if (score < bestD) { bestD = score; best = l; snapPos = heel + fwd * (0.02f + depth * 0.5f); snapPos.y = heel.y - Opening(l); }
            }
            return best;
        }

        public bool TryPickUp() => TryPickUp(true);

        bool TryPickUp(bool loud)
        {
            if (Carried != null) return false;
            if (Tool != Attachment.Forks) { if (loud) Game.Toast("Put the pallet forks on first (B)"); return false; }
            var l = FindLoad(out Vector3 snap);
            if (l == null) { if (loud) Game.Toast("Drive the forks all the way into the pallet openings"); return false; }
            if (l.WeightLb > RatedCapacityLb)
            {
                if (loud || Time.time > _toastT) { _toastT = Time.time + 3f; Game.Toast($"{l.Label}: {l.WeightLb:n0} lb is over rated capacity ({RatedCapacityLb:n0} lb), the rear wheels would lift"); }
                return false;
            }
            float yaw = l.transform.eulerAngles.y;
            l.transform.SetPositionAndRotation(snap, Quaternion.Euler(0f, yaw, 0f));
            l.PickUp(_forks);
            Carried = l;
            Sfx.Play("thud", snap, 0.35f);
            if (Operating) Game.Toast($"Picked up {l.Label} ({l.WeightLb:n0} lb)");
            return true;
        }

        public void SetDownCarried()
        {
            if (Carried == null) return;
            var l = Carried;
            Carried = null;
            l.SetDown();
            if (Operating) Game.Toast($"Set down {l.Label}");
        }

        float GroundBelow(Vector3 p)
        {
            float gy = float.NegativeInfinity;
            if (Physics.Raycast(p + Vector3.up * 0.25f, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
                gy = hit.point.y;
            if (Earth.Workable(p)) gy = Mathf.Max(gy, Earth.Height(p));
            return gy;
        }

        // ================================================================== bucket work

        Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : transform.forward; }

        float CutAcross(Vector3 edge, float targetY, float maxM3)
        {
            float got = 0f; Vector3 r = transform.right;
            for (int i = -1; i <= 1 && got < maxM3 - 1e-5f; i++)
            {
                Vector3 p = edge + r * (i * 0.6f);
                if (!Earth.Workable(p)) continue;
                float h = Earth.Height(p);
                if (h <= targetY + 0.004f) continue;
                if (got <= 1e-5f) LoadCover = Earth.CoverAt(p);
                got += Earth.Cut(p, 0.35f, targetY, maxM3 - got);
            }
            return got;
        }

        float FillAcross(Vector3 edge, float targetY, float maxM3)
        {
            float put = 0f; Vector3 r = transform.right;
            for (int i = -1; i <= 1 && put < maxM3 - 1e-5f; i++)
            {
                Vector3 p = edge + r * (i * 0.6f);
                if (!Earth.Workable(p)) continue;
                if (Earth.Height(p) >= targetY - 0.004f) continue;
                put += Earth.Fill(p, 0.35f, targetY, maxM3 - put, LoadCover);
            }
            return put;
        }

        /// <summary>Scripted grading pass: drags the cutting edge from → to at edgeY, cutting high ground into the bucket and
        /// back-filling low ground from it. Moves the machine along. Returns m³ moved (cut + fill).</summary>
        public float GradePass(Vector3 from, Vector3 to, float edgeY)
        {
            if (Tool != Attachment.Bucket) { if (Carried != null) return 0f; SetAttachment(Attachment.Bucket); }
            LiftDeg = 0f; TiltDeg = 0f; ApplyPose();
            Vector3 dir = to - from; dir.y = 0f;
            float len = dir.magnitude;
            if (len < 1e-3f) return 0f;
            dir /= len;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float edgeDist = transform.InverseTransformPoint(CuttingEdge).z;
            float moved = 0f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.25f));
            for (int s = 0; s <= steps; s++)
            {
                Vector3 p = from + dir * (len * s / steps); p.y = edgeY;
                float room = BucketCapacityM3 - BucketLoadM3;
                float cut = room > 1e-5f ? CutAcross(p, edgeY, room) : 0f;
                BucketLoadM3 += cut; moved += cut;
                if (BucketLoadM3 > 1e-5f)
                {
                    float fill = FillAcross(p, edgeY, BucketLoadM3);
                    BucketLoadM3 = Mathf.Max(0f, BucketLoadM3 - fill); moved += fill;
                }
            }
            Vector3 end = to - dir * edgeDist; end.y = transform.position.y;
            TeleportTo(end, yaw);
            RefreshHeap();
            return moved;
        }

        /// <summary>Dumps the bucket in front (into the haul truck when over its bed). Returns m³ (bank).</summary>
        public float DumpBucket()
        {
            float done = DumpAmount(BucketLoadM3);
            TiltDeg = TiltMin; ApplyPose();
            RefreshHeap();
            return done;
        }

        float DumpAmount(float bank)
        {
            bank = Mathf.Min(bank, BucketLoadM3);
            if (bank <= 1e-6f || Tool != Attachment.Bucket) return 0f;
            Vector3 edge = CuttingEdge;
            Vector3 p = edge + Flat(Attach.forward) * 0.25f;
            float done;
            if (HaulTruck.I != null && (HaulTruck.I.OverBed(edge) || HaulTruck.I.OverBed(p)))
                done = HaulTruck.I.Load(bank * Earth.SwellFactor) / Earth.SwellFactor;
            else
            {
                float h = Earth.Height(p);
                if (!float.IsNaN(h)) p.y = h;
                done = Earth.Deposit(p, bank * Earth.SwellFactor, LoadCover) / Earth.SwellFactor;
                if (done <= 0f && !Earth.Workable(p)) done = bank;
            }
            done = Mathf.Min(done, BucketLoadM3);
            BucketLoadM3 = Mathf.Max(0f, BucketLoadM3 - done);
            if (BucketLoadM3 < 1e-4f) BucketLoadM3 = 0f;
            return done;
        }

        // ================================================================== frame

        protected override void Operate(float dt, MachineControls c)
        {
            float lift0 = LiftDeg;
            LiftDeg = Mathf.Clamp(LiftDeg + c.Boom * LiftRate * dt, 0f, LiftMax);
            TiltDeg = Mathf.Clamp(TiltDeg - c.Tool * TiltRate * dt, TiltMin, TiltMax);
            if (c.Boom != 0 || c.Tool != 0) { ApplyPose(); _hydraulicLoad = Mathf.Max(_hydraulicLoad, 0.55f); }

            if (c.Toggle)
            {
                if (Mathf.Abs(Speed) > 0.1f) Game.Toast("Stop to swap attachments");
                else if (Carried != null) Game.Toast("Set the load down first");
                else if (BucketLoadM3 > 0.005f) Game.Toast("Dump the bucket first");
                else
                {
                    SetAttachment(Tool == Attachment.Bucket ? Attachment.Forks : Attachment.Bucket);
                    Game.Toast(Tool == Attachment.Forks ? "Swapped to pallet forks" : "Swapped to the 72\" dirt bucket");
                    Sfx.Play("thud", Attach.position, 0.4f);
                }
            }

            if (Tool == Attachment.Forks)
            {
                if (c.Action) { if (Carried != null) SetDownCarried(); else TryPickUp(true); }
                if (Carried == null && c.Boom > 0)
                {
                    var l = FindLoad(out _);
                    if (l != null && !TryPickUp(false))
                    {
                        // over capacity: the arms stall instead of lifting through the load
                        LiftDeg = lift0; ApplyPose(); _hydraulicLoad = 1f;
                    }
                }
                else if (Carried != null && c.Boom < 0)
                {
                    float gy = GroundBelow(Carried.transform.position);
                    if (!float.IsNegativeInfinity(gy) && Carried.transform.position.y - gy < 0.02f) SetDownCarried();
                }
                if (Carried != null) _hydraulicLoad = Mathf.Max(_hydraulicLoad, 0.4f + 0.4f * Carried.WeightLb / RatedCapacityLb);
                return;
            }

            // ---- bucket: grading while driving, dumping
            Vector3 edge = CuttingEdge;
            bool work = Earth.Workable(edge);
            float h = work ? Earth.Height(edge) : float.NaN;
            float spd = Speed;
            if (work && spd > 0.05f && edge.y < h - 0.01f)
            {
                float room = BucketCapacityM3 - BucketLoadM3;
                if (room > 1e-4f)
                {
                    float cut = CutAcross(edge, edge.y, Mathf.Min(room, BucketCapacityM3 * 1.2f * spd * dt));
                    BucketLoadM3 += cut;
                    _hydraulicLoad = 1f;
                }
                else if (Operating && Time.time > _toastT) { _toastT = Time.time + 4f; Game.Toast("Bucket heaped - back-drag or dump it"); }
            }
            else if (work && spd < -0.05f && BucketLoadM3 > 0f && Mathf.Abs(TiltDeg) < 8f && edge.y > h + 0.01f)
            {
                float fill = FillAcross(edge, edge.y, Mathf.Min(BucketLoadM3, BucketCapacityM3 * 1.2f * -spd * dt));
                BucketLoadM3 = Mathf.Max(0f, BucketLoadM3 - fill);
                _hydraulicLoad = Mathf.Max(_hydraulicLoad, 0.8f);
            }
            if (BucketLoadM3 > 0f && TiltDeg < -25f && (float.IsNaN(h) || edge.y > h + 0.02f))
            {
                DumpAmount(BucketCapacityM3 * dt / 0.9f);
                _hydraulicLoad = Mathf.Max(_hydraulicLoad, 0.6f);
            }
            RefreshHeap();
        }

        void RefreshHeap()
        {
            int level = Tool != Attachment.Bucket || BucketLoadM3 <= 0.004f ? 0 : Mathf.Clamp(Mathf.CeilToInt(BucketLoadM3 / BucketCapacityM3 * 4f), 1, 4);
            if (level == _heapLevel && (level == 0 || _heapCover == LoadCover)) return;
            _heapLevel = level; _heapCover = LoadCover;
            if (_heap != null) { var mf = _heap.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh); Destroy(_heap); _heap = null; }
            if (level == 0 || _bucket == null) return;
            float s = Mathf.Sqrt(level / 4f);
            var cover = LoadCover;
            _heap = CTool.Build("BucketHeap", (b, m) =>
            {
                int sub = m.K(cover == GroundCover.Gravel ? SurfaceKind.Gravel : SurfaceKind.Dirt);
                Vector2[] full =
                {
                    new Vector2(0.14f, -0.225f), new Vector2(0.88f, -0.225f), new Vector2(0.84f, 0.0f),
                    new Vector2(0.66f, 0.30f), new Vector2(0.40f, 0.46f), new Vector2(0.13f, 0.41f),
                };
                Vector2 anchor = new Vector2(0.30f, -0.20f);
                var poly = new Vector2[full.Length];
                for (int i = 0; i < full.Length; i++) poly[i] = anchor + (full[i] - anchor) * s;
                EquipModels.SideProfile(b, 0f, 1.78f, sub, poly);
            }, _bucket);
            _heap.layer = 2;
        }

        protected override string Readout
        {
            get
            {
                const float cuft = 35.3147f;
                string s = $"Attachment: {(Tool == Attachment.Bucket ? "72\" dirt bucket" : "48\" pallet forks")}\n";
                if (Tool == Attachment.Bucket)
                    s += $"Bucket: {BucketLoadM3 * cuft:0.0} / {BucketCapacityM3 * cuft:0.0} cu ft" + (BucketLoadM3 > 0 ? $" ({LoadCover.ToString().ToLower()})" : "") + "\n";
                else if (Carried != null)
                    s += $"Carrying: {Carried.Label}\n{Carried.WeightLb:n0} lb of {RatedCapacityLb:n0} lb rated capacity\n";
                else s += $"Forks empty · rated capacity {RatedCapacityLb:n0} lb\n";
                float pin = Attach != null ? Attach.position.y - transform.position.y : 0f;
                s += $"Hinge pin height: {Units.Format(pin)}   Tilt: {Mathf.RoundToInt(TiltDeg)} deg";
                var t = HaulTruck.I;
                if (t != null) s += $"\nTruck: {t.LoadYd:0.0} / {HaulTruck.CapacityYd:0} cu yd · {t.Loads} loads hauled" + (t.Away ? " (away)" : "");
                return s;
            }
        }

        protected override void OnDestroy()
        {
            if (Carried != null)
            {
                var l = Carried; Carried = null;
                try { if (l != null) l.SetDown(); } catch (System.Exception) { }   // re-parenting a child while this is being destroyed can throw (same as TruckCrane)
            }
            base.OnDestroy();
        }
    }
}
