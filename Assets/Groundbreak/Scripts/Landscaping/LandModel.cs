using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>MeshBuilder + material palette (one submesh per material). Used by landscaping, inspector NPC, markers.</summary>
    public class LandModel
    {
        public readonly MeshBuilder B = new MeshBuilder();
        readonly List<Material> _mats = new List<Material>();

        public int M(Material m)
        {
            int i = _mats.IndexOf(m);
            if (i >= 0) return i;
            _mats.Add(m); return _mats.Count - 1;
        }
        public int M(SurfaceKind k) => M(Materials.Get(k));
        public int M(SurfaceKind k, Color tint) => M(Materials.Get(k, tint));
        public int M(LandSurface k) => M(LandMaterials.Get(k));
        public int M(LandSurface k, Color tint) => M(LandMaterials.Get(k, tint));
        public int M(Color c) => M(MaterialLibrary.Get(c));

        public Mesh BuildMesh(string name)
        {
            if (_mats.Count == 0) M(SurfaceKind.PaintedWhite);
            B.MinSubmeshCount = _mats.Count;
            return B.Build(name);
        }

        public Material[] Mats => _mats.ToArray();

        /// <summary>Creates a child object with the mesh. Optional MeshCollider (non-convex) or BoxCollider from mesh bounds.</summary>
        public GameObject Make(Transform parent, string name, bool boxCollider = false)
        {
            var mesh = BuildMesh(name);
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = _mats.ToArray();
            if (boxCollider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = mesh.bounds.center; bc.size = mesh.bounds.size;
            }
            return go;
        }

        // ------------------------------------------------------------------ small shape helpers

        /// <summary>Ellipsoid-ish blob via lathe (radius r, height h, center c). Optional wobble for organic shapes.</summary>
        public static Vector2[] BlobProfile(float r, float h, int rings = 7, float flattenBottom = 0f)
        {
            var p = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float t = i / (float)rings;              // 0 bottom .. 1 top
                float a = Mathf.Lerp(-Mathf.PI / 2f, Mathf.PI / 2f, t);
                float y = Mathf.Sin(a) * h * 0.5f;
                if (y < 0) y *= 1f - flattenBottom;
                p[i] = new Vector2(Mathf.Max(0.0005f, Mathf.Cos(a) * r), y);
            }
            return p;
        }
    }
}
