using System;
using System.Collections.Generic;
using Nekuzaky.Hindsight.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nekuzaky.Hindsight.Tests
{
    public class RecorderTests
    {
        private readonly List<Object> _created = new();
        private Recorder _recorder;
        private Rewind _rewind;

        [TearDown]
        public void TearDown()
        {
            _rewind?.Dispose();
            _recorder?.Dispose();
            _rewind = null;
            _recorder = null;
            foreach (var created in _created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        [Test]
        public void RecordsMotionAndRewindsTheScene()
        {
            var cubes = Spawn(10);
            Start();
            for (var frame = 0; frame < 60; frame++)
            {
                for (var i = 0; i < cubes.Length; i++)
                    cubes[i].transform.position = new Vector3(i, frame * 0.1f, 0f);
                Capture(frame);
            }

            Assert.AreEqual(60, _recorder.History.FrameCount);
            Assert.IsTrue(_rewind.Show(20));
            Assert.IsTrue(_rewind.IsActive);
            for (var i = 0; i < cubes.Length; i++)
                AssertNear(new Vector3(i, 2f, 0f), cubes[i].transform.position);

            _rewind.ReturnToLive();
            Assert.IsFalse(_rewind.IsActive);
            for (var i = 0; i < cubes.Length; i++)
                AssertNear(new Vector3(i, 5.9f, 0f), cubes[i].transform.position);
        }

        [Test]
        public void ReadingWhileACaptureIsPendingIsSafe()
        {
            var cubes = Spawn(3);
            Start();
            for (var frame = 0; frame < 5; frame++)
            {
                cubes[0].transform.position = new Vector3(frame, 0f, 0f);
                Capture(frame);
            }

            // Like the window does while the game runs: a capture job is scheduled and not collected yet.
            cubes[0].transform.position = new Vector3(42f, 0f, 0f);
            _recorder.EndFrame(new FrameClock(5, 5 / 60d, 5 / 60d, 1f));
            Assert.IsTrue(_recorder.HasPendingFrame);

            Assert.IsTrue(_recorder.TryGetSlot(cubes[0].transform, out var slot));
            Assert.DoesNotThrow(() => Assert.AreEqual(42f, _rewind.Transforms[slot].Position.x));
            Assert.DoesNotThrow(() => _ = _rewind.Values);
            var samples = new TransformSample[16];
            Assert.DoesNotThrow(() => _recorder.ExtractSlot(slot, samples));
            Assert.IsFalse(_recorder.HasPendingFrame);
        }

        [Test]
        public void HidesObjectsThatDidNotExistYet()
        {
            Spawn(1);
            Start();
            GameObject late = null;
            for (var frame = 0; frame < 40; frame++)
            {
                if (frame == 30)
                {
                    late = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _created.Add(late);
                    _recorder.MarkHierarchyDirty();
                }
                Capture(frame);
            }

            Assert.IsTrue(_recorder.TryGetSlot(late.transform, out var slot));
            Assert.AreEqual(30, _recorder.BirthOf(slot));

            var renderer = late.GetComponent<Renderer>();
            _rewind.Show(10);
            Assert.IsTrue(renderer.forceRenderingOff, "An object spawned later must be hidden in the past.");
            _rewind.Show(35);
            Assert.IsFalse(renderer.forceRenderingOff);
            _rewind.Show(5);
            _rewind.ReturnToLive();
            Assert.IsFalse(renderer.forceRenderingOff, "Going live must restore every renderer.");
        }

        [Test]
        public void DestroyedObjectsDieAndTheirSlotIsReusedOnceForgotten()
        {
            var doomed = Spawn(1)[0];
            Start(historySeconds: 1d);
            Capture(0);
            Assert.IsTrue(_recorder.TryGetSlot(doomed.transform, out var slot));

            Object.DestroyImmediate(doomed);
            Capture(1);
            Assert.AreEqual(1, _recorder.DeathOf(slot));
            Assert.IsTrue(_recorder.ExistedAt(slot, 0));
            Assert.IsFalse(_recorder.ExistedAt(slot, 1));

            for (var frame = 2; frame < 400; frame++)
                Capture(frame);

            var replacement = Spawn(1)[0];
            _recorder.MarkHierarchyDirty();
            Capture(400);
            Assert.IsTrue(_recorder.TryGetSlot(replacement.transform, out var reused));
            Assert.AreEqual(slot, reused, "A slot nobody remembers is recycled instead of growing the arrays.");
            Assert.AreEqual(400, _recorder.BirthOf(reused));
        }

        [Test]
        public void NewObjectsAreRecordedOnTheFrameTheyAppear()
        {
            var container = Spawn(1)[0];
            Start();
            Capture(0);
            var bullet = new GameObject("Bullet");
            _created.Add(bullet);
            var nested = new GameObject("Nested Bullet");
            _created.Add(nested);
            nested.transform.SetParent(container.transform, false);
            Capture(1);

            Assert.IsTrue(_recorder.TryGetSlot(bullet.transform, out var rootSlot), "A new root object is found without any hint.");
            Assert.IsTrue(_recorder.TryGetSlot(nested.transform, out var childSlot), "A new child object is found without any hint.");
            Assert.AreEqual(1, _recorder.BirthOf(rootSlot));
            Assert.AreEqual(1, _recorder.BirthOf(childSlot));
        }

        [Test]
        public void ObjectSwappedUnderTheSameParentIsFoundByTheSweep()
        {
            var container = Spawn(1)[0];
            var first = new GameObject("First");
            var last = new GameObject("Last");
            first.transform.SetParent(container.transform, false);
            last.transform.SetParent(container.transform, false);
            _created.Add(first);
            _created.Add(last);
            Start();
            Capture(0);

            // Same count and same last child: only the sweep can notice the replacement.
            Object.DestroyImmediate(first);
            var replacement = new GameObject("Replacement");
            _created.Add(replacement);
            replacement.transform.SetParent(container.transform, false);
            replacement.transform.SetSiblingIndex(0);
            for (var frame = 1; frame <= Recorder.SweepPeriod + 1; frame++)
                Capture(frame);

            Assert.IsTrue(_recorder.TryGetSlot(replacement.transform, out _));
        }

        [Test]
        public void SamplesRigidbodyVelocityWhileTheBodyMoves()
        {
            var ball = Spawn(1)[0];
            var body = ball.AddComponent<Rigidbody>();
            body.useGravity = false;
            Start();
            for (var frame = 0; frame < 10; frame++)
            {
                body.linearVelocity = new Vector3(frame, 0f, 0f);
                ball.transform.position = new Vector3(frame, 0f, 0f);
                Capture(frame);
            }

            Assert.IsTrue(_recorder.TryGetSlot(ball.transform, out var slot));
            var streams = new List<int>();
            _recorder.GetStreams(slot, streams);
            Assert.AreEqual(StreamKind.LinearVelocity, _recorder.KindOf(streams[0]));

            var values = new Vector4[_recorder.History.FrameCount];
            _recorder.History.ExtractStream(streams[0], values);
            Assert.AreEqual(7f, values[7].x, 1e-4f);
        }

        [Test]
        public void SamplesTrackedFieldsEveryFrame()
        {
            var host = Spawn(1)[0];
            var probe = host.AddComponent<TrackedProbe>();
            Start();
            for (var frame = 0; frame < 20; frame++)
            {
                probe.Health = 100f - frame;
                probe.CurrentMode = (TrackedProbe.ProbeMode)(frame % 3);
                probe.Aim = new Vector2(frame, -frame);
                Capture(frame);
            }

            Assert.IsTrue(_recorder.TryGetSlot(host.transform, out var slot));
            var streams = new List<int>();
            _recorder.GetStreams(slot, streams);
            var labels = new List<string>();
            foreach (var stream in streams)
                labels.Add(_recorder.LabelOf(stream));
            CollectionAssert.AreEquivalent(new[] { "Health", "Mode", "Aim" }, labels);

            var health = streams[labels.IndexOf("Health")];
            var aim = streams[labels.IndexOf("Aim")];
            var values = new Vector4[_recorder.History.FrameCount];
            _recorder.History.ExtractStream(health, values);
            Assert.AreEqual(88f, values[12].x);
            _recorder.History.ExtractStream(aim, values);
            Assert.AreEqual(new Vector4(12f, -12f, 0f, 0f), values[12]);
        }

        [Test]
        public void SteadyStateRecordingDoesNotAllocate()
        {
            var cubes = Spawn(200);
            Start();
            for (var frame = 0; frame < 120; frame++)
                Move(cubes, frame);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 120; frame < 420; frame++)
                Move(cubes, frame);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "Recording must not create garbage once warmed up.");
        }

        [Test]
        public void TrackedMembers_ResolvePathsAndRejectUnsupportedTypes()
        {
            var chain = new List<System.Reflection.FieldInfo>();
            Assert.IsTrue(TrackedMembers.TryResolvePath(typeof(TrackedProbe), "Nested.Ammo", chain));
            Assert.AreEqual(2, chain.Count);
            Assert.IsFalse(TrackedMembers.TryResolvePath(typeof(TrackedProbe), "Nested.Name", chain));
            Assert.IsFalse(TrackedMembers.TryResolvePath(typeof(TrackedProbe), "Missing", chain));
            Assert.IsFalse(TrackedMembers.TryResolvePath(typeof(TrackedProbe), "list.Array.data[0]", chain));
        }

        [Test]
        public void PlayerLoopHook_UninstallRestoresTheLoop()
        {
            var before = CountSystems();
            PlayerLoopHook.Install(() => { }, () => { }, () => { });
            Assert.AreEqual(before + 3, CountSystems());
            PlayerLoopHook.Uninstall();
            Assert.AreEqual(before, CountSystems());
        }

        private void Move(GameObject[] cubes, int frame)
        {
            for (var i = 0; i < cubes.Length; i++)
                cubes[i].transform.localPosition = new Vector3(i, Mathf.Sin(frame * 0.1f + i), 0f);
            Capture(frame);
        }

        private void Start(double historySeconds = 30d)
        {
            var options = new RecorderOptions(historySeconds, 64L * 1024 * 1024, 0d, 100000, true, true, true, false, ~0);
            _recorder = new Recorder(options);
            _rewind = new Rewind(_recorder);
        }

        private void Capture(int frame)
        {
            _recorder.CaptureNow(new FrameClock(frame, frame / 60d, frame / 60d, 1f));
        }

        private GameObject[] Spawn(int count)
        {
            var objects = new GameObject[count];
            for (var i = 0; i < count; i++)
            {
                objects[i] = new GameObject($"Hindsight Test {i}");
                _created.Add(objects[i]);
            }
            return objects;
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.Less(Vector3.Distance(expected, actual), 1e-4f, $"Expected {expected}, got {actual}");
        }

        private static int CountSystems()
        {
            var total = 0;
            foreach (var phase in UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop().subSystemList)
                total += phase.subSystemList?.Length ?? 0;
            return total;
        }
    }
}
