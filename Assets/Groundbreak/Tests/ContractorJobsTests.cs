using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Contractor-stage jobs (deck, fence, shed, garage, driveway, patio, re-roof): generator, vehicle gating,
    /// offer board, and one job end to end (a cedar privacy fence built through the landscaping system).
    /// Screenshots go to Logs/shots/cj_*.png. Test lot: around x = -200, z = 120.</summary>
    public class ContractorJobsTests
    {
        static readonly Vector3 TestLot = new Vector3(-200f, 0f, 120f);
        static readonly string[] Vehicles = { "van", "pickup", "flatbed", "dump", "box", "boom", "skidsteer" };

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1280, 720, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = prev; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        static Camera Cam(Vector3 pos, Vector3 look)
        {
            var cam = new GameObject("ContractorJobsTestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = 60;
            return cam;
        }

        static void Snap(Vector3 pos, Vector3 look, string name)
        {
            var c = Cam(pos, look);
            Shot(c, name);
            Object.Destroy(c.gameObject);
        }

        static void BecomeContractor(string truckType)
        {
            Company.ResetState();
            ContractorJobSite.ClearAll();
            Company.HasWarehouse = true; Company.WarehouseLevel = 1;
            Company.AddVehicle(truckType, "Test " + truckType);
            Company.RecomputeStage();
        }

        int _reputation;

        [SetUp]
        public void Remember() { _reputation = Career.Reputation; }

        [TearDown]
        public void Cleanup()
        {
            ContractorJobSite.ClearAll();
            Company.ResetState();
            Career.Reputation = _reputation;
            GameSettings.Mode = GameMode.FreeBuild;
            Site.Lot = null; Inspections.RuleFilter = null;
        }

        // ------------------------------------------------------------------ generator

        [UnityTest]
        public IEnumerator GeneratorCoversAllTypes()
        {
            yield return Frames(5);
            Company.ResetState();
            Assert.AreEqual(7, ContractorJobs.Types.Length);
            int today = GameClock.I != null ? GameClock.I.Day : 1;
            for (int i = 0; i < ContractorJobs.Types.Length; i++)
            {
                string type = ContractorJobs.Types[i];
                for (int seed = 0; seed < 6; seed++)
                {
                    var j = ContractorJobs.Create(type, 1000 + 37 * i + seed);
                    string tag = type + " seed " + seed;
                    Assert.NotNull(j, tag);
                    Assert.IsTrue(ContractorJobs.IsContractorJob(j), tag);
                    Assert.AreEqual(type, ContractorJobs.TypeOf(j), tag);
                    Assert.AreEqual(type == "shed" || type == "garage" ? JobKind.Outbuilding : JobKind.Remodel, j.Kind, tag);
                    Assert.That(j.Payout, Is.InRange(2000, 50000), tag + " payout");
                    Assert.AreEqual(CompanyStage.Contractor, j.MinStage, tag);
                    Assert.Contains(j.RequiredVehicleType, Vehicles, tag + " vehicle");
                    Assert.Greater(j.DueDay, today, tag + " due day");
                    Assert.IsFalse(string.IsNullOrEmpty(j.Id), tag);
                    Assert.IsFalse(string.IsNullOrEmpty(j.Title), tag);
                    Assert.IsFalse(string.IsNullOrEmpty(j.Client), tag);
                    Assert.IsFalse(string.IsNullOrEmpty(j.Description), tag);
                    Assert.IsFalse(string.IsNullOrEmpty(j.Payload), tag);
                    Assert.IsFalse(j.Accepted || j.Done, tag);

                    var takeoff = ContractorJobs.Takeoff(j);
                    Assert.Greater(takeoff.Count, 3, tag + " takeoff lines");
                    foreach (var kv in takeoff)
                    {
                        Assert.NotNull(Catalog.Get(kv.Key), tag + " takeoff id not in the catalog: " + kv.Key);
                        Assert.Greater(kv.Value, 0, tag + " quantity of " + kv.Key);
                    }
                    int cost = ContractorJobs.TakeoffCost(j);
                    Assert.Greater(cost, 0, tag + " takeoff cost");
                    Assert.AreEqual(cost, j.MaterialBudget, tag + " material budget = takeoff cost");
                    Assert.Less(j.MaterialBudget, j.Payout, tag + " must leave a margin over materials");

                    var lot = ContractorJobs.LotOf(j);
                    Assert.GreaterOrEqual(lot.width, 20f, tag); Assert.GreaterOrEqual(lot.height, 20f, tag);
                    Assert.IsTrue(lot.Contains(new Vector2(j.WorldPos.x, j.WorldPos.z)), tag + " lot contains the job position");

                    // Company saves Job objects with JsonUtility: everything must come back from the payload
                    var copy = JsonUtility.FromJson<Job>(JsonUtility.ToJson(j));
                    Assert.AreEqual(j.Payload, copy.Payload, tag);
                    Assert.AreEqual(type, ContractorJobs.TypeOf(copy), tag);
                    Assert.AreEqual(takeoff.Count, ContractorJobs.Takeoff(copy).Count, tag + " takeoff after a save round trip");
                    Assert.AreEqual(lot, ContractorJobs.LotOf(copy), tag);

                    // nothing is built on a fresh lot
                    ContractorJobs.Relocate(j, TestLot + new Vector3(0f, 0f, 60f), "1 Empty Lot Rd");
                    var check = ContractorJobs.Verify(j);
                    Assert.IsFalse(check.Ok, tag + " verifies with nothing built");
                    Assert.Greater(check.Missing.Count, 0, tag);
                }
            }
            Assert.AreEqual(0, Company.Offers.Count, "Create must not add to the job board");
        }

        // ------------------------------------------------------------------ vehicle gating

        [UnityTest]
        public IEnumerator OfferBlockedUntilTheRightTruckIsOwned()
        {
            yield return Frames(5);
            Company.ResetState();
            var job = ContractorJobs.Create("fence", 77);
            Company.Offers.Add(job);
            Assert.AreEqual("pickup", job.RequiredVehicleType);
            Assert.LessOrEqual(job.MinReputation, Company.Reputation, "test assumes the fence has no reputation gate");
            Assert.IsNotNull(Company.Blocker(job), "a handyman can't take contractor work");

            BecomeContractor("flatbed");
            Company.Offers.Add(job);
            Assert.AreEqual(CompanyStage.Contractor, Company.Stage);
            string blocker = Company.Blocker(job);
            Assert.IsNotNull(blocker, "no pickup owned yet");
            StringAssert.Contains("pickup", blocker);
            Assert.IsFalse(Company.Accept(job, out _));

            Company.AddVehicle("pickup", "Test pickup");
            Assert.IsNull(Company.Blocker(job), "owning the pickup unblocks the fence job");
        }

        // ------------------------------------------------------------------ job board

        [UnityTest]
        public IEnumerator EnsureOffersFillsTheBoard()
        {
            yield return Frames(5);
            Company.ResetState();
            ContractorJobs.EnsureOffers(5);
            Assert.AreEqual(5, Company.Offers.Count);
            var types = new HashSet<string>(); var ids = new HashSet<string>();
            foreach (var o in Company.Offers)
            {
                Assert.IsTrue(ContractorJobs.IsContractorJob(o));
                types.Add(ContractorJobs.TypeOf(o)); ids.Add(o.Id);
            }
            Assert.AreEqual(5, types.Count, "five different job types on the board");
            Assert.AreEqual(5, ids.Count, "unique job ids (one lot each)");

            ContractorJobs.EnsureOffers(5);
            Assert.AreEqual(5, Company.Offers.Count, "already full: nothing added");
            ContractorJobs.EnsureOffers(7);
            Assert.AreEqual(7, Company.Offers.Count);
            types.Clear();
            foreach (var o in Company.Offers) types.Add(ContractorJobs.TypeOf(o));
            Assert.AreEqual(7, types.Count, "all seven types offered");
        }

        // ------------------------------------------------------------------ end to end: cedar privacy fence

        [UnityTest]
        public IEnumerator FenceJob_AcceptBuildVerifyGetPaid()
        {
            yield return Frames(5);
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            GameSettings.Mode = GameMode.Career;
            BecomeContractor("pickup");
            Assert.AreEqual(CompanyStage.Contractor, Company.Stage);
            bool hadField = Grade.Field != null;

            var job = ContractorJobs.Create("fence", 4242);
            ContractorJobs.Relocate(job, TestLot, "214 Test Lot Ln");
            Company.Offers.Add(job);
            var spec = ContractorJobs.SpecOf(job);
            Assert.NotNull(spec);
            Assert.IsTrue(ContractorJobs.LotOf(job).Contains(new Vector2(TestLot.x, TestLot.z)));
            Assert.IsNull(Company.Blocker(job), Company.Blocker(job));
            Assert.IsTrue(Company.Accept(job, out var msg), msg);
            Assert.Contains(job, Company.ActiveJobs);
            Assert.NotNull(ContractorJobSite.RootOf(job), "lot stakes + sign spawned on accept");
            yield return null;

            // nothing built: the client won't pay
            var before = ContractorJobs.Verify(job);
            Assert.IsFalse(before.Ok);
            Assert.Greater(before.Missing.Count, 0);
            int money = Game.Wallet.Money;
            Assert.IsFalse(ContractorJobs.TryComplete(job, out msg), msg);
            Assert.AreEqual(money, Game.Wallet.Money);
            Assert.Contains(job, Company.ActiveJobs);

            // put the takeoff on site (plus a little spare so rounding never starves a step)
            var takeoff = ContractorJobs.Takeoff(job);
            var stockBefore = new Dictionary<string, int>();
            foreach (var id in new[] { LandscapeFeature.PostStock, "postmix-50", "pt2x4x8", "picket-cedar", "gate-kit" })
            {
                Assert.IsTrue(takeoff.ContainsKey(id), "fence takeoff lists " + id);
                stockBefore[id] = Game.Stock.Count(id);
                Game.Stock.Add(id, takeoff[id] * Mathf.Max(1, Catalog.Get(id).UnitsPerPurchase) + 12);
            }

            // build it with the landscaping system, run by run
            var pts = ContractorJobs.FenceLine(job);
            Assert.AreEqual(4, pts.Length);
            var built = new List<LandscapeFeature>();
            for (int i = 0; i < 3; i++)
            {
                var run = Landscape.LayOut(LandscapeKind.Fence, pts[i], pts[i + 1]);
                Assert.NotNull(run, "fence run " + i);
                built.Add(run);
                Assert.Greater(run.CompleteAll(), 0);
                Assert.IsTrue(run.Done, run.Hint);
                if (i == 0)
                {
                    var partial = ContractorJobs.Verify(job);
                    Assert.IsFalse(partial.Ok, "one run of three is not the whole fence");
                }
            }
            var noGate = ContractorJobs.Verify(job);
            Assert.IsFalse(noGate.Ok, "the gate is still missing");
            Assert.AreEqual(1, noGate.Missing.Count, string.Join(" | ", noGate.Missing));

            ContractorJobs.GateLine(job, out var ga, out var gb);
            var gate = Landscape.LayOut(LandscapeKind.Gate, ga, gb);
            Assert.NotNull(gate, "gate");
            built.Add(gate);
            gate.CompleteAll();
            Assert.IsTrue(gate.Done, gate.Hint);
            yield return Frames(3);

            var after = ContractorJobs.Verify(job);
            Assert.IsTrue(after.Ok, "missing: " + string.Join(" | ", after.Missing));
            Assert.AreEqual(0, after.Missing.Count);
            Assert.GreaterOrEqual(after.Done.Count, 3);
            Assert.Greater(after.Quality, 0.8f, "notes: " + string.Join(" | ", after.Notes));
            Assert.IsNull(Site.Lot, "Verify must restore Site.Lot");
            Assert.IsNull(Inspections.RuleFilter, "Verify must restore the rule filter");

            Snap(TestLot + new Vector3(-13f, 9f, -22f), TestLot + new Vector3(0f, 0.8f, 2f), "cj_01_fence_job_done");
            Snap(pts[3] + new Vector3(-4.5f, 1.7f, -5f), pts[3] + new Vector3(-0.5f, 0.9f, 0f), "cj_02_fence_gate_stakes");

            // final payment
            money = Game.Wallet.Money;
            Assert.IsTrue(ContractorJobs.TryComplete(job, out msg), msg);
            int paid = Game.Wallet.Money - money;
            Assert.GreaterOrEqual(paid, Mathf.RoundToInt(job.Payout * 0.6f), "paid at least the base share");
            Assert.LessOrEqual(paid, Mathf.RoundToInt(job.Payout * 1.1f) + 1);
            Assert.IsTrue(job.Done);
            Assert.IsFalse(Company.ActiveJobs.Contains(job), "job left the active list");
            Assert.Contains(job.Id, Company.CompletedJobIds);
            Assert.AreEqual(1, Company.JobsCompleted);
            Assert.IsNull(ContractorJobSite.RootOf(job), "stakes removed when the job is paid");
            Assert.IsFalse(ContractorJobs.TryComplete(job, out _), "can't be paid twice");

            // leave the shared world as we found it
            Game.Wallet.TrySpend(paid, "test cleanup");
            foreach (var kv in stockBefore)
            {
                int extra = Game.Stock.Count(kv.Key) - kv.Value;
                if (extra > 0) Game.Stock.TryTake(kv.Key, extra);
            }
            foreach (var f in built) if (f != null) Object.Destroy(f.gameObject);
            if (!hadField) Grade.Destroy();
            yield return null;
        }
    }
}
