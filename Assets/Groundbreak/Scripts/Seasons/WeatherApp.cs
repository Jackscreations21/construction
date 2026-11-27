using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Tablet "Weather" app: conditions now, what work the weather allows, 7-day forecast (mostly right, not always),
    /// storm repair rush, seasonal business. Free Build: season / sky / storm picker.</summary>
    public static class WeatherApp
    {
        public const string Name = "Weather";
        static readonly string[] SeasonButtons = { "Spring", "Summer", "Fall", "Winter" };
        static readonly int[] SeasonMonths = { 4, 5, 9, 0 };   // May, June, October, January
        static readonly string[] SkyButtons = { "Sunny", "Cloudy", "Rain", "Thunder", "Snow" };
        static readonly Sky[] SkyValues = { Sky.Clear, Sky.Cloudy, Sky.Rain, Sky.Thunderstorm, Sky.Snow };
        static readonly string[] StormButtons = { "No storm", "Hail", "Wind", "Blizzard", "Flood rain" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => Tablet.RegisterApp(Name, Draw);

        public struct Row { public string Day, Sky, Temps, Precip, Wind, Note; }

        /// <summary>The 7-day forecast as issued today (Career: <see cref="Climate.Forecast"/>; Free Build: the picked weather).</summary>
        public static List<Row> ForecastRows()
        {
            var rows = new List<Row>();
            int today = SeasonWeather.Day;
            for (int i = 0; i < 7; i++)
            {
                DayWeather f; int pop;
                if (SeasonWeather.Career && SeasonWeather.Simulated && SeasonWeather.Override == null) f = Climate.Forecast(today, today + i, out pop);
                else { f = SeasonWeather.Today; pop = f.Wet ? 100 : 0; }
                string note = "";
                if (f.Storm != StormKind.None) note = "STORM";
                else if (f.LowF < SeasonRules.PourMinF) note = "cold pour";
                else if (f.GustMph >= SeasonRules.TrussWindMph + 12f || f.WindMph >= SeasonRules.TrussWindMph) note = "windy";
                else if (pop >= 50) note = f.IsSnow ? "snow" : "rain delay";
                else if (f.HighF >= SeasonRules.HeatF) note = "heat";
                else note = "good work day";
                rows.Add(new Row
                {
                    Day = i == 0 ? "Today" : SeasonWeather.Career ? Climate.DateLabel(today + i) : "Day +" + i,
                    Sky = f.SkyLabel, Temps = $"{Mathf.RoundToInt(f.HighF)}° / {Mathf.RoundToInt(f.LowF)}°",
                    Precip = $"{pop}%", Wind = $"{Mathf.RoundToInt(f.WindMph)} mph", Note = note
                });
            }
            return rows;
        }

        /// <summary>Everything the app shows, as text lines (also used for the screenshot board).</summary>
        public static List<string> Lines()
        {
            var l = new List<string>();
            var g = SeasonWeather.Ground; int m = SeasonWeather.Month;
            l.Add($"{SeasonWeather.SeasonName.ToUpperInvariant()} · {SeasonWeather.Summary}");
            l.Add($"Daylight {Climate.DaylightHours(m, 15):0.0} h · ground: {(g.Frozen ? $"frozen {Mathf.RoundToInt(g.FrostIn)}\"" : g.Muddy ? "muddy" : "workable")}" +
                  (g.SnowIn >= 0.5f ? $" · snow {Mathf.RoundToInt(g.SnowIn)}\"" : "") + $" · crews: {SeasonRules.CrewPaceNote()}");
            l.Add("");
            l.Add("TODAY'S WORK");
            foreach (var (work, why) in SeasonRules.Status()) l.Add($"  {(why == null ? "OK " : "NO ")} {work}{(why == null ? "" : " - " + why)}");
            if (!SeasonWeather.RulesOn) l.Add(SeasonWeather.Career ? "  (weather rules are off)" : "  (Free Build: weather is for looks only)");
            l.Add("");
            l.Add("7-DAY FORECAST  (forecasts are usually right, not always)");
            foreach (var r in ForecastRows()) l.Add($"  {r.Day,-12} {r.Sky,-15} {r.Temps,-10} rain {r.Precip,-5} wind {r.Wind,-7} {r.Note}");
            l.Add("");
            if (StormEvents.Active) l.Add($"STORM REPAIR RUSH: {Climate.StormName(StormEvents.Kind)} - {StormEvents.DaysLeft} days left, storm jobs pay +{Mathf.RoundToInt((StormEvents.Premium - 1f) * 100f)}%");
            var s = SeasonWeather.Season;
            l.Add($"Business: {s} pay x{SeasonBusiness.PayFactor(s):0.00} · outdoor work demand x{SeasonBusiness.DemandFactor(s, true):0.0}, indoor x{SeasonBusiness.DemandFactor(s, false):0.0}" +
                  (SeasonBusiness.ColdSnap ? " · COLD SNAP: furnace calls" : "") + (SeasonBusiness.HeatWave ? " · HEAT WAVE: AC calls" : ""));
            return l;
        }

        static Vector2 _scroll;
        static void Draw()
        {
            var t = Tablet.I; if (t == null) return;
            if (!SeasonWeather.Career) DrawPicker(t);
            var lines = Lines();
            foreach (var line in lines)
            {
                if (line.Length == 0) { GUILayout.Space(6); continue; }
                bool header = line == line.ToUpperInvariant() && !line.StartsWith("  ") || line.StartsWith("7-DAY") || line.StartsWith("STORM");
                GUILayout.Label(line, header ? t.HeaderStyle : t.LabelStyle);
            }
            GUILayout.Label("Rules: no pours below 40°F without curing blankets or a heater · frozen ground needs thaw blankets to dig · " +
                            "no roofing, exterior paint or pours in rain · no truss setting over 25 mph wind · heat over 90°F slows crews. " +
                            "Blankets, heaters and tarps: Garden Center → Outdoor.", t.LabelStyle);
        }

        static void DrawPicker(Tablet t)
        {
            GUILayout.Label("FREE BUILD WEATHER", t.HeaderStyle);
            int si = System.Array.IndexOf(SeasonMonths, SeasonWeather.FreeMonth); if (si < 0) si = 1;
            int ns = GUILayout.Toolbar(si, SeasonButtons, t.ButtonStyle);
            if (ns != si) SeasonWeather.FreeMonth = SeasonMonths[ns];
            int ki = System.Array.IndexOf(SkyValues, SeasonWeather.FreeSky); if (ki < 0) ki = 0;
            int nk = GUILayout.Toolbar(ki, SkyButtons, t.ButtonStyle);
            if (nk != ki) SeasonWeather.FreeSky = SkyValues[nk];
            int st = (int)SeasonWeather.FreeStorm;
            int nst = GUILayout.Toolbar(st, StormButtons, t.ButtonStyle);
            if (nst != st)
            {
                SeasonWeather.FreeStorm = (StormKind)nst;
                if (nst == 0) StormEvents.End(); else StormEvents.Trigger((StormKind)nst, SeasonWeather.Day);
            }
            GUILayout.Space(6);
        }

        // ------------------------------------------------------------------ in-world board (screenshots: IMGUI doesn't render in batchmode)

        /// <summary>Builds a dark tablet-like board showing <see cref="Lines"/> (1.6 m wide) facing -forward of <paramref name="rot"/>.</summary>
        public static GameObject BuildBoard(Vector3 pos, Quaternion rot)
        {
            var root = new GameObject("WeatherBoard");
            root.transform.SetPositionAndRotation(pos, rot);
            var lines = Lines();
            float w = 1.9f, lineH = 0.052f, h = lines.Count * lineH + 0.16f;
            var back = new GameObject("Back"); back.transform.SetParent(root.transform, false);
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-w / 2, -h, 0.005f), new Vector3(w / 2, -h, 0.005f), new Vector3(w / 2, 0.08f, 0.005f), new Vector3(-w / 2, 0.08f, 0.005f) };
            mesh.uv = new[] { Vector2.zero, new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; mesh.RecalculateNormals();
            back.AddComponent<MeshFilter>().sharedMesh = mesh;
            var unlit = Shader.Find("Unlit/Color");
            back.AddComponent<MeshRenderer>().sharedMaterial = new Material(unlit) { color = new Color(0.08f, 0.1f, 0.13f) };
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < lines.Count; i++)
            {
                var go = new GameObject("L" + i); go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(-w / 2 + 0.05f, -i * lineH, 0f);
                var tm = go.AddComponent<TextMesh>();
                tm.font = font; tm.text = lines[i]; tm.fontSize = 48; tm.characterSize = 0.0105f; tm.anchor = TextAnchor.UpperLeft;
                string s = lines[i];
                tm.color = s.Contains(" NO ") || s.StartsWith("  NO") || s.Contains("STORM") ? new Color(1f, 0.55f, 0.45f) : s.StartsWith("  OK") ? new Color(0.6f, 1f, 0.6f) : Color.white;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            return root;
        }
    }
}
