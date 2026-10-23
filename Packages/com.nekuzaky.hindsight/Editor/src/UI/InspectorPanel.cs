using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// The selected object at the displayed frame: transform, velocities and tracked values, plus a graph of one of
    /// them over the whole recording.
    /// </summary>
    internal sealed class InspectorPanel : VisualElement
    {
        #region Public

        public InspectorPanel(TimeAxis axis)
        {
            AddToClassList("hs-panel");
            AddToClassList("hs-inspector");
            Add(BuildHeader());

            _status = new Label();
            _status.AddToClassList("hs-inspector__status");
            Add(_status);

            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.AddToClassList("hs-inspector__body");
            Add(_body);

            _transformSection = Section("Transform", Glyphs.Object);
            _position = Row(_transformSection, "Position");
            _rotation = Row(_transformSection, "Rotation");
            _scale = Row(_transformSection, "Scale");
            _valuesSection = Section("Values", Glyphs.Speed);
            _graphSection = Section("Graph", Glyphs.Graph);
            BuildGraph(axis);

            _empty = new Label();
            _empty.AddToClassList("hs-empty-hint");
            Add(_empty);
        }

        public event Action<int> FrameRequested;

        /// <summary>Raised with a message to show, for example after copying a transform.</summary>
        public event Action<string> Notify;

        #endregion


        #region Private and Protected

        private const double LiveGraphInterval = 0.25d;

        private struct Channel
        {
            public string Label;
            public int Stream;
            public int Axis;
            public string Unit;
        }

        private readonly Label _status;
        private readonly ScrollView _body;
        private readonly VisualElement _transformSection;
        private readonly VisualElement _valuesSection;
        private readonly VisualElement _graphSection;
        private readonly Label _position;
        private readonly Label _rotation;
        private readonly Label _scale;
        private readonly Label _empty;
        private readonly List<int> _streams = new();
        private readonly List<Channel> _channels = new();
        private readonly List<string> _channelNames = new();
        private readonly List<Label> _valueRows = new();

        private Label _name;
        private Label _path;
        private DropdownField _channelField;
        private GraphView _graph;
        private TransformSample[] _samples = Array.Empty<TransformSample>();
        private Vector4[] _rawValues = Array.Empty<Vector4>();
        private float[] _series = Array.Empty<float>();
        private double[] _times = Array.Empty<double>();
        private int _slot = -1;
        private string _preferredChannel = "Speed";
        private int _graphOldest = -1;
        private int _graphNewest = -1;
        private int _graphChannel = -1;
        private double _lastGraphTime;

        #endregion


        #region Main API

        public void Refresh()
        {
            var recorder = HindsightSession.Recorder;
            var transform = Selection.activeTransform;
            if (recorder == null || transform == null || !recorder.TryGetSlot(transform, out var slot))
            {
                ShowEmpty(recorder, transform);
                return;
            }

            if (slot != _slot)
            {
                _slot = slot;
                RebuildChannels(recorder, slot);
            }

            _empty.style.display = DisplayStyle.None;
            _body.style.display = DisplayStyle.Flex;
            _name.text = transform.name;
            _path.text = PathOf(transform);
            ShowFrame(recorder, slot);
            RefreshGraph(recorder, slot);
        }

        #endregion


        #region Building

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("hs-panel__header");
            header.Add(Glyphs.Icon(Glyphs.Object, "hs-icon--dim"));

            var titles = new VisualElement();
            titles.AddToClassList("hs-inspector__titles");
            _name = new Label("Selection");
            _name.AddToClassList("hs-panel__title");
            _path = new Label();
            _path.AddToClassList("hs-inspector__path");
            titles.Add(_name);
            titles.Add(_path);
            header.Add(titles);

            header.Add(IconButton(Glyphs.Frame, "Frame the selection in the Scene view", FrameSelection));
            header.Add(IconButton(Glyphs.Copy, "Copy the Transform as it is at this frame, to paste after Play Mode", CopyTransform));
            return header;
        }

        private void BuildGraph(TimeAxis axis)
        {
            _channelField = new DropdownField(_channelNames, 0);
            _channelField.AddToClassList("hs-inspector__channel");
            _channelField.RegisterValueChangedCallback(evt =>
            {
                _preferredChannel = evt.newValue;
                _graphChannel = -1;
                Refresh();
            });
            _graphSection.Add(_channelField);

            _graph = new GraphView(axis);
            _graph.FrameRequested += sequence => FrameRequested?.Invoke(sequence);
            _graphSection.Add(_graph);
        }

        private VisualElement Section(string title, string glyph)
        {
            var section = new VisualElement();
            section.AddToClassList("hs-section");
            var header = new VisualElement();
            header.AddToClassList("hs-section__header");
            header.Add(Glyphs.Icon(glyph, "hs-icon--dim", "hs-icon--small"));
            var label = new Label(title);
            label.AddToClassList("hs-section__title");
            header.Add(label);
            section.Add(header);
            _body.Add(section);
            return section;
        }

        private static Label Row(VisualElement section, string name)
        {
            var row = new VisualElement();
            row.AddToClassList("hs-row");
            var label = new Label(name);
            label.AddToClassList("hs-row__name");
            var value = new Label();
            value.AddToClassList("hs-row__value");
            value.selection.isSelectable = true;
            row.Add(label);
            row.Add(value);
            section.Add(row);
            return value;
        }

        private static Button IconButton(string glyph, string tooltip, Action action)
        {
            var button = new Button(action) { tooltip = tooltip };
            button.AddToClassList("hs-icon-button");
            button.Add(Glyphs.Icon(glyph));
            return button;
        }

        #endregion


        #region Tools and Utilities

        private void ShowEmpty(Recorder recorder, Transform transform)
        {
            _slot = -1;
            _body.style.display = DisplayStyle.None;
            _empty.style.display = DisplayStyle.Flex;
            _name.text = transform != null ? transform.name : "Selection";
            _path.text = transform != null ? PathOf(transform) : string.Empty;
            _status.text = string.Empty;
            _graph.ClearSeries();

            if (recorder == null)
                _empty.text = "Select an object to inspect it frame by frame once recording starts.";
            else if (transform == null)
                _empty.text = "Select an object in the Hierarchy or the Scene view to see its values at the displayed frame.";
            else
                _empty.text = NotRecordedReason(recorder, transform);
        }

        private static string NotRecordedReason(Recorder recorder, Transform transform)
        {
            var settings = HindsightSettings.instance;
            if (settings.IgnoreStaticObjects && transform.gameObject.isStatic)
                return "This object is static. Turn off \"Ignore static objects\" in the preferences to record it.";
            if ((settings.LayerMask & (1 << transform.gameObject.layer)) == 0)
                return "This object's layer is excluded in the preferences.";
            if (settings.IgnoreCameras && transform.TryGetComponent<Camera>(out _))
                return "Cameras are excluded in the preferences.";
            if (recorder.LimitReached)
                return "The object limit or memory budget was reached before this object appeared.";
            return "This object is not recorded yet. It will be picked up on the next captured frame.";
        }

        private void ShowFrame(Recorder recorder, int slot)
        {
            var rewind = HindsightSession.Rewind;
            var sequence = rewind.Sequence;
            var existed = recorder.ExistedAt(slot, sequence);
            var history = recorder.History;
            if (!existed && history.Contains(recorder.BirthOf(slot)) && history.Contains(sequence))
            {
                var later = history.Frame(recorder.BirthOf(slot)).RealTime - history.Frame(sequence).RealTime;
                _status.text = $"Not spawned yet at this frame: it appears {Format.Number((float)later)} s later.";
            }
            else
            {
                _status.text = existed ? string.Empty : "Not spawned yet at this frame.";
            }

            var sample = rewind.Transforms[slot];
            _position.text = sample.IsSet ? Format.Vector(sample.Position) : "—";
            _rotation.text = sample.IsSet ? Format.Vector(sample.Rotation.eulerAngles) : "—";
            _scale.text = sample.IsSet ? Format.Vector(sample.Scale) : "—";

            var values = rewind.Values;
            for (var i = 0; i < _streams.Count && i < _valueRows.Count; i++)
            {
                var stream = _streams[i];
                _valueRows[i].text = Format.Value(values[stream], recorder.FormatOf(stream), recorder.EnumTypeOf(stream));
            }
        }

        private void RebuildChannels(Recorder recorder, int slot)
        {
            recorder.GetStreams(slot, _streams);
            _valuesSection.Clear();
            _valueRows.Clear();
            var header = new VisualElement();
            header.AddToClassList("hs-section__header");
            header.Add(Glyphs.Icon(Glyphs.Speed, "hs-icon--dim", "hs-icon--small"));
            var title = new Label("Values");
            title.AddToClassList("hs-section__title");
            header.Add(title);
            _valuesSection.Add(header);

            foreach (var stream in _streams)
                _valueRows.Add(Row(_valuesSection, recorder.LabelOf(stream)));
            if (_streams.Count == 0)
            {
                var hint = new Label("No velocity or tracked field on this object. Add [HindsightTrack] to a field, or right-click a field in the Inspector and choose Track in Hindsight.");
                hint.AddToClassList("hs-section__hint");
                _valuesSection.Add(hint);
            }

            BuildChannelList(recorder);
            _graphChannel = -1;
        }

        private void BuildChannelList(Recorder recorder)
        {
            _channels.Clear();
            foreach (var stream in _streams)
            {
                var format = recorder.FormatOf(stream);
                var unit = format == ValueFormat.LinearVelocity ? " m/s" : format == ValueFormat.AngularVelocity2D ? " °/s" : string.Empty;
                var label = format == ValueFormat.LinearVelocity ? "Speed" : recorder.LabelOf(stream);
                _channels.Add(new Channel { Label = label, Stream = stream, Axis = -1, Unit = unit });
            }
            _channels.Add(new Channel { Label = "Position X", Stream = -1, Axis = 0 });
            _channels.Add(new Channel { Label = "Position Y", Stream = -1, Axis = 1 });
            _channels.Add(new Channel { Label = "Position Z", Stream = -1, Axis = 2 });

            _channelNames.Clear();
            var selected = 0;
            for (var i = 0; i < _channels.Count; i++)
            {
                _channelNames.Add(_channels[i].Label);
                if (_channels[i].Label == _preferredChannel)
                    selected = i;
            }
            _channelField.choices = _channelNames;
            _channelField.SetValueWithoutNotify(_channelNames[selected]);
        }

        private void RefreshGraph(Recorder recorder, int slot)
        {
            var history = recorder.History;
            var channel = Math.Max(0, _channelNames.IndexOf(_channelField.value));
            var stale = channel != _graphChannel || history.OldestSequence != _graphOldest || history.NewestSequence != _graphNewest;
            var now = EditorApplication.timeSinceStartup;
            if (stale && (EditorApplication.isPaused || channel != _graphChannel || now - _lastGraphTime >= LiveGraphInterval))
            {
                _lastGraphTime = now;
                _graphChannel = channel;
                _graphOldest = history.OldestSequence;
                _graphNewest = history.NewestSequence;
                LoadSeries(recorder, slot, _channels[channel]);
            }

            var current = _channels[channel];
            var sequence = HindsightSession.Rewind.Sequence;
            string text;
            if (current.Stream >= 0)
            {
                var value = HindsightSession.Rewind.Values[current.Stream];
                text = ValueSample.IsSet(value) ? Format.Number(Format.Scalar(value, recorder.FormatOf(current.Stream))) + current.Unit : "—";
            }
            else
            {
                var sample = HindsightSession.Rewind.Transforms[slot];
                text = sample.IsSet ? Format.Number(sample.Position[current.Axis]) : "—";
            }
            _graph.SetCurrent(history.Contains(sequence) ? text : string.Empty);
        }

        private void LoadSeries(Recorder recorder, int slot, Channel channel)
        {
            var history = recorder.History;
            var count = history.FrameCount;
            if (_series.Length < count)
            {
                var size = Mathf.NextPowerOfTwo(count);
                _series = new float[size];
                _times = new double[size];
                _samples = new TransformSample[size];
                _rawValues = new Vector4[size];
            }

            for (var i = 0; i < count; i++)
                _times[i] = history.Frame(history.OldestSequence + i).RealTime;

            if (channel.Stream >= 0)
            {
                count = recorder.ExtractStream(channel.Stream, _rawValues);
                var format = recorder.FormatOf(channel.Stream);
                for (var i = 0; i < count; i++)
                    _series[i] = ValueSample.IsSet(_rawValues[i]) ? Format.Scalar(_rawValues[i], format) : float.NaN;
            }
            else
            {
                count = recorder.ExtractSlot(slot, _samples);
                for (var i = 0; i < count; i++)
                    _series[i] = _samples[i].IsSet ? _samples[i].Position[channel.Axis] : float.NaN;
            }
            _graph.SetSeries(_times, _series, count, channel.Unit);
        }

        private static void FrameSelection()
        {
            if (Selection.activeTransform != null)
                SceneView.FrameLastActiveSceneView();
        }

        private void CopyTransform()
        {
            var transform = Selection.activeTransform;
            if (transform == null || !UnityEditorInternal.ComponentUtility.CopyComponent(transform))
                return;
            Notify?.Invoke("Transform copied. After Play Mode, right-click the Transform and choose Paste Component Values.");
        }

        private static string PathOf(Transform transform)
        {
            var path = transform.name;
            var current = transform.parent;
            for (var depth = 0; current != null && depth < 32; depth++)
            {
                path = current.name + " / " + path;
                current = current.parent;
            }
            return path;
        }

        #endregion
    }
}
