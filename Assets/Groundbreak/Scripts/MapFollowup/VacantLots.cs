using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Groundbreak
{
    /// <summary>
    /// Vacant-lot dressing for every unclaimed town lot (core + map): for-sale sign, survey stakes with flagging at the corners, tall-grass
    /// and weed clumps, dry/bare patches, brush, a few trees along the back and sides, an old post-and-wire fence line, dirt piles and a
    /// gravel construction entrance. Real sizes, one merged mesh per lot, no colliders. Hidden as soon as the lot is claimed
    /// (<see cref="Town.LotsChanged"/>). Also replaces the core town's baked-in for-sale signs (which stayed after a claim) with per-lot ones,
    /// and dresses the margins of the player's build site.
    /// </summary>
    public static class VacantLots
    {
        public static GameObject Root { get; private set; }
        public static readonly Dictionary<string, GameObject> ByLot = new Dictionary<string, GameObject>();
        public static int Dressed => ByLot.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Root = null; ByLot.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            TownBuilder.ForSaleSigns = false;
            Town.Built -= Build; Town.Built += Build;
            Town.LotsChanged -= Refresh; Town.LotsChanged += Refresh;
            Town.Destroyed -= Clear; Town.Destroyed += Clear;
        }

        static void Clear() { if (Root != null) Object.Destroy(Root); Root = null; ByLot.Clear(); }

        /// <summary>(Re)builds the dressing for every lot that is free now; claimed lots get theirs too, hidden, so a released lot shows it again.</summary>
        public static void Build()
        {
            Clear();
            if (Town.Root == null) return;
            Root = new GameObject("VacantLotDressing");
            Root.transform.SetParent(Town.Root.transform, false);
            Root.AddComponent<VacantLotStreamer>();
            foreach (var l in Town.Lots)
            {
                if (l.Use == LotUse.BuildSite && l == Town.BuildSite) { try { DressBuildSite(l); } catch (System.Exception e) { Debug.LogException(e); } continue; }
                if (l.Use != LotUse.Free && string.IsNullOrEmpty(l.Owner)) continue;   // built lots (houses, businesses) are not vacant land
                if (l.TypeId != null) continue;
                try { ByLot[l.Id] = Dress(l); } catch (System.Exception e) { Debug.LogException(e); }
            }
            Refresh();
        }

        public static void Refresh()
        {
            foreach (var kv in ByLot)
            {
                if (kv.Value == null) continue;
                var l = Town.Find(kv.Key);
                bool show = l != null && l.Use == LotUse.Free;
                if (kv.Value.activeSelf != show) kv.Value.SetActive(show);
            }
        }

        // ------------------------------------------------------------------ one lot

        static readonly Color Flag = new Color(1f, 0.25f, 0.55f), Cap = new Color(1f, 0.45f, 0.05f),
            Straw = new Color(0.74f, 0.66f, 0.40f), Seed = new Color(0.58f, 0.50f, 0.30f), Green = new Color(0.36f, 0.50f, 0.22f), DarkGreen = new Color(0.26f, 0.38f, 0.16f),
            OldWood = new Color(0.62f, 0.58f, 0.52f), Rust = new Color(0.42f, 0.27f, 0.17f), DryPatch = new Color(0.66f, 0.60f, 0.38f);

        static GameObject Dress(TownLot lot)
        {
            int seed = Hash(lot.Id);
            var rng = new System.Random(seed);
            float W = lot.Frontage, D = lot.Depth;
            bool tract = LotBroker.IsTract(lot);
            var go = new GameObject("Vacant " + lot.Id);
            go.transform.SetParent(Root.transform, false);
            go.transform.SetPositionAndRotation(lot.FrontMid, lot.Rotation);   // lot frame: +Z toward the street, lot covers x ±W/2, z 0..-D
            var k = new LandModel();
            var b = new MapBuild { K = k, Rng = rng, Frame = Matrix4x4.identity };
            float R() => (float)rng.NextDouble();
            float Rr(float a, float c) => a + (c - a) * (float)rng.NextDouble();

            // for-sale sign (map lots already get one with their meadow, core lots got a baked one that never went away)
            if (!lot.External) Sign(k, new Vector3(-Mathf.Min(6f, W / 2f - 2f), 0, -2.5f), tract);

            // survey: an iron pin with an orange cap and a 4' lath with pink flagging at each corner
            foreach (var (cx, cz) in new[] { (-1f, 0f), (1f, 0f), (-1f, -1f), (1f, -1f) })
                Stake(k, new Vector3(cx * (W / 2f - 0.3f), 0, cz * (D - 0.3f) - (cz == 0 ? 0.3f : 0f)), rng);

            // bare and dry patches (read from the air), then tall-grass / weed clumps
            int patches = Mathf.Clamp(Mathf.RoundToInt(W * D / 250f), 2, 14);
            for (int i = 0; i < patches; i++)
            {
                var c = new Vector3(Rr(-W / 2f + 3f, W / 2f - 3f), 0, -Rr(4f, D - 3f));
                float r = Mathf.Min(Rr(1.5f, 4.5f), Mathf.Min(W, D) * 0.2f);
                Blob(k, c, r, i % 3 == 0 ? k.M(SurfaceKind.Dirt) : k.M(DryPatch), rng, 0.012f + i * 0.0005f);
            }
            int clumps = Mathf.Clamp(Mathf.RoundToInt(W * D / 18f), 25, 260);
            int centres = Mathf.Clamp(Mathf.RoundToInt(W * D / 300f), 3, 18);
            var hubs = new List<Vector3>();
            for (int i = 0; i < centres; i++) hubs.Add(new Vector3(Rr(-W / 2f + 1.5f, W / 2f - 1.5f), 0, -Rr(3.5f, D - 1.5f)));
            for (int i = 0; i < clumps; i++)
            {
                Vector3 p;
                if (i % 3 == 0)
                {   // along the side and back lot lines, where mowers never reach
                    int e = rng.Next(3);
                    p = e == 0 ? new Vector3(-W / 2f + Rr(0.3f, 1.8f), 0, -Rr(3f, D - 0.3f))
                      : e == 1 ? new Vector3(W / 2f - Rr(0.3f, 1.8f), 0, -Rr(3f, D - 0.3f))
                      : new Vector3(Rr(-W / 2f + 0.3f, W / 2f - 0.3f), 0, -D + Rr(0.3f, 2f));
                }
                else
                {
                    var h = hubs[rng.Next(hubs.Count)];
                    p = h + new Vector3(Rr(-4f, 4f), 0, Rr(-4f, 4f));
                    p.x = Mathf.Clamp(p.x, -W / 2f + 0.3f, W / 2f - 0.3f); p.z = Mathf.Clamp(p.z, -D + 0.3f, -3.2f);
                }
                Clump(k, p, rng, R() < 0.55f ? k.M(Straw) : R() < 0.5f ? k.M(Green) : k.M(DarkGreen), k.M(Seed));
            }

            // brush and young volunteer trees along the edges, a few real trees at the back / side
            int bushes = Mathf.Clamp(Mathf.RoundToInt(W * D / 220f), 2, 16);
            for (int i = 0; i < bushes; i++)
            {
                var p = new Vector3(R() < 0.5f ? -W / 2f + Rr(1f, 3f) : W / 2f - Rr(1f, 3f), 0, -Rr(D * 0.35f, D - 1f));
                if (i % 3 == 2) p = new Vector3(Rr(-W / 2f + 2f, W / 2f - 2f), 0, -D + Rr(1f, 3f));
                b.Frame = Matrix4x4.identity; k.B.Transform = Matrix4x4.identity;
                MapTrees.Bush(b, p, Rr(0.8f, 1.6f), rng.Next());
            }
            if (D >= 25f)
            {
                for (float x = -W / 2f + Rr(2f, 5f); x < W / 2f - 2f; x += Rr(7f, 13f))
                    if (R() < 0.55f) Tree(b, new Vector3(x, 0, -D + Rr(1.2f, 2.5f)), rng);
                if (R() < 0.6f) Tree(b, new Vector3((R() < 0.5f ? -1f : 1f) * (W / 2f - Rr(1.2f, 2.2f)), 0, -Rr(D * 0.4f, D - 4f)), rng);
            }

            // old fence: weathered posts every 8' with three strands of rusty barbed wire, a few leaning, a few missing
            if (R() < 0.6f)
            {
                bool back = R() < 0.6f || W > D;
                float side = R() < 0.5f ? -1f : 1f;
                Vector3 a = back ? new Vector3(-W / 2f + 0.4f, 0, -D + 0.4f) : new Vector3(side * (W / 2f - 0.4f), 0, -D + 0.4f);
                Vector3 e = back ? new Vector3(W / 2f - 0.4f, 0, -D + 0.4f) : new Vector3(side * (W / 2f - 0.4f), 0, -Mathf.Min(8f, D * 0.3f));
                Fence(k, a, e, rng);
            }

            // dirt piles left by someone's grading, and a gravel construction entrance off the street
            if (R() < 0.45f)
            {
                int n = tract ? 3 : 1 + (R() < 0.3f ? 1 : 0);
                for (int i = 0; i < n; i++) Pile(k, new Vector3(Rr(-W / 2f + 4f, W / 2f - 4f), 0, -Rr(D * 0.45f, D - 5f)), Rr(1.6f, 3.2f), Rr(0.8f, 1.6f), rng);
            }
            if (R() < 0.35f && !lot.External || R() < 0.3f)
            {
                float x = Rr(-W / 2f + 3f, W / 2f - 3f);
                k.B.AddBox(new Vector3(x, 0.04f, -4.6f), new Vector3(3.7f, 0.08f, 9f), Quaternion.identity, k.M(SurfaceKind.Gravel), Vector3.forward);
            }

            if (k.B.VertexCount > 0)
            {
                var mesh = k.Make(go.transform, "Dressing");
                var mr = mesh.GetComponent<MeshRenderer>(); if (mr != null) mr.shadowCastingMode = ShadowCastingMode.On;
            }
            return go;
        }

        /// <summary>Back and side margins of the player's build site: tree line, tall grass, survey stakes. The pad itself stays clear.</summary>
        static void DressBuildSite(TownLot lot)
        {
            var r = Town.BuildSiteRect;
            var rng = new System.Random(777);
            var go = new GameObject("BuildSite margins");
            go.transform.SetParent(Root.transform, false);
            var k = new LandModel();
            var b = new MapBuild { K = k, Rng = rng, Frame = Matrix4x4.identity };
            float Rr(float a, float c) => a + (c - a) * (float)rng.NextDouble();
            for (float x = r.xMin + 3f; x < r.xMax - 2f; x += Rr(6f, 11f)) Tree(b, new Vector3(x, 0, r.yMax - Rr(1.5f, 4f)), rng);
            for (int i = 0; i < 160; i++)
            {
                int e = rng.Next(3);
                var p = e == 0 ? new Vector3(Rr(r.xMin + 0.5f, r.xMax - 0.5f), 0, r.yMax - Rr(0.5f, 9f))
                      : new Vector3(e == 1 ? r.xMin + Rr(0.5f, 4f) : r.xMax - Rr(0.5f, 4f), 0, Rr(r.center.y, r.yMax - 1f));
                Clump(k, p, rng, i % 2 == 0 ? k.M(Straw) : k.M(Green), k.M(Seed));
            }
            for (int i = 0; i < 8; i++) MapTrees.Bush(b, new Vector3(i % 2 == 0 ? r.xMin + Rr(1f, 3f) : r.xMax - Rr(1f, 3f), 0, Rr(r.center.y + 10f, r.yMax - 6f)), Rr(1f, 1.8f), rng.Next());
            foreach (var p in new[] { new Vector3(r.xMin + 0.3f, 0, r.yMax - 0.3f), new Vector3(r.xMax - 0.3f, 0, r.yMax - 0.3f) }) Stake(k, p, rng);
            // side margins: tall grass bands and a loose row of trees (the pad around the origin stays clear)
            for (int i = 0; i < 220; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Clump(k, new Vector3(side * Rr(41f, r.xMax - 0.5f), 0, Rr(r.yMin + 3f, r.yMax - 2f)), rng, i % 3 == 0 ? k.M(Green) : k.M(Straw), k.M(Seed));
            }
            for (float z = r.yMin + 8f; z < r.yMax - 8f; z += Rr(9f, 15f))
                foreach (float side in new[] { -1f, 1f }) if (rng.NextDouble() < 0.7) Tree(b, new Vector3(side * Rr(47f, r.xMax - 1.5f), 0, z), rng);
            for (int i = 0; i < 6; i++) MapTrees.Bush(b, new Vector3((i % 2 == 0 ? -1f : 1f) * Rr(43f, 50f), 0, Rr(r.yMin + 6f, r.center.y)), Rr(1f, 1.7f), rng.Next());
            // street front: silt fence (black geotextile on 2x2 stakes) with a gap at the stabilized gravel construction entrance, job sign
            int fab = k.M(new Color(0.08f, 0.08f, 0.08f)), stake = k.M(SurfaceKind.PineLumber);
            float fz = r.yMin + 1.2f;
            foreach (var (x0, x1) in new[] { (r.xMin + 1f, -5f), (5f, r.xMax - 1f) })
            {
                k.B.AddBox(new Vector3((x0 + x1) / 2f, 0.33f, fz), new Vector3(x1 - x0, 0.6f, 0.01f), Quaternion.identity, fab);
                for (float x = x0; x <= x1 + 0.01f; x += 6f * Units.Foot) k.B.AddBox(new Vector3(x, 0.45f, fz - 0.03f), new Vector3(0.038f, 0.9f, 0.038f), Quaternion.identity, stake, Vector3.up);
            }
            k.B.AddBox(new Vector3(0f, 0.05f, r.yMin + 7.6f), new Vector3(6.1f, 0.1f, 15.2f), Quaternion.identity, k.M(SurfaceKind.Gravel), Vector3.forward);   // 20' x 50'
            var sp = new Vector3(-9f, 0, r.yMin + 2.5f);
            for (int sx = -1; sx <= 1; sx += 2) k.B.AddBox(sp + new Vector3(sx * 0.75f, 1.2f, 0), new Vector3(0.089f, 2.4f, 0.089f), Quaternion.identity, stake, Vector3.up);
            k.B.AddBox(sp + new Vector3(0, 1.75f, -0.06f), new Vector3(2.44f, 1.22f, 0.02f), Quaternion.identity, k.M(new Color(0.95f, 0.95f, 0.92f)));   // 4' x 8' plywood job sign
            k.B.AddBox(sp + new Vector3(0, 2.15f, -0.075f), new Vector3(2.1f, 0.3f, 0.004f), Quaternion.identity, k.M(new Color(0.1f, 0.25f, 0.55f)));
            k.B.AddBox(sp + new Vector3(0, 1.6f, -0.075f), new Vector3(1.6f, 0.12f, 0.004f), Quaternion.identity, k.M(new Color(0.15f, 0.15f, 0.15f)));
            k.B.AddBox(sp + new Vector3(0, 1.38f, -0.075f), new Vector3(1.2f, 0.1f, 0.004f), Quaternion.identity, k.M(new Color(0.15f, 0.15f, 0.15f)));
            if (k.B.VertexCount > 0) k.Make(go.transform, "Margins");
        }

        // ------------------------------------------------------------------ pieces (lot frame, metres)

        static int Hash(string s) { unchecked { int h = 17; foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; } }

        static void Sign(LandModel k, Vector3 p, bool land)
        {
            int post = k.M(SurfaceKind.PineLumber), red = k.M(new Color(0.75f, 0.1f, 0.08f)), wht = k.M(new Color(0.95f, 0.95f, 0.93f));
            if (land)
            {   // 6' x 4' land sign on two 4x4 posts
                for (int s = -1; s <= 1; s += 2) k.B.AddBox(p + new Vector3(s * 0.7f, 1.1f, 0), new Vector3(0.089f, 2.2f, 0.089f), Quaternion.identity, post, Vector3.up);
                k.B.AddBox(p + new Vector3(0, 1.55f, 0.06f), new Vector3(1.83f, 1.22f, 0.02f), Quaternion.identity, red);
                k.B.AddBox(p + new Vector3(0, 1.75f, 0.072f), new Vector3(1.5f, 0.25f, 0.004f), Quaternion.identity, wht);
                k.B.AddBox(p + new Vector3(0, 1.3f, 0.072f), new Vector3(1.1f, 0.12f, 0.004f), Quaternion.identity, wht);
            }
            else
            {   // 24" x 18" yard sign on a 4x4 post
                k.B.AddBox(p + new Vector3(0, 0.6f, 0), new Vector3(0.089f, 1.2f, 0.089f), Quaternion.identity, post, Vector3.up);
                k.B.AddBox(p + new Vector3(0, 0.95f, 0.06f), new Vector3(0.61f, 0.46f, 0.02f), Quaternion.identity, red);
                k.B.AddBox(p + new Vector3(0, 0.95f, 0.072f), new Vector3(0.5f, 0.12f, 0.004f), Quaternion.identity, wht);
            }
        }

        static void Stake(LandModel k, Vector3 p, System.Random rng)
        {
            // #5 rebar pin flush with grade + orange plastic cap
            k.B.AddCylinder(p + new Vector3(0, 0.02f, 0), 0.025f, 0.04f, 8, k.M(Cap));
            // 1x2 lath, 4' long, driven ~6"; pink flagging tied near the top
            var lp = p + new Vector3(0.25f, 0, 0.1f);
            var lean = Quaternion.Euler((float)rng.NextDouble() * 6f - 3f, 0, (float)rng.NextDouble() * 6f - 3f);
            k.B.AddBox(lp + lean * new Vector3(0, 0.53f, 0), new Vector3(0.038f, 1.07f, 0.019f), lean, k.M(SurfaceKind.PineLumber), Vector3.up);
            k.B.AddBox(lp + lean * new Vector3(0.03f, 0.86f, 0), new Vector3(0.022f, 0.36f, 0.002f), lean * Quaternion.Euler(0, 0, 12f), k.M(Flag));
            k.B.AddBox(lp + lean * new Vector3(-0.025f, 0.9f, 0.012f), new Vector3(0.022f, 0.3f, 0.002f), lean * Quaternion.Euler(0, 30f, -8f), k.M(Flag));
        }

        /// <summary>A clump of tall grass / weeds: thin tapered double-sided blades 1-3.5' tall, some with seed heads.</summary>
        static void Clump(LandModel k, Vector3 c, System.Random rng, int sub, int seedSub)
        {
            int n = 9 + rng.Next(8);
            float tall = 0.35f + (float)rng.NextDouble() * 0.75f;
            for (int i = 0; i < n; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = (float)rng.NextDouble() * 0.22f;
                var b0 = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                float h = tall * (0.6f + (float)rng.NextDouble() * 0.5f), w = 0.012f + (float)rng.NextDouble() * 0.012f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, dir).normalized * w;
                var tip = b0 + Vector3.up * h + dir * h * (0.15f + (float)rng.NextDouble() * 0.35f);
                k.B.AddTriangle(b0 - side, b0 + side, tip, sub, Vector3.Cross(side, tip - b0));
                k.B.AddTriangle(b0 + side, b0 - side, tip, sub, -Vector3.Cross(side, tip - b0));
                if (i % 5 == 0) k.B.AddBox(tip + Vector3.up * 0.04f, new Vector3(0.018f, 0.09f, 0.018f), Quaternion.LookRotation(dir), seedSub);
            }
        }

        /// <summary>Irregular flat patch on the ground (convex blob).</summary>
        static void Blob(LandModel k, Vector3 c, float r, int sub, System.Random rng, float y)
        {
            var pts = new List<Vector3>();
            int n = 12;
            for (int i = 0; i < n; i++)
            {
                float a = -i * Mathf.PI * 2f / n, rr = r * (0.8f + (float)rng.NextDouble() * 0.2f);
                pts.Add(c + new Vector3(Mathf.Cos(a) * rr * 1.3f, y, Mathf.Sin(a) * rr));
            }
            k.B.AddPolygon(pts, sub, Vector3.up, Vector3.right);
        }

        static void Tree(MapBuild b, Vector3 p, System.Random rng)
        {
            b.Frame = Matrix4x4.identity; b.B.Transform = Matrix4x4.identity;
            float h = 7f + (float)rng.NextDouble() * 6f;
            if (rng.NextDouble() < 0.3) MapTrees.Conifer(b, p, h, rng.Next()); else MapTrees.Deciduous(b, p, h, rng.Next());
            b.B.Transform = Matrix4x4.identity;
        }

        static void Fence(LandModel k, Vector3 a, Vector3 e, System.Random rng)
        {
            float len = Vector3.Distance(a, e);
            if (len < 3f) return;
            var dir = (e - a) / len;
            var rot = Quaternion.LookRotation(dir);
            int post = k.M(SurfaceKind.PineLumber, OldWood), wire = k.M(Rust);
            const float Span = 8f * Units.Foot;
            int n = Mathf.FloorToInt(len / Span);
            var tops = new List<Vector3?>();
            for (int i = 0; i <= n; i++)
            {
                if (i > 0 && i < n && rng.NextDouble() < 0.12) { tops.Add(null); continue; }   // rotted off
                var p = a + dir * (i * Span);
                var lean = Quaternion.AngleAxis(((float)rng.NextDouble() - 0.5f) * (rng.NextDouble() < 0.2 ? 22f : 6f), dir) * Quaternion.AngleAxis(((float)rng.NextDouble() - 0.5f) * 6f, Vector3.Cross(Vector3.up, dir));
                float h = 1.1f + (float)rng.NextDouble() * 0.15f;
                k.B.AddBox(p + lean * new Vector3(0, h / 2f, 0), new Vector3(0.1f, h, 0.1f), lean * rot, post, Vector3.up);
                tops.Add(p + lean * new Vector3(0, h, 0));
            }
            // three strands at 14", 26", 38" (sagging a little); a strand is missing where a post is
            for (int i = 0; i < tops.Count - 1; i++)
            {
                if (tops[i] == null || tops[i + 1] == null) continue;
                var p0 = a + dir * (i * Span); var p1 = a + dir * ((i + 1) * Span);
                foreach (float y in new[] { 0.36f, 0.66f, 0.97f })
                {
                    if (rng.NextDouble() < 0.08) continue;   // broken strand
                    var s0 = p0 + Vector3.up * y; var s1 = p1 + Vector3.up * (y - 0.02f);
                    var mid = (s0 + s1) / 2f - Vector3.up * 0.04f;
                    Wire(k, s0, mid, wire); Wire(k, mid, s1, wire);
                }
            }
        }

        static void Wire(LandModel k, Vector3 a, Vector3 b, int sub)
        {
            var d = b - a; float l = d.magnitude; if (l < 0.01f) return;
            k.B.AddBox((a + b) / 2f, new Vector3(0.005f, 0.005f, l), Quaternion.LookRotation(d / l), sub, Vector3.forward);
        }

        static void Pile(LandModel k, Vector3 c, float r, float h, System.Random rng)
        {
            var prof = new[] { new Vector2(r, 0f), new Vector2(r * 0.85f, h * 0.25f), new Vector2(r * 0.55f, h * 0.7f), new Vector2(r * 0.25f, h * 0.95f), new Vector2(0f, h) };
            k.B.AddLathe(c, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), prof, 14, k.M(SurfaceKind.Dirt), 60f);
            // a few weeds already growing on it
            for (int i = 0; i < 4; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, rr = r * (0.4f + (float)rng.NextDouble() * 0.5f);
                float y = h * (1f - rr / r) * 0.9f;
                Clump(k, c + new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr), rng, k.M(Green), k.M(Seed));
            }
        }
    }

    /// <summary>Hides lot dressing beyond the map's show distance (follows <see cref="MapStreamer"/>).</summary>
    public class VacantLotStreamer : MonoBehaviour
    {
        float _next; bool _frozen;
        readonly Dictionary<GameObject, Renderer[]> _r = new Dictionary<GameObject, Renderer[]>();

        void Update()
        {
            if (Time.unscaledTime < _next && MapStreamer.Frozen == _frozen) return;
            _frozen = MapStreamer.Frozen;
            _next = Time.unscaledTime + 0.5f;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position; p.y = 0;
            float d = MapStreamer.Frozen ? float.MaxValue : MapStreamer.ShowDistance + 60f;
            foreach (Transform t in transform)
            {
                if (!_r.TryGetValue(t.gameObject, out var rs)) _r[t.gameObject] = rs = t.GetComponentsInChildren<Renderer>(true);
                bool show = (t.position - p).sqrMagnitude < d * d || t.name.StartsWith("BuildSite");
                foreach (var r in rs) if (r != null && r.enabled != show) r.enabled = show;
            }
        }
    }
}
