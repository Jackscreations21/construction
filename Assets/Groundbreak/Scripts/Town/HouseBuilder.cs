using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Procedural one-storey ranch (slab on grade, 8' ceilings, 5/12 gable roof). House frame: origin at grade in the middle of the
    /// footprint, +Z = front. Plan (x from -W/2 left to +W/2 right, seen from the street looking at the house... i.e. +X is the
    /// house's left when you stand outside facing it): living room + kitchen on -X, partition at x = <see cref="P"/>, bedroom front +X,
    /// bath back +X (x P..P+2.4). Kitchen counters + sink on the back wall at -X, patio door + deck at the back, fence on the back lot line.
    /// One merged mesh per house; doors, outlets, panel, a deck board, a picket and a sidewalk square are separate fixtures.
    /// </summary>
    public static class HouseBuilder
    {
        public const float P = 1.0f;                    // living / bedroom partition (house-local x)
        public const float T = 0.15f, Ti = 0.12f;       // exterior / interior wall thickness
        public const float D = 8.5f;                    // depth (28')
        static readonly float[] Widths = { 10.4f, 11.0f, 11.6f };   // 34', 36', 38'
        const float Pitch = 5f / 12f, Over = 0.3f, Heel = 0.12f;

        static readonly Color[] SidingColors =
        {
            new Color(0.95f, 0.95f, 0.92f), new Color(0.80f, 0.82f, 0.80f), new Color(0.86f, 0.80f, 0.66f),
            new Color(0.70f, 0.78f, 0.68f), new Color(0.66f, 0.74f, 0.82f), new Color(0.93f, 0.88f, 0.76f),
        };
        static readonly Color[] RoofTints = { new Color(0.75f, 0.75f, 0.75f), new Color(0.55f, 0.52f, 0.50f), new Color(0.8f, 0.72f, 0.62f) };
        static readonly Color[] PaintTints = { new Color(0.97f, 0.95f, 0.90f), new Color(0.90f, 0.91f, 0.90f), new Color(0.95f, 0.90f, 0.82f) };

        static readonly Dictionary<Color, Material> _roofMats = new Dictionary<Color, Material>();
        static Material RoofMat(Color tint)
        {
            if (_roofMats.TryGetValue(tint, out var m) && m != null) return m;
            var b = RoofTextures.Shingle;
            m = new Material(b) { name = b.name + "_town", hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.color = b.color * tint;
            _roofMats[tint] = m;
            return m;
        }

        // ---- per-build state
        static LandModel _k;
        static GameObject _go;
        static TownHouse _h;
        static int _siding, _paint, _trim, _drywallCeil, _concrete, _floor, _glass, _tile;
        static float F, C, PT, W;

        struct Opening { public float c, w, sill, head; public bool glass; public Opening(float c, float w, float sill, float head, bool glass) { this.c = c; this.w = w; this.sill = sill; this.head = head; this.glass = glass; } }

        static int Hash(string s) { int h = 17; foreach (char ch in s) h = h * 31 + ch; return h & 0x7fffffff; }

        /// <summary>Builds the house for a lot. sidewalkSquare = world centre (top) of the public sidewalk square that is this house's fixture.</summary>
        public static TownHouse Build(TownLot lot, Transform parent, Vector3 sidewalkSquare)
        {
            int seed = Hash(lot.Id);
            W = Widths[seed % Widths.Length];
            Color siding = lot.Use == LotUse.PlayerHome ? new Color(0.56f, 0.64f, 0.72f) : SidingColors[(seed / 3) % SidingColors.Length];
            Color roofT = RoofTints[(seed / 7) % RoofTints.Length];
            Color paint = PaintTints[(seed / 11) % PaintTints.Length];

            _go = new GameObject("House " + lot.Address);
            _go.transform.SetParent(parent, false);
            _go.transform.position = lot.Local(0f, TownHouse.Setback + D / 2f);
            _go.transform.rotation = lot.Rotation;
            _h = _go.AddComponent<TownHouse>();
            _h.Lot = lot; _h.Width = W; _h.Depth = D; _h.Siding = siding;
            F = _h.FloorY; C = F + _h.CeilingHeight; PT = F + 2.6f;

            _k = new LandModel();
            _siding = _k.M(FinishMaterials.Get(ExteriorLooks.Vinyl, siding));
            _paint = _k.M(SurfaceKind.PaintedWhite, paint);
            _trim = _k.M(SurfaceKind.PaintedWhite);
            _drywallCeil = _k.M(SurfaceKind.PaintedWhite, new Color(0.98f, 0.98f, 0.97f));
            _concrete = _k.M(SurfaceKind.Concrete);
            _floor = _k.M(SurfaceKind.Oak);
            _glass = _k.M(SurfaceKind.Glass);
            _tile = _k.M(SurfaceKind.Porcelain);
            int roof = _k.M(RoofMat(roofT));

            float hw = W / 2f, hd = D / 2f;

            // ---- slab, floors, ceiling
            Solid(new Vector3(-hw, 0, -hd), new Vector3(hw, F, hd), _concrete, MeshBuilder.AllFaces & ~MeshBuilder.FacePosY);
            _k.B.AddQuad(new Vector3(-hw, F, -hd), new Vector3(-hw, F, hd), new Vector3(hw, F, hd), new Vector3(hw, F, -hd), _floor, Vector3.up, Vector3.right);
            _k.B.AddQuad(new Vector3(P, F + 0.003f, -hd + T), new Vector3(P, F + 0.003f, -0.6f), new Vector3(P + 2.4f, F + 0.003f, -0.6f), new Vector3(P + 2.4f, F + 0.003f, -hd + T), _tile, Vector3.up);
            _k.B.AddQuad(new Vector3(-hw, C, -hd), new Vector3(-hw, C, hd), new Vector3(hw, C, hd), new Vector3(hw, C, -hd), _drywallCeil, Vector3.down);
            Col(new Vector3(-hw, 0, -hd), new Vector3(hw, F, hd));
            // stoop + walk
            float dx = TownHouse.FrontDoorX;
            Solid(new Vector3(dx - 0.9f, 0, hd), new Vector3(dx + 0.9f, F - 0.02f, hd + 1.1f), _concrete);
            Col(new Vector3(dx - 0.9f, 0, hd), new Vector3(dx + 0.9f, F - 0.02f, hd + 1.1f));
            Solid(new Vector3(dx - 0.45f, 0, hd + 1.1f), new Vector3(dx + 0.45f, 0.05f, hd + TownHouse.Setback - 1.0f), _concrete);

            // ---- exterior walls
            float winHead = 2.1f;
            float livingWin = (-hw + T + dx - 0.46f) / 2f, bedWin = (P + hw) / 2f, xs = -hw + 1.8f;
            Wall(true, hd - T / 2, -hw, hw, T, +1, new[] {
                new Opening(dx, 0.914f, 0, 2.032f, false), new Opening(livingWin, 1.2f, 0.9f, winHead, true), new Opening(bedWin, 1.0f, 0.9f, winHead, true) });
            Wall(true, -hd + T / 2, -hw, hw, T, -1, new[] {
                new Opening(xs, 0.9f, 1.07f, winHead, true), new Opening(-0.75f, 1.83f, 0, 2.032f, false),
                new Opening(P + 1.96f, 0.6f, 1.4f, 2.0f, true), new Opening((P + 2.4f + hw) / 2f, 0.9f, 0.9f, winHead, true) });
            Wall(false, -hw + T / 2, -hd + T, hd - T, T, -1, new[] { new Opening(3.0f, 0.9f, 0.9f, winHead, true) });
            Wall(false, hw - T / 2, -hd + T, hd - T, T, +1, new Opening[0]);
            // patio slider: fixed glass panel on the left half, the right half open
            Solid(new Vector3(-0.75f - 0.915f, F, -hd + 0.07f), new Vector3(-0.75f, F + 2.03f, -hd + 0.08f), _glass);
            Solid(new Vector3(-0.75f - 0.915f, F, -hd + 0.06f), new Vector3(-0.75f - 0.88f, F + 2.03f, -hd + 0.09f), _trim);

            // ---- interior walls
            Wall(false, P, -hd + T, hd - T, Ti, 0, new[] { new Opening(1.5f, 0.813f, 0, 2.032f, false), new Opening(-2.5f, 0.762f, 0, 2.032f, false) }, C);
            Wall(true, -0.6f, P + Ti / 2, P + 2.4f + Ti / 2, Ti, 0, new Opening[0], C);
            Wall(false, P + 2.4f, -hd + T, -0.6f - Ti / 2, Ti, 0, new Opening[0], C);

            // ---- roof
            float eZ = hd + Over, eY = PT + Heel - Over * Pitch, rY = PT + Heel + hd * Pitch, rx = hw + Over, th = 0.16f;
            for (int s = -1; s <= 1; s += 2)
            {
                var a = new Vector3(-rx, eY, s * eZ); var b = new Vector3(rx, eY, s * eZ);
                var c = new Vector3(rx, rY, 0); var d = new Vector3(-rx, rY, 0);
                var n = new Vector3(0, 1, s * Pitch).normalized;
                _k.B.AddQuad(a, b, c, d, roof, n, Vector3.right);
                var down = Vector3.down * th;
                _k.B.AddQuad(a + down, b + down, c + down, d + down, _trim, -n);
                _k.B.AddQuad(a + down, a, d, d + down, _trim, Vector3.left);
                _k.B.AddQuad(b + down, b, c, c + down, _trim, Vector3.right);
                Solid(new Vector3(-rx, eY - 0.2f, s * eZ - 0.025f), new Vector3(rx, eY, s * eZ + 0.025f), _trim); // fascia
                // walkable roof plane
                float ang = Mathf.Atan(Pitch) * Mathf.Rad2Deg;
                var rc = new GameObject(s > 0 ? "RoofFront" : "RoofBack");
                rc.transform.SetParent(_go.transform, false);
                rc.transform.localPosition = new Vector3(0, (eY + rY) / 2f - 0.03f, s * eZ / 2f);
                rc.transform.localRotation = Quaternion.Euler(s * ang, 0, 0);
                rc.AddComponent<BoxCollider>().size = new Vector3(2 * rx, 0.06f, eZ / Mathf.Cos(Mathf.Atan(Pitch)));
            }
            for (int s = -1; s <= 1; s += 2) // gable ends
                _k.B.AddPolygon(new[] { new Vector3(s * hw, PT, -hd), new Vector3(s * hw, PT + Heel, -hd), new Vector3(s * hw, rY - 0.02f, 0), new Vector3(s * hw, PT + Heel, hd), new Vector3(s * hw, PT, hd) },
                    _siding, new Vector3(s, 0, 0), Vector3.forward);

            // ---- gutter (front, open K-style trough) + downspout
            float gz = eZ + 0.025f + 0.065f, gTop = eY - 0.03f, gB = gTop - 0.127f;
            _h.EaveHeight = gTop;
            Solid(new Vector3(-rx, gB, gz - 0.065f), new Vector3(rx, gB + 0.008f, gz + 0.065f), _trim);
            Solid(new Vector3(-rx, gB, gz - 0.068f), new Vector3(rx, gTop, gz - 0.062f), _trim);
            Solid(new Vector3(-rx, gB, gz + 0.062f), new Vector3(rx, gTop, gz + 0.068f), _trim);
            Solid(new Vector3(-rx - 0.006f, gB, gz - 0.068f), new Vector3(-rx, gTop, gz + 0.068f), _trim);
            Solid(new Vector3(rx, gB, gz - 0.068f), new Vector3(rx + 0.006f, gTop, gz + 0.068f), _trim);
            float dsX = hw + 0.1f;
            Solid(new Vector3(dsX - 0.025f, 0.1f, gz - 0.038f), new Vector3(dsX + 0.025f, gB, gz + 0.038f), _trim);
            Solid(new Vector3(dsX - 0.025f, 0.02f, gz - 0.038f), new Vector3(dsX + 0.025f, 0.1f, gz + 0.35f), _trim);

            // ---- kitchen
            Kitchen(xs, hd);
            // ---- bath
            Bath(hd);
            // ---- furniture (simple)
            Furn(new Vector3(-3.95f, F, 0.55f), new Vector3(-1.85f, F + 0.85f, 1.45f), new Color(0.35f, 0.40f, 0.46f)); // sofa
            Furn(new Vector3(2.0f, F, -0.5f), new Vector3(3.53f, F + 0.6f, 1.5f), new Color(0.80f, 0.78f, 0.72f));      // queen bed

            // ---- deck, fence
            Deck(hd);
            Fence(lot);

            var mesh = _k.BuildMesh("House");
            _go.AddComponent<MeshFilter>().sharedMesh = mesh;
            _go.AddComponent<MeshRenderer>().sharedMaterials = _k.Mats;

            // ---- fixtures + anchors
            Fixtures(xs, hd, sidewalkSquare);

            // lights (no shadows): living + bedroom
            AddLight(new Vector3(-2.5f, C - 0.25f, 0f)); AddLight(new Vector3(3.2f, C - 0.25f, 1.2f));
            lot.House = _h;
            var house = _h; _k = null; _go = null; _h = null;
            return house;
        }

        static void AddLight(Vector3 local)
        {
            var l = new GameObject("RoomLight").AddComponent<Light>();
            l.transform.SetParent(_go.transform, false); l.transform.localPosition = local;
            l.type = LightType.Point; l.range = 6.5f; l.intensity = 0.7f; l.color = new Color(1f, 0.93f, 0.82f); l.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ helpers

        static void Solid(Vector3 min, Vector3 max, int sub, int mask = MeshBuilder.AllFaces, Vector3 u = default)
        {
            Vector3 size = max - min;
            if (size.x <= 0.0005f || size.y <= 0.0005f || size.z <= 0.0005f) return;
            _k.B.AddBox((min + max) / 2f, size, Quaternion.identity, sub, u, mask);
        }

        static void Col(Vector3 min, Vector3 max)
        {
            var bc = _go.AddComponent<BoxCollider>();
            bc.center = (min + max) / 2f; bc.size = max - min;
        }

        static void Furn(Vector3 min, Vector3 max, Color c) { Solid(min, max, _k.M(c)); Col(min, max); }

        /// <summary>Axis-aligned wall. alongX: runs along X at z = pos. outSign: which side is exterior (+1 = +Z/+X face), 0 = interior wall.</summary>
        static void Wall(bool alongX, float pos, float a0, float a1, float t, int outSign, Opening[] ops, float top = -1f)
        {
            if (top < 0f) top = PT;
            var list = new List<Opening>(ops);
            list.Sort((x, y) => x.c.CompareTo(y.c));
            float cur = a0;
            foreach (var o in list)
            {
                float o0 = o.c - o.w / 2f, o1 = o.c + o.w / 2f;
                WallPiece(alongX, pos, cur, o0, F, top, t, outSign);
                if (o.sill > 0) WallPiece(alongX, pos, o0, o1, F, F + o.sill, t, outSign);
                WallPiece(alongX, pos, o0, o1, F + o.head, top, t, outSign);
                if (o.glass)
                {
                    Vector3 gmin = alongX ? new Vector3(o0, F + o.sill, pos - 0.003f) : new Vector3(pos - 0.003f, F + o.sill, o0);
                    Vector3 gmax = alongX ? new Vector3(o1, F + o.head, pos + 0.003f) : new Vector3(pos + 0.003f, F + o.head, o1);
                    Solid(gmin, gmax, _glass);
                }
                if (outSign != 0) Casing(alongX, pos + outSign * t / 2f, outSign, o0, o1, F + o.sill, F + o.head, o.sill > 0);
                cur = o1;
            }
            WallPiece(alongX, pos, cur, a1, F, top, t, outSign);
        }

        static void WallPiece(bool alongX, float pos, float s0, float s1, float y0, float y1, float t, int outSign)
        {
            if (s1 - s0 < 0.002f || y1 - y0 < 0.002f) return;
            Vector3 min = alongX ? new Vector3(s0, y0, pos - t / 2) : new Vector3(pos - t / 2, y0, s0);
            Vector3 max = alongX ? new Vector3(s1, y1, pos + t / 2) : new Vector3(pos + t / 2, y1, s1);
            int pos_ = alongX ? MeshBuilder.FacePosZ : MeshBuilder.FacePosX, neg = alongX ? MeshBuilder.FaceNegZ : MeshBuilder.FaceNegX;
            int edges = MeshBuilder.AllFaces & ~pos_ & ~neg;
            Vector3 u = alongX ? Vector3.right : Vector3.forward;
            if (outSign == 0) { Solid(min, max, _paint, pos_ | neg, u); }
            else
            {
                Solid(min, max, _siding, outSign > 0 ? pos_ : neg, u);
                Solid(min, max, _paint, outSign > 0 ? neg : pos_, u);
            }
            Solid(min, max, _trim, edges, u);
            Col(min, max);
        }

        static void Casing(bool alongX, float face, int outSign, float o0, float o1, float y0, float y1, bool sill)
        {
            const float w = 0.09f, d = 0.025f;
            float f0 = outSign > 0 ? face : face - d, f1 = outSign > 0 ? face + d : face;
            void B(float s0, float s1, float a, float b)
            {
                if (alongX) Solid(new Vector3(s0, a, f0), new Vector3(s1, b, f1), _trim);
                else Solid(new Vector3(f0, a, s0), new Vector3(f1, b, s1), _trim);
            }
            B(o0 - w, o0, y0 - (sill ? w : 0), y1 + w);
            B(o1, o1 + w, y0 - (sill ? w : 0), y1 + w);
            B(o0, o1, y1, y1 + w);
            if (sill) B(o0 - w, o1 + w, y0 - w, y0);
        }

        // ------------------------------------------------------------------ rooms

        static void Kitchen(float xs, float hd)
        {
            float hw = W / 2f, zb = -hd + T, zf = zb + 0.61f, x0 = -hw + T, x1 = -hw + 3.3f;
            int box = _k.M(SurfaceKind.Oak), top = _k.M(new Color(0.22f, 0.22f, 0.24f)), steel = _k.M(SurfaceKind.BrushedSteel), chrome = _k.M(SurfaceKind.Nickel);
            float s0 = xs - 0.457f, s1 = xs + 0.457f, ct = F + 0.876f;
            // cabinets either side of the sink base (closed boxes on a toe kick)
            foreach (var (a, b) in new[] { (x0, s0), (s1, x1) })
            {
                Solid(new Vector3(a, F + 0.1f, zb), new Vector3(b, ct, zf), box);
                Solid(new Vector3(a, F, zb), new Vector3(b, F + 0.1f, zf - 0.075f), _k.M(SurfaceKind.DarkPlastic));
                Col(new Vector3(a, F, zb), new Vector3(b, ct, zf));
            }
            // sink base: open front, sides, floor; plumbing stubs inside
            Solid(new Vector3(s0, F, zb), new Vector3(s0 + 0.019f, ct, zf), box);
            Solid(new Vector3(s1 - 0.019f, F, zb), new Vector3(s1, ct, zf), box);
            Solid(new Vector3(s0, F + 0.1f, zb), new Vector3(s1, F + 0.12f, zf), box);
            Solid(new Vector3(s0, F, zb), new Vector3(s1, F + 0.1f, zf - 0.075f), _k.M(SurfaceKind.DarkPlastic));
            Col(new Vector3(s0, F, zb), new Vector3(s0 + 0.019f, ct, zf)); Col(new Vector3(s1 - 0.019f, F, zb), new Vector3(s1, ct, zf));
            // countertop with sink cutout
            float zc = (zb + zf + 0.025f) / 2f, ctT = ct + 0.038f, bx = 0.38f, bz = 0.225f;
            Solid(new Vector3(x0, ct, zb), new Vector3(xs - bx, ctT, zf + 0.025f), top);
            Solid(new Vector3(xs + bx, ct, zb), new Vector3(x1, ctT, zf + 0.025f), top);
            Solid(new Vector3(xs - bx, ct, zb), new Vector3(xs + bx, ctT, zc - bz), top);
            Solid(new Vector3(xs - bx, ct, zc + bz), new Vector3(xs + bx, ctT, zf + 0.025f), top);
            Col(new Vector3(x0, ct, zb), new Vector3(x1, ctT, zf + 0.025f));
            _k.B.AddWell(new Vector3(xs, ctT, zc), new Vector2(2 * bx, 2 * bz), new Vector2(2 * bx - 0.06f, 2 * bz - 0.06f), 0.2f, steel);
            _k.B.AddCylinder(new Vector3(xs, ctT - 0.2f - 0.1f, zc), 0.019f, 0.1f, 10, chrome);        // tailpiece stub (1-1/2")
            _k.B.AddCylinder(new Vector3(xs, ctT, zc - bz - 0.06f), 0.015f, 0.28f, 10, chrome);         // faucet body
            _k.B.AddBox(new Vector3(xs, ctT + 0.27f, zc - bz + 0.02f), new Vector3(0.025f, 0.025f, 0.2f), Quaternion.identity, chrome);
            for (int s = -1; s <= 1; s += 2) // angle stops on the back wall
            {
                _k.B.AddBox(new Vector3(xs + s * 0.2f, F + 0.45f, zb + 0.04f), new Vector3(0.025f, 0.025f, 0.08f), Quaternion.identity, chrome);
                _k.B.AddBox(new Vector3(xs + s * 0.2f, F + 0.45f, zb + 0.085f), new Vector3(0.05f, 0.03f, 0.012f), Quaternion.identity, _k.M(new Color(0.85f, 0.85f, 0.8f)));
            }
            // upper cabinets (not over the sink window)
            foreach (var (a, b) in new[] { (x0, xs - 0.55f), (xs + 0.55f, x1) })
            {
                Solid(new Vector3(a, F + 1.37f, zb), new Vector3(b, F + 2.13f, zb + 0.31f), box);
                Col(new Vector3(a, F + 1.37f, zb), new Vector3(b, F + 2.13f, zb + 0.31f));
            }
        }

        static void Bath(float hd)
        {
            float zb = -hd + T;
            int box = _k.M(SurfaceKind.Oak), por = _k.M(SurfaceKind.Porcelain), chrome = _k.M(SurfaceKind.Nickel);
            // vanity 30" x 21" x 32" with open front
            float xv = P + Ti / 2 + 0.45f, v0 = xv - 0.381f, v1 = xv + 0.381f, zf = zb + 0.53f, ct = F + 0.79f;
            Solid(new Vector3(v0, F, zb), new Vector3(v0 + 0.019f, ct, zf), box);
            Solid(new Vector3(v1 - 0.019f, F, zb), new Vector3(v1, ct, zf), box);
            Solid(new Vector3(v0, F + 0.1f, zb), new Vector3(v1, F + 0.12f, zf), box);
            Col(new Vector3(v0, F, zb), new Vector3(v0 + 0.019f, ct, zf)); Col(new Vector3(v1 - 0.019f, F, zb), new Vector3(v1, ct, zf));
            float zc = (zb + zf) / 2f, tt = ct + 0.03f;
            Solid(new Vector3(v0, ct, zb), new Vector3(v1, tt, zc - 0.17f), por);
            Solid(new Vector3(v0, ct, zc + 0.17f), new Vector3(v1, tt, zf + 0.02f), por);
            Solid(new Vector3(v0, ct, zc - 0.17f), new Vector3(xv - 0.2f, tt, zc + 0.17f), por);
            Solid(new Vector3(xv + 0.2f, ct, zc - 0.17f), new Vector3(v1, tt, zc + 0.17f), por);
            Col(new Vector3(v0, ct, zb), new Vector3(v1, tt, zf + 0.02f));
            _k.B.AddWell(new Vector3(xv, tt, zc), new Vector2(0.4f, 0.34f), new Vector2(0.3f, 0.24f), 0.14f, por);
            _k.B.AddCylinder(new Vector3(xv, tt - 0.14f - 0.09f, zc), 0.016f, 0.09f, 10, chrome);
            _k.B.AddCylinder(new Vector3(xv, tt, zc - 0.2f), 0.012f, 0.16f, 10, chrome);
            for (int s = -1; s <= 1; s += 2)
                _k.B.AddBox(new Vector3(xv + s * 0.15f, F + 0.42f, zb + 0.04f), new Vector3(0.022f, 0.022f, 0.08f), Quaternion.identity, chrome);
            Solid(new Vector3(xv - 0.35f, F + 1.05f, zb), new Vector3(xv + 0.35f, F + 1.85f, zb + 0.012f), _k.M(SurfaceKind.Glass)); // mirror
            // 60" alcove tub against the bath's east wall
            float tx1 = P + 2.4f - Ti / 2, tx0 = tx1 - 0.762f, tz0 = zb, tz1 = zb + 1.524f, th = F + 0.406f;
            Solid(new Vector3(tx0, F, tz0), new Vector3(tx1, th, tz0 + 0.06f), por);
            Solid(new Vector3(tx0, F, tz1 - 0.06f), new Vector3(tx1, th, tz1), por);
            Solid(new Vector3(tx0, F, tz0 + 0.06f), new Vector3(tx0 + 0.07f, th, tz1 - 0.06f), por);
            Solid(new Vector3(tx1 - 0.04f, F, tz0 + 0.06f), new Vector3(tx1, th, tz1 - 0.06f), por);
            Solid(new Vector3(tx0 + 0.07f, F, tz0 + 0.06f), new Vector3(tx1 - 0.04f, F + 0.05f, tz1 - 0.06f), por);
            Col(new Vector3(tx0, F, tz0), new Vector3(tx1, th, tz1));
            // tile surround on the two walls up to 6'
            Solid(new Vector3(tx1 - 0.008f, th, tz0), new Vector3(tx1, F + 1.83f, tz1), _tile);
            Solid(new Vector3(tx0, th, tz0), new Vector3(tx1, F + 1.83f, tz0 + 0.008f), _tile);
            // toilet
            float toX = P + 1.0f, toZ = -0.6f - Ti / 2;
            Solid(new Vector3(toX - 0.19f, F, toZ - 0.2f), new Vector3(toX + 0.19f, F + 0.76f, toZ), por);     // tank
            Solid(new Vector3(toX - 0.18f, F, toZ - 0.7f), new Vector3(toX + 0.18f, F + 0.4f, toZ - 0.2f), por); // bowl
            Col(new Vector3(toX - 0.19f, F, toZ - 0.7f), new Vector3(toX + 0.19f, F + 0.76f, toZ));
        }

        static void Deck(float hd)
        {
            float x0 = -0.75f - 1.83f, len = 3.66f, x1 = x0 + len, top = 0.18f, pitch = Units.In(5.5f) + Units.In(0.125f);
            int pt = _k.M(SurfaceKind.TreatedLumber);
            _h.DeckBoardLength = len;
            int n = 21, fix = 10;
            float zEnd = -hd - n * pitch;
            for (int i = 0; i < n; i++)
            {
                if (i == fix) continue;
                float zc = -hd - pitch * (i + 0.5f);
                _k.B.AddBox(new Vector3((x0 + x1) / 2f, top - 0.0127f, zc), new Vector3(len, 0.0254f, Units.In(5.5f)), Quaternion.identity, pt, Vector3.right);
            }
            float jy1 = top - 0.0254f, jy0 = jy1 - Units.In(7.25f);
            for (float x = x0 + 0.02f; x <= x1 - 0.02f; x += Units.In(16))
                Solid(new Vector3(x, jy0, zEnd), new Vector3(x + Units.In(1.5f), jy1, -hd), pt, MeshBuilder.AllFaces, Vector3.forward);
            Solid(new Vector3(x0, jy0, zEnd - 0.038f), new Vector3(x1, jy1, zEnd), pt, MeshBuilder.AllFaces, Vector3.right);  // rim
            Solid(new Vector3(x0 - 0.038f, jy0, zEnd - 0.038f), new Vector3(x0, jy1, -hd), pt, MeshBuilder.AllFaces, Vector3.forward);
            Solid(new Vector3(x1, jy0, zEnd - 0.038f), new Vector3(x1 + 0.038f, jy1, -hd), pt, MeshBuilder.AllFaces, Vector3.forward);
            Col(new Vector3(x0 - 0.04f, 0, zEnd - 0.04f), new Vector3(x1 + 0.04f, top, -hd));
            _h.SetAnchor(HouseSpot.DeckBoard, new Vector3((x0 + x1) / 2f, top, -hd - pitch * (fix + 0.5f)), Quaternion.LookRotation(Vector3.right));
        }

        static float _fenceZ, _fixPicketX;
        static void Fence(TownLot lot)
        {
            float back = -(Town.LotDepth - TownHouse.Setback - D / 2f) + 0.1f;   // house-local z of the back lot line (+ 10 cm)
            _fenceZ = back;
            int cedar = _k.M(LandSurface.Cedar);
            float half = Town.LotFrontage / 2f - 0.1f, pw = Units.In(5.5f), pt = Units.In(0.625f), ph = Units.In(72);
            for (float x = -half; x <= half + 0.01f; x += 2.4384f)
            {
                float px = Mathf.Min(x, half - 0.09f);
                Solid(new Vector3(px, 0, back), new Vector3(px + 0.089f, ph - 0.05f, back + 0.089f), cedar, MeshBuilder.AllFaces, Vector3.up);
            }
            foreach (float ry in new[] { 0.3f, ph - 0.35f })
                Solid(new Vector3(-half, ry, back + 0.089f), new Vector3(half, ry + 0.089f, back + 0.127f), cedar, MeshBuilder.AllFaces, Vector3.right);
            Col(new Vector3(-half, 0, back), new Vector3(half, ph, back + 0.15f));
            var prof = new[] { new Vector2(-pw / 2, 0), new Vector2(pw / 2, 0), new Vector2(pw / 2, ph - 0.03f), new Vector2(pw / 2 - 0.03f, ph), new Vector2(-pw / 2 + 0.03f, ph), new Vector2(-pw / 2, ph - 0.03f) };
            float zp = back + 0.127f + pt / 2f;
            int count = Mathf.FloorToInt(2 * half / pw), fix = count / 2;
            for (int i = 0; i < count; i++)
            {
                float x = -half + pw * (i + 0.5f);
                if (i == fix) { _fixPicketX = x; continue; }
                _k.B.AddExtrude(prof, new Vector3(x, 0.03f, zp), Quaternion.identity, pt, cedar);
            }
            _h.SetAnchor(HouseSpot.FencePicket, new Vector3(_fixPicketX, 0.03f, zp), Quaternion.identity);
        }

        // ------------------------------------------------------------------ fixtures + anchors

        static GameObject FixtureObj(string name, HouseSpot spot, System.Action<LandModel> build, bool collider = true)
        {
            var k = new LandModel(); build(k);
            var go = k.Make(_go.transform, name, collider);
            var p = _h.Anchor(spot);
            go.transform.position = p.position; go.transform.rotation = p.rotation;
            _h.SetFixture(spot, go);
            return go;
        }

        static void Fixtures(float xs, float hd, Vector3 sidewalkWorld)
        {
            float hw = W / 2f, zb = -hd + T;
            var outward = Quaternion.identity;                      // +Z
            var inward = Quaternion.LookRotation(Vector3.back);
            var toPlusX = Quaternion.LookRotation(Vector3.right);
            var toMinusX = Quaternion.LookRotation(Vector3.left);

            _h.SetAnchor(HouseSpot.KitchenSink, new Vector3(xs, F, zb + 0.61f), outward);
            _h.SetAnchor(HouseSpot.BathVanity, new Vector3(P + Ti / 2 + 0.45f, F, zb + 0.53f), outward);
            _h.SetAnchor(HouseSpot.OutletLiving, new Vector3(-hw + T, F + Units.In(16), 1.8f), toPlusX);
            _h.SetAnchor(HouseSpot.OutletKitchen, new Vector3(xs + 1.0f, F + Units.In(44), zb), outward);
            _h.SetAnchor(HouseSpot.BreakerPanel, new Vector3(P - Ti / 2, F + Units.In(60), -0.9f), toMinusX);
            _h.SetAnchor(HouseSpot.DrywallLiving, new Vector3(-hw + T, F + Units.In(48), -0.5f), toPlusX);
            _h.SetAnchor(HouseSpot.DrywallBedroom, new Vector3(hw - T, F + Units.In(48), 1.5f), toMinusX);
            _h.SetAnchor(HouseSpot.InteriorDoor, new Vector3(P, F, 1.5f), toMinusX);
            _h.SetAnchor(HouseSpot.FrontDoor, new Vector3(TownHouse.FrontDoorX, F, hd - T / 2), outward);
            float livingWin = (-hw + T + TownHouse.FrontDoorX - 0.46f) / 2f;
            _h.WindowWidth = 1.2f;
            _h.SetAnchor(HouseSpot.WindowCaulk, new Vector3(livingWin, F + 0.9f, hd), outward);
            float tx1 = P + 2.4f - Ti / 2;
            _h.SetAnchor(HouseSpot.TubCaulk, new Vector3(tx1, F + 0.406f, zb + 0.762f), toMinusX);
            float eZ = hd + Over, eY = PT + Heel - Over * Pitch, ang = Mathf.Atan(Pitch);
            Vector3 up = new Vector3(0, Mathf.Sin(ang), -Mathf.Cos(ang)), n = new Vector3(0, Mathf.Cos(ang), Mathf.Sin(ang));
            float workX = hw - 2.6f;
            _h.SetAnchor(HouseSpot.RoofShingles, new Vector3(workX, eY, eZ) + up * 1.0f, Quaternion.LookRotation(up, n));
            _h.SetAnchor(HouseSpot.Gutter, new Vector3(workX, _h.EaveHeight, eZ + 0.09f), outward);
            _h.SetAnchor(HouseSpot.LadderFront, new Vector3(hw - 1.4f, 0f, eZ + 0.9f), outward);
            var swLocal = _go.transform.InverseTransformPoint(sidewalkWorld);
            _h.SetAnchor(HouseSpot.SidewalkSquare, swLocal, inward);

            // --- fixtures
            FixtureObj("Outlet", HouseSpot.OutletLiving, k => Receptacle(k, false));
            FixtureObj("OutletGFCI", HouseSpot.OutletKitchen, k => Receptacle(k, true));
            FixtureObj("Panel", HouseSpot.BreakerPanel, k =>
            {
                int g = k.M(SurfaceKind.PaintedSteel, new Color(0.75f, 0.76f, 0.76f));
                k.B.AddBox(new Vector3(0, 0, 0.05f), new Vector3(Units.In(14), Units.In(30), 0.1f), Quaternion.identity, g);
                k.B.AddBox(new Vector3(0, 0, 0.101f), new Vector3(Units.In(12), Units.In(26), 0.003f), Quaternion.identity, g);
                k.B.AddBox(new Vector3(Units.In(5), 0, 0.105f), new Vector3(0.02f, 0.05f, 0.008f), Quaternion.identity, k.M(SurfaceKind.DarkPlastic));
            });
            FixtureObj("Board", HouseSpot.DeckBoard, k =>
                k.B.AddBox(new Vector3(0, -0.0127f, 0), new Vector3(Units.In(5.5f), 0.0254f, _h.DeckBoardLength), Quaternion.identity, k.M(SurfaceKind.TreatedLumber), Vector3.forward));
            FixtureObj("Picket", HouseSpot.FencePicket, k =>
            {
                float pw = Units.In(5.5f), ph = Units.In(72);
                var prof = new[] { new Vector2(-pw / 2, 0), new Vector2(pw / 2, 0), new Vector2(pw / 2, ph - 0.03f), new Vector2(pw / 2 - 0.03f, ph), new Vector2(-pw / 2 + 0.03f, ph), new Vector2(-pw / 2, ph - 0.03f) };
                k.B.AddExtrude(prof, Vector3.zero, Quaternion.identity, Units.In(0.625f), k.M(LandSurface.Cedar));
            });
            FixtureObj("SidewalkSquare", HouseSpot.SidewalkSquare, k =>
                k.B.AddBox(new Vector3(0, -Town.CurbHeight / 2f, 0), new Vector3(1.518f, Town.CurbHeight, 1.518f), Quaternion.identity, k.M(SurfaceKind.BroomedConcrete)));

            Door(HouseSpot.FrontDoor, 0.914f, 0.044f, Materials.Get(SurfaceKind.PaintedSteel, new Color(0.45f, 0.12f, 0.10f)), false, -T / 2 + 0.03f);
            Door(HouseSpot.InteriorDoor, 0.813f, 0.035f, Materials.Get(SurfaceKind.PaintedWhite), true, 0f);
        }

        static void Receptacle(LandModel k, bool gfci)
        {
            int w = k.M(new Color(0.94f, 0.93f, 0.90f)), slot = k.M(new Color(0.15f, 0.15f, 0.15f));
            k.B.AddBox(new Vector3(0, 0, 0.003f), new Vector3(Units.In(2.75f), Units.In(4.5f), 0.006f), Quaternion.identity, w);
            if (gfci)
            {
                k.B.AddBox(new Vector3(0, 0, 0.009f), new Vector3(Units.In(1.3f), Units.In(2.6f), 0.006f), Quaternion.identity, w);
                k.B.AddBox(new Vector3(0, 0.004f, 0.0125f), new Vector3(0.012f, 0.006f, 0.002f), Quaternion.identity, k.M(new Color(0.75f, 0.1f, 0.1f)));
                k.B.AddBox(new Vector3(0, -0.006f, 0.0125f), new Vector3(0.012f, 0.006f, 0.002f), Quaternion.identity, slot);
            }
            else
                for (int s = -1; s <= 1; s += 2)
                {
                    k.B.AddBox(new Vector3(0, s * 0.019f, 0.009f), new Vector3(Units.In(1.35f), Units.In(1.2f), 0.006f), Quaternion.identity, w);
                    k.B.AddBox(new Vector3(-0.006f, s * 0.019f + 0.002f, 0.0121f), new Vector3(0.002f, 0.008f, 0.001f), Quaternion.identity, slot);
                    k.B.AddBox(new Vector3(0.006f, s * 0.019f + 0.002f, 0.0121f), new Vector3(0.002f, 0.01f, 0.001f), Quaternion.identity, slot);
                }
        }

        /// <summary>Hinged door at a door anchor (anchor +Z = the door's "front"). Pivot at the hinge jamb; slab along the pivot's +X.</summary>
        static void Door(HouseSpot spot, float width, float thick, Material mat, bool startOpen, float zOff)
        {
            var a = _h.Anchor(spot);
            var pivot = new GameObject(spot + "Hinge");
            pivot.transform.SetParent(_go.transform, false);
            pivot.transform.position = a.position + a.rotation * new Vector3(-width / 2f + 0.01f, 0f, zOff);
            pivot.transform.rotation = a.rotation;
            var k = new LandModel();
            int m = k.M(mat), knob = k.M(SurfaceKind.Nickel);
            k.B.AddBox(new Vector3(width / 2f - 0.01f, 1.016f, 0), new Vector3(width - 0.02f, 2.022f, thick), Quaternion.identity, m, Vector3.up);
            for (int s = -1; s <= 1; s += 2)
                k.B.AddCylinder(new Vector3(width - 0.08f, 0.91f, s * (thick / 2f + 0.03f)), Quaternion.Euler(90, 0, 0), 0.028f, 0.05f, 12, knob);
            var slab = k.Make(pivot.transform, "Slab", false);
            var bc = slab.AddComponent<BoxCollider>();
            bc.center = new Vector3(width / 2f - 0.01f, 1.016f, 0); bc.size = new Vector3(width - 0.02f, 2.022f, thick);
            var door = pivot.AddComponent<TownDoor>();
            // swing toward -Z of the anchor frame (into the house for the front door, into the bedroom for the bedroom door)
            door.SwingSign = 1f;
            door.SetOpenImmediate(startOpen || spot == HouseSpot.FrontDoor);
            _h.SetFixture(spot, pivot);
        }
    }
}
