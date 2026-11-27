using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Automatic job photos. When a job is accepted it gets an album; the first time the player arrives and can see the
    /// work area a "before" photo is taken from where they stand (looking at the work); when the job completes an "after" is
    /// taken from exactly the same camera. House builds start from the empty lot (taken from the street the frame after the
    /// job is accepted) and also get a progress photo as each build phase finishes.</summary>
    public class JobPhotos : MonoBehaviour
    {
        public static JobPhotos I;
        public const int Width = 1280, Height = 720;
        public const float CheckEvery = 0.4f;

        float _next;
        readonly List<string> _pendingDefaultBefore = new List<string>();

        /// <summary>Optional: other features can supply the point to photograph for their jobs.</summary>
        public static Func<Job, Vector3?> ExternalTarget;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            Company.JobAccepted += OnAccepted;
            Company.JobCompleted += OnCompleted;
            Projects.PhaseCompleted += OnPhase;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { ExternalTarget = null; }

        // ------------------------------------------------------------------ events

        static void OnAccepted(Job j)
        {
            if (j == null || string.IsNullOrEmpty(j.Id)) return;
            var a = PhotoStore.AlbumOf(j.Id);
            if (a == null)
            {
                a = new JobAlbum { JobId = j.Id, Kind = (int)j.Kind, AcceptedDay = Today };
                PhotoStore.Albums.Add(a);
            }
            a.Title = j.Title; a.Client = j.Client; a.Address = j.Address;
            if (j.Kind == JobKind.House && I != null) I._pendingDefaultBefore.Add(j.Id);
            PhotoStore.NotifyChanged();
        }

        static void OnCompleted(Job j)
        {
            if (j == null) return;
            var a = PhotoStore.AlbumOf(j.Id);
            if (a == null) return;
            try
            {
                a.Done = true; a.DoneDay = Today;
                a.Pay = PaidFor(j);
                a.Rating = RatingFor(j, a.Pay);
                if (a.Pay <= 0 && string.IsNullOrEmpty(a.BeforeId)) { PhotoStore.Albums.Remove(a); PhotoStore.NotifyChanged(); return; }   // warranty/callback with nothing to show
                if (!a.HasPose) SetPose(a, DefaultPose(j));
                if (a.HasPose) Take(a, "after", "After");
                PhotoStore.CheckBonus();
            }
            catch (Exception e) { Debug.LogException(e); }
            PhotoStore.NotifyChanged();
        }

        static void OnPhase(HouseProject p, BuildPhase phase)
        {
            if (p == null || string.IsNullOrEmpty(p.JobId)) return;
            var a = PhotoStore.AlbumOf(p.JobId);
            if (a == null) return;
            var j = FindJob(p.JobId);
            if (!a.HasPose && j != null) SetPose(a, DefaultPose(j));
            if (a.HasPose) CaptureProgress(a, HousePlan.Label(phase) + " done");
        }

        /// <summary>A progress photo from the album's camera (builds call this at each finished phase).</summary>
        public static PhotoEntry CaptureProgress(JobAlbum a, string caption)
        {
            if (a == null || !a.HasPose) return null;
            var e = Take(a, "progress", caption);
            if (e != null) a.ProgressIds.Add(e.Id);
            PhotoStore.NotifyChanged();
            return e;
        }

        // ------------------------------------------------------------------ arrival

        void Update()
        {
            if (_pendingDefaultBefore.Count > 0)
            {
                foreach (var id in _pendingDefaultBefore.ToArray())
                {
                    var j = FindJob(id); var a = PhotoStore.AlbumOf(id);
                    if (j != null && a != null && string.IsNullOrEmpty(a.BeforeId))
                    {
                        var pose = DefaultPose(j);
                        if (pose.HasValue) { SetPose(a, pose); a.BeforeId = Take(a, "before", "Before")?.Id; }
                    }
                }
                _pendingDefaultBefore.Clear();
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + CheckEvery;
            if (PhotoMode.Active || Game.Player == null || Game.Player.Cam == null) return;
            foreach (var j in Company.ActiveJobs.ToArray()) CheckArrival(j);
        }

        /// <summary>Takes the "before" photo if the player is close enough and can see the work. True when taken.</summary>
        public static bool CheckArrival(Job j)
        {
            var a = PhotoStore.AlbumOf(j.Id);
            if (a == null || !string.IsNullOrEmpty(a.BeforeId) || a.Done) return false;
            var target = TargetOf(j);
            if (!target.HasValue || Game.Player == null || Game.Player.Cam == null) return false;
            Vector3 eye = Game.Player.Cam.transform.position, t = target.Value;
            float d = Vector3.Distance(eye, t);
            if (d > ArrivalRadius(j)) return false;
            if (Physics.Linecast(eye, t, out var hit, ~(1 << PhotoRender.ViewModelLayer), QueryTriggerInteraction.Ignore)
                && Vector3.Distance(hit.point, t) > (IsService(j) ? 1.2f : 3f) && d > 2f) return false;   // a wall in the way: not there yet
            var dir = (t - eye).normalized;
            if (d < 1.6f && !Physics.CheckSphere(t - dir * 1.6f, 0.15f, ~(1 << PhotoRender.ViewModelLayer), QueryTriggerInteraction.Ignore))
                eye = t - dir * 1.6f;                         // step back a little for a close-up
            SetPose(a, (eye, Quaternion.LookRotation(t - eye), IsService(j) ? 70f : 60f, IsService(j)));
            a.BeforeId = Take(a, "before", "Before")?.Id;
            PhotoStore.NotifyChanged();
            return a.BeforeId != null;
        }

        // ------------------------------------------------------------------ where to look

        public static bool IsService(Job j) => RepairJobs.IsService(j);

        public static float ArrivalRadius(Job j) =>
            IsService(j) ? 7f : j.Kind == JobKind.House || j.Kind == JobKind.Development ? 45f : 28f;

        /// <summary>The point to photograph: the repair itself, the contractor job's lot, the house lot, else Job.WorldPos.</summary>
        public static Vector3? TargetOf(Job j)
        {
            if (j == null) return null;
            if (ExternalTarget != null) { var t = ExternalTarget(j); if (t.HasValue) return t; }
            try
            {
                if (IsService(j))
                {
                    var ds = RepairJobs.DefectsFor(j);
                    if (ds.Count > 0)
                    {
                        Vector3 s = Vector3.zero; foreach (var d in ds) s += d.transform.position;
                        return s / ds.Count + Vector3.up * 0.2f;
                    }
                    var lot = RepairJobs.LotOf(j);
                    if (lot != null) return lot.FrontDoor + Vector3.up * 1.5f;
                }
                if (ContractorJobs.IsContractorJob(j)) { var spec = ContractorJobs.SpecOf(j); if (spec != null) return spec.center + Vector3.up * 1f; }
                if (j.Kind == JobKind.House) { var p = Projects.ForJob(j.Id); if (p != null && p.Lot != null) return p.Lot.Center + Vector3.up * 2.5f; }
            }
            catch (Exception e) { Debug.LogWarning("[Photo] target: " + e.Message); }
            if (j.WorldPos != Vector3.zero) return j.WorldPos + Vector3.up * 1f;
            return null;
        }

        /// <summary>A street-side view used when the player never stood at the job (crew work, builds).</summary>
        public static (Vector3 pos, Quaternion rot, float fov, bool flash)? DefaultPose(Job j)
        {
            try
            {
                if (j.Kind == JobKind.House)
                {
                    var p = Projects.ForJob(j.Id);
                    if (p != null && p.Lot != null)
                    {
                        var lot = p.Lot; var right = Vector3.Cross(Vector3.up, lot.Front);
                        var pos = lot.FrontMid + lot.Front * 6f + right * lot.FrontageM * 0.3f + Vector3.up * 1.7f;
                        return (pos, Quaternion.LookRotation(lot.Center + Vector3.up * 3f - pos), 62f, false);
                    }
                }
                if (IsService(j))
                {
                    var lot = RepairJobs.LotOf(j);
                    if (lot != null)
                    {
                        var pos = lot.FrontMid + lot.Front * 2f + Vector3.up * 1.7f;
                        var look = lot.House != null ? lot.House.transform.position + Vector3.up * 2.5f : lot.FrontDoor + Vector3.up * 1.5f;
                        return (pos, Quaternion.LookRotation(look - pos), 60f, false);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Photo] pose: " + e.Message); }
            var t = TargetOf(j);
            if (!t.HasValue) return null;
            var at = t.Value + new Vector3(-7f, 1.2f, -9f);
            return (at, Quaternion.LookRotation(t.Value - at), 60f, false);
        }

        static void SetPose(JobAlbum a, (Vector3 pos, Quaternion rot, float fov, bool flash)? pose)
        {
            if (!pose.HasValue) return;
            a.CamPos = pose.Value.pos; a.CamRot = pose.Value.rot; a.Fov = pose.Value.fov; a.Flash = pose.Value.flash; a.HasPose = true;
        }

        static PhotoEntry Take(JobAlbum a, string kind, string caption)
        {
            if (!a.HasPose) return null;
            try
            {
                var s = new PhotoSettings { Fov = a.Fov, Flash = a.Flash, Vignette = 0.15f };
                var tex = PhotoRender.Capture(a.CamPos, a.CamRot, s, Width, Height);
                var e = PhotoStore.Add(tex, kind, a.JobId, caption);
                Destroy(tex);
                if (kind == "after") a.AfterId = e.Id;
                return e;
            }
            catch (Exception ex) { Debug.LogException(ex); return null; }
        }

        // ------------------------------------------------------------------ pay & rating

        static int Today => GameClock.I != null ? GameClock.I.Day : 0;

        static Job FindJob(string id) =>
            Company.ActiveJobs.Find(x => x.Id == id) ?? Company.Offers.Find(x => x.Id == id);

        /// <summary>What Company.Complete just paid (read back from the wallet log), else the job's payout.</summary>
        static int PaidFor(Job j)
        {
            var log = Game.Wallet.Log;
            string tag = "Paid: " + j.Title;
            for (int i = log.Count - 1; i >= 0 && i >= log.Count - 6; i--)
            {
                var line = log[i];
                if (!line.StartsWith("+$") || !line.EndsWith(tag)) continue;
                int sp = line.IndexOf(' ');
                if (sp > 2 && int.TryParse(line.Substring(2, sp - 2), out int v)) return v;
            }
            return j.Payout;
        }

        /// <summary>Customer rating 0..5 (half stars): the repair invoice's satisfaction, the house's quality, or how much of
        /// the quoted price the customer paid (Company.Complete pays 60%..110% by quality).</summary>
        static float RatingFor(Job j, int paid)
        {
            float q = -1f;
            try
            {
                if (IsService(j)) q = RepairJobs.PreviewInvoice(j).satisfaction;
                else if (j.Kind == JobKind.House) { var p = Projects.ForJob(j.Id); if (p != null) q = p.Quality; }
            }
            catch { q = -1f; }
            if (q < 0f) q = j.Payout > 0 ? Mathf.InverseLerp(0.6f, 1.1f, paid / (float)j.Payout) : 0.8f;
            return Mathf.Round((1f + 4f * Mathf.Clamp01(q)) * 2f) / 2f;
        }
    }
}
