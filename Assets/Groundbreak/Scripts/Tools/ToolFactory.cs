using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public static class ToolFactory
    {
        static readonly List<ToolShopItem> _items = new List<ToolShopItem>
        {
            new ToolShopItem { Id = "hammer", Name = "Framing Hammer", Price = 35, Description = "3 swings per nail, 2 nails fasten a piece." },
            new ToolShopItem { Id = "nailer", Name = "Cordless Framing Nailer", Price = 399, Description = "One click, one nail. Battery powered." },
            new ToolShopItem { Id = "saw", Name = "Circular Saw", Price = 149, Description = "Cut loose lumber to length. Right-click sets cut length preset. 2 slots." },
            new ToolShopItem { Id = "tape", Name = "Tape Measure", Price = 20, Description = "Click two points to measure." },
            new ToolShopItem { Id = "prybar", Name = "Pry Bar", Price = 25, Description = "Hold on a fastened piece to pull it. Lumber may split." },
            new ToolShopItem { Id = "level", Name = "4' Level", Price = 30, Description = "Shows how far a piece is off level/plumb." },
        };

        public static IReadOnlyList<ToolShopItem> ShopItems => _items;
        static readonly Dictionary<string, System.Func<Tool>> _ext = new Dictionary<string, System.Func<Tool>>();

        /// <summary>Extension point: feature modules add a buyable tool (e.g. bull float, wire strippers).
        /// Call from [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)].</summary>
        public static void Register(ToolShopItem item, System.Func<Tool> create)
        {
            _items.RemoveAll(i => i.Id == item.Id);
            _items.Add(item);
            _ext[item.Id] = create;
        }

        public static Tool Create(string id)
        {
            if (id != null && _ext.TryGetValue(id, out var make)) return make();
            switch (id)
            {
                case "place": return new PlaceTool();
                case "hammer": return new FramingHammer();
                case "nailer": return new CordlessFramingNailer();
                case "saw": return new CircularSaw();
                case "tape": return new TapeMeasure();
                case "prybar": return new PryBar();
                case "level": return new Level4ft();
                default: return null;
            }
        }
    }

    /// <summary>Shared helpers for tools.</summary>
    public static class ToolUtil
    {
        public static Piece AimPiece(float range, out RaycastHit hit)
        {
            hit = default;
            if (Game.Player == null || !Game.Player.TryAim(out hit, range)) return null;
            return hit.collider.GetComponentInParent<Piece>();
        }

        public static void Wear(Tool t, float amount)
        {
            t.Durability = Mathf.Max(0f, t.Durability - amount);
        }

        public static void CenterLabel(string text, Color color, float yOffset = 40f)
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
            style.normal.textColor = color;
            GUI.Label(new Rect(Screen.width * 0.5f - 200, Screen.height * 0.5f + yOffset, 400, 28), text, style);
        }
    }
}
