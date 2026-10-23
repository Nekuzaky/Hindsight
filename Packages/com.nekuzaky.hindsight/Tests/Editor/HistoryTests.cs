using System;
using System.Collections.Generic;
using Nekuzaky.Hindsight.Editor;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace Nekuzaky.Hindsight.Tests
{
    /// <summary>Drives a <see cref="History"/> by hand the way the recorder does, and keeps the ground truth.</summary>
    internal sealed class SyntheticRecording : IDisposable
    {
        public SyntheticRecording(int slots, int streams, int transformCapacity, int valueCapacity, double window)
        {
            Slots = slots;
            Streams = streams;
            History = new History(transformCapacity, valueCapacity, 4096, 256) { WindowSeconds = window };
            State = new NativeArray<TransformSample>(slots, Allocator.Persistent);
            Values = new NativeArray<Vector4>(Math.Max(1, streams), Allocator.Persistent);
            for (var i = 0; i < slots; i++)
                State[i] = TransformSample.Unset;
            for (var i = 0; i < Values.Length; i++)
                Values[i] = ValueSample.Unset;
        }

        public int Slots { get; }
        public int Streams { get; }
        public History History { get; }
        public NativeArray<TransformSample> State;
        public NativeArray<Vector4> Values;
        public readonly Dictionary<int, TransformSample[]> TruthTransforms = new();
        public readonly Dictionary<int, Vector4[]> TruthValues = new();

        public static TransformSample Expected(int slot, int frame)
        {
            // Even slots move every frame, odd slots only every fifth frame.
            var step = slot % 2 == 0 ? frame : frame / 5;
            return new TransformSample
            {
                Position = new Vector3(slot, step * 0.5f, -step),
                Rotation = Quaternion.Euler(0f, step * 3f, 0f),
                Scale = Vector3.one * (1f + slot * 0.01f)
            };
        }

        public static Vector4 ExpectedValue(int stream, int frame) => new(stream, frame / 3, 0f, 0f);

        public int Record(int frame, double time)
        {
            var sequence = History.BeginFrame(new FrameClock(frame, time, time, 1f), Slots, Streams);
            var moved = 0;
            for (var slot = 0; slot < Slots; slot++)
            {
                if (!TransformSample.Same(Expected(slot, frame), State[slot]))
                    moved++;
            }

            var block = new TransformBlockWriter(History.Transforms.Items, History.Transforms.Head, moved);
            for (var slot = 0; slot < Slots; slot++)
            {
                var sample = Expected(slot, frame);
                if (TransformSample.Same(sample, State[slot]))
                    continue;
                block.Add(slot, sample, State[slot]);
                State[slot] = sample;
            }
            var written = block.Finish();

            for (var stream = 0; stream < Streams; stream++)
            {
                var value = ExpectedValue(stream, frame);
                if (ValueSample.Same(value, Values[stream]))
                    continue;
                Values[stream] = value;
                History.AppendValue(stream, value);
            }

            History.EndFrame(written, moved, State, Slots, Values, Streams);
            TruthTransforms[sequence] = State.ToArray();
            TruthValues[sequence] = Values.ToArray();
            return sequence;
        }

        public void Dispose()
        {
            History.Dispose();
            State.Dispose();
            Values.Dispose();
        }
    }

    public class HistoryTests
    {
        [Test]
        public void Seek_RebuildsEveryKeptFrameExactly()
        {
            using var recording = new SyntheticRecording(32, 4, 1 << 16, 1 << 12, 1000d);
            for (var frame = 0; frame < 300; frame++)
                recording.Record(frame, frame / 60d);

            AssertSeeksMatchTruth(recording);
        }

        [Test]
        public void Window_DropsOldFramesAndOldestIsAlwaysSeekable()
        {
            using var recording = new SyntheticRecording(16, 2, 1 << 16, 1 << 12, 2d);
            for (var frame = 0; frame < 600; frame++)
                recording.Record(frame, frame / 60d);

            var history = recording.History;
            Assert.Greater(history.OldestSequence, 0, "Frames older than the window must be dropped.");
            Assert.GreaterOrEqual(history.Duration, 2d - 1.01d, "At least the window minus one checkpoint interval is kept.");
            Assert.LessOrEqual(history.Duration, 2d + 1.01d, "No more than the window plus one checkpoint interval is kept.");
            AssertSeeksMatchTruth(recording);
        }

        [Test]
        public void SmallRing_EvictsUnderPressureWithoutLosingConsistency()
        {
            // 64 slots, every even slot moving: 32 deltas per frame in a ring of 4096 entries.
            using var recording = new SyntheticRecording(64, 2, 4096, 4096, 1000d);
            for (var frame = 0; frame < 2000; frame++)
                recording.Record(frame, frame / 60d);

            var history = recording.History;
            Assert.Greater(history.OldestSequence, 0, "The ring is too small for everything; old frames must go.");
            Assert.LessOrEqual(history.Transforms.Count, history.Transforms.Capacity);
            AssertSeeksMatchTruth(recording);
        }

        [Test]
        public void ExtractSlotAndStream_MatchTheTruthFrameByFrame()
        {
            using var recording = new SyntheticRecording(8, 3, 1 << 14, 1 << 12, 1000d);
            for (var frame = 0; frame < 240; frame++)
                recording.Record(frame, frame / 60d);

            var history = recording.History;
            var samples = new TransformSample[history.FrameCount];
            var values = new Vector4[history.FrameCount];
            for (var slot = 0; slot < recording.Slots; slot++)
            {
                Assert.AreEqual(history.FrameCount, history.ExtractSlot(slot, samples));
                for (var i = 0; i < samples.Length; i++)
                {
                    var truth = recording.TruthTransforms[history.OldestSequence + i][slot];
                    Assert.IsTrue(TransformSample.Same(truth, samples[i]), $"Slot {slot}, frame {i}");
                }
            }

            for (var stream = 0; stream < recording.Streams; stream++)
            {
                Assert.AreEqual(history.FrameCount, history.ExtractStream(stream, values));
                for (var i = 0; i < values.Length; i++)
                    Assert.AreEqual(recording.TruthValues[history.OldestSequence + i][stream], values[i], $"Stream {stream}, frame {i}");
            }
        }

        [Test]
        public void Seek_OutsideTheHistoryFails()
        {
            using var recording = new SyntheticRecording(4, 0, 4096, 4096, 1000d);
            recording.Record(0, 0d);
            var transforms = new NativeArray<TransformSample>(4, Allocator.TempJob);
            var values = new NativeArray<Vector4>(1, Allocator.TempJob);
            try
            {
                Assert.IsTrue(recording.History.Seek(0, transforms, 4, values, 0));
                Assert.IsFalse(recording.History.Seek(1, transforms, 4, values, 0));
                Assert.IsFalse(recording.History.Seek(-1, transforms, 4, values, 0));
            }
            finally
            {
                transforms.Dispose();
                values.Dispose();
            }
        }

        private static void AssertSeeksMatchTruth(SyntheticRecording recording)
        {
            var history = recording.History;
            var transforms = new NativeArray<TransformSample>(recording.Slots, Allocator.TempJob);
            var values = new NativeArray<Vector4>(Math.Max(1, recording.Streams), Allocator.TempJob);
            try
            {
                for (var sequence = history.OldestSequence; sequence <= history.NewestSequence; sequence++)
                {
                    Assert.IsTrue(history.Seek(sequence, transforms, recording.Slots, values, recording.Streams));
                    var truth = recording.TruthTransforms[sequence];
                    for (var slot = 0; slot < recording.Slots; slot++)
                        Assert.IsTrue(TransformSample.Same(truth[slot], transforms[slot]), $"Sequence {sequence}, slot {slot}");

                    var truthValues = recording.TruthValues[sequence];
                    for (var stream = 0; stream < recording.Streams; stream++)
                        Assert.AreEqual(truthValues[stream], values[stream], $"Sequence {sequence}, stream {stream}");
                }
            }
            finally
            {
                transforms.Dispose();
                values.Dispose();
            }
        }
    }
}
