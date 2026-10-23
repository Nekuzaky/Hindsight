using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// "Find when": type a condition and jump to the frame where it becomes true. Enter searches forward from the
    /// displayed frame (from the start when live), Shift+Enter searches backward.
    /// </summary>
    internal sealed class FindBar : VisualElement
    {
        #region Public

        public const string Placeholder = "Find when...   y < -5     speed > 20     Health <= 0     any y < -10";

        public FindBar()
        {
            AddToClassList("hs-find");
            Add(Glyphs.Icon(Glyphs.Search, "hs-icon--dim"));

            _field = new TextField { name = "find" };
            _field.AddToClassList("hs-find__field");
            _field.textEdition.placeholder = Placeholder;
            _field.tooltip = "A condition on the selected object (x, y, z, speed, vx, vy, vz, angular or a tracked field), or 'any' followed by x, y or z to search every object. Enter: next, Shift+Enter: previous.";
            _field.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _field.RegisterCallback<FocusInEvent>(_ => ShowHint());
            Add(_field);

            Add(Button(Glyphs.StepBack, "Previous match (Shift+Enter)", () => Search(false)));
            Add(Button(Glyphs.StepForward, "Next match (Enter)", () => Search(true)));

            _result = new Label();
            _result.AddToClassList("hs-find__result");
            Add(_result);
        }

        public void FocusField() => _field.Focus();

        #endregion


        #region Private and Protected

        private readonly TextField _field;
        private readonly Label _result;

        #endregion


        #region Tools and Utilities

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                return;
            Search(!evt.shiftKey);
            evt.StopPropagation();
        }

        private void Search(bool forward)
        {
            var recorder = HindsightSession.Recorder;
            if (recorder == null || recorder.History.IsEmpty)
            {
                Show("Press Play: searching needs a recording.", false);
                return;
            }
            if (!ConditionQuery.TryParse(_field.value, out var query, out var error))
            {
                Show(error, false);
                return;
            }

            var slot = -1;
            if (!query.AnyObject && Selection.activeTransform != null)
                recorder.TryGetSlot(Selection.activeTransform, out slot);

            var history = recorder.History;
            var from = Navigation.IsRewinding ? Navigation.Displayed
                : forward ? history.OldestSequence - 1 : history.NewestSequence + 1;
            var result = MomentFinder.Find(recorder, query, slot, from, forward);
            Show(result.Message, result.Found);
            if (!result.Found)
                return;

            Navigation.Show(result.Sequence);
            if (query.AnyObject && recorder.IsAliveSlot(result.Slot))
                Selection.activeTransform = recorder.TransformOf(result.Slot);
        }

        private void ShowHint()
        {
            var recorder = HindsightSession.Recorder;
            var transform = Selection.activeTransform;
            if (recorder == null || transform == null || !recorder.TryGetSlot(transform, out var slot))
            {
                Show("Select an object to search its values, or start with 'any' to search every object.", true, hint: true);
                return;
            }
            Show($"{transform.name}: {MomentFinder.DescribeChannels(recorder, slot)}", true, hint: true);
        }

        private void Show(string message, bool success, bool hint = false)
        {
            _result.text = message;
            _result.EnableInClassList("hs-find__result--found", success && !hint);
            _result.EnableInClassList("hs-find__result--error", !success);
        }

        private static Button Button(string glyph, string tooltip, System.Action action)
        {
            var button = new Button(action) { tooltip = tooltip };
            button.AddToClassList("hs-icon-button");
            button.Add(Glyphs.Icon(glyph));
            return button;
        }

        #endregion
    }
}
