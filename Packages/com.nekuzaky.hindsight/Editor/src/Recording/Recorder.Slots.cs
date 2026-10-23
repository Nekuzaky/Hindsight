using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;

namespace Nekuzaky.Hindsight.Editor
{
    internal sealed partial class Recorder
    {
        #region Public

        public const int Alive = int.MaxValue;

        public Transform TransformOf(int slot) => _transforms[slot];

        public int BirthOf(int slot) => _birth[slot];

        public int DeathOf(int slot) => _death[slot];

        public bool IsAliveSlot(int slot) => slot >= 0 && slot < SlotCount && _death[slot] == Alive;

        public bool ExistedAt(int slot, int sequence) => _birth[slot] <= sequence && sequence < _death[slot];

        public bool WasRoot(int slot) => _isRoot[slot];

        public bool TryGetSlot(Transform transform, out int slot)
        {
            slot = -1;
            return transform != null && _slotById.TryGetValue(transform.GetEntityId(), out slot);
        }

        #endregion


        #region Private and Protected

        private readonly Dictionary<EntityId, int> _slotById = new(InitialSlots);

        private Transform[] _transforms;
        private int[] _birth;
        private int[] _death;
        private bool[] _isRoot;
        private int[] _recycleQueue;
        private int _recycleStart;
        private int _recycleCount;
        private int _slotCapacity;

        #endregion


        #region Tools and Utilities

        private void AllocateSlots(int capacity)
        {
            Debug.Assert(capacity > 0, "Slot capacity must be positive.");
            _access = new TransformAccessArray(capacity, -1);
            _counters = new NativeArray<int>(RecorderCounters.Length, Allocator.Persistent);
            ResizeSlots(capacity);
        }

        private void ResizeSlots(int capacity)
        {
            Debug.Assert(capacity >= SlotCount, "Slot arrays can only grow.");
            Grow(ref _last, capacity, NativeArrayOptions.UninitializedMemory);
            Grow(ref _current, capacity, NativeArrayOptions.UninitializedMemory);
            Grow(ref _flags, capacity, NativeArrayOptions.ClearMemory);
            Grow(ref _alive, capacity, NativeArrayOptions.ClearMemory);
            Grow(ref _moving, capacity, NativeArrayOptions.ClearMemory);
            Grow(ref _died, capacity, NativeArrayOptions.UninitializedMemory);
            Grow(ref _sampleSlots, capacity, NativeArrayOptions.UninitializedMemory);

            GrowManaged(ref _transforms, capacity, null);
            GrowManaged(ref _birth, capacity, 0);
            GrowManaged(ref _death, capacity, Alive);
            GrowManaged(ref _isRoot, capacity, false);
            GrowManaged(ref _bodyLinear, capacity, -1);
            GrowManaged(ref _bodyAngular, capacity, -1);
            GrowManaged(ref _firstField, capacity, -1);
            ResizeRecycleQueue(capacity);
            _slotCapacity = capacity;
        }

        private void ResizeRecycleQueue(int capacity)
        {
            var queue = new int[capacity];
            for (var i = 0; i < _recycleCount; i++)
                queue[i] = _recycleQueue[(_recycleStart + i) % _recycleQueue.Length];
            _recycleQueue = queue;
            _recycleStart = 0;
        }

        private int Register(Transform transform, int sequence)
        {
            Debug.Assert(transform != null, "Cannot record a missing transform.");
            if (!TryTakeSlot(out var slot, out var reused))
            {
                LimitReached = true;
                return -1;
            }

            if (reused)
                _access[slot] = transform;
            else
                _access.Add(transform);
            Debug.Assert(_access.length == SlotCount, "The access array must stay aligned with the slots.");

            _last[slot] = TransformSample.Unset;
            _alive[slot] = 1;
            _moving[slot] = 0;
            _transforms[slot] = transform;
            _birth[slot] = sequence;
            _death[slot] = Alive;
            _isRoot[slot] = transform.parent == null;
            _slotById[transform.GetEntityId()] = slot;
            AliveCount++;
            _appeared++;

            AttachStreams(slot, transform, sequence);
            return slot;
        }

        private bool TryTakeSlot(out int slot, out bool reused)
        {
            if (_recycleCount > 0 && _death[_recycleQueue[_recycleStart]] <= History.OldestSequence)
            {
                slot = _recycleQueue[_recycleStart];
                _recycleStart = (_recycleStart + 1) % _recycleQueue.Length;
                _recycleCount--;
                reused = true;
                return true;
            }

            reused = false;
            if (SlotCount >= Options.SlotLimit)
            {
                slot = -1;
                return false;
            }

            if (SlotCount == _slotCapacity)
                ResizeSlots(Mathf.Min(_slotCapacity * 2, Mathf.Max(Options.SlotLimit, _slotCapacity + 1)));
            slot = SlotCount++;
            return true;
        }

        private void ProcessDeaths(int count, int sequence)
        {
            Debug.Assert(count >= 0 && count <= SlotCount, "Death count out of range.");
            for (var i = 0; i < count; i++)
            {
                var slot = _died[i];
                Debug.Assert(_death[slot] == Alive, "A slot cannot die twice.");
                _death[slot] = sequence;
                _slotById.Remove(EntityIdOf(slot));
                _transforms[slot] = null;
                AliveCount--;
                DetachStreams(slot, sequence);

                _recycleQueue[(_recycleStart + _recycleCount) % _recycleQueue.Length] = slot;
                _recycleCount++;
            }
        }

        private EntityId EntityIdOf(int slot)
        {
            var transform = _transforms[slot];
            return ReferenceEquals(transform, null) ? EntityId.None : transform.GetEntityId();
        }

        private void DisposeSlots()
        {
            if (_access.isCreated)
                _access.Dispose();
            Release(ref _last);
            Release(ref _current);
            Release(ref _flags);
            Release(ref _alive);
            Release(ref _moving);
            Release(ref _died);
            Release(ref _sampleSlots);
            Release(ref _counters);
            _slotById.Clear();
        }

        #endregion
    }
}
