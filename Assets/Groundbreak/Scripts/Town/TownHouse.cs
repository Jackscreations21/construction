using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Places on a customer house where a repair can happen. Each has an anchor pose (see <see cref="TownHouse.Anchor"/>)
    /// and, for some, a healthy "fixture" object the repair feature hides while its own defect model is there.</summary>
    public enum HouseSpot
    {
        // wall mounted: position on the finished wall face, +Z out of the wall toward the viewer, +Y up
        KitchenSink,     // floor-level centre of the 36" sink base cabinet's open front; cabinet interior is behind (-Z). No fixture (cabinet is part of the house).
        BathVanity,      // same convention, 30" vanity
        OutletLiving,    // centre of a duplex receptacle box, 16" above the floor. Fixture: cover plate.
        OutletKitchen,   // centre of the countertop GFCI receptacle, 44" above the floor. Fixture: cover plate.
        BreakerPanel,    // centre of the 14"x30" load centre door, 60" above the floor. Fixture: panel.
        DrywallLiving,   // centre of a clear 4'x4' area of wall, 48" above the floor. No fixture.
        DrywallBedroom,  // same, in the bedroom
        InteriorDoor,    // floor-level centre of the 32"x80" bedroom door opening, mid wall thickness, +Z into the living room. Fixture: door slab.
        FrontDoor,       // floor-level centre of the 36"x80" entry opening, +Z outdoors. Fixture: door slab.
        WindowCaulk,     // exterior: centre of the living room window's sill line on the outside wall face, +Z outdoors. No fixture.
        TubCaulk,        // centre of the tub-to-wall joint (60" long), +X along the joint, +Z into the room. No fixture.
        // horizontal: +Y up
        DeckBoard,       // centre of the top face of one 5/4x6 deck board, +Z along the board. Fixture: the board (length = TownHouse.DeckBoardLength).
        SidewalkSquare,  // centre of the top face of one 5'x5'x4" public sidewalk square in front of the lot, +Z toward the house. Fixture: the square.
        FencePicket,     // base centre of one 5/8"x5-1/2"x6' dog-ear picket at ground level, +Z toward the house, +Y up. Fixture: the picket.
        // roof / eave
        RoofShingles,    // point on the front roof plane about 1 m up-slope from the eave: +Y roof normal, +Z up-slope, +X along the eave. Clear 1.5 m x 1 m. No fixture.
        Gutter,          // centre of the top of the front 5" K-style gutter: +X along the gutter, +Z away from the fascia, +Y up. No fixture.
        LadderFront,     // ground point below the front eave where an extension ladder stands: +Z away from the house. No fixture.
    }

    /// <summary>A procedural customer house. Built by <c>HouseBuilder</c>; the repair feature mounts defects at <see cref="Anchor"/> poses.
    /// House frame: origin at ground level in the middle of the footprint, +Z = front (toward the street), floor at y = <see cref="FloorY"/>.</summary>
    public class TownHouse : MonoBehaviour
    {
        /// <summary>Front wall is this far back from the street lot line; the front door is this far right of the lot centreline (lot-local x).</summary>
        public const float Setback = 9f, FrontDoorX = -1.5f;

        public TownLot Lot;
        public float Width, Depth;             // footprint (m), X and Z
        public float FloorY = 0.2f;            // finished floor above grade
        public float CeilingHeight = 2.44f;    // 8'
        public float EaveHeight;               // top of the gutter above grade
        public float DeckBoardLength;
        public float WindowWidth = 1.2f;
        public Color Siding = Color.white;

        readonly Dictionary<HouseSpot, Pose> _local = new Dictionary<HouseSpot, Pose>();
        readonly Dictionary<HouseSpot, GameObject> _fixtures = new Dictionary<HouseSpot, GameObject>();

        /// <summary>World pose of a repair spot (conventions on <see cref="HouseSpot"/>).</summary>
        public Pose Anchor(HouseSpot spot)
        {
            _local.TryGetValue(spot, out var p);
            return new Pose(transform.TransformPoint(p.position), transform.rotation * p.rotation);
        }

        public bool Has(HouseSpot spot) => _local.ContainsKey(spot);

        /// <summary>The healthy fixture object at a spot (door slab, deck board, cover plate...), or null.</summary>
        public GameObject Fixture(HouseSpot spot) { _fixtures.TryGetValue(spot, out var g); return g; }

        /// <summary>Hide the healthy fixture while a defect model stands in for it; show it again when repaired (or keep your repaired model).</summary>
        public void SetFixtureVisible(HouseSpot spot, bool visible)
        {
            if (_fixtures.TryGetValue(spot, out var g) && g != null) g.SetActive(visible);
        }

        /// <summary>World-space box of the heated interior (to test "is the player inside").</summary>
        public Bounds InteriorBounds
        {
            get
            {
                var b = new Bounds(transform.TransformPoint(new Vector3(0, FloorY + CeilingHeight / 2f, 0)), Vector3.zero);
                foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
                    b.Encapsulate(transform.TransformPoint(new Vector3(sx * Width / 2f, FloorY + (sx > 0 ? CeilingHeight : 0f), sz * Depth / 2f)));
                return b;
            }
        }

        // ---- used by HouseBuilder
        public void SetAnchor(HouseSpot spot, Vector3 localPos, Quaternion localRot) => _local[spot] = new Pose(localPos, localRot);
        public void SetFixture(HouseSpot spot, GameObject go) => _fixtures[spot] = go;
    }
}
