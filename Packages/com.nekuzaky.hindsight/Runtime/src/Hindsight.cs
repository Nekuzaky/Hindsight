using System;
using System.Diagnostics;

namespace Nekuzaky.Hindsight
{
    public static class Hindsight
    {
        #region Public

        public const int MaxLabelLength = 256;

        #endregion


        #region Private and Protected

        internal static event Action<string> BookmarkRequested;

        #endregion


        #region Main API

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
