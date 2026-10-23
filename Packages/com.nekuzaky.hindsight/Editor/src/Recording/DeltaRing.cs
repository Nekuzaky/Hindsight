using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// Fixed-size ring of delta entries addressed by an ever-growing absolute index.
    /// Allocated once per recording session; writing never allocates.
    /// </summary>
    internal sealed class DeltaRing<T> : IDisposable where T : unmanaged
    {
        #region Public

        public DeltaRing(int capacity)
        {
            Debug.Assert(capacity > 0, "A delta ring needs a positive capacity.");
            _items = new NativeArray<T>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        public int Capacity => _items.Length;

        /// <summary>Absolute index of the next entry to write.</summary>
        public long Head { get; private set; }

        /// <summary>Absolute index of the oldest entry still kept.</summary>
        public long Tail { get; private set; }

        public int Count => (int)(Head - Tail);

        public int Free => Capacity - Count;

        public NativeArray<T> Items => _items;

        public long SizeInBytes => (long)Capacity * Marshal.SizeOf<T>();

        #endregion


        #region Private and Protected

        private NativeArray<T> _items;

        #endregion


        #region Main API

        public int IndexOf(long absoluteIndex)
        {
            Debug.Assert(absoluteIndex >= 0, "Absolute ring indices are never negative.");
            return (int)(absoluteIndex % Capacity);
        }

        public T At(long absoluteIndex)
        {
            Debug.Assert(absoluteIndex >= Tail && absoluteIndex < Head, "Reading outside the kept range.");
            return _items[IndexOf(absoluteIndex)];
        }

        public void Append(in T value)
        {
            Debug.Assert(Free > 0, "Append on a full delta ring: room must be made before the frame starts.");
            _items[IndexOf(Head)] = value;
            Head++;
        }

        /// <summary>Accepts entries a job wrote right after <see cref="Head"/>.</summary>
        public void Commit(int written)
        {
            Debug.Assert(written >= 0 && written <= Free, "A job wrote more entries than the free space.");
            Head += written;
        }

        /// <summary>Forgets the entries written at or after <paramref name="absoluteIndex"/>.</summary>
        public void TruncateTo(long absoluteIndex)
        {
            Debug.Assert(absoluteIndex >= Tail && absoluteIndex <= Head, "Truncation point outside the kept range.");
            Head = absoluteIndex;
        }

        public void ReleaseUntil(long absoluteIndex)
        {
            Debug.Assert(absoluteIndex >= Tail && absoluteIndex <= Head, "Release point outside the kept range.");
            Tail = absoluteIndex;
        }

        public void Clear()
        {
            Head = 0;
            Tail = 0;
        }

        public void Dispose()
        {
            if (_items.IsCreated)
                _items.Dispose();
        }

        /// <summary>Writes the kept entries with their absolute indices.</summary>
        public void WriteTo(BinaryWriter writer, byte[] buffer)
        {
            writer.Write(Tail);
            writer.Write(Head);
            var first = Count == 0 ? 0 : IndexOf(Tail);
            var before = Math.Min(Count, Capacity - first);
            NativeIO.Write(writer, _items, first, before, buffer);
            NativeIO.Write(writer, _items, 0, Count - before, buffer);
        }

        /// <summary>Reads what <see cref="WriteTo"/> wrote. The ring may have another capacity, as long as the entries fit.</summary>
        public void ReadFrom(BinaryReader reader, byte[] buffer)
        {
            var tail = reader.ReadInt64();
            var head = reader.ReadInt64();
            if (tail < 0 || head < tail)
                throw new InvalidDataException("The recording is damaged: invalid delta range.");
            var count = NativeIO.CheckCount(head - tail, Capacity, "delta entries");
            Tail = tail;
            Head = head;
            var first = count == 0 ? 0 : IndexOf(tail);
            var before = Math.Min(count, Capacity - first);
            NativeIO.Read(reader, _items, first, before, buffer);
            NativeIO.Read(reader, _items, 0, count - before, buffer);
        }

        #endregion
    }
}
