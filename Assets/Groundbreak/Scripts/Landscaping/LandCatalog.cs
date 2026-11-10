using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Landscaping materials and products (bulk by the cubic yard, bags, plants, hardscape). Registered as
    /// Consumable so the framing place tool ignores them; landscaping tools consume them from Game.Stock.</summary>
    public static class LandCatalog
    {
        public class Item { public string Id, Name, Group; public int Price, Units = 1; }

        public static readonly List<Item> Items = new List<Item>
        {
            new Item { Id = "fill-dirt",     Group = "Bulk",      Name = "Fill dirt (1 cu yd)",                         Price = 28 },
            new Item { Id = "topsoil",       Group = "Bulk",      Name = "Screened topsoil (1 cu yd)",                  Price = 45 },
            new Item { Id = "crusher-run",   Group = "Bulk",      Name = "Crusher run base gravel (1 cu yd)",           Price = 48 },
            new Item { Id = "gravel-57",     Group = "Bulk",      Name = "#57 washed stone (1 cu yd)",                  Price = 58 },
            new Item { Id = "paver-sand",    Group = "Bulk",      Name = "Concrete sand for paver bedding (1 cu yd)",   Price = 52 },
            new Item { Id = "mulch",         Group = "Bulk",      Name = "Shredded hardwood mulch (1 cu yd)",           Price = 38 },
            new Item { Id = "paver-holland", Group = "Hardscape", Name = "Concrete paver 4x8 holland (bundle of 50)",   Price = 38, Units = 50 },
            new Item { Id = "paver-edging",  Group = "Hardscape", Name = "Paver edging 8' with spikes",                 Price = 14 },
            new Item { Id = "poly-sand",     Group = "Hardscape", Name = "Polymeric joint sand 50 lb",                  Price = 29 },
            new Item { Id = "retaining-block", Group = "Hardscape", Name = "Retaining wall block 12x4x7",               Price = 4 },
            new Item { Id = "postmix-50",    Group = "Hardscape", Name = "Fast-setting post concrete 50 lb",            Price = 7 },
            new Item { Id = "sod-roll",      Group = "Lawn",      Name = "Sod roll 2'x5' (pallet of 50)",               Price = 240, Units = 50 },
            new Item { Id = "grass-seed",    Group = "Lawn",      Name = "Tall fescue seed 7 lb (1,000 sq ft)",         Price = 42 },
            new Item { Id = "straw-bale",    Group = "Lawn",      Name = "Straw bale (covers ~1,000 sq ft)",             Price = 9 },
            new Item { Id = "tree-maple",    Group = "Plants",    Name = "Red maple 2\" caliper, balled & burlapped",   Price = 189 },
            new Item { Id = "tree-spruce",   Group = "Plants",    Name = "Colorado blue spruce 6', B&B",                Price = 159 },
            new Item { Id = "shrub-boxwood", Group = "Plants",    Name = "Boxwood, 3 gal",                              Price = 34 },
            new Item { Id = "shrub-hydrangea", Group = "Plants",  Name = "Hydrangea, 3 gal",                            Price = 39 },
            new Item { Id = "perennial-hosta", Group = "Plants",  Name = "Hosta, 1 gal",                                Price = 14 },
            new Item { Id = "picket-cedar",  Group = "Fence",     Name = "Cedar dog-ear picket 5/8\"x5-1/2\"x6'",       Price = 4 },
            new Item { Id = "gate-kit",      Group = "Fence",     Name = "Cedar gate 4' wide + hinges & latch",         Price = 189 },
            new Item { Id = "path-light",    Group = "Outdoor",   Name = "Low-voltage LED path light",                  Price = 29 },
            new Item { Id = "mailbox-kit",   Group = "Outdoor",   Name = "Mailbox, 4x4 post kit & address numbers",     Price = 129 },
            new Item { Id = "tarp",          Group = "Outdoor",   Name = "Poly tarp 10'x12' (cover material piles)",    Price = 15 },
        };

        public static bool IsLand(string id) => Items.Exists(i => i.Id == id);
        public static Item Get(string id) => Items.Find(i => i.Id == id);
        public static string Name(string id) { var i = Get(id); if (i != null) return i.Name; var d = Catalog.Get(id); return d != null ? d.Name : id; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            foreach (var i in Items)
                Catalog.Register(new PieceDefinition
                {
                    Id = i.Id, Name = i.Name, Category = PieceCategory.Consumable, Price = i.Price, UnitsPerPurchase = i.Units,
                    Size = new Vector3(0.4f, 0.3f, 0.6f), Color = new Color(0.55f, 0.45f, 0.3f)
                });
            SaveSystem.RegisterExtension("bulk", Bulk.Save, Bulk.Load);
        }
    }

    /// <summary>Partially used bulk units (a cubic yard of gravel goes out a wheelbarrow at a time; a seed bag covers 1,000 sq ft).</summary>
    public static class Bulk
    {
        static readonly Dictionary<string, float> _open = new Dictionary<string, float>();

        public static float OnHand(string id) => (_open.TryGetValue(id, out var f) ? f : 0f) + Game.Stock.Count(id);

        /// <summary>Takes a fractional amount of a stock unit (opening new units as needed).</summary>
        public static bool Take(string id, float units)
        {
            _open.TryGetValue(id, out float open);
            while (open + 1e-4f < units && Game.Stock.TryTake(id, 1)) open += 1f;
            if (open + 1e-4f < units) { _open[id] = open; return false; }
            _open[id] = Mathf.Max(0, open - units);
            return true;
        }

        [Serializable] class Data { public List<string> ids = new List<string>(); public List<float> vals = new List<float>(); }
        internal static string Save() { var d = new Data(); foreach (var kv in _open) { d.ids.Add(kv.Key); d.vals.Add(kv.Value); } return JsonUtility.ToJson(d); }
        internal static void Load(string json)
        {
            _open.Clear(); if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            for (int i = 0; i < d.ids.Count && i < d.vals.Count; i++) _open[d.ids[i]] = d.vals[i];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _open.Clear();
    }
}
