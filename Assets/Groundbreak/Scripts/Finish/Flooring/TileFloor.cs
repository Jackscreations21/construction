using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A tile floor job in progress: layout → cement backer → thinset (open time) → set with spacers (tile-saw cuts)
    /// → 24 h cure → grout (color) → wipe haze. The FloorInstall on the same object carries the finished facts.</summary>
    public class TileFloor : MonoBehaviour
    {
        public enum Stage { Layout, Backer, Thinset, Set, Grouted, Clean }
        public static readonly List<TileFloor> All = new List<TileFloor>();

        public Stage Step = Stage.Layout;
        public FloorInstall Install;
        public string TileId;
        public float TileL, TileW, Joint, BondShift;
        public float ThinsetAt = -999f, SetAt = -999f, GroutAt = -999f;
        public float MaxLippage;           // largest height difference between neighboring tiles (m)
        public Color GroutColor;
        public string GroutName = "";
        public int Tiles, Spacers, Cuts;
        public GameObject BackerGo, ThinsetGo, TilesGo, SpacersGo, GroutGo;
        public readonly List<Rect> TileRects = new List<Rect>();
        public readonly List<float> TileLift = new List<float>();

        public float BackerTop => Flooring.SubfloorTop(Install.Floor) + (BackerGo != null ? TileWork.BackerT : 0f);
        public float ThinsetTop => BackerTop + TileWork.ThinsetT;
        public float TileTop => ThinsetTop + TileWork.TileT;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
    }

    public static class TileWork
    {
        public static readonly float BackerT = Units.In(0.25f), ThinsetT = Units.In(0.1875f), TileT = Units.In(0.375f), GroutRecess = 0.0015f;
        public const float OpenTimeHours = 1f, CureHours = 24f, HazeHours = 0.25f;
        public static readonly Dictionary<string, Color> GroutColors = new Dictionary<string, Color>
        {
            { "Bright white", new Color(0.93f, 0.93f, 0.91f) }, { "Delorean gray", new Color(0.62f, 0.62f, 0.6f) },
            { "Charcoal", new Color(0.28f, 0.28f, 0.28f) }, { "Sand", new Color(0.78f, 0.72f, 0.6f) },
        };

        static Material _haze;

        /// <summary>Lays out a tile floor (12x24 in 1/3 running bond, or 12x12 straight) centered in the area, 1/8" joints,
        /// 1/4" gap at walls. Holes default to <see cref="Flooring.AutoHoles"/>.</summary>
        public static TileFloor Layout(FramePlane floor, Rect area, IList<Rect> holes = null, string tileId = "fin-tile-1224", float gap = -1f)
        {
            if (floor == null || !floor.IsFloor) { Game.Toast("Aim at the subfloor"); return null; }
            if (gap < 0f) gap = Units.In(0.25f);
            if (holes == null) holes = Flooring.AutoHoles(floor, area, gap);
            var go = new GameObject("TileFloor_" + tileId);
            go.transform.SetPositionAndRotation(floor.Origin, Flooring.Frame(floor));
            var fi = go.AddComponent<FloorInstall>();
            fi.Type = FloorType.Tile; fi.Floor = floor; fi.Gap = gap;
            fi.Area = Rect.MinMaxRect(area.xMin + gap, area.yMin + gap, area.xMax - gap, area.yMax - gap);
            fi.Holes.AddRange(holes);
            fi.MinStagger = float.MaxValue;
            fi.TopY = Flooring.SubfloorTop(floor);
            var t = go.AddComponent<TileFloor>();
            t.Install = fi; t.TileId = tileId;
            bool big = tileId.Contains("1224");
            t.TileL = Units.In(big ? 24f : 12f); t.TileW = Units.In(12f); t.Joint = Units.In(0.125f);
            t.BondShift = big ? 1f / 3f : 0f;
            fi.RowsAlongU = fi.Area.width >= fi.Area.height;
            fi.enabled = false; // not a walking surface until set
            return t;
        }

        /// <summary>1/4" cement backer board, screwed 8" o.c. (drill). Returns false if missing tools/stock.</summary>
        public static bool Backer(TileFloor t)
        {
            if (t == null || t.Step != TileFloor.Stage.Layout) return false;
            if (!FinishStock.Require("fin-drill", "screw down the backer board")) return false;
            float sqft = t.Install.SqFt;
            if (!InteriorSupplies.UseAmount("fin-cement-board", sqft, 15f)) return false;
            InteriorSupplies.UseFasteners("fin-backer-screws", Mathf.CeilToInt(sqft * 2.2f));
            var mb = new MeshBuilder();
            foreach (var c in PanelMesh.Cells(t.Install.Area, t.Install.Holes)) FinishGeom.Box(mb, c.xMin, c.xMax, 0f, BackerT, c.yMin, c.yMax, 0);
            t.BackerGo = Child(t, "Backer", mb.Build("Backer"), FinishMaterials.Get(InteriorMaterials.CementBoard));
            t.Step = TileFloor.Stage.Backer;
            Game.Toast("Cement backer board down and screwed — trowel thinset next");
            return true;
        }

        /// <summary>Spreads modified thinset with the 1/4" x 3/8" square-notch trowel. Starts the open-time clock.</summary>
        public static bool Trowel(TileFloor t)
        {
            if (t == null) return false;
            if (t.Step == TileFloor.Stage.Layout) { Game.Toast("Put down 1/4\" cement backer board first"); return false; }
            if (t.Step != TileFloor.Stage.Backer && t.Step != TileFloor.Stage.Thinset) return false;
            if (!FinishStock.Require("notched-trowel", "spread thinset")) return false;
            if (!InteriorSupplies.UseAmount("fin-thinset", t.Install.SqFt, 45f)) return false;
            if (t.ThinsetGo != null) Object.Destroy(t.ThinsetGo);
            var mb = new MeshBuilder();
            float y0 = t.BackerTop - Flooring.SubfloorTop(t.Install.Floor);
            foreach (var c in PanelMesh.Cells(t.Install.Area, t.Install.Holes)) FinishGeom.Box(mb, c.xMin, c.xMax, y0, y0 + ThinsetT, c.yMin, c.yMax, 0, t.Install.RowsAlongU ? Vector3.right : Vector3.forward);
            t.ThinsetGo = Child(t, "Thinset", mb.Build("Thinset"), FinishMaterials.Get(InteriorMaterials.Thinset));
            t.ThinsetAt = FinishTime.Now; t.Step = TileFloor.Stage.Thinset;
            Game.Toast($"Thinset troweled — set the tile within {OpenTimeHours:F0} h (open time)");
            return true;
        }

        /// <summary>Sets the tiles into the thinset with 1/8" spacers, cutting the edge tiles on the wet tile saw.
        /// maxLippage = largest random height difference between tiles (the inspector allows 1/32").</summary>
        public static bool Set(TileFloor t, float maxLippage = 0.0004f, int seed = 3)
        {
            if (t == null) return false;
            if (t.Step != TileFloor.Stage.Thinset) { Game.Toast(t.Step < TileFloor.Stage.Thinset ? "Trowel thinset first" : "Tile is already set"); return false; }
            if (!FinishStock.Require("tile-saw", "cut the edge tiles")) return false;
            float open = FinishTime.Now - t.ThinsetAt;
            if (open > OpenTimeHours) { Game.Toast($"The thinset skinned over ({open:F1} h) — scrape it and trowel fresh"); t.Step = TileFloor.Stage.Backer; return false; }
            var fi = t.Install; var rnd = new System.Random(seed);
            Rect area = fi.Area;
            bool alongU = fi.RowsAlongU;
            var holes = Flooring.ToRowSpace(fi.Holes, alongU);
            float a0 = alongU ? area.xMin : area.yMin, a1 = alongU ? area.xMax : area.yMax;
            float b0 = alongU ? area.yMin : area.xMin, b1 = alongU ? area.yMax : area.xMax;
            float mA = t.TileL + t.Joint, mB = t.TileW + t.Joint;
            // center the layout so both edges get equal cuts (no slivers)
            float startA = a0 + (Mathf.Repeat(a1 - a0, mA) - mA) * 0.5f, startB = b0 + (Mathf.Repeat(b1 - b0, mB) - mB) * 0.5f;
            if ((startA + mA) - a0 < t.TileL * 0.25f) startA += mA * 0.5f;
            if ((startB + mB) - b0 < t.TileW * 0.25f) startB += mB * 0.5f;
            var tb = new MeshBuilder(); tb.MinSubmeshCount = 3;
            var sb = new MeshBuilder();
            float baseY = t.ThinsetTop - Flooring.SubfloorTop(fi.Floor);
            t.TileRects.Clear(); t.TileLift.Clear();
            int tiles = 0, cuts = 0, spacers = 0;
            int row = 0;
            for (float rb = startB; rb < b1; rb += mB, row++)
            {
                float tb0 = rb, tb1 = rb + t.TileW;
                float cb0 = Mathf.Max(tb0, b0), cb1 = Mathf.Min(tb1, b1);
                if (cb1 - cb0 < Units.In(0.75f)) continue;
                float shift = t.BondShift > 0f ? Mathf.Repeat(row * t.BondShift, 1f) * mA : 0f;
                for (float ra = startA - mA + shift; ra < a1; ra += mA)
                {
                    float ta0 = ra, ta1 = ra + t.TileL;
                    float ca0 = Mathf.Max(ta0, a0), ca1 = Mathf.Min(ta1, a1);
                    if (ca1 - ca0 < Units.In(0.75f)) continue;
                    bool cut = ca0 > ta0 + 1e-4f || ca1 < ta1 - 1e-4f || cb0 > tb0 + 1e-4f || cb1 < tb1 - 1e-4f;
                    var cells = PanelMesh.Cells(Rect.MinMaxRect(ca0, cb0, ca1, cb1), holes);
                    if (cells.Count == 0) continue;
                    if (cells.Count != 1 || cells[0].width < ca1 - ca0 - 1e-4f || cells[0].height < cb1 - cb0 - 1e-4f) cut = true;
                    tiles++; if (cut) cuts++;
                    float lift = (float)rnd.NextDouble() * maxLippage;
                    tb.UvOffset = new Vector2((float)rnd.NextDouble() * 5f, (float)rnd.NextDouble() * 5f);
                    int sub = rnd.Next(3);
                    foreach (var p in cells)
                    {
                        if (p.width < Units.In(0.5f) || p.height < Units.In(0.5f)) continue;
                        Rect r = alongU ? p : Rect.MinMaxRect(p.yMin, p.xMin, p.yMax, p.xMax);
                        FinishGeom.EasedTile(tb, r.xMin, r.yMin, r.xMax, r.yMax, baseY + lift, baseY + TileT + lift, 0.0012f, sub, !alongU);
                        t.TileRects.Add(r); t.TileLift.Add(lift);
                    }
                    // spacer at the tile's lower corner (in the joint)
                    if (ta0 >= a0 + Units.In(1f) && tb0 >= b0 + Units.In(1f))
                    {
                        float ja = ta0 - t.Joint * 0.5f, jb = tb0 - t.Joint * 0.5f, arm = Units.In(0.4f), w = t.Joint * 0.9f;
                        float y0 = baseY + 0.001f, y1 = baseY + TileT - 0.0015f;
                        if (alongU) { FinishGeom.Box(sb, ja - w / 2, ja + w / 2, y0, y1, jb - arm, jb + arm, 0); FinishGeom.Box(sb, ja - arm, ja + arm, y0, y1, jb - w / 2, jb + w / 2, 0); }
                        else { FinishGeom.Box(sb, jb - w / 2, jb + w / 2, y0, y1, ja - arm, ja + arm, 0); FinishGeom.Box(sb, jb - arm, jb + arm, y0, y1, ja - w / 2, ja + w / 2, 0); }
                        spacers++;
                    }
                }
            }
            if (!InteriorSupplies.UseFasteners(t.TileId, tiles)) return false;
            InteriorSupplies.UseFasteners("fin-spacers", spacers);
            var mats = new[] { FinishMaterials.Get(InteriorMaterials.Porcelain), FinishMaterials.Get(InteriorMaterials.Porcelain, new Color(0.97f, 0.96f, 0.95f)), FinishMaterials.Get(InteriorMaterials.Porcelain, new Color(1.02f, 1.01f, 1.0f)) };
            t.TilesGo = Child(t, "Tiles", tb.Build("Tiles"), null, mats, "fin-floor-tile");
            t.SpacersGo = Child(t, "Spacers", sb.Build("Spacers"), FinishMaterials.Get(InteriorMaterials.Spacer));
            t.MaxLippage = maxLippage; t.Tiles = tiles; t.Cuts = cuts; t.Spacers = spacers;
            fi.Pieces = tiles; fi.Cuts = cuts; fi.TopY = t.TileTop; fi.enabled = true;
            t.SetAt = FinishTime.Now; t.Step = TileFloor.Stage.Set;
            Game.Toast($"Set {tiles} tiles ({cuts} cut on the wet saw) with {spacers} spacers — let the thinset cure {CureHours:F0} h before grouting");
            return true;
        }

        /// <summary>Pulls the spacers and floats sanded grout into the joints (rubber grout float). Needs the 24 h cure.</summary>
        public static bool Grout(TileFloor t, string colorName = "Delorean gray")
        {
            if (t == null) return false;
            if (t.Step != TileFloor.Stage.Set) { Game.Toast(t.Step < TileFloor.Stage.Set ? "Set the tile first" : "Already grouted"); return false; }
            if (!FinishStock.Require("grout-float", "pack the grout joints")) return false;
            float left = CureHours - (FinishTime.Now - t.SetAt);
            if (left > 0.01f) { Game.Toast($"Thinset is still curing: {left:F0} h left before grout"); return false; }
            if (!InteriorSupplies.UseAmount("fin-grout", t.Install.SqFt, 100f)) return false;
            if (!GroutColors.TryGetValue(colorName, out var col)) { colorName = "Delorean gray"; col = GroutColors[colorName]; }
            if (t.SpacersGo != null) { Object.Destroy(t.SpacersGo); t.SpacersGo = null; }
            var mb = new MeshBuilder();
            float y0 = t.ThinsetTop - Flooring.SubfloorTop(t.Install.Floor);
            foreach (var c in PanelMesh.Cells(t.Install.Area, t.Install.Holes)) FinishGeom.Box(mb, c.xMin, c.xMax, y0, y0 + TileT - GroutRecess, c.yMin, c.yMax, 0);
            t.GroutGo = Child(t, "Grout", mb.Build("Grout"), FinishMaterials.Get(InteriorMaterials.Grout, col));
            if (_haze == null)
            {
                _haze = new Material(FinishMaterials.Get(InteriorMaterials.Porcelain)) { name = "TileHaze", hideFlags = HideFlags.DontUnloadUnusedAsset };
                _haze.color = new Color(1.04f, 1.03f, 1.02f) * 0.97f; _haze.SetFloat("_GlossMapScale", 0.25f); _haze.SetFloat("_Glossiness", 0.12f);
            }
            var mr = t.TilesGo.GetComponent<MeshRenderer>(); var ms = mr.sharedMaterials;
            for (int i = 0; i < ms.Length; i++) ms[i] = _haze;
            mr.sharedMaterials = ms;
            t.GroutColor = col; t.GroutName = colorName; t.GroutAt = FinishTime.Now; t.Step = TileFloor.Stage.Grouted;
            Game.Toast($"Grouted ({colorName}) — wipe the haze with a damp sponge once it firms up (~15 min)");
            return true;
        }

        /// <summary>Wipes the grout haze off the tile faces (sponge/float). Needs the grout to firm up first.</summary>
        public static bool WipeHaze(TileFloor t)
        {
            if (t == null || t.Step != TileFloor.Stage.Grouted) return false;
            float left = HazeHours - (FinishTime.Now - t.GroutAt);
            if (left > 0.001f) { Game.Toast($"Let the grout firm up {left * 60f:F0} more min before wiping"); return false; }
            var mats = new[] { FinishMaterials.Get(InteriorMaterials.Porcelain), FinishMaterials.Get(InteriorMaterials.Porcelain, new Color(0.97f, 0.96f, 0.95f)), FinishMaterials.Get(InteriorMaterials.Porcelain, new Color(1.02f, 1.01f, 1.0f)) };
            t.TilesGo.GetComponent<MeshRenderer>().sharedMaterials = mats;
            t.Step = TileFloor.Stage.Clean;
            Game.Toast("Haze wiped — tile floor done");
            return true;
        }

        /// <summary>Whole sequence for scripted use: layout, backer, trowel, set, (skip 24 h), grout, (skip 15 min), wipe.</summary>
        public static TileFloor InstallAll(FramePlane floor, Rect area, IList<Rect> holes = null, string tileId = "fin-tile-1224", string grout = "Delorean gray")
        {
            var t = Layout(floor, area, holes, tileId);
            if (t == null || !Backer(t) || !Trowel(t) || !Set(t)) return t;
            FinishTime.Skip(CureHours);
            if (!Grout(t, grout)) return t;
            FinishTime.Skip(HazeHours + 0.01f);
            WipeHaze(t);
            return t;
        }

        static GameObject Child(TileFloor t, string name, Mesh mesh, Material mat, Material[] mats = null, string kind = "fin-floor-tile-layer")
        {
            var go = new GameObject(name);
            go.transform.SetParent(t.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats ?? new[] { mat };
            if (mesh.vertexCount > 0) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<FinishLayer>().Kind = kind;
            return go;
        }
    }
}
