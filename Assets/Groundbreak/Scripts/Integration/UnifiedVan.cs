using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// One van. The company's "van" (OwnedVehicle.TypeId "van", from HandymanStart or the Dealership) is drawn everywhere with the
    /// drivable WorkVan's real-size model. The first van the company owns IS the WorkVan: while it exists in town the yard
    /// leaves its stall empty instead of drawing a second copy; any extra vans and every truck park in the yard's stalls.
    /// </summary>
    public static class UnifiedVan
    {
        /// <summary>The OwnedVehicle that the drivable WorkVan represents (the company's first van), or null.</summary>
        public static OwnedVehicle DrivableVan => Company.Vehicles.Find(v => v.TypeId == WorkVan.TypeId);

        /// <summary>A parked (static) copy of the work van: body, rear doors, wheels, rack ladder; one box collider.</summary>
        public static GameObject BuildParked(Transform parent, bool rack)
        {
            var go = new GameObject("Vehicle_van");
            go.transform.SetParent(parent, false);
            var t = go.transform;
            WorkVanModel.BuildBody(t);
            WorkVanModel.BuildRearDoor(t, true);
            WorkVanModel.BuildRearDoor(t, false);
            float r = WorkVanModel.TireRadius, x = WorkVanModel.TrackHalf;
            WorkVanModel.BuildWheel(t, new Vector3(-x, r, WorkVanModel.FrontAxleZ));
            WorkVanModel.BuildWheel(t, new Vector3(x, r, WorkVanModel.FrontAxleZ));
            WorkVanModel.BuildWheel(t, new Vector3(-x, r, WorkVanModel.RearAxleZ));
            WorkVanModel.BuildWheel(t, new Vector3(x, r, WorkVanModel.RearAxleZ));
            if (rack) WorkVanModel.BuildRackLadder(t);
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 1.24f, 0f); bc.size = new Vector3(WorkVanModel.Width, 1.72f, WorkVanModel.Length);
            return go;
        }

        static bool SkipAtYard(OwnedVehicle v) => WorkVan.I != null && v == DrivableVan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            VehicleModels.VanOverride = BuildParked;
            YardSite.SkipParked = SkipAtYard;
        }
    }
}
