using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Shared helpers for the save sections that fill the save/load gaps (MEP networks, exterior work, set concrete slabs).
    /// Pieces are saved by <see cref="SaveSystem"/> and come back at the same position, so a feature that hangs state off a
    /// piece refers to it by <see cref="PieceRef"/> (definition id + world position) and finds the restored piece again.
    /// </summary>
    public static class SaveGaps
    {
        [Serializable] public class PieceRef { public string id; public Vector3 pos; }

        public static PieceRef Ref(Piece p) => p != null && p.Def != null ? new PieceRef { id = p.Def.Id, pos = p.transform.position } : null;

        /// <summary>Live pieces grouped by definition id, for many lookups in one load.</summary>
        public class PieceIndex
        {
            readonly Dictionary<string, List<Piece>> _byId = new Dictionary<string, List<Piece>>();

            public PieceIndex()
            {
                foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                {
                    if (p == null || p.Def == null || !p.gameObject.activeInHierarchy) continue;
                    if (!_byId.TryGetValue(p.Def.Id, out var l)) _byId[p.Def.Id] = l = new List<Piece>();
                    l.Add(p);
                }
            }

            /// <summary>The live piece with this id nearest the saved position (within tol meters), or null.</summary>
            public Piece Find(PieceRef r, float tol = 0.01f)
            {
                if (r == null || string.IsNullOrEmpty(r.id) || !_byId.TryGetValue(r.id, out var l)) return null;
                Piece best = null; float bd = tol * tol;
                foreach (var p in l)
                {
                    if (p == null) continue;
                    float d = (p.transform.position - r.pos).sqrMagnitude;
                    if (d < bd) { bd = d; best = p; }
                }
                return best;
            }
        }

        /// <summary>Hides an object at once (no colliders / no list membership) and destroys it at the end of the frame.</summary>
        public static void Kill(GameObject g)
        {
            if (g == null) return;
            g.SetActive(false);
            UnityEngine.Object.Destroy(g);
        }

        /// <summary>Runs a rebuild step without leaving its toasts on screen (a load should not show "Deck: Joists done").</summary>
        public static void Quiet(Action a)
        {
            string t = Game.ToastText; float until = Game.ToastUntil;
            try { a(); }
            finally { Game.ToastText = t; Game.ToastUntil = until; }
        }

        /// <summary>Replays an install through the trade's own public API as it was paid for: finish stock unlimited, the tools
        /// the API insists on lent to the belt for the call (taken back after), toasts suppressed.</summary>
        public static void Replay(Action a, params string[] tools)
        {
            bool unlimited = FinishStock.Unlimited;
            var lent = new List<Tool>();
            var belt = Game.Belt;
            if (belt != null)
                foreach (var id in tools)
                {
                    if (FinishStock.Owns(id)) continue;
                    var t = ToolFactory.Create(id);
                    if (t != null) { belt.Owned.Add(t); lent.Add(t); }
                }
            FinishStock.Unlimited = true;
            var scrap = new List<(string, float)>(Game.Stock.Scrap);   // and no offcuts added again
            try { Quiet(a); }
            finally
            {
                FinishStock.Unlimited = unlimited;
                Game.Stock.Scrap.Clear(); Game.Stock.Scrap.AddRange(scrap);
                if (belt != null) foreach (var t in lent) belt.Owned.Remove(t);
            }
        }

        // ------------------------------------------------------------------ framing faces

        /// <summary>A framing face (FramePlane) by its plane and a point on it: found again on the restored framing.</summary>
        [Serializable] public class PlaneRef { public Vector3 n, point; public float offset; public bool ok; }

        public static PlaneRef Plane(FramePlane fp) => fp == null ? new PlaneRef() : new PlaneRef { n = fp.N, offset = fp.FaceOffset, point = fp.World(fp.Bounds.center), ok = true };

        /// <summary>The face of the restored framing with this plane whose framing contains the saved point (cached per load).</summary>
        public static FramePlane FindPlane(PlaneRef r)
        {
            if (r == null || !r.ok) return null;
            string key = $"{r.n.x:F3},{r.n.y:F3},{r.n.z:F3}|{r.offset:F4}|{r.point.x:F2},{r.point.y:F2},{r.point.z:F2}";
            if (_planeFrame != Time.frameCount) { _planes.Clear(); _planeFrame = Time.frameCount; }
            if (_planes.TryGetValue(key, out var hit) && hit != null) return hit;
            var fp = FindPlane(r.n, r.offset, r.point);
            if (fp != null) _planes[key] = fp;
            return fp;
        }

        static readonly Dictionary<string, FramePlane> _planes = new Dictionary<string, FramePlane>();
        static int _planeFrame = -1;

        public static FramePlane FindPlane(Vector3 n, float offset, Vector3 point)
        {
            n = n.normalized;
            var cands = new List<(Piece p, float d)>();
            foreach (var q in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (q == null || q.Def == null || !q.gameObject.activeInHierarchy) continue;
                if (q.Def.Category != PieceCategory.Lumber && q.Def.Category != PieceCategory.Sheet) continue;
                if (q.GetComponentInParent<MaterialPile>() != null) continue;
                if (FramePlane.ExcludeMember != null && FramePlane.ExcludeMember(q)) continue;
                float dist = Vector3.Distance(q.transform.position, point);
                if (dist > 40f || !FaceAt(q, n, offset)) continue;
                cands.Add((q, dist));
            }
            cands.Sort((a, b) => a.d.CompareTo(b.d));
            int tries = 0;
            foreach (var (q, _) in cands)
            {
                if (tries++ > 24) break;
                var fp = FramePlane.FromPieceFace(q, n);
                if (fp == null || Vector3.Dot(fp.N, n) < 0.999f || Mathf.Abs(fp.FaceOffset - offset) > 0.006f) continue;
                var wb = fp.WorldBounds(); wb.Expand(0.1f);
                if (wb.Contains(point)) return fp;
            }
            return null;
        }

        static bool FaceAt(Piece q, Vector3 n, float offset)
        {
            var t = q.transform; Vector3[] ax = { t.right, t.up, t.forward }; float[] hs = { q.Size.x * 0.5f, q.Size.y * 0.5f, q.Size.z * 0.5f };
            for (int i = 0; i < 3; i++)
            {
                float dot = Vector3.Dot(ax[i], n);
                if (Mathf.Abs(dot) < 0.999f) continue;
                return Mathf.Abs(Vector3.Dot(t.position, n) + hs[i] - offset) < 0.006f;
            }
            return false;
        }

        // ------------------------------------------------------------------ compact arrays

        /// <summary>Run-length encoded float array (value, count pairs) as base64: painted / primed texel layers are long runs.</summary>
        public static string Rle(float[] a)
        {
            if (a == null) return "";
            var vals = new List<float>(); var counts = new List<int>();
            for (int i = 0; i < a.Length; i++)
            {
                if (vals.Count > 0 && vals[vals.Count - 1] == a[i]) counts[counts.Count - 1]++;
                else { vals.Add(a[i]); counts.Add(1); }
            }
            var b = new byte[vals.Count * 8];
            for (int i = 0; i < vals.Count; i++) { BitConverter.GetBytes(vals[i]).CopyTo(b, i * 8); BitConverter.GetBytes(counts[i]).CopyTo(b, i * 8 + 4); }
            return Convert.ToBase64String(b);
        }

        /// <summary>Decodes <see cref="Rle"/> into <paramref name="into"/>; false (untouched) if the length doesn't match.</summary>
        public static bool UnRle(string s, float[] into)
        {
            if (string.IsNullOrEmpty(s) || into == null) return false;
            var b = Convert.FromBase64String(s);
            int total = 0;
            for (int i = 0; i + 8 <= b.Length; i += 8) total += BitConverter.ToInt32(b, i + 4);
            if (total != into.Length) return false;
            int k = 0;
            for (int i = 0; i + 8 <= b.Length; i += 8)
            {
                float v = BitConverter.ToSingle(b, i); int n = BitConverter.ToInt32(b, i + 4);
                for (int j = 0; j < n; j++) into[k++] = v;
            }
            return true;
        }

        public static string Rle(Color[] a, int channel)
        {
            if (a == null) return "";
            var f = new float[a.Length];
            for (int i = 0; i < a.Length; i++) f[i] = a[i][channel];
            return Rle(f);
        }

        public static bool UnRle(string s, Color[] into, int channel)
        {
            if (into == null) return false;
            var f = new float[into.Length];
            if (!UnRle(s, f)) return false;
            for (int i = 0; i < into.Length; i++) { var c = into[i]; c[channel] = f[i]; into[i] = c; }
            return true;
        }

        public static string Pack(float[] a)
        {
            if (a == null) return "";
            var b = new byte[a.Length * 4];
            Buffer.BlockCopy(a, 0, b, 0, b.Length);
            return Convert.ToBase64String(b);
        }

        public static float[] Unpack(string s)
        {
            if (string.IsNullOrEmpty(s)) return new float[0];
            var b = Convert.FromBase64String(s);
            var f = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, f, 0, f.Length * 4);
            return f;
        }
    }
}
