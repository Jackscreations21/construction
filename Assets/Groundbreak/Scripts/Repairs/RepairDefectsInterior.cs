using System.Collections.Generic;
using UnityEngine;
using C = Groundbreak.RepairCatalog;

namespace Groundbreak
{
    // =====================================================================================================================
    /// <summary>Leak under the kitchen sink / bath vanity. Cause 0 = loose slip nut, 1 = cracked P-trap, 2 = leaking braided supply line.
    /// Frame: floor-level centre of the cabinet's open front, interior toward -Z.</summary>
    public class SinkLeakDefect : RepairDefect
    {
        public const int LooseNut = 0, CrackedTrap = 1, SupplyLine = 2;
        bool Kitchen => Spot == HouseSpot.KitchenSink;
        string TrapId => Kitchen ? C.Trap112 : C.Trap114;
        string TrapSize => Kitchen ? "1-1/2\"" : "1-1/4\"";
        bool CauseFixed => Cause == LooseNut ? Has("nutTight") || Has("trapNew") : Cause == CrackedTrap ? Has("trapNew") : Has("lineNew");
        bool Assembled => !Has("trapOff") && !Has("lineOff");
        /// <summary>Water is escaping right now (supply leaks under pressure, drain leaks show when the faucet runs).</summary>
        public bool Leaking => !CauseFixed && Assembled && !(Cause == SupplyLine && Has("stopClosed"));

        public override string Title => Kitchen ? "Leak under kitchen sink" : "Leak under bathroom vanity";
        public override string Symptom => Kitchen ? "There's water in the cabinet under my kitchen sink. Everything under there is soaked."
                                                  : "The bottom of the bathroom vanity is wet and it smells musty.";
        public override string Observation
        {
            get
            {
                if (IsFixed) return Has("dry") ? "Joints are dry, cabinet floor wiped out." : "No drip now. The cabinet floor is still wet.";
                if (Has("trapOff")) return "The trap is off. Tailpiece and trap arm are open.";
                if (Has("lineOff")) return "The cold supply line is off the stop.";
                return Has("tested") ? S.reading : "Puddle and water stains on the cabinet floor. Can't tell where it comes from until the joints are dried and the water is run.";
            }
        }
        public override bool IsFixed => CauseFixed && Assembled && !Has("stopClosed");
        public override float BookHours => Cause == LooseNut ? 0.5f : Cause == CrackedTrap ? 0.75f : 0.5f;
        protected override float SkipPenalty => (Has("verified") ? 0f : 0.15f);
        protected override Bounds ColliderBox => new Bounds(new Vector3(0, 0.45f, -0.25f), new Vector3(Kitchen ? 0.86f : 0.7f, 0.75f, 0.6f));
        protected override void FirstInit() { S.mess = 0.3f; }

        protected override void DefineActions()
        {
            Act("inspect", "Wipe every joint dry, run the faucet and watch for the first drip", null, 3f, null, () =>
            {
                Set("tested");
                if (!Assembled) return "Put it back together before running water.";
                if (Has("stopClosed") ) return "The stop is closed — no water at the faucet.";
                if (!Leaking) { Set("verified"); return "Ran it a full minute: every joint stays bone dry."; }
                return Cause == LooseNut ? "A bead forms at the slip nut where the tailpiece enters the trap. The nut turns by hand — it's loose."
                     : Cause == CrackedTrap ? "The nuts are dry. Water weeps from a hairline crack in the bottom of the J-bend."
                     : "The drain is dry even with the water running. The braided cold supply is wet at the crimp and drips without the faucet on.";
            }).Diag().Snd("water");
            Act("bucket", "Set a bucket under the trap", C.Bucket, 1f, () => !Has("bucket"), () => { Set("bucket"); return "Bucket is under the trap."; });
            Act("tighten", "Snug the slip nuts: hand tight plus a quarter turn", C.Pliers, 2f, () => Assembled && !Has("nutTight"), () =>
            {
                Set("nutTight"); Clear("verified");
                return Cause == LooseNut ? "The inlet nut took a full turn. Run water to check it." : "Both nuts were already snug. That wasn't it.";
            }).Snd("screw");
            Act("trap-off", "Loosen both slip nuts and drop the P-trap", C.Pliers, 3f, () => !Has("trapOff") && !Has("trapNew"), () =>
            {
                Set("trapOff");
                if (!Has("bucket")) { Dirty(0.3f); Lose(0.05f); return "The trap dumps a pint of grey water onto the cabinet floor. Should have set a bucket."; }
                return "Trap is off; its water went into the bucket.";
            }).Snd("screw");
            Act("trap-new", $"Fit a new {TrapSize} P-trap with fresh washers (bevel toward the joint)", C.Pliers, 4f, () => Has("trapOff"), () =>
            {
                Clear("trapOff"); Set("trapNew"); Clear("verified");
                if (Cause == SupplyLine) { S.wrongRepair = true; Lose(0.15f); return "New trap is on — but the old one wasn't leaking."; }
                return "New trap is on, nuts hand tight plus a quarter turn. Run water to check it.";
            }).Uses(TrapId).Snd("screw");
            Act("stop-close", "Close the cold angle stop (clockwise)", null, 1f, () => !Has("stopClosed"), () => { Set("stopClosed"); return "Stop closed."; });
            Act("stop-open", "Open the angle stop slowly", null, 1f, () => Has("stopClosed"), () =>
            {
                Clear("stopClosed"); Clear("verified");
                if (Has("lineOff")) { Dirty(0.5f); Lose(0.25f); return "Water blasts out of the open stop and floods the cabinet!"; }
                return "Stop is open.";
            }).Snd("water");
            Act("line-off", "Disconnect the braided cold supply at the stop and the faucet shank", C.Wrench, 3f, () => !Has("lineOff") && !Has("lineNew"), () =>
            {
                Set("lineOff");
                if (!Has("stopClosed")) { Dirty(0.5f); Lose(0.25f); Set("stopClosed"); return "Water sprays everywhere — the stop was still open! You get it shut, but the cabinet is flooded."; }
                return "Supply line is off.";
            }).Snd("screw");
            Act("line-new", "Thread on a new braided supply: hand tight, then a quarter turn with the wrench", C.Wrench, 3f, () => Has("lineOff"), () =>
            {
                Clear("lineOff"); Set("lineNew"); Clear("verified");
                if (Cause != SupplyLine) { S.wrongRepair = true; Lose(0.15f); return "New supply line is on — but the old one was fine."; }
                return "New supply line is on. Open the stop and check it.";
            }).Uses(C.Supply).Snd("screw");
            Act("dry", "Wipe out the cabinet and dry the floor", C.Bucket, 2f, () => !Has("dry") || S.mess > 0.01f, () =>
            {
                if (Leaking) return "It's still dripping — it will just get wet again.";
                Set("dry"); S.mess = 0f; return "Cabinet floor is dry.";
            });
        }

        Transform _drop; float _dropT;

        protected override void BuildModel(RM m)
        {
            float r = Kitchen ? 0.019f : 0.016f, fl = 0.115f;
            bool newTrap = Has("trapNew"), newLine = Has("lineNew");
            int pvc = m.Mat(SurfaceKind.PaintedWhite, newTrap ? Color.white : new Color(0.86f, 0.84f, 0.78f));
            int nut = m.Mat(SurfaceKind.PaintedWhite, new Color(0.8f, 0.8f, 0.78f));
            int chrome = m.Mat(SurfaceKind.Nickel), braid = m.Mat(SurfaceKind.BrushedSteel), water = m.Mat(new Color(0.45f, 0.6f, 0.72f));
            int stain = m.Mat(new Color(0.3f, 0.24f, 0.17f)), dark = m.Mat(SurfaceKind.DarkPlastic);
            var qx = Quaternion.Euler(90, 0, 0);

            // sink bowl underside + tailpiece
            m.Box(new Vector3(0, 0.72f, -0.30f), new Vector3(Kitchen ? 0.5f : 0.36f, 0.16f, 0.36f), m.Mat(Kitchen ? SurfaceKind.BrushedSteel : SurfaceKind.Porcelain));
            m.Cyl(new Vector3(0, 0.53f, -0.30f), r, 0.22f, pvc);
            // trap arm into the wall
            m.Cyl(new Vector3(0, 0.425f, -0.42f), r, 0.05f, pvc);
            m.Tube(new Vector3(0, 0.45f, -0.42f), new Vector3(0, 0.455f, -0.60f), r, pvc);
            m.Cyl(new Vector3(0, 0.455f, -0.585f), qx, r + 0.012f, 0.012f, chrome);
            if (!Has("trapOff"))
            {
                var pts = new List<Vector3>();
                for (int i = 0; i <= 8; i++) { float t = Mathf.PI * i / 8f; pts.Add(new Vector3(0, 0.40f - 0.06f * Mathf.Sin(t), -0.36f + 0.06f * Mathf.Cos(t))); }
                m.Path(pts, r, pvc);
                m.Cyl(new Vector3(0, 0.41f, -0.30f), r + 0.008f, 0.024f, nut, 8);
                m.Cyl(new Vector3(0, 0.41f, -0.42f), r + 0.008f, 0.024f, nut, 8);
                if (Cause == CrackedTrap && !newTrap && Has("tested")) m.Box(new Vector3(0, 0.3405f, -0.36f), new Vector3(0.003f, 0.012f, 0.05f), dark);
            }
            else   // old trap lies on the cabinet floor
                m.Tube(new Vector3(-0.28f, fl + r, -0.2f), new Vector3(-0.16f, fl + r, -0.24f), r, pvc);
            // angle stops and supplies (hot left, cold right)
            foreach (float sx in new[] { -0.1f, 0.1f })
            {
                m.Cyl(new Vector3(sx, 0.40f, -0.585f), qx, 0.012f, 0.05f, chrome);
                bool closed = sx > 0 && Has("stopClosed");
                m.Box(new Vector3(sx, 0.40f, -0.555f), closed ? new Vector3(0.012f, 0.034f, 0.008f) : new Vector3(0.034f, 0.012f, 0.008f), chrome);
                if (sx > 0 && Has("lineOff")) continue;
                int bm = sx > 0 && newLine ? m.Mat(SurfaceKind.Nickel) : braid;
                m.Path(new[] { new Vector3(sx, 0.415f, -0.58f), new Vector3(sx, 0.52f, -0.56f), new Vector3(sx * 0.6f, 0.66f, -0.5f), new Vector3(sx * 0.4f, 0.8f, -0.47f) }, 0.006f, bm);
            }
            // water
            Vector3 lp = LeakPoint;
            if (!Has("dry"))
            {
                m.Disc(new Vector3(lp.x - 0.03f, fl + 0.001f, lp.z + 0.06f), 0.2f, stain);
                m.Disc(new Vector3(lp.x, fl + 0.002f, lp.z + 0.05f), 0.11f + 0.1f * Mess, water);
            }
            if (Has("bucket"))
            {
                int bk = m.Mat(new Color(0.85f, 0.45f, 0.1f));
                m.L.B.AddFrustum(new Vector3(0, fl + 0.09f, -0.36f), Quaternion.identity, 0.1f, 0.125f, 0.18f, 14, bk);
                m.Disc(new Vector3(0, fl + 0.181f, -0.36f), 0.115f, Has("trapOff") || Has("trapNew") ? water : dark);
            }
            if (Leaking && Has("tested")) m.Cyl(lp + Vector3.down * 0.012f, 0.005f, 0.012f, water, 6);
        }

        Vector3 LeakPoint => Cause == LooseNut ? new Vector3(0, 0.40f, -0.30f) : Cause == CrackedTrap ? new Vector3(0, 0.335f, -0.36f) : new Vector3(0.1f, 0.42f, -0.57f);

        protected override void StateChanged()
        {
            if (_drop == null)
            {
                var d = new RM(); d.Cyl(Vector3.zero, 0.004f, 0.014f, d.Mat(new Color(0.55f, 0.7f, 0.85f)), 6);
                _drop = d.L.Make(transform, "Drip").transform;
            }
            _drop.gameObject.SetActive(Leaking && Has("tested"));
        }

        void Update()
        {
            if (_drop == null || !_drop.gameObject.activeSelf) return;
            _dropT = (_dropT + Time.deltaTime * 1.4f) % 1f;
            Vector3 lp = LeakPoint;
            _drop.localPosition = new Vector3(lp.x, Mathf.Lerp(lp.y - 0.02f, 0.12f, _dropT * _dropT), lp.z);
        }
    }

    // =====================================================================================================================
    /// <summary>Dead receptacle. Cause 0 = tripped breaker, 1 = tripped GFCI upstream (kitchen counter), 2 = failed back-stabbed receptacle,
    /// 3 = failed breaker. Stations: "panel" (breaker panel) and, for the living-room outlet, "gfci" (kitchen counter GFCI).</summary>
    public class DeadOutletDefect : RepairDefect
    {
        public const int TrippedBreaker = 0, TrippedGfci = 1, FailedReceptacle = 2, FailedBreaker = 3;
        bool Kitchen => Spot == HouseSpot.OutletKitchen;
        string GfciStation => Kitchen ? null : "gfci";
        Transform _panel, _gfci;

        bool BreakerOn => !Has("tripped") && !Has("off");
        bool BreakerBad => Cause == FailedBreaker && !Has("brkNew");
        bool DeviceBad => Cause == FailedReceptacle && !Has("devNew");
        public bool PowerAtBreaker => !Has("mainOff") && BreakerOn && !BreakerBad;
        public bool PowerAtBox => PowerAtBreaker && (Kitchen || !Has("gfciTrip"));
        public bool PowerAtFace => PowerAtBox && !DeviceBad && !Has("devOut") && !(Kitchen && Has("gfciTrip"));

        public override string Title => Kitchen ? "Dead kitchen counter outlet" : "Dead living room outlet";
        public override string Symptom => Kitchen ? "The outlet on the kitchen counter quit. The coffee maker won't turn on."
                                                  : "The outlet by the couch is dead. Lamp doesn't work in it, but the lamp is fine.";
        public override string Observation => Has("devOut") ? "The receptacle hangs out of the box on its wires: black on brass, white on silver, bare on green."
            : IsFixed ? "The night light plugged into it glows." : "The night light plugged into it is dark."
              + (DeviceBad && Cause == FailedReceptacle ? " There's a faint brown scorch at one slot." : "");
        public override bool IsFixed => PowerAtFace;
        public override float BookHours => Cause == FailedReceptacle ? 0.75f : Cause == FailedBreaker ? 1f : 0.5f;
        protected override float SkipPenalty => Has("verified") ? 0f : 0.1f;
        protected override Bounds ColliderBox => new Bounds(new Vector3(0, 0, 0.04f), new Vector3(0.22f, 0.26f, 0.12f));

        protected override void FirstInit()
        {
            if (Cause == TrippedBreaker) Set("tripped");
            if (Cause == TrippedGfci) Set("gfciTrip");
        }

        protected override void DefineActions()
        {
            _panel = AddStation("panel", HouseSpot.BreakerPanel, new Bounds(new Vector3(0, 0, 0.05f), new Vector3(0.5f, 0.85f, 0.14f)));
            if (!Kitchen) _gfci = AddStation("gfci", HouseSpot.OutletKitchen, new Bounds(new Vector3(0, 0, 0.04f), new Vector3(0.2f, 0.24f, 0.12f)));

            Act("test", "Hold the voltage tester to the slots", C.Tester, 1.5f, null, () =>
            {
                if (Has("devOut"))
                {
                    if (PowerAtBox) return "Tester chirps on the black wire: the box is HOT.";
                    if (!BreakerOn || Has("mainOff")) Set("deadChecked");
                    return "No chirp on any wire. The box is dead.";
                }
                if (PowerAtFace) { Set("verified"); return "Tester chirps at the short slot: the receptacle is live."; }
                if (PowerAtBox) return "Nothing at the slots, but it chirps on the cable behind the plate: power reaches the box, the device isn't passing it.";
                if (!BreakerOn || Has("mainOff")) Set("deadChecked");
                return "No chirp anywhere: no power is reaching this box.";
            }).Diag().Snd("beeper");
            Act("look-panel", "Look down the rows for a handle that isn't fully ON", null, 1.5f, null, () =>
                Has("mainOff") ? "The main is OFF." : Has("tripped") ? "Breaker 8 (LIV/KIT RECEPT) sits in the middle, off its ON stop: tripped."
                : Has("off") ? "Breaker 8 is OFF." : "Every handle is fully ON.").At("panel").Diag();
            Act("brk-off", "Push breaker 8 fully OFF", null, 1f, () => !Has("off"), () => { Set("off"); Clear("tripped"); Clear("verified"); return "Breaker 8 is OFF."; }).At("panel");
            Act("brk-on", "Switch breaker 8 back ON", null, 1f, () => Has("off"), () =>
            {
                Clear("off");
                return BreakerBad ? "The handle holds ON, but it feels loose and has no snap." : "Breaker 8 snaps ON and holds.";
            }).At("panel");
            Act("meter", "Meter the breaker's load screw to the neutral bar", C.Meter, 2f, null, () =>
                PowerAtBreaker ? "120 V leaving breaker 8." : BreakerOn && !Has("mainOff") && BreakerBad ? "0 V on the load screw with the handle ON, 120 V on the bus stab: the breaker has failed."
                : "0 V — the breaker is not on.").At("panel").Diag().Snd("beeper");
            Act("main-off", "Switch the MAIN breaker off", null, 1f, () => !Has("mainOff"), () => { Set("mainOff"); Clear("verified"); return "Main is OFF. The whole house is dark."; }).At("panel");
            Act("main-on", "Switch the MAIN breaker on", null, 1f, () => Has("mainOff"), () => { Clear("mainOff"); return "Main is ON."; }).At("panel");
            Act("brk-new", "Pull the dead-front, unclip breaker 8, land the wire on a new 15 A breaker and seat it on the bus", C.Screwdriver, 6f, () => !Has("brkNew"), () =>
            {
                string zap = "";
                if (!Has("mainOff")) { Lose(0.4f); zap = "ZAP! The bus was live — you got bit. Always kill the main first. "; Sfx.Play("error", transform.position); }
                Set("brkNew"); Set("off"); Clear("tripped"); Clear("verified");
                if (Cause != FailedBreaker) { S.wrongRepair = true; Lose(0.15f); return zap + "New breaker is in (left OFF) — the old one tested fine, though."; }
                return zap + "New 15 A breaker is in, left OFF.";
            }).At("panel").Uses(C.Breaker15).Snd("screw");
            Act("gfci-reset", "Press RESET on the kitchen counter GFCI", null, 1f, null, () =>
            {
                if (!Has("gfciTrip")) return "The RESET button is already in. It wasn't tripped.";
                if (!PowerAtBreaker) return "RESET won't latch: no power to the GFCI.";
                Clear("gfciTrip"); Clear("verified");
                return "Click — RESET latches and the light goes green. It feeds the outlets downstream of it.";
            }).At(GfciStation).Diag();
            Act("pull", "Remove the cover plate and pull the receptacle out of the box", C.Screwdriver, 3f, () => !Has("devOut"), () => { Set("devOut"); Clear("verified"); return "Device is out of the box."; }).Snd("screw");
            Act("swap", Kitchen ? "Move the wires to a new GFCI: feed on LINE, hot-brass, neutral-silver, ground-green"
                                : "Move the wires to a new receptacle's side screws (no back-stabs): hot-brass, neutral-silver, ground-green", C.Screwdriver, 5f,
                () => Has("devOut") && !Has("devNew"), () =>
            {
                string zap = "";
                if (PowerAtBox) { Lose(0.4f); zap = "ZAP! The circuit was live. Breaker off and test before you touch a wire. "; Sfx.Play("error", transform.position); }
                else if (!Has("deadChecked")) { Lose(0.1f); zap = "(You never proved it dead with the tester.) "; }
                Set("devNew");
                if (Kitchen) Clear("gfciTrip");
                if (Cause != FailedReceptacle && !(Kitchen && Cause == TrippedGfci)) { S.wrongRepair = true; Lose(0.15f); return zap + "New device is wired — the old one wasn't the problem."; }
                return zap + "New device is wired on the screw terminals.";
            }).Uses(Kitchen ? C.Gfci : C.Recept).Snd("screw");
            Act("install", "Fold the wires back, screw the device to the box, fit the cover plate", C.Screwdriver, 3f, () => Has("devOut"), () => { Clear("devOut"); return "Device and plate are back on."; }).Snd("screw");
        }

        protected override void Restore() { base.Restore(); House.SetFixtureVisible(HouseSpot.BreakerPanel, true); }

        protected override void BuildModel(RM m)
        {
            House.SetFixtureVisible(Spot, false);
            House.SetFixtureVisible(HouseSpot.BreakerPanel, false);
            int white = m.Mat(SurfaceKind.PaintedWhite), dark = m.Mat(SurfaceKind.DarkPlastic), blue = m.Mat(new Color(0.15f, 0.3f, 0.7f));
            // ---- the receptacle
            if (Has("devOut"))
            {
                m.Box(new Vector3(0, 0, 0.001f), new Vector3(0.058f, 0.098f, 0.002f), blue);
                m.Box(new Vector3(0, 0, 0.002f), new Vector3(0.046f, 0.086f, 0.002f), dark);
                var tilt = Quaternion.Euler(-25, 0, 0);
                Vector3 dc = new Vector3(0, -0.02f, 0.075f);
                m.Box(dc, new Vector3(0.034f, 0.105f, 0.022f), tilt, m.Mat(SurfaceKind.PaintedWhite, Has("devNew") ? Color.white : new Color(0.9f, 0.87f, 0.78f)));
                m.Tube(new Vector3(0.012f, 0.02f, 0.002f), dc + new Vector3(0.018f, 0.01f, 0), 0.002f, dark);
                m.Tube(new Vector3(-0.012f, 0.02f, 0.002f), dc + new Vector3(-0.018f, 0.01f, 0), 0.002f, white);
                m.Tube(new Vector3(0, -0.03f, 0.002f), dc + new Vector3(0, -0.05f, 0), 0.0015f, m.Mat(new Color(0.75f, 0.45f, 0.2f)));
            }
            else Device(m, Kitchen, Kitchen && Has("gfciTrip"), PowerAtFace, DeviceBad);
            // ---- kitchen GFCI indicator (living-room job): light + popped button over the house's own plate
            if (_gfci != null)
            {
                m.Frame = FrameOf(_gfci);
                bool trip = Has("gfciTrip");
                m.Box(new Vector3(0, 0, 0.0075f), new Vector3(0.036f, 0.07f, 0.003f), white);
                m.Box(new Vector3(0, 0.006f, 0.009f + (trip ? 0.002f : 0f)), new Vector3(0.012f, 0.008f, 0.004f), m.Mat(new Color(0.75f, 0.1f, 0.1f)));
                m.Box(new Vector3(0, -0.006f, 0.009f), new Vector3(0.012f, 0.008f, 0.003f), dark);
                m.Box(new Vector3(0.012f, 0f, 0.0092f), new Vector3(0.004f, 0.004f, 0.002f), m.Mat(trip ? new Color(1f, 0.15f, 0.1f) : PowerAtBreaker ? new Color(0.2f, 0.95f, 0.3f) : new Color(0.2f, 0.2f, 0.2f)));
            }
            // ---- the panel, door open
            m.Frame = FrameOf(_panel);
            int gray = m.Mat(SurfaceKind.PaintedSteel, new Color(0.62f, 0.64f, 0.66f));
            m.Box(new Vector3(0, 0, 0.01f), new Vector3(0.356f, 0.762f, 0.02f), gray);
            m.Box(new Vector3(0, 0, 0.0205f), new Vector3(0.24f, 0.62f, 0.002f), dark);
            var dq = Quaternion.Euler(0, -100f, 0);
            m.Box(new Vector3(-0.178f, 0, 0.02f) + dq * new Vector3(0.178f, 0, 0), new Vector3(0.356f, 0.762f, 0.004f), dq, gray);
            bool mainOff = Has("mainOff");
            m.Box(new Vector3(0, 0.27f, 0.027f), new Vector3(0.075f, 0.05f, 0.012f), dark);
            m.Box(new Vector3(0, 0.27f + (mainOff ? -0.012f : 0.012f), 0.036f), new Vector3(0.03f, 0.014f, 0.01f), m.Mat(new Color(0.25f, 0.25f, 0.27f)));
            int handle = m.Mat(new Color(0.3f, 0.3f, 0.32f)), flag = m.Mat(new Color(1f, 0.45f, 0.05f)), label = m.Mat(new Color(0.9f, 0.9f, 0.85f));
            for (int row = 0; row < 8; row++)
                foreach (int side in new[] { -1, 1 })
                {
                    float y = 0.19f - row * 0.0265f, x = side * 0.05f;
                    bool ours = row == 3 && side == 1;
                    m.Box(new Vector3(x, y, 0.027f), new Vector3(0.078f, 0.024f, 0.012f), dark);
                    float hx = -side * 0.018f;                                    // ON = handle toward the centre
                    if (ours) hx = Has("off") ? side * 0.018f : Has("tripped") ? 0f : hx;
                    m.Box(new Vector3(x + hx, y, 0.036f), new Vector3(0.016f, 0.012f, 0.01f), ours && Has("brkNew") ? m.Mat(new Color(0.08f, 0.08f, 0.09f)) : handle);
                    if (ours && Has("tripped")) m.Box(new Vector3(x + side * 0.03f, y, 0.0335f), new Vector3(0.008f, 0.01f, 0.002f), flag);
                    m.Box(new Vector3(side * 0.105f, y, 0.0215f), new Vector3(0.026f, 0.018f, 0.001f), label);
                }
            m.Frame = Matrix4x4.identity;
        }

        static void Device(RM m, bool gfci, bool tripped, bool live, bool scorched)
        {
            int white = m.Mat(SurfaceKind.PaintedWhite), dark = m.Mat(SurfaceKind.DarkPlastic);
            m.Box(new Vector3(0, 0, 0.003f), new Vector3(0.07f, 0.114f, 0.006f), white);
            int face = m.Mat(SurfaceKind.PaintedWhite, new Color(0.95f, 0.94f, 0.9f));
            if (gfci) m.Box(new Vector3(0, 0, 0.0075f), new Vector3(0.036f, 0.07f, 0.003f), face);
            foreach (float oy in new[] { 0.019f, -0.019f })
            {
                float y = gfci ? oy * 1.25f : oy;
                if (!gfci) m.Box(new Vector3(0, y, 0.0075f), new Vector3(0.034f, 0.028f, 0.003f), face);
                m.Box(new Vector3(-0.006f, y + 0.003f, 0.0092f), new Vector3(0.002f, 0.009f, 0.001f), dark);
                m.Box(new Vector3(0.006f, y + 0.003f, 0.0092f), new Vector3(0.002f, 0.007f, 0.001f), dark);
                m.DiscZ(new Vector3(0, y - 0.008f, 0.0092f), 0.0025f, dark, 8);
            }
            if (gfci)
            {
                m.Box(new Vector3(0, 0.006f, 0.0095f + (tripped ? 0.002f : 0f)), new Vector3(0.012f, 0.007f, 0.003f), m.Mat(new Color(0.75f, 0.1f, 0.1f)));
                m.Box(new Vector3(0, -0.006f, 0.0095f), new Vector3(0.012f, 0.007f, 0.003f), dark);
                m.Box(new Vector3(0.012f, 0f, 0.0095f), new Vector3(0.004f, 0.004f, 0.002f), m.Mat(tripped ? new Color(1f, 0.15f, 0.1f) : live ? new Color(0.2f, 0.95f, 0.3f) : new Color(0.2f, 0.2f, 0.2f)));
            }
            if (scorched) m.DiscZ(new Vector3(0.008f, (gfci ? 0.024f : 0.019f) + 0.006f, 0.0094f), 0.007f, m.Mat(new Color(0.28f, 0.17f, 0.08f)), 10);
            // the customer's plug-in night light in the lower outlet
            float ly = gfci ? -0.024f : -0.019f;
            m.Box(new Vector3(0, ly, 0.02f), new Vector3(0.03f, 0.03f, 0.022f), white);
            m.Box(new Vector3(0, ly - 0.03f, 0.022f), new Vector3(0.036f, 0.04f, 0.02f), m.Mat(live ? new Color(1f, 0.9f, 0.45f) : new Color(0.55f, 0.55f, 0.52f)));
        }
    }

    // =====================================================================================================================
    /// <summary>Hole in drywall. Cause 0 = small doorknob hole (mesh patch), 1 = large hole (cut square, backing, patch panel, tape, 3 coats).
    /// Frame: on the wall face, +Z out of the wall.</summary>
    public class DrywallHoleDefect : RepairDefect
    {
        public const int Small = 0, Large = 1;
        bool Big => Cause == Large;
        int Coats => (Has("c1") ? 1 : 0) + (Has("c2") ? 1 : 0) + (Has("c3") ? 1 : 0);
        bool Based => Big ? Has("panel") : Has("mesh");
        Material _wall; bool _wallLooked;

        public override string Title => Big ? "Large hole in drywall" : "Doorknob hole in drywall";
        public override string Symptom => Big ? "My son put a hole in the wall moving a dresser. It's about the size of a dinner plate."
                                              : "The door handle punched a hole in the wall behind the door.";
        public override string Observation =>
            Has("painted") ? (Has("primed") ? "Patch is painted. You have to know where to look." : "Painted, but the patch flashes dull through the paint: no primer under it.")
            : Has("primed") ? "Patch is primed white, ready for paint."
            : Coats > 0 ? $"{Coats} coat(s) of compound on" + (Has("sanded") ? ", sanded smooth." : Drying ? $", still wet (about {Mathf.CeilToInt(DryMinutesLeft)} min)." : ", dry, ridges not sanded.")
            : Based ? (Big ? "Patch panel screwed in. Four open seams." : "Mesh patch is stuck over the hole.")
            : Big ? (Has("square") ? (Has("backing") ? "Square opening with 1x3 backing behind the edges." : "Hole cut back to a clean 12\" square. Nothing to screw a patch to yet.")
                                   : "Ragged hole about 10\" across with crushed gypsum and torn paper around it. Too big for a stick-on patch.")
                  : "Round hole about 3\" across at doorknob height, torn paper at the edge.";
        public override bool IsFixed => Has("painted") && Coats > 0;
        public override float BookHours => Big ? 3f : 1.5f;
        protected override float SkipPenalty => (Coats < (Big ? 3 : 2) ? 0.15f : 0f) + (Has("sanded") ? 0f : 0.2f);
        protected override Bounds ColliderBox => new Bounds(new Vector3(0, 0, 0.03f), new Vector3(0.9f, 0.9f, 0.08f));

        protected override void DefineActions()
        {
            if (!Big)
            {
                Act("trim", "Cut away the loose paper and crumbled gypsum", C.Knife, 2f, () => !Has("trimmed") && !Has("mesh"), () => { Set("trimmed"); return "Edges are clean and flat."; }).Snd("cut");
                Act("mesh", "Stick a 6\" self-adhesive mesh patch over the hole", null, 2f, () => !Has("mesh"), () =>
                {
                    Set("mesh");
                    if (!Has("trimmed")) { Lose(0.1f); return "Patch is on, but it rides up on the torn paper. It will telegraph through the mud."; }
                    return "Patch is on flat.";
                }).Uses(C.MeshPatch);
            }
            else
            {
                Act("square", "Mark a 12\" square around the damage and cut it out with the jab saw", C.JabSaw, 4f, () => !Has("square"), () => { Set("square"); return "Clean 12\" x 12\" opening."; }).Snd("cut");
                Act("backing", "Screw 1x3 backing strips behind two edges of the opening", C.Drill, 3f, () => Has("square") && !Has("backing") && !Has("panel"), () => { Set("backing"); return "Backing strips are in, half behind the old board."; })
                    .Uses(C.Furring).Uses(C.DwScrews, 4).Snd("drill");
                Act("panel", "Cut a 12\" piece from a patch panel and screw it to the backing, heads just dimpled", C.Drill, 3f, () => Has("square") && !Has("panel"), () =>
                {
                    Set("panel");
                    if (!Has("backing")) { Lose(0.3f); return "With no backing the patch just floats in the hole on its edges. It will crack out."; }
                    return "Patch is flush with the wall, 8 screws.";
                }).Uses(C.PatchPanel).Uses(C.DwScrews, 8).Snd("drill");
            }
            Act("c1", Big ? "Bed paper tape in compound over the four seams, wipe it tight" : "First coat of compound through the mesh", C.Knives, 3f,
                () => Based && !Has("c1"), () => { Set("c1"); Clear("sanded"); return "First coat is on. Let it dry."; }).Uses(C.Compound).Uses(C.DwTape, Cause == Large ? 5 : 0).Wait(40f).Snd("scrape");
            Act("c2", "Second coat, feathered 2\" wider each side", C.Knives, 3f, () => Has("c1") && !Has("c2") && !Has("primed"), () => { Set("c2"); Clear("sanded"); return "Second coat is on."; }).Dry().Wait(40f).Snd("scrape");
            Act("c3", "Thin finish coat with the wide knife", C.Knives, 3f, () => Has("c2") && !Has("c3") && !Has("primed"), () => { Set("c3"); Clear("sanded"); return "Finish coat is on."; }).Dry().Wait(40f).Snd("scrape");
            Act("sand", "Sand smooth and feather the edges; check with a raking light", C.Sander, 3f, () => Coats > 0 && !Has("sanded") && !Has("primed"), () => { Set("sanded"); Dirty(0.1f); return "Smooth to the hand, no ridges."; }).Dry().Snd("roll");
            Act("prime", "Spot-prime the patch", C.Brush, 2f, () => Coats > 0 && !Has("primed") && !Has("painted"), () => { Set("primed"); return "Primed. Let it dry."; }).Uses(C.Primer).Dry().Wait(30f).Snd("scrape");
            Act("paint", "Roll two thin coats of matching paint, feathered into the wall", C.Roller, 3f, () => Coats > 0 && !Has("painted"), () =>
            {
                Set("painted"); S.mess = 0f;
                if (!Has("primed")) { Lose(0.2f); return "Painted — but raw compound drinks paint. The patch will flash dull in a week."; }
                return "Painted and blended in.";
            }).Uses(C.Paint).Dry().Snd("roll");
        }

        protected override void BuildModel(RM m)
        {
            if (!_wallLooked) { _wallLooked = true; _wall = SurfaceBehind(); }
            float z = 0.0015f;
            int dark = m.Mat(new Color(0.06f, 0.05f, 0.05f)), paper = m.Mat(new Color(0.62f, 0.58f, 0.5f)), gyp = m.Mat(new Color(0.88f, 0.87f, 0.84f));
            float ps = 0.305f;
            if (!Based)
            {
                if (Big && Has("square"))
                {
                    m.Box(new Vector3(0, 0, z), new Vector3(ps, ps, 0.001f), dark);
                    if (Has("backing")) foreach (float sx in new[] { -1f, 1f }) m.Box(new Vector3(sx * (ps / 2 - 0.02f), 0, z + 0.001f), new Vector3(0.064f, ps + 0.1f, 0.001f), m.Mat(SurfaceKind.PineLumber));
                }
                else
                {
                    float r = Big ? 0.125f : 0.04f;
                    Blob(m, r * 1.35f, z, Has("trimmed") ? gyp : paper, 11);
                    Blob(m, r, z + 0.001f, dark, 7);
                    if (Big) m.Box(new Vector3(0.05f, 0, z + 0.0015f), new Vector3(0.038f, r * 1.5f, 0.0005f), m.Mat(SurfaceKind.PineLumber, new Color(0.4f, 0.35f, 0.3f)));   // stud edge in the dark
                }
                return;
            }
            float size = Big ? ps : 0.152f;
            if (Big)
            {
                m.Box(new Vector3(0, 0, z), new Vector3(size, size, 0.001f), m.Mat(new Color(0.8f, 0.8f, 0.77f)));
                foreach (float sx in new[] { -1f, 1f }) for (int i = 0; i < 4; i++)
                    m.DiscZ(new Vector3(sx * (size / 2 - 0.02f), -0.11f + i * 0.073f, z + 0.001f), 0.004f, dark, 8);
            }
            else m.Box(new Vector3(0, 0, z), new Vector3(size, size, 0.001f), m.Mat(SurfaceKind.Galvanized));
            int n = Coats;
            if (n > 0)
            {
                bool wet = Drying;
                float s = size + 0.06f + n * 0.09f;
                var col = wet ? new Color(0.74f, 0.74f, 0.72f) : Has("sanded") ? new Color(0.97f, 0.97f, 0.95f) : new Color(0.9f, 0.9f, 0.87f);
                int mud = m.Mat(col);
                if (Big && n == 1) { foreach (float sx in new[] { -1f, 1f }) { m.Box(new Vector3(sx * size / 2, 0, z + 0.002f), new Vector3(0.1f, size + 0.1f, 0.001f), mud); m.Box(new Vector3(0, sx * size / 2, z + 0.002f), new Vector3(size + 0.1f, 0.1f, 0.001f), mud); } }
                else m.Box(new Vector3(0, 0, z + 0.002f), new Vector3(s, s, 0.001f), mud);
                if (!Has("sanded") && !wet) for (int i = 0; i < 3; i++) m.Box(new Vector3(-s / 4 + i * s / 4, 0, z + 0.003f), new Vector3(0.004f, s * 0.8f, 0.0006f), m.Mat(new Color(0.8f, 0.8f, 0.78f)));
                float top = size + 0.06f + 3 * 0.09f + 0.06f;
                if (Has("painted"))
                    m.Box(new Vector3(0, 0, z + 0.004f), new Vector3(top + 0.2f, top + 0.2f, 0.0006f), Has("primed") ? WallMat(m, Color.white) : WallMat(m, new Color(0.9f, 0.9f, 0.9f)));
                else if (Has("primed")) m.Box(new Vector3(0, 0, z + 0.004f), new Vector3(top, top, 0.0006f), m.Mat(SurfaceKind.PaintedWhite));
                if (Has("painted") && !Has("primed")) m.Box(new Vector3(0, 0, z + 0.005f), new Vector3(s, s, 0.0004f), WallMat(m, new Color(0.8f, 0.8f, 0.8f)));
            }
        }

        int WallMat(RM m, Color tint)
        {
            if (_wall != null && tint == Color.white) return m.Mat(_wall);
            Color c = _wall != null ? _wall.color : new Color(0.93f, 0.92f, 0.88f);
            return m.Mat(new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b));
        }

        static void Blob(RM m, float r, float z, int mat, int seed)
        {
            const int n = 14;
            var c = new Vector3(0, 0, z);
            Vector3 P(int i) { float a = i * Mathf.PI * 2f / n; float rr = r * (0.8f + 0.35f * Mathf.PerlinNoise(seed + i * 0.9f, seed * 0.37f)); return new Vector3(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, z); }
            for (int i = 0; i < n; i++) m.L.B.AddTriangle(c, P(i), P((i + 1) % n), mat, Vector3.forward);
        }
    }

    // =====================================================================================================================
    /// <summary>Sticking interior door. Cause 0 = loose top hinge screws (door sags), 1 = swollen latch edge (plane it).
    /// Frame: floor-level centre of the opening, +Z into the living room.</summary>
    public class StickingDoorDefect : RepairDefect
    {
        public const int LooseHinge = 0, Swollen = 1;
        const float W = 0.813f, H = 2.032f;

        public override string Title => "Sticking bedroom door";
        public override string Symptom => "The bedroom door sticks. You have to shoulder it to get it shut.";
        public override string Observation => Has("doorOff") ? "The door is off its hinges, on edge across the sawhorses" + (Has("planed") ? (Has("sealed") ? ", edge planed and sealed." : ", fresh bare wood on the planed edge.") : ".")
            : IsFixed ? "The door swings free." : "There's a shiny rub mark on the head jamb at the latch side.";
        public override bool IsFixed => Has("fixed") && !Has("doorOff");
        public override float BookHours => Cause == LooseHinge ? 0.5f : 1.5f;
        protected override float SkipPenalty => (Has("planed") && !Has("sealed") ? 0.2f : 0f) + (Has("verified") ? 0f : 0.05f);
        protected override Bounds ColliderBox => new Bounds(new Vector3(0, 1.05f, 0), new Vector3(1f, 2.15f, 0.3f));

        protected override void DefineActions()
        {
            Act("check", "Close the door slowly and read the gap all the way round", null, 2f, () => !Has("doorOff"), () =>
            {
                if (IsFixed) { Set("verified"); return "Even 1/8\" reveal all round. It swings free and latches with one finger."; }
                return Cause == LooseHinge ? "The gap is wide above the top hinge and pinched at the latch-side top corner. The top hinge leaf rocks: its screws spin without biting."
                                           : "Hinges are tight and the hinge-side gap is even. The latch edge is tight top to bottom: the edge has swollen.";
            }).Diag();
            Act("hinge", "Swap the top hinge's jamb screws for 3\" screws into the trimmer stud", C.Drill, 3f, () => !Has("hinge") && !Has("doorOff"), () =>
            {
                Set("hinge"); Clear("verified");
                if (Cause == LooseHinge) { Set("fixed"); return "The 3\" screws bite the stud and pull the door back up into the opening."; }
                return "The hinge was already tight. Nothing moved.";
            }).Uses(C.Screws3, 2).Snd("drill");
            Act("scribe", "Pencil a line along the latch edge where it rubs", null, 2f, () => !Has("scribed") && !Has("doorOff") && !Has("planed"), () => { Set("scribed"); return "Scribed a 1/8\" line down the latch edge."; });
            Act("pins", "Tap out the hinge pins (bottom first) and lift the door onto the sawhorses", C.Hammer, 3f, () => !Has("doorOff"), () => { Set("doorOff"); Clear("verified"); return "Door is off, latch edge up."; }).Snd("knock");
            Act("plane", "Plane the latch edge down to the line with a slight back bevel", C.Plane, 5f, () => Has("doorOff") && !Has("planed"), () =>
            {
                Set("planed"); Dirty(0.15f);
                string by = "";
                if (!Has("scribed")) { Lose(0.2f); by = "Planed by eye with no scribe line — the reveal is uneven. "; }
                if (Cause == Swollen) { Set("fixed"); return by + "Edge is down to the line."; }
                S.wrongRepair = true; Lose(0.3f);
                return by + "You took 1/8\" off a door that was only sagging. It still drags at the top and now the gap is fat at the bottom.";
            }).Snd("scrape");
            Act("seal", "Seal the raw planed edge with primer", C.Brush, 2f, () => Has("planed") && !Has("sealed") && Has("doorOff"), () => { Set("sealed"); return "Edge sealed so it won't take on moisture again."; }).Uses(C.Primer).Snd("scrape");
            Act("rehang", "Lift the door back onto its hinges and drive the pins (top first)", C.Hammer, 3f, () => Has("doorOff"), () => { Clear("doorOff"); return "Door is back on its hinges."; }).Snd("knock");
            Act("sweep", "Sweep up the shavings", null, 1.5f, () => S.mess > 0.01f && !Has("doorOff"), () => { S.mess = 0f; return "Floor is clean."; });
        }

        TownDoor Door { get { var f = House != null ? House.Fixture(Spot) : null; return f != null ? f.GetComponentInChildren<TownDoor>(true) : null; } }

        protected override void Restore() { var d = Door; if (d != null) d.Sticking = false; base.Restore(); }

        protected override void StateChanged()
        {
            House.SetFixtureVisible(Spot, !Has("doorOff"));
            var d = Door; if (d != null) d.Sticking = !IsFixed;
        }

        protected override void BuildModel(RM m)
        {
            if (!IsFixed) m.Box(new Vector3(0.28f, H + 0.004f, 0), new Vector3(0.2f, 0.003f, 0.03f), m.Mat(new Color(0.35f, 0.3f, 0.25f)));
            if (Has("hinge")) foreach (float dy in new[] { -0.03f, 0.03f }) m.DiscZ(new Vector3(-W / 2 - 0.004f, 1.85f + dy, 0.03f), 0.004f, m.Mat(SurfaceKind.Brass), 8);
            if (!Has("doorOff")) return;
            int pine = m.Mat(SurfaceKind.PineLumber), white = m.Mat(SurfaceKind.PaintedWhite);
            float z0 = 1.3f;
            foreach (float sx in new[] { -0.6f, 0.6f })
            {
                m.Box(new Vector3(sx, 0.72f, z0), new Vector3(0.089f, 0.038f, 0.8f), pine);
                foreach (float sz in new[] { -0.33f, 0.33f }) foreach (float lean in new[] { -1f, 1f })
                    m.Box(new Vector3(sx + lean * 0.09f, 0.35f, z0 + sz), new Vector3(0.038f, 0.74f, 0.064f), Quaternion.Euler(0, 0, lean * 14f), pine);
            }
            // slab on edge (latch edge up), clamped across the horses
            m.Box(new Vector3(0, 0.74f + W / 2, z0), new Vector3(H, W, 0.035f), white);
            float ey = 0.74f + W + 0.0005f;
            if (Has("planed")) m.Box(new Vector3(0, ey, z0), new Vector3(H, 0.001f, 0.035f), Has("sealed") ? white : pine);
            else if (Has("scribed")) m.Box(new Vector3(0, 0.74f + W - 0.004f, z0 + 0.018f), new Vector3(H, 0.001f, 0.0005f), m.Mat(SurfaceKind.DarkPlastic));
            if (Has("planed") && S.mess > 0.01f)
                for (int i = 0; i < 14; i++) m.Box(new Vector3(-0.8f + i * 0.12f, 0.004f, z0 + 0.12f + 0.1f * Mathf.Sin(i * 2.3f)), new Vector3(0.05f, 0.006f, 0.02f), Quaternion.Euler(0, i * 47f, 0), pine);
        }
    }
}
