using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>How the end of a trim stick is cut.</summary>
    public enum TrimCut
    {
        Square,        // butt cut (into a corner, against casing or a cabinet)
        OutsideMiter,  // 45° long point at the face (outside corner)
        InsideMiter,   // 45° short point at the face (inside corner that was NOT coped: inspection flag, opens up)
        Cope,          // coped to the face of the mating piece (inside corner done right)
        CasingMiter,   // 45° miter in the casing plane (leg to head)
        Scarf,         // 45° scarf splice over a stud (long walls)
    }

    /// <summary>One installed stick of molding: run geometry, cuts, nails, caulk. Built from its profile by
    /// <see cref="TrimMeshBuilder"/>; rebuilt when an end cut changes (e.g. re-coped).</summary>
    public class TrimPiece : MonoBehaviour
    {
        public static readonly List<TrimPiece> All = new List<TrimPiece>();

        public TrimProfile Profile;
        public TrimSpecies Species;
        public TrimKind Kind => Profile.Kind;
        public Vector3 Origin, Dir, Out, Across;   // run frame (world): back line start, run direction, proud direction, across
        public float Length;                       // along the back line, start to end (before end extensions)
        public TrimCut StartCut, EndCut;
        public FramePlane Face;                    // framing behind (for nailing)
        public float FaceCover;                    // finish thickness the piece sits on
        public HungDoor Door;                      // casing: the door it trims
        public float Reveal;                       // casing: setback from the jamb edge (meters)
        public int Nails, NailsInFraming;
        public bool Caulked;
        public float StockLength;                  // stick it was cut from (meters)

        public Vector3 StartPoint => Origin;
        public Vector3 EndPoint => Origin + Dir * Length;
        /// <summary>Longest point to longest point (what the carpenter marks on the stick).</summary>
        public float CutLength
        {
            get
            {
                float mx = 0f;
                foreach (var p in Profile.Poly) mx = Mathf.Max(mx, Length + Ext(StartCut, p, true) + Ext(EndCut, p, false));
                return mx;
            }
        }
        public bool Nailed => Nails > 0;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public float Ext(TrimCut c, Vector2 p, bool start)
        {
            switch (c)
            {
                case TrimCut.OutsideMiter: return p.x;
                case TrimCut.InsideMiter: return -p.x - Units.In(0.04f) * (p.x / Mathf.Max(1e-4f, Profile.Thickness)); // opens at the face
                case TrimCut.Cope: return -Profile.FrontAt(p.y) - 0.0003f;
                case TrimCut.CasingMiter: return p.y;
                case TrimCut.Scarf: return start ? -p.x : p.x;
                default: return 0f;
            }
        }

        public static TrimPiece Create(TrimProfile prof, TrimSpecies sp, Vector3 origin, Vector3 dir, Vector3 outN, Vector3 across, float length, TrimCut s, TrimCut e, FramePlane face, Transform parent = null)
        {
            var go = new GameObject($"Trim_{prof.Id}");
            if (parent != null) go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>();
            var fl = go.AddComponent<FinishLayer>(); fl.Kind = "fin-trim-" + prof.Kind.ToString().ToLowerInvariant();
            var t = go.AddComponent<TrimPiece>();
            fl.Owner = t;
            t.Profile = prof; t.Species = sp; t.Origin = origin; t.Dir = dir.normalized; t.Out = outN.normalized; t.Across = across.normalized;
            t.Length = length; t.StartCut = s; t.EndCut = e; t.Face = face;
            t.Rebuild();
            return t;
        }

        public void SetCut(bool start, TrimCut c) { if (start) StartCut = c; else EndCut = c; Rebuild(); }

        public void Rebuild()
        {
            var b = new TrimMeshBuilder();
            float uOff = (Mathf.Abs(Origin.x * 7.31f + Origin.z * 3.17f + Origin.y * 5.3f) % 3f);
            b.AddRun(Profile.Poly, Vector3.zero, Dir, Out, Across, Length, p => Ext(StartCut, p, true), p => Ext(EndCut, p, false), uOff);
            transform.position = Origin; transform.rotation = Quaternion.identity;
            var mesh = b.Build("Trim_" + Profile.Id);
            var mf = GetComponent<MeshFilter>();
            var old = mf.sharedMesh; mf.sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterial = Material;
            var mc = GetComponent<MeshCollider>();
            if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = null; mc.sharedMesh = mesh;
            if (old != null && old != mesh) Destroy(old);
        }

        public Material Material => Species == TrimSpecies.Oak ? FinishMaterials.Get(InteriorMaterials.OakTrim) : FinishMaterials.Get(InteriorMaterials.Primed);

        /// <summary>Point on the run at distance t from the start, proud x, across y (world).</summary>
        public Vector3 At(float t, float x, float y) => Origin + Dir * t + Out * x + Across * y;

        // ------------------------------------------------------------------ nailing

        /// <summary>Nails the piece: 15 ga finish nails every 16" (at stud lines where the face has them) — base low+high,
        /// casing outer edge into the jack/king + 18 ga brads into the jamb edge, crown into plate (low) and joists/blocking (high).
        /// Counts how many land in framing. Returns nails driven.</summary>
        public int Nail()
        {
            if (Nailed) return 0;
            // refresh the framing (plates may have been cut out of door openings since)
            if (Face != null)
                foreach (var m in Face.Members)
                    if (m.Piece != null && m.Piece.gameObject.activeInHierarchy) { Face = FramePlane.FromPieceFace(m.Piece, Face.N) ?? Face; break; }
            var pts = new List<Vector3>();
            float w = Profile.MaxY;
            var lines = new List<float>();
            if (Face != null && Kind != TrimKind.Casing && Face.IsWall)
            {
                foreach (var u in Face.FramingLinesU())
                {
                    float t = Vector3.Dot(Face.World(u, 0f) - Origin, Dir);
                    if (t > Units.In(1f) && t < Length - Units.In(1f)) lines.Add(t);
                }
            }
            if (lines.Count == 0 || Kind == TrimKind.Casing)
            {
                lines.Clear();
                int k = Mathf.Max(2, Mathf.CeilToInt(Length / Units.In(16f)) + 1);
                for (int i = 0; i < k; i++) lines.Add(Mathf.Lerp(Units.In(2f), Length - Units.In(2f), i / (float)(k - 1)));
            }
            foreach (var t in lines)
            {
                if (Kind == TrimKind.Casing) { pts.Add(At(t, 0f, w - Units.In(0.4f))); }
                else { pts.Add(At(t, 0f, Units.In(0.6f))); pts.Add(At(t, 0f, w - Units.In(0.6f))); }
            }
            int inFraming = 0;
            foreach (var p in pts)
            {
                bool hit = false;
                if (Face != null)
                {
                    var uv = Face.ToUV(p);
                    hit = Face.MemberAt(uv, Units.In(0.1f)) != null;
                    if (!hit && Kind == TrimKind.Crown) hit = Physics.Raycast(p + Out * 0.01f + Across * 0.0f, -Out, 0.12f) ; // crown top nails go into ceiling joists/top plate
                }
                if (hit) inFraming++;
            }
            int brads = Kind == TrimKind.Casing ? lines.Count : 0; // inner edge brads into the jamb
            Nails = pts.Count + brads;
            NailsInFraming = inFraming + brads;
            return Nails;
        }

        /// <summary>Painter's caulk: bead where the trim meets the wall (top of base, both edges of crown, outer edge of casing)
        /// and nail holes filled. Returns bead length (m).</summary>
        public float Caulk()
        {
            if (Caulked) return 0f;
            Caulked = true;
            var col = new Color(0.95f, 0.95f, 0.93f);
            float w = Profile.MaxY;
            float len = 0f;
            float s0 = Mathf.Max(0f, -Ext(StartCut, new Vector2(0f, w), true)), s1 = Length + Mathf.Min(0f, Ext(EndCut, new Vector2(0f, w), false));
            void B(float x, float y) { var a = At(s0, x, y); var b = At(s1, x, y); Bead.Add(a, b, col, 0.003f, transform, "fin-caulk"); len += Vector3.Distance(a, b); }
            if (Kind == TrimKind.Crown) { B(Profile.FrontAt(0.0003f) - 0.0005f, -0.0012f); B(0.0012f, w - 0.0003f); }
            else B(0.0012f, w + 0.0006f);
            return len;
        }
    }
}
