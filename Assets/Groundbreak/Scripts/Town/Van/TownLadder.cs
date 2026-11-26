using System;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// The 24' aluminium extension ladder (two 12' sections, 16" wide, rungs 12" o.c.) that lives on the van's roof rack.
    /// One ladder in the world: on the rack, carried, or standing against a wall at the real 4:1 (~75.5 deg) angle with 3' above the eave.
    /// The player climbs a standing ladder with W / S; at the top they step onto the roof.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class TownLadder : MonoBehaviour
    {
        public enum LadderState { OnRack, Carried, Placed }

        public const float SectionLength = 12f * Units.Foot;
        public const float ClosedLength = SectionLength + 0.10f;
        public const float MaxLength = 21f * Units.Foot;          // 24' ladder: 3' overlap when fully extended
        public const float Width = 16f * Units.Inch, RungSpacing = 12f * Units.Inch;
        public const float LeanDegrees = 75.96f;                  // 4:1
        public const float AboveEave = 3f * Units.Foot;
        public const float Reach = 4f, ClimbSpeed = 1.1f;
        const float StandOff = 0.38f;                              // climber's feet this far out from the rails

        static readonly float Sin = Mathf.Sin(LeanDegrees * Mathf.Deg2Rad), Cos = Mathf.Cos(LeanDegrees * Mathf.Deg2Rad);
        /// <summary>Highest eave the ladder can serve (3' above it at full extension).</summary>
        public static float MaxTopHeight => (MaxLength - AboveEave) * Sin;

        public static LadderState State { get; private set; } = LadderState.OnRack;
        /// <summary>The standing ladder, or null.</summary>
        public static TownLadder Placed { get; private set; }
        public static bool Carried => State == LadderState.Carried;
        /// <summary>Raised when the ladder moves between the rack, the player and the ground.</summary>
        public static event Action Changed;

        static GameObject _carry;

        // ---- a standing ladder
        public Vector3 Foot { get; private set; }
        /// <summary>Horizontal unit vector from the wall toward the climber.</summary>
        public Vector3 Away { get; private set; }
        /// <summary>Height of the eave / wall top it leans on, above the foot.</summary>
        public float TopHeight { get; private set; }
        public float Length { get; private set; }
        public bool Climbing { get; private set; }
        /// <summary>Non-zero replaces the W/S keys (tests, scripted climbs): +1 up, -1 down.</summary>
        public float ClimbOverride;
        float _s;

        Vector3 Up => -Away * Cos + Vector3.up * Sin;
        float STop => TopHeight / Sin;
        /// <summary>Where the rails touch the eave.</summary>
        public Vector3 TopContact => Foot + Up * STop;
        /// <summary>Height of the climber's feet above the ladder foot.</summary>
        public float ClimbHeight => Climbing ? _s * Sin : 0f;

        // ------------------------------------------------------------------ placing / taking

        /// <summary>Stands the ladder with its feet at <paramref name="foot"/>, leaning toward -awayFromWall onto an eave
        /// <paramref name="topHeight"/> above the foot. Replaces a ladder standing elsewhere.</summary>
        public static TownLadder Place(Vector3 foot, Vector3 awayFromWall, float topHeight)
        {
            Vector3 a = new Vector3(awayFromWall.x, 0f, awayFromWall.z);
            if (a.sqrMagnitude < 1e-6f) a = Vector3.forward;
            a.Normalize();
            DestroyPlaced(); DestroyCarry();
            topHeight = Mathf.Clamp(topHeight, 0.5f, MaxTopHeight);
            float len = Mathf.Clamp(topHeight / Sin + AboveEave, ClosedLength, MaxLength);

            var go = new GameObject("Extension ladder") { layer = 2 };
            Vector3 up = -a * Cos + Vector3.up * Sin;
            Vector3 face = a * Sin + Vector3.up * Cos;
            go.transform.SetPositionAndRotation(foot, Quaternion.LookRotation(face, up));
            var m = new LandModel();
            AddLadder(m.B, m.M(SurfaceKind.BrushedSteel), m.M(SurfaceKind.DarkPlastic), len);
            m.Make(go.transform, "Mesh").layer = 2;

            var lad = go.AddComponent<TownLadder>();
            lad.Foot = foot; lad.Away = a; lad.TopHeight = topHeight; lad.Length = len;
            Placed = lad; State = LadderState.Placed;
            Sfx.Play("knock", foot, 0.6f);
            Changed?.Invoke();
            return lad;
        }

        /// <summary>Works out where the ladder goes from an aimed point (wall, eave or the ground in front of one) and stands it there.</summary>
        public static bool TryPlaceAt(Vector3 point, Vector3 normal, Vector3 viewForward, out string why)
        {
            why = null;
            Vector3 f = Mathf.Abs(normal.y) < 0.5f ? -new Vector3(normal.x, 0f, normal.z) : new Vector3(viewForward.x, 0f, viewForward.z);
            if (f.sqrMagnitude < 1e-4f) { why = "Aim at a wall or eave to lean the ladder on"; return false; }
            f.Normalize();
            int mask = Physics.DefaultRaycastLayers;
            float groundY = Game.Player != null ? Game.Player.transform.position.y : point.y;
            Vector3 back = point - f * 1.0f;
            if (Physics.Raycast(new Vector3(back.x, groundY + 1.0f, back.z), Vector3.down, out var gh, 3f, mask, QueryTriggerInteraction.Ignore)) groundY = gh.point.y;

            // walk toward the wall looking straight down from above ladder height: the first thing well above the ground is the eave edge
            float startY = groundY + MaxTopHeight + 0.6f;
            bool found = false; float edgeS = 0f, topY = 0f;
            for (float s = -1.6f; s <= 1.6f; s += 0.05f)
            {
                Vector3 q = point + f * s;
                if (!Physics.Raycast(new Vector3(q.x, startY, q.z), Vector3.down, out var h, MaxTopHeight + 0.6f - 1.2f, mask, QueryTriggerInteraction.Ignore)) continue;
                found = true; edgeS = s; topY = h.point.y - groundY;
                break;
            }
            if (!found) { why = "Nothing here to lean the ladder on - aim at a wall or eave (up to 17' high)"; return false; }
            if (topY > MaxTopHeight) { why = "Too high for a 24' ladder"; return false; }
            Vector3 edge = point + f * edgeS;
            Vector3 foot = edge - f * (topY / 4f); foot.y = groundY;
            if (Physics.Raycast(new Vector3(foot.x, groundY + 1f, foot.z), Vector3.down, out var fh, 3f, mask, QueryTriggerInteraction.Ignore))
            {
                topY -= fh.point.y - groundY; foot.y = fh.point.y;
            }
            if (topY < 1.2f) { why = "Too low to need a ladder"; return false; }
            Place(foot, -f, topY);
            return true;
        }

        /// <summary>Pick the ladder up (off the rack or from where it stands).</summary>
        public static bool Take()
        {
            if (State == LadderState.Carried) return false;
            if (Placed != null && Placed.Climbing) return false;
            DestroyPlaced();
            State = LadderState.Carried;
            BuildCarry();
            Sfx.Play("knock", Game.Player != null ? Game.Player.transform.position : Vector3.zero, 0.5f);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Put the ladder back on the van's roof rack.</summary>
        public static void Stow()
        {
            bool was = State != LadderState.OnRack;
            DestroyPlaced(); DestroyCarry();
            State = LadderState.OnRack;
            if (was) Changed?.Invoke();
        }

        /// <summary>Back on the rack, nothing standing (new game / town destroyed / load).</summary>
        public static void ResetState() => Stow();

        static void DestroyPlaced()
        {
            if (Placed != null) { Placed.gameObject.SetActive(false); Destroy(Placed.gameObject); }
            Placed = null;
        }

        static void DestroyCarry()
        {
            if (_carry != null)
            {
                var mf = _carry.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(_carry);
            }
            _carry = null;
        }

        static void BuildCarry()
        {
            DestroyCarry();
            var p = Game.Player; if (p == null) return;
            var m = new LandModel();
            AddLadder(m.B, m.M(SurfaceKind.BrushedSteel), m.M(SurfaceKind.DarkPlastic), ClosedLength);
            _carry = m.Make(p.transform, "CarriedLadder");
            _carry.layer = 2;
            // on edge on the right shoulder, pointing ahead
            _carry.transform.localPosition = new Vector3(0.42f, 1.32f, -ClosedLength * 0.45f);
            _carry.transform.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.forward);
        }

        void OnDestroy()
        {
            var mf = GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            if (Placed == this) Placed = null;
        }

        // ------------------------------------------------------------------ model

        /// <summary>Ladder mesh: feet at the origin, rails along +Y, climbing face toward +Z. length above the closed length slides the fly section up.</summary>
        public static void AddLadder(MeshBuilder b, int alu, int rubber, float length)
        {
            length = Mathf.Clamp(length, ClosedLength, MaxLength);
            float baseHalf = Width * 0.5f - 0.014f;
            Section(b, alu, 0f, SectionLength, 0f, baseHalf);
            Section(b, alu, length - SectionLength, SectionLength, 0.078f, baseHalf - 0.03f);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                b.AddBox(new Vector3(sx * baseHalf, 0.015f, 0f), new Vector3(0.05f, 0.03f, 0.11f), rubber);                    // swivel feet
                b.AddBox(new Vector3(sx * (baseHalf - 0.03f), length - 0.02f, 0.078f), new Vector3(0.04f, 0.04f, 0.085f), rubber); // rail end caps
            }
        }

        static void Section(MeshBuilder b, int alu, float y0, float len, float zc, float halfW)
        {
            for (int sx = -1; sx <= 1; sx += 2)
                b.AddBox(new Vector3(sx * halfW, y0 + len * 0.5f, zc), new Vector3(0.028f, len, 0.075f), alu);
            for (float y = y0 + 0.28f; y < y0 + len - 0.1f; y += RungSpacing)
                b.AddCylinder(new Vector3(0f, y, zc), Quaternion.Euler(0f, 0f, 90f), 0.016f, halfW * 2f, 8, alu);
        }

        /// <summary>Does the view ray hit this ladder within maxDist?</summary>
        public bool Aimed(Ray ray, float maxDist, out float dist)
            => VanSystems.RayBox(ray, transform, new Vector3(0f, Length * 0.5f, 0.04f), new Vector3(0.6f, Length, 0.3f), maxDist, out dist, out _);

        // ------------------------------------------------------------------ climbing

        /// <summary>Put the player on the ladder (at the bottom, or at the eave when coming down off the roof).</summary>
        public void Mount(bool fromTop = false)
        {
            if (Game.Player == null) return;
            Climbing = true; _s = fromTop ? STop : 0f;
        }

        public void Dismount() { Climbing = false; }

        bool NearBottom(Vector3 pos)
        {
            Vector3 d = pos - (Foot + Away * StandOff);
            return Mathf.Abs(d.y) < 0.6f && new Vector2(d.x, d.z).magnitude < 0.7f;
        }

        bool NearTop(Vector3 pos)
        {
            Vector3 d = pos - TopContact;
            return d.y > -0.5f && d.y < 1.5f && new Vector2(d.x, d.z).magnitude < 1.1f;
        }

        void LateUpdate()
        {
            var p = Game.Player;
            if (p == null) { Climbing = false; return; }
            if (WorkVan.I != null && WorkVan.I.Driving) { Climbing = false; return; }
            bool keys = !Game.UIOpen && ClimbOverride == 0f;
            float input = ClimbOverride;
            if (keys)
            {
                if (GInput.GetKey(KeyCode.W)) input += 1f;
                if (GInput.GetKey(KeyCode.S)) input -= 1f;
            }
            Vector3 pos = p.transform.position;
            if (!Climbing)
            {
                if (ClimbOverride != 0f) return;                 // scripted climbs call Mount()
                float facing = Vector3.Dot(p.transform.forward, -Away);
                if (input > 0f && facing > 0.3f && NearBottom(pos)) { Climbing = true; _s = Mathf.Clamp((pos.y - Foot.y) / Sin, 0f, STop); }
                else if (input < 0f && facing > 0f && NearTop(pos)) { Climbing = true; _s = STop; }
                if (!Climbing) return;
            }
            if (keys && GInput.GetKeyDown(KeyCode.Space)) { Climbing = false; return; }   // let go

            _s += input * ClimbSpeed * Time.deltaTime;
            if (_s <= 0f && input < 0f)
            {
                Climbing = false;
                SetPlayer(p, Foot + Away * (StandOff + 0.1f) + Vector3.up * 0.05f);
                return;
            }
            if (_s >= STop && input > 0f)
            {
                // step off onto the roof ~0.6 m in from the eave, if there is a surface there
                Vector3 target = TopContact - Away * 0.6f;
                if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out var hit, 3.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.point.y > TopContact.y - 0.5f)
                {
                    Climbing = false;
                    SetPlayer(p, hit.point + Vector3.up * 0.06f);
                    return;
                }
                _s = STop;
            }
            _s = Mathf.Clamp(_s, 0f, STop);
            SetPlayer(p, Foot + Up * _s + Away * StandOff);
        }

        static void SetPlayer(PlayerRig p, Vector3 feet)
        {
            var cc = p.GetComponent<CharacterController>();
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            p.transform.position = feet;
            if (cc != null) cc.enabled = was;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { State = LadderState.OnRack; Placed = null; _carry = null; Changed = null; }
    }
}
