using System.Collections.Generic;
using UnityEngine;
using static Groundbreak.ExtGeo;

namespace Groundbreak
{
    /// <summary>One eave: a fascia board (any horizontal lumber Piece on the rafter tails) with what hangs on it —
    /// vented soffit + F-channel, aluminum-wrapped fascia, 5" K-style gutter and a 2x3 downspout.</summary>
    public class Eave : MonoBehaviour
    {
        public Piece Fascia;
        public Vector3 Along, Out;      // horizontal unit vectors: run of the fascia, away from the building
        public Vector3 Start;            // world point: fascia front face, bottom edge, at the start end
        public float L, Top, Bottom, Thick;
        public float FrontOff, BackOff, WallOff;   // dot(.., Out) of fascia front face, fascia back face, wall surface
        public ExteriorWall Wall;
        public bool Soffit, Wrapped, Gutter, Downspout;
        public bool OutletAtEnd = true;
        public float SlopeInPer10 = 0.25f;          // gutter fall toward the outlet, inches per 10 ft
        public float GutterYStart, GutterYEnd;      // world y of the gutter back-top at both ends
        public readonly List<float> Hangers = new List<float>();
        public float SoffitY => Bottom;
        public Vector3 P(float along, float outOff, float y) => Start + Along * along + Out * (outOff - FrontOff) + Vector3.up * (y - Start.y);
    }

    /// <summary>
    /// Eaves: soffit, fascia wrap, gutters and downspouts built from generic framing — the fascia is any horizontal
    /// lumber Piece fastened to rafter tails; the wall below is found by a ray under the eave.
    /// Scripted: <c>var e = Eaves.For(fasciaPiece); Eaves.InstallAll(e);</c> or InstallSoffit / WrapFascia / HangGutter / AddDownspout.
    /// </summary>
    public static class Eaves
    {
        public static readonly List<Eave> All = new List<Eave>();
        public static readonly float GutterW = In(5f), GutterH = In(4.5f), PipeA = In(3f), PipeB = In(2f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        /// <summary>Soffit underside (world y) over this wall, or NaN when no soffit is built above it.</summary>
        public static float SoffitUnderside(ExteriorWall w)
        {
            All.RemoveAll(e => e == null);
            foreach (var e in All) if (e.Soffit && e.Wall == w) return e.SoffitY - In(0.05f);
            return float.NaN;
        }

        /// <summary>Eave for a fascia board (created on first use). Null if the piece isn't horizontal lumber.</summary>
        public static Eave For(Piece fascia)
        {
            if (fascia == null || fascia.Def == null || fascia.Def.Category != PieceCategory.Lumber) return null;
            All.RemoveAll(e => e == null);
            foreach (var e0 in All) if (e0.Fascia == fascia) return e0;
            var t = fascia.transform;
            Vector3 along = t.forward; if (Mathf.Abs(along.y) > 0.2f) return null;
            along = new Vector3(along.x, 0f, along.z).normalized;
            // thickness axis = the horizontal one of right/up; height = the vertical one
            Vector3 thick; float halfT, halfH;
            if (Mathf.Abs(t.right.y) < 0.2f) { thick = t.right; halfT = fascia.Size.x * 0.5f; halfH = fascia.Size.y * 0.5f; }
            else { thick = t.up; halfT = fascia.Size.y * 0.5f; halfH = fascia.Size.x * 0.5f; }
            thick = new Vector3(thick.x, 0f, thick.z).normalized;
            // outward = away from the rafter tails touching it
            var fb = fascia.GetComponent<Collider>().bounds; fb.Expand(0.04f);
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var q in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (q == fascia || q.Def == null || q.Def.Category != PieceCategory.Lumber) continue;
                var c = q.GetComponent<Collider>(); if (c == null || !c.bounds.Intersects(fb)) continue;
                sum += q.transform.position; n++;
            }
            Vector3 outDir = thick;
            if (n > 0) { if (Vector3.Dot(sum / n - t.position, thick) > 0f) outDir = -thick; }
            var go = new GameObject("Eave");
            var e = go.AddComponent<Eave>();
            e.Fascia = fascia; e.Along = along; e.Out = outDir; e.L = fascia.Size.z; e.Thick = halfT * 2f;
            e.Top = t.position.y + halfH; e.Bottom = t.position.y - halfH;
            Vector3 front = t.position + outDir * halfT;
            e.FrontOff = Vector3.Dot(front, outDir); e.BackOff = e.FrontOff - e.Thick;
            e.Start = new Vector3(front.x, e.Bottom, front.z) - along * (e.L * 0.5f);
            FindWall(e);
            All.Add(e);
            return e;
        }

        static void FindWall(Eave e)
        {
            e.WallOff = e.BackOff - In(16f);
            var o = e.P(e.L * 0.5f, e.BackOff - 0.01f, e.Bottom - In(1.5f));
            foreach (var h in FinishAim.SortedHits(new Ray(o, -e.Out), 3f))
            {
                var p = h.collider.GetComponentInParent<Piece>();
                if (p == e.Fascia) continue;
                if (Mathf.Abs(h.normal.y) > 0.5f) continue;
                e.WallOff = Vector3.Dot(h.point, e.Out);
                var w = ExteriorWall.FromHit(h);
                if (w != null) e.Wall = w;
                break;
            }
        }

        /// <summary>Fascia board for an exterior wall: horizontal lumber parallel to the wall, out in front of it near its top.</summary>
        public static Piece FindFascia(ExteriorWall w)
        {
            Piece best = null; float bd = float.MaxValue;
            foreach (var q in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (q.Def == null || q.Def.Category != PieceCategory.Lumber) continue;
                var f = q.transform.forward;
                if (Mathf.Abs(f.y) > 0.1f || Mathf.Abs(Vector3.Dot(f, w.Face.U)) < 0.98f) continue;
                float outD = Vector3.Dot(q.transform.position, w.Face.N) - w.Face.FaceOffset;
                if (outD < 0.1f || outD > 1.5f) continue;
                float dy = q.transform.position.y - w.B.yMax;
                if (dy < -0.5f || dy > 0.6f) continue;
                if (outD < bd) { bd = outD; best = q; }
            }
            return best;
        }

        // ================================================================== scripted API

        /// <summary>Soffit, fascia wrap, gutter (outlet at the far end) and downspout.</summary>
        public static bool InstallAll(Eave e)
        {
            if (e == null) return false;
            if (!e.Soffit && !InstallSoffit(e)) return false;
            if (!e.Wrapped && !WrapFascia(e)) return false;
            if (!e.Gutter && !HangGutter(e)) return false;
            if (!e.Downspout && !AddDownspout(e)) return false;
            return true;
        }

        /// <summary>F-channel on the wall at the fascia-bottom line, vented soffit panels from the wall to the fascia.</summary>
        public static bool InstallSoffit(Eave e)
        {
            if (e.Soffit) { Game.Toast("Soffit is already installed"); return false; }
            float depth = e.FrontOff - e.WallOff;
            if (depth < In(4f)) { Game.Toast("No overhang here for a soffit"); return false; }
            int panels = Mathf.CeilToInt(e.L / In(12f));
            int perStick = Mathf.Max(1, Mathf.FloorToInt(In(144f) / Mathf.Max(In(6f), depth)));
            if (!FinishStock.Take(ExteriorCatalog.Soffit, Mathf.CeilToInt(panels / (float)perStick))) return false;
            if (!FinishStock.Take(ExteriorCatalog.FChannel, Mathf.CeilToInt(e.L / In(150f)))) return false;
            e.Soffit = true; Rebuild(e);
            Game.Toast($"Vented soffit: {panels} panels across a {Units.Format(depth)} overhang, F-channel on the wall");
            return true;
        }

        /// <summary>Coil brake: bends white aluminum coil to wrap the fascia face, lapping the soffit edge.</summary>
        public static bool WrapFascia(Eave e)
        {
            if (e.Wrapped) { Game.Toast("Fascia is already wrapped"); return false; }
            if (!e.Soffit) { Game.Toast("Install the soffit first — the fascia wrap laps over its edge"); return false; }
            float girth = (e.Top - e.Bottom) + In(1.5f);
            float feet = e.L / Units.Foot * (girth / In(24f));
            if (!FinishStock.Take(ExteriorCatalog.Coil, Mathf.Max(1, Mathf.CeilToInt(feet / 50f)))) return false;
            e.Wrapped = true; Rebuild(e);
            Game.Toast("Fascia wrapped: coil bent on the brake, lapped over the soffit, nailed under the drip edge");
            return true;
        }

        /// <summary>5" K-style gutter: hidden hangers every 24", end caps, outlet at the low end, 1/4" fall per 10'.</summary>
        public static bool HangGutter(Eave e, bool outletAtEnd = true, float slopeInPer10 = 0.25f)
        {
            if (e.Gutter) { Game.Toast("Gutter is already hung"); return false; }
            if (!e.Wrapped) { Game.Toast("Wrap the fascia before hanging the gutter"); return false; }
            int sections = Mathf.CeilToInt(e.L / (10f * Units.Foot));
            int hangers = Mathf.FloorToInt(e.L / In(24f)) + 1;
            if (!FinishStock.Take(ExteriorCatalog.Gutter, sections)) return false;
            if (!FinishStock.Take(ExteriorCatalog.Hangers, hangers)) return false;
            if (!FinishStock.Take(ExteriorCatalog.EndCaps)) return false;
            if (!FinishStock.Take(ExteriorCatalog.Outlet)) return false;
            e.OutletAtEnd = outletAtEnd; e.SlopeInPer10 = slopeInPer10;
            float drop = In(slopeInPer10) * (e.L / (10f * Units.Foot));
            float high = e.Top - In(1.25f);
            e.GutterYStart = outletAtEnd ? high : high - drop;
            e.GutterYEnd = outletAtEnd ? high - drop : high;
            e.Hangers.Clear();
            for (float a = In(6f); a < e.L - In(3f); a += In(24f)) e.Hangers.Add(a);
            e.Gutter = true; Rebuild(e);
            Game.Toast($"Gutter hung: {Units.Format(drop)} fall toward the outlet ({slopeInPer10:0.##}\" per 10'), {e.Hangers.Count} hidden hangers");
            return true;
        }

        /// <summary>2x3 downspout from the outlet: elbows back to the wall, strapped down the wall, kick-out to a splash block.</summary>
        public static bool AddDownspout(Eave e)
        {
            if (e.Downspout) { Game.Toast("Downspout is already on"); return false; }
            if (!e.Gutter) { Game.Toast("Hang the gutter first"); return false; }
            float run = (e.GutterYEnd - ExteriorSite.GradeY);
            if (!FinishStock.Take(ExteriorCatalog.Downspout, Mathf.CeilToInt(run / (10f * Units.Foot)))) return false;
            if (!FinishStock.Take(ExteriorCatalog.Elbow, 3)) return false;
            if (!FinishStock.Take(ExteriorCatalog.Splash)) return false;
            e.Downspout = true; Rebuild(e);
            Game.Toast("Downspout: A/B elbows to the wall, two straps, kick-out onto a splash block");
            return true;
        }

        public static float OutletAlong(Eave e) => e.OutletAtEnd ? e.L - In(8f) : In(8f);
        public static float GutterY(Eave e, float along) => Mathf.Lerp(e.GutterYStart, e.GutterYEnd, along / e.L);

        // ================================================================== visuals

        static readonly Vector2[] KProfile =
        {
            new Vector2(0f, 4.25f), new Vector2(0f, 0.25f), new Vector2(0.25f, 0f), new Vector2(3.5f, 0f), new Vector2(3.85f, 0.55f),
            new Vector2(3.85f, 1.5f), new Vector2(4.3f, 2.15f), new Vector2(4.55f, 2.55f), new Vector2(4.7f, 3.15f), new Vector2(5f, 3.55f),
            new Vector2(5f, 4.5f), new Vector2(4.65f, 4.5f), new Vector2(4.65f, 4.3f),
        };

        public static void Rebuild(Eave e)
        {
            for (int i = e.transform.childCount - 1; i >= 0; i--) Object.Destroy(e.transform.GetChild(i).gameObject);
            var b = new MeshBuilder();
            const int sAl = 0, sSof = 1, sConc = 2, sDark = 3;
            Vector3 up = Vector3.up;
            float depthS = e.FrontOff - e.WallOff;
            if (e.Soffit)
            {
                // panels: triple 4" with V-grooves, running from the wall to the fascia
                float y = e.SoffitY;
                Vector3 vAx = Vector3.Cross(Vector3.down, e.Out);
                for (float a = 0f; a < e.L - 1e-3f; a += In(12f))
                {
                    float pw = Mathf.Min(In(12f), e.L - a);
                    for (int sgm = 0; sgm < 3; sgm++)
                    {
                        float s0 = a + sgm * In(4f), s1 = Mathf.Min(a + pw, s0 + In(4f));
                        if (s1 - s0 < 1e-3f) continue;
                        float g = Mathf.Min(In(0.25f), (s1 - s0) * 0.2f);
                        b.UvOffset = new Vector2(0f, -Vector3.Dot(e.P(s0, 0f, y), vAx));
                        var prof = new[] { new Vector2(0f, 0f), new Vector2(s1 - s0 - g, 0f) };
                        Sheet(b, prof, e.P(s0 + g * 0.5f, e.WallOff + In(0.25f), y), e.Along, up, e.Out, depthS - In(0.5f), sSof, true);
                        b.UvOffset = Vector2.zero;
                        // V groove
                        var gv = new[] { new Vector2(s1 - s0 - g * 0.5f, 0f), new Vector2(s1 - s0, In(0.2f)), new Vector2(s1 - s0 + g * 0.5f, 0f) };
                        Sheet(b, gv, e.P(s0, e.WallOff + In(0.25f), y), e.Along, up, e.Out, depthS - In(0.5f), sSof, true);
                    }
                }
                // F-channel on the wall
                Vector3 fc = e.P(e.L * 0.5f, e.WallOff + In(0.3f), y + In(0.15f));
                b.AddBox(fc, new Vector3(e.L, In(0.9f), In(0.6f)), Quaternion.LookRotation(e.Along, up), sAl, Vector3.forward);
            }
            if (e.Wrapped)
            {
                float t = In(0.03f);
                var prof = new[]
                {
                    new Vector2(-e.Thick + In(0.5f), e.Top - e.Bottom + In(0.05f)), new Vector2(t, e.Top - e.Bottom + In(0.05f)),
                    new Vector2(t, -t), new Vector2(-In(0.75f), -t), new Vector2(-In(0.75f), In(0.05f)),
                };
                Sheet(b, prof, e.P(0f, e.FrontOff, e.Bottom), e.Out, up, e.Along, e.L, sAl, true);
            }
            if (e.Gutter)
            {
                Vector3 s0 = e.P(0f, e.FrontOff + In(0.06f), e.GutterYStart - In(4.25f));
                Vector3 s1 = e.P(e.L, e.FrontOff + In(0.06f), e.GutterYEnd - In(4.25f));
                var prof = new Vector2[KProfile.Length];
                for (int i = 0; i < prof.Length; i++) prof[i] = KProfile[i] * Units.Inch;
                Sheet(b, prof, s0, e.Out, up, (s1 - s0).normalized, (s1 - s0).magnitude, sAl, true);
                // end caps
                foreach (var (pt, n) in new[] { (s0, -e.Along), (s1, e.Along) })
                {
                    Vector3 c = pt + e.Out * In(2.5f) + up * In(2.2f);
                    for (int i = 0; i + 1 < prof.Length; i++)
                    {
                        Vector3 a = pt + e.Out * prof[i].x + up * prof[i].y, d = pt + e.Out * prof[i + 1].x + up * prof[i + 1].y;
                        b.AddTriangle(c, a, d, sAl, n); b.AddTriangle(c, a, d, sAl, -n);
                    }
                }
                // hidden hangers + screws into the fascia
                foreach (float a in e.Hangers)
                {
                    float gy = GutterY(e, a);
                    Vector3 h0 = e.P(a, e.FrontOff + In(0.1f), gy - In(0.15f));
                    Vector3 h1 = e.P(a, e.FrontOff + In(4.7f), gy + In(0.2f));
                    Vector3 mid = (h0 + h1) * 0.5f, dir = h1 - h0;
                    b.AddBox(mid, new Vector3(In(1f), In(0.12f), dir.magnitude), Quaternion.LookRotation(dir, Vector3.Cross(dir, e.Along).normalized), sAl);
                    b.AddCylinder(e.P(a, e.FrontOff + In(0.4f), gy - In(0.5f)), Quaternion.FromToRotation(Vector3.up, e.Out), In(0.2f), In(0.12f), 8, sDark);
                }
                // outlet drop
                float oa = OutletAlong(e), oy = GutterY(e, oa) - In(4.25f);
                Pipe(b, e.P(oa, e.FrontOff + In(2f), oy + In(0.1f)), e.P(oa, e.FrontOff + In(2f), oy - In(3f)), e.Along, sAl, 1.08f);
            }
            if (e.Downspout)
            {
                float oa = OutletAlong(e), oy = GutterY(e, oa) - In(4.25f) - In(3f);
                float xs = e.FrontOff + In(2f), xw = e.WallOff + PipeB * 0.5f + In(0.75f);
                float dx = xs - xw;
                Vector3 p0 = e.P(oa, xs, oy), p1 = e.P(oa, xs, oy - In(1.5f));
                Vector3 p2 = e.P(oa, xw, oy - In(1.5f) - dx);       // 45° offset back to the wall
                float yb = ExteriorSite.GradeY + In(14f);
                Vector3 p3 = e.P(oa, xw, yb);
                Vector3 p4 = e.P(oa, xw + In(10f), yb - In(10f));   // kick-out elbow onto the splash block
                Pipe(b, p0, p1, e.Along, sAl, 1.08f);
                Pipe(b, p1, p2, e.Along, sAl, 1f);
                Pipe(b, p2, p3, e.Along, sAl, 1f);
                Pipe(b, p3 + Vector3.up * In(1f), p4, e.Along, sAl, 1.06f);
                // elbow collars
                foreach (var pc in new[] { p1, p2, p3 }) b.AddBox(pc, new Vector3(PipeA * 1.12f, In(2.2f), PipeB * 1.12f), Quaternion.LookRotation(e.Out, up), sAl);
                // straps
                for (int k = 1; k <= 2; k++)
                {
                    Vector3 sp = Vector3.Lerp(p2, p3, k / 3f);
                    b.AddBox(sp, new Vector3(PipeA + In(0.2f), In(1f), PipeB + In(0.2f)), Quaternion.LookRotation(e.Out, up), sAl);
                }
                // splash block (concrete trough) on grade
                Vector3 sb = e.P(oa, xw + In(18f), ExteriorSite.GradeY + In(1f));
                b.AddBox(sb, new Vector3(In(11f), In(2f), In(24f)), Quaternion.LookRotation(e.Out, up) * Quaternion.Euler(-2f, 0f, 0f), sConc, Vector3.forward);
            }
            if (b.VertexCount == 0) return;
            var mesh = Build(b, "Eave", 4);
            var go = new GameObject("EaveParts");
            go.transform.SetParent(e.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                FinishMaterials.Get(ExteriorLooks.Alum), FinishMaterials.Get(ExteriorLooks.Soffit), Materials.Get(SurfaceKind.Concrete), Materials.Get(SurfaceKind.MatteBlack),
            };
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            var fl = go.AddComponent<FinishLayer>(); fl.Kind = "eave"; fl.Owner = e;
        }

        /// <summary>2x3 rectangular downspout segment between two points (3" side along the wall run).</summary>
        static void Pipe(MeshBuilder b, Vector3 a, Vector3 c, Vector3 along, int sub, float scale)
        {
            Vector3 d = c - a; float len = d.magnitude; if (len < 1e-4f) return;
            Vector3 f = d / len;
            var rot = Quaternion.LookRotation(f, Vector3.Cross(f, along).normalized);
            b.AddBox((a + c) * 0.5f, new Vector3(PipeA * scale, PipeB * scale, len), rot, sub, Vector3.forward);
        }
    }
}
