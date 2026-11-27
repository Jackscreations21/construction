using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Tablet apps: "Plans" (plan library, takeoff, overlay), "Builder" (client houses, draws, change orders, warranty)
    /// and "Development" (land, subdivision, spec houses, market, profit). Plus the neighborhood completion screen.</summary>
    public static class DevApps
    {
        static int _plan, _floor, _dev, _specPlan = 1, _finish = 1, _grp, _cat;
        static readonly string[] Groups = { "All", "Residential", "Outbuilding", "Commercial", "Industrial", "My plans" };
        // "My plans" (PlanDesigner): the player's own custom-* plans
        static bool InGroup(HousePlan x, int g) => g == 0 || (g == 5 ? x.Id.StartsWith("custom-") : x.Group == (PlanGroup)(g - 1));
        static readonly string[] FinishNames = { "Builder grade", "Standard", "Premium" };
        static readonly float[] FinishLevels = { 0.35f, 0.6f, 0.9f };
        public static string SelectedPlanId { get => HousePlans.All[Mathf.Clamp(_plan, 0, HousePlans.All.Count - 1)].Id; set { for (int i = 0; i < HousePlans.All.Count; i++) if (HousePlans.All[i].Id == value) _plan = i; } }

        static GUIStyle L => Tablet.I.LabelStyle; static GUIStyle Hd => Tablet.I.HeaderStyle; static GUIStyle B => Tablet.I.ButtonStyle;
        static float Fs => L.fontSize;
        static void Msg(string m) { if (Tablet.I != null) Tablet.I.ShowMessage(m); Game.Toast(m); }

        static void Bar(float v, float w, Color c)
        {
            var r = GUILayoutUtility.GetRect(w, Fs * 0.9f, GUILayout.Width(w));
            if (Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = new Color(1, 1, 1, 0.15f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c; GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        static void PhaseBars(HouseProject p)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 5; i++)
            {
                var b = (BuildPhase)i;
                GUILayout.Label(HousePlan.Label(b), L, GUILayout.Width(Fs * 5.5f));
                Bar(p.PhaseDone(b) ? 1f : p.Progress[i], Fs * 4f, p.PhaseDone(b) ? new Color(0.3f, 0.85f, 0.4f) : new Color(1f, 0.75f, 0.2f));
                GUILayout.Space(Fs * 0.5f);
            }
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ Plans

        static void PlansApp()
        {
            var plans = HousePlans.All;
            // filters (Blueprints): group, then type within the group
            _grp = GUILayout.Toolbar(Mathf.Clamp(_grp, 0, Groups.Length - 1), Groups, B);
            var cats = new List<string> { "All types" };
            foreach (var x in plans) if (InGroup(x, _grp) && !cats.Contains(x.Category)) cats.Add(x.Category);
            if (cats.Count > 2) _cat = GUILayout.SelectionGrid(Mathf.Clamp(_cat, 0, cats.Count - 1), cats.ToArray(), 6, B); else _cat = 0;
            var idx = new List<int>();
            for (int i = 0; i < plans.Count; i++)
                if (InGroup(plans[i], _grp) && (_cat <= 0 || _cat >= cats.Count || plans[i].Category == cats[_cat])) idx.Add(i);
            if (idx.Count == 0) { GUILayout.Label(_grp == 5 ? "No plans of your own yet: draw one in the Plan Designer app." : "No plans in this filter.", L); return; }
            var names = new string[idx.Count]; for (int i = 0; i < names.Length; i++) names[i] = plans[idx[i]].Name.Replace("The ", "") + $" {plans[idx[i]].SqFt:N0}";
            int sel = Mathf.Max(0, idx.IndexOf(_plan));
            sel = GUILayout.SelectionGrid(sel, names, 3, B); _plan = idx[Mathf.Clamp(sel, 0, idx.Count - 1)];
            var p = plans[_plan]; var t = p.Takeoff;
            GUILayout.Space(6);
            GUILayout.Label(p.Name, Hd);
            GUILayout.Label(p.Summary + $" · {p.WallHeightFt}' walls, {p.StudSize} @ 16\" O.C.", L);
            GUILayout.Label("Rooms: " + string.Join(", ", p.Rooms.ConvertAll(r => $"{r.Name} {r.Ft.width:0}x{r.Ft.height:0}" + (p.Floors > 1 ? $" (F{r.Floor + 1})" : ""))), L);
            GUILayout.Label($"Walls: {t.ExtWallLf:0} lf exterior, {t.IntWallLf:0} lf interior · {t.Windows} windows, {t.ExtDoors} exterior + {t.IntDoors} interior doors · inspections: " +
                string.Join(", ", p.RequiredInspections.ConvertAll(Inspections.Label)), L);
            GUILayout.BeginHorizontal();
            var on = Projects.OnSite;
            if (GUILayout.Button(on != null ? "Show overlay on my site" : "Show overlay (needs an active site)", B) && on != null)
            { PlanOverlay.Show(p, PlanPlacement.On(on.Lot, p), _floor); Msg("Plan overlay laid out on " + on.Lot.Address); }
            if (p.Floors > 1 && GUILayout.Button($"Floor {_floor + 1} of {p.Floors}", B, GUILayout.Width(Fs * 8))) { _floor = (_floor + 1) % p.Floors; if (on != null) PlanOverlay.Show(p, PlanPlacement.On(on.Lot, p), _floor); }
            if (PlanOverlay.Root != null && GUILayout.Button("Hide overlay", B, GUILayout.Width(Fs * 8))) PlanOverlay.Hide();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.Label("Material takeoff (from real dimensions)", Hd);
            BuildPhase? last = null;
            foreach (var line in t.Lines)
            {
                if (last != line.Phase) { GUILayout.Label(HousePlan.Label(line.Phase) + $" — materials ${t.Materials(line.Phase):N0} + labor ${t.Labor[(int)line.Phase]:N0}", Hd); last = line.Phase; }
                GUILayout.BeginHorizontal();
                GUILayout.Label(line.Item, L, GUILayout.Width(Fs * 22));
                GUILayout.Label($"{line.Qty:#,0.#} {line.Unit}", L, GUILayout.Width(Fs * 8));
                GUILayout.Label($"@ ${line.UnitCost:N0}", L, GUILayout.Width(Fs * 6));
                GUILayout.Label($"${line.Cost:N0}", L);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label($"Estimated build cost ${t.Total:N0} (${t.Total / p.SqFt:0}/sq ft) · {p.TotalDays} crew days", Hd);
        }

        // ------------------------------------------------------------------ Builder

        internal static void BuilderApp()
        {
            if (!GameSettings.IsCareer) { GUILayout.Label("Builder jobs are part of Career mode.", Hd); return; }
            GUILayout.Label($"{Company.Name} · stage {Company.Stage} · reputation {Company.Reputation}", Hd);
            if (Company.Stage < CompanyStage.Builder) GUILayout.Label("Full houses need the Builder stage: a yard + shop (warehouse level 2), a crew, reputation 65.", L);
            var on = Projects.OnSite;
            if (on != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"On site: {on.Plan.Name} @ {on.Lot.Address} · next inspection {Inspections.CurrentPhaseLabel}", L);
                if (GUILayout.Button("Measure", B, GUILayout.Width(Fs * 6))) Projects.Measure(on);
                if (GUILayout.Button("Leave site", B, GUILayout.Width(Fs * 7))) { Projects.LeaveSite(); DevVisuals.Refresh(); }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            GUILayout.Label("Client house contracts", Hd);
            foreach (var j in Company.Offers.ToArray())
            {
                if (!Builder.IsHouseJob(j)) continue;
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label($"{j.Title} - ${j.Payout:N0} for {j.Client} at {j.Address}", Hd);
                GUILayout.Label(j.Description, L);
                GUILayout.EndVertical();
                string block = Company.Blocker(j);
                GUI.enabled = block == null;
                if (GUILayout.Button(block == null ? "Sign" : "Locked", B, GUILayout.Width(Fs * 6))) { Company.Accept(j, out var m); Msg(m); GUIUtility.ExitGUI(); }   // the project list changes: end this GUI event
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (block != null) GUILayout.Label("   " + block, L);
            }
            foreach (var p in Projects.All)
            {
                if (p.Kind != ProjectKind.Client) continue;
                GUILayout.Space(6);
                GUILayout.Label($"{p.Client}: {p.Plan.Name} @ {p.Lot.Address} - {p.StatusLabel}" + (p.Complete ? "" : $" · due day {p.DueDay}"), Hd);
                PhaseBars(p);
                int paid = 0; for (int i = 0; i < 5; i++) if ((p.DrawsPaid & (1 << i)) != 0) paid += Mathf.RoundToInt(p.ContractPrice * Builder.DrawPct[i]);
                paid += Mathf.RoundToInt(p.ContractPrice * Builder.DepositPct);
                GUILayout.Label($"Contract ${p.ContractPrice:N0} + change orders ${p.ChangeOrderTotal:N0} · received ${paid:N0} · spent ${p.CostSpent:N0}", L);
                if (!p.Complete && p.Id != Projects.OnSiteId && GUILayout.Button("Work this house myself (go to site)", B)) { Projects.GoOnSite(p); GoTo(p.Lot); }
                foreach (var c in p.ChangeOrders)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Change order: {c.Title} +${c.Price:N0}, +{c.Days} days", L);
                    if (!c.Accepted && !c.Declined)
                    {
                        if (GUILayout.Button("Accept", B, GUILayout.Width(Fs * 6))) { Builder.AcceptChange(p, c, out var m); Msg(m); }
                        if (GUILayout.Button("Decline", B, GUILayout.Width(Fs * 6))) c.Declined = true;
                    }
                    else GUILayout.Label(c.Accepted ? "signed" : "declined", L, GUILayout.Width(Fs * 6));
                    GUILayout.EndHorizontal();
                }
                if (p.WarrantyJobId != null && !p.WarrantyDone && GUILayout.Button($"Warranty call: send a carpenter (${Builder.WarrantyCost})", B)) { Builder.ResolveWarranty(p, out var m); Msg(m); }
            }
        }

        public static void GoTo(DevLot lot)
        {
            if (Game.Player == null) return;
            var cc = Game.Player.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            Game.Player.transform.position = lot.FrontMid - lot.Front * 4f + Vector3.up * 1.1f;
            Game.Player.SetLook(lot.Yaw, 0f);
            if (cc != null) cc.enabled = true;
            if (DeliveryService.I != null) DeliveryService.I.SetDropPoint(lot.FrontMid - lot.Front * 8f + Vector3.Cross(Vector3.up, lot.Front) * 8f);
        }

        // ------------------------------------------------------------------ Development

        static void DevelopmentApp()
        {
            if (!GameSettings.IsCareer) { GUILayout.Label("Land development is part of Career mode.", Hd); return; }
            if (GameSettings.IsCareer && Company.Stage < CompanyStage.Developer)
                GUILayout.Label("Developing needs the Developer stage: HQ (warehouse level 3), 8+ workers, reputation 80.", L);
            GUILayout.Label($"Market index {Developments.MarketCycle(Projects.Day):P0} of trend · construction loans ${Developments.LoanBalance:N0} at {Developments.LoanRate:P1}", Hd);
            GUILayout.Label("Raw land for sale", Hd);
            foreach (var t in DevLots.Provider.LandForSale())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{t.Address}: {t.Acres:0.0} acres ({t.FrontageFt:0}' x {t.DepthFt:0}') at ${Developments.PricePerAcre(t):N0}/ac = ${Developments.LandPrice(t):N0}", L);
                if (GUILayout.Button("Buy", B, GUILayout.Width(Fs * 5))) { Developments.BuyLand(t, out _, out var m); Msg(m); GUIUtility.ExitGUI(); }
                GUILayout.EndHorizontal();
            }
            if (Developments.All.Count == 0) return;
            var names = Developments.All.ConvertAll(d => d.Name).ToArray();
            _dev = GUILayout.SelectionGrid(Mathf.Clamp(_dev, 0, names.Length - 1), names, 4, B);
            var dv = Developments.All[_dev];
            GUILayout.Label($"{dv.Name} — {dv.Tract.Acres:0.0} ac on {dv.Tract.Address}", Hd);
            if (!dv.Platted)
            {
                GUILayout.BeginHorizontal();
                foreach (float fr in new[] { 60f, 70f, 80f })
                    if (GUILayout.Button($"Plat {fr:0}' lots", B)) { Developments.Subdivide(dv, fr, out var m); Msg(m); }
                GUILayout.EndHorizontal();
                return;
            }
            SitePlan(dv);
            if (dv.SiteWorkStartDay < 0)
            {
                if (GUILayout.Button($"Hire site work: street, curbs, water/sewer/power (${Developments.SiteWorkCostFor(dv):N0}, {dv.SiteWorkDays} days)", B)) { Developments.StartSiteWork(dv, out var m); Msg(m); }
            }
            else if (!dv.SiteWorkDone) { GUILayout.BeginHorizontal(); GUILayout.Label("Site work", L, GUILayout.Width(Fs * 6)); Bar(dv.SiteWorkProgress, Fs * 12, new Color(1f, 0.75f, 0.2f)); GUILayout.EndHorizontal(); }

            var plans = new List<HousePlan>(); foreach (var x in HousePlans.All) if (x.Group == PlanGroup.Residential && x.WidthFt <= 60f) plans.Add(x);   // spec homes only (single lots)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Spec plan:", L, GUILayout.Width(Fs * 5));
            if (GUILayout.Button(plans[_specPlan % plans.Count].Name, B)) _specPlan = (_specPlan + 1) % plans.Count;
            if (GUILayout.Button(FinishNames[_finish], B, GUILayout.Width(Fs * 8))) _finish = (_finish + 1) % 3;
            GUILayout.EndHorizontal();
            int day = Projects.Day;
            foreach (var lot in dv.Lots)
            {
                HouseProject h = null; foreach (var x in dv.Houses) if (x.LotId == lot.Id) h = x;
                GUILayout.BeginHorizontal();
                GUILayout.Label(lot.Address, L, GUILayout.Width(Fs * 9));
                if (h == null)
                {
                    GUILayout.Label($"empty {lot.FrontageFt:0}'x{lot.DepthFt:0}'", L, GUILayout.Width(Fs * 12));
                    if (GUILayout.Button($"Build {plans[_specPlan % plans.Count].Name.Replace("The ", "")}", B)) { Developments.StartSpecHouse(dv, lot, plans[_specPlan % plans.Count], FinishLevels[_finish], out var m); Msg(m); }
                }
                else
                {
                    GUILayout.Label(h.Plan.Name.Replace("The ", "").Split(' ')[0] + " · " + h.StatusLabel, L, GUILayout.Width(Fs * 12));
                    Bar(h.Overall, Fs * 5, h.Sold ? new Color(0.3f, 0.85f, 0.4f) : new Color(1f, 0.75f, 0.2f));
                    int val = Developments.MarketValue(h, day);
                    GUILayout.Label($"cost ${h.CostSpent / 1000f:0}k · value ${val / 1000f:0}k" + (h.Sold ? $" · sold ${h.SalePrice / 1000f:0}k · profit ${Developments.Profit(h) / 1000f:0}k" : h.Listed ? $" · listed ${h.ListPrice / 1000f:0}k ({day - h.ListedDay} days)" : ""), L);
                    if (h.Complete && !h.Listed && !h.Sold)
                    {
                        if (GUILayout.Button("List at value", B, GUILayout.Width(Fs * 7))) { Developments.List(h, val, out var m); Msg(m); }
                        if (GUILayout.Button("List +5%", B, GUILayout.Width(Fs * 5))) { Developments.List(h, Mathf.RoundToInt(val * 1.05f / 1000f) * 1000, out var m); Msg(m); }
                    }
                }
                GUILayout.EndHorizontal();
            }
            int cost = 0, rev = 0; foreach (var h in dv.Houses) { cost += h.CostSpent; rev += h.Sold ? h.SalePrice : 0; }
            GUILayout.Label($"Land ${dv.LandCost:N0} · plat ${dv.SoftCosts:N0} · site work ${dv.SiteWorkCost:N0} · interest ${dv.InterestPaid:N0} · carry ${dv.CarryPaid:N0} · sales ${rev:N0} ({dv.SoldCount}/{dv.Lots.Count} sold)", L);
        }

        /// <summary>Top-down site plan: tract, street, lots colored by status.</summary>
        static void SitePlan(Development d)
        {
            var t = d.Tract.Rect;
            float scale = Mathf.Min(Fs * 26f / t.width, Fs * 16f / t.height);
            var r = GUILayoutUtility.GetRect(t.width * scale, t.height * scale, GUILayout.Width(t.width * scale), GUILayout.Height(t.height * scale));
            if (Event.current.type != EventType.Repaint) return;
            Rect Map(Rect w) => new Rect(r.x + (w.xMin - t.xMin) * scale, r.y + (t.yMax - w.yMax) * scale, w.width * scale, w.height * scale);
            var prev = GUI.color;
            void Fill(Rect w, Color c) { GUI.color = c; GUI.DrawTexture(Map(w), Texture2D.whiteTexture); }
            Fill(t, new Color(0.35f, 0.5f, 0.3f));
            Fill(d.StreetRect, d.SiteWorkDone ? new Color(0.25f, 0.25f, 0.27f) : new Color(0.6f, 0.55f, 0.45f));
            foreach (var lot in d.Lots)
            {
                HouseProject h = null; foreach (var x in d.Houses) if (x.LotId == lot.Id) h = x;
                Color c = h == null ? new Color(0.5f, 0.65f, 0.4f) : h.Sold ? new Color(0.3f, 0.6f, 0.95f) : h.Complete ? new Color(0.3f, 0.85f, 0.4f) : Color.Lerp(new Color(0.85f, 0.5f, 0.2f), new Color(1f, 0.85f, 0.3f), h.Overall);
                var m = Map(lot.Rect); GUI.color = c; GUI.DrawTexture(new Rect(m.x + 1, m.y + 1, m.width - 2, m.height - 2), Texture2D.whiteTexture);
            }
            GUI.color = prev;
        }

        // ------------------------------------------------------------------ completion

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Tablet.RegisterApp("Plans", () => { if (Tablet.I != null) PlansApp(); });
            Tablet.RegisterApp("Builder", () => { if (Tablet.I != null) BuilderApp(); });
            Tablet.RegisterApp("Development", () => { if (Tablet.I != null) DevelopmentApp(); });
            Tablet.SetAppGroup("Plans", "Plans"); Tablet.SetAppGroup("Builder", "Business"); Tablet.SetAppGroup("Development", "Business");
            Tablet.AddHelp("Plans app: pick a house plan, read its takeoff, lay the plan overlay (walls, openings, rooms) on your site");
            Developments.Completed += d => NeighborhoodComplete.Show(d);
        }
    }

    /// <summary>End-game screen when every lot of a development is sold.</summary>
    public class NeighborhoodComplete : MonoBehaviour
    {
        public static NeighborhoodComplete I;
        public Development Dev;
        public static NeighborhoodComplete Show(Development d)
        {
            if (I == null) I = new GameObject("NeighborhoodComplete").AddComponent<NeighborhoodComplete>();
            I.Dev = d; d.CompletionShown = true; Game.UIOpen = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            return I;
        }

        public static string[] Summary(Development d)
        {
            int rev = 0, cost = 0, dom = 0, n = 0; float best = float.MinValue; string bestLot = "";
            foreach (var h in d.Houses) { rev += h.SalePrice; cost += h.CostSpent; if (h.Sold) { dom += h.SoldDay - h.ListedDay; n++; } float pr = Developments.Profit(h); if (pr > best) { best = pr; bestLot = h.Lot.Address + " (" + h.Plan.Name + ")"; } }
            int days = (d.CompletedDay > 0 ? d.CompletedDay : Projects.Day) - d.BoughtDay;
            return new[]
            {
                $"{d.Lots.Count} homes built and sold in {days} days on {d.Tract.Acres:0.0} acres",
                $"Sales ${rev:N0} · all-in cost ${cost:N0} (land ${d.LandCost:N0}, site work ${d.SiteWorkCost:N0}, interest ${d.InterestPaid:N0})",
                $"Net profit ${rev - cost:N0} ({(cost > 0 ? (rev - cost) / (float)cost : 0):P1} on cost) · average {(n > 0 ? dom / n : 0)} days on market",
                $"Best house: {bestLot}, profit ${best:N0}",
            };
        }

        void OnGUI()
        {
            if (Dev == null) return;
            UISkin.Apply();
            float w = Screen.width * 0.6f, h = Screen.height * 0.5f;
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            UISkin.Panel(r, new Color(0.06f, 0.09f, 0.08f, 0.95f));
            int fs = Mathf.Max(14, Screen.height / 40);
            var head = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(fs * 1.6f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            head.normal.textColor = new Color(1f, 0.85f, 0.35f);
            var body = new GUIStyle(GUI.skin.label) { fontSize = fs, alignment = TextAnchor.MiddleCenter, wordWrap = true }; body.normal.textColor = Color.white;
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 16, r.width - 40, r.height - 32));
            GUILayout.Label($"{Dev.Name} is complete!", head);
            GUILayout.Label("Every lot is built and sold. The street is full of families.", body);
            GUILayout.Space(fs);
            foreach (var line in Summary(Dev)) GUILayout.Label(line, body);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Keep building", new GUIStyle(GUI.skin.button) { fontSize = fs })) { Dev = null; Game.UIOpen = false; Destroy(gameObject); I = null; }
            GUILayout.EndArea();
        }
    }
}
