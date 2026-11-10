using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>The building inspector: arrives, walks the lot (stopping at each spot a rule flags plus the building corners),
    /// writes on the clipboard, then signs off or leaves correction notices (<see cref="Inspections.Inspect"/>).</summary>
    public class InspectorNpc : MonoBehaviour
    {
        public static InspectorNpc Active { get; private set; }
        public static bool Busy => Active != null && !Active._done;
        /// <summary>Walking/look speed multiplier (tests speed it up).</summary>
        public static float SpeedMultiplier = 1f;

        public InspectionPhase Phase;
        public string Status = "";
        public InspectionResult Result;
        public const float WalkSpeed = 1.3f;

        readonly List<Vector3> _route = new List<Vector3>();
        int _leg; float _pause; bool _done; float _leaveAt; Vector3 _exit;
        Transform _legL, _legR, _armL, _armR;
        float _phaseT;

        /// <summary>Sends the inspector to the site for a phase. Returns null if one is already on site.</summary>
        public static InspectorNpc Dispatch(InspectionPhase phase)
        {
            if (Busy) return null;
            if (Active != null) Destroy(Active.gameObject);

            Vector3 center = Site.BuildingBounds(out var b) ? b.center : (Game.Player != null ? Game.Player.transform.position : Site.LotCenter);
            center.y = 0;
            Vector3 start = center + new Vector3(-14f, 0, -14f);
            if (Site.Lot.HasValue) { var l = Site.Lot.Value; start = new Vector3(l.center.x, 0, l.yMin - 2f); }

            var npc = Build(start);
            npc.Phase = phase;
            npc._exit = start;
            // Route: every issue spot the rules currently flag (that's what he'll look at), then the building corners.
            var pre = Inspections.Evaluate(phase);
            foreach (var i in pre.Issues) if (i.HasPosition && npc._route.Count < 12) npc._route.Add(Flat(i.Position));
            if (Site.BuildingBounds(out b))
            {
                float pad = 1.2f;
                npc._route.Add(new Vector3(b.min.x - pad, 0, b.min.z - pad));
                npc._route.Add(new Vector3(b.max.x + pad, 0, b.min.z - pad));
                npc._route.Add(new Vector3(b.max.x + pad, 0, b.max.z + pad));
                npc._route.Add(new Vector3(b.min.x - pad, 0, b.max.z + pad));
            }
            if (npc._route.Count == 0) npc._route.Add(center + new Vector3(1.5f, 0, 1.5f));
            npc.Status = $"Inspector on the way for {Inspections.Label(phase)}";
            Active = npc;
            Sfx.Play("truck", start, 0.5f);
            return npc;
        }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);

        void OnDestroy() { if (Active == this) Active = null; }

        /// <summary>Skips the walk and signs right now (used by tests / fast-forward).</summary>
        public void FinishNow()
        {
            if (_done) return;
            _route.Clear(); _leg = 0; Complete();
        }

        void Complete()
        {
            _done = true;
            Result = Inspections.Inspect(Phase);
            Status = Result.Summary;
            _leaveAt = Time.time + 4f / Mathf.Max(0.1f, SpeedMultiplier);
        }

        void Update()
        {
            float dt = Time.deltaTime * SpeedMultiplier;
            Vector3 target;
            bool walking = false;
            if (!_done)
            {
                if (_leg >= _route.Count) { Complete(); return; }
                target = _route[_leg];
                Status = $"Inspector checking {Inspections.Label(Phase)} ({_leg + 1}/{_route.Count})";
            }
            else
            {
                if (Time.time < _leaveAt) { Animate(false, dt); return; }
                target = _exit;
            }

            Vector3 pos = transform.position, flat = new Vector3(pos.x, 0, pos.z);
            Vector3 to = target - flat; to.y = 0;
            float stopDist = _done ? 0.3f : 0.9f;
            if (_pause > 0f) { _pause -= dt; Animate(false, dt); return; }
            if (to.magnitude > stopDist)
            {
                walking = true;
                Vector3 step = to.normalized * Mathf.Min(to.magnitude, WalkSpeed * dt);
                flat += step;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), 1f - Mathf.Exp(-8f * dt));
            }
            else if (!_done) { _leg++; _pause = 2.5f; }
            else { Destroy(gameObject); return; }

            float y = 0f;
            if (Physics.Raycast(new Vector3(flat.x, 50f, flat.z), Vector3.down, out var hit, 100f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                y = hit.collider.GetComponentInParent<Piece>() != null && hit.point.y > 0.5f ? 0f : hit.point.y;
            transform.position = new Vector3(flat.x, y, flat.z);
            Animate(walking, dt);
        }

        void Animate(bool walking, float dt)
        {
            if (walking) _phaseT += dt * 6.5f;
            float s = walking ? Mathf.Sin(_phaseT) * 28f : Mathf.MoveTowards(0, 0, 1);
            if (_legL != null) { _legL.localRotation = Quaternion.Euler(s, 0, 0); _legR.localRotation = Quaternion.Euler(-s, 0, 0); }
            if (_armL != null) _armL.localRotation = Quaternion.Euler(-s * 0.7f, 0, 4);
            // right arm holds the clipboard up while inspecting
            if (_armR != null) _armR.localRotation = walking ? Quaternion.Euler(-35f, 0, -4) : Quaternion.Euler(-70f, 15f, -4);
        }

        // ------------------------------------------------------------------ model (1.78 m adult in hi-vis vest and hard hat)

        static InspectorNpc Build(Vector3 pos)
        {
            var root = new GameObject("Inspector");
            root.transform.position = pos;
            var npc = root.AddComponent<InspectorNpc>();

            Color skin = new Color(0.78f, 0.58f, 0.45f), jeans = new Color(0.17f, 0.24f, 0.38f), shirt = new Color(0.82f, 0.84f, 0.86f);
            Color vest = new Color(0.80f, 1.0f, 0.12f), stripe = new Color(0.85f, 0.85f, 0.82f), boot = new Color(0.32f, 0.22f, 0.13f);

            // body (torso, hips, head, hat)
            var k = new LandModel();
            int mSkin = k.M(skin), mJeans = k.M(jeans), mShirt = k.M(shirt), mVest = k.M(vest), mStripe = k.M(stripe), mHat = k.M(SurfaceKind.PaintedWhite), mDark = k.M(SurfaceKind.MatteBlack);
            k.B.AddBevelBox(new Vector3(0, 0.98f, 0), new Vector3(0.34f, 0.17f, 0.20f), Quaternion.Euler(90, 0, 0), 0.04f, mJeans);
            k.B.AddFrustum(new Vector3(0, 1.08f, 0), Quaternion.identity, 0.03f, 0.03f, 0.04f, 10, mDark); // belt hint
            // torso: chest wider than deep (elliptical), the vest shell sits just outside it all the way round
            k.B.Transform = Matrix4x4.TRS(new Vector3(0, 1.25f, 0), Quaternion.identity, new Vector3(1f, 1f, 0.70f));
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] {
                new Vector2(0.001f, -0.20f), new Vector2(0.165f, -0.20f), new Vector2(0.175f, -0.05f), new Vector2(0.19f, 0.12f),
                new Vector2(0.20f, 0.19f), new Vector2(0.12f, 0.25f), new Vector2(0.06f, 0.26f), new Vector2(0.001f, 0.26f) }, 14, mShirt);
            k.B.ResetTransform();
            // vest shell over the torso + reflective bands
            k.B.Transform = Matrix4x4.TRS(new Vector3(0, 1.25f, 0), Quaternion.identity, new Vector3(1.07f, 1f, 0.78f));
            k.B.AddLathe(Vector3.zero, Quaternion.identity, new[] {
                new Vector2(0.175f, -0.17f), new Vector2(0.18f, -0.05f), new Vector2(0.195f, 0.12f), new Vector2(0.19f, 0.19f) }, 14, mVest);
            k.B.AddLathe(new Vector3(0, -0.12f, 0), Quaternion.identity, new[] { new Vector2(0.178f, -0.02f), new Vector2(0.18f, 0.02f) }, 14, mStripe);
            k.B.AddLathe(new Vector3(0, 0.02f, 0), Quaternion.identity, new[] { new Vector2(0.186f, -0.02f), new Vector2(0.19f, 0.02f) }, 14, mStripe);
            k.B.ResetTransform();
            k.B.AddCylinder(new Vector3(0, 1.54f, 0), 0.055f, 0.08f, 10, mSkin);
            k.B.Transform = Matrix4x4.TRS(new Vector3(0, 1.66f, 0.005f), Quaternion.identity, new Vector3(0.88f, 1f, 1f));
            k.B.AddLathe(Vector3.zero, Quaternion.identity, LandModel.BlobProfile(0.098f, 0.24f, 10), 16, mSkin);
            k.B.ResetTransform();
            // hard hat: dome + brim + ridge
            k.B.AddLathe(new Vector3(0, 1.725f, 0.005f), Quaternion.identity, new[] {
                new Vector2(0.15f, 0f), new Vector2(0.15f, 0.008f), new Vector2(0.112f, 0.012f), new Vector2(0.11f, 0.05f),
                new Vector2(0.09f, 0.09f), new Vector2(0.05f, 0.115f), new Vector2(0.001f, 0.12f) }, 18, mHat);
            k.B.AddBox(new Vector3(0, 1.84f, 0.005f), new Vector3(0.025f, 0.012f, 0.2f), mHat);
            // eyes / sunglasses band
            k.B.AddBox(new Vector3(0, 1.68f, 0.088f), new Vector3(0.13f, 0.03f, 0.012f), mDark);
            k.Make(root.transform, "Body");

            npc._legL = Limb(root.transform, "LegL", new Vector3(-0.09f, 0.94f, 0), 0.075f, 0.055f, 0.86f, jeans, boot, true);
            npc._legR = Limb(root.transform, "LegR", new Vector3(0.09f, 0.94f, 0), 0.075f, 0.055f, 0.86f, jeans, boot, true);
            npc._armL = Limb(root.transform, "ArmL", new Vector3(-0.22f, 1.43f, 0), 0.05f, 0.04f, 0.6f, shirt, skin, false);
            npc._armR = Limb(root.transform, "ArmR", new Vector3(0.22f, 1.43f, 0), 0.05f, 0.04f, 0.6f, shirt, skin, false);

            // clipboard in the right hand
            var c = new LandModel();
            int brown = c.M(new Color(0.45f, 0.30f, 0.16f)), paper = c.M(new Color(0.95f, 0.95f, 0.92f)), clip = c.M(SurfaceKind.Nickel);
            c.B.AddBox(new Vector3(0, -0.62f, 0.12f), new Vector3(0.23f, 0.004f, 0.31f), Quaternion.Euler(10, 0, 0), brown);
            c.B.AddBox(new Vector3(0, -0.617f, 0.12f), new Vector3(0.21f, 0.002f, 0.27f), Quaternion.Euler(10, 0, 0), paper);
            c.B.AddBox(new Vector3(0, -0.612f, 0.255f), new Vector3(0.08f, 0.012f, 0.03f), Quaternion.Euler(10, 0, 0), clip);
            c.Make(npc._armR, "Clipboard");
            return npc;
        }

        static Transform Limb(Transform parent, string name, Vector3 pivot, float r0, float r1, float len, Color cloth, Color end, bool leg)
        {
            var pv = new GameObject(name).transform;
            pv.SetParent(parent, false);
            pv.localPosition = pivot;
            var k = new LandModel();
            int mc = k.M(cloth), me = k.M(end);
            float clothLen = leg ? len - 0.08f : len - 0.08f;
            k.B.AddFrustum(new Vector3(0, -clothLen / 2, 0), Quaternion.identity, r1, r0, clothLen, 10, mc);
            if (leg) k.B.AddBevelBox(new Vector3(0, -len + 0.045f, 0.035f), new Vector3(0.11f, 0.09f, 0.28f), Quaternion.identity, 0.02f, me);
            else k.B.AddLathe(new Vector3(0, -len + 0.04f, 0), Quaternion.identity, LandModel.BlobProfile(0.04f, 0.11f), 10, me);
            k.Make(pv, name + "Mesh");
            return pv;
        }
    }
}
