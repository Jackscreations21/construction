using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// World objects for accepted contractor jobs: lot stakes + a job sign so the player can find the work, and the
    /// customer's existing structure where the job needs one (a sheathed, wrapped house wall on a foundation for a deck;
    /// a whole house with a bare roof deck for a re-roof). Everything is rebuilt from <see cref="Job.Payload"/> after a
    /// load, and removed when the job completes (a finished deck keeps its house wall).
    /// </summary>
    public static class ContractorJobSite
    {
        /// <summary>The customer's house wall for a deck job: 24' long, facing -Z, this far behind the lot center.</summary>
        public const float DeckWallLengthIn = 288f, DeckWallOffsetZ = 3f;

        static readonly Dictionary<string, GameObject> _roots = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, RoofInfo> _roofs = new Dictionary<string, RoofInfo>();

        static float In(float inches) => inches * Units.Inch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _roots.Clear(); _roofs.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Company.JobAccepted += OnAccepted;
            Company.JobCompleted += OnCompleted;
            Company.Changed += Prune;
            SaveSystem.BeforeLoad += ClearAll;
            SaveSystem.AfterLoad += RebuildAfterLoad;
        }

        static void OnAccepted(Job j) { if (ContractorJobs.IsContractorJob(j)) Setup(j, false); }
        static void OnCompleted(Job j) { if (ContractorJobs.IsContractorJob(j)) Cleanup(j, true); }

        /// <summary>Root object of an active job's site helpers (null if none).</summary>
        public static GameObject RootOf(Job j)
        {
            if (j == null || j.Id == null) return null;
            return _roots.TryGetValue(j.Id, out var go) && go != null ? go : null;
        }

        /// <summary>The existing roof spawned for a re-roof job (null for other jobs).</summary>
        public static RoofInfo RoofOf(Job j)
        {
            if (j == null || j.Id == null) return null;
            return _roofs.TryGetValue(j.Id, out var ri) && ri != null ? ri : null;
        }

        /// <summary>Outside corner of the deck job's house wall at the top of the foundation (the wall runs +X from here).</summary>
        public static Vector3 DeckWallStart(ContractorJobSpec s) =>
            new Vector3(s.center.x - In(DeckWallLengthIn) * 0.5f, In(s.heightIn - 9.25f), s.center.z + DeckWallOffsetZ);

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Builds the job's stakes/sign and any existing customer structure. Safe to call again (rebuilds).</summary>
        public static void Setup(Job j, bool afterLoad)
        {
            try
            {
                var s = ContractorJobs.SpecOf(j);
                if (s == null) return;
                Cleanup(j, false);
                var root = new GameObject("ContractorJob " + j.Id);
                root.transform.position = s.center;
                _roots[j.Id] = root;
                var markers = new GameObject("Markers").transform;
                markers.SetParent(root.transform, false);
                var structure = new GameObject("Structure").transform;
                structure.SetParent(root.transform, false);
                BuildMarkers(markers, j, s);
                if (s.type == "deck") BuildDeckWall(structure, s, afterLoad);
                else if (s.type == "reroof") BuildReroofHouse(j, structure, s, afterLoad);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Removes the job's helpers. On completion a deck keeps the house wall it hangs on; everything else goes.</summary>
        public static void Cleanup(Job j, bool completed)
        {
            if (j == null || j.Id == null) return;
            if (_roots.TryGetValue(j.Id, out var root) && root != null)
            {
                var s = ContractorJobs.SpecOf(j);
                if (completed && s != null && s.type == "deck")
                {
                    var st = root.transform.Find("Structure");
                    if (st != null) { st.SetParent(null, true); st.name = "Customer house (" + j.Address + ")"; }
                }
                DestroyRoofingJobs(j.Id);
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
            }
            _roots.Remove(j.Id);
            _roofs.Remove(j.Id);
        }

        static void DestroyRoofingJobs(string id)
        {
            if (!_roofs.TryGetValue(id, out var ri) || ri == null) return;
            foreach (var job in new[] { ri.DryInJob, ri.ShinglesJob, ri.SheathingJob })
                if (job != null) { job.gameObject.SetActive(false); UnityEngine.Object.Destroy(job.gameObject); }
        }

        /// <summary>Destroys every job helper (before a load; tests).</summary>
        public static void ClearAll()
        {
            foreach (var id in new List<string>(_roofs.Keys)) DestroyRoofingJobs(id);
            foreach (var kv in _roots)
                if (kv.Value != null) { kv.Value.SetActive(false); UnityEngine.Object.Destroy(kv.Value); }
            _roots.Clear(); _roofs.Clear();
        }

        /// <summary>Drops helpers whose job is no longer active (new game, job list reset).</summary>
        static void Prune()
        {
            if (_roots.Count == 0) return;
            List<string> dead = null;
            foreach (var kv in _roots)
                if (!Company.ActiveJobs.Exists(a => a.Id == kv.Key)) (dead ?? (dead = new List<string>())).Add(kv.Key);
            if (dead == null) return;
            foreach (var id in dead)
            {
                DestroyRoofingJobs(id);
                if (_roots.TryGetValue(id, out var root) && root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
                _roots.Remove(id); _roofs.Remove(id);
            }
        }

        static void RebuildAfterLoad()
        {
            foreach (var j in new List<Job>(Company.ActiveJobs))
                if (ContractorJobs.IsContractorJob(j)) Setup(j, true);
        }

        // ------------------------------------------------------------------ stakes + sign

        static void BuildMarkers(Transform parent, Job j, ContractorJobSpec s)
        {
            var stakes = new List<Vector3>();
            int flagged = -1;
            switch (s.type)
            {
                case "fence":
                {
                    stakes.AddRange(ContractorJobs.FenceLine(j));
                    ContractorJobs.GateLine(j, out _, out var gateEnd);
                    stakes.Add(gateEnd);
                    break;
                }
                case "deck":
                {
                    var R = DeckWallStart(s);
                    float face = R.z - In(0.4375f), w = s.widthFt * Units.Foot, d = s.lengthFt * Units.Foot;
                    stakes.Add(new Vector3(s.center.x - w / 2f, 0f, face - d));
                    stakes.Add(new Vector3(s.center.x + w / 2f, 0f, face - d));
                    break;
                }
                default:
                {
                    var wr = ContractorJobs.WorkRect(s);
                    flagged = 0;
                    stakes.Add(new Vector3(wr.xMin, 0f, wr.yMin));
                    stakes.Add(new Vector3(wr.xMax, 0f, wr.yMin));
                    stakes.Add(new Vector3(wr.xMax, 0f, wr.yMax));
                    stakes.Add(new Vector3(wr.xMin, 0f, wr.yMax));
                    break;
                }
            }
            if (s.type == "reroof") { stakes.Clear(); flagged = -1; } // the house itself is the landmark

            Vector3 origin = parent.position;
            Vector3 sign = new Vector3(0f, 0f, -ContractorJobs.LotSize / 2f + 1.5f);
            Framer.AttachMesh(parent, "StakesAndSign", (MeshBuilder b, Func<Material, int> sub) =>
            {
                int wood = sub(Materials.Get(SurfaceKind.PineLumber));
                int orange = sub(Materials.Get(SurfaceKind.PaintedWhite, new Color(1f, 0.42f, 0.08f)));
                int pink = sub(Materials.Get(SurfaceKind.PaintedWhite, new Color(1f, 0.25f, 0.6f)));
                int white = sub(Materials.Get(SurfaceKind.PaintedWhite));
                for (int i = 0; i < stakes.Count; i++)
                {
                    // 1x2 x 48" lath stake driven 12", with a tail of flagging tape (pink on the corner to start from)
                    Vector3 p = stakes[i] - origin;
                    b.AddBox(p + Vector3.up * In(12f), new Vector3(In(1.5f), In(48f), In(0.75f)), wood);
                    b.AddBox(p + new Vector3(In(3.5f), In(33f), 0f), new Vector3(In(6f), In(1.25f), In(0.05f)), i == flagged ? pink : orange);
                    b.AddBox(p + new Vector3(In(1.2f), In(29.5f), In(0.45f)), new Vector3(In(1.25f), In(7f), In(0.05f)), i == flagged ? pink : orange);
                }
                // job sign at the street side of the lot: 24" x 18" board on two 2x2 x 48" stakes
                b.AddBox(sign + new Vector3(-In(10f), In(14f), 0f), new Vector3(In(1.5f), In(48f), In(1.5f)), wood);
                b.AddBox(sign + new Vector3(In(10f), In(14f), 0f), new Vector3(In(1.5f), In(48f), In(1.5f)), wood);
                b.AddBox(sign + new Vector3(0f, In(28f), -In(1f)), new Vector3(In(24f), In(18f), In(0.5f)), white);
                b.AddBox(sign + new Vector3(0f, In(34.5f), -In(1.3f)), new Vector3(In(24f), In(4f), In(0.1f)), orange);
            });
        }

        // ------------------------------------------------------------------ deck: the customer's house wall

        static Piece Lumber(Transform parent, string id, float lengthM, Vector3 center, Quaternion rot)
        {
            var def = Catalog.Get(id);
            if (def == null) { Debug.LogWarning("[ContractorJobs] missing catalog id " + id); return null; }
            var p = Piece.Create(def, lengthM, center, rot);
            p.Nails = 2;
            p.Fasten();
            p.transform.SetParent(parent, true);
            return p;
        }

        static void RemovePieces(Func<Vector3, bool> inZone)
        {
            foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                if (p.GetComponentInParent<MaterialPile>() != null) continue;
                if (!inZone(p.transform.position)) continue;
                p.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(p.gameObject);
            }
        }

        /// <summary>Real framing so the exterior/deck systems can attach to it: foundation, 2x10 rim band, 2x4 stud wall
        /// 16" O.C. with a double top plate, 7/16" OSB sheathing down over the rim, house wrap.</summary>
        static void BuildDeckWall(Transform parent, ContractorJobSpec s, bool afterLoad)
        {
            Vector3 R = DeckWallStart(s);
            float fy = R.y, L = In(DeckWallLengthIn);
            Vector3 dir = Vector3.right, inward = Vector3.forward;

            if (afterLoad)
            {
                // the save restored this wall as plain pieces: replace them (leaves the player's deck lumber alone)
                float x0 = R.x - 0.1f, x1 = R.x + L + 0.1f, z0 = R.z - In(0.35f), z1 = R.z + In(4f);
                RemovePieces(p => p.x >= x0 && p.x <= x1 && p.z >= z0 && p.z <= z1);
            }

            // foundation wall + a plain house body behind the framed wall (visual only)
            Framer.AttachMesh(parent, "HouseBody", (MeshBuilder b, Func<SurfaceKind, int> sub) =>
            {
                int conc = sub(SurfaceKind.Concrete), body = sub(SurfaceKind.PaintedWhite);
                Vector3 o = parent.position;
                b.AddBox(new Vector3(R.x + L * 0.5f, fy * 0.5f - In(2f), R.z + In(3.875f)) - o, new Vector3(L + In(1f), fy + In(4f), In(8.25f)), conc);
                float top = fy + In(107f);
                b.AddBox(new Vector3(R.x + L * 0.5f, top * 0.5f, R.z + In(3.6f) + 1.5f) - o, new Vector3(L, top, 3f), body);
            });

            // rim band flush with the stud face (the ledger lags land in it)
            var onEdge = Quaternion.LookRotation(dir, Vector3.Cross(dir, Vector3.up));
            Lumber(parent, ExteriorCatalog.PT2x10, L * 0.5f, R + new Vector3(L * 0.25f, In(4.625f), In(0.75f)), onEdge);
            Lumber(parent, ExteriorCatalog.PT2x10, L * 0.5f, R + new Vector3(L * 0.75f, In(4.625f), In(0.75f)), onEdge);

            // stud wall on the subfloor, 10" above the foundation top
            Vector3 O = new Vector3(R.x, fy + In(10f), R.z);
            float t = In(1.5f), wd = In(3.5f);
            Vector3 mid = O + inward * (wd * 0.5f);
            var plateRot = Quaternion.LookRotation(dir, Vector3.up);
            var studRot = Quaternion.LookRotation(Vector3.up, dir);
            Lumber(parent, "pt2x4x8", L, mid + dir * (L * 0.5f) + Vector3.up * (t * 0.5f), plateRot);
            Lumber(parent, "2x4x8", L, mid + dir * (L * 0.5f) + Vector3.up * (In(94.125f) + t * 0.5f), plateRot);
            Lumber(parent, "2x4x8", L, mid + dir * (L * 0.5f) + Vector3.up * (In(95.625f) + t * 0.5f), plateRot);
            float studLen = In(92.625f), studY = t + studLen * 0.5f;
            var layout = new List<float> { 0.75f };
            for (float x = 16f; x < DeckWallLengthIn - 1.5f; x += 16f) layout.Add(x);
            layout.Add(DeckWallLengthIn - 0.75f);
            foreach (float uIn in layout) Lumber(parent, "2x4x925", studLen, mid + dir * In(uIn) + Vector3.up * studY, studRot);

            // OSB from the foundation top over the rim and the wall
            float th = In(0.4375f), hIn = 107.125f;
            var sheetRot = Quaternion.LookRotation(Vector3.up, -inward);
            Piece firstSheet = null;
            for (float x = 0f; x < DeckWallLengthIn - 0.5f; x += 48f)
            {
                var sheet = Lumber(parent, "osb716", In(hIn), R - inward * (th * 0.5f) + dir * In(x + 24f) + Vector3.up * In(hIn * 0.5f), sheetRot);
                if (firstSheet == null) firstSheet = sheet;
            }

            // an existing house is wrapped: lets the Z-flashing lap under the WRB
            if (firstSheet == null) return;
            try
            {
                Physics.SyncTransforms();
                var face = FramePlane.FromPieceFace(firstSheet, -inward);
                var wall = ExteriorWall.For(face);
                if (wall != null)
                {
                    wall.transform.SetParent(parent, true);
                    if (!HouseWrap.Covered(wall))
                    {
                        bool unlimited = FinishStock.Unlimited;
                        FinishStock.Unlimited = true;
                        try { HouseWrap.WrapWall(wall); }
                        finally { FinishStock.Unlimited = unlimited; }
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[ContractorJobs] could not wrap the customer's wall: " + e.Message); }
        }

        // ------------------------------------------------------------------ re-roof: the customer's house

        /// <summary>A house shell (foundation, sided walls, real double top plates) with a trussed, sheathed roof whose old
        /// shingles are already off: the roofing system has no tear-off, so the job starts at the bare deck.</summary>
        static void BuildReroofHouse(Job j, Transform parent, ContractorJobSpec s, bool afterLoad)
        {
            var wr = ContractorJobs.WorkRect(s);
            if (afterLoad)
            {
                // the save restored the roof framing as plain boxes without their roof data: rebuild it properly
                var zone = new Rect(wr.xMin - 0.7f, wr.yMin - 0.7f, wr.width + 1.4f, wr.height + 1.4f);
                RemovePieces(p => zone.Contains(new Vector2(p.x, p.z)));
            }

            var fp = Footprint.Make(new Vector3(wr.xMin, 0f, wr.yMin), 0f, s.lengthFt, s.widthFt);
            var shell = new GameObject("CustomerHouse").transform;
            shell.SetParent(parent, true);
            shell.SetPositionAndRotation(new Vector3(fp.Origin.x, 0f, fp.Origin.z), fp.Rotation);
            float L = fp.Length, W = fp.Width, P = fp.PlateTopY, wd = fp.WallDepth, deck = fp.DeckTopY;
            float wallTop = P - In(3f);
            var walls = Framer.AttachMesh(shell, "Walls", (MeshBuilder b, Func<SurfaceKind, int> sub) =>
            {
                int siding = sub(SurfaceKind.PaintedWhite), conc = sub(SurfaceKind.Concrete);
                b.AddBox(new Vector3(L / 2f, deck / 2f - 0.05f, W / 2f), new Vector3(L + 0.02f, deck + 0.1f, W + 0.02f), conc);
                float h = wallTop - deck, yc = deck + h / 2f, sh = In(0.4375f);
                b.AddBox(new Vector3(L / 2f, yc, wd / 2f - sh / 2f), new Vector3(L, h, wd + sh), siding);
                b.AddBox(new Vector3(L / 2f, yc, W - wd / 2f + sh / 2f), new Vector3(L, h, wd + sh), siding);
                b.AddBox(new Vector3(wd / 2f - sh / 2f, yc, W / 2f), new Vector3(wd + sh, h, W - 2f * wd), siding);
                b.AddBox(new Vector3(L - wd / 2f + sh / 2f, yc, W / 2f), new Vector3(wd + sh, h, W - 2f * wd), siding);
            });
            var mf = walls.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) walls.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

            float t = In(1.5f);
            string longId = RoofStock.Pick("2x4", L).id, endId = RoofStock.Pick("2x4", W - 2f * wd).id;
            for (int k = 0; k < 2; k++)
            {
                float y = k == 0 ? P - t * 1.5f : P - t * 0.5f;
                var role = k == 0 ? MemberRole.TopPlate : MemberRole.CapPlate;
                Framer.Make(longId, L, shell, new Vector3(L / 2f, y, wd / 2f), Vector3.right, Vector3.up, role);
                Framer.Make(longId, L, shell, new Vector3(L / 2f, y, W - wd / 2f), Vector3.right, Vector3.up, role);
                Framer.Make(endId, W - 2f * wd, shell, new Vector3(wd / 2f, y, W / 2f), Vector3.forward, Vector3.up, role);
                Framer.Make(endId, W - 2f * wd, shell, new Vector3(L - wd / 2f, y, W / 2f), Vector3.forward, Vector3.up, role);
            }

            bool requireStock = FramingJob.RequireStock;
            FramingJob.RequireStock = false;   // the existing house is not built out of the player's stock
            try
            {
                var framing = RoofFraming.Trusses(fp, s.pitch, 12f, 12f, false);
                framing.transform.SetParent(parent, true);
                framing.RunAll();
                var info = RoofFraming.InfoOf(framing);
                if (info == null) return;
                _roofs[j.Id] = info;
                var sheathing = RoofFraming.RoofSheathing(info);
                sheathing.transform.SetParent(parent, true);
                sheathing.RunAll();
                // work signed off before a save comes back finished
                if (afterLoad && s.latched.Contains("roof.dryin"))
                {
                    var dry = Roofing.DryIn(info);
                    dry.transform.SetParent(parent, true);
                    dry.RunAll();
                    if (s.latched.Contains("roof.shingles"))
                    {
                        var sg = Roofing.Shingles(info);
                        sg.transform.SetParent(parent, true);
                        sg.RunAll();
                    }
                }
            }
            finally { FramingJob.RequireStock = requireStock; }
        }
    }
}
