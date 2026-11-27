using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// The kitchen and bath finishes of an owned flip, so the "New kitchen look" and "Bathroom refresh" upgrades show.
    /// Dated: orange-stained raised-panel oak door and drawer fronts with brass hardware and an almond laminate counter; almond
    /// 4-1/4" tile with a pink accent row on the tub walls. Renovated: white painted shaker fronts with satin-nickel bar pulls and a
    /// light quartz counter; white 3x6 subway tile in a running bond. Thin overlays a few mm in front of the house's own cabinets,
    /// counter and tub surround (HouseBuilder.Kitchen / Bath layout, found from the KitchenSink and TubCaulk anchors), real sizes,
    /// no colliders. Built by <see cref="FlipVisuals.Refresh"/> from the items' done flags.
    /// </summary>
    public static class FlipInterior
    {
        public const string KitchenDated = "Flip kitchen (dated)", KitchenNew = "Flip kitchen (renovated)",
            BathDated = "Flip bath (dated)", BathNew = "Flip bath (renovated)";

        static bool Has(FlipHouse f, string item) => f.items.Exists(i => i.id == item);
        static bool Done(FlipHouse f, string item) { var it = f.items.Find(i => i.id == item); return it != null && it.done; }
        static Vector3 Local(TownHouse h, HouseSpot s) => h.transform.InverseTransformPoint(h.Anchor(s).position);

        /// <summary>Adds the kitchen / bath dressing under the flip's root (owned or for-sale flips only).</summary>
        public static void Build(FlipHouse f, TownHouse house, Transform parent)
        {
            if (f == null || house == null || parent == null) return;
            if (f.Status != FlipStatus.Owned && f.Status != FlipStatus.ForSale) return;
            if (Has(f, "kitchen") && house.Has(HouseSpot.KitchenSink))
            {
                bool done = Done(f, "kitchen");
                var k = new LandModel();
                Kitchen(k, house, done);
                Place(k, house, parent, done ? KitchenNew : KitchenDated);
            }
            if (Has(f, "bath") && house.Has(HouseSpot.TubCaulk))
            {
                bool done = Done(f, "bath");
                var k = new LandModel();
                Bath(k, house, done);
                Place(k, house, parent, done ? BathNew : BathDated);
            }
        }

        static void Place(LandModel k, TownHouse house, Transform parent, string name)
        {
            if (k.B.VertexCount == 0) return;
            var go = k.Make(parent, name);                    // no collider: it must not block the repair raycasts
            go.transform.SetPositionAndRotation(house.transform.position, house.transform.rotation);
        }

        static void Box(LandModel k, Vector3 min, Vector3 max, int mat, Vector3 uAxis = default) =>
            k.B.AddBox((min + max) / 2f, max - min, Quaternion.identity, mat, uAxis);

        // ------------------------------------------------------------------ kitchen

        // HouseBuilder.Kitchen: base boxes 24" deep (front at zf), 34-1/2" to the deck, 1-1/2" top, sink base open (18" each side of
        // the sink), uppers 12" deep from 54" to 84" (not over the sink).
        static void Kitchen(LandModel k, TownHouse h, bool renovated)
        {
            var a = Local(h, HouseSpot.KitchenSink);
            float F = a.y, xs = a.x, zf = a.z, zb = zf - 0.61f;
            float x0 = -h.Width / 2f + HouseBuilder.T, x1 = -h.Width / 2f + 3.3f;
            float ct = F + 0.876f, ctT = ct + 0.038f, s0 = xs - 0.457f, s1 = xs + 0.457f;

            int door = renovated ? k.M(SurfaceKind.PaintedWhite, new Color(0.98f, 0.98f, 0.96f)) : k.M(SurfaceKind.Oak, new Color(1.0f, 0.68f, 0.40f));
            int pull = renovated ? k.M(SurfaceKind.Nickel) : k.M(SurfaceKind.Brass);
            int top = renovated ? k.M(FinishMaterials.Get(InteriorMaterials.Quartz)) : k.M(FinishMaterials.Get(InteriorMaterials.Laminate, new Color(0.97f, 0.9f, 0.74f)));

            // base runs either side of the (open) sink base, laid out from the sink outward
            BaseRun(k, s1, x1 - s1, +1, F, ct, zf, door, pull, renovated);
            BaseRun(k, s0, s0 - x0, -1, F, ct, zf, door, pull, renovated);
            // upper runs
            UpperRun(k, xs + 0.55f, x1 - (xs + 0.55f), +1, F, zb + 0.31f, door, pull, renovated);
            UpperRun(k, xs - 0.55f, (xs - 0.55f) - x0, -1, F, zb + 0.31f, door, pull, renovated);

            // counter overlay (3 mm, 1 mm above the house's top), same sink cutout as the house
            float zc = (zb + zf + 0.025f) / 2f, bx = 0.38f, bz = 0.225f, y0 = ctT + 0.001f, y1 = ctT + 0.004f, zfront = zf + 0.025f;
            Box(k, new Vector3(x0, y0, zb), new Vector3(xs - bx, y1, zfront), top);
            Box(k, new Vector3(xs + bx, y0, zb), new Vector3(x1, y1, zfront), top);
            Box(k, new Vector3(xs - bx, y0, zb), new Vector3(xs + bx, y1, zc - bz), top);
            Box(k, new Vector3(xs - bx, y0, zc + bz), new Vector3(xs + bx, y1, zfront), top);
            Box(k, new Vector3(x0, ct - 0.002f, zfront + 0.001f), new Vector3(x1 + 0.004f, y1, zfront + 0.004f), top, Vector3.right);   // front edge
            Box(k, new Vector3(x1 + 0.001f, ct - 0.002f, zb), new Vector3(x1 + 0.004f, y1, zfront + 0.001f), top, Vector3.forward);    // end
        }

        /// <summary>Door widths from the sink outward: a 16" door next to the sink base (where the catalog cabinet door sits), the
        /// rest split into ~18" doors.</summary>
        static float[] Doors(float len)
        {
            if (len < 0.12f) return new float[0];
            if (len < 0.6f) return new[] { len };
            int n = Mathf.Max(1, Mathf.RoundToInt((len - 0.4f) / 0.45f));
            var w = new float[n + 1];
            w[0] = 0.4f;
            for (int i = 1; i <= n; i++) w[i] = (len - 0.4f) / n;
            return w;
        }

        static void BaseRun(LandModel k, float start, float len, int dir, float F, float ct, float zf, int door, int pull, bool renovated)
        {
            var ws = Doors(len);
            float u = 0f;
            for (int i = 0; i < ws.Length; i++)
            {
                float u0 = u, u1 = u + ws[i]; u = u1;
                float cx = start + dir * (u0 + u1) / 2f, w = ws[i] - 0.003f;
                // drawer front (6") over a door, on a 4" toe kick
                Front(k, cx, w, ct - 0.148f, ct - 0.004f, zf, door, renovated);
                Front(k, cx, w, F + 0.104f, ct - 0.152f, zf, door, renovated);
                // pulls: doors pair up (door 0 latches on its far side, door 1 on its near side, ...); bar pulls 21-3/4" up,
                // the same place as the catalog cabinet door's pull so it is covered
                float latch = (i % 2 == 0) ? u1 - 0.057f : u0 + 0.057f;
                Bar(k, new Vector3(start + dir * latch, F + 0.55f, zf), renovated ? 0.128f : 0.106f, true, pull);
                if (renovated) Bar(k, new Vector3(cx, ct - 0.076f, zf), 0.128f, false, pull);
                else Knob(k, new Vector3(cx, ct - 0.076f, zf), pull);
            }
        }

        static void UpperRun(LandModel k, float start, float len, int dir, float F, float zFace, int door, int pull, bool renovated)
        {
            if (len < 0.12f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 0.4f));
            float dw = len / n;
            for (int i = 0; i < n; i++)
            {
                float u0 = i * dw, u1 = u0 + dw, cx = start + dir * (u0 + u1) / 2f;
                Front(k, cx, dw - 0.003f, F + 1.373f, F + 2.127f, zFace, door, renovated);
                float latch = (i % 2 == 0) ? u1 - 0.05f : u0 + 0.05f;
                var p = new Vector3(start + dir * latch, F + 1.37f + 0.11f, zFace);
                if (renovated) Bar(k, p, 0.128f, true, pull); else Knob(k, p, pull);
            }
        }

        /// <summary>One 3/4" door or drawer front on a cabinet face at z = zFace (house +Z). Shaker: 2-1/4" rails and stiles around a
        /// 1/4" recessed panel. Dated: slab with a raised field.</summary>
        static void Front(LandModel k, float cx, float w, float y0, float y1, float zFace, int mat, bool shaker)
        {
            float z0 = zFace + 0.002f, z1 = z0 + 0.019f, x0 = cx - w / 2f, x1 = cx + w / 2f, r = Mathf.Min(0.057f, Mathf.Min(w, y1 - y0) * 0.3f);
            if (shaker)
            {
                // full-face panel (hides the catalog's oak door behind it) with the rails and stiles 1/4" proud
                Box(k, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), mat, Vector3.up);
                float z2 = z1 + 0.006f;
                Box(k, new Vector3(x0, y0, z1), new Vector3(x0 + r, y1, z2), mat, Vector3.up);               // stiles
                Box(k, new Vector3(x1 - r, y0, z1), new Vector3(x1, y1, z2), mat, Vector3.up);
                Box(k, new Vector3(x0 + r, y0, z1), new Vector3(x1 - r, y0 + r, z2), mat, Vector3.right);   // rails
                Box(k, new Vector3(x0 + r, y1 - r, z1), new Vector3(x1 - r, y1, z2), mat, Vector3.right);
            }
            else
            {
                Box(k, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), mat, Vector3.up);
                float ins = r + 0.006f;
                if (x1 - x0 > 2 * ins + 0.02f && y1 - y0 > 2 * ins + 0.02f)
                    Box(k, new Vector3(x0 + ins, y0 + ins, z1), new Vector3(x1 - ins, y1 - ins, z1 + 0.005f), mat, Vector3.up);   // raised field
            }
        }

        /// <summary>Bar pull (vertical or horizontal) standing 3/8" off the door: a 5/8" bar on two posts.</summary>
        static void Bar(LandModel k, Vector3 at, float len, bool vertical, int mat)
        {
            float face = at.z + 0.021f, zc = at.z + 0.034f;
            var size = vertical ? new Vector3(0.016f, len, 0.016f) : new Vector3(len, 0.016f, 0.016f);
            k.B.AddBox(new Vector3(at.x, at.y, zc), size, Quaternion.identity, mat);
            for (int s = -1; s <= 1; s += 2)
            {
                var off = vertical ? new Vector3(0, s * (len / 2f - 0.012f), 0) : new Vector3(s * (len / 2f - 0.012f), 0, 0);
                k.B.AddBox(new Vector3(at.x, at.y, (face + zc) / 2f) + off, new Vector3(0.008f, 0.008f, zc - face), Quaternion.identity, mat);
            }
        }

        /// <summary>1-1/4" round knob on the door face.</summary>
        static void Knob(LandModel k, Vector3 at, int mat)
        {
            float face = at.z + 0.021f;
            k.B.AddCylinder(new Vector3(at.x, at.y, face + 0.012f), Quaternion.Euler(90f, 0f, 0f), 0.008f, 0.024f, 10, mat);
            k.B.AddCylinder(new Vector3(at.x, at.y, face + 0.026f), Quaternion.Euler(90f, 0f, 0f), 0.016f, 0.01f, 14, mat);
        }

        // ------------------------------------------------------------------ bath

        // HouseBuilder.Bath: 60" x 30" alcove tub in the back-right corner, rim 16" up, 8 mm tile surround to 6' on the back wall
        // (z = zb + 8 mm) and the end wall (x = tx1 - 8 mm). TubCaulk anchor = (tx1, rim, zb + 30").
        static void Bath(LandModel k, TownHouse h, bool renovated)
        {
            var a = Local(h, HouseSpot.TubCaulk);
            float tx1 = a.x, rim = a.y, zb = a.z - 0.762f, F = rim - 0.406f, tx0 = tx1 - 0.762f, tz1 = zb + 1.524f;
            float H = F + 1.83f - rim, face = 0.008f, depth = 0.007f;
            int grout, tile, accent;
            if (renovated)
            {
                tile = k.M(FinishMaterials.Get(InteriorMaterials.Subway));
                accent = tile;
                grout = k.M(new Color(0.88f, 0.88f, 0.86f));
            }
            else
            {
                tile = k.M(SurfaceKind.Porcelain, new Color(0.93f, 0.85f, 0.70f));      // almond
                accent = k.M(SurfaceKind.Porcelain, new Color(0.92f, 0.64f, 0.66f));    // dusty-rose accent row
                grout = k.M(new Color(0.50f, 0.46f, 0.38f));                           // dingy grout
            }
            float tw = renovated ? 0.1524f : 0.108f, th = renovated ? 0.0762f : 0.108f;
            System.Func<int, int> rowMat = r => (!renovated && r == 9) ? accent : tile;
            // back wall: tx0 .. up to the end wall's new tile
            TileWall(k, new Vector3(tx0, rim, zb + face), Vector3.right, Vector3.forward, (tx1 - face - depth) - tx0, H, tw, th, renovated, rowMat, grout, Rect.zero);
            // end wall: leave the catalog regrout patch (6x6 of 6" tiles on the joint frame) showing while the bath is dated
            float zs = zb + face + depth;
            var skip = renovated ? Rect.zero : Rect.MinMaxRect(zb + 0.762f - 0.53f - zs, 0f, zb + 0.762f + 0.39f - zs, 0.92f);
            TileWall(k, new Vector3(tx1 - face, rim, zs), Vector3.forward, Vector3.left, tz1 - zs, H, tw, th, renovated, rowMat, grout, skip);
        }

        /// <summary>Tiles on a wall from o (bottom corner on the existing surface) along u, facing n: each tile 4 mm thick on a 2 mm grout
        /// bed, 1/8" joints, clipped at the edges; running bond if asked. Tiles touching the skip rect (u, v) are left off.</summary>
        static void TileWall(LandModel k, Vector3 o, Vector3 u, Vector3 n, float W, float H, float tw, float th, bool bond,
            System.Func<int, int> rowMat, int grout, Rect skip)
        {
            if (W <= 0f || H <= 0f) return;
            var rot = Quaternion.LookRotation(n, Vector3.up);       // box x = u, y = up, z = n
            int rows = Mathf.CeilToInt(H / th - 0.01f);
            for (int r = 0; r < rows; r++)
            {
                float v0 = r * th, v1 = Mathf.Min(H, v0 + th);
                float shift = bond && (r % 2 == 1) ? -tw / 2f : 0f;
                for (float c0 = shift; c0 < W - 0.002f; c0 += tw)
                {
                    float u0 = Mathf.Max(0f, c0), u1 = Mathf.Min(W, c0 + tw);
                    if (u1 - u0 < 0.006f || v1 - v0 < 0.006f) continue;
                    if (skip.width > 0f && u1 > skip.xMin && u0 < skip.xMax && v1 > skip.yMin && v0 < skip.yMax) continue;
                    var c = o + u * ((u0 + u1) / 2f) + Vector3.up * ((v0 + v1) / 2f);
                    k.B.AddBox(c + n * 0.002f, new Vector3(u1 - u0, v1 - v0, 0.002f), rot, grout);
                    k.B.AddBox(c + n * 0.005f, new Vector3(u1 - u0 - 0.003f, v1 - v0 - 0.003f, 0.004f), rot, rowMat(r), Vector3.right);
                }
            }
        }
    }
}
