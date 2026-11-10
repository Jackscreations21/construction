using UnityEngine;

namespace Groundbreak
{
    /// <summary>Small mesh helpers for finish goods built in a local frame (x, z = plan, y = up/out).</summary>
    public static class FinishGeom
    {
        /// <summary>A tile/plank: box from (x0,z0) to (x1,z1), bottom y0, top y1, with all four top edges eased by a chamfer
        /// (micro-bevel). uAlongZ puts the texture U along z (grain along a plank running in z).</summary>
        public static void EasedTile(MeshBuilder b, float x0, float z0, float x1, float z1, float y0, float y1, float ch, int sub, bool uAlongZ = false, int sideSub = -1)
        {
            if (sideSub < 0) sideSub = sub;
            ch = Mathf.Clamp(ch, 0f, Mathf.Min(Mathf.Min(x1 - x0, z1 - z0) * 0.3f, (y1 - y0) * 0.8f));
            Vector3 u = uAlongZ ? Vector3.forward : Vector3.right;
            float yt = y1 - ch;
            // top
            b.AddQuad(new Vector3(x0 + ch, y1, z0 + ch), new Vector3(x1 - ch, y1, z0 + ch), new Vector3(x1 - ch, y1, z1 - ch), new Vector3(x0 + ch, y1, z1 - ch), sub, Vector3.up, u);
            if (ch > 0f)
            {
                b.AddQuad(new Vector3(x0, yt, z0), new Vector3(x1, yt, z0), new Vector3(x1 - ch, y1, z0 + ch), new Vector3(x0 + ch, y1, z0 + ch), sub, new Vector3(0, 1, -1), u);
                b.AddQuad(new Vector3(x0, yt, z1), new Vector3(x1, yt, z1), new Vector3(x1 - ch, y1, z1 - ch), new Vector3(x0 + ch, y1, z1 - ch), sub, new Vector3(0, 1, 1), u);
                b.AddQuad(new Vector3(x0, yt, z0), new Vector3(x0, yt, z1), new Vector3(x0 + ch, y1, z1 - ch), new Vector3(x0 + ch, y1, z0 + ch), sub, new Vector3(-1, 1, 0), u);
                b.AddQuad(new Vector3(x1, yt, z0), new Vector3(x1, yt, z1), new Vector3(x1 - ch, y1, z1 - ch), new Vector3(x1 - ch, y1, z0 + ch), sub, new Vector3(1, 1, 0), u);
            }
            // sides
            b.AddQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, yt, z0), new Vector3(x0, yt, z0), sideSub, Vector3.back, u);
            b.AddQuad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, yt, z1), new Vector3(x0, yt, z1), sideSub, Vector3.forward, u);
            b.AddQuad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, yt, z1), new Vector3(x0, yt, z0), sideSub, Vector3.left, u);
            b.AddQuad(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, yt, z1), new Vector3(x1, yt, z0), sideSub, Vector3.right, u);
        }

        /// <summary>Axis-aligned box in local coordinates.</summary>
        public static void Box(MeshBuilder b, float x0, float x1, float y0, float y1, float z0, float z1, int sub, Vector3 u = default)
            => b.AddBox(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)), Quaternion.identity, sub, u);

        /// <summary>Rectangle minus holes as intervals along x for a band [z0,z1] (holes overlapping the band cut it fully).</summary>
        public static System.Collections.Generic.List<Vector2> Subtract(float x0, float x1, float z0, float z1, System.Collections.Generic.IList<Rect> holes)
        {
            var res = new System.Collections.Generic.List<Vector2> { new Vector2(x0, x1) };
            if (holes == null) return res;
            foreach (var h in holes)
            {
                if (h.yMax <= z0 + 1e-4f || h.yMin >= z1 - 1e-4f) continue;
                var next = new System.Collections.Generic.List<Vector2>();
                foreach (var iv in res)
                {
                    if (h.xMax <= iv.x || h.xMin >= iv.y) { next.Add(iv); continue; }
                    if (h.xMin > iv.x) next.Add(new Vector2(iv.x, h.xMin));
                    if (h.xMax < iv.y) next.Add(new Vector2(h.xMax, iv.y));
                }
                res = next;
            }
            return res;
        }
    }
}
