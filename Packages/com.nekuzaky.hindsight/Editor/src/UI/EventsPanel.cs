using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// The list of recorded events with filters and search. Picking an event rewinds to it; its stack trace lines
    /// open the script at the right line.
    /// </summary>
    internal sealed class EventsPanel : VisualElement
    {
        #region Public

        public EventsPanel()
        {
            AddToClassList("hs-panel");
            AddToClassList("hs-events");
            Add(BuildHeader());

            _list = new ListView(_rows, RowHeight, MakeRow, BindRow)
            {
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            _list.AddToClassList("hs-events__list");
            _list.selectionChanged += OnSelectionChanged;
            Add(_list);

            _empty = new Label("No events yet. Errors, warnings and bookmarks show up here while you play.");
            _empty.AddToClassList("hs-empty-hint");
            Add(_empty);

            _details = BuildDetails();
            Add(_details);
            _details.style.display = DisplayStyle.None;
        }

        public MarkerFilter Filter => _filter;

        public event Action FilterChanged;

        /// <summary>A marker index the user picked in the list.</summary>
        public event Action<int> MarkerPicked;

        #endregion


        #region Private and Protected

        private const int RowHeight = 26;
        private const int MaxSourceLinks = 6;
        private static readonly MarkerKind[] Kinds = (MarkerKind[])Enum.GetValues(typeof(MarkerKind));
        private static readonly Regex SourceLine = new(@"\(at (?<path>[^:()]+\.cs):(?<line>\d+)\)", RegexOptions.Compiled);

        private readonly List<int> _rows = new();
        private readonly ListView _list;
        private readonly Label _empty;
        private readonly VisualElement _details;
        private readonly Dictionary<MarkerKind, (Button chip, Label count)> _chips = new();
        private Label _title;
        private Label _detailMessage;
        private VisualElement _detailLinks;
        private string _search = string.Empty;
        private MarkerFilter _filter = new() { Errors = true, Warnings = true, Bookmarks = true, Logs = true, Lifecycle = true };
        private int _builtVersion = -1;
        private Recorder _builtFor;

        #endregion


        #region Main API

        public void Refresh()
        {
            var recorder = HindsightSession.Recorder;
            if (recorder != _builtFor || (recorder != null && recorder.Markers.Version != _builtVersion))
                Rebuild(recorder);
            else
                _list.RefreshItems();
        }

        #endregion


        #region Building

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("hs-panel__header");

            header.Add(Glyphs.Icon(Glyphs.Activity, "hs-icon--dim"));
            _title = new Label("Events");
            _title.AddToClassList("hs-panel__title");
            header.Add(_title);

            var chips = new VisualElement();
            chips.AddToClassList("hs-chips");
            chips.Add(Chip(MarkerKind.Error, "Errors and exceptions"));
            chips.Add(Chip(MarkerKind.Warning, "Warnings"));
            chips.Add(Chip(MarkerKind.Bookmark, "Bookmarks"));
            chips.Add(Chip(MarkerKind.Log, "Logs"));
            chips.Add(Chip(MarkerKind.Spawn, "Objects appearing, objects destroyed and scene loads"));
            header.Add(chips);

            var search = new ToolbarSearchField();
            search.AddToClassList("hs-search");
            search.RegisterValueChangedCallback(evt =>
            {
                _search = evt.newValue ?? string.Empty;
                Rebuild(HindsightSession.Recorder);
            });
            header.Add(search);
            return header;
        }

        private Button Chip(MarkerKind kind, string tooltip)
        {
            var chip = new Button { tooltip = $"Show or hide {tooltip.ToLowerInvariant()}" };
            chip.AddToClassList("hs-chip");
            chip.AddToClassList(Palette.ClassOf(kind));
            chip.Add(Glyphs.Icon(Palette.GlyphOf(kind), "hs-icon--kind"));
            var count = new Label("0");
            count.AddToClassList("hs-chip__count");
            chip.Add(count);
            chip.clicked += () => Toggle(kind);
            _chips[kind] = (chip, count);
            UpdateChip(kind);
            return chip;
        }

        private VisualElement BuildDetails()
        {
            var details = new VisualElement();
            details.AddToClassList("hs-events__details");
            _detailMessage = new Label();
            _detailMessage.AddToClassList("hs-events__message");
            _detailMessage.selection.isSelectable = true;
            details.Add(_detailMessage);
            _detailLinks = new VisualElement();
            _detailLinks.AddToClassList("hs-events__links");
            details.Add(_detailLinks);
            return details;
        }

        private static VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("hs-event");
            row.Add(Glyphs.Icon(Glyphs.Error, "hs-icon--kind", "hs-event__icon"));
            var title = new Label { name = "title" };
            title.AddToClassList("hs-event__title");
            row.Add(title);
            var count = new Label { name = "count" };
            count.AddToClassList("hs-event__count");
            row.Add(count);
            var time = new Label { name = "time" };
            time.AddToClassList("hs-event__time");
            row.Add(time);
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            var recorder = HindsightSession.Recorder;
            if (recorder == null || index >= _rows.Count || _rows[index] >= recorder.Markers.Count)
                return;

            ref readonly var marker = ref recorder.Markers[_rows[index]];
            var icon = row.Q(className: "hs-event__icon");
            Glyphs.Set(icon, Palette.GlyphOf(marker.Kind));
            foreach (var kind in Kinds)
                row.RemoveFromClassList(Palette.ClassOf(kind));
            row.AddToClassList(Palette.ClassOf(marker.Kind));

            row.Q<Label>("title").text = marker.Title;
            row.tooltip = marker.Message;
            var count = row.Q<Label>("count");
            count.text = marker.Count > 1 ? "×" + Format.Integer(marker.Count) : string.Empty;
            count.style.display = marker.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            row.Q<Label>("time").text = OffsetOf(recorder, marker.Sequence);
        }

        #endregion


        #region Tools and Utilities

        private void Rebuild(Recorder recorder)
        {
            _builtFor = recorder;
            _builtVersion = recorder?.Markers.Version ?? -1;
            _rows.Clear();
            if (recorder != null)
            {
                var markers = recorder.Markers;
                for (var i = 0; i < markers.Count; i++)
                {
                    ref readonly var marker = ref markers[i];
                    if (_filter.Accepts(marker.Kind) && Matches(marker))
                        _rows.Add(i);
                }
            }

            foreach (var kind in _chips.Keys)
                UpdateChip(kind);
            _title.text = recorder == null ? "Events" : $"Events  {Format.Integer(recorder.Markers.Count)}";
            _empty.style.display = _rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _list.style.display = _rows.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _list.RefreshItems();
            if (recorder == null)
                _details.style.display = DisplayStyle.None;
        }

        private bool Matches(in Marker marker)
        {
            return _search.Length == 0 || marker.Message.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Toggle(MarkerKind kind)
        {
            switch (kind)
            {
                case MarkerKind.Warning: _filter.Warnings = !_filter.Warnings; break;
                case MarkerKind.Log: _filter.Logs = !_filter.Logs; break;
                case MarkerKind.Bookmark: _filter.Bookmarks = !_filter.Bookmarks; break;
                case MarkerKind.Spawn: _filter.Lifecycle = !_filter.Lifecycle; break;
                default: _filter.Errors = !_filter.Errors; break;
            }
            Rebuild(HindsightSession.Recorder);
            FilterChanged?.Invoke();
        }

        private void UpdateChip(MarkerKind kind)
        {
            var (chip, count) = _chips[kind];
            chip.EnableInClassList("hs-chip--off", !_filter.Accepts(kind));
            var markers = HindsightSession.Recorder?.Markers;
            var total = markers == null ? 0
                : kind == MarkerKind.Error
                    ? markers.CountOf(MarkerKind.Error) + markers.CountOf(MarkerKind.Exception) + markers.CountOf(MarkerKind.Assert)
                : kind == MarkerKind.Spawn
                    ? markers.CountOf(MarkerKind.Spawn) + markers.CountOf(MarkerKind.Destroy) + markers.CountOf(MarkerKind.SceneLoad)
                    : markers.CountOf(kind);
            count.text = Format.Integer(total);
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            var recorder = HindsightSession.Recorder;
            var index = _list.selectedIndex;
            if (recorder == null || index < 0 || index >= _rows.Count)
            {
                _details.style.display = DisplayStyle.None;
                return;
            }

            var markerIndex = _rows[index];
            ShowDetails(recorder.Markers[markerIndex]);
            MarkerPicked?.Invoke(markerIndex);
        }

        private void ShowDetails(in Marker marker)
        {
            _details.style.display = DisplayStyle.Flex;
            _detailMessage.text = marker.Message;
            _detailLinks.Clear();
            var links = 0;
            foreach (Match match in SourceLine.Matches(marker.StackTrace ?? string.Empty))
            {
                if (links++ >= MaxSourceLinks)
                    break;
                var path = match.Groups["path"].Value.Replace('\\', '/');
                var line = int.Parse(match.Groups["line"].Value, Format.Culture);
                var link = new Button(() => OpenSource(path, line)) { text = $"{System.IO.Path.GetFileName(path)}:{line}", tooltip = path };
                link.AddToClassList("hs-link");
                _detailLinks.Add(link);
            }
        }

        private static void OpenSource(string path, int line)
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null)
                AssetDatabase.OpenAsset(script, line);
            else
                Debug.LogWarning($"{HindsightSession.LogPrefix} Cannot open {path}: it is not an asset of this project.");
        }

        private static string OffsetOf(Recorder recorder, int sequence)
        {
            var history = recorder.History;
            if (!history.Contains(sequence) || history.IsEmpty)
                return "now";
            return Format.Offset(history.Frame(sequence).RealTime - history.Frame(history.NewestSequence).RealTime);
        }

        #endregion
    }
}
