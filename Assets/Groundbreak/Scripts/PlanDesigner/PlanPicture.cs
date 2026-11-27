using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Something plan geometry can be painted onto: the designer's IMGUI canvas or a texture.</summary>
    public interface IPlanPainter
    {
        float PxPerFt { get; }
        void Fill(Rect ft, Color c);
    }

    /// <summary>Blueprint-style plan drawing shared by the designer canvas and <see cref="Render"/> (thumbnails, test pictures):
    /// room fills by kind, highlighted problem rooms, stair treads, exterior/interior walls at their real thickness and
    /// door/window/garage-door gaps (placed ones solid, automatic ones lighter).</summary>
    public static class PlanPicture
    {
        public static readonly Color Paper = new Color(0.06f, 0.18f, 0.33f), GridMinor = new Color(1f, 1f, 1f, 0.05f), GridMajor = new Color(1f, 1f, 1f, 0.12f),
            Wall = new Color(0.95f, 0.97f, 1f), IntWall = new Color(0.82f, 0.88f, 0.96f), Door = new Color(0.25f, 0.9f, 0.35f), Window = new Color(0.25f, 0.95f, 0.98f),
            Overhead = new Color(1f, 0.82f, 0.25f), Problem = new Color(1f, 0.2f, 0.15f), Select = new Color(1f, 0.85f, 0.2f);

        public static Color RoomColor(string name)
        {
            switch (Blueprint.KindOf(name))
            {
                case RoomKind.Bedroom: return new Color(0.36f, 0.46f, 0.80f);
                case RoomKind.Bath: case RoomKind.HalfBath: case RoomKind.Restroom: return new Color(0.28f, 0.66f, 0.82f);
                case RoomKind.Kitchen: case RoomKind.CommKitchen: return new Color(0.86f, 0.58f, 0.30f);
                case RoomKind.Living: case RoomKind.Dining: return new Color(0.42f, 0.70f, 0.44f);
                case RoomKind.Hall: case RoomKind.Corridor: case RoomKind.Lobby: case RoomKind.Stair: return new Color(0.52f, 0.55f, 0.62f);
                case RoomKind.Closet: case RoomKind.Storage: case RoomKind.Janitor: case RoomKind.Cooler: return new Color(0.55f, 0.47f, 0.40f);
                case RoomKind.Laundry: case RoomKind.Mech: return new Color(0.62f, 0.50f, 0.72f);
                case RoomKind.Garage: case RoomKind.Shop: case RoomKind.Warehouse: case RoomKind.Barn: case RoomKind.Shed: case RoomKind.Stall: case RoomKind.Loft: return new Color(0.55f, 0.55f, 0.48f);
                case RoomKind.Office: case RoomKind.BreakRoom: return new Color(0.70f, 0.66f, 0.38f);
                case RoomKind.Sales: return new Color(0.74f, 0.48f, 0.56f);
                case RoomKind.Void: return new Color(0.10f, 0.12f, 0.18f);
                default: return new Color(0.60f, 0.62f, 0.55f);
            }
        }

        static void Bar(IPlanPainter p, Vector2 a, Vector2 b, float thickFt, float minPx, Color c, float side = 0f)
        {
            float t = Mathf.Max(thickFt, minPx / Mathf.Max(0.01f, p.PxPerFt));
            bool h = Mathf.Abs(a.y - b.y) < 0.01f;
            // side: 0 centered, +1/-1 = thickness on the left/right of A→B (exterior walls sit inside the footprint)
            var n = new Vector2(-(b - a).normalized.y, (b - a).normalized.x);
            Vector2 o = side == 0f ? -n * t / 2f : side > 0f ? Vector2.zero : -n * t;
            Vector2 q0 = a + o, q1 = b + o + n * t;
            p.Fill(Rect.MinMaxRect(Mathf.Min(q0.x, q1.x), Mathf.Min(q0.y, q1.y), Mathf.Max(q0.x, q1.x), Mathf.Max(q0.y, q1.y)), c);
            _ = h;
        }

        /// <summary>Paints one floor of a plan. highlight: rects to tint red (problems); selected: one rect outlined yellow;
        /// placed: predicate telling which openings the player placed (others are drawn lighter).</summary>
        public static void Draw(IPlanPainter p, HousePlan plan, int floor, IEnumerable<Rect> highlight = null, Rect? selected = null, System.Func<PlanOpening, bool> placed = null, float flash = 1f)
        {
            float W = plan.WidthFt, D = plan.DepthFt;
            // grid
            float px = p.PxPerFt;
            float minor = px >= 6f ? 1f : px >= 1.5f ? 5f : 10f, major = minor * (minor >= 5f ? 2f : 5f);
            for (float x = 0; x <= W + 0.01f; x += minor) p.Fill(Rect.MinMaxRect(x - 0.5f / px, 0, x + 0.5f / px, D), Mathf.Abs(x / major - Mathf.Round(x / major)) < 0.01f ? GridMajor : GridMinor);
            for (float y = 0; y <= D + 0.01f; y += minor) p.Fill(Rect.MinMaxRect(0, y - 0.5f / px, W, y + 0.5f / px), Mathf.Abs(y / major - Mathf.Round(y / major)) < 0.01f ? GridMajor : GridMinor);
            // rooms
            foreach (var r in plan.Rooms)
            {
                if (r.Floor != floor) continue;
                var c = RoomColor(r.Name); c.a = 0.55f;
                p.Fill(r.Ft, c);
            }
            if (highlight != null) foreach (var h in highlight) p.Fill(h, new Color(Problem.r, Problem.g, Problem.b, 0.38f * flash));
            // stair treads
            var bp = plan as Blueprint;
            if (bp != null)
                foreach (var s in bp.Stairs)
                {
                    if (s.Floor != floor && !(s.Floor == floor - 1)) continue;
                    var f = s.Ft; float tread = Mathf.Max(0.5f, s.TreadIn / 12f);
                    var tc = new Color(1f, 1f, 1f, s.Floor == floor ? 0.55f : 0.25f);
                    if (s.AlongY) for (float y = f.yMin + 3f; y < f.yMax - 0.01f; y += tread) p.Fill(Rect.MinMaxRect(f.xMin + 0.2f, y, f.xMax - 0.2f, y + 1.2f / px), tc);
                    else for (float x = f.xMin + 3f; x < f.xMax - 0.01f; x += tread) p.Fill(Rect.MinMaxRect(x, f.yMin + 0.2f, x + 1.2f / px, f.yMax - 0.2f), tc);
                }
            // walls
            foreach (var w in plan.Walls)
            {
                if (w.Floor != floor) continue;
                if (w.Exterior) Bar(p, w.A, w.B, plan.WallDepthFt, 3f, Wall, 1f);
                else Bar(p, w.A, w.B, 4.5f / 12f, 2f, IntWall);
            }
            // openings: gap colored by type across the wall thickness
            foreach (var o in plan.Openings)
            {
                if (o.Floor != floor) continue;
                var w = plan.Walls[o.Wall];
                float half = o.RoughWidthIn / 24f;
                var a = w.At(Mathf.Max(0f, o.CenterFt - half)); var b = w.At(Mathf.Min(w.LengthFt, o.CenterFt + half));
                var c = o.IsOverhead ? Overhead : o.IsDoor ? Door : Window;
                if (placed != null && !placed(o)) c = Color.Lerp(c, Wall, 0.45f);
                float t = w.Exterior ? plan.WallDepthFt : 4.5f / 12f;
                Bar(p, a, b, t * 1.6f, 4f, c, w.Exterior ? 1f : 0f);
            }
            if (selected.HasValue)
            {
                var s = selected.Value; float e = 2.5f / px;
                p.Fill(Rect.MinMaxRect(s.xMin, s.yMin, s.xMax, s.yMin + e), Select); p.Fill(Rect.MinMaxRect(s.xMin, s.yMax - e, s.xMax, s.yMax), Select);
                p.Fill(Rect.MinMaxRect(s.xMin, s.yMin, s.xMin + e, s.yMax), Select); p.Fill(Rect.MinMaxRect(s.xMax - e, s.yMin, s.xMax, s.yMax), Select);
            }
        }

        // ------------------------------------------------------------------ texture rendering

        class TexPainter : IPlanPainter
        {
            public Color32[] Px; public int W, H; public float S, Ox, Oy;
            public float PxPerFt => S;
            public void Fill(Rect ft, Color c)
            {
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Ox + ft.xMin * S), 0, W), x1 = Mathf.Clamp(Mathf.CeilToInt(Ox + ft.xMax * S), 0, W);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(Oy + ft.yMin * S), 0, H), y1 = Mathf.Clamp(Mathf.CeilToInt(Oy + ft.yMax * S), 0, H);
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int i = y * W + x; Color b = Px[i];
                        Px[i] = Color.Lerp(b, new Color(c.r, c.g, c.b, 1f), c.a);
                    }
            }
        }

        /// <summary>The plan floor drawn into a new texture (front wall at the bottom). Caller destroys it.</summary>
        public static Texture2D Render(HousePlan plan, int floor, int width, int height, IEnumerable<Rect> highlight = null, System.Func<PlanOpening, bool> placed = null)
        {
            float margin = 3f;
            float s = Mathf.Min(width / (plan.WidthFt + margin * 2f), height / (plan.DepthFt + margin * 2f));
            var tp = new TexPainter { W = width, H = height, S = s, Px = new Color32[width * height] };
            tp.Ox = (width - plan.WidthFt * s) / 2f; tp.Oy = (height - plan.DepthFt * s) / 2f;
            Color32 bg = Paper; for (int i = 0; i < tp.Px.Length; i++) tp.Px[i] = bg;
            Draw(tp, plan, floor, highlight, null, placed);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(tp.Px); tex.Apply();
            return tex;
        }
    }
}
