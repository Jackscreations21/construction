using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Static landscaping actions (used by the hands tool and by tests/sandbox).</summary>
    public static class Landscape
    {
        public static readonly float WalkWidth = 3 * Units.Foot, DriveWidth = 10 * Units.Foot;
        public static readonly float SodW = 2 * Units.Foot, SodL = 5 * Units.Foot;
        public static readonly float BlockW = Units.In(11.75f), BlockH = Units.In(4), Setback = Units.In(0.75f);

        static Vector3 OnGround(Vector3 p) { p.y = Grade.HeightAt(p.x, p.z); return p; }

        /// <summary>Stakes out a run/rect job (walk, drive, fence, gate) from a to b.</summary>
        public static LandscapeFeature LayOut(LandscapeKind kind, Vector3 a, Vector3 b)
        {
            a = OnGround(a); b = OnGround(b);
            if (kind == LandscapeKind.Gate)
            {
                var d = new Vector3(b.x - a.x, 0, b.z - a.z);
                if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
                b = OnGround(a + d.normalized * (4 * Units.Foot + Units.In(3.5f) + Units.In(1)));
            }
            if (Vector3.Distance(a, b) < 0.5f) return null;
            float w = kind == LandscapeKind.Walkway ? WalkWidth : kind == LandscapeKind.GravelDrive ? DriveWidth : 0f;
            return LandscapeFeature.Create(kind, null, a, b, w, 0);
        }

        public static LandscapeFeature MarkPlant(string itemId, Vector3 p) => LandscapeFeature.Create(LandscapeKind.Plant, itemId, OnGround(p), OnGround(p), 0, 0);

        /// <summary>Lays one sod roll. Needs prepared soil (dirt/topsoil). Snaps into staggered rows next to existing rolls.</summary>
        public static LandscapeFeature LaySod(Vector3 p, float yaw, out string msg)
        {
            Grade.Ensure(p);
            var cover = Grade.CoverAt(p.x, p.z);
            if (cover != GroundCover.Dirt && cover != GroundCover.Topsoil) { msg = "Sod goes on raked bare soil - strip the turf and spread topsoil first"; return null; }
            // snap to the nearest roll's running-bond pattern
            LandscapeFeature near = null; float best = 2.2f;
            foreach (var f in LandscapeFeature.All)
                if (f.Kind == LandscapeKind.Sod) { float d = Vector3.Distance(f.A, p); if (d < best) { best = d; near = f; } }
            if (near != null)
            {
                yaw = near.Yaw;
                var rot = Quaternion.Euler(0, yaw, 0);
                var l = Quaternion.Inverse(rot) * (p - near.A);
                int row = Mathf.RoundToInt(l.x / SodW);
                float stagger = (row & 1) != 0 ? SodL / 2 : 0f;
                l.x = row * SodW; l.z = Mathf.Round((l.z - stagger) / SodL) * SodL + stagger;
                p = near.A + rot * new Vector3(l.x, 0, l.z);
                foreach (var f in LandscapeFeature.All)
                    if (f.Kind == LandscapeKind.Sod && new Vector2(f.A.x - p.x, f.A.z - p.z).magnitude < 0.2f) { msg = "Already sodded here"; return null; }
            }
            if (!Game.Stock.TryTake("sod-roll", 1)) { msg = "No sod rolls on site (Garden Center: Lawn)"; return null; }
            p = OnGround(p);
            var feat = LandscapeFeature.Create(LandscapeKind.Sod, "sod-roll", p, p, 0, yaw);
            msg = "Sod laid - water it in";
            Sfx.Play("thud", p, 0.5f);
            return feat;
        }

        /// <summary>Places a retaining wall block on a gravel leveling pad, or stacks it on an aimed block (3/4" setback).</summary>
        public static LandscapeFeature PlaceBlock(Vector3 p, float yaw, LandscapeFeature stackOn, out string msg)
        {
            Vector3 pos;
            if (stackOn != null && stackOn.Kind == LandscapeKind.RetainingBlock)
            {
                yaw = stackOn.Yaw;
                pos = stackOn.A + Vector3.up * BlockH + Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, -Setback);
            }
            else
            {
                Grade.Ensure(p);
                if (Grade.CoverAt(p.x, p.z) != GroundCover.Gravel && !GameSettings.Relaxed) { msg = "Set the first course on a compacted gravel leveling pad (wheelbarrow gravel)"; return null; }
                pos = OnGround(p) + Vector3.down * Units.In(1); // first course partly buried
                // continue a course: snap beside the nearest block at the same level
                foreach (var f in LandscapeFeature.All)
                {
                    if (f.Kind != LandscapeKind.RetainingBlock || Mathf.Abs(f.A.y - pos.y) > BlockH * 0.5f) continue;
                    var rot = Quaternion.Euler(0, f.Yaw, 0);
                    var l = Quaternion.Inverse(rot) * (pos - f.A);
                    if (Mathf.Abs(l.z) > 0.25f || Mathf.Abs(l.x) > BlockW * 1.7f || Mathf.Abs(l.x) < BlockW * 0.3f) continue;
                    yaw = f.Yaw; pos = f.A + rot * new Vector3(Mathf.Sign(l.x) * BlockW, 0, 0); break;
                }
            }
            foreach (var f in LandscapeFeature.All)
                if (f.Kind == LandscapeKind.RetainingBlock && Vector3.Distance(f.A, pos) < BlockW * 0.4f) { msg = "A block is already there"; return null; }
            if (!Game.Stock.TryTake("retaining-block", 1)) { msg = "No retaining wall blocks on site"; return null; }
            msg = "Block set";
            Sfx.Play("thud", pos, 0.7f);
            return LandscapeFeature.Create(LandscapeKind.RetainingBlock, "retaining-block", pos, pos, 0, yaw);
        }

        public static LandscapeFeature PlacePoint(LandscapeKind kind, string stockId, Vector3 p, float yaw, out string msg)
        {
            if (!Game.Stock.TryTake(stockId, 1)) { msg = $"No {LandCatalog.Name(stockId)} on site"; return null; }
            p = OnGround(p);
            msg = LandCatalog.Name(stockId) + " installed";
            Sfx.Play("thud", p, 0.6f);
            return LandscapeFeature.Create(kind, stockId, p, p, 0, yaw);
        }
    }

    /// <summary>Hands + layout kit: lay out jobs and set plants, sod, blocks, lights, mailbox, tarps.
    /// RMB / Shift+RMB = choose item, scroll = rotate, LMB = place (or do the aimed job's hands step).</summary>
    public class LandHandsTool : LandTool
    {
        public override string Id => "landhands";
        public override string Name => "Landscaping (hands)";

        static readonly string[] Items = { "Paver walkway 3'", "Gravel drive 10'", "Privacy fence 6'", "Gate 4'",
            "Red maple", "Blue spruce", "Boxwood", "Hydrangea", "Hosta", "Sod roll", "Retaining block", "Path light", "Mailbox", "Tarp a material pile" };
        public int Item;
        float _yaw;
        Vector3? _first;

        public override string Hint => $"[{Items[Item]}] RMB next item · scroll rotate · LMB {(IsRun ? (_first.HasValue ? "click END point" : "click START point") : "place")}";
        bool IsRun => Item <= 3;

        public override void OnUnequip() { _first = null; }

        public override void Tick()
        {
            if (Game.UIOpen || GInput.GetKey(KeyCode.Tab)) return;
            float s = GInput.Scroll;
            if (s != 0) _yaw = Mathf.Repeat(_yaw + s * (Game.Snap.AngleStep > 0 ? Game.Snap.AngleStep : 15f), 360f);
        }

        public override void Secondary()
        {
            Item = (Item + (GInput.Shift ? Items.Length - 1 : 1)) % Items.Length;
            _first = null;
            Say(Items[Item]);
        }

        public override void Primary()
        {
            if (!Aim(out var hit, out var f)) return;
            if (TryWorkFeature(f)) return;
            Vector3 p = Game.Snap.SnapPosition(hit.point);
            p.y = hit.point.y;
            string msg = "";
            if (IsRun)
            {
                if (!_first.HasValue) { _first = p; Say("Start staked - click the end point"); return; }
                var kind = Item == 0 ? LandscapeKind.Walkway : Item == 1 ? LandscapeKind.GravelDrive : Item == 2 ? LandscapeKind.Fence : LandscapeKind.Gate;
                var feat = Landscape.LayOut(kind, _first.Value, p);
                _first = null;
                Say(feat != null ? feat.Hint : "Too short");
                return;
            }
            switch (Item)
            {
                case 4: case 5: case 6: case 7: case 8:
                    var plant = Landscape.MarkPlant(PlantSpec.Ids[Item - 4], p);
                    msg = plant.Hint; break;
                case 9: Landscape.LaySod(p, _yaw, out msg); break;
                case 10: Landscape.PlaceBlock(p, _yaw, f, out msg); break;
                case 11: Landscape.PlacePoint(LandscapeKind.PathLight, "path-light", p, _yaw, out msg); break;
                case 12: Landscape.PlacePoint(LandscapeKind.Mailbox, "mailbox-kit", p, _yaw, out msg); break;
                case 13:
                    var pile = hit.collider.GetComponentInParent<MaterialPile>();
                    if (pile == null)
                    {
                        foreach (var mp in Object.FindObjectsByType<MaterialPile>(FindObjectsSortMode.None))
                            if (Vector3.Distance(mp.transform.position, hit.point) < 1.5f) { pile = mp; break; }
                    }
                    msg = pile == null ? "Aim at a material pile" : Weather.ToggleTarp(pile.DefinitionId);
                    break;
            }
            Say(msg);
        }

        public override void DrawGUI()
        {
            base.DrawGUI();
            if (_first.HasValue && Aim(out var hit, out _))
                UISkin.InfoBox($"Run length {Units.Format(Vector3.Distance(_first.Value, hit.point))}", Screen.height * 0.45f);
        }
    }
}
