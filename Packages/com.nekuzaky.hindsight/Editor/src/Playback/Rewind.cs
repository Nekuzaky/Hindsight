using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// Puts the scene back the way it was at a recorded frame while the game is paused, and restores the live
    /// state afterwards. Objects that did not exist yet are hidden; nothing else in the game is touched.
    /// </summary>
    internal sealed class Rewind : IDisposable
    {
        #region Public

        public Rewind(Recorder recorder)
        {
            Debug.Assert(recorder != null, "Rewind needs a recorder.");
            _recorder = recorder;
        }

        /// <summary>True while the scene shows a past frame.</summary>
        public bool IsActive { get; private set; }

        /// <summary>The frame the scene shows: the rewound one, or the newest one when live.</summary>
        public int Sequence => IsActive ? _sequence : _recorder.History.NewestSequence;

        public bool HideObjectsNotYetSpawned { get; set; } = true;

        /// <summary>Transforms of every slot at <see cref="Sequence"/>. Safe to read while the game runs.</summary>
        public NativeArray<TransformSample> Transforms
        {
            get
            {
                if (IsActive)
                    return _shown;
                // The capture job of the current frame may still own the live state: finish it first.
                _recorder.Commit();
                return _recorder.State;
            }
        }

        /// <summary>Values of every stream at <see cref="Sequence"/>. Safe to read while the game runs.</summary>
        public NativeArray<Vector4> Values
        {
            get
            {
                if (IsActive)
                    return _shownValues;
                _recorder.Commit();
                return _recorder.ValueState;
            }
        }

        public event Action Changed;

        #endregion


        #region Private and Protected

        private readonly Recorder _recorder;
        private readonly List<Renderer> _renderers = new(8);
        private readonly List<(Renderer renderer, bool forced)> _hidden = new(64);

        private NativeArray<TransformSample> _live;
        private NativeArray<TransformSample> _shown;
        private NativeArray<Vector4> _shownValues;
        private NativeArray<byte> _mask;
        private int _sequence;

        #endregion


        #region Main API

        /// <summary>Shows a recorded frame. Returns false when the frame is no longer in the history.</summary>
        public bool Show(int sequence)
        {
            _recorder.Commit();
            var history = _recorder.History;
            if (!history.Contains(sequence))
                return false;

            EnsureBuffers();
            if (!IsActive)
            {
                SnapshotLive();
                IsActive = true;
            }

            var seeked = history.Seek(sequence, _shown, _recorder.SlotCount, _shownValues, _recorder.StreamCount);
            Debug.Assert(seeked, "A frame inside the history must always be rebuildable.");
            BuildMask(sequence);
            new ApplyTransformsJob { States = _shown, Mask = _mask }.Schedule(_recorder.Access).Complete();
            UpdateHidden(sequence);

            _sequence = sequence;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Puts every object back where it was before rewinding. Does nothing when already live.</summary>
        public void ReturnToLive()
        {
            if (!IsActive)
                return;

            BuildMask(int.MaxValue);
            new ApplyTransformsJob { States = _live, Mask = _mask }.Schedule(_recorder.Access).Complete();
            Unhide();
            IsActive = false;
            Changed?.Invoke();
        }

        /// <summary>
        /// Makes the shown frame the live scene: objects stay where they are and hidden objects show again.
        /// Used to resume the game from a past frame.
        /// </summary>
        public void Adopt()
        {
            if (!IsActive)
                return;
            Unhide();
            IsActive = false;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            ReturnToLive();
            Release(ref _live);
            Release(ref _shown);
            Release(ref _shownValues);
            Release(ref _mask);
        }

        #endregion


        #region Tools and Utilities

        private void EnsureBuffers()
        {
            var slots = Math.Max(1, _recorder.SlotCount);
            var streams = Math.Max(1, _recorder.StreamCount);
            if (!_shown.IsCreated || _shown.Length < slots)
            {
                Debug.Assert(!IsActive, "Slots cannot be added while the game is paused and rewound.");
                Reallocate(ref _live, slots);
                Reallocate(ref _shown, slots);
                Reallocate(ref _mask, slots);
            }
            if (!_shownValues.IsCreated || _shownValues.Length < streams)
                Reallocate(ref _shownValues, streams);
        }

        private void SnapshotLive()
        {
            new ReadTransformsJob { States = _live }.ScheduleReadOnly(_recorder.Access, 64).Complete();
        }

        private void BuildMask(int sequence)
        {
            var slots = _recorder.SlotCount;
            for (var slot = 0; slot < slots; slot++)
            {
                var alive = _recorder.IsAliveSlot(slot);
                _mask[slot] = alive && _recorder.BirthOf(slot) <= sequence ? (byte)1 : (byte)0;
            }
        }

        private void UpdateHidden(int sequence)
        {
            Unhide();
            if (!HideObjectsNotYetSpawned)
                return;

            var slots = _recorder.SlotCount;
            for (var slot = 0; slot < slots; slot++)
            {
                if (!_recorder.IsAliveSlot(slot) || _recorder.BirthOf(slot) <= sequence)
                    continue;

                var transform = _recorder.TransformOf(slot);
                if (transform == null)
                    continue;

                transform.GetComponents(_renderers);
                foreach (var renderer in _renderers)
                {
                    _hidden.Add((renderer, renderer.forceRenderingOff));
                    renderer.forceRenderingOff = true;
                }
            }
            _renderers.Clear();
        }

        private void Unhide()
        {
            foreach (var (renderer, forced) in _hidden)
            {
                if (renderer != null)
                    renderer.forceRenderingOff = forced;
            }
            _hidden.Clear();
        }

        private static void Reallocate<T>(ref NativeArray<T> array, int length) where T : struct
        {
            Release(ref array);
            array = new NativeArray<T>(Mathf.NextPowerOfTwo(length), Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        private static void Release<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
                array.Dispose();
        }

        #endregion
    }
}
