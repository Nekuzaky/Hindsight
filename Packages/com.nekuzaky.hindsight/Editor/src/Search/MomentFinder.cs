using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// Finds the frame where a condition becomes true: on one value of the selected object, or, with <c>any</c>,
    /// on the position of every recorded object. Only transitions count, so a value that stays true is found once.
    /// </summary>
    internal static class MomentFinder
    {
        #region Public

        public readonly struct Result
        {
            public Result(bool found, int sequence, int slot, string message)
            {
                Found = found;
                Sequence = sequence;
                Slot = slot;
                Message = message;
            }

            public bool Found { get; }
            public int Sequence { get; }
            public int Slot { get; }
            public string Message { get; }
        }

        /// <summary>
        /// Searches after <paramref name="from"/> (or before it when <paramref name="forward"/> is false). Pass a
        /// sequence older than the history to search from the very first frame.
        /// </summary>
        public static Result Find(Recorder recorder, in ConditionQuery query, int selectedSlot, int from, bool forward)
        {
            Debug.Assert(recorder != null, "A recorder is required.");
            recorder.Commit();
            if (recorder.History.IsEmpty)
                return Fail("Nothing is recorded yet.");
            return query.AnyObject
                ? FindOnAnyObject(recorder, query, from, forward)
                : FindOnSlot(recorder, query, selectedSlot, from, forward);
        }

        /// <summary>Channels a slot offers, for hints: x, y, z, then velocities and tracked fields.</summary>
        public static string DescribeChannels(Recorder recorder, int slot)
        {
            var names = new List<string> { "x", "y", "z" };
            var streams = new List<int>();
            recorder.GetStreams(slot, streams);
            foreach (var stream in streams)
            {
                switch (recorder.KindOf(stream))
                {
                    case StreamKind.LinearVelocity: names.AddRange(new[] { "speed", "vx", "vy", "vz" }); break;
                    case StreamKind.AngularVelocity: names.Add("angular"); break;
                    default: names.Add(recorder.LabelOf(stream)); break;
                }
            }
            return string.Join(", ", names);
        }

        #endregion


        #region Private and Protected

        private const int NoAxis = -1;

        #endregion


        #region One object

        private static Result FindOnSlot(Recorder recorder, in ConditionQuery query, int slot, int from, bool forward)
        {
            if (slot < 0)
                return Fail("Select a recorded object first, or start with 'any' to search the position of every object.");
            if (!TryBuildSeries(recorder, query.Channel, slot, out var series, out var error))
                return Fail(error);

            var history = recorder.History;
            var start = from - history.OldestSequence;
            var index = forward ? FirstEdgeAfter(series, query, start) : LastEdgeBefore(series, query, start);
            return index < 0
                ? Fail($"No {(forward ? "later" : "earlier")} frame where {query} becomes true.")
                : Found(recorder, query, history.OldestSequence + index, slot);
        }

        private static int FirstEdgeAfter(float[] series, in ConditionQuery query, int start)
        {
            for (var i = Math.Max(0, start + 1); i < series.Length; i++)
            {
                if (query.Matches(series[i]) && (i == 0 || !query.Matches(series[i - 1])))
                    return i;
            }
            return -1;
        }

        private static int LastEdgeBefore(float[] series, in ConditionQuery query, int start)
        {
            for (var i = Math.Min(series.Length - 1, start - 1); i >= 0; i--)
            {
                if (query.Matches(series[i]) && (i == 0 || !query.Matches(series[i - 1])))
                    return i;
            }
            return -1;
        }

        private static bool TryBuildSeries(Recorder recorder, string channel, int slot, out float[] series, out string error)
        {
            var count = recorder.History.FrameCount;
            series = new float[count];
            error = null;

            var axis = AxisOf(channel);
            if (axis != NoAxis)
            {
                var samples = new TransformSample[count];
                recorder.ExtractSlot(slot, samples);
                for (var i = 0; i < count; i++)
                    series[i] = samples[i].IsSet ? samples[i].Position[axis] : float.NaN;
                return true;
            }

            if (!TryFindStream(recorder, slot, channel, out var stream, out var component))
            {
                error = $"'{channel}' is not recorded on this object. Available: {DescribeChannels(recorder, slot)}.";
                return false;
            }

            var raw = new Vector4[count];
            recorder.ExtractStream(stream, raw);
            var format = recorder.FormatOf(stream);
            for (var i = 0; i < count; i++)
            {
                var value = raw[i];
                series[i] = !ValueSample.IsSet(value) ? float.NaN
                    : component >= 0 ? value[component]
                    : Format.Scalar(value, format);
            }
            return true;
        }

        /// <summary>Finds the stream behind a channel name. <paramref name="component"/> is -1 for the whole value.</summary>
        private static bool TryFindStream(Recorder recorder, int slot, string channel, out int stream, out int component)
        {
            var streams = new List<int>();
            recorder.GetStreams(slot, streams);
            var key = Normalize(channel);
            component = -1;
            foreach (var candidate in streams)
            {
                var kind = recorder.KindOf(candidate);
                if (kind == StreamKind.LinearVelocity && (key == "speed" || key == "velocity" || key is "vx" or "vy" or "vz"))
                {
                    component = key == "vx" ? 0 : key == "vy" ? 1 : key == "vz" ? 2 : -1;
                    stream = candidate;
                    return true;
                }
                if ((kind == StreamKind.AngularVelocity && key == "angular") || Normalize(recorder.LabelOf(candidate)) == key)
                {
                    stream = candidate;
                    return true;
                }
            }
            stream = -1;
            return false;
        }

        #endregion


        #region Every object

        private static Result FindOnAnyObject(Recorder recorder, in ConditionQuery query, int from, bool forward)
        {
            var axis = AxisOf(query.Channel);
            if (axis == NoAxis)
                return Fail("'any' searches positions: use x, y or z, for example  any y < -10.");

            var history = recorder.History;
            var last = forward ? history.NewestSequence : Math.Min(from - 1, history.NewestSequence);
            var first = forward ? Math.Max(from, history.OldestSequence - 1) : history.OldestSequence - 1;
            if (last < history.OldestSequence)
                return Fail($"No earlier frame where {query} becomes true.");

            var hit = Scan(recorder, query, axis, first, last, stopAtFirst: forward);
            return hit.sequence < 0
                ? Fail($"No {(forward ? "later" : "earlier")} frame where {query} becomes true for any object.")
                : Found(recorder, query, hit.sequence, hit.slot);
        }

        /// <summary>
        /// Replays the transform deltas from <paramref name="first"/> + 1 to <paramref name="last"/>, tracking for each
        /// slot whether the condition holds. Returns the first transition, or the last one when not stopping early.
        /// </summary>
        private static (int sequence, int slot) Scan(Recorder recorder, in ConditionQuery query, int axis, int first, int last, bool stopAtFirst)
        {
            var history = recorder.History;
            var slots = recorder.SlotCount;
            var holds = new bool[slots];
            var states = new NativeArray<TransformSample>(Math.Max(1, slots), Allocator.TempJob);
            var values = new NativeArray<Vector4>(Math.Max(1, recorder.StreamCount), Allocator.TempJob);
            (int sequence, int slot) found = (-1, -1);
            try
            {
                // Searching from before the history: whatever already holds on the oldest frame is a first moment.
                var fromStart = first < history.OldestSequence;
                var start = Math.Max(first, history.OldestSequence);
                if (history.Seek(start, states, slots, values, recorder.StreamCount))
                {
                    for (var slot = 0; slot < slots; slot++)
                    {
                        holds[slot] = states[slot].IsSet && query.Matches(states[slot].Position[axis]);
                        if (!holds[slot] || !fromStart || found.sequence >= 0)
                            continue;
                        found = (start, slot);
                        if (stopAtFirst)
                            return found;
                    }
                }

                for (var sequence = start + 1; sequence <= last; sequence++)
                {
                    ref readonly var frame = ref history.Frame(sequence);
                    for (var i = 0; i < frame.TransformCount; i++)
                    {
                        var delta = history.Transforms.At(frame.TransformStart + i);
                        var matches = query.Matches(delta.Value.Position[axis]);
                        if (matches && !holds[delta.Slot])
                        {
                            found = (sequence, delta.Slot);
                            if (stopAtFirst)
                                return found;
                        }
                        holds[delta.Slot] = matches;
                    }
                }
            }
            finally
            {
                states.Dispose();
                values.Dispose();
            }
            return found;
        }

        #endregion


        #region Tools and Utilities

        private static int AxisOf(string channel)
        {
            switch (Normalize(channel))
            {
                case "x": case "positionx": return 0;
                case "y": case "positiony": return 1;
                case "z": case "positionz": return 2;
                default: return NoAxis;
            }
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty).Replace(" ", string.Empty).Replace(".", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        }

        private static Result Found(Recorder recorder, in ConditionQuery query, int sequence, int slot)
        {
            var history = recorder.History;
            var offset = history.Frame(sequence).RealTime - history.Frame(history.NewestSequence).RealTime;
            var who = query.AnyObject ? WhoIs(recorder, slot) + ": " : string.Empty;
            return new Result(true, sequence, slot, $"{who}{query} at {Format.Offset(offset)} (frame {Format.Integer(history.Frame(sequence).GameFrame)})");
        }

        private static string WhoIs(Recorder recorder, int slot)
        {
            var transform = recorder.IsAliveSlot(slot) ? recorder.TransformOf(slot) : null;
            return transform != null ? transform.name : "an object destroyed since";
        }

        private static Result Fail(string message) => new(false, -1, -1, message);

        #endregion
    }
}
