using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nekuzaky.Hindsight.Editor
{
    internal sealed class GraphView : VisualElement
    {
        #region Public

        public GraphView(TimeAxis axis)
        {
            _axis = axis;
            AddToClassList("hs-graph");
            generateVisualContent += Draw;

            _maxLabel = AddLabel("hs-graph__label hs-graph__label--top");
            _minLabel = AddLabel("hs-graph__label hs-graph__label--bottom");
            _valueLabel = AddLabel("hs-graph__value");

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        public event Action<int> FrameRequested;

        #endregion


        #region Private and Protected

        private readonly TimeAxis _axis;
        private readonly Label _maxLabel;
        private readonly Label _minLabel;
        private readonly Label _valueLabel;

        private float[] _values = Array.Empty<float>();
        private double[] _times = Array.Empty<double>();
        private int _count;
        private float _min;
        private float _max;
        private string _unit = string.Empty;
        private bool _scrubbing;

        #endregion


        #region Main API

        public void SetSeries(double[] times, float[] values, int count, string unit)
        {
            if (_values.Length < count)
            {
                _values = new float[Mathf.NextPowerOfTwo(count)];
                _times = new double[_values.Length];
            }
            Array.Copy(values, _values, count);
            Array.Copy(times, _times, count);
            _count = count;
            _unit = unit ?? string.Empty;
            ComputeRange();
            MarkDirtyRepaint();
        }

        public void ClearSeries()
        {
            _count = 0;
            _maxLabel.text = string.Empty;
            _minLabel.text = string.Empty;
            _valueLabel.text = string.Empty;
            MarkDirtyRepaint();
        }

        public void SetCurrent(string text)
        {
            _valueLabel.text = text;
            MarkDirtyRepaint();
        }

        #endregion


        #region Tools and Utilities

        private Label AddLabel(string classes)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            foreach (var name in classes.Split(' '))
                label.AddToClassList(name);
            Add(label);
            return label;
        }

        private void ComputeRange()
        {
            _min = float.PositiveInfinity;
            _max = float.NegativeInfinity;
            for (var i = 0; i < _count; i++)
            {
                var value = _values[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                    continue;
                _min = Mathf.Min(_min, value);
                _max = Mathf.Max(_max, value);
            }

            if (_min > _max)
            {
                _min = 0f;
                _max = 1f;
            }
            if (Mathf.Approximately(_min, _max))
            {
                _min -= 0.5f;
                _max += 0.5f;
            }
            _maxLabel.text = Format.Number(_max) + _unit;
            _minLabel.text = Format.Number(_min) + _unit;
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width < 4f || rect.height < 4f)
                return;

            var painter = context.painter2D;
            DrawGrid(painter, rect);
            if (_count < 2)
                return;

            DrawSeries(painter, rect);
            DrawPlayhead(painter, rect);
        }

        private static void DrawGrid(Painter2D painter, Rect rect)
        {
            painter.strokeColor = Palette.Grid;
            painter.lineWidth = 1f;
            painter.BeginPath();
            for (var i = 1; i < 4; i++)
            {
                var y = rect.height * i / 4f;
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(rect.width, y));
            }
            painter.Stroke();
        }

        private void DrawSeries(Painter2D painter, Rect rect)
        {
            var width = (int)rect.width;
            var top = 6f;
            var height = rect.height - 12f;
            var range = _max - _min;

            painter.strokeColor = Palette.Accent;
            painter.lineWidth = 1.5f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            var started = false;
            var index = 0;
            for (var x = 0; x < width; x++)
            {
                var columnEnd = _axis.ToTime(x + 1f, rect.width);
                float low = float.PositiveInfinity, high = float.NegativeInfinity;
                for (; index < _count && _times[index] < columnEnd; index++)
                {
                    if (_times[index] < _axis.Start || float.IsNaN(_values[index]))
                        continue;
                    low = Mathf.Min(low, _values[index]);
                    high = Mathf.Max(high, _values[index]);
                }
                if (low > high)
                    continue;

                var yHigh = top + (1f - (high - _min) / range) * height;
                var yLow = top + (1f - (low - _min) / range) * height;
                if (!started)
                {
                    painter.MoveTo(new Vector2(x, yHigh));
                    started = true;
                }
                painter.LineTo(new Vector2(x, yHigh));
                painter.LineTo(new Vector2(x, yLow));
            }
            painter.Stroke();
        }

        private void DrawPlayhead(Painter2D painter, Rect rect)
        {
            var recorder = HindsightSession.Recorder;
            var sequence = Navigation.Displayed;
            if (recorder == null || !recorder.History.Contains(sequence))
                return;

            var x = _axis.ToX(recorder.History.Frame(sequence).RealTime, rect.width);
            painter.strokeColor = Navigation.IsRewinding ? Palette.Accent : Palette.Live;
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, 0f));
            painter.LineTo(new Vector2(x, rect.height));
            painter.Stroke();
            _valueLabel.style.left = Mathf.Clamp(x + 6f, 0f, Mathf.Max(0f, rect.width - 140f));
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
                return;
            _scrubbing = true;
            this.CapturePointer(evt.pointerId);
            Request(evt.localPosition.x);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_scrubbing)
                Request(evt.localPosition.x);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_scrubbing)
                return;
            _scrubbing = false;
            this.ReleasePointer(evt.pointerId);
        }

        private void Request(float x)
        {
            var recorder = HindsightSession.Recorder;
            if (recorder == null || recorder.History.IsEmpty)
                return;
            var sequence = recorder.History.SequenceNearest(_axis.ToTime(x, contentRect.width));
            if (sequence >= 0)
                FrameRequested?.Invoke(sequence);
        }

        #endregion
    }
}
