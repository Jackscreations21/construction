using UnityEngine;

namespace Groundbreak
{
    /// <summary>Adds the blueprint layers to Development's on-site <see cref="PlanOverlay"/>: stair treads with an UP arrow and riser count,
    /// MEP fixture markers color-coded by trade (plumbing blue, electrical yellow, HVAC orange, fire red) with their tags, split-level
    /// offsets, and a title line with the occupancy/structure. Hooked through <see cref="PlanOverlay.Shown"/>.</summary>
    public static class BlueprintOverlay
    {
        public static int FixtureCount, StairCount;
        public static readonly Color Plumb = new Color(0.25f, 0.55f, 1f), Elec = new Color(1f, 0.85f, 0.15f), Hvac = new Color(1f, 0.55f, 0.15f), Fire = new Color(1f, 0.2f, 0.2f), Tread = new Color(0.85f, 0.85f, 0.9f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() { PlanOverlay.Shown -= OnShown; PlanOverlay.Shown += OnShown; }

        static void OnShown(HousePlan plan, PlanPlacement at, int floor)
        {
            FixtureCount = StairCount = 0;
            var bp = Blueprints.Info(plan);
            if (bp == null || PlanOverlay.Root == null) return;
            var k = new LandModel();
            int pl = k.M(Plumb), el = k.M(Elec), hv = k.M(Hvac), fi = k.M(Fire), tr = k.M(Tread);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rot = Quaternion.LookRotation(at.Y, Vector3.up);
            // stairs: treads at their real depth, rising toward the far end
            foreach (var s in bp.Stairs)
            {
                if (s.Floor != floor) continue;
                StairCount++;
                bool alongY = s.AlongY; float len = alongY ? s.Ft.height : s.Ft.width, wid = alongY ? s.Ft.width : s.Ft.height;
                float start = 3f;  // bottom landing
                int n = s.Treads; float td = s.TreadIn / 12f;
                for (int i = 0; i <= n; i++)
                {
                    float d = start + i * td; if (d > len) break;
                    if (s.Reverse) d = len - d;
                    Vector2 a = alongY ? new Vector2(s.Ft.xMin + 0.2f, s.Ft.yMin + d) : new Vector2(s.Ft.xMin + d, s.Ft.yMin + 0.2f);
                    Vector2 b = alongY ? new Vector2(s.Ft.xMax - 0.2f, s.Ft.yMin + d) : new Vector2(s.Ft.xMin + d, s.Ft.yMax - 0.2f);
                    var wa = at.World(a.x, a.y, 0.03f); var wb = at.World(b.x, b.y, 0.03f);
                    k.B.AddBox((wa + wb) / 2f, new Vector3(0.025f, 0.03f, (wb - wa).magnitude), Quaternion.LookRotation(wb - wa, Vector3.up), tr);
                }
                var c = s.Ft.center;
                Label(font, at, c, $"UP {s.Risers}R @ {s.RiserIn:0.##}\"\n{s.Treads}T @ {s.TreadIn:0}\"" + (s.Split ? "\nsplit entry" : ""), 0.045f, Tread);
            }
            // fixtures
            foreach (var x in bp.Fixtures)
            {
                if (x.Floor != floor) continue;
                FixtureCount++;
                int m = x.Trade == "Plumbing" ? pl : x.Trade == "Electrical" ? el : x.Trade == "Fire" ? fi : hv;
                float sz = x.Kind == "SUP" || x.Kind == "RET" ? 0.25f : x.Kind == "RTU" ? 1.2f : 0.4f;
                var p = at.World(x.Ft.x, x.Ft.y, 0.06f);
                k.B.AddBox(p, new Vector3(sz, 0.08f, sz * (x.Kind == "SUP" ? 0.4f : 1f)), rot, m);
                if (x.Kind != "SUP") Label(font, at, x.Ft + new Vector2(0, 0.7f), x.Kind, 0.04f, x.Trade == "Plumbing" ? Plumb : x.Trade == "Electrical" ? Elec : x.Trade == "Fire" ? Fire : Hvac);
            }
            foreach (var kv in bp.LevelOffsetFt)
            {
                var r = bp.Rooms.Find(q => q.Name == kv.Key && q.Floor == floor);
                if (r != null) Label(font, at, r.Ft.center + new Vector2(0, -1.6f), $"{(kv.Value >= 0 ? "+" : "-")}{Mathf.Abs(kv.Value):0.#}' level", 0.05f, new Color(1f, 0.85f, 0.3f));
            }
            Label(font, at, new Vector2(bp.WidthFt / 2f, -6.5f), $"{bp.Occupancy} · {bp.ConstructionType} · {bp.WallFrame} walls, {bp.RoofFrame} roof", 0.06f, new Color(1f, 0.85f, 0.3f));
            k.Make(PlanOverlay.Root.transform, "BlueprintLayers");
            PlanLabelLayout.Arrange(plan, bp, at, floor);   // move room labels off the fixture markers
        }

        static void Label(Font font, PlanPlacement at, Vector2 ft, string text, float size, Color c)
        {
            var go = new GameObject("BlueprintLabel");
            go.transform.SetParent(PlanOverlay.Root.transform, false);
            go.transform.position = at.World(ft.x, ft.y, 0.07f);
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, at.Y);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font; tm.text = text; tm.characterSize = size; tm.fontSize = 48; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = c;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }
}
