using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Countertop on a run of base cabinets. Local frame like the cabinets: x along Cross(up, N) from the run's
    /// x-min end, y up from the cabinet tops, z out from the finished wall.</summary>
    public class Countertop : MonoBehaviour
    {
        public static readonly List<Countertop> All = new List<Countertop>();
        public CounterType Type;
        public FramePlane Face;
        public float Length, Depth, Thick, FrontOverhang, OverhangXMin, OverhangXMax, TopY;
        public float RunXMin, RunXMax;          // cabinet run extent in local x (without overhang)
        public bool HasSink;
        public List<CabinetUnit> Bases = new List<CabinetUnit>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
    }

    public class Backsplash : MonoBehaviour
    {
        public static readonly List<Backsplash> All = new List<Backsplash>();
        public int Tiles, Cuts; public float Height; public string Grout;
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
    }

    public static class Countertops
    {
        public static readonly float Depth = Units.In(25.5f), FreeEndOverhang = Units.In(1f);
        public static float Thickness(CounterType t) => t == CounterType.Laminate || t == CounterType.ButcherBlock ? Units.In(1.5f) : 0.03f;
        static string StockId(CounterType t) => t == CounterType.Laminate ? "fin-ct-laminate" : t == CounterType.ButcherBlock ? "fin-ct-butcher" : t == CounterType.Granite ? "fin-ct-granite" : "fin-ct-quartz";
        static string Mat(CounterType t) => t == CounterType.Laminate ? InteriorMaterials.Laminate : t == CounterType.ButcherBlock ? InteriorMaterials.Butcher : t == CounterType.Granite ? InteriorMaterials.Granite : InteriorMaterials.Quartz;

        /// <summary>Countertop over the base cabinets of a run (25-1/2" deep: 1" past the doors; 1" past a free end, flush at a wall),
        /// with an undermount sink cutout + faucet over the sink base (if any).</summary>
        public static Countertop Install(IList<CabinetUnit> run, CounterType type = CounterType.Granite)
        {
            var bases = new List<CabinetUnit>();
            foreach (var c in run) if (c != null && !c.IsWall) bases.Add(c);
            if (bases.Count == 0) { Game.Toast("Set base cabinets first"); return null; }
            var first = bases[0];
            var rot = first.transform.rotation;
            // run extent in the first cabinet's local x
            float xMin = float.MaxValue, xMax = float.MinValue;
            foreach (var b in bases)
            {
                float x0 = first.transform.InverseTransformPoint(b.transform.position).x;
                xMin = Mathf.Min(xMin, x0); xMax = Mathf.Max(xMax, x0 + b.Width);
            }
            Vector3 R = first.transform.right;
            float topY = first.BottomY + Cabinets.BaseH;
            Vector3 origin = first.transform.position + R * xMin; origin.y = topY;
            float L = xMax - xMin;
            float oMin = WallAt(origin + Vector3.up * 0.05f + first.transform.forward * 0.3f, -R) ? 0f : FreeEndOverhang;
            float oMax = WallAt(origin + R * L + Vector3.up * 0.05f + first.transform.forward * 0.3f, R) ? 0f : FreeEndOverhang;
            float T = Thickness(type), total = L + oMin + oMax;
            int feet = Mathf.CeilToInt(total / Units.Foot - 0.01f);
            if (!FinishStock.Take(StockId(type), feet)) return null;

            var go = new GameObject("Countertop_" + type);
            go.transform.SetPositionAndRotation(origin, rot);
            var ct = go.AddComponent<Countertop>();
            ct.Type = type; ct.Face = first.Face; ct.Length = total; ct.Depth = Depth; ct.Thick = T; ct.TopY = topY + T;
            ct.FrontOverhang = Depth - first.DoorFaceZ; ct.OverhangXMin = oMin; ct.OverhangXMax = oMax; ct.RunXMin = 0f; ct.RunXMax = L;
            ct.Bases.AddRange(bases);

            var holes = new List<Rect>();
            CabinetUnit sink = bases.Find(b => b.IsSinkBase);
            float sinkX = 0f, sinkW = 0f;
            if (sink != null)
            {
                if (!FinishStock.Take("fin-sink-ss")) sink = null;
                else
                {
                    sinkX = first.transform.InverseTransformPoint(sink.transform.position).x - xMin + sink.Width * 0.5f;
                    sinkW = Mathf.Min(sink.Width - Units.In(6f), Units.In(27f));
                    holes.Add(Rect.MinMaxRect(sinkX - sinkW * 0.5f, Depth - Units.In(3f) - Units.In(16f), sinkX + sinkW * 0.5f, Depth - Units.In(3f)));
                    ct.HasSink = true;
                }
            }
            var mb = new MeshBuilder(); mb.MinSubmeshCount = 3;
            mb.Transform = Matrix4x4.TRS(new Vector3(0f, T, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            PanelMesh.AddSlab(mb, Rect.MinMaxRect(-oMin, 0f, L + oMax, Depth), holes, 0f, T, 0, 0, 0, Vector3.right);
            mb.ResetTransform();
            if (sink != null)
            {
                var h = holes[0];
                mb.AddWell(new Vector3(h.center.x, -0.001f, h.center.y), new Vector2(h.width + Units.In(0.5f), h.height + Units.In(0.5f)), new Vector2(h.width - Units.In(2f), h.height - Units.In(2f)), Units.In(9f), 1);
                Faucet(mb, new Vector3(sinkX, T, h.yMin - Units.In(2.25f)), 2);
            }
            var vis = new GameObject("Visual"); vis.transform.SetParent(go.transform, false);
            vis.AddComponent<MeshFilter>().sharedMesh = mb.Build("Countertop");
            vis.AddComponent<MeshRenderer>().sharedMaterials = new[] { FinishMaterials.Get(Mat(type)), Materials.Get(SurfaceKind.BrushedSteel), Materials.Get(SurfaceKind.Nickel) };
            var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3((L + oMax - oMin) * 0.5f, T * 0.5f, Depth * 0.5f); bc.size = new Vector3(total, T, Depth);
            go.AddComponent<FinishLayer>().Kind = "fin-cab-top";
            Game.Toast($"{type} countertop set: {Units.Format(total)} long, {Units.Format(ct.FrontOverhang)} overhang{(ct.HasSink ? ", sink cut out" : "")}");
            return ct;
        }

        static bool WallAt(Vector3 from, Vector3 dir)
        {
            foreach (var h in FinishAim.SortedHits(new Ray(from, dir), Units.In(2f)))
            {
                var fl = h.collider.GetComponentInParent<FinishLayer>();
                if (fl != null && (fl.Kind ?? "").StartsWith("fin-")) continue;
                return true;
            }
            return false;
        }

        static void Faucet(MeshBuilder mb, Vector3 b, int sub)
        {
            float I(float x) => Units.In(x);
            mb.AddCylinder(b + Vector3.up * I(0.75f), I(1.1f), I(1.5f), 16, sub);
            mb.AddCylinder(b + Vector3.up * I(7.5f), I(0.55f), I(12f), 14, sub, false);
            // gooseneck: arc from the top of the riser forward over the bowl
            Vector3 c = b + Vector3.up * I(13.5f) + Vector3.forward * I(4f);
            Vector3 prev = b + Vector3.up * I(13.5f);
            for (int i = 1; i <= 8; i++)
            {
                float a = Mathf.PI - i * Mathf.PI / 8f;
                Vector3 p = c + new Vector3(0f, Mathf.Sin(a) * I(4f), Mathf.Cos(a) * I(4f));
                Vector3 d = p - prev;
                mb.AddRod((p + prev) * 0.5f, Quaternion.LookRotation(d.normalized, Vector3.up), I(0.5f), d.magnitude + I(0.05f), 12, sub);
                prev = p;
            }
            mb.AddRod(prev + Vector3.down * I(1f), Quaternion.LookRotation(Vector3.down, Vector3.forward), I(0.55f), I(2f), 12, sub);
            mb.AddRod(b + new Vector3(I(3.2f), I(3f), 0f), Quaternion.LookRotation(Vector3.right, Vector3.up), I(0.3f), I(2.5f), 10, sub); // side handle
        }

        /// <summary>3x6 subway tile backsplash in running bond with 1/16" joints from the countertop up to the wall cabinets
        /// (or 18"), across the run; mastic, tile saw cuts at the ends/top row, sanded grout. </summary>
        public static Backsplash InstallBacksplash(Countertop ct, string grout = "Bright white", float height = -1f)
        {
            if (ct == null) { Game.Toast("Install the countertop first"); return null; }
            if (!FinishStock.Require("tile-saw", "cut the backsplash tiles")) return null;
            if (!FinishStock.Require("grout-float", "grout the backsplash")) return null;
            if (height < 0f)
            {
                height = Units.In(18f);
                float wallBottom = float.MaxValue;
                foreach (var c in CabinetUnit.All) if (c != null && c.IsWall && c.Face != null && ct.Face != null && c.Face.SameFace(ct.Face)) wallBottom = Mathf.Min(wallBottom, c.BottomY);
                if (wallBottom < 1e5f) height = wallBottom - ct.TopY;
            }
            float tw = Units.In(6f), th = Units.In(3f), j = Units.In(0.0625f), T = Units.In(0.3125f), mastic = Units.In(0.0625f);
            float x0 = ct.RunXMin, x1 = ct.RunXMax;
            var mb = new MeshBuilder(); mb.MinSubmeshCount = 2;
            mb.Transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            var rnd = new System.Random(7);
            int tiles = 0, cuts = 0, row = 0;
            for (float y = 0f; y < height - Units.In(0.5f); y += th + j, row++)
            {
                float ya = y, yb = Mathf.Min(y + th, height);
                if (yb < th - 1e-4f + ya) cuts++;
                float shift = (row % 2) * (tw + j) * 0.5f;
                for (float x = x0 - shift; x < x1; x += tw + j)
                {
                    float xa = Mathf.Max(x, x0), xb = Mathf.Min(x + tw, x1);
                    if (xb - xa < Units.In(0.5f)) continue;
                    if (xa > x + 1e-4f || xb < x + tw - 1e-4f) cuts++;
                    tiles++;
                    mb.UvOffset = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
                    // tile local (x, thickness, z) -> wall local (x, -z, thickness) under Euler(90,0,0)
                    FinishGeom.EasedTile(mb, xa, -yb, xb, -ya, mastic, mastic + T, 0.0022f, 0);
                }
            }
            mb.UvOffset = Vector2.zero;
            FinishGeom.Box(mb, x0, x1, 0f, mastic + T - 0.0012f, -height, 0f, 1);  // grout bed behind / in the joints
            mb.ResetTransform();
            InteriorSupplies.UseFasteners("fin-tile-subway", tiles);
            InteriorSupplies.UseAmount("fin-mastic", (x1 - x0) * height / (Units.Foot * Units.Foot), 30f);
            InteriorSupplies.UseAmount("fin-grout", (x1 - x0) * height / (Units.Foot * Units.Foot), 100f);
            if (!TileWork.GroutColors.TryGetValue(grout, out var gc)) gc = TileWork.GroutColors["Bright white"];
            var go = new GameObject("Backsplash");
            Vector3 o = ct.transform.position + Vector3.up * ct.Thick;
            go.transform.SetPositionAndRotation(o, ct.transform.rotation);
            go.transform.position += ct.transform.forward * 0f;
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build("Backsplash");
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { FinishMaterials.Get(InteriorMaterials.Subway), FinishMaterials.Get(InteriorMaterials.Grout, gc) };
            go.AddComponent<FinishLayer>().Kind = "fin-backsplash";
            var bs = go.AddComponent<Backsplash>(); bs.Tiles = tiles; bs.Cuts = cuts; bs.Height = height; bs.Grout = grout;
            Game.Toast($"Backsplash: {tiles} subway tiles ({cuts} cuts), grouted {grout}");
            return bs;
        }
    }
}
