namespace Groundbreak
{
    /// <summary>Every surface look in the game. Materials.Get(kind) returns a shared, textured material.
    /// Textures are procedural (generated at startup) and tile in world meters: UVs are authored in meters,
    /// and the material's tiling = 1 / Materials.TileMeters(kind).</summary>
    public enum SurfaceKind
    {
        PineLumber,        // SPF framing lumber, long grain along U
        PineEndGrain,      // cut ends: growth rings
        TreatedLumber,     // pressure-treated, green tint, incising marks
        Osb,               // oriented strand board flakes
        Plywood,           // rotary-cut veneer face
        PaintedWhite,      // painted trim/doors, slight orange peel
        PaintedSteel,      // steel entry/garage door, semi-gloss
        BrushedSteel,      // stainless appliances
        Galvanized,        // spangled galvanized sheet metal (AC cabinets, ducts)
        DarkPlastic,       // black plastic / rubber
        Glass,             // transparent, glossy
        Nickel,            // satin nickel hardware
        Brass,             // polished brass
        MatteBlack,        // matte black hardware/fixtures
        Oak,               // stained oak (cabinets, fiberglass door)
        Walnut,            // dark stained wood
        Porcelain,         // toilets, tubs
        Grass,             // ground
        Dirt,
        Gravel,
        Concrete,
        TruckPaint,        // glossy red paint
        Tire,
        Brick,             // red clay face brick, sanded texture + speckles (one brick face; mortar is separate)
        UsedBrick,         // tumbled, darker, chipped/soot variation
        ConcreteBlock,     // CMU: coarse gray aggregate, pitted
        Mortar,            // gray sand-cement
        Rebar,             // rusty steel, ribbed (ribs come from normal map along U)
        PaperBag,          // kraft paper bag
    }
}
