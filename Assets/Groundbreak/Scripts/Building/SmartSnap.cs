using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One pose the piece being placed could take, worked out from the pieces around the aim point.</summary>
    public struct SnapCandidate
    {
        public Vector3 Pos;
        public Quaternion Rot;
        public string Label;     // short readout: "16\" O.C.", "flush", "butt", "splice", "on center"...
        public Piece Target;     // the piece being snapped to (highlighted)
        public float Score;      // lower = better
    }

    public struct SnapQuery
    {
        public PieceDefinition Def;
        public Vector3 Size;            // ghost size (x width, y thickness, z length)
        public Vector3 Point, Normal;   // aim hit
        public Piece HitPiece;          // piece under the crosshair (may be null)
        public Vector3 BasePos;         // plain grid pose (what placement does with no neighbours)
        public Quaternion BaseRot;
        public bool ManualRotation;     // player turned the piece by hand: keep BaseRot, only align position
    }

    /// <summary>Context-aware snapping: looks at the real geometry (faces, ends, edges, centerlines) of pieces near the aim
    /// point and proposes where the selected piece belongs: a stud standing on a plate at the next on-center mark, a plate
    /// flush on a plate, an end-to-end splice, a joist butted into a rim, a sheet with its edges on framing centers.
    /// One non-alloc overlap query per frame, capped neighbour and candidate counts.</summary>
    public static class SmartSnap
    {
        public const int MaxCandidates = 8;
        const int MaxNeighbours = 32;
        const float Touch = 0.012f;                   // "resting on / touching" tolerance
        const float Parallel = 0.98f;
        static readonly float EdgeSnap = 3f * Units.Inch;
        static readonly float OcWindow = 7f * Units.Inch;
        static readonly float[] KnownSpacings = { 12f, 16f, 19.2f, 24f };

        struct Nb
        {
            public Piece P; public Vector3 C, R, U, F, H; public Quaternion Rot;
            public bool Stick, Sheet;
        }

        static readonly Collider[] _cols = new Collider[160];
        static readonly Nb[] _nbs = new Nb[MaxNeighbours];
        static int _n;
        static readonly float[] _sib = new float[24];
        static readonly float[] _tgt = new float[64];
        static readonly int[] _tgtKind = new int[64];
        static readonly Piece[] _tgtPiece = new Piece[64];
        static readonly int AimMask = Physics.DefaultRaycastLayers & ~(1 << 2);

        static SnapQuery _q;
        static Vector3 _g;           // ghost half extents
        static List<SnapCandidate> _out;

        /// <summary>Number of pieces found near the aim point by the last Solve.</summary>
        public static int NeighbourCount => _n;

        // ------------------------------------------------------------------ helpers

        static float Ext(in Nb b, Vector3 dir) =>
            Mathf.Abs(Vector3.Dot(b.R, dir)) * b.H.x + Mathf.Abs(Vector3.Dot(b.U, dir)) * b.H.y + Mathf.Abs(Vector3.Dot(b.F, dir)) * b.H.z;

        static float Ext(Quaternion rot, Vector3 half, Vector3 dir) =>
            Mathf.Abs(Vector3.Dot(rot * Vector3.right, dir)) * half.x
          + Mathf.Abs(Vector3.Dot(rot * Vector3.up, dir)) * half.y
          + Mathf.Abs(Vector3.Dot(rot * Vector3.forward, dir)) * half.z;

        static string OcLabel(float spacing) => (spacing / Units.Inch).ToString("0.#") + "\" O.C.";

        static float DefaultSpacing() =>
            Mathf.Abs(Game.Snap.GridMeters - 24f * Units.Inch) < 0.001f ? 24f * Units.Inch : 16f * Units.Inch;

        /// <summary>Grid step measured from a reference (a plate end), so marks pull from the end like a tape.</summary>
        static float GridFrom(float value, float reference) => reference + Game.Snap.SnapLength(value - reference);

        static void Add(Vector3 pos, Quaternion rot, string label, Piece target, float score)
        {
            for (int i = 0; i < _out.Count; i++)
            {
                if ((_out[i].Pos - pos).sqrMagnitude > 4e-6f || Quaternion.Angle(_out[i].Rot, rot) > 1f) continue;
                if (score < _out[i].Score) _out[i] = new SnapCandidate { Pos = pos, Rot = rot, Label = label, Target = target, Score = score };
                return;
            }
            _out.Add(new SnapCandidate { Pos = pos, Rot = rot, Label = label, Target = target, Score = score });
        }

        /// <summary>Signed neighbour axis closest to a direction. axis: 0 = R (width), 1 = U (thickness), 2 = F (length).</summary>
        static bool FaceAxis(in Nb b, Vector3 n, out Vector3 axisDir, out int axis)
        {
            float dr = Vector3.Dot(b.R, n), du = Vector3.Dot(b.U, n), df = Vector3.Dot(b.F, n);
            float ar = Mathf.Abs(dr), au = Mathf.Abs(du), af = Mathf.Abs(df);
            if (ar >= au && ar >= af) { axis = 0; axisDir = dr < 0 ? -b.R : b.R; return ar > 0.9f; }
            if (au >= af) { axis = 1; axisDir = du < 0 ? -b.U : b.U; return au > 0.9f; }
            axis = 2; axisDir = df < 0 ? -b.F : b.F; return af > 0.9f;
        }

        /// <summary>Offset across a face: centred when the widths match, otherwise the flush edge (or centre) nearest the aim.</summary>
        static float PickCross(float aim, float faceHalf, float ghostHalf)
        {
            float d = faceHalf - ghostHalf;
            if (Mathf.Abs(d) < 0.001f) return 0f;
            float best = 0f, bd = Mathf.Abs(aim);
            if (Mathf.Abs(aim - d) < bd) { best = d; bd = Mathf.Abs(aim - d); }
            if (Mathf.Abs(aim + d) < bd) best = -d;
            return best;
        }

        static int IndexOf(Piece p)
        {
            if (p == null) return -1;
            for (int i = 0; i < _n; i++) if (_nbs[i].P == p) return i;
            return -1;
        }

        static void SortSiblings(int count)
        {
            for (int i = 1; i < count; i++)
            {
                float v = _sib[i]; int j = i - 1;
                while (j >= 0 && _sib[j] > v) { _sib[j + 1] = _sib[j]; j--; }
                _sib[j + 1] = v;
            }
        }

        /// <summary>Spacing already in use among sorted sibling positions (12 / 16 / 19.2 / 24" O.C.), else the default.</summary>
        static float SpacingInUse(int count)
        {
            float sp = DefaultSpacing();
            if (count < 2) return sp;
            SortSiblings(count);
            float min = float.MaxValue;
            for (int i = 1; i < count; i++) { float gap = _sib[i] - _sib[i - 1]; if (gap > 0.05f && gap < min) min = gap; }
            foreach (float k in KnownSpacings)
                if (Mathf.Abs(min - k * Units.Inch) < 0.5f * Units.Inch) return k * Units.Inch;
            return sp;
        }

        // ------------------------------------------------------------------ neighbour search

        static void Gather(Vector3 point, float radius)
        {
            _n = 0;
            int hits = Physics.OverlapSphereNonAlloc(point, radius, _cols, AimMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits && _n < MaxNeighbours; i++)
            {
                var col = _cols[i];
                if (col == null) continue;
                if (!col.TryGetComponent(out Piece p)) p = col.GetComponentInParent<Piece>();
                if (p == null || p.Def == null) continue;
                bool dup = false;
                for (int k = 0; k < _n; k++) if (_nbs[k].P == p) { dup = true; break; }
                if (dup) continue;
                var t = p.transform;
                Vector3 h = p.Size * 0.5f;
                _nbs[_n++] = new Nb
                {
                    P = p, C = t.position, Rot = t.rotation, R = t.right, U = t.up, F = t.forward, H = h,
                    Stick = p.Def.Category == PieceCategory.Lumber && h.z >= 2f * Mathf.Max(h.x, h.y),
                    Sheet = p.Def.Category == PieceCategory.Sheet,
                };
            }
            for (int i = 0; i < hits; i++) _cols[i] = null;
        }

        // ------------------------------------------------------------------ entry point

        /// <summary>Fills <paramref name="results"/> (best first). Empty = nothing nearby: use the plain grid pose.</summary>
        public static void Solve(in SnapQuery q, List<SnapCandidate> results)
        {
            results.Clear();
            _n = 0;
            if (q.Def == null) return;
            _q = q; _g = q.Size * 0.5f; _out = results;

            bool gStick = q.Def.Category == PieceCategory.Lumber;
            bool gSheet = q.Def.Category == PieceCategory.Sheet;
            float longest = Mathf.Max(_g.x, Mathf.Max(_g.y, _g.z));
            Gather(q.Point, gSheet ? 1.8f : Mathf.Clamp(longest + 0.4f, 0.9f, 1.7f));
            if (_n == 0) return;

            if (!q.ManualRotation)
            {
                if (gStick) StickCandidates();
                else if (gSheet) SheetCandidates();
                else LikeNeighbour();
            }
            Align(q.BasePos, q.BaseRot, 3f, null);

            if (results.Count == 0) return;
            results.Sort(ByScore);
            if (results.Count > MaxCandidates - 1) results.RemoveRange(MaxCandidates - 1, results.Count - (MaxCandidates - 1));
            // the plain grid pose is always reachable by cycling
            results.Add(new SnapCandidate { Pos = q.BasePos, Rot = q.BaseRot, Label = "grid", Target = null, Score = 99f });
            _out = null;
        }

        static readonly System.Comparison<SnapCandidate> ByScore = (a, b) => a.Score.CompareTo(b.Score);

        // ------------------------------------------------------------------ lumber

        static void StickCandidates()
        {
            int hi = IndexOf(_q.HitPiece);
            Vector3 n = _q.Normal;
            if (hi >= 0 && _nbs[hi].Stick && FaceAxis(_nbs[hi], _q.Normal, out Vector3 N, out int axis))
            {
                n = N;
                if (axis == 2) EndFace(_nbs[hi], N);
                else SideFace(_nbs[hi], N, axis);
            }
            Lying(n, hi);
        }

        static bool StudLike(PieceDefinition d) =>
            (d.Id != null && d.Id.Contains("925")) || (d.Name != null && d.Name.ToLowerInvariant().Contains("stud"));

        /// <summary>A plate that sits on stud tops is a top plate: the next flat board on it is the doubled plate.</summary>
        static bool HasStudBelow(in Nb h)
        {
            float bottom = h.C.y - Ext(h, Vector3.up);
            Vector3 across = Vector3.Cross(Vector3.up, h.F).normalized;
            for (int i = 0; i < _n; i++)
            {
                ref Nb b = ref _nbs[i];
                if (!b.Stick || b.P == h.P || Mathf.Abs(b.F.y) < 0.9f) continue;
                if (Mathf.Abs(b.C.y + b.H.z - bottom) > 0.015f) continue;
                Vector3 d = b.C - h.C;
                if (Mathf.Abs(Vector3.Dot(d, h.F)) > h.H.z + 0.05f) continue;
                if (Mathf.Abs(Vector3.Dot(d, across)) > Ext(h, across) + 0.05f) continue;
                return true;
            }
            return false;
        }

        /// <summary>Aimed at a long face of a board: stand a member on it (butt) or lay one against it (flush).</summary>
        static void SideFace(in Nb h, Vector3 N, int axis)
        {
            Vector3 L = h.F;
            Vector3 C = Vector3.Cross(N, L).normalized;
            float hN = axis == 1 ? h.H.y : h.H.x;
            float wC = axis == 1 ? h.H.x : h.H.y;
            Vector3 fc = h.C + N * hN;
            float ta = Vector3.Dot(_q.Point - h.C, L), ca = Vector3.Dot(_q.Point - h.C, C);
            bool nVert = Mathf.Abs(N.y) > 0.9f, lVert = Mathf.Abs(L.y) > 0.9f, lHoriz = Mathf.Abs(L.y) < 0.1f;

            // which reading is the likely one for this face
            float buttBias, flushBias;
            if (nVert && lHoriz)
            {
                if (N.y < 0f) { buttBias = 0f; flushBias = 1.5f; }                       // underside of a top plate: stud
                else if (axis == 0) { buttBias = 1.2f; flushBias = 1.0f; }               // top edge of a joist
                else if (!StudLike(_q.Def) && HasStudBelow(h)) { buttBias = 1.5f; flushBias = 0f; }   // doubled top plate
                else { buttBias = 0f; flushBias = 1.5f; }                                // stud on a plate
            }
            else if (lHoriz) { if (axis == 1) { buttBias = 0f; flushBias = 1.5f; } else { buttBias = 1.5f; flushBias = 0f; } }  // rim: joist butts in
            else { buttBias = 1.5f; flushBias = 0f; }                                    // stud face: jack against king

            // ---- butt: ghost length along the face normal, thickness along the board
            {
                Quaternion rot = Quaternion.LookRotation(N, L);
                float gC = _g.x, gL = _g.y;
                float c = PickCross(ca, wC, gC);
                if (Mathf.Abs(C.y) > 0.9f && Mathf.Abs(wC - gC) > 0.001f) c = Mathf.Sign(C.y) * (wC - gC);   // tops flush (joist on a deeper rim)
                float end = h.H.z, lim = end - gL;

                int count = 0;
                for (int i = 0; i < _n && count < _sib.Length; i++)
                {
                    ref Nb b = ref _nbs[i];
                    if (!b.Stick || b.P == h.P || Mathf.Abs(Vector3.Dot(b.F, N)) < Parallel) continue;
                    Vector3 d = b.C - fc;
                    if (Mathf.Abs(Vector3.Dot(d, N) - b.H.z) > Touch) continue;
                    if (Mathf.Abs(Vector3.Dot(d, C)) > wC + 0.05f) continue;
                    float t = Vector3.Dot(b.C - h.C, L);
                    if (Mathf.Abs(t) > end + 0.05f) continue;
                    _sib[count++] = t;
                }
                float sp = SpacingInUse(count);

                // layout pulls from a plate end when the existing members sit on that end's layout, else from the nearest member
                float anchor = ta < 0f ? -end : end; bool fromEnd = true;
                if (count > 0)
                {
                    fromEnd = false;
                    for (int e = 0; e < 2 && !fromEnd; e++)
                    {
                        float ePos = e == 0 ? -end : end; bool all = true;
                        for (int i = 0; i < count && all; i++)
                        {
                            float off = Mathf.Abs(_sib[i] - ePos);
                            float k = Mathf.Round(off / sp);
                            bool onLayout = k >= 1f && Mathf.Abs(off - k * sp) < 0.007f;
                            bool endFlush = Mathf.Abs(off - gL) < 0.007f;
                            all = onLayout || endFlush;
                        }
                        if (all) { anchor = ePos; fromEnd = true; }
                    }
                    if (!fromEnd)
                    {
                        float bd = float.MaxValue;
                        for (int i = 0; i < count; i++) { float d = Mathf.Abs(_sib[i] - ta); if (d < bd) { bd = d; anchor = _sib[i]; } }
                    }
                }

                bool haveOc = false; float tOc = 0f; bool ocIsEnd = false;
                float k0 = Mathf.Round((ta - anchor) / sp);
                for (int attempt = 0; attempt < 3 && !haveOc; attempt++)
                {
                    float dir = ta >= anchor + k0 * sp ? 1f : -1f;
                    float kk = k0 + (attempt == 0 ? 0f : attempt == 1 ? dir : -dir);
                    float t = anchor + kk * sp; bool isEnd = false;
                    if (fromEnd && kk == 0f) { t = anchor + (anchor < 0f ? gL : -gL); isEnd = true; }
                    else if (Mathf.Abs(t) > lim + 0.001f)
                    {
                        if (Mathf.Abs(t) > end + gL + 0.001f) continue;
                        t = Mathf.Sign(t) * lim; isEnd = true;                           // last mark lands on the end: stud flush with it
                    }
                    bool occupied = false;
                    for (int i = 0; i < count; i++) if (Mathf.Abs(_sib[i] - t) < 0.03f) { occupied = true; break; }
                    if (occupied) continue;
                    haveOc = true; tOc = t; ocIsEnd = isEnd;
                }

                string gap = "";
                if (haveOc)
                {
                    Vector3 p = fc + N * _g.z + C * c + L * tOc;
                    gap = GapNote(fc + C * c + L * tOc, N, h.P);
                    float dist = Mathf.Abs(tOc - ta);
                    float score = buttBias + (count > 0 ? 0f : 0.3f) + dist + (dist > OcWindow ? 2f : 0f);
                    Add(p, rot, (ocIsEnd ? "flush end" : OcLabel(sp)) + gap, h.P, score);
                }
                float tEnd = ta < 0f ? -lim : lim;
                if (Mathf.Abs(tEnd - ta) < EdgeSnap)
                    Add(fc + N * _g.z + C * c + L * tEnd, rot, "flush end" + gap, h.P, buttBias + 0.2f + Mathf.Abs(tEnd - ta));
                float tg = Mathf.Clamp(GridFrom(ta, -end), -lim, lim);
                Add(fc + N * _g.z + C * c + L * tg, rot, "butt" + GapNote(fc + C * c + L * tg, N, h.P), h.P, buttBias + 0.6f);
            }

            // ---- flush: same orientation as the board, lying against the face
            {
                float gN = axis == 1 ? _g.y : _g.x, gC = axis == 1 ? _g.x : _g.y;
                float c = PickCross(ca, wC, gC);
                float d = h.H.z - _g.z, t;
                if (Mathf.Abs(d) < 0.001f) t = 0f;
                else if (lVert) t = L.y > 0f ? -d : d;                                   // bottoms together (jack beside king)
                else
                {
                    t = Mathf.Abs(ta - d) < Mathf.Abs(ta + d) ? d : -d;                  // nearer ends together
                    if (Mathf.Abs(t - ta) > 12f * Units.Inch) t = GridFrom(ta, -h.H.z);
                }
                Add(fc + N * gN + C * c + L * t, h.Rot, "flush", h.P, flushBias + 0.4f);
            }
        }

        /// <summary>Distance to whatever faces this one (a top plate over a bottom plate): tells the player the cut length.</summary>
        static string GapNote(Vector3 onFace, Vector3 N, Piece from)
        {
            float len = _q.Size.z;
            if (!Physics.Raycast(onFace + N * 0.02f, N, out RaycastHit hit, len + 0.6f, AimMask, QueryTriggerInteraction.Ignore)) return "";
            if (hit.collider == null || hit.collider.GetComponentInParent<Piece>() == null) return "";
            float gap = hit.distance + 0.02f;
            return Mathf.Abs(gap - len) <= 0.004f ? " · fits" : " · gap " + Units.Format(gap);
        }

        /// <summary>Row of parallel members whose ends share a plane (stud tops, joist ends): outer faces along the row.</summary>
        static void RowExtents(in Nb h, Vector3 N, Vector3 line, out float lo, out float hi)
        {
            Vector3 endC = h.C + N * h.H.z;
            Vector3 w = Vector3.Cross(N, line).normalized;
            float e0 = Ext(h, line);
            lo = -e0; hi = e0;
            for (int i = 0; i < _n; i++)
            {
                ref Nb b = ref _nbs[i];
                if (!b.Stick || b.P == h.P || Mathf.Abs(Vector3.Dot(b.F, h.F)) < Parallel) continue;
                if (Mathf.Abs(Vector3.Dot(b.C - endC, N) + b.H.z) > Touch) continue;
                if (Mathf.Abs(Vector3.Dot(b.C - h.C, w)) > 0.06f) continue;
                float t = Vector3.Dot(b.C - h.C, line), e = Ext(b, line);
                lo = Mathf.Min(lo, t - e); hi = Mathf.Max(hi, t + e);
            }
        }

        static float RowSlide(float ta, float lo, float hi, float ghostHalf, out bool flush)
        {
            float a = lo + ghostHalf, b = hi - ghostHalf;
            float t = Mathf.Abs(ta - a) <= Mathf.Abs(ta - b) ? a : b;
            flush = Mathf.Abs(t - ta) <= ghostHalf + 24f * Units.Inch;
            return flush ? t : GridFrom(ta, lo);
        }

        /// <summary>Aimed at the end grain: continue the board (splice) or run a plate / rim across the ends (cap).</summary>
        static void EndFace(in Nb h, Vector3 N)
        {
            Vector3 endC = h.C + N * h.H.z;
            float spliceScore, capScore;
            Vector3 capPos; Quaternion capRot; string capLabel;

            if (Mathf.Abs(N.y) > 0.9f)
            {
                // top of a standing stud: a plate along the wall line (the studs' thin axis)
                Vector3 line = h.U;
                capRot = Quaternion.LookRotation(line, N);
                float ta = Vector3.Dot(_q.Point - h.C, line);
                float across = PickCross(Vector3.Dot(_q.Point - h.C, h.R), h.H.x, _g.x);
                float t = 0f; bool found = false; capLabel = "plate";
                Vector3 foot = h.C - N * h.H.z;
                for (int i = 0; i < _n && !found; i++)
                {
                    ref Nb b = ref _nbs[i];
                    if (!b.Stick || b.P == h.P || Mathf.Abs(Vector3.Dot(b.F, line)) < Parallel) continue;
                    if (Mathf.Abs(Mathf.Abs(Vector3.Dot(b.C - foot, N)) - Ext(b, N)) > Touch) continue;
                    if (Mathf.Abs(Vector3.Dot(b.C - h.C, h.R)) > h.H.x + 0.05f) continue;
                    if (Mathf.Abs(b.H.z - _g.z) > 0.002f) continue;
                    t = Vector3.Dot(b.C - h.C, line); found = true; capLabel = "over plate";   // same length as the plate below: stack it
                }
                if (!found)
                {
                    RowExtents(h, N, line, out float lo, out float hi);
                    t = RowSlide(ta, lo, hi, _g.z, out bool flush);
                    if (flush) capLabel = "flush end";
                }
                capPos = endC + N * _g.y + h.R * across + line * t;
                capScore = 0.2f; spliceScore = 1.2f;
            }
            else
            {
                bool onEdge = Mathf.Abs(h.R.y) > 0.9f;
                Vector3 a = Mathf.Abs(h.U.y) < Mathf.Abs(h.R.y) ? h.U : h.R;      // the horizontal axis of the end face
                Vector3 v = Mathf.Abs(h.U.y) < Mathf.Abs(h.R.y) ? h.R : h.U;      // the upright one
                if (v.y < 0f) v = -v;
                float ta = Vector3.Dot(_q.Point - h.C, a);
                RowExtents(h, N, a, out float lo, out float hi);
                float t = RowSlide(ta, lo, hi, _g.z, out bool flush);
                capLabel = flush ? "flush corner" : "butt";
                if (onEdge)
                {
                    // joist ends: a rim on edge across them, tops flush
                    capRot = Quaternion.LookRotation(a, N);
                    capPos = endC + N * _g.y + v * (Ext(h, v) - _g.x) + a * t;
                    capScore = 0.2f; spliceScore = 0.6f;
                }
                else
                {
                    // end of a flat plate: the next wall turns the corner, bottoms level
                    capRot = Quaternion.LookRotation(a, v);
                    capPos = endC + N * _g.x + v * (_g.y - Ext(h, v)) + a * t;
                    capScore = 0.8f; spliceScore = 0.2f;
                }
            }
            Add(capPos, capRot, capLabel, h.P, capScore);
            Add(endC + N * _g.z, h.Rot, "splice", h.P, spliceScore);
        }

        /// <summary>Members already lying on the aimed surface: continue their on-center layout, or extend one in line.</summary>
        static void Lying(Vector3 n, int hitIndex)
        {
            Vector3 P = _q.Point;
            for (int i = 0; i < _n; i++)
            {
                if (i == hitIndex) continue;
                ref Nb b = ref _nbs[i];
                if (!b.Stick || Mathf.Abs(Vector3.Dot(b.F, n)) > 0.05f) continue;
                float bN = Ext(b, n);
                if (Mathf.Abs(Vector3.Dot(b.C - P, n) - bN) > Touch) continue;

                Vector3 A = Vector3.Cross(n, b.F).normalized;
                float ta = Vector3.Dot(P - b.C, b.F), la = Vector3.Dot(P - b.C, A);
                float gRest = Ext(b.Rot, _g, n), gA = Ext(b.Rot, _g, A), bA = Ext(b, A);
                Vector3 baseC = b.C + n * (gRest - bN);

                if (Mathf.Abs(la) <= Mathf.Max(bA, gA) + 4f * Units.Inch)
                {
                    float beyond = Mathf.Abs(ta) - b.H.z;
                    if (beyond <= 0f) continue;
                    if (beyond < 2f * _g.z + 0.3f)
                        Add(baseC + b.F * (Mathf.Sign(ta) * (b.H.z + _g.z)), b.Rot, "splice", b.P, 0.1f + Mathf.Abs(la));
                    else
                        Add(baseC + b.F * (Mathf.Sign(ta) * (b.H.z + Game.Snap.SnapLength(beyond))), b.Rot, "in line", b.P, 0.4f + Mathf.Abs(la));
                    continue;
                }
                if (Mathf.Abs(ta) > b.H.z + 0.3f) continue;

                // spacing already in use among the members lying beside this one
                int count = 0;
                for (int j = 0; j < _n && count < _sib.Length; j++)
                {
                    ref Nb o = ref _nbs[j];
                    if (j == hitIndex || !o.Stick || Mathf.Abs(Vector3.Dot(o.F, b.F)) < Parallel) continue;
                    if (Mathf.Abs(Vector3.Dot(o.C - P, n) - Ext(o, n)) > Touch) continue;
                    if (Mathf.Abs(Vector3.Dot(o.C - b.C, b.F)) > b.H.z + o.H.z) continue;
                    _sib[count++] = Vector3.Dot(o.C - b.C, A);
                }
                float sp = SpacingInUse(count);
                float k = Mathf.Max(1f, Mathf.Round(Mathf.Abs(la) / sp));
                float miss = Mathf.Abs(Mathf.Abs(la) - k * sp);
                if (miss > OcWindow) continue;
                float target = Mathf.Sign(la) * k * sp;
                bool occupied = false;
                for (int j = 0; j < count; j++) if (Mathf.Abs(_sib[j] - target) < 0.03f) { occupied = true; break; }
                if (occupied) continue;
                float d = b.H.z - _g.z, along = 0f;
                if (Mathf.Abs(d) > 0.001f) along = Mathf.Abs(ta - d) < Mathf.Abs(ta + d) ? d : -d;
                Add(baseC + A * target + b.F * along, b.Rot, OcLabel(sp), b.P, 0.15f + miss + Mathf.Abs(la) * 0.05f);
            }
        }

        // ------------------------------------------------------------------ sheet goods

        /// <summary>Sheathing / subfloor: lies on the framing plane with its edges on member centerlines (or flush with the
        /// outside of an end member, or butted to the previous sheet).</summary>
        static void SheetCandidates()
        {
            Vector3 P = _q.Point, n = _q.Normal;
            int hi = IndexOf(_q.HitPiece);
            if (hi < 0 || !_nbs[hi].Stick) return;
            if (!FaceAxis(_nbs[hi], _q.Normal, out Vector3 N, out int hitAxis)) return;
            n = N;

            // framing direction: studs run up a wall; on a floor or roof take the aimed joist / rafter
            Vector3 L;
            if (Mathf.Abs(n.y) < 0.3f) L = (Vector3.up - n * n.y).normalized;
            else if (hitAxis != 2) L = _nbs[hi].F;
            else return;
            Vector3 A = Vector3.Cross(n, L).normalized;

            int targets = 0;
            float lMin = float.MaxValue, lMax = float.MinValue;
            bool any = false;
            // pass 1: members with a face in the plane
            for (int i = 0; i < _n; i++)
            {
                ref Nb b = ref _nbs[i];
                if (!b.Stick) continue;
                if (Mathf.Abs(Vector3.Dot(b.C - P, n) + Ext(b, n)) > 0.004f) continue;
                if (Mathf.Max(Mathf.Abs(Vector3.Dot(b.R, n)), Mathf.Max(Mathf.Abs(Vector3.Dot(b.U, n)), Mathf.Abs(Vector3.Dot(b.F, n)))) < 0.99f) continue;
                any = true;
                float l = Vector3.Dot(b.C - P, L), el = Ext(b, L);
                lMin = Mathf.Min(lMin, l - el); lMax = Mathf.Max(lMax, l + el);
                if (Mathf.Abs(Vector3.Dot(b.F, L)) < Parallel) continue;

                float a = Vector3.Dot(b.C - P, A), ha = Ext(b, A);
                bool lowEnd = true, highEnd = true;
                for (int j = 0; j < _n; j++)
                {
                    if (j == i) continue;
                    ref Nb o = ref _nbs[j];
                    if (!o.Stick || Mathf.Abs(Vector3.Dot(o.F, L)) < Parallel) continue;
                    if (Mathf.Abs(Vector3.Dot(o.C - P, n) + Ext(o, n)) > 0.004f) continue;
                    float oa = Vector3.Dot(o.C - P, A) - a;
                    if (oa < -0.05f && oa > -26f * Units.Inch) lowEnd = false;      // (doubled studs don't count as a bay)
                    if (oa > 0.05f && oa < 26f * Units.Inch) highEnd = false;
                }
                if (targets >= _tgt.Length - 2) continue;
                if (!lowEnd && !highEnd) { _tgt[targets] = a; _tgtKind[targets] = 0; _tgtPiece[targets++] = b.P; }
                else
                {
                    if (lowEnd) { _tgt[targets] = a - ha; _tgtKind[targets] = 1; _tgtPiece[targets++] = b.P; }
                    if (highEnd) { _tgt[targets] = a + ha; _tgtKind[targets] = 1; _tgtPiece[targets++] = b.P; }
                }
            }
            if (!any) return;

            // sheets already hung on this plane: butt to their edges
            int lTargets = 0; float sheetLo = 0f, sheetHi = 0f; bool sheetL = false;
            for (int i = 0; i < _n; i++)
            {
                ref Nb b = ref _nbs[i];
                if (!b.Sheet || Mathf.Abs(Vector3.Dot(b.U, n)) < 0.99f) continue;
                if (Mathf.Abs(Vector3.Dot(b.C - P, n) - b.H.y) > 0.004f) continue;
                float a = Vector3.Dot(b.C - P, A), ha = Ext(b, A);
                if (targets < _tgt.Length - 2)
                {
                    _tgt[targets] = a - ha; _tgtKind[targets] = 2; _tgtPiece[targets++] = b.P;
                    _tgt[targets] = a + ha; _tgtKind[targets] = 2; _tgtPiece[targets++] = b.P;
                }
                if (Mathf.Abs(a) < ha + 0.05f)
                {
                    float l = Vector3.Dot(b.C - P, L), el = Ext(b, L);
                    if (!sheetL || Mathf.Abs(l) < Mathf.Abs((sheetLo + sheetHi) * 0.5f)) { sheetLo = l - el; sheetHi = l + el; sheetL = true; }
                    lTargets++;
                }
            }

            bool wall = Mathf.Abs(L.y) > 0.9f;
            for (int v = 0; v < 2; v++)
            {
                bool lengthAlongL = v == 0 ? wall : !wall;     // walls: sheet stands up; floors and roofs: across the framing
                float hA = lengthAlongL ? _g.x : _g.z, hL = lengthAlongL ? _g.z : _g.x;
                Quaternion rot = Quaternion.LookRotation(lengthAlongL ? L : A, n);

                float c = 0f, best = float.MaxValue; int kind = -1; Piece tp = null;
                for (int i = 0; i < targets; i++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float center = _tgt[i] + s * hA;
                        float cost = Mathf.Abs(center) - (_tgtKind[i] == 2 ? 0.02f : 0f);
                        if (cost < best) { best = cost; c = center; kind = _tgtKind[i]; tp = _tgtPiece[i]; }
                    }
                if (kind < 0 || Mathf.Abs(c) > 12f * Units.Inch) { c = 0f; kind = -1; tp = _q.HitPiece; }

                // walls are sheathed from the bottom up: the first course sits on the bottom of the wall
                float l0 = lMin + hL, bl = wall && !sheetL ? 0f : Mathf.Abs(l0);   // (a sheet already in this column: the next one stacks on it)
                void TryL(float cand) { if (Mathf.Abs(cand) < bl) { bl = Mathf.Abs(cand); l0 = cand; } }
                if (!wall) TryL(lMax - hL);
                if (sheetL) { TryL(sheetHi + hL); TryL(sheetLo - hL); }
                if (bl > 24f * Units.Inch) l0 = GridFrom(0f, lMin + hL);

                string label = kind == 0 ? "on center" : kind == 1 ? "flush edge" : kind == 2 ? "butt" : "on framing";
                Add(P + A * c + L * l0 + n * _g.y, rot, label, tp, v * 0.5f + Mathf.Abs(c) * 0.1f);
            }
        }

        // ------------------------------------------------------------------ everything else

        /// <summary>Bricks, blocks, fixtures: take the orientation of the nearest piece of the same product, then align.</summary>
        static void LikeNeighbour()
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < _n; i++)
            {
                ref Nb b = ref _nbs[i];
                if (b.P.Def != _q.Def) continue;
                float d = (b.C - _q.Point).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return;
            Quaternion rot = _nbs[best].Rot;
            if (Quaternion.Angle(rot, _q.BaseRot) < 1f) return;
            Vector3 n = _q.Normal;
            Vector3 pos = Game.Snap.SnapPosition(_q.Point + n * Ext(rot, _g, n));
            float h = Ext(rot, _g, n);
            pos += n * (Vector3.Dot(_q.Point, n) + h - Vector3.Dot(pos, n));
            if (!Align(pos, rot, 2.5f, "aligned")) Add(pos, rot, "aligned", _nbs[best].P, 2.6f);
        }

        /// <summary>Position-only alignment for a fixed rotation: faces flush or butted against nearby boxes whose axes
        /// line up with the ghost's (corners, runs of block, sheets side by side).</summary>
        static bool Align(Vector3 pos, Quaternion rot, float score, string prefix)
        {
            Vector3 ax0 = rot * Vector3.right, ax1 = rot * Vector3.up, ax2 = rot * Vector3.forward;
            float d0n = Mathf.Abs(Vector3.Dot(ax0, _q.Normal)), d1n = Mathf.Abs(Vector3.Dot(ax1, _q.Normal)), d2n = Mathf.Abs(Vector3.Dot(ax2, _q.Normal));
            int nAxis = d0n > 0.9f ? 0 : d1n > 0.9f ? 1 : d2n > 0.9f ? 2 : -1;

            float s0 = 0, s1 = 0, s2 = 0, b0 = float.MaxValue, b1 = float.MaxValue, b2 = float.MaxValue;
            bool butt = false, flush = false; Piece target = null; float targetBest = float.MaxValue;

            for (int i = 0; i < _n; i++)
            {
                ref Nb b = ref _nbs[i];
                if (b.P == _q.HitPiece) continue;      // the aimed piece is already handled by the face / edge snap
                if (!AxisAligned(ax0, b) || !AxisAligned(ax1, b) || !AxisAligned(ax2, b)) continue;
                Vector3 d = b.C - pos;
                float dx = Vector3.Dot(d, ax0), dy = Vector3.Dot(d, ax1), dz = Vector3.Dot(d, ax2);
                float hx = Ext(b, ax0), hy = Ext(b, ax1), hz = Ext(b, ax2);
                float gx = Mathf.Abs(dx) - (_g.x + hx), gy = Mathf.Abs(dy) - (_g.y + hy), gz = Mathf.Abs(dz) - (_g.z + hz);
                for (int a = 0; a < 3; a++)
                {
                    if (a == nAxis) continue;
                    float di = a == 0 ? dx : a == 1 ? dy : dz, hi = a == 0 ? hx : a == 1 ? hy : hz, gi = a == 0 ? _g.x : a == 1 ? _g.y : _g.z;
                    float gapI = a == 0 ? gx : a == 1 ? gy : gz;
                    float gapJ = a == 0 ? gy : gx, gapK = a == 2 ? gy : gz;
                    float shift = 0f; bool ok = false, isButt = false;
                    // butt: faces meet along this axis, boxes overlap across it
                    if (Mathf.Abs(gapI) <= EdgeSnap && gapJ <= 0.0127f && gapK <= 0.0127f)
                    { shift = Mathf.Sign(di) * gapI; ok = true; isButt = true; }
                    // flush: side by side, a pair of faces brought into one plane
                    else if (gapJ <= EdgeSnap && gapK <= EdgeSnap && (gapJ >= -0.005f || gapK >= -0.005f))
                    {
                        float sMax = di + hi - gi, sMin = di - hi + gi;
                        shift = Mathf.Abs(sMax) < Mathf.Abs(sMin) ? sMax : sMin;
                        ok = Mathf.Abs(shift) <= EdgeSnap;
                    }
                    if (!ok) continue;
                    float abs = Mathf.Abs(shift);
                    ref float bestA = ref b0; ref float sA = ref s0;
                    if (a == 1) { bestA = ref b1; sA = ref s1; } else if (a == 2) { bestA = ref b2; sA = ref s2; }
                    if (abs >= bestA) continue;
                    bestA = abs; sA = shift;
                    if (isButt) butt = true; else flush = true;
                    if (abs < targetBest) { targetBest = abs; target = b.P; }
                }
            }
            if (target == null) return false;
            Vector3 p = pos + ax0 * s0 + ax1 * s1 + ax2 * s2;
            string label = butt && flush ? "corner" : butt ? "butt" : "flush";
            Add(p, rot, prefix != null ? prefix + " · " + label : label, target, score);
            return true;
        }

        static bool AxisAligned(Vector3 a, in Nb b) =>
            Mathf.Abs(Vector3.Dot(a, b.R)) > 0.9994f || Mathf.Abs(Vector3.Dot(a, b.U)) > 0.9994f || Mathf.Abs(Vector3.Dot(a, b.F)) > 0.9994f;
    }
}
