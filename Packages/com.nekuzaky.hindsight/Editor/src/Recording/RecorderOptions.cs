using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>Snapshot of the settings a recording session starts with.</summary>
    internal readonly struct RecorderOptions
    {
        #region Public

        public const int TransformDeltaBytes = 44;
        public const int ValueDeltaBytes = 20;
        public const int MinimumRingEntries = 4096;
        public const int DefaultMaxFrames = 1 << 17;

        public RecorderOptions(double historySeconds, long memoryBudgetBytes, double captureInterval, int maxObjects,
            bool recordVelocities, bool recordTrackedFields, bool ignoreStatic, bool ignoreCameras, int layerMask)
        {
            Debug.Assert(historySeconds > 0d && memoryBudgetBytes > 0, "History length and budget must be positive.");
            HistorySeconds = historySeconds;
            MemoryBudgetBytes = memoryBudgetBytes;
            CaptureInterval = captureInterval;
            MaxObjects = maxObjects;
            RecordVelocities = recordVelocities;
            RecordTrackedFields = recordTrackedFields;
            IgnoreStatic = ignoreStatic;
            IgnoreCameras = ignoreCameras;
            LayerMask = layerMask;
        }

        public double HistorySeconds { get; }
        public long MemoryBudgetBytes { get; }
        public double CaptureInterval { get; }
        public int MaxObjects { get; }
        public bool RecordVelocities { get; }
        public bool RecordTrackedFields { get; }
        public bool IgnoreStatic { get; }
        public bool IgnoreCameras { get; }
        public int LayerMask { get; }

        /// <summary>70 % of the budget goes to transform deltas, 15 % to values; checkpoints use the rest.</summary>
        public int TransformCapacity => Clamp(MemoryBudgetBytes * 70 / 100 / TransformDeltaBytes);

        public int ValueCapacity => Clamp(MemoryBudgetBytes * 15 / 100 / ValueDeltaBytes);

        /// <summary>Slots are capped so a frame where every object moves always fits in a quarter of the ring.</summary>
        public int SlotLimit => Mathf.Max(1, Mathf.Min(MaxObjects, TransformCapacity / 4));

        public int StreamLimit => Mathf.Max(1, ValueCapacity / 4);

        public int MaxCheckpoints => Mathf.Clamp((int)(HistorySeconds * 2d) + 64, 64, 4096);

        public static RecorderOptions FromSettings(HindsightSettings settings)
        {
            var rate = (int)settings.CaptureRate;
            return new RecorderOptions(
                settings.HistorySeconds,
                (long)settings.MemoryBudgetMb * 1024L * 1024L,
                rate <= 0 ? 0d : 1d / rate,
                settings.MaxObjects,
                settings.RecordVelocities,
                settings.RecordTrackedFields,
                settings.IgnoreStaticObjects,
                settings.IgnoreCameras,
                settings.LayerMask);
        }

        #endregion


        #region Tools and Utilities

        private static int Clamp(long entries)
        {
            const long max = int.MaxValue / 2;
            return (int)(entries < MinimumRingEntries ? MinimumRingEntries : entries > max ? max : entries);
        }

        #endregion
    }
}
