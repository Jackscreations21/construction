using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>On-site plan overlay: low colored wall lines (blue exterior, light blue partitions), green door / cyan window
    /// openings with their rough-opening widths, room names with sizes, and corner stakes — so the player and crews can build from it.</summary>
    public static class PlanOverlay
    {
        public static GameObject Root { get; private set; }
        public static HousePlan Plan { get; private set; }
        public static readonly Color ExtColor = new Color(0.15f, 0.45f, 1f), IntColor = new Color(0.45f, 0.75f, 1f), DoorColor = new Color(0.2f, 0.9f, 0.3f), WindowColor = new Color(0.1f, 0.95f, 0.95f);
        public static int WallCount, OpeningCount, LabelCount;

        public static void Hide() { if (Root != null) Object.Destroy(Root); Root = null; Plan = null; }

        public static GameObject Show(HousePlan plan, PlanPlacement at, int floor)
        {
            Hide();
            Plan = plan; WallCount = OpeningCount = LabelCount = 0;
            Root = new GameObject("PlanOverlay: " + plan.Name);
            var k = new LandModel();
            int ext = k.M(ExtColor), inn = k.M(IntColor), door = k.M(DoorColor), win = k.M(WindowColor);
            float h = 0.12f, y0 = 0.02f;
            for (int wi = 0; wi < plan.Walls.Count; wi++)
            {
                var w = plan.Walls[wi]; if (w.Floor != floor) continue;
                float tFt = w.Exterior ? plan.WallDepthFt : 4.5f / 12f;
                var n = new Vector2(-w.Dir.y, w.Dir.x);
                Vector2 off = w.Exterior ? n * tFt / 2f : Vector2.zero;
                var cuts = new List<(float a, float b, PlanOpening o)>();
                foreach (var o in plan.Openings) if (o.Wall == wi) cuts.Add((o.CenterFt - o.RoughWidthIn / 24f, o.CenterFt + o.RoughWidthIn / 24f, o));
                cuts.Sort((p, q) => p.a.CompareTo(q.a));
                float s = 0f;
                void Seg(float a, float b, int mat, float hh)
                {
                    if (b - a < 0.02f) return;
                    var pa = w.At(a) + off; var pb = w.At(b) + off;
                    var wa = at.World(pa.x, pa.y, y0 + hh / 2f); var wb = at.World(pb.x, pb.y, y0 + hh / 2f);
                    k.B.AddBox((wa + wb) / 2f, new Vector3(tFt * Units.Foot, hh, (wb - wa).magnitude), Quaternion.LookRotation(wb - wa, Vector3.up), mat);
                }
                foreach (var c in cuts) { Seg(s, c.a, w.Exterior ? ext : inn, h); Seg(c.a, c.b, c.o.IsDoor ? door : win, h * 0.5f); s = c.b; OpeningCount++; }
                Seg(s, w.LengthFt, w.Exterior ? ext : inn, h);
                WallCount++;
            }
            // corner stakes
            foreach (var c in new[] { new Vector2(0, 0), new Vector2(plan.WidthFt, 0), new Vector2(plan.WidthFt, plan.DepthFt), new Vector2(0, plan.DepthFt) })
                LandModels.Stake(k, at.World(c.x, c.y));
            k.Make(Root.transform, "PlanLines");
            // labels
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (var r in plan.Rooms)
            {
                if (r.Floor != floor) continue;
                Label(font, at.World(r.Ft.center.x, r.Ft.center.y, 0.05f), at, $"{r.Name}\n{r.Ft.width:0}' x {r.Ft.height:0}'", 0.09f, Color.white);
                LabelCount++;
            }
            foreach (var o in plan.Openings)
            {
                if (o.Floor != floor) continue;
                var w = plan.Walls[o.Wall]; var n = new Vector2(-w.Dir.y, w.Dir.x);
                var p = w.At(o.CenterFt) + n * (w.Exterior ? 1.6f : 1.0f);
                Label(font, at.World(p.x, p.y, 0.05f), at, (o.IsDoor ? "D " : "W ") + $"{o.RoughWidthIn:0.#}\" RO", 0.05f, o.IsDoor ? DoorColor : WindowColor);
            }
            Label(font, at.World(plan.WidthFt / 2f, -4f, 0.05f), at, plan.Name + (plan.Floors > 1 ? $"  (floor {floor + 1})" : ""), 0.12f, new Color(1f, 0.85f, 0.3f));
            return Root;
        }

        static void Label(Font font, Vector3 pos, PlanPlacement at, string text, float size, Color c)
        {
            var go = new GameObject("PlanLabel");
            go.transform.SetParent(Root.transform, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(-Vector3.up, at.Y) * Quaternion.Euler(0, 0, 0);
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, at.Y);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font; tm.text = text; tm.characterSize = size; tm.fontSize = 48; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = c;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }

    /// <summary>Everything the Development feature shows in the world: the fallback road and lot stakes, subdivision streets,
    /// and a procedural model of each off-site house at its real stage (slab, studs, trusses, sheathing, wrap, roof, siding, yard).</summary>
    public static class DevVisuals
    {
        public static GameObject Root { get; private set; }
        public static readonly Dictionary<string, GameObject> Houses = new Dictionary<string, GameObject>();
        public static bool Enabled = true;

        static bool _dirty;
        /// <summary>Marks the world for rebuild at the end of the frame.</summary>
        public static void Refresh() => _dirty = true;
        internal static void LateTick() { if (_dirty) { _dirty = false; RefreshNow(); } }

        public static void RefreshNow()
        {
            _dirty = false;
            if (Root != null) Object.Destroy(Root);
            Houses.Clear(); Root = null;
            if (!Enabled || !Application.isPlaying) return;
            bool any = Projects.All.Count > 0 || Developments.All.Count > 0;
            if (!any) return;
            Root = new GameObject("DevelopmentWorld");
            var k = new LandModel();
            int asphalt = k.M(SurfaceKind.Concrete, new Color(0.32f, 0.32f, 0.34f)), conc = k.M(SurfaceKind.BroomedConcrete), gravel = k.M(SurfaceKind.Gravel);
            // fallback Ridge Rd + county road (only when the fallback provider is in use)
            if (DevLots.Provider is FallbackLotProvider)
            {
                Road(k, new Vector3(420f, 0, FallbackLotProvider.RidgeZ), new Vector3(905f, 0, FallbackLotProvider.RidgeZ), 1f, asphalt, conc);
                Road(k, new Vector3(420f, 0, 600f), new Vector3(1050f, 0, 600f), 1f, asphalt, conc);
            }
            foreach (var d in Developments.All)
            {
                if (!d.Platted) { StakeRect(k, d.Tract.Rect); continue; }
                var t = d.Tract; Vector3 back = -t.Front;
                var a = t.FrontMid; var b = t.FrontMid + back * (Mathf.Max(d.StreetRect.width, d.StreetRect.height));
                if (d.SiteWorkStartDay < 0) StakeLine(k, a, b);
                else if (!d.SiteWorkDone) Road(k, a, Vector3.Lerp(a, b, Mathf.Max(0.05f, d.SiteWorkProgress)), 1f, gravel, gravel);
                else Road(k, a, b, 1f, asphalt, conc);
                foreach (var l in d.Lots) StakeRect(k, l.Rect);
            }
            foreach (var p in Projects.All)
            {
                if (p.Lot == null) continue;
                if (p.Kind == ProjectKind.Client) StakeRect(k, p.Lot.Rect);
                if (p.Id == Projects.OnSiteId) continue;   // the player builds this one for real
                Houses[p.Id] = HouseModel(Root.transform, p);
            }
            k.Make(Root.transform, "SiteLines");
        }

        static void Road(LandModel k, Vector3 a, Vector3 b, float k2, int surface, int curb)
        {
            var dir = (b - a); float len = dir.magnitude; if (len < 0.1f) return;
            var r = Quaternion.LookRotation(dir, Vector3.up); var c = (a + b) / 2f;
            var side = Vector3.Cross(Vector3.up, dir.normalized);
            float pave = Developments.PavementFt * Units.Foot;
            k.B.AddBox(c + Vector3.up * 0.02f, new Vector3(pave, 0.04f, len), r, surface);
            foreach (int s in new[] { -1, 1 })
            {
                k.B.AddBox(c + side * s * (pave / 2f + 0.08f) + Vector3.up * 0.075f, new Vector3(0.15f, 0.15f, len), r, curb);
                k.B.AddBox(c + side * s * (pave / 2f + 1.37f + 0.76f) + Vector3.up * 0.05f, new Vector3(1.52f, 0.1f, len), r, curb); // 5' walk past a 4' strip
            }
        }

        static void StakeRect(LandModel k, Rect r)
        {
            var c = new[] { new Vector3(r.xMin, 0, r.yMin), new Vector3(r.xMax, 0, r.yMin), new Vector3(r.xMax, 0, r.yMax), new Vector3(r.xMin, 0, r.yMax) };
            for (int i = 0; i < 4; i++) { LandModels.Stake(k, c[i]); LandModels.String(k, c[i] + Vector3.up * 0.4f, c[(i + 1) % 4] + Vector3.up * 0.4f, new Color(1f, 0.3f, 0.65f)); }
        }
        static void StakeLine(LandModel k, Vector3 a, Vector3 b) { LandModels.Stake(k, a); LandModels.Stake(k, b); LandModels.String(k, a + Vector3.up * 0.4f, b + Vector3.up * 0.4f, new Color(1f, 0.55f, 0.1f)); }

        // ------------------------------------------------------------------ house at its stage

        static float F(HouseProject p, BuildPhase b) => p.PhaseDone(b) ? 1f : p.Progress[(int)b];

        public static GameObject HouseModel(Transform parent, HouseProject p)
        {
            var plan = p.Plan; var at = p.Placement;
            var go = new GameObject("House " + p.Lot.Address + " (" + p.StatusLabel + ")");
            go.transform.SetParent(parent, false);
            var k = new LandModel();
            int conc = k.M(SurfaceKind.Concrete), pine = k.M(SurfaceKind.PineLumber), osb = k.M(SurfaceKind.Osb), wrap = k.M(SurfaceKind.PaintedWhite, new Color(0.95f, 0.97f, 1f));
            int shingle = k.M(SurfaceKind.MatteBlack, new Color(1.5f, 1.5f, 1.55f)), glass = k.M(SurfaceKind.Glass), trim = k.M(SurfaceKind.PaintedWhite);
            Color[] sidingColors = { new Color(0.78f, 0.82f, 0.86f), new Color(0.86f, 0.80f, 0.66f), new Color(0.55f, 0.63f, 0.55f), new Color(0.88f, 0.86f, 0.80f), new Color(0.50f, 0.56f, 0.66f) };
            int hsh = 0; foreach (char ch in p.LotId) hsh += ch;
            int siding = k.M(SurfaceKind.PaintedWhite, sidingColors[hsh % sidingColors.Length]), door = k.M(SurfaceKind.Oak), grass = k.M(SurfaceKind.Grass), dirt = k.M(SurfaceKind.Dirt);
            float f0 = F(p, BuildPhase.Foundation), f1 = F(p, BuildPhase.Framing), f2 = F(p, BuildPhase.RoughIn), f3 = F(p, BuildPhase.Drywall), f4 = F(p, BuildPhase.Finish);
            float W = plan.WidthFt, D = plan.DepthFt, ft = Units.Foot, H = plan.WallHeightFt * ft, elev = 0.25f, th = plan.WallDepthFt;
            Quaternion rot = at.Rotation;

            // site: excavated dirt pad, then yard
            if (f4 >= 0.6f) k.B.AddBox(at.World(W / 2, D / 2, 0.01f), new Vector3((W + 30) * ft, 0.02f, (D + 40) * ft), rot, grass);
            else k.B.AddBox(at.World(W / 2, D / 2, 0.01f), new Vector3((W + 10) * ft, 0.02f, (D + 10) * ft), rot, dirt);
            if (f0 <= 0f) { k.Make(go.transform, "House"); return go; }
            // foundation (grows front to back)
            float fd = D * Mathf.Clamp01(f0);
            k.B.AddBox(at.World(W / 2, fd / 2, elev / 2), new Vector3(W * ft, elev, fd * ft), rot, conc);
            if (f1 > 0f)
            {
                float frameShare = Mathf.Clamp01(f1 / 0.75f);
                int floorsFramed = Mathf.Max(1, Mathf.CeilToInt(frameShare * plan.Floors));
                for (int fl = 0; fl < floorsFramed; fl++)
                {
                    float baseY = elev + fl * (H + 0.3f);
                    if (fl > 0) k.B.AddBox(at.World(W / 2, D / 2, baseY - 0.15f), new Vector3(W * ft, 0.3f, D * ft), rot, osb);
                    var walls = plan.Walls.FindAll(w => w.Floor == fl);
                    float share = Mathf.Clamp01(frameShare * plan.Floors - fl);
                    int n = Mathf.CeilToInt(walls.Count * share);
                    for (int i = 0; i < n; i++)
                    {
                        var w = walls[i]; int wi = plan.Walls.IndexOf(w);
                        bool solid = w.Exterior && f1 >= 1f;
                        var nrm = new Vector2(-w.Dir.y, w.Dir.x);
                        float t = w.Exterior ? th : 3.5f / 12f;
                        Vector2 off = w.Exterior ? nrm * t / 2f : Vector2.zero;
                        if (!solid)
                        {
                            for (float s = 0; s <= w.LengthFt; s += 16f / 12f)
                            {
                                var q = w.At(Mathf.Min(s, w.LengthFt - 0.06f)) + off;
                                k.B.AddBox(at.World(q.x, q.y, baseY + H / 2), new Vector3(t * ft, H, 1.5f * Units.Inch), Quaternion.LookRotation(at.X * w.Dir.x + at.Y * w.Dir.y, Vector3.up), pine);
                            }
                            var pa = w.At(0) + off; var pb = w.At(w.LengthFt) + off;
                            var wa = at.World(pa.x, pa.y); var wb = at.World(pb.x, pb.y);
                            var r2 = Quaternion.LookRotation(wb - wa, Vector3.up);
                            k.B.AddBox((wa + wb) / 2 + Vector3.up * (baseY + 0.02f), new Vector3(t * ft, 0.04f, (wb - wa).magnitude), r2, pine);
                            k.B.AddBox((wa + wb) / 2 + Vector3.up * (baseY + H - 0.04f), new Vector3(t * ft, 0.08f, (wb - wa).magnitude), r2, pine);
                        }
                        else
                        {
                            int skin = f3 >= 0.3f ? siding : f2 >= 0.5f ? wrap : osb;
                            SolidWall(k, plan, wi, at, baseY, H, t, skin, glass, door, f2 >= 0.3f, f4 >= 0.5f);
                        }
                    }
                }
                // roof: trusses after the walls, sheathing when framing is done, shingles at dry-in (rough-in)
                if (f1 >= 0.75f)
                {
                    float top = elev + plan.Floors * H + (plan.Floors - 1) * 0.3f;
                    float rise = (D / 2f + 1f) * plan.Pitch / 12f;
                    if (f1 < 1f)
                    {
                        int trusses = Mathf.FloorToInt(W * 12f / 24f) + 1;
                        int show = Mathf.CeilToInt(trusses * Mathf.Clamp01((f1 - 0.75f) / 0.25f));
                        for (int i = 0; i < show; i++)
                        {
                            float x = Mathf.Min(W, i * 2f);
                            Truss(k, at, x, D, top, rise, pine);
                        }
                    }
                    else Roof(k, plan, at, top, rise, f2 > 0.2f ? shingle : osb, f3 >= 0.3f ? siding : f2 >= 0.5f ? wrap : osb, trim);
                }
            }
            if (f4 >= 0.6f)
            {
                // driveway + front walk
                k.B.AddBox(at.World(W + 6f, -12f, 0.03f), new Vector3(12f * ft, 0.06f, 26f * ft), rot, conc);
                k.B.AddBox(at.World(plan.FrontDoorFt, -8f, 0.03f), new Vector3(4f * ft, 0.06f, 16f * ft), rot, conc);
            }
            k.Make(go.transform, "House");
            if (f4 >= 1f)
            {
                for (int i = 0; i < 4; i++)
                {
                    var s = new GameObject("Shrub").transform; s.SetParent(go.transform, false);
                    s.position = at.World(3f + i * (W - 6f) / 3f, -2.5f);
                    LandModels.Instance("dev-shrub" + (i % 2), s, kk => LandModels.Shrub(kk, 300 + i % 2, 1.1f, 0.9f, new Color(0.8f, 1f, 0.8f)));
                }
            }
            return go;
        }

        static void Truss(LandModel k, PlanPlacement at, float x, float D, float top, float rise, int mat)
        {
            var a = at.World(x, -1f, top); var b = at.World(x, D + 1f, top); var r = at.World(x, D / 2f, top + rise);
            void Stick(Vector3 p, Vector3 q) => k.B.AddBox((p + q) / 2, new Vector3(1.5f * Units.Inch, 3.5f * Units.Inch, (q - p).magnitude), Quaternion.LookRotation(q - p, Vector3.up), mat);
            Stick(a, b); Stick(a, r); Stick(r, b); Stick((a + r) / 2, at.World(x, D / 2f, top)); Stick((r + b) / 2, at.World(x, D / 2f, top));
        }

        static void Roof(LandModel k, HousePlan plan, PlanPlacement at, float top, float rise, int roofMat, int gableMat, int trimMat)
        {
            float W = plan.WidthFt, D = plan.DepthFt, o = 1f;
            var fl = at.World(-o, -o, top); var fr = at.World(W + o, -o, top); var bl = at.World(-o, D + o, top); var br = at.World(W + o, D + o, top);
            float inset = plan.Roof == RoofType.Hip ? Mathf.Min(D / 2f + o, W / 2f) : -o;
            var rl = at.World(inset, D / 2f, top + rise); var rr = at.World(W - inset, D / 2f, top + rise);
            var up = Vector3.up * 0.12f; // sheathing + shingle thickness
            k.B.AddQuad(fl + up, rl + up, rr + up, fr + up, roofMat, -at.Y + Vector3.up);
            k.B.AddQuad(br + up, rr + up, rl + up, bl + up, roofMat, at.Y + Vector3.up);
            if (plan.Roof == RoofType.Hip)
            {
                k.B.AddTriangle(bl + up, rl + up, fl + up, roofMat, -at.X + Vector3.up);
                k.B.AddTriangle(fr + up, rr + up, br + up, roofMat, at.X + Vector3.up);
            }
            else
            {
                k.B.AddTriangle(at.World(0, D, top), at.World(0, D / 2f, top + rise - 0.1f), at.World(0, 0, top), gableMat, -at.X);
                k.B.AddTriangle(at.World(W, 0, top), at.World(W, D / 2f, top + rise - 0.1f), at.World(W, D, top), gableMat, at.X);
            }
            // fascia
            k.B.AddBox((fl + fr) / 2 + Vector3.down * 0.08f, new Vector3(0.03f, 0.2f, (fr - fl).magnitude), Quaternion.LookRotation(fr - fl, Vector3.up), trimMat);
            k.B.AddBox((bl + br) / 2 + Vector3.down * 0.08f, new Vector3(0.03f, 0.2f, (br - bl).magnitude), Quaternion.LookRotation(br - bl, Vector3.up), trimMat);
        }

        /// <summary>A sheathed exterior wall with real rough openings; windows glazed once installed, doors hung at finish.</summary>
        static void SolidWall(LandModel k, HousePlan plan, int wi, PlanPlacement at, float baseY, float H, float tFt, int skin, int glass, int doorMat, bool windows, bool doors)
        {
            var w = plan.Walls[wi]; var nrm = new Vector2(-w.Dir.y, w.Dir.x); var off = nrm * tFt / 2f;
            var dirW = (at.X * w.Dir.x + at.Y * w.Dir.y).normalized; var r = Quaternion.LookRotation(dirW, Vector3.up);
            float t = tFt * Units.Foot;
            void Box(float a, float b, float y0, float y1, int m, float thick)
            {
                if (b - a < 0.01f || y1 - y0 < 0.01f) return;
                var c = w.At((a + b) / 2f) + off;
                k.B.AddBox(at.World(c.x, c.y, baseY + (y0 + y1) / 2f), new Vector3(thick, y1 - y0, (b - a) * Units.Foot), r, m);
            }
            var cuts = new List<PlanOpening>(); foreach (var o in plan.Openings) if (o.Wall == wi) cuts.Add(o);
            cuts.Sort((p, q) => p.CenterFt.CompareTo(q.CenterFt));
            float s = 0f, head = WallOpening.HeaderBottomIn * Units.Inch;
            foreach (var o in cuts)
            {
                float a = o.CenterFt - o.RoughWidthIn / 24f, b = o.CenterFt + o.RoughWidthIn / 24f;
                float sill = o.IsDoor ? 0f : head - o.RoughHeightIn * Units.Inch;
                Box(s, a, 0, H, skin, t);
                Box(a, b, head, H, skin, t);
                if (sill > 0) Box(a, b, 0, sill, skin, t);
                if (o.IsDoor ? doors : windows) Box(a, b, sill, head, o.IsDoor ? doorMat : glass, o.IsDoor ? 0.045f : 0.02f);
                s = b;
            }
            Box(s, w.LengthFt, 0, H, skin, t);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { Root = null; Houses.Clear(); Enabled = true; _dirty = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Projects.Changed += _ => Refresh();
            var go = new GameObject("DevelopmentRunner"); Object.DontDestroyOnLoad(go); go.AddComponent<DevRunner>();
        }
    }

    public class DevRunner : MonoBehaviour { void LateUpdate() => DevVisuals.LateTick(); }
}
