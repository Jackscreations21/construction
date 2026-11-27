using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// 3.5-ton class mini excavator (Kubota KX040 / CAT 303.5 size): rubber tracks, backfill blade, minimal-tail-swing upper
    /// with counterweight, 4-post ROPS canopy, offset boom swing bracket, banana boom, stick, 24" digging bucket with a real
    /// four-bar bucket linkage. Hierarchy: root (undercarriage) → House (swing) → Boom → Stick → Bucket. Digs real ground
    /// through <see cref="Earth"/> and loads <see cref="HaulTruck"/>.
    /// </summary>
    public class MiniExcavator : Machine
    {
        public override string TypeId => "miniex";
        public override string DisplayName => "Mini excavator";
        protected override string ControlHint => "W/S/A/D tracks · ←/→ swing · ↑/↓ boom · I/K stick out/in · J/L bucket curl/dump · B blade";
        protected override Transform SeatFrame => House != null ? House : transform;
        protected override Vector3 SeatLocal => new Vector3(-0.18f, 0.55f, -0.02f);
        protected override Vector3 EyeLocal => new Vector3(-0.18f, 1.40f, 0.04f);   // seated eye ~0.85 m above the cushion

        // ---- joints (degrees)
        public float SwingDeg, BoomDeg, StickDeg, BucketDeg;
        public bool BladeDown;
        public float BucketLoadM3;
        public const float BucketCapacityM3 = 0.11f;
        public float TotalDugM3;

        public const float BoomMin = -58f, BoomMax = 65f, StickMin = 25f, StickMax = 150f, BucketMin = -40f, BucketMax = 140f;
        const float MaxSwingRate = 54f, SwingAccel = 110f, BoomRate = 30f, StickRate = 34f, BucketRate = 38f;
        const float BladeUpDeg = -25f;

        // ---- geometry (meters)
        static readonly Vector3 HousePivot = new Vector3(0f, 0.55f, 0f);           // top of the slew ring (root local)
        const float BoomX = 0.18f, FootY = 0.45f, FootZ = 0.80f;                    // boom foot pin (house local)
        const float L1 = 2.45f, L2 = 1.45f;                                         // boom pin-pin, stick pin-pin
        static readonly Vector3 TipLocal = new Vector3(0f, -0.139f, 0.861f);       // middle tooth tip (bucket local)
        static readonly Vector3 MouthNormalLocal = new Vector3(0f, -0.946f, 0.324f);
        // bucket linkage (stick-local (z, y))
        static readonly Vector2 RockerPivot = new Vector2(1.20f, 0.08f), BucketLinkPin = new Vector2(-0.02f, 0.25f);
        const float RockerLen = 0.26f, LinkLen = 0.30f;
        // bucket outline (bucket-local (z, y)), mouth runs from the tip back to the rear lip
        static readonly Vector2[] Shell =
        {
            new Vector2(0.10f, -0.30f), new Vector2(0.04f, 0.02f), new Vector2(0.14f, 0.24f),
            new Vector2(0.42f, 0.27f), new Vector2(0.66f, 0.12f), new Vector2(0.80f, -0.06f),
        };

        public Transform House { get; private set; }
        public Transform Boom { get; private set; }
        public Transform Stick { get; private set; }
        public Transform Bucket { get; private set; }
        public Transform Blade { get; private set; }
        Transform _rocker, _link, _bucketRamB;
        HydraulicRam[] _rams = new HydraulicRam[0];
        GameObject _dirt; int _dirtLevel = -1;
        float _swingVel, _bladeT, _dumpToastT;

        public Vector3 BucketTip => Bucket != null ? Bucket.TransformPoint(TipLocal) : transform.position;
        Vector3 MouthNormal => Bucket != null ? Bucket.TransformDirection(MouthNormalLocal) : Vector3.down;

        // ================================================================== build

        protected override void Build()
        {
            TravelSpeed = 1.3f; TurnRate = 28f; ChaseDistance = 8f;
            Footprint = new Vector2(0.70f, 0.95f);
            BodyCenter = new Vector3(0f, 1.15f, -0.05f); BodySize = new Vector3(1.7f, 2.3f, 2.5f);
            EngineBasePitch = 0.75f;

            Part(transform, "Undercarriage", Vector3.zero, Quaternion.identity, BuildUndercarriage);
            Blade = Part(transform, "Blade", new Vector3(0f, 0.30f, 0.70f), Quaternion.identity, BuildBlade);
            House = Part(transform, "House", HousePivot, Quaternion.identity, BuildHouse);
            Boom = Part(House, "Boom", new Vector3(BoomX, FootY, FootZ), Quaternion.identity, BuildBoom);
            Stick = Part(Boom, "Stick", new Vector3(0f, 0f, L1), Quaternion.identity, BuildStick);
            Bucket = Part(Stick, "Bucket", new Vector3(0f, 0f, L2), Quaternion.identity, BuildBucket);
            _rocker = Part(Stick, "Rocker", V3(RockerPivot), Quaternion.identity, (b, m) =>
            {
                int p = EquipModels.PaintY(m);
                foreach (float x in new[] { -0.105f, 0.105f })
                    b.AddBox(new Vector3(x, 0f, RockerLen * 0.5f), new Vector3(0.02f, 0.07f, RockerLen + 0.06f), p);
                b.AddCylinder(Vector3.zero, Quaternion.Euler(0, 0, 90), 0.035f, 0.26f, 10, EquipModels.Chrome(m));
            });
            _link = Part(Stick, "Link", Vector3.zero, Quaternion.identity, (b, m) =>
            {
                int p = EquipModels.PaintY(m);
                foreach (float x in new[] { -0.13f, 0.13f })
                    b.AddBox(new Vector3(x, 0f, LinkLen * 0.5f), new Vector3(0.02f, 0.08f, LinkLen + 0.06f), p);
                b.AddCylinder(Vector3.zero, Quaternion.Euler(0, 0, 90), 0.04f, 0.30f, 10, EquipModels.Chrome(m));
                b.AddCylinder(new Vector3(0, 0, LinkLen), Quaternion.Euler(0, 0, 90), 0.035f, 0.31f, 10, EquipModels.Chrome(m));
            });
            _bucketRamB = Part(Stick, "BucketRamB", Vector3.zero, Quaternion.identity, null);

            // ram pins
            var boomA = Part(House, "BoomRamA", new Vector3(BoomX, 0.07f, 0.92f), Quaternion.identity, null);
            var boomB = Part(Boom, "BoomRamB", new Vector3(0f, 0.15f, 0.95f), Quaternion.identity, null);
            var stickA = Part(Boom, "StickRamA", new Vector3(0f, 0.59f, 0.95f), Quaternion.identity, null);
            var stickB = Part(Stick, "StickRamB", new Vector3(0f, 0.27f, -0.26f), Quaternion.identity, null);
            var bucketA = Part(Stick, "BucketRamA", new Vector3(0f, 0.25f, 0.12f), Quaternion.identity, null);
            var bladeA = Part(transform, "BladeRamA", new Vector3(0f, 0.47f, 0.60f), Quaternion.identity, null);
            var bladeB = Part(Blade, "BladeRamB", new Vector3(0f, 0.14f, 0.40f), Quaternion.identity, null);

            SwingDeg = 0; BoomDeg = 0; StickDeg = 90; BucketDeg = 30; _bladeT = 0;
            _rams = new[]
            {
                SizedRam(boomA, boomB, 0.055f, v => BoomDeg = v, BoomMin, BoomMax),
                SizedRam(stickA, stickB, 0.048f, v => StickDeg = v, StickMin, StickMax),
                SizedRam(bucketA, _bucketRamB, 0.042f, v => BucketDeg = v, BucketMin, BucketMax),
                SizedRam(bladeA, bladeB, 0.045f, v => _bladeT = v, 0f, 1f),
            };

            // parked: blade down, bucket resting on the ground in front
            BladeDown = true; _bladeT = 1f; SwingDeg = 0f;
            if (!PoseTipAt(transform.TransformPoint(new Vector3(BoomX, 0.02f, 3.1f))))
            { BoomDeg = -25f; StickDeg = 95f; BucketDeg = 60f; ApplyPose(); }

            // colliders: undercarriage on the root, upper + canopy roof on the house
            AddBox(new Vector3(0f, 0.25f, 0f), new Vector3(1.7f, 0.5f, 2.1f));
            AddBox(new Vector3(0f, 0.15f, 1.0f), new Vector3(1.7f, 0.3f, 0.35f));
            AddBox(new Vector3(0f, 0.45f, -0.15f), new Vector3(1.6f, 0.8f, 1.4f), House);
            AddBox(new Vector3(-0.21f, 1.92f, 0.08f), new Vector3(1.18f, 0.06f, 1.14f), House);
        }

        HydraulicRam SizedRam(Transform a, Transform b, float r, System.Action<float> set, float lo, float hi)
        {
            float mn = float.MaxValue, mx = 0f;
            for (int i = 0; i <= 16; i++)
            {
                set(Mathf.Lerp(lo, hi, i / 16f));
                ApplyPose(false);
                float d = Vector3.Distance(a.position, b.position);
                mn = Mathf.Min(mn, d); mx = Mathf.Max(mx, d);
            }
            float barrel = Mathf.Max(mn * 0.92f, mx * 0.5f + 0.03f);
            float rod = mx - barrel + 0.08f;
            var ram = HydraulicRam.Make(a, b, r, barrel, r * 0.55f, rod, EquipModels.Yellow);
            return ram;
        }

        static Vector3 V3(Vector2 zy) => new Vector3(0f, zy.y, zy.x);

        /// <summary>Rotation whose local Z follows d (a (z, y) direction) and local X stays world X.</summary>
        static Quaternion YZRot(Vector2 d)
        {
            Vector3 f = new Vector3(0f, d.y, d.x).normalized;
            return Quaternion.LookRotation(f, Vector3.Cross(f, Vector3.right));
        }

        /// <summary>Tapered beam between two (z, y) points, width w along X.</summary>
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

        static void Hose(MeshBuilder b, int sub, float x, Vector2 p, Vector2 q, float r)
        {
            Vector2 d = q - p;
            b.AddRod(new Vector3(x, (p.y + q.y) * 0.5f, (p.x + q.x) * 0.5f), YZRot(d), r, d.magnitude, 6, sub);
        }

        static void Boss(MeshBuilder b, int sub, Vector3 c, float r, float w) => b.AddCylinder(c, Quaternion.Euler(0, 0, 90), r, w, 12, sub);

        /// <summary>Convex arc slab (counterweight / hood) seen from above: circle radius r cut by the chord z = zChord, y0..y1.</summary>
        static void TailSlab(MeshBuilder b, int sub, float r, float zChord, float y0, float y1)
        {
            float t0 = Mathf.Asin(Mathf.Clamp(-zChord / r, -1f, 1f));
            const int n = 14;
            var poly = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = Mathf.Lerp(t0, Mathf.PI - t0, i / (float)n);
                poly[i] = new Vector2(r * Mathf.Cos(t), -r * Mathf.Sin(t));
            }
            b.AddExtrude(poly, new Vector3(0f, (y0 + y1) * 0.5f, 0f), Quaternion.Euler(90f, 0f, 0f), y1 - y0, sub);
        }

        void BuildUndercarriage(MeshBuilder b, CTool.Mats m)
        {
            int frame = EquipModels.PaintG(m), blk = EquipModels.Black(m), steel = EquipModels.Chrome(m), y = EquipModels.PaintY(m);
            foreach (float x in new[] { -0.70f, 0.70f }) EquipModels.Track(b, m, x, 1.65f, 0.45f, 0.30f);
            b.AddBox(new Vector3(0f, 0.33f, 0f), new Vector3(1.0f, 0.22f, 1.25f), frame);                 // centre frame
            foreach (float z in new[] { -0.48f, 0.42f }) b.AddBox(new Vector3(0f, 0.30f, z), new Vector3(1.24f, 0.16f, 0.24f), frame);
            b.AddBox(new Vector3(0f, 0.30f, 0.66f), new Vector3(0.80f, 0.20f, 0.20f), frame);              // blade mount
            foreach (float x in new[] { -0.70f, 0.70f })
            {
                b.AddBox(new Vector3(x, 0.40f, 0f), new Vector3(0.10f, 0.05f, 1.55f), y);                  // track frame top cover
                b.AddCylinder(new Vector3(x - Mathf.Sign(x) * 0.22f, 0.225f, -0.825f), Quaternion.Euler(0, 0, 90), 0.13f, 0.16f, 14, blk); // travel motor
            }
            b.AddCylinder(new Vector3(0f, 0.48f, 0f), 0.46f, 0.06f, 28, blk);                              // slew bearing
            b.AddCylinder(new Vector3(0f, 0.525f, 0f), 0.42f, 0.05f, 28, steel);
            b.AddCylinder(new Vector3(0f, 0.43f, 0f), 0.10f, 0.08f, 12, steel);                            // swivel joint
            Boss(b, steel, new Vector3(0f, 0.30f, 0.70f), 0.05f, 0.86f);                                   // blade pivot pin
            Boss(b, steel, new Vector3(0f, 0.47f, 0.60f), 0.045f, 0.14f);
            b.AddBox(new Vector3(0f, 0.43f, 0.60f), new Vector3(0.14f, 0.12f, 0.10f), frame);             // blade ram lug
        }

        void BuildBlade(MeshBuilder b, CTool.Mats m)
        {
            int p = EquipModels.PaintY(m), steel = EquipModels.Chrome(m), blk = EquipModels.Black(m);
            // two push arms from the pivot (local origin) to the moldboard, plus a cross tube
            foreach (float x in new[] { -0.36f, 0.36f })
            {
                Beam(b, p, x, 0.08f, new Vector2(0f, 0f), new Vector2(0.52f, -0.10f), 0.07f, 0.10f);
                Boss(b, p, new Vector3(x, 0, 0), 0.07f, 0.09f);
            }
            b.AddBox(new Vector3(0f, 0.05f, 0.38f), new Vector3(0.80f, 0.10f, 0.12f), p);
            b.AddBox(new Vector3(0f, 0.10f, 0.40f), new Vector3(0.14f, 0.10f, 0.10f), p);                 // ram lug
            Boss(b, steel, new Vector3(0f, 0.14f, 0.40f), 0.04f, 0.15f);
            // curved moldboard: three facets, bottom edge at y -0.30 (ground)
            b.AddBox(new Vector3(0f, -0.24f, 0.585f), new Vector3(1.70f, 0.11f, 0.025f), Quaternion.Euler(12f, 0, 0), p);
            b.AddBox(new Vector3(0f, -0.13f, 0.580f), new Vector3(1.70f, 0.12f, 0.025f), p);
            b.AddBox(new Vector3(0f, -0.02f, 0.595f), new Vector3(1.70f, 0.11f, 0.025f), Quaternion.Euler(-18f, 0, 0), p);
            b.AddBox(new Vector3(0f, 0.035f, 0.62f), new Vector3(1.70f, 0.03f, 0.06f), p);                // top lip
            b.AddBox(new Vector3(0f, -0.29f, 0.60f), new Vector3(1.70f, 0.025f, 0.07f), steel);           // cutting edge
            foreach (float x in new[] { -0.84f, 0.84f }) b.AddBox(new Vector3(x, -0.13f, 0.55f), new Vector3(0.02f, 0.33f, 0.12f), p);
            for (int i = -3; i <= 3; i++) b.AddBox(new Vector3(i * 0.24f, -0.13f, 0.535f), new Vector3(0.02f, 0.30f, 0.07f), p);   // ribs
            for (int i = -1; i <= 1; i += 2)                                                             // black/yellow chevrons
                b.AddBox(new Vector3(i * 0.72f, -0.06f, 0.613f), new Vector3(0.16f, 0.06f, 0.004f), blk);
        }

        void BuildHouse(MeshBuilder b, CTool.Mats m)
        {
            int y = EquipModels.PaintY(m), g = EquipModels.PaintG(m), blk = EquipModels.Black(m), steel = EquipModels.Chrome(m);
            int glass = EquipModels.Glass(m), red = m.K(SurfaceKind.PaintedSteel, new Color(0.75f, 0.08f, 0.06f));
            int amber = m.K(SurfaceKind.PaintedSteel, EquipModels.Orange), white = m.K(SurfaceKind.PaintedSteel, EquipModels.White);
            int mat = m.K(SurfaceKind.DarkPlastic);

            // revolving frame / deck
            b.AddBox(new Vector3(0f, 0.08f, 0.10f), new Vector3(1.62f, 0.12f, 1.20f), g);
            // counterweight (gray) + engine hood (yellow), minimal tail swing r 0.87
            TailSlab(b, g, 0.87f, -0.36f, 0.02f, 0.60f);
            TailSlab(b, y, 0.85f, -0.32f, 0.60f, 0.98f);
            b.AddBox(new Vector3(0f, 0.57f, -0.42f), new Vector3(1.50f, 0.82f, 0.20f), y);               // engine bulkhead behind the seat
            for (int i = 0; i < 6; i++) b.AddBox(new Vector3(-0.30f + i * 0.12f, 0.985f, -0.62f), new Vector3(0.07f, 0.01f, 0.30f), blk);   // hood louvres
            b.AddCylinder(new Vector3(0.45f, 1.08f, -0.55f), 0.035f, 0.22f, 10, blk);                    // exhaust stack
            b.AddCylinder(new Vector3(-0.50f, 0.72f, -0.74f), 0.05f, 0.03f, 10, red);                    // tail lights
            b.AddCylinder(new Vector3(0.50f, 0.72f, -0.74f), 0.05f, 0.03f, 10, red);
            // counterweight stripe decal + model plate
            b.AddBox(new Vector3(0f, 0.68f, -0.835f), new Vector3(0.70f, 0.06f, 0.01f), blk);
            b.AddBox(new Vector3(0f, 0.68f, -0.84f), new Vector3(0.40f, 0.035f, 0.01f), white);

            // right side compartment: hydraulic tank, fuel tank, valve bank
            b.AddBevelBox(new Vector3(0.58f, 0.50f, 0.05f), new Vector3(0.44f, 0.70f, 0.80f), Quaternion.identity, 0.04f, y);
            b.AddBox(new Vector3(0.805f, 0.55f, 0.05f), new Vector3(0.01f, 0.14f, 0.50f), blk);          // decal band
            b.AddBox(new Vector3(0.806f, 0.36f, 0.30f), new Vector3(0.01f, 0.08f, 0.12f), white);        // warning label
            b.AddCylinder(new Vector3(0.62f, 0.88f, -0.12f), 0.045f, 0.04f, 10, blk);                     // fuel cap
            b.AddBox(new Vector3(0.80f, 0.55f, 0.30f), new Vector3(0.02f, 0.03f, 0.10f), blk);           // latch

            // operator platform, consoles, controls
            b.AddBox(new Vector3(-0.20f, 0.155f, 0.20f), new Vector3(1.05f, 0.03f, 0.95f), mat);
            b.AddBox(new Vector3(-0.18f, 0.30f, -0.05f), new Vector3(0.40f, 0.28f, 0.40f), g);           // seat pedestal
            EquipModels.Seat(b, m, new Vector3(-0.18f, 0.55f, -0.02f));
            foreach (float x in new[] { -0.50f, 0.14f })
            {
                b.AddBevelBox(new Vector3(x, 0.52f, 0.02f), new Vector3(0.14f, 0.14f, 0.50f), Quaternion.identity, 0.02f, blk);
                b.AddBox(new Vector3(x, 0.38f, 0.02f), new Vector3(0.10f, 0.20f, 0.10f), g);
                EquipModels.Joystick(b, m, new Vector3(x, 0.74f, 0.14f));
            }
            b.AddBox(new Vector3(0.14f, 0.60f, -0.12f), new Vector3(0.10f, 0.02f, 0.14f), blk);          // throttle / blade lever pad
            b.AddCylinder(new Vector3(0.17f, 0.66f, -0.12f), 0.01f, 0.12f, 6, blk);
            for (int i = 0; i < 2; i++)                                                                  // travel levers + pedals
            {
                float x = -0.27f + i * 0.18f;
                b.AddBox(new Vector3(x, 0.22f, 0.52f), new Vector3(0.10f, 0.10f, 0.08f), g);
                b.AddRod(new Vector3(x, 0.48f, 0.47f), Quaternion.Euler(-80f, 0, 0), 0.012f, 0.50f, 6, blk);
                b.AddBox(new Vector3(x, 0.73f, 0.42f), new Vector3(0.09f, 0.04f, 0.04f), blk);
                b.AddBox(new Vector3(x, 0.20f, 0.40f), new Vector3(0.10f, 0.02f, 0.16f), Quaternion.Euler(-25f, 0, 0), blk);
            }
            b.AddBox(new Vector3(0.20f, 0.62f, 0.50f), new Vector3(0.18f, 0.12f, 0.03f), Quaternion.Euler(-30f, 0, 0), blk);   // monitor
            b.AddBox(new Vector3(0.20f, 0.62f, 0.512f), new Vector3(0.14f, 0.08f, 0.005f), Quaternion.Euler(-30f, 0, 0), glass);
            b.AddBox(new Vector3(-0.55f, 0.13f, 0.62f), new Vector3(0.30f, 0.03f, 0.18f), steel);        // entry step

            // ROPS / TOPS 4-post canopy, roof top 1.95 (2.50 m above ground)
            float[] px = { -0.74f, 0.32f }; float[] pz = { -0.40f, 0.60f };
            foreach (float x in px)
            {
                b.AddBox(new Vector3(x, 1.03f, pz[0]), new Vector3(0.07f, 1.74f, 0.07f), blk);
                b.AddBox(new Vector3(x, 1.03f, pz[1] - 0.04f), new Vector3(0.06f, 1.74f, 0.06f), Quaternion.Euler(-2.5f, 0, 0), blk);
                b.AddBox(new Vector3(x, 0.20f, 0.10f), new Vector3(0.08f, 0.06f, 1.06f), g);             // floor rails
            }
            b.AddBevelBox(new Vector3(-0.21f, 1.92f, 0.08f), new Vector3(1.18f, 0.06f, 1.14f), Quaternion.identity, 0.02f, blk);
            b.AddBox(new Vector3(-0.21f, 1.88f, 0.08f), new Vector3(1.10f, 0.03f, 1.04f), g);
            b.AddBox(new Vector3(-0.21f, 1.86f, -0.40f), new Vector3(1.06f, 0.06f, 0.06f), blk);         // rear header
            b.AddBox(new Vector3(-0.21f, 1.86f, 0.58f), new Vector3(1.06f, 0.06f, 0.06f), blk);          // front header
            for (int i = 0; i < 3; i++) b.AddBox(new Vector3(-0.21f, 1.05f + i * 0.22f, -0.40f), new Vector3(1.0f, 0.03f, 0.03f), blk);   // rear guard bars
            b.AddRod(new Vector3(-0.74f, 1.05f, 0.62f), Quaternion.Euler(-90f, 0, 0), 0.016f, 0.62f, 8, steel);   // grab handle
            b.AddBox(new Vector3(-0.74f, 0.75f, 0.60f), new Vector3(0.03f, 0.03f, 0.06f), steel);
            b.AddBox(new Vector3(-0.74f, 1.36f, 0.60f), new Vector3(0.03f, 0.03f, 0.06f), steel);
            // work light and beacon on the roof, mirror on the post
            b.AddBox(new Vector3(0.20f, 1.98f, 0.60f), new Vector3(0.14f, 0.10f, 0.08f), blk);
            b.AddBox(new Vector3(0.20f, 1.98f, 0.645f), new Vector3(0.11f, 0.07f, 0.01f), glass);
            b.AddCylinder(new Vector3(-0.60f, 2.00f, -0.30f), 0.06f, 0.10f, 12, amber);
            b.AddRod(new Vector3(-0.86f, 1.55f, 0.58f), Quaternion.Euler(0, -90f, 0), 0.01f, 0.24f, 6, blk);
            b.AddBox(new Vector3(-0.98f, 1.55f, 0.58f), new Vector3(0.14f, 0.18f, 0.02f), blk);
            b.AddBox(new Vector3(-0.98f, 1.55f, 0.591f), new Vector3(0.12f, 0.16f, 0.004f), glass);

            // boom swing bracket at the front (offset right), boom foot + boom ram base pins
            b.AddBox(new Vector3(BoomX, 0.26f, 0.78f), new Vector3(0.36f, 0.40f, 0.34f), y);
            foreach (float x in new[] { BoomX - 0.15f, BoomX + 0.15f })
            {
                Beam(b, y, x, 0.04f, new Vector2(0.70f, 0.30f), new Vector2(FootZ, FootY), 0.10f, 0.09f);
                Beam(b, y, x, 0.04f, new Vector2(0.80f, 0.12f), new Vector2(0.92f, 0.07f), 0.10f, 0.07f);
            }
            Boss(b, steel, new Vector3(BoomX, FootY, FootZ), 0.045f, 0.36f);
            Boss(b, steel, new Vector3(BoomX, 0.07f, 0.92f), 0.045f, 0.36f);
            b.AddCylinder(new Vector3(BoomX, 0.15f, 0.60f), 0.08f, 0.30f, 12, steel);                    // swing (offset) king pin
            // hydraulic lines from the valve bank to the boom foot
            for (int i = 0; i < 4; i++)
                Hose(b, blk, BoomX - 0.06f + i * 0.04f, new Vector2(0.45f, 0.75f), new Vector2(FootZ - 0.02f, FootY + 0.16f), 0.012f);
        }

        void BuildBoom(MeshBuilder b, CTool.Mats m)
        {
            int y = EquipModels.PaintY(m), steel = EquipModels.Chrome(m), blk = EquipModels.Black(m);
            Vector2 foot = Vector2.zero, bend = new Vector2(1.10f, 0.36f), tip = new Vector2(L1, 0f);
            Beam(b, y, 0f, 0.20f, foot, bend, 0.12f, 0.16f);
            Beam(b, y, 0f, 0.20f, bend, tip - new Vector2(0.10f, 0f), 0.16f, 0.10f);
            Boss(b, y, new Vector3(0f, bend.y, bend.x), 0.16f, 0.20f);
            Boss(b, y, Vector3.zero, 0.11f, 0.22f);
            foreach (float x in new[] { -0.105f, 0.105f })                                             // tip fork cheeks
                Beam(b, y, x, 0.03f, new Vector2(L1 - 0.30f, 0.04f), tip, 0.10f, 0.09f);
            Boss(b, steel, new Vector3(0f, 0f, L1), 0.045f, 0.28f);
            Boss(b, steel, Vector3.zero, 0.045f, 0.26f);
            // ram lugs: boom ram underneath, stick ram on top
            foreach (float x in new[] { -0.07f, 0.07f })
            {
                Beam(b, y, x, 0.03f, new Vector2(0.85f, 0.25f), new Vector2(0.95f, 0.15f), 0.07f, 0.06f);
                Beam(b, y, x, 0.03f, new Vector2(0.85f, 0.48f), new Vector2(0.95f, 0.59f), 0.07f, 0.06f);
            }
            Boss(b, steel, new Vector3(0f, 0.15f, 0.95f), 0.04f, 0.18f);
            Boss(b, steel, new Vector3(0f, 0.59f, 0.95f), 0.04f, 0.18f);
            // hydraulic lines along the top of the boom to the stick and bucket rams
            Vector2 n1 = new Vector2(-0.36f, 1.10f).normalized * 0.15f, n2 = new Vector2(0.36f, 1.35f).normalized * 0.13f;
            foreach (float x in new[] { -0.06f, 0.06f })
            {
                Hose(b, steel, x, foot + n1 + new Vector2(0.15f, 0f), bend + new Vector2(-0.05f, 0.17f), 0.011f);
                Hose(b, steel, x, bend + new Vector2(0.05f, 0.17f), tip + n2 + new Vector2(-0.35f, 0f), 0.011f);
                Hose(b, blk, x, tip + n2 + new Vector2(-0.35f, 0f), tip + new Vector2(0.10f, 0.22f), 0.014f);
            }
            b.AddBox(new Vector3(0.101f, 0.30f, 0.80f), new Vector3(0.004f, 0.08f, 0.40f), blk);       // boom decal
        }

        void BuildStick(MeshBuilder b, CTool.Mats m)
        {
            int y = EquipModels.PaintY(m), steel = EquipModels.Chrome(m), blk = EquipModels.Black(m);
            Beam(b, y, 0f, 0.15f, new Vector2(-0.26f, 0.27f), new Vector2(0.18f, 0.02f), 0.09f, 0.12f);
            Beam(b, y, 0f, 0.15f, new Vector2(0f, 0.02f), new Vector2(L2, 0f), 0.13f, 0.07f);
            Boss(b, y, Vector3.zero, 0.09f, 0.18f);
            Boss(b, y, new Vector3(0f, 0.27f, -0.26f), 0.08f, 0.16f);
            Boss(b, steel, new Vector3(0f, 0.27f, -0.26f), 0.04f, 0.22f);
            Boss(b, y, new Vector3(0f, 0f, L2), 0.07f, 0.30f);                                           // bucket pin boss
            Boss(b, steel, new Vector3(0f, 0f, L2), 0.035f, 0.38f);
            Boss(b, y, V3(RockerPivot), 0.06f, 0.18f);
            foreach (float x in new[] { -0.05f, 0.05f })                                               // bucket ram base lug
                Beam(b, y, x, 0.03f, new Vector2(0.05f, 0.12f), new Vector2(0.12f, 0.25f), 0.06f, 0.05f);
            Boss(b, steel, new Vector3(0f, 0.25f, 0.12f), 0.035f, 0.14f);
            Hose(b, blk, 0.06f, new Vector2(-0.10f, 0.22f), new Vector2(0.10f, 0.30f), 0.013f);
            Hose(b, blk, -0.06f, new Vector2(-0.10f, 0.22f), new Vector2(0.10f, 0.30f), 0.013f);
            b.AddBox(new Vector3(0.076f, 0.03f, 0.70f), new Vector3(0.004f, 0.06f, 0.30f), blk);       // stick decal
        }

        void BuildBucket(MeshBuilder b, CTool.Mats m)
        {
            int y = EquipModels.PaintY(m), steel = EquipModels.Chrome(m), blk = EquipModels.Black(m);
            const float w = 0.61f;
            // side plates (D shape) and the wrapped shell
            foreach (float x in new[] { -w * 0.5f + 0.006f, w * 0.5f - 0.006f }) EquipModels.SideProfile(b, x, 0.012f, y, Shell);
            for (int i = 0; i < Shell.Length - 1; i++) Plate(b, y, 0f, w - 0.02f, Shell[i], Shell[i + 1], 0.012f);
            Plate(b, steel, 0f, w - 0.02f, Shell[4], Shell[5], 0.02f);                                   // wear plate / cutting edge base
            Plate(b, steel, 0f, w + 0.01f, new Vector2(0.06f, -0.30f), new Vector2(0.14f, -0.30f), 0.03f);   // rear lip
            // ears (two pairs: main pin and link pin)
            foreach (float x in new[] { -0.17f, 0.17f })
                EquipModels.SideProfile(b, x, 0.025f, y, new Vector2(-0.07f, -0.06f), new Vector2(-0.08f, 0.30f), new Vector2(0.20f, 0.30f), new Vector2(0.20f, -0.06f));
            Boss(b, steel, Vector3.zero, 0.04f, 0.40f);
            Boss(b, steel, V3(BucketLinkPin), 0.035f, 0.38f);
            // wear strips on the back
            foreach (float x in new[] { -0.18f, 0.0f, 0.18f }) Plate(b, steel, x, 0.04f, new Vector2(0.42f, 0.285f), new Vector2(0.66f, 0.135f), 0.012f);
            // 5 teeth along the cutting edge
            Vector2 dir = (Shell[5] - Shell[4]).normalized;
            Quaternion tr = YZRot(dir);
            for (int i = -2; i <= 2; i++)
            {
                float x = i * 0.125f;
                Vector2 a = Shell[5] + dir * 0.02f, t = Shell[5] + dir * 0.07f;
                b.AddBox(new Vector3(x, a.y, a.x), new Vector3(0.055f, 0.045f, 0.08f), tr, blk);
                b.AddBox(new Vector3(x, t.y, t.x), new Vector3(0.042f, 0.026f, 0.06f), tr, steel);
            }
            foreach (float x in new[] { -w * 0.5f, w * 0.5f })                                         // side cutters
                b.AddBox(new Vector3(x, Shell[5].y + 0.05f, Shell[5].x - 0.05f), new Vector3(0.02f, 0.10f, 0.14f), YZRot(Shell[5] - Shell[4]), steel);
        }

        // ================================================================== pose

        void ApplyPose(bool aimRams = true)
        {
            if (House == null) return;
            House.localRotation = Quaternion.Euler(0f, SwingDeg, 0f);
            Boom.localRotation = Quaternion.Euler(-BoomDeg, 0f, 0f);
            Stick.localRotation = Quaternion.Euler(StickDeg, 0f, 0f);
            Bucket.localRotation = Quaternion.Euler(BucketDeg, 0f, 0f);
            Blade.localRotation = Quaternion.Euler(Mathf.Lerp(BladeUpDeg, 0f, _bladeT), 0f, 0f);

            // four-bar bucket linkage in the stick plane (z, y)
            Vector2 S = new Vector2(L2, 0f);
            Vector2 Q = S + Rot(BucketLinkPin, -BucketDeg);
            Vector2 P = RockerPivot, dq = Q - P;
            float d = Mathf.Max(1e-4f, dq.magnitude);
            float a = (RockerLen * RockerLen - LinkLen * LinkLen + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, RockerLen * RockerLen - a * a));
            Vector2 mid = P + dq * (a / d), perp = new Vector2(-dq.y, dq.x) / d;
            Vector2 J1 = mid + perp * h, J2 = mid - perp * h;
            Vector2 J = J1.y >= J2.y ? J1 : J2;
            _rocker.localRotation = Quaternion.Euler(-Elev(J - P), 0f, 0f);
            _link.localPosition = V3(J);
            _link.localRotation = Quaternion.Euler(-Elev(Q - J), 0f, 0f);
            _bucketRamB.localPosition = V3(J);
            if (aimRams) foreach (var r in _rams) if (r != null) r.Aim();
        }

        static Vector2 Rot(Vector2 v, float elevDeg)
        {
            float e = elevDeg * Mathf.Deg2Rad, c = Mathf.Cos(e), s = Mathf.Sin(e);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        static float Elev(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;

        public override float[] Joints
        {
            get => new[] { SwingDeg, BoomDeg, StickDeg, BucketDeg, BladeDown ? 1f : 0f };
            set
            {
                if (value == null) return;
                if (value.Length > 0) SwingDeg = Mathf.Repeat(value[0], 360f);
                if (value.Length > 1) BoomDeg = Mathf.Clamp(value[1], BoomMin, BoomMax);
                if (value.Length > 2) StickDeg = Mathf.Clamp(value[2], StickMin, StickMax);
                if (value.Length > 3) BucketDeg = Mathf.Clamp(value[3], BucketMin, BucketMax);
                if (value.Length > 4) { BladeDown = value[4] > 0.5f; _bladeT = BladeDown ? 1f : 0f; }
                _swingVel = 0f;
                ApplyPose();
            }
        }

        /// <summary>Analytic IK: swings toward the point and solves boom + stick (+ bucket so the teeth point down / forward)
        /// so <see cref="BucketTip"/> lands on it. False (pose unchanged) when out of reach.</summary>
        public bool PoseTipAt(Vector3 world)
        {
            Vector3 rel = transform.InverseTransformPoint(world) - HousePivot;
            float R = Mathf.Sqrt(rel.x * rel.x + rel.z * rel.z);
            if (R < BoomX + 0.05f) return false;
            float swing = Mathf.Atan2(rel.x, rel.z) * Mathf.Rad2Deg - Mathf.Asin(BoomX / R) * Mathf.Rad2Deg;
            float f = Mathf.Sqrt(R * R - BoomX * BoomX);
            float u = f - FootZ, v = rel.y - FootY;
            float tl = new Vector2(TipLocal.z, TipLocal.y).magnitude;
            float g0 = Mathf.Atan2(TipLocal.y, TipLocal.z) * Mathf.Rad2Deg;
            float[] tries = { -90f, -80f, -100f, -70f, -110f, -60f, -120f, -50f, -130f, -40f, -140f, -30f, -20f, -10f, 0f, 10f };
            foreach (float phi in tries)
            {
                float pr = phi * Mathf.Deg2Rad;
                float pu = u - tl * Mathf.Cos(pr), pv = v - tl * Mathf.Sin(pr);
                float d = Mathf.Sqrt(pu * pu + pv * pv);
                if (d > L1 + L2 - 1e-3f || d < Mathf.Abs(L1 - L2) + 1e-3f) continue;
                float a = Mathf.Atan2(pv, pu) + Mathf.Acos(Mathf.Clamp((L1 * L1 + d * d - L2 * L2) / (2f * L1 * d), -1f, 1f));
                float ex = L1 * Mathf.Cos(a), ey = L1 * Mathf.Sin(a);
                float beta = Mathf.Atan2(pv - ey, pu - ex) * Mathf.Rad2Deg;
                float boom = a * Mathf.Rad2Deg;
                float stick = Mathf.DeltaAngle(0f, boom - beta);
                float bucket = Mathf.DeltaAngle(0f, beta + g0 - phi);
                if (boom < BoomMin || boom > BoomMax || stick < StickMin || stick > StickMax || bucket < BucketMin || bucket > BucketMax) continue;
                SwingDeg = Mathf.Repeat(swing, 360f); BoomDeg = boom; StickDeg = stick; BucketDeg = bucket; _swingVel = 0f;
                ApplyPose();
                return true;
            }
            return false;
        }

        // ================================================================== work

        /// <summary>Scripted dig: tip to the point, cut toward bottomY (≤ bucket room), curl. Returns m³ cut (bank).</summary>
        public float DigPass(Vector3 point, float bottomY)
        {
            if (!PoseTipAt(point)) return 0f;
            float room = BucketCapacityM3 - BucketLoadM3;
            float cut = Earth.Workable(point) ? Earth.Cut(point, 0.3f, bottomY, room) : 0f;
            BucketLoadM3 += cut; TotalDugM3 += cut;
            BucketDeg = Mathf.Min(BucketMax, BucketDeg + 75f);
            ApplyPose();
            RefreshDirt();
            return cut;
        }

        /// <summary>Dumps the whole load at the bucket (into the haul truck if over its bed, else a spoil pile). Returns m³ (bank).</summary>
        public float Dump()
        {
            float done = DumpAmount(BucketLoadM3);
            BucketDeg = Mathf.Max(BucketMin, Mathf.Min(BucketDeg, -30f));
            ApplyPose();
            RefreshDirt();
            return done;
        }

        float DumpAmount(float bank)
        {
            bank = Mathf.Min(bank, BucketLoadM3);
            if (bank <= 1e-6f) return 0f;
            Vector3 tip = BucketTip;
            float done;
            if (HaulTruck.I != null && HaulTruck.I.OverBed(tip))
                done = HaulTruck.I.Load(bank * Earth.SwellFactor) / Earth.SwellFactor;
            else
            {
                Vector3 p = tip; float h = Earth.Height(p);
                if (!float.IsNaN(h)) p.y = h;
                done = Earth.Deposit(p, bank * Earth.SwellFactor) / Earth.SwellFactor;
                if (done <= 0f && !Earth.Workable(p)) done = bank;     // off the lot: spoil just lands on the grass
            }
            done = Mathf.Min(done, BucketLoadM3);
            BucketLoadM3 = Mathf.Max(0f, BucketLoadM3 - done);
            if (BucketLoadM3 < 1e-4f) BucketLoadM3 = 0f;
            return done;
        }

        float GroundAt(Vector3 tip, out bool workable)
        {
            workable = Earth.Workable(tip);
            if (workable) return Earth.Height(tip);
            if (Physics.Raycast(new Vector3(tip.x, tip.y + 2f, tip.z), Vector3.down, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
                return hit.point.y;
            return float.NaN;
        }

        protected override void Operate(float dt, MachineControls c)
        {
            float sw0 = SwingDeg, b0 = BoomDeg, s0 = StickDeg, k0 = BucketDeg;
            Vector3 tipBefore = BucketTip;

            _swingVel = Mathf.MoveTowards(_swingVel, c.Swing * MaxSwingRate, SwingAccel * dt);
            SwingDeg = Mathf.Repeat(SwingDeg + _swingVel * dt, 360f);
            BoomDeg = Mathf.Clamp(BoomDeg + c.Boom * BoomRate * dt, BoomMin, BoomMax);
            StickDeg = Mathf.Clamp(StickDeg - c.Arm * StickRate * dt, StickMin, StickMax);
            BucketDeg = Mathf.Clamp(BucketDeg - c.Tool * BucketRate * dt, BucketMin, BucketMax);
            if (c.Toggle) { BladeDown = !BladeDown; if (Operating) Game.Toast(BladeDown ? "Blade down" : "Blade up"); }
            float bladeTarget = BladeDown ? 1f : 0f;
            bool bladeMoving = !Mathf.Approximately(_bladeT, bladeTarget);
            _bladeT = Mathf.MoveTowards(_bladeT, bladeTarget, dt / 1.2f);

            bool moved = Mathf.Abs(_swingVel) > 0.01f || c.Boom != 0 || c.Arm != 0 || c.Tool != 0 || bladeMoving;
            if (!moved) { RefreshDirt(); return; }
            ApplyPose();
            _hydraulicLoad = Mathf.Max(_hydraulicLoad, 0.55f);

            Vector3 tip = BucketTip;
            float gy = GroundAt(tip, out bool work);
            bool below = !float.IsNaN(gy) && tip.y < gy - 0.01f;
            bool deeper = tip.y < tipBefore.y - 1e-4f;
            if (below)
            {
                float room = BucketCapacityM3 - BucketLoadM3;
                bool scooping = c.Tool < 0 || c.Arm < 0;
                if (work && room > 1e-4f && scooping)
                {
                    float cut = Earth.Cut(tip, 0.3f, tip.y, Mathf.Min(room, BucketCapacityM3 * dt / 1.6f));
                    BucketLoadM3 += cut; TotalDugM3 += cut;
                    _hydraulicLoad = 1f;
                }
                else if (deeper && (!work || room <= 1e-4f || tip.y < gy - 0.35f))
                {
                    // solid ground / full bucket: the boom stalls instead of plunging the bucket deeper
                    SwingDeg = sw0; BoomDeg = b0; StickDeg = s0; BucketDeg = k0; _swingVel = 0f;
                    ApplyPose();
                    _hydraulicLoad = 1f;
                    if (Operating && room <= 1e-4f && Time.time > _dumpToastT) { _dumpToastT = Time.time + 4f; Game.Toast("Bucket full - curl, lift and dump"); }
                }
            }
            else if (BucketLoadM3 > 0f && c.Tool > 0 && (BucketDeg < 0f || MouthNormal.y < -0.75f))
            {
                DumpAmount(BucketCapacityM3 * dt / 0.7f);
                _hydraulicLoad = Mathf.Max(_hydraulicLoad, 0.7f);
            }
            RefreshDirt();
        }

        void RefreshDirt()
        {
            int level = BucketLoadM3 <= 0.002f ? 0 : Mathf.Clamp(Mathf.CeilToInt(BucketLoadM3 / BucketCapacityM3 * 4f), 1, 4);
            if (level == _dirtLevel || Bucket == null) return;
            _dirtLevel = level;
            if (_dirt != null) { var mf = _dirt.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh); Destroy(_dirt); _dirt = null; }
            if (level == 0) return;
            float s = Mathf.Sqrt(level / 4f);
            _dirt = CTool.Build("BucketDirt", (b, m) =>
            {
                int dirt = m.K(SurfaceKind.Dirt);
                Vector2 c = Vector2.zero; foreach (var p in Shell) c += p; c /= Shell.Length;
                Vector2 anchor = new Vector2(0.40f, 0.20f);
                var poly = new Vector2[Shell.Length];
                for (int i = 0; i < Shell.Length; i++)
                {
                    Vector2 inner = Vector2.Lerp(Shell[i], c, 0.06f);
                    poly[i] = anchor + (inner - anchor) * s;
                }
                EquipModels.SideProfile(b, 0f, 0.57f, dirt, poly);
                if (level >= 4)
                {
                    Vector2 L = Vector2.Lerp(Shell[0], c, 0.06f), T = Vector2.Lerp(Shell[5], c, 0.06f);
                    Vector2 mid = (L + T) * 0.5f + new Vector2(MouthNormalLocal.z, MouthNormalLocal.y) * 0.10f;
                    EquipModels.SideProfile(b, 0f, 0.52f, dirt, L, mid, T);
                }
            }, Bucket);
            _dirt.layer = 2;
        }

        protected override string Readout
        {
            get
            {
                const float cuft = 35.3147f;
                Vector3 tip = BucketTip;
                float depth = transform.position.y - tip.y;
                string s = $"Bucket: {BucketLoadM3 * cuft:0.0} / {BucketCapacityM3 * cuft:0.0} cu ft\n"
                         + $"Swing: {Mathf.RoundToInt(Mathf.DeltaAngle(0f, SwingDeg))} deg\n"
                         + (depth > 0.02f ? $"Tip depth: {Units.Format(depth)} below grade\n" : $"Tip height: {Units.Format(-depth)} above grade\n")
                         + $"Blade: {(BladeDown ? "down" : "up")}   Dug: {TotalDugM3 / Grade.CubicYard:0.0} cu yd";
                var t = HaulTruck.I;
                if (t != null) s += $"\nTruck: {t.LoadYd:0.0} / {HaulTruck.CapacityYd:0} cu yd · {t.Loads} loads hauled" + (t.Away ? " (away)" : "");
                return s;
            }
        }
    }
}
