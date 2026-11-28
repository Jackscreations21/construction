using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Code values / text / behaviour fixes (fence posts, trim nailing, lifeline, header label, Units.Format, compaction,
    /// pulled form stakes, airflow cache, run split on a vertex). Shares the CodeValuesTests class with the stair/insulation tests.</summary>
    public partial class CodeValuesTests
    {
        static IEnumerator FxFrames(int n) { for (int i = 0; i < n; i++) yield return null; }

        [Test]
        public void Fx_UnitsFormat_UnderOneInch_HasNoLeadingZero()
        {
            Assert.AreEqual("1/2\"", Units.Format(Units.In(0.5f)));
            Assert.AreEqual("1/4\"", Units.Format(Units.In(0.25f)));
            Assert.AreEqual("1/16\"", Units.Format(Units.In(1f / 16f)));
            Assert.AreEqual("-1/2\"", Units.Format(-Units.In(0.5f)));
            Assert.AreEqual("0\"", Units.Format(0f));
            Assert.AreEqual("1 1/2\"", Units.Format(Units.In(1.5f)));
            Assert.AreEqual("1' 0 1/2\"", Units.Format(Units.In(12.5f)), "feet keep the 0 inches");
            Assert.AreEqual("7' 8 5/8\"", Units.Format(Units.In(92.625f)));
        }

        [UnityTest]
        public IEnumerator Fx_FencePosts_BelowFrost_30InMin_WithEnoughConcrete()
        {
            yield return null;
            Assert.GreaterOrEqual(Units.ToInches(LandscapeFeature.PostDepth), 30f - 1e-3f, "30\" minimum for a 6' privacy fence");
            Assert.GreaterOrEqual(LandscapeFeature.PostDepth, ExteriorSite.FrostDepth - 1e-5f, "post set to frost depth");
            var post = Catalog.Get(LandscapeFeature.PostStock);
            Assert.NotNull(post, "post stock in the catalog");
            Assert.GreaterOrEqual(post.Size.z + 1e-4f, LandscapeFeature.FenceHeight + LandscapeFeature.PostDepth, "post long enough for 6' + embedment");
            // 10" hole, 36" deep, minus a 4x4: about 1.38 cu ft = 4 bags at 0.375 cu ft
            Assert.AreEqual(4, LandscapeFeature.PostMixBags);

            var go = new GameObject("FxFence");
            var f = go.AddComponent<LandscapeFeature>();
            f.Kind = LandscapeKind.Fence;
            f.A = new Vector3(200f, 0f, 200f); f.B = f.A + new Vector3(Units.Foot * 16f, 0f, 0f);
            var st = f.Stages();
            StringAssert.Contains(Units.Format(LandscapeFeature.PostDepth), st[0].Name, "hole depth in the stage text");
            Assert.AreEqual(LandscapeFeature.PostStock, st[1].Material);
            Assert.AreEqual("postmix-50", st[2].Material);
            Assert.AreEqual(LandscapeFeature.PostMixBags, st[2].Qty, 1e-4f, "bags per post");
            Assert.AreEqual(f.Posts, st[2].Steps);
            Object.Destroy(go);
            yield return null;
        }

        [Test]
        public void Fx_TrimNailing_16InOC_TextAndCheckAgree()
        {
            Assert.AreEqual(1, InteriorInspection.NailsNeeded(Units.In(12f)));
            Assert.AreEqual(2, InteriorInspection.NailsNeeded(Units.In(20f)));
            Assert.AreEqual(5, InteriorInspection.NailsNeeded(Units.In(96f)), "8' base: a nail at each of the 5 inner studs at 16\" o.c.");
            Assert.AreEqual(8, InteriorInspection.NailsNeeded(Units.In(144f)));
            Assert.Greater(InteriorInspection.NailsNeeded(Units.In(144f)), Mathf.FloorToInt(144f / 24f), "stricter than the old 24\" check");
        }

        [Test]
        public void Fx_Lifeline_ReachMatchesShopText()
        {
            Assert.AreEqual(50f * Units.Foot, RoofSafety.LanyardLength, 1e-4f);
            var item = ToolFactory.ShopItems.FirstOrDefault(i => i.Id == "harness");
            Assert.AreEqual("harness", item.Id, "harness kit in the shop");
            StringAssert.Contains("50' rope lifeline", item.Description);
            StringAssert.DoesNotContain("25' lanyard", item.Description);
        }

        [Test]
        public void Fx_HeaderLabel_ShowsBuiltSize()
        {
            Assert.AreEqual("2x12", WallPlan.HeaderLumberLabel(11.25f), "header bumped to 11-1/4\" is a 2x12");
            Assert.AreEqual("2x6", WallPlan.HeaderLumberLabel(5.5f));
            Assert.AreEqual("2x10", WallPlan.HeaderLumberLabel(9.25f));
        }

        [UnityTest]
        public IEnumerator Fx_Compactor_SecondPassDoesNotLowerAgain()
        {
            yield return FxFrames(3);
            var g = SiteGround.I;
            if (g == null) { Assert.Ignore("SiteGround not booted"); yield break; }
            var c = new Vector3(-25f * Units.Foot, 0f, 54f * Units.Foot);
            g.RaiseTo(c, 0.4f, Units.In(2f), GroundCover.Gravel);
            float h0 = g.HeightAt(c);
            Assert.Greater(g.Compact(c, 0.3f), 0, "first pass compacts");
            float h1 = g.HeightAt(c);
            Assert.Less(h1, h0, "fill settles on the first pass");
            g.Compact(c, 0.3f);
            g.Compact(c + new Vector3(0.1f, 0f, 0f), 0.3f);
            Assert.AreEqual(h1, g.HeightAt(c), 1e-6f, "already compacted ground must not keep sinking");
        }

        [UnityTest]
        public IEnumerator Fx_PulledFormStake_NoLongerCounts()
        {
            yield return null;
            var def = Catalog.Get(FormSystem.StakeId);
            Assert.NotNull(def);
            var r = new FormRun { A = new Vector3(-27f * Units.Foot, 0f, 44f * Units.Foot), Dir = Vector3.right, Out = Vector3.back, Length = 2f };
            r.B = r.A + r.Dir * r.Length;
            foreach (float t in new[] { 0.15f, 1.0f, 1.85f })
            {
                var p = Piece.Create(def, 0f, r.PointAt(t) + r.Out * 0.06f, Quaternion.LookRotation(Vector3.up, r.Out));
                r.Stakes.Add(p); r.StakeT.Add(t);
            }
            var pulled = r.Stakes[1];
            Object.Destroy(pulled.gameObject); // what the pry bar does
            yield return null;
            FormSystem.PruneStakes(r);
            Assert.AreEqual(2, r.StakeT.Count, "pulled stake still counted");
            Assert.AreEqual(0.15f, r.StakeT[0], 0.01f);
            Assert.AreEqual(1.85f, r.StakeT[1], 0.01f);
            foreach (var s in r.Stakes) if (s != null) Object.Destroy(s.gameObject);
        }

        [Test]
        public void Fx_AirflowReport_CachedUntilNetworkChanges()
        {
            var a = AirflowSim.Current;
            var b = AirflowSim.Current;
            Assert.AreSame(a, b, "same report while nothing changed");
            NetGraph.Touch();
            Assert.AreNotSame(b, AirflowSim.Current, "network change recomputes");
        }

        [UnityTest]
        public IEnumerator Fx_SplitOnInteriorVertex_NoDoubledBackStub()
        {
            yield return null;
            var spec = RunSpecs.Get("nm-12-2");
            Assert.NotNull(spec);
            var o = new Vector3(210f, 1f, 210f);
            var run = NetRun.Create(spec, new List<Vector3> { o, o + Vector3.right, o + Vector3.right + Vector3.forward });
            var nr = run.SplitAt(o + Vector3.right);
            Assert.NotNull(nr, "split at the corner");
            Assert.AreEqual(2, nr.Points.Count, "second run starts on the next leg: " + string.Join(" ", nr.Points));
            Assert.Greater(Vector3.Dot(nr.Points[1] - nr.Points[0], Vector3.forward), 0.9f, "no back-tracking stub");
            Assert.AreEqual(2, run.Points.Count);
            Assert.Greater(Vector3.Dot(run.Points[1] - run.Points[0], Vector3.right), 0.9f);
            Object.Destroy(run.gameObject); Object.Destroy(nr.gameObject);
            NetGraph.Touch();
            yield return null;
        }
    }
}
