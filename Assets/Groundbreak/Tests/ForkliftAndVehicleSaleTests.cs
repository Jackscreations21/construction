using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>The yard forklift's business effect (hand-handling labor, lift pricing, faster warehouse runs) and the lock
    /// that keeps a vehicle an accepted job needs from being sold. Screenshot → Logs/shots/forklift_yard.png.</summary>
    public class ForkliftAndVehicleSaleTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        int _money, _rep;

        [SetUp]
        public void Begin()
        {
            _money = Game.Wallet.Money; _rep = Career.Reputation;
            GameSettings.Mode = GameMode.Career; GameSettings.UnlimitedMoney = false;
            Company.ResetState(); YardFleetRuntime.ResetAll();
            Fleet.TripSeconds = 0f;
            if (GameClock.I != null) GameClock.I.Hours = 10.5f;
        }

        [TearDown]
        public void End()
        {
            Company.ResetState(); YardFleetRuntime.ResetAll();
            Fleet.TripSeconds = 45f;
            GameSettings.Mode = GameMode.FreeBuild;
            Career.Reputation = _rep;
            foreach (var id in new[] { "2x6x12", "osb716" }) if (Game.Stock.Count(id) > 0) Game.Stock.TryTake(id, Game.Stock.Count(id));
            int d = _money - Game.Wallet.Money;
            if (d > 0) Game.Wallet.Add(d, "test restore"); else if (d < 0) Game.Wallet.TrySpend(-d, "test restore");
            SaveSystem.Delete("test_forklift");
            var board = GameObject.Find("ForkliftTestBoard"); if (board != null) Object.Destroy(board);
        }

        static void Cash(int amount) { if (Game.Wallet.Money < amount) Game.Wallet.Add(amount - Game.Wallet.Money, "test cash"); }

        // ~4,600 lb: a flatbed load of a 2x6 lift and a unit of OSB (well over what a driver hand-unloads)
        static Dictionary<string, int> HeavyLoad() => new Dictionary<string, int> { { "2x6x12", 100 }, { "osb716", 60 } };
        static int Fuel(string type) => Mathf.CeilToInt(Fleet.SupplierRoundTripMiles / FleetCatalog.Get(type).Mpg * Fleet.FuelPrice);

        static Job NeedsJob(string title, string vehicle)
        {
            var j = new Job { Id = "fk-" + title.GetHashCode(), Title = title, Client = "Test client", Kind = JobKind.Outbuilding, Payout = 9000,
                              MinStage = CompanyStage.Contractor, RequiredVehicleType = vehicle, Accepted = true };
            Company.ActiveJobs.Add(j);
            return j;
        }

        // ------------------------------------------------------------------ forklift

        [UnityTest]
        public IEnumerator Forklift_RemovesHandling_FasterRuns_LiftPricing()
        {
            yield return Frames(3);
            Cash(200000);
            Assert.IsTrue(Yard.Lease(1, out var msg), msg);
            var flat = Fleet.Buy("flatbed", true, 0, out msg); Assert.NotNull(flat, msg);
            var cart = HeavyLoad();
            Assert.Greater(ForkliftRules.HandledLb(cart), 4000f);

            // without a forklift: the crew hand-unloads everything over the driver's ton
            Assert.IsFalse(ForkliftRules.Has);
            int fee = ForkliftRules.HandlingFee(cart);
            float hours = ForkliftRules.HandHours(cart, false);
            Assert.That(hours, Is.InRange(1.5f, 2.5f), "about 1.5 labor-hours per ton over 2,000 lb");
            Assert.That(fee, Is.InRange(45, 75));
            Fleet.TripSeconds = 40f;
            int before = Game.Wallet.Money, mat = Fleet.MaterialCost(cart, true);
            Assert.IsTrue(Fleet.Haul(flat, cart, true, out msg), msg);
            StringAssert.Contains("hand unloading", msg);
            Assert.AreEqual(mat + Fuel("flatbed") + fee, before - Game.Wallet.Money, "materials + fuel + hand unloading");
            Assert.AreEqual(40f, Fleet.Trips[0].secondsLeft, 0.01f, "full run time without a forklift");
            Fleet.Tick(100f);
            Assert.AreEqual(100, Yard.Stock.Count("2x6x12"));

            // loading out by hand costs labor too
            before = Game.Wallet.Money;
            Assert.IsTrue(Fleet.HaulFromYard(flat, cart, out msg), msg);
            Assert.AreEqual(Fuel("flatbed") + fee, before - Game.Wallet.Money, "fuel + hand loading");
            Fleet.Tick(100f);

            // with a forklift: no handling labor, half the run time, lift pricing
            Assert.IsTrue(Yard.BuyEquipment("forklift", out msg), msg);
            Assert.IsTrue(ForkliftRules.Has);
            Assert.AreEqual(0, ForkliftRules.HandlingFee(cart));
            int matFork = Fleet.MaterialCost(cart, true);
            Assert.Less(matFork, mat, "lift pricing");
            Assert.AreEqual(mat * (1f - Yard.Discount - ForkliftRules.LiftDiscount) / (1f - Yard.Discount), matFork, 2f, "3% off on top of the bulk discount");
            Assert.AreEqual(Fleet.MaterialCost(cart, false), Fleet.MaterialCost(cart, false), "site pick-ups unchanged");
            before = Game.Wallet.Money;
            Assert.IsTrue(Fleet.Haul(flat, cart, true, out msg), msg);
            Assert.AreEqual(matFork + Fuel("flatbed"), before - Game.Wallet.Money, "no hand unloading with the forklift");
            Assert.AreEqual(20f, Fleet.Trips[0].secondsLeft, 0.01f, "warehouse runs take half the time");
            Fleet.Tick(100f);
            Assert.IsTrue(Fleet.HaulFromYard(flat, cart, out msg), msg);
            Assert.AreEqual(20f, Fleet.Trips[0].secondsLeft, 0.01f);
            Fleet.Tick(100f);

            // supplier delivery to the warehouse: hand unloading only without a forklift
            Yard.OwnedEquipment.Remove("forklift");
            before = Game.Wallet.Money;
            int order = Fleet.MaterialCost(cart, true) + Fleet.DeliveryFee;
            Assert.IsTrue(Yard.OrderDelivered(cart, out msg), msg);
            Assert.AreEqual(order + fee, before - Game.Wallet.Money);
            Yard.OwnedEquipment.Add("forklift");
            before = Game.Wallet.Money;
            order = Fleet.MaterialCost(cart, true) + Fleet.DeliveryFee;
            Assert.IsTrue(Yard.OrderDelivered(cart, out msg), msg);
            Assert.AreEqual(order, before - Game.Wallet.Money);

            // a small pickup load is hand-unloaded by the driver: no labor either way
            Assert.AreEqual(0, ForkliftRules.HandlingFee(new Dictionary<string, int> { { "2x4x8", 40 }, { "osb716", 20 } }, false));
            // loose bulk is tipped off the dump truck: never needs a forklift
            Assert.AreEqual(0, ForkliftRules.HandlingFee(new Dictionary<string, int> { { "crusher-run", 5 } }, false));
            // the labor shows in the P&L under the warehouse
            Assert.AreEqual("Warehouse", Finance.Categorize(ForkliftRules.UnloadReason, false));
        }

        [Test]
        public void Forklift_UiLines_And_Payback()
        {
            var e = Yard.EquipmentOf("forklift");
            Assert.AreEqual(28000, e.Price);
            Assert.AreEqual(ForkliftRules.ShopLine, e.Description, "the Warehouse app shows the real effect where it is sold");
            StringAssert.Contains("no crew hand-unloading", e.Description);
            StringAssert.Contains("half the time", e.Description);

            Company.ResetState(); Yard.OwnedEquipment.Clear();
            var cart = HeavyLoad();
            string without = ForkliftRules.LoadLine(cart, false);
            StringAssert.StartsWith("No forklift:", without);
            StringAssert.Contains("crew hours", without);
            Yard.OwnedEquipment.Add("forklift");
            string with = ForkliftRules.LoadLine(cart, true);
            StringAssert.StartsWith("Forklift:", with);
            StringAssert.Contains("no handling labor", with);
            Yard.OwnedEquipment.Clear();

            // payback: a busy builder yard (4 t in + 4 t out a day, $2,500/day of stock) pays it back in tens of days;
            // a light contractor yard takes much longer; nothing pays it back in under two weeks.
            float busy = ForkliftRules.PaybackDays(4f, 4f, 2500f);
            float light = ForkliftRules.PaybackDays(1f, 1f, 600f);
            float heavy = ForkliftRules.PaybackDays(10f, 10f, 6000f);
            Debug.Log($"[Forklift] payback busy {busy:0} days, light {light:0}, heavy {heavy:0}");
            Assert.That(busy, Is.InRange(20f, 120f));
            Assert.Greater(light, busy);
            Assert.Greater(heavy, 14f, "not an instant payback");
        }

        // ------------------------------------------------------------------ vehicle a job needs

        [UnityTest]
        public IEnumerator Sale_BlockedWhileJobNeedsVehicle_AllowedAfter()
        {
            yield return Frames(3);
            Cash(200000);
            Assert.IsTrue(Yard.Lease(1, out var msg), msg);
            var flat = Fleet.Buy("flatbed", true, 0, out msg); Assert.NotNull(flat, msg);
            var job = NeedsJob("12x16 shed for the Parkers", "flatbed");

            int before = Game.Wallet.Money;
            Assert.IsFalse(Fleet.Sell(flat, out msg), "a job needs it");
            StringAssert.Contains("12x16 shed for the Parkers", msg, "the message names the job");
            StringAssert.Contains("flatbed", msg);
            Assert.IsTrue(Company.Owns("flatbed"));
            Assert.AreEqual(before, Game.Wallet.Money, "nothing paid out");
            Assert.AreSame(job, JobVehicleLock.JobNeeding(flat));

            // a second flatbed covers the job: either one may go
            var flat2 = Fleet.Buy("flatbed", true, 0, out msg); Assert.NotNull(flat2, msg);
            Assert.IsTrue(Fleet.Sell(flat2, out msg), msg);
            Assert.IsFalse(Fleet.Sell(flat, out msg), "the last flatbed stays");

            // a crew job off site holds it too (same ActiveJobs list)
            var pickup = Fleet.Buy("pickup", true, 0, out msg); Assert.NotNull(pickup, msg);
            var crewJob = NeedsJob("Cedar fence (crew)", "pickup");
            Assert.IsFalse(Fleet.Sell(pickup, out msg));
            StringAssert.Contains("Cedar fence (crew)", msg);

            // done → allowed
            Company.Complete(job, 1f);
            Assert.IsTrue(Fleet.Sell(flat, out msg), msg);
            Assert.IsFalse(Company.Owns("flatbed"));
            Company.ActiveJobs.Remove(crewJob);
            Assert.IsTrue(Fleet.Sell(pickup, out msg), msg);

            // a job found without its vehicle (an older save) is on hold
            var orphan = NeedsJob("Driveway regrade", "dump");
            Assert.IsFalse(JobVehicleLock.Met(orphan));
            StringAssert.Contains("on hold", JobVehicleLock.PausedLine(orphan));
            Assert.IsTrue(JobVehicleLock.Met(new Job { Title = "any" }));
        }

        [UnityTest]
        public IEnumerator SaveLoad_KeepsForkliftAndJobRequirement()
        {
            yield return Frames(3);
            Cash(200000);
            Assert.IsTrue(Yard.Lease(1, out var msg), msg);
            var flat = Fleet.Buy("flatbed", true, 0, out msg); Assert.NotNull(flat, msg);
            Assert.IsTrue(Yard.BuyEquipment("forklift", out msg), msg);
            NeedsJob("Garage slab haul-in", "flatbed");
            yield return null;
            Assert.IsTrue(SaveSystem.Save("test_forklift"), SaveSystem.LastMessage);

            Company.ResetState(); YardFleetRuntime.ResetAll();
            Assert.IsFalse(ForkliftRules.Has);
            Assert.IsTrue(SaveSystem.Load("test_forklift"), SaveSystem.LastMessage);
            yield return Frames(3);
            Assert.IsTrue(ForkliftRules.Has, "forklift saved");
            Assert.AreEqual(0, ForkliftRules.HandlingFee(HeavyLoad()), "its effect is back after load");
            var f = Company.Vehicles.Find(v => v.TypeId == "flatbed");
            Assert.NotNull(f);
            Assert.IsFalse(Fleet.Sell(f, out msg), "the job's requirement survives save/load");
            StringAssert.Contains("Garage slab haul-in", msg);
        }

        // ------------------------------------------------------------------ screenshot

        [UnityTest]
        public IEnumerator Screenshot_ForkliftAtYard_WithLines()
        {
            yield return Frames(3);
            Cash(200000);
            Assert.IsTrue(Yard.Lease(2, out var msg), msg);
            var flat = Fleet.Buy("flatbed", true, 0, out msg); Assert.NotNull(flat, msg);
            string noFork = ForkliftRules.LoadLine(HeavyLoad(), false);
            Assert.IsTrue(Yard.BuyEquipment("forklift", out msg), msg);
            NeedsJob("12x16 shed for the Parkers", "flatbed");
            Fleet.Sell(flat, out var blocked);
            yield return Frames(4);

            var s = YardSite.I; Assert.NotNull(s);
            var eq = s.transform.Find("Contents/ShopEquipment");
            Assert.NotNull(eq, "forklift built at the shop");
            var r = eq.GetComponentInChildren<Renderer>(); Assert.NotNull(r);
            var b = r.bounds;
            Assert.That(b.size.y, Is.InRange(2.0f, 3.0f), "mast ~8' tall");
            var T = s.transform;
            Vector3 look = b.center - Vector3.up * 0.55f;
            Vector3 pos = b.center + T.forward * 3.6f - T.right * 3.4f + Vector3.up * 1.0f;

            var cam = new GameObject("ForkliftTestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = 60f; cam.nearClipPlane = 0.05f; cam.enabled = false;

            // a board in the lower part of the frame with the UI lines (IMGUI does not draw in batchmode)
            var lines = new StringBuilder();
            lines.AppendLine("Warehouse app - Forklift 5,000 lb ($28,000): " );
            foreach (var l in Wrap(ForkliftRules.ShopLine, 92)) lines.AppendLine("  " + l);
            lines.AppendLine("Before: " + noFork);
            lines.AppendLine("Now:    " + ForkliftRules.LoadLine(HeavyLoad(), false));
            lines.AppendLine("Dealership:");
            foreach (var l in Wrap(blocked, 92)) lines.AppendLine("  " + l);
            var board = new GameObject("ForkliftTestBoard");
            board.transform.SetParent(cam.transform, false);
            board.transform.localPosition = new Vector3(0f, -0.43f, 1f);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(board.transform, false);
            quad.transform.localScale = new Vector3(1.12f, 0.24f, 1f);   // a flat card (primitive quad is 1 m), not a scaled product model
            quad.GetComponent<Renderer>().material.color = new Color(0.08f, 0.09f, 0.1f);
            var tgo = new GameObject("Text"); tgo.transform.SetParent(board.transform, false);
            tgo.transform.localPosition = new Vector3(-0.54f, 0.11f, -0.01f);
            var tm = tgo.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font; tm.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            tm.text = lines.ToString().TrimEnd(); tm.anchor = TextAnchor.UpperLeft; tm.alignment = TextAlignment.Left;
            tm.fontSize = 48; tm.characterSize = 0.0036f; tm.color = new Color(0.95f, 0.95f, 0.9f);
            yield return null;

            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "forklift_yard.png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt); Object.Destroy(cam.gameObject);
            StringAssert.Contains("12x16 shed for the Parkers", blocked);
        }

        static List<string> Wrap(string text, int width)
        {
            var res = new List<string>(); var cur = new StringBuilder();
            foreach (var w in text.Split(' '))
            {
                if (cur.Length + w.Length + 1 > width && cur.Length > 0) { res.Add(cur.ToString()); cur.Clear(); }
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(w);
            }
            if (cur.Length > 0) res.Add(cur.ToString());
            return res;
        }
    }
}
