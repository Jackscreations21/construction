using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Small helpers the crew tasks share: where materials are, leaving real nail heads.</summary>
    public static class CrewWork
    {
        /// <summary>Delivery pile of a material (or the drop point when none is stacked yet).</summary>
        public static Vector3 DeliveryPile(string id, Vector3 fallback)
        {
            foreach (var p in Object.FindObjectsByType<MaterialPile>(FindObjectsSortMode.None))
                if (p != null && p.DefinitionId == id) return p.transform.position;
            if (DeliveryService.I != null) return DeliveryService.I.DropPoint;
            return fallback;
        }

        /// <summary>Closest place to pick a material up: a crew-staged stack near the work, else the delivery pile.</summary>
        public static Vector3 NearestPile(string id, Vector3 near)
        {
            var s = StagedPile.Nearest(id, near);
            if (s != null) return s.transform.position;
            return DeliveryPile(id, near);
        }

        /// <summary>Drives a visible nail head into the face of the piece nearest the worker's hand (same mark the hammer leaves).</summary>
        public static void NailMark(Piece p, Vector3 hand, int k)
        {
            var col = p != null ? p.GetComponent<Collider>() : null;
            if (col == null) return;
            Vector3 target = col.ClosestPoint(hand);
            // spread the nails a little along the piece
            target += p.transform.forward * ((k % 2 == 0 ? 1 : -1) * Mathf.Min(0.05f, p.Size.z * 0.2f));
            Vector3 from = hand;
            if ((target - from).sqrMagnitude < 0.0004f) from = target + (hand - p.transform.position).normalized * 0.3f + Vector3.up * 0.01f;
            var ray = new Ray(from, (target - from).normalized);
            if (col.Raycast(ray, out var hit, 5f)) LumberMarks.NailHead(p, hit);
        }
    }

    /// <summary>Workmanship mistakes the crew made (for the Crew app and the worker's record). The mistakes themselves are
    /// real world state (a loose piece, missing screws, a shallow trench) that the inspection rules find on their own.</summary>
    public static class CrewDefect
    {
        public struct Entry { public string Worker, Note; public Vector3 Where; public int Day; }
        public static readonly List<Entry> Log = new List<Entry>();

        public static void Mark(Piece p, string note, Worker w) => Mark(p != null ? p.transform.position : Vector3.zero, (p != null && p.Def != null ? p.Def.Name + ": " : "") + note, w);

        public static void Mark(Vector3 where, string note, Worker w)
        {
            Log.Add(new Entry { Worker = w != null ? w.Name : "?", Note = note, Where = where, Day = GameClock.I != null ? GameClock.I.Day : 0 });
            if (Log.Count > 200) Log.RemoveAt(0);
            if (w != null) Crew.Info(w).Mistakes++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Log.Clear();
    }

    /// <summary>Footing-phase rule: every trench the crew was told to dig must reach its depth along its whole length,
    /// measured off the real pad (<see cref="SiteGround"/>), within 1".</summary>
    public static class CrewRules
    {
        public const string TrenchRuleId = "crew.trench-depth";

        static IEnumerable<InspectionItem> TrenchDepth()
        {
            if (SiteGround.I == null) yield break;
            foreach (var t in Crew.Tasks)
            {
                if (!(t is DigTask d) || d.UnitsDone == 0 && !d.Done) continue;
                for (int i = 0; i < d.Stations; i++)
                {
                    float short_ = d.GroundAt(i) - d.TargetY;
                    if (short_ <= Units.In(1f)) continue;
                    yield return new InspectionItem($"Trench only {Units.Format(d.Depth - short_)} deep here (plans: {Units.Format(d.Depth)})", d.StationPos(i));
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() =>
            Inspections.Register(TrenchRuleId, InspectionPhase.Footing, "Footing trenches dug to plan depth on undisturbed soil (IRC R403.1.4)", TrenchDepth);
    }

    /// <summary>Material the crew carried from the delivery pile to where it's needed. Still counts as site stock
    /// (Game.Stock); the stack just shows where it is and shortens later trips.</summary>
    public class StagedPile : MonoBehaviour
    {
        [System.Serializable] public class Data { public string id; public int n; public Vector3 pos; public float yaw; }

        public static readonly List<StagedPile> All = new List<StagedPile>();
        public string Id;
        public int Count;
        public float Yaw;
        GameObject _vis; int _shown = -1;

        public static int Total(string id) { int n = 0; foreach (var p in All) if (p != null && p.Id == id) n += p.Count; return n; }

        public static StagedPile Nearest(string id, Vector3 near, float max = 40f)
        {
            StagedPile best = null; float bd = max * max;
            foreach (var p in All)
            {
                if (p == null || p.Id != id || p.Count <= 0) continue;
                float d = (p.transform.position - near).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        public static StagedPile At(Vector3 pos, float yaw, string id)
        {
            foreach (var p in All) if (p != null && p.Id == id && (p.transform.position - pos).sqrMagnitude < 0.25f) return p;
            var go = new GameObject("Staged_" + id);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var s = go.AddComponent<StagedPile>();
            s.Id = id; s.Yaw = yaw;
            return s;
        }

        public void Add(int n) { Count += n; Refresh(); }

        public Data ToData() => new Data { id = Id, n = Count, pos = transform.position, yaw = Yaw };
        public static void Restore(Data d) { if (d != null && d.n > 0) At(d.pos, d.yaw, d.id).Add(d.n); }
        public static void ClearAll() { foreach (var p in All.ToArray()) if (p != null) { p.gameObject.SetActive(false); Destroy(p.gameObject); } All.Clear(); }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            // material used from site stock comes off the staged stacks too
            int stock = Game.Stock.Count(Id), staged = Total(Id);
            if (staged > stock) Count = Mathf.Max(0, Count - (staged - stock));
            if (Count <= 0) { Destroy(gameObject); return; }
            if (Count != _shown) Refresh();
        }

        void Refresh()
        {
            if (Count == _shown) return;
            _shown = Count;
            if (_vis != null) Destroy(_vis);
            var def = Catalog.Get(Id);
            _vis = new GameObject("Stack");
            _vis.transform.SetParent(transform, false);
            if (def == null) return;
            int show = Mathf.Min(Count, def.Category == PieceCategory.Sheet ? 12 : 16);
            float y = 0.04f;
            for (int i = 0; i < show; i++)
            {
                // lumber: rows of 4 on edge-flat; sheets and products flat stacked
                bool lumber = def.Category == PieceCategory.Lumber;
                int col = lumber ? i % 4 : 0;
                if (lumber && col == 0 && i > 0) y += def.Size.y + 0.002f;
                var unit = new GameObject("Unit");
                unit.transform.SetParent(_vis.transform, false);
                unit.transform.localPosition = new Vector3(lumber ? (col - 1.5f) * (def.Size.x + 0.01f) : 0f, y, 0f);
                if (def.IsProduct) unit.transform.localRotation = Quaternion.Euler(90, 0, 0);
                try { ProductModels.Build(def, def.Size, unit.transform); } catch (System.Exception e) { Debug.LogException(e); }
                if (!lumber) y += (def.IsProduct ? def.Size.z : def.Size.y) + 0.002f;
            }
            foreach (var c in _vis.GetComponentsInChildren<Collider>()) Destroy(c);
        }
    }
}
