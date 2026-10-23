using System;
using System.Diagnostics;

namespace Nekuzaky.Hindsight.Editor
{
    internal sealed class RecorderStats
    {
        #region Public

        public const int Window = 600;

        public int CapturedFrames { get; private set; }
        public double LastMilliseconds { get; private set; }
        public double AverageMilliseconds { get; private set; }
        public long LastAllocatedBytes { get; private set; }
        public long TotalAllocatedBytes { get; private set; }
        public int SampleCount => Math.Min(CapturedFrames, Window);

        public static double TicksToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

        #endregion


        #region Private and Protected

        private const double Smoothing = 0.05d;

        private readonly double[] _recent = new double[Window];
        private readonly double[] _sorted = new double[Window];

        #endregion


        #region Main API

        public void AddFrame(long ticks, long allocatedBytes)
        {
            var milliseconds = TicksToMilliseconds(ticks);
            _recent[CapturedFrames % Window] = milliseconds;
            CapturedFrames++;
            LastMilliseconds = milliseconds;
            AverageMilliseconds = CapturedFrames == 1
                ? milliseconds
                : AverageMilliseconds + (milliseconds - AverageMilliseconds) * Smoothing;
            LastAllocatedBytes = allocatedBytes;
            TotalAllocatedBytes += allocatedBytes;
        }

        public double Percentile(double fraction)
        {
            var count = SampleCount;
            if (count == 0)
                return 0d;

            Array.Copy(_recent, _sorted, count);
            Array.Sort(_sorted, 0, count);
            var index = (int)Math.Round(Math.Clamp(fraction, 0d, 1d) * (count - 1));
            return _sorted[index];
        }

        public double Max()
        {
            var max = 0d;
            for (var i = 0; i < SampleCount; i++)
                max = Math.Max(max, _recent[i]);
            return max;
        }

        public void Reset()
        {
            CapturedFrames = 0;
            LastMilliseconds = 0d;
            AverageMilliseconds = 0d;
            LastAllocatedBytes = 0;
            TotalAllocatedBytes = 0;
        }

        #endregion
    }
}
