using UnityEngine;
using HB = Groundbreak.HouseBuilder;

namespace Groundbreak
{
    /// <summary>
    /// Named work locations on a town house, built from the house's HouseSpot anchors and its documented layout (HouseBuilder: living left,
    /// bedroom right of the partition at x = P, bath in the back right, kitchen on the back wall at -X, front at +Z). Positions are
    /// house-local, so they follow any house size. A house qualifies when it has every spot in <see cref="JobPlace.Needs"/>.
    /// </summary>
    public static class JobPlaces
    {
        static float F(TownHouse h) => h.FloorY;
        static float Ceil(TownHouse h) => h.FloorY + h.CeilingHeight;
        static float Hw(TownHouse h) => h.Width / 2f;
        static float Hd(TownHouse h) => h.Depth / 2f;
        static Vector3 Local(TownHouse h, HouseSpot s) => h.transform.InverseTransformPoint(h.Anchor(s).position);
        static Quaternion LocalRot(TownHouse h, HouseSpot s) => Quaternion.Inverse(h.transform.rotation) * h.Anchor(s).rotation;
        static readonly Quaternion Front = Quaternion.identity, Back = Quaternion.LookRotation(Vector3.back),
            ToMinusX = Quaternion.LookRotation(Vector3.left), ToPlusX = Quaternion.LookRotation(Vector3.right);
        static float LivingWinX(TownHouse h) => (-Hw(h) + HB.T + TownHouse.FrontDoorX - 0.46f) / 2f;

        static JobPlace P(string name, HouseSpot spot, string room, System.Func<TownHouse, Pose> place = null, bool outdoor = false, bool ladder = false, bool hide = false) =>
            new JobPlace { Name = name, Spot = spot, Needs = new[] { spot }, Room = room, Place = place, Outdoor = outdoor, Ladder = ladder, HideFixture = hide };

        // ---- bath
        public static readonly JobPlace Toilet = P("toilet", HouseSpot.BathVanity, "bath", h => new Pose(new Vector3(HB.P + 1.0f, F(h), -0.6f - HB.Ti / 2f), Back));
        public static readonly JobPlace Vanity = P("vanity", HouseSpot.BathVanity, "bath");
        public static readonly JobPlace TubWall = P("tub wall", HouseSpot.TubCaulk, "bath");
        public static readonly JobPlace TubValve = P("tub valve", HouseSpot.TubCaulk, "bath", h => new Pose(Local(h, HouseSpot.TubCaulk), LocalRot(h, HouseSpot.TubCaulk)));
        public static readonly JobPlace BathFloor = P("bath floor", HouseSpot.BathVanity, "bath", h => new Pose(new Vector3(HB.P + 0.7f, F(h), -2.3f), Front));
        public static readonly JobPlace BathCeiling = P("bath ceiling", HouseSpot.BathVanity, "bath", h => new Pose(new Vector3(HB.P + 0.9f, Ceil(h), -1.7f), Front));
        // ---- kitchen
        public static readonly JobPlace KitchenSink = P("kitchen sink", HouseSpot.KitchenSink, "kitchen");
        public static readonly JobPlace KitchenCabinet = P("kitchen cabinet", HouseSpot.KitchenSink, "kitchen", h => { var p = Local(h, HouseSpot.KitchenSink); return new Pose(p + new Vector3(0.65f, 0, 0), Front); });
        public static readonly JobPlace KitchenGfci = P("kitchen GFCI", HouseSpot.OutletKitchen, "kitchen", hide: true);
        // ---- mechanical wall (living side of the partition, back half)
        public static readonly JobPlace Panel = P("breaker panel", HouseSpot.BreakerPanel, "living", hide: true);
        public static readonly JobPlace WaterHeater = P("water heater", HouseSpot.BreakerPanel, "utility", h => new Pose(new Vector3(HB.P - HB.Ti / 2f, F(h), -Hd(h) + HB.T + 0.9f), ToMinusX));
        public static readonly JobPlace Furnace = P("furnace", HouseSpot.BreakerPanel, "utility", h => new Pose(new Vector3(HB.P - HB.Ti / 2f, F(h), -2.3f), ToMinusX));
        public static readonly JobPlace Thermostat = P("thermostat", HouseSpot.BreakerPanel, "living", h => new Pose(new Vector3(HB.P - HB.Ti / 2f, F(h) + 1.52f, -1.35f), ToMinusX));
        // ---- living / bedroom
        public static readonly JobPlace LivingOutlet = P("living room receptacle", HouseSpot.OutletLiving, "living", hide: true);
        public static readonly JobPlace NewOutlet = P("new receptacle", HouseSpot.OutletLiving, "living", h => { var p = Local(h, HouseSpot.OutletLiving); return new Pose(new Vector3(p.x, p.y, -1.0f), ToPlusX); });
        public static readonly JobPlace BedroomOutlet = P("bedroom receptacle", HouseSpot.DrywallBedroom, "bedroom", h => new Pose(new Vector3(Hw(h) - HB.T, F(h) + 0.406f, 0.3f), ToMinusX));
        public static readonly JobPlace EntrySwitch = P("light switch", HouseSpot.FrontDoor, "living", h => new Pose(new Vector3(TownHouse.FrontDoorX + 0.65f, F(h) + 1.22f, Hd(h) - HB.T), Back));
        public static readonly JobPlace LivingCeiling = P("living room ceiling", HouseSpot.OutletLiving, "living", h => new Pose(new Vector3((-Hw(h) + HB.P) / 2f, Ceil(h), 1.0f), Front));
        public static readonly JobPlace HallCeiling = P("hall ceiling", HouseSpot.InteriorDoor, "living", h => new Pose(new Vector3(HB.P - 0.6f, Ceil(h), 0.5f), Front));
        public static readonly JobPlace BedroomCeiling = P("bedroom ceiling", HouseSpot.DrywallBedroom, "bedroom", h => new Pose(new Vector3((HB.P + Hw(h)) / 2f, Ceil(h), 1.6f), Front));
        public static readonly JobPlace BedroomCeilingFront = P("bedroom ceiling (front)", HouseSpot.DrywallBedroom, "bedroom", h => new Pose(new Vector3((HB.P + Hw(h)) / 2f + 0.3f, Ceil(h), Hd(h) - 1.3f), Front));
        public static readonly JobPlace LivingWall = P("living room wall", HouseSpot.DrywallLiving, "living");
        public static readonly JobPlace BedroomWall = P("bedroom wall", HouseSpot.DrywallBedroom, "bedroom");
        public static readonly JobPlace LivingWallFull = P("living room walls", HouseSpot.DrywallLiving, "living", h => { var p = Local(h, HouseSpot.DrywallLiving); return new Pose(new Vector3(p.x, F(h), p.z), ToPlusX); });
        public static readonly JobPlace BedroomWallFull = P("bedroom walls", HouseSpot.DrywallBedroom, "bedroom", h => { var p = Local(h, HouseSpot.DrywallBedroom); return new Pose(new Vector3(p.x, F(h), p.z), ToMinusX); });
        public static readonly JobPlace BedroomBase = P("bedroom baseboard", HouseSpot.DrywallBedroom, "bedroom", h => new Pose(new Vector3(Hw(h) - HB.T, F(h), -0.1f), ToMinusX));
        public static readonly JobPlace LivingCorner = P("hall corner", HouseSpot.InteriorDoor, "living", h => new Pose(new Vector3(HB.P - HB.Ti / 2f, F(h), 2.2f), ToMinusX));
        public static readonly JobPlace BedroomFloor = P("bedroom floor", HouseSpot.DrywallBedroom, "bedroom", h => new Pose(new Vector3((HB.P + Hw(h)) / 2f, F(h), 0.6f), Front));
        public static readonly JobPlace LivingFloor = P("living room floor", HouseSpot.OutletLiving, "living", h => new Pose(new Vector3((-Hw(h) + HB.P) / 2f - 0.4f, F(h), 0.3f), Front));
        public static readonly JobPlace BedroomDoor = P("bedroom door", HouseSpot.InteriorDoor, "bedroom");
        public static readonly JobPlace LivingWindow = P("living room window", HouseSpot.WindowCaulk, "living", h => new Pose(new Vector3(LivingWinX(h), F(h) + 0.9f, Hd(h) - HB.T), Back));
        public static readonly JobPlace BedroomWindow = P("bedroom window", HouseSpot.WindowCaulk, "bedroom", h => new Pose(new Vector3((HB.P + Hw(h)) / 2f, F(h) + 0.9f, Hd(h) - HB.T), Back));
        // ---- exterior
        public static readonly JobPlace WindowSill = P("living window sill", HouseSpot.WindowCaulk, "exterior", outdoor: true);
        public static readonly JobPlace BedroomSill = P("bedroom window sill", HouseSpot.WindowCaulk, "exterior", h => new Pose(new Vector3((HB.P + Hw(h)) / 2f, F(h) + 0.9f, Hd(h)), Front), outdoor: true);
        public static readonly JobPlace EntryTrim = P("entry door trim", HouseSpot.FrontDoor, "exterior", outdoor: true);
        public static readonly JobPlace Doorbell = P("doorbell", HouseSpot.FrontDoor, "exterior", h => new Pose(new Vector3(TownHouse.FrontDoorX + 0.65f, F(h) + 1.2f, Hd(h)), Front), outdoor: true);
        public static readonly JobPlace PorchLight = P("porch light", HouseSpot.FrontDoor, "exterior", h => new Pose(new Vector3(TownHouse.FrontDoorX - 0.75f, F(h) + 1.75f, Hd(h)), Front), outdoor: true);
        public static readonly JobPlace HoseBib = P("hose bib", HouseSpot.WindowCaulk, "exterior", h => new Pose(new Vector3(Hw(h) - 0.6f, 0.45f, Hd(h)), Front), outdoor: true);
        public static readonly JobPlace DryerVent = P("dryer vent", HouseSpot.WindowCaulk, "exterior", h => new Pose(new Vector3(Hw(h), 0.6f, -1.5f), ToPlusX), outdoor: true);
        public static readonly JobPlace FrontSiding = P("front siding", HouseSpot.WindowCaulk, "exterior", h => new Pose(new Vector3(LivingWinX(h) - 1.05f, F(h) + 0.25f, Hd(h)), Front), outdoor: true);
        public static readonly JobPlace FrontWallSiding = P("front wall siding", HouseSpot.WindowCaulk, "exterior", h => new Pose(new Vector3(LivingWinX(h) - 1.0f, 0.15f, Hd(h)), Front), outdoor: true);
        public static readonly JobPlace Fascia = P("front fascia", HouseSpot.Gutter, "exterior", h => { var p = Local(h, HouseSpot.Gutter); return new Pose(p + new Vector3(1.2f, 0, 0), Front); }, outdoor: true, ladder: true);
        public static readonly JobPlace Gutter = P("front gutter", HouseSpot.Gutter, "exterior", h => { var p = Local(h, HouseSpot.Gutter); return new Pose(p + new Vector3(-1.4f, 0, 0), Front); }, outdoor: true, ladder: true);
        public static readonly JobPlace Soffit = P("front soffit", HouseSpot.Gutter, "exterior", h => { var p = Local(h, HouseSpot.Gutter); return new Pose(new Vector3(p.x + 2.2f, p.y - 0.17f, Hd(h) + 0.16f), Front); }, outdoor: true, ladder: true);
        public static readonly JobPlace Roof = P("roof", HouseSpot.RoofShingles, "roof", outdoor: true, ladder: true);
        public static readonly JobPlace RoofSkylight = P("skylight", HouseSpot.RoofShingles, "roof", h => { var a = h.Anchor(HouseSpot.RoofShingles); var p = Local(h, HouseSpot.RoofShingles); return new Pose(p + new Vector3(2.0f, 0, 0) + LocalRot(h, HouseSpot.RoofShingles) * new Vector3(0, 0, 0.8f), LocalRot(h, HouseSpot.RoofShingles)); }, outdoor: true, ladder: true);
        public static readonly JobPlace DeckLedger = P("deck ledger", HouseSpot.DeckBoard, "yard", h => { var p = Local(h, HouseSpot.DeckBoard); return new Pose(new Vector3(p.x, p.y, -Hd(h)), Back); }, outdoor: true);
        public static readonly JobPlace DeckStep = P("deck step", HouseSpot.DeckBoard, "yard", h => { var p = Local(h, HouseSpot.DeckBoard); return new Pose(new Vector3(p.x, 0f, p.z - 10.6f * Units.In(5.625f)), Back); }, outdoor: true);
        public static readonly JobPlace FenceBay = P("fence section", HouseSpot.FencePicket, "yard", h => { var p = Local(h, HouseSpot.FencePicket); return new Pose(p + LocalRot(h, HouseSpot.FencePicket) * new Vector3(0, 0, 0.03f), LocalRot(h, HouseSpot.FencePicket)); }, outdoor: true);
        public static readonly JobPlace FenceBay2 = P("fence section (west)", HouseSpot.FencePicket, "yard", h => { var p = Local(h, HouseSpot.FencePicket); var r = LocalRot(h, HouseSpot.FencePicket); return new Pose(p + r * new Vector3(-2.6f, 0, 0.03f), r); }, outdoor: true);
        public static readonly JobPlace FenceGate = P("fence gate", HouseSpot.FencePicket, "yard", h => { var p = Local(h, HouseSpot.FencePicket); var r = LocalRot(h, HouseSpot.FencePicket); return new Pose(p + r * new Vector3(3.2f, 0, 0.03f), r); }, outdoor: true);
        public static readonly JobPlace Stoop = P("front step", HouseSpot.FrontDoor, "exterior", h => new Pose(new Vector3(TownHouse.FrontDoorX, 0f, Hd(h) + 1.1f), Front), outdoor: true);
        public static readonly JobPlace Sidewalk = P("public sidewalk", HouseSpot.SidewalkSquare, "exterior", outdoor: true, hide: true);
        public static readonly JobPlace FrontWalk = P("front walk", HouseSpot.FrontDoor, "exterior", h => new Pose(new Vector3(TownHouse.FrontDoorX, 0.05f, Hd(h) + 3.0f), Front), outdoor: true);
        public static readonly JobPlace Planter = P("brick planter", HouseSpot.FrontDoor, "exterior", h => new Pose(new Vector3(TownHouse.FrontDoorX + 2.0f, 0f, Hd(h) + 2.4f), Front), outdoor: true);
        public static readonly JobPlace BackYard = P("back yard", HouseSpot.FencePicket, "yard", h => new Pose(new Vector3(2.6f, 0f, -Hd(h) - 4.6f), Back), outdoor: true);
        public static readonly JobPlace BackYardSlope = P("back yard slope", HouseSpot.FencePicket, "yard", h => new Pose(new Vector3(-1.0f, 0f, -Hd(h) - 6.0f), Front), outdoor: true);
    }
}
