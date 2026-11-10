using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum SidingKind { None, Vinyl, FiberCement, BoardBatten, Brick }

    /// <summary>Site-wide exterior settings (grade level, frost depth).</summary>
    public static class ExteriorSite
    {
        /// <summary>World Y of finished grade (the game ground plane is y = 0).</summary>
        public static float GradeY = 0f;
        /// <summary>Frost depth for deck footings (code minimum where the house is built).</summary>
        public static float FrostDepth = Units.In(36f);
    }

    /// <summary>Tag on every exterior part (course, post, tie row...) so tools know what they aim at.</summary>
    public class ExtPart : MonoBehaviour
    {
        public ExteriorWall Wall;
        public string Type;   // "wrap", "vinyl-course", "brick", "fc-course", ...
        public int Index;
    }

    /// <summary>
    /// One exterior wall face (the OSB sheathing face, or the stud face of an unsheathed wall) with every exterior layer
    /// built on it: house wrap + flashing, siding (vinyl / fiber cement / board-and-batten / brick veneer), trim and caulk.
    /// All meshes are children of this object, which sits at the face origin with the face rotation, so geometry is
    /// authored in face-local coordinates: x = u (along the wall), y = v (up = world height), z = -proud (into the wall).
    /// Get one with <see cref="For(FramePlane)"/> or <see cref="FromHit"/>.
    /// </summary>
    public class ExteriorWall : MonoBehaviour
    {
        public static readonly List<ExteriorWall> All = new List<ExteriorWall>();

        /// <summary>Outer framed face: sheathing face if sheathed, else stud face.</summary>
        public FramePlane Face;
        /// <summary>Stud face behind the sheathing (for openings and stud lines). Same u,v as <see cref="Face"/>.</summary>
        public FramePlane Studs;
        public float SheathT;
        /// <summary>Rough openings (u,v rects) — doors/windows.</summary>
        public readonly List<Rect> Openings = new List<Rect>();
        /// <summary>Stud center lines (u) for siding nails, ties, battens.</summary>
        public readonly List<float> StudLines = new List<float>();
        /// <summary>Wall depth behind the outer face (sheathing + stud), for jamb returns.</summary>
        public float Depth;

        public SidingKind Siding;
        public HouseWrap.State Wrap = new HouseWrap.State();
        public VinylSiding.State Vinyl;
        public FiberCement.State FC;
        public BoardBatten.State BB;
        public BrickVeneer.State Brick;
        public ExteriorTrim.State Trim = new ExteriorTrim.State();

        public Rect B => Face.Bounds;
        /// <summary>Local z of the weather-resistive barrier's outer surface (siding backs sit here).</summary>
        public float WrapZ => Wrap.Courses.Count > 0 ? -HouseWrap.Thickness * 2f : 0f;

        public Vector3 W(float u, float v, float proud = 0f) => Face.World(u, v, proud);
        public Vector3 L2W(Vector3 local) => transform.TransformPoint(local);

        // ------------------------------------------------------------------ lookup

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); }

        /// <summary>Exterior wall for a framed face (sheathing or studs). Creates it on first use.</summary>
        public static ExteriorWall For(FramePlane fp)
        {
            if (fp == null || !fp.IsWall) return null;
            All.RemoveAll(w => w == null);
            FramePlane face = fp, studs = null; float t = 0f;
            if (IsSheathing(fp)) { t = SheetDepth(fp); studs = StudsBehind(fp, t); }
            else
            {
                var sh = SheathOver(fp);
                if (sh != null) { face = sh; studs = fp; t = SheetDepth(sh); }
                else studs = fp;
            }
            foreach (var w in All) if (w.Face.SameFace(face)) return w;

            var go = new GameObject("ExteriorWall");
            go.transform.SetPositionAndRotation(face.Origin, face.Rotation);
            var ew = go.AddComponent<ExteriorWall>();
            ew.Face = face; ew.Studs = studs; ew.SheathT = t;
            ew.Refresh();
            All.Add(ew);
            return ew;
        }

        /// <summary>Wall under whatever the ray hit: an exterior layer (wrap, siding...), sheathing or studs.</summary>
        public static ExteriorWall FromHit(RaycastHit hit)
        {
            if (hit.collider == null) return null;
            var part = hit.collider.GetComponentInParent<ExtPart>();
            if (part != null && part.Wall != null) return part.Wall;
            var fl = hit.collider.GetComponentInParent<FinishLayer>();
            if (fl != null && fl.Owner is ExteriorWall ow) return ow;
            var fp = FinishAim.FaceBehind(hit);
            if (fp == null || !fp.IsWall) return null;
            // only faces pointing toward the shooter's side
            return For(fp);
        }

        /// <summary>Re-reads openings and stud lines from the framing (call after framing changes).</summary>
        public void Refresh()
        {
            Openings.Clear(); StudLines.Clear();
            var src = Studs ?? Face;
            Depth = SheathT;
            float d = 0f; foreach (var m in src.Members) d = Mathf.Max(d, m.Depth);
            Depth += Studs != null ? d : 0f;
            var raw = src.Openings();
            raw.RemoveAll(r => r.width < 0.25f || r.height < 0.25f);
            // merge vertically stacked pieces of the same opening
            raw.Sort((a, b) => a.yMin.CompareTo(b.yMin));
            foreach (var r in raw)
            {
                int i = Openings.FindIndex(o => Mathf.Abs(o.xMin - r.xMin) < 0.02f && Mathf.Abs(o.xMax - r.xMax) < 0.02f && Mathf.Abs(o.yMax - r.yMin) < 0.06f);
                if (i >= 0) Openings[i] = FramePlane.Union(Openings[i], r); else Openings.Add(r);
            }
            Openings.Sort((a, b) => a.xMin.CompareTo(b.xMin));
            foreach (var u in src.FramingLinesU()) if (StudLines.Count == 0 || u - StudLines[StudLines.Count - 1] > 0.02f) StudLines.Add(u);
        }

        static bool IsSheathing(FramePlane fp)
        {
            foreach (var m in fp.Members) if (m.Piece.Def.Category == PieceCategory.Sheet) return true;
            return false;
        }

        static float SheetDepth(FramePlane fp)
        {
            float t = 0f;
            foreach (var m in fp.Members) if (m.Piece.Def.Category == PieceCategory.Sheet) t = Mathf.Max(t, m.Depth);
            return t;
        }

        static bool FaceOf(Piece q, Vector3 n, out float outer, out float inner)
        {
            outer = inner = 0f;
            var t = q.transform; Vector3[] ax = { t.right, t.up, t.forward }; float[] hs = { q.Size.x * 0.5f, q.Size.y * 0.5f, q.Size.z * 0.5f };
            for (int i = 0; i < 3; i++)
                if (Mathf.Abs(Vector3.Dot(ax[i], n)) > 0.999f)
                {
                    float c = Vector3.Dot(q.transform.position, n);
                    outer = c + hs[i]; inner = c - hs[i];
                    return true;
                }
            return false;
        }

        static FramePlane StudsBehind(FramePlane sheath, float t)
        {
            float target = sheath.FaceOffset - t;
            var bb = sheath.Bounds; bb.xMin -= 0.05f; bb.xMax += 0.05f; bb.yMin -= 0.05f; bb.yMax += 0.05f;
            Piece best = null; bool bestVert = false;
            foreach (var q in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (q.Def == null || q.Def.Category != PieceCategory.Lumber) continue;
                if (FramePlane.ExcludeMember != null && FramePlane.ExcludeMember(q)) continue;
                if (!FaceOf(q, sheath.N, out float outer, out _)) continue;
                if (Mathf.Abs(outer - target) > 0.006f) continue;
                if (!bb.Contains(sheath.ToUV(q.transform.position))) continue;
                bool vert = Mathf.Abs(q.transform.forward.y) > 0.9f;
                if (best == null || (vert && !bestVert)) { best = q; bestVert = vert; }
            }
            return best != null ? FramePlane.FromPieceFace(best, sheath.N) : null;
        }

        static FramePlane SheathOver(FramePlane studs)
        {
            var bb = studs.Bounds;
            foreach (var q in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (q.Def == null || q.Def.Category != PieceCategory.Sheet) continue;
                if (!FaceOf(q, studs.N, out float outer, out float inner)) continue;
                if (Mathf.Abs(inner - studs.FaceOffset) > 0.006f || outer - inner > Units.In(1.2f)) continue;
                if (!bb.Overlaps(new Rect(studs.ToUV(q.transform.position) - Vector2.one * 0.3f, Vector2.one * 0.6f))) continue;
                return FramePlane.FromPieceFace(q, studs.N);
            }
            return null;
        }

        // ------------------------------------------------------------------ parts

        readonly Dictionary<string, Transform> _layers = new Dictionary<string, Transform>();

        /// <summary>Child container for one layer (cleared/rebuilt by the trades).</summary>
        public Transform Layer(string name)
        {
            if (_layers.TryGetValue(name, out var t) && t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            _layers[name] = go.transform;
            return go.transform;
        }

        public void ClearLayer(string name)
        {
            var t = Layer(name);
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>Adds a mesh part (face-local geometry) under a layer.</summary>
        public GameObject Part(string layer, string name, Mesh mesh, Material[] mats, string type, int index = 0, bool collider = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Layer(layer), false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            if (collider && mesh.vertexCount > 0) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            var fl = go.AddComponent<FinishLayer>(); fl.Kind = type; fl.Owner = this;
            var p = go.AddComponent<ExtPart>(); p.Wall = this; p.Type = type; p.Index = index;
            return go;
        }

        /// <summary>Opening index nearest a face point (or -1).</summary>
        public int OpeningNear(Vector2 uv, float maxDist = 1.5f)
        {
            int best = -1; float bd = maxDist;
            for (int i = 0; i < Openings.Count; i++)
            {
                var r = Openings[i];
                float dx = Mathf.Max(0f, Mathf.Max(r.xMin - uv.x, uv.x - r.xMax)), dy = Mathf.Max(0f, Mathf.Max(r.yMin - uv.y, uv.y - r.yMax));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= bd) { bd = d; best = i; }
            }
            return best;
        }

        public Vector2 ToUV(Vector3 world) => Face.ToUV(world);

        /// <summary>Total thickness of the exterior finish at uv (wrap + siding), for other trades.</summary>
        public float CoveringAt(Vector2 uv)
        {
            float t = -WrapZ;
            switch (Siding)
            {
                case SidingKind.Vinyl: t += Vinyl != null && Vinyl.Courses.Count > 0 ? Units.In(0.65f) : 0f; break;
                case SidingKind.FiberCement: t += FC != null && FC.Courses.Count > 0 ? Units.In(0.625f) : 0f; break;
                case SidingKind.BoardBatten: t += BB != null && BB.Panels ? Units.In(0.4375f) : 0f; break;
                case SidingKind.Brick: t += Brick != null && Brick.Courses > 0 ? BrickVeneer.AirSpace + BrickVeneer.BrickD : 0f; break;
            }
            return t;
        }

        static bool _hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterHooks()
        {
            if (_hooked) return; _hooked = true;
            FinishHooks.CoveringProviders.Add((fp, uv) =>
            {
                foreach (var w in All) if (w != null && w.Face.SameFace(fp)) return w.CoveringAt(uv);
                return 0f;
            });
        }
    }
}
