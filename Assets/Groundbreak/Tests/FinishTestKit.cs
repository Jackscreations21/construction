using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Groundbreak.Tests
{
    /// <summary>Builds real framing out of Pieces (studs, plates, headers, joists, OSB) for close-up/finish tests,
    /// plus camera/screenshot helpers. All dimensions are real US framing.</summary>
    public static class FinishTestKit
    {
        public static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        /// <summary>Destroys every Piece, finish layer, inspection flag and test camera so later tests start from a clean
        /// site (tests share one booted scene). Call from [UnityTearDown].</summary>
        public static IEnumerator CleanScene()
        {
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (p != null) Object.Destroy(p.gameObject);
            foreach (var f in Object.FindObjectsByType<FinishLayer>(FindObjectsSortMode.None))
                if (f != null) Object.Destroy(f.transform.root == f.transform || f.transform.root.GetComponent<Piece>() != null ? f.gameObject : f.transform.root.gameObject);
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c != null && c.name == "FinishTestCam") Object.Destroy(c.gameObject);
            CloseupInspection.ClearMarkers();
            FinishStock.Unlimited = false;
            // put the player back at the bootstrap spawn (finish tests teleport it to their own sites, which may be off the ground patch)
            if (Game.Player != null)
            {
                var cc = Game.Player.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                Game.Player.transform.position = new Vector3(0f, 0f, -4f);
                if (cc != null) cc.enabled = true;
                Game.Player.SetLook(0f, 0f);
            }
            yield return null;
        }

        public static Piece Lumber(string id, float lengthM, Vector3 center, Quaternion rot)
        {
            var p = Piece.Create(Catalog.Get(id), lengthM, center, rot);
            p.Fasten();
            return p;
        }

        public struct Opening { public float LeftIn, WidthIn, SillIn, HeadIn; } // RO measured from wall start / floor (top of subfloor)

        /// <summary>
        /// Stud wall: single bottom plate, double top plate (97 1/8" total), 2x4 studs 16" O.C. from <paramref name="start"/>
        /// (outside corner of the wall at floor level) running along <paramref name="dir"/>. The wall's depth (3.5") goes
        /// toward <paramref name="inward"/>. Openings get king+jack studs, a doubled 2x6 header (on edge), and a flat sill
        /// with cripples for windows (SillIn > 0). Returns all pieces.
        /// </summary>
        public static List<Piece> StudWall(Vector3 start, Vector3 dir, Vector3 inward, float lengthIn, params Opening[] openings)
        {
            dir.Normalize(); inward.Normalize();
            var ps = new List<Piece>();
            float t = Units.In(1.5f), w = Units.In(3.5f), L = Units.In(lengthIn);
            Vector3 mid = start + inward * (w * 0.5f);
            Quaternion plateRot = Quaternion.LookRotation(dir, Vector3.up);
            Quaternion studRot = Quaternion.LookRotation(Vector3.up, dir);
            Quaternion hdrRot = Quaternion.LookRotation(dir, Vector3.Cross(dir, Vector3.up));
            ps.Add(Lumber("pt2x4x8", L, mid + dir * (L * 0.5f) + Vector3.up * (t * 0.5f), plateRot));
            ps.Add(Lumber("2x4x8", L, mid + dir * (L * 0.5f) + Vector3.up * (Units.In(94.125f) + t * 0.5f), plateRot));
            ps.Add(Lumber("2x4x8", L, mid + dir * (L * 0.5f) + Vector3.up * (Units.In(95.625f) + t * 0.5f), plateRot));
            float studLen = Units.In(92.625f), studY = t + studLen * 0.5f;

            bool InOpening(float uIn)
            {
                foreach (var o in openings) if (uIn + 0.75f > o.LeftIn - 3f && uIn - 0.75f < o.LeftIn + o.WidthIn + 3f) return true;
                return false;
            }
            // layout studs 16" O.C. (first at the end, last at the end)
            var layout = new List<float> { 0.75f };
            for (float x = 16f; x < lengthIn - 1.5f; x += 16f) layout.Add(x);
            layout.Add(lengthIn - 0.75f);
            foreach (var uIn in layout)
                if (!InOpening(uIn)) ps.Add(Lumber("2x4x925", studLen, mid + dir * Units.In(uIn) + Vector3.up * studY, studRot));

            foreach (var o in openings)
            {
                float l = o.LeftIn, r = o.LeftIn + o.WidthIn;
                float hdrH = Units.In(5.5f);
                // king studs
                ps.Add(Lumber("2x4x925", studLen, mid + dir * Units.In(l - 2.25f) + Vector3.up * studY, studRot));
                ps.Add(Lumber("2x4x925", studLen, mid + dir * Units.In(r + 2.25f) + Vector3.up * studY, studRot));
                // jack studs (bottom plate to header)
                float jackLen = Units.In(o.HeadIn) - t;
                ps.Add(Lumber("2x4x925", jackLen, mid + dir * Units.In(l - 0.75f) + Vector3.up * (t + jackLen * 0.5f), studRot));
                ps.Add(Lumber("2x4x925", jackLen, mid + dir * Units.In(r + 0.75f) + Vector3.up * (t + jackLen * 0.5f), studRot));
                // header: two 2x6 on edge across jacks
                float hl = Units.In(o.WidthIn + 3f);
                Vector3 hc = mid + dir * Units.In((l + r) * 0.5f) + Vector3.up * (Units.In(o.HeadIn) + hdrH * 0.5f);
                ps.Add(Lumber("2x6x8", hl, hc + inward * Units.In(-0.875f), hdrRot));
                ps.Add(Lumber("2x6x8", hl, hc + inward * Units.In(0.875f), hdrRot));
                // top cripples between header and top plate
                float topCrip = Units.In(94.125f - o.HeadIn) - hdrH;
                for (float x = Mathf.Ceil(l / 16f) * 16f; x < r; x += 16f)
                    if (topCrip > Units.In(1f)) ps.Add(Lumber("2x4x925", topCrip, mid + dir * Units.In(x) + Vector3.up * (Units.In(o.HeadIn) + hdrH + topCrip * 0.5f), studRot));
                if (o.SillIn > 0f)
                {
                    ps.Add(Lumber("2x4x8", Units.In(o.WidthIn), mid + dir * Units.In((l + r) * 0.5f) + Vector3.up * (Units.In(o.SillIn) - t * 0.5f), plateRot));
                    float bc = Units.In(o.SillIn) - 2f * t;
                    for (float x = Mathf.Ceil(l / 16f) * 16f; x < r; x += 16f)
                        ps.Add(Lumber("2x4x925", bc, mid + dir * Units.In(x) + Vector3.up * (t + bc * 0.5f), studRot));
                }
            }
            return ps;
        }

        /// <summary>2x6 ceiling joists 16" O.C. resting on top plates at 97 1/8", spanning along <paramref name="span"/>
        /// for <paramref name="spanIn"/>, laid out along <paramref name="along"/> for <paramref name="runIn"/>.</summary>
        public static List<Piece> CeilingJoists(Vector3 corner, Vector3 along, Vector3 span, float runIn, float spanIn)
        {
            along.Normalize(); span.Normalize();
            var ps = new List<Piece>();
            float y = Units.In(97.125f + 2.75f);
            Quaternion rot = Quaternion.LookRotation(span, Vector3.Cross(span, Vector3.up));
            // rotate so the 5.5" dimension (local X) is vertical: LookRotation(span, horizontal) puts X = up
            var layout = new List<float> { 0.75f };
            for (float x = 16f; x < runIn - 1.5f; x += 16f) layout.Add(x);
            layout.Add(runIn - 0.75f);
            foreach (var a in layout)
                ps.Add(Lumber("2x6x12", Units.In(spanIn), corner + along * Units.In(a) + span * Units.In(spanIn * 0.5f) + Vector3.up * y, rot));
            return ps;
        }

        /// <summary>7/16" OSB sheathing, sheets standing vertical on the face of a wall on the side opposite <paramref name="inward"/>.</summary>
        public static List<Piece> Sheathing(Vector3 start, Vector3 dir, Vector3 inward, float lengthIn, float heightIn = 97.125f)
        {
            dir.Normalize(); inward.Normalize();
            var ps = new List<Piece>();
            float th = Units.In(0.4375f);
            Quaternion rot = Quaternion.LookRotation(Vector3.up, -inward);
            for (float x = 0; x < lengthIn - 0.5f; x += 48f)
            {
                float w = Mathf.Min(48f, lengthIn - x);
                var def = Catalog.Get("osb716");
                var p = Piece.Create(def, Units.In(heightIn), start - inward * (th * 0.5f) + dir * Units.In(x + w * 0.5f) + Vector3.up * Units.In(heightIn * 0.5f), rot);
                if (w < 48f) p.SetSize(new Vector3(Units.In(w), p.Size.y, p.Size.z));
                p.Fasten();
                ps.Add(p);
            }
            return ps;
        }

        /// <summary>A 3/4" plywood subfloor platform (flat sheets) with its top at y = 0 for floor tests.</summary>
        public static List<Piece> Subfloor(Vector3 corner, float widthIn, float depthIn)
        {
            var ps = new List<Piece>();
            float th = Units.In(0.75f);
            for (float x = 0; x < widthIn - 0.5f; x += 96f)
                for (float z = 0; z < depthIn - 0.5f; z += 48f)
                {
                    float w = Mathf.Min(96f, widthIn - x), d = Mathf.Min(48f, depthIn - z);
                    var p = Piece.Create(Catalog.Get("ply34"), Units.In(w), corner + new Vector3(Units.In(x + w * 0.5f), -th * 0.5f, Units.In(z + d * 0.5f)), Quaternion.LookRotation(Vector3.right, Vector3.up));
                    p.SetSize(new Vector3(Units.In(d), th, Units.In(w)));
                    p.Fasten(); ps.Add(p);
                }
            return ps;
        }

        public static Camera Cam(Vector3 pos, Vector3 lookAt, float fov = 60f)
        {
            var cam = new GameObject("FinishTestCam").AddComponent<Camera>();
            cam.transform.position = pos; cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.enabled = false;
            return cam;
        }

        public static void Shot(Camera cam, string name)
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

        /// <summary>Puts the player at feet position looking at a world point (so tools aim there).</summary>
        public static void AimPlayerAt(Vector3 feet, Vector3 target)
        {
            var pl = Game.Player;
            var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            pl.transform.position = feet;
            if (cc != null) cc.enabled = true;
            Vector3 eye = feet + Vector3.up * PlayerRig.EyeHeight;
            Vector3 d = target - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            pl.SetLook(yaw, pitch);
        }

        /// <summary>Electrical box stand-in: a 2x3x3.5" blue box nailed to the side of a stud, front 1/2" proud of the face.</summary>
        public static GameObject OutletBox(Vector3 faceCenter, Vector3 outward)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ElectricalBox";
            go.transform.localScale = Vector3.one; // collider sized below
            Object.Destroy(go.GetComponent<MeshFilter>()); Object.Destroy(go.GetComponent<MeshRenderer>());
            var bc = go.GetComponent<BoxCollider>();
            Vector3 size = new Vector3(Units.In(2f), Units.In(3f), Units.In(2.75f));
            bc.size = size;
            go.transform.rotation = Quaternion.LookRotation(-outward, Vector3.up);
            go.transform.position = faceCenter + outward * (Units.In(0.5f) - size.z * 0.5f);
            var b = new MeshBuilder();
            b.AddBox(Vector3.zero, size, 0);
            var v = new GameObject("Visual"); v.transform.SetParent(go.transform, false);
            v.AddComponent<MeshFilter>().sharedMesh = b.Build("box");
            v.AddComponent<MeshRenderer>().sharedMaterial = Materials.Get(SurfaceKind.DarkPlastic, new Color(0.25f, 0.45f, 1.6f));
            return go;
        }
    }
}
