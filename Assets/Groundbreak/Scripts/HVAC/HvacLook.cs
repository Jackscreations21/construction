using UnityEngine;

namespace Groundbreak
{
    /// <summary>Material tweaks for HVAC: duct sheet metal uses a finer ("minimized") spangle than the shared galvanized
    /// look (AC cabinets). Swapped onto HVAC duct runs and auto-inserted duct fittings as they are built.</summary>
    public static class HvacLook
    {
        static Material _galv;
        public static Material Galv
        {
            get
            {
                if (_galv != null) return _galv;
                var b = Materials.Get(SurfaceKind.Galvanized);
                _galv = new Material(b) { name = "GB_Galvanized_Duct", hideFlags = HideFlags.DontUnloadUnusedAsset };
                _galv.mainTextureScale = b.mainTextureScale * 4f;
                _galv.color = b.color * new Color(0.97f, 0.98f, 1f);
                return _galv;
            }
        }

        /// <summary>Replaces the coarse shared galvanized material on HVAC duct runs / fittings with the duct one.</summary>
        public static void Apply()
        {
            foreach (var r in NetGraph.RunsOf(Trade.Hvac))
                if (r != null && r.Spec.Kind == NetKind.Duct && r.Spec.Surface == SurfaceKind.Galvanized) Swap(r.transform);
            foreach (var n in NetGraph.NodesOf(Trade.Hvac))
            {
                if (n == null || n.Piece != null) continue;
                Swap(n.transform);
                if (n.Type == "elbow" && n.transform.Find("Gores") == null) Gores(n);
            }
        }

        static void Swap(Transform t)
        {
            foreach (Transform v in t)
            {
                if (v.name != "Visual") continue;   // a rebuilt run briefly has two (old one pending Destroy)
                var mr = v.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                var mats = mr.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i] != Galv && mats[i].name.StartsWith("GB_Galvanized")) { mats[i] = Galv; changed = true; }
                if (changed) mr.sharedMaterials = mats;
            }
        }

        /// <summary>Adjustable round elbows are 4 gores: draw the 3 rotating seams.</summary>
        static void Gores(NetNode n)
        {
            if (n.Ports.Count != 2) return;
            RunSpec spec = null;
            foreach (var p in n.Ports) foreach (var r in p.Runs) if (r != null) spec = r.Spec;
            if (spec == null || spec.Kind != NetKind.Duct || spec.Shape != RunShape.Round) return;
            Vector3 c = n.transform.position, d0 = n.Ports[0].WorldDir, d1 = n.Ports[1].WorldDir;
            if (Vector3.Angle(d0, -d1) < 2f) return;
            var path = MepMesh.ElbowPath(c, d0, d1, Fittings.Leg(spec) * 0.98f, 12);
            var mm = new MepMesh { Origin = c };
            var ring = MepMesh.Circle(spec.Radius * 1.03f, 24);
            for (int g = 1; g <= 3; g++)
            {
                float f = g / 4f * (path.Count - 1);
                int i = Mathf.Clamp(Mathf.RoundToInt(f), 1, path.Count - 2);
                Vector3 t = (path[i + 1] - path[i - 1]).normalized;
                mm.Band(path[i] - t * 0.004f, path[i] + t * 0.004f, ring, 0);
            }
            mm.Spawn(n.transform, "Gores", new[] { Galv });
        }
    }
}
