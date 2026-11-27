// RuralIndustrialModels — procedural map models for farm and industrial buildings.
// Registered types:
//   warehouse      tilt-up concrete distribution building, 30' clear, dock doors at 48" dock height, glass office corner
//   metalshop      pre-engineered metal building, 1:12 gable facing the street, wainscot, overhead doors, sign
//   lumberyard     store/office building + open lumber sheds with bundles on racks + outdoor stacks + forklift
//   concreteplant  ready-mix batch plant: cement silos, aggregate bins, charging conveyor, aggregate piles, mixer trucks
//   selfstorage    rows of 10'-deep storage units with roll-up doors, office, perimeter fence and gate
//   contractoryard metal shop + fenced yard with an excavator, dump truck, pipe and material piles
//   watertower     single-pedestal spheroid water tower (~130') with the town name on the bowl
//   barn           gambrel bank barn (red boards, white trim, sliding doors, hay door, cupola)
//   silo           concrete stave silo with steel dome
//   grainbins      row of corrugated steel grain bins on concrete rings, cone roofs, auger
//   machineshed    open-front pole barn (post-frame) with a tractor inside
// Frame: origin at grade, middle of the front edge, +Z toward the street, footprint x ±Width/2, z -Depth..0.
using UnityEngine;

namespace Groundbreak
{
    public static class RuralIndustrialModels
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            MapModels.Register("warehouse", Warehouse);
            MapModels.Register("metalshop", MetalShop);
            MapModels.Register("lumberyard", LumberYard);
            MapModels.Register("concreteplant", ConcretePlant);
            MapModels.Register("selfstorage", SelfStorage);
            MapModels.Register("contractoryard", ContractorYard);
            MapModels.Register("watertower", WaterTower);
            MapModels.Register("barn", Barn);
            MapModels.Register("silo", Silo);
            MapModels.Register("grainbins", GrainBins);
            MapModels.Register("machineshed", MachineShed);
        }

        const float In = 0.0254f, Ft = 0.3048f;
        const int Walls = MeshBuilder.AllFaces & ~MeshBuilder.FacePosY & ~MeshBuilder.FaceNegY;
        const int NoBottom = MeshBuilder.AllFaces & ~MeshBuilder.FaceNegY;

        // ------------------------------------------------------------------ materials
        static int Flat(MapBuild b, Color c) => b.K.M(MapMaterials.Flat(c));
        static int Panel(MapBuild b, Color c) => b.K.M(MapMaterials.MetalPanel(c));
        static int Paint(MapBuild b, Color c) => b.K.M(SurfaceKind.PaintedSteel, c);
        static int Dark(MapBuild b) => Flat(b, new Color(0.07f, 0.075f, 0.08f));
        static int Conc(MapBuild b) => b.K.M(MapMaterials.Concrete);
        static int Galv(MapBuild b) => b.K.M(SurfaceKind.Galvanized);

        static string SignOf(MapBuildingSpec s, string fallback) =>
            string.IsNullOrWhiteSpace(s.Sign) ? fallback : s.Sign.Trim().ToUpperInvariant();

        // ------------------------------------------------------------------ primitives
        static void Bx(MapBuild b, float x0, float y0, float z0, float x1, float y1, float z1, int sub, int mask = MeshBuilder.AllFaces, Vector3 u = default)
        {
            float ax = Mathf.Min(x0, x1), bx = Mathf.Max(x0, x1), ay = Mathf.Min(y0, y1), by = Mathf.Max(y0, y1), az = Mathf.Min(z0, z1), bz = Mathf.Max(z0, z1);
            if (bx - ax < 1e-4f || by - ay < 1e-4f || bz - az < 1e-4f) return;
            b.B.AddBox(new Vector3((ax + bx) * 0.5f, (ay + by) * 0.5f, (az + bz) * 0.5f), new Vector3(bx - ax, by - ay, bz - az),
                Quaternion.identity, sub, u == default ? Vector3.right : u, mask);
        }

        static void Cyl(MapBuild b, float x, float y0, float z, float r, float h, int seg, int sub, bool capped = true) =>
            b.B.AddCylinder(new Vector3(x, y0 + h * 0.5f, z), r, h, seg, sub, capped);

        /// <summary>Square-section member between two points (posts, braces, conveyors).</summary>
        static void Member(MapBuild b, Vector3 a, Vector3 c, float w, float h, int sub)
        {
            Vector3 d = c - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector3 up = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.98f ? Vector3.forward : Vector3.up;
            b.B.AddBox((a + c) * 0.5f, new Vector3(w, h, d.magnitude), Quaternion.LookRotation(d, up), sub, Vector3.forward);
        }

        static void Rod(MapBuild b, Vector3 a, Vector3 c, float r, int seg, int sub)
        {
            Vector3 d = c - a;
            if (d.sqrMagnitude < 1e-6f) return;
            b.B.AddRod((a + c) * 0.5f, Quaternion.LookRotation(d), r, d.magnitude, seg, sub);
        }

        /// <summary>Text on a backing panel, facing +Z, bottom centre at (x, y, z) of the panel face.</summary>
        static void SignBoard(MapBuild b, string text, float x, float y, float z, float maxW, float h, int panel, int ink)
        {
            if (string.IsNullOrEmpty(text)) return;
            float px = Mathf.Min(h * 0.62f / 7f, maxW * 0.9f / Mathf.Max(1f, text.Length * 6f - 1f));
            float tw = MapText.Width(text, px);
            if (panel >= 0) Bx(b, x - tw * 0.5f - h * 0.25f, y, z - 0.08f, x + tw * 0.5f + h * 0.25f, y + h, z, panel);
            MapText.Draw(b, text, new Vector3(x, y + (h - 7f * px) * 0.5f, z), px, ink);
        }

        /// <summary>Building with walls x ±hw, z z0..z1 (z0 &lt; z1), eave height, gable roof with the ridge along Z.</summary>
        static void GableBuilding(MapBuild b, float hw, float z0, float z1, float eave, float rise, float oh, float ohEnd, int wall, int roof, int trim)
        {
            Bx(b, -hw, 0f, z0, hw, eave, z1, wall, Walls, Vector3.right);
            float ry = eave + rise;
            foreach (float z in new[] { z0, z1 })
                b.B.AddTriangle(new Vector3(-hw, eave, z), new Vector3(hw, eave, z), new Vector3(0f, ry, z), wall, new Vector3(0f, 0f, z == z1 ? 1f : -1f));
            float slope = rise / Mathf.Max(0.05f, hw), ex = hw + oh, ey = eave - oh * slope;
            Vector3 r0 = new Vector3(0f, ry, z0 - ohEnd), r1 = new Vector3(0f, ry, z1 + ohEnd);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 e0 = new Vector3(s * ex, ey, z0 - ohEnd), e1 = new Vector3(s * ex, ey, z1 + ohEnd);
                Vector3 n = new Vector3(s * rise, hw, 0f).normalized;
                Vector3 lift = n * 0.08f;
                b.B.AddQuad(e0 + lift, e1 + lift, r1 + lift, r0 + lift, roof, n, Vector3.forward);
                b.B.AddQuad(e0, e1, r1, r0, roof, -n, Vector3.forward);
                // rake trim + eave gutter
                Member(b, e1 + lift * 0.5f, r1 + lift * 0.5f, 0.06f, 0.2f, trim);
                Member(b, e0 + lift * 0.5f, r0 + lift * 0.5f, 0.06f, 0.2f, trim);
                Bx(b, s * ex - 0.07f, ey - 0.14f, z0 - ohEnd, s * ex + 0.07f, ey + 0.02f, z1 + ohEnd, trim);
            }
            b.Solid(new Vector3(-hw, 0f, z0), new Vector3(hw, eave + rise * 0.5f, z1));
        }

        static void OverheadDoor(MapBuild b, float x, float z, float w, float h, int door, int frame)
        {
            Bx(b, x - w * 0.5f - 0.1f, 0f, z, x + w * 0.5f + 0.1f, h + 0.1f, z + 0.04f, frame);
            Bx(b, x - w * 0.5f, 0f, z + 0.04f, x + w * 0.5f, h, z + 0.07f, door);
            int seams = Mathf.Max(3, Mathf.RoundToInt(h / 0.53f));
            for (int i = 1; i < seams; i++) Bx(b, x - w * 0.5f, h * i / seams - 0.012f, z + 0.07f, x + w * 0.5f, h * i / seams + 0.012f, z + 0.08f, frame);
        }

        static void ManDoor(MapBuild b, float x, float z, int door, int frame)
        {
            Bx(b, x - 0.51f, 0f, z, x + 0.51f, 2.18f, z + 0.03f, frame);
            Bx(b, x - 0.46f, 0f, z + 0.03f, x + 0.46f, 2.13f, z + 0.06f, door);
            Bx(b, x + 0.3f, 0.98f, z + 0.06f, x + 0.38f, 1.02f, z + 0.11f, frame);
        }

        static void Window(MapBuild b, float x, float y, float z, float w, float h, int glass, int frame, int back)
        {
            Bx(b, x - w * 0.5f - 0.05f, y - 0.05f, z, x + w * 0.5f + 0.05f, y + h + 0.05f, z + 0.03f, frame);
            Bx(b, x - w * 0.5f, y, z + 0.01f, x + w * 0.5f, y + h, z + 0.035f, back);
            Bx(b, x - w * 0.5f, y, z + 0.035f, x + w * 0.5f, y + h, z + 0.045f, glass);
        }

        static void Rtu(MapBuild b, float x, float y, float z, int unit, int dark)
        {
            Bx(b, x - 1.1f, y, z - 0.7f, x + 1.1f, y + 1.1f, z + 0.7f, unit, NoBottom);
            Cyl(b, x + 0.45f, y + 1.1f, z, 0.32f, 0.05f, 12, dark);
        }

        // ------------------------------------------------------------------ vehicles & equipment
        static void Wheel(MapBuild b, Vector3 c, float r, float w, int tire, int rim)
        {
            b.B.AddCylinder(c, Quaternion.Euler(0f, 0f, 90f), r, w, 14, tire);
            b.B.AddCylinder(c, Quaternion.Euler(0f, 0f, 90f), r * 0.55f, w + 0.02f, 10, rim);
        }

        /// <summary>Ready-mix truck (~10.5 m), length along local Z, cab toward +Z.</summary>
        static void MixerTruck(MapBuild b, Vector3 p, float yaw)
        {
            b.BeginLocal(p, Quaternion.Euler(0f, yaw, 0f));
            int body = Paint(b, new Color(0.95f, 0.95f, 0.93f)), drum = Paint(b, new Color(0.92f, 0.55f, 0.12f));
            int dark = Dark(b), tire = b.K.M(SurfaceKind.Tire), rim = Galv(b), glass = b.K.M(MapMaterials.Glass);
            Bx(b, -0.45f, 0.75f, -5.2f, 0.45f, 1.05f, 4.6f, dark);                     // frame rails
            Bx(b, -1.25f, 0.95f, 2.9f, 1.25f, 3.1f, 5.0f, body);                       // cab
            Bx(b, -1.15f, 2.05f, 4.98f, 1.15f, 2.95f, 5.03f, glass);                   // windshield
            Bx(b, -1.2f, 0.7f, 5.0f, 1.2f, 1.2f, 5.25f, dark);                          // bumper
            b.B.AddFrustum(new Vector3(0f, 2.55f, -1.6f), Quaternion.Euler(-78f, 0f, 0f), 1.2f, 0.7f, 5.6f, 16, drum);
            b.B.AddFrustum(new Vector3(0f, 2.15f, 1.6f), Quaternion.Euler(102f, 0f, 0f), 1.2f, 0.95f, 1.0f, 16, drum);
            Member(b, new Vector3(0f, 3.2f, -4.4f), new Vector3(0f, 2.0f, -5.9f), 0.5f, 0.12f, dark);   // chute
            foreach (float z in new[] { 4.0f, -1.6f, -3.0f, -4.3f })
                foreach (int s in new[] { -1, 1 })
                    Wheel(b, new Vector3(s * 1.05f, 0.52f, z), 0.52f, 0.3f, tire, rim);
            b.EndLocal();
            Solid(b, p, yaw, new Vector3(-1.3f, 0f, -5.9f), new Vector3(1.3f, 3.9f, 5.25f));
        }

        /// <summary>Tandem dump truck, length along local Z, cab toward +Z.</summary>
        static void DumpTruck(MapBuild b, Vector3 p, float yaw, Color cabColor)
        {
            b.BeginLocal(p, Quaternion.Euler(0f, yaw, 0f));
            int cab = Paint(b, cabColor), bed = Paint(b, new Color(0.35f, 0.36f, 0.38f)), dark = Dark(b);
            int tire = b.K.M(SurfaceKind.Tire), rim = Galv(b), glass = b.K.M(MapMaterials.Glass);
            Bx(b, -0.45f, 0.75f, -4.3f, 0.45f, 1.05f, 4.2f, dark);
            Bx(b, -1.25f, 0.95f, 2.4f, 1.25f, 3.05f, 4.5f, cab);
            Bx(b, -1.15f, 2.05f, 4.48f, 1.15f, 2.9f, 4.53f, glass);
            Bx(b, -1.2f, 0.7f, 4.5f, 1.2f, 1.2f, 4.75f, dark);
            Bx(b, -1.25f, 1.25f, -4.4f, 1.25f, 1.4f, 2.1f, bed);                         // bed floor
            Bx(b, -1.25f, 1.4f, -4.4f, -1.17f, 2.6f, 2.1f, bed);
            Bx(b, 1.17f, 1.4f, -4.4f, 1.25f, 2.6f, 2.1f, bed);
            Bx(b, -1.25f, 1.4f, 1.95f, 1.25f, 3.1f, 2.1f, bed);                          // headboard
            Bx(b, -1.25f, 1.4f, -4.4f, 1.25f, 2.6f, -4.3f, bed);                         // tailgate
            foreach (float z in new[] { 3.4f, -2.2f, -3.5f })
                foreach (int s in new[] { -1, 1 })
                    Wheel(b, new Vector3(s * 1.05f, 0.52f, z), 0.52f, 0.3f, tire, rim);
            b.EndLocal();
            Solid(b, p, yaw, new Vector3(-1.3f, 0f, -4.4f), new Vector3(1.3f, 3.1f, 4.75f));
        }

        /// <summary>~20 t tracked excavator, tracks along local Z, boom toward +Z resting the bucket on the ground.</summary>
        static void Excavator(MapBuild b, Vector3 p, float yaw)
        {
            b.BeginLocal(p, Quaternion.Euler(0f, yaw, 0f));
            int yel = Paint(b, new Color(0.95f, 0.72f, 0.08f)), dark = Dark(b), steel = Paint(b, new Color(0.25f, 0.25f, 0.26f));
            int glass = b.K.M(MapMaterials.Glass);
            foreach (int s in new[] { -1, 1 })
            {
                Bx(b, s * 1.4f - 0.3f, 0f, -2.3f, s * 1.4f + 0.3f, 0.9f, 2.3f, dark);
                Bx(b, s * 1.4f - 0.32f, 0.2f, -2.55f, s * 1.4f + 0.32f, 0.7f, 2.55f, dark);
            }
            Bx(b, -1.1f, 0.6f, -1.2f, 1.1f, 1.1f, 1.2f, steel);
            Bx(b, -1.45f, 1.1f, -2.6f, 1.45f, 2.2f, 1.3f, yel);                          // upper house + counterweight
            Bx(b, -1.45f, 2.2f, 0.1f, -0.45f, 3.1f, 1.3f, yel);                          // cab
            Bx(b, -1.4f, 2.3f, 1.3f, -0.5f, 3.0f, 1.33f, glass);
            Bx(b, -1.48f, 2.3f, 0.2f, -1.45f, 3.0f, 1.2f, glass);
            Vector3 heel = new Vector3(0.3f, 2.0f, 1.2f), knee = new Vector3(0.3f, 4.6f, 4.6f), tip = new Vector3(0.3f, 1.0f, 6.6f);
            Member(b, heel, knee, 0.55f, 0.75f, yel);
            Member(b, knee, tip, 0.45f, 0.55f, yel);
            Bx(b, -0.25f, 0f, 6.3f, 0.85f, 1.0f, 7.3f, steel);                            // bucket
            Rod(b, new Vector3(0.3f, 1.6f, 1.6f), new Vector3(0.3f, 3.6f, 3.6f), 0.09f, 8, b.K.M(SurfaceKind.BrushedSteel));
            b.EndLocal();
            Solid(b, p, yaw, new Vector3(-1.75f, 0f, -2.6f), new Vector3(1.75f, 3.1f, 2.6f));
        }

        static void Forklift(MapBuild b, Vector3 p, float yaw)
        {
            b.BeginLocal(p, Quaternion.Euler(0f, yaw, 0f));
            int yel = Paint(b, new Color(0.95f, 0.70f, 0.08f)), dark = Dark(b), tire = b.K.M(SurfaceKind.Tire);
            Bx(b, -0.55f, 0.25f, -1.3f, 0.55f, 1.1f, 0.6f, yel);
            Bx(b, -0.55f, 0.25f, -1.4f, 0.55f, 1.2f, -1.0f, dark);                       // counterweight
            foreach (int s in new[] { -1, 1 }) { Member(b, new Vector3(s * 0.5f, 1.1f, -1.0f), new Vector3(s * 0.5f, 2.15f, -0.9f), 0.06f, 0.06f, dark); Member(b, new Vector3(s * 0.5f, 1.1f, 0.4f), new Vector3(s * 0.5f, 2.15f, 0.3f), 0.06f, 0.06f, dark); }
            Bx(b, -0.55f, 2.1f, -1.0f, 0.55f, 2.16f, 0.4f, dark);
            Bx(b, -0.45f, 0f, 0.65f, 0.45f, 2.3f, 0.75f, dark);                           // mast
            foreach (int s in new[] { -1, 1 }) Bx(b, s * 0.3f - 0.05f, 0.05f, 0.75f, s * 0.3f + 0.05f, 0.1f, 1.95f, dark);
            foreach (float z in new[] { 0.3f, -0.9f })
                foreach (int s in new[] { -1, 1 })
                    b.B.AddCylinder(new Vector3(s * 0.5f, 0.3f, z), Quaternion.Euler(0f, 0f, 90f), 0.3f, 0.2f, 10, tire);
            b.EndLocal();
        }

        /// <summary>Banded lumber bundle (unit of 2x stock), centre-bottom at p, length along local axis.</summary>
        static void Bundle(MapBuild b, Vector3 p, bool alongX, float len, int wood, int band)
        {
            float hl = len * 0.5f, hw = 0.56f, h = 0.75f;
            if (alongX)
            {
                Bx(b, p.x - hl, p.y + 0.1f, p.z - hw, p.x + hl, p.y + 0.1f + h, p.z + hw, wood, MeshBuilder.AllFaces, Vector3.right);
                foreach (float t in new[] { -0.6f, 0f, 0.6f }) Bx(b, p.x + t * hl - 0.02f, p.y + 0.08f, p.z - hw - 0.01f, p.x + t * hl + 0.02f, p.y + 0.12f + h, p.z + hw + 0.01f, band);
                foreach (float t in new[] { -0.7f, 0.7f }) Bx(b, p.x + t * hl - 0.05f, p.y, p.z - hw, p.x + t * hl + 0.05f, p.y + 0.1f, p.z + hw, wood);
            }
            else
            {
                Bx(b, p.x - hw, p.y + 0.1f, p.z - hl, p.x + hw, p.y + 0.1f + h, p.z + hl, wood, MeshBuilder.AllFaces, Vector3.forward);
                foreach (float t in new[] { -0.6f, 0f, 0.6f }) Bx(b, p.x - hw - 0.01f, p.y + 0.08f, p.z + t * hl - 0.02f, p.x + hw + 0.01f, p.y + 0.12f + h, p.z + t * hl + 0.02f, band);
                foreach (float t in new[] { -0.7f, 0.7f }) Bx(b, p.x - hw, p.y, p.z + t * hl - 0.05f, p.x + hw, p.y + 0.1f, p.z + t * hl + 0.05f, wood);
            }
        }

        /// <summary>Conical stockpile (angle of repose ~34°).</summary>
        static void Pile(MapBuild b, Vector3 p, float r, int sub)
        {
            float h = r * 0.67f;
            b.B.AddLathe(p, Quaternion.identity, new[] { new Vector2(r, 0f), new Vector2(r * 0.55f, h * 0.62f), new Vector2(r * 0.12f, h * 0.97f), new Vector2(0f, h) }, 16, sub, 60f);
        }

        /// <summary>Adds a local-frame box (rotated by yaw at p) as an axis-aligned building-frame solid.</summary>
        static void Solid(MapBuild b, Vector3 p, float yaw, Vector3 mn, Vector3 mx)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var c = p + q * new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
                lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c);
            }
            b.Solid(lo, hi);
        }

        /// <summary>Runs draw with the building frame moved to p/yaw (BeginLocal/EndLocal inside still work); solids are mapped back.</summary>
        static void At(MapBuild b, Vector3 p, float yaw, System.Action draw)
        {
            var saved = b.Frame;
            int solids = b.Solids.Count;
            b.Frame = saved * Matrix4x4.TRS(p, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            b.B.Transform = b.Frame;
            try { draw(); }
            finally
            {
                b.Frame = saved;
                b.B.Transform = saved;
                for (int i = b.Solids.Count - 1; i >= solids; i--)
                {
                    var s = b.Solids[i];
                    b.Solids.RemoveAt(i);
                    Solid(b, p, yaw, s.min, s.max);
                }
            }
        }

        /// <summary>Draws another registered model (e.g. "tractor") at p/yaw inside this model's frame.</summary>
        static void Sub(MapBuild b, string type, Vector3 p, float yaw, Color color, int seed)
        {
            if (!MapModels.Has(type)) return;
            At(b, p, yaw, () => MapModels.Draw(b, new MapBuildingSpec { Type = type, Width = 2f, Depth = 4f, Color = color, Seed = seed }));
        }

        // =====================================================================================================
        // warehouse
        // =====================================================================================================
        static void Warehouse(MapBuild b, MapBuildingSpec s)
        {
            float W = s.Width, D = s.Depth, hw = W * 0.5f, H = 9.1f;                     // 30' clear + parapet
            var tint = Color.Lerp(s.Color, new Color(0.86f, 0.84f, 0.79f), 0.7f);
            int wall = b.K.M(SurfaceKind.Concrete, tint * 1.08f), band = Paint(b, s.Accent == Color.white ? new Color(0.18f, 0.32f, 0.52f) : s.Accent);
            int dark = Dark(b), door = Paint(b, new Color(0.86f, 0.86f, 0.84f)), seal = Flat(b, new Color(0.05f, 0.05f, 0.05f));
            int glass = b.K.M(MapMaterials.Glass), alum = b.K.M(SurfaceKind.BrushedSteel), roof = Flat(b, new Color(0.82f, 0.82f, 0.80f));
            int unit = Paint(b, new Color(0.78f, 0.78f, 0.74f)), conc = Conc(b);
            Bx(b, -hw, 0f, -D, hw, H, 0f, wall, Walls);
            Bx(b, -hw, H - 0.01f, -D, hw, H, 0f, roof, MeshBuilder.FacePosY);
            // parapet cap + painted accent band
            Bx(b, -hw - 0.03f, H - 0.08f, -D - 0.03f, hw + 0.03f, H + 0.02f, 0.03f, alum, NoBottom);
            Bx(b, -hw - 0.01f, H - 1.4f, 0f, hw + 0.01f, H - 1.0f, 0.02f, band);
            // tilt-up panel joints (8 m panels)
            for (float x = -hw + 7.6f; x < hw - 1f; x += 7.6f) Bx(b, x - 0.012f, 0f, 0f, x + 0.012f, H, 0.015f, dark);
            for (float z = -7.6f; z > -D + 1f; z -= 7.6f) { Bx(b, hw, 0f, z - 0.012f, hw + 0.015f, H, z + 0.012f, dark); Bx(b, -hw - 0.015f, 0f, z - 0.012f, -hw, H, z + 0.012f, dark); }
            // office corner: storefront glass + canopy (right ~third)
            float ox0 = hw - Mathf.Min(14f, W * 0.3f), ox1 = hw - 1.2f;
            Bx(b, ox0, 0.3f, 0f, ox1, 3.4f, 0.04f, alum);
            Bx(b, ox0 + 0.08f, 0.38f, 0.04f, ox1 - 0.08f, 3.32f, 0.06f, dark);
            Bx(b, ox0 + 0.08f, 0.38f, 0.06f, ox1 - 0.08f, 3.32f, 0.07f, glass);
            for (float x = ox0 + 1.52f; x < ox1 - 0.3f; x += 1.52f) Bx(b, x - 0.03f, 0.3f, 0.06f, x + 0.03f, 3.4f, 0.09f, alum);
            Bx(b, ox0 + 0.08f, 4.3f, 0.04f, ox1 - 0.08f, 6.2f, 0.07f, glass);        // clerestory
            Bx(b, ox0 - 0.5f, 3.6f, 0f, ox1 + 0.5f, 3.85f, 1.8f, band);                 // canopy
            // dock doors on the left part: 9'x10' doors at 48" dock height, seals, bumpers, levelers
            float dx0 = -hw + 3f, dx1 = ox0 - 3f;
            int docks = Mathf.Clamp(Mathf.FloorToInt((dx1 - dx0) / 4.27f), 1, 8);
            float pitch = (dx1 - dx0) / docks;
            for (int i = 0; i < docks; i++)
            {
                float x = dx0 + pitch * (i + 0.5f);
                OverheadDoor(b, x, 0f, 2.74f, 3.05f, door, alum);
                // overhead door drawn from grade — mask the lower 48" with the wall's dock face
                Bx(b, x - 1.5f, 0f, 0f, x + 1.5f, 1.22f, 0.1f, wall);
                Bx(b, x - 1.75f, 1.22f, 0f, x - 1.45f, 4.6f, 0.35f, seal);
                Bx(b, x + 1.45f, 1.22f, 0f, x + 1.75f, 4.6f, 0.35f, seal);
                Bx(b, x - 1.75f, 4.3f, 0f, x + 1.75f, 4.75f, 0.35f, seal);
                foreach (int sx in new[] { -1, 1 }) Bx(b, x + sx * 1.05f - 0.13f, 0.85f, 0.1f, x + sx * 1.05f + 0.13f, 1.2f, 0.22f, seal);
                Bx(b, x - 0.12f, 4.9f, 0f, x + 0.12f, 5.05f, 0.25f, b.K.M(MapMaterials.Emissive(new Color(1f, 0.92f, 0.75f))));
            }
            // dock apron (concrete) in front of the docks
            Bx(b, -hw, 0f, 0f, dx1 + 1.5f, 0.03f, 4.5f, conc, MeshBuilder.FacePosY | MeshBuilder.FacePosZ);
            ManDoor(b, dx1 + 1.6f, 0f, door, alum);
            // side man doors, RTUs, sign
            ManDoor(b, 0f, -D, door, alum);
            for (int i = 0; i < Mathf.Max(2, Mathf.RoundToInt(W / 16f)); i++)
                Rtu(b, -hw + 6f + i * 14f, H, -D * 0.35f - (i % 2) * 8f, unit, dark);
            SignBoard(b, SignOf(s, "WAREHOUSE"), (ox0 + ox1) * 0.5f, H - 2.9f, 0.02f, ox1 - ox0, 1.6f, -1, Flat(b, new Color(0.12f, 0.14f, 0.18f)));
            b.Solid(new Vector3(-hw, 0f, -D), new Vector3(hw, H, 0f));
        }

        // =====================================================================================================
        // metal shop (PEMB)
        // =====================================================================================================
        static void MetalShop(MapBuild b, MapBuildingSpec s) => Shop(b, s, s.Width, s.Depth, true);

        static void MetalShopAt(MapBuild b, MapBuildingSpec s, float cx, float W, float D, bool sign) =>
            At(b, new Vector3(cx, 0f, 0f), 0f, () => Shop(b, s, W, D, sign));

        static void Shop(MapBuild b, MapBuildingSpec s, float W, float D, bool sign)
        {
            float hw = W * 0.5f, eave = W > 22f ? 5.5f : 4.9f, rise = hw / 12f;
            int wall = Panel(b, s.Color), wains = Panel(b, s.Color * 0.55f + new Color(0.05f, 0.05f, 0.05f, 0.45f));
            int roof = Panel(b, new Color(0.62f, 0.63f, 0.62f)), trim = Paint(b, new Color(0.15f, 0.17f, 0.2f));
            int door = Paint(b, new Color(0.93f, 0.93f, 0.9f)), glass = b.K.M(MapMaterials.Glass), dark = Dark(b);
            GableBuilding(b, hw, -D, 0f, eave, rise, 0.3f, 0.3f, wall, roof, trim);
            Bx(b, -hw - 0.02f, 0f, -D - 0.02f, hw + 0.02f, 1.07f, 0.02f, wains, Walls);       // 42" wainscot
            Bx(b, -hw - 0.05f, 0f, -D - 0.05f, hw + 0.05f, 0.18f, 0.05f, Conc(b), Walls);  // slab edge
            foreach (int sx in new[] { -1, 1 }) foreach (float z in new[] { 0f, -D })
                Bx(b, sx * hw - 0.07f, 0f, z - 0.07f, sx * hw + 0.07f, eave, z + 0.07f, trim);
            // front: two 12'x14' overhead doors + man door + office window
            int doors = W > 20f ? 2 : 1;
            float dw = 3.66f, dh = Mathf.Min(4.27f, eave - 0.5f);
            for (int i = 0; i < doors; i++)
            {
                float x = doors == 1 ? -hw * 0.3f : -hw + 1.5f + dw * 0.5f + i * (dw + 1.5f);
                OverheadDoor(b, x, 0.02f, dw, dh, door, trim);
            }
            float mx = hw - 2.2f;
            ManDoor(b, mx, 0.02f, door, trim);
            Window(b, mx - 2.2f, 1.1f, 0.02f, 1.8f, 1.2f, glass, trim, dark);
            Bx(b, mx - 0.8f, 2.4f, 0.02f, mx + 0.8f, 2.5f, 1.0f, trim);                      // door hood
            // side walls: windows + man door
            for (float z = -3f; z > -D + 2f; z -= 6f)
                foreach (int sx in new[] { -1, 1 })
                {
                    b.BeginLocal(new Vector3(sx * hw, 0f, z), Quaternion.Euler(0f, sx * 90f, 0f));
                    Window(b, 0f, 1.4f, 0.02f, 1.2f, 0.9f, glass, trim, dark);
                    b.EndLocal();
                }
            if (sign && !string.IsNullOrWhiteSpace(s.Sign))
                SignBoard(b, SignOf(s, ""), 0f, eave - 0.2f, 0.06f, W * 0.62f, 1.2f, Paint(b, new Color(0.96f, 0.96f, 0.94f)), Flat(b, new Color(0.62f, 0.12f, 0.08f)));
            // gutter downspouts
            foreach (int sx in new[] { -1, 1 }) Bx(b, sx * (hw + 0.2f) - 0.05f, 0f, -0.15f, sx * (hw + 0.2f) + 0.05f, eave - 0.1f, -0.05f, trim);
        }

        // =====================================================================================================
        // lumber yard
        // =====================================================================================================
        static void LumberYard(MapBuild b, MapBuildingSpec s)
        {
            float W = s.Width, D = s.Depth, hw = W * 0.5f;
            int wood = b.K.M(SurfaceKind.PineLumber), treated = b.K.M(SurfaceKind.TreatedLumber), band = b.K.M(SurfaceKind.BrushedSteel);
            int post = Paint(b, new Color(0.2f, 0.36f, 0.22f)), roof = Panel(b, new Color(0.25f, 0.42f, 0.28f)), dark = Dark(b);
            int conc = Conc(b), block = b.K.M(SurfaceKind.ConcreteBlock), osb = b.K.M(SurfaceKind.Osb), ply = b.K.M(SurfaceKind.Plywood);
            // store / contractor desk building at the front-left, gable to the street
            var sz = MapBodies.LumberStore(s); float sw = sz.x, sd = sz.y, scx = -hw + sw * 0.5f + 1f;
            var store = new MapBuildingSpec { Type = "metalshop", Width = sw, Depth = sd, Color = new Color(0.86f, 0.84f, 0.76f), Sign = "", Seed = s.Seed };
            MetalShopAt(b, store, scx, sw, sd, false);
            // storefront glass on the store (replaces part of the wall look)
            int glass = b.K.M(MapMaterials.Glass), alum = b.K.M(SurfaceKind.BrushedSteel);
            Bx(b, scx + 0.8f, 0.3f, 0.06f, scx + 4.4f, 2.6f, 0.1f, glass);
            Bx(b, scx + 0.6f, 2.6f, 0.03f, scx + 4.6f, 2.75f, 0.12f, alum);
            // big sign on a post frame by the street
            float sx0 = scx + sw * 0.5f + 4f;
            foreach (int k in new[] { -1, 1 }) Bx(b, sx0 + k * 3.2f - 0.12f, 0f, 2.6f, sx0 + k * 3.2f + 0.12f, 5.2f, 2.84f, post);
            SignBoard(b, SignOf(s, "LUMBER"), sx0, 3.2f, 2.9f, 7.2f, 1.8f, Paint(b, new Color(0.18f, 0.36f, 0.2f)), Flat(b, new Color(0.98f, 0.96f, 0.88f)));
            // open-front lumber sheds along the back and right side (mono-slope, open toward the yard)
            void Shed(float x0, float x1, float zb, float depth)
            {
                float hBack = 6.1f, hFront = 5.0f, zf = zb + depth;
                if (x1 - x0 < 0.5f) return;   // a lot this narrow made the post step 0 or negative: endless loop
                for (float x = x0; x <= x1 + 0.01f; x += (x1 - x0) / Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / 6f)))
                {
                    Bx(b, x - 0.1f, 0f, zb, x + 0.1f, hBack, zb + 0.2f, post);
                    Bx(b, x - 0.1f, 0f, zf - 0.2f, x + 0.1f, hFront, zf, post);
                }
                Bx(b, x0 - 0.1f, 0f, zb - 0.05f, x1 + 0.1f, hBack, zb, Panel(b, new Color(0.25f, 0.42f, 0.28f)), MeshBuilder.AllFaces);
                Vector3 a = new Vector3(x0 - 0.4f, hBack + 0.1f, zb - 0.3f), c = new Vector3(x1 + 0.4f, hBack + 0.1f, zb - 0.3f);
                Vector3 d = new Vector3(x0 - 0.4f, hFront + 0.1f, zf + 0.6f), e = new Vector3(x1 + 0.4f, hFront + 0.1f, zf + 0.6f);
                b.B.AddQuad(a, c, e, d, roof, Vector3.up, Vector3.right);
                b.B.AddQuad(a, c, e, d, roof, Vector3.down, Vector3.right);
                // cantilever racks with bundles, 3 levels
                for (float x = x0 + 1.5f; x + 4.9f < x1; x += 5.2f)   // the whole 16' bundle (x-0.04 .. x+4.84) stays under the roof
                    for (int lvl = 0; lvl < 3; lvl++)
                    {
                        if (b.R() < 0.15f) continue;
                        Bundle(b, new Vector3(x + 2.4f, lvl * 1.3f, zb + depth * 0.5f), true, 4.88f, b.R() < 0.3f ? treated : wood, band);
                    }
                b.Solid(new Vector3(x0, 0f, zb - 0.05f), new Vector3(x1, hBack, zb + 0.3f));
            }
            Shed(-hw + 2f, hw - 2f, -D + 0.5f, 11f);
            // outdoor bundle stacks + block/OSB pallets in the yard
            float yx0 = scx + sw * 0.5f + 3f, yx1 = hw - 3f;
            for (float x = yx0; x < yx1 - 1.5f; x += 3.2f)
                for (float z = -8f; z > -D + 15f; z -= 7f)
                {
                    int n = 1 + b.Rng.Next(3);
                    for (int i = 0; i < n; i++) Bundle(b, new Vector3(x + 0.6f, i * 0.88f, z - 2.5f), false, 4.88f, b.R() < 0.35f ? treated : wood, band);
                }
            for (int i = 0; i < 6; i++)
            {
                float x = -hw + 3f + i * 1.4f, z = -sd - 4f;
                Bx(b, x, 0f, z - 0.6f, x + 1.2f, 0.12f, z + 0.6f, wood);
                Bx(b, x + 0.05f, 0.12f, z - 0.55f, x + 1.15f, 1.1f, z + 0.55f, i % 3 == 0 ? osb : i % 3 == 1 ? ply : block);
            }
            Forklift(b, new Vector3(yx0 + 2f, 0f, -D + 14.5f), 200f);
            Forklift(b, new Vector3(hw - 6f, 0f, -12f), 95f);
        }

        // =====================================================================================================
        // ready-mix concrete batch plant
        // =====================================================================================================
        static void ConcretePlant(MapBuild b, MapBuildingSpec s)
        {
            float W = s.Width, D = s.Depth, hw = W * 0.5f;
            int steel = Paint(b, new Color(0.82f, 0.82f, 0.78f)), blue = Paint(b, new Color(0.2f, 0.36f, 0.58f)), dark = Dark(b);
            int galv = Galv(b), conc = Conc(b), block = b.K.M(SurfaceKind.Concrete, new Color(0.9f, 0.88f, 0.84f));
            int gravel = b.K.M(MapMaterials.Gravel), sand = b.K.M(SurfaceKind.Dirt, new Color(1.25f, 1.1f, 0.85f)), stone = b.K.M(SurfaceKind.Gravel, new Color(0.8f, 0.8f, 0.82f));
            float px = -6f, pz = -D * 0.45f;                                               // plant centre (truck bay runs along z)
            // truck bay slab
            Bx(b, px - 3f, 0f, pz - 9f, px + 3f, 0.05f, pz + 9f, conc, MeshBuilder.FacePosY);
            // 4 legs + charging platform + aggregate bins (4 compartments) above the bay
            float binY = 7.5f;
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                Bx(b, px + sx * 2.6f - 0.2f, 0f, pz + sz * 3.6f - 0.2f, px + sx * 2.6f + 0.2f, binY, pz + sz * 3.6f + 0.2f, blue);
            Bx(b, px - 2.8f, binY - 0.3f, pz - 3.8f, px + 2.8f, binY, pz + 3.8f, blue);
            Bx(b, px - 3f, binY, pz - 4f, px + 3f, binY + 4.2f, pz + 4f, steel, NoBottom);
            for (int i = 1; i < 4; i++) Bx(b, px - 3.02f, binY, pz - 4f + i * 2f - 0.04f, px + 3.02f, binY + 4.2f, pz - 4f + i * 2f + 0.04f, blue);
            b.B.AddFrustum(new Vector3(px, binY - 1.4f, pz), Quaternion.identity, 0.4f, 2.4f, 2.2f, 4, steel, false);   // weigh hopper
            Bx(b, px - 0.6f, 4.1f, pz - 0.6f, px + 0.6f, binY - 2.5f, pz + 0.6f, blue);                               // charge chute
            b.B.AddFrustum(new Vector3(px, 3.7f, pz), Quaternion.identity, 0.25f, 0.6f, 0.8f, 10, dark);
            SignBoard(b, SignOf(s, "READY MIX"), px, binY + 1.4f, pz + 4.02f, 5.6f, 1.4f, -1, b.K.M(SurfaceKind.PaintedSteel, new Color(0.2f, 0.36f, 0.58f)));
            // two cement silos (12' dia, ~70' tall) on leg frames beside the bay
            for (int i = 0; i < 2; i++)
            {
                float sx = px + 7.5f, sz = pz + (i == 0 ? -2.4f : 2.4f), r = 1.83f;
                foreach (int a in new[] { -1, 1 }) foreach (int c in new[] { -1, 1 })
                    Bx(b, sx + a * 1.3f - 0.12f, 0f, sz + c * 1.3f - 0.12f, sx + a * 1.3f + 0.12f, 5f, sz + c * 1.3f + 0.12f, blue);
                b.B.AddFrustum(new Vector3(sx, 4.4f, sz), Quaternion.identity, 0.3f, r, 2.4f, 16, steel, false);
                Cyl(b, sx, 5.6f, sz, r, 15.5f, 16, steel);
                b.B.AddFrustum(new Vector3(sx, 21.3f, sz), Quaternion.identity, r, 0.5f, 0.4f, 16, steel);
                Cyl(b, sx, 21.5f, sz, 0.45f, 1.0f, 10, galv);                               // dust collector
                Rod(b, new Vector3(sx - 1.2f, 6f, sz), new Vector3(px + 1.5f, binY + 2.5f, sz * 0.3f + pz * 0.7f), 0.15f, 8, blue);   // screw conveyor
                b.Solid(new Vector3(sx - r, 0f, sz - r), new Vector3(sx + r, 21.5f, sz + r));
            }
            // ladder cage up the near silo
            Bx(b, px + 7.5f + 1.83f, 5.6f, pz - 2.6f, px + 7.5f + 2.0f, 21.4f, pz - 2.2f, galv);
            // inclined charging conveyor from the ground hopper at the back up to the bins
            Vector3 c0 = new Vector3(px, 1.2f, -D + 6f), c1 = new Vector3(px, binY + 4.6f, pz - 3.5f);
            Member(b, c0, c1, 1.1f, 0.5f, steel);
            Member(b, c0 + Vector3.up * 1.2f, c1 + Vector3.up * 1.2f, 1.1f, 0.06f, galv);   // cover
            for (float t = 0.25f; t < 0.9f; t += 0.25f)
            {
                Vector3 m = Vector3.Lerp(c0, c1, t);
                Bx(b, m.x - 0.6f, 0f, m.z - 0.12f, m.x - 0.4f, m.y, m.z + 0.12f, blue);
                Bx(b, m.x + 0.4f, 0f, m.z - 0.12f, m.x + 0.6f, m.y, m.z + 0.12f, blue);
            }
            Bx(b, px - 1.8f, 0f, -D + 3.5f, px + 1.8f, 1.8f, -D + 8f, steel);           // ground hopper
            // aggregate bays: piles separated by concrete block (2'x2'x6' interlocking) walls
            float bayW = 9f;
            int bays = Mathf.Clamp(Mathf.FloorToInt((hw - 2f - (px + 4f)) / bayW), 1, 4);
            for (int i = 0; i <= bays; i++)
            {
                float x = px + 4f + i * bayW;
                for (int lvl = 0; lvl < 3; lvl++)
                    for (int j = 0; j < 4; j++)
                        Bx(b, x - 0.3f, lvl * 0.61f, -D + 1f + j * 1.83f, x + 0.3f, lvl * 0.61f + 0.6f, -D + 1f + (j + 1) * 1.83f - 0.02f, block);
            }
            for (int i = 0; i < bays; i++)
                Pile(b, new Vector3(px + 4f + (i + 0.5f) * bayW, 0f, -D + 5f), 4.3f, i % 3 == 0 ? sand : i % 3 == 1 ? gravel : stone);
            Pile(b, new Vector3(-hw + 7f, 0f, -D + 7f), 6f, sand);
            Pile(b, new Vector3(-hw + 7f, 0f, -D + 19f), 5.2f, gravel);
            // office trailer (14'x60') + steps at the front right
            float ox = hw - 4f, oz = -3.5f;
            int off = b.K.M(SurfaceKind.PaintedWhite, new Color(0.9f, 0.9f, 0.86f));
            Bx(b, ox - 2.1f, 0.6f, oz - 18.3f * 0.6f, ox + 2.1f, 3.4f, oz, off);
            Bx(b, ox - 2.1f, 0f, oz - 10.8f, ox + 2.1f, 0.6f, oz, dark, Walls);
            b.BeginLocal(new Vector3(ox - 2.1f, 0f, oz - 5f), Quaternion.Euler(0f, -90f, 0f));
            for (int i = -1; i <= 1; i++) Window(b, i * 2.6f, 1.6f, 0f, 1.2f, 0.9f, b.K.M(MapMaterials.Glass), off, dark);
            ManDoor(b, 1.3f, 0f, off, off);
            b.EndLocal();
            Bx(b, ox - 3.4f, 0f, oz - 4.6f, ox - 2.1f, 0.6f, oz - 3.0f, b.K.M(SurfaceKind.TreatedLumber));
            b.Solid(new Vector3(ox - 2.1f, 0f, oz - 10.8f), new Vector3(ox + 2.1f, 3.4f, oz));
            // mixer trucks: one under the plant, two parked
            MixerTruck(b, new Vector3(px, 0f, pz + 1.5f), 0f);
            MixerTruck(b, new Vector3(px + 13f, 0f, -6.5f), 0f);
            MixerTruck(b, new Vector3(px + 17f, 0f, -6.5f), 0f);
            Excavator(b, new Vector3(-hw + 14f, 0f, -D + 14f), 230f);   // wheel-loader stand-in feeding the hopper
            SignBoard(b, SignOf(s, "READY MIX"), -hw + 8f, 0.6f, 0.8f, 10f, 1.4f, Paint(b, new Color(0.2f, 0.36f, 0.58f)), Flat(b, new Color(0.97f, 0.97f, 0.95f)));
            foreach (int k in new[] { -1, 1 }) Bx(b, -hw + 8f + k * 4.2f - 0.08f, 0f, 0.62f, -hw + 8f + k * 4.2f + 0.08f, 0.62f, 0.78f, dark);
        }

        // =====================================================================================================
        // self storage
        // =====================================================================================================
        static void SelfStorage(MapBuild b, MapBuildingSpec s)
        {
            float W = s.Width, D = s.Depth, hw = W * 0.5f;
            int rows = Mathf.Clamp(s.Units, 1, 6);
            float bw = 6.1f, aisle = rows > 1 ? (W - rows * bw) / (rows - 1) : 0f;
            if (rows > 1 && aisle < 6f) { aisle = 6f; bw = (W - aisle * (rows - 1)) / rows; }
            var doorColors = new[] { new Color(0.85f, 0.38f, 0.08f), new Color(0.15f, 0.36f, 0.66f), new Color(0.7f, 0.12f, 0.1f), new Color(0.2f, 0.5f, 0.3f) };
            int door = Paint(b, doorColors[Mathf.Abs(s.Seed) % doorColors.Length]);
            int wall = Panel(b, new Color(0.88f, 0.86f, 0.8f)), roof = Panel(b, new Color(0.7f, 0.7f, 0.68f)), trim = Paint(b, new Color(0.88f, 0.86f, 0.8f) * 0.8f);
            float zf = -6f, zb = -D + 0.5f, eave = 2.9f;
            for (int r = 0; r < rows; r++)
            {
                float cx = -hw + bw * 0.5f + r * (bw + aisle);
                float hwb = bw * 0.5f;
                At(b, new Vector3(cx, 0f, 0f), 0f, () => GableBuilding(b, hwb, zb, zf, eave, 0.35f, 0.25f, 0.25f, wall, roof, trim));
                // 10' bays, 8'x7' roll-up doors on aisle sides
                foreach (int sx in new[] { -1, 1 })
                {
                    bool facesAisle = (sx < 0 && r > 0) || (sx > 0 && r < rows - 1) || rows == 1;
                    if (!facesAisle) continue;
                    float wx = cx + sx * bw * 0.5f;
                    for (float z = zf - 1.6f; z > zb + 1.2f; z -= 3.05f)
                    {
                        b.BeginLocal(new Vector3(wx, 0f, z), Quaternion.Euler(0f, sx * 90f, 0f));
                        OverheadDoor(b, 0f, 0f, 2.44f, 2.13f, door, trim);
                        b.EndLocal();
                    }
                }
            }
            // office in front of the first row, gate keypad + sliding gate + fence
            int glass = b.K.M(MapMaterials.Glass), dark = Dark(b), off = b.K.M(MapMaterials.Stucco(new Color(0.9f, 0.86f, 0.76f)));
            float ox = -hw + 4f;
            Bx(b, ox - 3.5f, 0f, -5.5f, ox + 3.5f, 3.2f, 0f, off, NoBottom);
            Window(b, ox + 1f, 0.5f, 0f, 3f, 2f, glass, trim, dark);
            ManDoor(b, ox - 2f, 0f, glass, trim);
            b.Solid(new Vector3(ox - 3.5f, 0f, -5.5f), new Vector3(ox + 3.5f, 3.2f, 0f));
            SignBoard(b, SignOf(s, "SELF STORAGE"), ox, 3.25f, 0.05f, 7f, 0.9f, door, Flat(b, Color.white));
            float gx0 = ox + 4.5f;
            PropModels.Fence(b, new Vector3(gx0 + 7f, 0f, -2f), new Vector3(hw - 0.3f, 0f, -2f), PropModels.FenceKind.ChainLink);
            Bx(b, gx0 - 0.08f, 0f, -2.08f, gx0 + 0.08f, 2.1f, -1.92f, Galv(b));
            Bx(b, gx0, 0.2f, -2.04f, gx0 + 7f, 1.9f, -1.96f, b.K.M(PropModels.ChainLinkMaterial()));
            Bx(b, gx0 - 1.5f, 0f, 1f, gx0 - 1.3f, 1.2f, 1.2f, dark);                        // keypad post
            PropModels.Fence(b, new Vector3(-hw + 0.2f, 0f, -6f), new Vector3(-hw + 0.2f, 0f, -D + 0.2f), PropModels.FenceKind.ChainLink);
            PropModels.Fence(b, new Vector3(hw - 0.2f, 0f, -2f), new Vector3(hw - 0.2f, 0f, -D + 0.2f), PropModels.FenceKind.ChainLink);
        }

        // =====================================================================================================
        // contractor yard
        // =====================================================================================================
        static void ContractorYard(MapBuild b, MapBuildingSpec s)
        {
            float W = s.Width, D = s.Depth, hw = W * 0.5f;
            var sz = MapBodies.YardShop(s); float sw = sz.x, sd = sz.y;
            var shop = new MapBuildingSpec { Type = "metalshop", Width = sw, Depth = sd, Color = s.Color, Sign = s.Sign, Seed = s.Seed };
            float scx = -hw + sw * 0.5f + 0.5f;
            MetalShopAt(b, shop, scx, sw, sd, true);
            int dirt = b.K.M(MapMaterials.Dirt), gravel = b.K.M(MapMaterials.Gravel), pipe = b.K.M(SurfaceKind.PaintedSteel, new Color(0.15f, 0.5f, 0.25f));
            int conc = Conc(b);
            ExcavatorAndTrucks(b, s, hw, D, sd);
            Pile(b, new Vector3(-hw + 6f, 0f, -D + 6f), 4.5f, dirt);
            Pile(b, new Vector3(-hw + 14f, 0f, -D + 5f), 3.6f, gravel);
            // stacked PVC/HDPE pipe and concrete pipe
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5 - i; j++)
                    b.B.AddCylinder(new Vector3(hw - 8f + j * 0.32f + i * 0.16f, 0.16f + i * 0.28f, -sd - 6f), Quaternion.Euler(90f, 0f, 0f), 0.16f, 6f, 10, pipe);
            for (int i = 0; i < 3; i++)
                b.B.AddCylinder(new Vector3(hw - 3f, 0.55f, -sd - 3f - i * 2.6f), Quaternion.Euler(0f, 0f, 90f), 0.55f, 2.4f, 14, conc, false);
            // yard fence on the open side (the lot fence along the back comes from the site)
            PropModels.Fence(b, new Vector3(-hw + 0.3f, 0f, -D + 0.3f), new Vector3(-hw + 0.3f, 0f, -sd - 0.5f), PropModels.FenceKind.ChainLink);
        }

        static void ExcavatorAndTrucks(MapBuild b, MapBuildingSpec s, float hw, float D, float sd)
        {
            Excavator(b, new Vector3(-hw + 10f, 0f, -sd - 9f), 160f);
            DumpTruck(b, new Vector3(hw - 9f, 0f, -sd - 14f), 0f, s.Color);
            if (D > 40f) DumpTruck(b, new Vector3(hw - 13f, 0f, -sd - 14f), 0f, s.Color);
        }

        // =====================================================================================================
        // water tower
        // =====================================================================================================
        static void WaterTower(MapBuild b, MapBuildingSpec s)
        {
            int paint = Paint(b, new Color(0.88f, 0.9f, 0.92f)), ink = Paint(b, new Color(0.12f, 0.24f, 0.48f)), dark = Dark(b), conc = Conc(b);
            float zc = -s.Depth * 0.5f;
            Cyl(b, 0f, 0f, zc, 4.2f, 0.6f, 24, conc);                                      // foundation
            b.B.AddFrustum(new Vector3(0f, 3.6f, zc), Quaternion.identity, 3.4f, 1.6f, 6f, 24, paint, false);   // flared base
            Cyl(b, 0f, 6.6f, zc, 1.6f, 24f, 20, paint, false);                              // pedestal (10'+ dia)
            b.B.AddFrustum(new Vector3(0f, 31.6f, zc), Quaternion.identity, 1.6f, 5.2f, 2f, 24, paint, false);  // cone to the bowl
            // spheroid bowl (~200,000 gal): 15 m wide, 11 m tall, centre at 37 m
            float cy = 37.5f, R = 7.6f, Rv = 5.6f;
            var prof = new Vector2[13];
            for (int i = 0; i <= 12; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * i / 12f;
                prof[i] = new Vector2(Mathf.Max(0f, Mathf.Cos(a) * R), Mathf.Sin(a) * Rv);
            }
            prof[0].x = 0f; prof[12].x = 0f;
            b.B.AddLathe(new Vector3(0f, cy, zc), Quaternion.identity, prof, 28, paint, 50f);
            Cyl(b, 0f, cy + Rv - 0.05f, zc, 0.5f, 0.8f, 10, paint);                         // roof vent
            b.B.AddRing(new Vector3(0f, cy - Rv * 0.55f, zc), Quaternion.identity, R * 0.86f + 0.9f, R * 0.86f + 0.75f, 1.05f, 32, dark);   // catwalk rail
            ManDoor(b, 0f, zc + 3.0f, dark, paint);
            // town name around the bowl, one letter per facet (faces +Z)
            string text = SignOf(s, "MILLBROOK");
            float px = 0.26f, adv = 6f * px, rr = R + 0.02f;
            for (int i = 0; i < text.Length; i++)
            {
                float ang = (i - (text.Length - 1) * 0.5f) * adv / rr;
                var pos = new Vector3(Mathf.Sin(ang) * (rr - 0.1f), cy - 3.5f * px, zc + Mathf.Cos(ang) * (rr - 0.1f));
                b.BeginLocal(pos, Quaternion.Euler(0f, ang * Mathf.Rad2Deg, 0f));
                MapText.Draw(b, text[i].ToString(), Vector3.zero, px, ink);
                b.EndLocal();
            }
            b.Solid(new Vector3(-3.4f, 0f, zc - 3.4f), new Vector3(3.4f, 30f, zc + 3.4f));
            float hl = s.Width * 0.5f - 0.5f;
            PropModels.Fence(b, new Vector3(-hl, 0f, 0f), new Vector3(hl, 0f, 0f), PropModels.FenceKind.ChainLink);
            PropModels.Fence(b, new Vector3(hl, 0f, 0f), new Vector3(hl, 0f, -s.Depth), PropModels.FenceKind.ChainLink);
            PropModels.Fence(b, new Vector3(hl, 0f, -s.Depth), new Vector3(-hl, 0f, -s.Depth), PropModels.FenceKind.ChainLink);
            PropModels.Fence(b, new Vector3(-hl, 0f, -s.Depth), new Vector3(-hl, 0f, 0f), PropModels.FenceKind.ChainLink);
        }

        // =====================================================================================================
        // barn
        // =====================================================================================================
        static void Barn(MapBuild b, MapBuildingSpec s)
        {
            float W = s.Width, D = s.Depth, hw = W * 0.5f, eave = 4.6f;
            int boards = b.K.M(SurfaceKind.PineLumber, s.Color * 1.4f), white = Paint(b, new Color(0.95f, 0.94f, 0.9f));
            int roof = Panel(b, new Color(0.55f, 0.56f, 0.56f)), stone = b.K.M(SurfaceKind.Concrete, new Color(0.85f, 0.83f, 0.8f)), dark = Dark(b);
            // gambrel profile: lower slope ~60°, upper ~25°
            float kx = hw * 0.62f, ky = eave + (hw - kx) * 1.75f, ry = ky + kx * 0.47f;
            Bx(b, -hw, 0f, -D, hw, eave, 0f, boards, Walls, Vector3.up);
            Bx(b, -hw - 0.05f, 0f, -D - 0.05f, hw + 0.05f, 0.8f, 0.05f, stone, Walls);        // foundation
            foreach (float z in new[] { 0f, -D })
            {
                var pts = new[] { new Vector3(-hw, eave, z), new Vector3(hw, eave, z), new Vector3(kx, ky, z), new Vector3(0f, ry, z), new Vector3(-kx, ky, z) };
                b.B.AddPolygon(pts, boards, new Vector3(0f, 0f, z == 0f ? 1f : -1f), Vector3.up);
            }
            float oh = 0.35f, z0 = -D - oh, z1 = oh;
            var prof = new[] { new Vector2(-hw - 0.25f, eave - 0.4f), new Vector2(-kx, ky), new Vector2(0f, ry), new Vector2(kx, ky), new Vector2(hw + 0.25f, eave - 0.4f) };
            for (int i = 0; i < 4; i++)
            {
                Vector2 p0 = prof[i], p1 = prof[i + 1];
                Vector2 dir = (p1 - p0).normalized;
                var n = new Vector3(-dir.y, dir.x, 0f);
                Vector3 a = new Vector3(p0.x, p0.y, z0) + n * 0.06f, c = new Vector3(p1.x, p1.y, z0) + n * 0.06f;
                b.B.AddQuad(a, c, c + new Vector3(0f, 0f, z1 - z0), a + new Vector3(0f, 0f, z1 - z0), roof, n, Vector3.forward);
                b.B.AddQuad(a, c, c + new Vector3(0f, 0f, z1 - z0), a + new Vector3(0f, 0f, z1 - z0), roof, -n, Vector3.forward);
                Member(b, new Vector3(p0.x, p0.y, z1), new Vector3(p1.x, p1.y, z1), 0.08f, 0.22f, white);    // rake trim
            }
            foreach (int sx in new[] { -1, 1 }) foreach (float z in new[] { 0f, -D })
                Bx(b, sx * hw - 0.08f, 0.8f, z - 0.08f, sx * hw + 0.08f, eave, z + 0.08f, white);
            // big sliding doors with white X-bracing, hay door above, cupola
            float dw = Mathf.Min(3.6f, hw * 0.55f), dh = 3.9f;
            foreach (int sx in new[] { -1, 1 })
            {
                float x0 = sx < 0 ? -dw : 0f, x1 = sx < 0 ? 0f : dw;
                Bx(b, x0, 0f, 0f, x1, dh, 0.06f, boards);
                Bx(b, x0, 0f, 0.06f, x1, 0.15f, 0.1f, white); Bx(b, x0, dh - 0.15f, 0.06f, x1, dh, 0.1f, white);
                Bx(b, x0, 0f, 0.06f, x0 + 0.15f, dh, 0.1f, white); Bx(b, x1 - 0.15f, 0f, 0.06f, x1, dh, 0.1f, white);
                Member(b, new Vector3(x0 + 0.1f, 0.1f, 0.09f), new Vector3(x1 - 0.1f, dh - 0.1f, 0.09f), 0.12f, 0.03f, white);
                Member(b, new Vector3(x1 - 0.1f, 0.1f, 0.09f), new Vector3(x0 + 0.1f, dh - 0.1f, 0.09f), 0.12f, 0.03f, white);
            }
            Bx(b, -dw - 0.2f, dh + 0.1f, 0f, dw + 0.2f, dh + 0.25f, 0.18f, dark);              // door track
            Bx(b, -1.0f, ky - 2.5f, 0f, 1.0f, ky - 0.5f, 0.05f, dark);                          // hay door
            Bx(b, -1.1f, ky - 2.6f, 0.05f, 1.1f, ky - 0.4f, 0.09f, white, MeshBuilder.AllFaces);
            Bx(b, -0.95f, ky - 2.45f, 0.09f, 0.95f, ky - 0.55f, 0.1f, boards);
            Member(b, new Vector3(0f, ry + 0.4f, 0.2f), new Vector3(0f, ry + 0.4f, 2.0f), 0.15f, 0.15f, dark);   // hay hood beam
            // cupola
            float cz = -D * 0.5f;
            Bx(b, -0.8f, ry - 0.3f, cz - 0.8f, 0.8f, ry + 1.2f, cz + 0.8f, white, NoBottom);
            for (int i = 0; i < 4; i++)
            {
                b.BeginLocal(new Vector3(0f, 0f, cz), Quaternion.Euler(0f, i * 90f, 0f));
                Bx(b, -0.55f, ry + 0.2f, 0.8f, 0.55f, ry + 1.0f, 0.83f, dark);
                b.EndLocal();
            }
            b.B.AddFrustum(new Vector3(0f, ry + 1.6f, cz), Quaternion.identity, 1.2f, 0.05f, 0.8f, 4, roof);
            Rod(b, new Vector3(0f, ry + 2f, cz), new Vector3(0f, ry + 2.9f, cz), 0.02f, 4, dark);
            // side windows
            for (float z = -2.5f; z > -D + 2f; z -= 3.6f)
                foreach (int sx in new[] { -1, 1 })
                {
                    b.BeginLocal(new Vector3(sx * hw, 0f, z), Quaternion.Euler(0f, sx * 90f, 0f));
                    Window(b, 0f, 1.9f, 0f, 0.9f, 0.7f, b.K.M(MapMaterials.Glass), white, dark);
                    b.EndLocal();
                }
            b.Solid(new Vector3(-hw, 0f, -D), new Vector3(hw, ky, 0f));
        }

        // =====================================================================================================
        // concrete stave silo
        // =====================================================================================================
        static void Silo(MapBuild b, MapBuildingSpec s)
        {
            float r = Mathf.Min(s.Width, s.Depth) * 0.5f - 0.1f, h = 18.3f, zc = -s.Depth * 0.5f;
            int stave = b.K.M(SurfaceKind.Concrete, new Color(0.88f, 0.86f, 0.82f)), dome = Galv(b), hoop = Paint(b, new Color(0.35f, 0.3f, 0.26f));
            Cyl(b, 0f, 0f, zc, r, h, 24, stave, false);
            for (float y = 1.2f; y < h; y += 1.5f) b.B.AddRing(new Vector3(0f, y, zc), Quaternion.identity, r + 0.03f, r - 0.02f, 0.05f, 24, hoop);
            var prof = new Vector2[7];
            for (int i = 0; i <= 6; i++) { float a = Mathf.PI * 0.5f * i / 6f; prof[i] = new Vector2(Mathf.Cos(a) * (r + 0.1f), Mathf.Sin(a) * r * 0.9f); }
            prof[6].x = 0f;
            b.B.AddLathe(new Vector3(0f, h, zc), Quaternion.identity, prof, 24, dome, 50f);
            // chute + ladder on the front
            Bx(b, -0.45f, 0.5f, zc + r - 0.1f, 0.45f, h, zc + r + 0.55f, Paint(b, new Color(0.55f, 0.56f, 0.56f)));
            b.Solid(new Vector3(-r, 0f, zc - r), new Vector3(r, h, zc + r));
        }

        // =====================================================================================================
        // grain bins
        // =====================================================================================================
        static void GrainBins(MapBuild b, MapBuildingSpec s)
        {
            int n = Mathf.Clamp(s.Units, 1, 6);
            float pitch = s.Width / n, r = Mathf.Min(pitch - 1.2f, s.Depth - 0.6f) * 0.5f, eave = Mathf.Clamp(r * 1.75f, 5f, 9f), zc = -s.Depth * 0.5f;
            int galv = Galv(b), conc = Conc(b), dark = Dark(b), steel = Paint(b, new Color(0.55f, 0.56f, 0.55f));
            for (int i = 0; i < n; i++)
            {
                float x = -s.Width * 0.5f + pitch * (i + 0.5f);
                float ri = r * (i == n - 1 && n > 2 ? 0.8f : 1f), ei = eave * (i == n - 1 && n > 2 ? 0.85f : 1f);
                Cyl(b, x, 0f, zc, ri + 0.3f, 0.3f, 24, conc);
                Cyl(b, x, 0.3f, zc, ri, ei, 28, galv, false);
                for (float y = 1.1f; y < ei; y += 0.85f) b.B.AddRing(new Vector3(x, 0.3f + y, zc), Quaternion.identity, ri + 0.02f, ri - 0.02f, 0.04f, 28, galv);
                b.B.AddFrustum(new Vector3(x, 0.3f + ei + ri * 0.29f, zc), Quaternion.identity, ri + 0.12f, 0.45f, ri * 0.58f, 28, galv);   // 30° roof
                Cyl(b, x, 0.3f + ei + ri * 0.58f, zc, 0.45f, 0.4f, 12, galv);
                Bx(b, x - 0.25f, 0.3f, zc + ri - 0.02f, x + 0.25f, 0.3f + ei, zc + ri + 0.3f, steel);               // ladder
                Bx(b, x - 0.6f, 0f, zc + ri, x + 0.6f, 1.2f, zc + ri + 1.0f, steel);                                // aeration fan
                Cyl(b, x, 0.3f, zc + ri + 0.6f, 0.45f, 0.75f, 12, dark);
                b.Solid(new Vector3(x - ri, 0f, zc - ri), new Vector3(x + ri, ei, zc + ri));
            }
            // bucket elevator leg beside the row and a portable auger
            float lx = s.Width * 0.5f + 0.6f;
            Bx(b, lx - 0.4f, 0f, zc - 0.4f, lx + 0.4f, eave + r + 4f, zc + 0.4f, steel);
            for (int i = 0; i < n; i++)
            {
                float x = -s.Width * 0.5f + pitch * (i + 0.5f);
                Rod(b, new Vector3(lx, eave + r + 3.6f, zc), new Vector3(x, 0.3f + eave + r * 0.58f + 0.2f, zc), 0.1f, 6, steel);
            }
            Rod(b, new Vector3(-s.Width * 0.5f - 6f, 0.6f, zc + 3f), new Vector3(-s.Width * 0.5f + pitch * 0.5f - r * 0.3f, eave - 0.5f, zc + r * 0.6f), 0.15f, 10, galv);
            foreach (int k in new[] { -1, 1 })
                b.B.AddCylinder(new Vector3(-s.Width * 0.5f - 3.5f + k * 0.3f, 0.35f, zc + 2.6f), Quaternion.Euler(0f, 0f, 90f), 0.35f, 0.2f, 10, b.K.M(SurfaceKind.Tire));
        }

        // =====================================================================================================
        // machine shed (post-frame pole barn, open front)
        // =====================================================================================================
        static void MachineShed(MapBuild b, MapBuildingSpec s)
        {
            float W = s.Width, D = s.Depth, hw = W * 0.5f, eave = 4.9f, hd = D * 0.5f, rise = hd * 4f / 12f;
            int wall = Panel(b, s.Color), roof = Panel(b, new Color(0.62f, 0.63f, 0.62f)), trim = Paint(b, new Color(0.95f, 0.95f, 0.92f));
            int post = b.K.M(SurfaceKind.TreatedLumber), dark = Dark(b), dirt = b.K.M(MapMaterials.Dirt);
            // walls: back + both ends, front open between posts; ridge along X (eave faces the street)
            Bx(b, -hw, 0f, -D, hw, eave, -D + 0.05f, wall, MeshBuilder.AllFaces, Vector3.right);
            Bx(b, -hw, 0f, -D, -hw + 0.05f, eave, 0f, wall, MeshBuilder.AllFaces, Vector3.forward);
            Bx(b, hw - 0.05f, 0f, -D, hw, eave, 0f, wall, MeshBuilder.AllFaces, Vector3.forward);
            Bx(b, -hw, eave - 0.9f, -0.05f, hw, eave, 0f, wall, MeshBuilder.AllFaces, Vector3.right);   // front header
            Bx(b, -hw, 0f, -D, hw, 0.02f, 0f, dirt, MeshBuilder.FacePosY);
            foreach (float x in new[] { -1f, 1f })
                b.B.AddTriangle(new Vector3(x * hw, eave, -D), new Vector3(x * hw, eave, 0f), new Vector3(x * hw, eave + rise, -hd), wall, new Vector3(x, 0f, 0f));
            // posts every 12' along the open front + interior shade
            int bays = Mathf.Max(2, Mathf.RoundToInt(W / 3.66f));
            for (int i = 0; i <= bays; i++)
            {
                float x = -hw + W * i / bays;
                Bx(b, x - 0.1f, 0f, -0.2f, x + 0.1f, eave - 0.9f, 0f, post);
            }
            Bx(b, -hw + 0.06f, eave - 0.05f, -D + 0.06f, hw - 0.06f, eave, -0.06f, dark, MeshBuilder.FaceNegY);   // dark ceiling
            // roof, ridge along X
            float oh = 0.3f;
            Vector3 r0 = new Vector3(-hw - oh, eave + rise, -hd), r1 = new Vector3(hw + oh, eave + rise, -hd);
            for (int sz = -1; sz <= 1; sz += 2)
            {
                float ez = sz > 0 ? oh : -D - oh, ey = eave - oh * rise / hd;
                Vector3 e0 = new Vector3(-hw - oh, ey, ez), e1 = new Vector3(hw + oh, ey, ez);
                var n = new Vector3(0f, hd, sz * rise).normalized;
                b.B.AddQuad(e0 + n * 0.06f, e1 + n * 0.06f, r1 + n * 0.06f, r0 + n * 0.06f, roof, n, Vector3.right);
                b.B.AddQuad(e0, e1, r1, r0, roof, -n, Vector3.right);
                Bx(b, -hw - oh, ey - 0.14f, ez - 0.07f, hw + oh, ey + 0.02f, ez + 0.07f, trim);
            }
            foreach (int sx in new[] { -1, 1 }) foreach (float z in new[] { 0f, -D })
                Bx(b, sx * hw - 0.07f, 0f, z - 0.07f, sx * hw + 0.07f, eave, z + 0.07f, trim);
            // equipment inside: tractor + hay wagon/planter stand-in
            Sub(b, "tractor", new Vector3(-hw * 0.45f, 0f, -D * 0.5f), 0f, new Color(0.15f, 0.45f, 0.18f), s.Seed);
            int wag = Paint(b, new Color(0.65f, 0.12f, 0.1f));
            Bx(b, hw * 0.15f, 0.6f, -D + 1.5f, hw * 0.15f + 2.4f, 0.75f, -D + 1.5f + 5.5f, b.K.M(SurfaceKind.PineLumber));
            Bx(b, hw * 0.15f, 0.75f, -D + 1.5f, hw * 0.15f + 0.08f, 1.6f, -D + 7f, wag);
            Bx(b, hw * 0.15f + 2.32f, 0.75f, -D + 1.5f, hw * 0.15f + 2.4f, 1.6f, -D + 7f, wag);
            foreach (float z in new[] { -D + 2.5f, -D + 6f }) foreach (float x in new[] { hw * 0.15f + 0.2f, hw * 0.15f + 2.2f })
                b.B.AddCylinder(new Vector3(x, 0.38f, z), Quaternion.Euler(0f, 0f, 90f), 0.38f, 0.22f, 10, b.K.M(SurfaceKind.Tire));
            b.Solid(new Vector3(-hw, 0f, -D), new Vector3(hw, eave, -D + 0.1f));
            b.Solid(new Vector3(-hw, 0f, -D), new Vector3(-hw + 0.1f, eave, 0f));
            b.Solid(new Vector3(hw - 0.1f, 0f, -D), new Vector3(hw, eave, 0f));
        }
    }
}
