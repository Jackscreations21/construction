using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The house shell being built, in real order: floor → walls one at a time (flat, tilt, brace) →
    /// double top plates → wall sheathing → openings flashed → roof framing → roof sheathing → dry-in → shingles.</summary>
    public class ShellProject
    {
        public static ShellProject Current;
        public Footprint Fp;
        public readonly List<WallOpening>[] Openings = { new List<WallOpening>(), new List<WallOpening>(), new List<WallOpening>(), new List<WallOpening>() };
        public FramingJob Floor, Caps, Sheathing;
        public WallAssembly[] Walls;
        public readonly FramingJob[] WallJobs = new FramingJob[4];
        public readonly List<FramingJob> Jobs = new List<FramingJob>();
        public int PitchRise = 6;
        public bool StickFrame, Hip;
        /// <summary>Set by the roofing module: builds the roof jobs in order for this project.</summary>
        public static System.Func<ShellProject, FramingJob> RoofFramingHook, RoofSheathingHook, DryInHook, ShinglesHook;
        public FramingJob Roof, RoofSheathing, DryIn, Shingles;

        public static ShellProject Start(Footprint fp)
        {
            Current = new ShellProject { Fp = fp };
            return Current;
        }

        FramingJob Track(FramingJob j) { if (j != null) Jobs.Add(j); return j; }

        public FramingJob StartFloor() => Floor ?? (Floor = Track(FloorPlan.Build(Fp)));

        /// <summary>Frames the walls one at a time so only one lies on the deck: wall i can start when wall i-1 stands.</summary>
        public FramingJob StartWall(int i)
        {
            if (Walls == null) Walls = WallPlan.ExteriorWalls(Fp, Openings);
            if (WallJobs[i] == null) WallJobs[i] = Track(WallPlan.Frame(Walls[i]));
            return WallJobs[i];
        }

        public FramingJob NextWall()
        {
            for (int i = 0; i < 4; i++)
            {
                if (WallJobs[i] == null) return StartWall(i);
                if (!WallJobs[i].Done) return WallJobs[i];
            }
            return null;
        }

        public bool WallsDone { get { for (int i = 0; i < 4; i++) if (WallJobs[i] == null || !WallJobs[i].Done) return false; return true; } }

        public FramingJob StartCaps() => Caps ?? (Caps = Track(WallPlan.CapPlates(Walls)));
        public FramingJob StartSheathing() => Sheathing ?? (Sheathing = Track(WallPlan.Sheathe(Walls)));
        public FramingJob StartRoof() => Roof ?? (Roof = Track(RoofFramingHook?.Invoke(this)));
        public FramingJob StartRoofSheathing() => RoofSheathing ?? (RoofSheathing = Track(RoofSheathingHook?.Invoke(this)));
        public FramingJob StartDryIn() => DryIn ?? (DryIn = Track(DryInHook?.Invoke(this)));
        public FramingJob StartShingles() => Shingles ?? (Shingles = Track(ShinglesHook?.Invoke(this)));

        public List<FramingJob> FlashOpenings()
        {
            var l = new List<FramingJob>();
            foreach (var ro in RoughOpening.All) if (!ro.Flashed) l.Add(Track(ro.FlashJob()));
            return l;
        }

        /// <summary>Whatever should be worked on next (null when the shell is done).</summary>
        public FramingJob Active
        {
            get
            {
                foreach (var j in Jobs) if (j != null && !j.Done) return j;
                return null;
            }
        }
    }

    /// <summary>Tablet app "Framing Plans": set the footprint, choose door/window products for the openings,
    /// start each stage, order its material takeoff, or hire a crew to finish a stage.</summary>
    public static class ShellApp
    {
        static int _lenFt = 16, _widFt = 12;
        static int _doorIdx, _winIdx;
        static Vector2 _scroll;

        public static void Register() => Tablet.RegisterApp("Framing Plans", Draw);

        static List<PieceDefinition> Of(PieceCategory c) => Catalog.All.FindAll(d => d.Category == c && (c != PieceCategory.Door || d.Id.Contains("ext")));

        static void Draw()
        {
            var t = Tablet.I;
            var label = t != null ? t.LabelStyle : GUI.skin.label;
            var head = t != null ? t.HeaderStyle : GUI.skin.label;
            var btn = t != null ? t.ButtonStyle : GUI.skin.button;
            var p = ShellProject.Current;
            GUILayout.Label("Framing Plans", head);
            if (p == null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Length " + _lenFt + "'", label, GUILayout.Width(120));
                if (GUILayout.Button("-", btn, GUILayout.Width(40))) _lenFt = Mathf.Max(8, _lenFt - 2);
                if (GUILayout.Button("+", btn, GUILayout.Width(40))) _lenFt = Mathf.Min(48, _lenFt + 2);
                GUILayout.Label("  Width " + _widFt + "'", label, GUILayout.Width(120));
                if (GUILayout.Button("-", btn, GUILayout.Width(40))) _widFt = Mathf.Max(8, _widFt - 2);
                if (GUILayout.Button("+", btn, GUILayout.Width(40))) _widFt = Mathf.Min(32, _widFt + 2);
                GUILayout.EndHorizontal();
                var doors = Of(PieceCategory.Door); var wins = Of(PieceCategory.Window);
                if (doors.Count > 0) { GUILayout.BeginHorizontal(); GUILayout.Label("Front door: " + doors[_doorIdx % doors.Count].Name, label); if (GUILayout.Button("Change", btn, GUILayout.Width(100))) _doorIdx++; GUILayout.EndHorizontal(); }
                if (wins.Count > 0) { GUILayout.BeginHorizontal(); GUILayout.Label("Windows: " + wins[_winIdx % wins.Count].Name, label); if (GUILayout.Button("Change", btn, GUILayout.Width(100))) _winIdx++; GUILayout.EndHorizontal(); }
                GUILayout.Label("The building starts at the grid origin (O key) facing its yaw. Sill sits on the ground / slab.", label);
                if (GUILayout.Button("Start project here", btn))
                {
                    var fp = Footprint.Make(Game.Snap.Origin, Game.Snap.OriginYaw, _lenFt, _widFt);
                    p = ShellProject.Start(fp);
                    if (doors.Count > 0) p.Openings[0].Add(new WallOpening { Product = doors[_doorIdx % doors.Count], CenterIn = _lenFt * 12f / 2f - 24f });
                    if (wins.Count > 0)
                    {
                        p.Openings[0].Add(new WallOpening { Product = wins[_winIdx % wins.Count], CenterIn = _lenFt * 12f / 2f + 40f });
                        p.Openings[2].Add(new WallOpening { Product = wins[_winIdx % wins.Count], CenterIn = _lenFt * 12f / 2f });
                    }
                    p.StartFloor();
                    t?.ShowMessage("Floor deck laid out - chalk line first, then sills");
                }
                return;
            }

            GUILayout.Label($"Project {Units.Format(p.Fp.Length)} x {Units.Format(p.Fp.Width)} · roof {p.PitchRise}/12 " + (p.StickFrame ? (p.Hip ? "stick hip" : "stick gable") : "trusses"), label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pitch -", btn)) p.PitchRise = Mathf.Max(3, p.PitchRise - 1);
            if (GUILayout.Button("Pitch +", btn)) p.PitchRise = Mathf.Min(12, p.PitchRise + 1);
            if (GUILayout.Button(p.StickFrame ? "Use trusses" : "Stick frame", btn)) p.StickFrame = !p.StickFrame;
            if (p.StickFrame && GUILayout.Button(p.Hip ? "Gable" : "Hip", btn)) p.Hip = !p.Hip;
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Screen.height * 0.35f));
            foreach (var j in p.Jobs)
            {
                if (j == null) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label((j.Done ? "[done] " : "[" + j.Next + "/" + j.Steps.Count + "] ") + j.Title + (j.Done ? "" : " - next: " + j.Current.Label), label);
                if (!j.Done)
                {
                    if (GUILayout.Button("Order mat'l", btn, GUILayout.Width(110))) Order(j);
                    int cost = (j.Steps.Count - j.Next) * 6;
                    if (GUILayout.Button("Crew $" + cost, btn, GUILayout.Width(110)))
                    {
                        if (Game.Wallet.TrySpend(cost, "Framing crew: " + j.Title)) j.RunAll(); else t?.ShowMessage("Not enough money");
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Label("Next stage:", head);
            if (p.Floor != null && p.Floor.Done && !p.WallsDone && GUILayout.Button("Frame next wall (flat on the deck)", btn)) p.NextWall();
            if (p.WallsDone && p.Caps == null && GUILayout.Button("Double top plates", btn)) p.StartCaps();
            if (p.Caps != null && p.Caps.Done && p.Sheathing == null && GUILayout.Button("Sheathe walls (7/16 OSB)", btn)) p.StartSheathing();
            if (p.WallsDone && RoughOpening.All.Exists(r => !r.Flashed) && GUILayout.Button("Flash rough openings", btn)) p.FlashOpenings();
            if (p.Caps != null && p.Caps.Done && p.Roof == null && ShellProject.RoofFramingHook != null && GUILayout.Button(p.StickFrame ? "Stick-frame the roof" : "Order trusses (boom truck sets them)", btn)) p.StartRoof();
            if (p.Roof != null && p.Roof.Done && p.RoofSheathing == null && ShellProject.RoofSheathingHook != null && GUILayout.Button("Roof sheathing + H-clips", btn)) p.StartRoofSheathing();
            if (p.RoofSheathing != null && p.RoofSheathing.Done && p.DryIn == null && ShellProject.DryInHook != null && GUILayout.Button("Dry-in: drip edge, ice & water, underlayment", btn)) p.StartDryIn();
            if (p.DryIn != null && p.DryIn.Done && p.Shingles == null && ShellProject.ShinglesHook != null && GUILayout.Button("Shingles: starter, courses, ridge vent + cap", btn)) p.StartShingles();
            if (GUILayout.Button("Request framing inspection", btn)) { FramingInspection.Run(); Object.FindAnyObjectByType<InspectionMarkers>()?.Refresh(); }
            if (FramingInspection.HasRun) GUILayout.Label(FramingInspection.Report(), label);
        }

        static void Order(FramingJob j)
        {
            var cart = new Dictionary<string, int>();
            foreach (var kv in j.Takeoff())
            {
                var d = Catalog.Get(kv.Key); if (d == null) continue;
                int need = kv.Value - Game.Stock.Count(kv.Key);
                if (need <= 0) continue;
                int per = Mathf.Max(1, d.UnitsPerPurchase);
                cart[kv.Key] = Mathf.CeilToInt(need / (float)per);
            }
            if (cart.Count == 0) { Tablet.I?.ShowMessage("Everything for this stage is on site"); return; }
            DeliveryService.I?.Order(cart, false);
            Tablet.I?.ShowMessage("Ordered the takeoff for " + j.Title);
        }
    }
}
