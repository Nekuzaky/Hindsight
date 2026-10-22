using System;
using System.Diagnostics;

namespace Nekuzaky.Hindsight
{
    /// <summary>
    /// Calls your game code can make to annotate a Hindsight recording.
    /// Every call is removed by the compiler outside the Unity Editor, so it costs nothing in player builds.
    /// </summary>
    public static class Hindsight
    {
        #region Public

        /// <summary>Longest label kept for a bookmark. Longer labels are truncated.</summary>
        public const int MaxLabelLength = 256;

        #endregion


        #region Private and Protected

        internal static event Action<string> BookmarkRequested;

        #endregion


        #region Main API

        /// <summary>
        /// Adds a bookmark on the Hindsight timeline at the current frame, for example right before a rare branch
        /// you want to inspect. Call it from the main thread. Does nothing when Hindsight is not recording.
        /// </summary>
        /// <param name="label">Text shown on the timeline and in the event list.</param>
        [Conditional("UNITY_EDITOR")]
        public static void Bookmark(string label = null)
        {
            var handler = BookmarkRequested;
            if (handler == null)
                return;

            var text = label ?? string.Empty;
            if (text.Length > MaxLabelLength)
                text = text.Substring(0, MaxLabelLength);

            handler(text);
        }

        #endregion
    }
}
