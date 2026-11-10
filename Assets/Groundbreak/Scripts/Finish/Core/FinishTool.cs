using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Base for close-up/finish tools: aiming at framed faces, modes cycled with right-click, stock checks.
    /// Subclasses implement <see cref="Use"/> (left click on a face) and may override <see cref="Modes"/>.</summary>
    public abstract class FinishTool : Tool
    {
        public virtual float Range => 4f;
        public virtual string[] Modes => null;
        public int Mode;
        public string ModeName => Modes != null && Modes.Length > 0 ? Modes[Mathf.Clamp(Mode, 0, Modes.Length - 1)] : "";

        public override void Secondary()
        {
            if (Modes == null || Modes.Length == 0) return;
            Mode = (Mode + 1) % Modes.Length;
            Game.Toast(Name + ": " + ModeName);
        }

        public override void Primary()
        {
            if (Durability <= 0f) { Game.Toast(Name + " is worn out"); return; }
            if (Cordless && Battery <= 0f) { Game.Toast(Name + " battery is dead"); return; }
            if (!FinishAim.Aim(Range, out var hit)) { Game.Toast("Aim at framing or a finished surface"); return; }
            Use(hit);
        }

        /// <summary>Left click with a valid aim.</summary>
        protected abstract void Use(RaycastHit hit);

        public override string Hint => Modes != null ? $"[{ModeName}] LMB use · RMB next mode" : "LMB use";
    }

    public static class FinishAim
    {
        public static bool Aim(float range, out RaycastHit hit)
        {
            hit = default;
            return Game.Player != null && Game.Player.TryAim(out hit, range);
        }

        /// <summary>Framed face behind whatever was hit: the framing itself, or the framing under a finish layer
        /// (casts a short ray through FinishLayer objects).</summary>
        public static FramePlane FaceBehind(RaycastHit hit)
        {
            var fp = FramePlane.FromHit(hit);
            if (fp != null) return fp;
            var ray = new Ray(hit.point - hit.normal * 0.001f, -hit.normal);
            foreach (var h in SortedHits(ray, 0.4f))
            {
                if (h.collider.GetComponentInParent<FinishLayer>() != null) continue;
                fp = FramePlane.FromHit(h);
                if (fp != null) return fp;
            }
            return null;
        }

        public static List<RaycastHit> SortedHits(Ray r, float dist)
        {
            var l = new List<RaycastHit>(Physics.RaycastAll(r, dist, Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore));
            l.Sort((a, b) => a.distance.CompareTo(b.distance));
            return l;
        }
    }

    /// <summary>Stock + ownership helpers.</summary>
    public static class FinishStock
    {
        /// <summary>Takes n units from the site stockpile, toasting what's missing. Free-build: set <see cref="Unlimited"/>.</summary>
        public static bool Take(string id, int n = 1)
        {
            if (Unlimited || n <= 0) return true;
            if (Game.Stock.TryTake(id, n)) return true;
            var d = Catalog.Get(id);
            Game.Toast($"Need {n} x {(d != null ? d.Name : id)} on site (have {Game.Stock.Count(id)}) — order on the tablet");
            return false;
        }

        public static bool Has(string id, int n = 1) => Unlimited || Game.Stock.Count(id) >= n;
        public static bool Unlimited;

        /// <summary>Does the player own this tool (by ToolFactory id)?</summary>
        public static bool Owns(string toolId)
        {
            if (Game.Belt == null) return false;
            foreach (var t in Game.Belt.Owned) if (t != null && t.Id == toolId) return true;
            return false;
        }

        public static bool Require(string toolId, string why)
        {
            if (Owns(toolId)) return true;
            string name = toolId;
            foreach (var i in ToolFactory.ShopItems) if (i.Id == toolId) name = i.Name;
            Game.Toast($"You need a {name} to {why} (Tool Shop)");
            return false;
        }

        /// <summary>Buys & adds a tool directly (tests / debugging).</summary>
        public static Tool Give(string toolId)
        {
            if (Game.Belt == null) return null;
            foreach (var t in Game.Belt.Owned) if (t != null && t.Id == toolId) return t;
            var nt = ToolFactory.Create(toolId);
            if (nt == null) return null;
            Game.Belt.Owned.Add(nt);
            Game.Belt.AddTool(nt);
            return nt;
        }

        /// <summary>Consumable catalog entry (not placeable with the place tool; finish tools install it).</summary>
        public static PieceDefinition Item(string id, string name, int price, int unitsPerPurchase, Color c, float wIn = 0, float hIn = 0, float lIn = 0) =>
            new PieceDefinition { Id = id, Name = name, Category = PieceCategory.Consumable, Price = price, UnitsPerPurchase = unitsPerPurchase, Color = c, Size = new Vector3(Units.In(wIn), Units.In(hIn), Units.In(lIn)) };
    }

    /// <summary>Game-time helpers (hours since day 0), used for mud/thinset/grout/paint dry times.</summary>
    public static class FinishTime
    {
        static float _fallback;
        public static float Now => GameClock.I != null ? GameClock.I.Day * 24f + GameClock.I.Hours : _fallback + Time.time / (GameClock.RealSecondsPerDay / 24f);

        /// <summary>Fast-forward the game clock (sleeping on site, tests).</summary>
        public static void Skip(float hours)
        {
            if (GameClock.I == null) { _fallback += hours; return; }
            GameClock.I.Hours += hours;
            while (GameClock.I.Hours >= 24f) { GameClock.I.Hours -= 24f; GameClock.I.Day++; }
        }
    }

    /// <summary>"Finish Supply" tablet app: the close-up/finish materials grouped by trade, with order buttons.
    /// Features add their items with <see cref="Add"/> from a BeforeSceneLoad initializer (after Catalog.Register).</summary>
    public static class FinishSupply
    {
        static readonly List<(string trade, string id)> _items = new List<(string, string)>();
        static readonly Dictionary<string, int> _cart = new Dictionary<string, int>();
        static int _trade;
        static readonly List<string> _trades = new List<string>();

        public static void Add(string trade, params string[] ids)
        {
            if (!_trades.Contains(trade)) _trades.Add(trade);
            foreach (var id in ids) if (!_items.Exists(x => x.id == id)) _items.Add((trade, id));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() { Tablet.RegisterApp("Finish Supply", Draw); }

        static void Draw()
        {
            var t = Tablet.I; if (t == null) return;
            if (_trades.Count == 0) { GUILayout.Label("No finish materials registered.", t.LabelStyle); return; }
            _trade = Mathf.Clamp(_trade, 0, _trades.Count - 1);
            _trade = GUILayout.Toolbar(_trade, _trades.ToArray(), t.ButtonStyle);
            int total = 0;
            foreach (var (trade, id) in _items)
            {
                var d = Catalog.Get(id);
                if (d == null) continue;
                _cart.TryGetValue(id, out int q);
                total += q * d.Price;
                if (trade != _trades[_trade]) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{d.Name}  ${d.Price}{(d.UnitsPerPurchase > 1 ? " / " + d.UnitsPerPurchase : "")}  · on site {Game.Stock.Count(id)}", t.LabelStyle, GUILayout.Width(560));
                if (GUILayout.Button("-", t.ButtonStyle, GUILayout.Width(36)) && q > 0) _cart[id] = q - 1;
                GUILayout.Label(q.ToString(), t.LabelStyle, GUILayout.Width(36));
                if (GUILayout.Button("+", t.ButtonStyle, GUILayout.Width(36))) _cart[id] = q + 1;
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Cart: ${total}", t.HeaderStyle, GUILayout.Width(200));
            if (GUILayout.Button("Order (standard)", t.ButtonStyle)) Order(false, total);
            if (GUILayout.Button($"Order (rush +${DeliveryService.RushFee})", t.ButtonStyle)) Order(true, total);
            GUILayout.EndHorizontal();
        }

        static void Order(bool rush, int total)
        {
            if (_cart.Count == 0 || total <= 0 || DeliveryService.I == null) return;
            if (!Game.Wallet.TrySpend(total + (rush ? DeliveryService.RushFee : 0), "Finish materials")) { Tablet.I.ShowMessage("Not enough money"); return; }
            var cart = new Dictionary<string, int>();
            foreach (var kv in _cart) if (kv.Value > 0) cart[kv.Key] = kv.Value;
            DeliveryService.I.Order(cart, rush);
            _cart.Clear();
            Tablet.I.ShowMessage("Finish materials ordered");
        }
    }
}
