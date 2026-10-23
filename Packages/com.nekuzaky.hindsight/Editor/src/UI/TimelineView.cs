using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nekuzaky.Hindsight.Editor
{
    internal sealed class TimelineView : VisualElement
    {
        #region Public

        public const float RulerHeight = 22f;
        public const float EventLaneHeight = 26f;

        public TimelineView(TimeAxis axis)
        {
            _axis = axis;
            AddToClassList("hs-timeline");
            focusable = true;
            generateVisualContent += Draw;

            _hoverLabel = new Label { pickingMode = PickingMode.Ignore };
            _hoverLabel.AddToClassList("hs-timeline__tooltip");
            _playheadLabel = new Label { pickingMode = PickingMode.Ignore };
            _playheadLabel.AddToClassList("hs-timeline__playhead-label");
            Add(_hoverLabel);
            Add(_playheadLabel);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(_ => SetHover(-1f));
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<GeometryChangedEvent>(_ => Invalidate());
        }

        public MarkerFilter Filter { get; set; } = MarkerFilter.All;

        public event Action<int> FrameRequested;

        public event Action<int> MarkerClicked;

        #endregion


        #region Private and Protected

        private const float MarkerHitRadius = 7f;
        private const float MinTickSpacing = 64f;
        private const int MaxTickLabels = 48;
        private static readonly double[] TickSteps = { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300 };

        private readonly TimeAxis _axis;
        private readonly Label _hoverLabel;
        private readonly Label _playheadLabel;
        private readonly List<Label> _tickLabels = new();
        private float[] _columns = Array.Empty<float>();
        private float _columnMax = 1f;
        private float _hoverX = -1f;
        private bool _scrubbing;
        private bool _panning;
        private float _panOrigin;

        #endregion


        #region Main API

        public void Invalidate()
        {
            ComputeColumns();
            UpdateTickLabels();
            UpdatePlayheadLabel();
            MarkDirtyRepaint();
        }

        #endregion


        #region Drawing

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width < 2f || rect.height < 2f)
                return;

            var painter = context.painter2D;
            var activityTop = RulerHeight + EventLaneHeight;
            FillRect(painter, new Rect(0f, 0f, rect.width, RulerHeight), Palette.Background);
            FillRect(painter, new Rect(0f, RulerHeight, rect.width, EventLaneHeight), Palette.Lane);
            FillRect(painter, new Rect(0f, activityTop, rect.width, rect.height - activityTop), Palette.LaneAlt);

            var recorder = HindsightSession.Recorder;
            if (recorder == null || recorder.History.IsEmpty)
                return;

            DrawUnrecorded(painter, rect);
            DrawTicks(painter, rect);
            DrawActivity(painter, rect, activityTop);
            DrawMarkers(painter, recorder, rect);
            DrawPlayhead(painter, recorder, rect);
            if (_hoverX >= 0f)
                StrokeLine(painter, new Vector2(_hoverX, 0f), new Vector2(_hoverX, rect.height), Palette.Hover, 1f);
        }

        private void DrawUnrecorded(Painter2D painter, Rect rect)
        {
            var left = _axis.ToX(_axis.Oldest, rect.width);
            if (left > 0f)
                FillRect(painter, new Rect(0f, 0f, left, rect.height), Palette.Unrecorded);
        }

        private void DrawTicks(Painter2D painter, Rect rect)
        {
            var step = TickStep(rect.width);
            var first = Math.Ceiling((_axis.Start - _axis.Newest) / step) * step + _axis.Newest;
            for (var i = 0; i < MaxTickLabels * 4; i++)
            {
                var time = first + i * step;
                if (time > _axis.End)
                    break;
                var x = _axis.ToX(time, rect.width);
                var major = Math.Abs(Math.IEEERemainder(time - _axis.Newest, step * 5d)) < step * 0.01d;
                StrokeLine(painter, new Vector2(x, major ? 6f : 13f), new Vector2(x, RulerHeight), Palette.GridStrong, 1f);
                StrokeLine(painter, new Vector2(x, RulerHeight), new Vector2(x, rect.height), Palette.Grid, 1f);
            }
            StrokeLine(painter, new Vector2(0f, RulerHeight), new Vector2(rect.width, RulerHeight), Palette.Border, 1f);
        }

        private void DrawActivity(Painter2D painter, Rect rect, float top)
        {
            var height = rect.height - top - 4f;
            painter.fillColor = Palette.Activity;
            painter.BeginPath();
            var count = Math.Min(_columns.Length, (int)rect.width);
            for (var x = 0; x < count; x++)
            {
                if (_columns[x] <= 0f)
                    continue;
                var bar = Mathf.Max(1.5f, Mathf.Sqrt(_columns[x] / _columnMax) * height);
                var bottom = rect.height - 2f;
                painter.MoveTo(new Vector2(x, bottom));
                painter.LineTo(new Vector2(x + 1f, bottom));
                painter.LineTo(new Vector2(x + 1f, bottom - bar));
                painter.LineTo(new Vector2(x, bottom - bar));
                painter.ClosePath();
            }
            painter.Fill();
        }

        private void DrawMarkers(Painter2D painter, Recorder recorder, Rect rect)
        {
            var history = recorder.History;
            var markers = recorder.Markers;
            var middle = RulerHeight + EventLaneHeight * 0.5f;
            for (var i = 0; i < markers.Count; i++)
            {
                ref readonly var marker = ref markers[i];
                if (!Filter.Accepts(marker.Kind) || !history.Contains(marker.Sequence))
                    continue;

                var x = _axis.ToX(history.Frame(marker.Sequence).RealTime, rect.width);
                if (marker.LastSequence > marker.Sequence && history.Contains(marker.LastSequence))
                {
                    var end = _axis.ToX(history.Frame(marker.LastSequence).RealTime, rect.width);
                    var color = Palette.Of(marker.Kind);
                    color.a = 0.45f;
                    FillRect(painter, new Rect(x, middle - 1.5f, Mathf.Max(1f, end - x), 3f), color);
                }
                if (x < -8f || x > rect.width + 8f)
                    continue;
                DrawMarkerShape(painter, marker.Kind, new Vector2(x, middle), rect.height);
            }
        }

        private static void DrawMarkerShape(Painter2D painter, MarkerKind kind, Vector2 center, float height)
        {
            var color = Palette.Of(kind);
            painter.fillColor = color;
            painter.BeginPath();
            switch (kind)
            {
                case MarkerKind.Bookmark:
                    var line = color;
                    line.a = 0.5f;
                    StrokeLine(painter, new Vector2(center.x, RulerHeight), new Vector2(center.x, height), line, 1f);
                    painter.fillColor = color;
                    painter.BeginPath();
                    painter.MoveTo(center + new Vector2(0f, -6f));
                    painter.LineTo(center + new Vector2(5f, 0f));
                    painter.LineTo(center + new Vector2(0f, 6f));
                    painter.LineTo(center + new Vector2(-5f, 0f));
                    break;
                case MarkerKind.Warning:
                    painter.MoveTo(center + new Vector2(0f, -5.5f));
                    painter.LineTo(center + new Vector2(5.5f, 4.5f));
                    painter.LineTo(center + new Vector2(-5.5f, 4.5f));
                    break;
                case MarkerKind.Log:
                    painter.Arc(center, 2.5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    break;
                case MarkerKind.Spawn:
                    painter.MoveTo(center + new Vector2(0f, -4f));
                    painter.LineTo(center + new Vector2(4f, 3f));
                    painter.LineTo(center + new Vector2(-4f, 3f));
                    break;
                case MarkerKind.Destroy:
                    painter.MoveTo(center + new Vector2(-4f, -3f));
                    painter.LineTo(center + new Vector2(4f, -3f));
                    painter.LineTo(center + new Vector2(0f, 4f));
                    break;
                case MarkerKind.SceneLoad:
                    var scene = color;
                    scene.a = 0.35f;
                    StrokeLine(painter, new Vector2(center.x, RulerHeight), new Vector2(center.x, height), scene, 1f);
                    painter.fillColor = color;
                    painter.BeginPath();
                    painter.MoveTo(center + new Vector2(-3.5f, -3.5f));
                    painter.LineTo(center + new Vector2(3.5f, -3.5f));
                    painter.LineTo(center + new Vector2(3.5f, 3.5f));
                    painter.LineTo(center + new Vector2(-3.5f, 3.5f));
                    break;
                default:
                    painter.Arc(center, 4.5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                    break;
            }
            painter.ClosePath();
            painter.Fill();
        }

        private void DrawPlayhead(Painter2D painter, Recorder recorder, Rect rect)
        {
            var rewinding = Navigation.IsRewinding;
            var sequence = Navigation.Displayed;
            if (!recorder.History.Contains(sequence))
                return;

            var x = _axis.ToX(recorder.History.Frame(sequence).RealTime, rect.width);
            var color = rewinding ? Palette.Accent : Palette.Live;
            if (rewinding)
            {
                var right = _axis.ToX(_axis.Newest, rect.width);
                FillRect(painter, new Rect(x, RulerHeight, Mathf.Max(0f, right - x), rect.height - RulerHeight), Palette.AccentSoft);
            }
            StrokeLine(painter, new Vector2(x, 0f), new Vector2(x, rect.height), color, 2f);
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x - 6f, 0f));
            painter.LineTo(new Vector2(x + 6f, 0f));
            painter.LineTo(new Vector2(x, 8f));
            painter.ClosePath();
            painter.Fill();
        }

        private static void FillRect(Painter2D painter, Rect rect, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Fill();
        }

        private static void StrokeLine(Painter2D painter, Vector2 from, Vector2 to, Color color, float width)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        #endregion


        #region Layout helpers

        private double TickStep(float width)
        {
            foreach (var step in TickSteps)
            {
                if (_axis.Span / step * MinTickSpacing <= width)
                    return step;
            }
            return TickSteps[TickSteps.Length - 1];
        }

        private void UpdateTickLabels()
        {
            var width = contentRect.width;
            var used = 0;
            if (HindsightSession.Recorder != null && !HindsightSession.Recorder.History.IsEmpty && width > 2f)
            {
                var step = TickStep(width);
                var first = Math.Ceiling((_axis.Start - _axis.Newest) / step) * step + _axis.Newest;
                for (var i = 0; i < MaxTickLabels; i++)
                {
                    var time = first + i * step;
                    if (time > _axis.End + step * 0.01d)
                        break;
                    var label = TickLabel(used++);
                    label.text = Format.Tick(time - _axis.Newest, step);
                    label.style.left = _axis.ToX(time, width) + 3f;
                }
            }

            for (var i = 0; i < _tickLabels.Count; i++)
                _tickLabels[i].style.display = i < used ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private Label TickLabel(int index)
        {
            if (index < _tickLabels.Count)
                return _tickLabels[index];

            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("hs-timeline__tick");
            _tickLabels.Add(label);
            Insert(0, label);
            return label;
        }

        private void UpdatePlayheadLabel()
        {
            var recorder = HindsightSession.Recorder;
            var sequence = Navigation.Displayed;
            if (recorder == null || !recorder.History.Contains(sequence))
            {
                _playheadLabel.style.display = DisplayStyle.None;
                return;
            }

            var rewinding = Navigation.IsRewinding;
            var time = recorder.History.Frame(sequence).RealTime;
            _playheadLabel.style.display = DisplayStyle.Flex;
            _playheadLabel.text = rewinding ? Format.Offset(time - _axis.Newest) : "LIVE";
            _playheadLabel.EnableInClassList("hs-timeline__playhead-label--live", !rewinding);
            var x = _axis.ToX(time, contentRect.width);
            _playheadLabel.style.left = Mathf.Clamp(x + 6f, 0f, Mathf.Max(0f, contentRect.width - 76f));
        }

        private void ComputeColumns()
        {
            var width = Mathf.Max(0, (int)contentRect.width);
            if (_columns.Length != width)
                _columns = new float[width];
            Array.Clear(_columns, 0, _columns.Length);
            _columnMax = 1f;

            var recorder = HindsightSession.Recorder;
            if (recorder == null || recorder.History.IsEmpty || width == 0)
                return;

            var history = recorder.History;
            var sequence = Math.Max(history.OldestSequence, history.SequenceNearest(_axis.Start) - 1);
            for (var guard = 0; sequence <= history.NewestSequence && guard < history.MaxFrames; guard++, sequence++)
            {
                ref readonly var frame = ref history.Frame(sequence);
                if (frame.RealTime > _axis.End)
                    break;
                var column = (int)_axis.ToX(frame.RealTime, width);
                if (column < 0 || column >= width)
                    continue;
                var amount = frame.TransformCount + frame.ValueCount;
                if (amount > _columns[column])
                    _columns[column] = amount;
                if (amount > _columnMax)
                    _columnMax = amount;
            }
        }

        #endregion


        #region Input

        private void OnPointerDown(PointerDownEvent evt)
        {
            Focus();
            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                _panning = true;
                _panOrigin = evt.localPosition.x;
                this.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }
            if (evt.button != 0)
                return;

            if (evt.clickCount == 2)
            {
                _axis.Fit();
                evt.StopPropagation();
                return;
            }

            var marker = MarkerAt(evt.localPosition);
            if (marker >= 0)
            {
                MarkerClicked?.Invoke(marker);
                evt.StopPropagation();
                return;
            }

            _scrubbing = true;
            this.CapturePointer(evt.pointerId);
            RequestAt(evt.localPosition.x);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            SetHover(evt.localPosition.x, evt.localPosition);
            if (_panning)
            {
                var dx = evt.localPosition.x - _panOrigin;
                _panOrigin = evt.localPosition.x;
                _axis.Pan(-dx / Mathf.Max(1f, contentRect.width) * _axis.Span);
            }
            else if (_scrubbing)
            {
                RequestAt(evt.localPosition.x);
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_scrubbing && !_panning)
                return;
            _scrubbing = false;
            _panning = false;
            this.ReleasePointer(evt.pointerId);
        }

        private void OnWheel(WheelEvent evt)
        {
            var width = Mathf.Max(1f, contentRect.width);
            if (evt.shiftKey || Mathf.Abs(evt.delta.x) > Mathf.Abs(evt.delta.y))
            {
                var amount = Mathf.Abs(evt.delta.x) > 0f ? evt.delta.x : evt.delta.y;
                _axis.Pan(amount * 0.05d * _axis.Span);
            }
            else
            {
                var anchor = _axis.ToTime(evt.localMousePosition.x, width);
                _axis.Zoom(evt.delta.y > 0f ? 1.2d : 1d / 1.2d, anchor);
            }
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow when evt.shiftKey: Navigation.PreviousEvent(Filter); break;
                case KeyCode.RightArrow when evt.shiftKey: Navigation.NextEvent(Filter); break;
                case KeyCode.LeftArrow: Navigation.StepBack(); break;
                case KeyCode.RightArrow: Navigation.StepForward(); break;
                case KeyCode.Home: Navigation.First(); break;
                case KeyCode.End: Navigation.Live(); break;
                case KeyCode.F: _axis.Fit(); break;
                default: return;
            }
            evt.StopPropagation();
        }

        private void RequestAt(float x)
        {
            var recorder = HindsightSession.Recorder;
            if (recorder == null || recorder.History.IsEmpty)
                return;
            var sequence = recorder.History.SequenceNearest(_axis.ToTime(x, contentRect.width));
            if (sequence >= 0)
                FrameRequested?.Invoke(sequence);
        }

        private int MarkerAt(Vector2 position)
        {
            var recorder = HindsightSession.Recorder;
            if (recorder == null || position.y < RulerHeight - 4f || position.y > RulerHeight + EventLaneHeight + 4f)
                return -1;

            var history = recorder.History;
            var markers = recorder.Markers;
            var best = -1;
            var bestDistance = MarkerHitRadius;
            for (var i = 0; i < markers.Count; i++)
            {
                ref readonly var marker = ref markers[i];
                if (!Filter.Accepts(marker.Kind) || !history.Contains(marker.Sequence))
                    continue;
                var distance = Mathf.Abs(_axis.ToX(history.Frame(marker.Sequence).RealTime, contentRect.width) - position.x);
                if (distance <= bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private void SetHover(float x, Vector2 position = default)
        {
            _hoverX = x;
            var recorder = HindsightSession.Recorder;
            if (x < 0f || recorder == null || recorder.History.IsEmpty)
            {
                _hoverLabel.style.display = DisplayStyle.None;
                MarkDirtyRepaint();
                return;
            }

            var marker = MarkerAt(position);
            var time = _axis.ToTime(x, contentRect.width);
            _hoverLabel.text = marker >= 0
                ? MarkerTooltip(recorder.Markers[marker])
                : Format.Offset(time - _axis.Newest);
            _hoverLabel.style.display = DisplayStyle.Flex;
            _hoverLabel.style.left = Mathf.Clamp(x + 10f, 0f, Mathf.Max(0f, contentRect.width - 260f));
            MarkDirtyRepaint();
        }

        private static string MarkerTooltip(in Marker marker)
        {
            var title = marker.Title;
            if (title.Length > 90)
                title = title.Substring(0, 90) + "…";
            return marker.Count > 1 ? $"{title}  ×{Format.Integer(marker.Count)}" : title;
        }

        #endregion
    }
}
