using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Per-frame driver and persistence for the Fleet/Yard/Finance features (SaveSystem key "yardfleet").</summary>
    public class YardFleetRuntime : MonoBehaviour
    {
        public static YardFleetRuntime I;
        int _builtLevel = -1;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            Fleet.Tick(Time.deltaTime);
            Finance.SyncLedger();
            if (Company.WarehouseLevel != _builtLevel)
            {
                _builtLevel = Company.WarehouseLevel;
                Yard.Position = TownHook.HQPosition;
                YardSite.Rebuild();
                Yard.Dirty = true;
            }
            if (Yard.Dirty && YardSite.I != null) { Yard.Dirty = false; YardSite.I.RefreshContents(); }
        }

        /// <summary>Walks the player (teleports) to the warehouse door. Fast travel proper belongs to the Town feature.</summary>
        public static bool GoToYard(out string msg)
        {
            if (YardSite.I == null) { msg = "You have no warehouse yet."; return false; }
            var p = Game.Player;
            if (p == null) { msg = "No player."; return false; }
            var cc = p.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            p.transform.position = YardSite.I.PlayerSpawn + Vector3.up * 0.05f;
            Vector3 look = YardSite.I.WorldBounds.center - YardSite.I.PlayerSpawn; look.y = 0;
            if (look.sqrMagnitude > 0.01f) p.SetLook(Quaternion.LookRotation(look).eulerAngles.y, 0f);
            if (cc != null) cc.enabled = true;
            msg = "At the warehouse."; return true;
        }

        /// <summary>Clears all Fleet/Yard/Finance state (tests and New Game call this through the save extension).</summary>
        public static void ResetAll()
        {
            Fleet.ResetState(); Finance.ResetState(); Yard.ResetState();
            YardSite.Rebuild();
            if (I != null) I._builtLevel = Company.WarehouseLevel;
        }

        // ------------------------------------------------------------------ save

        [Serializable]
        class Data
        {
            public List<string> stockIds = new List<string>(); public List<int> stockQty = new List<int>();
            public List<string> equipment = new List<string>();
            public List<VehicleInfo> vehicles = new List<VehicleInfo>();
            public List<Trip> trips = new List<Trip>();
            public Finance.Data finance;
        }

        static string Save()
        {
            var d = new Data { equipment = new List<string>(Yard.OwnedEquipment), vehicles = new List<VehicleInfo>(Fleet.Infos), trips = new List<Trip>(Fleet.Trips), finance = Finance.Capture() };
            foreach (var kv in Yard.Stock.Entries) if (kv.Value > 0) { d.stockIds.Add(kv.Key); d.stockQty.Add(kv.Value); }
            return JsonUtility.ToJson(d);
        }

        static void Load(string json)
        {
            Fleet.ResetState(); Yard.ResetState();
            if (string.IsNullOrEmpty(json)) { Finance.ResetState(); Yard.MarkDirty(); return; }
            var d = JsonUtility.FromJson<Data>(json);
            for (int i = 0; i < d.stockIds.Count && i < d.stockQty.Count; i++) Yard.Stock.Add(d.stockIds[i], d.stockQty[i]);
            Yard.OwnedEquipment.AddRange(d.equipment);
            Fleet.Infos.AddRange(d.vehicles);
            Fleet.Trips.AddRange(d.trips);
            Finance.Restore(d.finance);
            Yard.MarkDirty();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveSystem.RegisterExtension("yardfleet", Save, Load);
            // Company's own extension loads first or later; the warehouse site is rebuilt by Update when the level changes.
            SaveSystem.AfterLoad += () => { if (I != null) I._builtLevel = -1; Finance.ResetLedgerCursor(); };
            var go = new GameObject("YardFleetRuntime");
            DontDestroyOnLoad(go);
            go.AddComponent<YardFleetRuntime>();
        }
    }
}
