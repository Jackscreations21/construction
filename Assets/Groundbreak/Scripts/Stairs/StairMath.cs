using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Result of a stair calculation (all lengths in inches). Produced by <see cref="StairMath.Calculate"/>.</summary>
    [System.Serializable]
    public struct StairResult
    {
        public bool Valid;
        public float TotalRiseIn;
        public int Risers;
        public float UnitRiseIn;
        public float UnitRunIn;
        public int Treads;
        public float TotalRunIn;
        public float AngleDeg;
        /// <summary>Diagonal of one step (rise/run on the framing square).</summary>
        public float UnitHypIn;
        /// <summary>Straight hypotenuse of total run x total rise.</summary>
        public float HypotenuseIn;
        /// <summary>Real length of 2x12 the cut stringer occupies, incl. the level foot cut and the plumb top cut.</summary>
        public float StringerLengthIn;
        /// <summary>Wood left between the notch (inner corner) and the bottom edge of the board.</summary>
        public float ThroatIn;
        public bool ThroatOk, RiseOk, RunOk;
        public float TreadDepthIn, NosingIn, TreadThicknessIn, RiserThicknessIn, BoardWidthIn;
        /// <summary>Catalog id of the stringer stock that fits (pt2x12x12, or 2x12x16 when 12' is too short).</summary>
        public string StockId;
        public string HeadroomNote;

        public float TotalRiseM => Units.In(TotalRiseIn);
        public float UnitRiseM => Units.In(UnitRiseIn);
        public float UnitRunM => Units.In(UnitRunIn);
        public float TotalRunM => Units.In(TotalRunIn);
        public bool CodeOk => Valid && ThroatOk && RiseOk && RunOk;
    }

    /// <summary>Stair layout math (IRC R311.7): risers max 7-3/4", treads min 10" (default 10-1/4"),
    /// stringers cut from 2x12 with at least 5" of throat. All inputs/outputs in inches.</summary>
    public static class StairMath
    {
        public const float MaxRiserIn = 7.75f;
        public const float MinRunIn = 10f;
        public const float DefaultRunIn = 10.25f;
        public const float MinThroatIn = 5f;
        public const float StringerBoardIn = 11.25f;  // 2x12 actual
        public const float TreadThicknessIn = 1.5f;   // two 2x6 treads
        public const float TreadDepthIn = 11.25f;     // 5-1/2 + 1/4" drainage gap + 5-1/2
        public const float RiserThicknessIn = 0.75f;  // 1x8 riser board
        public const float MinWidthIn = 36f;
        public const float MinHeadroomIn = 80f;       // 6' 8"
        public const float StockLength12In = 144f;

        public static int RiserCount(float totalRiseIn) => Mathf.Max(1, Mathf.CeilToInt(totalRiseIn / MaxRiserIn - 1e-4f));

        public static StairResult Calculate(float totalRiseIn, float unitRunIn = DefaultRunIn)
        {
            var r = new StairResult
            {
                TotalRiseIn = totalRiseIn,
                TreadThicknessIn = TreadThicknessIn,
                RiserThicknessIn = RiserThicknessIn,
                BoardWidthIn = StringerBoardIn,
                TreadDepthIn = TreadDepthIn,
            };
            if (!(totalRiseIn > 0.5f) || totalRiseIn > 400f) { r.StockId = "pt2x12x12"; r.HeadroomNote = ""; return r; }
            r.Valid = true;
            r.UnitRunIn = Mathf.Max(MinRunIn, unitRunIn);
            r.Risers = RiserCount(totalRiseIn);
            r.UnitRiseIn = totalRiseIn / r.Risers;
            r.Treads = r.Risers - 1;                      // the top "tread" is the landing / upper floor
            r.TotalRunIn = r.Treads * r.UnitRunIn;
            r.AngleDeg = Mathf.Atan2(r.UnitRiseIn, r.UnitRunIn) * Mathf.Rad2Deg;
            r.UnitHypIn = Mathf.Sqrt(r.UnitRiseIn * r.UnitRiseIn + r.UnitRunIn * r.UnitRunIn);
            r.HypotenuseIn = Mathf.Sqrt(r.TotalRunIn * r.TotalRunIn + totalRiseIn * totalRiseIn);
            r.ThroatIn = StringerBoardIn - r.UnitRiseIn * r.UnitRunIn / r.UnitHypIn;
            r.ThroatOk = r.ThroatIn >= MinThroatIn - 1e-3f;
            r.RiseOk = r.UnitRiseIn <= MaxRiserIn + 1e-4f;
            r.RunOk = r.UnitRunIn >= MinRunIn - 1e-4f;
            r.NosingIn = TreadDepthIn - r.UnitRunIn;   // tread overhang past the riser face below
            var lay = new StringerLayout(r);
            r.StringerLengthIn = lay.LengthIn;
            r.StockId = r.StringerLengthIn + 1f <= StockLength12In ? "pt2x12x12" : "2x12x16";
            float lowNose = r.UnitRiseIn + MinHeadroomIn, topNose = r.Treads * r.UnitRiseIn + MinHeadroomIn;
            r.HeadroomNote = "Headroom: 6' 8\" min, measured plumb from the nosing line (IRC R311.7.2) - nothing lower than "
                             + FormatFtIn(lowNose) + " above the bottom floor at the first step, " + FormatFtIn(topNose) + " at the top step.";
            return r;
        }

        /// <summary>Stringer profile (stair plane, inches): x = horizontal run from the face of the first riser cut, y = up from the floor.
        /// Counter-clockwise. The stringer is dropped by the tread thickness at the bottom and cut back by the riser thickness at the top
        /// (the top riser / hanger board covers the landing rim).</summary>
        public static List<Vector2> StringerProfileIn(StairResult r) => new StringerLayout(r).Profile;

        // ------------------------------------------------------------------ formatting / parsing

        /// <summary>Inches to the nearest 1/32", e.g. 7 23/32".</summary>
        public static string FormatIn(float inches)
        {
            int n32 = Mathf.RoundToInt(Mathf.Abs(inches) * 32f);
            int whole = n32 / 32, frac = n32 % 32;
            string s = (inches < 0 ? "-" : "") + whole;
            if (frac != 0)
            {
                int num = frac, den = 32;
                while (num % 2 == 0) { num /= 2; den /= 2; }
                s += " " + num + "/" + den;
            }
            return s + "\"";
        }

        /// <summary>Feet and inches to 1/32", e.g. 5' 1 1/2".</summary>
        public static string FormatFtIn(float inches)
        {
            int n32 = Mathf.RoundToInt(Mathf.Abs(inches) * 32f);
            int ft = n32 / (12 * 32);
            float rest = (n32 - ft * 12 * 32) / 32f;
            return ft > 0 ? ft + "' " + FormatIn(rest) : FormatIn(rest);
        }

        /// <summary>Parses "48", "48.5", "48 1/2", "4'", "4' 0 1/2\"", "4ft 6in" into inches.</summary>
        public static bool TryParseInches(string s, out float inches)
        {
            inches = 0f;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().ToLowerInvariant().Replace("feet", "'").Replace("foot", "'").Replace("ft", "'")
                 .Replace("inches", "").Replace("inch", "").Replace("in", "").Replace("\"", " ").Replace("-", " ");
            float feet = 0f;
            int tick = s.IndexOf('\'');
            if (tick >= 0)
            {
                if (!TryParseNumber(s.Substring(0, tick), out feet)) return false;
                s = s.Substring(tick + 1);
            }
            float ins = 0f;
            if (!string.IsNullOrWhiteSpace(s) && !TryParseNumber(s, out ins)) return false;
            inches = feet * 12f + ins;
            return inches > 0f;
        }

        static bool TryParseNumber(string s, out float v)
        {
            v = 0f;
            var parts = s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            foreach (var p in parts)
            {
                int slash = p.IndexOf('/');
                if (slash > 0)
                {
                    if (!float.TryParse(p.Substring(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out float a)) return false;
                    if (!float.TryParse(p.Substring(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float b) || b == 0f) return false;
                    v += a / b;
                }
                else
                {
                    if (!float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float a)) return false;
                    v += a;
                }
            }
            return true;
        }
    }

    /// <summary>Geometry of one cut stringer (inches). Stair plane: x = run, y = up. Board frame: Z along the board's length (U),
    /// X across its width (V, +X = the top edge where the tooth points touch), origin on the board's centerline at UCenter.</summary>
    public class StringerLayout
    {
        public readonly StairResult R;
        public readonly List<Vector2> Profile = new List<Vector2>();
        public readonly Vector2 U, V;           // board length axis / across axis in the stair plane
        public readonly float VCenter;          // V coordinate of the board centerline
        public readonly float UMin, UMax;
        public readonly float XTop;             // run position of the top plumb cut
        public readonly float X0;               // run position where the bottom edge meets the floor (back of the foot cut)
        public readonly bool FootOnBottomEdge;  // false for very short stairs (the board's edge never reaches the floor)
        public float LengthIn => UMax - UMin;

        public StringerLayout(StairResult r)
        {
            R = r;
            int n = r.Risers;
            float Rr = r.UnitRiseIn, T = r.UnitRunIn, t = r.TreadThicknessIn, W = r.BoardWidthIn;
            float hyp = Mathf.Sqrt(Rr * Rr + T * T);
            U = new Vector2(T / hyp, Rr / hyp);
            V = new Vector2(-U.y, U.x);
            VCenter = (Rr - t) * U.x - W * 0.5f;     // top edge passes through the tooth points (0, R - t), (T, 2R - t), ...
            if (!r.Valid || n < 2) { UMin = UMax = 0f; return; }

            float slope = Rr / T, drop = W * hyp / T;
            XTop = (n - 1) * T - r.RiserThicknessIn;
            X0 = (drop - Rr + t) / slope;
            float YB(float x) => slope * x + Rr - t - drop;

            Profile.Add(new Vector2(0f, 0f));
            FootOnBottomEdge = X0 < XTop - 0.01f;
            if (FootOnBottomEdge) { Profile.Add(new Vector2(X0, 0f)); Profile.Add(new Vector2(XTop, YB(XTop))); }
            else Profile.Add(new Vector2(XTop, 0f));
            Profile.Add(new Vector2(XTop, (n - 1) * Rr - t));
            for (int k = n - 1; k >= 1; k--)
            {
                Profile.Add(new Vector2((k - 1) * T, k * Rr - t));                       // tooth point (top of riser cut k)
                if (k > 1) Profile.Add(new Vector2((k - 1) * T, (k - 1) * Rr - t));      // notch (bottom of riser cut k)
            }
            UMin = float.MaxValue; UMax = float.MinValue;
            foreach (var p in Profile) { float u = Vector2.Dot(p, U); UMin = Mathf.Min(UMin, u); UMax = Mathf.Max(UMax, u); }
        }

        /// <summary>Bottom edge height at a run position.</summary>
        public float BottomEdgeY(float x)
        {
            float Rr = R.UnitRiseIn, T = R.UnitRunIn;
            float hyp = Mathf.Sqrt(Rr * Rr + T * T);
            return Rr / T * x + Rr - R.TreadThicknessIn - R.BoardWidthIn * hyp / T;
        }

        /// <summary>Stair-plane point to board coordinates (x = across, y = along) for a board whose center is at uCenter.</summary>
        public Vector2 ToBoard(Vector2 p, float uCenter) => new Vector2(Vector2.Dot(p, V) - VCenter, Vector2.Dot(p, U) - uCenter);
        public Vector2 FromBoard(float x, float z, float uCenter) => (uCenter + z) * U + (VCenter + x) * V;
    }
}
