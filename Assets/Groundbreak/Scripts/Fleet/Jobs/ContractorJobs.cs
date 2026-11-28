using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Result of checking the real work on a job's lot.</summary>
    public class JobCheck
    {
        public bool Ok;
        public float Quality;   // 0..1
        public List<string> Done = new List<string>();
        public List<string> Missing = new List<string>();
        /// <summary>Workmanship remarks that cost quality but don't block payment.</summary>
        public List<string> Notes = new List<string>();
    }

    /// <summary>Everything about a contractor job, stored as JSON in <see cref="Job.Payload"/> (Company saves the Job).</summary>
    [Serializable]
    public class ContractorJobSpec
    {
        public string type = "";
        public int seed, lotIndex, offeredDay;
        /// <summary>deck: width along the house x depth out from it · slab: width x length · shed/garage/re-roof: building
        /// width x length · fence: lengthFt = linear feet.</summary>
        public float widthFt, lengthFt;
        public float heightIn;          // deck surface above grade
        public float thicknessIn;       // slab
        public int psi;                 // slab
        public bool broom;              // slab finish
        public int gates;               // fence
        public int pitch = 6;           // roof rise per 12
        public float squares;           // re-roof (100 sq ft each)
        public Vector3 center;          // lot center, y = 0
        public float lotX, lotZ, lotW, lotH;
        public string address = "";
        /// <summary>Checklist items that were verified once. Used only when the system that produced them does not
        /// survive a save/load (decks, concrete elements and roofs are not persisted by their own features).</summary>
        public List<string> latched = new List<string>();
        [NonSerialized] public bool dirty;

        public Rect Lot => new Rect(lotX, lotZ, lotW, lotH);
    }

    /// <summary>
    /// Contractor-stage work (JobKind.Remodel / JobKind.Outbuilding): decks, fences, sheds, garages, driveways, patios and
    /// re-roofs on customer lots. Generates fully specified offers, material takeoffs, and verifies the real work on the
    /// lot with the game's own systems (DeckBuilder, LandscapeFeature, ConcreteElement, RoofInfo, Inspections).
    /// World objects for a job (lot stakes, the customer's existing wall/house) live in <see cref="ContractorJobSite"/>.
    /// </summary>
    public static class ContractorJobs
    {
        public static readonly string[] Types = { "deck", "fence", "shed", "garage", "driveway", "patio", "reroof" };
        /// <summary>Side of the square work lot around TownHook.JobLot (meters).</summary>
        public const float LotSize = 30f;
        /// <summary>Unaccepted offers are withdrawn after this many game days.</summary>
        public const int OfferLifeDays = 4;
        public const string IdPrefix = "cj-";

        static readonly string[] Clients =
        {
            "Dana Whitaker", "Marcus Bell", "The Okafor family", "Priya Raman", "Tom and Linda Kowalski", "Elena Vasquez",
            "Greg Sandoval", "The Nguyen family", "Ruth Abernathy", "Sam Castellano", "Hannah Lindqvist", "Walt Pruitt",
        };

        static readonly Dictionary<string, KeyValuePair<string, ContractorJobSpec>> _specCache = new Dictionary<string, KeyValuePair<string, ContractorJobSpec>>();
        static int _lastLot = -1;

        static int Today => GameClock.I != null ? GameClock.I.Day : 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _specCache.Clear(); _lastLot = -1; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Company.DayStarted += OnDayStarted;
            Company.StageChanged += OnStageChanged;
        }

        static void OnDayStarted(int day)
        {
            if (GameSettings.IsCareer && Company.Stage >= CompanyStage.Contractor) EnsureOffers();
        }

        static void OnStageChanged(CompanyStage stage)
        {
            if (GameSettings.IsCareer && stage >= CompanyStage.Contractor) EnsureOffers();
        }

        // ------------------------------------------------------------------ spec access

        public static bool IsContractorJob(Job j) =>
            j != null && (j.Kind == JobKind.Remodel || j.Kind == JobKind.Outbuilding) && j.Id != null && j.Id.StartsWith(IdPrefix) && !string.IsNullOrEmpty(j.Payload);

        /// <summary>The job's spec, parsed from its payload (cached per payload string). Null for other jobs.</summary>
        public static ContractorJobSpec SpecOf(Job j)
        {
            if (!IsContractorJob(j)) return null;
            if (_specCache.TryGetValue(j.Id, out var kv) && kv.Key == j.Payload) return kv.Value;
            ContractorJobSpec s = null;
            try { s = JsonUtility.FromJson<ContractorJobSpec>(j.Payload); }
            catch (Exception e) { Debug.LogWarning("[ContractorJobs] bad payload on " + j.Id + ": " + e.Message); }
            if (s == null || string.IsNullOrEmpty(s.type)) return null;
            if (s.latched == null) s.latched = new List<string>();
            _specCache[j.Id] = new KeyValuePair<string, ContractorJobSpec>(j.Payload, s);
            return s;
        }

        static void StoreSpec(Job j, ContractorJobSpec s)
        {
            s.dirty = false;
            j.Payload = JsonUtility.ToJson(s);
            _specCache[j.Id] = new KeyValuePair<string, ContractorJobSpec>(j.Payload, s);
        }

        public static string TypeOf(Job j) { var s = SpecOf(j); return s != null ? s.type : ""; }

        public static Rect LotOf(Job j)
        {
            var s = SpecOf(j);
            if (s != null) return s.Lot;
            var p = j != null ? j.WorldPos : Vector3.zero;
            return new Rect(p.x - LotSize / 2, p.z - LotSize / 2, LotSize, LotSize);
        }

        static void SetLot(ContractorJobSpec s, Vector3 center)
        {
            center.y = 0f;
            s.center = center;
            s.lotX = center.x - LotSize / 2; s.lotZ = center.z - LotSize / 2; s.lotW = LotSize; s.lotH = LotSize;
        }

        /// <summary>Moves a job that has not been accepted yet to another lot (tests, or the town re-assigning a lot).</summary>
        public static void Relocate(Job j, Vector3 center, string address)
        {
            var s = SpecOf(j);
            if (s == null) return;
            SetLot(s, center);
            if (!string.IsNullOrEmpty(address)) { s.address = address; j.Address = address; }
            j.WorldPos = s.center;
            StoreSpec(j, s);
        }

        // ------------------------------------------------------------------ lot indices

        static int LotIndexOfId(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith(IdPrefix)) return -1;
            int k = id.LastIndexOf("-L", StringComparison.Ordinal);
            if (k < 0) return -1;
            return int.TryParse(id.Substring(k + 2), out int n) ? n : -1;
        }

        /// <summary>Every job gets a fresh lot: one past the highest index ever offered, taken or completed.</summary>
        static int NextLotIndex()
        {
            int max = _lastLot;
            foreach (var o in Company.Offers) max = Mathf.Max(max, LotIndexOfId(o.Id));
            foreach (var a in Company.ActiveJobs) max = Mathf.Max(max, LotIndexOfId(a.Id));
            foreach (var id in Company.CompletedJobIds) max = Mathf.Max(max, LotIndexOfId(id));
            _lastLot = max + 1;
            return _lastLot;
        }

        // ------------------------------------------------------------------ generator

        static T Pick<T>(System.Random rng, params T[] options) => options[rng.Next(options.Length)];

        /// <summary>Roof surface in sq ft for a gable roof over a building (12" overhangs all round).</summary>
        public static float RoofAreaSqFt(float lengthFt, float widthFt, int pitch)
        {
            float r = pitch / 12f;
            return (lengthFt + 2f) * (widthFt + 2f) * Mathf.Sqrt(1f + r * r);
        }

        /// <summary>Builds a fully specified offer. Does not add it to Company.Offers.</summary>
        public static Job Create(string type, int seed)
        {
            if (Array.IndexOf(Types, type) < 0) type = Types[Mathf.Abs(seed % Types.Length)];
            var rng = new System.Random(seed);
            int lot = NextLotIndex();
            string address;
            Vector3 c = TownHook.JobLot(lot, out address);
            var s = new ContractorJobSpec { type = type, seed = seed, lotIndex = lot, address = address ?? "", offeredDay = Today };
            SetLot(s, c);

            string title, desc, vehicle;
            int payout, days, rep = 0;
            var kind = JobKind.Remodel;
            switch (type)
            {
                case "deck":
                {
                    s.widthFt = Pick(rng, 12f, 16f, 20f); s.lengthFt = Pick(rng, 10f, 12f); s.heightIn = Pick(rng, 30f, 36f);
                    title = $"{s.lengthFt:0}' x {s.widthFt:0}' pressure-treated deck";
                    desc = $"Build a {s.lengthFt:0}' x {s.widthFt:0}' pressure-treated deck on the back of the house, {s.heightIn:0}\" above grade: " +
                           "2x10 ledger flashed and lagged to the rim 16\" O.C., 10\" piers below frost, 6x6 posts, doubled 2x10 beam, " +
                           "2x8 joists 16\" O.C. in hangers, 5/4x6 decking, and 36\" guards with balusters under 4\" apart. " +
                           "The stakes mark the house wall it attaches to.";
                    payout = Mathf.RoundToInt(45f * s.widthFt * s.lengthFt + 600f);
                    vehicle = s.widthFt <= 12f ? "pickup" : "flatbed";
                    days = 5;
                    break;
                }
                case "fence":
                {
                    s.lengthFt = Pick(rng, 100f, 120f, 140f, 160f, 180f); s.gates = 1;
                    title = $"{s.lengthFt:0} LF cedar privacy fence";
                    desc = $"Install {s.lengthFt:0} linear feet of 6' cedar privacy fence around the back yard with one 4' gate: " +
                           "4x4 posts 8' O.C. set 24\" deep in concrete, three 2x4 rails per bay, dog-ear pickets. " +
                           "Stakes mark the fence corners.";
                    payout = Mathf.RoundToInt(38f * s.lengthFt + 450f * s.gates);
                    vehicle = "pickup";
                    days = 3;
                    break;
                }
                case "shed":
                {
                    int k = rng.Next(3);
                    s.lengthFt = k == 0 ? 12f : k == 1 ? 12f : 16f; s.widthFt = k == 0 ? 8f : k == 1 ? 10f : 12f; s.pitch = 6;
                    kind = JobKind.Outbuilding;
                    title = $"{s.widthFt:0}' x {s.lengthFt:0}' gable storage shed";
                    desc = $"Build a {s.widthFt:0}' x {s.lengthFt:0}' gable shed in the back yard: framed floor on treated sills, 2x4 walls 16\" O.C. " +
                           "with a 36\" door, 7/16\" OSB sheathing, 6/12 trussed roof, dried in and shingled. Stakes mark the corners " +
                           "(set the grid origin on the flagged corner and start it from Framing Plans).";
                    payout = Mathf.RoundToInt(75f * s.widthFt * s.lengthFt);
                    vehicle = "flatbed";
                    days = 6; rep = 10;
                    break;
                }
                case "garage":
                {
                    int k = rng.Next(3);
                    s.lengthFt = k == 0 ? 20f : 24f; s.widthFt = k == 0 ? 20f : k == 1 ? 22f : 24f; s.pitch = 6;
                    s.thicknessIn = 4f; s.psi = 3000;
                    kind = JobKind.Outbuilding;
                    title = $"{s.widthFt:0}' x {s.lengthFt:0}' detached 2-car garage";
                    desc = $"Build a {s.widthFt:0}' x {s.lengthFt:0}' detached two-car garage: 4\" 3000 psi slab on compacted base with thickened edges " +
                           "and anchor bolts, 2x4 walls 16\" O.C., OSB sheathing, 6/12 trusses 24\" O.C. set with the boom truck, " +
                           "dried in and shingled, and a 16x7 overhead door. Stakes mark the corners.";
                    payout = Mathf.RoundToInt(70f * s.widthFt * s.lengthFt);
                    vehicle = "boom";
                    days = 21; rep = 30;
                    break;
                }
                case "driveway":
                {
                    int k = rng.Next(3);
                    s.widthFt = k == 0 ? 10f : k == 1 ? 12f : 16f; s.lengthFt = k == 0 ? 30f : 40f;
                    s.thicknessIn = 4f; s.psi = 4000; s.broom = true;
                    title = $"{s.widthFt:0}' x {s.lengthFt:0}' concrete driveway";
                    desc = $"Tear out and replace the driveway: {s.widthFt:0}' x {s.lengthFt:0}', 4\" of 4000 psi concrete on 4\" of compacted base, " +
                           "#4 rebar 18\" O.C. on chairs, control joints, broom finish. Haul off the spoils. Stakes mark the corners.";
                    payout = Mathf.RoundToInt(11f * s.widthFt * s.lengthFt);
                    vehicle = "dump";
                    days = 9; rep = 10;
                    break;
                }
                case "patio":
                {
                    int k = rng.Next(3);
                    s.widthFt = k == 0 ? 10f : k == 1 ? 12f : 16f; s.lengthFt = k == 0 ? 12f : k == 1 ? 14f : 20f;
                    s.thicknessIn = 4f; s.psi = 3000; s.broom = true;
                    title = $"{s.widthFt:0}' x {s.lengthFt:0}' concrete patio";
                    desc = $"Pour a {s.widthFt:0}' x {s.lengthFt:0}' patio behind the house: 4\" of 3000 psi concrete on 4\" of compacted base, " +
                           "#3 rebar 18\" O.C., control joints, light broom finish. Stakes mark the corners.";
                    payout = Mathf.RoundToInt(14f * s.widthFt * s.lengthFt + 400f);
                    vehicle = "dump";
                    days = 7;
                    break;
                }
                default: // reroof
                {
                    int k = rng.Next(3);
                    s.lengthFt = k == 0 ? 40f : k == 1 ? 44f : 48f; s.widthFt = k == 0 ? 26f : k == 1 ? 28f : 30f; s.pitch = 6;
                    s.squares = Mathf.Round(RoofAreaSqFt(s.lengthFt, s.widthFt, s.pitch) / 100f);
                    title = $"Re-roof, {s.squares:0} squares";
                    desc = $"Re-roof the house ({s.squares:0} squares, 6/12 gable): the old shingles are torn off down to the deck and go out in the dump truck; " +
                           "install drip edge, ice & water shield at the eaves, synthetic underlayment, starter, architectural shingles, " +
                           "ridge vent and ridge cap. Start the dry-in and the shingles from the Contractor Jobs app.";
                    payout = Mathf.RoundToInt(520f * s.squares / 10f) * 10;
                    vehicle = "dump";
                    days = 3; rep = 15;
                    break;
                }
            }

            var j = new Job
            {
                Id = $"{IdPrefix}{type}-{seed & 0x7fffffff}-L{lot}",
                Title = title,
                Client = Clients[rng.Next(Clients.Length)],
                Description = desc,
                Address = s.address,
                Kind = kind,
                Payout = Mathf.Clamp(payout, 2000, 50000),
                DueDay = Today + 2 + days,
                MinStage = CompanyStage.Contractor,
                MinReputation = rep,
                RequiredVehicleType = vehicle,
                RequiredTrade = null,
                WorldPos = s.center,
            };
            StoreSpec(j, s);
            j.MaterialBudget = TakeoffCost(j);
            return j;
        }

        /// <summary>Withdraws stale contractor offers and tops the job board up to <paramref name="target"/> of them,
        /// preferring job types that are not on the board yet.</summary>
        public static void EnsureOffers(int target = 5)
        {
            int today = Today;
            bool changed = false;
            for (int i = Company.Offers.Count - 1; i >= 0; i--)
            {
                var o = Company.Offers[i];
                var s = SpecOf(o);
                if (s == null) continue;
                if (today - s.offeredDay > OfferLifeDays) { Company.Offers.RemoveAt(i); _specCache.Remove(o.Id); changed = true; }
            }

            var have = new List<string>();
            int count = 0;
            foreach (var o in Company.Offers) { var s = SpecOf(o); if (s != null) { count++; have.Add(s.type); } }
            foreach (var a in Company.ActiveJobs) { var s = SpecOf(a); if (s != null) have.Add(s.type); }

            int start = UnityEngine.Random.Range(0, Types.Length);
            int guard = 0;
            while (count < target && guard++ < 64)
            {
                string type = null;
                for (int k = 0; k < Types.Length; k++)
                {
                    string t = Types[(start + k) % Types.Length];
                    if (!have.Contains(t)) { type = t; break; }
                }
                if (type == null) type = Types[(start + guard) % Types.Length];
                Job j;
                try { j = Create(type, UnityEngine.Random.Range(1, 1000000)); }
                catch (Exception e) { Debug.LogException(e); break; }
                Company.Offers.Add(j);
                have.Add(type);
                count++; changed = true;
            }
            if (changed) Company.NotifyChanged();
        }

        // ------------------------------------------------------------------ layout helpers (shared with the site markers)

        /// <summary>Fence line as 4 corner points (three runs, U-shape open to the street side, -Z) totalling the job's linear feet.</summary>
        public static Vector3[] FenceLine(Job j)
        {
            var s = SpecOf(j);
            if (s == null) return new Vector3[0];
            FenceRuns(s, out float a, out float b);
            float ha = a * Units.Foot / 2f, hb = b * Units.Foot / 2f;
            var c = s.center;
            return new[]
            {
                c + new Vector3(-hb, 0, -ha), c + new Vector3(-hb, 0, ha), c + new Vector3(hb, 0, ha), c + new Vector3(hb, 0, -ha),
            };
        }

        /// <summary>Where the 4' gate goes: from the last fence corner back toward the house side.</summary>
        public static void GateLine(Job j, out Vector3 a, out Vector3 b)
        {
            var pts = FenceLine(j);
            a = pts.Length == 4 ? pts[3] : Vector3.zero;
            b = a + Vector3.left * 1.4f;
        }

        /// <summary>Side run a (x2) and back run b in feet, 2a + b = linear feet.</summary>
        static void FenceRuns(ContractorJobSpec s, out float a, out float b)
        {
            b = Mathf.Round(s.lengthFt * 0.36f / 2f) * 2f;
            a = (s.lengthFt - b) / 2f;
        }

        /// <summary>World rectangle (XZ) of the slab / building the job asks for, centered on the lot. Long side along Z for
        /// slabs, along X for buildings (Footprint length).</summary>
        public static Rect WorkRect(ContractorJobSpec s)
        {
            bool building = s.type == "shed" || s.type == "garage" || s.type == "reroof";
            float x = (building ? s.lengthFt : s.widthFt) * Units.Foot, z = (building ? s.widthFt : s.lengthFt) * Units.Foot;
            return new Rect(s.center.x - x / 2f, s.center.z - z / 2f, x, z);
        }

        // ------------------------------------------------------------------ takeoff

        /// <summary>Ready-mix to order through Concrete Dispatch (cubic yards, +10% waste, rounded up to 1/4 yd). 0 if none.</summary>
        public static float ReadyMixYards(Job j)
        {
            var s = SpecOf(j);
            if (s == null || s.thicknessIn <= 0f) return 0f;
            float area = s.widthFt * s.lengthFt;
            float yd = area * (s.thicknessIn / 12f) / 27f;
            if (s.type == "garage") yd += 2f * (s.widthFt + s.lengthFt) * (8f / 12f) * 1f / 27f; // 12" x 12" thickened edge below the 4" slab
            return Mathf.Ceil(yd * 1.10f * 4f - 0.01f) / 4f;
        }

        static int ReadyMixPricePerYard(int psi) => psi >= 4000 ? 180 : psi >= 3000 ? 165 : 155;

        /// <summary>Adds what the job needs in base units (pieces, cubic feet, sq ft...) and converts to purchase quantity.
        /// Ids that are not in the catalog are skipped.</summary>
        static void Need(Dictionary<string, int> t, string id, float units)
        {
            if (units <= 0f) return;
            var d = Catalog.Get(id);
            if (d == null) return;
            int per = Mathf.Max(1, d.UnitsPerPurchase);
            int qty = Mathf.Max(1, Mathf.CeilToInt(units / per - 0.0001f));
            t[id] = (t.TryGetValue(id, out int n) ? n : 0) + qty;
        }

        /// <summary>Material takeoff: Catalog piece id -> purchase quantity (boxes, rolls, sticks, cubic yards).
        /// Ready-mix concrete is not a catalog item: see <see cref="ReadyMixYards"/>.</summary>
        public static Dictionary<string, int> Takeoff(Job j)
        {
            var t = new Dictionary<string, int>();
            var s = SpecOf(j);
            if (s == null) return t;
            float W = s.widthFt, L = s.lengthFt;
            switch (s.type)
            {
                case "deck":
                {
                    float D = L;                                              // depth out from the house
                    int spans = Mathf.CeilToInt(W / 12f);
                    int joists = Mathf.FloorToInt(W * 12f / 16f) + 2;
                    int rows = Mathf.FloorToInt((D * 12f + 0.75f) / 5.625f);
                    float guardFt = 2f * D + W;
                    Need(t, ExteriorCatalog.PT2x10, spans * 3);               // ledger + doubled beam
                    Need(t, ExteriorCatalog.LedgerScrews, Mathf.FloorToInt(W * 12f / 16f) + 1);
                    Need(t, ExteriorCatalog.ZFlash, Mathf.CeilToInt(W * 12f / 120f));
                    Need(t, ExteriorCatalog.Sonotube, 3);
                    Need(t, ExteriorCatalog.Concrete, 9);
                    Need(t, ExteriorCatalog.PostBase, 3);
                    Need(t, ExteriorCatalog.PT6x6, 3);
                    Need(t, ExteriorCatalog.PT2x8, joists + spans);           // joists + rim
                    Need(t, ExteriorCatalog.JoistHanger, joists);
                    Need(t, ExteriorCatalog.Decking, rows * 2);
                    Need(t, ExteriorCatalog.DeckScrews, rows * joists * 2);
                    Need(t, ExteriorCatalog.PT4x4, Mathf.CeilToInt(guardFt / 6f) + 4);
                    Need(t, ExteriorCatalog.Baluster, Mathf.CeilToInt(guardFt * 12f / 5.25f));
                    Need(t, ExteriorCatalog.PT2x4, 6);
                    Need(t, ExteriorCatalog.PT2x6, 4);
                    break;
                }
                case "fence":
                {
                    FenceRuns(s, out float a, out float b);
                    int baysA = Mathf.CeilToInt(a / 8f - 0.01f), baysB = Mathf.CeilToInt(b / 8f - 0.01f);
                    int bays = 2 * baysA + baysB;
                    int posts = 2 * (baysA + 1) + (baysB + 1) + 2 * s.gates;
                    int pickets = 2 * baysA * Mathf.CeilToInt(a * 12f / baysA / 5.5f) + baysB * Mathf.CeilToInt(b * 12f / baysB / 5.5f);
                    Need(t, "4x4x8", posts);
                    Need(t, "postmix-50", posts * 2);
                    Need(t, "pt2x4x8", bays * 3);
                    Need(t, "picket-cedar", Mathf.CeilToInt(pickets * 1.05f));
                    Need(t, "gate-kit", s.gates);
                    break;
                }
                case "shed":
                case "garage":
                {
                    bool garage = s.type == "garage";
                    float per = 2f * (W + L), roof = RoofAreaSqFt(L, W, s.pitch);
                    float slope = (W / 2f + 1f) * Mathf.Sqrt(1f + (s.pitch / 12f) * (s.pitch / 12f));
                    if (garage)
                    {
                        float area = W * L;
                        Need(t, SiteGround.GravelId, area * 4f / 12f);                       // cu ft of base
                        Need(t, "2x6x16", Mathf.CeilToInt(per / 16f) + 1);                   // forms
                        Need(t, FormSystem.StakeId, Mathf.CeilToInt(per / 3f) + 4);
                        Need(t, FormSystem.FormOilId, per * 0.5f);
                        Need(t, FormSystem.PolyId, area * 1.1f);
                        Need(t, "rebar-4", Mathf.CeilToInt(((W / 1.5f + 1f) * L + (L / 1.5f + 1f) * W + 2f * per) / 20f));
                        Need(t, RebarSystem.ChairId, area / 4f);
                        Need(t, "tiewire", 2);
                        Need(t, PourSystem.AnchorId, Mathf.CeilToInt(per / 4f) + 4);
                        Need(t, "sill-gasket", per);
                        Need(t, "pt2x4x8", Mathf.CeilToInt(per / 8f));                       // treated sill plates
                        Need(t, "garage-16x7", 1);
                        Need(t, "garage-opener", 1);
                        Need(t, "2x12x16", 2);                                               // door header
                    }
                    else
                    {
                        Need(t, "pt2x6x16", Mathf.CeilToInt(per / 16f));                     // sills
                        Need(t, "2x8x12", Mathf.CeilToInt(L * 12f / 16f) + 1 + 2 * Mathf.CeilToInt(L / 12f)); // joists + rims
                        Need(t, "subfloor34", Mathf.CeilToInt(W * L / 32f));
                        Need(t, "adhesive", Mathf.CeilToInt(W * L / 50f));
                    }
                    Need(t, "2x4x925", Mathf.CeilToInt(per * 12f / 16f) + 16);              // studs + corners/openings
                    Need(t, "2x4x12", Mathf.CeilToInt(3f * per / 12f));                      // bottom + double top plates
                    Need(t, "osb716", Mathf.CeilToInt(per * 8f / 32f));
                    Need(t, "door-ext-steel36", 1);
                    Need(t, "truss-fink", Mathf.Max(1, Mathf.RoundToInt(L / 2f) - 1));
                    Need(t, "truss-gable", 2);
                    Need(t, "hurricane-tie", 2 * (Mathf.RoundToInt(L / 2f) + 1));
                    Need(t, "2x6x16", Mathf.CeilToInt(2f * (L + 2f) / 16f));                // sub-fascia
                    Need(t, "1x6x16", Mathf.CeilToInt((2f * (L + 2f) + 4f * slope) / 16f)); // fascia
                    Need(t, "osb716roof", Mathf.CeilToInt(roof / 32f * 1.1f));
                    Need(t, "h-clip", roof / 8f);
                    RoofingTakeoff(t, L, roof, slope);
                    Need(t, "nails16d", 1000);
                    Need(t, "nails8d", 1000);
                    break;
                }
                case "driveway":
                case "patio":
                {
                    float area = W * L, per = 2f * (W + L);
                    Need(t, SiteGround.GravelId, area * 4f / 12f);                           // cu ft of 4" base
                    Need(t, "2x4x16", Mathf.CeilToInt(per / 16f) + 1);                       // 4" slab: 2x4 forms
                    Need(t, FormSystem.StakeId, Mathf.CeilToInt(per / 3f) + 4);
                    Need(t, FormSystem.FormOilId, per * 0.3f);
                    float barFt = (W / 1.5f + 1f) * L + (L / 1.5f + 1f) * W;
                    if (s.type == "driveway") Need(t, "rebar-4", Mathf.CeilToInt(barFt / 20f));
                    else Need(t, "rebar-3", Mathf.CeilToInt(barFt / 10f));
                    Need(t, RebarSystem.ChairId, area / 4f);
                    Need(t, "tiewire", 1);
                    break;
                }
                case "reroof":
                {
                    float roof = RoofAreaSqFt(L, W, s.pitch);
                    float slope = (W / 2f + 1f) * Mathf.Sqrt(1f + (s.pitch / 12f) * (s.pitch / 12f));
                    RoofingTakeoff(t, L, roof, slope);
                    break;
                }
            }
            return t;
        }

        static void RoofingTakeoff(Dictionary<string, int> t, float lengthFt, float roofSqFt, float slopeFt)
        {
            float eaves = 2f * (lengthFt + 2f), rakes = 4f * slopeFt;
            Need(t, "drip-edge", Mathf.CeilToInt((eaves + rakes) / 9.8f));
            Need(t, "ice-water", Mathf.CeilToInt(eaves * 3f / 198f));
            Need(t, "underlayment", Mathf.CeilToInt(roofSqFt / 1000f * 1.1f));
            Need(t, "cap-staples", 2000);
            Need(t, "starter-strip", Mathf.CeilToInt(eaves / 105f));
            Need(t, "shingles-arch", Mathf.CeilToInt(roofSqFt / 33.3f * 1.1f));
            Need(t, "ridge-vent", Mathf.CeilToInt((lengthFt - 1f) / 4f));
            Need(t, "ridge-cap", Mathf.CeilToInt(lengthFt / 33f));
            Need(t, "roof-nails", 7200);
        }

        /// <summary>Cost of the catalog takeoff plus the ready-mix the job needs.</summary>
        public static int TakeoffCost(Job j)
        {
            int cost = CatalogCost(Takeoff(j));
            var s = SpecOf(j);
            if (s != null) cost += Mathf.RoundToInt(ReadyMixYards(j) * ReadyMixPricePerYard(s.psi));
            return cost;
        }

        public static int CatalogCost(Dictionary<string, int> takeoff)
        {
            int cost = 0;
            foreach (var kv in takeoff) { var d = Catalog.Get(kv.Key); if (d != null) cost += d.Price * kv.Value; }
            return cost;
        }

        // ------------------------------------------------------------------ verification

        static T WithLot<T>(Rect lot, Func<T> f)
        {
            var prev = Site.Lot;
            Site.Lot = lot;
            try { return f(); }
            finally { Site.Lot = prev; }
        }

        /// <summary>Loose lumber/sheets left on the lot, from the inspection framework's own "site.loose-members" rule.</summary>
        static int LooseMembers(Rect lot)
        {
            var prevFilter = Inspections.RuleFilter;
            Inspections.RuleFilter = r => r.Id == "site.loose-members";
            try { return WithLot<int>(lot, () => Inspections.Evaluate(InspectionPhase.Framing).Issues.Count); }
            catch (Exception e) { Debug.LogWarning("[ContractorJobs] loose-member check failed: " + e.Message); return 0; }
            finally { Inspections.RuleFilter = prevFilter; }
        }

        /// <summary>Checklist line. A passing item is remembered in the spec; if the object it came from is gone
        /// (<paramref name="present"/> false: e.g. a roof or slab that did not survive a reload) the earlier sign-off stands.</summary>
        static void Item(JobCheck r, ContractorJobSpec s, string key, bool present, bool ok, string text)
        {
            if (ok)
            {
                if (!s.latched.Contains(key)) { s.latched.Add(key); s.dirty = true; }
                r.Done.Add(text);
            }
            else if (!present && s.latched.Contains(key)) r.Done.Add(text + " (signed off earlier)");
            else r.Missing.Add(text);
        }

        /// <summary>Worst error (feet) matching measured a x b against wanted w x l in either orientation.</summary>
        static float DimError(float a, float b, float w, float l)
        {
            float e1 = Mathf.Max(Mathf.Abs(a - w), Mathf.Abs(b - l));
            float e2 = Mathf.Max(Mathf.Abs(a - l), Mathf.Abs(b - w));
            return Mathf.Min(e1, e2);
        }

        static string Ft(float feet) => Units.Format(feet * Units.Foot);

        /// <summary>Checks the real work on the job's lot. Never changes the world; may record sign-offs in the job payload.</summary>
        public static JobCheck Verify(Job j)
        {
            var r = new JobCheck { Quality = 0f };
            var s = SpecOf(j);
            if (s == null) { r.Missing.Add("Not a contractor job"); return r; }
            float q = 1f;
            try
            {
                var lot = s.Lot;
                switch (s.type)
                {
                    case "deck": VerifyDeck(r, s, lot, ref q); break;
                    case "fence": VerifyFence(r, s, lot, ref q); break;
                    case "shed": VerifyBuilding(r, j, s, lot, false, ref q); break;
                    case "garage": VerifyBuilding(r, j, s, lot, true, ref q); break;
                    case "driveway":
                    case "patio": VerifySlab(r, s, lot, s.widthFt, s.lengthFt, s.type == "driveway" ? "Driveway" : "Patio", true, ref q); break;
                    case "reroof": VerifyReroof(r, j, s, lot, ref q); break;
                    default: r.Missing.Add("Unknown job type " + s.type); break;
                }
                int loose = LooseMembers(lot);
                if (loose > 0)
                {
                    r.Notes.Add($"{loose} loose board(s)/sheet(s) left on the lot - fasten them or haul them off");
                    q -= Mathf.Min(0.2f, loose * 0.03f);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                r.Missing.Add("Could not verify the work (" + e.GetType().Name + ")");
            }
            if (s.dirty) StoreSpec(j, s);
            r.Ok = r.Missing.Count == 0;
            r.Quality = Mathf.Clamp01(q);
            return r;
        }

        /// <summary>Verify; if everything is done, bill the client (Company.Complete pays, with the quality factor).</summary>
        public static bool TryComplete(Job j, out string msg)
        {
            if (j == null || !Company.ActiveJobs.Contains(j)) { msg = "That job is not active."; return false; }
            if (!IsContractorJob(j)) { msg = "Not a contractor job."; return false; }
            if (!JobVehicleLock.Met(j)) { msg = JobVehicleLock.PausedLine(j); return false; }   // paused until the vehicle is back
            var c = Verify(j);
            if (!c.Ok)
            {
                msg = $"The client won't sign off yet - {c.Missing.Count} item(s) open: {c.Missing[0]}";
                return false;
            }
            Company.Complete(j, c.Quality);
            msg = $"{j.Client} signed off on \"{j.Title}\" (workmanship {c.Quality * 100f:0}%).";
            return true;
        }

        // ---- deck (Exterior/Deck.cs)

        static Deck DeckOn(Rect lot)
        {
            Deck best = null;
            foreach (var d in DeckBuilder.All)
            {
                if (d == null || d.Wall == null) continue;
                Vector3 c = d.P(d.U0 + d.Width * 0.5f, d.Depth * 0.5f, 0f);
                if (!lot.Contains(new Vector2(c.x, c.z))) continue;
                if (best == null || d.Stage > best.Stage) best = d;
            }
            return best;
        }

        static void VerifyDeck(JobCheck r, ContractorJobSpec s, Rect lot, ref float q)
        {
            var d = DeckOn(lot);
            bool has = d != null;
            int stage = has ? d.Stage : 0;
            float wFt = has ? d.Width / Units.Foot : 0f, dFt = has ? d.Depth / Units.Foot : 0f;
            float hIn = has ? (d.SurfaceY - ExteriorSite.GradeY) / Units.Inch : 0f;
            bool sizeOk = has && Mathf.Abs(wFt - s.widthFt) <= 0.25f && Mathf.Abs(dFt - s.lengthFt) <= 0.25f;
            bool heightOk = has && Mathf.Abs(hIn - s.heightIn) <= 2f;

            Item(r, s, "deck.size", has, sizeOk, $"Deck laid out {Ft(s.lengthFt)} deep x {Ft(s.widthFt)} wide against the house" + (has && !sizeOk ? $" (it is {Ft(dFt)} x {Ft(wFt)})" : ""));
            Item(r, s, "deck.height", has, heightOk, $"Deck surface {s.heightIn:0}\" above grade" + (has && !heightOk ? $" (it is {hIn:0}\")" : ""));
            Item(r, s, "deck.ledger", has, stage >= 1, "Ledger lagged to the house with Z-flashing");
            Item(r, s, "deck.posts", has, stage >= 3, "Footings below frost and 6x6 posts set");
            Item(r, s, "deck.beam", has, stage >= 4, "Doubled 2x10 beam on the posts");
            Item(r, s, "deck.joists", has, stage >= 6, "2x8 joists in hangers 16\" O.C. and rim joist");
            Item(r, s, "deck.decking", has, stage >= 7, "5/4x6 decking screwed down");
            Item(r, s, "deck.guards", has, has && d.Done, "Guards: posts, rails, cap and balusters");
            if (!has) return;

            // workmanship: the same facts the exterior inspection (ext-deck-ledger / ext-deck-guard) looks at
            if (stage >= 1 && !d.ZFlash) { r.Notes.Add("Ledger not flashed"); q -= 0.15f; }
            else if (stage >= 1 && !d.WrapLapped) { r.Notes.Add("Z-flashing not lapped under the house wrap"); q -= 0.08f; }
            if (stage >= 1 && d.LagsInFraming < d.Lags.Count) { r.Notes.Add($"{d.Lags.Count - d.LagsInFraming} ledger lag(s) miss the framing"); q -= 0.15f; }
            if (stage >= 2 && d.FootingBottomY > ExteriorSite.GradeY - ExteriorSite.FrostDepth + 1e-3f) { r.Notes.Add("Footings above frost depth"); q -= 0.15f; }
            if (d.Done && d.GuardHeight < Units.In(36f) - 1e-4f) { r.Notes.Add("Guard under 36\" high"); q -= 0.15f; }
            if (d.Done && d.BalusterGap >= Units.In(4f)) { r.Notes.Add("Baluster gap of 4\" or more"); q -= 0.15f; }
            float err = Mathf.Max(Mathf.Abs(wFt - s.widthFt), Mathf.Abs(dFt - s.lengthFt));
            q -= Mathf.Clamp01(err / 0.25f) * 0.05f;
        }

        // ---- fence (Landscaping/LandscapeFeature.cs)

        static void VerifyFence(JobCheck r, ContractorJobSpec s, Rect lot, ref float q)
        {
            float doneFt = 0f, openFt = 0f; int gatesDone = 0, gatesOpen = 0, unfinishedRuns = 0;
            foreach (var f in LandscapeFeature.All)
            {
                if (f == null) continue;
                if (f.Kind != LandscapeKind.Fence && f.Kind != LandscapeKind.Gate) continue;
                var c = f.Center;
                if (!lot.Contains(new Vector2(c.x, c.z))) continue;
                if (f.Kind == LandscapeKind.Gate) { if (f.Done) gatesDone++; else gatesOpen++; continue; }
                float ft = f.Length / Units.Foot;
                if (f.Done) doneFt += ft; else { openFt += ft; unfinishedRuns++; }
            }
            bool present = doneFt + openFt > 0.1f;
            bool lengthOk = doneFt >= s.lengthFt * 0.98f;
            Item(r, s, "fence.layout", present, doneFt + openFt >= s.lengthFt * 0.98f,
                $"{s.lengthFt:0} LF of fence staked out on the lot ({doneFt + openFt:0} LF so far)");
            Item(r, s, "fence.built", present, lengthOk,
                $"{s.lengthFt:0} LF of 6' cedar privacy fence finished: posts in concrete, rails, pickets ({doneFt:0} LF done)");
            Item(r, s, "fence.gate", gatesDone + gatesOpen > 0, gatesDone >= s.gates, $"{s.gates} gate(s) hung with hardware");
            if (unfinishedRuns > 0 && lengthOk) r.Missing.Add($"{unfinishedRuns} fence run(s) on the lot left unfinished ({openFt:0} LF)");
            if (gatesOpen > 0 && gatesDone >= s.gates) r.Missing.Add($"{gatesOpen} gate(s) started but not hung");
            if (lengthOk)
            {
                float over = (doneFt - s.lengthFt) / Mathf.Max(1f, s.lengthFt);
                if (over > 0.1f) { r.Notes.Add($"{doneFt - s.lengthFt:0} LF more fence than the contract"); q -= Mathf.Min(0.1f, over * 0.3f); }
            }
        }

        // ---- concrete flatwork (Concrete/ConcreteElement.cs)

        static ConcreteElement SlabOn(Rect lot)
        {
            ConcreteElement best = null; float bestArea = 0f;
            foreach (var e in ConcreteElement.All)
            {
                if (e == null || e.Outline == null || e.Outline.Length < 3) continue;
                Vector2 c = Vector2.zero;
                foreach (var p in e.Outline) c += p;
                c /= e.Outline.Length;
                if (!lot.Contains(c)) continue;
                float a = e.Area();
                if (a > bestArea) { bestArea = a; best = e; }
            }
            return best;
        }

        static void VerifySlab(JobCheck r, ContractorJobSpec s, Rect lot, float wFt, float lFt, string what, bool flatwork, ref float q)
        {
            var e = SlabOn(lot);
            bool has = e != null;
            float a = 0f, b = 0f, area = 0f, err = 0f;
            if (has)
            {
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                foreach (var p in e.Outline) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.y); z1 = Mathf.Max(z1, p.y); }
                a = (x1 - x0) / Units.Foot; b = (z1 - z0) / Units.Foot; area = e.Area();
                err = DimError(a, b, wFt, lFt);
            }
            bool sizeOk = has && err <= 0.5f && area >= wFt * lFt * 0.95f;
            string k = flatwork ? "slab" : "gslab";
            Item(r, s, k + ".poured", has, has, $"{what} slab poured and finished");
            Item(r, s, k + ".size", has, sizeOk, $"{what} is {Ft(wFt)} x {Ft(lFt)}" + (has && !sizeOk ? $" (it is {Ft(a)} x {Ft(b)}, {area:0} sq ft)" : ""));
            Item(r, s, k + ".thick", has, has && e.ThicknessInches >= s.thicknessIn - 0.25f, $"At least {s.thicknessIn:0}\" thick" + (has ? $" (it is {e.ThicknessInches:0.#}\")" : ""));
            Item(r, s, k + ".psi", has, has && e.Psi >= s.psi, $"{s.psi} psi mix" + (has && e.Psi < s.psi ? $" (it is {e.Psi} psi)" : ""));
            if (flatwork && s.broom)
                Item(r, s, k + ".broom", has, has && e.Finish == SurfaceKind.BroomedConcrete, "Broom finish (non-slip)");
            if (flatwork)
                Item(r, s, k + ".stripped", has, has && e.FormsStripped, "Forms stripped and hauled off");
            if (!has) return;

            q -= Mathf.Clamp01(err / 0.5f) * 0.1f;
            foreach (var d in e.Defects) r.Notes.Add(what + " slab: " + d);
            q -= Mathf.Min(0.5f, e.Defects.Count * 0.1f);
            if (!e.Covered && !e.WetCured) { r.Notes.Add(what + " slab was not covered or wet-cured"); q -= 0.05f; }
        }

        // ---- shed / garage (framing + roofing systems)

        static RoofInfo RoofOn(Rect lot)
        {
            foreach (var ri in RoofInfo.All)
            {
                if (ri == null) continue;
                var fp = ri.Footprint;
                Vector3 c = fp.World(fp.Length * 0.5f, 0f, fp.Width * 0.5f);
                if (lot.Contains(new Vector2(c.x, c.z))) return ri;
            }
            return null;
        }

        static void VerifyBuilding(JobCheck r, Job j, ContractorJobSpec s, Rect lot, bool garage, ref float q)
        {
            if (garage) VerifySlab(r, s, lot, s.widthFt, s.lengthFt, "Garage", false, ref q);

            List<Piece> pieces = WithLot<List<Piece>>(lot, () => Site.Pieces());
            int lumber = 0, sheets = 0; bool door = false, entry = false;
            foreach (var p in pieces)
            {
                if (p.State != PieceState.Fastened) continue;
                if (p.Def.Category == PieceCategory.Lumber) lumber++;
                else if (p.Def.Category == PieceCategory.Sheet) sheets++;
                else if (p.Def.Category == PieceCategory.GarageDoor && p.Def.Id.StartsWith("garage-16x7")) door = true;
                else if (p.Def.Category == PieceCategory.Door) entry = true;
            }
            bool any = pieces.Count > 0;
            float per = 2f * (s.widthFt + s.lengthFt);
            int minLumber = Mathf.RoundToInt(per * 0.75f);      // studs 16" O.C. alone
            int minSheets = Mathf.CeilToInt(per / 4f);          // one course of 4' sheets around the walls

            var roof = RoofOn(lot);
            bool hasRoof = roof != null;
            float err = 99f;
            if (hasRoof) err = DimError(roof.Footprint.Length / Units.Foot, roof.Footprint.Width / Units.Foot, s.lengthFt, s.widthFt);
            else
            {
                Bounds bb = default;
                bool got = WithLot<bool>(lot, () => Site.BuildingBounds(out bb));
                if (got)
                {
                    // without a roof the piece bounds are the framing itself; allow for sheathing and a little slop
                    float e = DimError(bb.size.x / Units.Foot, bb.size.z / Units.Foot, s.lengthFt, s.widthFt);
                    if (lumber >= minLumber) err = e;
                }
            }
            bool sizeOk = err <= 0.5f;
            string name = garage ? "Garage" : "Shed";
            Item(r, s, "bld.size", any || hasRoof, sizeOk, $"{name} framed {Ft(s.widthFt)} x {Ft(s.lengthFt)} on the staked footprint" + (err < 50f && !sizeOk ? $" (off by {Ft(err)})" : ""));
            Item(r, s, "bld.walls", any, lumber >= minLumber, $"Walls framed and fastened ({lumber} of at least {minLumber} framing members nailed off)");
            Item(r, s, "bld.sheath", any, sheets >= minSheets, $"Wall sheathing nailed off ({sheets} of at least {minSheets} sheets)");
            Item(r, s, "bld.roofframe", hasRoof, hasRoof && roof.Framed, $"Roof framed ({s.pitch}/12 trusses 24\" O.C., hurricane ties)");
            Item(r, s, "bld.roofdeck", hasRoof, hasRoof && roof.Sheathed, "Roof sheathed");
            Item(r, s, "bld.dryin", hasRoof, hasRoof && roof.DriedIn, "Roof dried in: drip edge, ice & water, underlayment");
            Item(r, s, "bld.shingles", hasRoof, hasRoof && roof.Shingled, "Shingles, ridge vent and ridge cap");
            if (garage) Item(r, s, "bld.door", any, door, "16x7 overhead door installed and fastened");
            else Item(r, s, "bld.entry", any, entry, "36\" entry door installed and fastened");
            if (err < 50f) q -= Mathf.Clamp01(err / 0.5f) * 0.1f;
        }

        // ---- re-roof (roofing system on the customer's existing house)

        static void VerifyReroof(JobCheck r, Job j, ContractorJobSpec s, Rect lot, ref float q)
        {
            var roof = ContractorJobSite.RoofOf(j);
            if (roof == null) roof = RoofOn(lot);
            bool has = roof != null;
            Item(r, s, "roof.deck", has, has && roof.Sheathed, "Old roofing torn off to a sound, clean deck");
            Item(r, s, "roof.dryin", has, has && roof.DriedIn, "Drip edge, ice & water shield and synthetic underlayment");
            Item(r, s, "roof.shingles", has, has && roof.Shingled, $"Architectural shingles, ridge vent and cap ({s.squares:0} squares)");
            if (!has) return;
            float area = 0f;
            foreach (var p in roof.Planes) area += p.Area;
            float sq = area / (Units.Foot * Units.Foot) / 100f;
            if (Mathf.Abs(sq - s.squares) > 1.5f) r.Notes.Add($"Roof measures {sq:0.#} squares (contract says {s.squares:0})");
        }
    }
}
