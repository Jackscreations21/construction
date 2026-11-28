using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// "Town Plans" tablet app: the plans on file for the town's buildings (the building you stand at first, then the nearest ones).
    /// Shows the plan's summary, a drawn floor plan and can lay the blueprint over the real building (an x-ray overlay just above the roof,
    /// <see cref="ShowOver"/>). A HUD line names the plan on file when you walk onto a lot that has one.
    /// </summary>
    public static class BuildingPlansApp
    {
        public const string AppName = "Town Plans";
        static string _sel;
        static int _floor;
        static Vector2 _scroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Tablet.RegisterApp(AppName, Draw);
            Tablet.AddHelp("Town Plans app: the blueprint on file for the building you stand at (diner, gas station, warehouse, townhomes, houses...). Lay it over the building.");
        }

        static Vector3 PlayerPos { get { var c = Camera.main; return c != null ? c.transform.position : Vector3.zero; } }

        /// <summary>Town lots with a plan on file, nearest to p first.</summary>
        public static List<TownLot> Nearest(Vector3 p, int max)
        {
            var l = new List<TownLot>(MapPlans.LotsWithPlans());
            p.y = 0;
            l.Sort((a, b) => (a.Center - p).sqrMagnitude.CompareTo((b.Center - p).sqrMagnitude));
            if (l.Count > max) l.RemoveRange(max, l.Count - max);
            return l;
        }

        static void Draw()
        {
            if (!Town.Exists) { GUILayout.Label("The town plans office opens in a Career game (the town is not built)."); return; }
            var here = Town.LotAt(PlayerPos);
            var near = Nearest(PlayerPos, 12);
            if (here != null && MapLayout.PlanFor(here) != null && !near.Contains(here)) near.Insert(0, here);
            if (near.Count == 0) { GUILayout.Label("No plans on file."); return; }
            if (_sel == null || Town.Find(_sel) == null) _sel = (here != null && MapLayout.PlanFor(here) != null ? here : near[0]).Id;
            GUILayout.Label("Plans on file at the town office — nearest buildings:");
            var names = new string[near.Count];
            for (int i = 0; i < near.Count; i++) names[i] = $"{near[i].Address} ({Kind(near[i])})";
            int sel = Mathf.Max(0, near.FindIndex(x => x.Id == _sel));
            int was = sel;
            sel = GUILayout.SelectionGrid(sel, names, 2);
            _sel = near[Mathf.Clamp(sel, 0, near.Count - 1)].Id;
            if (sel != was) { _floor = 0; GUIUtility.ExitGUI(); }   // another plan: the rows below change, end this GUI event
            var lot = Town.Find(_sel);
            var plan = MapLayout.PlanFor(lot);
            if (plan == null) return;
            _floor = Mathf.Clamp(_floor, 0, Mathf.Max(0, plan.Floors - 1));   // e.g. floor 2 of a 2-story kept for a ranch
            bool overlayShown = PlanOverlay.Root != null;   // as in the Layout pass, so the Hide button doesn't appear mid-event
            GUILayout.Space(6);
            GUILayout.Label($"{lot.Address} · {(string.IsNullOrEmpty(lot.Resident) ? Kind(lot) : lot.Resident)} · {MapLayout.DistrictAt(lot.Center)?.Name ?? "Old Town"}");
            GUILayout.Label($"Plan on file: {plan.Name} ({plan.Id}) — {plan.SqFt:N0} sq ft, {plan.WidthFt:0}' x {plan.DepthFt:0}', {plan.Floors} floor(s)");
            GUILayout.Label(plan.Summary);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Lay the blueprint over the building")) { ShowOver(lot, _floor); Game.Toast("Blueprint laid over " + lot.Address); }
            if (plan.Floors > 1 && GUILayout.Button($"Floor {_floor + 1} of {plan.Floors}")) { _floor = (_floor + 1) % plan.Floors; if (PlanOverlay.Root != null) ShowOver(lot, _floor); }
            if (overlayShown && GUILayout.Button("Hide")) PlanOverlay.Hide();
            if (GUILayout.Button("Edit a copy in the Plan Designer")) { PlanDesignerApp.EditCopy(plan); GUIUtility.ExitGUI(); }   // the tablet switches apps
            GUILayout.EndHorizontal();
            float w = Tablet.I != null ? Tablet.I.ContentWidth - 30f : 600f;
            var r = GUILayoutUtility.GetRect(w, w * 0.5f);
            DrawPlan(r, plan, _floor);
        }

        static string Kind(TownLot l) => (l.TypeId ?? "building").Replace("twostory", "two-story").Replace("ranch2", "ranch");

        /// <summary>IMGUI floor plan: room rectangles to scale with names, on blueprint blue.</summary>
        public static void DrawPlan(Rect r, HousePlan plan, int floor)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = new Color(0.08f, 0.22f, 0.45f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
            float s = Mathf.Min((r.width - 20f) / Mathf.Max(1f, plan.WidthFt), (r.height - 20f) / Mathf.Max(1f, plan.DepthFt));
            float ox = r.x + (r.width - plan.WidthFt * s) / 2f, oy = r.y + (r.height - plan.DepthFt * s) / 2f;
            var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Clamp((int)(s * 2.2f), 9, 14), wordWrap = true };
            st.normal.textColor = new Color(0.85f, 0.93f, 1f);
            foreach (var room in plan.Rooms)
            {
                if (room.Floor != floor) continue;
                // plan y = 0 is the front (street) wall: draw the front at the bottom
                var rr = new Rect(ox + room.Ft.xMin * s, oy + (plan.DepthFt - room.Ft.yMax) * s, room.Ft.width * s, room.Ft.height * s);
                Outline(rr, 1.5f, new Color(0.85f, 0.93f, 1f));
                if (rr.width > 30f && rr.height > 14f) GUI.Label(rr, room.Name, st);
            }
            Outline(new Rect(ox, oy, plan.WidthFt * s, plan.DepthFt * s), 3f, Color.white);
        }

        static void Outline(Rect r, float t, Color c)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>Where a lot's building stands: footprint centre (grade), rotation (+Z to the street), size and roof height.</summary>
        public static bool Footprint(TownLot lot, out Vector3 centre, out Quaternion rot, out Vector2 size, out float roof)
        {
            centre = Vector3.zero; rot = Quaternion.identity; size = Vector2.zero; roof = 0f;
            if (lot == null) return false;
            var p = MapPlans.ParcelOf(lot);
            if (p != null && !p.Ranch)
            {
                // the building itself (a gas station's store behind its forecourt, a yard's shop), not the whole spec footprint
                var body = MapBodies.Body(p.Spec);
                rot = p.Rotation; size = new Vector2(body.width, body.height);
                centre = p.Origin + rot * new Vector3(body.center.x, 0, body.center.y);
            }
            else if (lot.House != null)
            {
                var t = lot.House.transform; rot = t.rotation; size = new Vector2(lot.House.Width, lot.House.Depth); centre = t.position;
            }
            else return false;
            centre.y = 0f;
            // roof: the highest collider straight above the footprint (map buildings are boxes in the cell colliders)
            roof = 3f;
            var frontC = centre + rot * new Vector3(0, 0, size.y / 2f);
            foreach (var o in new[] { new Vector3(0, 0, -2f), new Vector3(size.x * 0.3f, 0, -3f), new Vector3(-size.x * 0.3f, 0, -3f), new Vector3(0, 0, -size.y / 2f) })
                foreach (var hit in Physics.RaycastAll(frontC + rot * o + Vector3.up * 80f, Vector3.down, 80f))
                    if (hit.point.y > roof && hit.point.y < 40f) roof = hit.point.y;
            return true;
        }

        /// <summary>Lays the lot's plan over its building, centred on the footprint, 0.4 m above the roof so it reads from the street and the air.</summary>
        public static GameObject ShowOver(TownLot lot, int floor = 0)
        {
            var plan = MapLayout.PlanFor(lot);
            if (plan == null || !Footprint(lot, out var c, out var rot, out var size, out float roof)) return null;
            var Y = rot * Vector3.back;                      // plan y runs from the front wall toward the back
            var X = Vector3.Cross(Vector3.up, Y);            // right hand of someone on the street facing the building
            // the plan's front wall lines up with the building's front wall, centred across it
            var front = c - Y * (size.y / 2f);
            var origin = front - X * (plan.WidthFt * Units.Foot / 2f) + Vector3.up * (roof + 0.4f);
            return PlanOverlay.Show(plan, new PlanPlacement { Origin = origin, X = X, Y = Y }, floor);
        }
    }

    /// <summary>HUD line: the plan on file for the lot you are standing on.</summary>
    public class BuildingPlanHint : MonoBehaviour
    {
        TownLot _lot; float _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn() { var go = new GameObject("BuildingPlanHint"); DontDestroyOnLoad(go); go.AddComponent<BuildingPlanHint>(); }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            var cam = Camera.main;
            _lot = cam != null && Town.Exists ? Town.LotAt(cam.transform.position) : null;
            if (_lot != null && MapLayout.PlanFor(_lot) == null) _lot = null;
        }

        void OnGUI()
        {
            if (_lot == null || Game.UIOpen) return;
            var plan = MapLayout.PlanFor(_lot);
            if (plan == null) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Screen.height / 60), alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height - Screen.height / 9f, Screen.width, 30), $"{_lot.Address}: plan on file — {plan.Name}, {plan.SqFt:N0} sq ft (tablet → {BuildingPlansApp.AppName})", st);
        }
    }
}
