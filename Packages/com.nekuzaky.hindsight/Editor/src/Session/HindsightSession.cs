using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Nekuzaky.Hindsight.Editor
{
    internal enum SessionStatus
    {
        Idle,
        Off,
        Recording,
        Paused,
        Rewinding,
        Viewing
    }

    [InitializeOnLoad]
    internal static class HindsightSession
    {
        #region Public

        public const string LogPrefix = "[Hindsight]";

        public static Recorder Recorder { get; private set; }

        public static Rewind Rewind { get; private set; }

        public static bool IsRecording => Recorder != null && OpenedFile == null;

        public static string OpenedFile { get; private set; }

        public static SessionStatus Status
        {
            get
            {
                if (OpenedFile != null)
                    return Rewind != null && Rewind.IsActive ? SessionStatus.Rewinding : SessionStatus.Viewing;
                if (!EditorApplication.isPlaying)
                    return SessionStatus.Idle;
                if (Recorder == null)
                    return SessionStatus.Off;
                if (!EditorApplication.isPaused)
                    return SessionStatus.Recording;
                return Rewind != null && Rewind.IsActive ? SessionStatus.Rewinding : SessionStatus.Paused;
            }
        }

        public static event Action StatusChanged;

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
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            HindsightSettings.Changed += OnSettingsChanged;

            if (EditorApplication.isPlaying)
                EditorApplication.delayCall += Start;
        }

        public static void Start()
        {
            if (!EditorApplication.isPlaying || Recorder != null || !HindsightSettings.instance.RecordInPlayMode)
                return;
            Debug.Assert(OpenedFile == null, "A saved recording is closed before Play Mode starts.");

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
            SceneManager.sceneLoaded += OnSceneLoaded;
            global::Nekuzaky.Hindsight.Hindsight.BookmarkRequested += OnBookmarkRequested;
            StatusChanged?.Invoke();
        }

        public static void Stop()
        {
            if (Recorder == null || OpenedFile != null)
                return;

            PlayerLoopHook.Uninstall();
            Application.logMessageReceived -= OnLogMessage;
            SceneManager.sceneLoaded -= OnSceneLoaded;
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

        public static bool ShowFrame(int sequence)
        {
            if (Recorder == null)
                return false;
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
                EditorApplication.isPaused = true;
            return Rewind.Show(sequence);
        }

        public static bool CanResumeFromShownFrame => IsRecording && EditorApplication.isPlaying && Rewind.IsActive;

        public static bool ResumeFromShownFrame()
        {
            if (!CanResumeFromShownFrame)
                return false;

            var sequence = Rewind.Sequence;
            var history = Recorder.History;
            var offset = history.Frame(sequence).RealTime - history.Frame(history.NewestSequence).RealTime;
            Recorder.ResumeAt(sequence, Rewind.Transforms, Rewind.Values);
            Recorder.RestoreVelocities(sequence, Rewind.Values);
            Rewind.Adopt();
            Recorder.AddMarker(MarkerKind.Bookmark, $"Resumed from {Format.Offset(offset)}", string.Empty, Time.unscaledTimeAsDouble);
            EditorApplication.isPaused = false;
            return true;
        }

        public static string Save(string path)
        {
            if (Recorder == null || Recorder.History.IsEmpty)
                return "Nothing is recorded yet.";
            try
            {
                RecordingFile.Save(Recorder, path);
                return null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return exception.Message;
            }
        }

        public static string Open(string path)
        {
            if (EditorApplication.isPlaying)
                return "Leave Play Mode to open a saved recording.";
            Recorder recorder;
            try
            {
                recorder = RecordingFile.Load(path, out _);
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException
                || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return exception.Message;
            }

            Close();
            Recorder = recorder;
            Rewind = new Rewind(recorder) { HideObjectsNotYetSpawned = HindsightSettings.instance.HideObjectsNotYetSpawned };
            Rewind.Changed += OnRewindChanged;
            OpenedFile = path;
            StatusChanged?.Invoke();
            return null;
        }

        public static void Close()
        {
            if (OpenedFile == null)
                return;
            Rewind.Changed -= OnRewindChanged;
            Rewind.Dispose();
            Rewind = null;
            Recorder.Dispose();
            Recorder = null;
            OpenedFile = null;
            StatusChanged?.Invoke();
        }

        public static void ReturnToLive() => Rewind?.ReturnToLive();

        public static bool AddBookmark(string label = null)
        {
            if (!IsRecording)
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
            else if (change == PlayModeStateChange.ExitingEditMode)
                Close();
            else
                StatusChanged?.Invoke();
        }

        private static void StopAll()
        {
            Stop();
            Close();
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            if (OpenedFile != null)
                Rewind.ReturnToLive();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => Close();

        private static void OnPauseStateChanged(PauseState state)
        {
            if (Recorder != null)
            {
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

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Recorder == null)
                return;
            Recorder.MarkHierarchyDirty();
            if (!HindsightSettings.instance.MarkSceneLoads)
                return;
            var text = mode == LoadSceneMode.Additive ? $"Scene loaded (additive): {scene.name}" : $"Scene loaded: {scene.name}";
            Recorder.AddMarker(MarkerKind.SceneLoad, text, string.Empty, Time.unscaledTimeAsDouble);
        }

        private static void OnRewindChanged()
        {
            FrameShown?.Invoke();
            StatusChanged?.Invoke();
        }

        #endregion
    }
}
