using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum OrderState { Scheduled, EnRoute, OnSite, Leaving, Done }

    /// <summary>A ready-mix load: what was ordered, what it cost, where it is.</summary>
    public class ConcreteOrder
    {
        public float Yards = 4f;
        public int Psi = 3000;
        public int SlumpIn = 4;
        public bool Fiber, Accelerator, Retarder, Pump;
        public float ScheduledFor;      // game minutes (arrival)
        public float ArrivedAt = -1f;
        public float YardsLeft;
        public int Cost;
        public int StandbyCharged;
        public OrderState State;
        public ReadyMixTruck Truck;
        public float FreeUnloadMinutes => 60f;
        public float UnloadElapsed => ArrivedAt < 0 ? 0 : ConcreteTime.Now - ArrivedAt;
        /// <summary>Set-time multiplier: accelerator ~35% faster, retarder ~50% slower.</summary>
        public float SetRate => Accelerator ? 0.65f : Retarder ? 1.5f : 1f;
        public string Mix => $"{Yards:0.##} yd {Psi} psi, {SlumpIn}\" slump{(Fiber ? ", fiber" : "")}{(Accelerator ? ", accelerator" : "")}{(Retarder ? ", retarder" : "")}{(Pump ? ", pump" : "")}";
    }

    /// <summary>Ready-mix ordering, prices and fees, truck dispatch; draws the "Concrete Dispatch" tablet app.</summary>
    public static class ConcreteDispatch
    {
        public const float MinLoadYards = 5f;
        public const int ShortLoadFeePerYard = 75, PumpFee = 850, FuelSurcharge = 25, FiberPerYard = 15, AccelPerYard = 9, RetarderPerYard = 7;
        public const float StandbyPerMinute = 2f;     // $120/hr after the free unload window
        public const float LeadMinutes = 45f;         // ASAP: batch + drive

        public static readonly List<ConcreteOrder> Orders = new List<ConcreteOrder>();
        public static ConcreteOrder OnSite { get { foreach (var o in Orders) if (o.State == OrderState.OnSite) return o; return null; } }

        static ConcreteOrder _draft = new ConcreteOrder();
        static int _schedule;  // 0 ASAP, 1 in 2 h, 2 tomorrow 7 AM
        static string _msg = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Orders.Clear(); _draft = new ConcreteOrder(); _schedule = 0; _msg = ""; }

        public static int PricePerYard(int psi) => psi >= 4000 ? 180 : psi >= 3000 ? 165 : 155;

        public static int Quote(ConcreteOrder o, out string breakdown)
        {
            float y = o.Yards;
            int mix = Mathf.RoundToInt(PricePerYard(o.Psi) * y);
            int add = Mathf.RoundToInt(y * ((o.Fiber ? FiberPerYard : 0) + (o.Accelerator ? AccelPerYard : 0) + (o.Retarder ? RetarderPerYard : 0)));
            int shortFee = y < MinLoadYards ? Mathf.RoundToInt((MinLoadYards - y) * ShortLoadFeePerYard) : 0;
            int pump = o.Pump ? PumpFee : 0;
            int total = mix + add + shortFee + pump + FuelSurcharge;
            breakdown = $"{y:0.##} yd x ${PricePerYard(o.Psi)} = ${mix}" + (add > 0 ? $" · admixtures ${add}" : "") +
                        (shortFee > 0 ? $" · short load (< {MinLoadYards:0} yd) ${shortFee}" : "") + (pump > 0 ? $" · pump truck ${pump}" : "") + $" · fuel ${FuelSurcharge}";
            return total;
        }

        /// <summary>Places an order (charges the wallet). arriveInMinutes = game minutes until the truck is on site.</summary>
        public static ConcreteOrder Place(ConcreteOrder o, float arriveInMinutes, out string message)
        {
            if (o.Yards < 0.25f) { message = "Order at least 1/4 yard"; return null; }
            if (o.Accelerator && o.Retarder) { message = "Pick accelerator OR retarder, not both"; return null; }
            int cost = Quote(o, out _);
            if (!Game.Wallet.TrySpend(cost, $"Ready-mix: {o.Mix}")) { message = $"Not enough money (${cost:N0})"; return null; }
            o.Cost = cost; o.YardsLeft = o.Yards; o.State = OrderState.Scheduled;
            o.ScheduledFor = ConcreteTime.Now + Mathf.Max(LeadMinutes * 0.2f, arriveInMinutes);
            Orders.Add(o);
            message = $"Ordered {o.Mix} - ${cost:N0}. Truck ETA {ConcreteTime.Duration(o.ScheduledFor - ConcreteTime.Now)}.";
            Game.Toast(message);
            return o;
        }

        /// <summary>Per-frame: dispatch trucks, run the unload clock and standby fees.</summary>
        public static void Tick()
        {
            float now = ConcreteTime.Now;
            foreach (var o in Orders)
            {
                // the truck leaves the plant so it backs in right at the scheduled time (~25 s of driving)
                if (o.State == OrderState.Scheduled && now >= o.ScheduledFor - 25f * 1440f / GameClock.RealSecondsPerDay)
                {
                    o.State = OrderState.EnRoute;
                    o.Truck = ReadyMixTruck.Dispatch(o);
                }
                if (o.State == OrderState.OnSite)
                {
                    float over = o.UnloadElapsed - o.FreeUnloadMinutes;
                    int owed = over > 0 ? Mathf.FloorToInt(over * StandbyPerMinute) : 0;
                    if (owed > o.StandbyCharged)
                    {
                        int delta = owed - o.StandbyCharged;
                        if (Game.Wallet.TrySpend(delta, "Ready-mix standby time")) o.StandbyCharged = owed;
                    }
                }
            }
        }

        /// <summary>Sends the empty (or done) truck back to the plant. Leftover concrete costs a return fee.</summary>
        public static string Release(ConcreteOrder o)
        {
            if (o == null || o.State != OrderState.OnSite) return "No truck on site";
            string m = "Truck released";
            if (o.YardsLeft > 0.1f) { Game.Wallet.TrySpend(50, "Returned concrete / washout"); m += $" with {o.YardsLeft:0.00} yd left over ($50 return fee)"; }
            o.State = OrderState.Leaving;
            if (o.Truck != null) o.Truck.Leave();
            return m;
        }

        // ------------------------------------------------------------------ tablet app

        public static void DrawApp()
        {
            var t = Tablet.I; if (t == null) return;
            var L = t.LabelStyle; var Hd = t.HeaderStyle; var Bt = t.ButtonStyle;
            float bw = L.fontSize * 5f;
            var o = FormSystem.Active;

            GUILayout.Label("Takeoff", Hd);
            if (o == null) GUILayout.Label("No closed forms yet. Set forms with the Form Setting Kit, then come back for the yardage.", L);
            else
            {
                float yd = FormSystem.TakeoffYards(o);
                GUILayout.Label($"Forms: {o.AreaSqFt:0} sq ft, top of form {Units.Format(o.TopY - SiteLayout.BenchmarkY)} above benchmark · net volume {yd:0.00} cu yd ({yd * 27f:0} cu ft)", L);
                float risk = FormSystem.MaxRisk(o, out string why);
                GUILayout.Label($"Rebar: {RebarSystem.Bars.Count} bars, {RebarSystem.TiedCount}/{RebarSystem.Crossings.Count} crossings tied · Poly: {(o.Poly != null ? "down" : "none")} · Forms: {(risk <= 0 ? "staked OK" : $"BLOWOUT RISK {risk * 100:0}% - {why}")}", L);
                if (GUILayout.Button($"Use takeoff + 10% waste = {FormSystem.RecommendedOrder(o):0.00} yd", Bt)) _draft.Yards = FormSystem.RecommendedOrder(o);
            }

            GUILayout.Space(6);
            GUILayout.Label("Order ready-mix", Hd);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Yards: {_draft.Yards:0.00}", L, GUILayout.Width(L.fontSize * 9));
            if (GUILayout.Button("-1/4", Bt, GUILayout.Width(bw))) _draft.Yards = Mathf.Max(0.25f, _draft.Yards - 0.25f);
            if (GUILayout.Button("+1/4", Bt, GUILayout.Width(bw))) _draft.Yards += 0.25f;
            if (GUILayout.Button("+1", Bt, GUILayout.Width(bw))) _draft.Yards += 1f;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Strength:", L, GUILayout.Width(L.fontSize * 9));
            foreach (int psi in new[] { 2500, 3000, 4000 })
                if (GUILayout.Toggle(_draft.Psi == psi, $" {psi} psi (${PricePerYard(psi)})", Bt)) _draft.Psi = psi;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Slump:", L, GUILayout.Width(L.fontSize * 9));
            foreach (int s in new[] { 4, 5, 6 }) if (GUILayout.Toggle(_draft.SlumpIn == s, $" {s}\"", Bt)) _draft.SlumpIn = s;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            _draft.Fiber = GUILayout.Toggle(_draft.Fiber, $" Fiber mesh (+${FiberPerYard}/yd)", Bt);
            bool acc = GUILayout.Toggle(_draft.Accelerator, $" Accelerator (+${AccelPerYard}/yd)", Bt);
            bool ret = GUILayout.Toggle(_draft.Retarder, $" Retarder (+${RetarderPerYard}/yd)", Bt);
            if (acc && !_draft.Accelerator) ret = false; if (ret && !_draft.Retarder) acc = false;
            _draft.Accelerator = acc; _draft.Retarder = ret;
            _draft.Pump = GUILayout.Toggle(_draft.Pump, $" Pump truck (+${PumpFee})", Bt);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Arrive:", L, GUILayout.Width(L.fontSize * 9));
            string[] sch = { $"ASAP (~{LeadMinutes:0} min)", "In 2 hours", "Tomorrow 7:00 AM" };
            _schedule = GUILayout.SelectionGrid(_schedule, sch, 3, Bt);
            GUILayout.EndHorizontal();
            int total = Quote(_draft, out string bd);
            GUILayout.Label(bd, L);
            GUILayout.Label($"Total ${total:N0} · 60 min free unload, then ${StandbyPerMinute * 60:0}/hr standby", Hd);
            if (GUILayout.Button("Call it in", Bt))
            {
                float when = _schedule == 0 ? LeadMinutes : _schedule == 1 ? 120f : MinutesUntilTomorrow7();
                var placed = Place(_draft, when, out _msg);
                if (placed != null) { var keep = _draft; _draft = new ConcreteOrder { Yards = keep.Yards, Psi = keep.Psi, SlumpIn = keep.SlumpIn, Fiber = keep.Fiber, Accelerator = keep.Accelerator, Retarder = keep.Retarder, Pump = keep.Pump }; }
                t.ShowMessage(_msg);
            }

            GUILayout.Space(6);
            GUILayout.Label("Trucks", Hd);
            if (Orders.Count == 0) GUILayout.Label("No orders.", L);
            foreach (var ord in Orders)
            {
                if (ord.State == OrderState.Done) continue;
                string st = ord.State == OrderState.Scheduled ? $"scheduled, ETA {ConcreteTime.Duration(ord.ScheduledFor - ConcreteTime.Now)}"
                          : ord.State == OrderState.EnRoute ? "on the road, backing in"
                          : ord.State == OrderState.OnSite ? $"ON SITE · {ord.YardsLeft:0.00} yd left in the drum · unload {ConcreteTime.Duration(ord.UnloadElapsed)} of 60 free" + (ord.StandbyCharged > 0 ? $" · standby ${ord.StandbyCharged}" : "")
                          : "leaving";
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{ord.Mix}: {st}", L);
                if (ord.State == OrderState.OnSite && GUILayout.Button("Release truck", Bt, GUILayout.Width(L.fontSize * 8))) t.ShowMessage(Release(ord));
                GUILayout.EndHorizontal();
            }

            var pour = o != null ? o.Pour : PourSystem.Latest;
            if (pour != null)
            {
                GUILayout.Space(6);
                GUILayout.Label("Pour & finish", Hd);
                foreach (var line in pour.StatusLines()) GUILayout.Label(line, L);
                var el = pour.Element;
                if (el != null)
                {
                    GUILayout.BeginHorizontal();
                    if (!el.Covered && GUILayout.Button("Cover with poly (moist cure)", Bt)) t.ShowMessage(el.Cover());
                    if (!el.WetCured && GUILayout.Button("Wet cure with the hose", Bt)) t.ShowMessage(el.WetCure());
                    GUILayout.EndHorizontal();
                }
            }
        }

        static float MinutesUntilTomorrow7()
        {
            float now = ConcreteTime.Now;
            float dayStart = Mathf.Floor(now / 1440f) * 1440f;
            return dayStart + 1440f + 420f - now;
        }
    }

    /// <summary>Ready-mix truck: tandem-axle mixer with a rotating drum and a swinging, folding chute at the rear.
    /// Origin at ground level under the rear of the drum's discharge, truck faces +Z (cab forward).</summary>
    public class ReadyMixTruck : MonoBehaviour
    {
        public ConcreteOrder Order;
        public Transform Drum, ChutePivot;
        public const float ChuteReach = 5.6f;   // main chute + 3 extensions, measured from the pivot (~18')
        public float DrumRpm = 2f;
        const float Speed = 4f;

        public Vector3 ChuteBase => ChutePivot != null ? ChutePivot.position : transform.position;

        public static ReadyMixTruck Dispatch(ConcreteOrder o)
        {
            var t = Build();
            t.Order = o;
            Vector3 park, face;
            ParkingFor(FormSystem.Active, out park, out face);
            t.transform.SetPositionAndRotation(park + face * 45f, Quaternion.LookRotation(face));
            t.StartCoroutine(t.BackIn(park));
            return t;
        }

        /// <summary>Park with the chute 0.8 m outside the middle of the longest form run, cab pointing away.</summary>
        public static void ParkingFor(FormOutline o, out Vector3 park, out Vector3 face)
        {
            if (o == null || o.Runs.Count == 0)
            {
                Vector3 d = DeliveryService.I != null ? DeliveryService.I.DropPoint : new Vector3(6, 0, -2);
                park = d + new Vector3(-3, 0, 0); face = Vector3.back; return;
            }
            FormRun best = o.Runs[0];
            foreach (var r in o.Runs) if (r.Length > best.Length) best = r;
            face = best.Out;
            park = (best.A + best.B) * 0.5f + best.Out * 0.8f;
            park.y = 0f;
        }

        IEnumerator BackIn(Vector3 park)
        {
            DrumRpm = 3f;
            while ((transform.position - park).sqrMagnitude > 0.0004f)
            {
                transform.position = Vector3.MoveTowards(transform.position, park, Speed * Time.deltaTime);
                yield return null;
            }
            Order.State = OrderState.OnSite;
            Order.ArrivedAt = ConcreteTime.Now;
            DrumRpm = 6f;
            Game.Toast($"Ready-mix on site: {Order.Mix}. 60 min free unload starts now.");
        }

        public void Leave() { StartCoroutine(Drive()); }
        IEnumerator Drive()
        {
            DrumRpm = 2f;
            Vector3 target = transform.position + transform.forward * 60f;
            while (Vector3.Distance(transform.position, target) > 0.1f)
            {
                transform.position = Vector3.MoveTowards(transform.position, target, Speed * 1.5f * Time.deltaTime);
                yield return null;
            }
            Order.State = OrderState.Done;
            Destroy(gameObject);
        }

        void Update()
        {
            if (Drum != null) Drum.Rotate(Vector3.up, DrumRpm * 6f * Time.deltaTime, Space.Self);
        }

        /// <summary>Swings/drops the chute toward a world point. Returns true if it is within reach.</summary>
        public bool AimChute(Vector3 target)
        {
            if (ChutePivot == null) return false;
            Vector3 d = target - ChutePivot.position; float h = d.y; d.y = 0;
            float dist = d.magnitude;
            if (d.sqrMagnitude > 0.01f)
            {
                float local = Mathf.DeltaAngle(transform.eulerAngles.y, Quaternion.LookRotation(d.normalized).eulerAngles.y);
                float yaw = 180f + Mathf.Clamp(Mathf.DeltaAngle(180f, local), -100f, 100f); // chute swings around the back
                float pitch = Mathf.Clamp(Mathf.Atan2(-h, Mathf.Max(0.5f, dist)) * Mathf.Rad2Deg, 8f, 35f);
                ChutePivot.localRotation = Quaternion.Euler(pitch, yaw, 0);
            }
            return Order != null && Order.Pump || dist <= ChuteReach;
        }

        /// <summary>Takes up to `yards` out of the drum.</summary>
        public float Discharge(float yards)
        {
            if (Order == null || Order.State != OrderState.OnSite) return 0;
            float y = Mathf.Min(yards, Order.YardsLeft);
            Order.YardsLeft -= y;
            DrumRpm = -8f; // reversing the drum discharges
            CancelInvoke(nameof(Mixing)); Invoke(nameof(Mixing), 0.5f);
            if (Order.YardsLeft <= 0.001f) Game.Toast("Drum is empty");
            return y;
        }
        void Mixing() { DrumRpm = 6f; }

        // ------------------------------------------------------------------ model

        static ReadyMixTruck Build()
        {
            var root = new GameObject("ReadyMixTruck");
            var t = root.AddComponent<ReadyMixTruck>();
            Color cabColor = new Color(0.95f, 0.95f, 0.93f);
            Color drumRed = new Color(0.75f, 0.08f, 0.06f);
            const float rear = 0f;   // z of the rear bumper plane, everything forward is +Z

            CTool.Build("Chassis", (b, m) =>
            {
                int paint = m.K(SurfaceKind.PaintedSteel, cabColor), blk = m.K(SurfaceKind.MatteBlack), tire = m.K(SurfaceKind.Tire),
                    alu = m.K(SurfaceKind.BrushedSteel), glass = m.K(SurfaceKind.Glass), red = m.K(SurfaceKind.TruckPaint), galv = m.K(SurfaceKind.Galvanized);
                // frame rails
                b.AddBox(new Vector3(-0.45f, 0.95f, 4.6f), new Vector3(0.12f, 0.3f, 8.6f), Quaternion.identity, blk);
                b.AddBox(new Vector3(0.45f, 0.95f, 4.6f), new Vector3(0.12f, 0.3f, 8.6f), Quaternion.identity, blk);
                b.AddBox(new Vector3(0, 0.75f, rear + 0.35f), new Vector3(2.4f, 0.18f, 0.18f), Quaternion.identity, blk); // rear bumper
                // wheels: steer axle + tandem drive (duals) + lift axle (raised)
                void Wheel(float x, float z, float r, float w) => b.AddCylinder(new Vector3(x, r, z), Quaternion.Euler(0, 0, 90), r, w, 20, tire);
                void Hub(float x, float z, float r) => b.AddCylinder(new Vector3(x, r, z), Quaternion.Euler(0, 0, 90), 0.26f, 0.05f, 12, alu);
                foreach (float z in new[] { 1.55f, 2.9f })
                {
                    foreach (float s in new[] { -1f, 1f }) { Wheel(s * 0.88f, z, 0.52f, 0.28f); Wheel(s * 0.58f, z, 0.52f, 0.28f); Hub(s * 1.03f, z, 0.52f); }
                    b.AddCylinder(new Vector3(0, 0.52f, z), Quaternion.Euler(0, 0, 90), 0.09f, 1.6f, 10, blk);
                }
                foreach (float s in new[] { -1f, 1f }) { Wheel(s * 1.0f, 8.0f, 0.52f, 0.32f); Hub(s * 1.17f, 8.0f, 0.52f); }
                foreach (float s in new[] { -1f, 1f }) Wheel(s * 0.95f, 4.6f, 0.45f, 0.26f);   // lift axle, raised
                // fenders over the tandems
                foreach (float s in new[] { -1f, 1f }) b.AddBox(new Vector3(s * 0.8f, 1.12f, 2.22f), new Vector3(0.75f, 0.05f, 2.4f), Quaternion.identity, alu);
                // cab
                b.AddBevelBox(new Vector3(0, 2.0f, 8.45f), new Vector3(2.35f, 1.7f, 1.7f), Quaternion.identity, 0.08f, paint, paint);
                b.AddBox(new Vector3(0, 1.35f, 9.55f), new Vector3(2.3f, 0.95f, 1.1f), Quaternion.identity, paint);   // hood
                b.AddBox(new Vector3(0, 1.35f, 10.12f), new Vector3(1.0f, 0.8f, 0.04f), Quaternion.identity, galv);   // grille
                b.AddBox(new Vector3(0, 0.75f, 10.2f), new Vector3(2.4f, 0.25f, 0.2f), Quaternion.identity, alu);     // bumper
                b.AddBox(new Vector3(0, 2.35f, 9.31f), new Vector3(2.1f, 0.75f, 0.02f), Quaternion.Euler(-8, 0, 0), glass);
                foreach (float s in new[] { -1f, 1f })
                {
                    b.AddBox(new Vector3(s * 1.18f, 2.3f, 8.55f), new Vector3(0.02f, 0.6f, 0.9f), Quaternion.identity, glass);
                    b.AddBox(new Vector3(s * 1.35f, 2.3f, 9.2f), new Vector3(0.04f, 0.35f, 0.18f), Quaternion.identity, blk); // mirrors
                    b.AddBox(new Vector3(s * 0.85f, 1.35f, 10.15f), new Vector3(0.28f, 0.16f, 0.03f), Quaternion.identity, m.C(new Color(1f, 1f, 0.9f)));
                }
                b.AddCylinder(new Vector3(0.95f, 3.0f, 7.5f), 0.08f, 1.6f, 10, alu); // exhaust stack
                // water tank + pedestals for the drum
                b.AddCylinder(new Vector3(0, 2.2f, 7.25f), Quaternion.Euler(0, 0, 90), 0.4f, 1.6f, 18, alu);
                b.AddBox(new Vector3(0, 1.6f, 6.9f), new Vector3(1.4f, 1.1f, 0.25f), Quaternion.identity, red);   // front pedestal
                b.AddBox(new Vector3(0, 1.75f, 1.35f), new Vector3(1.5f, 1.4f, 0.3f), Quaternion.identity, red);  // rear pedestal
                // charging hopper above the drum mouth + ladder
                b.AddFrustum(new Vector3(0, 3.75f, 1.45f), Quaternion.identity, 0.25f, 0.55f, 0.55f, 16, galv, false);
                b.AddBox(new Vector3(0.9f, 1.9f, 0.55f), new Vector3(0.04f, 2.6f, 0.04f), Quaternion.identity, alu);
                b.AddBox(new Vector3(1.3f, 1.9f, 0.55f), new Vector3(0.04f, 2.6f, 0.04f), Quaternion.identity, alu);
                for (int i = 0; i < 8; i++) b.AddBox(new Vector3(1.1f, 0.9f + i * 0.3f, 0.55f), new Vector3(0.4f, 0.03f, 0.03f), Quaternion.identity, alu);
                // collection hopper under the discharge
                b.AddFrustum(new Vector3(0, 2.6f, 1.1f), Quaternion.identity, 0.18f, 0.4f, 0.35f, 14, galv, false);
            }, root.transform);

            // Drum: lathe about its own axis, tilted ~14° (mouth high at the rear)
            var drumPivot = new GameObject("DrumAxis").transform;
            drumPivot.SetParent(root.transform, false);
            drumPivot.localPosition = new Vector3(0, 2.75f, 4.2f);
            // local +Y of the drum axis points to the rear-and-up (mouth)
            drumPivot.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(0, Mathf.Sin(14f * Mathf.Deg2Rad), -Mathf.Cos(14f * Mathf.Deg2Rad)));
            var drum = CTool.Build("Drum", (b, m) =>
            {
                int red = m.K(SurfaceKind.TruckPaint), white = m.K(SurfaceKind.PaintedWhite), dark = m.K(SurfaceKind.MatteBlack);
                // (radius, y) bottom (front) -> mouth (rear)
                var prof = new[] { new Vector2(0.001f, -3.0f), new Vector2(0.55f, -2.95f), new Vector2(1.18f, -1.4f), new Vector2(1.2f, -0.6f), new Vector2(1.1f, 0.4f), new Vector2(0.55f, 2.3f), new Vector2(0.38f, 2.55f) };
                b.AddLathe(Vector3.zero, Quaternion.identity, prof, 28, red, 50f);
                b.AddRing(new Vector3(0, -1.0f, 0), Quaternion.identity, 1.2f, 1.17f, 0.25f, 28, white);   // painted band
                b.AddRing(new Vector3(0, 0.1f, 0), Quaternion.identity, 1.16f, 1.13f, 0.18f, 28, white);
                b.AddRing(new Vector3(0, 1.0f, 0), Quaternion.identity, 0.92f, 0.85f, 0.10f, 28, dark);    // roller track
                b.AddRing(new Vector3(0, 2.5f, 0), Quaternion.identity, 0.42f, 0.30f, 0.12f, 20, dark);    // mouth
                for (int k = 0; k < 6; k++) b.AddBox(new Vector3(Mathf.Cos(k * 1.047f) * 1.15f, -0.3f, Mathf.Sin(k * 1.047f) * 1.15f), new Vector3(0.05f, 0.9f, 0.05f), Quaternion.Euler(0, -k * 60f, 0), white); // stripe dashes show rotation
            }, drumPivot);
            t.Drum = drum.transform;

            // Main chute: pivot under the collection hopper, swings around the rear
            t.ChutePivot = new GameObject("ChutePivot").transform;
            t.ChutePivot.SetParent(root.transform, false);
            t.ChutePivot.localPosition = new Vector3(0, 2.35f, 1.0f);
            t.ChutePivot.localRotation = Quaternion.Euler(20f, 180f, 0);
            CTool.Build("Chute", (b, m) =>
            {
                int galv = m.K(SurfaceKind.Galvanized), blk = m.K(SurfaceKind.MatteBlack);
                // U-shaped trough sections (main + 2 extensions folded out), 14" wide
                float w = 0.18f;
                for (int sec = 0; sec < 3; sec++)
                {
                    float z0 = sec * 1.2f, len = 1.2f;
                    for (int k = 0; k < 6; k++)
                    {
                        float a0 = Mathf.PI * k / 6f, a1 = Mathf.PI * (k + 1) / 6f;
                        Vector3 p0 = new Vector3(Mathf.Cos(a0) * w, -Mathf.Sin(a0) * w, z0), p1 = new Vector3(Mathf.Cos(a1) * w, -Mathf.Sin(a1) * w, z0);
                        Vector3 nIn = -new Vector3(Mathf.Cos((a0 + a1) * 0.5f), -Mathf.Sin((a0 + a1) * 0.5f), 0);
                        b.AddQuad(p0, p1, p1 + Vector3.forward * len, p0 + Vector3.forward * len, galv, nIn);
                        b.AddQuad(p0, p1, p1 + Vector3.forward * len, p0 + Vector3.forward * len, galv, -nIn);
                    }
                    b.AddBox(new Vector3(0, -w - 0.02f, z0 + 0.05f), new Vector3(2 * w + 0.06f, 0.04f, 0.08f), Quaternion.identity, blk); // joint clamp
                }
            }, t.ChutePivot);
            return t;
        }
    }

    /// <summary>Chute signals + wheelbarrow. Hold LMB on the forms: the driver swings the chute there and discharges;
    /// beyond the chute's reach, fill the wheelbarrow at the chute (LMB near the truck) and dump it (LMB at the forms).</summary>
    public class ChuteTool : ConcreteToolBase
    {
        public override string Id => "chute";
        public override string Name => "Chute + Wheelbarrow";
        public const float BarrowCubicFeet = 3f;   // a 6 cu ft contractor barrow is filled about half with concrete
        public float BarrowYards;                 // concrete in the wheelbarrow

        public override string Hint
        {
            get
            {
                var o = ConcreteDispatch.OnSite;
                if (o == null) return "No ready-mix truck on site (order one in Concrete Dispatch)";
                return $"Hold LMB: chute to aim · beyond {Units.Format(ReadyMixTruck.ChuteReach)}: LMB at the chute loads the wheelbarrow, LMB at the forms dumps it · drum {o.YardsLeft:0.00} yd · barrow {BarrowYards * 27f:0.0} cu ft";
            }
        }

        public override void PrimaryHeld()
        {
            var ord = ConcreteDispatch.OnSite;
            if (ord == null || ord.Truck == null || !CTool.Aim(out var hit, 12f)) return;
            var pour = PourSystem.ForPoint(hit.point);
            bool atTruck = (CTool.XZ(hit.point - ord.Truck.ChuteBase)).magnitude < 1.5f || hit.collider.GetComponentInParent<ReadyMixTruck>() != null;
            if (pour != null && ord.Truck.AimChute(hit.point))
            {
                pour.Discharge(ord.Truck, hit.point, Time.deltaTime);
                return;
            }
            if (!Ready(0.4f)) return;
            if (atTruck && BarrowYards <= 0.001f) { BarrowYards = ord.Truck.Discharge(BarrowCubicFeet / 27f); Game.Toast($"Wheelbarrow loaded ({BarrowYards * 27f:0.0} cu ft)"); }
            else if (pour != null && BarrowYards > 0) { pour.AddConcrete(hit.point, BarrowYards / FormSystem.YardsPerM3, ord); BarrowYards = 0; Game.Toast("Dumped the wheelbarrow"); }
            else if (pour != null) Game.Toast("Out of chute reach - load the wheelbarrow at the chute");
        }
    }
}
