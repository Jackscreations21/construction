using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The site tablet (T): a Home screen of app icons plus a grouped app sidebar; supply store, tool shop, deliveries, bank, help built in. IMGUI.</summary>
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
        int _app = -1; // -1 = Home screen

        /// <summary>Extension point: feature modules add a tablet app (drawn with IMGUI inside the tablet's scroll view).
        /// Registering "Concrete Dispatch" replaces the built-in placeholder. Call from [RuntimeInitializeOnLoadMethod].</summary>
        public static void RegisterApp(string name, System.Action draw)
        {
            _extApps.RemoveAll(a => a.name == name);
            _extApps.Add((name, draw));
            _appNames = null;
        }

        /// <summary>Extension point: removes an app registered by a feature (e.g. when another app takes over its screen).</summary>
        public static void RemoveApp(string name)
        {
            if (_extApps.RemoveAll(a => a.name == name) > 0) _appNames = null;
        }

        static readonly Dictionary<string, System.Func<bool>> _visibleIf = new Dictionary<string, System.Func<bool>>();
        /// <summary>Extension point: show an app only while the condition holds (e.g. Career-only business apps in Free Build).</summary>
        public static void SetAppVisible(string name, System.Func<bool> visible)
        {
            if (visible == null) _visibleIf.Remove(name); else _visibleIf[name] = visible;
            _appNames = null;
        }

        /// <summary>Extension point: extra lines for the Help app (controls for a feature).</summary>
        public static void AddHelp(string line) { if (!_extHelp.Contains(line)) _extHelp.Add(line); }

        /// <summary>Shared styles so extension apps match the tablet look.</summary>
        public GUIStyle LabelStyle => _label;
        public GUIStyle HeaderStyle => _header;
        public GUIStyle ButtonStyle => _button;
        /// <summary>Open the tablet on the named app (e.g. from a tutorial's "show me where"). Unknown names just open the tablet.</summary>
        public void ShowApp(string name)
        {
            int i = name == "Home" ? -1 : System.Array.IndexOf(AppNames(), name);
            if ((i >= 0 || name == "Home") && i != _app) { _app = i; _scroll = Vector2.zero; _message = ""; }
            if (!IsOpen) Open();
        }

        /// <summary>Name of the app currently shown ("Home" for the home screen).</summary>
        public string CurrentApp { get { var n = AppNames(); return _app >= 0 && _app < n.Length ? n[_app] : "Home"; } }

        /// <summary>Show a status message at the bottom of the tablet.</summary>
        public void ShowMessage(string m) => _message = m;

        static string[] AppNames()
        {
            if (_appNames != null && _visibleIf.Count == 0) return _appNames;
            var list = new List<string>();
            foreach (var b in BuiltInApps)
                if (!_extApps.Exists(a => a.name == b)) list.Add(b);
            int help = list.IndexOf("Help");
            foreach (var a in _extApps) if (!list.Contains(a.name)) list.Insert(help >= 0 ? list.IndexOf("Help") : list.Count, a.name);
            list.RemoveAll(n => _visibleIf.TryGetValue(n, out var vis) && !vis());
            _appNames = list.ToArray();
            return _appNames;
        }
        Vector2 _scroll;
        string _message = "";
        readonly Dictionary<string, int> _cart = new Dictionary<string, int>();
        int _storeCat;
        static readonly PieceCategory[] StoreCats = (PieceCategory[])System.Enum.GetValues(typeof(PieceCategory));
        static string[] _storeCatLabels;

        GUIStyle _label, _header, _button, _window, _headerRich, _messageStyle;
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
            if (GInput.GetKeyDown(KeyCode.T) && !(IsOpen && GUIUtility.keyboardControl != 0)) { if (IsOpen) Close(); else Open(); }   // T typed into a text field doesn't close
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
            _headerRich = new GUIStyle(_header) { richText = true, wordWrap = false };
            _messageStyle = new GUIStyle(_label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _messageStyle.normal.textColor = new Color(1f, 0.86f, 0.62f);
        }

        // ------------------------------------------------------------------ layout: header · grouped sidebar · content.
        // The app list is a sidebar (scrolls when long) plus a Home screen with an icon grid, so it scales as apps are added.
        static readonly string[] GroupOrder = { "Buy", "Plans", "Site office", "Learn" };
        static readonly Dictionary<string, string> _groupOf = new Dictionary<string, string>
        {
            { "Supply Store", "Buy" }, { "Tool Shop", "Buy" }, { "Finish Supply", "Buy" }, { "Garden Center", "Buy" },
            { "Concrete Dispatch", "Buy" }, { "Deliveries", "Buy" },
            { "Framing Plans", "Plans" }, { "MEP Plans", "Plans" }, { "Stair Calculator", "Plans" }, { "Paint Colors", "Plans" },
            { "Bank", "Site office" }, { "Job Board", "Site office" }, { "Inspections", "Site office" }, { "Close-up Inspections", "Site office" },
            { "Tutorials", "Learn" }, { "Help", "Learn" },
        };
        static readonly List<string> _extraGroups = new List<string>();
        const string DefaultGroup = "More";

        /// <summary>Extension point: put an app under a sidebar/home group (e.g. "Business"). Unknown groups are added
        /// before "Learn"; apps without a group go under "More".</summary>
        public static void SetAppGroup(string app, string group)
        {
            _groupOf[app] = group;
            if (System.Array.IndexOf(GroupOrder, group) < 0 && !_extraGroups.Contains(group)) _extraGroups.Add(group);
        }

        static string GroupOf(string app) => _groupOf.TryGetValue(app, out var g) ? g : DefaultGroup;

        static List<string> Groups()
        {
            var l = new List<string>(GroupOrder);
            int learn = l.IndexOf("Learn");
            foreach (var g in _extraGroups) l.Insert(learn++, g);
            l.Insert(l.IndexOf("Learn"), DefaultGroup);
            return l;
        }

        static Color GroupColor(string g)
        {
            switch (g)
            {
                case "Buy": return new Color(0.93f, 0.56f, 0.16f);
                case "Plans": return new Color(0.26f, 0.52f, 0.86f);
                case "Site office": return new Color(0.30f, 0.66f, 0.42f);
                case "Learn": return new Color(0.58f, 0.42f, 0.82f);
                default:
                    float hue = Mathf.Repeat(g.GetHashCode() * 0.618f, 1f);
                    return Color.HSVToRGB(hue, 0.5f, 0.75f);
            }
        }

        static string Initials(string name)
        {
            var parts = name.Split(new[] { ' ', '-' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
            return name.Length >= 2 ? char.ToUpperInvariant(name[0]) + name.Substring(1, 1) : name;
        }

        /// <summary>Width (px) available to the current app's content, for apps that size pictures/columns.</summary>
        public float ContentWidth { get; private set; } = 900f;

        GUIStyle _side, _groupHead, _tileName, _tileGlyph;
        int _sideH;
        Vector2 _sideScroll;

        void EnsureLayoutStyles()
        {
            if (_side != null && _sideH == Screen.height) return;
            _sideH = Screen.height;
            int fs = _button.fontSize;
            _side = new GUIStyle(GUI.skin.toggle) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.Max(11, Mathf.RoundToInt(fs * 0.9f)), wordWrap = false, clipping = TextClipping.Clip };
            _side.padding = new RectOffset(10, 6, 4, 4);
            _side.fixedHeight = Mathf.Round(fs * 1.75f);
            _side.margin = new RectOffset(2, 2, 1, 1);
            _groupHead = new GUIStyle(_label) { fontSize = Mathf.Max(10, Mathf.RoundToInt(fs * 0.72f)), fontStyle = FontStyle.Bold, wordWrap = false };
            _groupHead.normal.textColor = new Color(0.62f, 0.66f, 0.72f);
            _groupHead.padding = new RectOffset(6, 0, 6, 1);
            _tileName = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter, fontSize = Mathf.Max(11, Mathf.RoundToInt(fs * 0.85f)), wordWrap = true };
            _tileGlyph = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(fs * 1.5f), fontStyle = FontStyle.Bold, wordWrap = false };
            _tileGlyph.normal.textColor = Color.white;
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            UISkin.Apply(); // site-career: shared rounded skin
            EnsureStyles();
            EnsureLayoutStyles();

            // open transition: dim the world, slide up + fade in over ~0.18 s
            float k = Mathf.Clamp01((Time.unscaledTime - _openedAt) / 0.18f); k = 1f - (1f - k) * (1f - k) * (1f - k);
            var prevColor = GUI.color;
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(0, 0, 0, 0.45f * k);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }
            GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, prevColor.a * k);
            float w = Mathf.Min(Screen.width * 0.9f, Screen.height * 1.8f), h = Screen.height * 0.86f;
            var rect = new Rect(Mathf.Round((Screen.width - w) / 2f), Mathf.Round((Screen.height - h) / 2f + (1f - k) * Screen.height * 0.04f), w, h);
            UISkin.Panel(rect, new Color(0.07f, 0.09f, 0.11f, 0.95f * k));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(UISkin.Accent.r, UISkin.Accent.g, UISkin.Accent.b, k);
                GUI.DrawTexture(new Rect(rect.x + 14, rect.y + 6, rect.width - 28, 2), Texture2D.whiteTexture); // accent rule
                GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, prevColor.a * k);
            }

            var names = AppNames();
            if (_app >= names.Length) _app = -1;
            string appName = _app >= 0 ? names[_app] : "Home";
            // The sidebar below can change _app during a click event; the content must still draw the app this event's
            // Layout pass measured, or IMGUI's control count changes between Layout and the click (layout errors).
            int shownApp = _app;

            // header
            float pad = 12f, headH = Mathf.Round(_header.fontSize * 2.1f);
            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y + 12, rect.width - pad * 2, headH));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Site Tablet  <color=#8f98a3>›</color>  " + appName, _headerRich);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"${Game.Wallet.Money:N0}   " + (GameClock.I != null ? GameClock.I.TimeLabel : ""), _header);
            GUILayout.Space(8);
            if (GUILayout.Button("Close (T)", _button, GUILayout.Width(_button.fontSize * 7))) Close();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            float top = rect.y + 12 + headH + 6, bodyH = rect.yMax - top - pad;
            float sideW = Mathf.Round(Mathf.Clamp(_button.fontSize * 12.5f, 170f, w * 0.24f));
            var sideRect = new Rect(rect.x + pad, top, sideW, bodyH);
            var contentRect = new Rect(sideRect.xMax + pad + 4, top, rect.xMax - pad - (sideRect.xMax + pad + 4), bodyH);
            ContentWidth = contentRect.width - 22f; // minus the vertical scrollbar

            // sidebar
            UISkin.Panel(sideRect, new Color(1f, 1f, 1f, 0.05f * k));
            GUILayout.BeginArea(new Rect(sideRect.x + 4, sideRect.y + 4, sideRect.width - 8, sideRect.height - 8));
            _sideScroll = GUILayout.BeginScrollView(_sideScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            int pick = _app;
            if (GUILayout.Toggle(_app < 0, "  Home", _side) && _app >= 0) pick = -1;
            foreach (var g in Groups())
            {
                bool any = false;
                for (int i = 0; i < names.Length; i++)
                {
                    if (GroupOf(names[i]) != g) continue;
                    if (!any) { GUILayout.Label(g.ToUpperInvariant(), _groupHead); any = true; }
                    if (GUILayout.Toggle(i == _app, names[i], _side) && i != _app) pick = i;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (pick != _app) { _app = pick; _scroll = Vector2.zero; _message = ""; }

            // content
            float msgH = string.IsNullOrEmpty(_message) ? 0f : _header.fontSize * 2f;
            GUILayout.BeginArea(new Rect(contentRect.x, contentRect.y, contentRect.width, contentRect.height - msgH));
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(ContentWidth));
            if (shownApp < 0) Home(names);
            else
            {
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
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (msgH > 0f)
            {
                var mr = new Rect(contentRect.x, contentRect.yMax - msgH + 4, contentRect.width, msgH - 4);
                UISkin.Panel(mr, new Color(UISkin.Accent.r, UISkin.Accent.g, UISkin.Accent.b, 0.18f * k));
                GUI.Label(new Rect(mr.x + 12, mr.y, mr.width - 24, mr.height), _message, _messageStyle);
            }
            GUI.color = prevColor;
        }

        /// <summary>Home screen: every app as an icon tile, grouped like the sidebar.</summary>
        void Home(string[] names)
        {
            float tileW = Mathf.Round(_button.fontSize * 7.5f), tileH = Mathf.Round(tileW * 0.95f), gap = 12f;
            int cols = Mathf.Max(2, Mathf.FloorToInt((ContentWidth + gap) / (tileW + gap)));
            foreach (var g in Groups())
            {
                var apps = new List<int>();
                for (int i = 0; i < names.Length; i++) if (GroupOf(names[i]) == g) apps.Add(i);
                if (apps.Count == 0) continue;
                GUILayout.Label(g, _header);
                for (int r = 0; r < apps.Count; r += cols)
                {
                    Rect row = GUILayoutUtility.GetRect(ContentWidth, tileH + gap);
                    for (int c = 0; c < cols && r + c < apps.Count; c++)
                    {
                        int i = apps[r + c];
                        var tr = new Rect(row.x + c * (tileW + gap), row.y, tileW, tileH);
                        bool hover = tr.Contains(Event.current.mousePosition);
                        UISkin.Panel(tr, hover ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.05f));
                        float ic = Mathf.Round(tileW * 0.46f);
                        var icon = new Rect(tr.center.x - ic / 2, tr.y + tileH * 0.12f, ic, ic);
                        UISkin.Panel(icon, GroupColor(g));
                        GUI.Label(icon, Initials(names[i]), _tileGlyph);
                        GUI.Label(new Rect(tr.x + 4, icon.yMax + 6, tr.width - 8, tr.yMax - icon.yMax - 8), names[i], _tileName);
                        if (GUI.Button(tr, GUIContent.none, GUIStyle.none)) { _app = i; _scroll = Vector2.zero; _message = ""; }
                    }
                }
                GUILayout.Space(4);
            }
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

        GUIStyle _qty;
        void SupplyStore()
        {
            float cw = ContentWidth, fsz = _label.fontSize;
            float nameW = Mathf.Max(fsz * 14, cw * 0.42f), colW = Mathf.Max(fsz * 5, cw * 0.13f), btnW = Mathf.Round(fsz * 2.2f);
            if (_qty == null || _qty.fontSize != _label.fontSize) _qty = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            if (_storeCatLabels == null)
            {
                _storeCatLabels = new string[StoreCats.Length];
                for (int i = 0; i < StoreCats.Length; i++) _storeCatLabels[i] = PieceDefinition.CategoryLabel(StoreCats[i]);
            }
            int catCols = Mathf.Clamp(Mathf.FloorToInt(cw / (fsz * 9.5f)), 3, 7);
            _storeCat = GUILayout.SelectionGrid(_storeCat, _storeCatLabels, catCols, _button);
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
                GUILayout.Label(q.ToString(), _qty, GUILayout.Width(btnW * 1.3f), GUILayout.Height(btnW * 0.8f));
                if (GUILayout.Button("+", _button, GUILayout.Width(btnW))) q += step;
                GUILayout.EndHorizontal();
                if (q > 0) _cart[d.Id] = q; else _cart.Remove(d.Id);
            }

            GUILayout.Space(8);
            int total = CartTotal();
            GUILayout.Label($"Cart total: ${total:N0}   (Shift+click = 10 at a time)", _header);
            int fee = Fleet.SupplierDeliveryFee;
            if (fee > 0) GUILayout.Label($"Delivery ${fee}  ·  order total ${Fleet.StoreOrderTotal(total, false):N0}  (your own truck hauls it free: Fleet app)", _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(fee > 0 ? $"Order — standard (+${fee} delivery, ~90s)" : "Order — standard (free, ~90s)", _button)) Checkout(false);
            if (GUILayout.Button($"Rush (+${DeliveryService.RushFee + fee}, ~20s)", _button)) Checkout(true);
            if (GUILayout.Button("Clear cart", _button)) _cart.Clear();
            GUILayout.EndHorizontal();
        }

        void Checkout(bool rush)
        {
            int total = CartTotal();
            if (total <= 0) { _message = "Cart is empty."; return; }
            if (DeliveryService.I == null) { _message = "Delivery service unavailable."; return; }
            int cost = Fleet.StoreOrderTotal(total, rush);   // materials + supplier delivery fee (Career) + rush
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
