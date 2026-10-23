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
            var block = Start;
            for (var step = 0L; step < End - Start && block < End; step++)
            {
                TransformCodec.ApplyBlock(Ring, block, States, SlotCount);
                block += TransformCodec.BlockWords(Ring, block);
            }
        }
    }

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
