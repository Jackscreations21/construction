using System.Collections.Generic;
using UnityEngine;

namespace Groundbreak
{
    /// <summary>
    /// 5x7 pixel font built from boxes (signs, billboards, storefronts). Text lies on a plane facing +Z (readable from +Z
    /// looking toward -Z, so successive letters go toward -X), centred on bottomCenter (bottom of the glyph row).
    /// Letters are boxes 0.02 m thick from bottomCenter.z toward +Z. Pixels are merged into rectangles (horizontal runs,
    /// and identical runs stacked over consecutive rows) to keep the triangle count low. Lower case is drawn as upper case;
    /// unknown characters advance like a space.
    /// </summary>
    public static class MapText
    {
        public const float Depth = 0.02f;

        static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            { 'A', new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" } },
            { 'B', new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" } },
            { 'C', new[] { "01110", "10001", "10000", "10000", "10000", "10001", "01110" } },
            { 'D', new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" } },
            { 'E', new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" } },
            { 'F', new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" } },
            { 'G', new[] { "01110", "10001", "10000", "10111", "10001", "10001", "01111" } },
            { 'H', new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" } },
            { 'I', new[] { "01110", "00100", "00100", "00100", "00100", "00100", "01110" } },
            { 'J', new[] { "00111", "00010", "00010", "00010", "00010", "10010", "01100" } },
            { 'K', new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" } },
            { 'L', new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" } },
            { 'M', new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" } },
            { 'N', new[] { "10001", "10001", "11001", "10101", "10011", "10001", "10001" } },
            { 'O', new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" } },
            { 'P', new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" } },
            { 'Q', new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" } },
            { 'R', new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" } },
            { 'S', new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" } },
            { 'T', new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" } },
            { 'U', new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" } },
            { 'V', new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" } },
            { 'W', new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" } },
            { 'X', new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" } },
            { 'Y', new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" } },
            { 'Z', new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" } },
            { '0', new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" } },
            { '1', new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" } },
            { '2', new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" } },
            { '3', new[] { "11111", "00010", "00100", "00010", "00001", "10001", "01110" } },
            { '4', new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" } },
            { '5', new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" } },
            { '6', new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" } },
            { '7', new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" } },
            { '8', new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" } },
            { '9', new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" } },
            { '&', new[] { "01100", "10010", "10100", "01000", "10101", "10010", "01101" } },
            { '\'', new[] { "00100", "00100", "01000", "00000", "00000", "00000", "00000" } },
            { '.', new[] { "00000", "00000", "00000", "00000", "00000", "01100", "01100" } },
            { '-', new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" } },
            { '/', new[] { "00001", "00001", "00010", "00100", "01000", "10000", "10000" } },
            { ':', new[] { "00000", "01100", "01100", "00000", "01100", "01100", "00000" } },
            { '#', new[] { "01010", "01010", "11111", "01010", "11111", "01010", "01010" } },
            { '!', new[] { "00100", "00100", "00100", "00100", "00100", "00000", "00100" } },
            { '$', new[] { "00100", "01111", "10100", "01110", "00101", "11110", "00100" } },
        };

        /// <summary>Merged pixel rectangle: columns [C0, C1), rows R0..R1 inclusive (row 0 = top).</summary>
        struct Rect { public int C0, C1, R0, R1; }

        static readonly Dictionary<char, Rect[]> _rects = new Dictionary<char, Rect[]>();

        static Rect[] Rects(char ch)
        {
            if (_rects.TryGetValue(ch, out var r)) return r;
            if (!Glyphs.TryGetValue(ch, out var g)) { _rects[ch] = null; return null; }
            var done = new List<Rect>();
            var open = new List<Rect>();
            for (int row = 0; row < 7; row++)
            {
                string s = g[row];
                var next = new List<Rect>();
                int col = 0;
                while (col < 5)
                {
                    if (s[col] != '1') { col++; continue; }
                    int start = col;
                    while (col < 5 && s[col] == '1') col++;
                    int idx = open.FindIndex(o => o.C0 == start && o.C1 == col);
                    if (idx >= 0)
                    {
                        var o = open[idx]; o.R1 = row; next.Add(o); open.RemoveAt(idx);
                    }
                    else next.Add(new Rect { C0 = start, C1 = col, R0 = row, R1 = row });
                }
                done.AddRange(open);
                open = next;
            }
            done.AddRange(open);
            r = done.ToArray();
            _rects[ch] = r;
            return r;
        }

        /// <summary>Total width of the text line (metres) for pixel size px (advance 6 px, no trailing gap).</summary>
        public static float Width(string text, float px)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return (text.Length * 6 - 1) * px;
        }

        /// <summary>Draws the text line into b in the current frame (see class summary).</summary>
        public static void Draw(MapBuild b, string text, Vector3 bottomCenter, float px, int sub)
        {
            if (b == null || string.IsNullOrEmpty(text) || px <= 0f) return;
            float w = Width(text, px);
            float adv = 6f * px;
            float zc = bottomCenter.z + Depth * 0.5f;
            const int faces = MeshBuilder.AllFaces & ~MeshBuilder.FaceNegZ;   // back face is always against the sign
            for (int i = 0; i < text.Length; i++)
            {
                var rects = Rects(char.ToUpperInvariant(text[i]));
                if (rects == null) continue;
                float left = bottomCenter.x + w * 0.5f - i * adv;   // glyph's left edge as seen from +Z
                foreach (var r in rects)
                {
                    float xa = left - r.C0 * px, xb = left - r.C1 * px;
                    float yTop = bottomCenter.y + (7 - r.R0) * px, yBot = bottomCenter.y + (6 - r.R1) * px;
                    b.B.AddBox(new Vector3((xa + xb) * 0.5f, (yTop + yBot) * 0.5f, zc), new Vector3(xa - xb, yTop - yBot, Depth),
                               Quaternion.identity, sub, Vector3.right, faces);
                }
            }
        }
    }
}
