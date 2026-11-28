using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Saves the house shell project (save key "shell", loads first: order -100): the footprint, door/window openings, roof
    /// pitch / stick / hip choice and every stage job the project started (floor deck, walls, top plates, wall sheathing,
    /// opening flashing, roof framing, roof sheathing, garage wing, dry-in, shingles, wing roofing) with how far it got. On load
    /// the project is planned again from the same data and each job is replayed step by step to the saved point
    /// (<see cref="FramingJob.ReplayTo"/>: no stock, nails or weather checks — the work was paid for). That brings back what a
    /// plain piece list loses: the cut rafter / truss / panel shapes, StructuralMember roles and connectors, the wall
    /// assemblies and rough openings, the <see cref="RoofInfo"/> (planes, ridge, hips, valleys, progress flags) and every roofing
    /// layer (drip edge, ice &amp; water, underlayment, chalk lines, starter, shingle courses, ridge vent, caps, valley work).
    /// Windows / doors set in the openings stay plain pieces; their opening is linked to them again and the install job
    /// (shim, level, fasten, tape) replayed. The shell's own pieces are left out of the plain piece list but their data is kept
    /// here: after the replay, pieces the player removed since are taken away again and pieces that moved come back where they were.
    /// </summary>
    public static class ShellSave
    {
        public const string Key = "shell";

        [Serializable] class OpeningD { public int wall; public string product; public float centerIn; }
        [Serializable] class JobD { public string kind; public int next; }
        [Serializable]
        class RoD
        {
            public int wall, index; public bool hasInstalled; public SaveGaps.PieceRef installed = new SaveGaps.PieceRef();
            public int installNext = -1;
        }
        [Serializable]
        class Data
        {
            public int version = 1; public bool has;
            public Footprint fp; public int pitch = 6; public bool stick, hip;
            public List<OpeningD> openings = new List<OpeningD>();
            public List<JobD> jobs = new List<JobD>();
            public List<RoD> ros = new List<RoD>();
            public List<SaveSystem.PieceData> pieces = new List<SaveSystem.PieceData>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension(Key, Save, Load, -100);
            SaveSystem.RegisterPieceFilter("shell", IsShellPiece);
        }

        // ================================================================== which pieces belong to the shell

        static int _rootsFrame = -1; static ShellProject _rootsFor;
        static readonly HashSet<Transform> _roots = new HashSet<Transform>();
        static readonly HashSet<string> _wallNames = new HashSet<string>();

        static void RefreshRoots()
        {
            var p = ShellProject.Current;
            if (_rootsFrame == Time.frameCount && _rootsFor == p) return;
            _rootsFrame = Time.frameCount; _rootsFor = p;
            _roots.Clear(); _wallNames.Clear();
            if (p == null) return;
            foreach (var j in p.Jobs)
            {
                if (j == null) continue;
                _roots.Add(j.transform);
                if (j.transform.parent != null) _roots.Add(j.transform.parent);
            }
            if (p.Walls != null) foreach (var w in p.Walls) if (w != null) { _roots.Add(w.transform); if (w.Spec != null) _wallNames.Add(w.Spec.Name); }
        }

        /// <summary>Is this piece built (and rebuilt on load) by the current shell project's jobs?</summary>
        public static bool IsShellPiece(Piece p)
        {
            if (p == null || ShellProject.Current == null) return false;
            RefreshRoots();
            if (_roots.Count == 0) return false;
            for (var t = p.transform; t != null; t = t.parent) if (_roots.Contains(t)) return true;
            // the wall braces and their deck blocks are nailed to the deck (not parented to the wall)
            var m = p.GetComponent<StructuralMember>();
            if (m != null && ((m.Role == MemberRole.Brace && _wallNames.Contains(m.Group)) || m.Group == "brace block")) return true;
            return false;
        }

        // ================================================================== save

        static string Kind(ShellProject p, FramingJob j)
        {
            if (j == p.Floor) return "floor";
            for (int i = 0; i < 4; i++) if (j == p.WallJobs[i]) return "wall:" + i;
            if (j == p.Caps) return "caps";
            if (j == p.Sheathing) return "sheathing";
            if (j == p.Roof) return "roof";
            if (j == p.RoofSheathing) return "roofsheathing";
            if (j == p.DryIn) return "dryin";
            if (j == p.Shingles) return "shingles";
            if (p.Wing != null && j == p.Wing.FramingJob) return "wing";
            if (j == p.WingRoofing) return "wingroofing";
            var ro = j.transform.parent != null ? j.transform.parent.GetComponent<RoughOpening>() : null;
            if (ro != null && p.Walls != null)
                for (int w = 0; w < p.Walls.Length; w++)
                {
                    int k = p.Walls[w] != null ? p.Walls[w].Openings.IndexOf(ro) : -1;
                    if (k >= 0) return "flash:" + w + ":" + k;
                }
            return null;
        }

        static string Save()
        {
            var d = new Data();
            var p = ShellProject.Current;
            if (p == null) return JsonUtility.ToJson(d);
            if (IsStale(p))
            {
                // a project whose jobs and walls were all destroyed (left over from an earlier session / scene clear): it doesn't
                // belong to this world, so it isn't saved (it used to be planned again on load) and is forgotten
                ShellProject.Current = null; _rootsFrame = -1;
                return JsonUtility.ToJson(d);
            }
            d.has = true; d.fp = p.Fp; d.pitch = p.PitchRise; d.stick = p.StickFrame; d.hip = p.Hip;
            for (int w = 0; w < p.Openings.Length; w++)
                foreach (var o in p.Openings[w])
                    if (o != null && o.Product != null) d.openings.Add(new OpeningD { wall = w, product = o.Product.Id, centerIn = o.CenterIn });
            foreach (var j in p.Jobs)
            {
                if (j == null) continue;
                var k = Kind(p, j);
                if (k == null) { Debug.LogWarning("[Save] shell: job '" + j.Title + "' isn't a known stage, left out"); continue; }
                d.jobs.Add(new JobD { kind = k, next = j.Next });
            }
            if (p.Walls != null)
                for (int w = 0; w < p.Walls.Length; w++)
                {
                    if (p.Walls[w] == null) continue;
                    for (int k = 0; k < p.Walls[w].Openings.Count; k++)
                    {
                        var ro = p.Walls[w].Openings[k];
                        if (ro == null) continue;
                        var rd = new RoD { wall = w, index = k };
                        if (ro.Installed != null) { rd.hasInstalled = true; rd.installed = SaveGaps.Ref(ro.Installed); }
                        foreach (var j in ro.GetComponentsInChildren<FramingJob>(true))
                            if (j.Title.StartsWith("Install")) rd.installNext = j.Next;
                        d.ros.Add(rd);
                    }
                }
            foreach (var q in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.InstanceID))
            {
                if (q == null || q.Def == null || !q.gameObject.activeInHierarchy || !IsShellPiece(q)) continue;
                d.pieces.Add(new SaveSystem.PieceData { id = q.Def.Id, size = q.Size, pos = q.transform.position, rot = q.transform.rotation, state = (int)q.State, nails = q.Nails });
            }
            return JsonUtility.ToJson(d);
        }

        /// <summary>Started jobs / walls that are all gone (destroyed): the project's world was cleared without it.</summary>
        public static bool IsStale(ShellProject p)
        {
            if (p == null) return false;
            int started = 0, alive = 0;
            foreach (var j in p.Jobs) { started++; if (j != null) alive++; }
            if (p.Walls != null) foreach (var w in p.Walls) { started++; if (w != null) alive++; }
            return started > 0 && alive == 0;
        }

        // ================================================================== load

        /// <summary>Removes the current project's objects (jobs, wall assemblies, floor root, roof anchor) and forgets it.</summary>
        public static void Wipe()
        {
            var p = ShellProject.Current;
            ShellProject.Current = null;
            _rootsFrame = -1;
            if (p == null) return;
            var infos = new List<RoofInfo>();
            foreach (var j in p.Jobs) { var ri = j != null ? j.GetComponent<RoofInfo>() : null; if (ri != null) infos.Add(ri); }
            foreach (var a in new List<RoofAnchor>(RoofAnchor.All)) if (a != null && (a.Roof == null || infos.Contains(a.Roof))) SaveGaps.Kill(a.gameObject);
            foreach (var j in p.Jobs)
            {
                if (j == null) continue;
                var parent = j.transform.parent;
                if (parent != null && parent.parent == null) SaveGaps.Kill(parent.gameObject);
                SaveGaps.Kill(j.gameObject);
            }
            if (p.Walls != null) foreach (var w in p.Walls) if (w != null) SaveGaps.Kill(w.gameObject);
        }

        static void Load(string json)
        {
            Wipe();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null || !d.has) return;
            var before = new HashSet<Piece>(UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None));
            var trucks = new HashSet<BoomTruck>(BoomTruck.All);
            var scrap = new List<(string, float)>(Game.Stock.Scrap);   // replayed cuts (door plates) must not add offcuts again
            ShellProject p = null;
            bool instant = FramingJobRunner.Instant, anim = RoofFraming.AnimateBoom;
            FramingJobRunner.Instant = true; RoofFraming.AnimateBoom = false;
            try
            {
                SaveGaps.Quiet(() =>
                {
                    p = ShellProject.Start(d.fp);
                    p.PitchRise = d.pitch; p.StickFrame = d.stick; p.Hip = d.hip;
                    foreach (var o in d.openings)
                    {
                        var def = Catalog.Get(o.product);
                        if (def == null || o.wall < 0 || o.wall >= p.Openings.Length) continue;
                        p.Openings[o.wall].Add(new WallOpening { Product = def, CenterIn = o.centerIn });
                    }
                    foreach (var jd in d.jobs)
                    {
                        try
                        {
                            var j = Start(p, jd.kind);
                            if (j == null) { Debug.LogWarning("[Save] shell: couldn't plan '" + jd.kind + "' again"); continue; }
                            j.ReplayTo(jd.next);
                        }
                        catch (Exception e) { Debug.LogError("[Save] shell stage '" + jd.kind + "' failed to replay: " + e); }
                    }
                });
            }
            finally
            {
                FramingJobRunner.Instant = instant; RoofFraming.AnimateBoom = anim;
                Game.Stock.Scrap.Clear(); Game.Stock.Scrap.AddRange(scrap);
            }
            if (p == null) return;

            // trucks that came and went during the replay (a finished truss job released its boom truck)
            foreach (var t in new List<BoomTruck>(BoomTruck.All))
                if (t != null && !trucks.Contains(t) && p.Roof != null && p.Roof.Done) SaveGaps.Kill(t.gameObject);

            Reconcile(d, before);
            Physics.SyncTransforms();

            // windows / doors set in the openings (plain pieces) and their install jobs
            var pieces = new SaveGaps.PieceIndex();
            foreach (var rd in d.ros)
            {
                try
                {
                    if (p.Walls == null || rd.wall < 0 || rd.wall >= p.Walls.Length || p.Walls[rd.wall] == null) continue;
                    var w = p.Walls[rd.wall];
                    if (rd.index < 0 || rd.index >= w.Openings.Count) continue;
                    var ro = w.Openings[rd.index];
                    if (!rd.hasInstalled) continue;
                    var unit = pieces.Find(rd.installed);
                    if (unit == null) continue;
                    ro.Installed = unit;
                    var m = unit.GetComponent<StructuralMember>();
                    if (m == null) m = unit.gameObject.AddComponent<StructuralMember>();
                    m.Role = MemberRole.Product; m.Connectors = Connector.Flashed;
                    if (rd.installNext >= 0) SaveGaps.Quiet(() => ro.InstallJob().ReplayTo(rd.installNext));
                }
                catch (Exception e) { Debug.LogError("[Save] rough opening failed to restore: " + e); }
            }
            SupportGraph.Dirty = true;
        }

        static FramingJob Start(ShellProject p, string kind)
        {
            if (kind == "floor") return p.StartFloor();
            if (kind.StartsWith("wall:")) return int.TryParse(kind.Substring(5), out int i) && i >= 0 && i < 4 ? p.StartWall(i) : null;
            if (kind == "caps") return p.Walls != null ? p.StartCaps() : null;
            if (kind == "sheathing") return p.Walls != null ? p.StartSheathing() : null;
            if (kind == "roof") return p.StartRoof();
            if (kind == "roofsheathing") return p.Roof != null ? p.StartRoofSheathing() : null;
            if (kind == "dryin") return p.Roof != null ? p.StartDryIn() : null;
            if (kind == "shingles") return p.Roof != null ? p.StartShingles() : null;
            if (kind == "wing")
            {
                var info = p.Roof != null ? p.Roof.GetComponent<RoofInfo>() : null;
                if (info == null) return null;
                p.Wing = CrossGable.Frame(info, p.Fp.Length / 2f, p.WingWidthFt, p.WingProjectionFt);
                p.Jobs.Add(p.Wing.FramingJob);
                return p.Wing.FramingJob;
            }
            if (kind == "wingroofing")
            {
                if (p.Wing == null) return null;
                p.WingRoofing = CrossGable.Roof(p.Wing);
                p.Jobs.Add(p.WingRoofing);
                return p.WingRoofing;
            }
            if (kind.StartsWith("flash:"))
            {
                var parts = kind.Split(':');
                if (parts.Length != 3 || p.Walls == null || !int.TryParse(parts[1], out int w) || !int.TryParse(parts[2], out int k)) return null;
                if (w < 0 || w >= p.Walls.Length || p.Walls[w] == null || k < 0 || k >= p.Walls[w].Openings.Count) return null;
                var j = p.Walls[w].Openings[k].FlashJob();
                p.Jobs.Add(j);
                return j;
            }
            return null;
        }

        /// <summary>Matches the replayed pieces with the saved ones: same definition within 1 cm keeps its saved state and nails;
        /// a replayed piece with no saved match was removed by the player since (taken away again); a saved piece with no replayed
        /// match had been moved (comes back as a plain piece where it was).</summary>
        static void Reconcile(Data d, HashSet<Piece> before)
        {
            var byId = new Dictionary<string, List<int>>();
            for (int i = 0; i < d.pieces.Count; i++)
            {
                if (!byId.TryGetValue(d.pieces[i].id, out var l)) byId[d.pieces[i].id] = l = new List<int>();
                l.Add(i);
            }
            var used = new bool[d.pieces.Count];
            int removed = 0, added = 0;
            foreach (var q in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.InstanceID))
            {
                if (q == null || q.Def == null || before.Contains(q) || !q.gameObject.activeInHierarchy) continue;
                var sm = q.GetComponent<StructuralMember>();
                if (sm != null && !sm.enabled) continue;   // a plate cut during the replay (destroyed at the end of the frame)
                int best = -1; float bd = 0.01f * 0.01f;
                if (byId.TryGetValue(q.Def.Id, out var l))
                    foreach (int i in l)
                    {
                        if (used[i]) continue;
                        float dd = (d.pieces[i].pos - q.transform.position).sqrMagnitude;
                        if (dd < bd) { bd = dd; best = i; }
                    }
                if (best < 0) { SaveGaps.Kill(q.gameObject); removed++; continue; }
                used[best] = true;
                q.State = (PieceState)d.pieces[best].state; q.Nails = d.pieces[best].nails;
            }
            for (int i = 0; i < d.pieces.Count; i++)
            {
                if (used[i]) continue;
                var pd = d.pieces[i];
                var def = Catalog.Get(pd.id);
                if (def == null) continue;
                var q = Piece.Create(def, pd.size.z, pd.pos, pd.rot);
                if ((q.Size - pd.size).sqrMagnitude > 1e-8f) q.SetSize(pd.size);
                q.State = (PieceState)pd.state; q.Nails = pd.nails;
                added++;
            }
            if (removed > 0 || added > 0) Debug.Log($"[Save] shell: {removed} replayed piece(s) had been removed since, {added} moved piece(s) put back as plain pieces");
        }
    }
}
