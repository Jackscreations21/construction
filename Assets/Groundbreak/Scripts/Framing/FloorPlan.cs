using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Platform floor system: sill gasket + PT 2x6 sill on anchor bolts, 2x8 rim/band, 2x8 joists 16" O.C.,
    /// mid-span blocking, glued + nailed 3/4" T&amp;G subfloor with staggered seams on joist centers.</summary>
    public static class FloorPlan
    {
        static float I(float inch) => inch * Units.Inch;

        /// <summary>Root transform at the footprint origin (rotation = footprint yaw). Local x along Length, z across Width.</summary>
        public static Transform Root(Footprint fp, string name)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(new Vector3(fp.Origin.x, fp.Origin.y, fp.Origin.z), fp.Rotation);
            return go.transform;
        }

        public static FramingJob Build(Footprint fp)
        {
            var root = Root(fp, "Floor System");
            var job = FramingJob.Create("Floor deck", root);
            float L = fp.Length, W = fp.Width;
            float sillT = I(1.5f), sillW = I(5.5f), joistD = I(7.25f), t = I(1.5f);
            float y0 = 0f;
            var ghost = (System.Func<Vector3, Vector3, Vector3, System.Func<(Vector3, Vector3, Quaternion)>>)((c, size, fwd) =>
                () => (root.TransformPoint(c), size, root.rotation * Quaternion.LookRotation(fwd)));
            Vector3 X = Vector3.right, Z = Vector3.forward, U = Vector3.up;

            // 1. Chalk lines for the sill: outside edge of framing
            job.Add("Snap chalk lines for the sill plates", ToolSets.Chalk, () =>
            {
                ChalkLine.Snap(root, new Vector3(0, y0, 0), new Vector3(L, y0, 0));
                ChalkLine.Snap(root, new Vector3(L, y0, 0), new Vector3(L, y0, W));
                ChalkLine.Snap(root, new Vector3(L, y0, W), new Vector3(0, y0, W));
                ChalkLine.Snap(root, new Vector3(0, y0, W), new Vector3(0, y0, 0));
            }, ghost(new Vector3(L / 2, 0.01f, 0), new Vector3(L, 0.02f, 0.02f), X));

            // 2. Sills (long sides full length, ends between)
            var sills = new List<(Vector3 c, float len, Vector3 dir)>
            {
                (new Vector3(L / 2, y0 + sillT / 2, sillW / 2), L, X),
                (new Vector3(L / 2, y0 + sillT / 2, W - sillW / 2), L, X),
                (new Vector3(sillW / 2, y0 + sillT / 2, W / 2), W - 2 * sillW, Z),
                (new Vector3(L - sillW / 2, y0 + sillT / 2, W / 2), W - 2 * sillW, Z),
            };
            foreach (var s in sills)
            {
                var sc = s;
                Vector3 across = sc.dir == X ? Z : X;
                int bolts = Mathf.Max(2, Mathf.CeilToInt((sc.len - I(24)) / I(72f)) + 1);
                job.Add("Lay sill gasket + PT 2x6 sill (" + Units.Format(sc.len) + "), drill for anchor bolts", ToolSets.Fasten, () =>
                {
                    var m = Framer.Make(PickStock("pt2x6", sc.len), sc.len, root, sc.c, sc.dir, U, MemberRole.SillPlate, Connector.Nailed | Connector.Gasket | Connector.AnchorBolt, "floor");
                    // gasket: blue foam strip under the sill
                    Framer.AttachMesh(m.transform, "Gasket", (MeshBuilder b, System.Func<Material, int> sub) =>
                        b.AddBox(new Vector3(0, -sillT / 2 - I(0.06f), 0), new Vector3(sillW + I(0.25f), I(0.12f), sc.len), Quaternion.identity, sub(ShellMaterials.Flat("gasket", new Color(0.22f, 0.42f, 0.85f), 0.15f, 0.12f, 0.3f)), Vector3.forward));
                    // anchor bolts: within 12" of the ends, max 6' O.C. (washer + nut on top of the sill)
                    for (int i = 0; i < bolts; i++)
                    {
                        float zz = Mathf.Lerp(-sc.len / 2 + I(12), sc.len / 2 - I(12), bolts == 1 ? 0.5f : i / (float)(bolts - 1));
                        Framer.Hardware(m.transform, "AnchorBolt", new Vector3(0, sillT / 2, zz), Quaternion.identity, (b, g) =>
                        {
                            b.AddCylinder(new Vector3(0, I(0.75f), 0), I(0.25f), I(1.75f), 10, g);
                            b.AddBox(new Vector3(0, I(0.1f), 0), new Vector3(I(2f), I(0.18f), I(2f)), g);
                            b.AddCylinder(new Vector3(0, I(0.45f), 0), I(0.42f), I(0.45f), 6, g);
                        });
                    }
                }, ghost(sc.c, new Vector3(sillW, sillT, sc.len), sc.dir)).Use(PickStock("pt2x6", sc.len)).Use("sill-gasket", Mathf.CeilToInt(Units.ToInches(sc.len) / 12f)).Use("anchor-bolt", bolts).Nails = 0;
            }

            // 3. Rim (band) joists on the long sides, end joists between them
            float ry = y0 + sillT + joistD / 2;
            AddMember(job, root, "Set rim joist (front)", "2x8", L, new Vector3(L / 2, ry, t / 2), X, Z, MemberRole.RimJoist, 6, ghost);
            AddMember(job, root, "Set rim joist (back)", "2x8", L, new Vector3(L / 2, ry, W - t / 2), X, Z, MemberRole.RimJoist, 6, ghost);
            AddMember(job, root, "Set end joist (left)", "2x8", W - 2 * t, new Vector3(t / 2, ry, W / 2), Z, X, MemberRole.Joist, 6, ghost);

            // 4. Joists at 16" O.C. (crowns up), toe-nailed to the sill and end-nailed through the rim
            float oc = I(16f);
            var joistX = new List<float>();
            for (float x = oc; x < L - t - I(2f); x += oc) joistX.Add(x);
            foreach (var x in joistX)
                AddMember(job, root, "Joist @ " + Units.Format(x) + " O.C. (crown up)", "2x8", W - 2 * t, new Vector3(x, ry, W / 2), Z, X, MemberRole.Joist, 6, ghost);
            AddMember(job, root, "Set end joist (right)", "2x8", W - 2 * t, new Vector3(L - t / 2, ry, W / 2), Z, X, MemberRole.Joist, 6, ghost);

            // 5. Mid-span solid blocking (staggered so it can be end-nailed)
            var xs = new List<float> { t / 2 };
            xs.AddRange(joistX); xs.Add(L - t / 2);
            for (int i = 0; i + 1 < xs.Count; i++)
            {
                float a = xs[i] + t / 2, b2 = xs[i + 1] - t / 2;
                float zc = W / 2 + (i % 2 == 0 ? -t / 2 : t / 2);
                AddMember(job, root, "Blocking bay " + (i + 1), "2x8", b2 - a, new Vector3((a + b2) / 2, ry, zc), X, Z, MemberRole.Blocking, 4, ghost);
            }

            // 6. Subfloor: 4x8 panels perpendicular to joists, seams staggered 4' and landing on joist centers, glued + nailed
            float sy = y0 + sillT + joistD + I(0.71875f) / 2;
            float sheetW = I(48f), sheetL = I(96f), gap = I(0.0625f);
            int rows = Mathf.CeilToInt(W / sheetW - 0.01f);
            for (int r = 0; r < rows; r++)
            {
                float z0 = r * sheetW, z1 = Mathf.Min(W, z0 + sheetW);
                float start = 0f;
                var seams = new List<float>();
                float first = r % 2 == 0 ? sheetL : sheetL / 2;
                for (float x = first; x < L - 0.01f; x += sheetL) seams.Add(x);
                seams.Add(L);
                foreach (var x1 in seams)
                {
                    float sx0 = start, sx1 = x1, sz0 = z0, sz1 = z1;
                    float w = (sz1 - sz0) - gap, len = (sx1 - sx0) - gap;
                    var c = new Vector3((sx0 + sx1) / 2, sy, (sz0 + sz1) / 2);
                    job.Add("Glue joists, lay 3/4\" T&G panel (row " + (r + 1) + ")", ToolSets.Fasten, () =>
                    {
                        var m = Framer.Sheet("subfloor34", w, len, root, c, X, U, MemberRole.Subfloor, Connector.Nailed | Connector.Adhesive, "floor");
                        // adhesive beads on the joists under this panel (visible at the edges)
                        Framer.AttachMesh(m.transform, "Adhesive", (MeshBuilder b, System.Func<Material, int> sub) =>
                        {
                            int g = sub(ShellMaterials.Flat("adhesive", new Color(0.82f, 0.72f, 0.48f), 0.5f, 0.08f, 0.2f));
                            foreach (var jx in xs)
                                if (jx > sx0 - 0.001f && jx < sx1 + 0.001f)
                                {
                                    float lx = jx - c.x;
                                    if (Mathf.Abs(lx) > len / 2) continue;
                                    b.AddCylinder(new Vector3(0, -I(0.36f) - I(0.08f), lx), Quaternion.Euler(0, 0, 90), I(0.18f), w - I(1f), 6, g);
                                }
                        });
                    }, ghost(c, new Vector3(w, I(0.75f), len), X)).Use("subfloor34").Use("adhesive", 1).Nails = 30;
                    start = x1;
                }
            }
            return job;
        }

        static void AddMember(FramingJob job, Transform root, string label, string size, float len, Vector3 c, Vector3 dir, Vector3 thin, MemberRole role, int nails,
                              System.Func<Vector3, Vector3, Vector3, System.Func<(Vector3, Vector3, Quaternion)>> ghost)
        {
            string stock = PickStock(size, len);
            var def = Catalog.Get(stock);
            Vector3 gsize = new Vector3(def.Size.x, def.Size.y, len);
            Quaternion r = Quaternion.LookRotation(dir, thin);
            job.Add(label, ToolSets.Fasten, () => Framer.Make(stock, len, root, c, dir, thin, role, Connector.Nailed, "floor"),
                () => (root.TransformPoint(c), gsize, root.rotation * r)).Use(stock).Nails = nails;
        }

        /// <summary>Shortest stock length of a size (e.g. "2x8", "pt2x6", "2x4") that covers len.</summary>
        public static string PickStock(string size, float len)
        {
            string best = null; float bestLen = float.MaxValue;
            foreach (var d in Catalog.All)
            {
                if (d.Category != PieceCategory.Lumber || d.Id == null) continue;
                if (!d.Id.StartsWith(size + "x")) continue;
                if (d.Size.z + 0.0005f >= len && d.Size.z < bestLen) { best = d.Id; bestLen = d.Size.z; }
            }
            if (best == null) Debug.LogWarning("[FloorPlan] no stock " + size + " long enough for " + Units.Format(len));
            return best ?? size + "x16";
        }
    }
}
