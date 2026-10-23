using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>Bootstrap Icons shipped with the package, tinted through USS.</summary>
    internal static class Glyphs
    {
        #region Public

        public const string Logo = "clock-history";
        public const string Record = "record-circle-fill";
        public const string RecordOff = "record-circle";
        public const string Paused = "pause-circle-fill";
        public const string First = "skip-start-fill";
        public const string Live = "skip-end-fill";
        public const string StepBack = "chevron-left";
        public const string StepForward = "chevron-right";
        public const string PreviousEvent = "chevron-double-left";
        public const string NextEvent = "chevron-double-right";
        public const string Bookmark = "bookmark-fill";
        public const string AddBookmark = "bookmark-plus-fill";
        public const string Error = "x-octagon-fill";
        public const string Exception = "bug-fill";
        public const string Warning = "exclamation-triangle-fill";
        public const string Log = "info-circle-fill";
        public const string Settings = "gear-fill";
        public const string Docs = "book";
        public const string GitHub = "github";
        public const string Coffee = "cup-hot-fill";
        public const string Search = "search";
        public const string Filter = "funnel-fill";
        public const string Graph = "graph-up";
        public const string Object = "box";
        public const string Cost = "cpu";
        public const string Memory = "memory";
        public const string Time = "stopwatch";
        public const string Activity = "activity";
        public const string Speed = "lightning-charge-fill";
        public const string Pin = "pin-angle-fill";
        public const string Unpin = "pin-angle";
        public const string Remove = "trash3";
        public const string Reset = "arrow-counterclockwise";
        public const string Trail = "bezier2";
        public const string Hidden = "eye-slash-fill";
        public const string Visible = "eye-fill";
        public const string Play = "play-fill";
        public const string Pause = "pause-fill";
        public const string Rewind = "rewind-fill";
        public const string Fit = "arrows-angle-expand";
        public const string Frame = "crosshair";
        public const string Help = "question-circle";
        public const string Off = "slash-circle";
        public const string Close = "x-lg";
        public const string Layers = "layers";
        public const string Hierarchy = "diagram-2";
        public const string Copy = "clipboard";
        public const string Spawned = "plus-circle-fill";
        public const string Destroyed = "dash-circle-fill";
        public const string Scene = "map-fill";
        public const string Save = "floppy-fill";
        public const string OpenFile = "folder2-open";
        public const string Waiting = "hourglass-split";

        public static Texture2D WindowIcon
        {
            get
            {
                var name = EditorGUIUtility.isProSkin ? "hindsight-window-dark.png" : "hindsight-window-light.png";
                return AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{name}");
            }
        }

        #endregion


        #region Private and Protected

        private const string Root = "Packages/com.nekuzaky.hindsight/Editor/UI";
        private const string IconFolder = Root + "/Icons";

        private static readonly Dictionary<string, VectorImage> Cache = new();

        #endregion


        #region Main API

        public static VectorImage Get(string name)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null)
                return cached;

            var image = AssetDatabase.LoadAssetAtPath<VectorImage>($"{IconFolder}/{name}.svg");
            Debug.Assert(image != null, $"Missing Hindsight icon '{name}'.");
            Cache[name] = image;
            return image;
        }

        /// <summary>An icon element. Its colour comes from the <c>hs-icon</c> classes in the style sheet.</summary>
        public static VisualElement Icon(string name, params string[] classes)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("hs-icon");
            icon.style.backgroundImage = new StyleBackground(Get(name));
            foreach (var className in classes)
                icon.AddToClassList(className);
            return icon;
        }

        public static void Set(VisualElement icon, string name)
        {
            icon.style.backgroundImage = new StyleBackground(Get(name));
        }

        public static StyleSheet StyleSheet => AssetDatabase.LoadAssetAtPath<StyleSheet>($"{Root}/Hindsight.uss");

        #endregion
    }
}
