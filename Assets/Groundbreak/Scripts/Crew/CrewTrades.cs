using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One construction trade: who does what, what they earn (US daily wage for an 8-hour day, helper .. master)
    /// and what they look like on site (shirt color).</summary>
    public class TradeInfo
    {
        public string Id, Label;
        public Color Shirt;
        public int WageLow, WageHigh;   // $/day at skill 1 and skill 5
        public bool HiVis;              // wears a hi-vis vest over the shirt
    }

    public static class CrewTrades
    {
        public static readonly List<TradeInfo> All = new List<TradeInfo>
        {
            // wages: BLS-flavoured hourly rates x 8 (apprentice/helper .. journeyman lead)
            T("laborer",     "Laborer",     new Color(0.95f, 0.45f, 0.10f), 136, 208, true),
            T("carpenter",   "Carpenter",   new Color(0.62f, 0.16f, 0.14f), 160, 320),
            T("concrete",    "Concrete",    new Color(0.42f, 0.44f, 0.46f), 152, 288),
            T("plumber",     "Plumber",     new Color(0.15f, 0.32f, 0.62f), 176, 384),
            T("electrician", "Electrician", new Color(0.88f, 0.74f, 0.12f), 176, 400),
            T("hvac",        "HVAC",        new Color(0.12f, 0.52f, 0.56f), 168, 368),
            T("drywall",     "Drywall",     new Color(0.86f, 0.86f, 0.82f), 152, 304),
            T("painter",     "Painter",     new Color(0.95f, 0.95f, 0.95f), 144, 272),
            T("roofer",      "Roofer",      new Color(0.20f, 0.20f, 0.22f), 152, 288),
            T("landscaper",  "Landscaper",  new Color(0.22f, 0.50f, 0.20f), 128, 224),
        };

        static TradeInfo T(string id, string label, Color c, int lo, int hi, bool vest = false) =>
            new TradeInfo { Id = id, Label = label, Shirt = c, WageLow = lo, WageHigh = hi, HiVis = vest };

        public static TradeInfo Get(string id) => All.Find(t => t.Id == id) ?? All[0];

        /// <summary>Going market rate ($/day, rounded to $4 = 50 cents an hour) for a trade and skill 1..5.</summary>
        public static int Wage(string trade, int skill)
        {
            var t = Get(trade);
            float w = Mathf.Lerp(t.WageLow, t.WageHigh, (Mathf.Clamp(skill, 1, 5) - 1) / 4f);
            return Mathf.RoundToInt(w / 4f) * 4;
        }

        public static string SkillLabel(int skill)
        {
            switch (Mathf.Clamp(skill, 1, 5))
            {
                case 1: return "Helper";
                case 2: return "Apprentice";
                case 3: return "Journeyman";
                case 4: return "Lead";
                default: return "Master";
            }
        }

        public static string Stars(int skill) => new string('*', Mathf.Clamp(skill, 1, 5)) + new string('-', 5 - Mathf.Clamp(skill, 1, 5));

        /// <summary>A laborer can stand in on rough work without the full "wrong trade" penalty.</summary>
        public static bool Matches(string workerTrade, string taskTrade)
        {
            if (string.IsNullOrEmpty(taskTrade) || workerTrade == taskTrade) return true;
            if (taskTrade == "laborer") return true;                        // anyone can carry and dig
            if (taskTrade == "roofer" && workerTrade == "carpenter") return true;
            return false;
        }

        static readonly string[] First = { "Mike", "Luis", "Dave", "Carlos", "Tony", "Jamal", "Ray", "Sam", "Eddie", "Marco", "Tanya", "Rosa", "Kim",
            "Andre", "Pete", "Hector", "Will", "Jess", "Dana", "Omar", "Nick", "Gus", "Maria", "Travis", "Bo", "Curtis", "Ivan", "Sara", "Hank", "Leo" };
        static readonly string[] Last = { "Alvarez", "Kowalski", "Jackson", "Nguyen", "Murphy", "Ortiz", "Baker", "Ruiz", "Hansen", "Price", "Santos",
            "Carter", "Bell", "Novak", "Reyes", "Dunn", "Flores", "Wade", "Kelly", "Marsh", "Tate", "Cruz", "Boyd", "Lang", "Soto", "Pike" };

        public static string RandomName(System.Random r) => First[r.Next(First.Length)] + " " + Last[r.Next(Last.Length)];
    }
}
