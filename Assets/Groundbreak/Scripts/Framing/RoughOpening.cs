using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A framed rough opening in a wall (between jack studs, under the header, over the rough sill).
    /// Hook for windows/doors: flash it (sill pan + jamb/head tape), then place the matching product with the
    /// place tool — it snaps in — then shim, level/plumb and nail the fin / jamb (InstallJob).</summary>
    public class RoughOpening : MonoBehaviour
    {
        public static readonly List<RoughOpening> All = new List<RoughOpening>();
        public WallAssembly Wall;
        public WallOpening Spec;
        public float WidthM, HeightM;      // rough opening size
        public bool IsDoor;
        public bool Flashed;
        public Piece Installed;
        public bool Shimmed, Leveled, Fastened, Taped;

        /// <summary>Invoked when a product has been fully installed in an opening (other modules: trim, siding...).</summary>
        public static System.Action<RoughOpening> ProductInstalled;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static RoughOpening Create(WallAssembly w, WallOpening o)
        {
            var go = new GameObject("RoughOpening " + (o.Product != null ? o.Product.Name : ""));
            go.transform.SetParent(w.transform, false);
            float rw = Units.In(o.RoughWidthIn), rh = Units.In(WallOpening.HeaderBottomIn - o.SillTopIn);
            // local origin: center of the opening on the exterior face plane
            go.transform.localPosition = new Vector3(Units.In(o.CenterIn), Units.In(o.SillTopIn) + rh / 2, 0f);
            go.transform.localRotation = Quaternion.identity;
            var ro = go.AddComponent<RoughOpening>();
            ro.Wall = w; ro.Spec = o; ro.WidthM = rw; ro.HeightM = rh; ro.IsDoor = o.IsDoor;
            w.Openings.Add(ro);
            return ro;
        }

        public Vector3 Center => transform.position;
        /// <summary>Direction pointing out of the building through this opening.</summary>
        public Vector3 Outward => -transform.forward;

        public bool Fits(PieceDefinition d, out string why)
        {
            why = "";
            if (d == null || (d.Category != PieceCategory.Window && d.Category != PieceCategory.Door)) { why = "not a window or door"; return false; }
            if ((d.Category == PieceCategory.Door) != IsDoor) { why = IsDoor ? "this is a door opening" : "this is a window opening"; return false; }
            float gw = WidthM - d.Size.x, gh = HeightM - d.Size.y;
            float minW = Units.In(IsDoor ? 1.5f : 0.375f), maxW = Units.In(IsDoor ? 2.75f : 1f);
            if (gw < minW) { why = "unit too wide for the " + Units.Format(WidthM) + " rough opening"; return false; }
            if (gw > maxW) { why = "rough opening " + Units.Format(WidthM) + " is too wide for this unit (gap " + Units.Format(gw) + ")"; return false; }
            if (gh < Units.In(0.25f)) { why = "unit too tall for the opening"; return false; }
            if (gh > Units.In(3f)) { why = "opening too tall for this unit"; return false; }
            return true;
        }

        public static RoughOpening Nearest(Vector3 p, float maxDist = 1.5f, PieceDefinition fitting = null)
        {
            RoughOpening best = null; float bd = maxDist;
            foreach (var ro in All)
            {
                if (ro.Installed != null || ro.Wall == null || ro.Wall.State == WallState.Flat) continue;
                if (fitting != null && !ro.Fits(fitting, out _)) continue;
                float d = Vector3.Distance(p, ro.Center);
                if (d < bd) { bd = d; best = ro; }
            }
            return best;
        }

        /// <summary>Weather-resistive flashing of the opening before the unit goes in: sill pan tape turned up the jacks,
        /// then (after the unit) jambs, then head – shingle-lapped.</summary>
        public FramingJob FlashJob()
        {
            var job = FramingJob.Create("Flash " + name, transform);
            job.Add("Sill pan: flashing tape across the rough sill, 6\" up each jack", ToolSets.Hands, () =>
            {
                Flashed = true;
                TapeStrip(new Vector3(0, -HeightM / 2, 0), new Vector3(WidthM + Units.In(12), Units.In(0.04f), Units.In(4f)), true);
                TapeStrip(new Vector3(-WidthM / 2 - Units.In(1.5f), -HeightM / 2 + Units.In(3f), -Units.In(0.02f)), new Vector3(Units.In(4f), Units.In(6f), Units.In(0.04f)), false);
                TapeStrip(new Vector3(WidthM / 2 + Units.In(1.5f), -HeightM / 2 + Units.In(3f), -Units.In(0.02f)), new Vector3(Units.In(4f), Units.In(6f), Units.In(0.04f)), false);
            }, () => (Center, new Vector3(WidthM, Units.In(2f), Units.In(4f)), transform.rotation)).Use("flash-tape", Mathf.CeilToInt(Units.ToInches(WidthM) / 12f) + 1);
            return job;
        }

        void TapeStrip(Vector3 local, Vector3 size, bool sill)
        {
            var mat = ShellMaterials.Flat("flash-tape", new Color(0.13f, 0.13f, 0.14f), 0.45f, 0.06f, 0.3f);
            var go = Framer.AttachMesh(transform, sill ? "SillPanTape" : "FlashTape", (MeshBuilder b, System.Func<Material, int> sub) =>
            {
                int g = sub(mat);
                if (sill)
                {
                    // across the sill top, lapping down the exterior face
                    b.AddBox(new Vector3(local.x, local.y + Units.In(0.02f), Units.In(Wall.Spec.DepthIn) / 2), new Vector3(size.x, Units.In(0.04f), Units.In(Wall.Spec.DepthIn)), Quaternion.identity, g);
                    b.AddBox(new Vector3(local.x, local.y - Units.In(1.5f), -Units.In(0.47f)), new Vector3(size.x, Units.In(3f), Units.In(0.04f)), Quaternion.identity, g);
                }
                else b.AddBox(new Vector3(local.x, local.y, -Units.In(0.47f)), size, Quaternion.identity, g);
            });
        }

        /// <summary>Snap a placed product into the opening (exterior face of the unit ~flush with the sheathing).</summary>
        public void Seat(Piece p)
        {
            Installed = p;
            var w = Wall.transform;
            float depth = p.Def.Size.z;
            float zLocal = IsDoor ? Units.In(Wall.Spec.DepthIn) / 2 : depth / 2 - Units.In(0.4375f) - Units.In(0.75f);
            float yBottom = transform.localPosition.y - HeightM / 2 + (IsDoor ? 0f : Units.In(0.25f));
            Vector3 local = new Vector3(transform.localPosition.x, yBottom + p.Def.Size.y / 2, zLocal);
            p.transform.SetPositionAndRotation(w.TransformPoint(local), w.rotation * Quaternion.Euler(0, 180, 0));
            p.Fasten();
            var m = p.GetComponent<StructuralMember>() ?? p.gameObject.AddComponent<StructuralMember>();
            m.Role = MemberRole.Product; m.Connectors = Connector.Flashed;
            SupportGraph.Dirty = true;
        }

        /// <summary>Steps after the unit is set: shim, level + plumb, fasten, tape jambs then head.</summary>
        public FramingJob InstallJob()
        {
            var job = FramingJob.Create("Install " + (Installed != null ? Installed.Def.Name : "unit"), transform);
            System.Func<(Vector3, Vector3, Quaternion)> g = () => (Center, new Vector3(WidthM, HeightM, Units.In(4f)), transform.rotation);
            job.Add("Shim the unit (cedar shims at jambs / hinge points)", ToolSets.Hands, () => { Shimmed = true; AddShims(); }, g).Use("shims", IsDoor ? 12 : 6);
            job.Add("Check level and plumb with the 4' level, adjust shims", new[] { "level" }, () => Leveled = true, g);
            job.Add(IsDoor ? "Nail jambs through shims (8d), long screw at the hinges" : "Nail the fin (roofing nails every 8\")", ToolSets.Fasten, () => Fastened = true, g).Nails = IsDoor ? 10 : 16;
            if (!IsDoor)
                job.Add("Tape the fins: jambs first, then the head (shingle-lapped)", ToolSets.Hands, () =>
                {
                    Taped = true;
                    TapeStrip(new Vector3(-WidthM / 2 - Units.In(0.5f), 0, 0), new Vector3(Units.In(4f), HeightM + Units.In(4f), Units.In(0.04f)), false);
                    TapeStrip(new Vector3(WidthM / 2 + Units.In(0.5f), 0, 0), new Vector3(Units.In(4f), HeightM + Units.In(4f), Units.In(0.04f)), false);
                    TapeStrip(new Vector3(0, HeightM / 2 + Units.In(1.5f), -Units.In(0.01f)), new Vector3(WidthM + Units.In(10f), Units.In(4f), Units.In(0.04f)), false);
                }, g).Use("flash-tape", Mathf.CeilToInt(Units.ToInches(WidthM + 2 * HeightM) / 12f) + 2);
            job.Completed += () => ProductInstalled?.Invoke(this);
            return job;
        }

        void AddShims()
        {
            float d = Units.In(Wall.Spec.DepthIn);
            Framer.AttachMesh(transform, "Shims", (MeshBuilder b, System.Func<SurfaceKind, int> s) =>
            {
                int g = s(SurfaceKind.PineLumber);
                int n = IsDoor ? 3 : 2;
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < n; i++)
                    {
                        float y = -HeightM / 2 + HeightM * (i + 0.5f) / n;
                        float x = side * (WidthM / 2 - Units.In(0.3f));
                        var poly = new[] { new Vector2(-Units.In(0.02f), 0), new Vector2(Units.In(0.3f), 0), new Vector2(-Units.In(0.02f), d * 0.9f) };
                        b.AddExtrude(poly, new Vector3(x, y, d * 0.05f), Quaternion.Euler(0, side < 0 ? 0 : 180, 0) * Quaternion.Euler(-90, 0, 0) * Quaternion.Euler(0, 0, 0), Units.In(1.5f), g);
                    }
            });
        }

        /// <summary>PlacementController hook: product ghosts near an opening must fit it and the opening must be flashed.</summary>
        public static (int level, string reason) CheckProduct(PieceDefinition d, Vector3 pos)
        {
            if (d == null || (d.Category != PieceCategory.Window && d.Category != PieceCategory.Door)) return (0, "");
            RoughOpening near = null; float bd = 1.5f;
            foreach (var ro in All)
            {
                if (ro.Installed != null || ro.Wall == null || ro.Wall.State == WallState.Flat) continue;
                float dd = Vector3.Distance(pos, ro.Center); if (dd < bd) { bd = dd; near = ro; }
            }
            if (near == null) return (0, "");
            if (!near.Fits(d, out string why)) return (2, why);
            if (!near.Flashed) return (2, "flash the rough opening first (sill pan tape)");
            return (0, "snaps into the rough opening");
        }

        public static void OnPlaced(Piece p)
        {
            if (p == null || p.Def == null) return;
            if (p.Def.Category != PieceCategory.Window && p.Def.Category != PieceCategory.Door) return;
            var ro = Nearest(p.transform.position, 1.5f, p.Def);
            if (ro == null || !ro.Flashed) return;
            ro.Seat(p);
            ro.InstallJob();
            Game.Toast(p.Def.Name + " set in the rough opening - shim, level, fasten, tape");
        }
    }
}
