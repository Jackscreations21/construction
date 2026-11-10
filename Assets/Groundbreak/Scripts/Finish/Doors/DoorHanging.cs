using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// A prehung interior door set in a rough opening: the catalog door Piece (ProductModels draws slab, knob, jamb core)
    /// plus the full-depth split jambs, stops, hinges and cedar shim packs this trade adds. Local frame: x = Across
    /// (hinge → latch), y = up, z = Through (toward the stop side; the door swings toward -Through).
    /// </summary>
    public class HungDoor : MonoBehaviour
    {
        public static readonly List<HungDoor> All = new List<HungDoor>();

        public Piece Piece;
        public string DefId;
        public Vector3 Base;            // floor point at the center of the opening, on the finished wall's center plane
        public Vector3 Across, Through;
        public float SlabWidth, SlabThick, JambIn, HeadIn, JambWidth, ShimGap;
        public FramePlane FaceA, FaceB; // framing faces on each side (FaceA = the one it was hung from)
        public Rect RO;                 // rough opening on FaceA (u,v)
        public bool Shimmed, IsBarn, IsBifold;
        public float PlumbError;        // meters out of plumb over the door height
        public int JambNails, Shims;
        public readonly List<TrimPiece> Casings = new List<TrimPiece>();

        public Vector3 SwingSide => -Through;
        public float SlabSwingFaceZ => -JambWidth * 0.5f + DoorHanging.Setback;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public Vector3 Local(float x, float y, float z) => transform.TransformPoint(new Vector3(x, y, z));

        /// <summary>Anything (cabinet, wall, appliance) inside the door's 90° swing?</summary>
        public Collider SwingObstruction()
        {
            if (IsBarn || IsBifold) return null;
            float r = SlabWidth;
            var center = Local(0f, Units.In(40f), SlabSwingFaceZ - r * 0.5f - Units.In(0.5f));
            var half = new Vector3(JambIn - Units.In(0.3f), Units.In(38f), r * 0.5f - Units.In(0.3f));
            foreach (var c in Physics.OverlapBox(center, half, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.transform.IsChildOf(transform) || (Piece != null && c.transform.IsChildOf(Piece.transform))) continue;
                if (c.GetComponentInParent<PlayerRig>() != null) continue;
                var fl = c.GetComponentInParent<FinishLayer>();
                if (fl != null && ((fl.Kind ?? "").StartsWith("fin-trim") || (fl.Kind ?? "").StartsWith("fin-floor") || (fl.Kind ?? "").StartsWith("bead") || fl.Kind == "marker" || (fl.Kind ?? "").StartsWith("fin-caulk"))) continue;
                if (c.bounds.size.x > 20f || c.bounds.size.z > 20f) continue;
                return c;
            }
            return null;
        }
    }

    /// <summary>Hanging prehung interior doors in framed rough openings (RO = door width + 2", 82-1/2" tall).</summary>
    public static class DoorHanging
    {
        public static readonly float JambStd = Units.In(4.5625f);        // 4-9/16" jamb for 2x4 + 1/2" drywall both sides
        public static readonly float JambT = Units.In(0.625f);           // jamb stock (matches the product model core)
        public static readonly float Setback = Units.In(0.3125f);        // slab face behind the swing-side jamb edge
        static readonly float StopT = Units.In(0.4375f), StopW = Units.In(1.25f);

        /// <summary>Rough opening on the face that contains uv (null if none).</summary>
        public static Rect? OpeningAt(FramePlane fp, Vector2 uv)
        {
            foreach (var r in fp.Openings())
            {
                var e = r; e.xMin -= 0.02f; e.xMax += 0.02f; e.yMin -= 0.05f; e.yMax += 0.02f;
                if (e.Contains(uv)) return r;
            }
            return null;
        }

        /// <summary>In-game: hang from an aimed hit in (or at the edge of) a rough opening.</summary>
        public static HungDoor HangAt(RaycastHit hit, string doorId, bool swingTowardViewer = true)
        {
            var fp = FinishAim.FaceBehind(hit);
            if (fp == null || !fp.IsWall) { Game.Toast("Aim at a framed wall with a door rough opening"); return null; }
            var ro = OpeningAt(fp, fp.ToUV(hit.point));
            if (ro == null) { Game.Toast("Aim into the door rough opening"); return null; }
            return Hang(fp, ro.Value, doorId, swingTowardViewer);
        }

        /// <summary>
        /// Hangs a prehung interior door (catalog id, e.g. "door-int-6p32") in the rough opening <paramref name="ro"/> of wall face
        /// <paramref name="face"/>: cuts the bottom plate out of the opening, checks the RO (door + 2", ≥ 81-1/2" tall), centers the
        /// unit, shims it plumb/level (cedar shim packs at the hinges and strike) and nails through the jambs with the finish nailer.
        /// The door swings toward the face side when <paramref name="swingTowardFace"/>. shim=false leaves it out of plumb (inspection flag).
        /// </summary>
        public static HungDoor Hang(FramePlane face, Rect ro, string doorId, bool swingTowardFace = true, bool shim = true)
        {
            var def = Catalog.Get(doorId);
            if (def == null || def.Category != PieceCategory.Door) { Game.Toast("Not a door: " + doorId); return null; }
            string id = def.Id;
            if (id.Contains("ext") || id.Contains("slider")) { Game.Toast("That's an exterior door — this is for interior prehung doors"); return null; }
            if (id.Contains("pocket")) { Game.Toast("Pocket door kits need a pocket frame (2x the door width) — not supported here yet"); return null; }
            if (!FinishStock.Require("finish-nailer", "nail the jambs")) return null;
            if (!FinishStock.Require("level", "set the door plumb")) return null;
            if (!FinishStock.Has(doorId)) { FinishStock.Take(doorId); return null; }

            bool barn = id.Contains("barn"), bifold = id.Contains("bifold");
            float slabW = def.Size.x, slabT = def.Size.z;
            float floorV = face.Bounds.yMin;
            CutPlateInOpening(face, ro);
            ro.yMin = floorV;

            if (!barn)
            {
                float roW = ro.width, need = slabW + Units.In(2f);
                if (roW < slabW + Units.In(1.625f) || roW > slabW + Units.In(2.75f))
                { Game.Toast($"Rough opening is {Units.Format(roW)} wide — a {Units.Format(slabW)} prehung needs {Units.Format(need)} (door + 2\")"); return null; }
                if (ro.height < Units.In(81.5f) || ro.height > Units.In(84f))
                { Game.Toast($"Rough opening is {Units.Format(ro.height)} tall — interior prehungs need 82-1/2\""); return null; }
            }

            // wall build-up
            FramePlane back = null; float depth = 0f;
            foreach (var m in face.Members)
            {
                depth = Mathf.Max(depth, m.Depth);
                if (back == null && m.AlongV && Mathf.Abs(m.CenterU - ro.center.x) < ro.width) back = FramePlane.FromPieceFace(m.Piece, -face.N);
            }
            float c1 = FinishHooks.CoveringThickness(face, ro.center);
            float c2 = back != null ? FinishHooks.CoveringThickness(back, back.ToUV(face.World(ro.center, -depth))) : 0f;
            if (c1 <= 0f || c2 <= 0f) Game.Toast("Tip: hang drywall on both sides before setting interior doors — the 4-9/16\" jamb is sized for it");
            float jw = Mathf.Max(JambStd, depth + c1 + c2 + Units.In(0.0625f));

            var p0 = face.World(ro.center.x, floorV, 0f);
            float centerOff = (c1 - depth - c2) * 0.5f;
            Vector3 basePt = p0 + face.N * centerOff;
            Vector3 through = swingTowardFace ? -face.N : face.N;
            Vector3 across = Vector3.Cross(Vector3.up, through).normalized;

            var go = new GameObject("HungDoor_" + id);
            var hd = go.AddComponent<HungDoor>();
            go.AddComponent<FinishLayer>().Kind = "fin-door";
            hd.DefId = id; hd.FaceA = face; hd.FaceB = back; hd.RO = ro; hd.IsBarn = barn; hd.IsBifold = bifold;
            hd.Across = across; hd.Through = through; hd.SlabWidth = slabW; hd.SlabThick = slabT; hd.JambWidth = jw;
            hd.Base = basePt;
            hd.JambIn = slabW * 0.5f + Units.In(0.125f);
            hd.HeadIn = Units.In(80.625f);
            hd.ShimGap = (ro.width - (slabW + Units.In(1.5f))) * 0.5f;
            hd.Shimmed = shim;
            hd.PlumbError = shim ? 0f : Units.In(0.25f) * (Random.value < 0.5f ? -1f : 1f);
            Quaternion rot = Quaternion.LookRotation(through, Vector3.up);
            if (!shim) rot = Quaternion.AngleAxis(Mathf.Atan2(hd.PlumbError, Units.In(81f)) * Mathf.Rad2Deg, through) * rot;
            go.transform.SetPositionAndRotation(basePt, rot);

            FinishStock.Take(doorId);
            if (barn)
            {
                // surface-mounted slab + track over the opening, on the finished face
                var s = def.Size;
                Vector3 pos = face.World(ro.center.x, floorV + s.y * 0.5f, c1 + s.z * 0.5f + Units.In(0.25f));
                hd.Piece = Piece.Create(def, s.z, pos, Quaternion.LookRotation(face.N, Vector3.up));
                hd.Piece.Fasten();
                hd.HeadIn = ro.height;
                Game.Toast("Barn door track lagged into the header, slab hung");
                return hd;
            }

            // prehung unit: size so the product model's slab is the real slab (width + 2-7/8", 81-15/16" tall)
            float sx = slabW + Units.In(2.875f), sy = Units.In(81.9375f), sz = slabT / 0.72f;
            float zc = -jw * 0.5f + Setback + slabT * 0.5f;
            var piece = Piece.Create(def, sz, go.transform.TransformPoint(new Vector3(0f, sy * 0.5f, zc)), rot);
            piece.SetSize(new Vector3(sx, sy, sz));
            piece.Fasten();
            piece.transform.SetParent(go.transform, true);
            hd.Piece = piece;

            BuildFrame(hd, slabT, zc);
            int shimN = hd.Shims;
            FinishStock.Take("fin-shims");
            InteriorSupplies.UseFasteners("fin-nails-15ga", shimN * 2);
            hd.JambNails = shimN * 2;
            piece.Nails = hd.JambNails;
            Game.Toast(shim ? $"Door hung: shimmed plumb at {shimN} points, {hd.JambNails} nails through the jambs" : "Door set without shims — it's out of plumb");
            return hd;
        }

        static void BuildFrame(HungDoor hd, float slabT, float zc)
        {
            float jIn = hd.JambIn, head = hd.HeadIn, jw = hd.JambWidth, hz = jw * 0.5f, eps = 0.0004f;
            var mb = new MeshBuilder();
            int white = 0, nickel = 1, cedar = 2;
            mb.MinSubmeshCount = 3;
            // full-depth split jambs + head jamb
            Box(mb, -jIn - JambT, -jIn + eps, 0f, head + JambT, -hz, hz, white, Vector3.up);
            Box(mb, jIn - eps, jIn + JambT, 0f, head + JambT, -hz, hz, white, Vector3.up);
            Box(mb, -jIn, jIn, head - eps, head + JambT, -hz, hz, white, Vector3.right);
            if (!hd.IsBifold)
            {
                float z0 = zc + slabT * 0.5f + Units.In(0.03f), z1 = z0 + StopW;
                Box(mb, -jIn, -jIn + StopT, 0f, head, z0, z1, white, Vector3.up);
                Box(mb, jIn - StopT, jIn, 0f, head, z0, z1, white, Vector3.up);
                Box(mb, -jIn + StopT, jIn - StopT, head - StopT, head, z0, z1, white, Vector3.right);
                // hinges: barrel + pin caps on the swing side of the hinge jamb
                float zs = zc - slabT * 0.5f - Units.In(0.12f);
                foreach (float hy in new[] { Units.In(13.25f), Units.In(42.5f), Units.In(71.75f) })
                {
                    mb.AddCylinder(new Vector3(-jIn + Units.In(0.06f), hy, zs), Units.In(0.24f), Units.In(3.5f), 12, nickel);
                    mb.AddCylinder(new Vector3(-jIn + Units.In(0.06f), hy + Units.In(1.85f), zs), Units.In(0.17f), Units.In(0.2f), 10, nickel);
                    mb.AddCylinder(new Vector3(-jIn + Units.In(0.06f), hy - Units.In(1.85f), zs), Units.In(0.17f), Units.In(0.2f), 10, nickel);
                    Box(mb, -jIn - eps * 2f, -jIn + eps, hy - Units.In(1.75f), hy + Units.In(1.75f), zs + Units.In(0.1f), zs + Units.In(1.1f), nickel, Vector3.up);
                }
                // strike plate on the latch jamb
                Box(mb, jIn - eps * 2f, jIn + eps, Units.In(35f), Units.In(37.75f), zc - Units.In(0.45f), zc + Units.In(0.45f), nickel, Vector3.up);
            }
            // cedar shim packs (pairs of opposing wedges) in the gap between jamb and jack stud, snapped flush with the jamb edges
            float g = hd.ShimGap;
            int count = 0;
            if (hd.Shimmed && g > Units.In(0.06f))
            {
                foreach (int side in new[] { -1, 1 })
                {
                    float[] ys = side < 0 ? new[] { Units.In(13.25f), Units.In(42.5f), Units.In(71.75f) } : new[] { Units.In(13.25f), Units.In(36f), Units.In(71.75f) };
                    foreach (var y in ys) { ShimPack(mb, side, jIn + JambT, g, y, hz, cedar); count++; }
                }
            }
            hd.Shims = count;
            var go = new GameObject("JambsStopsShims");
            go.transform.SetParent(hd.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build("DoorFrame");
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { FinishMaterials.Get(InteriorMaterials.Primed), Materials.Get(SurfaceKind.Nickel), FinishMaterials.Get(InteriorMaterials.Cedar) };
            go.AddComponent<FinishLayer>().Kind = "fin-door";
        }

        static void ShimPack(MeshBuilder mb, int side, float xJamb, float g, float y, float hz, int sub)
        {
            float h = Units.In(1.5f);
            // in local (x, z): xIn = jamb back, xOut = RO edge (toward the jack stud)
            float xIn = side * xJamb, xOut = side * (xJamb + g);
            float thin = g * 0.12f;
            // wedge A thick at -z, wedge B thick at +z; slight seam between them
            var a = new[] { new Vector2(xIn, hz), new Vector2(xIn, -hz), new Vector2(xOut, -hz), new Vector2(xIn + side * thin, hz) };
            var b = new[] { new Vector2(xOut, -hz + 0.0008f), new Vector2(xOut, hz), new Vector2(xIn + side * (thin + 0.0006f), hz) };
            foreach (var poly in new[] { a, b })
            {
                var p = new Vector2[poly.Length];
                for (int i = 0; i < p.Length; i++) p[i] = new Vector2(poly[i].x, -poly[i].y); // local XY -> (x, -z) under Euler(-90,0,0)
                mb.AddExtrude(p, new Vector3(0f, y, 0f), Quaternion.Euler(-90f, 0f, 0f), h, sub);
            }
        }

        static void Box(MeshBuilder mb, float x0, float x1, float y0, float y1, float z0, float z1, int sub, Vector3 u)
            => mb.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), Quaternion.identity, sub, u);

        /// <summary>Cuts the bottom plate out of the door opening (reciprocating saw), splitting it into two plates.</summary>
        static void CutPlateInOpening(FramePlane fp, Rect ro)
        {
            foreach (var m in fp.Members.ToArray())
            {
                if (m.AlongV || m.Piece == null || m.Piece.Def.Category != PieceCategory.Lumber) continue;
                if (m.R.yMax > fp.Bounds.yMin + Units.In(2f) || m.R.xMin > ro.xMin + 0.01f || m.R.xMax < ro.xMax - 0.01f) continue;
                var p = m.Piece;
                var def = p.Def; var rot = p.transform.rotation; var size = p.Size;
                float vC = m.CenterV, dC = -m.Depth * 0.5f;
                void Part(float a, float b)
                {
                    if (b - a < Units.In(1f)) return;
                    var np = Piece.Create(def, b - a, fp.World((a + b) * 0.5f, vC, dC), rot);
                    np.SetSize(new Vector3(size.x, size.y, b - a));
                    np.Fasten();
                }
                Part(m.R.xMin, ro.xMin);
                Part(ro.xMax, m.R.xMax);
                p.gameObject.SetActive(false);
                Object.Destroy(p.gameObject);
                fp.Members.Remove(m);
            }
        }
    }
}
