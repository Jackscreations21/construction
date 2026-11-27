using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Puts the crew assigned to the job the player is at on site during work hours (7:00-15:30, overtime to 17:30), sends
    /// them home after, hands out tasks, and runs the K key "point and assign" menu.
    /// </summary>
    public class CrewManager : MonoBehaviour
    {
        public static CrewManager I;
        /// <summary>Which job is "here" (town feature can supply this). Default: nearest active job within 45 m of the player, else the build site.</summary>
        public static System.Func<string> HereJobResolver;
        public const KeyCode AssignKey = KeyCode.K;

        float _nextCheck;
        bool _menu;
        Vector3 _aimPoint;
        Piece _aimPiece;
        FramingJob _aimJob;
        bool _aimFace, _aimPad, _aimLot;
        Vector3? _trenchStart;
        Vector2 _menuPos;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("CrewSystems");
            DontDestroyOnLoad(go);
            go.AddComponent<CrewManager>();
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public static void DespawnAll()
        {
            foreach (var n in CrewNpc.All.ToArray()) if (n != null) { n.gameObject.SetActive(false); Destroy(n.gameObject); }
        }

        public static CrewNpc NpcFor(Worker w) => CrewNpc.All.Find(n => n != null && n.W == w);

        /// <summary>Where workers arrive and leave: the delivery drop point (by the trucks), else near the player.</summary>
        public static Vector3? ArrivalOverride;

        public static Vector3 ArrivalPoint()
        {
            if (ArrivalOverride.HasValue) return ArrivalOverride.Value;
            if (DeliveryService.I != null) return DeliveryService.I.DropPoint + new Vector3(-2f, 0, -1f);
            if (Game.Player != null) return Game.Player.transform.position + Game.Player.transform.right * 3f;
            return Vector3.zero;
        }

        static string ResolveHere()
        {
            if (HereJobResolver != null) { var r = HereJobResolver(); if (!string.IsNullOrEmpty(r)) return r; }
            if (Game.Player != null)
            {
                Vector3 p = Game.Player.transform.position; Job best = null; float bd = 45f * 45f;
                foreach (var j in Company.ActiveJobs)
                {
                    if (j.WorldPos == Vector3.zero) continue;
                    float d = (j.WorldPos - p).sqrMagnitude;
                    if (d < bd) { bd = d; best = j; }
                }
                if (best != null) return best.Id;
            }
            return Crew.SiteJobId;
        }

        /// <summary>Best open task for a worker on their job: their own trade first, then general labor, then anything.</summary>
        public static CrewTask PickTask(Worker w)
        {
            CrewTask best = null; int bestScore = int.MinValue;
            int order = 0;
            foreach (var t in Crew.Tasks)
            {
                order++;
                if (t.Done || t.JobId != w.AssignedJobId) continue;
                if (t.ClaimedBy != null && t.ClaimedBy != w.Uid && Crew.Find(t.ClaimedBy) != null && NpcFor(Crew.Find(t.ClaimedBy)) != null) continue;
                if (!string.IsNullOrEmpty(t.Blocked) && Time.time < t.RetryAt) continue;
                int score = (t.Trade == w.Trade ? 300 : CrewTrades.Matches(w.Trade, t.Trade) ? 200 : 0) - order;
                if (score > bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        void Update()
        {
            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + 0.5f;
                Crew.HereJobId = ResolveHere();
                SyncNpcs();
                if (Weather.IsRaining && CrewNpc.All.Count > 0) Crew.RainedOnCrew = true;
            }
            HandleKey();
        }

        void SyncNpcs()
        {
            bool career = GameSettings.IsCareer || Crew.IgnoreWorkHours;
            foreach (var n in CrewNpc.All.ToArray())
            {
                if (n == null || n.External || n.State == CrewNpc.Phase.Leaving) continue;
                var w = n.W;
                bool keep = career && w != null && Company.Workers.Contains(w) && w.AssignedJobId == Crew.HereJobId && Crew.Available(w) && Crew.OnTheClock(w);
                if (!keep)
                {
                    if (w == null || !Company.Workers.Contains(w)) Destroy(n.gameObject);
                    else n.SendHome(ArrivalPoint());
                }
            }
            if (!career) return;
            int k = 0;
            foreach (var w in Company.Workers)
            {
                if (w.AssignedJobId != Crew.HereJobId || !Crew.Available(w) || !Crew.OnTheClock(w)) continue;
                var existing = NpcFor(w);
                if (existing != null && existing.State != CrewNpc.Phase.Leaving) continue;
                if (existing != null) Destroy(existing.gameObject);
                Vector3 p = ArrivalPoint() + new Vector3((k++ % 4) * 0.8f, 0, (k / 4) * 0.8f);
                CrewNpc.Spawn(w, p);
            }
        }

        // ------------------------------------------------------------------ K: point at something and assign it

        void HandleKey()
        {
            if (_menu)
            {
                if (GInput.GetKeyDown(KeyCode.Escape) || GInput.GetKeyDown(AssignKey)) CloseMenu();
                return;
            }
            if (Game.UIOpen || !GameSettings.IsCareer || Game.Player == null) return;
            if (!GInput.GetKeyDown(AssignKey)) return;
            if (Company.Workers.Count == 0) { Game.Toast("No crew yet - hire workers in the Crew app on the tablet"); return; }
            OpenMenu();
        }

        void OpenMenu()
        {
            _aimPiece = null; _aimJob = null; _aimFace = _aimPad = _aimLot = false;
            _aimJob = FramingInteraction.I != null ? FramingInteraction.I.AimedJob(12f) : null;
            if (Game.Player.TryAim(out var hit, 12f))
            {
                _aimPoint = hit.point;
                _aimPiece = hit.collider.GetComponentInParent<Piece>();
                _aimFace = _aimPiece != null && (_aimPiece.Def.Category == PieceCategory.Lumber || _aimPiece.Def.Category == PieceCategory.Sheet);
                _aimPad = SiteGround.I != null && SiteGround.InPatch(hit.point) && _aimPiece == null;
                _aimLot = _aimPiece == null && !_aimPad;
            }
            else _aimPoint = Game.Player.transform.position + Game.Player.transform.forward * 3f;
            _menu = true; Game.UIOpen = true;
            _menuPos = new Vector2(Screen.width * 0.5f + 30f, Screen.height * 0.5f - 160f);
        }

        void CloseMenu() { _menu = false; Game.UIOpen = Tablet.I != null && Tablet.I.IsOpen; }

        void Add(CrewTask t, string what)
        {
            Crew.AddTask(t);
            Game.Toast("Crew: " + what);
            CloseMenu();
        }

        GUIStyle _box, _btn;
        void OnGUI()
        {
            if (!_menu) return;
            if (_box == null) { _box = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) }; _btn = new GUIStyle(GUI.skin.button) { fontSize = 14, alignment = TextAnchor.MiddleLeft }; }
            var opts = new List<(string label, System.Action act)>();
            Vector3 here = Game.Player != null ? Game.Player.transform.position : _aimPoint;
            string yawTxt = "";
            if (_aimJob != null && !_aimJob.Done) opts.Add(($"Build this: {_aimJob.Title} ({_aimJob.Next}/{_aimJob.Steps.Count})", () => Add(FramingJobTask.For(_aimJob), _aimJob.Title)));
            foreach (var j in FramingJob.All)
                if (j != null && !j.Done && j != _aimJob && (j.transform.position - _aimPoint).magnitude < 14f && opts.Count < 4)
                { var jj = j; opts.Add(($"Build: {j.Title}", () => Add(FramingJobTask.For(jj), jj.Title))); }
            opts.Add(("Fasten everything I've placed (within 30')", () => Add(new FastenTask { A = _aimPoint, Radius = 30f * Units.Foot }, "fasten loose pieces")));
            if (_aimFace || Drywall.Surfaces.Count > 0)
            {
                opts.Add(("Hang, tape and finish drywall in this room", () => Add(new DrywallTask { A = here }, "drywall this room")));
                opts.Add(("Prime and paint this room (Linen)", () => Add(new PaintTask { A = here, Ref = "Linen" }, "paint this room")));
            }
            if (_aimPad)
            {
                if (_trenchStart == null) opts.Add(("Dig trench: start here (press K again at the end)", () => { _trenchStart = _aimPoint; Game.Toast("Trench start set - aim at the end and press K"); CloseMenu(); }));
                else opts.Add(("Dig trench from the start point to here: 12\" wide, 12\" deep", () => { var a = _trenchStart.Value; _trenchStart = null; Add(new DigTask { A = Flat(a), B = Flat(_aimPoint), Radius = 6f * Units.Inch, Depth = 12f * Units.Inch }, "dig trench"); }));
                opts.Add(("Dig a 24\" x 24\" footing pad here, 12\" deep", () => Add(new DigTask { A = Flat(_aimPoint) - Vector3.right * 0.3f, B = Flat(_aimPoint) + Vector3.right * 0.3f, Radius = 12f * Units.Inch, Depth = 12f * Units.Inch }, "dig footing")));
            }
            if (_aimLot) opts.Add(("Lay sod 10' x 10' here (on raked soil)", () => Add(new SodTask { A = Flat(_aimPoint), B = new Vector3(5f * Units.Foot, 0, 5f * Units.Foot) }, "lay sod")));
            var sel = Game.Placement != null ? Game.Placement.Selected : null;
            if (sel != null) opts.Add(($"Carry and stock 10 x {sel.Name} here", () => Add(new StockTask { A = Flat(_aimPoint), Ref = sel.Id, Count = 10, Yaw = Game.Player != null ? Game.Player.transform.eulerAngles.y : 0f }, "stock " + sel.Name)));

            float h = 46f + opts.Count * 32f;
            var r = new Rect(_menuPos.x, _menuPos.y, 440f, h);
            GUI.Box(r, $"Crew orders ({Crew.JobLabel(Crew.HereJobId)}){yawTxt}", _box);
            for (int i = 0; i < opts.Count; i++)
                if (GUI.Button(new Rect(r.x + 8, r.y + 34 + i * 32, r.width - 16, 28), opts[i].label, _btn)) { opts[i].act(); return; }
        }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);
    }
}
