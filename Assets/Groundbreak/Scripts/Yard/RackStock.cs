using UnityEngine;

namespace Groundbreak
{
    /// <summary>Stored stock on a pallet-rack beam level, drawn so it reads from the aisle: sheet goods flat on 4x4
    /// dunnage with steel banding, lumber bundles, brick/block cubes and bags/cartons/buckets on 40"x48" pallets, all
    /// pulled to the front beam, plus a yellow load label on the beam face. One merged mesh per load (real sizes,
    /// UVs in meters). Local frame: origin = slot center at deck height, front (aisle) side is -Z.</summary>
    internal static class RackStock
    {
        const float PalletX = 1.016f, PalletZ = 1.219f, PalletH = 0.14f;   // 40" face x 48" deep GMA pallet

        /// <summary>Builds the load for one beam level. room = (usable width, usable height, deck depth) of the slot.</summary>
        public static GameObject Build(Transform parent, string id, PieceDefinition def, Vector3 pos, Vector3 room, int count)
        {
            var p = new SitePart();
            float W = room.x + 0.1f, D = room.z + 0.12f, H = room.y;          // bay clear width, rack depth, clear height
            count = Mathf.Max(1, count);
            Vector3 s = def != null ? def.Size : Vector3.zero;
            bool sized = s.sqrMagnitude > 1e-6f;
            string name = (def != null ? def.Name : Cargo.NameOf(id)) ?? id;
            string lower = (id + " " + name).ToLowerInvariant();

            if (sized && s.y < 0.04f && Mathf.Min(s.x, s.z) > 0.5f) Sheets(p, def, lower, s, W, D, H, count);
            else if (sized && def.Category == PieceCategory.Masonry) Masonry(p, def, s, H, count);
            else if (sized && Mathf.Max(s.x, s.z) > 0.6f && Mathf.Max(s.x, s.y) < 0.3f) Bundle(p, def, s, W, D, H, count);
            else Palletized(p, def, lower, H, count);

            var go = p.Finish(parent, "ShelfLoad " + id);
            if (go != null) go.transform.localPosition = pos;
            Label(parent, pos, D, pos.y > 0.5f, $"{Short(name)}  x{count}");
            return go;
        }

        /// <summary>Yellow load label on the front beam (or a floor card for the bottom level) with the item + count.</summary>
        public static void Label(Transform parent, Vector3 pos, float rackDepth, bool onBeam, string text)
        {
            var p = new SitePart();
            int plate = p.M(SurfaceKind.PaintedWhite, new Color(1f, 0.86f, 0.25f)), edge = p.M(SurfaceKind.MatteBlack);
            float zf = onBeam ? -rackDepth / 2 : -rackDepth / 2 - 0.12f;        // beam face, or just in front of the pallet
            float y0 = onBeam ? -0.108f : 0.0f, y1 = onBeam ? -0.012f : 0.096f;
            float hw = 0.36f;
            p.Box(-hw, hw, y0, y1, zf - 0.004f, zf, plate);
            p.Box(-hw, hw, y0, y0 + 0.008f, zf - 0.005f, zf - 0.004f, edge, MeshBuilder.FaceNegZ);   // bar-code strip
            if (!onBeam) p.Box(-hw + 0.02f, hw - 0.02f, 0f, y0 + 0.004f, zf, zf + 0.03f, edge);      // card stand
            var go = p.Finish(parent, "LoadLabel", false);
            if (go == null) return;
            go.transform.localPosition = pos;
            var t = new GameObject("LoadLabelText"); t.transform.SetParent(go.transform, false);
            t.transform.localPosition = new Vector3(0, (y0 + y1) / 2 + 0.004f, zf - 0.006f);
            t.transform.localRotation = Quaternion.identity;                    // readable from the aisle (-Z)
            var tm = t.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font; tm.text = text; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontSize = 64; tm.characterSize = 0.0072f; tm.color = new Color(0.05f, 0.05f, 0.06f);
            var r = t.GetComponent<MeshRenderer>(); r.sharedMaterial = font.material;
            float width = r.bounds.size.x, maxW = hw * 2 - 0.04f;
            if (width <= 0.001f) width = text.Length * tm.characterSize * tm.fontSize * 0.1f * 0.5f;   // mesh not generated yet: estimate
            if (width > maxW && width > 0f) tm.characterSize *= maxW / width;
        }

        static string Short(string n)
        {
            if (string.IsNullOrEmpty(n)) return "?";
            int paren = n.IndexOf(" (");
            if (paren > 8) n = n.Substring(0, paren);
            return n.Length > 30 ? n.Substring(0, 29) + "." : n;
        }

        // ------------------------------------------------------------------ loads

        /// <summary>4x8 / 4x12 sheets flat on three 4x4 dunnage blocks, long edge along the beams, banded twice.</summary>
        static void Sheets(SitePart p, PieceDefinition def, string lower, Vector3 s, float W, float D, float H, int count)
        {
            bool drywall = lower.Contains("drywall") || lower.StartsWith("dw-");
            SurfaceKind kind = def.Category == PieceCategory.Sheet && !drywall ? Materials.ForDefinition(def) : SurfaceKind.PaintedWhite;
            Color baseTint = drywall ? new Color(0.93f, 0.93f, 0.9f) : (kind == SurfaceKind.PaintedWhite ? def.Color : Color.white);
            int a = p.M(kind, baseTint), b = p.M(kind, new Color(baseTint.r * 0.8f, baseTint.g * 0.8f, baseTint.b * 0.8f, 1f));
            int dun = p.M(SurfaceKind.TreatedLumber), band = p.M(SurfaceKind.MatteBlack);
            int tag = p.M(SurfaceKind.PaintedWhite, drywall ? new Color(0.2f, 0.45f, 0.85f) : new Color(0.15f, 0.6f, 0.25f));
            float len = Mathf.Min(Mathf.Max(s.x, s.z), W - 0.01f), wid = Mathf.Min(s.x, s.z), th = s.y;
            float z0 = -wid / 2, z1 = wid / 2, blk = 0.089f;
            foreach (float bx in new[] { -len / 2 + 0.2f, 0f, len / 2 - 0.2f }) p.Box(bx - 0.045f, bx + 0.045f, 0, blk, z0 + 0.06f, z1 - 0.06f, dun);
            int n = Mathf.Min(count, Mathf.Max(1, Mathf.FloorToInt((H - blk) / th)), 60);
            for (int i = 0; i < n; i++)
                p.Box(-len / 2, len / 2, blk + i * th, blk + (i + 1) * th, z0, z1, i % 2 == 0 ? a : b);
            float top = blk + n * th;
            foreach (float bx in new[] { -len / 4, len / 4 })
            {
                p.Box(bx - 0.016f, bx + 0.016f, top, top + 0.002f, z0 - 0.002f, z1 + 0.002f, band);
                p.Box(bx - 0.016f, bx + 0.016f, blk, top + 0.002f, z0 - 0.003f, z0 - 0.001f, band);
                p.Box(bx - 0.016f, bx + 0.016f, blk, top + 0.002f, z1 + 0.001f, z1 + 0.003f, band);
            }
            p.Box(len / 2 - 0.25f, len / 2 - 0.1f, blk + n * th * 0.2f, blk + Mathf.Max(n * th * 0.8f, n * th * 0.2f + 0.02f), z0 - 0.004f, z0 - 0.003f, tag, MeshBuilder.FaceNegZ);
        }

        /// <summary>Lumber / rebar / trim bundle: sticks along the beams on dunnage, slight gaps so each piece reads, two bands.</summary>
        static void Bundle(SitePart p, PieceDefinition def, Vector3 s, float W, float D, float H, int count)
        {
            int wood = p.M(Materials.ForDefinition(def)), wood2 = p.M(Materials.ForDefinition(def), new Color(0.85f, 0.82f, 0.78f));
            int dun = p.M(SurfaceKind.TreatedLumber), band = p.M(SurfaceKind.MatteBlack), paint = p.M(SurfaceKind.PaintedWhite, new Color(0.9f, 0.2f, 0.15f));
            float len = Mathf.Min(Mathf.Max(s.z, s.x), W - 0.01f), wid = Mathf.Min(s.x, s.z), th = s.y, blk = 0.089f;
            if (th > wid) { float t = th; th = wid; wid = t; }
            float gap = 0.004f, depth = D - 0.06f;
            int across = Mathf.Max(1, Mathf.FloorToInt(depth / (wid + gap)));
            int layers = Mathf.Max(1, Mathf.FloorToInt((H - blk) / (th + gap)));
            int n = Mathf.Min(count, across * layers, 160);
            int rows = Mathf.CeilToInt(n / (float)across);
            float zs = -D / 2 + 0.03f;
            foreach (float bx in new[] { -len / 2 + 0.25f, 0f, len / 2 - 0.25f }) p.Box(bx - 0.045f, bx + 0.045f, 0, blk, -D / 2 + 0.02f, D / 2 - 0.02f, dun);
            for (int i = 0; i < n; i++)
            {
                int c = i % across, l = i / across;
                float z = zs + c * (wid + gap), y = blk + l * (th + gap);
                p.Box(-len / 2, len / 2, y, y + th, z, z + wid, (c + l) % 3 == 0 ? wood2 : wood);
                if (c == 0) p.Box(len / 2 - 0.004f, len / 2, y, y + th, z, z + wid, paint, MeshBuilder.FacePosX);   // end-paint
            }
            float top = blk + rows * (th + gap), zb = zs + Mathf.Min(n, across) * (wid + gap);
            foreach (float bx in new[] { -len / 3, len / 3 })
            {
                p.Box(bx - 0.016f, bx + 0.016f, top, top + 0.002f, zs - 0.002f, zb + 0.002f, band);
                p.Box(bx - 0.016f, bx + 0.016f, blk, top + 0.002f, zs - 0.003f, zs - 0.001f, band);
            }
        }

        /// <summary>Brick / block cubed on a pallet against the front of the bay.</summary>
        static void Masonry(SitePart p, PieceDefinition def, Vector3 s, float H, int count)
        {
            int unit = p.M(Materials.ForDefinition(def)), band = p.M(SurfaceKind.MatteBlack);
            float px = -PalletX / 2 - 0.05f;
            Pallet(p, px, out float py);
            float L = s.z, w = s.x, h = s.y, gap = 0.006f;
            int nx = Mathf.Max(1, Mathf.FloorToInt(PalletX / (L + gap))), nz = Mathf.Max(1, Mathf.FloorToInt(PalletZ / (w + gap)));
            int ny = Mathf.Max(1, Mathf.FloorToInt((H - PalletH) / (h + gap)));
            int n = Mathf.Min(count, nx * nz * ny, 180);
            float x0 = px - nx * (L + gap) / 2, z0 = -PalletZ / 2 + 0.02f;
            for (int i = 0; i < n; i++)
            {
                int ix = i % nx, iz = (i / nx) % nz, iy = i / (nx * nz);
                float x = x0 + ix * (L + gap), z = z0 + iz * (w + gap), y = py + iy * (h + gap);
                p.Box(x, x + L, y, y + h, z, z + w, unit);
            }
            int layers = Mathf.CeilToInt(n / (float)(nx * nz));
            float top = py + layers * (h + gap);
            p.Box(px - 0.012f, px + 0.012f, py, top + 0.002f, z0 - 0.004f, z0 - 0.002f, band);
        }

        /// <summary>Bags, cartons, buckets or sod rolls on a pallet (two pallets for big counts).</summary>
        static void Palletized(SitePart p, PieceDefinition def, string lower, float H, int count)
        {
            Color c = def != null ? def.Color : new Color(0.7f, 0.6f, 0.45f);
            bool bag = lower.Contains("bag") || lower.Contains("mortar") || lower.Contains("mix") || lower.Contains("grout") || (lower.Contains(" lb") && !lower.Contains("screw"));
            bool bucket = lower.Contains("gal") || lower.Contains("bucket") || lower.Contains("pail");
            bool sod = lower.Contains("sod");
            int goods, accent = p.M(SurfaceKind.PaintedWhite, Color.Lerp(c, new Color(0.9f, 0.15f, 0.1f), c.maxColorComponent < 0.35f ? 0.7f : 0.15f));
            float gw, gh, gd;                                   // one unit: width (X), height, depth (Z)
            if (sod) { goods = p.M(SurfaceKind.Grass); gw = 0.6f; gh = 0.2f; gd = 0.4f; }
            else if (bucket) { goods = p.M(SurfaceKind.PaintedWhite, new Color(0.95f, 0.95f, 0.93f)); gw = 0.31f; gh = 0.37f; gd = 0.31f; }
            else if (bag) { goods = p.M(SurfaceKind.PaperBag, Color.Lerp(Color.white, c, 0.35f)); gw = 0.48f; gh = 0.13f; gd = 0.6f; }
            else { goods = p.M(SurfaceKind.PaperBag); gw = 0.32f; gh = 0.22f; gd = 0.29f; }   // corrugated cartons
            int nx = Mathf.Max(1, Mathf.FloorToInt(PalletX / (gw + 0.01f))), nz = Mathf.Max(1, Mathf.FloorToInt(PalletZ / (gd + 0.01f)));
            int ny = Mathf.Max(1, Mathf.FloorToInt((H - PalletH - 0.05f) / (gh + 0.004f)));
            int per = nx * nz * ny;
            int n = Mathf.Min(count, per * 2, 120);
            int pallets = n > per ? 2 : 1;
            for (int pi = 0; pi < pallets; pi++)
            {
                float px = pallets == 1 ? -PalletX / 2 - 0.05f : (pi == 0 ? -PalletX / 2 - 0.05f : PalletX / 2 + 0.05f);
                Pallet(p, px, out float py);
                int m = Mathf.Min(per, n - pi * per);
                float x0 = px - nx * (gw + 0.01f) / 2 + 0.005f, z0 = -PalletZ / 2 + 0.02f;
                for (int i = 0; i < m; i++)
                {
                    int ix = i % nx, iz = (i / nx) % nz, iy = i / (nx * nz);
                    float x = x0 + ix * (gw + 0.01f), z = z0 + iz * (gd + 0.01f), y = py + iy * (gh + 0.004f);
                    if (bucket)
                    {
                        p.CylY(x + gw / 2, z + gd / 2, y, y + gh, gw / 2 - 0.01f, 14, goods);
                        if (iz == 0) p.CylY(x + gw / 2, z + gd / 2, y + gh * 0.35f, y + gh * 0.65f, gw / 2 - 0.006f, 14, accent, false);
                    }
                    else if (sod) p.CylX(y + gh / 2, z + gd / 2, x, x + gw, gh / 2, 10, goods);
                    else
                    {
                        p.Box(x, x + gw, y, y + gh, z, z + gd, goods);
                        if (iz == 0) p.Box(x + gw * 0.15f, x + gw * 0.85f, y + gh * 0.3f, y + gh * 0.7f, z - 0.002f, z, accent, MeshBuilder.FaceNegZ);   // printed label
                    }
                }
            }
        }

        /// <summary>40" x 48" stringer pallet centered at x = cx (front edge overhangs the beam 3", like the real thing).</summary>
        static void Pallet(SitePart p, float cx, out float top)
        {
            int wood = p.M(SurfaceKind.PineLumber, new Color(0.72f, 0.62f, 0.5f));
            float x0 = cx - PalletX / 2, x1 = cx + PalletX / 2, z0 = -PalletZ / 2, z1 = PalletZ / 2;
            for (int i = 0; i < 3; i++) { float x = x0 + i * (PalletX - 0.04f) / 2; p.Box(x, x + 0.04f, 0, 0.09f, z0, z1, wood); }   // stringers
            for (int i = 0; i < 7; i++) { float z = z0 + i * (PalletZ - 0.09f) / 6; p.Box(x0, x1, 0.09f, PalletH, z, z + 0.09f, wood); }   // deck boards
            top = PalletH;
        }
    }
}
