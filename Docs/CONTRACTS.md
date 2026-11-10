# Groundbreak — Milestone 1 code contracts

Unity 6, built-in render pipeline for the prototype, all scene objects built from code (`GameBootstrap`).
Namespace `Groundbreak` everywhere. World units = meters; show feet/inches via `Units.Format`.
**All input goes through `GInput`** (never `Input.*` or `Keyboard.current` directly).
Gameplay input is ignored while `Game.UIOpen` is true. UI is IMGUI (`OnGUI`) for the prototype.

Shared core (already written, do not change signatures): `Assets/Groundbreak/Scripts/Core/`
`Game`, `Units`, `SnapSettings`, `PieceDefinition`/`Catalog`, `Piece`, `Wallet`, `Stockpile`, `MaterialPile`,
`ToolShopItem`, `MaterialLibrary`, `Tool`, `GInput`, `GameBootstrap`.

## Agent A — Building (folder `Scripts/Building/`)
- `public class PlacementController : MonoBehaviour` — sets `Game.Placement = this` in Awake.
  - `public PieceDefinition Selected { get; }` — current placeable material (cycle with `[` / `]`, only placeable ones).
  - `public float CutLength { get; set; }` — meters; 0 = full stock length. Next placed lumber is this long (must be ≤ stock).
  - `public bool Active { get; set; }` — true while PlaceTool is equipped; ghost only shown when Active.
  - `public string StatusLine { get; }` — e.g. `2x4 x 8' · cut 92 5/8" · 12 on site · VALID`.
  - Always (when !UIOpen) handles: G/Shift+G grid, R/Shift+R angle, O set origin at aimed point.
  - When Active: scroll = yaw by angle step, Alt+scroll = pitch (stand a stud up), Ctrl held = grid only (ignore surface/edge snapping), F = repeat-place one grid step... (see brief), Ctrl+Z = undo last Loose piece.
- `public class PlaceTool : Tool` — Id `"place"`, Name `"Hands / Place"`. Primary = place, Secondary = pick up aimed Loose piece back to stock.

## Agent B — Player & tools (folder `Scripts/Player/`, `Scripts/Tools/`)
- `public class PlayerRig : MonoBehaviour` — `public static PlayerRig Spawn(Vector3 feetPos)`; sets `Game.Player`.
  - `public Camera Cam { get; }`, `public Ray AimRay { get; }`, `public bool TryAim(out RaycastHit hit, float range = 6f)`.
  - First-person CharacterController, WASD/Space/C, mouse look, cursor locked unless `Game.UIOpen`, Esc toggles cursor.
- `public class ToolBelt : MonoBehaviour` — on the player; sets `Game.Belt`.
  - `public const int SlotCount = 8;` `public Tool[] Slots` (length 8, null = empty, a 2-slot tool occupies its slot and the next as the same reference),
    `public int ActiveIndex`, `public Tool Active`, `public List<Tool> Owned`, `public void AddTool(Tool t)`,
    `public int Nails`, `public int NailCapacity` (pouch, refilled from `Game.Stock` "nails16d" with E near a `MaterialPile`).
- `public static class ToolFactory` — `public static IReadOnlyList<ToolShopItem> ShopItems`, `public static Tool Create(string id)`.
- Tools: framing hammer, cordless framing nailer, circular saw, tape measure, pry bar, 4' level (+ `PlaceTool` from A, created by id `"place"` via `new PlaceTool()`).

## Agent C — Tablet, deliveries, HUD (folder `Scripts/UI/`, `Scripts/Logistics/`)
- `public class GameClock : MonoBehaviour` — `public static GameClock I`, `public float Hours` (0–24), `public int Day`, 20 real min per game day, `public string TimeLabel`.
- `public class DeliveryService : MonoBehaviour` — `public static DeliveryService I`, `public static void SpawnInitialPiles()`, `public void Order(Dictionary<string,int> cart, bool rush)`, `public Vector3 DropPoint`.
- `public class Tablet : MonoBehaviour` — T toggles; sets `Game.UIOpen`.
- `public class Hud : MonoBehaviour` — crosshair, belt, snap readout, money, clock, toasts, placement status, tool hint.
