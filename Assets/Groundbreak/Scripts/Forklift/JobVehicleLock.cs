namespace Groundbreak
{
    /// <summary>Keeps the vehicles an accepted job needs in the fleet. A job's <see cref="Job.RequiredVehicleType"/> is
    /// checked at accept (Company.Blocker); this guards the other side: a vehicle can't be sold while an active job
    /// (worked by you or by a crew off site) needs that type and no other vehicle of the type is left. The requirement
    /// is part of the saved Job, so it holds after a load. If a job is ever found without its vehicle (an older save),
    /// it pauses: crews don't progress it and the client won't sign off until the type is owned again.
    /// Bankruptcy repossession already drops such jobs (Finance.DeclareBankruptcy).</summary>
    public static class JobVehicleLock
    {
        /// <summary>The active job that would lose its required vehicle if this one left the fleet, or null.</summary>
        public static Job JobNeeding(OwnedVehicle v)
        {
            if (v == null) return null;
            int sameType = 0;
            foreach (var o in Company.Vehicles) if (o != v && o.TypeId == v.TypeId) sameType++;
            if (sameType > 0) return null;
            foreach (var j in Company.ActiveJobs)
                if (j != null && !j.Done && j.RequiredVehicleType == v.TypeId) return j;
            return null;
        }

        /// <summary>Why this vehicle can't be sold right now, or null.</summary>
        public static string SaleBlocker(OwnedVehicle v)
        {
            var j = JobNeeding(v);
            if (j == null) return null;
            return $"Can't sell {v.Name}: the job \"{j.Title}\" needs a {TypeName(v.TypeId)}. Finish that job or buy another {TypeName(v.TypeId)} first.";
        }

        /// <summary>True when the job's required vehicle type is in the fleet (or it needs none).</summary>
        public static bool Met(Job j) => j == null || string.IsNullOrEmpty(j.RequiredVehicleType) || Company.Owns(j.RequiredVehicleType);

        public static string PausedLine(Job j) => $"\"{j.Title}\" is on hold: it needs a {TypeName(j.RequiredVehicleType)} and you have none.";

        public static string TypeName(string typeId)
        {
            var t = FleetCatalog.Get(typeId);
            return t != null ? t.Name.ToLower() : typeId;
        }
    }
}
