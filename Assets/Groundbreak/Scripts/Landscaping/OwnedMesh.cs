using UnityEngine;

namespace Groundbreak
{
    /// <summary>Frees a procedurally built mesh when its GameObject is destroyed (destroying a GameObject does not free a
    /// Mesh created with new Mesh()). Added by LandModel.Make and SitePart.Finish, whose meshes are built per object and
    /// never shared. Only the mesh it was given is destroyed, so a filter whose mesh was swapped later is left alone.</summary>
    public class OwnedMesh : MonoBehaviour
    {
        Mesh _mesh;

        public static void Attach(GameObject go, Mesh mesh)
        {
            if (go == null || mesh == null) return;
            go.AddComponent<OwnedMesh>()._mesh = mesh;
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            _mesh = null;
        }
    }
}
