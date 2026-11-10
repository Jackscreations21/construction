using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum CabFinish { Oak, Walnut, PaintedWhite }
    public enum CounterType { Laminate, ButcherBlock, Granite, Quartz }

    /// <summary>One installed face-frame cabinet (base or wall). Local frame: x along the wall (0..Width, toward
    /// Cross(up, N)), y up from the cabinet bottom, z out from the finished wall (0 = wall).</summary>
    public class CabinetUnit : MonoBehaviour
    {
        public static readonly List<CabinetUnit> All = new List<CabinetUnit>();
        public bool IsWall;
        public int WidthIn;
        public float Width, Height, BoxDepth;     // meters; BoxDepth = carcass + face frame (doors add 3/4")
        public CabFinish Finish;
        public FramePlane Face;
        public float BottomY;                     // world y of the cabinet bottom
        public int Screws, ScrewsInStuds;
        public bool Shimmed;
        public float LevelError;                  // meters across the width
        public bool IsFiller, IsSinkBase;
        public float DoorFaceZ => BoxDepth + Units.In(0.75f);

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        public Vector3 Local(float x, float y, float z) => transform.TransformPoint(new Vector3(x, y, z));
    }

    /// <summary>Kitchen cabinets: base (34-1/2" H x 24" D, 4-1/2" x 3" toe kick) and wall (30" H x 12" D, bottom at 54" AFF)
    /// face-frame cabinets with shaker doors, drawer fronts, bar pulls; shimmed level, screwed through the hang rails into studs.</summary>
    public static class Cabinets
    {
        public static readonly float BaseH = Units.In(34.5f), BaseBox = Units.In(23.75f), WallH = Units.In(30f), WallBox = Units.In(11.25f);
        public static readonly float WallBottomAff = Units.In(54f), ToeH = Units.In(4.5f), ToeD = Units.In(3f), Frame = Units.In(1.5f), DoorT = Units.In(0.75f);

        /// <summary>
        /// Installs a run of cabinets along a wall face starting at <paramref name="startU"/> (finished-corner side), running +U:
        /// optional filler strip first (inches), then the base cabinets (widths in inches, 12-36 in 3" steps) and, above them,
        /// the wall cabinets (null = same widths). Needs the drill and the level; screws go through the hang rails into studs.
        /// </summary>
        public static List<CabinetUnit> InstallRun(FramePlane wall, float startU, IList<int> baseWidths, IList<int> wallWidths = null,
            CabFinish finish = CabFinish.Oak, float fillerIn = 0f, int sinkIndex = -1, bool shim = true, bool blackHardware = false)
        {
            var res = new List<CabinetUnit>();
            if (wall == null || !wall.IsWall) { Game.Toast("Aim at a wall"); return res; }
            if (!FinishStock.Require("fin-drill", "screw cabinets to the studs")) return res;
            if (!FinishStock.Require("level", "set the cabinets level")) return res;
            foreach (var w in baseWidths) if (!FinishStock.Has($"fin-cab-b{w}")) { FinishStock.Take($"fin-cab-b{w}"); return res; }
            if (wallWidths == null) wallWidths = baseWidths;
            foreach (var w in wallWidths) if (w > 0 && !FinishStock.Has($"fin-cab-w{w}")) { FinishStock.Take($"fin-cab-w{w}"); return res; }
            float c = FinishHooks.CoveringThickness(wall, wall.Bounds.center);
            float floorY = WallTrim.FloorBelow(wall.World(startU + 0.3f, wall.Bounds.yMin + 0.4f, c + 0.3f), 1f, out bool ok);
            if (!ok) floorY = wall.World(0f, wall.Bounds.yMin).y;
            float u = startU;
            if (fillerIn > 0f)
            {
                if (!FinishStock.Take("fin-cab-filler")) return res;
                res.Add(Build(wall, c, u, Units.In(fillerIn), floorY, false, finish, true, false, shim, blackHardware, 0));
                res.Add(Build(wall, c, u, Units.In(fillerIn), floorY + WallBottomAff, true, finish, true, false, shim, blackHardware, 0));
                u += Units.In(fillerIn);
            }
            float u0 = u;
            for (int i = 0; i < baseWidths.Count; i++)
            {
                FinishStock.Take($"fin-cab-b{baseWidths[i]}");
                res.Add(Build(wall, c, u, Units.In(baseWidths[i]), floorY, false, finish, false, i == sinkIndex, shim, blackHardware, baseWidths[i]));
                u += Units.In(baseWidths[i]);
            }
            u = u0;
            foreach (var w in wallWidths)
            {
                if (w <= 0) { u += Units.In(-w); continue; }
                FinishStock.Take($"fin-cab-w{w}");
                res.Add(Build(wall, c, u, Units.In(w), floorY + WallBottomAff, true, finish, false, false, shim, blackHardware, w));
                u += Units.In(w);
            }
            int screws = 0; foreach (var r in res) screws += r.Screws;
            InteriorSupplies.UseFasteners("fin-cab-screws", screws);
            Game.Toast($"Cabinets hung: {res.Count} boxes, {screws} screws through the hang rails{(shim ? ", shimmed level" : "")}");
            return res;
        }

        static CabinetUnit Build(FramePlane wall, float c, float u, float W, float bottomY, bool isWall, CabFinish fin, bool filler, bool sink, bool shim, bool black, int wIn)
        {
            float H = isWall ? WallH : BaseH, D = isWall ? WallBox : BaseBox;
            if (filler) D = isWall ? WallBox : BaseBox;
            Vector3 R = Vector3.Cross(Vector3.up, wall.N).normalized; // local +x
            // run goes +U = -R; this cabinet covers U in [u, u+W] -> local x origin at U = u + W
            Vector3 o = wall.World(u + W, 0f, c); o.y = bottomY;
            var go = new GameObject((isWall ? "WallCab" : "BaseCab") + (filler ? "Filler" : wIn.ToString()));
            go.transform.SetPositionAndRotation(o, Quaternion.LookRotation(wall.N, Vector3.up));
            var cu = go.AddComponent<CabinetUnit>();
            cu.IsWall = isWall; cu.WidthIn = wIn; cu.Width = W; cu.Height = H; cu.BoxDepth = D; cu.Finish = fin; cu.Face = wall; cu.BottomY = bottomY;
            cu.IsFiller = filler; cu.IsSinkBase = sink; cu.Shimmed = shim; cu.LevelError = shim ? 0f : Units.In(0.1875f);
            if (!shim) go.transform.rotation = Quaternion.AngleAxis(Mathf.Atan2(cu.LevelError, W) * Mathf.Rad2Deg, wall.N) * go.transform.rotation;
            var mb = new MeshBuilder(); mb.MinSubmeshCount = 3;
            int fs = 0, hw = 1, box = 2;
            if (filler)
            {
                FinishGeom.Box(mb, 0f, W, isWall ? 0f : ToeH, H, D - Units.In(0.75f), D, fs, Vector3.up);
                if (!isWall) FinishGeom.Box(mb, 0f, W, 0f, ToeH, D - Units.In(0.75f) - ToeD - Units.In(0.5f), D - Units.In(0.75f) - ToeD, fs, Vector3.right);
            }
            else if (isWall) BuildWall(mb, W, fs, hw, box);
            else BuildBase(mb, W, sink, fs, hw, box);
            var vis = new GameObject("Visual");
            vis.transform.SetParent(go.transform, false);
            vis.AddComponent<MeshFilter>().sharedMesh = mb.Build(go.name);
            vis.AddComponent<MeshRenderer>().sharedMaterials = new[] { FinishMat(fin), Materials.Get(black ? SurfaceKind.MatteBlack : SurfaceKind.Nickel), FinishMaterials.Get(InteriorMaterials.CabinetBox) };
            var bc = go.AddComponent<BoxCollider>();
            if (isWall) { bc.center = new Vector3(W * 0.5f, H * 0.5f, (D + DoorT) * 0.5f); bc.size = new Vector3(W, H, D + DoorT); }
            else
            {
                // carcass above the toe kick + the toe-kick footprint (floors run into the 3" recess)
                bc.center = new Vector3(W * 0.5f, (ToeH + H) * 0.5f, (D + DoorT) * 0.5f); bc.size = new Vector3(W, H - ToeH, D + DoorT);
                var toe = go.AddComponent<BoxCollider>();
                float tz = D - Units.In(0.75f) - ToeD;
                toe.center = new Vector3(W * 0.5f, ToeH * 0.5f, tz * 0.5f); toe.size = new Vector3(W, ToeH, tz);
            }
            go.AddComponent<FinishLayer>().Kind = "fin-cab";
            // screws through the hang rail(s) into studs
            if (!filler)
            {
                var studs = wall.FramingLinesU();
                int hits = 0;
                foreach (var s in studs) if (s > u + Units.In(0.75f) && s < u + W - Units.In(0.75f)) hits++;
                int rails = isWall ? 2 : 1;
                cu.ScrewsInStuds = hits * rails;
                cu.Screws = cu.ScrewsInStuds + 2; // + face-frame screws to the neighbour
            }
            return cu;
        }

        public static Material FinishMat(CabFinish f) => f == CabFinish.Oak ? Materials.Get(SurfaceKind.Oak) : f == CabFinish.Walnut ? Materials.Get(SurfaceKind.Walnut) : FinishMaterials.Get(InteriorMaterials.Primed);

        static float I(float x) => Units.In(x);

        static void BuildBase(MeshBuilder mb, float W, bool sink, int fs, int hw, int box)
        {
            float D = BaseBox, H = BaseH, ff = D - I(0.75f), side = I(0.5f);
            // sides (notched for the toe kick), bottom, back, hang rail
            FinishGeom.Box(mb, 0f, side, ToeH, H, 0f, ff, fs, Vector3.up);
            FinishGeom.Box(mb, W - side, W, ToeH, H, 0f, ff, fs, Vector3.up);
            FinishGeom.Box(mb, 0f, side, 0f, ToeH, 0f, ff - ToeD, fs, Vector3.up);
            FinishGeom.Box(mb, W - side, W, 0f, ToeH, 0f, ff - ToeD, fs, Vector3.up);
            FinishGeom.Box(mb, side, W - side, 0f, ToeH, ff - ToeD - I(0.5f), ff - ToeD, fs, Vector3.right);      // toe kick plate
            FinishGeom.Box(mb, side, W - side, ToeH, ToeH + I(0.75f), I(0.25f), ff, box);                        // bottom
            FinishGeom.Box(mb, side, W - side, ToeH, H - I(0.75f), 0f, I(0.25f), box);                           // back
            FinishGeom.Box(mb, side, W - side, H - I(4f), H - I(0.5f), I(0.25f), I(1f), box);                    // hang rail
            FinishGeom.Box(mb, side, W - side, H - I(0.75f), H, ff - I(3.5f), ff, box);                          // front stretcher
            // face frame
            float z0 = ff, z1 = D;
            FinishGeom.Box(mb, 0f, Frame, ToeH, H, z0, z1, fs, Vector3.up);
            FinishGeom.Box(mb, W - Frame, W, ToeH, H, z0, z1, fs, Vector3.up);
            FinishGeom.Box(mb, Frame, W - Frame, H - Frame, H, z0, z1, fs, Vector3.right);                      // top rail
            FinishGeom.Box(mb, Frame, W - Frame, I(26.5f), I(28f), z0, z1, fs, Vector3.right);                  // drawer rail
            FinishGeom.Box(mb, Frame, W - Frame, ToeH, ToeH + Frame, z0, z1, fs, Vector3.right);                // bottom rail
            // dark interior behind the openings (so gaps read as depth)
            FinishGeom.Box(mb, Frame, W - Frame, ToeH + Frame, H - Frame, z0 - I(0.2f), z0 - I(0.1f), box);
            // drawer front (or tilt-out false front on a sink base) + doors, 1/2" overlay
            float df0 = I(27.5f), df1 = H - I(1.0f);
            float x0 = I(1.0f), x1 = W - I(1.0f);
            Shaker(mb, x0, x1, df0, df1, z1, fs, I(1.5f));
            float pullY = (df0 + df1) * 0.5f, pz = z1 + DoorT;
            if (!sink) Pull(mb, (x0 + x1) * 0.5f, pullY, pz, true, hw, W > I(24f) ? I(6.3f) : I(5f));
            float dy0 = ToeH + I(1.0f), dy1 = I(27.0f);
            bool two = W >= I(24f) - 0.001f;
            if (two)
            {
                float mid = W * 0.5f, g = I(0.0625f);
                Shaker(mb, x0, mid - g, dy0, dy1, z1, fs, I(2.25f));
                Shaker(mb, mid + g, x1, dy0, dy1, z1, fs, I(2.25f));
                Pull(mb, mid - g - I(1.5f), dy1 - I(3.5f), pz, false, hw, I(5f));
                Pull(mb, mid + g + I(1.5f), dy1 - I(3.5f), pz, false, hw, I(5f));
            }
            else
            {
                Shaker(mb, x0, x1, dy0, dy1, z1, fs, I(2.25f));
                Pull(mb, x1 - I(1.5f), dy1 - I(3.5f), pz, false, hw, I(5f));
            }
        }

        static void BuildWall(MeshBuilder mb, float W, int fs, int hw, int box)
        {
            float D = WallBox, H = WallH, ff = D - I(0.75f), side = I(0.5f);
            FinishGeom.Box(mb, 0f, side, 0f, H, 0f, ff, fs, Vector3.up);
            FinishGeom.Box(mb, W - side, W, 0f, H, 0f, ff, fs, Vector3.up);
            FinishGeom.Box(mb, side, W - side, 0f, I(0.75f), 0f, ff, fs);                                         // bottom (visible from below)
            FinishGeom.Box(mb, side, W - side, H - I(0.75f), H, 0f, ff, fs);                                      // top
            FinishGeom.Box(mb, side, W - side, I(0.75f), H - I(0.75f), 0f, I(0.25f), box);                      // back
            FinishGeom.Box(mb, side, W - side, H - I(4.25f), H - I(0.75f), I(0.25f), I(1f), box);               // top hang rail
            FinishGeom.Box(mb, side, W - side, I(0.75f), I(4.25f), I(0.25f), I(1f), box);                       // bottom hang rail
            float z0 = ff, z1 = D;
            FinishGeom.Box(mb, 0f, Frame, 0f, H, z0, z1, fs, Vector3.up);
            FinishGeom.Box(mb, W - Frame, W, 0f, H, z0, z1, fs, Vector3.up);
            FinishGeom.Box(mb, Frame, W - Frame, H - Frame, H, z0, z1, fs, Vector3.right);
            FinishGeom.Box(mb, Frame, W - Frame, 0f, Frame, z0, z1, fs, Vector3.right);
            FinishGeom.Box(mb, Frame, W - Frame, Frame, H - Frame, z0 - I(0.2f), z0 - I(0.1f), box);
            float x0 = I(1.0f), x1 = W - I(1.0f), y0 = I(1.0f), y1 = H - I(1.0f), pz = z1 + DoorT;
            if (W >= I(24f) - 0.001f)
            {
                float mid = W * 0.5f, g = I(0.0625f);
                Shaker(mb, x0, mid - g, y0, y1, z1, fs, I(2.25f));
                Shaker(mb, mid + g, x1, y0, y1, z1, fs, I(2.25f));
                Pull(mb, mid - g - I(1.5f), y0 + I(3.5f), pz, false, hw, I(5f));
                Pull(mb, mid + g + I(1.5f), y0 + I(3.5f), pz, false, hw, I(5f));
            }
            else
            {
                Shaker(mb, x0, x1, y0, y1, z1, fs, I(2.25f));
                Pull(mb, x1 - I(1.5f), y0 + I(3.5f), pz, false, hw, I(5f));
            }
        }

        /// <summary>Shaker door/drawer front: 3/4" frame (stiles full height, rails between) around a recessed flat panel.</summary>
        static void Shaker(MeshBuilder mb, float x0, float x1, float y0, float y1, float zb, int sub, float rail)
        {
            float zf = zb + DoorT, st = Mathf.Min(rail, (x1 - x0) * 0.3f), rl = Mathf.Min(rail, (y1 - y0) * 0.3f);
            float e = I(0.06f);
            // stiles + rails with eased front edges
            Ease(mb, x0, x0 + st, y0, y1, zb, zf, e, sub, Vector3.up);
            Ease(mb, x1 - st, x1, y0, y1, zb, zf, e, sub, Vector3.up);
            Ease(mb, x0 + st, x1 - st, y1 - rl, y1, zb, zf, e, sub, Vector3.right);
            Ease(mb, x0 + st, x1 - st, y0, y0 + rl, zb, zf, e, sub, Vector3.right);
            // recessed panel (1/4" ply in the groove, face 3/8" behind the frame face)
            FinishGeom.Box(mb, x0 + st, x1 - st, y0 + rl, y1 - rl, zb + I(0.125f), zf - I(0.375f), sub, Vector3.up);
        }

        static void Ease(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, float e, int sub, Vector3 u)
        {
            // box with a small chamfer on the four front edges (local z = out)
            var b = mb;
            float zc = z1 - e;
            b.AddQuad(new Vector3(x0 + e, y0 + e, z1), new Vector3(x1 - e, y0 + e, z1), new Vector3(x1 - e, y1 - e, z1), new Vector3(x0 + e, y1 - e, z1), sub, Vector3.forward, u);
            b.AddQuad(new Vector3(x0, y0, zc), new Vector3(x1, y0, zc), new Vector3(x1 - e, y0 + e, z1), new Vector3(x0 + e, y0 + e, z1), sub, new Vector3(0, -1, 1), u);
            b.AddQuad(new Vector3(x0, y1, zc), new Vector3(x1, y1, zc), new Vector3(x1 - e, y1 - e, z1), new Vector3(x0 + e, y1 - e, z1), sub, new Vector3(0, 1, 1), u);
            b.AddQuad(new Vector3(x0, y0, zc), new Vector3(x0, y1, zc), new Vector3(x0 + e, y1 - e, z1), new Vector3(x0 + e, y0 + e, z1), sub, new Vector3(-1, 0, 1), u);
            b.AddQuad(new Vector3(x1, y0, zc), new Vector3(x1, y1, zc), new Vector3(x1 - e, y1 - e, z1), new Vector3(x1 - e, y0 + e, z1), sub, new Vector3(1, 0, 1), u);
            b.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + zc) * 0.5f), new Vector3(x1 - x0, y1 - y0, zc - z0), Quaternion.identity, sub, u,
                MeshBuilder.FacePosX | MeshBuilder.FaceNegX | MeshBuilder.FacePosY | MeshBuilder.FaceNegY | MeshBuilder.FaceNegZ);
        }

        /// <summary>Bar pull: round bar on two posts, standing off the face by 1-1/8".</summary>
        static void Pull(MeshBuilder mb, float x, float y, float z, bool horizontal, int sub, float cc)
        {
            float r = I(0.22f), off = I(1.125f), len = cc + I(1.25f);
            Quaternion rot = horizontal ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.Euler(90f, 0f, 0f);
            mb.AddRod(new Vector3(x, y, z + off), rot, r, len, 14, sub);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 p = horizontal ? new Vector3(x + s * cc * 0.5f, y, z + off * 0.5f) : new Vector3(x, y + s * cc * 0.5f, z + off * 0.5f);
                mb.AddRod(p, Quaternion.identity, I(0.18f), off, 10, sub);
            }
        }
    }
}
