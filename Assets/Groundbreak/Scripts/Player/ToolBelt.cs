using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>8-slot tool belt + nail pouch. Drives the active Tool.</summary>
    public class ToolBelt : MonoBehaviour
    {
        public const int SlotCount = 8;
        public Tool[] Slots = new Tool[SlotCount];
        public int ActiveIndex;
        public Tool Active => ActiveIndex >= 0 && ActiveIndex < SlotCount ? Slots[ActiveIndex] : null;
        public List<Tool> Owned = new List<Tool>();

        public int Nails = 200;
        public int NailCapacity = 200;
        public const string NailId = "nails16d";
        public const float PileRange = 3f;

        Tool _equipped;
        bool _showList;

        void Awake()
        {
            Game.Belt = this;
            foreach (var id in new[] { "place", "hammer", "saw", "tape", "prybar", "level" })
            {
                var t = ToolFactory.Create(id);
                if (t != null) AddTool(t);
            }
            ActiveIndex = 0;
        }

        void Start() { Equip(); }

        void OnDestroy() { if (Game.Belt == this) Game.Belt = null; }

        /// <summary>Adds to Owned and puts it in the first free run of slots that fits.</summary>
        public void AddTool(Tool t)
        {
            if (t == null) return;
            if (!Owned.Contains(t)) Owned.Add(t);
            if (IndexOf(t) >= 0) return;
            for (int i = 0; i + t.Slots <= SlotCount; i++)
            {
                bool free = true;
                for (int k = 0; k < t.Slots; k++) if (Slots[i + k] != null) free = false;
                if (free) { Assign(t, i); return; }
            }
        }

        public int IndexOf(Tool t) { for (int i = 0; i < SlotCount; i++) if (Slots[i] == t) return i; return -1; }

        /// <summary>Puts tool into slot (and next slot for 2-slot tools), evicting whatever was there.</summary>
        public void Assign(Tool t, int slot)
        {
            if (t == null) return;
            if (slot + t.Slots > SlotCount) slot = SlotCount - t.Slots;
            Tool before = Active;
            for (int i = 0; i < SlotCount; i++) if (Slots[i] == t) Slots[i] = null; // remove old position
            for (int k = 0; k < t.Slots; k++)
            {
                var evict = Slots[slot + k];
                if (evict != null) for (int i = 0; i < SlotCount; i++) if (Slots[i] == evict) Slots[i] = null;
            }
            for (int k = 0; k < t.Slots; k++) Slots[slot + k] = t;
            if (Active != before) Equip();
        }

        public void Select(int slot)
        {
            if (slot < 0 || slot >= SlotCount) return;
            ActiveIndex = slot;
            Equip();
        }

        void Equip()
        {
            var a = Active;
            if (a == _equipped) return;
            _equipped?.OnUnequip();
            _equipped = a;
            _equipped?.OnEquip();
        }

        void Update()
        {
            if (Game.UIOpen) { _showList = false; return; }

            _showList = GInput.GetKey(KeyCode.Tab);
            for (int i = 0; i < SlotCount; i++)
            {
                if (!GInput.GetKeyDown(KeyCode.Alpha1 + i)) continue;
                if (_showList)
                {
                    // Tab + number: assign the Nth owned tool... number picks the target slot for the highlighted tool
                    if (_listPick >= 0 && _listPick < Owned.Count) Assign(Owned[_listPick], i);
                }
                else Select(i);
            }
            if (_showList)
            {
                float s = GInput.Scroll;
                if (s != 0 && Owned.Count > 0) _listPick = (_listPick - (int)s + Owned.Count) % Owned.Count;
            }

            if (GInput.GetKeyDown(KeyCode.E)) TryRefillNails();

            Equip(); // in case slots changed externally
            var t = Active;
            if (t == null) return;
            t.Tick();
            if (_showList || Cursor.lockState != CursorLockMode.Locked) return;
            if (GInput.MouseDown(0)) t.Primary();
            if (GInput.Mouse(0)) t.PrimaryHeld();
            if (GInput.MouseDown(1)) t.Secondary();
        }

        int _listPick;

        void TryRefillNails()
        {
            if (Nails >= NailCapacity) { Game.Toast("Nail pouch already full"); return; }
            bool near = false;
            foreach (var p in Object.FindObjectsByType<MaterialPile>(FindObjectsSortMode.None))
                if (Vector3.Distance(p.transform.position, transform.position) <= PileRange) { near = true; break; }
            if (!near) return;
            int want = NailCapacity - Nails;
            int have = Game.Stock.Count(NailId);
            int take = Mathf.Min(want, have);
            if (take <= 0) { Game.Toast("No nails on site - order a box"); return; }
            Game.Stock.TryTake(NailId, take);
            Nails += take;
            Game.Toast($"Refilled pouch +{take} nails ({Nails}/{NailCapacity})");
        }

        void OnGUI()
        {
            if (Game.UIOpen) return;
            Active?.DrawGUI();
            if (!_showList) return;
            float w = 280, h = 26 * (Owned.Count + 2);
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(r, "Tools  (scroll = pick, 1-8 = put in slot)");
            for (int i = 0; i < Owned.Count; i++)
            {
                var t = Owned[i];
                int idx = IndexOf(t);
                string slot = idx >= 0 ? $"[{idx + 1}]" : "[ - ]";
                string line = $"{(i == _listPick ? "> " : "   ")}{slot} {t.Name}{(t.Slots > 1 ? " (2 slots)" : "")}{(t.Cordless ? $"  {Mathf.RoundToInt(t.Battery * 100)}%" : "")}";
                GUI.Label(new Rect(r.x + 10, r.y + 26 * (i + 1), w - 20, 24), line);
            }
        }
    }
}
