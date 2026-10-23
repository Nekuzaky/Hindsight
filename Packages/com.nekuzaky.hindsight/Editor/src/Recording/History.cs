using System;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// The recording: frame records, transform and value deltas, and periodic checkpoints.
    /// The oldest kept frame is always a checkpoint, so any kept frame can be rebuilt from at most one
    /// checkpoint plus a short run of deltas. Memory is fixed when the session starts.
    /// </summary>
    internal sealed class History : IDisposable
    {
        #region Public

        public History(int transformCapacity, int valueCapacity, int maxFrames, int maxCheckpoints)
        {
            Debug.Assert(maxFrames >= 64, "History needs room for at least a few frames.");
            Transforms = new DeltaRing<uint>(transformCapacity);
            Values = new DeltaRing<ValueDelta>(valueCapacity);
            _frames = new FrameRecord[maxFrames];
            _checkpoints = new CheckpointStore(maxCheckpoints);
        }

        /// <summary>Transform blocks, one per frame that moved something. See <see cref="TransformCodec"/>.</summary>
        public DeltaRing<uint> Transforms { get; }

        public DeltaRing<ValueDelta> Values { get; }

        public double WindowSeconds { get; set; } = 30d;

        public double CheckpointInterval { get; set; } = 1d;

        public int OldestSequence { get; private set; }

        /// <summary>Newest committed frame, or <see cref="OldestSequence"/> - 1 when empty.</summary>
        public int NewestSequence => _next - 1;

        public int FrameCount => _next - OldestSequence;

        public bool IsEmpty => FrameCount == 0;

        public bool HasOpenFrame { get; private set; }

        public int MaxFrames => _frames.Length;

        public int CheckpointCount => _checkpoints.Count;

        /// <summary>Raised when old frames are dropped; the argument is the new oldest sequence.</summary>
        public event Action<int> Trimmed;

        public long SizeInBytes => Transforms.SizeInBytes + Values.SizeInBytes + _checkpoints.SizeInBytes
            + (long)_frames.Length * FrameRecordBytes;

        public double Duration => IsEmpty ? 0d : Frame(NewestSequence).RealTime - Frame(OldestSequence).RealTime;

        /// <summary>Bytes holding kept frames: live deltas plus checkpoints. Always at most <see cref="SizeInBytes"/>.</summary>
        public long UsedBytes => (long)Transforms.Count * RecorderOptions.TransformWordBytes
            + (long)Values.Count * RecorderOptions.ValueDeltaBytes + _checkpoints.SizeInBytes
            + (long)FrameCount * FrameRecordBytes;

        #endregion


        #region Private and Protected

        private const int FrameRecordBytes = 64;

        private readonly FrameRecord[] _frames;
        private readonly CheckpointStore _checkpoints;

        private int _next;
        private double _lastCheckpointTime;
        private long _transformsAtCheckpoint;
        private long _valuesAtCheckpoint;
        private int _sequenceAtCheckpoint;

        #endregion


        #region Main API

        public bool Contains(int sequence) => sequence >= OldestSequence && sequence < _next;

        /// <summary>The kept frame whose clock is closest to <paramref name="realTime"/>, or -1 when empty.</summary>
        public int SequenceNearest(double realTime)
        {
            if (IsEmpty)
                return -1;

            int low = OldestSequence, high = NewestSequence;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (Frame(middle).RealTime < realTime)
                    low = middle + 1;
                else
                    high = middle;
            }

            if (low > OldestSequence && realTime - Frame(low - 1).RealTime < Frame(low).RealTime - realTime)
                return low - 1;
            return low;
        }

        public ref readonly FrameRecord Frame(int sequence)
        {
            Debug.Assert(sequence >= OldestSequence && sequence <= _next, "Frame outside the kept range.");
            return ref _frames[sequence % _frames.Length];
        }

        /// <summary>Opens the frame the transform job is about to write, after making room for it.</summary>
        public int BeginFrame(in FrameClock clock, int slotCount, int streamCount)
        {
            Debug.Assert(!HasOpenFrame, "The previous frame was never committed.");
            var transformNeed = TransformCodec.WorstCaseWords(slotCount);
            Debug.Assert(slotCount <= TransformCodec.MaxSlots && transformNeed <= Transforms.Capacity / 4, "Too many slots for the transform ring.");
            Debug.Assert(streamCount <= Values.Capacity / 4, "Too many streams for the value ring.");

            MakeRoom(transformNeed, streamCount, clock.RealTime);
            ref var frame = ref _frames[_next % _frames.Length];
            frame = new FrameRecord
            {
                Sequence = _next,
                GameFrame = clock.GameFrame,
                GameTime = clock.GameTime,
                RealTime = clock.RealTime,
                TimeScale = clock.TimeScale,
                TransformStart = Transforms.Head,
                ValueStart = Values.Head
            };
            HasOpenFrame = true;
            return _next;
        }

        public void AppendValue(int stream, in Vector4 value)
        {
            Debug.Assert(HasOpenFrame, "Values can only be written into an open frame.");
            Values.Append(new ValueDelta { Stream = stream, Value = value });
        }

        /// <summary>Closes the open frame and takes a checkpoint when one is due.</summary>
        /// <param name="transformWords">Words of the transform block written at the ring head.</param>
        /// <param name="moved">Entries in that block.</param>
        public void EndFrame(int transformWords, int moved, NativeArray<TransformSample> state, int slotCount,
            NativeArray<Vector4> values, int streamCount)
        {
            Debug.Assert(HasOpenFrame, "No open frame to commit.");
            Debug.Assert((transformWords == 0) == (moved == 0), "A block exists exactly when something moved.");
            Transforms.Commit(transformWords);

            ref var frame = ref _frames[_next % _frames.Length];
            frame.TransformCount = moved;
            frame.TransformWords = transformWords;
            frame.ValueCount = (int)(Values.Head - frame.ValueStart);
            frame.Committed = true;
            var sequence = _next;
            _next++;
            HasOpenFrame = false;

            if (IsCheckpointDue(sequence, frame.RealTime))
                TakeCheckpoint(sequence, frame.RealTime, state, slotCount, values, streamCount);
        }

        /// <summary>Rebuilds every slot and stream at <paramref name="sequence"/>. Unknown ones come out unset.</summary>
        public bool Seek(int sequence, NativeArray<TransformSample> transforms, int slotCount,
            NativeArray<Vector4> values, int streamCount)
        {
            if (!Contains(sequence))
                return false;

            var checkpoint = _checkpoints.FindAtOrBefore(sequence);
            Debug.Assert(checkpoint >= 0, "The oldest kept frame must always be a checkpoint.");
            _checkpoints.CopyTo(checkpoint, transforms, slotCount, values, streamCount);

            var from = _checkpoints.SequenceAt(checkpoint);
            if (from == sequence)
                return true;

            ref readonly var first = ref Frame(from + 1);
            ref readonly var last = ref Frame(sequence);
            new ReplayTransformsJob
            {
                Ring = Transforms.Items,
                Start = first.TransformStart,
                End = last.TransformStart + last.TransformWords,
                SlotCount = slotCount,
                States = transforms
            }.Run();
            new ReplayValuesJob
            {
                Ring = Values.Items,
                Start = first.ValueStart,
                End = last.ValueStart + last.ValueCount,
                StreamCount = streamCount,
                Values = values
            }.Run();
            return true;
        }

        /// <summary>One transform per kept frame for a single slot, oldest first. Returns the number written.</summary>
        public int ExtractSlot(int slot, TransformSample[] output)
        {
            if (IsEmpty || _checkpoints.Count == 0)
                return 0;

            var count = Math.Min(FrameCount, output.Length);
            var state = _checkpoints.TransformAt(0, slot);
            output[0] = state;
            for (var i = 1; i < count; i++)
            {
                ref readonly var frame = ref Frame(OldestSequence + i);
                var entry = FindEntry(frame, slot);
                if (entry >= 0)
                    TransformCodec.ApplyEntry(Transforms.Items, frame.TransformStart, entry, ref state);
                output[i] = state;
            }
            return count;
        }

        /// <summary>One value per kept frame for a single stream, oldest first. Returns the number written.</summary>
        public int ExtractStream(int stream, Vector4[] output)
        {
            if (IsEmpty || _checkpoints.Count == 0)
                return 0;

            var count = Math.Min(FrameCount, output.Length);
            var starts = new NativeArray<long>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var lengths = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var result = new NativeArray<Vector4>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    ref readonly var frame = ref Frame(OldestSequence + i);
                    starts[i] = frame.ValueStart;
                    lengths[i] = frame.ValueCount;
                }

                new ExtractStreamJob
                {
                    Ring = Values.Items,
                    Starts = starts,
                    Lengths = lengths,
                    Stream = stream,
                    Initial = _checkpoints.ValueAt(0, stream),
                    Output = result
                }.Run();
                NativeArray<Vector4>.Copy(result, output, count);
                return count;
            }
            finally
            {
                starts.Dispose();
                lengths.Dispose();
                result.Dispose();
            }
        }

        /// <summary>
        /// Applies entry <paramref name="entry"/> of a frame to <paramref name="states"/>, for walks that follow every
        /// object frame by frame. Returns the slot it belongs to.
        /// </summary>
        public int ApplyEntry(in FrameRecord frame, int entry, NativeArray<TransformSample> states)
        {
            Debug.Assert(entry >= 0 && entry < frame.TransformCount, "Entry outside the frame.");
            var slot = TransformCodec.SlotAt(Transforms.Items, frame.TransformStart, entry);
            if (slot >= states.Length)
                return slot;
            var state = states[slot];
            TransformCodec.ApplyEntry(Transforms.Items, frame.TransformStart, entry, ref state);
            states[slot] = state;
            return slot;
        }

        public void Clear()
        {
            Transforms.Clear();
            Values.Clear();
            _checkpoints.Clear();
            OldestSequence = _next;
            HasOpenFrame = false;
            Trimmed?.Invoke(OldestSequence);
        }

        public void Dispose()
        {
            Transforms.Dispose();
            Values.Dispose();
            _checkpoints.Dispose();
        }

        /// <summary>Writes every kept frame, delta and checkpoint. The frame must not be open.</summary>
        public void WriteTo(BinaryWriter writer, byte[] buffer)
        {
            Debug.Assert(!HasOpenFrame, "Commit the pending frame before saving.");
            writer.Write(WindowSeconds);
            writer.Write(OldestSequence);
            writer.Write(_next);
            for (var sequence = OldestSequence; sequence < _next; sequence++)
                WriteFrame(writer, Frame(sequence));
            Transforms.WriteTo(writer, buffer);
            Values.WriteTo(writer, buffer);
            _checkpoints.WriteTo(writer, buffer);
        }

        public void ReadFrom(BinaryReader reader, byte[] buffer, int maxSlots, int maxStreams)
        {
            WindowSeconds = reader.ReadDouble();
            OldestSequence = reader.ReadInt32();
            _next = reader.ReadInt32();
            NativeIO.CheckCount((long)_next - OldestSequence, _frames.Length - 1, "frames");
            for (var sequence = OldestSequence; sequence < _next; sequence++)
                _frames[sequence % _frames.Length] = ReadFrame(reader, sequence);
            Transforms.ReadFrom(reader, buffer);
            Values.ReadFrom(reader, buffer);
            _checkpoints.ReadFrom(reader, buffer, maxSlots, maxStreams);
            HasOpenFrame = false;
            if (!IsEmpty && (_checkpoints.Count == 0 || _checkpoints.SequenceAt(0) != OldestSequence))
                throw new InvalidDataException("The recording is damaged: its oldest frame has no checkpoint.");
        }

        #endregion


        #region Tools and Utilities

        private static void WriteFrame(BinaryWriter writer, in FrameRecord frame)
        {
            writer.Write(frame.GameFrame);
            writer.Write(frame.GameTime);
            writer.Write(frame.RealTime);
            writer.Write(frame.TimeScale);
            writer.Write(frame.TransformStart);
            writer.Write(frame.TransformCount);
            writer.Write(frame.TransformWords);
            writer.Write(frame.ValueStart);
            writer.Write(frame.ValueCount);
        }

        private static FrameRecord ReadFrame(BinaryReader reader, int sequence)
        {
            return new FrameRecord
            {
                Sequence = sequence,
                GameFrame = reader.ReadInt32(),
                GameTime = reader.ReadDouble(),
                RealTime = reader.ReadDouble(),
                TimeScale = reader.ReadSingle(),
                TransformStart = reader.ReadInt64(),
                TransformCount = reader.ReadInt32(),
                TransformWords = reader.ReadInt32(),
                ValueStart = reader.ReadInt64(),
                ValueCount = reader.ReadInt32(),
                Committed = true
            };
        }

        private bool IsCheckpointDue(int sequence, double realTime)
        {
            return _checkpoints.Count == 0
                || realTime - _lastCheckpointTime >= CheckpointInterval
                // Transform blocks are compact, so this bound keeps seeks short when frames come very fast.
                || Transforms.Head - _transformsAtCheckpoint >= Transforms.Capacity / 32
                || Values.Head - _valuesAtCheckpoint >= Values.Capacity / 8
                || sequence - _sequenceAtCheckpoint >= _frames.Length / 8;
        }

        private void TakeCheckpoint(int sequence, double realTime, NativeArray<TransformSample> state, int slotCount,
            NativeArray<Vector4> values, int streamCount)
        {
            if (_checkpoints.IsFull)
                DropOldestSegment();

            _checkpoints.Add(sequence, state, slotCount, values, streamCount);
            _lastCheckpointTime = realTime;
            _transformsAtCheckpoint = Transforms.Head;
            _valuesAtCheckpoint = Values.Head;
            _sequenceAtCheckpoint = sequence;
        }

        private void MakeRoom(long transformNeed, int streamNeed, double now)
        {
            // Every pass drops one checkpoint, so the loop is bounded by the checkpoint count.
            var passes = _checkpoints.Count;
            for (var pass = 0; pass < passes; pass++)
            {
                var needSpace = Transforms.Free < transformNeed || Values.Free < streamNeed
                    || FrameCount >= _frames.Length - 1;
                var tooOld = _checkpoints.Count >= 2
                    && now - Frame(_checkpoints.SequenceAt(1)).RealTime >= WindowSeconds;
                if (!needSpace && !tooOld)
                    return;

                if (_checkpoints.Count < 2)
                {
                    Clear();
                    return;
                }
                DropOldestSegment();
            }

            if (Transforms.Free < transformNeed || Values.Free < streamNeed)
                Clear();
        }

        private void DropOldestSegment()
        {
            Debug.Assert(_checkpoints.Count >= 2, "Dropping a segment needs a checkpoint after it.");
            var nextStart = _checkpoints.SequenceAt(1);
            ref readonly var frame = ref Frame(nextStart);
            Transforms.ReleaseUntil(frame.TransformStart);
            Values.ReleaseUntil(frame.ValueStart);
            _checkpoints.RemoveOldest();
            OldestSequence = nextStart;
            Trimmed?.Invoke(OldestSequence);
        }

        /// <summary>Index of the slot's entry in a frame, or -1 when it did not move.</summary>
        private int FindEntry(in FrameRecord frame, int slot)
        {
            // Entries inside a frame are written in slot order, so a binary search finds the slot.
            int low = 0, high = frame.TransformCount - 1;
            while (low <= high)
            {
                var middle = (low + high) >> 1;
                var found = TransformCodec.SlotAt(Transforms.Items, frame.TransformStart, middle);
                if (found == slot)
                    return middle;
                if (found < slot)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return -1;
        }

        #endregion
    }

    /// <summary>Walks the value ring frame by frame and outputs the latest value of one stream per frame.</summary>
    [BurstCompile]
    internal struct ExtractStreamJob : IJob
    {
        [ReadOnly] public NativeArray<ValueDelta> Ring;
        [ReadOnly] public NativeArray<long> Starts;
        [ReadOnly] public NativeArray<int> Lengths;
        public int Stream;
        public Vector4 Initial;
        [WriteOnly] public NativeArray<Vector4> Output;

        public void Execute()
        {
            var capacity = Ring.Length;
            var state = Initial;
            Output[0] = state;
            for (var frame = 1; frame < Starts.Length; frame++)
            {
                var start = Starts[frame];
                var end = start + Lengths[frame];
                for (var index = start; index < end; index++)
                {
                    var delta = Ring[(int)(index % capacity)];
                    if (delta.Stream == Stream)
                        state = delta.Value;
                }
                Output[frame] = state;
            }
        }
    }
}
