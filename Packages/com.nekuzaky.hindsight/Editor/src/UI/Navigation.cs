using UnityEditor;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>Moves the displayed frame. Shared by the window, the Scene view overlay and the shortcuts.</summary>
    internal static class Navigation
    {
        #region Public

        public static bool CanNavigate
        {
            get
            {
                var recorder = HindsightSession.Recorder;
                return recorder != null && !recorder.History.IsEmpty;
            }
        }

        public static int Displayed => HindsightSession.Rewind?.Sequence ?? -1;

        public static bool IsRewinding => HindsightSession.Rewind != null && HindsightSession.Rewind.IsActive;

        #endregion


        #region Main API

        public static void Show(int sequence)
        {
            var recorder = HindsightSession.Recorder;
            if (recorder == null || recorder.History.IsEmpty)
                return;

            var history = recorder.History;
            var clamped = sequence < history.OldestSequence ? history.OldestSequence
                : sequence > history.NewestSequence ? history.NewestSequence
                : sequence;
            HindsightSession.ShowFrame(clamped);
        }

        public static void StepBack() => Show(Displayed - 1);

        public static void StepForward()
        {
            if (!IsRewinding)
                return;
            Show(Displayed + 1);
        }

        public static void First()
        {
            if (CanNavigate)
                Show(HindsightSession.Recorder.History.OldestSequence);
        }

        public static void Live()
        {
            HindsightSession.ReturnToLive();
        }

        /// <summary>Jumps to the newest event before the displayed frame.</summary>
        public static void PreviousEvent(MarkerFilter filter)
        {
            if (!CanNavigate)
                return;

            var markers = HindsightSession.Recorder.Markers;
            var current = Displayed;
            for (var i = markers.Count - 1; i >= 0; i--)
            {
                ref readonly var marker = ref markers[i];
                if (marker.Sequence < current && filter.Accepts(marker.Kind))
                {
                    Show(marker.Sequence);
                    return;
                }
            }
        }

        /// <summary>Jumps to the oldest event after the displayed frame.</summary>
        public static void NextEvent(MarkerFilter filter)
        {
            if (!CanNavigate)
                return;

            var markers = HindsightSession.Recorder.Markers;
            var current = IsRewinding ? Displayed : int.MaxValue;
            for (var i = 0; i < markers.Count; i++)
            {
                ref readonly var marker = ref markers[i];
                if (marker.Sequence > current && filter.Accepts(marker.Kind))
                {
                    Show(marker.Sequence);
                    return;
                }
            }
        }

        public static void TogglePause()
        {
            if (EditorApplication.isPlaying)
                EditorApplication.isPaused = !EditorApplication.isPaused;
        }

        #endregion
    }

    /// <summary>Which kinds of event are visible and reachable with previous / next.</summary>
    internal struct MarkerFilter
    {
        public bool Errors;
        public bool Warnings;
        public bool Logs;
        public bool Bookmarks;

        public static MarkerFilter All => new() { Errors = true, Warnings = true, Logs = true, Bookmarks = true };

        public readonly bool Accepts(MarkerKind kind)
        {
            switch (kind)
            {
                case MarkerKind.Warning: return Warnings;
                case MarkerKind.Log: return Logs;
                case MarkerKind.Bookmark: return Bookmarks;
                default: return Errors;
            }
        }
    }
}
