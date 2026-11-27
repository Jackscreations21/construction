using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Per trade: how much of that trade's work is left on a site (measured from the real world state the trade systems keep:
    /// framing layouts and their steps, framed faces without drywall, open bays, unmade joints, boxes not made up, staked
    /// landscape jobs, an unpoured form...) and the <see cref="CrewTask"/>s a sub crew works to do it.
    /// </summary>
    public static class SubScopes
    {
        const float SqFt = Units.Foot * Units.Foot;
        static bool Near(Vector3 p, Vector3 c, float r) { p.y = 0f; c.y = 0f; return (p - c).magnitude <= r; }

        public static float Measure(string trade, Vector3 c, float r, out string detail)
        {
            detail = "nothing to do";
            try
            {
                switch (trade)
                {
                    case "framing": return Framing(c, r, false, out detail);
                    case "roofing": return Framing(c, r, true, out detail);
                    case "drywall": return DrywallQty(c, r, out detail);
                    case "painting": return PaintQty(c, r, out detail);
                    case "insulation": return InsulationQty(c, r, out detail);
                    case "electrical": return ElectricalQty(c, r, out detail);
                    case "plumbing": return JointQty(Trade.Plumbing, c, r, out detail);
                    case "hvac": return JointQty(Trade.Hvac, c, r, out detail);
                    case "landscaping": return LandscapeQty(c, r, out detail);
                    case "flooring": return FloorQty(c, r, out detail);
                    case "concrete": return ConcreteQty(c, r, out detail);
                    case "excavation": return ExcavationQty(c, r, out detail);
                }
            }
            catch (Exception e) { Debug.LogException(e); detail = "couldn't measure (" + e.GetType().Name + ")"; }
            return 0f;
        }

        public static List<CrewTask> Tasks(string trade, Vector3 c, float r)
        {
            var l = new List<CrewTask>();
            try
            {
                switch (trade)
                {
                    case "framing": case "roofing":
                        foreach (var j in FramingJobs(c, r, trade == "roofing")) l.Add(FramingJobTask.For(j));
                        break;
                    case "drywall": foreach (var p in RoomPoints(c, r)) l.Add(new DrywallTask { A = p }); break;
                    case "painting": foreach (var p in PaintPoints(c, r)) l.Add(new PaintTask { A = p, Radius = 6f }); break;
                    case "insulation": foreach (var fp in InsulationFaces(c, r)) l.Add(new SubInsulateTask { Face = fp, A = fp.World(fp.Bounds.center) }); break;
                    case "electrical": l.Add(new SubElectricalTask { A = c, Radius = r }); break;
                    case "plumbing": l.Add(new SubJointTask { A = c, Radius = r, Ref = "plumbing" }); break;
                    case "hvac": l.Add(new SubJointTask { A = c, Radius = r, Ref = "hvac" }); break;
                    case "landscaping":
                        foreach (var f in LandscapeFeature.All) if (f != null && !f.Done && Near(f.A, c, r)) l.Add(new SubLandscapeTask { Feature = f, A = f.A });
                        break;
                    case "flooring": foreach (var fp in OpenFloors(c, r)) l.Add(new SubFloorTask { Face = fp, A = c, Radius = r }); break;
                    case "concrete": if (ActiveForm(c, r) != null) l.Add(new SubConcreteTask { A = c, Radius = r }); break;
                    case "excavation": l.AddRange(Trenches(c, r)); break;
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            return l;
        }

        /// <summary>Warranty callback work: fix the trade's real defects.</summary>
        public static List<CrewTask> FixTasks(string trade, Vector3 c, float r)
        {
            var l = new List<CrewTask>();
            switch (trade)
            {
                case "framing": case "roofing": l.Add(new FastenTask { A = c, Radius = r }); break;
                case "drywall": l.Add(new SubDrywallFixTask { A = c, Radius = r }); break;
                case "insulation": l.Add(new SubStapleFixTask { A = c, Radius = r }); break;
                case "excavation": l.AddRange(Trenches(c, r)); break;
                default: l.AddRange(Tasks(trade, c, r)); break;
            }
            if (l.Count == 0) l.Add(new FastenTask { A = c, Radius = r });
            return l;
        }

        // ------------------------------------------------------------------ framing / roofing (FramingJob layouts)

        public static bool IsRoofing(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            string t = title.ToLowerInvariant();
            return t.Contains("shingle") || t.Contains("underlayment") || t.Contains("dry-in") || t.Contains("dry in") || t.Contains("felt") || t.Contains("roofing");
        }

        public static List<FramingJob> FramingJobs(Vector3 c, float r, bool roofing)
        {
            var l = new List<FramingJob>();
            foreach (var j in FramingJob.All)
                if (j != null && !j.Done && j.Steps.Count > 0 && Near(j.transform.position, c, r + 6f) && IsRoofing(j.Title) == roofing) l.Add(j);
            return l;
        }

        /// <summary>Plan area of a layout (the two biggest sides of its ghost boxes' bounds).</summary>
        static float JobArea(FramingJob j)
        {
            bool any = false; Bounds b = default;
            foreach (var s in j.Steps)
            {
                if (s.Ghost == null) continue;
                Bounds g;
                try
                {
                    var gh = s.Ghost();
                    Vector3 ex = gh.rot * gh.size;
                    g = new Bounds(gh.center, new Vector3(Mathf.Abs(ex.x), Mathf.Abs(ex.y), Mathf.Abs(ex.z)));
                }
                catch { continue; }
                if (!any) { b = g; any = true; } else b.Encapsulate(g);
            }
            if (!any) return 0f;
            var e = new List<float> { b.size.x, b.size.y, b.size.z }; e.Sort();
            return e[1] * e[2];
        }

        static float Framing(Vector3 c, float r, bool roofing, out string detail)
        {
            float area = 0f; int steps = 0;
            var jobs = FramingJobs(c, r, roofing);
            foreach (var j in jobs)
            {
                int left = j.Steps.Count - j.Next;
                steps += left;
                area += JobArea(j) * left / (float)j.Steps.Count;
            }
            float sqft = area / SqFt;
            detail = $"{jobs.Count} layout(s), {steps} step(s) left";
            if (jobs.Count > 0 && sqft < 1f) sqft = steps * 4f;
            return roofing ? sqft / 100f : sqft;
        }

        // ------------------------------------------------------------------ drywall / paint

        /// <summary>Points inside rooms (a ceiling overhead), one per ceiling, on a 2.5 m grid over the site.</summary>
        public static List<Vector3> RoomPoints(Vector3 c, float r)
        {
            var pts = new List<Vector3>(); var ceilings = new List<FramePlane>();
            Bounds area = new Bounds(c, new Vector3(r * 2f, 10f, r * 2f));
            if (Site.Lot.HasValue && Site.BuildingBounds(out var bb) && Near(bb.center, c, r)) area = bb;
            float floor = area.min.y;
            for (float x = area.min.x + 1f; x < area.max.x; x += 2.5f)
                for (float z = area.min.z + 1f; z < area.max.z; z += 2.5f)
                {
                    Vector3 p = new Vector3(x, floor, z);
                    if (!Near(p, c, r)) continue;
                    FramePlane top = null;
                    try { top = Insulation.FaceFrom(p + Vector3.up * 1.2f, Vector3.up, 5f); } catch { }
                    if (top == null || !top.IsCeiling || ceilings.Exists(o => o.SameFace(top))) continue;
                    ceilings.Add(top); pts.Add(p);
                }
            return pts;
        }

        static DrywallProduct Board => Drywall.Product("dw-12-4x8");

        static float DrywallQty(Vector3 c, float r, out string detail)
        {
            var faces = new List<FramePlane>();
            foreach (var p in RoomPoints(c, r))
                foreach (var fp in DrywallTask.RoomFaces(p)) if (!faces.Exists(o => o.SameFace(fp))) faces.Add(fp);
            int sheets = 0; float finish = 0f;
            foreach (var fp in faces)
            {
                var s = Drywall.SurfaceFor(fp);
                int hung = s != null ? s.Sheets.Count : 0;
                sheets += Mathf.Max(0, Drywall.PlanLayout(fp, Board).Count - hung);
                if (hung > 0 && !Drywall.AllSanded(s)) finish += fp.Bounds.width * fp.Bounds.height / SqFt * 0.5f;   // tape + finish only
            }
            detail = $"{faces.Count} wall/ceiling face(s), {sheets} sheet(s) to hang";
            return sheets * 32f + finish;
        }

        static IEnumerable<DrywallSurface> PaintSurfaces(Vector3 c, float r)
        {
            foreach (var s in Drywall.Surfaces)
                if (s != null && s.Fp != null && s.Sheets.Count > 0 && Near(s.Fp.World(s.Fp.Bounds.center), c, r) && s.Coverage(false, 2) < 0.98f) yield return s;
        }

        static float PaintQty(Vector3 c, float r, out string detail)
        {
            float a = 0f; int n = 0;
            foreach (var s in PaintSurfaces(c, r)) { a += s.Fp.Bounds.width * s.Fp.Bounds.height / SqFt; n++; }
            detail = $"{n} surface(s) to texture, prime and paint";
            return a;
        }

        public static List<Vector3> PaintPoints(Vector3 c, float r)
        {
            var pts = new List<Vector3>();
            foreach (var s in PaintSurfaces(c, r))
            {
                Vector3 ctr = s.Fp.World(s.Fp.Bounds.center);
                Vector3 p = s.Fp.IsCeiling ? ctr + Vector3.down * 2.2f : ctr + s.Fp.N * 1.2f + Vector3.down * 1.2f;
                if (!pts.Exists(o => (o - p).magnitude < 3f)) pts.Add(p);
            }
            return pts;
        }

        // ------------------------------------------------------------------ insulation

        public static List<FramePlane> InsulationFaces(Vector3 c, float r)
        {
            var l = new List<FramePlane>();
            foreach (var fp in InsulationInspection.ExteriorWalls())
                if (fp != null && Near(fp.World(fp.Bounds.center), c, r) && OpenBays(fp) > 0) l.Add(fp);
            return l;
        }

        public static int OpenBays(FramePlane fp)
        {
            int n = 0;
            foreach (var bay in fp.Bays())
            {
                if (bay.IsOpening || bay.R.height < Units.In(3f) || bay.R.width < Units.In(2f)) continue;
                if (Insulation.BattIn(fp, bay) == null && Insulation.FoamIn(fp, bay) == null) n++;
            }
            return n;
        }

        static float InsulationQty(Vector3 c, float r, out string detail)
        {
            float a = 0f; int bays = 0;
            foreach (var fp in InsulationFaces(c, r))
                foreach (var bay in fp.Bays())
                {
                    if (bay.IsOpening || bay.R.height < Units.In(3f) || bay.R.width < Units.In(2f)) continue;
                    if (Insulation.BattIn(fp, bay) != null || Insulation.FoamIn(fp, bay) != null) continue;
                    a += bay.R.width * bay.R.height / SqFt; bays++;
                }
            detail = $"{bays} open stud bay(s) in exterior walls";
            return a;
        }

        // ------------------------------------------------------------------ MEP

        public static List<ElBox> OpenBoxes(Vector3 c, float r)
        {
            var l = new List<ElBox>();
            foreach (var b in ElBox.All)
                if (b != null && Near(b.transform.position, c, r) && (!b.MadeUp || (b.Device == ElDevice.None && b.Kind != ElBoxKind.Round))) l.Add(b);
            return l;
        }

        static float ElectricalQty(Vector3 c, float r, out string detail)
        {
            int n = OpenBoxes(c, r).Count;
            detail = $"{n} box(es) to make up and trim out";
            return n;
        }

        public static List<NetPort> OpenJoints(Trade t, Vector3 c, float r)
        {
            var l = new List<NetPort>();
            foreach (var n in NetGraph.NodesOf(t))
                foreach (var p in n.Ports)
                    if (p.NeedsJoin && Near(p.WorldPos, c, r)) l.Add(p);
            return l;
        }

        static float JointQty(Trade t, Vector3 c, float r, out string detail)
        {
            int n = OpenJoints(t, c, r).Count;
            detail = t == Trade.Hvac ? $"{n} duct / line-set connection(s) to seal" : $"{n} pipe joint(s) to make (sweat, crimp, glue)";
            return n;
        }

        // ------------------------------------------------------------------ landscaping

        static float LandscapeQty(Vector3 c, float r, out string detail)
        {
            float a = 0f; int n = 0;
            foreach (var f in LandscapeFeature.All)
            {
                if (f == null || f.Done || !Near(f.A, c, r)) continue;
                n++;
                Vector3 d = f.B - f.A; d.y = 0f;
                float w = f.Width > 0.01f ? f.Width : Mathf.Max(0.3f, Mathf.Min(Mathf.Abs(d.x), Mathf.Abs(d.z)));
                float len = f.Width > 0.01f ? d.magnitude : Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z));
                float sqft = Mathf.Max(25f, len * w / SqFt);
                var st = f.Stages(); float left = 0f, all = 0f;
                for (int i = 0; i < st.Count; i++) { all += st[i].Steps; if (i > f.Stage) left += st[i].Steps; else if (i == f.Stage) left += st[i].Steps - f.Progress; }
                a += sqft * (all > 0 ? left / all : 1f);
            }
            detail = $"{n} staked landscape job(s)";
            return a;
        }

        // ------------------------------------------------------------------ flooring

        public static List<FramePlane> OpenFloors(Vector3 c, float r)
        {
            var l = new List<FramePlane>();
            foreach (var p in Site.Pieces())
            {
                if (p.Def == null || p.Def.Category != PieceCategory.Sheet || !Near(p.transform.position, c, r)) continue;
                if (Mathf.Abs(Vector3.Dot(p.transform.up, Vector3.up)) < 0.9f && Mathf.Abs(Vector3.Dot(p.transform.forward, Vector3.up)) < 0.9f && Mathf.Abs(Vector3.Dot(p.transform.right, Vector3.up)) < 0.9f) continue;
                if (p.transform.position.y < 0.15f) continue;   // a sheet lying on the ground isn't a subfloor
                FramePlane fp = null;
                try { fp = FramePlane.FromPieceFace(p, Vector3.up); } catch { }
                if (fp == null || !fp.IsFloor || l.Exists(o => o.SameFace(fp))) continue;
                if (FloorInstall.All.Exists(f => f != null && f.Floor != null && f.Floor.SameFace(fp))) continue;
                l.Add(fp);
            }
            return l;
        }

        static float FloorQty(Vector3 c, float r, out string detail)
        {
            float a = 0f; var floors = OpenFloors(c, r);
            foreach (var fp in floors)
            {
                Rect room = Flooring.FindRoom(fp, ClampTo(fp, c));
                a += room.width * room.height / SqFt;
            }
            detail = $"{floors.Count} subfloor area(s) without finish flooring";
            return a;
        }

        public static Vector3 ClampTo(FramePlane fp, Vector3 p)
        {
            var b = fp.WorldBounds();
            return new Vector3(Mathf.Clamp(p.x, b.min.x + 0.3f, b.max.x - 0.3f), b.max.y, Mathf.Clamp(p.z, b.min.z + 0.3f, b.max.z - 0.3f));
        }

        // ------------------------------------------------------------------ concrete / excavation

        public static FormOutline ActiveForm(Vector3 c, float r)
        {
            var o = FormSystem.Active;
            if (o == null || o.Corners.Count < 3) return null;
            Vector3 m = Vector3.zero; foreach (var p in o.Corners) m += p; m /= o.Corners.Count;
            if (!Near(m, c, r + 5f)) return null;
            if (o.Pour != null && o.Pour.Element != null) return null;      // already poured
            return o;
        }

        static float ConcreteQty(Vector3 c, float r, out string detail)
        {
            var o = ActiveForm(c, r);
            if (o == null) { detail = "no formed footing or slab ready to pour"; return 0f; }
            float yd = FormSystem.TakeoffYards(o);
            detail = $"formed slab {Mathf.RoundToInt(o.AreaSqFt)} sq ft, {yd:0.0} cu yd";
            return yd;
        }

        public const float TrenchWidth = 12f * Units.Inch, TrenchDepth = 12f * Units.Inch;

        /// <summary>A turned-down footing trench 12" wide x 12" deep under every side of the formed slab (on the building pad).</summary>
        static List<CrewTask> Trenches(Vector3 c, float r)
        {
            var l = new List<CrewTask>();
            var o = FormSystem.Active;
            if (o == null || o.Corners.Count < 3 || SiteGround.I == null) return l;
            for (int i = 0; i < o.Corners.Count; i++)
            {
                Vector3 a = o.Corners[i], b = o.Corners[(i + 1) % o.Corners.Count];
                a.y = 0f; b.y = 0f;
                if (!Near(a, c, r + 5f) || !SiteGround.InPatch(a) || !SiteGround.InPatch(b)) continue;
                l.Add(new DigTask { A = a, B = b, Radius = TrenchWidth * 0.5f, Depth = TrenchDepth });
            }
            return l;
        }

        static float ExcavationQty(Vector3 c, float r, out string detail)
        {
            float m3 = 0f; float lf = 0f;
            foreach (var t in Trenches(c, r))
            {
                var d = (DigTask)t;
                float len = (d.B - d.A).magnitude; lf += len;
                float sum = 0f;
                for (int i = 0; i < d.Stations; i++) sum += Mathf.Max(0f, d.GroundAt(i) - d.TargetY);
                m3 += len * d.Radius * 2f * sum / d.Stations;
            }
            detail = lf > 0f ? $"{Units.Format(lf)} of footing trench, 12\" x 12\"" : "no formed slab on the building pad to dig footings for";
            return m3 * 1.30795f;   // cu yd
        }
    }
}
