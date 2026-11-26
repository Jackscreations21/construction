using UnityEngine;

namespace Groundbreak
{
    /// <summary>Marks an I-joist end that frames into a girder: hung in a face-mount hanger, web stiffeners nailed in.
    /// Read by the I-joist inspection check.</summary>
    public class IJoistEnd : MonoBehaviour
    {
        public bool Hanger;
        public bool Stiffened;
    }
}
