using Unity.Collections;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    // Resuming from a past frame: the scene already shows that frame; the recording forgets what came after it and
    // carries on from there, and Rigidbodies get the velocities they had.
    internal sealed partial class Recorder
    {
        #region Public

        /// <summary>
        /// Makes <paramref name="sequence"/> the newest frame. <paramref name="states"/> and <paramref name="values"/>
        /// hold that frame, as rebuilt by the rewind; the scene must show it already.
        /// </summary>
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

        /// <summary>Gives every Rigidbody and Rigidbody2D that existed at that frame the velocity it had then.</summary>
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
                // Appeared after that frame: its story starts again with the next frame, written whole.
                _birth[slot] = next;
                if (_death[slot] != Alive)
                    _death[slot] = next;
                _last[slot] = TransformSample.Unset;
                return;
            }

            _last[slot] = states[slot];
            // Destroyed after that frame: it cannot come back, so it disappears with the next frame.
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
