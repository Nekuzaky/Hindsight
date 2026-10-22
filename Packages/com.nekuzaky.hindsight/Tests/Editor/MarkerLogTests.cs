using Nekuzaky.Hindsight.Editor;
using NUnit.Framework;

namespace Nekuzaky.Hindsight.Tests
{
    public class MarkerLogTests
    {
        [Test]
        public void Add_KeepsOrderAndCounts()
        {
            var log = new MarkerLog(16);
            log.Add(MarkerKind.Error, "a", "", 1, 0d);
            log.Add(MarkerKind.Warning, "b", "", 2, 0d);
            log.Add(MarkerKind.Bookmark, "", "", 5, 0d);

            Assert.AreEqual(3, log.Count);
            Assert.AreEqual(1, log.CountOf(MarkerKind.Error));
            Assert.AreEqual("Bookmark", log[2].Title);
            Assert.AreEqual(1, log.LowerBound(2));
            Assert.AreEqual(3, log.LowerBound(6));
        }

        [Test]
        public void Collapse_MergesRepeatsEvenWhenTheyAlternate()
        {
            var log = new MarkerLog(16) { CollapseRepeats = true };
            for (var frame = 0; frame < 100; frame++)
            {
                log.Add(MarkerKind.Error, "Ball fell", "", frame, 0d);
                log.Add(MarkerKind.Warning, "Slow frame", "", frame, 0d);
            }

            Assert.AreEqual(2, log.Count);
            Assert.AreEqual(100, log[0].Count);
            Assert.AreEqual(99, log[0].LastSequence);
        }

        [Test]
        public void Collapse_StartsANewMarkerAfterAGap()
        {
            var log = new MarkerLog(16) { CollapseRepeats = true };
            log.Add(MarkerKind.Error, "x", "", 0, 0d);
            log.Add(MarkerKind.Error, "x", "", 10, 0d);

            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void Capacity_DropsTheOldestMarker()
        {
            var log = new MarkerLog(4) { CollapseRepeats = false };
            for (var i = 0; i < 10; i++)
                log.Add(MarkerKind.Log, i.ToString(), "", i, 0d);

            Assert.AreEqual(4, log.Count);
            Assert.AreEqual("6", log[0].Message);
            Assert.AreEqual("9", log[3].Message);
        }

        [Test]
        public void TrimBefore_RemovesMarkersOlderThanTheHistory()
        {
            var log = new MarkerLog(8) { CollapseRepeats = false };
            for (var i = 0; i < 5; i++)
                log.Add(MarkerKind.Log, i.ToString(), "", i * 10, 0d);

            log.TrimBefore(25);

            Assert.AreEqual(2, log.Count);
            Assert.AreEqual(30, log[0].Sequence);
        }

        [Test]
        public void Title_IsTheFirstLine()
        {
            var log = new MarkerLog(4);
            log.Add(MarkerKind.Exception, "NullReferenceException: boom\r\nat Foo.Bar()", "", 0, 0d);
            Assert.AreEqual("NullReferenceException: boom", log[0].Title);
        }
    }
}
