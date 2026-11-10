using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Rough electrical inspection rules (NEC simplified).</summary>
    public static class ElRules
    {
        public const float StapleFromBox = 8f * 0.0254f;     // 314.17(B)(1) exception: non-metallic box without clamps
        public const float StapleSpacing = 4.5f * 0.3048f;   // 334.30

        static void R(string id, string text, System.Func<IEnumerable<Finding>> check) =>
            MepInspection.Register(new MepRule { Id = id, Trade = Trade.Electrical, Text = text, Check = check });

        public static void Register()
        {
            R("el-height", "Receptacles 12-16\" to center, countertop 42-44\", switches 48\" (house standard)", Heights);
            R("el-staples", "NM cable stapled within 8\" of every box and at least every 4-1/2' (NEC 334.30, 314.17)", StapleFindings);
            R("el-fill", "Box fill within volume: 2.0 cu in per 14 AWG, 2.25 per 12 AWG, grounds = 1, device = 2 (NEC 314.16)", Fill);
            R("el-gauge", "Conductor ampacity matches breaker: 14 AWG ≤ 15 A, 12 AWG ≤ 20 A, 10 AWG ≤ 30 A (NEC 240.4(D))", Gauge);
            R("el-grounds", "Equipment grounds spliced and pigtailed in every box (NEC 250.148)", GroundsCheck);
            R("el-afci", "15/20 A dwelling outlet and lighting circuits AFCI protected (NEC 210.12, simplified)", Afci);
            R("el-gfci", "Bath, kitchen, outdoor receptacles GFCI protected (NEC 210.8)", Gfci);
            R("el-label", "Every circuit in the panel labeled; every home run landed (NEC 408.4)", Labels);
            R("el-fixture", "Every fixture mounted on a listed box", Fixtures);
        }

        static Finding F(Vector3 p, string note) => new Finding { Pos = p, Note = note };

        static IEnumerable<Finding> Heights()
        {
            foreach (var b in ElBox.All)
            {
                if (b == null || b.Kind == ElBoxKind.Round || b.Device == ElDevice.None) continue;
                float h = Units.ToInches(b.HeightAboveFloor());
                if (b.IsSwitch && (h < 44f || h > 50f)) yield return F(b.FrontWorld, $"Switch box at {h:0.#}\" to center - set switches at 48\"");
                else if (b.IsReceptacle && b.Countertop && (h < 42f || h > 44.5f)) yield return F(b.FrontWorld, $"Countertop receptacle at {h:0.#}\" - 42-44\" to center");
                else if (b.IsReceptacle && !b.Countertop && (h < 12f || h > 16.5f)) yield return F(b.FrontWorld, $"Receptacle at {h:0.#}\" to center - 12-16\"");
            }
        }

        /// <summary>Distances (along the run) where it is supported by passing through a bored hole in framing.</summary>
        public static List<float> HoleSupports(NetRun r)
        {
            var o = new List<float>();
            var pts = r.Points; float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector3 a = pts[i - 1], b = pts[i]; float l = Vector3.Distance(a, b);
                if (l < 1e-4f) continue;
                foreach (var h in Physics.RaycastAll(a, (b - a) / l, l, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    var p = h.collider.GetComponentInParent<Piece>();
                    if (p != null && p.Def != null && p.Def.Category == PieceCategory.Lumber && p.GetComponent<StudHoles>() != null) o.Add(acc + h.distance);
                }
                acc += l;
            }
            return o;
        }

        public static IEnumerable<Finding> StapleFindings()
        {
            foreach (var r in new List<NetRun>(NetGraph.RunsOf(Trade.Electrical)))
            {
                if (!ElCatalog.IsBranch(r)) continue;
                float L = r.Length;
                var sup = new List<float>(r.Staples);
                sup.AddRange(HoleSupports(r));
                sup.Sort();
                bool boxA = r.NodeA != null && r.NodeA.GetComponent<ElBox>() != null, boxB = r.NodeB != null && r.NodeB.GetComponent<ElBox>() != null;
                bool panA = r.NodeA != null && r.NodeA.GetComponent<ElPanel>() != null, panB = r.NodeB != null && r.NodeB.GetComponent<ElPanel>() != null;
                if (boxA && !r.Staples.Exists(s => s <= StapleFromBox)) yield return F(r.PointAt(0.1f, out _), $"{r.Spec.Name}: no staple within 8\" of the box");
                if (boxB && !r.Staples.Exists(s => s >= L - StapleFromBox)) yield return F(r.PointAt(L - 0.1f, out _), $"{r.Spec.Name}: no staple within 8\" of the box");
                // spacing between supports (ends at boxes/panel count as supported by the clamp/staple rule above)
                var all = new List<float>();
                if (boxA || panA) all.Add(0f);
                all.AddRange(sup);
                if (boxB || panB) all.Add(L);
                all.Sort();
                for (int i = 1; i < all.Count; i++)
                    if (all[i] - all[i - 1] > StapleSpacing + 0.01f)
                    { yield return F(r.PointAt((all[i] + all[i - 1]) * 0.5f, out _), $"{r.Spec.Name}: unsupported for {Units.Format(all[i] - all[i - 1])} (staple every 4-1/2')"); break; }
            }
        }

        static IEnumerable<Finding> Fill()
        {
            foreach (var b in ElBox.All)
            {
                if (b == null) continue;
                float f = b.FillCuIn();
                if (f > b.VolumeCuIn + 0.01f) yield return F(b.FrontWorld, $"Box fill {f:0.#} cu in exceeds {b.VolumeCuIn:0.#} cu in box");
            }
        }

        static IEnumerable<Finding> Gauge()
        {
            foreach (var p in ElPanel.All)
                foreach (var br in p.Breakers)
                    foreach (var r in br.Runs)
                        if (r != null && r.Spec.Ampacity + 0.01f < br.Amps)
                            yield return F(r.PointAt(r.Length * 0.5f, out _), $"{r.Spec.Awg} AWG ({r.Spec.Name}) on a {br.Amps} A breaker (space {br.Slot}) - max {r.Spec.Ampacity:0} A");
        }

        static IEnumerable<Finding> GroundsCheck()
        {
            foreach (var b in ElBox.All)
            {
                if (b == null || b.Cables.Count == 0) continue;
                if (!b.Stripped || !b.Grounds) yield return F(b.FrontWorld, "Grounds not spliced/pigtailed - OPEN GROUND");
                else if (b.Circuit != null && !b.GroundIn) yield return F(b.FrontWorld, "Open ground upstream - ground path broken");
            }
        }

        static IEnumerable<Finding> Afci()
        {
            foreach (var p in ElPanel.All)
                foreach (var br in p.Breakers)
                {
                    if (br.Run == null || br.Poles != 1 || br.Amps > 20 || br.Afci) continue;
                    bool outlets = br.Loads.Count > 0 || br.Boxes.Exists(b => b.IsReceptacle || b.Kind == ElBoxKind.Round);
                    if (outlets) yield return F(p.transform.TransformPoint(p.SpaceLocal(br.Slot)), $"Space {br.Slot} ({br.Name}) feeds outlets/lighting - needs AFCI or dual-function breaker");
                }
        }

        static IEnumerable<Finding> Gfci()
        {
            foreach (var b in ElBox.All)
            {
                if (b == null || !b.Wet || !b.IsReceptacle) continue;
                bool ok = b.Device == ElDevice.Gfci || (b.Circuit != null && b.Circuit.Gfci);
                for (var u = b.Parent; u != null && !ok; u = u.Parent) if (u.Device == ElDevice.Gfci) ok = true;
                if (!ok) yield return F(b.FrontWorld, "Wet-location receptacle without GFCI protection");
            }
        }

        static IEnumerable<Finding> Labels()
        {
            foreach (var p in ElPanel.All)
            {
                foreach (var br in p.Breakers)
                    if (br.Run != null && string.IsNullOrEmpty(br.Label?.Trim()))
                        yield return F(p.transform.TransformPoint(p.SpaceLocal(br.Slot)), $"Circuit in space {br.Slot} not labeled");
                foreach (var r in p.Unlanded()) yield return F(p.transform.position + Vector3.up * p.Piece.Size.y * 0.5f, $"Home run {r.Spec.Name} not landed on a breaker");
            }
        }

        static IEnumerable<Finding> Fixtures()
        {
            foreach (var l in ElLoad.All)
                if (l != null && l.FindBox() == null) yield return F(l.transform.position, $"{l.Piece.Def.Name} not mounted on a box");
        }
    }
}
