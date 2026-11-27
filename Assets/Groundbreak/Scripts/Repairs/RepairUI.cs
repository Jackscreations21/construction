using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>On-site repair work: aim at a defect within reach, pick an action (Q / Z / wheel), hold the left mouse button with the right
    /// tool in hand. B sends the invoice. Also draws the navigation marker for the Jobs app.</summary>
    public class RepairInteraction : MonoBehaviour
    {
        public static RepairInteraction I;
        public const float Reach = 2.6f;

        RepairDefect _target; string _station;
        List<RepairAction> _acts = new List<RepairAction>();
        int _pick; float _progress, _soundAt, _billConfirmUntil;
        bool _needRelease; string _why = ""; float _whyUntil;
        GUIStyle _label, _head, _small; int _styleH;

        public RepairDefect Target => _target;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("RepairInteraction");
            DontDestroyOnLoad(go);
            I = go.AddComponent<RepairInteraction>();
            Tablet.RegisterApp("Jobs", RepairJobsApp.Draw);
            Tablet.AddHelp("Service calls: Tablet → Jobs. At the house aim at the problem: Q / Z or the wheel picks an action, HOLD left mouse with the right tool in hand.");
            Tablet.AddHelp("Service calls: test before you replace parts. Parts come out of the van (park within 130 ft). B = send the invoice on site.");
        }

        /// <summary>The defect (and station) the player is aiming at, nearest first; walls in front block it.</summary>
        public static RepairDefect Aim(Ray ray, float reach, out string station)
        {
            station = null; RepairDefect best = null; float bd = reach;
            foreach (var d in RepairDefect.All)
            {
                if (d == null || !d.isActiveAndEnabled) continue;
                foreach (var (col, st) in d.Colliders)
                    if (col != null && col.Raycast(ray, out var h, bd)) { bd = h.distance; best = d; station = st; }
            }
            if (best != null && Physics.Raycast(ray, out var wall, bd, Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore) && wall.distance < bd - 0.45f) return null;
            return best;
        }

        void Update()
        {
            var p = Game.Player;
            // navigation marker: clear on arrival
            if (RepairJobs.Waypoint.HasValue && p != null)
            {
                Vector3 d = RepairJobs.Waypoint.Value - p.transform.position; d.y = 0;
                if (d.magnitude < 9f) { Game.Toast("Arrived: " + RepairJobs.WaypointLabel); RepairJobs.ClearWaypoint(); }
            }
            bool driving = WorkVan.I != null && WorkVan.I.Driving;
            if (p == null || Game.UIOpen || driving || !GameSettings.IsCareer || RepairDefect.All.Count == 0) { _target = null; _progress = 0; return; }

            var t = Aim(p.AimRay, Reach, out string st);
            if (t != _target || st != _station) { _target = t; _station = st; _pick = 0; _progress = 0; }
            if (_target == null) return;
            _acts = _target.ActionsAt(_station);
            if (_acts.Count == 0) { _progress = 0; }
            else
            {
                if (!GInput.GetKey(KeyCode.Tab))
                {
                    int step = (GInput.GetKeyDown(KeyCode.Z) && !GInput.Ctrl ? 1 : 0) - (GInput.GetKeyDown(KeyCode.Q) ? 1 : 0) - (int)GInput.Scroll;
                    if (step != 0) { _pick = ((_pick + step) % _acts.Count + _acts.Count) % _acts.Count; _progress = 0; Sfx.Play("click", Vector3.zero, 0.4f); }
                }
                _pick = Mathf.Clamp(_pick, 0, _acts.Count - 1);
                var a = _acts[_pick];
                if (!GInput.Mouse(0)) { _needRelease = false; _progress = 0; }
                else if (!_needRelease && Cursor.lockState == CursorLockMode.Locked)
                {
                    if (!_target.CanStart(a, RepairContext.Player, out string why))
                    {
                        if (GInput.MouseDown(0)) { Say(why); Sfx.Play("error", Vector3.zero, 0.5f); }
                        _progress = 0;
                    }
                    else
                    {
                        _progress += Time.deltaTime / Mathf.Max(0.2f, a.Seconds);
                        if (Time.time >= _soundAt) { Sfx.Play(a.Sound, _target.transform.position, 0.5f); _soundAt = Time.time + 0.6f; }
                        if (_progress >= 1f)
                        {
                            _progress = 0; _needRelease = true;
                            _target.Perform(a.Id, RepairContext.Player, out string msg);
                            Say(msg, 5f);
                        }
                    }
                }
            }
            if (GInput.GetKeyDown(KeyCode.B) && _target != null && _target.Job != null && !(_target.Job.Id ?? "").StartsWith("flip-"))   // flips are never invoiced
            {
                var job = _target.Job;
                if (RepairJobs.AllFixed(job) || Time.time < _billConfirmUntil) { RepairJobs.SendInvoice(job); _target = null; }
                else { _billConfirmUntil = Time.time + 4f; Say("Not everything is fixed. Press B again to bill anyway (expect a callback).", 4f); }
            }
        }

        void Say(string s, float seconds = 3f) { _why = s ?? ""; _whyUntil = Time.time + seconds; }

        void Styles()
        {
            if (_label != null && _styleH == Screen.height) return;
            _styleH = Screen.height;
            int fs = Mathf.Max(12, Screen.height / 54);
            _label = new GUIStyle(GUI.skin.label) { fontSize = fs, wordWrap = true, richText = true };
            _label.normal.textColor = new Color(0.93f, 0.94f, 0.96f);
            _head = new GUIStyle(_label) { fontSize = fs + 3, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(_label) { fontSize = Mathf.Max(11, fs - 2) };
            _small.normal.textColor = new Color(0.65f, 0.69f, 0.74f);
        }

        void OnGUI()
        {
            if (Game.UIOpen || !GameSettings.IsCareer) return;
            Styles();
            DrawWaypoint();
            if (_target == null) return;
            var d = _target;
            float w = Mathf.Max(380f, Screen.height * 0.62f), x = Screen.width * 0.5f + Screen.height * 0.07f, pad = 10f;
            w = Mathf.Min(w, Screen.width - x - 8f);
            string hand = Game.Belt != null && Game.Belt.Active != null ? Game.Belt.Active.Id : null;

            var lines = new List<(string text, GUIStyle style, bool selected)>();
            string where = _station == "panel" ? " — breaker panel" : _station == "gfci" ? " — kitchen GFCI" : "";
            lines.Add((d.Title + where + (d.IsFixed ? "  <color=#8fe28f>FIXED</color>" : ""), _head, false));
            lines.Add(("Customer: <i>" + d.Symptom + "</i>", _small, false));
            lines.Add(("You see: " + d.Observation, _label, false));
            if (!string.IsNullOrEmpty(d.Reading)) lines.Add(("<color=#ffd27a>Last test: " + d.Reading + "</color>", _label, false));
            if (d.Drying) lines.Add(($"<color=#9ad0ff>Drying / setting: about {Mathf.CeilToInt(d.DryMinutesLeft)} min</color>", _small, false));
            for (int i = 0; i < _acts.Count; i++)
            {
                var a = _acts[i];
                bool ok = RepairCatalog.ToolMatches(a.Tool, hand);
                string tool = a.Tool == null ? "" : ok ? $"  <color=#8fe28f>[{a.ToolName}]</color>" : RepairCatalog.Owns(a.Tool) ? $"  <color=#ffb060>[{a.ToolName}]</color>" : $"  <color=#ff7070>[{a.ToolName} — not owned]</color>";
                string mats = "";
                foreach (var (id, n) in a.Mats) mats += $"  <color=#9aa3ad>{n} x {RepairCatalog.MatName(id)}</color>";
                lines.Add(((i == _pick ? "> " : "   ") + a.Label + tool + mats, _label, i == _pick));
            }
            if (_acts.Count == 0) lines.Add((d.IsFixed ? "Nothing more to do here." : "Nothing to do at this spot right now.", _small, false));
            if (Time.time < _whyUntil && _why.Length > 0) lines.Add(("<color=#ffe08a>" + _why + "</color>", _label, false));
            bool all = RepairJobs.AllFixed(d.Job);
            if (d.Job != null && (d.Job.Id ?? "").StartsWith("flip-"))   // your own flip house: no customer, no invoice
                lines.Add((d.IsFixed ? "<color=#8fe28f>Flip punch list item done — see the Flips app</color>" : "Hold LMB = work · Q / Z / wheel = choose · your flip (Flips app)", _small, false));
            else
                lines.Add((all ? "<color=#8fe28f>All repairs done — B sends the invoice</color>" : "Hold LMB = work · Q / Z / wheel = choose · B = invoice", _small, false));

            float h = pad * 2 + 10f;
            var hs = new float[lines.Count];
            for (int i = 0; i < lines.Count; i++) { hs[i] = lines[i].style.CalcHeight(new GUIContent(lines[i].text), w - pad * 2); h += hs[i] + 2f; }
            float y = Mathf.Clamp(Screen.height * 0.5f - h * 0.4f, 8f, Mathf.Max(8f, Screen.height - h - 8f));
            UISkin.Panel(new Rect(x, y, w, h));
            float cy = y + pad;
            for (int i = 0; i < lines.Count; i++)
            {
                var r = new Rect(x + pad, cy, w - pad * 2, hs[i]);
                if (lines[i].selected) UISkin.Panel(new Rect(x + 3, cy - 1, w - 6, hs[i] + 2), new Color(UISkin.Accent.r, UISkin.Accent.g, UISkin.Accent.b, 0.35f));
                GUI.Label(r, lines[i].text, lines[i].style);
                cy += hs[i] + 2f;
            }
            if (_progress > 0f)
            {
                var bar = new Rect(x + pad, y + h - 9f, w - pad * 2, 5f);
                var prev = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = UISkin.Accent; GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(_progress), bar.height), Texture2D.whiteTexture);
                GUI.color = prev;
            }
        }

        void DrawWaypoint()
        {
            var p = Game.Player;
            if (!RepairJobs.Waypoint.HasValue || p == null || p.Cam == null) return;
            Vector3 to = RepairJobs.Waypoint.Value - p.transform.position; to.y = 0;
            Vector3 f = p.Cam.transform.forward; f.y = 0;
            float ang = Vector3.SignedAngle(f, to, Vector3.up), feet = to.magnitude / Units.Foot;
            float cx = Screen.width * 0.5f, top = Mathf.Max(54f, Screen.height * 0.075f), bw = Mathf.Max(260f, Screen.height * 0.36f), bh = _label.fontSize * 1.9f;
            UISkin.Panel(new Rect(cx - bw / 2, top, bw, bh));
            var m = GUI.matrix; var c = GUI.color;
            var pivot = new Vector2(cx - bw / 2 + bh * 0.55f, top + bh / 2);
            GUIUtility.RotateAroundPivot(ang, pivot);
            GUI.color = UISkin.Accent;
            float s = bh * 0.32f;
            GUI.DrawTexture(new Rect(pivot.x - 1.5f, pivot.y - s, 3f, s * 2), Texture2D.whiteTexture);                  // shaft
            GUI.DrawTexture(new Rect(pivot.x - s * 0.45f, pivot.y - s, s * 0.9f, 4f), Texture2D.whiteTexture);        // head (points up = ahead)
            GUI.matrix = m; GUI.color = c;
            GUI.Label(new Rect(cx - bw / 2 + bh * 1.15f, top + 2, bw - bh * 1.2f, bh - 4), $"{RepairJobs.WaypointLabel}  ·  {feet:N0} ft", _label);
        }
    }

    /// <summary>Tablet app "Jobs": service-call offers, the active job's checklist, navigation, invoices.</summary>
    public static class RepairJobsApp
    {
        static string _confirmBill;

        public static void Draw()
        {
            var t = Tablet.I; if (t == null) return;
            GUIStyle label = t.LabelStyle, header = t.HeaderStyle, button = t.ButtonStyle; float fs = label.fontSize;
            if (!GameSettings.IsCareer) { GUILayout.Label("Service calls are for Career mode. (Main menu: F10)", header); return; }
            GUILayout.Label($"{Company.Name}  ·  {Company.Stage}  ·  reputation {Company.Reputation}/100  ·  jobs done {Company.JobsCompleted}", header);
            GUILayout.Label($"Rates: ${RepairJobs.LaborRate}/h book labour, ${RepairJobs.TripCharge} trip charge, parts at cost + 20%. Quotes are estimates; the invoice bills what you actually fixed.", label);
            GUILayout.Space(6);

            // ---- active
            bool anyActive = false;
            foreach (var job in new List<Job>(Company.ActiveJobs))
            {
                if (!RepairJobs.IsService(job)) continue;
                anyActive = true;
                var lot = RepairJobs.LotOf(job);
                var live = RepairJobs.DefectsFor(job);
                float feet = lot != null && Game.Player != null ? Vector3.Distance(Game.Player.transform.position, lot.Parking) / Units.Foot : 0f;
                GUILayout.Label($"ACTIVE: {job.Title}", header);
                GUILayout.Label($"{job.Client}, {job.Address}  ·  {feet:N0} ft away  ·  due day {job.DueDay}" + (job.Payout > 0 ? $"  ·  quoted ${job.Payout:N0}" : "  ·  no charge"), label);
                var p = RepairJobs.Payload(job);
                if (live.Count > 0) foreach (var d in live) GUILayout.Label((d.IsFixed ? "  [x] " : "  [  ] ") + d.Title + " — " + d.Symptom, label);
                else if (p != null) foreach (var s in p.defects) GUILayout.Label("  [  ] " + RepairJobs.Describe(s).title, label);
                var inv = RepairJobs.PreviewInvoice(job);
                GUILayout.Label(inv.callback ? "Invoice now: no charge (callback)"
                    : $"Invoice now: labour ${inv.labor:N0} + parts ${inv.materials:N0} + trip ${inv.trip} = ${inv.total:N0}  ·  customer would pay about ${inv.paid:N0} ({inv.satisfaction * 100f:0}% satisfied)", label);
                GUILayout.BeginHorizontal();
                if (lot != null && GUILayout.Button("Navigate", button, GUILayout.Width(fs * 8))) { RepairJobs.SetWaypoint(lot); t.ShowMessage("Marker set: " + lot.Address); }
                if (lot != null && GUILayout.Button("Drive there", button, GUILayout.Width(fs * 9)))
                    t.ShowMessage(WorkVan.FastTravel(lot) ? "Drove to " + lot.Address : "Can't drive there right now (get to the van first).");
                bool all = RepairJobs.AllFixed(job);
                if (GUILayout.Button(all ? "Send invoice" : _confirmBill == job.Id ? "Really bill unfinished?" : "Bill anyway", button, GUILayout.Width(fs * 13)))
                {
                    if (all || _confirmBill == job.Id) { var r = RepairJobs.SendInvoice(job); _confirmBill = null; if (r != null) t.ShowMessage($"Invoice sent: ${r.total:N0}, paid ${r.paid:N0}" + (r.tip > 0 ? $" + ${r.tip} tip" : "")); }
                    else _confirmBill = job.Id;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(8);
            }
            if (!anyActive) GUILayout.Label("No active service call. Accept one below, load the van at the Hardware Store, and drive over.", label);

            // ---- offers
            GUILayout.Space(4);
            GUILayout.Label("Service calls", header);
            bool anyOffer = false;
            foreach (var job in new List<Job>(Company.Offers))
            {
                if (!RepairJobs.IsService(job)) continue;
                anyOffer = true;
                string block = Company.Blocker(job);
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label($"{job.Title}  —  " + (job.Payout > 0 ? $"est. ${job.Payout:N0}" : "no charge") + $", due day {job.DueDay}", header);
                GUILayout.Label($"{job.Client}, {job.Address}: {job.Description}", label);
                var miss = RepairJobs.MissingTools(job);
                if (miss.Count > 0) GUILayout.Label("May need tools you don't own: " + string.Join(", ", miss.ConvertAll(RepairCatalog.ToolName)), label);
                if (block != null) GUILayout.Label("Can't accept: " + block, label);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUI.enabled = block == null;
                if (GUILayout.Button("Accept", button, GUILayout.Width(fs * 7))) { Company.Accept(job, out var msg); t.ShowMessage(msg); }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }
            if (!anyOffer) GUILayout.Label("No calls right now. New ones come in every morning.", label);

            // ---- history
            if (RepairJobs.Invoices.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("Recent invoices", header);
                foreach (var inv in RepairJobs.Invoices)
                    GUILayout.Label($"Day {inv.day}  {inv.title}, {inv.address}: ${inv.total:N0} billed, ${inv.paid:N0} paid" + (inv.tip > 0 ? $", ${inv.tip} tip" : "")
                        + $"  ·  {inv.satisfaction * 100f:0}% satisfied" + (inv.willCallBack ? "  ·  CALLBACK coming" : ""), label);
            }
            if (RepairJobs.PendingCallbacks > 0) GUILayout.Label($"{RepairJobs.PendingCallbacks} unhappy customer(s) will be calling back.", label);
        }
    }
}
