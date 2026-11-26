using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>One procedural mesh under construction for the yard premises: a MeshBuilder plus its material palette
    /// (material -> submesh index). Everything is authored at true size in meters; UVs come out in meters.</summary>
    internal sealed class SitePart
    {
        public readonly MeshBuilder B = new MeshBuilder();
        readonly List<Material> _mats = new List<Material>();
        readonly Dictionary<Material, int> _index = new Dictionary<Material, int>();

        public int Mat(Material m)
        {
            if (m == null) m = Materials.Get(SurfaceKind.PaintedWhite);
            if (!_index.TryGetValue(m, out int i)) { i = _mats.Count; _mats.Add(m); _index[m] = i; }
            return i;
        }

        public int M(SurfaceKind kind) => Mat(Materials.Get(kind));
        public int M(SurfaceKind kind, Color tint) => Mat(Materials.Get(kind, tint));

        /// <summary>Axis-aligned box from min/max coordinates (in the current MeshBuilder.Transform frame).</summary>
        public void Box(float x0, float x1, float y0, float y1, float z0, float z1, int sub, int mask = MeshBuilder.AllFaces)
        {
            B.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                     new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), Quaternion.identity, sub, Vector3.zero, mask);
        }

        public void CylY(float x, float z, float y0, float y1, float r, int segs, int sub, bool cap = true)
            => B.AddCylinder(new Vector3(x, (y0 + y1) * 0.5f, z), Quaternion.identity, r, Mathf.Abs(y1 - y0), segs, sub, cap);

        public void CylX(float y, float z, float x0, float x1, float r, int segs, int sub, bool cap = true)
            => B.AddCylinder(new Vector3((x0 + x1) * 0.5f, y, z), Quaternion.Euler(0f, 0f, 90f), r, Mathf.Abs(x1 - x0), segs, sub, cap);

        public void CylZ(float x, float y, float z0, float z1, float r, int segs, int sub, bool cap = true)
            => B.AddCylinder(new Vector3(x, y, (z0 + z1) * 0.5f), Quaternion.Euler(90f, 0f, 0f), r, Mathf.Abs(z1 - z0), segs, sub, cap);

        /// <summary>Square/rectangular bar from a to b.</summary>
        public void Strut(Vector3 a, Vector3 b, float w, float h, int sub)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 up = Mathf.Abs(d.y) > 0.95f * len ? Vector3.forward : Vector3.up;
            B.AddBox((a + b) * 0.5f, new Vector3(w, h, len), Quaternion.LookRotation(d / len, up), sub, Vector3.forward);
        }

        public Mesh BuildMesh(string name, out Material[] mats)
        {
            if (_mats.Count == 0) M(SurfaceKind.PaintedWhite);
            B.ResetTransform();
            B.MinSubmeshCount = _mats.Count;
            mats = _mats.ToArray();
            return B.Build(name);
        }

        /// <summary>Creates the GameObject (MeshFilter + MeshRenderer). Returns null if nothing was added.</summary>
        public GameObject Finish(Transform parent, string name, bool shadows = true)
        {
            if (B.VertexCount == 0) return null;
            Mesh mesh = BuildMesh(name, out Material[] mats);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }
}
