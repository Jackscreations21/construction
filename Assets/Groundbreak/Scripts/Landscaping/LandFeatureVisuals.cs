using UnityEngine;

namespace Groundbreak
{
    /// <summary>Builds the stage-by-stage look of a <see cref="LandscapeFeature"/>. Run features (walk, drive, fence, gate) are
    /// built in run-local space: +Z along the run, X across, y = 0 at finished grade (BaseY).</summary>
    public static class LandFeatureVisuals
    {
        static readonly Color Pink = new Color(1f, 0.25f, 0.6f), Orange = new Color(1f, 0.45f, 0.05f);

        public static void Build(LandscapeFeature f, Transform parent, BoxCollider col)
        {
            switch (f.Kind)
            {
                case LandscapeKind.Walkway: Walkway(f, parent, col); break;
                case LandscapeKind.GravelDrive: Drive(f, parent, col); break;
                case LandscapeKind.Fence:
                case LandscapeKind.Gate: Fence(f, parent, col); break;
                case LandscapeKind.Plant: Plant(f, parent, col); break;
                case LandscapeKind.Sod:
                    LandModels.Instance("sod", parent, LandModels.SodFlat);
                    col.center = new Vector3(0, 0.0127f, 0); col.size = new Vector3(2 * Units.Foot, 0.0254f, 5 * Units.Foot);
                    break;
                case LandscapeKind.RetainingBlock:
                    LandModels.Instance("retaining-block", parent, LandModels.RetainingBlock);
                    col.center = new Vector3(0, Units.In(2), 0); col.size = new Vector3(Units.In(11.75f), Units.In(4), Units.In(6.75f));
                    break;
                case LandscapeKind.PathLight:
                    LandModels.Instance("path-light", parent, LandModels.PathLight);
                    col.center = new Vector3(0, Units.In(10), 0); col.size = new Vector3(Units.In(6), Units.In(21), Units.In(6));
                    break;
                case LandscapeKind.Mailbox:
                    LandModels.Instance("mailbox", parent, LandModels.Mailbox);
                    col.center = new Vector3(0, Units.In(26), Units.In(6)); col.size = new Vector3(Units.In(7), Units.In(52), Units.In(22));
                    break;
            }
        }

        static float Frac(LandscapeFeature f, int stage)
        {
            if (f.Stage > stage) return 1f;
            if (f.Stage < stage) return 0f;
            var c = f.Current; return c.HasValue ? f.Progress / (float)c.Value.Steps : 1f;
        }

        static void Layout(LandModel k, float hw, float hl)
        {
            var c = new[] { new Vector3(-hw - 0.15f, 0, -hl - 0.15f), new Vector3(hw + 0.15f, 0, -hl - 0.15f), new Vector3(hw + 0.15f, 0, hl + 0.15f), new Vector3(-hw - 0.15f, 0, hl + 0.15f) };
            foreach (var p in c) LandModels.Stake(k, p);
            float y = Units.In(4);
            LandModels.String(k, new Vector3(-hw, y, -hl - 0.15f), new Vector3(-hw, y, hl + 0.15f), Pink);
            LandModels.String(k, new Vector3(hw, y, -hl - 0.15f), new Vector3(hw, y, hl + 0.15f), Pink);
        }

        // ------------------------------------------------------------------ walkway

        static void Walkway(LandscapeFeature f, Transform parent, BoxCollider col)
        {
            float W = f.Width, L = f.Length, hw = W / 2, hl = L / 2;
            var k = new LandModel();
            if (f.Stage <= 3) Layout(k, hw, hl);
            float bottom = -LandscapeFeature.WalkDepth;
            float baseF = Frac(f, 1);
            if (baseF > 0)
            {
                float t = LandscapeFeature.BaseT * baseF;
                k.B.AddBox(new Vector3(0, bottom + t / 2, 0), new Vector3(W, t, L), Quaternion.identity, k.M(SurfaceKind.Gravel, new Color(0.85f, 0.85f, 0.85f)));
            }
            float sandF = Frac(f, 2);
            if (sandF > 0)
            {
                float t = LandscapeFeature.SandT;
                float len = L * sandF;
                k.B.AddBox(new Vector3(0, bottom + LandscapeFeature.BaseT + t / 2, -hl + len / 2), new Vector3(W, t, len), Quaternion.identity, k.M(LandSurface.Sand));
            }
            int rows = Mathf.CeilToInt(L / Units.In(4) - 1e-4f);
            int laid = f.Stage > 3 ? rows : f.Stage == 3 ? Mathf.Min(rows, f.Progress * 3) : 0;
            if (laid > 0)
            {
                int[] tones = { k.M(LandSurface.Paver, new Color(0.80f, 0.74f, 0.66f)), k.M(LandSurface.Paver, new Color(0.70f, 0.64f, 0.58f)), k.M(LandSurface.Paver, new Color(0.88f, 0.82f, 0.74f)) };
                float pw = Units.In(8), pl = Units.In(4), gap = Units.In(1f / 16f), bev = Units.In(0.25f), pt = LandscapeFeature.PaverT;
                var rng = new System.Random(f.GetHashCode());
                for (int r = 0; r < laid; r++)
                {
                    float z0 = -hl + r * pl, z1 = Mathf.Min(hl, z0 + pl);
                    float x = -hw - (r % 2 == 1 ? pw / 2 : 0);
                    for (; x < hw - 0.001f; x += pw)
                    {
                        float xa = Mathf.Max(-hw, x), xb = Mathf.Min(hw, x + pw);
                        if (xb - xa < 0.01f) continue;
                        var size = new Vector3(xb - xa - gap, pt, z1 - z0 - gap);
                        k.B.UvOffset = new Vector2((float)rng.NextDouble() * 3, (float)rng.NextDouble() * 3);
                        k.B.AddBevelBox(new Vector3((xa + xb) / 2, -pt / 2, (z0 + z1) / 2), new Vector3(size.z, size.y, size.x), Quaternion.Euler(0, 90, 0), bev, tones[rng.Next(3)]);
                    }
                }
                k.B.UvOffset = Vector2.zero;
            }
            float edgeF = Frac(f, 4);
            if (edgeF > 0)
            {
                int blk = k.M(SurfaceKind.MatteBlack);
                float len = L * edgeF;
                foreach (float s in new[] { -1f, 1f })
                {
                    k.B.AddBox(new Vector3(s * (hw + Units.In(0.1f)), -Units.In(1.6f), -hl + len / 2), new Vector3(Units.In(0.2f), Units.In(2.2f), len), Quaternion.identity, blk); // top 1/2" below the pavers, soil over it
                    k.B.AddBox(new Vector3(s * (hw + Units.In(1.2f)), -PaverDepthBase(), -hl + len / 2), new Vector3(Units.In(2.2f), Units.In(0.2f), len), Quaternion.identity, blk);
                }
            }
            k.Make(parent, "Walkway");
            col.center = new Vector3(0, -0.02f, 0); col.size = new Vector3(W + 0.3f, 0.04f, L + 0.3f);
        }

        static float PaverDepthBase() => LandscapeFeature.PaverT - Units.In(0.1f);

        // ------------------------------------------------------------------ drive

        static void Drive(LandscapeFeature f, Transform parent, BoxCollider col)
        {
            float W = f.Width, L = f.Length;
            var k = new LandModel();
            if (!f.Done) Layout(k, W / 2, L / 2);
            float baseF = Frac(f, 2), topF = Frac(f, 3);
            if (!f.Done && baseF > 0)
            {
                float t = Units.In(4) * baseF;
                k.B.AddBox(new Vector3(0, -LandscapeFeature.DriveDepth + t / 2, 0), new Vector3(W, t, L), Quaternion.identity, k.M(SurfaceKind.Gravel, new Color(0.82f, 0.82f, 0.85f)));
            }
            if (!f.Done && topF > 0)
            {
                float t = Units.In(2);
                float len = L * topF;
                k.B.AddBox(new Vector3(0, -t / 2, -L / 2 + len / 2), new Vector3(W, t, len), Quaternion.identity, k.M(SurfaceKind.Gravel));
            }
            k.Make(parent, "Drive");
            col.center = new Vector3(0, -0.02f, 0); col.size = new Vector3(W + 0.3f, 0.04f, L + 0.3f);
        }

        // ------------------------------------------------------------------ fence / gate

        static void Fence(LandscapeFeature f, Transform parent, BoxCollider col)
        {
            float L = f.Length, hl = L / 2;
            int posts = f.Posts;
            bool gate = f.Kind == LandscapeKind.Gate;
            var k = new LandModel();
            int dirt = k.M(LandSurface.Topsoil, new Color(0.55f, 0.5f, 0.45f)), wood = k.M(SurfaceKind.TreatedLumber), conc = k.M(SurfaceKind.Concrete), cedar = k.M(LandSurface.Cedar), blk = k.M(SurfaceKind.MatteBlack);
            float post = Units.In(3.5f);
            float Local(int i) => gate ? (i == 0 ? -hl : hl) : -hl + i * (L / f.Bays);
            float GroundY(int i) { var w = f.PostAt(i); return w.y - f.BaseY; }

            if (f.Stage <= 1)
            {
                LandModels.Stake(k, new Vector3(0, GroundY(0), -hl - 0.2f));
                LandModels.Stake(k, new Vector3(0, GroundY(posts - 1), hl + 0.2f));
                LandModels.String(k, new Vector3(0, GroundY(0) + Units.In(4), -hl - 0.2f), new Vector3(0, GroundY(posts - 1) + Units.In(4), hl + 0.2f), Pink);
            }
            int holesDug = f.Stage > 0 ? posts : f.Progress / 2;
            int postsSet = f.Stage > 1 ? posts : f.Stage == 1 ? f.Progress : 0;
            int poured = f.Stage > 2 ? posts : f.Stage == 2 ? f.Progress : 0;
            for (int i = 0; i < posts; i++)
            {
                float z = Local(i), gy = GroundY(i);
                if (i < holesDug && i >= poured)
                {
                    if (i >= postsSet) k.B.AddDisc(new Vector3(0, gy + 0.004f, z), Quaternion.identity, Units.In(5), 14, dirt, true);
                    else k.B.AddDisc(new Vector3(0, gy + 0.004f, z), Quaternion.identity, Units.In(5), 14, dirt, true);
                }
                else if (f.Stage == 0 && i == holesDug && f.Progress % 2 == 1)
                    k.B.AddDisc(new Vector3(0, gy + 0.004f, z), Quaternion.identity, Units.In(3.5f), 12, dirt, true);
                if (i < holesDug && i >= poured)
                {
                    // spoil pile next to the hole
                    k.B.Transform = Matrix4x4.TRS(new Vector3(Units.In(12), gy, z + Units.In(3)), Quaternion.Euler(0, i * 47, 0), new Vector3(1.2f, 1, 0.9f));
                    k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0.17f, 0), new Vector2(0.12f, 0.05f), new Vector2(0.05f, 0.09f), new Vector2(0.001f, 0.1f) }, 9, dirt, 60f);
                    k.B.ResetTransform();
                }
                if (i < postsSet)
                {
                    float above = gate ? 6 * Units.Foot : LandscapeFeature.FenceHeight;
                    k.B.AddBox(new Vector3(0, gy + (above - Units.In(24)) / 2, z), new Vector3(post, above + Units.In(24), post), Quaternion.identity, wood, Vector3.up);
                }
                if (i < poured)
                    k.B.AddFrustum(new Vector3(0, gy + Units.In(0.75f), z), Quaternion.identity, Units.In(5.5f), Units.In(2.4f), Units.In(1.5f), 16, conc, false);
            }

            if (!gate)
            {
                int bays = f.Bays;
                float sp = L / bays;
                int railsTotal = bays * 3, rails = f.Stage > 3 ? railsTotal : f.Stage == 3 ? f.Progress : 0;
                float[] h = { Units.In(8), Units.In(38), Units.In(68) };
                float railX = -(post / 2 + Units.In(0.75f));
                for (int r = 0; r < rails; r++)
                {
                    int bay = r / 3, lvl = r % 3;
                    float za = Local(bay), zb = Local(bay + 1), ya = GroundY(bay) + h[lvl], yb = GroundY(bay + 1) + h[lvl];
                    var a = new Vector3(railX, ya, za - post / 2); var b = new Vector3(railX, yb, zb + post / 2);
                    k.B.AddBox((a + b) / 2, new Vector3(Units.In(1.5f), Units.In(3.5f), Vector3.Distance(a, b)), Quaternion.LookRotation(b - a), wood, Vector3.forward);
                }
                int baysPicketed = f.Stage > 4 ? bays : f.Stage == 4 ? f.Progress : 0;
                float pw = Units.In(5.5f), pt = Units.In(0.625f), ph = 6 * Units.Foot - Units.In(2);
                var poly = new[] { new Vector2(-pw / 2, 0), new Vector2(pw / 2, 0), new Vector2(pw / 2, ph - Units.In(0.75f)), new Vector2(pw / 2 - Units.In(0.75f), ph), new Vector2(-pw / 2 + Units.In(0.75f), ph), new Vector2(-pw / 2, ph - Units.In(0.75f)) };
                float px = railX - Units.In(0.75f) - pt / 2;
                var rng = new System.Random(f.GetHashCode());
                for (int bay = 0; bay < baysPicketed; bay++)
                {
                    int n = Mathf.Max(1, Mathf.FloorToInt(sp / (pw + Units.In(0.375f)))); // ~3/8" gaps: boards shrink, and the gaps read as shadow lines
                    float za = Local(bay);
                    for (int p = 0; p < n; p++)
                    {
                        float z = za + (p + 0.5f) * (sp / n);
                        float gy = Mathf.Lerp(GroundY(bay), GroundY(bay + 1), (p + 0.5f) / n);
                        k.B.UvOffset = new Vector2((float)rng.NextDouble() * 4f, (float)rng.NextDouble() * 4f);
                        // extrude the dog-ear outline through the board thickness (local Z -> run X)
                        k.B.AddExtrude(poly, new Vector3(px, gy + Units.In(2), z), Quaternion.Euler(0, -90, 0), pt, cedar);
                    }
                }
                k.B.UvOffset = Vector2.zero;
            }
            else if (f.Done)
            {
                // 4' gate leaf: cedar boards on a Z-braced frame, black strap hinges + latch
                float gw = L - post - Units.In(1), gh = Units.In(70), y0 = GroundY(0) + Units.In(3);
                int boards = Mathf.Max(2, Mathf.RoundToInt(gw / Units.In(5.5f)));
                float bw = gw / boards;
                for (int b = 0; b < boards; b++)
                    k.B.AddBox(new Vector3(-post / 2 - Units.In(1.1f), y0 + gh / 2, -gw / 2 + (b + 0.5f) * bw), new Vector3(Units.In(0.625f), gh, bw - Units.In(0.1f)), Quaternion.identity, cedar, Vector3.forward); // cedar grain runs along V: U across the board = vertical grain
                float fx = -post / 2 - Units.In(0.1f);
                foreach (float yy in new[] { Units.In(8), Units.In(62) })
                    k.B.AddBox(new Vector3(fx - Units.In(0.25f), y0 + yy, 0), new Vector3(Units.In(1.5f), Units.In(3.5f), gw - Units.In(1)), Quaternion.identity, cedar, Vector3.up);
                var d0 = new Vector3(fx - Units.In(0.25f), y0 + Units.In(10), -gw / 2 + Units.In(3)); var d1 = new Vector3(fx - Units.In(0.25f), y0 + Units.In(60), gw / 2 - Units.In(3));
                k.B.AddBox((d0 + d1) / 2, new Vector3(Units.In(1.5f), Units.In(3.5f), Vector3.Distance(d0, d1)), Quaternion.LookRotation(d1 - d0), cedar, Vector3.up);
                foreach (float yy in new[] { Units.In(10), Units.In(60) })
                    k.B.AddBox(new Vector3(-post / 2 - Units.In(1.6f), y0 + yy, -gw / 2 + Units.In(4)), new Vector3(Units.In(0.15f), Units.In(1.5f), Units.In(12)), Quaternion.identity, blk);
                k.B.AddBox(new Vector3(-post / 2 - Units.In(1.6f), y0 + Units.In(40), gw / 2 - Units.In(1.5f)), new Vector3(Units.In(0.4f), Units.In(1.2f), Units.In(4)), Quaternion.identity, blk);
            }
            k.Make(parent, gate ? "Gate" : "Fence");
            float hgt = f.Stage >= 1 ? 1.85f : 0.4f;
            col.center = new Vector3(0, hgt / 2, 0); col.size = new Vector3(0.2f, hgt, L + 0.3f);
        }

        // ------------------------------------------------------------------ plants

        static string PlantKey(string item, int seed, out System.Action<LandModel> build)
        {
            seed = Mathf.Abs(seed) % 3;
            switch (item)
            {
                case "tree-maple": build = k => LandModels.Deciduous(k, 11 + seed, 1f, LandModels.LeafMaple); return "maple" + seed;
                case "tree-spruce": build = k => LandModels.Conifer(k, 21 + seed, 1.85f, LandModels.LeafSpruce); return "spruce" + seed;
                case "shrub-boxwood": build = k => LandModels.Shrub(k, 31 + seed, 0.6f, 0.55f, LandModels.LeafBox); return "boxwood" + seed;
                case "shrub-hydrangea": build = k => LandModels.Shrub(k, 41 + seed, 0.75f, 0.7f, new Color(0.8f, 1f, 0.7f), new Color(1.6f, 1.8f, 2.6f)); return "hydrangea" + seed;
                default: build = k => LandModels.Hosta(k, 51 + seed); return "hosta" + seed;
            }
        }

        public static GameObject PlantModel(string itemId, int seed, Transform parent)
        {
            string key = PlantKey(itemId, seed, out var build);
            return LandModels.Instance(key, parent, build);
        }

        static void Plant(LandscapeFeature f, Transform parent, BoxCollider col)
        {
            var spec = PlantSpec.For(f.ItemId);
            float gy = Grade.HeightAt(f.A.x, f.A.z) - f.A.y;
            var k = new LandModel();
            int soil = k.M(LandSurface.Topsoil, new Color(0.55f, 0.5f, 0.45f));
            float dig = Frac(f, 0), back = Frac(f, 2);
            if (f.Stage == 0 && f.Progress == 0)
            {
                // marking-paint ring where the hole goes
                int paint = k.M(Orange);
                for (int i = 0; i < 16; i++)
                {
                    float a = i / 16f * Mathf.PI * 2;
                    k.B.AddBox(new Vector3(Mathf.Cos(a) * spec.HoleR, gy + 0.004f, Mathf.Sin(a) * spec.HoleR), new Vector3(0.05f, 0.004f, spec.HoleR * 0.3f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), paint);
                }
            }
            if (dig > 0 && f.Stage < 3 && back < 1f)
            {
                float r = spec.HoleR * Mathf.Lerp(0.4f, 1f, dig) * (1f - back * 0.9f);
                k.B.AddDisc(new Vector3(0, gy + 0.005f, 0), Quaternion.identity, r, 20, k.M(new Color(0.12f, 0.09f, 0.06f)), true);
                float vol = Mathf.PI * spec.HoleR * spec.HoleR * spec.HoleR * 0.9f * dig * (1f - back);
                k.B.Transform = Matrix4x4.Translate(new Vector3(spec.HoleR + 0.35f, gy, 0));
                float h = Mathf.Clamp(Mathf.Pow(vol, 1f / 3f) * 0.55f, 0.04f, 0.5f), rr = Mathf.Sqrt(3f * Mathf.Max(vol, 0.002f) / (Mathf.PI * h)) * 0.8f;
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(rr, 0), new Vector2(rr * 0.7f, h * 0.4f), new Vector2(rr * 0.25f, h * 0.95f), new Vector2(0.001f, h) }, 12, soil, 60f);
                k.B.ResetTransform();
            }
            k.Make(parent, "Ground");
            if (f.Stage >= 2)
            {
                var plant = PlantModelFor(f, parent);
                plant.transform.localPosition = new Vector3(0, gy, 0);
                if (f.Stage == 2 && back < 1f)
                {
                    var ball = LandModels.Instance("ball" + Mathf.RoundToInt(spec.BallR * 100), parent, kk => LandModels.RootBall(kk, spec.BallR));
                    ball.transform.localPosition = new Vector3(0, gy - spec.BallR * 1.6f * (1f - back * 0.3f) + spec.BallR * 0.5f, 0);
                }
            }
            float w = spec.Tree ? 0.25f : spec.HoleR * 1.6f, ht = f.Stage >= 2 ? spec.Height : 0.1f;
            col.center = new Vector3(0, gy + ht / 2, 0); col.size = new Vector3(w, ht, w);
        }

        static GameObject PlantModelFor(LandscapeFeature f, Transform parent)
        {
            string key = PlantKey(f.ItemId, Mathf.RoundToInt(f.A.x * 7.3f + f.A.z * 3.1f), out var build);
            return LandModels.Instance(key, parent, build);
        }
    }
}
