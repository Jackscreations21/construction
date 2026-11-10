using UnityEngine;

namespace Groundbreak
{
    public enum PieceState { Loose, Fastened }

    /// <summary>A placed building piece. The root is never scaled: the BoxCollider is sized to Size and the visual
    /// is a real-size mesh built by ProductModels (UVs in meters, so textures keep real proportions).
    /// Lumber/sheets: long axis = local Z. Products: Size = (width, height, depth), upright.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Piece : MonoBehaviour
    {
        public PieceDefinition Def;
        public Vector3 Size;            // current size (meters); Z may be shorter than stock after cutting
        public PieceState State = PieceState.Loose;
        public int Nails;               // nails driven into this piece

        GameObject _visual;

        public static Piece Create(PieceDefinition def, float length, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(def.Name);
            go.AddComponent<BoxCollider>();
            var p = go.AddComponent<Piece>();
            p.Def = def;
            go.transform.SetPositionAndRotation(position, rotation);
            p.SetSize(new Vector3(def.Size.x, def.Size.y, length > 0 ? length : def.Size.z));
            return p;
        }

        public void SetSize(Vector3 size)
        {
            Size = size;
            transform.localScale = Vector3.one;
            var col = GetComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size = size;
            if (_visual != null) Destroy(_visual);
            _visual = ProductModels.Build(Def, size, transform);
        }

        /// <summary>Position of a world point along this piece's length axis (meters from center).</summary>
        public float LocalZ(Vector3 worldPoint) => transform.InverseTransformPoint(worldPoint).z;

        /// <summary>Cuts along local Z at the given world point. Keeps the part on the side of keepEndSign (+1 = +Z end).
        /// Returns the removed (offcut) length in meters.</summary>
        public float CutAt(Vector3 worldPoint, int keepEndSign)
        {
            float halfLen = Size.z * 0.5f;
            float localZ = Mathf.Clamp(LocalZ(worldPoint), -halfLen, halfLen);
            float keepFrom = keepEndSign > 0 ? localZ : -halfLen;
            float keepTo = keepEndSign > 0 ? halfLen : localZ;
            float newLen = keepTo - keepFrom;
            if (newLen <= 0.001f) return 0f;
            float offcut = Size.z - newLen;
            Vector3 newCenterLocal = new Vector3(0, 0, (keepFrom + keepTo) * 0.5f);
            transform.position = transform.position + transform.rotation * newCenterLocal;
            SetSize(new Vector3(Size.x, Size.y, newLen));
            return offcut;
        }

        public void Fasten()
        {
            State = PieceState.Fastened;
        }
    }
}
