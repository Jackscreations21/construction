using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Info left on a landing job's GameObject so stairs can be hung from it.</summary>
    public class StairLanding : MonoBehaviour
    {
        public float SizeM, HeightM;
        /// <summary>Outer ply of the front (stair-side) beam: the stairs' top riser / hanger board goes on its face.</summary>
        public StructuralMember FrontBeam;
        /// <summary>Ground point under the middle of the front beam's outer face.</summary>
        public Vector3 FrontFaceGround => transform.position + transform.rotation * new Vector3(0f, 0f, -SizeM * 0.5f);
    }

    /// <summary>Builds stairs and a small landing as FramingJobs in real order with real lumber.
    /// Stair frame: local +Z runs up the stairs, +X is to the right going up, Y up; origin = bottomCenter
    /// (on the floor, at the face of the first riser cut of the stringers).</summary>
    public static class StairBuilder
    {
        static float M(float inches) => inches * Units.Inch;
        static Vector3 P(float xIn, float yIn, float zIn) => new Vector3(M(xIn), M(yIn), M(zIn));
        const float Gap = 0.0005f; // keep ghost boxes from z-fighting
        const float Galv = 0.05f;  // 18 ga connector steel, inches

        /// <summary>Allocates cut pieces from stock boards; Take returns how many new boards the cut opens (0 or 1).</summary>
        class Cutter
        {
            readonly float _stock; float _left;
            public Cutter(float stockIn) { _stock = stockIn; }
            public int Take(float lenIn)
            {
                const float kerf = 0.125f;
                if (lenIn + kerf <= _left) { _left -= lenIn + kerf; return 0; }
                _left = _stock - lenIn - kerf;
                return 1;
            }
        }

        static Func<(Vector3, Vector3, Quaternion)> Ghost(Transform t, Vector3 localCenter, Vector3 size) =>
            () => (t.TransformPoint(localCenter), size + Vector3.one * Gap, t.rotation);

        /// <summary>Where the bottom of a stair must start so its top riser lands on a rim face (ground point under the face center).</summary>
        public static Vector3 BottomFor(Vector3 rimFaceGround, float yaw, float totalRiseM, float unitRunIn = StairMath.DefaultRunIn)
        {
            var r = StairMath.Calculate(Units.ToInches(totalRiseM), unitRunIn);
            return rimFaceGround - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * M(r.Treads * r.UnitRunIn);
        }

        // ================================================================== stairs

        /// <summary>Straight stair: 3 stringers cut from 2x12 (PT 12' if it fits), hung from a 3/4" hanger board on the landing rim
        /// with galvanized stringer connectors (when supportTop is given), 1x8 risers, two PT 2x6 per tread (1/4" gap, 1" nosing).</summary>
        public static FramingJob Stairs(Vector3 bottomCenter, float yaw, float totalRiseM, float widthM = 0.9144f, Transform supportTop = null)
        {
            var r = StairMath.Calculate(Units.ToInches(totalRiseM));
            var lay = new StringerLayout(r);
            int n = r.Risers;
            float R = r.UnitRiseIn, T = r.UnitRunIn, t = r.TreadThicknessIn, W = Mathf.Max(24f, Units.ToInches(widthM));
            float H = r.TotalRiseIn;

            var job = FramingJob.Create("Stairs (" + n + " risers @ " + StairMath.FormatIn(R) + ")");
            var root = job.transform;
            root.SetPositionAndRotation(bottomCenter, Quaternion.Euler(0f, yaw, 0f));
            if (!r.Valid || n < 2) { Debug.LogWarning("[StairBuilder] rise too small for a stair: " + H + "\""); return job; }

            string stock = r.StockId;
            float[] lats = { -(W * 0.5f - 0.75f), 0f, W * 0.5f - 0.75f };
            string[] names = { "left", "center", "right" };
            float topSeat = (n - 1) * R - t;
            var stringerBox = new Vector3(M(1.5f), M(topSeat), M(lay.XTop));
            Vector3 StringerGhostC(float lat) => P(lat, topSeat * 0.5f, lay.XTop * 0.5f);

            // 1. layout + cuts (framing square, then saw; the first stringer is the template for the others)
            job.Add("Lay out stringer: " + StairMath.FormatIn(R) + " rise / " + StairMath.FormatIn(T) + " run on the framing square", ToolSets.Layout, null,
                    Ghost(root, StringerGhostC(lats[0]), stringerBox));
            job.Add("Cut stringer #1 from " + Framer.Def(stock).Name, ToolSets.Saw, null, Ghost(root, StringerGhostC(lats[0]), stringerBox)).Use(stock, 1);
            job.Add("Cut stringers #2 and #3 using #1 as a template", ToolSets.Saw, null, Ghost(root, StringerGhostC(lats[1]), stringerBox)).Use(stock, 2);

            // 2. hanger board (3/4" ply strip) on the landing rim: it is the top riser and what the stringers hang from
            float xTop = lay.XTop;
            float yBot = lay.FootOnBottomEdge ? lay.BottomEdgeY(xTop) : 0f;
            if (supportTop != null)
            {
                float boardLen = W, boardLat = 0f, boardTop = H - 0.71875f;
                var col = supportTop.GetComponent<BoxCollider>();
                if (col != null)
                {
                    // supporting rim/beam: its long axis gives the board length, its top the board top
                    Vector3 axis = supportTop.rotation * Vector3.forward;
                    boardLen = Mathf.Max(W, Units.ToInches(col.size.z) * Mathf.Abs(Vector3.Dot(axis, root.right)));
                    boardLat = Units.ToInches(Vector3.Dot(supportTop.TransformPoint(col.center) - bottomCenter, root.right));
                    // (collider.bounds can be stale in the frame the rim was built: compute the top from the transform)
                    Vector3 hs = col.size * 0.5f;
                    float ext = Mathf.Abs((supportTop.rotation * Vector3.right).y) * hs.x + Mathf.Abs((supportTop.rotation * Vector3.up).y) * hs.y
                              + Mathf.Abs((supportTop.rotation * Vector3.forward).y) * hs.z;
                    boardTop = Units.ToInches(supportTop.TransformPoint(col.center).y + ext - bottomCenter.y);
                }
                float boardBot = Mathf.Max(0.5f, yBot - 0.75f);
                float hb = boardTop - boardBot;
                var hbC = P(boardLat, (boardTop + boardBot) * 0.5f, xTop + 0.75f - 0.359375f); // tight to the rim face
                job.Add("Nail 3/4\" hanger board to the landing rim (top riser)", ToolSets.Fasten,
                        () => Framer.Sheet("subfloor34", M(hb), M(boardLen), root, hbC, Vector3.right, Vector3.back, MemberRole.Ledger, Connector.Nailed, "stairs").name = "Stair hanger board (3/4\" ply)",
                        Ghost(root, hbC, new Vector3(M(boardLen), M(hb), M(0.71875f)))).Use("subfloor34", 1).Nails = 12;
            }

            // 3. set the stringers
            for (int i = 0; i < 3; i++)
            {
                float lat = lats[i];
                int side = i == 0 ? 1 : i == 2 ? -1 : 1; // connector on the inside face of the outer stringers
                bool hang = supportTop != null;
                var s = job.Add("Set " + names[i] + " stringer" + (hang ? " (galvanized stringer connector to hanger board)" : ""), ToolSets.Fasten, () =>
                {
                    var m = MakeStringer(lay, stock, root, lat, "stairs");
                    if (hang)
                    {
                        m.Connectors |= Connector.Hanger;
                        StringerConnector(m, root, lat, side, xTop, yBot, topSeat);
                    }
                }, Ghost(root, StringerGhostC(lat), stringerBox));
                s.Nails = hang ? 10 : 4;
            }

            // 4. risers bottom-up (the top riser is the hanger board / rim), 1x8 ripped to the riser height
            var riserCut = new Cutter(96f);
            for (int k = 1; k <= n - 1; k++)
            {
                float y0 = k == 1 ? 0f : (k - 1) * R - t;
                float y1 = k * R - t;
                float hgt = Mathf.Min(y1 - y0, 7.25f);
                var c = P(0f, y0 + hgt * 0.5f, (k - 1) * T - 0.375f);
                int kk = k;
                job.Add("Nail riser " + k + " (1x8 ripped to " + StairMath.FormatIn(hgt) + ")", ToolSets.Fasten,
                        () => Framer.Sheet("1x8x8", M(hgt), M(W), root, c, Vector3.right, Vector3.back, MemberRole.Riser, Connector.Nailed, "stairs").name = "Riser " + kk,
                        Ghost(root, c, new Vector3(M(W), M(hgt), M(0.75f)))).Use("1x8x8", riserCut.Take(W)).Nails = 6;
            }

            // 5. treads bottom-up: back 2x6 tight to the riser above, 1/4" gap, front 2x6 with 1" nosing
            var treadCut = new Cutter(192f);
            for (int k = 1; k <= n - 1; k++)
            {
                float back = k * T - 0.75f;
                foreach (var (label, z1) in new[] { ("back", back), ("front", back - 5.75f) })
                {
                    var c = P(0f, k * R - 0.75f, z1 - 2.75f);
                    int kk = k;
                    job.Add("Tread " + k + ": nail " + label + " PT 2x6 (" + StairMath.FormatIn(W) + ")", ToolSets.Fasten,
                            () => Framer.Make("pt2x6x16", M(W), root, c, Vector3.right, Vector3.up, MemberRole.Tread, Connector.Nailed, "stairs").name = "Tread " + kk + " " + label,
                            Ghost(root, c, new Vector3(M(W), M(1.5f), M(5.5f)))).Use("pt2x6x16", treadCut.Take(W)).Nails = 6;
                }
            }
            return job;
        }

        /// <summary>Installed stringer. The collider is the solid "throat" strip under the notches (so the player walks on the treads, not on
        /// an invisible slab) plus a thin contact box under each seat cut so the treads/risers find their support.</summary>
        public static StructuralMember MakeStringer(StringerLayout lay, string defId, Transform parent, float latIn, string group)
        {
            var r = lay.R;
            int n = r.Risers;
            float W = r.BoardWidthIn;
            float uC = (lay.UMin + lay.UMax) * 0.5f;

            // collider box in board coordinates (x across, z along)
            float xLo, xHi, zLo, zHi;
            if (lay.FootOnBottomEdge)
            {
                xLo = -W * 0.5f; xHi = -W * 0.5f + r.ThroatIn;
                zLo = Vector2.Dot(new Vector2(lay.X0, 0f), lay.U) - uC;
                zHi = Vector2.Dot(new Vector2(lay.XTop, lay.BottomEdgeY(lay.XTop)), lay.U) - uC;
            }
            else
            {
                xLo = zLo = float.MaxValue; xHi = zHi = float.MinValue;
                foreach (var p in lay.Profile)
                {
                    var q = lay.ToBoard(p, uC);
                    xLo = Mathf.Min(xLo, q.x); xHi = Mathf.Max(xHi, q.x); zLo = Mathf.Min(zLo, q.y); zHi = Mathf.Max(zHi, q.y);
                }
            }
            float xc = (xLo + xHi) * 0.5f, zc = (zLo + zHi) * 0.5f;
            Vector2 c2 = lay.FromBoard(xc, zc, uC);
            Vector3 center = P(latIn, c2.y, c2.x);
            Vector3 u3 = new Vector3(0f, lay.U.y, lay.U.x);
            Quaternion rot = Quaternion.LookRotation(u3, Vector3.left); // piece X = board "across" (V), Z = along (U)
            Vector3 size = new Vector3(M(xHi - xLo), M(1.5f), M(zHi - zLo));

            Vector3 Local(Vector2 p) { var q = lay.ToBoard(p, uC); return new Vector3(M(q.x - xc), 0f, M(q.y - zc)); }
            var def = Framer.Def(defId);
            var m = Framer.Custom(defId, size, parent, center, rot, MemberRole.Stringer,
                (b, s) => StringerMesh.Solid(b, lay, Local, Vector3.up, M(1.5f), Vector3.forward, s(Materials.ForDefinition(def)), s(SurfaceKind.PineLumber)),
                Connector.Nailed, group);
            m.gameObject.name = "Stringer (" + def.Name + ")";
            StringerMesh.RemoveStockVisuals(m.Piece);
            m.gameObject.AddComponent<CutStringer>().Result = r;

            // seat contact boxes (axis-aligned in the stair frame, inside the wood just under each seat cut)
            var inv = Quaternion.Inverse(rot);
            for (int k = 1; k <= n - 1; k++)
            {
                float x0 = (k - 1) * r.UnitRunIn, x1 = Mathf.Min(k * r.UnitRunIn, lay.XTop);
                float y1 = k * r.UnitRiseIn - r.TreadThicknessIn, y0 = y1 - 1f;
                var go = new GameObject("SeatContact " + k);
                go.transform.SetParent(m.transform, false);
                go.transform.localRotation = inv;
                go.transform.localPosition = inv * (P(latIn, (y0 + y1) * 0.5f, (x0 + x1) * 0.5f) - center);
                var bc = go.AddComponent<BoxCollider>();
                bc.size = P(1.5f, y1 - y0, x1 - x0);
            }
            return m;
        }

        /// <summary>Galvanized adjustable stringer connector: a plate on the stringer side and a flange nailed to the hanger board.</summary>
        static void StringerConnector(StructuralMember stringer, Transform root, float lat, int side, float xTop, float yBot, float topSeat)
        {
            float y0 = yBot + 0.5f, y1 = Mathf.Min(topSeat - 0.5f, y0 + 9f);
            float face = lat + side * 0.75f;
            AttachHardware(stringer.transform, root, "Stringer connector", Vector3.zero, Quaternion.identity, (b, g) =>
            {
                // plate on the stringer's side face
                b.AddBox(P(face + side * Galv * 0.5f, (y0 + y1) * 0.5f, xTop - 1.5f), P(Galv, y1 - y0, 3f), g);
                // flange on the hanger board, running away from the stringer
                b.AddBox(P(face + side * 0.8f, (y0 + y1) * 0.5f, xTop - Galv * 0.5f), P(1.6f, y1 - y0, Galv), g);
                NailHeads(b, g, new Vector3(1, 0, 0) * side, P(face + side * Galv, 0, 0), y0, y1, xTop - 2.4f, xTop - 0.6f, true);
            });
        }

        static void NailHeads(MeshBuilder b, int g, Vector3 normal, Vector3 planeOffset, float y0, float y1, float z0, float z1, bool sideFace)
        {
            // a few 10d nail heads punched through the plate
            for (float y = y0 + 1f; y < y1 - 0.5f; y += 2f)
                for (float z = z0; z <= z1 + 1e-3f; z += (z1 - z0))
                {
                    var c = new Vector3(planeOffset.x, M(y), M(z)) + normal * M(0.03f);
                    b.AddCylinder(c, Quaternion.FromToRotation(Vector3.up, normal), M(0.14f), M(0.06f), 8, g);
                }
        }

        /// <summary>Hardware mesh built in 'frame' local coordinates, parented to a member (moves/removes with it).</summary>
        static GameObject AttachHardware(Transform member, Transform frame, string name, Vector3 frameLocalOrigin, Quaternion frameLocalRot, Action<MeshBuilder, int> build)
        {
            Vector3 worldPos = frame.TransformPoint(frameLocalOrigin);
            Quaternion worldRot = frame.rotation * frameLocalRot;
            return Framer.Hardware(member, name, member.InverseTransformPoint(worldPos), Quaternion.Inverse(member.rotation) * worldRot, build);
        }

        // ================================================================== landing

        /// <summary>Small square landing: 4 PT 4x4 posts on the ground, doubled 2x8 flush beams front and back on the posts,
        /// 2x8 side rims and joists at 16" O.C. in galvanized joist hangers (concealed-flange at the outer rims), 3/4" T&amp;G subfloor
        /// glued and nailed. heightM = top of the deck. Local frame: stairs arrive from -Z (front beam at z = -size/2).</summary>
        public static FramingJob Landing(Vector3 center, float yaw, float sizeM, float heightM)
        {
            float S = Units.ToInches(sizeM), H = Units.ToInches(heightM);
            const float sub = 0.71875f, depth = 7.25f;
            float jTop = H - sub, jBot = jTop - depth, postH = jBot, jMid = (jTop + jBot) * 0.5f;
            var job = FramingJob.Create("Stair landing " + StairMath.FormatFtIn(S) + " x " + StairMath.FormatFtIn(S));
            var root = job.transform;
            root.SetPositionAndRotation(center, Quaternion.Euler(0f, yaw, 0f));
            var info = job.gameObject.AddComponent<StairLanding>();
            info.SizeM = sizeM; info.HeightM = heightM;
            if (postH < 6f) { Debug.LogWarning("[StairBuilder] landing too low for posts + 2x8 framing"); return job; }

            // 1. posts
            int perBoard = Mathf.Max(1, Mathf.FloorToInt(120f / (postH + 0.125f)));
            job.Add("Cut 4 PT 4x4 posts to " + StairMath.FormatFtIn(postH), ToolSets.Saw, null).Use("pt4x4x10", Mathf.CeilToInt(4f / perBoard));
            float pc = S * 0.5f - 1.75f;
            int pi = 0;
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var c = P(sx * pc, postH * 0.5f, sz * pc);
                pi++;
                int idx = pi;
                job.Add("Set & plumb post " + idx, ToolSets.Level,
                        () => Framer.Make("pt4x4x10", M(postH), root, c, Vector3.up, Vector3.forward, MemberRole.Post, Connector.Nailed, "landing").name = "Post " + idx,
                        Ghost(root, c, P(3.5f, postH, 3.5f)));
            }

            // 2. doubled 2x8 beams across the post tops (front = stair side)
            var c2x8 = new Cutter(144f);
            foreach (var (label, sz) in new[] { ("front", -1), ("back", 1) })
            {
                for (int ply = 0; ply < 2; ply++)
                {
                    float z = sz * (S * 0.5f - 0.75f - (1 - ply) * 1.5f); // inner ply first, then the outer ply
                    var c = P(0f, jMid, z);
                    bool outerFront = sz < 0 && ply == 1;
                    int plyN = ply + 1;
                    var s = job.Add((ply == 0 ? "Set " + label + " beam ply 1 on the posts" : "Laminate " + label + " beam ply 2 (16d @ 16\" staggered)"), ToolSets.Fasten, () =>
                    {
                        var m = Framer.Make("2x8x12", M(S), root, c, Vector3.right, Vector3.forward, MemberRole.Beam, Connector.Nailed, "landing");
                        m.name = label + " beam ply " + plyN;
                        if (outerFront) info.FrontBeam = m;
                    }, Ghost(root, c, P(S, depth, 1.5f)));
                    s.Use("2x8x12", c2x8.Take(S));
                    s.Nails = ply == 0 ? 4 : 10;
                }
            }

            // 3. rims + joists at 16" O.C. in hangers between the beams
            float span = S - 6f;
            var xs = new List<(float x, bool rim)> { (-(S * 0.5f - 0.75f), true) };
            for (float x = -S * 0.5f + 16f; x < S * 0.5f - 1.5f - 2f; x += 16f) xs.Add((x, false));
            xs.Add((S * 0.5f - 0.75f, true));
            foreach (var (x, rim) in xs)
            {
                var c = P(x, jMid, 0f);
                int outward = rim ? (x < 0 ? -1 : 1) : 0;
                var s = job.Add((rim ? "Hang side rim" : "Hang joist") + " in LUS28 hangers at " + StairMath.FormatIn(x + S * 0.5f), ToolSets.Fasten, () =>
                {
                    var m = Framer.Make("2x8x12", M(span), root, c, Vector3.forward, Vector3.right, rim ? MemberRole.RimJoist : MemberRole.Joist, Connector.Nailed | Connector.Hanger, "landing");
                    m.name = (rim ? "Side rim" : "Joist") + " @ " + StairMath.FormatIn(x + S * 0.5f);
                    JoistHanger(m, root, x, jBot, -span * 0.5f, +1, outward);
                    JoistHanger(m, root, x, jBot, span * 0.5f, -1, outward);
                }, Ghost(root, c, P(1.5f, depth, span)));
                s.Use("2x8x12", c2x8.Take(span)).Use("hanger-2x8", 2);
                s.Nails = 12;
            }

            // 4. subfloor: 4x8 sheets, long way across the joists, glued + nailed
            var sheetCut = new Cutter(96f);
            for (float z0 = -S * 0.5f; z0 < S * 0.5f - 0.01f; z0 += 48f)
                for (float x0 = -S * 0.5f; x0 < S * 0.5f - 0.01f; x0 += 96f)
                {
                    float w = Mathf.Min(48f, S * 0.5f - z0), l = Mathf.Min(96f, S * 0.5f - x0);
                    var c = P(x0 + l * 0.5f, H - sub * 0.5f, z0 + w * 0.5f);
                    job.Add("Glue & nail 3/4\" T&G subfloor (" + StairMath.FormatFtIn(l) + " x " + StairMath.FormatFtIn(w) + ")", ToolSets.Fasten,
                            () => Framer.Sheet("subfloor34", M(w), M(l), root, c, Vector3.right, Vector3.up, MemberRole.Subfloor, Connector.Nailed | Connector.Adhesive, "landing").name = "Landing subfloor",
                            Ghost(root, c, P(l, sub, w))).Use("subfloor34", sheetCut.Take(l)).Use("adhesive", 1).Nails = 16;
                }
            return job;
        }

        /// <summary>LUS28-style face-mount joist hanger at a joist end. faceZ = header face (landing local inches), dir = +1 if the joist
        /// runs toward +Z from that face. outward != 0: concealed flange on that side (outer rim flush with the beam end).</summary>
        static void JoistHanger(StructuralMember joist, Transform root, float x, float yBot, float faceZ, int dir, int outward)
        {
            const float inner = 0.78f, sideH = 6.25f, deep = 1.75f, flange = 1.1f;
            AttachHardware(joist.transform, root, "Joist hanger", Vector3.zero, Quaternion.identity, (b, g) =>
            {
                float zSeat = faceZ + dir * deep * 0.5f;
                // seat under the joist
                b.AddBox(P(x, yBot - Galv * 0.5f, zSeat), P(2f * (inner + Galv), Galv, deep), g);
                foreach (int sx in new[] { -1, 1 })
                {
                    // side wings hugging the joist
                    b.AddBox(P(x + sx * (inner + Galv * 0.5f), yBot + sideH * 0.5f - Galv, zSeat), P(Galv, sideH, deep), g);
                    // nailing flange on the beam face (outer rims flush with the beam end use a concealed-flange hanger: no outside flange)
                    if (sx == outward) continue;
                    b.AddBox(P(x + sx * (inner + Galv + flange * 0.5f), yBot + sideH * 0.5f - Galv, faceZ + dir * Galv * 0.5f), P(flange, sideH - 0.4f, Galv), g);
                    for (float y = yBot + 1.2f; y < yBot + sideH - 0.6f; y += 1.6f)
                        b.AddCylinder(P(x + sx * (inner + Galv + flange * 0.55f), y, faceZ + dir * Galv * 1.2f), Quaternion.FromToRotation(Vector3.up, Vector3.forward * dir), M(0.14f), M(0.06f), 8, g);
                }
            });
        }
    }
}
