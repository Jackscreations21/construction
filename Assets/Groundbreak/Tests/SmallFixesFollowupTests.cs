using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Small follow-up fixes from the photo-mode handoff: close-up "before" photo eye height and the
    /// PhotoRender.HideInPhotos hook for world-space gameplay helpers. Screenshot → Logs/shots/followup_photo_hidden_helper.png.</summary>
    public class SmallFixesFollowupTests
    {
        static readonly Vector3 Spot = new Vector3(-820f, 40f, 820f);
        GameObject _helper;

        [TearDown]
        public void TearDown()
        {
            if (_helper != null)
            {
                var r = _helper.GetComponent<Renderer>();
                PhotoRender.ShowInPhotos(r);
                Object.Destroy(_helper);
            }
            _helper = null;
        }

        [Test]
        public void CloseUpEye_LimitsDownwardLook()
        {
            var sink = new Vector3(0f, 0.85f, 0f);
            var standing = new Vector3(0f, 1.65f, 0.9f);
            var eye = JobPhotos.CloseUpEye(standing, sink);
            float down = Vector3.Angle(Vector3.ProjectOnPlane(sink - eye, Vector3.up), sink - eye);
            Assert.LessOrEqual(down, 35.5f, "no steep look-down on a close-up");
            Assert.GreaterOrEqual(eye.y, sink.y, "never below the work");
            Assert.AreEqual(standing.x, eye.x, 1e-4f); Assert.AreEqual(standing.z, eye.z, 1e-4f);
            // far away: already shallow, unchanged
            var far = new Vector3(0f, 1.65f, 5f);
            Assert.AreEqual(far, JobPhotos.CloseUpEye(far, sink));
            // straight overhead: left alone (the step-back handles it)
            var over = new Vector3(0f, 1.65f, 0.1f);
            Assert.AreEqual(over, JobPhotos.CloseUpEye(over, sink));
        }

        [UnityTest]
        public IEnumerator HideInPhotos_HelperNotInCapture_AndRestored()
        {
            yield return null;
            _helper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _helper.name = "FollowupGuideHelper";
            _helper.transform.position = Spot + new Vector3(0f, 0f, 3f);
            var r = _helper.GetComponent<Renderer>();
            r.material.color = new Color(1f, 0.05f, 0.05f);
            var s = new PhotoSettings { Vignette = 0f };
            var rot = Quaternion.LookRotation(Vector3.forward);

            var shown = PhotoRender.Capture(Spot, rot, s, 64, 64);
            Color cShown = shown.GetPixel(32, 32);

            PhotoRender.HideInPhotos(_helper);
            Assert.IsTrue(PhotoRender.IsHiddenInPhotos(r));
            var hidden = PhotoRender.Capture(Spot, rot, s, 64, 64);
            Color cHidden = hidden.GetPixel(32, 32);
            Assert.IsTrue(r.enabled, "the helper is switched back on after the photo");

            var dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "followup_photo_hidden_helper.png"), hidden.EncodeToPNG());
            Object.Destroy(shown); Object.Destroy(hidden);

            float diff = Mathf.Abs(cShown.r - cHidden.r) + Mathf.Abs(cShown.g - cHidden.g) + Mathf.Abs(cShown.b - cHidden.b);
            Assert.Greater(diff, 0.1f, $"the registered helper is not in the photo (shown {cShown}, hidden {cHidden})");
        }
    }
}
