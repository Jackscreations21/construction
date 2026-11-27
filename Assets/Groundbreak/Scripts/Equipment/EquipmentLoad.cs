using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// A palletized or bundled load that the skid steer forks and the crane hook can pick up: a real-size model with a
    /// weight. Kinds: "shingles" (full pallet, 42 bundles), "shingles-half" (21 bundles), "osb" (lift of 40 sheets 7/16" 4x8),
    /// "block" (pallet of 90 8x8x16 CMU). Root origin = bottom centre; forks/hook pick point = <see cref="PickLocal"/>.
    /// The root sits on layer 2 with a box collider (stops the player, ignored by aim / ground rays).
    /// </summary>
    public class EquipmentLoad : MonoBehaviour
    {
        public static readonly List<EquipmentLoad> All = new List<EquipmentLoad>();
        public string Kind, Label;
        public float WeightLb;
        public Vector3 Size;                              // overall (x, y, z) in meters
        public Vector3 PickLocal => new Vector3(0, Size.y + 0.05f, 0);
        /// <summary>The machine part carrying it (fork carriage / hook), or null when it rests on something.</summary>
        public Transform CarriedBy { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { All.Clear(); }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        void OnDestroy() { foreach (var mf in GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) Destroy(mf.sharedMesh); }

        public static EquipmentLoad Create(string kind, Vector3 pos, float yaw)
        {
            var go = new GameObject("Load_" + kind) { layer = 2 };
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var l = go.AddComponent<EquipmentLoad>();
            l.Kind = kind;
            float pw = 1.016f, pl = 1.219f, ph = 0.14f;   // 40" x 48" pallet
            switch (kind)
            {
                case "shingles": l.Label = "Pallet of shingles (42 bundles)"; l.WeightLb = 3100; l.Size = new Vector3(pw, ph + 14 * 0.075f, pl); break;
                case "shingles-half": l.Label = "Half pallet of shingles (21 bundles)"; l.WeightLb = 1550; l.Size = new Vector3(pw, ph + 7 * 0.075f, pl); break;
                case "block": l.Label = "Pallet of 8x8x16 block (90)"; l.WeightLb = 3150; l.Size = new Vector3(pw, ph + 5 * 0.194f, pl); break;
                default: l.Kind = "osb"; l.Label = "Lift of 7/16\" OSB (40 sheets)"; l.WeightLb = 1760; l.Size = new Vector3(1.219f, 0.1f + 40 * 0.0111f, 2.438f); break;
            }
            var mesh = CTool.Build("Model", (b, m) => l.Author(b, m), go.transform);
            mesh.layer = 2;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, l.Size.y * 0.5f, 0); bc.size = l.Size;
            return l;
        }

        void Author(MeshBuilder b, CTool.Mats m)
        {
            int wood = m.K(SurfaceKind.PineLumber);
            if (Kind == "osb")
            {
                // 4x4 dunnage under a strapped lift of sheets
                int osb = m.K(SurfaceKind.Osb), strap = m.K(SurfaceKind.MatteBlack);
                foreach (float z in new[] { -0.9f, 0f, 0.9f }) b.AddBox(new Vector3(0, 0.045f, z), new Vector3(Size.x, 0.09f, 0.09f), wood);
                float h = Size.y - 0.1f;
                b.AddBox(new Vector3(0, 0.1f + h * 0.5f, 0), new Vector3(Size.x, h, Size.z), Quaternion.identity, osb, Vector3.forward);
                foreach (float z in new[] { -0.7f, 0.7f }) b.AddBox(new Vector3(0, 0.1f + h * 0.5f, z), new Vector3(Size.x + 0.004f, h + 0.004f, 0.02f), strap);
                return;
            }
            // 40x48 stringer pallet
            for (int i = 0; i < 7; i++) b.AddBox(new Vector3(-0.45f + i * 0.15f, 0.13f, 0), new Vector3(0.09f, 0.016f, 1.219f), wood);
            foreach (float x in new[] { -0.47f, 0f, 0.47f }) b.AddBox(new Vector3(x, 0.065f, 0), new Vector3(0.038f, 0.09f, 1.219f), wood);
            for (int i = 0; i < 3; i++) b.AddBox(new Vector3(-0.38f + i * 0.38f, 0.008f, 0), new Vector3(0.09f, 0.016f, 1.219f), wood);
            if (Kind == "block")
            {
                int cmu = m.K(SurfaceKind.Concrete);
                for (int layer = 0; layer < 5; layer++)
                    for (int r = 0; r < 3; r++)
                        for (int c = 0; c < 6; c++)
                            b.AddBox(new Vector3(-0.42f + r * 0.203f * 2.05f, 0.14f + layer * 0.194f + 0.097f, -0.5f + c * 0.2f), new Vector3(0.39f, 0.19f, 0.19f), cmu);
                return;
            }
            int shingle = m.K(SurfaceKind.MatteBlack), wrap = m.K(SurfaceKind.PaintedWhite, new Color(0.75f, 0.72f, 0.66f));
            int layers = Kind == "shingles" ? 14 : 7;
            for (int lyr = 0; lyr < layers; lyr++)
                for (int s = 0; s < 3; s++)   // 3 bundles per layer, cross-stacked
                {
                    float y = 0.14f + lyr * 0.075f + 0.0375f;
                    if (lyr % 2 == 0) b.AddBox(new Vector3(-0.34f + s * 0.34f, y, 0), new Vector3(0.33f, 0.072f, 1.0f), Quaternion.identity, s == 1 ? wrap : shingle);
                    else b.AddBox(new Vector3(0, y, -0.4f + s * 0.4f), new Vector3(1.0f, 0.072f, 0.33f), Quaternion.identity, s == 1 ? wrap : shingle);
                }
        }

        /// <summary>Attaches to a carrier (fork carriage / hook block) keeping its world pose.</summary>
        public void PickUp(Transform carrier)
        {
            CarriedBy = carrier;
            transform.SetParent(carrier, true);
        }

        /// <summary>Sets it down where it is (snapped upright onto whatever is below).</summary>
        public void SetDown()
        {
            CarriedBy = null;
            transform.SetParent(null, true);
            Vector3 p = transform.position;
            float yaw = transform.eulerAngles.y;
            float y = p.y;
            var hits = Physics.RaycastAll(p + Vector3.up * 0.3f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (Game.Player != null && h.collider.transform.IsChildOf(Game.Player.transform)) continue;
                if (Machine.Current != null && h.collider.transform.IsChildOf(Machine.Current.transform)) continue;
                if (h.point.y > best) best = h.point.y;
            }
            if (!float.IsNegativeInfinity(best)) y = best;
            transform.SetPositionAndRotation(new Vector3(p.x, y, p.z), Quaternion.Euler(0, yaw, 0));
            Physics.SyncTransforms();
        }
    }
}
