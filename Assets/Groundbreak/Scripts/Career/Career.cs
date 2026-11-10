using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    public enum LotType { FlatSuburban, Sloped, Wooded }

    public class Contract
    {
        public string Id, Title, Client, Description; public int Price, Days, MinRep; public LotType Lot;
        public Vector2 LotFeet; public InspectionPhase[] Phases;
    }

    /// <summary>Career mode: job board contracts (shed → garage → house → two-story → custom), deadline, 10% deposit on
    /// signing, balance paid when the Final inspection passes, reputation and unlocks (tools, materials, lot types).</summary>
    public static class Career
    {
        public static readonly List<Contract> Contracts = new List<Contract>
        {
            new Contract { Id = "shed", Title = "Backyard storage shed 10'x12'", Client = "The Garcias", Price = 9500, Days = 12, MinRep = 0, Lot = LotType.FlatSuburban, LotFeet = new Vector2(60, 70),
                Description = "Wood-framed shed on a gravel pad, 4x4 skids, OSB sheathing, shingle roof.", Phases = new[] { InspectionPhase.Framing, InspectionPhase.Final } },
            new Contract { Id = "garage", Title = "Detached 2-car garage 24'x24'", Client = "Dan Whitaker", Price = 48000, Days = 30, MinRep = 55, Lot = LotType.FlatSuburban, LotFeet = new Vector2(80, 100),
                Description = "Monolithic slab, 2x6 walls, trusses, 16x7 door, 2 circuits.", Phases = new[] { InspectionPhase.Footing, InspectionPhase.Framing, InspectionPhase.RoughMEP, InspectionPhase.Final } },
            new Contract { Id = "house", Title = "Starter home 1,200 sq ft, 3 bed", Client = "Priya & Sam Patel", Price = 185000, Days = 90, MinRep = 65, Lot = LotType.Sloped, LotFeet = new Vector2(100, 140),
                Description = "Crawlspace foundation, full MEP, insulation, drywall, siding, landscaping.", Phases = (InspectionPhase[])Enum.GetValues(typeof(InspectionPhase)) },
            new Contract { Id = "twostory", Title = "Two-story family home 2,400 sq ft", Client = "The Okafors", Price = 395000, Days = 150, MinRep = 75, Lot = LotType.Sloped, LotFeet = new Vector2(120, 160),
                Description = "Basement, engineered floor system, 9' walls, 4 bed / 2.5 bath.", Phases = (InspectionPhase[])Enum.GetValues(typeof(InspectionPhase)) },
            new Contract { Id = "custom", Title = "Custom home 3,500 sq ft (architect plans)", Client = "Halvorsen family", Price = 720000, Days = 210, MinRep = 85, Lot = LotType.Wooded, LotFeet = new Vector2(160, 220),
                Description = "Wooded lot: clearing, walkout basement, timber accents, full landscape plan.", Phases = (InspectionPhase[])Enum.GetValues(typeof(InspectionPhase)) },
        };

        /// <summary>Unlocks by reputation (career only; Free Build has everything).</summary>
        static readonly Dictionary<string, int> _unlockRep = new Dictionary<string, int>
        {
            { "sodcutter", 55 }, { "tiller", 60 }, { "retaining-block", 55 }, { "gate-kit", 55 }, { "tree-spruce", 60 }, { "sod-roll", 55 },
        };

        public static int Reputation = 50;
        public static string ActiveId;
        public static int StartDay;
        public static readonly List<string> Completed = new List<string>();
        public static Contract Active => Contracts.Find(c => c.Id == ActiveId);
        public static int DueDay => Active != null ? StartDay + Active.Days : 0;
        public static int DaysLeft => Active != null && GameClock.I != null ? DueDay - GameClock.I.Day : 0;
        public static event Action<Contract, int> Paid;

        public static bool IsUnlocked(string id) => !GameSettings.IsCareer || !_unlockRep.TryGetValue(id, out int r) || Reputation >= r;
        public static string UnlockHint(string id) => _unlockRep.TryGetValue(id, out int r) ? $"reputation {r}" : "";
        public static bool CanTake(Contract c) => ActiveId == null && Reputation >= c.MinRep;

        public static void ResetState() { Reputation = 50; ActiveId = null; StartDay = 0; Completed.Clear(); Site.Lot = null; Site.LotName = ""; }

        /// <summary>Signs a contract: deposit, lot staked out (and graded/wooded per lot type), required inspections set.</summary>
        public static bool Accept(Contract c, out string msg)
        {
            if (!CanTake(c)) { msg = ActiveId != null ? "Finish your current contract first." : $"Needs reputation {c.MinRep}."; return false; }
            ActiveId = c.Id;
            StartDay = GameClock.I != null ? GameClock.I.Day : 1;
            Inspections.ResetState();
            Inspections.RequiredPhases = new List<InspectionPhase>(c.Phases);
            int deposit = c.Price / 10;
            Game.Wallet.Add(deposit, $"Deposit: {c.Title}");
            SetupLot(c);
            msg = $"Signed: {c.Title}. Deposit ${deposit:N0}. Due day {DueDay}.";
            Game.Toast(msg);
            return true;
        }

        /// <summary>Lot is laid out in front of the starting area (+Z), centered on x = 0.</summary>
        public static Rect LotRect(Contract c)
        {
            float w = c.LotFeet.x * Units.Foot, d = c.LotFeet.y * Units.Foot;
            return new Rect(-w / 2, 2f, w, d);
        }

        static GameObject _lotDecor;

        public static void SetupLot(Contract c)
        {
            var r = LotRect(c);
            Site.Lot = r; Site.LotName = c.Title;
            if (_lotDecor != null) UnityEngine.Object.Destroy(_lotDecor);
            _lotDecor = new GameObject("LotDecor");
            var k = new LandModel();
            var corners = new[] { new Vector3(r.xMin, 0, r.yMin), new Vector3(r.xMax, 0, r.yMin), new Vector3(r.xMax, 0, r.yMax), new Vector3(r.xMin, 0, r.yMax) };
            if (c.Lot == LotType.Sloped && Grade.Field == null)
            {
                Grade.Ensure(Site.LotCenter);
                float drop = 1.6f; // ~5' fall front to back
                Grade.Field.SetHeights((x, z) => Mathf.Clamp01((z - r.yMin) / r.height) * drop * -1f + 0.3f * Mathf.Sin(x * 0.07f));
            }
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i]; a.y = Grade.HeightAt(a.x, a.z);
                var b = corners[(i + 1) % 4]; b.y = Grade.HeightAt(b.x, b.z);
                LandModels.Stake(k, a);
                LandModels.String(k, a + Vector3.up * 0.4f, b + Vector3.up * 0.4f, new Color(1f, 0.3f, 0.65f));
            }
            k.Make(_lotDecor.transform, "LotLines");
            if (c.Lot == LotType.Wooded)
            {
                var rng = new System.Random(4242);
                for (int i = 0; i < 26; i++)
                {
                    float x = Mathf.Lerp(r.xMin + 3, r.xMax - 3, (float)rng.NextDouble()), z = Mathf.Lerp(r.yMin + r.height * 0.55f, r.yMax - 2, (float)rng.NextDouble());
                    var t = new GameObject("WoodedTree").transform; t.SetParent(_lotDecor.transform, false);
                    t.position = new Vector3(x, Grade.HeightAt(x, z), z);
                    bool con = rng.NextDouble() < 0.35;
                    int seed = i % 3;
                    LandModels.Instance(con ? "bigspruce" + seed : "oak" + seed, t, kk => { if (con) LandModels.Conifer(kk, 61 + seed, 14f, new Color(0.6f, 0.75f, 0.6f)); else LandModels.Deciduous(kk, 71 + seed, 3f, new Color(0.85f, 0.95f, 0.75f)); });
                    var col = t.gameObject.AddComponent<CapsuleCollider>(); col.radius = 0.3f; col.height = 6f; col.center = Vector3.up * 3f;
                }
            }
        }

        /// <summary>Pays the balance when the Final inspection passes on the active job.</summary>
        static void OnInspection(InspectionResult r)
        {
            if (!GameSettings.IsCareer || Active == null || r.Phase != InspectionPhase.Final || !r.Passed) return;
            if (Inspections.CurrentPhase.HasValue) return; // other required phases still open
            var c = Active;
            int day = GameClock.I != null ? GameClock.I.Day : StartDay;
            int late = Mathf.Max(0, day - DueDay);
            float penalty = Mathf.Min(0.3f, late * 0.01f);
            int balance = Mathf.RoundToInt(c.Price * 0.9f - c.Price * penalty);
            Game.Wallet.Add(balance, $"Final payment: {c.Title}" + (late > 0 ? $" (-{penalty:P0} late)" : ""));
            int fails = 0; foreach (var p in c.Phases) fails += Inspections.FailCount(p);
            int repGain = (late == 0 ? 8 : 2) - Mathf.Min(6, fails * 2) - late / 5;
            Reputation = Mathf.Clamp(Reputation + repGain, 0, 100);
            Completed.Add(c.Id);
            ActiveId = null;
            Game.Toast($"Certificate of occupancy! Paid ${balance:N0}. Reputation {Reputation} ({(repGain >= 0 ? "+" : "")}{repGain})");
            Sfx.Play("success", Vector3.zero);
            Paid?.Invoke(c, balance);
        }

        // ------------------------------------------------------------------ job board app

        static void JobBoard()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle; float fs = label.fontSize;
            if (!GameSettings.IsCareer) { GUILayout.Label("Job board is for Career mode. (Main menu: F10)", header); return; }
            GUILayout.BeginHorizontal();
            GUILayout.Label(UISkin.Icon("star"), GUILayout.Width(fs * 1.5f), GUILayout.Height(fs * 1.5f));
            GUILayout.Label($"Reputation {Reputation}/100   ·   Jobs completed: {Completed.Count}", header);
            GUILayout.EndHorizontal();
            if (Active != null)
            {
                GUILayout.Label($"Current job: {Active.Title} for {Active.Client}", header);
                GUILayout.Label($"Contract ${Active.Price:N0} · due day {DueDay} ({DaysLeft} days left) · next inspection: {Inspections.CurrentPhaseLabel}", label);
            }
            GUILayout.Space(8);
            foreach (var c in Contracts)
            {
                bool done = Completed.Contains(c.Id), locked = Reputation < c.MinRep;
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label($"{c.Title}  -  ${c.Price:N0}, {c.Days} days, {c.Lot} lot {c.LotFeet.x}'x{c.LotFeet.y}'", header);
                GUILayout.Label($"{c.Client}: {c.Description}  Inspections: {string.Join(", ", Array.ConvertAll(c.Phases, Inspections.Label))}", label);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUI.enabled = CanTake(c);
                string b = done ? "Completed" : locked ? $"Rep {c.MinRep}" : ActiveId == c.Id ? "Active" : "Sign";
                if (GUILayout.Button(b, button, GUILayout.Width(fs * 7))) { Accept(c, out var m); t.ShowMessage(m); }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            GUILayout.Label("Unlocks: rep 55 sod, wall block, gates, sod cutter · rep 60 tiller, spruce · lots: sloped at 65, wooded at 85.", label);
        }

        // ------------------------------------------------------------------ save

        [Serializable] class Data { public int rep = 50; public string active; public int start; public List<string> done = new List<string>(); }
        static string Save() => JsonUtility.ToJson(new Data { rep = Reputation, active = ActiveId, start = StartDay, done = new List<string>(Completed) });
        static void Load(string json)
        {
            var reqs = new List<InspectionPhase>(Inspections.RequiredPhases);
            ResetState();
            if (_lotDecor != null) UnityEngine.Object.Destroy(_lotDecor);
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json);
            Reputation = d.rep; ActiveId = string.IsNullOrEmpty(d.active) ? null : d.active; StartDay = d.start; Completed.AddRange(d.done);
            if (Active != null) SetupLot(Active);
            Inspections.RequiredPhases = reqs;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { ResetState(); Paid = null; _lotDecor = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Tablet.RegisterApp("Job Board", JobBoard);
            SaveSystem.RegisterExtension("career", Save, Load);
            Inspections.Completed += OnInspection;
        }
    }
}
