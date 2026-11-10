using UnityEngine;

namespace Groundbreak
{
    /// <summary>Gravel base handling shared by the excavator and tests: tracks cubic feet drawn from stock.</summary>
    public static class GravelBase
    {
        public const float CubicFeetPerM3 = 35.3147f;
        static float _owedCf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _owedCf = 0; }

        /// <summary>Spreads gravel up to topY in a circle, limited by gravel on site. Returns cubic feet placed.</summary>
        public static float Spread(Vector3 center, float radius, float topY)
        {
            var g = SiteGround.I; if (g == null) return 0;
            float haveCf = Game.Stock.Count(SiteGround.GravelId) - _owedCf;
            if (haveCf <= 0.01f) { Game.Toast("No gravel on site - order 3/4\" crushed stone in the Supply Store (Fasteners & bags)"); return 0; }
            float m3 = g.RaiseTo(center, radius, topY, GroundCover.Gravel, haveCf / CubicFeetPerM3);
            Consume(m3 * CubicFeetPerM3);
            return m3 * CubicFeetPerM3;
        }

        public static float SpreadRect(Vector3 min, Vector3 max, float topY)
        {
            var g = SiteGround.I; if (g == null) return 0;
            float cf = g.RaiseRect(min, max, topY, GroundCover.Gravel) * CubicFeetPerM3;
            Consume(cf);
            return cf;
        }

        static void Consume(float cf)
        {
            _owedCf += cf;
            int whole = Mathf.FloorToInt(_owedCf);
            if (whole > 0) { Game.Stock.TryTake(SiteGround.GravelId, Mathf.Min(whole, Game.Stock.Count(SiteGround.GravelId))); _owedCf -= whole; }
        }
    }

    public class ShovelTool : ConcreteToolBase
    {
        public override string Id => "shovel";
        public override string Name => "Round-Point Shovel";
        public override string Hint => "LMB dig 2\" · RMB backfill 2\" · in wet concrete: LMB push concrete to low spots";

        public override void Primary()
        {
            if (!CTool.Aim(out var hit)) return;
            var pour = hit.collider.GetComponentInParent<PourSystem>();
            if (pour != null && pour.Plastic) { pour.Apply(FinishOp.Rake, hit.point); return; }
            if (SiteGround.I == null || !SiteGround.InPatch(hit.point)) { Game.Toast("Dig inside the staked site"); return; }
            if (SiteGround.I.DigBy(hit.point, 0.13f, 2f * Units.Inch) > 1e-5f) SiteFx.Dig(hit.point);
            ToolUtil.Wear(this, 0.0005f);
        }

        public override void Secondary()
        {
            if (!CTool.Aim(out var hit) || SiteGround.I == null || !SiteGround.InPatch(hit.point)) return;
            SiteGround.I.RaiseTo(hit.point, 0.13f, SiteGround.GroundY(hit.point) + 2f * Units.Inch, GroundCover.Dirt);
            SiteFx.Burst(hit.point, SiteFx.DirtColor, 6, 0.03f, 0.4f, 0.8f);
        }
    }

    public class MiniExcavatorTool : ConcreteToolBase
    {
        public override string Id => "excavator";
        public override string Name => "Mini Excavator (rental)";
        public override int Slots => 2;
        float _depth = 4f * Units.Inch;      // below benchmark
        static GameObject _machine;
        static Transform _house, _boom, _stick;
        float _dig;

        public override string Hint => $"Hold LMB dig to {Units.Format(_depth)} below benchmark (scroll ±1\") · Hold RMB spread gravel up to benchmark grade";

        public override void Tick()
        {
            float s = GInput.Scroll;
            if (s != 0) _depth = Mathf.Clamp(_depth + s * Units.Inch, 0f, 48f * Units.Inch);
            _dig = Mathf.MoveTowards(_dig, 0f, Time.deltaTime * 2f);
            if (_stick != null) _stick.localRotation = Quaternion.Euler(40f + 50f * Mathf.Sin(_dig * Mathf.PI), 0, 0);
            if (GInput.Mouse(1) && !Game.UIOpen) SecondaryHeld();
            _status = CTool.Aim(out var hit, 9f) ? $"Grade here {Units.Format(SiteGround.GroundY(hit.point) - SiteLayout.BenchmarkY)} vs benchmark · target -{Units.Format(_depth)}" : "";
        }

        public override void PrimaryHeld()
        {
            if (!Ready(0.12f) || !CTool.Aim(out var hit, 9f) || SiteGround.I == null || !SiteGround.InPatch(hit.point)) return;
            Park(hit.point);
            float cut = SiteGround.I.DigTo(hit.point, 0.16f, SiteLayout.BenchmarkY - _depth);
            if (cut > 0) { _dig = 1f; SiteFx.Burst(hit.point, SiteFx.DirtColor, 18, 0.05f, 1.2f, 2.2f); }
        }

        void SecondaryHeld()
        {
            if (!Ready(0.12f) || !CTool.Aim(out var hit, 9f) || !SiteGround.InPatch(hit.point)) return;
            Park(hit.point);
            GravelBase.Spread(hit.point, 0.3f, SiteLayout.BenchmarkY);
            SiteFx.Gravel(hit.point);
            _dig = 1f;
        }

        /// <summary>Places the machine about 3 m from the work point, house slewed toward it.</summary>
        void Park(Vector3 work)
        {
            if (_machine == null) BuildMachine();
            Vector3 me = Game.Player != null ? Game.Player.transform.position : work + Vector3.back * 4f;
            Vector3 dir = work - me; dir.y = 0; if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 cur = _machine.transform.position;
            if ((CTool.XZ(cur - work)).magnitude > 4.2f || (CTool.XZ(cur - work)).magnitude < 2.2f)
            {
                Vector3 p = work - dir * 3.1f; p.y = SiteGround.GroundY(p);
                _machine.transform.SetPositionAndRotation(p, Quaternion.LookRotation(dir));
            }
            Vector3 to = work - _machine.transform.position; to.y = 0;
            if (to.sqrMagnitude > 0.01f) _house.rotation = Quaternion.LookRotation(to.normalized);
        }

        static void BuildMachine()
        {
            _machine = new GameObject("MiniExcavator");
            Color yellow = new Color(0.98f, 0.72f, 0.08f);
            CTool.Build("Undercarriage", (b, m) =>
            {
                int trk = m.K(SurfaceKind.Tire), steel = m.K(SurfaceKind.MatteBlack);
                foreach (float x in new[] { -0.62f, 0.62f })
                {
                    // track: rounded-end block
                    b.AddBox(new Vector3(x, 0.22f, 0), new Vector3(0.32f, 0.36f, 1.55f), Quaternion.identity, trk);
                    b.AddCylinder(new Vector3(x, 0.22f, 0.78f), Quaternion.Euler(0, 0, 90), 0.18f, 0.32f, 16, trk);
                    b.AddCylinder(new Vector3(x, 0.22f, -0.78f), Quaternion.Euler(0, 0, 90), 0.18f, 0.32f, 16, trk);
                }
                b.AddBox(new Vector3(0, 0.38f, 0), new Vector3(0.95f, 0.22f, 1.1f), Quaternion.identity, steel);
                b.AddBox(new Vector3(0, 0.28f, 1.05f), new Vector3(1.5f, 0.3f, 0.08f), Quaternion.Euler(-15, 0, 0), m.C(yellow)); // dozer blade
                b.AddCylinder(new Vector3(0, 0.52f, 0), 0.42f, 0.08f, 24, steel);
            }, _machine.transform);

            _house = new GameObject("House").transform;
            _house.SetParent(_machine.transform, false);
            _house.localPosition = new Vector3(0, 0.56f, 0);
            CTool.Build("Upper", (b, m) =>
            {
                int paint = m.K(SurfaceKind.PaintedSteel, yellow), blk = m.K(SurfaceKind.MatteBlack), glass = m.K(SurfaceKind.Glass);
                b.AddBox(new Vector3(0, 0.3f, -0.25f), new Vector3(1.3f, 0.6f, 1.2f), Quaternion.identity, paint);
                b.AddBox(new Vector3(0, 0.25f, -0.82f), new Vector3(1.3f, 0.5f, 0.25f), Quaternion.identity, m.C(new Color(0.25f, 0.25f, 0.26f))); // counterweight
                // ROPS canopy cab
                float cx = -0.28f;
                foreach (var (x, z) in new[] { (cx - 0.33f, 0.25f), (cx + 0.33f, 0.25f), (cx - 0.33f, -0.55f), (cx + 0.33f, -0.55f) })
                    b.AddBox(new Vector3(x, 1.05f, z), new Vector3(0.05f, 0.9f, 0.05f), Quaternion.identity, blk);
                b.AddBox(new Vector3(cx, 1.52f, -0.15f), new Vector3(0.78f, 0.06f, 0.9f), Quaternion.identity, paint);
                b.AddBox(new Vector3(cx, 1.05f, 0.25f), new Vector3(0.62f, 0.8f, 0.01f), Quaternion.identity, glass);
                b.AddBox(new Vector3(cx, 0.75f, -0.25f), new Vector3(0.45f, 0.1f, 0.45f), Quaternion.identity, blk); // seat
                b.AddBox(new Vector3(cx, 1.0f, -0.45f), new Vector3(0.45f, 0.5f, 0.08f), Quaternion.identity, blk);
            }, _house);

            _boom = new GameObject("Boom").transform;
            _boom.SetParent(_house, false);
            _boom.localPosition = new Vector3(0.25f, 0.45f, 0.35f);
            _boom.localRotation = Quaternion.Euler(-35f, 0, 0);
            CTool.Build("BoomMesh", (b, m) =>
            {
                int paint = m.K(SurfaceKind.PaintedSteel, yellow), chrome = m.K(SurfaceKind.BrushedSteel);
                b.AddBox(new Vector3(0, 0, 0.85f), new Vector3(0.16f, 0.22f, 1.75f), Quaternion.identity, paint);
                b.AddRod(new Vector3(0, -0.18f, 0.7f), Quaternion.Euler(-8, 0, 0), 0.04f, 1.0f, 10, chrome);
            }, _boom);

            _stick = new GameObject("Stick").transform;
            _stick.SetParent(_boom, false);
            _stick.localPosition = new Vector3(0, 0, 1.7f);
            _stick.localRotation = Quaternion.Euler(70, 0, 0);
            CTool.Build("StickMesh", (b, m) =>
            {
                int paint = m.K(SurfaceKind.PaintedSteel, yellow), steel = m.K(SurfaceKind.MatteBlack);
                b.AddBox(new Vector3(0, 0, 0.55f), new Vector3(0.12f, 0.16f, 1.15f), Quaternion.identity, paint);
                // 12" digging bucket with teeth
                b.AddBox(new Vector3(0, -0.12f, 1.18f), new Vector3(0.3f, 0.3f, 0.06f), Quaternion.identity, steel);
                b.AddBox(new Vector3(0, -0.26f, 1.05f), new Vector3(0.3f, 0.04f, 0.3f), Quaternion.identity, steel);
                b.AddBox(new Vector3(-0.15f, -0.12f, 1.05f), new Vector3(0.02f, 0.3f, 0.3f), Quaternion.identity, steel);
                b.AddBox(new Vector3(0.15f, -0.12f, 1.05f), new Vector3(0.02f, 0.3f, 0.3f), Quaternion.identity, steel);
                for (int t = -2; t <= 2; t++) b.AddBox(new Vector3(t * 0.06f, -0.3f, 0.9f), new Vector3(0.025f, 0.05f, 0.06f), Quaternion.identity, steel);
            }, _stick);
        }
    }

    public class PlateCompactorTool : ConcreteToolBase
    {
        public override string Id => "compactor";
        public override string Name => "Plate Compactor (rental)";
        public override int Slots => 2;
        GameObject _model;
        public override string Hint => "Hold LMB and walk the plate over the subgrade / gravel (2 passes)";

        public override void PrimaryHeld()
        {
            if (!Ready(0.1f) || !CTool.Aim(out var hit, 4f) || SiteGround.I == null) return;
            SiteGround.I.Compact(hit.point, 0.28f);
            if (_model == null)
                _model = CTool.Build("PlateCompactor", (b, m) =>
                {
                    int steel = m.K(SurfaceKind.MatteBlack), red = m.K(SurfaceKind.TruckPaint), alu = m.K(SurfaceKind.BrushedSteel);
                    b.AddBox(new Vector3(0, 0.03f, 0), new Vector3(0.5f, 0.06f, 0.56f), Quaternion.identity, steel);
                    b.AddBox(new Vector3(0, 0.22f, 0), new Vector3(0.36f, 0.28f, 0.34f), Quaternion.identity, red);
                    b.AddCylinder(new Vector3(0.1f, 0.42f, 0), 0.09f, 0.1f, 14, red);
                    b.AddRod(new Vector3(0, 0.6f, -0.38f), Quaternion.Euler(-35, 0, 0), 0.015f, 0.9f, 8, alu);
                });
            float y = SiteGround.GroundY(hit.point);
            _model.transform.position = new Vector3(hit.point.x, y + 0.005f * Mathf.Sin(Time.time * 120f), hit.point.z);
            var fwd = Game.Player != null ? Game.Player.transform.forward : Vector3.forward;
            _model.transform.rotation = Quaternion.LookRotation(new Vector3(fwd.x, 0, fwd.z).normalized);
        }

        public override void OnUnequip() { if (_model != null) Object.Destroy(_model); }
    }
}
