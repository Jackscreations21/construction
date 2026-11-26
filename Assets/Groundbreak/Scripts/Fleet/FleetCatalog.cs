using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A vehicle the company can buy. Dimensions and ratings are real-world figures for the named model class.</summary>
    public class VehicleType
    {
        public string Id, Name, Model, Description;
        public int PriceNew, PriceUsed;
        public float PayloadLb;            // rated payload
        public float VolumeCuFt;           // usable cargo volume (water-level for open beds, incl. reasonable stacking)
        public Vector3 Bed;                // cargo area interior (width, height, length) in meters
        public float MaxItemLength;        // longest single item it can legally carry (meters), without a rack
        public float RackMaxItemLength;    // with an added ladder/lumber rack; 0 = no rack option
        public int RackPrice;
        public bool Bulk;                  // can haul loose gravel/soil (dump body)
        public bool Crane;                 // can set trusses / lift loads
        public bool Enclosed;              // weather-protected cargo
        public bool Machine;               // equipment, carries no cargo
        public float Mpg;
        public int InsurancePerDay, MaintenancePerDay;

        public string BedLabel => Machine ? "bucket 0.5 cu yd" :
            $"{Units.Format(Bed.z)} L x {Units.Format(Bed.x)} W" + (Bed.y > 0.05f ? $" x {Units.Format(Bed.y)} H" : "");
    }

    /// <summary>The dealership's line-up. Type ids are shared with Company.AddVehicle / Job.RequiredVehicleType.</summary>
    public static class FleetCatalog
    {
        static float In(float inches) => Units.In(inches);
        static float Ft(float feet) => feet * Units.Foot;

        public static readonly List<VehicleType> All = new List<VehicleType>
        {
            new VehicleType { Id = "van", Name = "Work van", Model = "3/4-ton cargo van, medium roof, 148\" WB",
                Description = "Enclosed, lockable, shelving for service work. Sheet goods lie flat; long stock rides on the ladder rack.",
                PriceNew = 48500, PriceUsed = 18500, PayloadLb = 3500, VolumeCuFt = 357,
                Bed = new Vector3(In(69), In(72), In(143.7f)), MaxItemLength = Ft(12), RackMaxItemLength = Ft(20), RackPrice = 850,
                Enclosed = true, Mpg = 15, InsurancePerDay = 6, MaintenancePerDay = 4 },
            new VehicleType { Id = "pickup", Name = "Half-ton pickup", Model = "1/2-ton regular cab, 8' bed",
                Description = "Everyday truck. 4x8 sheets lie flat between the wheelhouses; 12' stock with the tailgate down and a flag. 16' needs a lumber rack.",
                PriceNew = 46000, PriceUsed = 17000, PayloadLb = 2000, VolumeCuFt = 77,
                Bed = new Vector3(In(65.2f), In(21.4f), In(97.6f)), MaxItemLength = Ft(12), RackMaxItemLength = Ft(20), RackPrice = 1150,
                Mpg = 18, InsurancePerDay = 5, MaintenancePerDay = 3 },
            new VehicleType { Id = "flatbed", Name = "Flatbed truck", Model = "Class 4 chassis cab, dual rear wheels, 12' stake bed",
                Description = "Carries lifts of lumber and sheet goods; 16' and 20' stock rides flat. Load from either side with a forklift.",
                PriceNew = 72000, PriceUsed = 31000, PayloadLb = 7000, VolumeCuFt = 384,
                Bed = new Vector3(In(96), In(40), Ft(12)), MaxItemLength = Ft(20),
                Mpg = 11, InsurancePerDay = 9, MaintenancePerDay = 7 },
            new VehicleType { Id = "dump", Name = "Dump truck", Model = "Class 6 single-axle, 10' dump body (5 cu yd)",
                Description = "The only truck that hauls loose gravel, sand and soil, and hauls off spoils and tear-off debris.",
                PriceNew = 96000, PriceUsed = 38000, PayloadLb = 12000, VolumeCuFt = 140,
                Bed = new Vector3(In(84), In(24), Ft(10)), MaxItemLength = Ft(12), Bulk = true,
                Mpg = 8, InsurancePerDay = 12, MaintenancePerDay = 10 },
            new VehicleType { Id = "box", Name = "Box truck", Model = "Class 4 cab-over, 16' dry van body",
                Description = "Dry, lockable cargo: cabinets, doors, windows, drywall, appliances. Liftgate at the rear.",
                PriceNew = 68000, PriceUsed = 26000, PayloadLb = 6000, VolumeCuFt = 960,
                Bed = new Vector3(In(92), In(84), Ft(16)), MaxItemLength = Ft(16), Enclosed = true,
                Mpg = 10, InsurancePerDay = 9, MaintenancePerDay = 7 },
            new VehicleType { Id = "boom", Name = "Boom truck", Model = "Tandem-axle, 20' flatbed, 19-ton telescoping crane",
                Description = "Hauls and sets roof trusses, lifts sheathing and shingles to the roof, carries the longest stock.",
                PriceNew = 185000, PriceUsed = 78000, PayloadLb = 14000, VolumeCuFt = 640,
                Bed = new Vector3(In(96), In(48), Ft(20)), MaxItemLength = Ft(44), Crane = true,
                Mpg = 6, InsurancePerDay = 20, MaintenancePerDay = 16 },
            new VehicleType { Id = "skidsteer", Name = "Skid steer + trailer", Model = "74 hp skid steer (2,690 lb ROC) on a 16' 14k equipment trailer",
                Description = "Digs, grades, moves pallets and loads the dump truck. Towed by any truck in the fleet.",
                PriceNew = 74000, PriceUsed = 36000, PayloadLb = 0, VolumeCuFt = 0,
                Bed = new Vector3(In(74), 0, In(40)), MaxItemLength = 0, Machine = true,
                Mpg = 0, InsurancePerDay = 6, MaintenancePerDay = 8 },
        };

        public static VehicleType Get(string id) => All.Find(t => t.Id == id);
        public static string NameOf(string id) { var t = Get(id); return t != null ? t.Name : id; }
    }
}
