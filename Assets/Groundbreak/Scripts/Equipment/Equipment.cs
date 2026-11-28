using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One operable machine the company can rent or buy. Prices are typical 2026 US figures.</summary>
    public class EquipmentType
    {
        public string Id, Name, Model, Specs;
        public int RentPerDay, RentPerWeek, PriceNew, PriceUsed;   // PriceNew 0 = bought at the Dealership (fleet vehicle)
        public int InsuranceMaintPerDay;
        public string FleetTypeId;                                  // owning this fleet vehicle (Company.Vehicles) = owning the machine
        public bool OnTrailer = true;                               // delivered on an equipment trailer (the boom truck drives in)
        public Func<Vector3, float, Machine> Spawn;
    }

    /// <summary>
    /// Heavy equipment manager: catalog, rentals (by the day), purchases, trailer delivery to where the player is working,
    /// returns, daily costs (rental + insurance/maintenance via Company.RegisterDailyCost), save key "equipment", and the
    /// on-foot "E: climb in" prompt. Career: from the Contractor stage. Free Build: everything, free.
    /// </summary>
    public class Equipment : MonoBehaviour
    {
        public static Equipment I;
        public const int DeliveryFee = 175;          // each way, per machine (lowboy / tag trailer)
        public static readonly List<EquipmentType> Catalog = new List<EquipmentType>();
        /// <summary>Machines bought outright that are not fleet vehicles (mini excavator, boom lift).</summary>
        public static readonly HashSet<string> Owned = new HashSet<string>();
        /// <summary>Active rentals: type id → days billed so far.</summary>
        public static readonly Dictionary<string, int> Rentals = new Dictionary<string, int>();
        public static string Prompt { get; private set; }
        static string _msg = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { I = null; Owned.Clear(); Rentals.Clear(); Prompt = null; _registered = false; }
        static bool _registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            BuildCatalog();
            SaveSystem.RegisterExtension("equipment", Save, Load);
            SaveSystem.BeforeLoad -= LeaveMachinesBeforeLoad; SaveSystem.BeforeLoad += LeaveMachinesBeforeLoad;
            Tablet.RegisterApp("Equipment", DrawApp);
            Tablet.AddHelp("Equipment app (tablet) - rent or buy a mini excavator, skid steer, boom lift or crane truck; delivered on a trailer. Walk up and press E to climb in.");
            Tablet.AddHelp("Machines: W/S/A/D travel, arrows swing + boom, I/K stick/telescope, J/L bucket curl/dump, Y/H hoist, F action, B blade/attachment/outriggers, V camera, E climb out");
            if (!_registered)
            {
                _registered = true;
                Company.RegisterDailyCost("Equipment rental", RentalCostPerDay);
                Company.RegisterDailyCost("Equipment insurance & maintenance", OwnedCostPerDay);
                Company.DayStarted += d => { foreach (var k in new List<string>(Rentals.Keys)) Rentals[k]++; };
            }
            var go = new GameObject("Equipment");
            DontDestroyOnLoad(go);
            I = go.AddComponent<Equipment>();
        }

        static void BuildCatalog()
        {
            Catalog.Clear();
            Catalog.Add(new EquipmentType
            {
                Id = "miniex", Name = "Mini excavator", Model = "3.5-ton class, 40 hp, 24\" bucket, backfill blade, canopy",
                Specs = "Dig depth 11' 2\" · reach 18' 6\" · bucket 0.14 cu yd · 7,700 lb · 5' 7\" wide rubber tracks",
                RentPerDay = 375, RentPerWeek = 1150, PriceNew = 78000, PriceUsed = 46000, InsuranceMaintPerDay = 13,
                Spawn = (p, y) => Machine.Spawn<MiniExcavator>(p, y),
            });
            Catalog.Add(new EquipmentType
            {
                Id = "skidsteer", Name = "Skid steer", Model = "74 hp, 72\" bucket + 48\" pallet forks (quick-attach)",
                Specs = "Rated operating capacity 2,690 lb · lift height 10' 4\" · bucket 0.5 cu yd · 7.4 mph",
                RentPerDay = 325, RentPerWeek = 1000, PriceNew = 0, PriceUsed = 0, FleetTypeId = "skidsteer",
                Spawn = (p, y) => Machine.Spawn<SkidSteer>(p, y),
            });
            Catalog.Add(new EquipmentType
            {
                Id = "boomlift", Name = "Telescopic boom lift", Model = "40' straight boom, 4WD, 6' platform, 500 lb capacity",
                Specs = "Platform height 40' · working height 46' · horizontal reach 34' 6\" · 360° turntable · 15,000 lb",
                RentPerDay = 425, RentPerWeek = 1250, PriceNew = 98000, PriceUsed = 48000, InsuranceMaintPerDay = 15,
                Spawn = (p, y) => Machine.Spawn<BoomLift>(p, y),
            });
            Catalog.Add(new EquipmentType
            {
                Id = "boom", Name = "Crane truck (boom truck)", Model = "Tandem-axle, 20' bed, telescoping crane 25'-70', outriggers",
                Specs = "Max 24,000 lb at 10' radius · 3,900 lb at 50' · 2,100 lb at 66' · load moment indicator",
                RentPerDay = 1150, RentPerWeek = 4200, PriceNew = 0, PriceUsed = 0, FleetTypeId = "boom", OnTrailer = false,
                Spawn = (p, y) => Machine.Spawn<TruckCrane>(p, y),
            });
        }

        public static EquipmentType Get(string id) => Catalog.Find(t => t.Id == id);

        // ------------------------------------------------------------------ status

        public static bool Unlocked => !GameSettings.IsCareer || Company.Stage >= CompanyStage.Contractor;
        public static bool IsOwned(string id)
        {
            var t = Get(id); if (t == null) return false;
            return Owned.Contains(id) || (t.FleetTypeId != null && Company.Owns(t.FleetTypeId));
        }
        public static bool IsRented(string id) => Rentals.ContainsKey(id);
        /// <summary>May this machine be brought to site now (owned, rented, or Free Build)?</summary>
        public static bool Available(string id) => !GameSettings.IsCareer || IsOwned(id) || IsRented(id);
        public static Machine OnSite(string id) => Machine.All.Find(m => m != null && m.TypeId == id);

        /// <summary>What rental day <paramref name="day"/> (1 = the day it was delivered) costs. Rental yards cap each 7-day block at the
        /// weekly rate: the daily rate is billed until the week's total reaches <see cref="EquipmentType.RentPerWeek"/>, then the rest of
        /// that week is free (mini excavator $375/day, $1,150/week: days 1-3 $375, day 4 $25, days 5-7 $0).</summary>
        public static int RentForDay(EquipmentType t, int day)
        {
            if (t == null || day < 1) return 0;
            int week = t.RentPerWeek > 0 ? t.RentPerWeek : t.RentPerDay * 7;
            int pos = (day - 1) % 7;
            int before = Mathf.Min(pos * t.RentPerDay, week), after = Mathf.Min((pos + 1) * t.RentPerDay, week);
            return Mathf.Max(0, after - before);
        }

        /// <summary>Total rent for a rental of <paramref name="days"/> days (weekly cap applied per 7-day block).</summary>
        public static int RentFor(EquipmentType t, int days) { int s = 0; for (int d = 1; d <= days; d++) s += RentForDay(t, d); return s; }

        // charged each morning for the day that starts (Rentals holds the days billed so far; DayStarted then counts the new one)
        static int RentalCostPerDay()
        {
            int s = 0;
            foreach (var kv in Rentals) s += RentForDay(Get(kv.Key), kv.Value + 1);
            return s;
        }

        static int OwnedCostPerDay()
        {
            int s = 0;
            foreach (var k in Owned) { var t = Get(k); if (t != null) s += t.InsuranceMaintPerDay; }
            return s;
        }

        static bool Charge(int amount, string reason)
        {
            if (!GameSettings.IsCareer || GameSettings.UnlimitedMoney || amount <= 0) return true;
            if (!Game.Wallet.TrySpend(amount, reason)) { _msg = $"Not enough cash (${amount:N0} needed)"; Game.Toast(_msg); return false; }
            return true;
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Rents a machine (first day + delivery charged now) and delivers it. Career only.</summary>
        public static bool Rent(string id, out string msg)
        {
            var t = Get(id); msg = "";
            if (t == null) { msg = "Unknown machine"; return false; }
            if (!Unlocked) { msg = "Heavy equipment opens at the Contractor stage (warehouse + a truck)"; return false; }
            if (IsRented(id)) { msg = t.Name + " is already rented"; return false; }
            if (!Charge(t.RentPerDay + (t.OnTrailer ? DeliveryFee : 0), "Equipment rental")) { msg = _msg; return false; }
            Rentals[id] = 1;
            var m = Deliver(id);
            msg = $"Rented {t.Name}: ${t.RentPerDay}/day" + (t.OnTrailer ? $" + ${DeliveryFee} delivery" : "") + (m != null ? " - delivered beside you" : "");
            return true;
        }

        /// <summary>Buys a machine that is not a fleet vehicle (mini excavator, boom lift) for cash.</summary>
        public static bool Buy(string id, bool used, out string msg)
        {
            var t = Get(id); msg = "";
            if (t == null || t.PriceNew <= 0) { msg = "Buy this one at the Dealership"; return false; }
            if (!Unlocked) { msg = "Heavy equipment opens at the Contractor stage"; return false; }
            if (Owned.Contains(id)) { msg = "You already own one"; return false; }
            int price = used ? t.PriceUsed : t.PriceNew;
            if (!Charge(price, "Equipment purchase")) { msg = _msg; return false; }
            Owned.Add(id);
            if (Rentals.Remove(id)) { var m = OnSite(id); if (m != null) m.Rented = false; }
            msg = $"Bought a {(used ? "used" : "new")} {t.Name} for ${price:N0}";
            Company.RecomputeStage();
            return true;
        }

        /// <summary>Brings an available machine to a clear spot near the player (on its trailer). Returns it, or null.</summary>
        public static Machine Deliver(string id) => Deliver(id, null, 0f);

        public static Machine Deliver(string id, Vector3? at, float yaw)
        {
            var t = Get(id);
            if (t == null || (!_fromSave && !Available(id))) return null;   // a save is the authority for what was on site
            var existing = OnSite(id);
            if (existing != null) return existing;
            Vector3 pos; float y;
            if (at.HasValue) { pos = at.Value; y = yaw; }
            else if (!FindSpot(t, out pos, out y)) { Game.Toast("No clear spot nearby for the delivery - stand in an open area"); return null; }
            var m = t.Spawn(pos, y);
            m.Rented = IsRented(id);
            if (t.OnTrailer && !at.HasValue) m.Trailer = BuildTrailer(m.transform.position + Quaternion.Euler(0, y, 0) * new Vector3(m.BodySize.x * 0.5f + 1.9f, 0, 0), y);
            return m;
        }

        /// <summary>Returns a rental (pick-up fee) or sends an owned machine back to the yard.</summary>
        public static bool Return(string id, out string msg)
        {
            var t = Get(id); msg = "";
            if (t == null) return false;
            var m = OnSite(id);
            if (m != null && m.Operating) m.ForceLeave();
            if (IsRented(id))
            {
                Charge(t.OnTrailer ? DeliveryFee : 0, "Equipment rental");
                Rentals.Remove(id);
                msg = $"{t.Name} returned to the rental yard";
            }
            else msg = $"{t.Name} hauled back to the yard";
            if (m != null) { m.gameObject.SetActive(false); Destroy(m.gameObject); }
            return true;
        }

        static bool FindSpot(EquipmentType t, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0;
            var p = Game.Player; if (p == null) return false;
            Vector3 me = p.transform.position, fwd = p.transform.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            float baseYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float len = t.Id == "boom" ? 10.5f : t.Id == "boomlift" ? 7.5f : 4.5f, wid = t.Id == "boom" ? 2.7f : 2.4f;
            float extra = t.OnTrailer ? 3.6f : 0f;
            foreach (float d in new[] { 7f, 10f, 13f, 17f, 22f })
                for (int a = 0; a < 12; a++)
                {
                    float ang = baseYaw + (a % 2 == 0 ? 1 : -1) * (a / 2) * 30f;
                    Vector3 c = me + Quaternion.Euler(0, ang, 0) * Vector3.forward * (d + len * 0.3f);
                    float gy = GroundAt(c);
                    if (float.IsNaN(gy)) continue;
                    c.y = gy;
                    float myYaw = ang + 90f;
                    var rot = Quaternion.Euler(0, myYaw, 0);
                    Vector3 half = new Vector3((wid + extra) * 0.5f, 1.4f, len * 0.5f + 0.3f);
                    Vector3 center = c + rot * new Vector3(extra * 0.5f, 1.6f, 0);
                    if (Clear(center, half, rot)) { pos = c; yaw = myYaw; return true; }
                }
            return false;
        }

        static readonly Collider[] _cols = new Collider[32];
        static bool Clear(Vector3 center, Vector3 half, Quaternion rot)
        {
            int n = Physics.OverlapBoxNonAlloc(center, half, _cols, rot, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _cols[i];
                if (c == null || c is MeshCollider) continue;
                if (Game.Player != null && c.transform.IsChildOf(Game.Player.transform)) continue;
                return false;
            }
            return true;
        }

        static float GroundAt(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, p.y + 30f, p.z), Vector3.down, out var hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (Vector3.Angle(hit.normal, Vector3.up) > 12f) return float.NaN;
                return hit.point.y;
            }
            return float.NaN;
        }

        /// <summary>20' tandem-axle equipment trailer (18' deck + 2' dovetail) with its ramps down at the rear.</summary>
        public static GameObject BuildTrailer(Vector3 pos, float yaw)
        {
            var go = CTool.Build("Equipment trailer", (b, m) =>
            {
                int steel = m.K(SurfaceKind.PaintedSteel, new Color(0.1f, 0.1f, 0.11f)), deck = m.K(SurfaceKind.TreatedLumber), galv = m.K(SurfaceKind.Galvanized);
                float y = 0.62f, L = 6.1f, W = 2.08f;
                b.AddBox(new Vector3(-W * 0.5f + 0.06f, y - 0.1f, 0), new Vector3(0.12f, 0.2f, L), steel);
                b.AddBox(new Vector3(W * 0.5f - 0.06f, y - 0.1f, 0), new Vector3(0.12f, 0.2f, L), steel);
                for (float z = -L * 0.5f + 0.2f; z < L * 0.5f; z += 0.6f) b.AddBox(new Vector3(0, y - 0.13f, z), new Vector3(W, 0.08f, 0.08f), steel);
                b.AddBox(new Vector3(0, y + 0.02f, 0.3f), new Vector3(W - 0.1f, 0.05f, L - 0.6f), Quaternion.identity, deck, Vector3.forward);
                b.AddBox(new Vector3(0, y - 0.1f, -L * 0.5f + 0.3f), new Vector3(W, 0.05f, 0.6f), Quaternion.Euler(-8, 0, 0), deck);   // dovetail
                // tongue + coupler + jack
                b.AddBox(new Vector3(-0.45f, y - 0.1f, L * 0.5f + 0.75f), new Vector3(0.1f, 0.15f, 1.7f), Quaternion.Euler(0, 15, 0), steel);
                b.AddBox(new Vector3(0.45f, y - 0.1f, L * 0.5f + 0.75f), new Vector3(0.1f, 0.15f, 1.7f), Quaternion.Euler(0, -15, 0), steel);
                b.AddBox(new Vector3(0, y - 0.05f, L * 0.5f + 1.6f), new Vector3(0.14f, 0.12f, 0.3f), galv);
                b.AddCylinder(new Vector3(0.25f, y * 0.5f, L * 0.5f + 1.1f), 0.04f, y, 8, galv);
                // fenders, wheels (tandem 7k axles)
                foreach (float z in new[] { -0.55f, 0.55f })
                    foreach (int s in new[] { -1, 1 })
                        EquipModels.Wheel(b, m, new Vector3(s * (W * 0.5f + 0.13f), 0.37f, z - 0.6f), 0.37f, 0.22f, new Color(0.8f, 0.8f, 0.8f));
                foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * (W * 0.5f + 0.13f), 0.82f, -0.6f), new Vector3(0.3f, 0.04f, 2.0f), steel);
                // ramps down to the ground
                foreach (float x in new[] { -0.6f, 0.6f })
                {
                    float rl = 1.9f, ang = Mathf.Asin((y - 0.12f) / rl) * Mathf.Rad2Deg;
                    b.AddBox(new Vector3(x, (y - 0.1f) * 0.5f, -L * 0.5f - Mathf.Cos(ang * Mathf.Deg2Rad) * rl * 0.5f), new Vector3(0.45f, 0.06f, rl), Quaternion.Euler(-ang, 0, 0), galv);
                }
            });
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.45f, 0.3f); bc.size = new Vector3(2.4f, 0.6f, 6.1f);
            go.layer = 2;
            return go;
        }

        // ------------------------------------------------------------------ on-foot prompt

        void Update() { Prompt = Interact(); }

        static string Interact()
        {
            var p = Game.Player;
            if (p == null || Game.UIOpen || MainMenu.Showing || !p.enabled || Machine.Current != null) return null;
            Ray ray = p.AimRay;
            float world = p.TryAim(out RaycastHit wh, 6f) ? wh.distance : float.MaxValue;
            Machine best = null; float bd = float.MaxValue;
            foreach (var m in Machine.All)
                if (m != null && m.Aimed(ray, 3.5f, out float d) && d < bd && d < world + 0.1f) { best = m; bd = d; }
            if (best == null) return null;
            if (GInput.GetKeyDown(KeyCode.E) && Time.frameCount != Machine.TransitionFrame) best.Enter();
            return "E: climb into the " + best.DisplayName.ToLowerInvariant() + (best.Rented ? " (rental)" : "");
        }

        GUIStyle _style; int _styleH;

        void OnGUI()
        {
            if (string.IsNullOrEmpty(Prompt) || Game.UIOpen) return;
            if (_style == null || _styleH != Screen.height)
            {
                _styleH = Screen.height;
                _style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 42f)), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            var r = new Rect(0f, Screen.height * 0.62f, Screen.width, _style.fontSize * 1.6f);
            _style.normal.textColor = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), Prompt, _style);
            _style.normal.textColor = Color.white;
            GUI.Label(r, Prompt, _style);
        }

        // ------------------------------------------------------------------ tablet app

        static void DrawApp()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle;
            float fs = label.fontSize;
            bool career = GameSettings.IsCareer;
            GUILayout.Label(career ? $"Heavy equipment  ·  cash ${Game.Wallet.Money:N0}  ·  delivery ${DeliveryFee} each way on a trailer"
                                   : "Heavy equipment  ·  Free Build: every machine, no charge", header);
            if (!Unlocked) { GUILayout.Label("Opens at the Contractor stage: lease a warehouse and own a truck (see the Company app).", label); return; }
            if (!string.IsNullOrEmpty(_msg)) GUILayout.Label(_msg, header);
            GUILayout.Space(4);
            foreach (var e in Catalog)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                var m = OnSite(e.Id);
                string status = m != null ? (m.Operating ? "on site, in use" : "on site") : "";
                if (career) status += (IsOwned(e.Id) ? (status.Length > 0 ? " · " : "") + "owned" : IsRented(e.Id) ? (status.Length > 0 ? " · " : "") + $"rented (day {Rentals[e.Id]})" : "");
                GUILayout.Label($"{e.Name}  —  {e.Model}" + (status.Length > 0 ? $"   [{status}]" : ""), header);
                GUILayout.Label(e.Specs, label);
                if (career)
                    GUILayout.Label($"Rent ${e.RentPerDay:N0}/day, capped at the ${e.RentPerWeek:N0}/week rate (the rest of a week is free once the week's rent reaches it)" +
                        (e.PriceNew > 0 ? $"  ·  buy ${e.PriceNew:N0} new / ${e.PriceUsed:N0} used, ~${e.InsuranceMaintPerDay}/day insurance + upkeep" : "  ·  buy it at the Dealership"), label);
                GUILayout.BeginHorizontal();
                if (m == null && Available(e.Id))
                {
                    if (GUILayout.Button("Bring to site", button, GUILayout.Width(fs * 9))) { if (Deliver(e.Id) != null) _msg = e.Name + " delivered beside you"; }
                }
                if (career && !IsOwned(e.Id) && !IsRented(e.Id) && GUILayout.Button($"Rent ${e.RentPerDay}/day", button, GUILayout.Width(fs * 9))) { Rent(e.Id, out _msg); GUIUtility.ExitGUI(); }   // a "Return rental" button appears: end this GUI event
                if (career && e.PriceNew > 0 && !IsOwned(e.Id))
                {
                    if (GUILayout.Button($"Buy new ${e.PriceNew:N0}", button, GUILayout.Width(fs * 10))) Buy(e.Id, false, out _msg);
                    if (GUILayout.Button($"Buy used ${e.PriceUsed:N0}", button, GUILayout.Width(fs * 10))) Buy(e.Id, true, out _msg);
                }
                if ((m != null || IsRented(e.Id)) && GUILayout.Button(IsRented(e.Id) ? "Return rental" : "Send back", button, GUILayout.Width(fs * 9))) Return(e.Id, out _msg);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            if (OnSite("miniex") != null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Haul-off", header);
                GUILayout.Label(HaulTruck.I != null ? $"Dump truck on site: {HaulTruck.I.LoadYd:0.0} / {HaulTruck.CapacityYd:0} cu yd, {HaulTruck.I.Loads} loads hauled"
                    : HaulTruck.Owned ? "Your dump truck can park beside the excavator for loading." : $"Hire a dump truck: ${HaulTruck.HiredPerLoad} per 5 cu yd load. Or dump spoil in a pile on the lot.", label);
                GUILayout.BeginHorizontal();
                if (HaulTruck.I == null && GUILayout.Button("Park dump truck by the excavator", button, GUILayout.Width(fs * 16))) CallHaulTruck();
                if (HaulTruck.I != null && GUILayout.Button("Send dump truck away", button, GUILayout.Width(fs * 12))) HaulTruck.Dismiss();
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }

        /// <summary>Parks a dump truck within the excavator's reach (to its right), facing the same way.</summary>
        public static HaulTruck CallHaulTruck()
        {
            var ex = OnSite("miniex"); if (ex == null) return null;
            var rot = Quaternion.Euler(0, ex.Yaw, 0);
            Vector3 p = ex.transform.position + rot * new Vector3(4.3f, 0, 1.0f);
            float gy = GroundAt(p); if (!float.IsNaN(gy)) p.y = gy;
            return HaulTruck.Call(p, ex.Yaw);
        }

        // ------------------------------------------------------------------ save

        [Serializable] class SiteEntry { public string id; public Vector3 pos; public float yaw; public float[] joints; }
        [Serializable] class RentEntry { public string id; public int days; }
        [Serializable]
        class Data
        {
            public List<string> owned = new List<string>();
            public List<RentEntry> rentals = new List<RentEntry>();
            public List<SiteEntry> site = new List<SiteEntry>();
        }

        public static string Save()
        {
            var d = new Data();
            d.owned.AddRange(Owned);
            foreach (var kv in Rentals) d.rentals.Add(new RentEntry { id = kv.Key, days = kv.Value });
            foreach (var m in Machine.All)
                if (m != null) d.site.Add(new SiteEntry { id = m.TypeId, pos = m.transform.position, yaw = m.Yaw, joints = m.Joints });
            return JsonUtility.ToJson(d);
        }

        public static void Load(string json)
        {
            ClearSite();
            Owned.Clear(); Rentals.Clear();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json); if (d == null) return;
            foreach (var o in d.owned) Owned.Add(o);
            foreach (var r in d.rentals) Rentals[r.id] = r.days;
            // the "company" extension (fleet-owned skid steer / crane truck) may load after us, so don't re-check ownership here
            _fromSave = true;
            try
            {
                foreach (var s in d.site)
                {
                    var m = Deliver(s.id, s.pos, s.yaw);
                    if (m != null && s.joints != null && s.joints.Length > 0) m.Joints = s.joints;
                }
            }
            finally { _fromSave = false; }
        }

        static bool _fromSave;

        /// <summary>Before a load moves the player: climb out of any machine first, so leaving it later doesn't put the
        /// player beside the old machine instead of at the saved position.</summary>
        static void LeaveMachinesBeforeLoad()
        {
            foreach (var m in new List<Machine>(Machine.All)) if (m != null && m.Operating) m.ForceLeave();
        }

        /// <summary>Removes every machine (and the haul truck) from the world. Rentals/ownership are untouched.</summary>
        public static void ClearSite()
        {
            HaulTruck.Dismiss();
            foreach (var m in new List<Machine>(Machine.All))
                if (m != null) { m.ForceLeave(); m.gameObject.SetActive(false); Destroy(m.gameObject); }
        }

        /// <summary>Tests: forget rentals, ownership and machines.</summary>
        public static void ResetState() { ClearSite(); Owned.Clear(); Rentals.Clear(); _msg = ""; }
    }
}
