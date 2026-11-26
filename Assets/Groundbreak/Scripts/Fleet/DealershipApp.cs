using UnityEngine;

namespace Groundbreak
{
    /// <summary>"Dealership" tablet app: buy new/used vehicles cash or financed; your fleet with racks, service and resale.</summary>
    public static class DealershipApp
    {
        static int _term = 1;   // index into Finance.VehicleTerms
        static string _msg = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Tablet.RegisterApp("Dealership", Draw);
            Tablet.AddHelp("Dealership app (tablet) - buy trucks new or used, cash or financed; racks let vans/pickups carry 16'-20' stock");
        }

        static void Draw()
        {
            var t = Tablet.I; if (t == null) return;
            var label = t.LabelStyle; var header = t.HeaderStyle; var button = t.ButtonStyle;
            float fs = label.fontSize;

            GUILayout.Label($"Cash ${Game.Wallet.Money:N0}  ·  Financing: {Finance.VehicleTerms[_term]}-day term, {Finance.VehicleAprNew * 100:0.0}% new / {Finance.VehicleAprUsed * 100:0.0}% used APR", header);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Loan term:", label, GUILayout.Width(fs * 6));
            for (int i = 0; i < Finance.VehicleTerms.Length; i++)
                if (GUILayout.Toggle(_term == i, $" {Finance.VehicleTerms[i]} days", button, GUILayout.Width(fs * 7))) _term = i;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_msg)) GUILayout.Label(_msg, header);
            GUILayout.Space(6);

            foreach (var v in FleetCatalog.All)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                int owned = 0; foreach (var o in Company.Vehicles) if (o.TypeId == v.Id) owned++;
                GUILayout.Label($"{v.Name}  —  {v.Model}" + (owned > 0 ? $"   (you own {owned})" : ""), header);
                GUILayout.Label(v.Description, label);
                string cap = v.Machine ? "Equipment: no cargo." :
                    $"Payload {v.PayloadLb:N0} lb · {v.VolumeCuFt:N0} cu ft · bed {v.BedLabel} · longest stock {Units.Format(v.MaxItemLength)}" +
                    (v.RackMaxItemLength > 0 ? $" ({Units.Format(v.RackMaxItemLength)} with ${v.RackPrice:N0} rack)" : "") +
                    (v.Bulk ? " · loose gravel/soil" : "") + (v.Crane ? " · crane: trusses" : "") + (v.Enclosed ? " · enclosed" : "");
                GUILayout.Label(cap, label);
                GUILayout.Label($"Running costs ~${v.InsurancePerDay + v.MaintenancePerDay}/day insurance + maintenance" + (v.Mpg > 0 ? $", {v.Mpg:0} mpg" : ""), label);
                GUILayout.BeginHorizontal();
                int term = Finance.VehicleTerms[_term];
                foreach (bool used in new[] { false, true })
                {
                    int price = Fleet.Price(v.Id, used), down = Fleet.DownPayment(v.Id, used);
                    float apr = used ? Finance.VehicleAprUsed : Finance.VehicleAprNew;
                    int pay = Finance.PaymentFor(price - down, apr, term);
                    string kind = used ? "Used" : "New";
                    if (GUILayout.Button($"{kind} ${price:N0} cash", button, GUILayout.Width(fs * 11))) { Fleet.Buy(v.Id, used, 0, out _msg); }
                    if (GUILayout.Button($"{kind}: ${down:N0} down + ${pay:N0}/day", button, GUILayout.Width(fs * 15))) { Fleet.Buy(v.Id, used, term, out _msg); }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(10);
            GUILayout.Label("Your fleet", header);
            if (Company.Vehicles.Count == 0) GUILayout.Label("  (no vehicles)", label);
            foreach (var o in Company.Vehicles.ToArray())
            {
                var vt = FleetCatalog.Get(o.TypeId);
                var loan = Finance.LoanOn(o.Uid);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{o.Name}  ·  condition {o.Condition * 100:0}%" + (Fleet.HasRack(o.Uid) ? " · rack" : "") +
                                (loan != null ? $" · owes ${loan.Balance:N0}" : "") + (Fleet.IsBusy(o.Uid) ? " · ON A RUN" : ""), label, GUILayout.Width(fs * 26));
                GUILayout.FlexibleSpace();
                if (vt != null && vt.RackMaxItemLength > 0 && !Fleet.HasRack(o.Uid) && GUILayout.Button($"Add rack ${vt.RackPrice:N0}", button, GUILayout.Width(fs * 8))) Fleet.AddRack(o, out _msg);
                int svc = Fleet.ServiceCost(o);
                GUI.enabled = svc > 0;
                if (GUILayout.Button(svc > 0 ? $"Service ${svc:N0}" : "Serviced", button, GUILayout.Width(fs * 7))) Fleet.Service(o, out _msg);
                GUI.enabled = true;
                if (GUILayout.Button($"Sell ${Fleet.ResaleValue(o):N0}", button, GUILayout.Width(fs * 8))) Fleet.Sell(o, out _msg);
                GUILayout.EndHorizontal();
            }
        }
    }
}
