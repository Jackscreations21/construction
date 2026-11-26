# Hand-off: mep-site-details — branch `claude/mep-site-details-92f734`

## Done (one commit per item)
1. **Electrical panel + devices** (`30d17bd`): `Electrical/ElPanelLook.cs` builds the 200 A load center: knockouts, NM connectors,
   bus stabs, rails, the main breaker with lugs, breakers with amp-printed handles, AFCI/GFCI test buttons, 2-pole ties,
   neutral bars with a bonding screw, a ground bar with the GEC, and the sheath stripped inside the can.
   `ElPanel.Cover` (Shift+E on the panel) adds the dead-front: windows only at installed breakers, space numbers, nameplate,
   screws, and the open door with the circuit directory. `ElLabels.cs` is a generic text-decal helper.
   Devices get mounting screws and back-wire holes. `ElBox.Plate` (pliers on a box with a device) adds a duplex, toggle or
   decorator plate. Boxes get ribs and clamp flaps.
   **Behaviour change:** `ElPanel.SpaceLocal` now puts odd spaces on the viewer's left and breaker faces at the can front.
2. **Duct seams** (`5d43ab7`): `HVAC/DuctDetail.cs`, driven from `HvacSystem.LateUpdate`, adds seams, slips/cleats,
   mastic, crimps, screws, foil tape when sealed, and the R-8 attic wrap (over joists, outside rooms, or tag "attic").
   The takeoff collar gets flange screws and mastic.
3. **Dust** (`abb45bd`): `SiteLook/SiteFx.cs` rewritten. Dust scales with the action and drifts with `SiteFx.Wind`
   (Weather has no wind, so this is a site breeze; `WindOverride` exists for tests). The excavator uses `Dig(p, 1.8f)`.
4. **Water heaters** (`b6c2171`): `Plumbing/WaterHeaterTrim.cs` adds the unions, cold ball valve, gas cock + union,
   MC whip, drain valve and tankless service valves.

Tests: `Tests/MepDetailTests.cs` (4 tests, shots `Logs/shots/detail_01..22`).

## Shared files
None in Core or Visuals. One line in `HVAC/HvacInspection.cs` (my area): it calls `DuctDetail.Apply()`.
Unity generated `Scripts/Business.meta` and `Business/Company.cs.meta`; they are left untracked for the business session.

## Not done / ideas
- Duct mastic still reads as flat gray strips; it could use a smeared texture.
- Fixture details beyond the water heaters were not touched (the toilet already has an angle stop).
