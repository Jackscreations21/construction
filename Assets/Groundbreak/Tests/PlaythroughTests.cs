using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Groundbreak.Tests
{
    /// <summary>Scripted play-through of milestone 1. Fails on any logged error or exception.
    /// Saves screenshots to Logs/shots/.</summary>
    public class PlaythroughTests
    {
        static int ToolSlot(string id)
        {
            for (int i = 0; i < ToolBelt.SlotCount; i++)
                if (Game.Belt.Slots[i] != null && Game.Belt.Slots[i].Id == id) return i;
            return -1;
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1280, 720, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Logs", "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        static Camera Overview(Vector3 pos, Vector3 lookAt)
        {
            var cam = new GameObject("TestOverviewCam").AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.enabled = false;
            return cam;
        }

        [UnityTest]
        public IEnumerator Milestone1Playthrough()
        {
            yield return Frames(10);

            // --- Boot ---
            Assert.NotNull(Game.Placement, "PlacementController missing");
            Assert.NotNull(Game.Player, "PlayerRig missing");
            Assert.NotNull(Game.Belt, "ToolBelt missing");
            Assert.NotNull(Tablet.I, "Tablet missing");
            Assert.NotNull(DeliveryService.I, "DeliveryService missing");
            Assert.NotNull(GameClock.I, "GameClock missing");
            Assert.IsTrue(Object.FindObjectsByType<MaterialPile>(FindObjectsSortMode.None).Length > 0, "no material piles");
            Shot(Game.Player.Cam, "01_start_view");

            // --- Place a 2x4 with the place tool ---
            int place = ToolSlot("place");
            Assert.GreaterOrEqual(place, 0, "place tool not on belt");
            Game.Belt.Select(place);
            yield return Frames(2);
            Assert.IsTrue(Game.Placement.Active, "placement not active after equipping place tool");

            Game.Player.SetLook(0f, 45f);
            yield return Frames(3);
            var def = Game.Placement.Selected;
            Assert.NotNull(def, "no material selected");
            int stockBefore = Game.Stock.Count(def.Id);
            int piecesBefore = Object.FindObjectsByType<Piece>(FindObjectsSortMode.None).Length;
            Debug.Log("[Test] status before place: " + Game.Placement.StatusLine);
            Assert.IsTrue(Game.Placement.Place(), "Place() failed: " + Game.Placement.StatusLine);
            yield return Frames(2);
            Assert.AreEqual(piecesBefore + 1, Object.FindObjectsByType<Piece>(FindObjectsSortMode.None).Length, "piece not created");
            Assert.AreEqual(stockBefore - 1, Game.Stock.Count(def.Id), "stock not decremented");

            // --- Repeat (F) a few times: a run of pieces at 16" O.C. ---
            for (int i = 0; i < 4; i++) { Game.Placement.Repeat(); yield return Frames(2); }
            int afterRepeat = Object.FindObjectsByType<Piece>(FindObjectsSortMode.None).Length;
            Debug.Log("[Test] pieces after repeat: " + afterRepeat);
            Assert.AreEqual(piecesBefore + 5, afterRepeat, "Repeat() should lay 4 more boards at 16\" O.C.");
            var all = Object.FindObjectsByType<Piece>(FindObjectsSortMode.None);
            float minGap = float.MaxValue;
            for (int a = 0; a < all.Length; a++)
                for (int b = a + 1; b < all.Length; b++)
                    minGap = Mathf.Min(minGap, Vector3.Distance(all[a].transform.position, all[b].transform.position));
            Assert.AreEqual(16f, Units.ToInches(minGap), 0.1f, "nearest repeated board should be 16\" on center");

            // --- Snap cycling ---
            int g = Game.Snap.GridIndex; Game.Snap.CycleGrid(1); Assert.AreNotEqual(g, Game.Snap.GridIndex); Game.Snap.CycleGrid(-1);

            // --- Hammer the aimed piece until fastened ---
            int hammer = ToolSlot("hammer");
            Assert.GreaterOrEqual(hammer, 0, "hammer not on belt");
            Game.Belt.Select(hammer);
            yield return Frames(2);
            Assert.IsFalse(Game.Placement.Active, "placement still active with hammer");
            Game.Player.TryAim(out var hit, 6f);
            var target = hit.collider != null ? hit.collider.GetComponent<Piece>() : null;
            Assert.NotNull(target, "hammer is not aiming at a piece (hit: " + (hit.collider ? hit.collider.name : "nothing") + ")");
            for (int i = 0; i < 12 && target.State != PieceState.Fastened; i++)
            {
                Game.Belt.Active.Primary();
                yield return new WaitForSeconds(0.4f);
            }
            Assert.AreEqual(PieceState.Fastened, target.State, "piece not fastened after hammering, nails=" + target.Nails);

            Shot(Game.Player.Cam, "02_after_placing");

            // --- Tablet open/close ---
            Tablet.I.Open();
            yield return Frames(2);
            Assert.IsTrue(Game.UIOpen, "tablet open did not set UIOpen");
            Tablet.I.Close();
            yield return Frames(3);
            Assert.IsFalse(Game.UIOpen, "tablet close did not clear UIOpen");

            // --- Rush delivery ---
            int boards = Game.Stock.Count("2x6x8");
            DeliveryService.I.Order(new Dictionary<string, int> { { "2x6x8", 5 } }, true);
            float until = Time.time + 60f;
            bool shotTruck = false;
            var overview = Overview(new Vector3(10f, 7f, -14f), new Vector3(2f, 0f, 0f));
            while (Game.Stock.Count("2x6x8") < boards + 5 && Time.time < until)
            {
                if (!shotTruck && DeliveryService.I.Pending.Count > 0 && DeliveryService.I.Pending[0].ETA < 3f) { Shot(overview, "03_truck_arriving"); shotTruck = true; }
                yield return null;
            }
            Assert.AreEqual(boards + 5, Game.Stock.Count("2x6x8"), "delivery never arrived");
            yield return Frames(5);
            Shot(overview, "04_overview");
            Debug.Log("[Test] money left: " + Game.Wallet.Money);
        }

        /// <summary>Builds every catalog item in a showroom grid and photographs it, so models/textures can be reviewed.</summary>
        [UnityTest]
        public IEnumerator Showroom()
        {
            yield return Frames(5);
            var origin = new Vector3(-30f, 0f, 20f);
            float x = 0f, z = 0f, rowDepth = 0f;
            var items = new List<PieceDefinition>();
            foreach (var d in Catalog.All) if (d.IsPlaceable) items.Add(d);
            int built = 0;
            foreach (var d in items)
            {
                Vector3 s = d.Size;
                bool flat = !d.IsProduct; // lumber & sheets lie flat
                float w = s.x, depth = flat ? s.z : s.z;
                if (x + w > 14f) { x = 0f; z += rowDepth + 0.8f; rowDepth = 0f; }
                float yc = s.y * 0.5f;
                if (d.Id == "win-skylight" || d.Id == "hvac-bathfan" || d.Id.StartsWith("el-ceiling") || d.Id == "el-smoke") yc += 1.2f; // ceiling-ish items shown raised
                var p = Piece.Create(d, 0f, origin + new Vector3(x + w * 0.5f, yc, z + depth * 0.5f), Quaternion.identity);
                Assert.NotNull(p.GetComponentInChildren<MeshRenderer>(), "no visual for " + d.Id);
                var mf = p.GetComponentInChildren<MeshFilter>();
                Assert.NotNull(mf != null ? mf.sharedMesh : null, "no mesh for " + d.Id);
                Assert.Greater(mf.sharedMesh.vertexCount, 0, "empty mesh for " + d.Id);
                x += w + 0.4f; rowDepth = Mathf.Max(rowDepth, depth);
                built++;
            }
            yield return Frames(3);
            Debug.Log("[Test] showroom items: " + built);

            Shot(Overview(origin + new Vector3(7f, 6f, -7f), origin + new Vector3(7f, 0.5f, 6f)), "10_showroom_overview");
            Shot(Overview(origin + new Vector3(3f, 1.7f, -3.5f), origin + new Vector3(3f, 1.0f, 1f)), "11_showroom_front_left");
            Shot(Overview(origin + new Vector3(11f, 1.7f, -3.5f), origin + new Vector3(11f, 1.0f, 1f)), "12_showroom_front_right");
            Shot(Overview(origin + new Vector3(7f, 2.2f, z - 2f), origin + new Vector3(7f, 0.8f, z + 2f)), "13_showroom_back");

            // Close-ups of the material piles (OSB + lumber)
            Transform osb = null, lumber = null;
            foreach (var pile in Object.FindObjectsByType<MaterialPile>(FindObjectsSortMode.None))
            {
                if (pile.DefinitionId == "osb716") osb = pile.transform;
                if (pile.DefinitionId == "2x4x8") lumber = pile.transform;
            }
            if (osb != null) Shot(Overview(osb.position + new Vector3(1.4f, 1.3f, -1.6f), osb.position + new Vector3(0f, 0.15f, 0f)), "14_osb_closeup");
            if (lumber != null) Shot(Overview(lumber.position + new Vector3(0.9f, 0.8f, -1.8f), lumber.position + new Vector3(0f, 0.2f, -0.9f)), "15_lumber_closeup");
            var truckCam = Overview(new Vector3(0f, 2.5f, -8f), new Vector3(0f, 1f, 0f));
            var truck = new GameObject("ShowTruck");
            ProductModels.BuildFlatbedTruck(truck.transform);
            truck.transform.position = new Vector3(-3f, 0f, -2f);
            truck.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            yield return null;
            Shot(truckCam, "16_truck");
        }
    }
}
