using UnityEngine;

namespace Groundbreak
{
    /// <summary>Code numbers a plan reviewer checks: occupant load (IBC 1004.5), plumbing fixture counts (IPC 2902.1), sprinklers (IBC 903.2 /
    /// IRC R313 for townhouses), exits (IBC 1006), structural limits (IRC R502 joist spans, rafter spans, stud heights).</summary>
    public partial class Blueprint
    {
        /// <summary>IBC occupant load factor (sq ft per person) for a room kind; 0 = not counted (restrooms, corridors, mech).</summary>
        public float LoadFactor(RoomKind k)
        {
            if (UsesIrc) return 0f;
            switch (k)
            {
                case RoomKind.Sales: return OccLetter == "B" ? 150f : 60f;
                case RoomKind.Dining: return 15f;
                case RoomKind.CommKitchen: return 200f;
                case RoomKind.Office: case RoomKind.Lobby: case RoomKind.BreakRoom: return 150f;
                case RoomKind.Storage: case RoomKind.Cooler: return 300f;
                case RoomKind.Warehouse: return 500f;
                case RoomKind.Shop: return 100f;
                default: return 0f;
            }
        }

        public int OccupantLoad { get { float s = 0; foreach (var r in Rooms) { float f = LoadFactor(KindOf(r.Name)); if (f > 0) s += Mathf.Ceil(r.Ft.width * r.Ft.height / f); } return Mathf.RoundToInt(s); } }

        /// <summary>Main occupancy letter from the Occupancy string ("IBC M (mercantile)" → "M").</summary>
        public string OccLetter
        {
            get
            {
                if (UsesIrc) return "R";
                if (Occupancy.Contains("A-2")) return "A-2"; if (Occupancy.Contains("S-1")) return "S-1"; if (Occupancy.Contains("F-1")) return "F-1";
                if (Occupancy.Contains(" M")) return "M"; if (Occupancy.Contains(" B")) return "B"; return "B";
            }
        }

        /// <summary>Water closets required (IPC Table 2902.1), counting single-user restrooms.</summary>
        public int WaterClosetsRequired
        {
            get
            {
                int ol = OccupantLoad; if (UsesIrc || ol == 0) return 0;
                switch (OccLetter)
                {
                    case "B": return ol <= 50 ? Mathf.CeilToInt(ol / 25f) : 2 + Mathf.CeilToInt((ol - 50) / 50f);
                    case "M": return Mathf.CeilToInt(ol / 500f);
                    case "A-2": return Mathf.CeilToInt(ol / 75f);
                    default: return Mathf.CeilToInt(ol / 100f);
                }
            }
        }
        public int LavatoriesRequired
        {
            get
            {
                int ol = OccupantLoad; if (UsesIrc || ol == 0) return 0;
                switch (OccLetter) { case "B": return ol <= 80 ? Mathf.CeilToInt(ol / 40f) : 2 + Mathf.CeilToInt((ol - 80) / 80f); case "M": return Mathf.CeilToInt(ol / 750f); case "A-2": return Mathf.CeilToInt(ol / 200f); default: return Mathf.CeilToInt(ol / 100f); }
            }
        }
        /// <summary>Drinking fountains (IPC 410): none for ≤ 15 occupants, restaurants serving water, or mercantile ≤ 100.</summary>
        public int DrinkingFountainsRequired
        {
            get
            {
                int ol = OccupantLoad; if (UsesIrc || ol <= 15) return 0;
                switch (OccLetter) { case "A-2": return 0; case "M": return ol <= 100 ? 0 : Mathf.CeilToInt(ol / 1000f); case "B": return Mathf.CeilToInt(ol / 100f); default: return Mathf.CeilToInt(ol / 1000f); }
            }
        }
        public bool ServiceSinkRequired => !UsesIrc && OccupantLoad > 15;

        /// <summary>IBC 903.2: A-2 fire area &gt; 5,000 sq ft or ≥ 100 occupants; M / S-1 / F-1 fire area &gt; 12,000 sq ft. IRC R313.1: townhouses.</summary>
        public bool SprinklersRequired
        {
            get
            {
                if (Category == "townhome") return true;
                if (UsesIrc) return false;
                float fireArea = WidthFt * DepthFt * (MezzanineFt > 0 ? 1.33f : Floors);
                switch (OccLetter)
                {
                    case "A-2": return fireArea > 5000f || OccupantLoad >= 100;
                    case "M": case "S-1": case "F-1": return fireArea > 12000f;
                    default: return false;
                }
            }
        }
        /// <summary>IBC 1006.2.1: two exits once a story's occupant load passes 49 (or 29 for S/F... kept at 49 here).</summary>
        public int ExitsRequired => UsesIrc ? 1 : OccupantLoad > 49 ? 2 : 1;

        // ------------------------------------------------------------------ structural limits

        /// <summary>Max clear span (ft) of a floor joist at 16" O.C., 40 psf live / 10 dead, L/360 (DF-L #2 lumber; I-joists per typical tables).</summary>
        public static float JoistSpanLimit(string joist) =>
            joist.StartsWith("2x8") ? 13.1f : joist.StartsWith("2x10") ? 16.0f : joist.StartsWith("2x12") ? 18.5f :
            joist.Contains("11-7/8") ? 19.5f : joist.Contains("14") ? 22.5f : joist.Contains("16") ? 26f : 16f;

        /// <summary>Max horizontal rafter span (ft) at 16" O.C., 30 psf snow, ceiling not attached (DF-L #2).</summary>
        public static float RafterSpanLimit(string size) => size.StartsWith("2x6") ? 11.5f : size.StartsWith("2x8") ? 14.6f : size.StartsWith("2x10") ? 17.8f : size.StartsWith("2x12") ? 20.7f : 14f;

        /// <summary>Max clear span (ft) of the roof framing system.</summary>
        public float RoofSpanLimit =>
            RoofFrame == RoofSystem.Trusses ? 60f : RoofFrame == RoofSystem.PostFrameTrusses ? 80f : RoofFrame == RoofSystem.RigidFrame ? 160f :
            RoofFrame == RoofSystem.BarJoist ? 60f : RafterSpanLimit(RafterSize) * (Roof == RoofType.Shed ? 1f : 2f);

        /// <summary>The span the roof framing actually covers (between bearing lines).</summary>
        public float RoofSpanFt
        {
            get
            {
                float across = RidgeAlongY ? WidthFt : DepthFt;
                if (Roof == RoofType.Flat) across = Mathf.Min(WidthFt, DepthFt);
                return across / (InteriorColumnLines + 1);
            }
        }


        public float MaxWallHeightFt =>
            WallFrame == WallSystem.PostFrame ? 20f : WallFrame == WallSystem.SteelFrame ? 40f : WallFrame == WallSystem.Cmu ? 28f :
            StudSize == "2x4" ? 10f : (UsesIrc ? 12f : 16f);   // IRC R602.3(5)/(6); commercial 2x6 walls engineered to 16'

        /// <summary>Lowest roof slope the covering is rated for (rise per 12).</summary>
        public float MinPitch => Covering == RoofCovering.Shingles ? 2f : Covering == RoofCovering.MetalPanels ? 1f : Covering == RoofCovering.StandingSeam ? 0.5f : 0.25f;
    }
}
