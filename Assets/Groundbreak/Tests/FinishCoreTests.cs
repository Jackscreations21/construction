using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Groundbreak.Tests.FinishTestKit;

namespace Groundbreak.Tests
{
    /// <summary>Framework checks for the close-up/finish trades: framed-face discovery, bays, openings, obstacles, panels.</summary>
    public class FinishCoreTests
    {
        [UnityTest]
        public IEnumerator FramePlaneFindsBaysOpeningsAndBoxes()
        {
            yield return Frames(5);
            var origin = new Vector3(40f, 0f, 40f);
            // 12' wall along +X, depth toward +Z; interior face is the +Z side? Depth goes +Z, so the face toward -Z is z = origin.z
            StudWall(origin, Vector3.right, Vector3.forward, 144f, new Opening { LeftIn = 60f, WidthIn = 38f, SillIn = 0f, HeadIn = 82.5f });
            CeilingJoists(origin, Vector3.right, Vector3.forward, 144f, 144f);
            var box = OutletBox(origin + Vector3.right * Units.In(17.5f + 1f) + Vector3.up * Units.In(16f), Vector3.back);
            yield return Frames(3);
            Physics.SyncTransforms();

            var ray = new Ray(origin + new Vector3(Units.In(32f), 1.2f, -1f), Vector3.forward);
            Assert.IsTrue(Physics.Raycast(ray, out var hit, 3f), "no stud hit");
            var fp = FramePlane.FromHit(hit);
            Assert.NotNull(fp, "no frame plane from stud hit");
            Debug.Log("[FinishCore] " + fp);
            Assert.IsTrue(fp.IsWall);
            Assert.AreEqual(144f, Units.ToInches(fp.Bounds.width), 0.2f, "wall face width");
            Assert.AreEqual(97.125f, Units.ToInches(fp.Bounds.height), 0.2f, "wall face height");
            var bays = fp.Bays();
            int normal = 0, open = 0;
            foreach (var b in bays) { if (b.IsOpening) open++; else normal++; }
            Debug.Log($"[FinishCore] bays {normal} + openings {open}");
            Assert.GreaterOrEqual(normal, 6);
            Assert.AreEqual(1, open, "door rough opening");
            var obs = fp.Obstacles();
            Assert.AreEqual(1, obs.Count, "outlet box in cavity");
            Assert.AreEqual(0.5f, Units.ToInches(obs[0].Proud), 0.05f, "box sits 1/2\" proud for drywall");

            // ceiling from below
            var ray2 = new Ray(origin + new Vector3(Units.In(16f), 1.5f, 1.5f), Vector3.up);
            Assert.IsTrue(Physics.Raycast(ray2, out var hit2, 3f), "no joist hit");
            var cp = FramePlane.FromHit(hit2);
            Assert.NotNull(cp);
            Assert.IsTrue(cp.IsCeiling, "ceiling expected: " + cp);
            Debug.Log("[FinishCore] ceiling " + cp + " lines " + cp.FramingLinesU().Count);
            Assert.GreaterOrEqual(cp.FramingLinesU().Count, 10);

            // Panel with a hole builds a mesh
            var mb = new MeshBuilder();
            PanelMesh.AddSlab(mb, new Rect(0, 0, 1.2f, 2.4f), new[] { new Rect(0.5f, 0.5f, 0.1f, 0.1f) }, 0f, 0.0127f, 0, 1, 2);
            var mesh = mb.Build();
            Assert.Greater(mesh.vertexCount, 40);
            Object.Destroy(box);
        }
    }
}
