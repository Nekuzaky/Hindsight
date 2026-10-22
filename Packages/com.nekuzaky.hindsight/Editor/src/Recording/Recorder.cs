using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// Records the scene frame after frame. At the end of a frame it schedules a Burst job that reads every tracked
    /// transform on worker threads; at the start of the next frame it collects the result, samples velocities and
    /// tracked fields, and closes the frame. Steady-state recording does not allocate managed memory.
    /// </summary>
    internal sealed partial class Recorder : IDisposable
    {
        #region Public

        public Recorder(in RecorderOptions options)
        {
            Options = options;
            History = new History(options.TransformCapacity, options.ValueCapacity, RecorderOptions.DefaultMaxFrames,
                options.MaxCheckpoints)
            {
                WindowSeconds = options.HistorySeconds
            };
            History.Trimmed += OnHistoryTrimmed;
            Markers = new MarkerLog();
            Stats = new RecorderStats();
            AllocateSlots(InitialSlots);
            AllocateStreams(InitialStreams);
        }

        public RecorderOptions Options { get; }
        public History History { get; }
        public MarkerLog Markers { get; }
        public RecorderStats Stats { get; }

        /// <summary>Slots in use, alive or dead. Arrays indexed by slot are at least this long.</summary>
        public int SlotCount { get; private set; }

        public int AliveCount { get; private set; }

        public int StreamCount { get; private set; }

        /// <summary>True once the object limit or the memory budget stopped new objects from being recorded.</summary>
        public bool LimitReached { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool HasPendingFrame => _pending;

        /// <summary>The sequence a Console message raised now belongs to.</summary>
        public int MarkerSequence => History.NewestSequence + 1;

        /// <summary>Live transforms of every slot, as of the newest committed frame.</summary>
        public NativeArray<TransformSample> State => _last;

        public NativeArray<Vector4> ValueState => _values;

        public TransformAccessArray Access => _access;

        public long SizeInBytes => History.SizeInBytes + (long)_slotCapacity * SlotBytes + (long)_streamCapacity * 16;

        #endregion


        #region Private and Protected

        private const int InitialSlots = 1024;
        private const int InitialStreams = 256;
        private const int SlotBytes = 40 * 2 + 3 + 4 * 2;
        private const int CaptureBatchSize = 64;

        private TransformAccessArray _access;
        private NativeArray<TransformSample> _last;
        private NativeArray<TransformSample> _current;
        private NativeArray<byte> _flags;
        private NativeArray<byte> _alive;
        private NativeArray<byte> _moving;
        private NativeArray<int> _died;
        private NativeArray<int> _sampleSlots;
        private NativeArray<int> _counters;
        private NativeArray<Vector4> _values;

        private JobHandle _handle;
        private bool _pending;
        private double _lastCaptureTime = double.NegativeInfinity;

        #endregion


        #region Main API

        /// <summary>End of a frame: registers new objects and schedules the capture job.</summary>
        public void EndFrame(in FrameClock clock)
        {
            Debug.Assert(!IsDisposed, "EndFrame on a disposed recorder.");
            if (_pending)
                Commit();

            if (Options.CaptureInterval > 0d && clock.RealTime - _lastCaptureTime < Options.CaptureInterval * 0.999d)
                return;
            _lastCaptureTime = clock.RealTime;

            Scan();
            History.BeginFrame(clock, SlotCount, StreamCount);
            Schedule();
        }

        /// <summary>
        /// Finishes the pending frame: waits for the job, records deaths and values, and closes the frame.
        /// Safe to call at any time; does nothing when no frame is pending.
        /// </summary>
        public void Commit()
        {
            if (!_pending)
                return;

            _handle.Complete();
            _pending = false;

            var sequence = History.NewestSequence + 1;
            ProcessDeaths(_counters[RecorderCounters.Died], sequence);
            SampleValues(_counters[RecorderCounters.Sampled], sequence);
            History.EndFrame(_counters[RecorderCounters.Written], _last, SlotCount, _values, StreamCount);
        }

        /// <summary>Captures one whole frame synchronously. Used by tests and the benchmark.</summary>
        public void CaptureNow(in FrameClock clock)
        {
            EndFrame(clock);
            Commit();
        }

        public void AddMarker(MarkerKind kind, string message, string stackTrace, double realTime)
        {
            Markers.Add(kind, message, stackTrace, MarkerSequence, realTime);
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            _handle.Complete();
            _pending = false;
            History.Trimmed -= OnHistoryTrimmed;
            History.Dispose();
            DisposeSlots();
            DisposeStreams();
            DisposeScanner();
            IsDisposed = true;
        }

        #endregion


        #region Tools and Utilities

        private void Schedule()
        {
            var capture = new CaptureTransformsJob { Last = _last, Current = _current, Flags = _flags };
            var pack = new PackTransformsJob
            {
                SlotCount = SlotCount,
                RingHead = History.Transforms.Head,
                Last = _last,
                Current = _current,
                Flags = _flags,
                Alive = _alive,
                Moving = _moving,
                Ring = History.Transforms.Items,
                Died = _died,
                SampleSlots = _sampleSlots,
                Counters = _counters
            };

            var captured = capture.ScheduleReadOnly(_access, CaptureBatchSize);
            _handle = pack.Schedule(captured);
            JobHandle.ScheduleBatchedJobs();
            _pending = true;
        }

        private void OnHistoryTrimmed(int oldestSequence)
        {
            Markers.TrimBefore(oldestSequence);
        }

        private static void Grow<T>(ref NativeArray<T> array, int capacity, NativeArrayOptions options) where T : struct
        {
            var grown = new NativeArray<T>(capacity, Allocator.Persistent, options);
            if (array.IsCreated)
            {
                NativeArray<T>.Copy(array, grown, Math.Min(array.Length, capacity));
                array.Dispose();
            }
            array = grown;
        }

        private static void Release<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
                array.Dispose();
        }

        private static void GrowManaged<T>(ref T[] array, int capacity, T fill)
        {
            var previous = array?.Length ?? 0;
            Array.Resize(ref array, capacity);
            for (var i = previous; i < capacity; i++)
                array[i] = fill;
        }

        #endregion
    }
}
