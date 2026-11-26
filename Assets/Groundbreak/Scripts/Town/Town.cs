using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum LotUse { Customer, PlayerHome, Store, Free, BuildSite, Company }

    /// <summary>One parcel in town. All positions are world space, ground level (y = 0). See Docs/TOWN.md.</summary>
    public class TownLot
    {
        public string Id;            // stable, e.g. "main-108"
        public string Address;       // "108 Main St"
        public string Street; public int Number;
        public LotUse Use;
        public string Owner;         // who claimed a Free lot (feature tag), null otherwise
        public string Resident;      // customer / business name ("" for empty lots)
        public Rect Rect;            // world XZ (Rect.y is world Z)
        public Vector3 Front;        // unit vector from the lot toward its street
        public Vector3 FrontMid;     // midpoint of the lot line on the street side
        public Vector3 Parking;      // where a vehicle parks: on the driveway (houses), in the lot (store), at the curb (empty lots)
        public float ParkingYaw;     // heading (degrees) of a vehicle parked there
        public Vector3 FrontDoor;    // standing spot just outside the front door (lot centre for empty lots)
        public TownHouse House;      // null for empty lots / the build site / the store

        public Vector3 Center => new Vector3(Rect.center.x, 0f, Rect.center.y);
        public float Frontage => Mathf.Abs(Front.x) > 0.5f ? Rect.height : Rect.width;
        public float Depth => Mathf.Abs(Front.x) > 0.5f ? Rect.width : Rect.height;
        public float AreaSqFt => Rect.width * Rect.height / (Units.Foot * Units.Foot);
        /// <summary>Rotation whose forward (+Z) points at the street; houses are built in this frame.</summary>
        public Quaternion Rotation => Quaternion.LookRotation(Front, Vector3.up);
        /// <summary>Lot-local point: x = metres to the right (standing in the lot facing the street), depth = metres back from the street lot line.</summary>
        public Vector3 Local(float x, float depth) => FrontMid + Rotation * new Vector3(x, 0f, -depth);
        public bool Contains(Vector3 p) => Rect.Contains(new Vector2(p.x, p.z));
        public bool IsFree => Use == LotUse.Free;
    }

    public class TownStreet
    {
        public string Name;
        public Vector3 A, B;          // centreline ends (axis aligned)
        public bool EastWest => Mathf.Abs(A.z - B.z) < 0.01f;
    }

    /// <summary>
    /// The town around the build site: streets, addressed lots, customer houses, the hardware store, the player's home.
    /// Public API for the other business features (Docs/TOWN.md). The layout is fixed and deterministic, so lot ids and
    /// addresses are stable across saves; only lot claims (<see cref="Claim"/>) and <see cref="HQPosition"/> are saved.
    /// The layout (lots, addresses) is always available; the 3D town exists only after <see cref="Ensure"/> (Career mode).
    /// </summary>
    public static class Town
    {
        // ---- street cross-section (metres), real US residential: 30' pavement, 6" curb, 4' planting strip, 5' sidewalk
        public const float RoadHalf = 15f * Units.Foot;
        public const float CurbWidth = 0.15f, CurbHeight = 0.14f;
        public const float StripWidth = 4f * Units.Foot, SidewalkWidth = 5f * Units.Foot;
        /// <summary>Street centreline to the private lot line.</summary>
        public const float RowHalf = 7.5f;
        public const float LotFrontage = 20f, LotDepth = 38f;
        public const float DrivewayWidth = 12f * Units.Foot;

        // ---- layout: Main St and Cedar Ln run east-west, Oak St and Maple Ave run north-south. The build pad (origin) is north of Main St.
        public const float MainZ = -60f, CedarZ = -170f, OakX = -90f, MapleX = 90f, StreetEnd = 230f, NorthEnd = 150f;
        /// <summary>The existing build site (pad, Grade lot, career lots): nothing of the town is built in here.</summary>
        public static readonly Rect BuildSiteRect = Rect.MinMaxRect(-55f, MainZ + RowHalf, 55f, 95f);

        static readonly List<TownLot> _lots = new List<TownLot>();
        static readonly List<TownStreet> _streets = new List<TownStreet>();
        static GameObject _root;
        static Vector3? _hq;

        /// <summary>Raised after the 3D town was built / destroyed, and when a lot's use changes.</summary>
        public static event Action Built, Destroyed, LotsChanged;

        public static IReadOnlyList<TownLot> Lots { get { Layout(); return _lots; } }
        public static IReadOnlyList<TownStreet> Streets { get { Layout(); return _streets; } }
        /// <summary>Customer houses (the lots repair and remodel jobs happen at).</summary>
        public static IEnumerable<TownLot> Addresses { get { foreach (var l in Lots) if (l.Use == LotUse.Customer) yield return l; } }
        /// <summary>Empty lots nobody has claimed yet (later stages buy/claim these with <see cref="Claim"/>).</summary>
        public static IEnumerable<TownLot> FreeLots { get { foreach (var l in Lots) if (l.Use == LotUse.Free) yield return l; } }
        public static TownLot Home => Find("main-108");
        public static TownLot Store => Find("main-201");
        public static TownLot BuildSite => Find("main-101");
        public static bool Exists => _root != null;
        public static Transform Root => _root != null ? _root.transform : null;

        /// <summary>Where the company is based: the player's home driveway until a feature moves it (e.g. the warehouse).</summary>
        public static Vector3 HQPosition
        {
            get => _hq ?? Home.Parking;
            set { _hq = value; LotsChanged?.Invoke(); }
        }

        /// <summary>The lot containing a world point, or null (streets, outside town).</summary>
        public static TownLot LotAt(Vector3 p)
        {
            foreach (var l in Lots) if (l.Contains(p)) return l;
            return null;
        }

        /// <summary>Find by lot id ("main-108") or address ("108 Main St"), case-insensitive.</summary>
        public static TownLot Find(string idOrAddress)
        {
            if (string.IsNullOrEmpty(idOrAddress)) return null;
            foreach (var l in Lots)
                if (string.Equals(l.Id, idOrAddress, StringComparison.OrdinalIgnoreCase) || string.Equals(l.Address, idOrAddress, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        /// <summary>Driveway / parking spot for an address or lot id. False when unknown.</summary>
        public static bool TryGetParking(string idOrAddress, out Vector3 pos, out float yaw)
        {
            var l = Find(idOrAddress);
            pos = l != null ? l.Parking : Vector3.zero; yaw = l != null ? l.ParkingYaw : 0f;
            return l != null;
        }

        /// <summary>Take a Free lot for a feature (warehouse, spec house...). Saved. False if it is not free.</summary>
        public static bool Claim(TownLot lot, LotUse use, string owner)
        {
            if (lot == null || lot.Use != LotUse.Free || use == LotUse.Free) return false;
            lot.Use = use; lot.Owner = owner;
            LotsChanged?.Invoke();
            return true;
        }

        /// <summary>Give a claimed lot back (only lots that were claimed with <see cref="Claim"/>).</summary>
        public static void Release(TownLot lot)
        {
            if (lot == null || lot.Owner == null) return;
            lot.Use = LotUse.Free; lot.Owner = null;
            LotsChanged?.Invoke();
        }

        /// <summary>Closest point on a street centreline (for navigation / spawning traffic-side things).</summary>
        public static Vector3 NearestRoadPoint(Vector3 p)
        {
            Vector3 best = p; float bd = float.MaxValue;
            foreach (var s in Streets)
            {
                Vector3 ab = s.B - s.A;
                float t = Mathf.Clamp01(Vector3.Dot(p - s.A, ab) / ab.sqrMagnitude);
                Vector3 q = s.A + ab * t; q.y = 0f;
                float d = (new Vector3(p.x, 0, p.z) - q).sqrMagnitude;
                if (d < bd) { bd = d; best = q; }
            }
            return best;
        }

        /// <summary>True on street pavement (between the curbs).</summary>
        public static bool OnRoad(Vector3 p)
        {
            Vector3 q = NearestRoadPoint(p);
            return Mathf.Abs(q.x - p.x) <= RoadHalf && Mathf.Abs(q.z - p.z) <= RoadHalf;
        }

        /// <summary>Builds the 3D town if it is not there yet (cheap to call). Career mode calls this on a new game / load.</summary>
        public static void Ensure()
        {
            if (_root != null) return;
            Layout();
            _root = new GameObject("Town");
            TownBuilder.Build(_root.transform);
            Built?.Invoke();
        }

        public static void Destroy()
        {
            if (_root == null) return;
            foreach (var l in _lots) l.House = null;
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
            _root = null;
            Destroyed?.Invoke();
        }

        // ------------------------------------------------------------------ layout

        static readonly string[] Residents =
        {
            "Maria Alvarez", "Tom & Jenny Becker", "Darnell Brooks", "Linh Tran", "The Kowalskis", "Ruth Feldman", "Marcus Hill", "Aisha Rahman",
            "Bill Nakamura", "The O'Connors", "Sofia Rossi", "Hank Lindqvist", "Grace Okoye", "Pete Sandoval", "Yuki Tanaka", "The Whitfields",
            "Carmen Delgado", "Earl Jennings", "Nadia Petrov", "Luis Mendoza",
        };

        static void Layout()
        {
            if (_lots.Count > 0) return;
            _streets.Add(new TownStreet { Name = "Main St", A = new Vector3(-StreetEnd, 0, MainZ), B = new Vector3(StreetEnd, 0, MainZ) });
            _streets.Add(new TownStreet { Name = "Cedar Ln", A = new Vector3(-StreetEnd, 0, CedarZ), B = new Vector3(StreetEnd, 0, CedarZ) });
            _streets.Add(new TownStreet { Name = "Oak St", A = new Vector3(OakX, 0, -StreetEnd), B = new Vector3(OakX, 0, NorthEnd) });
            _streets.Add(new TownStreet { Name = "Maple Ave", A = new Vector3(MapleX, 0, -StreetEnd), B = new Vector3(MapleX, 0, NorthEnd) });

            int res = 0;
            string NextResident() => Residents[res++ % Residents.Length];
            float d = LotDepth, f = LotFrontage;

            // The build site: north side of Main St around the origin (pad + career lots). Parking at the curb on Main St.
            Add("main-101", "Main St", 101, LotUse.BuildSite, BuildSiteRect, Vector3.back, "Your build site");

            // Main St, south side, between Oak and Maple: 8 house lots (108 is the player's home).
            for (int i = 0; i < 8; i++)
            {
                float cx = -70f + i * f; int num = 102 + i * 2;
                var r = Rect.MinMaxRect(cx - f / 2, MainZ - RowHalf - d, cx + f / 2, MainZ - RowHalf);
                bool home = num == 108;
                Add("main-" + num, "Main St", num, home ? LotUse.PlayerHome : LotUse.Customer, r, Vector3.forward, home ? "Home" : NextResident());
            }
            // Cedar Ln, north side: 6 houses + 2 empty lots at the east end.
            for (int i = 0; i < 8; i++)
            {
                float cx = -70f + i * f; int num = 101 + i * 2;
                var r = Rect.MinMaxRect(cx - f / 2, CedarZ + RowHalf, cx + f / 2, CedarZ + RowHalf + d);
                Add("cedar-" + num, "Cedar Ln", num, i < 6 ? LotUse.Customer : LotUse.Free, r, Vector3.back, i < 6 ? NextResident() : "");
            }
            // Cedar Ln, south side: 8 empty lots.
            for (int i = 0; i < 8; i++)
            {
                float cx = -70f + i * f; int num = 102 + i * 2;
                var r = Rect.MinMaxRect(cx - f / 2, CedarZ - RowHalf - d, cx + f / 2, CedarZ - RowHalf);
                Add("cedar-" + num, "Cedar Ln", num, LotUse.Free, r, Vector3.forward, "");
            }
            // Oak St, west side and Maple Ave, east side, between Cedar and Main: 2 houses + 2 empty lots each.
            for (int i = 0; i < 4; i++)
            {
                float cz = -150f + i * f; int num = 201 + i * 2;
                var ro = Rect.MinMaxRect(OakX - RowHalf - d, cz - f / 2, OakX - RowHalf, cz + f / 2);
                Add("oak-" + num, "Oak St", num, i >= 2 ? LotUse.Customer : LotUse.Free, ro, Vector3.right, i >= 2 ? NextResident() : "");
                var rm = Rect.MinMaxRect(MapleX + RowHalf, cz - f / 2, MapleX + RowHalf + d, cz + f / 2);
                Add("maple-" + (num + 1), "Maple Ave", num + 1, i >= 2 ? LotUse.Customer : LotUse.Free, rm, Vector3.left, i >= 2 ? NextResident() : "");
            }
            // Hardware store: Main St north side, west of Oak St (commercial lot with a parking lot in front).
            Add("main-201", "Main St", 201, LotUse.Store, Rect.MinMaxRect(-152f, MainZ + RowHalf, -100f, -8f), Vector3.back, "Hartley's Hardware & Lumber");
            // Big tracts for the warehouse / yard and for subdivisions (later stages).
            Add("main-301", "Main St", 301, LotUse.Free, Rect.MinMaxRect(-222f, MainZ + RowHalf, -160f, 20f), Vector3.back, "");       // ~1.2 acre commercial
            Add("main-121", "Main St", 121, LotUse.Free, Rect.MinMaxRect(100f, MainZ + RowHalf, 222f, 40f), Vector3.back, "");         // ~2.8 acre tract
            Add("maple-300", "Maple Ave", 300, LotUse.Free, Rect.MinMaxRect(MapleX + RowHalf, 48f, 222f, 145f), Vector3.left, "");     // ~3 acre tract
            Add("oak-301", "Oak St", 301, LotUse.Free, Rect.MinMaxRect(-222f, 30f, OakX - RowHalf, 145f), Vector3.right, "");           // ~3.5 acre tract
        }

        static void Add(string id, string street, int number, LotUse use, Rect rect, Vector3 front, string resident)
        {
            var l = new TownLot { Id = id, Street = street, Number = number, Address = number + " " + street, Use = use, Rect = rect, Front = front, Resident = resident };
            Vector3 c = l.Center;
            float half = (Mathf.Abs(front.x) > 0.5f ? rect.width : rect.height) / 2f;
            l.FrontMid = c + front * half;
            switch (use)
            {
                case LotUse.Customer:
                case LotUse.PlayerHome:
                    // driveway along the right side of the lot (seen from the lot, facing the street), 12' wide, 1 m off the lot line
                    float dx = LotFrontage / 2f - 1f - DrivewayWidth / 2f;
                    l.Parking = l.Local(dx, 6.5f);
                    l.ParkingYaw = Quaternion.LookRotation(-front).eulerAngles.y;     // nose toward the house
                    l.FrontDoor = l.Local(TownHouse.FrontDoorX, TownHouse.Setback - 1.2f);
                    break;
                case LotUse.Store:
                    l.Parking = l.Local(-8f, 9f);
                    l.ParkingYaw = Quaternion.LookRotation(-front).eulerAngles.y;
                    l.FrontDoor = l.Local(0f, 17f);
                    break;
                default:
                    // at the curb in front of the lot, on the lot's side of the street, heading with traffic (right-hand side)
                    l.Parking = l.FrontMid + front * (RowHalf - RoadHalf + 1.3f);
                    l.ParkingYaw = Quaternion.LookRotation(Quaternion.Euler(0, 90, 0) * front).eulerAngles.y;
                    l.FrontDoor = l.FrontMid - front * 3f;
                    break;
            }
            _lots.Add(l);
        }

        // ------------------------------------------------------------------ save (claims + HQ only; the layout is deterministic)

        [Serializable] class ClaimData { public string id; public int use; public string owner; }
        [Serializable] class Data { public List<ClaimData> claims = new List<ClaimData>(); public bool hasHq; public Vector3 hq; }

        static string Save()
        {
            if (!Exists) return "";
            var d = new Data { hasHq = _hq.HasValue, hq = _hq ?? Vector3.zero };
            foreach (var l in _lots) if (l.Owner != null) d.claims.Add(new ClaimData { id = l.Id, use = (int)l.Use, owner = l.Owner });
            return JsonUtility.ToJson(d);
        }

        static void Load(string json)
        {
            Layout();
            foreach (var l in _lots) if (l.Owner != null) { l.Use = LotUse.Free; l.Owner = null; }
            _hq = null;
            if (string.IsNullOrEmpty(json))
            {
                // new game or a save without a town: Free Build has no town; a new career builds it in HandymanStart
                if (!GameSettings.IsCareer) Destroy();
                LotsChanged?.Invoke();
                return;
            }
            var d = JsonUtility.FromJson<Data>(json);
            foreach (var c in d.claims) { var l = Find(c.id); if (l != null && l.Use == LotUse.Free) { l.Use = (LotUse)c.use; l.Owner = c.owner; } }
            if (d.hasHq) _hq = d.hq;
            Ensure();
            LotsChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _lots.Clear(); _streets.Clear(); _root = null; _hq = null; Built = null; Destroyed = null; LotsChanged = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() { SaveSystem.RegisterExtension("town", Save, Load); }
    }
}
