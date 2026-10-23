using System.Collections.Generic;
using Nekuzaky.Hindsight.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nekuzaky.Hindsight.Tests
{
    public class SearchTests
    {
        private readonly List<Object> _created = new();
        private Recorder _recorder;

        [TearDown]
        public void TearDown()
        {
            _recorder?.Dispose();
            _recorder = null;
            foreach (var created in _created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        [TestCase("y < -5", false, "y", (int)Comparison.Less, -5f)]
        [TestCase("  speed>=20.5 ", false, "speed", (int)Comparison.GreaterOrEqual, 20.5f)]
        [TestCase("any y < -10", true, "y", (int)Comparison.Less, -10f)]
        [TestCase("Is Grounded == false", false, "Is Grounded", (int)Comparison.Equal, 0f)]
        [TestCase("Health != 3", false, "Health", (int)Comparison.NotEqual, 3f)]
        public void Parse_ReadsConditions(string text, bool any, string channel, int comparison, float value)
        {
            Assert.IsTrue(ConditionQuery.TryParse(text, out var query, out var error), error);
            Assert.AreEqual(any, query.AnyObject);
            Assert.AreEqual(channel, query.Channel);
            Assert.AreEqual((Comparison)comparison, query.Comparison);
            Assert.AreEqual(value, query.Value);
        }

        [TestCase("")]
        [TestCase("y <")]
        [TestCase("< 3")]
        [TestCase("y < abc")]
        public void Parse_RejectsNonConditions(string text)
        {
            Assert.IsFalse(ConditionQuery.TryParse(text, out _, out var error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void FindsWhenAValueOfTheSelectedObjectBecomesTrue()
        {
            var cube = Spawn("Rising");
            Record(60, frame => cube.transform.position = new Vector3(0f, frame * 0.1f, 0f));
            Assert.IsTrue(_recorder.TryGetSlot(cube.transform, out var slot));

            var forward = Find("y > 2", slot, _recorder.History.OldestSequence - 1, true);
            Assert.IsTrue(forward.Found, forward.Message);
            Assert.AreEqual(21, forward.Sequence);

            var backward = Find("y > 2", slot, _recorder.History.NewestSequence + 1, false);
            Assert.AreEqual(21, backward.Sequence, "A value that stays true is found once, where it starts.");
            Assert.IsFalse(Find("y > 2", slot, 21, true).Found);
        }

        [Test]
        public void FindsEachTimeAnOscillatingValueCrossesAgain()
        {
            var cube = Spawn("Bouncing");
            Record(60, frame => cube.transform.position = new Vector3(0f, frame % 20 < 10 ? 0f : 5f, 0f));
            _recorder.TryGetSlot(cube.transform, out var slot);

            var first = Find("y > 1", slot, -1, true);
            var second = Find("y > 1", slot, first.Sequence, true);
            Assert.AreEqual(10, first.Sequence);
            Assert.AreEqual(30, second.Sequence);
        }

        [Test]
        public void AnySearchesThePositionOfEveryObject()
        {
            var up = Spawn("Up");
            var down = Spawn("Down");
            Record(60, frame =>
            {
                up.transform.position = new Vector3(0f, frame * 0.1f, 0f);
                down.transform.position = new Vector3(1f, -frame * 0.1f, 0f);
            });

            var result = Find("any y < -3", -1, _recorder.History.OldestSequence - 1, true);
            Assert.IsTrue(result.Found, result.Message);
            Assert.AreEqual(31, result.Sequence);
            Assert.IsTrue(_recorder.TryGetSlot(down.transform, out var downSlot));
            Assert.AreEqual(downSlot, result.Slot);
            StringAssert.Contains("Down", result.Message);
        }

        [Test]
        public void FindsTrackedFieldsByTheirLabel()
        {
            var host = Spawn("Probe");
            var probe = host.AddComponent<TrackedProbe>();
            Record(30, frame => probe.Health = 100f - frame);
            _recorder.TryGetSlot(host.transform, out var slot);

            var result = Find("health <= 90", slot, -1, true);
            Assert.IsTrue(result.Found, result.Message);
            Assert.AreEqual(10, result.Sequence);
        }

        [Test]
        public void ExplainsWhatIsMissing()
        {
            var cube = Spawn("Plain");
            Record(5, _ => { });
            _recorder.TryGetSlot(cube.transform, out var slot);

            var unknown = Find("mana < 3", slot, -1, true);
            Assert.IsFalse(unknown.Found);
            StringAssert.Contains("x, y, z", unknown.Message);
            Assert.IsFalse(Find("y < 3", -1, -1, true).Found, "Without a selection, only 'any' searches can run.");
            Assert.IsFalse(Find("any speed > 3", -1, -1, true).Found, "'any' only searches positions.");
        }

        private MomentFinder.Result Find(string text, int slot, int from, bool forward)
        {
            Assert.IsTrue(ConditionQuery.TryParse(text, out var query, out var error), error);
            return MomentFinder.Find(_recorder, query, slot, from, forward);
        }

        private void Record(int frames, System.Action<int> move)
        {
            var options = new RecorderOptions(30d, 64L * 1024 * 1024, 0d, 100000, true, true, true, false, ~0);
            _recorder = new Recorder(options);
            for (var frame = 0; frame < frames; frame++)
            {
                move(frame);
                _recorder.CaptureNow(new FrameClock(frame, frame / 60d, frame / 60d, 1f));
            }
        }

        private GameObject Spawn(string name)
        {
            var created = new GameObject(name);
            _created.Add(created);
            return created;
        }
    }
}
