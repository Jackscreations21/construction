using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// Machine earthwork on top of the existing ground systems: the building pad (<see cref="SiteGround"/>, 4" cells, where
    /// footings, trenches and basements are dug) and the lot field (<see cref="Grade"/>, 0.5 m cells, rough grading).
    /// Cut returns the bank volume removed; Deposit builds a spoil pile. Volumes in m³.
    /// </summary>
    public static class Earth
    {
        public const float SwellFactor = 1.25f;          // loose / bank volume for ordinary soil
        /// <summary>Spoil hauled off site this session (cu yd, loose) — for the HUD and tests.</summary>
        public static float HauledYd;
        public static event System.Action<float> Dug;     // m³ cut by a machine

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { HauledYd = 0; Dug = null; }

        public static bool OnPad(Vector3 p) => SiteGround.I != null && SiteGround.InPatch(p);
        public static bool OnField(Vector3 p) => Grade.Field != null && Grade.Field.Contains(p.x, p.z);
        public static bool Workable(Vector3 p) => OnPad(p) || OnField(p);

        /// <summary>Ground height for digging (pad or lot field), else NaN.</summary>
        public static float Height(Vector3 p)
        {
            if (OnPad(p)) return SiteGround.I.HeightAt(p);
            if (OnField(p)) return Grade.Field.HeightAt(p.x, p.z);
            return float.NaN;
        }

        public static GroundCover CoverAt(Vector3 p)
        {
            if (OnPad(p)) return SiteGround.I.CoverAt(p);
            if (OnField(p)) { var c = Grade.Field.CoverAt(p.x, p.z); return c == LandCover.Gravel ? GroundCover.Gravel : c == LandCover.Grass || c == LandCover.Lawn ? GroundCover.Grass : GroundCover.Dirt; }
            return GroundCover.Grass;
        }

        /// <summary>Cuts the ground down toward targetY in a circle, at most maxM3. Returns m³ removed (bank).</summary>
        public static float Cut(Vector3 p, float radius, float targetY, float maxM3 = float.MaxValue)
        {
            if (maxM3 <= 1e-5f) return 0f;
            float got = 0f;
            if (OnPad(p))
            {
                float h = SiteGround.I.HeightAt(p);
                if (h <= targetY + 0.002f) return 0f;
                // step down so one pass never removes more than the bucket can hold
                float area = Mathf.PI * radius * radius;
                float step = Mathf.Min(h - targetY, Mathf.Max(0.01f, maxM3 / Mathf.Max(0.01f, area)));
                got = SiteGround.I.DigTo(p, radius, h - step);
            }
            else if (OnField(p))
            {
                float h = Grade.Field.HeightAt(p.x, p.z);
                if (h <= targetY + 0.002f) return 0f;
                float area = Mathf.PI * radius * radius * 0.5f;
                float step = Mathf.Min(h - targetY, Mathf.Max(0.01f, maxM3 / Mathf.Max(0.01f, area)));
                got = -Grade.Field.Raise(p, radius, -step);
                Grade.Field.Paint(p, radius * 0.8f, LandCover.Dirt);
            }
            if (got > 0) Dug?.Invoke(got);
            return Mathf.Max(0f, got);
        }

        /// <summary>Raises ground up to targetY (filling low spots), using at most maxM3. Returns m³ placed.</summary>
        public static float Fill(Vector3 p, float radius, float targetY, float maxM3, GroundCover cover = GroundCover.Dirt)
        {
            if (maxM3 <= 1e-5f) return 0f;
            if (OnPad(p)) return SiteGround.I.RaiseTo(p, radius, targetY, cover, maxM3);
            if (OnField(p))
            {
                float h = Grade.Field.HeightAt(p.x, p.z);
                if (h >= targetY - 0.002f) return 0f;
                float area = Mathf.PI * radius * radius * 0.5f;
                float step = Mathf.Min(targetY - h, maxM3 / Mathf.Max(0.01f, area));
                float v = Grade.Field.Raise(p, radius, step);
                Grade.Field.Paint(p, radius * 0.8f, cover == GroundCover.Gravel ? LandCover.Gravel : LandCover.Dirt);
                return Mathf.Max(0f, v);
            }
            return 0f;
        }

        /// <summary>Dumps loose material as a pile centred at p (repose ~35°). Returns m³ placed (0 off the lot).</summary>
        public static float Deposit(Vector3 p, float m3, GroundCover cover = GroundCover.Dirt)
        {
            if (m3 <= 1e-5f || !Workable(p)) return 0f;
            float left = m3, placed = 0f;
            float baseY = Height(p);
            // grow a cone: each ring is filled to a height that falls off with radius
            for (int guard = 0; guard < 24 && left > 1e-4f; guard++)
            {
                float r = 0.35f + guard * 0.12f;
                float top = baseY + Mathf.Max(0.05f, (1.6f - r) * 0.7f);
                float v = Fill(p, r, top, left, cover);
                placed += v; left -= v;
                if (v < 1e-5f && r > 1.4f) break;
            }
            return placed;
        }

        /// <summary>Records spoil hauled away (loose m³).</summary>
        public static void Haul(float looseM3) { HauledYd += looseM3 / Grade.CubicYard; }
    }

    /// <summary>A dump truck parked for haul-off: the excavator loads its bed; when 5 cu yd (heaped) are in, it drives off
    /// and returns empty after a short trip. Uses the fleet's real dump-truck model. Owned dump truck = no charge; otherwise
    /// a hired truck at a per-load rate.</summary>
    public class HaulTruck : MonoBehaviour
    {
        public static HaulTruck I;
        public const float CapacityYd = 5f;
        public const int HiredPerLoad = 385;     // typical US haul-off: truck + driver + dump fee per load
        public float LoadYd;
        public int Loads;
        public bool Away => _awayT > 0f;
        float _awayT;
        GameObject _model, _heap;
        static readonly Vector3 BedCenterLocal = new Vector3(0f, 1.55f, -1.55f), BedSize = new Vector3(2.1f, 0.9f, 3.1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { I = null; }

        public static bool Owned => Company.Owns("dump");

        public static HaulTruck Call(Vector3 pos, float yaw)
        {
            Dismiss();
            var go = new GameObject("Haul dump truck") { layer = 2 };
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            I = go.AddComponent<HaulTruck>();
            I._model = VehicleModels.Build("dump", go.transform);
            Machine.SetLayerRecursive(go, 2);
            return I;
        }

        public static void Dismiss() { if (I != null) { Destroy(I.gameObject); I = null; } }

        /// <summary>Is the world point over the dump body (within its footprint, above its floor)?</summary>
        public bool OverBed(Vector3 world)
        {
            if (Away) return false;
            Vector3 l = transform.InverseTransformPoint(world) - BedCenterLocal;
            return Mathf.Abs(l.x) < BedSize.x * 0.5f + 0.15f && Mathf.Abs(l.z) < BedSize.z * 0.5f + 0.15f && l.y > -0.6f && l.y < 3f;
        }

        /// <summary>Adds loose m³ to the bed. Returns what fit.</summary>
        public float Load(float looseM3)
        {
            float room = (CapacityYd - LoadYd) * Grade.CubicYard;
            float v = Mathf.Min(room, looseM3);
            LoadYd += v / Grade.CubicYard;
            Earth.Haul(v);
            RebuildHeap();
            if (LoadYd >= CapacityYd - 0.05f) Depart();
            return v;
        }

        void Depart()
        {
            Loads++;
            if (GameSettings.IsCareer && !Owned && !GameSettings.UnlimitedMoney) Game.Wallet.TrySpend(HiredPerLoad, "Haul-off (dump truck)");
            Game.Toast($"Dump truck full ({CapacityYd:0} cu yd) - hauling off" + (Owned ? "" : $" (${HiredPerLoad}/load)"));
            _awayT = 20f;
            if (_model != null) _model.SetActive(false);
        }

        void RebuildHeap()
        {
            if (_heap != null) { var mf = _heap.GetComponent<MeshFilter>(); if (mf != null) Destroy(mf.sharedMesh); Destroy(_heap); }
            if (LoadYd <= 0.01f || _model == null || !_model.activeSelf) return;
            float f = Mathf.Clamp01(LoadYd / CapacityYd);
            float h = 0.1f + f * 0.95f;
            _heap = CTool.Build("Heap", (b, m) =>
            {
                int dirt = m.K(SurfaceKind.Dirt);
                float y0 = BedCenterLocal.y - BedSize.y * 0.5f;
                b.AddFrustum(new Vector3(BedCenterLocal.x, y0 + h * 0.5f, BedCenterLocal.z), Quaternion.identity, 1.05f, 0.35f + 0.2f * (1 - f), h, 10, dirt);
            }, transform);
            _heap.layer = 2;
        }

        void Update()
        {
            if (_awayT > 0f)
            {
                _awayT -= Time.deltaTime;
                if (_awayT <= 0f) { LoadYd = 0f; if (_model != null) _model.SetActive(true); RebuildHeap(); Game.Toast("Empty dump truck back for the next load"); }
            }
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            if (_heap != null) { var mf = _heap.GetComponent<MeshFilter>(); if (mf != null) Destroy(mf.sharedMesh); }
        }
    }
}
