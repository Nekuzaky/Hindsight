using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>Colours used by custom drawing. They match the variables in Hindsight.uss.</summary>
    internal static class Palette
    {
        public static readonly Color Background = Rgb(17, 19, 24);
        public static readonly Color Lane = Rgb(23, 26, 32);
        public static readonly Color LaneAlt = Rgb(27, 30, 37);
        public static readonly Color Border = Rgb(44, 48, 58);
        public static readonly Color Grid = new(1f, 1f, 1f, 0.05f);
        public static readonly Color GridStrong = new(1f, 1f, 1f, 0.11f);
        public static readonly Color Text = Rgb(230, 233, 239);
        public static readonly Color TextDim = Rgb(139, 146, 162);
        public static readonly Color Accent = Rgb(139, 124, 255);
        public static readonly Color AccentSoft = new(139f / 255f, 124f / 255f, 1f, 0.22f);
        public static readonly Color Live = Rgb(61, 220, 132);
        public static readonly Color Recording = Rgb(255, 77, 94);
        public static readonly Color Error = Rgb(255, 92, 92);
        public static readonly Color Warning = Rgb(255, 176, 32);
        public static readonly Color Log = Rgb(139, 146, 162);
        public static readonly Color Bookmark = Rgb(56, 189, 248);
        public static readonly Color Activity = new(139f / 255f, 124f / 255f, 1f, 0.55f);
        public static readonly Color Unrecorded = new(0f, 0f, 0f, 0.28f);
        public static readonly Color Hover = new(1f, 1f, 1f, 0.35f);

        public static Color Of(MarkerKind kind)
        {
            switch (kind)
            {
                case MarkerKind.Warning: return Warning;
                case MarkerKind.Log: return Log;
                case MarkerKind.Bookmark: return Bookmark;
                default: return Error;
            }
        }

        public static string GlyphOf(MarkerKind kind)
        {
            switch (kind)
            {
                case MarkerKind.Exception: return Glyphs.Exception;
                case MarkerKind.Warning: return Glyphs.Warning;
                case MarkerKind.Log: return Glyphs.Log;
                case MarkerKind.Bookmark: return Glyphs.Bookmark;
                default: return Glyphs.Error;
            }
        }

        public static string ClassOf(MarkerKind kind)
        {
            switch (kind)
            {
                case MarkerKind.Warning: return "hs-kind--warning";
                case MarkerKind.Log: return "hs-kind--log";
                case MarkerKind.Bookmark: return "hs-kind--bookmark";
                default: return "hs-kind--error";
            }
        }

        private static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);
    }
}
