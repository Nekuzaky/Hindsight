using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    internal static class Palette
    {
        public static readonly Color Background = Rgb(24, 24, 26);
        public static readonly Color Lane = Rgb(30, 30, 33);
        public static readonly Color LaneAlt = Rgb(34, 34, 37);
        public static readonly Color Border = Rgb(50, 50, 55);
        public static readonly Color Grid = new(1f, 1f, 1f, 0.05f);
        public static readonly Color GridStrong = new(1f, 1f, 1f, 0.11f);
        public static readonly Color Text = Rgb(220, 220, 224);
        public static readonly Color TextDim = Rgb(148, 148, 156);
        public static readonly Color Accent = Rgb(142, 132, 222);
        public static readonly Color AccentSoft = new(142f / 255f, 132f / 255f, 222f / 255f, 0.22f);
        public static readonly Color Live = Rgb(96, 190, 132);
        public static readonly Color Recording = Rgb(222, 86, 92);
        public static readonly Color Error = Rgb(226, 96, 96);
        public static readonly Color Warning = Rgb(222, 170, 72);
        public static readonly Color Log = Rgb(148, 148, 156);
        public static readonly Color Bookmark = Rgb(92, 166, 214);
        public static readonly Color Spawn = Rgb(88, 176, 160);
        public static readonly Color Destroy = Rgb(146, 146, 156);
        public static readonly Color SceneLoad = Rgb(208, 208, 214);
        public static readonly Color Activity = new(142f / 255f, 132f / 255f, 222f / 255f, 0.5f);
        public static readonly Color Unrecorded = new(0f, 0f, 0f, 0.28f);
        public static readonly Color Hover = new(1f, 1f, 1f, 0.35f);

        public static Color Of(MarkerKind kind)
        {
            switch (kind)
            {
                case MarkerKind.Warning: return Warning;
                case MarkerKind.Log: return Log;
                case MarkerKind.Bookmark: return Bookmark;
                case MarkerKind.Spawn: return Spawn;
                case MarkerKind.Destroy: return Destroy;
                case MarkerKind.SceneLoad: return SceneLoad;
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
                case MarkerKind.Spawn: return Glyphs.Spawned;
                case MarkerKind.Destroy: return Glyphs.Destroyed;
                case MarkerKind.SceneLoad: return Glyphs.Scene;
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
                case MarkerKind.Spawn: return "hs-kind--spawn";
                case MarkerKind.Destroy: return "hs-kind--destroy";
                case MarkerKind.SceneLoad: return "hs-kind--scene";
                default: return "hs-kind--error";
            }
        }

        private static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);
    }
}
