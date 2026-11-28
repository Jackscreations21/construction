using UnityEngine;

namespace Groundbreak
{
    /// <summary>What the Stair Layout Kit measured at the aimed edge (all world positions in meters).</summary>
    public class StairPlan
    {
        /// <summary>An edge with a top surface and ground below was found.</summary>
        public bool Measured;
        /// <summary>Primary may start a job (rise is a real stair / landing fits).</summary>
        public bool CanStart;
        /// <summary>Problem or advice ("add a landing", "single step"...). Empty when all is well.</summary>
        public string Message = "";
        public float TopY, FootY, RiseM;
        /// <summary>Ground point under the middle of the stair at the support face (y = foot ground).</summary>
        public Vector3 FaceGround;
        /// <summary>Stair frame yaw: +Z runs up the stairs toward the support.</summary>
        public float Yaw;
        public StairResult R;
        public Vector3 Bottom;
        /// <summary>Rim / beam / ledger the stairs hang from (null for anything that isn't framing).</summary>
        public Transform Support;
        public bool FromCalculator;
        // landing mode
        public int UpperRisers, LowerRisers;
        public Vector3 LandingCenter;
        public float LandingHeightM, LandingSizeM;
        public Vector3 UpperBottom, LowerBottom;
        public StairResult UpperR, LowerR;

        public bool NeedsLanding => Measured && R.Valid && R.Risers >= 2 && !R.LengthOk;
    }

    /// <summary>Stair Layout Kit: 25' tape, story pole, chalk line and a 4' level. Aim at the edge of an upper floor, deck or landing
    /// (the rim / beam face, or the floor near its edge): it measures the total rise plumb down to where the bottom step lands,
    /// shows the stair footprint as a ghost and starts the guided stair job (<see cref="StairBuilder.Stairs"/>).
    /// RMB switches to landing mode (<see cref="StairBuilder.Landing"/>) for rises too tall for one 16' stringer.</summary>
    public class StairLayoutKit : Tool
    {
        public const string ToolId = "stairkit";
        public override string Id => ToolId;
        public override string Name => "Stair Layout Kit";

        public const float Range = 8f;
        public const float MinWidthIn = StairMath.MinWidthIn;     // IRC R311.7.1
        public const float MaxWidthIn = 48f;                      // three 2x12 stringers: 2x6 treads span 24" max
        public const float WidthStepIn = 6f;
        public const float MinLandingIn = 36f, MaxLandingIn = 72f;  // IRC R311.7.6: landing at least 36" deep and as wide as the stair
        const float MaxSearchFt = 40f;
        const int Mask = Physics.DefaultRaycastLayers & ~(1 << 2);

        public enum KitMode { Stairs, Landing }
        public KitMode Mode = KitMode.Stairs;
        public float WidthIn { get; private set; } = MinWidthIn;
        public float LandingIn { get; private set; } = 48f;
        /// <summary>Latest measurement (updated every Tick and on Primary).</summary>
        public StairPlan Plan { get; private set; } = new StairPlan { Message = "Aim at the edge of a floor, deck or landing" };
        /// <summary>The job the last Primary started (null if refused).</summary>
        public FramingJob LastJob { get; private set; }

        GameObject _ghost;
        MeshFilter _ghostMf;
        MeshRenderer _ghostMr;
        string _ghostKey = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            ToolFactory.Register(new ToolShopItem
            {
                Id = ToolId,
                Name = "Stair Layout Kit",
                Price = 45,
                Description = "25' tape, story pole, chalk line and 4' level. Aim at a deck / floor / landing edge: measures the total rise, "
                              + "shows the stair footprint and starts a guided stair job (36\" min wide). RMB: landing mode for tall rises.",
            }, () => new StairLayoutKit());
            Tablet.AddHelp("Stair Layout Kit: aim at the edge of a deck, floor or landing - it measures the rise and shows the stairs. "
                           + "-/= width (36\" min), LMB start the stair job, RMB landing mode (when the stringer would pass 16')");
        }

        // ------------------------------------------------------------------ settings

        public void SetWidthIn(float inches)
        {
            float w = MinWidthIn + Mathf.Round((inches - MinWidthIn) / WidthStepIn) * WidthStepIn;
            WidthIn = Mathf.Clamp(w, MinWidthIn, MaxWidthIn);
            if (LandingIn < WidthIn) LandingIn = Mathf.Min(MaxLandingIn, Mathf.Ceil(WidthIn / WidthStepIn) * WidthStepIn);
        }

        public void ChangeWidth(int dir) => SetWidthIn(WidthIn + dir * WidthStepIn);

        public void SetLandingIn(float inches)
        {
            float s = Mathf.Round(inches / WidthStepIn) * WidthStepIn;
            LandingIn = Mathf.Clamp(s, Mathf.Max(MinLandingIn, WidthIn), MaxLandingIn);
        }

        // ------------------------------------------------------------------ tool events

        public override void Tick()
        {
            if (!Game.UIOpen)
            {
                int d = (GInput.GetKeyDown(KeyCode.Equals) ? 1 : 0) - (GInput.GetKeyDown(KeyCode.Minus) ? 1 : 0);
                if (d != 0)
                {
                    if (Mode == KitMode.Stairs) { float was = WidthIn; ChangeWidth(d); Game.Toast(WidthIn == was ? (d < 0 ? "Stairs must be at least 36\" wide (IRC R311.7.1)" : "48\" max on three stringers") : "Stair width " + StairMath.FormatIn(WidthIn)); }
                    else { SetLandingIn(LandingIn + d * WidthStepIn); Game.Toast("Landing " + StairMath.FormatFtIn(LandingIn) + " x " + StairMath.FormatFtIn(LandingIn)); }
                }
            }
            if (Game.Player != null) Plan = Measure(Game.Player.AimRay);
            UpdateGhost();
        }

        public override void OnUnequip() => DestroyGhost();

        public override void Secondary()
        {
            if (Game.UIOpen) return;
            Mode = Mode == KitMode.Stairs ? KitMode.Landing : KitMode.Stairs;
            if (Mode == KitMode.Landing && LandingIn < WidthIn) SetLandingIn(WidthIn);
            Game.Toast(Name + ": " + (Mode == KitMode.Stairs ? "stairs" : "landing (splits a tall rise into two flights)"));
        }

        public override void Primary()
        {
            LastJob = null;
            if (Game.UIOpen || Game.Player == null) return;
            if (Durability <= 0f) { Game.Toast(Name + " is worn out"); return; }
            Plan = Measure(Game.Player.AimRay);
            var p = Plan;
            if (!p.CanStart) { Game.Toast(string.IsNullOrEmpty(p.Message) ? "Aim at the edge of a floor, deck or landing" : p.Message); return; }

            if (Mode == KitMode.Stairs)
            {
                var job = StairBuilder.Stairs(p.Bottom, p.Yaw, p.RiseM, Units.In(WidthIn), p.Support);
                if (job.Steps.Count == 0)
                {
                    string why = string.IsNullOrEmpty(StairBuilder.LastError) ? "Can't build stairs here" : StairBuilder.LastError;
                    Object.Destroy(job.gameObject);
                    Game.Toast(p.NeedsLanding ? why + " (RMB: landing mode)" : why);
                    return;
                }
                LastJob = job;
                StairCalculator.UseForLayout(p.R);   // the framing square's gauges now match the job's first step
                Game.Toast("Stairs laid out: " + p.R.Risers + " risers @ " + StairMath.FormatIn(p.R.UnitRiseIn) + ", " + p.R.Treads + " treads @ "
                           + StairMath.FormatIn(p.R.UnitRunIn) + " - order " + Takeoff(job) + " (framing square gauges set)");
            }
            else
            {
                var job = StairBuilder.Landing(p.LandingCenter, p.Yaw, p.LandingSizeM, p.LandingHeightM);
                if (job.Steps.Count == 0) { Object.Destroy(job.gameObject); Game.Toast("Landing too low for posts + 2x8 framing"); return; }
                LastJob = job;
                Game.Toast("Landing laid out " + StairMath.FormatFtIn(Units.ToInches(p.LandingHeightM)) + " high: build it, then the Stair Layout Kit on each edge - "
                           + p.UpperRisers + " risers up to the floor, " + p.LowerRisers + " down to grade. Order " + Takeoff(job));
            }
            ToolUtil.Wear(this, 0.001f);
        }

        static string Takeoff(FramingJob job)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var kv in job.Takeoff())
            {
                var d = Catalog.Get(kv.Key);
                parts.Add(kv.Value + " x " + (d != null ? d.Name : kv.Key));
            }
            return string.Join(", ", parts);
        }

        // ------------------------------------------------------------------ measuring

        static bool Cast(Vector3 from, Vector3 dir, float dist, out RaycastHit hit) =>
            Physics.Raycast(from, dir, out hit, dist, Mask, QueryTriggerInteraction.Ignore);

        static bool IsFraming(Collider c)
        {
            if (!(c is BoxCollider)) return false;
            var m = c.GetComponentInParent<StructuralMember>();
            if (m == null) return false;
            switch (m.Role)
            {
                case MemberRole.RimJoist: case MemberRole.Beam: case MemberRole.Ledger: case MemberRole.Joist:
                case MemberRole.Blocking: case MemberRole.Header: return true;
                default: return false;
            }
        }

        /// <summary>Measures the edge the ray points at with the current mode/width/landing size. Pure query: builds nothing.</summary>
        public StairPlan Measure(Ray aim)
        {
            var p = new StairPlan();
            if (!Cast(aim.origin, aim.direction, Range, out var hit)) { p.Message = "Aim at the edge of a floor, deck or landing"; return p; }

            // outward direction (away from the support, toward where the stairs land)
            Vector3 n;
            bool faceHit = Mathf.Abs(hit.normal.y) < 0.5f;
            if (faceHit) n = new Vector3(hit.normal.x, 0f, hit.normal.z);
            else if (hit.normal.y > 0.5f) n = new Vector3(-aim.direction.x, 0f, -aim.direction.z);
            else { p.Message = "Aim at the rim or the floor's edge, not underneath it"; return p; }
            if (n.sqrMagnitude < 1e-4f) { p.Message = "Aim at the edge from the side the stairs will come down"; return p; }
            n.Normalize();
            float outYaw = Game.Snap.SnapAngle(Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg);
            n = Quaternion.Euler(0f, outYaw, 0f) * Vector3.forward;
            p.Yaw = Mathf.Repeat(outYaw + 180f, 360f);
            Vector3 right = Quaternion.Euler(0f, p.Yaw, 0f) * Vector3.right;

            // top of the floor and the face it ends in
            Vector3 face;
            Collider faceCol = null;
            if (faceHit)
            {
                face = hit.point;
                faceCol = hit.collider;
                Vector3 probe = hit.point - n * 0.02f;
                float from = hit.collider.bounds.max.y + 0.05f;
                p.TopY = Cast(new Vector3(probe.x, from, probe.z), Vector3.down, 0.6f, out var top) ? top.point.y : hit.collider.bounds.max.y;
            }
            else
            {
                p.TopY = hit.point.y;
                Vector3 e = hit.point;
                bool found = false;
                for (float d = 0f; d < 3f; d += Units.Inch)
                {
                    Vector3 q = hit.point + n * d;
                    if (!Cast(new Vector3(q.x, p.TopY + 0.05f, q.z), Vector3.down, 0.1f, out _)) { found = true; break; }
                    e = q;
                }
                if (!found) { p.Message = "Aim nearer the edge where the stairs go down"; return p; }
                Vector3 o = new Vector3(e.x, p.TopY - 0.06f, e.z) + n * 0.3f;
                if (Cast(o, -n, 0.4f, out var fh)) { face = fh.point; faceCol = fh.collider; }
                else face = new Vector3(e.x, p.TopY, e.z);
            }
            if (faceCol != null && IsFraming(faceCol)) p.Support = faceCol.transform;

            // snap the stair centerline along the edge
            float s = Vector3.Dot(face - Game.Snap.Origin, right);
            face += right * (Game.Snap.SnapLength(s) - s);

            // story pole: walk out from the face until the stair's run reaches the spot where the bottom step lands
            bool foot = false;
            float plumbRise = -1f, plumbY = 0f;
            float maxD = MaxSearchFt * Units.Foot;
            for (float d = Units.In(0.5f); d <= maxD; d += Units.Inch)
            {
                Vector3 q = face + n * d;
                if (!Cast(new Vector3(q.x, p.TopY - 0.02f, q.z), Vector3.down, 40f, out var g)) continue;
                float riseIn = Units.ToInches(p.TopY - g.point.y);
                if (plumbRise < 0f) { plumbRise = riseIn; plumbY = g.point.y; }
                if (riseIn < 0.5f) continue;
                var rr = StairMath.Calculate(riseIn);
                if (!rr.Valid) continue;
                if (Units.ToInches(d) + 0.5f >= rr.TotalRunIn) { p.FootY = g.point.y; foot = true; break; }
            }
            if (!foot)
            {
                if (plumbRise > 0f) p.FootY = plumbY;                       // never reached: too long for one flight, report the plumb rise
                else if (StairCalculator.HasLayout)
                {
                    p.FootY = p.TopY - StairCalculator.Layout.TotalRiseM; p.FromCalculator = true;  // no ground in reach: use the calculator's rise
                }
                else { p.Message = "No ground below that edge to measure to"; return p; }
            }

            p.RiseM = p.TopY - p.FootY;
            float rise = Units.ToInches(p.RiseM);
            p.R = StairMath.Calculate(rise);
            p.FaceGround = new Vector3(face.x, p.FootY, face.z);
            p.Bottom = StairBuilder.BottomFor(p.FaceGround, p.Yaw, p.RiseM);
            p.Measured = true;

            if (rise > 400f) { p.Message = "Rise of " + StairMath.FormatFtIn(rise) + " is too tall to measure for one stair"; return p; }
            if (!p.R.Valid || p.R.Risers < 2) { p.Message = "Rise of " + StairMath.FormatIn(rise) + " is a single step - no stringer needed"; return p; }

            if (Mode == KitMode.Stairs)
            {
                p.CanStart = true;   // a too-long stringer is refused by StairBuilder itself (LastError -> toast)
                if (p.NeedsLanding)
                    p.Message = "Stringer would need " + StairMath.FormatFtIn(p.R.StringerLengthIn) + " of 2x12 - longer than a 16' board: add a landing (RMB: landing mode)";
                return p;
            }

            // landing mode: split the risers into two flights with the same unit rise
            int nR = p.R.Risers;
            if (nR < 4) { p.Message = "Only " + nR + " risers - one flight, no landing needed"; return p; }
            float R = p.R.UnitRiseIn;
            p.UpperRisers = nR / 2;
            p.LowerRisers = nR - p.UpperRisers;
            p.UpperR = StairMath.Calculate(p.UpperRisers * R);
            p.LandingSizeM = Units.In(LandingIn);
            float upperRunIn = p.UpperR.Treads * p.UpperR.UnitRunIn;
            float landingTop = p.TopY - Units.In(p.UpperRisers * R);
            // landing's back edge 1 1/2" under the upper flight's bottom step so the stringer feet bear on the deck
            p.LandingCenter = p.FaceGround + n * (Units.In(upperRunIn - 1.5f) + p.LandingSizeM * 0.5f);
            float groundAtLanding = Cast(new Vector3(p.LandingCenter.x, landingTop - 0.02f, p.LandingCenter.z), Vector3.down, 40f, out var lg) ? lg.point.y : p.FootY;
            p.LandingCenter.y = groundAtLanding;
            p.LandingHeightM = landingTop - groundAtLanding;
            p.LowerR = StairMath.Calculate(Units.ToInches(p.LandingHeightM));
            p.UpperBottom = StairBuilder.BottomFor(new Vector3(face.x, landingTop, face.z), p.Yaw, Units.In(p.UpperRisers * R));
            Vector3 frontGround = p.LandingCenter + n * (p.LandingSizeM * 0.5f);
            p.LowerBottom = StairBuilder.BottomFor(frontGround, p.Yaw, p.LandingHeightM);
            if (Units.ToInches(p.LandingHeightM) < 0.71875f + 7.25f + 6f) { p.Message = "Landing would be too low for posts under 2x8 framing"; return p; }
            if (!p.UpperR.LengthOk || !p.LowerR.LengthOk) { p.Message = "Still too tall for two flights - build this landing, then measure again from it for another"; }
            p.CanStart = true;
            return p;
        }

        // ------------------------------------------------------------------ HUD

        public override string Hint
        {
            get
            {
                var p = Plan;
                string keys = Mode == KitMode.Stairs ? " · LMB start stair job · RMB landing mode · -/= width" : " · LMB lay out landing · RMB stairs mode · -/= landing size";
                if (p == null || !p.Measured) return "[" + ModeLabel + "] " + (p != null && p.Message != "" ? p.Message : "Aim at the edge of a floor, deck or landing") + keys;
                if (!p.R.Valid || p.R.Risers < 2) return "[" + ModeLabel + "] " + p.Message + keys;
                if (Mode == KitMode.Landing)
                    return "[" + ModeLabel + "] landing top " + StairMath.FormatFtIn(Units.ToInches(p.LandingHeightM)) + " · up " + p.UpperRisers + " risers, down "
                           + p.LowerRisers + " @ " + StairMath.FormatIn(p.R.UnitRiseIn) + keys;
                var r = p.R;
                return "[" + ModeLabel + "] " + r.Risers + " risers x " + StairMath.FormatIn(r.UnitRiseIn) + " · tread run " + StairMath.FormatIn(r.UnitRunIn)
                       + " · total run " + StairMath.FormatFtIn(r.TotalRunIn) + " · " + StockLine(r)
                       + (r.HandrailRequired ? " · handrail required (4+ risers)" : " · no handrail needed") + keys;
            }
        }

        string ModeLabel => Mode == KitMode.Stairs ? "Stairs " + StairMath.FormatIn(WidthIn) + " wide" : "Landing " + StairMath.FormatFtIn(LandingIn) + " sq";

        static string StockLine(StairResult r)
        {
            if (!r.LengthOk) return "stringer " + StairMath.FormatFtIn(r.StringerLengthIn) + " - over 16'!";
            var d = Catalog.Get(r.StockId);
            return "3 stringers " + StairMath.FormatFtIn(r.StringerLengthIn) + " from " + (d != null ? d.Name : r.StockId);
        }

        /// <summary>Lines shown under the crosshair: measured rise, Stair Calculator comparison and any warning.</summary>
        public string[] InfoLines()
        {
            var p = Plan;
            var lines = new System.Collections.Generic.List<string>();
            if (p == null || !p.Measured) return lines.ToArray();
            lines.Add("Total rise " + StairMath.FormatFtIn(Units.ToInches(p.RiseM)) + (p.FromCalculator ? " (from the Stair Calculator - no ground in reach)" : " (measured plumb at the bottom step)")
                      + (p.Support != null ? " · hangs from the " + p.Support.name : ""));
            if (StairCalculator.HasLayout && p.R.Valid)
            {
                var L = StairCalculator.Layout;
                bool same = Mathf.Abs(L.TotalRiseIn - p.R.TotalRiseIn) <= 0.25f;
                lines.Add("Stair Calculator: " + L.Risers + " @ " + StairMath.FormatIn(L.UnitRiseIn) + " (rise " + StairMath.FormatFtIn(L.TotalRiseIn) + ")"
                          + (same ? " - agrees with the tape" : " - differs from the tape by " + StairMath.FormatIn(Mathf.Abs(L.TotalRiseIn - p.R.TotalRiseIn)) + ": the job uses the measured rise")
                          + (Mathf.Abs(L.UnitRunIn - StairMath.DefaultRunIn) > 0.01f ? "; job treads " + StairMath.FormatIn(StairMath.DefaultRunIn) + " run" : ""));
            }
            if (!string.IsNullOrEmpty(p.Message)) lines.Add(p.Message);
            return lines.ToArray();
        }

        GUIStyle _style;
        public override void DrawGUI()
        {
            if (Game.UIOpen) return;
            var lines = InfoLines();
            if (lines.Length == 0) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            float y = Screen.height * 0.5f + 90f;
            foreach (var l in lines)
            {
                bool warn = l == Plan.Message;
                _style.normal.textColor = warn ? new Color(1f, 0.65f, 0.3f) : new Color(0.75f, 0.9f, 1f);
                GUI.Label(new Rect(Screen.width * 0.5f - 380f, y, 760f, 22f), l, _style);
                y += 22f;
            }
        }

        // ------------------------------------------------------------------ ghost (real-size massing of the flight / landing)

        public bool GhostVisible => _ghost != null && _ghost.activeSelf;

        void UpdateGhost()
        {
            var p = Plan;
            if (p == null || !p.Measured || !p.R.Valid || p.R.Risers < 2 || Game.UIOpen) { if (_ghost != null) _ghost.SetActive(false); return; }
            if (_ghost == null)
            {
                _ghost = new GameObject("StairKitGhost") { layer = 2 };
                _ghostMf = _ghost.AddComponent<MeshFilter>();
                _ghostMr = _ghost.AddComponent<MeshRenderer>();
                _ghostMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _ghostMr.receiveShadows = false;
            }
            _ghost.SetActive(true);
            bool ok = p.CanStart && string.IsNullOrEmpty(p.Message);
            _ghostMr.sharedMaterial = MaterialLibrary.Ghost(ok ? new Color(0.25f, 0.6f, 1f) : new Color(1f, 0.45f, 0.2f));
            string key = Mode + "|" + p.Bottom.ToString("F3") + "|" + p.Yaw + "|" + p.RiseM.ToString("F4") + "|" + WidthIn + "|" + LandingIn + "|" + p.LandingCenter.ToString("F3");
            if (key == _ghostKey && _ghostMf.sharedMesh != null) return;
            _ghostKey = key;
            var b = new MeshBuilder();
            var rot = Quaternion.Euler(0f, p.Yaw, 0f);
            float W = Units.In(WidthIn);
            if (Mode == KitMode.Stairs) Flight(b, p.Bottom, rot, p.R, W);
            else
            {
                float S = p.LandingSizeM, H = p.LandingHeightM;
                b.Transform = Matrix4x4.TRS(p.LandingCenter, rot, Vector3.one);
                b.AddBox(new Vector3(0f, H * 0.5f, 0f), new Vector3(S, H, S), 0);
                if (p.UpperR.Valid && p.UpperR.Risers >= 2) Flight(b, p.UpperBottom, rot, p.UpperR, W);
                if (p.LowerR.Valid && p.LowerR.Risers >= 2) Flight(b, p.LowerBottom, rot, p.LowerR, W);
            }
            if (_ghostMf.sharedMesh != null) Object.Destroy(_ghostMf.sharedMesh);
            _ghostMf.sharedMesh = b.Build("StairKitGhost");
        }

        /// <summary>Stepped massing of a flight: one block per tread up to its nosing height, plus a thin pad on the ground.</summary>
        static void Flight(MeshBuilder b, Vector3 bottom, Quaternion rot, StairResult r, float w)
        {
            b.Transform = Matrix4x4.TRS(bottom, rot, Vector3.one);
            float R = r.UnitRiseM, T = r.UnitRunM, back = Units.In(r.RiserThicknessIn);
            b.AddBox(new Vector3(0f, 0.003f, r.TotalRunM * 0.5f - back), new Vector3(w, 0.006f, r.TotalRunM), 0);
            for (int k = 1; k <= r.Treads; k++)
            {
                float z0 = (k - 1) * T - back, z1 = k * T - back;
                b.AddBox(new Vector3(0f, k * R * 0.5f, (z0 + z1) * 0.5f), new Vector3(w, k * R, z1 - z0), 0);
            }
            b.ResetTransform();
        }

        void DestroyGhost()
        {
            if (_ghost == null) return;
            if (_ghostMf != null && _ghostMf.sharedMesh != null) Object.Destroy(_ghostMf.sharedMesh);
            Object.Destroy(_ghost);
            _ghost = null; _ghostKey = "";
        }
    }
}
