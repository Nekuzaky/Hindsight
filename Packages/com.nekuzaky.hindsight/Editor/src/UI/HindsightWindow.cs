using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nekuzaky.Hindsight.Editor
{
    internal sealed class HindsightWindow : EditorWindow
    {
        #region Public

        public const string Title = "Hindsight";
        public const string RepositoryUrl = "https://github.com/Nekuzaky/Hindsight";
        public const string DocumentationUrl = RepositoryUrl + "/blob/main/Packages/com.nekuzaky.hindsight/Documentation~/index.md";
        public const string CoffeeUrl = "https://buymeacoffee.com/nekuzaky";

        [MenuItem("Tools/Hindsight/Open Hindsight %&h", priority = 1)]
        [MenuItem("Window/Analysis/Hindsight", priority = 30)]
        public static void OpenFromMenu() => Open();

        public static HindsightWindow Open()
        {
            var window = GetWindow<HindsightWindow>();
            window.minSize = new Vector2(640f, 360f);
            window.Show();
            return window;
        }

        #endregion


        #region Private and Protected

        private const long RefreshMilliseconds = 100;

        private readonly TimeAxis _axis = new();
        private TimelineView _timeline;
        private EventsPanel _events;
        private FindBar _find;
        private InspectorPanel _inspector;
        private VisualElement _main;
        private VisualElement _emptyState;
        private Label _emptyTitle;
        private Label _emptyText;
        private Button _emptyButton;
        private Label _statusPill;
        private Label _objectsStat;
        private Label _memoryStat;
        private Label _costStat;
        private Label _offsetLabel;
        private Label _frameLabel;
        private Button _recordButton;
        private Button _saveButton;
        private Button _openButton;
        private Button _closeButton;
        private Button _pauseButton;
        private Button _liveButton;
        private Button _resumeButton;
        private VisualElement _recordIcon;
        private VisualElement _pauseIcon;
        private int _lastNewest = int.MinValue;
        private int _lastShown = int.MinValue;

        #endregion


        #region Unity Callbacks

        private void OnEnable()
        {
            titleContent = new GUIContent(Title, Glyphs.WindowIcon, "Rewind debugger");
            HindsightSession.StatusChanged += OnStatusChanged;
            HindsightSession.FrameShown += OnFrameShown;
            Selection.selectionChanged += OnSelectionChanged;
            _axis.Changed += OnAxisChanged;
        }

        private void OnDisable()
        {
            HindsightSession.StatusChanged -= OnStatusChanged;
            HindsightSession.FrameShown -= OnFrameShown;
            Selection.selectionChanged -= OnSelectionChanged;
            _axis.Changed -= OnAxisChanged;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.styleSheets.Add(Glyphs.StyleSheet);
            root.AddToClassList("hs-root");

            root.Add(BuildHeader());
            _main = new VisualElement();
            _main.AddToClassList("hs-main");
            _main.Add(BuildTransport());
            _find = new FindBar();
            _main.Add(_find);

            _timeline = new TimelineView(_axis);
            _timeline.FrameRequested += Navigation.Show;
            _timeline.MarkerClicked += ShowMarker;
            _main.Add(_timeline);

            var split = new TwoPaneSplitView(0, 420f, TwoPaneSplitViewOrientation.Horizontal);
            split.AddToClassList("hs-split");
            _events = new EventsPanel();
            _events.MarkerPicked += ShowMarker;
            _events.FilterChanged += () => _timeline.Filter = _events.Filter;
            _inspector = new InspectorPanel(_axis);
            _inspector.FrameRequested += Navigation.Show;
            _inspector.Notify += message => ShowNotification(new GUIContent(message), 4d);
            split.Add(_events);
            split.Add(_inspector);
            _main.Add(split);
            root.Add(_main);

            root.Add(BuildEmptyState());
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown);
            root.schedule.Execute(Tick).Every(RefreshMilliseconds);
            Refresh(true);
        }

        #endregion


        #region Building

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("hs-header");
            header.Add(Glyphs.Icon(Glyphs.Logo, "hs-icon--brand", "hs-icon--title"));
            var title = new Label(Title);
            title.AddToClassList("hs-title");
            header.Add(title);

            _statusPill = new Label();
            _statusPill.AddToClassList("hs-pill");
            header.Add(_statusPill);

            var stats = new VisualElement();
            stats.AddToClassList("hs-stats");
            _objectsStat = Stat(stats, Glyphs.Object, "Objects recorded now / slots used");
            _memoryStat = Stat(stats, Glyphs.Memory, "Memory holding the frames kept now");
            _costStat = Stat(stats, Glyphs.Cost, "Main-thread time Hindsight used per frame (smoothed)");
            header.Add(stats);

            var spacer = new VisualElement();
            spacer.AddToClassList("hs-spacer");
            header.Add(spacer);

            header.Add(HeaderButton(Glyphs.AddBookmark, "Add a bookmark at this frame (F9)", () => HindsightSession.AddBookmark()));
            _saveButton = HeaderButton(Glyphs.Save, "Save this recording to a file", RecordingCommands.SaveWithDialog);
            header.Add(_saveButton);
            _openButton = HeaderButton(Glyphs.OpenFile, "Open a saved recording (outside Play Mode)", RecordingCommands.OpenWithDialog);
            header.Add(_openButton);
            _closeButton = HeaderButton(Glyphs.Close, "Close the saved recording and put every object back", HindsightSession.Close);
            header.Add(_closeButton);
            _recordButton = HeaderButton(Glyphs.Record, "Record in Play Mode", ToggleRecording);
            _recordIcon = _recordButton.Q(className: "hs-icon");
            header.Add(_recordButton);
            header.Add(HeaderButton(Glyphs.Settings, "Preferences", HindsightPreferences.Open));
            header.Add(HeaderButton(Glyphs.Help, "Documentation", () => Application.OpenURL(DocumentationUrl)));
            return header;
        }

        private VisualElement BuildTransport()
        {
            var transport = new VisualElement();
            transport.AddToClassList("hs-transport");
            transport.Add(TransportButton(Glyphs.First, "Oldest recorded frame (Home)", Navigation.First));
            transport.Add(TransportButton(Glyphs.PreviousEvent, "Previous event (Shift+Left)", () => Navigation.PreviousEvent(_events.Filter)));
            transport.Add(TransportButton(Glyphs.StepBack, "One frame back (Left)", Navigation.StepBack));
            _pauseButton = TransportButton(Glyphs.Pause, "Pause or resume the game", Navigation.TogglePause);
            _pauseButton.AddToClassList("hs-transport__main");
            _pauseIcon = _pauseButton.Q(className: "hs-icon");
            transport.Add(_pauseButton);
            transport.Add(TransportButton(Glyphs.StepForward, "One frame forward (Right)", Navigation.StepForward));
            transport.Add(TransportButton(Glyphs.NextEvent, "Next event (Shift+Right)", () => Navigation.NextEvent(_events.Filter)));
            _liveButton = TransportButton(Glyphs.Live, "Back to the live scene (End)", Navigation.Live);
            _liveButton.AddToClassList("hs-transport__live");
            var liveLabel = new Label("Live");
            _liveButton.Add(liveLabel);
            transport.Add(_liveButton);
            _resumeButton = TransportButton(Glyphs.Resume, "Carry the game on from this frame: objects stay here, Rigidbodies get their velocities back, and what came after is forgotten. Scripts keep their current state.", () => HindsightSession.ResumeFromShownFrame());
            _resumeButton.AddToClassList("hs-transport__resume");
            _resumeButton.Add(new Label("Resume here"));
            transport.Add(_resumeButton);

            var readout = new VisualElement();
            readout.AddToClassList("hs-readout");
            _offsetLabel = new Label();
            _offsetLabel.AddToClassList("hs-readout__offset");
            _frameLabel = new Label();
            _frameLabel.AddToClassList("hs-readout__frame");
            readout.Add(_offsetLabel);
            readout.Add(_frameLabel);
            transport.Add(readout);

            var spacer = new VisualElement();
            spacer.AddToClassList("hs-spacer");
            transport.Add(spacer);
            transport.Add(TransportButton(Glyphs.Fit, "Show the whole recording (double-click the timeline)", _axis.Fit));
            return transport;
        }

        private VisualElement BuildEmptyState()
        {
            _emptyState = new VisualElement();
            _emptyState.AddToClassList("hs-empty");
            _emptyState.Add(Glyphs.Icon(Glyphs.Rewind, "hs-icon--brand", "hs-empty__icon"));
            _emptyTitle = new Label();
            _emptyTitle.AddToClassList("hs-empty__title");
            _emptyText = new Label();
            _emptyText.AddToClassList("hs-empty__text");
            _emptyButton = new Button();
            _emptyButton.AddToClassList("hs-button");
            _emptyButton.AddToClassList("hs-button--primary");
            _emptyState.Add(_emptyTitle);
            _emptyState.Add(_emptyText);
            _emptyState.Add(_emptyButton);
            return _emptyState;
        }

        private static Label Stat(VisualElement parent, string glyph, string tooltip)
        {
            var stat = new VisualElement { tooltip = tooltip };
            stat.AddToClassList("hs-stat");
            stat.Add(Glyphs.Icon(glyph, "hs-icon--dim", "hs-icon--small"));
            var label = new Label();
            label.AddToClassList("hs-stat__value");
            stat.Add(label);
            parent.Add(stat);
            return label;
        }

        private static Button HeaderButton(string glyph, string tooltip, System.Action action)
        {
            var button = new Button(action) { tooltip = tooltip };
            button.AddToClassList("hs-icon-button");
            button.Add(Glyphs.Icon(glyph));
            return button;
        }

        private static Button TransportButton(string glyph, string tooltip, System.Action action)
        {
            var button = HeaderButton(glyph, tooltip, action);
            button.AddToClassList("hs-transport__button");
            return button;
        }

        #endregion


        #region Refresh

        private void Tick() => Refresh(false);

        private void Refresh(bool force)
        {
            if (_main == null)
                return;

            var recorder = HindsightSession.Recorder;
            recorder?.Commit();

            UpdateStatus();
            var hasData = recorder != null && !recorder.History.IsEmpty;
            _emptyState.style.display = hasData ? DisplayStyle.None : DisplayStyle.Flex;
            _main.style.display = hasData ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasData)
            {
                UpdateEmptyState();
                return;
            }

            var history = recorder.History;
            _axis.SetBounds(history.Frame(history.OldestSequence).RealTime, history.Frame(history.NewestSequence).RealTime);
            var shown = HindsightSession.Rewind.Sequence;
            var changed = force || history.NewestSequence != _lastNewest || shown != _lastShown;
            _lastNewest = history.NewestSequence;
            _lastShown = shown;

            UpdateReadout(recorder);
            _events.Refresh();
            if (changed)
            {
                _timeline.Invalidate();
                _inspector.Refresh();
            }
        }

        private void UpdateStatus()
        {
            var status = HindsightSession.Status;
            var recorder = HindsightSession.Recorder;
            _statusPill.text = status switch
            {
                SessionStatus.Recording => "Recording",
                SessionStatus.Paused => "Paused",
                SessionStatus.Rewinding => "Rewinding",
                SessionStatus.Off => "Off",
                SessionStatus.Viewing => "Saved recording",
                _ => "Idle"
            };
            var file = HindsightSession.OpenedFile;
            _statusPill.tooltip = file ?? string.Empty;
            _statusPill.EnableInClassList("hs-pill--file", file != null);
            _saveButton.SetEnabled(RecordingCommands.CanSave());
            _openButton.SetEnabled(RecordingCommands.CanOpen());
            _closeButton.style.display = file != null ? DisplayStyle.Flex : DisplayStyle.None;
            _statusPill.EnableInClassList("hs-pill--recording", status == SessionStatus.Recording);
            _statusPill.EnableInClassList("hs-pill--paused", status == SessionStatus.Paused);
            _statusPill.EnableInClassList("hs-pill--rewinding", status == SessionStatus.Rewinding);

            var settings = HindsightSettings.instance;
            Glyphs.Set(_recordIcon, settings.RecordInPlayMode ? Glyphs.Record : Glyphs.RecordOff);
            _recordButton.EnableInClassList("hs-icon-button--recording", settings.RecordInPlayMode);
            _recordButton.tooltip = settings.RecordInPlayMode ? "Recording in Play Mode is on. Click to turn it off." : "Recording is off. Click to record in Play Mode.";
            Glyphs.Set(_pauseIcon, EditorApplication.isPaused ? Glyphs.Play : Glyphs.Pause);
            _pauseButton.SetEnabled(EditorApplication.isPlaying);
            _liveButton.EnableInClassList("hs-transport__live--active", status == SessionStatus.Rewinding);
            _resumeButton.style.display = HindsightSession.CanResumeFromShownFrame ? DisplayStyle.Flex : DisplayStyle.None;

            if (recorder == null)
            {
                _objectsStat.text = "—";
                _memoryStat.text = "—";
                _costStat.text = "—";
                return;
            }
            _objectsStat.text = $"{Format.Integer(recorder.AliveCount)} / {Format.Integer(recorder.SlotCount)}";
            if (recorder.IsFromFile)
            {
                _objectsStat.parent.tooltip = $"{Format.Integer(recorder.MatchedObjects)} objects found in the open scenes, {Format.Integer(recorder.MissingObjects)} not found (created while playing, renamed or moved since).";
                _memoryStat.text = Format.Bytes(recorder.History.UsedBytes);
                _costStat.text = "—";
                return;
            }
            _memoryStat.text = Format.Bytes(recorder.History.UsedBytes);
            _memoryStat.parent.tooltip = $"{Format.Bytes(recorder.History.UsedBytes)} hold the frames kept now, out of {Format.Bytes(recorder.SizeInBytes)} reserved when Play Mode started (memory budget in the preferences).";
            _costStat.text = Format.Milliseconds(recorder.Stats.AverageMilliseconds);
            _costStat.parent.tooltip = $"Main-thread time per captured frame. Average {Format.Milliseconds(recorder.Stats.AverageMilliseconds)}, 95th percentile {Format.Milliseconds(recorder.Stats.Percentile(0.95d))}, last {Format.Milliseconds(recorder.Stats.LastMilliseconds)}.";
        }

        private void UpdateReadout(Recorder recorder)
        {
            var history = recorder.History;
            var sequence = HindsightSession.Rewind.Sequence;
            if (!history.Contains(sequence))
                return;

            ref readonly var frame = ref history.Frame(sequence);
            var newest = history.Frame(history.NewestSequence).RealTime;
            _offsetLabel.text = Navigation.IsRewinding ? Format.Offset(frame.RealTime - newest) : "Now";
            _offsetLabel.EnableInClassList("hs-readout__offset--live", !Navigation.IsRewinding);
            _frameLabel.text = $"Frame {Format.Integer(frame.GameFrame)}   ·   {Format.Integer(history.FrameCount)} frames, {history.Duration.ToString("0.0", Format.Culture)} s kept";
        }

        private void UpdateEmptyState()
        {
            var status = HindsightSession.Status;
            _emptyButton.clickable = null;
            if (status == SessionStatus.Off || !HindsightSettings.instance.RecordInPlayMode)
            {
                _emptyTitle.text = "Recording is off";
                _emptyText.text = "Hindsight records nothing until you turn recording back on.";
                _emptyButton.text = "Turn recording on";
                _emptyButton.clickable = new Clickable(() => HindsightSettings.instance.RecordInPlayMode = true);
            }
            else if (status == SessionStatus.Idle)
            {
                _emptyTitle.text = "Press Play to start recording";
                _emptyText.text = "Hindsight records your scene while you play. When something goes wrong, pause and drag the timeline back to see every object exactly as it was. Nothing is added to your builds. To look at a recording saved earlier, open it with the folder button.";
                _emptyButton.text = "Enter Play Mode";
                _emptyButton.clickable = new Clickable(() => EditorApplication.isPlaying = true);
            }
            else
            {
                _emptyTitle.text = "Waiting for the first frame";
                _emptyText.text = "Recording starts with the next frame the game renders.";
                _emptyButton.text = "Resume";
                _emptyButton.clickable = new Clickable(() => EditorApplication.isPaused = false);
            }
        }

        #endregion


        #region Event Handlers

        private void OnStatusChanged()
        {
            _lastNewest = int.MinValue;
            Refresh(true);
        }

        private void OnFrameShown()
        {
            var recorder = HindsightSession.Recorder;
            if (recorder != null && recorder.History.Contains(HindsightSession.Rewind.Sequence))
                _axis.Reveal(recorder.History.Frame(HindsightSession.Rewind.Sequence).RealTime);
            Refresh(true);
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private void OnSelectionChanged()
        {
            if (_inspector != null)
                _inspector.Refresh();
        }

        private void OnAxisChanged()
        {
            _timeline?.Invalidate();
            _inspector?.Refresh();
        }

        private void ShowMarker(int markerIndex)
        {
            var recorder = HindsightSession.Recorder;
            if (recorder == null || markerIndex < 0 || markerIndex >= recorder.Markers.Count)
                return;
            Navigation.Show(recorder.Markers[markerIndex].Sequence);
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.F || !evt.actionKey)
                return;
            _find?.FocusField();
            evt.StopPropagation();
        }

        private static void ToggleRecording()
        {
            var settings = HindsightSettings.instance;
            settings.RecordInPlayMode = !settings.RecordInPlayMode;
        }

        #endregion
    }
}
