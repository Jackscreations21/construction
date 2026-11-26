using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Job catalog: every definition validates, the generator gives variety per stage, and jobs play to completion through the
    /// public APIs (RepairDefect.Perform via JobCatalog.Solve, RepairJobs invoices, ContractorJobs, Development projects).</summary>
    public class JobCatalogTests
    {
        int _money, _rep, _day; float _hours; GameMode _mode; bool _relaxed;
        Vector3 _pos; float _yaw;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return Frames(2);
            _money = Game.Wallet.Money; _rep = Career.Reputation; _mode = GameSettings.Mode; _relaxed = GameSettings.Relaxed;
            if (GameClock.I != null) { _day = GameClock.I.Day; _hours = GameClock.I.Hours; GameClock.I.Hours = 11f; }
            if (Game.Player != null) { _pos = Game.Player.transform.position; _yaw = Game.Player.transform.eulerAngles.y; }
            GameSettings.Mode = GameMode.Career; GameSettings.Relaxed = false;
            Company.ResetState();
            RepairJobs.ResetState();
            JobCatalog.ResetState();
            HandymanStart.Begin();
            Company.Offers.Clear();
            Career.Reputation = 50;
            yield return Frames(2);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RepairJobs.ResetState();
            JobCatalog.ResetState();
            TownLadder.ResetState();
            WorkVan.Despawn();
            VanCargo.Clear();
            Town.Destroy();
            Company.ResetState();
            Game.Wallet = new Wallet(_money);
            Career.Reputation = _rep; GameSettings.Mode = _mode; GameSettings.Relaxed = _relaxed;
            if (GameClock.I != null) { GameClock.I.Day = _day; GameClock.I.Hours = _hours; }
            HandymanStart.MovePlayer(_pos, _yaw);
            yield return Frames(2);
        }

        // ------------------------------------------------------------------ helpers

        static TownLot LotFor(JobDef d)
        {
            foreach (var l in Town.Addresses) if (l.House != null && JobCatalog.Fits(l.House, d) && !Company.ActiveJobs.Exists(j => j.Address == l.Address)) return l;
            Assert.Fail("no house fits " + d.Id);
            return null;
        }

        static readonly HashSet<string> Ceiling = new HashSet<string> { "ceilinglight", "smoke", "fan", "register", "ceilingstain" };
        static readonly HashSet<string> Floor = new HashSet<string> { "floorpatch", "transition", "slab", "step", "deckstep", "pergola", "retwall" };

        /// <summary>Screenshot framed for the kind of model (camera in the defect's frame), with a fill light indoors.</summary>
        static void Shot(CatalogDefect d, string name)
        {
            var def = d.Def; var sz = def.PropSize; string p = def.Prop;
            Vector3 cam, look;
            if (Ceiling.Contains(p)) { cam = new Vector3(0.9f, -1.3f, 1.1f); look = new Vector3(0, -0.02f, 0); }
            else if (p == "roofpatch" || p == "skylight") { cam = new Vector3(0.6f, 1.8f, -1.6f); look = Vector3.zero; }
            else if (p == "sinkbase") { cam = new Vector3(0.25f, 1.0f, 0.95f); look = new Vector3(0, 0.45f, -0.3f); }
            else if (p == "toilet" || p == "furnace" || p == "waterheater") { cam = new Vector3(0.9f, sz.y * 0.75f + 0.3f, 1.9f); look = new Vector3(0, sz.y * 0.45f, 0.3f); }
            else if (p == "pergola") { cam = new Vector3(5f, 3.2f, 6f); look = new Vector3(0, 1.4f, 0); }
            else if (Floor.Contains(p)) { cam = new Vector3(0.5f, 1.3f, 1.4f + sz.z * 0.4f); look = new Vector3(0, 0.05f, -sz.z * 0.2f); }
            else if (p == "fascia" || p == "gutter" || p == "soffit") { cam = new Vector3(0.4f, -1.0f, 2.4f); look = new Vector3(0, -0.15f, 0); }
            else if (p == "doorhw" || p == "transition") { cam = new Vector3(0.2f, 1.4f, 1.6f); look = new Vector3(0, 1.0f, 0); }
            else if (p == "ledger") { cam = new Vector3(0.4f, 0.6f, 1.6f); look = new Vector3(0, -0.08f, 0); }
            else { float h = Mathf.Max(0.15f, sz.y); cam = new Vector3(0.3f, h * 0.55f + 0.1f, 0.6f + Mathf.Max(sz.x, h) * 0.9f); look = new Vector3(0, h * 0.5f, 0); }
            bool indoor = def.Where != null && !def.Where.Outdoor;
            Vector3 c = d.transform.TransformPoint(cam);
            GameObject lg = null;
            if (indoor)
            {
                lg = new GameObject("TestLight"); lg.transform.position = c + Vector3.up * 0.1f;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 6f; l.intensity = 1.6f;
            }
            SiteLookTests.Shot(c, d.transform.TransformPoint(look), name);
            if (lg != null) Object.DestroyImmediate(lg);
        }

        static CatalogDefect Spawned(Job job)
        {
            var l = RepairJobs.DefectsFor(job);
            Assert.AreEqual(1, l.Count, "one defect spawned for " + job.Title);
            var d = l[0] as CatalogDefect;
            Assert.NotNull(d, "catalog defect");
            return d;
        }

        /// <summary>Accepts a service call for a definition with a chosen cause, plays it with the solver, screenshots before/after, bills it.</summary>
        static RepairInvoice Play(string id, string causeId, string shot)
        {
            var def = JobCatalog.Get(id);
            Assert.NotNull(def, id);
            int ci = def.Causes.FindIndex(c => c.Id == causeId);
            Assert.GreaterOrEqual(ci, 0, causeId);
            Company.Reputation = Mathf.Max(Company.Reputation, def.MinRep);
            var job = JobCatalog.CreateService(LotFor(def), true, new[] { ci }, def);
            Assert.NotNull(job);
            Assert.IsNull(Company.Blocker(job), "handyman with a van can take " + id);
            Assert.IsTrue(Company.Accept(job, out var msg), msg);
            var d = Spawned(job);
            Assert.AreEqual(def.Name, d.Title);
            Assert.IsFalse(d.IsFixed, "starts broken");
            JobCatalog.NextStep(d, RepairContext.Bypass, out var first);     // diagnosis (or what it needs)
            Shot(d, $"jobcat_{shot}_before");
            var steps = JobCatalog.Solve(d);
            Assert.IsTrue(d.IsFixed, $"{id}/{causeId} fixed after {string.Join(", ", steps)}; last: {d.LastMessage}");
            Assert.IsFalse(d.WrongRepair, "no wrong part replaced");
            Assert.GreaterOrEqual(d.Quality, 0.85f, "quality");
            Shot(d, $"jobcat_{shot}_after");
            var inv = RepairJobs.SendInvoice(job);
            Assert.NotNull(inv);
            Assert.Greater(inv.total, RepairJobs.TripCharge);
            Assert.IsFalse(inv.willCallBack, "a good job brings no callback");
            Assert.GreaterOrEqual(inv.satisfaction, 0.8f);
            Debug.Log($"[JobCatalogTests] {id}/{causeId}: {steps.Count} steps, invoice ${inv.total}, paid ${inv.paid}, satisfaction {inv.satisfaction:0.00}");
            return inv;
        }

        // ------------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator AllDefinitionsValidate()
        {
            var errors = JobCatalog.ValidateAll();
            Assert.IsEmpty(errors, string.Join("\n", errors));
            int service = JobCatalog.CountWhere(d => d.Mode == JobMode.Service && !JobCatalog.IsTask(d) && !JobCatalog.IsLegacy(d));
            int contractor = JobCatalog.CountWhere(d => d.Stage == CompanyStage.Contractor && (d.Mode == JobMode.Scope || d.Mode == JobMode.Fleet));
            int builds = JobCatalog.CountWhere(d => d.Mode == JobMode.Build);
            int commercial = JobCatalog.CountWhere(d => d.Mode == JobMode.Build && d.BuildingType != "house");
            Debug.Log($"[JobCatalogTests] catalog: {service} service, {contractor} contractor, {builds} builds ({commercial} outbuilding/commercial), {JobCatalog.All.Count} total");
            Assert.GreaterOrEqual(service, 60, "at least 60 repair/service jobs");
            Assert.GreaterOrEqual(contractor, 25, "at least 25 contractor jobs");
            Assert.GreaterOrEqual(builds, 10);
            Assert.GreaterOrEqual(commercial, 4);
            foreach (var t in new[] { JobTrade.Plumbing, JobTrade.Electrical, JobTrade.Hvac, JobTrade.Carpentry, JobTrade.Drywall, JobTrade.Exterior, JobTrade.Concrete, JobTrade.Masonry, JobTrade.Flooring, JobTrade.Tile, JobTrade.Cabinets, JobTrade.Roofing })
                Assert.GreaterOrEqual(JobCatalog.CountWhere(d => d.Mode == JobMode.Service && !JobCatalog.IsLegacy(d) && d.Trades.Contains(t)), 1, "service jobs for " + t);
            // plan lookups: Development plans resolve, missing blueprint ids are tolerated
            foreach (var id in new[] { "bld.starter", "bld.cottage", "bld.ranch", "bld.craftsman", "bld.colonial", "bld.estate" })
                Assert.NotNull(JobCatalog.PlanFor(JobCatalog.Get(id)), id + " resolves a plan");
            // every service location resolves on a real town house, inside or near its footprint, and every cause is solvable
            Town.Ensure();
            yield return Frames(2);
            var lot = new List<TownLot>(Town.Addresses).Find(l => l.House != null);
            Assert.NotNull(lot);
            var house = lot.House;
            var bounds = new Bounds(house.transform.position, Vector3.zero);
            foreach (var r in house.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            bounds.Expand(new Vector3(16f, 4f, 16f));
            foreach (var d in JobCatalog.All)
            {
                if (d.Mode != JobMode.Service || JobCatalog.IsLegacy(d)) continue;
                Assert.IsTrue(JobCatalog.Fits(house, d), d.Id + " fits a standard house");
                for (int ci = 0; ci < d.Causes.Count; ci++)
                {
                    var job = new Job { Id = "jc-test-" + d.Id + ci, Kind = JobKind.Repair };
                    var s = new DefectState { type = (int)RepairType.Catalog, spot = (int)d.Where.Spot, cause = ci, def = d.Id, jobId = job.Id };
                    var def = RepairDefect.Create(s, job, house) as CatalogDefect;
                    Assert.NotNull(def);
                    Assert.IsTrue(bounds.Contains(def.transform.position), $"{d.Id} at {def.transform.position} is near the house");
                    var steps = JobCatalog.Solve(def);
                    Assert.IsTrue(def.IsFixed, $"{d.Id} cause {d.Causes[ci].Id} solvable: {string.Join(", ", steps)}");
                    Assert.IsFalse(def.WrongRepair, $"{d.Id} cause {d.Causes[ci].Id}: solver used only the right fix");
                    def.Remove();
                }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator GeneratorGivesVarietyPerStage()
        {
            Town.Ensure();
            yield return Frames(2);
            var stages = new[] { (CompanyStage.Handyman, 30), (CompanyStage.Handyman, 75), (CompanyStage.Contractor, 60), (CompanyStage.Crew, 70), (CompanyStage.Builder, 80), (CompanyStage.Developer, 92) };
            foreach (var (stage, rep) in stages)
            {
                Company.Stage = stage; Company.Reputation = rep;
                JobCatalog.ResetState();
                var seen = new HashSet<string>(); var modes = new HashSet<JobMode>();
                List<string> last = null;
                for (int round = 0; round < 8; round++)
                {
                    Company.Offers.Clear();
                    JobCatalog.Generate();
                    var now = new List<string>();
                    foreach (var j in Company.Offers)
                    {
                        var d = JobCatalog.DefOf(j);
                        Assert.NotNull(d, "every offer maps back to its definition: " + j.Id);
                        Assert.LessOrEqual((int)d.Stage, (int)stage, d.Id + " not above the company stage");
                        Assert.LessOrEqual(d.MinRep, rep, d.Id + " reputation gate");
                        now.Add(d.Id); seen.Add(d.Id); modes.Add(d.Mode);
                    }
                    Assert.GreaterOrEqual(now.Count, JobCatalog.ServiceTarget(stage, rep), $"{stage}: board filled");
                    var dupes = new HashSet<string>();
                    foreach (var id in now) Assert.IsTrue(dupes.Add(id), $"{stage}: {id} twice on one board");
                    if (last != null) foreach (var id in now) Assert.IsFalse(last.Contains(id), $"{stage}: {id} offered back to back");
                    last = now;
                }
                Debug.Log($"[JobCatalogTests] {stage} rep {rep}: {seen.Count} different jobs in 8 days, modes {string.Join(",", modes)}");
                Assert.GreaterOrEqual(seen.Count, stage == CompanyStage.Handyman && rep < 40 ? 12 : 18, stage + " variety");
                if (stage == CompanyStage.Handyman) Assert.IsFalse(modes.Contains(JobMode.Fleet) || modes.Contains(JobMode.Scope) || modes.Contains(JobMode.Build), "handyman gets service calls only");
                if (stage >= CompanyStage.Contractor) Assert.IsTrue(modes.Contains(JobMode.Fleet) || modes.Contains(JobMode.Scope), stage + " gets contractor work");
                if (stage >= CompanyStage.Builder) Assert.IsTrue(modes.Contains(JobMode.Build), stage + " gets new builds");
            }
            // season and weather
            var deck = JobCatalog.Get("drive.12x40");
            int winterDay = 1 + 30 * 9;     // month 0 = January
            Assert.AreEqual(Seasons.Winter, JobCatalog.SeasonOf(winterDay));
            Assert.AreEqual(0f, JobCatalog.WeightFor(deck, CompanyStage.Contractor, 60, winterDay, false, 30f), "no concrete pours offered in winter");
            var roof = JobCatalog.Get("ext.roof-leak");
            Assert.Greater(JobCatalog.WeightFor(roof, CompanyStage.Handyman, 60, 40, true, 60f), JobCatalog.WeightFor(roof, CompanyStage.Handyman, 60, 40, false, 60f), "roof leak calls go up in the rain");
            var furnace = JobCatalog.Get("hvac.no-heat");
            Assert.Greater(JobCatalog.WeightFor(furnace, CompanyStage.Handyman, 60, winterDay, false, 25f), JobCatalog.WeightFor(furnace, CompanyStage.Handyman, 60, winterDay, false, 55f), "no-heat calls go up in the cold");
            Assert.AreEqual(0f, JobCatalog.WeightFor(JobCatalog.Get("ext.roof-leak"), CompanyStage.Handyman, 10, 40, false, 60f), "reputation gate");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlumbingAndElectricalCallsPlayToCompletion()
        {
            Town.Ensure();
            yield return Frames(2);
            Play("plb.toilet-running", "flapper", "toilet");
            Play("plb.wh-no-hot", "lower", "waterheater");
            Play("el.gfci-chain", "gfci", "gfci");
            Play("el.fixture-swap", "box", "light");
            yield return null;
        }

        [UnityTest]
        public IEnumerator HvacCarpentryDrywallCallsPlayToCompletion()
        {
            Town.Ensure();
            yield return Frames(2);
            Play("hvac.condensate", "trap", "furnace");
            Play("carp.window-sash", "balance", "sash");
            Play("dw.ceiling-stain", "sag", "ceiling");
            Play("carp.door-sag", "screws", "door");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExteriorAndMasonryCallsPlayToCompletion()
        {
            Town.Ensure();
            yield return Frames(2);
            Play("ext.fence-section", "post", "fence");
            Play("mas.tuckpoint", "joints", "planter");
            Play("ext.roof-leak", "boot", "roof");
            Play("flr.cracked-tile", "impact", "tile");
            Play("mas.cracked-step", "edge", "step");
            Play("ext.siding", "cracked", "siding");
            yield return null;
        }

        [UnityTest]
        public IEnumerator WrongFixCostsQualityAndUnfinishedWorkCallsBack()
        {
            Town.Ensure();
            yield return Frames(2);
            var def = JobCatalog.Get("plb.toilet-running");
            int fill = def.Causes.FindIndex(c => c.Id == "fill");
            var job = JobCatalog.CreateService(LotFor(def), true, new[] { fill }, def);
            Assert.IsTrue(Company.Accept(job, out var m), m);
            var d = Spawned(job);
            var ctx = RepairContext.Bypass;
            Assert.IsFalse(d.Perform("flapper", ctx, out _), "can't swap the flapper with the lid on and water on");
            Assert.IsTrue(d.Perform("lid-off", ctx, out _));
            Assert.IsTrue(d.Perform("dye", ctx, out var reading));
            StringAssert.Contains("fill valve", reading, "diagnosis shows the real cause");
            Assert.IsTrue(d.Perform("water-off", ctx, out _));
            Assert.IsTrue(d.Perform("flapper", ctx, out var wrong));
            StringAssert.Contains("not what was wrong", wrong);
            Assert.IsTrue(d.WrongRepair);
            Assert.IsFalse(d.IsFixed);
            JobCatalog.Solve(d);
            Assert.IsTrue(d.IsFixed);
            Assert.Less(d.Quality, 0.9f, "the unneeded part cost quality");
            RepairJobs.SendInvoice(job);

            // a half-done job: billed unfinished -> callback that still knows its definition
            var def2 = JobCatalog.Get("el.switch");
            var job2 = JobCatalog.CreateService(LotFor(def2), true, new[] { 0 }, def2);
            Assert.IsTrue(Company.Accept(job2, out m), m);
            var d2 = Spawned(job2);
            Assert.IsTrue(d2.Perform("power-off", ctx, out _));
            Assert.IsTrue(d2.Perform("plate-off", ctx, out _));
            Assert.IsFalse(d2.IsFixed);
            int pending = RepairJobs.PendingCallbacks;
            var inv = RepairJobs.SendInvoice(job2);
            Assert.IsTrue(inv.willCallBack);
            Assert.AreEqual(pending + 1, RepairJobs.PendingCallbacks);
            RepairJobs.ProcessDay((GameClock.I != null ? GameClock.I.Day : 1) + 5);
            var cb = Company.Offers.Find(j => j.Title.StartsWith("Callback"));
            Assert.NotNull(cb, "callback offered");
            Assert.AreEqual(def2, JobCatalog.DefOf(cb), "callback keeps the catalog definition");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RemodelScopeFleetAndBuildJobs()
        {
            Town.Ensure();
            yield return Frames(2);
            // contractor company
            Company.HasWarehouse = true; Company.WarehouseLevel = 2;
            Company.AddVehicle("pickup", "Pickup"); Company.AddVehicle("flatbed", "Flatbed"); Company.AddVehicle("dump", "Dump truck"); Company.AddVehicle("boom", "Boom truck");
            Assert.GreaterOrEqual((int)Company.Stage, (int)CompanyStage.Contractor);
            Company.Reputation = 60;

            // remodel scope: every task played, billed through the invoice system
            var scope = JobCatalog.Get("rem.bath-refresh");
            var lot = LotFor(scope);
            var job = JobCatalog.CreateScope(scope, lot, true);
            Assert.NotNull(job);
            Assert.AreEqual(JobKind.Remodel, job.Kind);
            Assert.IsTrue(RepairJobs.IsService(job));
            Assert.AreEqual(scope, JobCatalog.DefOf(job));
            Assert.IsNull(Company.Blocker(job), Company.Blocker(job));
            Assert.IsTrue(Company.Accept(job, out var m), m);
            var defects = RepairJobs.DefectsFor(job);
            Assert.AreEqual(scope.Tasks.Count, defects.Count, "one defect per task");
            CatalogDefect vanity = null;
            foreach (var x in defects)
            {
                var cd = (CatalogDefect)x;
                if (cd.Def.Id == "task.vanity") vanity = cd;
                JobCatalog.Solve(cd);
                Assert.IsTrue(cd.IsFixed, cd.Def.Id);
            }
            Shot(vanity, "jobcat_scope_vanity_after");
            var inv = RepairJobs.SendInvoice(job);
            Assert.Greater(inv.total, 1500, "a bathroom refresh is a real-money job");
            Debug.Log($"[JobCatalogTests] bath refresh invoice ${inv.total}");

            // fleet job: sized and priced by the definition, verified by ContractorJobs
            var deckDef = JobCatalog.Get("deck.16x20");
            var deck = JobCatalog.CreateFleet(deckDef, 1234, true);
            Assert.NotNull(deck);
            Assert.IsTrue(ContractorJobs.IsContractorJob(deck));
            Assert.AreEqual(deckDef, JobCatalog.DefOf(deck));
            var spec = ContractorJobs.SpecOf(deck);
            Assert.AreEqual(20f, spec.widthFt); Assert.AreEqual(16f, spec.lengthFt); Assert.AreEqual(36f, spec.heightIn);
            Assert.AreEqual(JobCatalog.FleetPrice(deckDef), deck.Payout);
            Assert.Greater(deck.MaterialBudget, 2000, "takeoff priced");
            Assert.IsFalse(ContractorJobs.Verify(deck).Ok, "nothing built yet");

            // new build: plan looked up at runtime, accept starts a Development project with a deposit
            Company.Workers.Add(new Worker { Uid = "w1", Name = "A" }); Company.Workers.Add(new Worker { Uid = "w2", Name = "B" });
            Company.Stage = CompanyStage.Builder; Company.Reputation = 70;
            var bdef = JobCatalog.Get("bld.ranch");
            var bjob = JobCatalog.CreateBuild(bdef, true);
            if (bjob == null) { Assert.Inconclusive("no free development lot in this town layout"); yield break; }
            Assert.AreEqual(JobKind.House, bjob.Kind);
            Assert.AreEqual(bdef, JobCatalog.DefOf(bjob));
            Assert.Greater(bjob.Payout, 200000);
            Company.ActiveJobs.Clear();
            int before = Game.Wallet.Money;
            Assert.IsTrue(Company.Accept(bjob, out m), m);
            var bp = JobCatalog.ReadBuild(bjob);
            Assert.IsFalse(string.IsNullOrEmpty(bp.project), "project started");
            Assert.AreEqual(Mathf.RoundToInt(bjob.Payout * Builder.DepositPct), Game.Wallet.Money - before, "10% deposit paid");
            var proj = Projects.All.Find(p => p.Id == bp.project);
            Assert.NotNull(proj);
            Assert.AreEqual(bjob.Id, proj.JobId, "Builder's draw schedule pays this job");
            Projects.All.Remove(proj);
            // a commercial build whose blueprint may not exist yet: tolerated
            var wh = JobCatalog.Get("bld.warehouse");
            if (JobCatalog.PlanFor(wh) == null) Assert.IsNull(JobCatalog.CreateBuild(wh, false), "missing blueprint: no offer, no crash");
            yield return null;
        }
    }
}
