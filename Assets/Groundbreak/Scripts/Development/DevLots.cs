using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>A building lot (or a raw-land tract) in world XZ meters. Mirrors the Town's TownLot conventions:
    /// Front = unit vector toward the street, Local(x, depth) = x to the right when facing the street from the lot,
    /// depth back from the front lot line.</summary>
    [Serializable]
    public class DevLot
    {
        public string Id, Address, Owner;
        public Rect Rect;              // world XZ
        public Vector3 Front;          // toward the street
        public bool Claimed;
        public bool IsTract;           // raw land, not a finished lot

        public Vector3 Center => new Vector3(Rect.center.x, 0f, Rect.center.y);
        Vector3 Right => Vector3.Cross(Vector3.up, Front);
        public float FrontageM => Mathf.Abs(Front.x) > 0.5f ? Rect.height : Rect.width;
        public float DepthM => Mathf.Abs(Front.x) > 0.5f ? Rect.width : Rect.height;
        public float FrontageFt => FrontageM / Units.Foot;
        public float DepthFt => DepthM / Units.Foot;
        public float Acres => FrontageFt * DepthFt / 43560f;
        public Vector3 FrontMid => Center + Front * (DepthM / 2f);
        public Vector3 Local(float x, float depth) => FrontMid + Right * x - Front * depth;
        public bool Contains(Vector3 p) => Rect.Contains(new Vector2(p.x, p.z));
        public float Yaw => Quaternion.LookRotation(-Front).eulerAngles.y; // facing back into the lot from the street
    }

    /// <summary>Where a plan sits on a lot: plan feet (x to the right as seen from the street, y back from the front wall) → world.</summary>
    public struct PlanPlacement
    {
        public Vector3 Origin;   // front-left corner of the house as seen from the street (world, y = grade)
        public Vector3 X, Y;     // unit vectors of plan x and y in world
        public Vector3 World(float xFt, float yFt, float upM = 0f) => Origin + X * (xFt * Units.Foot) + Y * (yFt * Units.Foot) + Vector3.up * upM;
        public Quaternion Rotation => Quaternion.LookRotation(Y, Vector3.up);

        /// <summary>Centered on the frontage, set back by frontSetbackFt from the front lot line.</summary>
        public static PlanPlacement On(DevLot lot, HousePlan plan, float frontSetbackFt = 25f)
        {
            var back = -lot.Front;
            var x = Vector3.Cross(Vector3.up, back); // right hand of someone on the street looking at the house
            var origin = lot.FrontMid + back * (frontSetbackFt * Units.Foot) - x * (plan.WidthFt * Units.Foot / 2f);
            origin.y = 0f;
            return new PlanPlacement { Origin = origin, X = x, Y = back };
        }
    }

    /// <summary>Source of buildable lots and raw land. The Town feature provides the real one after merge;
    /// until then <see cref="FallbackLotProvider"/> lays lots out on open ground far from the origin.</summary>
    public interface ILotProvider
    {
        IEnumerable<DevLot> FreeLots();            // finished house lots for client builds
        IEnumerable<DevLot> LandForSale();         // raw tracts for development
        DevLot Find(string id);
        bool Claim(DevLot lot, string owner);
        void Release(DevLot lot);
    }

    public static class DevLots
    {
        static ILotProvider _provider;
        /// <summary>Set by the Town adapter after merge; defaults to the fallback layout.</summary>
        public static ILotProvider Provider { get => _provider ?? (_provider = new FallbackLotProvider()); set => _provider = value; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void StaticReset() { _provider = null; }
    }

    /// <summary>Open ground east of the town (x ≥ 420 m): 12 house lots on "Ridge Rd" (z = 420) and 3 raw tracts further out.
    /// Deterministic; claims are saved by the Development save extension.</summary>
    public class FallbackLotProvider : ILotProvider
    {
        public const float RidgeZ = 420f;
        readonly List<DevLot> _lots = new List<DevLot>();

        public FallbackLotProvider()
        {
            float fw = 75f * Units.Foot, d = 130f * Units.Foot;
            for (int i = 0; i < 6; i++)
            {
                float x0 = 430f + i * fw;
                // north side of the road (lot fronts face -z toward the road)
                _lots.Add(new DevLot { Id = "ridge-" + (101 + 2 * i), Address = (101 + 2 * i) + " Ridge Rd", Rect = new Rect(x0, RidgeZ + 7.5f, fw, d), Front = Vector3.back });
                _lots.Add(new DevLot { Id = "ridge-" + (102 + 2 * i), Address = (102 + 2 * i) + " Ridge Rd", Rect = new Rect(x0, RidgeZ - 7.5f - d, fw, d), Front = Vector3.forward });
            }
            // raw tracts (fronting a county road along z = 600, facing -z)
            AddTract("tract-a", "County Rd 9, tract A", 430f, 610f, 140f, 152f);   // ≈ 5.3 ac
            AddTract("tract-b", "County Rd 9, tract B", 600f, 610f, 180f, 152f);   // ≈ 6.8 ac
            AddTract("tract-c", "County Rd 9, tract C", 800f, 610f, 240f, 200f);   // ≈ 11.9 ac
        }

        void AddTract(string id, string addr, float x, float z, float w, float d) =>
            _lots.Add(new DevLot { Id = id, Address = addr, Rect = new Rect(x, z, w, d), Front = Vector3.back, IsTract = true });

        public IEnumerable<DevLot> FreeLots() { foreach (var l in _lots) if (!l.IsTract && !l.Claimed) yield return l; }
        public IEnumerable<DevLot> LandForSale() { foreach (var l in _lots) if (l.IsTract && !l.Claimed) yield return l; }
        public DevLot Find(string id) => _lots.Find(l => l.Id == id);
        public bool Claim(DevLot lot, string owner) { if (lot == null || lot.Claimed) return false; lot.Claimed = true; lot.Owner = owner; return true; }
        public void Release(DevLot lot) { if (lot != null) { lot.Claimed = false; lot.Owner = null; } }
    }
}
