using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    internal sealed class DeltaRing<T> : IDisposable where T : unmanaged
    {
        #region Public

        public DeltaRing(int capacity)
        {
            Debug.Assert(capacity > 0, "A delta ring needs a positive capacity.");
            _items = new NativeArray<T>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        public int Capacity => _items.Length;

        public long Head { get; private set; }

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

        public void Commit(int written)
        {
            Debug.Assert(written >= 0 && written <= Free, "A job wrote more entries than the free space.");
            Head += written;
        }

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

        public void WriteTo(BinaryWriter writer, byte[] buffer)
        {
            writer.Write(Tail);
            writer.Write(Head);
            var first = Count == 0 ? 0 : IndexOf(Tail);
            var before = Math.Min(Count, Capacity - first);
            NativeIO.Write(writer, _items, first, before, buffer);
            NativeIO.Write(writer, _items, 0, Count - before, buffer);
        }

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
