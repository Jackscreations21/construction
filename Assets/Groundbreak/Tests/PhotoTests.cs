using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>For the whole test run, automatic job photos (taken while other suites accept and finish jobs) go to a
    /// throwaway folder under persistentDataPath that is deleted at the end, so tests never leave PNGs behind.</summary>
    [SetUpFixture]
    public class PhotoTestRunFolder
    {
        string _dir;
        [OneTimeSetUp] public void Before() { _dir = Path.Combine(Application.persistentDataPath, "Photos-testrun"); PhotoStore.RootOverride = _dir; }
        [OneTimeTearDown]
        public void After()
        {
            PhotoStore.Clear(false);
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (System.Exception e) { Debug.LogWarning("[PhotoTests] " + e.Message); }
            PhotoStore.RootOverride = null;
        }
    }

    /// <summary>Photo mode (free camera, depth of field, filters, frames, shutter), automatic before/after/progress job photos,
    /// the portfolio bonus and save. Screenshots → Logs/shots/photo_*.png. Each test writes its PNGs into its own folder under
    /// persistentDataPath and deletes it in teardown. Test build site: around x = 760, z = -760.</summary>
    public class PhotoTests
    {
        static readonly Vector3 Site = new Vector3(760f, 0f, -760f);
        string _dir, _prevRoot, _prevIndex;
        int _money, _rep; GameMode _mode; float _hours; int _day;
        Vector3 _pos; float _yaw;
        readonly List<GameObject> _spawned = new List<GameObject>();

        static string ShotDir { get { var d = Path.Combine(Application.dataPath, "..", "Logs", "shots"); Directory.CreateDirectory(d); return d; } }
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(2);
            _prevRoot = PhotoStore.RootOverride; _prevIndex = PhotoStore.SaveJson();
            _dir = Path.Combine(Application.persistentDataPath, "Photos-test-" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            PhotoStore.RootOverride = _dir;
            PhotoStore.LoadJson(null);
            _money = Game.Wallet.Money; _rep = Career.Reputation; _mode = GameSettings.Mode;
            if (GameClock.I != null) { _hours = GameClock.I.Hours; _day = GameClock.I.Day; GameClock.I.Hours = 11f; }
            if (Game.Player != null) { _pos = Game.Player.transform.position; _yaw = Game.Player.transform.eulerAngles.y; }
            GameSettings.Mode = GameMode.Career;
            Company.ResetState();
            Career.Reputation = 50;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (PhotoMode.Active) PhotoMode.I.Exit();
            Time.timeScale = 1f;
            if (Tablet.I != null && Tablet.I.IsOpen) Tablet.I.Close();
            foreach (var g in _spawned) if (g != null) Object.Destroy(g);
            _spawned.Clear();
            RepairJobs.ResetState();
            TownLadder.ResetState();
            WorkVan.Despawn();
            VanCargo.Clear();
            if (Town.Exists) Town.Destroy();
            Company.ResetState();
            yield return Frames(2);
            PhotoStore.Clear(false);
            PhotoThumbs.Clear();
            if (OfficeFrames.I != null) OfficeFrames.I.Refresh();
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (System.Exception e) { Debug.LogWarning("[PhotoTests] cleanup: " + e.Message); }
            Assert.IsFalse(Directory.Exists(_dir), "test photos deleted");
            PhotoStore.RootOverride = _prevRoot;
            PhotoStore.LoadJson(_prevIndex);
            Game.Wallet = new Wallet(_money); Career.Reputation = _rep; GameSettings.Mode = _mode;
            if (GameClock.I != null) { GameClock.I.Hours = _hours; GameClock.I.Day = _day; }
            if (Game.Player != null) HandymanStart.MovePlayer(_pos, _yaw);
            yield return Frames(2);
        }

        // ------------------------------------------------------------------ helpers

        static void CopyShot(PhotoEntry e, string name) => File.Copy(PhotoStore.PathOf(e), Path.Combine(ShotDir, name + ".png"), true);

        /// <summary>Side-by-side strip of the given photos (scaled to 640 wide each) → Logs/shots/name.png.</summary>
        static void Strip(string name, params PhotoEntry[] photos)
        {
            const int w = 640, h = 360, gap = 8;
            var outTex = new Texture2D(photos.Length * w + (photos.Length - 1) * gap, h, TextureFormat.RGB24, false);
            var px = new Color32[outTex.width * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(20, 22, 26, 255);
            outTex.SetPixels32(px);
            for (int k = 0; k < photos.Length; k++)
            {
                var src = PhotoThumbs.Load(photos[k]);
                Assert.NotNull(src, "photo loads back: " + photos[k].File);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        outTex.SetPixel(k * (w + gap) + x, y, src.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h));
                Object.Destroy(src);
            }
            outTex.Apply();
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), outTex.EncodeToPNG());
            Object.Destroy(outTex);
        }

        static float Difference(PhotoEntry a, PhotoEntry b)
        {
            var ta = PhotoThumbs.Load(a); var tb = PhotoThumbs.Load(b);
            float d = 0f; int n = 0;
            for (int y = 8; y < ta.height; y += 16) for (int x = 8; x < ta.width; x += 16) { var c1 = ta.GetPixel(x, y); var c2 = tb.GetPixel(x, y); d += Mathf.Abs(c1.r - c2.r) + Mathf.Abs(c1.g - c2.g) + Mathf.Abs(c1.b - c2.b); n++; }
            Object.Destroy(ta); Object.Destroy(tb);
            return d / n;
        }

        GameObject Box(Transform parent, Vector3 center, Vector3 size, SurfaceKind kind, Quaternion? rot = null)
        {
            var b = new MeshBuilder();
            b.AddBox(Vector3.zero, size, 0);
            var go = new GameObject("PhotoTestBox");
            go.transform.SetParent(parent, false);
            go.transform.position = center; go.transform.rotation = rot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = b.Build("box");
            go.AddComponent<MeshRenderer>().sharedMaterial = Materials.Get(kind);
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>A small fenced yard far from everything: lawn, back fence, survey stakes.</summary>
        Transform BuildYard()
        {
            var root = new GameObject("PhotoTestYard").transform; _spawned.Add(root.gameObject);
            Box(root, Site + new Vector3(0, -0.05f, 0), new Vector3(40f, 0.1f, 40f), SurfaceKind.Grass);
            for (int i = -6; i <= 6; i++) Box(root, Site + new Vector3(i * 2.4f, 0.9f, 9f), new Vector3(0.09f, 1.8f, 0.09f), SurfaceKind.PineLumber);
            Box(root, Site + new Vector3(0, 1.5f, 9.05f), new Vector3(29f, 0.09f, 0.04f), SurfaceKind.PineLumber);
            Box(root, Site + new Vector3(0, 0.4f, 9.05f), new Vector3(29f, 0.09f, 0.04f), SurfaceKind.PineLumber);
            foreach (var c in new[] { new Vector3(-2.4f, 0, -1.8f), new Vector3(2.4f, 0, -1.8f), new Vector3(-2.4f, 0, 1.8f), new Vector3(2.4f, 0, 1.8f) })
                Box(root, Site + c + Vector3.up * 0.3f, new Vector3(0.04f, 0.6f, 0.04f), SurfaceKind.PineLumber);
            return root;
        }

        /// <summary>A 10x12 shed: framing only, or framed + sided + roofed.</summary>
        void BuildShed(Transform root, bool finished)
        {
            float w = 4.88f, d = 3.66f, h = 2.44f;
            Box(root, Site + new Vector3(0, 0.075f, 0), new Vector3(w, 0.15f, d), SurfaceKind.Concrete);
            for (float x = -w / 2 + 0.05f; x <= w / 2; x += 0.406f)
            {
                Box(root, Site + new Vector3(x, 0.15f + h / 2, -d / 2 + 0.045f), new Vector3(0.038f, h, 0.089f), SurfaceKind.PineLumber);
                Box(root, Site + new Vector3(x, 0.15f + h / 2, d / 2 - 0.045f), new Vector3(0.038f, h, 0.089f), SurfaceKind.PineLumber);
            }
            if (!finished) return;
            var mat = SurfaceKind.PaintedWhite;
            Box(root, Site + new Vector3(0, 0.15f + h / 2, -d / 2 - 0.01f), new Vector3(w + 0.04f, h, 0.02f), mat);
            Box(root, Site + new Vector3(0, 0.15f + h / 2, d / 2 + 0.01f), new Vector3(w + 0.04f, h, 0.02f), mat);
            Box(root, Site + new Vector3(-w / 2 - 0.01f, 0.15f + h / 2, 0), new Vector3(0.02f, h, d), mat);
            Box(root, Site + new Vector3(w / 2 + 0.01f, 0.15f + h / 2, 0), new Vector3(0.02f, h, d), mat);
            Box(root, Site + new Vector3(0, 0.15f + 1.03f, -d / 2 - 0.03f), new Vector3(0.92f, 2.03f, 0.04f), SurfaceKind.PaintedSteel);   // door
            float pitch = 26.6f, run = d / 2 + 0.3f, slope = run / Mathf.Cos(pitch * Mathf.Deg2Rad);
            Box(root, Site + new Vector3(0, 0.15f + h + Mathf.Tan(pitch * Mathf.Deg2Rad) * run / 2, -run / 2), new Vector3(w + 0.6f, 0.03f, slope), SurfaceKind.MatteBlack, Quaternion.Euler(-pitch, 0, 0));
            Box(root, Site + new Vector3(0, 0.15f + h + Mathf.Tan(pitch * Mathf.Deg2Rad) * run / 2, run / 2), new Vector3(w + 0.6f, 0.03f, slope), SurfaceKind.MatteBlack, Quaternion.Euler(pitch, 0, 0));
        }

        static Job NewJob(string id, string title, Vector3 where) => new Job
        {
            Id = id, Title = title, Client = "R. Alvarez", Address = "4410 Orchard Ln", Kind = JobKind.Outbuilding,
            Payout = 4000, WorldPos = where, MinStage = CompanyStage.Handyman,
        };

        // ------------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator PhotoMode_FreezesHidesHud_AndSavesPng()
        {
            Assert.NotNull(PhotoMode.I, "photo mode installed");
            Assert.NotNull(PhotoRender.Mat, "photo shader loads (Resources/GroundbreakPhoto)");
            var root = BuildYard();
            BuildShed(root, true);
            HandymanStart.MovePlayer(Site + new Vector3(-5f, 0f, -8f), 25f);
            yield return Frames(3);

            Assert.IsTrue(PhotoMode.I.Enter(), "enter photo mode");
            Assert.IsTrue(PhotoMode.Active);
            Assert.AreEqual(0f, Time.timeScale, "game frozen");
            Assert.IsTrue(Game.UIOpen, "gameplay input + HUD off");
            Assert.IsFalse(PhotoMode.I.Enter(), "already in photo mode");

            // free camera: a low three-quarter view of the shed in late-afternoon light
            var m = PhotoMode.I;
            m.Position = Site + new Vector3(-6.5f, 1.2f, -6.5f);
            var look = Quaternion.LookRotation(Site + new Vector3(0, 1.4f, 0) - m.Position).eulerAngles;
            m.Yaw = look.y; m.Pitch = Mathf.DeltaAngle(0, look.x);
            m.Hours = 17.25f; m.S.Fov = 45f; m.S.Roll = 0f;
            yield return Frames(3);
            Assert.AreEqual(17.25f, GameClock.I.Hours, 0.01f, "time-of-day slider drives the clock");
            Assert.NotNull(m.Viewfinder, "live viewfinder rendered");
            var p1 = m.TakePhoto();
            string path = PhotoStore.PathOf(p1);
            Assert.IsTrue(File.Exists(path), "PNG written");
            Assert.IsTrue(Path.GetFullPath(path).StartsWith(Path.GetFullPath(Application.persistentDataPath)), "under persistentDataPath");
            Assert.IsFalse(Path.GetFullPath(path).StartsWith(Path.GetFullPath(Application.dataPath)), "never inside Assets/");
            var tex = PhotoThumbs.Load(p1);
            Assert.AreEqual(PhotoMode.CaptureWidth, tex.width); Assert.AreEqual(PhotoMode.CaptureHeight, tex.height);
            Object.Destroy(tex);
            CopyShot(p1, "photo_mode_capture");

            // depth of field: focus on a near survey stake, shed far behind; plus black & white + instant-print frame
            m.Position = Site + new Vector3(-3.0f, 0.55f, -3.4f);
            var stake = Site + new Vector3(-2.4f, 0.35f, -1.8f);
            look = Quaternion.LookRotation(stake - m.Position).eulerAngles;
            m.Yaw = look.y + 8f; m.Pitch = Mathf.DeltaAngle(0, look.x) + 2f; m.S.Fov = 30f;
            m.S.Dof = true; m.S.FStop = 1.8f; m.S.Focus = Vector3.Distance(m.Position, stake);
            var p2 = m.TakePhoto();
            CopyShot(p2, "photo_mode_dof");
            m.S.Filter = 1; m.S.Frame = 1; m.S.Roll = -6f;
            var p3 = m.TakePhoto();
            CopyShot(p3, "photo_mode_bw_instant");
            m.S.Filter = 2; m.S.Frame = 2; m.S.Dof = false; m.S.Roll = 0f; m.Hours = 8f; m.S.Fov = 50f;
            m.Position = Site + new Vector3(4.5f, 1.7f, -7f);
            look = Quaternion.LookRotation(Site + Vector3.up * 1.4f - m.Position).eulerAngles; m.Yaw = look.y; m.Pitch = Mathf.DeltaAngle(0, look.x);
            yield return Frames(2);
            var p4 = m.TakePhoto();
            CopyShot(p4, "photo_mode_sepia_mat");
            Assert.AreEqual(4, PhotoStore.Photos.Count);
            Assert.IsTrue(PhotoStore.Photos.TrueForAll(p => p.Kind == "player"));

            // DOF really blurs: compare against the same view without it
            m.S.Filter = 0; m.S.Frame = 0;
            var dofTex = PhotoRender.Capture(Site + new Vector3(-3.0f, 0.55f, -3.4f), Quaternion.Euler(m.Pitch, m.Yaw, 0), new PhotoSettings { Fov = 30f, Dof = true, FStop = 1.4f, Focus = 1.0f, Vignette = 0 }, 320, 180);
            var sharp = PhotoRender.Capture(Site + new Vector3(-3.0f, 0.55f, -3.4f), Quaternion.Euler(m.Pitch, m.Yaw, 0), new PhotoSettings { Fov = 30f, Vignette = 0 }, 320, 180);
            float edgeDof = 0, edgeSharp = 0;
            for (int y = 1; y < 179; y++) for (int x = 1; x < 319; x++)
                {
                    edgeDof += Mathf.Abs(dofTex.GetPixel(x, y).grayscale - dofTex.GetPixel(x + 1, y).grayscale);
                    edgeSharp += Mathf.Abs(sharp.GetPixel(x, y).grayscale - sharp.GetPixel(x + 1, y).grayscale);
                }
            Object.Destroy(dofTex); Object.Destroy(sharp);
            Debug.Log($"[PhotoTests] edge energy sharp {edgeSharp:0} vs depth of field {edgeDof:0}");
            Assert.Less(edgeDof, edgeSharp * 0.9f, "depth of field softens out-of-focus detail");

            PhotoMode.I.Exit();
            Assert.IsFalse(PhotoMode.Active);
            Assert.AreEqual(1f, Time.timeScale, "time runs again");
            Assert.AreEqual(11f, GameClock.I.Hours, 0.01f, "clock back where it was");
            yield return Frames(3);
            Assert.IsFalse(Game.UIOpen, "gameplay input back");
        }

        [UnityTest]
        public IEnumerator RepairJob_BeforeOnArrival_AfterOnInvoice_SameCamera()
        {
            RepairJobs.ResetState();
            HandymanStart.Begin();
            RepairKit.GrantStarterTools();
            Company.Offers.RemoveAll(j => j.Kind == JobKind.Repair);
            yield return Frames(2);
            var lot = new List<TownLot>(Town.Addresses)[0];
            var job = RepairJobs.CreateJob(lot, true, (RepairType.SinkLeak, HouseSpot.KitchenSink, SinkLeakDefect.CrackedTrap));
            Assert.IsTrue(Company.Accept(job, out var msg), msg);
            var album = PhotoStore.AlbumOf(job.Id);
            Assert.NotNull(album, "album opened on accept");
            Assert.IsNull(album.BeforeId, "no photo until the player gets there");
            var d = RepairJobs.DefectsFor(job)[0];

            // in the van at the curb: too far, nothing yet
            HandymanStart.MovePlayer(lot.Parking + lot.Front * 6f, 0f);
            yield return new WaitForSecondsRealtime(JobPhotos.CheckEvery * 2.5f);
            Assert.IsNull(album.BeforeId, "not there yet");

            // walk into the kitchen and face the sink
            var stand = d.transform.TransformPoint(new Vector3(0.1f, 0f, 1.25f));
            var face = Quaternion.LookRotation(Vector3.ProjectOnPlane(d.transform.position - stand, Vector3.up)).eulerAngles.y;
            HandymanStart.MovePlayer(new Vector3(stand.x, d.transform.position.y, stand.z), face);
            yield return new WaitForSecondsRealtime(JobPhotos.CheckEvery * 3f);
            Assert.NotNull(album.BeforeId, "before photo taken on arrival");
            Assert.IsTrue(album.HasPose);
            var camPos = album.CamPos; var camRot = album.CamRot;

            var ctx = RepairContext.Bypass;
            foreach (var a in new[] { "inspect", "bucket", "trap-off", "trap-new", "dry" }) Assert.IsTrue(d.Perform(a, ctx, out var why), a + ": " + why);
            Assert.IsTrue(d.IsFixed);
            yield return Frames(2);
            var inv = RepairJobs.SendInvoice(job);
            Assert.NotNull(inv);

            Assert.IsTrue(album.Done);
            Assert.NotNull(album.AfterId, "after photo taken on completion");
            Assert.AreEqual(camPos, album.CamPos, "after from the same camera");
            Assert.AreEqual(camRot, album.CamRot);
            Assert.AreEqual(inv.paid, album.Pay, "pay = what the customer paid");
            Assert.That(album.Rating, Is.InRange(1f, 5f));
            Assert.AreEqual(album.Rating, Mathf.Round((1f + 4f * inv.satisfaction) * 2f) / 2f, 0.001f, "rating = invoice satisfaction");
            Assert.AreEqual(lot.Address, album.Address);
            var before = PhotoStore.Get(album.BeforeId); var after = PhotoStore.Get(album.AfterId);
            Assert.AreEqual("before", before.Kind); Assert.AreEqual("after", after.Kind);
            Debug.Log($"[PhotoTests] repair before/after difference {Difference(before, after):0.000}");
            CopyShot(before, "photo_repair_before");
            CopyShot(after, "photo_repair_after");
            Strip("photo_before_after_repair", before, after);
        }

        [UnityTest]
        public IEnumerator BuildJob_BeforeProgressAfter_PayRatingAndBonus()
        {
            var root = BuildYard();
            var job = NewJob("photo-test-shed", "Build a 10x12 garden shed", Site);
            Company.Offers.Add(job);
            Assert.IsTrue(Company.Accept(job, out var msg), msg);
            var album = PhotoStore.AlbumOf(job.Id);
            Assert.NotNull(album);
            HandymanStart.MovePlayer(Site + new Vector3(-6f, 0f, -9f), 30f);
            yield return new WaitForSecondsRealtime(JobPhotos.CheckEvery * 3f);
            Assert.NotNull(album.BeforeId, "before photo when the player arrives");

            HandymanStart.MovePlayer(Site + new Vector3(30f, 0f, 30f), 0f);  // walk off: later photos still use the before camera
            BuildShed(root, false);
            yield return Frames(2);
            var prog = JobPhotos.CaptureProgress(album, "Framing done");
            Assert.NotNull(prog);
            Assert.AreEqual(1, album.ProgressIds.Count);
            BuildShed(root, true);
            yield return Frames(2);
            Company.Complete(job, 0.9f);

            Assert.IsTrue(album.Done); Assert.NotNull(album.AfterId);
            Assert.AreEqual(4200, album.Pay, "4000 quoted x 1.05 for 90% quality");
            Assert.AreEqual(4.5f, album.Rating, "90% quality ≈ 4.5 stars");
            Assert.IsTrue(album.Documented);
            var before = PhotoStore.Get(album.BeforeId); var after = PhotoStore.Get(album.AfterId);
            float diff = Difference(before, after);
            Debug.Log($"[PhotoTests] build before/after difference {diff:0.000}");
            Assert.Greater(diff, 0.02f, "the shed shows up in the after photo");
            Strip("photo_before_after_build", before, prog, after);

            // modest portfolio bonus: +1 reputation at 5 documented jobs, once
            int rep = Career.Reputation;
            for (int i = 0; i < 4; i++)
            {
                var a = new JobAlbum { JobId = "photo-fake-" + i, Title = "Fake " + i, Done = true, HasPose = true, CamPos = Site + new Vector3(-6, 1.7f, -9), CamRot = Quaternion.identity };
                var t = new Texture2D(16, 9);
                a.BeforeId = PhotoStore.Add(t, "before", a.JobId).Id; a.AfterId = PhotoStore.Add(t, "after", a.JobId).Id;
                Object.Destroy(t);
                PhotoStore.Albums.Add(a);
            }
            PhotoStore.CheckBonus();
            Assert.AreEqual(rep + 1, Career.Reputation, "+1 reputation at 5 documented jobs");
            PhotoStore.CheckBonus();
            Assert.AreEqual(rep + 1, Career.Reputation, "only once");
            Assert.AreEqual(15, PhotoStore.NextBonusAt);
        }

        [UnityTest]
        public IEnumerator SaveRoundTrip_FavouritesFramesAndDelete()
        {
            var t = new Texture2D(64, 36);
            var mine = PhotoStore.Add(t, "player", null, "Test photo");
            var b = PhotoStore.Add(t, "before", "j1"); var a = PhotoStore.Add(t, "after", "j1");
            Object.Destroy(t);
            PhotoStore.Albums.Add(new JobAlbum { JobId = "j1", Title = "Test job", Address = "1 Test St", Done = true, Pay = 350, Rating = 4f, BeforeId = b.Id, AfterId = a.Id, HasPose = true, CamPos = new Vector3(1, 2, 3) });
            PhotoStore.ToggleFavourite(mine);

            Assert.That(SaveSystem.ExtensionKeys, Has.Member("photos"), "saved through SaveSystem");
            string json = PhotoStore.SaveJson();
            PhotoStore.LoadJson(null);
            Assert.AreEqual(0, PhotoStore.Photos.Count);
            PhotoStore.LoadJson(json);
            Assert.AreEqual(3, PhotoStore.Photos.Count);
            var al = PhotoStore.AlbumOf("j1");
            Assert.NotNull(al); Assert.AreEqual(350, al.Pay); Assert.AreEqual(new Vector3(1, 2, 3), al.CamPos);
            Assert.IsTrue(PhotoStore.Get(mine.Id).Favourite);
            Assert.AreEqual(1, OfficeFrames.Shown().Count, "favourite goes on the office desk");

            var next = PhotoStore.Add(new Texture2D(4, 4), "player");
            Assert.AreNotEqual(mine.Id, next.Id, "ids keep counting after a load");

            string fileA = PhotoStore.PathOf(PhotoStore.Get(a.Id));
            PhotoStore.Delete(PhotoStore.Get(a.Id));
            Assert.IsFalse(File.Exists(fileA), "file deleted");
            Assert.IsNull(al.AfterId, "album forgets the deleted photo");
            PhotoStore.DeleteAlbum(al);
            Assert.IsNull(PhotoStore.AlbumOf("j1"));
            Assert.AreEqual(2, PhotoStore.Photos.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OfficeDesk_ShowsFavouritesInFrames()
        {
            bool made = YardSite.I == null;
            if (made)
            {
                Company.HasWarehouse = true; Company.WarehouseLevel = 1;
                YardSite.Rebuild();
            }
            Assert.NotNull(YardSite.I, "company premises");
            var root = BuildYard(); BuildShed(root, true);
            var s = new PhotoSettings { Fov = 45f };
            var pos = Site + new Vector3(-6.5f, 1.4f, -6.5f);
            var tex = PhotoRender.Capture(pos, Quaternion.LookRotation(Site + Vector3.up * 1.4f - pos), s, 1280, 720);
            var e = PhotoStore.Add(tex, "player", null, "Shed"); Object.Destroy(tex);
            PhotoStore.ToggleFavourite(e);
            OfficeFrames.I.Refresh();
            Assert.NotNull(OfficeFrames.I.Root, "desk frames built");
            Assert.AreEqual(1, OfficeFrames.I.Root.transform.childCount);
            var desk = YardSite.I.OfficeDesk;
            var fwd = YardSite.I.transform.rotation * Vector3.forward;
            var cam = desk + fwd * 0.9f + Vector3.up * 1.25f;
            var lg = new GameObject("PhotoTestLight").AddComponent<Light>(); lg.type = LightType.Point; lg.range = 5f; lg.intensity = 1.5f;
            lg.transform.position = cam + Vector3.up * 0.4f; _spawned.Add(lg.gameObject);
            yield return null;
            SiteLookTests.Shot(cam, desk + Vector3.up * 0.82f - fwd * 0.15f, "photo_office_desk_frames", 55f);
            if (made && YardSite.I != null) Object.Destroy(YardSite.I.gameObject);
        }

        /// <summary>IMGUI screenshots (photo-mode panel, Portfolio app): windowed runs only, see Docs/UI-CAPTURE.md.</summary>
        [UnityTest, Category("UICapture")]
        public IEnumerator Capture_PhotoModeUI_AndPortfolioApp()
        {
            if (Application.isBatchMode) { Assert.Ignore("IMGUI does not render in -batchmode: run windowed, see Docs/UI-CAPTURE.md"); yield break; }
            UICaptureTests.SetGameView(1600, 900);
            if (MainMenu.Showing) MainMenu.I.Hide();
            var root = BuildYard();
            var job = NewJob("photo-ui-shed", "Build a 10x12 garden shed", Site);
            Company.Offers.Add(job);
            Assert.IsTrue(Company.Accept(job, out var msg), msg);
            HandymanStart.MovePlayer(Site + new Vector3(-6f, 0f, -9f), 30f);
            yield return new WaitForSecondsRealtime(JobPhotos.CheckEvery * 3f);
            BuildShed(root, false); yield return null;
            JobPhotos.CaptureProgress(PhotoStore.AlbumOf(job.Id), "Framing done");
            BuildShed(root, true); yield return null;
            Company.Complete(job, 0.9f);

            // photo mode panel
            Assert.IsTrue(PhotoMode.I.Enter());
            PhotoMode.I.S.Dof = true; PhotoMode.I.S.Focus = 9f; PhotoMode.I.Hours = 17.5f;
            yield return new WaitForSecondsRealtime(1.2f);
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(ShotDir, "photo_mode_ui.png"), shot.EncodeToPNG()); Object.Destroy(shot);
            PhotoMode.I.TakePhoto();
            PhotoMode.I.Exit();
            yield return new WaitForSecondsRealtime(0.3f);

            foreach (var (tab, slider, name) in new[] { (0, false, "photo_portfolio_app"), (0, true, "photo_portfolio_slider"), (1, false, "photo_portfolio_album") })
            {
                PortfolioApp.Show(tab, slider);
                Tablet.I.ShowApp(PortfolioApp.Name);
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.AreEqual(PortfolioApp.Name, Tablet.I.CurrentApp);
                yield return new WaitForEndOfFrame();
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), shot.EncodeToPNG()); Object.Destroy(shot);
            }
            Tablet.I.Close();
            yield return Frames(2);
        }
    }
}
