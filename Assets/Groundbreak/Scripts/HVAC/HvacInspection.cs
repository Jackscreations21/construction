using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Rough mechanical inspection rules (IRC M1601 / M1411 / M1501, simplified) + the HVAC tablet section.</summary>
    public static class HvacInspection
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Rule("hvac-joints", "Duct joints and seams sealed with UL 181 tape or mastic (IRC M1601.4.1)", Joints);
            Rule("hvac-open", "No open duct ends or unused openings in the supply system", OpenEnds);
            Rule("hvac-boots", "Every supply boot connected to a branch duct", Boots);
            Rule("hvac-airflow", "Each room receives ≥ 90% of its design airflow (Manual D)", Airflow);
            Rule("hvac-return", "Return air path from the conditioned space to the air handler (IRC M1602)", Return);
            Rule("hvac-exhaust", "Bath/kitchen exhaust ducted to the outdoors through a cap with damper, not into the attic (IRC M1501.1)", Exhaust);
            Rule("hvac-condensate", "Condensate drain ≥ 1/8\"/ft slope to an approved place of disposal (IRC M1411.3)", Condensate);
            Rule("hvac-lineset", "Line set brazed to condenser + coil, suction line insulated (IRC M1411.6)", LineSets);

            MepTablet.AddSection(Trade.Hvac, "Mechanical", DrawTablet);
            Tablet.AddHelp("HVAC: plenum on the furnace, 8x12 trunk to an end cap, takeoff tool on the trunk, 6\" pipe to boots + registers, return grille back to the return drop. Foil-tape every joint.");
            MepBootstrap.AddSystem(go => go.AddComponent<HvacSystem>());
            ToolFactory.Register(new ToolShopItem { Id = "takeoff-tool", Name = "Takeoff punch + collars", Price = 35, Description = "Cut round starting collars (5–8\") into rectangular trunk. LMB on trunk, RMB size." },
                () => new TakeoffTool());
        }

        static void Rule(string id, string text, System.Func<IEnumerable<Finding>> check)
            => MepInspection.Register(new MepRule { Id = id, Trade = Trade.Hvac, Text = text, Check = check });

        static HvacReport R => AirflowSim.Evaluate();

        static IEnumerable<Finding> Joints()
        {
            foreach (var p in HvacBuild.OpenJoints(NetKind.Duct))
                yield return new Finding { Note = $"Un-taped duct joint at {p.Node.Label}", Pos = p.WorldPos };
        }

        static IEnumerable<Finding> OpenEnds()
        {
            foreach (var u in R.Units)
                foreach (var t in u.Terminals)
                    if (t.Kind == TerminalKind.OpenEnd)
                        yield return new Finding { Note = $"Open duct end dumps {t.Delivered:0} CFM into the attic/house", Pos = t.Pos };
        }

        static IEnumerable<Finding> Boots()
        {
            foreach (var b in R.DisconnectedBoots)
                yield return new Finding { Note = $"{b.Label} not connected to a branch duct", Pos = b.transform.position };
        }

        static IEnumerable<Finding> Airflow()
        {
            var rep = R;
            if (rep.Units.Count == 0) yield break;
            foreach (var ra in rep.Rooms)
                if (!ra.Ok)
                    yield return new Finding { Note = $"{ra.Room.RoomName}: {ra.Delivered:0} of {ra.Required:0} CFM ({ra.Percent:0}%) - LOW", Pos = ra.Room.Bounds.center };
        }

        static IEnumerable<Finding> Return()
        {
            foreach (var u in R.Units)
                if (!u.ReturnConnected)
                    yield return new Finding { Note = $"{u.Unit.Node.Label}: no return air connected", Pos = u.Unit.transform.position + Vector3.up * 0.3f };
        }

        static IEnumerable<Finding> Exhaust()
        {
            foreach (var e in R.Exhaust)
                if (!e.Ok) yield return new Finding { Note = $"{e.Fan.Node.Label}: {e.Note}", Pos = e.Ducted ? e.EndPoint : e.Fan.transform.position };
        }

        static IEnumerable<Finding> Condensate()
        {
            foreach (var c in R.Condensate)
                if (!c.Ok) yield return new Finding { Note = $"{c.Source.Label} condensate: {c.Note}", Pos = !c.Sloped ? c.BadPoint : c.Connected ? c.EndPoint : c.Source.transform.position };
        }

        static IEnumerable<Finding> LineSets()
        {
            foreach (var l in R.LineSets)
                if (!l.Ok) yield return new Finding { Note = $"{l.Condenser.Node.Label} line set: {l.Note}", Pos = l.Condenser.transform.position + Vector3.up * 0.5f };
        }

        // ------------------------------------------------------------------ tablet

        static GUIStyle L => Tablet.I != null ? Tablet.I.LabelStyle : GUI.skin.label;
        static GUIStyle H => Tablet.I != null ? Tablet.I.HeaderStyle : GUI.skin.label;

        static void DrawTablet()
        {
            var rep = AirflowSim.Current;
            GUILayout.Label("Equipment", H);
            int count = 0;
            foreach (var e in HvacEquipment.All)
            {
                if (e == null) continue; count++;
                string extra = e.RatedCfm > 0 ? $" · {e.RatedCfm:0} CFM" : e.Tons > 0 ? $" · {e.Tons:0.#} ton" : "";
                GUILayout.Label($"• {e.Node.Label}{extra}", L);
            }
            int tstats = 0; foreach (var p in Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)) if (HvacParts.IsThermostat(p)) tstats++;
            if (tstats > 0) GUILayout.Label($"• Thermostat x{tstats}", L);
            if (count == 0) GUILayout.Label("No HVAC equipment placed yet.", L);

            GUILayout.Space(4);
            GUILayout.Label("Airflow by room", H);
            foreach (var u in rep.Units)
                GUILayout.Label($"{u.Unit.Node.Label}: {u.RatedCfm:0} CFM rated · {u.Delivered:0} to registers · {u.Leaked:0} leaked · {u.Dumped:0} dumped · return {(u.ReturnConnected ? "connected" : "MISSING")}", L);
            if (rep.Rooms.Count == 0) GUILayout.Label("No rooms defined.", L);
            foreach (var ra in rep.Rooms)
                GUILayout.Label($"{ra.Room.RoomName} ({ra.Room.FloorAreaSqFt:0} ft²): {ra.Delivered:0} / {ra.Required:0} CFM  {(ra.Ok ? "OK" : "LOW")} · {ra.Boots} boots, {ra.Registers} registers · return {(ra.ReturnInRoom ? "in room" : ra.ReturnPath ? "central" : "NONE")}", L);

            GUILayout.Space(4);
            GUILayout.Label("Duct leakage", H);
            if (rep.UntapedDuctJoints.Count == 0) GUILayout.Label("All duct joints taped.", L);
            else
            {
                GUILayout.Label($"{rep.UntapedDuctJoints.Count} un-taped joints · ~{rep.TotalLeaked:0} CFM lost", L);
                for (int i = 0; i < Mathf.Min(6, rep.UntapedDuctJoints.Count); i++) GUILayout.Label("  - " + rep.UntapedDuctJoints[i].Node.Label, L);
            }
            foreach (var b in rep.DisconnectedBoots) GUILayout.Label("  - disconnected: " + b.Label, L);

            GUILayout.Space(4);
            GUILayout.Label("Line set / condensate / exhaust", H);
            foreach (var l in rep.LineSets) GUILayout.Label($"{l.Condenser.Node.Label}: {l.Note}", L);
            foreach (var c in rep.Condensate) GUILayout.Label($"{c.Source.Label} condensate: {c.Note}", L);
            foreach (var e in rep.Exhaust) GUILayout.Label($"{e.Fan.Node.Label}: {e.Note}", L);
        }
    }

    /// <summary>Per-frame HVAC system on the "MEP Systems" object (keeps the cached report warm).</summary>
    public class HvacSystem : MonoBehaviour
    {
        public static HvacReport Last;
        int _ver = -1; float _next;
        void LateUpdate()
        {
            if (_ver == NetGraph.Version && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            if (_ver != NetGraph.Version) { _ver = NetGraph.Version; Last = AirflowSim.Evaluate(); }
            HvacLook.Apply();
        }
    }

    /// <summary>Hand tool: punches a hole in rectangular trunk and installs a spin-in starting collar facing the aimed face.</summary>
    public class TakeoffTool : Tool
    {
        static readonly float[] Sizes = { 5f, 6f, 7f, 8f };
        int _size = 1;
        public override string Id => "takeoff-tool";
        public override string Name => "Takeoff Punch";
        public override string Hint => $"LMB on trunk: install {Sizes[_size]:0}\" starting collar · RMB: change size";

        public override void Secondary() { _size = (_size + 1) % Sizes.Length; Game.Toast($"Collar size {Sizes[_size]:0}\""); }

        public override void Primary()
        {
            var r = MepAim.AimRun(out var hit);
            if (r == null || r.Spec.Kind != NetKind.Duct || r.Spec.Shape != RunShape.Rect) { Game.Toast("Aim at a rectangular trunk"); return; }
            var n = HvacBuild.Takeoff(r, hit.point, hit.normal, Sizes[_size]);
            if (n == null) Game.Toast("Can't cut a collar here (too close to a joint)");
            else { Game.Toast($"{Sizes[_size]:0}\" collar installed - tape it after the branch is on"); ToolUtil.Wear(this, 0.001f); }
        }

        public override void DrawGUI()
        {
            var r = MepAim.AimRun(out _);
            if (r != null && r.Spec.Shape == RunShape.Rect) ToolUtil.CenterLabel($"{r.Spec.Name}: LMB to cut in a {Sizes[_size]:0}\" takeoff", new Color(0.7f, 0.9f, 1f));
        }
    }
}
