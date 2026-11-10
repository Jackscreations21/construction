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
            string sign = meters < 0 ? "-" : "";
            return feet > 0 ? $"{sign}{feet}' {whole}{f}\"" : $"{sign}{whole}{f}\"";
        }
    }
}
