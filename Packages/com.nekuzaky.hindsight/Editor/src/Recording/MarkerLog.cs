using System;
using System.IO;
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
        Bookmark,
        Spawn,
        Destroy,
        SceneLoad
    }

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

        public readonly bool IsLifecycle => Kind == MarkerKind.Spawn || Kind == MarkerKind.Destroy || Kind == MarkerKind.SceneLoad;

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

        public void TrimAfter(int sequence)
        {
            for (var guard = Count; guard > 0 && Count > 0 && this[Count - 1].Sequence > sequence; guard--)
            {
                _items[(_start + Count - 1) % _items.Length] = default;
                Count--;
            }
            for (var i = 0; i < Count; i++)
            {
                ref var marker = ref _items[(_start + i) % _items.Length];
                if (marker.LastSequence > sequence)
                    marker.LastSequence = sequence;
            }
            Version++;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            _start = 0;
            Count = 0;
            Version++;
        }

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(Count);
            for (var i = 0; i < Count; i++)
            {
                ref readonly var marker = ref this[i];
                writer.Write(marker.Sequence);
                writer.Write(marker.LastSequence);
                writer.Write(marker.RealTime);
                writer.Write((byte)marker.Kind);
                writer.Write(marker.Count);
                writer.Write(marker.Message ?? string.Empty);
                writer.Write(marker.StackTrace ?? string.Empty);
            }
        }

        public void ReadFrom(BinaryReader reader)
        {
            Clear();
            var count = NativeIO.CheckCount(reader.ReadInt32(), _items.Length, "events");
            for (var i = 0; i < count; i++)
            {
                _items[i] = new Marker
                {
                    Sequence = reader.ReadInt32(),
                    LastSequence = reader.ReadInt32(),
                    RealTime = reader.ReadDouble(),
                    Kind = (MarkerKind)reader.ReadByte(),
                    Count = reader.ReadInt32(),
                    Message = reader.ReadString(),
                    StackTrace = reader.ReadString()
                };
            }
            Count = count;
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
