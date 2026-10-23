using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Nekuzaky.Hindsight.Editor
{
    internal enum SessionStatus
    {
        /// <summary>The editor is not in Play Mode.</summary>
        Idle,
        /// <summary>In Play Mode, but recording is turned off.</summary>
        Off,
        Recording,
        /// <summary>The game is paused and the scene shows the live state.</summary>
        Paused,
        /// <summary>The game is paused and the scene shows a past frame.</summary>
        Rewinding
    }

    /// <summary>
    /// Starts a recorder when Play Mode starts and stops it when Play Mode ends, wires the player loop, the Console
    /// and bookmarks to it, and puts the scene back live before the game runs again. Works with and without
    /// domain reload on entering Play Mode.
    /// </summary>
    [InitializeOnLoad]
    internal static class HindsightSession
    {
        #region Public

        public const string LogPrefix = "[Hindsight]";

        public static Recorder Recorder { get; private set; }

        public static Rewind Rewind { get; private set; }

        public static bool IsRecording => Recorder != null;

        public static SessionStatus Status
        {
            get
            {
                if (!EditorApplication.isPlaying)
                    return SessionStatus.Idle;
                if (Recorder == null)
                    return SessionStatus.Off;
                if (!EditorApplication.isPaused)
                    return SessionStatus.Recording;
                return Rewind != null && Rewind.IsActive ? SessionStatus.Rewinding : SessionStatus.Paused;
            }
        }

        /// <summary>Recording started or stopped, the game paused or resumed, or rewinding began or ended.</summary>
        public static event Action StatusChanged;

        /// <summary>The scene now shows another frame.</summary>
        public static event Action FrameShown;

        #endregion


        #region Private and Protected

        private static GameObject _probe;
        private static long _pendingTicks;
        private static bool _insideFrame;

        #endregion


        #region Main API

        static HindsightSession()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.pauseStateChanged += OnPauseStateChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            HindsightSettings.Changed += OnSettingsChanged;

            // Scripts recompiled during Play Mode: pick recording up again.
            if (EditorApplication.isPlaying)
                EditorApplication.delayCall += Start;
        }

        /// <summary>Starts recording if Play Mode is on and recording is enabled.</summary>
        public static void Start()
        {
            if (!EditorApplication.isPlaying || Recorder != null || !HindsightSettings.instance.RecordInPlayMode)
                return;

            var settings = HindsightSettings.instance;
            TrackedMembers.Invalidate();
            Recorder = new Recorder(RecorderOptions.FromSettings(settings));
            Recorder.Markers.CollapseRepeats = settings.CollapseRepeats;
            Rewind = new Rewind(Recorder) { HideObjectsNotYetSpawned = settings.HideObjectsNotYetSpawned };
            Rewind.Changed += OnRewindChanged;

            _probe = new GameObject("Hindsight Probe") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(_probe);
            Recorder.SetPersistentSceneProbe(_probe);

            PlayerLoopHook.Install(OnFrameStart, OnAfterLateUpdate, OnFrameEnd);
            Application.logMessageReceived += OnLogMessage;
            global::Nekuzaky.Hindsight.Hindsight.BookmarkRequested += OnBookmarkRequested;
            StatusChanged?.Invoke();
        }

        /// <summary>Stops recording and frees its memory. The scene is put back live first.</summary>
        public static void Stop()
        {
            if (Recorder == null)
                return;

            PlayerLoopHook.Uninstall();
            Application.logMessageReceived -= OnLogMessage;
            global::Nekuzaky.Hindsight.Hindsight.BookmarkRequested -= OnBookmarkRequested;

            Rewind.Changed -= OnRewindChanged;
            Rewind.Dispose();
            Rewind = null;
            Recorder.Dispose();
            Recorder = null;

            if (_probe != null)
                Object.DestroyImmediate(_probe);
            _probe = null;
            StatusChanged?.Invoke();
        }

        /// <summary>Shows a recorded frame, pausing the game first if it is running.</summary>
        public static bool ShowFrame(int sequence)
        {
            if (Recorder == null)
                return false;
            if (!EditorApplication.isPaused)
                EditorApplication.isPaused = true;
            return Rewind.Show(sequence);
        }

        public static void ReturnToLive() => Rewind?.ReturnToLive();

        /// <summary>Adds a bookmark at the current frame. Returns false when nothing is recording.</summary>
        public static bool AddBookmark(string label = null)
        {
            if (Recorder == null)
                return false;
            Recorder.AddMarker(MarkerKind.Bookmark, label ?? string.Empty, string.Empty, Time.unscaledTimeAsDouble);
            return true;
        }

        #endregion


        #region Tools and Utilities

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
                Start();
            else if (change == PlayModeStateChange.ExitingPlayMode)
                Stop();
            else
                StatusChanged?.Invoke();
        }

        private static void OnPauseStateChanged(PauseState state)
        {
            if (Recorder != null)
            {
                // The game must never run on top of a rewound scene.
                if (state == PauseState.Unpaused)
                    Rewind.ReturnToLive();
                else
                    Recorder.Commit();
            }
            StatusChanged?.Invoke();
        }

        private static void OnHierarchyChanged() => Recorder?.MarkHierarchyDirty();

        private static void OnSettingsChanged()
        {
            var settings = HindsightSettings.instance;
            if (!EditorApplication.isPlaying)
                return;

            if (settings.RecordInPlayMode && Recorder == null)
                Start();
            else if (!settings.RecordInPlayMode && Recorder != null)
                Stop();

            if (Recorder != null)
            {
                Recorder.History.WindowSeconds = settings.HistorySeconds;
                Recorder.Markers.CollapseRepeats = settings.CollapseRepeats;
                Rewind.HideObjectsNotYetSpawned = settings.HideObjectsNotYetSpawned;
            }
        }

        private static void OnFrameStart()
        {
            if (Recorder == null)
                return;

            _insideFrame = true;
            var start = Stopwatch.GetTimestamp();
            Rewind.ReturnToLive();
            Recorder.Commit();
            _pendingTicks += Stopwatch.GetTimestamp() - start;
        }

        // Between the end of a frame and the start of the next one, the editor runs its own code (windows, inspectors,
        // importers): what it logs is not about the game. The end-of-frame system and the editor update both close
        // the frame, whichever comes first.
        private static void OnFrameEnd() => _insideFrame = false;

        private static void OnEditorUpdate() => _insideFrame = false;

        private static void OnAfterLateUpdate()
        {
            if (Recorder == null)
                return;

            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var before = Recorder.History.NewestSequence;
            Recorder.EndFrame(FrameClock.Now);
            var ticks = _pendingTicks + Stopwatch.GetTimestamp() - start;
            _pendingTicks = 0;

            if (Recorder.HasPendingFrame || Recorder.History.NewestSequence != before)
                Recorder.Stats.AddFrame(ticks, GC.GetAllocatedBytesForCurrentThread() - allocated);
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (Recorder == null || condition == null || condition.StartsWith(LogPrefix, StringComparison.Ordinal))
                return;

            // Messages raised by editor code between two frames (inspectors, importers, the editor itself) are
            // not about the game, so they stay out of the timeline unless asked for.
            var settings = HindsightSettings.instance;
            if (!_insideFrame && !settings.RecordEditorMessages)
                return;

            MarkerKind kind;
            switch (type)
            {
                case LogType.Error: kind = MarkerKind.Error; break;
                case LogType.Exception: kind = MarkerKind.Exception; break;
                case LogType.Assert: kind = MarkerKind.Assert; break;
                case LogType.Warning: kind = MarkerKind.Warning; break;
                default: kind = MarkerKind.Log; break;
            }

            var wanted = kind == MarkerKind.Warning ? settings.RecordWarnings
                : kind == MarkerKind.Log ? settings.RecordLogs
                : settings.RecordErrors;
            if (wanted)
                Recorder.AddMarker(kind, condition, stackTrace, Time.unscaledTimeAsDouble);
        }

        private static void OnBookmarkRequested(string label) => AddBookmark(label);

        private static void OnRewindChanged()
        {
            FrameShown?.Invoke();
            StatusChanged?.Invoke();
        }

        #endregion
    }
}
