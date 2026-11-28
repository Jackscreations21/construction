using UnityEngine;

namespace Groundbreak
{
    /// <summary>World units are meters. Everything the player sees is feet/inches.</summary>
    public static class Units
    {
        public const float Inch = 0.0254f;
        public const float Foot = 0.3048f;

        public static float In(float inches) => inches * Inch;
        public static float ToInches(float meters) => meters / Inch;

        /// <summary>Formats meters as e.g. 7' 8 5/8" (rounded to nearest 1/16").</summary>
        public static string Format(float meters)
        {
            float inches = Mathf.Abs(ToInches(meters));
            int sixteenths = Mathf.RoundToInt(inches * 16f);
            bool negative = meters < 0 && sixteenths > 0; // a tiny negative that rounds to 0 is 0", not -0"
            int feet = sixteenths / (12 * 16);
            sixteenths -= feet * 12 * 16;
            int whole = sixteenths / 16;
            int frac = sixteenths % 16;
            string f = "";
            if (frac != 0)
            {
                int num = frac, den = 16;
                while (num % 2 == 0) { num /= 2; den /= 2; }
                f = " " + num + "/" + den;
            }
            string sign = negative ? "-" : "";
            if (feet > 0) return $"{sign}{feet}' {whole}{f}\"";
            // under 1": 1/2", not 0 1/2"
            return whole == 0 && frac != 0 ? $"{sign}{f.TrimStart()}\"" : $"{sign}{whole}{f}\"";
        }
    }
}
