using System.Collections.Generic;
using UnityEngine;
using static Groundbreak.ExtGeo;

namespace Groundbreak
{
    /// <summary>Marker on every deck Piece (excluded from wall/ceiling face discovery).</summary>
    public class DeckPart : MonoBehaviour { public Deck Deck; }

    /// <summary>A deck attached to an exterior wall. Built stage by stage with <see cref="DeckBuilder"/>.</summary>
    public class Deck : MonoBehaviour
    {
        public ExteriorWall Wall;
        public float U0, Width, Depth, SurfaceY;
        public int Stage;                                   // stages completed (see DeckBuilder.Stages)
        public float LedgerTop, JoistSpacing = Units.In(16f), BeamOut, FootingBottomY;
        public bool ZFlash, WrapLapped;
        public readonly List<Vector2> Lags = new List<Vector2>();   // (u, y) on the wall face
        public int LagsInFraming;
        public readonly List<Vector3> Footings = new List<Vector3>(); // world pier tops
        public readonly List<float> Joists = new List<float>();     // joist center u
        public int Hangers;
        public float GuardHeight, BalusterGap;
        public readonly List<Piece> Pieces = new List<Piece>();
        public bool Done => Stage >= DeckBuilder.Stages.Length;
        public Vector3 P(float u, float outward, float y) => Wall.W(u, y, outward);
    }

    /// <summary>
    /// Deck/porch against a wall, one stage per call (or per click of the Deck Builder's Kit):
    /// 1 ledger (PT 2x10, Z-flashing lapped under the house wrap, 1/2" lags staggered 16" O.C. into the rim/framing),
    /// 2 footings (10" piers to frost depth, 6x6 post bases), 3 PT 6x6 posts, 4 doubled PT 2x10 beam,
    /// 5 PT 2x8 joists 16" O.C. in hangers, 6 rim joist, 7 5/4x6 decking with 1/8" gaps and staggered butt joints on joists,
    /// 8 guards: 4x4 posts, 2x4 rails, 2x6 cap at 37-1/2", 2x2 balusters with 3-3/4" clear (4" sphere rule).
    /// Scripted: <c>var d = DeckBuilder.Plan(wall, u0, widthIn, depthIn, surfaceY); DeckBuilder.BuildAll(d);</c>
    /// </summary>
    public static class DeckBuilder
    {
        public static readonly string[] Stages = { "Ledger + Z-flashing", "Footings", "Posts", "Beam", "Joists + hangers", "Rim joist", "Decking", "Guards" };
        public static readonly List<Deck> All = new List<Deck>();
        public static readonly float DeckT = In(1f), BoardW = In(5.5f), BoardGap = In(0.125f), JoistH = In(7.25f), LedgerH = In(9.25f), T2 = In(1.5f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        static bool _hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            if (_hooked) return; _hooked = true;
            var prev = FramePlane.ExcludeMember;
            FramePlane.ExcludeMember = p => (prev != null && prev(p)) || (p != null && p.GetComponent<DeckPart>() != null);
        }

        /// <summary>Lays out a deck: u0 = left end along the wall, surfaceY = world height of the deck surface
        /// (about 1" below the door threshold).</summary>
        public static Deck Plan(ExteriorWall w, float u0, float widthIn, float depthIn, float surfaceY)
        {
            if (w == null) return null;
            var go = new GameObject("Deck");
            var d = go.AddComponent<Deck>();
            d.Wall = w; d.U0 = u0; d.Width = In(widthIn); d.Depth = In(depthIn); d.SurfaceY = surfaceY;
            d.LedgerTop = surfaceY - DeckT;
            d.BeamOut = d.Depth - In(16f);
            All.Add(d);
            Game.Toast($"Deck laid out: {Units.Format(d.Width)} x {Units.Format(d.Depth)} — next: {Stages[0]}");
            return d;
        }

        public static Deck Near(ExteriorWall w, Vector2 uv)
        {
            All.RemoveAll(x => x == null);
            foreach (var d in All) if (d.Wall == w && uv.x > d.U0 - 1f && uv.x < d.U0 + d.Width + 1f) return d;
            return null;
        }

        public static bool BuildAll(Deck d)
        {
            int guard = 0;
            while (d != null && !d.Done && guard++ < 12) if (!NextStage(d)) return false;
            return d != null && d.Done;
        }

        /// <summary>Builds the next stage. Returns false (with a toast) if stock is missing.</summary>
        public static bool NextStage(Deck d)
        {
            if (d.Done) { Game.Toast("The deck is finished"); return false; }
            bool ok;
            switch (d.Stage)
            {
                case 0: ok = Ledger(d); break;
                case 1: ok = Footings(d); break;
                case 2: ok = Posts(d); break;
                case 3: ok = Beam(d); break;
                case 4: ok = Joists(d); break;
                case 5: ok = Rim(d); break;
                case 6: ok = Decking(d); break;
                default: ok = Guards(d); break;
            }
            if (!ok) return false;
            d.Stage++;
            Game.Toast(d.Done ? "Deck complete" : $"Deck: {Stages[d.Stage - 1]} done — next: {Stages[d.Stage]}");
            return true;
        }

        // ------------------------------------------------------------------ helpers

        static Quaternion Rot(Vector3 len, Vector3 width) => Quaternion.LookRotation(len, Vector3.Cross(len, width));

        static Piece Add(Deck d, string id, float length, Vector3 center, Quaternion rot)
        {
            var def = Catalog.Get(id);
            var p = Piece.Create(def, Mathf.Min(length, def.Size.z), center, rot);
            p.Fasten();
            p.gameObject.AddComponent<DeckPart>().Deck = d;
            p.transform.SetParent(d.transform, true);
            d.Pieces.Add(p);
            return p;
        }

        /// <summary>Horizontal board along the wall (U) from u0 to u1, with its wide face vertical (on edge) or flat.</summary>
        static void AlongU(Deck d, string id, float u0, float u1, float outC, float yC, bool onEdge)
        {
            var w = d.Wall; float len = u1 - u0;
            var c = d.P((u0 + u1) * 0.5f, outC, yC);
            Add(d, id, len, c, Rot(w.Face.U, onEdge ? Vector3.up : w.Face.N));
        }

        static Material Galv => Materials.Get(SurfaceKind.Galvanized);

        static GameObject Hardware(Deck d, string name, MeshBuilder b, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(d.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Build(b, name, mats.Length);
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            var fl = go.AddComponent<FinishLayer>(); fl.Kind = "deck-hardware"; fl.Owner = d;
            return go;
        }

        static void WBox(MeshBuilder b, Vector3 center, Vector3 size, Quaternion rot, int sub) => b.AddBox(center, size, rot, sub, Vector3.forward);

        // ------------------------------------------------------------------ stages

        static bool Ledger(Deck d)
        {
            var w = d.Wall;
            int pieces = Mathf.CeilToInt(d.Width / In(144f));
            int lags = Mathf.FloorToInt(d.Width / In(16f)) + 1;
            if (!FinishStock.Take(ExteriorCatalog.PT2x10, pieces)) return false;
            if (!FinishStock.Take(ExteriorCatalog.LedgerScrews, lags)) return false;
            if (!FinishStock.Take(ExteriorCatalog.ZFlash, Mathf.CeilToInt(d.Width / In(120f)))) return false;
            float yc = d.LedgerTop - LedgerH * 0.5f, outC = T2 * 0.5f;
            for (float u = d.U0; u < d.U0 + d.Width - 1e-3f; u += In(144f))
                AlongU(d, ExteriorCatalog.PT2x10, u, Mathf.Min(d.U0 + d.Width, u + In(144f)), outC, yc, true);
            // lags: two rows, staggered, 16" O.C.
            d.Lags.Clear(); d.LagsInFraming = 0;
            var fp = w.Studs ?? w.Face;
            int k = 0;
            for (float u = d.U0 + In(4f); u < d.U0 + d.Width - In(2f); u += In(16f), k++)
            {
                float y = (k % 2 == 0) ? d.LedgerTop - In(2f) : d.LedgerTop - LedgerH + In(2f);
                d.Lags.Add(new Vector2(u, y));
                if (fp.MemberAt(new Vector2(u, y), In(0.1f)) != null) d.LagsInFraming++;
            }
            d.ZFlash = true;
            d.WrapLapped = HouseWrap.Covered(w);
            var b = new MeshBuilder();
            var rot = w.Face.Rotation;
            foreach (var l in d.Lags)
            {
                var c = d.P(l.x, T2 + In(0.15f), l.y);
                b.AddCylinder(c, Quaternion.FromToRotation(Vector3.up, w.Face.N), In(0.55f), In(0.1f), 12, 0);      // washer
                b.AddBox(c + w.Face.N * In(0.2f), new Vector3(In(0.75f), In(0.75f), In(0.3f)), rot, 0);           // hex head
            }
            // Z-flashing: up the wall (under the wrap), over the ledger top, drip down the face
            WBox(b, d.P(d.U0 + d.Width * 0.5f, -0.0004f, d.LedgerTop + In(1.5f)), new Vector3(d.Width, In(3f), 0.0004f), rot, 0);
            WBox(b, d.P(d.U0 + d.Width * 0.5f, T2 * 0.5f + In(0.1f), d.LedgerTop + In(0.02f)), new Vector3(d.Width, In(0.04f), T2 + In(0.2f)), rot, 0);
            WBox(b, d.P(d.U0 + d.Width * 0.5f, T2 + In(0.22f), d.LedgerTop - In(0.25f)), new Vector3(d.Width, In(0.55f), In(0.04f)), rot, 0);
            Hardware(d, "LedgerHardware", b, new[] { Galv });
            return true;
        }

        static List<float> PostU(Deck d) => new List<float> { d.U0 + In(12f), d.U0 + d.Width * 0.5f, d.U0 + d.Width - In(12f) };

        static bool Footings(Deck d)
        {
            var us = PostU(d);
            if (!FinishStock.Take(ExteriorCatalog.Sonotube, us.Count)) return false;
            if (!FinishStock.Take(ExteriorCatalog.Concrete, us.Count * 3)) return false;
            if (!FinishStock.Take(ExteriorCatalog.PostBase, us.Count)) return false;
            d.FootingBottomY = ExteriorSite.GradeY - ExteriorSite.FrostDepth - In(6f);
            var b = new MeshBuilder();
            float top = ExteriorSite.GradeY + In(2f);
            d.Footings.Clear();
            foreach (var u in us)
            {
                var c = d.P(u, d.BeamOut, top);
                d.Footings.Add(c);
                float h = top - d.FootingBottomY;
                b.AddCylinder(c + Vector3.down * (h * 0.5f), In(5f), h, 20, 0);
                // post base: plate + two side straps
                WBox(b, c + Vector3.up * In(0.5f), new Vector3(In(5.5f), In(1f), In(5.5f)), d.Wall.Face.Rotation, 1);
                WBox(b, c + Vector3.up * In(3f) + d.Wall.Face.U * In(2.83f), new Vector3(In(0.12f), In(5f), In(3f)), d.Wall.Face.Rotation, 1);
                WBox(b, c + Vector3.up * In(3f) - d.Wall.Face.U * In(2.83f), new Vector3(In(0.12f), In(5f), In(3f)), d.Wall.Face.Rotation, 1);
            }
            Hardware(d, "Footings", b, new[] { Materials.Get(SurfaceKind.Concrete), Galv });
            return true;
        }

        static float BeamTop(Deck d) => d.LedgerTop - JoistH;

        static bool Posts(Deck d)
        {
            var us = PostU(d);
            if (!FinishStock.Take(ExteriorCatalog.PT6x6, us.Count)) return false;
            float y0 = ExteriorSite.GradeY + In(3f), y1 = BeamTop(d) - LedgerH;
            if (y1 - y0 < In(4f)) { Game.Toast("Deck is too low for posts — use a ground-level deck"); return false; }
            foreach (var u in us) Add(d, ExteriorCatalog.PT6x6, y1 - y0, d.P(u, d.BeamOut, (y0 + y1) * 0.5f), Rot(Vector3.up, d.Wall.Face.U));
            return true;
        }

        static bool Beam(Deck d)
        {
            int n = 2 * Mathf.CeilToInt(d.Width / In(144f));
            if (!FinishStock.Take(ExteriorCatalog.PT2x10, n)) return false;
            float yc = BeamTop(d) - LedgerH * 0.5f;
            for (int ply = 0; ply < 2; ply++)
            {
                float o = d.BeamOut + (ply == 0 ? -T2 * 0.5f : T2 * 0.5f);
                // splice the two plies at different points
                float split = ply == 0 ? In(144f) : d.Width - In(144f);
                if (d.Width <= In(144f)) AlongU(d, ExteriorCatalog.PT2x10, d.U0, d.U0 + d.Width, o, yc, true);
                else { AlongU(d, ExteriorCatalog.PT2x10, d.U0, d.U0 + split, o, yc, true); AlongU(d, ExteriorCatalog.PT2x10, d.U0 + split, d.U0 + d.Width, o, yc, true); }
            }
            return true;
        }

        static bool Joists(Deck d)
        {
            d.Joists.Clear();
            d.Joists.Add(d.U0 + T2 * 0.5f);
            for (float u = d.U0 + d.JoistSpacing; u < d.U0 + d.Width - T2; u += d.JoistSpacing) d.Joists.Add(u);
            d.Joists.Add(d.U0 + d.Width - T2 * 0.5f);
            if (!FinishStock.Take(ExteriorCatalog.PT2x8, d.Joists.Count)) return false;
            if (!FinishStock.Take(ExteriorCatalog.JoistHanger, d.Joists.Count)) return false;
            float len = d.Depth - T2 - T2;  // from the ledger face to the rim
            var b = new MeshBuilder();
            var rot = d.Wall.Face.Rotation;
            foreach (var u in d.Joists)
            {
                Add(d, ExteriorCatalog.PT2x8, len, d.P(u, T2 + len * 0.5f, d.LedgerTop - JoistH * 0.5f), Rot(d.Wall.Face.N, Vector3.up));
                // joist hanger: seat + two side flanges + face flanges on the ledger
                var c = d.P(u, T2 + In(1.5f), d.LedgerTop - JoistH);
                WBox(b, c + Vector3.down * In(0.03f), new Vector3(In(1.75f), In(0.06f), In(3f)), rot, 0);
                WBox(b, c + Vector3.up * In(3f) + d.Wall.Face.U * In(0.82f), new Vector3(In(0.06f), In(6f), In(3f)), rot, 0);
                WBox(b, c + Vector3.up * In(3f) - d.Wall.Face.U * In(0.82f), new Vector3(In(0.06f), In(6f), In(3f)), rot, 0);
                WBox(b, d.P(u + In(1.6f), T2 + In(0.03f), d.LedgerTop - JoistH + In(3f)), new Vector3(In(1.5f), In(6f), In(0.06f)), rot, 0);
                WBox(b, d.P(u - In(1.6f), T2 + In(0.03f), d.LedgerTop - JoistH + In(3f)), new Vector3(In(1.5f), In(6f), In(0.06f)), rot, 0);
            }
            d.Hangers = d.Joists.Count;
            Hardware(d, "JoistHangers", b, new[] { Galv });
            return true;
        }

        static bool Rim(Deck d)
        {
            int n = Mathf.CeilToInt(d.Width / In(144f));
            if (!FinishStock.Take(ExteriorCatalog.PT2x8, n)) return false;
            float o = d.Depth - T2 * 0.5f, yc = d.LedgerTop - JoistH * 0.5f;
            for (float u = d.U0; u < d.U0 + d.Width - 1e-3f; u += In(144f))
                AlongU(d, ExteriorCatalog.PT2x8, u, Mathf.Min(d.U0 + d.Width, u + In(144f)), o, yc, true);
            return true;
        }

        static bool Decking(Deck d)
        {
            int rows = Mathf.FloorToInt((d.Depth + In(1f) - In(0.25f)) / (BoardW + BoardGap));
            if (!FinishStock.Take(ExteriorCatalog.Decking, rows * 2)) return false;
            FinishStock.Take(ExteriorCatalog.DeckScrews, rows * d.Joists.Count * 2);
            float yc = d.LedgerTop + DeckT * 0.5f;
            float a = d.U0 - In(1f), z = d.U0 + d.Width + In(1f);
            for (int r = 0; r < rows; r++)
            {
                float o = In(0.25f) + r * (BoardW + BoardGap) + BoardW * 0.5f;
                // butt joints on joist centers, staggered row to row
                float j = d.Width > In(143f) ? d.U0 + ((r % 2 == 0) ? In(128f) : In(64f)) : z;
                if (j >= z) { AlongU(d, ExteriorCatalog.Decking, a, z, o, yc, false); continue; }
                AlongU(d, ExteriorCatalog.Decking, a, j - In(0.0625f), o, yc, false);
                AlongU(d, ExteriorCatalog.Decking, j + In(0.0625f), z, o, yc, false);
            }
            return true;
        }

        static bool Guards(Deck d)
        {
            var N = d.Wall.Face.N; var U = d.Wall.Face.U;
            float post = In(3.5f), capTop = d.SurfaceY + In(37.5f);
            // guard lines: left side, front, right side (posts outside the rim/end joists)
            var lines = new List<(Vector3 a, Vector3 b)>
            {
                (d.P(d.U0 - post * 0.5f, In(4f), 0f), d.P(d.U0 - post * 0.5f, d.Depth + post * 0.5f, 0f)),
                (d.P(d.U0 - post * 0.5f, d.Depth + post * 0.5f, 0f), d.P(d.U0 + d.Width + post * 0.5f, d.Depth + post * 0.5f, 0f)),
                (d.P(d.U0 + d.Width + post * 0.5f, d.Depth + post * 0.5f, 0f), d.P(d.U0 + d.Width + post * 0.5f, In(4f), 0f)),
            };
            var postPts = new List<Vector3>();
            foreach (var (a, b) in lines)
            {
                float L = (b - a).magnitude; int segs = Mathf.CeilToInt(L / In(72f));
                for (int i = 0; i <= segs; i++) { var p = Vector3.Lerp(a, b, i / (float)segs); if (!postPts.Exists(q => (q - p).sqrMagnitude < 0.01f)) postPts.Add(p); }
            }
            int balusters = 0; float spacing = In(5.25f);
            foreach (var (a, b) in lines) balusters += Mathf.FloorToInt(((b - a).magnitude - post) / spacing);
            if (!FinishStock.Take(ExteriorCatalog.PT4x4, postPts.Count)) return false;
            if (!FinishStock.Take(ExteriorCatalog.Baluster, balusters)) return false;
            if (!FinishStock.Take(ExteriorCatalog.PT2x4, 6)) return false;
            if (!FinishStock.Take(ExteriorCatalog.PT2x6, 4)) return false;
            float postBottom = d.LedgerTop - JoistH;
            foreach (var p in postPts)
            {
                float y0 = postBottom, y1 = capTop - T2;
                Add(d, ExteriorCatalog.PT4x4, y1 - y0, new Vector3(p.x, (y0 + y1) * 0.5f, p.z), Rot(Vector3.up, U));
            }
            foreach (var (a, b) in lines)
            {
                Vector3 dir = (b - a).normalized; float L = (b - a).magnitude;
                Vector3 inward = Vector3.Cross(Vector3.up, dir); // toward the deck for our winding? choose the side facing the deck center
                Vector3 center = d.P(d.U0 + d.Width * 0.5f, d.Depth * 0.5f, 0f);
                if (Vector3.Dot(center - a, inward) < 0f) inward = -inward;
                // cap (2x6 flat) and rails (2x4 on edge) between posts, balusters fastened to the rails
                SplitBoard(d, ExteriorCatalog.PT2x6, a - dir * post * 0.5f, b + dir * post * 0.5f, capTop - T2 * 0.5f, false, inward * In(1f));
                SplitBoard(d, ExteriorCatalog.PT2x4, a + dir * post * 0.5f, b - dir * post * 0.5f, capTop - T2 - In(1.75f), true, Vector3.zero);
                SplitBoard(d, ExteriorCatalog.PT2x4, a + dir * post * 0.5f, b - dir * post * 0.5f, d.SurfaceY + In(3.5f) + In(1.75f), true, Vector3.zero);
                int n = Mathf.FloorToInt((L - post) / spacing);
                float start = (L - (n - 1) * spacing) * 0.5f;
                float by0 = d.SurfaceY + In(3.5f), by1 = capTop - T2 - In(0.25f);
                for (int i = 0; i < n; i++)
                {
                    var p = a + dir * (start + i * spacing) + inward * In(1.5f);
                    Add(d, ExteriorCatalog.Baluster, by1 - by0, new Vector3(p.x, (by0 + by1) * 0.5f, p.z), Rot(Vector3.up, dir));
                }
            }
            d.GuardHeight = capTop - d.SurfaceY;
            d.BalusterGap = spacing - In(1.5f);
            return true;
        }

        static void SplitBoard(Deck d, string id, Vector3 a, Vector3 b, float y, bool onEdge, Vector3 shift)
        {
            Vector3 dir = (b - a); float L = dir.magnitude; dir /= L;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            float maxL = Catalog.Get(id).Size.z;
            int n = Mathf.CeilToInt(L / maxL);
            for (int i = 0; i < n; i++)
            {
                float s0 = L * i / n, s1 = L * (i + 1) / n;
                var c = a + dir * ((s0 + s1) * 0.5f) + shift; c.y = y;
                Add(d, id, s1 - s0, c, Rot(dir, onEdge ? Vector3.up : side));
            }
        }
    }
}
