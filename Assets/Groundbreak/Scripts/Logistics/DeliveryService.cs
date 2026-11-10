using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Supply deliveries: a flatbed truck drives in, drops material piles at DropPoint, adds to Game.Stock, drives away.
    /// Piles near the drop point always mirror what is in Game.Stock.</summary>
    public class DeliveryService : MonoBehaviour
    {
        public static DeliveryService I;

        public const float StandardDelay = 90f, RushDelay = 20f;
        public const int RushFee = 50;
        const float TruckSpeed = 12f, SpawnDistance = 60f;

        public class PendingDelivery
        {
            public Dictionary<string, int> Items;
            public bool Rush;
            public float ArriveTime;           // Time.time when the truck reaches the drop point (approx.)
            public bool TruckOnSite;
            public float ETA => Mathf.Max(0f, ArriveTime - Time.time);
            public string Summary
            {
                get
                {
                    var sb = new StringBuilder();
                    foreach (var kv in Items)
                    {
                        var d = Catalog.Get(kv.Key);
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(kv.Value).Append(" x ").Append(d != null ? d.Name : kv.Key);
                    }
                    return sb.ToString();
                }
            }
        }

        public readonly List<PendingDelivery> Pending = new List<PendingDelivery>();

        public Vector3 DropPoint { get; private set; } = new Vector3(6f, 0f, -2f);

        GameObject _marker;
        readonly Dictionary<string, GameObject> _piles = new Dictionary<string, GameObject>();
        readonly Dictionary<string, Vector3> _pilePos = new Dictionary<string, Vector3>();
        int _nextSlot;
        bool _pilesDirty;
        Stockpile _subscribed;

        void Awake() { I = this; }

        void Start()
        {
            _subscribed = Game.Stock;
            if (_subscribed != null) _subscribed.Changed += MarkDirty;
            UpdateMarker();
        }

        void OnDestroy()
        {
            if (_subscribed != null) _subscribed.Changed -= MarkDirty;
            if (I == this) I = null;
        }

        void MarkDirty() => _pilesDirty = true;

        void LateUpdate()
        {
            if (_pilesDirty) { _pilesDirty = false; RebuildPiles(); }
        }

        // ---------------- Drop point ----------------

        public void SetDropPoint(Vector3 p)
        {
            DropPoint = new Vector3(p.x, 0f, p.z);
            UpdateMarker();
            Game.Toast("Delivery drop point set");
        }

        void UpdateMarker()
        {
            if (_marker == null)
            {
                _marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _marker.name = "DropMarker";
                var col = _marker.GetComponent<Collider>(); if (col != null) Destroy(col);
                _marker.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Get(new Color(1f, 0.5f, 0.05f));
                _marker.transform.localScale = new Vector3(1.5f, 0.01f, 1.5f); // cylinder is 2 m tall -> 2 cm disc
            }
            _marker.transform.position = DropPoint + Vector3.up * 0.02f;
        }

        // ---------------- Orders ----------------

        /// <summary>Wallet has already been charged by the caller. Cart values are purchase quantities (boxes for consumables).</summary>
        public void Order(Dictionary<string, int> cart, bool rush)
        {
            if (cart == null) return;
            var items = new Dictionary<string, int>();
            foreach (var kv in cart) if (kv.Value > 0) items[kv.Key] = kv.Value;
            if (items.Count == 0) return;

            float delay = rush ? RushDelay : StandardDelay;
            var d = new PendingDelivery { Items = items, Rush = rush, ArriveTime = Time.time + delay };
            Pending.Add(d);
            StartCoroutine(RunDelivery(d, delay));
            Game.Toast(rush ? "Rush order placed: ~20 s" : "Order placed: ~90 s");
        }

        IEnumerator RunDelivery(PendingDelivery d, float delay)
        {
            float travel = SpawnDistance / TruckSpeed;
            yield return new WaitForSeconds(Mathf.Max(0f, delay - travel));

            Vector3 drop = DropPoint;
            Vector3 dir = new Vector3(drop.x, 0f, drop.z);
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.right;
            Vector3 side = Vector3.Cross(Vector3.up, dir) * 3f; // park beside the drop point
            Vector3 start = drop + dir * SpawnDistance + side;
            Vector3 stop = drop + side;

            var truck = BuildTruck();
            truck.transform.position = start;
            d.TruckOnSite = true;
            yield return Drive(truck, stop);

            yield return new WaitForSeconds(1.5f);
            foreach (var kv in d.Items)
            {
                var def = Catalog.Get(kv.Key);
                int units = def != null && def.Category == PieceCategory.Consumable ? kv.Value * def.UnitsPerPurchase : kv.Value;
                Game.Stock.Add(kv.Key, units);
            }
            Pending.Remove(d);
            Game.Toast("Delivery arrived: " + d.Summary);
            yield return new WaitForSeconds(1.5f);

            yield return Drive(truck, stop - dir * SpawnDistance);
            if (truck != null) Destroy(truck);
        }

        IEnumerator Drive(GameObject truck, Vector3 target)
        {
            while (truck != null && (truck.transform.position - target).sqrMagnitude > 0.01f)
            {
                Vector3 p = truck.transform.position;
                Vector3 to = target - p; to.y = 0f;
                if (to.sqrMagnitude > 0.0001f) truck.transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
                truck.transform.position = Vector3.MoveTowards(p, target, TruckSpeed * Time.deltaTime);
                yield return null;
            }
        }

        static GameObject BuildTruck()
        {
            var root = new GameObject("DeliveryTruck");
            ProductModels.BuildFlatbedTruck(root.transform);
            return root;
        }

        static GameObject Part(GameObject parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Color c)
        {
            var g = GameObject.CreatePrimitive(type);
            var col = g.GetComponent<Collider>(); if (col != null) Destroy(col);
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = MaterialLibrary.Get(c);
            return g;
        }

        // ---------------- Piles ----------------

        /// <summary>Creates piles for whatever Game.Stock already holds, near the drop point.</summary>
        public static void SpawnInitialPiles()
        {
            if (I != null) I.RebuildPiles();
        }

        Vector3 SlotPosition(string id)
        {
            if (_pilePos.TryGetValue(id, out var p)) return p;
            int idx = _nextSlot++;
            // Rows of 5 piles, 2.2 m apart across, 5.4 m apart along (fits 16' lumber and garage doors); beside the marker.
            p = DropPoint + new Vector3(1.8f + (idx % 5) * 2.2f, 0f, (idx / 5) * 5.4f);
            _pilePos[id] = p;
            return p;
        }

        public void RebuildPiles()
        {
            if (Game.Stock == null) return;
            var seen = new HashSet<string>();
            foreach (var kv in Game.Stock.Entries)
            {
                if (kv.Value <= 0) continue;
                var def = Catalog.Get(kv.Key);
                if (def == null) continue;
                seen.Add(kv.Key);
                Vector3 pos = SlotPosition(kv.Key);
                if (_piles.TryGetValue(kv.Key, out var old) && old != null) Destroy(old);
                _piles[kv.Key] = BuildPile(def, kv.Value, pos);
            }
            var remove = new List<string>();
            foreach (var kv in _piles) if (!seen.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                if (_piles[id] != null) Destroy(_piles[id]);
                _piles.Remove(id);
                _pilePos.Remove(id);
            }
        }

        static GameObject Holder(GameObject root, Vector3 localPos, Quaternion localRot)
        {
            var h = new GameObject("Unit");
            h.transform.SetParent(root.transform, false);
            h.transform.localPosition = localPos;
            h.transform.localRotation = localRot;
            return h;
        }

        /// <summary>Stack of real pieces: lumber in rows on stickers, sheets flat, products on a pallet.</summary>
        static GameObject BuildPile(PieceDefinition def, int count, Vector3 pos)
        {
            var root = new GameObject("Pile_" + def.Id);
            root.transform.position = pos;
            var pile = root.AddComponent<MaterialPile>();
            pile.DefinitionId = def.Id;
            var sticker = Catalog.Get("2x4x8");
            const float gap = 0.003f;
            float y = 0f;

            if (def.Category == PieceCategory.Masonry)
            {
                // pallet, then a cube of units: lengths along X, widths along Z, courses up Y
                foreach (var x in new[] { -0.45f, 0f, 0.45f })
                {
                    var h = Holder(root, new Vector3(x, 0.045f, 0f), Quaternion.Euler(0f, 0f, 90f));
                    ProductModels.Build(sticker, new Vector3(sticker.Size.x, sticker.Size.y, 1.05f), h.transform);
                }
                y = 0.09f;
                float L = def.Size.z, W = def.Size.x, H = def.Size.y;
                int alongX = Mathf.Max(1, Mathf.FloorToInt(1.0f / (L + 0.003f)));
                int alongZ = Mathf.Max(1, Mathf.FloorToInt(1.0f / (W + 0.003f)));
                int perCourse = alongX * alongZ;
                int maxCourses = Mathf.Max(1, Mathf.FloorToInt(0.9f / (H + 0.002f)));
                int shown = Mathf.Min(count, perCourse * maxCourses);
                float spanX = alongX * (L + 0.003f), spanZ = alongZ * (W + 0.003f);
                for (int n = 0; n < shown; n++)
                {
                    int course = n / perCourse, k = n % perCourse;
                    int ix = k % alongX, iz = k / alongX;
                    var lp = new Vector3(-spanX * 0.5f + (ix + 0.5f) * (L + 0.003f), y + course * (H + 0.002f) + H * 0.5f,
                                         -spanZ * 0.5f + (iz + 0.5f) * (W + 0.003f));
                    var h = Holder(root, lp, Quaternion.Euler(0f, 90f, 0f));
                    ProductModels.Build(def, def.Size, h.transform);
                }
            }
            else if (def.Category == PieceCategory.Rebar)
            {
                // bundle on two stickers, tight rows
                float dun = sticker.Size.x;
                foreach (var z in new[] { -0.3f, 0.3f })
                {
                    var h = Holder(root, new Vector3(0f, dun * 0.5f, z * def.Size.z), Quaternion.Euler(0f, 90f, 90f));
                    ProductModels.Build(sticker, new Vector3(sticker.Size.x, sticker.Size.y, 0.5f), h.transform);
                }
                y = dun;
                int shown = Mathf.Min(count, 60), perRow = 10;
                for (int n = 0; n < shown; n++)
                {
                    int row = n / perRow, i = n % perRow;
                    float d = def.Size.x;
                    var h = Holder(root, new Vector3((i - (perRow - 1) * 0.5f) * (d + 0.004f) + (row % 2) * d * 0.5f, y + row * d * 0.9f + d * 0.5f, 0f), Quaternion.identity);
                    ProductModels.Build(def, def.Size, h.transform);
                }
            }
            else if (def.Category == PieceCategory.Lumber || def.Category == PieceCategory.Sheet)
            {
                // two 2x4 stickers (dunnage) across the stack
                float dun = sticker.Size.x; // 3.5" tall when stood on edge
                float stackW = def.Category == PieceCategory.Lumber ? def.Size.x * Mathf.Min(count, 6) + 0.02f * 5 : def.Size.x;
                foreach (var z in new[] { -0.33f, 0.33f })
                {
                    var h = Holder(root, new Vector3(0f, dun * 0.5f, z * def.Size.z), Quaternion.Euler(0f, 90f, 90f));
                    ProductModels.Build(sticker, new Vector3(sticker.Size.x, sticker.Size.y, stackW + 0.15f), h.transform);
                }
                y = dun;
                if (def.Category == PieceCategory.Lumber)
                {
                    int remaining = count, layers = 0;
                    while (remaining > 0 && layers < 12)
                    {
                        int inRow = Mathf.Min(remaining, 6);
                        float rowW = inRow * def.Size.x + (inRow - 1) * 0.02f;
                        for (int i = 0; i < inRow; i++)
                        {
                            float x = -rowW * 0.5f + def.Size.x * 0.5f + i * (def.Size.x + 0.02f);
                            var h = Holder(root, new Vector3(x, y + def.Size.y * 0.5f, 0f), Quaternion.identity);
                            ProductModels.Build(def, def.Size, h.transform);
                        }
                        y += def.Size.y + gap; remaining -= inRow; layers++;
                    }
                }
                else
                {
                    int layers = Mathf.Clamp(count, 1, 25);
                    for (int i = 0; i < layers; i++)
                    {
                        // slight random-looking offset so the stack doesn't read as one block
                        float jx = ((i * 37) % 7 - 3) * 0.002f, jz = ((i * 53) % 7 - 3) * 0.003f;
                        var h = Holder(root, new Vector3(jx, y + def.Size.y * 0.5f, jz), Quaternion.identity);
                        ProductModels.Build(def, def.Size, h.transform);
                        y += def.Size.y + 0.0005f;
                    }
                }
            }
            else if (def.Category == PieceCategory.Consumable)
            {
                int boxes = Mathf.Clamp(Mathf.CeilToInt(count / (float)Mathf.Max(1, def.UnitsPerPurchase)), 1, 6);
                for (int i = 0; i < boxes; i++)
                {
                    Part(root, PrimitiveType.Cube, new Vector3(0f, y + 0.1f, 0f), new Vector3(0.3f, 0.2f, 0.4f), new Color(0.62f, 0.47f, 0.30f));
                    y += 0.2f + gap;
                }
            }
            else
            {
                // pallet: deck boards on three runners
                foreach (var x in new[] { -0.5f, 0f, 0.5f })
                {
                    var h = Holder(root, new Vector3(x, 0.045f, 0f), Quaternion.Euler(0f, 0f, 90f));
                    ProductModels.Build(sticker, new Vector3(sticker.Size.x, sticker.Size.y, 1.2f), h.transform);
                }
                for (int i = 0; i < 7; i++)
                {
                    var h = Holder(root, new Vector3(0f, 0.09f + 0.01f, -0.54f + i * 0.18f), Quaternion.Euler(0f, 90f, 0f));
                    ProductModels.Build(sticker, new Vector3(sticker.Size.x, Units.In(0.75f), 1.2f), h.transform);
                }
                y = 0.11f;
                bool lieFlat = def.Category == PieceCategory.GarageDoor && def.Size.x > 2f;
                int units = Mathf.Clamp(count, 1, 4);
                for (int i = 0; i < units; i++)
                {
                    Vector3 lp; Quaternion lr;
                    if (lieFlat)
                    {
                        lr = Quaternion.Euler(90f, 0f, 0f); // height -> along Z, depth -> up
                        lp = new Vector3(0f, y + def.Size.z * 0.5f + i * (def.Size.z + 0.01f), 0f);
                    }
                    else
                    {
                        lr = Quaternion.identity;
                        float step = def.Size.z + 0.05f;
                        lp = new Vector3(0f, y + def.Size.y * 0.5f, (i - (units - 1) * 0.5f) * step);
                    }
                    var h = Holder(root, lp, lr);
                    ProductModels.Build(def, def.Size, h.transform);
                }
            }

            // Collider around everything that was built
            var rends = root.GetComponentsInChildren<Renderer>();
            var bc = root.AddComponent<BoxCollider>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                bc.center = b.center - pos;
                bc.size = b.size;
            }
            else { bc.center = new Vector3(0f, 0.25f, 0f); bc.size = new Vector3(0.5f, 0.5f, 0.5f); }
            return root;
        }
    }
}
