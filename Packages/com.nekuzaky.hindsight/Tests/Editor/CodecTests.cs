using Nekuzaky.Hindsight.Editor;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace Nekuzaky.Hindsight.Tests
{
    public class CodecTests
    {
        private static readonly TransformSample Origin = new()
        {
            Position = new Vector3(1f, 2f, 3f),
            Rotation = Quaternion.Euler(10f, 20f, 30f),
            Scale = Vector3.one
        };

        [Test]
        public void OnlyChangedComponentsAreWritten()
        {
            var moved = Origin;
            moved.Position.x += 0.25f;
            moved.Position.y -= 1f;
            Assert.AreEqual(0b11, TransformCodec.Diff(moved, Origin));
            Assert.AreEqual(TransformCodec.FullMask, TransformCodec.Diff(Origin, TransformSample.Unset), "A first sample is written whole.");
            Assert.AreEqual(0, TransformCodec.Diff(Origin, Origin));
        }

        [Test]
        public void BlocksRoundTripBitForBit()
        {
            var ring = new NativeArray<uint>(64, Allocator.Temp);
            var states = new NativeArray<TransformSample>(3, Allocator.Temp);
            try
            {
                var spun = Origin;
                spun.Rotation = Quaternion.Euler(10f, 21.5f, 30f);
                var shrunk = Origin;
                shrunk.Scale = new Vector3(-0f, 0.5f, float.Epsilon);

                var writer = new TransformBlockWriter(ring, 60, 2);
                writer.Add(0, spun, Origin);
                writer.Add(2, shrunk, Origin);
                var words = writer.Finish();
                Assert.AreEqual(2 + 2 * 2 + CountBits(TransformCodec.Diff(spun, Origin)) + 3, words);

                for (var i = 0; i < states.Length; i++)
                    states[i] = Origin;
                TransformCodec.ApplyBlock(ring, 60, states, states.Length);
                AssertSameBits(spun, states[0]);
                AssertSameBits(Origin, states[1]);
                AssertSameBits(shrunk, states[2]);
            }
            finally
            {
                ring.Dispose();
                states.Dispose();
            }
        }

        [Test]
        public void NothingMovedWritesNothing()
        {
            var ring = new NativeArray<uint>(16, Allocator.Temp);
            try
            {
                Assert.AreEqual(0, new TransformBlockWriter(ring, 0, 0).Finish());
            }
            finally
            {
                ring.Dispose();
            }
        }

        [Test]
        public void ObjectsSlidingOnAPlaneCostSixteenBytes()
        {
            const int slots = 100;
            var ring = new NativeArray<uint>(4096, Allocator.Temp);
            try
            {
                var writer = new TransformBlockWriter(ring, 0, slots);
                for (var slot = 0; slot < slots; slot++)
                {
                    var next = Origin;
                    next.Position.x += 1f;
                    next.Position.y += 1f;
                    writer.Add(slot, next, Origin);
                }
                var bytes = (writer.Finish() - TransformCodec.HeaderWords) * 4;
                Assert.AreEqual(16 * slots, bytes, "Slot, meta and two floats per object, against 44 bytes before.");
            }
            finally
            {
                ring.Dispose();
            }
        }

        private static int CountBits(int mask)
        {
            var count = 0;
            for (var bit = 0; bit < TransformCodec.ComponentCount; bit++)
                count += (mask >> bit) & 1;
            return count;
        }

        private static void AssertSameBits(in TransformSample expected, in TransformSample actual)
        {
            for (var component = 0; component < TransformCodec.ComponentCount; component++)
            {
                var a = System.BitConverter.SingleToInt32Bits(TransformCodec.Get(expected, component));
                var b = System.BitConverter.SingleToInt32Bits(TransformCodec.Get(actual, component));
                Assert.AreEqual(a, b, $"Component {component} differs.");
            }
        }
    }
}
