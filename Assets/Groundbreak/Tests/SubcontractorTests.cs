using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Subcontractors: catalog + real pricing above own-crew labor, the Contractor-stage gate, a premium framing sub
    /// building a wall through the crew machinery (truck, temporary crew, invoice, repeat discount, no payroll), a cheap sub's
    /// defects failing inspection and a free callback fixing them, late arrivals and the save round-trip.
    /// Test area around (-1600, 0, 1500).</summary>
    public class SubcontractorTests
    {
        static readonly Vector3 Area = new Vector3(-1600f, 0f, 1500f);
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        readonly List<GameObject> _made = new List<GameObject>();
        static string ShotDir => Path.Combine(Application.dataPath, "..", "Logs", "shots");

        static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
            cam.targetTexture = prev; RenderTexture.active = null;
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        Camera Cam(Vector3 pos, Vector3 look)
        {
            var cam = new GameObject("SubTestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.enabled = false; cam.fieldOfView = 55; cam.nearClipPlane = 0.03f;
            _made.Add(cam.gameObject);
            return cam;
        }

        int _money;

        void Setup(Vector3 at, bool contractor = true)
        {
            Company.ResetState(); Crew.ResetState(); Subs.ResetState(); CrewDefect.Log.Clear();
            GameSettings.Mode = GameMode.Career;
            Crew.IgnoreWorkHours = true; Crew.SpeedMultiplier = 10f;
            Subs.IgnoreWorkHours = true; Subs.ForceOnTime = true;
            CrewManager.ArrivalOverride = at + new Vector3(-4f, 0, -5f);
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
            Weather.ForceRain = false;
            Site.Lot = new Rect(at.x - 8f, at.z - 8f, 16f, 16f);
            if (Game.Player != null) Game.Player.transform.position = at + new Vector3(-12f, 0, -12f);
            if (contractor) { Company.HasWarehouse = true; Company.WarehouseLevel = 1; Company.AddVehicle("pickup", "F-250"); }
            if (Game.Wallet.Money < 50000) Game.Wallet.Add(50000 - Game.Wallet.Money, "test funds");
            _money = Game.Wallet.Money;
            FramingJob.RequireStock = false; FramingJobRunner.Instant = true;
        }

        FramingJob Wall(Vector3 at, string name)
        {
            var spec = new WallSpec { Name = name, LengthIn = 120f };
            spec.Openings.Add(new WallOpening { Product = Catalog.Get("win-sh-3648"), CenterIn = 60f });
            var wall = WallPlan.Create(spec, at, Quaternion.identity);
            _made.Add(wall.gameObject);
            return WallPlan.Frame(wall);
        }

        [TearDown]
        public void Teardown()
        {
            Subs.ResetState(); Crew.ResetState(); Company.ResetState(); CrewDefect.Log.Clear();
            Subs.IgnoreWorkHours = false; Subs.ForceOnTime = null;
            Crew.IgnoreWorkHours = false; Crew.SpeedMultiplier = 1f; CrewManager.ArrivalOverride = null;
            GameSettings.Mode = GameMode.FreeBuild; Weather.ForceRain = null; Site.Lot = null; Inspections.RuleFilter = null;
            FramingJob.RequireStock = true; FramingJobRunner.Instant = false;
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                if (p != null && (p.transform.position - Area).magnitude < 60f) Object.Destroy(p.gameObject);
            foreach (var g in _made) if (g != null) Object.Destroy(g);
            _made.Clear();
        }

        [Test]
        public void CatalogHasEveryTradeWithRealPricesAboveOwnCrew()
        {
            string[] trades = { "excavation", "concrete", "framing", "roofing", "plumbing", "electrical", "hvac", "insulation", "drywall", "painting", "flooring", "landscaping" };
            Assert.AreEqual(trades.Length, SubCatalog.Trades.Count);
            foreach (var id in trades)
            {
                var t = SubCatalog.Trade(id);
                Assert.NotNull(t, id);
                var cos = new List<SubCompany>(SubCatalog.For(id));
                Assert.GreaterOrEqual(cos.Count, 3, id + " has local companies");
                float crewPerUnit = CrewTrades.Wage(t.CrewTrade, 3) / t.CrewPerDay;
                foreach (var c in cos)
                {
                    Assert.Greater(t.Rate * c.PriceFactor, crewPerUnit * 1.4f, $"{c.Name} costs clearly more than your own crew's wages per {t.Unit}");
                    Assert.That(c.Quality, Is.InRange(1, 5)); Assert.That(c.Reliability, Is.InRange(0.3f, 1f)); Assert.That(c.LeadDays, Is.InRange(1, 10));
                    Assert.IsFalse(string.IsNullOrEmpty(c.Name));
                }
                // cheap = sloppy and late, premium = good and booked out
                cos.Sort((a, b) => a.PriceFactor.CompareTo(b.PriceFactor));
                Assert.Less(cos[0].Quality, cos[cos.Count - 1].Quality);
                Assert.Less(cos[0].Reliability, cos[cos.Count - 1].Reliability);
                Assert.Less(cos[0].LeadDays, cos[cos.Count - 1].LeadDays);
            }
            // real US ballparks
            Assert.That(SubCatalog.Trade("roofing").Rate, Is.InRange(120f, 300f), "$/square");
            Assert.That(SubCatalog.Trade("drywall").Rate, Is.InRange(1.2f, 3.5f), "$/sq ft hang + finish");
            Assert.That(SubCatalog.Trade("framing").Rate, Is.InRange(5f, 16f), "$/sq ft");
        }

        [UnityTest]
        public IEnumerator HandymanCantHireSubs()
        {
            yield return Frames(3);
            var at = Area + new Vector3(30f, 0, 0);
            Setup(at, contractor: false);
            Wall(at, "Sub gate wall");
            var q = Subs.Quote(SubCatalog.Get("framing-2"), Crew.SiteJobId, at, 8f);
            Assert.NotNull(q.Blocker, "a handyman can't hire subs");
            Assert.IsNull(Subs.Accept(q, out _));
            Company.HasWarehouse = true; Company.WarehouseLevel = 1; Company.AddVehicle("pickup", "F-250");
            Assert.AreEqual(CompanyStage.Contractor, Company.Stage);
            q = Subs.Quote(SubCatalog.Get("framing-2"), Crew.SiteJobId, at, 8f);
            Assert.IsNull(q.Blocker, q.Blocker);
            Assert.Greater(q.Qty, 0f);
            Assert.GreaterOrEqual(q.Price, SubCatalog.Trade("framing").Minimum);
            Assert.Greater(q.Price, q.CrewCost, "the sub costs more than doing it with your own crew");
        }

        [UnityTest]
        public IEnumerator PremiumFramingSubBuildsWallAndInvoices()
        {
            yield return Frames(5);
            var at = Area;
            Setup(at);
            var job = Wall(at, "Sub framing wall");
            var co = SubCatalog.Get("framing-3");
            Assert.AreEqual(5, co.Quality);
            var q = Subs.Quote(co, Crew.SiteJobId, at, 8f);
            Assert.IsNull(q.Blocker, q.Blocker);
            Assert.Greater(q.Qty, 20f, q.Detail);
            var k = Subs.Accept(q, out var msg);
            Assert.NotNull(k, msg);
            Assert.AreEqual(_money, Game.Wallet.Money, "nothing paid up front");

            var cam = Cam(at + new Vector3(6.5f, 2.6f, -6.5f), at + new Vector3(-0.5f, 0.6f, -1.5f));
            bool shot = false;
            for (float t = 0; t < 120f && k.State != SubState.Done; t += Time.deltaTime)
            {
                if (!shot && job.Next > job.Steps.Count / 2)
                {
                    var npcs = SubCrews.NpcsOf(k);
                    if (npcs.Count > 0 && System.Linq.Enumerable.Any(npcs, n => n != null && n.WorkElapsed > 0.05f))
                    {
                        Assert.NotNull(SubCrews.TruckOf(k), "the sub came in their own truck");
                        Assert.AreEqual(0, Company.Workers.Count, "sub workers are not your employees");
                        Assert.AreEqual(0, Crew.Payroll(), "and not on payroll");
                        Shot(cam, "subs_crew_working"); shot = true;
                    }
                }
                yield return null;
            }
            Assert.IsTrue(shot, "screenshot of the sub crew at work");
            Assert.AreEqual(SubState.Done, k.State, $"sub finished ({job.Next}/{job.Steps.Count}) {k.Status}");
            Assert.IsTrue(job.Done);
            Assert.IsTrue(k.Paid);
            Assert.AreEqual(_money - k.Price, Game.Wallet.Money, "invoice paid from the wallet on completion");
            Assert.IsTrue(Game.Wallet.Log.Exists(l => l.Contains("Sub invoice")));
            Assert.IsNull(SubCrews.TruckOf(k), "truck left");
            Assert.Greater(SubCrews.Departures, 0, "the crew walks back to their truck before it drives off");
            for (float t = 0; t < SubCrews.WalkBackTimeout + 5f && SubCrews.Departures > 0; t += Time.deltaTime) yield return null;
            Assert.AreEqual(0, SubCrews.Departures, "truck gone once the crew is aboard");
            yield return Frames(10);
            Shot(Cam(at + new Vector3(5.2f, 2.1f, -4.4f), at + new Vector3(1.5f, 1.1f, 0)), "subs_finished_wall");

            // repeat business: 2% off the next job with them
            Assert.AreEqual(1, Subs.JobsWith[co.Id]);
            Wall(at + new Vector3(0, 0, 6f), "Sub framing wall 2");
            var q2 = Subs.Quote(co, Crew.SiteJobId, at, 12f);
            Assert.Greater(q2.Discount, 0, "repeat-customer discount");
            Assert.AreEqual(Mathf.RoundToInt(q2.ListPrice * 0.98f / 5f) * 5, q2.Price);
        }

        [UnityTest]
        public IEnumerator CheapSubDefectsFailInspectionAndCallbackFixesThem()
        {
            yield return Frames(5);
            var at = Area + new Vector3(0, 0, 30f);
            Setup(at);
            var job = Wall(at, "Cheap sub wall");
            var co = SubCatalog.Get("framing-1");
            Assert.AreEqual(1, co.Quality);
            var k = Subs.Accept(Subs.Quote(co, Crew.SiteJobId, at, 8f), out var msg);
            Assert.NotNull(k, msg);
            for (float t = 0; t < 150f && k.State != SubState.Done; t += Time.deltaTime) yield return null;
            Assert.AreEqual(SubState.Done, k.State, k.Status);
            Assert.Greater(k.Mistakes, 0, "a poor-quality sub leaves workmanship defects");
            int paid = Game.Wallet.Money;

            Inspections.RuleFilter = r => r.Id == "site.loose-members";
            var res = Inspections.Evaluate(InspectionPhase.Framing);
            Assert.IsFalse(res.Passed, "inspection catches the sub's missed nails");

            Assert.IsTrue(Subs.RequestCallback(k, out msg), msg);
            Assert.AreEqual(SubState.Callback, k.State);
            for (float t = 0; t < 90f && k.State != SubState.Done; t += Time.deltaTime) yield return null;
            Assert.AreEqual(SubState.Done, k.State, k.Status);
            Assert.IsTrue(Inspections.Evaluate(InspectionPhase.Framing).Passed, "fixed on the callback");
            Assert.AreEqual(paid, Game.Wallet.Money, "warranty work is free");
            Assert.AreEqual(1, Subs.JobsWith[co.Id], "a callback isn't a new job");
        }

        [UnityTest]
        public IEnumerator LateSubAndSaveRoundTrip()
        {
            yield return Frames(3);
            var at = Area + new Vector3(30f, 0, 30f);
            Setup(at);
            Subs.IgnoreWorkHours = false; Subs.ForceOnTime = false;
            if (GameClock.I != null) GameClock.I.Hours = 20f;      // after hours: nobody shows up during the test
            Wall(at, "Late sub wall");
            var co = SubCatalog.Get("framing-1");
            var k = Subs.Accept(Subs.Quote(co, Crew.SiteJobId, at, 8f), out var msg);
            Assert.NotNull(k, msg);
            int day = GameClock.I != null ? GameClock.I.Day : 1;
            Assert.AreEqual(day + co.LeadDays, k.PromisedDay);
            Assert.Greater(k.StartDay, k.PromisedDay, "an unreliable sub shows up late");
            Subs.JobsWith["roofing-2"] = 3;

            var save = typeof(Subs).GetMethod("Save", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var load = typeof(Subs).GetMethod("Load", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            string json = (string)save.Invoke(null, null);
            Subs.ResetState();
            Assert.AreEqual(0, Subs.Contracts.Count);
            load.Invoke(null, new object[] { json });
            Assert.AreEqual(1, Subs.Contracts.Count);
            var k2 = Subs.Contracts[0];
            Assert.AreEqual(k.Id, k2.Id); Assert.AreEqual(k.Price, k2.Price); Assert.AreEqual(k.StartDay, k2.StartDay);
            Assert.AreEqual(SubState.Scheduled, k2.State); Assert.AreEqual("framing", k2.Trade);
            Assert.AreEqual(3, Subs.JobsWith["roofing-2"]);
            Assert.AreEqual(0.06f, Subs.DiscountWith("roofing-2"), 1e-4f);
            Assert.IsTrue(SaveSystem.ExtensionKeys is IEnumerable<string> keys && System.Linq.Enumerable.Contains(keys, "subs"));
            Subs.Cancel(k2);
            Assert.AreEqual(0, Subs.Contracts.Count);
        }

        /// <summary>The tablet app (IMGUI: only in a windowed run, see Docs/UI-CAPTURE.md).</summary>
        [UnityTest]
        public IEnumerator SubcontractorsAppShot()
        {
            if (Application.isBatchMode) { Assert.Ignore("IMGUI does not render in -batchmode: run windowed, see Docs/UI-CAPTURE.md"); yield break; }
            yield return Frames(5);
            var at = Area + new Vector3(0, 0, -30f);
            Setup(at);
            Wall(at, "App wall");
            if (Game.Player != null) Game.Player.transform.position = at + new Vector3(-3f, 0, -3f);
            Subs.JobsWith["framing-2"] = 2;
            if (MainMenu.Showing) MainMenu.I.Hide();
            SubsApp.Show("framing");
            Tablet.I.ShowApp(SubsApp.AppName);
            yield return Frames(5);
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(Path.Combine(ShotDir, "subs_app.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            Tablet.I.Close();
        }
    }
}
