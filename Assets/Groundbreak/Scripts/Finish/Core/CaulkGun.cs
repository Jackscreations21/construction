using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Cross-trade hooks for the finish features.</summary>
    public static class FinishHooks
    {
        /// <summary>Providers of "how thick is the finish already on this face" (e.g. 1/2" drywall). Trim, cabinets and
        /// siding use <see cref="CoveringThickness"/> to sit on the finished surface instead of the bare framing.</summary>
        public static readonly List<Func<FramePlane, Vector2, float>> CoveringProviders = new List<Func<FramePlane, Vector2, float>>();

        /// <summary>Total finish thickness on the face at uv (0 if bare framing).</summary>
        public static float CoveringThickness(FramePlane fp, Vector2 uv)
        {
            float t = 0f;
            foreach (var p in CoveringProviders) t = Mathf.Max(t, p(fp, uv));
            return t;
        }
    }

    /// <summary>Caulk gun (shared by every trade). Each trade registers a tube type with <see cref="RegisterTube"/>:
    /// fire caulk at plate penetrations, exterior sealant at trim/siding joints, painter's caulk on interior trim.
    /// RMB cycles the loaded tube; LMB lays a bead with the active tube's handler.</summary>
    public class CaulkGun : FinishTool
    {
        public class Tube
        {
            public string Name;          // "Fire caulk (red)"
            public string StockId;       // consumable id (one tube ≈ 25 linear feet of 1/4" bead)
            public Color BeadColor;
            /// <summary>Lay a bead at the aimed hit; return true if something was caulked.</summary>
            public Func<RaycastHit, CaulkGun, bool> Apply;
        }

        static readonly List<Tube> _tubes = new List<Tube>();
        public static IReadOnlyList<Tube> Tubes => _tubes;
        public static void RegisterTube(Tube t) { _tubes.RemoveAll(x => x.Name == t.Name); _tubes.Add(t); }

        /// <summary>Feet of bead left in the loaded tube.</summary>
        public float FeetLeft;
        public const float FeetPerTube = 25f;

        public override string Id => "caulk-gun";
        public override string Name => "Caulk Gun";
        public override float Range => 3f;
        public override string[] Modes { get { var m = new string[_tubes.Count]; for (int i = 0; i < m.Length; i++) m[i] = _tubes[i].Name; return m; } }
        public Tube Active => _tubes.Count == 0 ? null : _tubes[Mathf.Clamp(Mode, 0, _tubes.Count - 1)];
        public override string Hint => Active == null ? "No caulk tubes available" : $"[{Active.Name}] {FeetLeft:F0} ft left in tube · LMB bead · RMB swap tube";

        public override void Secondary() { base.Secondary(); FeetLeft = 0f; }

        protected override void Use(RaycastHit hit)
        {
            var t = Active; if (t == null) return;
            t.Apply?.Invoke(hit, this);
        }

        /// <summary>Uses bead length from the tube, loading a new tube from stock when empty. Handlers call this.</summary>
        public bool Consume(float meters)
        {
            float feet = meters / Units.Foot;
            while (FeetLeft < feet)
            {
                if (!FinishStock.Take(Active.StockId)) return false;
                FeetLeft += FeetPerTube;
            }
            FeetLeft -= feet;
            ToolUtil.Wear(this, 0.0005f);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            ToolFactory.Register(new ToolShopItem { Id = "caulk-gun", Name = "Caulk Gun", Price = 12, Description = "Dripless caulk gun for 10 oz tubes: fire caulk, exterior sealant, painter's caulk." }, () => new CaulkGun());
        }
    }

    /// <summary>Visual caulk / sealant / foam beads: a small rounded rod between two points.</summary>
    public static class Bead
    {
        static readonly Dictionary<Color, Material> _mats = new Dictionary<Color, Material>();

        public static GameObject Add(Vector3 a, Vector3 b, Color color, float diameter = 0.0064f, Transform parent = null, string kind = "bead")
        {
            Vector3 d = b - a; float len = d.magnitude;
            if (len < 0.001f) return null;
            var mb = new MeshBuilder();
            mb.AddRod(Vector3.zero, Quaternion.identity, diameter * 0.5f, len, 8, 0);
            var go = new GameObject("Bead_" + kind);
            if (parent != null) go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(d / len, Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up));
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build("Bead");
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat(color);
            go.AddComponent<FinishLayer>().Kind = kind;
            return go;
        }

        public static Material Mat(Color c)
        {
            if (_mats.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(FinishMaterials.Get("caulk-white")) { name = "Bead_" + ColorUtility.ToHtmlStringRGB(c), hideFlags = HideFlags.DontUnloadUnusedAsset };
            m.color = c;
            _mats[c] = m;
            return m;
        }
    }
}
