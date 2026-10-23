using System;

namespace Nekuzaky.Hindsight.Editor
{
    internal sealed class TimeAxis
    {
        #region Public

        public const double MinSpan = 0.05d;

        public double Start { get; private set; }
        public double End { get; private set; } = 1d;
        public double Span => End - Start;
        public bool Follow { get; private set; } = true;
        public bool ShowsEverything => _fitAll;

        public double Oldest { get; private set; }
        public double Newest { get; private set; }

        public event Action Changed;

        #endregion


        #region Private and Protected

        private const double Epsilon = 1e-6d;

        private bool _fitAll = true;
        private double _span;

        #endregion


        #region Main API

        public float ToX(double time, float width) => (float)((time - Start) / Math.Max(Span, Epsilon) * width);

        public double ToTime(float x, float width) => Start + x / Math.Max(width, 1f) * Span;

        public void SetBounds(double oldest, double newest)
        {
            Oldest = oldest;
            Newest = Math.Max(newest, oldest);
            var available = Math.Max(Newest - Oldest, MinSpan);

            if (Follow)
            {
                var span = _fitAll ? available : Math.Min(_span, available);
                Apply(Newest - span, Newest);
                return;
            }

            var width = Math.Min(Span, available);
            var start = Math.Max(Start, Oldest);
            if (start + width > Newest)
                start = Newest - width;
            Apply(start, start + width);
        }

        public void Zoom(double factor, double anchor)
        {
            var available = Math.Max(Newest - Oldest, MinSpan);
            var span = Math.Clamp(Span * factor, MinSpan, available);
            var ratio = Span > Epsilon ? (anchor - Start) / Span : 1d;
            var start = anchor - ratio * span;
            _fitAll = span >= available - Epsilon;
            _span = span;
            Place(start, span);
        }

        public void Pan(double seconds)
        {
            _fitAll = false;
            _span = Span;
            Place(Start + seconds, Span);
        }

        public void Fit()
        {
            _fitAll = true;
            Follow = true;
            SetBounds(Oldest, Newest);
        }

        public void Reveal(double time)
        {
            if (time >= Start && time <= End)
                return;
            Place(time - Span * 0.5d, Span);
        }

        #endregion


        #region Tools and Utilities

        private void Place(double start, double span)
        {
            start = Math.Max(Oldest, Math.Min(start, Newest - span));
            start = Math.Max(start, Oldest);
            Follow = start + span >= Newest - Epsilon;
            Apply(start, start + span);
        }

        private void Apply(double start, double end)
        {
            if (Math.Abs(start - Start) < Epsilon && Math.Abs(end - End) < Epsilon)
                return;
            Start = start;
            End = Math.Max(end, start + Epsilon);
            Changed?.Invoke();
        }

        #endregion
    }
}
