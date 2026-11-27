using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum FieldKind { Corn, Soybeans, Wheat, Plowed, Hay, Pasture }

    /// <summary>
    /// Materials for the map (buildings, fields, water). All UVs are metres; textures tile in metres like <see cref="Materials"/>.
    /// Texture direction conventions (MeshBuilder picks U from the uHint / face): rows, ribs and courses run along U.
    /// Every getter is cached and cheap to call repeatedly.
    /// </summary>
    public static partial class MapMaterials
    {
        static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

        static Material Cached(string key, System.Func<Material> make)
        {
            if (_cache.TryGetValue(key, out var m) && m != null) return m;
            m = make(); m.hideFlags = HideFlags.DontUnloadUnusedAsset; m.enableInstancing = true;
            _cache[key] = m;
            return m;
        }

        static Material Tinted(Material src, Color tint, string key) => Cached(key, () => new Material(src) { name = key, color = src.color * tint });

        /// <summary>Plain colour (matte paint, plastics, signs).</summary>
        public static Material Flat(Color c) => MaterialLibrary.Get(c);

        /// <summary>Self-lit colour (lit sign faces, lit windows at night). Not affected by fog much less than lit surfaces.</summary>
        public static Material Emissive(Color c) => Cached("emi" + c, () =>
        {
            var m = new Material(Shader.Find("Standard")) { name = "MapEmissive", color = c };
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 0.8f);
            return m;
        });

        /// <summary>Running-bond face brick with mortar joints (modular brick, 3 courses = 8"). Courses along U.</summary>
        public static Material BrickWall(Color tint) => BrickWallImpl(tint);
        /// <summary>Ribbed steel panel (R-panel, ribs every 12"). Ribs run along V (vertical on walls when U is horizontal).</summary>
        public static Material MetalPanel(Color c) => MetalPanelImpl(c);
        /// <summary>Asphalt architectural shingles (tinted).</summary>
        public static Material Shingle(Color tint) => Tinted(RoofTextures.Shingle, tint, "shingle" + tint);
        /// <summary>Vinyl lap siding, courses along U.</summary>
        public static Material Siding(Color c) => FinishMaterials.Get(ExteriorLooks.Vinyl, c);
        /// <summary>Painted stucco / EIFS.</summary>
        public static Material Stucco(Color c) => StuccoImpl(c);
        /// <summary>Crop field / pasture surface; crop rows (30" spacing) run along U.</summary>
        public static Material Field(FieldKind k) => FieldImpl(k);
        /// <summary>Pond / creek water (glossy, dark).</summary>
        public static Material Water => WaterImpl();
        public static Material Glass => Materials.Get(SurfaceKind.Glass);
        public static Material Asphalt => LandMaterials.Get(LandSurface.Asphalt);
        public static Material Concrete => Materials.Get(SurfaceKind.Concrete);
        public static Material Gravel => Materials.Get(SurfaceKind.Gravel);
        public static Material Grass => Materials.Get(SurfaceKind.Grass);
        public static Material Dirt => Materials.Get(SurfaceKind.Dirt);

        // ---- implementations (procedural textures live in MapTextures.cs; these fall back to tinted stock materials)
        static Material BrickWallImpl(Color tint) => MapTextures.Ready ? MapTextures.BrickWall(tint) : Tinted(Materials.Get(SurfaceKind.Brick), tint, "brick" + tint);
        static Material MetalPanelImpl(Color c) => MapTextures.Ready ? MapTextures.MetalPanel(c) : Tinted(Materials.Get(SurfaceKind.PaintedSteel), c, "metal" + c);
        static Material StuccoImpl(Color c) => MapTextures.Ready ? MapTextures.Stucco(c) : Tinted(Materials.Get(SurfaceKind.PaintedWhite), c, "stucco" + c);
        static Material FieldImpl(FieldKind k) => MapTextures.Ready ? MapTextures.Field(k) : Tinted(Materials.Get(k == FieldKind.Plowed ? SurfaceKind.Dirt : SurfaceKind.Grass), Color.white, "field" + k);
        static Material WaterImpl() => MapTextures.Ready ? MapTextures.Water() : Cached("water", () => new Material(Shader.Find("Standard")) { color = new Color(0.16f, 0.22f, 0.22f) });
    }
}
