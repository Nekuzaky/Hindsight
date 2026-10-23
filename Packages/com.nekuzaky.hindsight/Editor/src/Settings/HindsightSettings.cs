using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    internal enum CaptureRate
    {
        EveryFrame = 0,
        Hz120 = 120,
        Hz60 = 60,
        Hz30 = 30,
        Hz15 = 15
    }

    [Serializable]
    internal struct TrackedPin
    {
        public string TypeName;
        public string Path;

        public TrackedPin(string typeName, string path)
        {
            TypeName = typeName;
            Path = path;
        }
    }

    [FilePath("UserSettings/HindsightSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class HindsightSettings : ScriptableSingleton<HindsightSettings>
    {
        #region Public

        public const float MinHistorySeconds = 5f;
        public const float MaxHistorySeconds = 600f;
        public const int MinMemoryBudgetMb = 32;
        public const int MaxMemoryBudgetMb = 8192;
        public const int MinTrackedObjects = 1000;
        public const int MaxTrackedObjects = 500000;
        public const int MaxPins = 256;

        public static event Action Changed;

        public bool RecordInPlayMode { get => _recordInPlayMode; set => Set(ref _recordInPlayMode, value); }
        public float HistorySeconds { get => _historySeconds; set => Set(ref _historySeconds, Mathf.Clamp(value, MinHistorySeconds, MaxHistorySeconds)); }
        public int MemoryBudgetMb { get => _memoryBudgetMb; set => Set(ref _memoryBudgetMb, Mathf.Clamp(value, MinMemoryBudgetMb, MaxMemoryBudgetMb)); }
        public CaptureRate CaptureRate { get => _captureRate; set => Set(ref _captureRate, value); }
        public int MaxObjects { get => _maxObjects; set => Set(ref _maxObjects, Mathf.Clamp(value, MinTrackedObjects, MaxTrackedObjects)); }
        public bool RecordVelocities { get => _recordVelocities; set => Set(ref _recordVelocities, value); }
        public bool RecordTrackedFields { get => _recordTrackedFields; set => Set(ref _recordTrackedFields, value); }
        public bool IgnoreStaticObjects { get => _ignoreStaticObjects; set => Set(ref _ignoreStaticObjects, value); }
        public bool IgnoreCameras { get => _ignoreCameras; set => Set(ref _ignoreCameras, value); }
        public int LayerMask { get => _layerMask; set => Set(ref _layerMask, value); }

        public bool RecordErrors { get => _recordErrors; set => Set(ref _recordErrors, value); }
        public bool RecordWarnings { get => _recordWarnings; set => Set(ref _recordWarnings, value); }
        public bool RecordLogs { get => _recordLogs; set => Set(ref _recordLogs, value); }
        public bool CollapseRepeats { get => _collapseRepeats; set => Set(ref _collapseRepeats, value); }
        public bool RecordEditorMessages { get => _recordEditorMessages; set => Set(ref _recordEditorMessages, value); }
        public bool MarkObjectLifecycle { get => _markObjectLifecycle; set => Set(ref _markObjectLifecycle, value); }
        public bool MarkSceneLoads { get => _markSceneLoads; set => Set(ref _markSceneLoads, value); }

        public bool HideObjectsNotYetSpawned { get => _hideNotYetSpawned; set => Set(ref _hideNotYetSpawned, value); }
        public bool ShowSceneOverlay { get => _showSceneOverlay; set => Set(ref _showSceneOverlay, value); }
        public bool ShowMotionTrail { get => _showMotionTrail; set => Set(ref _showMotionTrail, value); }
        public bool ShowDestroyedGhosts { get => _showDestroyedGhosts; set => Set(ref _showDestroyedGhosts, value); }
        public bool FollowSelectionInGraph { get => _followSelection; set => Set(ref _followSelection, value); }

        public int PinCount => _pins.Count;

        #endregion


        #region Private and Protected

        [SerializeField] private bool _recordInPlayMode = true;
        [SerializeField] private float _historySeconds = 30f;
        [SerializeField] private int _memoryBudgetMb = 512;
        [SerializeField] private CaptureRate _captureRate = CaptureRate.EveryFrame;
        [SerializeField] private int _maxObjects = 100000;
        [SerializeField] private bool _recordVelocities = true;
        [SerializeField] private bool _recordTrackedFields = true;
        [SerializeField] private bool _ignoreStaticObjects = true;
        [SerializeField] private bool _ignoreCameras;
        [SerializeField] private int _layerMask = ~0;

        [SerializeField] private bool _recordErrors = true;
        [SerializeField] private bool _recordWarnings = true;
        [SerializeField] private bool _recordLogs;
        [SerializeField] private bool _collapseRepeats = true;
        [SerializeField] private bool _recordEditorMessages;
        [SerializeField] private bool _markObjectLifecycle = true;
        [SerializeField] private bool _markSceneLoads = true;

        [SerializeField] private bool _hideNotYetSpawned = true;
        [SerializeField] private bool _showSceneOverlay = true;
        [SerializeField] private bool _showMotionTrail = true;
        [SerializeField] private bool _showDestroyedGhosts = true;
        [SerializeField] private bool _followSelection = true;

        [SerializeField] private List<TrackedPin> _pins = new();

        #endregion


        #region Main API

        public TrackedPin GetPin(int index)
        {
            Debug.Assert(index >= 0 && index < _pins.Count, "Pin index out of range.");
            return _pins[index];
        }

        public bool IsPinned(string typeName, string path) => IndexOfPin(typeName, path) >= 0;

        public bool AddPin(string typeName, string path)
        {
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(path) || _pins.Count >= MaxPins)
                return false;
            if (IsPinned(typeName, path))
                return false;

            _pins.Add(new TrackedPin(typeName, path));
            Commit();
            return true;
        }

        public bool RemovePin(string typeName, string path)
        {
            var index = IndexOfPin(typeName, path);
            if (index < 0)
                return false;

            _pins.RemoveAt(index);
            Commit();
            return true;
        }

        public void ClearPins()
        {
            if (_pins.Count == 0)
                return;
            _pins.Clear();
            Commit();
        }

        public void ResetToDefaults()
        {
            var pins = new List<TrackedPin>(_pins);
            var defaults = CreateInstance<HindsightSettings>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(defaults), this);
            DestroyImmediate(defaults);
            _pins = pins;
            Commit();
        }

        #endregion


        #region Tools and Utilities

        private int IndexOfPin(string typeName, string path)
        {
            for (var i = 0; i < _pins.Count; i++)
            {
                if (_pins[i].TypeName == typeName && _pins[i].Path == path)
                    return i;
            }
            return -1;
        }

        private void Set<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            Commit();
        }

        private void Commit()
        {
            Save(true);
            TrackedMembers.Invalidate();
            Changed?.Invoke();
        }

        #endregion
    }
}
