using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum Trade { Plumbing, Electrical, Hvac }

    /// <summary>What flows in a run. Ports only connect to runs of a compatible kind (see NetPort.Accepts).</summary>
    public enum NetKind { Supply, Drain, Vent, Power, Duct, Refrigerant, Condensate, Gas }

    /// <summary>How a run end is joined to a fitting/port. None = no joining step needed (Romex into a box clamp).</summary>
    public enum JoinMethod { None, Crimp, Sweat, Solvent, Tape, Thread }

    public enum RunShape { Round, Flat, Rect }

    /// <summary>Data for one kind of pipe / cable / duct. All sizes meters unless named *In.</summary>
    public class RunSpec
    {
        public string Id, Name;
        public Trade Trade;
        public NetKind Kind;
        public RunShape Shape = RunShape.Round;
        public float Width;            // OD (round) or width (flat/rect)
        public float Height;           // flat/rect only
        public float NominalIn;        // nominal size in inches (1/2" PEX, 3" PVC, 6" duct...). Ports match on this.
        public SurfaceKind Surface = SurfaceKind.PaintedWhite;
        public Color Tint = Color.white;
        public JoinMethod Join = JoinMethod.None;
        public bool BendsInline;       // PEX / Romex / line set: corners are bends in the run, no fittings
        public float MinBendRadius;    // centerline radius for inline bends and elbows
        public string StockId;         // catalog id consumed when routing (null = free)
        public float FeetPerUnit = 1f; // feet per stock unit (10' stick = 10, coils counted in feet = 1)
        public string CutTool;         // tool id that cuts this (tubing cutter / PVC saw / tin snips / strippers)
        public int FittingPrice = 3;   // $ charged per auto-inserted fitting (from the fitting box)
        public bool Hot;               // hot water supply (red PEX)
        // Electrical
        public int Awg, Conductors;    // conductors excluding ground (14/2 = 2, 10/3 = 3)
        public float Ampacity;
        // HVAC
        public float Cfm;              // design airflow a branch of this size carries at ~0.08 in.w.c./100'
        public float MaxSlope = 0f;

        public bool IsDrain => Kind == NetKind.Drain || Kind == NetKind.Vent || Kind == NetKind.Condensate;
        public float Radius => Width * 0.5f;
        public float ClearanceDia => Shape == RunShape.Round ? Width : Mathf.Max(Width, Height);
        public override string ToString() => Name;
    }

    public static class RunSpecs
    {
        static readonly List<RunSpec> _all = new List<RunSpec>();
        public static IReadOnlyList<RunSpec> All => _all;

        /// <summary>Register (or replace by Id) run specs. Call from BeforeSceneLoad.</summary>
        public static void Register(params RunSpec[] specs)
        {
            foreach (var s in specs)
            {
                int i = _all.FindIndex(x => x.Id == s.Id);
                if (i >= 0) _all[i] = s; else _all.Add(s);
            }
        }

        public static RunSpec Get(string id) => _all.Find(s => s.Id == id);
        public static List<RunSpec> ForTrade(Trade t) => _all.FindAll(s => s.Trade == t);
    }

    /// <summary>A connection point on a node (fitting socket, box knockout, fixture stub, register collar).</summary>
    public class NetPort
    {
        public NetNode Node;
        public string Label = "";
        public Vector3 LocalPos, LocalDir = Vector3.forward;
        public NetKind Kind;
        public float NominalIn;         // 0 = any size
        public JoinMethod Join;          // joining needed where a run lands here
        public NetRun Run;               // connected run (null = open)
        public bool Joined;              // crimped / sweated / glued / taped
        public bool Capped;              // test cap or plug installed (counts as closed)
        public int MaxRuns = 1;          // >1 for electrical boxes (several cables through one clamp side)
        public readonly List<NetRun> ExtraRuns = new List<NetRun>();

        public Vector3 WorldPos => Node != null ? Node.transform.TransformPoint(LocalPos) : LocalPos;
        public Vector3 WorldDir => Node != null ? Node.transform.TransformDirection(LocalDir).normalized : LocalDir;
        public bool Free => Run == null || (MaxRuns > 1 && 1 + ExtraRuns.Count < MaxRuns);
        /// <summary>Open = water/air would escape here: nothing connected and not capped.</summary>
        public bool Open => Run == null && !Capped;
        public bool NeedsJoin => Run != null && Join != JoinMethod.None && !Joined;

        public IEnumerable<NetRun> Runs
        {
            get { if (Run != null) yield return Run; foreach (var r in ExtraRuns) if (r != null) yield return r; }
        }

        public bool Accepts(RunSpec s)
        {
            if (s == null) return false;
            if (!KindsCompatible(Kind, s.Kind)) return false;
            return NominalIn <= 0f || s.NominalIn <= 0f || Mathf.Abs(NominalIn - s.NominalIn) < 0.01f;
        }

        public static bool KindsCompatible(NetKind a, NetKind b)
        {
            if (a == b) return true;
            bool dwvA = a == NetKind.Drain || a == NetKind.Vent, dwvB = b == NetKind.Drain || b == NetKind.Vent;
            return dwvA && dwvB;
        }
    }
}
