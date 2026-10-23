using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace Nekuzaky.Hindsight.Editor
{
    internal static class SlotFlag
    {
        public const byte Unchanged = 0;
        public const byte Changed = 1;
        public const byte Invalid = 2;
    }

    /// <summary>Reads every tracked transform on worker threads and flags the ones that moved.</summary>
    [BurstCompile]
    internal struct CaptureTransformsJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<TransformSample> Last;
        [WriteOnly] public NativeArray<TransformSample> Current;
        [WriteOnly] public NativeArray<byte> Flags;

        public void Execute(int index, TransformAccess transform)
        {
            if (!transform.isValid)
            {
                Flags[index] = SlotFlag.Invalid;
                return;
            }

            transform.GetLocalPositionAndRotation(out var position, out var rotation);
            var sample = new TransformSample { Position = position, Rotation = rotation, Scale = transform.localScale };
            Current[index] = sample;
            Flags[index] = TransformSample.Same(sample, Last[index]) ? SlotFlag.Unchanged : SlotFlag.Changed;
        }
    }

    /// <summary>
    /// Writes the changed components of the moved transforms into the ring as one block (see
    /// <see cref="TransformCodec"/>), reports destroyed objects and lists the slots whose velocity should be sampled
    /// (moved this frame or the frame before).
    /// </summary>
    [BurstCompile]
    internal struct PackTransformsJob : IJob
    {
        public int SlotCount;
        public long RingHead;
        public NativeArray<TransformSample> Last;
        [ReadOnly] public NativeArray<TransformSample> Current;
        [ReadOnly] public NativeArray<byte> Flags;
        public NativeArray<byte> Alive;
        public NativeArray<byte> Moving;
        public NativeArray<uint> Ring;
        [WriteOnly] public NativeArray<int> Died;
        [WriteOnly] public NativeArray<int> SampleSlots;
        public NativeArray<int> Counters;

        public void Execute()
        {
            int moved = 0, died = 0, sampled = 0;
            for (var slot = 0; slot < SlotCount; slot++)
            {
                if (Flags[slot] == SlotFlag.Changed)
                    moved++;
            }

            var block = new TransformBlockWriter(Ring, RingHead, moved);
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var flag = Flags[slot];
                if (flag == SlotFlag.Invalid)
                {
                    if (Alive[slot] == 1)
                    {
                        Alive[slot] = 0;
                        Died[died++] = slot;
                    }
                    Moving[slot] = 0;
                    continue;
                }

                var changed = flag == SlotFlag.Changed;
                if (changed)
                {
                    var value = Current[slot];
                    block.Add(slot, value, Last[slot]);
                    Last[slot] = value;
                }

                if (changed || Moving[slot] == 1)
                    SampleSlots[sampled++] = slot;
                Moving[slot] = changed ? (byte)1 : (byte)0;
            }

            Counters[RecorderCounters.Written] = block.Finish();
            Counters[RecorderCounters.Moved] = moved;
            Counters[RecorderCounters.Died] = died;
            Counters[RecorderCounters.Sampled] = sampled;
        }
    }

    internal static class RecorderCounters
    {
        public const int Written = 0;
        public const int Died = 1;
        public const int Sampled = 2;
        public const int Moved = 3;
        public const int Length = 4;
    }

    /// <summary>Copies the live transforms, used to come back to the present after rewinding.</summary>
    [BurstCompile]
    internal struct ReadTransformsJob : IJobParallelForTransform
    {
        [WriteOnly] public NativeArray<TransformSample> States;

        public void Execute(int index, TransformAccess transform)
        {
            if (!transform.isValid)
            {
                States[index] = TransformSample.Unset;
                return;
            }

            transform.GetLocalPositionAndRotation(out var position, out var rotation);
            States[index] = new TransformSample { Position = position, Rotation = rotation, Scale = transform.localScale };
        }
    }

    /// <summary>Moves objects to a recorded state. Slots with a zero mask or an unset sample are left alone.</summary>
    [BurstCompile]
    internal struct ApplyTransformsJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<TransformSample> States;
        [ReadOnly] public NativeArray<byte> Mask;

        public void Execute(int index, TransformAccess transform)
        {
            if (!transform.isValid || Mask[index] == 0)
                return;

            var state = States[index];
            if (!state.IsSet)
                return;

            transform.SetLocalPositionAndRotation(state.Position, state.Rotation);
            transform.localScale = state.Scale;
        }
    }

    /// <summary>Replays a contiguous run of transform blocks on top of a checkpoint.</summary>
    [BurstCompile]
    internal struct ReplayTransformsJob : IJob
    {
        [ReadOnly] public NativeArray<uint> Ring;
        public long Start;
        public long End;
        public int SlotCount;
        public NativeArray<TransformSample> States;

        public void Execute()
        {
            // A block is never shorter than its header, so the walk ends within (End - Start) / 2 steps.
            var block = Start;
            for (var step = 0L; step < End - Start && block < End; step++)
            {
                TransformCodec.ApplyBlock(Ring, block, States, SlotCount);
                block += TransformCodec.BlockWords(Ring, block);
            }
        }
    }

    /// <summary>Replays a contiguous range of value deltas on top of a checkpoint.</summary>
    [BurstCompile]
    internal struct ReplayValuesJob : IJob
    {
        [ReadOnly] public NativeArray<ValueDelta> Ring;
        public long Start;
        public long End;
        public int StreamCount;
        public NativeArray<Vector4> Values;

        public void Execute()
        {
            var capacity = Ring.Length;
            for (var index = Start; index < End; index++)
            {
                var delta = Ring[(int)(index % capacity)];
                if (delta.Stream < StreamCount)
                    Values[delta.Stream] = delta.Value;
            }
        }
    }
}
