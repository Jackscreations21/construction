using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Blue chalk lines snapped on decks, slabs and roofs (thin quads lying on the surface, no collider).</summary>
    public static class ChalkLine
    {
        public static readonly List<GameObject> Lines = new List<GameObject>();
        public static Material Mat => ShellMaterials.Flat("chalk-blue", new Color(0.18f, 0.42f, 0.92f), 0.05f, 0.35f, 0.15f);

        /// <summary>Line between two points in a parent's local space (surface normal = parent up).</summary>
        public static GameObject Snap(Transform parent, Vector3 a, Vector3 b, float widthIn = 0.12f)
        {
            var go = SnapWorld(parent.TransformPoint(a), parent.TransformPoint(b), widthIn, parent.up);
            return go;
        }

        public static GameObject SnapWorld(Vector3 a, Vector3 b, float widthIn = 0.12f, Vector3 normal = default)
        {
            if (normal == default) normal = Vector3.up;
            Vector3 dir = (b - a); float len = dir.magnitude; if (len < 1e-4f) return null; dir /= len;
            Vector3 side = Vector3.Cross(normal, dir).normalized * Units.In(widthIn) * 0.5f;
            Vector3 lift = normal * 0.0012f;
            var go = Framer.AttachMesh(null, "ChalkLine", (MeshBuilder mb, System.Func<Material, int> sub) =>
            {
                int g = sub(Mat);
                // dusty: a crisp core plus a faint wider halo made of a few broken segments
                mb.AddQuad(a - side + lift, a + side + lift, b + side + lift, b - side + lift, g, normal, dir);
                int segs = Mathf.Max(1, Mathf.RoundToInt(len / 0.4f));
                for (int i = 0; i < segs; i++)
                {
                    if (ShellMaterials.Hash(i, segs, 3) < 0.45f) continue;
                    float t0 = i / (float)segs, t1 = Mathf.Min(1f, t0 + 0.6f / segs);
                    Vector3 p0 = Vector3.Lerp(a, b, t0), p1 = Vector3.Lerp(a, b, t1);
                    Vector3 s2 = side * (2.2f + ShellMaterials.Hash(i, 1, 9));
                    mb.AddQuad(p0 - s2 + lift * 0.6f, p0 + s2 + lift * 0.6f, p1 + s2 + lift * 0.6f, p1 - s2 + lift * 0.6f, g, normal, dir);
                }
            });
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Lines.Add(go);
            return go;
        }
    }

    /// <summary>Carpenter's layout on the plates: a square line at each stud edge and an X on the side the stud goes,
    /// "J"/"K"/"C" for jacks, kings and cripples, drawn with a lumber crayon / pencil on the plate edges.</summary>
    public static class LayoutMarks
    {
        static Material Pencil => ShellMaterials.Flat("pencil", new Color(0.12f, 0.12f, 0.14f), 0.25f, 0.1f, 0.2f);
        static Material Crayon => ShellMaterials.Flat("crayon-red", new Color(0.75f, 0.12f, 0.10f), 0.2f, 0.15f, 0.2f);

        public static void Draw(Transform bottomPlate, Transform topPlate, Transform wallRoot, WallSpec s, List<float> marks, List<(float x0, float x1, MemberRole role)> sticks)
        {
            // Draw on the exterior edge of both plates (it faces up while the wall lies flat), in wall-local inches.
            float zFace = -0.01f; // just outside the exterior face
            float[] plateY = { 0.75f, 1.5f + s.StudIn + 0.75f };
            var go = Framer.AttachMesh(wallRoot, "LayoutMarks", (MeshBuilder b, System.Func<Material, int> sub) =>
            {
                int pen = sub(Pencil), red = sub(Crayon);
                foreach (var st in sticks)
                {
                    bool special = st.role != MemberRole.Stud;
                    foreach (var py in plateY)
                    {
                        if (st.role == MemberRole.Cripple || st.role == MemberRole.RoughSill || st.role == MemberRole.Header) { if (py > 2f && st.role != MemberRole.Cripple) continue; }
                        Line(b, pen, new Vector2(st.x0, py - 0.7f), new Vector2(st.x0, py + 0.7f), zFace);
                        Line(b, pen, new Vector2(st.x1, py - 0.7f), new Vector2(st.x1, py + 0.7f), zFace);
                        if (st.role == MemberRole.RoughSill || st.role == MemberRole.Header) continue;
                        // X between the lines (or a letter for jack / king / cripple)
                        Vector2 c = new Vector2((st.x0 + st.x1) / 2, py);
                        float hw = (st.x1 - st.x0) * 0.4f, hh = 0.55f;
                        if (st.role == MemberRole.JackStud) { Line(b, red, c + new Vector2(hw * 0.3f, hh), c + new Vector2(hw * 0.3f, -hh), zFace); Line(b, red, c + new Vector2(hw * 0.3f, -hh), c + new Vector2(-hw, -hh * 0.6f), zFace); }
                        else if (st.role == MemberRole.KingStud) { Line(b, red, c + new Vector2(-hw, hh), c + new Vector2(-hw, -hh), zFace); Line(b, red, c + new Vector2(-hw, 0), c + new Vector2(hw, hh), zFace); Line(b, red, c + new Vector2(-hw, 0), c + new Vector2(hw, -hh), zFace); }
                        else if (st.role == MemberRole.Cripple) { Line(b, red, c + new Vector2(hw, hh), c + new Vector2(-hw, hh), zFace); Line(b, red, c + new Vector2(-hw, hh), c + new Vector2(-hw, -hh), zFace); Line(b, red, c + new Vector2(-hw, -hh), c + new Vector2(hw, -hh), zFace); }
                        else { Line(b, special ? red : pen, c + new Vector2(-hw, -hh), c + new Vector2(hw, hh), zFace); Line(b, special ? red : pen, c + new Vector2(-hw, hh), c + new Vector2(hw, -hh), zFace); }
                    }
                }
            });
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Line(MeshBuilder b, int sub, Vector2 p, Vector2 q, float z)
        {
            Vector3 a = new Vector3(p.x, p.y, 0) * Units.Inch, c = new Vector3(q.x, q.y, 0) * Units.Inch;
            a.z = c.z = z * Units.Inch;
            Vector3 dir = (c - a).normalized;
            Vector3 side = Vector3.Cross(Vector3.forward, dir) * Units.In(0.05f);
            b.AddQuad(a - side, a + side, c + side, c - side, sub, Vector3.back, dir);
        }
    }
}
