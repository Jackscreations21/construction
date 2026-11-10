using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Ghost preview + placement of building pieces, snap-setting keys, repeat (F) and undo (Ctrl+Z).</summary>
    public class PlacementController : MonoBehaviour
    {
        public const float AimRange = 8f;
        const float EdgeSnap = 3f * Units.Inch;   // edge/end alignment threshold when snapping to a piece
        const float Shrink = 0.002f;              // overlap test tolerance (meters)

        public PieceDefinition Selected { get; private set; }
        public float CutLength { get; set; }
        public bool Active { get; set; }
        public string StatusLine { get; private set; } = "";

        float _yaw, _pitch;
        GameObject _ghost;
        Renderer _ghostRenderer;

        bool _hasAim, _valid;
        string _reason = "";
        Vector3 _ghostPos;
        Quaternion _ghostRot = Quaternion.identity;
        Vector3 _aimPoint;

        // Repeat / undo
        bool _hasLast;
        Vector3 _lastPos;
        Quaternion _lastRot;
        Vector3 _lastDelta;
        struct Placed { public Piece Piece; public string Id; public float Offcut; }
        readonly List<Placed> _history = new List<Placed>();

        void Awake()
        {
            Game.Placement = this;
            foreach (var d in Catalog.All) if (d.IsPlaceable) { Selected = d; break; }
        }

        void OnDestroy()
        {
            if (Game.Placement == this) Game.Placement = null;
            if (_ghost != null) Destroy(_ghost);
        }

        float StockLength => Selected != null ? Selected.Size.z : 0f;
        float CurrentLength => CutLength > 0f ? CutLength : StockLength;

        Vector3 GhostSize => Selected == null ? Vector3.zero : new Vector3(Selected.Size.x, Selected.Size.y, CurrentLength);

        void Update()
        {
            if (!Game.UIOpen) HandleAlwaysKeys();

            if (Active && !Game.UIOpen) HandleActiveKeys();

            if (Active) UpdateGhost();
            else { _hasAim = false; SetGhostVisible(false); }

            BuildStatus();
        }

        // ---------------- input ----------------

        void HandleAlwaysKeys()
        {
            var snap = Game.Snap;
            if (GInput.GetKeyDown(KeyCode.G))
            {
                snap.CycleGrid(GInput.Shift ? -1 : 1);
                Game.Toast("Grid: " + snap.GridLabel);
            }
            if (GInput.GetKeyDown(KeyCode.R))
            {
                snap.CycleAngle(GInput.Shift ? -1 : 1);
                Game.Toast("Angle snap: " + snap.AngleLabel);
            }
            if (GInput.GetKeyDown(KeyCode.O) && Game.Player != null)
            {
                if (Game.Player.TryAim(out RaycastHit hit, AimRange))
                {
                    snap.Origin = hit.point;
                    snap.OriginYaw = Mathf.Round(PlayerYaw() / 90f) * 90f % 360f;
                    Game.Toast("Origin set (yaw " + snap.OriginYaw + "°)");
                }
                else Game.Toast("Origin: aim at a surface");
            }
            if (GInput.GetKeyDown(KeyCode.LeftBracket)) CycleSelected(-1);
            if (GInput.GetKeyDown(KeyCode.RightBracket)) CycleSelected(1);
        }

        void HandleActiveKeys()
        {
            // Tab + scroll is the tool belt's picker; don't rotate the ghost then.
            float scroll = GInput.GetKey(KeyCode.Tab) ? 0f : GInput.Scroll;
            if (scroll != 0f)
            {
                float step = Game.Snap.AngleStep > 0f ? Game.Snap.AngleStep : 5f;
                if (GInput.Alt) _pitch = Mathf.Repeat(_pitch + scroll * step, 360f);
                else _yaw = Mathf.Repeat(_yaw + scroll * step, 360f);
            }

            if (GInput.Ctrl && GInput.GetKeyDown(KeyCode.Z)) Undo();
            else if (GInput.GetKeyDown(KeyCode.F)) Repeat();
        }

        void CycleSelected(int dir)
        {
            var list = Catalog.All.FindAll(d => d.IsPlaceable);
            if (list.Count == 0) return;
            int i = Mathf.Max(0, list.IndexOf(Selected));
            Selected = list[(i + dir + list.Count) % list.Count];
            Game.Toast("Selected: " + Selected.Name + " (" + Game.Stock.Count(Selected.Id) + " on site)");
        }

        float PlayerYaw()
        {
            if (Game.Player == null) return 0f;
            var cam = Game.Player.Cam;
            return cam != null ? cam.transform.eulerAngles.y : Game.Player.transform.eulerAngles.y;
        }

        // ---------------- ghost ----------------

        void EnsureGhost()
        {
            if (_ghost != null) return;
            _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ghost.name = "PlacementGhost";
            _ghost.layer = 2; // Ignore Raycast
            var col = _ghost.GetComponent<Collider>();
            if (col != null) { col.enabled = false; Destroy(col); }
            _ghostRenderer = _ghost.GetComponent<Renderer>();
            _ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ghostRenderer.receiveShadows = false;
        }

        void SetGhostVisible(bool v)
        {
            if (_ghost != null && _ghost.activeSelf != v) _ghost.SetActive(v);
        }

        /// <summary>Half-extent of a rotated box along a world direction.</summary>
        static float HalfExtentAlong(Quaternion rot, Vector3 half, Vector3 dir)
        {
            return Mathf.Abs(Vector3.Dot(rot * Vector3.right, dir)) * half.x
                 + Mathf.Abs(Vector3.Dot(rot * Vector3.up, dir)) * half.y
                 + Mathf.Abs(Vector3.Dot(rot * Vector3.forward, dir)) * half.z;
        }

        void UpdateGhost()
        {
            _hasAim = false;
            if (Selected == null || Game.Player == null) { SetGhostVisible(false); return; }

            if (!Game.Player.TryAim(out RaycastHit hit, AimRange))
            {
                SetGhostVisible(false);
                _valid = false; _reason = "nothing aimed";
                return;
            }
            _hasAim = true;
            _aimPoint = hit.point;

            Vector3 size = GhostSize;
            Vector3 half = size * 0.5f;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 n = hit.normal;

            float hn = HalfExtentAlong(rot, half, n);
            Vector3 pos = hit.point + n * hn;
            pos = Game.Snap.SnapPosition(pos);
            // Restore exact contact with the hit surface along its normal (grid snap may have pushed it off/into it).
            float facePlane = Vector3.Dot(hit.point, n);
            pos += n * (facePlane + hn - Vector3.Dot(pos, n));

            if (!GInput.Ctrl)
            {
                var piece = hit.collider != null ? hit.collider.GetComponentInParent<Piece>() : null;
                if (piece != null) pos = SnapToPiece(piece, pos, rot, half, n);
            }

            _ghostPos = pos;
            _ghostRot = rot;

            EnsureGhost();
            SetGhostVisible(true);
            _ghost.transform.SetPositionAndRotation(pos, rot);
            _ghost.transform.localScale = size;

            _valid = Validate(pos, rot, size, out _reason);
            _ghostRenderer.sharedMaterial = MaterialLibrary.Ghost(_valid ? new Color(0.2f, 0.5f, 1f) : new Color(1f, 0.2f, 0.2f));
        }

        /// <summary>Sit exactly on the hit face (done) and align ends/edges with the piece along its other local axes.</summary>
        Vector3 SnapToPiece(Piece piece, Vector3 pos, Quaternion rot, Vector3 half, Vector3 n)
        {
            Transform t = piece.transform;
            Vector3 pHalf = piece.Size * 0.5f;
            Vector3 center = t.position;
            Vector3[] axes = { t.right, t.up, t.forward };
            float[] ext = { pHalf.x, pHalf.y, pHalf.z };

            for (int i = 0; i < 3; i++)
            {
                Vector3 a = axes[i];
                if (Mathf.Abs(Vector3.Dot(a, n)) > 0.5f) continue; // the contact axis: already exact
                float gh = HalfExtentAlong(rot, half, a);
                float gc = Vector3.Dot(pos - center, a);   // ghost center along axis, relative to piece center
                float pMin = -ext[i], pMax = ext[i];

                float best = float.MaxValue, target = gc;
                void Try(float candidateCenter)
                {
                    float d = Mathf.Abs(candidateCenter - gc);
                    if (d < best) { best = d; target = candidateCenter; }
                }
                Try(pMin + gh);       // ghost min edge = piece min edge
                Try(pMax - gh);       // ghost max edge = piece max edge
                Try(0f);              // centered
                Try(pMin - gh);       // butt against min end (outside)
                Try(pMax + gh);       // butt against max end (outside)
                if (best <= EdgeSnap) pos += a * (target - gc);
            }
            return pos;
        }

        bool Validate(Vector3 pos, Quaternion rot, Vector3 size, out string reason)
        {
            if (Selected == null) { reason = "nothing selected"; return false; }
            if (CutLength > StockLength + 0.0005f) { reason = "cut longer than stock"; return false; }
            if (Game.Stock.Count(Selected.Id) <= 0) { reason = "none on site"; return false; }
            if (Overlaps(pos, rot, size)) { reason = "blocked"; return false; }
            reason = "";
            return true;
        }

        static bool Overlaps(Vector3 pos, Quaternion rot, Vector3 size)
        {
            Vector3 half = size * 0.5f - Vector3.one * Shrink;
            half = Vector3.Max(half, Vector3.one * 0.0005f);
            var cols = Physics.OverlapBox(pos, half, rot, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
                if (c != null && c.GetComponentInParent<Piece>() != null) return true;
            return false;
        }

        // ---------------- actions ----------------

        /// <summary>Place the current ghost. Called by PlaceTool.Primary.</summary>
        public bool Place()
        {
            if (!Active || !_hasAim) { Game.Toast("Aim at a surface within 8 m"); return false; }
            if (!_valid) { Game.Toast("Can't place: " + _reason); return false; }
            Vector3 prev = _lastPos;
            bool hadLast = _hasLast;
            if (!PlaceAt(_ghostPos, _ghostRot)) return false;
            if (hadLast && (_ghostPos - prev).sqrMagnitude > 1e-6f) _lastDelta = _ghostPos - prev;
            return true;
        }

        bool PlaceAt(Vector3 pos, Quaternion rot)
        {
            if (!Game.Stock.TryTake(Selected.Id)) { Game.Toast("No " + Selected.Name + " on site"); return false; }
            float len = CurrentLength;
            var piece = Piece.Create(Selected, len, pos, rot);
            float offcut = 0f;
            if (CutLength > 0f && CutLength < StockLength - 0.0005f)
            {
                offcut = StockLength - CutLength;
                Game.Stock.Scrap.Add((Selected.Id, offcut));
            }
            _history.Add(new Placed { Piece = piece, Id = Selected.Id, Offcut = offcut });
            _hasLast = true;
            _lastPos = pos;
            _lastRot = rot;
            return true;
        }

        /// <summary>F: place another copy one step from the last placed piece (16" O.C., or the grid step if larger).</summary>
        public void Repeat()
        {
            if (!_hasLast || Selected == null) { Game.Toast("Nothing to repeat"); return; }
            float g = Game.Snap.GridMeters;
            float step = g >= 16f * Units.Inch - 0.0001f ? g : 16f * Units.Inch;

            Vector3 dir = LayoutAxis();

            Vector3 pos = _lastPos + dir * step;
            Vector3 size = new Vector3(Selected.Size.x, Selected.Size.y, CurrentLength);
            if (!Validate(pos, _lastRot, size, out string reason)) { Game.Toast("Repeat skipped: " + reason); return; }
            if (PlaceAt(pos, _lastRot)) _lastDelta = dir * step;
        }

        /// <summary>Direction for on-center layout: the piece's thinnest horizontal axis other than its length
        /// (across a flat board; along the wall line for a standing stud). Sign continues the last repeat,
        /// otherwise points the way the player faces.</summary>
        Vector3 LayoutAxis()
        {
            Vector3 x = _lastRot * Vector3.right, y = _lastRot * Vector3.up, z = _lastRot * Vector3.forward;
            Vector3 best = Vector3.zero; float bestSize = float.MaxValue;
            void Consider(Vector3 axis, float size)
            {
                if (Mathf.Abs(axis.y) > 0.3f) return; // not horizontal
                if (size < bestSize) { best = axis; bestSize = size; }
            }
            Consider(x, Selected.Size.x);
            Consider(y, Selected.Size.y);
            if (best == Vector3.zero) Consider(z, CurrentLength); // e.g. odd tilt: fall back to length
            if (best == Vector3.zero) best = Vector3.right;
            best.y = 0f; best.Normalize();

            float sign;
            if (_lastDelta.sqrMagnitude > 1e-6f && Mathf.Abs(Vector3.Dot(_lastDelta.normalized, best)) > 0.7f)
                sign = Mathf.Sign(Vector3.Dot(_lastDelta, best));
            else
            {
                float d = Vector3.Dot(SnappedFacingAxis(), best);
                if (Mathf.Abs(d) < 0.1f) d = Vector3.Dot(Quaternion.Euler(0f, PlayerYaw(), 0f) * Vector3.right, best);
                sign = d < 0f ? -1f : 1f;
            }
            return best * sign;
        }

        /// <summary>Player's facing projected onto the horizontal plane and snapped to the nearest grid axis (rotated by OriginYaw).</summary>
        Vector3 SnappedFacingAxis()
        {
            if (Game.Player == null) return Vector3.zero;
            Vector3 f = Quaternion.Euler(0f, PlayerYaw(), 0f) * Vector3.forward;
            Quaternion o = Quaternion.Euler(0f, Game.Snap.OriginYaw, 0f);
            Vector3 x = o * Vector3.right, z = o * Vector3.forward;
            float dx = Vector3.Dot(f, x), dz = Vector3.Dot(f, z);
            return Mathf.Abs(dx) >= Mathf.Abs(dz) ? x * Mathf.Sign(dx) : z * Mathf.Sign(dz);
        }

        /// <summary>Ctrl+Z: remove the most recently placed piece if it is still Loose, returning it to stock.</summary>
        public void Undo()
        {
            while (_history.Count > 0)
            {
                var h = _history[_history.Count - 1];
                _history.RemoveAt(_history.Count - 1);
                if (h.Piece == null) continue; // already picked up / destroyed
                if (h.Piece.State != PieceState.Loose) { Game.Toast("Can't undo: piece is fastened"); return; }

                // If the offcut is still in scrap, the stick was never really used: return full stock.
                int idx = h.Offcut > 0f ? Game.Stock.Scrap.FindIndex(s => s.id == h.Id && Mathf.Abs(s.length - h.Offcut) < 0.0005f) : -1;
                bool fullLength = h.Piece.Def != null && h.Piece.Size.z >= h.Piece.Def.Size.z - 0.0005f;
                if (fullLength) Game.Stock.Add(h.Id, 1);
                else if (idx >= 0) { Game.Stock.Scrap.RemoveAt(idx); Game.Stock.Add(h.Id, 1); }
                else Game.Stock.Scrap.Add((h.Id, h.Piece.Size.z));

                Destroy(h.Piece.gameObject);
                Game.Toast("Undo: removed " + h.Piece.Def?.Name);

                // Step repeat anchor back
                _hasLast = false;
                for (int i = _history.Count - 1; i >= 0; i--)
                    if (_history[i].Piece != null && _history[i].Piece.gameObject != h.Piece.gameObject)
                    { _hasLast = true; _lastPos = _history[i].Piece.transform.position; _lastRot = _history[i].Piece.transform.rotation; break; }
                return;
            }
            Game.Toast("Nothing to undo");
        }

        // ---------------- status / GUI ----------------

        void BuildStatus()
        {
            if (Selected == null) { StatusLine = ""; return; }
            string s = Selected.Name;
            if (CutLength > 0f) s += " · cut " + Units.Format(CutLength);
            s += " · " + Game.Stock.Count(Selected.Id) + " on site";
            if (Active)
            {
                if (!_hasAim) s += " · aim at a surface";
                else
                {
                    s += _valid ? " · VALID" : " · INVALID (" + _reason + ")";
                    Vector3 d = _aimPoint - Game.Snap.Origin;
                    Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, Game.Snap.OriginYaw, 0f)) * d;
                    s += " · from origin " + Units.Format(new Vector2(d.x, d.z).magnitude)
                       + " (x " + Units.Format(local.x) + ", z " + Units.Format(local.z) + ")";
                }
            }
            StatusLine = s;
        }

        GUIStyle _small;

        void OnGUI()
        {
            if (!Active || !_hasAim || Game.UIOpen) return;
            if (_small == null) _small = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _small.normal.textColor = _valid ? new Color(0.7f, 0.85f, 1f) : new Color(1f, 0.55f, 0.55f);
            string txt = Units.Format(CurrentLength) + "\nyaw " + Mathf.RoundToInt(_yaw) + "°  pitch " + Mathf.RoundToInt(_pitch) + "°";
            GUI.Label(new Rect(Screen.width * 0.5f + 14f, Screen.height * 0.5f + 10f, 160f, 34f), txt, _small);
        }
    }
}
