using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Up to three favourite photos stand in 8x10 desk frames on the office desk at the company premises
    /// (YardSite.OfficeDesk; rebuilt when the premises or the favourites change).</summary>
    public class OfficeFrames : MonoBehaviour
    {
        public const int MaxFrames = 3;
        const float In = 0.0254f;
        GameObject _root;
        YardSite _site;
        string _key = "";
        float _next;
        readonly List<Material> _mats = new List<Material>();
        readonly List<Texture2D> _texs = new List<Texture2D>();

        public static OfficeFrames I;
        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; Clear(); }

        /// <summary>Favourites shown on the desk, newest first.</summary>
        public static List<PhotoEntry> Shown()
        {
            var l = new List<PhotoEntry>();
            for (int i = PhotoStore.Photos.Count - 1; i >= 0 && l.Count < MaxFrames; i--) if (PhotoStore.Photos[i].Favourite) l.Add(PhotoStore.Photos[i]);
            return l;
        }

        public GameObject Root => _root;

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            Refresh();
        }

        public void Refresh()
        {
            var site = YardSite.I;
            var shown = Shown();
            string key = site != null ? "site:" : "none";
            foreach (var e in shown) key += e.Id + ",";
            if (key == _key && site == _site && (site == null || _root != null)) return;
            _key = key;
            Clear();
            _site = site;
            if (site == null || shown.Count == 0 || site.OfficeDesk == Vector3.zero) return;

            _root = new GameObject("OfficePhotoFrames");
            _root.transform.SetParent(site.transform, false);
            _root.transform.position = site.OfficeDesk;
            _root.transform.rotation = site.transform.rotation;
            // desk top is 30" (0.76 m) high; the chair side is local +Z, so frames face +Z from the back edge of the desk
            float[] xs = { -0.56f, 0.52f, 0.36f };
            float[] yaws = { 18f, -16f, -6f };
            for (int i = 0; i < shown.Count; i++)
            {
                var tex = PhotoThumbs.Load(shown[i]);
                if (tex == null) continue;
                _texs.Add(tex);
                BuildFrame(_root.transform, new Vector3(xs[i], 0.76f, -0.22f + (i == 2 ? 0.12f : 0f)), yaws[i], tex, i == 2 ? 0.6f : 1f);
            }
        }

        void BuildFrame(Transform parent, Vector3 local, float yaw, Texture2D tex, float scale)
        {
            // 8x10 print in a 1" moulding = 12" x 10" outside, 3/4" deep; leans back 12 degrees on an easel leg
            float w = (10f + 2f) * In * scale, h = (8f + 2f) * In * scale, m = 1f * In * scale, d = 0.75f * In;
            var go = new GameObject("DeskFrame");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(12f, 180f + yaw, 0f);

            var b = new MeshBuilder();
            b.AddBox(new Vector3(0, h - m / 2, 0), new Vector3(w, m, d), 0);           // top rail
            b.AddBox(new Vector3(0, m / 2, 0), new Vector3(w, m, d), 0);               // bottom rail
            b.AddBox(new Vector3(-w / 2 + m / 2, h / 2, 0), new Vector3(m, h - 2 * m, d), 0);
            b.AddBox(new Vector3(w / 2 - m / 2, h / 2, 0), new Vector3(m, h - 2 * m, d), 0);
            b.AddBox(new Vector3(0, h / 2, d / 2 - 0.002f), new Vector3(w - 2 * m, h - 2 * m, 0.003f), 1);   // hardboard back
            var leg = Quaternion.Euler(-28f, 0f, 0f);
            b.AddBox(new Vector3(0, h * 0.42f, d / 2 + 0.03f), new Vector3(0.03f * scale, h * 0.8f, 0.004f), leg, 1);
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = b.Build("DeskFrame");
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { Materials.Get(SurfaceKind.Oak), Materials.Get(SurfaceKind.DarkPlastic) };

            // the print itself (UVs 0..1 over the opening), just behind the front face so the moulding overlaps it
            var pic = new GameObject("Print");
            pic.transform.SetParent(go.transform, false);
            float pw = w - 2 * m, ph = h - 2 * m, z = -d / 2 + 0.004f;
            var mesh = new Mesh { name = "Print" };
            mesh.vertices = new[] { new Vector3(-pw / 2, m, z), new Vector3(pw / 2, m, z), new Vector3(pw / 2, m + ph, z), new Vector3(-pw / 2, m + ph, z) };
            // crop the 16:9 photo to the 5:4 opening
            float u0 = 0.5f - (pw / ph) * (9f / 16f) * 0.5f, u1 = 1f - u0;
            mesh.uv = new[] { new Vector2(u1, 0), new Vector2(u0, 0), new Vector2(u0, 1), new Vector2(u1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            pic.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(Shader.Find("Standard")) { mainTexture = tex };
            mat.SetFloat("_Glossiness", 0.75f);          // glass over the print
            _mats.Add(mat);
            pic.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void Clear()
        {
            if (_root != null)
            {
                // the frame and print meshes are built per frame: free them with the objects
                foreach (var mf in _root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(_root);
            }
            _root = null;
            foreach (var m in _mats) if (m != null) Destroy(m);
            _mats.Clear();
            foreach (var t in _texs) if (t != null) Destroy(t);
            _texs.Clear();
        }
    }
}
