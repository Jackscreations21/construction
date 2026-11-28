using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Left on a stair job built by <see cref="StairBuilder.Stairs"/>: the layout and width, so the framing inspection can
    /// check the stair (IRC R311.7: width, handrail on 4+ risers). Local frame = the stair frame (+Z up the stairs, +X right).</summary>
    public class StairAssembly : MonoBehaviour
    {
        public StairResult Result;
        public float WidthIn;
        /// <summary>Front edge of a tread (z of its nosing) is k * run - NosingBackIn (riser board + tread depth).</summary>
        public float NosingBackIn => Result.RiserThicknessIn + Result.TreadDepthIn;
        /// <summary>Height (inches above the bottom floor) of the nosing line at run position z (inches, stair frame).</summary>
        public float NosingLineY(float zIn) => Result.UnitRiseIn / Result.UnitRunIn * (zIn + NosingBackIn);
        public static readonly List<StairAssembly> All = new List<StairAssembly>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);
    }

    /// <summary>Stair handrail (IRC 2021 R311.7.8): PT 4x4 posts bolted to the outside of the right stringer (two 1/2" carriage
    /// bolts each, 4' O.C. max so every bracket has a post), a galvanized handrail bracket on each post's inside face and a
    /// 1 5/8" round PT handrail screwed to the brackets: top 36" plumb above the nosing line, 1 11/16" clear of the posts,
    /// running from above the bottom riser to above the top riser. Catalog, structural rules and the inspection check live here.</summary>
    public static partial class StairBuilder
    {
        public const float RailDiaIn = 1.625f;
        public const float RailOffsetIn = 2.5f;      // post face to rail center (bracket reach): 1 11/16" clear
        public const float PostMaxSpacingIn = 48f;   // handrail brackets 4' O.C. max
        public static readonly string[] RailIds = { "pt-rail-8", "pt-rail-12", "pt-rail-16" };
        public const string BracketId = "rail-bracket", BoltId = "carriage-bolt-12";
        static readonly string[] RailTools = { "fin-drill", "drill", "nailer", "hammer" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterHandrail()
        {
            var pt = new Color(0.55f, 0.62f, 0.42f);
            PieceDefinition Rail(string id, int ft, int price) => new PieceDefinition
            {
                Id = id, Name = "PT handrail, 1 5/8\" round x " + ft + "'", Category = PieceCategory.Lumber,
                Size = new Vector3(Units.In(RailDiaIn), Units.In(RailDiaIn), Units.In(ft * 12f)), Price = price, Cuttable = true, Color = pt,
            };
            Catalog.Register(
                Rail("pt-rail-8", 8, 19), Rail("pt-rail-12", 12, 27), Rail("pt-rail-16", 16, 36),
                new PieceDefinition { Id = BracketId, Name = "Handrail bracket, galvanized, with screws", Category = PieceCategory.Consumable, Price = 6, UnitsPerPurchase = 1, Color = new Color(0.72f, 0.74f, 0.76f) },
                new PieceDefinition { Id = BoltId, Name = "1/2\" x 7\" galvanized carriage bolt, washer + nut", Category = PieceCategory.Consumable, Price = 2, UnitsPerPurchase = 1, Color = new Color(0.72f, 0.74f, 0.76f) });

            // a post bolted to a stringer is carried by it; the handrail hangs on brackets screwed to posts / studs
            var post = StructuralRules.Get(MemberRole.Post);
            if (System.Array.IndexOf(post.SupportedBy, MemberRole.Stringer) < 0)
            {
                var sup = new List<MemberRole>(post.SupportedBy) { MemberRole.Stringer };
                StructuralRules.Set(new RoleRule { Role = MemberRole.Post, Foundation = post.Foundation, Requires = post.Requires, MaxOCInches = post.MaxOCInches, Allow24OC = post.Allow24OC, SupportedBy = sup.ToArray(), Note = post.Note });
            }
            StructuralRules.Set(new RoleRule
            {
                Role = MemberRole.Handrail, Requires = Connector.Nailed,
                SupportedBy = new[] { MemberRole.Post, MemberRole.Stud, MemberRole.KingStud, MemberRole.Stringer, MemberRole.Blocking },
                Note = "handrail 34\"-38\" above the nosings, 1 1/4\"-2\" grip, on brackets 4' O.C. max",
            });
            if (!FramingInspection.Checks.Contains(StairCheck)) FramingInspection.Checks.Add(StairCheck);
            if (!ProductModels.Overrides.Contains(RailModel)) ProductModels.Overrides.Add(RailModel);
        }

        /// <summary>Loose handrail stock (delivery piles, the player's hands) is round, not a square stick.</summary>
        static bool RailModel(PieceDefinition d, Vector3 s, MeshBuilder b, System.Func<Material, int> mat)
        {
            if (d == null || d.Id == null || !d.Id.StartsWith("pt-rail")) return false;
            b.AddRod(Vector3.zero, Quaternion.identity, Mathf.Min(s.x, s.y) * 0.5f, s.z, 24, mat(Materials.Get(SurfaceKind.TreatedLumber)));
            return true;
        }

        /// <summary>Smallest handrail stock that covers a length (inches), or null when even 16' is too short.</summary>
        public static string RailStockFor(float lenIn)
        {
            foreach (var id in RailIds) { var d = Catalog.Get(id); if (d != null && Units.ToInches(d.Size.z) >= lenIn + 0.5f) return id; }
            return null;
        }

        /// <summary>Handrail steps for a stair job (stair frame inches; right side going up).</summary>
        static void AddHandrail(FramingJob job, StairAssembly asm, StringerLayout lay, float W)
        {
            var r = asm.Result;
            var root = job.transform;
            float R = r.UnitRiseIn, T = r.UnitRunIn;
            float hyp = Mathf.Sqrt(R * R + T * T), cos = T / hyp;
            float rr = RailDiaIn * 0.5f;
            float face = W * 0.5f;                       // outside face of the right stringer = inside face of the posts
            float xRail = face - RailOffsetIn;
            float xPost = face + 1.75f;
            float xTop = lay.XTop;
            float RailTopY(float z) => asm.NosingLineY(z) + StairMath.HandrailHeightIn;
            float RailBottomY(float z) => RailTopY(z) - 2f * rr / cos;

            // posts: first just behind the bottom riser, last just below the top riser, 4' O.C. max between
            float z0 = 1.75f, z1 = xTop - 1.75f;
            int count = Mathf.Max(2, Mathf.CeilToInt((z1 - z0) / PostMaxSpacingIn - 1e-3f) + 1);
            var posts = new List<float>();
            for (int i = 0; i < count; i++) posts.Add(Mathf.Lerp(z0, z1, i / (float)(count - 1)));

            var postCut = new Cutter(120f);
            var lengths = new List<(float zc, float y0, float y1)>();
            foreach (float zc in posts)
            {
                float y0 = lay.FootOnBottomEdge ? Mathf.Max(0f, lay.BottomEdgeY(zc - 1.75f)) : 0f;
                float y1 = RailTopY(zc) + 2f;
                lengths.Add((zc, y0, y1));
            }
            int boards = 0;
            foreach (var (_, y0, y1) in lengths) boards += postCut.Take(y1 - y0);
            job.Add("Handrail: cut " + count + " PT 4x4 posts (bottom post on grade, the rest bolted to the right stringer)", ToolSets.Saw, null).Use("pt4x4x10", boards);

            int idx = 0;
            foreach (var (zc, y0, y1) in lengths)
            {
                idx++;
                int k = idx;
                float len = y1 - y0;
                var c = P(xPost, (y0 + y1) * 0.5f, zc);
                float yb = Mathf.Max(0f, lay.FootOnBottomEdge ? lay.BottomEdgeY(zc) : 0f);
                float bracketY = RailBottomY(zc);
                var s = job.Add("Handrail: bolt post " + k + " to the right stringer (two 1/2\" carriage bolts), screw a rail bracket to its inside face", RailTools, () =>
                {
                    var m = Framer.Make("pt4x4x10", M(len), root, c, Vector3.up, Vector3.right, MemberRole.Post, Connector.Nailed | Connector.AnchorBolt, "stairs");
                    m.name = "Handrail post " + k;
                    CarriageBolts(m.transform, root, face, zc, yb);
                    RailBracket(m.transform, root, face, xRail, zc, bracketY);
                }, Ghost(root, c, P(3.5f, len, 3.5f)));
                s.Use(BoltId, 2).Use(BracketId, 1);
            }

            // the rail: from above the face of the bottom riser to above the top riser (hanger board), parallel to the nosing line
            float zA = -r.RiserThicknessIn - 1f, zB = xTop + 1f;
            float railLen = (zB - zA) / cos;
            string stock = RailStockFor(railLen);
            if (stock == null) { Debug.LogWarning("[StairBuilder] handrail longer than 16' stock: " + railLen + "\""); return; }
            float zc2 = (zA + zB) * 0.5f;
            float yc = RailTopY(zc2) - rr / cos;
            var rc = P(xRail, yc, zc2);
            Vector3 along = new Vector3(0f, R, T).normalized, up = new Vector3(0f, T, -R).normalized;
            Quaternion rot = Quaternion.LookRotation(along, up);
            Vector3 size = P(RailDiaIn, RailDiaIn, railLen);
            var def = Framer.Def(stock);
            job.Add("Handrail: cut the " + def.Name + " to " + StairMath.FormatFtIn(railLen) + ", set it on the brackets " + StairMath.FormatIn(StairMath.HandrailHeightIn)
                    + " above the nosings and screw it down", RailTools, () =>
            {
                var m = Framer.Custom(stock, size, root, rc, rot, MemberRole.Handrail, (b, sub) =>
                {
                    int g = sub(Materials.ForDefinition(def));
                    b.AddRod(Vector3.zero, Quaternion.identity, M(rr), M(railLen), 24, g);
                    // eased ends: a small chamfer ring at each end reads as a sanded, rounded end
                    b.AddFrustum(new Vector3(0f, 0f, M(railLen * 0.5f) + M(0.1f)), Quaternion.FromToRotation(Vector3.up, Vector3.forward), M(rr), M(rr - 0.18f), M(0.2f), 24, g);
                    b.AddFrustum(new Vector3(0f, 0f, -M(railLen * 0.5f) - M(0.1f)), Quaternion.FromToRotation(Vector3.up, Vector3.back), M(rr), M(rr - 0.18f), M(0.2f), 24, g);
                }, Connector.Nailed, "stairs");
                m.gameObject.name = "Stair handrail (" + def.Name + ")";
                StringerMesh.RemoveStockVisuals(m.Piece);
            }, Ghost(root, rc, size)).Use(stock, 1).Nails = 2 * count;
            job.Steps[job.Steps.Count - 1].Ghost = () => (root.TransformPoint(rc), size + Vector3.one * Gap, root.rotation * rot);
        }

        /// <summary>Two carriage bolt heads on the post's outside face and washers + nuts on the inside of the stringer.</summary>
        static void CarriageBolts(Transform post, Transform root, float face, float zc, float yBottom)
        {
            AttachHardware(post, root, "Carriage bolts", Vector3.zero, Quaternion.identity, (b, g) =>
            {
                var side = Quaternion.FromToRotation(Vector3.up, Vector3.right);
                foreach (float y in new[] { yBottom + 1.75f, yBottom + 4.25f })
                {
                    b.AddFrustum(P(face + 3.5f + 0.12f, y, zc), side, M(0.55f), M(0.35f), M(0.24f), 16, g);  // domed head
                    b.AddCylinder(P(face - 1.5f - 0.05f, y, zc), side, M(0.68f), M(0.1f), 16, g);          // washer
                    b.AddCylinder(P(face - 1.5f - 0.32f, y, zc), side, M(0.43f), M(0.44f), 6, g);          // hex nut
                    b.AddCylinder(P(face - 1.5f - 0.7f, y, zc), side, M(0.25f), M(0.4f), 10, g);           // bolt end
                }
            });
        }

        /// <summary>Galvanized handrail bracket on a post's inside face; its saddle (with a small collider) carries the rail.</summary>
        static void RailBracket(Transform post, Transform root, float face, float xRail, float zc, float railBottomY)
        {
            float yArm = railBottomY - 1.4f;
            var go = AttachHardware(post, root, "Handrail bracket", Vector3.zero, Quaternion.identity, (b, g) =>
            {
                var side = Quaternion.FromToRotation(Vector3.up, Vector3.right);
                b.AddCylinder(P(face - 0.08f, yArm - 0.6f, zc), side, M(0.95f), M(0.16f), 20, g);                 // round wall plate
                foreach (float dy in new[] { -1.2f, 0f })
                    b.AddCylinder(P(face - 0.2f, yArm - 0.6f + dy * 0.5f + 0.3f, zc), side, M(0.17f), M(0.1f), 8, g); // screw heads
                b.AddBox(P((face + xRail) * 0.5f - 0.1f, yArm, zc), P(face - xRail - 0.2f, 0.32f, 0.32f), g);     // arm out from the post
                b.AddBox(P(xRail, (yArm + railBottomY) * 0.5f, zc), P(0.3f, railBottomY - yArm, 0.3f), g);        // riser up to the saddle
                b.AddBox(P(xRail, railBottomY - 0.06f, zc), P(0.9f, 0.12f, 1.4f), g);                              // saddle under the rail
                b.AddCylinder(P(xRail, railBottomY - 0.15f, zc), Quaternion.identity, M(0.15f), M(0.08f), 8, g);  // saddle screw
            });
            var col = go.AddComponent<BoxCollider>();
            col.center = P(xRail, railBottomY - 0.5f, zc);
            col.size = P(0.9f, 1.05f, 1.4f);
        }

        // ================================================================== inspection

        /// <summary>Framing inspection: every built stair at least 36" wide; stairs with 4+ risers need a handrail whose top is 34"-38"
        /// above the nosing line, 1 1/4"-2" round, carried by posts/studs and running the full flight.</summary>
        static List<InspectionIssue> StairCheck()
        {
            var list = new List<InspectionIssue>();
            foreach (var a in StairAssembly.All)
            {
                if (a == null || !a.isActiveAndEnabled) continue;
                var r = a.Result;
                StructuralMember rail = null;
                bool treads = false;
                foreach (var m in a.GetComponentsInChildren<StructuralMember>())
                {
                    if (m.Role == MemberRole.Tread) treads = true;
                    if (m.Role == MemberRole.Handrail && rail == null) rail = m;
                }
                if (!treads) continue; // stair not built yet
                Vector3 where = a.transform.TransformPoint(P(0f, r.TotalRiseIn * 0.5f + 30f, r.TotalRunIn * 0.5f));
                if (a.WidthIn < StairMath.MinWidthIn - 0.01f)
                    list.Add(new InspectionIssue { Severity = StructuralStatus.Warning, Where = where, Note = "Stair: " + StairMath.FormatIn(a.WidthIn) + " wide - 36\" minimum (IRC R311.7.1)" });
                if (!r.HandrailRequired) continue;
                if (rail == null)
                {
                    list.Add(new InspectionIssue { Severity = StructuralStatus.Warning, Where = where, Note = "Stair: no handrail - required on " + r.Risers + " risers (IRC R311.7.8)" });
                    continue;
                }
                float top = HandrailTopAboveNosingIn(a, rail, out float dia, out float zLo, out float zHi);
                if (top < StairMath.HandrailMinHeightIn - 0.1f || top > StairMath.HandrailMaxHeightIn + 0.1f)
                    list.Add(new InspectionIssue { Member = rail, Severity = StructuralStatus.Warning, Where = rail.transform.position, Note = "Handrail: top " + StairMath.FormatIn(top) + " above the nosings - must be 34\"-38\" (IRC R311.7.8.1)" });
                if (dia < StairMath.HandrailMinDiaIn - 0.01f || dia > StairMath.HandrailMaxDiaIn + 0.01f)
                    list.Add(new InspectionIssue { Member = rail, Severity = StructuralStatus.Warning, Where = rail.transform.position, Note = "Handrail: " + StairMath.FormatIn(dia) + " grip - must be 1 1/4\"-2\" round (IRC R311.7.8.5)" });
                float bottomRiser = -r.RiserThicknessIn, topRiser = (r.Risers - 1) * r.UnitRunIn - r.RiserThicknessIn;
                if (zLo > bottomRiser + 1f || zHi < topRiser - 1f)
                    list.Add(new InspectionIssue { Member = rail, Severity = StructuralStatus.Warning, Where = rail.transform.position, Note = "Handrail: must run from above the top riser to above the bottom riser (IRC R311.7.8.4)" });
            }
            return list;
        }

        /// <summary>Measured handrail: top height above the nosing line (plumb, at its middle), its grip diameter and the run range it covers.</summary>
        public static float HandrailTopAboveNosingIn(StairAssembly a, StructuralMember rail, out float diaIn, out float zLoIn, out float zHiIn)
        {
            var col = rail.GetComponent<BoxCollider>();
            Vector3 size = col != null ? col.size : Vector3.one * Units.In(RailDiaIn);
            diaIn = Units.ToInches(Mathf.Min(size.x, size.y));
            Vector3 c = a.transform.InverseTransformPoint(rail.transform.TransformPoint(col != null ? col.center : Vector3.zero));
            Vector3 axis = a.transform.InverseTransformDirection(rail.transform.forward);
            Vector3 upAx = a.transform.InverseTransformDirection(rail.transform.up);
            float halfL = size.z * 0.5f;
            Vector3 e0 = c - axis * halfL, e1 = c + axis * halfL;
            zLoIn = Units.ToInches(Mathf.Min(e0.z, e1.z)); zHiIn = Units.ToInches(Mathf.Max(e0.z, e1.z));
            float zc = Units.ToInches(c.z);
            float topY = Units.ToInches(c.y) + (diaIn * 0.5f) / Mathf.Max(0.2f, Mathf.Abs(upAx.y));
            return topY - a.NosingLineY(zc);
        }
    }
}
