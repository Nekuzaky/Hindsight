using Unity.Collections;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    internal sealed partial class Recorder
    {
        #region Public

        public void ResumeAt(int sequence, NativeArray<TransformSample> states, NativeArray<Vector4> values)
        {
            Debug.Assert(!IsFromFile, "A recording opened from a file cannot be resumed.");
            Commit();
            History.TruncateAfter(sequence);
            Markers.TrimAfter(sequence);
            for (var slot = 0; slot < SlotCount; slot++)
                ResumeSlot(slot, sequence, states);
            for (var stream = 0; stream < StreamCount; stream++)
                ResumeStream(stream, sequence, values);
        }

        public void RestoreVelocities(int sequence, NativeArray<Vector4> values)
        {
            Physics.SyncTransforms();
            Physics2D.SyncTransforms();
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var linear = _bodyLinear[slot];
                if (linear < 0 || !IsAliveSlot(slot) || _birth[slot] > sequence)
                    continue;
                var velocity = values[linear];
                var angular = values[_bodyAngular[slot]];
                if (!ValueSample.IsSet(velocity) || !ValueSample.IsSet(angular))
                    continue;

                var body = _streamBody[linear];
                if (body != null && !body.isKinematic)
                {
                    body.linearVelocity = velocity;
                    body.angularVelocity = angular;
                }
                var body2D = _streamBody2D[linear];
                if (body2D != null && body2D.bodyType == RigidbodyType2D.Dynamic)
                {
                    body2D.linearVelocity = velocity;
                    body2D.angularVelocity = angular.z;
                }
            }
        }

        #endregion


        #region Tools and Utilities

        private void ResumeSlot(int slot, int sequence, NativeArray<TransformSample> states)
        {
            var next = sequence + 1;
            _moving[slot] = 0;
            if (_birth[slot] > sequence)
            {
                _birth[slot] = next;
                if (_death[slot] != Alive)
                    _death[slot] = next;
                _last[slot] = TransformSample.Unset;
                return;
            }

            _last[slot] = states[slot];
            if (_death[slot] != Alive && _death[slot] > sequence)
                _death[slot] = next;
        }

        private void ResumeStream(int stream, int sequence, NativeArray<Vector4> values)
        {
            var next = sequence + 1;
            if (_streamBirth[stream] > sequence)
            {
                _streamBirth[stream] = next;
                if (_streamDeath[stream] != Alive)
                    _streamDeath[stream] = next;
                _values[stream] = ValueSample.Unset;
                return;
            }

            _values[stream] = values[stream];
            if (_streamDeath[stream] != Alive && _streamDeath[stream] > sequence)
                _streamDeath[stream] = next;
        }

        #endregion
    }
}
