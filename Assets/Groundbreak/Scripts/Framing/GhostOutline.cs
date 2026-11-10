using UnityEngine;

namespace Groundbreak
{
    /// <summary>Crisp edges on the blue layout ghost so the target reads against busy framing: a thin unlit line around the
    /// box's 12 edges, gently pulsing. Width stays constant in world units (LineRenderer widths ignore transform scale).</summary>
    public static class GhostOutline
    {
        static Material _mat;
        static readonly Vector3[] Path =
        {
            // bottom loop, up, top loop, then the three remaining verticals (drawn as back-and-forth spurs)
            new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,-.5f,.5f), new Vector3(-.5f,-.5f,.5f), new Vector3(-.5f,-.5f,-.5f),
            new Vector3(-.5f,.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f),
            new Vector3(.5f,.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f),
            new Vector3(-.5f,.5f,.5f), new Vector3(-.5f,-.5f,.5f), new Vector3(-.5f,.5f,.5f), new Vector3(-.5f,.5f,-.5f),
        };

        public static void Update(GameObject ghost)
        {
            if (ghost == null) return;
            var t = ghost.transform.Find("Outline");
            LineRenderer lr;
            if (t == null)
            {
                var go = new GameObject("Outline") { layer = ghost.layer };
                go.transform.SetParent(ghost.transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = Path.Length;
                lr.SetPositions(Path);
                lr.widthMultiplier = 0.006f;
                lr.numCornerVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                if (_mat == null)
                {
                    var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                    _mat = new Material(sh) { name = "GB_GhostOutline", hideFlags = HideFlags.DontUnloadUnusedAsset };
                }
                lr.sharedMaterial = _mat;
            }
            else lr = t.GetComponent<LineRenderer>();
            if (lr == null) return;
            float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
            var c = new Color(0.55f, 0.85f, 1f, pulse);
            lr.startColor = c; lr.endColor = c;
        }
    }
}
