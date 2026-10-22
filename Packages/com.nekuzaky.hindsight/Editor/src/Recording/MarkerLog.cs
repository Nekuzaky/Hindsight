using System;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    internal enum MarkerKind : byte
    {
        Error,
        Exception,
        Assert,
        Warning,
        Log,
        Bookmark
    }

    /// <summary>Something that happened at a frame: a Console message or a bookmark.</summary>
    internal struct Marker
    {
        public int Sequence;
        public int LastSequence;
        public double RealTime;
        public MarkerKind Kind;
        public int Count;
        public string Message;
        public string StackTrace;

        public readonly bool IsError => Kind == MarkerKind.Error || Kind == MarkerKind.Exception || Kind == MarkerKind.Assert;

        /// <summary>First line of the message, the part shown in lists.</summary>
        public readonly string Title
        {
            get
            {
                if (string.IsNullOrEmpty(Message))
                    return Kind == MarkerKind.Bookmark ? "Bookmark" : string.Empty;
                var newline = Message.IndexOf('\n');
                return newline < 0 ? Message : Message.Substring(0, newline).TrimEnd('\r');
            }
        }
    }

    /// <summary>
    /// Bounded ring of markers. When repeats are collapsed, a message identical to the previous one within a few
    /// frames extends that marker instead of adding a new one, so an error raised every frame stays one entry.
    /// </summary>
    internal sealed class MarkerLog
    {
        #region Public

        public const int DefaultCapacity = 4096;
        public const int CollapseGap = 2;
        public const int CollapseWindow = 8;

        public MarkerLog(int capacity = DefaultCapacity)
        {
            Debug.Assert(capacity > 0, "A marker log needs a positive capacity.");
            _items = new Marker[capacity];
        }

        public int Count { get; private set; }

        public int Capacity => _items.Length;

        /// <summary>Changes every time a marker is added, extended or removed; lets views skip rebuilding.</summary>
        public int Version { get; private set; }

        public bool CollapseRepeats { get; set; } = true;

        #endregion


        #region Private and Protected

        private readonly Marker[] _items;
        private int _start;

        #endregion


        #region Main API

        public ref readonly Marker this[int index]
        {
            get
            {
                Debug.Assert(index >= 0 && index < Count, "Marker index out of range.");
                return ref _items[(_start + index) % _items.Length];
            }
        }

        public void Add(MarkerKind kind, string message, string stackTrace, int sequence, double realTime)
        {
            Debug.Assert(sequence >= 0, "Markers belong to a recorded frame.");
            if (CollapseRepeats && TryExtendLast(kind, message, sequence))
                return;

            if (Count == _items.Length)
            {
                _start = (_start + 1) % _items.Length;
                Count--;
            }

            _items[(_start + Count) % _items.Length] = new Marker
            {
                Sequence = sequence,
                LastSequence = sequence,
                RealTime = realTime,
                Kind = kind,
                Count = 1,
                Message = message ?? string.Empty,
                StackTrace = stackTrace ?? string.Empty
            };
            Count++;
            Version++;
        }

        /// <summary>Drops markers whose whole range is older than <paramref name="oldestSequence"/>.</summary>
        public void TrimBefore(int oldestSequence)
        {
            var removed = 0;
            while (Count > 0 && removed < _items.Length && _items[_start].LastSequence < oldestSequence)
            {
                _items[_start] = default;
                _start = (_start + 1) % _items.Length;
                Count--;
                removed++;
            }
            if (removed > 0)
                Version++;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            _start = 0;
            Count = 0;
            Version++;
        }

        public int CountOf(MarkerKind kind)
        {
            var total = 0;
            for (var i = 0; i < Count; i++)
            {
                if (this[i].Kind == kind)
                    total++;
            }
            return total;
        }

        /// <summary>Index of the first marker at or after <paramref name="sequence"/>, or <see cref="Count"/>.</summary>
        public int LowerBound(int sequence)
        {
            int low = 0, high = Count;
            while (low < high)
            {
                var middle = (low + high) >> 1;
                if (this[middle].Sequence < sequence)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        #endregion


        #region Tools and Utilities

        private bool TryExtendLast(MarkerKind kind, string message, int sequence)
        {
            // Looks a few markers back so messages that alternate every frame collapse too.
            var depth = Math.Min(Count, CollapseWindow);
            for (var back = 1; back <= depth; back++)
            {
                ref var candidate = ref _items[(_start + Count - back) % _items.Length];
                if (candidate.Kind != kind || sequence - candidate.LastSequence > CollapseGap
                    || !string.Equals(candidate.Message, message, StringComparison.Ordinal))
                    continue;

                candidate.LastSequence = sequence;
                candidate.Count++;
                Version++;
                return true;
            }
            return false;
        }

        #endregion
    }
}
