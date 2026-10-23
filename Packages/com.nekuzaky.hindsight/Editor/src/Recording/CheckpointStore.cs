using System;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// Full copies of the recorded state taken every second or so, so seeking never replays more than a short
    /// stretch of deltas. Buffers are reused when old checkpoints are dropped.
    /// </summary>
    internal sealed class CheckpointStore : IDisposable
    {
        #region Public

        public CheckpointStore(int capacity)
        {
            Debug.Assert(capacity >= 2, "Seeking needs at least two checkpoints.");
            _entries = new Entry[capacity];
        }

        public int Count { get; private set; }

        public int Capacity => _entries.Length;

        public bool IsFull => Count == Capacity;

        public long SizeInBytes
        {
            get
            {
                long total = 0;
                for (var i = 0; i < _entries.Length; i++)
                {
                    if (_entries[i].Transforms.IsCreated)
                        total += (long)_entries[i].Transforms.Length * TransformBytes;
                    if (_entries[i].Values.IsCreated)
                        total += (long)_entries[i].Values.Length * ValueBytes;
                }
                return total;
            }
        }

        #endregion


        #region Private and Protected

        private const int TransformBytes = 40;
        private const int ValueBytes = 16;
        private const int MinimumBuffer = 64;

        private struct Entry
        {
            public int Sequence;
            public int SlotCount;
            public int StreamCount;
            public NativeArray<TransformSample> Transforms;
            public NativeArray<Vector4> Values;
        }

        private readonly Entry[] _entries;
        private int _start;

        #endregion


        #region Main API

        public int SequenceAt(int index)
        {
            Debug.Assert(index >= 0 && index < Count, "Checkpoint index out of range.");
            return _entries[(_start + index) % Capacity].Sequence;
        }

        public void Add(int sequence, NativeArray<TransformSample> transforms, int slotCount,
            NativeArray<Vector4> values, int streamCount)
        {
            Debug.Assert(!IsFull, "Drop the oldest checkpoint before adding one to a full store.");
            Debug.Assert(Count == 0 || sequence > SequenceAt(Count - 1), "Checkpoints must be added in order.");

            ref var entry = ref _entries[(_start + Count) % Capacity];
            Reserve(ref entry.Transforms, slotCount);
            Reserve(ref entry.Values, streamCount);
            NativeArray<TransformSample>.Copy(transforms, entry.Transforms, slotCount);
            NativeArray<Vector4>.Copy(values, entry.Values, streamCount);
            entry.Sequence = sequence;
            entry.SlotCount = slotCount;
            entry.StreamCount = streamCount;
            Count++;
        }

        /// <summary>Drops the checkpoints taken after <paramref name="sequence"/>.</summary>
        public void RemoveAfter(int sequence)
        {
            for (var guard = Count; guard > 0 && Count > 0 && SequenceAt(Count - 1) > sequence; guard--)
                Count--;
        }

        public void RemoveOldest()
        {
            Debug.Assert(Count > 0, "No checkpoint to remove.");
            _start = (_start + 1) % Capacity;
            Count--;
        }

        public void Clear()
        {
            _start = 0;
            Count = 0;
        }

        /// <summary>Index of the newest checkpoint taken at or before <paramref name="sequence"/>, or -1.</summary>
        public int FindAtOrBefore(int sequence)
        {
            int low = 0, high = Count - 1, found = -1;
            while (low <= high)
            {
                var middle = (low + high) >> 1;
                if (SequenceAt(middle) <= sequence)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return found;
        }

        /// <summary>Writes the checkpoint into the output buffers; slots created after it are reset to unset.</summary>
        public void CopyTo(int index, NativeArray<TransformSample> transforms, int slotCount,
            NativeArray<Vector4> values, int streamCount)
        {
            Debug.Assert(transforms.Length >= slotCount && values.Length >= streamCount, "Output buffers too small.");
            ref readonly var entry = ref _entries[(_start + index) % Capacity];

            var copiedSlots = Math.Min(entry.SlotCount, slotCount);
            NativeArray<TransformSample>.Copy(entry.Transforms, transforms, copiedSlots);
            for (var i = copiedSlots; i < slotCount; i++)
                transforms[i] = TransformSample.Unset;

            var copiedStreams = Math.Min(entry.StreamCount, streamCount);
            NativeArray<Vector4>.Copy(entry.Values, values, copiedStreams);
            for (var i = copiedStreams; i < streamCount; i++)
                values[i] = ValueSample.Unset;
        }

        public TransformSample TransformAt(int index, int slot)
        {
            ref readonly var entry = ref _entries[(_start + index) % Capacity];
            return slot < entry.SlotCount ? entry.Transforms[slot] : TransformSample.Unset;
        }

        public Vector4 ValueAt(int index, int stream)
        {
            ref readonly var entry = ref _entries[(_start + index) % Capacity];
            return stream < entry.StreamCount ? entry.Values[stream] : ValueSample.Unset;
        }

        public void WriteTo(BinaryWriter writer, byte[] buffer)
        {
            writer.Write(Count);
            for (var i = 0; i < Count; i++)
            {
                ref readonly var entry = ref _entries[(_start + i) % Capacity];
                writer.Write(entry.Sequence);
                writer.Write(entry.SlotCount);
                writer.Write(entry.StreamCount);
                NativeIO.Write(writer, entry.Transforms, 0, entry.SlotCount, buffer);
                NativeIO.Write(writer, entry.Values, 0, entry.StreamCount, buffer);
            }
        }

        public void ReadFrom(BinaryReader reader, byte[] buffer, int maxSlots, int maxStreams)
        {
            Clear();
            var count = NativeIO.CheckCount(reader.ReadInt32(), Capacity, "checkpoints");
            for (var i = 0; i < count; i++)
            {
                ref var entry = ref _entries[i];
                entry.Sequence = reader.ReadInt32();
                entry.SlotCount = NativeIO.CheckCount(reader.ReadInt32(), maxSlots, "slots in a checkpoint");
                entry.StreamCount = NativeIO.CheckCount(reader.ReadInt32(), maxStreams, "streams in a checkpoint");
                Reserve(ref entry.Transforms, entry.SlotCount);
                Reserve(ref entry.Values, entry.StreamCount);
                NativeIO.Read(reader, entry.Transforms, 0, entry.SlotCount, buffer);
                NativeIO.Read(reader, entry.Values, 0, entry.StreamCount, buffer);
                if (i > 0 && entry.Sequence <= _entries[i - 1].Sequence)
                    throw new InvalidDataException("The recording is damaged: checkpoints out of order.");
                Count++;
            }
        }

        public void Dispose()
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Transforms.IsCreated)
                    _entries[i].Transforms.Dispose();
                if (_entries[i].Values.IsCreated)
                    _entries[i].Values.Dispose();
            }
            Count = 0;
        }

        #endregion


        #region Tools and Utilities

        private static void Reserve<T>(ref NativeArray<T> buffer, int count) where T : struct
        {
            if (buffer.IsCreated && buffer.Length >= count)
                return;
            if (buffer.IsCreated)
                buffer.Dispose();
            buffer = new NativeArray<T>(Math.Max(MinimumBuffer, Mathf.NextPowerOfTwo(count)), Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        #endregion
    }
}
