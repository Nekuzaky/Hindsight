using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    internal enum StreamKind : byte
    {
        LinearVelocity,
        AngularVelocity,
        Field
    }

    // Streams: values sampled on the main thread. Each Rigidbody gets a linear and an angular velocity stream,
    // and each tracked field of each component gets one stream. Streams die with their object and are reused
    // like slots.
    internal sealed partial class Recorder
    {
        #region Public

        public StreamKind KindOf(int stream) => _streamKind[stream];
        public int OwnerOf(int stream) => _streamOwner[stream];
        public ValueFormat FormatOf(int stream) => _streamFormat[stream];
        public string LabelOf(int stream) => _streamLabel[stream];
        public Type EnumTypeOf(int stream) => _streamEnum[stream];
        public bool StreamExistedAt(int stream, int sequence) => _streamBirth[stream] <= sequence && sequence < _streamDeath[stream];

        /// <summary>Streams attached to a slot's current object, velocities first.</summary>
        public int GetStreams(int slot, List<int> output)
        {
            output.Clear();
            if (slot < 0 || slot >= SlotCount)
                return 0;
            if (_bodyLinear[slot] >= 0)
                output.Add(_bodyLinear[slot]);
            if (_bodyAngular[slot] >= 0)
                output.Add(_bodyAngular[slot]);

            var stream = _firstField[slot];
            for (var guard = 0; stream >= 0 && guard < _streamCapacity; guard++)
            {
                output.Add(stream);
                stream = _nextField[stream];
            }
            return output.Count;
        }

        #endregion


        #region Private and Protected

        private readonly List<MonoBehaviour> _behaviours = new(16);

        private int[] _bodyLinear;
        private int[] _bodyAngular;
        private int[] _firstField;

        private StreamKind[] _streamKind;
        private int[] _streamOwner;
        private int[] _streamBirth;
        private int[] _streamDeath;
        private ValueFormat[] _streamFormat;
        private string[] _streamLabel;
        private Type[] _streamEnum;
        private Func<Component, Vector4>[] _streamGetter;
        private Component[] _streamComponent;
        private Rigidbody[] _streamBody;
        private Rigidbody2D[] _streamBody2D;
        private int[] _nextField;

        private int[] _activeFields;
        private int[] _activeIndex;
        private int _activeFieldCount;
        private int[] _streamRecycle;
        private int _streamRecycleStart;
        private int _streamRecycleCount;
        private int _streamCapacity;
        private bool _reportedFieldError;

        #endregion


        #region Tools and Utilities

        private void AllocateStreams(int capacity)
        {
            Grow(ref _values, capacity, NativeArrayOptions.UninitializedMemory);
            GrowManaged(ref _streamKind, capacity, StreamKind.Field);
            GrowManaged(ref _streamOwner, capacity, -1);
            GrowManaged(ref _streamBirth, capacity, 0);
            GrowManaged(ref _streamDeath, capacity, Alive);
            GrowManaged(ref _streamFormat, capacity, ValueFormat.Float);
            GrowManaged(ref _streamLabel, capacity, null);
            GrowManaged(ref _streamEnum, capacity, null);
            GrowManaged(ref _streamGetter, capacity, null);
            GrowManaged(ref _streamComponent, capacity, null);
            GrowManaged(ref _streamBody, capacity, null);
            GrowManaged(ref _streamBody2D, capacity, null);
            GrowManaged(ref _nextField, capacity, -1);
            GrowManaged(ref _activeFields, capacity, -1);
            GrowManaged(ref _activeIndex, capacity, -1);

            var recycle = new int[capacity];
            for (var i = 0; i < _streamRecycleCount; i++)
                recycle[i] = _streamRecycle[(_streamRecycleStart + i) % _streamRecycle.Length];
            _streamRecycle = recycle;
            _streamRecycleStart = 0;
            _streamCapacity = capacity;
        }

        private void AttachStreams(int slot, Transform transform, int sequence)
        {
            _bodyLinear[slot] = -1;
            _bodyAngular[slot] = -1;
            _firstField[slot] = -1;

            if (Options.RecordVelocities)
                AttachBody(slot, transform, sequence);
            if (Options.RecordTrackedFields && TrackedMembers.HasAny)
                AttachFields(slot, transform, sequence);
        }

        private void AttachBody(int slot, Transform transform, int sequence)
        {
            if (transform.TryGetComponent<Rigidbody>(out var body))
            {
                var linear = NewStream(slot, StreamKind.LinearVelocity, ValueFormat.LinearVelocity, "Velocity", sequence);
                var angular = NewStream(slot, StreamKind.AngularVelocity, ValueFormat.AngularVelocity, "Angular velocity", sequence);
                if (linear < 0 || angular < 0)
                    return;
                _streamBody[linear] = body;
                _bodyLinear[slot] = linear;
                _bodyAngular[slot] = angular;
            }
            else if (transform.TryGetComponent<Rigidbody2D>(out var body2D))
            {
                var linear = NewStream(slot, StreamKind.LinearVelocity, ValueFormat.LinearVelocity, "Velocity", sequence);
                var angular = NewStream(slot, StreamKind.AngularVelocity, ValueFormat.AngularVelocity2D, "Angular velocity", sequence);
                if (linear < 0 || angular < 0)
                    return;
                _streamBody2D[linear] = body2D;
                _bodyLinear[slot] = linear;
                _bodyAngular[slot] = angular;
            }
        }

        private void AttachFields(int slot, Transform transform, int sequence)
        {
            transform.GetComponents(_behaviours);
            var previous = -1;
            foreach (var behaviour in _behaviours)
            {
                if (behaviour == null)
                    continue;
                foreach (var member in TrackedMembers.For(behaviour.GetType()))
                {
                    var stream = NewStream(slot, StreamKind.Field, member.Format, member.Label, sequence);
                    if (stream < 0)
                        return;
                    _streamGetter[stream] = member.Getter;
                    _streamComponent[stream] = behaviour;
                    _streamEnum[stream] = member.EnumType;
                    if (previous < 0)
                        _firstField[slot] = stream;
                    else
                        _nextField[previous] = stream;
                    previous = stream;
                    ActivateField(stream);
                }
            }
            _behaviours.Clear();
        }

        private int NewStream(int slot, StreamKind kind, ValueFormat format, string label, int sequence)
        {
            if (!TryTakeStream(out var stream))
            {
                LimitReached = true;
                return -1;
            }

            _streamKind[stream] = kind;
            _streamOwner[stream] = slot;
            _streamBirth[stream] = sequence;
            _streamDeath[stream] = Alive;
            _streamFormat[stream] = format;
            _streamLabel[stream] = label;
            _streamEnum[stream] = null;
            _streamGetter[stream] = null;
            _streamComponent[stream] = null;
            _streamBody[stream] = null;
            _streamBody2D[stream] = null;
            _nextField[stream] = -1;
            _values[stream] = ValueSample.Unset;
            return stream;
        }

        private bool TryTakeStream(out int stream)
        {
            if (_streamRecycleCount > 0 && _streamDeath[_streamRecycle[_streamRecycleStart]] <= History.OldestSequence)
            {
                stream = _streamRecycle[_streamRecycleStart];
                _streamRecycleStart = (_streamRecycleStart + 1) % _streamRecycle.Length;
                _streamRecycleCount--;
                return true;
            }

            if (StreamCount >= Options.StreamLimit)
            {
                stream = -1;
                return false;
            }

            if (StreamCount == _streamCapacity)
                AllocateStreams(Mathf.Min(_streamCapacity * 2, Mathf.Max(Options.StreamLimit, _streamCapacity + 1)));
            stream = StreamCount++;
            return true;
        }

        private void DetachStreams(int slot, int sequence)
        {
            KillStream(_bodyLinear[slot], sequence);
            KillStream(_bodyAngular[slot], sequence);
            var stream = _firstField[slot];
            for (var guard = 0; stream >= 0 && guard < _streamCapacity; guard++)
            {
                var next = _nextField[stream];
                KillStream(stream, sequence);
                stream = next;
            }
            _bodyLinear[slot] = -1;
            _bodyAngular[slot] = -1;
            _firstField[slot] = -1;
        }

        private void KillStream(int stream, int sequence)
        {
            if (stream < 0 || _streamDeath[stream] != Alive)
                return;

            _streamDeath[stream] = sequence;
            _streamComponent[stream] = null;
            _streamBody[stream] = null;
            _streamBody2D[stream] = null;
            _streamGetter[stream] = null;
            DeactivateField(stream);
            _streamRecycle[(_streamRecycleStart + _streamRecycleCount) % _streamRecycle.Length] = stream;
            _streamRecycleCount++;
        }

        private void ActivateField(int stream)
        {
            _activeIndex[stream] = _activeFieldCount;
            _activeFields[_activeFieldCount++] = stream;
        }

        private void DeactivateField(int stream)
        {
            var index = _activeIndex[stream];
            if (index < 0)
                return;

            var moved = _activeFields[--_activeFieldCount];
            _activeFields[index] = moved;
            _activeIndex[moved] = index;
            _activeIndex[stream] = -1;
        }

        private void SampleValues(int sampledSlots, int sequence)
        {
            if (Options.RecordVelocities)
                SampleBodies(sampledSlots);
            if (_activeFieldCount > 0)
                SampleFields(sequence);
        }

        private void SampleBodies(int sampledSlots)
        {
            for (var i = 0; i < sampledSlots; i++)
            {
                var slot = _sampleSlots[i];
                var linear = _bodyLinear[slot];
                if (linear < 0)
                    continue;

                var angular = _bodyAngular[slot];
                var body = _streamBody[linear];
                if (!ReferenceEquals(body, null))
                {
                    if (body == null)
                        continue;
                    Write(linear, body.linearVelocity);
                    Write(angular, body.angularVelocity);
                    continue;
                }

                var body2D = _streamBody2D[linear];
                if (body2D == null)
                    continue;
                Write(linear, body2D.linearVelocity);
                Write(angular, new Vector4(0f, 0f, body2D.angularVelocity, 0f));
            }
        }

        private void SampleFields(int sequence)
        {
            // Iterates backwards because a dead component removes its stream by swapping with the last one.
            for (var i = _activeFieldCount - 1; i >= 0; i--)
            {
                var stream = _activeFields[i];
                var component = _streamComponent[stream];
                if (component == null)
                {
                    KillStream(stream, sequence);
                    continue;
                }

                try
                {
                    Write(stream, _streamGetter[stream](component));
                }
                catch (Exception exception)
                {
                    KillStream(stream, sequence);
                    if (!_reportedFieldError)
                        Debug.LogWarning($"[Hindsight] Stopped recording {_streamLabel[stream]} on {component.name}: {exception.Message}");
                    _reportedFieldError = true;
                }
            }
        }

        private void Write(int stream, Vector4 value)
        {
            if (ValueSample.Same(value, _values[stream]))
                return;
            _values[stream] = value;
            History.AppendValue(stream, value);
        }

        private void DisposeStreams()
        {
            Release(ref _values);
            Array.Clear(_streamComponent, 0, _streamComponent.Length);
            Array.Clear(_streamBody, 0, _streamBody.Length);
            Array.Clear(_streamBody2D, 0, _streamBody2D.Length);
            _activeFieldCount = 0;
        }

        #endregion
    }
}
