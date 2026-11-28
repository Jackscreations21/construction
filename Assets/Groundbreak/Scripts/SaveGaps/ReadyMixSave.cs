using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Saves paid-for ready-mix and the form outline still being drawn (save key "readymix", order 15: after "concrete", which
    /// restores the closed forms the trucks park at).
    /// <list type="bullet">
    /// <item>Every ready-mix order that isn't finished: the mix (yards, psi, slump, fiber / accelerator / retarder / pump), cost,
    /// arrival time, yards left in the drum, unload clock and standby charged. A scheduled order arrives at the same game time;
    /// a truck that was on the road backs in again; a truck on site is parked where it stood with its unload clock running from
    /// the saved arrival. A truck that was already leaving is done (nothing owed either way). Nothing is charged again.</item>
    /// <item><see cref="FormSystem.Building"/>: the corners of an outline not yet closed (with their stakes / string-line markers),
    /// so the player can keep clicking corners and close it.</item>
    /// </list>
    /// </summary>
    public static class ReadyMixSave
    {
        public const string Key = "readymix";

        [Serializable]
        class OrderD
        {
            public float yards, scheduledFor, arrivedAt = -1f, yardsLeft; public int psi, slump, cost, standby, state;
            public bool fiber, accel, retard, pump, hasTruck; public Vector3 truckPos; public Quaternion truckRot = Quaternion.identity;
        }
        [Serializable]
        class Data
        {
            public int version = 1;
            public List<OrderD> orders = new List<OrderD>();
            public bool building; public float buildingTopY; public List<Vector3> buildingCorners = new List<Vector3>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => SaveSystem.RegisterExtension(Key, Save, Load, 15);

        static string Save()
        {
            var d = new Data();
            foreach (var o in ConcreteDispatch.Orders)
            {
                if (o == null || o.State == OrderState.Done || o.State == OrderState.Leaving) continue;
                var od = new OrderD
                {
                    yards = o.Yards, psi = o.Psi, slump = o.SlumpIn, fiber = o.Fiber, accel = o.Accelerator, retard = o.Retarder, pump = o.Pump,
                    scheduledFor = o.ScheduledFor, arrivedAt = o.ArrivedAt, yardsLeft = o.YardsLeft, cost = o.Cost, standby = o.StandbyCharged, state = (int)o.State,
                };
                if (o.State == OrderState.OnSite && o.Truck != null) { od.hasTruck = true; od.truckPos = o.Truck.transform.position; od.truckRot = o.Truck.transform.rotation; }
                d.orders.Add(od);
            }
            var b = FormSystem.Building;
            if (b != null && !b.Closed && b.Corners.Count > 0)
            {
                d.building = true; d.buildingTopY = b.TopY;
                d.buildingCorners.AddRange(b.Corners);
            }
            return JsonUtility.ToJson(d);
        }

        /// <summary>Sends every truck away at once and forgets the orders (a load does this first).</summary>
        public static void Wipe()
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<ReadyMixTruck>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null) SaveGaps.Kill(t.gameObject);
            ConcreteDispatch.Orders.Clear();
        }

        static void Load(string json)
        {
            Wipe();
            // ("concrete" already cleared any outline being drawn; old saves simply have none)
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            foreach (var od in d.orders)
            {
                try { RestoreOrder(od); }
                catch (Exception e) { Debug.LogError("[Save] ready-mix order failed to restore: " + e); }
            }
            if (d.building && d.buildingCorners.Count > 0)
            {
                try { RestoreBuilding(d.buildingTopY, d.buildingCorners); }
                catch (Exception e) { Debug.LogError("[Save] form outline being drawn failed to restore: " + e); }
            }
        }

        static void RestoreOrder(OrderD od)
        {
            var o = new ConcreteOrder
            {
                Yards = od.yards, Psi = od.psi, SlumpIn = od.slump, Fiber = od.fiber, Accelerator = od.accel, Retarder = od.retard, Pump = od.pump,
                ScheduledFor = od.scheduledFor, ArrivedAt = od.arrivedAt, YardsLeft = od.yardsLeft, Cost = od.cost, StandbyCharged = od.standby,
            };
            var st = (OrderState)od.state;
            if (st == OrderState.OnSite)
            {
                ConcreteDispatch.Orders.Add(o);
                SaveGaps.Quiet(() =>
                {
                    var t = ReadyMixTruck.Dispatch(o);
                    t.StopAllCoroutines();     // parked already: no backing in, no "on site" toast, unload clock not restarted
                    Vector3 pos = t.transform.position; Quaternion rot = t.transform.rotation;
                    if (od.hasTruck) { pos = od.truckPos; rot = od.truckRot; }
                    else { ReadyMixTruck.ParkingFor(FormSystem.Active, out var park, out var face); pos = park; rot = Quaternion.LookRotation(face); }
                    t.transform.SetPositionAndRotation(pos, rot);
                    t.DrumRpm = 6f;
                    o.Truck = t;
                });
                o.State = OrderState.OnSite;
                if (o.ArrivedAt < 0f) o.ArrivedAt = ConcreteTime.Now;
            }
            else
            {
                // scheduled, or on the road (it backs in again from the plant road when the dispatcher ticks)
                o.State = OrderState.Scheduled;
                ConcreteDispatch.Orders.Add(o);
            }
        }

        /// <summary>The outline being drawn: same corners, corner stakes and string lines as <see cref="FormSystem.AddCorner"/> leaves.</summary>
        static void RestoreBuilding(float topY, List<Vector3> corners)
        {
            var o = new FormOutline { TopY = topY, Root = new GameObject("ConcreteForms") };
            FormSystem.All.Add(o);
            FormSystem.Building = o;
            for (int i = 0; i < corners.Count; i++)
            {
                var p = corners[i];
                o.Corners.Add(p);
                var mk = CTool.Build("FormCorner", (b, m) =>
                {
                    b.AddBox(new Vector3(0, -0.15f, 0), new Vector3(0.012f, 0.3f, 0.012f), Quaternion.identity, m.K(SurfaceKind.PineLumber));
                    b.AddBox(new Vector3(0.03f, -0.03f, 0), new Vector3(0.06f, 0.04f, 0.002f), Quaternion.identity, m.C(new Color(1f, 0.4f, 0f)));
                }, o.Root.transform);
                mk.transform.position = p;
                o.Markers.Add(mk);
                if (i >= 1)
                {
                    Vector3 a = corners[i - 1], c = p, dd = c - a;
                    if (dd.sqrMagnitude < 1e-8f) continue;
                    o.Markers.Add(CTool.Build("FormLine", (b, m) => b.AddRod((a + c) * 0.5f, Quaternion.LookRotation(dd.normalized), 0.0012f, dd.magnitude, 4, m.C(new Color(1f, 0.25f, 0.55f)), false), o.Root.transform));
                }
            }
        }
    }
}
