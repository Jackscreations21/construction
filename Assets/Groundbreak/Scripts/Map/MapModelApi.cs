using System;
using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// What a map building is: its type id (registered in <see cref="MapModels"/>), footprint and look parameters.
    /// Building frame (the frame every model draws in): origin at grade (y = 0) in the middle of the FRONT edge of the footprint,
    /// +Z points out of the front (toward the street), the footprint covers x -Width/2..+Width/2 and z -Depth..0.
    /// +X is the right-hand side for someone standing inside the lot looking at the street.
    /// </summary>
    public class MapBuildingSpec
    {
        public string Type;          // registered model type id, e.g. "storefront", "twostory", "barn"
        public float Width = 10f;    // metres along the street (x)
        public float Depth = 10f;    // metres back from the front (z = 0 .. -Depth)
        public int Stories = 1;
        public int Units = 1;        // townhome row units, strip-mall bays, storage rows...
        public string Sign = "";     // sign text (upper case A-Z 0-9 & ' . - space) for businesses, "" for none
        public Color Color = Color.white;   // main body colour (siding / brick tint / paint)
        public Color Accent = new Color(0.2f, 0.25f, 0.3f); // trim / awning / door colour
        public int Seed;             // deterministic variation
        /// <summary>Lot size around the building in the same frame (x -LotWidth/2..+LotWidth/2, z from +FrontYard to -(Depth+BackYard)).
        /// Models that draw site work (parking lots, yards, pumps, fences) may use it; 0 = unknown.</summary>
        public float LotWidth, FrontYard, BackYard;
    }

    /// <summary>Drawing context handed to a model builder. Everything is appended to one shared <see cref="LandModel"/>
    /// (one merged mesh per map cell); <see cref="MeshBuilder.Transform"/> is already set to the building frame, so draw in local
    /// metres. UVs are in metres (MeshBuilder does that for boxes/quads) — never scale anything.</summary>
    public class MapBuild
    {
        public LandModel K;
        public MeshBuilder B => K.B;
        public System.Random Rng;
        /// <summary>Building frame → mesh space. Set by the caller; B.Transform == Frame while a model draws.
        /// NEVER set B.Transform / call B.ResetTransform() yourself: use BeginLocal/EndLocal for rotated parts.</summary>
        public Matrix4x4 Frame = Matrix4x4.identity;

        /// <summary>Following geometry is drawn in a sub-frame (pos/rot relative to the building frame). Call EndLocal() after.</summary>
        public void BeginLocal(Vector3 pos, Quaternion rot) => B.Transform = Frame * Matrix4x4.TRS(pos, rot, Vector3.one);
        public void EndLocal() => B.Transform = Frame;
        /// <summary>Solid boxes (building-frame min/max) that get BoxColliders (walls, the whole body of a closed building...).</summary>
        public readonly List<(Vector3 min, Vector3 max)> Solids = new List<(Vector3, Vector3)>();
        /// <summary>Optional point lights (building-frame position, colour) — keep to 0..1 per building.</summary>
        public readonly List<(Vector3 pos, Color color, float range)> Lights = new List<(Vector3, Color, float)>();

        public float R() => (float)Rng.NextDouble();
        public float R(float a, float b) => a + (b - a) * (float)Rng.NextDouble();

        /// <summary>Axis-aligned box from two corners (building frame). uAxis = texture U direction (default along x).</summary>
        public void Box(Vector3 a, Vector3 b, int sub, Vector3 uAxis = default)
        {
            Vector3 min = Vector3.Min(a, b), max = Vector3.Max(a, b);
            if (max.x - min.x < 1e-4f || max.y - min.y < 1e-4f || max.z - min.z < 1e-4f) return;
            B.AddBox((min + max) * 0.5f, max - min, Quaternion.identity, sub, uAxis == default ? Vector3.right : uAxis);
        }
        /// <summary>Box that is also solid (collider).</summary>
        public void SolidBox(Vector3 a, Vector3 b, int sub, Vector3 uAxis = default) { Box(a, b, sub, uAxis); Solid(a, b); }
        public void Solid(Vector3 a, Vector3 b) => Solids.Add((Vector3.Min(a, b), Vector3.Max(a, b)));
    }

    /// <summary>Registry of map building models by type id. Model files register their types from
    /// [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]. Unknown types draw a plain box so the map never breaks.</summary>
    public static class MapModels
    {
        static readonly Dictionary<string, Action<MapBuild, MapBuildingSpec>> _types = new Dictionary<string, Action<MapBuild, MapBuildingSpec>>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string type, Action<MapBuild, MapBuildingSpec> build) => _types[type] = build;
        public static bool Has(string type) => type != null && _types.ContainsKey(type);
        public static IEnumerable<string> Types => _types.Keys;

        /// <summary>Draws a building (spec) into b. Catches model errors and falls back to a plain box.</summary>
        public static void Draw(MapBuild b, MapBuildingSpec s)
        {
            if (s.Type != null && _types.TryGetValue(s.Type, out var f))
            {
                try { f(b, s); return; }
                catch (Exception e) { Debug.LogError($"[Map] model '{s.Type}' failed: {e}"); }
            }
            int sub = b.K.M(SurfaceKind.PaintedWhite, s.Color);
            float h = 3f * Mathf.Max(1, s.Stories);
            b.SolidBox(new Vector3(-s.Width / 2, 0, -s.Depth), new Vector3(s.Width / 2, h, 0), sub);
        }
    }
}
