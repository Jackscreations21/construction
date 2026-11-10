using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Final (Finish phase) inspection rules for interior trim, doors, floors and cabinets.</summary>
    public static class InteriorInspection
    {
        static readonly float Tol = Units.In(1f / 32f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            string F = CloseupInspection.Finish;
            CloseupInspection.Register("fin-casing-reveal", F, "Trim", "Door casing: consistent 3/16\" reveal off the jamb edge on every leg and head", CasingReveal);
            CloseupInspection.Register("fin-coped-corners", F, "Trim", "Inside corners of base and crown are coped, not mitered", Coped);
            CloseupInspection.Register("fin-trim-nailed", F, "Trim", "Trim nailed into framing (studs/plates/jambs) at ≤ 16\" o.c.", Nailed);
            CloseupInspection.Register("fin-trim-caulked", F, "Trim", "Painted trim caulked to the wall, nail holes filled", Caulked);
            CloseupInspection.Register("fin-floor-gap", F, "Flooring", "Floating/solid floors keep a ≥ 1/4\" expansion gap at walls (hidden by base)", FloorGap);
            CloseupInspection.Register("fin-lvp-stagger", F, "Flooring", "LVP end joints staggered ≥ 6\" between adjacent rows", Stagger);
            CloseupInspection.Register("fin-tile-grout", F, "Tile", "Tile grouted, haze cleaned, lippage ≤ 1/32\"", TileOk);
            CloseupInspection.Register("fin-cab-level", F, "Cabinets", "Cabinets level (≤ 1/16\") and screwed to studs through the hang rails", Cabs);
            CloseupInspection.Register("fin-counter-overhang", F, "Cabinets", "Countertop overhang 3/4\"-1-1/2\" past the doors and at free ends", Counter);
            CloseupInspection.Register("fin-door-plumb-swing", F, "Doors", "Doors plumb (≤ 1/16\") and swing clear of cabinets/walls", Doors);
        }

        static IEnumerable<CloseupInspection.Violation> CasingReveal()
        {
            foreach (var d in HungDoor.All)
            {
                if (d == null || d.IsBarn) continue;
                var cs = d.Casings.FindAll(c => c != null);
                if (cs.Count < 6) { yield return new CloseupInspection.Violation("fin-casing-reveal", d.Base + Vector3.up * 2.1f, $"Door not cased on both sides ({cs.Count}/6 pieces)"); continue; }
                float mn = float.MaxValue, mx = 0f;
                foreach (var c in cs) { mn = Mathf.Min(mn, c.Reveal); mx = Mathf.Max(mx, c.Reveal); }
                if (mx - mn > Tol || mn < Units.In(0.125f) - 1e-4f || mx > Units.In(0.25f) + 1e-4f)
                    yield return new CloseupInspection.Violation("fin-casing-reveal", d.Base + Vector3.up * 2.1f, $"Casing reveal inconsistent ({Units.Format(mn)}-{Units.Format(mx)})");
            }
        }

        static IEnumerable<CloseupInspection.Violation> Coped()
        {
            foreach (var t in TrimPiece.All)
            {
                if (t == null) continue;
                if (t.StartCut == TrimCut.InsideMiter) yield return new CloseupInspection.Violation("fin-coped-corners", t.StartPoint + t.Out * 0.03f, $"{t.Kind}: inside corner mitered — cope it");
                if (t.EndCut == TrimCut.InsideMiter) yield return new CloseupInspection.Violation("fin-coped-corners", t.EndPoint + t.Out * 0.03f, $"{t.Kind}: inside corner mitered — cope it");
            }
        }

        static IEnumerable<CloseupInspection.Violation> Nailed()
        {
            foreach (var t in TrimPiece.All)
            {
                if (t == null) continue;
                int need = t.Length < Units.In(16f) ? 1 : Mathf.Max(2, Mathf.FloorToInt(t.Length / Units.In(24f)));
                if (!t.Nailed || t.NailsInFraming < need)
                    yield return new CloseupInspection.Violation("fin-trim-nailed", t.At(t.Length * 0.5f, 0.03f, t.Profile.MaxY * 0.5f), $"{t.Kind} not nailed into framing ({t.NailsInFraming}/{need})");
            }
        }

        static IEnumerable<CloseupInspection.Violation> Caulked()
        {
            foreach (var t in TrimPiece.All)
                if (t != null && t.Species == TrimSpecies.Primed && !t.Caulked)
                    yield return new CloseupInspection.Violation("fin-trim-caulked", t.At(t.Length * 0.5f, 0.03f, t.Profile.MaxY), $"{t.Kind} not caulked");
        }

        static Vector3 FloorPos(FloorInstall f) => f.Floor.World(f.Area.center, 0.02f) + Vector3.up * 0.02f;

        static IEnumerable<CloseupInspection.Violation> FloorGap()
        {
            foreach (var f in FloorInstall.All)
                if (f != null && f.Type != FloorType.Tile && f.Gap < Units.In(0.25f) - 1e-4f)
                    yield return new CloseupInspection.Violation("fin-floor-gap", f.Floor.World(f.Area.min, 0.02f), $"{f.Type}: expansion gap only {Units.Format(f.Gap)} (need 1/4\")");
        }

        static IEnumerable<CloseupInspection.Violation> Stagger()
        {
            foreach (var f in FloorInstall.All)
                if (f != null && f.Type == FloorType.Lvp && f.MinStagger < Units.In(6f) - 1e-4f)
                    yield return new CloseupInspection.Violation("fin-lvp-stagger", FloorPos(f), $"LVP end joints only {Units.Format(f.MinStagger)} apart");
        }

        static IEnumerable<CloseupInspection.Violation> TileOk()
        {
            foreach (var t in TileFloor.All)
            {
                if (t == null || t.Install == null) continue;
                if (t.Step != TileFloor.Stage.Clean) yield return new CloseupInspection.Violation("fin-tile-grout", FloorPos(t.Install), t.Step < TileFloor.Stage.Grouted ? "Tile not grouted" : "Grout haze not cleaned off");
                if (t.MaxLippage > Tol + 1e-5f) yield return new CloseupInspection.Violation("fin-tile-grout", FloorPos(t.Install), $"Tile lippage {Units.ToInches(t.MaxLippage):F3}\" (max 1/32\")");
            }
        }

        static IEnumerable<CloseupInspection.Violation> Cabs()
        {
            foreach (var c in CabinetUnit.All)
            {
                if (c == null || c.IsFiller) continue;
                var p = c.Local(c.Width * 0.5f, c.Height + 0.05f, c.DoorFaceZ);
                if (Mathf.Abs(c.LevelError) > Units.In(1f / 16f)) yield return new CloseupInspection.Violation("fin-cab-level", p, $"Cabinet out of level {Units.Format(Mathf.Abs(c.LevelError))}");
                if (c.ScrewsInStuds < (c.IsWall ? 2 : 1)) yield return new CloseupInspection.Violation("fin-cab-level", p, $"{(c.IsWall ? "Wall" : "Base")} cabinet not screwed into studs ({c.ScrewsInStuds})");
            }
        }

        static IEnumerable<CloseupInspection.Violation> Counter()
        {
            foreach (var t in Countertop.All)
            {
                if (t == null) continue;
                var p = t.transform.TransformPoint(new Vector3(t.Length * 0.5f, t.Thick + 0.05f, t.Depth));
                if (t.FrontOverhang < Units.In(0.75f) - 1e-4f || t.FrontOverhang > Units.In(1.5f) + 1e-4f)
                    yield return new CloseupInspection.Violation("fin-counter-overhang", p, $"Countertop front overhang {Units.Format(t.FrontOverhang)}");
                if (t.OverhangXMin > Units.In(1.5f) || t.OverhangXMax > Units.In(1.5f))
                    yield return new CloseupInspection.Violation("fin-counter-overhang", p, "Countertop end overhang too long");
            }
        }

        static IEnumerable<CloseupInspection.Violation> Doors()
        {
            foreach (var d in HungDoor.All)
            {
                if (d == null) continue;
                if (Mathf.Abs(d.PlumbError) > Units.In(1f / 16f)) yield return new CloseupInspection.Violation("fin-door-plumb-swing", d.Base + Vector3.up * 1.2f, $"Door out of plumb {Units.Format(Mathf.Abs(d.PlumbError))} — shim it");
                var ob = d.SwingObstruction();
                if (ob != null) yield return new CloseupInspection.Violation("fin-door-plumb-swing", d.Base + Vector3.up * 1.2f + d.SwingSide * 0.4f, "Door swing hits " + ob.name);
            }
        }
    }
}
