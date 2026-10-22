using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>Scene view overlay: recording status, how far back the scene is, and frame controls.</summary>
    [Overlay(typeof(SceneView), Id, "Hindsight", true)]
    [Icon(IconPath)]
    internal sealed class HindsightOverlay : Overlay
    {
        #region Public

        public const string Id = "hindsight-overlay";

        #endregion


        #region Private and Protected

        private const string IconPath = "Packages/com.nekuzaky.hindsight/Editor/UI/hindsight-window-dark.png";

        private VisualElement _dot;
        private Label _status;
        private Label _offset;
        private VisualElement _controls;
        private IVisualElementScheduledItem _refresh;

        #endregion


        #region Overlay

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.styleSheets.Add(Glyphs.StyleSheet);
            root.AddToClassList("hs-overlay");

            var line = new VisualElement();
            line.AddToClassList("hs-overlay__line");
            _dot = new VisualElement();
            _dot.AddToClassList("hs-overlay__dot");
            _status = new Label();
            _status.AddToClassList("hs-overlay__status");
            _offset = new Label();
            _offset.AddToClassList("hs-overlay__offset");
            line.Add(_dot);
            line.Add(_status);
            line.Add(_offset);
            root.Add(line);

            _controls = new VisualElement();
            _controls.AddToClassList("hs-overlay__controls");
            _controls.Add(Button(Glyphs.PreviousEvent, "Previous event", () => Navigation.PreviousEvent(MarkerFilter.All)));
            _controls.Add(Button(Glyphs.StepBack, "One frame back", Navigation.StepBack));
            _controls.Add(Button(Glyphs.StepForward, "One frame forward", Navigation.StepForward));
            _controls.Add(Button(Glyphs.NextEvent, "Next event", () => Navigation.NextEvent(MarkerFilter.All)));
            _controls.Add(Button(Glyphs.Live, "Back to live", Navigation.Live));
            _controls.Add(Button(Glyphs.Logo, "Open the Hindsight window", () => HindsightWindow.Open()));
            root.Add(_controls);

            _refresh = root.schedule.Execute(Refresh).Every(150);
            Refresh();
            return root;
        }

        public override void OnCreated()
        {
            HindsightSettings.Changed += SyncVisibility;
            SyncVisibility();
        }

        public override void OnWillBeDestroyed()
        {
            HindsightSettings.Changed -= SyncVisibility;
            _refresh?.Pause();
        }

        #endregion


        #region Tools and Utilities

        private static Button Button(string glyph, string tooltip, System.Action action)
        {
            var button = new Button(action) { tooltip = tooltip };
            button.AddToClassList("hs-icon-button");
            button.AddToClassList("hs-overlay__button");
            button.Add(Glyphs.Icon(glyph));
            return button;
        }

        private void SyncVisibility()
        {
            displayed = HindsightSettings.instance.ShowSceneOverlay;
        }

        private void Refresh()
        {
            if (_status == null)
                return;

            var status = HindsightSession.Status;
            var recorder = HindsightSession.Recorder;
            _status.text = status switch
            {
                SessionStatus.Recording => recorder != null ? $"Recording · {Format.Integer(recorder.AliveCount)} objects" : "Recording",
                SessionStatus.Paused => "Paused · drag the timeline back",
                SessionStatus.Rewinding => "Rewound",
                SessionStatus.Off => "Recording off",
                _ => "Ready · press Play"
            };

            _dot.EnableInClassList("hs-overlay__dot--recording", status == SessionStatus.Recording);
            _dot.EnableInClassList("hs-overlay__dot--rewinding", status == SessionStatus.Rewinding);
            _dot.EnableInClassList("hs-overlay__dot--paused", status == SessionStatus.Paused);

            _offset.text = string.Empty;
            if (status == SessionStatus.Rewinding && recorder != null)
            {
                var history = recorder.History;
                var sequence = HindsightSession.Rewind.Sequence;
                if (history.Contains(sequence))
                    _offset.text = Format.Offset(history.Frame(sequence).RealTime - history.Frame(history.NewestSequence).RealTime);
            }

            var canNavigate = status == SessionStatus.Paused || status == SessionStatus.Rewinding;
            _controls.style.display = canNavigate ? DisplayStyle.Flex : DisplayStyle.None;
        }

        #endregion
    }
}
