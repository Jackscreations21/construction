using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Marks a board laid out with the framing square (pencil lines), waiting to be cut.</summary>
    public class StringerLayoutMark : MonoBehaviour
    {
        public StairResult Result;
        public float UCenter;   // board-frame center of the layout (inches along the stair-plane U axis)
    }

    /// <summary>On every cut stringer (loose or installed): the layout it was cut to.</summary>
    public class CutStringer : MonoBehaviour
    {
        public StairResult Result;
    }

    /// <summary>Real-size sawtooth stringer meshes and pencil layout lines.</summary>
    public static class StringerMesh
    {
        static float M(float inches) => inches * Units.Inch;

        /// <summary>Extruded sawtooth solid. toLocal maps a stair-plane point (inches) to the piece-local plane (meters, thickness axis = 0).
        /// Faces use faceSub (grain along 'grain'), the original board edge uses faceSub, every saw cut uses cutSub.</summary>
        public static void Solid(MeshBuilder b, StringerLayout lay, Func<Vector2, Vector3> toLocal, Vector3 thickAxis, float thickM,
                                 Vector3 grain, int faceSub, int cutSub)
        {
            var poly = lay.Profile;
            int n = poly.Count;
            if (n < 3) return;
            Vector3 h = thickAxis.normalized * (thickM * 0.5f);
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++) pts[i] = toLocal(poly[i]);

            // wide faces
            var tris = Triangulate(poly);
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                Vector3 a = pts[tris[i]], c = pts[tris[i + 1]], d = pts[tris[i + 2]];
                b.AddTriangle(a + h, c + h, d + h, faceSub, h, grain);
                b.AddTriangle(a - h, c - h, d - h, faceSub, -h, grain);
            }

            // edges: outward normal of a CCW polygon = (dy, -dx)
            float sign = SignedArea(poly) >= 0f ? 1f : -1f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % n];
                Vector2 e = q - p;
                if (e.sqrMagnitude < 1e-8f) continue;
                Vector2 n2 = new Vector2(e.y, -e.x) * sign;
                Vector3 nl = toLocal(p + n2.normalized) - toLocal(p);
                bool boardEdge = Mathf.Abs(Vector2.Dot(e.normalized, lay.U)) > 0.999f;
                Vector3 P = pts[i], Q = pts[(i + 1) % n];
                b.AddQuad(P - h, Q - h, Q + h, P + h, boardEdge ? faceSub : cutSub, nl, grain);
            }
        }

        /// <summary>Pencil lines of the framing-square layout on one wide face (faceOffset = thickness/2 + a hair).</summary>
        public static void PencilLines(MeshBuilder b, StringerLayout lay, Func<Vector2, Vector3> toLocal, Vector3 faceOffset, int sub)
        {
            var poly = lay.Profile;
            int n = poly.Count;
            Vector3 nrm = faceOffset.normalized;
            float w = 0.0012f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % n];
                Vector2 e = q - p;
                if (e.sqrMagnitude < 1e-6f) continue;
                if (Mathf.Abs(Vector2.Dot(e.normalized, lay.U)) > 0.999f) continue; // the board's own edge needs no line
                // carpenters run the square's lines a little past each corner
                Vector2 ext = e.normalized * 0.4f;
                Vector3 P = toLocal(p - ext) + faceOffset, Q = toLocal(q + ext) + faceOffset;
                Vector3 side = Vector3.Cross(nrm, (Q - P).normalized) * (w * 0.5f);
                b.AddQuad(P - side, Q - side, Q + side, P + side, sub, nrm);
            }
            // waste "X" in every notch triangle so the cut-off side is obvious
            for (int i = 0; i + 2 < n; i++)
            {
                Vector2 a = poly[i], c = poly[i + 1], d = poly[(i + 2) % n];
                // tooth point a, down the riser cut to notch c, along the seat to the next tooth point d (a and d lie on the top edge)
                bool notch = Mathf.Abs(a.x - c.x) < 1e-3f && Mathf.Abs(c.y - d.y) < 1e-3f && a.y > c.y && d.x < c.x;
                if (!notch) continue;
                Vector2 mid = (a + c + d) / 3f;
                float s = 0.9f;
                foreach (var dir in new[] { new Vector2(1, 1), new Vector2(1, -1) })
                {
                    Vector3 P = toLocal(mid - dir * s) + faceOffset, Q = toLocal(mid + dir * s) + faceOffset;
                    Vector3 side = Vector3.Cross(nrm, (Q - P).normalized) * (w * 0.5f);
                    b.AddQuad(P - side, Q - side, Q + side, P + side, sub, nrm);
                }
            }
        }

        public static float SignedArea(IList<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.x * r.y - r.x * q.y; }
            return a * 0.5f;
        }

        /// <summary>Ear-clipping triangulation of a simple polygon (returns CCW index triples).</summary>
        public static List<int> Triangulate(IList<Vector2> pts)
        {
            var idx = new List<int>();
            for (int i = 0; i < pts.Count; i++) idx.Add(i);
            if (SignedArea(pts) < 0f) idx.Reverse();
            var tris = new List<int>();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                    Vector2 A = pts[a], B = pts[b], C = pts[c];
                    float cross = (B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x);
                    if (cross <= 1e-7f) continue;
                    bool inside = false;
                    for (int j = 0; j < idx.Count && !inside; j++)
                    {
                        int q = idx[j];
                        if (q == a || q == b || q == c) continue;
                        inside = InTri(pts[q], A, B, C);
                    }
                    if (inside) continue;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // only collinear leftovers remain: drop the flattest vertex
                    idx.RemoveAt(0);
                }
            }
            if (idx.Count == 3) tris.AddRange(idx);
            return tris;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            float d2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
            float d3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
            return d1 > 1e-6f && d2 > 1e-6f && d3 > 1e-6f;
        }

        /// <summary>Framer.Custom can leave the plain stock-board visual behind (Piece.SetSize runs twice and only the first,
        /// already-dying "Visual" is destroyed). Remove every "Visual" child that isn't the custom mesh.</summary>
        public static void RemoveStockVisuals(Piece p)
        {
            foreach (Transform ch in p.transform)
            {
                if (ch.name != "Visual") continue;
                var mf = ch.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name != "Visual") UnityEngine.Object.Destroy(ch.gameObject);
            }
        }

        static Material _pencil;
        public static Material Pencil => _pencil != null ? _pencil : (_pencil = ShellMaterials.Flat("stair-pencil", new Color(0.16f, 0.16f, 0.18f), 0.45f, 0.04f, 0.2f));

        // ------------------------------------------------------------------ pieces

        /// <summary>Draws (or redraws) the pencil layout on a loose 2x12 lying in its own frame. Returns false if the board is too short.</summary>
        public static bool LayOutOnBoard(Piece board, StairResult r, out string msg)
        {
            var lay = new StringerLayout(r);
            float L = Units.ToInches(board.Size.z);
            if (lay.LengthIn + 0.5f > L) { msg = "Board too short: stringer needs " + StairMath.FormatFtIn(lay.LengthIn) + " of 2x12"; return false; }
            float uC = lay.UMin + L * 0.5f - 0.25f; // start the layout 1/4" in from the -Z end
            var old = board.transform.Find("StringerLayout");
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            var mark = board.GetComponent<StringerLayoutMark>();
            if (mark == null) mark = board.gameObject.AddComponent<StringerLayoutMark>();
            mark.Result = r; mark.UCenter = uC;
            Vector3 Local(Vector2 p) { var q = lay.ToBoard(p, uC); return new Vector3(M(q.x), 0f, M(q.y)); }
            float hy = board.Size.y * 0.5f + 0.0004f;
            Framer.AttachMesh(board.transform, "StringerLayout", (MeshBuilder b, Func<Material, int> s) =>
            {
                int pen = s(Pencil);
                PencilLines(b, lay, Local, new Vector3(0f, hy, 0f), pen);
                PencilLines(b, lay, Local, new Vector3(0f, -hy, 0f), pen);
            });
            msg = "Stringer laid out: " + r.Risers + " risers @ " + StairMath.FormatIn(r.UnitRiseIn) + ", " + r.Treads + " treads @ " + StairMath.FormatIn(r.UnitRunIn);
            return true;
        }

        /// <summary>Replaces a laid-out loose board with a loose cut stringer (same place, same box). Offcuts go to scrap.</summary>
        public static Piece CutBoard(Piece board, out int cuts)
        {
            var mark = board.GetComponent<StringerLayoutMark>();
            var r = mark.Result;
            var lay = new StringerLayout(r);
            float uC = mark.UCenter;
            Vector3 Local(Vector2 p) { var q = lay.ToBoard(p, uC); return new Vector3(M(q.x), 0f, M(q.y)); }
            var def = board.Def;
            Vector3 size = board.Size;
            var m = Framer.Custom(def.Id, size, null, board.transform.position, board.transform.rotation, MemberRole.Stringer,
                (b, s) => Solid(b, lay, Local, Vector3.up, size.y, Vector3.forward, s(Materials.ForDefinition(def)), s(SurfaceKind.PineLumber)));
            var p = m.Piece;
            RemoveStockVisuals(p);
            p.State = PieceState.Loose;
            p.Nails = 0;
            p.gameObject.name = "Cut stringer (" + def.Name + ")";
            p.gameObject.AddComponent<CutStringer>().Result = r;

            // scrap: the notch triangles and the end offcut
            float L = Units.ToInches(size.z);
            for (int i = 0; i < r.Risers - 2; i++) Game.Stock.Scrap.Add((def.Id, Units.In(r.UnitHypIn)));
            float tail = L - 0.25f - lay.LengthIn;
            if (tail > 1f) Game.Stock.Scrap.Add((def.Id, Units.In(tail)));
            cuts = 2 * (r.Risers - 1) + 2;
            UnityEngine.Object.Destroy(board.gameObject);
            return p;
        }
    }
}
