using UnityEngine;

namespace Groundbreak
{
    /// <summary>Doors and windows the non-residential blueprints need (overhead/dock doors, storefront, hollow metal, barn and shed doors).
    /// Size = (width, height, depth) in inches like the core catalog. 2026 US supplier prices.</summary>
    public static class BlueprintProducts
    {
        static PieceDefinition P(string id, string name, PieceCategory cat, float w, float h, float d, int price, Color c) =>
            new PieceDefinition { Id = id, Name = name, Category = cat, Size = new Vector3(Units.In(w), Units.In(h), Units.In(d)), Price = price, Cuttable = false, Color = c };

        static readonly Color White = new Color(0.93f, 0.93f, 0.91f), Steel = new Color(0.70f, 0.72f, 0.74f), Bronze = new Color(0.32f, 0.26f, 0.20f),
                              Red = new Color(0.55f, 0.16f, 0.12f), Gray = new Color(0.55f, 0.57f, 0.58f), Wood = new Color(0.62f, 0.46f, 0.28f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register()
        {
            Catalog.Register(
                // vehicle doors (framed to the door size)
                P("garage-8x7", "Garage door 8x7 steel", PieceCategory.GarageDoor, 96, 84, 2, 829, White),
                P("garage-18x8", "Garage door 18x8 insulated steel", PieceCategory.GarageDoor, 216, 96, 2, 2299, White),
                P("oh-10x10", "Sectional overhead door 10x10 (commercial)", PieceCategory.GarageDoor, 120, 120, 2, 2600, White),
                P("oh-12x12", "Sectional overhead door 12x12 (commercial)", PieceCategory.GarageDoor, 144, 144, 2, 3300, White),
                P("oh-12x14", "Sectional overhead door 12x14 (commercial)", PieceCategory.GarageDoor, 144, 168, 2, 3900, White),
                P("oh-14x14", "Sectional overhead door 14x14 (commercial)", PieceCategory.GarageDoor, 168, 168, 2, 4600, White),
                P("oh-dock-9x10", "Dock door 9x10 + 6x8 dock leveler + seal", PieceCategory.GarageDoor, 108, 120, 2, 9800, Gray),
                P("door-slide-12x12", "Sliding barn door 12x12 (pair, steel clad)", PieceCategory.GarageDoor, 144, 144, 3, 2100, Red),
                P("door-slide-10x10", "Sliding barn door 10x10 (pair, steel clad)", PieceCategory.GarageDoor, 120, 120, 3, 1700, Red),
                // man doors
                P("door-hm36", "Hollow metal door 36x84 + frame, closer, lever", PieceCategory.Door, 36, 84, 1.75f, 1150, Gray),
                P("door-hm36-panic", "Hollow metal exit door 36x84 + panic bar", PieceCategory.Door, 36, 84, 1.75f, 1650, Gray),
                P("door-storefront36", "Aluminum storefront door 36x84 + closer + panic", PieceCategory.Door, 36, 84, 1.75f, 3400, Bronze),
                P("door-storefront72", "Aluminum storefront pair 72x84 + closers + panic", PieceCategory.Door, 72, 84, 1.75f, 6200, Bronze),
                P("door-int-sc36", "Interior solid-core 36x80 (ADA) + lever", PieceCategory.Door, 36, 80, 1.75f, 420, Wood),
                P("door-fire20-36", "20-min fire-rated steel 36x80, self-closing (garage-house)", PieceCategory.Door, 36, 80, 1.75f, 389, White),
                P("door-shed-dbl60", "Shed double door 60x72 (site-built, T1-11)", PieceCategory.Door, 60, 72, 1.5f, 260, Wood),
                P("door-stall48", "Horse stall sliding door 48x84 (grille top)", PieceCategory.Door, 48, 84, 2, 980, Wood),
                P("door-cooler36", "Walk-in cooler door 36x78 (insulated, self-closing)", PieceCategory.Door, 36, 78, 4, 1800, Steel),
                P("door-walk-36-steel", "Steel walk door 36x80 (post-frame)", PieceCategory.Door, 36, 80, 1.75f, 450, White),
                // windows
                P("win-sh-3636", "Single-hung vinyl 36x36", PieceCategory.Window, 36, 36, 3.25f, 219, White),
                P("win-storefront-7284", "Aluminum storefront glazing 72x84 (1\" insulated)", PieceCategory.Window, 72, 84, 4.5f, 2400, Bronze));
        }
    }
}
