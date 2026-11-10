using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// JSON save games in <c>Application.persistentDataPath/saves/&lt;slot&gt;.json</c>.
    /// Saves every Piece (definition id, size, transform, state, nails), stock + scrap, wallet, tool belt, clock, snap settings,
    /// delivery drop point, player position and every registered extension. Quick save F5 / quick load F9; autosave every
    /// <see cref="AutosaveGameMinutes"/> game minutes into slot "autosave".
    ///
    /// Features persist their own state with <see cref="RegisterExtension"/>:
    /// <code>SaveSystem.RegisterExtension("concrete", () => JsonUtility.ToJson(myState), json => Restore(json));</code>
    /// The load callback runs after pieces/stock/belt are restored. It is called with null when a save has no entry for the key
    /// (old save) or when starting a new game, so treat null as "reset to empty".
    /// </summary>
    public static class SaveSystem
    {
        public const int Version = 1;
        public const string QuickSlot = "quicksave", AutoSlot = "autosave";
        public static float AutosaveGameMinutes = 5f;

        struct Ext { public string Key; public Func<string> Save; public Action<string> Load; }
        static readonly List<Ext> _ext = new List<Ext>();

        /// <summary>Fired before a load wipes the world (destroy your runtime objects here if you prefer) and after it completes.</summary>
        public static event Action BeforeLoad, AfterLoad, AfterSave;
        public static string LastMessage = "";

        /// <summary>Register persistent state for a feature. Same key replaces. Safe to call from BeforeSceneLoad.</summary>
        public static void RegisterExtension(string key, Func<string> save, Action<string> load)
        {
            if (string.IsNullOrEmpty(key) || save == null || load == null) throw new ArgumentException("key/save/load required");
            _ext.RemoveAll(e => e.Key == key);
            _ext.Add(new Ext { Key = key, Save = save, Load = load });
        }

        public static IEnumerable<string> ExtensionKeys { get { foreach (var e in _ext) yield return e.Key; } }

        // ------------------------------------------------------------------ data

        [Serializable] public class PieceData { public string id; public Vector3 size, pos; public Quaternion rot; public int state, nails; }
        [Serializable] public class Count { public string id; public int n; }
        [Serializable] public class Scrap { public string id; public float len; }
        [Serializable] public class ToolData { public string id; public float battery = 1, durability = 1; }
        [Serializable] public class KV { public string key, value; }

        [Serializable]
        public class SaveFile
        {
            public int version = Version;
            public string savedAt, label;
            public int money; public List<string> walletLog = new List<string>();
            public List<Count> stock = new List<Count>();
            public List<Scrap> scrap = new List<Scrap>();
            public List<PieceData> pieces = new List<PieceData>();
            public List<ToolData> owned = new List<ToolData>();
            public List<int> slots = new List<int>();       // index into owned, -1 empty
            public int activeSlot, nails, nailCapacity;
            public float hours; public int day;
            public int gridIndex, angleIndex; public Vector3 snapOrigin; public float snapYaw;
            public Vector3 dropPoint; public bool hasDrop;
            public Vector3 playerPos; public float playerYaw; public bool hasPlayer;
            public string settings;
            public List<KV> ext = new List<KV>();
        }

        public static string Dir => Path.Combine(Application.persistentDataPath, "saves");
        public static string PathFor(string slot) => Path.Combine(Dir, Sanitize(slot) + ".json");
        static string Sanitize(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s; }

        public static bool Exists(string slot) => File.Exists(PathFor(slot));

        /// <summary>Slot names on disk, newest first.</summary>
        public static List<(string slot, DateTime time, string label)> ListSlots()
        {
            var list = new List<(string, DateTime, string)>();
            if (!Directory.Exists(Dir)) return list;
            foreach (var f in Directory.GetFiles(Dir, "*.json"))
            {
                string label = "";
                try { var sf = JsonUtility.FromJson<SaveFile>(File.ReadAllText(f)); label = sf.label; } catch { }
                list.Add((Path.GetFileNameWithoutExtension(f), File.GetLastWriteTime(f), label));
            }
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return list;
        }

        public static void Delete(string slot) { if (Exists(slot)) File.Delete(PathFor(slot)); }

        // ------------------------------------------------------------------ capture

        public static SaveFile Capture()
        {
            var s = new SaveFile { savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
            s.money = Game.Wallet.Money;
            s.walletLog.AddRange(Game.Wallet.Log);
            foreach (var kv in Game.Stock.Entries) if (kv.Value != 0) s.stock.Add(new Count { id = kv.Key, n = kv.Value });
            foreach (var (id, len) in Game.Stock.Scrap) s.scrap.Add(new Scrap { id = id, len = len });

            foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.InstanceID))
            {
                if (p == null || p.Def == null || !p.gameObject.activeInHierarchy) continue;
                if (p.GetComponentInParent<MaterialPile>() != null) continue;
                s.pieces.Add(new PieceData { id = p.Def.Id, size = p.Size, pos = p.transform.position, rot = p.transform.rotation, state = (int)p.State, nails = p.Nails });
            }

            var belt = Game.Belt;
            if (belt != null)
            {
                foreach (var t in belt.Owned) s.owned.Add(new ToolData { id = t.Id, battery = t.Battery, durability = t.Durability });
                for (int i = 0; i < ToolBelt.SlotCount; i++) s.slots.Add(belt.Slots[i] != null ? belt.Owned.IndexOf(belt.Slots[i]) : -1);
                s.activeSlot = belt.ActiveIndex; s.nails = belt.Nails; s.nailCapacity = belt.NailCapacity;
            }
            if (GameClock.I != null) { s.hours = GameClock.I.Hours; s.day = GameClock.I.Day; }
            s.gridIndex = Game.Snap.GridIndex; s.angleIndex = Game.Snap.AngleIndex; s.snapOrigin = Game.Snap.Origin; s.snapYaw = Game.Snap.OriginYaw;
            if (DeliveryService.I != null) { s.hasDrop = true; s.dropPoint = DeliveryService.I.DropPoint; }
            if (Game.Player != null) { s.hasPlayer = true; s.playerPos = Game.Player.transform.position; s.playerYaw = Game.Player.transform.eulerAngles.y; }
            s.settings = GameSettings.ToJson();
            s.label = (GameSettings.IsCareer ? "Career" : "Free Build") + (GameClock.I != null ? " - " + GameClock.I.TimeLabel : "");

            foreach (var e in _ext)
            {
                try { s.ext.Add(new KV { key = e.Key, value = e.Save() }); }
                catch (Exception ex) { Debug.LogError($"[Save] extension '{e.Key}' failed to save: {ex}"); }
            }
            return s;
        }

        public static bool Save(string slot)
        {
            try
            {
                var s = Capture();
                Directory.CreateDirectory(Dir);
                string tmp = PathFor(slot) + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(s, false));
                if (File.Exists(PathFor(slot))) File.Delete(PathFor(slot));
                File.Move(tmp, PathFor(slot));
                LastMessage = $"Saved '{slot}' ({s.pieces.Count} pieces)";
                AfterSave?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                LastMessage = "Save failed: " + e.Message;
                Debug.LogError("[Save] " + e);
                return false;
            }
        }

        // ------------------------------------------------------------------ restore

        public static bool Load(string slot)
        {
            if (!Exists(slot)) { LastMessage = $"No save in slot '{slot}'"; return false; }
            SaveFile s;
            try { s = JsonUtility.FromJson<SaveFile>(File.ReadAllText(PathFor(slot))); }
            catch (Exception e) { LastMessage = "Save is corrupt: " + e.Message; Debug.LogError("[Save] " + e); return false; }
            Apply(s);
            LastMessage = $"Loaded '{slot}'";
            return true;
        }

        /// <summary>Destroys all building pieces (used by load and New Game).</summary>
        public static void ClearPieces()
        {
            foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (p == null || p.GetComponentInParent<MaterialPile>() != null) continue;
                p.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(p.gameObject);
            }
        }

        static void SetStock(Dictionary<string, int> target)
        {
            var current = new List<KeyValuePair<string, int>>(Game.Stock.Entries);
            foreach (var kv in current) if (kv.Value > 0) Game.Stock.TryTake(kv.Key, kv.Value);
            foreach (var kv in target) if (kv.Value > 0) Game.Stock.Add(kv.Key, kv.Value);
        }

        public static void Apply(SaveFile s)
        {
            BeforeLoad?.Invoke();
            GameSettings.FromJson(s.settings);
            ClearPieces();

            var w = new Wallet(s.money);
            w.Log.AddRange(s.walletLog);
            Game.Wallet = w;

            var stock = new Dictionary<string, int>();
            foreach (var c in s.stock) stock[c.id] = c.n;
            SetStock(stock);
            Game.Stock.Scrap.Clear();
            foreach (var sc in s.scrap) Game.Stock.Scrap.Add((sc.id, sc.len));

            int missing = 0;
            foreach (var pd in s.pieces)
            {
                var def = Catalog.Get(pd.id);
                if (def == null) { missing++; continue; }
                var p = Piece.Create(def, pd.size.z, pd.pos, pd.rot);
                if ((p.Size - pd.size).sqrMagnitude > 1e-8f) p.SetSize(pd.size);
                p.State = (PieceState)pd.state;
                p.Nails = pd.nails;
            }
            if (missing > 0) Debug.LogWarning($"[Save] {missing} piece(s) had unknown definitions and were skipped");

            var belt = Game.Belt;
            if (belt != null && s.owned.Count > 0)
            {
                var owned = new List<Tool>();
                foreach (var td in s.owned)
                {
                    var t = ToolFactory.Create(td.id);
                    if (t == null) { owned.Add(null); continue; }
                    t.Battery = td.battery; t.Durability = td.durability;
                    owned.Add(t);
                }
                belt.Owned.Clear();
                foreach (var t in owned) if (t != null) belt.Owned.Add(t);
                for (int i = 0; i < ToolBelt.SlotCount; i++) belt.Slots[i] = null;
                for (int i = 0; i < ToolBelt.SlotCount && i < s.slots.Count; i++)
                {
                    int k = s.slots[i];
                    if (k >= 0 && k < owned.Count) belt.Slots[i] = owned[k];
                }
                belt.Nails = s.nails; belt.NailCapacity = s.nailCapacity > 0 ? s.nailCapacity : belt.NailCapacity;
                belt.Select(Mathf.Clamp(s.activeSlot, 0, ToolBelt.SlotCount - 1));
            }

            if (GameClock.I != null) { GameClock.I.Hours = s.hours; GameClock.I.Day = Mathf.Max(1, s.day); }
            Game.Snap.GridIndex = s.gridIndex; Game.Snap.AngleIndex = s.angleIndex; Game.Snap.Origin = s.snapOrigin; Game.Snap.OriginYaw = s.snapYaw;
            if (s.hasDrop && DeliveryService.I != null) DeliveryService.I.SetDropPoint(s.dropPoint);
            if (s.hasPlayer && Game.Player != null)
            {
                var cc = Game.Player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                Game.Player.transform.position = s.playerPos + Vector3.up * 0.05f;
                Game.Player.SetLook(s.playerYaw, 0f);
                if (cc != null) cc.enabled = true;
            }

            var map = new Dictionary<string, string>();
            foreach (var kv in s.ext) map[kv.key] = kv.value;
            foreach (var e in _ext)
            {
                map.TryGetValue(e.Key, out var json);
                try { e.Load(json); }
                catch (Exception ex) { Debug.LogError($"[Save] extension '{e.Key}' failed to load: {ex}"); }
            }
            AfterLoad?.Invoke();
        }

        /// <summary>Resets every extension (load(null)) — used by New Career / New Free Build.</summary>
        public static void ResetExtensions()
        {
            foreach (var e in _ext)
            {
                try { e.Load(null); } catch (Exception ex) { Debug.LogError($"[Save] extension '{e.Key}' failed to reset: {ex}"); }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { BeforeLoad = null; AfterLoad = null; AfterSave = null; LastMessage = ""; }
    }

    /// <summary>F5 quick save, F9 quick load, periodic autosave.</summary>
    public class SaveHotkeys : MonoBehaviour
    {
        float _lastAutoHours = -1; int _lastAutoDay;
        public static bool AutosaveEnabled = true;

        void Update()
        {
            if (GInput.GetKeyDown(KeyCode.F5)) { SaveSystem.Save(SaveSystem.QuickSlot); Game.Toast(SaveSystem.LastMessage); }
            if (GInput.GetKeyDown(KeyCode.F9)) { SaveSystem.Load(SaveSystem.QuickSlot); Game.Toast(SaveSystem.LastMessage); }

            var clock = GameClock.I;
            if (clock == null || !AutosaveEnabled || MainMenu.Showing) return;
            if (_lastAutoHours < 0) { _lastAutoHours = clock.Hours; _lastAutoDay = clock.Day; return; }
            float elapsedMin = ((clock.Day - _lastAutoDay) * 24f + clock.Hours - _lastAutoHours) * 60f;
            if (elapsedMin < 0) { _lastAutoHours = clock.Hours; _lastAutoDay = clock.Day; return; }
            if (elapsedMin >= SaveSystem.AutosaveGameMinutes)
            {
                _lastAutoHours = clock.Hours; _lastAutoDay = clock.Day;
                SaveSystem.Save(SaveSystem.AutoSlot);
            }
        }
    }
}
