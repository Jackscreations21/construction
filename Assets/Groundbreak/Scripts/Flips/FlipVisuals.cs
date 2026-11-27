using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// What a distressed house looks like from the street: knee-high grass and weeds, saplings, a boarded window, a blue tarp on a
    /// leaking roof, the yard sign (bank-owned / under renovation / for sale / sold) and a 20-yard roll-off dumpster while it's being
    /// renovated. Rebuilt from the flip's state (one merged mesh per house, no colliders except the dumpster).
    /// </summary>
    public static class FlipVisuals
    {
        static readonly Dictionary<string, GameObject> _roots = new Dictionary<string, GameObject>();

        public static GameObject RootOf(FlipHouse f) => f != null && _roots.TryGetValue(f.id, out var g) ? g : null;

        public static void Clear(FlipHouse f)
        {
            if (f == null || !_roots.TryGetValue(f.id, out var g)) return;
            _roots.Remove(f.id);
            if (g != null) Object.Destroy(g);
        }

        public static void ClearAll()
        {
            foreach (var g in _roots.Values) if (g != null) Object.Destroy(g);
            _roots.Clear();
        }

        static bool Done(FlipHouse f, string item) { var it = f.items.Find(i => i.id == item); return it == null || it.done; }
        static bool Has(FlipHouse f, string item) => f.items.Exists(i => i.id == item);

        static bool StepDone(FlipHouse f, string item, string step)
        {
            var it = f.items.Find(i => i.id == item);
            if (it == null || it.done) return true;
            foreach (var s in it.defects) if (s.done != null && s.done.Contains(step)) return true;
            return false;
        }

        /// <summary>Redraws the house's dressing (needs the 3D town; does nothing without it).</summary>
        public static void Refresh(FlipHouse f)
        {
            Clear(f);
            if (f == null || !Town.Exists) return;
            if (f.Status == FlipStatus.Listed && !Flips.Unlocked) return;       // market listings only show in a career that can flip
            var lot = f.Lot; if (lot == null) return;
            var house = lot.House;
            var go = new GameObject("Flip " + f.address);
            go.transform.SetParent(Town.Root, false);
            _roots[f.id] = go;

            var k = new LandModel();
            var rng = new System.Random(f.id.GetHashCode() & 0x7fff);
            float R() => (float)rng.NextDouble();
            Vector3 front = lot.Front, side = Vector3.Cross(Vector3.up, front).normalized;
            float driveSide = Mathf.Sign(Vector3.Dot(lot.Parking - lot.FrontMid, side));
            if (driveSide == 0f) driveSide = 1f;
            bool sold = f.Status == FlipStatus.Sold;

            // ---- overgrowth: tall grass tufts, weeds, saplings (until the trash-out is done)
            if (!sold && !Done(f, "yard"))
            {
                int grass = k.M(LandSurface.Foliage, new Color(0.72f, 0.78f, 0.45f)), dry = k.M(LandSurface.Straw, new Color(0.85f, 0.78f, 0.55f)),
                    weed = k.M(LandSurface.Foliage, new Color(0.5f, 0.7f, 0.35f));
                bool weedsGone = StepDone(f, "yard", "weeds");
                int n = weedsGone ? 0 : 900;
                for (int i = 0; i < n; i++)
                {
                    var p = lot.Local((R() - 0.5f) * (lot.Frontage - 0.8f), 0.6f + R() * (lot.Depth - 1.2f));
                    if (Blocked(lot, house, p, driveSide, side)) continue;
                    // a clump of 6-9 thin blades, leaning out (knee-high fescue, crabgrass, seed heads)
                    int blades = 6 + rng.Next(4), mat = R() < 0.3f ? dry : grass;
                    float clumpH = 0.3f + R() * 0.45f;
                    for (int b = 0; b < blades; b++)
                    {
                        float a = R() * 360f, lean = 8f + R() * 26f, h = clumpH * (0.7f + R() * 0.5f);
                        var rot = Quaternion.Euler(0, a, 0) * Quaternion.Euler(lean, 0, 0);
                        var basePt = p + Quaternion.Euler(0, a, 0) * new Vector3(0, 0, R() * 0.06f);
                        k.B.AddFrustum(basePt + rot * Vector3.up * (h / 2f), rot, 0.014f, 0.002f, h, 3, mat, false);
                    }
                    if (R() < 0.03f) k.B.AddFrustum(p + Vector3.up * 0.35f, Quaternion.Euler(0, R() * 360f, 0), 0.12f, 0.03f, 0.7f, 6, weed, false);
                }
                // volunteer saplings along the foundation and fence
                if (!weedsGone && house != null)
                    for (int i = 0; i < 4; i++)
                    {
                        var p = house.transform.TransformPoint(new Vector3((R() - 0.5f) * house.Width, 0f, (i % 2 == 0 ? 1 : -1) * (house.Depth / 2f + 0.5f)));
                        if (Blocked(lot, house, p, driveSide, side, false)) continue;
                        k.B.Transform = Matrix4x4.TRS(p, Quaternion.Euler(0, R() * 360f, 0), Vector3.one);
                        LandModels.Shrub(k, 300 + i, 1.0f + R() * 0.5f, 1.6f + R() * 0.8f, new Color(0.55f, 0.75f, 0.4f));
                        k.B.ResetTransform();
                    }
                // boarded bedroom window (plywood over the opening) until it's pried off
                if (house != null && !StepDone(f, "yard", "board"))
                {
                    float x = (0.75f + house.Width / 2f) / 2f;     // bedroom window centre (right of the hall partition)
                    var c = house.transform.TransformPoint(new Vector3(x, house.FloorY + 0.9f + 0.6f, house.Depth / 2f + 0.03f));
                    k.B.AddBox(c, new Vector3(1.3f, 1.35f, 0.012f), house.transform.rotation, k.M(SurfaceKind.Plywood));
                }
            }

            // ---- blue tarp over the roof leak, held down with 2x4s (until the roof is fixed)
            if (!sold && Has(f, "roof") && !Done(f, "roof") && house != null && house.Has(HouseSpot.RoofShingles))
            {
                var a = house.Anchor(HouseSpot.RoofShingles);
                int tarp = k.M(LandSurface.Tarp, new Color(0.25f, 0.45f, 0.85f)), wood = k.M(SurfaceKind.PineLumber);
                k.B.AddBox(a.position + a.rotation * new Vector3(0.6f, 0.03f, 0.6f), new Vector3(3.6f, 0.01f, 2.8f), a.rotation, tarp);
                for (int i = 0; i < 2; i++)
                    k.B.AddBox(a.position + a.rotation * new Vector3(0.6f, 0.07f, -0.7f + i * 2.6f), new Vector3(3.66f, 0.038f, 0.089f), a.rotation, wood);
            }

            // ---- 20-yard roll-off dumpster on the lawn while it's being renovated
            if (f.Status == FlipStatus.Owned && lot.Contains(DumpsterPos(lot, side, driveSide)))
            {
                var p = DumpsterPos(lot, side, driveSide);
                var rot = Quaternion.LookRotation(front, Vector3.up);
                var dk = new LandModel();
                int steel = dk.M(SurfaceKind.PaintedSteel, new Color(0.2f, 0.42f, 0.3f)), rail = dk.M(SurfaceKind.Galvanized);
                float L = (22f * Units.Foot), W = (7.5f * Units.Foot), H = (4.5f * Units.Foot), t = 0.05f;
                dk.B.AddBox(new Vector3(0, 0.08f, 0), new Vector3(W, 0.12f, L), steel);
                dk.B.AddBox(new Vector3(-W / 2f + t / 2f, H / 2f, 0), new Vector3(t, H, L), steel);
                dk.B.AddBox(new Vector3(W / 2f - t / 2f, H / 2f, 0), new Vector3(t, H, L), steel);
                dk.B.AddBox(new Vector3(0, H / 2f, L / 2f - t / 2f), new Vector3(W, H, t), steel);
                dk.B.AddBox(new Vector3(0, H / 2f, -L / 2f + t / 2f), new Vector3(W, H, t), steel);
                for (int i = 0; i < 2; i++) dk.B.AddBox(new Vector3((i == 0 ? -1 : 1) * (W / 2f - 0.6f), 0.02f, 0), new Vector3(0.1f, 0.04f, L + 0.2f), rail);
                if (StepDone(f, "yard", "bag")) dk.B.AddBox(new Vector3(0, H * 0.45f, 0.4f), new Vector3(W - 0.2f, H * 0.6f, L * 0.6f), dk.M(SurfaceKind.DarkPlastic, new Color(0.15f, 0.15f, 0.16f)));
                var dgo = dk.Make(go.transform, "Roll-off dumpster", true);
                dgo.transform.SetPositionAndRotation(p, rot);
            }

            // ---- yard sign by the sidewalk
            {
                string top, line2;
                switch (f.Status)
                {
                    case FlipStatus.Listed: top = "FOR SALE"; line2 = f.seller.Contains("Bank") || f.seller.Contains("Federal") ? "BANK OWNED" : "AS-IS"; break;
                    case FlipStatus.Owned: top = "COMING SOON"; line2 = "RENOVATION"; break;
                    case FlipStatus.ForSale: top = "FOR SALE"; line2 = "RENOVATED"; break;
                    default: top = "SOLD"; line2 = ""; break;
                }
                var p = lot.FrontMid - front * 1.2f + side * (-driveSide) * 2.5f;
                var rot = Quaternion.LookRotation(front, Vector3.up);
                var sk = new LandModel();
                int post = sk.M(SurfaceKind.PaintedWhite), panel = sk.M(f.Status == FlipStatus.Sold ? new Color(0.75f, 0.12f, 0.1f) : new Color(0.12f, 0.22f, 0.45f)),
                    ink = sk.M(new Color(0.97f, 0.97f, 0.95f));
                sk.B.AddBox(new Vector3(0, 0.6f, 0), new Vector3(0.09f, 1.2f, 0.09f), post);                       // 4x4 post
                sk.B.AddBox(new Vector3(0.35f, 1.12f, 0), new Vector3(0.8f, 0.05f, 0.05f), post);                  // arm
                sk.B.AddBox(new Vector3(0.38f, 0.82f, 0), new Vector3(0.61f, 0.46f, 0.015f), panel);               // 24"x18" panel
                var mb = new MapBuild { K = sk };
                mb.Frame = Matrix4x4.identity;
                MapText.Draw(mb, top, new Vector3(0.38f, 0.9f, 0.008f), 0.0155f, ink);
                if (line2.Length > 0) MapText.Draw(mb, line2, new Vector3(0.38f, 0.76f, 0.008f), 0.011f, ink);
                var sgo = sk.Make(go.transform, "Yard sign");
                sgo.transform.SetPositionAndRotation(p, rot);
            }

            if (k.B.VertexCount > 0) k.Make(go.transform, "Overgrowth");
        }

        /// <summary>The roll-off sits on the lawn on the side away from the driveway, running toward the house (22' long).</summary>
        static Vector3 DumpsterPos(TownLot lot, Vector3 side, float driveSide) =>
            lot.FrontMid - lot.Front * 4.6f - side * driveSide * (lot.Frontage / 2f - 1.9f);

        /// <summary>No tufts on the house, the driveway, the front walk or the dumpster pad.</summary>
        static bool Blocked(TownLot lot, TownHouse house, Vector3 p, float driveSide, Vector3 side, bool keepOffHouse = true)
        {
            if (!lot.Contains(p)) return true;                                                                    // never outside the lot
            var r = lot.Rect;
            if (p.x < r.xMin + 0.3f || p.x > r.xMax - 0.3f || p.z < r.yMin + 0.3f || p.z > r.yMax - 0.3f) return true;
            if (house != null)
            {
                var l = house.transform.InverseTransformPoint(p);
                if (Mathf.Abs(l.x) < house.Width / 2f + 0.25f && Mathf.Abs(l.z) < house.Depth / 2f + 0.25f) return true;
                if (Mathf.Abs(l.x - TownHouse.FrontDoorX) < 0.9f && l.z > 0f) return true;                    // front walk / stoop
                if (l.z < -house.Depth / 2f && l.z > -house.Depth / 2f - 3.8f && l.x > -3.2f && l.x < 3.2f) return true;   // rear deck
            }
            float across = Vector3.Dot(p - lot.Parking, side);
            if (Mathf.Abs(across) < 2.1f) return true;                                                            // driveway strip
            var dump = DumpsterPos(lot, side, driveSide);
            if (Mathf.Abs(Vector3.Dot(p - dump, side)) < 1.5f && Mathf.Abs(Vector3.Dot(p - dump, lot.Front)) < 3.7f) return true;
            return false;
        }
    }
}
