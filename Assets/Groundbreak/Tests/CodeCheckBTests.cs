using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Regression tests for the business/world code check (Docs/CODECHECK-B.md). Most are pure logic; the yard test
    /// builds the premises at the HQ fallback spot and takes one screenshot (Logs/shots/codecheck_b_yard.png).</summary>
    public class CodeCheckBTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        int _money;

        [SetUp]
        public void Begin() { _money = Game.Wallet.Money; DevLots.Provider = new FallbackLotProvider(); }

        [TearDown]
        public void End()
        {
            Projects.LeaveSite(); Projects.ResetState(); Developments.ResetState(); DevLots.Provider = null;
            PlanOverlay.Hide(); DevVisuals.RefreshNow();
            Subs.ResetState(); StormEvents.JobIds.Clear();
            Yard.OwnedEquipment.Clear();
            Company.ResetState(); YardFleetRuntime.ResetAll();
            GameSettings.Mode = GameMode.FreeBuild;
            int d = _money - Game.Wallet.Money;
            if (d > 0) Game.Wallet.Add(d, "test restore"); else if (d < 0) Game.Wallet.TrySpend(-d, "test restore");
        }

        static HouseProject SpecProject()
        {
            DevLot lot = null; foreach (var l in DevLots.Provider.FreeLots()) { lot = l; break; }
            Assert.NotNull(lot, "a fallback lot");
            var p = Projects.Create(ProjectKind.Spec, lot, HousePlans.Get("starter-900"));
            p.Client = "Spec";
            return p;
        }

        [Test]
        public void DevSaveKeepsNullIdsNull()
        {
            var p = SpecProject();
            Assert.IsNull(p.JobId); Assert.IsNull(p.WarrantyJobId);
            string json = DevSave.Save();
            DevSave.Load(json);
            var back = Projects.Get(p.Id);
            Assert.NotNull(back);
            Assert.IsNull(back.JobId, "spec house has no job after a load (JsonUtility writes \"\")");
            Assert.IsNull(back.WarrantyJobId, "no warranty call issued yet");
            Assert.IsNull(back.DevId);
        }

        [Test]
        public void FinishedSpecHouseReleasesItsCrew()
        {
            var p = SpecProject();
            var w = Company.Hire(new Worker { Name = "CC Tester", Trade = "carpenter", Skill = 3, AssignedJobId = p.Id });
            foreach (var ph in Inspections.AllPhases) Projects.MarkPassed(p, ph);
            Assert.IsTrue(p.Complete, "all phases passed");
            Assert.IsTrue(string.IsNullOrEmpty(w.AssignedJobId), "crew goes back to the yard when the spec house is done");
        }

        [Test]
        public void SpecCarryTaxesTheLandOnce()
        {
            var p = SpecProject();
            p.CostSpent = 200000;
            int expect = Mathf.RoundToInt(200000 * Developments.PropertyTaxRate / 365f) + Developments.BuildersRiskPerHouseDay;
            Assert.AreEqual(expect, Developments.DailyCarry(p));
        }

        [Test]
        public void StormJobCompletionIsSeen()
        {
            var j = new Job { Id = "cc-storm-1", Title = StormEvents.TitlePrefix + "test", Address = "nowhere", Payout = 100 };
            Company.ActiveJobs.Add(j); StormEvents.JobIds.Add(j.Id);
            Company.Complete(j, 1f);
            Assert.IsFalse(StormEvents.JobIds.Contains(j.Id), "the finished storm job is handled (its damage cleared) on completion");
        }

        [Test]
        public void SecondSubForSameTradeAndSiteIsRefused()
        {
            Subs.ForceOnTime = true;
            string trade = null; SubCompany a = null, b = null;
            foreach (var c in SubCatalog.Companies)
            {
                if (a == null) { a = c; trade = c.Trade; continue; }
                if (c.Trade == trade && c != a) { b = c; break; }
            }
            Assert.NotNull(b, "two companies in one trade");
            SubQuote Q(SubCompany c) => new SubQuote { Company = c, Trade = SubCatalog.Trade(c.Trade), JobId = "cc-site", Qty = 10f, Price = 500, ListPrice = 500 };
            var qa = Q(a); var qb = Q(b);                  // both quoted before either was booked
            Assert.NotNull(Subs.Accept(qa, out var m1), m1);
            Assert.IsNull(Subs.Accept(qb, out var m2), "stale quote can't double-book the trade");
            StringAssert.Contains("already booked", m2);
        }

        [Test]
        public void LedgerCategoriesForEquipment()
        {
            Assert.AreEqual("Tools & equipment", Finance.Categorize("Shop equipment: Fuel tank 500 gal", false));
            Assert.AreEqual("Tools & equipment", Finance.Categorize("Equipment rental", false));
            Assert.AreEqual("Fuel", Finance.Categorize("Fuel: pickup", false));
        }

        [Test]
        public void RemovingTopFloorKeepsTheLowerStair()
        {
            var ed = DesignEditor.Blank(PlanGroup.Residential, 40f, 30f);
            var r = Rect.MinMaxRect(2, 2, 6, 16);
            Assert.IsTrue(ed.AddStair(0, r), ed.Message);
            Assert.IsTrue(ed.AddStair(1, r), ed.Message);
            Assert.AreEqual(3, ed.Design.floors);
            Assert.IsTrue(ed.RemoveTopFloor(), ed.Message);
            Assert.IsTrue(ed.Design.rooms.Exists(x => x.floor == 1 && x.name == "Stair" && x.Ft == r), "the 1->2 stair still arrives on floor 2");
        }

        [Test]
        public void CarvedLeftoversGetUniqueNames()
        {
            var ed = DesignEditor.Blank(PlanGroup.Residential, 40f, 30f);
            Assert.IsTrue(ed.AddRoom(0, Rect.MinMaxRect(0, 0, 20, 30), "Bedroom"), ed.Message);
            Assert.IsTrue(ed.AddRoom(0, Rect.MinMaxRect(8, 10, 12, 20), "Bath"), ed.Message);   // a box in the middle of the bedroom
            var seen = new HashSet<string>();
            foreach (var x in ed.Design.rooms)
            {
                if (x.floor != 0 || Blueprint.IsOpen(Blueprint.KindOf(x.name))) continue;
                Assert.IsTrue(seen.Add(x.name), "duplicate closed-room name " + x.name);
            }
        }

        [UnityTest]
        public IEnumerator LandModelMeshIsFreedWithItsObject()
        {
            var k = new LandModel(); int s = k.M(SurfaceKind.PaintedWhite);
            k.B.AddBox(Vector3.zero, Vector3.one, s);
            var go = k.Make(null, "CC_OwnedMeshTest");
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(mesh != null);
            Object.Destroy(go);
            yield return Frames(2);
            Assert.IsTrue(mesh == null, "the per-object mesh is destroyed with the GameObject");
        }

        [UnityTest]
        public IEnumerator YardRefreshDoesNotStackEquipmentColliders()
        {
            GameSettings.Mode = GameMode.FreeBuild;
            Company.HasWarehouse = true; Company.WarehouseLevel = 2;
            foreach (var id in new[] { "forklift", "sawstation", "compressor", "lift", "fueltank" }) Yard.OwnedEquipment.Add(id);
            var s = YardSite.Rebuild();
            yield return Frames(3);
            s = YardSite.I; Assert.NotNull(s);
            int Count() { int n = 0; foreach (var c in s.GetComponentsInChildren<BoxCollider>(true)) if (c != null) n++; return n; }
            yield return null;
            int before = Count();
            for (int i = 0; i < 3; i++) { s.RefreshContents(); yield return null; }
            yield return Frames(2);
            Assert.AreEqual(before, Count(), "refreshing the contents replaces the equipment colliders instead of adding more");
            Shot(s.InteriorCameraSpot, s.InteriorCameraLook, "codecheck_b_yard");
        }

        static void Shot(Vector3 pos, Vector3 look, string name)
        {
            var cam = new GameObject("CCBCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = 70f; cam.farClipPlane = 1500f;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt); Object.Destroy(cam.gameObject);
        }
    }
}
