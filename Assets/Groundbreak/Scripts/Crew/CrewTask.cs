using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>One trip of work for a worker: (fetch a material,) walk to the spot, work for a while, then <see cref="Do"/>
    /// calls the same public API the player's tool would.</summary>
    public class WorkUnit
    {
        public Vector3 Spot;            // where the work is (world)
        public Vector3 StandAt;         // where to stand (ground); defaults to just short of Spot
        public bool HasStand;
        public float Seconds = 2f;      // for a journeyman of the right trade
        public string Anim = "place";   // hammer, drill, saw, level, roll, trowel, sand, shovel, kneel, hose, place
        public string CarryId;          // material carried from a pile first (null = none)
        public Vector3 PickupAt;
        public string Label = "";
        /// <summary>Does the work. Returns null/"" on success or the reason it couldn't be done (blocks the task).</summary>
        public Func<CrewCtx, string> Do;
    }

    public class CrewCtx
    {
        public Worker W;
        public bool Sloppy;             // this unit is done wrong (low skill / wrong trade / low morale)
        public System.Random Rng;
        public Vector3 HandPos;
    }

    /// <summary>
    /// A job for the crew, queued per Company job. Tasks keep almost no state of their own: every <see cref="Next"/> looks at
    /// the real world (loose pieces, framing-job steps, drywall surfaces, ground heights) and hands out the next unit, so the
    /// player and the crew can work the same thing at once and a loaded save just carries on.
    /// </summary>
    public abstract class CrewTask
    {
        [Serializable]
        public class Data
        {
            public string kind, uid, jobId, refId, extra;
            public Vector3 a, b; public float radius, depth, yaw; public int count, moved; public bool done;
        }

        public string Uid, JobId, Ref, Extra;
        public Vector3 A, B;
        public float Radius, Depth, Yaw;
        public int Count, Moved;
        public bool Done;
        public string Blocked;          // why nobody can work it right now (retried every few seconds)
        public float RetryAt;
        public string ClaimedBy;        // Worker.Uid doing it; others on the job help (carry) and speed it up
        public readonly HashSet<string> Helpers = new HashSet<string>();
        public Vector3 LastSpot;
        public int UnitsDone, Mistakes;

        public abstract string Kind { get; }
        public abstract string Trade { get; }
        public abstract string Title { get; }
        public virtual string Status => Done ? "Done" : !string.IsNullOrEmpty(Blocked) ? "Waiting: " + Blocked : ClaimedBy != null ? "In progress" : "Queued";

        public WorkUnit Next(Worker w, Vector3 from)
        {
            if (Done) return null;
            Blocked = null;
            var u = NextUnit(w, from);
            if (u != null) LastSpot = u.Spot;
            return u;
        }

        protected abstract WorkUnit NextUnit(Worker w, Vector3 from);
        protected WorkUnit Block(string why) { Blocked = why; RetryAt = Time.time + 4f; return null; }
        protected WorkUnit Finish() { Done = true; Blocked = null; ClaimedBy = null; Helpers.Clear(); Crew.NotifyChanged(); return null; }
        public void Release(string uid) { if (ClaimedBy == uid) ClaimedBy = null; Helpers.Remove(uid); }
        /// <summary>Go over it again (after an inspection found the crew's mistakes).</summary>
        public virtual void Reopen() { Done = false; Blocked = null; }

        public Data ToData() => new Data { kind = Kind, uid = Uid, jobId = JobId, refId = Ref, extra = Extra, a = A, b = B, radius = Radius, depth = Depth, yaw = Yaw, count = Count, moved = Moved, done = Done };

        public static CrewTask FromData(Data d)
        {
            CrewTask t;
            switch (d.kind)
            {
                case "fasten": t = new FastenTask(); break;
                case "stock": t = new StockTask(); break;
                case "frame": t = new FramingJobTask(); break;
                case "drywall": t = new DrywallTask(); break;
                case "paint": t = new PaintTask(); break;
                case "dig": t = new DigTask(); break;
                case "sod": t = new SodTask(); break;
                default: return null;
            }
            t.Uid = d.uid; t.JobId = d.jobId; t.Ref = d.refId; t.Extra = d.extra; t.A = d.a; t.B = d.b; t.Radius = d.radius; t.Depth = d.depth; t.Yaw = d.yaw;
            t.Count = d.count; t.Moved = d.moved; t.Done = d.done;
            return t;
        }

        protected static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);
    }

    // ====================================================================== fasten everything I've placed

    /// <summary>Nails off every loose piece the player placed (within Radius of A; Radius 0 = everywhere): two 16d nails each,
    /// exactly what the framing hammer does (Piece.Nails, Piece.Fasten). A sloppy hand leaves one nail and moves on.</summary>
    public class FastenTask : CrewTask
    {
        public override string Kind => "fasten";
        public override string Trade => "carpenter";
        public override string Title => Radius > 0f ? $"Fasten loose pieces within {Units.Format(Radius)}" : "Fasten every loose piece on site";
        public const int NailsToFasten = 2;     // same as FramingHammer
        readonly HashSet<Piece> _missed = new HashSet<Piece>();

        public override void Reopen() { base.Reopen(); _missed.Clear(); }

        public static bool Wants(Piece p) =>
            p != null && p.Def != null && p.State == PieceState.Loose && p.gameObject.activeInHierarchy && p.GetComponentInParent<MaterialPile>() == null;

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            Piece best = null; float bd = float.MaxValue;
            foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (!Wants(p) || _missed.Contains(p)) continue;
                if (Radius > 0f && Flat(p.transform.position - A).magnitude > Radius) continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (best == null) return Finish();
            if (FramingJob.RequireStock && Game.Stock.Count("nails16d") < NailsToFasten) return Block("Out of 16d nails");
            var piece = best;
            return new WorkUnit
            {
                Spot = piece.transform.position, Seconds = 2.2f, Anim = "hammer", Label = "Nailing " + piece.Def.Name,
                Do = ctx =>
                {
                    if (!Wants(piece)) return null;     // the player got to it first
                    int need = Mathf.Max(1, NailsToFasten - piece.Nails);
                    if (ctx.Sloppy) need = Mathf.Min(need, 1);
                    if (FramingJob.RequireStock && !Game.Stock.TryTake("nails16d", need)) return "Out of 16d nails";
                    for (int i = 0; i < need; i++) { piece.Nails++; CrewWork.NailMark(piece, ctx.HandPos, i); }
                    if (ctx.Sloppy && piece.Nails < NailsToFasten)
                    {
                        CrewDefect.Mark(piece, "only one nail driven (missed fastener)", ctx.W);
                        _missed.Add(piece);
                        return null;
                    }
                    piece.Fasten();
                    SupportGraph.Dirty = true;
                    return null;
                }
            };
        }
    }

    // ====================================================================== carry and stock materials

    /// <summary>Carries Count units of a material from the delivery pile and stacks them at A (a <see cref="StagedPile"/>:
    /// still site stock, just where the work is, so later trips for that material are short).</summary>
    public class StockTask : CrewTask
    {
        public override string Kind => "stock";
        public override string Trade => "laborer";
        public override string Title { get { var d = Catalog.Get(Ref); return $"Stock {Count} x {(d != null ? d.Name : Ref)} ({Moved}/{Count})"; } }

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            if (Moved >= Count) return Finish();
            var def = Catalog.Get(Ref);
            if (def == null) return Finish();
            if (Game.Stock.Count(Ref) - StagedPile.Total(Ref) < 1) return Block($"No more {def.Name} at the delivery pile");
            return new WorkUnit
            {
                Spot = A, Seconds = 1.2f, Anim = "place", CarryId = Ref, PickupAt = CrewWork.DeliveryPile(Ref, A), Label = "Carrying " + def.Name,
                Do = ctx =>
                {
                    if (Game.Stock.Count(Ref) - StagedPile.Total(Ref) < 1) return $"No more {def.Name} at the delivery pile";
                    StagedPile.At(A, Yaw, Ref).Add(1);
                    Moved++;
                    return null;
                }
            };
        }
    }

    // ====================================================================== frame / sheathe from the layout

    /// <summary>Works a <see cref="FramingJob"/> (floor deck, a wall from its layout, cap plates, wall or roof sheathing, roofing...)
    /// step by step through <see cref="FramingJob.TryAdvance"/> - the call the player's click makes - so stock is consumed and
    /// the support graph, inspections and saves see ordinary pieces. A sloppy step leaves one of its pieces under-nailed.</summary>
    public class FramingJobTask : CrewTask
    {
        public override string Kind => "frame";
        public override string Trade => IsRoofing(Ref) ? "roofer" : "carpenter";
        public override string Title => "Build: " + Ref + (Job != null && !Job.Done ? $" ({Job.Next}/{Job.Steps.Count})" : "");
        FramingJob _job;

        static bool IsRoofing(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            string t = title.ToLowerInvariant();
            return t.Contains("shingle") || t.Contains("underlayment") || t.Contains("dry-in") || t.Contains("dry in") || t.Contains("felt") || t.Contains("roofing");
        }

        public static FramingJobTask For(FramingJob job) => new FramingJobTask { Ref = job.Title, A = job.transform.position, _job = job };

        public FramingJob Job
        {
            get
            {
                if (_job != null) return _job;
                float bd = float.MaxValue;
                foreach (var j in FramingJob.All)
                {
                    if (j == null || j.Title != Ref) continue;
                    float d = (j.transform.position - A).sqrMagnitude;
                    if (d < bd) { bd = d; _job = j; }
                }
                return _job;
            }
        }

        static string ToolFor(BuildStep s)
        {
            if (s.Tools == null || s.Tools.Length == 0) return null;
            if (Array.IndexOf(s.Tools, "nailer") >= 0) return "nailer";
            if (Array.IndexOf(s.Tools, "roofnailer") >= 0) return "roofnailer";
            return s.Tools[0];
        }

        static string AnimFor(string tool)
        {
            switch (tool)
            {
                case "nailer": case "hammer": case "roofnailer": case "hatchet": case "capstapler": return "hammer";
                case "saw": return "saw";
                case "level": return "level";
                case "chalkline": case "speedsquare": case "framingsquare": case "tape": return "kneel";
                default: return "place";
            }
        }

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            var job = Job;
            if (job == null) return Block("Layout \"" + Ref + "\" isn't on this site");
            if (job.Done) return Finish();
            var s = job.Current;
            string miss = job.MissingStock(s);
            if (miss != null) return Block(miss);
            Vector3 spot = s.Ghost != null ? s.Ghost().center : job.transform.position;
            string tool = ToolFor(s);
            string carry = null;
            foreach (var kv in s.Uses) { var d = Catalog.Get(kv.Key); if (d != null && d.IsPlaceable) { carry = kv.Key; break; } }
            var step = s;
            return new WorkUnit
            {
                Spot = spot, Seconds = 1.4f + 0.11f * s.Nails + (carry != null ? 0.8f : 0f), Anim = AnimFor(tool), Label = s.Label,
                CarryId = carry, PickupAt = carry != null ? CrewWork.NearestPile(carry, spot) : default,
                Do = ctx =>
                {
                    if (job == null || job.Done || job.Current != step) return null;    // the player did this step meanwhile
                    HashSet<int> before = null;
                    if (ctx.Sloppy && step.Nails > 0)
                    {
                        before = new HashSet<int>();
                        foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) before.Add(p.GetInstanceID());
                    }
                    int at = job.Next; string msg = "";
                    for (int i = 0; i < 60 && job.Next == at; i++)      // a hammer takes several swings per step
                        if (!job.TryAdvance(tool, out msg)) return msg;
                    if (job.Next == at) return msg;
                    if (before != null)
                    {
                        var fresh = new List<Piece>();
                        foreach (var p in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                            if (!before.Contains(p.GetInstanceID()) && p.State == PieceState.Fastened && p.Def != null && !p.Def.IsProduct) fresh.Add(p);
                        if (fresh.Count > 0)
                        {
                            var bad = fresh[ctx.Rng.Next(fresh.Count)];
                            bool sheet = bad.Def.Category == PieceCategory.Sheet;
                            bad.State = PieceState.Loose;
                            bad.Nails = sheet ? Mathf.Max(1, bad.Nails / 2) : 1;
                            CrewDefect.Mark(bad, sheet ? "nailed about 12\" O.C. at the panel edges (6\" required)" : "missed nails: only one nail holding it", ctx.W);
                            SupportGraph.Dirty = true;
                        }
                    }
                    return null;
                }
            };
        }
    }

    // ====================================================================== drywall: hang, tape, finish

    /// <summary>Hangs, screws, tapes, coats (3) and sands the drywall of the room around A, ceiling first, through the
    /// <see cref="Drywall"/> API. Mud dries in real game time, so the task waits between coats (the crew does other work).
    /// A sloppy hand skips the screws along one stud - which the drywall screw inspection finds.</summary>
    public class DrywallTask : CrewTask
    {
        public override string Kind => "drywall";
        public override string Trade => "drywall";
        public override string Title => "Hang, tape and finish drywall in this room";
        List<FramePlane> _faces;
        readonly HashSet<DrywallSheet> _screwed = new HashSet<DrywallSheet>();
        string Product => string.IsNullOrEmpty(Ref) ? "dw-12-4x8" : Ref;

        public override void Reopen() { base.Reopen(); _screwed.Clear(); _faces = null; }

        /// <summary>Framed faces (ceiling first, then walls) seen from a point inside the room.</summary>
        public static List<FramePlane> RoomFaces(Vector3 inside, float range = 9f)
        {
            var list = new List<FramePlane>();
            Vector3 eye = inside + Vector3.up * 1.2f;
            void Try(Vector3 dir, float r)
            {
                FramePlane fp = null;
                try { fp = Insulation.FaceFrom(eye, dir, r); } catch (Exception e) { Debug.LogException(e); }
                if (fp == null || !(fp.IsWall || fp.IsCeiling)) return;
                foreach (var o in list) if (o.SameFace(fp)) return;
                if (fp.IsCeiling) list.Insert(0, fp); else list.Add(fp);
            }
            Try(Vector3.up, 5f);
            for (int i = 0; i < 16; i++) { float a = i * Mathf.PI / 8f; Try(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), range); }
            return list;
        }

        static Vector3 FaceSpot(FramePlane fp, Vector2 uv) => fp.World(uv, 0.05f);
        static WorkUnit Stand(WorkUnit u, FramePlane fp, Vector2 uv) { u.StandAt = fp.IsCeiling ? fp.World(uv) : fp.World(uv, 0.75f); u.HasStand = true; return u; }

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            if (_faces == null) _faces = RoomFaces(A);
            if (_faces.Count == 0) return Block("No framed walls or ceiling around here");
            var prod = Drywall.Product(Product);
            if (prod == null) return Block("Unknown drywall " + Product);

            // 1. hang (ceiling first: NextPlan refuses walls until the lid is on)
            string wait = null;
            foreach (var fp in _faces)
            {
                if (!Drywall.NextPlan(fp, prod, out var plan, out string why)) { if (why != "This face is fully hung") wait = wait ?? why; continue; }
                if (!FinishStock.Unlimited && Game.Stock.Count(prod.Id) < 1) return Block("Need " + prod.Name + " on site");
                var f = fp; var pl = plan;
                return Stand(new WorkUnit
                {
                    Spot = FaceSpot(f, pl.R.center), Seconds = 5f, Anim = "place", CarryId = prod.Id, PickupAt = CrewWork.NearestPile(prod.Id, A), Label = "Hanging drywall",
                    Do = ctx => Drywall.NextPlan(f, prod, out var p2, out _) ? (Drywall.HangSheet(f, prod, p2) != null ? null : "Couldn't hang the sheet (stock?)") : null
                }, f, pl.R.center);
            }

            var surfaces = new List<DrywallSurface>();
            foreach (var fp in _faces) { var s = Drywall.SurfaceFor(fp); if (s != null && s.Sheets.Count > 0) surfaces.Add(s); }
            if (surfaces.Count == 0) return Block(wait ?? "Nothing to hang here");

            // 2. cut out boxes and screw off each sheet
            foreach (var s in surfaces)
                foreach (var sh in s.Sheets)
                {
                    if (_screwed.Contains(sh) || s.Screws.Exists(x => x.Sheet == sh)) continue;
                    if (!FinishStock.Unlimited && Game.Stock.Count(Drywall.ScrewsId) < 30) return Block("Need drywall screws on site");
                    var sheet = sh; var surf = s;
                    return Stand(new WorkUnit
                    {
                        Spot = FaceSpot(s.Fp, sh.R.center), Seconds = 5f, Anim = "drill", Label = "Screwing off drywall",
                        Do = ctx =>
                        {
                            _screwed.Add(sheet);
                            Drywall.CutOutBoxes(surf);
                            var lines = Drywall.ScrewLines(sheet);
                            int skip = ctx.Sloppy && lines.Count > 1 ? ctx.Rng.Next(lines.Count) : -1;   // forgets a whole stud
                            for (int i = 0; i < lines.Count; i++) if (i != skip) Drywall.ScrewLine(sheet, lines[i]);
                            surf.Refresh();
                            return null;
                        }
                    }, s.Fp, sh.R.center);
                }

            // 3. tape + three coats, each after the last one dried; then sand
            string compound = Game.Stock.Count("dw-mud-set45") > 0 ? "dw-mud-set45" : null;
            bool waiting = false;
            foreach (var s in surfaces)
            {
                for (int coat = 1; coat <= 3; coat++)
                {
                    if (Drywall.AllCoated(s, coat, false)) continue;
                    if (coat > 1 && !Drywall.AllCoated(s, coat - 1, true)) { waiting = true; break; }
                    var surf = s; int c = coat;
                    return Stand(new WorkUnit
                    {
                        Spot = FaceSpot(s.Fp, s.Fp.Bounds.center), Seconds = 7f, Anim = "trowel", Label = coat == 1 ? "Taping joints" : "Mud coat " + coat,
                        Do = ctx => Drywall.CoatAll(surf, c, false, compound) > 0 ? null : "Mud: still wet or out of compound/tape"
                    }, s.Fp, s.Fp.Bounds.center);
                }
            }
            foreach (var s in surfaces)
            {
                if (!Drywall.AllCoated(s, 3, false) || Drywall.AllSanded(s)) continue;
                if (!Drywall.AllCoated(s, 3, true)) { waiting = true; continue; }
                var surf = s;
                return Stand(new WorkUnit
                {
                    Spot = FaceSpot(s.Fp, s.Fp.Bounds.center), Seconds = 6f, Anim = "sand", Label = "Sanding",
                    Do = ctx => { Drywall.SandAll(surf); return Drywall.AllSanded(surf) ? null : "Sanding: joints not dry or out of sandpaper"; }
                }, s.Fp, s.Fp.Bounds.center);
            }
            if (waiting) return Block("Mud is drying");
            foreach (var s in surfaces) if (!Drywall.AllSanded(s)) return Block("Mud is drying");
            return Finish();
        }
    }

    // ====================================================================== paint

    /// <summary>Textures, primes and paints (2 coats) every finished drywall surface facing A within Radius.</summary>
    public class PaintTask : CrewTask
    {
        public override string Kind => "paint";
        public override string Trade => "painter";
        public override string Title => "Prime and paint this room (" + Color + ")";
        string Color => string.IsNullOrEmpty(Ref) ? "Linen" : Ref;

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            float rad = Radius > 0f ? Radius : 7f;
            bool any = false, waiting = false; string why = null;
            foreach (var s in Drywall.Surfaces)
            {
                if (s == null || s.Fp == null) continue;
                Vector3 c = s.Fp.World(s.Fp.Bounds.center);
                if ((c - (A + Vector3.up * 1.2f)).magnitude > rad || Vector3.Dot(s.Fp.N, A + Vector3.up * 1.2f - c) <= 0f) continue;
                any = true;
                if (!Drywall.AllSanded(s)) { why = "Drywall isn't taped, finished and sanded yet"; continue; }
                var surf = s;
                WorkUnit U(string label, float sec, string anim, Func<CrewCtx, string> act)
                {
                    var u = new WorkUnit { Spot = surf.Fp.World(surf.Fp.Bounds.center, 0.05f), Seconds = sec, Anim = anim, Label = label, Do = act, HasStand = true };
                    u.StandAt = surf.Fp.IsCeiling ? surf.Fp.World(surf.Fp.Bounds.center) : surf.Fp.World(surf.Fp.Bounds.center, 0.9f);
                    return u;
                }
                if (s.TextureKind == null)
                    return U("Spraying texture", 5f, "roll", ctx => Drywall.ApplyTexture(surf, "orange-peel") ? null : "Need texture mud on site");
                if (!Drywall.TextureDry(s)) { waiting = true; continue; }
                if (s.Coverage(true, 1) < 0.98f)
                    return U("Priming", 7f, "roll", ctx => PaintJob.Prime(surf) > 0 ? null : "Need PVA primer on site");
                if (s.Coverage(false, 2) < 0.98f)
                {
                    var color = PaintColors.Get(s.Fp.IsCeiling ? "Ceiling White" : Color);
                    var sheen = s.Fp.IsCeiling ? Sheen.Flat : Sheen.Eggshell;
                    if (surf.Coverage(false, 1) >= 0.98f && _lastCoat.TryGetValue(surf, out float t) && FinishTime.Now - t < PaintRules.DryHours) { waiting = true; continue; }
                    return U("Painting", 8f, "roll", ctx =>
                    {
                        int n = PaintJob.Paint(surf, color, sheen);
                        if (n > 0) _lastCoat[surf] = FinishTime.Now;
                        return n > 0 ? null : "Paint: still wet or out of paint";
                    });
                }
            }
            if (!any) return Block("No drywall to paint around here");
            if (waiting) return Block("Waiting for texture / paint to dry");
            if (why != null) return Block(why);
            return Finish();
        }
        readonly Dictionary<DrywallSurface, float> _lastCoat = new Dictionary<DrywallSurface, float>();
    }

    // ====================================================================== dig a trench / footing

    /// <summary>Digs a trench from A to B on the building pad with the shovel API (<see cref="SiteGround.DigBy"/>, 2" a bite):
    /// Radius = half width, Depth below the benchmark. A sloppy hand stops a few inches short at a station, which the crew's
    /// trench-depth rule (Footing inspection) measures off the real ground.</summary>
    public class DigTask : CrewTask
    {
        public override string Kind => "dig";
        public override string Trade => "laborer";
        public override string Title => $"Dig trench {Units.Format((B - A).magnitude)} long, {Units.Format(Radius * 2f)} wide, {Units.Format(Depth)} deep";
        public const float Station = 0.22f, Bite = 2f * Units.Inch, ShovelRadius = 0.13f;
        readonly HashSet<int> _short = new HashSet<int>();

        public override void Reopen() { base.Reopen(); _short.Clear(); }
        public float TargetY => SiteLayout.BenchmarkY - Depth;
        public int Stations => Mathf.Max(1, Mathf.CeilToInt(Flat(B - A).magnitude / Station)) + 1;
        public Vector3 StationPos(int i) => Vector3.Lerp(A, B, Stations <= 1 ? 0f : i / (float)(Stations - 1));

        IEnumerable<Vector3> Across(Vector3 p)
        {
            Vector3 dir = Flat(B - A); dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            Vector3 side = new Vector3(-dir.z, 0, dir.x);
            float reach = Radius - ShovelRadius;
            if (reach <= 0.02f) { yield return p; yield break; }
            int n = Mathf.Max(1, Mathf.CeilToInt(reach / (ShovelRadius * 1.2f)));
            for (int k = -n; k <= n; k++) yield return p + side * (reach * k / n);
        }

        /// <summary>Highest ground across the trench at a station (world Y).</summary>
        public float GroundAt(int i)
        {
            float y = float.MinValue;
            foreach (var q in Across(StationPos(i))) y = Mathf.Max(y, SiteGround.I.HeightAt(q));
            return y;
        }

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            var g = SiteGround.I;
            if (g == null) return Block("No building pad here");
            if (!SiteGround.InPatch(A) || !SiteGround.InPatch(B)) return Block("Trench runs outside the building pad");
            for (int i = 0; i < Stations; i++)
            {
                if (_short.Contains(i) || GroundAt(i) <= TargetY + 0.012f) continue;
                int idx = i; Vector3 p = StationPos(i);
                float deep = GroundAt(i) - TargetY;
                Vector3 dir = Flat(B - A).normalized;
                return new WorkUnit
                {
                    Spot = p, HasStand = true, StandAt = p + new Vector3(-dir.z, 0, dir.x) * (Radius + 0.45f),
                    Seconds = 1.2f + 6f * deep / 0.3f * Mathf.Max(1f, Radius / 0.15f), Anim = "shovel", Label = "Digging",
                    Do = ctx =>
                    {
                        float target = TargetY;
                        if (ctx.Sloppy) { target += Units.In(2f + ctx.Rng.Next(3)); _short.Add(idx); }   // calls it good 2-4" shallow
                        foreach (var q in Across(p))
                            for (int k = 0; k < 40 && g.HeightAt(q) > target + 0.004f; k++) g.DigBy(q, ShovelRadius, Mathf.Min(Bite, g.HeightAt(q) - target), target);
                        return null;
                    }
                };
            }
            return Finish();
        }
    }

    // ====================================================================== lay sod

    /// <summary>Lays sod rolls over the rectangle centered at A (half extents B.x, B.z) with <see cref="Landscape.LaySod"/>
    /// (takes "sod-roll" stock, needs raked soil), then waters it in. A sloppy hand leaves a gap.</summary>
    public class SodTask : CrewTask
    {
        public override string Kind => "sod";
        public override string Trade => "landscaper";
        public override string Title => $"Lay sod {Units.Format(B.x * 2f)} x {Units.Format(B.z * 2f)} ({Laid} rolls)";
        public int Laid => Count;
        bool _watered;

        int Cols => Mathf.Max(1, Mathf.FloorToInt(B.x * 2f / Landscape.SodL));
        int Rows => Mathf.Max(1, Mathf.FloorToInt(B.z * 2f / Landscape.SodW));
        Vector3 Cell(int i)
        {
            int r = i / Cols, c = i % Cols;
            return A + new Vector3(-B.x + (c + 0.5f) * Landscape.SodL, 0, -B.z + (r + 0.5f) * Landscape.SodW);
        }

        protected override WorkUnit NextUnit(Worker w, Vector3 from)
        {
            int total = Rows * Cols;
            if (Moved < total)
            {
                if (Game.Stock.Count("sod-roll") < 1) return Block("No sod rolls on site (Garden Center: Lawn)");
                int idx = Moved; Vector3 p = Cell(idx);
                return new WorkUnit
                {
                    Spot = p, Seconds = 2.2f, Anim = "kneel", CarryId = "sod-roll", PickupAt = CrewWork.NearestPile("sod-roll", A), Label = "Laying sod",
                    Do = ctx =>
                    {
                        if (Moved != idx) return null;
                        if (ctx.Sloppy) { Moved++; return null; }      // skipped a roll: a bare strip in the lawn
                        var f = Landscape.LaySod(p, Yaw, out string msg);
                        if (f == null && msg != null && !msg.StartsWith("Already")) return msg;
                        if (f != null) Count++;
                        Moved++;
                        return null;
                    }
                };
            }
            if (!_watered)
                return new WorkUnit
                {
                    Spot = A, Seconds = 5f, Anim = "hose", Label = "Watering sod",
                    Do = ctx => { LandscapeSystem.WaterAround(A, Mathf.Max(B.x, B.z) + 1.5f); _watered = true; return null; }
                };
            return Finish();
        }
    }
}
