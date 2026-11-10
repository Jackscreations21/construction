using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum MemberRole
    {
        Unknown, SillGasket, SillPlate, RimJoist, Joist, Blocking, Beam, Post, Subfloor,
        BottomPlate, TopPlate, CapPlate, Stud, KingStud, JackStud, Header, Cripple, RoughSill, CornerBacker, Brace,
        WallSheathing, Truss, Rafter, HipRafter, ValleyRafter, JackRafter, RidgeBoard, CeilingJoist, CollarTie,
        SubFascia, Fascia, GableStud, RoofSheathing, Stringer, Tread, Riser, Ledger, Lookout, Product,
    }

    [Flags]
    public enum Connector { None = 0, Nailed = 1, AnchorBolt = 2, Hanger = 4, HurricaneTie = 8, HClip = 16, Adhesive = 32, Gasket = 64, Shimmed = 128, Flashed = 256 }

    public enum StructuralStatus { Ok, Warning, Unsupported }

    /// <summary>One structural rule per role: what may carry it, required connectors and on-center spacing.</summary>
    public class RoleRule
    {
        public MemberRole Role;
        public MemberRole[] SupportedBy = new MemberRole[0];
        public bool Foundation;          // may bear directly on ground / concrete
        public Connector Requires;       // connectors inspection expects
        public float MaxOCInches;        // 0 = no spacing rule
        public bool Allow24OC;           // 24" O.C. is also a valid layout
        public string Note = "";
    }

    /// <summary>Structural rules as data (IRC-flavoured). Feature modules may add or replace rules with Set().</summary>
    public static class StructuralRules
    {
        static readonly Dictionary<MemberRole, RoleRule> _rules = new Dictionary<MemberRole, RoleRule>();
        static readonly MemberRole[] FloorTop = { MemberRole.Subfloor, MemberRole.Joist, MemberRole.RimJoist, MemberRole.Blocking, MemberRole.Beam };
        static readonly MemberRole[] WallTop = { MemberRole.TopPlate, MemberRole.CapPlate };

        static StructuralRules()
        {
            R(MemberRole.SillGasket, true);
            R(MemberRole.SillPlate, true, Connector.AnchorBolt | Connector.Gasket, note: "PT sill on gasket, 1/2\" anchor bolts max 6' O.C., within 12\" of ends");
            R(MemberRole.RimJoist, false, Connector.Nailed, sup: new[] { MemberRole.SillPlate, MemberRole.Beam, MemberRole.Ledger, MemberRole.TopPlate, MemberRole.CapPlate });
            R(MemberRole.Joist, false, Connector.Nailed, 16f, false, sup: new[] { MemberRole.SillPlate, MemberRole.Beam, MemberRole.RimJoist, MemberRole.Ledger, MemberRole.TopPlate, MemberRole.CapPlate, MemberRole.Header },
              note: "16\" O.C.; joists framing into a beam or rim end-on need hangers");
            R(MemberRole.Blocking, false, Connector.Nailed, sup: new[] { MemberRole.Subfloor, MemberRole.Joist, MemberRole.SillPlate, MemberRole.Rafter, MemberRole.Truss, MemberRole.Stud });
            R(MemberRole.Beam, true, Connector.Nailed, sup: new[] { MemberRole.Post, MemberRole.SillPlate, MemberRole.KingStud, MemberRole.JackStud, MemberRole.Stud });
            R(MemberRole.Post, true, Connector.Nailed, sup: new[] { MemberRole.Subfloor, MemberRole.SillPlate, MemberRole.Beam });
            R(MemberRole.Subfloor, false, Connector.Nailed | Connector.Adhesive, sup: new[] { MemberRole.Joist, MemberRole.RimJoist, MemberRole.Blocking, MemberRole.Beam }, note: "glue + nail 6\" edges / 12\" field");
            R(MemberRole.BottomPlate, true, Connector.Nailed, sup: FloorTop);
            R(MemberRole.Stud, false, Connector.Nailed, 16f, true, sup: new[] { MemberRole.BottomPlate, MemberRole.RoughSill, MemberRole.TopPlate, MemberRole.Header }, note: "16\" or 24\" O.C.");
            R(MemberRole.KingStud, false, Connector.Nailed, sup: new[] { MemberRole.BottomPlate });
            R(MemberRole.JackStud, false, Connector.Nailed, sup: new[] { MemberRole.BottomPlate, MemberRole.Subfloor });
            R(MemberRole.CornerBacker, false, Connector.Nailed, sup: new[] { MemberRole.BottomPlate, MemberRole.Stud });
            R(MemberRole.Cripple, false, Connector.Nailed, sup: new[] { MemberRole.BottomPlate, MemberRole.Header, MemberRole.RoughSill, MemberRole.TopPlate });
            R(MemberRole.Header, false, Connector.Nailed, sup: new[] { MemberRole.JackStud, MemberRole.KingStud }, note: "header sized for span, bears on jack studs");
            R(MemberRole.RoughSill, false, Connector.Nailed, sup: new[] { MemberRole.Cripple, MemberRole.JackStud, MemberRole.KingStud });
            R(MemberRole.TopPlate, false, Connector.Nailed, sup: new[] { MemberRole.Stud, MemberRole.KingStud, MemberRole.Cripple, MemberRole.CornerBacker, MemberRole.Header });
            R(MemberRole.CapPlate, false, Connector.Nailed, sup: new[] { MemberRole.TopPlate }, note: "double top plate, lap corners and splices 24\" min");
            R(MemberRole.Brace, true, Connector.Nailed, sup: new[] { MemberRole.Stud, MemberRole.KingStud, MemberRole.TopPlate, MemberRole.Subfloor, MemberRole.Blocking });
            R(MemberRole.WallSheathing, false, Connector.Nailed, sup: new[] { MemberRole.Stud, MemberRole.KingStud, MemberRole.JackStud, MemberRole.TopPlate, MemberRole.CapPlate, MemberRole.BottomPlate, MemberRole.Header, MemberRole.Cripple, MemberRole.CornerBacker, MemberRole.RimJoist, MemberRole.GableStud, MemberRole.Truss });
            R(MemberRole.Truss, false, Connector.HurricaneTie, 24f, true, sup: WallTop, note: "24\" O.C., hurricane tie at each bearing");
            R(MemberRole.Rafter, false, Connector.HurricaneTie, 16f, true, sup: new[] { MemberRole.TopPlate, MemberRole.CapPlate, MemberRole.RidgeBoard, MemberRole.HipRafter, MemberRole.ValleyRafter }, note: "birdsmouth on the plate, hurricane tie");
            R(MemberRole.JackRafter, false, Connector.Nailed, 16f, true, sup: new[] { MemberRole.TopPlate, MemberRole.CapPlate, MemberRole.HipRafter, MemberRole.ValleyRafter, MemberRole.RidgeBoard });
            R(MemberRole.HipRafter, false, Connector.Nailed, sup: new[] { MemberRole.TopPlate, MemberRole.CapPlate, MemberRole.RidgeBoard, MemberRole.Rafter });
            R(MemberRole.ValleyRafter, false, Connector.Nailed, sup: new[] { MemberRole.TopPlate, MemberRole.CapPlate, MemberRole.RidgeBoard, MemberRole.Rafter });
            R(MemberRole.RidgeBoard, false, Connector.Nailed, sup: new[] { MemberRole.Rafter, MemberRole.Post, MemberRole.GableStud, MemberRole.HipRafter });
            R(MemberRole.CeilingJoist, false, Connector.Nailed, 16f, true, sup: WallTop);
            R(MemberRole.CollarTie, false, Connector.Nailed, sup: new[] { MemberRole.Rafter });
            R(MemberRole.SubFascia, false, Connector.Nailed, sup: new[] { MemberRole.Rafter, MemberRole.Truss, MemberRole.JackRafter, MemberRole.HipRafter });
            R(MemberRole.Fascia, false, Connector.Nailed, sup: new[] { MemberRole.SubFascia, MemberRole.Rafter, MemberRole.Truss, MemberRole.Lookout });
            R(MemberRole.GableStud, false, Connector.Nailed, sup: WallTop);
            R(MemberRole.Lookout, false, Connector.Nailed, sup: new[] { MemberRole.Truss, MemberRole.Rafter });
            R(MemberRole.RoofSheathing, false, Connector.Nailed | Connector.HClip, sup: new[] { MemberRole.Truss, MemberRole.Rafter, MemberRole.JackRafter, MemberRole.HipRafter, MemberRole.ValleyRafter, MemberRole.RidgeBoard, MemberRole.SubFascia, MemberRole.Lookout, MemberRole.RoofSheathing }, note: "H-clips between rafters, 1/8\" gaps");
            R(MemberRole.Ledger, false, Connector.Nailed, sup: new[] { MemberRole.RimJoist, MemberRole.Stud, MemberRole.Post });
            R(MemberRole.Stringer, true, Connector.Nailed, sup: new[] { MemberRole.RimJoist, MemberRole.Ledger, MemberRole.Subfloor, MemberRole.Beam, MemberRole.Joist });
            R(MemberRole.Tread, false, Connector.Nailed, sup: new[] { MemberRole.Stringer });
            R(MemberRole.Riser, false, Connector.Nailed, sup: new[] { MemberRole.Stringer });
            R(MemberRole.Product, true, Connector.None, sup: new[] { MemberRole.Subfloor, MemberRole.RoughSill, MemberRole.JackStud, MemberRole.BottomPlate });
            R(MemberRole.Unknown, true, Connector.None, sup: (MemberRole[])Enum.GetValues(typeof(MemberRole)));
        }

        static void R(MemberRole role, bool foundation, Connector req = Connector.None, float oc = 0f, bool allow24 = false, MemberRole[] sup = null, string note = "")
            => _rules[role] = new RoleRule { Role = role, Foundation = foundation, Requires = req, MaxOCInches = oc, Allow24OC = allow24, SupportedBy = sup ?? new MemberRole[0], Note = note };

        public static RoleRule Get(MemberRole role) => _rules.TryGetValue(role, out var r) ? r : _rules[MemberRole.Unknown];
        public static void Set(RoleRule rule) => _rules[rule.Role] = rule;

        public static bool CanSupport(MemberRole member, MemberRole supporter)
        {
            var r = Get(member);
            return Array.IndexOf(r.SupportedBy, supporter) >= 0;
        }

        /// <summary>Is an on-center distance (inches) acceptable for this role?</summary>
        public static bool SpacingOk(RoleRule r, float ocInches)
        {
            if (r.MaxOCInches <= 0f) return true;
            if (ocInches <= r.MaxOCInches + 0.25f) return true;
            if (r.Allow24OC && Mathf.Abs(ocInches - 24f) <= 0.25f) return true;
            return false;
        }

        /// <summary>Header size (lumber id prefix + depth inches) for a rough-opening width, for a bearing wall carrying roof only
        /// (IRC R602.7 simplified): to 3' 2x6, to 4' 2x8, to 6' 2x10, to 8' 2x12, beyond that an LVL.</summary>
        public static (string lumber, float depthIn, int jacks) HeaderFor(float spanInches)
        {
            if (spanInches <= 36f) return ("2x6", 5.5f, 1);
            if (spanInches <= 48f) return ("2x8", 7.25f, 1);
            if (spanInches <= 72f) return ("2x10", 9.25f, 1);
            if (spanInches <= 96f) return ("2x12", 11.25f, 2);
            return ("lvl", 9.25f, 2);
        }

        /// <summary>Guess a role for a free-placed piece from its definition and orientation.</summary>
        public static MemberRole Classify(PieceDefinition def, Quaternion rot, Vector3 size)
        {
            if (def == null) return MemberRole.Unknown;
            if (def.IsProduct) return MemberRole.Product;
            if (def.Category == PieceCategory.Sheet)
            {
                Vector3 n = rot * Vector3.up;
                if (Mathf.Abs(n.y) > 0.95f) return MemberRole.Subfloor;
                if (Mathf.Abs(n.y) < 0.1f) return MemberRole.WallSheathing;
                return MemberRole.RoofSheathing;
            }
            if (def.Category != PieceCategory.Lumber) return MemberRole.Unknown;
            Vector3 len = rot * Vector3.forward;
            string id = def.Id ?? "";
            bool treated = id.StartsWith("pt");
            if (Mathf.Abs(len.y) > 0.95f) return id.Contains("4x4") ? MemberRole.Post : MemberRole.Stud;
            if (Mathf.Abs(len.y) > 0.1f) return MemberRole.Rafter;
            Vector3 thin = rot * Vector3.up; // board's 1.5" axis
            bool flat = Mathf.Abs(thin.y) > 0.9f;
            if (flat) return treated ? MemberRole.SillPlate : MemberRole.BottomPlate;
            if (id.Contains("lvl")) return MemberRole.Beam;
            return MemberRole.Joist;
        }
    }

    /// <summary>Attached to every structural piece: role, connectors and the members it bears on (support graph edges).</summary>
    [DisallowMultipleComponent]
    public class StructuralMember : MonoBehaviour
    {
        public MemberRole Role;
        public Connector Connectors;
        public readonly List<StructuralMember> SupportedBy = new List<StructuralMember>();
        public bool OnFoundation;
        public StructuralStatus Status;
        public readonly List<string> Notes = new List<string>();
        /// <summary>Set for members that are part of a wall assembly / truss / plan (they move as a unit).</summary>
        public string Group = "";

        public Piece Piece => GetComponent<Piece>();

        void OnEnable() { SupportGraph.Members.Add(this); SupportGraph.Dirty = true; }
        void OnDisable() { SupportGraph.Members.Remove(this); SupportGraph.Dirty = true; }

        public bool Has(Connector c) => (Connectors & c) == c;

        /// <summary>World-space oriented box (center, half extents, rotation) from the BoxCollider.</summary>
        public bool GetBox(out Vector3 center, out Vector3 half, out Quaternion rot)
        {
            var col = GetComponent<BoxCollider>();
            if (col == null) { center = transform.position; half = Vector3.one * 0.01f; rot = transform.rotation; return false; }
            center = transform.TransformPoint(col.center);
            half = Vector3.Scale(col.size * 0.5f, transform.lossyScale);
            rot = transform.rotation;
            return true;
        }
    }

    /// <summary>Support graph: which member bears on which, grounded via the foundation (ground or concrete).
    /// Hook <see cref="FoundationProbe"/> lets the concrete module say "this point sits on a footing/slab".</summary>
    public static class SupportGraph
    {
        public static readonly HashSet<StructuralMember> Members = new HashSet<StructuralMember>();
        public static bool Dirty = true;
        public const float Touch = 0.008f; // 8 mm contact tolerance

        /// <summary>Optional: returns true if the world point (bottom of a member) rests on a foundation surface
        /// (e.g. a ConcreteElement top). Default: ground plane at y = 0.</summary>
        public static Func<Vector3, bool> FoundationProbe;
        public static float GroundY = 0f;

        public static bool IsFoundation(Vector3 bottomPoint)
        {
            if (FoundationProbe != null && FoundationProbe(bottomPoint)) return true;
            if (bottomPoint.y <= GroundY + 0.02f) return true;
            // Anything that is not a framing member but carries a collider named like a foundation counts too.
            var hits = Physics.OverlapSphere(bottomPoint + Vector3.down * 0.01f, 0.02f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<StructuralMember>() != null || h.GetComponentInParent<Piece>() != null) continue;
                string n = h.gameObject.name.ToLowerInvariant();
                if (n.Contains("ground") || n.Contains("slab") || n.Contains("footing") || n.Contains("foundation") || n.Contains("concrete") || n.Contains("terrain")) return true;
            }
            return false;
        }

        /// <summary>Members touching an oriented box (excluding 'self').</summary>
        public static List<StructuralMember> Touching(Vector3 c, Vector3 half, Quaternion rot, StructuralMember self = null)
        {
            var list = new List<StructuralMember>();
            var cols = Physics.OverlapBox(c, half + Vector3.one * Touch, rot, ~0, QueryTriggerInteraction.Ignore);
            foreach (var col in cols)
            {
                var m = col.GetComponentInParent<StructuralMember>();
                if (m == null || m == self || list.Contains(m)) continue;
                list.Add(m);
            }
            return list;
        }

        static float BottomY(Vector3 c, Vector3 half, Quaternion rot)
        {
            float ext = Mathf.Abs((rot * Vector3.right).y) * half.x + Mathf.Abs((rot * Vector3.up).y) * half.y + Mathf.Abs((rot * Vector3.forward).y) * half.z;
            return c.y - ext;
        }

        /// <summary>Direct supporters of a box with a given role. A supporter must be of an allowed role and not entirely above it.</summary>
        public static List<StructuralMember> FindSupports(MemberRole role, Vector3 c, Vector3 half, Quaternion rot, StructuralMember self, out bool onFoundation)
        {
            var rule = StructuralRules.Get(role);
            float bottom = BottomY(c, half, rot);
            onFoundation = rule.Foundation && IsFoundation(new Vector3(c.x, bottom, c.z));
            var result = new List<StructuralMember>();
            foreach (var m in Touching(c, half, rot, self))
            {
                if (!StructuralRules.CanSupport(role, m.Role)) continue;
                if (!m.GetBox(out var mc, out var mh, out var mr)) continue;
                if (BottomY(mc, mh, mr) > c.y + 0.001f && role != MemberRole.Header && role != MemberRole.Joist) continue; // hangs only by hangers/nails
                result.Add(m);
            }
            return result;
        }

        /// <summary>Recomputes every member's supports and whether it is grounded (BFS from the foundation).</summary>
        public static void Rebuild()
        {
            Physics.SyncTransforms();
            foreach (var m in Members)
            {
                m.SupportedBy.Clear();
                if (!m.GetBox(out var c, out var h, out var r)) continue;
                m.SupportedBy.AddRange(FindSupports(m.Role, c, h, r, m, out bool f));
                m.OnFoundation = f;
            }
            Dirty = false;
        }

        public static HashSet<StructuralMember> Grounded()
        {
            if (Dirty) Rebuild();
            var g = new HashSet<StructuralMember>();
            bool changed = true;
            foreach (var m in Members) if (m.OnFoundation) g.Add(m);
            while (changed)
            {
                changed = false;
                foreach (var m in Members)
                {
                    if (g.Contains(m)) continue;
                    foreach (var s in m.SupportedBy) if (g.Contains(s)) { g.Add(m); changed = true; break; }
                }
            }
            return g;
        }

        /// <summary>Placement check for a ghost box: Unsupported (red) if nothing grounded carries it, Warning (yellow) if the
        /// on-center spacing to a neighbouring member of the same role is wrong.</summary>
        public static StructuralStatus CheckGhost(PieceDefinition def, Vector3 c, Quaternion rot, Vector3 size, out string reason)
        {
            reason = "";
            Physics.SyncTransforms();
            var role = StructuralRules.Classify(def, rot, size);
            if (role == MemberRole.Unknown || role == MemberRole.Product) return StructuralStatus.Ok;
            Vector3 half = size * 0.5f;
            var sup = FindSupports(role, c, half, rot, null, out bool found);
            if (!found && IsFoundation(new Vector3(c.x, BottomY(c, half, rot), c.z))) return StructuralStatus.Ok; // material laid on the ground
            if (!found)
            {
                var grounded = Grounded();
                bool any = false;
                foreach (var s in sup) if (grounded.Contains(s)) { any = true; break; }
                if (!any) { reason = "unsupported " + role + " (nothing under it can carry it)"; return StructuralStatus.Unsupported; }
            }
            float oc = NearestOC(role, c, rot, null);
            if (oc > 0f && !StructuralRules.SpacingOk(StructuralRules.Get(role), Units.ToInches(oc)))
            {
                reason = role + " at " + Units.Format(oc) + " O.C. - will fail inspection (16\"/24\" O.C.)";
                return StructuralStatus.Warning;
            }
            return StructuralStatus.Ok;
        }

        /// <summary>On-center distance (meters) to the nearest parallel member of the same role within 30", or 0.</summary>
        public static float NearestOC(MemberRole role, Vector3 c, Quaternion rot, StructuralMember self)
        {
            var rule = StructuralRules.Get(role);
            if (rule.MaxOCInches <= 0f) return 0f;
            Vector3 len = rot * Vector3.forward;
            float best = 0f;
            foreach (var m in Members)
            {
                if (m == self || m.Role != role || !m.isActiveAndEnabled) continue;
                Vector3 ml = m.transform.rotation * Vector3.forward;
                if (Mathf.Abs(Vector3.Dot(ml, len)) < 0.98f) continue;
                Vector3 d = m.transform.position - c;
                Vector3 perp = d - len * Vector3.Dot(d, len);
                float along = Mathf.Abs(Vector3.Dot(d, len));
                if (along > m.GetComponent<BoxCollider>().size.z * 0.5f + 0.05f) continue; // not side by side
                if (Mathf.Abs(perp.y) > 0.3f) continue; // different level
                float dist = perp.magnitude;
                if (dist < 0.02f || dist > Units.In(30f)) continue;
                if (best == 0f || dist < best) best = dist;
            }
            return best;
        }

        /// <summary>Full evaluation for one placed member (used by inspection and HUD).</summary>
        public static StructuralStatus Evaluate(StructuralMember m, HashSet<StructuralMember> grounded)
        {
            m.Notes.Clear();
            var rule = StructuralRules.Get(m.Role);
            var status = StructuralStatus.Ok;
            if (!grounded.Contains(m)) { m.Notes.Add("Not supported - no load path to the foundation"); status = StructuralStatus.Unsupported; }
            if (m.Piece != null && m.Piece.State != PieceState.Fastened) { m.Notes.Add("Not fastened"); status = Max(status, StructuralStatus.Warning); }
            var missing = rule.Requires & ~m.Connectors;
            if (missing != Connector.None) { m.Notes.Add("Missing: " + missing); status = Max(status, StructuralStatus.Warning); }
            float oc = NearestOC(m.Role, m.transform.position, m.transform.rotation, m);
            if (oc > 0f && !StructuralRules.SpacingOk(rule, Units.ToInches(oc)))
            { m.Notes.Add(m.Role + " spacing " + Units.Format(oc) + " O.C. (max " + rule.MaxOCInches + "\"" + (rule.Allow24OC ? " or 24\"" : "") + ")"); status = Max(status, StructuralStatus.Warning); }
            m.Status = status;
            return status;
        }

        static StructuralStatus Max(StructuralStatus a, StructuralStatus b) => (int)a >= (int)b ? a : b;
    }
}
