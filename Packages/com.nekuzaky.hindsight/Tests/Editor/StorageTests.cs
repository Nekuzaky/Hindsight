using Nekuzaky.Hindsight.Editor;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace Nekuzaky.Hindsight.Tests
{
    public class StorageTests
    {
        [Test]
        public void DeltaRing_TracksHeadTailAndWraps()
        {
            using var ring = new DeltaRing<ValueDelta>(4);
            for (var i = 0; i < 4; i++)
                ring.Append(new ValueDelta { Stream = i });

            Assert.AreEqual(4, ring.Count);
            Assert.AreEqual(0, ring.Free);

            ring.ReleaseUntil(2);
            ring.Append(new ValueDelta { Stream = 4 });
            ring.Append(new ValueDelta { Stream = 5 });

            Assert.AreEqual(4, ring.Count);
            Assert.AreEqual(2, ring.At(2).Stream);
            Assert.AreEqual(5, ring.At(5).Stream);
            Assert.AreEqual(1, ring.IndexOf(5));
        }

        [Test]
        public void CheckpointStore_FindsNewestAtOrBefore()
        {
            using var store = new CheckpointStore(8);
            var transforms = new NativeArray<TransformSample>(4, Allocator.Temp);
            var values = new NativeArray<Vector4>(2, Allocator.Temp);
            try
            {
                store.Add(0, transforms, 4, values, 2);
                store.Add(10, transforms, 4, values, 2);
                store.Add(20, transforms, 4, values, 2);

                Assert.AreEqual(-1, store.FindAtOrBefore(-1));
                Assert.AreEqual(0, store.FindAtOrBefore(9));
                Assert.AreEqual(1, store.FindAtOrBefore(10));
                Assert.AreEqual(2, store.FindAtOrBefore(500));

                store.RemoveOldest();
                Assert.AreEqual(10, store.SequenceAt(0));
            }
            finally
            {
                transforms.Dispose();
                values.Dispose();
            }
        }

        [Test]
        public void CheckpointStore_FillsSlotsCreatedLaterWithUnset()
        {
            using var store = new CheckpointStore(4);
            var source = new NativeArray<TransformSample>(2, Allocator.Temp);
            var values = new NativeArray<Vector4>(1, Allocator.Temp);
            var output = new NativeArray<TransformSample>(4, Allocator.Temp);
            var outputValues = new NativeArray<Vector4>(3, Allocator.Temp);
            try
            {
                source[1] = new TransformSample { Position = Vector3.one, Rotation = Quaternion.identity, Scale = Vector3.one };
                store.Add(0, source, 2, values, 1);
                store.CopyTo(0, output, 4, outputValues, 3);

                Assert.AreEqual(Vector3.one, output[1].Position);
                Assert.IsFalse(output[2].IsSet);
                Assert.IsFalse(output[3].IsSet);
                Assert.IsFalse(ValueSample.IsSet(outputValues[2]));
            }
            finally
            {
                source.Dispose();
                values.Dispose();
                output.Dispose();
                outputValues.Dispose();
            }
        }

        [Test]
        public void TransformSample_SameIsExactAndUnsetNeverMatches()
        {
            var a = new TransformSample { Position = Vector3.one, Rotation = Quaternion.identity, Scale = Vector3.one };
            var b = a;
            Assert.IsTrue(TransformSample.Same(a, b));

            b.Position.x += 1e-6f;
            Assert.IsFalse(TransformSample.Same(a, b));
            Assert.IsFalse(TransformSample.Same(TransformSample.Unset, TransformSample.Unset));
        }
    }
}
