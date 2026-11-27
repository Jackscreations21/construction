using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// A hired worker on site: procedural 1.75 m person (same build as the inspector) in a trade-colored shirt and hard hat.
    /// Walks with simple steering (no NavMesh): straight at the target, side-stepping obstacles found by a knee-high ray
    /// and keeping apart from the other workers. Fetches material from a pile, walks to the work, works with a visible tool
    /// animation for a time set by skill/trade/morale, then the <see cref="WorkUnit"/> calls the real game API.
    /// </summary>
    public class CrewNpc : MonoBehaviour
    {
        public static readonly List<CrewNpc> All = new List<CrewNpc>();
        public const float WalkSpeed = 1.35f, Reach = 1.1f, MaxFetch = 50f;
        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        public Worker W;
        /// <summary>Not one of your workers (a subcontractor's hand, see Scripts/Subs): CrewManager leaves it alone.</summary>
        public bool External;
        public CrewTask Task;
        public WorkUnit Unit;
        public enum Phase { Idle, ToPickup, ToWork, Working, Leaving }
        public Phase State = Phase.Idle;
        /// <summary>Seconds into the current piece of work (0 when not working).</summary>
        public float WorkElapsed => State == Phase.Working ? _workT : 0f;
        public string Doing => Unit != null ? Unit.Label : State == Phase.Leaving ? "Heading home" : "Waiting for work";

        Transform _hips, _legL, _legR, _armL, _armR, _toolRoot, _carryRoot;
        float _walkT, _workT, _workLeft, _stuck, _thinkAt, _sideSign = 1f;
        Vector3 _lastPos, _exit;
        string _toolShown, _carryShown;
        System.Random _rng;
        int _units;

        void OnEnable() => All.Add(this);
        void OnDisable() { All.Remove(this); if (Task != null && W != null) Task.Release(W.Uid); }

        // ------------------------------------------------------------------ brain

        void Update()
        {
            float dt = Time.deltaTime * Crew.SpeedMultiplier;
            if (W == null) { Destroy(gameObject); return; }
            if (State == Phase.Leaving)
            {
                if (MoveTo(_exit, 0.4f, dt)) Destroy(gameObject);
                return;
            }
            if (Unit == null)
            {
                Pose("idle", dt);
                if (Time.time >= _thinkAt) { _thinkAt = Time.time + 0.5f; Think(); }
                return;
            }
            switch (State)
            {
                case Phase.ToPickup:
                    if (MoveTo(Unit.PickupAt, Reach, dt)) { ShowCarry(Unit.CarryId); State = Phase.ToWork; }
                    break;
                case Phase.ToWork:
                    Vector3 stand = Unit.HasStand ? Unit.StandAt : Unit.Spot;
                    if (MoveTo(stand, Unit.HasStand ? 0.35f : Reach, dt)) { State = Phase.Working; _workLeft = WorkSeconds(); _workT = 0f; ShowCarry(null); ShowTool(Unit.Anim); Pose(Unit.Anim, 0f); }
                    break;
                case Phase.Working:
                    Face(Unit.Spot, dt);
                    ShowTool(Unit.Anim);
                    Pose(Unit.Anim, dt);
                    float before = _workT;
                    _workT += dt;
                    if (Mathf.Floor(before / 0.6f) != Mathf.Floor(_workT / 0.6f)) Sound(Unit.Anim);
                    _workLeft -= dt;
                    if (_workLeft <= 0f) FinishUnit();
                    break;
            }
        }

        float WorkSeconds() => Unit.Seconds / Mathf.Max(0.15f, Crew.Speed(W, Task != null ? Task.Trade : null));

        void Think()
        {
            if (Task != null && (Task.Done || !Crew.Tasks.Contains(Task) || Task.JobId != W.AssignedJobId)) { Task.Release(W.Uid); Task = null; }
            if (Task == null) Task = CrewManager.PickTask(W);
            if (Task == null) { Crew.Info(W).Doing = "Waiting for work"; return; }
            Task.ClaimedBy = W.Uid;
            Unit = Task.Next(W, transform.position);
            if (Unit == null) { Crew.Info(W).Doing = Task.Blocked != null ? "Waiting: " + Task.Blocked : "Waiting for work"; Task.Release(W.Uid); Task = null; return; }
            Crew.Info(W).Doing = Unit.Label;
            // fetch from the pile unless it's off this site (then it's already been brought over with the tools)
            Vector3 work = Unit.HasStand ? Unit.StandAt : Unit.Spot;
            bool fetch = !string.IsNullOrEmpty(Unit.CarryId) && Vector3.Distance(Flat(Unit.PickupAt), Flat(work)) < MaxFetch;
            State = fetch ? Phase.ToPickup : Phase.ToWork;
            if (!fetch) ShowCarry(Unit.CarryId);
            _stuck = 0f;
        }

        void FinishUnit()
        {
            var u = Unit; Unit = null; State = Phase.Idle; ShowTool(null);
            if (u == null || u.Do == null || Task == null) return;
            _rng = _rng ?? new System.Random((W.Uid ?? "x").GetHashCode());
            var ctx = new CrewCtx { W = W, Rng = _rng, HandPos = _armR != null ? _armR.TransformPoint(new Vector3(0, -0.62f, 0.05f)) : transform.position + Vector3.up };
            ctx.Sloppy = _rng.NextDouble() < Crew.MistakeChance(W, Task.Trade);
            string err = null;
            try { err = u.Do(ctx); }
            catch (System.Exception e) { Debug.LogException(e); err = e.Message; }
            if (!string.IsNullOrEmpty(err)) { Task.Blocked = err; Task.RetryAt = Time.time + 4f; Task.Release(W.Uid); Task = null; return; }
            Task.UnitsDone++; _units++;
            if (ctx.Sloppy) Task.Mistakes++;
            Crew.Info(W).UnitsDone++;
            _thinkAt = 0f;
        }

        public void SendHome(Vector3 exit)
        {
            if (Task != null) Task.Release(W.Uid);
            Task = null; Unit = null; State = Phase.Leaving; _exit = exit; ShowTool(null); ShowCarry(null);
        }

        // ------------------------------------------------------------------ steering

        bool MoveTo(Vector3 target, float stop, float dt)
        {
            Vector3 pos = transform.position;
            Vector3 to = target - pos; to.y = 0f;
            if (to.magnitude <= stop) { Pose(_carryShown != null ? "carry-idle" : "idle", dt); return true; }
            Vector3 dir = to.normalized;
            // obstacle ahead at knee height: side-step around it
            Vector3 knee = pos + Vector3.up * 0.45f;
            if (Physics.Raycast(knee, dir, out var hit, 0.7f, ~(1 << 2), QueryTriggerInteraction.Ignore) && hit.distance < to.magnitude)
            {
                Vector3 side = Vector3.Cross(Vector3.up, dir) * _sideSign;
                if (Physics.Raycast(knee, side, 0.6f, ~(1 << 2), QueryTriggerInteraction.Ignore)) { _sideSign = -_sideSign; side = -side; }
                dir = (dir * 0.3f + side).normalized;
            }
            // keep apart from the other workers
            foreach (var o in All)
            {
                if (o == this) continue;
                Vector3 d = pos - o.transform.position; d.y = 0;
                if (d.magnitude < 0.6f && d.magnitude > 1e-3f) dir += d.normalized * (0.6f - d.magnitude) * 2f;
            }
            dir.y = 0; dir.Normalize();
            Vector3 next = pos + dir * Mathf.Min(to.magnitude, WalkSpeed * dt);
            next.y = GroundY(next, pos.y);
            transform.position = next;
            Face(next + dir, dt);
            Pose(_carryShown != null ? "carry" : "walk", dt);
            // stuck for a while (boxed in by walls): step over / around it like a person climbing through framing
            if ((next - _lastPos).sqrMagnitude < (WalkSpeed * dt * 0.25f) * (WalkSpeed * dt * 0.25f)) _stuck += dt; else _stuck = 0f;
            _lastPos = next;
            if (_stuck > 5f) { var t = target; t.y = GroundY(t, t.y); transform.position = t; _stuck = 0f; return true; }
            return false;
        }

        static float GroundY(Vector3 p, float current)
        {
            // walk on the ground, a slab or a floor deck - but not on top of a wall or a stack
            float best = SiteGround.I != null && SiteGround.InPatch(p) ? SiteGround.I.HeightAt(p) : 0f;
            var hits = Physics.RaycastAll(new Vector3(p.x, current + 1.0f, p.z), Vector3.down, 6f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.point.y > current + 0.45f) continue;          // too high a step
                if (h.collider.GetComponentInParent<CrewNpc>() != null) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return best;
        }

        void Face(Vector3 p, float dt)
        {
            Vector3 d = p - transform.position; d.y = 0;
            if (d.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d.normalized), 1f - Mathf.Exp(-8f * dt));
        }

        // ------------------------------------------------------------------ animation

        void Pose(string anim, float dt)
        {
            _walkT += dt;
            float legs = 0f, armL = 0f, armR = 0f, armRz = -4f, armLz = 4f, bend = 0f, drop = 0f;
            float s = Mathf.Sin(_walkT * 6.5f);
            switch (anim)
            {
                case "walk": legs = s * 28f; armL = -s * 20f; armR = s * 20f; break;
                case "carry": legs = s * 24f; armL = -75f; armR = -75f; armLz = 10f; armRz = -10f; break;
                case "carry-idle": armL = -75f; armR = -75f; break;
                case "hammer": armR = -95f + Mathf.Sin(_walkT * 11f) * 45f; armL = -40f; bend = 12f; break;
                case "drill": armR = -85f + Mathf.Sin(_walkT * 30f) * 2f; armL = -70f; break;
                case "saw": armR = -60f + Mathf.Sin(_walkT * 9f) * 12f; armL = -50f; bend = 25f; break;
                case "level": armR = -90f; armL = -90f; break;
                case "roll": armR = -110f + Mathf.Sin(_walkT * 2.6f) * 45f; armL = -100f + Mathf.Sin(_walkT * 2.6f) * 40f; break;
                case "trowel": armR = -100f; armRz = -4f + Mathf.Sin(_walkT * 3f) * 35f; armL = -60f; break;
                case "sand": armR = -130f + Mathf.Sin(_walkT * 5f) * 25f; armL = -125f + Mathf.Sin(_walkT * 5f) * 25f; break;
                case "shovel":
                    float c = Mathf.Sin(_walkT * 3.2f);
                    bend = 25f + c * 15f; armR = -40f - c * 20f; armL = -55f - c * 20f; break;
                case "kneel": bend = 55f; drop = 0.32f; armR = -60f + Mathf.Sin(_walkT * 3f) * 10f; armL = -55f; legs = -70f; break;
                case "hose": armR = -55f + Mathf.Sin(_walkT * 1.5f) * 8f; armRz = -4f + Mathf.Sin(_walkT * 1.1f) * 20f; armL = -30f; break;
                case "place": armR = -70f; armL = -70f; bend = 20f + Mathf.Sin(_walkT * 2f) * 8f; break;
            }
            if (_legL != null)
            {
                if (anim == "kneel") { _legL.localRotation = Quaternion.Euler(-80f, 0, 0); _legR.localRotation = Quaternion.Euler(0, 0, 0); }
                else { _legL.localRotation = Quaternion.Euler(legs, 0, 0); _legR.localRotation = Quaternion.Euler(-legs, 0, 0); }
            }
            if (_hips != null) { _hips.localRotation = Quaternion.Euler(bend, 0, 0); _hips.localPosition = new Vector3(0, HipY - drop, 0); }
            if (_armL != null) _armL.localRotation = Quaternion.Euler(armL, 0, armLz);
            if (_armR != null) _armR.localRotation = Quaternion.Euler(armR, 0, armRz);
        }

        void Sound(string anim)
        {
            string n = null; float v = 0.5f;
            switch (anim)
            {
                case "hammer": n = W != null && W.Skill >= 3 ? "nailgun" : "hammer"; v = 0.55f; break;
                case "drill": n = "screw"; v = 0.4f; break;
                case "saw": n = "saw"; v = 0.5f; break;
                case "roll": case "sand": n = "roll"; v = 0.35f; break;
                case "trowel": case "shovel": n = "scrape"; v = 0.4f; break;
                case "place": n = "thud"; v = 0.3f; break;
            }
            if (n != null) Sfx.Play(n, transform.position + Vector3.up, v);
        }

        // ------------------------------------------------------------------ held tool / carried material

        void ShowTool(string anim)
        {
            if (anim == _toolShown) return;
            _toolShown = anim;
            if (_toolRoot != null) Destroy(_toolRoot.gameObject);
            _toolRoot = null;
            if (anim == null || _armR == null) return;
            var k = new LandModel();
            int wood = k.M(SurfaceKind.PaintedWhite, new Color(0.62f, 0.45f, 0.26f)), steel = k.M(SurfaceKind.PaintedSteel, new Color(0.5f, 0.5f, 0.52f));
            int body = k.M(TradeColor(anim)), black = k.M(SurfaceKind.MatteBlack);
            Vector3 hand = new Vector3(0, -0.62f, 0.02f);
            switch (anim)
            {
                case "hammer":
                    k.B.AddCylinder(hand + new Vector3(0, 0, 0.12f), Quaternion.Euler(90, 0, 0), 0.014f, 0.33f, 8, wood);
                    k.B.AddBox(hand + new Vector3(0, 0, 0.27f), new Vector3(0.03f, 0.13f, 0.03f), steel); break;
                case "drill":
                    k.B.AddBox(hand + new Vector3(0, 0.07f, 0.06f), new Vector3(0.06f, 0.07f, 0.2f), body);
                    k.B.AddBox(hand, new Vector3(0.04f, 0.13f, 0.05f), black);
                    k.B.AddCylinder(hand + new Vector3(0, 0.07f, 0.2f), Quaternion.Euler(90, 0, 0), 0.004f, 0.08f, 6, steel); break;
                case "saw":
                    k.B.AddBox(hand + new Vector3(0, -0.03f, 0.08f), new Vector3(0.14f, 0.12f, 0.24f), body);
                    k.B.AddCylinder(hand + new Vector3(0.07f, -0.06f, 0.08f), Quaternion.Euler(0, 0, 90), 0.092f, 0.003f, 16, steel); break;
                case "level":
                    k.B.AddBox(hand + new Vector3(0, 0, 0.3f), new Vector3(0.025f, 0.065f, 1.22f), k.M(new Color(0.85f, 0.65f, 0.1f))); break;
                case "roll": case "sand":
                    k.B.AddCylinder(hand + new Vector3(0, 0, 0.45f), Quaternion.Euler(90, 0, 0), 0.012f, 0.9f, 8, anim == "roll" ? steel : wood);
                    if (anim == "roll") k.B.AddCylinder(hand + new Vector3(0, 0.03f, 0.92f), Quaternion.Euler(0, 0, 90), 0.03f, 0.23f, 12, k.M(new Color(0.92f, 0.92f, 0.9f)));
                    else k.B.AddBox(hand + new Vector3(0, 0.02f, 0.92f), new Vector3(0.24f, 0.02f, 0.09f), k.M(new Color(0.75f, 0.65f, 0.45f)));
                    break;
                case "trowel":
                    k.B.AddCylinder(hand, 0.016f, 0.11f, 8, black);
                    k.B.AddBox(hand + new Vector3(0, 0.02f, 0.09f), new Vector3(0.25f, 0.003f, 0.08f), steel); break;
                case "shovel":
                    k.B.AddCylinder(hand + new Vector3(0, 0, 0.2f), Quaternion.Euler(80, 0, 0), 0.017f, 1.2f, 8, wood);
                    k.B.AddBox(hand + new Vector3(0, -0.09f, 0.78f), new Vector3(0.22f, 0.3f, 0.01f), Quaternion.Euler(80, 0, 0), steel); break;
                case "hose":
                    k.B.AddCylinder(hand + new Vector3(0, 0, 0.1f), Quaternion.Euler(90, 0, 0), 0.015f, 0.2f, 8, k.M(new Color(0.15f, 0.5f, 0.2f)));
                    k.B.AddCylinder(hand + new Vector3(0, 0, 0.22f), Quaternion.Euler(90, 0, 0), 0.012f, 0.06f, 8, k.M(new Color(0.8f, 0.6f, 0.15f))); break;
                default: return;
            }
            _toolRoot = k.Make(_armR, "HeldTool").transform;
            SetLayer(_toolRoot.gameObject);
        }

        static Color TradeColor(string anim) => anim == "drill" ? new Color(0.95f, 0.75f, 0.1f) : new Color(0.15f, 0.3f, 0.7f);

        void ShowCarry(string id)
        {
            if (id == _carryShown) return;
            _carryShown = id;
            if (_carryRoot != null) Destroy(_carryRoot.gameObject);
            _carryRoot = null;
            var def = string.IsNullOrEmpty(id) ? null : Catalog.Get(id);
            if (def == null || _hips == null) return;
            var go = new GameObject("Carried_" + id);
            go.transform.SetParent(_hips, false);
            Vector3 size = def.Size;
            if (def.Category == PieceCategory.Lumber)
            {   // on the right shoulder, long axis forward
                go.transform.localPosition = new Vector3(0.17f, 0.62f, 0.15f);
                go.transform.localRotation = Quaternion.Euler(-4f, 0, 0);
            }
            else if (def.Category == PieceCategory.Sheet)
            {   // a sheet on edge at the side, held with both hands (long side horizontal)
                go.transform.localPosition = new Vector3(0.26f, 0.15f, 0.25f);
                go.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
            else
            {   // boxes, buckets, rolls in front at the chest
                go.transform.localPosition = new Vector3(0, 0.2f, 0.32f);
                if (size.magnitude > 1.2f) size = size.normalized * 1.2f;
            }
            try { ProductModels.Build(def, size, go.transform); } catch (System.Exception e) { Debug.LogException(e); }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            SetLayer(go);
            _carryRoot = go.transform;
        }

        static void SetLayer(GameObject go) { foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2; }

        // ------------------------------------------------------------------ model (inspector build, trade shirt, hard hat, tool belt)

        const float HipY = 0.95f;

        public static CrewNpc Spawn(Worker w, Vector3 pos)
        {
            var root = new GameObject("Worker_" + w.Name);
            root.transform.position = pos;
            var npc = root.AddComponent<CrewNpc>();
            npc.W = w;
            npc._lastPos = pos;
            var trade = CrewTrades.Get(w.Trade);
            int seed = (w.Uid ?? w.Name ?? "x").GetHashCode();
            var r = new System.Random(seed);
            Color[] skins = { new Color(0.93f, 0.76f, 0.63f), new Color(0.80f, 0.60f, 0.46f), new Color(0.62f, 0.44f, 0.31f), new Color(0.45f, 0.31f, 0.21f), new Color(0.32f, 0.22f, 0.15f) };
            Color skin = skins[r.Next(skins.Length)];
            Color jeans = r.Next(3) == 0 ? new Color(0.36f, 0.30f, 0.22f) : new Color(0.17f, 0.24f, 0.38f);   // canvas work pants or denim
            Color shirt = trade.Shirt, boot = new Color(0.40f, 0.28f, 0.15f);
            Color hat = w.Skill >= 4 ? Color.white : trade.Id == "electrician" ? new Color(0.2f, 0.4f, 0.85f) : new Color(1f, 0.82f, 0.1f);

            // legs hang from the root, the upper body pivots at the hips (bending over, kneeling)
            npc._legL = Limb(root.transform, "LegL", new Vector3(-0.09f, HipY - 0.01f, 0), 0.075f, 0.055f, 0.86f, jeans, boot, true);
            npc._legR = Limb(root.transform, "LegR", new Vector3(0.09f, HipY - 0.01f, 0), 0.075f, 0.055f, 0.86f, jeans, boot, true);
            npc._hips = new GameObject("Hips").transform;
            npc._hips.SetParent(root.transform, false);
            npc._hips.localPosition = new Vector3(0, HipY, 0);

            const float o = -HipY;
            var k = new LandModel();
            int mSkin = k.M(skin), mJeans = k.M(jeans), mShirt = k.M(shirt), mHat = k.M(SurfaceKind.PaintedWhite, hat), mDark = k.M(SurfaceKind.MatteBlack);
            int mLeather = k.M(new Color(0.45f, 0.30f, 0.15f)), mVest = k.M(new Color(0.80f, 1.0f, 0.12f));
            k.B.AddBevelBox(new Vector3(0, 0.98f + o, 0), new Vector3(0.34f, 0.17f, 0.20f), Quaternion.Euler(90, 0, 0), 0.04f, mJeans);
            k.B.Transform = Matrix4x4.TRS(new Vector3(0, 1.25f + o, 0), Quaternion.identity, new Vector3(1f, 1f, 0.70f));
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] {
                new Vector2(0.001f, -0.20f), new Vector2(0.165f, -0.20f), new Vector2(0.175f, -0.05f), new Vector2(0.19f, 0.12f),
                new Vector2(0.20f, 0.19f), new Vector2(0.12f, 0.25f), new Vector2(0.06f, 0.26f), new Vector2(0.001f, 0.26f) }, 14, mShirt);
            if (trade.HiVis)
                k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0.185f, -0.17f), new Vector2(0.19f, -0.05f), new Vector2(0.203f, 0.12f), new Vector2(0.198f, 0.19f) }, 14, mVest);
            k.B.ResetTransform();
            // leather tool belt with pouches
            k.B.AddFrustum(new Vector3(0, 1.06f + o, 0), Quaternion.identity, 0.185f, 0.185f, 0.05f, 14, mLeather);
            k.B.AddBox(new Vector3(-0.17f, 0.98f + o, 0.06f), new Vector3(0.06f, 0.14f, 0.13f), mLeather);
            k.B.AddBox(new Vector3(0.17f, 0.98f + o, 0.06f), new Vector3(0.06f, 0.14f, 0.13f), mLeather);
            k.B.AddCylinder(new Vector3(0, 1.54f + o, 0), 0.055f, 0.08f, 10, mSkin);
            k.B.Transform = Matrix4x4.TRS(new Vector3(0, 1.66f + o, 0.005f), Quaternion.identity, new Vector3(0.88f, 1f, 1f));
            k.B.AddLathe(Vector3.zero, Quaternion.identity, LandModel.BlobProfile(0.098f, 0.24f, 10), 16, mSkin);
            k.B.ResetTransform();
            k.B.AddLathe(new Vector3(0, 1.725f + o, 0.005f), Quaternion.identity, new[] {
                new Vector2(0.15f, 0f), new Vector2(0.15f, 0.008f), new Vector2(0.112f, 0.012f), new Vector2(0.11f, 0.05f),
                new Vector2(0.09f, 0.09f), new Vector2(0.05f, 0.115f), new Vector2(0.001f, 0.12f) }, 18, mHat);
            k.B.AddBox(new Vector3(0, 1.84f + o, 0.005f), new Vector3(0.025f, 0.012f, 0.2f), mHat);
            k.B.AddBox(new Vector3(0, 1.68f + o, 0.088f), new Vector3(0.13f, 0.03f, 0.012f), mDark);   // safety glasses
            k.Make(npc._hips, "Body");

            npc._armL = Limb(npc._hips, "ArmL", new Vector3(-0.22f, 1.43f + o, 0), 0.05f, 0.04f, 0.6f, shirt, skin, false);
            npc._armR = Limb(npc._hips, "ArmR", new Vector3(0.22f, 1.43f + o, 0), 0.05f, 0.04f, 0.6f, shirt, skin, false);
            SetLayer(root);
            return npc;
        }

        static Transform Limb(Transform parent, string name, Vector3 pivot, float r0, float r1, float len, Color cloth, Color end, bool leg)
        {
            var pv = new GameObject(name).transform;
            pv.SetParent(parent, false);
            pv.localPosition = pivot;
            var k = new LandModel();
            int mc = k.M(cloth), me = k.M(end);
            float clothLen = len - 0.08f;
            k.B.AddFrustum(new Vector3(0, -clothLen / 2, 0), Quaternion.identity, r1, r0, clothLen, 10, mc);
            if (leg) k.B.AddBevelBox(new Vector3(0, -len + 0.045f, 0.035f), new Vector3(0.11f, 0.09f, 0.28f), Quaternion.identity, 0.02f, me);
            else k.B.AddLathe(new Vector3(0, -len + 0.04f, 0), Quaternion.identity, LandModel.BlobProfile(0.04f, 0.11f), 10, me);
            k.Make(pv, name + "Mesh");
            return pv;
        }
    }
}
