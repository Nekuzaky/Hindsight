using System.Collections.Generic;
using System.IO;
using Nekuzaky.Hindsight.Editor;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nekuzaky.Hindsight.Tests
{
    public class RecordingFileTests
    {
        private readonly List<Object> _created = new();
        private readonly List<Recorder> _recorders = new();
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), $"hindsight-test-{System.Guid.NewGuid():N}.{RecordingFile.Extension}");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var recorder in _recorders)
                recorder.Dispose();
            _recorders.Clear();
            foreach (var created in _created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
            _created.Clear();
            if (File.Exists(_path))
                File.Delete(_path);
        }

        [Test]
        public void SavedRecordingsOpenWithEveryFrameIntact()
        {
            var parent = Spawn("Saved Parent", null);
            var child = Spawn("Saved Child", parent.transform);
            var probe = child.AddComponent<TrackedProbe>();
            var original = Record(90, frame =>
            {
                parent.transform.position = new Vector3(frame * 0.1f, 0f, 0f);
                child.transform.localRotation = Quaternion.Euler(0f, frame * 4f, 0f);
                probe.Health = 100f - frame;
                if (frame == 40)
                    AddBookmark(frame);
            });
            var expected = SeekAll(original);

            RecordingFile.Save(original, _path);
            var opened = Load();

            Assert.IsTrue(opened.IsFromFile);
            Assert.AreEqual(original.History.OldestSequence, opened.History.OldestSequence);
            Assert.AreEqual(original.History.NewestSequence, opened.History.NewestSequence);
            var actual = SeekAll(opened);
            for (var i = 0; i < expected.Count; i++)
                CollectionAssert.AreEqual(expected[i], actual[i], $"Frame {i} differs after reopening.");

            Assert.AreEqual(original.Markers.Count, opened.Markers.Count);
            Assert.AreEqual("Saved bookmark", opened.Markers[opened.Markers.Count - 1].Message);
            Assert.IsTrue(opened.TryGetSlot(child.transform, out var slot), "Objects are found again by their place in the hierarchy.");
            var streams = new List<int>();
            opened.GetStreams(slot, streams);
            var labels = streams.ConvertAll(opened.LabelOf);
            CollectionAssert.AreEquivalent(new[] { "Health", "Mode", "Aim" }, labels);
            var mode = streams[labels.IndexOf("Mode")];
            Assert.AreEqual(typeof(TrackedProbe.ProbeMode), opened.EnumTypeOf(mode), "Enum types survive saving.");
            Assert.AreEqual(0, opened.MissingObjects);
        }

        [Test]
        public void OpenedRecordingsRewindTheSceneAndPutItBack()
        {
            var cube = Spawn("Rewound From File", null);
            var original = Record(30, frame => cube.transform.position = new Vector3(0f, frame, 0f));
            RecordingFile.Save(original, _path);
            cube.transform.position = new Vector3(5f, 5f, 5f);

            var opened = Load();
            using var rewind = new Rewind(opened);
            Assert.IsTrue(rewind.Show(opened.History.OldestSequence + 10));
            Assert.AreEqual(10f, cube.transform.position.y, 1e-4f);
            rewind.ReturnToLive();
            Assert.AreEqual(new Vector3(5f, 5f, 5f), cube.transform.position);
        }

        [Test]
        public void ObjectsMissingFromTheSceneAreCountedAndLeftAlone()
        {
            var gone = Spawn("Created While Playing", null);
            var original = Record(10, frame => gone.transform.position = Vector3.one * frame);
            RecordingFile.Save(original, _path);
            Object.DestroyImmediate(gone);

            var opened = Load();
            Assert.AreEqual(1, opened.MissingObjects);
            using var rewind = new Rewind(opened);
            Assert.IsTrue(rewind.Show(opened.History.OldestSequence + 3), "Rewinding skips objects that are not there.");
        }

        [Test]
        public void DamagedFilesAreRefused()
        {
            File.WriteAllBytes(_path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            Assert.Throws<InvalidDataException>(() => RecordingFile.Load(_path, out _));

            var original = Record(10, _ => { });
            RecordingFile.Save(original, _path);
            var bytes = File.ReadAllBytes(_path);
            File.WriteAllBytes(_path, bytes[..(bytes.Length / 2)]);
            Assert.Catch<IOException>(() => RecordingFile.Load(_path, out _), "A truncated file must fail cleanly.");
        }

        private Recorder _recording;

        private void AddBookmark(int frame) => _recording.AddMarker(MarkerKind.Bookmark, "Saved bookmark", string.Empty, frame / 60d);

        private Recorder Record(int frames, System.Action<int> move)
        {
            var options = new RecorderOptions(30d, 64L * 1024 * 1024, 0d, 100000, true, true, true, false, ~0);
            _recording = new Recorder(options);
            _recorders.Add(_recording);
            for (var frame = 0; frame < frames; frame++)
            {
                move(frame);
                _recording.CaptureNow(new FrameClock(frame, frame / 60d, frame / 60d, 1f));
            }
            return _recording;
        }

        private Recorder Load()
        {
            var recorder = RecordingFile.Load(_path, out var header);
            _recorders.Add(recorder);
            Assert.AreEqual(Application.unityVersion, header.UnityVersion);
            return recorder;
        }

        private static List<TransformSample[]> SeekAll(Recorder recorder)
        {
            var history = recorder.History;
            var result = new List<TransformSample[]>();
            var states = new NativeArray<TransformSample>(recorder.SlotCount, Allocator.TempJob);
            var values = new NativeArray<Vector4>(System.Math.Max(1, recorder.StreamCount), Allocator.TempJob);
            try
            {
                for (var sequence = history.OldestSequence; sequence <= history.NewestSequence; sequence++)
                {
                    Assert.IsTrue(history.Seek(sequence, states, recorder.SlotCount, values, recorder.StreamCount));
                    result.Add(states.ToArray());
                }
            }
            finally
            {
                states.Dispose();
                values.Dispose();
            }
            return result;
        }

        private GameObject Spawn(string name, Transform parent)
        {
            var created = new GameObject(name);
            created.transform.SetParent(parent, false);
            if (parent == null)
                _created.Add(created);
            return created;
        }
    }
}
