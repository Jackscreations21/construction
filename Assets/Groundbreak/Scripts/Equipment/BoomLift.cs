using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// 40' telescopic straight boom lift (JLG 400S / Genie S-40 class): 4WD chassis 2.31 m wide on foam-filled tyres, 360°
    /// turntable with counterweight + engine hoods, 3-section telescoping boom pivoting at the turntable rear, lift ram, and a
    /// 0.91 x 1.83 m (36" x 72") platform with 1.1 m guard rails, toe boards, entry gate and control box, kept level.
    /// Platform floor max 12.2 m (40'). The operator stands in the basket; F lets go of the controls so the player can work
    /// from the parked basket with tools, F again takes the controls back.
    /// Hierarchy: root (chassis) → Turntable (SwingDeg about Y) → Boom (BoomDeg about local X at the pivot) → Fly1 → Fly2 →
    /// Platform (world-level, facing outward along the boom's horizontal direction).
    /// </summary>
    public class BoomLift : Machine
    {
        public override string TypeId => "boomlift";
        public override string DisplayName => "Boom lift";

        // ---- geometry (meters)
        const float TurntableY = 1.05f;                       // slewing ring top, root local
        static readonly Vector3 PivotLocal = new Vector3(0f, 0.95f, -0.95f);   // turntable local
        const float RetractedLen = 5.8f;                      // pivot → tip pin, retracted
        const float Drop = 1.25f;                             // tip pin height above the platform floor
        const float TipToCenter = 0.705f;                     // tip pin → platform centre, horizontally (outward)
        public const float MaxExtend = 6.05f;                 // gives 12.2 m (40') floor height at 75°
        const float PlatW = 1.83f, PlatD = 0.91f, RailH = 1.1f;
        const float StowDeg = -3f;

        public float SwingDeg, BoomDeg = StowDeg, ExtendM;

        Transform _turntable, _boom, _fly1, _fly2, _platform, _gate;
        float _gateOpen = 1f;
        int _leftFrame = -10;

        public Transform Platform => _platform;
        /// <summary>Platform floor height above the ground under the chassis.</summary>
        public float PlatformHeight => _platform != null ? _platform.position.y - transform.position.y : 0f;
        public bool OnControls => Operating;
        public bool Elevated => BoomDeg >= 10f || ExtendM >= 0.3f;

        protected override Transform SeatFrame => _platform != null ? _platform : transform;
        protected override Vector3 SeatLocal => new Vector3(0f, 0.02f, 0.02f);
        protected override Vector3 EyeLocal => new Vector3(0f, 1.65f, 0.02f);
        protected override string ControlHint =>
            "W/S/A/D drive (slow when raised) · ←/→ swing · ↑/↓ boom · I/K telescope · F let go of the controls (work from the basket)";

        public override float[] Joints
        {
            get => new[] { SwingDeg, BoomDeg, ExtendM };
            set
            {
                if (value == null) return;
                if (value.Length > 0) SwingDeg = Mathf.Repeat(value[0], 360f);
                if (value.Length > 1) BoomDeg = value[1];
                if (value.Length > 2) ExtendM = value[2];
                ApplyPose();
                Physics.SyncTransforms();
            }
        }

        float BoomLen => RetractedLen + ExtendM;
        static float Rad(float d) => d * Mathf.Deg2Rad;

        // ------------------------------------------------------------------ build

        protected override void Build()
        {
            BodyCenter = new Vector3(0f, 1.2f, 2.35f);
            BodySize = new Vector3(2.31f, 2.4f, 7.3f);
            Footprint = new Vector2(1.0f, 1.015f);
            TravelSpeed = 2.0f; TurnRate = 28f; ChaseDistance = 11f;
            EngineBasePitch = 0.9f;

            BuildChassis();
            _turntable = Part(transform, "Turntable", new Vector3(0f, TurntableY, 0f), Quaternion.identity, AuthorTurntable);
            _boom = Part(_turntable, "Boom", PivotLocal, Quaternion.identity, AuthorBase);
            _fly1 = Part(_boom, "Fly1", Vector3.zero, Quaternion.identity, AuthorFly1);
            _fly2 = Part(_fly1, "Fly2", Vector3.zero, Quaternion.identity, AuthorFly2);
            _platform = Part(_fly2, "Platform", Vector3.zero, Quaternion.identity, AuthorPlatform);
            _gate = Part(_platform, "Gate", new Vector3(-PlatW * 0.5f, 0f, -0.3f), Quaternion.identity, AuthorGate);

            // lift ram: barrel on the turntable riser below the pivot, rod on the base section underside
            var pinA = Part(_turntable, "RamPinA", PivotLocal + new Vector3(0f, -0.7f, 0f), Quaternion.identity, null);
            var pinB = Part(_boom, "RamPinB", new Vector3(0f, -0.28f, 1.6f), Quaternion.identity, null);
            HydraulicRam.Make(pinA, pinB, 0.075f, 1.3f, 0.045f, 1.3f, EquipModels.Orange);

            // colliders (layer 2 via the base): chassis, turntable, base boom, basket floor + rails
            AddBox(new Vector3(0f, 0.65f, 0f), new Vector3(1.6f, 0.6f, 3.3f));
            AddBox(new Vector3(0f, 0.5f, -0.35f), new Vector3(2.0f, 1.0f, 2.0f), _turntable);
            AddBox(new Vector3(0f, 0f, 2.5f), new Vector3(0.42f, 0.5f, 5.6f), _boom);
            float t = 0.05f;
            AddBox(new Vector3(0f, -0.03f, 0f), new Vector3(PlatW, 0.06f, PlatD), _platform);
            AddBox(new Vector3(0f, RailH * 0.5f, PlatD * 0.5f - t * 0.5f), new Vector3(PlatW, RailH, t), _platform);
            AddBox(new Vector3(0f, RailH * 0.5f, -PlatD * 0.5f + t * 0.5f), new Vector3(PlatW, RailH, t), _platform);
            AddBox(new Vector3(PlatW * 0.5f - t * 0.5f, RailH * 0.5f, 0f), new Vector3(t, RailH, PlatD), _platform);
            AddBox(new Vector3(-PlatW * 0.5f + t * 0.5f, RailH * 0.5f, -0.3775f), new Vector3(t, RailH, 0.155f), _platform);
            AddBox(new Vector3(-PlatW * 0.5f + t * 0.5f, RailH * 0.5f, 0.3525f), new Vector3(t, RailH, 0.205f), _platform);
            AddBox(new Vector3(0f, RailH * 0.5f, 0.275f), new Vector3(t, RailH, 0.55f), _gate);

            ApplyPose();
        }

        void BuildChassis()
        {
            Part(transform, "Chassis", Vector3.zero, Quaternion.identity, (b, m) =>
            {
                int org = m.K(SurfaceKind.PaintedSteel, EquipModels.Orange), gray = EquipModels.PaintG(m), blk = EquipModels.Black(m);
                int steel = EquipModels.Chrome(m), amber = m.C(new Color(1f, 0.6f, 0.05f)), red = m.C(new Color(0.8f, 0.05f, 0.05f));
                int white = m.K(SurfaceKind.PaintedSteel, EquipModels.White);
                // main frame: box section chassis with tapered ends
                EquipModels.SideProfile(b, 0f, 1.5f, org,
                    new Vector2(-1.65f, 0.45f), new Vector2(-1.45f, 0.35f), new Vector2(1.45f, 0.35f), new Vector2(1.65f, 0.45f),
                    new Vector2(1.65f, 0.95f), new Vector2(-1.65f, 0.95f));
                b.AddBox(new Vector3(0f, 0.955f, 0f), new Vector3(1.4f, 0.01f, 3.0f), gray);              // deck plate
                // axles + drive hubs (oscillating front axle, fixed rear)
                foreach (float z in new[] { -1.015f, 1.015f })
                {
                    b.AddBox(new Vector3(0f, 0.42f, z), new Vector3(1.75f, 0.2f, 0.22f), gray);
                    foreach (int s in new[] { -1, 1 })
                    {
                        b.AddCylinder(new Vector3(s * 0.86f, 0.42f, z), Quaternion.Euler(0, 0, 90), 0.16f, 0.14f, 12, blk);   // planetary hub
                        EquipModels.Wheel(b, m, new Vector3(s * 1.0f, 0.42f, z), 0.42f, 0.31f, EquipModels.Gray);
                    }
                }
                // steer cylinders front
                b.AddRod(new Vector3(0f, 0.55f, 1.25f), Quaternion.Euler(0, 90, 0), 0.035f, 1.3f, 8, steel);
                // turntable bearing base
                b.AddCylinder(new Vector3(0f, 1.0f, 0f), 0.62f, 0.1f, 28, gray);
                // tie-down / lifting lugs
                foreach (float z in new[] { -1.62f, 1.62f })
                    foreach (int s in new[] { -1, 1 })
                        b.AddBox(new Vector3(s * 0.55f, 0.6f, z), new Vector3(0.08f, 0.16f, 0.06f), blk);
                // boom rest (front, for the stowed boom)
                b.AddBox(new Vector3(0f, 1.28f, 1.5f), new Vector3(0.12f, 0.66f, 0.12f), org);
                b.AddBox(new Vector3(0f, 1.61f, 1.5f), new Vector3(0.5f, 0.04f, 0.16f), blk);
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.23f, 1.68f, 1.5f), new Vector3(0.04f, 0.12f, 0.16f), org);
                // drive/steer end markings, lights, decals
                foreach (int s in new[] { -1, 1 })
                {
                    b.AddBox(new Vector3(s * 0.6f, 0.75f, 1.655f), new Vector3(0.12f, 0.06f, 0.01f), amber);
                    b.AddBox(new Vector3(s * 0.6f, 0.75f, -1.655f), new Vector3(0.12f, 0.06f, 0.01f), red);
                    b.AddBox(new Vector3(s * 0.751f, 0.7f, 0f), new Vector3(0.01f, 0.18f, 1.6f), white);    // side decal band
                    b.AddBox(new Vector3(s * 0.752f, 0.7f, 0.55f), new Vector3(0.01f, 0.12f, 0.3f), blk);    // model plate
                }
                // drive direction triangles (blue = drive, yellow = steer)
                b.AddBox(new Vector3(0.3f, 0.961f, 1.35f), new Vector3(0.12f, 0.004f, 0.12f), Quaternion.Euler(0, 45, 0), m.C(new Color(0.1f, 0.3f, 0.8f)));
                b.AddBox(new Vector3(-0.3f, 0.961f, 1.35f), new Vector3(0.12f, 0.004f, 0.12f), Quaternion.Euler(0, 45, 0), m.C(new Color(0.95f, 0.8f, 0.1f)));
            });
        }

        void AuthorTurntable(MeshBuilder b, CTool.Mats m)
        {
            int org = m.K(SurfaceKind.PaintedSteel, EquipModels.Orange), gray = EquipModels.PaintG(m), blk = EquipModels.Black(m);
            int steel = EquipModels.Chrome(m), amber = m.C(new Color(1f, 0.6f, 0.05f));
            b.AddCylinder(new Vector3(0f, -0.05f, 0f), 0.56f, 0.1f, 28, blk);                        // slewing bearing
            b.AddBox(new Vector3(0f, 0.04f, -0.2f), new Vector3(1.9f, 0.08f, 2.1f), gray);            // turntable deck
            // counterweight (rear), rounded
            b.AddBevelBox(new Vector3(0f, 0.5f, -1.15f), new Vector3(0.4f, 0.9f, 1.9f), Quaternion.Euler(0, 90, 0), 0.12f, gray);
            // engine hood (left) + hydraulic tank / fuel hood (right)
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBevelBox(new Vector3(s * 0.7f, 0.45f, -0.25f), new Vector3(0.62f, 0.82f, 1.5f), Quaternion.identity, 0.07f, org);
                for (int i = 0; i < 6; i++)   // vent louvers
                    b.AddBox(new Vector3(s * 1.012f, 0.35f + i * 0.07f, -0.35f), new Vector3(0.01f, 0.03f, 0.7f), blk);
                b.AddBox(new Vector3(s * 0.7f, 0.865f, -0.25f), new Vector3(0.5f, 0.01f, 1.3f), blk);    // anti-slip strip
            }
            // ground control box on the left hood
            b.AddBox(new Vector3(-1.04f, 0.55f, 0.25f), new Vector3(0.08f, 0.4f, 0.32f), gray);
            b.AddCylinder(new Vector3(-1.09f, 0.68f, 0.33f), Quaternion.Euler(0, 0, 90), 0.025f, 0.03f, 10, m.C(new Color(0.85f, 0.05f, 0.05f)));
            b.AddBox(new Vector3(-1.085f, 0.5f, 0.2f), new Vector3(0.01f, 0.12f, 0.16f), m.C(new Color(0.9f, 0.9f, 0.85f)));
            // exhaust + beacon
            b.AddCylinder(new Vector3(-0.85f, 0.98f, -0.8f), 0.04f, 0.25f, 8, steel);
            b.AddCylinder(new Vector3(0.85f, 0.95f, -0.9f), 0.05f, 0.1f, 10, amber);
            // riser: two side plates up to the boom pivot
            foreach (float x in new[] { -0.27f, 0.27f })
                EquipModels.SideProfile(b, x, 0.04f, org,
                    new Vector2(-1.25f, 0.08f), new Vector2(-0.45f, 0.08f), new Vector2(-0.75f, 1.1f), new Vector2(-1.15f, 1.1f));
            b.AddCylinder(new Vector3(0f, PivotLocal.y, PivotLocal.z), Quaternion.Euler(0, 0, 90), 0.06f, 0.64f, 12, steel); // pivot pin
            b.AddCylinder(new Vector3(0f, PivotLocal.y - 0.7f, PivotLocal.z), Quaternion.Euler(0, 0, 90), 0.05f, 0.6f, 10, steel); // ram pin
            // hoses down the riser
            b.AddRod(new Vector3(0.18f, 0.55f, -0.75f), Quaternion.Euler(-70, 0, 0), 0.015f, 0.9f, 6, blk);
            b.AddRod(new Vector3(0.21f, 0.55f, -0.75f), Quaternion.Euler(-70, 0, 0), 0.015f, 0.9f, 6, blk);
        }

        void AuthorBase(MeshBuilder b, CTool.Mats m)
        {
            int white = m.K(SurfaceKind.PaintedSteel, EquipModels.White), blk = EquipModels.Black(m), org = m.K(SurfaceKind.PaintedSteel, EquipModels.Orange);
            b.AddBevelBox(new Vector3(0f, 0f, 2.5f), new Vector3(0.42f, 0.5f, 5.6f), Quaternion.identity, 0.03f, white);
            b.AddCylinder(Vector3.zero, Quaternion.Euler(0, 0, 90), 0.12f, 0.5f, 14, white);            // pivot boss
            b.AddBox(new Vector3(0f, -0.29f, 1.6f), new Vector3(0.2f, 0.12f, 0.3f), white);             // ram lug
            b.AddBox(new Vector3(0f, 0f, 5.26f), new Vector3(0.44f, 0.52f, 0.08f), blk);                 // collar + wear pads
            // hose carrier (energy chain) along the right side
            b.AddBox(new Vector3(0.25f, 0.05f, 2.6f), new Vector3(0.07f, 0.12f, 4.6f), blk);
            // decals: orange band + capacity / model panels
            foreach (int s in new[] { -1, 1 })
            {
                b.AddBox(new Vector3(s * 0.211f, 0.15f, 3.2f), new Vector3(0.004f, 0.08f, 2.8f), org);
                b.AddBox(new Vector3(s * 0.211f, -0.08f, 4.4f), new Vector3(0.004f, 0.18f, 0.6f), blk);
            }
        }

        void AuthorFly1(MeshBuilder b, CTool.Mats m)
        {
            int white = m.K(SurfaceKind.PaintedSteel, EquipModels.White), blk = EquipModels.Black(m);
            b.AddBevelBox(new Vector3(0f, 0f, 2.9f), new Vector3(0.36f, 0.43f, 5.4f), Quaternion.identity, 0.025f, white);
            b.AddBox(new Vector3(0f, 0f, 5.56f), new Vector3(0.38f, 0.45f, 0.07f), blk);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.181f, 0.12f, 5.2f), new Vector3(0.004f, 0.05f, 0.5f), m.C(new Color(0.85f, 0.1f, 0.1f)));
        }

        void AuthorFly2(MeshBuilder b, CTool.Mats m)
        {
            int white = m.K(SurfaceKind.PaintedSteel, EquipModels.White), steel = EquipModels.Chrome(m), org = m.K(SurfaceKind.PaintedSteel, EquipModels.Orange);
            b.AddBevelBox(new Vector3(0f, 0f, 3.05f), new Vector3(0.30f, 0.36f, 5.1f), Quaternion.identity, 0.02f, white);
            // boom head: clevis plates around the tip pin (z = 5.8)
            foreach (int s in new[] { -1, 1 })
                EquipModels.SideProfile(b, s * 0.13f, 0.03f, org,
                    new Vector2(5.55f, -0.18f), new Vector2(5.75f, -0.15f), new Vector2(5.92f, 0f), new Vector2(5.75f, 0.15f), new Vector2(5.55f, 0.18f));
            b.AddCylinder(new Vector3(0f, 0f, RetractedLen), Quaternion.Euler(0, 0, 90), 0.045f, 0.34f, 10, steel);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * 0.151f, 0.1f, 5.0f), new Vector3(0.004f, 0.05f, 0.5f), m.C(new Color(0.85f, 0.1f, 0.1f)));
        }

        void AuthorPlatform(MeshBuilder b, CTool.Mats m)
        {
            int galv = m.K(SurfaceKind.Galvanized), org = m.K(SurfaceKind.PaintedSteel, EquipModels.Orange), blk = EquipModels.Black(m);
            int gray = EquipModels.PaintG(m), steel = EquipModels.Chrome(m), red = m.C(new Color(0.85f, 0.05f, 0.05f));
            float hw = PlatW * 0.5f, hd = PlatD * 0.5f, r = 0.024f;
            var alongX = Quaternion.Euler(0, 90, 0);
            // floor: perforated grip deck on a tube frame
            b.AddBox(new Vector3(0f, -0.02f, 0f), new Vector3(PlatW, 0.04f, PlatD), galv);
            b.AddBox(new Vector3(0f, -0.07f, 0f), new Vector3(PlatW - 0.1f, 0.06f, 0.06f), org);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(0f, -0.07f, s * (hd - 0.04f)), new Vector3(PlatW, 0.06f, 0.06f), org);
            // toe boards (front, rear, right; left in two pieces around the gate)
            b.AddBox(new Vector3(0f, 0.075f, hd - 0.005f), new Vector3(PlatW, 0.15f, 0.01f), org);
            b.AddBox(new Vector3(0f, 0.075f, -hd + 0.005f), new Vector3(PlatW, 0.15f, 0.01f), org);
            b.AddBox(new Vector3(hw - 0.005f, 0.075f, 0f), new Vector3(0.01f, 0.15f, PlatD), org);
            b.AddBox(new Vector3(-hw + 0.005f, 0.075f, -0.3775f), new Vector3(0.01f, 0.15f, 0.155f), org);
            b.AddBox(new Vector3(-hw + 0.005f, 0.075f, 0.3525f), new Vector3(0.01f, 0.15f, 0.205f), org);
            // posts
            float[] px = { -hw + 0.025f, 0f, hw - 0.025f };
            foreach (float x in px)
                foreach (int s in new[] { -1, 1 })
                    b.AddCylinder(new Vector3(x, RailH * 0.5f, s * (hd - 0.025f)), r, RailH, 8, org);
            b.AddCylinder(new Vector3(hw - 0.025f, RailH * 0.5f, 0f), r, RailH, 8, org);
            b.AddCylinder(new Vector3(-hw + 0.025f, RailH * 0.5f, 0.25f), r, RailH, 8, org);
            b.AddCylinder(new Vector3(-hw + 0.025f, RailH * 0.5f, -0.3f), r, RailH, 8, org);
            // top + mid rails
            foreach (float y in new[] { RailH, RailH * 0.5f })
            {
                foreach (int s in new[] { -1, 1 }) b.AddRod(new Vector3(0f, y, s * (hd - 0.025f)), alongX, r, PlatW, 8, org);
                b.AddRod(new Vector3(hw - 0.025f, y, 0f), Quaternion.identity, r, PlatD, 8, org);
                b.AddRod(new Vector3(-hw + 0.025f, y, 0.3525f), Quaternion.identity, r, 0.205f, 8, org);
                b.AddRod(new Vector3(-hw + 0.025f, y, -0.3775f), Quaternion.identity, r, 0.155f, 8, org);
            }
            // control box on the front rail, with joystick, rocker switches and red e-stop
            b.AddBevelBox(new Vector3(0f, 0.98f, hd - 0.12f), new Vector3(0.46f, 0.26f, 0.18f), Quaternion.identity, 0.02f, gray);
            b.AddBox(new Vector3(0f, 1.115f, hd - 0.13f), new Vector3(0.42f, 0.01f, 0.15f), Quaternion.Euler(-12, 0, 0), blk);
            EquipModels.Joystick(b, m, new Vector3(0.12f, 1.2f, hd - 0.13f));
            b.AddCylinder(new Vector3(-0.15f, 1.14f, hd - 0.12f), 0.025f, 0.04f, 10, red);
            for (int i = 0; i < 4; i++) b.AddBox(new Vector3(-0.05f + i * 0.045f - 0.08f, 1.125f, hd - 0.17f), new Vector3(0.025f, 0.02f, 0.035f), blk);
            b.AddBox(new Vector3(0f, 1.0f, hd - 0.01f), new Vector3(0.3f, 0.12f, 0.005f), m.C(new Color(0.95f, 0.95f, 0.9f)));   // decal
            // foot switch + manual tube + harness anchors
            b.AddBox(new Vector3(0.25f, 0.03f, 0.2f), new Vector3(0.22f, 0.06f, 0.3f), blk);
            b.AddCylinder(new Vector3(hw - 0.08f, 0.75f, hd - 0.08f), 0.035f, 0.3f, 8, blk);
            foreach (int s in new[] { -1, 1 }) b.AddRing(new Vector3(s * 0.4f, 0.2f, -hd + 0.04f), Quaternion.Euler(90, 0, 0), 0.04f, 0.025f, 0.015f, 10, steel);
            // rotator / leveling bracket up to the tip pin (platform local (0, Drop, -TipToCenter))
            b.AddBox(new Vector3(0f, 0.3f, -hd - 0.03f), new Vector3(0.6f, 0.5f, 0.06f), org);           // mounting plate
            b.AddBox(new Vector3(0f, (Drop - 0.15f) * 0.5f, -hd - 0.15f), new Vector3(0.2f, Drop + 0.15f, 0.18f), org);   // rotator post
            b.AddCylinder(new Vector3(0f, 0.45f, -hd - 0.15f), 0.13f, 0.18f, 14, gray);                   // rotary actuator
            foreach (int s in new[] { -1, 1 })
                EquipModels.SideProfile(b, s * 0.17f, 0.03f, org,
                    new Vector2(-hd - 0.24f, Drop - 0.35f), new Vector2(-hd - 0.06f, Drop - 0.35f), new Vector2(-hd - 0.06f, Drop - 0.05f),
                    new Vector2(-TipToCenter, Drop + 0.12f), new Vector2(-TipToCenter - 0.1f, Drop + 0.05f));
            // leveling (slave) cylinder
            b.AddRod(new Vector3(0f, Drop - 0.55f, -hd - 0.28f), Quaternion.Euler(-80, 0, 0), 0.04f, 0.7f, 10, org);
            b.AddRod(new Vector3(0f, Drop - 0.1f, -hd - 0.25f), Quaternion.Euler(-80, 0, 0), 0.022f, 0.35f, 8, steel);
        }

        void AuthorGate(MeshBuilder b, CTool.Mats m)
        {
            // self-closing entry gate on the left side, hinge at local origin, spanning +Z 0.55 m
            int org = m.K(SurfaceKind.PaintedSteel, new Color(0.95f, 0.75f, 0.1f));
            float r = 0.022f, len = 0.55f;
            foreach (float y in new[] { RailH, RailH * 0.5f }) b.AddRod(new Vector3(0.025f, y, len * 0.5f), Quaternion.identity, r, len, 8, org);
            b.AddCylinder(new Vector3(0.025f, RailH * 0.5f + 0.1f, len), r, RailH * 0.5f + 0.1f, 8, org);
            b.AddCylinder(new Vector3(0.025f, RailH * 0.75f, 0.02f), r * 1.4f, RailH * 0.5f, 8, EquipModels.Black(m));   // spring hinge
        }

        // ------------------------------------------------------------------ pose

        void Constrain()
        {
            BoomDeg = Mathf.Clamp(BoomDeg, -5f, 75f);
            float maxExt = MaxExtend;
            if (BoomDeg < 0f)
            {
                // keep the floor at least 0.15 m above the ground when booming out low
                float drop = Mathf.Sin(-Rad(BoomDeg));
                float floorMin = 0.15f, pivotY = TurntableY + PivotLocal.y;
                if (drop > 1e-4f) maxExt = Mathf.Min(maxExt, (pivotY - Drop - floorMin) / drop - RetractedLen);
            }
            ExtendM = Mathf.Clamp(ExtendM, 0f, Mathf.Max(0f, maxExt));
        }

        void ApplyPose()
        {
            if (_turntable == null) return;
            Constrain();
            _turntable.localRotation = Quaternion.Euler(0f, SwingDeg, 0f);
            _boom.localRotation = Quaternion.Euler(-BoomDeg, 0f, 0f);
            float e = ExtendM * 0.5f;
            _fly1.localPosition = new Vector3(0f, 0f, e);
            _fly2.localPosition = new Vector3(0f, 0f, e);
            LevelPlatform();
        }

        /// <summary>Leveling link: the basket hangs world-level below the tip pin, facing outward.</summary>
        void LevelPlatform()
        {
            if (_platform == null) return;
            Vector3 tip = _fly2.TransformPoint(new Vector3(0f, 0f, RetractedLen));
            Vector3 fwd = _turntable.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            _platform.SetPositionAndRotation(tip - rot * new Vector3(0f, Drop, -TipToCenter), rot);
        }

        // ------------------------------------------------------------------ operate

        protected override void Operate(float dt, MachineControls c)
        {
            bool moving = false;
            if (c.Swing != 0) { SwingDeg = Mathf.Repeat(SwingDeg + c.Swing * 6f * dt, 360f); moving = true; }
            if (c.Boom != 0) { BoomDeg += c.Boom * 6f * dt; moving = true; }
            if (c.Arm != 0) { ExtendM += c.Arm * 0.35f * dt; moving = true; }
            if (moving) _hydraulicLoad = Mathf.Max(_hydraulicLoad, 0.7f);
            ApplyPose();
            if (c.Action && Operating) StandInBasket();
        }

        protected override bool CanTravel(out string why) { why = null; return true; }

        protected override bool CanExit(out string why)
        {
            why = null;
            if (PlatformHeight < 0.8f) return true;
            why = "Lower the platform to climb out (or F to let go of the controls and work from the basket)";
            return false;
        }

        protected override void Update()
        {
            TravelSpeed = Elevated ? 0.25f : 2.0f;
            base.Update();
            // on foot in the basket: F takes the controls back
            var p = Game.Player;
            if (!Operating && Current == null && !Game.UIOpen && Time.frameCount != _leftFrame && p != null && p.enabled
                && GInput.GetKeyDown(KeyCode.F) && PlayerInBasket())
                TakeControls();
            // entry gate: swings open when the platform is down and nobody is on the controls
            float target = (PlatformHeight < 0.8f && !Operating) ? 1f : 0f;
            _gateOpen = Mathf.MoveTowards(_gateOpen, target, Time.deltaTime * 2f);
            if (_gate != null) _gate.localRotation = Quaternion.Euler(0f, -80f * _gateOpen, 0f);
        }

        protected override void LateUpdate()
        {
            LevelPlatform();
            base.LateUpdate();
        }

        // ------------------------------------------------------------------ basket

        /// <summary>Are the player's feet inside the platform?</summary>
        public bool PlayerInBasket()
        {
            var p = Game.Player;
            if (p == null || _platform == null) return false;
            Vector3 l = _platform.InverseTransformPoint(p.transform.position);
            return Mathf.Abs(l.x) < PlatW * 0.5f + 0.05f && Mathf.Abs(l.z) < PlatD * 0.5f + 0.05f && l.y > -0.3f && l.y < 0.7f;
        }

        /// <summary>Let go of the controls: the player stands in the basket on foot (tools in hand).</summary>
        public void StandInBasket()
        {
            if (!Operating || _platform == null) return;
            _leftFrame = Time.frameCount;
            LevelPlatform();
            Leave(false, _platform.TransformPoint(new Vector3(0f, 0f, -0.05f)));
            Game.Toast("Working from the basket - F takes the platform controls");
        }

        /// <summary>From standing in the basket: take the platform controls again.</summary>
        public void TakeControls()
        {
            if (Operating || (Current != null && Current != this)) return;
            Enter();
        }

        // ------------------------------------------------------------------ HUD

        protected override string Readout
        {
            get
            {
                Vector3 d = _platform.position - _turntable.position; d.y = 0f;
                return $"Platform height {Units.Format(Mathf.Max(0f, PlatformHeight))}\n" +
                       $"Reach {Units.Format(d.magnitude)}\n" +
                       $"Boom angle {BoomDeg:0}°   telescope {Units.Format(ExtendM)}\n" +
                       "Capacity 500 lb (unrestricted)\n" +
                       (Elevated ? "Drive: elevated - creep speed" : "Drive: stowed");
            }
        }

        GUIStyle _footHint; int _footH;

        protected override void OnGUI()
        {
            base.OnGUI();
            if (Operating || Current != null || Game.UIOpen || !PlayerInBasket()) return;
            if (_footHint == null || _footH != Screen.height)
            {
                _footH = Screen.height;
                _footHint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(11, Mathf.RoundToInt(Screen.height / 50f)), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            const string msg = "F: take the platform controls";
            var r = new Rect(0f, Screen.height * 0.70f, Screen.width, _footHint.fontSize * 1.6f);
            _footHint.normal.textColor = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), msg, _footHint);
            _footHint.normal.textColor = Color.white;
            GUI.Label(r, msg, _footHint);
        }
    }
}
