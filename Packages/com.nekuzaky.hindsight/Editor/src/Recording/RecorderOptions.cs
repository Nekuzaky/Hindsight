using System;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    internal readonly struct RecorderOptions
    {
        #region Public

        public const int TransformWordBytes = 4;
        public const int ValueDeltaBytes = 20;
        public const int MinimumRingEntries = 4096;
        public const int DefaultMaxFrames = 1 << 17;

        public RecorderOptions(double historySeconds, long memoryBudgetBytes, double captureInterval, int maxObjects,
            bool recordVelocities, bool recordTrackedFields, bool ignoreStatic, bool ignoreCameras, int layerMask,
            bool markLifecycle = true)
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
            MarkLifecycle = markLifecycle;
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
        public bool MarkLifecycle { get; }

        public int TransformCapacity => Clamp(MemoryBudgetBytes * 70 / 100 / TransformWordBytes);

        public int ValueCapacity => Clamp(MemoryBudgetBytes * 15 / 100 / ValueDeltaBytes);

        public int SlotLimit => Mathf.Max(1, Mathf.Min(Mathf.Min(MaxObjects, TransformCodec.MaxSlots), TransformCapacity / 4 / TransformCodec.MaxWordsPerSlot - 1));

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
                settings.LayerMask,
                settings.MarkObjectLifecycle);
        }

        #endregion


        #region Main API

        public static RecorderOptions ForFile(double historySeconds, long transformWords, long valueEntries, int checkpoints, int slots)
        {
            var transformBudget = (transformWords + MinimumRingEntries) * TransformWordBytes * 100 / 70 + 1;
            var valueBudget = (valueEntries + MinimumRingEntries) * ValueDeltaBytes * 100 / 15 + 1;
            var seconds = Math.Max(Math.Max(historySeconds, 1d), (checkpoints - 64) / 2d + 1d);
            return new RecorderOptions(seconds, Math.Max(transformBudget, valueBudget), 0d, Math.Max(1, slots),
                true, true, true, false, ~0, false);
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
