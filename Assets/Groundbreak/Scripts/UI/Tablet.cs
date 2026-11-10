using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The site tablet (T): supply store, tool shop, deliveries, bank, help. IMGUI.</summary>
    public class Tablet : MonoBehaviour
    {
        public static Tablet I;
        public bool IsOpen { get; private set; }
        int _releaseFrame;
        float _openedAt = -10f;

        static readonly string[] BuiltInApps = { "Supply Store", "Tool Shop", "Concrete Dispatch", "Deliveries", "Bank", "Help" };
        static readonly List<(string name, System.Action draw)> _extApps = new List<(string, System.Action)>();
        static readonly List<string> _extHelp = new List<string>();
        static string[] _appNames;
        int _app;

        /// <summary>Extension point: feature modules add a tablet app (drawn with IMGUI inside the tablet's scroll view).
        /// Registering "Concrete Dispatch" replaces the built-in placeholder. Call from [RuntimeInitializeOnLoadMethod].</summary>
        public static void RegisterApp(string name, System.Action draw)
        {
            _extApps.RemoveAll(a => a.name == name);
            _extApps.Add((name, draw));
            _appNames = null;
        }

        /// <summary>Extension point: extra lines for the Help app (controls for a feature).</summary>
        public static void AddHelp(string line) { if (!_extHelp.Contains(line)) _extHelp.Add(line); }

        /// <summary>Shared styles so extension apps match the tablet look.</summary>
        public GUIStyle LabelStyle => _label;
        public GUIStyle HeaderStyle => _header;
        public GUIStyle ButtonStyle => _button;
        /// <summary>Show a status message at the bottom of the tablet.</summary>
        public void ShowMessage(string m) => _message = m;

        static string[] AppNames()
        {
            if (_appNames != null) return _appNames;
            var list = new List<string>();
            foreach (var b in BuiltInApps)
                if (!_extApps.Exists(a => a.name == b)) list.Add(b);
            int help = list.IndexOf("Help");
            foreach (var a in _extApps) if (!list.Contains(a.name)) list.Insert(help >= 0 ? list.IndexOf("Help") : list.Count, a.name);
            _appNames = list.ToArray();
            return _appNames;
        }
        Vector2 _scroll;
        string _message = "";
        readonly Dictionary<string, int> _cart = new Dictionary<string, int>();
        int _storeCat;
        static readonly PieceCategory[] StoreCats = (PieceCategory[])System.Enum.GetValues(typeof(PieceCategory));
        static string[] _storeCatLabels;

        GUIStyle _label, _header, _button, _window;
        int _styleHeight;

        static readonly string[] HelpLines =
        {
            "WASD - move,  Mouse - look,  Space - jump,  C - crouch",
            "1-8 - select belt slot,  Tab - cycle tools",
            "G / Shift+G - grid size,  R / Shift+R - angle step",
            "O - set grid origin at aimed point",
            "[ / ] - previous / next material",
            "Scroll - rotate (yaw),  Alt+Scroll - tilt (stand a stud up)",
            "F - repeat last placement one grid step",
            "Hold Ctrl - grid only (no edge / surface snap)",
            "Ctrl+Z - undo last loose piece",
            "E - refill nail pouch at a nail pile",
            "T - open / close tablet",
            "Esc - release / capture mouse cursor (closes tablet)",
        };

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            if (GInput.GetKeyDown(KeyCode.T)) { if (IsOpen) Close(); else Open(); }
            else if (IsOpen && GInput.GetKeyDown(KeyCode.Escape)) Close();

            // Release UIOpen one frame after closing so the closing key/click (Esc, T, mouse) isn't also seen by gameplay.
            if (!IsOpen && _releaseFrame > 0 && Time.frameCount > _releaseFrame)
            {
                _releaseFrame = 0;
                Game.UIOpen = false;
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            }

            if (IsOpen)
            {
                Game.UIOpen = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void Open()
        {
            if (!IsOpen) { _openedAt = Time.unscaledTime; Sfx.Play("ui-open", Vector3.zero, 0.8f); }
            IsOpen = true; _message = ""; _releaseFrame = 0;
            Game.UIOpen = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        public void Close()
        {
            if (IsOpen) Sfx.Play("ui-close", Vector3.zero, 0.8f);
            IsOpen = false;
            _releaseFrame = Time.frameCount;
        }

        void EnsureStyles()
        {
            if (_label != null && _styleHeight == Screen.height) return;
            _styleHeight = Screen.height;
            int fs = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 50f));
            _label = new GUIStyle(GUI.skin.label) { fontSize = fs, wordWrap = true };
            _label.normal.textColor = Color.white;
            _header = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(fs * 1.3f), fontStyle = FontStyle.Bold };
            _button = new GUIStyle(GUI.skin.button) { fontSize = fs };
            _window = new GUIStyle(GUI.skin.box);
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            UISkin.Apply(); // site-career: shared rounded skin
            EnsureStyles();

            // open transition: dim the world, slide up + fade in over ~0.18 s
            float k = Mathf.Clamp01((Time.unscaledTime - _openedAt) / 0.18f); k = 1f - (1f - k) * (1f - k) * (1f - k);
            var prevColor = GUI.color;
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(0, 0, 0, 0.35f * k);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }
            GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, prevColor.a * k);
            float w = Screen.width * 0.7f, h = Screen.height * 0.7f;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f + (1f - k) * Screen.height * 0.04f, w, h);
            UISkin.Panel(rect, new Color(0.07f, 0.09f, 0.11f, 0.93f * k));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(UISkin.Accent.r, UISkin.Accent.g, UISkin.Accent.b, k);
                GUI.DrawTexture(new Rect(rect.x + 14, rect.y + 6, rect.width - 28, 2), Texture2D.whiteTexture); // accent rule
                GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, prevColor.a * k);
            }

            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 20));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Site Tablet", _header);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"${Game.Wallet.Money:N0}   " + (GameClock.I != null ? GameClock.I.TimeLabel : ""), _header);
            if (GUILayout.Button("Close (T)", _button, GUILayout.Width(_button.fontSize * 7))) Close();
            GUILayout.EndHorizontal();

            var names = AppNames();
            if (_app >= names.Length) _app = 0;
            int newApp = GUILayout.SelectionGrid(_app, names, Mathf.Min(names.Length, 7), _button);
            if (newApp != _app) { _app = newApp; _scroll = Vector2.zero; _message = ""; }
            GUILayout.Space(8);

            _scroll = GUILayout.BeginScrollView(_scroll);
            string appName = names[_app];
            int ext = _extApps.FindIndex(a => a.name == appName);
            if (ext >= 0) _extApps[ext].draw?.Invoke();
            else switch (appName)
            {
                case "Supply Store": SupplyStore(); break;
                case "Tool Shop": ToolShop(); break;
                case "Concrete Dispatch": GUILayout.Label("Coming in milestone 2: forms, rebar and ready-mix trucks", _label); break;
                case "Deliveries": Deliveries(); break;
                case "Bank": Bank(); break;
                case "Help":
                    foreach (var l in HelpLines) GUILayout.Label(l, _label);
                    foreach (var l in _extHelp) GUILayout.Label(l, _label);
                    break;
            }
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message, _header);
            GUILayout.EndArea();
            GUI.color = prevColor;
        }

        int CartTotal()
        {
            int total = 0;
            foreach (var kv in _cart)
            {
                var d = Catalog.Get(kv.Key);
                if (d != null) total += d.Price * kv.Value;
            }
            return total;
        }

        void SupplyStore()
        {
            float nameW = _label.fontSize * 18, colW = _label.fontSize * 6, btnW = _label.fontSize * 2.5f;
            if (_storeCatLabels == null)
            {
                _storeCatLabels = new string[StoreCats.Length];
                for (int i = 0; i < StoreCats.Length; i++) _storeCatLabels[i] = PieceDefinition.CategoryLabel(StoreCats[i]);
            }
            _storeCat = GUILayout.SelectionGrid(_storeCat, _storeCatLabels, 6, _button);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Item", _header, GUILayout.Width(nameW));
            GUILayout.Label("Price", _header, GUILayout.Width(colW));
            GUILayout.Label("On site", _header, GUILayout.Width(colW));
            GUILayout.Label("Cart", _header);
            GUILayout.EndHorizontal();

            foreach (var d in Catalog.All)
            {
                if (d.Category != StoreCats[_storeCat]) continue;
                _cart.TryGetValue(d.Id, out int q);
                GUILayout.BeginHorizontal();
                GUILayout.Label(d.Name, _label, GUILayout.Width(nameW));
                GUILayout.Label($"${d.Price}", _label, GUILayout.Width(colW));
                int onSite = Game.Stock.Count(d.Id);
                string site = d.Category == PieceCategory.Consumable ? onSite + " pcs" : onSite.ToString();
                GUILayout.Label(site, _label, GUILayout.Width(colW));
                int step = GInput.Shift ? 10 : 1;
                if (GUILayout.Button("-", _button, GUILayout.Width(btnW))) q = Mathf.Max(0, q - step);
                GUILayout.Label(q.ToString(), _label, GUILayout.Width(btnW * 1.2f));
                if (GUILayout.Button("+", _button, GUILayout.Width(btnW))) q += step;
                GUILayout.EndHorizontal();
                if (q > 0) _cart[d.Id] = q; else _cart.Remove(d.Id);
            }

            GUILayout.Space(8);
            int total = CartTotal();
            GUILayout.Label($"Cart total: ${total:N0}   (Shift+click = 10 at a time)", _header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Order — standard (free, ~90s)", _button)) Checkout(false);
            if (GUILayout.Button($"Rush (+${DeliveryService.RushFee}, ~20s)", _button)) Checkout(true);
            if (GUILayout.Button("Clear cart", _button)) _cart.Clear();
            GUILayout.EndHorizontal();
        }

        void Checkout(bool rush)
        {
            int total = CartTotal();
            if (total <= 0) { _message = "Cart is empty."; return; }
            if (DeliveryService.I == null) { _message = "Delivery service unavailable."; return; }
            int cost = total + (rush ? DeliveryService.RushFee : 0);
            if (!Game.Wallet.TrySpend(cost, rush ? "Supply order (rush)" : "Supply order"))
            {
                _message = $"Not enough money: need ${cost:N0}, have ${Game.Wallet.Money:N0}.";
                return;
            }
            DeliveryService.I.Order(new Dictionary<string, int>(_cart), rush);
            _cart.Clear();
            _message = rush ? "Rush order placed. Truck in ~20 s." : "Order placed. Truck in ~90 s.";
        }

        void ToolShop()
        {
            var belt = Game.Belt;
            foreach (var item in ToolFactory.ShopItems)
            {
                if (!Career.IsUnlocked(item.Id)) { GUILayout.Label($"{item.Name} - locked ({Career.UnlockHint(item.Id)})", _label); continue; }
                bool owned = false;
                if (belt != null && belt.Owned != null)
                    foreach (var t in belt.Owned) if (t != null && t.Id == item.Id) { owned = true; break; }

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label($"{item.Name}  -  ${item.Price}", _header);
                if (!string.IsNullOrEmpty(item.Description)) GUILayout.Label(item.Description, _label);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (owned) GUILayout.Label("Owned", _header, GUILayout.Width(_label.fontSize * 6));
                else if (GUILayout.Button("Buy", _button, GUILayout.Width(_label.fontSize * 6))) Buy(item);
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
        }

        void Buy(ToolShopItem item)
        {
            if (Game.Belt == null) { _message = "No tool belt."; return; }
            var tool = ToolFactory.Create(item.Id);
            if (tool == null) { _message = "That tool is out of stock."; return; }
            if (!Game.Wallet.TrySpend(item.Price, "Tool: " + item.Name))
            {
                _message = $"Not enough money for {item.Name} (${item.Price}).";
                return;
            }
            Game.Belt.AddTool(tool);
            _message = $"Bought {item.Name}.";
        }

        void Deliveries()
        {
            var ds = DeliveryService.I;
            if (ds == null) { GUILayout.Label("Delivery service unavailable.", _label); return; }

            Vector3 p = ds.DropPoint;
            GUILayout.Label($"Drop point (orange marker): x {p.x:0.0} m, z {p.z:0.0} m", _label);
            if (GUILayout.Button("Set drop marker where I'm looking", _button))
            {
                if (Game.Player != null && Game.Player.TryAim(out RaycastHit hit, 30f))
                {
                    ds.SetDropPoint(hit.point);
                    _message = "Drop marker moved.";
                }
                else _message = "Look at the ground within 30 m (close the tablet, aim, reopen).";
            }
            GUILayout.Space(8);

            if (ds.Pending.Count == 0) GUILayout.Label("No deliveries pending.", _label);
            foreach (var d in ds.Pending)
            {
                string eta = d.ETA > 0.5f ? $"ETA {Mathf.CeilToInt(d.ETA)} s" : "unloading";
                if (d.TruckOnSite && d.ETA > 0.5f) eta += " (truck on the road)";
                GUILayout.Label($"{(d.Rush ? "RUSH" : "Standard")} - {eta}: {d.Summary}", _label);
            }
        }

        void Bank()
        {
            GUILayout.Label($"Balance: ${Game.Wallet.Money:N0}", _header);
            var log = Game.Wallet.Log;
            int start = Mathf.Max(0, log.Count - 15);
            if (log.Count == 0) GUILayout.Label("No transactions yet.", _label);
            for (int i = log.Count - 1; i >= start; i--) GUILayout.Label(log[i], _label);
        }
    }
}
