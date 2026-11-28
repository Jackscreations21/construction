using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>Long framing runs (sills, plates, rims) built from real stock lengths: a run longer than the longest board of its size
    /// is cut into several pieces with butt joints at allowed spots (over a stud / joist), staggered away from the joints of the layer
    /// below (IRC R602.3.2: double top plate splices offset 24" min). Also registers the 20' rafter stock lumber yards carry.</summary>
    public static class Splice
    {
        static readonly Color Pine = new Color(0.86f, 0.72f, 0.50f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterLongStock()
        {
            // 20' 2x8 / 2x10 / 2x12: stocked for rafters and long rims (beyond this a rafter needs a purlin/bearing wall or trusses)
            Catalog.Register(
                new PieceDefinition { Id = "2x8x20", Name = "2x8 x 20'", Category = PieceCategory.Lumber, Size = new Vector3(Units.In(7.25f), Units.In(1.5f), Units.In(240f)), Price = 27, Cuttable = true, Color = Pine },
                new PieceDefinition { Id = "2x10x20", Name = "2x10 x 20'", Category = PieceCategory.Lumber, Size = new Vector3(Units.In(9.25f), Units.In(1.5f), Units.In(240f)), Price = 36, Cuttable = true, Color = Pine },
                new PieceDefinition { Id = "2x12x20", Name = "2x12 x 20'", Category = PieceCategory.Lumber, Size = new Vector3(Units.In(11.25f), Units.In(1.5f), Units.In(240f)), Price = 46, Cuttable = true, Color = Pine });
        }

        /// <summary>Longest stock (meters) of a size prefix ("2x4", "pt2x6"...), 16' if none is in the catalog.</summary>
        public static float LongestStock(string size)
        {
            float best = 0f;
            foreach (var d in Catalog.All)
                if (d.Category == PieceCategory.Lumber && d.Id != null && d.Id.StartsWith(size + "x") && d.Size.z > best) best = d.Size.z;
            return best > 0f ? best : 16 * Units.Foot;
        }

        /// <summary>Splits a run [0, len] (any unit) into pieces no longer than maxLen. Joints go at the farthest 'allowed' position
        /// that fits (null = anywhere), at least minOffset away from every 'avoid' position (joints of the layer below).
        /// A run that fits in one board comes back as one piece.</summary>
        public static List<(float a, float b)> Segments(float len, float maxLen, IList<float> allowed = null, IList<float> avoid = null, float minOffset = 0f)
        {
            var segs = new List<(float a, float b)>();
            float eps = maxLen * 1e-4f, minPiece = maxLen * 0.1f;
            float start = 0f;
            for (int guard = 0; guard < 64 && len - start > maxLen + eps; guard++)
            {
                float joint = -1f;
                if (allowed != null)
                {
                    foreach (var p in allowed)
                        if (p > start + minPiece && p <= start + maxLen + eps && p < len - eps && Clear(p, avoid, minOffset) && p > joint) joint = p;
                }
                else
                {
                    // anywhere: as long as possible, then back off until clear of the joints below
                    for (float p = start + maxLen; p > start + minPiece; p -= maxLen * 0.01f)
                        if (Clear(p, avoid, minOffset)) { joint = p; break; }
                }
                if (joint < 0f) joint = start + maxLen; // nothing allowed fits: cut at the board length
                segs.Add((start, joint));
                start = joint;
            }
            segs.Add((start, len));
            return segs;
        }

        static bool Clear(float p, IList<float> avoid, float minOffset)
        {
            if (avoid == null || minOffset <= 0f) return true;
            foreach (var q in avoid) if (Mathf.Abs(p - q) < minOffset) return false;
            return true;
        }

        /// <summary>Interior joints of a segment list.</summary>
        public static List<float> Joints(List<(float a, float b)> segs)
        {
            var j = new List<float>();
            for (int i = 1; i < segs.Count; i++) j.Add(segs[i].a);
            return j;
        }
    }
}
