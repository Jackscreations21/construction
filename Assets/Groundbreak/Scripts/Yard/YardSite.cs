using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The company's premises as a walkable place at Yard.Position: level 1 rented flex bay, level 2 fenced yard +
    /// steel shop, level 3 HQ warehouse with a two-storey office. Street side is local -Z. Static shell is built once per
    /// level; stored materials, parked vehicles and shop equipment are rebuilt by RefreshContents.</summary>
    public class YardSite : MonoBehaviour
    {
        public static YardSite I;
        public int Level;
        public Vector3 PlayerSpawn, OfficeDesk, CameraSpot, CameraLook, InteriorCameraSpot, InteriorCameraLook;
        public Bounds WorldBounds;
        readonly List<GameObject> _parked = new List<GameObject>();
        public IReadOnlyList<GameObject> ParkedVehicles => _parked;
        /// <summary>Optional: vehicles not to draw in a stall (Integration: the drivable work van while it exists in town).</summary>
        public static System.Func<OwnedVehicle, bool> SkipParked;

        // Layout (local) filled by the level builders.
        readonly List<(Vector3 pos, Vector3 room)> _shelfSlots = new List<(Vector3, Vector3)>();   // pallet-rack beam levels
        readonly List<(Vector3 pos, Vector3 room)> _longSlots = new List<(Vector3, Vector3)>();    // lumber racks / dunnage
        readonly List<Vector3> _bulkSlots = new List<Vector3>();                                    // bunkers / bulk-bag spots
        readonly List<(Vector3 pos, float yaw)> _stalls = new List<(Vector3, float)>();
        Vector3 _streetParking, _equipInside, _equipOutside;
        // shop equipment poses (local floor point + yaw), set by the level builders; models in YardEquipmentModels
        (Vector3 pos, float yaw) _sawPose, _forkPose, _compPose, _liftPose;

        GameObject _colliders;
        Transform _contents;

        const float Ft = 0.3048f;
        static float F(float feet) => feet * Ft;

        // ------------------------------------------------------------------ public

        public static YardSite Rebuild()
        {
            if (I != null) { Destroy(I.gameObject); I = null; }
            int level = Company.WarehouseLevel;
            if (level <= 0) return null;
            var go = new GameObject("CompanyPremises");
            go.transform.position = Yard.Position;
            var s = go.AddComponent<YardSite>();
            I = s; s.Level = level;
            s._colliders = new GameObject("Colliders");
            s._colliders.transform.SetParent(go.transform, false);
            var shell = new GameObject("Shell").transform; shell.SetParent(go.transform, false);
            switch (level)
            {
                case 1: s.BuildBay(shell); break;
                case 2: s.BuildYardShop(shell); break;
                default: s.BuildHQ(shell); break;
            }
            s.ComputeBounds();
            s.RefreshContents();
            return s;
        }

        void OnDestroy() { if (I == this) I = null; }

        /// <summary>World pose of parking stall i (vehicles park in Company.Vehicles order). False if there is no such stall.</summary>
        public bool TryStall(int i, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            if (i < 0 || i >= _stalls.Count) return false;
            pos = W(_stalls[i].pos); pos.y = transform.position.y;
            yaw = _stalls[i].yaw + transform.eulerAngles.y;
            return true;
        }

        /// <summary>World floor point + facing of a shop equipment model ("sawstation", "forklift", "compressor", "lift",
        /// "fueltank"); the model's front/working side faces rot * back. False for an unknown id.</summary>
        public bool TryEquipmentPose(string id, out Vector3 pos, out Quaternion rot)
        {
            (Vector3 pos, float yaw) p;
            switch (id)
            {
                case "sawstation": p = _sawPose; break;
                case "forklift": p = _forkPose; break;
                case "compressor": p = _compPose; break;
                case "lift": p = _liftPose; break;
                case "fueltank": p = (_equipOutside, 0f); break;
                default: pos = transform.position; rot = transform.rotation; return false;
            }
            pos = W(p.pos); rot = transform.rotation * Quaternion.Euler(0f, p.yaw, 0f);
            return true;
        }

        Vector3 W(Vector3 local) => transform.TransformPoint(local);

        void ComputeBounds()
        {
            bool any = false; var b = new Bounds(transform.position, Vector3.one);
            foreach (var r in GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            WorldBounds = b;
        }

        // ------------------------------------------------------------------ helpers

        void Col(Vector3 center, Vector3 size)
        {
            var c = _colliders.AddComponent<BoxCollider>();
            c.center = center; c.size = size;
        }

        /// <summary>Box from min/max with a collider.</summary>
        void Solid(SitePart p, float x0, float x1, float y0, float y1, float z0, float z1, int sub)
        {
            p.Box(x0, x1, y0, y1, z0, z1, sub);
            Col(new Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)));
        }

        static void Slab(SitePart p, float x0, float x1, float z0, float z1, int sub, float top = 0.03f) => p.Box(x0, x1, 0.005f, top, z0, z1, sub);

        void Light(Transform parent, Vector3 local, float range, float intensity)
        {
            var go = new GameObject("ShopLight"); go.transform.SetParent(parent, false); go.transform.localPosition = local;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            l.color = new Color(1f, 0.97f, 0.9f);
        }

        struct Door { public float xc, w, h; public bool overhead; public Door(float xc, float w, float h, bool oh) { this.xc = xc; this.w = w; this.h = h; overhead = oh; } }

        /// <summary>Pre-engineered metal building: 6" walls, open doors in the front (-Z) wall, 1:12 gable roof with the
        /// ridge along X, interior lights. Returns the walls/roof part (already finished).</summary>
        void MetalBuilding(Transform parent, string name, float x0, float x1, float z0, float z1, float eave, List<Door> doors, Color panel, bool backDoors = false)
        {
            var p = new SitePart();
            int wall = p.M(SurfaceKind.PaintedSteel, panel), trim = p.M(SurfaceKind.PaintedSteel, new Color(0.25f, 0.27f, 0.3f));
            int roof = p.M(SurfaceKind.Galvanized), slab = p.M(SurfaceKind.Concrete), door = p.M(SurfaceKind.PaintedSteel, new Color(0.9f, 0.9f, 0.88f));
            const float t = 0.15f;
            Slab(p, x0, x1, z0, z1, slab, 0.1f);
            // side and back walls
            Solid(p, x0, x0 + t, 0.1f, eave, z0, z1, wall);
            Solid(p, x1 - t, x1, 0.1f, eave, z0, z1, wall);
            Solid(p, x0, x1, 0.1f, eave, z1 - t, z1, wall);
            // front wall with openings
            doors.Sort((a, b) => a.xc.CompareTo(b.xc));
            float cursor = x0;
            foreach (var d in doors)
            {
                float a = d.xc - d.w / 2, b = d.xc + d.w / 2;
                if (a > cursor) Solid(p, cursor, a, 0.1f, eave, z0, z0 + t, wall);
                p.Box(a, b, d.h, eave, z0, z0 + t, wall);                                       // header panel
                p.Box(a - 0.08f, a, 0.1f, d.h + 0.08f, z0 - 0.03f, z0 + t, trim);                // jamb trim
                p.Box(b, b + 0.08f, 0.1f, d.h + 0.08f, z0 - 0.03f, z0 + t, trim);
                p.Box(a - 0.08f, b + 0.08f, d.h, d.h + 0.08f, z0 - 0.03f, z0 + t, trim);
                if (d.overhead) p.Box(a, b, d.h - 0.55f, d.h, z0 + t, z0 + t + 0.6f, door);   // rolled-up door coil/hood
                else p.Box(a - 0.02f, a + 0.05f, 0.1f, d.h, z0 + t, z0 + t + d.w, door);       // man door swung open inward
                cursor = b;
            }
            if (cursor < x1) Solid(p, cursor, x1, 0.1f, eave, z0, z0 + t, wall);
            // base trim + eave trim
            p.Box(x0 - 0.02f, x1 + 0.02f, 0.1f, 0.35f, z0 - 0.02f, z0, trim, MeshBuilder.FaceNegZ | MeshBuilder.FacePosY);
            // gable roof, ridge along X at mid depth, 1:12
            float half = (z1 - z0) / 2f, rise = half / 12f, ang = Mathf.Atan2(rise, half) * Mathf.Rad2Deg, len = Mathf.Sqrt(half * half + rise * rise) + 0.35f;
            float cx = (x0 + x1) / 2, w = x1 - x0 + 0.5f;
            p.B.AddBox(new Vector3(cx, eave + rise / 2 + 0.06f, z0 + half / 2 - 0.15f), new Vector3(w, 0.1f, len), Quaternion.Euler(-ang, 0, 0), roof);
            p.B.AddBox(new Vector3(cx, eave + rise / 2 + 0.06f, z1 - half / 2 + 0.15f), new Vector3(w, 0.1f, len), Quaternion.Euler(ang, 0, 0), roof);
            foreach (float x in new[] { x0, x1 })
            {
                var a = new Vector3(x, eave, z0); var b = new Vector3(x, eave, z1); var c = new Vector3(x, eave + rise, z0 + half);
                p.B.AddTriangle(a, b, c, wall, Vector3.left); p.B.AddTriangle(a, c, b, wall, Vector3.right);
            }
            p.Finish(parent, name);
            // interior lights (high-bay LEDs)
            for (float x = x0 + 5f; x < x1 - 2f; x += 9f)
                for (float z = z0 + 4f; z < z1 - 2f; z += 9f)
                    Light(parent, new Vector3(x, eave - 0.6f, z), 13f, 1.3f);
        }

        /// <summary>Selective pallet racking along X: 96" beams, 42" deep frames, 12' uprights, 3 beam levels.</summary>
        void PalletRack(SitePart p, float x0, float z0, int bays, bool slots = true)
        {
            int up = p.M(SurfaceKind.PaintedSteel, new Color(0.12f, 0.3f, 0.62f)), beam = p.M(SurfaceKind.PaintedSteel, new Color(0.95f, 0.45f, 0.08f));
            int deck = p.M(SurfaceKind.Galvanized);
            float bw = F(8) + 0.08f, d = F(3.5f), h = F(12);
            float[] levels = { 0.12f, 1.55f, 3.0f };
            for (int i = 0; i <= bays; i++)
            {
                float x = x0 + i * bw;
                Solid(p, x - 0.04f, x + 0.04f, 0, h, z0, z0 + 0.08f, up);
                Solid(p, x - 0.04f, x + 0.04f, 0, h, z0 + d - 0.08f, z0 + d, up);
                for (float y = 0.4f; y < h; y += 1.2f) p.Box(x - 0.015f, x + 0.015f, y, y + 0.04f, z0 + 0.08f, z0 + d - 0.08f, up);
            }
            for (int i = 0; i < bays; i++)
            {
                float xa = x0 + i * bw + 0.04f, xb = x0 + (i + 1) * bw - 0.04f;
                foreach (float y in levels)
                {
                    if (y > 0.5f)
                    {
                        p.Box(xa, xb, y - 0.12f, y, z0, z0 + 0.06f, beam);
                        p.Box(xa, xb, y - 0.12f, y, z0 + d - 0.06f, z0 + d, beam);
                        p.Box(xa, xb, y - 0.01f, y, z0 + 0.06f, z0 + d - 0.06f, deck);   // wire deck
                    }
                    if (slots) _shelfSlots.Add((new Vector3((xa + xb) / 2, y, z0 + d / 2), new Vector3(xb - xa - 0.1f, 1.3f, d - 0.12f)));
                }
            }
        }

        /// <summary>Cantilever lumber rack along Z: columns every 8', 4 arm levels, arms point toward -X.</summary>
        void LumberRack(SitePart p, float x, float z0, float z1)
        {
            int steel = p.M(SurfaceKind.PaintedSteel, new Color(0.2f, 0.35f, 0.2f));
            int n = Mathf.Max(2, Mathf.RoundToInt((z1 - z0) / F(8)) + 1);
            float step = (z1 - z0) / (n - 1);
            for (int i = 0; i < n; i++)
            {
                float z = z0 + i * step;
                Solid(p, x - 0.08f, x + 0.08f, 0, 4.0f, z - 0.08f, z + 0.08f, steel);
                p.Box(x - 1.0f, x + 0.5f, 0, 0.12f, z - 0.1f, z + 0.1f, steel);                // base
                for (int a = 0; a < 4; a++) p.Box(x - 1.25f, x - 0.08f, 0.6f + a * 0.9f, 0.68f + a * 0.9f, z - 0.05f, z + 0.05f, steel);
            }
            for (int a = 0; a < 4; a++) _longSlots.Add((new Vector3(x - 0.66f, 0.68f + a * 0.9f, (z0 + z1) / 2), new Vector3(1.1f, 0.75f, z1 - z0)));
        }

        /// <summary>Three-sided CMU aggregate bunker, opening to -Z.</summary>
        void Bunker(SitePart p, float x0, float z0, float w, float d)
        {
            int cmu = p.M(SurfaceKind.ConcreteBlock);
            float t = F(8f / 12f), h = 1.6f;
            Solid(p, x0, x0 + t, 0, h, z0, z0 + d, cmu);
            Solid(p, x0 + w - t, x0 + w, 0, h, z0, z0 + d, cmu);
            Solid(p, x0, x0 + w, 0, h, z0 + d - t, z0 + d, cmu);
            _bulkSlots.Add(new Vector3(x0 + w / 2, 0, z0 + d * 0.55f));
        }

        void ChainLinkFence(SitePart p, float x0, float x1, float z0, float z1, float gateX0, float gateX1)
        {
            int post = p.M(SurfaceKind.Galvanized), wire = p.M(SurfaceKind.Galvanized, new Color(0.75f, 0.77f, 0.78f));
            const float h = 1.83f;
            void Run(Vector3 a, Vector3 b)
            {
                float len = (b - a).magnitude; if (len < 0.05f) return;
                int n = Mathf.CeilToInt(len / 3f);
                for (int i = 0; i <= n; i++) { var q = Vector3.Lerp(a, b, i / (float)n); p.CylY(q.x, q.z, 0, h + 0.05f, 0.035f, 8, post); }
                p.Strut(a + Vector3.up * h, b + Vector3.up * h, 0.042f, 0.042f, post);
                for (float y = 0.15f; y < h; y += 0.15f) p.Strut(a + Vector3.up * y, b + Vector3.up * y, 0.006f, 0.006f, wire);
                var c = (a + b) / 2; var dir = (b - a).normalized;
                Col(new Vector3(c.x, h / 2, c.z), new Vector3(Mathf.Abs(dir.x) * len + 0.1f, h, Mathf.Abs(dir.z) * len + 0.1f));
            }
            Run(new Vector3(x0, 0, z0), new Vector3(gateX0, 0, z0));
            Run(new Vector3(gateX1, 0, z0), new Vector3(x1, 0, z0));
            Run(new Vector3(x1, 0, z0), new Vector3(x1, 0, z1));
            Run(new Vector3(x1, 0, z1), new Vector3(x0, 0, z1));
            Run(new Vector3(x0, 0, z1), new Vector3(x0, 0, z0));
            // rolling gate, open: parked behind the fence beside the opening
            float gw = gateX1 - gateX0;
            p.Strut(new Vector3(gateX0 - gw, 0.1f, z0 + 0.25f), new Vector3(gateX0, 0.1f, z0 + 0.25f), 0.05f, 0.05f, post);
            p.Strut(new Vector3(gateX0 - gw, h, z0 + 0.25f), new Vector3(gateX0, h, z0 + 0.25f), 0.05f, 0.05f, post);
            for (float y = 0.25f; y < h; y += 0.15f) p.Strut(new Vector3(gateX0 - gw, y, z0 + 0.25f), new Vector3(gateX0, y, z0 + 0.25f), 0.006f, 0.006f, wire);
        }

        /// <summary>Row of parking stalls along X (12' x 35'), painted lines on the pavement; vehicles park nose toward -Z.</summary>
        void Stalls(SitePart p, float x0, float z0, int count, float length = 10.67f, float width = 3.66f)
        {
            int paint = p.M(SurfaceKind.PaintedWhite, new Color(0.95f, 0.92f, 0.6f));
            for (int i = 0; i <= count; i++) p.Box(x0 + i * width - 0.05f, x0 + i * width + 0.05f, 0.031f, 0.034f, z0, z0 + length, paint);
            for (int i = 0; i < count; i++) _stalls.Add((new Vector3(x0 + (i + 0.5f) * width, 0.03f, z0 + length / 2), 180f));
        }

        void Office(SitePart p, float x0, float x1, float z0, float z1, float h, bool windowFront)
        {
            int ply = p.M(SurfaceKind.Plywood), glass = p.M(SurfaceKind.Glass), desk = p.M(SurfaceKind.Oak), dark = p.M(SurfaceKind.DarkPlastic);
            const float t = 0.12f;
            // walls: inner side (x1) has a door gap near z0+1
            Solid(p, x1 - t, x1, 0.1f, h, z0 + 1.6f, z1, ply);
            p.Box(x1 - t, x1, 2.1f, h, z0, z0 + 1.6f, ply);
            Solid(p, x1 - t, x1, 0.1f, h, z0, z0 + 0.6f, ply);
            if (windowFront) { Solid(p, x0, x1, 0.1f, 1.0f, z1 - t, z1, ply); p.Box(x0, x1, 1.0f, 2.1f, z1 - t / 2 - 0.01f, z1 - t / 2 + 0.01f, glass); p.Box(x0, x1, 2.1f, h, z1 - t, z1, ply); Col(new Vector3((x0 + x1) / 2, 1.5f, z1 - t / 2), new Vector3(x1 - x0, 1.2f, t)); }
            else Solid(p, x0, x1, 0.1f, h, z1 - t, z1, ply);
            p.Box(x0, x1, h, h + 0.02f, z0, z1, ply);                                          // ceiling / storage deck
            // desk + chair + filing cabinet
            float dx = (x0 + x1) / 2, dz = z0 + 1.2f;
            Solid(p, dx - 0.76f, dx + 0.76f, 0.73f, 0.76f, dz - 0.38f, dz + 0.38f, desk);
            p.Box(dx - 0.74f, dx - 0.7f, 0.1f, 0.73f, dz - 0.36f, dz + 0.36f, desk); p.Box(dx + 0.7f, dx + 0.74f, 0.1f, 0.73f, dz - 0.36f, dz + 0.36f, desk);
            p.Box(dx - 0.25f, dx + 0.25f, 0.76f, 1.1f, dz + 0.1f, dz + 0.14f, dark);          // monitor
            p.Box(dx - 0.25f, dx + 0.25f, 0.1f, 1.0f, dz + 0.65f, dz + 1.05f, dark);          // chair (simple)
            Solid(p, x0 + 0.15f, x0 + 0.6f, 0.1f, 1.4f, z1 - 0.9f, z1 - 0.2f, p.M(SurfaceKind.PaintedSteel, new Color(0.5f, 0.52f, 0.55f)));
            OfficeDesk = W(new Vector3(dx, 0, dz));
        }

        /// <summary>Workbench with pegboard tool wall and a job box (always present: tool storage).</summary>
        void Workbench(SitePart p, float x0, float z, bool againstBackWall)
        {
            int top = p.M(SurfaceKind.Plywood), frame = p.M(SurfaceKind.PineLumber), peg = p.M(SurfaceKind.Plywood, new Color(0.75f, 0.62f, 0.45f));
            float x1 = x0 + F(8), d = 0.76f, zb = againstBackWall ? z - d : z;
            Solid(p, x0, x1, 0.86f, 0.91f, zb, zb + d, top);
            foreach (float x in new[] { x0 + 0.05f, x1 - 0.1f }) foreach (float zz in new[] { zb + 0.05f, zb + d - 0.1f }) p.Box(x, x + 0.05f, 0.1f, 0.86f, zz, zz + 0.05f, frame);
            p.Box(x0, x1, 0.25f, 0.29f, zb + 0.05f, zb + d - 0.05f, top);                       // lower shelf
            p.Box(x0, x1, 1.0f, 2.2f, zb + d - 0.02f, zb + d, peg);                              // pegboard
            YardEquipmentModels.PegboardTools(p, new Vector3(x0, 0.1f, zb + d - 0.02f), 0f);
            // rolling tool chest + job box beside the bench (YardEquipmentModels), drawers/hasp toward the aisle
            YardEquipmentModels.ToolChest(p, new Vector3(x0 - 0.5f, 0.1f, zb + d - 0.25f), 0f, Col);
            YardEquipmentModels.JobBox(p, new Vector3(x0 - 1.62f, 0.1f, zb + d - 0.32f), 0f, Col);
        }

        void Sign(Transform parent, string text, Vector3 local, float size)
        {
            var go = new GameObject("Sign"); go.transform.SetParent(parent, false);
            go.transform.localPosition = local; go.transform.localRotation = Quaternion.identity;
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font; tm.text = text; tm.anchor = TextAnchor.MiddleCenter; tm.characterSize = size; tm.fontSize = 64; tm.color = new Color(0.1f, 0.15f, 0.3f);
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            go.transform.localRotation = Quaternion.Euler(0, 0, 0);
        }

        // ------------------------------------------------------------------ level 1: rented bay

        void BuildBay(Transform shell)
        {
            float w = F(30), d = F(50), eave = F(16);
            float bx0 = -w / 2, bx1 = w / 2, z0 = 0f, z1 = d;
            // the tenant's bay, plus neighbouring bays of the same flex building (closed)
            MetalBuilding(shell, "Bay", bx0, bx1, z0, z1, eave, new List<Door> { new Door(F(4), F(12), F(14), true), new Door(-F(10), F(3), F(7), false) }, new Color(0.82f, 0.83f, 0.8f));
            var n = new SitePart();
            int wall = n.M(SurfaceKind.PaintedSteel, new Color(0.82f, 0.83f, 0.8f)), door = n.M(SurfaceKind.PaintedSteel, new Color(0.9f, 0.9f, 0.88f));
            int roof = n.M(SurfaceKind.Galvanized), glass = n.M(SurfaceKind.Glass);
            foreach (int side in new[] { -1, 1 })
            {
                float xa = side < 0 ? bx0 - 2 * w : bx1, xb = side < 0 ? bx0 : bx1 + 2 * w;
                Solid(n, xa, xb, 0, eave, z0, z1, wall);
                float rise = d / 2 / 12f, ang = Mathf.Atan2(rise, d / 2) * Mathf.Rad2Deg, len = Mathf.Sqrt(d * d / 4 + rise * rise) + 0.35f;
                n.B.AddBox(new Vector3((xa + xb) / 2, eave + rise / 2 + 0.06f, d / 4 - 0.15f), new Vector3(xb - xa, 0.1f, len), Quaternion.Euler(-ang, 0, 0), roof);
                n.B.AddBox(new Vector3((xa + xb) / 2, eave + rise / 2 + 0.06f, d * 0.75f + 0.15f), new Vector3(xb - xa, 0.1f, len), Quaternion.Euler(ang, 0, 0), roof);
                for (int b = 0; b < 2; b++)
                {
                    float cx = xa + w * (b + 0.5f);
                    n.Box(cx - F(6) + F(4), cx + F(6) + F(4), 0.1f, F(14), z0 - 0.06f, z0, door);              // closed overhead door
                    for (float y = 0.4f; y < F(14); y += 0.55f) n.Box(cx - F(2), cx + F(10), y, y + 0.02f, z0 - 0.07f, z0 - 0.06f, wall);
                    n.Box(cx - F(10) - F(1.5f), cx - F(10) + F(1.5f), 0.1f, F(7), z0 - 0.05f, z0, door);
                    n.Box(cx - F(10) - F(1.5f), cx - F(10) + F(1.5f), 1.2f, 1.9f, z0 - 0.055f, z0 - 0.05f, glass);
                }
            }
            n.Finish(shell, "NeighbourBays");

            var p = new SitePart();
            int asphalt = p.M(SurfaceKind.Concrete, new Color(0.32f, 0.32f, 0.33f));
            Slab(p, bx0 - 2 * w, bx1 + 2 * w, -14f, z0, asphalt);
            Stalls(p, bx0 - F(36) - 0.5f, -13.5f, 3, 10.0f);
            Office(p, bx0 + 0.15f, bx0 + F(10), z0 + 0.15f, z0 + F(12), 2.6f, true);
            PalletRack(p, bx0 + 0.4f, z1 - F(3.5f) - 0.3f, 3);                         // along the back wall
            PalletRack(p, bx0 + F(11), F(18), 2);                                        // a free-standing row
            Workbench(p, bx1 - F(8) - 0.4f, F(39.5f), false);                         // between the free-standing rack and the back rack
            YardEquipmentModels.SafetyStation(p, shell, new Vector3(bx0 + F(10), 0.1f, 2.7f), -90f);   // on the office wall
            // floor dunnage for long stock along the right wall
            int dun = p.M(SurfaceKind.TreatedLumber);
            for (int i = 0; i < 4; i++) p.Box(bx1 - 1.5f, bx1 - 0.3f, 0.1f, 0.19f, F(16) + i * 2f, F(16) + i * 2f + 0.09f, dun);
            _longSlots.Add((new Vector3(bx1 - 0.9f, 0.19f, F(16) + 3f), new Vector3(1.1f, 1.0f, 7.5f)));
            _bulkSlots.Add(new Vector3(bx1 + 1.5f, 0, -3f));
            _bulkSlots.Add(new Vector3(bx1 + 4f, 0, -3f));
            p.Finish(shell, "BayFitOut");
            Sign(shell, Company.Name, new Vector3(F(4), F(14) + 0.5f, z0 - 0.08f), 0.06f);

            _streetParking = new Vector3(bx0 - 3 * w, 0.03f, -17f);
            _equipInside = new Vector3(bx1 - 2.0f, 0.1f, F(8)); _equipOutside = new Vector3(bx1 + 6f, 0.03f, -6f);
            _sawPose = (new Vector3(bx0 + 0.75f, 0.1f, 8.3f), -90f);        // miter bench along the left wall, table saw out front
            _compPose = (new Vector3(bx0 + 0.46f, 0.1f, 12.8f), -90f);
            _forkPose = (new Vector3(F(2), 0.1f, 2.2f), 0f);                 // just inside the overhead door, forks toward the back
            _liftPose = (new Vector3(0.5f, 0.1f, 9.5f), 0f);                // (a bay can't own a lift; kept for safety)
            PlayerSpawn = W(new Vector3(F(4), 0, -3f));
            CameraSpot = W(new Vector3(-24f, 9f, -30f)); CameraLook = W(new Vector3(-4f, 2f, 6f));
            InteriorCameraSpot = W(new Vector3(bx1 - 0.8f, 2.4f, 0.9f)); InteriorCameraLook = W(new Vector3(bx0 + 3f, 1.4f, z1 - 4f));
        }

        // ------------------------------------------------------------------ level 2: yard + shop

        void BuildYardShop(Transform shell)
        {
            float yx0 = -F(75), yx1 = F(75), yz0 = 0, yz1 = F(200);
            var p = new SitePart();
            int gravel = p.M(SurfaceKind.Gravel), asphalt = p.M(SurfaceKind.Concrete, new Color(0.32f, 0.32f, 0.33f));
            Slab(p, yx0, yx1, yz0, yz1, gravel);
            Slab(p, F(30), F(60), -8f, yz0 + 0.5f, asphalt);                              // driveway apron through the gate
            ChainLinkFence(p, yx0, yx1, yz0, yz1, F(30), F(60));
            Stalls(p, yx0 + 1.5f, 2f, 8);
            for (int r = 0; r < 2; r++) LumberRack(p, yx1 - 2.5f - r * 4.5f, F(70), F(130));
            Bunker(p, F(10), yz1 - 6.5f, 4.5f, 6f); Bunker(p, F(10) + 4.6f, yz1 - 6.5f, 4.5f, 6f); Bunker(p, F(10) + 9.2f, yz1 - 6.5f, 4.5f, 6f);
            p.Finish(shell, "Yard");

            // 40' x 60' shop at the back left, doors facing the street
            float sx0 = yx0 + 4f, sx1 = sx0 + F(60), sz0 = F(110), sz1 = sz0 + F(40), eave = F(16);
            MetalBuilding(shell, "Shop", sx0, sx1, sz0, sz1, eave, new List<Door> { new Door(sx0 + F(24), F(12), F(14), true), new Door(sx0 + F(44), F(12), F(14), true), new Door(sx0 + F(7), F(3), F(7), false) }, new Color(0.62f, 0.7f, 0.78f));
            var f = new SitePart();
            Office(f, sx0 + 0.15f, sx0 + F(14), sz0 + 0.15f, sz0 + F(12), 2.7f, true);
            PalletRack(f, sx0 + 0.4f, sz1 - F(3.5f) - 0.3f, 6);
            Workbench(f, sx1 - F(8) - 0.6f, sz1 - F(3.5f) - 0.6f - F(14), false);
            YardEquipmentModels.SafetyStation(f, shell, new Vector3(sx0 + F(14), 0.1f, sz0 + 2.9f), -90f);
            f.Finish(shell, "ShopFitOut");
            Sign(shell, Company.Name, new Vector3(sx0 + F(34), eave - 0.6f, sz0 - 0.08f), 0.07f);

            _streetParking = new Vector3(yx0 - 2f, 0.03f, -6f);
            _equipInside = new Vector3(sx1 - 3f, 0.1f, sz0 + 3f); _equipOutside = new Vector3(yx1 - 12f, 0.03f, F(150));
            _sawPose = (new Vector3(sx0 + 0.75f, 0.1f, sz0 + 6.5f), -90f);
            _compPose = (new Vector3(sx1 - 0.46f, 0.1f, sz1 - 0.82f), 90f);
            _forkPose = (new Vector3(sx1 - 1.0f, 0.1f, sz0 + 1.8f), 0f);
            _liftPose = (new Vector3(sx0 + F(44), 0.1f, sz0 + 8.8f), 0f);   // in line with the right overhead door
            PlayerSpawn = W(new Vector3(F(45), 0, -3f));
            CameraSpot = W(new Vector3(yx1 + 18f, 22f, -22f)); CameraLook = W(new Vector3(-4f, 0f, F(95)));
            InteriorCameraSpot = W(new Vector3(sx0 + F(24), 1.7f, sz0 + 1.0f)); InteriorCameraLook = W(new Vector3(sx0 + F(20), 1.8f, sz1 - 1f));
        }

        // ------------------------------------------------------------------ level 3: HQ

        void BuildHQ(Transform shell)
        {
            float yx0 = -F(125), yx1 = F(125), yz0 = 0, yz1 = F(300);
            var p = new SitePart();
            int gravel = p.M(SurfaceKind.Gravel), asphalt = p.M(SurfaceKind.Concrete, new Color(0.32f, 0.32f, 0.33f));
            Slab(p, yx0, yx1, yz0, yz1, gravel);
            Slab(p, yx0, F(40), yz0, F(55), asphalt);                                      // paved front lot
            Slab(p, F(70), F(110), -8f, yz0 + 0.5f, asphalt);
            ChainLinkFence(p, yx0, yx1, yz0, yz1, F(70), F(110));
            Stalls(p, yx0 + 1.5f, 2f, 10);
            Stalls(p, yx0 + 1.5f, 2f + 10.67f + 7f, 6);
            for (int r = 0; r < 3; r++) LumberRack(p, yx1 - 2.5f - r * 4.5f, F(110), F(200));
            for (int b = 0; b < 4; b++) Bunker(p, F(20) + b * 4.6f, yz1 - 6.5f, 4.5f, 6f);
            p.Finish(shell, "Yard");

            // 120' x 60' warehouse, long side to the street, 2-storey office across the left end
            float wx0 = yx0 + 6f, wx1 = wx0 + F(120), wz0 = F(130), wz1 = wz0 + F(60), eave = F(24);
            float ox1 = wx0 + F(36);
            var doors = new List<Door>();
            for (int i = 0; i < 4; i++) doors.Add(new Door(ox1 + F(12) + i * F(19), F(12), F(14), true));
            doors.Add(new Door(wx0 + F(18), F(6), F(7.5f), false));                          // storefront entry
            MetalBuilding(shell, "Warehouse", wx0, wx1, wz0, wz1, eave, doors, new Color(0.86f, 0.86f, 0.84f));
            var o = new SitePart();
            int glass = o.M(SurfaceKind.Glass), band = o.M(SurfaceKind.PaintedSteel, new Color(0.12f, 0.22f, 0.42f)), ply = o.M(SurfaceKind.PaintedWhite);
            int conc = o.M(SurfaceKind.Concrete);
            // storefront glazing across the office front (two storeys) + sign band
            for (int fl = 0; fl < 2; fl++)
            {
                float y0 = 0.9f + fl * 3.66f;
                o.Box(wx0 + 0.3f, ox1 - 0.3f, y0, y0 + 2.2f, wz0 - 0.03f, wz0 - 0.01f, glass);
                for (float x = wx0 + 0.3f; x <= ox1 - 0.3f; x += 1.52f) o.Box(x - 0.03f, x + 0.03f, y0, y0 + 2.2f, wz0 - 0.06f, wz0 - 0.01f, band);
            }
            o.Box(wx0, ox1, F(20), F(23), wz0 - 0.1f, wz0, band);
            Solid(o, ox1 - 0.12f, ox1, 0.1f, 2 * 3.66f, wz0 + 0.15f, wz0 + F(30), ply);    // office/warehouse demising wall (door gap at back)
            o.Box(wx0 + 0.15f, ox1, 3.66f, 3.66f + 0.2f, wz0 + 0.15f, wz1 - 0.15f, conc);   // second floor
            Col(new Vector3((wx0 + ox1) / 2, 3.76f, (wz0 + wz1) / 2), new Vector3(ox1 - wx0, 0.2f, wz1 - wz0 - 0.3f));
            // loading dock on the right end: 4' high concrete dock with a ramp-free face for box trucks
            Solid(o, wx1, wx1 + F(10), 0, 1.22f, wz0 + F(15), wz0 + F(45), conc);
            Office(o, ox1 - F(14), ox1 - 0.15f, wz0 + F(32), wz1 - 0.15f, 2.8f, false);
            PalletRack(o, ox1 + 1f, wz1 - F(3.5f) - 0.3f, 9);                              // 9 bays end ~1.8 m short of the end wall
            PalletRack(o, ox1 + 1f, wz1 - F(3.5f) - 0.3f - F(14), 9);
            PalletRack(o, ox1 + 1f, wz1 - F(3.5f) - 0.3f - F(28), 6);
            Workbench(o, wx1 - F(8) - 0.6f, wz0 + F(28), false);
            YardEquipmentModels.SafetyStation(o, shell, new Vector3(ox1, 0.1f, wz0 + 4.4f), -90f);   // on the office demising wall
            o.Finish(shell, "HQFitOut");
            Sign(shell, Company.Name, new Vector3((wx0 + ox1) / 2, F(21.5f), wz0 - 0.15f), 0.09f);

            _streetParking = new Vector3(yx0 - 2f, 0.03f, -6f);
            _equipInside = new Vector3(wx1 - 4f, 0.1f, wz0 + F(10)); _equipOutside = new Vector3(yx1 - 18f, 0.03f, F(240));
            _sawPose = (new Vector3(wx1 - 0.75f, 0.1f, wz0 + 5.4f), 90f);   // miter bench along the end wall
            _compPose = (new Vector3(wx1 - 0.46f, 0.1f, wz0 + 7.9f), 90f);
            _forkPose = (new Vector3(ox1 + F(12) + 2.5f * F(19), 0.1f, wz0 + 4.0f), 0f);   // between doors 3 and 4
            _liftPose = (new Vector3(ox1 + F(12) + F(19), 0.1f, wz0 + 5.0f), 0f);          // in line with door 2
            PlayerSpawn = W(new Vector3(wx0 + F(18), 0, wz0 - 3f));
            CameraSpot = W(new Vector3(yx1 + 25f, 30f, -30f)); CameraLook = W(new Vector3(-10f, 0f, F(150)));
            InteriorCameraSpot = W(new Vector3(ox1 + F(31), 1.7f, wz0 + 1f)); InteriorCameraLook = W(new Vector3(ox1 + F(40), 2f, wz1 - 2f));
        }

        // ------------------------------------------------------------------ contents

        public void RefreshContents()
        {
            if (_contents != null) Destroy(_contents.gameObject);
            _contents = new GameObject("Contents").transform; _contents.SetParent(transform, false);
            _parked.Clear();
            PlaceStock();
            PlaceVehicles();
            // equipment colliders live under the contents so each refresh replaces them instead of stacking more on the shell
            var shell = _colliders;
            _colliders = new GameObject("EquipColliders");
            _colliders.transform.SetParent(_contents, false);
            try { PlaceEquipment(); }
            finally { _colliders = shell; }
        }

        void PlaceVehicles()
        {
            int street = 0;
            for (int i = 0; i < Company.Vehicles.Count; i++)
            {
                var v = Company.Vehicles[i];
                if (Fleet.IsBusy(v.Uid)) continue;
                if (SkipParked != null && SkipParked(v)) continue;   // e.g. the work van, which is out driving in town
                GameObject go;
                try { go = VehicleModels.Build(v.TypeId, _contents, Fleet.HasRack(v.Uid)); }
                catch (System.Exception e) { Debug.LogWarning($"[Yard] vehicle model {v.TypeId}: {e.Message}"); continue; }
                if (go == null) continue;
                go.name = "Parked " + v.Name;
                if (i < _stalls.Count) { go.transform.localPosition = _stalls[i].pos; go.transform.localRotation = Quaternion.Euler(0, _stalls[i].yaw, 0); }
                else { go.transform.localPosition = _streetParking + new Vector3(street * 11f, 0, 0); go.transform.localRotation = Quaternion.Euler(0, 90, 0); street++; }
                _parked.Add(go);
            }
        }

        void PlaceStock()
        {
            int shelf = 0, longS = 0, bulk = 0;
            var holder = _contents;
            var shelfOrder = ShelfFillOrder();
            var entries = new List<KeyValuePair<string, int>>(Yard.Stock.Entries);
            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            foreach (var kv in entries)
            {
                if (kv.Value <= 0) continue;
                string id = kv.Key;
                int purchases = Mathf.Max(1, kv.Value / Cargo.UnitsPerPurchase(id));
                var def = Catalog.Get(id);
                if (Cargo.IsBulk(id))
                {
                    if (bulk >= _bulkSlots.Count) continue;
                    Mound(holder, _bulkSlots[bulk++], purchases, id);
                    continue;
                }
                bool hasSize = def != null && def.Size.sqrMagnitude > 1e-6f;
                bool isLong = hasSize && !def.IsProduct && def.Size.z > 1.8f;
                if (isLong && longS < _longSlots.Count) { Stack(holder, def, _longSlots[longS++], purchases, true); continue; }
                if (shelf >= shelfOrder.Count) continue;
                var slot = _shelfSlots[shelfOrder[shelf++]];
                if (def != null && def.IsProduct)
                {
                    Stack(holder, def, slot, purchases, false);
                    RackStock.Label(holder, slot.pos, slot.room.z + 0.12f, slot.pos.y > 0.5f, $"{def.Name}  x{purchases}");
                }
                else RackStock.Build(holder, id, def, slot.pos, slot.room, purchases);
            }
        }

        /// <summary>Shelf slots in fill order: the chest-high beam level first (easiest to see and pick), then the
        /// floor level, then the top beams; rack/bay order is kept within a level.</summary>
        List<int> ShelfFillOrder()
        {
            var order = new List<int>();
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < _shelfSlots.Count; i++)
                {
                    float y = _shelfSlots[i].pos.y;
                    int lvl = y < 0.5f ? 1 : y < 2.2f ? 0 : 2;
                    if (lvl == pass) order.Add(i);
                }
            return order;
        }

        static GameObject Visual(PieceDefinition def, Transform parent, Vector3 pos, Quaternion rot)
        {
            var g = new GameObject(def.Name); g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localRotation = rot;
            ProductModels.Build(def, def.Size, g.transform);
            return g;
        }

        /// <summary>Flat stack (lumber/sheets: long axis along the slot's Z) or a row of upright products, capped at 24 visible units.</summary>
        void Stack(Transform parent, PieceDefinition def, (Vector3 pos, Vector3 room) slot, int count, bool longSlot)
        {
            int shown = Mathf.Min(count, 24);
            var s = def.Size;
            if (def.IsProduct)
            {
                // products stand upright side by side along X (rotated to face the aisle)
                int perRow = Mathf.Max(1, Mathf.FloorToInt(slot.room.x / Mathf.Max(0.05f, s.x)));
                if (s.y > slot.room.y + 0.3f) { perRow = Mathf.Max(1, Mathf.FloorToInt(slot.room.x / Mathf.Max(0.05f, s.y))); }
                for (int i = 0; i < Mathf.Min(shown, perRow); i++)
                {
                    bool lay = s.y > slot.room.y + 0.3f;
                    var rot = lay ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;
                    float w = lay ? s.y : s.x, h = lay ? s.x : s.y;
                    float zFront = s.z < slot.room.z ? -slot.room.z / 2 + s.z / 2 + 0.02f : 0f;   // pulled to the aisle side
                    Visual(def, parent, slot.pos + new Vector3(-slot.room.x / 2 + (i + 0.5f) * w, h / 2 + 0.01f, zFront), rot);
                }
                return;
            }
            // lumber / sheet / masonry: lie flat, length along Z (slot room z), width along X
            float len = s.z, wid = s.x, th = s.y;
            Quaternion r = Quaternion.identity;
            float roomLen = longSlot ? slot.room.z : slot.room.z, roomW = slot.room.x;
            if (!longSlot && len > slot.room.z + 0.05f) { r = Quaternion.Euler(0, 90, 0); float t = roomLen; roomLen = roomW; roomW = t; }
            int across = Mathf.Max(1, Mathf.FloorToInt(roomW / Mathf.Max(0.02f, wid)));
            int layers = Mathf.Max(1, Mathf.FloorToInt(slot.room.y / Mathf.Max(0.005f, th)));
            for (int i = 0; i < shown; i++)
            {
                int col = i % across, lay = i / across;
                if (lay >= layers) break;
                float off = -roomW / 2 + (col + 0.5f) * wid;
                Vector3 offset = r == Quaternion.identity ? new Vector3(off, 0, 0) : new Vector3(0, 0, off);
                Visual(def, parent, slot.pos + offset + new Vector3(0, th * (lay + 0.5f) + 0.005f, 0), r);
            }
        }

        static void Pallet(Transform parent, Vector3 pos, SurfaceKind goods, int count)
        {
            var p = new SitePart();
            int wood = p.M(SurfaceKind.PineLumber), bag = p.M(goods);
            float pw = 1.22f, pd = 1.02f;
            for (int i = 0; i < 3; i++) p.Box(-pw / 2, pw / 2, 0, 0.1f, -pd / 2 + i * (pd - 0.1f) / 2, -pd / 2 + i * (pd - 0.1f) / 2 + 0.1f, wood);
            p.Box(-pw / 2, pw / 2, 0.1f, 0.13f, -pd / 2, pd / 2, wood);
            for (int i = 0; i < count; i++)
            {
                int c = i % 4, l = i / 4;
                float x = -pw / 2 + 0.3f + (c % 2) * 0.6f, z = -pd / 4 + (c / 2) * pd / 2;
                p.Box(x - 0.29f, x + 0.29f, 0.13f + l * 0.15f, 0.27f + l * 0.15f, z - 0.24f, z + 0.24f, bag);
            }
            var go = p.Finish(parent, "Pallet");
            if (go != null) go.transform.localPosition = pos;
        }

        static void Mound(Transform parent, Vector3 pos, int yards, string id)
        {
            var p = new SitePart();
            var kind = id.Contains("dirt") || id.Contains("soil") ? SurfaceKind.Dirt : SurfaceKind.Gravel;
            int sub = p.M(kind);
            float vol = Mathf.Min(yards, 30) * 0.7646f;                       // m³
            float r = Mathf.Clamp(Mathf.Pow(vol * 3f / Mathf.PI / 0.6f, 1f / 3f), 0.6f, 2.1f);  // cone at ~31° repose
            p.B.AddFrustum(new Vector3(0, r * 0.3f, 0), Quaternion.identity, r, r * 0.15f, r * 0.6f, 18, sub, true);
            var go = p.Finish(parent, "Bulk " + id);
            if (go != null) go.transform.localPosition = pos;
        }

        void PlaceEquipment()
        {
            var inside = new SitePart(); var outside = new SitePart();
            if (Yard.HasEquipment("forklift")) YardEquipmentModels.Forklift(inside, _forkPose.pos, _forkPose.yaw, Col);
            if (Yard.HasEquipment("sawstation")) YardEquipmentModels.SawStation(inside, _sawPose.pos, _sawPose.yaw, Col);
            if (Yard.HasEquipment("compressor")) YardEquipmentModels.Compressor(inside, _compPose.pos, _compPose.yaw, Col);
            if (Yard.HasEquipment("lift")) YardEquipmentModels.TwoPostLift(inside, _liftPose.pos, _liftPose.yaw, Col);
            inside.Finish(_contents, "ShopEquipment");
            if (Yard.HasEquipment("fueltank")) YardEquipmentModels.FuelTank(outside, _contents, _equipOutside, 0f, Col);
            outside.Finish(_contents, "YardEquipment");
        }
    }
}
