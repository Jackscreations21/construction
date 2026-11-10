using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public class Wallet
    {
        public int Money { get; private set; }
        public readonly List<string> Log = new List<string>();
        public event Action Changed;

        public Wallet(int start) { Money = start; }

        public bool TrySpend(int amount, string reason)
        {
            if (amount > Money) return false;
            Money -= amount; Log.Add($"-${amount}  {reason}"); Changed?.Invoke(); return true;
        }

        public void Add(int amount, string reason) { Money += amount; Log.Add($"+${amount}  {reason}"); Changed?.Invoke(); }
    }

    /// <summary>Materials on site, by PieceDefinition.Id. Consumables count individual units (nails).</summary>
    public class Stockpile
    {
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        public event Action Changed;

        public int Count(string id) => _counts.TryGetValue(id, out var n) ? n : 0;
        public void Add(string id, int n) { _counts[id] = Count(id) + n; Changed?.Invoke(); }
        public bool TryTake(string id, int n = 1)
        {
            if (Count(id) < n) return false;
            _counts[id] -= n; Changed?.Invoke(); return true;
        }
        public IEnumerable<KeyValuePair<string, int>> Entries => _counts;

        /// <summary>Scrap offcuts (meters) by lumber id; reusable later.</summary>
        public readonly List<(string id, float length)> Scrap = new List<(string, float)>();
    }

    /// <summary>A material pile on site (spawned by deliveries). Standing near one lets the player refill pouches (E).</summary>
    public class MaterialPile : MonoBehaviour
    {
        public string DefinitionId;
    }

    public struct ToolShopItem
    {
        public string Id; public string Name; public int Price; public string Description;
    }

    public static class MaterialLibrary
    {
        static readonly Dictionary<Color, Material> _cache = new Dictionary<Color, Material>();

        public static Material Get(Color c)
        {
            if (_cache.TryGetValue(c, out var m) && m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader) { color = c };
            _cache[c] = m;
            return m;
        }

        /// <summary>Transparent tint for ghost previews.</summary>
        public static Material Ghost(Color c)
        {
            var key = new Color(c.r, c.g, c.b, 0.35f);
            if (_cache.TryGetValue(key, out var m) && m != null) return m;
            var shader = Shader.Find("Standard");
            m = new Material(shader) { color = key };
            // Standard shader -> Transparent mode
            m.SetFloat("_Mode", 3);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
            _cache[key] = m;
            return m;
        }
    }
}
