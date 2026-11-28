using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>What a save needs to plan a <see cref="FramingJob"/> again: the builder kind, where it stands and the builder's
    /// numbers. A supporting member the job was hung from is kept as its pose + collider box (the job reads only that when it is
    /// planned), so the job can be planned again even if that member is gone or loads later.</summary>
    [Serializable]
    public class JobRecipeData
    {
        public string kind; public Vector3 pos; public float yaw; public List<float> args = new List<float>();
        public bool hasSupport, supportCol; public Vector3 supportPos, supportColCenter, supportColSize; public Quaternion supportRot = Quaternion.identity;

        public float Arg(int i, float fallback = 0f) => args != null && i < args.Count ? args[i] : fallback;

        /// <summary>A temporary stand-in for the supporting member (same pose and collider). Destroy it after planning.</summary>
        public Transform MakeSupport()
        {
            if (!hasSupport) return null;
            var go = new GameObject("JobSupport (temp)");
            go.transform.SetPositionAndRotation(supportPos, supportRot);
            if (supportCol) { var bc = go.AddComponent<BoxCollider>(); bc.center = supportColCenter; bc.size = supportColSize; bc.enabled = false; }
            return go.transform;
        }
    }

    /// <summary>Left on a framing job's GameObject by builders whose jobs aren't part of the shell project (stair builder stairs
    /// and landing) so <see cref="JobSave"/> can plan and replay the job on load.</summary>
    public class JobRecipe : MonoBehaviour
    {
        public JobRecipeData D = new JobRecipeData();
        public int Seq;
        static int _seq;

        public static JobRecipe Attach(FramingJob job, string kind, Vector3 pos, float yaw, Transform support, params float[] args)
        {
            if (job == null) return null;
            var r = job.gameObject.GetComponent<JobRecipe>();
            if (r == null) r = job.gameObject.AddComponent<JobRecipe>();
            r.Seq = ++_seq;
            r.D = new JobRecipeData { kind = kind, pos = pos, yaw = yaw };
            if (args != null) r.D.args.AddRange(args);
            if (support != null)
            {
                r.D.hasSupport = true; r.D.supportPos = support.position; r.D.supportRot = support.rotation;
                var col = support.GetComponent<BoxCollider>();
                if (col != null) { r.D.supportCol = true; r.D.supportColCenter = col.center; r.D.supportColSize = col.size; }
            }
            return r;
        }
    }

    /// <summary>
    /// Saves framing jobs that aren't one of the shell project's stages (save key "jobs", order -50: after the shell, before the
    /// stairs / exterior / finish sections). Each job a builder tagged with a <see cref="JobRecipe"/> (stair builder stairs and
    /// landings) is planned again from its recipe and replayed to the saved step (<see cref="FramingJob.ReplayTo"/>: no stock,
    /// nails or weather checks), which brings back the cut <see cref="Framer.Custom"/> shapes (sawtooth stringers), the stringers'
    /// "SeatContact" support boxes, hangers / connectors, StructuralMember roles and the landing's <see cref="StairLanding"/>.
    /// Then the replayed pieces are matched with the saved ones like the shell's: removed since = taken away again, moved = back
    /// as a plain piece where it was. The jobs' pieces are left out of the plain piece list.
    /// Other features add their own job kinds with <see cref="RegisterKind"/>.
    /// </summary>
    public static class JobSave
    {
        public const string Key = "jobs";

        [Serializable]
        class JobD
        {
            public JobRecipeData recipe = new JobRecipeData(); public int next;
            public List<SaveSystem.PieceData> pieces = new List<SaveSystem.PieceData>();
        }
        [Serializable] class Data { public int version = 1; public List<JobD> jobs = new List<JobD>(); }

        static readonly Dictionary<string, Func<JobRecipeData, FramingJob>> _kinds = new Dictionary<string, Func<JobRecipeData, FramingJob>>();

        /// <summary>How to plan a job of this kind again from its recipe. Same kind replaces.</summary>
        public static void RegisterKind(string kind, Func<JobRecipeData, FramingJob> plan)
        {
            if (string.IsNullOrEmpty(kind)) throw new ArgumentException("kind required");
            if (plan == null) _kinds.Remove(kind); else _kinds[kind] = plan;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension(Key, Save, Load, -50);
            SaveSystem.RegisterPieceFilter("jobs", IsJobPiece);
            RegisterKind("stairs", r =>
            {
                var sup = r.MakeSupport();
                try { return StairBuilder.Stairs(r.pos, r.yaw, r.Arg(0), r.Arg(1, 0.9144f), sup); }
                finally { if (sup != null) UnityEngine.Object.DestroyImmediate(sup.gameObject); }
            });
            RegisterKind("landing", r => StairBuilder.Landing(r.pos, r.yaw, r.Arg(0), r.Arg(1)));
        }

        /// <summary>Is this piece built (and rebuilt on load) by a saved job?</summary>
        public static bool IsJobPiece(Piece p)
        {
            if (p == null) return false;
            var r = p.GetComponentInParent<JobRecipe>();
            return r != null && r.D != null && r.D.kind != null && _kinds.ContainsKey(r.D.kind) && r.GetComponent<FramingJob>() != null;
        }

        static List<JobRecipe> Recipes()
        {
            var l = new List<JobRecipe>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<JobRecipe>(FindObjectsSortMode.None))
                if (r != null && r.gameObject.activeInHierarchy && r.D != null && r.D.kind != null && _kinds.ContainsKey(r.D.kind) && r.GetComponent<FramingJob>() != null) l.Add(r);
            l.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            return l;
        }

        // ================================================================== save

        static string Save()
        {
            var d = new Data();
            foreach (var r in Recipes())
            {
                var job = r.GetComponent<FramingJob>();
                var jd = new JobD { recipe = r.D, next = job.Next };
                foreach (var q in r.GetComponentsInChildren<Piece>())
                {
                    if (q == null || q.Def == null || !q.gameObject.activeInHierarchy) continue;
                    jd.pieces.Add(new SaveSystem.PieceData { id = q.Def.Id, size = q.Size, pos = q.transform.position, rot = q.transform.rotation, state = (int)q.State, nails = q.Nails });
                }
                d.jobs.Add(jd);
            }
            return JsonUtility.ToJson(d);
        }

        // ================================================================== load

        /// <summary>Removes the session's saved-kind jobs (their pieces were cleared with the rest).</summary>
        public static void Wipe()
        {
            foreach (var r in UnityEngine.Object.FindObjectsByType<JobRecipe>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r != null) SaveGaps.Kill(r.gameObject);
        }

        static void Load(string json)
        {
            Wipe();
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            foreach (var jd in d.jobs)
            {
                try { Rebuild(jd); }
                catch (Exception e) { Debug.LogError("[Save] job '" + jd.recipe?.kind + "' failed to replay: " + e); }
            }
            Physics.SyncTransforms();
            SupportGraph.Dirty = true;
        }

        static void Rebuild(JobD jd)
        {
            if (jd.recipe == null || jd.recipe.kind == null || !_kinds.TryGetValue(jd.recipe.kind, out var plan)) return;
            var before = new HashSet<Piece>(UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None));
            var scrap = new List<(string, float)>(Game.Stock.Scrap);
            FramingJob job = null;
            try
            {
                SaveGaps.Quiet(() =>
                {
                    job = plan(jd.recipe);
                    if (job != null) job.ReplayTo(jd.next);
                });
            }
            finally { Game.Stock.Scrap.Clear(); Game.Stock.Scrap.AddRange(scrap); }
            if (job == null) return;
            Reconcile(jd.pieces, before, "job '" + job.Title + "'");
        }

        /// <summary>Matches pieces made since <paramref name="before"/> with the saved ones (same definition within 1 cm keeps its
        /// saved state and nails); a replayed piece with no saved match was removed since (taken away again); a saved piece with no
        /// replayed match had been moved (comes back as a plain piece where it was).</summary>
        public static void Reconcile(List<SaveSystem.PieceData> saved, HashSet<Piece> before, string what)
        {
            var byId = new Dictionary<string, List<int>>();
            for (int i = 0; i < saved.Count; i++)
            {
                if (!byId.TryGetValue(saved[i].id, out var l)) byId[saved[i].id] = l = new List<int>();
                l.Add(i);
            }
            var used = new bool[saved.Count];
            int removed = 0, added = 0;
            foreach (var q in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.InstanceID))
            {
                if (q == null || q.Def == null || before.Contains(q) || !q.gameObject.activeInHierarchy) continue;
                var sm = q.GetComponent<StructuralMember>();
                if (sm != null && !sm.enabled) continue;
                int best = -1; float bd = 0.01f * 0.01f;
                if (byId.TryGetValue(q.Def.Id, out var l))
                    foreach (int i in l)
                    {
                        if (used[i]) continue;
                        float dd = (saved[i].pos - q.transform.position).sqrMagnitude;
                        if (dd < bd) { bd = dd; best = i; }
                    }
                if (best < 0) { SaveGaps.Kill(q.gameObject); removed++; continue; }
                used[best] = true;
                q.State = (PieceState)saved[best].state; q.Nails = saved[best].nails;
            }
            for (int i = 0; i < saved.Count; i++)
            {
                if (used[i]) continue;
                var pd = saved[i];
                var def = Catalog.Get(pd.id);
                if (def == null) continue;
                var q = Piece.Create(def, pd.size.z, pd.pos, pd.rot);
                if ((q.Size - pd.size).sqrMagnitude > 1e-8f) q.SetSize(pd.size);
                q.State = (PieceState)pd.state; q.Nails = pd.nails;
                added++;
            }
            if (removed > 0 || added > 0) Debug.Log($"[Save] {what}: {removed} replayed piece(s) had been removed since, {added} moved piece(s) put back as plain pieces");
        }
    }
}
