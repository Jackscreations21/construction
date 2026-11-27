using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Map visual fixes: building footprints agree with their plans, the build site across from home is filled but stays buildable,
    /// open land between everything is dressed, the far ring reaches the horizon, weeds read as weeds, seasons apply. Shots Logs/shots/mapfix_*.</summary>
    public class MapFixTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        float _hours = -1f;
        readonly List<GameObject> _temp = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var g in _temp) if (g != null) Object.Destroy(g);
            _temp.Clear();
            SeasonWeather.FreeMonth = 5; SeasonWeather.FreeSky = Sky.Clear; SeasonWeather.FreeStorm = StormKind.None;
            SeasonWeather.Override = null; SeasonWeather.GroundOverride = null;
            PlanOverlay.Hide();
            Town.Destroy();
            Company.ResetState();
            MapStreamer.Frozen = false;
            if (GameClock.I != null && _hours >= 0) GameClock.I.Hours = _hours;
            yield return Frames(3);
            SeasonLook.Refresh(); WeedLook.ApplyAll();
        }

        IEnumerator BuildTown()
        {
            yield return Frames(2);
            GameSettings.Mode = GameMode.FreeBuild;
            Town.Ensure();
            if (GameClock.I != null) { _hours = GameClock.I.Hours; GameClock.I.Hours = 10.5f; }
            yield return Frames(3);
        }

        static void Shot(Vector3 pos, Vector3 look, string name, float fov = 60f, bool noFog = false)
        {
            bool fog = RenderSettings.fog;
            if (noFog) RenderSettings.fog = false;
            SiteLookTests.Shot(pos, look, name, fov);
            RenderSettings.fog = fog;
        }

        /// <summary>Union (model frame) of the drawn building's solid boxes (≥ 2 m tall) whose centre is inside its body (+1.5 m).</summary>
        static bool Measured(MapBuildingSpec spec, Rect body, out Rect drawn)
        {
            var k = new LandModel();
            var b = new MapBuild { K = k, Rng = new System.Random(spec.Seed), Frame = Matrix4x4.identity };
            MapModels.Draw(b, spec);
            var grow = Rect.MinMaxRect(body.xMin - 1.5f, body.yMin - 1.5f, body.xMax + 1.5f, body.yMax + 1.5f);
            bool any = false; drawn = default;
            foreach (var (mn, mx) in b.Solids)
            {
                if (mx.y - mn.y < 2f) continue;
                var c = new Vector2((mn.x + mx.x) / 2f, (mn.z + mx.z) / 2f);
                if (!grow.Contains(c)) continue;
                var r = Rect.MinMaxRect(mn.x, mn.z, mx.x, mx.z);
                drawn = any ? Rect.MinMaxRect(Mathf.Min(drawn.xMin, r.xMin), Mathf.Min(drawn.yMin, r.yMin), Mathf.Max(drawn.xMax, r.xMax), Mathf.Max(drawn.yMax, r.yMax)) : r;
                any = true;
            }
            return any;
        }

        [UnityTest]
        public IEnumerator PlanFootprintsAgreeWithBuildings()
        {
            yield return BuildTown();
            var lots = MapPlans.LotsWithPlans().ToList();
            Debug.Log($"[MapFix] {lots.Count} buildings with a plan, {MapPlans.Resized} resized to their plan, {MapPlans.Dropped} without a fitting plan");
            Assert.Greater(lots.Count, 150, "most buildings still have a plan");
            var bad = new List<string>();
            var byType = new Dictionary<string, int>();
            foreach (var l in lots)
            {
                var plan = MapLayout.PlanFor(l);
                float pw = plan.WidthFt * Units.Foot, pd = plan.DepthFt * Units.Foot;
                string type = l.TypeId ?? "core-house";
                byType[type + " → " + plan.Id] = byType.TryGetValue(type + " → " + plan.Id, out var n) ? n + 1 : 1;
                if (l.House != null)
                {
                    if (!MapPlans.Agrees(l.House.Width, l.House.Depth, pw, pd)) bad.Add($"{l.Id} house {l.House.Width:0.0}x{l.House.Depth:0.0} vs {plan.Id} {pw:0.0}x{pd:0.0}");
                    continue;
                }
                var p = MapPlans.ParcelOf(l);
                Assert.NotNull(p, l.Id + " has a plan but no building");
                var body = MapBodies.Body(p.Spec);
                if (!MapPlans.Agrees(body.width, body.height, pw, pd)) { bad.Add($"{l.Id} {type} body {body.width:0.0}x{body.height:0.0} vs {plan.Id} {pw:0.0}x{pd:0.0}"); continue; }
                if (!Measured(p.Spec, body, out var drawn)) { bad.Add($"{l.Id} {type}: no solid building drawn"); continue; }
                if (!MapPlans.Agrees(drawn.width, drawn.height, pw, pd)) bad.Add($"{l.Id} {type} drawn {drawn.width:0.0}x{drawn.height:0.0} vs {plan.Id} {pw:0.0}x{pd:0.0}");
            }
            Debug.Log("[MapFix] plans by type: " + string.Join(", ", byType.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} x{kv.Value}")));
            Assert.IsEmpty(bad, $"{bad.Count} footprints disagree with their plans: " + string.Join("; ", bad.Take(40)));

            // the diner and a gas station with their blueprints laid over them
            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            var diner = Town.Lots.First(l => l.TypeId == "diner" && l.External && MapLayout.PlanFor(l) != null);
            Assert.NotNull(BuildingPlansApp.ShowOver(diner));
            Assert.IsTrue(BuildingPlansApp.Footprint(diner, out var c, out var rot, out _, out float roof));
            Shot(c + rot * Vector3.forward * 26f + Vector3.up * (roof + 16f) + rot * Vector3.right * 10f, c + Vector3.up * roof, "mapfix_diner_blueprint");
            var strip = Town.Lots.Where(l => l.TypeId == "diner" && l.External && MapLayout.PlanFor(l) != null).Skip(1).FirstOrDefault();
            if (strip != null)
            {
                BuildingPlansApp.ShowOver(strip);
                BuildingPlansApp.Footprint(strip, out c, out rot, out _, out roof);
                Shot(c + rot * Vector3.forward * 30f + Vector3.up * (roof + 18f) - rot * Vector3.right * 12f, c + Vector3.up * roof, "mapfix_diner2_blueprint");
            }
            var gas = Town.Lots.FirstOrDefault(l => l.TypeId == "gasstation" && MapLayout.PlanFor(l) != null);
            if (gas != null)
            {
                BuildingPlansApp.ShowOver(gas);
                BuildingPlansApp.Footprint(gas, out c, out rot, out _, out roof);
                Shot(c + rot * Vector3.forward * 34f + Vector3.up * (roof + 20f) + rot * Vector3.right * 12f, c + Vector3.up * roof, "mapfix_gas_blueprint");
            }
        }

        [UnityTest]
        public IEnumerator BuildSiteIsFilledAndStaysBuildable()
        {
            yield return BuildTown();
            var fill = BuildSiteFill.I;
            Assert.NotNull(fill, "build-site fill exists");
            Debug.Log($"[MapFix] build site: {fill.Chunks} chunks, {fill.Spots} weed spots, {fill.Vertices:N0} vertices, {fill.Hidden} hidden at start");
            Assert.Greater(fill.Spots, 500, "the field across from home is not bare");
            // at the start only chunks next to the pad may be cleared (the starting lumber pile sits beside it)
            foreach (var probe0 in new[] { new Vector3(-33f, 0, 40f), new Vector3(33f, 0, 60f), new Vector3(0f, 0, 78f), new Vector3(-30f, 0, -38f), new Vector3(30f, 0, -32f), new Vector3(-20f, 0, 50f) })
                Assert.IsFalse(fill.IsHidden(probe0), "nothing blocks the fill at the start: " + BuildSiteFill.LastBlock);
            Assert.IsFalse(fill.IsFilled(new Vector3(0, 0, 9f)), "the pad stays clear");
            Assert.IsNull(fill.transform.parent, "not part of Town.Root (the town builds nothing on the site)");

            // something built in the meadow → that chunk's dressing goes; removed → it comes back
            var probe = new Vector3(-32f, 0, 40f);
            Assert.IsTrue(fill.IsFilled(probe));
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(box);
            box.transform.position = probe + Vector3.up * 1f; box.transform.localScale = new Vector3(3f, 2f, 3f);
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
            fill.CheckNow();
            Assert.IsTrue(fill.IsHidden(probe), "dressing cleared where something is built");
            Object.Destroy(box);
            yield return Frames(2);
            Physics.SyncTransforms();
            fill.CheckNow();
            Assert.IsFalse(fill.IsHidden(probe), "dressing back once the spot is clear");
            // graded / dug ground clears it too
            if (Grade.Field == null)
            {
                Grade.Create(-40f, 20f, 40);
                Grade.Field.SetHeights((x, z) => 0.4f);
                fill.CheckNow();
                Assert.IsTrue(fill.IsHidden(new Vector3(-30f, 0, 30f)), "graded ground clears the dressing");
                Grade.Destroy();
                yield return Frames(2);
                fill.CheckNow();
                Assert.IsFalse(fill.IsHidden(new Vector3(-30f, 0, 30f)));
            }

            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            var home = Town.Home.Parking + Vector3.up * 1.7f;
            Shot(home, home + Vector3.forward * 50f, "mapfix_field_north_eye");
            Shot(new Vector3(-12f, 1.7f, -45f), new Vector3(-30f, 0.5f, 10f), "mapfix_field_staging_west");
            Shot(new Vector3(8f, 1.7f, -46f), new Vector3(30f, 1f, -32f), "mapfix_field_trailer");
            Shot(new Vector3(0f, 1.7f, 30f), new Vector3(-35f, 0.3f, 60f), "mapfix_field_meadow");
            Shot(new Vector3(0f, 70f, -110f), new Vector3(0f, 0f, 20f), "mapfix_field_aerial", 60f, true);
        }

        [UnityTest]
        public IEnumerator OpenLandAndHorizonAreDressed()
        {
            yield return BuildTown();
            Debug.Log($"[MapFix] map {MapWorld.Last.Seconds:0.00} s, {MapWorld.Last.Triangles:N0} tris; gap fill {MapGapFill.FreeArea / 10000f:0.0} ha, {MapGapFill.Patches} patches, {MapGapFill.Spots} spots, {MapGapFill.Trees} trees; far ring {FarRing.Fields} fields, {FarRing.TreeBlobs} blobs, {FarRing.Vertices:N0} verts; vacant lots {VacantLots.Vertices:N0} verts");
            Assert.Greater(MapGapFill.Spots, 1500, "open land has weeds and wildflowers");
            Assert.Greater(MapGapFill.Patches, 50, "unmown meadow patches");
            Assert.NotNull(FarRing.Root, "far ring built");
            Assert.Greater(FarRing.Fields, 200);
            Assert.Less(MapWorld.Last.Seconds, 6f, "map build stays quick");

            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            // aerials to the horizon (the far-ring camera hook pushes the far plane out above 60 m)
            Shot(new Vector3(-250f, 520f, -820f), new Vector3(120f, 0f, 120f), "mapfix_aerial_horizon", 62f, true);
            Shot(new Vector3(-250f, 520f, -820f), new Vector3(120f, 0f, 120f), "mapfix_aerial_horizon_fog", 62f, false);
            Shot(new Vector3(1500f, 300f, 200f), new Vector3(4000f, 0f, 400f), "mapfix_aerial_east_edge", 62f, true);
            // eye-level sweep of the districts
            foreach (var id in new[] { "strip", "school", "eastside", "industrial", "westside", "north", "farms", "devland" })
            {
                var d = MapLayout.District(id);
                var fwd = Quaternion.Euler(0, d.ViewYaw, 0) * Vector3.forward;
                Shot(d.View, d.View + fwd * 40f, "mapfix_eye_" + id);
            }
        }

        [UnityTest]
        public IEnumerator RebuildingTheMapDoesNotLeak()
        {
            yield return BuildTown();
            var raw = new List<int>(); var swept = new List<int>(); var mats = new List<int>(); var texs = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                Town.Destroy();
                yield return Frames(3);
                raw.Add(Resources.FindObjectsOfTypeAll<Mesh>().Length);   // after the explicit mesh destroys, before the sweep
                yield return MapMemory.SweepNow();
                swept.Add(Resources.FindObjectsOfTypeAll<Mesh>().Length);
                mats.Add(Resources.FindObjectsOfTypeAll<Material>().Length);
                texs.Add(Resources.FindObjectsOfTypeAll<Texture2D>().Length);
                Town.Ensure();
                yield return Frames(2);
            }
            Debug.Log($"[MapFix] live meshes after each town teardown: raw {string.Join(",", raw)}; swept {string.Join(",", swept)}; materials {string.Join(",", mats)}; textures {string.Join(",", texs)}");
            Assert.LessOrEqual(raw[9], raw[1] + 40, "map / far ring / dressing meshes are destroyed with the town: " + string.Join(",", raw));
            Assert.LessOrEqual(swept[9], swept[1] + 20, "live meshes keep growing across rebuilds: " + string.Join(",", swept));
            Assert.LessOrEqual(mats[9], mats[1] + 20, "live materials keep growing: " + string.Join(",", mats));
            Assert.LessOrEqual(texs[9], texs[1] + 20, "live textures keep growing: " + string.Join(",", texs));
        }

        [UnityTest]
        public IEnumerator WeedsReadAsWeedsAndFollowSeasons()
        {
            yield return BuildTown();
            MapStreamer.Frozen = true; MapWorld.ShowAll(true);
            yield return Frames(3);
            var lot = Town.FreeLots.First(l => l.External && l.District == "north" && l.TypeId == null);
            Shot(lot.Local(1.5f, 6f) + Vector3.up * 0.9f, lot.Local(-2f, 14f) + Vector3.up * 0.2f, "mapfix_weeds_closeup", 55f);
            Shot(new Vector3(-30f, 1.1f, 20f), new Vector3(-34f, 0.2f, 34f), "mapfix_weeds_meadow_closeup", 55f);

            var grass = WeedLook.Get(WeedTone.Grass); var summer = grass.color;
            var home = Town.Home.Parking + Vector3.up * 1.7f;
            SeasonWeather.FreeMonth = 9; yield return Frames(2); SeasonLook.Refresh(); WeedLook.ApplyAll();
            StringAssert.StartsWith("Fall", WeedLook.Key);
            Assert.AreNotEqual(summer, grass.color, "weeds go tawny in the fall");
            Shot(home, home + Vector3.forward * 50f, "mapfix_fall_field");
            SeasonWeather.FreeMonth = 0; yield return Frames(2); SeasonLook.Refresh(); WeedLook.ApplyAll();
            StringAssert.StartsWith("Winter", WeedLook.Key);
            Assert.Greater(SeasonWeather.Ground.SnowIn, 0.5f);
            Assert.Greater(WeedLook.Get(WeedTone.DryPatch).color.b, 0.7f, "flat patches whiten under snow");
            Shot(home, home + Vector3.forward * 50f, "mapfix_winter_field");
            Shot(new Vector3(-250f, 520f, -820f), new Vector3(120f, 0f, 120f), "mapfix_winter_aerial", 62f, true);
            SeasonWeather.FreeMonth = 5; yield return Frames(2); SeasonLook.Refresh(); WeedLook.ApplyAll();
            Assert.AreEqual(summer, grass.color, "summer colours come back");
        }
    }
}
