using System.Collections.Generic;
using UnityEngine;
using V = Groundbreak.CloseupInspection.Violation;

namespace Groundbreak
{
    /// <summary>Insulation inspection rules (phase "Insulation") as data. Targets live in <see cref="InsulationRules"/>.
    /// Rule ids: ins-fill, ins-wall-r, ins-staple, ins-compress, ins-firestop, ins-airseal, ins-vapor, ins-attic-r,
    /// ins-rulers, ins-baffles.</summary>
    public static class InsulationInspection
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            const string P = CloseupInspection.Insulation;
            CloseupInspection.Register("ins-fill", P, "IRC N1102.4.1.1", "Every exterior wall cavity filled — no voids or gaps", Fill);
            CloseupInspection.Register("ins-wall-r", P, "IRC Table N1102.1.2", "Exterior wall cavity insulation meets the minimum R-value (data: InsulationRules.WallMinR)", WallR);
            CloseupInspection.Register("ins-staple", P, "Mfr. instructions", "Kraft-faced batts stapled (flanges every 8\")", Staples);
            CloseupInspection.Register("ins-compress", P, "IRC N1102.2 / Mfr.", "Batts split around boxes and pipes — no compressed insulation", Compressed);
            CloseupInspection.Register("ins-firestop", P, "IRC R302.11", "Plate penetrations (pipes, wires) fire-blocked with fire caulk", FireStop);
            CloseupInspection.Register("ins-airseal", P, "IRC Table N1102.4.1.1", "Electrical boxes in exterior walls air-sealed", AirSeal);
            CloseupInspection.Register("ins-vapor", P, "IRC R702.7", "Class I/II vapor retarder on exterior walls where the climate zone requires it", Vapor);
            CloseupInspection.Register("ins-attic-r", P, "IRC Table N1102.1.2", "Attic insulation meets the minimum R-value everywhere (data: InsulationRules.AtticMinR)", AtticR);
            CloseupInspection.Register("ins-rulers", P, "IRC N1101.10.1.1", "Attic depth markers: one per 300 sq ft of blown insulation", AtticRulers);
            CloseupInspection.Register("ins-baffles", P, "IRC R806.3", "Eave baffles in every insulated eave bay (vent channel kept open)", Baffles);
        }

        // ---------------------------------------------------------- face discovery

        /// <summary>All wall faces in the world (both sides of each stud wall), deduplicated.</summary>
        static int _wfFrame = -1, _exFrame = -1;
        static List<FramePlane> _wf, _ex;

        /// <summary>Optional inspection area (null = whole site): only faces whose bounds touch it are inspected.</summary>
        public static Bounds? Scope;

        public static List<FramePlane> WallFaces()
        {
            if (_wfFrame == Time.frameCount && _wf != null) return _wf;
            var faces = new List<FramePlane>();
            var seen = new HashSet<Piece>();
            foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (p.Def == null || p.Def.Category != PieceCategory.Lumber || seen.Contains(p)) continue;
                if (Mathf.Abs(Vector3.Dot(p.transform.forward, Vector3.up)) < 0.95f) continue; // studs stand up
                var t = p.transform;
                Vector3 depthAxis = p.Size.x >= p.Size.y ? t.right : t.up;
                foreach (var n in new[] { depthAxis, -depthAxis })
                {
                    bool dup = false;
                    foreach (var f in faces) if (Vector3.Dot(f.N, n) > 0.99f && f.Members.Exists(m => m.Piece == p)) { dup = true; break; }
                    if (dup) continue;
                    var fp = FramePlane.FromPieceFace(p, n);
                    if (fp == null || !fp.IsWall) continue;
                    faces.Add(fp);
                }
                seen.Add(p);
            }
            if (Scope.HasValue) faces.RemoveAll(f => !f.WorldBounds().Intersects(Scope.Value));
            _wf = faces; _wfFrame = Time.frameCount;
            return faces;
        }

        public static List<FramePlane> ExteriorWalls()
        {
            if (_exFrame == Time.frameCount && _ex != null) return _ex;
            var l = new List<FramePlane>();
            foreach (var f in WallFaces()) if (Insulation.IsExterior(f)) l.Add(f);
            _ex = l; _exFrame = Time.frameCount;
            return l;
        }

        static bool Insulable(FramePlane.Bay b) => !b.IsOpening && b.R.height >= Units.In(3f) && b.R.width >= Units.In(2f);

        // ---------------------------------------------------------- rules

        static IEnumerable<V> Fill()
        {
            foreach (var fp in ExteriorWalls())
                foreach (var bay in fp.Bays())
                {
                    if (!Insulable(bay)) continue;
                    var b = Insulation.BattIn(fp, bay); var f = Insulation.FoamIn(fp, bay);
                    if (b == null && f == null) yield return new V("ins-fill", fp.World(bay.R.center, 0.05f), "Empty exterior wall bay — insulate it");
                    else if (b != null && b.P.Width < bay.R.width - Units.In(0.25f)) yield return new V("ins-fill", b.FaceCenter, $"{b.P.WidthIn:0}\" batt in a {Units.Format(bay.R.width)} bay leaves gaps");
                }
        }

        static IEnumerable<V> WallR()
        {
            foreach (var fp in ExteriorWalls())
                foreach (var bay in fp.Bays())
                {
                    if (!Insulable(bay)) continue;
                    var b = Insulation.BattIn(fp, bay); var f = Insulation.FoamIn(fp, bay);
                    float r = b != null ? b.EffectiveR : f != null ? f.EffectiveR : -1f;
                    if (r >= 0f && r < InsulationRules.WallMinR - 0.01f)
                        yield return new V("ins-wall-r", fp.World(bay.R.center, 0.05f), $"R-{r:0.#} < required R-{InsulationRules.WallMinR:0}");
                }
        }

        static IEnumerable<V> Staples()
        {
            foreach (var b in Insulation.Batts)
                if (b != null && b.P.Faced && !b.Stapled) yield return new V("ins-staple", b.FaceCenter, "Faced batt not stapled — staple the flanges");
        }

        static IEnumerable<V> Compressed()
        {
            foreach (var b in Insulation.Batts)
                if (b != null && b.Compressed)
                {
                    var o = b.Obstacles[0];
                    yield return new V("ins-compress", b.Fp.World(o.R.center, 0.05f), "Batt jammed over a box/pipe (compressed) — split it around");
                }
        }

        static IEnumerable<V> FireStop()
        {
            var done = new List<Vector3>();
            foreach (var fp in WallFaces())
                foreach (var p in Insulation.Penetrations(fp))
                {
                    bool dup = false; foreach (var q in done) if ((q - p).sqrMagnitude < 0.01f) dup = true;
                    if (dup) continue; done.Add(p);
                    if (!Insulation.IsFireBlocked(p)) yield return new V("ins-firestop", p + fp.N * 0.05f, "Plate penetration not fire-blocked — fire caulk");
                }
        }

        static IEnumerable<V> AirSeal()
        {
            if (!InsulationRules.RequireBoxAirSeal) yield break;
            foreach (var fp in ExteriorWalls())
                foreach (var (c, o) in Insulation.Boxes(fp))
                    if (!Insulation.IsBoxSealed(c)) yield return new V("ins-airseal", c + fp.N * 0.06f, "Box in exterior wall not air-sealed — foam/caulk it");
        }

        static IEnumerable<V> Vapor()
        {
            if (!InsulationRules.ColdClimate) yield break;
            foreach (var fp in ExteriorWalls())
            {
                if (Insulation.HasPoly(fp)) continue;
                foreach (var bay in fp.Bays())
                {
                    if (!Insulable(bay)) continue;
                    var b = Insulation.BattIn(fp, bay);
                    if (b != null && b.P.Faced) continue;                         // kraft = Class II
                    var f = Insulation.FoamIn(fp, bay);
                    if (f != null && f.Thick >= Units.In(1.5f)) continue;         // 1.5"+ closed cell = Class II
                    yield return new V("ins-vapor", fp.World(bay.R.center, 0.05f), $"Climate zone {InsulationRules.ClimateZone}: vapor retarder required (kraft facing or 6-mil poly)");
                }
            }
        }

        static IEnumerable<V> AtticR()
        {
            foreach (var a in Insulation.Attics)
            {
                if (a == null || a.AverageDepth < 0.01f) continue;
                if (a.RValue < InsulationRules.AtticMinR - 0.01f)
                    yield return new V("ins-attic-r", a.Fp.World(a.Fp.Bounds.center, -0.3f), $"Attic R-{a.RValue:0} (thin spots) < required R-{InsulationRules.AtticMinR:0} = {InsulationRules.AtticMinR / InsulationRules.LooseFillRPerInch:0.0}\" loose fill");
            }
            foreach (var b in Insulation.Batts)
                if (b != null && b.Fp.IsCeiling && b.EffectiveR < InsulationRules.AtticMinR - 0.01f)
                    yield return new V("ins-attic-r", b.Fp.World(b.R.center, -0.2f), $"Ceiling batt R-{b.EffectiveR:0} < required R-{InsulationRules.AtticMinR:0}");
        }

        static IEnumerable<V> AtticRulers()
        {
            foreach (var a in Insulation.Attics)
            {
                if (a == null || a.AverageDepth < 0.01f) continue;
                int need = Insulation.RulersNeeded(a.Fp), have = Insulation.RulersIn(a.Fp);
                if (have < need) yield return new V("ins-rulers", a.Fp.World(a.Fp.Bounds.center, -0.4f), $"Depth rulers: {have} of {need} (1 per {InsulationRules.RulerEverySqFt:0} sq ft)");
            }
        }

        static IEnumerable<V> Baffles()
        {
            var ceilings = new List<FramePlane>();
            foreach (var a in Insulation.Attics) if (a != null && a.AverageDepth > 0.01f) ceilings.Add(a.Fp);
            foreach (var b in Insulation.Batts) if (b != null && b.Fp.IsCeiling && !ceilings.Exists(c => c.SameFace(b.Fp))) ceilings.Add(b.Fp);
            if (ceilings.Count == 0) yield break;
            var ext = ExteriorWalls();
            foreach (var c in ceilings)
                foreach (var (bay, end) in Insulation.EaveBays(c, ext))
                    if (!Insulation.HasBaffle(end)) yield return new V("ins-baffles", end + Vector3.up * 0.3f, "Insulated eave bay without a baffle");
        }
    }
}
