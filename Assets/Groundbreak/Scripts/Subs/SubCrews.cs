using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// The sub's people and truck on site. Each contract gets a temporary crew of <see cref="Worker"/>s (never in
    /// Company.Workers, so never on payroll) drawn by the Crew feature's <see cref="CrewNpc"/> (flagged External so CrewManager
    /// leaves them alone) and working <see cref="CrewTask"/>s from <see cref="SubScopes"/>. Each worker owns a slot job id
    /// "sub:&lt;contract&gt;:&lt;n&gt;" so two hands never work the same task; a hand who runs out takes over an unclaimed task.
    /// </summary>
    public class SubCrews : MonoBehaviour
    {
        public static SubCrews I;

        class Live
        {
            public readonly List<Worker> Workers = new List<Worker>();
            public readonly List<CrewNpc> Npcs = new List<CrewNpc>();
            public readonly List<CrewTask> Tasks = new List<CrewTask>();
            public GameObject Truck;
            public bool TasksMade;
        }

        static readonly Dictionary<string, Live> _live = new Dictionary<string, Live>();
        float _next;

        /// <summary>A crew walking back to its truck; the truck drives off once they are all aboard (or after a timeout).</summary>
        class Departure { public readonly List<CrewNpc> Npcs = new List<CrewNpc>(); public GameObject Truck; public float GiveUpAt; }
        static readonly List<Departure> _departing = new List<Departure>();
        public const float WalkBackTimeout = 45f;

        /// <summary>Sub hands still walking back to a truck (any contract).</summary>
        /// <summary>Crews/trucks still on their way out (a truck waits here until its crew has walked back).</summary>
        public static int Departures => _departing.Count;
        public static int DepartingCount { get { int n = 0; foreach (var d in _departing) foreach (var c in d.Npcs) if (c != null) n++; return n; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("SubcontractorSystems");
            DontDestroyOnLoad(go);
            I = go.AddComponent<SubCrews>();
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            TickDepartures();
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            if (!GameSettings.IsCareer && !Subs.IgnoreWorkHours) return;
            PurgeStrayTasks();
            Subs.Tick();
            foreach (var kv in _live) ShareWork(kv.Key, kv.Value);
        }

        static Live L(SubContract k, bool create = true)
        {
            if (k == null) return null;
            if (!_live.TryGetValue(k.Id, out var l) && create) { l = new Live(); _live[k.Id] = l; }
            return l;
        }

        public static string Slot(SubContract k, int i) => $"sub:{k.Id}:{i}";
        public static bool IsSubJob(string jobId) => jobId != null && jobId.StartsWith("sub:");

        /// <summary>Tasks with a sub slot id that no live contract owns (e.g. restored by the crew save) are removed.</summary>
        static void PurgeStrayTasks()
        {
            for (int i = Crew.Tasks.Count - 1; i >= 0; i--)
            {
                var t = Crew.Tasks[i];
                if (!IsSubJob(t.JobId)) continue;
                bool owned = false;
                foreach (var l in _live.Values) if (l.Tasks.Contains(t)) { owned = true; break; }
                if (!owned) Crew.Tasks.RemoveAt(i);
            }
        }

        public static bool Present(SubContract k)
        {
            var l = L(k, false);
            if (l == null) return false;
            l.Npcs.RemoveAll(n => n == null);
            return l.Npcs.Exists(n => n.State != CrewNpc.Phase.Leaving);
        }

        public static IReadOnlyList<CrewTask> TasksOf(SubContract k) { var l = L(k, false); return l != null ? (IReadOnlyList<CrewTask>)l.Tasks : new List<CrewTask>(); }
        public static IReadOnlyList<CrewNpc> NpcsOf(SubContract k) { var l = L(k, false); return l != null ? (IReadOnlyList<CrewNpc>)l.Npcs : new List<CrewNpc>(); }
        public static GameObject TruckOf(SubContract k) => L(k, false)?.Truck;

        static void MakeTasks(SubContract k, Live l, List<CrewTask> tasks)
        {
            int n = Mathf.Max(1, k.CrewSize);
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                t.JobId = Slot(k, i % n);
                Crew.AddTask(t);
                l.Tasks.Add(t);
            }
            l.TasksMade = true;
        }

        /// <summary>The truck pulls up and the crew gets out (work hours).</summary>
        public static void Arrive(SubContract k)
        {
            var l = L(k);
            var info = k.TradeInfo; var co = k.Company;
            // after a load the live task list is empty: a warranty callback must get its fix tasks back, not the original scope
            if (!l.TasksMade) MakeTasks(k, l, k.State == SubState.Callback ? SubScopes.FixTasks(k.Trade, k.Center, k.Radius) : SubScopes.Tasks(k.Trade, k.Center, k.Radius));
            Vector3 park = k.Arrival;
            Vector3 toSite = k.Center - park; toSite.y = 0f;
            float yaw = toSite.sqrMagnitude > 1f ? Mathf.Atan2(toSite.x, toSite.z) * Mathf.Rad2Deg + 90f : 0f;
            yaw = Mathf.Round(yaw / 45f) * 45f;
            if (l.Truck == null && info != null)
            {
                var root = new GameObject($"SubTruck_{co?.Name}");
                root.transform.SetPositionAndRotation(park + Quaternion.Euler(0, yaw, 0) * new Vector3(-2.2f, 0f, 0f), Quaternion.Euler(0, yaw, 0));
                VehicleModels.Build(info.Truck, root.transform, info.Truck == "pickup" || info.Truck == "van");
                SubVisuals.DoorSigns(root.transform, info.Truck, co);
                l.Truck = root;
            }
            if (l.Workers.Count == 0)
            {
                var r = new System.Random((k.Id ?? "x").GetHashCode());
                for (int i = 0; i < Mathf.Max(1, k.CrewSize); i++)
                    l.Workers.Add(new Worker
                    {
                        Uid = $"sub-{k.Id}-{i}", Name = CrewTrades.RandomName(r), Trade = info != null ? info.CrewTrade : "laborer",
                        Skill = Mathf.Clamp(k.Skill, 1, 5), Morale = 0.8f, DailyWage = 0,
                    });
            }
            for (int i = 0; i < l.Workers.Count; i++)
            {
                var w = l.Workers[i];
                w.Skill = Mathf.Clamp(k.Skill, 1, 5);
                w.AssignedJobId = Slot(k, i);
                if (l.Npcs.Exists(n => n != null && n.W == w && n.State != CrewNpc.Phase.Leaving)) continue;
                var npc = CrewNpc.Spawn(w, park + Quaternion.Euler(0, yaw, 0) * new Vector3(0.9f + 0.8f * i, 0f, -1.5f));
                npc.External = true;
                l.Npcs.Add(npc);
            }
        }

        /// <summary>A hand with nothing left in their own slot takes over an unclaimed open task (retagged to their slot).</summary>
        static void ShareWork(string id, Live l)
        {
            var k = Subs.Find(id);
            if (k == null || l.Workers.Count < 2) return;
            foreach (var n in l.Npcs)
            {
                if (n == null || n.State == CrewNpc.Phase.Leaving || n.Task != null || n.Unit != null) continue;
                string mine = n.W.AssignedJobId;
                if (l.Tasks.Exists(t => !t.Done && t.JobId == mine)) continue;
                var take = l.Tasks.Find(t => !t.Done && t.ClaimedBy == null && string.IsNullOrEmpty(t.Blocked) && !l.Npcs.Exists(o => o != null && o.Task == t));
                if (take != null) take.JobId = mine;
            }
        }

        public static bool WorkDone(SubContract k)
        {
            var l = L(k, false);
            return l != null && l.TasksMade && l.Tasks.TrueForAll(t => t.Done);
        }

        /// <summary>Work units the sub's hands finished on their live tasks (since the tasks were made).</summary>
        public static int UnitsOf(SubContract k)
        {
            var l = L(k, false); int n = 0;
            if (l != null) foreach (var t in l.Tasks) n += t.UnitsDone;
            return n;
        }

        public static int MistakesOf(SubContract k)
        {
            var l = L(k, false); int m = 0;
            if (l != null) foreach (var t in l.Tasks) m += t.Mistakes;
            return m;
        }

        public static string Doing(SubContract k)
        {
            var l = L(k, false);
            if (l == null) return "";
            int done = l.Tasks.FindAll(t => t.Done).Count;
            foreach (var n in l.Npcs) if (n != null && n.Unit != null) return $"{n.Doing} ({done}/{l.Tasks.Count} tasks done)";
            foreach (var t in l.Tasks) if (!t.Done && !string.IsNullOrEmpty(t.Blocked)) return $"Waiting: {t.Blocked} ({done}/{l.Tasks.Count})";
            return $"Working ({done}/{l.Tasks.Count} tasks done)";
        }

        /// <summary>End of the day (or of the job): the crew walks to the truck and the truck leaves.</summary>
        public static void SendHome(SubContract k)
        {
            var l = L(k, false);
            if (l == null) return;
            var dep = new Departure { Truck = l.Truck, GiveUpAt = Time.time + WalkBackTimeout };
            Vector3 door = l.Truck != null ? l.Truck.transform.position + l.Truck.transform.right * 1.3f : k.Arrival;
            foreach (var n in l.Npcs)
            {
                if (n == null) continue;
                n.SendHome(door);          // CrewNpc walks there and removes itself
                dep.Npcs.Add(n);
            }
            l.Npcs.Clear();
            l.Truck = null;
            if (dep.Npcs.Count > 0 || dep.Truck != null) _departing.Add(dep);
        }

        /// <summary>Trucks leave once their crew has walked back (CrewNpc destroys itself at the exit point).</summary>
        static void TickDepartures()
        {
            for (int i = _departing.Count - 1; i >= 0; i--)
            {
                var d = _departing[i];
                d.Npcs.RemoveAll(n => n == null);
                if (d.Npcs.Count > 0 && Time.time < d.GiveUpAt) continue;
                DestroyDeparture(d);
                _departing.RemoveAt(i);
            }
        }

        static void DestroyDeparture(Departure d)
        {
            foreach (var n in d.Npcs) if (n != null) { n.gameObject.SetActive(false); Destroy(n.gameObject); }
            d.Npcs.Clear();
            if (d.Truck != null) Destroy(d.Truck);
            d.Truck = null;
        }

        /// <summary>Warranty callback: fresh fix-up tasks for the trade (re-nail loose pieces, re-screw, re-dig, re-sod...).</summary>
        public static void Reopen(SubContract k)
        {
            var l = L(k);
            foreach (var t in l.Tasks) Crew.Tasks.Remove(t);
            l.Tasks.Clear();
            MakeTasks(k, l, SubScopes.FixTasks(k.Trade, k.Center, k.Radius));
        }

        /// <summary>Forgets a contract's people, truck and (unless kept for a later callback) its tasks.</summary>
        public static void Clear(SubContract k, bool keepTasks = false)
        {
            var l = L(k, false);
            if (l == null) return;
            SendHome(k);
            foreach (var t in l.Tasks) Crew.Tasks.Remove(t);
            if (!keepTasks) _live.Remove(k.Id);
            else l.Tasks.RemoveAll(t => true);
            l.Workers.Clear();
            l.TasksMade = false;
        }

        public static void ClearAll()
        {
            foreach (var l in _live.Values)
            {
                foreach (var n in l.Npcs) if (n != null) { n.gameObject.SetActive(false); Destroy(n.gameObject); }
                if (l.Truck != null) Destroy(l.Truck);
                foreach (var t in l.Tasks) Crew.Tasks.Remove(t);
            }
            _live.Clear();
            foreach (var d in _departing) DestroyDeparture(d);
            _departing.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _live.Clear(); _departing.Clear(); }
    }
}
